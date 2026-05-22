"""Execution-batch CI validation contracts.

These helpers implement execution-batch validation artifacts introduced by
the execution-batch model.
"""

from __future__ import annotations

import hashlib
import re
from collections.abc import Mapping, MutableMapping, Sequence

from three_workflow_release_contracts.ci_validation import (
    API_VERSIONS_BY_KIND,
    CiValidationKind,
    CommonEnvelope,
    DiagnosticDetail,
    DiagnosticFamily,
    artifact_physical_name,
    canonical_json_bytes,
    canonical_json_digest,
    validate_artifact_logical_ref,
    validate_artifact_physical_name,
    validate_ci_validation_diagnostic_record,
    validate_common_envelope,
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
_RUNNER_FAMILIES = frozenset({"windows", "ubuntu"})
_ECOSYSTEMS = frozenset({"dotnet", "python", "javascript", "typescript"})
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
_EXPECTED_FINAL_VALIDATION_ARTIFACTS = 2
_MAX_VALIDATION_ARTIFACTS = 20
_MAX_PREFINAL_VALIDATION_ARTIFACTS = (
    _MAX_VALIDATION_ARTIFACTS - _EXPECTED_FINAL_VALIDATION_ARTIFACTS
)
_MAX_TOTAL_JOBS = 18
_MAX_WINDOWS_JOBS = 8
_MAX_EXECUTION_BATCHES = 13
_G1_FINAL_EVIDENCE_DETAILS = frozenset(
    {
        "aggregate-duration-exceeded",
        "aggregate-evidence-manifest-missing",
        "aggregate-evidence-manifest-duplicate",
        "aggregate-evidence-manifest-unreadable",
        "aggregate-evidence-manifest-malformed",
        "aggregate-evidence-manifest-non-canonical",
        "aggregate-evidence-manifest-digest-mismatch",
        "aggregate-summary-missing",
        "aggregate-summary-duplicate",
        "aggregate-summary-unreadable",
        "aggregate-summary-malformed",
        "aggregate-summary-non-canonical",
        "aggregate-summary-digest-mismatch",
        "aggregate-summary-without-manifest",
        "required-input-artifact-failure",
        "execution-batch-manifest-missing",
        "execution-batch-manifest-duplicate",
        "execution-batch-manifest-unreadable",
        "execution-batch-manifest-malformed",
        "execution-batch-manifest-non-canonical",
        "execution-batch-manifest-digest-mismatch",
        "execution-batch-manifest-plan-mismatch",
        "namespace-overflow",
    }
)
_G1_DETAILS_BY_DIAGNOSTIC_CODE = {
    DiagnosticFamily.REQUIRED_EVIDENCE_MISSING.value: frozenset(
        {DiagnosticDetail.MISSING_BUNDLE.value}
    ),
    DiagnosticFamily.REQUIRED_EVIDENCE_SKIPPED.value: frozenset(
        {DiagnosticDetail.DEPENDENCY_BLOCKED.value}
    ),
    DiagnosticFamily.BLOCKING_VALIDATION_FAILURE.value: frozenset(
        {DiagnosticDetail.BLOCKING_VALIDATION_FAILURE.value}
    ),
    DiagnosticFamily.INADMISSIBLE_BATCH_EVIDENCE.value: frozenset(
        {
            DiagnosticDetail.MALFORMED_BUNDLE.value,
            DiagnosticDetail.MISSING_BUNDLE.value,
            DiagnosticDetail.EXECUTION_BATCH_MANIFEST_MISSING.value,
            DiagnosticDetail.EXECUTION_BATCH_MANIFEST_MALFORMED.value,
            DiagnosticDetail.EXECUTION_BATCH_MANIFEST_DIGEST_MISMATCH.value,
            DiagnosticDetail.EXECUTION_BATCH_MANIFEST_PLAN_MISMATCH.value,
            DiagnosticDetail.EXECUTION_BATCH_MANIFEST_BUNDLE_REF_MISMATCH.value,
        }
    ),
    DiagnosticFamily.NAMESPACE_CLOSURE_FAILURE.value: frozenset(
        {
            DiagnosticDetail.UNEXPECTED_CONTRACT_ARTIFACT.value,
            DiagnosticDetail.NAMESPACE_OVERFLOW.value,
        }
    ),
    DiagnosticFamily.AGGREGATE_DURATION_EXCEEDED.value: frozenset(
        {DiagnosticDetail.AGGREGATE_DURATION_EXCEEDED.value}
    ),
    DiagnosticFamily.FINAL_EVIDENCE_FAILURE.value: _G1_FINAL_EVIDENCE_DETAILS,
}

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
        "observed-workflow",
        "observed-job",
        "observed-matrix",
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
    },
)
_FINAL_AGGREGATE_SUMMARY_ENTRY_KEYS = frozenset(
    {
        "artifact-ref",
    },
)
_SUMMARY_REASON_KEYS = frozenset(
    {
        "invalid-plan",
        "fail-closed",
        "required-evidence-missing",
        "required-evidence-skipped",
        "blocking-validation-failure",
        "inadmissible-batch-evidence",
        "namespace-closure-failure",
        "aggregate-duration-exceeded",
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
        "message": "No authoritative validation plan was available.",
        "source": {"type": "aggregation", "id": None},
        "severity": "fail-closed",
        "verdict-effect": "fail-closed",
    },
    "message": "No authoritative validation plan was available.",
}


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


