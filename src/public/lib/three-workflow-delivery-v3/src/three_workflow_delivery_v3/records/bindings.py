"""Caller-authoritative transport admission bindings."""

from __future__ import annotations

import re
from collections.abc import Collection
from dataclasses import dataclass
from enum import StrEnum
from typing import TYPE_CHECKING

from three_workflow_delivery_v3.canonical import canonical_sha256

if TYPE_CHECKING:
    from three_workflow_delivery_v3.canonical import JsonPrimitive, JsonValue

type PlatformMetadata = tuple[tuple[str, JsonPrimitive], ...]
type JsonExpectedType = type[str | int]
type JsonSchema = tuple[tuple[str, JsonExpectedType], ...]


class AdmissionMode(StrEnum):
    """Admission branch selected by the trusted caller."""

    CURRENT_AUTHORITY = "current-authority"
    EXECUTION_HISTORY = "execution-history"


@dataclass(frozen=True, slots=True)
class HistoryLineage:
    """Trusted lineage used to correlate one history candidate."""

    release_execution: str
    purpose: str
    target: str
    control_identity: str


@dataclass(frozen=True, slots=True)
class CurrentAuthorityContext:
    """Exact bindings required for a current transport."""

    release_execution: str
    purpose: str
    request: str
    workflow_run_id: int
    run_attempt: int
    attempt: str
    target: str
    producer: str
    control: str
    artifact_id: int
    artifact_digest: str
    payload_digest: str


@dataclass(frozen=True, slots=True)
class ExecutionHistoryContext:
    """Trusted candidate facts and artifact bindings for history admission."""

    lineage: HistoryLineage
    operation: str
    attempt_created: bool
    artifact_id: int
    artifact_digest: str
    payload_digest: str
    source_workflow_run_id: int
    current_workflow_run_id: int
    current_run_attempt: int
    exposed_platform_metadata: PlatformMetadata


@dataclass(frozen=True, slots=True)
class PlatformRunFacts:
    """Run facts obtained separately from the platform API."""

    workflow_run_id: int
    head_sha: str
    run_attempt: int
    exposed_metadata: PlatformMetadata


@dataclass(frozen=True, slots=True)
class PlatformJobFacts:
    """Job and phase facts obtained separately from the platform API."""

    job_id: int
    conclusion: str
    phase: str


@dataclass(frozen=True, slots=True)
class Admission:
    """Successful admission with an explicit authority boundary."""

    mode: AdmissionMode
    history_only: bool
    release_execution: str
    purpose: str
    target: str
    control_identity: str
    artifact_digest: str
    payload_digest: str
    platform_run: PlatformRunFacts | None = None
    platform_job: PlatformJobFacts | None = None
    diagnostic_claims: tuple[tuple[str, JsonValue], ...] = ()


_CURRENT_PAYLOAD_SCHEMA: JsonSchema = (
    ("release_execution", str),
    ("purpose", str),
    ("request", str),
    ("workflow_run_id", int),
    ("run_attempt", int),
    ("attempt", str),
    ("target", str),
    ("producer", str),
    ("control", str),
)
_HISTORY_REQUIRED_SCHEMA: JsonSchema = (
    ("execution", str),
    ("target", str),
)
_HISTORY_DIAGNOSTIC_SCHEMA: JsonSchema = (
    ("producer", str),
    ("run_attempt", int),
    ("reusable_workflow", str),
    ("purpose", str),
    ("control", str),
)
_HISTORY_DIAGNOSTIC_CLAIMS = tuple(
    name for name, _ in _HISTORY_DIAGNOSTIC_SCHEMA
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
    mode: str,
    required: JsonSchema,
    optional: JsonSchema = (),
) -> None:
    required_names = {name for name, _ in required}
    optional_names = {name for name, _ in optional}
    missing = required_names - document.keys()
    if missing:
        name = sorted(missing)[0]
        message = f"{mode} schema missing required field: {name}"
        raise ValueError(message)
    unknown = document.keys() - required_names - optional_names
    if unknown:
        name = sorted(unknown)[0]
        message = f"{mode} schema unknown field: {name}"
        raise ValueError(message)
    for name, expected in (*required, *optional):
        if name in document and not _matches_json_type(
            document[name],
            expected,
        ):
            message = f"{mode} schema wrong JSON type: {name}"
            raise TypeError(message)


