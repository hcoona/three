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
ARTIFACT_PHYSICAL_NAME_LENGTH = None
DIGEST_ALGORITHM = "sha256"
CANONICAL_JSON_PROFILE = "rfc8785-ijson-no-floats"
MAX_IJSON_SAFE_INTEGER = 9_007_199_254_740_991

_PHYSICAL_ARTIFACT_NAME_RE = re.compile(
    rf"^{re.escape(ARTIFACT_PHYSICAL_NAME_PREFIX)}"
    r"(?P<run_id>[A-Za-z0-9._-]+)-(?P<run_attempt>[A-Za-z0-9._-]+)-"
    r"(?P<digest>[0-9a-f]{64})$",
)
_LOGICAL_REF_ATTEMPT_RE = re.compile(
    r"^ci-validation/[^/]+/(?P<run_id>[A-Za-z0-9._-]+)/"
    r"(?P<run_attempt>[A-Za-z0-9._-]+)/"
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
    EXECUTION_BATCH_MANIFEST = "ci-validation-execution-batch-manifest"
    BATCH_EVIDENCE_BUNDLE = "ci-validation-batch-evidence-bundle"
    AGGREGATE_EVIDENCE_MANIFEST = "ci-validation-aggregate-evidence-manifest"
    AGGREGATE_SUMMARY = "ci-validation-aggregate-summary"


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
    BLOCKING_VALIDATION_FAILURE = "blocking-validation-failure"
    INADMISSIBLE_BATCH_EVIDENCE = "inadmissible-batch-evidence"
    NAMESPACE_CLOSURE_FAILURE = "namespace-closure-failure"
    REQUIRED_INPUT_ARTIFACT_FAILURE = "required-input-artifact-failure"
    AGGREGATE_SUMMARY_WITHOUT_MANIFEST = "aggregate-summary-without-manifest"
    FINAL_PRODUCER_UNVERIFIED = "final-producer-unverified"
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
    SNAPSHOT_COMPANION_UNPROVEN = "snapshot-companion-unproven"
    STRUCTURALLY_INVALID = "structurally-invalid"
    BUILD = "build"
    TEST = "test"
    LINT = "lint"
    FORMAT = "format"
    TYPE_CHECK = "type-check"
    TOOLING = "tooling"
    DEPENDENCY_BLOCKED = "dependency-blocked"
    MALFORMED_BUNDLE = "malformed-bundle"
    MISSING_BUNDLE = "missing-bundle"
    DUPLICATE_BUNDLE_CANDIDATES = "duplicate-bundle-candidates"
    BUNDLE_PRODUCER_UNVERIFIED = "bundle-producer-unverified"
    BUNDLE_METADATA_AUTHORITY_INVALID = "bundle-metadata-authority-invalid"
    UNEXPECTED_CONTRACT_ARTIFACT = "unexpected-contract-artifact"
    EXECUTION_BATCH_MANIFEST_MISSING = "execution-batch-manifest-missing"
    EXECUTION_BATCH_MANIFEST_DUPLICATE = "execution-batch-manifest-duplicate"
    EXECUTION_BATCH_MANIFEST_UNREADABLE = "execution-batch-manifest-unreadable"
    EXECUTION_BATCH_MANIFEST_MALFORMED = "execution-batch-manifest-malformed"
    EXECUTION_BATCH_MANIFEST_NON_CANONICAL = (
        "execution-batch-manifest-non-canonical"
    )
    EXECUTION_BATCH_MANIFEST_DIGEST_MISMATCH = (
        "execution-batch-manifest-digest-mismatch"
    )
    EXECUTION_BATCH_MANIFEST_PLAN_MISMATCH = (
        "execution-batch-manifest-plan-mismatch"
    )
    EXECUTION_BATCH_MANIFEST_BUNDLE_REF_MISMATCH = (
        "execution-batch-manifest-bundle-ref-mismatch"
    )
    BLOCKING_VALIDATION_FAILURE = "blocking-validation-failure"
    NAMESPACE_ENUMERATION_UNAVAILABLE = "namespace-enumeration-unavailable"
    REQUIRED_INPUT_ARTIFACT_FAILURE = "required-input-artifact-failure"
    AGGREGATE_EVIDENCE_MANIFEST_MISSING = "aggregate-evidence-manifest-missing"
    AGGREGATE_EVIDENCE_MANIFEST_DUPLICATE = (
        "aggregate-evidence-manifest-duplicate"
    )
    AGGREGATE_EVIDENCE_MANIFEST_UNREADABLE = (
        "aggregate-evidence-manifest-unreadable"
    )
    AGGREGATE_EVIDENCE_MANIFEST_MALFORMED = (
        "aggregate-evidence-manifest-malformed"
    )
    AGGREGATE_EVIDENCE_MANIFEST_NON_CANONICAL = (
        "aggregate-evidence-manifest-non-canonical"
    )
    AGGREGATE_EVIDENCE_MANIFEST_DIGEST_MISMATCH = (
        "aggregate-evidence-manifest-digest-mismatch"
    )
    FINAL_PRODUCER_UNVERIFIED = "final-producer-unverified"
    AGGREGATE_SUMMARY_WITHOUT_MANIFEST = "aggregate-summary-without-manifest"
    NAMESPACE_OVERFLOW = "namespace-overflow"


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
_COMMON_DIAGNOSTIC_DETAILS = frozenset(
    {
        DiagnosticDetail.MISSING.value,
        DiagnosticDetail.INCOMPLETE.value,
        DiagnosticDetail.INCONSISTENT.value,
        DiagnosticDetail.UNCONFIRMED_PROVENANCE.value,
    },
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
    DiagnosticFamily.RANGE_UNCONFIRMED.value: _COMMON_DIAGNOSTIC_DETAILS,
    DiagnosticFamily.UNKNOWN_CHANGE.value: _COMMON_DIAGNOSTIC_DETAILS,
    DiagnosticFamily.SUBJECT_UNRESOLVED.value: _COMMON_DIAGNOSTIC_DETAILS,
    DiagnosticFamily.DEPENDENCY_IMPACT_INSUFFICIENT.value: (
        _COMMON_DIAGNOSTIC_DETAILS
    ),
    DiagnosticFamily.FACT_PROVIDER_INSUFFICIENT.value: (
        _COMMON_DIAGNOSTIC_DETAILS
    ),
    DiagnosticFamily.NO_VALIDATION_CAPABILITY.value: _COMMON_DIAGNOSTIC_DETAILS,
    DiagnosticFamily.INFRASTRUCTURE_SURFACE_UNCLASSIFIED.value: (
        _COMMON_DIAGNOSTIC_DETAILS
    ),
    DiagnosticFamily.DESCRIPTOR_INVALID.value: _COMMON_DIAGNOSTIC_DETAILS,
    DiagnosticFamily.ARTIFACT_SHAPE_UNCONFIRMED.value: (
        _COMMON_DIAGNOSTIC_DETAILS
    ),
    DiagnosticFamily.KNOWN_NON_IMPACTING.value: _COMMON_DIAGNOSTIC_DETAILS,
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
    DiagnosticFamily.REQUIRED_EVIDENCE_MISSING.value: frozenset(
        {DiagnosticDetail.MISSING_BUNDLE.value},
    ),
    DiagnosticFamily.REQUIRED_EVIDENCE_SKIPPED.value: frozenset(
        {DiagnosticDetail.DEPENDENCY_BLOCKED.value},
    ),
    DiagnosticFamily.BLOCKING_VALIDATION_FAILURE.value: frozenset(
        {DiagnosticDetail.BLOCKING_VALIDATION_FAILURE.value},
    ),
    DiagnosticFamily.INADMISSIBLE_BATCH_EVIDENCE.value: frozenset(
        {
            DiagnosticDetail.MALFORMED_BUNDLE.value,
            DiagnosticDetail.MISSING_BUNDLE.value,
            DiagnosticDetail.DUPLICATE_BUNDLE_CANDIDATES.value,
            DiagnosticDetail.BUNDLE_PRODUCER_UNVERIFIED.value,
            DiagnosticDetail.BUNDLE_METADATA_AUTHORITY_INVALID.value,
            DiagnosticDetail.EXECUTION_BATCH_MANIFEST_MISSING.value,
            DiagnosticDetail.EXECUTION_BATCH_MANIFEST_DUPLICATE.value,
            DiagnosticDetail.EXECUTION_BATCH_MANIFEST_UNREADABLE.value,
            DiagnosticDetail.EXECUTION_BATCH_MANIFEST_MALFORMED.value,
            DiagnosticDetail.EXECUTION_BATCH_MANIFEST_NON_CANONICAL.value,
            DiagnosticDetail.EXECUTION_BATCH_MANIFEST_DIGEST_MISMATCH.value,
            DiagnosticDetail.EXECUTION_BATCH_MANIFEST_PLAN_MISMATCH.value,
            DiagnosticDetail.EXECUTION_BATCH_MANIFEST_BUNDLE_REF_MISMATCH.value,
        },
    ),
    DiagnosticFamily.NAMESPACE_CLOSURE_FAILURE.value: frozenset(
        {
            DiagnosticDetail.NAMESPACE_ENUMERATION_UNAVAILABLE.value,
            DiagnosticDetail.UNEXPECTED_CONTRACT_ARTIFACT.value,
            DiagnosticDetail.NAMESPACE_OVERFLOW.value,
        },
    ),
    DiagnosticFamily.REQUIRED_INPUT_ARTIFACT_FAILURE.value: frozenset(
        {
            DiagnosticDetail.REQUIRED_INPUT_ARTIFACT_FAILURE.value,
            DiagnosticDetail.SNAPSHOT_COMPANION_UNPROVEN.value,
        },
    ),
    DiagnosticFamily.AGGREGATE_SUMMARY_WITHOUT_MANIFEST.value: frozenset(
        {DiagnosticDetail.AGGREGATE_SUMMARY_WITHOUT_MANIFEST.value},
    ),
    DiagnosticFamily.FINAL_PRODUCER_UNVERIFIED.value: frozenset(
        {DiagnosticDetail.FINAL_PRODUCER_UNVERIFIED.value},
    ),
    DiagnosticFamily.FINAL_EVIDENCE_FAILURE.value: frozenset(
        {
            DiagnosticDetail.AGGREGATE_EVIDENCE_MANIFEST_MISSING.value,
            DiagnosticDetail.AGGREGATE_EVIDENCE_MANIFEST_DUPLICATE.value,
            DiagnosticDetail.AGGREGATE_EVIDENCE_MANIFEST_UNREADABLE.value,
            DiagnosticDetail.AGGREGATE_EVIDENCE_MANIFEST_MALFORMED.value,
            DiagnosticDetail.AGGREGATE_EVIDENCE_MANIFEST_NON_CANONICAL.value,
            DiagnosticDetail.AGGREGATE_EVIDENCE_MANIFEST_DIGEST_MISMATCH.value,
            DiagnosticDetail.FINAL_PRODUCER_UNVERIFIED.value,
        },
    ),
}

CI_VALIDATION_FINAL_EVIDENCE_DETAILS = DETAILS_BY_DIAGNOSTIC_CODE[
    DiagnosticFamily.FINAL_EVIDENCE_FAILURE.value
]
CI_VALIDATION_G1_DETAILS_BY_DIAGNOSTIC_CODE = {
    code: DETAILS_BY_DIAGNOSTIC_CODE[code]
    for code in (
        DiagnosticFamily.REQUEST_INVALID.value,
        DiagnosticFamily.INVALID_PLAN.value,
        DiagnosticFamily.REQUIRED_EVIDENCE_MISSING.value,
        DiagnosticFamily.REQUIRED_EVIDENCE_SKIPPED.value,
        DiagnosticFamily.BLOCKING_VALIDATION_FAILURE.value,
        DiagnosticFamily.INADMISSIBLE_BATCH_EVIDENCE.value,
        DiagnosticFamily.NAMESPACE_CLOSURE_FAILURE.value,
        DiagnosticFamily.REQUIRED_INPUT_ARTIFACT_FAILURE.value,
        DiagnosticFamily.AGGREGATE_SUMMARY_WITHOUT_MANIFEST.value,
        DiagnosticFamily.FINAL_PRODUCER_UNVERIFIED.value,
        DiagnosticFamily.FINAL_EVIDENCE_FAILURE.value,
    )
}

CI_VALIDATION_INVALID_PLAN_RETAINED_PROJECTION_DETAIL_PRIORITY = (
    DiagnosticDetail.PLAN_DUPLICATE.value,
    DiagnosticDetail.PLAN_PRODUCER_UNVERIFIED.value,
    DiagnosticDetail.PLAN_DIGEST_MISMATCH.value,
    DiagnosticDetail.SCHEMA_INVALID.value,
    DiagnosticDetail.STRUCTURALLY_INVALID.value,
    DiagnosticDetail.CHANGED_FILES_SNAPSHOT_MALFORMED.value,
    DiagnosticDetail.CHANGED_FILES_SNAPSHOT_SCHEMA_INVALID.value,
    DiagnosticDetail.CHANGED_FILES_SNAPSHOT_REF_MISMATCH.value,
    DiagnosticDetail.CHANGED_FILES_SNAPSHOT_ENVELOPE_MISMATCH.value,
    DiagnosticDetail.CHANGED_FILES_SNAPSHOT_NONCANONICAL.value,
    DiagnosticDetail.CHANGED_FILES_SNAPSHOT_DIGEST_MISMATCH.value,
    DiagnosticDetail.FACT_SNAPSHOT_MALFORMED.value,
    DiagnosticDetail.FACT_SNAPSHOT_PRODUCER_UNVERIFIED.value,
    DiagnosticDetail.FACT_SNAPSHOT_SCHEMA_INVALID.value,
    DiagnosticDetail.FACT_SNAPSHOT_REF_MISMATCH.value,
    DiagnosticDetail.FACT_SNAPSHOT_ENVELOPE_MISMATCH.value,
    DiagnosticDetail.FACT_SNAPSHOT_PLAN_MISMATCH.value,
    DiagnosticDetail.FACT_SNAPSHOT_CROSS_REFERENCE_INVALID.value,
    DiagnosticDetail.FACT_SNAPSHOT_NONCANONICAL.value,
    DiagnosticDetail.FACT_SNAPSHOT_DIGEST_MISMATCH.value,
)
CI_VALIDATION_INVALID_PLAN_RETAINED_PROJECTION_DETAILS = frozenset(
    CI_VALIDATION_INVALID_PLAN_RETAINED_PROJECTION_DETAIL_PRIORITY
)
CI_VALIDATION_INVALID_PLAN_NO_AUTHORITY_PROJECTION_DETAILS = frozenset(
    {
        DiagnosticDetail.PLAN_MISSING.value,
        DiagnosticDetail.MALFORMED_PLAN.value,
        DiagnosticDetail.PLAN_UNREADABLE.value,
    }
)
CI_VALIDATION_INVALID_PLAN_SNAPSHOT_MALFORMED_DETAILS = frozenset(
    {
        DiagnosticDetail.CHANGED_FILES_SNAPSHOT_MALFORMED.value,
        DiagnosticDetail.FACT_SNAPSHOT_MALFORMED.value,
    }
)
CI_VALIDATION_INVALID_PLAN_MISSING_MESSAGE = (
    "No authoritative validation plan was available."
)
CI_VALIDATION_INVALID_PLAN_NON_AUTHORITATIVE_MESSAGE = (
    "CI validation plan evidence is not authoritative."
)
CI_VALIDATION_INVALID_PLAN_RETAINED_PROJECTION_DETAIL_MESSAGES = dict.fromkeys(
    CI_VALIDATION_INVALID_PLAN_RETAINED_PROJECTION_DETAILS,
    CI_VALIDATION_INVALID_PLAN_NON_AUTHORITATIVE_MESSAGE,
)
CI_VALIDATION_INVALID_PLAN_NO_AUTHORITY_PROJECTION_DETAIL_MESSAGES = {
    DiagnosticDetail.MALFORMED_PLAN.value: (
        CI_VALIDATION_INVALID_PLAN_NON_AUTHORITATIVE_MESSAGE
    ),
    DiagnosticDetail.PLAN_UNREADABLE.value: (
        CI_VALIDATION_INVALID_PLAN_NON_AUTHORITATIVE_MESSAGE
    ),
}
CI_VALIDATION_AGGREGATE_MANIFEST_AUTHORITY_DETAILS = frozenset(
    {
        DiagnosticDetail.AGGREGATE_EVIDENCE_MANIFEST_MISSING.value,
        DiagnosticDetail.AGGREGATE_EVIDENCE_MANIFEST_DUPLICATE.value,
        DiagnosticDetail.AGGREGATE_EVIDENCE_MANIFEST_UNREADABLE.value,
        DiagnosticDetail.AGGREGATE_EVIDENCE_MANIFEST_MALFORMED.value,
        DiagnosticDetail.AGGREGATE_EVIDENCE_MANIFEST_NON_CANONICAL.value,
        DiagnosticDetail.AGGREGATE_EVIDENCE_MANIFEST_DIGEST_MISMATCH.value,
    }
)
CI_VALIDATION_AGGREGATE_MANIFEST_AUTHORITY_MESSAGES = {
    DiagnosticDetail.AGGREGATE_EVIDENCE_MANIFEST_MISSING.value: (
        "Preserved aggregate evidence manifest is missing."
    ),
    DiagnosticDetail.AGGREGATE_EVIDENCE_MANIFEST_DUPLICATE.value: (
        "Preserved aggregate evidence manifest is duplicated."
    ),
    DiagnosticDetail.AGGREGATE_EVIDENCE_MANIFEST_UNREADABLE.value: (
        "Preserved aggregate evidence manifest is unreadable."
    ),
    DiagnosticDetail.AGGREGATE_EVIDENCE_MANIFEST_MALFORMED.value: (
        "Preserved aggregate evidence manifest is malformed."
    ),
    DiagnosticDetail.AGGREGATE_EVIDENCE_MANIFEST_NON_CANONICAL.value: (
        "Preserved aggregate evidence manifest bytes are not canonical."
    ),
    DiagnosticDetail.AGGREGATE_EVIDENCE_MANIFEST_DIGEST_MISMATCH.value: (
        "Preserved aggregate evidence manifest differs from the recomputed "
        "validation view."
    ),
}
CI_VALIDATION_AGGREGATE_MANIFEST_CONTRACT_INVALID_MESSAGE = (
    "Preserved aggregate evidence manifest is contract-invalid."
)
CI_VALIDATION_AGGREGATE_MANIFEST_AUTHORITY_MESSAGE_OPTIONS = {
    detail: frozenset({message})
    for detail, message in (
        CI_VALIDATION_AGGREGATE_MANIFEST_AUTHORITY_MESSAGES.items()
    )
} | {
    DiagnosticDetail.AGGREGATE_EVIDENCE_MANIFEST_MALFORMED.value: frozenset(
        {
            CI_VALIDATION_AGGREGATE_MANIFEST_AUTHORITY_MESSAGES[
                DiagnosticDetail.AGGREGATE_EVIDENCE_MANIFEST_MALFORMED.value
            ],
            CI_VALIDATION_AGGREGATE_MANIFEST_CONTRACT_INVALID_MESSAGE,
        }
    ),
}


def preferred_ci_validation_invalid_plan_retained_projection_detail(
    details: set[str],
) -> str:
    """Return the canonical retained invalid-plan projection detail."""
    for (
        detail
    ) in CI_VALIDATION_INVALID_PLAN_RETAINED_PROJECTION_DETAIL_PRIORITY:
        if detail in details:
            return detail
    return sorted(details)[0]


API_VERSIONS_BY_KIND = {
    CiValidationKind.REQUEST.value: "three.ci.validation.request/v1alpha1",
    CiValidationKind.PLAN.value: "three.ci.validation.plan/v1alpha1",
    CiValidationKind.CHANGED_FILES_SNAPSHOT.value: (
        "three.ci.validation.changed-files/v1alpha1"
    ),
    CiValidationKind.FACT_SNAPSHOT.value: (
        "three.ci.validation.fact-snapshot/v1alpha1"
    ),
    CiValidationKind.EXECUTION_BATCH_MANIFEST.value: (
        "three.ci.validation.execution-batch-manifest/v1alpha1"
    ),
    CiValidationKind.BATCH_EVIDENCE_BUNDLE.value: (
        "three.ci.validation.batch-evidence-bundle/v1alpha2"
    ),
    CiValidationKind.AGGREGATE_EVIDENCE_MANIFEST.value: (
        "three.ci.validation.aggregate-evidence-manifest/v1alpha1"
    ),
    CiValidationKind.AGGREGATE_SUMMARY.value: (
        "three.ci.validation.aggregate-summary/v1alpha1"
    ),
}


@dataclass(frozen=True, slots=True)
class ArtifactPhysicalName:
    """Logical ref and attempt-scoped GitHub artifact name; no fixed length."""

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


def artifact_physical_name(
    logical_ref: str,
    *,
    run_id: str | int | None = None,
    run_attempt: str | int | None = None,
) -> str:
    """Map a logical CI validation artifact ref to its attempt-scoped name."""
    validate_artifact_logical_ref(logical_ref)
    attempt = _logical_ref_attempt(logical_ref)
    physical_run_id = str(run_id) if run_id is not None else attempt[0]
    physical_run_attempt = (
        str(run_attempt) if run_attempt is not None else attempt[1]
    )
    _validate_physical_attempt_segment(physical_run_id, "run_id")
    _validate_physical_attempt_segment(physical_run_attempt, "run_attempt")
    if (physical_run_id, physical_run_attempt) != attempt:
        raise ContractValidationError(
            [
                ValidationIssue(
                    "physical-artifact-name",
                    "attempt discriminator must match logical artifact ref",
                )
            ],
        )
    digest = hashlib.sha256(logical_ref.encode("utf-8")).hexdigest()
    return (
        f"{ARTIFACT_PHYSICAL_NAME_PREFIX}"
        f"{physical_run_id}-{physical_run_attempt}-{digest}"
    )


def artifact_physical_ref(
    logical_ref: str,
    *,
    run_id: str | int | None = None,
    run_attempt: str | int | None = None,
) -> ArtifactPhysicalName:
    """Return the logical/physical artifact ref pair."""
    return ArtifactPhysicalName(
        logical_ref=logical_ref,
        physical_name=artifact_physical_name(
            logical_ref,
            run_id=run_id,
            run_attempt=run_attempt,
        ),
    )


def validate_ci_validation_diagnostic_record(
    value: object,
    path: str = "diagnostic",
) -> None:
    """Validate a complete CI validation diagnostic record."""
    issues: list[ValidationIssue] = []
    _validate_diagnostic_record(value, path, issues)
    if issues:
        raise ContractValidationError(issues)


def _validate_diagnostic_record(  # noqa: C901
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(value, Mapping):
        issues.append(ValidationIssue(path, "must be an object"))
        return
    allowed = frozenset(
        {
            "diagnostic-id",
            "code",
            "detail",
            "message",
            "source",
            "severity",
            "verdict-effect",
        }
    )
    for key in sorted(set(value) - allowed):
        issues.append(ValidationIssue(f"{path}.{key}", "is not allowed"))
    for key in sorted(allowed - set(value)):
        issues.append(ValidationIssue(f"{path}.{key}", "is required"))
    diagnostic_id = value.get("diagnostic-id")
    if not isinstance(diagnostic_id, str) or diagnostic_id == "":
        issues.append(
            ValidationIssue(f"{path}.diagnostic-id", "must be a string")
        )
    code = value.get("code")
    if not isinstance(code, str) or code == "":
        issues.append(ValidationIssue(f"{path}.code", "must be a string"))
    elif code not in REGISTERED_CI_VALIDATION_DIAGNOSTIC_CODES:
        issues.append(ValidationIssue(f"{path}.code", "is not registered"))
    _nullable_registered_detail(
        value.get("detail"),
        f"{path}.detail",
        code if isinstance(code, str) else "",
        issues,
    )
    _nullable_string(value.get("message"), f"{path}.message", issues)
    _validate_diagnostic_source(value.get("source"), f"{path}.source", issues)
    severity = value.get("severity")
    if not isinstance(severity, str) or severity == "":
        issues.append(ValidationIssue(f"{path}.severity", "must be a string"))
    elif severity not in {
        item.value for item in DiagnosticSeverity.__members__.values()
    }:
        issues.append(ValidationIssue(f"{path}.severity", "is not registered"))
    verdict_effect = value.get("verdict-effect")
    if not isinstance(verdict_effect, str) or verdict_effect == "":
        issues.append(
            ValidationIssue(f"{path}.verdict-effect", "must be a string")
        )
    elif verdict_effect not in {
        item.value for item in DiagnosticVerdictEffect.__members__.values()
    }:
        issues.append(
            ValidationIssue(f"{path}.verdict-effect", "is not registered")
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
    """Validate an attempt-scoped CI validation physical artifact name."""
    issues: list[ValidationIssue] = []
    if not isinstance(physical_name, str):
        issues.append(
            ValidationIssue("physical-artifact-name", "must be a string"),
        )
    elif (match := _PHYSICAL_ARTIFACT_NAME_RE.fullmatch(physical_name)) is None:
        issues.append(
            ValidationIssue(
                "physical-artifact-name",
                "must be three-ci-validation-{run-id}-{run-attempt}-"
                " followed by 64 lowercase hex chars",
            ),
        )
    else:
        for group_name in ("run_id", "run_attempt"):
            if match.group(group_name) in {".", ".."}:
                issues.append(
                    ValidationIssue(
                        f"physical-artifact-name.{group_name}",
                        "must not be . or ..",
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
    return _validate_common_envelope_with_versions(
        document,
        api_version=api_version,
        kind=kind,
        extra_api_versions_by_kind={},
    )


def _validate_common_envelope_with_versions(
    document: JsonObject,
    *,
    api_version: str,
    kind: CiValidationKind | str,
    extra_api_versions_by_kind: Mapping[str, str] | None = None,
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
    extra_versions = extra_api_versions_by_kind or {}
    registered_api_version = API_VERSIONS_BY_KIND.get(
        document_kind
    ) or extra_versions.get(document_kind)
    expected_api_version = API_VERSIONS_BY_KIND.get(
        expected_kind
    ) or extra_versions.get(expected_kind)
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


def _logical_ref_attempt(logical_ref: str) -> tuple[str, str]:
    match = _LOGICAL_REF_ATTEMPT_RE.fullmatch(
        logical_ref[: logical_ref.find("/", len("ci-validation/") + 1) + 1]
    )
    if match is None:
        match = _LOGICAL_REF_ATTEMPT_RE.match(logical_ref)
    if match is None:
        raise ContractValidationError(
            [
                ValidationIssue(
                    "artifact-ref",
                    "must include run-id and run-attempt segments",
                )
            ],
        )
    return match.group("run_id"), match.group("run_attempt")


def _validate_physical_attempt_segment(value: object, path: str) -> None:
    if (
        not isinstance(value, str)
        or value in {".", ".."}
        or _ARTIFACT_REF_SEGMENT_RE.fullmatch(value) is None
    ):
        raise ContractValidationError(
            [
                ValidationIssue(
                    path,
                    "must be a non-empty normalized artifact ref segment",
                )
            ],
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
    allowed = frozenset({"type", "id"})
    for key in sorted(set(value) - allowed):
        issues.append(ValidationIssue(f"{path}.{key}", "is not allowed"))
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
