"""Retained qualification and native publication preparation."""

from __future__ import annotations

from typing import TYPE_CHECKING

from three_workflow_delivery_v3.canonical import canonical_sha256
from three_workflow_delivery_v3.records.release import (
    DestinationOperationProfile,
    NugetDestinationOperationProfile,
    NugetPublicationAction,
    NugetReleaseArtifact,
    NugetRemoteStateObservation,
    ObligationDisposition,
    PublicationAction,
    PublicationObservationReference,
    PublicationSnapshot,
    QualificationDecision,
    QualificationEvidence,
    QualificationSnapshot,
    ReleaseArtifact,
    ReleaseAttemptIdentity,
    RemoteStateObservation,
    SimulationBinding,
    SimulationIdentity,
    form_nuget_publication_action,
    form_publication_action,
)
from three_workflow_delivery_v3.release.nuget_eligibility import (
    AdmittedNugetLiveEligibilityDecision,
)
from three_workflow_delivery_v3.release.observation import (
    admit_destination_observation,
)
from three_workflow_delivery_v3.release.qualification import (
    admit_evidence_for_snapshot,
)
from three_workflow_delivery_v3.release.qualification import (
    validate_qualification_artifacts as _validate_artifacts,
)
from three_workflow_delivery_v3.release.qualification import (
    validate_qualification_decision as _validate_decision,
)
from three_workflow_delivery_v3.release.qualification import (
    validate_qualification_decision_artifacts as _validate_decision_artifacts,
)

if TYPE_CHECKING:
    from datetime import datetime

    from three_workflow_delivery_v3.records.artifacts import ArtifactReference
    from three_workflow_delivery_v3.records.release import (
        ReleaseAttemptBinding,
        ReleaseIntent,
    )
    from three_workflow_delivery_v3.release.eligibility import (
        AdmittedLiveEligibilityDecision,
    )
    from three_workflow_delivery_v3.repository.descriptors import ReleasePolicy


def _subject(
    snapshot: QualificationSnapshot,
) -> SimulationIdentity | ReleaseAttemptIdentity:
    if isinstance(snapshot.subject, SimulationBinding):
        return snapshot.subject.simulation
    return snapshot.subject


