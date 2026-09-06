"""Normal-Live approval, authorization, and exact public revision checkout."""

# ruff: noqa: EM101, PLR0913, TRY003

from __future__ import annotations

import re
from dataclasses import dataclass
from datetime import datetime
from typing import TYPE_CHECKING

from three_workflow_delivery_v3.records.artifacts import ArtifactReference
from three_workflow_delivery_v3.records.release import (
    ApprovalBoundary,
    ApprovalBundle,
    DestinationOperationProfile,
    DestinationProjection,
    GovernanceProof,
    PublicationAction,
    PublicationAuthorization,
    PublicationSnapshot,
    QualificationDecision,
    QualificationSnapshot,
    ReleaseArtifact,
    ReleaseAttemptBinding,
    ReleaseIntent,
    validate_publication_action_instantiation,
)
from three_workflow_delivery_v3.release.eligibility import (
    GovernanceObservation,
    governance_observation_provenance,
    require_action_governance,
)
from three_workflow_delivery_v3.release.identity import (
    derive_buddy_execution_identity,
)

if TYPE_CHECKING:
    from collections.abc import Callable

_SHA_PATTERN = re.compile(r"[0-9a-f]{40}\Z")
_PUBLIC_REPOSITORY_URL = "https://github.com/hcoona/three.git"
_APPROVAL_ENVIRONMENT = "workflow-delivery-v3-buddy-approval"
_APPROVAL_JOB = "approve-publication"
_APPROVAL_SENTINEL_NAME = "WDV3_APPROVAL_ENVIRONMENT_MARKER"
_APPROVAL_SENTINEL_VALUE = "workflow-delivery-v3-buddy-approval/v1"
_REVIEWER_SUMMARY_PAYLOAD_PATH = "reviewer-summary.md"


@dataclass(frozen=True, slots=True)
class PublicRevisionCheckout:
    """Anonymous exact public revision materialization facts."""

    target: str
    repository_url: str
    head: str
    detached: bool
    commands: tuple[tuple[str, ...], ...]


def form_approval_bundle(
    *,
    intent: ReleaseIntent,
    attempt_binding: ReleaseAttemptBinding,
    qualification_decision: QualificationDecision,
    publication_snapshot: PublicationSnapshot,
    publication_snapshot_reference: ArtifactReference,
    reviewer_summary_reference: ArtifactReference,
    control: str,
) -> ApprovalBundle:
    """Validate the pre-wait closure and retain only direct references."""
    if type(intent) is not ReleaseIntent:
        raise TypeError("Approval Bundle requires an exact Release Intent")
    if type(attempt_binding) is not ReleaseAttemptBinding:
        raise TypeError("Approval Bundle requires an exact Attempt binding")
    if (
        attempt_binding.intent_digest != intent.intent_digest
        or attempt_binding.request_id != intent.request_id
        or attempt_binding.execution != derive_buddy_execution_identity(intent)
    ):
        raise ValueError("Approval Bundle Intent binding mismatch")
    if type(qualification_decision) is not QualificationDecision:
        raise TypeError("Approval Bundle requires a Qualification Decision")
    if type(publication_snapshot) is not PublicationSnapshot:
        raise TypeError("Approval Bundle requires a Publication Snapshot")
    if (
        qualification_decision.subject != attempt_binding.attempt
        or publication_snapshot.attempt != attempt_binding.attempt
        or publication_snapshot.qualification_snapshot_digest
        != qualification_decision.qualification_snapshot_digest
        or publication_snapshot.qualification_decision_digest
        != qualification_decision.decision_digest
        or qualification_decision.terminal_result != "success"
        or qualification_decision.admitted_artifact_digests
        != publication_snapshot.artifact_digests
        or len(publication_snapshot.materialized_actions) != 1
    ):
        raise ValueError("Approval Bundle qualification closure mismatch")
    if (
        type(publication_snapshot_reference) is not ArtifactReference
        or publication_snapshot_reference.payload_digest
        != publication_snapshot.snapshot_digest
    ):
        raise ValueError("Approval Bundle Snapshot reference mismatch")
    if (
        type(reviewer_summary_reference) is not ArtifactReference
        or reviewer_summary_reference.payload_path
        != _REVIEWER_SUMMARY_PAYLOAD_PATH
    ):
        raise ValueError("Approval Bundle reviewer reference mismatch")
    return ApprovalBundle(
        attempt=attempt_binding.attempt,
        publication_snapshot_reference=publication_snapshot_reference,
        reviewer_summary_reference=reviewer_summary_reference,
        producer="materialize-publication",
        control=control,
        workflow_run_id=attempt_binding.attempt.workflow_run_id,
    )


