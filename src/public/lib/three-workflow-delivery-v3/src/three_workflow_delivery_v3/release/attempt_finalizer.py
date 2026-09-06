"""Read-only current-DAG admission and normal-Live terminal classification."""

# ruff: noqa: EM101, TRY003

from __future__ import annotations

import hashlib
import json
from dataclasses import dataclass
from datetime import datetime
from pathlib import PurePosixPath
from typing import TYPE_CHECKING

from three_workflow_delivery_v3.canonical import canonical_sha256, canonicalize
from three_workflow_delivery_v3.records.artifacts import (
    ArtifactReference,
    artifact_reference_from_document,
)
from three_workflow_delivery_v3.records.release import (
    ApprovalBundle,
    AttemptOutcome,
    DestinationOperationProfile,
    DirectPredecessor,
    ExactSatisfiedFinalizationProof,
    MutationMayHaveStartedMarker,
    PublicationAuthorization,
    PublicationObservationReference,
    PublicationResult,
    PublicationSnapshot,
    RemoteStateObservation,
    admit_release_record,
    form_publication_action,
)
from three_workflow_delivery_v3.release.eligibility import (
    AdmittedLiveEligibilityDecision,
    release_policy_digest,
    require_action_governance,
)
from three_workflow_delivery_v3.release.exact_satisfied import (
    admit_exact_satisfied_finalization_proof,
)
from three_workflow_delivery_v3.release.finalizer import finalize_qualification
from three_workflow_delivery_v3.release.identity import (
    derive_buddy_execution_identity,
    derive_release_attempt_binding,
)
from three_workflow_delivery_v3.release.live import (
    validate_approval_bundle_closure,
)
from three_workflow_delivery_v3.release.observation import (
    admit_remote_state_observation,
    classify_package_control,
)
from three_workflow_delivery_v3.repository.compiler import (
    compile_release_policy,
)

if TYPE_CHECKING:
    from three_workflow_delivery_v3.records.release import (
        QualificationDecision,
        QualificationEvidence,
        QualificationSnapshot,
        ReleaseArtifact,
        ReleaseAttemptBinding,
        ReleaseIntent,
        ReleaseRecord,
    )
    from three_workflow_delivery_v3.records.release_transport import (
        ReleaseAdmissionBindings,
    )
    from three_workflow_delivery_v3.repository.descriptors import ReleasePolicy

_PLATFORM_OUTCOMES = frozenset({"success", "failure", "cancelled", "skipped"})
_RECORD_REFERENCE_PAIR_SIZE = 2


def _profile() -> DestinationOperationProfile:
    from three_workflow_delivery_v3.adapters.github_packages import (  # noqa: PLC0415
        github_packages_destination_operation_profile,
    )

    return github_packages_destination_operation_profile()


def parse_publication_terminal_reference(
    value: str | None, *, publisher_conclusion: str
) -> ArtifactReference | None:
    """Admit the scalar wire value; only a skipped job may omit its output."""
    if publisher_conclusion not in _PLATFORM_OUTCOMES:
        raise ValueError("Publisher conclusion is not a direct terminal fact")
    if publisher_conclusion == "skipped" and value in (None, ""):
        return None
    if type(value) is not str or not value:
        raise ValueError("Running Publisher omitted its terminal reference")
    document = json.loads(value)
    if canonicalize(document).decode("utf-8") != value:
        raise ValueError("Publication terminal reference is not canonical JSON")
    if document is None:
        return None
    reference = artifact_reference_from_document(document)
    if publisher_conclusion == "skipped":
        raise ValueError("Skipped Publisher cannot have a publication terminal")
    return reference


