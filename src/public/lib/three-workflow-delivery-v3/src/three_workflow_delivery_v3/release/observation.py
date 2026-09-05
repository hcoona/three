"""Authority-first observation runtime and pure contextual admission."""

from __future__ import annotations

from datetime import datetime, timedelta
from typing import TYPE_CHECKING, Literal, cast

from three_workflow_delivery_v3.catalogs import DESTINATION_DEFINITIONS
from three_workflow_delivery_v3.records.release import (
    DestinationReadback,
    PackageControlProof,
    PackageControlSubject,
    PublicationDiagnostics,
    RemoteStateObservation,
)
from three_workflow_delivery_v3.release.eligibility import (
    AdmittedLiveEligibilityDecision,
    EnabledGovernanceActivation,
    release_policy_digest,
    require_action_governance,
)
from three_workflow_delivery_v3.release.identity import (
    derive_buddy_execution_identity,
    derive_release_attempt_binding,
)
from three_workflow_delivery_v3.release.qualification import (
    validate_qualification_artifacts,
    validate_qualification_decision,
    validate_qualification_decision_artifacts,
)
from three_workflow_delivery_v3.repository.compiler import (
    compile_release_policy,
)
from three_workflow_delivery_v3.repository.descriptors import (
    FIRST_SLICE_PACKAGE,
    GOVERNANCE_MAX_AGE_DAYS,
    GOVERNANCE_REPOSITORY,
)

if TYPE_CHECKING:
    from three_workflow_delivery_v3.adapters.github_packages import (
        GitHubPackagesTransport,
    )
    from three_workflow_delivery_v3.adapters.node import ArtifactExpectation
    from three_workflow_delivery_v3.records.artifacts import ArtifactReference
    from three_workflow_delivery_v3.records.release import (
        DestinationProjection,
        QualificationDecision,
        QualificationSnapshot,
        ReleaseArtifact,
        ReleaseAttemptBinding,
        ReleaseIntent,
    )
    from three_workflow_delivery_v3.repository.descriptors import ReleasePolicy

_DESTINATION = DESTINATION_DEFINITIONS["npm/github-packages-hcoona-three-v1"]


def _validate_package_subject(
    subject: PackageControlSubject,
    eligibility: AdmittedLiveEligibilityDecision,
) -> None:
    attestation = eligibility.governance.attestation
    principal = attestation.package_principal
    if (
        subject.destination_id != _DESTINATION.logical_id
        or subject.registry != _DESTINATION.registry
        or subject.normalized_package != FIRST_SLICE_PACKAGE
        or subject.normalized_package != attestation.package
        or subject.normalized_package != principal.intended_coordinate
        or principal.repository != GOVERNANCE_REPOSITORY
    ):
        message = "Observation package subject does not match Governance"
        raise ValueError(message)


def classify_package_control(
    proof: PackageControlProof | None,
    *,
    subject: PackageControlSubject,
    eligibility: AdmittedLiveEligibilityDecision,
) -> Literal["ready", "conflicting", "unprovable"]:
    """Compare observed supported facts with admitted Governance and policy.

    Call only after contextual basis admission. This is not an ACL inventory:
    the supported USER package API exposes no package-grant facts. In
    particular, repository.permissions cannot populate exposed-access.
    """
    _validate_package_subject(subject, eligibility)
    if proof is None:
        return "unprovable"
    if proof.subject != subject:
        return "conflicting"
    repository = eligibility.governance.attestation.package_principal.repository
    owner = repository.split("/", 1)[0]
    resource = subject.normalized_package.removeprefix(f"@{owner}/")
    endpoint = f"https://api.github.com/users/{owner}/packages/npm/{resource}"
    if proof.endpoints != (endpoint,):
        return "unprovable"
    expected_facts = (
        ("exposed-access", ()),
        ("owner", (owner,)),
        ("repository-association", (repository,)),
        ("visibility", ("public",)),
    )
    return "ready" if proof.facts == expected_facts else "conflicting"


