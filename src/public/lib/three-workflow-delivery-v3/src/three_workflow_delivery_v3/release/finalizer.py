"""Pure qualification, second-snapshot, and simulation finalization."""

from __future__ import annotations

from three_workflow_delivery_v3.canonical import canonical_sha256
from three_workflow_delivery_v3.records.release import (
    NPMJS_OBSERVATION_CONTRACT_ID,
    NPMJS_OBSERVER_PRODUCER,
    HypotheticalAction,
    ObligationDisposition,
    ObservationRequestFacts,
    ObservationResponseFacts,
    ObservationValue,
    ProjectionObservation,
    PublicationObservationReference,
    PublicationSnapshot,
    QualificationDecision,
    QualificationEvidence,
    QualificationSnapshot,
    ReleaseArtifact,
    ReleaseAttemptIdentity,
    SimulationBinding,
    SimulationIdentity,
    SimulationOutcome,
)
from three_workflow_delivery_v3.release.qualification import (
    admit_evidence_for_snapshot,
)


class UnsupportedPublicationPrimitiveError(RuntimeError):
    """Normal Live cannot materialize an unimplemented destination primitive."""


def _subject(
    snapshot: QualificationSnapshot,
) -> SimulationIdentity | ReleaseAttemptIdentity:
    if isinstance(snapshot.subject, SimulationBinding):
        return snapshot.subject.simulation
    return snapshot.subject


def _validate_artifacts(
    snapshot: QualificationSnapshot,
    artifacts: tuple[ReleaseArtifact, ...],
) -> tuple[ReleaseArtifact, ...]:
    if type(artifacts) is not tuple:
        message = "Release artifacts must be an exact tuple"
        raise TypeError(message)
    by_output: dict[str, ReleaseArtifact] = {}
    record_digests: set[str] = set()
    expected_subject = _subject(snapshot)
    for artifact in artifacts:
        if type(artifact) is not ReleaseArtifact:
            message = "Release artifact has the wrong runtime type"
            raise TypeError(message)
        record_digest = artifact.artifact_digest
        if record_digest in record_digests:
            message = "Release artifact set contains a duplicate record"
            raise ValueError(message)
        record_digests.add(record_digest)
        if (
            artifact.subject != expected_subject
            or artifact.repository != snapshot.repository
            or artifact.qualification_snapshot_digest
            != snapshot.snapshot_digest
            or artifact.repository_model_digest
            != snapshot.repository_model_digest
            or artifact.target != snapshot.target
            or artifact.output not in snapshot.outputs
        ):
            message = "Release artifact does not match the current Snapshot"
            raise ValueError(message)
        output_id = artifact.output.output_id
        if output_id in by_output:
            message = "Release artifact set substitutes a planned output"
            raise ValueError(message)
        by_output[output_id] = artifact
    return tuple(
        by_output[output.output_id]
        for output in snapshot.outputs
        if output.output_id in by_output
    )


