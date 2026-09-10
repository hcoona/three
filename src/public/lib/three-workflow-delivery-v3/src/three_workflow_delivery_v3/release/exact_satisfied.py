"""Fresh read-only zero-action proof and historical contextual admission."""

from __future__ import annotations

from datetime import datetime
from typing import TYPE_CHECKING

from three_workflow_delivery_v3.records.release import (
    DestinationReadback,
    ExactSatisfiedFinalizationProof,
    GovernanceProof,
    NugetDestinationReadback,
    NugetReleaseArtifact,
    NugetRemoteStateObservation,
    RemoteStateObservation,
)
from three_workflow_delivery_v3.release.eligibility import (
    governance_observation_provenance,
    require_fresh_governance_identity,
)
from three_workflow_delivery_v3.release.finalizer import (
    materialize_publication_snapshot,
)
from three_workflow_delivery_v3.release.observation import (
    classify_package_control,
)

if TYPE_CHECKING:
    from collections.abc import Callable

    from three_workflow_delivery_v3.adapters.github_packages import (
        GitHubPackagesTransport,
    )
    from three_workflow_delivery_v3.adapters.node import ArtifactExpectation
    from three_workflow_delivery_v3.adapters.nuget_github_packages import (
        NuGetAuthority,
        NuGetReadTransport,
    )
    from three_workflow_delivery_v3.records.artifacts import ArtifactReference
    from three_workflow_delivery_v3.records.release import (
        PublicationSnapshot,
        QualificationDecision,
        QualificationSnapshot,
        ReleaseArtifact,
        ReleaseAttemptBinding,
        ReleaseIntent,
    )
    from three_workflow_delivery_v3.release.eligibility import (
        AdmittedLiveEligibilityDecision,
        GovernanceSourceClient,
    )
    from three_workflow_delivery_v3.release.nuget_eligibility import (
        AdmittedNugetLiveEligibilityDecision,
    )
    from three_workflow_delivery_v3.repository.descriptors import ReleasePolicy


def validate_exact_satisfied_snapshot(  # noqa: PLR0913
    *,
    publication_snapshot: PublicationSnapshot,
    publication_snapshot_reference: ArtifactReference,
    intent: ReleaseIntent,
    attempt_binding: ReleaseAttemptBinding,
    eligibility: AdmittedLiveEligibilityDecision
    | AdmittedNugetLiveEligibilityDecision,
    policy: ReleasePolicy,
    snapshot: QualificationSnapshot,
    decision: QualificationDecision,
    decision_reference: ArtifactReference,
    artifact: ReleaseArtifact | NugetReleaseArtifact,
    observation: RemoteStateObservation | NugetRemoteStateObservation,
) -> None:
    """Admit the complete zero-action closure without IO or action authority."""
    if (
        publication_snapshot.materialized_actions
        or observation.classification != "exact-satisfied"
        or publication_snapshot_reference.payload_digest
        != publication_snapshot.snapshot_digest
    ):
        message = (
            "Exact-satisfied proof requires the exact zero-action Snapshot"
        )
        raise ValueError(message)
    # The exact branch never creates actions or checks native-acceptance age.
    expected = materialize_publication_snapshot(
        snapshot,
        decision,
        (observation,),
        (artifact,),
        intent=intent,
        attempt_binding=attempt_binding,
        eligibility=eligibility,
        policy=policy,
        decision_reference=decision_reference,
        action_creation_at=eligibility.governance.observed_at,
    )
    if publication_snapshot != expected:
        message = "Exact-satisfied proof Snapshot closure mismatch"
        raise ValueError(message)