def validate_approval_bundle_closure(
    *,
    approval_bundle: ApprovalBundle,
    intent: ReleaseIntent,
    attempt_binding: ReleaseAttemptBinding,
    qualification_decision: QualificationDecision,
    qualification_snapshot: QualificationSnapshot,
    release_artifact: ReleaseArtifact,
    destination_operation_profile: DestinationOperationProfile,
    publication_snapshot: PublicationSnapshot,
    publication_snapshot_reference: ArtifactReference,
    reviewer_summary_reference: ArtifactReference,
    control: str,
) -> None:
    """Resolve and validate the exact transitive pre-wait closure."""
    expected = form_approval_bundle(
        intent=intent,
        attempt_binding=attempt_binding,
        qualification_decision=qualification_decision,
        publication_snapshot=publication_snapshot,
        publication_snapshot_reference=publication_snapshot_reference,
        reviewer_summary_reference=reviewer_summary_reference,
        control=control,
    )
    if (
        type(approval_bundle) is not ApprovalBundle
        or approval_bundle != expected
    ):
        raise ValueError("Approval Bundle resolved closure mismatch")
    _validate_publication_action_context(
        action=publication_snapshot.materialized_actions[0],
        publication_snapshot=publication_snapshot,
        qualification_decision=qualification_decision,
        qualification_snapshot=qualification_snapshot,
        release_artifact=release_artifact,
        destination_operation_profile=destination_operation_profile,
        context="Approval Bundle action",
    )


def form_publication_authorization(
    *,
    approval_bundle: ApprovalBundle,
    approval_bundle_reference: ArtifactReference,
    approval_boundary_sentinel_result: str,
    governance: GovernanceObservation,
    destination_operation_profile_digest: str,
    completed_at: str,
    control: str,
) -> PublicationAuthorization:
    """Form the sole authorization after successful Environment approval."""
    if type(approval_bundle) is not ApprovalBundle:
        raise TypeError("Publication Authorization requires Approval Bundle")
    if (
        type(approval_bundle_reference) is not ArtifactReference
        or approval_bundle_reference.payload_digest
        != approval_bundle.bundle_digest
    ):
        raise ValueError("Publication Authorization Bundle reference mismatch")
    if type(governance) is not GovernanceObservation:
        raise TypeError(
            "Publication Authorization requires fresh Governance proof"
        )
    require_action_governance(
        governance.attestation,
        now=datetime.fromisoformat(completed_at),
        destination_operation_profile_digest=destination_operation_profile_digest,
    )
    governance_proof = GovernanceProof(
        provenance=governance_observation_provenance(governance),
        current_main_sha=governance.current_main_sha,
        observed_at=governance.observed_at.strftime("%Y-%m-%dT%H:%M:%SZ"),
        expires_at=governance.attestation.expires_at.strftime(
            "%Y-%m-%dT%H:%M:%SZ"
        ),
        live_enabled=governance.attestation.live_enabled,
    )
    return PublicationAuthorization(
        attempt=approval_bundle.attempt,
        approval_bundle_reference=approval_bundle_reference,
        approval_boundary=ApprovalBoundary(
            environment=_APPROVAL_ENVIRONMENT,
            job=_APPROVAL_JOB,
            sentinel_name=_APPROVAL_SENTINEL_NAME,
            sentinel_value=_APPROVAL_SENTINEL_VALUE,
            sentinel_result=approval_boundary_sentinel_result,
        ),
        governance_proof=governance_proof,
        completed_at=completed_at,
        producer=_APPROVAL_JOB,
        control=control,
        workflow_run_id=approval_bundle.workflow_run_id,
    )


