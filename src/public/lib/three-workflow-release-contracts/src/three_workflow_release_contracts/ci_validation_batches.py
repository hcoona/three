"""Execution-batch CI validation contracts.

These helpers implement execution-batch validation artifacts introduced by
the execution-batch model.
"""

from __future__ import annotations

import hashlib
import re
from collections.abc import Iterator, Mapping, MutableMapping, Sequence
from dataclasses import dataclass
from typing import Literal, cast

from three_workflow_release_contracts.ci_validation import (
    API_VERSIONS_BY_KIND,
    CI_VALIDATION_AGGREGATE_MANIFEST_AUTHORITY_DETAILS,
    CI_VALIDATION_AGGREGATE_MANIFEST_AUTHORITY_MESSAGE_OPTIONS,
    CI_VALIDATION_AGGREGATE_MANIFEST_AUTHORITY_MESSAGES,
    CI_VALIDATION_G1_DETAILS_BY_DIAGNOSTIC_CODE,
    CI_VALIDATION_INVALID_PLAN_MISSING_MESSAGE,
    CI_VALIDATION_INVALID_PLAN_NO_AUTHORITY_PROJECTION_DETAILS,
    CI_VALIDATION_INVALID_PLAN_NON_AUTHORITATIVE_MESSAGE,
    CI_VALIDATION_INVALID_PLAN_RETAINED_PROJECTION_DETAILS,
    CI_VALIDATION_INVALID_PLAN_SNAPSHOT_MALFORMED_DETAILS,
    DETAILS_BY_DIAGNOSTIC_CODE,
    CiValidationKind,
    CommonEnvelope,
    DiagnosticDetail,
    DiagnosticFamily,
    DiagnosticSeverity,
    DiagnosticVerdictEffect,
    artifact_physical_name,
    canonical_json_bytes,
    canonical_json_digest,
    preferred_ci_validation_invalid_plan_retained_projection_detail,
    validate_artifact_logical_ref,
    validate_artifact_physical_name,
    validate_ci_validation_diagnostic_record,
    validate_common_envelope,
)
from three_workflow_release_contracts.ci_validation_assignments import (
    ci_validation_writer_id,
)
from three_workflow_release_contracts.ci_validation_plans import (
    _freeze_fact_snapshot_providers,
    _validate_plan_id_value,
    _validate_repo_relative_git_path,
    ci_validation_changed_files_hash,
    ci_validation_changed_files_snapshot_artifact_ref,
    ci_validation_fact_snapshot_artifact_ref,
    ci_validation_fact_snapshot_id,
    ci_validation_plan_digest,
    validate_ci_validation_plan,
    validate_ci_validation_plan_structure,
)
from three_workflow_release_contracts.ci_validation_requests import (
    ci_validation_aggregate_evidence_manifest_artifact_ref,
    ci_validation_aggregate_summary_artifact_ref,
    ci_validation_batch_evidence_bundle_artifact_ref,
    ci_validation_execution_batch_manifest_artifact_ref,
    ci_validation_plan_artifact_ref,
    ci_validation_request_artifact_ref,
    validate_ci_validation_request,
)
from three_workflow_release_contracts.contracts import (
    ContractValidationError,
    ValidationIssue,
)

_LOCAL_ID_RE = re.compile(r"^[a-z0-9][a-z0-9._-]{0,127}$")
_DIGEST_RE = re.compile(r"^[0-9a-f]{64}$")
_SHA_RE = re.compile(r"^[0-9a-f]{40}$")
_RUNNER_FAMILIES = frozenset({"windows", "ubuntu", "macos"})
_ECOSYSTEMS = frozenset(
    {"dotnet", "python", "javascript", "typescript", "ruby"}
)
_MODES = frozenset({"pull_request", "push", "scheduled_full"})
_SUMMARY_MODES = _MODES | frozenset({"unknown"})
_AFFECTED_STATUSES = frozenset(
    {"available", "unavailable", "not-applicable", "unknown"},
)
_OUTCOMES = frozenset({"success", "blocking-failure", "skipped"})
_RESULT_OUTCOMES = frozenset({"satisfied", "missing", "skipped", "failed"})
_ADMISSIBILITIES = frozenset(
    {"valid", "inadmissible", "missing", "not-required", "duplicate"},
)
_BUNDLE_ADMISSIBILITIES = frozenset(
    {"valid", "inadmissible", "missing", "duplicate"},
)
_AGGREGATE_MAX_DURATION_SECONDS = 120
_PROOF_ADMISSIBILITY = "validation-only"
_REPOSITORY_VALIDATION_BATCH_KINDS = frozenset(
    {"descriptor-validation", "workflow-release-tooling"},
)
_EXPECTED_FINAL_VALIDATION_ARTIFACTS = 2
_MAX_VALIDATION_ARTIFACTS = 20
_MAX_PREFINAL_VALIDATION_ARTIFACTS = (
    _MAX_VALIDATION_ARTIFACTS - _EXPECTED_FINAL_VALIDATION_ARTIFACTS
)
_MAX_TOTAL_JOBS = 18
_MAX_WINDOWS_JOBS = 8
_MAX_EXECUTION_BATCHES = 13
_AGGREGATE_MANIFEST_AUTHORITY_DETAILS = (
    CI_VALIDATION_AGGREGATE_MANIFEST_AUTHORITY_DETAILS
)
_AGGREGATE_MANIFEST_AUTHORITY_MESSAGES = (
    CI_VALIDATION_AGGREGATE_MANIFEST_AUTHORITY_MESSAGES
)
_AGGREGATE_MANIFEST_AUTHORITY_MESSAGE_OPTIONS = (
    CI_VALIDATION_AGGREGATE_MANIFEST_AUTHORITY_MESSAGE_OPTIONS
)
_BUDGET_KEYS = frozenset(
    {
        "min-total-jobs",
        "max-total-jobs",
        "min-windows-jobs",
        "max-windows-jobs",
        "non-batch-control-plane-job-count",
        "actual-total-jobs",
        "actual-windows-jobs",
        "max-validation-artifacts",
        "actual-validation-artifacts",
        "expected-input-non-bundle-validation-artifacts",
        "expected-final-validation-artifacts",
        "expected-non-bundle-validation-artifacts",
        "pre-final-validation-artifacts",
        "max-execution-batches",
        "actual-execution-batches",
        "aggregate-target-duration-seconds",
        "aggregate-max-duration-seconds",
    },
)
_COMPATIBILITY_KEYS = frozenset(
    {
        "ecosystem",
        "setup-profile",
        "setup-profile-digest",
        "execution-profile",
        "execution-profile-digest",
        "release-shaped-profile",
        "release-shaped-profile-digest",
    },
)
_ORDERED_SELECTOR_KEYS = frozenset(
    {
        "work-group-id",
        "selector-index",
        "depends-on",
        "expected-evidence-id",
        "expected-evidence-slot",
    },
)
_BATCH_WRITER_KEYS = frozenset(
    {
        "identity-source",
        "expected-boundary",
        "expected-job-identity",
        "provenance-fields",
    },
)
_BATCH_KEYS = frozenset(
    {
        "batch-id",
        "runner-family",
        "compatibility-profile",
        "depends-on-batches",
        "ordered-selectors",
        "expected-batch-evidence-bundle-ref",
        "batch-writer",
    },
)
_EXECUTION_BATCH_MANIFEST_KEYS = frozenset(
    {
        "api-version",
        "kind",
        "created-at",
        "repository",
        "run",
        "schema-diagnostics",
        "execution-job",
        "plan-id",
        "plan-digest",
        "budget",
        "batches",
    },
)
_BUNDLE_WRITER_KEYS = frozenset(
    {
        "identity-source",
        "expected-boundary",
        "expected-job-identity",
        "observed-writer-identity",
        "observed-workflow",
        "observed-job",
        "observed-matrix",
        "logical-batch-identity",
        "observed-orchestrator-slot-index",
    },
)
_EXECUTION_TREE_KEYS = frozenset(
    {"observed-commit-sha", "source", "verified"},
)
_SELECTOR_RESULT_KEYS = frozenset(
    {
        "work-group-id",
        "selector-index",
        "expected-evidence-id",
        "expected-evidence-slot-digest",
        "mode",
        "validation-tree",
        "affected-range",
        "scheduled-full",
        "coverage-target",
        "ecosystem",
        "runner-family",
        "selector-variant",
        "depends-on",
        "dependency-results",
        "outcome",
        "skip-reason",
        "evidence",
        "diagnostics",
        "proof-admissibility",
    },
)
_DEPENDENCY_RESULT_KEYS = frozenset(
    {
        "work-group-id",
        "source-batch-id",
        "upstream-artifact-ref",
        "upstream-bundle-id",
        "upstream-artifact-instance-id",
        "upstream-admitted-candidate-id",
        "outcome",
        "admitted-for-gating",
    },
)
_BATCH_EVIDENCE_BUNDLE_KEYS = frozenset(
    {
        "api-version",
        "kind",
        "created-at",
        "repository",
        "run",
        "schema-diagnostics",
        "artifact-ref",
        "bundle-id",
        "plan-id",
        "plan-digest",
        "mode",
        "validation-tree",
        "affected-range",
        "scheduled-full",
        "execution-batch-manifest",
        "batch",
        "writer",
        "execution-tree",
        "started-at",
        "completed-at",
        "selector-results",
        "batch-diagnostics",
        "proof-admissibility",
    },
)
_INPUT_ARTIFACT_KEYS = frozenset(
    {
        "artifact-ref",
        "artifact-instance-id",
        "content-digest",
        "required",
        "expected-cardinality",
        "admissibility",
        "diagnostics",
    },
)
_INPUT_ARTIFACT_NAMES = frozenset(
    {
        "request",
        "validation-plan",
        "changed-files-snapshot",
        "fact-snapshot",
        "execution-batch-manifest",
    },
)
_REQUIRED_INPUT_ARTIFACT_ADMISSIBILITIES = frozenset(
    {"valid", "missing", "inadmissible"},
)
_BATCH_BUNDLE_SLOT_KEYS = frozenset(
    {
        "batch-id",
        "artifact-ref",
        "expected-cardinality",
        "slot-admissibility",
        "admitted-candidate-id",
        "observed-candidates",
        "diagnostics",
    },
)
_CANDIDATE_KEYS = frozenset(
    {
        "candidate-id",
        "artifact-instance-id",
        "content-digest",
        "producer-verification",
        "payload-readable",
        "admissibility",
        "diagnostics",
    },
)
_UNEXPECTED_KEYS = frozenset(
    {
        "physical-artifact-name",
        "observed-physical-artifact-name",
        "artifact-instance-id",
        "classification",
        "diagnostics",
    },
)
_NAMESPACE_OVERFLOW_KEYS = frozenset(
    {
        "detected",
        "observed-prefixed-artifact-count-lower-bound",
        "max-prefixed-validation-artifacts",
        "diagnostics",
    },
)
_PROJECTION_AUTHORITY_KEYS = frozenset(
    {
        "mode",
        "validation-tree",
        "affected-range",
        "request",
        "scheduled-full",
        "projection-digest",
    },
)
_PROJECTION_AUTHORITY_PAYLOAD_KEYS = tuple(
    key for key in _PROJECTION_AUTHORITY_KEYS if key != "projection-digest"
)
_AGGREGATE_EVIDENCE_MANIFEST_KEYS = frozenset(
    {
        "api-version",
        "kind",
        "created-at",
        "repository",
        "run",
        "schema-diagnostics",
        "artifact-ref",
        "plan-id",
        "plan-digest",
        "input-artifacts",
        "batch-bundles",
        "unexpected-contract-artifacts",
        "namespace-overflow",
        "projection-authority",
        "pre-final-validation-artifacts",
        "namespace-closed-at",
        "proof-admissibility",
    },
)
_FINAL_ARTIFACT_KEYS = frozenset(
    {
        "aggregate-evidence-manifest",
        "aggregate-summary",
    },
)
_FINAL_AGGREGATE_EVIDENCE_MANIFEST_ENTRY_KEYS = frozenset(
    {
        "artifact-ref",
        "artifact-instance-id",
        "content-digest",
        "producer-verified",
        "authority-diagnostics",
    },
)
_FINAL_AGGREGATE_SUMMARY_ENTRY_KEYS = frozenset(
    {
        "artifact-ref",
    },
)

AggregateSummaryFailureKind = Literal[
    "invalid-plan",
    "fail-closed",
    "required-evidence-missing",
    "required-evidence-skipped",
    "blocking-validation-failure",
    "inadmissible-batch-evidence",
    "namespace-closure-failure",
    "required-input-artifact-failure",
    "aggregate-summary-without-manifest",
    "final-producer-unverified",
    "final-evidence-failure",
]
FailureKind = AggregateSummaryFailureKind

_SUMMARY_REASON_KEYS = frozenset(
    {
        "invalid-plan",
        "fail-closed",
        "required-evidence-missing",
        "required-evidence-skipped",
        "blocking-validation-failure",
        "inadmissible-batch-evidence",
        "namespace-closure-failure",
        "required-input-artifact-failure",
        "aggregate-summary-without-manifest",
        "final-producer-unverified",
        "final-evidence-failure",
    },
)
_SUMMARY_BUDGET_KEYS = frozenset(
    {
        "pre-final-validation-artifacts",
        "expected-final-validation-artifacts",
        "expected-actual-validation-artifacts",
        "max-validation-artifacts",
        "actual-execution-batches",
        "actual-total-jobs",
        "actual-windows-jobs",
        "aggregate-duration-seconds",
        "aggregate-target-duration-seconds",
        "aggregate-max-duration-seconds",
    },
)
_SUMMARY_BUNDLE_KEYS = frozenset(
    {
        "batch-id",
        "artifact-ref",
        "bundle-id",
        "admitted-candidate-id",
        "candidate-count",
        "admissibility",
        "diagnostics",
    },
)
_SUMMARY_EVIDENCE_RESULT_KEYS = frozenset(
    {
        "evidence-expectation-id",
        "work-group-id",
        "batch-id",
        "bundle-id",
        "selector-index",
        "outcome",
        "diagnostics",
    },
)
_SUMMARY_FAILURE_KEYS = frozenset(
    {
        "kind",
        "batch-id",
        "work-group-id",
        "evidence-expectation-id",
        "bundle-id",
        "diagnostic",
        "message",
    },
)
_SUMMARY_FAILURE_KINDS = _SUMMARY_REASON_KEYS
_SUMMARY_WORK_GROUP_KEYS = frozenset(
    {
        "executable-required",
        "required-succeeded",
        "required-failed",
        "required-skipped",
        "required-missing",
        "terminal-aggregation",
    },
)
_AGGREGATE_SUMMARY_KEYS = frozenset(
    {
        "api-version",
        "kind",
        "created-at",
        "repository",
        "run",
        "schema-diagnostics",
        "artifact-ref",
        "plan-id",
        "plan-digest",
        "mode",
        "aggregate-evidence-manifest",
        "final-artifacts",
        "validation-tree",
        "affected-range",
        "request",
        "scheduled-full",
        "verdict",
        "reason",
        "budgets",
        "diagnostics",
        "batch-bundles",
        "evidence-results",
        "failures",
        "work-groups",
        "proof-admissibility",
    },
)
_UNKNOWN_VALIDATION_TREE = {"commit-sha": None, "ref": None}
_UNKNOWN_AFFECTED_RANGE = {
    "status": "unknown",
    "base-sha": None,
    "base-tip-sha": None,
    "head-sha": None,
    "changed-files-hash": None,
}
_UNKNOWN_REQUEST_SUMMARY = {"artifact-ref": None, "request-digest": None}
_UNKNOWN_SCHEDULED_FULL = {"enabled": False}
_CHANGED_FILES_SNAPSHOT_KEYS = frozenset(
    {
        "api-version",
        "kind",
        "created-at",
        "repository",
        "run",
        "schema-diagnostics",
        "artifact-ref",
        "changed-files-hash",
        "hash-payload",
    },
)
_CHANGED_FILES_HASH_PAYLOAD_KEYS = frozenset(
    {"api-version", "changed-files"},
)
_FACT_SNAPSHOT_KEYS = frozenset(
    {
        "api-version",
        "kind",
        "created-at",
        "repository",
        "run",
        "schema-diagnostics",
        "artifact-ref",
        "fact-snapshot-id",
        "plan-id",
        "providers",
    },
)
_NO_AUTHORITY_SUMMARY_PROJECTION = {
    "mode": "unknown",
    "validation-tree": _UNKNOWN_VALIDATION_TREE,
    "affected-range": _UNKNOWN_AFFECTED_RANGE,
    "request": _UNKNOWN_REQUEST_SUMMARY,
    "scheduled-full": _UNKNOWN_SCHEDULED_FULL,
}
_INVALID_PLAN_FAILURE = {
    "kind": "invalid-plan",
    "batch-id": None,
    "work-group-id": None,
    "evidence-expectation-id": None,
    "bundle-id": None,
    "diagnostic": {
        "diagnostic-id": "invalid-plan",
        "code": "invalid-plan",
        "detail": "plan-missing",
        "message": CI_VALIDATION_INVALID_PLAN_MISSING_MESSAGE,
        "source": {"type": "aggregation", "id": None},
        "severity": "fail-closed",
        "verdict-effect": "fail-closed",
    },
    "message": CI_VALIDATION_INVALID_PLAN_MISSING_MESSAGE,
}
_INVALID_PLAN_INPUT_FALLBACK_DETAILS = {
    "validation-plan": {
        "missing": DiagnosticDetail.PLAN_MISSING.value,
        "duplicate": DiagnosticDetail.PLAN_DUPLICATE.value,
        "inadmissible": DiagnosticDetail.SCHEMA_INVALID.value,
    },
    "changed-files-snapshot": {
        "missing": DiagnosticDetail.CHANGED_FILES_SNAPSHOT_MISSING.value,
        "duplicate": DiagnosticDetail.CHANGED_FILES_SNAPSHOT_DUPLICATE.value,
        "inadmissible": (
            DiagnosticDetail.CHANGED_FILES_SNAPSHOT_SCHEMA_INVALID.value
        ),
    },
    "fact-snapshot": {
        "missing": DiagnosticDetail.FACT_SNAPSHOT_MISSING.value,
        "duplicate": DiagnosticDetail.FACT_SNAPSHOT_DUPLICATE.value,
        "inadmissible": DiagnosticDetail.FACT_SNAPSHOT_SCHEMA_INVALID.value,
    },
}
_INVALID_PLAN_NO_AUTHORITY_PROJECTION_DETAILS = (
    CI_VALIDATION_INVALID_PLAN_NO_AUTHORITY_PROJECTION_DETAILS
)
_INVALID_PLAN_MALFORMED_SNAPSHOT_DETAILS = (
    CI_VALIDATION_INVALID_PLAN_SNAPSHOT_MALFORMED_DETAILS
)


@dataclass(frozen=True, slots=True)
class CiValidationExecutionBatchMaterialization:
    """Materialized execution-batch manifest plus matrix handoff payload."""

    manifest: Mapping[str, object]
    matrix: Mapping[str, object]


class _TrustedDependencyBundle(dict[str, object]):
    """Validated dependency bundle plus aggregate-admitted identity metadata."""

    def __init__(
        self,
        bundle: Mapping[str, object],
        *,
        artifact_instance_id: str,
        admitted_candidate_id: str,
    ) -> None:
        super().__init__(bundle)
        self.artifact_instance_id = artifact_instance_id
        self.admitted_candidate_id = admitted_candidate_id


def ci_validation_execution_batch_manifest_content_digest(
    raw_manifest_bytes: bytes,
) -> str:
    """Return the SHA-256 digest for raw execution-batch manifest bytes."""
    return _raw_digest(raw_manifest_bytes, "raw-manifest-bytes")


def ci_validation_execution_batch_manifest_payload_digest(
    manifest: Mapping[str, object],
) -> str:
    """Return the canonical payload digest for an execution-batch manifest."""
    return _payload_digest(manifest, "execution-batch-manifest")


def ci_validation_batch_evidence_bundle_content_digest(
    raw_bundle_bytes: bytes,
) -> str:
    """Return the SHA-256 digest for raw batch evidence bundle bytes."""
    return _raw_digest(raw_bundle_bytes, "raw-bundle-bytes")


def ci_validation_batch_evidence_bundle_payload_digest(
    bundle: Mapping[str, object],
) -> str:
    """Return the canonical payload digest for a batch evidence bundle."""
    return _payload_digest(bundle, "batch-evidence-bundle")


def ci_validation_aggregate_evidence_manifest_content_digest(
    raw_manifest_bytes: bytes,
) -> str:
    """Return the SHA-256 digest for raw aggregate evidence manifest bytes."""
    return _raw_digest(raw_manifest_bytes, "raw-manifest-bytes")


def ci_validation_aggregate_evidence_manifest_payload_digest(
    manifest: Mapping[str, object],
) -> str:
    """Return the canonical payload digest for aggregate evidence."""
    return _payload_digest(manifest, "aggregate-evidence-manifest")


def ci_validation_aggregate_summary_content_digest(
    raw_summary_bytes: bytes,
) -> str:
    """Return the SHA-256 digest for raw aggregate summary bytes."""
    return _raw_digest(raw_summary_bytes, "raw-summary-bytes")


def ci_validation_aggregate_summary_payload_digest(
    summary: Mapping[str, object],
) -> str:
    """Return the canonical payload digest for an aggregate summary."""
    return _payload_digest(summary, "aggregate-summary")


def ci_validation_batch_evidence_bundle_id(
    *,
    run_id: str,
    run_attempt: str,
    batch_id: str,
    execution_batch_manifest_digest: str,
    artifact_ref: str,
) -> str:
    """Return the deterministic bundle id for one execution batch."""
    preimage = {
        "run-id": run_id,
        "run-attempt": run_attempt,
        "batch-id": batch_id,
        "execution-batch-manifest-digest": execution_batch_manifest_digest,
        "artifact-ref": artifact_ref,
    }
    return f"bundle-{canonical_json_digest(preimage)}"


def ci_validation_batch_evidence_candidate_id(  # noqa: PLR0913
    *,
    run_id: str,
    run_attempt: str,
    batch_id: str,
    artifact_ref: str,
    artifact_instance_id: str | None,
    physical_artifact_name: str,
) -> str:
    """Return the deterministic candidate id for an observed bundle."""
    preimage = {
        "run-id": run_id,
        "run-attempt": run_attempt,
        "batch-id": batch_id,
        "artifact-ref": artifact_ref,
        "artifact-instance-id": artifact_instance_id or "",
        "physical-artifact-name": physical_artifact_name,
    }
    return f"candidate-{canonical_json_digest(preimage)}"


def freeze_ci_validation_execution_batch_manifest(  # noqa: PLR0913
    *,
    plan: Mapping[str, object],
    request: Mapping[str, object] | None = None,
    changed_files_snapshot: Mapping[str, object] | None = None,
    fact_snapshot: Mapping[str, object] | None = None,
    pull_request_merge_commit_verification: Mapping[str, object] | None = None,
    expected_run_id: str | None = None,
    expected_run_attempt: str | None = None,
    batches: Sequence[Mapping[str, object]],
    budget: Mapping[str, object],
    created_at: str,
    execution_job: str = "execution-batch",
    authorizing: bool = True,
) -> dict[str, object]:
    """Freeze a post-plan execution-batch manifest."""
    envelope = _envelope(plan, CiValidationKind.PLAN)
    _verified_plan_digest(plan)
    if not isinstance(execution_job, str) or execution_job == "":
        raise ContractValidationError(
            [ValidationIssue("execution-job", "must be a string")]
        )
    frozen_batches = sorted(
        (dict(batch) for batch in batches),
        key=lambda item: str(item.get("batch-id")),
    )
    if not authorizing and frozen_batches:
        raise ContractValidationError(
            [
                ValidationIssue(
                    "authorizing",
                    "is required to freeze non-empty execution batches",
                )
            ]
        )
    manifest = {
        "api-version": API_VERSIONS_BY_KIND[
            CiValidationKind.EXECUTION_BATCH_MANIFEST.value
        ],
        "kind": CiValidationKind.EXECUTION_BATCH_MANIFEST.value,
        "created-at": created_at,
        "repository": {
            "owner": envelope.repository_owner,
            "name": envelope.repository_name,
        },
        "run": {
            "workflow": envelope.workflow,
            "run-id": envelope.run_id,
            "run-attempt": envelope.run_attempt,
        },
        "schema-diagnostics": [],
        "execution-job": execution_job,
        "plan-id": plan["plan-id"],
        "plan-digest": _verified_plan_digest(plan),
        "budget": dict(budget),
        "batches": frozen_batches,
    }
    validate_ci_validation_execution_batch_manifest(
        manifest,
        plan=plan,
        request=request,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        pull_request_merge_commit_verification=(
            pull_request_merge_commit_verification
        ),
        expected_run_id=expected_run_id,
        expected_run_attempt=expected_run_attempt,
        authorizing=authorizing,
    )
    return manifest


def materialize_ci_validation_execution_batches(  # noqa: PLR0913
    *,
    plan: Mapping[str, object],
    request: Mapping[str, object],
    created_at: str,
    changed_files_snapshot: Mapping[str, object] | None = None,
    fact_snapshot: Mapping[str, object] | None = None,
    pull_request_merge_commit_verification: Mapping[str, object] | None = None,
    execution_workflow: str | None = None,
    execution_job: str = "execution-batch",
    expected_run_id: str | None = None,
    expected_run_attempt: str | None = None,
    non_batch_control_plane_job_count: int = 0,
    aggregate_target_duration_seconds: int = 60,
    aggregate_max_duration_seconds: int = _AGGREGATE_MAX_DURATION_SECONDS,
) -> CiValidationExecutionBatchMaterialization:
    """Materialize a CI plan into execution batches and matrix rows."""
    validate_ci_validation_plan(
        plan,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        pull_request_merge_commit_verification=(
            pull_request_merge_commit_verification
        ),
        expected_run_id=expected_run_id,
        expected_run_attempt=expected_run_attempt,
    )
    _validate_materializer_current_run_inputs(
        request=request,
        execution_workflow=execution_workflow,
        expected_run_id=expected_run_id,
        expected_run_attempt=expected_run_attempt,
    )
    if not isinstance(created_at, str) or created_at == "":
        raise ContractValidationError(
            [ValidationIssue("created-at", "must be a string")]
        )
    for path, value in (
        (
            "non-batch-control-plane-job-count",
            non_batch_control_plane_job_count,
        ),
        (
            "aggregate-target-duration-seconds",
            aggregate_target_duration_seconds,
        ),
        ("aggregate-max-duration-seconds", aggregate_max_duration_seconds),
    ):
        if not isinstance(value, int) or isinstance(value, bool) or value < 0:
            raise ContractValidationError(
                [ValidationIssue(path, "must be a non-negative integer")]
            )
    envelope = _envelope(plan, CiValidationKind.PLAN)
    current_run_id = cast("str", expected_run_id)
    current_run_attempt = cast("str", expected_run_attempt)
    _validate_materializer_request_context(
        request,
        plan,
        envelope,
        expected_run_id=current_run_id,
        expected_run_attempt=current_run_attempt,
    )
    workflow = execution_workflow
    if not isinstance(workflow, str) or workflow == "":
        raise ContractValidationError(
            [ValidationIssue("execution-workflow", "must be a string")]
        )
    if workflow != envelope.workflow:
        raise ContractValidationError(
            [
                ValidationIssue(
                    "execution-workflow",
                    "must match validation plan workflow",
                )
            ]
        )
    if not isinstance(execution_job, str) or execution_job == "":
        raise ContractValidationError(
            [ValidationIssue("execution-job", "must be a string")]
        )

    groups = _materializer_executable_work_groups(plan)
    expectations = _materializer_evidence_by_work_group(plan, groups)
    ordered_group_ids = _materializer_topological_work_group_ids(groups)
    batch_specs = _materializer_batch_specs(plan, groups, ordered_group_ids)
    max_batches = _materializer_max_execution_batches(
        expected_input_non_bundle_validation_artifacts=(
            _expected_input_non_bundle_validation_artifacts(plan)
        ),
    )
    if len(batch_specs) > max_batches:
        raise ContractValidationError(
            [
                ValidationIssue(
                    "batches",
                    "executable work groups cannot fit execution-batch budget",
                )
            ]
        )

    batches = _materializer_batches(
        envelope=envelope,
        workflow=workflow,
        execution_job=execution_job,
        batch_specs=batch_specs,
        groups=groups,
        expectations=expectations,
    )
    budget = _materializer_budget(
        plan=plan,
        batches=batches,
        expected_input_non_bundle_validation_artifacts=(
            _expected_input_non_bundle_validation_artifacts(plan)
        ),
        max_execution_batches=max_batches,
        non_batch_control_plane_job_count=non_batch_control_plane_job_count,
        aggregate_target_duration_seconds=aggregate_target_duration_seconds,
        aggregate_max_duration_seconds=aggregate_max_duration_seconds,
    )
    manifest = freeze_ci_validation_execution_batch_manifest(
        plan=plan,
        request=request,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        pull_request_merge_commit_verification=(
            pull_request_merge_commit_verification
        ),
        expected_run_id=expected_run_id,
        expected_run_attempt=expected_run_attempt,
        batches=batches,
        budget=budget,
        created_at=created_at,
        execution_job=execution_job,
    )
    validate_ci_validation_execution_batch_manifest(
        manifest,
        plan=plan,
        request=request,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        pull_request_merge_commit_verification=(
            pull_request_merge_commit_verification
        ),
        expected_run_id=expected_run_id,
        expected_run_attempt=expected_run_attempt,
    )
    return CiValidationExecutionBatchMaterialization(
        manifest=manifest,
        matrix=ci_validation_execution_batch_matrix(
            manifest,
            plan=plan,
            request=request,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            pull_request_merge_commit_verification=(
                pull_request_merge_commit_verification
            ),
            expected_run_id=expected_run_id,
            expected_run_attempt=expected_run_attempt,
        ),
    )


def ci_validation_execution_batch_matrix(  # noqa: PLR0913
    manifest: Mapping[str, object],
    *,
    plan: Mapping[str, object] | None = None,
    request: Mapping[str, object] | None = None,
    changed_files_snapshot: Mapping[str, object] | None = None,
    fact_snapshot: Mapping[str, object] | None = None,
    pull_request_merge_commit_verification: Mapping[str, object] | None = None,
    expected_run_id: str | None = None,
    expected_run_attempt: str | None = None,
    authorizing: bool = True,
) -> dict[str, object]:
    """Return the deterministic matrix include payload for a batch manifest."""
    if authorizing and plan is None:
        raise ContractValidationError(
            [
                ValidationIssue(
                    "plan",
                    "is required to emit authorizing execution matrix rows",
                )
            ]
        )
    validate_ci_validation_execution_batch_manifest(
        manifest,
        plan=plan,
        request=request,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        pull_request_merge_commit_verification=(
            pull_request_merge_commit_verification
        ),
        expected_run_id=expected_run_id,
        expected_run_attempt=expected_run_attempt,
        authorizing=authorizing,
    )
    batches = cast("Sequence[Mapping[str, object]]", manifest["batches"])
    if not authorizing and batches:
        raise ContractValidationError(
            [
                ValidationIssue(
                    "authorizing",
                    "is required to emit non-empty execution matrix rows",
                )
            ]
        )
    workflow = cast("Mapping[str, object]", manifest["run"])["workflow"]
    execution_job = manifest["execution-job"]
    return {
        "include": [
            _execution_batch_matrix_row(
                batch,
                workflow=cast("str", workflow),
                execution_job=cast("str", execution_job),
            )
            for batch in batches
        ]
    }


def _execution_batch_matrix_row(
    batch: Mapping[str, object],
    *,
    workflow: str,
    execution_job: str,
) -> dict[str, object]:
    identity = _execution_batch_matrix_identity(batch)
    return {
        **identity,
        "identity-matrix": dict(identity),
        "expected-job-identity": ci_validation_writer_id(
            workflow=workflow,
            job=execution_job,
            matrix=identity,
        ),
    }


def _execution_batch_matrix_identity(
    batch: Mapping[str, object],
) -> dict[str, object]:
    return {
        "batch-id": batch["batch-id"],
        "runner-family": batch["runner-family"],
        "expected-batch-evidence-bundle-ref": batch[
            "expected-batch-evidence-bundle-ref"
        ],
    }


def _validate_materializer_current_run_inputs(
    *,
    request: object,
    execution_workflow: object,
    expected_run_id: object,
    expected_run_attempt: object,
) -> None:
    issues: list[ValidationIssue] = []
    _validate_non_empty_mapping(request, "request", issues)
    _validate_non_empty_string(execution_workflow, "execution-workflow", issues)
    _validate_non_empty_string(expected_run_id, "expected-run-id", issues)
    _validate_non_empty_string(
        expected_run_attempt, "expected-run-attempt", issues
    )
    if issues:
        raise ContractValidationError(issues)


def _validate_materializer_request_context(
    request: Mapping[str, object],
    plan: Mapping[str, object],
    envelope: CommonEnvelope,
    *,
    expected_run_id: str,
    expected_run_attempt: str,
) -> None:
    expected_ref = ci_validation_request_artifact_ref(
        run_id=expected_run_id,
        run_attempt=expected_run_attempt,
    )
    normalized = validate_ci_validation_request(
        request,
        expected_run_id=expected_run_id,
        expected_run_attempt=expected_run_attempt,
        expected_artifact_ref=expected_ref,
    )
    request_envelope = _envelope(normalized.document, CiValidationKind.REQUEST)
    issues: list[ValidationIssue] = []
    _validate_context_envelope_matches_current(
        request_envelope,
        envelope,
        "request",
        issues,
    )
    plan_request = _mapping(plan["request"])
    if plan_request.get("artifact-ref") != normalized.artifact_ref:
        issues.append(
            ValidationIssue("request.artifact-ref", "must match plan request")
        )
    if plan_request.get("request-digest") != normalized.request_digest:
        issues.append(
            ValidationIssue("request.request-digest", "must match plan request")
        )
    if issues:
        raise ContractValidationError(issues)


def validate_ci_validation_execution_batch_manifest(  # noqa: PLR0913
    manifest: object,
    *,
    plan: Mapping[str, object] | None = None,
    request: Mapping[str, object] | None = None,
    changed_files_snapshot: Mapping[str, object] | None = None,
    fact_snapshot: Mapping[str, object] | None = None,
    pull_request_merge_commit_verification: Mapping[str, object] | None = None,
    expected_envelope: CommonEnvelope | None = None,
    expected_run_id: str | None = None,
    expected_run_attempt: str | None = None,
    authorizing: bool = True,
) -> None:
    """Validate an execution-batch manifest."""
    _validate_ci_validation_execution_batch_manifest(
        manifest,
        plan=plan,
        request=request,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        pull_request_merge_commit_verification=(
            pull_request_merge_commit_verification
        ),
        expected_envelope=expected_envelope,
        expected_run_id=expected_run_id,
        expected_run_attempt=expected_run_attempt,
        authorizing=authorizing,
    )


def _validate_ci_validation_execution_batch_manifest(  # noqa: C901, PLR0913
    manifest: object,
    *,
    plan: Mapping[str, object] | None = None,
    request: Mapping[str, object] | None = None,
    changed_files_snapshot: Mapping[str, object] | None = None,
    fact_snapshot: Mapping[str, object] | None = None,
    pull_request_merge_commit_verification: Mapping[str, object] | None = None,
    expected_envelope: CommonEnvelope | None = None,
    expected_run_id: str | None = None,
    expected_run_attempt: str | None = None,
    authorizing: bool = True,
    _allow_planless_non_authorizing_batches: bool = False,
) -> None:
    """Validate an execution-batch manifest for trusted internal callers."""
    if not isinstance(manifest, Mapping):
        raise ContractValidationError(
            [ValidationIssue("$", "must be an object")],
        )
    issues: list[ValidationIssue] = []
    plan = _validate_optional_mapping_context(plan, "plan", issues)
    _validate_canonical(manifest, "$", issues)
    envelope = _envelope_or_collect(
        manifest,
        CiValidationKind.EXECUTION_BATCH_MANIFEST,
        issues=issues,
    )
    _validate_root_keys_with_optional(
        manifest,
        _EXECUTION_BATCH_MANIFEST_KEYS,
        frozenset({"plan-id", "plan-digest"}),
        "$",
        issues,
    )
    _validate_g1_schema_diagnostics(manifest.get("schema-diagnostics"), issues)
    _validate_expected_run(
        envelope, expected_run_id, expected_run_attempt, issues
    )
    _validate_non_empty_string(
        manifest.get("execution-job"), "$.execution-job", issues
    )
    if envelope is not None and expected_envelope is not None:
        _validate_context_envelope_matches_current(
            envelope,
            expected_envelope,
            "$",
            issues,
        )
    plan_work_groups: dict[str, Mapping[str, object]] = {}
    plan_evidence_expectations: dict[str, Mapping[str, object]] = {}
    executable_work_group_ids: set[str] | None = None
    plan_context_valid = True
    if plan is not None:
        plan_context_valid = _validate_plan_context(
            plan,
            request,
            changed_files_snapshot,
            fact_snapshot,
            pull_request_merge_commit_verification,
            expected_run_id,
            expected_run_attempt,
            issues,
            require_authorizing_context=authorizing,
        )
        plan_envelope = _validated_plan_envelope(plan, issues)
        if envelope is not None and plan_envelope is not None:
            _validate_envelope_matches(envelope, plan_envelope, issues)
        if manifest.get("plan-id") != plan.get("plan-id"):
            issues.append(ValidationIssue("$.plan-id", "must match plan"))
        if manifest.get("plan-digest") != _verified_plan_digest_or_none(plan):
            issues.append(ValidationIssue("$.plan-digest", "must match plan"))
        if plan_context_valid:
            plan_work_groups = _work_groups_by_id(plan)
            plan_evidence_expectations = _evidence_expectations_by_id(plan)
            executable_work_group_ids = {
                item_id
                for item_id, group in plan_work_groups.items()
                if group.get("kind") != "evidence-aggregation"
            }
    batches = _validate_batches(
        manifest.get("batches"),
        envelope,
        plan_work_groups,
        plan_evidence_expectations,
        executable_work_group_ids,
        issues,
    )
    _validate_planless_non_empty_batches(
        plan=plan,
        authorizing=authorizing,
        allow_planless_non_authorizing_batches=(
            _allow_planless_non_authorizing_batches
        ),
        batches=batches,
        issues=issues,
    )
    if plan is None:
        _validate_planless_manifest_identity(
            manifest,
            batches,
            issues,
        )
    if plan is not None and plan_context_valid:
        _validate_plan_bound_batch_materialization(
            batches,
            plan,
            manifest.get("budget"),
            envelope,
            manifest.get("execution-job"),
            issues,
        )
    _validate_batch_writer_identities(
        batches,
        envelope,
        manifest.get("execution-job"),
        issues,
    )
    _validate_budget(
        manifest.get("budget"),
        len(batches),
        batches,
        plan_work_groups,
        plan if plan_context_valid else None,
        issues,
    )
    if issues:
        raise ContractValidationError(issues)


def _validate_planless_non_empty_batches(
    *,
    plan: Mapping[str, object] | None,
    authorizing: bool,
    allow_planless_non_authorizing_batches: bool,
    batches: Sequence[Mapping[str, object]],
    issues: list[ValidationIssue],
) -> None:
    if plan is None and not batches and authorizing:
        issues.append(
            ValidationIssue(
                "authorizing",
                "requires explicit non-authorizing mode for planless "
                "zero-batch manifests",
            )
        )
    if plan is None and batches and not allow_planless_non_authorizing_batches:
        issues.append(
            ValidationIssue(
                "authorizing",
                "requires plan context for non-empty execution batches",
            )
        )


def _validate_planless_manifest_identity(
    manifest: Mapping[str, object],
    batches: Sequence[Mapping[str, object]],
    issues: list[ValidationIssue],
) -> None:
    if batches:
        _validate_non_empty_string(manifest.get("plan-id"), "$.plan-id", issues)
        _validate_digest(manifest.get("plan-digest"), "$.plan-digest", issues)
        return
    if manifest.get("plan-id") is not None:
        issues.append(
            ValidationIssue(
                "$.plan-id",
                "must be null for planless zero-batch manifests",
            )
        )
    if manifest.get("plan-digest") is not None:
        issues.append(
            ValidationIssue(
                "$.plan-digest",
                "must be null for planless zero-batch manifests",
            )
        )


def _validate_plan_context(  # noqa: PLR0913
    plan: Mapping[str, object],
    request: Mapping[str, object] | None,
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    pull_request_merge_commit_verification: Mapping[str, object] | None,
    expected_run_id: str | None,
    expected_run_attempt: str | None,
    issues: list[ValidationIssue],
    *,
    require_authorizing_context: bool = False,
) -> bool:
    authorizing_context_requested = require_authorizing_context or any(
        item is not None
        for item in (
            request,
            changed_files_snapshot,
            fact_snapshot,
            pull_request_merge_commit_verification,
        )
    )
    if authorizing_context_requested:
        if request is None:
            issues.append(
                ValidationIssue(
                    "request",
                    "is required for authorizing plan-bound manifest",
                )
            )
        if not isinstance(expected_run_id, str) or expected_run_id == "":
            issues.append(
                ValidationIssue(
                    "expected-run-id",
                    "is required for authorizing plan-bound manifest",
                )
            )
        if (
            not isinstance(expected_run_attempt, str)
            or expected_run_attempt == ""
        ):
            issues.append(
                ValidationIssue(
                    "expected-run-attempt",
                    "is required for authorizing plan-bound manifest",
                )
            )
        try:
            validate_ci_validation_plan(
                plan,
                changed_files_snapshot=changed_files_snapshot,
                fact_snapshot=fact_snapshot,
                pull_request_merge_commit_verification=(
                    pull_request_merge_commit_verification
                ),
                expected_run_id=expected_run_id,
                expected_run_attempt=expected_run_attempt,
            )
        except ContractValidationError as error:
            issues.extend(error.issues)
        plan_envelope = _validated_plan_envelope(plan, issues)
        if (
            request is not None
            and expected_run_id is not None
            and expected_run_attempt is not None
            and plan_envelope is not None
        ):
            try:
                _validate_materializer_request_context(
                    request,
                    plan,
                    plan_envelope,
                    expected_run_id=expected_run_id,
                    expected_run_attempt=expected_run_attempt,
                )
            except ContractValidationError as error:
                issues.extend(error.issues)
    else:
        try:
            validate_ci_validation_plan_structure(plan)
        except ContractValidationError as error:
            issues.extend(error.issues)
    return _validate_plan_context_canonical_arrays(plan, issues)


def _authorizing_context_supplied(
    *,
    request: Mapping[str, object] | None,
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
) -> bool:
    return any(
        item is not None
        for item in (
            request,
            changed_files_snapshot,
            fact_snapshot,
        )
    )


def freeze_ci_validation_batch_evidence_bundle(  # noqa: PLR0913
    *,
    plan: Mapping[str, object],
    execution_batch_manifest: Mapping[str, object],
    batch_id: str,
    selector_results: Sequence[Mapping[str, object]],
    writer: Mapping[str, object],
    execution_tree: Mapping[str, object],
    started_at: str,
    completed_at: str,
    created_at: str,
    batch_diagnostics: Sequence[Mapping[str, object]] = (),
    request: Mapping[str, object] | None = None,
    changed_files_snapshot: Mapping[str, object] | None = None,
    fact_snapshot: Mapping[str, object] | None = None,
    expected_run_id: str | None = None,
    expected_run_attempt: str | None = None,
    dependency_evidence_bundles: Sequence[Mapping[str, object]] = (),
) -> dict[str, object]:
    """Freeze a validation-only evidence bundle for one execution batch."""
    authorizing_context_supplied = _authorizing_context_supplied(
        request=request,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
    )
    validate_ci_validation_execution_batch_manifest(
        execution_batch_manifest,
        plan=plan,
        request=request,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        expected_run_id=expected_run_id,
        expected_run_attempt=expected_run_attempt,
        authorizing=authorizing_context_supplied,
    )
    envelope = _envelope(plan, CiValidationKind.PLAN)
    batch = _batch_by_id(execution_batch_manifest, batch_id)
    manifest_digest = ci_validation_execution_batch_manifest_payload_digest(
        execution_batch_manifest,
    )
    artifact_ref = ci_validation_batch_evidence_bundle_artifact_ref(
        run_id=envelope.run_id,
        run_attempt=envelope.run_attempt,
        batch_id=batch_id,
    )
    bundle = {
        "api-version": API_VERSIONS_BY_KIND[
            CiValidationKind.BATCH_EVIDENCE_BUNDLE.value
        ],
        "kind": CiValidationKind.BATCH_EVIDENCE_BUNDLE.value,
        "created-at": created_at,
        "repository": {
            "owner": envelope.repository_owner,
            "name": envelope.repository_name,
        },
        "run": {
            "workflow": envelope.workflow,
            "run-id": envelope.run_id,
            "run-attempt": envelope.run_attempt,
        },
        "schema-diagnostics": [],
        "artifact-ref": artifact_ref,
        "bundle-id": ci_validation_batch_evidence_bundle_id(
            run_id=envelope.run_id,
            run_attempt=envelope.run_attempt,
            batch_id=batch_id,
            execution_batch_manifest_digest=manifest_digest,
            artifact_ref=artifact_ref,
        ),
        "plan-id": plan["plan-id"],
        "plan-digest": _verified_plan_digest(plan),
        "mode": plan["mode"],
        "validation-tree": dict(_mapping(plan["validation-tree"])),
        "affected-range": _summary_affected_range(plan),
        "scheduled-full": dict(_mapping(plan["scheduled-full"])),
        "execution-batch-manifest": {
            "artifact-ref": ci_validation_execution_batch_manifest_artifact_ref(
                run_id=envelope.run_id,
                run_attempt=envelope.run_attempt,
            ),
            "content-digest": manifest_digest,
        },
        "batch": _bundle_batch_projection(batch),
        "writer": dict(writer),
        "execution-tree": dict(execution_tree),
        "started-at": started_at,
        "completed-at": completed_at,
        "selector-results": [dict(item) for item in selector_results],
        "batch-diagnostics": sorted(
            (dict(item) for item in batch_diagnostics),
            key=lambda item: str(item.get("diagnostic-id")),
        ),
        "proof-admissibility": _PROOF_ADMISSIBILITY,
    }
    validate_ci_validation_batch_evidence_bundle(
        bundle,
        plan=plan,
        request=request,
        execution_batch_manifest=execution_batch_manifest,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        expected_run_id=expected_run_id,
        expected_run_attempt=expected_run_attempt,
        dependency_evidence_bundles=dependency_evidence_bundles,
    )
    return bundle


def validate_ci_validation_batch_evidence_bundle(  # noqa: PLR0913
    bundle: object,
    *,
    plan: Mapping[str, object] | None = None,
    request: Mapping[str, object] | None = None,
    execution_batch_manifest: Mapping[str, object] | None = None,
    changed_files_snapshot: Mapping[str, object] | None = None,
    fact_snapshot: Mapping[str, object] | None = None,
    expected_run_id: str | None = None,
    expected_run_attempt: str | None = None,
    dependency_evidence_bundles: Sequence[Mapping[str, object]] = (),
) -> None:
    """Validate a batch evidence bundle."""
    if not isinstance(bundle, Mapping):
        raise ContractValidationError(
            [ValidationIssue("$", "must be an object")],
        )
    issues: list[ValidationIssue] = []
    plan = _validate_optional_mapping_context(plan, "plan", issues)
    execution_batch_manifest = _validate_optional_mapping_context(
        execution_batch_manifest, "execution_batch_manifest", issues
    )
    request = _validate_optional_mapping_context(request, "request", issues)
    changed_files_snapshot = _validate_optional_mapping_context(
        changed_files_snapshot, "changed_files_snapshot", issues
    )
    fact_snapshot = _validate_optional_mapping_context(
        fact_snapshot, "fact_snapshot", issues
    )
    dependency_evidence_bundles = (
        _validate_optional_mapping_sequence_context(
            dependency_evidence_bundles,
            "dependency_evidence_bundles",
            issues,
        )
        or []
    )
    authorizing_context_supplied = _authorizing_context_supplied(
        request=request,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
    )
    if execution_batch_manifest is not None and plan is not None:
        validate_ci_validation_execution_batch_manifest(
            execution_batch_manifest,
            plan=plan,
            request=request,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            expected_run_id=expected_run_id,
            expected_run_attempt=expected_run_attempt,
            authorizing=authorizing_context_supplied,
        )
    _validate_canonical(bundle, "$", issues)
    envelope = _envelope_or_collect(
        bundle,
        CiValidationKind.BATCH_EVIDENCE_BUNDLE,
        issues,
    )
    _validate_root_keys(bundle, _BATCH_EVIDENCE_BUNDLE_KEYS, "$", issues)
    _validate_g1_schema_diagnostics(bundle.get("schema-diagnostics"), issues)
    _validate_expected_run(
        envelope, expected_run_id, expected_run_attempt, issues
    )
    batch: Mapping[str, object] | None = None
    if plan is not None:
        _validate_bundle_plan_fields(bundle, plan, envelope, issues)
    else:
        _validate_non_empty_string(bundle.get("plan-id"), "$.plan-id", issues)
        _validate_digest(bundle.get("plan-digest"), "$.plan-digest", issues)
    _validate_bundle_manifest_claim(
        bundle.get("execution-batch-manifest"),
        envelope,
        execution_batch_manifest,
        issues,
    )
    _validate_bundle_batch_projection_shape(bundle.get("batch"), issues)
    if execution_batch_manifest is not None:
        batch = _validate_bundle_manifest_fields(
            bundle,
            execution_batch_manifest,
            envelope,
            plan,
            request,
            changed_files_snapshot,
            fact_snapshot,
            expected_run_id,
            expected_run_attempt,
            _authorizing_context_supplied(
                request=request,
                changed_files_snapshot=changed_files_snapshot,
                fact_snapshot=fact_snapshot,
            ),
            issues,
        )
    _validate_bundle_artifact_ref(bundle, envelope, batch, issues)
    _validate_object(
        bundle.get("writer"),
        _BUNDLE_WRITER_KEYS,
        "$.writer",
        issues,
    )
    _validate_writer(bundle.get("writer"), "$.writer", issues)
    _validate_bundle_writer_matches_batch(
        bundle.get("writer"),
        batch,
        execution_batch_manifest,
        issues,
    )
    _validate_object(
        bundle.get("execution-tree"),
        _EXECUTION_TREE_KEYS,
        "$.execution-tree",
        issues,
    )
    _validate_execution_tree(
        bundle.get("execution-tree"),
        bundle.get("validation-tree"),
        issues,
    )
    _validate_non_empty_string(bundle.get("started-at"), "$.started-at", issues)
    _validate_non_empty_string(
        bundle.get("completed-at"),
        "$.completed-at",
        issues,
    )
    _validate_diagnostics(
        bundle.get("batch-diagnostics"),
        "$.batch-diagnostics",
        issues,
    )
    _validate_selector_results(
        bundle.get("selector-results"),
        batch,
        execution_batch_manifest,
        bundle,
        plan,
        fact_snapshot,
        issues,
    )
    if authorizing_context_supplied:
        _validate_authorizing_batch_dependency_evidence(
            bundle,
            batch,
            execution_batch_manifest,
            dependency_evidence_bundles,
            plan=plan,
            request=request,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            expected_run_id=expected_run_id,
            expected_run_attempt=expected_run_attempt,
            issues=issues,
        )
    if bundle.get("proof-admissibility") != _PROOF_ADMISSIBILITY:
        issues.append(
            ValidationIssue(
                "$.proof-admissibility",
                "must be validation-only",
            ),
        )
    if issues:
        raise ContractValidationError(issues)


def freeze_ci_validation_aggregate_evidence_manifest(  # noqa: PLR0913
    *,
    created_at: str,
    repository_owner: str,
    repository_name: str,
    workflow: str,
    run_id: str,
    run_attempt: str,
    input_artifacts: Mapping[str, object],
    batch_bundles: Sequence[Mapping[str, object]],
    unexpected_contract_artifacts: Sequence[Mapping[str, object]],
    namespace_overflow: Mapping[str, object],
    pre_final_validation_artifacts: int,
    namespace_closed_at: str,
    plan: Mapping[str, object] | None = None,
    execution_batch_manifest: Mapping[str, object] | None = None,
    request: Mapping[str, object] | None = None,
    changed_files_snapshot: Mapping[str, object] | None = None,
    fact_snapshot: Mapping[str, object] | None = None,
) -> dict[str, object]:
    """Freeze the pre-final aggregate evidence manifest."""
    return _freeze_ci_validation_aggregate_evidence_manifest(
        created_at=created_at,
        repository_owner=repository_owner,
        repository_name=repository_name,
        workflow=workflow,
        run_id=run_id,
        run_attempt=run_attempt,
        input_artifacts=input_artifacts,
        batch_bundles=batch_bundles,
        unexpected_contract_artifacts=unexpected_contract_artifacts,
        namespace_overflow=namespace_overflow,
        pre_final_validation_artifacts=pre_final_validation_artifacts,
        namespace_closed_at=namespace_closed_at,
        plan=plan,
        execution_batch_manifest=execution_batch_manifest,
        request=request,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        _require_authoritative_snapshot_inputs=True,
    )


def _freeze_ci_validation_aggregate_evidence_manifest(  # noqa: PLR0913
    *,
    created_at: str,
    repository_owner: str,
    repository_name: str,
    workflow: str,
    run_id: str,
    run_attempt: str,
    input_artifacts: Mapping[str, object],
    batch_bundles: Sequence[Mapping[str, object]],
    unexpected_contract_artifacts: Sequence[Mapping[str, object]],
    namespace_overflow: Mapping[str, object],
    pre_final_validation_artifacts: int,
    namespace_closed_at: str,
    plan: Mapping[str, object] | None = None,
    execution_batch_manifest: Mapping[str, object] | None = None,
    request: Mapping[str, object] | None = None,
    changed_files_snapshot: Mapping[str, object] | None = None,
    fact_snapshot: Mapping[str, object] | None = None,
    _require_authoritative_snapshot_inputs: bool = True,
) -> dict[str, object]:
    """Freeze an aggregate evidence manifest for trusted internal callers."""
    plan_id = (
        plan.get("plan-id")
        if plan is not None
        else (
            execution_batch_manifest.get("plan-id")
            if execution_batch_manifest is not None
            else None
        )
    )
    plan_digest = (
        _verified_plan_digest(plan)
        if plan is not None
        else (
            execution_batch_manifest.get("plan-digest")
            if execution_batch_manifest is not None
            else None
        )
    )
    manifest = {
        "api-version": API_VERSIONS_BY_KIND[
            CiValidationKind.AGGREGATE_EVIDENCE_MANIFEST.value
        ],
        "kind": CiValidationKind.AGGREGATE_EVIDENCE_MANIFEST.value,
        "created-at": created_at,
        "repository": {"owner": repository_owner, "name": repository_name},
        "run": {
            "workflow": workflow,
            "run-id": run_id,
            "run-attempt": run_attempt,
        },
        "schema-diagnostics": [],
        "artifact-ref": ci_validation_aggregate_evidence_manifest_artifact_ref(
            run_id=run_id,
            run_attempt=run_attempt,
        ),
        "plan-id": plan_id,
        "plan-digest": plan_digest,
        "input-artifacts": dict(input_artifacts),
        "batch-bundles": sorted(
            (dict(item) for item in batch_bundles),
            key=_aggregate_batch_bundle_sort_key,
        ),
        "unexpected-contract-artifacts": sorted(
            (dict(item) for item in unexpected_contract_artifacts),
            key=lambda item: _unexpected_implicit_id(
                item,
                run_id=run_id,
                run_attempt=run_attempt,
            ),
        ),
        "namespace-overflow": dict(namespace_overflow),
        "projection-authority": (
            _projection_authority_from_plan(plan)
            if plan is not None
            and (
                _input_artifacts_have_projection_authority(
                    input_artifacts,
                    plan=plan,
                )
                or (
                    request is not None
                    and _input_artifacts_have_retained_projection_authority(
                        input_artifacts,
                        plan=plan,
                    )
                )
            )
            else None
        ),
        "pre-final-validation-artifacts": pre_final_validation_artifacts,
        "namespace-closed-at": namespace_closed_at,
        "proof-admissibility": _PROOF_ADMISSIBILITY,
    }
    _validate_ci_validation_aggregate_evidence_manifest(
        manifest,
        plan=plan,
        execution_batch_manifest=execution_batch_manifest,
        request=request,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        _require_authoritative_snapshot_inputs=(
            _require_authoritative_snapshot_inputs
        ),
        _require_context_proof_for_valid_inputs=(
            _require_authoritative_snapshot_inputs
        ),
    )
    return manifest


def validate_ci_validation_aggregate_evidence_manifest(  # noqa: PLR0913
    manifest: object,
    *,
    plan: Mapping[str, object] | None = None,
    execution_batch_manifest: Mapping[str, object] | None = None,
    request: Mapping[str, object] | None = None,
    changed_files_snapshot: Mapping[str, object] | None = None,
    fact_snapshot: Mapping[str, object] | None = None,
    frozen_input_digests: Mapping[str, str] | None = None,
    expected_run_id: str | None = None,
    expected_run_attempt: str | None = None,
) -> None:
    """Validate an aggregate evidence manifest."""
    _validate_ci_validation_aggregate_evidence_manifest(
        manifest,
        plan=plan,
        execution_batch_manifest=execution_batch_manifest,
        request=request,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        frozen_input_digests=frozen_input_digests,
        expected_run_id=expected_run_id,
        expected_run_attempt=expected_run_attempt,
    )


def _validate_ci_validation_aggregate_evidence_manifest(  # noqa: PLR0913,PLR0915
    manifest: object,
    *,
    plan: Mapping[str, object] | None = None,
    execution_batch_manifest: Mapping[str, object] | None = None,
    request: Mapping[str, object] | None = None,
    changed_files_snapshot: Mapping[str, object] | None = None,
    fact_snapshot: Mapping[str, object] | None = None,
    frozen_input_digests: Mapping[str, str] | None = None,
    expected_run_id: str | None = None,
    expected_run_attempt: str | None = None,
    _require_authoritative_snapshot_inputs: bool = True,
    _require_context_proof_for_valid_inputs: bool = True,
) -> None:
    """Validate an aggregate evidence manifest for trusted internal callers."""
    if not isinstance(manifest, Mapping):
        raise ContractValidationError(
            [ValidationIssue("$", "must be an object")],
        )
    issues: list[ValidationIssue] = []
    plan = _validate_optional_mapping_context(plan, "plan", issues)
    execution_batch_manifest = _validate_optional_mapping_context(
        execution_batch_manifest, "execution_batch_manifest", issues
    )
    request = _validate_optional_mapping_context(request, "request", issues)
    changed_files_snapshot = _validate_optional_mapping_context(
        changed_files_snapshot, "changed_files_snapshot", issues
    )
    fact_snapshot = _validate_optional_mapping_context(
        fact_snapshot, "fact_snapshot", issues
    )
    frozen_input_digests = _validate_optional_str_mapping_context(
        frozen_input_digests, "frozen_input_digests", issues
    )
    _validate_canonical(manifest, "$", issues, fail_closed=True)
    envelope = _envelope_or_collect(
        manifest,
        CiValidationKind.AGGREGATE_EVIDENCE_MANIFEST,
        issues,
    )
    _validate_root_keys(
        manifest,
        _AGGREGATE_EVIDENCE_MANIFEST_KEYS,
        "$",
        issues,
    )
    _validate_g1_schema_diagnostics(manifest.get("schema-diagnostics"), issues)
    _validate_expected_run(
        envelope, expected_run_id, expected_run_attempt, issues
    )
    _validate_aggregate_manifest_ref(manifest, envelope, issues)
    no_authority_invalid_plan = (
        _aggregate_manifest_has_true_no_authority_invalid_plan(manifest)
    )
    planless_manifest_has_no_authoritative_plan = (
        _aggregate_manifest_lacks_planless_retained_authority(manifest)
    )
    _validate_plan_nullable_fields(
        manifest,
        None
        if no_authority_invalid_plan
        or planless_manifest_has_no_authoritative_plan
        else plan,
        envelope,
        issues,
    )
    request_context_digest = (
        _validated_request_context_digest_or_none(
            request,
            envelope,
            issues,
        )
        if _aggregate_input_admissibility(manifest, "request") == "valid"
        else None
    )
    request_input_proven = _input_artifact_authorizes_supplied_document(
        manifest,
        "request",
        envelope,
        request_context_digest,
    )
    changed_files_snapshot_context_hash = (
        _validated_changed_files_snapshot_hash_or_none(
            changed_files_snapshot,
            envelope,
            issues,
        )
    )
    changed_files_snapshot_input_proven = (
        _input_artifact_authorizes_supplied_document(
            manifest,
            "changed-files-snapshot",
            envelope,
            changed_files_snapshot_context_hash,
        )
    )
    fact_snapshot_input_proven = (
        _aggregate_context_input_proven_or_not_required(
            manifest,
            "fact-snapshot",
            envelope,
            fact_snapshot,
            _fact_snapshot_context_id_for_authority(fact_snapshot),
        )
    )
    supplied_execution_context_proven = (
        request_input_proven
        and _aggregate_context_input_proven_or_not_required(
            manifest,
            "changed-files-snapshot",
            envelope,
            changed_files_snapshot,
            changed_files_snapshot_context_hash,
        )
        and fact_snapshot_input_proven
    )
    if no_authority_invalid_plan or planless_manifest_has_no_authoritative_plan:
        _validate_null_plan_identity(manifest, "$", issues)
    execution_batch_manifest_proven = False
    if (
        execution_batch_manifest is not None
        and not planless_manifest_has_no_authoritative_plan
    ):
        execution_batch_manifest_proven = (
            _validate_supplied_aggregate_execution_batch_manifest(
                manifest,
                execution_batch_manifest,
                plan,
                envelope,
                request=request if supplied_execution_context_proven else None,
                changed_files_snapshot=changed_files_snapshot
                if supplied_execution_context_proven
                else None,
                fact_snapshot=fact_snapshot
                if supplied_execution_context_proven
                else None,
                issues=issues,
            )
        )
    invalid_plan_input_names = _invalid_plan_input_failure_input_names(manifest)
    expected_fact_snapshot_plan_id = (
        None
        if invalid_plan_input_names
        else _expected_context_plan_id(
            plan,
            execution_batch_manifest,
            changed_files_snapshot=changed_files_snapshot,
            changed_files_snapshot_context_hash=(
                changed_files_snapshot_context_hash
            ),
            changed_files_snapshot_input_proven=(
                changed_files_snapshot_input_proven
            ),
            fact_snapshot=fact_snapshot,
            envelope=envelope,
            execution_batch_manifest_proven=execution_batch_manifest_proven,
            issues=issues,
        )
    )
    retained_invalid_plan_context_authorized = isinstance(
        plan, Mapping
    ) and _supplied_plan_input_authorizes_retained_invalid_plan_projection(
        manifest,
        envelope,
        plan,
        request=request if request_input_proven else None,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
    )
    if (
        expected_fact_snapshot_plan_id is None
        and isinstance(plan, Mapping)
        and retained_invalid_plan_context_authorized
        and isinstance(plan.get("plan-id"), str)
    ):
        expected_fact_snapshot_plan_id = cast("str", plan["plan-id"])
    if (
        expected_fact_snapshot_plan_id is None
        and not invalid_plan_input_names
        and isinstance(plan, Mapping)
        and fact_snapshot is not None
        and changed_files_snapshot is None
        and _plan_requires_changed_files_snapshot(plan)
    ):
        issues.append(
            ValidationIssue(
                "$.changed-files-snapshot",
                "companion is required",
            )
        )
    fact_snapshot_context_id = _validated_fact_snapshot_id_or_none(
        fact_snapshot,
        envelope,
        issues,
        expected_plan_id=expected_fact_snapshot_plan_id,
    )
    if not invalid_plan_input_names:
        _validate_supplied_plan_document_for_aggregate(
            plan,
            envelope,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            issues=issues,
        )
    require_context_proof_for_valid_inputs = (
        _require_context_proof_for_valid_inputs
        and not invalid_plan_input_names
        and manifest.get("projection-authority") is not None
    )
    require_authoritative_snapshot_inputs = (
        _require_authoritative_snapshot_inputs and not invalid_plan_input_names
    )
    input_artifact_count = _validate_input_artifacts(
        manifest.get("input-artifacts"),
        envelope,
        plan,
        execution_batch_manifest,
        request_context_digest,
        changed_files_snapshot_context_hash,
        fact_snapshot_context_id,
        changed_files_snapshot_input_proven=(
            changed_files_snapshot_input_proven
        ),
        plan_fact_snapshot_binding_proven=(
            expected_fact_snapshot_plan_id is not None
        ),
        require_authoritative_snapshot_inputs=require_authoritative_snapshot_inputs,
        frozen_input_digests=frozen_input_digests,
        require_context_proof_for_valid_inputs=require_context_proof_for_valid_inputs,
        issues=issues,
    )
    _validate_valid_execution_batch_manifest_input_has_document(
        manifest,
        execution_batch_manifest,
        issues,
    )
    _validate_valid_validation_plan_input_has_document(
        manifest,
        plan,
        issues,
    )
    if (
        plan is None
        and not _aggregate_manifest_lacks_planless_retained_authority(manifest)
    ):
        _validate_standalone_aggregate_manifest_plan_identity(manifest, issues)
    batch_bundle_count = _validate_batch_bundle_slots(
        manifest,
        manifest.get("batch-bundles"),
        envelope,
        plan,
        request,
        changed_files_snapshot,
        fact_snapshot,
        None
        if _invalid_plan_input_failure_details(manifest)
        else execution_batch_manifest,
        issues,
    )
    unexpected_artifact_count = _validate_unexpected_artifacts(
        manifest.get("unexpected-contract-artifacts"),
        envelope,
        issues,
    )
    _validate_namespace_overflow(
        manifest.get("namespace-overflow"),
        input_artifact_count + batch_bundle_count + unexpected_artifact_count,
        issues,
    )
    _validate_projection_authority(
        manifest.get("projection-authority"),
        "$.projection-authority",
        issues,
    )
    _validate_aggregate_manifest_projection_authority(
        manifest,
        envelope,
        plan,
        request,
        changed_files_snapshot,
        fact_snapshot,
        issues,
    )
    _validate_pre_final_validation_artifacts(
        manifest.get("pre-final-validation-artifacts"),
        input_artifact_count,
        batch_bundle_count,
        issues,
    )
    _validate_non_empty_string(
        manifest.get("namespace-closed-at"),
        "$.namespace-closed-at",
        issues,
    )
    if manifest.get("proof-admissibility") != _PROOF_ADMISSIBILITY:
        issues.append(
            ValidationIssue(
                "$.proof-admissibility",
                "must be validation-only",
            ),
        )
    if issues:
        raise ContractValidationError(issues)


def _validate_supplied_aggregate_execution_batch_manifest(  # noqa: PLR0913
    manifest: Mapping[str, object],
    execution_batch_manifest: Mapping[str, object],
    plan: Mapping[str, object] | None,
    envelope: CommonEnvelope | None,
    *,
    request: Mapping[str, object] | None,
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> bool:
    execution_manifest_issue_count = len(issues)
    try:
        _validate_ci_validation_execution_batch_manifest(
            execution_batch_manifest,
            plan=plan,
            request=request,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            expected_envelope=envelope,
            expected_run_id=envelope.run_id if envelope is not None else None,
            expected_run_attempt=(
                envelope.run_attempt if envelope is not None else None
            ),
            authorizing=False,
            _allow_planless_non_authorizing_batches=(
                _allow_planless_execution_manifest_diagnostic(
                    plan,
                    execution_batch_manifest,
                )
            ),
        )
    except ContractValidationError as error:
        issues.extend(error.issues)
    _validate_plan_identity_matches(
        manifest,
        execution_batch_manifest,
        "$",
        "execution-batch manifest",
        issues,
    )
    if plan is None:
        _valid_context_plan_id_or_none(
            execution_batch_manifest.get("plan-id"),
            "execution_batch_manifest.plan-id",
            issues,
        )
    return (
        plan is not None
        and len(issues) == execution_manifest_issue_count
        and _input_artifact_authorizes_supplied_document(
            manifest,
            "execution-batch-manifest",
            envelope,
            ci_validation_execution_batch_manifest_payload_digest(
                execution_batch_manifest,
            ),
        )
    )


def freeze_ci_validation_aggregate_summary(  # noqa: PLR0913,PLR0915
    *,
    created_at: str,
    repository_owner: str,
    repository_name: str,
    workflow: str,
    run_id: str,
    run_attempt: str,
    aggregate_evidence_manifest: Mapping[str, object],
    final_artifacts: Mapping[str, object],
    validation_tree: Mapping[str, object],
    affected_range: Mapping[str, object],
    request: Mapping[str, object],
    scheduled_full: Mapping[str, object],
    verdict: str,
    reason: Mapping[str, object],
    budgets: Mapping[str, object],
    diagnostics: Sequence[Mapping[str, object]],
    batch_bundles: Sequence[Mapping[str, object]],
    evidence_results: Sequence[Mapping[str, object]],
    failures: Sequence[Mapping[str, object]],
    work_groups: Mapping[str, object],
    plan: Mapping[str, object] | None = None,
    aggregate_evidence_manifest_document: Mapping[str, object] | None = None,
    admitted_batch_evidence_bundles: Sequence[Mapping[str, object]]
    | None = None,
    execution_batch_manifest: Mapping[str, object] | None = None,
    request_document: Mapping[str, object] | None = None,
    changed_files_snapshot: Mapping[str, object] | None = None,
    fact_snapshot: Mapping[str, object] | None = None,
    aggregate_evidence_manifest_bound: bool | None = None,
    aggregate_evidence_manifest_external_binding_verified: bool = False,
    aggregate_manifest_authority_failure_details: Sequence[str] | None = None,
) -> dict[str, object]:
    """Freeze the final aggregate summary bound to an evidence manifest."""
    input_final_manifest = final_artifacts.get("aggregate-evidence-manifest")
    externally_bound_authority_failure_details = {
        detail
        for detail in aggregate_manifest_authority_failure_details or ()
        if detail in _AGGREGATE_MANIFEST_AUTHORITY_DETAILS
    }
    raw_aggregate_manifest_evidence_bound = (
        aggregate_evidence_manifest_document is not None
        and aggregate_evidence_manifest_bound is not False
    )
    raw_authority_failure_details = (
        _aggregate_manifest_authority_failure_details_from_final_manifest(
            input_final_manifest,
        )
        & externally_bound_authority_failure_details
    )
    final_aggregate_manifest_authority_bound = (
        raw_aggregate_manifest_evidence_bound
    )
    invalid_plan_input_detail = _freezer_invalid_plan_input_detail(
        aggregate_evidence_manifest_document,
        failures,
        aggregate_manifest_evidence_bound=raw_aggregate_manifest_evidence_bound,
    )
    retained_invalid_plan_context = (
        plan is not None
        and aggregate_evidence_manifest_document is not None
        and final_aggregate_manifest_authority_bound
        and _invalid_plan_detail_allows_retained_plan_context(
            invalid_plan_input_detail
        )
        and _supplied_plan_input_authorizes_retained_invalid_plan_projection(
            aggregate_evidence_manifest_document,
            _aggregate_manifest_envelope_or_none(
                aggregate_evidence_manifest_document,
            ),
            plan,
            request=request_document,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
        )
    )
    invalid_plan_uses_no_authority_projection = (
        invalid_plan_input_detail is not None
        and not retained_invalid_plan_context
    )
    if (
        plan is not None
        and _invalid_plan_detail_allows_retained_plan_context(
            invalid_plan_input_detail
        )
        and not retained_invalid_plan_context
        and _invalid_plan_context_has_complete_retained_projection(plan)
    ):
        raise ContractValidationError(
            [
                ValidationIssue(
                    "$.projection-authority",
                    "retained invalid-plan details require aggregate "
                    "manifest input authority",
                )
            ]
        )
    aggregate_manifest_plan_unbound = invalid_plan_input_detail is None and (
        not final_aggregate_manifest_authority_bound
        or (
            aggregate_evidence_manifest_document is not None
            and _aggregate_manifest_has_no_authoritative_plan(
                aggregate_evidence_manifest_document
            )
        )
    )
    authority_plan = (
        None
        if invalid_plan_uses_no_authority_projection
        or aggregate_manifest_plan_unbound
        else plan
    )
    plan_id = (
        None
        if invalid_plan_uses_no_authority_projection
        or aggregate_manifest_plan_unbound
        else _summary_plan_identity_value(
            "plan-id",
            plan,
            aggregate_evidence_manifest_document,
        )
    )
    plan_digest = (
        plan.get("plan-digest")
        if retained_invalid_plan_context and plan is not None
        else None
        if invalid_plan_uses_no_authority_projection
        or aggregate_manifest_plan_unbound
        else _summary_plan_identity_value(
            "plan-digest",
            plan,
            aggregate_evidence_manifest_document,
        )
        if invalid_plan_input_detail is not None
        else _verified_plan_digest(plan)
        if plan is not None
        else _summary_plan_identity_value(
            "plan-digest",
            plan,
            aggregate_evidence_manifest_document,
        )
    )
    projection = _summary_projection_from_authority(
        plan=authority_plan,
        aggregate_evidence_manifest=aggregate_evidence_manifest_document,
        request=request_document,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        fallback={
            "mode": "unknown",
            "validation-tree": dict(validation_tree),
            "affected-range": dict(affected_range),
            "request": dict(request),
            "scheduled-full": dict(scheduled_full),
        },
    )
    summary = {
        "api-version": API_VERSIONS_BY_KIND[
            CiValidationKind.AGGREGATE_SUMMARY.value
        ],
        "kind": CiValidationKind.AGGREGATE_SUMMARY.value,
        "created-at": created_at,
        "repository": {"owner": repository_owner, "name": repository_name},
        "run": {
            "workflow": workflow,
            "run-id": run_id,
            "run-attempt": run_attempt,
        },
        "schema-diagnostics": [],
        "artifact-ref": ci_validation_aggregate_summary_artifact_ref(
            run_id=run_id,
            run_attempt=run_attempt,
        ),
        "plan-id": plan_id,
        "plan-digest": plan_digest,
        "mode": projection["mode"],
        "aggregate-evidence-manifest": dict(aggregate_evidence_manifest),
        "final-artifacts": dict(final_artifacts),
        "validation-tree": dict(_mapping(projection["validation-tree"])),
        "affected-range": dict(_mapping(projection["affected-range"])),
        "request": dict(_mapping(projection["request"])),
        "scheduled-full": dict(_mapping(projection["scheduled-full"])),
        "verdict": verdict,
        "reason": _summary_reason_with_defaults(reason),
        "budgets": dict(budgets),
        "diagnostics": sorted(
            (dict(item) for item in diagnostics),
            key=lambda item: str(item.get("diagnostic-id")),
        ),
        "batch-bundles": sorted(
            (dict(item) for item in batch_bundles),
            key=_summary_batch_bundle_sort_key,
        ),
        "evidence-results": sorted(
            (dict(item) for item in evidence_results),
            key=lambda item: str(item.get("evidence-expectation-id")),
        ),
        "failures": sorted(
            (dict(item) for item in failures),
            key=_summary_failure_sort_key,
        ),
        "work-groups": dict(work_groups),
        "proof-admissibility": _PROOF_ADMISSIBILITY,
    }
    if invalid_plan_input_detail is not None:
        summary_reason = summary.get("reason")
        if isinstance(summary_reason, MutableMapping):
            cast("MutableMapping[str, object]", summary_reason)[
                "invalid-plan"
            ] = True
    aggregate_manifest_evidence_bound = raw_aggregate_manifest_evidence_bound
    bound_final_producer_unverified = False
    final_manifest_producer_unverified = False
    if _is_invalid_plan_summary(summary):
        preserve_invalid_plan_manifest_claim = aggregate_manifest_evidence_bound
        if retained_invalid_plan_context and plan is not None:
            retained_context_complete = (
                _invalid_plan_context_has_complete_retained_projection(plan)
                if plan is not None
                else False
            )
            invalid_plan_projection = (
                _summary_projection_from_plan(plan)
                if retained_context_complete
                else _invalid_plan_summary_projection_from_context(
                    plan,
                    aggregate_evidence_manifest_document,
                    request=request_document,
                    changed_files_snapshot=changed_files_snapshot,
                    fact_snapshot=fact_snapshot,
                )
            )
            summary["plan-id"] = plan.get("plan-id")
            summary["plan-digest"] = (
                plan.get("plan-digest")
                if retained_invalid_plan_context
                else _verified_plan_digest(plan)
            )
            summary["mode"] = invalid_plan_projection["mode"]
            summary["validation-tree"] = dict(
                _mapping(invalid_plan_projection["validation-tree"])
            )
            summary["affected-range"] = dict(
                _mapping(invalid_plan_projection["affected-range"])
            )
            summary["request"] = dict(
                _mapping(invalid_plan_projection["request"])
            )
            summary["scheduled-full"] = dict(
                _mapping(invalid_plan_projection["scheduled-full"])
            )
        preserve_invalid_plan_projection = (
            plan is not None
            and invalid_plan_input_detail is not None
            and _invalid_plan_detail_allows_retained_plan_context(
                invalid_plan_input_detail
            )
            and retained_invalid_plan_context
        )
        forced_invalid_plan_detail = (
            invalid_plan_input_detail
            if (
                preserve_invalid_plan_projection
                or _invalid_plan_detail_allows_no_authority_projection(
                    invalid_plan_input_detail
                )
            )
            else _invalid_plan_failure_detail_from_summary(summary)
            if _invalid_plan_detail_allows_no_authority_projection(
                _invalid_plan_failure_detail_from_summary(summary)
            )
            else DiagnosticDetail.PLAN_MISSING.value
            if invalid_plan_input_detail is None
            else DiagnosticDetail.MALFORMED_PLAN.value
        )
        final_manifest_producer_unverified = (
            isinstance(input_final_manifest, Mapping)
            and input_final_manifest.get("producer-verified") is False
        )
        forced_invalid_plan_uses_no_authority_projection = (
            not preserve_invalid_plan_projection
            and _invalid_plan_detail_allows_no_authority_projection(
                forced_invalid_plan_detail
            )
        )
        preserve_invalid_plan_manifest_claim = (
            aggregate_manifest_evidence_bound
            and (
                not forced_invalid_plan_uses_no_authority_projection
                or aggregate_evidence_manifest_external_binding_verified
            )
        )
        bound_final_producer_unverified = (
            final_manifest_producer_unverified
            and _summary_has_final_producer_unverified_failure(summary)
            and (
                (
                    raw_aggregate_manifest_evidence_bound
                    and not forced_invalid_plan_uses_no_authority_projection
                )
                or bool(raw_authority_failure_details)
            )
        )
        if (
            final_manifest_producer_unverified
            and preserve_invalid_plan_manifest_claim
            and not bound_final_producer_unverified
        ):
            raise ContractValidationError(
                [
                    ValidationIssue(
                        "$.final-artifacts.aggregate-evidence-manifest."
                        "producer-verified",
                        "producer-verified false requires bound "
                        "final-producer-unverified failure",
                    )
                ]
            )
        _force_invalid_plan_summary_fields(
            summary,
            preserve_manifest_claim=preserve_invalid_plan_manifest_claim,
            preserve_projection=preserve_invalid_plan_projection,
            invalid_plan_detail=forced_invalid_plan_detail,
            final_producer_unverified_bound=bound_final_producer_unverified,
            preserve_final_manifest_producer_unverified=(
                bound_final_producer_unverified
            ),
            preserve_authority_failure_details=(
                aggregate_manifest_evidence_bound
                or (
                    bool(raw_authority_failure_details)
                    and not invalid_plan_uses_no_authority_projection
                )
            ),
        )
    if not bound_final_producer_unverified:
        _strip_unbound_final_producer_unverified_if_needed(summary)
    final_manifest = _summary_final_aggregate_manifest(summary)
    if (
        aggregate_evidence_manifest_document is None
        and isinstance(final_manifest, MutableMapping)
        and final_manifest.get("artifact-instance-id") is None
        and final_manifest.get("content-digest") is None
    ):
        final_manifest["producer-verified"] = False
    missing_manifest_fail_closed = _summary_has_missing_manifest_failure(
        summary
    )
    manifest_authority_fail_closed = bool(
        _summary_aggregate_manifest_authority_failure_details(summary)
    )
    if (
        not _is_invalid_plan_summary(summary)
        and not missing_manifest_fail_closed
        and not manifest_authority_fail_closed
        and aggregate_evidence_manifest_document is None
        and _summary_freezer_requires_manifest_document(
            summary,
            plan,
            admitted_batch_evidence_bundles,
            execution_batch_manifest,
        )
    ):
        raise ContractValidationError(
            [
                ValidationIssue(
                    "aggregate_evidence_manifest_document",
                    "is required with authoritative aggregate summary inputs",
                )
            ]
        )
    validation_plan_context = (
        plan
        if (
            plan is not None
            and (
                bool(_plan_fail_closed_failure_causes(plan))
                or (
                    not aggregate_manifest_plan_unbound
                    and (
                        invalid_plan_input_detail is None
                        or retained_invalid_plan_context
                    )
                )
            )
        )
        else None
    )
    validate_ci_validation_aggregate_summary(
        summary,
        plan=validation_plan_context,
        aggregate_evidence_manifest=aggregate_evidence_manifest_document,
        admitted_batch_evidence_bundles=admitted_batch_evidence_bundles,
        execution_batch_manifest=execution_batch_manifest
        if validation_plan_context is not None
        else None,
        request=(
            request_document if validation_plan_context is not None else None
        ),
        changed_files_snapshot=(
            changed_files_snapshot
            if validation_plan_context is not None
            else None
        ),
        fact_snapshot=(
            fact_snapshot if validation_plan_context is not None else None
        ),
        _require_aggregate_evidence_manifest=missing_manifest_fail_closed,
        _aggregate_evidence_manifest_bound=raw_aggregate_manifest_evidence_bound,
        _aggregate_evidence_manifest_external_binding_verified=(
            raw_aggregate_manifest_evidence_bound
            and aggregate_evidence_manifest_external_binding_verified
        ),
        _aggregate_manifest_authority_failure_details=(
            raw_authority_failure_details
            if raw_authority_failure_details
            or aggregate_manifest_evidence_bound
            or not _is_invalid_plan_summary(summary)
            else set()
        ),
    )
    return summary


def _summary_reason_with_defaults(
    reason: Mapping[str, object],
) -> dict[str, object]:
    summary_reason: dict[str, object] = dict.fromkeys(
        _SUMMARY_REASON_KEYS,
        False,
    )
    summary_reason.update(reason)
    return summary_reason


def _invalid_plan_context_has_complete_retained_projection(
    plan: Mapping[str, object],
) -> bool:
    try:
        projection = _summary_projection_from_plan(plan)
    except ContractValidationError:
        return False
    return _invalid_plan_summary_has_complete_retained_projection(
        {
            "plan-id": plan.get("plan-id"),
            "plan-digest": plan.get("plan-digest"),
            **projection,
        }
    )


def validate_ci_validation_aggregate_summary(  # noqa: C901, PLR0912, PLR0913, PLR0915
    summary: object,
    *,
    plan: Mapping[str, object] | None = None,
    aggregate_evidence_manifest: Mapping[str, object] | None = None,
    admitted_batch_evidence_bundles: Sequence[Mapping[str, object]]
    | None = None,
    execution_batch_manifest: Mapping[str, object] | None = None,
    request: Mapping[str, object] | None = None,
    changed_files_snapshot: Mapping[str, object] | None = None,
    fact_snapshot: Mapping[str, object] | None = None,
    expected_run_id: str | None = None,
    expected_run_attempt: str | None = None,
    _require_aggregate_evidence_manifest: bool = True,
    _aggregate_evidence_manifest_bound: bool | None = None,
    _aggregate_evidence_manifest_external_binding_verified: bool = False,
    _aggregate_manifest_authority_failure_details: set[str] | None = None,
) -> None:
    """Validate a final aggregate summary."""
    if not isinstance(summary, Mapping):
        raise ContractValidationError(
            [ValidationIssue("$", "must be an object")],
        )
    issues: list[ValidationIssue] = []
    plan = _validate_optional_mapping_context(plan, "plan", issues)
    aggregate_evidence_manifest = _validate_optional_mapping_context(
        aggregate_evidence_manifest, "aggregate_evidence_manifest", issues
    )
    execution_batch_manifest = _validate_optional_mapping_context(
        execution_batch_manifest, "execution_batch_manifest", issues
    )
    request = _validate_optional_mapping_context(request, "request", issues)
    changed_files_snapshot = _validate_optional_mapping_context(
        changed_files_snapshot, "changed_files_snapshot", issues
    )
    fact_snapshot = _validate_optional_mapping_context(
        fact_snapshot, "fact_snapshot", issues
    )
    admitted_batch_evidence_bundles = (
        _validate_optional_mapping_sequence_context(
            admitted_batch_evidence_bundles,
            "admitted_batch_evidence_bundles",
            issues,
        )
    )
    _validate_canonical(summary, "$", issues, fail_closed=True)
    envelope = _envelope_or_collect(
        summary,
        CiValidationKind.AGGREGATE_SUMMARY,
        issues,
    )
    _validate_root_keys(summary, _AGGREGATE_SUMMARY_KEYS, "$", issues)
    _validate_g1_schema_diagnostics(summary.get("schema-diagnostics"), issues)
    _validate_expected_run(
        envelope, expected_run_id, expected_run_attempt, issues
    )
    _validate_summary_ref(summary, envelope, issues)
    invalid_plan_summary = _is_invalid_plan_summary(summary)
    invalid_plan_detail = (
        _invalid_plan_failure_detail_from_summary(summary)
        if invalid_plan_summary
        else None
    )
    aggregate_manifest_input_not_valid = (
        aggregate_evidence_manifest is not None
        and _aggregate_execution_batch_manifest_input_not_valid(
            aggregate_evidence_manifest
        )
    )
    summary_has_manifest_claim = _summary_manifest_claim_has_content_digest(
        summary
    )
    externally_bound_aggregate_manifest_evidence = (
        aggregate_evidence_manifest is not None
        and not (
            aggregate_manifest_input_not_valid
            and not summary_has_manifest_claim
        )
        and _aggregate_evidence_manifest_bound is not False
    )
    externally_verified_aggregate_manifest_evidence = (
        aggregate_evidence_manifest is not None
        and _aggregate_evidence_manifest_bound is True
        and _aggregate_evidence_manifest_external_binding_verified
    )
    externally_bound_authority_failure_details: set[str] = set()
    if aggregate_evidence_manifest is not None:
        externally_bound_authority_failure_details = (
            set(_aggregate_manifest_authority_failure_details)
            if _aggregate_manifest_authority_failure_details is not None
            else set()
        )
    if aggregate_evidence_manifest is not None:
        externally_bound_authority_failure_details &= (
            _bound_aggregate_manifest_authority_failure_details(
                summary,
                aggregate_evidence_manifest,
            )
        )
    independently_bound_aggregate_manifest_evidence = (
        externally_bound_aggregate_manifest_evidence
        or externally_verified_aggregate_manifest_evidence
    )
    externally_bound_authority_failure = bool(
        externally_bound_authority_failure_details
    )
    final_aggregate_manifest_authority_bound = (
        independently_bound_aggregate_manifest_evidence
    )
    bound_aggregate_manifest_authority_failure_details = (
        externally_bound_authority_failure_details
        if (
            final_aggregate_manifest_authority_bound
            or externally_bound_authority_failure
        )
        else set()
    )
    aggregate_manifest_evidence_bound = (
        independently_bound_aggregate_manifest_evidence
    )
    if (
        not aggregate_manifest_evidence_bound
        and not externally_bound_authority_failure
    ):
        bound_aggregate_manifest_authority_failure_details = set()
    summary_manifest_authority_context_allowed = (
        aggregate_manifest_evidence_bound or externally_bound_authority_failure
    )
    aggregate_manifest_authority_failure_details: set[str] = set()
    aggregate_manifest_authority_failure_details.update(
        bound_aggregate_manifest_authority_failure_details
    )
    aggregate_manifest_plan_unbound = (
        not final_aggregate_manifest_authority_bound
        or (
            aggregate_evidence_manifest is not None
            and _aggregate_manifest_has_no_authoritative_plan(
                aggregate_evidence_manifest
            )
        )
    )
    invalid_plan_no_authority_projection = (
        invalid_plan_summary
        and _invalid_plan_detail_allows_no_authority_projection(
            invalid_plan_detail,
        )
        and _summary_projection_matches(
            summary,
            _no_authority_summary_projection(),
        )
    )
    if (
        invalid_plan_no_authority_projection
        and not summary_has_manifest_claim
        and (
            _aggregate_evidence_manifest_bound is False
            or (
                aggregate_evidence_manifest is not None
                and _aggregate_manifest_has_true_no_authority_invalid_plan(
                    aggregate_evidence_manifest
                )
                and not _summary_aggregate_manifest_authority_failure_details(
                    summary
                )
                and not (
                    isinstance(
                        final_aggregate_manifest := (
                            _summary_final_aggregate_manifest(summary)
                        ),
                        Mapping,
                    )
                    and final_aggregate_manifest.get("authority-diagnostics")
                )
            )
        )
    ):
        aggregate_manifest_evidence_bound = False
    retained_invalid_plan_context_authorized = (
        invalid_plan_summary
        and _invalid_plan_detail_allows_retained_plan_context(
            invalid_plan_detail,
        )
        and final_aggregate_manifest_authority_bound
        and plan is not None
        and aggregate_evidence_manifest is not None
        and (
            _supplied_plan_input_authorizes_retained_invalid_plan_projection(
                aggregate_evidence_manifest,
                _aggregate_manifest_envelope_or_none(
                    aggregate_evidence_manifest
                ),
                plan,
                request=request,
                changed_files_snapshot=changed_files_snapshot,
                fact_snapshot=fact_snapshot,
            )
            or (
                aggregate_evidence_manifest.get("projection-authority")
                == _projection_authority_from_plan(plan)
                and isinstance(
                    aggregate_evidence_manifest.get("input-artifacts"),
                    Mapping,
                )
                and _input_artifacts_have_retained_projection_authority(
                    cast(
                        "Mapping[str, object]",
                        aggregate_evidence_manifest["input-artifacts"],
                    ),
                    plan=plan,
                )
            )
        )
    )
    if (
        invalid_plan_summary
        and _invalid_plan_detail_allows_retained_plan_context(
            invalid_plan_detail,
        )
        and aggregate_evidence_manifest is None
        and plan is not None
        and not _summary_projection_matches(
            summary,
            _no_authority_summary_projection(),
        )
    ):
        issues.append(
            ValidationIssue(
                "$.projection-authority",
                "retained invalid-plan details require aggregate "
                "manifest input authority",
            )
        )
    if (
        invalid_plan_summary
        and _invalid_plan_detail_allows_retained_plan_context(
            invalid_plan_detail,
        )
        and plan is None
        and aggregate_evidence_manifest is None
        and not _summary_projection_matches(
            summary,
            _no_authority_summary_projection(),
        )
    ):
        issues.append(
            ValidationIssue(
                "$.projection-authority",
                "retained invalid-plan details require aggregate "
                "manifest input authority",
            )
        )
    summary_plan_identity_unbound = (
        aggregate_manifest_plan_unbound
        or invalid_plan_no_authority_projection
        or (
            aggregate_evidence_manifest is not None
            and _aggregate_manifest_has_no_authoritative_plan(
                aggregate_evidence_manifest
            )
        )
        or (
            aggregate_evidence_manifest is not None
            and _summary_bound_aggregate_manifest_digest_mismatch(
                summary,
                aggregate_evidence_manifest,
            )
        )
        or (invalid_plan_summary and aggregate_evidence_manifest is None)
        or (
            aggregate_evidence_manifest is None
            and aggregate_manifest_evidence_bound
            and bool(bound_aggregate_manifest_authority_failure_details)
        )
    ) and not retained_invalid_plan_context_authorized
    identity_plan = plan
    if summary_plan_identity_unbound:
        identity_plan = None
    _validate_plan_nullable_fields(
        summary,
        identity_plan,
        envelope,
        issues,
        allow_retained_invalid_plan_digest=(
            retained_invalid_plan_context_authorized
        ),
    )
    if summary_plan_identity_unbound and not (
        invalid_plan_summary
        and _invalid_plan_detail_allows_no_authority_projection(
            invalid_plan_detail
        )
    ):
        _validate_null_plan_identity(summary, "$", issues)
    if not invalid_plan_summary:
        _validate_supplied_plan_document_for_aggregate(
            plan,
            envelope,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            issues=issues,
        )
    execution_batch_manifest_authoritative = False
    if (
        not retained_invalid_plan_context_authorized
        and not invalid_plan_no_authority_projection
    ):
        execution_batch_manifest_authoritative = (
            _validate_supplied_summary_execution_manifest(
                execution_batch_manifest,
                plan,
                envelope,
                request=request,
                changed_files_snapshot=changed_files_snapshot,
                fact_snapshot=fact_snapshot,
                issues=issues,
            )
        )
    authoritative_execution_batch_manifest = None
    if (
        execution_batch_manifest_authoritative
        and final_aggregate_manifest_authority_bound
        and not (invalid_plan_summary and aggregate_manifest_plan_unbound)
        and not (
            aggregate_evidence_manifest is not None
            and _aggregate_manifest_has_no_authoritative_plan(
                aggregate_evidence_manifest
            )
        )
    ):
        authoritative_execution_batch_manifest = execution_batch_manifest
    if (
        plan is None
        and aggregate_evidence_manifest is None
        and not _invalid_plan_detail_allows_retained_plan_context(
            invalid_plan_detail
        )
    ):
        _validate_null_plan_identity(summary, "$", issues)
    if (
        plan is None
        and aggregate_evidence_manifest is not None
        and _aggregate_manifest_has_no_authoritative_plan(
            aggregate_evidence_manifest
        )
    ):
        _validate_null_plan_identity(summary, "$", issues)
    if (
        invalid_plan_summary
        and plan is None
        and not _invalid_plan_detail_allows_retained_plan_context(
            invalid_plan_detail
        )
    ):
        _validate_null_plan_identity(summary, "$", issues)
    elif authoritative_execution_batch_manifest is not None:
        _validate_plan_identity_matches(
            summary,
            authoritative_execution_batch_manifest,
            "$",
            "execution-batch manifest",
            issues,
        )
    if summary.get("mode") not in _SUMMARY_MODES:
        issues.append(ValidationIssue("$.mode", "is not registered"))
    bound_no_authority_manifest = invalid_plan_no_authority_projection and (
        aggregate_manifest_evidence_bound
    )
    no_bound_aggregate_manifest_evidence = not aggregate_manifest_evidence_bound
    require_non_authoritative_manifest = (
        no_bound_aggregate_manifest_evidence
        and not (
            externally_bound_authority_failure and summary_has_manifest_claim
        )
        and (
            (
                aggregate_manifest_input_not_valid
                and _summary_has_required_input_failure(summary)
            )
            or (
                aggregate_manifest_input_not_valid
                and _summary_has_final_producer_unverified_failure(summary)
            )
            or (
                aggregate_manifest_input_not_valid
                and _summary_has_final_evidence_failure(summary)
            )
            or (
                aggregate_manifest_input_not_valid
                and _summary_has_missing_manifest_failure(summary)
            )
            or aggregate_evidence_manifest is None
            or (
                aggregate_evidence_manifest is not None
                and invalid_plan_summary
                and not aggregate_manifest_authority_failure_details
            )
            or (
                aggregate_evidence_manifest is None
                and not summary_manifest_authority_context_allowed
            )
            or (
                not aggregate_manifest_authority_failure_details
                and (
                    (
                        invalid_plan_no_authority_projection
                        and not bound_no_authority_manifest
                        and not summary_has_manifest_claim
                    )
                    or (
                        invalid_plan_summary
                        and aggregate_evidence_manifest is None
                        and not summary_has_manifest_claim
                    )
                    or (
                        aggregate_evidence_manifest is None
                        and (
                            _require_aggregate_evidence_manifest
                            and not (
                                invalid_plan_summary
                                and summary_has_manifest_claim
                            )
                        )
                    )
                    or (
                        aggregate_evidence_manifest is None
                        and _summary_has_missing_manifest_failure(summary)
                    )
                )
            )
        )
    )
    skip_manifest_digest_match = (
        (
            bool(aggregate_manifest_authority_failure_details)
            and (aggregate_evidence_manifest is None or invalid_plan_summary)
        )
        or (
            no_bound_aggregate_manifest_evidence
            and invalid_plan_summary
            and not aggregate_manifest_authority_failure_details
        )
        or (
            invalid_plan_no_authority_projection
            and not aggregate_manifest_authority_failure_details
            and not bound_no_authority_manifest
        )
        or (
            no_bound_aggregate_manifest_evidence
            and aggregate_manifest_input_not_valid
        )
    )
    manifest_claim = _validate_summary_manifest_claim(
        summary.get("aggregate-evidence-manifest"),
        envelope,
        aggregate_evidence_manifest,
        require_non_authoritative_manifest=require_non_authoritative_manifest,
        skip_manifest_digest_match=skip_manifest_digest_match,
        issues=issues,
    )
    final_manifest = _summary_final_aggregate_manifest(summary)
    final_manifest_producer_false = (
        isinstance(final_manifest, Mapping)
        and final_manifest.get("producer-verified") is False
    )
    aggregate_manifest_producer_unverified = (
        final_manifest_producer_false
        and _summary_has_final_producer_unverified_failure(summary)
    )
    bound_aggregate_manifest_producer_unverified = (
        final_aggregate_manifest_authority_bound
        and aggregate_manifest_producer_unverified
    )
    if (
        invalid_plan_summary
        and aggregate_manifest_producer_unverified
        and not bound_aggregate_manifest_producer_unverified
    ):
        issues.append(
            ValidationIssue(
                "$.failures",
                "final-producer-unverified requires a bound unverified final "
                "manifest producer",
            )
        )
    _validate_final_artifacts(
        summary.get("final-artifacts"),
        envelope,
        manifest_claim,
        aggregate_evidence_manifest,
        require_non_authoritative_manifest=require_non_authoritative_manifest,
        skip_manifest_digest_match=skip_manifest_digest_match,
        issues=issues,
    )
    _validate_summary_final_producer_failure_coverage(summary, issues)
    _validate_summary_manifest_authority_diagnostics_match_context(
        summary,
        aggregate_manifest_authority_failure_details,
        aggregate_evidence_manifest,
        issues,
    )
    _validate_validation_tree(
        summary.get("validation-tree"),
        "$.validation-tree",
        allow_unknown=True,
        issues=issues,
    )
    _validate_affected_range(summary.get("affected-range"), issues)
    _validate_request_summary(summary.get("request"), issues)
    _validate_summary_request_matches_aggregate_manifest(
        summary, aggregate_evidence_manifest, issues
    )
    _validate_scheduled_full(summary.get("scheduled-full"), issues)
    _validate_summary_projection_authority(
        summary,
        plan,
        aggregate_evidence_manifest,
        envelope,
        request,
        changed_files_snapshot,
        fact_snapshot,
        issues=issues,
    )
    if summary.get("verdict") not in {"passed", "failed"}:
        issues.append(ValidationIssue("$.verdict", "is not registered"))
    _validate_bool_object(
        summary.get("reason"), _SUMMARY_REASON_KEYS, "$.reason", issues
    )
    _validate_summary_budgets(summary.get("budgets"), issues)
    _validate_summary_budget_matches_execution_manifest(
        summary,
        authoritative_execution_batch_manifest,
        plan,
        issues,
    )
    _validate_diagnostics(summary.get("diagnostics"), "$.diagnostics", issues)
    _validate_summary_bundles(summary.get("batch-bundles"), envelope, issues)
    _validate_summary_bundle_ids_match_execution_manifest(
        summary.get("batch-bundles"),
        None
        if invalid_plan_summary
        else authoritative_execution_batch_manifest,
        issues,
    )
    _validate_no_summary_admitted_candidates_without_manifest(
        summary,
        aggregate_evidence_manifest,
        issues,
    )
    _validate_no_summary_satisfied_evidence_without_manifest(
        summary,
        aggregate_evidence_manifest,
        issues,
    )
    _validate_summary_evidence_results(summary.get("evidence-results"), issues)
    _validate_summary_failures(summary.get("failures"), issues)
    _validate_summary_invalid_plan_diagnostics_are_bound(summary, issues)
    _validate_invalid_plan_final_failure_root_diagnostics(summary, issues)
    _validate_invalid_plan_summary_failure_attribution(summary, issues)
    _validate_summary_evidence_matches_plan(summary, plan, issues)
    _validate_summary_work_groups(summary.get("work-groups"), issues)
    summary_evidence_rows = _summary_evidence_rows(
        summary.get("evidence-results")
    )
    namespace_failure_details = (
        _aggregate_namespace_failure_details(aggregate_evidence_manifest)
        if aggregate_evidence_manifest is not None
        else set()
    )
    required_input_failure = (
        _aggregate_required_input_failure(aggregate_evidence_manifest)
        if aggregate_evidence_manifest is not None
        else _summary_has_required_input_failure(summary)
    )
    invalid_plan_input_failure_details = _invalid_plan_input_failure_details(
        aggregate_evidence_manifest
    )
    invalid_plan_expected_projection = (
        _summary_projection_from_plan(plan)
        if retained_invalid_plan_context_authorized and plan is not None
        else _invalid_plan_summary_projection_from_context(
            plan,
            aggregate_evidence_manifest,
            request=request,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
        )
    )
    _validate_summary_derived_status(
        summary,
        summary_evidence_rows,
        inadmissible_batch=_summary_has_inadmissible_batch(summary),
        namespace_failure_details=namespace_failure_details,
        plan_fail_closed_failure_causes=_plan_fail_closed_failure_causes(plan),
        required_input_failure=required_input_failure,
        aggregate_duration_exceeded=_summary_duration_exceeded(summary),
        aggregate_manifest_producer_unverified=(
            aggregate_manifest_producer_unverified
        ),
        aggregate_manifest_authority_failure_details=(
            aggregate_manifest_authority_failure_details
        ),
        aggregate_summary_without_manifest=(
            not invalid_plan_summary and aggregate_evidence_manifest is None
        ),
        invalid_plan_input_failure_details=invalid_plan_input_failure_details,
        invalid_plan_expected_projection=invalid_plan_expected_projection,
        retained_invalid_plan_context_authorized=(
            retained_invalid_plan_context_authorized
        ),
        issues=issues,
    )
    _validate_summary_count_relationships(summary, issues)
    # Missing aggregate evidence manifests are represented as an explicit
    # final-evidence-failure cause in the summary itself.
    if aggregate_evidence_manifest is not None:
        execution_batch_manifest_input_not_valid = (
            _aggregate_execution_batch_manifest_input_not_valid(
                aggregate_evidence_manifest
            )
        )
        if (
            execution_batch_manifest is None
            and not execution_batch_manifest_input_not_valid
        ):
            issues.append(
                ValidationIssue(
                    "execution_batch_manifest",
                    "is required with aggregate evidence manifest",
                )
            )
        _validate_summary_matches_aggregate_manifest(
            summary,
            aggregate_evidence_manifest,
            plan,
            execution_batch_manifest,
            request,
            changed_files_snapshot,
            fact_snapshot,
            envelope,
            issues,
        )
    admitted_payloads_required = _summary_requires_admitted_bundle_payloads(
        summary, aggregate_evidence_manifest
    )
    if admitted_payloads_required and admitted_batch_evidence_bundles is None:
        issues.append(
            ValidationIssue(
                "admitted_batch_evidence_bundles",
                "is required for admitted or satisfied evidence",
            )
        )
    if admitted_batch_evidence_bundles is not None:
        if execution_batch_manifest is None:
            issues.append(
                ValidationIssue(
                    "execution_batch_manifest",
                    "is required with admitted batch evidence bundles",
                )
            )
        _validate_summary_matches_admitted_bundles(
            summary,
            aggregate_evidence_manifest,
            admitted_batch_evidence_bundles,
            plan,
            execution_batch_manifest,
            request,
            changed_files_snapshot,
            fact_snapshot,
            envelope,
            issues,
        )
    if summary.get("proof-admissibility") != _PROOF_ADMISSIBILITY:
        issues.append(
            ValidationIssue(
                "$.proof-admissibility",
                "must be validation-only",
            ),
        )
    if issues:
        raise ContractValidationError(issues)


def _validate_supplied_summary_execution_manifest(  # noqa: PLR0913
    execution_batch_manifest: Mapping[str, object] | None,
    plan: Mapping[str, object] | None,
    envelope: CommonEnvelope | None,
    *,
    request: Mapping[str, object] | None,
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> bool:
    if execution_batch_manifest is None:
        return False
    execution_manifest_issue_count = len(issues)
    try:
        _validate_ci_validation_execution_batch_manifest(
            execution_batch_manifest,
            plan=plan,
            request=request,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            expected_envelope=envelope,
            expected_run_id=envelope.run_id if envelope is not None else None,
            expected_run_attempt=(
                envelope.run_attempt if envelope is not None else None
            ),
            authorizing=(
                plan is not None
                and _authorizing_context_supplied(
                    request=request,
                    changed_files_snapshot=changed_files_snapshot,
                    fact_snapshot=fact_snapshot,
                )
            ),
            _allow_planless_non_authorizing_batches=(
                _allow_planless_execution_manifest_diagnostic(
                    plan,
                    execution_batch_manifest,
                )
            ),
        )
    except ContractValidationError as error:
        issues.extend(error.issues)
    return plan is not None and len(issues) == execution_manifest_issue_count


def _raw_digest(value: bytes, path: str) -> str:
    if not isinstance(value, bytes):
        raise ContractValidationError([ValidationIssue(path, "must be bytes")])
    return hashlib.sha256(value).hexdigest()


def _payload_digest(value: Mapping[str, object], path: str) -> str:
    try:
        return hashlib.sha256(canonical_json_bytes(value)).hexdigest()
    except (TypeError, ValueError) as error:
        raise ContractValidationError(
            [ValidationIssue(path, str(error))],
        ) from error


def _envelope(
    document: Mapping[str, object],
    kind: CiValidationKind,
) -> CommonEnvelope:
    return validate_common_envelope(
        document,
        api_version=API_VERSIONS_BY_KIND[kind.value],
        kind=kind,
    )


def _envelope_or_collect(
    document: Mapping[str, object],
    kind: CiValidationKind,
    issues: list[ValidationIssue],
) -> CommonEnvelope | None:
    try:
        return _envelope(document, kind)
    except ContractValidationError as error:
        issues.extend(error.issues)
        return None


def _validate_optional_mapping_context(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> Mapping[str, object] | None:
    if value is None:
        return None
    if not isinstance(value, Mapping):
        issues.append(ValidationIssue(path, "must be an object"))
        return None
    return value


def _validate_optional_mapping_sequence_context(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> Sequence[Mapping[str, object]] | None:
    if value is None:
        return None
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        issues.append(ValidationIssue(path, "must be a sequence of objects"))
        return None
    malformed = False
    items: list[Mapping[str, object]] = []
    for index, item in enumerate(value):
        if not isinstance(item, Mapping):
            issues.append(
                ValidationIssue(f"{path}[{index}]", "must be an object")
            )
            malformed = True
        else:
            items.append(item)
    if malformed:
        return None
    return items


def _validate_optional_str_mapping_context(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> Mapping[str, str] | None:
    if value is None:
        return None
    if not isinstance(value, Mapping):
        issues.append(ValidationIssue(path, "must be an object"))
        return None
    items: dict[str, str] = {}
    malformed = False
    for key, item in value.items():
        if not isinstance(key, str) or not isinstance(item, str):
            issues.append(ValidationIssue(path, "must map strings to strings"))
            malformed = True
            continue
        items[key] = item
    if malformed:
        return None
    return items


def _validated_plan_envelope(
    plan: Mapping[str, object],
    issues: list[ValidationIssue],
) -> CommonEnvelope | None:
    try:
        _verified_plan_digest(plan)
        return _envelope(plan, CiValidationKind.PLAN)
    except ContractValidationError as error:
        issues.extend(error.issues)
        return None


def _verified_plan_digest(plan: Mapping[str, object]) -> str:
    digest = plan.get("plan-digest")
    if not isinstance(digest, str) or _DIGEST_RE.fullmatch(digest) is None:
        raise ContractValidationError(
            [ValidationIssue("plan-digest", "must be a SHA-256 digest")],
        )
    try:
        recomputed = ci_validation_plan_digest(plan)
    except (TypeError, ValueError) as error:
        raise ContractValidationError(
            [ValidationIssue("plan-digest", str(error))],
        ) from error
    if digest != recomputed:
        raise ContractValidationError(
            [ValidationIssue("plan-digest", "must match canonical plan")],
        )
    return digest


def _verified_plan_digest_or_none(plan: Mapping[str, object]) -> str | None:
    try:
        return _verified_plan_digest(plan)
    except ContractValidationError:
        return None


def _validate_canonical(
    value: object,
    path: str,
    issues: list[ValidationIssue],
    *,
    fail_closed: bool = False,
) -> None:
    try:
        canonical_json_bytes(value)
    except (TypeError, ValueError) as error:
        issues.append(ValidationIssue(path, str(error)))
        if fail_closed:
            raise ContractValidationError(issues) from error


def _validate_root_keys(
    document: Mapping[str, object],
    allowed: frozenset[str],
    path: str,
    issues: list[ValidationIssue],
) -> None:
    keys: set[str] = set()
    for key in document:
        if not isinstance(key, str):
            issues.append(ValidationIssue(path, "keys must be strings"))
            continue
        keys.add(key)
    for key in sorted(keys - allowed):
        issues.append(ValidationIssue(f"{path}.{key}", "is not allowed"))
    for key in sorted(allowed - keys):
        issues.append(ValidationIssue(f"{path}.{key}", "is required"))


def _validate_root_keys_with_optional(
    document: Mapping[str, object],
    allowed: frozenset[str],
    optional: frozenset[str],
    path: str,
    issues: list[ValidationIssue],
) -> None:
    keys: set[str] = set()
    for key in document:
        if not isinstance(key, str):
            issues.append(ValidationIssue(path, "keys must be strings"))
            continue
        keys.add(key)
    for key in sorted(keys - allowed):
        issues.append(ValidationIssue(f"{path}.{key}", "is not allowed"))
    for key in sorted((allowed - optional) - keys):
        issues.append(ValidationIssue(f"{path}.{key}", "is required"))


def _validate_allowed_keys(
    document: Mapping[str, object],
    allowed: frozenset[str],
    path: str,
    issues: list[ValidationIssue],
) -> None:
    for key in document:
        if not isinstance(key, str):
            issues.append(ValidationIssue(path, "keys must be strings"))
        elif key not in allowed:
            issues.append(ValidationIssue(f"{path}.{key}", "is not allowed"))


def _validate_object(
    value: object,
    allowed: frozenset[str],
    path: str,
    issues: list[ValidationIssue],
) -> Mapping[str, object] | None:
    if not isinstance(value, Mapping):
        issues.append(ValidationIssue(path, "must be an object"))
        return None
    _validate_allowed_keys(value, allowed, path, issues)
    return value


def _validate_envelope_matches(
    left: CommonEnvelope,
    right: CommonEnvelope,
    issues: list[ValidationIssue],
) -> None:
    if left.repository_owner != right.repository_owner:
        issues.append(ValidationIssue("$.repository.owner", "must match plan"))
    if left.repository_name != right.repository_name:
        issues.append(ValidationIssue("$.repository.name", "must match plan"))
    if left.workflow != right.workflow:
        issues.append(ValidationIssue("$.run.workflow", "must match plan"))
    if left.run_id != right.run_id:
        issues.append(ValidationIssue("$.run.run-id", "must match plan"))
    if left.run_attempt != right.run_attempt:
        issues.append(ValidationIssue("$.run.run-attempt", "must match plan"))


def _validate_context_envelope_matches_current(
    context_envelope: CommonEnvelope,
    current_envelope: CommonEnvelope,
    path: str,
    issues: list[ValidationIssue],
) -> bool:
    matched = True
    if context_envelope.repository_owner != current_envelope.repository_owner:
        issues.append(
            ValidationIssue(
                f"{path}.repository.owner",
                "must match current repository",
            )
        )
        matched = False
    if context_envelope.repository_name != current_envelope.repository_name:
        issues.append(
            ValidationIssue(
                f"{path}.repository.name",
                "must match current repository",
            )
        )
        matched = False
    if context_envelope.workflow != current_envelope.workflow:
        issues.append(
            ValidationIssue(
                f"{path}.run.workflow",
                "must match current workflow",
            )
        )
        matched = False
    if context_envelope.run_id != current_envelope.run_id:
        issues.append(
            ValidationIssue(f"{path}.run.run-id", "must match current run")
        )
        matched = False
    if context_envelope.run_attempt != current_envelope.run_attempt:
        issues.append(
            ValidationIssue(
                f"{path}.run.run-attempt",
                "must match current run attempt",
            )
        )
        matched = False
    return matched


def _validate_expected_run(
    envelope: CommonEnvelope | None,
    expected_run_id: str | None,
    expected_run_attempt: str | None,
    issues: list[ValidationIssue],
) -> None:
    if envelope is None:
        return
    if expected_run_id is not None and envelope.run_id != expected_run_id:
        issues.append(
            ValidationIssue("$.run.run-id", "must match expected run")
        )
    if (
        expected_run_attempt is not None
        and envelope.run_attempt != expected_run_attempt
    ):
        issues.append(
            ValidationIssue(
                "$.run.run-attempt",
                "must match expected run attempt",
            ),
        )


def _validate_non_empty_string(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(value, str) or value == "":
        issues.append(ValidationIssue(path, "must be a string"))


def _validate_nullable_non_empty_string(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if value is not None and (not isinstance(value, str) or value == ""):
        issues.append(ValidationIssue(path, "must be null or non-empty string"))


def _validate_digest(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(value, str) or _DIGEST_RE.fullmatch(value) is None:
        issues.append(ValidationIssue(path, "must be a SHA-256 digest"))


def _validate_nullable_digest(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if value is not None:
        _validate_digest(value, path, issues)


def _validate_non_negative_int(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(value, int) or isinstance(value, bool) or value < 0:
        issues.append(ValidationIssue(path, "must be a non-negative integer"))


def _validate_bool_object(
    value: object,
    keys: frozenset[str],
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(value, Mapping):
        issues.append(ValidationIssue(path, "must be an object"))
        return
    _validate_root_keys(value, keys, path, issues)
    for key in keys:
        if not isinstance(value.get(key), bool):
            issues.append(ValidationIssue(f"{path}.{key}", "must be boolean"))


def _validate_local_id(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(value, str) or _LOCAL_ID_RE.fullmatch(value) is None:
        issues.append(ValidationIssue(path, "must be path-safe"))


def _validate_artifact_ref(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(value, str):
        issues.append(ValidationIssue(path, "must be a string"))
        return
    try:
        validate_artifact_logical_ref(value)
    except ContractValidationError as error:
        issues.extend(
            ValidationIssue(path, issue.message) for issue in error.issues
        )


def _validate_nullable_artifact_ref(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if value is not None:
        _validate_artifact_ref(value, path, issues)


def _validate_diagnostics(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        issues.append(ValidationIssue(path, "must be an array"))
        return
    previous: str | None = None
    for index, item in enumerate(value):
        item_path = f"{path}[{index}]"
        if not isinstance(item, Mapping):
            issues.append(ValidationIssue(item_path, "must be an object"))
            continue
        _validate_g1_diagnostic_record(item, item_path, issues)
        current = item.get("diagnostic-id")
        if isinstance(current, str):
            if previous is not None and previous > current:
                issues.append(ValidationIssue(path, "must be sorted"))
            previous = current


def _validate_g1_diagnostic_record(
    item: Mapping[str, object],
    path: str,
    issues: list[ValidationIssue],
) -> None:
    try:
        validate_ci_validation_diagnostic_record(item, path)
    except ContractValidationError as error:
        issues.extend(error.issues)
    code = item.get("code")
    if not isinstance(code, str):
        return
    allowed_details = CI_VALIDATION_G1_DETAILS_BY_DIAGNOSTIC_CODE.get(code)
    if allowed_details is None:
        return
    detail = item.get("detail")
    if detail is None:
        return
    if detail not in allowed_details:
        issues.append(
            ValidationIssue(
                f"{path}.detail",
                "is not a G1 detail for this diagnostic code",
            )
        )


def _validate_summary_invalid_plan_diagnostics_are_bound(
    summary: Mapping[str, object],
    issues: list[ValidationIssue],
) -> None:
    invalid_plan_summary = _is_invalid_plan_summary(summary)
    invalid_plan_detail = (
        _invalid_plan_failure_detail_from_summary(summary)
        if invalid_plan_summary
        else None
    )
    for path, diagnostic in _summary_diagnostic_records(summary):
        if diagnostic.get("code") != DiagnosticFamily.INVALID_PLAN.value:
            continue
        if invalid_plan_summary:
            bound = _invalid_plan_diagnostic_is_bound(
                diagnostic,
                invalid_plan_detail=invalid_plan_detail,
            )
        else:
            bound = _non_invalid_summary_invalid_plan_diagnostic_is_bound(
                diagnostic
            )
        if not bound:
            issues.append(
                ValidationIssue(
                    path,
                    "must be a canonical bound invalid-plan diagnostic",
                )
            )


def _non_invalid_summary_invalid_plan_diagnostic_is_bound(
    diagnostic: Mapping[str, object],
) -> bool:
    return _canonical_invalid_plan_diagnostic_matches(
        diagnostic
    ) and _neutral_aggregation_diagnostic_matches(diagnostic)


def _validate_invalid_plan_final_failure_root_diagnostics(
    summary: Mapping[str, object],
    issues: list[ValidationIssue],
) -> None:
    if not _is_invalid_plan_summary(summary):
        return
    root_diagnostics = summary.get("diagnostics")
    failures = summary.get("failures")
    if not isinstance(root_diagnostics, Sequence) or isinstance(
        root_diagnostics,
        str | bytes,
    ):
        return
    if not isinstance(failures, Sequence) or isinstance(failures, str | bytes):
        return
    root_diagnostic_bytes = {
        canonical_json_bytes(diagnostic)
        for diagnostic in root_diagnostics
        if isinstance(diagnostic, Mapping)
    }
    for index, failure in enumerate(failures):
        if not isinstance(failure, Mapping) or failure.get("kind") not in {
            "final-evidence-failure",
            "final-producer-unverified",
        }:
            continue
        diagnostic = failure.get("diagnostic")
        if not isinstance(diagnostic, Mapping):
            continue
        if canonical_json_bytes(diagnostic) not in root_diagnostic_bytes:
            issues.append(
                ValidationIssue(
                    f"$.failures[{index}].diagnostic",
                    "must be covered by root diagnostics",
                )
            )


def _summary_diagnostic_records(
    summary: Mapping[str, object],
) -> Iterator[tuple[str, Mapping[str, object]]]:
    for key in ("schema-diagnostics", "diagnostics"):
        yield from _diagnostic_records_from_array(summary.get(key), f"$.{key}")
    yield from _summary_final_artifact_diagnostic_records(summary)
    yield from _summary_row_diagnostic_records(summary)
    failures = summary.get("failures")
    if not isinstance(failures, Sequence) or isinstance(failures, str | bytes):
        return
    for failure_index, failure in enumerate(failures):
        if not isinstance(failure, Mapping):
            continue
        diagnostic = failure.get("diagnostic")
        if isinstance(diagnostic, Mapping):
            yield f"$.failures[{failure_index}].diagnostic", diagnostic


def _summary_final_artifact_diagnostic_records(
    summary: Mapping[str, object],
) -> Iterator[tuple[str, Mapping[str, object]]]:
    final_artifacts = summary.get("final-artifacts")
    if isinstance(final_artifacts, Mapping):
        final_manifest = final_artifacts.get("aggregate-evidence-manifest")
        if isinstance(final_manifest, Mapping):
            yield from _diagnostic_records_from_array(
                final_manifest.get("authority-diagnostics"),
                "$.final-artifacts.aggregate-evidence-manifest."
                "authority-diagnostics",
            )


def _summary_row_diagnostic_records(
    summary: Mapping[str, object],
) -> Iterator[tuple[str, Mapping[str, object]]]:
    for collection_key in ("batch-bundles", "evidence-results"):
        rows = summary.get(collection_key)
        if not isinstance(rows, Sequence) or isinstance(rows, str | bytes):
            continue
        for row_index, row in enumerate(rows):
            if not isinstance(row, Mapping):
                continue
            yield from _diagnostic_records_from_array(
                row.get("diagnostics"),
                f"$.{collection_key}[{row_index}].diagnostics",
            )


def _diagnostic_records_from_array(
    value: object,
    path: str,
) -> Iterator[tuple[str, Mapping[str, object]]]:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        return
    for index, item in enumerate(value):
        if isinstance(item, Mapping):
            yield f"{path}[{index}]", item


def _invalid_plan_diagnostic_is_bound(
    diagnostic: Mapping[str, object],
    *,
    invalid_plan_detail: str | None,
) -> bool:
    if diagnostic.get("detail") != invalid_plan_detail:
        return False
    if _canonical_fail_closed_invalid_plan_diagnostic_matches(diagnostic):
        return _fail_closed_aggregation_diagnostic_matches(diagnostic)
    if _canonical_invalid_plan_diagnostic_matches(diagnostic):
        return _neutral_aggregation_diagnostic_matches(
            diagnostic,
        ) or _fail_closed_aggregation_diagnostic_matches(diagnostic)
    return False


def _canonical_invalid_plan_diagnostic_matches(
    diagnostic: Mapping[str, object],
) -> bool:
    if diagnostic.get("code") != DiagnosticFamily.INVALID_PLAN.value:
        return False
    detail = diagnostic.get("detail")
    if detail == DiagnosticDetail.PLAN_MISSING.value:
        return diagnostic.get("diagnostic-id") == "invalid-plan"
    if not isinstance(detail, str):
        return False
    return (
        detail
        in {
            *_invalid_plan_retained_projection_details(),
            *_INVALID_PLAN_NO_AUTHORITY_PROJECTION_DETAILS,
            *_INVALID_PLAN_MALFORMED_SNAPSHOT_DETAILS,
        }
        and diagnostic.get("diagnostic-id") == f"invalid-plan/{detail}"
    )


def _canonical_fail_closed_invalid_plan_diagnostic_matches(
    diagnostic: Mapping[str, object],
) -> bool:
    detail = diagnostic.get("detail")
    return (
        diagnostic.get("code") == DiagnosticFamily.INVALID_PLAN.value
        and detail
        in {
            DiagnosticDetail.PLAN_MISSING.value,
            *_invalid_plan_retained_projection_details(),
            *_INVALID_PLAN_NO_AUTHORITY_PROJECTION_DETAILS,
        }
        and diagnostic.get("diagnostic-id")
        == f"fail-closed/invalid-plan/{detail}"
        and _nullable_string_member_is_present(diagnostic, "message")
    )


def _neutral_aggregation_diagnostic_matches(
    diagnostic: Mapping[str, object],
) -> bool:
    source = diagnostic.get("source")
    return (
        diagnostic.get("severity")
        in {DiagnosticSeverity.INFO.value, DiagnosticSeverity.WARNING.value}
        and diagnostic.get("verdict-effect")
        == DiagnosticVerdictEffect.NONE.value
        and isinstance(source, Mapping)
        and source.get("type") == "aggregation"
        and source.get("id") is None
    )


def _fail_closed_aggregation_diagnostic_matches(
    diagnostic: Mapping[str, object],
) -> bool:
    source = diagnostic.get("source")
    return (
        diagnostic.get("severity") == DiagnosticSeverity.FAIL_CLOSED.value
        and diagnostic.get("verdict-effect")
        == DiagnosticVerdictEffect.FAIL_CLOSED.value
        and isinstance(source, Mapping)
        and source.get("type") == "aggregation"
        and source.get("id") is None
    )


def _invalid_plan_retained_projection_details() -> set[str]:
    return set(CI_VALIDATION_INVALID_PLAN_RETAINED_PROJECTION_DETAILS)


def _validate_g1_schema_diagnostics(
    value: object,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        return
    for index, item in enumerate(value):
        if not isinstance(item, Mapping):
            continue
        code = item.get("code")
        if not isinstance(code, str):
            continue
        allowed_details = CI_VALIDATION_G1_DETAILS_BY_DIAGNOSTIC_CODE.get(code)
        if allowed_details is None:
            continue
        detail = item.get("detail")
        if detail is None:
            continue
        if detail not in allowed_details:
            issues.append(
                ValidationIssue(
                    f"$.schema-diagnostics[{index}].detail",
                    "is not a G1 detail for this diagnostic code",
                )
            )


def _materializer_executable_work_groups(
    plan: Mapping[str, object],
) -> dict[str, Mapping[str, object]]:
    groups = _sequence(plan["work-groups"])
    result: dict[str, Mapping[str, object]] = {}
    for group in groups:
        if (
            isinstance(group, Mapping)
            and group.get("kind") != "evidence-aggregation"
            and isinstance(group.get("work-group-id"), str)
        ):
            result[str(group["work-group-id"])] = group
    return result


def _materializer_evidence_by_work_group(
    plan: Mapping[str, object],
    groups: Mapping[str, Mapping[str, object]],
) -> dict[str, Mapping[str, object]]:
    expectations = _sequence(plan["evidence-expectations"])
    result: dict[str, Mapping[str, object]] = {}
    issues: list[ValidationIssue] = []
    for expectation in expectations:
        if not isinstance(expectation, Mapping):
            continue
        work_group_id = expectation.get("work-group-id")
        if not isinstance(work_group_id, str) or work_group_id not in groups:
            continue
        if work_group_id in result:
            issues.append(
                ValidationIssue(
                    "evidence-expectations",
                    "must contain exactly one expectation per work group",
                )
            )
        result[work_group_id] = expectation
    missing = sorted(set(groups) - set(result))
    for work_group_id in missing:
        issues.append(
            ValidationIssue(
                f"evidence-expectations.{work_group_id}",
                "is required for executable work group",
            )
        )
    if issues:
        raise ContractValidationError(issues)
    return result


def _materializer_topological_work_group_ids(
    groups: Mapping[str, Mapping[str, object]],
) -> list[str]:
    dependencies: dict[str, set[str]] = {}
    dependents: dict[str, set[str]] = {
        work_group_id: set() for work_group_id in groups
    }
    for work_group_id, group in groups.items():
        local_deps = {
            str(item)
            for item in _sequence(group.get("depends-on", []))
            if isinstance(item, str) and item in groups
        }
        dependencies[work_group_id] = local_deps
        for dependency in local_deps:
            dependents[dependency].add(work_group_id)
    ready = sorted(
        work_group_id
        for work_group_id, local_deps in dependencies.items()
        if not local_deps
    )
    ordered: list[str] = []
    while ready:
        work_group_id = ready.pop(0)
        ordered.append(work_group_id)
        for dependent in sorted(dependents[work_group_id]):
            dependencies[dependent].remove(work_group_id)
            if not dependencies[dependent]:
                ready.append(dependent)
        ready.sort()
    if len(ordered) != len(groups):
        raise ContractValidationError(
            [ValidationIssue("work-groups.depends-on", "must be acyclic")]
        )
    return ordered


def _materializer_batch_specs(
    plan: Mapping[str, object],
    groups: Mapping[str, Mapping[str, object]],
    ordered_group_ids: Sequence[str],
) -> list[dict[str, object]]:
    artifact_obligations = (
        _materializer_release_artifact_obligations_by_work_group(
            plan,
            groups,
        )
    )
    specs: list[dict[str, object]] = []
    for work_group_id in ordered_group_ids:
        group = groups[work_group_id]
        key_payload = _materializer_compatibility_key(
            group,
            artifact_obligations=artifact_obligations,
        )
        key = canonical_json_digest(key_payload)
        spec = next(
            (
                item
                for item in specs
                if item["key"] == key
                and not _materializer_batch_specs_would_cycle(
                    specs,
                    groups,
                    candidate_spec=item,
                    candidate_work_group_id=work_group_id,
                )
            ),
            None,
        )
        if spec is None:
            work_group_ids: list[str] = []
            spec = cast(
                "dict[str, object]",
                {
                    "key": key,
                    "key-payload": key_payload,
                    "work-group-ids": work_group_ids,
                },
            )
            specs.append(spec)
        cast("list[str]", spec["work-group-ids"]).append(work_group_id)
    for spec in specs:
        profile = _materializer_compatibility_profile(
            groups=groups,
            work_group_ids=cast("Sequence[str]", spec["work-group-ids"]),
            key_payload=cast("Mapping[str, object]", spec["key-payload"]),
        )
        spec["compatibility-profile"] = profile
        spec["batch-id"] = _materializer_batch_id(
            profile=profile,
            work_group_ids=cast("Sequence[str]", spec["work-group-ids"]),
        )
    return sorted(specs, key=lambda item: str(item["batch-id"]))


def _materializer_batch_specs_would_cycle(
    specs: Sequence[Mapping[str, object]],
    groups: Mapping[str, Mapping[str, object]],
    *,
    candidate_spec: Mapping[str, object],
    candidate_work_group_id: str,
) -> bool:
    batch_by_work_group = _materializer_candidate_batch_assignments(
        specs,
        groups,
        candidate_spec=candidate_spec,
        candidate_work_group_id=candidate_work_group_id,
    )
    return _directed_graph_has_cycle(
        _materializer_batch_dependency_graph(groups, batch_by_work_group)
    )


def _materializer_candidate_batch_assignments(
    specs: Sequence[Mapping[str, object]],
    groups: Mapping[str, Mapping[str, object]],
    *,
    candidate_spec: Mapping[str, object],
    candidate_work_group_id: str,
) -> dict[str, str]:
    batch_by_work_group: dict[str, str] = {}
    for index, spec in enumerate(specs):
        batch_key = f"batch:{index}"
        work_group_ids = list(cast("Sequence[str]", spec["work-group-ids"]))
        if spec is candidate_spec:
            work_group_ids.append(candidate_work_group_id)
        for work_group_id in work_group_ids:
            batch_by_work_group[work_group_id] = batch_key
    for work_group_id in groups:
        batch_by_work_group.setdefault(
            work_group_id, f"work-group:{work_group_id}"
        )
    return batch_by_work_group


def _materializer_batch_dependency_graph(
    groups: Mapping[str, Mapping[str, object]],
    batch_by_work_group: Mapping[str, str],
) -> dict[str, set[str]]:
    dependencies_by_batch: dict[str, set[str]] = {
        batch_id: set() for batch_id in batch_by_work_group.values()
    }
    for work_group_id, group in groups.items():
        consumer_batch = batch_by_work_group[work_group_id]
        for dependency in _sequence(group.get("depends-on", [])):
            if not isinstance(dependency, str) or dependency not in groups:
                continue
            producer_batch = batch_by_work_group[dependency]
            if producer_batch != consumer_batch:
                dependencies_by_batch[consumer_batch].add(producer_batch)
    return dependencies_by_batch


def _directed_graph_has_cycle(
    dependencies_by_node: Mapping[str, set[str]],
) -> bool:
    visiting: set[str] = set()
    visited: set[str] = set()

    def visit(node_id: str) -> bool:
        if node_id in visited:
            return False
        if node_id in visiting:
            return True
        visiting.add(node_id)
        has_cycle = any(
            visit(dependency)
            for dependency in dependencies_by_node.get(node_id, set())
        )
        visiting.remove(node_id)
        visited.add(node_id)
        return has_cycle

    return any(visit(node_id) for node_id in dependencies_by_node)


def _materializer_compatibility_key(
    group: Mapping[str, object],
    *,
    artifact_obligations: Mapping[str, Mapping[str, object]],
) -> dict[str, object]:
    kind = _materializer_compatibility_kind(group)
    key: dict[str, object] = {
        "api-version": "three.ci.validation.batch-compatibility/v1alpha1",
        "runner-family": group.get("runner-family"),
        "ecosystem": group.get("ecosystem"),
        "kind": kind,
        "release-shaped": group.get("kind") == "release-shaped-artifact",
    }
    if group.get("kind") == "release-shaped-artifact":
        key["release-shaped-executor-profile"] = (
            _materializer_release_executor_profile(
                group,
                artifact_obligations=artifact_obligations,
            )
        )
    return key


def _materializer_compatibility_kind(group: Mapping[str, object]) -> object:
    kind = group.get("kind")
    if (
        kind in _REPOSITORY_VALIDATION_BATCH_KINDS
        and group.get("runner-family") == "ubuntu"
        and group.get("ecosystem") is None
    ):
        return "repository-validation"
    return kind


def _materializer_artifact_obligations_by_work_group(
    plan: Mapping[str, object],
) -> dict[str, Mapping[str, object]]:
    obligations: dict[str, Mapping[str, object]] = {}
    for obligation in _sequence(plan.get("artifact-obligations", [])):
        if not isinstance(obligation, Mapping):
            continue
        work_group_id = obligation.get("work-group-id")
        if isinstance(work_group_id, str):
            obligations[work_group_id] = obligation
    return obligations


def _materializer_release_artifact_obligations_by_work_group(
    plan: Mapping[str, object],
    groups: Mapping[str, Mapping[str, object]],
) -> dict[str, Mapping[str, object]]:
    obligations_by_work_group: dict[str, list[Mapping[str, object]]] = {}
    issues: list[ValidationIssue] = []
    for index, obligation in enumerate(
        _sequence(plan.get("artifact-obligations", []))
    ):
        if not isinstance(obligation, Mapping):
            continue
        work_group_id = obligation.get("work-group-id")
        if not isinstance(work_group_id, str):
            continue
        obligations_by_work_group.setdefault(work_group_id, []).append(
            obligation
        )
        if len(obligations_by_work_group[work_group_id]) > 1:
            issues.append(
                ValidationIssue(
                    f"$.artifact-obligations[{index}].work-group-id",
                    "must bind a unique artifact obligation",
                )
            )
    for work_group_id, group in sorted(groups.items()):
        if group.get("kind") != "release-shaped-artifact":
            continue
        obligations = obligations_by_work_group.get(work_group_id, [])
        path = f"$.work-groups.{work_group_id}"
        if len(obligations) != 1:
            issues.append(
                ValidationIssue(
                    path,
                    "release-shaped groups require one artifact obligation",
                )
            )
            continue
        obligation = obligations[0]
        _validate_non_empty_mapping(
            obligation.get("artifact"),
            f"{path}.artifact",
            issues,
        )
        _validate_non_empty_mapping(
            obligation.get("release-receipt"),
            f"{path}.release-receipt",
            issues,
        )
    if issues:
        raise ContractValidationError(issues)
    return {
        work_group_id: obligations[0]
        for work_group_id, obligations in obligations_by_work_group.items()
    }


def _validate_non_empty_mapping(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(value, Mapping) or not value:
        issues.append(ValidationIssue(path, "must be a non-empty object"))


def _materializer_release_executor_profile(
    group: Mapping[str, object],
    *,
    artifact_obligations: Mapping[str, Mapping[str, object]],
) -> dict[str, object]:
    obligation = artifact_obligations.get(str(group.get("work-group-id")))
    return {
        "api-version": (
            "three.ci.validation.release-executor-profile/v1alpha1"
        ),
        "credential-posture": obligation.get("credential-posture")
        if obligation
        else None,
        "no-publish": True,
    }


def _materializer_compatibility_profile(
    *,
    groups: Mapping[str, Mapping[str, object]],
    work_group_ids: Sequence[str],
    key_payload: Mapping[str, object],
) -> dict[str, object]:
    runner_family = str(key_payload["runner-family"])
    ecosystem = key_payload.get("ecosystem")
    kind = str(key_payload["kind"])
    ecosystem_part = str(ecosystem) if ecosystem is not None else "generic"
    setup_profile = _profile_id("setup", runner_family, ecosystem_part)
    execution_profile = _profile_id("exec", kind, ecosystem_part)
    setup_preimage = {
        "api-version": "three.ci.validation.setup-profile/v1alpha1",
        "runner-family": runner_family,
        "ecosystem": ecosystem,
        "tool-provisioning": "mise",
    }
    execution_preimage = {
        "api-version": "three.ci.validation.execution-profile/v1alpha1",
        "kind": kind,
        "runner-family": runner_family,
        "ecosystem": ecosystem,
    }
    release_profile: str | None = None
    release_digest: str | None = None
    if kind == "release-shaped-artifact":
        release_profile = _profile_id("release", runner_family, ecosystem_part)
        release_preimage = {
            "api-version": (
                "three.ci.validation.release-shaped-profile/v1alpha1"
            ),
            "runner-family": runner_family,
            "ecosystem": ecosystem,
            "executor-profile": key_payload.get(
                "release-shaped-executor-profile"
            ),
            "work-groups": [
                {
                    "work-group-id": work_group_id,
                    "coverage-target": dict(
                        _mapping(groups[work_group_id]["coverage-target"])
                    ),
                }
                for work_group_id in work_group_ids
            ],
            "no-publish": True,
        }
        release_digest = canonical_json_digest(release_preimage)
    return {
        "ecosystem": ecosystem,
        "setup-profile": setup_profile,
        "setup-profile-digest": canonical_json_digest(setup_preimage),
        "execution-profile": execution_profile,
        "execution-profile-digest": canonical_json_digest(execution_preimage),
        "release-shaped-profile": release_profile,
        "release-shaped-profile-digest": release_digest,
    }


def _profile_id(prefix: str, *parts: str) -> str:
    raw = "-".join((prefix, *parts))
    safe = re.sub(r"[^a-z0-9._-]+", "-", raw.lower()).strip(".-_")
    if not safe or _LOCAL_ID_RE.fullmatch(safe) is None:
        safe = f"{prefix}-{canonical_json_digest({'parts': parts})[:16]}"
    return safe[:128]


def _materializer_batch_id(
    *,
    profile: Mapping[str, object],
    work_group_ids: Sequence[str],
) -> str:
    digest = canonical_json_digest(
        {
            "api-version": "three.ci.validation.batch-id/v1alpha1",
            "compatibility-profile": dict(profile),
            "work-group-ids": list(work_group_ids),
        }
    )
    ecosystem = profile.get("ecosystem")
    ecosystem_part = str(ecosystem) if ecosystem is not None else "generic"
    prefix = _profile_id(
        "batch",
        str(profile["execution-profile"]),
        ecosystem_part,
    )
    return f"{prefix[:56]}-{digest}"


def _materializer_batches(  # noqa: PLR0913
    *,
    envelope: CommonEnvelope,
    workflow: str,
    execution_job: str,
    batch_specs: Sequence[Mapping[str, object]],
    groups: Mapping[str, Mapping[str, object]],
    expectations: Mapping[str, Mapping[str, object]],
) -> list[dict[str, object]]:
    batch_id_by_work_group = {
        work_group_id: str(spec["batch-id"])
        for spec in batch_specs
        for work_group_id in cast("Sequence[str]", spec["work-group-ids"])
    }
    return [
        _materializer_batch(
            envelope=envelope,
            workflow=workflow,
            execution_job=execution_job,
            spec=spec,
            groups=groups,
            expectations=expectations,
            batch_id_by_work_group=batch_id_by_work_group,
        )
        for spec in batch_specs
    ]


def _materializer_batch(  # noqa: PLR0913
    *,
    envelope: CommonEnvelope,
    workflow: str,
    execution_job: str,
    spec: Mapping[str, object],
    groups: Mapping[str, Mapping[str, object]],
    expectations: Mapping[str, Mapping[str, object]],
    batch_id_by_work_group: Mapping[str, str],
) -> dict[str, object]:
    batch_id = str(spec["batch-id"])
    work_group_ids = cast("Sequence[str]", spec["work-group-ids"])
    bundle_ref = ci_validation_batch_evidence_bundle_artifact_ref(
        run_id=envelope.run_id,
        run_attempt=envelope.run_attempt,
        batch_id=batch_id,
    )
    matrix_identity = {
        "batch-id": batch_id,
        "runner-family": groups[work_group_ids[0]]["runner-family"],
        "expected-batch-evidence-bundle-ref": bundle_ref,
    }
    dependencies = sorted(
        {
            batch_id_by_work_group[dependency]
            for work_group_id in work_group_ids
            for dependency in _sequence(
                groups[work_group_id].get("depends-on", [])
            )
            if isinstance(dependency, str)
            and dependency in batch_id_by_work_group
            and batch_id_by_work_group[dependency] != batch_id
        }
    )
    return {
        "batch-id": batch_id,
        "runner-family": matrix_identity["runner-family"],
        "compatibility-profile": dict(
            cast("Mapping[str, object]", spec["compatibility-profile"])
        ),
        "depends-on-batches": dependencies,
        "ordered-selectors": [
            _materializer_selector(
                selector_index=index,
                group=groups[work_group_id],
                expectation=expectations[work_group_id],
            )
            for index, work_group_id in enumerate(work_group_ids)
        ],
        "expected-batch-evidence-bundle-ref": bundle_ref,
        "batch-writer": {
            "identity-source": "github-actions-job-context",
            "expected-boundary": "execution-batch",
            "expected-job-identity": ci_validation_writer_id(
                workflow=workflow,
                job=execution_job,
                matrix=matrix_identity,
            ),
            "provenance-fields": ["workflow", "job", "matrix"],
        },
    }


def _materializer_selector(
    *,
    selector_index: int,
    group: Mapping[str, object],
    expectation: Mapping[str, object],
) -> dict[str, object]:
    return {
        "work-group-id": group["work-group-id"],
        "selector-index": selector_index,
        "depends-on": list(_sequence(group["depends-on"])),
        "expected-evidence-id": expectation["evidence-expectation-id"],
        "expected-evidence-slot": {
            "coverage-target": dict(_mapping(group["coverage-target"])),
            "ecosystem": group.get("ecosystem"),
            "runner-family": group["runner-family"],
            "selector-variant": group.get("selector-variant"),
            "evidence": {
                "category": expectation["category"],
                "planned-capabilities": expectation.get("planned-capabilities"),
                "detail-profile": expectation.get("detail-profile"),
            },
        },
    }


def _materializer_budget(  # noqa: PLR0913
    *,
    plan: Mapping[str, object],
    batches: Sequence[Mapping[str, object]],
    expected_input_non_bundle_validation_artifacts: int,
    max_execution_batches: int,
    non_batch_control_plane_job_count: int,
    aggregate_target_duration_seconds: int,
    aggregate_max_duration_seconds: int,
) -> dict[str, object]:
    batch_count = len(batches)
    final_count = _EXPECTED_FINAL_VALIDATION_ARTIFACTS
    pre_final = expected_input_non_bundle_validation_artifacts + batch_count
    active_runner_family_orchestrators = (
        _active_runner_family_orchestrator_count(batches)
    )
    actual_total_jobs = (
        non_batch_control_plane_job_count + active_runner_family_orchestrators
    )
    actual_windows_jobs = _derived_windows_jobs(
        batches,
        _work_groups_by_id(plan),
    )
    min_total_jobs = actual_total_jobs if batch_count else 0
    min_windows_jobs = actual_windows_jobs if batch_count else 0
    return {
        "min-total-jobs": min_total_jobs,
        "max-total-jobs": _MAX_TOTAL_JOBS,
        "min-windows-jobs": min_windows_jobs,
        "max-windows-jobs": _MAX_WINDOWS_JOBS,
        "non-batch-control-plane-job-count": non_batch_control_plane_job_count,
        "actual-total-jobs": actual_total_jobs,
        "actual-windows-jobs": actual_windows_jobs,
        "max-validation-artifacts": _MAX_VALIDATION_ARTIFACTS,
        "actual-validation-artifacts": pre_final + final_count,
        "expected-input-non-bundle-validation-artifacts": (
            expected_input_non_bundle_validation_artifacts
        ),
        "expected-final-validation-artifacts": final_count,
        "expected-non-bundle-validation-artifacts": (
            expected_input_non_bundle_validation_artifacts + final_count
        ),
        "pre-final-validation-artifacts": pre_final,
        "max-execution-batches": max_execution_batches,
        "actual-execution-batches": batch_count,
        "aggregate-target-duration-seconds": aggregate_target_duration_seconds,
        "aggregate-max-duration-seconds": aggregate_max_duration_seconds,
    }


def _materializer_max_execution_batches(
    *,
    expected_input_non_bundle_validation_artifacts: int,
) -> int:
    bound = _max_execution_batch_bound(
        input_count=expected_input_non_bundle_validation_artifacts,
    )
    if bound is None:
        return _MAX_EXECUTION_BATCHES
    return max(0, bound)


def _expected_input_non_bundle_validation_artifacts(
    plan: Mapping[str, object],
) -> int:
    count = 3
    affected_range = _mapping(plan["affected-range"])
    if affected_range.get("changed-files-hash") is not None:
        count += 1
    fact_snapshot = _mapping(plan["fact-snapshot"])
    if fact_snapshot.get("status") == "available":
        count += 1
    return count


def _validate_budget(  # noqa: C901,PLR0912,PLR0915
    value: object,
    batch_count: int,
    batches: Sequence[Mapping[str, object]],
    plan_work_groups: Mapping[str, Mapping[str, object]],
    _plan: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> None:
    budget = _validate_object(value, _BUDGET_KEYS, "$.budget", issues)
    if budget is None:
        return
    for key in _BUDGET_KEYS:
        _validate_non_negative_int(budget.get(key), f"$.budget.{key}", issues)
    if budget.get("max-validation-artifacts") != _MAX_VALIDATION_ARTIFACTS:
        issues.append(
            ValidationIssue(
                "$.budget.max-validation-artifacts",
                "must be 20",
            ),
        )
    if budget.get("expected-final-validation-artifacts") != (
        _EXPECTED_FINAL_VALIDATION_ARTIFACTS
    ):
        issues.append(
            ValidationIssue(
                "$.budget.expected-final-validation-artifacts",
                "must be 2",
            ),
        )
    if budget.get("max-total-jobs") != _MAX_TOTAL_JOBS:
        issues.append(
            ValidationIssue("$.budget.max-total-jobs", "must be 18"),
        )
    if budget.get("max-windows-jobs") != _MAX_WINDOWS_JOBS:
        issues.append(
            ValidationIssue("$.budget.max-windows-jobs", "must be 8"),
        )
    if budget.get("actual-execution-batches") != batch_count:
        issues.append(
            ValidationIssue(
                "$.budget.actual-execution-batches",
                "must equal batches length",
            ),
        )
    target = budget.get("aggregate-target-duration-seconds")
    maximum = budget.get("aggregate-max-duration-seconds")
    if isinstance(maximum, int) and maximum > _AGGREGATE_MAX_DURATION_SECONDS:
        issues.append(
            ValidationIssue(
                "$.budget.aggregate-max-duration-seconds",
                "must be at most 120",
            ),
        )
    if (
        isinstance(target, int)
        and isinstance(maximum, int)
        and target > maximum
    ):
        issues.append(
            ValidationIssue(
                "$.budget.aggregate-target-duration-seconds",
                "must not exceed aggregate max duration",
            ),
        )
    input_count = budget.get("expected-input-non-bundle-validation-artifacts")
    pre_final = budget.get("pre-final-validation-artifacts")
    final_count = budget.get("expected-final-validation-artifacts")
    expected_non_bundle = budget.get("expected-non-bundle-validation-artifacts")
    actual_artifacts = budget.get("actual-validation-artifacts")
    actual_batches = budget.get("actual-execution-batches")
    actual_total = budget.get("actual-total-jobs")
    max_total = budget.get("max-total-jobs")
    min_total = budget.get("min-total-jobs")
    actual_windows = budget.get("actual-windows-jobs")
    max_windows = budget.get("max-windows-jobs")
    min_windows = budget.get("min-windows-jobs")
    control_plane = budget.get("non-batch-control-plane-job-count")
    max_batches = budget.get("max-execution-batches")
    max_execution_batch_bound = _max_execution_batch_bound(
        input_count=input_count,
    )
    if (
        isinstance(max_batches, int)
        and max_execution_batch_bound is not None
        and max_batches > max_execution_batch_bound
    ):
        issues.append(
            ValidationIssue(
                "$.budget.max-execution-batches",
                f"must be at most {max_execution_batch_bound}",
            ),
        )
    if isinstance(input_count, int) and isinstance(pre_final, int):
        if pre_final != input_count + batch_count:
            issues.append(
                ValidationIssue(
                    "$.budget.pre-final-validation-artifacts",
                    "must equal input non-bundle artifacts plus batches",
                ),
            )
        if pre_final > _MAX_VALIDATION_ARTIFACTS - (
            _EXPECTED_FINAL_VALIDATION_ARTIFACTS
        ):
            issues.append(
                ValidationIssue(
                    "$.budget.pre-final-validation-artifacts",
                    "must leave two final artifact slots",
                ),
            )
    if (
        isinstance(input_count, int)
        and isinstance(final_count, int)
        and expected_non_bundle != input_count + final_count
    ):
        issues.append(
            ValidationIssue(
                "$.budget.expected-non-bundle-validation-artifacts",
                "must equal inputs plus final aggregate artifacts",
            ),
        )
    if (
        isinstance(pre_final, int)
        and isinstance(final_count, int)
        and actual_artifacts != pre_final + final_count
    ):
        issues.append(
            ValidationIssue(
                "$.budget.actual-validation-artifacts",
                "must equal pre-final plus final aggregate artifacts",
            ),
        )
    if (
        isinstance(actual_artifacts, int)
        and actual_artifacts > _MAX_VALIDATION_ARTIFACTS
    ):
        issues.append(
            ValidationIssue(
                "$.budget.actual-validation-artifacts",
                "must be at most 20",
            ),
        )
    if (
        isinstance(actual_batches, int)
        and isinstance(max_batches, int)
        and actual_batches > max_batches
    ):
        issues.append(
            ValidationIssue(
                "$.budget.actual-execution-batches",
                "must not exceed max execution batches",
            ),
        )
    if (
        isinstance(actual_total, int)
        and isinstance(max_total, int)
        and actual_total > max_total
    ):
        issues.append(
            ValidationIssue(
                "$.budget.actual-total-jobs",
                "must not exceed max total jobs",
            ),
        )
    if (
        isinstance(actual_windows, int)
        and isinstance(max_windows, int)
        and actual_windows > max_windows
    ):
        issues.append(
            ValidationIssue(
                "$.budget.actual-windows-jobs",
                "must not exceed max windows jobs",
            ),
        )
    if (
        isinstance(actual_windows, int)
        and isinstance(actual_total, int)
        and actual_windows > actual_total
    ):
        issues.append(
            ValidationIssue(
                "$.budget.actual-windows-jobs",
                "must not exceed actual total jobs",
            ),
        )
    derived_windows = _derived_windows_jobs(batches, plan_work_groups)
    if isinstance(actual_windows, int) and actual_windows != derived_windows:
        issues.append(
            ValidationIssue(
                "$.budget.actual-windows-jobs",
                "must equal Windows batch and control-plane jobs",
            ),
        )
    expected_physical_jobs = (
        control_plane + _active_runner_family_orchestrator_count(batches)
        if isinstance(control_plane, int)
        else None
    )
    if isinstance(actual_total, int) and (
        expected_physical_jobs is not None
        and actual_total != expected_physical_jobs
    ):
        issues.append(
            ValidationIssue(
                "$.budget.actual-total-jobs",
                "must equal control-plane jobs plus active runner-family "
                "orchestrator jobs",
            ),
        )
    if (
        batch_count > 0
        and isinstance(min_total, int)
        and isinstance(actual_total, int)
        and min_total != actual_total
    ):
        issues.append(
            ValidationIssue(
                "$.budget.min-total-jobs",
                "must equal physical total job count",
            ),
        )
    if (
        batch_count > 0
        and isinstance(min_windows, int)
        and isinstance(actual_windows, int)
        and min_windows != actual_windows
    ):
        issues.append(
            ValidationIssue(
                "$.budget.min-windows-jobs",
                "must equal physical Windows job count",
            ),
        )
    lower_bounds_apply = batch_count > 0
    if batch_count == 0:
        if isinstance(min_total, int) and min_total != 0:
            issues.append(
                ValidationIssue(
                    "$.budget.min-total-jobs",
                    "must be zero for empty batch manifests",
                )
            )
        if isinstance(min_windows, int) and min_windows != 0:
            issues.append(
                ValidationIssue(
                    "$.budget.min-windows-jobs",
                    "must be zero for empty batch manifests",
                )
            )
    if (
        lower_bounds_apply
        and isinstance(min_total, int)
        and isinstance(actual_total, int)
        and actual_total < min_total
    ):
        issues.append(
            ValidationIssue(
                "$.budget.actual-total-jobs",
                "must meet min total jobs",
            ),
        )
    if (
        lower_bounds_apply
        and isinstance(min_windows, int)
        and isinstance(actual_windows, int)
        and actual_windows < min_windows
    ):
        issues.append(
            ValidationIssue(
                "$.budget.actual-windows-jobs",
                "must meet min windows jobs",
            ),
        )


def _validate_batches(  # noqa: C901,PLR0912,PLR0913
    value: object,
    envelope: CommonEnvelope | None,
    plan_work_groups: Mapping[str, Mapping[str, object]],
    plan_evidence_expectations: Mapping[str, Mapping[str, object]],
    executable_work_group_ids: set[str] | None,
    issues: list[ValidationIssue],
) -> list[Mapping[str, object]]:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        issues.append(ValidationIssue("$.batches", "must be an array"))
        return []
    if (
        not value
        and executable_work_group_ids is not None
        and executable_work_group_ids != set()
    ):
        issues.append(
            ValidationIssue(
                "$.batches",
                "must include at least one execution batch",
            )
        )
    batches: list[Mapping[str, object]] = []
    batch_ids: set[str] = set()
    selector_ids: list[str] = []
    previous: str | None = None
    for index, item in enumerate(value):
        path = f"$.batches[{index}]"
        if not isinstance(item, Mapping):
            issues.append(ValidationIssue(path, "must be an object"))
            continue
        _validate_batch(
            item,
            path,
            envelope,
            plan_work_groups,
            plan_evidence_expectations,
            issues,
        )
        batch_id = item.get("batch-id")
        if isinstance(batch_id, str):
            if batch_id in batch_ids:
                issues.append(
                    ValidationIssue(f"{path}.batch-id", "must be unique")
                )
            batch_ids.add(batch_id)
            if previous is not None and previous > batch_id:
                issues.append(ValidationIssue("$.batches", "must be sorted"))
            previous = batch_id
        ordered = item.get("ordered-selectors")
        if isinstance(ordered, Sequence) and not isinstance(
            ordered, str | bytes
        ):
            for selector in ordered:
                if isinstance(selector, Mapping) and isinstance(
                    selector.get("work-group-id"),
                    str,
                ):
                    selector_ids.append(str(selector["work-group-id"]))
        batches.append(item)
    for index, item in enumerate(batches):
        deps = item.get("depends-on-batches")
        if not isinstance(deps, Sequence) or isinstance(deps, str | bytes):
            continue
        for dep in deps:
            if dep not in batch_ids:
                issues.append(
                    ValidationIssue(
                        f"$.batches[{index}].depends-on-batches",
                        "must resolve within manifest",
                    ),
                )
    if executable_work_group_ids is not None:
        if set(selector_ids) != executable_work_group_ids:
            issues.append(
                ValidationIssue(
                    "$.batches.ordered-selectors",
                    "must cover executable plan work groups exactly once",
                ),
            )
        if len(selector_ids) != len(set(selector_ids)):
            issues.append(
                ValidationIssue(
                    "$.batches.ordered-selectors",
                    "must not duplicate work groups",
                ),
            )
    _validate_batch_dag(batches, plan_work_groups, issues)
    return batches


def _max_execution_batch_bound(*, input_count: object) -> int | None:
    bounds = [_MAX_EXECUTION_BATCHES]
    if isinstance(input_count, int):
        bounds.append(_MAX_PREFINAL_VALIDATION_ARTIFACTS - input_count)
    return min(bounds)


def _control_plane_windows_from_groups(
    plan_work_groups: Mapping[str, Mapping[str, object]],
) -> int:
    return sum(
        1
        for group in plan_work_groups.values()
        if group.get("kind") == "evidence-aggregation"
        and group.get("runner-family") == "windows"
    )


def _derived_windows_jobs(
    batches: Sequence[Mapping[str, object]],
    plan_work_groups: Mapping[str, Mapping[str, object]],
) -> int:
    batch_windows = int(
        any(batch.get("runner-family") == "windows" for batch in batches)
    )
    control_plane_windows = _control_plane_windows_from_groups(plan_work_groups)
    return batch_windows + control_plane_windows


def _active_runner_family_orchestrator_count(
    batches: Sequence[Mapping[str, object]],
) -> int:
    return len(
        {
            runner_family
            for batch in batches
            if isinstance((runner_family := batch.get("runner-family")), str)
        }
    )


def _validate_batch(  # noqa: PLR0913
    batch: Mapping[str, object],
    path: str,
    envelope: CommonEnvelope | None,
    plan_work_groups: Mapping[str, Mapping[str, object]],
    plan_evidence_expectations: Mapping[str, Mapping[str, object]],
    issues: list[ValidationIssue],
) -> None:
    _validate_root_keys(batch, _BATCH_KEYS, path, issues)
    _validate_local_id(batch.get("batch-id"), f"{path}.batch-id", issues)
    if batch.get("runner-family") not in _RUNNER_FAMILIES:
        issues.append(
            ValidationIssue(f"{path}.runner-family", "is not registered")
        )
    _validate_compatibility_profile(
        batch.get("compatibility-profile"),
        f"{path}.compatibility-profile",
        issues,
    )
    _validate_id_array(
        batch.get("depends-on-batches"),
        f"{path}.depends-on-batches",
        issues,
    )
    _validate_ordered_selectors(
        batch.get("ordered-selectors"),
        path,
        batch.get("runner-family"),
        plan_work_groups,
        plan_evidence_expectations,
        issues,
    )
    _validate_expected_bundle_ref(batch, path, envelope, issues)
    writer = _validate_object(
        batch.get("batch-writer"),
        _BATCH_WRITER_KEYS,
        f"{path}.batch-writer",
        issues,
    )
    if writer is not None:
        if writer.get("identity-source") != "github-actions-job-context":
            issues.append(
                ValidationIssue(
                    f"{path}.batch-writer.identity-source",
                    "must be github-actions-job-context",
                ),
            )
        if writer.get("expected-boundary") != "execution-batch":
            issues.append(
                ValidationIssue(
                    f"{path}.batch-writer.expected-boundary",
                    "must be execution-batch",
                ),
            )
        _validate_non_empty_string(
            writer.get("expected-job-identity"),
            f"{path}.batch-writer.expected-job-identity",
            issues,
        )
        if writer.get("provenance-fields") != ["workflow", "job", "matrix"]:
            issues.append(
                ValidationIssue(
                    f"{path}.batch-writer.provenance-fields",
                    "must be [workflow, job, matrix]",
                ),
            )


def _validate_batch_writer_identities(
    batches: Sequence[Mapping[str, object]],
    envelope: CommonEnvelope | None,
    execution_job: object,
    issues: list[ValidationIssue],
) -> None:
    if envelope is None or not isinstance(execution_job, str):
        return
    for index, batch in enumerate(batches):
        writer = batch.get("batch-writer")
        if not isinstance(writer, Mapping):
            continue
        if not _batch_has_matrix_identity(batch):
            continue
        expected = ci_validation_writer_id(
            workflow=envelope.workflow,
            job=execution_job,
            matrix=_execution_batch_matrix_identity(batch),
        )
        if writer.get("expected-job-identity") != expected:
            issues.append(
                ValidationIssue(
                    f"$.batches[{index}].batch-writer.expected-job-identity",
                    "must match execution job context",
                )
            )


def _batch_has_matrix_identity(batch: Mapping[str, object]) -> bool:
    return (
        isinstance(batch.get("batch-id"), str)
        and batch.get("runner-family") in _RUNNER_FAMILIES
        and isinstance(batch.get("expected-batch-evidence-bundle-ref"), str)
    )


def _validate_plan_bound_batch_materialization(  # noqa: PLR0913
    batches: Sequence[Mapping[str, object]],
    plan: Mapping[str, object],
    manifest_budget: object,
    envelope: CommonEnvelope | None,
    execution_job: object,
    issues: list[ValidationIssue],
) -> None:
    expected = _expected_plan_bound_materialization(
        plan,
        manifest_budget=manifest_budget,
        envelope=envelope,
        execution_job=execution_job,
        issues=issues,
    )
    if expected is None:
        return
    expected_batches, expected_budget = expected
    if len(batches) != len(expected_batches):
        issues.append(
            ValidationIssue("$.batches", "must match materializer batch count")
        )
    for index, (batch, expected_batch) in enumerate(
        zip(batches, expected_batches, strict=False)
    ):
        for key in (
            "batch-id",
            "runner-family",
            "compatibility-profile",
            "depends-on-batches",
            "ordered-selectors",
            "expected-batch-evidence-bundle-ref",
            "batch-writer",
        ):
            if batch.get(key) != expected_batch.get(key):
                issues.append(
                    ValidationIssue(
                        f"$.batches[{index}].{key}",
                        "must match materialized plan batch",
                    )
                )
    if (
        isinstance(manifest_budget, Mapping)
        and manifest_budget != expected_budget
    ):
        issues.append(
            ValidationIssue("$.budget", "must match materialized plan")
        )


def _expected_plan_bound_materialization(  # noqa: C901,PLR0911
    plan: Mapping[str, object],
    *,
    manifest_budget: object,
    envelope: CommonEnvelope | None,
    execution_job: object,
    issues: list[ValidationIssue],
) -> tuple[list[dict[str, object]], dict[str, object]] | None:
    if envelope is None or not isinstance(execution_job, str):
        return None
    try:
        groups = _materializer_executable_work_groups(plan)
        expectations = _materializer_evidence_by_work_group(plan, groups)
    except ContractValidationError as error:
        issues.extend(error.issues)
        return None
    except (KeyError, TypeError, ValueError) as error:
        issues.append(ValidationIssue("plan.work-groups", str(error)))
        return None
    if not _validate_materializer_selected_plan_fields(
        plan,
        groups,
        expectations,
        issues,
    ):
        return None
    try:
        ordered_group_ids = _materializer_topological_work_group_ids(groups)
        batch_specs = _materializer_batch_specs(plan, groups, ordered_group_ids)
        expected_input_count = _expected_input_non_bundle_validation_artifacts(
            plan
        )
    except ContractValidationError as error:
        issues.extend(error.issues)
        return None
    except (KeyError, TypeError, ValueError) as error:
        issues.append(ValidationIssue("plan.work-groups", str(error)))
        return None
    if isinstance(manifest_budget, Mapping):
        control_plane = manifest_budget.get("non-batch-control-plane-job-count")
        target_duration = manifest_budget.get(
            "aggregate-target-duration-seconds"
        )
        max_duration = manifest_budget.get("aggregate-max-duration-seconds")
    else:
        control_plane = None
        target_duration = None
        max_duration = None
    if (
        not isinstance(control_plane, int)
        or isinstance(control_plane, bool)
        or not isinstance(target_duration, int)
        or isinstance(target_duration, bool)
        or not isinstance(max_duration, int)
        or isinstance(max_duration, bool)
    ):
        return None
    try:
        max_batches = _materializer_max_execution_batches(
            expected_input_non_bundle_validation_artifacts=expected_input_count,
        )
        expected_batches = _materializer_batches(
            envelope=envelope,
            workflow=envelope.workflow,
            execution_job=execution_job,
            batch_specs=batch_specs,
            groups=groups,
            expectations=expectations,
        )
        expected_budget = _materializer_budget(
            plan=plan,
            batches=expected_batches,
            expected_input_non_bundle_validation_artifacts=expected_input_count,
            max_execution_batches=max_batches,
            non_batch_control_plane_job_count=control_plane,
            aggregate_target_duration_seconds=target_duration,
            aggregate_max_duration_seconds=max_duration,
        )
    except ContractValidationError as error:
        issues.extend(error.issues)
        return None
    except (KeyError, TypeError, ValueError) as error:
        issues.append(ValidationIssue("plan.work-groups", str(error)))
        return None
    return expected_batches, expected_budget


def _validate_materializer_selected_plan_fields(
    plan: Mapping[str, object],
    groups: Mapping[str, Mapping[str, object]],
    expectations: Mapping[str, Mapping[str, object]],
    issues: list[ValidationIssue],
) -> bool:
    before = len(issues)
    for work_group_id, group in groups.items():
        group_path = f"plan.work-groups.{work_group_id}"
        if not isinstance(group.get("coverage-target"), Mapping):
            issues.append(
                ValidationIssue(f"{group_path}.coverage-target", "is required")
            )
        if group.get("runner-family") not in _RUNNER_FAMILIES:
            issues.append(
                ValidationIssue(
                    f"{group_path}.runner-family",
                    "is not registered",
                )
            )
        depends_on = group.get("depends-on")
        if not isinstance(depends_on, Sequence) or isinstance(
            depends_on, str | bytes
        ):
            issues.append(
                ValidationIssue(f"{group_path}.depends-on", "must be an array")
            )
    for work_group_id, expectation in expectations.items():
        expectation_path = f"plan.evidence-expectations.{work_group_id}"
        for key in ("evidence-expectation-id", "category"):
            if not isinstance(expectation.get(key), str):
                issues.append(
                    ValidationIssue(f"{expectation_path}.{key}", "is required")
                )
    if not isinstance(plan.get("affected-range"), Mapping):
        issues.append(ValidationIssue("plan.affected-range", "is required"))
    if not isinstance(plan.get("fact-snapshot"), Mapping):
        issues.append(ValidationIssue("plan.fact-snapshot", "is required"))
    return len(issues) == before


def _validate_compatibility_profile(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    profile = _validate_object(value, _COMPATIBILITY_KEYS, path, issues)
    if profile is None:
        return
    ecosystem = profile.get("ecosystem")
    if ecosystem is not None and ecosystem not in _ECOSYSTEMS:
        issues.append(ValidationIssue(f"{path}.ecosystem", "is not registered"))
    for key in ("setup-profile", "execution-profile"):
        _validate_local_id(profile.get(key), f"{path}.{key}", issues)
    for key in ("setup-profile-digest", "execution-profile-digest"):
        _validate_digest(profile.get(key), f"{path}.{key}", issues)
    release_profile = profile.get("release-shaped-profile")
    release_digest = profile.get("release-shaped-profile-digest")
    if release_profile is None:
        if release_digest is not None:
            issues.append(
                ValidationIssue(
                    f"{path}.release-shaped-profile-digest",
                    "must be null when release-shaped-profile is null",
                ),
            )
    else:
        _validate_local_id(
            release_profile, f"{path}.release-shaped-profile", issues
        )
        _validate_digest(
            release_digest,
            f"{path}.release-shaped-profile-digest",
            issues,
        )


def _validate_ordered_selectors(  # noqa: C901,PLR0912,PLR0913
    value: object,
    batch_path: str,
    batch_runner_family: object,
    plan_work_groups: Mapping[str, Mapping[str, object]],
    plan_evidence_expectations: Mapping[str, Mapping[str, object]],
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        issues.append(
            ValidationIssue(
                f"{batch_path}.ordered-selectors", "must be an array"
            ),
        )
        return
    if len(value) == 0:
        issues.append(
            ValidationIssue(
                f"{batch_path}.ordered-selectors",
                "must contain at least one selector",
            ),
        )
    seen: set[str] = set()
    for index, selector in enumerate(value):
        path = f"{batch_path}.ordered-selectors[{index}]"
        if not isinstance(selector, Mapping):
            issues.append(ValidationIssue(path, "must be an object"))
            continue
        _validate_root_keys(selector, _ORDERED_SELECTOR_KEYS, path, issues)
        work_group_id = selector.get("work-group-id")
        _validate_local_id(work_group_id, f"{path}.work-group-id", issues)
        if isinstance(work_group_id, str):
            if work_group_id in seen:
                issues.append(
                    ValidationIssue(f"{path}.work-group-id", "must be unique"),
                )
            seen.add(work_group_id)
            group = plan_work_groups.get(work_group_id)
            if group is not None and selector.get("depends-on") != group.get(
                "depends-on",
            ):
                issues.append(
                    ValidationIssue(
                        f"{path}.depends-on",
                        "must match plan work group",
                    ),
                )
        if selector.get("selector-index") != index:
            issues.append(
                ValidationIssue(
                    f"{path}.selector-index", "must match array index"
                ),
            )
        _validate_id_array(
            selector.get("depends-on"), f"{path}.depends-on", issues
        )
        _validate_local_id(
            selector.get("expected-evidence-id"),
            f"{path}.expected-evidence-id",
            issues,
        )
        expected_evidence_id = selector.get("expected-evidence-id")
        slot = selector.get("expected-evidence-slot")
        if not isinstance(slot, Mapping):
            issues.append(
                ValidationIssue(
                    f"{path}.expected-evidence-slot",
                    "must be an object",
                ),
            )
        else:
            if _contains_execution_result_data(slot):
                issues.append(
                    ValidationIssue(
                        f"{path}.expected-evidence-slot",
                        "must not contain execution result data",
                    ),
                )
            if isinstance(work_group_id, str):
                group = plan_work_groups.get(work_group_id)
                if group is not None:
                    _validate_slot_matches_work_group(slot, group, path, issues)
                    if (
                        batch_runner_family in _RUNNER_FAMILIES
                        and group.get("runner-family") != batch_runner_family
                    ):
                        issues.append(
                            ValidationIssue(
                                f"{path}.work-group-id",
                                "must match batch runner family",
                            )
                        )
                    if (
                        batch_runner_family in _RUNNER_FAMILIES
                        and slot.get("runner-family") != batch_runner_family
                    ):
                        issues.append(
                            ValidationIssue(
                                f"{path}.expected-evidence-slot.runner-family",
                                "must match batch runner family",
                            )
                        )
            if isinstance(expected_evidence_id, str):
                evidence = plan_evidence_expectations.get(expected_evidence_id)
                if plan_evidence_expectations and evidence is None:
                    issues.append(
                        ValidationIssue(
                            f"{path}.expected-evidence-id",
                            "must resolve to plan evidence expectation",
                        )
                    )
                if evidence is not None:
                    if evidence.get("work-group-id") != work_group_id:
                        issues.append(
                            ValidationIssue(
                                f"{path}.expected-evidence-id",
                                "must match selected work group",
                            )
                        )
                    _validate_slot_matches_evidence(
                        slot, evidence, path, issues
                    )


def _contains_execution_result_data(value: object) -> bool:
    forbidden = {
        "outcome",
        "diagnostics",
        "observed",
        "observed-artifact-refs",
        "observed-digests",
        "command-output",
        "started-at",
        "completed-at",
    }
    if isinstance(value, Mapping):
        return any(
            key in forbidden or _contains_execution_result_data(item)
            for key, item in value.items()
        )
    if isinstance(value, Sequence) and not isinstance(value, str | bytes):
        return any(_contains_execution_result_data(item) for item in value)
    return False


def _validate_expected_bundle_ref(
    batch: Mapping[str, object],
    path: str,
    envelope: CommonEnvelope | None,
    issues: list[ValidationIssue],
) -> None:
    _validate_artifact_ref(
        batch.get("expected-batch-evidence-bundle-ref"),
        f"{path}.expected-batch-evidence-bundle-ref",
        issues,
    )
    if envelope is None or not isinstance(batch.get("batch-id"), str):
        return
    expected = ci_validation_batch_evidence_bundle_artifact_ref(
        run_id=envelope.run_id,
        run_attempt=envelope.run_attempt,
        batch_id=str(batch["batch-id"]),
    )
    if batch.get("expected-batch-evidence-bundle-ref") != expected:
        issues.append(
            ValidationIssue(
                f"{path}.expected-batch-evidence-bundle-ref",
                "must match batch id",
            ),
        )


def _validate_batch_dag(
    batches: Sequence[Mapping[str, object]],
    plan_work_groups: Mapping[str, Mapping[str, object]],
    issues: list[ValidationIssue],
) -> None:
    deps_by_id = {
        str(batch["batch-id"]): [
            str(item)
            for item in _sequence(batch.get("depends-on-batches", []))
            if isinstance(item, str)
        ]
        for batch in batches
        if isinstance(batch.get("batch-id"), str)
    }
    visiting: set[str] = set()
    visited: set[str] = set()

    def visit(batch_id: str) -> bool:
        if batch_id in visited:
            return False
        if batch_id in visiting:
            return True
        visiting.add(batch_id)
        has_cycle = any(visit(dep) for dep in deps_by_id.get(batch_id, []))
        visiting.remove(batch_id)
        visited.add(batch_id)
        return has_cycle

    if any(visit(batch_id) for batch_id in deps_by_id):
        issues.append(
            ValidationIssue("$.batches.depends-on-batches", "must be acyclic")
        )
    if plan_work_groups:
        _validate_batch_dag_matches_plan_dependencies(
            batches, deps_by_id, plan_work_groups, issues
        )


def _validate_batch_dag_matches_plan_dependencies(  # noqa: C901,PLR0912
    batches: Sequence[Mapping[str, object]],
    deps_by_id: Mapping[str, Sequence[str]],
    plan_work_groups: Mapping[str, Mapping[str, object]],
    issues: list[ValidationIssue],
) -> None:
    positions: dict[str, tuple[str, int, int]] = {}
    for batch_index, batch in enumerate(batches):
        batch_id = batch.get("batch-id")
        selectors = batch.get("ordered-selectors")
        if (
            not isinstance(batch_id, str)
            or not isinstance(selectors, Sequence)
            or isinstance(selectors, str | bytes)
        ):
            continue
        for selector_index, selector in enumerate(selectors):
            if not isinstance(selector, Mapping):
                continue
            work_group_id = selector.get("work-group-id")
            if isinstance(work_group_id, str):
                positions[work_group_id] = (
                    batch_id,
                    batch_index,
                    selector_index,
                )
    expected_edges: set[tuple[str, str]] = set()
    for consumer_id, consumer_position in positions.items():
        consumer_batch = consumer_position[0]
        consumer_group = plan_work_groups.get(consumer_id)
        depends_on = (
            consumer_group.get("depends-on")
            if consumer_group is not None
            else None
        )
        if not isinstance(depends_on, Sequence) or isinstance(
            depends_on, str | bytes
        ):
            continue
        for producer_id in depends_on:
            if not isinstance(producer_id, str) or producer_id not in positions:
                continue
            producer_batch, _, producer_selector_index = positions[producer_id]
            if producer_batch == consumer_batch:
                if producer_selector_index >= consumer_position[2]:
                    issues.append(
                        ValidationIssue(
                            "$.batches.ordered-selectors",
                            "in-batch selector dependencies must appear "
                            "earlier",
                        )
                    )
            else:
                expected_edges.add((consumer_batch, producer_batch))
    declared_edges = {
        (consumer_batch, producer_batch)
        for consumer_batch, producer_batches in deps_by_id.items()
        for producer_batch in producer_batches
    }
    for edge in sorted(expected_edges - declared_edges):
        issues.append(
            ValidationIssue(
                "$.batches.depends-on-batches",
                f"must include plan dependency edge {edge[0]} -> {edge[1]}",
            )
        )
    for edge in sorted(declared_edges - expected_edges):
        issues.append(
            ValidationIssue(
                "$.batches.depends-on-batches",
                "must not include stale dependency edge "
                f"{edge[0]} -> {edge[1]}",
            )
        )


def _validate_id_array(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        issues.append(ValidationIssue(path, "must be an array"))
        return
    seen: set[str] = set()
    for index, item in enumerate(value):
        _validate_local_id(item, f"{path}[{index}]", issues)
        if isinstance(item, str):
            if item in seen:
                issues.append(ValidationIssue(path, "must be unique"))
            seen.add(item)


def _validate_plan_context_canonical_arrays(
    plan: Mapping[str, object],
    issues: list[ValidationIssue],
) -> bool:
    initial_issue_count = len(issues)
    for section, id_key in (
        ("work-groups", "work-group-id"),
        ("evidence-expectations", "evidence-expectation-id"),
        ("validation-obligations", "validation-obligation-id"),
        ("descriptor-obligations", "descriptor-obligation-id"),
        ("artifact-obligations", "artifact-obligation-id"),
        ("diagnostics", "diagnostic-id"),
    ):
        _validate_plan_identifier_record_order(
            plan.get(section),
            id_key,
            f"$.{section}",
            issues,
        )
    return len(issues) == initial_issue_count


def _validate_plan_identifier_record_order(
    records: object,
    id_key: str,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(records, Sequence) or isinstance(records, str | bytes):
        issues.append(ValidationIssue(path, "must be an array"))
        return
    frozen: list[Mapping[str, object]] = []
    identifiers: list[str] = []
    for index, record in enumerate(records):
        if not isinstance(record, Mapping):
            issues.append(ValidationIssue(f"{path}[{index}]", "must be object"))
            continue
        frozen.append(record)
        identifier = record.get(id_key)
        if not isinstance(identifier, str) or identifier == "":
            issues.append(
                ValidationIssue(f"{path}[{index}].{id_key}", "is required")
            )
            continue
        identifiers.append(identifier)
    expected = sorted(frozen, key=lambda item: str(item.get(id_key)))
    if frozen != expected or len(identifiers) != len(set(identifiers)):
        issues.append(
            ValidationIssue(path, f"must be ordered uniquely by {id_key}")
        )


def _work_groups_by_id(
    plan: Mapping[str, object],
) -> dict[str, Mapping[str, object]]:
    groups = plan.get("work-groups")
    if not isinstance(groups, Sequence) or isinstance(groups, str | bytes):
        return {}
    return {
        str(group["work-group-id"]): group
        for group in groups
        if isinstance(group, Mapping)
        and isinstance(group.get("work-group-id"), str)
    }


def _evidence_expectations_by_id(
    plan: Mapping[str, object],
) -> dict[str, Mapping[str, object]]:
    expectations = plan.get("evidence-expectations")
    if not isinstance(expectations, Sequence) or isinstance(
        expectations, str | bytes
    ):
        return {}
    return {
        str(expectation["evidence-expectation-id"]): expectation
        for expectation in expectations
        if isinstance(expectation, Mapping)
        and isinstance(expectation.get("evidence-expectation-id"), str)
    }


def _validate_slot_matches_work_group(
    slot: Mapping[str, object],
    group: Mapping[str, object],
    selector_path: str,
    issues: list[ValidationIssue],
) -> None:
    slot_path = f"{selector_path}.expected-evidence-slot"
    for key in (
        "coverage-target",
        "ecosystem",
        "runner-family",
        "selector-variant",
    ):
        if slot.get(key) != group.get(key):
            issues.append(
                ValidationIssue(f"{slot_path}.{key}", "must match plan")
            )


def _validate_slot_matches_evidence(
    slot: Mapping[str, object],
    evidence: Mapping[str, object],
    selector_path: str,
    issues: list[ValidationIssue],
) -> None:
    slot_path = f"{selector_path}.expected-evidence-slot"
    if slot.get("coverage-target") != evidence.get("coverage-target"):
        issues.append(
            ValidationIssue(f"{slot_path}.coverage-target", "must match plan")
        )
    slot_evidence = slot.get("evidence")
    if not isinstance(slot_evidence, Mapping):
        issues.append(
            ValidationIssue(f"{slot_path}.evidence", "must be object")
        )
        return
    for slot_key, evidence_key in (
        ("category", "category"),
        ("planned-capabilities", "planned-capabilities"),
    ):
        if slot_evidence.get(slot_key) != evidence.get(evidence_key):
            issues.append(
                ValidationIssue(
                    f"{slot_path}.evidence.{slot_key}",
                    "must match plan",
                )
            )


def _batch_by_id(
    manifest: Mapping[str, object],
    batch_id: str,
) -> Mapping[str, object]:
    batches = manifest.get("batches")
    if isinstance(batches, Sequence) and not isinstance(batches, str | bytes):
        for batch in batches:
            if isinstance(batch, Mapping) and batch.get("batch-id") == batch_id:
                return batch
    raise ContractValidationError(
        [ValidationIssue("batch-id", "must exist in execution-batch manifest")],
    )


def _bundle_batch_projection(
    batch: Mapping[str, object],
) -> dict[str, object]:
    return {
        "batch-id": batch["batch-id"],
        "runner-family": batch["runner-family"],
        "compatibility-profile": dict(
            _mapping(batch["compatibility-profile"]),
        ),
        "depends-on-batches": list(_sequence(batch["depends-on-batches"])),
    }


def _validate_bundle_plan_fields(
    bundle: Mapping[str, object],
    plan: Mapping[str, object],
    envelope: CommonEnvelope | None,
    issues: list[ValidationIssue],
) -> None:
    plan_envelope = _validated_plan_envelope(plan, issues)
    if envelope is not None and plan_envelope is not None:
        _validate_envelope_matches(envelope, plan_envelope, issues)
    if bundle.get("plan-id") != plan.get("plan-id"):
        issues.append(ValidationIssue("$.plan-id", "must match plan"))
    if bundle.get("plan-digest") != _verified_plan_digest_or_none(plan):
        issues.append(ValidationIssue("$.plan-digest", "must match plan"))
    if bundle.get("mode") != plan.get("mode"):
        issues.append(ValidationIssue("$.mode", "must match plan"))
    if bundle.get("validation-tree") != plan.get("validation-tree"):
        issues.append(ValidationIssue("$.validation-tree", "must match plan"))
    try:
        affected_range = _summary_affected_range(plan)
    except ContractValidationError as error:
        issues.extend(error.issues)
    else:
        if bundle.get("affected-range") != affected_range:
            issues.append(
                ValidationIssue("$.affected-range", "must match plan")
            )
    if bundle.get("scheduled-full") != plan.get("scheduled-full"):
        issues.append(ValidationIssue("$.scheduled-full", "must match plan"))


def _validate_bundle_manifest_fields(  # noqa: PLR0913
    bundle: Mapping[str, object],
    manifest: Mapping[str, object],
    envelope: CommonEnvelope | None,
    plan: Mapping[str, object] | None,
    request: Mapping[str, object] | None,
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    expected_run_id: str | None,
    expected_run_attempt: str | None,
    authorizing: bool,  # noqa: FBT001
    issues: list[ValidationIssue],
) -> Mapping[str, object] | None:
    try:
        _validate_ci_validation_execution_batch_manifest(
            manifest,
            plan=plan,
            request=request,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            expected_envelope=envelope,
            expected_run_id=(
                expected_run_id
                if authorizing
                else envelope.run_id
                if envelope is not None
                else None
            ),
            expected_run_attempt=(
                expected_run_attempt
                if authorizing
                else envelope.run_attempt
                if envelope is not None
                else None
            ),
            authorizing=authorizing,
            _allow_planless_non_authorizing_batches=(
                _allow_planless_execution_manifest_diagnostic(plan, manifest)
                and not authorizing
            ),
        )
    except ContractValidationError as error:
        issues.extend(error.issues)
        return None
    _validate_plan_identity_matches(
        bundle,
        manifest,
        "$",
        "execution-batch manifest",
        issues,
    )
    manifest_claim = bundle.get("execution-batch-manifest")
    if manifest_claim is not None and envelope is not None:
        if not isinstance(manifest_claim, Mapping):
            return None
        expected_ref = ci_validation_execution_batch_manifest_artifact_ref(
            run_id=envelope.run_id,
            run_attempt=envelope.run_attempt,
        )
        if manifest_claim.get("artifact-ref") != expected_ref:
            issues.append(
                ValidationIssue(
                    "$.execution-batch-manifest.artifact-ref",
                    "must match run",
                ),
            )
        if manifest_claim.get("content-digest") != (
            ci_validation_execution_batch_manifest_payload_digest(manifest)
        ):
            issues.append(
                ValidationIssue(
                    "$.execution-batch-manifest.content-digest",
                    "must match manifest",
                ),
            )
    batch_projection = bundle.get("batch")
    batch_id = (
        batch_projection.get("batch-id")
        if isinstance(batch_projection, Mapping)
        else None
    )
    if not isinstance(batch_id, str):
        return None
    batch = _batch_by_id(manifest, batch_id)
    if batch_projection != _bundle_batch_projection(batch):
        issues.append(ValidationIssue("$.batch", "must match manifest batch"))
    return batch


def _validate_bundle_manifest_claim(
    value: object,
    envelope: CommonEnvelope | None,
    execution_batch_manifest: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> None:
    claim = _validate_object(
        value,
        frozenset({"artifact-ref", "content-digest"}),
        "$.execution-batch-manifest",
        issues,
    )
    if claim is None:
        return
    _validate_root_keys(
        claim,
        frozenset({"artifact-ref", "content-digest"}),
        "$.execution-batch-manifest",
        issues,
    )
    _validate_artifact_ref(
        claim.get("artifact-ref"),
        "$.execution-batch-manifest.artifact-ref",
        issues,
    )
    _validate_digest(
        claim.get("content-digest"),
        "$.execution-batch-manifest.content-digest",
        issues,
    )
    if envelope is not None:
        expected_ref = ci_validation_execution_batch_manifest_artifact_ref(
            run_id=envelope.run_id,
            run_attempt=envelope.run_attempt,
        )
        if claim.get("artifact-ref") != expected_ref:
            issues.append(
                ValidationIssue(
                    "$.execution-batch-manifest.artifact-ref",
                    "must match run",
                ),
            )
    if execution_batch_manifest is None:
        issues.append(
            ValidationIssue(
                "$.execution-batch-manifest",
                "requires authoritative execution-batch manifest",
            )
        )


def _validate_bundle_batch_projection_shape(
    value: object,
    issues: list[ValidationIssue],
) -> None:
    projection = _validate_object(
        value,
        frozenset(
            {
                "batch-id",
                "runner-family",
                "compatibility-profile",
                "depends-on-batches",
            }
        ),
        "$.batch",
        issues,
    )
    if projection is None:
        return
    _validate_root_keys(
        projection,
        frozenset(
            {
                "batch-id",
                "runner-family",
                "compatibility-profile",
                "depends-on-batches",
            }
        ),
        "$.batch",
        issues,
    )
    _validate_local_id(projection.get("batch-id"), "$.batch.batch-id", issues)
    if projection.get("runner-family") not in _RUNNER_FAMILIES:
        issues.append(
            ValidationIssue("$.batch.runner-family", "is not registered")
        )
    _validate_compatibility_profile(
        projection.get("compatibility-profile"),
        "$.batch.compatibility-profile",
        issues,
    )
    depends_on = projection.get("depends-on-batches")
    if not isinstance(depends_on, Sequence) or isinstance(
        depends_on, str | bytes
    ):
        issues.append(
            ValidationIssue("$.batch.depends-on-batches", "must be an array")
        )
    else:
        for index, item in enumerate(depends_on):
            _validate_local_id(
                item,
                f"$.batch.depends-on-batches[{index}]",
                issues,
            )


def _validate_bundle_artifact_ref(
    bundle: Mapping[str, object],
    envelope: CommonEnvelope | None,
    batch: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> None:
    _validate_artifact_ref(bundle.get("artifact-ref"), "$.artifact-ref", issues)
    _validate_non_empty_string(bundle.get("bundle-id"), "$.bundle-id", issues)
    if (
        envelope is None
        or batch is None
        or not isinstance(batch.get("batch-id"), str)
    ):
        return
    artifact_ref = ci_validation_batch_evidence_bundle_artifact_ref(
        run_id=envelope.run_id,
        run_attempt=envelope.run_attempt,
        batch_id=str(batch["batch-id"]),
    )
    if bundle.get("artifact-ref") != artifact_ref:
        issues.append(ValidationIssue("$.artifact-ref", "must match batch"))
    manifest_claim = bundle.get("execution-batch-manifest")
    if isinstance(manifest_claim, Mapping):
        manifest_digest = manifest_claim.get("content-digest")
        if isinstance(manifest_digest, str):
            expected_id = ci_validation_batch_evidence_bundle_id(
                run_id=envelope.run_id,
                run_attempt=envelope.run_attempt,
                batch_id=str(batch["batch-id"]),
                execution_batch_manifest_digest=manifest_digest,
                artifact_ref=artifact_ref,
            )
            if bundle.get("bundle-id") != expected_id:
                issues.append(
                    ValidationIssue("$.bundle-id", "must be deterministic"),
                )


def _validate_writer(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(value, Mapping):
        return
    identity_source = value.get("identity-source")
    if identity_source not in {
        "github-actions-job-context",
        "github-actions-orchestrator-job-context",
    }:
        issues.append(
            ValidationIssue(
                f"{path}.identity-source",
                "must be github-actions-job-context or "
                "github-actions-orchestrator-job-context",
            ),
        )
    if value.get("expected-boundary") != "execution-batch":
        issues.append(
            ValidationIssue(
                f"{path}.expected-boundary",
                "must be execution-batch",
            ),
        )
    for key in (
        "expected-job-identity",
        "observed-workflow",
        "observed-job",
    ):
        _validate_non_empty_string(value.get(key), f"{path}.{key}", issues)
    if "observed-writer-identity" in value:
        _validate_non_empty_string(
            value.get("observed-writer-identity"),
            f"{path}.observed-writer-identity",
            issues,
        )
    observed_matrix = value.get("observed-matrix")
    if observed_matrix is not None and not isinstance(observed_matrix, Mapping):
        issues.append(
            ValidationIssue(f"{path}.observed-matrix", "must be object or null")
        )
    _validate_observed_writer_identity(value, path, issues)
    logical_identity = value.get("logical-batch-identity")
    if logical_identity is not None and not isinstance(
        logical_identity, Mapping
    ):
        issues.append(
            ValidationIssue(
                f"{path}.logical-batch-identity",
                "must be object or null",
            )
        )
    _validate_orchestrator_slot(value, path, issues, identity_source)


def _validate_orchestrator_slot(
    value: Mapping[str, object],
    path: str,
    issues: list[ValidationIssue],
    identity_source: object,
) -> None:
    slot = value.get("observed-orchestrator-slot-index")
    if identity_source == "github-actions-orchestrator-job-context" and (
        not isinstance(slot, str) or slot == ""
    ):
        issues.append(
            ValidationIssue(
                f"{path}.observed-orchestrator-slot-index",
                "must be a non-empty string for orchestrator job context",
            )
        )
    elif identity_source == "github-actions-job-context" and slot is not None:
        issues.append(
            ValidationIssue(
                f"{path}.observed-orchestrator-slot-index",
                "must be absent or null for direct job context",
            )
        )
    elif slot is not None and (not isinstance(slot, str) or slot == ""):
        issues.append(
            ValidationIssue(
                f"{path}.observed-orchestrator-slot-index",
                "must be a non-empty string",
            )
        )


def _validate_observed_writer_identity(
    value: Mapping[str, object],
    path: str,
    issues: list[ValidationIssue],
) -> None:
    observed_identity = value.get("observed-writer-identity")
    observed_workflow = value.get("observed-workflow")
    observed_job = value.get("observed-job")
    observed_matrix = value.get("observed-matrix")
    if not (
        isinstance(observed_identity, str)
        and isinstance(observed_workflow, str)
        and isinstance(observed_job, str)
        and isinstance(observed_matrix, Mapping)
    ):
        return
    try:
        expected_identity = ci_validation_writer_id(
            workflow=observed_workflow,
            job=observed_job,
            matrix=observed_matrix,
        )
    except ContractValidationError as error:
        issues.extend(error.issues)
        return
    if observed_identity != expected_identity:
        issues.append(
            ValidationIssue(
                f"{path}.observed-writer-identity",
                "must match observed workflow/job/matrix identity",
            )
        )


def _validate_bundle_writer_matches_batch(  # noqa: C901, PLR0912
    writer: object,
    batch: Mapping[str, object] | None,
    manifest: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(writer, Mapping) or batch is None:
        return
    batch_writer = batch.get("batch-writer")
    if not isinstance(batch_writer, Mapping):
        return
    for key in (
        "expected-job-identity",
        "expected-boundary",
    ):
        if (
            key in writer
            and key in batch_writer
            and writer.get(key) != batch_writer.get(key)
        ):
            issues.append(
                ValidationIssue(
                    f"$.writer.{key}",
                    "must match manifest batch writer",
                )
            )
    if (
        writer.get("identity-source") != batch_writer.get("identity-source")
        and writer.get("identity-source")
        != "github-actions-orchestrator-job-context"
    ):
        issues.append(
            ValidationIssue(
                "$.writer.identity-source",
                "must match manifest batch writer or orchestrator context",
            )
        )
    if manifest is None or not _batch_has_matrix_identity(batch):
        return
    run = manifest.get("run")
    if isinstance(run, Mapping):
        expected_workflow = run.get("workflow")
        if (
            isinstance(expected_workflow, str)
            and writer.get("observed-workflow") != expected_workflow
        ):
            issues.append(
                ValidationIssue(
                    "$.writer.observed-workflow",
                    "must match execution-batch manifest workflow",
                )
            )
    expected_matrix = _execution_batch_matrix_identity(batch)
    identity_source = writer.get("identity-source")
    if identity_source == "github-actions-orchestrator-job-context":
        expected_job = (
            f"execution-batch-{expected_matrix['runner-family']}-orchestrator"
        )
        if writer.get("observed-job") != expected_job:
            issues.append(
                ValidationIssue(
                    "$.writer.observed-job",
                    "must match runner-family orchestrator job",
                )
            )
        if writer.get("observed-matrix") != {}:
            issues.append(
                ValidationIssue(
                    "$.writer.observed-matrix",
                    "must match physical orchestrator job matrix",
                )
            )
        if writer.get("logical-batch-identity") != expected_matrix:
            issues.append(
                ValidationIssue(
                    "$.writer.logical-batch-identity",
                    "must match execution-batch matrix identity",
                )
            )
        return
    expected_job = manifest.get("execution-job")
    if (
        isinstance(expected_job, str)
        and writer.get("observed-job") != expected_job
    ):
        issues.append(
            ValidationIssue(
                "$.writer.observed-job",
                "must match execution-batch manifest execution job",
            )
        )
    if writer.get("observed-matrix") != expected_matrix:
        issues.append(
            ValidationIssue(
                "$.writer.observed-matrix",
                "must match execution-batch matrix identity",
            )
        )


def _validate_execution_tree(
    value: object,
    validation_tree: object,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(value, Mapping):
        return
    observed = value.get("observed-commit-sha")
    if observed is not None and (
        not isinstance(observed, str) or _SHA_RE.fullmatch(observed) is None
    ):
        issues.append(
            ValidationIssue(
                "$.execution-tree.observed-commit-sha",
                "must be null or SHA-1",
            ),
        )
    if value.get("source") != "execution-batch-boundary":
        issues.append(
            ValidationIssue(
                "$.execution-tree.source",
                "must be execution-batch-boundary",
            ),
        )
    if value.get("verified") is not True:
        issues.append(
            ValidationIssue("$.execution-tree.verified", "must be true")
        )
    if isinstance(validation_tree, Mapping):
        expected = validation_tree.get("commit-sha")
        if isinstance(expected, str) and observed != expected:
            issues.append(
                ValidationIssue(
                    "$.execution-tree.observed-commit-sha",
                    "must match validation tree commit",
                )
            )


def _validate_selector_results(  # noqa: PLR0913
    value: object,
    batch: Mapping[str, object] | None,
    execution_batch_manifest: Mapping[str, object] | None,
    bundle: Mapping[str, object],
    plan: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        issues.append(ValidationIssue("$.selector-results", "must be an array"))
        return
    ordered = []
    ordered_selectors = (
        batch.get("ordered-selectors") if batch is not None else None
    )
    if isinstance(ordered_selectors, Sequence) and not isinstance(
        ordered_selectors,
        str | bytes,
    ):
        ordered = [
            item for item in ordered_selectors if isinstance(item, Mapping)
        ]
    if ordered and len(value) != len(ordered):
        issues.append(
            ValidationIssue(
                "$.selector-results",
                "must contain one row per ordered selector",
            ),
        )
    for index, result in enumerate(value):
        path = f"$.selector-results[{index}]"
        if not isinstance(result, Mapping):
            issues.append(ValidationIssue(path, "must be an object"))
            continue
        _validate_selector_result(result, path, issues, plan, fact_snapshot)
        if index < len(ordered):
            expected = ordered[index]
            _validate_selector_result_matches_slot(
                result,
                expected,
                execution_batch_manifest,
                bundle,
                path,
                issues,
            )


def _validate_selector_result(
    result: Mapping[str, object],
    path: str,
    issues: list[ValidationIssue],
    plan: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
) -> None:
    _validate_root_keys(result, _SELECTOR_RESULT_KEYS, path, issues)
    _validate_local_id(
        result.get("work-group-id"), f"{path}.work-group-id", issues
    )
    if not isinstance(result.get("selector-index"), int) or isinstance(
        result.get("selector-index"),
        bool,
    ):
        issues.append(
            ValidationIssue(f"{path}.selector-index", "must be an integer")
        )
    _validate_local_id(
        result.get("expected-evidence-id"),
        f"{path}.expected-evidence-id",
        issues,
    )
    _validate_digest(
        result.get("expected-evidence-slot-digest"),
        f"{path}.expected-evidence-slot-digest",
        issues,
    )
    if result.get("mode") not in _MODES:
        issues.append(ValidationIssue(f"{path}.mode", "is not registered"))
    _validate_validation_tree(
        result.get("validation-tree"),
        f"{path}.validation-tree",
        allow_unknown=False,
        issues=issues,
    )
    _validate_affected_range(result.get("affected-range"), issues, path)
    _validate_scheduled_full(result.get("scheduled-full"), issues, path)
    ecosystem = result.get("ecosystem")
    if ecosystem is not None and ecosystem not in _ECOSYSTEMS:
        issues.append(ValidationIssue(f"{path}.ecosystem", "is not registered"))
    if result.get("runner-family") not in _RUNNER_FAMILIES:
        issues.append(
            ValidationIssue(f"{path}.runner-family", "is not registered")
        )
    _validate_id_array(result.get("depends-on"), f"{path}.depends-on", issues)
    if result.get("outcome") not in _OUTCOMES:
        issues.append(ValidationIssue(f"{path}.outcome", "is not registered"))
    if result.get("skip-reason") not in {
        None,
        "dependency-blocked",
        "not-applicable",
    }:
        issues.append(
            ValidationIssue(f"{path}.skip-reason", "is not registered")
        )
    _validate_diagnostics(
        result.get("diagnostics"), f"{path}.diagnostics", issues
    )
    evidence_outcome = _validate_evidence(
        result.get("evidence"),
        f"{path}.evidence",
        issues,
        selector_result=result,
        plan=plan,
        fact_snapshot=fact_snapshot,
    )
    if (
        evidence_outcome is not None
        and result.get("outcome") != evidence_outcome
    ):
        issues.append(
            ValidationIssue(f"{path}.outcome", "must match evidence outcome")
        )
    if result.get("proof-admissibility") != _PROOF_ADMISSIBILITY:
        issues.append(
            ValidationIssue(
                f"{path}.proof-admissibility",
                "must be validation-only",
            ),
        )


def _validate_selector_result_matches_slot(  # noqa: C901, PLR0913
    result: Mapping[str, object],
    expected: Mapping[str, object],
    execution_batch_manifest: Mapping[str, object] | None,
    bundle: Mapping[str, object],
    path: str,
    issues: list[ValidationIssue],
) -> None:
    for key in (
        "work-group-id",
        "selector-index",
        "expected-evidence-id",
        "depends-on",
    ):
        if result.get(key) != expected.get(key):
            issues.append(
                ValidationIssue(f"{path}.{key}", "must match manifest")
            )
    slot = expected.get("expected-evidence-slot")
    if isinstance(slot, Mapping):
        digest = canonical_json_digest(slot)
        if result.get("expected-evidence-slot-digest") != digest:
            issues.append(
                ValidationIssue(
                    f"{path}.expected-evidence-slot-digest",
                    "must match manifest slot",
                ),
            )
        for key in (
            "coverage-target",
            "ecosystem",
            "runner-family",
            "selector-variant",
        ):
            if result.get(key) != slot.get(key):
                issues.append(
                    ValidationIssue(f"{path}.{key}", "must match manifest slot")
                )
        evidence = result.get("evidence")
        expected_evidence = slot.get("evidence")
        if isinstance(evidence, Mapping) and isinstance(
            expected_evidence, Mapping
        ):
            for key in ("category", "planned-capabilities"):
                if evidence.get(key) != expected_evidence.get(key):
                    issues.append(
                        ValidationIssue(
                            f"{path}.evidence.{key}",
                            "must match manifest slot",
                        )
                    )
    for key in ("mode", "validation-tree", "affected-range", "scheduled-full"):
        if result.get(key) != bundle.get(key):
            issues.append(ValidationIssue(f"{path}.{key}", "must match bundle"))
    _validate_dependency_results(
        result,
        expected,
        execution_batch_manifest,
        path,
        issues,
    )


def _validate_authorizing_batch_dependency_evidence(  # noqa: C901, PLR0912, PLR0913
    bundle: Mapping[str, object],
    batch: Mapping[str, object] | None,
    execution_batch_manifest: Mapping[str, object] | None,
    dependency_evidence_bundles: Sequence[Mapping[str, object]],
    *,
    plan: Mapping[str, object] | None,
    request: Mapping[str, object] | None,
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    expected_run_id: str | None,
    expected_run_attempt: str | None,
    issues: list[ValidationIssue],
) -> None:
    selector_results = bundle.get("selector-results")
    if not isinstance(selector_results, Sequence) or isinstance(
        selector_results, str | bytes
    ):
        return
    if batch is None or execution_batch_manifest is None:
        _reject_dependency_results_without_manifest(selector_results, issues)
        return
    current_batch_id = batch.get("batch-id")
    if not isinstance(current_batch_id, str):
        return
    authoritative = _authoritative_dependency_evidence_lookup(
        dependency_evidence_bundles,
        plan=plan,
        request=request,
        execution_batch_manifest=execution_batch_manifest,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        expected_run_id=expected_run_id,
        expected_run_attempt=expected_run_attempt,
        issues=issues,
    )
    same_batch_authoritative: dict[str, Mapping[str, object]] = {}
    for selector_index, selector in enumerate(selector_results):
        if not isinstance(selector, Mapping):
            continue
        dependencies = selector.get("dependency-results")
        if not isinstance(dependencies, Sequence) or isinstance(
            dependencies, str | bytes
        ):
            continue
        for dependency_index, dependency in enumerate(dependencies):
            if not isinstance(dependency, Mapping):
                continue
            source_batch_id = dependency.get("source-batch-id")
            item_path = (
                f"$.selector-results[{selector_index}]"
                f".dependency-results[{dependency_index}]"
            )
            work_group_id = dependency.get("work-group-id")
            actual = None
            if isinstance(work_group_id, str):
                actual = (
                    same_batch_authoritative.get(work_group_id)
                    if source_batch_id == current_batch_id
                    else authoritative.get(work_group_id)
                )
            if actual is None:
                if _is_unresolved_dependency_result(
                    dependency,
                    execution_batch_manifest,
                ):
                    continue
                issues.append(
                    ValidationIssue(
                        item_path,
                        "requires authoritative upstream bundle evidence",
                    )
                )
                continue
            for key in (
                "outcome",
                "admitted-for-gating",
                "source-batch-id",
                "upstream-artifact-ref",
                "upstream-bundle-id",
                "upstream-artifact-instance-id",
                "upstream-admitted-candidate-id",
            ):
                if dependency.get(key) != actual.get(key):
                    issues.append(
                        ValidationIssue(
                            f"{item_path}.{key}",
                            "must match authoritative upstream bundle evidence",
                        )
                    )
        work_group_id = selector.get("work-group-id")
        if isinstance(work_group_id, str):
            outcome = _selector_outcome_to_summary_outcome(
                selector.get("outcome")
            )
            same_batch_authoritative[work_group_id] = {
                "work-group-id": work_group_id,
                "source-batch-id": current_batch_id,
                "upstream-artifact-ref": None,
                "upstream-bundle-id": None,
                "upstream-artifact-instance-id": None,
                "upstream-admitted-candidate-id": None,
                "outcome": outcome,
                "admitted-for-gating": _selector_outcome_admitted_for_gating(
                    selector.get("outcome")
                ),
            }


def _is_unresolved_dependency_result(
    dependency: Mapping[str, object],
    execution_batch_manifest: Mapping[str, object] | None,
) -> bool:
    work_group_id = dependency.get("work-group-id")
    source_batch_id = dependency.get("source-batch-id")
    if not isinstance(work_group_id, str) or not isinstance(
        source_batch_id, str
    ):
        return False
    if (
        _selector_batch_positions(execution_batch_manifest).get(work_group_id)
        != source_batch_id
    ):
        return False
    if dependency.get("outcome") not in {"missing", "skipped"}:
        return False
    if dependency.get("admitted-for-gating") is not False:
        return False
    return not any(
        dependency.get(key) is not None
        for key in (
            "upstream-artifact-ref",
            "upstream-bundle-id",
            "upstream-artifact-instance-id",
            "upstream-admitted-candidate-id",
        )
    )


def _reject_dependency_results_without_manifest(
    selector_results: Sequence[object],
    issues: list[ValidationIssue],
) -> None:
    for selector_index, selector in enumerate(selector_results):
        if not isinstance(selector, Mapping):
            continue
        dependencies = selector.get("dependency-results")
        if not isinstance(dependencies, Sequence) or isinstance(
            dependencies, str | bytes
        ):
            continue
        for dependency_index, dependency in enumerate(dependencies):
            if isinstance(dependency, Mapping):
                issues.append(
                    ValidationIssue(
                        "$.selector-results"
                        f"[{selector_index}].dependency-results"
                        f"[{dependency_index}]",
                        "requires authoritative execution-batch manifest",
                    )
                )


def _authoritative_dependency_evidence_lookup(  # noqa: C901, PLR0912, PLR0913
    dependency_evidence_bundles: Sequence[Mapping[str, object]],
    *,
    plan: Mapping[str, object] | None,
    request: Mapping[str, object] | None,
    execution_batch_manifest: Mapping[str, object],
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    expected_run_id: str | None,
    expected_run_attempt: str | None,
    issues: list[ValidationIssue],
) -> dict[str, Mapping[str, object]]:
    authoritative: dict[str, Mapping[str, object]] = {}
    validated_dependency_bundles: list[Mapping[str, object]] = []
    pending = list(enumerate(dependency_evidence_bundles))
    last_errors: dict[int, ContractValidationError] = {}
    while pending:
        next_pending: list[tuple[int, Mapping[str, object]]] = []
        progressed = False
        for index, dependency_bundle in pending:
            try:
                validate_ci_validation_batch_evidence_bundle(
                    dependency_bundle,
                    plan=plan,
                    request=request,
                    execution_batch_manifest=execution_batch_manifest,
                    changed_files_snapshot=changed_files_snapshot,
                    fact_snapshot=fact_snapshot,
                    expected_run_id=expected_run_id,
                    expected_run_attempt=expected_run_attempt,
                    dependency_evidence_bundles=validated_dependency_bundles,
                )
            except ContractValidationError as error:
                last_errors[index] = error
                next_pending.append((index, dependency_bundle))
                continue
            validated_dependency_bundles.append(dependency_bundle)
            progressed = True
        if not next_pending:
            break
        if not progressed:
            for index, _dependency_bundle in next_pending:
                error = last_errors[index]
                path_prefix = f"dependency_evidence_bundles[{index}]"
                issues.extend(
                    ValidationIssue(
                        _prefixed_validation_issue_path(
                            path_prefix,
                            issue.path,
                        ),
                        issue.message,
                    )
                    for issue in error.issues
                )
            break
        pending = next_pending
    for dependency_bundle in validated_dependency_bundles:
        batch_value = dependency_bundle.get("batch")
        batch_id = (
            batch_value.get("batch-id")
            if isinstance(batch_value, Mapping)
            else None
        )
        if not isinstance(batch_id, str):
            continue
        selector_results = dependency_bundle.get("selector-results")
        if not isinstance(selector_results, Sequence) or isinstance(
            selector_results, str | bytes
        ):
            continue
        for selector in selector_results:
            if not isinstance(selector, Mapping):
                continue
            work_group_id = selector.get("work-group-id")
            if not isinstance(work_group_id, str):
                continue
            outcome = _selector_outcome_to_summary_outcome(
                selector.get("outcome")
            )
            artifact_ref = dependency_bundle.get("artifact-ref")
            bundle_id = dependency_bundle.get("bundle-id")
            if work_group_id in authoritative:
                issues.append(
                    ValidationIssue(
                        "dependency_evidence_bundles",
                        "duplicate authoritative upstream bundle evidence",
                    )
                )
                continue
            artifact_instance_id = getattr(
                dependency_bundle,
                "artifact_instance_id",
                None,
            )
            admitted_candidate_id = getattr(
                dependency_bundle,
                "admitted_candidate_id",
                None,
            )
            if not isinstance(artifact_instance_id, str) or not isinstance(
                admitted_candidate_id, str
            ):
                issues.append(
                    ValidationIssue(
                        "dependency_evidence_bundles",
                        "requires trusted upstream artifact identity metadata",
                    )
                )
            authoritative[work_group_id] = {
                "work-group-id": work_group_id,
                "source-batch-id": batch_id,
                "upstream-artifact-ref": artifact_ref
                if isinstance(artifact_ref, str)
                else None,
                "upstream-bundle-id": bundle_id
                if isinstance(bundle_id, str)
                else None,
                "upstream-artifact-instance-id": artifact_instance_id
                if isinstance(artifact_instance_id, str)
                else None,
                "upstream-admitted-candidate-id": admitted_candidate_id
                if isinstance(admitted_candidate_id, str)
                else None,
                "outcome": outcome,
                "admitted-for-gating": _selector_outcome_admitted_for_gating(
                    selector.get("outcome")
                ),
            }
    return authoritative


def _validate_dependency_results(  # noqa: C901,PLR0912,PLR0915
    result: Mapping[str, object],
    expected: Mapping[str, object],
    execution_batch_manifest: Mapping[str, object] | None,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    depends_on = [
        item
        for item in _sequence(expected.get("depends-on", []))
        if isinstance(item, str)
    ]
    value = result.get("dependency-results")
    dep_path = f"{path}.dependency-results"
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        issues.append(ValidationIssue(dep_path, "must be an array"))
        return
    rows_by_work_group: dict[str, Mapping[str, object]] = {}
    positions = _selector_batch_positions(execution_batch_manifest)
    blocked = False
    for index, item in enumerate(value):
        item_path = f"{dep_path}[{index}]"
        if not isinstance(item, Mapping):
            issues.append(ValidationIssue(item_path, "must be an object"))
            continue
        _validate_root_keys_with_optional(
            item,
            _DEPENDENCY_RESULT_KEYS,
            frozenset(
                {
                    "upstream-artifact-ref",
                    "upstream-bundle-id",
                    "upstream-artifact-instance-id",
                    "upstream-admitted-candidate-id",
                }
            ),
            item_path,
            issues,
        )
        work_group_id = item.get("work-group-id")
        _validate_local_id(work_group_id, f"{item_path}.work-group-id", issues)
        if isinstance(work_group_id, str):
            if work_group_id in rows_by_work_group:
                issues.append(ValidationIssue(dep_path, "must be unique"))
            rows_by_work_group[work_group_id] = item
        source_batch_id = item.get("source-batch-id")
        _validate_local_id(
            source_batch_id, f"{item_path}.source-batch-id", issues
        )
        upstream_ref = item.get("upstream-artifact-ref")
        if upstream_ref is not None:
            _validate_artifact_ref(
                upstream_ref, f"{item_path}.upstream-artifact-ref", issues
            )
        upstream_bundle_id = item.get("upstream-bundle-id")
        if upstream_bundle_id is not None:
            _validate_non_empty_string(
                upstream_bundle_id, f"{item_path}.upstream-bundle-id", issues
            )
        upstream_instance_id = item.get("upstream-artifact-instance-id")
        if upstream_instance_id is not None:
            _validate_non_empty_string(
                upstream_instance_id,
                f"{item_path}.upstream-artifact-instance-id",
                issues,
            )
        upstream_candidate_id = item.get("upstream-admitted-candidate-id")
        if upstream_candidate_id is not None:
            _validate_non_empty_string(
                upstream_candidate_id,
                f"{item_path}.upstream-admitted-candidate-id",
                issues,
            )
        if upstream_ref is not None or upstream_bundle_id is not None:
            if upstream_instance_id is None:
                issues.append(
                    ValidationIssue(
                        f"{item_path}.upstream-artifact-instance-id",
                        "is required for upstream artifact evidence",
                    )
                )
            if upstream_candidate_id is None:
                issues.append(
                    ValidationIssue(
                        f"{item_path}.upstream-admitted-candidate-id",
                        "is required for upstream artifact evidence",
                    )
                )
        if isinstance(work_group_id, str) and isinstance(source_batch_id, str):
            expected_batch_id = positions.get(work_group_id)
            if (
                expected_batch_id is not None
                and source_batch_id != expected_batch_id
            ):
                issues.append(
                    ValidationIssue(
                        f"{item_path}.source-batch-id",
                        "must match dependency source batch",
                    )
                )
        outcome = item.get("outcome")
        if outcome not in _RESULT_OUTCOMES:
            issues.append(
                ValidationIssue(f"{item_path}.outcome", "is not registered")
            )
        admitted = item.get("admitted-for-gating")
        if not isinstance(admitted, bool):
            issues.append(
                ValidationIssue(
                    f"{item_path}.admitted-for-gating", "must be a boolean"
                )
            )
        elif outcome in _RESULT_OUTCOMES and admitted != (
            outcome in {"satisfied", "failed"}
        ):
            issues.append(
                ValidationIssue(
                    f"{item_path}.admitted-for-gating",
                    "must match dependency outcome",
                )
            )
        if admitted is not True:
            blocked = True
    observed_ids = set(rows_by_work_group)
    expected_ids = set(depends_on)
    if observed_ids != expected_ids:
        issues.append(
            ValidationIssue(dep_path, "must cover depends-on exactly")
        )
    if blocked:
        if result.get("outcome") != "skipped":
            issues.append(
                ValidationIssue(
                    path + ".outcome", "must be skipped when dependency-blocked"
                )
            )
        if result.get("skip-reason") != "dependency-blocked":
            issues.append(
                ValidationIssue(
                    path + ".skip-reason", "must be dependency-blocked"
                )
            )
        if not _has_dependency_blocked_diagnostic(result.get("diagnostics")):
            issues.append(
                ValidationIssue(
                    path + ".diagnostics",
                    "must include a dependency-blocked diagnostic",
                )
            )


def _selector_batch_positions(
    execution_batch_manifest: Mapping[str, object] | None,
) -> dict[str, str]:
    if execution_batch_manifest is None:
        return {}
    batches = execution_batch_manifest.get("batches")
    if not isinstance(batches, Sequence) or isinstance(batches, str | bytes):
        return {}
    positions: dict[str, str] = {}
    for batch in batches:
        if not isinstance(batch, Mapping):
            continue
        batch_id = batch.get("batch-id")
        selectors = batch.get("ordered-selectors")
        if (
            not isinstance(batch_id, str)
            or not isinstance(selectors, Sequence)
            or isinstance(selectors, str | bytes)
        ):
            continue
        for selector in selectors:
            if not isinstance(selector, Mapping):
                continue
            work_group_id = selector.get("work-group-id")
            if isinstance(work_group_id, str):
                positions[work_group_id] = batch_id
    return positions


def _has_dependency_blocked_diagnostic(value: object) -> bool:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        return False
    return any(
        isinstance(item, Mapping)
        and item.get("detail") == DiagnosticDetail.DEPENDENCY_BLOCKED.value
        for item in value
    )


def _validate_evidence(  # noqa: C901,PLR0912,PLR0913
    value: object,
    path: str,
    issues: list[ValidationIssue],
    *,
    selector_result: Mapping[str, object] | None = None,
    plan: Mapping[str, object] | None = None,
    fact_snapshot: Mapping[str, object] | None = None,
) -> str | None:
    if not isinstance(value, Mapping):
        issues.append(ValidationIssue(path, "must be an object"))
        return None
    allowed = frozenset(
        {
            "category",
            "planned-capabilities",
            "artifact-refs",
            "capability-results",
            "category-result",
        },
    )
    _validate_allowed_keys(value, allowed, path, issues)
    for key in ("category", "planned-capabilities", "artifact-refs"):
        if key not in value:
            issues.append(ValidationIssue(f"{path}.{key}", "is required"))
    category = value.get("category")
    _validate_non_empty_string(category, f"{path}.category", issues)
    planned = value.get("planned-capabilities")
    planned_capabilities: list[str] = []
    capability_branch_expected = isinstance(
        planned, Sequence
    ) and not isinstance(planned, str | bytes)
    if capability_branch_expected:
        seen: set[str] = set()
        for index, item in enumerate(planned):
            _validate_local_id(
                item, f"{path}.planned-capabilities[{index}]", issues
            )
            if isinstance(item, str):
                if item in seen:
                    issues.append(
                        ValidationIssue(
                            f"{path}.planned-capabilities",
                            "must be unique",
                        )
                    )
                seen.add(item)
                planned_capabilities.append(item)
    elif planned is not None:
        issues.append(
            ValidationIssue(
                f"{path}.planned-capabilities",
                "must be null or an array",
            )
        )
    artifact_refs = value.get("artifact-refs")
    top_level_refs: list[str] = []
    if not isinstance(artifact_refs, Sequence) or isinstance(
        artifact_refs, str | bytes
    ):
        issues.append(
            ValidationIssue(f"{path}.artifact-refs", "must be an array")
        )
    else:
        for index, item in enumerate(artifact_refs):
            _validate_artifact_ref(
                item, f"{path}.artifact-refs[{index}]", issues
            )
            if isinstance(item, str):
                top_level_refs.append(item)
    has_capability_results = "capability-results" in value
    has_category_result = "category-result" in value
    if capability_branch_expected:
        if not has_capability_results:
            issues.append(
                ValidationIssue(
                    f"{path}.capability-results",
                    "is required for capability evidence",
                )
            )
        if has_category_result:
            issues.append(
                ValidationIssue(
                    f"{path}.category-result",
                    "must not appear with capability-results",
                )
            )
        outcome, nested_refs = _validate_capability_results(
            value.get("capability-results"),
            planned_capabilities,
            f"{path}.capability-results",
            issues,
        )
    else:
        if has_capability_results:
            issues.append(
                ValidationIssue(
                    f"{path}.capability-results",
                    "must not appear with category-result",
                )
            )
        if not has_category_result:
            issues.append(
                ValidationIssue(
                    f"{path}.category-result",
                    "is required for category evidence",
                )
            )
        outcome, nested_refs = _validate_category_result(
            value.get("category-result"),
            category,
            f"{path}.category-result",
            issues,
            selector_result=selector_result,
            plan=plan,
            fact_snapshot=fact_snapshot,
        )
    if sorted(top_level_refs) != sorted(nested_refs):
        issues.append(
            ValidationIssue(
                f"{path}.artifact-refs",
                "must match nested result artifact refs",
            )
        )
    return outcome


def _validate_capability_results(  # noqa: C901
    value: object,
    planned_capabilities: Sequence[str],
    path: str,
    issues: list[ValidationIssue],
) -> tuple[str | None, list[str]]:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        issues.append(ValidationIssue(path, "must be an array"))
        return None, []
    refs: list[str] = []
    outcomes: list[str] = []
    seen: set[str] = set()
    for index, item in enumerate(value):
        item_path = f"{path}[{index}]"
        if not isinstance(item, Mapping):
            issues.append(ValidationIssue(item_path, "must be an object"))
            continue
        _validate_allowed_keys(
            item,
            frozenset(
                {"capability", "outcome", "diagnostics", "artifact-refs"}
            ),
            item_path,
            issues,
        )
        for key in ("capability", "outcome", "diagnostics"):
            if key not in item:
                issues.append(
                    ValidationIssue(f"{item_path}.{key}", "is required")
                )
        capability = item.get("capability")
        _validate_local_id(capability, f"{item_path}.capability", issues)
        if isinstance(capability, str):
            if capability in seen:
                issues.append(
                    ValidationIssue(path, "capabilities must be unique")
                )
            seen.add(capability)
        outcome = item.get("outcome")
        if outcome not in _OUTCOMES:
            issues.append(
                ValidationIssue(f"{item_path}.outcome", "is not registered")
            )
        elif isinstance(outcome, str):
            outcomes.append(outcome)
        _validate_diagnostics(
            item.get("diagnostics"), f"{item_path}.diagnostics", issues
        )
        refs.extend(
            _validate_optional_artifact_refs(
                item.get("artifact-refs"),
                f"{item_path}.artifact-refs",
                issues,
            )
        )
    observed_capabilities = [
        str(item.get("capability"))
        for item in value
        if isinstance(item, Mapping) and isinstance(item.get("capability"), str)
    ]
    if list(planned_capabilities) != observed_capabilities:
        issues.append(
            ValidationIssue(
                path,
                "must cover planned capabilities exactly in order",
            )
        )
    return _derive_selector_outcome(outcomes), refs


def _validate_category_result(  # noqa: PLR0913
    value: object,
    expected_category: object,
    path: str,
    issues: list[ValidationIssue],
    *,
    selector_result: Mapping[str, object] | None = None,
    plan: Mapping[str, object] | None = None,
    fact_snapshot: Mapping[str, object] | None = None,
) -> tuple[str | None, list[str]]:
    if not isinstance(value, Mapping):
        issues.append(ValidationIssue(path, "must be an object"))
        return None, []
    _validate_allowed_keys(
        value,
        frozenset(
            {"category", "outcome", "diagnostics", "artifact-refs", "detail"}
        ),
        path,
        issues,
    )
    for key in ("category", "outcome", "diagnostics"):
        if key not in value:
            issues.append(ValidationIssue(f"{path}.{key}", "is required"))
    if value.get("category") != expected_category:
        issues.append(
            ValidationIssue(f"{path}.category", "must match evidence category")
        )
    outcome = value.get("outcome")
    if outcome not in _OUTCOMES:
        issues.append(ValidationIssue(f"{path}.outcome", "is not registered"))
        derived = None
    else:
        derived = str(outcome)
    _validate_diagnostics(
        value.get("diagnostics"), f"{path}.diagnostics", issues
    )
    if (
        expected_category == "release-shaped-artifact"
        and selector_result is not None
        and value.get("diagnostics") != selector_result.get("diagnostics")
    ):
        issues.append(
            ValidationIssue(f"{path}.diagnostics", "must match selector")
        )
    refs = _validate_optional_artifact_refs(
        value.get("artifact-refs"),
        f"{path}.artifact-refs",
        issues,
    )
    if expected_category == "release-shaped-artifact":
        _validate_release_shaped_batch_detail(
            value.get("detail"),
            outcome,
            refs,
            f"{path}.detail",
            issues,
            selector_result=selector_result,
            plan=plan,
            fact_snapshot=fact_snapshot,
        )
    elif "detail" in value:
        issues.append(ValidationIssue(f"{path}.detail", "is not allowed"))
    return derived, refs


def _validate_release_shaped_batch_detail(  # noqa: PLR0913
    value: object,
    outcome: object,
    artifact_refs: Sequence[str],
    path: str,
    issues: list[ValidationIssue],
    *,
    selector_result: Mapping[str, object] | None = None,
    plan: Mapping[str, object] | None = None,
    fact_snapshot: Mapping[str, object] | None = None,
) -> None:
    if not isinstance(value, Mapping):
        issues.append(
            ValidationIssue(
                path,
                "is required for release-shaped evidence",
            )
        )
        return
    _validate_allowed_keys(
        value,
        frozenset(
            {
                "artifact-obligation-results",
                "evidence-source",
                "source-proof",
            }
        ),
        path,
        issues,
    )
    source = value.get("evidence-source")
    results = value.get("artifact-obligation-results")
    observed_digests = _validate_release_result_digests(
        results,
        f"{path}.artifact-obligation-results",
        issues,
        outcome=outcome,
        diagnostics=(
            selector_result.get("diagnostics")
            if selector_result is not None
            else None
        ),
        plan=plan,
        fact_snapshot=fact_snapshot,
        selector_result=selector_result,
    )
    if outcome != "success":
        for key in ("evidence-source", "source-proof"):
            if key in value:
                issues.append(
                    ValidationIssue(
                        f"{path}.{key}",
                        "is not allowed for non-success release-shaped "
                        "evidence",
                    )
                )
        return
    if source == "no-publish-validation":
        _validate_no_publish_source_proof(
            value.get("source-proof"),
            artifact_refs,
            f"{path}.source-proof",
            issues,
            selector_result=selector_result,
            observed_digests=observed_digests,
        )
    else:
        issues.append(
            ValidationIssue(f"{path}.evidence-source", "is not registered")
        )


def _validate_release_result_digests(  # noqa: C901,PLR0912,PLR0913,PLR0915
    value: object,
    path: str,
    issues: list[ValidationIssue],
    *,
    outcome: object,
    diagnostics: object,
    plan: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    selector_result: Mapping[str, object] | None,
) -> dict[str, str]:
    observed: dict[str, str] = {}
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        issues.append(ValidationIssue(path, "is required"))
        return observed
    if not value:
        issues.append(ValidationIssue(path, "must not be empty"))
    plan_obligations = _release_shaped_obligations_for_selector(
        plan,
        selector_result,
    )
    if plan_obligations is not None:
        result_ids = [
            item.get("artifact-obligation-id")
            for item in value
            if isinstance(item, Mapping)
        ]
        expected_ids = [
            item.get("artifact-obligation-id") for item in plan_obligations
        ]
        if result_ids != expected_ids:
            issues.append(
                ValidationIssue(path, "must cover plan obligations exactly")
            )
    obligations_by_id = {
        str(item["artifact-obligation-id"]): item
        for item in plan_obligations or []
        if isinstance(item.get("artifact-obligation-id"), str)
    }
    for result_index, result in enumerate(value):
        result_path = f"{path}[{result_index}]"
        if not isinstance(result, Mapping):
            issues.append(ValidationIssue(result_path, "must be an object"))
            continue
        obligation = obligations_by_id.get(
            str(result.get("artifact-obligation-id"))
        )
        _validate_release_shaped_obligation_result(
            result,
            result_path,
            issues,
            outcome=outcome,
            diagnostics=diagnostics,
            obligation=obligation,
            fact_snapshot=fact_snapshot,
        )
        artifact = result.get("artifact")
        if not isinstance(artifact, Mapping):
            issues.append(
                ValidationIssue(f"{result_path}.artifact", "must be an object")
            )
            continue
        artifact_observed = artifact.get("observed")
        if not isinstance(artifact_observed, Mapping):
            issues.append(
                ValidationIssue(
                    f"{result_path}.artifact.observed", "must be an object"
                )
            )
            continue
        digests = artifact_observed.get("digests")
        if not isinstance(digests, Sequence) or isinstance(
            digests, str | bytes
        ):
            issues.append(
                ValidationIssue(
                    f"{result_path}.artifact.observed.digests",
                    "must be an array",
                )
            )
            continue
        for digest_index, item in enumerate(digests):
            item_path = (
                f"{result_path}.artifact.observed.digests[{digest_index}]"
            )
            if not isinstance(item, Mapping):
                issues.append(ValidationIssue(item_path, "must be an object"))
                continue
            artifact_ref = item.get("artifact-ref")
            digest = item.get("digest")
            if not isinstance(artifact_ref, str):
                issues.append(
                    ValidationIssue(
                        f"{item_path}.artifact-ref",
                        "must be a string",
                    )
                )
                continue
            digest_available = item.get("digest-available")
            digest_is_valid = isinstance(digest, str) and (
                _DIGEST_RE.fullmatch(digest) is not None
            )
            if item.get("algorithm") != "sha256":
                issues.append(
                    ValidationIssue(f"{item_path}.algorithm", "must be sha256")
                )
            if outcome == "success" and digest_available is not True:
                issues.append(
                    ValidationIssue(
                        f"{item_path}.digest-available", "must be true"
                    )
                )
            if not digest_is_valid and (
                outcome == "success" or digest_available is True
            ):
                issues.append(
                    ValidationIssue(
                        f"{item_path}.digest",
                        "must be a digest",
                    )
                )
            if (
                item.get("algorithm") == "sha256"
                and digest_available is True
                and digest_is_valid
            ):
                if artifact_ref in observed:
                    issues.append(
                        ValidationIssue(
                            f"{result_path}.artifact.observed.digests",
                            "must be unique",
                        )
                    )
                observed[artifact_ref] = digest
            if (
                outcome != "success"
                and digest_available is False
                and item.get("diagnostics") != diagnostics
            ):
                issues.append(
                    ValidationIssue(
                        f"{item_path}.diagnostics",
                        "must match selector",
                    )
                )
        if obligation is not None and outcome == "success":
            expected_refs = set(_artifact_expected_refs(obligation))
            result_refs = {
                str(item.get("artifact-ref"))
                for item in digests
                if isinstance(item, Mapping)
                and isinstance(item.get("artifact-ref"), str)
            }
            if result_refs != expected_refs:
                issues.append(
                    ValidationIssue(
                        f"{result_path}.artifact.observed.digests",
                        "must cover expected artifact refs exactly",
                    )
                )
    return observed


def _release_shaped_obligations_for_selector(
    plan: Mapping[str, object] | None,
    selector_result: Mapping[str, object] | None,
) -> list[Mapping[str, object]] | None:
    if plan is None or selector_result is None:
        return None
    work_group_id = selector_result.get("work-group-id")
    if not isinstance(work_group_id, str):
        return None
    obligations = [
        item
        for item in _sequence(plan.get("artifact-obligations", []))
        if isinstance(item, Mapping)
        and item.get("work-group-id") == work_group_id
    ]
    return obligations if obligations else None


def _validate_release_shaped_obligation_result(  # noqa: C901, PLR0912, PLR0913
    result: Mapping[str, object],
    path: str,
    issues: list[ValidationIssue],
    *,
    outcome: object,
    diagnostics: object,
    obligation: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
) -> None:
    if result.get("outcome") != outcome:
        issues.append(ValidationIssue(f"{path}.outcome", "must match selector"))
    if result.get("diagnostics") != diagnostics:
        issues.append(
            ValidationIssue(f"{path}.diagnostics", "must match selector")
        )
    if obligation is None:
        return
    descriptor = result.get("descriptor")
    if not isinstance(descriptor, Mapping):
        issues.append(
            ValidationIssue(f"{path}.descriptor", "must be an object")
        )
    else:
        descriptor_path = str(obligation.get("descriptor-path"))
        if descriptor.get("path") != descriptor_path:
            issues.append(
                ValidationIssue(f"{path}.descriptor.path", "must match plan")
            )
        fact = _descriptor_fact(fact_snapshot, descriptor_path)
        expected_identity = (
            fact.get("descriptor-identity") if fact is not None else None
        )
        if not isinstance(expected_identity, str) or not expected_identity:
            issues.append(
                ValidationIssue(
                    f"{path}.descriptor.identity",
                    "must have a non-empty descriptor fact identity",
                )
            )
        if descriptor.get("identity") != expected_identity:
            issues.append(
                ValidationIssue(
                    f"{path}.descriptor.identity",
                    "must match fact snapshot",
                )
            )
    if result.get("profile-coverage") != obligation.get("profile-coverage"):
        issues.append(
            ValidationIssue(f"{path}.profile-coverage", "must match plan")
        )
    artifact = result.get("artifact")
    release_receipt = result.get("release-receipt")
    if not isinstance(artifact, Mapping):
        issues.append(ValidationIssue(f"{path}.artifact", "must be an object"))
        return
    if not isinstance(release_receipt, Mapping):
        issues.append(
            ValidationIssue(f"{path}.release-receipt", "must be an object")
        )
        return
    if artifact.get("planned") != obligation.get("artifact"):
        issues.append(
            ValidationIssue(f"{path}.artifact.planned", "must match plan")
        )
    if release_receipt.get("planned") != obligation.get("release-receipt"):
        issues.append(
            ValidationIssue(
                f"{path}.release-receipt.planned",
                "must match plan",
            )
        )
    for branch_name, branch in (
        ("artifact", artifact),
        ("release-receipt", release_receipt),
    ):
        if branch.get("outcome") != outcome:
            issues.append(
                ValidationIssue(
                    f"{path}.{branch_name}.outcome",
                    "must match selector",
                )
            )
        if branch.get("diagnostics") != diagnostics:
            issues.append(
                ValidationIssue(
                    f"{path}.{branch_name}.diagnostics",
                    "must match selector",
                )
            )
    if release_receipt.get("expected") is not True:
        issues.append(
            ValidationIssue(f"{path}.release-receipt.expected", "must be true")
        )
    expected_schema_checked = outcome == "success"
    if release_receipt.get("schema-checked") is not expected_schema_checked:
        issues.append(
            ValidationIssue(
                f"{path}.release-receipt.schema-checked",
                "must match outcome",
            )
        )
    _validate_release_shaped_observed_artifact(
        artifact,
        path,
        issues,
        outcome=outcome,
        obligation=obligation,
    )


def _validate_release_shaped_observed_artifact(
    artifact: Mapping[str, object],
    path: str,
    issues: list[ValidationIssue],
    *,
    outcome: object,
    obligation: Mapping[str, object],
) -> None:
    observed = artifact.get("observed")
    if not isinstance(observed, Mapping):
        issues.append(
            ValidationIssue(f"{path}.artifact.observed", "must be an object")
        )
        return
    expected_refs = _artifact_expected_refs(obligation)
    refs = observed.get("refs")
    if not isinstance(refs, Sequence) or isinstance(refs, str | bytes):
        issues.append(
            ValidationIssue(
                f"{path}.artifact.observed.refs",
                "must be an array",
            )
        )
        return
    observed_refs = [item for item in refs if isinstance(item, str)]
    if len(observed_refs) != len(refs):
        issues.append(
            ValidationIssue(
                f"{path}.artifact.observed.refs",
                "must contain strings",
            )
        )
    if outcome == "success" and observed_refs != expected_refs:
        issues.append(
            ValidationIssue(
                f"{path}.artifact.observed.refs",
                "must match expected artifact refs",
            )
        )
    if outcome != "success" and observed_refs not in ([], expected_refs):
        issues.append(
            ValidationIssue(
                f"{path}.artifact.observed.refs",
                "must be empty or expected artifact refs",
            )
        )


def _artifact_expected_refs(obligation: Mapping[str, object]) -> list[str]:
    artifact = obligation.get("artifact")
    if not isinstance(artifact, Mapping):
        return []
    refs = artifact.get("expected-artifact-refs")
    if not isinstance(refs, Sequence) or isinstance(refs, str | bytes):
        return []
    return [item for item in refs if isinstance(item, str)]


def _descriptor_fact(
    fact_snapshot: Mapping[str, object] | None,
    descriptor_path: str,
) -> Mapping[str, object] | None:
    if fact_snapshot is None:
        return None
    providers = fact_snapshot.get("providers")
    if not isinstance(providers, Sequence) or isinstance(
        providers, str | bytes
    ):
        return None
    for provider in providers:
        if not isinstance(provider, Mapping):
            continue
        descriptors = provider.get("descriptors")
        if not isinstance(descriptors, Sequence) or isinstance(
            descriptors, str | bytes
        ):
            continue
        for descriptor in descriptors:
            if (
                isinstance(descriptor, Mapping)
                and descriptor.get("descriptor-path") == descriptor_path
            ):
                return descriptor
    return None


def _validate_no_publish_source_proof(  # noqa: C901, PLR0912, PLR0913
    value: object,
    artifact_refs: Sequence[str],
    path: str,
    issues: list[ValidationIssue],
    *,
    selector_result: Mapping[str, object] | None = None,
    observed_digests: Mapping[str, str] | None = None,
) -> None:
    if not isinstance(value, Mapping):
        issues.append(ValidationIssue(path, "must be an object"))
        return
    _validate_allowed_keys(
        value,
        frozenset(
            {
                "kind",
                "work-group-id",
                "coverage-target",
                "observed-commit-sha",
                "artifact-digests",
            }
        ),
        path,
        issues,
    )
    if value.get("kind") != "no-publish-validation-result":
        issues.append(ValidationIssue(f"{path}.kind", "is not registered"))
    if selector_result is not None:
        if value.get("work-group-id") != selector_result.get("work-group-id"):
            issues.append(
                ValidationIssue(f"{path}.work-group-id", "must match selector")
            )
        if value.get("coverage-target") != selector_result.get(
            "coverage-target"
        ):
            issues.append(
                ValidationIssue(
                    f"{path}.coverage-target", "must match selector"
                )
            )
        validation_tree = selector_result.get("validation-tree")
        expected_commit = (
            validation_tree.get("commit-sha")
            if isinstance(validation_tree, Mapping)
            else None
        )
        if value.get("observed-commit-sha") != expected_commit:
            issues.append(
                ValidationIssue(
                    f"{path}.observed-commit-sha",
                    "must match validation tree commit",
                )
            )
    digests = value.get("artifact-digests")
    if not isinstance(digests, Sequence) or isinstance(digests, str | bytes):
        issues.append(
            ValidationIssue(f"{path}.artifact-digests", "must be an array")
        )
        return
    seen: set[str] = set()
    for index, item in enumerate(digests):
        item_path = f"{path}.artifact-digests[{index}]"
        if not isinstance(item, Mapping):
            issues.append(ValidationIssue(item_path, "must be an object"))
            continue
        _validate_allowed_keys(
            item,
            frozenset({"artifact-ref", "algorithm", "digest", "byte-source"}),
            item_path,
            issues,
        )
        artifact_ref = item.get("artifact-ref")
        _validate_artifact_ref(
            artifact_ref,
            f"{item_path}.artifact-ref",
            issues,
        )
        if isinstance(artifact_ref, str):
            if artifact_ref in seen:
                issues.append(
                    ValidationIssue(
                        f"{path}.artifact-digests",
                        "must be unique",
                    )
                )
            seen.add(artifact_ref)
        if item.get("algorithm") != "sha256":
            issues.append(
                ValidationIssue(f"{item_path}.algorithm", "must be sha256")
            )
        _validate_digest(item.get("digest"), f"{item_path}.digest", issues)
        if (
            isinstance(artifact_ref, str)
            and observed_digests is not None
            and item.get("digest") != observed_digests.get(artifact_ref)
        ):
            issues.append(
                ValidationIssue(
                    f"{item_path}.digest",
                    "must match artifact obligation result digest",
                )
            )
        byte_source = item.get("byte-source")
        if not isinstance(byte_source, Mapping):
            issues.append(
                ValidationIssue(
                    f"{item_path}.byte-source",
                    "must be an object",
                )
            )
            continue
        _validate_allowed_keys(
            byte_source,
            frozenset({"kind", "path", "size"}),
            f"{item_path}.byte-source",
            issues,
        )
        if byte_source.get("kind") != "validation-build-output":
            issues.append(
                ValidationIssue(
                    f"{item_path}.byte-source.kind",
                    "is not registered",
                )
            )
        _validate_non_empty_string(
            byte_source.get("path"), f"{item_path}.byte-source.path", issues
        )
        size = byte_source.get("size")
        if not isinstance(size, int) or isinstance(size, bool) or size < 0:
            issues.append(
                ValidationIssue(
                    f"{item_path}.byte-source.size",
                    "must be a non-negative integer",
                )
            )
    if seen != set(artifact_refs):
        issues.append(
            ValidationIssue(
                f"{path}.artifact-digests",
                "must cover release-shaped artifact refs exactly",
            )
        )


def _validate_optional_artifact_refs(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> list[str]:
    if value is None:
        return []
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        issues.append(ValidationIssue(path, "must be an array"))
        return []
    refs: list[str] = []
    for index, item in enumerate(value):
        _validate_artifact_ref(item, f"{path}[{index}]", issues)
        if isinstance(item, str):
            refs.append(item)
    return refs


def _derive_selector_outcome(outcomes: Sequence[str]) -> str | None:
    if not outcomes:
        return None
    if any(outcome == "blocking-failure" for outcome in outcomes):
        return "blocking-failure"
    if all(outcome == "skipped" for outcome in outcomes):
        return "skipped"
    if all(outcome == "success" for outcome in outcomes):
        return "success"
    return "blocking-failure"


def _validate_aggregate_manifest_ref(
    manifest: Mapping[str, object],
    envelope: CommonEnvelope | None,
    issues: list[ValidationIssue],
) -> None:
    _validate_artifact_ref(
        manifest.get("artifact-ref"), "$.artifact-ref", issues
    )
    if envelope is None:
        return
    expected = ci_validation_aggregate_evidence_manifest_artifact_ref(
        run_id=envelope.run_id,
        run_attempt=envelope.run_attempt,
    )
    if manifest.get("artifact-ref") != expected:
        issues.append(ValidationIssue("$.artifact-ref", "must match run"))


def _validate_summary_ref(
    summary: Mapping[str, object],
    envelope: CommonEnvelope | None,
    issues: list[ValidationIssue],
) -> None:
    _validate_artifact_ref(
        summary.get("artifact-ref"), "$.artifact-ref", issues
    )
    if envelope is None:
        return
    expected = ci_validation_aggregate_summary_artifact_ref(
        run_id=envelope.run_id,
        run_attempt=envelope.run_attempt,
    )
    if summary.get("artifact-ref") != expected:
        issues.append(ValidationIssue("$.artifact-ref", "must match run"))


def _validate_plan_nullable_fields(
    document: Mapping[str, object],
    plan: Mapping[str, object] | None,
    envelope: CommonEnvelope | None,
    issues: list[ValidationIssue],
    *,
    allow_retained_invalid_plan_digest: bool = False,
) -> None:
    if plan is None:
        _validate_nullable_non_empty_string(
            document.get("plan-id"),
            "$.plan-id",
            issues,
        )
        _validate_nullable_digest(
            document.get("plan-digest"),
            "$.plan-digest",
            issues,
        )
        return
    plan_envelope = (
        _envelope_or_collect(plan, CiValidationKind.PLAN, issues)
        if allow_retained_invalid_plan_digest
        else _validated_plan_envelope(plan, issues)
    )
    if envelope is not None and plan_envelope is not None:
        _validate_envelope_matches(envelope, plan_envelope, issues)
    if document.get("plan-id") != plan.get("plan-id"):
        issues.append(ValidationIssue("$.plan-id", "must match plan"))
    expected_plan_digest = (
        plan.get("plan-digest")
        if allow_retained_invalid_plan_digest
        else _verified_plan_digest_or_none(plan)
    )
    if document.get("plan-digest") != expected_plan_digest:
        issues.append(ValidationIssue("$.plan-digest", "must match plan"))


def _validated_request_context_digest_or_none(
    request: Mapping[str, object] | None,
    envelope: CommonEnvelope | None,
    issues: list[ValidationIssue],
) -> str | None:
    if request is None or envelope is None:
        return None
    try:
        request_envelope = validate_common_envelope(
            request,
            api_version=API_VERSIONS_BY_KIND[CiValidationKind.REQUEST.value],
            kind=CiValidationKind.REQUEST,
        )
    except ContractValidationError as error:
        issues.extend(
            ValidationIssue(f"request.{issue.path}", issue.message)
            for issue in error.issues
        )
        return None
    if not _validate_context_envelope_matches_current(
        request_envelope,
        envelope,
        "request.$",
        issues,
    ):
        return None
    try:
        normalized = validate_ci_validation_request(
            request,
            expected_run_id=envelope.run_id,
            expected_run_attempt=envelope.run_attempt,
        )
    except ContractValidationError as error:
        issues.extend(
            ValidationIssue(f"request.{issue.path}", issue.message)
            for issue in error.issues
        )
        return None
    return normalized.request_digest


def _validated_changed_files_snapshot_hash_or_none(  # noqa: C901,PLR0911,PLR0912
    changed_files_snapshot: Mapping[str, object] | None,
    envelope: CommonEnvelope | None,
    issues: list[ValidationIssue],
) -> str | None:
    if changed_files_snapshot is None or envelope is None:
        return None
    path = "changed_files_snapshot"
    validation_issue_count = len(issues)
    _validate_root_keys(
        changed_files_snapshot,
        _CHANGED_FILES_SNAPSHOT_KEYS,
        path,
        issues,
    )
    try:
        snapshot_envelope = validate_common_envelope(
            changed_files_snapshot,
            api_version=API_VERSIONS_BY_KIND[
                CiValidationKind.CHANGED_FILES_SNAPSHOT.value
            ],
            kind=CiValidationKind.CHANGED_FILES_SNAPSHOT,
        )
    except ContractValidationError as error:
        issues.extend(
            ValidationIssue(f"{path}.{issue.path}", issue.message)
            for issue in error.issues
        )
        return None
    if snapshot_envelope is not None and not (
        _validate_context_envelope_matches_current(
            snapshot_envelope,
            envelope,
            path,
            issues,
        )
    ):
        return None
    expected_ref = ci_validation_changed_files_snapshot_artifact_ref(
        run_id=envelope.run_id,
        run_attempt=envelope.run_attempt,
    )
    if changed_files_snapshot.get("artifact-ref") != expected_ref:
        issues.append(
            ValidationIssue(
                f"{path}.artifact-ref",
                "must match current run",
            )
        )
    snapshot_hash = changed_files_snapshot.get("changed-files-hash")
    _validate_digest(snapshot_hash, f"{path}.changed-files-hash", issues)
    payload = changed_files_snapshot.get("hash-payload")
    if not isinstance(payload, Mapping):
        issues.append(ValidationIssue(f"{path}.hash-payload", "must be object"))
        return None
    _validate_root_keys(
        payload,
        _CHANGED_FILES_HASH_PAYLOAD_KEYS,
        f"{path}.hash-payload",
        issues,
    )
    payload_api_version = payload.get("api-version")
    expected_payload_api_version = API_VERSIONS_BY_KIND[
        CiValidationKind.CHANGED_FILES_SNAPSHOT.value
    ]
    if payload_api_version != expected_payload_api_version:
        issues.append(
            ValidationIssue(
                f"{path}.hash-payload.api-version",
                "must match changed-files snapshot api-version",
            )
        )
    changed_files = payload.get("changed-files")
    if not isinstance(changed_files, Sequence) or isinstance(
        changed_files,
        str | bytes,
    ):
        issues.append(
            ValidationIssue(
                f"{path}.hash-payload.changed-files",
                "must be string array",
            )
        )
        return None
    for index, item in enumerate(changed_files):
        if not isinstance(item, str):
            issues.append(
                ValidationIssue(
                    f"{path}.hash-payload.changed-files[{index}]",
                    "must be string",
                )
            )
    if not all(isinstance(item, str) for item in changed_files):
        return None
    if len(issues) != validation_issue_count:
        return None
    path_issue_count = len(issues)
    for index, item in enumerate(changed_files):
        _validate_repo_relative_git_path(
            item,
            f"{path}.hash-payload.changed-files[{index}]",
            issues,
        )
    if len(issues) != path_issue_count:
        return None
    try:
        recomputed_hash = ci_validation_changed_files_hash(
            [str(item) for item in changed_files],
        )
    except ContractValidationError as error:
        issues.extend(
            ValidationIssue(f"{path}.hash-payload.{issue.path}", issue.message)
            for issue in error.issues
        )
        return None
    expected_payload = {
        "api-version": expected_payload_api_version,
        "changed-files": sorted({str(item) for item in changed_files}),
    }
    if payload != expected_payload:
        issues.append(
            ValidationIssue(
                f"{path}.hash-payload",
                "must be canonical",
            )
        )
        return None
    if snapshot_hash != recomputed_hash:
        issues.append(
            ValidationIssue(
                f"{path}.changed-files-hash",
                "does not match hash-payload",
            )
        )
        return None
    return recomputed_hash


def _expected_context_plan_id(  # noqa: PLR0913
    plan: Mapping[str, object] | None,
    execution_batch_manifest: Mapping[str, object] | None,
    *,
    changed_files_snapshot: Mapping[str, object] | None,
    changed_files_snapshot_context_hash: str | None,
    changed_files_snapshot_input_proven: bool,
    fact_snapshot: Mapping[str, object] | None,
    envelope: CommonEnvelope | None,
    execution_batch_manifest_proven: bool,
    issues: list[ValidationIssue],
) -> str | None:
    if fact_snapshot is None:
        return None
    if plan is not None:
        if _plan_requires_changed_files_snapshot(plan) and (
            changed_files_snapshot_context_hash is None
            or not changed_files_snapshot_input_proven
        ):
            _validated_context_plan_id_or_none(
                plan,
                changed_files_snapshot=changed_files_snapshot
                if changed_files_snapshot_context_hash is not None
                else None,
                fact_snapshot=None,
                envelope=envelope,
                issues=issues,
            )
            return None
        return _validated_context_plan_id_or_none(
            plan,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            envelope=envelope,
            issues=issues,
        )
    if execution_batch_manifest is not None and execution_batch_manifest_proven:
        plan_id = _valid_context_plan_id_or_none(
            execution_batch_manifest.get("plan-id"),
            "execution_batch_manifest.plan-id",
            issues,
        )
        if (
            changed_files_snapshot_context_hash is None
            or not changed_files_snapshot_input_proven
        ):
            return None
        return plan_id
    return None


def _validated_context_plan_id_or_none(
    plan: Mapping[str, object],
    *,
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    envelope: CommonEnvelope | None,
    issues: list[ValidationIssue],
) -> str | None:
    try:
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            expected_run_id=envelope.run_id if envelope is not None else None,
            expected_run_attempt=(
                envelope.run_attempt if envelope is not None else None
            ),
        )
    except ContractValidationError as error:
        validation_issues: list[ValidationIssue] = []
        blocking_issues: list[ValidationIssue] = []
        for issue in error.issues:
            if _is_companion_presence_validation_issue(
                issue,
            ):
                continue
            validation_issues.append(issue)
            blocking_issues.append(issue)
        issues.extend(validation_issues)
        if blocking_issues:
            return None
    return _valid_context_plan_id_or_none(
        plan.get("plan-id"),
        "plan.plan-id",
        issues,
    )


def _validate_supplied_plan_document_for_aggregate(
    plan: Mapping[str, object] | None,
    envelope: CommonEnvelope | None,
    *,
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> None:
    if plan is None:
        return
    try:
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            expected_run_id=envelope.run_id if envelope is not None else None,
            expected_run_attempt=(
                envelope.run_attempt if envelope is not None else None
            ),
        )
    except ContractValidationError as error:
        issues.extend(
            issue
            for issue in error.issues
            if not _is_plan_companion_presence_validation_issue(issue)
        )


def _is_plan_companion_presence_validation_issue(
    issue: ValidationIssue,
) -> bool:
    messages = {
        "companion is required",
        "must not have a companion when unavailable",
    }
    return issue.path in {"$.changed-files-snapshot", "$.fact-snapshot"} and (
        issue.message in messages
    )


def _is_companion_presence_validation_issue(
    issue: ValidationIssue,
) -> bool:
    companion_messages = {
        "companion is required",
        "must not have a companion when unavailable",
    }
    if (
        issue.path == "$.changed-files-snapshot"
        and issue.message == "companion is required"
    ):
        return True
    return (
        issue.path == "$.changed-files-snapshot"
        and issue.message in companion_messages
    ) or (
        issue.path == "$.fact-snapshot" and issue.message in companion_messages
    )


def _valid_context_plan_id_or_none(
    plan_id: object,
    path: str,
    issues: list[ValidationIssue],
) -> str | None:
    issue_count = len(issues)
    _validate_plan_id_value(plan_id, path, issues)
    if len(issues) != issue_count:
        return None
    return plan_id if isinstance(plan_id, str) else None


def _validated_fact_snapshot_id_or_none(  # noqa: C901,PLR0911,PLR0912
    fact_snapshot: Mapping[str, object] | None,
    envelope: CommonEnvelope | None,
    issues: list[ValidationIssue],
    *,
    expected_plan_id: str | None = None,
) -> str | None:
    if fact_snapshot is None or envelope is None:
        return None
    path = "fact_snapshot"
    _validate_root_keys(fact_snapshot, _FACT_SNAPSHOT_KEYS, path, issues)
    try:
        snapshot_envelope = validate_common_envelope(
            fact_snapshot,
            api_version=API_VERSIONS_BY_KIND[
                CiValidationKind.FACT_SNAPSHOT.value
            ],
            kind=CiValidationKind.FACT_SNAPSHOT,
        )
    except ContractValidationError as error:
        issues.extend(
            ValidationIssue(f"{path}.{issue.path}", issue.message)
            for issue in error.issues
        )
        return None
    if snapshot_envelope is not None and not (
        _validate_context_envelope_matches_current(
            snapshot_envelope,
            envelope,
            path,
            issues,
        )
    ):
        return None
    expected_ref = ci_validation_fact_snapshot_artifact_ref(
        run_id=envelope.run_id,
        run_attempt=envelope.run_attempt,
    )
    if fact_snapshot.get("artifact-ref") != expected_ref:
        issues.append(
            ValidationIssue(
                f"{path}.artifact-ref",
                "must match current run",
            )
        )
        return None
    snapshot_id = fact_snapshot.get("fact-snapshot-id")
    digest_issue_count = len(issues)
    _validate_digest(snapshot_id, f"{path}.fact-snapshot-id", issues)
    if len(issues) != digest_issue_count:
        return None
    providers_value = fact_snapshot.get("providers")
    if not isinstance(providers_value, Sequence) or isinstance(
        providers_value,
        str | bytes,
    ):
        issues.append(ValidationIssue(f"{path}.providers", "must be array"))
        return None
    providers: list[Mapping[str, object]] = []
    for index, provider in enumerate(providers_value):
        if not isinstance(provider, Mapping):
            issues.append(
                ValidationIssue(f"{path}.providers[{index}]", "must be object")
            )
            return None
        providers.append(provider)
    if not providers:
        issues.append(ValidationIssue(f"{path}.providers", "is required"))
        return None
    try:
        frozen_providers = _freeze_fact_snapshot_providers(providers)
        expected_id = ci_validation_fact_snapshot_id(frozen_providers)
    except ContractValidationError as error:
        issues.extend(
            ValidationIssue(f"{path}.{issue.path}", issue.message)
            for issue in error.issues
        )
        return None
    except (TypeError, ValueError) as error:
        issues.append(
            ValidationIssue(
                f"{path}.providers",
                f"cannot canonicalize fact snapshot: {error}",
            )
        )
        return None
    if providers != frozen_providers:
        issues.append(
            ValidationIssue(
                f"{path}.providers",
                "must be canonical",
            )
        )
        return None
    if expected_plan_id is None:
        issues.append(
            ValidationIssue(
                f"{path}.plan-id",
                "requires proven plan identity",
            )
        )
        return None
    if fact_snapshot.get("plan-id") != expected_plan_id:
        issues.append(ValidationIssue(f"{path}.plan-id", "must match plan"))
        return None
    if snapshot_id != expected_id:
        issues.append(
            ValidationIssue(
                f"{path}.fact-snapshot-id",
                "does not match providers",
            )
        )
        return None
    return snapshot_id if isinstance(snapshot_id, str) else None


def _validate_plan_identity_matches(
    document: Mapping[str, object],
    reference: Mapping[str, object],
    path: str,
    reference_name: str,
    issues: list[ValidationIssue],
) -> None:
    for key in ("plan-id", "plan-digest"):
        if document.get(key) != reference.get(key):
            issues.append(
                ValidationIssue(
                    f"{path}.{key}",
                    f"must match {reference_name}",
                )
            )


def _summary_plan_identity_value(
    key: str,
    plan: Mapping[str, object] | None,
    aggregate_evidence_manifest: Mapping[str, object] | None,
) -> object:
    if plan is not None:
        return plan.get(key)
    if aggregate_evidence_manifest is not None:
        if _aggregate_manifest_has_no_authoritative_plan(
            aggregate_evidence_manifest
        ):
            return None
        return aggregate_evidence_manifest.get(key)
    return None


def _summary_projection_from_authority(  # noqa: PLR0913
    *,
    plan: Mapping[str, object] | None,
    aggregate_evidence_manifest: Mapping[str, object] | None,
    request: Mapping[str, object] | None = None,
    changed_files_snapshot: Mapping[str, object] | None = None,
    fact_snapshot: Mapping[str, object] | None = None,
    fallback: Mapping[str, object],
) -> dict[str, object]:
    projection = dict(fallback)
    manifest_envelope = (
        _aggregate_manifest_envelope_or_none(aggregate_evidence_manifest)
        if aggregate_evidence_manifest is not None
        else None
    )
    if (
        plan is not None
        and aggregate_evidence_manifest is not None
        and _supplied_plan_input_authorizes_projection(
            aggregate_evidence_manifest,
            manifest_envelope,
            plan,
            request=request,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
        )
    ):
        projection.update(_summary_projection_from_plan(plan))
        return projection
    if (
        aggregate_evidence_manifest is not None
        and not _aggregate_manifest_has_no_authoritative_plan(
            aggregate_evidence_manifest
        )
    ):
        authority = aggregate_evidence_manifest.get("projection-authority")
        if isinstance(authority, Mapping):
            issues: list[ValidationIssue] = []
            _validated_projection_authority_or_none(
                authority,
                "aggregate_evidence_manifest_document.projection-authority",
                issues,
            )
            if issues:
                raise ContractValidationError(issues)
        projection.update(_no_authority_summary_projection())
        return projection
    projection.update(_no_authority_summary_projection())
    return projection


def _invalid_plan_summary_projection_from_context(
    plan: Mapping[str, object] | None,
    aggregate_evidence_manifest: Mapping[str, object] | None,
    *,
    request: Mapping[str, object] | None = None,
    changed_files_snapshot: Mapping[str, object] | None = None,
    fact_snapshot: Mapping[str, object] | None = None,
) -> dict[str, object]:
    manifest_envelope = (
        _aggregate_manifest_envelope_or_none(aggregate_evidence_manifest)
        if aggregate_evidence_manifest is not None
        else None
    )
    if (
        plan is not None
        and aggregate_evidence_manifest is not None
        and _supplied_plan_input_authorizes_retained_invalid_plan_projection(
            aggregate_evidence_manifest,
            manifest_envelope,
            plan,
            request=request,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
        )
    ):
        return _summary_projection_from_plan(plan)
    return _no_authority_summary_projection()


def _no_authority_summary_projection() -> dict[str, object]:
    return {
        key: dict(value) if isinstance(value, Mapping) else value
        for key, value in _NO_AUTHORITY_SUMMARY_PROJECTION.items()
    }


def _aggregate_manifest_envelope_or_none(
    aggregate_evidence_manifest: Mapping[str, object],
) -> CommonEnvelope | None:
    try:
        return _envelope(
            aggregate_evidence_manifest,
            CiValidationKind.AGGREGATE_EVIDENCE_MANIFEST,
        )
    except ContractValidationError:
        return None


def _summary_projection_from_plan(
    plan: Mapping[str, object],
) -> dict[str, object]:
    try:
        return {
            "mode": plan.get("mode"),
            "validation-tree": dict(_mapping(plan["validation-tree"])),
            "affected-range": _summary_affected_range(plan),
            "request": dict(_mapping(plan["request"])),
            "scheduled-full": dict(_mapping(plan["scheduled-full"])),
        }
    except KeyError as error:
        raise ContractValidationError(
            [ValidationIssue(f"plan.{error.args[0]}", "is required")],
        ) from error


def _projection_authority_from_plan(
    plan: Mapping[str, object],
) -> dict[str, object]:
    authority = _summary_projection_from_plan(plan)
    authority["projection-digest"] = _projection_authority_digest(authority)
    return authority


def _projection_authority_payload(
    authority: Mapping[str, object],
) -> dict[str, object]:
    return {key: authority[key] for key in _PROJECTION_AUTHORITY_PAYLOAD_KEYS}


def _projection_authority_digest(authority: Mapping[str, object]) -> str:
    return canonical_json_digest(_projection_authority_payload(authority))


def _sort_component(value: object) -> str:
    if value is None:
        return ""
    return str(value)


def _sort_digest_component(value: object) -> str:
    try:
        return canonical_json_digest(value)
    except (TypeError, ValueError):
        return ""


def _aggregate_batch_bundle_sort_key(
    item: Mapping[str, object],
) -> tuple[str, str, str, str]:
    return (
        _sort_component(item.get("batch-id")),
        _sort_component(item.get("artifact-ref")),
        _sort_component(item.get("admitted-candidate-id")),
        _sort_digest_component(item),
    )


def _summary_batch_bundle_sort_key(
    item: Mapping[str, object],
) -> tuple[str, str, str, str, str]:
    return (
        _sort_component(item.get("batch-id")),
        _sort_component(item.get("artifact-ref")),
        _sort_component(item.get("bundle-id")),
        _sort_component(item.get("admitted-candidate-id")),
        _sort_digest_component(item),
    )


def _summary_failure_sort_key(
    item: Mapping[str, object],
) -> tuple[str, str, str, str, str, str]:
    return (
        _sort_component(item.get("kind")),
        _sort_component(item.get("evidence-expectation-id")),
        _sort_component(item.get("work-group-id")),
        _sort_component(item.get("batch-id")),
        _sort_component(item.get("bundle-id")),
        _sort_digest_component(item),
    )


def _validate_projection_authority(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if value is None:
        return
    _validated_projection_authority_or_none(value, path, issues)


def _validate_aggregate_manifest_projection_authority(  # noqa: C901,PLR0912,PLR0913,PLR0911
    manifest: Mapping[str, object],
    envelope: CommonEnvelope | None,
    plan: Mapping[str, object] | None,
    request: Mapping[str, object] | None,
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> None:
    authority = manifest.get("projection-authority")
    if plan is not None:
        retained_detail = _invalid_plan_failure_detail_from_inputs(manifest)
        if _invalid_plan_detail_allows_retained_plan_context(retained_detail):
            plan_authority = _projection_authority_from_plan(plan)
            authorizes_retained_projection = (
                _supplied_plan_input_authorizes_retained_invalid_plan_projection
            )
            retained_authorized = authorizes_retained_projection(
                manifest,
                envelope,
                plan,
                request=request,
                changed_files_snapshot=changed_files_snapshot,
                fact_snapshot=fact_snapshot,
            )
            if not retained_authorized:
                issues.append(
                    ValidationIssue(
                        "$.input-artifacts.validation-plan",
                        "must be valid and match supplied plan to "
                        "authorize projection",
                    )
                )
                issues.append(
                    ValidationIssue(
                        "$.projection-authority",
                        "retained invalid-plan details require aggregate "
                        "manifest input authority",
                    )
                )
                return
            if authority is None:
                issues.append(
                    ValidationIssue(
                        "$.projection-authority",
                        "retained invalid-plan details require aggregate "
                        "manifest input authority",
                    )
                )
                return
            if authority != plan_authority:
                issues.append(
                    ValidationIssue(
                        "$.projection-authority",
                        "must match plan projection authority",
                    )
                )
            return
        if _aggregate_manifest_lacks_planless_retained_authority(manifest):
            if authority is not None:
                issues.append(
                    ValidationIssue(
                        "$.projection-authority",
                        "must be null without an authoritative plan",
                    )
                )
            return
        if not _supplied_plan_input_authorizes_projection(
            manifest,
            envelope,
            plan,
            request=request,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
        ):
            if authority is not None:
                issues.append(
                    ValidationIssue(
                        "$.input-artifacts.validation-plan",
                        "must be valid and match supplied plan to "
                        "authorize projection",
                    )
                )
            return
        plan_authority = _projection_authority_from_plan(plan)
        if authority != plan_authority:
            issues.append(
                ValidationIssue(
                    "$.projection-authority",
                    "must match plan projection authority",
                )
            )
        _validate_projection_authority_request_context(
            plan_authority,
            request,
            envelope,
            "$.projection-authority",
            issues,
        )
        return
    if _aggregate_manifest_lacks_planless_retained_authority(manifest):
        if authority is not None:
            issues.append(
                ValidationIssue(
                    "$.projection-authority",
                    "must be null without an authoritative plan",
                )
            )
        return
    invalid_plan_details = _invalid_plan_input_failure_details(manifest)
    if invalid_plan_details:
        if authority is not None:
            issues.append(
                ValidationIssue(
                    "$.projection-authority",
                    "must be null without an authoritative plan",
                )
            )
        else:
            issues.append(
                ValidationIssue(
                    "$.projection-authority",
                    "retained invalid-plan details require complete "
                    "producer-compatible projection context",
                )
            )
        return
    if _aggregate_manifest_has_valid_plan_input(manifest):
        if isinstance(authority, Mapping):
            _validate_manifest_projection_authority_input_binding(
                manifest,
                envelope,
                authority,
                "$.projection-authority",
                issues,
            )
            _validate_projection_authority_request_context(
                authority,
                request,
                envelope,
                "$.projection-authority",
                issues,
            )
        issues.append(
            ValidationIssue(
                "$.input-artifacts.validation-plan",
                "valid admissibility requires supplied validated current-run "
                "plan context",
            )
        )
        return
    if authority is None:
        issues.append(
            ValidationIssue(
                "$.projection-authority",
                "is required with an authoritative plan",
            )
        )
        return
    if request is None:
        issues.append(
            ValidationIssue(
                "$.projection-authority",
                "supplied request context is required to authorize "
                "planless projection authority",
            )
        )
        return
    _validate_manifest_projection_authority_input_binding(
        manifest,
        envelope,
        authority,
        "$.projection-authority",
        issues,
    )
    _validate_projection_authority_request_context(
        authority,
        request,
        envelope,
        "$.projection-authority",
        issues,
    )


def _validate_projection_authority_request_context(
    authority: object,
    request: Mapping[str, object] | None,
    envelope: CommonEnvelope | None,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if (
        request is None
        or envelope is None
        or not isinstance(authority, Mapping)
    ):
        return
    try:
        request_envelope = validate_common_envelope(
            request,
            api_version=API_VERSIONS_BY_KIND[CiValidationKind.REQUEST.value],
            kind=CiValidationKind.REQUEST,
        )
    except ContractValidationError as error:
        issues.extend(
            ValidationIssue(f"request.{issue.path}", issue.message)
            for issue in error.issues
        )
        return
    if not _validate_context_envelope_matches_current(
        request_envelope,
        envelope,
        "request.$",
        issues,
    ):
        return
    try:
        normalized = validate_ci_validation_request(
            request,
            expected_run_id=envelope.run_id,
            expected_run_attempt=envelope.run_attempt,
        )
    except ContractValidationError as error:
        issues.extend(
            ValidationIssue(f"request.{issue.path}", issue.message)
            for issue in error.issues
        )
        return
    expected = _projection_authority_from_request(
        normalized.projection,
        artifact_ref=normalized.artifact_ref,
        request_digest=normalized.request_digest,
    )
    for key, expected_value in expected.items():
        if authority.get(key) != expected_value:
            issues.append(
                ValidationIssue(
                    f"{path}.{key}",
                    "must match supplied request projection",
                )
            )


def _projection_authority_matches_request_context(
    authority: object,
    request: Mapping[str, object] | None,
    envelope: CommonEnvelope | None,
) -> bool:
    if request is None:
        return False
    issues: list[ValidationIssue] = []
    _validate_projection_authority_request_context(
        authority,
        request,
        envelope,
        "$.projection-authority",
        issues,
    )
    return not issues


def _projection_authority_from_request(
    projection: Mapping[str, object],
    *,
    artifact_ref: str,
    request_digest: str,
) -> dict[str, object]:
    mode = projection["mode"]
    return {
        "mode": mode,
        "validation-tree": dict(_mapping(projection["validation-tree"])),
        "affected-range": _projection_authority_affected_range_from_request(
            projection,
        ),
        "request": {
            "artifact-ref": artifact_ref,
            "request-digest": request_digest,
        },
        "scheduled-full": {"enabled": mode == "scheduled_full"},
    }


def _projection_authority_affected_range_from_request(
    projection: Mapping[str, object],
) -> dict[str, object]:
    if projection.get("mode") == "scheduled_full":
        return {
            "status": "not-applicable",
            "base-sha": None,
            "base-tip-sha": None,
            "head-sha": None,
            "changed-files-hash": None,
        }
    affected = _mapping(projection["affected-range"])
    changed_files_hash: str | None = None
    changed_files = affected.get("changed-files")
    if affected.get("status") == "available":
        if not isinstance(changed_files, Sequence) or isinstance(
            changed_files,
            str | bytes,
        ):
            raise ContractValidationError(
                [
                    ValidationIssue(
                        "request.$.affected-range.changed-files",
                        "must be a string array",
                    ),
                ],
            )
        changed_files_hash = ci_validation_changed_files_hash(
            [str(item) for item in changed_files],
        )
    return {
        "status": affected["status"],
        "base-sha": affected["base-sha"],
        "base-tip-sha": affected["base-tip-sha"],
        "head-sha": affected["head-sha"],
        "changed-files-hash": changed_files_hash,
    }


def _validate_manifest_projection_authority_input_binding(
    manifest: Mapping[str, object],
    envelope: CommonEnvelope | None,
    authority: object,
    path: str,
    issues: list[ValidationIssue],
) -> bool:
    if not isinstance(authority, Mapping):
        return False
    bound = True
    request = authority.get("request")
    request_digest = (
        request.get("request-digest") if isinstance(request, Mapping) else None
    )
    request_artifact_ref = (
        request.get("artifact-ref") if isinstance(request, Mapping) else None
    )
    if not _validate_projection_authority_input_digest_binding(
        manifest,
        envelope,
        "request",
        request_digest,
        f"{path}.request.request-digest",
        issues,
        authority_artifact_ref=request_artifact_ref,
        authority_artifact_ref_path=f"{path}.request.artifact-ref",
    ):
        bound = False
    affected_range = authority.get("affected-range")
    changed_files_hash = (
        affected_range.get("changed-files-hash")
        if isinstance(affected_range, Mapping)
        else None
    )
    if (
        isinstance(changed_files_hash, str)
        and changed_files_hash
        and not _validate_projection_authority_input_digest_binding(
            manifest,
            envelope,
            "changed-files-snapshot",
            changed_files_hash,
            f"{path}.affected-range.changed-files-hash",
            issues,
        )
    ):
        bound = False
    return bound


def _validate_projection_authority_input_digest_binding(  # noqa: PLR0913
    manifest: Mapping[str, object],
    envelope: CommonEnvelope | None,
    input_name: str,
    authority_digest: object,
    authority_path: str,
    issues: list[ValidationIssue],
    *,
    authority_artifact_ref: object | None = None,
    authority_artifact_ref_path: str | None = None,
) -> bool:
    artifact = _valid_current_run_input_artifact(
        manifest,
        envelope,
        input_name,
    )
    if artifact is None:
        issues.append(
            ValidationIssue(
                f"$.input-artifacts.{input_name}",
                "valid current-run input is required for projection authority",
            )
        )
        return False
    bound = True
    if artifact.get("content-digest") != authority_digest:
        issues.append(
            ValidationIssue(
                authority_path,
                f"must match input-artifacts.{input_name}.content-digest",
            )
        )
        bound = False
    if (
        authority_artifact_ref_path is not None
        and artifact.get("artifact-ref") != authority_artifact_ref
    ):
        issues.append(
            ValidationIssue(
                authority_artifact_ref_path,
                f"must match input-artifacts.{input_name}.artifact-ref",
            )
        )
        bound = False
    return bound


def _manifest_projection_authority_input_bound(
    manifest: Mapping[str, object],
    envelope: CommonEnvelope | None,
    authority: object,
) -> bool:
    issues: list[ValidationIssue] = []
    return _validate_manifest_projection_authority_input_binding(
        manifest,
        envelope,
        authority,
        "$.projection-authority",
        issues,
    )


def _validated_projection_authority_or_none(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> Mapping[str, object] | None:
    authority = _validate_object(
        value, _PROJECTION_AUTHORITY_KEYS, path, issues
    )
    if authority is None:
        return None
    missing_keys = _PROJECTION_AUTHORITY_KEYS - set(authority)
    for key in sorted(missing_keys):
        issues.append(ValidationIssue(f"{path}.{key}", "is required"))
    if authority.get("mode") not in _MODES:
        issues.append(ValidationIssue(f"{path}.mode", "is not registered"))
    _validate_validation_tree(
        authority.get("validation-tree"),
        f"{path}.validation-tree",
        allow_unknown=False,
        issues=issues,
    )
    _validate_affected_range(
        authority.get("affected-range"),
        issues,
        f"{path}.affected-range",
    )
    _validate_request_summary(
        authority.get("request"), issues, f"{path}.request"
    )
    _validate_scheduled_full(
        authority.get("scheduled-full"),
        issues,
        f"{path}.scheduled-full",
    )
    _validate_digest(
        authority.get("projection-digest"),
        f"{path}.projection-digest",
        issues,
    )
    if isinstance(authority.get("projection-digest"), str) and not any(
        key in missing_keys for key in _PROJECTION_AUTHORITY_PAYLOAD_KEYS
    ):
        try:
            projection_digest = _projection_authority_digest(authority)
        except (TypeError, ValueError) as error:
            issues.append(
                ValidationIssue(f"{path}.projection-digest", str(error))
            )
        else:
            if authority.get("projection-digest") != projection_digest:
                issues.append(
                    ValidationIssue(
                        f"{path}.projection-digest",
                        "must match projection authority payload",
                    )
                )
    if missing_keys:
        return None
    return authority


def _validate_summary_projection_authority(  # noqa: PLR0913
    summary: Mapping[str, object],
    plan: Mapping[str, object] | None,
    aggregate_evidence_manifest: Mapping[str, object] | None,
    envelope: CommonEnvelope | None,
    request: Mapping[str, object] | None,
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> None:
    if _is_invalid_plan_summary(summary):
        return
    if plan is not None:
        _validate_supplied_plan_summary_projection(
            summary,
            plan,
            aggregate_evidence_manifest,
            envelope,
            request,
            changed_files_snapshot,
            fact_snapshot,
            issues=issues,
        )
        return
    if (
        aggregate_evidence_manifest is not None
        and not _aggregate_manifest_has_no_authoritative_plan(
            aggregate_evidence_manifest
        )
    ):
        if aggregate_evidence_manifest.get(
            "projection-authority"
        ) is None and _summary_projection_matches(
            summary,
            _no_authority_summary_projection(),
        ):
            return
        authority = _validated_projection_authority_or_none(
            aggregate_evidence_manifest.get("projection-authority"),
            "$.aggregate-evidence-manifest.projection-authority",
            issues,
        )
        if authority is None:
            issues.append(
                ValidationIssue(
                    "$.aggregate-evidence-manifest.projection-authority",
                    "is required without plan",
                )
            )
            return
        if not _validate_manifest_projection_authority_input_binding(
            aggregate_evidence_manifest,
            envelope,
            authority,
            "$.aggregate-evidence-manifest.projection-authority",
            issues,
        ):
            return
        issues.append(
            ValidationIssue(
                "$.aggregate-evidence-manifest.projection-authority",
                "supplied plan is required to authorize planless "
                "projection authority",
            )
        )
        _validate_summary_projection_matches(
            summary,
            _no_authority_summary_projection(),
            "no-authority fail-closed projection",
            issues,
        )
        return
    _validate_summary_projection_matches(
        summary,
        _no_authority_summary_projection(),
        "no-authority fail-closed projection",
        issues,
    )


def _validate_supplied_plan_summary_projection(  # noqa: PLR0913
    summary: Mapping[str, object],
    plan: Mapping[str, object],
    aggregate_evidence_manifest: Mapping[str, object] | None,
    envelope: CommonEnvelope | None,
    request: Mapping[str, object] | None,
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> None:
    plan_authorizes_projection = (
        aggregate_evidence_manifest is not None
        and _supplied_plan_input_authorizes_projection(
            aggregate_evidence_manifest,
            envelope,
            plan,
            request=request,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
        )
    )
    if plan_authorizes_projection:
        expected_projection = _summary_projection_from_plan(plan)
        if not _aggregate_manifest_has_valid_request(
            aggregate_evidence_manifest
        ):
            expected_projection["request"] = dict(_UNKNOWN_REQUEST_SUMMARY)
        _validate_summary_projection_matches(
            summary,
            expected_projection,
            "plan",
            issues,
        )
        return
    if _validate_summary_manifest_projection_authority(
        summary,
        aggregate_evidence_manifest,
        issues,
    ):
        return
    _validate_summary_projection_matches(
        summary,
        _no_authority_summary_projection(),
        "no-authority fail-closed projection",
        issues,
    )
    if not _summary_projection_matches(
        summary,
        _no_authority_summary_projection(),
    ):
        issues.append(
            ValidationIssue(
                "$.projection-authority",
                "supplied plan or aggregate manifest projection authority "
                "is required for non-unknown projection",
            )
        )


def _validate_summary_manifest_projection_authority(
    summary: Mapping[str, object],
    aggregate_evidence_manifest: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> bool:
    if (
        aggregate_evidence_manifest is None
        or _aggregate_manifest_has_no_authoritative_plan(
            aggregate_evidence_manifest
        )
    ):
        return False
    authority_value = aggregate_evidence_manifest.get("projection-authority")
    if authority_value is None:
        return False
    authority = _validated_projection_authority_or_none(
        authority_value,
        "$.aggregate-evidence-manifest.projection-authority",
        issues,
    )
    if authority is None:
        return False
    if not _validate_manifest_projection_authority_input_binding(
        aggregate_evidence_manifest,
        _aggregate_manifest_envelope_or_none(aggregate_evidence_manifest),
        authority,
        "$.aggregate-evidence-manifest.projection-authority",
        issues,
    ):
        return False
    expected_projection = _projection_authority_payload(authority)
    _validate_summary_projection_matches(
        summary,
        expected_projection,
        "aggregate manifest projection authority",
        issues,
    )
    return _summary_projection_matches(summary, expected_projection)


def _projection_with_manifest_request_state(
    projection: Mapping[str, object],
    aggregate_evidence_manifest: Mapping[str, object] | None,
) -> dict[str, object]:
    normalized = dict(projection)
    if not _aggregate_manifest_has_valid_request(aggregate_evidence_manifest):
        normalized["request"] = dict(_UNKNOWN_REQUEST_SUMMARY)
    return normalized


def _summary_projection_matches(
    summary: Mapping[str, object],
    expected_projection: Mapping[str, object],
) -> bool:
    return all(
        summary.get(key) == expected
        for key, expected in expected_projection.items()
    )


def _aggregate_manifest_has_valid_request(
    aggregate_evidence_manifest: Mapping[str, object] | None,
) -> bool:
    if aggregate_evidence_manifest is None:
        return True
    input_artifacts = aggregate_evidence_manifest.get("input-artifacts")
    if not isinstance(input_artifacts, Mapping):
        return False
    request = input_artifacts.get("request")
    return (
        isinstance(request, Mapping) and request.get("admissibility") == "valid"
    )


def _validate_summary_projection_matches(
    summary: Mapping[str, object],
    expected_projection: Mapping[str, object],
    authority_name: str,
    issues: list[ValidationIssue],
) -> None:
    for key, expected in expected_projection.items():
        if summary.get(key) != expected:
            issues.append(
                ValidationIssue(
                    f"$.{key}",
                    f"must match {authority_name}",
                )
            )


def _validate_null_plan_identity(
    document: Mapping[str, object],
    path: str,
    issues: list[ValidationIssue],
) -> None:
    for key in ("plan-id", "plan-digest"):
        if document.get(key) is not None:
            issues.append(
                ValidationIssue(
                    f"{path}.{key}",
                    "must be null without an authoritative plan",
                )
            )


def _aggregate_manifest_has_no_authoritative_plan(
    manifest: Mapping[str, object],
) -> bool:
    if _aggregate_manifest_has_true_no_authority_invalid_plan(manifest):
        return True
    inputs = manifest.get("input-artifacts")
    if not isinstance(inputs, Mapping):
        return False
    validation_plan = inputs.get("validation-plan")
    if not isinstance(validation_plan, Mapping):
        return False
    return validation_plan.get("admissibility") != "valid"


def _aggregate_manifest_lacks_planless_retained_authority(
    manifest: Mapping[str, object],
) -> bool:
    details = _invalid_plan_plan_authority_failure_details(manifest)
    if not details:
        return _aggregate_manifest_has_no_authoritative_plan(manifest)
    if _invalid_plan_inputs_have_multiple_invalid_plan_diagnostics(manifest):
        return False
    if (
        _canonical_plan_authority_failure_details(details)
        <= _INVALID_PLAN_NO_AUTHORITY_PROJECTION_DETAILS
    ):
        return True
    if any(
        _invalid_plan_detail_allows_retained_plan_context(detail)
        for detail in details
    ):
        return False
    retained_detail = _invalid_plan_failure_detail_from_detail_set(details)
    return not _invalid_plan_detail_allows_retained_plan_context(
        retained_detail
    )


def _aggregate_manifest_has_true_no_authority_invalid_plan(
    manifest: Mapping[str, object],
) -> bool:
    details = _invalid_plan_plan_authority_failure_details(manifest)
    return (
        bool(details)
        and not _invalid_plan_inputs_have_multiple_invalid_plan_diagnostics(
            manifest
        )
        and _canonical_plan_authority_failure_details(details)
        <= _INVALID_PLAN_NO_AUTHORITY_PROJECTION_DETAILS
    )


def _canonical_plan_authority_failure_details(details: set[str]) -> set[str]:
    return set(details)


def _invalid_plan_plan_authority_failure_details(
    aggregate_manifest: Mapping[str, object] | None,
) -> set[str]:
    return _invalid_plan_input_failure_details(aggregate_manifest) - {
        DiagnosticDetail.CHANGED_FILES_SNAPSHOT_MISSING.value,
        DiagnosticDetail.FACT_SNAPSHOT_MISSING.value,
    }


def _invalid_plan_inputs_have_multiple_invalid_plan_diagnostics(
    aggregate_manifest: Mapping[str, object] | None,
) -> bool:
    if aggregate_manifest is None:
        return False
    input_artifacts = aggregate_manifest.get("input-artifacts")
    if not isinstance(input_artifacts, Mapping):
        return False
    for item in input_artifacts.values():
        if not isinstance(item, Mapping):
            continue
        diagnostics = item.get("diagnostics")
        if not isinstance(diagnostics, Sequence) or isinstance(
            diagnostics,
            str | bytes,
        ):
            continue
        invalid_plan_diagnostic_count = sum(
            1
            for diagnostic in diagnostics
            if isinstance(diagnostic, Mapping)
            and diagnostic.get("code") == DiagnosticFamily.INVALID_PLAN.value
        )
        if invalid_plan_diagnostic_count > 1:
            return True
    return False


def _execution_batch_manifest_has_non_empty_batches(
    execution_batch_manifest: Mapping[str, object],
) -> bool:
    batches = execution_batch_manifest.get("batches")
    return (
        isinstance(batches, Sequence)
        and not isinstance(batches, str | bytes)
        and len(batches) > 0
    )


def _allow_planless_execution_manifest_diagnostic(
    plan: Mapping[str, object] | None,
    execution_batch_manifest: Mapping[str, object],
) -> bool:
    return (
        plan is not None
        or not _execution_batch_manifest_has_non_empty_batches(
            execution_batch_manifest
        )
    )


def _aggregate_manifest_has_valid_plan_input(
    manifest: Mapping[str, object],
) -> bool:
    inputs = manifest.get("input-artifacts")
    if not isinstance(inputs, Mapping):
        return False
    validation_plan = inputs.get("validation-plan")
    return (
        isinstance(validation_plan, Mapping)
        and validation_plan.get("admissibility") == "valid"
    )


def _aggregate_manifest_has_valid_execution_batch_manifest_input(
    manifest: Mapping[str, object],
) -> bool:
    inputs = manifest.get("input-artifacts")
    if not isinstance(inputs, Mapping):
        return False
    execution_batch_manifest = inputs.get("execution-batch-manifest")
    return (
        isinstance(execution_batch_manifest, Mapping)
        and execution_batch_manifest.get("admissibility") == "valid"
    )


def _validate_valid_execution_batch_manifest_input_has_document(
    manifest: Mapping[str, object],
    execution_batch_manifest: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> None:
    if (
        execution_batch_manifest is not None
        or not _aggregate_manifest_has_valid_execution_batch_manifest_input(
            manifest
        )
    ):
        return
    issues.append(
        ValidationIssue(
            "$.input-artifacts.execution-batch-manifest",
            "valid admissibility requires execution-batch manifest document",
        )
    )


def _validate_valid_validation_plan_input_has_document(
    manifest: Mapping[str, object],
    plan: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> None:
    if (
        plan is not None
        or _aggregate_manifest_has_no_authoritative_plan(manifest)
        or not _aggregate_manifest_has_valid_plan_input(manifest)
    ):
        return
    issues.append(
        ValidationIssue(
            "$.input-artifacts.validation-plan",
            "valid admissibility requires supplied validated current-run "
            "plan context",
        )
    )


def _validate_standalone_aggregate_manifest_plan_identity(
    manifest: Mapping[str, object],
    issues: list[ValidationIssue],
) -> None:
    _validate_non_empty_string(manifest.get("plan-id"), "$.plan-id", issues)
    _validate_digest(manifest.get("plan-digest"), "$.plan-digest", issues)
    inputs = manifest.get("input-artifacts")
    validation_plan = (
        inputs.get("validation-plan") if isinstance(inputs, Mapping) else None
    )
    if not isinstance(validation_plan, Mapping):
        return
    if validation_plan.get("admissibility") != "valid":
        return
    if validation_plan.get("content-digest") != manifest.get("plan-digest"):
        issues.append(
            ValidationIssue(
                "$.input-artifacts.validation-plan.content-digest",
                "must match aggregate manifest plan digest",
            )
        )


def _validate_input_artifacts(  # noqa: C901,PLR0912,PLR0913,PLR0915
    value: object,
    envelope: CommonEnvelope | None,
    plan: Mapping[str, object] | None,
    execution_batch_manifest: Mapping[str, object] | None,
    request_context_digest: str | None,
    changed_files_snapshot_context_hash: str | None,
    fact_snapshot_context_id: str | None,
    *,
    changed_files_snapshot_input_proven: bool,
    plan_fact_snapshot_binding_proven: bool,
    require_authoritative_snapshot_inputs: bool,
    frozen_input_digests: Mapping[str, str] | None,
    require_context_proof_for_valid_inputs: bool,
    issues: list[ValidationIssue],
) -> int:
    if not isinstance(value, Mapping):
        issues.append(ValidationIssue("$.input-artifacts", "must be an object"))
        return 0
    _validate_root_keys(
        value, _INPUT_ARTIFACT_NAMES, "$.input-artifacts", issues
    )
    bindable_inputs: set[str] = set()
    context_required_inputs = {"request", "execution-batch-manifest"}
    if plan is not None:
        context_required_inputs.add("validation-plan")
    if plan is not None:
        context_required_inputs.update(_plan_required_snapshot_inputs(plan))
    input_artifact_count = 0
    for name in sorted(_INPUT_ARTIFACT_NAMES):
        item = value.get(name)
        path = f"$.input-artifacts.{name}"
        artifact = _validate_object(item, _INPUT_ARTIFACT_KEYS, path, issues)
        if artifact is None:
            continue
        _validate_nullable_artifact_ref(
            artifact.get("artifact-ref"), f"{path}.artifact-ref", issues
        )
        _validate_nullable_non_empty_string(
            artifact.get("artifact-instance-id"),
            f"{path}.artifact-instance-id",
            issues,
        )
        _validate_nullable_digest(
            artifact.get("content-digest"),
            f"{path}.content-digest",
            issues,
        )
        if not isinstance(artifact.get("required"), bool):
            issues.append(
                ValidationIssue(f"{path}.required", "must be boolean")
            )
        if artifact.get("expected-cardinality") not in {0, 1}:
            issues.append(
                ValidationIssue(
                    f"{path}.expected-cardinality",
                    "must be 0 or 1",
                ),
            )
        admissibility = artifact.get("admissibility")
        if admissibility not in _ADMISSIBILITIES:
            issues.append(
                ValidationIssue(f"{path}.admissibility", "is not registered")
            )
        _validate_input_artifact_state(
            name,
            artifact,
            path,
            context_required_inputs,
            issues,
        )
        if (
            plan is not None
            and name in {"changed-files-snapshot", "fact-snapshot"}
            and name not in context_required_inputs
        ):
            _validate_not_required_input_artifact(artifact, path, issues)
        if _input_artifact_counts_toward_prefinal(artifact):
            input_artifact_count += 1
        if artifact.get("admissibility") in {"missing", "not-required"}:
            for absent_key in (
                "artifact-ref",
                "artifact-instance-id",
                "content-digest",
            ):
                if artifact.get(absent_key) is not None:
                    issues.append(
                        ValidationIssue(
                            f"{path}.{absent_key}",
                            "must be null when artifact is absent",
                        )
                    )
        if (
            artifact.get("required") is True
            and artifact.get("admissibility") == "valid"
        ):
            if artifact.get("artifact-ref") is None:
                issues.append(
                    ValidationIssue(f"{path}.artifact-ref", "is required")
                )
            _validate_non_empty_string(
                artifact.get("artifact-instance-id"),
                f"{path}.artifact-instance-id",
                issues,
            )
            _validate_digest(
                artifact.get("content-digest"),
                f"{path}.content-digest",
                issues,
            )
            bindable_inputs.add(name)
        _validate_diagnostics(
            artifact.get("diagnostics"), f"{path}.diagnostics", issues
        )
        _validate_input_artifact_invalid_plan_diagnostics(
            name,
            artifact,
            f"{path}.diagnostics",
            issues,
        )
    if envelope is None:
        return input_artifact_count
    expected_refs = {
        "request": ci_validation_request_artifact_ref(
            run_id=envelope.run_id,
            run_attempt=envelope.run_attempt,
        ),
        "validation-plan": ci_validation_plan_artifact_ref(
            run_id=envelope.run_id,
            run_attempt=envelope.run_attempt,
        ),
        "changed-files-snapshot": (
            ci_validation_changed_files_snapshot_artifact_ref(
                run_id=envelope.run_id,
                run_attempt=envelope.run_attempt,
            )
        ),
        "fact-snapshot": ci_validation_fact_snapshot_artifact_ref(
            run_id=envelope.run_id,
            run_attempt=envelope.run_attempt,
        ),
        "execution-batch-manifest": (
            ci_validation_execution_batch_manifest_artifact_ref(
                run_id=envelope.run_id,
                run_attempt=envelope.run_attempt,
            )
        ),
    }
    for name, expected_ref in expected_refs.items():
        item = value.get(name)
        if (
            name in bindable_inputs
            and isinstance(item, Mapping)
            and item.get("artifact-ref") != expected_ref
        ):
            issues.append(
                ValidationIssue(
                    f"$.input-artifacts.{name}.artifact-ref",
                    "must match current run",
                ),
            )
    expected_digests = _expected_input_digests(
        plan=plan,
        execution_batch_manifest=execution_batch_manifest,
        request_context_digest=request_context_digest,
        changed_files_snapshot_context_hash=(
            changed_files_snapshot_context_hash
        ),
        fact_snapshot_context_id=fact_snapshot_context_id,
        plan_fact_snapshot_binding_proven=plan_fact_snapshot_binding_proven,
        frozen_input_digests=frozen_input_digests,
        issues=issues,
    )
    for name, expected_digest in expected_digests.items():
        item = value.get(name)
        if (
            name in bindable_inputs
            and isinstance(item, Mapping)
            and item.get("content-digest") != expected_digest
        ):
            issues.append(
                ValidationIssue(
                    f"$.input-artifacts.{name}.content-digest",
                    "must match frozen input digest",
                )
            )
    if request_context_digest is not None and (
        require_context_proof_for_valid_inputs
        or _input_artifact_is_valid(value, "request")
    ):
        _validate_supplied_context_input_artifact_binding(
            value,
            envelope,
            "request",
            request_context_digest,
            "request",
            issues,
        )
    elif require_context_proof_for_valid_inputs:
        _validate_valid_input_artifact_requires_context(
            value,
            "request",
            "request",
            issues,
        )
    if changed_files_snapshot_context_hash is not None and (
        require_context_proof_for_valid_inputs
        or _input_artifact_is_valid(value, "changed-files-snapshot")
    ):
        _validate_supplied_context_input_artifact_binding(
            value,
            envelope,
            "changed-files-snapshot",
            changed_files_snapshot_context_hash,
            "changed_files_snapshot",
            issues,
        )
    elif require_context_proof_for_valid_inputs:
        _validate_valid_input_artifact_requires_context(
            value,
            "changed-files-snapshot",
            "changed_files_snapshot",
            issues,
        )
    if fact_snapshot_context_id is not None and (
        require_context_proof_for_valid_inputs
        or _input_artifact_is_valid(value, "fact-snapshot")
    ):
        _validate_supplied_context_input_artifact_binding(
            value,
            envelope,
            "fact-snapshot",
            fact_snapshot_context_id,
            "fact_snapshot",
            issues,
        )
    elif require_context_proof_for_valid_inputs:
        _validate_valid_input_artifact_requires_context(
            value,
            "fact-snapshot",
            "fact_snapshot",
            issues,
        )
    if require_authoritative_snapshot_inputs:
        _validate_unproven_fact_snapshot_input(
            value,
            plan,
            changed_files_snapshot_context_hash,
            fact_snapshot_context_id,
            changed_files_snapshot_input_proven=(
                changed_files_snapshot_input_proven
            ),
            plan_fact_snapshot_binding_proven=plan_fact_snapshot_binding_proven,
            issues=issues,
        )
    return input_artifact_count


def _validate_supplied_context_input_artifact_binding(  # noqa: PLR0913
    input_artifacts: Mapping[str, object],
    envelope: CommonEnvelope,
    input_name: str,
    expected_digest: str,
    context_name: str,
    issues: list[ValidationIssue],
) -> None:
    item = input_artifacts.get(input_name)
    path = f"$.input-artifacts.{input_name}"
    if not isinstance(item, Mapping):
        issues.append(
            ValidationIssue(
                path,
                f"valid current-run input is required for {context_name}",
            )
        )
        return
    if item.get("admissibility") != "valid":
        issues.append(
            ValidationIssue(
                f"{path}.admissibility",
                f"must be valid when {context_name} is supplied",
            ),
        )
    expected_ref = _expected_input_artifact_ref(input_name, envelope)
    if item.get("artifact-ref") != expected_ref:
        issues.append(
            ValidationIssue(
                f"{path}.artifact-ref",
                f"must match current run when {context_name} is supplied",
            )
        )
    if item.get("content-digest") != expected_digest:
        issues.append(
            ValidationIssue(
                f"{path}.content-digest",
                f"must match {context_name}",
            ),
        )


def _input_artifact_is_valid(
    input_artifacts: Mapping[str, object],
    input_name: str,
) -> bool:
    item = input_artifacts.get(input_name)
    return isinstance(item, Mapping) and item.get("admissibility") == "valid"


def _input_artifacts_have_projection_authority(
    input_artifacts: Mapping[str, object],
    *,
    plan: Mapping[str, object],
) -> bool:
    for input_name in ("request", "validation-plan"):
        item = input_artifacts.get(input_name)
        if (
            not isinstance(item, Mapping)
            or item.get("admissibility") != "valid"
        ):
            return False
    for input_name in _plan_required_snapshot_inputs(plan):
        item = input_artifacts.get(input_name)
        if (
            not isinstance(item, Mapping)
            or item.get("admissibility") != "valid"
        ):
            return False
    return True


def _input_artifacts_have_retained_projection_authority(
    input_artifacts: Mapping[str, object],
    *,
    plan: Mapping[str, object],
) -> bool:
    retained_detail = _invalid_plan_failure_detail_from_inputs(
        {"input-artifacts": input_artifacts}
    )
    if not _invalid_plan_detail_allows_retained_plan_context(retained_detail):
        return False
    culprit_input_name = _retained_projection_culprit_input_name(
        cast("str", retained_detail)
    )
    request = input_artifacts.get("request")
    validation_plan = input_artifacts.get("validation-plan")
    plan_request = plan.get("request")
    if not (
        isinstance(request, Mapping)
        and isinstance(validation_plan, Mapping)
        and isinstance(plan_request, Mapping)
        and request.get("admissibility") == "valid"
        and request.get("artifact-ref") == plan_request.get("artifact-ref")
        and request.get("content-digest") == plan_request.get("request-digest")
        and validation_plan.get("content-digest")
        == _retained_invalid_plan_digest_or_none(plan)
    ):
        return False
    if not _validation_plan_has_retained_projection_authority(
        validation_plan,
        culprit_input_name=culprit_input_name,
        retained_detail=cast("str", retained_detail),
    ):
        return False
    return _snapshot_inputs_have_retained_projection_authority(
        input_artifacts,
        plan=plan,
        culprit_input_name=culprit_input_name,
        retained_detail=cast("str", retained_detail),
    )


def _snapshot_inputs_have_retained_projection_authority(
    input_artifacts: Mapping[str, object],
    *,
    plan: Mapping[str, object],
    culprit_input_name: str,
    retained_detail: str,
) -> bool:
    for input_name in _plan_required_snapshot_inputs(plan):
        item = input_artifacts.get(input_name)
        if not isinstance(item, Mapping):
            return False
        if input_name == culprit_input_name:
            if not _retained_projection_input_has_complete_invalid_identity(
                item,
                input_name,
                retained_detail,
            ):
                return False
            continue
        expected_digest = _retained_invalid_plan_snapshot_digest(
            plan,
            input_name,
        )
        if (
            item.get("admissibility") == "valid"
            and item.get("content-digest") == expected_digest
            and item.get("diagnostics") == []
        ):
            continue
        if _snapshot_input_has_unproven_companion_identity(input_name, item):
            continue
        return False
    return True


def _validation_plan_has_retained_projection_authority(
    validation_plan: Mapping[str, object],
    *,
    culprit_input_name: str,
    retained_detail: str,
) -> bool:
    if culprit_input_name == "validation-plan":
        return _retained_projection_input_has_complete_invalid_identity(
            validation_plan,
            "validation-plan",
            retained_detail,
        )
    return validation_plan.get("admissibility") == "valid"


def _retained_projection_input_has_complete_invalid_identity(
    item: Mapping[str, object],
    input_name: str,
    retained_detail: str,
) -> bool:
    diagnostics = item.get("diagnostics")
    return (
        item.get("required") is True
        and item.get("expected-cardinality") == 1
        and item.get("admissibility") == "inadmissible"
        and isinstance(item.get("artifact-ref"), str)
        and bool(item.get("artifact-ref"))
        and isinstance(item.get("artifact-instance-id"), str)
        and bool(item.get("artifact-instance-id"))
        and isinstance(item.get("content-digest"), str)
        and bool(item.get("content-digest"))
        and isinstance(diagnostics, Sequence)
        and not isinstance(diagnostics, str | bytes)
        and any(
            isinstance(diagnostic, Mapping)
            and diagnostic.get("detail") == retained_detail
            and _input_artifact_invalid_plan_diagnostic_is_canonical(
                input_name,
                item,
                diagnostic,
            )
            for diagnostic in diagnostics
        )
    )


def _validate_valid_input_artifact_requires_context(
    input_artifacts: Mapping[str, object],
    input_name: str,
    context_name: str,
    issues: list[ValidationIssue],
) -> None:
    item = input_artifacts.get(input_name)
    if not isinstance(item, Mapping) or item.get("admissibility") != "valid":
        return
    if input_name != "request" and item.get("required") is not True:
        return
    issues.append(
        ValidationIssue(
            f"$.input-artifacts.{input_name}.admissibility",
            f"valid {input_name} input requires proven {context_name} context",
        )
    )


def _plan_required_snapshot_inputs(plan: Mapping[str, object]) -> set[str]:
    required: set[str] = set()
    affected = plan.get("affected-range")
    if isinstance(affected, Mapping):
        status = affected.get("status")
        changed_files_hash = affected.get("changed-files-hash")
        if status == "available" or (
            isinstance(changed_files_hash, str) and changed_files_hash != ""
        ):
            required.add("changed-files-snapshot")
    fact = plan.get("fact-snapshot")
    if isinstance(fact, Mapping) and fact.get("status") == "available":
        required.add("fact-snapshot")
    return required


def _plan_requires_changed_files_snapshot(plan: Mapping[str, object]) -> bool:
    return "changed-files-snapshot" in _plan_required_snapshot_inputs(plan)


def _validate_unproven_fact_snapshot_input(  # noqa: PLR0913
    input_artifacts: Mapping[str, object],
    plan: Mapping[str, object] | None,
    changed_files_snapshot_context_hash: str | None,
    fact_snapshot_context_id: str | None,
    *,
    changed_files_snapshot_input_proven: bool,
    plan_fact_snapshot_binding_proven: bool,
    issues: list[ValidationIssue],
) -> None:
    del plan_fact_snapshot_binding_proven
    if fact_snapshot_context_id is not None:
        return
    item = input_artifacts.get("fact-snapshot")
    if not isinstance(item, Mapping) or item.get("admissibility") != "valid":
        return
    if plan is not None:
        if "fact-snapshot" not in _plan_required_snapshot_inputs(plan):
            return
        if _plan_requires_changed_files_snapshot(plan) and (
            changed_files_snapshot_context_hash is None
            or not changed_files_snapshot_input_proven
        ):
            return
    issues.append(
        ValidationIssue(
            "$.input-artifacts.fact-snapshot.admissibility",
            "valid fact-snapshot input requires authoritative fact snapshot",
        )
    )


def _validate_not_required_input_artifact(
    artifact: Mapping[str, object],
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if artifact.get("required") is not False:
        issues.append(ValidationIssue(f"{path}.required", "must be false"))
    if artifact.get("expected-cardinality") != 0:
        issues.append(
            ValidationIssue(f"{path}.expected-cardinality", "must be 0")
        )
    if artifact.get("admissibility") != "not-required":
        issues.append(
            ValidationIssue(f"{path}.admissibility", "must be not-required")
        )
    for absent_key in (
        "artifact-ref",
        "artifact-instance-id",
        "content-digest",
    ):
        if artifact.get(absent_key) is not None:
            issues.append(
                ValidationIssue(
                    f"{path}.{absent_key}",
                    "must be null when artifact is not required",
                )
            )


def _input_artifact_counts_toward_prefinal(
    artifact: Mapping[str, object],
) -> bool:
    return (
        artifact.get("required") is True
        or artifact.get("expected-cardinality") == 1
        or artifact.get("artifact-ref") is not None
    )


def _validate_input_artifact_state(  # noqa: C901
    name: str,
    artifact: Mapping[str, object],
    path: str,
    context_required_inputs: set[str],
    issues: list[ValidationIssue],
) -> None:
    required = artifact.get("required")
    cardinality = artifact.get("expected-cardinality")
    admissibility = artifact.get("admissibility")
    if name in context_required_inputs:
        if required is not True:
            issues.append(ValidationIssue(f"{path}.required", "must be true"))
        if cardinality != 1:
            issues.append(
                ValidationIssue(f"{path}.expected-cardinality", "must be 1")
            )
        if admissibility not in _REQUIRED_INPUT_ARTIFACT_ADMISSIBILITIES:
            issues.append(
                ValidationIssue(
                    f"{path}.admissibility",
                    "must be valid, missing, or inadmissible",
                )
            )
        return
    if admissibility == "not-required":
        if required is not False:
            issues.append(ValidationIssue(f"{path}.required", "must be false"))
        if cardinality != 0:
            issues.append(
                ValidationIssue(f"{path}.expected-cardinality", "must be 0")
            )
        return
    if admissibility == "duplicate":
        issues.append(
            ValidationIssue(
                f"{path}.admissibility",
                "must be valid, missing, inadmissible, or not-required",
            )
        )
    if admissibility in {"valid", "missing", "inadmissible"}:
        if required is not True:
            issues.append(ValidationIssue(f"{path}.required", "must be true"))
        if cardinality != 1:
            issues.append(
                ValidationIssue(f"{path}.expected-cardinality", "must be 1")
            )


def _expected_input_digests(  # noqa: C901,PLR0913
    *,
    plan: Mapping[str, object] | None,
    execution_batch_manifest: Mapping[str, object] | None,
    request_context_digest: str | None,
    changed_files_snapshot_context_hash: str | None,
    fact_snapshot_context_id: str | None,
    plan_fact_snapshot_binding_proven: bool,
    frozen_input_digests: Mapping[str, str] | None,
    issues: list[ValidationIssue],
) -> dict[str, str]:
    expected: dict[str, str] = {}

    def add_expected(name: str, digest: object, source_path: str) -> None:
        if not isinstance(digest, str) or _DIGEST_RE.fullmatch(digest) is None:
            return
        existing = expected.get(name)
        if existing is not None and existing != digest:
            issues.append(
                ValidationIssue(
                    source_path,
                    "must match plan-frozen input digest",
                )
            )
            return
        expected[name] = digest

    if plan is not None:
        add_expected(
            "validation-plan",
            _verified_plan_digest_or_none(plan),
            "plan.plan-digest",
        )
        plan_request = plan.get("request")
        if isinstance(plan_request, Mapping):
            add_expected(
                "request",
                plan_request.get("request-digest"),
                "plan.request.request-digest",
            )
        affected = plan.get("affected-range")
        if isinstance(affected, Mapping):
            add_expected(
                "changed-files-snapshot",
                affected.get("changed-files-hash"),
                "plan.affected-range.changed-files-hash",
            )
        fact = plan.get("fact-snapshot")
        if plan_fact_snapshot_binding_proven and isinstance(fact, Mapping):
            add_expected(
                "fact-snapshot", fact.get("id"), "plan.fact-snapshot.id"
            )
    if frozen_input_digests is not None:
        for key, value in frozen_input_digests.items():
            if key in _INPUT_ARTIFACT_NAMES:
                add_expected(key, value, f"frozen_input_digests.{key}")
    if request_context_digest is not None:
        add_expected(
            "request",
            request_context_digest,
            "request.request-digest",
        )
    if changed_files_snapshot_context_hash is not None:
        add_expected(
            "changed-files-snapshot",
            changed_files_snapshot_context_hash,
            "changed_files_snapshot.hash-payload.changed-files",
        )
    if fact_snapshot_context_id is not None:
        add_expected(
            "fact-snapshot",
            fact_snapshot_context_id,
            "fact_snapshot.fact-snapshot-id",
        )
    if execution_batch_manifest is not None:
        add_expected(
            "execution-batch-manifest",
            ci_validation_execution_batch_manifest_payload_digest(
                execution_batch_manifest
            ),
            "execution_batch_manifest",
        )
    return expected


def _validate_batch_bundle_slots(  # noqa: C901,PLR0912,PLR0913
    aggregate_manifest: Mapping[str, object],
    value: object,
    envelope: CommonEnvelope | None,
    plan: Mapping[str, object] | None,
    request: Mapping[str, object] | None,
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    execution_batch_manifest: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> int:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        issues.append(ValidationIssue("$.batch-bundles", "must be an array"))
        return 0
    previous: tuple[str, str, str, str] | None = None
    batch_ids: list[str] = []
    has_authoritative_execution_manifest = (
        _aggregate_manifest_has_authoritative_execution_manifest(
            aggregate_manifest,
            envelope,
            execution_batch_manifest,
        )
    )
    has_authoritative_projection = (
        _aggregate_manifest_has_authoritative_projection(
            aggregate_manifest,
            plan,
            envelope,
            request,
            changed_files_snapshot,
            fact_snapshot,
        )
    )
    can_admit_valid_bundle = (
        has_authoritative_execution_manifest and has_authoritative_projection
    )
    for index, item in enumerate(value):
        path = f"$.batch-bundles[{index}]"
        if not isinstance(item, Mapping):
            issues.append(ValidationIssue(path, "must be an object"))
            continue
        _validate_root_keys(item, _BATCH_BUNDLE_SLOT_KEYS, path, issues)
        batch_id = item.get("batch-id")
        _validate_local_id(batch_id, f"{path}.batch-id", issues)
        if isinstance(batch_id, str):
            batch_ids.append(batch_id)
            current = _aggregate_batch_bundle_sort_key(item)
            if previous is not None and previous > current:
                issues.append(
                    ValidationIssue("$.batch-bundles", "must be sorted")
                )
            previous = current
        _validate_artifact_ref(
            item.get("artifact-ref"), f"{path}.artifact-ref", issues
        )
        if envelope is not None and isinstance(batch_id, str):
            expected = ci_validation_batch_evidence_bundle_artifact_ref(
                run_id=envelope.run_id,
                run_attempt=envelope.run_attempt,
                batch_id=batch_id,
            )
            if item.get("artifact-ref") != expected:
                issues.append(
                    ValidationIssue(f"{path}.artifact-ref", "must match batch")
                )
        if item.get("expected-cardinality") != 1:
            issues.append(
                ValidationIssue(f"{path}.expected-cardinality", "must be 1")
            )
        slot_admissibility = item.get("slot-admissibility")
        if slot_admissibility not in _BUNDLE_ADMISSIBILITIES:
            issues.append(
                ValidationIssue(
                    f"{path}.slot-admissibility", "is not registered"
                )
            )
        if not can_admit_valid_bundle and slot_admissibility == "valid":
            issues.append(
                ValidationIssue(
                    f"{path}.slot-admissibility",
                    "requires authoritative plan or projection authority",
                )
            )
        _validate_nullable_non_empty_string(
            item.get("admitted-candidate-id"),
            f"{path}.admitted-candidate-id",
            issues,
        )
        _validate_observed_candidates(
            item.get("observed-candidates"),
            item.get("admitted-candidate-id"),
            slot_admissibility,
            envelope,
            batch_id,
            item.get("artifact-ref"),
            path,
            issues,
        )
        if not can_admit_valid_bundle:
            _validate_no_valid_candidates_without_authority(
                item.get("observed-candidates"),
                path,
                issues,
            )
        _validate_diagnostics(
            item.get("diagnostics"), f"{path}.diagnostics", issues
        )
    if len(batch_ids) != len(set(batch_ids)):
        issues.append(
            ValidationIssue("$.batch-bundles.batch-id", "must be unique")
        )
    if execution_batch_manifest is not None:
        manifest_batches = execution_batch_manifest.get("batches")
        if isinstance(manifest_batches, Sequence) and not isinstance(
            manifest_batches, str | bytes
        ):
            expected_batch_ids = {
                str(batch["batch-id"])
                for batch in manifest_batches
                if isinstance(batch, Mapping)
                and isinstance(batch.get("batch-id"), str)
            }
            if set(batch_ids) != expected_batch_ids:
                issues.append(
                    ValidationIssue(
                        "$.batch-bundles",
                        "must cover execution-batch manifest batches exactly",
                    )
                )
    return len(value)


def _aggregate_manifest_has_authoritative_projection(  # noqa: PLR0913
    manifest: Mapping[str, object],
    plan: Mapping[str, object] | None,
    envelope: CommonEnvelope | None,
    request: Mapping[str, object] | None = None,
    changed_files_snapshot: Mapping[str, object] | None = None,
    fact_snapshot: Mapping[str, object] | None = None,
) -> bool:
    if plan is not None:
        if not _supplied_plan_input_authorizes_projection(
            manifest,
            envelope,
            plan,
            request=request,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
        ):
            return False
        return _projection_authority_matches_request_context(
            _projection_authority_from_plan(plan),
            request,
            envelope,
        )
    if _aggregate_manifest_has_no_authoritative_plan(manifest):
        return False
    if _aggregate_manifest_has_valid_plan_input(manifest):
        return False
    authority = manifest.get("projection-authority")
    return (
        isinstance(authority, Mapping)
        and _manifest_projection_authority_input_bound(
            manifest,
            envelope,
            authority,
        )
        and _projection_authority_matches_request_context(
            authority,
            request,
            envelope,
        )
    )


def _aggregate_manifest_has_authoritative_execution_manifest(
    manifest: Mapping[str, object],
    envelope: CommonEnvelope | None,
    execution_batch_manifest: Mapping[str, object] | None,
) -> bool:
    if execution_batch_manifest is None:
        return _aggregate_manifest_has_valid_execution_batch_manifest_input(
            manifest
        )
    return _input_artifact_authorizes_supplied_document(
        manifest,
        "execution-batch-manifest",
        envelope,
        ci_validation_execution_batch_manifest_payload_digest(
            execution_batch_manifest
        ),
    )


def _supplied_plan_input_authorizes_projection(  # noqa: PLR0913
    manifest: Mapping[str, object],
    envelope: CommonEnvelope | None,
    plan: Mapping[str, object],
    *,
    request: Mapping[str, object] | None = None,
    changed_files_snapshot: Mapping[str, object] | None = None,
    fact_snapshot: Mapping[str, object] | None = None,
) -> bool:
    try:
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            expected_run_id=envelope.run_id if envelope is not None else None,
            expected_run_attempt=(
                envelope.run_attempt if envelope is not None else None
            ),
        )
    except ContractValidationError:
        return False
    return (
        _input_artifact_authorizes_supplied_document(
            manifest,
            "validation-plan",
            envelope,
            _verified_plan_digest_or_none(plan),
        )
        and _supplied_request_context_authorizes_projection(
            manifest,
            envelope,
            plan,
            request,
        )
        and _supplied_snapshot_contexts_authorize_projection(
            manifest,
            envelope,
            plan,
            changed_files_snapshot,
            fact_snapshot,
        )
    )


def _supplied_plan_input_authorizes_retained_invalid_plan_projection(  # noqa: PLR0913
    manifest: Mapping[str, object],
    envelope: CommonEnvelope | None,
    plan: Mapping[str, object],
    *,
    request: Mapping[str, object] | None = None,
    changed_files_snapshot: Mapping[str, object] | None = None,
    fact_snapshot: Mapping[str, object] | None = None,
) -> bool:
    retained_detail = _invalid_plan_failure_detail_from_inputs(manifest)
    if not _invalid_plan_detail_allows_retained_plan_context(retained_detail):
        return False
    culprit_input_name = _retained_projection_culprit_input_name(
        cast("str", retained_detail)
    )
    request_authorized = _supplied_request_context_authorizes_projection(
        manifest,
        envelope,
        plan,
        request,
    )
    return (
        _retained_projection_validation_plan_input_authorizes(
            manifest,
            envelope,
            _retained_invalid_plan_digest_or_none(plan),
            culprit_input_name=culprit_input_name,
            retained_detail=cast("str", retained_detail),
        )
        and request_authorized
        and _supplied_retained_invalid_snapshot_contexts_authorize_projection(
            manifest,
            envelope,
            plan,
            changed_files_snapshot,
            fact_snapshot,
            culprit_input_name=culprit_input_name,
            retained_detail=cast("str", retained_detail),
        )
    )


def _supplied_retained_invalid_snapshot_contexts_authorize_projection(  # noqa: PLR0913
    manifest: Mapping[str, object],
    envelope: CommonEnvelope | None,
    plan: Mapping[str, object],
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    *,
    culprit_input_name: str,
    retained_detail: str,
) -> bool:
    for input_name in _plan_required_snapshot_inputs(plan):
        supplied_snapshot = (
            changed_files_snapshot
            if input_name == "changed-files-snapshot"
            else fact_snapshot
        )
        expected_digest = _retained_invalid_plan_snapshot_digest(
            plan,
            input_name,
        )
        if input_name == culprit_input_name:
            if not _retained_projection_culprit_input_artifact_matches(
                manifest,
                input_name,
                envelope,
                expected_digest,
                retained_detail,
            ):
                return False
            continue
        digest = _retained_invalid_snapshot_document_digest(
            input_name,
            supplied_snapshot,
            envelope,
            plan,
        )
        if digest is None:
            if supplied_snapshot is not None:
                return False
            if not _retained_projection_companion_input_artifact_matches(
                manifest,
                input_name,
                envelope,
                expected_digest,
            ) and not _retained_projection_snapshot_input_unavailable(
                manifest,
                input_name,
            ):
                return False
            continue
        if _aggregate_input_admissibility(manifest, input_name) == "valid":
            if not _input_artifact_authorizes_supplied_document(
                manifest,
                input_name,
                envelope,
                digest,
            ):
                return False
        elif not _retained_projection_companion_input_artifact_matches(
            manifest,
            input_name,
            envelope,
            expected_digest,
        ) and not _retained_projection_snapshot_input_unavailable(
            manifest,
            input_name,
        ):
            return False
    return True


def _retained_projection_culprit_input_name(detail: str) -> str:
    if detail.startswith("changed-files-snapshot-"):
        return "changed-files-snapshot"
    if detail.startswith("fact-snapshot-"):
        return "fact-snapshot"
    return "validation-plan"


def _retained_projection_snapshot_input_unavailable(
    manifest: Mapping[str, object],
    input_name: str,
) -> bool:
    inputs = manifest.get("input-artifacts")
    artifact = inputs.get(input_name) if isinstance(inputs, Mapping) else None
    return isinstance(artifact, Mapping) and (
        _snapshot_input_has_unproven_companion_identity(input_name, artifact)
    )


def _snapshot_input_has_unproven_companion_identity(
    input_name: str,
    artifact: Mapping[str, object],
) -> bool:
    diagnostics = artifact.get("diagnostics")
    return (
        input_name in {"changed-files-snapshot", "fact-snapshot"}
        and artifact.get("required") is True
        and artifact.get("expected-cardinality") == 1
        and artifact.get("admissibility") in {"missing", "inadmissible"}
        and artifact.get("artifact-ref") is None
        and artifact.get("artifact-instance-id") is None
        and artifact.get("content-digest") is None
        and isinstance(diagnostics, Sequence)
        and not isinstance(diagnostics, str | bytes)
        and len(diagnostics) == 1
        and isinstance(diagnostics[0], Mapping)
        and _snapshot_companion_unproven_diagnostic_is_canonical(
            cast("Mapping[str, object]", diagnostics[0]),
            expected_source_id=None,
        )
    )


_SNAPSHOT_COMPANION_UNPROVEN_DIAGNOSTIC_KEYS = frozenset(
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


def _snapshot_companion_unproven_diagnostic_is_canonical(
    diagnostic: Mapping[str, object],
    *,
    expected_source_id: object,
) -> bool:
    source = diagnostic.get("source")
    return (
        set(diagnostic) == _SNAPSHOT_COMPANION_UNPROVEN_DIAGNOSTIC_KEYS
        and diagnostic.get("diagnostic-id")
        == "required-input-artifact-failure/snapshot-companion-unproven"
        and diagnostic.get("code")
        == DiagnosticFamily.REQUIRED_INPUT_ARTIFACT_FAILURE.value
        and diagnostic.get("detail")
        == DiagnosticDetail.SNAPSHOT_COMPANION_UNPROVEN.value
        and isinstance(diagnostic.get("message"), str)
        and diagnostic.get("severity") == DiagnosticSeverity.FAIL_CLOSED.value
        and diagnostic.get("verdict-effect")
        == DiagnosticVerdictEffect.FAIL_CLOSED.value
        and isinstance(source, Mapping)
        and set(source) == {"type", "id"}
        and source.get("type") == "aggregation"
        and source.get("id") == expected_source_id
    )


def _retained_invalid_snapshot_document_digest(
    input_name: str,
    supplied_snapshot: Mapping[str, object] | None,
    envelope: CommonEnvelope | None,
    plan: Mapping[str, object],
) -> str | None:
    issues: list[ValidationIssue] = []
    if input_name == "changed-files-snapshot":
        digest = _validated_changed_files_snapshot_hash_or_none(
            supplied_snapshot,
            envelope,
            issues,
        )
    else:
        expected_plan_id = plan.get("plan-id")
        digest = _validated_fact_snapshot_id_or_none(
            supplied_snapshot,
            envelope,
            issues,
            expected_plan_id=expected_plan_id
            if isinstance(expected_plan_id, str)
            else None,
        )
    return None if issues else digest


def _retained_invalid_plan_snapshot_digest(
    plan: Mapping[str, object],
    input_name: str,
) -> str | None:
    if input_name == "changed-files-snapshot":
        affected_range = plan.get("affected-range")
        digest = (
            affected_range.get("changed-files-hash")
            if isinstance(affected_range, Mapping)
            else None
        )
        return digest if isinstance(digest, str) else None
    if input_name == "fact-snapshot":
        fact_snapshot = plan.get("fact-snapshot")
        digest = (
            fact_snapshot.get("id")
            if isinstance(fact_snapshot, Mapping)
            else None
        )
        return digest if isinstance(digest, str) else None
    return None


def _retained_projection_companion_input_artifact_matches(
    manifest: Mapping[str, object],
    input_name: str,
    envelope: CommonEnvelope | None,
    expected_digest: str | None,
) -> bool:
    artifact = _retained_projection_bound_input_artifact(
        manifest,
        input_name,
        envelope,
        expected_digest,
        admissibility="valid",
    )
    if artifact is not None and artifact.get("diagnostics") == []:
        return True
    if input_name not in {"changed-files-snapshot", "fact-snapshot"}:
        return False
    artifact = _retained_projection_bound_input_artifact(
        manifest,
        input_name,
        envelope,
        expected_digest,
        admissibility="inadmissible",
    )
    diagnostics = artifact.get("diagnostics") if artifact is not None else None
    return (
        isinstance(diagnostics, Sequence)
        and not isinstance(diagnostics, str | bytes)
        and artifact is not None
        and any(
            isinstance(diagnostic, Mapping)
            and diagnostic.get("code") == DiagnosticFamily.INVALID_PLAN.value
            and _input_artifact_invalid_plan_diagnostic_is_canonical(
                input_name,
                artifact,
                diagnostic,
            )
            for diagnostic in diagnostics
        )
    )


def _retained_projection_culprit_input_artifact_matches(
    manifest: Mapping[str, object],
    input_name: str,
    envelope: CommonEnvelope | None,
    expected_digest: str | None,
    retained_detail: str,
) -> bool:
    artifact = (
        _retained_projection_malformed_culprit_input_artifact(
            manifest,
            input_name,
            envelope,
        )
        if retained_detail == f"{input_name}-malformed"
        else _retained_projection_bound_input_artifact(
            manifest,
            input_name,
            envelope,
            expected_digest,
            admissibility="inadmissible",
        )
    )
    if artifact is None:
        return False
    diagnostics = artifact.get("diagnostics")
    if not isinstance(diagnostics, Sequence) or isinstance(
        diagnostics, str | bytes
    ):
        return False
    invalid_plan_diagnostics = [
        diagnostic
        for diagnostic in diagnostics
        if isinstance(diagnostic, Mapping)
        and diagnostic.get("code") == DiagnosticFamily.INVALID_PLAN.value
    ]
    matching_diagnostics = [
        diagnostic
        for diagnostic in invalid_plan_diagnostics
        if diagnostic.get("detail") == retained_detail
        and _input_artifact_invalid_plan_diagnostic_is_canonical(
            input_name,
            artifact,
            diagnostic,
        )
    ]
    return len(matching_diagnostics) == 1


def _retained_projection_malformed_culprit_input_artifact(
    manifest: Mapping[str, object],
    input_name: str,
    envelope: CommonEnvelope | None,
) -> Mapping[str, object] | None:
    if envelope is None:
        return None
    inputs = manifest.get("input-artifacts")
    artifact = inputs.get(input_name) if isinstance(inputs, Mapping) else None
    if not isinstance(artifact, Mapping):
        return None
    if (
        artifact.get("required") is True
        and artifact.get("expected-cardinality") == 1
        and artifact.get("admissibility") == "inadmissible"
        and isinstance(artifact.get("artifact-instance-id"), str)
        and artifact.get("artifact-instance-id") != ""
        and isinstance(artifact.get("content-digest"), str)
        and artifact.get("content-digest") != ""
        and artifact.get("artifact-ref")
        == _expected_input_artifact_ref(input_name, envelope)
    ):
        return artifact
    return None


def _retained_projection_bound_input_artifact(
    manifest: Mapping[str, object],
    input_name: str,
    envelope: CommonEnvelope | None,
    expected_digest: str | None,
    *,
    admissibility: str,
) -> Mapping[str, object] | None:
    if envelope is None or expected_digest is None:
        return None
    inputs = manifest.get("input-artifacts")
    artifact = inputs.get(input_name) if isinstance(inputs, Mapping) else None
    if not isinstance(artifact, Mapping):
        return None
    if (
        artifact.get("required") is True
        and artifact.get("expected-cardinality") == 1
        and artifact.get("admissibility") == admissibility
        and isinstance(artifact.get("artifact-instance-id"), str)
        and artifact.get("artifact-instance-id") != ""
        and artifact.get("content-digest") == expected_digest
        and artifact.get("artifact-ref")
        == _expected_input_artifact_ref(input_name, envelope)
    ):
        return artifact
    return None


def _invalid_plan_input_failure_input_names(
    aggregate_manifest: Mapping[str, object],
) -> set[str]:
    input_artifacts = aggregate_manifest.get("input-artifacts")
    if not isinstance(input_artifacts, Mapping):
        return set()
    result: set[str] = set()
    for input_name in (
        "validation-plan",
        "changed-files-snapshot",
        "fact-snapshot",
    ):
        item = input_artifacts.get(input_name)
        if not isinstance(item, Mapping) or item.get("required") is not True:
            continue
        if item.get("admissibility") == "valid":
            continue
        if _input_artifact_invalid_plan_diagnostic_details_for_name(
            input_name,
            item,
        ):
            result.add(input_name)
    return result


def _retained_projection_validation_plan_input_authorizes(
    manifest: Mapping[str, object],
    envelope: CommonEnvelope | None,
    expected_digest: str | None,
    *,
    culprit_input_name: str,
    retained_detail: str,
) -> bool:
    if culprit_input_name == "validation-plan":
        return _retained_projection_culprit_input_artifact_matches(
            manifest,
            "validation-plan",
            envelope,
            expected_digest,
            retained_detail,
        )
    return _retained_projection_companion_input_artifact_matches(
        manifest,
        "validation-plan",
        envelope,
        expected_digest,
    )


def _retained_invalid_plan_digest_or_none(
    plan: Mapping[str, object],
) -> str | None:
    try:
        return ci_validation_plan_digest(plan)
    except (TypeError, ValueError):
        return None


def _supplied_request_context_authorizes_projection(
    manifest: Mapping[str, object],
    envelope: CommonEnvelope | None,
    plan: Mapping[str, object],
    request: Mapping[str, object] | None,
) -> bool:
    if (
        request is None
        or envelope is None
        or not _projection_authority_matches_request_context(
            _projection_authority_from_plan(plan),
            request,
            envelope,
        )
    ):
        return False
    try:
        normalized = validate_ci_validation_request(
            request,
            expected_run_id=envelope.run_id,
            expected_run_attempt=envelope.run_attempt,
        )
    except ContractValidationError:
        return False
    return _input_artifact_authorizes_supplied_document(
        manifest,
        "request",
        envelope,
        normalized.request_digest,
    )


def _supplied_snapshot_contexts_authorize_projection(
    manifest: Mapping[str, object],
    envelope: CommonEnvelope | None,
    plan: Mapping[str, object],
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
) -> bool:
    for input_name in _plan_required_snapshot_inputs(plan):
        issues: list[ValidationIssue] = []
        if input_name == "changed-files-snapshot":
            digest = _validated_changed_files_snapshot_hash_or_none(
                changed_files_snapshot,
                envelope,
                issues,
            )
        else:
            expected_plan_id = plan.get("plan-id")
            digest = _validated_fact_snapshot_id_or_none(
                fact_snapshot,
                envelope,
                issues,
                expected_plan_id=expected_plan_id
                if isinstance(expected_plan_id, str)
                else None,
            )
        if issues or digest is None:
            return False
        if not _input_artifact_authorizes_supplied_document(
            manifest,
            input_name,
            envelope,
            digest,
        ):
            return False
    return True


def _aggregate_input_admissibility(
    manifest: Mapping[str, object],
    input_name: str,
) -> object:
    inputs = manifest.get("input-artifacts")
    if not isinstance(inputs, Mapping):
        return None
    artifact = inputs.get(input_name)
    if not isinstance(artifact, Mapping):
        return None
    return artifact.get("admissibility")


def _fact_snapshot_context_id_for_authority(
    fact_snapshot: Mapping[str, object] | None,
) -> str | None:
    if fact_snapshot is None:
        return None
    snapshot_id = fact_snapshot.get("fact-snapshot-id")
    return snapshot_id if isinstance(snapshot_id, str) else None


def _aggregate_context_input_proven_or_not_required(
    manifest: Mapping[str, object],
    input_name: str,
    envelope: CommonEnvelope | None,
    supplied_document: Mapping[str, object] | None,
    expected_digest: str | None,
) -> bool:
    if supplied_document is None:
        return _aggregate_input_admissibility(manifest, input_name) == (
            "not-required"
        )
    return _input_artifact_authorizes_supplied_document(
        manifest,
        input_name,
        envelope,
        expected_digest,
    )


def _input_artifact_authorizes_supplied_document(  # noqa: PLR0911
    manifest: Mapping[str, object],
    input_name: str,
    envelope: CommonEnvelope | None,
    expected_digest: str | None,
) -> bool:
    if envelope is None or expected_digest is None:
        return False
    inputs = manifest.get("input-artifacts")
    if not isinstance(inputs, Mapping):
        return False
    artifact = inputs.get(input_name)
    if not isinstance(artifact, Mapping):
        return False
    if artifact.get("required") is not True:
        return False
    if artifact.get("expected-cardinality") != 1:
        return False
    instance_id = artifact.get("artifact-instance-id")
    if not isinstance(instance_id, str) or instance_id == "":
        return False
    if artifact.get("admissibility") != "valid":
        return False
    digest = artifact.get("content-digest")
    if (
        not isinstance(digest, str)
        or _DIGEST_RE.fullmatch(digest) is None
        or digest != expected_digest
    ):
        return False
    expected_ref = _expected_input_artifact_ref(input_name, envelope)
    return artifact.get("artifact-ref") == expected_ref


def _valid_current_run_input_artifact(
    manifest: Mapping[str, object],
    envelope: CommonEnvelope | None,
    input_name: str,
) -> Mapping[str, object] | None:
    inputs = manifest.get("input-artifacts")
    artifact = inputs.get(input_name) if isinstance(inputs, Mapping) else None
    if (
        envelope is None
        or not isinstance(artifact, Mapping)
        or artifact.get("required") is not True
        or artifact.get("expected-cardinality") != 1
        or artifact.get("admissibility") != "valid"
        or not isinstance(artifact.get("content-digest"), str)
        or not isinstance(artifact.get("artifact-instance-id"), str)
        or artifact.get("artifact-instance-id") == ""
    ):
        return None
    expected_ref = _expected_input_artifact_ref(input_name, envelope)
    if artifact.get("artifact-ref") != expected_ref:
        return None
    return artifact


def _expected_input_artifact_ref(
    input_name: str,
    envelope: CommonEnvelope,
) -> str | None:
    refs = {
        "request": ci_validation_request_artifact_ref(
            run_id=envelope.run_id,
            run_attempt=envelope.run_attempt,
        ),
        "validation-plan": ci_validation_plan_artifact_ref(
            run_id=envelope.run_id,
            run_attempt=envelope.run_attempt,
        ),
        "changed-files-snapshot": (
            ci_validation_changed_files_snapshot_artifact_ref(
                run_id=envelope.run_id,
                run_attempt=envelope.run_attempt,
            )
        ),
        "fact-snapshot": ci_validation_fact_snapshot_artifact_ref(
            run_id=envelope.run_id,
            run_attempt=envelope.run_attempt,
        ),
        "execution-batch-manifest": (
            ci_validation_execution_batch_manifest_artifact_ref(
                run_id=envelope.run_id,
                run_attempt=envelope.run_attempt,
            )
        ),
    }
    return refs.get(input_name)


def _validate_pre_final_validation_artifacts(
    value: object,
    input_artifact_count: int,
    batch_bundle_count: int,
    issues: list[ValidationIssue],
) -> None:
    _validate_non_negative_int(
        value,
        "$.pre-final-validation-artifacts",
        issues,
    )
    if not isinstance(value, int) or isinstance(value, bool):
        return
    expected = input_artifact_count + batch_bundle_count
    if value != expected:
        issues.append(
            ValidationIssue(
                "$.pre-final-validation-artifacts",
                "must equal input artifacts plus batch bundle slots",
            )
        )
    if value > _MAX_PREFINAL_VALIDATION_ARTIFACTS:
        issues.append(
            ValidationIssue(
                "$.pre-final-validation-artifacts",
                "must leave two final artifact slots",
            )
        )


def _validate_observed_candidates(  # noqa: C901, PLR0912, PLR0913
    value: object,
    admitted_candidate_id: object,
    slot_admissibility: object,
    envelope: CommonEnvelope | None,
    batch_id: object,
    artifact_ref: object,
    parent_path: str,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        issues.append(
            ValidationIssue(
                f"{parent_path}.observed-candidates", "must be an array"
            ),
        )
        return
    previous: str | None = None
    seen: set[str] = set()
    admitted_matches = 0
    for index, item in enumerate(value):
        path = f"{parent_path}.observed-candidates[{index}]"
        if not isinstance(item, Mapping):
            issues.append(ValidationIssue(path, "must be an object"))
            continue
        _validate_root_keys(item, _CANDIDATE_KEYS, path, issues)
        _validate_non_empty_string(
            item.get("candidate-id"), f"{path}.candidate-id", issues
        )
        current = item.get("candidate-id")
        if isinstance(current, str):
            if previous is not None and previous > current:
                issues.append(
                    ValidationIssue(
                        f"{parent_path}.observed-candidates",
                        "must be sorted",
                    ),
                )
            if current in seen:
                issues.append(
                    ValidationIssue(
                        f"{parent_path}.observed-candidates",
                        "candidate ids must be unique",
                    )
                )
            seen.add(current)
            previous = current
            if current == admitted_candidate_id:
                admitted_matches += 1
                if item.get("producer-verification") != "verified":
                    issues.append(
                        ValidationIssue(
                            f"{path}.producer-verification",
                            "admitted candidate must be verified",
                        )
                    )
                if item.get("payload-readable") is not True:
                    issues.append(
                        ValidationIssue(
                            f"{path}.payload-readable",
                            "admitted candidate must be readable",
                        )
                    )
                if item.get("admissibility") != "valid":
                    issues.append(
                        ValidationIssue(
                            f"{path}.admissibility",
                            "admitted candidate must be valid",
                        )
                    )
                _validate_non_empty_string(
                    item.get("artifact-instance-id"),
                    f"{path}.artifact-instance-id",
                    issues,
                )
                _validate_digest(
                    item.get("content-digest"),
                    f"{path}.content-digest",
                    issues,
                )
            artifact_instance_id = item.get("artifact-instance-id")
            if (
                envelope is not None
                and isinstance(batch_id, str)
                and isinstance(artifact_ref, str)
                and (
                    artifact_instance_id is None
                    or isinstance(artifact_instance_id, str)
                )
            ):
                expected_candidate_id = (
                    ci_validation_batch_evidence_candidate_id(
                        run_id=envelope.run_id,
                        run_attempt=envelope.run_attempt,
                        batch_id=batch_id,
                        artifact_ref=artifact_ref,
                        artifact_instance_id=artifact_instance_id,
                        physical_artifact_name=artifact_physical_name(
                            artifact_ref
                        ),
                    )
                )
                if current != expected_candidate_id:
                    issues.append(
                        ValidationIssue(
                            f"{path}.candidate-id",
                            "must bind run, batch, ref, instance, and name",
                        )
                    )
        _validate_nullable_non_empty_string(
            item.get("artifact-instance-id"),
            f"{path}.artifact-instance-id",
            issues,
        )
        _validate_nullable_digest(
            item.get("content-digest"), f"{path}.content-digest", issues
        )
        if item.get("producer-verification") not in {
            "verified",
            "producer-unverified",
            "wrong-producer",
            "not-checked",
        }:
            issues.append(
                ValidationIssue(
                    f"{path}.producer-verification",
                    "is not registered",
                ),
            )
        if not isinstance(item.get("payload-readable"), bool):
            issues.append(
                ValidationIssue(f"{path}.payload-readable", "must be boolean")
            )
        if item.get("admissibility") not in {"valid", "inadmissible"}:
            issues.append(
                ValidationIssue(f"{path}.admissibility", "is not registered")
            )
        _validate_diagnostics(
            item.get("diagnostics"), f"{path}.diagnostics", issues
        )
    if slot_admissibility == "valid":
        if (
            not isinstance(admitted_candidate_id, str)
            or admitted_candidate_id == ""
        ):
            issues.append(
                ValidationIssue(
                    f"{parent_path}.admitted-candidate-id",
                    "must identify the admitted valid candidate",
                )
            )
        elif admitted_matches != 1:
            issues.append(
                ValidationIssue(
                    f"{parent_path}.admitted-candidate-id",
                    "must match exactly one observed candidate",
                )
            )
    elif admitted_candidate_id is not None:
        issues.append(
            ValidationIssue(
                f"{parent_path}.admitted-candidate-id",
                "must be null unless slot-admissibility is valid",
            )
        )


def _validate_no_valid_candidates_without_authority(
    value: object,
    parent_path: str,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        return
    for index, item in enumerate(value):
        if isinstance(item, Mapping) and item.get("admissibility") == "valid":
            issues.append(
                ValidationIssue(
                    f"{parent_path}.observed-candidates[{index}].admissibility",
                    "requires authoritative plan or projection authority",
                )
            )


def _validate_unexpected_artifacts(
    value: object,
    envelope: CommonEnvelope | None,
    issues: list[ValidationIssue],
) -> int:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        issues.append(
            ValidationIssue(
                "$.unexpected-contract-artifacts",
                "must be an array",
            ),
        )
        return 0
    previous: str | None = None
    seen: set[str] = set()
    for index, item in enumerate(value):
        path = f"$.unexpected-contract-artifacts[{index}]"
        if not isinstance(item, Mapping):
            issues.append(ValidationIssue(path, "must be an object"))
            continue
        _validate_root_keys_with_optional(
            item,
            _UNEXPECTED_KEYS,
            frozenset({"observed-physical-artifact-name"}),
            path,
            issues,
        )
        if envelope is not None:
            current = _unexpected_implicit_id(
                item,
                run_id=envelope.run_id,
                run_attempt=envelope.run_attempt,
            )
            if previous is not None and previous > current:
                issues.append(
                    ValidationIssue(
                        "$.unexpected-contract-artifacts",
                        "must be sorted",
                    )
                )
            if current in seen:
                issues.append(
                    ValidationIssue(
                        "$.unexpected-contract-artifacts",
                        "implicit ids must be unique",
                    )
                )
            seen.add(current)
            previous = current
        try:
            validate_artifact_physical_name(item.get("physical-artifact-name"))
        except ContractValidationError:
            issues.append(
                ValidationIssue(
                    f"{path}.physical-artifact-name",
                    "must be an attempt-visible three-ci-validation physical "
                    "artifact name",
                ),
            )
        _validate_nullable_non_empty_string(
            item.get("artifact-instance-id"),
            f"{path}.artifact-instance-id",
            issues,
        )
        observed_physical_name = item.get("observed-physical-artifact-name")
        if observed_physical_name is not None:
            _validate_nullable_non_empty_string(
                observed_physical_name,
                f"{path}.observed-physical-artifact-name",
                issues,
            )
        if item.get("classification") not in {
            "unexpected",
            "unreadable",
            "wrong-ref",
            "wrong-producer",
        }:
            issues.append(
                ValidationIssue(f"{path}.classification", "is not registered")
            )
        _validate_diagnostics(
            item.get("diagnostics"), f"{path}.diagnostics", issues
        )
    return len(value)


def _validate_namespace_overflow(
    value: object,
    observed_lower_bound: int,
    issues: list[ValidationIssue],
) -> None:
    overflow = _validate_object(
        value,
        _NAMESPACE_OVERFLOW_KEYS,
        "$.namespace-overflow",
        issues,
    )
    if overflow is None:
        return
    if not isinstance(overflow.get("detected"), bool):
        issues.append(
            ValidationIssue("$.namespace-overflow.detected", "must be boolean")
        )
    for key in (
        "observed-prefixed-artifact-count-lower-bound",
        "max-prefixed-validation-artifacts",
    ):
        _validate_non_negative_int(
            overflow.get(key),
            f"$.namespace-overflow.{key}",
            issues,
        )
    observed = overflow.get("observed-prefixed-artifact-count-lower-bound")
    maximum = overflow.get("max-prefixed-validation-artifacts")
    if (
        isinstance(observed, int)
        and not isinstance(observed, bool)
        and observed < observed_lower_bound
    ):
        issues.append(
            ValidationIssue(
                "$.namespace-overflow."
                "observed-prefixed-artifact-count-lower-bound",
                "must cover expected and unexpected pre-final artifacts",
            )
        )
    if maximum != _MAX_PREFINAL_VALIDATION_ARTIFACTS:
        issues.append(
            ValidationIssue(
                "$.namespace-overflow.max-prefixed-validation-artifacts",
                f"must be {_MAX_PREFINAL_VALIDATION_ARTIFACTS}",
            )
        )
    if (
        isinstance(observed, int)
        and max(observed, observed_lower_bound)
        > _MAX_PREFINAL_VALIDATION_ARTIFACTS
        and overflow.get("detected") is not True
    ):
        issues.append(
            ValidationIssue(
                "$.namespace-overflow.detected",
                "must be true when observed lower bound exceeds max",
            )
        )
    _validate_diagnostics(
        overflow.get("diagnostics"),
        "$.namespace-overflow.diagnostics",
        issues,
    )


def _validate_summary_manifest_claim(  # noqa: PLR0913
    value: object,
    envelope: CommonEnvelope | None,
    aggregate_evidence_manifest: Mapping[str, object] | None,
    *,
    require_non_authoritative_manifest: bool,
    skip_manifest_digest_match: bool,
    issues: list[ValidationIssue],
) -> Mapping[str, object] | None:
    claim = _validate_object(
        value,
        frozenset({"artifact-ref", "artifact-instance-id", "content-digest"}),
        "$.aggregate-evidence-manifest",
        issues,
    )
    if claim is None:
        return None
    _validate_artifact_ref(
        claim.get("artifact-ref"),
        "$.aggregate-evidence-manifest.artifact-ref",
        issues,
    )
    if require_non_authoritative_manifest:
        for key in ("artifact-instance-id", "content-digest"):
            if claim.get(key) is not None:
                issues.append(
                    ValidationIssue(
                        f"$.aggregate-evidence-manifest.{key}",
                        "must be null without aggregate evidence manifest",
                    )
                )
    else:
        _validate_non_empty_string(
            claim.get("artifact-instance-id"),
            "$.aggregate-evidence-manifest.artifact-instance-id",
            issues,
        )
        _validate_digest(
            claim.get("content-digest"),
            "$.aggregate-evidence-manifest.content-digest",
            issues,
        )
    if envelope is not None:
        expected_ref = ci_validation_aggregate_evidence_manifest_artifact_ref(
            run_id=envelope.run_id,
            run_attempt=envelope.run_attempt,
        )
        if claim.get("artifact-ref") != expected_ref:
            issues.append(
                ValidationIssue(
                    "$.aggregate-evidence-manifest.artifact-ref",
                    "must match run",
                )
            )
    if aggregate_evidence_manifest is not None:
        expected_ref = aggregate_evidence_manifest.get("artifact-ref")
        if claim.get("artifact-ref") != expected_ref:
            issues.append(
                ValidationIssue(
                    "$.aggregate-evidence-manifest.artifact-ref",
                    "must match aggregate evidence manifest",
                ),
            )
        if not skip_manifest_digest_match and claim.get("content-digest") != (
            ci_validation_aggregate_evidence_manifest_payload_digest(
                aggregate_evidence_manifest,
            )
        ):
            issues.append(
                ValidationIssue(
                    "$.aggregate-evidence-manifest.content-digest",
                    "must match aggregate evidence manifest",
                ),
            )
    return claim


def _summary_bound_aggregate_manifest_digest_mismatch(
    summary: Mapping[str, object],
    aggregate_evidence_manifest: Mapping[str, object],
) -> bool:
    expected_digest = ci_validation_aggregate_evidence_manifest_payload_digest(
        aggregate_evidence_manifest,
    )
    manifest_claim = summary.get("aggregate-evidence-manifest")
    if (
        isinstance(manifest_claim, Mapping)
        and manifest_claim.get("content-digest") != expected_digest
    ):
        return True
    final_artifacts = summary.get("final-artifacts")
    final_manifest = (
        final_artifacts.get("aggregate-evidence-manifest")
        if isinstance(final_artifacts, Mapping)
        else None
    )
    return (
        isinstance(final_manifest, Mapping)
        and final_manifest.get("content-digest") != expected_digest
    )


def _validate_final_artifacts(  # noqa: C901,PLR0912,PLR0913
    value: object,
    envelope: CommonEnvelope | None,
    manifest_claim: Mapping[str, object] | None,
    aggregate_evidence_manifest: Mapping[str, object] | None,
    *,
    require_non_authoritative_manifest: bool,
    skip_manifest_digest_match: bool,
    issues: list[ValidationIssue],
) -> None:
    final_artifacts = _validate_object(
        value,
        _FINAL_ARTIFACT_KEYS,
        "$.final-artifacts",
        issues,
    )
    if final_artifacts is None:
        return
    manifest = _validate_object(
        final_artifacts.get("aggregate-evidence-manifest"),
        _FINAL_AGGREGATE_EVIDENCE_MANIFEST_ENTRY_KEYS,
        "$.final-artifacts.aggregate-evidence-manifest",
        issues,
    )
    summary = _validate_object(
        final_artifacts.get("aggregate-summary"),
        _FINAL_AGGREGATE_SUMMARY_ENTRY_KEYS,
        "$.final-artifacts.aggregate-summary",
        issues,
    )
    if manifest is not None:
        _validate_artifact_ref(
            manifest.get("artifact-ref"),
            "$.final-artifacts.aggregate-evidence-manifest.artifact-ref",
            issues,
        )
        if require_non_authoritative_manifest:
            for key in ("artifact-instance-id", "content-digest"):
                if manifest.get(key) is not None:
                    issues.append(
                        ValidationIssue(
                            "$.final-artifacts.aggregate-evidence-manifest."
                            f"{key}",
                            "must be null without aggregate evidence manifest",
                        )
                    )
            if manifest.get("producer-verified") is not False:
                issues.append(
                    ValidationIssue(
                        "$.final-artifacts.aggregate-evidence-manifest."
                        "producer-verified",
                        "must be false without bound aggregate evidence "
                        "manifest",
                    ),
                )
        else:
            _validate_non_empty_string(
                manifest.get("artifact-instance-id"),
                "$.final-artifacts.aggregate-evidence-manifest."
                "artifact-instance-id",
                issues,
            )
            _validate_digest(
                manifest.get("content-digest"),
                "$.final-artifacts.aggregate-evidence-manifest.content-digest",
                issues,
            )
            if manifest.get("producer-verified") not in {True, False}:
                issues.append(
                    ValidationIssue(
                        "$.final-artifacts.aggregate-evidence-manifest."
                        "producer-verified",
                        "must be a boolean",
                    ),
                )
        if "authority-diagnostics" in manifest:
            _validate_diagnostics(
                manifest.get("authority-diagnostics"),
                "$.final-artifacts.aggregate-evidence-manifest."
                "authority-diagnostics",
                issues,
            )
            _validate_aggregate_manifest_authority_diagnostic_details(
                manifest.get("authority-diagnostics"),
                "$.final-artifacts.aggregate-evidence-manifest."
                "authority-diagnostics",
                issues,
            )
        if manifest_claim is not None:
            for key in (
                "artifact-ref",
                "artifact-instance-id",
                "content-digest",
            ):
                if manifest.get(key) != manifest_claim.get(key):
                    issues.append(
                        ValidationIssue(
                            f"$.final-artifacts.aggregate-evidence-manifest.{key}",
                            "must match aggregate evidence manifest claim",
                        )
                    )
        if (
            aggregate_evidence_manifest is not None
            and not skip_manifest_digest_match
            and manifest.get("content-digest")
            != ci_validation_aggregate_evidence_manifest_payload_digest(
                aggregate_evidence_manifest
            )
        ):
            issues.append(
                ValidationIssue(
                    "$.final-artifacts.aggregate-evidence-manifest.content-digest",
                    "must match aggregate evidence manifest",
                )
            )
    if summary is not None:
        _validate_artifact_ref(
            summary.get("artifact-ref"),
            "$.final-artifacts.aggregate-summary.artifact-ref",
            issues,
        )
    if envelope is None:
        return
    if isinstance(manifest, Mapping) and manifest.get("artifact-ref") != (
        ci_validation_aggregate_evidence_manifest_artifact_ref(
            run_id=envelope.run_id,
            run_attempt=envelope.run_attempt,
        )
    ):
        issues.append(
            ValidationIssue(
                "$.final-artifacts.aggregate-evidence-manifest.artifact-ref",
                "must match run",
            ),
        )
    if isinstance(summary, Mapping) and summary.get(
        "artifact-ref"
    ) != ci_validation_aggregate_summary_artifact_ref(
        run_id=envelope.run_id,
        run_attempt=envelope.run_attempt,
    ):
        issues.append(
            ValidationIssue(
                "$.final-artifacts.aggregate-summary.artifact-ref",
                "must match run",
            ),
        )


def _validate_summary_budgets(  # noqa: C901
    value: object,
    issues: list[ValidationIssue],
) -> None:
    budgets = _validate_object(value, _SUMMARY_BUDGET_KEYS, "$.budgets", issues)
    if budgets is None:
        return
    for key in _SUMMARY_BUDGET_KEYS:
        _validate_non_negative_int(budgets.get(key), f"$.budgets.{key}", issues)
    if budgets.get("expected-final-validation-artifacts") != (
        _EXPECTED_FINAL_VALIDATION_ARTIFACTS
    ):
        issues.append(
            ValidationIssue(
                "$.budgets.expected-final-validation-artifacts",
                "must be 2",
            ),
        )
    if budgets.get("max-validation-artifacts") != _MAX_VALIDATION_ARTIFACTS:
        issues.append(
            ValidationIssue("$.budgets.max-validation-artifacts", "must be 20")
        )
    for key, maximum_value in (
        ("actual-execution-batches", _MAX_EXECUTION_BATCHES),
        ("actual-total-jobs", _MAX_TOTAL_JOBS),
        ("actual-windows-jobs", _MAX_WINDOWS_JOBS),
    ):
        current = budgets.get(key)
        if isinstance(current, int) and current > maximum_value:
            issues.append(
                ValidationIssue(
                    f"$.budgets.{key}",
                    f"must be at most {maximum_value}",
                )
            )
    actual_total = budgets.get("actual-total-jobs")
    actual_windows = budgets.get("actual-windows-jobs")
    if (
        isinstance(actual_windows, int)
        and isinstance(actual_total, int)
        and actual_windows > actual_total
    ):
        issues.append(
            ValidationIssue(
                "$.budgets.actual-windows-jobs",
                "must not exceed actual total jobs",
            )
        )
    pre_final = budgets.get("pre-final-validation-artifacts")
    expected_final = budgets.get("expected-final-validation-artifacts")
    expected_actual = budgets.get("expected-actual-validation-artifacts")
    if (
        isinstance(pre_final, int)
        and isinstance(expected_final, int)
        and expected_actual != pre_final + expected_final
    ):
        issues.append(
            ValidationIssue(
                "$.budgets.expected-actual-validation-artifacts",
                "must equal pre-final plus reserved final artifacts",
            ),
        )
    if isinstance(pre_final, int) and pre_final > (
        _MAX_VALIDATION_ARTIFACTS - _EXPECTED_FINAL_VALIDATION_ARTIFACTS
    ):
        issues.append(
            ValidationIssue(
                "$.budgets.pre-final-validation-artifacts",
                "must leave two final artifact slots",
            )
        )
    if isinstance(expected_actual, int) and expected_actual > (
        _MAX_VALIDATION_ARTIFACTS
    ):
        issues.append(
            ValidationIssue(
                "$.budgets.expected-actual-validation-artifacts",
                "must be at most 20",
            )
        )
    target = budgets.get("aggregate-target-duration-seconds")
    maximum = budgets.get("aggregate-max-duration-seconds")
    if isinstance(maximum, int) and maximum > _AGGREGATE_MAX_DURATION_SECONDS:
        issues.append(
            ValidationIssue(
                "$.budgets.aggregate-max-duration-seconds",
                "must be at most 120",
            ),
        )
    if (
        isinstance(target, int)
        and isinstance(maximum, int)
        and target > maximum
    ):
        issues.append(
            ValidationIssue(
                "$.budgets.aggregate-target-duration-seconds",
                "must not exceed aggregate max duration",
            ),
        )


def _validate_summary_bundles(
    value: object,
    envelope: CommonEnvelope | None,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        issues.append(ValidationIssue("$.batch-bundles", "must be an array"))
        return
    previous: tuple[str, str, str, str, str] | None = None
    sort_keys: list[tuple[str, str, str, str, str]] = []
    batch_ids: set[str] = set()
    for index, item in enumerate(value):
        path = f"$.batch-bundles[{index}]"
        if not isinstance(item, Mapping):
            issues.append(ValidationIssue(path, "must be an object"))
            continue
        current = _summary_batch_bundle_sort_key(item)
        sort_keys.append(current)
        if previous is not None and previous > current:
            issues.append(ValidationIssue("$.batch-bundles", "must be sorted"))
        previous = current
        _validate_root_keys(item, _SUMMARY_BUNDLE_KEYS, path, issues)
        _validate_summary_bundle_identity(
            item,
            path,
            envelope,
            batch_ids,
            issues,
        )
        _validate_nullable_non_empty_string(
            item.get("bundle-id"), f"{path}.bundle-id", issues
        )
        _validate_nullable_non_empty_string(
            item.get("admitted-candidate-id"),
            f"{path}.admitted-candidate-id",
            issues,
        )
        _validate_non_negative_int(
            item.get("candidate-count"), f"{path}.candidate-count", issues
        )
        if item.get("admissibility") not in _BUNDLE_ADMISSIBILITIES:
            issues.append(
                ValidationIssue(f"{path}.admissibility", "is not registered")
            )
        _validate_diagnostics(
            item.get("diagnostics"), f"{path}.diagnostics", issues
        )
    if len(sort_keys) != len(set(sort_keys)):
        issues.append(ValidationIssue("$.batch-bundles", "must be unique"))


def _validate_summary_bundle_ids_match_execution_manifest(
    value: object,
    execution_batch_manifest: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> None:
    if execution_batch_manifest is None:
        return
    batches = execution_batch_manifest.get("batches")
    if (
        not isinstance(value, Sequence)
        or isinstance(value, str | bytes)
        or not isinstance(batches, Sequence)
        or isinstance(batches, str | bytes)
    ):
        return
    summary_batch_ids = [
        item.get("batch-id") for item in value if isinstance(item, Mapping)
    ]
    manifest_batch_ids = [
        item.get("batch-id") for item in batches if isinstance(item, Mapping)
    ]
    if summary_batch_ids != manifest_batch_ids:
        issues.append(
            ValidationIssue(
                "$.batch-bundles",
                "must match execution-batch manifest batch ids exactly",
            )
        )


def _validate_summary_bundle_identity(
    item: Mapping[str, object],
    path: str,
    envelope: CommonEnvelope | None,
    batch_ids: set[str],
    issues: list[ValidationIssue],
) -> None:
    batch_id = item.get("batch-id")
    _validate_local_id(batch_id, f"{path}.batch-id", issues)
    if isinstance(batch_id, str):
        if batch_id in batch_ids:
            issues.append(
                ValidationIssue("$.batch-bundles.batch-id", "must be unique")
            )
        batch_ids.add(batch_id)
    _validate_artifact_ref(
        item.get("artifact-ref"), f"{path}.artifact-ref", issues
    )
    if envelope is None or not isinstance(batch_id, str):
        return
    expected_ref = ci_validation_batch_evidence_bundle_artifact_ref(
        run_id=envelope.run_id,
        run_attempt=envelope.run_attempt,
        batch_id=batch_id,
    )
    if item.get("artifact-ref") != expected_ref:
        issues.append(
            ValidationIssue(
                f"{path}.artifact-ref",
                "must match run and batch",
            )
        )


def _validate_no_summary_admitted_candidates_without_manifest(
    summary: Mapping[str, object],
    aggregate_manifest: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> None:
    if aggregate_manifest is not None:
        return
    rows = summary.get("batch-bundles")
    if not isinstance(rows, Sequence) or isinstance(rows, str | bytes):
        return
    for index, row in enumerate(rows):
        if (
            isinstance(row, Mapping)
            and row.get("admitted-candidate-id") is not None
        ):
            issues.append(
                ValidationIssue(
                    f"$.batch-bundles[{index}].admitted-candidate-id",
                    "requires aggregate evidence manifest",
                )
            )


def _validate_no_summary_satisfied_evidence_without_manifest(
    summary: Mapping[str, object],
    aggregate_manifest: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> None:
    if aggregate_manifest is not None:
        return
    rows = summary.get("evidence-results")
    if not isinstance(rows, Sequence) or isinstance(rows, str | bytes):
        return
    for index, row in enumerate(rows):
        if isinstance(row, Mapping) and row.get("outcome") == "satisfied":
            issues.append(
                ValidationIssue(
                    f"$.evidence-results[{index}].outcome",
                    "requires aggregate evidence manifest",
                )
            )


def _validate_summary_evidence_results(
    value: object,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        issues.append(ValidationIssue("$.evidence-results", "must be an array"))
        return
    previous: str | None = None
    for index, item in enumerate(value):
        path = f"$.evidence-results[{index}]"
        if not isinstance(item, Mapping):
            issues.append(ValidationIssue(path, "must be an object"))
            continue
        _validate_root_keys(item, _SUMMARY_EVIDENCE_RESULT_KEYS, path, issues)
        _validate_local_id(
            item.get("evidence-expectation-id"),
            f"{path}.evidence-expectation-id",
            issues,
        )
        current = item.get("evidence-expectation-id")
        if isinstance(current, str):
            if previous is not None and previous > current:
                issues.append(
                    ValidationIssue("$.evidence-results", "must be sorted")
                )
            previous = current
        _validate_local_id(
            item.get("work-group-id"), f"{path}.work-group-id", issues
        )
        _validate_nullable_non_empty_string(
            item.get("batch-id"), f"{path}.batch-id", issues
        )
        _validate_nullable_non_empty_string(
            item.get("bundle-id"), f"{path}.bundle-id", issues
        )
        selector_index = item.get("selector-index")
        if selector_index is not None and (
            not isinstance(selector_index, int)
            or isinstance(selector_index, bool)
        ):
            issues.append(
                ValidationIssue(
                    f"{path}.selector-index",
                    "must be null or integer",
                ),
            )
        outcome = item.get("outcome")
        if outcome not in _RESULT_OUTCOMES:
            issues.append(
                ValidationIssue(f"{path}.outcome", "is not registered")
            )
        elif outcome == "missing":
            _validate_missing_evidence_result_provenance(item, path, issues)
        _validate_diagnostics(
            item.get("diagnostics"), f"{path}.diagnostics", issues
        )


def _validate_missing_evidence_result_provenance(
    item: Mapping[str, object],
    path: str,
    issues: list[ValidationIssue],
) -> None:
    for field in ("batch-id", "bundle-id", "selector-index"):
        if item.get(field) is not None:
            issues.append(
                ValidationIssue(
                    f"{path}.{field}",
                    "must be null for missing evidence",
                )
            )


def _validate_summary_failures(
    value: object,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        issues.append(ValidationIssue("$.failures", "must be an array"))
        return
    previous: tuple[str, str, str, str, str, str] | None = None
    for index, item in enumerate(value):
        path = f"$.failures[{index}]"
        if not isinstance(item, Mapping):
            issues.append(ValidationIssue(path, "must be an object"))
            continue
        current = _summary_failure_sort_key(item)
        if previous is not None and previous > current:
            issues.append(ValidationIssue("$.failures", "must be sorted"))
        previous = current
        _validate_root_keys(item, _SUMMARY_FAILURE_KEYS, path, issues)
        _validate_non_empty_string(item.get("kind"), f"{path}.kind", issues)
        if item.get("kind") not in _SUMMARY_FAILURE_KINDS:
            issues.append(ValidationIssue(f"{path}.kind", "is not registered"))
        _validate_nullable_non_empty_string(
            item.get("batch-id"), f"{path}.batch-id", issues
        )
        _validate_nullable_non_empty_string(
            item.get("work-group-id"),
            f"{path}.work-group-id",
            issues,
        )
        _validate_nullable_non_empty_string(
            item.get("evidence-expectation-id"),
            f"{path}.evidence-expectation-id",
            issues,
        )
        _validate_nullable_non_empty_string(
            item.get("bundle-id"), f"{path}.bundle-id", issues
        )
        diagnostic = item.get("diagnostic")
        if isinstance(diagnostic, Mapping):
            _validate_g1_diagnostic_record(
                diagnostic, f"{path}.diagnostic", issues
            )
        else:
            issues.append(
                ValidationIssue(f"{path}.diagnostic", "must be an object")
            )
        _validate_summary_failure_diagnostic_binding(item, path, issues)
        if item.get("kind") == "required-evidence-missing":
            for field in ("batch-id", "bundle-id"):
                if item.get(field) is not None:
                    issues.append(
                        ValidationIssue(
                            f"{path}.{field}",
                            "must be null for missing evidence failure",
                        )
                    )
        _validate_non_empty_string(
            item.get("message"), f"{path}.message", issues
        )


def _validate_invalid_plan_summary_failure_attribution(
    summary: Mapping[str, object],
    issues: list[ValidationIssue],
) -> None:
    failures = summary.get("failures")
    if not isinstance(failures, Sequence) or isinstance(failures, str | bytes):
        return
    protected_kinds = {
        "invalid-plan",
        "fail-closed",
        "final-evidence-failure",
        "final-producer-unverified",
    }
    attribution_fields = (
        "batch-id",
        "work-group-id",
        "evidence-expectation-id",
        "bundle-id",
    )
    for index, failure in enumerate(failures):
        if not isinstance(failure, Mapping):
            continue
        if failure.get("kind") not in protected_kinds:
            continue
        for field in attribution_fields:
            if failure.get(field) is not None:
                issues.append(
                    ValidationIssue(
                        f"$.failures[{index}].{field}",
                        "must be null for protected fail-closed failure rows",
                    )
                )


def _validate_summary_failure_diagnostic_binding(
    failure: Mapping[str, object],
    path: str,
    issues: list[ValidationIssue],
) -> None:
    kind = failure.get("kind")
    diagnostic = failure.get("diagnostic")
    if not isinstance(kind, str) or not isinstance(diagnostic, Mapping):
        return
    code = diagnostic.get("code")
    if not isinstance(code, str):
        return
    if kind in {
        "fail-closed",
        "final-evidence-failure",
        "final-producer-unverified",
    }:
        _validate_summary_fail_closed_diagnostic(diagnostic, path, issues)
    if kind == "fail-closed":
        return
    if code != kind:
        issues.append(
            ValidationIssue(
                f"{path}.diagnostic.code",
                "must match failure kind",
            )
        )
    source = diagnostic.get("source")
    if not isinstance(source, Mapping):
        return
    source_type = source.get("type")
    source_id = source.get("id")
    work_group_id = failure.get("work-group-id")
    if isinstance(work_group_id, str):
        if source_type != "work-group" or source_id != work_group_id:
            issues.append(
                ValidationIssue(
                    f"{path}.diagnostic.source",
                    "must match attributed work group",
                )
            )
        return
    if source_type != "aggregation" or source_id is not None:
        issues.append(
            ValidationIssue(
                f"{path}.diagnostic.source",
                "must be aggregate final evidence source",
            )
        )


def _validate_summary_fail_closed_diagnostic(
    diagnostic: Mapping[str, object],
    path: str,
    issues: list[ValidationIssue],
) -> None:
    source = diagnostic.get("source")
    if isinstance(source, Mapping) and (
        source.get("type") != "aggregation" or source.get("id") is not None
    ):
        issues.append(
            ValidationIssue(
                f"{path}.diagnostic.source",
                "must be aggregate fail-closed source",
            )
        )
    if diagnostic.get("verdict-effect") != "fail-closed":
        issues.append(
            ValidationIssue(
                f"{path}.diagnostic.verdict-effect",
                "must be fail-closed for fail-closed failures",
            )
        )
    if diagnostic.get("severity") != "fail-closed":
        issues.append(
            ValidationIssue(
                f"{path}.diagnostic.severity",
                "must be fail-closed for fail-closed failures",
            )
        )


def _validate_summary_work_groups(
    value: object,
    issues: list[ValidationIssue],
) -> None:
    groups = _validate_object(
        value,
        _SUMMARY_WORK_GROUP_KEYS,
        "$.work-groups",
        issues,
    )
    if groups is None:
        return
    for key in _SUMMARY_WORK_GROUP_KEYS - {"terminal-aggregation"}:
        _validate_non_negative_int(
            groups.get(key), f"$.work-groups.{key}", issues
        )
    if groups.get("terminal-aggregation") != "present":
        issues.append(
            ValidationIssue(
                "$.work-groups.terminal-aggregation",
                "must be present",
            ),
        )


def _validate_summary_budget_matches_aggregate_manifest(
    summary: Mapping[str, object],
    aggregate_manifest: Mapping[str, object],
    issues: list[ValidationIssue],
) -> None:
    budgets = summary.get("budgets")
    if not isinstance(budgets, Mapping):
        return
    pre_final = aggregate_manifest.get("pre-final-validation-artifacts")
    if budgets.get("pre-final-validation-artifacts") != pre_final:
        issues.append(
            ValidationIssue(
                "$.budgets.pre-final-validation-artifacts",
                "must match aggregate evidence manifest",
            )
        )
    if isinstance(pre_final, int) and budgets.get(
        "expected-actual-validation-artifacts"
    ) != (pre_final + _EXPECTED_FINAL_VALIDATION_ARTIFACTS):
        issues.append(
            ValidationIssue(
                "$.budgets.expected-actual-validation-artifacts",
                "must equal aggregate manifest pre-final plus final artifacts",
            )
        )


def _validate_summary_budget_matches_execution_manifest(
    summary: Mapping[str, object],
    execution_batch_manifest: Mapping[str, object] | None,
    plan: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> None:
    if execution_batch_manifest is None or _is_invalid_plan_summary(summary):
        return
    budgets = summary.get("budgets")
    manifest_budget = execution_batch_manifest.get("budget")
    if not isinstance(budgets, Mapping) or not isinstance(
        manifest_budget, Mapping
    ):
        return
    matching_keys = (
        "pre-final-validation-artifacts",
        "expected-final-validation-artifacts",
        "max-validation-artifacts",
        "aggregate-target-duration-seconds",
        "aggregate-max-duration-seconds",
    )
    for key in matching_keys:
        if budgets.get(key) != manifest_budget.get(key):
            issues.append(
                ValidationIssue(
                    f"$.budgets.{key}",
                    "must match execution batch manifest budget",
                )
            )
    _validate_summary_actual_jobs_match_execution_manifest(
        budgets,
        execution_batch_manifest,
        manifest_budget,
        plan,
        issues,
    )
    if budgets.get("expected-actual-validation-artifacts") != (
        manifest_budget.get("actual-validation-artifacts")
    ):
        issues.append(
            ValidationIssue(
                "$.budgets.expected-actual-validation-artifacts",
                "must match execution batch manifest actual validation "
                "artifacts",
            )
        )


def _validate_summary_actual_jobs_match_execution_manifest(
    budgets: Mapping[str, object],
    execution_batch_manifest: Mapping[str, object],
    manifest_budget: Mapping[str, object],
    plan: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> None:
    batches = execution_batch_manifest.get("batches")
    if not isinstance(batches, Sequence) or isinstance(batches, str | bytes):
        return
    expected_batches = len(batches)
    if budgets.get("actual-execution-batches") != expected_batches:
        issues.append(
            ValidationIssue(
                "$.budgets.actual-execution-batches",
                "must match execution batch manifest batches",
            )
        )
    control_plane = manifest_budget.get("non-batch-control-plane-job-count")
    expected_physical_jobs = (
        control_plane
        + _active_runner_family_orchestrator_count(
            [batch for batch in batches if isinstance(batch, Mapping)]
        )
        if isinstance(control_plane, int)
        else None
    )
    if (
        expected_physical_jobs is not None
        and budgets.get("actual-total-jobs") != expected_physical_jobs
    ):
        issues.append(
            ValidationIssue(
                "$.budgets.actual-total-jobs",
                "must match execution batch manifest job topology",
            )
        )
    if plan is None:
        return
    derived_windows = _derived_windows_jobs(
        [batch for batch in batches if isinstance(batch, Mapping)],
        _work_groups_by_id(plan),
    )
    if budgets.get("actual-windows-jobs") != derived_windows:
        issues.append(
            ValidationIssue(
                "$.budgets.actual-windows-jobs",
                "must match execution batch manifest Windows topology",
            )
        )


def _summary_frozen_input_digests_from_plan(
    plan: Mapping[str, object] | None,
) -> dict[str, str] | None:
    if plan is None:
        return None
    frozen: dict[str, str] = {}
    plan_digest = _verified_plan_digest_or_none(plan)
    if plan_digest is not None:
        frozen["validation-plan"] = plan_digest
    request = plan.get("request")
    if isinstance(request, Mapping):
        request_digest = request.get("request-digest")
        if isinstance(request_digest, str):
            frozen["request"] = request_digest
    affected = plan.get("affected-range")
    if isinstance(affected, Mapping):
        changed_files_hash = affected.get("changed-files-hash")
        if isinstance(changed_files_hash, str) and changed_files_hash != "":
            frozen["changed-files-snapshot"] = changed_files_hash
    fact = plan.get("fact-snapshot")
    if isinstance(fact, Mapping):
        fact_snapshot_id = fact.get("id")
        if isinstance(fact_snapshot_id, str):
            frozen["fact-snapshot"] = fact_snapshot_id
    return frozen


def _context_for_valid_aggregate_input(
    aggregate_manifest: Mapping[str, object],
    input_name: str,
    context: Mapping[str, object] | None,
) -> Mapping[str, object] | None:
    input_artifacts = aggregate_manifest.get("input-artifacts")
    if not isinstance(input_artifacts, Mapping):
        return None
    artifact = input_artifacts.get(input_name)
    if not isinstance(artifact, Mapping):
        return None
    if artifact.get("admissibility") != "valid":
        return None
    return context


def _context_for_valid_or_retained_invalid_aggregate_input(
    aggregate_manifest: Mapping[str, object],
    input_name: str,
    context: Mapping[str, object] | None,
) -> Mapping[str, object] | None:
    if (
        _context_for_valid_aggregate_input(
            aggregate_manifest,
            input_name,
            context,
        )
        is not None
    ):
        return context
    if input_name in _invalid_plan_input_failure_input_names(
        aggregate_manifest,
    ):
        return context
    return None


def _aggregate_manifest_issue_allowed_for_retained_invalid_input(
    issue: ValidationIssue,
    invalid_input_names: set[str],
) -> bool:
    if (
        "changed-files-snapshot" in invalid_input_names
        and issue.path == "fact_snapshot.plan-id"
        and issue.message == "requires proven plan identity"
    ):
        return True
    return issue.path in {
        f"$.input-artifacts.{input_name}.admissibility"
        for input_name in invalid_input_names
    } and issue.message.startswith("must be valid when ")


def _validate_summary_matches_aggregate_manifest(  # noqa: C901,PLR0912,PLR0913,PLR0915
    summary: Mapping[str, object],
    aggregate_manifest: Mapping[str, object],
    plan: Mapping[str, object] | None,
    execution_batch_manifest: Mapping[str, object] | None,
    request: Mapping[str, object] | None,
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    envelope: CommonEnvelope | None,
    issues: list[ValidationIssue],
) -> None:
    if envelope is not None:
        manifest_envelope = _envelope_or_collect(
            aggregate_manifest,
            CiValidationKind.AGGREGATE_EVIDENCE_MANIFEST,
            issues=issues,
        )
        if manifest_envelope is not None:
            _validate_context_envelope_matches_current(
                manifest_envelope,
                envelope,
                "$.aggregate-evidence-manifest",
                issues,
            )
    invalid_plan_input_failure = bool(
        _invalid_plan_input_failure_details(aggregate_manifest),
    )
    retained_invalid_plan_summary = _is_invalid_plan_summary(
        summary,
    ) and _invalid_plan_detail_allows_retained_plan_context(
        _invalid_plan_failure_detail_from_summary(summary),
    )
    unbound_summary_plan = (
        summary.get("plan-id") is None and summary.get("plan-digest") is None
    )
    summary_uses_unbound_manifest_projection = aggregate_manifest.get(
        "projection-authority"
    ) is None and _summary_projection_matches(
        summary, _no_authority_summary_projection()
    )
    if (
        not (
            invalid_plan_input_failure
            and (retained_invalid_plan_summary or unbound_summary_plan)
        )
        and not summary_uses_unbound_manifest_projection
    ):
        if summary.get("plan-id") != aggregate_manifest.get("plan-id"):
            issues.append(
                ValidationIssue(
                    "$.aggregate-evidence-manifest.plan-id",
                    "must match summary",
                )
            )
        if summary.get("plan-digest") != aggregate_manifest.get("plan-digest"):
            issues.append(
                ValidationIssue(
                    "$.aggregate-evidence-manifest.plan-digest",
                    "must match summary",
                )
            )
    _validate_summary_budget_matches_aggregate_manifest(
        summary, aggregate_manifest, issues
    )
    context_plan = (
        None
        if (
            aggregate_manifest.get("plan-id") is None
            and aggregate_manifest.get("plan-digest") is None
        )
        or summary_uses_unbound_manifest_projection
        or (
            invalid_plan_input_failure
            and (
                unbound_summary_plan
                or (
                    aggregate_manifest.get("plan-id") is None
                    and aggregate_manifest.get("plan-digest") is None
                )
            )
        )
        else plan
    )
    aggregate_manifest_validation_plan = (
        plan
        if plan is not None
        and (
            aggregate_manifest.get("plan-id") is not None
            or aggregate_manifest.get("plan-digest") is not None
        )
        else context_plan
    )
    context_changed_files_snapshot = (
        changed_files_snapshot
        if aggregate_manifest_validation_plan is not None
        else None
    )
    context_fact_snapshot = (
        fact_snapshot
        if aggregate_manifest_validation_plan is not None
        else None
    )
    context_execution_batch_manifest = (
        execution_batch_manifest
        if aggregate_manifest_validation_plan is not None
        or _aggregate_manifest_has_no_authoritative_plan(aggregate_manifest)
        else None
    )
    try:
        _validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=aggregate_manifest_validation_plan,
            execution_batch_manifest=context_execution_batch_manifest,
            request=_context_for_valid_or_retained_invalid_aggregate_input(
                aggregate_manifest, "request", request
            ),
            changed_files_snapshot=_context_for_valid_or_retained_invalid_aggregate_input(
                aggregate_manifest,
                "changed-files-snapshot",
                context_changed_files_snapshot,
            ),
            fact_snapshot=_context_for_valid_or_retained_invalid_aggregate_input(
                aggregate_manifest, "fact-snapshot", context_fact_snapshot
            ),
            frozen_input_digests=(
                _summary_frozen_input_digests_from_plan(plan)
                if aggregate_manifest_validation_plan is not None
                else None
            ),
            expected_run_id=envelope.run_id if envelope is not None else None,
            expected_run_attempt=(
                envelope.run_attempt if envelope is not None else None
            ),
            _require_authoritative_snapshot_inputs=False,
            _require_context_proof_for_valid_inputs=not (
                invalid_plan_input_failure
                or summary_uses_unbound_manifest_projection
            ),
        )
    except ContractValidationError as error:
        invalid_input_names = _invalid_plan_input_failure_input_names(
            aggregate_manifest,
        )
        issues.extend(
            issue
            for issue in error.issues
            if not _aggregate_manifest_issue_allowed_for_retained_invalid_input(
                issue,
                invalid_input_names,
            )
            and not (
                summary_uses_unbound_manifest_projection
                and issue.path == "$.input-artifacts.validation-plan"
                and "valid admissibility requires supplied" in issue.message
            )
        )
    manifest_rows = _rows_by_local_id(
        aggregate_manifest.get("batch-bundles"),
        "batch-id",
        "$.aggregate-evidence-manifest.batch-bundles",
        issues,
    )
    summary_rows = _rows_by_local_id(
        summary.get("batch-bundles"),
        "batch-id",
        "$.batch-bundles",
        issues,
    )
    if set(summary_rows) != set(manifest_rows) and not (
        invalid_plan_input_failure and unbound_summary_plan
    ):
        issues.append(
            ValidationIssue(
                "$.batch-bundles",
                "must cover aggregate evidence manifest batch bundles exactly",
            )
        )
    for batch_id, manifest_row in manifest_rows.items():
        summary_row = summary_rows.get(batch_id)
        if summary_row is None:
            continue
        candidates = manifest_row.get("observed-candidates")
        candidate_count = (
            len(candidates)
            if isinstance(candidates, Sequence)
            and not isinstance(candidates, str | bytes)
            else None
        )
        comparisons = {
            "artifact-ref": manifest_row.get("artifact-ref"),
            "admitted-candidate-id": manifest_row.get("admitted-candidate-id"),
            "candidate-count": candidate_count,
            "admissibility": manifest_row.get("slot-admissibility"),
        }
        for key, expected in comparisons.items():
            if summary_row.get(key) != expected:
                issues.append(
                    ValidationIssue(
                        f"$.batch-bundles[{batch_id}].{key}",
                        "must match aggregate evidence manifest",
                    )
                )

    evidence_rows = _rows_by_local_id(
        summary.get("evidence-results"),
        "evidence-expectation-id",
        "$.evidence-results",
        issues,
    )
    for evidence_id, row in evidence_rows.items():
        if row.get("outcome") != "satisfied":
            continue
        batch_id = row.get("batch-id")
        bundle_id = row.get("bundle-id")
        if not isinstance(batch_id, str):
            issues.append(
                ValidationIssue(
                    f"$.evidence-results[{evidence_id}].batch-id",
                    "satisfied evidence must reference an admitted batch",
                )
            )
            continue
        manifest_row = manifest_rows.get(batch_id)
        summary_row = summary_rows.get(batch_id)
        if manifest_row is None:
            issues.append(
                ValidationIssue(
                    f"$.evidence-results[{evidence_id}].batch-id",
                    "must reference an aggregate manifest batch",
                )
            )
        elif manifest_row.get(
            "slot-admissibility"
        ) != "valid" or not isinstance(
            manifest_row.get("admitted-candidate-id"), str
        ):
            issues.append(
                ValidationIssue(
                    f"$.evidence-results[{evidence_id}].batch-id",
                    "satisfied evidence must reference an admitted batch",
                )
            )
        if (
            summary_row is None
            or not isinstance(bundle_id, str)
            or summary_row.get("bundle-id") != bundle_id
        ):
            issues.append(
                ValidationIssue(
                    f"$.evidence-results[{evidence_id}].bundle-id",
                    "satisfied evidence must reference an admitted bundle",
                )
            )
    if plan is not None:
        plan_evidence = _evidence_expectations_by_id(plan)
        manifest_batch_ids = set(manifest_rows)
        for evidence_id, row in evidence_rows.items():
            expected = plan_evidence.get(evidence_id)
            if expected is not None and row.get(
                "work-group-id"
            ) != expected.get("work-group-id"):
                issues.append(
                    ValidationIssue(
                        f"$.evidence-results[{evidence_id}].work-group-id",
                        "must match plan",
                    )
                )
            batch_id = row.get("batch-id")
            if batch_id is not None and batch_id not in manifest_batch_ids:
                issues.append(
                    ValidationIssue(
                        f"$.evidence-results[{evidence_id}].batch-id",
                        "must reference an aggregate manifest batch",
                    )
                )


def _validate_summary_evidence_matches_plan(
    summary: Mapping[str, object],
    plan: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> None:
    if (
        plan is None
        or _is_invalid_plan_summary(summary)
        or _summary_has_required_input_failure(summary)
    ):
        return
    evidence_rows = _rows_by_local_id(
        summary.get("evidence-results"),
        "evidence-expectation-id",
        "$.evidence-results",
        issues,
    )
    plan_evidence = _evidence_expectations_by_id(plan)
    if set(evidence_rows) != set(plan_evidence):
        issues.append(
            ValidationIssue(
                "$.evidence-results",
                "must cover plan evidence expectations exactly",
            )
        )
    for evidence_id, row in evidence_rows.items():
        expected = plan_evidence.get(evidence_id)
        if expected is not None and row.get("work-group-id") != expected.get(
            "work-group-id"
        ):
            issues.append(
                ValidationIssue(
                    f"$.evidence-results[{evidence_id}].work-group-id",
                    "must match plan",
                )
            )


def _rows_by_local_id(
    value: object,
    key: str,
    path: str,
    issues: list[ValidationIssue],
) -> dict[str, Mapping[str, object]]:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        return {}
    rows: dict[str, Mapping[str, object]] = {}
    for index, item in enumerate(value):
        if not isinstance(item, Mapping):
            continue
        row_id = item.get(key)
        if isinstance(row_id, str):
            if row_id in rows:
                issues.append(ValidationIssue(path, f"{key} must be unique"))
            rows[row_id] = item
        else:
            issues.append(
                ValidationIssue(f"{path}[{index}].{key}", "must be a string")
            )
    return rows


def _summary_evidence_rows(value: object) -> list[Mapping[str, object]]:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        return []
    return [item for item in value if isinstance(item, Mapping)]


def _summary_has_inadmissible_batch(summary: Mapping[str, object]) -> bool:
    rows = summary.get("batch-bundles")
    if not isinstance(rows, Sequence) or isinstance(rows, str | bytes):
        return False
    return any(
        isinstance(row, Mapping) and row.get("admissibility") != "valid"
        for row in rows
    )


def _summary_has_required_input_failure(summary: Mapping[str, object]) -> bool:
    failures = summary.get("failures")
    if not isinstance(failures, Sequence) or isinstance(failures, str | bytes):
        return False
    return any(
        isinstance(failure, Mapping)
        and failure.get("kind")
        == DiagnosticFamily.REQUIRED_INPUT_ARTIFACT_FAILURE.value
        and isinstance(failure.get("diagnostic"), Mapping)
        and cast("Mapping[str, object]", failure["diagnostic"]).get("code")
        == DiagnosticFamily.REQUIRED_INPUT_ARTIFACT_FAILURE.value
        and cast("Mapping[str, object]", failure["diagnostic"]).get("detail")
        in {
            DiagnosticDetail.REQUIRED_INPUT_ARTIFACT_FAILURE.value,
            DiagnosticDetail.SNAPSHOT_COMPANION_UNPROVEN.value,
        }
        for failure in failures
    )


def _summary_duration_exceeded(summary: Mapping[str, object]) -> bool:
    budgets = summary.get("budgets")
    if not isinstance(budgets, Mapping):
        return False
    duration = budgets.get("aggregate-duration-seconds")
    maximum = budgets.get("aggregate-max-duration-seconds")
    return (
        isinstance(duration, int)
        and isinstance(maximum, int)
        and duration > maximum
    )


def _validate_summary_count_relationships(
    summary: Mapping[str, object],
    issues: list[ValidationIssue],
) -> None:
    budgets = summary.get("budgets")
    if not isinstance(budgets, Mapping):
        return
    bundles = summary.get("batch-bundles")
    bundle_count = (
        len(bundles)
        if isinstance(bundles, Sequence)
        and not isinstance(bundles, str | bytes)
        else None
    )
    actual_batches = budgets.get("actual-execution-batches")
    if (
        bundle_count is not None
        and isinstance(actual_batches, int)
        and actual_batches != bundle_count
    ):
        issues.append(
            ValidationIssue(
                "$.budgets.actual-execution-batches",
                "must equal summary batch bundle count",
            )
        )


def _validate_summary_matches_admitted_bundles(  # noqa: C901, PLR0912, PLR0913
    summary: Mapping[str, object],
    aggregate_manifest: Mapping[str, object] | None,
    bundles: Sequence[Mapping[str, object]],
    plan: Mapping[str, object] | None,
    execution_batch_manifest: Mapping[str, object] | None,
    request: Mapping[str, object] | None,
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    envelope: CommonEnvelope | None,
    issues: list[ValidationIssue],
) -> None:
    bundle_by_batch: dict[str, Mapping[str, object]] = {}
    for index, bundle in enumerate(bundles):
        _validate_plan_identity_matches(
            summary,
            bundle,
            "$",
            f"admitted_batch_evidence_bundles[{index}]",
            issues,
        )
        _validate_summary_projection_matches_bundle(
            summary,
            bundle,
            f"admitted_batch_evidence_bundles[{index}]",
            issues,
        )
        batch = bundle.get("batch")
        if isinstance(batch, Mapping) and isinstance(
            batch.get("batch-id"), str
        ):
            batch_id = str(batch["batch-id"])
            if batch_id in bundle_by_batch:
                issues.append(
                    ValidationIssue(
                        f"admitted_batch_evidence_bundles[{index}]",
                        "batch ids must be unique",
                    )
                )
            bundle_by_batch[batch_id] = bundle
    if aggregate_manifest is not None:
        manifest_rows = _rows_by_local_id(
            aggregate_manifest.get("batch-bundles"),
            "batch-id",
            "$.aggregate-evidence-manifest.batch-bundles",
            issues,
        )
    else:
        manifest_rows = {}
    _validate_admitted_bundles_topologically(
        bundles,
        manifest_rows=manifest_rows,
        plan=plan,
        request=request,
        execution_batch_manifest=execution_batch_manifest,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        envelope=envelope,
        issues=issues,
    )
    derived_results: dict[str, dict[str, object]] = {}
    summary_bundle_rows = _rows_by_local_id(
        summary.get("batch-bundles"),
        "batch-id",
        "$.batch-bundles",
        issues,
    )
    if aggregate_manifest is not None:
        for batch_id, manifest_row in manifest_rows.items():
            if manifest_row.get(
                "slot-admissibility"
            ) != "valid" or not isinstance(
                manifest_row.get("admitted-candidate-id"), str
            ):
                continue
            if batch_id not in bundle_by_batch:
                issues.append(
                    ValidationIssue(
                        f"admitted_batch_evidence_bundles[{batch_id}]",
                        "must include every valid admitted aggregate "
                        "manifest slot",
                    )
                )
    elif set(summary_bundle_rows) != set(bundle_by_batch):
        issues.append(
            ValidationIssue(
                "$.batch-bundles",
                "must cover admitted batch evidence bundles exactly",
            )
        )
    for batch_id, bundle in bundle_by_batch.items():
        if aggregate_manifest is not None:
            manifest_row = manifest_rows.get(batch_id)
            if manifest_row is None:
                issues.append(
                    ValidationIssue(
                        f"admitted_batch_evidence_bundles[{batch_id}]",
                        "must be admitted by aggregate evidence manifest",
                    )
                )
                continue
            _validate_admitted_bundle_candidate(
                batch_id, bundle, manifest_row, issues
            )
        summary_bundle_row = summary_bundle_rows.get(batch_id)
        if summary_bundle_row is None:
            issues.append(
                ValidationIssue(
                    f"$.batch-bundles[{batch_id}]",
                    "must include admitted batch evidence bundle",
                )
            )
        else:
            _validate_summary_bundle_row_matches_admitted_bundle(
                batch_id,
                summary_bundle_row,
                bundle,
                require_single_candidate_count=aggregate_manifest is None,
                issues=issues,
            )
        _derive_summary_results_from_bundle(bundle, derived_results, issues)
    _validate_admitted_bundle_dependency_results(
        bundle_by_batch,
        manifest_rows,
        issues,
    )
    summary_rows = _rows_by_local_id(
        summary.get("evidence-results"),
        "evidence-expectation-id",
        "$.evidence-results",
        issues,
    )
    for evidence_id, expected in derived_results.items():
        row = summary_rows.get(evidence_id)
        if row is None:
            issues.append(
                ValidationIssue(
                    f"$.evidence-results[{evidence_id}]",
                    "must include admitted bundle selector result",
                )
            )
            continue
        for key, expected_value in expected.items():
            if row.get(key) != expected_value:
                issues.append(
                    ValidationIssue(
                        f"$.evidence-results[{evidence_id}].{key}",
                        "must match admitted bundle selector result",
                    )
                )
    bundle_payload_summary_rows = {
        evidence_id
        for evidence_id, row in summary_rows.items()
        if row.get("outcome") != "missing"
    }
    if bundle_payload_summary_rows != set(derived_results):
        issues.append(
            ValidationIssue(
                "$.evidence-results",
                "must equal admitted bundle selector results exactly",
            )
        )


def _validate_admitted_bundles_topologically(  # noqa: PLR0913
    bundles: Sequence[Mapping[str, object]],
    *,
    manifest_rows: Mapping[str, Mapping[str, object]] | None = None,
    plan: Mapping[str, object] | None,
    request: Mapping[str, object] | None,
    execution_batch_manifest: Mapping[str, object] | None,
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    envelope: CommonEnvelope | None,
    issues: list[ValidationIssue],
) -> None:
    bundle_by_batch_id = {
        batch_id: bundle
        for bundle in bundles
        if (batch_id := _batch_id_from_evidence_bundle(bundle)) is not None
    }
    validated_by_batch_id: dict[str, Mapping[str, object]] = {}
    pending = list(enumerate(bundles))
    last_errors: dict[int, ContractValidationError] = {}
    while pending:
        next_pending: list[tuple[int, Mapping[str, object]]] = []
        progressed = False
        for index, bundle in pending:
            batch_id = _batch_id_from_evidence_bundle(bundle)
            if batch_id is not None and not _bundle_dependencies_validated(
                batch_id,
                bundle_by_batch_id=bundle_by_batch_id,
                validated_by_batch_id=validated_by_batch_id,
            ):
                next_pending.append((index, bundle))
                continue
            dependency_bundles = (
                _validated_dependency_bundles_for_batch(
                    batch_id,
                    bundle_by_batch_id=bundle_by_batch_id,
                    validated_by_batch_id=validated_by_batch_id,
                )
                if batch_id is not None
                else ()
            )
            try:
                validate_ci_validation_batch_evidence_bundle(
                    bundle,
                    plan=plan,
                    request=request,
                    execution_batch_manifest=execution_batch_manifest,
                    changed_files_snapshot=changed_files_snapshot,
                    fact_snapshot=fact_snapshot,
                    expected_run_id=(
                        envelope.run_id if envelope is not None else None
                    ),
                    expected_run_attempt=(
                        envelope.run_attempt if envelope is not None else None
                    ),
                    dependency_evidence_bundles=dependency_bundles,
                )
            except ContractValidationError as error:
                last_errors[index] = error
                next_pending.append((index, bundle))
                continue
            if batch_id is not None:
                validated_by_batch_id[batch_id] = (
                    _trusted_dependency_bundle_from_manifest(
                        bundle,
                        manifest_rows or {},
                    )
                )
            progressed = True
        if not next_pending:
            break
        if not progressed:
            for index, _bundle in next_pending:
                path_prefix = f"admitted_batch_evidence_bundles[{index}]"
                error = last_errors.get(index)
                if error is None:
                    issues.append(
                        ValidationIssue(
                            f"{path_prefix}.batch.depends-on-batches",
                            "could not resolve dependency topology",
                        )
                    )
                    continue
                issues.extend(
                    ValidationIssue(
                        _prefixed_validation_issue_path(
                            path_prefix,
                            issue.path,
                        ),
                        issue.message,
                    )
                    for issue in error.issues
                )
            break
        pending = next_pending


def _batch_id_from_evidence_bundle(
    bundle: Mapping[str, object],
) -> str | None:
    batch = bundle.get("batch")
    if isinstance(batch, Mapping) and isinstance(batch.get("batch-id"), str):
        return str(batch["batch-id"])
    return None


def _batch_dependency_ids_from_evidence_bundle(
    bundle: Mapping[str, object],
) -> tuple[str, ...]:
    batch = bundle.get("batch")
    if not isinstance(batch, Mapping):
        return ()
    return tuple(
        str(item)
        for item in _sequence(batch.get("depends-on-batches", []))
        if isinstance(item, str)
    )


def _bundle_dependencies_validated(
    batch_id: str,
    *,
    bundle_by_batch_id: Mapping[str, Mapping[str, object]],
    validated_by_batch_id: Mapping[str, Mapping[str, object]],
) -> bool:
    for dependency_id in _batch_dependency_ids_from_evidence_bundle(
        bundle_by_batch_id[batch_id]
    ):
        if dependency_id not in bundle_by_batch_id:
            continue
        if dependency_id not in validated_by_batch_id:
            return False
        if not _bundle_dependencies_validated(
            dependency_id,
            bundle_by_batch_id=bundle_by_batch_id,
            validated_by_batch_id=validated_by_batch_id,
        ):
            return False
    return True


def _validated_dependency_bundles_for_batch(
    batch_id: str,
    *,
    bundle_by_batch_id: Mapping[str, Mapping[str, object]],
    validated_by_batch_id: Mapping[str, Mapping[str, object]],
) -> tuple[Mapping[str, object], ...]:
    dependency_bundles: list[Mapping[str, object]] = []
    visited: set[str] = set()

    def append_dependency(dependency_id: str) -> None:
        if dependency_id in visited:
            return
        visited.add(dependency_id)
        dependency = bundle_by_batch_id.get(dependency_id)
        if dependency is None:
            return
        for (
            transitive_dependency_id
        ) in _batch_dependency_ids_from_evidence_bundle(dependency):
            append_dependency(transitive_dependency_id)
        validated = validated_by_batch_id.get(dependency_id)
        if validated is not None:
            dependency_bundles.append(validated)

    for dependency_id in _batch_dependency_ids_from_evidence_bundle(
        bundle_by_batch_id[batch_id]
    ):
        append_dependency(dependency_id)
    return tuple(dependency_bundles)


def _prefixed_validation_issue_path(prefix: str, path: str) -> str:
    if path == "$":
        return prefix
    if path.startswith(("$.", "$[")):
        return f"{prefix}{path[1:]}"
    if path.startswith((".", "[")):
        return f"{prefix}{path}"
    return f"{prefix}.{path}"


def _validate_admitted_bundle_candidate(
    batch_id: str,
    bundle: Mapping[str, object],
    manifest_row: Mapping[str, object],
    issues: list[ValidationIssue],
) -> None:
    if manifest_row.get("slot-admissibility") != "valid" or not isinstance(
        manifest_row.get("admitted-candidate-id"), str
    ):
        issues.append(
            ValidationIssue(
                f"admitted_batch_evidence_bundles[{batch_id}]",
                "must be admitted by aggregate evidence manifest",
            )
        )
        return
    summary_ref = bundle.get("artifact-ref")
    if summary_ref != manifest_row.get("artifact-ref"):
        issues.append(
            ValidationIssue(
                f"admitted_batch_evidence_bundles[{batch_id}].artifact-ref",
                "must match aggregate evidence manifest",
            )
        )
    admitted_id = manifest_row.get("admitted-candidate-id")
    candidates = manifest_row.get("observed-candidates")
    if not isinstance(candidates, Sequence) or isinstance(
        candidates, str | bytes
    ):
        return
    admitted = next(
        (
            candidate
            for candidate in candidates
            if isinstance(candidate, Mapping)
            and candidate.get("candidate-id") == admitted_id
        ),
        None,
    )
    if admitted is None:
        return
    expected_digest = admitted.get("content-digest")
    if expected_digest != ci_validation_batch_evidence_bundle_payload_digest(
        bundle
    ):
        issues.append(
            ValidationIssue(
                f"admitted_batch_evidence_bundles[{batch_id}].content-digest",
                "must match admitted aggregate manifest candidate",
            )
        )


def _trusted_dependency_bundle_from_manifest(
    bundle: Mapping[str, object],
    manifest_rows: Mapping[str, Mapping[str, object]],
) -> Mapping[str, object]:
    batch = bundle.get("batch")
    batch_id = batch.get("batch-id") if isinstance(batch, Mapping) else None
    if not isinstance(batch_id, str):
        return bundle
    artifact_instance_id, admitted_candidate_id = _admitted_batch_identity(
        manifest_rows.get(batch_id)
    )
    if not isinstance(artifact_instance_id, str) or not isinstance(
        admitted_candidate_id, str
    ):
        return bundle
    return _TrustedDependencyBundle(
        bundle,
        artifact_instance_id=artifact_instance_id,
        admitted_candidate_id=admitted_candidate_id,
    )


def _validate_summary_bundle_row_matches_admitted_bundle(
    batch_id: str,
    summary_row: Mapping[str, object],
    bundle: Mapping[str, object],
    *,
    require_single_candidate_count: bool,
    issues: list[ValidationIssue],
) -> None:
    comparisons = {
        "artifact-ref": bundle.get("artifact-ref"),
        "bundle-id": bundle.get("bundle-id"),
        "admissibility": "valid",
    }
    for key, expected in comparisons.items():
        if summary_row.get(key) != expected:
            issues.append(
                ValidationIssue(
                    f"$.batch-bundles[{batch_id}].{key}",
                    "must match admitted batch evidence bundle",
                )
            )
    if (
        require_single_candidate_count
        and summary_row.get("candidate-count") != 1
    ):
        issues.append(
            ValidationIssue(
                f"$.batch-bundles[{batch_id}].candidate-count",
                "must equal admitted bundle payload count",
            )
        )


def _validate_summary_projection_matches_bundle(
    summary: Mapping[str, object],
    bundle: Mapping[str, object],
    path: str,
    issues: list[ValidationIssue],
) -> None:
    for key in ("mode", "validation-tree", "affected-range", "scheduled-full"):
        if summary.get(key) != bundle.get(key):
            issues.append(
                ValidationIssue(
                    f"{path}.{key}",
                    "must match aggregate summary",
                )
            )


def _derive_summary_results_from_bundle(
    bundle: Mapping[str, object],
    derived_results: dict[str, dict[str, object]],
    issues: list[ValidationIssue],
) -> None:
    selector_results = bundle.get("selector-results")
    batch = bundle.get("batch")
    batch_id = batch.get("batch-id") if isinstance(batch, Mapping) else None
    bundle_id = bundle.get("bundle-id")
    if not isinstance(selector_results, Sequence) or isinstance(
        selector_results, str | bytes
    ):
        return
    for selector in selector_results:
        if not isinstance(selector, Mapping):
            continue
        evidence_id = selector.get("expected-evidence-id")
        if not isinstance(evidence_id, str):
            continue
        outcome = selector.get("outcome")
        derived = {
            "evidence-expectation-id": evidence_id,
            "work-group-id": selector.get("work-group-id"),
            "batch-id": batch_id,
            "bundle-id": bundle_id,
            "selector-index": selector.get("selector-index"),
            "outcome": _selector_outcome_to_summary_outcome(outcome),
            "diagnostics": selector.get("diagnostics"),
        }
        if evidence_id in derived_results:
            issues.append(
                ValidationIssue(
                    "$.evidence-results",
                    "admitted bundle selector evidence ids must be unique",
                )
            )
        derived_results[evidence_id] = derived


def _validate_admitted_bundle_dependency_results(
    bundle_by_batch: Mapping[str, Mapping[str, object]],
    manifest_rows: Mapping[str, Mapping[str, object]],
    issues: list[ValidationIssue],
) -> None:
    selector_lookup: dict[
        str, tuple[str, bool, str | None, str | None, str | None]
    ] = {}
    for batch_id, bundle in bundle_by_batch.items():
        selector_results = bundle.get("selector-results")
        if not isinstance(selector_results, Sequence) or isinstance(
            selector_results, str | bytes
        ):
            continue
        artifact_instance_id, admitted_candidate_id = _admitted_batch_identity(
            manifest_rows.get(batch_id)
        )
        for selector in selector_results:
            if not isinstance(selector, Mapping):
                continue
            work_group_id = selector.get("work-group-id")
            if isinstance(work_group_id, str):
                selector_lookup[work_group_id] = (
                    _selector_outcome_to_summary_outcome(
                        selector.get("outcome")
                    ),
                    _selector_outcome_admitted_for_gating(
                        selector.get("outcome")
                    ),
                    batch_id,
                    artifact_instance_id,
                    admitted_candidate_id,
                )
    for batch_id, bundle in bundle_by_batch.items():
        selector_results = bundle.get("selector-results")
        if not isinstance(selector_results, Sequence) or isinstance(
            selector_results, str | bytes
        ):
            continue
        for selector_index, selector in enumerate(selector_results):
            if not isinstance(selector, Mapping):
                continue
            _validate_selector_dependencies_against_admitted_evidence(
                batch_id,
                selector_index,
                selector,
                selector_lookup,
                issues,
            )


def _validate_selector_dependencies_against_admitted_evidence(  # noqa: C901, PLR0912
    batch_id: str,
    selector_index: int,
    selector: Mapping[str, object],
    selector_lookup: Mapping[
        str, tuple[str, bool, str | None, str | None, str | None]
    ],
    issues: list[ValidationIssue],
) -> None:
    dependency_results = selector.get("dependency-results")
    if not isinstance(dependency_results, Sequence) or isinstance(
        dependency_results, str | bytes
    ):
        return
    blocked_by_actual_evidence = False
    dep_path = (
        f"admitted_batch_evidence_bundles[{batch_id}]"
        f".selector-results[{selector_index}].dependency-results"
    )
    for dependency_index, dependency in enumerate(dependency_results):
        if not isinstance(dependency, Mapping):
            continue
        work_group_id = dependency.get("work-group-id")
        (
            actual_outcome,
            actual_admitted,
            actual_batch_id,
            actual_artifact_instance_id,
            actual_candidate_id,
        ) = (
            selector_lookup.get(
                work_group_id, ("missing", False, None, None, None)
            )
            if isinstance(work_group_id, str)
            else ("missing", False, None, None, None)
        )
        item_path = f"{dep_path}[{dependency_index}]"
        if dependency.get("outcome") != actual_outcome:
            issues.append(
                ValidationIssue(
                    f"{item_path}.outcome",
                    "must match admitted upstream selector result",
                )
            )
        if dependency.get("admitted-for-gating") != actual_admitted:
            issues.append(
                ValidationIssue(
                    f"{item_path}.admitted-for-gating",
                    "must match admitted upstream selector result",
                )
            )
        source_batch_id = dependency.get("source-batch-id")
        if actual_batch_id is not None and source_batch_id != actual_batch_id:
            issues.append(
                ValidationIssue(
                    f"{item_path}.source-batch-id",
                    "must match admitted upstream selector batch",
                )
            )
        if not actual_admitted:
            blocked_by_actual_evidence = True
        if actual_batch_id is not None and (
            dependency.get("upstream-artifact-instance-id")
            != actual_artifact_instance_id
        ):
            issues.append(
                ValidationIssue(
                    f"{item_path}.upstream-artifact-instance-id",
                    "must match admitted upstream artifact instance",
                )
            )
        if actual_batch_id is not None and (
            dependency.get("upstream-admitted-candidate-id")
            != actual_candidate_id
        ):
            issues.append(
                ValidationIssue(
                    f"{item_path}.upstream-admitted-candidate-id",
                    "must match admitted upstream candidate",
                )
            )
    selector_path = (
        f"admitted_batch_evidence_bundles[{batch_id}]"
        f".selector-results[{selector_index}]"
    )
    if not blocked_by_actual_evidence:
        return
    if selector.get("outcome") != "skipped":
        issues.append(
            ValidationIssue(
                f"{selector_path}.outcome",
                "must be skipped when admitted dependency evidence is blocked",
            )
        )
    if selector.get("skip-reason") != "dependency-blocked":
        issues.append(
            ValidationIssue(
                f"{selector_path}.skip-reason",
                "must be dependency-blocked",
            )
        )
    if not _has_dependency_blocked_diagnostic(selector.get("diagnostics")):
        issues.append(
            ValidationIssue(
                f"{selector_path}.diagnostics",
                "must include a dependency-blocked diagnostic",
            )
        )


def _admitted_batch_identity(
    manifest_row: Mapping[str, object] | None,
) -> tuple[str | None, str | None]:
    if manifest_row is None:
        return None, None
    admitted_candidate_id = manifest_row.get("admitted-candidate-id")
    candidates = manifest_row.get("observed-candidates")
    if (
        not isinstance(admitted_candidate_id, str)
        or not isinstance(candidates, Sequence)
        or isinstance(candidates, str | bytes)
    ):
        return None, None
    admitted = next(
        (
            candidate
            for candidate in candidates
            if isinstance(candidate, Mapping)
            and candidate.get("candidate-id") == admitted_candidate_id
        ),
        None,
    )
    if not isinstance(admitted, Mapping):
        return None, admitted_candidate_id
    artifact_instance_id = admitted.get("artifact-instance-id")
    return (
        artifact_instance_id if isinstance(artifact_instance_id, str) else None,
        admitted_candidate_id,
    )


def _selector_outcome_to_summary_outcome(outcome: object) -> str:
    if outcome == "success":
        return "satisfied"
    if outcome == "skipped":
        return "skipped"
    return "failed"


def _selector_outcome_admitted_for_gating(outcome: object) -> bool:
    return outcome in {"success", "blocking-failure"}


def _is_invalid_plan_summary(summary: Mapping[str, object]) -> bool:
    reason = summary.get("reason")
    if not isinstance(reason, Mapping):
        return False
    if reason.get("invalid-plan") is True:
        return True
    return (
        reason.get("fail-closed") is True
        and _canonical_fail_closed_invalid_plan_failure(summary) is not None
    )


def _invalid_plan_failure(detail: str | None = None) -> dict[str, object]:
    failure = {
        **_INVALID_PLAN_FAILURE,
        "diagnostic": dict(
            cast("Mapping[str, object]", _INVALID_PLAN_FAILURE["diagnostic"])
        ),
    }
    if detail is None or detail == DiagnosticDetail.PLAN_MISSING.value:
        return failure
    diagnostic = cast("dict[str, object]", failure["diagnostic"])
    diagnostic["diagnostic-id"] = f"invalid-plan/{detail}"
    diagnostic["detail"] = detail
    diagnostic["message"] = CI_VALIDATION_INVALID_PLAN_NON_AUTHORITATIVE_MESSAGE
    failure["message"] = CI_VALIDATION_INVALID_PLAN_NON_AUTHORITATIVE_MESSAGE
    return failure


def _nullable_string_member_is_present(
    value: Mapping[str, object],
    key: str,
) -> bool:
    member = value.get(key)
    return key in value and (member is None or isinstance(member, str))


def _failure_matches_canonical_identity(
    failure: Mapping[str, object],
    expected: Mapping[str, object],
) -> bool:
    if not _nullable_string_member_is_present(failure, "message"):
        return False
    diagnostic = failure.get("diagnostic")
    expected_diagnostic = expected.get("diagnostic")
    if not isinstance(diagnostic, Mapping) or not isinstance(
        expected_diagnostic,
        Mapping,
    ):
        return False
    if not _nullable_string_member_is_present(diagnostic, "message"):
        return False
    failure_identity = {
        key: value for key, value in failure.items() if key != "message"
    }
    expected_identity = {
        key: value for key, value in expected.items() if key != "message"
    }
    failure_identity["diagnostic"] = {
        key: value for key, value in diagnostic.items() if key != "message"
    }
    expected_identity["diagnostic"] = {
        key: value
        for key, value in expected_diagnostic.items()
        if key != "message"
    }
    return failure_identity == expected_identity


def _fail_closed_invalid_plan_failure(
    detail: str | None = None,
) -> dict[str, object]:
    actual_detail = detail or DiagnosticDetail.PLAN_MISSING.value
    return {
        "kind": "fail-closed",
        "batch-id": None,
        "work-group-id": None,
        "evidence-expectation-id": None,
        "bundle-id": None,
        "diagnostic": {
            "diagnostic-id": f"fail-closed/invalid-plan/{actual_detail}",
            "code": DiagnosticFamily.INVALID_PLAN.value,
            "detail": actual_detail,
            "message": "Validation planning failed closed.",
            "source": {"type": "aggregation", "id": None},
            "severity": "fail-closed",
            "verdict-effect": "fail-closed",
        },
        "message": "Validation planning failed closed.",
    }


def _final_producer_unverified_failure() -> dict[str, object]:
    diagnostic = {
        "diagnostic-id": "final-producer-unverified",
        "code": DiagnosticFamily.FINAL_PRODUCER_UNVERIFIED.value,
        "detail": DiagnosticDetail.FINAL_PRODUCER_UNVERIFIED.value,
        "message": (
            "Aggregate evidence manifest producer boundary was not verified "
            "before summary generation."
        ),
        "source": {"type": "aggregation", "id": None},
        "severity": "fail-closed",
        "verdict-effect": "fail-closed",
    }
    return {
        "kind": "final-producer-unverified",
        "batch-id": None,
        "work-group-id": None,
        "evidence-expectation-id": None,
        "bundle-id": None,
        "diagnostic": diagnostic,
        "message": diagnostic["message"],
    }


def _final_evidence_failure(detail: str) -> dict[str, object]:
    diagnostic = {
        "diagnostic-id": f"final-evidence-failure/{detail}",
        "code": DiagnosticFamily.FINAL_EVIDENCE_FAILURE.value,
        "detail": detail,
        "message": "Final aggregate evidence was not authoritative.",
        "source": {"type": "aggregation", "id": None},
        "severity": "fail-closed",
        "verdict-effect": "fail-closed",
    }
    return {
        "kind": "final-evidence-failure",
        "batch-id": None,
        "work-group-id": None,
        "evidence-expectation-id": None,
        "bundle-id": None,
        "diagnostic": diagnostic,
        "message": diagnostic["message"],
    }


def _canonical_fail_closed_invalid_plan_failure(
    summary: Mapping[str, object],
) -> Mapping[str, object] | None:
    failures = summary.get("failures")
    if not isinstance(failures, Sequence) or isinstance(failures, str | bytes):
        return None
    matches: list[Mapping[str, object]] = []
    for failure in failures:
        if not isinstance(failure, Mapping):
            continue
        diagnostic = failure.get("diagnostic")
        detail = (
            diagnostic.get("detail")
            if isinstance(diagnostic, Mapping)
            else None
        )
        if (
            isinstance(detail, str)
            and detail
            in DETAILS_BY_DIAGNOSTIC_CODE[DiagnosticFamily.INVALID_PLAN.value]
            and _failure_matches_canonical_identity(
                failure,
                _fail_closed_invalid_plan_failure(detail),
            )
        ):
            matches.append(failure)
    return matches[0] if len(matches) == 1 else None


def _invalid_plan_failure_detail_from_inputs(
    aggregate_manifest: Mapping[str, object] | None,
) -> str | None:
    return _invalid_plan_failure_detail_from_detail_set(
        _invalid_plan_input_failure_details(aggregate_manifest)
    )


def _freezer_invalid_plan_input_detail(
    aggregate_manifest: Mapping[str, object] | None,
    failures: Sequence[Mapping[str, object]],
    *,
    aggregate_manifest_evidence_bound: bool,
) -> str | None:
    invalid_plan_detail = _invalid_plan_failure_detail_from_inputs(
        aggregate_manifest
    )
    retained_failure_detail = _invalid_plan_failure_detail_from_summary(
        {"failures": failures}
    )
    if invalid_plan_detail is not None:
        return invalid_plan_detail
    if not _invalid_plan_detail_allows_retained_plan_context(
        retained_failure_detail
    ):
        return None
    if aggregate_manifest_evidence_bound:
        return retained_failure_detail
    _raise_retained_invalid_plan_manifest_authority_required()
    return None


def _invalid_plan_failure_detail_from_detail_set(
    details: set[str],
) -> str | None:
    if not details:
        return None
    retained_details = {
        detail
        for detail in details
        if _invalid_plan_detail_allows_retained_plan_context(detail)
    }
    if retained_details:
        return _preferred_retained_invalid_plan_detail(retained_details)
    if len(details) == 1:
        return next(iter(details))
    return DiagnosticDetail.MALFORMED_PLAN.value


def _invalid_plan_failure_detail_from_summary(
    summary: Mapping[str, object],
) -> str | None:
    failures = summary.get("failures")
    if not isinstance(failures, Sequence) or isinstance(failures, str | bytes):
        return None
    details: list[str] = []
    for failure in failures:
        if not isinstance(failure, Mapping):
            continue
        if failure.get("kind") not in {"invalid-plan", "fail-closed"}:
            continue
        diagnostic = failure.get("diagnostic")
        if failure.get("kind") == "fail-closed" and (
            not isinstance(diagnostic, Mapping)
            or diagnostic.get("code") != DiagnosticFamily.INVALID_PLAN.value
        ):
            continue
        detail = (
            diagnostic.get("detail")
            if isinstance(diagnostic, Mapping)
            else None
        )
        if (
            isinstance(detail, str)
            and detail
            in DETAILS_BY_DIAGNOSTIC_CODE[DiagnosticFamily.INVALID_PLAN.value]
        ):
            details.append(detail)
    return details[0] if len(details) == 1 else None


def _invalid_plan_input_failure_details(
    aggregate_manifest: Mapping[str, object] | None,
) -> set[str]:
    if aggregate_manifest is None:
        return set()
    input_artifacts = aggregate_manifest.get("input-artifacts")
    if not isinstance(input_artifacts, Mapping):
        return set()
    details: set[str] = set()
    for input_name in (
        "validation-plan",
        "changed-files-snapshot",
        "fact-snapshot",
    ):
        item = input_artifacts.get(input_name)
        if not isinstance(item, Mapping) or item.get("required") is not True:
            continue
        if item.get("admissibility") == "valid":
            continue
        diagnostic_details = (
            _input_artifact_invalid_plan_diagnostic_details_for_name(
                input_name,
                item,
            )
        )
        details.update(diagnostic_details)
        diagnostics = item.get("diagnostics")
        has_invalid_plan_diagnostics = (
            isinstance(diagnostics, Sequence)
            and not isinstance(diagnostics, str | bytes)
            and any(
                isinstance(diagnostic, Mapping)
                and diagnostic.get("code")
                == DiagnosticFamily.INVALID_PLAN.value
                for diagnostic in diagnostics
            )
        )
        if has_invalid_plan_diagnostics and not diagnostic_details:
            details.add(DiagnosticDetail.MALFORMED_PLAN.value)
            continue
        fallback = _INVALID_PLAN_INPUT_FALLBACK_DETAILS[input_name].get(
            str(item.get("admissibility")),
        )
        has_non_invalid_plan_diagnostics = (
            isinstance(diagnostics, Sequence)
            and not isinstance(diagnostics, str | bytes)
            and len(diagnostics) > 0
            and not diagnostic_details
        )
        if (
            fallback is not None
            and not diagnostic_details
            and not has_non_invalid_plan_diagnostics
        ):
            details.add(fallback)
    return details


def _input_artifact_invalid_plan_diagnostic_details(
    item: Mapping[str, object],
) -> set[str]:
    return _input_artifact_invalid_plan_diagnostic_details_for_name(None, item)


def _input_artifact_invalid_plan_diagnostic_details_for_name(
    input_name: str | None,
    item: Mapping[str, object],
) -> set[str]:
    diagnostics = item.get("diagnostics")
    if not isinstance(diagnostics, Sequence) or isinstance(
        diagnostics,
        str | bytes,
    ):
        return set()
    invalid_plan_diagnostics = [
        diagnostic
        for diagnostic in diagnostics
        if isinstance(diagnostic, Mapping)
        and diagnostic.get("code") == DiagnosticFamily.INVALID_PLAN.value
    ]
    if not invalid_plan_diagnostics:
        return set()
    details: set[str] = set()
    for diagnostic in invalid_plan_diagnostics:
        detail = diagnostic.get("detail")
        if (
            isinstance(detail, str)
            and detail
            in DETAILS_BY_DIAGNOSTIC_CODE[DiagnosticFamily.INVALID_PLAN.value]
            and _input_artifact_invalid_plan_diagnostic_is_canonical(
                input_name,
                item,
                diagnostic,
            )
        ):
            details.add(detail)
    return details


def _validate_input_artifact_invalid_plan_diagnostics(
    input_name: str,
    item: Mapping[str, object],
    path: str,
    issues: list[ValidationIssue],
) -> None:
    diagnostics = item.get("diagnostics")
    if not isinstance(diagnostics, Sequence) or isinstance(
        diagnostics,
        str | bytes,
    ):
        return
    for index, diagnostic in enumerate(diagnostics):
        if not isinstance(diagnostic, Mapping):
            continue
        if diagnostic.get("code") != DiagnosticFamily.INVALID_PLAN.value:
            continue
        if not _input_artifact_invalid_plan_diagnostic_is_canonical(
            input_name,
            item,
            diagnostic,
        ):
            issues.append(
                ValidationIssue(
                    f"{path}[{index}]",
                    "must be a canonical bound invalid-plan input diagnostic",
                )
            )


def _input_artifact_invalid_plan_diagnostic_is_canonical(
    input_name: str | None,
    item: Mapping[str, object],
    diagnostic: Mapping[str, object],
) -> bool:
    detail = diagnostic.get("detail")
    if not isinstance(detail, str):
        return False
    source = diagnostic.get("source")
    if not isinstance(source, Mapping):
        return False
    if (
        not _canonical_invalid_plan_diagnostic_matches(diagnostic)
        or diagnostic.get("severity") != DiagnosticSeverity.FAIL_CLOSED.value
        or diagnostic.get("verdict-effect")
        != DiagnosticVerdictEffect.FAIL_CLOSED.value
        or source.get("type") != "aggregation"
        or (
            input_name is not None
            and not _invalid_plan_detail_matches_input(input_name, detail)
        )
    ):
        return False
    source_id = source.get("id")
    artifact_ref = item.get("artifact-ref")
    return (
        source_id == artifact_ref
        if isinstance(artifact_ref, str)
        else source_id is None
    )


def _invalid_plan_detail_matches_input(input_name: str, detail: str) -> bool:
    if input_name == "validation-plan":
        return detail in {
            DiagnosticDetail.PLAN_MISSING.value,
            DiagnosticDetail.MALFORMED_PLAN.value,
            DiagnosticDetail.PLAN_UNREADABLE.value,
            DiagnosticDetail.PLAN_DUPLICATE.value,
            DiagnosticDetail.PLAN_PRODUCER_UNVERIFIED.value,
            DiagnosticDetail.PLAN_DIGEST_MISMATCH.value,
            DiagnosticDetail.SCHEMA_INVALID.value,
            DiagnosticDetail.STRUCTURALLY_INVALID.value,
        }
    if input_name == "changed-files-snapshot":
        return detail.startswith("changed-files-snapshot-")
    if input_name == "fact-snapshot":
        return detail.startswith("fact-snapshot-")
    return False


def _clear_invalid_plan_manifest_claims(summary: dict[str, object]) -> None:
    manifest_claim = summary.get("aggregate-evidence-manifest")
    if isinstance(manifest_claim, MutableMapping):
        manifest_claim["artifact-instance-id"] = None
        manifest_claim["content-digest"] = None
    final_artifacts = summary.get("final-artifacts")
    final_manifest = (
        final_artifacts.get("aggregate-evidence-manifest")
        if isinstance(final_artifacts, Mapping)
        else None
    )
    if isinstance(final_manifest, MutableMapping):
        final_manifest["artifact-instance-id"] = None
        final_manifest["content-digest"] = None
        final_manifest["producer-verified"] = False


def _authorized_invalid_plan_final_failures(
    failures: object,
    authority_failure_details: set[str],
    *,
    producer_unverified: bool,
    producer_unverified_final_evidence: bool = True,
) -> list[dict[str, object]]:
    if not isinstance(failures, Sequence) or isinstance(failures, str | bytes):
        return []
    authorized: list[dict[str, object]] = []
    final_evidence_details = set(authority_failure_details)
    if producer_unverified and producer_unverified_final_evidence:
        final_evidence_details.add(
            DiagnosticDetail.FINAL_PRODUCER_UNVERIFIED.value
        )
    for failure in failures:
        if not isinstance(failure, Mapping):
            continue
        diagnostic = failure.get("diagnostic")
        detail = (
            diagnostic.get("detail")
            if isinstance(diagnostic, Mapping)
            else None
        )
        if (
            failure.get("kind") == "final-evidence-failure"
            and isinstance(detail, str)
            and detail in final_evidence_details
            and _summary_failure_has_zero_attribution(failure)
        ):
            authorized.append(dict(failure))
            continue
        if (
            failure.get("kind") == "final-producer-unverified"
            and producer_unverified
            and _summary_failure_has_zero_attribution(failure)
        ):
            authorized.append(dict(failure))
            continue
    return authorized


def _summary_failure_has_zero_attribution(
    failure: Mapping[str, object],
) -> bool:
    return all(
        failure.get(field) is None
        for field in (
            "batch-id",
            "work-group-id",
            "evidence-expectation-id",
            "bundle-id",
        )
    )


def _strip_unbound_final_producer_unverified(
    summary: dict[str, object],
) -> None:
    reason = summary.get("reason")
    if isinstance(reason, MutableMapping):
        reason["final-producer-unverified"] = False
    failures = summary.get("failures")
    if isinstance(failures, Sequence) and not isinstance(failures, str | bytes):
        summary["failures"] = [
            failure
            for failure in failures
            if not (
                isinstance(failure, Mapping)
                and (
                    failure.get("kind") == "final-producer-unverified"
                    or (
                        failure.get("kind") == "final-evidence-failure"
                        and isinstance(failure.get("diagnostic"), Mapping)
                        and failure["diagnostic"].get("detail")
                        == DiagnosticDetail.FINAL_PRODUCER_UNVERIFIED.value
                    )
                )
            )
        ]
    diagnostics = summary.get("diagnostics")
    if isinstance(diagnostics, Sequence) and not isinstance(
        diagnostics, str | bytes
    ):
        summary["diagnostics"] = [
            diagnostic
            for diagnostic in diagnostics
            if not (
                isinstance(diagnostic, Mapping)
                and (
                    diagnostic.get("code")
                    == DiagnosticFamily.FINAL_PRODUCER_UNVERIFIED.value
                    or (
                        diagnostic.get("code")
                        == DiagnosticFamily.FINAL_EVIDENCE_FAILURE.value
                        and diagnostic.get("detail")
                        == DiagnosticDetail.FINAL_PRODUCER_UNVERIFIED.value
                    )
                )
            )
        ]


def _strip_unbound_final_producer_unverified_if_needed(
    summary: dict[str, object],
) -> None:
    final_manifest = _summary_final_aggregate_manifest(summary)
    if (
        isinstance(final_manifest, Mapping)
        and final_manifest.get("producer-verified") is False
        and not _summary_aggregate_manifest_producer_unverified(summary)
    ):
        _strip_unbound_final_producer_unverified(summary)


def _mark_final_manifest_producer_verified(
    summary: Mapping[str, object],
    *,
    producer_verified: bool,
) -> None:
    final_manifest = _summary_final_aggregate_manifest(summary)
    if isinstance(final_manifest, MutableMapping):
        final_manifest["producer-verified"] = producer_verified


def _mark_final_manifest_verified_unless_bound_unverified(
    summary: Mapping[str, object],
    *,
    producer_unverified: bool,
) -> None:
    if producer_unverified:
        return
    _mark_final_manifest_producer_verified(summary, producer_verified=True)


def _mark_invalid_plan_final_manifest_producer_state(
    summary: Mapping[str, object],
    *,
    invalid_plan_detail: str | None,
    preserve_manifest_claim: bool,
    preserve_producer_unverified: bool,
) -> None:
    if (
        _invalid_plan_detail_allows_no_authority_projection(
            invalid_plan_detail,
        )
        and not preserve_manifest_claim
    ):
        _mark_final_manifest_producer_verified(
            summary,
            producer_verified=False,
        )
        return
    _mark_final_manifest_verified_unless_bound_unverified(
        summary,
        producer_unverified=preserve_producer_unverified,
    )


def _force_invalid_plan_summary_fields(  # noqa: C901, PLR0913
    summary: dict[str, object],
    *,
    preserve_manifest_claim: bool = False,
    preserve_projection: bool = False,
    invalid_plan_detail: str | None = None,
    final_producer_unverified_bound: bool = False,
    preserve_final_manifest_producer_unverified: bool = False,
    preserve_authority_failure_details: bool = False,
) -> None:
    authority_failure_details = (
        _summary_aggregate_manifest_authority_failure_details(summary)
        if preserve_authority_failure_details
        else set()
    )
    if not preserve_projection:
        summary["plan-id"] = None
        summary["plan-digest"] = None
        summary["mode"] = "unknown"
        summary["validation-tree"] = dict(_UNKNOWN_VALIDATION_TREE)
        summary["affected-range"] = dict(_UNKNOWN_AFFECTED_RANGE)
        summary["request"] = dict(_UNKNOWN_REQUEST_SUMMARY)
        summary["scheduled-full"] = dict(_UNKNOWN_SCHEDULED_FULL)
    if not preserve_manifest_claim:
        _clear_invalid_plan_manifest_claims(summary)
    if not preserve_authority_failure_details:
        final_manifest = _summary_final_aggregate_manifest(summary)
        if isinstance(final_manifest, MutableMapping):
            final_manifest["authority-diagnostics"] = []
    preserve_producer_unverified = (
        final_producer_unverified_bound
        or preserve_final_manifest_producer_unverified
    )
    _mark_invalid_plan_final_manifest_producer_state(
        summary,
        invalid_plan_detail=invalid_plan_detail,
        preserve_manifest_claim=preserve_manifest_claim,
        preserve_producer_unverified=preserve_producer_unverified,
    )
    summary["verdict"] = "failed"
    reason = summary.get("reason")
    if isinstance(reason, MutableMapping):
        for key in _SUMMARY_REASON_KEYS:
            reason[key] = key == "invalid-plan"
        reason["final-producer-unverified"] = (
            preserve_final_manifest_producer_unverified
        )
        reason["final-evidence-failure"] = bool(authority_failure_details)
    budgets = summary.get("budgets")
    if isinstance(budgets, MutableMapping):
        budgets["actual-execution-batches"] = 0
        budgets["actual-windows-jobs"] = 0
    work_groups = summary.get("work-groups")
    if isinstance(work_groups, MutableMapping):
        work_groups["executable-required"] = 0
        work_groups["required-succeeded"] = 0
        work_groups["required-failed"] = 0
        work_groups["required-skipped"] = 0
        work_groups["required-missing"] = 0
    summary["batch-bundles"] = []
    summary["evidence-results"] = []
    final_failures = _authorized_invalid_plan_final_failures(
        summary.get("failures"),
        authority_failure_details,
        producer_unverified=final_producer_unverified_bound,
        producer_unverified_final_evidence=final_producer_unverified_bound,
    )
    if final_producer_unverified_bound and not any(
        failure.get("kind") == "final-evidence-failure"
        and isinstance(failure.get("diagnostic"), Mapping)
        and failure["diagnostic"].get("detail")
        == DiagnosticDetail.FINAL_PRODUCER_UNVERIFIED.value
        for failure in final_failures
    ):
        final_failures.append(
            _final_evidence_failure(
                DiagnosticDetail.FINAL_PRODUCER_UNVERIFIED.value
            )
        )
    if final_producer_unverified_bound and not any(
        failure.get("kind") == "final-producer-unverified"
        for failure in final_failures
    ):
        final_failures.append(_final_producer_unverified_failure())
    summary["failures"] = sorted(
        [_invalid_plan_failure(invalid_plan_detail), *final_failures],
        key=_summary_failure_sort_key,
    )
    summary["diagnostics"] = sorted(
        [
            failure["diagnostic"]
            for failure in cast(
                "Sequence[Mapping[str, object]]",
                summary["failures"],
            )
            if isinstance(failure.get("diagnostic"), Mapping)
        ],
        key=lambda item: str(
            cast("Mapping[str, object]", item).get("diagnostic-id")
        ),
    )
    reason = summary.get("reason")
    if isinstance(reason, MutableMapping):
        reason["fail-closed"] = False
        reason["final-evidence-failure"] = bool(
            authority_failure_details or final_producer_unverified_bound
        )
        reason["final-producer-unverified"] = any(
            failure.get("kind") == "final-producer-unverified"
            for failure in final_failures
        )


def _summary_aggregate_manifest_producer_unverified(
    summary: Mapping[str, object],
) -> bool:
    final_manifest = _summary_final_aggregate_manifest(summary)
    reason = summary.get("reason")
    unbound_without_manifest_authority = (
        isinstance(reason, Mapping)
        and reason.get("aggregate-summary-without-manifest") is True
        and isinstance(final_manifest, Mapping)
        and not final_manifest.get("authority-diagnostics")
    )
    bound_final_manifest = (
        isinstance(final_manifest, Mapping)
        and isinstance(final_manifest.get("artifact-instance-id"), str)
        and bool(final_manifest.get("artifact-instance-id"))
        and isinstance(final_manifest.get("content-digest"), str)
        and bool(final_manifest.get("content-digest"))
    )
    return (
        isinstance(final_manifest, Mapping)
        and final_manifest.get("producer-verified") is False
        and _summary_has_final_producer_unverified_failure(summary)
        and (bound_final_manifest or unbound_without_manifest_authority)
    )


def _summary_final_aggregate_manifest(
    summary: Mapping[str, object],
) -> object:
    final_artifacts = summary.get("final-artifacts")
    if not isinstance(final_artifacts, Mapping):
        return None
    return final_artifacts.get("aggregate-evidence-manifest")


def _summary_has_final_producer_unverified_failure(
    summary: Mapping[str, object],
) -> bool:
    failures = summary.get("failures")
    if not isinstance(failures, Sequence) or isinstance(failures, str | bytes):
        return False
    return any(
        isinstance(failure, Mapping)
        and failure.get("kind") == "final-producer-unverified"
        for failure in failures
    )


def _summary_has_final_evidence_failure(
    summary: Mapping[str, object],
) -> bool:
    failures = summary.get("failures")
    if not isinstance(failures, Sequence) or isinstance(failures, str | bytes):
        return False
    return any(
        isinstance(failure, Mapping)
        and failure.get("kind") == "final-evidence-failure"
        for failure in failures
    )


def _validate_summary_final_producer_failure_coverage(
    summary: Mapping[str, object],
    issues: list[ValidationIssue],
) -> None:
    final_manifest = _summary_final_aggregate_manifest(summary)
    if (
        isinstance(final_manifest, Mapping)
        and final_manifest.get("producer-verified") is False
        and isinstance(final_manifest.get("artifact-instance-id"), str)
        and bool(final_manifest.get("artifact-instance-id"))
        and isinstance(final_manifest.get("content-digest"), str)
        and bool(final_manifest.get("content-digest"))
        and not _summary_has_final_producer_unverified_failure(summary)
    ):
        issues.append(
            ValidationIssue(
                "$.failures",
                "producer-verified false requires final-producer-unverified "
                "failure",
            )
        )


def _summary_manifest_claim_has_content_digest(
    summary: Mapping[str, object],
) -> bool:
    manifest_claim = summary.get("aggregate-evidence-manifest")
    return (
        isinstance(manifest_claim, Mapping)
        and isinstance(manifest_claim.get("content-digest"), str)
        and bool(manifest_claim.get("content-digest"))
    )


def _summary_aggregate_manifest_authority_failure_details(
    summary: Mapping[str, object],
) -> set[str]:
    final_artifacts = summary.get("final-artifacts")
    return _aggregate_manifest_authority_failure_details_from_final_artifacts(
        final_artifacts
    )


def _aggregate_manifest_authority_failure_details_from_final_artifacts(
    final_artifacts: object,
) -> set[str]:
    if not isinstance(final_artifacts, Mapping):
        return set()
    final_manifest = final_artifacts.get("aggregate-evidence-manifest")
    return _aggregate_manifest_authority_failure_details_from_final_manifest(
        final_manifest,
    )


def _aggregate_manifest_authority_failure_details_from_final_manifest(
    final_manifest: object,
) -> set[str]:
    if not isinstance(final_manifest, Mapping):
        return set()
    diagnostics = final_manifest.get("authority-diagnostics")
    if not isinstance(diagnostics, Sequence) or isinstance(
        diagnostics,
        str | bytes,
    ):
        return set()
    details: set[str] = set()
    for diagnostic in diagnostics:
        if not isinstance(diagnostic, Mapping):
            continue
        detail = diagnostic.get("detail")
        if (
            isinstance(detail, str)
            and detail in _AGGREGATE_MANIFEST_AUTHORITY_DETAILS
            and _aggregate_manifest_authority_diagnostic_is_canonical(
                diagnostic,
            )
        ):
            details.add(detail)
    return details


def _raise_retained_invalid_plan_manifest_authority_required() -> None:
    raise ContractValidationError(
        [
            ValidationIssue(
                "$.projection-authority",
                "retained invalid-plan details require aggregate manifest "
                "input authority",
            )
        ]
    )


def _bound_aggregate_manifest_authority_failure_details(
    summary: Mapping[str, object],
    aggregate_evidence_manifest: Mapping[str, object],
) -> set[str]:
    if _summary_bound_aggregate_manifest_digest_mismatch(
        summary,
        aggregate_evidence_manifest,
    ):
        return {"aggregate-evidence-manifest-digest-mismatch"}
    return set()


def _validate_summary_manifest_authority_diagnostics_match_context(
    summary: Mapping[str, object],
    authority_failure_details: set[str],
    aggregate_evidence_manifest: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> None:
    del aggregate_evidence_manifest
    final_manifest = _summary_final_aggregate_manifest(summary)
    diagnostics = (
        final_manifest.get("authority-diagnostics")
        if isinstance(final_manifest, Mapping)
        else None
    )
    has_noncanonical_authority_diagnostic = False
    if isinstance(diagnostics, Sequence) and not isinstance(
        diagnostics,
        str | bytes,
    ):
        has_noncanonical_authority_diagnostic = any(
            isinstance(diagnostic, Mapping)
            and not _aggregate_manifest_authority_diagnostic_is_canonical(
                diagnostic,
            )
            for diagnostic in diagnostics
        )
    summary_details = _summary_aggregate_manifest_authority_failure_details(
        summary
    )
    unsupported = summary_details - authority_failure_details
    if unsupported or has_noncanonical_authority_diagnostic:
        issues.append(
            ValidationIssue(
                "$.final-artifacts.aggregate-evidence-manifest."
                "authority-diagnostics",
                "must match supplied aggregate evidence manifest authority",
            )
        )


def _validate_aggregate_manifest_authority_diagnostic_details(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        return
    for index, item in enumerate(value):
        if not isinstance(item, Mapping):
            issues.append(
                ValidationIssue(
                    f"{path}[{index}]",
                    "must be a canonical aggregate evidence manifest "
                    "authority diagnostic",
                )
            )
            continue
        detail = item.get("detail")
        if detail not in _AGGREGATE_MANIFEST_AUTHORITY_DETAILS:
            issues.append(
                ValidationIssue(
                    f"{path}[{index}].detail",
                    "must be an aggregate evidence manifest authority detail",
                )
            )
            continue
        if not _aggregate_manifest_authority_diagnostic_is_canonical(item):
            issues.append(
                ValidationIssue(
                    f"{path}[{index}]",
                    "must be a canonical aggregate evidence manifest "
                    "authority diagnostic",
                )
            )


def _canonical_aggregate_manifest_authority_diagnostic(
    detail: str,
) -> dict[str, object]:
    return {
        "diagnostic-id": f"final-evidence-failure/{detail}",
        "code": DiagnosticFamily.FINAL_EVIDENCE_FAILURE.value,
        "detail": detail,
        "message": _AGGREGATE_MANIFEST_AUTHORITY_MESSAGES[detail],
        "source": {"type": "aggregation", "id": None},
        "severity": DiagnosticSeverity.FAIL_CLOSED.value,
        "verdict-effect": DiagnosticVerdictEffect.FAIL_CLOSED.value,
    }


def _aggregate_manifest_authority_diagnostic_is_canonical(
    diagnostic: Mapping[str, object],
) -> bool:
    detail = diagnostic.get("detail")
    if (
        not isinstance(detail, str)
        or detail not in _AGGREGATE_MANIFEST_AUTHORITY_DETAILS
    ):
        return False
    expected = _canonical_aggregate_manifest_authority_diagnostic(detail)
    return (
        diagnostic.keys() == expected.keys()
        and diagnostic.get("diagnostic-id") == expected["diagnostic-id"]
        and diagnostic.get("code") == expected["code"]
        and diagnostic.get("detail") == expected["detail"]
        and diagnostic.get("message")
        in _AGGREGATE_MANIFEST_AUTHORITY_MESSAGE_OPTIONS[detail]
        and diagnostic.get("source") == expected["source"]
        and diagnostic.get("severity") == expected["severity"]
        and diagnostic.get("verdict-effect") == expected["verdict-effect"]
    )


def _validate_summary_derived_status(  # noqa: PLR0913
    summary: Mapping[str, object],
    evidence_rows: Sequence[Mapping[str, object]],
    *,
    inadmissible_batch: bool,
    namespace_failure_details: set[str],
    plan_fail_closed_failure_causes: set[_FailClosedCause],
    required_input_failure: bool,
    aggregate_duration_exceeded: bool,
    aggregate_manifest_producer_unverified: bool,
    aggregate_manifest_authority_failure_details: set[str],
    aggregate_summary_without_manifest: bool,
    invalid_plan_input_failure_details: set[str],
    invalid_plan_expected_projection: Mapping[str, object],
    retained_invalid_plan_context_authorized: bool,
    issues: list[ValidationIssue],
) -> None:
    namespace_failure = bool(namespace_failure_details)
    plan_fail_closed_failure = bool(plan_fail_closed_failure_causes)
    aggregate_manifest_authority_failure = bool(
        aggregate_manifest_authority_failure_details
    )
    outcomes = [row.get("outcome") for row in evidence_rows]
    missing = sum(1 for outcome in outcomes if outcome == "missing")
    skipped = sum(1 for outcome in outcomes if outcome == "skipped")
    failed = sum(1 for outcome in outcomes if outcome == "failed")
    satisfied = sum(1 for outcome in outcomes if outcome == "satisfied")
    final_evidence_failure = (
        aggregate_manifest_authority_failure
        or aggregate_manifest_producer_unverified
    )
    invalid_plan_input_failure = bool(invalid_plan_input_failure_details)
    del aggregate_duration_exceeded, retained_invalid_plan_context_authorized
    expected_reason = {
        "invalid-plan": invalid_plan_input_failure,
        "fail-closed": (
            namespace_failure
            or plan_fail_closed_failure
            or required_input_failure
            or final_evidence_failure
            or aggregate_summary_without_manifest
        )
        and not invalid_plan_input_failure,
        "required-evidence-missing": missing > 0
        and not invalid_plan_input_failure,
        "required-evidence-skipped": skipped > 0
        and not invalid_plan_input_failure,
        "blocking-validation-failure": failed > 0
        and not invalid_plan_input_failure,
        "inadmissible-batch-evidence": inadmissible_batch
        and not invalid_plan_input_failure,
        "namespace-closure-failure": namespace_failure
        and not invalid_plan_input_failure,
        "required-input-artifact-failure": required_input_failure
        and not invalid_plan_input_failure,
        "aggregate-summary-without-manifest": aggregate_summary_without_manifest
        and not invalid_plan_input_failure,
        "final-producer-unverified": aggregate_manifest_producer_unverified
        and not invalid_plan_input_failure,
        "final-evidence-failure": final_evidence_failure
        and not invalid_plan_input_failure,
    }
    reason = summary.get("reason")
    if isinstance(reason, Mapping) and _is_invalid_plan_summary(summary):
        input_invalid_plan_detail = (
            _invalid_plan_failure_detail_from_detail_set(
                invalid_plan_input_failure_details
            )
        )
        summary_invalid_plan_detail = _invalid_plan_failure_detail_from_summary(
            summary
        )
        invalid_plan_detail = input_invalid_plan_detail
        if (
            invalid_plan_detail is None
            and _invalid_plan_detail_allows_retained_plan_context(
                summary_invalid_plan_detail
            )
            and _invalid_plan_summary_has_complete_retained_projection(summary)
        ):
            invalid_plan_detail = summary_invalid_plan_detail
        if invalid_plan_detail is None:
            invalid_plan_detail = summary_invalid_plan_detail
        effective_invalid_plan_expected_projection = (
            _no_authority_summary_projection()
            if _invalid_plan_detail_allows_no_authority_projection(
                invalid_plan_detail
            )
            and _summary_projection_matches(
                summary,
                _no_authority_summary_projection(),
            )
            else invalid_plan_expected_projection
        )
        _validate_invalid_plan_summary_mode(
            summary,
            evidence_rows,
            issues,
            invalid_plan_detail=invalid_plan_detail,
            expected_projection=effective_invalid_plan_expected_projection,
            aggregate_manifest_producer_unverified=(
                aggregate_manifest_producer_unverified
            ),
            aggregate_manifest_authority_failure_details=(
                aggregate_manifest_authority_failure_details
            ),
        )
        return
    required_failure_attributions = _required_summary_failure_attributions(
        summary,
        evidence_rows,
        {
            "namespace-closure-failure": namespace_failure,
            "fail-closed": plan_fail_closed_failure,
            "required-input-artifact-failure": required_input_failure,
            "aggregate-summary-without-manifest": (
                aggregate_summary_without_manifest
            ),
            "final-producer-unverified": (
                aggregate_manifest_producer_unverified
            ),
            "final-evidence-failure": final_evidence_failure,
        },
    )
    _validate_summary_failure_coverage(
        summary,
        required_failure_attributions,
        issues,
    )
    _validate_final_evidence_failure_details(
        summary,
        _final_evidence_failure_causes(
            required_input_failure=required_input_failure,
            aggregate_duration_exceeded=False,
            aggregate_manifest_producer_unverified=(
                aggregate_manifest_producer_unverified
            ),
            aggregate_summary_without_manifest=aggregate_summary_without_manifest,
            aggregate_manifest_authority_failure_details=(
                aggregate_manifest_authority_failure_details
            ),
        ),
        issues,
    )
    _validate_namespace_closure_failure_details(
        summary,
        namespace_failure_details,
        issues,
    )
    _validate_fail_closed_failure_details(
        summary,
        _fail_closed_failure_causes(
            namespace_failure_details=namespace_failure_details,
            plan_fail_closed_failure_causes=plan_fail_closed_failure_causes,
            required_input_failure=required_input_failure,
            aggregate_duration_exceeded=False,
            aggregate_manifest_producer_unverified=(
                aggregate_manifest_producer_unverified
            ),
            aggregate_summary_without_manifest=aggregate_summary_without_manifest,
            aggregate_manifest_authority_failure_details=(
                aggregate_manifest_authority_failure_details
            ),
        ),
        issues,
    )
    if isinstance(reason, Mapping):
        for key, expected in expected_reason.items():
            if reason.get(key) != expected:
                issues.append(
                    ValidationIssue(
                        f"$.reason.{key}", "must be derived from evidence"
                    )
                )
    expected_verdict = (
        "failed"
        if missing > 0
        or skipped > 0
        or failed > 0
        or inadmissible_batch
        or namespace_failure
        or plan_fail_closed_failure
        or required_input_failure
        or aggregate_manifest_producer_unverified
        or aggregate_summary_without_manifest
        or aggregate_manifest_authority_failure
        or invalid_plan_input_failure
        else "passed"
    )
    if summary.get("verdict") != expected_verdict:
        issues.append(
            ValidationIssue("$.verdict", "must be derived from evidence")
        )
    expected_work_groups = {
        "executable-required": len(evidence_rows),
        "required-succeeded": satisfied,
        "required-failed": failed,
        "required-skipped": skipped,
        "required-missing": missing,
        "terminal-aggregation": "present",
    }
    work_groups = summary.get("work-groups")
    if isinstance(work_groups, Mapping) and work_groups != expected_work_groups:
        issues.append(
            ValidationIssue(
                "$.work-groups",
                "must be derived from evidence results",
            )
        )


def _final_evidence_failure_causes(
    *,
    required_input_failure: bool,
    aggregate_duration_exceeded: bool,
    aggregate_manifest_producer_unverified: bool,
    aggregate_summary_without_manifest: bool,
    aggregate_manifest_authority_failure_details: set[str],
) -> set[str]:
    del required_input_failure
    del aggregate_duration_exceeded
    del aggregate_summary_without_manifest
    causes = set(aggregate_manifest_authority_failure_details)
    if aggregate_manifest_producer_unverified:
        causes.add(DiagnosticDetail.FINAL_PRODUCER_UNVERIFIED.value)
    return causes


type _FailClosedCause = tuple[str, str]


def _plan_fail_closed_failure_causes(
    plan: Mapping[str, object] | None,
) -> set[_FailClosedCause]:
    if plan is None:
        return set()
    diagnostics = plan.get("diagnostics")
    if not isinstance(diagnostics, Sequence) or isinstance(
        diagnostics, str | bytes
    ):
        return set()
    causes: set[_FailClosedCause] = set()
    for diagnostic in diagnostics:
        if not isinstance(diagnostic, Mapping):
            continue
        if (
            diagnostic.get("verdict-effect") != "fail-closed"
            and diagnostic.get("severity") != "fail-closed"
        ):
            continue
        code = diagnostic.get("code")
        detail = diagnostic.get("detail")
        if isinstance(code, str) and isinstance(detail, str):
            causes.add((code, detail))
    return causes


def _fail_closed_failure_causes(  # noqa: PLR0913
    *,
    namespace_failure_details: set[str],
    plan_fail_closed_failure_causes: set[_FailClosedCause],
    required_input_failure: bool,
    aggregate_duration_exceeded: bool,
    aggregate_manifest_producer_unverified: bool,
    aggregate_summary_without_manifest: bool,
    aggregate_manifest_authority_failure_details: set[str],
) -> set[_FailClosedCause]:
    causes = set(plan_fail_closed_failure_causes)
    del namespace_failure_details
    del required_input_failure
    del aggregate_duration_exceeded
    del aggregate_manifest_producer_unverified
    del aggregate_summary_without_manifest
    del aggregate_manifest_authority_failure_details
    return causes


def _validate_namespace_closure_failure_details(
    summary: Mapping[str, object],
    causes: set[str],
    issues: list[ValidationIssue],
) -> None:
    failures = summary.get("failures")
    if not isinstance(failures, Sequence) or isinstance(failures, str | bytes):
        return
    observed_details: list[str] = []
    for index, failure in enumerate(failures):
        if (
            not isinstance(failure, Mapping)
            or failure.get("kind") != "namespace-closure-failure"
        ):
            continue
        diagnostic = failure.get("diagnostic")
        detail = (
            diagnostic.get("detail")
            if isinstance(diagnostic, Mapping)
            else None
        )
        if isinstance(detail, str):
            observed_details.append(detail)
        if detail not in causes:
            issues.append(
                ValidationIssue(
                    f"$.failures[{index}].diagnostic.detail",
                    "must match actual namespace closure failure cause",
                )
            )
    observed_causes = set(observed_details)
    if len(observed_details) != len(observed_causes):
        issues.append(
            ValidationIssue(
                "$.failures",
                "must include at most one namespace closure failure per cause",
            )
        )
    if observed_causes != causes:
        issues.append(
            ValidationIssue(
                "$.failures",
                "must exactly cover namespace closure failure causes",
            )
        )


def _validate_fail_closed_failure_details(
    summary: Mapping[str, object],
    causes: set[_FailClosedCause],
    issues: list[ValidationIssue],
) -> None:
    failures = summary.get("failures")
    if not isinstance(failures, Sequence) or isinstance(failures, str | bytes):
        return
    observed_causes: list[_FailClosedCause] = []
    for index, failure in enumerate(failures):
        if (
            not isinstance(failure, Mapping)
            or failure.get("kind") != "fail-closed"
        ):
            continue
        diagnostic = failure.get("diagnostic")
        if not isinstance(diagnostic, Mapping):
            continue
        code = diagnostic.get("code")
        detail = diagnostic.get("detail")
        observed = (code, detail)
        if isinstance(code, str) and isinstance(detail, str):
            observed_causes.append((code, detail))
        if observed not in causes:
            issues.append(
                ValidationIssue(
                    f"$.failures[{index}].diagnostic",
                    "must match actual fail-closed cause",
                )
            )
    observed_cause_set = set(observed_causes)
    if len(observed_causes) != len(observed_cause_set):
        issues.append(
            ValidationIssue(
                "$.failures",
                "must include at most one fail-closed failure per cause",
            )
        )
    if observed_cause_set != causes:
        issues.append(
            ValidationIssue(
                "$.failures",
                "must exactly cover fail-closed failure causes",
            )
        )


def _summary_has_missing_manifest_failure(
    summary: Mapping[str, object],
) -> bool:
    failures = summary.get("failures")
    if not isinstance(failures, Sequence) or isinstance(failures, str | bytes):
        return False
    for failure in failures:
        if not isinstance(failure, Mapping):
            continue
        if failure.get("kind") not in {
            "aggregate-summary-without-manifest",
            "final-evidence-failure",
            "fail-closed",
        }:
            continue
        diagnostic = failure.get("diagnostic")
        if (
            isinstance(diagnostic, Mapping)
            and diagnostic.get("detail") == "aggregate-summary-without-manifest"
        ):
            return True
    return False


def _validate_final_evidence_failure_details(
    summary: Mapping[str, object],
    causes: set[str],
    issues: list[ValidationIssue],
) -> None:
    failures = summary.get("failures")
    if not isinstance(failures, Sequence) or isinstance(failures, str | bytes):
        return
    observed_details: list[str] = []
    for index, failure in enumerate(failures):
        if not isinstance(failure, Mapping):
            continue
        if failure.get("kind") != "final-evidence-failure":
            continue
        diagnostic = failure.get("diagnostic")
        detail = (
            diagnostic.get("detail")
            if isinstance(diagnostic, Mapping)
            else None
        )
        if isinstance(detail, str):
            observed_details.append(detail)
        if detail not in causes:
            issues.append(
                ValidationIssue(
                    f"$.failures[{index}].diagnostic.detail",
                    "must match actual final evidence failure cause",
                )
            )
    observed_causes = set(observed_details)
    if len(observed_details) != len(observed_causes):
        issues.append(
            ValidationIssue(
                "$.failures",
                "must include at most one final evidence failure per cause",
            )
        )
    if observed_causes != causes:
        issues.append(
            ValidationIssue(
                "$.failures",
                "must exactly cover final evidence failure causes",
            )
        )


def _validate_invalid_plan_summary_mode(  # noqa: C901,PLR0912,PLR0913,PLR0915
    summary: Mapping[str, object],
    evidence_rows: Sequence[Mapping[str, object]],
    issues: list[ValidationIssue],
    *,
    invalid_plan_detail: str | None = None,
    expected_projection: Mapping[str, object],
    aggregate_manifest_producer_unverified: bool,
    aggregate_manifest_authority_failure_details: set[str],
) -> None:
    expected_fail_closed_causes: set[_FailClosedCause] = set()
    fail_closed_invalid_plan = (
        _canonical_fail_closed_invalid_plan_failure(summary) is not None
    )
    if summary.get("verdict") != "failed":
        issues.append(ValidationIssue("$.verdict", "must be failed"))
    no_authority_projection = _no_authority_summary_projection()
    summary_has_no_authority_projection = _summary_projection_matches(
        summary,
        no_authority_projection,
    )
    expected_final_failure_details = set(
        aggregate_manifest_authority_failure_details
    )
    if aggregate_manifest_producer_unverified:
        expected_final_failure_details.add(
            DiagnosticDetail.FINAL_PRODUCER_UNVERIFIED.value
        )
    no_authority_expected = expected_projection == no_authority_projection
    no_authority_detail_allowed = (
        _invalid_plan_detail_allows_no_authority_projection(invalid_plan_detail)
    )
    if (
        invalid_plan_detail == DiagnosticDetail.PLAN_MISSING.value
        and not summary_has_no_authority_projection
    ):
        issues.append(
            ValidationIssue(
                "$.projection-authority",
                "plan-missing invalid-plan details must use no-authority "
                "projection context",
            )
        )
    elif no_authority_expected and not no_authority_detail_allowed:
        issues.append(
            ValidationIssue(
                "$.projection-authority",
                "retained invalid-plan details must preserve complete "
                "producer-compatible projection context",
            )
        )
    elif no_authority_detail_allowed and summary_has_no_authority_projection:
        pass
    elif not _invalid_plan_summary_allows_retained_projection(
        summary,
        invalid_plan_detail,
        expected_projection,
    ):
        _validate_summary_projection_matches(
            summary,
            expected_projection,
            "invalid-plan context",
            issues,
        )
    batch_bundles = summary.get("batch-bundles")
    if (
        isinstance(batch_bundles, Sequence)
        and not isinstance(batch_bundles, str | bytes)
        and len(batch_bundles) != 0
    ):
        issues.append(
            ValidationIssue(
                "$.batch-bundles",
                "must be empty for invalid-plan summary mode",
            )
        )
    if evidence_rows:
        issues.append(
            ValidationIssue(
                "$.evidence-results",
                "must be empty for invalid plan",
            )
        )
    reason = summary.get("reason")
    failures = summary.get("failures")
    if isinstance(reason, Mapping):
        expected_reason = dict.fromkeys(_SUMMARY_REASON_KEYS, False)
        expected_reason["invalid-plan"] = not fail_closed_invalid_plan
        expected_reason["fail-closed"] = fail_closed_invalid_plan
        expected_reason["final-producer-unverified"] = (
            aggregate_manifest_producer_unverified
        )
        expected_reason["final-evidence-failure"] = bool(
            expected_final_failure_details
        )
        for key, expected in expected_reason.items():
            if reason.get(key) != expected:
                issues.append(
                    ValidationIssue(
                        f"$.reason.{key}",
                        "must match invalid-plan summary mode",
                    )
                )
    budgets = summary.get("budgets")
    if isinstance(budgets, Mapping):
        for key in ("actual-execution-batches", "actual-windows-jobs"):
            if budgets.get(key) != 0:
                issues.append(ValidationIssue(f"$.budgets.{key}", "must be 0"))
    expected_work_groups = {
        "executable-required": 0,
        "required-succeeded": 0,
        "required-failed": 0,
        "required-skipped": 0,
        "required-missing": 0,
        "terminal-aggregation": "present",
    }
    work_groups = summary.get("work-groups")
    if isinstance(work_groups, Mapping) and work_groups != expected_work_groups:
        issues.append(
            ValidationIssue(
                "$.work-groups",
                "must be zeroed for invalid-plan summary mode",
            )
        )
    if not isinstance(failures, Sequence) or isinstance(failures, str | bytes):
        return
    invalid_plan_failure_kind = (
        "fail-closed" if fail_closed_invalid_plan else "invalid-plan"
    )
    invalid_plan_failures = [
        failure
        for failure in failures
        if isinstance(failure, Mapping)
        and failure.get("kind") == invalid_plan_failure_kind
    ]
    if len(invalid_plan_failures) != 1:
        issues.append(
            ValidationIssue(
                "$.failures",
                f"must contain exactly one {invalid_plan_failure_kind} failure",
            )
        )
    else:
        expected_failure = (
            _fail_closed_invalid_plan_failure(invalid_plan_detail)
            if fail_closed_invalid_plan
            else _invalid_plan_failure(invalid_plan_detail)
        )
        if not _failure_matches_canonical_identity(
            invalid_plan_failures[0],
            expected_failure,
        ):
            issues.append(
                ValidationIssue(
                    "$.failures",
                    f"must match canonical {invalid_plan_failure_kind} failure",
                )
            )
    if not fail_closed_invalid_plan and any(
        isinstance(failure, Mapping) and failure.get("kind") == "fail-closed"
        for failure in failures
    ):
        issues.append(
            ValidationIssue(
                "$.failures",
                "must not contain fail-closed invalid-plan failures",
            )
        )
    unexpected_failures = [
        failure
        for failure in failures
        if isinstance(failure, Mapping)
        and failure.get("kind")
        not in {
            "invalid-plan",
            *(["fail-closed"] if fail_closed_invalid_plan else []),
            "final-producer-unverified",
            "final-evidence-failure",
        }
    ]
    if unexpected_failures:
        issues.append(
            ValidationIssue(
                "$.failures",
                "must contain only invalid-plan or final evidence failures",
            )
        )
    if (
        any(
            isinstance(failure, Mapping)
            and failure.get("kind") == "final-producer-unverified"
            for failure in failures
        )
        and not aggregate_manifest_producer_unverified
    ):
        issues.append(
            ValidationIssue(
                "$.failures",
                "final-producer-unverified requires a bound unverified final "
                "manifest producer",
            )
        )
    if aggregate_manifest_producer_unverified and not any(
        isinstance(failure, Mapping)
        and failure.get("kind") == "final-producer-unverified"
        for failure in failures
    ):
        issues.append(
            ValidationIssue(
                "$.failures",
                "unverified final manifest producer requires "
                "final-producer-unverified failure",
            )
        )
    _validate_final_evidence_failure_details(
        summary,
        expected_final_failure_details,
        issues,
    )
    if not fail_closed_invalid_plan:
        _validate_fail_closed_failure_details(
            summary,
            expected_fail_closed_causes,
            issues,
        )


def _invalid_plan_detail_allows_retained_plan_context(
    detail: str | None,
) -> bool:
    return detail in _invalid_plan_retained_context_details()


def _invalid_plan_retained_context_details() -> set[str]:
    return set(CI_VALIDATION_INVALID_PLAN_RETAINED_PROJECTION_DETAILS)


def _preferred_retained_invalid_plan_detail(details: set[str]) -> str:
    return preferred_ci_validation_invalid_plan_retained_projection_detail(
        details,
    )


def _invalid_plan_detail_allows_no_authority_projection(
    detail: str | None,
) -> bool:
    return detail in _INVALID_PLAN_NO_AUTHORITY_PROJECTION_DETAILS


def _invalid_plan_summary_allows_retained_projection(
    summary: Mapping[str, object],
    invalid_plan_detail: str | None,
    expected_projection: Mapping[str, object],
) -> bool:
    return (
        _invalid_plan_detail_allows_retained_plan_context(invalid_plan_detail)
        and expected_projection != _no_authority_summary_projection()
        and _summary_projection_matches(summary, expected_projection)
        and _invalid_plan_summary_has_complete_retained_projection(summary)
    )


def _invalid_plan_summary_has_complete_retained_projection(
    summary: Mapping[str, object],
) -> bool:
    validation_tree = summary.get("validation-tree")
    affected_range = summary.get("affected-range")
    request = summary.get("request")
    scheduled_full = summary.get("scheduled-full")
    mode = summary.get("mode")
    return (
        isinstance(summary.get("plan-id"), str)
        and summary.get("plan-id") != ""
        and isinstance(summary.get("plan-digest"), str)
        and _DIGEST_RE.fullmatch(cast("str", summary.get("plan-digest")))
        is not None
        and mode in _MODES
        and isinstance(validation_tree, Mapping)
        and set(validation_tree) == {"commit-sha", "ref"}
        and isinstance(validation_tree.get("commit-sha"), str)
        and _SHA_RE.fullmatch(cast("str", validation_tree.get("commit-sha")))
        is not None
        and isinstance(validation_tree.get("ref"), str)
        and validation_tree.get("ref") != ""
        and isinstance(affected_range, Mapping)
        and set(affected_range)
        == {
            "status",
            "base-sha",
            "base-tip-sha",
            "head-sha",
            "changed-files-hash",
        }
        and affected_range.get("status") in (_AFFECTED_STATUSES - {"unknown"})
        and all(
            value is None
            or (isinstance(value, str) and _SHA_RE.fullmatch(value) is not None)
            for value in (
                affected_range.get("base-sha"),
                affected_range.get("base-tip-sha"),
                affected_range.get("head-sha"),
            )
        )
        and (
            affected_range.get("changed-files-hash") is None
            or (
                isinstance(affected_range.get("changed-files-hash"), str)
                and _DIGEST_RE.fullmatch(
                    cast("str", affected_range.get("changed-files-hash"))
                )
                is not None
            )
        )
        and _invalid_plan_summary_has_complete_retained_request(request)
        and isinstance(scheduled_full, Mapping)
        and set(scheduled_full) == {"enabled"}
        and scheduled_full.get("enabled") is (mode == "scheduled_full")
    )


def _invalid_plan_summary_has_complete_retained_request(
    request: object,
) -> bool:
    if not isinstance(request, Mapping):
        return False
    artifact_ref = request.get("artifact-ref")
    request_digest = request.get("request-digest")
    if (
        not isinstance(artifact_ref, str)
        or not isinstance(request_digest, str)
        or _DIGEST_RE.fullmatch(request_digest) is None
    ):
        return False
    try:
        validate_artifact_logical_ref(artifact_ref)
    except ContractValidationError:
        return False
    return True


type _FailureAttribution = tuple[
    str,
    object,
    object,
    object,
    object,
]


def _required_summary_failure_attributions(
    summary: Mapping[str, object],
    evidence_rows: Sequence[Mapping[str, object]],
    condition_failures: Mapping[str, bool],
) -> set[_FailureAttribution]:
    required: set[_FailureAttribution] = set()
    _add_evidence_failure_attributions(required, evidence_rows)
    _add_batch_failure_attributions(required, summary)
    for kind, condition in condition_failures.items():
        if condition:
            required.add((kind, None, None, None, None))
    reason = summary.get("reason")
    if isinstance(reason, Mapping):
        for kind in _SUMMARY_FAILURE_KINDS:
            if reason.get(kind) is not True:
                continue
            if kind == "fail-closed":
                continue
            if not any(item[0] == kind for item in required):
                required.add((kind, None, None, None, None))
    return required


def _add_evidence_failure_attributions(
    required: set[_FailureAttribution],
    evidence_rows: Sequence[Mapping[str, object]],
) -> None:
    outcome_failures = {
        "missing": "required-evidence-missing",
        "skipped": "required-evidence-skipped",
        "failed": "blocking-validation-failure",
    }
    for row in evidence_rows:
        kind = outcome_failures.get(str(row.get("outcome")))
        if kind is None:
            continue
        required.add(
            (
                kind,
                row.get("evidence-expectation-id"),
                row.get("work-group-id"),
                row.get("batch-id"),
                row.get("bundle-id"),
            )
        )


def _add_batch_failure_attributions(
    required: set[_FailureAttribution],
    summary: Mapping[str, object],
) -> None:
    batch_rows = summary.get("batch-bundles")
    if not isinstance(batch_rows, Sequence) or isinstance(
        batch_rows, str | bytes
    ):
        return
    for row in batch_rows:
        if not isinstance(row, Mapping) or row.get("admissibility") == "valid":
            continue
        required.add(
            (
                "inadmissible-batch-evidence",
                None,
                None,
                row.get("batch-id"),
                None,
            )
        )


def _validate_summary_failure_coverage(
    summary: Mapping[str, object],
    required_failure_attributions: set[_FailureAttribution],
    issues: list[ValidationIssue],
) -> None:
    failures = summary.get("failures")
    if not isinstance(failures, Sequence) or isinstance(failures, str | bytes):
        return
    observed_list = [
        _summary_failure_attribution(failure)
        for failure in failures
        if isinstance(failure, Mapping) and isinstance(failure.get("kind"), str)
    ]
    observed = set(observed_list)
    duplicate_checked = [
        attribution
        for attribution in observed_list
        if attribution[0]
        not in {
            "fail-closed",
            "final-evidence-failure",
            "namespace-closure-failure",
        }
    ]
    if len(duplicate_checked) != len(set(duplicate_checked)):
        issues.append(
            ValidationIssue(
                "$.failures",
                "must not contain duplicate failure attributions",
            )
        )
    for attribution in sorted(required_failure_attributions - observed):
        issues.append(
            ValidationIssue(
                "$.failures",
                f"must include {_format_failure_attribution(attribution)}",
            ),
        )
    for attribution in sorted(observed - required_failure_attributions):
        issues.append(
            ValidationIssue(
                "$.failures",
                "must not include "
                f"{_format_failure_attribution(attribution)} "
                "without corresponding failure",
            ),
        )


def _summary_failure_attribution(
    failure: Mapping[str, object],
) -> _FailureAttribution:
    return (
        str(failure["kind"]),
        failure.get("evidence-expectation-id"),
        failure.get("work-group-id"),
        failure.get("batch-id"),
        failure.get("bundle-id"),
    )


def _format_failure_attribution(
    attribution: _FailureAttribution,
) -> str:
    kind, evidence_id, work_group_id, batch_id, bundle_id = attribution
    return (
        kind
        + "["
        + ",".join(
            (
                f"evidence-expectation-id={evidence_id}",
                f"work-group-id={work_group_id}",
                f"batch-id={batch_id}",
                f"bundle-id={bundle_id}",
            )
        )
        + "]"
    )


def _summary_requires_admitted_bundle_payloads(
    summary: Mapping[str, object],
    aggregate_manifest: Mapping[str, object] | None,
) -> bool:
    if aggregate_manifest is not None:
        manifest_rows = aggregate_manifest.get("batch-bundles")
        if isinstance(manifest_rows, Sequence) and not isinstance(
            manifest_rows, str | bytes
        ):
            for row in manifest_rows:
                if (
                    isinstance(row, Mapping)
                    and row.get("slot-admissibility") == "valid"
                    and isinstance(row.get("admitted-candidate-id"), str)
                ):
                    return True
    evidence_rows = summary.get("evidence-results")
    if isinstance(evidence_rows, Sequence) and not isinstance(
        evidence_rows, str | bytes
    ):
        return any(
            isinstance(row, Mapping) and row.get("outcome") == "satisfied"
            for row in evidence_rows
        )
    return False


def _summary_freezer_requires_manifest_document(
    summary: Mapping[str, object],
    plan: Mapping[str, object] | None,
    admitted_batch_evidence_bundles: Sequence[Mapping[str, object]] | None,
    execution_batch_manifest: Mapping[str, object] | None,
) -> bool:
    if not _is_invalid_plan_summary(summary):
        return True
    if (
        plan is not None
        or execution_batch_manifest is not None
        or admitted_batch_evidence_bundles is not None
    ):
        return True
    batch_rows = summary.get("batch-bundles")
    if (
        isinstance(batch_rows, Sequence)
        and not isinstance(batch_rows, str | bytes)
        and any(
            isinstance(row, Mapping) and row.get("admissibility") == "valid"
            for row in batch_rows
        )
    ):
        return True
    evidence_rows = summary.get("evidence-results")
    if isinstance(evidence_rows, Sequence) and not isinstance(
        evidence_rows, str | bytes
    ):
        return any(
            isinstance(row, Mapping) and row.get("outcome") == "satisfied"
            for row in evidence_rows
        )
    return False


def _aggregate_namespace_failure_details(
    aggregate_manifest: Mapping[str, object],
) -> set[str]:
    details: set[str] = set()
    unexpected = aggregate_manifest.get("unexpected-contract-artifacts")
    if (
        isinstance(unexpected, Sequence)
        and not isinstance(unexpected, str | bytes)
        and len(unexpected) > 0
    ):
        details.add("unexpected-contract-artifact")
    overflow = aggregate_manifest.get("namespace-overflow")
    if isinstance(overflow, Mapping):
        observed = overflow.get("observed-prefixed-artifact-count-lower-bound")
        if overflow.get("detected") is True or (
            isinstance(observed, int)
            and observed > _MAX_PREFINAL_VALIDATION_ARTIFACTS
        ):
            details.add("namespace-overflow")
        diagnostics = overflow.get("diagnostics")
        if isinstance(diagnostics, Sequence) and not isinstance(
            diagnostics, str | bytes
        ):
            for diagnostic in diagnostics:
                if (
                    isinstance(diagnostic, Mapping)
                    and diagnostic.get("code") == "namespace-closure-failure"
                    and diagnostic.get("detail")
                    == "namespace-enumeration-unavailable"
                ):
                    details.add("namespace-enumeration-unavailable")
    return details


def _aggregate_required_input_failure(
    aggregate_manifest: Mapping[str, object],
) -> bool:
    inputs = aggregate_manifest.get("input-artifacts")
    if not isinstance(inputs, Mapping):
        return False
    return any(
        isinstance(item, Mapping)
        and item.get("required") is True
        and item.get("admissibility") != "valid"
        for item in inputs.values()
    )


def _aggregate_execution_batch_manifest_input_not_valid(
    aggregate_manifest: Mapping[str, object],
) -> bool:
    inputs = aggregate_manifest.get("input-artifacts")
    if not isinstance(inputs, Mapping):
        return False
    execution_manifest = inputs.get("execution-batch-manifest")
    return (
        isinstance(execution_manifest, Mapping)
        and execution_manifest.get("required") is True
        and execution_manifest.get("admissibility") != "valid"
    )


def _validate_validation_tree(
    value: object,
    path: str,
    *,
    allow_unknown: bool,
    issues: list[ValidationIssue],
) -> None:
    tree = _validate_object(
        value,
        frozenset({"commit-sha", "ref"}),
        path,
        issues,
    )
    if tree is None:
        return
    commit_sha = tree.get("commit-sha")
    if commit_sha is None and allow_unknown:
        pass
    elif (
        not isinstance(commit_sha, str) or _SHA_RE.fullmatch(commit_sha) is None
    ):
        issues.append(ValidationIssue(f"{path}.commit-sha", "must be SHA-1"))
    _validate_nullable_non_empty_string(tree.get("ref"), f"{path}.ref", issues)


def _validate_affected_range(
    value: object,
    issues: list[ValidationIssue],
    path: str = "$.affected-range",
) -> None:
    affected = _validate_object(
        value,
        frozenset(
            {
                "status",
                "base-sha",
                "base-tip-sha",
                "head-sha",
                "changed-files-hash",
            },
        ),
        path,
        issues,
    )
    if affected is None:
        return
    if affected.get("status") not in _AFFECTED_STATUSES:
        issues.append(ValidationIssue(f"{path}.status", "is not registered"))
    for key in ("base-sha", "base-tip-sha", "head-sha"):
        sha = affected.get(key)
        if sha is not None and (
            not isinstance(sha, str) or _SHA_RE.fullmatch(sha) is None
        ):
            issues.append(
                ValidationIssue(f"{path}.{key}", "must be null or SHA-1")
            )
    _validate_nullable_digest(
        affected.get("changed-files-hash"),
        f"{path}.changed-files-hash",
        issues,
    )


def _validate_scheduled_full(
    value: object,
    issues: list[ValidationIssue],
    path: str = "$.scheduled-full",
) -> None:
    scheduled = _validate_object(value, frozenset({"enabled"}), path, issues)
    if scheduled is not None and not isinstance(scheduled.get("enabled"), bool):
        issues.append(ValidationIssue(f"{path}.enabled", "must be boolean"))


def _validate_request_summary(
    value: object,
    issues: list[ValidationIssue],
    path: str = "$.request",
) -> None:
    request = _validate_object(
        value,
        frozenset({"artifact-ref", "request-digest"}),
        path,
        issues,
    )
    if request is None:
        return
    _validate_nullable_artifact_ref(
        request.get("artifact-ref"),
        f"{path}.artifact-ref",
        issues,
    )
    _validate_nullable_digest(
        request.get("request-digest"),
        f"{path}.request-digest",
        issues,
    )


def _validate_summary_request_matches_aggregate_manifest(
    summary: Mapping[str, object],
    aggregate_manifest: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> None:
    if aggregate_manifest is None or _is_invalid_plan_summary(summary):
        return
    if aggregate_manifest.get(
        "projection-authority"
    ) is None and _summary_projection_matches(
        summary, _no_authority_summary_projection()
    ):
        return
    if _aggregate_manifest_has_no_authoritative_plan(
        aggregate_manifest
    ) and _summary_projection_matches(
        summary, _no_authority_summary_projection()
    ):
        return
    summary_request = summary.get("request")
    input_artifacts = aggregate_manifest.get("input-artifacts")
    if not isinstance(summary_request, Mapping) or not isinstance(
        input_artifacts, Mapping
    ):
        return
    manifest_request = input_artifacts.get("request")
    comparisons = (
        {
            "artifact-ref": manifest_request.get("artifact-ref"),
            "request-digest": manifest_request.get("content-digest"),
        }
        if isinstance(manifest_request, Mapping)
        and manifest_request.get("admissibility") == "valid"
        else {"artifact-ref": None, "request-digest": None}
    )
    for key, expected in comparisons.items():
        if summary_request.get(key) != expected:
            issues.append(
                ValidationIssue(
                    f"$.request.{key}",
                    "must match aggregate evidence manifest request",
                )
            )


def _summary_affected_range(plan: Mapping[str, object]) -> dict[str, object]:
    affected_value = plan.get("affected-range")
    if not isinstance(affected_value, Mapping):
        raise ContractValidationError(
            [ValidationIssue("plan.affected-range", "is required")]
        )
    affected = affected_value
    missing_keys = [
        key
        for key in ("status", "base-sha", "base-tip-sha", "head-sha")
        if key not in affected
    ]
    if missing_keys:
        raise ContractValidationError(
            [
                ValidationIssue(f"plan.affected-range.{key}", "is required")
                for key in missing_keys
            ]
        )
    return {
        "status": affected["status"],
        "base-sha": affected["base-sha"],
        "base-tip-sha": affected["base-tip-sha"],
        "head-sha": affected["head-sha"],
        "changed-files-hash": affected.get("changed-files-hash") or None,
    }


def _unexpected_implicit_id(
    item: Mapping[str, object],
    *,
    run_id: str,
    run_attempt: str,
) -> str:
    physical_name = _sort_component(item.get("physical-artifact-name"))
    observed_physical_name = _sort_component(
        item.get("observed-physical-artifact-name")
    )
    instance_id = _sort_component(item.get("artifact-instance-id"))
    classification = _sort_component(item.get("classification"))
    preimage = {
        "run-id": run_id,
        "run-attempt": run_attempt,
        "physical-artifact-name": physical_name,
        "artifact-instance-id": instance_id,
        "classification": classification,
    }
    if observed_physical_name:
        preimage["observed-physical-artifact-name"] = observed_physical_name
    return canonical_json_digest(preimage)


def _mapping(value: object) -> Mapping[str, object]:
    if isinstance(value, Mapping):
        return value
    raise ContractValidationError([ValidationIssue("value", "must be object")])


def _sequence(value: object) -> Sequence[object]:
    if isinstance(value, Sequence) and not isinstance(value, str | bytes):
        return value
    raise ContractValidationError([ValidationIssue("value", "must be array")])