def admit_exact_satisfied_finalization_proof(  # noqa: PLR0913
    proof: ExactSatisfiedFinalizationProof,
    *,
    publication_snapshot: PublicationSnapshot,
    publication_snapshot_reference: ArtifactReference,
    intent: ReleaseIntent,
    attempt_binding: ReleaseAttemptBinding,
    eligibility: AdmittedLiveEligibilityDecision
    | AdmittedNugetLiveEligibilityDecision,
    policy: ReleasePolicy,
    snapshot: QualificationSnapshot,
    decision: QualificationDecision,
    decision_reference: ArtifactReference,
    artifact: ReleaseArtifact | NugetReleaseArtifact,
    observation: RemoteStateObservation | NugetRemoteStateObservation,
) -> ExactSatisfiedFinalizationProof:
    """Admit evidence against original Governance, not today's clock."""
    validate_exact_satisfied_snapshot(
        publication_snapshot=publication_snapshot,
        publication_snapshot_reference=publication_snapshot_reference,
        intent=intent,
        attempt_binding=attempt_binding,
        eligibility=eligibility,
        policy=policy,
        snapshot=snapshot,
        decision=decision,
        decision_reference=decision_reference,
        artifact=artifact,
        observation=observation,
    )
    governance = eligibility.governance
    readback = proof.exact_version_readback
    if type(observation) is NugetRemoteStateObservation:
        same_version = (
            type(readback) is NugetDestinationReadback
            and readback.identity.normalized_version
            == observation.desired_identity.normalized_version
        )
    elif type(observation) is RemoteStateObservation:
        same_version = (
            type(readback) is DestinationReadback
            and readback.version == observation.desired_version
        )
    else:
        message = "Exact proof Observation variant is unsupported"
        raise TypeError(message)
    if (
        proof.attempt != publication_snapshot.attempt
        or proof.publication_snapshot_reference
        != publication_snapshot_reference
        or proof.governance_proof.provenance != governance.provenance
        or datetime.fromisoformat(proof.governance_proof.expires_at)
        != governance.attestation.expires_at
        or not governance.attestation.live_enabled
        or readback.package != observation.desired_subject.normalized_package
        or not same_version
        or readback.content_sha256 != artifact.content.content_sha256
        or readback.content_sha512 != artifact.content.content_sha512
        or readback.witness_digest != artifact.witness_digest
        or classify_package_control(
            proof.package_control_proof,
            subject=observation.desired_subject,
            eligibility=eligibility,
        )
        != "ready"
    ):
        message = (
            "Exact-satisfied finalization proof authority binding mismatch"
        )
        raise ValueError(message)
    # Constructors own post-read/expiry bounds. Context owns the lower bound:
    # fresh evidence cannot predate the initial Observation or Eligibility.
    initial_times = [governance.observed_at]
    if observation.package_control is not None:
        initial_times.append(
            datetime.fromisoformat(observation.package_control.observed_at)
        )
    if observation.active_readback is not None:
        initial_times.append(
            datetime.fromisoformat(observation.active_readback.observed_at)
        )
    for observed_at in (
        proof.governance_proof.observed_at,
        proof.package_control_proof.observed_at,
        readback.observed_at,
    ):
        if datetime.fromisoformat(observed_at) < max(initial_times):
            message = "Exact-satisfied finalization proof evidence is stale"
            raise ValueError(message)
    for observed_at in (
        proof.package_control_proof.observed_at,
        readback.observed_at,
    ):
        if datetime.fromisoformat(observed_at) < datetime.fromisoformat(
            proof.governance_proof.observed_at
        ):
            message = (
                "Exact-satisfied remote evidence predates fresh Governance"
            )
            raise ValueError(message)
    return proof


def prove_exact_satisfied(  # noqa: PLR0913
    *,
    publication_snapshot: PublicationSnapshot,
    publication_snapshot_reference: ArtifactReference,
    intent: ReleaseIntent,
    attempt_binding: ReleaseAttemptBinding,
    eligibility: AdmittedLiveEligibilityDecision,
    policy: ReleasePolicy,
    snapshot: QualificationSnapshot,
    decision: QualificationDecision,
    decision_reference: ArtifactReference,
    artifact: ReleaseArtifact,
    observation: RemoteStateObservation,
    publisher_conclusion: str,
    expectation: ArtifactExpectation,
    governance_client: GovernanceSourceClient,
    transport: GitHubPackagesTransport,
    token: str,
    clock: Callable[[], datetime],
) -> ExactSatisfiedFinalizationProof:
    """Re-read Governance and actual exact bytes after closure admission."""
    from three_workflow_delivery_v3.adapters.github_packages import (  # noqa: PLC0415
        read_github_packages_active_state,
    )

    if publisher_conclusion != "skipped":
        message = "Exact-satisfied proof requires publisher skipped"
        raise ValueError(message)
    validate_exact_satisfied_snapshot(
        publication_snapshot=publication_snapshot,
        publication_snapshot_reference=publication_snapshot_reference,
        intent=intent,
        attempt_binding=attempt_binding,
        eligibility=eligibility,
        policy=policy,
        snapshot=snapshot,
        decision=decision,
        decision_reference=decision_reference,
        artifact=artifact,
        observation=observation,
    )
    if (
        expectation.package_name
        != observation.desired_subject.normalized_package
        or expectation.npm_package_version != observation.desired_version
    ):
        message = (
            "Exact-satisfied expectation differs from qualified desired state"
        )
        raise ValueError(message)
    initial = eligibility.governance
    fresh = require_fresh_governance_identity(
        policy.governance,
        governance_client,
        now=clock(),
        expected_provenance=initial.provenance,
        expected_canonical_content_digest=initial.canonical_content_digest,
        expected_expires_at=initial.attestation.expires_at.isoformat().replace(
            "+00:00", "Z"
        ),
        expected_live_enabled=initial.attestation.live_enabled,
    )
    state = read_github_packages_active_state(
        artifact,
        expectation,
        token=token,
        transport=transport,
        observed_at=clock().isoformat().replace("+00:00", "Z"),
    )
    control = classify_package_control(
        state.package_control,
        subject=observation.desired_subject,
        eligibility=eligibility,
    )
    if (
        state.readback.classification != "exact-satisfied"
        or state.package_control is None
        or control != "ready"
    ):
        message = (
            "Fresh exact-satisfied state is not exact: "
            f"version={state.readback.classification}, "
            f"package-control={control}"
        )
        raise ValueError(message)
    proof = ExactSatisfiedFinalizationProof(
        attempt=publication_snapshot.attempt,
        publication_snapshot_reference=publication_snapshot_reference,
        governance_proof=GovernanceProof(
            provenance=governance_observation_provenance(fresh),
            current_main_sha=fresh.current_main_sha,
            observed_at=fresh.observed_at.isoformat().replace("+00:00", "Z"),
            expires_at=fresh.attestation.expires_at.isoformat().replace(
                "+00:00", "Z"
            ),
            live_enabled=fresh.attestation.live_enabled,
        ),
        package_control_proof=state.package_control,
        exact_version_readback=state.readback,
        proved_at=clock().isoformat().replace("+00:00", "Z"),
        producer="prove-exact-satisfied",
        control=eligibility.context.control,
        workflow_run_id=intent.workflow_run_id,
    )
    return admit_exact_satisfied_finalization_proof(
        proof,
        publication_snapshot=publication_snapshot,
        publication_snapshot_reference=publication_snapshot_reference,
        intent=intent,
        attempt_binding=attempt_binding,
        eligibility=eligibility,
        policy=policy,
        snapshot=snapshot,
        decision=decision,
        decision_reference=decision_reference,
        artifact=artifact,
        observation=observation,
    )


