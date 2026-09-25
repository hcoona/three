"""Normal publication retains bounded readback for offline finalization."""

import base64
import copy
from dataclasses import replace
from datetime import datetime, timedelta
from pathlib import Path

import pytest
from three_workflow_delivery_v3.adapters import pypi
from three_workflow_delivery_v3.adapters.pypi import PythonHttpResponse
from three_workflow_delivery_v3.canonical import canonicalize, parse_json_strict
from three_workflow_delivery_v3.records.release import admit_release_record
from three_workflow_delivery_v3.records.release_transport import (
    ReleaseAdmissionBindings,
)
from three_workflow_delivery_v3.release.python_publication import (
    PythonPublicationResult,
    audit_python_publication_result,
    execute_python_publication,
    python_publication_result_from_document,
)
from three_workflow_delivery_v3.repository.python_provider import python_digest

from ..adapters.test_pypi import _entry, _index
from ..adapters.test_python_observation import Boundary
from ..python_fixtures import NOW, RUN_ID, TARGET, reference
from .python_fixtures import prepared_publication
from .test_python_finalizer import _finalize, _inputs

_TOKEN = "pypi-observation-test-secret"  # noqa: S105 - synthetic local token
_OK = PythonHttpResponse(200, b"accepted", "text/plain")


@pytest.fixture(autouse=True)
def deny_network(monkeypatch):
    """The real HTTPS boundary is forbidden throughout publication tests."""

    def denied(*_args, **_kwargs):
        pytest.fail("Real publication network access is forbidden")

    monkeypatch.setattr(pypi.http.client, "HTTPSConnection", denied)


def responses(marker, distributions, *, pending=(1, 1), unrelated=False):
    """Supply only external replies; production owns upload/read ordering."""
    registry = marker.absence.registry
    wheel = _entry(registry, distributions[0])
    sdist = _entry(registry, distributions[1])
    extra = copy.deepcopy(wheel)
    extra["filename"] = extra["filename"].replace(
        marker.absence.version, "9.9.9"
    )
    prior_files = [extra] if unrelated else []
    wheel_index = _index([*prior_files, wheel])
    final_index = _index([wheel, sdist])
    downloads = [
        PythonHttpResponse(200, item.content, "application/octet-stream")
        for item in distributions
    ]
    return [
        _OK,
        *([_index(prior_files)] * pending[0]),
        wheel_index,
        downloads[0],
        _OK,
        *([wheel_index] * pending[1]),
        final_index,
        *downloads,
    ]


