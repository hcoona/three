"""Selector-assignment contract helpers."""

from __future__ import annotations

import re
from collections.abc import Mapping, Sequence
from dataclasses import dataclass
from typing import Literal, cast

from three_workflow_release_contracts.actions_artifacts import (
    ArtifactAdmission,
    ArtifactGroups,
    admit_exactly_one_artifact,
)
from three_workflow_release_contracts.ci_validation import (
    API_VERSIONS_BY_KIND,
    CiValidationKind,
    CommonEnvelope,
    _validate_common_envelope_with_versions,
    canonical_json_digest,
    validate_artifact_logical_ref,
)
from three_workflow_release_contracts.ci_validation_plans import (
    ci_validation_plan_digest,
    validate_ci_validation_plan,
)
from three_workflow_release_contracts.contracts import (
    ContractValidationError,
    ValidationIssue,
)

_WriterIdentitySource = Literal["github-actions-job-context"]
type _ProducerBoundary = Literal["materialize-work-groups"]

_LOCAL_ID_RE = re.compile(r"^[a-z0-9][a-z0-9._-]{0,127}$")
_WRITER_ID_RE = re.compile(r"^github-actions-job:[0-9a-f]{64}$")
_DIGEST_RE = re.compile(r"^[0-9a-f]{64}$")
_WRITER_IDENTITY_API_VERSION = "three.ci.validation.writer-id/v1alpha1"
_WRITER_IDENTITY_SOURCE = "github-actions-job-context"
_LEGACY_WRITER_OBSERVATION_API_VERSION = (
    "three.ci.validation.writer-observation/v1alpha1"
)
_LEGACY_WRITER_OBSERVATION_KIND = "ci-validation-writer-observation"
_SELECTOR_ASSIGNMENTS_BOUNDARY = "materialize-work-groups"
_WRITER_OBSERVATION_BOUNDARY = "trusted-observation-boundary"
_SELECTOR_ASSIGNMENTS_KIND = "ci-validation-selector-assignments"
_SELECTOR_ASSIGNMENTS_API_VERSION = (
    "three.ci.validation.selector-assignments/v1alpha1"
)
_LEGACY_ENVELOPE_API_VERSIONS_BY_KIND = {
    _SELECTOR_ASSIGNMENTS_KIND: _SELECTOR_ASSIGNMENTS_API_VERSION,
}
type _LegacyProducerBoundary = Literal["trusted-observation-boundary"]
_EXECUTABLE_WORK_GROUP_KINDS = frozenset(
    {
        "lightweight-preflight",
        "ecosystem-gate",
        "descriptor-validation",
        "release-shaped-artifact",
        "workflow-release-tooling",
    },
)
_TERMINAL_WORK_GROUP_KIND = "evidence-aggregation"
_WORK_GROUP_KINDS = _EXECUTABLE_WORK_GROUP_KINDS | frozenset(
    {_TERMINAL_WORK_GROUP_KIND},
)

_SELECTOR_ASSIGNMENTS_ROOT_KEYS = frozenset(
    {
        "api-version",
        "kind",
        "created-at",
        "repository",
        "run",
        "schema-diagnostics",
        "plan-id",
        "plan-digest",
        "assignments",
    },
)
_SELECTOR_ASSIGNMENT_KEYS = frozenset(
    {
        "assignment-id",
        "work-group-id",
        "trusted-writer-id",
        "writer-identity-source",
        "receipt-artifact-ref",
    },
)
_WRITER_OBSERVATION_ROOT_KEYS = frozenset(
    {
        "api-version",
        "kind",
        "created-at",
        "repository",
        "run",
        "schema-diagnostics",
        "plan-id",
        "plan-digest",
        "assignment-id",
        "work-group-id",
        "receipt-artifact-ref",
        "artifact-instance-id",
        "writer-identity-source",
        "observed-writer-id",
    },
)


@dataclass(frozen=True, slots=True)
class _CiValidationArtifactProducerAuthority:
    """Non-payload producer-boundary verification for one artifact instance."""

    artifact_id: int
    boundary: _ProducerBoundary
    verified: bool

    def __post_init__(self) -> None:
        """Validate direct producer-authority construction."""
        issues: list[ValidationIssue] = []
        if not isinstance(self.artifact_id, int) or isinstance(
            self.artifact_id, bool
        ):
            issues.append(ValidationIssue("artifact-id", "must be an integer"))
        elif self.artifact_id < 1:
            issues.append(ValidationIssue("artifact-id", "must be >= 1"))
        if self.boundary not in {
            _SELECTOR_ASSIGNMENTS_BOUNDARY,
        }:
            issues.append(ValidationIssue("boundary", "is not registered"))
        if not isinstance(self.verified, bool):
            issues.append(ValidationIssue("verified", "must be a boolean"))
        if issues:
            raise ContractValidationError(issues)


