"""Same-revision candidate formation and semantic CI slice planning."""

from __future__ import annotations

import re
from pathlib import PurePosixPath
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
)
from three_workflow_delivery_v3.catalogs import QUALITY_DEFINITIONS
from three_workflow_delivery_v3.ci.path_admission import (
    is_repository_only_path,
    is_static_reference_control_path,
)
from three_workflow_delivery_v3.records.ci import (
    CI_LANE_IDS,
    CI_WORKFLOW_PATH,
    CiCandidate,
    CiObligation,
    CiOutputIdentity,
    CiQualificationSnapshot,
    ci_candidate_digest,
)
from three_workflow_delivery_v3.repository.compiler import (
    CompiledBuild,
    CompiledOutput,
    CompiledQualitySelection,
    CompiledReleasePolicy,
    CompiledReleaseUnit,
    RepositoryModelSnapshot,
    validate_compilation_context,
    validate_compiled_release_policy,
    validate_first_slice_repository_model_snapshot,
)
from three_workflow_delivery_v3.repository.node_provider import (
    FIRST_SLICE_REQUIRED_GLOBAL_INPUTS,
    NbgvFacts,
    ProjectNode,
    node_provider_version_input_candidates,
)

if TYPE_CHECKING:
    from three_workflow_delivery_v3.catalogs import QualityDefinition

type ComparisonIdentity = tuple[str, str]

ROOT_HK_DEFINITION = "repository/source-tree-conformance-v1"

_SHA_PATTERN = re.compile(r"[0-9a-f]{40}\Z")
_DIGEST_PATTERN = re.compile(r"sha256:[0-9a-f]{64}\Z")
_COMPARISON_FIELD_COUNT = 2
_REVERSE_INDEX_FIELD_COUNT = 2
_LANE_DEFINITIONS = {
    "root-hk": ROOT_HK_DEFINITION,
    "project-build": "node/project-build-v1",
    "project-test": "node/project-test-v1",
    "npm-artifact-build": "node/npm-artifact-v1",
}
_LANE_PREREQUISITES = {
    "root-hk": (),
    "project-build": (),
    "project-test": (),
    "npm-artifact-build": (),
}
_V3_CONTROL_PREFIX = "src/public/lib/three-workflow-delivery-v3/"
_V3_ACTION_PREFIX = ".github/actions/workflow-delivery-v3"
_V3_SCRIPT_PREFIX = "eng/scripts/workflow_delivery_v3_"


def _require_comparison_identity(
    value: object,
    *,
    field: str,
) -> ComparisonIdentity:
    if type(value) is not tuple:
        message = f"{field} must be an exact tuple"
        raise TypeError(message)
    values = cast("tuple[object, ...]", value)
    if len(values) != _COMPARISON_FIELD_COUNT:
        message = f"{field} is unavailable"
        raise ValueError(message)
    base_sha, head_sha = values
    for index, sha in enumerate((base_sha, head_sha)):
        if type(sha) is not str or _SHA_PATTERN.fullmatch(sha) is None:
            message = f"{field}[{index}] is unavailable"
            raise ValueError(message)
    if base_sha == head_sha:
        message = f"{field} is conflicting"
        raise ValueError(message)
    return cast("ComparisonIdentity", values)


def _require_diagnostics(value: object) -> tuple[str, ...]:
    if type(value) is not tuple:
        message = "planner diagnostics must be an exact tuple"
        raise TypeError(message)
    diagnostics = cast("tuple[object, ...]", value)
    for diagnostic in diagnostics:
        if (
            type(diagnostic) is not str
            or not diagnostic
            or diagnostic != diagnostic.strip()
        ):
            message = "planner diagnostics must contain nonempty exact strings"
            raise TypeError(message)
    return cast("tuple[str, ...]", diagnostics)


def _require_repository_model_digest(value: object) -> str:
    if type(value) is not str or _DIGEST_PATTERN.fullmatch(value) is None:
        message = "repository_model_digest must be sha256:<64 lowercase hex>"
        raise ValueError(message)
    return value


