"""Request normalization helpers for workflow-release CI validation."""

from __future__ import annotations

import re
from collections.abc import Mapping, Sequence
from dataclasses import dataclass
from typing import Literal

from three_workflow_release_contracts.ci_validation import (
    API_VERSIONS_BY_KIND,
    DETAILS_BY_DIAGNOSTIC_CODE,
    REGISTERED_CI_VALIDATION_DIAGNOSTIC_CODES,
    REGISTERED_CI_VALIDATION_DIAGNOSTIC_DETAILS,
    CiValidationKind,
    DiagnosticDetail,
    DiagnosticFamily,
    DiagnosticSeverity,
    DiagnosticVerdictEffect,
    canonical_json_digest,
    validate_artifact_logical_ref,
    validate_common_envelope,
)
from three_workflow_release_contracts.contracts import (
    ContractValidationError,
    ValidationIssue,
)

CiValidationMode = Literal["pull_request", "push", "scheduled_full"]
JobConclusion = Literal[
    "success",
    "failure",
    "cancelled",
    "skipped",
    "timed_out",
    "action_required",
    "neutral",
]

_REQUEST_MODES = frozenset({"pull_request", "push", "scheduled_full"})
_EVENT_NAME_BY_MODE = {
    "pull_request": "pull_request",
    "push": "push",
    "scheduled_full": "schedule",
}
_AFFECTED_SOURCES = frozenset({"pull_request", "push"})
_AFFECTED_STATUSES = frozenset({"available", "unavailable"})
_AFFECTED_RANGE_KEYS = frozenset(
    {
        "status",
        "base-sha",
        "base-tip-sha",
        "head-sha",
        "changed-files",
        "source",
        "diagnostic",
        "diagnostic-detail",
    },
)
_RANGE_DETAILS = frozenset(
    {
        DiagnosticDetail.MISSING.value,
        DiagnosticDetail.INCOMPLETE.value,
        DiagnosticDetail.INCONSISTENT.value,
        DiagnosticDetail.UNCONFIRMED_PROVENANCE.value,
    },
)
_SHA_RE = re.compile(r"^[0-9a-f]{40}$")
_DIGEST_RE = re.compile(r"^[0-9a-f]{64}$")
_ZERO_SHA = "0" * 40
_SURROGATE_MIN = 0xD800
_SURROGATE_MAX = 0xDFFF


@dataclass(frozen=True, slots=True)
class NormalizedCiValidationRequest:
    """Validated CI request projection and deterministic request identity."""

    document: Mapping[str, object]
    artifact_ref: str
    request_digest: str
    projection: Mapping[str, object]
    mode: CiValidationMode
    run_id: str
    run_attempt: str


@dataclass(frozen=True, slots=True)
class CiValidationRequestNormalization:
    """Fail-closed request normalization result for planner inputs."""

    request: NormalizedCiValidationRequest | None
    diagnostics: tuple[Mapping[str, object], ...]

    @property
    def is_valid(self) -> bool:
        """Return whether a normalized planner-facing request is available."""
        return self.request is not None and not self.diagnostics


def ci_validation_request_artifact_ref(
    *,
    run_id: str,
    run_attempt: str,
) -> str:
    """Return the contract-owned planner-facing request logical artifact ref."""
    return (
        f"ci-validation/requests/{run_id}/{run_attempt}/"
        "ci-validation-request.json"
    )


def ci_validation_plan_artifact_ref(
    *,
    run_id: str,
    run_attempt: str,
) -> str:
    """Return the contract-owned validation-plan logical artifact ref."""
    return f"ci-validation/planning/{run_id}/{run_attempt}/validation-plan.json"


def ci_validation_planner_diagnostics_artifact_ref(
    *,
    run_id: str,
    run_attempt: str,
) -> str:
    """Return the contract-owned planner diagnostics logical artifact ref."""
    return (
        f"ci-validation/planning/{run_id}/{run_attempt}/"
        "planner-diagnostics.json"
    )


def ci_validation_execution_batch_manifest_artifact_ref(
    *,
    run_id: str,
    run_attempt: str,
) -> str:
    """Return the execution-batch manifest ref."""
    return (
        f"ci-validation/execution-batches/{run_id}/{run_attempt}/"
        "execution-batch-manifest.json"
    )


