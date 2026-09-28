"""A fresh qualified Attempt completes only the missing exact-set member."""

from copy import deepcopy
from dataclasses import replace
from datetime import timedelta

import pytest
from three_workflow_delivery_v3.adapters import pypi
from three_workflow_delivery_v3.adapters.pypi import PythonHttpResponse
from three_workflow_delivery_v3.canonical import canonical_sha256, canonicalize
from three_workflow_delivery_v3.records.release import admit_release_record
from three_workflow_delivery_v3.records.release_transport import (
    ReleaseAdmissionBindings,
)
from three_workflow_delivery_v3.release.python_publication import (
    PythonPublicationResult,
    audit_python_publication_result,
    execute_python_publication,
    python_publication_result_from_document,
    render_python_approval_summary,
)
from three_workflow_delivery_v3.release.python_transport import (
    python_marker_from_document,
    python_native_observation_from_document,
)
from three_workflow_delivery_v3.repository.python_provider import python_digest

from ..adapters.test_pypi import _entry, _index
from ..adapters.test_python_observation import Boundary
from ..python_fixtures import NOW, reference
from .python_fixtures import (
    native_observation,
    prepared_publication,
    publication_snapshot,
)
from .test_python_finalizer import _finalize, _inputs
from .test_python_publication import _readback

_TOKEN = "pypi-recovery-test-only"  # noqa: S105 - controlled transport only
_OK = PythonHttpResponse(200, b"accepted", "text/plain")


@pytest.fixture(autouse=True)
def deny_network(monkeypatch):
    """Neither modeled recovery nor offline admission may contact a host."""

    def denied(*_args, **_kwargs):
        pytest.fail("Real publication network access is forbidden")

    monkeypatch.setattr(pypi.http.client, "HTTPSConnection", denied)


def execute_recovery(tmp_path, retained, replies=None, *, qualified=None):
    """Exercise the actual executor with controlled registry replies."""
    marker, marker_ref, payloads, originals = prepared_publication(
        retained=(retained,), qualified=qualified
    )
    boundary = Boundary(
        *(
            replies
            if replies is not None
            else (_OK, *_readback(marker.pre_state.registry, originals))
        )
    )
    result = execute_python_publication(
        marker,
        marker_ref,
        payloads,
        token=_TOKEN,
        transport=boundary,
        claim_path=tmp_path / "claim",
        clock=lambda: NOW + timedelta(seconds=3),
        monotonic=boundary.clock,
        wait=boundary.wait,
    )
    return marker, marker_ref, originals, boundary, result


def finalize_recovery(result, marker, marker_ref):
    """Bind finalization to this concrete Attempt, including native fixtures."""
    return _finalize(
        replace(
            _inputs(marker),
            terminal=(result, reference(result.to_document(), 508)),
            result_marker=(marker, marker_ref),
        ),
        current=ReleaseAdmissionBindings(
            "live-release",
            marker.attempt.workflow_run_id,
            None,
            marker.attempt.execution.target,
        ),
    )


@pytest.mark.parametrize("retained", ["wheel", "sdist"])
def test_python_partial_publication_uploads_only_missing_file(
    tmp_path, retained
):
    """Both subsets close via one actual POST and the last-upload readback."""
    marker, marker_ref, originals, boundary, result = execute_recovery(
        tmp_path, retained
    )
    missing = 1 if retained == "wheel" else 0
    kept = 1 - missing
    assert [call[0] for call in boundary.calls] == ["POST", "GET", "GET", "GET"]
    assert originals[missing].content in boundary.calls[0][3]
    assert originals[kept].content not in boundary.calls[0][3]
    assert boundary.responses == []
    assert result.result == "published"
    assert result.mutation_classification == "mutated"
    assert result.operations[kept].status == "already-present"
    assert result.operations[kept].readback_exact is True
    assert result.operations[kept].readback_digest == canonical_sha256(
        marker.pre_state.to_document()
    )
    assert result.operations[kept].response_digest is None
    assert result.operations[kept].observation is None
    assert result.operations[missing].status == "succeeded"
    assert len(result.operations[missing].observation["downloads"]) == len(
        originals
    )
    assert result.final_readback_exact is True
    assert (
        result.final_readback_digest
        == result.operations[missing].readback_digest
    )
    admitted = admit_release_record(
        canonicalize(result.to_document()),
        expected_type=PythonPublicationResult,
        expected_digest=result.result_digest,
        expected_bindings=ReleaseAdmissionBindings(
            "live-release",
            marker.attempt.workflow_run_id,
            None,
            marker.attempt.execution.target,
            "publish-python",
        ),
    )
    assert admitted.to_document() == result.to_document()
    audit_python_publication_result(admitted, marker)
    outcome = finalize_recovery(admitted, marker, marker_ref)
    assert outcome.disposition == "published"
    assert outcome.possibly_mutated is False
    assert outcome.attempt == marker.attempt
    summary = render_python_approval_summary(
        marker.authorization.bundle.snapshot
    ).decode()
    assert "already-present" in summary
    assert "upload" in summary
    for original in originals:
        assert original.filename in summary
        assert original.digest in summary