def finalize_qualification(  # noqa: C901, PLR0912, PLR0915
    snapshot: QualificationSnapshot,
    evidence_records: tuple[QualificationEvidence, ...],
    artifacts: tuple[ReleaseArtifact | NugetReleaseArtifact, ...],
) -> QualificationDecision:
    """Close every obligation while preserving failure-continuation state."""
    if type(snapshot) is not QualificationSnapshot:
        message = "Qualification Finalizer requires an exact Snapshot"
        raise TypeError(message)
    if type(evidence_records) is not tuple:
        message = "Qualification Evidence records must be an exact tuple"
        raise TypeError(message)
    admitted_artifacts = _validate_artifacts(snapshot, artifacts)
    artifact_by_digest = {
        artifact.artifact_digest: artifact for artifact in admitted_artifacts
    }
    admitted_by_obligation: dict[str, QualificationEvidence] = {}
    evidence_ids: set[str] = set()
    for evidence in evidence_records:
        admitted = admit_evidence_for_snapshot(snapshot, evidence)
        obligation_id = admitted.obligation.obligation_id
        if (
            admitted.evidence_id in evidence_ids
            or obligation_id in admitted_by_obligation
        ):
            message = (
                "Qualification Finalizer received duplicate or conflicting "
                "Evidence"
            )
            raise ValueError(message)
        evidence_ids.add(admitted.evidence_id)
        admitted_by_obligation[obligation_id] = admitted

    dispositions: list[ObligationDisposition] = []
    failed_obligations = {
        obligation_id
        for obligation_id, evidence in admitted_by_obligation.items()
        if evidence.normalized_outcome == "failed"
    }
    incomplete_obligations = {
        obligation_id
        for obligation_id, evidence in admitted_by_obligation.items()
        if evidence.normalized_outcome == "incomplete"
    }
    definitive_failure = bool(failed_obligations)
    for obligation in snapshot.obligations:
        evidence = admitted_by_obligation.get(obligation.obligation_id)
        if evidence is not None:
            outcome = evidence.normalized_outcome
            evidence_digests = (evidence.evidence_digest,)
            explanation = (
                f"{obligation.obligation_id} {evidence.normalized_outcome}"
            )
        else:
            blocked = any(
                prerequisite in failed_obligations
                or prerequisite in incomplete_obligations
                for prerequisite in obligation.prerequisites
            )
            outcome = "incomplete"
            evidence_digests = ()
            if blocked:
                explanation = (
                    f"{obligation.obligation_id} blocked-by-prerequisite"
                )
            elif definitive_failure:
                explanation = (
                    f"{obligation.obligation_id} aborted-after-failure"
                )
            else:
                explanation = f"{obligation.obligation_id} missing-evidence"
        dispositions.append(
            ObligationDisposition(
                obligation=obligation,
                outcome=outcome,
                evidence_digests=evidence_digests,
                explanation=explanation,
            )
        )

    complete_evidence = bool(
        snapshot.obligations
        and set(admitted_by_obligation)
        == {obligation.obligation_id for obligation in snapshot.obligations}
    )
    complete_artifacts = len(admitted_artifacts) == len(snapshot.outputs) and {
        artifact.output.output_id for artifact in admitted_artifacts
    } == {output.output_id for output in snapshot.outputs}
    evidence_artifact_digests = {
        digest
        for evidence in admitted_by_obligation.values()
        for digest in evidence.artifact_digests
    }
    evidence_artifact_bindings_complete = (
        evidence_artifact_digests <= artifact_by_digest.keys()
    )
    artifact_bindings_complete = (
        not complete_artifacts
        or evidence_artifact_digests == artifact_by_digest.keys()
    )
    outcomes = tuple(disposition.outcome for disposition in dispositions)
    incomplete_next_action = (
        "rerun-simulation"
        if isinstance(snapshot.subject, SimulationBinding)
        else "new-attempt"
    )
    if not evidence_artifact_bindings_complete:
        terminal_result = "incomplete"
        failure_class = "incomplete-qualification"
        next_action = incomplete_next_action
    elif "failed" in outcomes:
        terminal_result = "failure"
        failure_class = "quality-failure"
        next_action = "fix-quality-failure-and-rerun"
    elif (
        not complete_evidence
        or not complete_artifacts
        or not artifact_bindings_complete
        or "incomplete" in outcomes
    ):
        terminal_result = "incomplete"
        failure_class = "incomplete-qualification"
        next_action = incomplete_next_action
    elif all(outcome == "satisfied" for outcome in outcomes):
        terminal_result = "success"
        failure_class = "none"
        next_action = "observe-destinations"
    else:
        terminal_result = "failure"
        failure_class = "quality-failure"
        next_action = "fix-quality-failure-and-rerun"

    admitted_evidence = tuple(
        admitted_by_obligation[obligation.obligation_id]
        for obligation in snapshot.obligations
        if obligation.obligation_id in admitted_by_obligation
    )
    return QualificationDecision(
        subject=_subject(snapshot),
        qualification_snapshot_digest=snapshot.snapshot_digest,
        obligation_dispositions=tuple(dispositions),
        admitted_evidence_digests=tuple(
            evidence.evidence_digest for evidence in admitted_evidence
        ),
        admitted_artifact_digests=tuple(
            artifact.artifact_digest for artifact in admitted_artifacts
        ),
        terminal_result=terminal_result,
        failure_class=failure_class,
        next_action=next_action,
    )


def desired_projection_state_digest(
    snapshot: QualificationSnapshot,
    projection_id: str,
    artifact: ReleaseArtifact,
) -> str:
    """Return the immutable pre-observation desired-state basis digest."""
    projections = tuple(
        projection
        for projection in snapshot.destination_projections
        if projection.projection_id == projection_id
    )
    if len(projections) != 1:
        message = "desired state projection is not uniquely planned"
        raise ValueError(message)
    projection = projections[0]
    if (
        artifact.output != projection.output
        or artifact.qualification_snapshot_digest != snapshot.snapshot_digest
    ):
        message = "desired state artifact does not match the projection"
        raise ValueError(message)
    return canonical_sha256(
        {
            "schema": "workflow-delivery/v3/projection-desired-state",
            "qualification-snapshot-digest": snapshot.snapshot_digest,
            "projection": projection.to_document(),
            "artifact-digest": artifact.artifact_digest,
            "content": artifact.content.to_document(),
            "witness-digest": artifact.witness_digest,
        }
    )


