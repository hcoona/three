"""Pure non-authoritative finalization of one immutable CI slice Plan."""

from __future__ import annotations

import re

from three_workflow_delivery_v3.ci.evidence import (
    admit_lane_result_for_plan,
    form_evidence_lane_result,
)
from three_workflow_delivery_v3.records.ci import (
    CiEvidence,
    CiLaneResult,
    CiObligationDisposition,
    CiQualificationSnapshot,
    CiSliceDecision,
    CiSliceSummary,
    ci_artifact_digest,
    ci_evidence_digest,
    ci_qualification_snapshot_digest,
    ci_slice_decision_digest,
    ci_slice_summary_text,
    derive_ci_failure,
    derive_ci_pr_slo,
)

_SHA_PATTERN = re.compile(r"[0-9a-f]{40}\Z")


def _plan_digest(plan: CiQualificationSnapshot) -> str:
    if type(plan) is not CiQualificationSnapshot:
        message = "plan must be an exact CiQualificationSnapshot"
        raise TypeError(message)
    return ci_qualification_snapshot_digest(plan)


def admit_planned_evidence(
    plan: CiQualificationSnapshot,
    evidence_records: tuple[CiEvidence, ...],
) -> tuple[CiEvidence, ...]:
    """Admit a conflict-free subset of exact selected Plan Evidence."""
    plan_digest = _plan_digest(plan)
    if type(evidence_records) is not tuple:
        message = "evidence_records must be an exact tuple"
        raise TypeError(message)

    planned_by_id = {
        obligation.obligation_id: obligation
        for obligation in plan.obligations
        if obligation.selected
    }
    admitted_by_id: dict[str, CiEvidence] = {}
    evidence_ids: set[str] = set()
    for evidence in evidence_records:
        if type(evidence) is not CiEvidence:
            message = "planned Evidence must be exact CiEvidence records"
            raise TypeError(message)
        ci_evidence_digest(evidence)
        form_evidence_lane_result(plan, evidence)
        obligation = planned_by_id.get(evidence.obligation.obligation_id)
        if obligation is None:
            message = "Finalizer received extra or unplanned Evidence"
            raise ValueError(message)
        if (
            evidence.plan_digest != plan_digest
            or evidence.candidate != plan.candidate
            or evidence.obligation != obligation
            or evidence.evidence_id != obligation.expected_evidence_id
            or evidence.producer != obligation.lane_id
            or evidence.workflow_run_id != plan.workflow_run_id
            or evidence.run_attempt != plan.run_attempt
        ):
            message = "Finalizer Evidence does not match the exact Plan binding"
            raise ValueError(message)
        if (
            evidence.evidence_id in evidence_ids
            or obligation.obligation_id in admitted_by_id
        ):
            message = "Finalizer received duplicate or conflicting Evidence"
            raise ValueError(message)
        evidence_ids.add(evidence.evidence_id)
        admitted_by_id[obligation.obligation_id] = evidence

    return tuple(
        admitted_by_id[obligation.obligation_id]
        for obligation in plan.obligations
        if obligation.obligation_id in admitted_by_id
    )


def derive_ci_supersession_state(
    plan: CiQualificationSnapshot,
    *,
    current_base_sha: str,
    current_head_sha: str,
    current_tested_merge_sha: str,
) -> str:
    """Compare the trusted current PR identity with the exact planned one."""
    _plan_digest(plan)
    if plan.candidate.event_kind != "pull_request":
        message = "supersession comparison requires a pull-request Plan"
        raise ValueError(message)
    current_identity = (
        current_base_sha,
        current_head_sha,
        current_tested_merge_sha,
    )
    if any(
        type(value) is not str or _SHA_PATTERN.fullmatch(value) is None
        for value in current_identity
    ):
        message = "current pull-request identity is unavailable"
        raise ValueError(message)
    planned_identity = (
        plan.candidate.base_sha,
        plan.candidate.head_sha,
        plan.candidate.tested_merge_sha,
    )
    return (
        "not-superseded"
        if current_identity == planned_identity
        else "superseded"
    )


def _explanation(
    dispositions: tuple[CiObligationDisposition, ...],
    terminal_result: str,
) -> str:
    if terminal_result == "success":
        return "all selected CI slice obligations were satisfied"
    incomplete = tuple(
        item.obligation.lane_id
        for item in dispositions
        if item.obligation.selected and item.outcome == "incomplete"
    )
    if incomplete:
        return "selected CI slice obligations are incomplete: " + ", ".join(
            incomplete
        )
    failed = tuple(
        f"{item.obligation.lane_id}={item.outcome}"
        for item in dispositions
        if item.obligation.selected and item.outcome != "satisfied"
    )
    if failed:
        return "selected CI slice obligations were not satisfied: " + ", ".join(
            failed
        )
    return "CI slice Plan was not ready for required work"


