"""Mechanical Evidence and static-lane result formation for CI."""

from __future__ import annotations

from typing import cast

from three_workflow_delivery_v3.records.ci import (
    CiArtifact,
    CiEvidence,
    CiLaneResult,
    CiObligation,
    CiQualificationSnapshot,
    ci_evidence_digest,
    ci_lane_result_digest,
    ci_qualification_snapshot_digest,
)

_REQUIRED_OUTCOMES = {
    "success": "satisfied",
    "failure": "failed",
    "skipped": "skipped",
    "timed-out": "timed-out",
    "unknown": "unknown",
}
_STATIC_LANE_RUNNER = "ubuntu-24.04"


def _plan_digest(plan: CiQualificationSnapshot) -> str:
    if type(plan) is not CiQualificationSnapshot:
        message = "plan must be an exact CiQualificationSnapshot"
        raise TypeError(message)
    return ci_qualification_snapshot_digest(plan)


def _planned_obligation(
    plan: CiQualificationSnapshot,
    supplied: CiObligation,
) -> CiObligation:
    if type(supplied) is not CiObligation:
        message = "obligation must be an exact CiObligation"
        raise TypeError(message)
    planned = next(
        (
            obligation
            for obligation in plan.obligations
            if obligation.obligation_id == supplied.obligation_id
        ),
        None,
    )
    if planned is None:
        message = "Evidence obligation was not planned"
        raise ValueError(message)
    if supplied != planned:
        message = "Evidence obligation does not match the exact Plan binding"
        raise ValueError(message)
    if not planned.selected or not planned.required:
        message = "Evidence cannot be formed for unselected work"
        raise ValueError(message)
    return planned


def _lane_obligation(
    plan: CiQualificationSnapshot,
    lane_id: object,
) -> CiObligation:
    if type(lane_id) is not str:
        message = "lane_id must be an exact string"
        raise TypeError(message)
    planned = next(
        (
            obligation
            for obligation in plan.obligations
            if obligation.lane_id == lane_id
        ),
        None,
    )
    if planned is None:
        message = "static lane was not planned"
        raise ValueError(message)
    return planned


def _admit_evidence_for_plan(
    plan: CiQualificationSnapshot,
    evidence: CiEvidence,
) -> CiEvidence:
    plan_digest = _plan_digest(plan)
    if type(evidence) is not CiEvidence:
        message = "evidence must be an exact CiEvidence"
        raise TypeError(message)
    ci_evidence_digest(evidence)
    planned = _planned_obligation(plan, evidence.obligation)
    if (
        evidence.plan_digest != plan_digest
        or evidence.candidate != plan.candidate
        or evidence.evidence_id != planned.expected_evidence_id
        or evidence.producer != planned.lane_id
        or evidence.workflow_run_id != plan.workflow_run_id
        or evidence.run_attempt != plan.run_attempt
        or evidence.runner != _STATIC_LANE_RUNNER
    ):
        message = "Evidence does not match its exact current Plan binding"
        raise ValueError(message)
    artifact_outputs = tuple(
        (
            artifact.output_id,
            artifact.logical_role,
            artifact.media_kind,
        )
        for artifact in evidence.artifacts
    )
    if artifact_outputs and artifact_outputs != plan.selected_outputs:
        message = (
            "Evidence artifacts do not match the exact planned output contract"
        )
        raise ValueError(message)
    return evidence


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


def _validate_success_closure(
    plan: CiQualificationSnapshot,
    obligation: CiObligation,
    normalized_outcome: str,
    output_digests: tuple[str, ...],
    artifacts: tuple[CiArtifact, ...],
) -> None:
    if obligation.lane_id != "npm-artifact-build" and artifacts:
        message = "non-artifact Evidence cannot claim CI artifacts"
        raise ValueError(message)
    if normalized_outcome != "satisfied":
        if artifacts:
            message = "unsatisfied Evidence cannot claim CI artifacts"
            raise ValueError(message)
        return
    if not output_digests:
        message = "satisfied Evidence requires an output digest"
        raise ValueError(message)
    if obligation.lane_id == "npm-artifact-build" and len(artifacts) != 1:
        message = (
            "satisfied npm artifact Evidence requires exactly one "
            "complete CI artifact record and output provenance"
        )
        raise ValueError(message)
    if obligation.lane_id == "npm-artifact-build":
        artifact_outputs = tuple(
            (
                artifact.output_id,
                artifact.logical_role,
                artifact.media_kind,
            )
            for artifact in artifacts
        )
        if artifact_outputs != plan.selected_outputs:
            message = (
                "satisfied npm artifact Evidence must match the planned "
                "output contract"
            )
            raise ValueError(message)