def _validate_basis(  # noqa: PLR0913
    *,
    intent: ReleaseIntent,
    attempt_binding: ReleaseAttemptBinding,
    eligibility: AdmittedLiveEligibilityDecision,
    policy: ReleasePolicy,
    snapshot: QualificationSnapshot,
    decision: QualificationDecision,
    decision_reference: ArtifactReference,
    artifact: ReleaseArtifact,
) -> DestinationProjection:
    if type(eligibility) is not AdmittedLiveEligibilityDecision:
        message = "Observation requires parser-admitted Live Eligibility"
        raise TypeError(message)
    context = eligibility.context
    if (
        context.request_id,
        context.selected_ref,
        context.target,
        context.workflow_run_id,
        context.control,
        context.release_policy_digest,
    ) != (
        intent.request_id,
        intent.selected_ref,
        intent.target,
        intent.workflow_run_id,
        f"workflow-delivery-v3:{intent.target}",
        release_policy_digest(policy),
    ):
        message = "Observation Eligibility does not match current Intent"
        raise ValueError(message)
    expected_binding = derive_release_attempt_binding(
        intent=intent,
        execution=derive_buddy_execution_identity(intent),
        repository_model_digest=context.repository_model_digest,
        live_eligibility_artifact_id=(
            attempt_binding.live_eligibility_artifact_id
        ),
        live_eligibility_artifact_digest=(
            attempt_binding.live_eligibility_artifact_digest
        ),
        live_eligibility_payload_digest=eligibility.canonical_digest,
        attestation_provenance=eligibility.governance.provenance,
    )
    if attempt_binding != expected_binding:
        message = (
            "Observation Attempt binding differs from admitted Eligibility"
        )
        raise ValueError(message)
    if (
        snapshot.subject != attempt_binding.attempt
        or snapshot.repository != intent.repository
        or snapshot.repository != eligibility.governance.source.repository
        or snapshot.repository_model_digest != context.repository_model_digest
        or snapshot.release_policy_digest
        != compile_release_policy(policy).policy_digest
    ):
        message = "Observation Snapshot differs from current Eligibility"
        raise ValueError(message)
    if decision_reference.payload_digest != decision.decision_digest:
        message = "Observation Decision reference payload digest mismatch"
        raise ValueError(message)
    validate_qualification_decision(snapshot, decision)
    if decision.terminal_result != "success":
        message = "Observation requires successful Qualification"
        raise ValueError(message)
    artifacts = validate_qualification_artifacts(snapshot, (artifact,))
    validate_qualification_decision_artifacts(decision, artifacts)
    if len(snapshot.destination_projections) != 1:
        message = "Observation requires one planned destination projection"
        raise ValueError(message)
    projection = snapshot.destination_projections[0]
    requests = tuple(
        request
        for request in snapshot.build_requests
        if request.output == projection.output
    )
    if (
        artifact.output != projection.output
        or len(requests) != 1
        or artifact.build_request_digest != requests[0].request_digest
        or artifact.witness_digest != requests[0].witness_digest
    ):
        message = "Observation artifact differs from planned build or witness"
        raise ValueError(message)
    _validate_package_subject(
        PackageControlSubject(
            destination_id=projection.destination_id,
            registry=projection.registry,
            normalized_package=projection.coordinate.package_name,
        ),
        eligibility,
    )
    return projection


def _require_current_eligibility(
    eligibility: AdmittedLiveEligibilityDecision,
    now: datetime,
) -> None:
    if now.tzinfo is None or now.utcoffset() is None:
        message = "Observation admission time must be timezone-aware"
        raise ValueError(message)
    governance = eligibility.governance
    # The admitted wrapper does not retain CURRENT_FRESHNESS versus
    # AUTHORIZATION_REPLAY. Recheck only the temporal part, not its schema.
    if not governance.observed_at <= now < governance.attestation.expires_at:
        message = "Observation requires currently fresh Live Eligibility"
        raise ValueError(message)


def _require_action_freshness(
    eligibility: AdmittedLiveEligibilityDecision,
    now: datetime,
) -> None:
    _require_current_eligibility(eligibility, now)
    from three_workflow_delivery_v3.adapters.github_packages import (  # noqa: PLC0415
        github_packages_destination_operation_profile,
    )

    require_action_governance(
        eligibility.governance.attestation,
        now=now,
        destination_operation_profile_digest=(
            github_packages_destination_operation_profile().profile_digest
        ),
    )