@dataclass(frozen=True, slots=True)
class _LegacyCiValidationArtifactProducerAuthority:
    """Internal legacy producer verification for writer observations."""

    artifact_id: int
    boundary: _LegacyProducerBoundary
    verified: bool

    def __post_init__(self) -> None:
        issues: list[ValidationIssue] = []
        if not isinstance(self.artifact_id, int) or isinstance(
            self.artifact_id, bool
        ):
            issues.append(ValidationIssue("artifact-id", "must be an integer"))
        elif self.artifact_id < 1:
            issues.append(ValidationIssue("artifact-id", "must be >= 1"))
        if self.boundary != _WRITER_OBSERVATION_BOUNDARY:
            issues.append(ValidationIssue("boundary", "is not registered"))
        if not isinstance(self.verified, bool):
            issues.append(ValidationIssue("verified", "must be a boolean"))
        if issues:
            raise ContractValidationError(issues)


def _ci_validation_selector_assignments_artifact_ref(
    *,
    run_id: str,
    run_attempt: str,
) -> str:
    """Return the contract-owned selector-assignment manifest ref."""
    return (
        f"ci-validation/assignments/{run_id}/{run_attempt}/"
        "selector-assignments.json"
    )


def _ci_validation_receipt_artifact_ref(
    *,
    run_id: str,
    run_attempt: str,
    work_group_id: str,
) -> str:
    """Return the contract-owned receipt ref for one executable work group."""
    _validate_local_id_or_raise(work_group_id, "work-group-id")
    return (
        f"ci-validation/receipts/{run_id}/{run_attempt}/"
        f"{work_group_id}/receipt.json"
    )


def _legacy_ci_validation_writer_observation_artifact_ref(
    *,
    run_id: str,
    run_attempt: str,
    assignment_id: str,
) -> str:
    """Return the contract-owned writer-observation ref for one assignment."""
    _validate_local_id_or_raise(assignment_id, "assignment-id")
    return (
        f"ci-validation/writer-observations/{run_id}/{run_attempt}/"
        f"{assignment_id}.json"
    )


def _ci_validation_assignment_id(*, work_group_id: str) -> str:
    """Return the default stable assignment ID for a work-group selector."""
    _validate_local_id_or_raise(work_group_id, "work-group-id")
    return work_group_id


def ci_validation_writer_id(
    *,
    workflow: str,
    job: str,
    matrix: Mapping[str, object] | None = None,
) -> str:
    """Return the trusted/observed writer ID from control-plane job context."""
    issues: list[ValidationIssue] = []
    if not isinstance(workflow, str) or workflow == "":
        issues.append(ValidationIssue("workflow", "must be a string"))
    if not isinstance(job, str) or job == "":
        issues.append(ValidationIssue("job", "must be a string"))
    matrix_value: Mapping[str, object]
    if matrix is None:
        matrix_value = {}
    elif not isinstance(matrix, Mapping):
        issues.append(ValidationIssue("matrix", "must be an object"))
        matrix_value = {}
    else:
        matrix_value = matrix
        _validate_string_keys(
            cast("Mapping[object, object]", matrix_value),
            "matrix",
            issues,
        )
    if issues:
        raise ContractValidationError(issues)
    try:
        digest = canonical_json_digest(
            {
                "api-version": _WRITER_IDENTITY_API_VERSION,
                "workflow": workflow,
                "job": job,
                "matrix": dict(matrix_value),
            },
        )
    except (TypeError, ValueError) as error:
        raise ContractValidationError(
            [ValidationIssue("matrix", str(error))],
        ) from error
    return f"github-actions-job:{digest}"