def ci_validation_batch_evidence_bundle_artifact_ref(
    *,
    run_id: str,
    run_attempt: str,
    batch_id: str,
) -> str:
    """Return the batch evidence bundle ref."""
    return (
        f"ci-validation/bundles/{run_id}/{run_attempt}/{batch_id}/"
        "batch-evidence-bundle.json"
    )


def ci_validation_aggregate_evidence_manifest_artifact_ref(
    *,
    run_id: str,
    run_attempt: str,
) -> str:
    """Return the aggregate evidence manifest ref."""
    return (
        f"ci-validation/aggregate/{run_id}/{run_attempt}/"
        "aggregate-evidence-manifest.json"
    )


def ci_validation_aggregate_summary_artifact_ref(
    *,
    run_id: str,
    run_attempt: str,
) -> str:
    """Return the aggregate summary ref."""
    return (
        f"ci-validation/aggregate/{run_id}/{run_attempt}/aggregate-summary.json"
    )


def ci_validation_request_projection(
    request: Mapping[str, object],
) -> dict[str, object]:
    """Return the canonical request digest projection defined by the LLD."""
    mode = request.get("mode")
    projection: dict[str, object] = {
        "api-version": request["api-version"],
        "kind": request["kind"],
        "mode": mode,
        "validation-tree": request["validation-tree"],
        "event": request["event"],
    }
    if mode == "scheduled_full":
        projection["scheduled-full"] = request["scheduled-full"]
    else:
        projection["affected-range"] = request["affected-range"]
    return projection


def normalize_ci_validation_request(
    document: object,
    *,
    expected_run_id: str,
    expected_run_attempt: str,
    expected_artifact_ref: str | None = None,
) -> CiValidationRequestNormalization:
    """Validate a planner-facing CI request and recompute its canonical digest.

    Invalid inputs return fail-closed ``request-invalid`` diagnostics instead of
    fabricating a partial request. Callers that require exception semantics can
    inspect ``diagnostics`` and raise their own boundary-specific error.
    """
    issues: list[ValidationIssue] = []
    request = _normalize_request_or_collect_issues(
        document,
        expected_run_id=expected_run_id,
        expected_run_attempt=expected_run_attempt,
        expected_artifact_ref=expected_artifact_ref,
        issues=issues,
    )
    if issues:
        return CiValidationRequestNormalization(
            request=None,
            diagnostics=(
                ci_validation_diagnostic(
                    diagnostic_id="request-invalid/001",
                    code=DiagnosticFamily.REQUEST_INVALID.value,
                    detail=_request_invalid_detail(issues),
                    message="CI validation request is not replayable",
                    source_type="request",
                    source_id=None,
                    severity=DiagnosticSeverity.FAIL_CLOSED.value,
                    verdict_effect=DiagnosticVerdictEffect.FAIL_CLOSED.value,
                ),
            ),
        )
    if request is None:
        msg = "request normalization produced no request and no diagnostics"
        raise AssertionError(msg)
    return CiValidationRequestNormalization(request=request, diagnostics=())


def validate_ci_validation_request(
    document: object,
    *,
    expected_run_id: str,
    expected_run_attempt: str,
    expected_artifact_ref: str | None = None,
) -> NormalizedCiValidationRequest:
    """Return a normalized request or raise ``ContractValidationError``."""
    result = normalize_ci_validation_request(
        document,
        expected_run_id=expected_run_id,
        expected_run_attempt=expected_run_attempt,
        expected_artifact_ref=expected_artifact_ref,
    )
    if result.request is None:
        raise ContractValidationError(
            [
                ValidationIssue(
                    "ci-validation-request",
                    str(result.diagnostics[0].get("detail")),
                ),
            ],
        )
    return result.request


def ci_validation_diagnostic(  # noqa: PLR0913
    *,
    diagnostic_id: str,
    code: str,
    detail: str | None,
    message: str | None,
    source_type: str,
    source_id: str | None,
    severity: str,
    verdict_effect: str,
) -> dict[str, object]:
    """Build a closed-vocabulary CI validation diagnostic record."""
    _validate_diagnostic_record_inputs(
        diagnostic_id=diagnostic_id,
        code=code,
        detail=detail,
        message=message,
        source_type=source_type,
        source_id=source_id,
        severity=severity,
        verdict_effect=verdict_effect,
    )
    return {
        "diagnostic-id": diagnostic_id,
        "code": code,
        "detail": detail,
        "message": message,
        "source": {"type": source_type, "id": source_id},
        "severity": severity,
        "verdict-effect": verdict_effect,
    }