@dataclass(frozen=True, slots=True)
class FinalizationInputs:
    """Transient transport-admitted current records, not a new authority."""

    intent: ReleaseIntent
    attempt_binding: ReleaseAttemptBinding
    eligibility: AdmittedLiveEligibilityDecision
    policy: ReleasePolicy
    snapshot: QualificationSnapshot
    decision: QualificationDecision
    decision_reference: ArtifactReference
    evidence: tuple[QualificationEvidence, ...]
    artifacts: tuple[ReleaseArtifact, ...]
    observations: tuple[
        tuple[RemoteStateObservation, ArtifactReference], ...
    ] = ()
    publication: tuple[PublicationSnapshot, ArtifactReference] | None = None
    bundle: tuple[ApprovalBundle, ArtifactReference] | None = None
    reviewer_summary: tuple[bytes, ArtifactReference] | None = None
    authorization: tuple[PublicationAuthorization, ArtifactReference] | None = (
        None
    )
    exact_proof: (
        tuple[ExactSatisfiedFinalizationProof, ArtifactReference] | None
    ) = None
    terminal: (
        tuple[
            MutationMayHaveStartedMarker | PublicationResult, ArtifactReference
        ]
        | None
    ) = None
    result_marker: (
        tuple[MutationMayHaveStartedMarker, ArtifactReference] | None
    ) = None


def _admit_pair(
    pair: tuple[ReleaseRecord, ArtifactReference],
    expected_type: type[ReleaseRecord],
    current: ReleaseAdmissionBindings,
) -> None:
    if (
        type(pair) is not tuple
        or len(pair) != _RECORD_REFERENCE_PAIR_SIZE
        or type(pair[0]) is not expected_type
        or type(pair[1]) is not ArtifactReference
    ):
        raise ValueError(
            "Finalizer requires one exact record and full reference"
        )
    record, reference = pair
    admit_release_record(
        canonicalize(record.to_document()),
        expected=record,
        expected_digest=reference.payload_digest,
        expected_bindings=current,
    )


def _admit_qualification(
    inputs: FinalizationInputs,
    current: ReleaseAdmissionBindings,
    run_attempt: int,
) -> None:
    eligibility = inputs.eligibility
    if (
        type(run_attempt) is not int
        or run_attempt != 1
        or type(eligibility) is not AdmittedLiveEligibilityDecision
        or current.purpose != "live-release"
        or current.run_attempt is not None
        or current.workflow_run_id != inputs.intent.workflow_run_id
        or current.target != inputs.intent.target
    ):
        raise ValueError("Finalizer requires exact current-run Live authority")
    context = eligibility.context
    if (
        context.request_id,
        context.selected_ref,
        context.target,
        context.workflow_run_id,
        context.control,
        context.release_policy_digest,
    ) != (
        inputs.intent.request_id,
        inputs.intent.selected_ref,
        inputs.intent.target,
        inputs.intent.workflow_run_id,
        f"workflow-delivery-v3:{inputs.intent.target}",
        release_policy_digest(inputs.policy),
    ):
        raise ValueError(
            "Finalizer Intent and admitted Model/Eligibility differ"
        )
    binding = derive_release_attempt_binding(
        intent=inputs.intent,
        execution=derive_buddy_execution_identity(inputs.intent),
        repository_model_digest=context.repository_model_digest,
        live_eligibility_artifact_id=inputs.attempt_binding.live_eligibility_artifact_id,
        live_eligibility_artifact_digest=inputs.attempt_binding.live_eligibility_artifact_digest,
        live_eligibility_payload_digest=eligibility.canonical_digest,
        attestation_provenance=eligibility.governance.provenance,
    )
    if (
        inputs.attempt_binding != binding
        or inputs.snapshot.subject != binding.attempt
        or inputs.snapshot.repository != inputs.intent.repository
        or inputs.snapshot.repository_model_digest
        != context.repository_model_digest
        or inputs.snapshot.release_policy_digest
        != compile_release_policy(inputs.policy).policy_digest
        or inputs.decision_reference.payload_digest
        != inputs.decision.decision_digest
        or finalize_qualification(
            inputs.snapshot, inputs.evidence, inputs.artifacts
        )
        != inputs.decision
    ):
        raise ValueError("Finalizer Qualification closure mismatch")
    for record in (inputs.intent, binding, inputs.snapshot, inputs.decision):
        admit_release_record(
            canonicalize(record.to_document()),
            expected=record,
            expected_digest=canonical_sha256(record.to_document()),
            expected_bindings=current,
        )