def _freeze_ci_validation_selector_assignments(  # noqa: PLR0913
    *,
    plan: Mapping[str, object],
    trusted_writer_ids: Mapping[str, str],
    created_at: str,
    changed_files_snapshot: Mapping[str, object] | None = None,
    fact_snapshot: Mapping[str, object] | None = None,
    pull_request_merge_commit_verification: Mapping[str, object] | None = None,
) -> dict[str, object]:
    """Freeze the selector-assignment manifest for a validated plan."""
    validate_ci_validation_plan(
        plan,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        pull_request_merge_commit_verification=(
            pull_request_merge_commit_verification
        ),
    )
    issues: list[ValidationIssue] = []
    plan_envelope = _plan_envelope_or_collect(plan, issues)
    plan_id = _required_str(plan, "plan-id", "$.plan", issues)
    plan_digest = _verified_plan_digest_or_collect(plan, issues)
    executable_work_groups = _executable_work_groups(plan, "$.plan", issues)
    _validate_writer_mapping(
        trusted_writer_ids,
        executable_work_groups,
        issues,
    )
    if not isinstance(created_at, str) or created_at == "":
        issues.append(ValidationIssue("created-at", "must be a string"))
    if issues:
        raise ContractValidationError(issues)
    plan_envelope = cast("CommonEnvelope", plan_envelope)
    plan_id = cast("str", plan_id)
    plan_digest = cast("str", plan_digest)

    assignments = [
        _assignment_record(
            run_id=plan_envelope.run_id,
            run_attempt=plan_envelope.run_attempt,
            work_group_id=work_group_id,
            trusted_writer_id=trusted_writer_ids[work_group_id],
        )
        for work_group_id in sorted(executable_work_groups)
    ]
    return {
        "api-version": _SELECTOR_ASSIGNMENTS_API_VERSION,
        "kind": _SELECTOR_ASSIGNMENTS_KIND,
        "created-at": created_at,
        "repository": {
            "owner": plan_envelope.repository_owner,
            "name": plan_envelope.repository_name,
        },
        "run": {
            "workflow": plan_envelope.workflow,
            "run-id": plan_envelope.run_id,
            "run-attempt": plan_envelope.run_attempt,
        },
        "schema-diagnostics": [],
        "plan-id": plan_id,
        "plan-digest": plan_digest,
        "assignments": assignments,
    }


def _validate_ci_validation_selector_assignments(  # noqa: PLR0913
    manifest: object,
    *,
    plan: Mapping[str, object],
    changed_files_snapshot: Mapping[str, object] | None = None,
    fact_snapshot: Mapping[str, object] | None = None,
    pull_request_merge_commit_verification: Mapping[str, object] | None = None,
    expected_run_id: str | None = None,
    expected_run_attempt: str | None = None,
) -> None:
    """Validate selector assignments against the authoritative plan boundary."""
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
    issues: list[ValidationIssue] = []
    if not isinstance(manifest, Mapping):
        raise ContractValidationError(
            [ValidationIssue("$", "must be an object")],
        )
    envelope = _selector_assignments_envelope_or_collect(manifest, issues)
    _validate_root_keys(
        manifest,
        _SELECTOR_ASSIGNMENTS_ROOT_KEYS,
        "$",
        issues,
    )
    plan_envelope = _plan_envelope_or_collect(plan, issues)
    plan_id = _required_str(plan, "plan-id", "$.plan", issues)
    plan_digest = _verified_plan_digest_or_collect(plan, issues)
    executable_work_groups = _executable_work_groups(plan, "$.plan", issues)
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
        if plan_envelope is not None:
            _validate_envelope_matches_plan(envelope, plan_envelope, issues)
    if manifest.get("plan-id") != plan_id:
        issues.append(ValidationIssue("$.plan-id", "must match plan"))
    if manifest.get("plan-digest") != plan_digest:
        issues.append(ValidationIssue("$.plan-digest", "must match plan"))
    assignments = _sequence_or_issue(
        manifest.get("assignments"),
        "$.assignments",
        issues,
    )
    if assignments is not None and envelope is not None:
        _validate_assignments(
            assignments,
            run_id=envelope.run_id,
            run_attempt=envelope.run_attempt,
            executable_work_groups=executable_work_groups,
            issues=issues,
        )
    if issues:
        raise ContractValidationError(issues)