def form_pull_request_candidate(  # noqa: PLR0913
    *,
    repository: str,
    request_id: str,
    workflow_run_id: int,
    run_attempt: int,
    selected_ref: str,
    base_sha: str,
    head_sha: str,
    tested_merge_sha: str,
    comparison_identity: ComparisonIdentity,
) -> CiCandidate:
    """Form one shadow PR candidate over the tested merge and exact range."""
    authoritative_comparison = _require_comparison_identity(
        comparison_identity,
        field="pull_request comparison identity",
    )
    if authoritative_comparison != (base_sha, head_sha):
        message = (
            "pull_request comparison identity conflicts with its base and head"
        )
        raise ValueError(message)
    return CiCandidate(
        event_kind="pull_request",
        purpose="ci-pr-slice-shadow",
        repository=repository,
        workflow_path=CI_WORKFLOW_PATH,
        workflow_sha=tested_merge_sha,
        request_id=request_id,
        producer="request",
        workflow_run_id=workflow_run_id,
        run_attempt=run_attempt,
        selected_ref=selected_ref,
        target=tested_merge_sha,
        base_sha=base_sha,
        head_sha=head_sha,
        tested_merge_sha=tested_merge_sha,
    )


def form_slice_validation_candidate(  # noqa: PLR0913
    *,
    repository: str,
    request_id: str,
    workflow_run_id: int,
    run_attempt: int,
    selected_ref: str,
    target: str,
) -> CiCandidate:
    """Form one scope-less manual candidate at the selected revision."""
    return CiCandidate(
        event_kind="workflow_dispatch",
        purpose="slice-validation",
        repository=repository,
        workflow_path=CI_WORKFLOW_PATH,
        workflow_sha=target,
        request_id=request_id,
        producer="request",
        workflow_run_id=workflow_run_id,
        run_attempt=run_attempt,
        selected_ref=selected_ref,
        target=target,
        base_sha=None,
        head_sha=None,
        tested_merge_sha=None,
    )