def finalize_qualification(  # noqa: C901, PLR0912, PLR0915
    snapshot: QualificationSnapshot,
    evidence_records: tuple[QualificationEvidence, ...],
    artifacts: tuple[ReleaseArtifact, ...],
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


def _admit_synthetic_projection_observation(
    snapshot: QualificationSnapshot,
    decision: QualificationDecision,
    artifact: ReleaseArtifact,
    *,
    classification: str,
    owner: str | None = None,
) -> ProjectionObservation:
    """Form test-only observations without remote interpretation."""
    if classification not in {"absent", "exact-satisfied"}:
        message = "synthetic observation supports only absent or exact state"
        raise ValueError(message)
    _validate_decision(snapshot, decision)
    admitted_artifacts = _validate_artifacts(snapshot, (artifact,))
    if len(admitted_artifacts) != 1:
        message = "synthetic observation requires the complete artifact"
        raise ValueError(message)
    projection = snapshot.destination_projections[0]
    desired_digest = desired_projection_state_digest(
        snapshot,
        projection.projection_id,
        artifact,
    )
    request_facts = ObservationRequestFacts(
        qualification_snapshot_digest=snapshot.snapshot_digest,
        projection_digest=projection.projection_digest,
        desired_state_digest=desired_digest,
        method="GET",
        url=(f"synthetic://workflow-delivery-v3/{projection.projection_id}"),
        headers=(),
    )
    request_digest = request_facts.request_digest
    if classification == "absent":
        value = ObservationValue(
            classification="absent",
            owner=None,
            coordinate=None,
            content_sha512=None,
            witness_digest=None,
            routing=(),
        )
    else:
        if owner is None:
            message = "synthetic exact observation requires an explicit owner"
            raise ValueError(message)
        value = ObservationValue(
            classification="exact-satisfied",
            owner=owner,
            coordinate=projection.coordinate,
            content_sha512=artifact.content.content_sha512,
            witness_digest=artifact.witness_digest,
            routing=(),
        )
    response_facts = ObservationResponseFacts(
        stage="synthetic",
        requested_url=request_facts.url,
        final_url=request_facts.url,
        redirects=(),
        status="synthetic",
        selected_headers=(),
        truncated=False,
        body_sha256=None,
    )
    response_digest = canonical_sha256(
        {
            "schema": "workflow-delivery/v3/observation-response",
            "request-digest": request_digest,
            "facts": response_facts.to_document(),
            "value": value.to_document(),
        }
    )
    return ProjectionObservation(
        subject=_subject(snapshot),
        purpose=(
            snapshot.subject.purpose
            if isinstance(snapshot.subject, SimulationBinding)
            else "live-release"
        ),
        target=snapshot.target,
        producer=NPMJS_OBSERVER_PRODUCER,
        qualification_snapshot_digest=snapshot.snapshot_digest,
        projection=projection,
        desired_state_digest=desired_digest,
        observation_contract_id=projection.observation_contract_id,
        request_facts=request_facts,
        request_digest=request_digest,
        response_facts=response_facts,
        response_digest=response_digest,
        value=value,
    )


def _validate_decision(
    snapshot: QualificationSnapshot,
    decision: QualificationDecision,
) -> None:
    if type(decision) is not QualificationDecision:
        message = "finalization requires an exact QualificationDecision"
        raise TypeError(message)
    if (
        decision.subject != _subject(snapshot)
        or decision.qualification_snapshot_digest != snapshot.snapshot_digest
        or tuple(
            disposition.obligation
            for disposition in decision.obligation_dispositions
        )
        != snapshot.obligations
    ):
        message = "Qualification Decision does not match the Snapshot"
        raise ValueError(message)
    disposition_outcomes = tuple(
        disposition.outcome for disposition in decision.obligation_dispositions
    )
    if decision.terminal_result == "success" and (
        not disposition_outcomes
        or any(outcome != "satisfied" for outcome in disposition_outcomes)
        or len(decision.admitted_evidence_digests)
        != len(snapshot.expected_evidence_ids)
        or len(decision.admitted_artifact_digests) != len(snapshot.outputs)
    ):
        message = "successful Qualification Decision is not complete"
        raise ValueError(message)


def _validate_decision_artifacts(
    decision: QualificationDecision,
    artifacts: tuple[ReleaseArtifact, ...],
) -> None:
    if (
        tuple(artifact.artifact_digest for artifact in artifacts)
        != decision.admitted_artifact_digests
    ):
        message = "Qualification Decision artifact binding mismatch"
        raise ValueError(message)


def _validate_observation_producer(
    observation: ProjectionObservation,
) -> None:
    if (
        observation.observation_contract_id == NPMJS_OBSERVATION_CONTRACT_ID
        and observation.producer != NPMJS_OBSERVER_PRODUCER
    ):
        message = "npmjs Projection observation producer mismatch"
        raise ValueError(message)


def validate_projection_observations(
    snapshot: QualificationSnapshot,
    observations: tuple[ProjectionObservation, ...],
    artifacts: tuple[ReleaseArtifact, ...],
) -> tuple[ProjectionObservation, ...]:
    """Admit observations against the frozen projection and artifact basis."""
    if type(observations) is not tuple:
        message = "Projection observations must be an exact tuple"
        raise TypeError(message)
    artifact_by_output = {
        artifact.output.output_id: artifact for artifact in artifacts
    }
    by_projection: dict[str, ProjectionObservation] = {}
    for observation in observations:
        if type(observation) is not ProjectionObservation:
            message = "Projection observation has the wrong runtime type"
            raise TypeError(message)
        projection_id = observation.projection.projection_id
        if projection_id in by_projection:
            message = "Projection observation set contains duplicates"
            raise ValueError(message)
        planned = next(
            (
                projection
                for projection in snapshot.destination_projections
                if projection.projection_id == projection_id
            ),
            None,
        )
        if planned is None:
            message = "Projection observation was not planned"
            raise ValueError(message)
        artifact = artifact_by_output.get(planned.output.output_id)
        if artifact is None:
            message = "Projection observation lacks its qualified artifact"
            raise ValueError(message)
        if (
            observation.subject != _subject(snapshot)
            or observation.purpose
            != (
                snapshot.subject.purpose
                if isinstance(snapshot.subject, SimulationBinding)
                else "live-release"
            )
            or observation.target != snapshot.target
            or observation.qualification_snapshot_digest
            != snapshot.snapshot_digest
            or observation.projection != planned
            or observation.desired_state_digest
            != desired_projection_state_digest(
                snapshot,
                projection_id,
                artifact,
            )
        ):
            message = "Projection observation binding mismatch"
            raise ValueError(message)
        _validate_observation_producer(observation)
        if observation.value.classification == "exact-satisfied" and (
            observation.value.coordinate != planned.coordinate
            or observation.value.content_sha512
            != artifact.content.content_sha512
            or observation.value.witness_digest != artifact.witness_digest
        ):
            message = (
                "exact Projection observation does not match desired artifact"
            )
            raise ValueError(message)
        by_projection[projection_id] = observation
    expected_ids = tuple(
        projection.projection_id
        for projection in snapshot.destination_projections
    )
    if set(by_projection) != set(expected_ids):
        message = "exactly one Observation is required per projection"
        raise ValueError(message)
    return tuple(by_projection[projection_id] for projection_id in expected_ids)


def materialize_hypothetical_actions(
    snapshot: QualificationSnapshot,
    decision: QualificationDecision,
    observations: tuple[ProjectionObservation, ...],
    artifacts: tuple[ReleaseArtifact, ...],
) -> tuple[HypotheticalAction, ...]:
    """Materialize absent-only hypothetical actions for a simulation."""
    if not isinstance(snapshot.subject, SimulationBinding):
        message = "Hypothetical actions require a Simulation Binding"
        raise TypeError(message)
    _validate_decision(snapshot, decision)
    if decision.terminal_result != "success":
        message = "Hypothetical actions require successful qualification"
        raise ValueError(message)
    admitted_artifacts = _validate_artifacts(snapshot, artifacts)
    if len(admitted_artifacts) != len(snapshot.outputs):
        message = "Hypothetical actions require complete artifacts"
        raise ValueError(message)
    _validate_decision_artifacts(decision, admitted_artifacts)
    admitted_observations = validate_projection_observations(
        snapshot,
        observations,
        admitted_artifacts,
    )
    artifact_by_output = {
        artifact.output.output_id: artifact for artifact in admitted_artifacts
    }
    action_by_projection = {
        action.projection_id: action for action in snapshot.potential_actions
    }
    actions: list[HypotheticalAction] = []
    for observation in admitted_observations:
        classification = observation.value.classification
        if classification == "exact-satisfied":
            continue
        if classification != "absent":
            message = (
                "only absent or exact-satisfied observations permit "
                "hypothetical action materialization"
            )
            raise ValueError(message)
        projection = observation.projection
        potential = action_by_projection[projection.projection_id]
        artifact = artifact_by_output[projection.output.output_id]
        key_digest = canonical_sha256(projection.coordinate.to_document())
        actions.append(
            HypotheticalAction(
                simulation=snapshot.subject.simulation,
                qualification_snapshot_digest=snapshot.snapshot_digest,
                qualification_decision_digest=decision.decision_digest,
                projection_id=projection.projection_id,
                potential_action=potential,
                artifact_digest=artifact.artifact_digest,
                mutable_resource_keys=(
                    (
                        "external-package-coordinate:"
                        f"{key_digest.removeprefix('sha256:')}"
                    ),
                ),
                capability_requirements=potential.capability_requirements,
            )
        )
    return tuple(actions)


def materialize_publication_snapshot(
    snapshot: QualificationSnapshot,
    decision: QualificationDecision,
    observations: tuple[ProjectionObservation, ...],
    artifacts: tuple[ReleaseArtifact, ...],
) -> PublicationSnapshot:
    """Materialize the guarded second Snapshot for a live Attempt only."""
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
    admitted_observations = validate_projection_observations(
        snapshot,
        observations,
        admitted_artifacts,
    )
    for observation in admitted_observations:
        if observation.value.classification not in {
            "absent",
            "exact-satisfied",
        }:
            message = "Publication Snapshot observation is not ready"
            raise ValueError(message)
    absent_projection_ids = {
        observation.projection.projection_id
        for observation in admitted_observations
        if observation.value.classification == "absent"
    }
    if absent_projection_ids:
        message = (
            "Normal Live destination primitive is not implemented; "
            "publication remains activation-blocked"
        )
        raise UnsupportedPublicationPrimitiveError(message)
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
        observation_references=tuple(
            PublicationObservationReference(
                projection_id=observation.projection.projection_id,
                observation_digest=observation.observation_digest,
                classification=observation.value.classification,
            )
            for observation in admitted_observations
        ),
        materialized_actions=(),
    )