def _legacy_freeze_ci_validation_writer_observation(  # noqa: PLR0913
    *,
    plan: Mapping[str, object],
    selector_assignments_manifest: Mapping[str, object],
    assignment: Mapping[str, object],
    artifact_instance_id: str,
    observed_writer_id: str,
    created_at: str,
    changed_files_snapshot: Mapping[str, object] | None = None,
    fact_snapshot: Mapping[str, object] | None = None,
    pull_request_merge_commit_verification: Mapping[str, object] | None = None,
) -> dict[str, object]:
    """Freeze one trusted observation binding a receipt instance to a writer."""
    manifest_assignment = _validated_manifest_assignment(
        selector_assignments_manifest,
        assignment=assignment,
        plan=plan,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        pull_request_merge_commit_verification=(
            pull_request_merge_commit_verification
        ),
    )
    issues: list[ValidationIssue] = []
    plan_envelope = _plan_envelope_or_collect(plan, issues)
    plan_id = _required_str(plan, "plan-id", "$.plan", issues)
    plan_digest = _verified_plan_digest_or_collect(plan, issues)
    _validate_artifact_instance_id(
        artifact_instance_id,
        "artifact-instance-id",
        issues,
    )
    _validate_writer_id(observed_writer_id, "observed-writer-id", issues)
    if not isinstance(created_at, str) or created_at == "":
        issues.append(ValidationIssue("created-at", "must be a string"))
    if issues:
        raise ContractValidationError(issues)
    plan_envelope = cast("CommonEnvelope", plan_envelope)
    plan_id = cast("str", plan_id)
    plan_digest = cast("str", plan_digest)

    return {
        "api-version": _LEGACY_WRITER_OBSERVATION_API_VERSION,
        "kind": _LEGACY_WRITER_OBSERVATION_KIND,
        "created-at": created_at,
        "repository": {
            "owner": plan_envelope.repository_owner,
            "name": plan_envelope.repository_name,
        },
        "run": {
            "workflow": plan_envelope.workflow,
            "run-id": plan_envelope.run_id,
            "run-attempt": plan_envelope.run_attempt,
        },
        "schema-diagnostics": [],
        "plan-id": plan_id,
        "plan-digest": plan_digest,
        "assignment-id": manifest_assignment["assignment-id"],
        "work-group-id": manifest_assignment["work-group-id"],
        "receipt-artifact-ref": manifest_assignment["receipt-artifact-ref"],
        "artifact-instance-id": artifact_instance_id,
        "writer-identity-source": _WRITER_IDENTITY_SOURCE,
        "observed-writer-id": observed_writer_id,
    }


def _legacy_validate_ci_validation_writer_observation(  # noqa: PLR0913
    observation: object,
    *,
    plan: Mapping[str, object],
    selector_assignments_manifest: Mapping[str, object],
    assignment: Mapping[str, object],
    expected_artifact_instance_id: str,
    changed_files_snapshot: Mapping[str, object] | None = None,
    fact_snapshot: Mapping[str, object] | None = None,
    pull_request_merge_commit_verification: Mapping[str, object] | None = None,
) -> None:
    """Validate one writer observation without trusting receipt payload ID."""
    manifest_assignment = _validated_manifest_assignment(
        selector_assignments_manifest,
        assignment=assignment,
        plan=plan,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        pull_request_merge_commit_verification=(
            pull_request_merge_commit_verification
        ),
    )
    issues: list[ValidationIssue] = []
    if not isinstance(observation, Mapping):
        raise ContractValidationError(
            [ValidationIssue("$", "must be an object")],
        )
    envelope = _writer_observation_envelope_or_collect(observation, issues)
    _validate_root_keys(
        observation,
        _WRITER_OBSERVATION_ROOT_KEYS,
        "$",
        issues,
    )
    plan_envelope = _plan_envelope_or_collect(plan, issues)
    plan_id = _required_str(plan, "plan-id", "$.plan", issues)
    plan_digest = _verified_plan_digest_or_collect(plan, issues)
    if envelope is not None and plan_envelope is not None:
        _validate_envelope_matches_plan(envelope, plan_envelope, issues)
    if observation.get("plan-id") != plan_id:
        issues.append(ValidationIssue("$.plan-id", "must match plan"))
    if observation.get("plan-digest") != plan_digest:
        issues.append(ValidationIssue("$.plan-digest", "must match plan"))
    _validate_observation_assignment_binding(
        observation,
        manifest_assignment,
        issues,
    )
    artifact_instance_id = observation.get("artifact-instance-id")
    _validate_artifact_instance_id(
        artifact_instance_id,
        "$.artifact-instance-id",
        issues,
    )
    _validate_artifact_instance_id(
        expected_artifact_instance_id,
        "expected-artifact-instance-id",
        issues,
    )
    if artifact_instance_id != expected_artifact_instance_id:
        issues.append(
            ValidationIssue(
                "$.artifact-instance-id",
                "must match observed receipt artifact instance",
            ),
        )
    observed_writer_id = observation.get("observed-writer-id")
    _validate_writer_id(observed_writer_id, "$.observed-writer-id", issues)
    if observed_writer_id != manifest_assignment.get("trusted-writer-id"):
        issues.append(
            ValidationIssue(
                "$.observed-writer-id",
                "must match trusted writer identity",
            ),
        )
    if issues:
        raise ContractValidationError(issues)