def _validate_digest(digest: str, *, mode: str, authority: str) -> None:
    if not isinstance(digest, str) or _SHA256_PATTERN.fullmatch(digest) is None:
        message = f"{mode} malformed {authority}"
        raise ValueError(message)


def _validate_purpose(value: object, *, mode: str) -> None:
    if not isinstance(value, str) or value not in _PURPOSES:
        message = f"{mode} invalid closed purpose: purpose"
        raise ValueError(message)


def _validate_commit_sha(value: object, *, mode: str, field: str) -> None:
    if (
        not isinstance(value, str)
        or _COMMIT_SHA_PATTERN.fullmatch(value) is None
    ):
        message = f"{mode} malformed {field}: expected 40 lowercase hex"
        raise ValueError(message)


def _validate_positive_integer(
    value: object,
    *,
    mode: str,
    field: str,
) -> None:
    if not isinstance(value, int) or isinstance(value, bool) or value <= 0:
        message = f"{mode} {field} must be a positive non-Boolean integer"
        raise ValueError(message)


def _validate_verified_prior_attempts(
    verified_prior_attempts: object,
) -> frozenset[int]:
    if isinstance(
        verified_prior_attempts,
        (str, bytes, bytearray),
    ) or not isinstance(verified_prior_attempts, Collection):
        message = (
            "execution-history verified prior run_attempts must be a "
            "collection of positive non-Boolean integers"
        )
        raise TypeError(message)
    for run_attempt in verified_prior_attempts:
        _validate_positive_integer(
            run_attempt,
            mode="execution-history",
            field="verified prior run_attempt",
        )
    return frozenset(verified_prior_attempts)


def _admit_current(
    payload: JsonValue,
    artifact_id: int,
    artifact_digest: str,
    context: CurrentAuthorityContext | None,
) -> Admission:
    document = _require_json_object(payload)
    _validate_closed_schema(
        document,
        mode="current-authority",
        required=_CURRENT_PAYLOAD_SCHEMA,
    )
    if context is None:
        message = "current-authority context is required"
        raise ValueError(message)
    _validate_purpose(document["purpose"], mode="current-authority")
    _validate_purpose(context.purpose, mode="current-authority")
    _validate_commit_sha(
        document["target"],
        mode="current-authority",
        field="target",
    )
    _validate_commit_sha(
        context.target,
        mode="current-authority",
        field="target",
    )
    _validate_positive_integer(
        document["workflow_run_id"],
        mode="current-authority",
        field="workflow_run_id",
    )
    _validate_positive_integer(
        context.workflow_run_id,
        mode="current-authority",
        field="workflow_run_id",
    )
    _validate_positive_integer(
        document["run_attempt"],
        mode="current-authority",
        field="run_attempt",
    )
    _validate_positive_integer(
        context.run_attempt,
        mode="current-authority",
        field="run_attempt",
    )
    _validate_positive_integer(
        artifact_id,
        mode="current-authority",
        field="artifact_id",
    )
    _validate_positive_integer(
        context.artifact_id,
        mode="current-authority",
        field="artifact_id",
    )
    _validate_digest(
        artifact_digest,
        mode="current-authority",
        authority="artifact_digest",
    )
    _validate_digest(
        context.artifact_digest,
        mode="current-authority",
        authority="artifact_digest",
    )
    _validate_digest(
        context.payload_digest,
        mode="current-authority",
        authority="payload_digest",
    )
    for name, _ in _CURRENT_PAYLOAD_SCHEMA:
        if document[name] != getattr(context, name):
            message = f"current-authority binding mismatch: {name}"
            raise ValueError(message)
    if artifact_id != context.artifact_id:
        message = "current-authority binding mismatch: artifact_id"
        raise ValueError(message)
    if artifact_digest != context.artifact_digest:
        message = "current-authority binding mismatch: artifact_digest"
        raise ValueError(message)
    if canonical_sha256(payload) != context.payload_digest:
        message = "current-authority payload integrity mismatch"
        raise ValueError(message)
    return Admission(
        mode=AdmissionMode.CURRENT_AUTHORITY,
        history_only=False,
        release_execution=context.release_execution,
        purpose=context.purpose,
        target=context.target,
        control_identity=context.control,
        artifact_digest=context.artifact_digest,
        payload_digest=context.payload_digest,
    )