def finalize_simulation(
    snapshot: QualificationSnapshot,
    decision: QualificationDecision,
    *,
    observations: tuple[ProjectionObservation, ...] = (),
    artifacts: tuple[ReleaseArtifact, ...] = (),
) -> SimulationOutcome:
    """Finalize simulation from admitted observation classifications."""
    if not isinstance(snapshot.subject, SimulationBinding):
        message = "Simulation Finalizer requires a Simulation Binding"
        raise TypeError(message)
    _validate_decision(snapshot, decision)
    admitted_artifacts = _validate_artifacts(snapshot, artifacts)
    complete_artifacts = len(admitted_artifacts) == len(snapshot.outputs)
    if decision.terminal_result != "success":
        return SimulationOutcome(
            binding=snapshot.subject,
            qualification_snapshot_digest=snapshot.snapshot_digest,
            qualification_decision_digest=decision.decision_digest,
            observation_digests=(),
            hypothetical_actions=(),
            terminal_result=decision.terminal_result,
            failure_class=decision.failure_class,
            next_action=decision.next_action,
        )
    if not complete_artifacts:
        message = "successful simulation qualification lacks artifacts"
        raise ValueError(message)
    _validate_decision_artifacts(decision, admitted_artifacts)
    admitted_observations = validate_projection_observations(
        snapshot,
        observations,
        admitted_artifacts,
    )
    classifications = tuple(
        observation.value.classification
        for observation in admitted_observations
    )
    if all(
        classification in {"absent", "exact-satisfied"}
        for classification in classifications
    ):
        actions = materialize_hypothetical_actions(
            snapshot,
            decision,
            admitted_observations,
            admitted_artifacts,
        )
        terminal_result = "success"
        failure_class = "none"
        next_action = "none"
    elif "conflicting" in classifications or "partial" in classifications:
        actions = ()
        terminal_result = "failure"
        failure_class = "reconciliation-required"
        next_action = "reconcile-destination-state"
    elif "unprovable" in classifications:
        actions = ()
        terminal_result = "incomplete"
        failure_class = "unprovable-observation"
        next_action = "fix-observation-capability-and-rerun"
    else:
        actions = ()
        terminal_result = "incomplete"
        failure_class = "unknown-observation"
        next_action = "rerun-simulation"
    return SimulationOutcome(
        binding=snapshot.subject,
        qualification_snapshot_digest=snapshot.snapshot_digest,
        qualification_decision_digest=decision.decision_digest,
        observation_digests=tuple(
            observation.observation_digest
            for observation in admitted_observations
        ),
        hypothetical_actions=actions,
        terminal_result=terminal_result,
        failure_class=failure_class,
        next_action=next_action,
    )


__all__ = [
    "desired_projection_state_digest",
    "finalize_qualification",
    "finalize_simulation",
    "materialize_hypothetical_actions",
    "materialize_publication_snapshot",
]
