"""Validation receipt contract helpers for CI affected validation."""

from __future__ import annotations

import hashlib
import json
import re
from collections.abc import Callable, Mapping, Sequence
from dataclasses import dataclass
from typing import Literal, cast

from three_workflow_release_contracts.ci_validation import (
    API_VERSIONS_BY_KIND,
    DETAILS_BY_DIAGNOSTIC_CODE,
    REGISTERED_CI_VALIDATION_DIAGNOSTIC_CODES,
    REGISTERED_CI_VALIDATION_DIAGNOSTIC_DETAILS,
    CiValidationKind,
    CommonEnvelope,
    DiagnosticSeverity,
    DiagnosticVerdictEffect,
    canonical_json_bytes,
    validate_artifact_logical_ref,
    validate_common_envelope,
)
from three_workflow_release_contracts.ci_validation_assignments import (
    validate_ci_validation_selector_assignments,
)
from three_workflow_release_contracts.ci_validation_plans import (
    PLANNED_CAPABILITY_ORDER,
    ci_validation_plan_digest,
    validate_ci_validation_plan,
)
from three_workflow_release_contracts.contracts import (
    ContractValidationError,
    ValidationIssue,
)

ReceiptOutcome = Literal["success", "blocking-failure", "skipped"]

_LOCAL_ID_RE = re.compile(r"^[a-z0-9][a-z0-9._-]{0,127}$")
_DIGEST_RE = re.compile(r"^[0-9a-f]{64}$")
_SHA_RE = re.compile(r"^[0-9a-f]{40}$")
_OUTCOMES = frozenset({"success", "blocking-failure", "skipped"})
_CAPABILITIES = frozenset(PLANNED_CAPABILITY_ORDER)
_RECEIPT_DIAGNOSTIC_CODES = frozenset(
    {
        "descriptor-invalid",
        "artifact-shape-unconfirmed",
        "validation-work-failed",
        "validation-work-skipped",
    },
)
_LOCATION_SCOPED_DIAGNOSTIC_CODES = frozenset(
    {"artifact-shape-unconfirmed", "descriptor-invalid"}
)
_RECEIPT_ROOT_KEYS = frozenset(
    {
        "api-version",
        "kind",
        "created-at",
        "repository",
        "run",
        "schema-diagnostics",
        "artifact-ref",
        "receipt-id",
        "plan-id",
        "plan-digest",
        "work-group-id",
        "assignment-id",
        "mode",
        "validation-tree",
        "execution-tree",
        "affected-range",
        "scheduled-full",
        "coverage-target",
        "outcome",
        "evidence",
        "diagnostics",
        "proof-admissibility",
    },
)
_EXECUTION_TREE_KEYS = frozenset(
    {"observed-commit-sha", "source", "verified"},
)
_RECEIPT_AFFECTED_RANGE_KEYS = frozenset(
    {
        "status",
        "base-sha",
        "base-tip-sha",
        "head-sha",
        "changed-files-hash",
    },
)
_EVIDENCE_KEYS = frozenset(
    {
        "category",
        "planned-capabilities",
        "capability-results",
        "category-result",
        "artifact-refs",
    },
)
_CAPABILITY_RESULT_KEYS = frozenset(
    {"capability", "outcome", "diagnostics"},
)
_SUBCHECK_RESULT_KEYS = frozenset(
    {"subcheck-id", "outcome", "diagnostics"},
)
_CATEGORY_RESULT_KEYS = frozenset({"outcome", "diagnostics", "detail"})
_DIAGNOSTIC_KEYS = frozenset(
    {
        "diagnostic-id",
        "code",
        "detail",
        "message",
        "source",
        "severity",
        "verdict-effect",
    },
)
_DIAGNOSTIC_SOURCE_KEYS = frozenset({"type", "id"})
_DETAIL_PROFILE_CATEGORIES = frozenset(
    {"lightweight-preflight", "workflow-release-tooling"},
)
_RELEASE_SHAPED_CATEGORY = "release-shaped-artifact"

_DESCRIPTOR_RESULT_KEYS = frozenset(
    {
        "descriptor-obligation-id",
        "descriptor",
        "descriptor-scope",
        "outcome",
        "diagnostics",
    },
)
_DESCRIPTOR_KEYS = frozenset({"path", "identity", "owner-subject-id", "source"})
_RELEASE_RESULT_KEYS = frozenset(
    {
        "artifact-obligation-id",
        "descriptor",
        "profile-coverage",
        "artifact",
        "release-receipt",
        "outcome",
        "diagnostics",
    },
)
_RELEASE_DESCRIPTOR_KEYS = frozenset({"path", "identity"})
_RELEASE_ARTIFACT_KEYS = frozenset(
    {"planned", "observed", "outcome", "diagnostics"}
)
_RELEASE_ARTIFACT_PLANNED_KEYS = frozenset(
    {
        "kind-family",
        "concrete-kind",
        "logical-artifact-role",
        "variant-dimensions",
        "expected-artifact-refs",
    },
)
_RELEASE_ARTIFACT_OBSERVED_KEYS = frozenset({"refs", "digests"})
_RELEASE_DIGEST_KEYS = frozenset(
    {"artifact-ref", "algorithm", "digest", "digest-available", "diagnostics"},
)
_RELEASE_RECEIPT_KEYS = frozenset(
    {"planned", "expected", "schema-checked", "outcome", "diagnostics"},
)
_RELEASE_RECEIPT_PLANNED_KEYS = frozenset(
    {"expected-family", "logical-receipt-role", "variant-dimensions"},
)


def ci_validation_receipt_content_digest(raw_receipt_bytes: bytes) -> str:
    """Return the SHA-256 digest for observed raw receipt artifact bytes."""
    if not isinstance(raw_receipt_bytes, bytes):
        raise ContractValidationError(
            [ValidationIssue("raw-receipt-bytes", "must be bytes")],
        )
    return hashlib.sha256(raw_receipt_bytes).hexdigest()


def load_ci_validation_receipt_payload(
    raw_receipt_bytes: bytes,
) -> Mapping[str, object]:
    """Parse a receipt JSON artifact and fail closed on non-I-JSON content."""
    if not isinstance(raw_receipt_bytes, bytes):
        raise ContractValidationError(
            [ValidationIssue("raw-receipt-bytes", "must be bytes")],
        )
    try:
        payload = json.loads(
            raw_receipt_bytes.decode("utf-8"),
            object_pairs_hook=_reject_duplicate_object_members,
        )
    except UnicodeDecodeError as error:
        raise ContractValidationError(
            [ValidationIssue("$", "must be UTF-8 JSON")],
        ) from error
    except json.JSONDecodeError as error:
        raise ContractValidationError(
            [ValidationIssue("$", "must be valid JSON")],
        ) from error
    except ValueError as error:
        raise ContractValidationError(
            [ValidationIssue("$", str(error))],
        ) from error
    if not isinstance(payload, Mapping):
        raise ContractValidationError(
            [ValidationIssue("$", "must be an object")]
        )
    try:
        canonical_json_bytes(payload)
    except (TypeError, ValueError) as error:
        raise ContractValidationError(
            [ValidationIssue("$", str(error))],
        ) from error
    return cast("Mapping[str, object]", payload)


def _reject_duplicate_object_members(
    pairs: Sequence[tuple[str, object]],
) -> dict[str, object]:
    members: dict[str, object] = {}
    for key, value in pairs:
        if key in members:
            msg = f"duplicate object member: {key}"
            raise ValueError(msg)
        members[key] = value
    return members


def ci_validation_receipt_payload_digest(receipt: Mapping[str, object]) -> str:
    """Return the canonical digest of a JSON-safe receipt payload."""
    try:
        return hashlib.sha256(canonical_json_bytes(receipt)).hexdigest()
    except (TypeError, ValueError) as error:
        raise ContractValidationError(
            [ValidationIssue("receipt", str(error))],
        ) from error


