"""One-set approval, ordered one-shot mutation and strict Result semantics."""

from dataclasses import replace
from datetime import timedelta

import pytest
from three_workflow_delivery_v3.adapters.pypi import PythonHttpResponse
from three_workflow_delivery_v3.canonical import (
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.release.python_publication import (
    PythonOperationResult,
    PythonPublicationResult,
    execute_python_publication,
    python_publication_result_from_document,
)

from ..adapters.test_pypi import FakeHttp, _entry, _index
from ..python_fixtures import NOW, governance, qualification, reference
from .python_fixtures import (
    native_observation,
    prepared_publication,
    publication_snapshot,
)

_DIGEST = "sha256:" + "1" * 64
_TOKEN = "pypi-synthetic-test-only"  # noqa: S105 - never sent outside tests


def _readback(registry, distributions):
    return (
        _index([_entry(registry, item) for item in distributions]),
        *(
            PythonHttpResponse(200, item.content, "application/octet-stream")
            for item in distributions
        ),
    )


@pytest.mark.parametrize("state", ["absent", "exact", "partial", "conflict"])
def test_python_publication_snapshot_forms_only_absent_or_exact_set(state):
    """A partial/conflicting version cannot authorize completion uploads."""
    decision, _, distributions = qualification()
    if state == "absent":
        observed = native_observation(decision)
    elif state == "partial":
        observed = native_observation(decision, distributions[:1], "partial")
    else:
        if state == "conflict":
            distributions = (
                replace(distributions[0], content=b"conflicting original"),
                distributions[1],
            )
        observed = native_observation(decision, distributions, "complete")
    if state in {"partial", "conflict"}:
        with pytest.raises(ValueError, match="blocks publication"):
            publication_snapshot(decision, observed)
    else:
        snapshot = publication_snapshot(decision, observed)
        assert snapshot.action_required is (state == "absent")
        action = snapshot.to_document()["action"]
        if state == "exact":
            assert action is None
        else:
            assert action["kind"] == "python-distribution-set"
            assert [
                (item["ordinal"], item["variant"])
                for item in action["operations"]
            ] == [(0, "wheel"), (1, "sdist")]


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("run-id", 999),
        ("run-attempt", 2),
        ("target", "f" * 40),
        ("environment", "workflow-delivery-v3-python-pypi"),
        ("reviewer", "other"),
        ("state", "pending"),
        ("deployment-id", True),
    ],
)
def test_python_authorization_rejects_borrowed_native_approval(field, value):
    """Only exact current-run Environment approval can authorize the set."""
    marker, _, _, _ = prepared_publication()
    proof = parse_canonical_json(marker.authorization.approval_evidence)
    proof[field] = value
    with pytest.raises(ValueError, match="native current-run approval"):
        replace(marker.authorization, approval_evidence=canonicalize(proof))


@pytest.mark.parametrize(
    "change",
    [
        "partial",
        "destination",
        "freshness",
        "before-authorization",
        "reference",
    ],
)
def test_python_marker_rejects_fresh_state_or_authority_drift(change):
    """Fresh absence and unchanged admitted authority precede uploads."""
    marker, _, _, distributions = prepared_publication()
    changes = {}
    if change == "partial":
        changes["absence"] = replace(
            marker.absence, files=distributions[:1], classification="partial"
        )
    elif change == "destination":
        changes["fresh_governance"] = governance(
            "pypi", NOW + timedelta(seconds=2)
        )
    elif change == "freshness":
        changes["fresh_governance"] = governance(observed_at=NOW)
    elif change == "before-authorization":
        changes["observed_at"] = NOW
    else:
        changes["authorization_reference"] = reference({})
    with pytest.raises(ValueError, match="Python"):
        replace(marker, **changes)


@pytest.mark.parametrize("name", ["testpypi", "pypi"])
def test_python_publication_success_requires_two_posts_and_exact_readback(
    tmp_path, name
):
    """One set result follows ordered original uploads and both exact reads."""
    marker, marker_ref, payloads, distributions = prepared_publication(name)
    registry = marker.absence.registry
    ok = PythonHttpResponse(200, b"created", "text/plain")
    transport = FakeHttp(
        ok,
        *_readback(registry, distributions[:1]),
        ok,
        *_readback(registry, distributions),
    )
    result = execute_python_publication(
        marker,
        marker_ref,
        payloads,
        token=_TOKEN,
        transport=transport,
        claim_path=tmp_path / "claim",
        clock=lambda: NOW + timedelta(seconds=3),
    )
    assert result.result == "published"
    assert result.mutation_classification == "mutated"
    assert [
        (entry.ordinal, entry.status, entry.readback_exact)
        for entry in result.operations
    ] == [(0, "succeeded", True), (1, "succeeded", True)]
    assert result.final_readback_exact is True
    assert result.mutation_marker_reference == marker_ref
    assert [call[0] for call in transport.calls] == [
        "POST",
        "GET",
        "GET",
        "POST",
        "GET",
        "GET",
        "GET",
    ]
    assert distributions[0].content in transport.calls[0][3]
    assert distributions[1].content in transport.calls[3][3]
    assert (
        python_publication_result_from_document(
            result.to_document()
        ).result_digest
        == result.result_digest
    )