@pytest.mark.parametrize("retained", ["wheel", "sdist"])
@pytest.mark.parametrize("drift", ["absent", "complete", "swapped", "url"])
def test_python_recovery_marker_rejects_approved_subset_or_inventory_drift(
    retained, drift
):
    """Fresh pre-state cannot change which file was approved for upload."""
    marker, _, _, originals = prepared_publication(retained=(retained,))
    decision = marker.authorization.bundle.snapshot.observation.decision
    if drift == "url":
        entries = [
            _entry(marker.pre_state.registry, item)
            for item in marker.pre_state.files
        ]
        entries[0]["url"] += "?changed=1"
        response = _index(entries)
        native = replace(
            marker.pre_state,
            index_response=response,
            index_digest=python_digest(response.body),
        )
    else:
        files = (
            ()
            if drift == "absent"
            else originals
            if drift == "complete"
            else tuple(d for d in originals if d.variant != retained)
        )
        native = native_observation(
            decision,
            files,
            "absent"
            if not files
            else "complete"
            if len(files) == len(originals)
            else "partial",
        )
    with pytest.raises(ValueError, match="Python"):
        replace(marker, pre_state=native)


@pytest.mark.parametrize("retained", ["wheel", "sdist"])
@pytest.mark.parametrize(
    "change", ["bytes", "witness", "extra", "yanked", "unknown"]
)
def test_python_recovery_blocks_unverified_or_conflicting_existing_file(
    retained, change
):
    """Presence alone never admits a retained-file disposition."""
    marker, _, _, originals = prepared_publication(retained=(retained,))
    decision = marker.authorization.bundle.snapshot.observation.decision
    file = next(d for d in originals if d.variant == retained)
    if change == "bytes":
        native = native_observation(
            decision, (replace(file, content=b"foreign bytes"),), "partial"
        )
    elif change == "witness":
        native = native_observation(
            decision,
            (
                replace(
                    file,
                    witness=replace(
                        file.witness, control_digest="sha256:" + "f" * 64
                    ),
                ),
            ),
            "partial",
        )
    elif change == "unknown":
        native = replace(marker.pre_state, classification="unknown")
    else:
        entries = [_entry(marker.pre_state.registry, file)]
        if change == "extra":
            entries.append(
                {
                    **entries[0],
                    "filename": (
                        "hcoona_release_smoke_python-0.1.0b7-"
                        "cp314-cp314-linux_x86_64.whl"
                    ),
                }
            )
        else:
            entries[0]["yanked"] = True
        response = _index(entries)
        native = replace(
            marker.pre_state,
            index_response=response,
            index_digest=python_digest(response.body),
        )
    if change in {"extra", "yanked"}:
        with pytest.raises(ValueError, match="Python"):
            python_native_observation_from_document(
                native.to_document(), originals
            )
    else:
        with pytest.raises(ValueError, match="Python"):
            publication_snapshot(decision, native)


@pytest.mark.parametrize("retained", ["wheel", "sdist"])
@pytest.mark.parametrize(
    "failure",
    ["rejected", "ambiguous", "partial", "lost-retained", "corrupt-retained"],
)
def test_python_partial_recovery_failure_stops_without_retry(
    tmp_path, retained, failure
):
    """A failed missing upload or broken retained file never becomes success."""
    marker, _, _, originals = prepared_publication(retained=(retained,))
    registry = marker.pre_state.registry
    if failure == "rejected":
        replies = [PythonHttpResponse(400, b"duplicate", "text/plain")]
    elif failure == "ambiguous":
        replies = [TimeoutError("lost upload response")]
    elif failure == "partial":
        pending = _index([_entry(registry, d) for d in marker.pre_state.files])
        replies = [_OK, *([pending] * 6)]
    elif failure == "lost-retained":
        replies = [
            _OK,
            _index(
                [
                    _entry(registry, d)
                    for d in originals
                    if d.variant != retained
                ]
            ),
        ]
    else:
        replies = [_OK, *_readback(registry, originals)]
        replies[2 + (0 if retained == "wheel" else 1)] = PythonHttpResponse(
            200, b"corrupt retained content", "application/octet-stream"
        )
    replies.append(_OK)
    marker, marker_ref, _, boundary, result = execute_recovery(
        tmp_path, retained, replies
    )
    assert result.result == "failed"
    assert result.final_readback_exact is False
    assert result.final_readback_digest is None
    assert sum(call[0] == "POST" for call in boundary.calls) == 1
    assert boundary.responses[-1] == _OK
    assert (
        result.operations[0 if retained == "wheel" else 1].status
        == "already-present"
    )
    audit_python_publication_result(result, marker)
    outcome = finalize_recovery(result, marker, marker_ref)
    assert outcome.disposition == "publication-failed"
    assert outcome.possibly_mutated is True