def _validate_diagnostic_record_inputs(  # noqa: C901,PLR0913
    *,
    diagnostic_id: str,
    code: str,
    detail: str | None,
    message: str | None,
    source_type: str,
    source_id: str | None,
    severity: str,
    verdict_effect: str,
) -> None:
    issues: list[ValidationIssue] = []
    if not diagnostic_id:
        issues.append(ValidationIssue("diagnostic-id", "must not be empty"))
    if code not in REGISTERED_CI_VALIDATION_DIAGNOSTIC_CODES:
        issues.append(ValidationIssue("code", "is not registered"))
    if detail is not None:
        if detail not in REGISTERED_CI_VALIDATION_DIAGNOSTIC_DETAILS:
            issues.append(ValidationIssue("detail", "is not registered"))
        elif detail not in DETAILS_BY_DIAGNOSTIC_CODE.get(code, frozenset()):
            issues.append(
                ValidationIssue(
                    "detail",
                    "is not valid for this diagnostic code",
                ),
            )
    if message is not None and message == "":
        issues.append(ValidationIssue("message", "must be null or non-empty"))
    if source_type not in {
        "request",
        "impact",
        "subject",
        "descriptor",
        "fact-provider",
        "work-group",
        "aggregation",
    }:
        issues.append(ValidationIssue("source.type", "is not registered"))
    if source_id is not None and source_id == "":
        issues.append(ValidationIssue("source.id", "must be null or non-empty"))
    severities = {
        item.value for item in DiagnosticSeverity.__members__.values()
    }
    if severity not in severities:
        issues.append(ValidationIssue("severity", "is not registered"))
    verdict_effects = {
        item.value for item in DiagnosticVerdictEffect.__members__.values()
    }
    if verdict_effect not in verdict_effects:
        issues.append(ValidationIssue("verdict-effect", "is not registered"))
    if issues:
        raise ContractValidationError(issues)


def _normalize_request_or_collect_issues(
    document: object,
    *,
    expected_run_id: str,
    expected_run_attempt: str,
    expected_artifact_ref: str | None,
    issues: list[ValidationIssue],
) -> NormalizedCiValidationRequest | None:
    if not isinstance(document, Mapping):
        issues.append(ValidationIssue("$", "must be an object"))
        return None
    try:
        envelope = validate_common_envelope(
            document,
            api_version=API_VERSIONS_BY_KIND[CiValidationKind.REQUEST.value],
            kind=CiValidationKind.REQUEST,
        )
    except ContractValidationError as error:
        issues.extend(error.issues)
        return None
    if envelope.run_id != expected_run_id:
        issues.append(ValidationIssue("$.run.run-id", "must match current run"))
    if envelope.run_attempt != expected_run_attempt:
        issues.append(
            ValidationIssue(
                "$.run.run-attempt",
                "must match current run attempt",
            ),
        )
    expected_ref = expected_artifact_ref or ci_validation_request_artifact_ref(
        run_id=expected_run_id,
        run_attempt=expected_run_attempt,
    )
    _validate_request_shape(
        document,
        expected_run_id=expected_run_id,
        expected_run_attempt=expected_run_attempt,
        expected_artifact_ref=expected_ref,
        issues=issues,
    )
    if issues:
        return None
    projection = ci_validation_request_projection(document)
    try:
        computed_digest = canonical_json_digest(projection)
    except (TypeError, ValueError) as error:
        issues.append(
            ValidationIssue(
                "$.request-projection",
                f"request projection is not canonicalizable: {error}",
            ),
        )
        return None
    request_digest = document["request-digest"]
    if request_digest != computed_digest:
        issues.append(
            ValidationIssue(
                "$.request-digest", "does not match request projection"
            ),
        )
        return None
    mode = document["mode"]
    if mode not in _REQUEST_MODES:
        msg = "validated mode unexpectedly not registered"
        raise AssertionError(msg)
    return NormalizedCiValidationRequest(
        document=document,
        artifact_ref=str(document["artifact-ref"]),
        request_digest=computed_digest,
        projection=projection,
        mode=mode,  # type: ignore[arg-type]
        run_id=expected_run_id,
        run_attempt=expected_run_attempt,
    )