def _admit_ci_validation_selector_assignments_artifact(
    artifact_groups: ArtifactGroups,
    *,
    run_id: str,
    run_attempt: str,
    producer_authority: _CiValidationArtifactProducerAuthority,
) -> ArtifactAdmission:
    """Admit one verified selector-assignment manifest artifact instance."""
    admission = admit_exactly_one_artifact(
        artifact_groups,
        logical_ref=_ci_validation_selector_assignments_artifact_ref(
            run_id=run_id,
            run_attempt=run_attempt,
        ),
    )
    _verify_producer_authority(
        admission,
        producer_authority,
        expected_boundary=_SELECTOR_ASSIGNMENTS_BOUNDARY,
    )
    return admission


def _legacy_admit_ci_validation_writer_observation_artifact(
    artifact_groups: ArtifactGroups,
    *,
    assignment: Mapping[str, object],
    producer_authority: _LegacyCiValidationArtifactProducerAuthority,
) -> ArtifactAdmission:
    """Admit one verified writer-observation artifact for an assignment."""
    observation_ref = assignment.get("writer-observation-ref")
    if not isinstance(observation_ref, str):
        raise ContractValidationError(
            [
                ValidationIssue(
                    "assignment.writer-observation-ref",
                    "must be a string",
                ),
            ],
        )
    admission = admit_exactly_one_artifact(
        artifact_groups,
        logical_ref=observation_ref,
    )
    _verify_producer_authority(
        admission,
        producer_authority,
        expected_boundary=_WRITER_OBSERVATION_BOUNDARY,
    )
    return admission


def _verify_producer_authority(
    admission: ArtifactAdmission,
    authority: (
        _CiValidationArtifactProducerAuthority
        | _LegacyCiValidationArtifactProducerAuthority
    ),
    *,
    expected_boundary: str,
) -> None:
    if not isinstance(
        authority,
        (
            _CiValidationArtifactProducerAuthority,
            _LegacyCiValidationArtifactProducerAuthority,
        ),
    ):
        raise ContractValidationError(
            [
                ValidationIssue(
                    "producer-authority",
                    "must be producer-boundary verification",
                ),
            ],
        )
    issues: list[ValidationIssue] = []
    if authority.artifact_id != admission.artifact.artifact_id:
        issues.append(
            ValidationIssue(
                "producer-authority.artifact-id",
                "must match admitted artifact instance",
            ),
        )
    if authority.boundary != expected_boundary:
        issues.append(
            ValidationIssue(
                "producer-authority.boundary",
                f"must be {expected_boundary}",
            ),
        )
    if not authority.verified:
        issues.append(
            ValidationIssue(
                "producer-authority.verified",
                "must be verified by non-payload boundary evidence",
            ),
        )
    if issues:
        raise ContractValidationError(issues)


def _assignment_record(
    *,
    run_id: str,
    run_attempt: str,
    work_group_id: str,
    trusted_writer_id: str,
) -> dict[str, object]:
    assignment_id = _ci_validation_assignment_id(work_group_id=work_group_id)
    return {
        "assignment-id": assignment_id,
        "work-group-id": work_group_id,
        "trusted-writer-id": trusted_writer_id,
        "writer-identity-source": _WRITER_IDENTITY_SOURCE,
        "receipt-artifact-ref": _ci_validation_receipt_artifact_ref(
            run_id=run_id,
            run_attempt=run_attempt,
            work_group_id=work_group_id,
        ),
    }


