"""Contract foundations for workflow-release CI affected validation."""

from __future__ import annotations

import hashlib
import json
import re
from collections.abc import Mapping, Sequence
from dataclasses import dataclass
from datetime import datetime
from enum import StrEnum

from three_workflow_release_contracts.contracts import (
    ContractValidationError,
    ValidationIssue,
)

CONTRACT_API_VERSION_PREFIX = "three.ci.validation."
ARTIFACT_PHYSICAL_NAME_PREFIX = "three-ci-validation-"
ARTIFACT_PHYSICAL_NAME_LENGTH = len(ARTIFACT_PHYSICAL_NAME_PREFIX) + 64
DIGEST_ALGORITHM = "sha256"
CANONICAL_JSON_PROFILE = "rfc8785-ijson-no-floats"
MAX_IJSON_SAFE_INTEGER = 9_007_199_254_740_991

_PHYSICAL_ARTIFACT_NAME_RE = re.compile(
    rf"^{re.escape(ARTIFACT_PHYSICAL_NAME_PREFIX)}[0-9a-f]{{64}}$",
)
_ARTIFACT_REF_SEGMENT_RE = re.compile(r"^[A-Za-z0-9._-]+$")
_RFC3339_TIMESTAMP_RE = re.compile(
    r"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}"
    r"(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$",
)
_SURROGATE_MIN = 0xD800
_SURROGATE_MAX = 0xDFFF


type JsonObject = Mapping[str, object]


class CiValidationKind(StrEnum):
    """Closed CI affected-validation contract kind vocabulary."""

    REQUEST = "ci-validation-request"
    PLAN = "ci-validation-plan"
    CHANGED_FILES_SNAPSHOT = "ci-validation-changed-files-snapshot"
    FACT_SNAPSHOT = "ci-validation-fact-snapshot"
    SELECTOR_ASSIGNMENTS = "ci-validation-selector-assignments"
    VALIDATION_RECEIPT = "ci-validation-receipt"
    WRITER_OBSERVATION = "ci-validation-writer-observation"
    RECEIPT_MANIFEST = "ci-validation-receipt-manifest"
    AGGREGATE = "ci-validation-aggregate"


class DiagnosticFamily(StrEnum):
    """Closed CI validation diagnostic-code family vocabulary."""

    REQUEST_INVALID = "request-invalid"
    RANGE_UNCONFIRMED = "range-unconfirmed"
    UNKNOWN_CHANGE = "unknown-change"
    SUBJECT_UNRESOLVED = "subject-unresolved"
    DEPENDENCY_IMPACT_INSUFFICIENT = "dependency-impact-insufficient"
    FACT_PROVIDER_INSUFFICIENT = "fact-provider-insufficient"
    NO_VALIDATION_CAPABILITY = "no-validation-capability"
    INFRASTRUCTURE_SURFACE_UNCLASSIFIED = "infrastructure-surface-unclassified"
    DESCRIPTOR_INVALID = "descriptor-invalid"
    ARTIFACT_SHAPE_UNCONFIRMED = "artifact-shape-unconfirmed"
    VALIDATION_WORK_FAILED = "validation-work-failed"
    VALIDATION_WORK_SKIPPED = "validation-work-skipped"
    KNOWN_NON_IMPACTING = "known-non-impacting"
    REQUIRED_EVIDENCE_MISSING = "required-evidence-missing"
    REQUIRED_EVIDENCE_SKIPPED = "required-evidence-skipped"
    INADMISSIBLE_RECEIPT = "inadmissible-receipt"
    FINAL_EVIDENCE_FAILURE = "final-evidence-failure"
    INVALID_PLAN = "invalid-plan"