def freeze_ci_validation_execution_batch_manifest(
    *,
    plan: Mapping[str, object],
    batches: Sequence[Mapping[str, object]],
    budget: Mapping[str, object],
    created_at: str,
) -> dict[str, object]:
    """Freeze a post-plan execution-batch manifest."""
    envelope = _envelope(plan, CiValidationKind.PLAN)
    _verified_plan_digest(plan)
    frozen_batches = sorted(
        (dict(batch) for batch in batches),
        key=lambda item: str(item.get("batch-id")),
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
        "plan-id": plan["plan-id"],
        "plan-digest": _verified_plan_digest(plan),
        "budget": dict(budget),
        "batches": frozen_batches,
    }
    validate_ci_validation_execution_batch_manifest(manifest, plan=plan)
    return manifest


def validate_ci_validation_execution_batch_manifest(
    manifest: object,
    *,
    plan: Mapping[str, object] | None = None,
    expected_envelope: CommonEnvelope | None = None,
    expected_run_id: str | None = None,
    expected_run_attempt: str | None = None,
) -> None:
    """Validate an execution-batch manifest."""
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
    _validate_root_keys(manifest, _EXECUTION_BATCH_MANIFEST_KEYS, "$", issues)
    _validate_g1_schema_diagnostics(manifest.get("schema-diagnostics"), issues)
    _validate_expected_run(
        envelope, expected_run_id, expected_run_attempt, issues
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
    if plan is not None:
        plan_envelope = _validated_plan_envelope(plan, issues)
        if envelope is not None and plan_envelope is not None:
            _validate_envelope_matches(envelope, plan_envelope, issues)
        if manifest.get("plan-id") != plan.get("plan-id"):
            issues.append(ValidationIssue("$.plan-id", "must match plan"))
        if manifest.get("plan-digest") != _verified_plan_digest_or_none(plan):
            issues.append(ValidationIssue("$.plan-digest", "must match plan"))
        plan_work_groups = _work_groups_by_id(plan)
        plan_evidence_expectations = _evidence_expectations_by_id(plan)
        executable_work_group_ids = {
            item_id
            for item_id, group in plan_work_groups.items()
            if group.get("kind") != "evidence-aggregation"
        }
    else:
        _validate_non_empty_string(manifest.get("plan-id"), "$.plan-id", issues)
        _validate_digest(manifest.get("plan-digest"), "$.plan-digest", issues)
    batches = _validate_batches(
        manifest.get("batches"),
        envelope,
        plan_work_groups,
        plan_evidence_expectations,
        executable_work_group_ids,
        issues,
    )
    _validate_budget(
        manifest.get("budget"),
        len(batches),
        batches,
        plan_work_groups,
        issues,
    )
    if issues:
        raise ContractValidationError(issues)


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
) -> dict[str, object]:
    """Freeze a validation-only evidence bundle for one execution batch."""
    validate_ci_validation_execution_batch_manifest(
        execution_batch_manifest,
        plan=plan,
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
        execution_batch_manifest=execution_batch_manifest,
    )
    return bundle


