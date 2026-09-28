"""Temporary TestPyPI seed control preserves ordinary failure evidence."""

from copy import deepcopy
from dataclasses import replace
from datetime import timedelta

import pytest
from three_workflow_delivery_v3.adapters import pypi
from three_workflow_delivery_v3.adapters.pypi import PythonHttpResponse
from three_workflow_delivery_v3.canonical import canonical_sha256, canonicalize
from three_workflow_delivery_v3.release.python_publication import (
    audit_python_publication_result,
    execute_python_publication,
    python_publication_result_from_document,
    render_python_approval_summary,
)
from three_workflow_delivery_v3.release.python_transport import (
    admit_python_publication_snapshot,
    python_approval_bundle_from_document,
)

from ..adapters.test_python_observation import Boundary
from ..python_fixtures import NOW, RUN_ID, qualification
from .python_fixtures import (
    native_observation,
    prepared_publication,
    publication_snapshot,
)
from .test_python_partial_recovery import (
    _OK,
    _TOKEN,
    execute_recovery,
    finalize_recovery,
)
from .test_python_publication import _readback


@pytest.fixture(autouse=True)
def deny_native_registry_network(monkeypatch):
    """Proof scenarios may exercise only their supplied transport boundary."""

    def denied(*_args, **_kwargs):
        pytest.fail("Real proof registry access is forbidden")

    monkeypatch.setattr(pypi.http.client, "HTTPSConnection", denied)


def execute_seed(tmp_path, replies=None):
    """Execute only the reviewed seed plan against finite external replies."""
    marker, marker_ref, payloads, originals = prepared_publication(
        proof_mode="stop-after-wheel"
    )
    boundary = Boundary(
        *(
            replies
            if replies is not None
            else (
                _OK,
                *_readback(marker.pre_state.registry, originals[:1]),
                _OK,
            )
        )
    )
    result = execute_python_publication(
        marker,
        marker_ref,
        payloads,
        token=_TOKEN,
        transport=boundary,
        claim_path=tmp_path / "seed-claim",
        clock=lambda: NOW + timedelta(seconds=3),
        monotonic=boundary.clock,
        wait=boundary.wait,
    )
    return marker, marker_ref, originals, boundary, result


def test_python_proof_seed_then_fresh_normal_recovery(tmp_path):
    """A truthful wheel-only failed Attempt precedes fresh normal completion."""
    marker, marker_ref, originals, boundary, result = execute_seed(tmp_path)
    assert [call[0] for call in boundary.calls] == ["POST", "GET", "GET"]
    assert originals[0].content in boundary.calls[0][3]
    assert originals[1].content not in boundary.calls[0][3]
    assert boundary.responses == [_OK]
    assert [entry.status for entry in result.operations] == [
        "succeeded",
        "not-attempted",
    ]
    assert result.operations[0].readback_exact is True
    assert result.operations[0].response_digest is not None
    assert result.operations[0].observation["phase"]["terminal"] == "exact"
    assert result.operations[1].response_digest is None
    assert result.operations[1].observation is None
    assert result.final_readback_exact is False
    assert result.final_readback_digest is None
    assert result.result == "failed"
    assert result.mutation_classification == "mutated"
    admitted = python_publication_result_from_document(result.to_document())
    audit_python_publication_result(admitted, marker)
    outcome = finalize_recovery(admitted, marker, marker_ref)
    assert outcome.disposition == "publication-failed"
    assert outcome.possibly_mutated is True
    assert (
        outcome.direct_predecessor.reference.payload_digest
        == result.result_digest
    )
    before = (
        canonicalize(result.to_document()),
        canonicalize(outcome.to_document()),
    )
    new = qualification(run_id=RUN_ID + 1)
    other, other_ref, pair, transport, recovered = execute_recovery(
        tmp_path, "wheel", qualified=new
    )
    assert other.authorization.bundle.snapshot.proof_mode == "none"
    assert other.attempt != marker.attempt
    assert pair[0].witness == originals[0].witness
    assert [call[0] for call in transport.calls] == [
        "POST",
        "GET",
        "GET",
        "GET",
    ]
    assert pair[1].content in transport.calls[0][3]
    assert pair[0].content not in transport.calls[0][3]
    audit_python_publication_result(recovered, other)
    assert (
        finalize_recovery(recovered, other, other_ref).disposition
        == "published"
    )
    assert before == (
        canonicalize(result.to_document()),
        canonicalize(outcome.to_document()),
    )


@pytest.mark.parametrize(
    "mode", [None, True, 1, [], {}, "", "stop", "STOP-AFTER-WHEEL"]
)
def test_python_proof_snapshot_rejects_unknown_or_non_string_mode(mode):
    """No truthiness coercion or unknown proof selection enters Approval."""
    decision, _, _ = qualification()
    with pytest.raises((ValueError, TypeError)):
        publication_snapshot(
            decision, native_observation(decision), proof_mode=mode
        )