def _validated_manifest_assignment(  # noqa: PLR0913
    manifest: Mapping[str, object],
    *,
    assignment: Mapping[str, object],
    plan: Mapping[str, object],
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    pull_request_merge_commit_verification: Mapping[str, object] | None,
) -> Mapping[str, object]:
    _validate_ci_validation_selector_assignments(
        manifest,
        plan=plan,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        pull_request_merge_commit_verification=(
            pull_request_merge_commit_verification
        ),
    )
    issues: list[ValidationIssue] = []
    _validate_assignment_shape(assignment, "$.assignment", issues)
    assignments = _sequence_or_issue(
        manifest.get("assignments"),
        "$.selector-assignments.assignments",
        issues,
    )
    matched_assignment: Mapping[str, object] | None = None
    if assignments is not None:
        for index, manifest_assignment in enumerate(assignments):
            if not isinstance(manifest_assignment, Mapping):
                issues.append(
                    ValidationIssue(
                        f"$.selector-assignments.assignments[{index}]",
                        "must be an object",
                    ),
                )
                continue
            if manifest_assignment.get("assignment-id") == assignment.get(
                "assignment-id"
            ) and manifest_assignment.get("work-group-id") == assignment.get(
                "work-group-id"
            ):
                matched_assignment = manifest_assignment
                break
    if matched_assignment is None:
        issues.append(
            ValidationIssue(
                "$.assignment",
                "must match a validated selector-assignment manifest entry",
            ),
        )
    elif dict(matched_assignment) != dict(assignment):
        issues.append(
            ValidationIssue(
                "$.assignment",
                "must exactly match selector-assignment manifest entry",
            ),
        )
    if issues:
        raise ContractValidationError(issues)
    return cast("Mapping[str, object]", matched_assignment)


def _selector_assignments_envelope_or_collect(
    document: Mapping[str, object],
    issues: list[ValidationIssue],
) -> CommonEnvelope | None:
    try:
        return _validate_common_envelope_with_versions(
            document,
            api_version=_SELECTOR_ASSIGNMENTS_API_VERSION,
            kind=_SELECTOR_ASSIGNMENTS_KIND,
            extra_api_versions_by_kind=_LEGACY_ENVELOPE_API_VERSIONS_BY_KIND,
        )
    except ContractValidationError as error:
        issues.extend(error.issues)
        return None


def _writer_observation_envelope_or_collect(
    document: Mapping[str, object],
    issues: list[ValidationIssue],
) -> CommonEnvelope | None:
    envelope_issues: list[ValidationIssue] = []
    try:
        envelope = _validate_common_envelope_with_versions(
            {
                **document,
                "api-version": API_VERSIONS_BY_KIND[
                    CiValidationKind.REQUEST.value
                ],
                "kind": CiValidationKind.REQUEST.value,
            },
            api_version=API_VERSIONS_BY_KIND[CiValidationKind.REQUEST.value],
            kind=CiValidationKind.REQUEST,
        )
    except ContractValidationError as error:
        issues.extend(error.issues)
        return None
    if document.get("api-version") != _LEGACY_WRITER_OBSERVATION_API_VERSION:
        envelope_issues.append(
            ValidationIssue(
                "$.api-version",
                f"must be {_LEGACY_WRITER_OBSERVATION_API_VERSION}",
            )
        )
    if document.get("kind") != _LEGACY_WRITER_OBSERVATION_KIND:
        envelope_issues.append(
            ValidationIssue(
                "$.kind",
                f"must be {_LEGACY_WRITER_OBSERVATION_KIND}",
            )
        )
    if envelope_issues:
        issues.extend(envelope_issues)
        return None
    return CommonEnvelope(
        api_version=_LEGACY_WRITER_OBSERVATION_API_VERSION,
        kind=_LEGACY_WRITER_OBSERVATION_KIND,
        created_at=envelope.created_at,
        repository_owner=envelope.repository_owner,
        repository_name=envelope.repository_name,
        workflow=envelope.workflow,
        run_id=envelope.run_id,
        run_attempt=envelope.run_attempt,
    )


def _plan_envelope_or_collect(
    plan: Mapping[str, object],
    issues: list[ValidationIssue],
) -> CommonEnvelope | None:
    try:
        return _validate_common_envelope_with_versions(
            plan,
            api_version=API_VERSIONS_BY_KIND[CiValidationKind.PLAN.value],
            kind=CiValidationKind.PLAN,
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
            ValidationIssue("$.plan.plan-digest", "must be a SHA-256 digest"),
        )
        return None
    try:
        recomputed = ci_validation_plan_digest(plan)
    except (TypeError, ValueError) as error:
        issues.append(ValidationIssue("$.plan.plan-digest", str(error)))
        return None
    if plan_digest != recomputed:
        issues.append(
            ValidationIssue("$.plan.plan-digest", "must match canonical plan"),
        )
    return plan_digest