def _native_acceptance_expiry(
    eligibility: AdmittedLiveEligibilityDecision,
) -> datetime:
    activation = cast(
        "EnabledGovernanceActivation",
        eligibility.governance.attestation.activation,
    )
    return activation.destination_primitive.captured_at + timedelta(
        days=GOVERNANCE_MAX_AGE_DAYS
    )


def validate_remote_state_observation_basis(  # noqa: PLR0913
    *,
    intent: ReleaseIntent,
    attempt_binding: ReleaseAttemptBinding,
    eligibility: AdmittedLiveEligibilityDecision,
    policy: ReleasePolicy,
    snapshot: QualificationSnapshot,
    decision: QualificationDecision,
    decision_reference: ArtifactReference,
    artifact: ReleaseArtifact,
    now: datetime,
) -> DestinationProjection:
    """Validate current authority before any remote read, without doing IO.

    Inputs are canonical parser-admitted records. The shared artifact loader
    owns transport service metadata; decision_reference is the actual
    producer-returned tuple, not a lookup hint. The admitted Eligibility
    already closes Intent/Model/policy/Governance parsing and passing status.
    Policy supplies its existing normalized and compiled digest derivations.
    No Model copy or fresh protected-source IO is introduced here.
    """
    projection = _validate_basis(
        intent=intent,
        attempt_binding=attempt_binding,
        eligibility=eligibility,
        policy=policy,
        snapshot=snapshot,
        decision=decision,
        decision_reference=decision_reference,
        artifact=artifact,
    )
    _require_current_eligibility(eligibility, now)
    return projection


def admit_remote_state_observation(  # noqa: PLR0913
    observation: RemoteStateObservation,
    *,
    intent: ReleaseIntent,
    attempt_binding: ReleaseAttemptBinding,
    eligibility: AdmittedLiveEligibilityDecision,
    policy: ReleasePolicy,
    snapshot: QualificationSnapshot,
    decision: QualificationDecision,
    decision_reference: ArtifactReference,
    artifact: ReleaseArtifact,
    action_creation_at: datetime | None = None,
) -> RemoteStateObservation:
    """Admit completed evidence; never turn blocking facts into ready state.

    Action materializers must supply their current action_creation_at.
    Read-only historical finalizers omit it: admission checks the recorded
    absence times, not today's clock, and grants no action-creation authority.
    Such callers may use Eligibility parsed under AUTHORIZATION_REPLAY.
    Constructors already own readback/ready shape and exactness, including
    the absence-only tag constraint; this function owns cross-record closure.
    """
    projection = _validate_basis(
        intent=intent,
        attempt_binding=attempt_binding,
        eligibility=eligibility,
        policy=policy,
        snapshot=snapshot,
        decision=decision,
        decision_reference=decision_reference,
        artifact=artifact,
    )
    subject = PackageControlSubject(
        destination_id=projection.destination_id,
        registry=projection.registry,
        normalized_package=projection.coordinate.package_name,
    )
    if (
        observation.attempt != attempt_binding.attempt
        or observation.qualification_decision_reference != decision_reference
        or observation.desired_subject != subject
        or observation.desired_version != projection.coordinate.native_version
        or observation.desired_content_sha256 != artifact.content.content_sha256
        or observation.desired_content_sha512 != artifact.content.content_sha512
        or observation.desired_witness_digest != artifact.witness_digest
    ):
        message = (
            "Remote-State Observation differs from qualified desired state"
        )
        raise ValueError(message)
    control = classify_package_control(
        observation.package_control, subject=subject, eligibility=eligibility
    )
    if observation.classification in {"absent", "exact-satisfied"} and (
        control != "ready"
    ):
        message = "Ready Observation package control is not admitted"
        raise ValueError(message)
    if observation.classification == "absent":
        readback = cast("DestinationReadback", observation.active_readback)
        proof = cast("PackageControlProof", observation.package_control)
        if action_creation_at is not None:
            _require_action_freshness(eligibility, action_creation_at)
        for observed_at in (readback.observed_at, proof.observed_at):
            instant = datetime.fromisoformat(observed_at)
            _require_action_freshness(eligibility, instant)
            if action_creation_at is not None and instant > action_creation_at:
                message = "Action creation cannot precede Observation evidence"
                raise ValueError(message)
    return observation