@pytest.mark.parametrize(
    "failure",
    [
        "wheel-rejected",
        "wheel-timeout",
        "wheel-readback",
        "sdist-rejected",
        "sdist-timeout",
        "final-readback",
    ],
)
def test_python_publication_failure_stops_without_completion_or_retry(
    tmp_path, failure
):
    """Known partial and ambiguous effects stay failed without extra uploads."""
    marker, marker_ref, payloads, distributions = prepared_publication()
    registry = marker.absence.registry
    ok = PythonHttpResponse(200, b"created", "text/plain")
    rejected = PythonHttpResponse(400, b"duplicate", "text/plain")
    missing = PythonHttpResponse(404, b"missing", "text/plain")
    if failure == "wheel-rejected":
        responses, statuses = (rejected,), ("failed", "not-attempted")
    elif failure == "wheel-timeout":
        responses, statuses = (
            (TimeoutError("lost response"),),
            ("unknown", "not-attempted"),
        )
    elif failure == "wheel-readback":
        responses, statuses = (ok, missing), ("succeeded", "not-attempted")
    else:
        ending = (
            (rejected,)
            if failure == "sdist-rejected"
            else (TimeoutError("lost response"),)
            if failure == "sdist-timeout"
            else (ok, missing)
        )
        responses = (ok, *_readback(registry, distributions[:1]), *ending)
        statuses = (
            "succeeded",
            "failed"
            if failure == "sdist-rejected"
            else "unknown"
            if failure == "sdist-timeout"
            else "succeeded",
        )
    transport = FakeHttp(*responses)
    result = execute_python_publication(
        marker,
        marker_ref,
        payloads,
        token=_TOKEN,
        transport=transport,
        claim_path=tmp_path / "claim",
        clock=lambda: NOW + timedelta(seconds=3),
    )
    assert result.result == "failed"
    assert tuple(entry.status for entry in result.operations) == statuses
    assert result.final_readback_exact is False
    assert result.final_readback_digest is None
    assert [call[0] for call in transport.calls].count("POST") == (
        1 if failure.startswith("wheel") else 2
    )
    assert transport.responses == []
    assert result.mutation_classification != "not-mutated"


def test_python_publication_claim_prevents_second_execution(tmp_path):
    """Even a rejected first upload consumes the local one-shot claim."""
    marker, marker_ref, payloads, _ = prepared_publication()
    transport = FakeHttp(PythonHttpResponse(400, b"rejected", "text/plain"))
    arguments = {
        "token": _TOKEN,
        "transport": transport,
        "claim_path": tmp_path / "claim",
        "clock": lambda: NOW + timedelta(seconds=3),
    }
    first = execute_python_publication(
        marker, marker_ref, payloads, **arguments
    )
    assert first.result == "failed"
    with pytest.raises(FileExistsError):
        execute_python_publication(marker, marker_ref, payloads, **arguments)
    assert len(transport.calls) == 1


@pytest.mark.parametrize(
    "change",
    [
        "missing",
        "extra",
        "duplicate",
        "reversed",
        "sdist-after-failure",
        "sdist-before-readback",
    ],
)
def test_python_result_rejects_unclosed_ordinals_or_unsafe_sequence(change):
    """A Result cannot manufacture an independently approved second action."""
    marker, marker_ref, _, _ = prepared_publication()
    wheel = PythonOperationResult(
        0, "succeeded", _DIGEST, _DIGEST, readback_exact=True
    )
    sdist = PythonOperationResult(
        1, "succeeded", _DIGEST, _DIGEST, readback_exact=True
    )
    operations = (wheel, sdist)
    if change == "missing":
        operations = (wheel,)
    elif change == "extra":
        operations = (*operations, sdist)
    elif change == "duplicate":
        operations = (wheel, wheel)
    elif change == "reversed":
        operations = (sdist, wheel)
    elif change == "sdist-after-failure":
        operations = (replace(wheel, status="failed"), sdist)
    else:
        operations = (replace(wheel, readback_exact=False), sdist)
    with pytest.raises(ValueError, match="Python"):
        PythonPublicationResult(
            marker.attempt,
            marker_ref,
            operations,
            _DIGEST,
            final_readback_exact=True,
        )


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("variant", "npm"),
        ("producer", "foreign"),
        ("result", "published"),
        ("extra", True),
        ("final-readback-exact", 0),
    ],
)
def test_python_result_serialization_rejects_variant_or_claim_substitution(
    field, value
):
    """The Python Result has one strict schema variant and scalar marker ref."""
    marker, marker_ref, _, _ = prepared_publication()
    result = PythonPublicationResult(
        marker.attempt,
        marker_ref,
        (
            PythonOperationResult(
                0, "failed", _DIGEST, None, readback_exact=False
            ),
            PythonOperationResult(
                1, "not-attempted", None, None, readback_exact=False
            ),
        ),
        None,
        final_readback_exact=False,
    )
    document = result.to_document()
    document[field] = value
    with pytest.raises(ValueError, match="Python"):
        python_publication_result_from_document(document)
