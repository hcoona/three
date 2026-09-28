"""CI-owned request and verdict rules shared by formation and admission."""

from __future__ import annotations

from typing import TYPE_CHECKING

from three_workflow_delivery_v3.canonical import JsonValue, canonical_sha256
from three_workflow_delivery_v3.catalogs import QUALITY_DEFINITIONS

if TYPE_CHECKING:
    from three_workflow_delivery_v3.catalogs import QualityDefinition

CI_LANES: dict[str, tuple[str, tuple[str, ...]]] = {
    "root-hk": ("repository/source-tree-conformance-v1", ()),
    "project-build": ("node/project-build-v1", ()),
    "project-test": ("node/project-test-v1", ()),
    "npm-artifact-build": ("node/npm-artifact-v1", ()),
}
CI_LANE_IDS = tuple(CI_LANES)
CI_ROOT_HK_DEFINITION = CI_LANES["root-hk"][0]
_REQUIRED_OUTCOMES = {
    "success": "satisfied",
    "failure": "failed",
    "skipped": "skipped",
    "timed-out": "timed-out",
    "unknown": "unknown",
}
RAW_OUTCOMES = frozenset(_REQUIRED_OUTCOMES)
EVIDENCE_OUTCOMES = frozenset(_REQUIRED_OUTCOMES.values())
_SUPERSESSION_REASONS = {
    "not-superseded": "trusted-current-candidate",
    "superseded": "trusted-superseded-candidate",
    "unsupported": "platform-proof-unavailable",
    "not-applicable": "not-pull-request",
}
SUPERSESSION_STATES = frozenset(_SUPERSESSION_REASONS)
SUPERSESSION_REASONS = frozenset(_SUPERSESSION_REASONS.values())


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


def ci_definition_digest(definition_id: str) -> str:
    """Bind the CI projection of one catalog quality definition."""
    return canonical_sha256(
        _quality_definition_document(QUALITY_DEFINITIONS[definition_id])
    )


def ci_obligation_request_digest(  # noqa: PLR0913
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
    selected_outputs: tuple[tuple[str, str, str], ...],
) -> str:
    """Bind candidate, model, work definition and complete selected scope."""
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


def normalize_required_outcome(raw_outcome: str) -> str:
    """Normalize a closed required-work outcome without diagnostic input."""
    if type(raw_outcome) is not str:
        message = "required outcome must be an exact string"
        raise TypeError(message)
    normalized = _REQUIRED_OUTCOMES.get(raw_outcome)
    if normalized is None:
        message = "required outcome has an invalid closed value"
        raise ValueError(message)
    return normalized


def disposition_explanation(
    lane_id: str, *, selected: bool, outcome: str
) -> str:
    """Explain one selected or empty CI obligation."""
    if not selected:
        return f"{lane_id} was not selected"
    if outcome == "incomplete":
        return f"{lane_id} selected work did not emit Evidence"
    return f"{lane_id} {outcome}"


def terminal_result(selected_outcomes: tuple[str, ...]) -> str:
    """Derive the selected-work result, including absent Evidence."""
    if "incomplete" in selected_outcomes:
        return "incomplete"
    if selected_outcomes and all(
        outcome == "satisfied" for outcome in selected_outcomes
    ):
        return "success"
    return "failure"


def decision_explanation(
    selected_lane_outcomes: tuple[tuple[str, str], ...],
    terminal_result: str,
) -> str:
    """Explain a CI verdict in the admitted obligation order."""
    if terminal_result == "success":
        return "all selected CI slice obligations were satisfied"
    incomplete = tuple(
        lane_id
        for lane_id, outcome in selected_lane_outcomes
        if outcome == "incomplete"
    )
    if incomplete:
        return "selected CI slice obligations are incomplete: " + ", ".join(
            incomplete
        )
    failed = tuple(
        f"{lane_id}={outcome}"
        for lane_id, outcome in selected_lane_outcomes
        if outcome != "satisfied"
    )
    if failed:
        return "selected CI slice obligations were not satisfied: " + ", ".join(
            failed
        )
    return "CI slice Plan was not ready for required work"


def failure_action(selected_outcomes: tuple[str, ...]) -> tuple[str, str]:
    """Classify a CI failure and its permitted next action."""
    if not selected_outcomes:
        return "incomplete-model-plan", "fix-model-plan-and-rerun"
    if any(
        outcome in {"incomplete", "skipped", "timed-out", "unknown"}
        for outcome in selected_outcomes
    ):
        return "incomplete-qualification", "rerun-candidate"
    if "failed" in selected_outcomes:
        return "quality-failure", "fix-quality-failure-and-rerun"
    return "none", "none"


def supersession_reason(state: str) -> str:
    """Explain the already-derived supersession state."""
    reason = _SUPERSESSION_REASONS.get(state)
    if reason is None:
        message = "supersession_state has an invalid closed value"
        raise ValueError(message)
    return reason