class DiagnosticDetail(StrEnum):
    """Closed CI validation diagnostic detail vocabulary."""

    REQUEST_MISSING = "request-missing"
    REQUEST_DUPLICATE = "request-duplicate"
    REQUEST_UNREADABLE = "request-unreadable"
    REQUEST_MALFORMED = "request-malformed"
    REQUEST_SCHEMA_INVALID = "request-schema-invalid"
    REQUEST_REF_MISMATCH = "request-ref-mismatch"
    REQUEST_DIGEST_MISMATCH = "request-digest-mismatch"
    REQUEST_WRONG_RUN_ATTEMPT = "request-wrong-run-attempt"
    REQUEST_PRODUCER_UNVERIFIED = "request-producer-unverified"
    MISSING = "missing"
    INCOMPLETE = "incomplete"
    INCONSISTENT = "inconsistent"
    UNCONFIRMED_PROVENANCE = "unconfirmed-provenance"
    MALFORMED_ARTIFACT_REF = "malformed-artifact-ref"
    MALFORMED_RECEIPT = "malformed-receipt"
    WRONG_PLAN = "wrong-plan"
    UNKNOWN_WORK_GROUP = "unknown-work-group"
    MISMATCHED_WORK_GROUP = "mismatched-work-group"
    MISMATCHED_WRITER_IDENTITY = "mismatched-writer-identity"
    MISMATCHED_EVIDENCE_PAYLOAD = "mismatched-evidence-payload"
    MISMATCHED_OUTCOME = "mismatched-outcome"
    DUPLICATE_RECEIPT = "duplicate-receipt"
    UNSTABLE_ARTIFACT_INSTANCE_ID = "unstable-artifact-instance-id"
    UNEXPECTED_RECEIPT = "unexpected-receipt"
    PLAN_UNREADABLE = "plan-unreadable"
    PLAN_MISSING = "plan-missing"
    PLAN_DUPLICATE = "plan-duplicate"
    MALFORMED_PLAN = "malformed-plan"
    SCHEMA_INVALID = "schema-invalid"
    PLAN_PRODUCER_UNVERIFIED = "plan-producer-unverified"
    PLAN_DIGEST_MISMATCH = "plan-digest-mismatch"
    SUBJECT_UNIVERSE_DIGEST_MISMATCH = "subject-universe-digest-mismatch"
    CHANGED_FILES_IMPACT_COVERAGE_MISMATCH = (
        "changed-files-impact-coverage-mismatch"
    )
    CHANGED_FILES_SNAPSHOT_MISSING = "changed-files-snapshot-missing"
    CHANGED_FILES_SNAPSHOT_UNEXPECTED = "changed-files-snapshot-unexpected"
    CHANGED_FILES_SNAPSHOT_DUPLICATE = "changed-files-snapshot-duplicate"
    CHANGED_FILES_SNAPSHOT_PRODUCER_UNVERIFIED = (
        "changed-files-snapshot-producer-unverified"
    )
    CHANGED_FILES_SNAPSHOT_UNREADABLE = "changed-files-snapshot-unreadable"
    CHANGED_FILES_SNAPSHOT_MALFORMED = "changed-files-snapshot-malformed"
    CHANGED_FILES_SNAPSHOT_SCHEMA_INVALID = (
        "changed-files-snapshot-schema-invalid"
    )
    CHANGED_FILES_SNAPSHOT_REF_MISMATCH = "changed-files-snapshot-ref-mismatch"
    CHANGED_FILES_SNAPSHOT_ENVELOPE_MISMATCH = (
        "changed-files-snapshot-envelope-mismatch"
    )
    CHANGED_FILES_SNAPSHOT_NONCANONICAL = "changed-files-snapshot-noncanonical"
    CHANGED_FILES_SNAPSHOT_DIGEST_MISMATCH = (
        "changed-files-snapshot-digest-mismatch"
    )
    FACT_SNAPSHOT_MISSING = "fact-snapshot-missing"
    FACT_SNAPSHOT_UNEXPECTED = "fact-snapshot-unexpected"
    FACT_SNAPSHOT_DUPLICATE = "fact-snapshot-duplicate"
    FACT_SNAPSHOT_PRODUCER_UNVERIFIED = "fact-snapshot-producer-unverified"
    FACT_SNAPSHOT_UNREADABLE = "fact-snapshot-unreadable"
    FACT_SNAPSHOT_MALFORMED = "fact-snapshot-malformed"
    FACT_SNAPSHOT_SCHEMA_INVALID = "fact-snapshot-schema-invalid"
    FACT_SNAPSHOT_REF_MISMATCH = "fact-snapshot-ref-mismatch"
    FACT_SNAPSHOT_ENVELOPE_MISMATCH = "fact-snapshot-envelope-mismatch"
    FACT_SNAPSHOT_PLAN_MISMATCH = "fact-snapshot-plan-mismatch"
    FACT_SNAPSHOT_CROSS_REFERENCE_INVALID = (
        "fact-snapshot-cross-reference-invalid"
    )
    FACT_SNAPSHOT_NONCANONICAL = "fact-snapshot-noncanonical"
    FACT_SNAPSHOT_DIGEST_MISMATCH = "fact-snapshot-digest-mismatch"
    SELECTOR_ASSIGNMENT_MISSING = "selector-assignment-missing"
    SELECTOR_ASSIGNMENT_DUPLICATE = "selector-assignment-duplicate"
    SELECTOR_ASSIGNMENT_UNREADABLE = "selector-assignment-unreadable"
    SELECTOR_ASSIGNMENT_MALFORMED = "selector-assignment-malformed"
    SELECTOR_ASSIGNMENT_SCHEMA_INVALID = "selector-assignment-schema-invalid"
    SELECTOR_ASSIGNMENT_PLAN_MISMATCH = "selector-assignment-plan-mismatch"
    SELECTOR_ASSIGNMENT_PRODUCER_UNVERIFIED = (
        "selector-assignment-producer-unverified"
    )
    SELECTOR_ASSIGNMENT_STRUCTURALLY_INVALID = (
        "selector-assignment-structurally-invalid"
    )
    STRUCTURALLY_INVALID = "structurally-invalid"
    BUILD = "build"
    TEST = "test"
    LINT = "lint"
    FORMAT = "format"
    TYPE_CHECK = "type-check"
    TOOLING = "tooling"
    DEPENDENCY_BLOCKED = "dependency-blocked"
    FINAL_MANIFEST_MISSING = "final-manifest-missing"
    FINAL_MANIFEST_DUPLICATE = "final-manifest-duplicate"
    FINAL_MANIFEST_UNREADABLE = "final-manifest-unreadable"
    FINAL_MANIFEST_MALFORMED = "final-manifest-malformed"
    FINAL_MANIFEST_NON_CANONICAL = "final-manifest-non-canonical"
    FINAL_MANIFEST_DIGEST_MISMATCH = "final-manifest-digest-mismatch"
    FINAL_AGGREGATE_MISSING = "final-aggregate-missing"
    FINAL_AGGREGATE_DUPLICATE = "final-aggregate-duplicate"
    FINAL_AGGREGATE_UNREADABLE = "final-aggregate-unreadable"
    FINAL_AGGREGATE_MALFORMED = "final-aggregate-malformed"
    FINAL_AGGREGATE_NON_CANONICAL = "final-aggregate-non-canonical"
    FINAL_AGGREGATE_DIGEST_MISMATCH = "final-aggregate-digest-mismatch"
    FINAL_PRODUCER_UNVERIFIED = "final-producer-unverified"
    FINAL_NAMESPACE_CLOSURE_MISMATCH = "final-namespace-closure-mismatch"
    AGGREGATE_WITHOUT_MANIFEST = "aggregate-without-manifest"


