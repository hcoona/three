"""Plan and snapshot helpers for workflow-release CI validation."""

from __future__ import annotations

import re
import unicodedata
from collections.abc import Mapping, Sequence
from dataclasses import dataclass
from typing import Literal

from three_workflow_release_contracts.ci_validation import (
    API_VERSIONS_BY_KIND,
    DETAILS_BY_DIAGNOSTIC_CODE,
    REGISTERED_CI_VALIDATION_DIAGNOSTIC_CODES,
    REGISTERED_CI_VALIDATION_DIAGNOSTIC_DETAILS,
    CiValidationKind,
    CommonEnvelope,
    DiagnosticFamily,
    DiagnosticSeverity,
    DiagnosticVerdictEffect,
    canonical_json_bytes,
    canonical_json_digest,
    validate_artifact_logical_ref,
    validate_common_envelope,
)
from three_workflow_release_contracts.ci_validation_requests import (
    CiValidationRequestNormalization,
    NormalizedCiValidationRequest,
    ci_validation_request_artifact_ref,
)
from three_workflow_release_contracts.contracts import (
    ContractValidationError,
    ValidationIssue,
)

CiValidationPlanVerdictIntent = Literal["executable", "fail-closed"]
CiValidationSnapshotStatus = Literal["available", "unavailable"]
RunnerFamily = Literal["windows", "ubuntu"]

PLANNED_CAPABILITY_ORDER = ("build", "test", "lint", "format", "type-check")
_PLAN_VERDICT_INTENTS = frozenset({"executable", "fail-closed"})
_SNAPSHOT_STATUSES = frozenset({"available", "unavailable"})
_RUNNER_FAMILIES = frozenset({"windows", "ubuntu"})
_PLAN_MODES = frozenset({"pull_request", "push", "scheduled_full"})
_AFFECTED_STATUSES = frozenset({"available", "unavailable"})
_SHA_RE = re.compile(r"^[0-9a-f]{40}$")
_EXECUTABLE_WORK_GROUP_KINDS = frozenset(
    {
        "lightweight-preflight",
        "ecosystem-gate",
        "descriptor-validation",
        "release-shaped-artifact",
        "workflow-release-tooling",
    },
)
_WORK_GROUP_KINDS = _EXECUTABLE_WORK_GROUP_KINDS | frozenset(
    {"evidence-aggregation"},
)
_ECOSYSTEMS = frozenset(
    {"dotnet", "python", "javascript", "typescript", "ruby"}
)
_SUBJECT_ECOSYSTEMS = _ECOSYSTEMS | frozenset({"other"})
_ACTIVITY_STATUSES = frozenset({"active", "explicitly-excluded", "inactive"})
_SELECTION_STATUSES = frozenset({"selected", "not-selected"})
_CAPABILITY_CLASSES = frozenset({"descriptor-backed", "validation-only"})
_SUBJECT_REQUIRED_KEYS = (
    "subject-id",
    "ecosystem",
    "root",
    "activity-status",
    "selection-status",
    "capability-class",
    "descriptor",
    "capabilities",
    "inclusion",
    "exclusion",
)
_SUBJECT_CAPABILITIES = frozenset(
    {
        "build",
        "test",
        "lint",
        "format",
        "type-check",
        "release-shaped-artifacts",
    },
)
_INCLUSION_SOURCES = frozenset({"descriptor", "workspace", "solution"})
_EXECUTABLE_COVERAGE_TARGET_TYPES = frozenset(
    {
        "subject",
        "ecosystem",
        "descriptor",
        "tooling-surface",
        "artifact-obligation",
        "lightweight-policy",
    },
)
_DETAIL_PROFILE_COVERAGE_TARGET_TYPES = (
    _EXECUTABLE_COVERAGE_TARGET_TYPES - frozenset({"artifact-obligation"})
)
_TERMINAL_AGGREGATION_COVERAGE_TARGET = {
    "type": "aggregation",
    "id": "ci-validation-aggregate",
}
_EVIDENCE_CATEGORIES = frozenset(
    {
        "lightweight-preflight",
        "ecosystem-gate",
        "descriptor-validation",
        "release-shaped-artifact",
        "workflow-release-tooling",
    },
)
_DETAIL_PROFILE_CATEGORIES = frozenset(
    {"lightweight-preflight", "workflow-release-tooling"},
)
_DETAIL_PROFILE_REQUIRED_CATEGORIES = _DETAIL_PROFILE_CATEGORIES
_CATEGORY_RESULT_EVIDENCE_CATEGORIES = frozenset(
    {
        "descriptor-validation",
        "lightweight-preflight",
        "release-shaped-artifact",
        "workflow-release-tooling",
    },
)
_TOOLING_SURFACE_IDS = frozenset(
    {
        "planner",
        "classifier",
        "fact-provider",
        "descriptor-contract",
        "workflow-release-contract",
        "authoring-validation",
        "target-catalog",
        "workflow-orchestration",
        "build-execution",
        "publish-execution",
        "smoke-validation",
        "descriptor-schema-documentation",
    },
)
_SCHEDULED_FULL_EQUIVALENT_INFRASTRUCTURE_SURFACES = frozenset(
    {
        "planner",
        "classifier",
        "workflow-release-contract",
        "workflow-orchestration",
    },
)
_ALL_DESCRIPTOR_INFRASTRUCTURE_SURFACES = frozenset(
    {
        "fact-provider",
        "descriptor-contract",
        "authoring-validation",
        "build-execution",
        "publish-execution",
        "smoke-validation",
    },
)
_ARTIFACT_SCOPE_INFRASTRUCTURE_SURFACES = frozenset(
    {
        "descriptor-contract",
        "target-catalog",
        "build-execution",
        "publish-execution",
        "smoke-validation",
    },
)
_ACTIVE_BUILD_SCOPE_INFRASTRUCTURE_SURFACES = frozenset(
    {"build-execution"},
)
_LIGHTWEIGHT_POLICY_IDS = frozenset({"known-non-impacting"})
_SUBCHECK_KINDS = frozenset(
    {"configuration", "policy", "contract", "tool-discovery", "documentation"},
)
_IMPACT_CATEGORIES = frozenset(
    {
        "project-scoped",
        "ecosystem-scoped",
        "workflow-release-infrastructure",
        "global",
        "known-non-impacting",
        "unknown",
    },
)
_IMPACT_COVERAGE_TARGET_TYPES = frozenset(
    {"subject", "ecosystem", "tooling-surface", "global", "none"},
)
_BROAD_EXPANSION_CATEGORIES = frozenset(
    {"ecosystem", "global", "workflow-release-infrastructure"},
)
_SUBJECT_SELECTION_KINDS = frozenset(
    {"direct", "downstream", "broad-expansion", "scheduled-full"},
)
_CREDENTIAL_POSTURES = frozenset(
    {"credential-free", "unsigned-equivalent", "unavailable"},
)
_PROVIDER_IDS = frozenset(
    {"dotnet", "python", "javascript-typescript", "ruby", "workflow-release"},
)
_DEPENDENCY_RELATIONS = frozenset(
    {"project-reference", "package-reference", "workspace", "tooling"},
)
_DESCRIPTOR_SOURCES = frozenset(
    {"ecosystem-provider", "workflow-release-provider"},
)
_PLANNER_DIAGNOSTIC_SOURCES = frozenset(
    {
        "request",
        "impact",
        "subject",
        "descriptor",
        "fact-provider",
        "aggregation",
    },
)
_DIAGNOSTIC_SOURCES = _PLANNER_DIAGNOSTIC_SOURCES | frozenset({"work-group"})
_PLAN_LOCAL_ID_RE = re.compile(r"^[a-z0-9][a-z0-9._-]{0,127}$")
_DIGEST_RE = re.compile(r"^[0-9a-f]{64}$")
_MIN_DUPLICATE_TARGET_GROUPS = 2
_PR_MERGE_VERIFICATION_SOURCE = "github-control-plane"


@dataclass(frozen=True, slots=True)
class CiValidationPlanSnapshot:
    """Authoritative plan plus companion planner snapshots."""

    plan: Mapping[str, object]
    changed_files_snapshot: Mapping[str, object] | None
    fact_snapshot: Mapping[str, object] | None


@dataclass(frozen=True, slots=True)
class _BindingSections:
    evidence_expectations: Sequence[Mapping[str, object]]
    validation_obligations: Sequence[Mapping[str, object]]
    descriptor_obligations: Sequence[Mapping[str, object]]
    artifact_obligations: Sequence[Mapping[str, object]]


@dataclass(frozen=True, slots=True)
class _WorkGroupBindingRecords:
    evidence: Sequence[Mapping[str, object]]
    validation: Sequence[Mapping[str, object]]
    descriptor: Sequence[Mapping[str, object]]
    artifact: Sequence[Mapping[str, object]]


@dataclass(frozen=True, slots=True)
class _SpecialObligationResolution:
    evidence_ids: set[str]
    validation_ids: set[str]
    kind_by_work_group: Mapping[str, object]


@dataclass(frozen=True, slots=True)
class _FactIndexes:
    descriptors: Mapping[str, Mapping[str, object]]
    catalog_entries: Mapping[tuple[str, str], Sequence[Mapping[str, object]]]


@dataclass(frozen=True, slots=True)
class _CoverageTargetUniverse:
    subject_ids: set[str]
    ecosystems: set[str]
    descriptor_paths: set[str]
    artifact_ids: set[str]


def ci_validation_changed_files_snapshot_artifact_ref(
    *,
    run_id: str,
    run_attempt: str,
) -> str:
    """Return the contract-owned changed-files snapshot logical ref."""
    return (
        f"ci-validation/planning/{run_id}/{run_attempt}/"
        "changed-files-snapshot.json"
    )


def ci_validation_fact_snapshot_artifact_ref(
    *,
    run_id: str,
    run_attempt: str,
) -> str:
    """Return the contract-owned fact snapshot logical ref."""
    return f"ci-validation/planning/{run_id}/{run_attempt}/fact-snapshot.json"


def ci_validation_changed_files_hash(
    changed_files: Sequence[str],
) -> str:
    """Return the digest for the versioned changed-files hash payload."""
    return canonical_json_digest(_changed_files_hash_payload(changed_files))


def ci_validation_subject_universe_id(
    subjects: Sequence[Mapping[str, object]],
) -> str:
    """Return the digest for the frozen subject-universe section."""
    _validate_subjects(subjects)
    return canonical_json_digest(
        {
            "api-version": "three.ci.validation.subject-universe/v1alpha1",
            "subjects": [dict(subject) for subject in subjects],
        },
    )


def ci_validation_fact_snapshot_id(
    providers: Sequence[Mapping[str, object]],
) -> str:
    """Return the digest for a fact snapshot provider projection."""
    frozen_providers = _freeze_fact_snapshot_providers(providers)
    return canonical_json_digest(
        {
            "api-version": API_VERSIONS_BY_KIND[
                CiValidationKind.FACT_SNAPSHOT.value
            ],
            "kind": CiValidationKind.FACT_SNAPSHOT.value,
            "providers": frozen_providers,
        },
    )


def ci_validation_plan_digest(plan: Mapping[str, object]) -> str:
    """Return the validation-plan digest excluding root ``plan-digest``."""
    projection = dict(plan)
    projection.pop("plan-digest", None)
    return canonical_json_digest(projection)


def ci_validation_terminal_aggregation_work_group(
    *,
    depends_on: Sequence[str] = (),
) -> dict[str, object]:
    """Return the frozen terminal aggregation work group."""
    depends = _sorted_unique_strings(depends_on, "depends-on")
    return {
        "work-group-id": "evidence-aggregation",
        "kind": "evidence-aggregation",
        "coverage-target": dict(_TERMINAL_AGGREGATION_COVERAGE_TARGET),
        "runner-family": "ubuntu",
        "depends-on": depends,
        "aggregate-output": CiValidationKind.AGGREGATE_SUMMARY.value,
    }


def freeze_ci_validation_plan(  # noqa: PLR0913
    *,
    request: NormalizedCiValidationRequest,
    plan_id: str,
    created_at: str,
    observed_commit_sha: str,
    verdict_intent: CiValidationPlanVerdictIntent,
    classification: Mapping[str, object] | None = None,
    subjects: Sequence[Mapping[str, object]] = (),
    descriptor_obligations: Sequence[Mapping[str, object]] = (),
    validation_obligations: Sequence[Mapping[str, object]] = (),
    artifact_obligations: Sequence[Mapping[str, object]] = (),
    work_groups: Sequence[Mapping[str, object]] = (),
    evidence_expectations: Sequence[Mapping[str, object]] = (),
    detail_profiles: Sequence[Mapping[str, object]] = (),
    diagnostics: Sequence[Mapping[str, object]] = (),
    fact_snapshot_providers: Sequence[Mapping[str, object]] | None = (),
    pull_request_merge_commit_verification: Mapping[str, object] | None = None,
    policy_version: str | None = None,
) -> CiValidationPlanSnapshot:
    """Freeze a deterministic authoritative plan from a normalized request.

    The request parameter deliberately requires Group 3's trusted normalized
    request object. Raw dictionaries and invalid normalization results are
    rejected so planner code cannot forge a plan from untrusted input.
    """
    _validate_normalized_request(request)
    _validate_plan_id(plan_id)
    _validate_verdict_intent(verdict_intent)
    if observed_commit_sha != _validation_tree(request)["commit-sha"]:
        raise ContractValidationError(
            [
                ValidationIssue(
                    "observed-commit-sha",
                    "must equal validation-tree.commit-sha",
                ),
            ],
        )
    frozen_subjects = _sorted_records(subjects, "subject-id", "subjects")
    _validate_subjects(frozen_subjects)
    frozen_diagnostics = _sorted_records(
        diagnostics,
        "diagnostic-id",
        "diagnostics",
    )
    _validate_unavailable_range_diagnostics_for_request(
        request,
        frozen_diagnostics,
    )
    _validate_diagnostics(frozen_diagnostics, verdict_intent)
    frozen_work_groups = _freeze_work_groups(work_groups)
    frozen_work_groups = _ensure_terminal_work_group(frozen_work_groups)
    executable_work_group_ids = [
        str(group["work-group-id"])
        for group in frozen_work_groups
        if group["kind"] != "evidence-aggregation"
    ]
    _validate_no_executable_work(verdict_intent, executable_work_group_ids)
    _validate_fail_closed_sections(
        verdict_intent,
        {
            "descriptor-obligations": descriptor_obligations,
            "validation-obligations": validation_obligations,
            "artifact-obligations": artifact_obligations,
            "evidence-expectations": evidence_expectations,
            "detail-profiles": detail_profiles,
        },
    )
    _validate_evidence_expectations(evidence_expectations)
    fact_snapshot = _freeze_fact_snapshot(
        request=request,
        plan_id=plan_id,
        created_at=created_at,
        providers=fact_snapshot_providers,
    )
    changed_files_snapshot = _freeze_changed_files_snapshot(
        request=request,
        created_at=created_at,
    )
    affected_range = _plan_affected_range(request, changed_files_snapshot)
    frozen_classification = _freeze_classification(classification)
    fact_status = "unavailable" if fact_snapshot is None else "available"
    subject_status = (
        "available" if fact_status == "available" else "unavailable"
    )
    _validate_plan_statuses(
        request=request,
        verdict_intent=verdict_intent,
        fact_snapshot=fact_snapshot,
        subjects=frozen_subjects,
    )
    if verdict_intent == "executable" and fact_snapshot is None:
        raise ContractValidationError(
            [
                ValidationIssue(
                    "fact-snapshot",
                    "is required for executable plans",
                ),
            ],
        )
    if verdict_intent == "executable":
        _validate_no_unavailable_fact_providers(
            fact_snapshot,
            "fact-snapshot.providers",
        )
    _validate_detail_profiles(detail_profiles)
    _validate_fact_backed_obligations(
        subjects=frozen_subjects,
        classification=frozen_classification,
        descriptor_obligations=descriptor_obligations,
        artifact_obligations=artifact_obligations,
        work_groups=frozen_work_groups,
        fact_snapshot=fact_snapshot,
        allow_global_workflow_descriptor_impacts=(
            verdict_intent == "executable"
            and request.mode in {"pull_request", "push"}
            and _classification_has_global_impact(frozen_classification)
        ),
    )
    _validate_provider_subject_coverage(
        subjects=frozen_subjects,
        provider_subjects=_provider_subject_projection(
            fact_snapshot,
            frozen_subjects,
        ),
    )
    plan = _plan_document(
        request=request,
        plan_id=plan_id,
        created_at=created_at,
        verdict_intent=verdict_intent,
        affected_range=affected_range,
        policy_version=policy_version,
        observed_commit_sha=observed_commit_sha,
        subject_status=subject_status,
        subject_id=(
            ci_validation_subject_universe_id(frozen_subjects)
            if subject_status == "available"
            else None
        ),
        fact_status=fact_status,
        fact_id=(
            str(fact_snapshot["fact-snapshot-id"])
            if fact_snapshot is not None
            else None
        ),
        classification=frozen_classification,
        subjects=frozen_subjects,
        descriptor_obligations=_sorted_records(
            descriptor_obligations,
            "descriptor-obligation-id",
            "descriptor-obligations",
        ),
        validation_obligations=_sorted_records(
            validation_obligations,
            "validation-obligation-id",
            "validation-obligations",
        ),
        artifact_obligations=_sorted_records(
            artifact_obligations,
            "artifact-obligation-id",
            "artifact-obligations",
        ),
        work_groups=frozen_work_groups,
        evidence_expectations=_sorted_records(
            evidence_expectations,
            "evidence-expectation-id",
            "evidence-expectations",
        ),
        detail_profiles=_sorted_records(
            detail_profiles,
            "detail-profile-id",
            "detail-profiles",
        ),
        diagnostics=frozen_diagnostics,
        pull_request_merge_commit_verification=(
            pull_request_merge_commit_verification
        ),
    )
    coverage_validated = False
    if (
        verdict_intent == "executable"
        and (classification is None or not frozen_classification.get("impacts"))
        and fact_snapshot is not None
        and frozen_work_groups
        and evidence_expectations
        and (
            validation_obligations
            or descriptor_obligations
            or artifact_obligations
        )
    ):
        _validate_classification_changed_files_for_request(
            frozen_classification,
            request,
        )
        coverage_validated = True
    plan["plan-digest"] = ci_validation_plan_digest(plan)
    validate_ci_validation_plan(
        plan,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        pull_request_merge_commit_verification=(
            pull_request_merge_commit_verification
        ),
    )
    if verdict_intent == "executable" and not coverage_validated:
        _validate_classification_changed_files_for_request(
            frozen_classification,
            request,
        )
    return CiValidationPlanSnapshot(
        plan=plan,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
    )


def plan_from_request_normalization(  # noqa: PLR0913
    normalization: CiValidationRequestNormalization,
    *,
    plan_id: str,
    created_at: str,
    observed_commit_sha: str,
    verdict_intent: CiValidationPlanVerdictIntent,
    diagnostics: Sequence[Mapping[str, object]] = (),
    fact_snapshot_providers: Sequence[Mapping[str, object]] | None = (),
    pull_request_merge_commit_verification: Mapping[str, object] | None = None,
) -> CiValidationPlanSnapshot | None:
    """Freeze a plan only when Group 3 request normalization succeeded."""
    if not isinstance(normalization, CiValidationRequestNormalization):
        raise ContractValidationError(
            [
                ValidationIssue(
                    "normalization",
                    "must be a request normalization",
                ),
            ],
        )
    if normalization.request is None:
        return None
    return freeze_ci_validation_plan(
        request=normalization.request,
        plan_id=plan_id,
        created_at=created_at,
        observed_commit_sha=observed_commit_sha,
        verdict_intent=verdict_intent,
        diagnostics=diagnostics,
        fact_snapshot_providers=fact_snapshot_providers,
        pull_request_merge_commit_verification=(
            pull_request_merge_commit_verification
        ),
    )


