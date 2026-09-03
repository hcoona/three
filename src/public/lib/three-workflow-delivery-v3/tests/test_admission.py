"""Current-authority transport admission contracts."""

# ruff: noqa: D103

from __future__ import annotations

from dataclasses import replace
from typing import cast

import pytest
from three_workflow_delivery_v3.canonical import JsonValue, canonical_sha256
from three_workflow_delivery_v3.records import (
    CurrentAuthorityContext,
    admit,
)

TARGET = "a" * 40
ARTIFACT_DIGEST = "sha256:" + ("b" * 64)


class _DerivedCurrentAuthorityContext(CurrentAuthorityContext):
    """Invalid extension of the exact current-authority context."""


def _payload(
    *,
    purpose: str = "live-release",
) -> dict[str, JsonValue]:
    payload: dict[str, JsonValue] = {
        "release_execution": f"buddy:unit:{TARGET}",
        "purpose": purpose,
        "request": "release-request:" + ("c" * 64),
        "workflow_run_id": 700,
        "attempt": "attempt:" + ("d" * 64),
        "target": TARGET,
        "producer": "producer",
        "control": "control:" + ("e" * 64),
    }
    if purpose != "live-release":
        payload["run_attempt"] = 2
    return payload


def _context(
    payload: dict[str, JsonValue],
) -> CurrentAuthorityContext:
    return CurrentAuthorityContext(
        release_execution=str(payload["release_execution"]),
        purpose=str(payload["purpose"]),
        request=str(payload["request"]),
        workflow_run_id=cast("int", payload["workflow_run_id"]),
        run_attempt=(
            None
            if payload["purpose"] == "live-release"
            else cast("int", payload["run_attempt"])
        ),
        attempt=str(payload["attempt"]),
        target=str(payload["target"]),
        producer=str(payload["producer"]),
        control=str(payload["control"]),
        artifact_id=17,
        artifact_digest=ARTIFACT_DIGEST,
        payload_digest=canonical_sha256(payload),
    )


def test_current_authority_admits_exact_live_payload() -> None:
    payload = _payload()
    context = _context(payload)

    admission = admit(
        payload=payload,
        artifact_id=context.artifact_id,
        artifact_digest=context.artifact_digest,
        current=context,
    )

    assert admission.release_execution == context.release_execution
    assert admission.purpose == "live-release"
    assert admission.target == TARGET
    assert admission.control_identity == context.control
    assert admission.artifact_digest == context.artifact_digest
    assert admission.payload_digest == context.payload_digest


@pytest.mark.parametrize(
    "purpose",
    [
        "ci-pr-slice-shadow",
        "slice-validation",
        "release-simulation",
        "destination-acceptance",
    ],
)
def test_non_live_current_authority_requires_attempt(
    purpose: str,
) -> None:
    payload = _payload(purpose=purpose)
    context = _context(payload)

    admission = admit(
        payload=payload,
        artifact_id=context.artifact_id,
        artifact_digest=context.artifact_digest,
        current=context,
    )

    assert admission.purpose == purpose


def test_live_payload_rejects_attempt_field() -> None:
    payload = _payload()
    payload["run_attempt"] = 1

    with pytest.raises(
        ValueError,
        match="current-authority schema unknown field: run_attempt",
    ):
        admit(
            payload=payload,
            artifact_id=17,
            artifact_digest=ARTIFACT_DIGEST,
            current=_context(_payload()),
        )


def test_non_live_payload_requires_attempt_field() -> None:
    payload = _payload(purpose="release-simulation")
    del payload["run_attempt"]

    with pytest.raises(
        ValueError,
        match="current-authority schema missing required field: run_attempt",
    ):
        admit(
            payload=payload,
            artifact_id=17,
            artifact_digest=ARTIFACT_DIGEST,
            current=_context(_payload(purpose="release-simulation")),
        )


def test_current_authority_rejects_mixed_purpose_before_attempt_access() -> (
    None
):
    live_payload = _payload()
    simulation_payload = _payload(purpose="release-simulation")

    with pytest.raises(
        ValueError,
        match="current-authority binding mismatch: purpose",
    ):
        admit(
            payload=live_payload,
            artifact_id=17,
            artifact_digest=ARTIFACT_DIGEST,
            current=_context(simulation_payload),
        )


def test_current_authority_rejects_mixed_purpose_before_schema_access() -> None:
    simulation_payload = _payload(purpose="release-simulation")
    del simulation_payload["run_attempt"]

    with pytest.raises(
        ValueError,
        match="current-authority binding mismatch: purpose",
    ):
        admit(
            payload=simulation_payload,
            artifact_id=17,
            artifact_digest=ARTIFACT_DIGEST,
            current=_context(_payload()),
        )