def form_ci_evidence(  # noqa: PLR0913
    plan: CiQualificationSnapshot,
    *,
    obligation: CiObligation,
    producer: str,
    workflow_run_id: int,
    run_attempt: int,
    runner: str,
    raw_outcome: str,
    output_digests: tuple[str, ...],
    artifacts: tuple[CiArtifact, ...] = (),
    diagnostics: tuple[str, ...] = (),
) -> CiEvidence:
    """Wrap mechanical facts as Evidence for one exact selected obligation."""
    plan_digest = _plan_digest(plan)
    planned = _planned_obligation(plan, obligation)
    if type(runner) is not str or runner != _STATIC_LANE_RUNNER:
        message = "Evidence runner does not match the static lane runner"
        raise ValueError(message)
    normalized_outcome = normalize_required_outcome(raw_outcome)
    _validate_success_closure(
        plan,
        planned,
        normalized_outcome,
        output_digests,
        artifacts,
    )
    evidence = CiEvidence(
        evidence_id=planned.expected_evidence_id,
        plan_digest=plan_digest,
        candidate=plan.candidate,
        obligation=planned,
        producer=producer,
        workflow_run_id=workflow_run_id,
        run_attempt=run_attempt,
        runner=runner,
        raw_outcome=raw_outcome,
        output_digests=output_digests,
        artifacts=artifacts,
        normalized_outcome=normalized_outcome,
        diagnostics=diagnostics,
    )
    return _admit_evidence_for_plan(plan, evidence)


def form_evidence_lane_result(
    plan: CiQualificationSnapshot,
    evidence: CiEvidence,
) -> CiLaneResult:
    """Form the always-emitted selected-lane result for admitted Evidence."""
    admitted = _admit_evidence_for_plan(plan, evidence)
    result = CiLaneResult(
        plan_digest=admitted.plan_digest,
        candidate=admitted.candidate,
        lane_id=admitted.obligation.lane_id,
        producer=admitted.producer,
        workflow_run_id=admitted.workflow_run_id,
        run_attempt=admitted.run_attempt,
        disposition=admitted.normalized_outcome,
        evidence=admitted,
    )
    return admit_lane_result_for_plan(plan, result)


def form_empty_lane_result(
    plan: CiQualificationSnapshot,
    *,
    lane_id: str,
) -> CiLaneResult:
    """Form a valid Plan-bound result for one unselected static lane."""
    plan_digest = _plan_digest(plan)
    obligation = _lane_obligation(plan, lane_id)
    if obligation.lane_id == "root-hk" and plan.ready:
        message = "root-hk is empty only for a blocked Plan"
        raise ValueError(message)
    if obligation.selected:
        message = "selected work cannot be downgraded to an empty lane"
        raise ValueError(message)
    result = CiLaneResult(
        plan_digest=plan_digest,
        candidate=plan.candidate,
        lane_id=obligation.lane_id,
        producer=obligation.lane_id,
        workflow_run_id=plan.workflow_run_id,
        run_attempt=plan.run_attempt,
        disposition="empty",
        evidence=None,
    )
    return admit_lane_result_for_plan(plan, result)


def admit_lane_result_for_plan(
    plan: CiQualificationSnapshot,
    result: CiLaneResult,
) -> CiLaneResult:
    """Admit one static result only at its exact immutable Plan position."""
    plan_digest = _plan_digest(plan)
    if type(result) is not CiLaneResult:
        message = "lane_result must be an exact CiLaneResult"
        raise TypeError(message)
    ci_lane_result_digest(result)
    obligation = _lane_obligation(plan, result.lane_id)
    if (
        result.plan_digest != plan_digest
        or result.candidate != plan.candidate
        or result.producer != obligation.lane_id
        or result.workflow_run_id != plan.workflow_run_id
        or result.run_attempt != plan.run_attempt
    ):
        message = "lane result does not match its exact current Plan binding"
        raise ValueError(message)
    if obligation.selected:
        if result.disposition == "empty" or result.evidence is None:
            message = "selected work cannot be removed or downgraded to empty"
            raise ValueError(message)
        _admit_evidence_for_plan(
            plan,
            cast("CiEvidence", result.evidence),
        )
    else:
        if obligation.lane_id == "root-hk" and plan.ready:
            message = "root-hk is empty only for a blocked Plan"
            raise ValueError(message)
        if result.disposition != "empty" or result.evidence is not None:
            message = "unselected work must emit exactly one empty lane result"
            raise ValueError(message)
    return result


__all__ = [
    "admit_lane_result_for_plan",
    "form_ci_evidence",
    "form_empty_lane_result",
    "form_evidence_lane_result",
    "normalize_required_outcome",
]