def _validate_history_lineage_primitives(
    candidate: HistoryLineage,
    expected: HistoryLineage,
) -> None:
    _validate_purpose(candidate.purpose, mode="execution-history")
    _validate_purpose(expected.purpose, mode="execution-history")
    _validate_commit_sha(
        candidate.target,
        mode="execution-history",
        field="target",
    )
    _validate_commit_sha(
        expected.target,
        mode="execution-history",
        field="target",
    )


def _validate_history_lineage(
    candidate: HistoryLineage,
    expected: HistoryLineage,
) -> None:
    for name in (
        "release_execution",
        "target",
        "purpose",
        "control_identity",
    ):
        if getattr(candidate, name) != getattr(expected, name):
            message = f"execution-history lineage mismatch: {name}"
            raise ValueError(message)


def _validate_history_primitives(  # noqa: PLR0913
    document: dict[str, JsonValue],
    artifact_id: int,
    context: ExecutionHistoryContext,
    expected_lineage: HistoryLineage,
    platform_run: PlatformRunFacts,
    platform_job: PlatformJobFacts,
    verified_prior_attempts: object,
) -> frozenset[int]:
    _validate_commit_sha(
        document["target"],
        mode="execution-history",
        field="target",
    )
    if "purpose" in document:
        _validate_purpose(
            document["purpose"],
            mode="execution-history",
        )
    if "run_attempt" in document:
        _validate_positive_integer(
            document["run_attempt"],
            mode="execution-history",
            field="run_attempt",
        )
    _validate_history_lineage_primitives(
        context.lineage,
        expected_lineage,
    )
    positive_integers = (
        ("artifact_id", artifact_id),
        ("artifact_id", context.artifact_id),
        ("source_workflow_run_id", context.source_workflow_run_id),
        ("current_workflow_run_id", context.current_workflow_run_id),
        ("current_run_attempt", context.current_run_attempt),
        ("workflow_run_id", platform_run.workflow_run_id),
        ("source run_attempt", platform_run.run_attempt),
        ("job_id", platform_job.job_id),
    )
    for field, value in positive_integers:
        _validate_positive_integer(
            value,
            mode="execution-history",
            field=field,
        )
    _validate_commit_sha(
        platform_run.head_sha,
        mode="execution-history",
        field="head_sha",
    )
    return _validate_verified_prior_attempts(verified_prior_attempts)