def fetch_exact_public_revision(
    *,
    target: str,
    run: Callable[[tuple[str, ...]], str],
) -> PublicRevisionCheckout:
    """Anonymously fetch only the exact public commit SHA and verify HEAD."""
    if type(target) is not str or _SHA_PATTERN.fullmatch(target) is None:
        message = "target must be a 40-character lowercase SHA"
        raise ValueError(message)
    commands = (
        ("git", "init", "."),
        ("git", "remote", "add", "origin", _PUBLIC_REPOSITORY_URL),
        ("git", "fetch", "--no-tags", "--depth=1", "origin", target),
        ("git", "checkout", "--detach", target),
        ("git", "rev-parse", "HEAD"),
        ("git", "symbolic-ref", "-q", "HEAD"),
    )
    head = ""
    symbolic = ""
    for command in commands:
        result = run(command)
        if type(result) is not str:
            message = "public revision runner returned a malformed result"
            raise TypeError(message)
        if command[-2:] == ("rev-parse", "HEAD"):
            head = result.strip()
        elif command[-3:] == ("symbolic-ref", "-q", "HEAD"):
            symbolic = result.strip()
    if head != target:
        raise ValueError("fetched commit does not match exact target")
    if symbolic:
        raise ValueError("fetched revision is not detached HEAD")
    return PublicRevisionCheckout(
        target=target,
        repository_url=_PUBLIC_REPOSITORY_URL,
        head=head,
        detached=True,
        commands=commands,
    )


def _validate_publication_action_context(
    *,
    action: PublicationAction,
    publication_snapshot: PublicationSnapshot,
    qualification_decision: QualificationDecision,
    qualification_snapshot: QualificationSnapshot | None,
    release_artifact: ReleaseArtifact | None,
    destination_operation_profile: DestinationOperationProfile | None,
    context: str,
) -> tuple[DestinationProjection, ReleaseArtifact]:
    qualification_context_error = (
        f"{context} requires exact qualification context"
    )
    destination_profile_error = (
        f"{context} requires an exact Destination Operation Profile"
    )
    context_mismatch_error = f"{context} qualification context mismatch"
    if (
        type(qualification_snapshot) is not QualificationSnapshot
        or type(release_artifact) is not ReleaseArtifact
    ):
        raise TypeError(qualification_context_error)
    if type(destination_operation_profile) is not DestinationOperationProfile:
        raise TypeError(destination_profile_error)
    if len(qualification_snapshot.destination_projections) != 1:
        raise ValueError(context_mismatch_error)
    projection = qualification_snapshot.destination_projections[0]
    if (
        qualification_snapshot.subject != publication_snapshot.attempt
        or qualification_snapshot.snapshot_digest
        != publication_snapshot.qualification_snapshot_digest
        or publication_snapshot.projection_ids != (projection.projection_id,)
        or publication_snapshot.artifact_digests
        != (release_artifact.artifact_digest,)
        or publication_snapshot.artifact_output_ids
        != (release_artifact.output.output_id,)
        or qualification_decision.admitted_artifact_digests
        != (release_artifact.artifact_digest,)
        or release_artifact.subject != publication_snapshot.attempt
        or release_artifact.qualification_snapshot_digest
        != qualification_snapshot.snapshot_digest
        or projection.output != release_artifact.output
    ):
        raise ValueError(context_mismatch_error)
    validate_publication_action_instantiation(
        action,
        destination_operation_profile=destination_operation_profile,
        projection=projection,
        artifact=release_artifact,
    )
    return projection, release_artifact


__all__ = [
    "PublicRevisionCheckout",
    "fetch_exact_public_revision",
    "form_approval_bundle",
    "form_publication_authorization",
    "validate_approval_bundle_closure",
]