class DiagnosticSeverity(StrEnum):
    """Closed diagnostic severity vocabulary."""

    INFO = "info"
    WARNING = "warning"
    FAIL_CLOSED = "fail-closed"
    BLOCKING_FAILURE = "blocking-failure"


class DiagnosticVerdictEffect(StrEnum):
    """Closed diagnostic verdict-effect vocabulary."""

    NONE = "none"
    FAIL_CLOSED = "fail-closed"
    FAILED = "failed"


REGISTERED_CI_VALIDATION_KINDS = frozenset(
    item.value for item in CiValidationKind.__members__.values()
)
REGISTERED_CI_VALIDATION_DIAGNOSTIC_FAMILIES = frozenset(
    item.value for item in DiagnosticFamily.__members__.values()
)
REGISTERED_CI_VALIDATION_DIAGNOSTIC_CODES = (
    REGISTERED_CI_VALIDATION_DIAGNOSTIC_FAMILIES
)
REGISTERED_CI_VALIDATION_DIAGNOSTIC_DETAILS = frozenset(
    item.value for item in DiagnosticDetail.__members__.values()
)
DETAILS_BY_DIAGNOSTIC_CODE = {
    DiagnosticFamily.REQUEST_INVALID.value: frozenset(
        {
            DiagnosticDetail.REQUEST_MISSING.value,
            DiagnosticDetail.REQUEST_DUPLICATE.value,
            DiagnosticDetail.REQUEST_UNREADABLE.value,
            DiagnosticDetail.REQUEST_MALFORMED.value,
            DiagnosticDetail.REQUEST_SCHEMA_INVALID.value,
            DiagnosticDetail.REQUEST_REF_MISMATCH.value,
            DiagnosticDetail.REQUEST_DIGEST_MISMATCH.value,
            DiagnosticDetail.REQUEST_WRONG_RUN_ATTEMPT.value,
            DiagnosticDetail.REQUEST_PRODUCER_UNVERIFIED.value,
        },
    ),
    DiagnosticFamily.RANGE_UNCONFIRMED.value: frozenset(
        {
            DiagnosticDetail.MISSING.value,
            DiagnosticDetail.INCOMPLETE.value,
            DiagnosticDetail.INCONSISTENT.value,
            DiagnosticDetail.UNCONFIRMED_PROVENANCE.value,
        },
    ),
    DiagnosticFamily.INADMISSIBLE_RECEIPT.value: frozenset(
        {
            DiagnosticDetail.MALFORMED_ARTIFACT_REF.value,
            DiagnosticDetail.MALFORMED_RECEIPT.value,
            DiagnosticDetail.WRONG_PLAN.value,
            DiagnosticDetail.UNKNOWN_WORK_GROUP.value,
            DiagnosticDetail.MISMATCHED_WORK_GROUP.value,
            DiagnosticDetail.MISMATCHED_WRITER_IDENTITY.value,
            DiagnosticDetail.MISMATCHED_EVIDENCE_PAYLOAD.value,
            DiagnosticDetail.MISMATCHED_OUTCOME.value,
            DiagnosticDetail.DUPLICATE_RECEIPT.value,
            DiagnosticDetail.UNSTABLE_ARTIFACT_INSTANCE_ID.value,
            DiagnosticDetail.UNEXPECTED_RECEIPT.value,
        },
    ),
    DiagnosticFamily.INVALID_PLAN.value: frozenset(
        {
            DiagnosticDetail.PLAN_UNREADABLE.value,
            DiagnosticDetail.PLAN_MISSING.value,
            DiagnosticDetail.PLAN_DUPLICATE.value,
            DiagnosticDetail.MALFORMED_PLAN.value,
            DiagnosticDetail.SCHEMA_INVALID.value,
            DiagnosticDetail.PLAN_PRODUCER_UNVERIFIED.value,
            DiagnosticDetail.PLAN_DIGEST_MISMATCH.value,
            DiagnosticDetail.SUBJECT_UNIVERSE_DIGEST_MISMATCH.value,
            DiagnosticDetail.CHANGED_FILES_IMPACT_COVERAGE_MISMATCH.value,
            DiagnosticDetail.CHANGED_FILES_SNAPSHOT_MISSING.value,
            DiagnosticDetail.CHANGED_FILES_SNAPSHOT_UNEXPECTED.value,
            DiagnosticDetail.CHANGED_FILES_SNAPSHOT_DUPLICATE.value,
            DiagnosticDetail.CHANGED_FILES_SNAPSHOT_PRODUCER_UNVERIFIED.value,
            DiagnosticDetail.CHANGED_FILES_SNAPSHOT_UNREADABLE.value,
            DiagnosticDetail.CHANGED_FILES_SNAPSHOT_MALFORMED.value,
            DiagnosticDetail.CHANGED_FILES_SNAPSHOT_SCHEMA_INVALID.value,
            DiagnosticDetail.CHANGED_FILES_SNAPSHOT_REF_MISMATCH.value,
            DiagnosticDetail.CHANGED_FILES_SNAPSHOT_ENVELOPE_MISMATCH.value,
            DiagnosticDetail.CHANGED_FILES_SNAPSHOT_NONCANONICAL.value,
            DiagnosticDetail.CHANGED_FILES_SNAPSHOT_DIGEST_MISMATCH.value,
            DiagnosticDetail.FACT_SNAPSHOT_MISSING.value,
            DiagnosticDetail.FACT_SNAPSHOT_UNEXPECTED.value,
            DiagnosticDetail.FACT_SNAPSHOT_DUPLICATE.value,
            DiagnosticDetail.FACT_SNAPSHOT_PRODUCER_UNVERIFIED.value,
            DiagnosticDetail.FACT_SNAPSHOT_UNREADABLE.value,
            DiagnosticDetail.FACT_SNAPSHOT_MALFORMED.value,
            DiagnosticDetail.FACT_SNAPSHOT_SCHEMA_INVALID.value,
            DiagnosticDetail.FACT_SNAPSHOT_REF_MISMATCH.value,
            DiagnosticDetail.FACT_SNAPSHOT_ENVELOPE_MISMATCH.value,
            DiagnosticDetail.FACT_SNAPSHOT_PLAN_MISMATCH.value,
            DiagnosticDetail.FACT_SNAPSHOT_CROSS_REFERENCE_INVALID.value,
            DiagnosticDetail.FACT_SNAPSHOT_NONCANONICAL.value,
            DiagnosticDetail.FACT_SNAPSHOT_DIGEST_MISMATCH.value,
            DiagnosticDetail.SELECTOR_ASSIGNMENT_MISSING.value,
            DiagnosticDetail.SELECTOR_ASSIGNMENT_DUPLICATE.value,
            DiagnosticDetail.SELECTOR_ASSIGNMENT_UNREADABLE.value,
            DiagnosticDetail.SELECTOR_ASSIGNMENT_MALFORMED.value,
            DiagnosticDetail.SELECTOR_ASSIGNMENT_SCHEMA_INVALID.value,
            DiagnosticDetail.SELECTOR_ASSIGNMENT_PLAN_MISMATCH.value,
            DiagnosticDetail.SELECTOR_ASSIGNMENT_PRODUCER_UNVERIFIED.value,
            DiagnosticDetail.SELECTOR_ASSIGNMENT_STRUCTURALLY_INVALID.value,
            DiagnosticDetail.STRUCTURALLY_INVALID.value,
        },
    ),
    DiagnosticFamily.VALIDATION_WORK_FAILED.value: frozenset(
        {
            DiagnosticDetail.BUILD.value,
            DiagnosticDetail.TEST.value,
            DiagnosticDetail.LINT.value,
            DiagnosticDetail.FORMAT.value,
            DiagnosticDetail.TYPE_CHECK.value,
            DiagnosticDetail.TOOLING.value,
        },
    ),
    DiagnosticFamily.VALIDATION_WORK_SKIPPED.value: frozenset(
        {DiagnosticDetail.DEPENDENCY_BLOCKED.value},
    ),
    DiagnosticFamily.FINAL_EVIDENCE_FAILURE.value: frozenset(
        {
            DiagnosticDetail.FINAL_MANIFEST_MISSING.value,
            DiagnosticDetail.FINAL_MANIFEST_DUPLICATE.value,
            DiagnosticDetail.FINAL_MANIFEST_UNREADABLE.value,
            DiagnosticDetail.FINAL_MANIFEST_MALFORMED.value,
            DiagnosticDetail.FINAL_MANIFEST_NON_CANONICAL.value,
            DiagnosticDetail.FINAL_MANIFEST_DIGEST_MISMATCH.value,
            DiagnosticDetail.FINAL_AGGREGATE_MISSING.value,
            DiagnosticDetail.FINAL_AGGREGATE_DUPLICATE.value,
            DiagnosticDetail.FINAL_AGGREGATE_UNREADABLE.value,
            DiagnosticDetail.FINAL_AGGREGATE_MALFORMED.value,
            DiagnosticDetail.FINAL_AGGREGATE_NON_CANONICAL.value,
            DiagnosticDetail.FINAL_AGGREGATE_DIGEST_MISMATCH.value,
            DiagnosticDetail.FINAL_PRODUCER_UNVERIFIED.value,
            DiagnosticDetail.FINAL_NAMESPACE_CLOSURE_MISMATCH.value,
            DiagnosticDetail.AGGREGATE_WITHOUT_MANIFEST.value,
        },
    ),
}