@pytest.mark.parametrize("retained", ["wheel", "sdist"])
@pytest.mark.parametrize(
    "change",
    [
        "response",
        "observation",
        "digest",
        "missing-digest",
        "retained-as-upload",
        "not-exact",
        "upload-as-retained",
        "marker",
        "attempt",
        "phase-ordinal",
        "file-order",
    ],
)
def test_python_recovery_result_rejects_forged_retained_evidence(  # noqa: C901 - independent wire attacks
    tmp_path, retained, change
):
    """Retained status and final exactness cannot replace bound evidence."""
    marker, marker_ref, _, _, result = execute_recovery(tmp_path, retained)
    document = deepcopy(result.to_document())
    kept = 0 if retained == "wheel" else 1
    missing = 1 - kept
    operation = document["operations"][kept]
    if change == "response":
        operation["response-digest"] = "sha256:" + "f" * 64
    elif change == "observation":
        operation["observation"] = document["operations"][missing][
            "observation"
        ]
    elif change == "digest":
        operation["readback-digest"] = "sha256:" + "f" * 64
    elif change == "missing-digest":
        operation["readback-digest"] = None
    elif change == "retained-as-upload":
        document["operations"][kept] = {
            **deepcopy(document["operations"][missing]),
            "ordinal": kept,
        }
    elif change == "not-exact":
        operation["readback-exact"] = False
    elif change == "upload-as-retained":
        document["operations"][missing] = {**operation, "ordinal": missing}
        document["final-readback-exact"] = False
        document["final-readback-digest"] = None
        document["result"] = "failed"
        document["mutation-classification"] = "not-mutated"
    elif change == "marker":
        document["mutation-marker-reference"]["payload-digest"] = (
            "sha256:" + "f" * 64
        )
    elif change == "attempt":
        document["attempt"]["workflow-run-id"] += 1
    elif change == "phase-ordinal":
        document["operations"][missing]["observation"]["phase"]["phase"] = (
            f"normal-{kept}"
        )
    else:
        downloads = document["operations"][missing]["observation"]["downloads"]
        downloads.reverse()
    with pytest.raises((ValueError, TypeError)):
        replay_recovery_result(document, marker, marker_ref)


def replay_recovery_result(document, marker, marker_ref):
    """Admit wire, replay evidence and finalize without external effects."""
    forged = python_publication_result_from_document(document)
    audit_python_publication_result(forged, marker)
    return finalize_recovery(forged, marker, marker_ref)


def test_python_recovery_marker_rejects_legacy_absence_wire():
    """An older absence-only marker is not coerced into new recovery proof."""
    marker, _, _, _ = prepared_publication(retained=("wheel",))
    document = marker.to_document()
    document["absence"] = document.pop("pre-state")
    with pytest.raises((ValueError, TypeError)):
        python_marker_from_document(
            document, marker.authorization, marker.authorization_reference
        )


@pytest.mark.parametrize("retained", ["wheel", "sdist"])
def test_python_recovery_delayed_missing_file_preserves_retained_inventory(
    tmp_path, retained
):
    """Visibility waiting retains the original subset and never resends."""
    marker, _, _, originals = prepared_publication(retained=(retained,))
    replies = (
        _OK,
        marker.pre_state.index_response,
        marker.pre_state.index_response,
        *_readback(marker.pre_state.registry, originals),
    )
    marker, marker_ref, _, boundary, result = execute_recovery(
        tmp_path, retained, replies
    )
    upload = result.operations[1 if retained == "wheel" else 0]
    assert result.result == "published"
    assert [
        r["classification"] for r in upload.observation["phase"]["reads"]
    ] == ["pending", "pending", "exact"]
    assert [event[1] for event in boundary.events if event[0] == "wait"] == [
        10,
        10,
    ]
    assert [call[0] for call in boundary.calls] == [
        "POST",
        "GET",
        "GET",
        "GET",
        "GET",
        "GET",
    ]
    assert boundary.responses == []
    audit_python_publication_result(result, marker)
    assert (
        finalize_recovery(result, marker, marker_ref).disposition == "published"
    )