def freeze_ci_validation_receipt(  # noqa: PLR0913
    *,
    plan: Mapping[str, object],
    selector_assignments_manifest: Mapping[str, object],
    assignment: Mapping[str, object],
    receipt_id: str,
    created_at: str,
    execution_observed_commit_sha: str,
    outcome: ReceiptOutcome,
    evidence: Mapping[str, object],
    diagnostics: Sequence[Mapping[str, object]] = (),
    changed_files_snapshot: Mapping[str, object] | None = None,
    fact_snapshot: Mapping[str, object] | None = None,
    pull_request_merge_commit_verification: Mapping[str, object] | None = None,
) -> dict[str, object]:
    """Freeze a receipt from a validated plan and assignment."""
    context = _validated_receipt_context(
        plan=plan,
        selector_assignments_manifest=selector_assignments_manifest,
        assignment=assignment,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        pull_request_merge_commit_verification=(
            pull_request_merge_commit_verification
        ),
    )
    issues: list[ValidationIssue] = []
    _validate_local_id(receipt_id, "receipt-id", issues)
    if not isinstance(created_at, str) or created_at == "":
        issues.append(ValidationIssue("created-at", "must be a string"))
    _validate_sha(
        execution_observed_commit_sha, "execution-observed-commit-sha", issues
    )
    if outcome not in _OUTCOMES:
        issues.append(ValidationIssue("outcome", "is not registered"))
    expected_source_id = _context_work_group_id(context)
    _validate_diagnostics(
        diagnostics,
        "diagnostics",
        issues,
        expected_source_id=expected_source_id,
    )
    if issues:
        raise ContractValidationError(issues)
    envelope = context.envelope
    receipt = {
        "api-version": API_VERSIONS_BY_KIND[
            CiValidationKind.VALIDATION_RECEIPT.value
        ],
        "kind": CiValidationKind.VALIDATION_RECEIPT.value,
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
        "artifact-ref": assignment["receipt-artifact-ref"],
        "receipt-id": receipt_id,
        "plan-id": plan["plan-id"],
        "plan-digest": plan["plan-digest"],
        "work-group-id": assignment["work-group-id"],
        "assignment-id": assignment["assignment-id"],
        "mode": plan["mode"],
        "validation-tree": dict(
            cast("Mapping[str, object]", plan["validation-tree"])
        ),
        "execution-tree": {
            "observed-commit-sha": execution_observed_commit_sha,
            "source": "trusted-receipt-boundary",
            "verified": True,
        },
        "affected-range": _receipt_affected_range(plan),
        "scheduled-full": dict(
            cast("Mapping[str, object]", plan["scheduled-full"])
        ),
        "coverage-target": dict(context.coverage_target),
        "outcome": outcome,
        "evidence": dict(evidence),
        "diagnostics": [dict(item) for item in diagnostics],
        "proof-admissibility": "validation-only",
    }
    validate_ci_validation_receipt(
        receipt,
        plan=plan,
        selector_assignments_manifest=selector_assignments_manifest,
        assignment=assignment,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        pull_request_merge_commit_verification=(
            pull_request_merge_commit_verification
        ),
    )
    return receipt


def validate_ci_validation_receipt(  # noqa: PLR0913
    receipt: object,
    *,
    plan: Mapping[str, object],
    selector_assignments_manifest: Mapping[str, object],
    assignment: Mapping[str, object],
    changed_files_snapshot: Mapping[str, object] | None = None,
    fact_snapshot: Mapping[str, object] | None = None,
    pull_request_merge_commit_verification: Mapping[str, object] | None = None,
    expected_run_id: str | None = None,
    expected_run_attempt: str | None = None,
) -> None:
    """Validate one receipt against plan and assignment authority."""
    context = _validated_receipt_context(
        plan=plan,
        selector_assignments_manifest=selector_assignments_manifest,
        assignment=assignment,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        pull_request_merge_commit_verification=(
            pull_request_merge_commit_verification
        ),
        expected_run_id=expected_run_id,
        expected_run_attempt=expected_run_attempt,
    )
    if not isinstance(receipt, Mapping):
        raise ContractValidationError(
            [ValidationIssue("$", "must be an object")]
        )
    issues: list[ValidationIssue] = []
    try:
        canonical_json_bytes(receipt)
    except (TypeError, ValueError) as error:
        issues.append(ValidationIssue("$", str(error)))
    envelope = _receipt_envelope_or_collect(receipt, issues)
    _validate_root_keys(receipt, _RECEIPT_ROOT_KEYS, "$", issues)
    if envelope is not None:
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
        _validate_envelope_matches_plan(envelope, context.envelope, issues)
    _validate_payload_bindings(receipt, context, issues)
    _validate_execution_tree(receipt.get("execution-tree"), context, issues)
    expected_source_id = _context_work_group_id(context)
    _validate_diagnostics(
        receipt.get("diagnostics"),
        "$.diagnostics",
        issues,
        expected_source_id=expected_source_id,
    )
    _validate_diagnostic_location_scope(
        receipt.get("diagnostics"),
        "$.diagnostics",
        _diagnostic_scope_for_category(
            context.evidence_expectation.get("category")
        ),
        issues,
    )
    _validate_result_diagnostic_outcome(
        receipt.get("diagnostics"),
        receipt.get("outcome")
        if isinstance(receipt.get("outcome"), str)
        else None,
        "$",
        issues,
    )
    evidence = receipt.get("evidence")
    if isinstance(evidence, Mapping):
        _validate_evidence(receipt, evidence, context, issues)
    else:
        issues.append(ValidationIssue("$.evidence", "must be an object"))
    if issues:
        raise ContractValidationError(issues)


@dataclass(frozen=True, slots=True)
class _ReceiptContext:
    plan: Mapping[str, object]
    envelope: CommonEnvelope
    assignment: Mapping[str, object]
    work_group: Mapping[str, object]
    evidence_expectation: Mapping[str, object]
    coverage_target: Mapping[str, object]
    plan_digest: str
    detail_profile: Mapping[str, object] | None
    fact_snapshot: Mapping[str, object] | None


def _context_work_group_id(context: _ReceiptContext) -> str:
    return cast("str", context.work_group["work-group-id"])


def _validated_receipt_context(  # noqa: PLR0913
    *,
    plan: Mapping[str, object],
    selector_assignments_manifest: Mapping[str, object],
    assignment: Mapping[str, object],
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    pull_request_merge_commit_verification: Mapping[str, object] | None,
    expected_run_id: str | None = None,
    expected_run_attempt: str | None = None,
) -> _ReceiptContext:
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
    validate_ci_validation_selector_assignments(
        selector_assignments_manifest,
        plan=plan,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        pull_request_merge_commit_verification=(
            pull_request_merge_commit_verification
        ),
        expected_run_id=expected_run_id,
        expected_run_attempt=expected_run_attempt,
    )
    issues: list[ValidationIssue] = []
    envelope = _plan_envelope_or_collect(plan, issues)
    plan_digest = _verified_plan_digest_or_collect(plan, issues)
    matched_assignment = _matched_manifest_assignment(
        selector_assignments_manifest,
        assignment,
        issues,
    )
    work_group = _matched_work_group(
        plan, assignment.get("work-group-id"), issues
    )
    evidence = _matched_evidence_expectation(
        plan,
        assignment.get("work-group-id"),
        issues,
    )
    coverage_target = _coverage_target(work_group, evidence, issues)
    detail_profile = _matched_detail_profile(plan, evidence, issues)
    if issues:
        raise ContractValidationError(issues)
    return _ReceiptContext(
        plan=plan,
        envelope=cast("CommonEnvelope", envelope),
        assignment=cast("Mapping[str, object]", matched_assignment),
        work_group=cast("Mapping[str, object]", work_group),
        evidence_expectation=cast("Mapping[str, object]", evidence),
        coverage_target=cast("Mapping[str, object]", coverage_target),
        plan_digest=cast("str", plan_digest),
        detail_profile=detail_profile,
        fact_snapshot=fact_snapshot,
    )


def _validate_payload_bindings(  # noqa: C901,PLR0912
    receipt: Mapping[str, object],
    context: _ReceiptContext,
    issues: list[ValidationIssue],
) -> None:
    if receipt.get("artifact-ref") != context.assignment.get(
        "receipt-artifact-ref"
    ):
        issues.append(
            ValidationIssue("$.artifact-ref", "must match assignment")
        )
    for key in ("assignment-id", "work-group-id"):
        if receipt.get(key) != context.assignment.get(key):
            issues.append(ValidationIssue(f"$.{key}", "must match assignment"))
    _validate_local_id(receipt.get("receipt-id"), "$.receipt-id", issues)
    if not isinstance(receipt.get("plan-id"), str):
        issues.append(ValidationIssue("$.plan-id", "must be a string"))
    if receipt.get("plan-digest") != context.plan_digest:
        issues.append(ValidationIssue("$.plan-digest", "must match plan"))
    plan = context.plan
    if receipt.get("plan-id") != plan.get("plan-id"):
        issues.append(ValidationIssue("$.plan-id", "must match plan"))
    if receipt.get("mode") != plan.get("mode"):
        issues.append(ValidationIssue("$.mode", "must match plan"))
    if receipt.get("validation-tree") != plan.get("validation-tree"):
        issues.append(ValidationIssue("$.validation-tree", "must match plan"))
    if receipt.get("affected-range") != _receipt_affected_range(plan):
        issues.append(ValidationIssue("$.affected-range", "must match plan"))
    if receipt.get("scheduled-full") != plan.get("scheduled-full"):
        issues.append(ValidationIssue("$.scheduled-full", "must match plan"))
    if receipt.get("coverage-target") != context.coverage_target:
        issues.append(ValidationIssue("$.coverage-target", "must match plan"))
    if receipt.get("proof-admissibility") != "validation-only":
        issues.append(
            ValidationIssue(
                "$.proof-admissibility",
                "must be validation-only",
            ),
        )
    artifact_ref = receipt.get("artifact-ref")
    if isinstance(artifact_ref, str):
        _validate_artifact_ref(artifact_ref, "$.artifact-ref", issues)


def _receipt_affected_range(plan: Mapping[str, object]) -> dict[str, object]:
    affected = cast("Mapping[str, object]", plan["affected-range"])
    return {
        "status": affected.get("status"),
        "base-sha": affected.get("base-sha"),
        "base-tip-sha": affected.get("base-tip-sha"),
        "head-sha": affected.get("head-sha"),
        "changed-files-hash": affected.get("changed-files-hash"),
    }