API_VERSIONS_BY_KIND = {
    CiValidationKind.REQUEST.value: "three.ci.validation.request/v1alpha1",
    CiValidationKind.PLAN.value: "three.ci.validation.plan/v1alpha1",
    CiValidationKind.CHANGED_FILES_SNAPSHOT.value: (
        "three.ci.validation.changed-files/v1alpha1"
    ),
    CiValidationKind.FACT_SNAPSHOT.value: (
        "three.ci.validation.fact-snapshot/v1alpha1"
    ),
    CiValidationKind.SELECTOR_ASSIGNMENTS.value: (
        "three.ci.validation.selector-assignments/v1alpha1"
    ),
    CiValidationKind.VALIDATION_RECEIPT.value: (
        "three.ci.validation.receipt/v1alpha1"
    ),
    CiValidationKind.WRITER_OBSERVATION.value: (
        "three.ci.validation.writer-observation/v1alpha1"
    ),
    CiValidationKind.RECEIPT_MANIFEST.value: (
        "three.ci.validation.receipt-manifest/v1alpha1"
    ),
    CiValidationKind.AGGREGATE.value: "three.ci.validation.aggregate/v1alpha1",
}


@dataclass(frozen=True, slots=True)
class ArtifactPhysicalName:
    """Logical artifact ref and its fixed GitHub artifact name."""

    logical_ref: str
    physical_name: str