def _validate_request_shape(
    document: Mapping[str, object],
    *,
    expected_run_id: str,
    expected_run_attempt: str,
    expected_artifact_ref: str,
    issues: list[ValidationIssue],
) -> None:
    required = {
        "api-version",
        "kind",
        "created-at",
        "repository",
        "run",
        "schema-diagnostics",
        "artifact-ref",
        "request-digest",
        "mode",
        "validation-tree",
        "event",
    }
    mode = _required_enum(document, "mode", "$.mode", _REQUEST_MODES, issues)
    allowed = set(required)
    if mode == "scheduled_full":
        required.add("scheduled-full")
        allowed.add("scheduled-full")
    elif isinstance(mode, str) and mode in _AFFECTED_SOURCES:
        required.add("affected-range")
        allowed.add("affected-range")
    for key in sorted(required - set(document)):
        issues.append(ValidationIssue(f"$.{key}", "is required"))
    for key in sorted(set(document) - allowed):
        issues.append(ValidationIssue(f"$.{key}", "is not allowed"))
    artifact_ref = _required_str(
        document, "artifact-ref", "$.artifact-ref", issues
    )
    if artifact_ref is not None:
        _validate_artifact_ref(
            artifact_ref,
            expected_artifact_ref=expected_artifact_ref,
            issues=issues,
        )
    _required_digest(document, "request-digest", "$.request-digest", issues)
    validation_tree = _validate_validation_tree(
        document.get("validation-tree"),
        issues,
    )
    _validate_event(
        document.get("event"),
        mode,
        expected_run_id,
        expected_run_attempt,
        issues,
    )
    if mode == "scheduled_full":
        _validate_scheduled_full(document.get("scheduled-full"), issues)
    elif isinstance(mode, str) and mode in _AFFECTED_SOURCES:
        _validate_affected_range(
            document.get("affected-range"),
            mode,
            validation_tree,
            issues,
        )


def _validate_artifact_ref(
    artifact_ref: str,
    *,
    expected_artifact_ref: str,
    issues: list[ValidationIssue],
) -> None:
    try:
        validate_artifact_logical_ref(artifact_ref)
    except ContractValidationError as error:
        issues.extend(error.issues)
        return
    if artifact_ref != expected_artifact_ref:
        issues.append(
            ValidationIssue(
                "$.artifact-ref",
                f"must be {expected_artifact_ref}",
            ),
        )


def _validate_validation_tree(
    value: object,
    issues: list[ValidationIssue],
) -> Mapping[str, object] | None:
    tree = _required_mapping(value, "$.validation-tree", issues)
    if tree is None:
        return None
    _required_sha(tree, "commit-sha", "$.validation-tree.commit-sha", issues)
    _nullable_str(tree, "ref", "$.validation-tree.ref", issues)
    _reject_extra(tree, {"commit-sha", "ref"}, "$.validation-tree", issues)
    return tree


def _validate_event(
    value: object,
    mode: str | None,
    run_id: str,
    run_attempt: str,
    issues: list[ValidationIssue],
) -> None:
    event = _required_mapping(value, "$.event", issues)
    if event is None:
        return
    event_name = _required_str(event, "name", "$.event.name", issues)
    _nullable_str(event, "number", "$.event.number", issues)
    _required_str(event, "actor", "$.event.actor", issues)
    event_run_id = _required_str(event, "run-id", "$.event.run-id", issues)
    event_run_attempt = _required_str(
        event,
        "run-attempt",
        "$.event.run-attempt",
        issues,
    )
    if event_run_id is not None and event_run_id != run_id:
        issues.append(
            ValidationIssue("$.event.run-id", "must match run.run-id")
        )
    if event_run_attempt is not None and event_run_attempt != run_attempt:
        issues.append(
            ValidationIssue(
                "$.event.run-attempt", "must match run.run-attempt"
            ),
        )
    expected_event_name = _EVENT_NAME_BY_MODE.get(mode or "")
    if (
        event_name is not None
        and expected_event_name is not None
        and event_name != expected_event_name
    ):
        issues.append(
            ValidationIssue(
                "$.event.name",
                f"must be {expected_event_name} for mode {mode}",
            ),
        )
    _reject_extra(
        event,
        {"name", "number", "actor", "run-id", "run-attempt"},
        "$.event",
        issues,
    )


def _validate_scheduled_full(
    value: object,
    issues: list[ValidationIssue],
) -> None:
    scheduled = _required_mapping(value, "$.scheduled-full", issues)
    if scheduled is None:
        return
    if scheduled.get("enabled") is not True:
        issues.append(
            ValidationIssue("$.scheduled-full.enabled", "must be true")
        )
    _reject_extra(scheduled, {"enabled"}, "$.scheduled-full", issues)