@pytest.mark.parametrize(
    ("registry", "state"),
    [
        ("pypi", "absent"),
        ("testpypi", "wheel"),
        ("testpypi", "sdist"),
        ("testpypi", "complete"),
    ],
)
def test_python_proof_snapshot_rejects_non_seed_domain(registry, state):
    """Only an absent TestPyPI set can authorize intentional partial seeding."""
    decision, _, originals = qualification(registry)
    files = (
        ()
        if state == "absent"
        else originals
        if state == "complete"
        else tuple(d for d in originals if d.variant == state)
    )
    native = native_observation(
        decision,
        files,
        "absent"
        if not files
        else "complete"
        if state == "complete"
        else "partial",
    )
    with pytest.raises(ValueError, match="Python"):
        publication_snapshot(decision, native, proof_mode="stop-after-wheel")


@pytest.mark.parametrize("mode", ["none", "stop-after-wheel"])
def test_python_proof_snapshot_roundtrip_and_summary_bind_selection(mode):
    """The immutable plan and human summary agree on intentional failure."""
    marker, _, _, originals = prepared_publication(proof_mode=mode)
    bundle = marker.authorization.bundle
    snapshot = bundle.snapshot
    assert snapshot.to_document()["proof-mode"] == mode
    assert (
        admit_python_publication_snapshot(
            snapshot.to_document(),
            snapshot.observation,
            snapshot.observation_reference,
        )
        == snapshot
    )
    summary = render_python_approval_summary(snapshot).decode()
    for distribution in originals:
        assert distribution.filename in summary
        assert distribution.digest in summary
    if mode == "stop-after-wheel":
        assert (
            "Intentionally stop after successful wheel upload and exact "
            "readback, before any sdist upload. This Attempt must remain "
            "a failed partial publication."
        ) in summary
    else:
        assert "Intentionally stop" not in summary
    normal = replace(snapshot, proof_mode="none")
    proof = replace(snapshot, proof_mode="stop-after-wheel")
    assert canonical_sha256(normal.to_document()) != canonical_sha256(
        proof.to_document()
    )
    assert render_python_approval_summary(
        normal
    ) != render_python_approval_summary(proof)
    changed = bundle.to_document()
    with pytest.raises(ValueError, match="Python"):
        python_approval_bundle_from_document(
            changed,
            proof if mode == "none" else normal,
            bundle.snapshot_reference,
        )


@pytest.mark.parametrize("change", ["missing", "unknown", "type", "summary"])
def test_python_proof_transport_rejects_invalid_mode_or_summary(change):
    """Strict records cannot omit proof policy or replace its disclosure."""
    marker, _, _, _ = prepared_publication(proof_mode="stop-after-wheel")
    bundle = marker.authorization.bundle
    snapshot = bundle.snapshot
    document = deepcopy(snapshot.to_document())
    if change == "missing":
        del document["proof-mode"]
    elif change == "unknown":
        document["proof-mode"] = "other"
    elif change == "type":
        document["proof-mode"] = False
    else:
        changed = replace(
            bundle.summary_reference, payload_digest="sha256:" + "f" * 64
        )
        with pytest.raises(ValueError, match="Python"):
            replace(bundle, summary_reference=changed)
        return
    with pytest.raises((ValueError, TypeError)):
        admit_python_publication_snapshot(
            document, snapshot.observation, snapshot.observation_reference
        )


@pytest.mark.parametrize(
    "failure", ["rejected", "ambiguous", "readback", "pending"]
)
def test_python_proof_wheel_failure_preserves_ordinary_terminal_semantics(
    tmp_path, failure
):
    """The deliberate stop never invents successful upload or exact readback."""
    marker, _, _, _ = prepared_publication(proof_mode="stop-after-wheel")
    replies = {
        "rejected": [PythonHttpResponse(400, b"rejected", "text/plain")],
        "ambiguous": [TimeoutError("unknown wheel response")],
        "readback": [_OK, PythonHttpResponse(404, b"gone", "text/plain")],
        "pending": [_OK, *([marker.pre_state.index_response] * 6)],
    }[failure]
    marker, marker_ref, _, boundary, result = execute_seed(
        tmp_path, [*replies, _OK]
    )
    assert sum(call[0] == "POST" for call in boundary.calls) == 1
    assert boundary.responses == [_OK]
    assert result.operations[0].status == {
        "rejected": "failed",
        "ambiguous": "unknown",
    }.get(failure, "succeeded")
    assert result.operations[0].readback_exact is False
    assert result.operations[1].status == "not-attempted"
    assert result.result == "failed"
    assert result.final_readback_exact is False
    audit_python_publication_result(result, marker)
    assert (
        finalize_recovery(result, marker, marker_ref).disposition
        == "publication-failed"
    )


def test_python_proof_audit_rejects_sdist_upload_under_stop_plan(tmp_path):
    """A normally successful pair is impossible under the intentional stop."""
    normal, normal_ref, payloads, originals = prepared_publication()
    boundary = Boundary(
        _OK,
        *_readback(normal.pre_state.registry, originals[:1]),
        _OK,
        *_readback(normal.pre_state.registry, originals),
    )
    result = execute_python_publication(
        normal,
        normal_ref,
        payloads,
        token=_TOKEN,
        transport=boundary,
        claim_path=tmp_path / "normal",
        clock=lambda: NOW + timedelta(seconds=3),
        monotonic=boundary.clock,
        wait=boundary.wait,
    )
    stopped, stopped_ref, _, _ = prepared_publication(
        proof_mode="stop-after-wheel"
    )
    forged = replace(result, mutation_marker_reference=stopped_ref)
    with pytest.raises(ValueError, match="Python"):
        audit_python_publication_result(forged, stopped)