def _executable_work_groups(
    plan: Mapping[str, object],
    path: str,
    issues: list[ValidationIssue],
) -> set[str]:
    groups = _sequence_or_issue(
        plan.get("work-groups"), f"{path}.work-groups", issues
    )
    if groups is None:
        return set()
    result: set[str] = set()
    for index, group in enumerate(groups):
        item_path = f"{path}.work-groups[{index}]"
        if not isinstance(group, Mapping):
            issues.append(ValidationIssue(item_path, "must be an object"))
            continue
        work_group_id = group.get("work-group-id")
        kind = group.get("kind")
        if kind not in _WORK_GROUP_KINDS:
            issues.append(
                ValidationIssue(f"{item_path}.kind", "is not registered"),
            )
            continue
        if kind == _TERMINAL_WORK_GROUP_KIND:
            continue
        if not isinstance(work_group_id, str) or not _is_local_id(
            work_group_id
        ):
            issues.append(
                ValidationIssue(
                    f"{item_path}.work-group-id",
                    "must be path-safe",
                ),
            )
            continue
        if work_group_id in result:
            issues.append(
                ValidationIssue(
                    f"{item_path}.work-group-id",
                    "must be unique",
                ),
            )
        result.add(work_group_id)
    return result


def _validate_writer_mapping(
    trusted_writer_ids: Mapping[str, str],
    executable_work_groups: set[str],
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(trusted_writer_ids, Mapping):
        issues.append(
            ValidationIssue("trusted-writer-ids", "must be an object")
        )
        return
    provided_ids = set(trusted_writer_ids)
    missing = executable_work_groups - provided_ids
    extra = provided_ids - executable_work_groups
    for work_group_id in sorted(missing):
        issues.append(
            ValidationIssue(
                f"trusted-writer-ids.{work_group_id}",
                "is required for executable work group",
            ),
        )
    for work_group_id in sorted(extra):
        issues.append(
            ValidationIssue(
                f"trusted-writer-ids.{work_group_id}",
                "does not match an executable work group",
            ),
        )
    for work_group_id, writer_id in trusted_writer_ids.items():
        if not isinstance(work_group_id, str) or not _is_local_id(
            work_group_id
        ):
            issues.append(
                ValidationIssue(
                    "trusted-writer-ids",
                    "keys must be path-safe work-group IDs",
                ),
            )
        _validate_writer_id(
            writer_id,
            f"trusted-writer-ids.{work_group_id}",
            issues,
        )


def _validate_assignments(
    assignments: Sequence[object],
    *,
    run_id: str,
    run_attempt: str,
    executable_work_groups: set[str],
    issues: list[ValidationIssue],
) -> None:
    previous_work_group_id: str | None = None
    seen_work_groups: set[str] = set()
    seen_receipt_refs: set[str] = set()
    for index, assignment in enumerate(assignments):
        path = f"$.assignments[{index}]"
        if not isinstance(assignment, Mapping):
            issues.append(ValidationIssue(path, "must be an object"))
            continue
        _validate_assignment_shape(assignment, path, issues)
        work_group_id = assignment.get("work-group-id")
        if isinstance(work_group_id, str):
            if (
                previous_work_group_id is not None
                and previous_work_group_id > work_group_id
            ):
                issues.append(
                    ValidationIssue(
                        "$.assignments", "must be sorted by work-group-id"
                    ),
                )
            previous_work_group_id = work_group_id
            if work_group_id not in executable_work_groups:
                issues.append(
                    ValidationIssue(
                        f"{path}.work-group-id",
                        "must match an executable work group",
                    ),
                )
            _record_unique(
                seen_work_groups,
                work_group_id,
                f"{path}.work-group-id",
                issues,
            )
            expected_receipt_ref = _ci_validation_receipt_artifact_ref(
                run_id=run_id,
                run_attempt=run_attempt,
                work_group_id=work_group_id,
            )
            if assignment.get("receipt-artifact-ref") != expected_receipt_ref:
                issues.append(
                    ValidationIssue(
                        f"{path}.receipt-artifact-ref",
                        "must match work-group receipt ref",
                    ),
                )
            if assignment.get("assignment-id") != _ci_validation_assignment_id(
                work_group_id=work_group_id,
            ):
                issues.append(
                    ValidationIssue(
                        f"{path}.assignment-id",
                        "must match derived work-group assignment id",
                    ),
                )
        receipt_ref = assignment.get("receipt-artifact-ref")
        if isinstance(receipt_ref, str):
            _record_unique(
                seen_receipt_refs,
                receipt_ref,
                f"{path}.receipt-artifact-ref",
                issues,
            )
    if seen_work_groups != executable_work_groups:
        issues.append(
            ValidationIssue(
                "$.assignments",
                "must contain exactly one assignment per executable work group",
            ),
        )


def _validate_assignment_shape(
    assignment: Mapping[str, object],
    path: str,
    issues: list[ValidationIssue],
) -> None:
    _validate_root_keys(assignment, _SELECTOR_ASSIGNMENT_KEYS, path, issues)
    assignment_id = _required_str(assignment, "assignment-id", path, issues)
    if assignment_id is not None and not _is_local_id(assignment_id):
        issues.append(
            ValidationIssue(f"{path}.assignment-id", "must be path-safe")
        )
    work_group_id = _required_str(assignment, "work-group-id", path, issues)
    if work_group_id is not None and not _is_local_id(work_group_id):
        issues.append(
            ValidationIssue(f"{path}.work-group-id", "must be path-safe")
        )
    _validate_writer_id(
        assignment.get("trusted-writer-id"),
        f"{path}.trusted-writer-id",
        issues,
    )
    if assignment.get("writer-identity-source") != _WRITER_IDENTITY_SOURCE:
        issues.append(
            ValidationIssue(
                f"{path}.writer-identity-source",
                f"must be {_WRITER_IDENTITY_SOURCE}",
            ),
        )
    _validate_artifact_ref(
        assignment.get("receipt-artifact-ref"),
        f"{path}.receipt-artifact-ref",
        issues,
    )


def _validate_observation_assignment_binding(
    observation: Mapping[str, object],
    assignment: Mapping[str, object],
    issues: list[ValidationIssue],
) -> None:
    for key in (
        "assignment-id",
        "work-group-id",
        "receipt-artifact-ref",
        "writer-identity-source",
    ):
        if observation.get(key) != assignment.get(key):
            issues.append(ValidationIssue(f"$.{key}", "must match assignment"))


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
    for key in sorted(set(document) - allowed_keys):
        issues.append(ValidationIssue(f"{path}.{key}", "is not allowed"))
    for key in sorted(allowed_keys - set(document)):
        issues.append(ValidationIssue(f"{path}.{key}", "is required"))


def _sequence_or_issue(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> Sequence[object] | None:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        issues.append(ValidationIssue(path, "must be an array"))
        return None
    return value


def _required_str(
    document: Mapping[str, object],
    key: str,
    path: str,
    issues: list[ValidationIssue],
) -> str | None:
    value = document.get(key)
    if not isinstance(value, str) or value == "":
        issues.append(ValidationIssue(f"{path}.{key}", "must be a string"))
        return None
    return value


def _validate_local_id_or_raise(value: object, path: str) -> None:
    issues: list[ValidationIssue] = []
    if not isinstance(value, str) or not _is_local_id(value):
        issues.append(ValidationIssue(path, "must be path-safe"))
    if issues:
        raise ContractValidationError(issues)


def _is_local_id(value: str) -> bool:
    return _LOCAL_ID_RE.fullmatch(value) is not None


def _validate_writer_id(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(value, str) or _WRITER_ID_RE.fullmatch(value) is None:
        issues.append(
            ValidationIssue(
                path,
                "must be github-actions-job: followed by a SHA-256 digest",
            ),
        )


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


def _validate_artifact_instance_id(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(value, str) or value == "":
        issues.append(ValidationIssue(path, "must be a string"))


def _validate_string_keys(
    value: Mapping[object, object],
    path: str,
    issues: list[ValidationIssue],
) -> None:
    for key in value:
        if not isinstance(key, str):
            issues.append(ValidationIssue(path, "keys must be strings"))


def _record_unique(
    seen: set[str],
    value: str,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if value in seen:
        issues.append(ValidationIssue(path, "must be unique"))
    seen.add(value)