def execute_case(tmp_path, *, pending=(1, 1), unrelated=False):
    """Use complete typed authorization with finite HTTP and virtual time."""
    marker, marker_ref, payloads, distributions = prepared_publication()
    boundary = Boundary(
        *responses(marker, distributions, pending=pending, unrelated=unrelated)
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
    return marker, marker_ref, distributions, boundary, result


def finalize_document(document, marker, marker_ref):
    """Use strict Result parsing followed by the actual shared Finalizer."""
    changed = python_publication_result_from_document(document)
    terminal_ref = reference(changed.to_document(), 508)
    return _finalize(
        replace(
            _inputs(marker),
            terminal=(changed, terminal_ref),
            result_marker=(marker, marker_ref),
        )
    )


def execute_failed_download_case(tmp_path, failure, operation):
    """Execute a real download failure with later responses left queued."""
    marker, marker_ref, payloads, distributions = prepared_publication()
    replies = responses(marker, distributions, pending=(0, 0))
    replacement = {
        "http": PythonHttpResponse(503, b"unavailable", "text/plain"),
        "hash": PythonHttpResponse(
            200, b"changed original bytes", "application/octet-stream"
        ),
        "transport": TimeoutError("synthetic download timeout"),
    }[failure]
    replies[2 if operation == 0 else 6] = replacement
    boundary = Boundary(*replies, _OK)
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
    return marker, marker_ref, boundary, result


@pytest.mark.parametrize("failure", ["http", "hash", "transport"])
@pytest.mark.parametrize("operation", [0, 1])
def test_normal_actual_failed_download_finalizes_without_later_effects(
    tmp_path, failure, operation
):
    """Real download failures remain auditable publication failures."""
    marker, marker_ref, boundary, result = execute_failed_download_case(
        tmp_path, failure, operation
    )
    assert result.result == "failed"
    assert result.mutation_classification == "mutated"
    assert result.operations[operation].readback_exact is False
    assert result.operations[operation].readback_digest is None
    assert [call[0] for call in boundary.calls] == (
        ["POST", "GET", "GET"]
        if operation == 0
        else ["POST", "GET", "GET", "POST", "GET", "GET", "GET"]
    )
    assert len(boundary.responses) == (5 if operation == 0 else 1)
    if operation == 0:
        assert result.operations[1].status == "not-attempted"
    else:
        assert result.operations[0].readback_exact is True
    downloads = result.operations[operation].observation["downloads"]
    assert len(downloads) == operation + 1
    if failure == "transport":
        assert downloads[-1]["response"] is None
    else:
        assert base64.b64decode(downloads[-1]["response"]["body"]) == (
            b"unavailable" if failure == "http" else b"changed original bytes"
        )
    document = parse_json_strict(canonicalize(result.to_document()))
    outcome = finalize_document(document, marker, marker_ref)
    assert outcome.disposition == "publication-failed"
    assert outcome.possibly_mutated is True
    assert outcome.direct_predecessor.reference == reference(document, 508)


@pytest.mark.parametrize(
    ("change", "error"),
    [
        ("before-phase", "download timing"),
        ("reversed-response", "download timing"),
        ("overlong-response", "download timing"),
        ("expired-authority", "download timing"),
        ("wrong-url", "download URL"),
        ("missing-history", r"download.*(?:missing|inventory)"),
        ("extra-history", "unconsumed downloads"),
    ],
)
def test_normal_failed_flags_cannot_hide_invalid_download_history(
    tmp_path, change, error
):
    """Failure flags cannot excuse impossible or incomplete effect history."""
    marker, marker_ref, _, result = execute_failed_download_case(
        tmp_path, "http", 0
    )
    document = parse_json_strict(canonicalize(result.to_document()))
    operation = document["operations"][0]
    evidence = operation["observation"]
    downloads = evidence["downloads"]
    entry = downloads[0]
    if change == "before-phase":
        entry["start"] = evidence["phase"]["stopped-at"] - 1
    elif change == "reversed-response":
        entry["finish"] = entry["start"] - 1
    elif change == "overlong-response":
        entry["finish"] = entry["start"] + 30.001
    elif change == "expired-authority":
        entry["start"] = evidence["authority"]["deadline"]
        entry["finish"] = entry["start"]
    elif change == "wrong-url":
        entry["url"] = "https://foreign.invalid/packages/file.whl"
    elif change == "missing-history":
        downloads.clear()
    else:
        downloads.append(copy.deepcopy(entry))
    assert document["result"] == "failed"
    assert operation["readback-exact"] is False
    assert operation["readback-digest"] is None
    with pytest.raises(ValueError, match=error):
        finalize_document(document, marker, marker_ref)


def test_normal_delayed_result_crosses_shared_transport_and_finalizer(
    tmp_path, monkeypatch
):
    """Both sixth reads can prove published through immutable Result replay."""
    marker, marker_ref, distributions, boundary, result = execute_case(
        tmp_path, pending=(5, 5)
    )
    assert result.result == "published"
    assert [call[0] for call in boundary.calls] == [
        "POST",
        *(["GET"] * 7),
        "POST",
        *(["GET"] * 8),
    ]
    assert distributions[0].content in boundary.calls[0][3]
    assert distributions[1].content in boundary.calls[8][3]
    assert [entry[1] for entry in boundary.events if entry[0] == "wait"] == [
        10
    ] * 10
    for operation in result.operations:
        assert [
            read["classification"]
            for read in operation.observation["phase"]["reads"]
        ] == [*(["pending"] * 5), "exact"]
        assert len(operation.observation["downloads"]) == operation.ordinal + 1
    admitted = admit_release_record(
        canonicalize(result.to_document()),
        expected_type=PythonPublicationResult,
        expected_digest=result.result_digest,
        expected_bindings=ReleaseAdmissionBindings(
            "live-release", RUN_ID, None, TARGET, "publish-python"
        ),
    )

    def denied(*_args, **_kwargs):
        pytest.fail("Offline finalization attempted a destination or wait")

    monkeypatch.setattr(boundary, "request", denied)
    monkeypatch.setattr("time.sleep", denied)
    terminal_ref = reference(admitted.to_document(), 508)
    outcome = _finalize(
        replace(
            _inputs(marker),
            terminal=(admitted, terminal_ref),
            result_marker=(marker, marker_ref),
        )
    )
    assert outcome.disposition == "published"
    assert outcome.direct_predecessor.reference == terminal_ref
    assert outcome.possibly_mutated is False


@pytest.mark.parametrize("phase", [0, 1])
def test_normal_pending_exhaustion_cannot_borrow_allowance_or_retry(
    tmp_path, phase
):
    """Earlier unused reads cannot supply a seventh read to another phase."""
    marker, _, _, boundary, result = execute_case(
        tmp_path, pending=(6, 0) if phase == 0 else (0, 6)
    )
    assert result.result == "failed"
    assert result.mutation_classification == "mutated"
    assert sum(call[0] == "POST" for call in boundary.calls) == phase + 1
    assert (
        result.operations[phase].observation["phase"]["terminal"] == "exhausted"
    )
    assert result.operations[phase].observation["downloads"] == []
    assert len(boundary.responses) == (6 if phase == 0 else 3)
    if phase == 0:
        assert result.operations[1].status == "not-attempted"
    audit_python_publication_result(result, marker)


def test_normal_existing_project_404_is_terminal_after_one_read(tmp_path):
    """The bootstrap-only 404 exception never applies to normal publication."""
    marker, marker_ref, payloads, distributions = prepared_publication()
    boundary = Boundary(
        _OK,
        PythonHttpResponse(404, b"disappeared", "text/plain"),
        *responses(marker, distributions),
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
    assert [call[0] for call in boundary.calls] == ["POST", "GET"]
    assert result.result == "failed"
    assert result.operations[1].status == "not-attempted"
    assert not any(event[0] == "wait" for event in boundary.events)
    assert result.operations[0].observation["phase"]["terminal"] == "failed"
    audit_python_publication_result(result, marker)


def test_normal_scope_ignores_other_version_without_downloading_it(tmp_path):
    """Unrelated versions may change while the approved version stays exact."""
    marker, _, distributions, boundary, result = execute_case(
        tmp_path, unrelated=True
    )
    assert result.result == "published"
    file_urls = [call[1] for call in boundary.calls if "/packages/" in call[1]]
    registry = marker.absence.registry
    assert file_urls == [
        _entry(registry, distributions[0])["url"],
        _entry(registry, distributions[0])["url"],
        _entry(registry, distributions[1])["url"],
    ]
    audit_python_publication_result(result, marker)


@pytest.mark.parametrize(
    "change",
    [
        "phase-missing",
        "pending-raw",
        "pending-flag",
        "pending-removed",
        "upload-order",
        "download-order",
        "download-body",
        "download-extra",
        "previous",
        "profile",
        "clock",
        "policy",
        "authority-map",
        "authority-switch",
    ],
)
def test_normal_finalizer_rejects_tampered_immutable_observation(  # noqa: C901, PLR0912 - independent evidence mutations
    tmp_path, change
):
    """A final digest or exact flag cannot override the original phase trace."""
    marker, marker_ref, _, _, result = execute_case(tmp_path)
    document = parse_json_strict(canonicalize(result.to_document()))
    first = document["operations"][0]["observation"]
    phase = first["phase"]
    if change == "phase-missing":
        first["phase"] = None
    elif change == "pending-raw":
        phase["reads"][0]["response"]["body"] = base64.b64encode(b"{}").decode()
        phase["reads"][0]["response"]["digest"] = python_digest(b"{}")
    elif change == "pending-flag":
        phase["reads"][0]["classification"] = "exact"
    elif change == "pending-removed":
        phase["reads"].pop(0)
    elif change == "upload-order":
        document["operations"][1]["observation"]["upload-started"] = 99
    elif change == "download-order":
        first["downloads"][0]["start"] = 99
    elif change == "download-body":
        first["downloads"][0]["response"]["body"] = base64.b64encode(
            b"foreign wheel"
        ).decode()
        first["downloads"][0]["response"]["digest"] = python_digest(
            b"foreign wheel"
        )
    elif change == "download-extra":
        first["downloads"].append(copy.deepcopy(first["downloads"][0]))
    elif change == "previous":
        phase["previous"]["body"] = base64.b64encode(b"{}").decode()
        phase["previous"]["digest"] = python_digest(b"{}")
    elif change == "profile":
        phase["profile-digest"] = "sha256:" + "f" * 64
    elif change == "clock":
        phase["clock"] = "UTC"
    elif change == "authority-map":
        first["authority"]["deadline"] += 1
    elif change == "authority-switch":
        document["operations"][1]["observation"]["authority"]["utc"] = (
            NOW.isoformat()
        )
    else:
        phase["policy"] = "foreign-policy"
    with pytest.raises((ValueError, TypeError, KeyError)):
        finalize_document(document, marker, marker_ref)


@pytest.mark.parametrize("encoding", ["plain", "base64", "basic"])
def test_normal_reflected_credentials_never_enter_result(tmp_path, encoding):
    """Unsafe index replies stop without retaining credential-bearing bytes."""
    marker, marker_ref, payloads, distributions = prepared_publication()
    secret = {
        "plain": _TOKEN.encode(),
        "base64": base64.b64encode(_TOKEN.encode()),
        "basic": base64.b64encode(f"__token__:{_TOKEN}".encode()),
    }[encoding]
    boundary = Boundary(
        _OK,
        PythonHttpResponse(200, secret, "text/plain"),
        *responses(marker, distributions),
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
    assert result.result == "failed"
    assert [call[0] for call in boundary.calls] == ["POST", "GET"]
    assert secret not in canonicalize(result.to_document())
    assert base64.b64encode(secret) not in canonicalize(result.to_document())
    assert (
        result.operations[0].observation["phase"]["reads"][0]["response"]
        is None
    )
    assert result.operations[1].status == "not-attempted"


def test_normal_marker_previous_index_is_verified_before_first_upload(tmp_path):
    """A foreign original absence response cannot be silently substituted."""
    marker, _, payloads, distributions = prepared_publication()
    marker = replace(
        marker,
        absence=replace(
            marker.absence,
            index_response=_index(
                [_entry(marker.absence.registry, distributions[0])]
            ),
        ),
    )
    marker_ref = reference(marker.to_document(), 507)
    boundary = Boundary(*responses(marker, distributions))
    with pytest.raises(ValueError, match=r"previous|absence"):
        execute_python_publication(
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
    assert boundary.calls == []
    assert not (tmp_path / "claim").exists()


def test_normal_authority_expiry_during_wait_blocks_next_effect(tmp_path):
    """Pending observation cannot extend the current governance authority."""
    marker, marker_ref, payloads, distributions = prepared_publication()
    boundary = Boundary(*responses(marker, distributions))
    now = [NOW + timedelta(seconds=3)]

    def expire(seconds):
        boundary.wait(seconds)
        now[0] = datetime.fromisoformat(
            marker.fresh_governance.document["expires-at"]
        )

    result = execute_python_publication(
        marker,
        marker_ref,
        payloads,
        token=_TOKEN,
        transport=boundary,
        claim_path=tmp_path / "claim",
        clock=lambda: now[0],
        monotonic=boundary.clock,
        wait=expire,
    )
    assert result.result == "failed"
    assert [call[0] for call in boundary.calls] == ["POST", "GET"]
    assert result.operations[1].status == "not-attempted"


def test_normal_authority_expiry_after_exact_index_blocks_file(tmp_path):
    """File admission retains authority after index observation succeeds."""
    marker, marker_ref, payloads, distributions = prepared_publication()
    boundary = Boundary(*responses(marker, distributions, pending=(0, 0)))
    expires = datetime.fromisoformat(
        marker.fresh_governance.document["expires-at"]
    )

    def utc():
        return (
            expires
            if len(boundary.calls) >= 2  # noqa: PLR2004 - POST then exact index
            else NOW + timedelta(seconds=3)
        )

    result = execute_python_publication(
        marker,
        marker_ref,
        payloads,
        token=_TOKEN,
        transport=boundary,
        claim_path=tmp_path / "claim",
        clock=utc,
        monotonic=boundary.clock,
        wait=boundary.wait,
    )
    assert [call[0] for call in boundary.calls] == ["POST", "GET"]
    assert result.result == "failed"
    assert result.operations[0].observation["phase"]["terminal"] == "exact"
    assert result.operations[0].readback_exact is False
    assert result.operations[1].status == "not-attempted"
    audit_python_publication_result(result, marker)


@pytest.mark.parametrize(
    ("retention_fault", "pending", "expected"),
    [
        (
            ("index-0.response.json", False),
            (1, 1),
            (["POST", "GET"], "failed"),
        ),
        (
            ("download-0.json", False),
            (0, 0),
            (["POST", "GET", "GET"], "exact"),
        ),
        (
            ("index-0.response.json", True),
            (1, 1),
            (["POST", "GET"], "failed"),
        ),
    ],
    ids=["pending-index", "exact-download", "pending-index-reversed-clock"],
)
def test_normal_retention_failure_preserves_raw_and_stops_effects(
    tmp_path, monkeypatch, retention_fault, pending, expected
):
    """Failed evidence persistence leaves only an unknown marker outcome."""
    failed_record, reverse_terminal_clock = retention_fault
    methods, terminal = expected
    marker, marker_ref, payloads, distributions = prepared_publication()
    marker_bytes = canonicalize(marker.to_document())
    replies = responses(marker, distributions, pending=pending)
    boundary = Boundary(*replies)
    operation_root = tmp_path / "claim-observations/operation-0"
    original_open = Path.open

    def fail_response_record(path, *args, **kwargs):
        if path == operation_root / failed_record:
            if reverse_terminal_clock:
                boundary.now -= 1
            message = "synthetic retention unavailable"
            raise OSError(message)
        return original_open(path, *args, **kwargs)

    monkeypatch.setattr(Path, "open", fail_response_record)
    with pytest.raises(OSError, match="evidence retention failed"):
        execute_python_publication(
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
    assert [call[0] for call in boundary.calls] == methods
    assert len(boundary.responses) == len(replies) - len(methods)
    assert not any(event[0] == "wait" for event in boundary.events)
    assert (tmp_path / "claim").is_file()
    assert not (tmp_path / "claim-observations/operation-1").exists()
    assert not (operation_root / failed_record).exists()
    assert (operation_root / "index-0.body").read_bytes() == replies[1].body
    admission = parse_json_strict(
        (operation_root / "admission.json").read_bytes()
    )
    assert admission["upload-started"] == boundary.calls[0][5]
    phase = parse_json_strict((operation_root / "phase.json").read_bytes())
    assert phase["terminal"] == terminal
    if reverse_terminal_clock:
        assert phase["stopped-at"] == boundary.calls[-1][5] - 1
    assert canonicalize(marker.to_document()) == marker_bytes
    outcome = _finalize(
        replace(_inputs(marker), terminal=(marker, marker_ref)),
        publisher_conclusion="failure",
        publication_step_outcome="failure",
    )
    assert outcome.disposition == "unknown"
    assert outcome.possibly_mutated is True
    assert outcome.direct_predecessor.kind == "mutation-marker"
    assert outcome.direct_predecessor.reference == marker_ref


def test_normal_finalizer_rejects_aggregate_readback_downgrade(tmp_path):
    """A failed aggregate cannot contradict two successful exact operations."""
    marker, marker_ref, _, _, result = execute_case(tmp_path)
    assert result.result == "published"
    document = parse_json_strict(canonicalize(result.to_document()))
    document["final-readback-exact"] = False
    document["final-readback-digest"] = None
    document["result"] = "failed"
    for operation in document["operations"]:
        assert operation["status"] == "succeeded"
        assert operation["readback-exact"] is True
        assert operation["readback-digest"] is not None
    with pytest.raises(ValueError, match=r"final readback.*operation evidence"):
        finalize_document(document, marker, marker_ref)