def _admit_history(  # noqa: C901, PLR0912, PLR0913
    payload: JsonValue,
    artifact_id: int,
    artifact_digest: str,
    context: ExecutionHistoryContext | None,
    expected_lineage: HistoryLineage | None,
    platform_run: PlatformRunFacts | None,
    platform_job: PlatformJobFacts | None,
    *,
    requires_current_authority: bool,
    verified_prior_attempts: object,
) -> Admission:
    document = _require_json_object(payload)
    _validate_closed_schema(
        document,
        mode="execution-history",
        required=_HISTORY_REQUIRED_SCHEMA,
        optional=_HISTORY_DIAGNOSTIC_SCHEMA,
    )
    if requires_current_authority:
        message = "execution history cannot satisfy current authority"
        raise ValueError(message)
    if (
        context is None
        or expected_lineage is None
        or platform_run is None
        or platform_job is None
    ):
        message = (
            "history context, caller expectations, and separate platform "
            "facts are required"
        )
        raise ValueError(message)
    verified_attempts = _validate_history_primitives(
        document,
        artifact_id,
        context,
        expected_lineage,
        platform_run,
        platform_job,
        verified_prior_attempts,
    )
    outside_lifecycle = (
        expected_lineage.purpose != "live-release"
        or context.operation != "admit"
        or context.attempt_created
    )
    if outside_lifecycle:
        message = "execution history is limited to pre-Attempt live admit"
        raise ValueError(message)
    _validate_history_lineage(context.lineage, expected_lineage)
    _validate_digest(
        artifact_digest,
        mode="execution-history",
        authority="artifact_digest",
    )
    _validate_digest(
        context.artifact_digest,
        mode="execution-history",
        authority="artifact_digest",
    )
    _validate_digest(
        context.payload_digest,
        mode="execution-history",
        authority="payload_digest",
    )
    if document["execution"] != expected_lineage.release_execution:
        message = "execution-history attribution mismatch: release_execution"
        raise ValueError(message)
    if document["target"] != expected_lineage.target:
        message = "execution-history attribution mismatch: target"
        raise ValueError(message)
    lineage_diagnostics = (
        ("purpose", expected_lineage.purpose),
        ("control", expected_lineage.control_identity),
    )
    for name, trusted_value in lineage_diagnostics:
        if name in document and document[name] != trusted_value:
            message = f"execution-history diagnostic claim mismatch: {name}"
            raise ValueError(message)
    checks = (
        ("artifact_id", artifact_id, context.artifact_id),
        ("artifact_digest", artifact_digest, context.artifact_digest),
        (
            "source_workflow_run_id",
            platform_run.workflow_run_id,
            context.source_workflow_run_id,
        ),
        ("head_sha", platform_run.head_sha, expected_lineage.target),
        (
            "exposed_platform_metadata",
            platform_run.exposed_metadata,
            context.exposed_platform_metadata,
        ),
    )
    for name, actual, expected in checks:
        if actual != expected:
            message = f"execution-history attribution mismatch: {name}"
            raise ValueError(message)
    same_run_candidate = (
        platform_run.workflow_run_id == context.current_workflow_run_id
    )
    if same_run_candidate:
        if platform_run.run_attempt >= context.current_run_attempt:
            message = (
                "execution-history source run_attempt must be earlier than "
                "current_run_attempt"
            )
            raise ValueError(message)
        if platform_run.run_attempt not in verified_attempts:
            message = (
                "execution-history source run_attempt lacks a verified prior "
                "run_attempt existence fact"
            )
            raise ValueError(message)
    phase_checks = (
        ("conclusion", platform_job.conclusion == "success"),
        ("phase", platform_job.phase == "finalized"),
    )
    for name, matches in phase_checks:
        if not matches:
            message = f"execution-history phase fact mismatch: {name}"
            raise ValueError(message)
    if canonical_sha256(payload) != context.payload_digest:
        message = "execution-history payload integrity mismatch"
        raise ValueError(message)
    diagnostics = tuple(
        (name, document[name])
        for name in _HISTORY_DIAGNOSTIC_CLAIMS
        if name in document
    )
    return Admission(
        mode=AdmissionMode.EXECUTION_HISTORY,
        history_only=True,
        release_execution=expected_lineage.release_execution,
        purpose=expected_lineage.purpose,
        target=expected_lineage.target,
        control_identity=expected_lineage.control_identity,
        artifact_digest=context.artifact_digest,
        payload_digest=context.payload_digest,
        platform_run=platform_run,
        platform_job=platform_job,
        diagnostic_claims=diagnostics,
    )


def admit(  # noqa: PLR0913
    *,
    mode: AdmissionMode,
    payload: JsonValue,
    artifact_id: int,
    artifact_digest: str,
    current: CurrentAuthorityContext | None = None,
    history: ExecutionHistoryContext | None = None,
    expected_history_lineage: HistoryLineage | None = None,
    platform_run: PlatformRunFacts | None = None,
    platform_job: PlatformJobFacts | None = None,
    requires_current_authority: bool = False,
    verified_prior_attempts: Collection[int] = (),
) -> Admission:
    """Admit a payload according to the trusted caller's selected mode."""
    if mode is AdmissionMode.CURRENT_AUTHORITY:
        return _admit_current(payload, artifact_id, artifact_digest, current)
    if mode is AdmissionMode.EXECUTION_HISTORY:
        return _admit_history(
            payload,
            artifact_id,
            artifact_digest,
            history,
            expected_history_lineage,
            platform_run,
            platform_job,
            requires_current_authority=requires_current_authority,
            verified_prior_attempts=verified_prior_attempts,
        )
    message = f"unsupported admission mode: {mode!r}"
    raise ValueError(message)