def validate_ci_validation_batch_evidence_bundle(
    bundle: object,
    *,
    plan: Mapping[str, object] | None = None,
    execution_batch_manifest: Mapping[str, object] | None = None,
    expected_run_id: str | None = None,
    expected_run_attempt: str | None = None,
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
    _validate_bundle_writer_matches_batch(bundle.get("writer"), batch, issues)
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
        issues,
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
    _require_authoritative_snapshot_inputs: bool = True,
) -> dict[str, object]:
    """Freeze the pre-final aggregate evidence manifest."""
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
        "projection-authority": _projection_authority_from_plan(plan)
        if plan is not None
        else None,
        "pre-final-validation-artifacts": pre_final_validation_artifacts,
        "namespace-closed-at": namespace_closed_at,
        "proof-admissibility": _PROOF_ADMISSIBILITY,
    }
    validate_ci_validation_aggregate_evidence_manifest(
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
    _require_authoritative_snapshot_inputs: bool = True,
    _require_context_proof_for_valid_inputs: bool = True,
) -> None:
    """Validate an aggregate evidence manifest."""
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
    _validate_plan_nullable_fields(manifest, plan, envelope, issues)
    request_context_digest = _validated_request_context_digest_or_none(
        request,
        envelope,
        issues,
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
    if (
        plan is None
        and execution_batch_manifest is None
        and _aggregate_manifest_has_no_authoritative_plan(manifest)
    ):
        _validate_null_plan_identity(manifest, "$", issues)
    execution_batch_manifest_proven = False
    if execution_batch_manifest is not None:
        execution_manifest_issue_count = len(issues)
        try:
            validate_ci_validation_execution_batch_manifest(
                execution_batch_manifest,
                plan=plan,
                expected_envelope=envelope,
                expected_run_id=envelope.run_id
                if envelope is not None
                else None,
                expected_run_attempt=(
                    envelope.run_attempt if envelope is not None else None
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
        execution_batch_manifest_proven = (
            len(issues) == execution_manifest_issue_count
            and _input_artifact_authorizes_supplied_document(
                manifest,
                "execution-batch-manifest",
                envelope,
                ci_validation_execution_batch_manifest_payload_digest(
                    execution_batch_manifest
                ),
            )
        )
    expected_fact_snapshot_plan_id = _expected_context_plan_id(
        plan,
        execution_batch_manifest,
        changed_files_snapshot=changed_files_snapshot,
        changed_files_snapshot_context_hash=changed_files_snapshot_context_hash,
        changed_files_snapshot_input_proven=(
            changed_files_snapshot_input_proven
        ),
        fact_snapshot=fact_snapshot,
        envelope=envelope,
        execution_batch_manifest_proven=execution_batch_manifest_proven,
        issues=issues,
    )
    fact_snapshot_context_id = _validated_fact_snapshot_id_or_none(
        fact_snapshot,
        envelope,
        issues,
        expected_plan_id=expected_fact_snapshot_plan_id,
    )
    _validate_supplied_plan_document_for_aggregate(
        plan,
        envelope,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        issues=issues,
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
        require_authoritative_snapshot_inputs=(
            _require_authoritative_snapshot_inputs
        ),
        frozen_input_digests=frozen_input_digests,
        require_context_proof_for_valid_inputs=(
            _require_context_proof_for_valid_inputs
        ),
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
    if plan is None and not _aggregate_manifest_has_no_authoritative_plan(
        manifest
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
        execution_batch_manifest,
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


def freeze_ci_validation_aggregate_summary(  # noqa: PLR0913
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
) -> dict[str, object]:
    """Freeze the final aggregate summary bound to an evidence manifest."""
    plan_id = _summary_plan_identity_value(
        "plan-id",
        plan,
        aggregate_evidence_manifest_document,
        execution_batch_manifest,
        admitted_batch_evidence_bundles,
    )
    plan_digest = (
        _verified_plan_digest(plan)
        if plan is not None
        else _summary_plan_identity_value(
            "plan-digest",
            plan,
            aggregate_evidence_manifest_document,
            execution_batch_manifest,
            admitted_batch_evidence_bundles,
        )
    )
    projection = _summary_projection_from_authority(
        plan=plan,
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
        "reason": dict(reason),
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
    if _is_invalid_plan_summary(summary):
        _force_invalid_plan_summary_fields(summary)
    missing_manifest_fail_closed = _summary_has_missing_manifest_failure(
        summary
    )
    if (
        not _is_invalid_plan_summary(summary)
        and not missing_manifest_fail_closed
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
    validate_ci_validation_aggregate_summary(
        summary,
        plan=plan,
        aggregate_evidence_manifest=aggregate_evidence_manifest_document,
        admitted_batch_evidence_bundles=admitted_batch_evidence_bundles,
        execution_batch_manifest=execution_batch_manifest,
        request=request_document,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        _require_aggregate_evidence_manifest=missing_manifest_fail_closed,
    )
    return summary


def validate_ci_validation_aggregate_summary(  # noqa: C901, PLR0913, PLR0915
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
    _validate_plan_nullable_fields(summary, plan, envelope, issues)
    _validate_supplied_plan_document_for_aggregate(
        plan,
        envelope,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        issues=issues,
    )
    _validate_supplied_summary_execution_manifest(
        execution_batch_manifest,
        plan,
        envelope,
        issues,
    )
    if _is_invalid_plan_summary(summary):
        _validate_null_plan_identity(summary, "$", issues)
    elif execution_batch_manifest is not None:
        _validate_plan_identity_matches(
            summary,
            execution_batch_manifest,
            "$",
            "execution-batch manifest",
            issues,
        )
    if summary.get("mode") not in _SUMMARY_MODES:
        issues.append(ValidationIssue("$.mode", "is not registered"))
    require_non_authoritative_manifest = (
        aggregate_evidence_manifest is None
        and (
            _is_invalid_plan_summary(summary)
            or _require_aggregate_evidence_manifest
            or _summary_has_missing_manifest_failure(summary)
        )
    )
    manifest_claim = _validate_summary_manifest_claim(
        summary.get("aggregate-evidence-manifest"),
        envelope,
        aggregate_evidence_manifest,
        require_non_authoritative_manifest=require_non_authoritative_manifest,
        issues=issues,
    )
    _validate_final_artifacts(
        summary.get("final-artifacts"),
        envelope,
        manifest_claim,
        aggregate_evidence_manifest,
        require_non_authoritative_manifest=require_non_authoritative_manifest,
        issues=issues,
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
        execution_batch_manifest,
        plan,
        issues,
    )
    _validate_diagnostics(summary.get("diagnostics"), "$.diagnostics", issues)
    _validate_summary_bundles(summary.get("batch-bundles"), envelope, issues)
    _validate_summary_bundle_ids_match_execution_manifest(
        summary.get("batch-bundles"), execution_batch_manifest, issues
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
        else False
    )
    _validate_summary_derived_status(
        summary,
        summary_evidence_rows,
        inadmissible_batch=_summary_has_inadmissible_batch(summary),
        namespace_failure_details=namespace_failure_details,
        required_input_failure=required_input_failure,
        aggregate_duration_exceeded=_summary_duration_exceeded(summary),
        aggregate_summary_without_manifest=(
            not _is_invalid_plan_summary(summary)
            and aggregate_evidence_manifest is None
        ),
        issues=issues,
    )
    _validate_summary_count_relationships(summary, issues)
    # Missing aggregate evidence manifests are represented as an explicit
    # final-evidence-failure cause in the summary itself.
    if aggregate_evidence_manifest is not None:
        if execution_batch_manifest is None:
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


def _validate_supplied_summary_execution_manifest(
    execution_batch_manifest: Mapping[str, object] | None,
    plan: Mapping[str, object] | None,
    envelope: CommonEnvelope | None,
    issues: list[ValidationIssue],
) -> None:
    if execution_batch_manifest is None:
        return
    try:
        validate_ci_validation_execution_batch_manifest(
            execution_batch_manifest,
            plan=plan,
            expected_envelope=envelope,
            expected_run_id=envelope.run_id if envelope is not None else None,
            expected_run_attempt=(
                envelope.run_attempt if envelope is not None else None
            ),
        )
    except ContractValidationError as error:
        issues.extend(error.issues)


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
    allowed_details = _G1_DETAILS_BY_DIAGNOSTIC_CODE.get(code)
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
        allowed_details = _G1_DETAILS_BY_DIAGNOSTIC_CODE.get(code)
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


def _validate_budget(  # noqa: C901,PLR0912,PLR0915
    value: object,
    batch_count: int,
    batches: Sequence[Mapping[str, object]],
    plan_work_groups: Mapping[str, Mapping[str, object]],
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
        max_total=max_total,
        control_plane=control_plane,
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
    if (
        isinstance(actual_total, int)
        and isinstance(control_plane, int)
        and actual_total != batch_count + control_plane
    ):
        issues.append(
            ValidationIssue(
                "$.budget.actual-total-jobs",
                "must equal batch jobs plus control-plane jobs",
            ),
        )
    lower_bounds_apply = batch_count > 0
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
    if not value:
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


def _max_execution_batch_bound(
    *,
    input_count: object,
    max_total: object,
    control_plane: object,
) -> int | None:
    bounds = [_MAX_EXECUTION_BATCHES]
    if isinstance(input_count, int):
        bounds.append(_MAX_PREFINAL_VALIDATION_ARTIFACTS - input_count)
    if isinstance(max_total, int) and isinstance(control_plane, int):
        bounds.append(max_total - control_plane)
    return min(bounds)


def _derived_windows_jobs(
    batches: Sequence[Mapping[str, object]],
    plan_work_groups: Mapping[str, Mapping[str, object]],
) -> int:
    batch_windows = sum(
        1 for batch in batches if batch.get("runner-family") == "windows"
    )
    control_plane_windows = sum(
        1
        for group in plan_work_groups.values()
        if group.get("kind") == "evidence-aggregation"
        and group.get("runner-family") == "windows"
    )
    return batch_windows + control_plane_windows


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


def _validate_bundle_manifest_fields(
    bundle: Mapping[str, object],
    manifest: Mapping[str, object],
    envelope: CommonEnvelope | None,
    plan: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> Mapping[str, object] | None:
    try:
        validate_ci_validation_execution_batch_manifest(
            manifest,
            plan=plan,
            expected_envelope=envelope,
            expected_run_id=envelope.run_id if envelope is not None else None,
            expected_run_attempt=(
                envelope.run_attempt if envelope is not None else None
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
    if value.get("identity-source") != "github-actions-job-context":
        issues.append(
            ValidationIssue(
                f"{path}.identity-source",
                "must be github-actions-job-context",
            ),
        )
    if value.get("expected-boundary") != "execution-batch":
        issues.append(
            ValidationIssue(
                f"{path}.expected-boundary",
                "must be execution-batch",
            ),
        )
    for key in ("expected-job-identity", "observed-workflow", "observed-job"):
        _validate_non_empty_string(value.get(key), f"{path}.{key}", issues)
    observed_matrix = value.get("observed-matrix")
    if observed_matrix is not None and not isinstance(observed_matrix, Mapping):
        issues.append(
            ValidationIssue(f"{path}.observed-matrix", "must be object or null")
        )


def _validate_bundle_writer_matches_batch(
    writer: object,
    batch: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(writer, Mapping) or batch is None:
        return
    batch_writer = batch.get("batch-writer")
    if not isinstance(batch_writer, Mapping):
        return
    for key in (
        "expected-job-identity",
        "identity-source",
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


def _validate_selector_results(
    value: object,
    batch: Mapping[str, object] | None,
    execution_batch_manifest: Mapping[str, object] | None,
    bundle: Mapping[str, object],
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
        _validate_selector_result(result, path, issues)
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


def _validate_dependency_results(  # noqa: C901,PLR0912
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
        _validate_root_keys(item, _DEPENDENCY_RESULT_KEYS, item_path, issues)
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
        elif isinstance(outcome, str) and admitted != (outcome == "satisfied"):
            issues.append(
                ValidationIssue(
                    f"{item_path}.admitted-for-gating",
                    "must match dependency outcome",
                )
            )
        if outcome != "satisfied":
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


def _validate_evidence(  # noqa: C901,PLR0912
    value: object,
    path: str,
    issues: list[ValidationIssue],
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


def _validate_category_result(
    value: object,
    expected_category: object,
    path: str,
    issues: list[ValidationIssue],
) -> tuple[str | None, list[str]]:
    if not isinstance(value, Mapping):
        issues.append(ValidationIssue(path, "must be an object"))
        return None, []
    _validate_allowed_keys(
        value,
        frozenset({"category", "outcome", "diagnostics", "artifact-refs"}),
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
    refs = _validate_optional_artifact_refs(
        value.get("artifact-refs"),
        f"{path}.artifact-refs",
        issues,
    )
    return derived, refs


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
    plan_envelope = _validated_plan_envelope(plan, issues)
    if envelope is not None and plan_envelope is not None:
        _validate_envelope_matches(envelope, plan_envelope, issues)
    if document.get("plan-id") != plan.get("plan-id"):
        issues.append(ValidationIssue("$.plan-id", "must match plan"))
    if document.get("plan-digest") != _verified_plan_digest_or_none(plan):
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
        return False
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
    execution_batch_manifest: Mapping[str, object] | None,
    admitted_batch_evidence_bundles: Sequence[Mapping[str, object]] | None,
) -> object:
    if plan is not None:
        return plan.get(key)
    if aggregate_evidence_manifest is not None:
        return aggregate_evidence_manifest.get(key)
    if execution_batch_manifest is not None:
        return execution_batch_manifest.get(key)
    if admitted_batch_evidence_bundles:
        return admitted_batch_evidence_bundles[0].get(key)
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


def _validate_aggregate_manifest_projection_authority(  # noqa: C901,PLR0913
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
    if _aggregate_manifest_has_no_authoritative_plan(manifest):
        if authority is not None:
            issues.append(
                ValidationIssue(
                    "$.projection-authority",
                    "must be null without an authoritative plan",
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
    return False


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
    inputs = manifest.get("input-artifacts")
    if not isinstance(inputs, Mapping):
        return False
    validation_plan = inputs.get("validation-plan")
    if not isinstance(validation_plan, Mapping):
        return False
    return validation_plan.get("admissibility") != "valid"


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
    if plan is not None or not _aggregate_manifest_has_valid_plan_input(
        manifest
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
    if request_context_digest is not None:
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
    if changed_files_snapshot_context_hash is not None:
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
    if fact_snapshot_context_id is not None:
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
            )
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
        _validate_root_keys(item, _UNEXPECTED_KEYS, path, issues)
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
                    "must be three-ci-validation- followed by 64 lowercase "
                    "hex chars",
                ),
            )
        _validate_nullable_non_empty_string(
            item.get("artifact-instance-id"),
            f"{path}.artifact-instance-id",
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


def _validate_summary_manifest_claim(
    value: object,
    envelope: CommonEnvelope | None,
    aggregate_evidence_manifest: Mapping[str, object] | None,
    *,
    require_non_authoritative_manifest: bool,
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
        if claim.get("content-digest") != (
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


def _validate_final_artifacts(  # noqa: C901,PLR0912,PLR0913
    value: object,
    envelope: CommonEnvelope | None,
    manifest_claim: Mapping[str, object] | None,
    aggregate_evidence_manifest: Mapping[str, object] | None,
    *,
    require_non_authoritative_manifest: bool,
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
                        "must be false without aggregate evidence manifest",
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
            if manifest.get("producer-verified") is not True:
                issues.append(
                    ValidationIssue(
                        "$.final-artifacts.aggregate-evidence-manifest."
                        "producer-verified",
                        "must be true",
                    ),
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
        if aggregate_evidence_manifest is not None and manifest.get(
            "content-digest"
        ) != ci_validation_aggregate_evidence_manifest_payload_digest(
            aggregate_evidence_manifest
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


def _validate_summary_budgets(  # noqa: C901,PLR0912
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
    actual_batches = budgets.get("actual-execution-batches")
    actual_total = budgets.get("actual-total-jobs")
    actual_windows = budgets.get("actual-windows-jobs")
    if (
        isinstance(actual_batches, int)
        and isinstance(actual_total, int)
        and actual_batches > actual_total
    ):
        issues.append(
            ValidationIssue(
                "$.budgets.actual-total-jobs",
                "must cover execution batches",
            )
        )
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
    if kind == "fail-closed":
        _validate_summary_fail_closed_diagnostic(diagnostic, path, issues)
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
    if isinstance(control_plane, int) and budgets.get("actual-total-jobs") != (
        expected_batches + control_plane
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


def _validate_summary_matches_aggregate_manifest(  # noqa: C901,PLR0912,PLR0913
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
    if summary.get("plan-id") != aggregate_manifest.get("plan-id"):
        issues.append(
            ValidationIssue(
                "$.aggregate-evidence-manifest.plan-id", "must match summary"
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
    try:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=execution_batch_manifest,
            request=_context_for_valid_aggregate_input(
                aggregate_manifest, "request", request
            ),
            changed_files_snapshot=_context_for_valid_aggregate_input(
                aggregate_manifest,
                "changed-files-snapshot",
                changed_files_snapshot,
            ),
            fact_snapshot=_context_for_valid_aggregate_input(
                aggregate_manifest, "fact-snapshot", fact_snapshot
            ),
            frozen_input_digests=_summary_frozen_input_digests_from_plan(plan),
            expected_run_id=envelope.run_id if envelope is not None else None,
            expected_run_attempt=(
                envelope.run_attempt if envelope is not None else None
            ),
            _require_authoritative_snapshot_inputs=False,
        )
    except ContractValidationError as error:
        issues.extend(error.issues)
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
    if set(summary_rows) != set(manifest_rows):
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
    if plan is None or _is_invalid_plan_summary(summary):
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


def _validate_summary_matches_admitted_bundles(  # noqa: C901,PLR0912,PLR0913
    summary: Mapping[str, object],
    aggregate_manifest: Mapping[str, object] | None,
    bundles: Sequence[Mapping[str, object]],
    plan: Mapping[str, object] | None,
    execution_batch_manifest: Mapping[str, object] | None,
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
        try:
            validate_ci_validation_batch_evidence_bundle(
                bundle,
                plan=plan,
                execution_batch_manifest=execution_batch_manifest,
                expected_run_id=envelope.run_id
                if envelope is not None
                else None,
                expected_run_attempt=(
                    envelope.run_attempt if envelope is not None else None
                ),
            )
        except ContractValidationError as error:
            issues.extend(error.issues)
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
    derived_results: dict[str, dict[str, object]] = {}
    if aggregate_manifest is not None:
        manifest_rows = _rows_by_local_id(
            aggregate_manifest.get("batch-bundles"),
            "batch-id",
            "$.aggregate-evidence-manifest.batch-bundles",
            issues,
        )
    else:
        manifest_rows = {}
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
    _validate_admitted_bundle_dependency_results(bundle_by_batch, issues)
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
    issues: list[ValidationIssue],
) -> None:
    selector_lookup: dict[str, tuple[str, str | None]] = {}
    for batch_id, bundle in bundle_by_batch.items():
        selector_results = bundle.get("selector-results")
        if not isinstance(selector_results, Sequence) or isinstance(
            selector_results, str | bytes
        ):
            continue
        for selector in selector_results:
            if not isinstance(selector, Mapping):
                continue
            work_group_id = selector.get("work-group-id")
            if isinstance(work_group_id, str):
                selector_lookup[work_group_id] = (
                    _selector_outcome_to_summary_outcome(
                        selector.get("outcome")
                    ),
                    batch_id,
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


def _validate_selector_dependencies_against_admitted_evidence(  # noqa: C901
    batch_id: str,
    selector_index: int,
    selector: Mapping[str, object],
    selector_lookup: Mapping[str, tuple[str, str | None]],
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
        actual_outcome, actual_batch_id = (
            selector_lookup.get(work_group_id, ("missing", None))
            if isinstance(work_group_id, str)
            else ("missing", None)
        )
        item_path = f"{dep_path}[{dependency_index}]"
        if dependency.get("outcome") != actual_outcome:
            issues.append(
                ValidationIssue(
                    f"{item_path}.outcome",
                    "must match admitted upstream selector result",
                )
            )
        if dependency.get("admitted-for-gating") != (
            actual_outcome == "satisfied"
        ):
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
        if actual_outcome != "satisfied":
            blocked_by_actual_evidence = True
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


def _selector_outcome_to_summary_outcome(outcome: object) -> str:
    if outcome == "success":
        return "satisfied"
    if outcome == "skipped":
        return "skipped"
    return "failed"


def _is_invalid_plan_summary(summary: Mapping[str, object]) -> bool:
    reason = summary.get("reason")
    return isinstance(reason, Mapping) and reason.get("invalid-plan") is True


def _force_invalid_plan_summary_fields(summary: dict[str, object]) -> None:
    summary["plan-id"] = None
    summary["plan-digest"] = None
    summary["mode"] = "unknown"
    summary["validation-tree"] = dict(_UNKNOWN_VALIDATION_TREE)
    summary["affected-range"] = dict(_UNKNOWN_AFFECTED_RANGE)
    summary["request"] = dict(_UNKNOWN_REQUEST_SUMMARY)
    summary["scheduled-full"] = dict(_UNKNOWN_SCHEDULED_FULL)
    summary["verdict"] = "failed"
    reason = summary.get("reason")
    if isinstance(reason, MutableMapping):
        for key in _SUMMARY_REASON_KEYS:
            reason[key] = key == "invalid-plan"
    budgets = summary.get("budgets")
    if isinstance(budgets, MutableMapping):
        budgets["actual-execution-batches"] = 0
        budgets["actual-total-jobs"] = 0
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
    summary["failures"] = [dict(_INVALID_PLAN_FAILURE)]
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


def _validate_summary_derived_status(  # noqa: PLR0913
    summary: Mapping[str, object],
    evidence_rows: Sequence[Mapping[str, object]],
    *,
    inadmissible_batch: bool,
    namespace_failure_details: set[str],
    required_input_failure: bool,
    aggregate_duration_exceeded: bool,
    aggregate_summary_without_manifest: bool,
    issues: list[ValidationIssue],
) -> None:
    namespace_failure = bool(namespace_failure_details)
    outcomes = [row.get("outcome") for row in evidence_rows]
    missing = sum(1 for outcome in outcomes if outcome == "missing")
    skipped = sum(1 for outcome in outcomes if outcome == "skipped")
    failed = sum(1 for outcome in outcomes if outcome == "failed")
    satisfied = sum(1 for outcome in outcomes if outcome == "satisfied")
    final_evidence_failure = (
        aggregate_duration_exceeded
        or required_input_failure
        or aggregate_summary_without_manifest
    )
    expected_reason = {
        "invalid-plan": False,
        "fail-closed": (
            namespace_failure
            or required_input_failure
            or aggregate_summary_without_manifest
        ),
        "required-evidence-missing": missing > 0,
        "required-evidence-skipped": skipped > 0,
        "blocking-validation-failure": failed > 0,
        "inadmissible-batch-evidence": inadmissible_batch,
        "namespace-closure-failure": namespace_failure,
        "aggregate-duration-exceeded": aggregate_duration_exceeded,
        "final-evidence-failure": final_evidence_failure,
    }
    reason = summary.get("reason")
    if isinstance(reason, Mapping) and reason.get("invalid-plan") is True:
        _validate_invalid_plan_summary_mode(summary, evidence_rows, issues)
        return
    required_failure_attributions = _required_summary_failure_attributions(
        summary,
        evidence_rows,
        {
            "namespace-closure-failure": namespace_failure,
            "aggregate-duration-exceeded": aggregate_duration_exceeded,
            "final-evidence-failure": (
                required_input_failure
                or aggregate_duration_exceeded
                or aggregate_summary_without_manifest
            ),
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
            aggregate_duration_exceeded=aggregate_duration_exceeded,
            aggregate_summary_without_manifest=aggregate_summary_without_manifest,
        ),
        issues,
    )
    _validate_fail_closed_failure_details(
        summary,
        _fail_closed_failure_causes(
            namespace_failure_details=namespace_failure_details,
            required_input_failure=required_input_failure,
            aggregate_summary_without_manifest=aggregate_summary_without_manifest,
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
        or required_input_failure
        or aggregate_duration_exceeded
        or aggregate_summary_without_manifest
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
    aggregate_summary_without_manifest: bool,
) -> set[str]:
    causes: set[str] = set()
    if required_input_failure:
        causes.add("required-input-artifact-failure")
    if aggregate_duration_exceeded:
        causes.add("aggregate-duration-exceeded")
    if aggregate_summary_without_manifest:
        causes.add("aggregate-summary-without-manifest")
    return causes


type _FailClosedCause = tuple[str, str]


def _fail_closed_failure_causes(
    *,
    namespace_failure_details: set[str],
    required_input_failure: bool,
    aggregate_summary_without_manifest: bool,
) -> set[_FailClosedCause]:
    causes = {
        ("namespace-closure-failure", detail)
        for detail in namespace_failure_details
    }
    if required_input_failure:
        causes.add(
            ("final-evidence-failure", "required-input-artifact-failure")
        )
    if aggregate_summary_without_manifest:
        causes.add(
            ("final-evidence-failure", "aggregate-summary-without-manifest")
        )
    return causes


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
        if failure.get("kind") != "final-evidence-failure":
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


def _validate_invalid_plan_summary_mode(  # noqa: C901,PLR0912
    summary: Mapping[str, object],
    evidence_rows: Sequence[Mapping[str, object]],
    issues: list[ValidationIssue],
) -> None:
    if summary.get("verdict") != "failed":
        issues.append(ValidationIssue("$.verdict", "must be failed"))
    if summary.get("mode") != "unknown":
        issues.append(ValidationIssue("$.mode", "must be unknown"))
    if summary.get("validation-tree") != _UNKNOWN_VALIDATION_TREE:
        issues.append(
            ValidationIssue(
                "$.validation-tree",
                "must be unknown for invalid-plan summary mode",
            )
        )
    if summary.get("affected-range") != _UNKNOWN_AFFECTED_RANGE:
        issues.append(
            ValidationIssue(
                "$.affected-range",
                "must be unknown for invalid-plan summary mode",
            )
        )
    if summary.get("request") != _UNKNOWN_REQUEST_SUMMARY:
        issues.append(
            ValidationIssue(
                "$.request",
                "must be empty for invalid-plan summary mode",
            )
        )
    if summary.get("scheduled-full") != _UNKNOWN_SCHEDULED_FULL:
        issues.append(
            ValidationIssue(
                "$.scheduled-full",
                "must be disabled for invalid-plan summary mode",
            )
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
    if isinstance(reason, Mapping):
        expected_reason = dict.fromkeys(_SUMMARY_REASON_KEYS, False)
        expected_reason["invalid-plan"] = True
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
        for key in (
            "actual-execution-batches",
            "actual-total-jobs",
            "actual-windows-jobs",
        ):
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
    failures = summary.get("failures")
    if not isinstance(failures, Sequence) or isinstance(failures, str | bytes):
        return
    if len(failures) != 1 or not isinstance(failures[0], Mapping):
        issues.append(
            ValidationIssue(
                "$.failures",
                "must contain exactly one invalid-plan failure",
            )
        )
    elif failures[0].get("kind") != "invalid-plan":
        issues.append(
            ValidationIssue(
                "$.failures[0].kind",
                "must be invalid-plan",
            )
        )
    elif failures[0] != _INVALID_PLAN_FAILURE:
        issues.append(
            ValidationIssue(
                "$.failures[0]",
                "must match canonical invalid-plan failure",
            )
        )


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
        if attribution[0] not in {"fail-closed", "final-evidence-failure"}
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
    instance_id = _sort_component(item.get("artifact-instance-id"))
    classification = _sort_component(item.get("classification"))
    return canonical_json_digest(
        {
            "run-id": run_id,
            "run-attempt": run_attempt,
            "physical-artifact-name": physical_name,
            "artifact-instance-id": instance_id,
            "classification": classification,
        },
    )


def _mapping(value: object) -> Mapping[str, object]:
    if isinstance(value, Mapping):
        return value
    raise ContractValidationError([ValidationIssue("value", "must be object")])


def _sequence(value: object) -> Sequence[object]:
    if isinstance(value, Sequence) and not isinstance(value, str | bytes):
        return value
    raise ContractValidationError([ValidationIssue("value", "must be array")])