@dataclass(frozen=True, slots=True)
class CommonEnvelope:
    """Minimum common envelope shared by CI validation artifacts."""

    api_version: str
    kind: str
    created_at: str
    repository_owner: str
    repository_name: str
    workflow: str
    run_id: str
    run_attempt: str


def canonical_json_bytes(value: object) -> bytes:
    """Return deterministic UTF-8 JSON bytes for the supported JCS subset.

    This helper intentionally supports the CI validation contract's I-JSON data
    model and rejects floats instead of pretending to implement full RFC 8785
    number serialization. Object keys are ordered by UTF-16BE code units to
    match the RFC 8785 member ordering rule for valid Unicode strings.
    """
    return _canonical_json_text(value).encode("utf-8")


def canonical_json_digest(value: object) -> str:
    """Return lowercase SHA-256 over :func:`canonical_json_bytes`."""
    return hashlib.sha256(canonical_json_bytes(value)).hexdigest()


def artifact_physical_name(logical_ref: str) -> str:
    """Map a logical CI validation artifact ref to its physical name."""
    validate_artifact_logical_ref(logical_ref)
    digest = hashlib.sha256(logical_ref.encode("utf-8")).hexdigest()
    return f"{ARTIFACT_PHYSICAL_NAME_PREFIX}{digest}"