def _validate_snapshot_binding(  # noqa: C901, PLR0912, PLR0915
    candidate: CiCandidate,
    repository_model: RepositoryModelSnapshot,
    repository_model_digest: str,
) -> tuple[str, tuple[str, ...]]:
    ci_candidate_digest(candidate)
    trusted_digest = _require_repository_model_digest(repository_model_digest)
    if type(repository_model) is not RepositoryModelSnapshot:
        message = "Repository Model Snapshot has the wrong runtime type"
        raise TypeError(message)
    validate_compilation_context(repository_model.context)
    tuple_fields = (
        ("provider_result_digests", repository_model.provider_result_digests),
        ("project_nodes", repository_model.project_nodes),
        ("release_units", repository_model.release_units),
        ("quality", repository_model.quality),
        ("reverse_index", repository_model.reverse_index),
        ("unresolved", repository_model.unresolved),
    )
    for field, value in tuple_fields:
        if type(value) is not tuple:
            message = (
                f"Repository Model Snapshot {field} must be an exact tuple"
            )
            raise TypeError(message)
    if (
        type(repository_model.manifest_digest) is not str
        or _DIGEST_PATTERN.fullmatch(repository_model.manifest_digest) is None
    ):
        message = "Repository Model Snapshot manifest digest is malformed"
        raise ValueError(message)
    if any(
        type(digest) is not str or _DIGEST_PATTERN.fullmatch(digest) is None
        for digest in repository_model.provider_result_digests
    ):
        message = "Repository Model Snapshot Provider digest is malformed"
        raise ValueError(message)
    if any(
        type(project) is not ProjectNode
        for project in repository_model.project_nodes
    ):
        message = "Repository Model Snapshot Project Node is malformed"
        raise TypeError(message)
    for release_unit in repository_model.release_units:
        if type(release_unit) is not CompiledReleaseUnit:
            message = "Repository Model Snapshot Release Unit is malformed"
            raise TypeError(message)
        if type(release_unit.builds) is not tuple or any(
            type(build) is not CompiledBuild for build in release_unit.builds
        ):
            message = "Repository Model Snapshot Build closure is malformed"
            raise TypeError(message)
        for build in release_unit.builds:
            if type(build.outputs) is not tuple or any(
                type(output) is not CompiledOutput for output in build.outputs
            ):
                message = "Repository Model Snapshot output is malformed"
                raise TypeError(message)
    if any(
        type(selection) is not CompiledQualitySelection
        for selection in repository_model.quality
    ):
        message = "Repository Model Snapshot Quality selection is malformed"
        raise TypeError(message)
    if type(repository_model.release_policy_path) is not str:
        message = "Repository Model Snapshot Release policy path is malformed"
        raise TypeError(message)
    if repository_model.release_policy is not None:
        if type(repository_model.release_policy) is not CompiledReleasePolicy:
            message = "Repository Model Snapshot Release policy is malformed"
            raise TypeError(message)
        validate_compiled_release_policy(repository_model.release_policy)
    if type(repository_model.nbgv) is not NbgvFacts:
        message = "Repository Model Snapshot NBGV facts are malformed"
        raise TypeError(message)
    for entry in repository_model.reverse_index:
        if (
            type(entry) is not tuple
            or len(entry) != _REVERSE_INDEX_FIELD_COUNT
            or type(entry[0]) is not str
            or type(entry[1]) is not tuple
            or any(type(value) is not str for value in entry[1])
        ):
            message = "Repository Model Snapshot reverse index is malformed"
            raise TypeError(message)
    if type(repository_model.ready) is not bool:
        message = "Repository Model Snapshot ready flag is malformed"
        raise TypeError(message)
    if repository_model.ready and repository_model.release_policy is None:
        message = "ready Repository Model Snapshot lacks compiled policy"
        raise ValueError(message)
    if (
        not repository_model.ready
        and repository_model.release_policy is not None
    ):
        message = "incomplete Repository Model Snapshot claims compiled policy"
        raise ValueError(message)
    for unresolved in repository_model.unresolved:
        if (
            type(unresolved) is not str
            or not unresolved
            or unresolved != unresolved.strip()
        ):
            message = "Repository Model Snapshot unresolved fact is malformed"
            raise TypeError(message)
    actual_digest = repository_model.snapshot_digest
    if trusted_digest != actual_digest:
        message = "Repository Model Snapshot digest does not match"
        raise ValueError(message)

    context = repository_model.context
    if context.producer != "plan":
        message = "Repository Model Snapshot producer must be plan"
        raise ValueError(message)
    if context.target != candidate.target:
        message = "Repository Model Snapshot target does not match candidate"
        raise ValueError(message)
    if context.purpose != candidate.purpose:
        message = "Repository Model Snapshot purpose does not match candidate"
        raise ValueError(message)
    if context.request_id != candidate.request_id:
        message = "Repository Model Snapshot request does not match candidate"
        raise ValueError(message)
    if (
        context.workflow_run_id != candidate.workflow_run_id
        or context.run_attempt != candidate.run_attempt
    ):
        message = (
            "Repository Model Snapshot workflow run or attempt is not current"
        )
        raise ValueError(message)
    expected_control = f"workflow-delivery-v3:{candidate.workflow_sha}"
    if context.control != expected_control:
        message = "Repository Model Snapshot control does not match candidate"
        raise ValueError(message)
    if repository_model.ready:
        validate_first_slice_repository_model_snapshot(repository_model)
        return actual_digest, ()
    if not repository_model.unresolved:
        message = (
            "incomplete Repository Model Snapshot requires unresolved facts"
        )
        raise ValueError(message)
    return actual_digest, tuple(
        f"Repository Model Snapshot unresolved: {unresolved}"
        for unresolved in repository_model.unresolved
    )


