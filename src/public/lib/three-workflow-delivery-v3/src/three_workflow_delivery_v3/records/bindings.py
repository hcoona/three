"""Caller-authoritative current transport admission bindings."""

from __future__ import annotations

import re
from dataclasses import dataclass
from typing import TYPE_CHECKING

from three_workflow_delivery_v3.canonical import canonical_sha256

if TYPE_CHECKING:
    from three_workflow_delivery_v3.canonical import JsonPrimitive, JsonValue

type PlatformMetadata = tuple[tuple[str, JsonPrimitive], ...]
type JsonExpectedType = type[str | int]
type JsonSchema = tuple[tuple[str, JsonExpectedType], ...]


@dataclass(frozen=True, slots=True)
class CurrentAuthorityContext:
    """Exact bindings required for a current transport."""

    release_execution: str
    purpose: str
    request: str
    workflow_run_id: int
    run_attempt: int | None
    attempt: str
    target: str
    producer: str
    control: str
    artifact_id: int
    artifact_digest: str
    payload_digest: str


@dataclass(frozen=True, slots=True)
class Admission:
    """Successful admission under current caller authority."""

    release_execution: str
    purpose: str
    target: str
    control_identity: str
    artifact_digest: str
    payload_digest: str


_CURRENT_PAYLOAD_SCHEMA: JsonSchema = (
    ("release_execution", str),
    ("purpose", str),
    ("request", str),
    ("workflow_run_id", int),
    ("attempt", str),
    ("target", str),
    ("producer", str),
    ("control", str),
)
_CURRENT_ATTEMPT_PAYLOAD_SCHEMA: JsonSchema = (
    *_CURRENT_PAYLOAD_SCHEMA,
    ("run_attempt", int),
)
_PURPOSES = frozenset(
    {
        "ci-pr-slice-shadow",
        "slice-validation",
        "live-release",
        "release-simulation",
        "destination-acceptance",
    }
)
_COMMIT_SHA_PATTERN = re.compile(r"[0-9a-f]{40}\Z")
_SHA256_PATTERN = re.compile(r"sha256:[0-9a-f]{64}\Z")


def _require_json_object(payload: JsonValue) -> dict[str, JsonValue]:
    if not isinstance(payload, dict):
        message = "admission payload must be a JSON object"
        raise TypeError(message)
    return payload


def _matches_json_type(
    value: JsonValue,
    expected: JsonExpectedType,
) -> bool:
    if expected is int:
        return isinstance(value, int) and not isinstance(value, bool)
    return isinstance(value, expected)


def _validate_closed_schema(
    document: dict[str, JsonValue],
    *,
    required: JsonSchema,
) -> None:
    required_names = {name for name, _ in required}
    missing = required_names - document.keys()
    if missing:
        name = sorted(missing)[0]
        message = f"current-authority schema missing required field: {name}"
        raise ValueError(message)
    unknown = document.keys() - required_names
    if unknown:
        name = sorted(unknown)[0]
        message = f"current-authority schema unknown field: {name}"
        raise ValueError(message)
    for name, expected in required:
        if not _matches_json_type(document[name], expected):
            message = f"current-authority schema wrong JSON type: {name}"
            raise TypeError(message)


def _validate_digest(digest: str, *, authority: str) -> None:
    if not isinstance(digest, str) or _SHA256_PATTERN.fullmatch(digest) is None:
        message = f"current-authority malformed {authority}"
        raise ValueError(message)


def _validate_purpose(value: object) -> None:
    if not isinstance(value, str) or value not in _PURPOSES:
        message = "current-authority invalid closed purpose: purpose"
        raise ValueError(message)


def _validate_commit_sha(value: object, *, field: str) -> None:
    if (
        not isinstance(value, str)
        or _COMMIT_SHA_PATTERN.fullmatch(value) is None
    ):
        message = (
            f"current-authority malformed {field}: expected 40 lowercase hex"
        )
        raise ValueError(message)


def _validate_positive_integer(value: object, *, field: str) -> None:
    if not isinstance(value, int) or isinstance(value, bool) or value <= 0:
        message = (
            f"current-authority {field} must be a positive non-Boolean integer"
        )
        raise ValueError(message)


def _validate_binding(value: object, expected: object, *, field: str) -> None:
    if value != expected:
        message = f"current-authority binding mismatch: {field}"
        raise ValueError(message)


def admit(
    *,
    payload: JsonValue,
    artifact_id: int,
    artifact_digest: str,
    current: CurrentAuthorityContext,
) -> Admission:
    """Admit an exact payload under caller-selected current authority."""
    document = _require_json_object(payload)
    if "purpose" not in document:
        message = "current-authority schema missing required field: purpose"
        raise ValueError(message)
    _validate_purpose(document["purpose"])
    if type(current) is not CurrentAuthorityContext:
        message = "current-authority context is required"
        raise TypeError(message)
    _validate_purpose(current.purpose)
    _validate_binding(document["purpose"], current.purpose, field="purpose")
    schema = (
        _CURRENT_PAYLOAD_SCHEMA
        if current.purpose == "live-release"
        else _CURRENT_ATTEMPT_PAYLOAD_SCHEMA
    )
    _validate_closed_schema(document, required=schema)
    _validate_commit_sha(document["target"], field="target")
    _validate_commit_sha(current.target, field="target")
    _validate_positive_integer(
        document["workflow_run_id"],
        field="workflow_run_id",
    )
    _validate_positive_integer(
        current.workflow_run_id,
        field="workflow_run_id",
    )
    if current.purpose == "live-release":
        if current.run_attempt is not None:
            message = "current-authority live context cannot bind run_attempt"
            raise ValueError(message)
    else:
        _validate_positive_integer(current.run_attempt, field="run_attempt")
        _validate_positive_integer(document["run_attempt"], field="run_attempt")
    _validate_positive_integer(artifact_id, field="artifact_id")
    _validate_positive_integer(current.artifact_id, field="artifact_id")
    _validate_digest(artifact_digest, authority="artifact_digest")
    _validate_digest(current.artifact_digest, authority="artifact_digest")
    _validate_digest(current.payload_digest, authority="payload_digest")
    for name, _ in schema:
        _validate_binding(document[name], getattr(current, name), field=name)
    if artifact_id != current.artifact_id:
        message = "current-authority binding mismatch: artifact_id"
        raise ValueError(message)
    if artifact_digest != current.artifact_digest:
        message = "current-authority binding mismatch: artifact_digest"
        raise ValueError(message)
    if canonical_sha256(payload) != current.payload_digest:
        message = "current-authority payload integrity mismatch"
        raise ValueError(message)
    return Admission(
        release_execution=current.release_execution,
        purpose=current.purpose,
        target=current.target,
        control_identity=current.control,
        artifact_digest=current.artifact_digest,
        payload_digest=current.payload_digest,
    )


__all__ = ["Admission", "CurrentAuthorityContext", "admit"]