def artifact_physical_ref(logical_ref: str) -> ArtifactPhysicalName:
    """Return the logical/physical artifact ref pair."""
    return ArtifactPhysicalName(
        logical_ref=logical_ref,
        physical_name=artifact_physical_name(logical_ref),
    )


def validate_artifact_logical_ref(logical_ref: object) -> None:
    """Validate the canonical logical artifact-ref path shape."""
    issues: list[ValidationIssue] = []
    if not isinstance(logical_ref, str):
        issues.append(ValidationIssue("artifact-ref", "must be a string"))
    elif not _is_artifact_logical_ref(logical_ref):
        issues.append(
            ValidationIssue(
                "artifact-ref",
                "must be a normalized ci-validation logical artifact ref",
            ),
        )
    if issues:
        raise ContractValidationError(issues)


def validate_artifact_physical_name(physical_name: object) -> None:
    """Validate a fixed-length CI validation physical artifact name."""
    issues: list[ValidationIssue] = []
    if not isinstance(physical_name, str):
        issues.append(
            ValidationIssue("physical-artifact-name", "must be a string"),
        )
    elif _PHYSICAL_ARTIFACT_NAME_RE.fullmatch(physical_name) is None:
        issues.append(
            ValidationIssue(
                "physical-artifact-name",
                "must be three-ci-validation- followed by 64 lowercase "
                "hex chars",
            ),
        )
    if issues:
        raise ContractValidationError(issues)


def validate_common_envelope(
    document: JsonObject,
    *,
    api_version: str,
    kind: CiValidationKind | str,
) -> CommonEnvelope:
    """Validate and return the minimum CI validation common envelope."""
    expected_kind = kind.value if isinstance(kind, CiValidationKind) else kind
    issues: list[ValidationIssue] = []
    if not isinstance(document, Mapping):
        raise ContractValidationError(
            [ValidationIssue("$", "must be an object")],
        )
    _require_string(document, "api-version", "$", issues)
    document_kind = _require_string(document, "kind", "$", issues)
    created_at = _require_string(document, "created-at", "$", issues)
    if created_at:
        _validate_rfc3339_timestamp(created_at, "$.created-at", issues)
    repository = _require_mapping(document, "repository", "$", issues)
    run = _require_mapping(document, "run", "$", issues)
    _validate_schema_diagnostics(
        document.get("schema-diagnostics"),
        "$.schema-diagnostics",
        issues,
    )
    registered_api_version = API_VERSIONS_BY_KIND.get(document_kind)
    expected_api_version = API_VERSIONS_BY_KIND.get(expected_kind)
    if registered_api_version is None and isinstance(document_kind, str):
        issues.append(ValidationIssue("$.kind", "is not registered"))
    if expected_api_version is None:
        issues.append(
            ValidationIssue("$.kind", "expected kind is not registered"),
        )
    elif api_version != expected_api_version:
        issues.append(
            ValidationIssue(
                "$.api-version",
                f"expected api-version must be {expected_api_version}",
            ),
        )
    if (
        registered_api_version is not None
        and document.get("api-version") != registered_api_version
    ):
        issues.append(
            ValidationIssue(
                "$.api-version",
                f"must be {registered_api_version}",
            ),
        )
    if document.get("kind") != expected_kind:
        issues.append(ValidationIssue("$.kind", f"must be {expected_kind}"))
    owner = _mapping_string(repository, "owner", "$.repository", issues)
    name = _mapping_string(repository, "name", "$.repository", issues)
    workflow = _mapping_string(run, "workflow", "$.run", issues)
    run_id = _mapping_string(run, "run-id", "$.run", issues)
    run_attempt = _mapping_string(run, "run-attempt", "$.run", issues)
    if issues:
        raise ContractValidationError(issues)
    return CommonEnvelope(
        api_version=api_version,
        kind=expected_kind,
        created_at=str(document["created-at"]),
        repository_owner=owner,
        repository_name=name,
        workflow=workflow,
        run_id=run_id,
        run_attempt=run_attempt,
    )