def finalize_ci_slice(  # noqa: C901, PLR0912, PLR0915
    plan: CiQualificationSnapshot,
    lane_results: tuple[CiLaneResult, ...],
    *,
    elapsed_seconds: int,
    supersession_state: str,
) -> CiSliceDecision:
    """Close every Plan obligation without executing or querying anything."""
    plan_digest = _plan_digest(plan)
    if type(elapsed_seconds) is not int or elapsed_seconds < 0:
        message = "elapsed_seconds must be an exact nonnegative integer"
        raise TypeError(message)
    pr_slo, pr_slo_reason = derive_ci_pr_slo(
        plan.candidate,
        plan.changed_paths,
        elapsed_seconds,
        supersession_state,
    )
    if type(lane_results) is not tuple:
        message = "lane_results must be an exact tuple"
        raise TypeError(message)

    admitted_results: dict[str, CiLaneResult] = {}
    for result in lane_results:
        admitted = admit_lane_result_for_plan(plan, result)
        if admitted.lane_id in admitted_results:
            message = "Finalizer received duplicate or conflicting lane results"
            raise ValueError(message)
        admitted_results[admitted.lane_id] = admitted

    for obligation in plan.obligations:
        if (
            not obligation.selected
            and obligation.lane_id not in admitted_results
        ):
            message = (
                "Finalizer requires an empty result for every unselected lane"
            )
            raise ValueError(message)

    evidence_records = tuple(
        result.evidence
        for obligation in plan.obligations
        if (
            (result := admitted_results.get(obligation.lane_id)) is not None
            and result.evidence is not None
        )
    )
    admitted_evidence = admit_planned_evidence(
        plan,
        evidence_records,
    )
    evidence_by_obligation = {
        evidence.obligation.obligation_id: evidence
        for evidence in admitted_evidence
    }

    dispositions: list[CiObligationDisposition] = []
    for obligation in plan.obligations:
        result = admitted_results.get(obligation.lane_id)
        evidence = evidence_by_obligation.get(obligation.obligation_id)
        if not obligation.selected:
            outcome = "empty"
            evidence_digests: tuple[str, ...] = ()
            explanation = f"{obligation.lane_id} was not selected"
        elif result is None or evidence is None:
            outcome = "incomplete"
            evidence_digests = ()
            explanation = (
                f"{obligation.lane_id} selected work did not emit Evidence"
            )
        else:
            outcome = result.disposition
            evidence_digests = (ci_evidence_digest(evidence),)
            explanation = f"{obligation.lane_id} {outcome}"
        dispositions.append(
            CiObligationDisposition(
                obligation=obligation,
                outcome=outcome,
                evidence_digests=evidence_digests,
                explanation=explanation,
            )
        )

    closed_dispositions = tuple(dispositions)
    selected_outcomes = tuple(
        item.outcome for item in closed_dispositions if item.obligation.selected
    )
    if "incomplete" in selected_outcomes:
        terminal_result = "incomplete"
    elif selected_outcomes and all(
        outcome == "satisfied" for outcome in selected_outcomes
    ):
        terminal_result = "success"
    else:
        terminal_result = "failure"
    explanation = _explanation(closed_dispositions, terminal_result)
    failure_class, next_action = derive_ci_failure(closed_dispositions)
    admitted_evidence_digests = tuple(
        digest
        for disposition in closed_dispositions
        for digest in disposition.evidence_digests
    )
    admitted_artifact_digests = tuple(
        ci_artifact_digest(artifact)
        for evidence in admitted_evidence
        for artifact in evidence.artifacts
    )
    supersession_reason = {
        "not-superseded": "trusted-current-candidate",
        "superseded": "trusted-superseded-candidate",
        "unsupported": "platform-proof-unavailable",
        "not-applicable": "not-pull-request",
    }.get(supersession_state)
    if supersession_reason is None:
        message = "supersession_state has an invalid closed value"
        raise ValueError(message)
    summary_text = ci_slice_summary_text(
        candidate=plan.candidate,
        repository_model_digest=plan.repository_model_digest,
        plan_digest=plan_digest,
        scope_mode=plan.scope_mode,
        changed_paths=plan.changed_paths,
        selected_project_nodes=plan.selected_project_nodes,
        selected_release_units=plan.selected_release_units,
        selected_variants=plan.selected_variants,
        selected_outputs=plan.selected_outputs,
        plan_diagnostics=plan.diagnostics,
        dispositions=closed_dispositions,
        evidence_digests=admitted_evidence_digests,
        artifact_digests=admitted_artifact_digests,
        explanation=explanation,
        terminal_result=terminal_result,
        failure_class=failure_class,
        next_action=next_action,
        elapsed_seconds=elapsed_seconds,
        supersession_state=supersession_state,
        supersession_reason=supersession_reason,
        pr_slo=pr_slo,
        pr_slo_reason=pr_slo_reason,
    )
    summary = CiSliceSummary(
        authority="non-authoritative",
        terminal_result=terminal_result,
        text=summary_text,
    )
    return CiSliceDecision(
        plan_digest=plan_digest,
        repository_model_digest=plan.repository_model_digest,
        candidate=plan.candidate,
        producer="required-finalizer",
        workflow_run_id=plan.workflow_run_id,
        run_attempt=plan.run_attempt,
        scope_mode=plan.scope_mode,
        changed_paths=plan.changed_paths,
        selected_project_nodes=plan.selected_project_nodes,
        selected_release_units=plan.selected_release_units,
        selected_variants=plan.selected_variants,
        selected_outputs=plan.selected_outputs,
        plan_diagnostics=plan.diagnostics,
        obligation_dispositions=closed_dispositions,
        admitted_evidence_digests=admitted_evidence_digests,
        admitted_artifact_digests=admitted_artifact_digests,
        explanation=explanation,
        terminal_result=terminal_result,
        failure_class=failure_class,
        next_action=next_action,
        authority="non-authoritative",
        elapsed_seconds=elapsed_seconds,
        supersession_state=supersession_state,
        supersession_reason=supersession_reason,
        pr_slo=pr_slo,
        pr_slo_reason=pr_slo_reason,
        summary=summary,
    )


def render_ci_slice_summary(decision: CiSliceDecision) -> str:
    """Render only the admitted Decision's explicit non-authoritative text."""
    if type(decision) is not CiSliceDecision:
        message = "decision must be an exact CiSliceDecision"
        raise TypeError(message)
    ci_slice_decision_digest(decision)
    return decision.summary.text


__all__ = [
    "admit_planned_evidence",
    "derive_ci_supersession_state",
    "finalize_ci_slice",
    "render_ci_slice_summary",
]