def _validate_affected_range(
    value: object,
    mode: str,
    validation_tree: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> None:
    affected = _required_mapping(value, "$.affected-range", issues)
    if affected is None:
        return
    for key in sorted(_AFFECTED_RANGE_KEYS - set(affected)):
        issues.append(ValidationIssue(f"$.affected-range.{key}", "is required"))
    status = _required_enum(
        affected,
        "status",
        "$.affected-range.status",
        _AFFECTED_STATUSES,
        issues,
    )
    source = _required_enum(
        affected,
        "source",
        "$.affected-range.source",
        _AFFECTED_SOURCES,
        issues,
    )
    if source is not None and source != mode:
        issues.append(
            ValidationIssue("$.affected-range.source", "must match mode")
        )
    _nullable_sha(affected, "base-sha", "$.affected-range.base-sha", issues)
    _nullable_sha(
        affected,
        "base-tip-sha",
        "$.affected-range.base-tip-sha",
        issues,
    )
    _nullable_sha(affected, "head-sha", "$.affected-range.head-sha", issues)
    if mode == "push" and affected.get("base-tip-sha") is not None:
        issues.append(
            ValidationIssue(
                "$.affected-range.base-tip-sha", "must be null for push"
            ),
        )
    if status == "available":
        _validate_available_range(affected, mode, validation_tree, issues)
    elif status == "unavailable":
        _validate_unavailable_range(affected, issues)
    _reject_extra(
        affected,
        set(_AFFECTED_RANGE_KEYS),
        "$.affected-range",
        issues,
    )


def _validate_available_range(
    affected: Mapping[str, object],
    mode: str,
    validation_tree: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> None:
    for key in ("base-sha", "head-sha"):
        if affected.get(key) is None:
            issues.append(
                ValidationIssue(f"$.affected-range.{key}", "is required")
            )
    if mode == "pull_request" and affected.get("base-tip-sha") is None:
        issues.append(
            ValidationIssue("$.affected-range.base-tip-sha", "is required"),
        )
    if mode == "push":
        for key in ("base-sha", "head-sha"):
            if affected.get(key) == _ZERO_SHA:
                issues.append(
                    ValidationIssue(
                        f"$.affected-range.{key}",
                        "must not be all-zero for available push ranges",
                    ),
                )
    if (
        mode == "push"
        and validation_tree is not None
        and validation_tree.get("commit-sha") != affected.get("head-sha")
    ):
        issues.append(
            ValidationIssue(
                "$.validation-tree.commit-sha",
                "must match affected-range.head-sha for push",
            ),
        )
    if affected.get("diagnostic") is not None:
        issues.append(
            ValidationIssue("$.affected-range.diagnostic", "must be null")
        )
    if affected.get("diagnostic-detail") is not None:
        issues.append(
            ValidationIssue(
                "$.affected-range.diagnostic-detail", "must be null"
            ),
        )
    _validate_changed_files(affected.get("changed-files"), issues)


def _validate_unavailable_range(
    affected: Mapping[str, object],
    issues: list[ValidationIssue],
) -> None:
    if affected.get("changed-files") is not None:
        issues.append(
            ValidationIssue("$.affected-range.changed-files", "must be null")
        )
    if affected.get("diagnostic") != DiagnosticFamily.RANGE_UNCONFIRMED.value:
        issues.append(
            ValidationIssue(
                "$.affected-range.diagnostic",
                "must be range-unconfirmed",
            ),
        )
    detail = affected.get("diagnostic-detail")
    if detail not in _RANGE_DETAILS:
        issues.append(
            ValidationIssue(
                "$.affected-range.diagnostic-detail",
                "must be a range-unconfirmed detail",
            ),
        )


def _validate_changed_files(
    value: object,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(value, list):
        issues.append(
            ValidationIssue(
                "$.affected-range.changed-files", "must be an array"
            )
        )
        return
    previous: str | None = None
    seen: set[str] = set()
    for index, item in enumerate(value):
        path = f"$.affected-range.changed-files[{index}]"
        if not isinstance(item, str):
            issues.append(ValidationIssue(path, "must be a string"))
            continue
        if not _is_canonical_repo_path(item):
            issues.append(
                ValidationIssue(path, "must be a canonical repository path"),
            )
            continue
        if item in seen:
            issues.append(ValidationIssue(path, "must not be duplicated"))
        seen.add(item)
        if previous is not None and previous.encode() > item.encode():
            issues.append(
                ValidationIssue(
                    "$.affected-range.changed-files", "must be sorted"
                ),
            )
        previous = item


def _request_invalid_detail(issues: Sequence[ValidationIssue]) -> str:
    paths = {issue.path for issue in issues}
    if (
        "$.run.run-id" in paths
        or "$.run.run-attempt" in paths
        or "$.event.run-id" in paths
        or "$.event.run-attempt" in paths
    ):
        return DiagnosticDetail.REQUEST_WRONG_RUN_ATTEMPT.value
    if "$.artifact-ref" in paths or "artifact-ref" in paths:
        return DiagnosticDetail.REQUEST_REF_MISMATCH.value
    if "$.request-digest" in paths:
        return DiagnosticDetail.REQUEST_DIGEST_MISMATCH.value
    if any("schema-diagnostics" in path for path in paths):
        return DiagnosticDetail.REQUEST_SCHEMA_INVALID.value
    return DiagnosticDetail.REQUEST_SCHEMA_INVALID.value


def _required_mapping(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> Mapping[str, object] | None:
    if not isinstance(value, Mapping):
        issues.append(ValidationIssue(path, "must be an object"))
        return None
    return value


def _required_str(
    obj: Mapping[str, object],
    key: str,
    path: str,
    issues: list[ValidationIssue],
) -> str | None:
    if key not in obj:
        issues.append(ValidationIssue(path, "is required"))
        return None
    value = obj[key]
    if not isinstance(value, str) or value == "":
        issues.append(ValidationIssue(path, "must be a non-empty string"))
        return None
    return value


def _required_int_value(
    obj: Mapping[str, object],
    key: str,
    path: str,
    issues: list[ValidationIssue],
) -> int | None:
    if key not in obj:
        issues.append(ValidationIssue(path, "is required"))
        return None
    value = obj[key]
    if not isinstance(value, int) or isinstance(value, bool):
        issues.append(ValidationIssue(path, "must be an integer"))
        return None
    return value


def _nullable_str(
    obj: Mapping[str, object],
    key: str,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if key not in obj:
        issues.append(ValidationIssue(path, "is required"))
        return
    value = obj[key]
    if value is not None and (not isinstance(value, str) or value == ""):
        issues.append(
            ValidationIssue(path, "must be null or a non-empty string")
        )


def _required_enum(
    obj: Mapping[str, object],
    key: str,
    path: str,
    allowed: frozenset[str],
    issues: list[ValidationIssue],
) -> str | None:
    value = _required_str(obj, key, path, issues)
    if value is not None and value not in allowed:
        issues.append(
            ValidationIssue(path, f"must be one of {sorted(allowed)}")
        )
        return None
    return value


def _required_digest(
    obj: Mapping[str, object],
    key: str,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    value = _required_str(obj, key, path, issues)
    if value is not None and _DIGEST_RE.fullmatch(value) is None:
        issues.append(ValidationIssue(path, "must be 64 lowercase hex chars"))


def _required_sha(
    obj: Mapping[str, object],
    key: str,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    value = _required_str(obj, key, path, issues)
    if value is not None and _SHA_RE.fullmatch(value) is None:
        issues.append(
            ValidationIssue(path, "must be a 40-char lowercase hex SHA")
        )


def _nullable_sha(
    obj: Mapping[str, object],
    key: str,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if key not in obj:
        issues.append(ValidationIssue(path, "is required"))
        return
    value = obj[key]
    if value is None:
        return
    if not isinstance(value, str) or _SHA_RE.fullmatch(value) is None:
        issues.append(
            ValidationIssue(path, "must be null or a 40-char lowercase hex SHA")
        )


def _reject_extra(
    obj: Mapping[str, object],
    allowed: set[str],
    path: str,
    issues: list[ValidationIssue],
) -> None:
    for key in sorted(set(obj) - allowed):
        issues.append(ValidationIssue(f"{path}.{key}", "is not allowed"))


def _is_canonical_repo_path(value: str) -> bool:
    if _has_surrogate(value):
        return False
    if (
        value == ""
        or value.startswith(("/", "./"))
        or value.endswith("/")
        or "\\" in value
    ):
        return False
    return all(part not in {"", ".", ".."} for part in value.split("/"))


def _has_surrogate(value: str) -> bool:
    return any(_SURROGATE_MIN <= ord(char) <= _SURROGATE_MAX for char in value)