def _normalize_changed_paths(value: object) -> tuple[str, ...]:
    if type(value) is not tuple:
        message = "incremental changed_paths must be an exact tuple"
        raise TypeError(message)
    paths = cast("tuple[object, ...]", value)
    accepted: list[str] = []
    for path in paths:
        if (
            type(path) is not str
            or not path
            or path != path.strip()
            or path.startswith("/")
            or path.endswith("/")
            or ".." in path.split("/")
            or PurePosixPath(path).as_posix() != path
        ):
            message = "incremental changed_paths contains an invalid path"
            raise ValueError(message)
        accepted.append(path)
    if len(set(accepted)) != len(accepted):
        message = "incremental changed_paths contains duplicate paths"
        raise ValueError(message)
    return tuple(sorted(accepted))


def _comparison_diagnostic(
    candidate: CiCandidate,
    comparison_identity: object,
) -> str | None:
    if comparison_identity is None:
        return "incremental comparison identity is unavailable"
    if type(comparison_identity) is not tuple:
        message = "incremental comparison identity must be an exact tuple"
        raise TypeError(message)
    values = cast("tuple[object, ...]", comparison_identity)
    if len(values) != _COMPARISON_FIELD_COUNT or any(
        type(value) is not str or _SHA_PATTERN.fullmatch(value) is None
        for value in values
    ):
        return "incremental comparison identity is unavailable"
    if values != (candidate.base_sha, candidate.head_sha):
        return "incremental comparison identity conflicts with candidate"
    return None


def _is_slice_affecting_path(  # noqa: PLR0911
    path: str,
    repository_model: RepositoryModelSnapshot,
) -> bool:
    if is_static_reference_control_path(path):
        return True
    project = repository_model.project_nodes[0]
    if path == project.path or path.startswith(f"{project.path}/"):
        return True
    release_unit = repository_model.release_units[0]
    if path == release_unit.descriptor_path:
        return True
    if any(path == selection.path for selection in repository_model.quality):
        return True
    policy_path = (
        repository_model.release_policy.path
        if repository_model.release_policy is not None
        else repository_model.release_policy_path
    )
    if path == policy_path:
        return True
    global_inputs = {
        *FIRST_SLICE_REQUIRED_GLOBAL_INPUTS,
        *node_provider_version_input_candidates(project.path),
        "mise.lock",
        "mise.toml",
    }
    if path in global_inputs:
        return True
    if path == CI_WORKFLOW_PATH:
        return True
    return path.startswith(
        (_V3_CONTROL_PREFIX, _V3_ACTION_PREFIX, _V3_SCRIPT_PREFIX)
    )


def _quality_definition_document(
    definition: QualityDefinition,
) -> dict[str, JsonValue]:
    capability_requirements: list[JsonValue] = list(
        definition.capability_requirements
    )
    return {
        "schema": "workflow-delivery/v3/quality-definition",
        "logical-id": definition.logical_id,
        "subject": definition.subject,
        "operation": definition.operation,
        "implementation-id": definition.implementation_id,
        "execution-class": definition.execution_class,
        "capability-requirements": capability_requirements,
    }


def _definition_digest(definition_id: str) -> str:
    return canonical_sha256(
        _quality_definition_document(QUALITY_DEFINITIONS[definition_id])
    )