def observe_remote_state(  # noqa: PLR0913
    *,
    intent: ReleaseIntent,
    attempt_binding: ReleaseAttemptBinding,
    eligibility: AdmittedLiveEligibilityDecision,
    policy: ReleasePolicy,
    snapshot: QualificationSnapshot,
    decision: QualificationDecision,
    decision_reference: ArtifactReference,
    artifact: ReleaseArtifact,
    expectation: ArtifactExpectation,
    token: str,
    transport: GitHubPackagesTransport,
    now: datetime,
) -> RemoteStateObservation:
    """Read active facts only after admitting the complete current authority."""
    from three_workflow_delivery_v3.adapters.github_packages import (  # noqa: PLC0415
        read_github_packages_active_state,
    )

    projection = validate_remote_state_observation_basis(
        intent=intent,
        attempt_binding=attempt_binding,
        eligibility=eligibility,
        policy=policy,
        snapshot=snapshot,
        decision=decision,
        decision_reference=decision_reference,
        artifact=artifact,
        now=now,
    )
    subject = PackageControlSubject(
        projection.destination_id,
        projection.registry,
        projection.coordinate.package_name,
    )
    if (
        expectation.package_name != subject.normalized_package
        or expectation.npm_package_version
        != projection.coordinate.native_version
    ):
        message = "Observation expectation differs from qualified projection"
        raise ValueError(message)
    state = read_github_packages_active_state(
        artifact,
        expectation,
        token=token,
        transport=transport,
        observed_at=now.isoformat().replace("+00:00", "Z"),
    )
    control = classify_package_control(
        state.package_control, subject=subject, eligibility=eligibility
    )
    readback = state.readback
    blockers: set[str] = set()
    diagnostics = list(state.diagnostics.entries)
    if control != "ready":
        blockers.add(control)
        diagnostics.append(f"package-control admission: {control}")
    if readback.classification not in {"absent", "exact-satisfied"}:
        blockers.add(readback.classification)
    if readback.classification == "absent":
        if readback.tag_state != "absent":
            blockers.add(
                "conflicting"
                if readback.tag_state == "present"
                else "unprovable"
            )
            diagnostics.append(
                f"absent-version target-tag: {readback.tag_state}"
            )
        if now > _native_acceptance_expiry(eligibility):
            blockers.add("unprovable")
            diagnostics.append("absent-version native acceptance: expired")
    # Different blocking dimensions have no normative priority. Preserve all
    # evidence and report the combined state as unprovable.
    classification = (
        next(iter(blockers))
        if len(blockers) == 1
        else "unprovable"
        if blockers
        else readback.classification
    )
    observation = RemoteStateObservation(
        attempt=attempt_binding.attempt,
        qualification_decision_reference=decision_reference,
        desired_subject=subject,
        desired_version=projection.coordinate.native_version,
        desired_content_sha256=artifact.content.content_sha256,
        desired_content_sha512=cast("str", artifact.content.content_sha512),
        desired_witness_digest=artifact.witness_digest,
        classification=classification,
        package_control=state.package_control,
        active_readback=readback,
        response_identity=state.response_identity,
        diagnostics=PublicationDiagnostics(
            entries=tuple(diagnostics), truncated=state.diagnostics.truncated
        ),
        producer="observe-github-packages",
        control=eligibility.context.control,
        workflow_run_id=intent.workflow_run_id,
    )
    return admit_remote_state_observation(
        observation,
        intent=intent,
        attempt_binding=attempt_binding,
        eligibility=eligibility,
        policy=policy,
        snapshot=snapshot,
        decision=decision,
        decision_reference=decision_reference,
        artifact=artifact,
    )