def test_current_authority_rejects_mixed_purpose_before_context_schema() -> (
    None
):
    with pytest.raises(
        ValueError,
        match="current-authority binding mismatch: purpose",
    ):
        admit(
            payload=_payload(purpose="release-simulation"),
            artifact_id=17,
            artifact_digest=ARTIFACT_DIGEST,
            current=_context(_payload()),
        )


@pytest.mark.parametrize(
    ("field", "replacement"),
    [
        ("release_execution", "buddy:other:" + TARGET),
        ("request", "release-request:" + ("f" * 64)),
        ("workflow_run_id", 701),
        ("attempt", "attempt:" + ("f" * 64)),
        ("target", "f" * 40),
        ("producer", "other-producer"),
        ("control", "control:" + ("f" * 64)),
    ],
)
def test_current_authority_rejects_payload_binding_substitution(
    field: str,
    replacement: JsonValue,
) -> None:
    payload = _payload()
    context = _context(payload)
    payload[field] = replacement

    with pytest.raises(
        ValueError,
        match=f"current-authority binding mismatch: {field}",
    ):
        admit(
            payload=payload,
            artifact_id=context.artifact_id,
            artifact_digest=context.artifact_digest,
            current=context,
        )


@pytest.mark.parametrize(
    ("artifact_id", "artifact_digest", "context_payload_digest", "message"),
    [
        (18, ARTIFACT_DIGEST, None, "binding mismatch: artifact_id"),
        (
            17,
            "sha256:" + ("f" * 64),
            None,
            "binding mismatch: artifact_digest",
        ),
        (
            17,
            ARTIFACT_DIGEST,
            "sha256:" + ("f" * 64),
            "payload integrity mismatch",
        ),
    ],
)
def test_current_authority_keeps_transport_and_payload_authorities_distinct(
    artifact_id: int,
    artifact_digest: str,
    context_payload_digest: str | None,
    message: str,
) -> None:
    payload = _payload()
    context = _context(payload)
    if context_payload_digest is not None:
        context = replace(
            context,
            payload_digest=context_payload_digest,
        )

    with pytest.raises(ValueError, match=message):
        admit(
            payload=payload,
            artifact_id=artifact_id,
            artifact_digest=artifact_digest,
            current=context,
        )


@pytest.mark.parametrize(
    ("field", "value", "error"),
    [
        ("workflow_run_id", True, TypeError),
        ("workflow_run_id", 0, ValueError),
        ("target", "A" * 40, ValueError),
        ("purpose", "execution-history", ValueError),
    ],
)
def test_current_authority_rejects_invalid_closed_primitives(
    field: str,
    value: JsonValue,
    error: type[Exception],
) -> None:
    payload = _payload()
    payload[field] = value

    with pytest.raises(error):
        admit(
            payload=payload,
            artifact_id=17,
            artifact_digest=ARTIFACT_DIGEST,
            current=_context(_payload()),
        )


@pytest.mark.parametrize("payload", [None, [], "", 1, True])
def test_current_authority_requires_json_object(payload: object) -> None:
    with pytest.raises(TypeError, match="must be a JSON object"):
        admit(
            payload=payload,  # type: ignore[arg-type]
            artifact_id=17,
            artifact_digest=ARTIFACT_DIGEST,
            current=_context(_payload()),
        )


def test_current_authority_requires_exact_context_type() -> None:
    payload = _payload()

    with pytest.raises(TypeError, match="context is required"):
        admit(
            payload=payload,
            artifact_id=17,
            artifact_digest=ARTIFACT_DIGEST,
            current=object(),  # type: ignore[arg-type]
        )


def test_current_authority_rejects_context_subclass() -> None:
    payload = _payload()
    context = _context(payload)
    derived = _DerivedCurrentAuthorityContext(
        release_execution=context.release_execution,
        purpose=context.purpose,
        request=context.request,
        workflow_run_id=context.workflow_run_id,
        run_attempt=context.run_attempt,
        attempt=context.attempt,
        target=context.target,
        producer=context.producer,
        control=context.control,
        artifact_id=context.artifact_id,
        artifact_digest=context.artifact_digest,
        payload_digest=context.payload_digest,
    )

    with pytest.raises(TypeError, match="context is required"):
        admit(
            payload=payload,
            artifact_id=derived.artifact_id,
            artifact_digest=derived.artifact_digest,
            current=derived,
        )


def test_current_authority_validates_trusted_context_purpose() -> None:
    payload = _payload()
    context = replace(_context(payload), purpose="execution-history")

    with pytest.raises(ValueError, match="invalid closed purpose"):
        admit(
            payload=payload,
            artifact_id=context.artifact_id,
            artifact_digest=context.artifact_digest,
            current=context,
        )