def _canonical_json_text(value: object) -> str:  # noqa: PLR0911
    """Serialize one supported JSON value to canonical text."""
    if value is None:
        return "null"
    if value is True:
        return "true"
    if value is False:
        return "false"
    if isinstance(value, int) and not isinstance(value, bool):
        if abs(value) > MAX_IJSON_SAFE_INTEGER:
            msg = "integers must be within the I-JSON safe range"
            raise ValueError(msg)
        return str(value)
    if isinstance(value, float):
        msg = "floats are not supported by this canonical JSON profile"
        raise TypeError(msg)
    if isinstance(value, str):
        _reject_surrogates(value)
        return json.dumps(value, ensure_ascii=False, separators=(",", ":"))
    if isinstance(value, Sequence) and not isinstance(
        value,
        str | bytes | bytearray,
    ):
        items = ",".join(_canonical_json_text(item) for item in value)
        return f"[{items}]"
    if isinstance(value, Mapping):
        return _canonical_json_object_text(value)
    msg = f"unsupported JSON value type: {type(value).__name__}"
    raise TypeError(msg)


def _reject_surrogates(value: str) -> None:
    """Reject strings that cannot be represented as valid Unicode scalars."""
    if any(_SURROGATE_MIN <= ord(char) <= _SURROGATE_MAX for char in value):
        msg = "strings must not contain Unicode surrogate code points"
        raise ValueError(msg)


def _canonical_json_object_text(value: Mapping[object, object]) -> str:
    """Serialize one JSON object to canonical text."""
    items: list[tuple[str, object]] = []
    for key, item in value.items():
        if not isinstance(key, str):
            msg = "object keys must be strings"
            raise TypeError(msg)
        _reject_surrogates(key)
        items.append((key, item))
    items.sort(key=lambda pair: pair[0].encode("utf-16-be"))
    serialized = ",".join(
        f"{_canonical_json_text(key)}:{_canonical_json_text(item)}"
        for key, item in items
    )
    return f"{{{serialized}}}"


def _is_artifact_logical_ref(value: str) -> bool:
    """Return whether *value* is a canonical CI validation artifact ref."""
    if not value.startswith("ci-validation/") or value.endswith("/"):
        return False
    parts = value.split("/")
    return all(
        part not in {"", ".", ".."}
        and _ARTIFACT_REF_SEGMENT_RE.fullmatch(part) is not None
        for part in parts
    )


def _require_mapping(
    document: Mapping[str, object],
    key: str,
    path: str,
    issues: list[ValidationIssue],
) -> Mapping[str, object] | None:
    """Require one child mapping from *document*."""
    value = document.get(key)
    if not isinstance(value, Mapping):
        issues.append(ValidationIssue(f"{path}.{key}", "must be an object"))
        return None
    return value


def _require_string(
    document: Mapping[str, object],
    key: str,
    path: str,
    issues: list[ValidationIssue],
) -> str:
    """Require one non-empty string from *document*."""
    value = document.get(key)
    if not isinstance(value, str) or value == "":
        issues.append(ValidationIssue(f"{path}.{key}", "must be a string"))
        return ""
    return value