def materialize_publication_snapshot(  # noqa: C901, PLR0913
    snapshot: QualificationSnapshot,
    decision: QualificationDecision,
    observations: tuple[
        RemoteStateObservation | NugetRemoteStateObservation, ...
    ],
    artifacts: tuple[ReleaseArtifact | NugetReleaseArtifact, ...],
    *,
    intent: ReleaseIntent,
    attempt_binding: ReleaseAttemptBinding,
    eligibility: AdmittedLiveEligibilityDecision
    | AdmittedNugetLiveEligibilityDecision,
    policy: ReleasePolicy,
    decision_reference: ArtifactReference,
    action_creation_at: datetime,
    destination_operation_profile: DestinationOperationProfile
    | NugetDestinationOperationProfile
    | None = None,
) -> PublicationSnapshot:
    """Materialize the guarded second Snapshot for a live Attempt only."""
    if action_creation_at is None:
        message = "Current materialization requires action-creation time"
        raise TypeError(message)
    if not isinstance(snapshot.subject, ReleaseAttemptIdentity):
        message = "Publication Snapshot cannot be emitted for simulation"
        raise TypeError(message)
    _validate_decision(snapshot, decision)
    if decision.terminal_result != "success":
        message = "Publication Snapshot requires successful qualification"
        raise ValueError(message)
    admitted_artifacts = _validate_artifacts(snapshot, artifacts)
    if len(admitted_artifacts) != len(snapshot.outputs):
        message = "Publication Snapshot requires complete artifacts"
        raise ValueError(message)
    _validate_decision_artifacts(decision, admitted_artifacts)
    if type(observations) is not tuple or len(observations) != 1:
        message = "Publication Snapshot requires one Remote-State Observation"
        raise ValueError(message)
    (artifact,) = admitted_artifacts
    observation = admit_destination_observation(
        observations[0],
        intent=intent,
        attempt_binding=attempt_binding,
        eligibility=eligibility,
        policy=policy,
        snapshot=snapshot,
        decision=decision,
        decision_reference=decision_reference,
        artifact=artifact,
        action_creation_at=action_creation_at,
    )
    (projection,) = snapshot.destination_projections
    if observation.classification not in {"absent", "exact-satisfied"}:
        message = "Publication Snapshot observation is not ready"
        raise ValueError(message)
    materialized_actions: tuple[
        PublicationAction | NugetPublicationAction, ...
    ] = ()
    if observation.classification == "absent":
        if type(artifact) is NugetReleaseArtifact:
            if (
                type(destination_operation_profile)
                is not NugetDestinationOperationProfile
                or type(eligibility) is not AdmittedNugetLiveEligibilityDecision
            ):
                message = "NuGet action requires its exact admitted profile"
                raise TypeError(message)
            if destination_operation_profile != eligibility.profile:
                message = (
                    "NuGet action profile differs from admitted Eligibility"
                )
                raise ValueError(message)
            materialized_actions = (
                form_nuget_publication_action(
                    destination_operation_profile=destination_operation_profile,
                    projection=projection,
                    artifact=artifact,
                ),
            )
        else:
            if (
                type(artifact) is not ReleaseArtifact
                or type(destination_operation_profile)
                is not DestinationOperationProfile
            ):
                message = (
                    "Publication Action requires an exact "
                    "Destination Operation Profile"
                )
                raise TypeError(message)
            materialized_actions = (
                form_publication_action(
                    destination_operation_profile=destination_operation_profile,
                    projection=projection,
                    artifact=artifact,
                ),
            )
    return PublicationSnapshot(
        attempt=snapshot.subject,
        qualification_snapshot_digest=snapshot.snapshot_digest,
        qualification_decision_digest=decision.decision_digest,
        qualification_result=decision.terminal_result,
        projection_ids=tuple(
            projection.projection_id
            for projection in snapshot.destination_projections
        ),
        artifact_digests=tuple(
            artifact.artifact_digest for artifact in admitted_artifacts
        ),
        artifact_output_ids=tuple(
            artifact.output.output_id for artifact in admitted_artifacts
        ),
        observation_references=(
            PublicationObservationReference(
                projection_id=projection.projection_id,
                observation_digest=observation.observation_digest,
                classification=observation.classification,
            ),
        ),
        materialized_actions=materialized_actions,
    )


__all__ = [
    "desired_projection_state_digest",
    "finalize_qualification",
    "materialize_publication_snapshot",
]