def _obligation_request_digest(  # noqa: PLR0913
    *,
    candidate_digest: str,
    repository_model_digest: str,
    lane_id: str,
    definition_id: str,
    definition_digest: str,
    prerequisites: tuple[str, ...],
    selected: bool,
    scope_mode: str,
    changed_paths: tuple[str, ...],
    selected_project_nodes: tuple[str, ...],
    selected_release_units: tuple[str, ...],
    selected_variants: tuple[str, ...],
    selected_outputs: tuple[CiOutputIdentity, ...],
) -> str:
    changed_path_values: list[JsonValue] = list(changed_paths)
    prerequisite_values: list[JsonValue] = list(prerequisites)
    project_values: list[JsonValue] = list(selected_project_nodes)
    release_unit_values: list[JsonValue] = list(selected_release_units)
    variant_values: list[JsonValue] = list(selected_variants)
    output_values: list[JsonValue] = [
        {
            "output-id": output_id,
            "logical-role": logical_role,
            "media-kind": media_kind,
        }
        for output_id, logical_role, media_kind in selected_outputs
    ]
    return canonical_sha256(
        {
            "schema": "workflow-delivery/v3/ci-obligation-request",
            "candidate-digest": candidate_digest,
            "repository-model-digest": repository_model_digest,
            "lane-id": lane_id,
            "definition-id": definition_id,
            "definition-digest": definition_digest,
            "prerequisites": prerequisite_values,
            "selected": selected,
            "required": selected,
            "scope-mode": scope_mode,
            "changed-paths": changed_path_values,
            "selected-project-nodes": project_values,
            "selected-release-units": release_unit_values,
            "selected-variants": variant_values,
            "selected-outputs": output_values,
        }
    )


def _form_obligations(  # noqa: PLR0913
    *,
    candidate: CiCandidate,
    repository_model_digest: str,
    selected_lanes: tuple[str, ...],
    scope_mode: str,
    changed_paths: tuple[str, ...],
    selected_project_nodes: tuple[str, ...],
    selected_release_units: tuple[str, ...],
    selected_variants: tuple[str, ...],
    selected_outputs: tuple[CiOutputIdentity, ...],
) -> tuple[CiObligation, ...]:
    candidate_digest = ci_candidate_digest(candidate)
    obligations: list[CiObligation] = []
    for lane_id in CI_LANE_IDS:
        definition_id = _LANE_DEFINITIONS[lane_id]
        definition_digest = _definition_digest(definition_id)
        prerequisites = _LANE_PREREQUISITES[lane_id]
        selected = lane_id in selected_lanes
        request_digest = _obligation_request_digest(
            candidate_digest=candidate_digest,
            repository_model_digest=repository_model_digest,
            lane_id=lane_id,
            definition_id=definition_id,
            definition_digest=definition_digest,
            prerequisites=prerequisites,
            selected=selected,
            scope_mode=scope_mode,
            changed_paths=changed_paths,
            selected_project_nodes=selected_project_nodes,
            selected_release_units=selected_release_units,
            selected_variants=selected_variants,
            selected_outputs=selected_outputs,
        )
        obligations.append(
            CiObligation(
                obligation_id=f"ci:{lane_id}",
                lane_id=lane_id,
                request_digest=request_digest,
                definition_id=definition_id,
                definition_digest=definition_digest,
                prerequisites=prerequisites,
                selected=selected,
                required=selected,
                expected_evidence_id=(
                    f"evidence:{lane_id}:"
                    f"{request_digest.removeprefix('sha256:')}"
                ),
            )
        )
    return tuple(obligations)