def validate_ci_validation_plan(  # noqa: PLR0913
    plan: object,
    *,
    changed_files_snapshot: Mapping[str, object] | None = None,
    fact_snapshot: Mapping[str, object] | None = None,
    pull_request_merge_commit_verification: Mapping[str, object] | None = None,
    expected_run_id: str | None = None,
    expected_run_attempt: str | None = None,
) -> None:
    """Validate the reusable Group 4 plan envelope and frozen structure."""
    issues: list[ValidationIssue] = []
    if not isinstance(plan, Mapping):
        raise ContractValidationError(
            [ValidationIssue("$", "must be an object")],
        )
    try:
        envelope = validate_common_envelope(
            plan,
            api_version=API_VERSIONS_BY_KIND[CiValidationKind.PLAN.value],
            kind=CiValidationKind.PLAN,
        )
    except ContractValidationError as error:
        issues.extend(error.issues)
        envelope = None
    if envelope is not None:
        if expected_run_id is not None and envelope.run_id != expected_run_id:
            issues.append(
                ValidationIssue("$.run.run-id", "must match expected run"),
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
    _validate_required_plan_members(plan, issues)
    if not issues:
        _validate_plan_digest(plan, issues)
        _validate_plan_envelope_runtime(
            plan,
            issues,
            pull_request_merge_commit_verification=(
                pull_request_merge_commit_verification
            ),
        )
        changed_files_issue_count = len(issues)
        _validate_companion_changed_files_snapshot(
            plan,
            changed_files_snapshot,
            issues,
        )
        changed_files_companion_valid = len(issues) == changed_files_issue_count
        _validate_companion_fact_snapshot(
            plan,
            fact_snapshot,
            issues,
            bind_provider_identity=changed_files_companion_valid,
        )
        _validate_plan_sections(
            plan,
            issues,
            changed_files_snapshot=changed_files_snapshot
            if changed_files_companion_valid
            else None,
            fact_snapshot=fact_snapshot,
        )
    if issues:
        raise ContractValidationError(issues)


def validate_ci_validation_plan_structure(  # noqa: PLR0913
    plan: object,
    *,
    changed_files_snapshot: Mapping[str, object] | None = None,
    fact_snapshot: Mapping[str, object] | None = None,
    pull_request_merge_commit_verification: Mapping[str, object] | None = None,
    expected_run_id: str | None = None,
    expected_run_attempt: str | None = None,
) -> None:
    """Validate plan structure without requiring companion artifacts."""
    issues: list[ValidationIssue] = []
    if not isinstance(plan, Mapping):
        raise ContractValidationError(
            [ValidationIssue("$", "must be an object")],
        )
    try:
        envelope = validate_common_envelope(
            plan,
            api_version=API_VERSIONS_BY_KIND[CiValidationKind.PLAN.value],
            kind=CiValidationKind.PLAN,
        )
    except ContractValidationError as error:
        issues.extend(error.issues)
        envelope = None
    if envelope is not None:
        if expected_run_id is not None and envelope.run_id != expected_run_id:
            issues.append(
                ValidationIssue("$.run.run-id", "must match expected run"),
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
    _validate_required_plan_members(plan, issues)
    if not issues:
        _validate_plan_digest(plan, issues)
        _validate_plan_envelope_runtime(
            plan,
            issues,
            pull_request_merge_commit_verification=(
                pull_request_merge_commit_verification
            ),
        )
        _validate_plan_sections(
            plan,
            issues,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
        )
    if issues:
        raise ContractValidationError(issues)


def _validate_normalized_request(request: object) -> None:
    if not isinstance(request, NormalizedCiValidationRequest):
        raise ContractValidationError(
            [ValidationIssue("request", "must be a normalized CI request")],
        )


def _validate_plan_id(plan_id: object) -> None:
    issues: list[ValidationIssue] = []
    _validate_plan_id_value(plan_id, "plan-id", issues)
    if issues:
        raise ContractValidationError(
            issues,
        )


def _validate_plan_id_value(
    plan_id: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(plan_id, str) or not _PLAN_LOCAL_ID_RE.fullmatch(plan_id):
        issues.append(
            ValidationIssue(path, "must be a stable plan identifier"),
        )


def _validate_verdict_intent(verdict_intent: object) -> None:
    if verdict_intent not in _PLAN_VERDICT_INTENTS:
        raise ContractValidationError(
            [ValidationIssue("verdict-intent", "is not registered")],
        )


def _validation_tree(
    request: NormalizedCiValidationRequest,
) -> Mapping[str, object]:
    value = request.projection["validation-tree"]
    if not isinstance(value, Mapping):
        message = "normalized request validation-tree must be an object"
        raise TypeError(message)
    return value


def _plan_validation_tree(
    request: NormalizedCiValidationRequest,
    affected_range: Mapping[str, object],
    pull_request_merge_commit_verification: Mapping[str, object] | None = None,
) -> dict[str, object]:
    validation_tree = _validation_tree(request)
    plan_tree = {
        "commit-sha": validation_tree["commit-sha"],
        "ref": validation_tree["ref"],
    }
    if (
        request.mode == "pull_request"
        and affected_range.get("status") == "available"
        and validation_tree.get("commit-sha") != affected_range.get("head-sha")
    ):
        plan_tree["merge-commit"] = _freeze_pull_request_merge_commit(
            validation_tree,
            affected_range,
            pull_request_merge_commit_verification,
        )
    return plan_tree


def _freeze_pull_request_merge_commit(
    validation_tree: Mapping[str, object],
    affected_range: Mapping[str, object],
    verification: Mapping[str, object] | None,
) -> dict[str, object]:
    issues: list[ValidationIssue] = []
    if verification is None:
        raise ContractValidationError(
            [
                ValidationIssue(
                    "pull-request-merge-commit-verification",
                    "is required for non-head pull_request validation trees",
                ),
            ],
        )
    _validate_pull_request_merge_commit_verification(
        verification,
        validation_tree,
        validation_tree.get("commit-sha"),
        affected_range.get("base-tip-sha"),
        affected_range.get("head-sha"),
        issues,
        "pull-request-merge-commit-verification",
    )
    if issues:
        raise ContractValidationError(issues)
    return {
        "commit-sha": verification["commit-sha"],
        "base-tip-sha": verification["base-tip-sha"],
        "head-sha": verification["head-sha"],
        "ref": verification["ref"],
        "verified": True,
        "verification-source": _PR_MERGE_VERIFICATION_SOURCE,
    }


def _validate_pull_request_merge_commit_verification(  # noqa: PLR0913
    verification: Mapping[str, object],
    validation_tree: Mapping[str, object],
    tree_sha: object,
    base_tip: object,
    head_sha: object,
    issues: list[ValidationIssue],
    path: str,
) -> None:
    allowed_keys = {
        "commit-sha",
        "base-tip-sha",
        "head-sha",
        "ref",
        "verified",
        "verification-source",
    }
    for key in sorted(set(verification) - allowed_keys):
        issues.append(ValidationIssue(f"{path}.{key}", "is not allowed"))
    if verification.get("verified") is not True:
        issues.append(ValidationIssue(f"{path}.verified", "must be true"))
    if verification.get("verification-source") != _PR_MERGE_VERIFICATION_SOURCE:
        issues.append(
            ValidationIssue(
                f"{path}.verification-source",
                f"must be {_PR_MERGE_VERIFICATION_SOURCE}",
            ),
        )
    if verification.get("commit-sha") != tree_sha:
        issues.append(
            ValidationIssue(
                f"{path}.commit-sha",
                "must match validation-tree.commit-sha",
            ),
        )
    if verification.get("base-tip-sha") != base_tip:
        issues.append(
            ValidationIssue(
                f"{path}.base-tip-sha",
                "must match affected-range.base-tip-sha",
            ),
        )
    if verification.get("head-sha") != head_sha:
        issues.append(
            ValidationIssue(
                f"{path}.head-sha",
                "must match affected-range.head-sha",
            ),
        )
    if verification.get("ref") != validation_tree.get("ref"):
        issues.append(
            ValidationIssue(f"{path}.ref", "must match validation-tree.ref"),
        )
    if not _is_pull_request_merge_ref(verification.get("ref")):
        issues.append(
            ValidationIssue(f"{path}.ref", "must be a pull request merge ref"),
        )


def _freeze_classification(
    classification: Mapping[str, object] | None,
) -> dict[str, object]:
    if classification is None:
        classification = {}
    required_defaults: dict[str, object] = {
        "impacts": [],
        "broad-expansions": [],
        "subject-selection-provenance": [],
        "subsumptions": [],
        "lightweight-only": False,
    }
    frozen = {**required_defaults, **dict(classification)}
    frozen["impacts"] = _sorted_records(
        _sequence(frozen["impacts"], "classification.impacts"),
        "impact-id",
        "classification.impacts",
    )
    frozen["broad-expansions"] = _sorted_records(
        _sequence(
            frozen["broad-expansions"],
            "classification.broad-expansions",
        ),
        "expansion-id",
        "classification.broad-expansions",
    )
    frozen["subject-selection-provenance"] = _sorted_records(
        _sequence(
            frozen["subject-selection-provenance"],
            "classification.subject-selection-provenance",
        ),
        "provenance-id",
        "classification.subject-selection-provenance",
    )
    frozen["subsumptions"] = _sorted_records(
        _sequence(frozen["subsumptions"], "classification.subsumptions"),
        "subsumption-id",
        "classification.subsumptions",
    )
    if not isinstance(frozen["lightweight-only"], bool):
        raise ContractValidationError(
            [
                ValidationIssue(
                    "classification.lightweight-only",
                    "must be a boolean",
                ),
            ],
        )
    return frozen


def _validate_classification_changed_files_for_request(  # noqa: C901
    classification: Mapping[str, object],
    request: NormalizedCiValidationRequest,
) -> None:
    if request.mode == "scheduled_full":
        return
    affected_range = request.projection.get("affected-range")
    if not isinstance(affected_range, Mapping):
        return
    if affected_range.get("status") != "available":
        return
    changed_files = affected_range.get("changed-files")
    if not isinstance(changed_files, Sequence) or isinstance(
        changed_files,
        str | bytes,
    ):
        raise ContractValidationError(
            [
                ValidationIssue(
                    "affected-range.changed-files",
                    "must be a string array",
                ),
            ],
        )
    expected = _sorted_unique_strings(changed_files, "changed-files")
    impacts = _sequence(
        classification.get("impacts"),
        "classification.impacts",
    )
    matched_paths: list[str] = []
    issues: list[ValidationIssue] = []
    for index, impact in enumerate(impacts):
        if not isinstance(impact, Mapping):
            issues.append(
                ValidationIssue(
                    f"classification.impacts[{index}]",
                    "must be an object",
                ),
            )
            continue
        paths = impact.get("matched-paths")
        if not isinstance(paths, Sequence) or isinstance(paths, str | bytes):
            issues.append(
                ValidationIssue(
                    f"classification.impacts[{index}].matched-paths",
                    "must be a string array",
                ),
            )
            continue
        matched_paths.extend(str(path) for path in paths)
    if issues:
        raise ContractValidationError(issues)
    try:
        actual = _sorted_unique_strings(matched_paths, "matched-paths")
    except ContractValidationError as error:
        raise ContractValidationError(
            [
                *error.issues,
                ValidationIssue(
                    "classification.impacts.matched-paths",
                    "must not overlap across impact records",
                ),
            ],
        ) from error
    if actual != expected:
        raise ContractValidationError(
            [
                ValidationIssue(
                    "classification.impacts.matched-paths",
                    "must exactly cover changed files",
                ),
            ],
        )


def _freeze_work_groups(
    work_groups: Sequence[Mapping[str, object]],
) -> list[dict[str, object]]:
    groups = _sorted_records(work_groups, "work-group-id", "work-groups")
    for group in groups:
        _validate_work_group(group)
    return groups


def _ensure_terminal_work_group(
    work_groups: list[dict[str, object]],
) -> list[dict[str, object]]:
    terminal = [
        group
        for group in work_groups
        if group["kind"] == "evidence-aggregation"
    ]
    if len(terminal) > 1:
        raise ContractValidationError(
            [ValidationIssue("work-groups", "must have one terminal group")],
        )
    if terminal:
        return work_groups
    return sorted(
        [
            *work_groups,
            ci_validation_terminal_aggregation_work_group(
                depends_on=[
                    str(group["work-group-id"]) for group in work_groups
                ],
            ),
        ],
        key=lambda item: str(item["work-group-id"]),
    )


def _validate_work_group(group: Mapping[str, object]) -> None:
    issues: list[ValidationIssue] = []
    work_group_id = _required_str(group, "work-group-id", issues)
    kind = _required_str(group, "kind", issues)
    if work_group_id and not _PLAN_LOCAL_ID_RE.fullmatch(work_group_id):
        issues.append(ValidationIssue("work-group-id", "is not path-safe"))
    if kind not in _WORK_GROUP_KINDS:
        issues.append(ValidationIssue("kind", "is not registered"))
    if kind == "evidence-aggregation":
        _validate_terminal_group(group, issues)
    else:
        _validate_executable_group(group, issues)
    if issues:
        raise ContractValidationError(issues)


def _validate_terminal_group(
    group: Mapping[str, object],
    issues: list[ValidationIssue],
) -> None:
    if group.get("runner-family") != "ubuntu":
        issues.append(ValidationIssue("runner-family", "must be ubuntu"))
    if (
        group.get("aggregate-output")
        != CiValidationKind.AGGREGATE_SUMMARY.value
    ):
        issues.append(
            ValidationIssue(
                "aggregate-output",
                "must be ci-validation-aggregate-summary",
            ),
        )
    if group.get("coverage-target") != _TERMINAL_AGGREGATION_COVERAGE_TARGET:
        issues.append(
            ValidationIssue(
                "coverage-target",
                "must be ci-validation aggregate",
            ),
        )
    _validate_depends_on(group.get("depends-on"), issues)


def _validate_executable_group(
    group: Mapping[str, object],
    issues: list[ValidationIssue],
) -> None:
    _validate_executable_runner(group, issues)
    _validate_depends_on(group.get("depends-on"), issues)
    _validate_coverage_target(
        group.get("coverage-target"), "coverage-target", issues
    )
    _validate_selector_variant(group.get("selector-variant"), issues)
    _validate_work_group_expected_evidence(
        group.get("expected-evidence"),
        issues,
    )


def _validate_executable_runner(
    group: Mapping[str, object],
    issues: list[ValidationIssue],
) -> None:
    runner_family = group.get("runner-family")
    ecosystem = group.get("ecosystem")
    kind = group.get("kind")
    if runner_family not in _RUNNER_FAMILIES:
        issues.append(ValidationIssue("runner-family", "is not registered"))
    if ecosystem is not None and ecosystem not in _ECOSYSTEMS:
        issues.append(ValidationIssue("ecosystem", "is not registered"))
    if kind == "lightweight-preflight" and ecosystem is not None:
        issues.append(ValidationIssue("ecosystem", "must be null"))
    if kind == "lightweight-preflight" and runner_family != "ubuntu":
        issues.append(ValidationIssue("runner-family", "must be ubuntu"))
    if (
        kind == "descriptor-validation"
        and ecosystem is None
        and runner_family != "ubuntu"
    ):
        issues.append(ValidationIssue("runner-family", "must be ubuntu"))
    if kind == "workflow-release-tooling":
        _validate_tooling_group_ecosystem(group, issues)
    if kind in {"ecosystem-gate", "release-shaped-artifact"} and (
        ecosystem not in _ECOSYSTEMS
    ):
        issues.append(ValidationIssue("ecosystem", "is required"))
    if ecosystem == "dotnet" and runner_family != "windows":
        issues.append(ValidationIssue("runner-family", "must be windows"))
    if ecosystem in {"python", "javascript", "typescript", "ruby"} and (
        runner_family != "ubuntu"
    ):
        issues.append(ValidationIssue("runner-family", "must be ubuntu"))


def _validate_tooling_group_ecosystem(
    group: Mapping[str, object],
    issues: list[ValidationIssue],
) -> None:
    ecosystem = group.get("ecosystem")
    if ecosystem is None:
        return
    target = group.get("coverage-target")
    selector_variant = group.get("selector-variant")
    if (
        not isinstance(target, Mapping)
        or target.get("type") != "ecosystem"
        or target.get("id") != ecosystem
        or not isinstance(selector_variant, str)
        or selector_variant == ""
    ):
        issues.append(
            ValidationIssue(
                "ecosystem",
                "must be null unless tooling targets ecosystem-owned scope",
            ),
        )


def _validate_selector_variant(
    selector_variant: object,
    issues: list[ValidationIssue],
) -> None:
    if selector_variant is None:
        return
    if (
        not isinstance(selector_variant, str)
        or _PLAN_LOCAL_ID_RE.fullmatch(selector_variant) is None
    ):
        issues.append(
            ValidationIssue("selector-variant", "must be null or path-safe")
        )


def _validate_work_group_expected_evidence(
    expected: object,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(expected, Mapping):
        issues.append(ValidationIssue("expected-evidence", "must be an object"))
        return
    if expected.get("category") not in _EVIDENCE_CATEGORIES:
        issues.append(
            ValidationIssue("expected-evidence.category", "is invalid")
        )
    _validate_planned_capabilities(
        expected.get("planned-capabilities"),
        expected.get("category"),
        "expected-evidence.planned-capabilities",
        issues,
    )
    profile = expected.get("detail-profile")
    if profile is not None and (
        not isinstance(profile, str)
        or _PLAN_LOCAL_ID_RE.fullmatch(profile) is None
    ):
        issues.append(
            ValidationIssue(
                "expected-evidence.detail-profile",
                "must be null or a detail-profile id",
            ),
        )


def _validate_depends_on(
    value: object,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        issues.append(ValidationIssue("depends-on", "must be a string array"))
        return
    try:
        _sorted_unique_strings(value, "depends-on")
    except ContractValidationError as error:
        issues.extend(error.issues)


def _validate_no_executable_work(
    verdict_intent: str,
    executable_work_group_ids: Sequence[str],
) -> None:
    if verdict_intent == "fail-closed" and executable_work_group_ids:
        raise ContractValidationError(
            [
                ValidationIssue(
                    "work-groups",
                    "fail-closed plans must not contain executable work groups",
                ),
            ],
        )


def _validate_fail_closed_sections(
    verdict_intent: str,
    sections: Mapping[str, Sequence[Mapping[str, object]]],
) -> None:
    if verdict_intent != "fail-closed":
        return
    nonempty_sections = [name for name, section in sections.items() if section]
    if nonempty_sections:
        raise ContractValidationError(
            [
                ValidationIssue(
                    nonempty_sections[0],
                    "fail-closed plans must leave obligation sections empty",
                ),
            ],
        )


def _validate_plan_statuses(
    *,
    request: NormalizedCiValidationRequest,
    verdict_intent: str,
    fact_snapshot: Mapping[str, object] | None,
    subjects: Sequence[Mapping[str, object]],
) -> None:
    if _request_range_unavailable(request) and verdict_intent != "fail-closed":
        raise ContractValidationError(
            [
                ValidationIssue(
                    "affected-range.status",
                    "unavailable affected ranges force fail-closed planning",
                ),
            ],
        )
    if fact_snapshot is None and subjects:
        raise ContractValidationError(
            [
                ValidationIssue(
                    "subjects",
                    "must be empty when subject-universe is unavailable",
                ),
            ],
        )


def _request_range_unavailable(
    request: NormalizedCiValidationRequest,
) -> bool:
    if request.mode == "scheduled_full":
        return False
    affected_range = request.projection["affected-range"]
    if not isinstance(affected_range, Mapping):
        message = "normalized request affected-range must be an object"
        raise TypeError(message)
    return affected_range.get("status") == "unavailable"


def _request_range_unconfirmed_detail(
    request: NormalizedCiValidationRequest,
) -> object | None:
    if not _request_range_unavailable(request):
        return None
    affected_range = request.projection["affected-range"]
    if not isinstance(affected_range, Mapping):
        message = "normalized request affected-range must be an object"
        raise TypeError(message)
    return affected_range.get("diagnostic-detail")


def _validate_subjects(subjects: Sequence[Mapping[str, object]]) -> None:
    issues: list[ValidationIssue] = []
    try:
        _sorted_records(subjects, "subject-id", "subjects")
    except ContractValidationError as error:
        issues.extend(error.issues)
    for index, subject in enumerate(subjects):
        if not isinstance(subject, Mapping):
            continue
        _validate_subject(subject, f"subjects[{index}]", issues)
    if issues:
        raise ContractValidationError(issues)


def _provider_subject_projection(  # noqa: C901
    fact_snapshot: Mapping[str, object] | None,
    subjects: Sequence[Mapping[str, object]] | None = None,
) -> list[dict[str, str]] | None:
    if fact_snapshot is None:
        return None
    providers = fact_snapshot.get("providers")
    if not isinstance(providers, Sequence) or isinstance(
        providers, str | bytes
    ):
        return None
    subject_ecosystems = {
        str(subject.get("subject-id")): str(subject.get("ecosystem"))
        for subject in subjects or []
        if isinstance(subject.get("subject-id"), str)
        and subject.get("ecosystem") in _ECOSYSTEMS
    }
    provider_subjects: list[dict[str, str]] = []
    for provider in providers:
        if not isinstance(provider, Mapping):
            continue
        provider_id = provider.get("provider")
        if provider_id == "workflow-release":
            continue
        if provider.get("status") != "available":
            continue
        subjects = provider.get("subjects")
        if not isinstance(subjects, Sequence) or isinstance(
            subjects, str | bytes
        ):
            continue
        for subject_id in subjects:
            if isinstance(subject_id, str):
                record = {
                    "subject-id": subject_id,
                    "provider": str(provider_id),
                }
                ecosystem = subject_ecosystems.get(subject_id)
                if ecosystem is None:
                    provider_ecosystems = _provider_ecosystems(str(provider_id))
                    if len(provider_ecosystems) == 1:
                        ecosystem = next(iter(provider_ecosystems))
                if ecosystem is not None:
                    record["ecosystem"] = ecosystem
                provider_subjects.append(record)
    return sorted(
        provider_subjects,
        key=lambda item: (item["subject-id"], item["provider"]),
    )


def _validate_provider_subject_coverage(
    *,
    subjects: Sequence[Mapping[str, object]],
    provider_subjects: Sequence[Mapping[str, object]] | None,
) -> None:
    if provider_subjects is None:
        return
    issues: list[ValidationIssue] = []
    subjects_by_id = {
        str(subject.get("subject-id")): str(subject.get("ecosystem"))
        for subject in subjects
        if subject.get("ecosystem") in _ECOSYSTEMS
        and isinstance(subject.get("subject-id"), str)
    }
    provider_map: dict[str, str] = {}
    seen: set[str] = set()
    for index, record in enumerate(provider_subjects):
        subject_id = record.get("subject-id")
        provider = record.get("provider")
        ecosystem = record.get("ecosystem")
        path = f"subject-universe.provider-subjects[{index}]"
        if (
            not isinstance(subject_id, str)
            or not isinstance(provider, str)
            or not isinstance(ecosystem, str)
        ):
            issues.append(
                ValidationIssue(path, "must bind subject/provider/ecosystem"),
            )
            continue
        if subject_id in seen:
            issues.append(ValidationIssue(path, "duplicates provider coverage"))
        seen.add(subject_id)
        provider_map[subject_id] = provider
        subject_ecosystem = subjects_by_id.get(subject_id)
        if subject_ecosystem is None:
            continue
        if ecosystem != subject_ecosystem:
            issues.append(ValidationIssue(path, "ecosystem mismatch"))
        if subject_ecosystem not in _provider_ecosystems(provider):
            issues.append(
                ValidationIssue(path, "provider does not match ecosystem"),
            )
    if sorted(subjects_by_id) != sorted(provider_map):
        issues.append(
            ValidationIssue(
                "subject-universe.provider-subjects",
                (
                    "must exactly match available ecosystem-provider "
                    "subject bindings"
                ),
            ),
        )
    if issues:
        raise ContractValidationError(issues)


def _provider_ecosystems(provider: str) -> frozenset[str]:
    if provider == "dotnet":
        return frozenset({"dotnet"})
    if provider == "python":
        return frozenset({"python"})
    if provider == "javascript-typescript":
        return frozenset({"javascript", "typescript"})
    if provider == "ruby":
        return frozenset({"ruby"})
    return frozenset()


def _validate_subject_member_set(
    subject: Mapping[str, object],
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if set(subject) != set(_SUBJECT_REQUIRED_KEYS):
        issues.append(
            ValidationIssue(
                path,
                "must only contain registered subject members",
            ),
        )
    for key in _SUBJECT_REQUIRED_KEYS:
        if key not in subject:
            issues.append(ValidationIssue(f"{path}.{key}", "is required"))


def _validate_subject(
    subject: Mapping[str, object],
    path: str,
    issues: list[ValidationIssue],
) -> None:
    _validate_subject_member_set(subject, path, issues)
    subject_id = _required_str(subject, "subject-id", issues)
    root = _required_str(subject, "root", issues)
    if root is not None:
        _validate_repo_directory_root(root, f"{path}.root", issues)
    ecosystem = subject.get("ecosystem")
    if ecosystem not in _SUBJECT_ECOSYSTEMS:
        issues.append(ValidationIssue(f"{path}.ecosystem", "is not registered"))
    activity = subject.get("activity-status")
    selection = subject.get("selection-status")
    capability_class = subject.get("capability-class")
    if activity not in _ACTIVITY_STATUSES:
        issues.append(ValidationIssue(f"{path}.activity-status", "is invalid"))
    if selection not in _SELECTION_STATUSES:
        issues.append(ValidationIssue(f"{path}.selection-status", "is invalid"))
    if capability_class not in _CAPABILITY_CLASSES:
        issues.append(ValidationIssue(f"{path}.capability-class", "is invalid"))
    _validate_subject_descriptor(subject.get("descriptor"), path, issues)
    capabilities = _validate_subject_capabilities(
        subject.get("capabilities"),
        path,
        issues,
    )
    _validate_subject_inclusion(subject.get("inclusion"), path, issues)
    _validate_subject_exclusion(subject.get("exclusion"), path, issues)
    if ecosystem not in _ECOSYSTEMS:
        _validate_unsupported_subject(subject, path, issues)
    _validate_subject_status_rules(subject, path, capabilities, issues)
    if subject_id is not None and "/" in subject_id:
        issues.append(
            ValidationIssue(f"{path}.subject-id", "must be stable id")
        )


def _validate_subject_status_rules(
    subject: Mapping[str, object],
    path: str,
    capabilities: Mapping[str, bool] | None,
    issues: list[ValidationIssue],
) -> None:
    activity = subject.get("activity-status")
    selection = subject.get("selection-status")
    capability_class = subject.get("capability-class")
    if selection == "selected" and activity != "active":
        issues.append(
            ValidationIssue(
                f"{path}.selection-status",
                "selected subjects must be active",
            ),
        )
    if (
        selection == "selected"
        and activity == "active"
        and capability_class == "validation-only"
        and capabilities is not None
        and not any(capabilities[name] for name in PLANNED_CAPABILITY_ORDER)
    ):
        issues.append(
            ValidationIssue(
                f"{path}.capabilities",
                "selected validation-only subjects need a capability",
            ),
        )
    if (
        capability_class == "validation-only"
        and capabilities is not None
        and capabilities["release-shaped-artifacts"]
    ):
        issues.append(
            ValidationIssue(
                f"{path}.capabilities.release-shaped-artifacts",
                "validation-only subjects cannot publish artifacts",
            ),
        )


def _validate_subject_descriptor(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    descriptor = _mapping_or_issue(value, f"{path}.descriptor", issues)
    if descriptor is None:
        return
    descriptor_path = descriptor.get("path")
    _nullable_str(descriptor_path, f"{path}.descriptor.path", issues)
    if descriptor_path is not None:
        _validate_repo_relative_git_path(
            descriptor_path,
            f"{path}.descriptor.path",
            issues,
        )
    _nullable_str(
        descriptor.get("identity"),
        f"{path}.descriptor.identity",
        issues,
    )


def _validate_subject_capabilities(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> dict[str, bool] | None:
    capabilities = _mapping_or_issue(value, f"{path}.capabilities", issues)
    if capabilities is None:
        return None
    result: dict[str, bool] = {}
    for key in sorted(_SUBJECT_CAPABILITIES):
        item = capabilities.get(key)
        if not isinstance(item, bool):
            issues.append(
                ValidationIssue(f"{path}.capabilities.{key}", "must be bool")
            )
        else:
            result[key] = item
    for key in set(capabilities) - _SUBJECT_CAPABILITIES:
        issues.append(
            ValidationIssue(f"{path}.capabilities.{key}", "is not allowed")
        )
    return result if len(result) == len(_SUBJECT_CAPABILITIES) else None


def _validate_subject_inclusion(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    inclusion = _mapping_or_issue(value, f"{path}.inclusion", issues)
    if inclusion is None:
        return
    if inclusion.get("source") not in _INCLUSION_SOURCES:
        issues.append(ValidationIssue(f"{path}.inclusion.source", "is invalid"))
    if not isinstance(inclusion.get("reason"), str) or not inclusion.get(
        "reason"
    ):
        issues.append(
            ValidationIssue(f"{path}.inclusion.reason", "is required")
        )


def _validate_subject_exclusion(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    exclusion = _mapping_or_issue(value, f"{path}.exclusion", issues)
    if exclusion is None:
        return
    _nullable_str(exclusion.get("reason"), f"{path}.exclusion.reason", issues)


def _validate_unsupported_subject(
    subject: Mapping[str, object],
    path: str,
    issues: list[ValidationIssue],
) -> None:
    descriptor = subject.get("descriptor")
    capabilities = subject.get("capabilities")
    exclusion = subject.get("exclusion")
    if subject.get("activity-status") != "inactive":
        issues.append(
            ValidationIssue(f"{path}.activity-status", "must be inactive")
        )
    if subject.get("selection-status") != "not-selected":
        issues.append(
            ValidationIssue(f"{path}.selection-status", "must be not-selected"),
        )
    if isinstance(descriptor, Mapping) and (
        descriptor.get("path") is not None
        or descriptor.get("identity") is not None
    ):
        issues.append(
            ValidationIssue(f"{path}.descriptor", "must be null-valued")
        )
    if isinstance(capabilities, Mapping) and any(capabilities.values()):
        issues.append(
            ValidationIssue(f"{path}.capabilities", "must all be false")
        )
    if not isinstance(exclusion, Mapping) or (
        exclusion.get("reason") != "unsupported-ecosystem"
    ):
        issues.append(
            ValidationIssue(
                f"{path}.exclusion.reason",
                "must be unsupported-ecosystem",
            ),
        )


def _validate_fact_backed_obligations(  # noqa: PLR0913
    *,
    subjects: Sequence[Mapping[str, object]],
    classification: Mapping[str, object] | None,
    descriptor_obligations: Sequence[Mapping[str, object]],
    artifact_obligations: Sequence[Mapping[str, object]],
    work_groups: Sequence[Mapping[str, object]],
    fact_snapshot: Mapping[str, object] | None,
    allow_global_workflow_descriptor_impacts: bool = False,
) -> None:
    issues: list[ValidationIssue] = []
    if fact_snapshot is None:
        if descriptor_obligations or artifact_obligations:
            issues.append(ValidationIssue("fact-snapshot", "is required"))
        if issues:
            raise ContractValidationError(issues)
        return
    indexes = _fact_indexes(fact_snapshot)
    subjects_by_id = {
        str(subject.get("subject-id")): subject
        for subject in subjects
        if isinstance(subject.get("subject-id"), str)
    }
    groups_by_id = {
        str(group.get("work-group-id")): group
        for group in work_groups
        if isinstance(group.get("work-group-id"), str)
    }
    impacts_by_id = _impacts_by_id(classification)
    for obligation in descriptor_obligations:
        _validate_descriptor_fact_backing(
            obligation,
            indexes,
            subjects_by_id,
            impacts_by_id,
            groups_by_id,
            issues,
            allow_global_workflow_descriptor_impacts=(
                allow_global_workflow_descriptor_impacts
            ),
        )
    for obligation in artifact_obligations:
        _validate_artifact_fact_backing(
            obligation,
            indexes,
            subjects_by_id,
            groups_by_id,
            issues,
        )
    _validate_artifact_target_catalog_profile_coverage(
        subjects,
        artifact_obligations,
        indexes,
        issues,
    )
    if issues:
        raise ContractValidationError(issues)


def _fact_indexes(fact_snapshot: Mapping[str, object]) -> _FactIndexes:
    descriptors: dict[str, Mapping[str, object]] = {}
    catalog_entries: dict[tuple[str, str], list[Mapping[str, object]]] = {}
    providers = fact_snapshot.get("providers")
    if not isinstance(providers, Sequence) or isinstance(
        providers, str | bytes
    ):
        return _FactIndexes(descriptors, catalog_entries)
    for provider in providers:
        if not isinstance(provider, Mapping):
            continue
        for descriptor in _mapping_items(provider.get("descriptors")):
            descriptor_path = descriptor.get("descriptor-path")
            if isinstance(descriptor_path, str):
                descriptors[descriptor_path] = descriptor
        catalog = provider.get("target-catalog")
        if not isinstance(catalog, Mapping):
            continue
        for entry in _mapping_items(catalog.get("entries")):
            descriptor_path = entry.get("descriptor-path")
            profile = entry.get("profile")
            if isinstance(descriptor_path, str) and isinstance(profile, str):
                catalog_entries.setdefault(
                    (descriptor_path, profile), []
                ).append(entry)
    return _FactIndexes(descriptors, catalog_entries)


def _target_catalog_profiles_by_descriptor(
    fact_snapshot: Mapping[str, object],
) -> dict[str, set[str]]:
    profiles_by_descriptor: dict[str, set[str]] = {}
    for descriptor_path, profile in _fact_indexes(
        fact_snapshot
    ).catalog_entries:
        profiles_by_descriptor.setdefault(descriptor_path, set()).add(profile)
    return profiles_by_descriptor


def _mapping_items(value: object) -> list[Mapping[str, object]]:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        return []
    return [item for item in value if isinstance(item, Mapping)]


def _validate_descriptor_fact_backing(  # noqa: PLR0913
    obligation: Mapping[str, object],
    indexes: _FactIndexes,
    subjects_by_id: Mapping[str, Mapping[str, object]],
    impacts_by_id: Mapping[str, Mapping[str, object]],
    groups_by_id: Mapping[str, Mapping[str, object]],
    issues: list[ValidationIssue],
    *,
    allow_global_workflow_descriptor_impacts: bool = False,
) -> None:
    target = obligation.get("coverage-target")
    _validate_coverage_target(
        target, "descriptor-obligation.coverage-target", issues
    )
    if not isinstance(target, Mapping) or target.get("type") != "descriptor":
        issues.append(
            ValidationIssue(
                "descriptor-obligation.coverage-target",
                "must target descriptor",
            ),
        )
        return
    descriptor_path = target.get("id")
    descriptor = indexes.descriptors.get(str(descriptor_path))
    if descriptor is None:
        issues.append(
            ValidationIssue(
                "descriptor-obligation.coverage-target",
                "must resolve to descriptor fact",
            ),
        )
        return
    source = descriptor.get("source")
    owner_subject_id = descriptor.get("owner-subject-id")
    if source == "ecosystem-provider":
        selected_owner = _selected_descriptor_subject_id(
            str(descriptor_path),
            subjects_by_id,
        )
        if selected_owner is None or owner_subject_id != selected_owner:
            issues.append(
                ValidationIssue(
                    "descriptor-obligation.coverage-target",
                    "must resolve to selected descriptor-backed subject",
                ),
            )
        _validate_descriptor_group_ecosystem(
            obligation,
            descriptor,
            subjects_by_id,
            groups_by_id,
            issues,
        )
    elif source == "workflow-release-provider":
        if owner_subject_id is not None:
            issues.append(
                ValidationIssue(
                    "descriptor-obligation.coverage-target",
                    "workflow-release descriptor must not have owner subject",
                ),
            )
        _validate_descriptor_group_ecosystem(
            obligation,
            descriptor,
            subjects_by_id,
            groups_by_id,
            issues,
        )
        _validate_workflow_release_descriptor_impact(
            obligation,
            impacts_by_id,
            issues,
            allow_global_impacts=allow_global_workflow_descriptor_impacts,
        )


def _validate_descriptor_group_ecosystem(
    obligation: Mapping[str, object],
    descriptor: Mapping[str, object],
    subjects_by_id: Mapping[str, Mapping[str, object]],
    groups_by_id: Mapping[str, Mapping[str, object]],
    issues: list[ValidationIssue],
) -> None:
    work_group_id = obligation.get("work-group-id")
    if not isinstance(work_group_id, str):
        return
    group = groups_by_id.get(work_group_id)
    if group is None or group.get("kind") != "descriptor-validation":
        return
    group_ecosystem = group.get("ecosystem")
    if obligation.get("descriptor-scope") != "ecosystem":
        if group_ecosystem is not None:
            issues.append(
                ValidationIssue(
                    "descriptor-obligation.ecosystem",
                    "must be null unless descriptor-scope is ecosystem",
                ),
            )
        return
    owner_subject_id = descriptor.get("owner-subject-id")
    owner_subject = (
        subjects_by_id.get(owner_subject_id)
        if isinstance(owner_subject_id, str)
        else None
    )
    owner_ecosystem = (
        owner_subject.get("ecosystem")
        if isinstance(owner_subject, Mapping)
        else None
    )
    if owner_ecosystem not in _ECOSYSTEMS:
        issues.append(
            ValidationIssue(
                "descriptor-obligation.ecosystem",
                "must resolve to owning subject ecosystem",
            ),
        )
    elif group_ecosystem != owner_ecosystem:
        issues.append(
            ValidationIssue(
                "descriptor-obligation.ecosystem",
                "must match owning subject ecosystem",
            ),
        )


def _selected_descriptor_subject_id(
    descriptor_path: str,
    subjects_by_id: Mapping[str, Mapping[str, object]],
) -> str | None:
    for subject_id, subject in subjects_by_id.items():
        descriptor = subject.get("descriptor")
        if (
            subject.get("activity-status") == "active"
            and subject.get("selection-status") == "selected"
            and subject.get("capability-class") == "descriptor-backed"
            and isinstance(descriptor, Mapping)
            and descriptor.get("path") == descriptor_path
        ):
            return subject_id
    return None


def _impacts_by_id(
    classification: Mapping[str, object] | None,
) -> dict[str, Mapping[str, object]]:
    impacts = (
        classification.get("impacts") if classification is not None else None
    )
    if not isinstance(impacts, Sequence) or isinstance(impacts, str | bytes):
        return {}
    return {
        str(impact.get("impact-id")): impact
        for impact in impacts
        if isinstance(impact, Mapping)
        and isinstance(impact.get("impact-id"), str)
    }


def _validate_workflow_release_descriptor_impact(
    obligation: Mapping[str, object],
    impacts_by_id: Mapping[str, Mapping[str, object]],
    issues: list[ValidationIssue],
    *,
    allow_global_impacts: bool = False,
) -> None:
    impact_ids = obligation.get("source-impact-ids")
    allowed_target_types = {"tooling-surface"}
    if (
        allow_global_impacts
        and obligation.get("descriptor-scope") == "all-discovered"
    ):
        allowed_target_types.add("global")
    if not isinstance(impact_ids, Sequence) or isinstance(
        impact_ids,
        str | bytes,
    ):
        issues.append(
            ValidationIssue(
                "descriptor-obligation.source-impact-ids",
                "must be tooling-surface or global impacts"
                if "global" in allowed_target_types
                else "must be tooling-surface impacts",
            ),
        )
        return
    for impact_id in impact_ids:
        impact = impacts_by_id.get(str(impact_id))
        target = impact.get("coverage-target") if impact is not None else None
        if (
            not isinstance(target, Mapping)
            or target.get("type") not in allowed_target_types
        ):
            issues.append(
                ValidationIssue(
                    "descriptor-obligation.source-impact-ids",
                    "must be tooling-surface or global impacts"
                    if "global" in allowed_target_types
                    else "must be tooling-surface impacts",
                ),
            )
            return


def _validate_artifact_fact_backing(
    obligation: Mapping[str, object],
    indexes: _FactIndexes,
    subjects_by_id: Mapping[str, Mapping[str, object]],
    groups_by_id: Mapping[str, Mapping[str, object]],
    issues: list[ValidationIssue],
) -> None:
    _validate_artifact_obligation_schema(obligation, issues)
    subject_id = obligation.get("subject-id")
    descriptor_path = obligation.get("descriptor-path")
    subject = subjects_by_id.get(str(subject_id))
    descriptor = indexes.descriptors.get(str(descriptor_path))
    if subject is None:
        issues.append(
            ValidationIssue("artifact-obligation.subject-id", "unknown")
        )
    else:
        _validate_artifact_subject_eligibility(
            subject,
            str(descriptor_path),
            issues,
        )
    if descriptor is None:
        issues.append(
            ValidationIssue("artifact-obligation.descriptor-path", "unbacked"),
        )
    elif descriptor.get("owner-subject-id") != subject_id:
        issues.append(
            ValidationIssue(
                "artifact-obligation.descriptor-path",
                "owner subject mismatch",
            ),
        )
    group = groups_by_id.get(str(obligation.get("work-group-id")))
    if subject is not None and group is not None:
        _validate_artifact_group_subject_match(group, subject, issues)
    _validate_artifact_catalog_backing(obligation, indexes, issues)


def _validate_artifact_subject_eligibility(
    subject: Mapping[str, object],
    descriptor_path: str,
    issues: list[ValidationIssue],
) -> None:
    if subject.get("activity-status") != "active":
        issues.append(
            ValidationIssue("artifact-obligation.subject-id", "inactive")
        )
    if subject.get("selection-status") != "selected":
        issues.append(
            ValidationIssue("artifact-obligation.subject-id", "unselected")
        )
    if subject.get("capability-class") != "descriptor-backed":
        issues.append(
            ValidationIssue(
                "artifact-obligation.subject-id",
                "must be descriptor-backed",
            ),
        )
    subject_descriptor = subject.get("descriptor")
    if not isinstance(subject_descriptor, Mapping) or (
        subject_descriptor.get("path") != descriptor_path
    ):
        issues.append(
            ValidationIssue(
                "artifact-obligation.descriptor-path",
                "must match subject descriptor path",
            ),
        )


def _validate_artifact_group_subject_match(
    group: Mapping[str, object],
    subject: Mapping[str, object],
    issues: list[ValidationIssue],
) -> None:
    ecosystem = subject.get("ecosystem")
    if group.get("ecosystem") != ecosystem:
        issues.append(
            ValidationIssue("artifact-obligation.ecosystem", "mismatch")
        )
    expected_runner = "windows" if ecosystem == "dotnet" else "ubuntu"
    if group.get("runner-family") != expected_runner:
        issues.append(
            ValidationIssue("artifact-obligation.runner-family", "mismatch")
        )


def _validate_artifact_obligation_schema(
    obligation: Mapping[str, object],
    issues: list[ValidationIssue],
) -> None:
    _required_str(obligation, "artifact-obligation-id", issues)
    _required_str(obligation, "subject-id", issues)
    descriptor_path = _required_str(obligation, "descriptor-path", issues)
    if descriptor_path is not None:
        _validate_repo_relative_git_path(
            descriptor_path,
            "artifact-obligation.descriptor-path",
            issues,
        )
    if (
        obligation.get("expected-evidence-category")
        != "release-shaped-artifact"
    ):
        issues.append(
            ValidationIssue(
                "artifact-obligation.expected-evidence-category", "invalid"
            ),
        )
    if obligation.get("credential-posture") not in _CREDENTIAL_POSTURES:
        issues.append(
            ValidationIssue("artifact-obligation.credential-posture", "bad")
        )
    _validate_profile_coverage(obligation.get("profile-coverage"), issues)
    _validate_artifact_payload(obligation.get("artifact"), issues)
    _validate_receipt_payload(obligation.get("release-receipt"), issues)


def _validate_profile_coverage(
    value: object,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        issues.append(
            ValidationIssue("artifact-obligation.profile-coverage", "array")
        )
        return
    if not value:
        issues.append(
            ValidationIssue(
                "artifact-obligation.profile-coverage",
                "must exactly cover target-catalog profiles",
            ),
        )
        return
    try:
        _sorted_unique_strings(value, "artifact-obligation.profile-coverage")
    except ContractValidationError as error:
        issues.extend(error.issues)


def _validate_artifact_payload(
    value: object,
    issues: list[ValidationIssue],
) -> None:
    artifact = _mapping_or_issue(value, "artifact-obligation.artifact", issues)
    if artifact is None:
        return
    for key in ("kind-family", "concrete-kind", "logical-artifact-role"):
        _required_str(artifact, key, issues)
    if not isinstance(artifact.get("variant-dimensions"), Mapping):
        issues.append(
            ValidationIssue(
                "artifact-obligation.artifact.variant-dimensions", "object"
            ),
        )
    refs = artifact.get("expected-artifact-refs")
    if not isinstance(refs, Sequence) or isinstance(refs, str | bytes):
        issues.append(
            ValidationIssue(
                "artifact-obligation.artifact.expected-artifact-refs", "array"
            ),
        )
        return
    try:
        refs = _sorted_unique_strings(
            refs,
            "artifact-obligation.artifact.expected-artifact-refs",
        )
    except ContractValidationError as error:
        issues.extend(error.issues)
        return
    if not refs:
        issues.append(
            ValidationIssue(
                "artifact-obligation.artifact.expected-artifact-refs",
                "non-empty",
            ),
        )
    for ref in refs:
        try:
            validate_artifact_logical_ref(ref)
        except ContractValidationError as error:
            issues.extend(error.issues)


def _validate_receipt_payload(
    value: object,
    issues: list[ValidationIssue],
) -> None:
    receipt = _mapping_or_issue(
        value, "artifact-obligation.release-receipt", issues
    )
    if receipt is None:
        return
    for key in ("expected-family", "logical-receipt-role"):
        _required_str(receipt, key, issues)
    if not isinstance(receipt.get("variant-dimensions"), Mapping):
        issues.append(
            ValidationIssue(
                "artifact-obligation.release-receipt.variant-dimensions",
                "object",
            ),
        )


def _validate_artifact_catalog_backing(
    obligation: Mapping[str, object],
    indexes: _FactIndexes,
    issues: list[ValidationIssue],
) -> None:
    descriptor_path = str(obligation.get("descriptor-path"))
    profiles = _string_items(obligation.get("profile-coverage"))
    for profile in profiles:
        entries = indexes.catalog_entries.get((descriptor_path, profile), ())
        if not entries:
            issues.append(
                ValidationIssue(
                    "artifact-obligation.profile-coverage", "unbacked"
                ),
            )
            continue
        if not any(
            _artifact_catalog_entry_matches(obligation, entry)
            for entry in entries
        ):
            issues.append(
                ValidationIssue("artifact-obligation.artifact", "unbacked")
            )


def _validate_artifact_target_catalog_profile_coverage(
    subjects: Sequence[Mapping[str, object]],
    artifact_obligations: Sequence[Mapping[str, object]],
    indexes: _FactIndexes,
    issues: list[ValidationIssue],
) -> None:
    entries_by_descriptor = _catalog_entry_keys_by_descriptor(indexes, issues)
    for subject_id, descriptor_path in _selected_descriptor_subject_bindings(
        subjects,
    ):
        subject_obligations = [
            obligation
            for obligation in artifact_obligations
            if obligation.get("subject-id") == subject_id
            and obligation.get("descriptor-path") == descriptor_path
        ]
        if not subject_obligations:
            continue
        required_entries = entries_by_descriptor.get(descriptor_path, set())
        if not required_entries:
            issues.append(
                ValidationIssue(
                    "artifact-obligation.profile-coverage",
                    "must exactly cover target-catalog profiles",
                ),
            )
            continue
        covered_entries: set[tuple[object, ...]] = set()
        duplicate_entries: set[tuple[object, ...]] = set()
        for obligation in subject_obligations:
            for profile in _string_items(obligation.get("profile-coverage")):
                key = _artifact_obligation_catalog_key(obligation, profile)
                if key in covered_entries:
                    duplicate_entries.add(key)
                covered_entries.add(key)
        if duplicate_entries:
            issues.append(
                ValidationIssue(
                    "artifact-obligation.profile-coverage",
                    "must not duplicate target-catalog profiles",
                ),
            )
        if covered_entries != required_entries:
            issues.append(
                ValidationIssue(
                    "artifact-obligation.profile-coverage",
                    "must exactly cover target-catalog profiles",
                ),
            )


def _catalog_entry_keys_by_descriptor(
    indexes: _FactIndexes,
    issues: list[ValidationIssue],
) -> dict[str, set[tuple[object, ...]]]:
    entries_by_descriptor: dict[str, set[tuple[object, ...]]] = {}
    for (descriptor_path, _profile), entries in indexes.catalog_entries.items():
        descriptor_entries = entries_by_descriptor.setdefault(
            descriptor_path,
            set(),
        )
        for entry in entries:
            try:
                descriptor_entries.add(_target_catalog_entry_key(entry))
            except (TypeError, ValueError) as error:
                issues.append(
                    ValidationIssue(
                        "artifact-obligation.profile-coverage",
                        "cannot canonicalize fact snapshot target catalog "
                        f"dimensions: {error}",
                    ),
                )
    return entries_by_descriptor


def _selected_descriptor_subject_bindings(
    subjects: Sequence[Mapping[str, object]],
) -> list[tuple[str, str]]:
    bindings: list[tuple[str, str]] = []
    for subject in subjects:
        if (
            subject.get("activity-status") != "active"
            or subject.get("selection-status") != "selected"
            or subject.get("capability-class") != "descriptor-backed"
        ):
            continue
        subject_id = subject.get("subject-id")
        descriptor = subject.get("descriptor")
        descriptor_path = (
            descriptor.get("path") if isinstance(descriptor, Mapping) else None
        )
        if isinstance(subject_id, str) and isinstance(descriptor_path, str):
            bindings.append((subject_id, descriptor_path))
    return bindings


def _artifact_catalog_entry_matches(
    obligation: Mapping[str, object],
    entry: Mapping[str, object],
) -> bool:
    artifact = obligation.get("artifact")
    receipt = obligation.get("release-receipt")
    entry_artifact = entry.get("artifact")
    entry_receipt = entry.get("release-receipt")
    return artifact == entry_artifact and receipt == entry_receipt


def _validate_evidence_expectations(
    evidence_expectations: Sequence[Mapping[str, object]],
) -> None:
    for item in evidence_expectations:
        issues: list[ValidationIssue] = []
        _validate_coverage_target(
            item.get("coverage-target"),
            "evidence-expectation.coverage-target",
            issues,
        )
        if item.get("category") not in _EVIDENCE_CATEGORIES:
            issues.append(
                ValidationIssue("evidence-expectation.category", "invalid")
            )
        capabilities = item.get("planned-capabilities")
        _validate_planned_capabilities(
            capabilities,
            item.get("category"),
            "evidence-expectation.planned-capabilities",
            issues,
        )
        profile = item.get("detail-profile")
        if profile is not None and (
            not isinstance(profile, str)
            or _PLAN_LOCAL_ID_RE.fullmatch(profile) is None
        ):
            issues.append(
                ValidationIssue(
                    "evidence-expectation.detail-profile",
                    "must be null or a detail-profile id",
                ),
            )
        if issues:
            raise ContractValidationError(issues)


def _validate_coverage_target(
    value: object,
    path: str,
    issues: list[ValidationIssue],
    *,
    allowed_types: frozenset[str] = _EXECUTABLE_COVERAGE_TARGET_TYPES,
) -> None:
    target = _mapping_or_issue(value, path, issues)
    if target is None:
        return
    target_type = target.get("type")
    target_id = target.get("id")
    if target_type not in allowed_types:
        issues.append(ValidationIssue(f"{path}.type", "is not registered"))
    _validate_coverage_target_id(target_type, target_id, path, issues)


def _validate_coverage_target_id(
    target_type: object,
    target_id: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if target_type == "aggregation":
        if target_id != "ci-validation-aggregate":
            issues.append(ValidationIssue(f"{path}.id", "is invalid"))
    elif target_type == "tooling-surface":
        if target_id not in _TOOLING_SURFACE_IDS:
            issues.append(ValidationIssue(f"{path}.id", "is not registered"))
    elif target_type == "lightweight-policy":
        if target_id not in _LIGHTWEIGHT_POLICY_IDS:
            issues.append(ValidationIssue(f"{path}.id", "is not registered"))
    elif target_type in {"global", "none"}:
        if target_id is not None:
            issues.append(ValidationIssue(f"{path}.id", "must be null"))
    elif not isinstance(target_id, str) or target_id == "":
        issues.append(ValidationIssue(f"{path}.id", "must be non-empty string"))


def _validate_planned_capabilities(
    value: object,
    category: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if category in _CATEGORY_RESULT_EVIDENCE_CATEGORIES and value is not None:
        issues.append(ValidationIssue(path, "must be null for category result"))
        return
    if value is None:
        return
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        issues.append(ValidationIssue(path, "must be null or an array"))
        return
    capabilities = list(value)
    if not capabilities:
        issues.append(
            ValidationIssue(path, "must be a non-empty capability branch"),
        )
    if any(item not in PLANNED_CAPABILITY_ORDER for item in capabilities):
        issues.append(ValidationIssue(path, "contains unregistered capability"))
    ordered = [
        item for item in PLANNED_CAPABILITY_ORDER if item in capabilities
    ]
    if capabilities != ordered or len(capabilities) != len(set(capabilities)):
        issues.append(ValidationIssue(path, "must be in declared unique order"))


def _freeze_fact_snapshot(
    *,
    request: NormalizedCiValidationRequest,
    plan_id: str,
    created_at: str,
    providers: Sequence[Mapping[str, object]] | None,
) -> dict[str, object] | None:
    if providers is None:
        return None
    frozen_providers = _freeze_fact_snapshot_providers(providers)
    if not frozen_providers:
        return None
    fact_snapshot_id = ci_validation_fact_snapshot_id(frozen_providers)
    return {
        "api-version": API_VERSIONS_BY_KIND[
            CiValidationKind.FACT_SNAPSHOT.value
        ],
        "kind": CiValidationKind.FACT_SNAPSHOT.value,
        "created-at": created_at,
        "repository": _repository(request),
        "run": _run(request),
        "schema-diagnostics": [],
        "artifact-ref": ci_validation_fact_snapshot_artifact_ref(
            run_id=request.run_id,
            run_attempt=request.run_attempt,
        ),
        "fact-snapshot-id": fact_snapshot_id,
        "plan-id": plan_id,
        "providers": frozen_providers,
    }


def _freeze_fact_snapshot_providers(
    providers: Sequence[Mapping[str, object]],
) -> list[dict[str, object]]:
    frozen_providers = _sorted_records(providers, "provider", "providers")
    descriptor_paths: set[str] = set()
    for provider_index, provider in enumerate(frozen_providers):
        _validate_provider(
            provider,
            path=f"providers[{provider_index}]",
            descriptor_paths=descriptor_paths,
        )
    for provider_index, provider in enumerate(frozen_providers):
        _validate_provider_catalog(
            provider,
            path=f"providers[{provider_index}]",
            descriptor_paths=descriptor_paths,
        )
    return frozen_providers


def _validate_provider(
    provider: Mapping[str, object],
    *,
    path: str,
    descriptor_paths: set[str],
) -> None:
    issues: list[ValidationIssue] = []
    _validate_provider_scalars(provider, path, issues)
    if provider.get("status") == "unavailable":
        _validate_unavailable_provider(provider, path, issues)
        if issues:
            raise ContractValidationError(issues)
        return
    for key in ("roots", "subjects", "tooling-surfaces"):
        _validate_sorted_string_array(provider, key, f"{path}.{key}", issues)
    tooling_surfaces = _string_items(provider.get("tooling-surfaces"))
    for index, surface in enumerate(tooling_surfaces):
        if surface not in _TOOLING_SURFACE_IDS:
            issues.append(
                ValidationIssue(
                    f"{path}.tooling-surfaces[{index}]",
                    "must resolve to closed tooling surface",
                ),
            )
    if provider.get("provider") == "workflow-release" and _string_items(
        provider.get("subjects")
    ):
        issues.append(
            ValidationIssue(
                f"{path}.subjects",
                "workflow-release provider subjects must be empty",
            ),
        )
    for index, root in enumerate(_string_items(provider.get("roots"))):
        _validate_repo_directory_root(
            root,
            f"{path}.roots[{index}]",
            issues,
        )
    _validate_dependency_edges(
        provider.get("dependency-edges"),
        f"{path}.dependency-edges",
        issues,
        provider_subject_ids=set(_string_items(provider.get("subjects"))),
    )
    _validate_descriptors(
        provider.get("descriptors"),
        f"{path}.descriptors",
        descriptor_paths,
        issues,
    )
    _validate_provider_diagnostics(
        provider.get("diagnostics"),
        f"{path}.diagnostics",
        issues,
    )
    if issues:
        raise ContractValidationError(issues)


def _validate_unavailable_provider(
    provider: Mapping[str, object],
    path: str,
    issues: list[ValidationIssue],
) -> None:
    for key in (
        "roots",
        "subjects",
        "dependency-edges",
        "tooling-surfaces",
        "descriptors",
    ):
        value = provider.get(key)
        if not isinstance(value, Sequence) or isinstance(value, str | bytes):
            issues.append(ValidationIssue(f"{path}.{key}", "must be an array"))
        elif value:
            issues.append(ValidationIssue(f"{path}.{key}", "must be empty"))
    catalog = provider.get("target-catalog")
    if not isinstance(catalog, Mapping):
        issues.append(
            ValidationIssue(f"{path}.target-catalog", "must be object"),
        )
    elif not _target_catalog_empty(catalog):
        issues.append(
            ValidationIssue(f"{path}.target-catalog", "must be empty"),
        )
    diagnostics = provider.get("diagnostics")
    if (
        not isinstance(diagnostics, Sequence)
        or isinstance(diagnostics, str | bytes)
        or not diagnostics
    ):
        issues.append(
            ValidationIssue(f"{path}.diagnostics", "must be non-empty"),
        )
        return
    _validate_provider_diagnostics(
        diagnostics,
        f"{path}.diagnostics",
        issues,
    )


def _target_catalog_empty(catalog: Mapping[str, object]) -> bool:
    if catalog.get("catalog-id") is not None:
        return False
    for key in ("descriptor-paths", "entries"):
        value = catalog.get(key)
        if not isinstance(value, Sequence) or isinstance(value, str | bytes):
            return False
        if value:
            return False
    return True


def _validate_provider_catalog(
    provider: Mapping[str, object],
    *,
    path: str,
    descriptor_paths: set[str],
) -> None:
    issues: list[ValidationIssue] = []
    _validate_target_catalog(
        provider.get("target-catalog"),
        f"{path}.target-catalog",
        descriptor_paths,
        issues,
    )
    if issues:
        raise ContractValidationError(issues)


def _validate_provider_scalars(
    provider: Mapping[str, object],
    path: str,
    issues: list[ValidationIssue],
) -> None:
    required = {
        "provider",
        "provider-version",
        "status",
        "roots",
        "subjects",
        "dependency-edges",
        "tooling-surfaces",
        "descriptors",
        "target-catalog",
        "diagnostics",
    }
    for key in sorted(required - set(provider)):
        issues.append(ValidationIssue(f"{path}.{key}", "is required"))
    provider_id = _required_str(provider, "provider", issues)
    if provider_id not in _PROVIDER_IDS:
        issues.append(ValidationIssue(f"{path}.provider", "is not registered"))
    provider_version = provider.get("provider-version")
    if provider_version is not None and not isinstance(provider_version, str):
        issues.append(
            ValidationIssue(
                f"{path}.provider-version",
                "must be null or a string",
            ),
        )
    status = _required_str(provider, "status", issues)
    if status not in _SNAPSHOT_STATUSES:
        issues.append(ValidationIssue(f"{path}.status", "is not registered"))


def _validate_sorted_string_array(
    obj: Mapping[str, object],
    key: str,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    value = obj.get(key)
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        issues.append(ValidationIssue(path, "must be a string array"))
        return
    try:
        sorted_values = _sorted_unique_strings(value, path)
    except ContractValidationError as error:
        issues.extend(error.issues)
        return
    if list(value) != sorted_values:
        issues.append(ValidationIssue(path, "must be canonical"))


def _validate_dependency_edges(
    value: object,
    path: str,
    issues: list[ValidationIssue],
    *,
    provider_subject_ids: set[str] | None = None,
) -> None:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        issues.append(ValidationIssue(path, "must be an array"))
        return
    edges: list[Mapping[str, object]] = []
    for index, item in enumerate(value):
        item_path = f"{path}[{index}]"
        if not isinstance(item, Mapping):
            issues.append(ValidationIssue(item_path, "must be an object"))
            continue
        edges.append(item)
        from_subject = _required_str(item, "from-subject-id", issues)
        to_subject = _required_str(item, "to-subject-id", issues)
        _required_str(item, "relation", issues)
        if provider_subject_ids is not None:
            if (
                from_subject is not None
                and from_subject not in provider_subject_ids
            ):
                issues.append(
                    ValidationIssue(
                        f"{item_path}.from-subject-id",
                        "must resolve to provider subject",
                    ),
                )
            if (
                to_subject is not None
                and to_subject not in provider_subject_ids
            ):
                issues.append(
                    ValidationIssue(
                        f"{item_path}.to-subject-id",
                        "must resolve to provider subject",
                    ),
                )
        if item.get("relation") not in _DEPENDENCY_RELATIONS:
            issues.append(
                ValidationIssue(f"{item_path}.relation", "is not registered"),
            )
    expected = sorted(
        edges,
        key=lambda item: (
            str(item.get("from-subject-id")),
            str(item.get("to-subject-id")),
            str(item.get("relation")),
        ),
    )
    if edges != expected or len({_edge_key(item) for item in edges}) != len(
        edges
    ):
        issues.append(ValidationIssue(path, "must be canonical and unique"))


def _validate_descriptors(
    value: object,
    path: str,
    descriptor_paths: set[str],
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        issues.append(ValidationIssue(path, "must be an array"))
        return
    descriptors: list[Mapping[str, object]] = []
    for index, item in enumerate(value):
        item_path = f"{path}[{index}]"
        if not isinstance(item, Mapping):
            issues.append(ValidationIssue(item_path, "must be an object"))
            continue
        descriptors.append(item)
        descriptor_path = _required_str(item, "descriptor-path", issues)
        if "descriptor-identity" not in item:
            issues.append(
                ValidationIssue(
                    f"{item_path}.descriptor-identity",
                    "is required",
                )
            )
        else:
            _nullable_str(
                item.get("descriptor-identity"),
                f"{item_path}.descriptor-identity",
                issues,
            )
        _nullable_str(
            item.get("owner-subject-id"),
            f"{item_path}.owner-subject-id",
            issues,
        )
        if item.get("source") not in _DESCRIPTOR_SOURCES:
            issues.append(
                ValidationIssue(f"{item_path}.source", "is not registered"),
            )
        if descriptor_path is not None:
            _validate_repo_relative_git_path(
                descriptor_path,
                f"{item_path}.descriptor-path",
                issues,
            )
            if descriptor_path in descriptor_paths:
                issues.append(
                    ValidationIssue(
                        f"{item_path}.descriptor-path",
                        "must be globally unique",
                    ),
                )
            descriptor_paths.add(descriptor_path)
    expected = sorted(descriptors, key=_descriptor_key)
    if descriptors != expected:
        issues.append(ValidationIssue(path, "must be canonical"))


def _validate_target_catalog(
    value: object,
    path: str,
    descriptor_paths: set[str],
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(value, Mapping):
        issues.append(ValidationIssue(path, "must be an object"))
        return
    _nullable_str(value.get("catalog-id"), f"{path}.catalog-id", issues)
    _validate_sorted_string_array(
        value,
        "descriptor-paths",
        f"{path}.descriptor-paths",
        issues,
    )
    for index, descriptor_path in enumerate(
        _string_items(value.get("descriptor-paths")),
    ):
        _validate_repo_relative_git_path(
            descriptor_path,
            f"{path}.descriptor-paths[{index}]",
            issues,
        )
        if descriptor_path not in descriptor_paths:
            issues.append(
                ValidationIssue(
                    f"{path}.descriptor-paths",
                    "must resolve to descriptor facts",
                ),
            )
    _validate_target_catalog_entries(
        value.get("entries"),
        f"{path}.entries",
        descriptor_paths,
        issues,
    )


def _validate_target_catalog_entries(
    value: object,
    path: str,
    descriptor_paths: set[str],
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        issues.append(ValidationIssue(path, "must be an array"))
        return
    entries: list[Mapping[str, object]] = []
    for index, item in enumerate(value):
        item_path = f"{path}[{index}]"
        if not isinstance(item, Mapping):
            issues.append(ValidationIssue(item_path, "must be an object"))
            continue
        entries.append(item)
        descriptor_path = _required_str(item, "descriptor-path", issues)
        _required_str(item, "profile", issues)
        if (
            descriptor_path is not None
            and descriptor_path not in descriptor_paths
        ):
            issues.append(
                ValidationIssue(
                    f"{item_path}.descriptor-path",
                    "must resolve to descriptor facts",
                ),
            )
        if descriptor_path is not None:
            _validate_repo_relative_git_path(
                descriptor_path,
                f"{item_path}.descriptor-path",
                issues,
            )
        _validate_catalog_artifact(
            item.get("artifact"),
            f"{item_path}.artifact",
            issues,
        )
        _validate_catalog_receipt(
            item.get("release-receipt"),
            f"{item_path}.release-receipt",
            issues,
        )
    has_variant_key_errors = False
    for index, item in enumerate(entries):
        if (
            _target_catalog_entry_variant_keys(
                item,
                f"{path}[{index}]",
                issues,
            )
            is None
        ):
            has_variant_key_errors = True
    if has_variant_key_errors:
        return
    expected = sorted(entries, key=_target_catalog_entry_key)
    if entries != expected or len(
        {_target_catalog_entry_key(item) for item in entries}
    ) != len(entries):
        issues.append(ValidationIssue(path, "must be canonical and unique"))


def _validate_catalog_artifact(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(value, Mapping):
        issues.append(ValidationIssue(path, "must be an object"))
        return
    for key in ("kind-family", "concrete-kind", "logical-artifact-role"):
        _required_str(value, key, issues)
    if not isinstance(value.get("variant-dimensions"), Mapping):
        issues.append(
            ValidationIssue(f"{path}.variant-dimensions", "must be an object"),
        )
    _validate_sorted_string_array(
        value,
        "expected-artifact-refs",
        f"{path}.expected-artifact-refs",
        issues,
    )


def _validate_catalog_receipt(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(value, Mapping):
        issues.append(ValidationIssue(path, "must be an object"))
        return
    for key in ("expected-family", "logical-receipt-role"):
        _required_str(value, key, issues)
    if not isinstance(value.get("variant-dimensions"), Mapping):
        issues.append(
            ValidationIssue(f"{path}.variant-dimensions", "must be an object"),
        )


def _validate_provider_diagnostics(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        issues.append(ValidationIssue(path, "must be an array"))
        return
    diagnostics: list[Mapping[str, object]] = []
    for index, diagnostic in enumerate(value):
        if not isinstance(diagnostic, Mapping):
            issues.append(
                ValidationIssue(f"{path}[{index}]", "must be an object"),
            )
            continue
        diagnostics.append(diagnostic)
        _validate_diagnostic(
            diagnostic,
            f"{path}[{index}]",
            issues,
            allow_work_group_source=True,
            allow_null_message=True,
        )
    expected = sorted(
        diagnostics,
        key=lambda item: str(item.get("diagnostic-id")),
    )
    if diagnostics != expected or len(
        {str(item.get("diagnostic-id")) for item in diagnostics}
    ) != len(diagnostics):
        issues.append(ValidationIssue(path, "must be canonical and unique"))


def _freeze_changed_files_snapshot(
    *,
    request: NormalizedCiValidationRequest,
    created_at: str,
) -> dict[str, object] | None:
    if request.mode == "scheduled_full":
        return None
    affected_range = request.projection["affected-range"]
    if not isinstance(affected_range, Mapping):
        message = "normalized request affected-range must be an object"
        raise TypeError(message)
    if affected_range["status"] != "available":
        return None
    changed_files = affected_range["changed-files"]
    if not isinstance(changed_files, Sequence) or isinstance(
        changed_files,
        str | bytes,
    ):
        message = "normalized changed-files must be a sequence"
        raise TypeError(message)
    payload = _changed_files_hash_payload(changed_files)
    digest = canonical_json_digest(payload)
    return {
        "api-version": API_VERSIONS_BY_KIND[
            CiValidationKind.CHANGED_FILES_SNAPSHOT.value
        ],
        "kind": CiValidationKind.CHANGED_FILES_SNAPSHOT.value,
        "created-at": created_at,
        "repository": _repository(request),
        "run": _run(request),
        "schema-diagnostics": [],
        "artifact-ref": ci_validation_changed_files_snapshot_artifact_ref(
            run_id=request.run_id,
            run_attempt=request.run_attempt,
        ),
        "changed-files-hash": digest,
        "hash-payload": payload,
    }


def _changed_files_hash_payload(
    changed_files: Sequence[str],
) -> dict[str, object]:
    return {
        "api-version": API_VERSIONS_BY_KIND[
            CiValidationKind.CHANGED_FILES_SNAPSHOT.value
        ],
        "changed-files": _sorted_unique_strings(changed_files, "changed-files"),
    }


def _plan_affected_range(
    request: NormalizedCiValidationRequest,
    changed_files_snapshot: Mapping[str, object] | None,
) -> dict[str, object]:
    if request.mode == "scheduled_full":
        return {
            "status": "not-applicable",
            "base-sha": None,
            "base-tip-sha": None,
            "head-sha": None,
            "changed-files-hash": None,
        }
    affected_range = request.projection["affected-range"]
    if not isinstance(affected_range, Mapping):
        message = "normalized request affected-range must be an object"
        raise TypeError(message)
    return {
        "status": affected_range["status"],
        "base-sha": affected_range["base-sha"],
        "base-tip-sha": affected_range["base-tip-sha"],
        "head-sha": affected_range["head-sha"],
        "changed-files-hash": (
            changed_files_snapshot["changed-files-hash"]
            if changed_files_snapshot is not None
            else None
        ),
    }


def _plan_document(  # noqa: PLR0913
    *,
    request: NormalizedCiValidationRequest,
    plan_id: str,
    created_at: str,
    verdict_intent: str,
    affected_range: Mapping[str, object],
    policy_version: str | None,
    observed_commit_sha: str,
    subject_status: str,
    subject_id: str | None,
    fact_status: str,
    fact_id: str | None,
    classification: Mapping[str, object],
    subjects: Sequence[Mapping[str, object]],
    descriptor_obligations: Sequence[Mapping[str, object]],
    validation_obligations: Sequence[Mapping[str, object]],
    artifact_obligations: Sequence[Mapping[str, object]],
    work_groups: Sequence[Mapping[str, object]],
    evidence_expectations: Sequence[Mapping[str, object]],
    detail_profiles: Sequence[Mapping[str, object]],
    diagnostics: Sequence[Mapping[str, object]],
    pull_request_merge_commit_verification: Mapping[str, object] | None,
) -> dict[str, object]:
    return {
        "api-version": API_VERSIONS_BY_KIND[CiValidationKind.PLAN.value],
        "kind": CiValidationKind.PLAN.value,
        "created-at": created_at,
        "repository": _repository(request),
        "run": _run(request),
        "schema-diagnostics": [],
        "plan-id": plan_id,
        "plan-digest": "0" * 64,
        "mode": request.mode,
        "verdict-intent": verdict_intent,
        "validation-tree": _plan_validation_tree(
            request,
            affected_range,
            pull_request_merge_commit_verification,
        ),
        "affected-range": dict(affected_range),
        "request": {
            "artifact-ref": request.artifact_ref,
            "request-digest": request.request_digest,
        },
        "scheduled-full": {"enabled": request.mode == "scheduled_full"},
        "planner": {
            "policy-source": "validation-tree",
            "version": policy_version,
            "execution-tree": {
                "observed-commit-sha": observed_commit_sha,
                "source": "plan-boundary",
                "verified": True,
            },
        },
        "subject-universe": {
            "status": subject_status,
            "id": subject_id,
        },
        "fact-snapshot": {
            "status": fact_status,
            "id": fact_id,
        },
        "classification": dict(classification),
        "subjects": [dict(subject) for subject in subjects],
        "descriptor-obligations": [
            dict(item) for item in descriptor_obligations
        ],
        "validation-obligations": [
            dict(item) for item in validation_obligations
        ],
        "artifact-obligations": [dict(item) for item in artifact_obligations],
        "work-groups": [dict(group) for group in work_groups],
        "evidence-expectations": [dict(item) for item in evidence_expectations],
        "detail-profiles": [dict(item) for item in detail_profiles],
        "diagnostics": [dict(item) for item in diagnostics],
    }


def _repository(request: NormalizedCiValidationRequest) -> dict[str, object]:
    repository = request.document["repository"]
    if not isinstance(repository, Mapping):
        message = "normalized request repository must be an object"
        raise TypeError(message)
    return dict(repository)


def _run(request: NormalizedCiValidationRequest) -> dict[str, object]:
    run = request.document["run"]
    if not isinstance(run, Mapping):
        message = "normalized request run must be an object"
        raise TypeError(message)
    return dict(run)


def _validate_diagnostics(
    diagnostics: Sequence[Mapping[str, object]],
    verdict_intent: str,
) -> None:
    has_fail_closed = False
    issues: list[ValidationIssue] = []
    for index, diagnostic in enumerate(diagnostics):
        path = f"diagnostics[{index}]"
        _validate_diagnostic(diagnostic, path, issues)
        if (
            diagnostic.get("verdict-effect")
            == DiagnosticVerdictEffect.FAIL_CLOSED.value
        ):
            has_fail_closed = True
    if issues:
        raise ContractValidationError(issues)
    if verdict_intent == "fail-closed" and not has_fail_closed:
        raise ContractValidationError(
            [
                ValidationIssue(
                    "diagnostics",
                    "fail-closed plans require a fail-closed diagnostic",
                ),
            ],
        )
    if verdict_intent == "executable" and has_fail_closed:
        raise ContractValidationError(
            [
                ValidationIssue(
                    "diagnostics",
                    "executable plans must not contain fail-closed diagnostics",
                ),
            ],
        )


def _validate_unavailable_range_diagnostics_for_request(
    request: NormalizedCiValidationRequest,
    diagnostics: Sequence[Mapping[str, object]],
) -> None:
    expected_detail = _request_range_unconfirmed_detail(request)
    if expected_detail is None:
        return
    issues: list[ValidationIssue] = []
    _validate_unavailable_range_diagnostics(
        diagnostics,
        issues,
        expected_detail=expected_detail,
        path="diagnostics",
    )
    if issues:
        raise ContractValidationError(issues)


def _validate_unavailable_range_diagnostics_for_plan(
    plan: Mapping[str, object],
    diagnostics: Sequence[Mapping[str, object]],
    issues: list[ValidationIssue],
) -> None:
    affected_range = plan.get("affected-range")
    if not isinstance(affected_range, Mapping):
        return
    if affected_range.get("status") != "unavailable":
        return
    _validate_unavailable_range_diagnostics(
        diagnostics,
        issues,
        expected_detail=None,
        path="$.diagnostics",
    )


def _validate_unavailable_range_diagnostics(
    diagnostics: Sequence[Mapping[str, object]],
    issues: list[ValidationIssue],
    *,
    expected_detail: object | None,
    path: str,
) -> None:
    has_range_unconfirmed = False
    has_matching_detail = False
    has_any_detail = False
    for diagnostic in diagnostics:
        if diagnostic.get("code") != DiagnosticFamily.RANGE_UNCONFIRMED.value:
            continue
        has_range_unconfirmed = True
        detail = diagnostic.get("detail")
        if detail is not None:
            has_any_detail = True
        if expected_detail is None:
            has_matching_detail = detail is not None
        elif detail == expected_detail:
            has_matching_detail = True
    if not has_range_unconfirmed:
        issues.append(
            ValidationIssue(
                path,
                "unavailable ranges require a range-unconfirmed diagnostic",
            ),
        )
        return
    if expected_detail is None and not has_any_detail:
        issues.append(
            ValidationIssue(
                path,
                "range-unconfirmed diagnostic detail is required",
            ),
        )
        return
    if not has_matching_detail:
        issues.append(
            ValidationIssue(
                path,
                "range-unconfirmed diagnostic detail must match "
                "affected-range.diagnostic-detail",
            ),
        )


def _validate_diagnostic(
    diagnostic: Mapping[str, object],
    path: str,
    issues: list[ValidationIssue],
    *,
    allow_work_group_source: bool = False,
    allow_null_message: bool = False,
) -> None:
    diagnostic_id = diagnostic.get("diagnostic-id")
    if not isinstance(diagnostic_id, str) or diagnostic_id == "":
        issues.append(ValidationIssue(f"{path}.diagnostic-id", "is required"))
    code = diagnostic.get("code")
    if code not in REGISTERED_CI_VALIDATION_DIAGNOSTIC_CODES:
        issues.append(ValidationIssue(f"{path}.code", "is not registered"))
    _validate_diagnostic_detail(diagnostic.get("detail"), code, path, issues)
    message = diagnostic.get("message")
    if allow_null_message and "message" in diagnostic and message is None:
        pass
    elif not isinstance(message, str) or message == "":
        issues.append(ValidationIssue(f"{path}.message", "must be non-empty"))
    _validate_diagnostic_source(
        diagnostic.get("source"),
        path,
        issues,
        allow_work_group_source=allow_work_group_source,
    )
    severity = diagnostic.get("severity")
    severities = {
        item.value for item in DiagnosticSeverity.__members__.values()
    }
    if severity not in severities:
        issues.append(ValidationIssue(f"{path}.severity", "is not registered"))
    verdict_effect = diagnostic.get("verdict-effect")
    verdict_effects = {
        item.value for item in DiagnosticVerdictEffect.__members__.values()
    }
    if verdict_effect not in verdict_effects:
        issues.append(
            ValidationIssue(f"{path}.verdict-effect", "is not registered"),
        )


def _validate_diagnostic_source(
    source: object,
    path: str,
    issues: list[ValidationIssue],
    *,
    allow_work_group_source: bool,
) -> None:
    if not isinstance(source, Mapping):
        issues.append(ValidationIssue(f"{path}.source", "must be an object"))
        return
    allowed_sources = (
        _DIAGNOSTIC_SOURCES
        if allow_work_group_source
        else _PLANNER_DIAGNOSTIC_SOURCES
    )
    source_type = source.get("type")
    if source_type not in allowed_sources:
        issues.append(
            ValidationIssue(f"{path}.source.type", "is not registered"),
        )
    if "id" not in source:
        issues.append(ValidationIssue(f"{path}.source.id", "is required"))
        return
    source_id = source.get("id")
    if source_id is not None and (
        not isinstance(source_id, str) or source_id == ""
    ):
        issues.append(
            ValidationIssue(
                f"{path}.source.id",
                "must be null or a non-empty string",
            ),
        )


def _validate_diagnostic_detail(
    detail: object,
    code: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if detail is None:
        return
    if detail not in REGISTERED_CI_VALIDATION_DIAGNOSTIC_DETAILS:
        issues.append(ValidationIssue(f"{path}.detail", "is not registered"))
        return
    if isinstance(code, str) and detail not in (
        DETAILS_BY_DIAGNOSTIC_CODE.get(code, frozenset())
    ):
        issues.append(
            ValidationIssue(
                f"{path}.detail",
                "is not valid for this diagnostic code",
            ),
        )


_PLAN_ROOT_KEYS = frozenset(
    {
        "api-version",
        "kind",
        "created-at",
        "repository",
        "run",
        "schema-diagnostics",
        "plan-id",
        "plan-digest",
        "mode",
        "verdict-intent",
        "validation-tree",
        "affected-range",
        "request",
        "scheduled-full",
        "planner",
        "subject-universe",
        "fact-snapshot",
        "classification",
        "subjects",
        "descriptor-obligations",
        "validation-obligations",
        "artifact-obligations",
        "work-groups",
        "evidence-expectations",
        "detail-profiles",
        "diagnostics",
    },
)


_PLAN_REQUIRED_KEYS = frozenset(
    {
        "plan-id",
        "plan-digest",
        "mode",
        "verdict-intent",
        "validation-tree",
        "affected-range",
        "request",
        "scheduled-full",
        "planner",
        "subject-universe",
        "fact-snapshot",
        "classification",
        "subjects",
        "descriptor-obligations",
        "validation-obligations",
        "artifact-obligations",
        "work-groups",
        "evidence-expectations",
        "detail-profiles",
        "diagnostics",
    },
)


_CHANGED_FILES_SNAPSHOT_ROOT_KEYS = frozenset(
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


_FACT_SNAPSHOT_ROOT_KEYS = frozenset(
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


def _validate_allowed_root_members(
    value: Mapping[str, object],
    allowed: frozenset[str],
    path: str,
    issues: list[ValidationIssue],
) -> None:
    for key in sorted(
        key for key in value if isinstance(key, str) and key not in allowed
    ):
        issues.append(ValidationIssue(f"{path}.{key}", "is not allowed"))


def _validate_required_plan_members(
    plan: Mapping[str, object],
    issues: list[ValidationIssue],
) -> None:
    required = _PLAN_REQUIRED_KEYS
    for key in sorted(required - set(plan)):
        issues.append(ValidationIssue(f"$.{key}", "is required"))
    _validate_allowed_root_members(plan, _PLAN_ROOT_KEYS, "$", issues)


def _validate_plan_envelope_runtime(
    plan: Mapping[str, object],
    issues: list[ValidationIssue],
    *,
    pull_request_merge_commit_verification: Mapping[str, object] | None,
) -> None:
    mode = plan.get("mode")
    if mode not in _PLAN_MODES:
        issues.append(ValidationIssue("$.mode", "is not registered"))
    _validate_plan_id_value(plan.get("plan-id"), "$.plan-id", issues)
    validation_tree = _mapping_or_issue(
        plan.get("validation-tree"),
        "$.validation-tree",
        issues,
    )
    tree_sha = None
    if validation_tree is not None:
        tree_sha = _required_sha(
            validation_tree,
            "commit-sha",
            "$.validation-tree.commit-sha",
            issues,
        )
        ref = validation_tree.get("ref")
        if ref is not None and not isinstance(ref, str):
            issues.append(
                ValidationIssue(
                    "$.validation-tree.ref",
                    "must be null or string",
                ),
            )
    _validate_plan_request_binding(plan.get("request"), plan.get("run"), issues)
    _validate_plan_mode_range(
        mode,
        plan.get("scheduled-full"),
        plan.get("affected-range"),
        validation_tree,
        issues,
        pull_request_merge_commit_verification=(
            pull_request_merge_commit_verification
        ),
    )
    _validate_planner_runtime(plan.get("planner"), tree_sha, issues)
    subjects = _sequence_or_issue(plan.get("subjects"), "$.subjects", issues)
    if subjects is not None:
        try:
            _validate_subjects(subjects)
        except ContractValidationError as error:
            issues.extend(error.issues)
    _validate_subject_universe(
        plan.get("subject-universe"),
        subjects,
        issues,
    )
    _validate_fact_snapshot_envelope(plan.get("fact-snapshot"), issues)
    _validate_plan_status_invariants(plan, issues)


def _validate_plan_request_binding(
    value: object,
    run: object,
    issues: list[ValidationIssue],
) -> None:
    request = _mapping_or_issue(value, "$.request", issues)
    if request is None:
        return
    artifact_ref = request.get("artifact-ref")
    try:
        validate_artifact_logical_ref(artifact_ref)
    except ContractValidationError as error:
        issues.extend(error.issues)
    run_mapping = _mapping_or_issue(run, "$.run", issues)
    if run_mapping is not None:
        run_id = run_mapping.get("run-id")
        run_attempt = run_mapping.get("run-attempt")
        if isinstance(run_id, str) and isinstance(run_attempt, str):
            expected_ref = ci_validation_request_artifact_ref(
                run_id=run_id,
                run_attempt=run_attempt,
            )
            if artifact_ref != expected_ref:
                issues.append(
                    ValidationIssue(
                        "$.request.artifact-ref",
                        "must match plan run identity",
                    ),
                )
    _required_digest(
        request,
        "request-digest",
        "$.request.request-digest",
        issues,
    )


def _validate_plan_mode_range(  # noqa: PLR0913
    mode: object,
    scheduled_full: object,
    affected_range: object,
    validation_tree: Mapping[str, object] | None,
    issues: list[ValidationIssue],
    *,
    pull_request_merge_commit_verification: Mapping[str, object] | None,
) -> None:
    scheduled = _mapping_or_issue(
        scheduled_full,
        "$.scheduled-full",
        issues,
    )
    affected = _mapping_or_issue(
        affected_range,
        "$.affected-range",
        issues,
    )
    if scheduled is None or affected is None:
        return
    enabled = scheduled.get("enabled")
    if not isinstance(enabled, bool):
        issues.append(
            ValidationIssue("$.scheduled-full.enabled", "must be bool"),
        )
    if mode == "scheduled_full":
        if enabled is not True:
            issues.append(
                ValidationIssue("$.scheduled-full.enabled", "must be true"),
            )
        _validate_merge_commit_absent_for_non_pr_mode(
            mode,
            validation_tree,
            issues,
        )
        _validate_scheduled_full_range(affected, issues)
        return
    if mode in {"pull_request", "push"}:
        if enabled is not False:
            issues.append(
                ValidationIssue("$.scheduled-full.enabled", "must be false"),
            )
        tree_sha = None
        if validation_tree is not None:
            candidate_tree_sha = validation_tree.get("commit-sha")
            if isinstance(candidate_tree_sha, str):
                tree_sha = candidate_tree_sha
        if mode == "push":
            _validate_merge_commit_absent_for_non_pr_mode(
                mode,
                validation_tree,
                issues,
            )
        _validate_affected_mode_range(
            mode,
            affected,
            validation_tree,
            tree_sha,
            issues,
            pull_request_merge_commit_verification=(
                pull_request_merge_commit_verification
            ),
        )


def _validate_merge_commit_absent_for_non_pr_mode(
    mode: object,
    validation_tree: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> None:
    if (
        isinstance(validation_tree, Mapping)
        and "merge-commit" in validation_tree
    ):
        issues.append(
            ValidationIssue(
                "$.validation-tree.merge-commit",
                f"must be absent for {mode}",
            ),
        )


def _validate_scheduled_full_range(
    affected: Mapping[str, object],
    issues: list[ValidationIssue],
) -> None:
    if affected.get("status") != "not-applicable":
        issues.append(
            ValidationIssue(
                "$.affected-range.status",
                "must be not-applicable",
            ),
        )
    for key in ("base-sha", "base-tip-sha", "head-sha", "changed-files-hash"):
        if affected.get(key) is not None:
            issues.append(
                ValidationIssue(f"$.affected-range.{key}", "must be null"),
            )


def _validate_affected_mode_range(  # noqa: PLR0913
    mode: object,
    affected: Mapping[str, object],
    validation_tree: Mapping[str, object] | None,
    tree_sha: str | None,
    issues: list[ValidationIssue],
    *,
    pull_request_merge_commit_verification: Mapping[str, object] | None,
) -> None:
    status = affected.get("status")
    if status not in _AFFECTED_STATUSES:
        _validate_pull_request_merge_commit_requires_available_range(
            mode,
            status,
            validation_tree,
            issues,
        )
        issues.append(ValidationIssue("$.affected-range.status", "is invalid"))
        return
    _validate_pull_request_merge_commit_requires_available_range(
        mode,
        status,
        validation_tree,
        issues,
    )
    if status == "available":
        _required_sha(
            affected,
            "base-sha",
            "$.affected-range.base-sha",
            issues,
        )
        _required_sha(
            affected,
            "head-sha",
            "$.affected-range.head-sha",
            issues,
        )
        base_tip = affected.get("base-tip-sha")
        if base_tip is not None and (
            not isinstance(base_tip, str) or _SHA_RE.fullmatch(base_tip) is None
        ):
            issues.append(
                ValidationIssue("$.affected-range.base-tip-sha", "must be sha"),
            )
        if mode == "pull_request":
            _validate_pull_request_affected_boundary(
                affected,
                base_tip,
                validation_tree,
                tree_sha,
                issues,
                pull_request_merge_commit_verification=(
                    pull_request_merge_commit_verification
                ),
            )
        if mode == "push":
            _validate_push_affected_boundary(
                affected,
                base_tip,
                tree_sha,
                issues,
            )
        _required_digest(
            affected,
            "changed-files-hash",
            "$.affected-range.changed-files-hash",
            issues,
        )
        return
    for key in ("base-sha", "base-tip-sha", "head-sha"):
        _nullable_sha(affected, key, f"$.affected-range.{key}", issues)
    if mode == "push" and affected.get("base-tip-sha") is not None:
        issues.append(
            ValidationIssue(
                "$.affected-range.base-tip-sha",
                "must be null for push",
            ),
        )
    if affected.get("changed-files-hash") is not None:
        issues.append(
            ValidationIssue(
                "$.affected-range.changed-files-hash",
                "must be null",
            ),
        )


def _validate_pull_request_affected_boundary(  # noqa: PLR0913
    affected: Mapping[str, object],
    base_tip: object,
    validation_tree: Mapping[str, object] | None,
    tree_sha: str | None,
    issues: list[ValidationIssue],
    *,
    pull_request_merge_commit_verification: Mapping[str, object] | None,
) -> None:
    if base_tip is None:
        issues.append(
            ValidationIssue(
                "$.affected-range.base-tip-sha",
                "is required for pull_request",
            ),
        )
    head_sha = affected.get("head-sha")
    if (
        isinstance(head_sha, str)
        and tree_sha is not None
        and tree_sha == head_sha
        and isinstance(validation_tree, Mapping)
        and "merge-commit" in validation_tree
    ):
        issues.append(
            ValidationIssue(
                "$.validation-tree.merge-commit",
                "must be absent when pull_request validation tree is head-sha",
            ),
        )
    if (
        isinstance(validation_tree, Mapping)
        and "merge-commit" in validation_tree
        and (not isinstance(head_sha, str) or tree_sha is None)
    ):
        issues.append(
            ValidationIssue(
                "$.validation-tree.merge-commit",
                "requires a valid pull_request head and validation tree commit",
            ),
        )
    if (
        isinstance(head_sha, str)
        and tree_sha is not None
        and tree_sha != head_sha
        and not _has_verified_pull_request_merge_commit(
            validation_tree,
            pull_request_merge_commit_verification,
            tree_sha,
            base_tip,
            head_sha,
            issues,
        )
    ):
        issues.append(
            ValidationIssue(
                "$.validation-tree.commit-sha",
                "must match affected-range.head-sha or verified merge "
                "commit for pull_request",
            ),
        )


def _validate_pull_request_merge_commit_requires_available_range(
    mode: object,
    status: object,
    validation_tree: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> None:
    if (
        mode == "pull_request"
        and status != "available"
        and isinstance(validation_tree, Mapping)
        and "merge-commit" in validation_tree
    ):
        issues.append(
            ValidationIssue(
                "$.validation-tree.merge-commit",
                "must be absent unless pull_request affected range is "
                "available",
            ),
        )


def _has_verified_pull_request_merge_commit(  # noqa: C901, PLR0912, PLR0913
    validation_tree: Mapping[str, object] | None,
    verification: Mapping[str, object] | None,
    tree_sha: str,
    base_tip: object,
    head_sha: str,
    issues: list[ValidationIssue],
) -> bool:
    if validation_tree is None:
        return False
    merge_commit = validation_tree.get("merge-commit")
    if not isinstance(merge_commit, Mapping):
        return False
    if verification is None:
        issues.append(
            ValidationIssue(
                "$.validation-tree.merge-commit",
                "requires trusted pull-request merge commit verification",
            ),
        )
        return False
    verification_issues: list[ValidationIssue] = []
    _validate_pull_request_merge_commit_verification(
        verification,
        validation_tree,
        tree_sha,
        base_tip,
        head_sha,
        verification_issues,
        "pull-request-merge-commit-verification",
    )
    verified = merge_commit.get("verified")
    commit_sha = merge_commit.get("commit-sha")
    merge_base_tip = merge_commit.get("base-tip-sha")
    merge_head = merge_commit.get("head-sha")
    merge_ref = merge_commit.get("ref")
    verification_source = merge_commit.get("verification-source")
    ref = validation_tree.get("ref")

    valid = not verification_issues
    issues.extend(verification_issues)
    allowed_keys = {
        "commit-sha",
        "base-tip-sha",
        "head-sha",
        "ref",
        "verified",
        "verification-source",
    }
    for key in sorted(set(merge_commit) - allowed_keys):
        issues.append(
            ValidationIssue(
                f"$.validation-tree.merge-commit.{key}",
                "is not allowed",
            ),
        )
        valid = False
    if verified is not True:
        issues.append(
            ValidationIssue(
                "$.validation-tree.merge-commit.verified",
                "must be true",
            ),
        )
        valid = False
    if verification_source != _PR_MERGE_VERIFICATION_SOURCE:
        issues.append(
            ValidationIssue(
                "$.validation-tree.merge-commit.verification-source",
                f"must be {_PR_MERGE_VERIFICATION_SOURCE}",
            ),
        )
        valid = False
    if commit_sha != tree_sha:
        issues.append(
            ValidationIssue(
                "$.validation-tree.merge-commit.commit-sha",
                "must match validation-tree.commit-sha",
            ),
        )
        valid = False
    if merge_base_tip != base_tip:
        issues.append(
            ValidationIssue(
                "$.validation-tree.merge-commit.base-tip-sha",
                "must match affected-range.base-tip-sha",
            ),
        )
        valid = False
    if merge_head != head_sha:
        issues.append(
            ValidationIssue(
                "$.validation-tree.merge-commit.head-sha",
                "must match affected-range.head-sha",
            ),
        )
        valid = False
    if merge_ref != ref:
        issues.append(
            ValidationIssue(
                "$.validation-tree.merge-commit.ref",
                "must match validation-tree.ref",
            ),
        )
        valid = False
    if not _is_pull_request_merge_ref(merge_ref):
        issues.append(
            ValidationIssue(
                "$.validation-tree.merge-commit.ref",
                "must be a pull request merge ref",
            ),
        )
        valid = False
    if not _is_pull_request_merge_ref(ref):
        issues.append(
            ValidationIssue(
                "$.validation-tree.ref",
                "must be a pull request merge ref",
            ),
        )
        valid = False
    for key in ("commit-sha", "base-tip-sha", "head-sha", "ref"):
        if merge_commit.get(key) != verification.get(key):
            issues.append(
                ValidationIssue(
                    f"$.validation-tree.merge-commit.{key}",
                    "must match trusted merge verification",
                ),
            )
            valid = False
    return valid


def _is_pull_request_merge_ref(ref: object) -> bool:
    return (
        isinstance(ref, str)
        and ref.startswith("refs/pull/")
        and ref.endswith("/merge")
    )


def _validate_push_affected_boundary(
    affected: Mapping[str, object],
    base_tip: object,
    tree_sha: str | None,
    issues: list[ValidationIssue],
) -> None:
    if base_tip is not None:
        issues.append(
            ValidationIssue(
                "$.affected-range.base-tip-sha",
                "must be null for push",
            ),
        )
    head_sha = affected.get("head-sha")
    if (
        isinstance(head_sha, str)
        and tree_sha is not None
        and tree_sha != head_sha
    ):
        issues.append(
            ValidationIssue(
                "$.validation-tree.commit-sha",
                "must match affected-range.head-sha for push",
            ),
        )


def _validate_planner_runtime(
    value: object,
    tree_sha: str | None,
    issues: list[ValidationIssue],
) -> None:
    planner = _mapping_or_issue(value, "$.planner", issues)
    if planner is None:
        return
    if planner.get("policy-source") != "validation-tree":
        issues.append(
            ValidationIssue(
                "$.planner.policy-source",
                "must be validation-tree",
            ),
        )
    version = planner.get("version")
    if version is not None and not isinstance(version, str):
        issues.append(
            ValidationIssue("$.planner.version", "must be null or string"),
        )
    execution_tree = _mapping_or_issue(
        planner.get("execution-tree"),
        "$.planner.execution-tree",
        issues,
    )
    if execution_tree is None:
        return
    if execution_tree.get("observed-commit-sha") != tree_sha:
        issues.append(
            ValidationIssue(
                "$.planner.execution-tree.observed-commit-sha",
                "must match validation-tree.commit-sha",
            ),
        )
    if execution_tree.get("source") != "plan-boundary":
        issues.append(
            ValidationIssue(
                "$.planner.execution-tree.source",
                "must be plan-boundary",
            ),
        )
    if execution_tree.get("verified") is not True:
        issues.append(
            ValidationIssue(
                "$.planner.execution-tree.verified",
                "must be true",
            ),
        )


def _validate_subject_universe(
    value: object,
    subjects: Sequence[Mapping[str, object]] | None,
    issues: list[ValidationIssue],
) -> None:
    subject_universe = _mapping_or_issue(value, "$.subject-universe", issues)
    if subject_universe is None:
        return
    status = subject_universe.get("status")
    subject_id = subject_universe.get("id")
    if set(subject_universe) != {"status", "id"}:
        issues.append(
            ValidationIssue(
                "$.subject-universe",
                "must only contain status and id",
            ),
        )
    if status not in _SNAPSHOT_STATUSES:
        issues.append(
            ValidationIssue("$.subject-universe.status", "is invalid"),
        )
        return
    if status == "available":
        _validate_available_subject_universe(
            subject_id=subject_id,
            subjects=subjects,
            issues=issues,
        )
        return
    if subject_id is not None:
        issues.append(ValidationIssue("$.subject-universe.id", "must be null"))
    if subjects:
        issues.append(
            ValidationIssue(
                "$.subjects",
                "must be empty when subject-universe is unavailable",
            ),
        )


def _validate_available_subject_universe(
    *,
    subject_id: object,
    subjects: Sequence[Mapping[str, object]] | None,
    issues: list[ValidationIssue],
) -> None:
    if subjects is None:
        return
    try:
        expected_id = ci_validation_subject_universe_id(subjects)
    except ContractValidationError as error:
        issues.extend(error.issues)
        return
    except (TypeError, ValueError) as error:
        issues.append(
            ValidationIssue(
                "$.subject-universe.id",
                f"cannot canonicalize subjects: {error}",
            ),
        )
        return
    if subject_id != expected_id:
        issues.append(
            ValidationIssue(
                "$.subject-universe.id",
                "does not match subjects",
            ),
        )


def _validate_fact_snapshot_envelope(
    value: object,
    issues: list[ValidationIssue],
) -> None:
    fact_snapshot = _mapping_or_issue(value, "$.fact-snapshot", issues)
    if fact_snapshot is None:
        return
    status = fact_snapshot.get("status")
    snapshot_id = fact_snapshot.get("id")
    if set(fact_snapshot) != {"status", "id"}:
        issues.append(
            ValidationIssue(
                "$.fact-snapshot",
                "must only contain status and id",
            ),
        )
    if status not in _SNAPSHOT_STATUSES:
        issues.append(ValidationIssue("$.fact-snapshot.status", "is invalid"))
        return
    if status == "available":
        if (
            not isinstance(snapshot_id, str)
            or _DIGEST_RE.fullmatch(snapshot_id) is None
        ):
            issues.append(
                ValidationIssue(
                    "$.fact-snapshot.id",
                    "must be a sha256 digest",
                ),
            )
        return
    if snapshot_id is not None:
        issues.append(ValidationIssue("$.fact-snapshot.id", "must be null"))


def _validate_companion_fact_snapshot(  # noqa: C901, PLR0911, PLR0912, PLR0915
    plan: Mapping[str, object],
    fact_snapshot: Mapping[str, object] | None,
    issues: list[ValidationIssue],
    *,
    bind_provider_identity: bool = True,
) -> None:
    fact_envelope = plan.get("fact-snapshot")
    if not isinstance(fact_envelope, Mapping):
        return
    status = fact_envelope.get("status")
    snapshot_id = fact_envelope.get("id")
    if status == "unavailable":
        if fact_snapshot is not None:
            issues.append(
                ValidationIssue(
                    "$.fact-snapshot",
                    "must not have a companion when unavailable",
                ),
            )
        return
    if status != "available":
        return
    if fact_snapshot is None:
        issues.append(
            ValidationIssue("$.fact-snapshot", "companion is required"),
        )
        return
    provenance_issue_count = len(issues)
    _validate_allowed_root_members(
        fact_snapshot,
        _FACT_SNAPSHOT_ROOT_KEYS,
        "$.fact-snapshot",
        issues,
    )
    try:
        envelope = validate_common_envelope(
            fact_snapshot,
            api_version=API_VERSIONS_BY_KIND[
                CiValidationKind.FACT_SNAPSHOT.value
            ],
            kind=CiValidationKind.FACT_SNAPSHOT,
        )
    except ContractValidationError as error:
        issues.extend(error.issues)
        envelope = None
    if envelope is not None:
        plan_run_identity = _validate_companion_envelope_matches_plan(
            envelope,
            plan,
            "$.fact-snapshot",
            issues,
        )
        if plan_run_identity is not None:
            plan_run_id, plan_run_attempt = plan_run_identity
            expected_ref = ci_validation_fact_snapshot_artifact_ref(
                run_id=plan_run_id,
                run_attempt=plan_run_attempt,
            )
            if fact_snapshot.get("artifact-ref") != expected_ref:
                issues.append(
                    ValidationIssue(
                        "$.fact-snapshot.artifact-ref",
                        "must match plan run identity",
                    ),
                )
    _validate_plan_id_value(
        fact_snapshot.get("plan-id"),
        "$.fact-snapshot.plan-id",
        issues,
    )
    if fact_snapshot.get("plan-id") != plan.get("plan-id"):
        issues.append(
            ValidationIssue("$.fact-snapshot.plan-id", "must match plan"),
        )
    if not bind_provider_identity:
        return
    if len(issues) != provenance_issue_count:
        return
    fact_snapshot_id = fact_snapshot.get("fact-snapshot-id")
    if (
        not isinstance(fact_snapshot_id, str)
        or _DIGEST_RE.fullmatch(fact_snapshot_id) is None
    ):
        issues.append(
            ValidationIssue(
                "$.fact-snapshot.fact-snapshot-id",
                "must be a sha256 digest",
            )
        )
        return
    providers_value = fact_snapshot.get("providers")
    if not isinstance(providers_value, Sequence) or isinstance(
        providers_value,
        str | bytes,
    ):
        issues.append(
            ValidationIssue("$.fact-snapshot.providers", "must be array"),
        )
        return
    providers: list[Mapping[str, object]] = []
    for index, provider in enumerate(providers_value):
        if not isinstance(provider, Mapping):
            issues.append(
                ValidationIssue(
                    f"$.fact-snapshot.providers[{index}]",
                    "must be object",
                ),
            )
            return
        providers.append(provider)
    if not providers:
        issues.append(
            ValidationIssue("$.fact-snapshot.providers", "is required")
        )
        return
    try:
        frozen_providers = _freeze_fact_snapshot_providers(providers)
    except ContractValidationError as error:
        issues.extend(error.issues)
        return
    except (TypeError, ValueError) as error:
        issues.append(
            ValidationIssue(
                "$.fact-snapshot.providers",
                f"cannot canonicalize fact snapshot: {error}",
            ),
        )
        return
    if plan.get("verdict-intent") == "executable":
        try:
            _validate_no_unavailable_fact_providers(
                {"providers": frozen_providers},
                "$.fact-snapshot.providers",
            )
        except ContractValidationError as error:
            issues.extend(error.issues)
    if providers != frozen_providers:
        issues.append(
            ValidationIssue(
                "$.fact-snapshot.providers",
                "must be in canonical order",
            ),
        )
        return
    try:
        expected_id = canonical_json_digest(
            {
                "api-version": API_VERSIONS_BY_KIND[
                    CiValidationKind.FACT_SNAPSHOT.value
                ],
                "kind": CiValidationKind.FACT_SNAPSHOT.value,
                "providers": frozen_providers,
            },
        )
    except (TypeError, ValueError) as error:
        issues.append(
            ValidationIssue(
                "$.fact-snapshot.providers",
                f"cannot canonicalize fact snapshot: {error}",
            ),
        )
        return
    if fact_snapshot_id != expected_id:
        issues.append(
            ValidationIssue(
                "$.fact-snapshot.fact-snapshot-id",
                "does not match providers",
            ),
        )
    if snapshot_id != fact_snapshot_id:
        issues.append(
            ValidationIssue(
                "$.fact-snapshot.id",
                "must match companion fact snapshot",
            ),
        )


def _validate_companion_envelope_matches_plan(
    envelope: CommonEnvelope,
    plan: Mapping[str, object],
    path: str,
    issues: list[ValidationIssue],
) -> tuple[str, str] | None:
    plan_repository = plan.get("repository")
    if isinstance(plan_repository, Mapping):
        if envelope.repository_owner != plan_repository.get("owner"):
            issues.append(
                ValidationIssue(
                    f"{path}.repository.owner",
                    "must match plan",
                ),
            )
        if envelope.repository_name != plan_repository.get("name"):
            issues.append(
                ValidationIssue(
                    f"{path}.repository.name",
                    "must match plan",
                ),
            )
    plan_run = plan.get("run")
    if not isinstance(plan_run, Mapping):
        return None
    if envelope.workflow != plan_run.get("workflow"):
        issues.append(
            ValidationIssue(
                f"{path}.run.workflow",
                "must match plan",
            ),
        )
    plan_run_id = plan_run.get("run-id")
    plan_run_attempt = plan_run.get("run-attempt")
    if envelope.run_id != plan_run_id:
        issues.append(
            ValidationIssue(
                f"{path}.run.run-id",
                "must match plan",
            ),
        )
    if envelope.run_attempt != plan_run_attempt:
        issues.append(
            ValidationIssue(
                f"{path}.run.run-attempt",
                "must match plan",
            ),
        )
    if isinstance(plan_run_id, str) and isinstance(plan_run_attempt, str):
        return plan_run_id, plan_run_attempt
    return None


def _validate_companion_changed_files_snapshot(  # noqa: C901, PLR0911, PLR0912
    plan: Mapping[str, object],
    changed_files_snapshot: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> None:
    affected_range = plan.get("affected-range")
    if not isinstance(affected_range, Mapping):
        return
    plan_hash = affected_range.get("changed-files-hash")
    if plan_hash is None:
        if changed_files_snapshot is not None:
            issues.append(
                ValidationIssue(
                    "$.changed-files-snapshot",
                    "must not have a companion when unavailable",
                ),
            )
        return
    if changed_files_snapshot is None:
        issues.append(
            ValidationIssue(
                "$.changed-files-snapshot",
                "companion is required",
            ),
        )
        return
    provenance_issue_count = len(issues)
    _validate_allowed_root_members(
        changed_files_snapshot,
        _CHANGED_FILES_SNAPSHOT_ROOT_KEYS,
        "$.changed-files-snapshot",
        issues,
    )
    try:
        envelope = validate_common_envelope(
            changed_files_snapshot,
            api_version=API_VERSIONS_BY_KIND[
                CiValidationKind.CHANGED_FILES_SNAPSHOT.value
            ],
            kind=CiValidationKind.CHANGED_FILES_SNAPSHOT,
        )
    except ContractValidationError as error:
        issues.extend(error.issues)
        envelope = None
    if envelope is not None:
        plan_run_identity = _validate_companion_envelope_matches_plan(
            envelope,
            plan,
            "$.changed-files-snapshot",
            issues,
        )
        if plan_run_identity is not None:
            plan_run_id, plan_run_attempt = plan_run_identity
            expected_ref = ci_validation_changed_files_snapshot_artifact_ref(
                run_id=plan_run_id,
                run_attempt=plan_run_attempt,
            )
            if changed_files_snapshot.get("artifact-ref") != expected_ref:
                issues.append(
                    ValidationIssue(
                        "$.changed-files-snapshot.artifact-ref",
                        "must match plan run identity",
                    ),
                )
    if len(issues) != provenance_issue_count:
        return
    snapshot_hash = changed_files_snapshot.get("changed-files-hash")
    if snapshot_hash != plan_hash:
        issues.append(
            ValidationIssue(
                "$.changed-files-snapshot.changed-files-hash",
                "must match plan",
            ),
        )
    payload = changed_files_snapshot.get("hash-payload")
    if not isinstance(payload, Mapping):
        issues.append(
            ValidationIssue(
                "$.changed-files-snapshot.hash-payload",
                "must be object",
            ),
        )
        return
    changed_files = payload.get("changed-files")
    if not isinstance(changed_files, Sequence) or isinstance(
        changed_files,
        str | bytes,
    ):
        issues.append(
            ValidationIssue(
                "$.changed-files-snapshot.hash-payload.changed-files",
                "must be string array",
            ),
        )
        return
    for index, path in enumerate(changed_files):
        _validate_repo_relative_git_path(
            path,
            f"$.changed-files-snapshot.hash-payload.changed-files[{index}]",
            issues,
        )
    if len(issues) != provenance_issue_count:
        return
    try:
        expected_payload = _changed_files_hash_payload(
            [str(path) for path in changed_files],
        )
    except ContractValidationError as error:
        issues.extend(error.issues)
        return
    if payload != expected_payload:
        issues.append(
            ValidationIssue(
                "$.changed-files-snapshot.hash-payload",
                "must be canonical",
            ),
        )
    expected_hash = canonical_json_digest(expected_payload)
    if snapshot_hash != expected_hash:
        issues.append(
            ValidationIssue(
                "$.changed-files-snapshot.changed-files-hash",
                "does not match changed files",
            ),
        )


def _validate_plan_status_invariants(
    plan: Mapping[str, object],
    issues: list[ValidationIssue],
) -> None:
    verdict_intent = plan.get("verdict-intent")
    affected_range = plan.get("affected-range")
    if (
        isinstance(affected_range, Mapping)
        and affected_range.get("status") == "unavailable"
        and verdict_intent != "fail-closed"
    ):
        issues.append(
            ValidationIssue(
                "$.affected-range.status",
                "unavailable ranges require fail-closed intent",
            ),
        )
    subject_status = _snapshot_status(plan, "subject-universe")
    fact_status = _snapshot_status(plan, "fact-snapshot")
    if verdict_intent == "executable" and subject_status != "available":
        issues.append(
            ValidationIssue(
                "$.subject-universe.status",
                "must be available for executable plans",
            ),
        )
    if verdict_intent == "executable" and fact_status != "available":
        issues.append(
            ValidationIssue(
                "$.fact-snapshot.status",
                "must be available for executable plans",
            ),
        )
    if verdict_intent == "fail-closed":
        for key in (
            "descriptor-obligations",
            "validation-obligations",
            "artifact-obligations",
            "evidence-expectations",
            "detail-profiles",
        ):
            value = plan.get(key)
            if (
                isinstance(value, Sequence)
                and not isinstance(value, str | bytes)
                and value
            ):
                issues.append(
                    ValidationIssue(
                        f"$.{key}",
                        "must be empty for fail-closed plans",
                    ),
                )


def _validate_no_unavailable_fact_providers(
    fact_snapshot: Mapping[str, object] | None,
    path: str,
) -> None:
    if fact_snapshot is None:
        return
    providers = fact_snapshot.get("providers")
    if not isinstance(providers, Sequence) or isinstance(
        providers,
        str | bytes,
    ):
        return
    issues = [
        ValidationIssue(
            f"{path}[{index}].status",
            "unavailable providers are not allowed for executable plans",
        )
        for index, provider in enumerate(providers)
        if isinstance(provider, Mapping)
        and provider.get("status") == "unavailable"
    ]
    if issues:
        raise ContractValidationError(issues)


def _validate_plan_digest(
    plan: Mapping[str, object],
    issues: list[ValidationIssue],
) -> None:
    digest = plan.get("plan-digest")
    if not isinstance(digest, str) or _DIGEST_RE.fullmatch(digest) is None:
        issues.append(
            ValidationIssue("$.plan-digest", "must be a sha256 digest"),
        )
        return
    try:
        expected_digest = ci_validation_plan_digest(plan)
    except (TypeError, ValueError) as error:
        issues.append(
            ValidationIssue(
                "$.plan-digest",
                f"cannot canonicalize plan: {error}",
            ),
        )
        return
    if digest != expected_digest:
        issues.append(ValidationIssue("$.plan-digest", "does not match plan"))


def _validate_plan_sections(  # noqa: C901,PLR0915
    plan: Mapping[str, object],
    issues: list[ValidationIssue],
    *,
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
) -> None:
    verdict_intent = plan.get("verdict-intent")
    frozen_groups: list[dict[str, object]] = []
    if verdict_intent not in _PLAN_VERDICT_INTENTS:
        issues.append(ValidationIssue("$.verdict-intent", "is not registered"))
    try:
        work_groups = _sequence(plan["work-groups"], "$.work-groups")
        frozen_groups = _freeze_work_groups(work_groups)
        executable_ids = [
            str(group["work-group-id"])
            for group in frozen_groups
            if group["kind"] != "evidence-aggregation"
        ]
        if isinstance(verdict_intent, str):
            _validate_no_executable_work(verdict_intent, executable_ids)
        _validate_dependency_graph(frozen_groups, issues)
    except (KeyError, ContractValidationError) as error:
        _extend_issues(issues, error, "$.work-groups")
    try:
        evidence_expectations = _sequence(
            plan["evidence-expectations"],
            "$.evidence-expectations",
        )
        validation_obligations = _sequence(
            plan["validation-obligations"],
            "$.validation-obligations",
        )
        descriptor_obligations = _sequence(
            plan["descriptor-obligations"],
            "$.descriptor-obligations",
        )
        artifact_obligations = _sequence(
            plan["artifact-obligations"],
            "$.artifact-obligations",
        )
        _validate_identifier_record_order(
            evidence_expectations,
            "evidence-expectation-id",
            "$.evidence-expectations",
            issues,
        )
        _validate_identifier_record_order(
            validation_obligations,
            "validation-obligation-id",
            "$.validation-obligations",
            issues,
        )
        _validate_identifier_record_order(
            descriptor_obligations,
            "descriptor-obligation-id",
            "$.descriptor-obligations",
            issues,
        )
        _validate_identifier_record_order(
            artifact_obligations,
            "artifact-obligation-id",
            "$.artifact-obligations",
            issues,
        )
        if fact_snapshot is None:
            for obligation in artifact_obligations:
                _validate_artifact_obligation_schema(obligation, issues)
        _validate_evidence_expectations(evidence_expectations)
        if isinstance(verdict_intent, str):
            _validate_executable_bindings(
                frozen_groups,
                _BindingSections(
                    evidence_expectations=evidence_expectations,
                    validation_obligations=validation_obligations,
                    descriptor_obligations=descriptor_obligations,
                    artifact_obligations=artifact_obligations,
                ),
                issues,
            )
            _validate_selector_variants(frozen_groups, issues)
            _validate_detail_profile_references(
                plan.get("detail-profiles"),
                frozen_groups,
                evidence_expectations,
                issues,
            )
            _validate_coverage_target_resolution(
                plan.get("subjects"),
                plan.get("detail-profiles"),
                frozen_groups,
                _BindingSections(
                    evidence_expectations=evidence_expectations,
                    validation_obligations=validation_obligations,
                    descriptor_obligations=descriptor_obligations,
                    artifact_obligations=artifact_obligations,
                ),
                fact_snapshot,
                issues,
            )
            _validate_unsupported_subject_isolation(
                plan.get("subjects"),
                _BindingSections(
                    evidence_expectations=evidence_expectations,
                    validation_obligations=validation_obligations,
                    descriptor_obligations=descriptor_obligations,
                    artifact_obligations=artifact_obligations,
                ),
                frozen_groups,
                plan.get("classification"),
                fact_snapshot,
                issues,
            )
            subjects = _sequence(plan["subjects"], "$.subjects")
            _validate_standalone_fact_subject_coverage(
                subjects,
                fact_snapshot,
            )
            _validate_selected_validation_only_coverage(
                subjects,
                frozen_groups,
                evidence_expectations,
                validation_obligations,
                plan.get("classification"),
                issues,
            )
            _validate_selected_descriptor_backed_coverage(
                subjects,
                frozen_groups,
                evidence_expectations,
                validation_obligations,
                descriptor_obligations,
                artifact_obligations,
                fact_snapshot,
                issues,
            )
            _validate_scheduled_full_equivalent_scope(
                plan,
                frozen_groups,
                evidence_expectations,
                validation_obligations,
                issues,
            )
            _validate_workflow_release_infrastructure_scope(
                plan,
                frozen_groups,
                evidence_expectations,
                validation_obligations,
                issues,
            )
            if fact_snapshot is not None:
                _validate_workflow_release_infrastructure_fact_scope(
                    plan,
                    frozen_groups,
                    evidence_expectations,
                    descriptor_obligations,
                    artifact_obligations,
                    fact_snapshot,
                    issues,
                )
                _validate_full_scope_descriptor_coverage(
                    plan,
                    frozen_groups,
                    evidence_expectations,
                    descriptor_obligations,
                    fact_snapshot,
                    issues,
                )
                classification_for_facts = plan.get("classification")
                if not isinstance(classification_for_facts, Mapping):
                    classification_for_facts = None
                _validate_fact_backed_obligations(
                    subjects=subjects,
                    classification=classification_for_facts,
                    descriptor_obligations=descriptor_obligations,
                    artifact_obligations=artifact_obligations,
                    work_groups=frozen_groups,
                    fact_snapshot=fact_snapshot,
                    allow_global_workflow_descriptor_impacts=(
                        _requires_scheduled_full_equivalent_scope(plan)
                    ),
                )
    except (KeyError, ContractValidationError) as error:
        _extend_issues(issues, error, "$.executable-bindings")
    try:
        diagnostics = _sequence(plan["diagnostics"], "$.diagnostics")
        frozen_diagnostics = _sorted_records(
            diagnostics,
            "diagnostic-id",
            "$.diagnostics",
        )
        if isinstance(verdict_intent, str):
            _validate_diagnostics(frozen_diagnostics, verdict_intent)
            _validate_unavailable_range_diagnostics_for_plan(
                plan,
                frozen_diagnostics,
                issues,
            )
    except (KeyError, ContractValidationError) as error:
        _extend_issues(issues, error, "$.diagnostics")
    _validate_classification_section(
        plan,
        issues,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
    )


def _validate_standalone_fact_subject_coverage(
    subjects: Sequence[Mapping[str, object]],
    fact_snapshot: Mapping[str, object] | None,
) -> None:
    if fact_snapshot is None:
        return
    _validate_provider_subject_coverage(
        subjects=subjects,
        provider_subjects=_provider_subject_projection(
            fact_snapshot,
            subjects,
        ),
    )


def _extend_issues(
    issues: list[ValidationIssue],
    error: Exception,
    path: str,
) -> None:
    if isinstance(error, ContractValidationError):
        issues.extend(error.issues)
    else:
        issues.append(ValidationIssue(path, str(error)))


def _validate_identifier_record_order(
    records: Sequence[Mapping[str, object]],
    id_key: str,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    ids: list[str] = []
    for index, record in enumerate(records):
        identifier = record.get(id_key)
        if not isinstance(identifier, str) or identifier == "":
            issues.append(
                ValidationIssue(f"{path}[{index}].{id_key}", "is required"),
            )
            continue
        ids.append(identifier)
    expected = sorted(records, key=lambda item: str(item.get(id_key)))
    if records != expected or len(ids) != len(set(ids)):
        issues.append(
            ValidationIssue(path, f"must be ordered uniquely by {id_key}"),
        )


def _validate_classification_section(
    plan: Mapping[str, object],
    issues: list[ValidationIssue],
    *,
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
) -> None:
    classification = _mapping_or_issue(
        plan.get("classification"),
        "$.classification",
        issues,
    )
    if classification is None:
        return
    impact_ids = _validate_impact_records(classification, issues)
    _validate_impact_coverage_target_resolution(
        classification,
        plan.get("subjects"),
        issues,
    )
    _validate_scheduled_full_classification_shape(classification, plan, issues)
    if plan.get("verdict-intent") == "executable":
        _validate_executable_impact_categories(classification, issues)
    _validate_classification_changed_files_for_snapshot(
        classification,
        changed_files_snapshot,
        issues,
    )
    _validate_obligation_source_impact_ids(
        plan,
        impact_ids,
        issues,
    )
    expansion_ids = _validate_broad_expansion_records(
        classification,
        impact_ids,
        plan,
        issues,
    )
    fact_dependency_edges = (
        _fact_snapshot_dependency_edge_keys(fact_snapshot)
        if fact_snapshot is not None
        else None
    )
    provenance_ids = _validate_subject_selection_records(
        classification,
        impact_ids,
        expansion_ids,
        fact_dependency_edges,
        plan,
        issues,
    )
    _validate_ecosystem_scoped_impact_subject_scope(
        classification,
        plan,
        issues,
    )
    _validate_subsumption_records(
        classification,
        impact_ids,
        expansion_ids,
        provenance_ids,
        plan,
        issues,
    )
    if not isinstance(classification.get("lightweight-only"), bool):
        issues.append(
            ValidationIssue(
                "$.classification.lightweight-only",
                "must be a boolean",
            ),
        )
    else:
        _validate_zero_file_executable_lightweight_only(
            classification,
            plan,
            changed_files_snapshot,
            issues,
        )
        _validate_known_non_impacting_sets_are_lightweight_only(
            classification,
            issues,
        )
        _validate_lightweight_only_plan(classification, plan, issues)


def _validate_executable_impact_categories(
    classification: Mapping[str, object],
    issues: list[ValidationIssue],
) -> None:
    impacts = classification.get("impacts")
    if not isinstance(impacts, Sequence) or isinstance(impacts, str | bytes):
        return
    for index, impact in enumerate(impacts):
        if isinstance(impact, Mapping) and impact.get("category") == "unknown":
            issues.append(
                ValidationIssue(
                    f"$.classification.impacts[{index}].category",
                    "executable plans cannot include unknown impacts",
                ),
            )


def _validate_ecosystem_scoped_impact_subject_scope(
    classification: Mapping[str, object],
    plan: Mapping[str, object],
    issues: list[ValidationIssue],
) -> None:
    if plan.get("verdict-intent") != "executable":
        return
    targeted_ecosystems = _ecosystem_scoped_impact_ecosystems(classification)
    if not targeted_ecosystems:
        return
    for subject in _mapping_items(plan.get("subjects")):
        if (
            subject.get("activity-status") == "active"
            and subject.get("ecosystem") in targeted_ecosystems
            and subject.get("selection-status") != "selected"
        ):
            issues.append(
                ValidationIssue(
                    "$.subjects",
                    "ecosystem-scoped executable impacts must select every "
                    "active subject in the targeted ecosystem",
                ),
            )


def _ecosystem_scoped_impact_ecosystems(
    classification: Mapping[str, object],
) -> set[str]:
    ecosystems: set[str] = set()
    impacts = classification.get("impacts")
    if not isinstance(impacts, Sequence) or isinstance(impacts, str | bytes):
        return ecosystems
    for impact in impacts:
        if (
            not isinstance(impact, Mapping)
            or impact.get("category") != "ecosystem-scoped"
        ):
            continue
        target = impact.get("coverage-target")
        if (
            isinstance(target, Mapping)
            and target.get("type") == "ecosystem"
            and target.get("id") in _ECOSYSTEMS
        ):
            ecosystems.add(str(target["id"]))
    return ecosystems


def _validate_scheduled_full_classification_shape(
    classification: Mapping[str, object],
    plan: Mapping[str, object],
    issues: list[ValidationIssue],
) -> None:
    if plan.get("mode") != "scheduled_full":
        return
    impacts = classification.get("impacts")
    if (
        isinstance(impacts, Sequence)
        and not isinstance(impacts, str | bytes)
        and impacts
    ):
        issues.append(
            ValidationIssue(
                "$.classification.impacts",
                "must be empty for scheduled-full plans",
            ),
        )


def _validate_scheduled_full_equivalent_scope(
    plan: Mapping[str, object],
    work_groups: Sequence[Mapping[str, object]],
    evidence_expectations: Sequence[Mapping[str, object]],
    validation_obligations: Sequence[Mapping[str, object]],
    issues: list[ValidationIssue],
) -> None:
    if plan.get("verdict-intent") != "executable":
        return
    requires_global_scope = _requires_scheduled_full_equivalent_scope(plan)
    if plan.get("mode") != "scheduled_full" and not requires_global_scope:
        return
    missing_surfaces = _TOOLING_SURFACE_IDS - _workflow_tooling_chain_surfaces(
        work_groups,
        evidence_expectations,
        validation_obligations,
    )
    if missing_surfaces:
        message = (
            "scheduled-full-equivalent executable plans must cover "
            "closed tooling surface scope"
            if requires_global_scope and plan.get("mode") != "scheduled_full"
            else "scheduled-full plans must cover every closed tooling surface"
        )
        issues.append(
            ValidationIssue(
                "$.work-groups",
                message,
            ),
        )


def _requires_scheduled_full_equivalent_scope(
    plan: Mapping[str, object],
) -> bool:
    if plan.get("mode") not in {"pull_request", "push"}:
        return False
    classification = plan.get("classification")
    if not isinstance(classification, Mapping):
        return False
    return _classification_has_global_impact(
        classification,
    ) or _classification_has_scheduled_full_equivalent_infrastructure_impact(
        classification,
    )


def _classification_has_global_impact(
    classification: Mapping[str, object],
) -> bool:
    impacts = classification.get("impacts")
    if not isinstance(impacts, Sequence) or isinstance(impacts, str | bytes):
        return False
    for impact in impacts:
        if not isinstance(impact, Mapping):
            continue
        target = impact.get("coverage-target")
        if impact.get("category") == "global":
            return True
        if isinstance(target, Mapping) and target.get("type") == "global":
            return True
    return False


def _classification_has_scheduled_full_equivalent_infrastructure_impact(
    classification: Mapping[str, object],
) -> bool:
    impacts = classification.get("impacts")
    if not isinstance(impacts, Sequence) or isinstance(impacts, str | bytes):
        return False
    for impact in impacts:
        if not isinstance(impact, Mapping):
            continue
        target = impact.get("coverage-target")
        if (
            impact.get("category") == "workflow-release-infrastructure"
            and isinstance(target, Mapping)
            and target.get("type") == "tooling-surface"
            and target.get("id")
            in _SCHEDULED_FULL_EQUIVALENT_INFRASTRUCTURE_SURFACES
        ):
            return True
    return False


def _validate_workflow_release_infrastructure_scope(
    plan: Mapping[str, object],
    work_groups: Sequence[Mapping[str, object]],
    evidence_expectations: Sequence[Mapping[str, object]],
    validation_obligations: Sequence[Mapping[str, object]],
    issues: list[ValidationIssue],
) -> None:
    if plan.get("verdict-intent") != "executable":
        return
    classification = plan.get("classification")
    if not isinstance(classification, Mapping):
        return
    infrastructure_impacts = _workflow_release_infrastructure_impacts(
        classification,
        issues,
    )
    if not infrastructure_impacts:
        return
    expansion_impact_ids = _workflow_release_infrastructure_expansion_impacts(
        classification,
    )
    covered = _workflow_tooling_chain_surface_impact_ids(
        work_groups,
        evidence_expectations,
        validation_obligations,
    )
    for impact_id, surface in sorted(infrastructure_impacts.items()):
        if impact_id not in expansion_impact_ids:
            issues.append(
                ValidationIssue(
                    "$.classification.broad-expansions",
                    "workflow-release infrastructure impacts require "
                    "surface-specific expansion",
                ),
            )
        if impact_id not in covered.get(surface, set()):
            issues.append(
                ValidationIssue(
                    "$.validation-obligations",
                    "workflow-release infrastructure impacts require "
                    "surface-specific workflow-release-tooling validation",
                ),
            )


def _workflow_release_infrastructure_impacts(
    classification: Mapping[str, object],
    issues: list[ValidationIssue],
) -> dict[str, str]:
    impacts = classification.get("impacts")
    if not isinstance(impacts, Sequence) or isinstance(impacts, str | bytes):
        return {}
    result: dict[str, str] = {}
    for index, impact in enumerate(impacts):
        if (
            not isinstance(impact, Mapping)
            or impact.get("category") != "workflow-release-infrastructure"
        ):
            continue
        target = impact.get("coverage-target")
        impact_id = impact.get("impact-id")
        if (
            not isinstance(impact_id, str)
            or not isinstance(target, Mapping)
            or target.get("type") != "tooling-surface"
            or not isinstance(target.get("id"), str)
        ):
            issues.append(
                ValidationIssue(
                    f"$.classification.impacts[{index}].coverage-target",
                    "workflow-release infrastructure impacts require "
                    "tooling-surface coverage targets",
                ),
            )
            continue
        result[impact_id] = str(target["id"])
    return result


def _workflow_release_infrastructure_expansion_impacts(
    classification: Mapping[str, object],
) -> set[str]:
    expansions = classification.get("broad-expansions")
    if not isinstance(expansions, Sequence) or isinstance(
        expansions,
        str | bytes,
    ):
        return set()
    return {
        str(expansion["source-impact-id"])
        for expansion in expansions
        if isinstance(expansion, Mapping)
        and expansion.get("category") == "workflow-release-infrastructure"
        and isinstance(expansion.get("source-impact-id"), str)
    }


def _workflow_tooling_chain_surface_impact_ids(
    work_groups: Sequence[Mapping[str, object]],
    evidence_expectations: Sequence[Mapping[str, object]],
    validation_obligations: Sequence[Mapping[str, object]],
) -> dict[str, set[str]]:
    evidence_by_work_group = {
        str(item.get("work-group-id")): item
        for item in evidence_expectations
        if isinstance(item.get("work-group-id"), str)
    }
    obligations_by_work_group = {
        str(item.get("work-group-id")): item
        for item in validation_obligations
        if isinstance(item.get("work-group-id"), str)
    }
    covered: dict[str, set[str]] = {}
    for group in work_groups:
        surface = _workflow_tooling_group_surface(
            group,
            evidence_by_work_group,
            obligations_by_work_group,
        )
        if surface is None:
            continue
        work_group_id = group.get("work-group-id")
        if not isinstance(work_group_id, str):
            continue
        obligation = obligations_by_work_group[work_group_id]
        source_impact_ids = obligation.get("source-impact-ids")
        if not isinstance(source_impact_ids, Sequence) or isinstance(
            source_impact_ids,
            str | bytes,
        ):
            continue
        covered.setdefault(surface, set()).update(
            str(impact_id)
            for impact_id in source_impact_ids
            if isinstance(impact_id, str)
        )
    return covered


def _workflow_tooling_group_surface(
    group: Mapping[str, object],
    evidence_by_work_group: Mapping[str, Mapping[str, object]],
    obligations_by_work_group: Mapping[str, Mapping[str, object]],
) -> str | None:
    if group.get("kind") != "workflow-release-tooling":
        return None
    work_group_id = group.get("work-group-id")
    target = group.get("coverage-target")
    if not isinstance(work_group_id, str) or not isinstance(target, Mapping):
        return None
    surface = target.get("id")
    if target.get("type") != "tooling-surface" or not isinstance(surface, str):
        return None
    evidence = evidence_by_work_group.get(work_group_id)
    obligation = obligations_by_work_group.get(work_group_id)
    evidence_id = None
    if evidence is not None:
        evidence_id = evidence.get("evidence-expectation-id")
    if (
        evidence is None
        or obligation is None
        or evidence.get("category") != "workflow-release-tooling"
        or evidence.get("coverage-target") != target
        or obligation.get("kind") != "workflow-release-tooling"
        or obligation.get("coverage-target") != target
        or obligation.get("expected-evidence-id") != evidence_id
    ):
        return None
    return surface


def _validate_workflow_release_infrastructure_fact_scope(  # noqa: PLR0913
    plan: Mapping[str, object],
    work_groups: Sequence[Mapping[str, object]],
    evidence_expectations: Sequence[Mapping[str, object]],
    descriptor_obligations: Sequence[Mapping[str, object]],
    artifact_obligations: Sequence[Mapping[str, object]],
    fact_snapshot: Mapping[str, object],
    issues: list[ValidationIssue],
) -> None:
    if plan.get("verdict-intent") != "executable":
        return
    classification = plan.get("classification")
    if not isinstance(classification, Mapping):
        return
    impact_issues: list[ValidationIssue] = []
    infrastructure_impacts = _workflow_release_infrastructure_impacts(
        classification,
        impact_issues,
    )
    surfaces = set(infrastructure_impacts.values())
    if not surfaces:
        return
    if surfaces & _ALL_DESCRIPTOR_INFRASTRUCTURE_SURFACES:
        _validate_all_discovered_descriptor_coverage(
            work_groups,
            evidence_expectations,
            descriptor_obligations,
            fact_snapshot,
            issues,
            scope_name="workflow-release infrastructure scope",
        )
    if surfaces & _ARTIFACT_SCOPE_INFRASTRUCTURE_SURFACES:
        _validate_descriptor_backed_artifact_scope(
            plan.get("subjects"),
            artifact_obligations,
            fact_snapshot,
            issues,
        )
    if surfaces & _ACTIVE_BUILD_SCOPE_INFRASTRUCTURE_SURFACES:
        _validate_active_build_subject_scope(plan.get("subjects"), issues)
    fact_provider_impact_ids = {
        impact_id
        for impact_id, surface in infrastructure_impacts.items()
        if surface == "fact-provider"
    }
    if fact_provider_impact_ids:
        _validate_active_provider_bound_subject_scope(
            plan.get("subjects"),
            fact_snapshot,
            _workflow_release_infrastructure_affected_ecosystems(
                classification,
                fact_provider_impact_ids,
            ),
            issues,
        )


def _validate_full_scope_descriptor_coverage(  # noqa: PLR0913
    plan: Mapping[str, object],
    work_groups: Sequence[Mapping[str, object]],
    evidence_expectations: Sequence[Mapping[str, object]],
    descriptor_obligations: Sequence[Mapping[str, object]],
    fact_snapshot: Mapping[str, object],
    issues: list[ValidationIssue],
) -> None:
    if plan.get("verdict-intent") != "executable" or (
        plan.get("mode") != "scheduled_full"
        and not _requires_scheduled_full_equivalent_scope(plan)
    ):
        return
    _validate_all_discovered_descriptor_coverage(
        work_groups,
        evidence_expectations,
        descriptor_obligations,
        fact_snapshot,
        issues,
        scope_name="full-scope plans",
    )


def _validate_all_discovered_descriptor_coverage(  # noqa: PLR0913
    work_groups: Sequence[Mapping[str, object]],
    evidence_expectations: Sequence[Mapping[str, object]],
    descriptor_obligations: Sequence[Mapping[str, object]],
    fact_snapshot: Mapping[str, object],
    issues: list[ValidationIssue],
    *,
    scope_name: str,
) -> None:
    descriptor_paths = set(_fact_indexes(fact_snapshot).descriptors)
    if not descriptor_paths:
        return
    obligations_by_descriptor = _descriptor_obligations_by_target(
        descriptor_obligations,
    )
    groups_by_descriptor = _descriptor_work_groups_by_target(work_groups)
    evidence_by_work_group = {
        str(item.get("work-group-id")): item
        for item in evidence_expectations
        if isinstance(item.get("work-group-id"), str)
    }
    for descriptor_path in sorted(descriptor_paths):
        obligations = obligations_by_descriptor.get(descriptor_path, [])
        groups = groups_by_descriptor.get(descriptor_path, [])
        if len(obligations) != 1:
            issues.append(
                ValidationIssue(
                    "$.descriptor-obligations",
                    f"{scope_name} must include exactly one descriptor "
                    "obligation for every discovered descriptor",
                ),
            )
            continue
        if len(groups) != 1:
            issues.append(
                ValidationIssue(
                    "$.work-groups",
                    f"{scope_name} descriptor obligations must bind one-to-one "
                    "to descriptor-validation work groups",
                ),
            )
            continue
        work_group_id = groups[0].get("work-group-id")
        if (
            obligations[0].get("work-group-id") != work_group_id
            or evidence_by_work_group.get(str(work_group_id)) is None
        ):
            issues.append(
                ValidationIssue(
                    "$.descriptor-obligations",
                    f"{scope_name} descriptor obligations must bind one-to-one "
                    "to descriptor-validation work groups and evidence",
                ),
            )
        elif obligations[0].get("descriptor-scope") != "all-discovered":
            issues.append(
                ValidationIssue(
                    "$.descriptor-obligations",
                    f"{scope_name} descriptor obligations must use "
                    "all-discovered descriptor scope",
                ),
            )


def _validate_descriptor_backed_artifact_scope(
    subjects_value: object,
    artifact_obligations: Sequence[Mapping[str, object]],
    fact_snapshot: Mapping[str, object],
    issues: list[ValidationIssue],
) -> None:
    subjects = _mapping_items(subjects_value)
    catalog_profiles = _target_catalog_profiles_by_descriptor(fact_snapshot)
    required = _active_descriptor_artifact_subject_bindings(
        subjects,
        catalog_profiles,
    )
    for subject_id, descriptor_path, selected in required:
        if not selected:
            issues.append(
                ValidationIssue(
                    "$.subjects",
                    "workflow-release infrastructure artifact scope must "
                    "select descriptor-backed artifact subjects",
                ),
            )
            continue
        expected_profiles = catalog_profiles.get(descriptor_path, set())
        if not expected_profiles:
            issues.append(
                ValidationIssue(
                    "$.artifact-obligations",
                    "workflow-release infrastructure artifact scope must "
                    "resolve target-catalog profiles",
                ),
            )
            continue
        covered_profiles: set[str] = set()
        for obligation in artifact_obligations:
            if (
                obligation.get("subject-id") == subject_id
                and obligation.get("descriptor-path") == descriptor_path
            ):
                covered_profiles.update(
                    _string_items(obligation.get("profile-coverage")),
                )
        if covered_profiles != expected_profiles:
            issues.append(
                ValidationIssue(
                    "$.artifact-obligations",
                    "workflow-release infrastructure artifact scope must "
                    "cover descriptor-backed artifact obligations",
                ),
            )


def _active_descriptor_artifact_subject_bindings(
    subjects: Sequence[Mapping[str, object]],
    catalog_profiles: Mapping[str, set[str]],
) -> list[tuple[str, str, bool]]:
    bindings: list[tuple[str, str, bool]] = []
    for subject in subjects:
        descriptor = subject.get("descriptor")
        if (
            subject.get("activity-status") != "active"
            or subject.get("capability-class") != "descriptor-backed"
            or not isinstance(descriptor, Mapping)
        ):
            continue
        subject_id = subject.get("subject-id")
        descriptor_path = descriptor.get("path")
        if (
            isinstance(subject_id, str)
            and isinstance(descriptor_path, str)
            and descriptor_path in catalog_profiles
        ):
            bindings.append(
                (
                    subject_id,
                    descriptor_path,
                    subject.get("selection-status") == "selected",
                ),
            )
    return bindings


def _validate_active_build_subject_scope(
    subjects_value: object,
    issues: list[ValidationIssue],
) -> None:
    for subject in _mapping_items(subjects_value):
        capabilities = subject.get("capabilities")
        if (
            subject.get("activity-status") == "active"
            and isinstance(capabilities, Mapping)
            and (
                capabilities.get("build") is True
                or capabilities.get("release-shaped-artifacts") is True
            )
            and subject.get("selection-status") != "selected"
        ):
            issues.append(
                ValidationIssue(
                    "$.subjects",
                    "workflow-release infrastructure build scope must select "
                    "active build-capable subjects",
                ),
            )


def _validate_active_provider_bound_subject_scope(
    subjects_value: object,
    fact_snapshot: Mapping[str, object],
    affected_ecosystems: set[str],
    issues: list[ValidationIssue],
) -> None:
    if not affected_ecosystems:
        issues.append(
            ValidationIssue(
                "$.classification.broad-expansions",
                "workflow-release infrastructure fact-provider scope must "
                "determine affected ecosystems",
            ),
        )
        return
    subjects = _mapping_items(subjects_value)
    provider_subjects = _provider_subject_projection(fact_snapshot, subjects)
    if provider_subjects is None:
        return
    provider_bound_subject_ids = {
        str(record.get("subject-id"))
        for record in provider_subjects
        if record.get("ecosystem") in affected_ecosystems
        and isinstance(record.get("subject-id"), str)
    }
    for subject in subjects:
        if (
            subject.get("activity-status") == "active"
            and subject.get("subject-id") in provider_bound_subject_ids
            and subject.get("selection-status") != "selected"
        ):
            issues.append(
                ValidationIssue(
                    "$.subjects",
                    "workflow-release infrastructure fact-provider scope must "
                    "select active provider-bound subjects",
                ),
            )


def _workflow_release_infrastructure_affected_ecosystems(
    classification: Mapping[str, object],
    impact_ids: set[str],
) -> set[str]:
    ecosystems: set[str] = set()
    expansions = classification.get("broad-expansions")
    if not isinstance(expansions, Sequence) or isinstance(
        expansions,
        str | bytes,
    ):
        return ecosystems
    for expansion in expansions:
        if (
            not isinstance(expansion, Mapping)
            or expansion.get("category") != "workflow-release-infrastructure"
            or expansion.get("source-impact-id") not in impact_ids
        ):
            continue
        scope = expansion.get("resulting-scope")
        if not isinstance(scope, Mapping):
            continue
        ecosystems.update(
            ecosystem
            for ecosystem in _string_items(scope.get("ecosystems"))
            if ecosystem in _ECOSYSTEMS
        )
    return ecosystems


def _descriptor_obligations_by_target(
    descriptor_obligations: Sequence[Mapping[str, object]],
) -> dict[str, list[Mapping[str, object]]]:
    obligations_by_descriptor: dict[str, list[Mapping[str, object]]] = {}
    for obligation in descriptor_obligations:
        descriptor_path = _descriptor_target_id(
            obligation.get("coverage-target"),
        )
        if descriptor_path is not None:
            obligations_by_descriptor.setdefault(descriptor_path, []).append(
                obligation,
            )
    return obligations_by_descriptor


def _descriptor_work_groups_by_target(
    work_groups: Sequence[Mapping[str, object]],
) -> dict[str, list[Mapping[str, object]]]:
    groups_by_descriptor: dict[str, list[Mapping[str, object]]] = {}
    for group in work_groups:
        if group.get("kind") != "descriptor-validation":
            continue
        descriptor_path = _descriptor_target_id(group.get("coverage-target"))
        if descriptor_path is not None:
            groups_by_descriptor.setdefault(descriptor_path, []).append(group)
    return groups_by_descriptor


def _descriptor_target_id(target: object) -> str | None:
    if not isinstance(target, Mapping) or target.get("type") != "descriptor":
        return None
    target_id = target.get("id")
    return target_id if isinstance(target_id, str) else None


def _workflow_tooling_chain_surfaces(
    work_groups: Sequence[Mapping[str, object]],
    evidence_expectations: Sequence[Mapping[str, object]],
    validation_obligations: Sequence[Mapping[str, object]],
) -> set[str]:
    evidence_by_work_group = {
        str(item.get("work-group-id")): item
        for item in evidence_expectations
        if isinstance(item.get("work-group-id"), str)
    }
    obligations_by_work_group = {
        str(item.get("work-group-id")): item
        for item in validation_obligations
        if isinstance(item.get("work-group-id"), str)
    }
    covered: set[str] = set()
    for group in work_groups:
        if group.get("kind") != "workflow-release-tooling":
            continue
        work_group_id = group.get("work-group-id")
        target = group.get("coverage-target")
        if not isinstance(work_group_id, str) or not isinstance(
            target,
            Mapping,
        ):
            continue
        surface = target.get("id")
        if target.get("type") != "tooling-surface" or not isinstance(
            surface,
            str,
        ):
            continue
        evidence = evidence_by_work_group.get(work_group_id)
        obligation = obligations_by_work_group.get(work_group_id)
        evidence_id = (
            evidence.get("evidence-expectation-id")
            if evidence is not None
            else None
        )
        if (
            evidence is not None
            and obligation is not None
            and evidence.get("category") == "workflow-release-tooling"
            and evidence.get("coverage-target") == target
            and obligation.get("kind") == "workflow-release-tooling"
            and obligation.get("coverage-target") == target
            and obligation.get("expected-evidence-id") == evidence_id
        ):
            covered.add(surface)
    return covered


def _validate_lightweight_only_plan(
    classification: Mapping[str, object],
    plan: Mapping[str, object],
    issues: list[ValidationIssue],
) -> None:
    if classification.get("lightweight-only") is not True:
        return
    _validate_lightweight_only_classification(classification, issues)
    _validate_lightweight_only_subjects(plan.get("subjects"), issues)
    _validate_lightweight_only_sections(plan, issues)


def _validate_known_non_impacting_sets_are_lightweight_only(
    classification: Mapping[str, object],
    issues: list[ValidationIssue],
) -> None:
    impacts = classification.get("impacts")
    if (
        not isinstance(impacts, Sequence)
        or isinstance(impacts, str | bytes)
        or not impacts
    ):
        return
    if (
        all(
            isinstance(impact, Mapping)
            and impact.get("category") == "known-non-impacting"
            for impact in impacts
        )
        and classification.get("lightweight-only") is not True
    ):
        issues.append(
            ValidationIssue(
                "$.classification.lightweight-only",
                "all known-non-impacting impact sets must be lightweight-only",
            ),
        )


def _validate_zero_file_executable_lightweight_only(
    classification: Mapping[str, object],
    plan: Mapping[str, object],
    changed_files_snapshot: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> None:
    changed_files = _zero_file_executable_changed_files(
        plan,
        changed_files_snapshot,
    )
    if changed_files is None or changed_files:
        return
    if classification.get("lightweight-only") is not True:
        issues.append(
            ValidationIssue(
                "$.classification.lightweight-only",
                "zero-file executable plans must be lightweight-only",
            ),
        )
    _validate_zero_file_executable_no_scope(classification, plan, issues)


def _zero_file_executable_changed_files(
    plan: Mapping[str, object],
    changed_files_snapshot: Mapping[str, object] | None,
) -> Sequence[object] | None:
    if plan.get("verdict-intent") != "executable" or plan.get("mode") not in {
        "pull_request",
        "push",
    }:
        return None
    affected_range = plan.get("affected-range")
    if (
        not isinstance(affected_range, Mapping)
        or affected_range.get("status") != "available"
    ):
        return None
    payload = (
        changed_files_snapshot.get("hash-payload")
        if changed_files_snapshot is not None
        else None
    )
    if not isinstance(payload, Mapping):
        return None
    changed_files = payload.get("changed-files")
    if not isinstance(changed_files, Sequence) or isinstance(
        changed_files,
        str | bytes,
    ):
        return None
    return changed_files


def _validate_zero_file_executable_no_scope(
    classification: Mapping[str, object],
    plan: Mapping[str, object],
    issues: list[ValidationIssue],
) -> None:
    impacts = classification.get("impacts")
    if (
        isinstance(impacts, Sequence)
        and not isinstance(impacts, str | bytes)
        and impacts
    ):
        issues.append(
            ValidationIssue(
                "$.classification.impacts",
                "must be empty for zero-file executable plans",
            ),
        )
    for section_name in (
        "descriptor-obligations",
        "validation-obligations",
        "artifact-obligations",
        "evidence-expectations",
        "detail-profiles",
    ):
        _validate_zero_file_empty_section(
            plan.get(section_name),
            f"$.{section_name}",
            issues,
        )
    _validate_zero_file_terminal_work_groups(plan.get("work-groups"), issues)


def _validate_zero_file_empty_section(
    records: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if (
        isinstance(records, Sequence)
        and not isinstance(records, str | bytes)
        and records
    ):
        issues.append(
            ValidationIssue(
                path,
                "must be empty for zero-file executable plans",
            ),
        )


def _validate_zero_file_terminal_work_groups(
    work_groups: object,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(work_groups, Sequence) or isinstance(
        work_groups,
        str | bytes,
    ):
        return
    terminal_groups = [
        group
        for group in work_groups
        if isinstance(group, Mapping)
        and group.get("kind") == "evidence-aggregation"
    ]
    if len(work_groups) == 1 and len(terminal_groups) == 1:
        return
    issues.append(
        ValidationIssue(
            "$.work-groups",
            "zero-file executable plans allow only terminal "
            "evidence aggregation",
        ),
    )


def _validate_lightweight_only_classification(
    classification: Mapping[str, object],
    issues: list[ValidationIssue],
) -> None:
    impacts = classification.get("impacts")
    if isinstance(impacts, Sequence) and not isinstance(impacts, str | bytes):
        for index, impact in enumerate(impacts):
            if not isinstance(impact, Mapping):
                continue
            if impact.get("category") != "known-non-impacting":
                issues.append(
                    ValidationIssue(
                        f"$.classification.impacts[{index}].category",
                        "lightweight-only plans require known-non-impacting "
                        "impacts",
                    ),
                )
            if impact.get("coverage-target") != {"type": "none", "id": None}:
                issues.append(
                    ValidationIssue(
                        f"$.classification.impacts[{index}].coverage-target",
                        "lightweight-only plans must not target subjects",
                    ),
                )
    for key in (
        "broad-expansions",
        "subject-selection-provenance",
        "subsumptions",
    ):
        records = classification.get(key)
        if (
            isinstance(records, Sequence)
            and not isinstance(records, str | bytes)
            and records
        ):
            issues.append(
                ValidationIssue(
                    f"$.classification.{key}",
                    "must be empty for lightweight-only plans",
                ),
            )


def _validate_lightweight_only_subjects(
    subjects: object,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(subjects, Sequence) or isinstance(subjects, str | bytes):
        return
    for index, subject in enumerate(subjects):
        if not isinstance(subject, Mapping):
            continue
        if (
            subject.get("activity-status") == "active"
            and subject.get("selection-status") == "selected"
        ):
            issues.append(
                ValidationIssue(
                    f"$.subjects[{index}]",
                    "lightweight-only plans cannot select subjects",
                ),
            )


def _validate_lightweight_only_sections(
    plan: Mapping[str, object],
    issues: list[ValidationIssue],
) -> None:
    for section_name in ("descriptor-obligations", "artifact-obligations"):
        records = plan.get(section_name)
        if (
            isinstance(records, Sequence)
            and not isinstance(records, str | bytes)
            and records
        ):
            issues.append(
                ValidationIssue(
                    f"$.{section_name}",
                    "must be empty for lightweight-only plans",
                ),
            )
    _validate_lightweight_only_validation_obligations(
        plan.get("validation-obligations"),
        issues,
    )
    _validate_lightweight_only_work_groups(plan.get("work-groups"), issues)
    _validate_lightweight_only_evidence(
        plan.get("evidence-expectations"),
        issues,
    )
    _validate_lightweight_only_profiles(plan.get("detail-profiles"), issues)


def _validate_lightweight_only_validation_obligations(
    validation_obligations: object,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(validation_obligations, Sequence) or isinstance(
        validation_obligations,
        str | bytes,
    ):
        return
    for index, obligation in enumerate(validation_obligations):
        if not isinstance(obligation, Mapping):
            continue
        if obligation.get("kind") != "lightweight-preflight":
            issues.append(
                ValidationIssue(
                    f"$.validation-obligations[{index}].kind",
                    "lightweight-only plans allow only lightweight preflight",
                ),
            )
        _validate_lightweight_policy_target(
            f"$.validation-obligations[{index}].coverage-target",
            obligation.get("coverage-target"),
            issues,
        )


def _validate_lightweight_only_work_groups(
    work_groups: object,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(work_groups, Sequence) or isinstance(
        work_groups,
        str | bytes,
    ):
        return
    for index, group in enumerate(work_groups):
        if not isinstance(group, Mapping):
            continue
        if group.get("kind") == "evidence-aggregation":
            continue
        if group.get("kind") != "lightweight-preflight":
            issues.append(
                ValidationIssue(
                    f"$.work-groups[{index}].kind",
                    "lightweight-only plans allow only lightweight preflight",
                ),
            )
        _validate_lightweight_policy_target(
            f"$.work-groups[{index}].coverage-target",
            group.get("coverage-target"),
            issues,
        )


def _validate_lightweight_only_evidence(
    evidence_expectations: object,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(evidence_expectations, Sequence) or isinstance(
        evidence_expectations,
        str | bytes,
    ):
        return
    for index, evidence in enumerate(evidence_expectations):
        if not isinstance(evidence, Mapping):
            continue
        if evidence.get("category") != "lightweight-preflight":
            issues.append(
                ValidationIssue(
                    f"$.evidence-expectations[{index}].category",
                    "lightweight-only plans allow only lightweight preflight",
                ),
            )
        _validate_lightweight_policy_target(
            f"$.evidence-expectations[{index}].coverage-target",
            evidence.get("coverage-target"),
            issues,
        )


def _validate_lightweight_only_profiles(
    detail_profiles: object,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(detail_profiles, Sequence) or isinstance(
        detail_profiles,
        str | bytes,
    ):
        return
    for index, profile in enumerate(detail_profiles):
        if not isinstance(profile, Mapping):
            continue
        if profile.get("category") != "lightweight-preflight":
            issues.append(
                ValidationIssue(
                    f"$.detail-profiles[{index}].category",
                    "lightweight-only plans allow only lightweight preflight",
                ),
            )
        _validate_lightweight_policy_target(
            f"$.detail-profiles[{index}].coverage-target",
            profile.get("coverage-target"),
            issues,
        )


def _validate_lightweight_policy_target(
    path: str,
    target: object,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(target, Mapping):
        issues.append(ValidationIssue(path, "must be an object"))
        return
    target_type = target.get("type")
    if target_type == "lightweight-policy":
        if target.get("id") != "known-non-impacting":
            issues.append(
                ValidationIssue(
                    f"{path}.id",
                    "must target known-non-impacting lightweight policy",
                ),
            )
        return
    if target_type == "tooling-surface":
        if target.get("id") not in _TOOLING_SURFACE_IDS:
            issues.append(ValidationIssue(f"{path}.id", "is not registered"))
        return
    issues.append(
        ValidationIssue(
            f"{path}.type",
            "must target lightweight policy or "
            "workflow-release tooling surface",
        ),
    )


def _validate_classification_changed_files_for_snapshot(  # noqa: PLR0911
    classification: Mapping[str, object],
    changed_files_snapshot: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> None:
    if changed_files_snapshot is None:
        return
    payload = changed_files_snapshot.get("hash-payload")
    if not isinstance(payload, Mapping):
        return
    changed_files = payload.get("changed-files")
    if not isinstance(changed_files, Sequence) or isinstance(
        changed_files,
        str | bytes,
    ):
        return
    impacts = classification.get("impacts")
    if not isinstance(impacts, Sequence) or isinstance(impacts, str | bytes):
        return
    matched_paths: list[str] = []
    for impact in impacts:
        if not isinstance(impact, Mapping):
            return
        paths = impact.get("matched-paths")
        if not isinstance(paths, Sequence) or isinstance(paths, str | bytes):
            return
        matched_paths.extend(str(path) for path in paths)
    try:
        expected = _sorted_unique_strings(
            [str(path) for path in changed_files],
            "$.changed-files-snapshot.hash-payload.changed-files",
        )
        actual = _sorted_unique_strings(
            matched_paths,
            "$.classification.impacts.matched-paths",
        )
    except ContractValidationError:
        return
    if actual != expected:
        issues.append(
            ValidationIssue(
                "$.classification.impacts.matched-paths",
                "must exactly match companion changed files",
            ),
        )


def _validate_obligation_source_impact_ids(
    plan: Mapping[str, object],
    impact_ids: set[str],
    issues: list[ValidationIssue],
) -> None:
    allow_empty = plan.get("mode") == "scheduled_full"
    for section_name in (
        "validation-obligations",
        "descriptor-obligations",
        "artifact-obligations",
    ):
        records = plan.get(section_name)
        if not isinstance(records, Sequence) or isinstance(
            records,
            str | bytes,
        ):
            continue
        for index, record in enumerate(records):
            if not isinstance(record, Mapping):
                continue
            _validate_source_impact_ids(
                record.get("source-impact-ids"),
                impact_ids,
                f"$.{section_name}[{index}].source-impact-ids",
                issues,
                allow_empty=allow_empty,
            )


def _validate_impact_records(  # noqa: C901
    classification: Mapping[str, object],
    issues: list[ValidationIssue],
) -> set[str]:
    impacts = _sequence_or_issue(
        classification.get("impacts"),
        "$.classification.impacts",
        issues,
    )
    if impacts is None:
        return set()
    ids: set[str] = set()
    all_paths: set[str] = set()
    expected = sorted(impacts, key=lambda item: str(item.get("impact-id")))
    if impacts != expected:
        issues.append(
            ValidationIssue("$.classification.impacts", "must be canonical"),
        )
    for index, impact in enumerate(impacts):
        path = f"$.classification.impacts[{index}]"
        if not isinstance(impact, Mapping):
            issues.append(ValidationIssue(path, "must be an object"))
            continue
        impact_id = _required_str(impact, "impact-id", issues)
        if impact_id is not None:
            if _PLAN_LOCAL_ID_RE.fullmatch(impact_id) is None:
                issues.append(ValidationIssue(f"{path}.impact-id", "invalid"))
            if impact_id in ids:
                issues.append(ValidationIssue(f"{path}.impact-id", "duplicate"))
            ids.add(impact_id)
        if impact.get("category") not in _IMPACT_CATEGORIES:
            issues.append(ValidationIssue(f"{path}.category", "is invalid"))
        _validate_impact_matched_paths(
            impact.get("matched-paths"),
            path,
            all_paths,
            issues,
        )
        for key in ("source-rule", "rationale"):
            value = impact.get(key)
            if not isinstance(value, str) or value == "":
                issues.append(
                    ValidationIssue(f"{path}.{key}", "must be non-empty"),
                )
        _validate_impact_coverage_target(
            impact.get("coverage-target"),
            f"{path}.coverage-target",
            issues,
        )
        _validate_impact_requires(impact.get("requires"), path, issues)
    return ids


def _validate_impact_matched_paths(
    value: object,
    path: str,
    all_paths: set[str],
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        issues.append(ValidationIssue(f"{path}.matched-paths", "must be array"))
        return
    paths: list[str] = []
    for index, item in enumerate(value):
        if not isinstance(item, str) or item == "":
            issues.append(
                ValidationIssue(
                    f"{path}.matched-paths[{index}]",
                    "must be non-empty string",
                ),
            )
            continue
        _validate_repo_relative_git_path(
            item,
            f"{path}.matched-paths[{index}]",
            issues,
        )
        if item in all_paths:
            issues.append(
                ValidationIssue(
                    f"{path}.matched-paths[{index}]",
                    "must not overlap another impact",
                ),
            )
        all_paths.add(item)
        paths.append(item)
    if paths != sorted(paths) or len(paths) != len(set(paths)):
        issues.append(
            ValidationIssue(
                f"{path}.matched-paths",
                "must be ordered uniquely",
            ),
        )


def _validate_impact_coverage_target(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    target = _mapping_or_issue(value, path, issues)
    if target is None:
        return
    target_type = target.get("type")
    if target_type not in _IMPACT_COVERAGE_TARGET_TYPES:
        issues.append(ValidationIssue(f"{path}.type", "is not registered"))
    _validate_coverage_target_id(target_type, target.get("id"), path, issues)


def _validate_impact_requires(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    requires = _mapping_or_issue(value, f"{path}.requires", issues)
    if requires is None:
        return
    for key in (
        "descriptor-validation",
        "downstream-expansion",
        "broad-expansion",
    ):
        if not isinstance(requires.get(key), bool):
            issues.append(
                ValidationIssue(f"{path}.requires.{key}", "must be bool"),
            )
    if "diagnostic" not in requires:
        issues.append(
            ValidationIssue(f"{path}.requires.diagnostic", "is required"),
        )
    diagnostic = requires.get("diagnostic")
    if (
        diagnostic is not None
        and diagnostic not in REGISTERED_CI_VALIDATION_DIAGNOSTIC_CODES
    ):
        issues.append(
            ValidationIssue(
                f"{path}.requires.diagnostic",
                "is not registered",
            ),
        )


def _validate_broad_expansion_records(  # noqa: C901
    classification: Mapping[str, object],
    impact_ids: set[str],
    plan: Mapping[str, object],
    issues: list[ValidationIssue],
) -> set[str]:
    expansions = _sequence_or_issue(
        classification.get("broad-expansions"),
        "$.classification.broad-expansions",
        issues,
    )
    if expansions is None:
        return set()
    ids: set[str] = set()
    expected = sorted(
        expansions,
        key=lambda item: str(item.get("expansion-id")),
    )
    if expansions != expected:
        issues.append(
            ValidationIssue(
                "$.classification.broad-expansions",
                "must be canonical",
            ),
        )
    for index, expansion in enumerate(expansions):
        path = f"$.classification.broad-expansions[{index}]"
        if not isinstance(expansion, Mapping):
            issues.append(ValidationIssue(path, "must be an object"))
            continue
        expansion_id = _required_str(expansion, "expansion-id", issues)
        if expansion_id is not None:
            if expansion_id in ids:
                issues.append(
                    ValidationIssue(f"{path}.expansion-id", "duplicate"),
                )
            ids.add(expansion_id)
        source_impact_id = expansion.get("source-impact-id")
        if source_impact_id not in impact_ids:
            issues.append(
                ValidationIssue(
                    f"{path}.source-impact-id",
                    "must resolve to impact",
                ),
            )
        if expansion.get("category") not in _BROAD_EXPANSION_CATEGORIES:
            issues.append(ValidationIssue(f"{path}.category", "is invalid"))
        reason = expansion.get("reason")
        if not isinstance(reason, str) or reason == "":
            issues.append(
                ValidationIssue(f"{path}.reason", "must be non-empty"),
            )
        resulting_scope = expansion.get("resulting-scope")
        if not isinstance(resulting_scope, Mapping):
            issues.append(
                ValidationIssue(
                    f"{path}.resulting-scope",
                    "must be object",
                ),
            )
        else:
            _validate_broad_expansion_scope(
                resulting_scope,
                path,
                plan,
                issues,
            )
    return ids


def _validate_broad_expansion_scope(
    scope: Mapping[str, object],
    path: str,
    plan: Mapping[str, object],
    issues: list[ValidationIssue],
) -> None:
    subject_ids = _record_ids(plan.get("subjects"), "subject-id")
    descriptor_paths = _plan_descriptor_paths(plan)
    ecosystem_refs = _validate_string_array_refs(
        scope.get("ecosystems"),
        f"{path}.resulting-scope.ecosystems",
        issues,
        allow_empty=True,
    )
    for ecosystem in ecosystem_refs:
        if ecosystem not in _ECOSYSTEMS:
            issues.append(
                ValidationIssue(
                    f"{path}.resulting-scope.ecosystems",
                    "must resolve to ecosystem",
                ),
            )
    subject_refs = _validate_string_array_refs(
        scope.get("subjects"),
        f"{path}.resulting-scope.subjects",
        issues,
        allow_empty=True,
    )
    for subject_id in subject_refs:
        if subject_id not in subject_ids:
            issues.append(
                ValidationIssue(
                    f"{path}.resulting-scope.subjects",
                    "must resolve to subject",
                ),
            )
    descriptors = scope.get("descriptors")
    if descriptors in {"all-discovered", "selected", "none"}:
        if descriptors == "selected" and not descriptor_paths:
            issues.append(
                ValidationIssue(
                    f"{path}.resulting-scope.descriptors",
                    "must resolve to descriptor",
                ),
            )
    else:
        issues.append(
            ValidationIssue(
                f"{path}.resulting-scope.descriptors",
                "is invalid",
            ),
        )


def _plan_descriptor_paths(plan: Mapping[str, object]) -> set[str]:
    paths: set[str] = set()
    for obligation in _mapping_items(plan.get("descriptor-obligations")):
        target = obligation.get("coverage-target")
        if (
            isinstance(target, Mapping)
            and target.get("type") == "descriptor"
            and isinstance(target.get("id"), str)
        ):
            paths.add(str(target["id"]))
    for subject in _mapping_items(plan.get("subjects")):
        descriptor = subject.get("descriptor")
        if isinstance(descriptor, Mapping) and isinstance(
            descriptor.get("path"),
            str,
        ):
            paths.add(str(descriptor["path"]))
    return paths


def _validate_subject_selection_records(  # noqa: C901, PLR0912, PLR0913, PLR0915
    classification: Mapping[str, object],
    impact_ids: set[str],
    expansion_ids: set[str],
    fact_dependency_edges: set[tuple[str, str, str]] | None,
    plan: Mapping[str, object],
    issues: list[ValidationIssue],
) -> set[str]:
    provenance = _sequence_or_issue(
        classification.get("subject-selection-provenance"),
        "$.classification.subject-selection-provenance",
        issues,
    )
    if provenance is None:
        return set()
    subjects = _sequence_or_issue(plan.get("subjects"), "$.subjects", issues)
    subject_ids = _record_ids(subjects, "subject-id")
    active_subject_ids = {
        str(subject.get("subject-id"))
        for subject in subjects or []
        if subject.get("activity-status") == "active"
        and subject.get("capability-class")
        in {"validation-only", "descriptor-backed"}
        and isinstance(subject.get("subject-id"), str)
    }
    active_selected_subject_ids = {
        str(subject.get("subject-id"))
        for subject in subjects or []
        if subject.get("activity-status") == "active"
        and subject.get("selection-status") == "selected"
        and subject.get("capability-class")
        in {"validation-only", "descriptor-backed"}
        and isinstance(subject.get("subject-id"), str)
    }
    provenance_subject_ids: set[str] = set()
    ids: set[str] = set()
    expected = sorted(
        provenance,
        key=lambda item: str(item.get("provenance-id")),
    )
    if provenance != expected:
        issues.append(
            ValidationIssue(
                "$.classification.subject-selection-provenance",
                "must be canonical",
            ),
        )
    for index, record in enumerate(provenance):
        path = f"$.classification.subject-selection-provenance[{index}]"
        if not isinstance(record, Mapping):
            issues.append(ValidationIssue(path, "must be an object"))
            continue
        provenance_id = _required_str(record, "provenance-id", issues)
        if provenance_id is not None:
            if provenance_id in ids:
                issues.append(
                    ValidationIssue(f"{path}.provenance-id", "duplicate"),
                )
            ids.add(provenance_id)
        subject_id = record.get("subject-id")
        if subject_id not in subject_ids:
            issues.append(ValidationIssue(f"{path}.subject-id", "must resolve"))
        elif subject_id not in active_selected_subject_ids:
            issues.append(
                ValidationIssue(
                    f"{path}.subject-id",
                    "must target active selected subject",
                ),
            )
        elif isinstance(subject_id, str):
            provenance_subject_ids.add(subject_id)
        selection_kind = record.get("selection-kind")
        if selection_kind not in _SUBJECT_SELECTION_KINDS:
            issues.append(
                ValidationIssue(f"{path}.selection-kind", "is invalid"),
            )
        _validate_source_impact_ids(
            record.get("source-impact-ids"),
            impact_ids,
            f"{path}.source-impact-ids",
            issues,
            allow_empty=(
                selection_kind == "scheduled-full"
                or plan.get("mode") == "scheduled_full"
            ),
        )
        broad_expansion_id = record.get("broad-expansion-id")
        if (
            broad_expansion_id is not None
            and broad_expansion_id not in expansion_ids
        ):
            issues.append(
                ValidationIssue(
                    f"{path}.broad-expansion-id",
                    "must resolve to broad expansion",
                ),
            )
        if not isinstance(record.get("scheduled-full-source"), bool):
            issues.append(
                ValidationIssue(
                    f"{path}.scheduled-full-source",
                    "must be bool",
                ),
            )
        _validate_selection_provenance_semantics(
            record,
            path,
            selection_kind,
            impact_ids,
            expansion_ids,
            classification,
            fact_dependency_edges,
            subject_ids,
            plan,
            issues,
        )
    if plan.get("verdict-intent") == "executable":
        _validate_project_scoped_direct_provenance(
            classification,
            provenance,
            active_selected_subject_ids,
            issues,
        )
        _validate_project_scoped_downstream_closure(
            classification,
            provenance,
            fact_dependency_edges,
            subject_ids,
            active_subject_ids,
            active_selected_subject_ids,
            plan,
            issues,
        )
        requires_global_scope = _requires_scheduled_full_equivalent_scope(plan)
        if plan.get("mode") == "scheduled_full" or requires_global_scope:
            active_not_selected = (
                active_subject_ids - active_selected_subject_ids
            )
            if active_not_selected:
                message = (
                    "scheduled-full-equivalent executable plans must select "
                    "every active subject"
                    if (
                        requires_global_scope
                        and plan.get("mode") != "scheduled_full"
                    )
                    else "scheduled-full plans must select every active subject"
                )
                issues.append(
                    ValidationIssue(
                        "$.subjects",
                        message,
                    ),
                )
            missing = active_subject_ids - provenance_subject_ids
        else:
            missing = active_selected_subject_ids - provenance_subject_ids
        if missing:
            issues.append(
                ValidationIssue(
                    "$.classification.subject-selection-provenance",
                    "must cover every active selected subject",
                ),
            )
    return ids


def _validate_project_scoped_direct_provenance(
    classification: Mapping[str, object],
    provenance: Sequence[object],
    active_selected_subject_ids: set[str],
    issues: list[ValidationIssue],
) -> None:
    project_impacts = _project_scoped_impact_subjects(classification)
    if not project_impacts:
        return
    missing_subject_ids: list[str] = []
    for subject_id in sorted(
        set(project_impacts) & active_selected_subject_ids
    ):
        expected_impact_ids = project_impacts[subject_id]
        if not _has_direct_project_provenance(
            provenance,
            subject_id,
            expected_impact_ids,
        ):
            missing_subject_ids.append(subject_id)
    if missing_subject_ids:
        issues.append(
            ValidationIssue(
                "$.classification.subject-selection-provenance",
                "project-scoped executable impacts must include direct "
                "provenance for every targeted active selected impact",
            ),
        )


def _has_direct_project_provenance(
    provenance: Sequence[object],
    subject_id: str,
    expected_impact_ids: set[str],
) -> bool:
    covered_impact_ids: set[str] = set()
    for record in provenance:
        if not isinstance(record, Mapping):
            continue
        if (
            record.get("subject-id") == subject_id
            and record.get("selection-kind") == "direct"
        ):
            covered_impact_ids.update(
                set(_provenance_source_impact_ids(record))
                & expected_impact_ids,
            )
    return expected_impact_ids <= covered_impact_ids


def _validate_project_scoped_downstream_closure(  # noqa: PLR0913
    classification: Mapping[str, object],
    provenance: Sequence[object],
    fact_dependency_edges: set[tuple[str, str, str]] | None,
    subject_ids: set[str],
    active_subject_ids: set[str],
    active_selected_subject_ids: set[str],
    plan: Mapping[str, object],
    issues: list[ValidationIssue],
) -> None:
    if (
        plan.get("mode") == "scheduled_full"
        or fact_dependency_edges is None
        or not fact_dependency_edges
    ):
        return
    project_impacts = _project_scoped_impact_subjects(classification)
    if not project_impacts:
        return
    roots = set(project_impacts)
    required_by_subject: dict[str, set[str]] = {}
    for root in sorted(roots & subject_ids):
        for subject_id in _downstream_subject_closure(
            root,
            fact_dependency_edges,
            subject_ids,
        ):
            if subject_id in active_subject_ids:
                required_by_subject.setdefault(subject_id, set()).add(root)
    if not required_by_subject:
        return
    missing_selection = set(required_by_subject) - active_selected_subject_ids
    if missing_selection:
        issues.append(
            ValidationIssue(
                "$.subjects",
                "project-scoped executable impacts must select every active "
                "downstream subject",
            ),
        )
    for subject_id in sorted(
        set(required_by_subject) & active_selected_subject_ids
    ):
        root_ids = required_by_subject[subject_id]
        if not _has_downstream_project_provenance(
            provenance,
            subject_id,
            root_ids,
            project_impacts,
            fact_dependency_edges,
        ):
            issues.append(
                ValidationIssue(
                    "$.classification.subject-selection-provenance",
                    "project-scoped executable impacts must include downstream "
                    "provenance for every active downstream subject",
                ),
            )


def _project_scoped_impact_subjects(
    classification: Mapping[str, object],
) -> dict[str, set[str]]:
    subjects: dict[str, set[str]] = {}
    impacts = classification.get("impacts")
    if not isinstance(impacts, Sequence) or isinstance(impacts, str | bytes):
        return subjects
    for impact in impacts:
        if (
            not isinstance(impact, Mapping)
            or impact.get("category") != "project-scoped"
        ):
            continue
        impact_id = impact.get("impact-id")
        target = impact.get("coverage-target")
        if (
            isinstance(impact_id, str)
            and isinstance(target, Mapping)
            and target.get("type") == "subject"
            and isinstance(target.get("id"), str)
        ):
            subjects.setdefault(str(target["id"]), set()).add(impact_id)
    return subjects


def _project_scoped_impact_subject_by_id(
    classification: Mapping[str, object],
) -> dict[str, str]:
    return {
        impact_id: subject_id
        for subject_id, impact_ids_for_subject in (
            _project_scoped_impact_subjects(classification).items()
        )
        for impact_id in impact_ids_for_subject
    }


def _downstream_subject_closure(
    root: str,
    fact_dependency_edges: set[tuple[str, str, str]],
    subject_ids: set[str],
) -> set[str]:
    graph: dict[str, set[str]] = {}
    for from_subject, to_subject, _relation in fact_dependency_edges:
        if from_subject in subject_ids and to_subject in subject_ids:
            graph.setdefault(to_subject, set()).add(from_subject)
    visited: set[str] = set()
    pending = list(graph.get(root, set()))
    while pending:
        subject_id = pending.pop()
        if subject_id == root or subject_id in visited:
            continue
        visited.add(subject_id)
        pending.extend(sorted(graph.get(subject_id, set()) - visited))
    return visited


def _has_downstream_project_provenance(
    provenance: Sequence[object],
    subject_id: str,
    root_ids: set[str],
    project_impacts: Mapping[str, set[str]],
    fact_dependency_edges: set[tuple[str, str, str]],
) -> bool:
    expected_impact_ids: set[str] = set()
    for root_id in root_ids:
        expected_impact_ids.update(project_impacts.get(root_id, set()))
    covered_impact_ids: set[str] = set()
    for record in provenance:
        if not isinstance(record, Mapping):
            continue
        direct_subject_id = record.get("direct-subject-id")
        if (
            record.get("subject-id") != subject_id
            or record.get("selection-kind") != "downstream"
            or not isinstance(direct_subject_id, str)
            or direct_subject_id not in root_ids
        ):
            continue
        record_impact_ids = set(
            _provenance_source_impact_ids(record)
        ) & project_impacts.get(direct_subject_id, set())
        if not record_impact_ids:
            continue
        edge_basis = record.get("dependency-edge-basis")
        if not isinstance(edge_basis, Sequence) or isinstance(
            edge_basis,
            str | bytes,
        ):
            continue
        basis_keys = {
            _edge_key(edge)
            for edge in edge_basis
            if isinstance(edge, Mapping)
            and _edge_key(edge) in fact_dependency_edges
        }
        if subject_id in _downstream_subject_closure(
            direct_subject_id,
            basis_keys,
            {direct_subject_id, subject_id}
            | {
                from_subject
                for from_subject, _to_subject, _relation in basis_keys
            }
            | {
                to_subject
                for _from_subject, to_subject, _relation in basis_keys
            },
        ):
            covered_impact_ids.update(record_impact_ids)
    return expected_impact_ids <= covered_impact_ids


def _validate_selection_provenance_semantics(  # noqa: C901, PLR0912, PLR0913, PLR0915
    record: Mapping[str, object],
    path: str,
    selection_kind: object,
    impact_ids: set[str],
    expansion_ids: set[str],
    classification: Mapping[str, object],
    fact_dependency_edges: set[tuple[str, str, str]] | None,
    subject_ids: set[str],
    plan: Mapping[str, object],
    issues: list[ValidationIssue],
) -> None:
    source_impact_ids = _provenance_source_impact_ids(record)
    direct_subject_id = record.get("direct-subject-id")
    broad_expansion_id = record.get("broad-expansion-id")
    scheduled_full_source = record.get("scheduled-full-source")
    edges = _validate_selection_dependency_edges(
        record.get("dependency-edge-basis"),
        path,
        subject_ids,
        fact_dependency_edges,
        issues,
    )
    if plan.get("mode") == "scheduled_full":
        _validate_scheduled_full_selection_provenance_shape(
            path=path,
            record=record,
            source_impact_ids=source_impact_ids,
            edges=edges,
            issues=issues,
        )
        return
    if selection_kind == "direct":
        if not source_impact_ids:
            issues.append(
                ValidationIssue(f"{path}.source-impact-ids", "is required"),
            )
        if direct_subject_id is not None:
            issues.append(
                ValidationIssue(f"{path}.direct-subject-id", "must be null"),
            )
        if edges:
            issues.append(
                ValidationIssue(
                    f"{path}.dependency-edge-basis",
                    "must be empty",
                ),
            )
        if broad_expansion_id is not None:
            issues.append(
                ValidationIssue(f"{path}.broad-expansion-id", "must be null"),
            )
        if scheduled_full_source is not False:
            issues.append(
                ValidationIssue(
                    f"{path}.scheduled-full-source",
                    "must be false",
                ),
            )
        project_impact_subjects = _project_scoped_impact_subject_by_id(
            classification,
        )
        for impact_id in source_impact_ids:
            target_subject_id = project_impact_subjects.get(impact_id)
            if (
                target_subject_id is not None
                and target_subject_id
                != record.get(
                    "subject-id",
                )
            ):
                issues.append(
                    ValidationIssue(
                        f"{path}.source-impact-ids",
                        "project-scoped impacts must target subject-id",
                    ),
                )
    elif selection_kind == "downstream":
        if direct_subject_id not in subject_ids:
            issues.append(
                ValidationIssue(f"{path}.direct-subject-id", "must resolve"),
            )
        if not edges:
            issues.append(
                ValidationIssue(
                    f"{path}.dependency-edge-basis",
                    "must be non-empty",
                ),
            )
        if broad_expansion_id is not None:
            issues.append(
                ValidationIssue(f"{path}.broad-expansion-id", "must be null"),
            )
        if scheduled_full_source is not False:
            issues.append(
                ValidationIssue(
                    f"{path}.scheduled-full-source",
                    "must be false",
                ),
            )
    elif selection_kind == "broad-expansion":
        if broad_expansion_id not in expansion_ids:
            issues.append(
                ValidationIssue(
                    f"{path}.broad-expansion-id",
                    "must resolve to broad expansion",
                ),
            )
        if direct_subject_id is not None:
            issues.append(
                ValidationIssue(f"{path}.direct-subject-id", "must be null"),
            )
        if edges:
            issues.append(
                ValidationIssue(
                    f"{path}.dependency-edge-basis",
                    "must be empty",
                ),
            )
        if scheduled_full_source is not False:
            issues.append(
                ValidationIssue(
                    f"{path}.scheduled-full-source",
                    "must be false",
                ),
            )
    elif selection_kind == "scheduled-full":
        if plan.get("mode") != "scheduled_full":
            issues.append(
                ValidationIssue(
                    f"{path}.selection-kind",
                    "requires scheduled mode",
                ),
            )
        if source_impact_ids:
            issues.append(
                ValidationIssue(f"{path}.source-impact-ids", "must be empty"),
            )
        if direct_subject_id is not None:
            issues.append(
                ValidationIssue(f"{path}.direct-subject-id", "must be null"),
            )
        if edges:
            issues.append(
                ValidationIssue(
                    f"{path}.dependency-edge-basis",
                    "must be empty",
                ),
            )
        if broad_expansion_id is not None:
            issues.append(
                ValidationIssue(f"{path}.broad-expansion-id", "must be null"),
            )
        if scheduled_full_source is not True:
            issues.append(
                ValidationIssue(
                    f"{path}.scheduled-full-source",
                    "must be true",
                ),
            )
    for impact_id in source_impact_ids:
        if impact_id not in impact_ids:
            issues.append(
                ValidationIssue(f"{path}.source-impact-ids", "must resolve"),
            )


def _validate_scheduled_full_selection_provenance_shape(
    *,
    path: str,
    record: Mapping[str, object],
    source_impact_ids: Sequence[str],
    edges: Sequence[Mapping[str, object]],
    issues: list[ValidationIssue],
) -> None:
    if record.get("selection-kind") != "scheduled-full":
        issues.append(
            ValidationIssue(
                f"{path}.selection-kind",
                "must be scheduled-full for scheduled mode",
            ),
        )
    if source_impact_ids:
        issues.append(
            ValidationIssue(f"{path}.source-impact-ids", "must be empty"),
        )
    if record.get("direct-subject-id") is not None:
        issues.append(
            ValidationIssue(f"{path}.direct-subject-id", "must be null"),
        )
    if record.get("broad-expansion-id") is not None:
        issues.append(
            ValidationIssue(f"{path}.broad-expansion-id", "must be null"),
        )
    if edges:
        issues.append(
            ValidationIssue(f"{path}.dependency-edge-basis", "must be empty"),
        )
    if record.get("scheduled-full-source") is not True:
        issues.append(
            ValidationIssue(f"{path}.scheduled-full-source", "must be true"),
        )


def _provenance_source_impact_ids(
    record: Mapping[str, object],
) -> list[str]:
    value = record.get("source-impact-ids")
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        return []
    return [item for item in value if isinstance(item, str)]


def _validate_selection_dependency_edges(
    value: object,
    path: str,
    subject_ids: set[str],
    fact_dependency_edges: set[tuple[str, str, str]] | None,
    issues: list[ValidationIssue],
) -> list[Mapping[str, object]]:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        issues.append(
            ValidationIssue(f"{path}.dependency-edge-basis", "must be array"),
        )
        return []
    edges: list[Mapping[str, object]] = []
    for index, item in enumerate(value):
        item_path = f"{path}.dependency-edge-basis[{index}]"
        if not isinstance(item, Mapping):
            issues.append(ValidationIssue(item_path, "must be an object"))
            continue
        edges.append(item)
        from_subject = item.get("from-subject-id")
        to_subject = item.get("to-subject-id")
        relation = item.get("relation")
        if from_subject not in subject_ids:
            issues.append(
                ValidationIssue(f"{item_path}.from-subject-id", "must resolve"),
            )
        if to_subject not in subject_ids:
            issues.append(
                ValidationIssue(f"{item_path}.to-subject-id", "must resolve"),
            )
        if relation not in _DEPENDENCY_RELATIONS:
            issues.append(
                ValidationIssue(f"{item_path}.relation", "is not registered"),
            )
        if (
            fact_dependency_edges is not None
            and _edge_key(item) not in fact_dependency_edges
        ):
            issues.append(
                ValidationIssue(item_path, "must resolve to fact snapshot"),
            )
    expected = sorted(
        edges,
        key=lambda item: (
            str(item.get("from-subject-id")),
            str(item.get("to-subject-id")),
            str(item.get("relation")),
        ),
    )
    if edges != expected or len({_edge_key(item) for item in edges}) != len(
        edges
    ):
        issues.append(
            ValidationIssue(
                f"{path}.dependency-edge-basis",
                "must be canonical and unique",
            ),
        )
    return edges


def _fact_snapshot_dependency_edge_keys(
    fact_snapshot: Mapping[str, object],
) -> set[tuple[str, str, str]]:
    providers = fact_snapshot.get("providers")
    if not isinstance(providers, Sequence) or isinstance(
        providers,
        str | bytes,
    ):
        return set()
    edge_keys: set[tuple[str, str, str]] = set()
    for provider in providers:
        if not isinstance(provider, Mapping):
            continue
        edges = provider.get("dependency-edges")
        if not isinstance(edges, Sequence) or isinstance(edges, str | bytes):
            continue
        for edge in edges:
            if isinstance(edge, Mapping):
                edge_keys.add(_edge_key(edge))
    return edge_keys


def _validate_subsumption_records(  # noqa: PLR0913
    classification: Mapping[str, object],
    impact_ids: set[str],
    expansion_ids: set[str],
    provenance_ids: set[str],
    plan: Mapping[str, object],
    issues: list[ValidationIssue],
) -> None:
    records = _sequence_or_issue(
        classification.get("subsumptions"),
        "$.classification.subsumptions",
        issues,
    )
    if records is None:
        return
    retained_namespaces = {
        "subject-selection-provenance": provenance_ids,
        "descriptor-obligation": _record_ids(
            plan.get("descriptor-obligations"),
            "descriptor-obligation-id",
        ),
        "validation-obligation": _record_ids(
            plan.get("validation-obligations"),
            "validation-obligation-id",
        ),
        "artifact-obligation": _record_ids(
            plan.get("artifact-obligations"),
            "artifact-obligation-id",
        ),
        "work-group": _record_ids(plan.get("work-groups"), "work-group-id"),
        "evidence-expectation": _record_ids(
            plan.get("evidence-expectations"),
            "evidence-expectation-id",
        ),
        "detail-profile": _record_ids(
            plan.get("detail-profiles"),
            "detail-profile-id",
        ),
    }
    seen: set[str] = set()
    expected = sorted(records, key=lambda item: str(item.get("subsumption-id")))
    if records != expected:
        issues.append(
            ValidationIssue(
                "$.classification.subsumptions",
                "must be canonical",
            ),
        )
    for index, record in enumerate(records):
        path = f"$.classification.subsumptions[{index}]"
        if not isinstance(record, Mapping):
            issues.append(ValidationIssue(path, "must be an object"))
            continue
        subsumption_id = _required_str(record, "subsumption-id", issues)
        if subsumption_id is not None:
            if subsumption_id in seen:
                issues.append(
                    ValidationIssue(f"{path}.subsumption-id", "duplicate"),
                )
            seen.add(subsumption_id)
        _validate_source_impact_ids(
            record.get("source-impact-ids"),
            impact_ids,
            f"{path}.source-impact-ids",
            issues,
        )
        _validate_string_refs(
            record.get("source-expansion-ids"),
            expansion_ids,
            f"{path}.source-expansion-ids",
            issues,
        )
        subsumed_kind = record.get("subsumed-kind")
        namespace = retained_namespaces.get(str(subsumed_kind))
        if namespace is None:
            issues.append(
                ValidationIssue(f"{path}.subsumed-kind", "is invalid"),
            )
            namespace = set()
        retained_id = record.get("retained-id")
        if retained_id not in namespace:
            issues.append(
                ValidationIssue(f"{path}.retained-id", "must resolve"),
            )
        reason = record.get("reason")
        if not isinstance(reason, str) or not reason:
            issues.append(ValidationIssue(f"{path}.reason", "is required"))
        _validate_string_array_refs(
            record.get("subsumed-candidate-ids"),
            f"{path}.subsumed-candidate-ids",
            issues,
            allow_empty=False,
        )


def _validate_source_impact_ids(
    value: object,
    impact_ids: set[str],
    path: str,
    issues: list[ValidationIssue],
    *,
    allow_empty: bool = True,
) -> None:
    resolved = _validate_string_array_refs(
        value,
        path,
        issues,
        allow_empty=allow_empty,
    )
    for item in resolved:
        if item not in impact_ids:
            issues.append(ValidationIssue(path, "must resolve to impact"))


def _validate_string_refs(
    value: object,
    allowed: set[str],
    path: str,
    issues: list[ValidationIssue],
) -> None:
    resolved = _validate_string_array_refs(
        value,
        path,
        issues,
        allow_empty=True,
    )
    for item in resolved:
        if item not in allowed:
            issues.append(ValidationIssue(path, "must resolve"))


def _validate_string_array_refs(
    value: object,
    path: str,
    issues: list[ValidationIssue],
    *,
    allow_empty: bool,
) -> list[str]:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        issues.append(ValidationIssue(path, "must be array"))
        return []
    result: list[str] = []
    for index, item in enumerate(value):
        if not isinstance(item, str) or item == "":
            issues.append(
                ValidationIssue(f"{path}[{index}]", "must be non-empty string"),
            )
            continue
        result.append(item)
    if (
        (not allow_empty and not result)
        or result != sorted(result)
        or len(result) != len(set(result))
    ):
        issues.append(ValidationIssue(path, "must be ordered uniquely"))
    return result


def _record_ids(value: object, key: str) -> set[str]:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        return set()
    return {
        str(item.get(key))
        for item in value
        if isinstance(item, Mapping) and isinstance(item.get(key), str)
    }


def _validate_detail_profiles(
    detail_profiles: Sequence[Mapping[str, object]],
) -> None:
    issues: list[ValidationIssue] = []
    profile_ids: set[str] = set()
    profiles = list(detail_profiles)
    expected = sorted(
        profiles,
        key=lambda item: str(item.get("detail-profile-id")),
    )
    if profiles != expected:
        issues.append(
            ValidationIssue("detail-profiles", "must be canonical"),
        )
    for index, profile in enumerate(profiles):
        path = f"detail-profiles[{index}]"
        profile_id = _required_str(profile, "detail-profile-id", issues)
        if profile_id is not None:
            if _PLAN_LOCAL_ID_RE.fullmatch(profile_id) is None:
                issues.append(
                    ValidationIssue(f"{path}.detail-profile-id", "invalid")
                )
            if profile_id in profile_ids:
                issues.append(
                    ValidationIssue(f"{path}.detail-profile-id", "duplicate")
                )
            profile_ids.add(profile_id)
        if profile.get("category") not in _DETAIL_PROFILE_CATEGORIES:
            issues.append(ValidationIssue(f"{path}.category", "is invalid"))
        _validate_coverage_target(
            profile.get("coverage-target"),
            f"{path}.coverage-target",
            issues,
            allowed_types=_DETAIL_PROFILE_COVERAGE_TARGET_TYPES,
        )
        _validate_subchecks(profile.get("required-subchecks"), path, issues)
    if issues:
        raise ContractValidationError(issues)


def _validate_subchecks(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        issues.append(
            ValidationIssue(f"{path}.required-subchecks", "must be array")
        )
        return
    subchecks: list[Mapping[str, object]] = []
    ids: set[str] = set()
    for index, item in enumerate(value):
        item_path = f"{path}.required-subchecks[{index}]"
        if not isinstance(item, Mapping):
            issues.append(ValidationIssue(item_path, "must be an object"))
            continue
        subchecks.append(item)
        subcheck_id = _required_str(item, "subcheck-id", issues)
        if subcheck_id is not None:
            normalized_subcheck_id = unicodedata.normalize("NFC", subcheck_id)
            if normalized_subcheck_id in ids:
                issues.append(
                    ValidationIssue(f"{item_path}.subcheck-id", "duplicate")
                )
            ids.add(normalized_subcheck_id)
        if item.get("check-kind") not in _SUBCHECK_KINDS:
            issues.append(
                ValidationIssue(f"{item_path}.check-kind", "is invalid")
            )
        if not isinstance(item.get("blocking"), bool):
            issues.append(
                ValidationIssue(f"{item_path}.blocking", "must be bool"),
            )
        description = item.get("description")
        if not isinstance(description, str) or description == "":
            issues.append(
                ValidationIssue(
                    f"{item_path}.description",
                    "must be non-empty",
                ),
            )
    expected = sorted(subchecks, key=lambda item: str(item.get("subcheck-id")))
    if not subchecks or subchecks != expected:
        issues.append(
            ValidationIssue(f"{path}.required-subchecks", "must be canonical"),
        )


def _validate_selector_variants(
    work_groups: Sequence[Mapping[str, object]],
    issues: list[ValidationIssue],
) -> None:
    groups: dict[tuple[str, bytes], list[Mapping[str, object]]] = {}
    for index, group in enumerate(work_groups):
        if group.get("kind") == "evidence-aggregation":
            continue
        try:
            coverage_target_key = canonical_json_bytes(
                group.get("coverage-target"),
            )
        except (TypeError, ValueError) as error:
            issues.append(
                ValidationIssue(
                    f"$.work-groups[{index}].coverage-target",
                    f"cannot canonicalize coverage target: {error}",
                ),
            )
            continue
        key = (
            str(group.get("kind")),
            coverage_target_key,
        )
        groups.setdefault(key, []).append(group)
    for duplicates in groups.values():
        if len(duplicates) < _MIN_DUPLICATE_TARGET_GROUPS:
            continue
        variants = [item.get("selector-variant") for item in duplicates]
        if any(not isinstance(item, str) or not item for item in variants):
            issues.append(
                ValidationIssue(
                    "$.work-groups.selector-variant",
                    "is required for duplicate targets",
                ),
            )
            continue
        if len(set(variants)) != len(variants):
            issues.append(
                ValidationIssue(
                    "$.work-groups.selector-variant",
                    "must be unique per duplicate target",
                ),
            )


def _validate_detail_profile_references(
    detail_profiles: object,
    work_groups: Sequence[Mapping[str, object]],
    evidence_expectations: Sequence[Mapping[str, object]],
    issues: list[ValidationIssue],
) -> None:
    profiles = _sequence_or_issue(
        detail_profiles,
        "$.detail-profiles",
        issues,
    )
    if profiles is None:
        return
    try:
        _validate_detail_profiles(profiles)
    except ContractValidationError as error:
        issues.extend(error.issues)
    profiles_by_id = {
        str(profile.get("detail-profile-id")): profile
        for profile in profiles
        if isinstance(profile.get("detail-profile-id"), str)
    }
    for reference in _detail_profile_refs(work_groups, evidence_expectations):
        profile_id = reference.profile_id
        if profile_id is None:
            if reference.category in _DETAIL_PROFILE_REQUIRED_CATEGORIES:
                issues.append(
                    ValidationIssue(
                        reference.path,
                        "must reference a detail-profile",
                    ),
                )
            continue
        profile = profiles_by_id.get(profile_id)
        if profile is None:
            issues.append(
                ValidationIssue(
                    reference.path,
                    "must resolve to one detail-profile definition",
                ),
            )
            continue
        if profile.get("category") != reference.category:
            issues.append(
                ValidationIssue(reference.path, "category does not match")
            )
        if profile.get("coverage-target") != reference.coverage_target:
            issues.append(
                ValidationIssue(
                    reference.path,
                    "coverage-target does not match",
                )
            )


@dataclass(frozen=True)
class _DetailProfileReference:
    profile_id: str | None
    category: object
    coverage_target: object
    path: str


def _detail_profile_refs(
    work_groups: Sequence[Mapping[str, object]],
    evidence_expectations: Sequence[Mapping[str, object]],
) -> list[_DetailProfileReference]:
    refs: list[_DetailProfileReference] = []
    for group in work_groups:
        expected = group.get("expected-evidence")
        if isinstance(expected, Mapping):
            profile = expected.get("detail-profile")
            refs.append(
                _DetailProfileReference(
                    profile if isinstance(profile, str) else None,
                    expected.get("category"),
                    group.get("coverage-target"),
                    "$.work-groups.expected-evidence.detail-profile",
                ),
            )
    for evidence in evidence_expectations:
        profile = evidence.get("detail-profile")
        refs.append(
            _DetailProfileReference(
                profile if isinstance(profile, str) else None,
                evidence.get("category"),
                evidence.get("coverage-target"),
                "$.evidence-expectations.detail-profile",
            ),
        )
    return refs


def _validate_coverage_target_resolution(  # noqa: PLR0913
    subjects_value: object,
    detail_profiles_value: object,
    work_groups: Sequence[Mapping[str, object]],
    sections: _BindingSections,
    fact_snapshot: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> None:
    subjects = _sequence_or_issue(subjects_value, "$.subjects", issues)
    if subjects is None:
        return
    subject_ids, ecosystems = _active_selected_subject_target_universe(subjects)
    descriptor_paths = (
        set(_fact_indexes(fact_snapshot).descriptors)
        if fact_snapshot is not None
        else set()
    )
    artifact_ids = {
        str(item.get("artifact-obligation-id"))
        for item in sections.artifact_obligations
        if isinstance(item.get("artifact-obligation-id"), str)
    }
    detail_profiles = _sequence_or_issue(
        detail_profiles_value,
        "$.detail-profiles",
        issues,
    )
    for path, target in _coverage_targets(
        work_groups,
        sections,
        detail_profiles or (),
    ):
        _validate_one_coverage_target_resolution(
            path,
            target,
            _CoverageTargetUniverse(
                subject_ids=subject_ids,
                ecosystems=ecosystems,
                descriptor_paths=descriptor_paths,
                artifact_ids=artifact_ids,
            ),
            issues,
        )


def _active_selected_subject_target_universe(
    subjects: Sequence[object],
) -> tuple[set[str], set[str]]:
    subject_ids: set[str] = set()
    ecosystems: set[str] = set()
    for subject in subjects:
        if not isinstance(subject, Mapping):
            continue
        subject_id = subject.get("subject-id")
        ecosystem = subject.get("ecosystem")
        if (
            subject.get("activity-status") != "active"
            or subject.get("selection-status") != "selected"
            or not isinstance(subject_id, str)
        ):
            continue
        subject_ids.add(subject_id)
        if ecosystem in _ECOSYSTEMS:
            ecosystems.add(str(ecosystem))
    return subject_ids, ecosystems


def _validate_impact_coverage_target_resolution(
    classification: Mapping[str, object],
    subjects_value: object,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(subjects_value, Sequence) or isinstance(
        subjects_value,
        str | bytes,
    ):
        return
    impacts = classification.get("impacts")
    if not isinstance(impacts, Sequence) or isinstance(impacts, str | bytes):
        return
    subject_ids, ecosystems = _active_selected_subject_target_universe(
        subjects_value,
    )
    universe = _CoverageTargetUniverse(
        subject_ids=subject_ids,
        ecosystems=ecosystems,
        descriptor_paths=set(),
        artifact_ids=set(),
    )
    for index, impact in enumerate(impacts):
        if not isinstance(impact, Mapping):
            continue
        _validate_one_coverage_target_resolution(
            f"$.classification.impacts[{index}].coverage-target",
            impact.get("coverage-target"),
            universe,
            issues,
        )


def _validate_selected_validation_only_coverage(  # noqa: PLR0913
    subjects: Sequence[Mapping[str, object]],
    work_groups: Sequence[Mapping[str, object]],
    evidence_expectations: Sequence[Mapping[str, object]],
    validation_obligations: Sequence[Mapping[str, object]],
    classification: object,
    issues: list[ValidationIssue],
) -> None:
    if (
        isinstance(classification, Mapping)
        and classification.get("lightweight-only") is True
    ):
        return
    evidence_by_work_group = {
        str(item.get("work-group-id")): item
        for item in evidence_expectations
        if isinstance(item.get("work-group-id"), str)
    }
    obligations_by_work_group = {
        str(item.get("work-group-id")): item
        for item in validation_obligations
        if isinstance(item.get("work-group-id"), str)
    }
    selected_validation_subjects = [
        subject
        for subject in subjects
        if subject.get("activity-status") == "active"
        and subject.get("selection-status") == "selected"
        and subject.get("capability-class") == "validation-only"
    ]
    subject_capabilities: dict[str, list[str]] = {}
    ecosystem_capabilities = _selected_active_ecosystem_capabilities(subjects)
    for subject in selected_validation_subjects:
        subject_id = subject.get("subject-id")
        ecosystem = subject.get("ecosystem")
        if not isinstance(subject_id, str) or ecosystem not in _ECOSYSTEMS:
            continue
        derived = _derived_validation_capabilities(subject)
        subject_capabilities[subject_id] = derived
    for subject in subjects:
        if (
            subject.get("activity-status") != "active"
            or subject.get("selection-status") != "selected"
            or subject.get("capability-class") != "validation-only"
        ):
            continue
        subject_id = subject.get("subject-id")
        ecosystem = subject.get("ecosystem")
        if not isinstance(subject_id, str) or ecosystem not in _ECOSYSTEMS:
            continue
        ecosystem_id = str(ecosystem)
        derived_capabilities = subject_capabilities.get(subject_id, [])
        if not derived_capabilities:
            issues.append(
                ValidationIssue(
                    "$.subjects",
                    "selected validation-only subjects need planned "
                    "capabilities",
                ),
            )
            continue
        ecosystem_derived_capabilities = [
            capability
            for capability in PLANNED_CAPABILITY_ORDER
            if capability in ecosystem_capabilities.get(ecosystem_id, set())
        ]
        expected_capabilities = {
            ("subject", subject_id): derived_capabilities,
            ("ecosystem", ecosystem_id): ecosystem_derived_capabilities,
        }
        accepted_targets = (
            {"type": "subject", "id": subject_id},
            {"type": "ecosystem", "id": ecosystem_id},
        )
        matching_groups = [
            group
            for group in work_groups
            if group.get("kind") == "ecosystem-gate"
            and group.get("ecosystem") == ecosystem_id
            and group.get("coverage-target") in accepted_targets
        ]
        if not matching_groups:
            issues.append(
                ValidationIssue(
                    "$.subjects",
                    "selected validation-only subjects need "
                    "ecosystem-gate work",
                ),
            )
            continue
        if not any(
            _has_validation_only_chain(
                group,
                evidence_by_work_group,
                obligations_by_work_group,
                accepted_targets,
                expected_capabilities,
            )
            for group in matching_groups
        ):
            issues.append(
                ValidationIssue(
                    "$.subjects",
                    "selected validation-only subjects need "
                    "ecosystem-gate chain",
                ),
            )


def _derived_validation_capabilities(
    subject: Mapping[str, object],
) -> list[str]:
    capabilities = subject.get("capabilities")
    if not isinstance(capabilities, Mapping):
        return []
    return [
        capability
        for capability in PLANNED_CAPABILITY_ORDER
        if capabilities.get(capability) is True
    ]


def _selected_active_ecosystem_capabilities(
    subjects: Sequence[Mapping[str, object]],
) -> dict[str, set[str]]:
    ecosystem_capabilities: dict[str, set[str]] = {}
    for subject in subjects:
        if (
            subject.get("activity-status") != "active"
            or subject.get("selection-status") != "selected"
            or subject.get("capability-class")
            not in {"validation-only", "descriptor-backed"}
        ):
            continue
        ecosystem = subject.get("ecosystem")
        if ecosystem not in _ECOSYSTEMS:
            continue
        ecosystem_capabilities.setdefault(str(ecosystem), set()).update(
            _derived_validation_capabilities(subject),
        )
    return ecosystem_capabilities


def _has_validation_only_chain(
    group: Mapping[str, object],
    evidence_by_work_group: Mapping[str, Mapping[str, object]],
    obligations_by_work_group: Mapping[str, Mapping[str, object]],
    accepted_targets: tuple[dict[str, str], dict[str, str]],
    expected_capabilities: Mapping[tuple[str, str], Sequence[str]],
) -> bool:
    work_group_id = group.get("work-group-id")
    if not isinstance(work_group_id, str):
        return False
    evidence = evidence_by_work_group.get(work_group_id)
    obligation = obligations_by_work_group.get(work_group_id)
    if evidence is None or obligation is None:
        return False
    evidence_id = evidence.get("evidence-expectation-id")
    target = group.get("coverage-target")
    if not isinstance(target, Mapping):
        return False
    target_type = target.get("type")
    target_id = target.get("id")
    if not isinstance(target_type, str) or not isinstance(target_id, str):
        return False
    capabilities = expected_capabilities.get((target_type, target_id), ())
    return (
        evidence.get("category") == "ecosystem-gate"
        and evidence.get("coverage-target") in accepted_targets
        and obligation.get("kind") == "ecosystem-gate"
        and obligation.get("coverage-target") in accepted_targets
        and obligation.get("expected-evidence-id") == evidence_id
        and _planned_capabilities_match(
            group.get("expected-evidence"),
            evidence.get("planned-capabilities"),
            capabilities,
        )
    )


def _validate_selected_descriptor_backed_coverage(  # noqa: PLR0913
    subjects: Sequence[Mapping[str, object]],
    work_groups: Sequence[Mapping[str, object]],
    evidence_expectations: Sequence[Mapping[str, object]],
    validation_obligations: Sequence[Mapping[str, object]],
    descriptor_obligations: Sequence[Mapping[str, object]],
    artifact_obligations: Sequence[Mapping[str, object]],
    fact_snapshot: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> None:
    evidence_by_work_group = {
        str(item.get("work-group-id")): item
        for item in evidence_expectations
        if isinstance(item.get("work-group-id"), str)
    }
    validation_by_work_group = {
        str(item.get("work-group-id")): item
        for item in validation_obligations
        if isinstance(item.get("work-group-id"), str)
    }
    descriptor_by_work_group = {
        str(item.get("work-group-id")): item
        for item in descriptor_obligations
        if isinstance(item.get("work-group-id"), str)
    }
    groups_by_id = {
        str(item.get("work-group-id")): item
        for item in work_groups
        if isinstance(item.get("work-group-id"), str)
    }
    catalog_profiles = (
        _target_catalog_profiles_by_descriptor(fact_snapshot)
        if fact_snapshot is not None
        else {}
    )
    ecosystem_capabilities = _selected_active_ecosystem_capabilities(subjects)
    for subject in subjects:
        if (
            subject.get("activity-status") != "active"
            or subject.get("selection-status") != "selected"
            or subject.get("capability-class") != "descriptor-backed"
        ):
            continue
        subject_id = subject.get("subject-id")
        descriptor = subject.get("descriptor")
        descriptor_path = (
            descriptor.get("path") if isinstance(descriptor, Mapping) else None
        )
        if not isinstance(subject_id, str) or not isinstance(
            descriptor_path,
            str,
        ):
            issues.append(
                ValidationIssue(
                    "$.subjects",
                    "selected descriptor-backed subjects need a descriptor",
                ),
            )
            continue
        ecosystem = subject.get("ecosystem")
        derived_capabilities = _derived_validation_capabilities(subject)
        capabilities = subject.get("capabilities")
        requires_artifact_chain = descriptor_path in catalog_profiles
        if (
            requires_artifact_chain
            and isinstance(capabilities, Mapping)
            and capabilities.get("release-shaped-artifacts") is not True
        ):
            issues.append(
                ValidationIssue(
                    "$.subjects",
                    "descriptor-backed artifact capability must match "
                    "target-catalog entries",
                ),
            )
        if derived_capabilities and ecosystem in _ECOSYSTEMS:
            ecosystem_id = str(ecosystem)
            accepted_targets = (
                {"type": "subject", "id": subject_id},
                {"type": "ecosystem", "id": ecosystem_id},
            )
            ecosystem_derived_capabilities = [
                capability
                for capability in PLANNED_CAPABILITY_ORDER
                if capability in ecosystem_capabilities.get(ecosystem_id, set())
            ]
            expected_capabilities = {
                ("subject", subject_id): derived_capabilities,
                ("ecosystem", ecosystem_id): ecosystem_derived_capabilities,
            }
            matching_groups = [
                group
                for group in work_groups
                if group.get("kind") == "ecosystem-gate"
                and group.get("ecosystem") == ecosystem_id
                and group.get("coverage-target") in accepted_targets
            ]
            if not matching_groups or not any(
                _has_validation_only_chain(
                    group,
                    evidence_by_work_group,
                    validation_by_work_group,
                    accepted_targets,
                    expected_capabilities,
                )
                for group in matching_groups
            ):
                issues.append(
                    ValidationIssue(
                        "$.subjects",
                        "selected descriptor-backed subjects need "
                        "ecosystem-gate chain",
                    ),
                )
        if not _has_descriptor_backed_descriptor_chain(
            descriptor_path,
            groups_by_id,
            evidence_by_work_group,
            descriptor_by_work_group,
        ):
            issues.append(
                ValidationIssue(
                    "$.subjects",
                    "selected descriptor-backed subjects need descriptor chain",
                ),
            )
        if (
            requires_artifact_chain
            or (
                isinstance(capabilities, Mapping)
                and capabilities.get("release-shaped-artifacts") is True
            )
        ) and not _has_descriptor_backed_artifact_chain(
            subject_id,
            descriptor_path,
            groups_by_id,
            evidence_by_work_group,
            validation_by_work_group,
            artifact_obligations,
        ):
            issues.append(
                ValidationIssue(
                    "$.subjects",
                    "selected descriptor-backed subjects need artifact chain",
                ),
            )


def _has_descriptor_backed_descriptor_chain(
    descriptor_path: str,
    groups_by_id: Mapping[str, Mapping[str, object]],
    evidence_by_work_group: Mapping[str, Mapping[str, object]],
    descriptor_by_work_group: Mapping[str, Mapping[str, object]],
) -> bool:
    expected_target = {"type": "descriptor", "id": descriptor_path}
    for work_group_id, descriptor in descriptor_by_work_group.items():
        group = groups_by_id.get(work_group_id)
        evidence = evidence_by_work_group.get(work_group_id)
        if (
            group is not None
            and evidence is not None
            and group.get("kind") == "descriptor-validation"
            and group.get("coverage-target") == expected_target
            and evidence.get("coverage-target") == expected_target
            and descriptor.get("coverage-target") == expected_target
            and descriptor.get("expected-evidence-id")
            == evidence.get("evidence-expectation-id")
        ):
            return True
    return False


def _has_descriptor_backed_artifact_chain(  # noqa: PLR0913
    subject_id: str,
    descriptor_path: str,
    groups_by_id: Mapping[str, Mapping[str, object]],
    evidence_by_work_group: Mapping[str, Mapping[str, object]],
    validation_by_work_group: Mapping[str, Mapping[str, object]],
    artifact_obligations: Sequence[Mapping[str, object]],
) -> bool:
    for artifact in artifact_obligations:
        if (
            artifact.get("subject-id") != subject_id
            or artifact.get("descriptor-path") != descriptor_path
        ):
            continue
        artifact_id = artifact.get("artifact-obligation-id")
        work_group_id = artifact.get("work-group-id")
        expected_target = {"type": "artifact-obligation", "id": artifact_id}
        group = groups_by_id.get(str(work_group_id))
        evidence = evidence_by_work_group.get(str(work_group_id))
        validation = validation_by_work_group.get(str(work_group_id))
        if (
            isinstance(artifact_id, str)
            and group is not None
            and evidence is not None
            and validation is not None
            and group.get("kind") == "release-shaped-artifact"
            and group.get("coverage-target") == expected_target
            and evidence.get("coverage-target") == expected_target
            and validation.get("coverage-target") == expected_target
            and artifact.get("expected-evidence-id")
            == evidence.get("evidence-expectation-id")
            and artifact.get("validation-obligation-id")
            == validation.get("validation-obligation-id")
            and validation.get("expected-evidence-id")
            == evidence.get("evidence-expectation-id")
        ):
            return True
    return False


def _planned_capabilities_match(
    expected_evidence: object,
    evidence_capabilities: object,
    capabilities: Sequence[str],
) -> bool:
    if not capabilities:
        return False
    if not isinstance(expected_evidence, Mapping):
        return False
    group_capabilities = expected_evidence.get("planned-capabilities")
    expected = list(capabilities)
    return group_capabilities == expected and evidence_capabilities == expected


def _coverage_targets(
    work_groups: Sequence[Mapping[str, object]],
    sections: _BindingSections,
    detail_profiles: Sequence[Mapping[str, object]] = (),
) -> list[tuple[str, object]]:
    result: list[tuple[str, object]] = []
    for index, group in enumerate(work_groups):
        result.append(
            (
                f"$.work-groups[{index}].coverage-target",
                group.get("coverage-target"),
            )
        )
    for index, evidence in enumerate(sections.evidence_expectations):
        result.append(
            (
                f"$.evidence-expectations[{index}].coverage-target",
                evidence.get("coverage-target"),
            ),
        )
    for index, obligation in enumerate(sections.validation_obligations):
        result.append(
            (
                f"$.validation-obligations[{index}].coverage-target",
                obligation.get("coverage-target"),
            ),
        )
    for index, obligation in enumerate(sections.descriptor_obligations):
        result.append(
            (
                f"$.descriptor-obligations[{index}].coverage-target",
                obligation.get("coverage-target"),
            ),
        )
    for index, profile in enumerate(detail_profiles):
        result.append(
            (
                f"$.detail-profiles[{index}].coverage-target",
                profile.get("coverage-target"),
            ),
        )
    return result


def _validate_one_coverage_target_resolution(
    path: str,
    target: object,
    universe: _CoverageTargetUniverse,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(target, Mapping):
        return
    target_type = target.get("type")
    target_id = target.get("id")
    if target_type == "subject" and target_id not in universe.subject_ids:
        issues.append(ValidationIssue(path, "subject target is unresolved"))
    if target_type == "ecosystem" and target_id not in universe.ecosystems:
        issues.append(ValidationIssue(path, "ecosystem target is unresolved"))
    if (
        target_type == "descriptor"
        and target_id not in universe.descriptor_paths
    ):
        issues.append(ValidationIssue(path, "descriptor target is unresolved"))
    if (
        target_type == "artifact-obligation"
        and target_id not in universe.artifact_ids
    ):
        issues.append(ValidationIssue(path, "artifact target is unresolved"))


def _validate_unsupported_subject_isolation(  # noqa: PLR0913
    subjects_value: object,
    sections: _BindingSections,
    work_groups: Sequence[Mapping[str, object]],
    classification: object,
    fact_snapshot: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(subjects_value, Sequence) or isinstance(
        subjects_value,
        str | bytes,
    ):
        return
    unsupported_ids = {
        str(subject.get("subject-id"))
        for subject in subjects_value
        if isinstance(subject, Mapping)
        and subject.get("ecosystem") not in _ECOSYSTEMS
        and isinstance(subject.get("subject-id"), str)
    }
    if not unsupported_ids:
        return
    for path, target in _coverage_targets(work_groups, sections):
        if (
            isinstance(target, Mapping)
            and target.get("type") == "subject"
            and target.get("id") in unsupported_ids
        ):
            issues.append(
                ValidationIssue(path, "must not target unsupported subject"),
            )
    for index, obligation in enumerate(sections.artifact_obligations):
        if obligation.get("subject-id") in unsupported_ids:
            issues.append(
                ValidationIssue(
                    f"$.artifact-obligations[{index}].subject-id",
                    "must not target unsupported subject",
                ),
            )
    if isinstance(classification, Mapping):
        _validate_unsupported_subject_classification_isolation(
            classification,
            unsupported_ids,
            issues,
        )
    if fact_snapshot is not None:
        _validate_unsupported_subject_fact_isolation(
            fact_snapshot,
            unsupported_ids,
            issues,
        )


def _validate_unsupported_subject_classification_isolation(  # noqa: C901
    classification: Mapping[str, object],
    unsupported_ids: set[str],
    issues: list[ValidationIssue],
) -> None:
    provenance = classification.get("subject-selection-provenance")
    if not isinstance(provenance, Sequence) or isinstance(
        provenance,
        str | bytes,
    ):
        return
    for index, record in enumerate(provenance):
        if not isinstance(record, Mapping):
            continue
        path = f"$.classification.subject-selection-provenance[{index}]"
        for key in ("subject-id", "direct-subject-id"):
            if record.get(key) in unsupported_ids:
                issues.append(
                    ValidationIssue(
                        f"{path}.{key}",
                        "must not reference unsupported subject",
                    ),
                )
        edges = record.get("dependency-edge-basis")
        if not isinstance(edges, Sequence) or isinstance(edges, str | bytes):
            continue
        for edge_index, edge in enumerate(edges):
            if not isinstance(edge, Mapping):
                continue
            edge_path = f"{path}.dependency-edge-basis[{edge_index}]"
            for key in ("from-subject-id", "to-subject-id"):
                if edge.get(key) in unsupported_ids:
                    issues.append(
                        ValidationIssue(
                            f"{edge_path}.{key}",
                            "must not reference unsupported subject",
                        ),
                    )


def _validate_unsupported_subject_fact_isolation(  # noqa: C901
    fact_snapshot: Mapping[str, object],
    unsupported_ids: set[str],
    issues: list[ValidationIssue],
) -> None:
    providers = fact_snapshot.get("providers")
    if not isinstance(providers, Sequence) or isinstance(
        providers,
        str | bytes,
    ):
        return
    for provider_index, provider in enumerate(providers):
        if not isinstance(provider, Mapping):
            continue
        provider_path = f"$.fact-snapshot.providers[{provider_index}]"
        subjects = provider.get("subjects")
        if isinstance(subjects, Sequence) and not isinstance(
            subjects,
            str | bytes,
        ):
            for subject_index, subject_id in enumerate(subjects):
                if subject_id in unsupported_ids:
                    issues.append(
                        ValidationIssue(
                            f"{provider_path}.subjects[{subject_index}]",
                            "must not include unsupported subject",
                        ),
                    )
        edges = provider.get("dependency-edges")
        if not isinstance(edges, Sequence) or isinstance(edges, str | bytes):
            continue
        for edge_index, edge in enumerate(edges):
            if not isinstance(edge, Mapping):
                continue
            edge_path = f"{provider_path}.dependency-edges[{edge_index}]"
            for key in ("from-subject-id", "to-subject-id"):
                if edge.get(key) in unsupported_ids:
                    issues.append(
                        ValidationIssue(
                            f"{edge_path}.{key}",
                            "must not reference unsupported subject",
                        ),
                    )


def _validate_executable_bindings(
    work_groups: Sequence[Mapping[str, object]],
    sections: _BindingSections,
    issues: list[ValidationIssue],
) -> None:
    executable_groups = [
        group
        for group in work_groups
        if group.get("kind") != "evidence-aggregation"
    ]
    evidence_by_work_group = _records_by_key(
        sections.evidence_expectations,
        "work-group-id",
        "$.evidence-expectations",
        issues,
    )
    obligations_by_work_group = _records_by_key(
        sections.validation_obligations,
        "work-group-id",
        "$.validation-obligations",
        issues,
    )
    descriptor_by_work_group = _records_by_key(
        sections.descriptor_obligations,
        "work-group-id",
        "$.descriptor-obligations",
        issues,
        ignore_null=True,
    )
    artifact_by_work_group = _records_by_key(
        sections.artifact_obligations,
        "work-group-id",
        "$.artifact-obligations",
        issues,
        ignore_null=True,
    )
    executable_ids = {
        str(group.get("work-group-id")) for group in executable_groups
    }
    _validate_orphan_binding_records(
        work_groups,
        sections,
        executable_ids,
        issues,
    )
    for group in executable_groups:
        _validate_one_executable_binding(
            group,
            _WorkGroupBindingRecords(
                evidence=evidence_by_work_group.get(
                    str(group.get("work-group-id")),
                    [],
                ),
                validation=obligations_by_work_group.get(
                    str(group.get("work-group-id")),
                    [],
                ),
                descriptor=descriptor_by_work_group.get(
                    str(group.get("work-group-id")),
                    [],
                ),
                artifact=artifact_by_work_group.get(
                    str(group.get("work-group-id")),
                    [],
                ),
            ),
            issues,
        )


def _validate_dependency_graph(
    work_groups: Sequence[Mapping[str, object]],
    issues: list[ValidationIssue],
) -> None:
    ids = {str(group.get("work-group-id")) for group in work_groups}
    terminal_ids = [
        str(group.get("work-group-id"))
        for group in work_groups
        if group.get("kind") == "evidence-aggregation"
    ]
    if len(terminal_ids) != 1:
        issues.append(
            ValidationIssue("$.work-groups", "must have one terminal group"),
        )
        return
    terminal_id = terminal_ids[0]
    graph = _dependency_graph(work_groups, ids, terminal_id, issues)
    if _has_cycle(graph):
        issues.append(
            ValidationIssue("$.work-groups.depends-on", "must be acyclic"),
        )
        return
    executable_ids = ids - {terminal_id}
    terminal_ancestors = _dependency_ancestors(terminal_id, graph)
    missing = executable_ids - terminal_ancestors
    if missing:
        issues.append(
            ValidationIssue(
                "$.work-groups.evidence-aggregation.depends-on",
                "must be downstream of every executable work group",
            ),
        )


def _dependency_graph(
    work_groups: Sequence[Mapping[str, object]],
    ids: set[str],
    terminal_id: str,
    issues: list[ValidationIssue],
) -> dict[str, set[str]]:
    graph: dict[str, set[str]] = {}
    for group in work_groups:
        work_group_id = str(group.get("work-group-id"))
        dependencies = set(_string_items(group.get("depends-on")))
        unresolved = dependencies - ids
        if unresolved:
            issues.append(
                ValidationIssue(
                    f"$.work-groups[{work_group_id}].depends-on",
                    "must resolve to known work groups",
                ),
            )
        if (
            group.get("kind") != "evidence-aggregation"
            and terminal_id in dependencies
        ):
            issues.append(
                ValidationIssue(
                    f"$.work-groups[{work_group_id}].depends-on",
                    "executable work groups must not depend on aggregation",
                ),
            )
        graph[work_group_id] = dependencies
    return graph


def _has_cycle(graph: Mapping[str, set[str]]) -> bool:
    visiting: set[str] = set()
    visited: set[str] = set()

    def visit(node: str) -> bool:
        if node in visiting:
            return True
        if node in visited:
            return False
        visiting.add(node)
        for dependency in graph.get(node, set()):
            if dependency in graph and visit(dependency):
                return True
        visiting.remove(node)
        visited.add(node)
        return False

    return any(visit(node) for node in graph)


def _dependency_ancestors(
    start: str,
    graph: Mapping[str, set[str]],
) -> set[str]:
    result: set[str] = set()
    stack = list(graph.get(start, set()))
    while stack:
        node = stack.pop()
        if node in result:
            continue
        result.add(node)
        stack.extend(graph.get(node, set()))
    return result


def _validate_one_executable_binding(
    group: Mapping[str, object],
    records: _WorkGroupBindingRecords,
    issues: list[ValidationIssue],
) -> None:
    work_group_id = str(group.get("work-group-id"))
    kind = group.get("kind")
    if len(records.evidence) != 1:
        issues.append(
            ValidationIssue(
                f"$.work-groups[{work_group_id}]",
                "must have exactly one evidence expectation",
            ),
        )
        return
    expected_validation_obligations = (
        0 if kind == "descriptor-validation" else 1
    )
    if len(records.validation) != expected_validation_obligations:
        issues.append(
            ValidationIssue(
                f"$.work-groups[{work_group_id}]",
                "must have expected validation obligation chain",
            ),
        )
        return
    evidence = records.evidence[0]
    expected_evidence = group.get("expected-evidence")
    if (
        isinstance(expected_evidence, Mapping)
        and expected_evidence.get("category") != kind
    ):
        issues.append(
            ValidationIssue("expected-evidence.category", "must match kind"),
        )
    if evidence.get("category") != kind:
        issues.append(ValidationIssue("evidence.category", "must match kind"))
    _validate_work_group_evidence_binding(group, evidence, issues)
    if records.validation:
        obligation = records.validation[0]
        expected_evidence_id = evidence.get("evidence-expectation-id")
        if obligation.get("expected-evidence-id") != expected_evidence_id:
            issues.append(
                ValidationIssue(
                    f"$.validation-obligations[{work_group_id}]",
                    "must reference the bound evidence expectation",
                ),
            )
        _validate_binding_fields(group, evidence, obligation, issues)
    _validate_kind_specific_obligation_chain(
        group,
        evidence,
        records,
        issues,
    )


def _validate_binding_fields(
    group: Mapping[str, object],
    evidence: Mapping[str, object],
    obligation: Mapping[str, object],
    issues: list[ValidationIssue],
) -> None:
    expected_evidence = group.get("expected-evidence")
    if not isinstance(expected_evidence, Mapping):
        issues.append(
            ValidationIssue("$.work-groups.expected-evidence", "invalid"),
        )
        return
    _validate_coverage_target(
        obligation.get("coverage-target"),
        "validation-obligation.coverage-target",
        issues,
    )
    comparisons = (
        (group.get("kind"), obligation.get("kind"), "kind"),
        (
            expected_evidence.get("category"),
            evidence.get("category"),
            "category",
        ),
        (evidence.get("category"), obligation.get("kind"), "category"),
        (
            group.get("coverage-target"),
            evidence.get("coverage-target"),
            "coverage-target",
        ),
        (
            evidence.get("coverage-target"),
            obligation.get("coverage-target"),
            "coverage-target",
        ),
        (
            expected_evidence.get("planned-capabilities"),
            evidence.get("planned-capabilities"),
            "planned-capabilities",
        ),
        (
            expected_evidence.get("detail-profile"),
            evidence.get("detail-profile"),
            "detail-profile",
        ),
    )
    for left, right, field in comparisons:
        if left != right:
            issues.append(ValidationIssue(field, "does not match binding"))
    if expected_evidence.get("required") is not True:
        issues.append(
            ValidationIssue("expected-evidence.required", "must be true"),
        )
    if evidence.get("required") is not True:
        issues.append(ValidationIssue("evidence.required", "must be true"))
    if evidence.get("blocking-if-missing") is not True:
        issues.append(
            ValidationIssue("evidence.blocking-if-missing", "must be true"),
        )
    if obligation.get("required") is not True:
        issues.append(ValidationIssue("obligation.required", "must be true"))
    if obligation.get("blocking") is not True:
        issues.append(ValidationIssue("obligation.blocking", "must be true"))


def _validate_work_group_evidence_binding(
    group: Mapping[str, object],
    evidence: Mapping[str, object],
    issues: list[ValidationIssue],
) -> None:
    expected_evidence = group.get("expected-evidence")
    if not isinstance(expected_evidence, Mapping):
        issues.append(
            ValidationIssue("$.work-groups.expected-evidence", "invalid"),
        )
        return
    comparisons = (
        (
            expected_evidence.get("category"),
            evidence.get("category"),
            "category",
        ),
        (
            group.get("coverage-target"),
            evidence.get("coverage-target"),
            "coverage-target",
        ),
        (
            expected_evidence.get("planned-capabilities"),
            evidence.get("planned-capabilities"),
            "planned-capabilities",
        ),
        (
            expected_evidence.get("detail-profile"),
            evidence.get("detail-profile"),
            "detail-profile",
        ),
    )
    for left, right, field in comparisons:
        if left != right:
            issues.append(ValidationIssue(field, "does not match binding"))
    if expected_evidence.get("required") is not True:
        issues.append(
            ValidationIssue("expected-evidence.required", "must be true"),
        )
    if evidence.get("required") is not True:
        issues.append(ValidationIssue("evidence.required", "must be true"))
    if evidence.get("blocking-if-missing") is not True:
        issues.append(
            ValidationIssue("evidence.blocking-if-missing", "must be true"),
        )


def _validate_kind_specific_obligation_chain(
    group: Mapping[str, object],
    evidence: Mapping[str, object],
    records: _WorkGroupBindingRecords,
    issues: list[ValidationIssue],
) -> None:
    kind = group.get("kind")
    if kind == "descriptor-validation":
        _validate_descriptor_obligation_chain(
            group,
            evidence,
            records.descriptor,
            issues,
        )
    elif records.descriptor:
        issues.append(
            ValidationIssue(
                "$.descriptor-obligations",
                "must bind only descriptor-validation work groups",
            ),
        )
    if kind == "release-shaped-artifact":
        _validate_artifact_obligation_chain(
            group,
            evidence,
            records.validation,
            records.artifact,
            issues,
        )
    elif records.artifact:
        issues.append(
            ValidationIssue(
                "$.artifact-obligations",
                "must bind only release-shaped-artifact work groups",
            ),
        )


def _validate_descriptor_obligation_chain(
    group: Mapping[str, object],
    evidence: Mapping[str, object],
    descriptor_records: Sequence[Mapping[str, object]],
    issues: list[ValidationIssue],
) -> None:
    work_group_id = str(group.get("work-group-id"))
    if len(descriptor_records) != 1:
        issues.append(
            ValidationIssue(
                f"$.work-groups[{work_group_id}]",
                "must have exactly one descriptor obligation",
            ),
        )
        return
    descriptor = descriptor_records[0]
    if descriptor.get("expected-evidence-id") != evidence.get(
        "evidence-expectation-id",
    ):
        issues.append(
            ValidationIssue(
                f"$.descriptor-obligations[{work_group_id}]",
                "must reference the bound evidence expectation",
            ),
        )
    _validate_obligation_common_binding(group, evidence, descriptor, issues)
    if descriptor.get("descriptor-scope") not in {
        "selected",
        "ecosystem",
        "all-discovered",
    }:
        issues.append(
            ValidationIssue(
                "descriptor-obligation.descriptor-scope",
                "invalid",
            ),
        )


def _validate_artifact_obligation_chain(
    group: Mapping[str, object],
    evidence: Mapping[str, object],
    validation_records: Sequence[Mapping[str, object]],
    artifact_records: Sequence[Mapping[str, object]],
    issues: list[ValidationIssue],
) -> None:
    work_group_id = str(group.get("work-group-id"))
    if len(artifact_records) != 1:
        issues.append(
            ValidationIssue(
                f"$.work-groups[{work_group_id}]",
                "must have exactly one artifact obligation",
            ),
        )
        return
    artifact = artifact_records[0]
    if artifact.get("expected-evidence-id") != evidence.get(
        "evidence-expectation-id",
    ):
        issues.append(
            ValidationIssue(
                f"$.artifact-obligations[{work_group_id}]",
                "must reference the bound evidence expectation",
            ),
        )
    if len(validation_records) != 1:
        return
    validation = validation_records[0]
    if artifact.get("validation-obligation-id") != validation.get(
        "validation-obligation-id",
    ):
        issues.append(
            ValidationIssue(
                f"$.artifact-obligations[{work_group_id}]",
                "must reference the bound validation obligation",
            ),
        )
    artifact_id = artifact.get("artifact-obligation-id")
    expected_target = {"type": "artifact-obligation", "id": artifact_id}
    if group.get("coverage-target") != expected_target:
        issues.append(
            ValidationIssue("artifact-obligation.coverage-target", "mismatch"),
        )
    if evidence.get("coverage-target") != expected_target:
        issues.append(
            ValidationIssue("artifact-obligation.evidence-target", "mismatch"),
        )
    if validation.get("coverage-target") != expected_target:
        issues.append(
            ValidationIssue(
                "artifact-obligation.validation-target",
                "mismatch",
            ),
        )
    _validate_required_blocking(artifact, "artifact-obligation", issues)


def _validate_obligation_common_binding(
    group: Mapping[str, object],
    evidence: Mapping[str, object],
    obligation: Mapping[str, object],
    issues: list[ValidationIssue],
) -> None:
    if obligation.get("coverage-target") != group.get("coverage-target"):
        issues.append(ValidationIssue("obligation.coverage-target", "mismatch"))
    if obligation.get("coverage-target") != evidence.get("coverage-target"):
        issues.append(ValidationIssue("obligation.evidence-target", "mismatch"))
    _validate_required_blocking(obligation, "obligation", issues)


def _validate_required_blocking(
    record: Mapping[str, object],
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if record.get("required") is not True:
        issues.append(ValidationIssue(f"{path}.required", "must be true"))
    if record.get("blocking") is not True:
        issues.append(ValidationIssue(f"{path}.blocking", "must be true"))


def _validate_orphan_binding_records(
    work_groups: Sequence[Mapping[str, object]],
    sections: _BindingSections,
    executable_ids: set[str],
    issues: list[ValidationIssue],
) -> None:
    kind_by_work_group = {
        str(item.get("work-group-id")): item.get("kind") for item in work_groups
    }
    evidence_ids = {
        str(item.get("evidence-expectation-id"))
        for item in sections.evidence_expectations
        if isinstance(item.get("evidence-expectation-id"), str)
    }
    validation_ids = {
        str(item.get("validation-obligation-id"))
        for item in sections.validation_obligations
        if isinstance(item.get("validation-obligation-id"), str)
    }
    for evidence in sections.evidence_expectations:
        work_group_id = evidence.get("work-group-id")
        if work_group_id not in executable_ids:
            issues.append(
                ValidationIssue(
                    "$.evidence-expectations",
                    "must bind to an executable work group",
                ),
            )
    for obligation in sections.validation_obligations:
        work_group_id = obligation.get("work-group-id")
        expected_evidence_id = obligation.get("expected-evidence-id")
        _validate_required_blocking(
            obligation,
            "$.validation-obligations",
            issues,
        )
        if work_group_id not in executable_ids:
            issues.append(
                ValidationIssue(
                    "$.validation-obligations",
                    "must bind to an executable work group",
                ),
            )
        if expected_evidence_id not in evidence_ids:
            issues.append(
                ValidationIssue(
                    "$.validation-obligations.expected-evidence-id",
                    "must resolve to evidence expectation",
                ),
            )
    _validate_orphan_special_obligations(
        sections.descriptor_obligations,
        sections.artifact_obligations,
        _SpecialObligationResolution(
            evidence_ids=evidence_ids,
            validation_ids=validation_ids,
            kind_by_work_group=kind_by_work_group,
        ),
        issues,
    )


def _validate_orphan_special_obligations(
    descriptor_obligations: Sequence[Mapping[str, object]],
    artifact_obligations: Sequence[Mapping[str, object]],
    resolution: _SpecialObligationResolution,
    issues: list[ValidationIssue],
) -> None:
    for obligation in descriptor_obligations:
        work_group_id = obligation.get("work-group-id")
        _validate_required_blocking(
            obligation,
            "$.descriptor-obligations",
            issues,
        )
        _validate_required_obligation_binding(
            obligation,
            "$.descriptor-obligations",
            issues,
        )
        if work_group_id is None:
            continue
        if (
            resolution.kind_by_work_group.get(str(work_group_id))
            != "descriptor-validation"
        ):
            issues.append(
                ValidationIssue(
                    "$.descriptor-obligations",
                    "must bind descriptor-validation work groups",
                ),
            )
        if (
            obligation.get("expected-evidence-id")
            not in resolution.evidence_ids
        ):
            issues.append(
                ValidationIssue(
                    "$.descriptor-obligations.expected-evidence-id",
                    "must resolve to evidence expectation",
                ),
            )
    for obligation in artifact_obligations:
        work_group_id = obligation.get("work-group-id")
        _validate_required_blocking(
            obligation,
            "$.artifact-obligations",
            issues,
        )
        _validate_required_obligation_binding(
            obligation,
            "$.artifact-obligations",
            issues,
        )
        if work_group_id is None:
            continue
        if (
            resolution.kind_by_work_group.get(str(work_group_id))
            != "release-shaped-artifact"
        ):
            issues.append(
                ValidationIssue(
                    "$.artifact-obligations",
                    "must bind release-shaped-artifact work groups",
                ),
            )
        if (
            obligation.get("expected-evidence-id")
            not in resolution.evidence_ids
        ):
            issues.append(
                ValidationIssue(
                    "$.artifact-obligations.expected-evidence-id",
                    "must resolve to evidence expectation",
                ),
            )
        _validate_artifact_validation_obligation_reference(
            obligation,
            resolution.validation_ids,
            issues,
        )


def _validate_artifact_validation_obligation_reference(
    obligation: Mapping[str, object],
    validation_ids: set[str],
    issues: list[ValidationIssue],
) -> None:
    validation_obligation_id = obligation.get("validation-obligation-id")
    if (
        not isinstance(validation_obligation_id, str)
        or validation_obligation_id == ""
    ):
        issues.append(
            ValidationIssue(
                "$.artifact-obligations.validation-obligation-id",
                "is required",
            ),
        )
    elif validation_obligation_id not in validation_ids:
        issues.append(
            ValidationIssue(
                "$.artifact-obligations.validation-obligation-id",
                "must resolve to validation obligation",
            ),
        )


def _validate_required_obligation_binding(
    obligation: Mapping[str, object],
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(obligation.get("work-group-id"), str) or (
        obligation.get("work-group-id") == ""
    ):
        issues.append(
            ValidationIssue(
                f"{path}.work-group-id",
                "is required for required obligations",
            ),
        )
    if not isinstance(obligation.get("expected-evidence-id"), str) or (
        obligation.get("expected-evidence-id") == ""
    ):
        issues.append(
            ValidationIssue(
                f"{path}.expected-evidence-id",
                "is required for required obligations",
            ),
        )


def _records_by_key(
    records: Sequence[Mapping[str, object]],
    key: str,
    path: str,
    issues: list[ValidationIssue],
    *,
    ignore_null: bool = False,
) -> dict[str, list[Mapping[str, object]]]:
    result: dict[str, list[Mapping[str, object]]] = {}
    for index, record in enumerate(records):
        value = record.get(key)
        if ignore_null and value is None:
            continue
        if not isinstance(value, str) or value == "":
            issues.append(
                ValidationIssue(f"{path}[{index}].{key}", "is required"),
            )
            continue
        result.setdefault(value, []).append(record)
    return result


def _mapping_or_issue(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> Mapping[str, object] | None:
    if not isinstance(value, Mapping):
        issues.append(ValidationIssue(path, "must be an object"))
        return None
    return value


def _sequence_or_issue(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> Sequence[Mapping[str, object]] | None:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        issues.append(ValidationIssue(path, "must be an array"))
        return None
    items: list[Mapping[str, object]] = []
    for index, item in enumerate(value):
        if not isinstance(item, Mapping):
            issues.append(
                ValidationIssue(f"{path}[{index}]", "must be an object"),
            )
            continue
        items.append(item)
    return items


def _required_sha(
    value: Mapping[str, object],
    key: str,
    path: str,
    issues: list[ValidationIssue],
) -> str | None:
    item = value.get(key)
    if not isinstance(item, str) or _SHA_RE.fullmatch(item) is None:
        issues.append(ValidationIssue(path, "must be a lowercase sha"))
        return None
    return item


def _nullable_sha(
    value: Mapping[str, object],
    key: str,
    path: str,
    issues: list[ValidationIssue],
) -> str | None:
    item = value.get(key)
    if item is None:
        return None
    if not isinstance(item, str) or _SHA_RE.fullmatch(item) is None:
        issues.append(ValidationIssue(path, "must be null or lowercase sha"))
        return None
    return item


def _required_digest(
    value: Mapping[str, object],
    key: str,
    path: str,
    issues: list[ValidationIssue],
) -> str | None:
    item = value.get(key)
    if not isinstance(item, str) or _DIGEST_RE.fullmatch(item) is None:
        issues.append(ValidationIssue(path, "must be a sha256 digest"))
        return None
    return item


def _snapshot_status(plan: Mapping[str, object], key: str) -> object:
    value = plan.get(key)
    if not isinstance(value, Mapping):
        return None
    return value.get("status")


def _nullable_str(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if value is not None and not isinstance(value, str):
        issues.append(ValidationIssue(path, "must be null or a string"))


def _validate_repo_relative_git_path(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(value, str) or value == "":
        issues.append(ValidationIssue(path, "must be non-empty string"))
        return
    if value.startswith(("/", "./")) or value.endswith("/") or "\\" in value:
        issues.append(
            ValidationIssue(path, "must be canonical repo-relative Git path"),
        )
        return
    segments = value.split("/")
    if any(segment in {"", ".", ".."} for segment in segments):
        issues.append(
            ValidationIssue(path, "must be canonical repo-relative Git path"),
        )


def _validate_repo_directory_root(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if value == ".":
        return
    _validate_repo_relative_git_path(value, path, issues)


def _string_items(value: object) -> list[str]:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        return []
    return [item for item in value if isinstance(item, str)]


def _edge_key(item: Mapping[str, object]) -> tuple[str, str, str]:
    return (
        str(item.get("from-subject-id")),
        str(item.get("to-subject-id")),
        str(item.get("relation")),
    )


def _nullable_sort_key(value: object) -> tuple[int, str]:
    if value is None:
        return (0, "")
    return (1, str(value))


def _descriptor_key(
    item: Mapping[str, object],
) -> tuple[str, tuple[int, str], tuple[int, str], str]:
    return (
        str(item.get("descriptor-path")),
        _nullable_sort_key(item.get("descriptor-identity")),
        _nullable_sort_key(item.get("owner-subject-id")),
        str(item.get("source")),
    )


def _target_catalog_entry_key(
    item: Mapping[str, object],
) -> tuple[object, ...]:
    artifact = item.get("artifact")
    release_receipt = item.get("release-receipt")
    if not isinstance(artifact, Mapping):
        artifact = {}
    if not isinstance(release_receipt, Mapping):
        release_receipt = {}
    return (
        str(item.get("descriptor-path")),
        str(item.get("profile")),
        str(artifact.get("kind-family")),
        str(artifact.get("concrete-kind")),
        str(artifact.get("logical-artifact-role")),
        tuple(_string_items(artifact.get("expected-artifact-refs"))),
        str(release_receipt.get("expected-family")),
        str(release_receipt.get("logical-receipt-role")),
        *_target_catalog_entry_variant_key_parts(artifact, release_receipt),
    )


def _artifact_obligation_catalog_key(
    obligation: Mapping[str, object],
    profile: str,
) -> tuple[object, ...]:
    artifact = obligation.get("artifact")
    release_receipt = obligation.get("release-receipt")
    if not isinstance(artifact, Mapping):
        artifact = {}
    if not isinstance(release_receipt, Mapping):
        release_receipt = {}
    return (
        str(obligation.get("descriptor-path")),
        profile,
        str(artifact.get("kind-family")),
        str(artifact.get("concrete-kind")),
        str(artifact.get("logical-artifact-role")),
        tuple(_string_items(artifact.get("expected-artifact-refs"))),
        str(release_receipt.get("expected-family")),
        str(release_receipt.get("logical-receipt-role")),
        *_target_catalog_entry_variant_key_parts(artifact, release_receipt),
    )


def _target_catalog_entry_variant_keys(
    item: Mapping[str, object],
    path: str,
    issues: list[ValidationIssue],
) -> tuple[bytes, bytes] | None:
    artifact = item.get("artifact")
    release_receipt = item.get("release-receipt")
    if not isinstance(artifact, Mapping):
        artifact = {}
    if not isinstance(release_receipt, Mapping):
        release_receipt = {}
    try:
        artifact_variant_key = canonical_json_bytes(
            artifact.get("variant-dimensions", {}),
        )
    except (TypeError, ValueError) as error:
        issues.append(
            ValidationIssue(
                f"{path}.artifact.variant-dimensions",
                "cannot canonicalize fact snapshot target catalog variant "
                f"dimensions: {error}",
            ),
        )
        return None
    try:
        release_receipt_variant_key = canonical_json_bytes(
            release_receipt.get("variant-dimensions", {}),
        )
    except (TypeError, ValueError) as error:
        issues.append(
            ValidationIssue(
                f"{path}.release-receipt.variant-dimensions",
                "cannot canonicalize fact snapshot target catalog variant "
                f"dimensions: {error}",
            ),
        )
        return None
    return (artifact_variant_key, release_receipt_variant_key)


def _target_catalog_entry_variant_key_parts(
    artifact: Mapping[str, object],
    release_receipt: Mapping[str, object],
) -> tuple[bytes, bytes]:
    return (
        canonical_json_bytes(artifact.get("variant-dimensions", {})),
        canonical_json_bytes(release_receipt.get("variant-dimensions", {})),
    )


def _sorted_records(
    records: Sequence[Mapping[str, object]],
    id_key: str,
    path: str,
) -> list[dict[str, object]]:
    if not isinstance(records, Sequence) or isinstance(records, str | bytes):
        raise ContractValidationError(
            [ValidationIssue(path, "must be an array")],
        )
    frozen: list[dict[str, object]] = []
    for index, record in enumerate(records):
        if not isinstance(record, Mapping):
            raise ContractValidationError(
                [ValidationIssue(f"{path}[{index}]", "must be an object")],
            )
        if not isinstance(record.get(id_key), str) or not record.get(id_key):
            raise ContractValidationError(
                [ValidationIssue(f"{path}[{index}].{id_key}", "is required")],
            )
        frozen.append(dict(record))
    sorted_records = sorted(frozen, key=lambda item: str(item[id_key]))
    ids = [str(item[id_key]) for item in sorted_records]
    if len(ids) != len(set(ids)) or frozen != sorted_records:
        raise ContractValidationError(
            [ValidationIssue(path, f"must be ordered uniquely by {id_key}")],
        )
    return sorted_records


def _sequence(value: object, path: str) -> Sequence[Mapping[str, object]]:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        raise ContractValidationError(
            [ValidationIssue(path, "must be an array")],
        )
    for index, item in enumerate(value):
        if not isinstance(item, Mapping):
            raise ContractValidationError(
                [ValidationIssue(f"{path}[{index}]", "must be an object")],
            )
    return [dict(item) for item in value]


def _sorted_unique_strings(values: Sequence[object], path: str) -> list[str]:
    strings: list[str] = []
    for index, value in enumerate(values):
        if not isinstance(value, str) or value == "":
            raise ContractValidationError(
                [
                    ValidationIssue(
                        f"{path}[{index}]",
                        "must be a non-empty string",
                    ),
                ],
            )
        strings.append(value)
    if strings != sorted(strings) or len(strings) != len(set(strings)):
        raise ContractValidationError(
            [ValidationIssue(path, "must be sorted and unique")],
        )
    return strings


def _required_str(
    value: Mapping[str, object],
    key: str,
    issues: list[ValidationIssue],
) -> str | None:
    item = value.get(key)
    if not isinstance(item, str) or item == "":
        issues.append(ValidationIssue(key, "must be a non-empty string"))
        return None
    return item