def prove_nuget_exact_satisfied(  # noqa: PLR0913
    *,
    publication_snapshot: PublicationSnapshot,
    publication_snapshot_reference: ArtifactReference,
    intent: ReleaseIntent,
    attempt_binding: ReleaseAttemptBinding,
    eligibility: AdmittedNugetLiveEligibilityDecision,
    policy: ReleasePolicy,
    snapshot: QualificationSnapshot,
    decision: QualificationDecision,
    decision_reference: ArtifactReference,
    artifact: NugetReleaseArtifact,
    observation: NugetRemoteStateObservation,
    publisher_conclusion: str,
    authority: NuGetAuthority,
    governance_client: GovernanceSourceClient,
    transport: NuGetReadTransport,
    token: str,
    clock: Callable[[], datetime],
) -> ExactSatisfiedFinalizationProof:
    """Recheck the protected source and actual native bytes for zero actions."""
    from three_workflow_delivery_v3.adapters.nuget_github_packages import (  # noqa: PLC0415
        read_nuget_active_state,
    )
    from three_workflow_delivery_v3.release.nuget_observation import (  # noqa: PLC0415
        classify_nuget_package_control,
        nuget_readback_from_state,
    )

    if publisher_conclusion != "skipped":
        message = "NuGet exact-satisfied proof requires publisher skipped"
        raise ValueError(message)
    validate_exact_satisfied_snapshot(
        publication_snapshot=publication_snapshot,
        publication_snapshot_reference=publication_snapshot_reference,
        intent=intent,
        attempt_binding=attempt_binding,
        eligibility=eligibility,
        policy=policy,
        snapshot=snapshot,
        decision=decision,
        decision_reference=decision_reference,
        artifact=artifact,
        observation=observation,
    )
    initial = eligibility.governance
    fresh = require_fresh_governance_identity(
        policy.governance,
        governance_client,
        now=clock(),
        expected_provenance=initial.provenance,
        expected_canonical_content_digest=initial.canonical_content_digest,
        expected_expires_at=initial.attestation.expires_at.isoformat().replace(
            "+00:00", "Z"
        ),
        expected_live_enabled=initial.attestation.live_enabled,
    )
    state = read_nuget_active_state(
        transport=transport,
        authority=authority,
        token=token,
        package_id=artifact.identity.package_name,
        version=artifact.identity.native_version,
    )
    control, readback = nuget_readback_from_state(
        state,
        artifact=artifact,
        observed_at=clock().isoformat().replace("+00:00", "Z"),
    )
    if (
        readback.classification != "exact-satisfied"
        or classify_nuget_package_control(
            control,
            subject=observation.desired_subject,
            eligibility=eligibility,
        )
        != "ready"
    ):
        message = (
            "NuGet fresh exact-satisfied state or package control is not exact"
        )
        raise ValueError(message)
    proof = ExactSatisfiedFinalizationProof(
        publication_snapshot.attempt,
        publication_snapshot_reference,
        GovernanceProof(
            governance_observation_provenance(fresh),
            fresh.current_main_sha,
            fresh.observed_at.isoformat().replace("+00:00", "Z"),
            fresh.attestation.expires_at.isoformat().replace("+00:00", "Z"),
            fresh.attestation.live_enabled,
        ),
        control,
        readback,
        clock().isoformat().replace("+00:00", "Z"),
        "prove-exact-satisfied",
        eligibility.context.control,
        intent.workflow_run_id,
    )
    return admit_exact_satisfied_finalization_proof(
        proof,
        publication_snapshot=publication_snapshot,
        publication_snapshot_reference=publication_snapshot_reference,
        intent=intent,
        attempt_binding=attempt_binding,
        eligibility=eligibility,
        policy=policy,
        snapshot=snapshot,
        decision=decision,
        decision_reference=decision_reference,
        artifact=artifact,
        observation=observation,
    )