def _validate_execution_tree(
    value: object,
    context: _ReceiptContext,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(value, Mapping):
        issues.append(ValidationIssue("$.execution-tree", "must be an object"))
        return
    _validate_root_keys(value, _EXECUTION_TREE_KEYS, "$.execution-tree", issues)
    plan_tree = context.plan.get("validation-tree")
    planned_sha = None
    if isinstance(plan_tree, Mapping):
        planned_sha = plan_tree.get("commit-sha")
    observed = value.get("observed-commit-sha")
    _validate_sha(observed, "$.execution-tree.observed-commit-sha", issues)
    if observed != planned_sha:
        issues.append(
            ValidationIssue(
                "$.execution-tree.observed-commit-sha",
                "must match validation-tree.commit-sha",
            ),
        )
    if value.get("source") != "trusted-receipt-boundary":
        issues.append(
            ValidationIssue(
                "$.execution-tree.source",
                "must be trusted-receipt-boundary",
            ),
        )
    if value.get("verified") is not True:
        issues.append(
            ValidationIssue("$.execution-tree.verified", "must be true")
        )


def _validate_evidence(
    receipt: Mapping[str, object],
    evidence: Mapping[str, object],
    context: _ReceiptContext,
    issues: list[ValidationIssue],
) -> None:
    _validate_evidence_keys(evidence, "$.evidence", issues)
    expected = context.evidence_expectation
    if evidence.get("category") != expected.get("category"):
        issues.append(ValidationIssue("$.evidence.category", "must match plan"))
    planned = evidence.get("planned-capabilities")
    if planned != expected.get("planned-capabilities"):
        issues.append(
            ValidationIssue(
                "$.evidence.planned-capabilities",
                "must match plan",
            ),
        )
    _validate_artifact_refs(evidence, context, issues)
    top_outcome = receipt.get("outcome")
    if top_outcome not in _OUTCOMES:
        issues.append(ValidationIssue("$.outcome", "is not registered"))
    if planned is None:
        _validate_category_branch(receipt, evidence, context, issues)
    else:
        _validate_capability_branch(receipt, evidence, context, issues)


def _validate_capability_branch(  # noqa: C901,PLR0912
    receipt: Mapping[str, object],
    evidence: Mapping[str, object],
    context: _ReceiptContext,
    issues: list[ValidationIssue],
) -> None:
    planned = evidence.get("planned-capabilities")
    if not isinstance(planned, Sequence) or isinstance(planned, str | bytes):
        issues.append(
            ValidationIssue(
                "$.evidence.planned-capabilities",
                "must be an array",
            ),
        )
        return
    planned_list = list(planned)
    if not planned_list:
        issues.append(
            ValidationIssue(
                "$.evidence.planned-capabilities",
                "must be non-empty",
            ),
        )
    for index, capability in enumerate(planned_list):
        if capability not in _CAPABILITIES:
            issues.append(
                ValidationIssue(
                    f"$.evidence.planned-capabilities[{index}]",
                    "is not registered",
                ),
            )
    if "category-result" in evidence:
        issues.append(
            ValidationIssue("$.evidence.category-result", "must be absent")
        )
    results_value = evidence.get("capability-results")
    if not isinstance(results_value, Sequence) or isinstance(
        results_value,
        str | bytes,
    ):
        issues.append(
            ValidationIssue(
                "$.evidence.capability-results", "must be an array"
            ),
        )
        return
    result_outcomes: list[str] = []
    seen: set[str] = set()
    for index, result in enumerate(results_value):
        path = f"$.evidence.capability-results[{index}]"
        if not isinstance(result, Mapping):
            issues.append(ValidationIssue(path, "must be an object"))
            continue
        _validate_root_keys(result, _CAPABILITY_RESULT_KEYS, path, issues)
        capability = result.get("capability")
        if capability not in planned_list:
            issues.append(
                ValidationIssue(f"{path}.capability", "must be planned"),
            )
        elif isinstance(capability, str):
            if capability in seen:
                issues.append(
                    ValidationIssue(f"{path}.capability", "must be unique"),
                )
            seen.add(capability)
        outcome = _validate_outcome(
            result.get("outcome"), f"{path}.outcome", issues
        )
        if outcome is not None:
            result_outcomes.append(outcome)
        diagnostics = result.get("diagnostics")
        _validate_diagnostics(
            diagnostics,
            f"{path}.diagnostics",
            issues,
            expected_source_id=_context_work_group_id(context),
        )
        _validate_diagnostic_location_scope(
            diagnostics, f"{path}.diagnostics", frozenset(), issues
        )
        _validate_result_diagnostic_outcome(diagnostics, outcome, path, issues)
    if seen != {str(item) for item in planned_list}:
        issues.append(
            ValidationIssue(
                "$.evidence.capability-results",
                "must contain exactly one result per planned capability",
            ),
        )
    expected_outcome = _outcome_with_top_diagnostics(
        _derive_outcome(result_outcomes),
        receipt.get("diagnostics"),
    )
    if (
        expected_outcome is not None
        and receipt.get("outcome") != expected_outcome
    ):
        issues.append(
            ValidationIssue("$.outcome", "must match capability results")
        )


def _validate_category_branch(
    receipt: Mapping[str, object],
    evidence: Mapping[str, object],
    context: _ReceiptContext,
    issues: list[ValidationIssue],
) -> None:
    if "capability-results" in evidence:
        issues.append(
            ValidationIssue("$.evidence.capability-results", "must be absent"),
        )
    category_result = evidence.get("category-result")
    if not isinstance(category_result, Mapping):
        issues.append(
            ValidationIssue("$.evidence.category-result", "must be an object"),
        )
        return
    _validate_root_keys(
        category_result,
        _CATEGORY_RESULT_KEYS,
        "$.evidence.category-result",
        issues,
    )
    outcome = _validate_outcome(
        category_result.get("outcome"),
        "$.evidence.category-result.outcome",
        issues,
    )
    diagnostics = category_result.get("diagnostics")
    _validate_diagnostics(
        diagnostics,
        "$.evidence.category-result.diagnostics",
        issues,
        expected_source_id=_context_work_group_id(context),
    )
    category = context.evidence_expectation.get("category")
    _validate_diagnostic_location_scope(
        diagnostics,
        "$.evidence.category-result.diagnostics",
        _diagnostic_scope_for_category(category),
        issues,
    )
    _validate_result_diagnostic_outcome(
        diagnostics,
        outcome,
        "$.evidence.category-result",
        issues,
    )
    detail = category_result.get("detail")
    detail_outcome = outcome
    if category in _DETAIL_PROFILE_CATEGORIES:
        detail_outcome = _validate_detail_profile_detail(
            detail,
            context,
            issues,
        )
    elif category == "descriptor-validation":
        detail_outcome = _validate_descriptor_detail(detail, context, issues)
    elif category == _RELEASE_SHAPED_CATEGORY:
        detail_outcome = _validate_release_shaped_detail(
            detail, evidence, context, issues
        )
    expected_category_outcome = _outcome_with_top_diagnostics(
        detail_outcome,
        diagnostics,
    )
    if (
        expected_category_outcome is not None
        and outcome != expected_category_outcome
    ):
        issues.append(
            ValidationIssue(
                "$.evidence.category-result.outcome",
                "must match detail results",
            ),
        )
    expected_top_outcome = _outcome_with_top_diagnostics(
        outcome,
        receipt.get("diagnostics"),
    )
    if (
        expected_top_outcome is not None
        and receipt.get("outcome") != expected_top_outcome
    ):
        issues.append(
            ValidationIssue("$.outcome", "must match category result")
        )


def _validate_detail_profile_detail(
    detail: object,
    context: _ReceiptContext,
    issues: list[ValidationIssue],
) -> str | None:
    path = "$.evidence.category-result.detail"
    if not isinstance(detail, Mapping):
        issues.append(ValidationIssue(path, "must be an object"))
        return None
    keys = {
        "work-group-id",
        "detail-profile",
        "coverage-target",
        "selector-variant",
        "runner-family",
        "outcome",
        "subcheck-results",
        "diagnostics",
    }
    if (
        context.evidence_expectation.get("category")
        == "workflow-release-tooling"
    ):
        keys.add("ecosystem")
    _validate_root_keys(detail, frozenset(keys), path, issues)
    _compare(detail, "work-group-id", context.work_group, path, issues)
    _compare(detail, "coverage-target", context.work_group, path, issues)
    _compare(detail, "selector-variant", context.work_group, path, issues)
    _compare(detail, "runner-family", context.work_group, path, issues)
    if "ecosystem" in keys:
        _compare(detail, "ecosystem", context.work_group, path, issues)
    profile_id = context.evidence_expectation.get("detail-profile")
    if detail.get("detail-profile") != profile_id:
        issues.append(
            ValidationIssue(f"{path}.detail-profile", "must match plan")
        )
    outcome = _validate_outcome(
        detail.get("outcome"), f"{path}.outcome", issues
    )
    diagnostics = detail.get("diagnostics")
    _validate_diagnostics(
        diagnostics,
        f"{path}.diagnostics",
        issues,
        expected_source_id=_context_work_group_id(context),
    )
    _validate_diagnostic_location_scope(
        diagnostics, f"{path}.diagnostics", frozenset(), issues
    )
    _validate_result_diagnostic_outcome(
        diagnostics,
        outcome,
        path,
        issues,
    )
    blocking_subcheck_outcomes = _validate_subcheck_results(
        detail.get("subcheck-results"),
        context,
        issues,
    )
    derived = _outcome_with_top_diagnostics(
        _derive_outcome(blocking_subcheck_outcomes),
        diagnostics,
    )
    if derived is not None and outcome != derived:
        issues.append(
            ValidationIssue(f"{path}.outcome", "must match subchecks")
        )
    return outcome


def _validate_subcheck_results(  # noqa: C901,PLR0912
    value: object,
    context: _ReceiptContext,
    issues: list[ValidationIssue],
) -> list[str]:
    path = "$.evidence.category-result.detail.subcheck-results"
    if context.detail_profile is None:
        issues.append(ValidationIssue(path, "must reference a detail profile"))
        return []
    required = context.detail_profile.get("required-subchecks")
    required_ids = set()
    blocking_ids = set()
    if isinstance(required, Sequence) and not isinstance(required, str | bytes):
        for item in required:
            if isinstance(item, Mapping) and isinstance(
                item.get("subcheck-id"), str
            ):
                item_id = str(item["subcheck-id"])
                required_ids.add(item_id)
                if item.get("blocking") is True:
                    blocking_ids.add(item_id)
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        issues.append(ValidationIssue(path, "must be an array"))
        return []
    seen: set[str] = set()
    outcomes: list[str] = []
    for index, result in enumerate(value):
        item_path = f"{path}[{index}]"
        if not isinstance(result, Mapping):
            issues.append(ValidationIssue(item_path, "must be an object"))
            continue
        _validate_root_keys(result, _SUBCHECK_RESULT_KEYS, item_path, issues)
        subcheck_id = result.get("subcheck-id")
        if subcheck_id not in required_ids:
            issues.append(
                ValidationIssue(f"{item_path}.subcheck-id", "must be planned")
            )
        elif isinstance(subcheck_id, str):
            if subcheck_id in seen:
                issues.append(
                    ValidationIssue(
                        f"{item_path}.subcheck-id", "must be unique"
                    )
                )
            seen.add(subcheck_id)
        outcome = _validate_outcome(
            result.get("outcome"), f"{item_path}.outcome", issues
        )
        if outcome is not None and subcheck_id in blocking_ids:
            outcomes.append(outcome)
        diagnostics = result.get("diagnostics")
        _validate_diagnostics(
            diagnostics,
            f"{item_path}.diagnostics",
            issues,
            expected_source_id=_context_work_group_id(context),
        )
        _validate_diagnostic_location_scope(
            diagnostics, f"{item_path}.diagnostics", frozenset(), issues
        )
        _validate_result_diagnostic_outcome(
            diagnostics,
            outcome,
            item_path,
            issues,
        )
        if outcome in {"blocking-failure", "skipped"} and not _has_diagnostics(
            diagnostics
        ):
            issues.append(
                ValidationIssue(
                    f"{item_path}.diagnostics",
                    "must explain skipped or failed subcheck",
                ),
            )
    if seen != required_ids:
        issues.append(
            ValidationIssue(path, "must contain exactly planned subchecks")
        )
    return outcomes


def _validate_descriptor_detail(
    detail: object,
    context: _ReceiptContext,
    issues: list[ValidationIssue],
) -> str | None:
    path = "$.evidence.category-result.detail"
    if not isinstance(detail, Mapping):
        issues.append(ValidationIssue(path, "must be an object"))
        return None
    _validate_root_keys(
        detail, frozenset({"descriptor-obligation-results"}), path, issues
    )
    obligations = _records_for_work_group(
        context.plan.get("descriptor-obligations"),
        "work-group-id",
        context.work_group.get("work-group-id"),
    )
    result_outcomes = _validate_bound_descriptor_results(
        detail.get("descriptor-obligation-results"),
        obligations,
        context,
        f"{path}.descriptor-obligation-results",
        issues,
    )
    return _derive_outcome(result_outcomes)


def _validate_release_shaped_detail(
    detail: object,
    evidence: Mapping[str, object],
    context: _ReceiptContext,
    issues: list[ValidationIssue],
) -> str | None:
    path = "$.evidence.category-result.detail"
    if not isinstance(detail, Mapping):
        issues.append(ValidationIssue(path, "must be an object"))
        return None
    _validate_root_keys(
        detail, frozenset({"artifact-obligation-results"}), path, issues
    )
    obligations = _records_for_work_group(
        context.plan.get("artifact-obligations"),
        "work-group-id",
        context.work_group.get("work-group-id"),
    )
    result_outcomes = _validate_bound_release_results(
        detail.get("artifact-obligation-results"),
        obligations,
        context,
        f"{path}.artifact-obligation-results",
        issues,
    )
    results = detail.get("artifact-obligation-results")
    if (
        isinstance(results, Sequence)
        and not isinstance(results, str | bytes)
        and results
    ):
        first = results[0]
        if isinstance(first, Mapping):
            observed_refs = _release_observed_refs(first)
            if (
                observed_refs is not None
                and evidence.get("artifact-refs") != observed_refs
            ):
                issues.append(
                    ValidationIssue(
                        "$.evidence.artifact-refs",
                        "must match observed artifact refs",
                    ),
                )
    return _derive_outcome(result_outcomes)


def _validate_bound_descriptor_results(
    value: object,
    obligations: Sequence[Mapping[str, object]],
    context: _ReceiptContext,
    path: str,
    issues: list[ValidationIssue],
) -> list[str]:
    return _validate_bound_results(
        value,
        obligations,
        "descriptor-obligation-id",
        path,
        issues,
        lambda result, obligation, item_path: _validate_descriptor_result(
            result,
            obligation,
            context,
            item_path,
            issues,
        ),
    )


def _validate_bound_release_results(
    value: object,
    obligations: Sequence[Mapping[str, object]],
    context: _ReceiptContext,
    path: str,
    issues: list[ValidationIssue],
) -> list[str]:
    return _validate_bound_results(
        value,
        obligations,
        "artifact-obligation-id",
        path,
        issues,
        lambda result, obligation, item_path: _validate_release_result(
            result,
            obligation,
            context,
            item_path,
            issues,
        ),
    )


def _validate_bound_results(  # noqa: PLR0913
    value: object,
    obligations: Sequence[Mapping[str, object]],
    key: str,
    path: str,
    issues: list[ValidationIssue],
    validate_result: Callable[
        [Mapping[str, object], Mapping[str, object], str], str | None
    ],
) -> list[str]:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        issues.append(ValidationIssue(path, "must be an array"))
        return []
    obligations_by_id = {
        str(item[key]): item
        for item in obligations
        if isinstance(item.get(key), str)
    }
    seen: set[str] = set()
    outcomes: list[str] = []
    for index, result in enumerate(value):
        item_path = f"{path}[{index}]"
        if not isinstance(result, Mapping):
            issues.append(ValidationIssue(item_path, "must be an object"))
            continue
        result_id = result.get(key)
        obligation = obligations_by_id.get(str(result_id))
        if obligation is None:
            issues.append(
                ValidationIssue(f"{item_path}.{key}", "must be planned")
            )
        elif isinstance(result_id, str):
            if result_id in seen:
                issues.append(
                    ValidationIssue(f"{item_path}.{key}", "must be unique")
                )
            seen.add(result_id)
        if obligation is not None:
            outcome = validate_result(result, obligation, item_path)
            if outcome is not None:
                outcomes.append(outcome)
    if seen != set(obligations_by_id):
        issues.append(
            ValidationIssue(path, "must contain exactly planned obligations"),
        )
    return outcomes


def _validate_descriptor_result(
    result: Mapping[str, object],
    obligation: Mapping[str, object],
    context: _ReceiptContext,
    path: str,
    issues: list[ValidationIssue],
) -> str | None:
    _validate_root_keys(result, _DESCRIPTOR_RESULT_KEYS, path, issues)
    descriptor = result.get("descriptor")
    descriptor_path = _descriptor_obligation_path(obligation)
    descriptor_fact = _descriptor_fact(context.fact_snapshot, descriptor_path)
    if isinstance(descriptor, Mapping):
        _validate_root_keys(
            descriptor, _DESCRIPTOR_KEYS, f"{path}.descriptor", issues
        )
        _require_string_value(
            descriptor.get("path"), f"{path}.descriptor.path", issues
        )
        _validate_optional_string(
            descriptor.get("identity"), f"{path}.descriptor.identity", issues
        )
        _validate_optional_string(
            descriptor.get("owner-subject-id"),
            f"{path}.descriptor.owner-subject-id",
            issues,
        )
        if descriptor.get("source") not in {
            "ecosystem-provider",
            "workflow-release-provider",
        }:
            issues.append(
                ValidationIssue(
                    f"{path}.descriptor.source", "is not registered"
                ),
            )
        _validate_descriptor_matches_fact(
            descriptor,
            descriptor_fact,
            f"{path}.descriptor",
            issues,
        )
    else:
        issues.append(
            ValidationIssue(f"{path}.descriptor", "must be an object")
        )
    if result.get("descriptor-scope") not in {
        "selected",
        "ecosystem",
        "all-discovered",
    }:
        issues.append(
            ValidationIssue(f"{path}.descriptor-scope", "is not registered"),
        )
    if result.get("descriptor-scope") != obligation.get("descriptor-scope"):
        issues.append(
            ValidationIssue(f"{path}.descriptor-scope", "must match plan"),
        )
    outcome = _validate_outcome(
        result.get("outcome"), f"{path}.outcome", issues
    )
    diagnostics = result.get("diagnostics")
    _validate_diagnostics(
        diagnostics,
        f"{path}.diagnostics",
        issues,
        expected_source_id=_context_work_group_id(context),
    )
    _validate_diagnostic_location_scope(
        diagnostics,
        f"{path}.diagnostics",
        frozenset({"descriptor-invalid"}),
        issues,
    )
    _validate_result_diagnostic_outcome(diagnostics, outcome, path, issues)
    return outcome


def _validate_release_result(
    result: Mapping[str, object],
    obligation: Mapping[str, object],
    context: _ReceiptContext,
    path: str,
    issues: list[ValidationIssue],
) -> str | None:
    _validate_root_keys(result, _RELEASE_RESULT_KEYS, path, issues)
    descriptor = _mapping_or_issue(
        result.get("descriptor"), f"{path}.descriptor", issues
    )
    descriptor_path = str(obligation.get("descriptor-path"))
    descriptor_fact = _descriptor_fact(context.fact_snapshot, descriptor_path)
    if descriptor is not None:
        _validate_root_keys(
            descriptor, _RELEASE_DESCRIPTOR_KEYS, f"{path}.descriptor", issues
        )
        _require_string_value(
            descriptor.get("path"), f"{path}.descriptor.path", issues
        )
        _validate_optional_string(
            descriptor.get("identity"), f"{path}.descriptor.identity", issues
        )
        if descriptor.get("path") != obligation.get("descriptor-path"):
            issues.append(
                ValidationIssue(f"{path}.descriptor.path", "must match plan")
            )
        if descriptor_fact is not None and descriptor.get(
            "identity"
        ) != descriptor_fact.get("descriptor-identity"):
            issues.append(
                ValidationIssue(
                    f"{path}.descriptor.identity", "must match plan"
                )
            )
    profile_coverage = _validate_string_array(
        result.get("profile-coverage"), f"{path}.profile-coverage", issues
    )
    if profile_coverage != obligation.get("profile-coverage"):
        issues.append(
            ValidationIssue(f"{path}.profile-coverage", "must match plan")
        )
    artifact = _mapping_or_issue(
        result.get("artifact"), f"{path}.artifact", issues
    )
    if artifact is not None:
        _validate_release_artifact(
            artifact,
            obligation,
            _context_work_group_id(context),
            f"{path}.artifact",
            issues,
        )
    receipt = _mapping_or_issue(
        result.get("release-receipt"), f"{path}.release-receipt", issues
    )
    if receipt is not None:
        _validate_release_receipt(
            receipt,
            obligation,
            _context_work_group_id(context),
            f"{path}.release-receipt",
            issues,
        )
    outcome = _validate_outcome(
        result.get("outcome"), f"{path}.outcome", issues
    )
    diagnostics = result.get("diagnostics")
    _validate_diagnostics(
        diagnostics,
        f"{path}.diagnostics",
        issues,
        expected_source_id=_context_work_group_id(context),
    )
    _validate_diagnostic_location_scope(
        diagnostics, f"{path}.diagnostics", frozenset(), issues
    )
    _validate_result_diagnostic_outcome(diagnostics, outcome, path, issues)
    _validate_release_result_outcome(result, outcome, context, path, issues)
    return outcome


def _validate_release_artifact(
    artifact: Mapping[str, object],
    obligation: Mapping[str, object],
    expected_source_id: str,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    _validate_root_keys(artifact, _RELEASE_ARTIFACT_KEYS, path, issues)
    planned = _mapping_or_issue(
        artifact.get("planned"), f"{path}.planned", issues
    )
    if planned is not None:
        _validate_root_keys(
            planned, _RELEASE_ARTIFACT_PLANNED_KEYS, f"{path}.planned", issues
        )
        for key in ("kind-family", "concrete-kind", "logical-artifact-role"):
            _require_string_value(
                planned.get(key), f"{path}.planned.{key}", issues
            )
        if not isinstance(planned.get("variant-dimensions"), Mapping):
            issues.append(
                ValidationIssue(
                    f"{path}.planned.variant-dimensions", "must be an object"
                ),
            )
        planned_refs = _validate_artifact_ref_array(
            planned.get("expected-artifact-refs"),
            f"{path}.planned.expected-artifact-refs",
            issues,
        )
        if dict(planned) != obligation.get("artifact"):
            issues.append(ValidationIssue(f"{path}.planned", "must match plan"))
        if planned_refs != _expected_artifact_refs(obligation):
            issues.append(
                ValidationIssue(
                    f"{path}.planned.expected-artifact-refs",
                    "must match plan",
                ),
            )
    observed = _mapping_or_issue(
        artifact.get("observed"), f"{path}.observed", issues
    )
    if observed is not None:
        _validate_root_keys(
            observed,
            _RELEASE_ARTIFACT_OBSERVED_KEYS,
            f"{path}.observed",
            issues,
        )
        refs = _validate_artifact_ref_array(
            observed.get("refs"), f"{path}.observed.refs", issues
        )
        observed_refs_match_plan = refs == _expected_artifact_refs(obligation)
        outcome = _validate_outcome(
            artifact.get("outcome"), f"{path}.outcome", issues
        )
        has_failed_digest_diagnostic = _validate_release_digests(
            observed.get("digests"),
            refs,
            outcome,
            expected_source_id,
            f"{path}.observed.digests",
            issues,
        )
    else:
        observed_refs_match_plan = True
        has_failed_digest_diagnostic = False
        outcome = _validate_outcome(
            artifact.get("outcome"), f"{path}.outcome", issues
        )
    diagnostics = artifact.get("diagnostics")
    _validate_diagnostics(
        diagnostics,
        f"{path}.diagnostics",
        issues,
        expected_source_id=expected_source_id,
    )
    _validate_diagnostic_location_scope(
        diagnostics,
        f"{path}.diagnostics",
        frozenset({"artifact-shape-unconfirmed"}),
        issues,
    )
    _validate_result_diagnostic_outcome(
        diagnostics,
        outcome,
        path,
        issues,
    )
    if has_failed_digest_diagnostic and outcome != "blocking-failure":
        issues.append(
            ValidationIssue(
                f"{path}.outcome",
                "must be blocking-failure for failed digest diagnostics",
            )
        )
    if not observed_refs_match_plan and outcome not in {
        "blocking-failure",
        "skipped",
    }:
        issues.append(
            ValidationIssue(
                f"{path}.observed.refs",
                "must be blocking-failure or skipped when mismatched",
            )
        )
    if outcome == "success" and not _release_artifact_digests_available(
        artifact
    ):
        issues.append(
            ValidationIssue(
                f"{path}.observed.digests",
                "must be available for success",
            )
        )


def _validate_release_receipt(
    receipt: Mapping[str, object],
    obligation: Mapping[str, object],
    expected_source_id: str,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    _validate_root_keys(receipt, _RELEASE_RECEIPT_KEYS, path, issues)
    planned = _mapping_or_issue(
        receipt.get("planned"), f"{path}.planned", issues
    )
    if planned is not None:
        _validate_root_keys(
            planned, _RELEASE_RECEIPT_PLANNED_KEYS, f"{path}.planned", issues
        )
        for key in ("expected-family", "logical-receipt-role"):
            _require_string_value(
                planned.get(key), f"{path}.planned.{key}", issues
            )
        if not isinstance(planned.get("variant-dimensions"), Mapping):
            issues.append(
                ValidationIssue(
                    f"{path}.planned.variant-dimensions", "must be an object"
                ),
            )
        if dict(planned) != obligation.get("release-receipt"):
            issues.append(ValidationIssue(f"{path}.planned", "must match plan"))
    if receipt.get("expected") is not True:
        issues.append(ValidationIssue(f"{path}.expected", "must be true"))
    if not isinstance(receipt.get("schema-checked"), bool):
        issues.append(
            ValidationIssue(f"{path}.schema-checked", "must be a boolean")
        )
    outcome = _validate_outcome(
        receipt.get("outcome"), f"{path}.outcome", issues
    )
    diagnostics = receipt.get("diagnostics")
    _validate_diagnostics(
        diagnostics,
        f"{path}.diagnostics",
        issues,
        expected_source_id=expected_source_id,
    )
    _validate_diagnostic_location_scope(
        diagnostics, f"{path}.diagnostics", frozenset(), issues
    )
    _validate_result_diagnostic_outcome(
        diagnostics,
        outcome,
        path,
        issues,
    )
    if outcome == "success" and receipt.get("schema-checked") is not True:
        issues.append(
            ValidationIssue(
                f"{path}.schema-checked",
                "must be true for success",
            )
        )


def _validate_release_result_outcome(
    result: Mapping[str, object],
    outcome: str | None,
    context: _ReceiptContext,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    artifact = result.get("artifact")
    release_receipt = result.get("release-receipt")
    if not isinstance(artifact, Mapping) or not isinstance(
        release_receipt, Mapping
    ):
        return
    artifact_outcome = artifact.get("outcome")
    release_receipt_outcome = release_receipt.get("outcome")
    derived = _outcome_with_top_diagnostics(
        _derive_outcome(
            [
                cast("str", item)
                for item in (artifact_outcome, release_receipt_outcome)
                if item in _OUTCOMES
            ]
        ),
        result.get("diagnostics"),
    )
    dependency_blocked_skip = _is_dependency_blocked_release_skip(
        result, context
    )
    if outcome is not None and derived is not None and outcome != derived:
        issues.append(
            ValidationIssue(f"{path}.outcome", "must match nested results")
        )
    _validate_nested_release_skips(
        (artifact_outcome, release_receipt_outcome),
        outcome,
        path,
        issues,
        dependency_blocked_skip=dependency_blocked_skip,
    )
    if outcome == "success":
        if artifact_outcome != "success":
            issues.append(
                ValidationIssue(
                    f"{path}.artifact.outcome", "must be success for success"
                )
            )
        if release_receipt_outcome != "success":
            issues.append(
                ValidationIssue(
                    f"{path}.release-receipt.outcome",
                    "must be success for success",
                )
            )
        if release_receipt.get("expected") is not True:
            issues.append(
                ValidationIssue(
                    f"{path}.release-receipt.expected",
                    "must be true for success",
                )
            )
        if release_receipt.get("schema-checked") is not True:
            issues.append(
                ValidationIssue(
                    f"{path}.release-receipt.schema-checked",
                    "must be true for success",
                )
            )
        if not _release_artifact_digests_available(artifact):
            issues.append(
                ValidationIssue(
                    f"{path}.artifact.observed.digests",
                    "must be available for success",
                )
            )
    elif outcome == "skipped" and not dependency_blocked_skip:
        issues.append(
            ValidationIssue(
                f"{path}.diagnostics",
                "must be backed by frozen depends-on and explain "
                "dependency-blocked release skip",
            )
        )


def _validate_nested_release_skips(
    nested_outcomes: Sequence[object],
    outcome: str | None,
    path: str,
    issues: list[ValidationIssue],
    *,
    dependency_blocked_skip: bool,
) -> None:
    has_nested_skip = "skipped" in nested_outcomes
    if has_nested_skip and not (
        outcome == "skipped" and dependency_blocked_skip
    ):
        issues.append(
            ValidationIssue(
                f"{path}.outcome",
                "nested skipped release results require "
                "dependency-blocked skip",
            )
        )


def _is_dependency_blocked_release_skip(
    result: Mapping[str, object],
    context: _ReceiptContext,
) -> bool:
    artifact = result.get("artifact")
    release_receipt = result.get("release-receipt")
    if not isinstance(artifact, Mapping) or not isinstance(
        release_receipt, Mapping
    ):
        return False
    observed = artifact.get("observed")
    return (
        result.get("outcome") == "skipped"
        and artifact.get("outcome") == "skipped"
        and release_receipt.get("outcome") == "skipped"
        and release_receipt.get("expected") is True
        and release_receipt.get("schema-checked") is False
        and _has_frozen_dependencies(context.work_group)
        and isinstance(observed, Mapping)
        and observed.get("refs") == []
        and observed.get("digests") == []
        and _has_dependency_blocked_diagnostic(
            [
                result.get("diagnostics"),
                artifact.get("diagnostics"),
                release_receipt.get("diagnostics"),
            ]
        )
    )


def _has_frozen_dependencies(work_group: Mapping[str, object]) -> bool:
    depends_on = work_group.get("depends-on")
    if not isinstance(depends_on, Sequence) or isinstance(
        depends_on, str | bytes
    ):
        return False
    return any(isinstance(item, str) and item for item in depends_on)


def _has_dependency_blocked_diagnostic(
    diagnostic_lists: Sequence[object],
) -> bool:
    for diagnostics in diagnostic_lists:
        if not isinstance(diagnostics, Sequence) or isinstance(
            diagnostics, str | bytes
        ):
            continue
        for diagnostic in diagnostics:
            if (
                isinstance(diagnostic, Mapping)
                and diagnostic.get("code") == "validation-work-skipped"
                and diagnostic.get("detail") == "dependency-blocked"
            ):
                return True
    return False


def _validate_release_digests(  # noqa: C901,PLR0912,PLR0913
    value: object,
    refs: Sequence[str],
    outcome: str | None,
    expected_source_id: str,
    path: str,
    issues: list[ValidationIssue],
) -> bool:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        issues.append(ValidationIssue(path, "must be an array"))
        return False
    seen: set[str] = set()
    has_failed_diagnostic = False
    for index, item in enumerate(value):
        item_path = f"{path}[{index}]"
        if not isinstance(item, Mapping):
            issues.append(ValidationIssue(item_path, "must be an object"))
            continue
        _validate_root_keys(item, _RELEASE_DIGEST_KEYS, item_path, issues)
        artifact_ref = item.get("artifact-ref")
        _validate_artifact_ref(
            artifact_ref, f"{item_path}.artifact-ref", issues
        )
        if isinstance(artifact_ref, str):
            if artifact_ref in seen:
                issues.append(
                    ValidationIssue(
                        f"{item_path}.artifact-ref", "must be unique"
                    )
                )
            seen.add(artifact_ref)
            if artifact_ref not in refs:
                issues.append(
                    ValidationIssue(
                        f"{item_path}.artifact-ref", "must be observed"
                    )
                )
        if item.get("algorithm") != "sha256":
            issues.append(
                ValidationIssue(f"{item_path}.algorithm", "must be sha256")
            )
        available = item.get("digest-available")
        if not isinstance(available, bool):
            issues.append(
                ValidationIssue(
                    f"{item_path}.digest-available", "must be a boolean"
                )
            )
        digest = item.get("digest")
        diagnostics = item.get("diagnostics")
        if available is True:
            if (
                not isinstance(digest, str)
                or _DIGEST_RE.fullmatch(digest) is None
            ):
                issues.append(
                    ValidationIssue(
                        f"{item_path}.digest", "must be a SHA-256 digest"
                    )
                )
        elif digest != "":
            issues.append(
                ValidationIssue(
                    f"{item_path}.digest", "must be empty when unavailable"
                )
            )
        if available is False and not _has_diagnostic_code(
            diagnostics, "artifact-shape-unconfirmed"
        ):
            issues.append(
                ValidationIssue(
                    f"{item_path}.diagnostics",
                    "must include artifact-shape-unconfirmed when unavailable",
                )
            )
        _validate_diagnostics(
            diagnostics,
            f"{item_path}.diagnostics",
            issues,
            expected_source_id=expected_source_id,
        )
        _validate_diagnostic_location_scope(
            diagnostics,
            f"{item_path}.diagnostics",
            frozenset({"artifact-shape-unconfirmed"}),
            issues,
        )
        if isinstance(diagnostics, Sequence) and not isinstance(
            diagnostics, str | bytes
        ):
            for diagnostic in diagnostics:
                if isinstance(diagnostic, Mapping):
                    _validate_diagnostic_code_outcome(
                        diagnostic, outcome, item_path, issues
                    )
        has_failed_diagnostic = has_failed_diagnostic or _has_failed_diagnostic(
            diagnostics
        )
    if set(refs) != seen:
        issues.append(
            ValidationIssue(path, "must contain one digest per observed ref")
        )
    return has_failed_diagnostic


def _release_artifact_digests_available(
    artifact: Mapping[str, object],
) -> bool:
    observed = artifact.get("observed")
    if not isinstance(observed, Mapping):
        return True
    digests = observed.get("digests")
    if not isinstance(digests, Sequence) or isinstance(digests, str | bytes):
        return True
    return all(
        isinstance(item, Mapping) and item.get("digest-available") is True
        for item in digests
    )


def _descriptor_obligation_path(obligation: Mapping[str, object]) -> str:
    target = obligation.get("coverage-target")
    if isinstance(target, Mapping) and isinstance(target.get("id"), str):
        return str(target["id"])
    return str(obligation.get("descriptor-path"))


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


def _validate_descriptor_matches_fact(
    descriptor: Mapping[str, object],
    fact: Mapping[str, object] | None,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if fact is None:
        issues.append(ValidationIssue(path, "must resolve to descriptor fact"))
        return
    expected = {
        "path": fact.get("descriptor-path"),
        "identity": fact.get("descriptor-identity"),
        "owner-subject-id": fact.get("owner-subject-id"),
        "source": fact.get("source"),
    }
    for key, value in expected.items():
        if descriptor.get(key) != value:
            issues.append(ValidationIssue(f"{path}.{key}", "must match plan"))


def _expected_artifact_refs(obligation: Mapping[str, object]) -> list[str]:
    artifact = obligation.get("artifact")
    if not isinstance(artifact, Mapping):
        return []
    refs = artifact.get("expected-artifact-refs")
    if not isinstance(refs, Sequence) or isinstance(refs, str | bytes):
        return []
    return [str(ref) for ref in refs if isinstance(ref, str)]


def _mapping_or_issue(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> Mapping[str, object] | None:
    if not isinstance(value, Mapping):
        issues.append(ValidationIssue(path, "must be an object"))
        return None
    return value


def _require_string_value(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(value, str) or value == "":
        issues.append(ValidationIssue(path, "must be a string"))


def _validate_optional_string(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if value is not None and (not isinstance(value, str) or value == ""):
        issues.append(ValidationIssue(path, "must be null or non-empty"))


def _validate_string_array(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> list[str]:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        issues.append(ValidationIssue(path, "must be an array"))
        return []
    result: list[str] = []
    previous: str | None = None
    for index, item in enumerate(value):
        if not isinstance(item, str) or item == "":
            issues.append(
                ValidationIssue(f"{path}[{index}]", "must be a string")
            )
            continue
        if previous is not None and previous >= item:
            issues.append(ValidationIssue(path, "must be sorted and unique"))
        previous = item
        result.append(item)
    return result


def _validate_artifact_ref_array(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> list[str]:
    refs = _validate_string_array(value, path, issues)
    for index, ref in enumerate(refs):
        _validate_artifact_ref(ref, f"{path}[{index}]", issues)
    return refs


def _release_observed_refs(result: Mapping[str, object]) -> list[object] | None:
    artifact = result.get("artifact")
    if not isinstance(artifact, Mapping):
        return None
    observed = artifact.get("observed")
    if not isinstance(observed, Mapping):
        return None
    refs = observed.get("refs")
    if not isinstance(refs, list):
        return None
    return refs


def _validate_artifact_refs(
    evidence: Mapping[str, object],
    context: _ReceiptContext,
    issues: list[ValidationIssue],
) -> None:
    refs = evidence.get("artifact-refs")
    if not isinstance(refs, Sequence) or isinstance(refs, str | bytes):
        issues.append(
            ValidationIssue("$.evidence.artifact-refs", "must be an array")
        )
        return
    for index, ref in enumerate(refs):
        _validate_artifact_ref(
            ref, f"$.evidence.artifact-refs[{index}]", issues
        )
    if context.evidence_expectation.get(
        "category"
    ) != _RELEASE_SHAPED_CATEGORY and list(refs):
        issues.append(
            ValidationIssue(
                "$.evidence.artifact-refs",
                "must be empty for non-artifact receipts",
            ),
        )


def _validate_diagnostics(
    value: object,
    path: str,
    issues: list[ValidationIssue],
    *,
    expected_source_id: str | None = None,
) -> None:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        issues.append(ValidationIssue(path, "must be an array"))
        return
    for index, diagnostic in enumerate(value):
        item_path = f"{path}[{index}]"
        if not isinstance(diagnostic, Mapping):
            issues.append(ValidationIssue(item_path, "must be an object"))
            continue
        _validate_diagnostic(
            diagnostic,
            item_path,
            issues,
            expected_source_id=expected_source_id,
        )


def _diagnostic_scope_for_category(category: object) -> frozenset[str]:
    if category == "descriptor-validation":
        return frozenset({"descriptor-invalid"})
    return frozenset()


def _validate_diagnostic_location_scope(
    value: object,
    path: str,
    allowed_scoped_codes: frozenset[str],
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        return
    for index, diagnostic in enumerate(value):
        if not isinstance(diagnostic, Mapping):
            continue
        code = diagnostic.get("code")
        if (
            isinstance(code, str)
            and code in _LOCATION_SCOPED_DIAGNOSTIC_CODES
            and code not in allowed_scoped_codes
        ):
            issues.append(
                ValidationIssue(
                    f"{path}[{index}].code",
                    "is not valid for this receipt category or location",
                )
            )


def _validate_diagnostic(  # noqa: C901,PLR0912
    diagnostic: Mapping[str, object],
    path: str,
    issues: list[ValidationIssue],
    *,
    expected_source_id: str | None = None,
) -> None:
    _validate_root_keys(diagnostic, _DIAGNOSTIC_KEYS, path, issues)
    diagnostic_id = diagnostic.get("diagnostic-id")
    if not isinstance(diagnostic_id, str) or diagnostic_id == "":
        issues.append(
            ValidationIssue(f"{path}.diagnostic-id", "must be a string")
        )
    code = diagnostic.get("code")
    if code not in REGISTERED_CI_VALIDATION_DIAGNOSTIC_CODES:
        issues.append(ValidationIssue(f"{path}.code", "is not registered"))
    elif code not in _RECEIPT_DIAGNOSTIC_CODES:
        issues.append(
            ValidationIssue(f"{path}.code", "is not valid for receipts")
        )
    detail = diagnostic.get("detail")
    if detail is not None:
        if detail not in REGISTERED_CI_VALIDATION_DIAGNOSTIC_DETAILS:
            issues.append(
                ValidationIssue(f"{path}.detail", "is not registered")
            )
        elif isinstance(
            code, str
        ) and detail not in DETAILS_BY_DIAGNOSTIC_CODE.get(code, frozenset()):
            issues.append(
                ValidationIssue(
                    f"{path}.detail",
                    "is not valid for this diagnostic code",
                ),
            )
    message = diagnostic.get("message")
    if message is not None and (not isinstance(message, str) or message == ""):
        issues.append(
            ValidationIssue(f"{path}.message", "must be null or non-empty")
        )
    source = diagnostic.get("source")
    if not isinstance(source, Mapping):
        issues.append(ValidationIssue(f"{path}.source", "must be an object"))
    else:
        _validate_root_keys(
            source, _DIAGNOSTIC_SOURCE_KEYS, f"{path}.source", issues
        )
        if source.get("type") != "work-group":
            issues.append(
                ValidationIssue(f"{path}.source.type", "must be work-group")
            )
        source_id = source.get("id")
        if source_id is not None and (
            not isinstance(source_id, str) or source_id == ""
        ):
            issues.append(
                ValidationIssue(
                    f"{path}.source.id", "must be null or non-empty"
                )
            )
        if expected_source_id is not None and source_id != expected_source_id:
            issues.append(
                ValidationIssue(
                    f"{path}.source.id", "must match receipt work group"
                )
            )
    severity = diagnostic.get("severity")
    severities = {
        item.value for item in DiagnosticSeverity.__members__.values()
    }
    if severity not in severities:
        issues.append(ValidationIssue(f"{path}.severity", "is not registered"))
    if severity == DiagnosticSeverity.FAIL_CLOSED.value:
        issues.append(
            ValidationIssue(f"{path}.severity", "is not valid for receipts")
        )
    effect = diagnostic.get("verdict-effect")
    effects = {
        item.value for item in DiagnosticVerdictEffect.__members__.values()
    }
    if effect not in effects:
        issues.append(
            ValidationIssue(f"{path}.verdict-effect", "is not registered")
        )
    if effect == DiagnosticVerdictEffect.FAIL_CLOSED.value:
        issues.append(
            ValidationIssue(
                f"{path}.verdict-effect",
                "is not valid for executable receipts",
            ),
        )


def _validate_result_diagnostic_outcome(
    diagnostics: object,
    outcome: str | None,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(diagnostics, Sequence) or isinstance(
        diagnostics, str | bytes
    ):
        return
    for diagnostic in diagnostics:
        if not isinstance(diagnostic, Mapping):
            continue
        _validate_diagnostic_code_outcome(diagnostic, outcome, path, issues)
    has_failed = any(
        isinstance(item, Mapping)
        and item.get("verdict-effect") == DiagnosticVerdictEffect.FAILED.value
        for item in diagnostics
    )
    if has_failed and outcome != "blocking-failure":
        issues.append(
            ValidationIssue(
                f"{path}.outcome",
                "must be blocking-failure for failed diagnostics",
            ),
        )
    if outcome in {"blocking-failure", "skipped"} and not _has_diagnostics(
        diagnostics
    ):
        issues.append(
            ValidationIssue(
                f"{path}.diagnostics",
                "must explain non-success outcome",
            ),
        )


def _validate_diagnostic_code_outcome(
    diagnostic: Mapping[str, object],
    outcome: str | None,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    code = diagnostic.get("code")
    effect = diagnostic.get("verdict-effect")
    if code in {
        "validation-work-failed",
        "artifact-shape-unconfirmed",
        "descriptor-invalid",
    }:
        if effect != DiagnosticVerdictEffect.FAILED.value:
            issues.append(
                ValidationIssue(
                    f"{path}.diagnostics",
                    "blocking diagnostic codes require failed verdict-effect",
                )
            )
        if outcome != "blocking-failure":
            issues.append(
                ValidationIssue(
                    f"{path}.outcome",
                    "must be blocking-failure for blocking diagnostics",
                )
            )
    if code == "validation-work-skipped":
        if effect != DiagnosticVerdictEffect.NONE.value:
            issues.append(
                ValidationIssue(
                    f"{path}.diagnostics",
                    "skipped diagnostics require none verdict-effect",
                )
            )
        if outcome != "skipped":
            issues.append(
                ValidationIssue(
                    f"{path}.outcome",
                    "must be skipped for skipped diagnostics",
                )
            )


def _outcome_with_top_diagnostics(
    base_outcome: str | None,
    diagnostics: object,
) -> str | None:
    if _has_failed_diagnostic(diagnostics):
        return "blocking-failure"
    return base_outcome


def _has_failed_diagnostic(diagnostics: object) -> bool:
    if not isinstance(diagnostics, Sequence) or isinstance(
        diagnostics, str | bytes
    ):
        return False
    return any(
        isinstance(item, Mapping)
        and item.get("verdict-effect") == DiagnosticVerdictEffect.FAILED.value
        for item in diagnostics
    )


def _has_diagnostics(diagnostics: object) -> bool:
    if not isinstance(diagnostics, Sequence) or isinstance(
        diagnostics, str | bytes
    ):
        return False
    return any(isinstance(item, Mapping) for item in diagnostics)


def _has_diagnostic_code(diagnostics: object, code: str) -> bool:
    if not isinstance(diagnostics, Sequence) or isinstance(
        diagnostics, str | bytes
    ):
        return False
    return any(
        isinstance(item, Mapping) and item.get("code") == code
        for item in diagnostics
    )


def _derive_outcome(outcomes: Sequence[str]) -> str | None:
    if not outcomes:
        return None
    if "blocking-failure" in outcomes:
        return "blocking-failure"
    if "skipped" in outcomes:
        return "skipped"
    return "success"


def _validate_outcome(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> str | None:
    if value not in _OUTCOMES:
        issues.append(ValidationIssue(path, "is not registered"))
        return None
    return cast("str", value)


def _matched_manifest_assignment(
    manifest: Mapping[str, object],
    assignment: Mapping[str, object],
    issues: list[ValidationIssue],
) -> Mapping[str, object] | None:
    assignments = manifest.get("assignments")
    if not isinstance(assignments, Sequence) or isinstance(
        assignments, str | bytes
    ):
        issues.append(
            ValidationIssue(
                "$.selector-assignments.assignments", "must be an array"
            )
        )
        return None
    for item in assignments:
        if isinstance(item, Mapping) and item.get(
            "assignment-id"
        ) == assignment.get("assignment-id"):
            if dict(item) != dict(assignment):
                issues.append(
                    ValidationIssue(
                        "$.assignment", "must exactly match selector assignment"
                    )
                )
                return None
            return item
    issues.append(
        ValidationIssue("$.assignment", "must match selector assignment")
    )
    return None


def _matched_work_group(
    plan: Mapping[str, object],
    work_group_id: object,
    issues: list[ValidationIssue],
) -> Mapping[str, object] | None:
    groups = plan.get("work-groups")
    if not isinstance(groups, Sequence) or isinstance(groups, str | bytes):
        issues.append(ValidationIssue("$.plan.work-groups", "must be an array"))
        return None
    matched = [
        item
        for item in groups
        if isinstance(item, Mapping)
        and item.get("work-group-id") == work_group_id
    ]
    if len(matched) != 1:
        issues.append(
            ValidationIssue("$.work-group-id", "must match one work group")
        )
        return None
    if matched[0].get("kind") == "evidence-aggregation":
        issues.append(ValidationIssue("$.work-group-id", "must be executable"))
        return None
    return matched[0]


def _matched_evidence_expectation(
    plan: Mapping[str, object],
    work_group_id: object,
    issues: list[ValidationIssue],
) -> Mapping[str, object] | None:
    expectations = plan.get("evidence-expectations")
    if not isinstance(expectations, Sequence) or isinstance(
        expectations, str | bytes
    ):
        issues.append(
            ValidationIssue("$.plan.evidence-expectations", "must be an array")
        )
        return None
    matched = [
        item
        for item in expectations
        if isinstance(item, Mapping)
        and item.get("work-group-id") == work_group_id
    ]
    if len(matched) != 1:
        issues.append(
            ValidationIssue(
                "$.work-group-id",
                "must match one evidence expectation",
            ),
        )
        return None
    return matched[0]


def _coverage_target(
    work_group: Mapping[str, object] | None,
    evidence: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> Mapping[str, object] | None:
    if work_group is None or evidence is None:
        return None
    target = work_group.get("coverage-target")
    if target != evidence.get("coverage-target"):
        issues.append(
            ValidationIssue("$.coverage-target", "plan bindings mismatch")
        )
    if not isinstance(target, Mapping):
        issues.append(ValidationIssue("$.coverage-target", "must be an object"))
        return None
    return target


def _matched_detail_profile(
    plan: Mapping[str, object],
    evidence: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> Mapping[str, object] | None:
    if evidence is None or evidence.get("detail-profile") is None:
        return None
    profile_id = evidence.get("detail-profile")
    profiles = plan.get("detail-profiles")
    if not isinstance(profiles, Sequence) or isinstance(profiles, str | bytes):
        issues.append(
            ValidationIssue("$.plan.detail-profiles", "must be an array")
        )
        return None
    matched = [
        item
        for item in profiles
        if isinstance(item, Mapping)
        and item.get("detail-profile-id") == profile_id
    ]
    if len(matched) != 1:
        issues.append(
            ValidationIssue("$.detail-profile", "must match one profile")
        )
        return None
    return matched[0]


def _records_for_work_group(
    value: object,
    key: str,
    work_group_id: object,
) -> list[Mapping[str, object]]:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        return []
    return [
        item
        for item in value
        if isinstance(item, Mapping)
        and item.get("work-group-id") == work_group_id
        and isinstance(item.get(key), str)
    ]


def _plan_envelope_or_collect(
    plan: Mapping[str, object],
    issues: list[ValidationIssue],
) -> CommonEnvelope | None:
    try:
        return validate_common_envelope(
            plan,
            api_version=API_VERSIONS_BY_KIND[CiValidationKind.PLAN.value],
            kind=CiValidationKind.PLAN,
        )
    except ContractValidationError as error:
        issues.extend(error.issues)
        return None


def _receipt_envelope_or_collect(
    receipt: Mapping[str, object],
    issues: list[ValidationIssue],
) -> CommonEnvelope | None:
    try:
        return validate_common_envelope(
            receipt,
            api_version=API_VERSIONS_BY_KIND[
                CiValidationKind.VALIDATION_RECEIPT.value
            ],
            kind=CiValidationKind.VALIDATION_RECEIPT,
        )
    except ContractValidationError as error:
        issues.extend(error.issues)
        return None


def _verified_plan_digest_or_collect(
    plan: Mapping[str, object],
    issues: list[ValidationIssue],
) -> str | None:
    plan_digest = plan.get("plan-digest")
    if (
        not isinstance(plan_digest, str)
        or _DIGEST_RE.fullmatch(plan_digest) is None
    ):
        issues.append(
            ValidationIssue("$.plan.plan-digest", "must be a SHA-256 digest")
        )
        return None
    try:
        recomputed = ci_validation_plan_digest(plan)
    except (TypeError, ValueError) as error:
        issues.append(ValidationIssue("$.plan.plan-digest", str(error)))
        return None
    if plan_digest != recomputed:
        issues.append(
            ValidationIssue("$.plan.plan-digest", "must match canonical plan")
        )
    return plan_digest


def _validate_envelope_matches_plan(
    envelope: CommonEnvelope,
    plan_envelope: CommonEnvelope,
    issues: list[ValidationIssue],
) -> None:
    if envelope.repository_owner != plan_envelope.repository_owner:
        issues.append(ValidationIssue("$.repository.owner", "must match plan"))
    if envelope.repository_name != plan_envelope.repository_name:
        issues.append(ValidationIssue("$.repository.name", "must match plan"))
    if envelope.workflow != plan_envelope.workflow:
        issues.append(ValidationIssue("$.run.workflow", "must match plan"))
    if envelope.run_id != plan_envelope.run_id:
        issues.append(ValidationIssue("$.run.run-id", "must match plan"))
    if envelope.run_attempt != plan_envelope.run_attempt:
        issues.append(ValidationIssue("$.run.run-attempt", "must match plan"))


def _validate_root_keys(
    document: Mapping[str, object],
    allowed_keys: frozenset[str],
    path: str,
    issues: list[ValidationIssue],
) -> None:
    keys = set()
    for key in document:
        if not isinstance(key, str):
            issues.append(ValidationIssue(path, "keys must be strings"))
            continue
        keys.add(key)
    for key in sorted(keys - allowed_keys):
        issues.append(ValidationIssue(f"{path}.{key}", "is not allowed"))
    for key in sorted(allowed_keys - keys):
        issues.append(ValidationIssue(f"{path}.{key}", "is required"))


def _compare(
    left: Mapping[str, object],
    key: str,
    right: Mapping[str, object],
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if left.get(key) != right.get(key):
        issues.append(ValidationIssue(f"{path}.{key}", "must match plan"))


def _validate_local_id(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(value, str) or _LOCAL_ID_RE.fullmatch(value) is None:
        issues.append(ValidationIssue(path, "must be path-safe"))


def _validate_sha(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(value, str) or _SHA_RE.fullmatch(value) is None:
        issues.append(ValidationIssue(path, "must be a lowercase SHA"))


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


def _validate_evidence_keys(
    evidence: Mapping[str, object],
    path: str,
    issues: list[ValidationIssue],
) -> None:
    keys = set()
    for key in evidence:
        if not isinstance(key, str):
            issues.append(ValidationIssue(path, "keys must be strings"))
            continue
        keys.add(key)
    for key in sorted(keys - _EVIDENCE_KEYS):
        issues.append(ValidationIssue(f"{path}.{key}", "is not allowed"))
    for key in ("category", "planned-capabilities", "artifact-refs"):
        if key not in keys:
            issues.append(ValidationIssue(f"{path}.{key}", "is required"))