def _admit_publication(inputs: FinalizationInputs) -> None:  # noqa: C901, PLR0912
    if inputs.publication is None:
        if any(
            (
                inputs.bundle,
                inputs.authorization,
                inputs.exact_proof,
                inputs.terminal,
            )
        ):
            raise ValueError(
                "Downstream lineage requires its Publication Snapshot"
            )
        return
    if len(inputs.observations) != 1 or len(inputs.artifacts) != 1:
        raise ValueError(
            "Publication Snapshot requires its Observation and artifact"
        )
    publication, _ = inputs.publication
    observation, _ = inputs.observations[0]
    artifact = inputs.artifacts[0]
    (projection,) = inputs.snapshot.destination_projections
    if observation.classification not in {"absent", "exact-satisfied"}:
        raise ValueError("Blocking Observation cannot precede a Snapshot")
    profile = _profile()
    actions = (
        (
            form_publication_action(
                destination_operation_profile=profile,
                projection=projection,
                artifact=artifact,
            ),
        )
        if observation.classification == "absent"
        else ()
    )
    expected = PublicationSnapshot(
        attempt=inputs.attempt_binding.attempt,
        qualification_snapshot_digest=inputs.snapshot.snapshot_digest,
        qualification_decision_digest=inputs.decision.decision_digest,
        qualification_result=inputs.decision.terminal_result,
        projection_ids=(projection.projection_id,),
        artifact_digests=(artifact.artifact_digest,),
        artifact_output_ids=(artifact.output.output_id,),
        observation_references=(
            PublicationObservationReference(
                projection_id=projection.projection_id,
                observation_digest=observation.observation_digest,
                classification=observation.classification,
            ),
        ),
        materialized_actions=actions,
    )
    if publication != expected:
        raise ValueError("Finalizer Publication Snapshot closure mismatch")
    if not actions:
        if any(
            (
                inputs.bundle,
                inputs.reviewer_summary,
                inputs.authorization,
                inputs.terminal,
            )
        ):
            raise ValueError("Zero-action Snapshot has action-bearing lineage")
        if inputs.exact_proof is not None:
            admit_exact_satisfied_finalization_proof(
                inputs.exact_proof[0],
                publication_snapshot=publication,
                publication_snapshot_reference=inputs.publication[1],
                intent=inputs.intent,
                attempt_binding=inputs.attempt_binding,
                eligibility=inputs.eligibility,
                policy=inputs.policy,
                snapshot=inputs.snapshot,
                decision=inputs.decision,
                decision_reference=inputs.decision_reference,
                artifact=artifact,
                observation=observation,
            )
        return
    if inputs.exact_proof is not None:
        raise ValueError(
            "Action-bearing Snapshot cannot have exact finalization proof"
        )
    if inputs.reviewer_summary is not None:
        summary, summary_reference = inputs.reviewer_summary
        if (
            type(summary) is not bytes
            or type(summary_reference) is not ArtifactReference
            or summary_reference.payload_path != "reviewer-summary.md"
            or "sha256:" + hashlib.sha256(summary).hexdigest()
            != summary_reference.payload_digest
        ):
            raise ValueError("Reviewer payload reference mismatch")
    if inputs.bundle is None:
        if inputs.authorization is not None or inputs.terminal is not None:
            raise ValueError(
                "Publication authority requires its Approval Bundle"
            )
        return
    if inputs.reviewer_summary is None:
        raise ValueError("Approval Bundle requires its actual reviewer payload")
    _, summary_reference = inputs.reviewer_summary
    validate_approval_bundle_closure(
        approval_bundle=inputs.bundle[0],
        intent=inputs.intent,
        attempt_binding=inputs.attempt_binding,
        qualification_decision=inputs.decision,
        qualification_snapshot=inputs.snapshot,
        release_artifact=artifact,
        destination_operation_profile=profile,
        publication_snapshot=publication,
        publication_snapshot_reference=inputs.publication[1],
        reviewer_summary_reference=summary_reference,
        control=inputs.eligibility.context.control,
    )
    if inputs.authorization is None:
        if inputs.terminal is not None:
            raise ValueError("Publication terminal requires its Authorization")
        return
    authorization, _ = inputs.authorization
    initial = inputs.eligibility.governance
    if (
        authorization.approval_bundle_reference != inputs.bundle[1]
        or authorization.governance_proof.provenance != initial.provenance
        or datetime.fromisoformat(authorization.governance_proof.expires_at)
        != initial.attestation.expires_at
        or not initial.observed_at
        <= datetime.fromisoformat(authorization.governance_proof.observed_at)
        <= datetime.fromisoformat(authorization.completed_at)
    ):
        raise ValueError("Publication Authorization closure mismatch")
    require_action_governance(
        initial.attestation,
        now=datetime.fromisoformat(authorization.completed_at),
        destination_operation_profile_digest=profile.profile_digest,
    )