def _validate_rfc3339_timestamp(
    value: str,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    """Validate the RFC 3339 timestamp syntax used by the common envelope."""
    if _RFC3339_TIMESTAMP_RE.fullmatch(value) is None:
        issues.append(ValidationIssue(path, "must be an RFC 3339 timestamp"))
        return
    try:
        datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        issues.append(ValidationIssue(path, "must be an RFC 3339 timestamp"))


def _mapping_string(
    document: Mapping[str, object] | None,
    key: str,
    path: str,
    issues: list[ValidationIssue],
) -> str:
    """Require a child string when the parent mapping exists."""
    if document is None:
        return ""
    return _require_string(document, key, path, issues)


def _validate_schema_diagnostics(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    """Validate sorted schema diagnostics."""
    if not isinstance(value, list):
        issues.append(ValidationIssue(path, "must be an array"))
        return
    previous_id: str | None = None
    for index, diagnostic in enumerate(value):
        item_path = f"{path}[{index}]"
        _validate_schema_diagnostic(diagnostic, item_path, issues)
        if not isinstance(diagnostic, Mapping):
            continue
        diagnostic_id = diagnostic.get("diagnostic-id")
        if not isinstance(diagnostic_id, str):
            continue
        if previous_id is not None and previous_id > diagnostic_id:
            issues.append(
                ValidationIssue(path, "must be sorted by diagnostic-id"),
            )
        previous_id = diagnostic_id


def _validate_schema_diagnostic(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    """Validate the warning/info-only schema diagnostic subset."""
    if not isinstance(value, Mapping):
        issues.append(ValidationIssue(path, "must be an object"))
        return
    required = (
        "diagnostic-id",
        "code",
        "detail",
        "message",
        "source",
        "severity",
        "verdict-effect",
    )
    code = value.get("code")
    for key in required:
        if key not in value:
            issues.append(ValidationIssue(f"{path}.{key}", "is required"))
            continue
        if key == "detail":
            _nullable_registered_detail(
                value[key],
                f"{path}.{key}",
                str(code) if isinstance(code, str) else "",
                issues,
            )
        elif key == "message":
            _nullable_string(value[key], f"{path}.{key}", issues)
        elif key == "source":
            _validate_diagnostic_source(value[key], f"{path}.{key}", issues)
        else:
            _require_string(value, key, path, issues)
    code = value.get("code")
    if (
        isinstance(code, str)
        and code not in REGISTERED_CI_VALIDATION_DIAGNOSTIC_CODES
    ):
        issues.append(ValidationIssue(f"{path}.code", "is not registered"))
    if value.get("severity") not in {
        DiagnosticSeverity.WARNING.value,
        DiagnosticSeverity.INFO.value,
    }:
        issues.append(
            ValidationIssue(f"{path}.severity", "must be warning or info"),
        )
    if value.get("verdict-effect") != DiagnosticVerdictEffect.NONE.value:
        issues.append(
            ValidationIssue(f"{path}.verdict-effect", "must be none"),
        )


def _nullable_string(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    """Validate a nullable string value."""
    if value is None:
        return
    if not isinstance(value, str) or value == "":
        issues.append(ValidationIssue(path, "must be null or a string"))


def _nullable_registered_detail(
    value: object,
    path: str,
    code: str,
    issues: list[ValidationIssue],
) -> None:
    """Validate a nullable diagnostic detail for one registered code."""
    if value is None:
        return
    if not isinstance(value, str) or value == "":
        issues.append(ValidationIssue(path, "must be null or a string"))
        return
    if value not in REGISTERED_CI_VALIDATION_DIAGNOSTIC_DETAILS:
        issues.append(ValidationIssue(path, "is not registered"))
        return
    if value not in DETAILS_BY_DIAGNOSTIC_CODE.get(code, frozenset()):
        issues.append(
            ValidationIssue(path, "is not valid for this diagnostic code"),
        )


def _validate_diagnostic_source(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    """Validate a diagnostic-record source object."""
    if not isinstance(value, Mapping):
        issues.append(ValidationIssue(path, "must be an object"))
        return
    source_type = _require_string(value, "type", path, issues)
    if source_type not in {
        "request",
        "impact",
        "subject",
        "descriptor",
        "fact-provider",
        "work-group",
        "aggregation",
    }:
        issues.append(ValidationIssue(f"{path}.type", "is not registered"))
    if "id" not in value:
        issues.append(ValidationIssue(f"{path}.id", "is required"))
    else:
        _nullable_string(value["id"], f"{path}.id", issues)