def plan_ci_qualification(  # noqa: PLR0913
    candidate: CiCandidate,
    repository_model: RepositoryModelSnapshot,
    *,
    repository_model_digest: str,
    changed_paths: tuple[str, ...] | None = None,
    comparison_identity: ComparisonIdentity | None = None,
    diagnostics: tuple[str, ...] = (),
) -> CiQualificationSnapshot:
    """Close one required-only first-slice Plan without executor inference."""
    actual_model_digest, model_diagnostics = _validate_snapshot_binding(
        candidate,
        repository_model,
        repository_model_digest,
    )
    supplied_diagnostics = _require_diagnostics(diagnostics)

    ready = not model_diagnostics
    planner_diagnostics = list(model_diagnostics)
    if candidate.event_kind == "workflow_dispatch":
        if changed_paths is not None:
            message = "slice-validation rejects changed paths or selected scope"
            raise ValueError(message)
        if comparison_identity is not None:
            message = "slice-validation rejects synthetic comparison identity"
            raise ValueError(message)
        if any(
            "repository-wide" in diagnostic.lower()
            or "full validation" in diagnostic.lower()
            for diagnostic in supplied_diagnostics
        ):
            message = (
                "slice-validation diagnostics cannot claim repository-wide "
                "full validation"
            )
            raise ValueError(message)
        scope_mode = "slice-validation"
        normalized_paths: tuple[str, ...] = ()
        selected_lanes = CI_LANE_IDS if ready else ()
        if ready:
            planner_diagnostics.append(
                "slice-validation selected the complete first-slice scope "
                "without changed-path pruning"
            )
    else:
        normalized_paths = _normalize_changed_paths(changed_paths)
        scope_mode = "incremental"
        comparison_problem = _comparison_diagnostic(
            candidate,
            comparison_identity,
        )
        unclassified_paths = (
            ()
            if model_diagnostics
            else tuple(
                path
                for path in normalized_paths
                if not _is_slice_affecting_path(path, repository_model)
                and not is_repository_only_path(path)
            )
        )
        if (
            model_diagnostics
            or comparison_problem is not None
            or unclassified_paths
        ):
            ready = False
            selected_lanes = ()
            if comparison_problem is not None:
                planner_diagnostics.append(comparison_problem)
            planner_diagnostics.extend(
                f"changed path is unclassified: {path}"
                for path in unclassified_paths
            )
        elif any(
            _is_slice_affecting_path(path, repository_model)
            for path in normalized_paths
        ):
            selected_lanes = CI_LANE_IDS
            planner_diagnostics.append(
                "incremental comparison selected the complete affected "
                "first-slice scope"
            )
        else:
            selected_lanes = ("root-hk",)
            planner_diagnostics.append(
                "incremental comparison selected repository "
                "source-tree conformance only"
            )

    if ready and selected_lanes == CI_LANE_IDS:
        selected_project_nodes = tuple(
            project.project_id for project in repository_model.project_nodes
        )
        selected_release_units = tuple(
            unit.release_unit for unit in repository_model.release_units
        )
        selected_variants = tuple(
            build.build_id
            for unit in repository_model.release_units
            for build in unit.builds
        )
        selected_outputs = tuple(
            sorted(
                (
                    output.output_id,
                    output.role,
                    output.kind,
                )
                for unit in repository_model.release_units
                for build in unit.builds
                for output in build.outputs
            )
        )
    else:
        selected_project_nodes = ()
        selected_release_units = ()
        selected_variants = ()
        selected_outputs = ()

    obligations = _form_obligations(
        candidate=candidate,
        repository_model_digest=actual_model_digest,
        selected_lanes=selected_lanes,
        scope_mode=scope_mode,
        changed_paths=normalized_paths,
        selected_project_nodes=selected_project_nodes,
        selected_release_units=selected_release_units,
        selected_variants=selected_variants,
        selected_outputs=selected_outputs,
    )
    root_hk_definition_digest = _definition_digest(ROOT_HK_DEFINITION)
    return CiQualificationSnapshot(
        candidate=candidate,
        producer="plan",
        workflow_run_id=candidate.workflow_run_id,
        run_attempt=candidate.run_attempt,
        repository_model_digest=actual_model_digest,
        root_hk_definition=ROOT_HK_DEFINITION,
        root_hk_definition_digest=root_hk_definition_digest,
        scope_mode=scope_mode,
        changed_paths=normalized_paths,
        selected_project_nodes=selected_project_nodes,
        selected_release_units=selected_release_units,
        selected_variants=selected_variants,
        selected_outputs=selected_outputs,
        obligations=obligations,
        expected_evidence_ids=tuple(
            obligation.expected_evidence_id
            for obligation in obligations
            if obligation.selected
        ),
        ready=ready,
        diagnostics=(*planner_diagnostics, *supplied_diagnostics),
    )