def _admit_marker(
    inputs: FinalizationInputs, marker: MutationMayHaveStartedMarker
) -> None:
    if inputs.authorization is None or inputs.publication is None:
        raise ValueError("Marker requires complete Authorization lineage")
    authorization, authorization_reference = inputs.authorization
    observation = inputs.observations[0][0]
    artifact = inputs.artifacts[0]
    action = inputs.publication[0].materialized_actions[0]
    initial = inputs.eligibility.governance
    profile = _profile()
    match = marker.profile_match
    if len(match.command) != len(profile.command_template):
        raise ValueError("Marker command template mismatch")
    tarball = match.command[profile.command_template.index("{tarball-path}")]
    path = PurePosixPath(tarball)
    expected_command = tuple(
        {"{tarball-path}": tarball, "{tag}": action.tag}.get(word, word)
        for word in profile.command_template
    )
    expected_configuration = tuple(
        sorted(
            {
                "@hcoona:registry": profile.registry,
                "registry": profile.registry + "/",
                "tag": action.tag,
                "ignore-scripts": "true",
                "fetch-retries": "0",
                "access": "null",
            }.items()
        )
    )
    if (
        marker.publication_authorization_reference != authorization_reference
        or marker.governance_proof.provenance != initial.provenance
        or datetime.fromisoformat(marker.governance_proof.expires_at)
        != initial.attestation.expires_at
        or classify_package_control(
            marker.package_control_proof,
            subject=observation.desired_subject,
            eligibility=inputs.eligibility,
        )
        != "ready"
        or not datetime.fromisoformat(authorization.completed_at)
        <= datetime.fromisoformat(marker.governance_proof.observed_at)
        <= datetime.fromisoformat(marker.package_control_proof.observed_at)
        <= datetime.fromisoformat(match.matched_at)
        < initial.attestation.expires_at
        or match.destination_operation_profile_digest != profile.profile_digest
        or match.node_version != profile.node_version
        or match.npm_version != profile.npm_version
        or match.command != expected_command
        or not path.is_absolute()
        or path.as_posix() != tarball
        or ".." in path.parts
        or path.name != artifact.content.basename
        or match.configuration != expected_configuration
    ):
        raise ValueError("Finalizer marker authority/profile evidence mismatch")
    require_action_governance(
        initial.attestation,
        now=datetime.fromisoformat(match.matched_at),
        destination_operation_profile_digest=profile.profile_digest,
    )


def _admit_result(
    inputs: FinalizationInputs,
    result: PublicationResult,
    marker: MutationMayHaveStartedMarker,
) -> None:
    readback = result.post_action_readback
    if readback is None:
        return
    artifact = inputs.artifacts[0]
    observation = inputs.observations[0][0]
    if inputs.publication is None:
        raise ValueError("Result requires its Publication Snapshot")
    action = inputs.publication[0].materialized_actions[0]
    if (
        readback.package != observation.desired_subject.normalized_package
        or readback.version != observation.desired_version
        or readback.tag != action.tag
        or datetime.fromisoformat(readback.observed_at)
        < datetime.fromisoformat(marker.profile_match.matched_at)
        or (
            readback.classification == "exact-satisfied"
            and (
                readback.content_sha256 != artifact.content.content_sha256
                or readback.content_sha512 != artifact.content.content_sha512
                or readback.witness_digest != artifact.witness_digest
                or readback.witness_target != inputs.intent.target
            )
        )
    ):
        raise ValueError(
            "Finalizer Publication Result readback binding mismatch"
        )


def finalize_attempt_outcome(  # noqa: C901, PLR0912, PLR0913, PLR0915
    inputs: FinalizationInputs,
    *,
    current: ReleaseAdmissionBindings,
    run_attempt: int,
    publisher_conclusion: str,
    publication_step_outcome: str | None,
    publication_terminal_reference: str | None,
    observation_conclusion: str | None = None,
) -> AttemptOutcome | None:
    """Admit all presented records, then select one terminal predecessor."""
    reference = parse_publication_terminal_reference(
        publication_terminal_reference,
        publisher_conclusion=publisher_conclusion,
    )
    if publication_step_outcome not in _PLATFORM_OUTCOMES | {None, ""}:
        raise ValueError(
            "Publication step outcome is not a platform terminal fact"
        )
    if observation_conclusion not in _PLATFORM_OUTCOMES | {None, ""}:
        raise ValueError("Observation conclusion is not a platform fact")
    if publisher_conclusion == "skipped" and publication_step_outcome not in {
        None,
        "",
        "skipped",
    }:
        raise ValueError(
            "Skipped Publisher cannot have an executed publication step"
        )
    _admit_qualification(inputs, current, run_attempt)
    if type(inputs.observations) is not tuple or len(inputs.observations) > 1:
        raise ValueError("Finalizer requires at most one Observation")
    for pair in inputs.observations:
        _admit_pair(pair, RemoteStateObservation, current)
    if inputs.observations and observation_conclusion == "skipped":
        raise ValueError("Skipped Observer cannot supply an Observation")
    for pair, record_type in (
        (inputs.publication, PublicationSnapshot),
        (inputs.bundle, ApprovalBundle),
        (inputs.authorization, PublicationAuthorization),
        (inputs.exact_proof, ExactSatisfiedFinalizationProof),
        (inputs.result_marker, MutationMayHaveStartedMarker),
    ):
        if pair is not None:
            _admit_pair(pair, record_type, current)
            if pair[0].attempt != inputs.attempt_binding.attempt:
                raise ValueError("Finalizer record belongs to another Attempt")
    if inputs.terminal is None:
        if reference is not None or inputs.result_marker is not None:
            raise ValueError(
                "Terminal transport or marker lineage is incomplete"
            )
    else:
        terminal, actual_reference = inputs.terminal
        if type(terminal) not in {
            MutationMayHaveStartedMarker,
            PublicationResult,
        }:
            raise ValueError("Unsupported publication terminal target schema")
        _admit_pair(inputs.terminal, type(terminal), current)
        if (
            reference != actual_reference
            or terminal.attempt != inputs.attempt_binding.attempt
        ):
            raise ValueError("Publication terminal reference mismatch")
    downstream = any(
        (
            inputs.observations,
            inputs.publication,
            inputs.bundle,
            inputs.reviewer_summary,
            inputs.authorization,
            inputs.exact_proof,
            inputs.terminal,
            inputs.result_marker,
        )
    )
    if inputs.decision.terminal_result != "success":
        if downstream:
            raise ValueError(
                "Unsuccessful Qualification cannot have publication lineage"
            )
        return None
    if len(inputs.artifacts) != 1:
        raise ValueError("Successful Qualification requires its exact artifact")
    for observation, _ in inputs.observations:
        admit_remote_state_observation(
            observation,
            intent=inputs.intent,
            attempt_binding=inputs.attempt_binding,
            eligibility=inputs.eligibility,
            policy=inputs.policy,
            snapshot=inputs.snapshot,
            decision=inputs.decision,
            decision_reference=inputs.decision_reference,
            artifact=inputs.artifacts[0],
        )
    _admit_publication(inputs)
    if inputs.reviewer_summary is not None and inputs.publication is None:
        raise ValueError(
            "Reviewer payload requires its action-bearing Snapshot"
        )
    if inputs.terminal is not None:
        terminal, terminal_reference = inputs.terminal
        if type(terminal) is PublicationResult:
            if inputs.result_marker is None:
                raise ValueError("Result must resolve its exact direct marker")
            marker, marker_reference = inputs.result_marker
            if terminal.mutation_marker_reference != marker_reference:
                raise ValueError("Result direct marker reference mismatch")
            _admit_marker(inputs, marker)
            _admit_result(inputs, terminal, marker)
            disposition = (
                "published"
                if terminal.result == "published"
                else "publication-failed"
            )
            possibly_mutated = (
                terminal.result == "failed"
                and terminal.mutation_classification != "not-mutated"
            )
            predecessor = DirectPredecessor(
                "publication-result", terminal_reference
            )
        else:
            if inputs.result_marker is not None:
                raise ValueError(
                    "Marker terminal cannot have a parallel marker"
                )
            if type(terminal) is not MutationMayHaveStartedMarker:
                raise ValueError("Unsupported publication terminal target")
            _admit_marker(inputs, terminal)
            disposition, possibly_mutated = "unknown", True
            predecessor = DirectPredecessor(
                "mutation-marker", terminal_reference
            )
    elif (
        inputs.publication is not None
        and not inputs.publication[0].materialized_actions
    ):
        if publisher_conclusion != "skipped":
            raise ValueError("Zero-action Snapshot requires skipped Publisher")
        disposition = (
            "exact-satisfied" if inputs.exact_proof is not None else "unknown"
        )
        possibly_mutated = False
        predecessor = (
            DirectPredecessor(
                "exact-satisfied-finalization-proof", inputs.exact_proof[1]
            )
            if inputs.exact_proof is not None
            else DirectPredecessor(
                "zero-action-publication-snapshot", inputs.publication[1]
            )
        )
    else:
        if inputs.authorization is not None:
            predecessor = DirectPredecessor(
                "publication-authorization", inputs.authorization[1]
            )
        elif inputs.bundle is not None:
            predecessor = DirectPredecessor("approval-bundle", inputs.bundle[1])
        elif inputs.publication is not None:
            predecessor = DirectPredecessor(
                "action-bearing-publication-snapshot", inputs.publication[1]
            )
        elif inputs.observations:
            observation, observation_reference = inputs.observations[0]
            if observation.classification in {"absent", "exact-satisfied"}:
                return None
            predecessor = DirectPredecessor(
                "blocking-observation", observation_reference
            )
        else:
            # Missing transport cannot prove interruption before Observation.
            if observation_conclusion != "skipped":
                return None
            predecessor = DirectPredecessor(
                "qualification-decision", inputs.decision_reference
            )
        if (
            publisher_conclusion == "success"
            and publication_step_outcome == "skipped"
        ):
            raise ValueError(
                "Successful Publisher with skipped publication step "
                "has no pre-marker outcome"
            )
        not_started = publisher_conclusion == "skipped" or (
            publisher_conclusion in {"failure", "cancelled"}
            and publication_step_outcome == "skipped"
        )
        disposition = "failed-before-publication" if not_started else "unknown"
        possibly_mutated = not not_started
    return AttemptOutcome(
        attempt=inputs.attempt_binding.attempt,
        disposition=disposition,
        possibly_mutated=possibly_mutated,
        direct_predecessor=predecessor,
        producer="finalize-attempt",
        control=inputs.eligibility.context.control,
        workflow_run_id=inputs.intent.workflow_run_id,
    )
