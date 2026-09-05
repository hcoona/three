"""Normal-Live approval, authorization, and current-DAG finalization."""

# ruff: noqa: EM101, PLR0913, TRY003

from __future__ import annotations

import re
from dataclasses import dataclass
from typing import TYPE_CHECKING

from three_workflow_delivery_v3.records.artifacts import ArtifactReference
from three_workflow_delivery_v3.records.release import (
    ActionResult,
    ApprovalBoundary,
    ApprovalBundle,
    AttemptOutcome,
    DestinationOperationProfile,
    DestinationProjection,
    ExactSatisfiedGovernanceProof,
    GovernanceProof,
    ProjectionObservation,
    PublicationAction,
    PublicationAuthorization,
    PublicationSnapshot,
    QualificationDecision,
    QualificationSnapshot,
    ReleaseArtifact,
    ReleaseAttemptBinding,
    ReleaseAttemptIdentity,
    ReleaseIntent,
    validate_publication_action_instantiation,
)
from three_workflow_delivery_v3.release.eligibility import (
    GovernanceObservation,
    governance_observation_provenance,
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


def form_exact_satisfied_governance_proof(
    *,
    publication_snapshot: PublicationSnapshot,
    governance: GovernanceObservation,
    proved_at: str,
    control: str,
) -> ExactSatisfiedGovernanceProof:
    """Form the fresh proof that an exact state requires no mutation."""
    if (
        type(publication_snapshot) is not PublicationSnapshot
        or publication_snapshot.materialized_actions
        or any(
            reference.classification != "exact-satisfied"
            for reference in publication_snapshot.observation_references
        )
    ):
        message = (
            "Exact-satisfied Governance proof requires an actionless exact "
            "Publication Snapshot"
        )
        raise ValueError(message)
    if type(governance) is not GovernanceObservation:
        raise TypeError(
            "Exact-satisfied proof requires fresh Governance observation"
        )
    if not governance.attestation.live_enabled:
        raise ValueError("Exact-satisfied proof requires enabled Governance")
    return ExactSatisfiedGovernanceProof(
        attempt=publication_snapshot.attempt,
        publication_snapshot=publication_snapshot,
        governance_provenance=governance_observation_provenance(governance),
        governance_current_main_sha=governance.current_main_sha,
        governance_expires_at=governance.attestation.expires_at.strftime(
            "%Y-%m-%dT%H:%M:%SZ"
        ),
        governance_live_enabled=governance.attestation.live_enabled,
        governance_observed_at=governance.observed_at.strftime(
            "%Y-%m-%dT%H:%M:%SZ"
        ),
        proved_at=proved_at,
        producer="prove-exact-satisfied",
        control=control,
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


def _outcome(
    *,
    attempt: ReleaseAttemptIdentity,
    qualification_decision: QualificationDecision,
    publication_snapshot: PublicationSnapshot | None,
    exact_satisfied_governance_proof: (
        ExactSatisfiedGovernanceProof | None
    ) = None,
    approval_bundle: ApprovalBundle | None,
    publication_authorization: PublicationAuthorization | None,
    action_results: tuple[ActionResult, ...] = (),
    observations: tuple[ProjectionObservation, ...] = (),
    terminal_phase: str,
    result: str,
    uncertainty: bool,
    possibly_mutated: bool,
    next_action: str,
) -> AttemptOutcome:
    return AttemptOutcome(
        attempt=attempt,
        qualification_decision_digest=qualification_decision.decision_digest,
        publication_snapshot_digest=(
            None
            if publication_snapshot is None
            else publication_snapshot.snapshot_digest
        ),
        exact_satisfied_governance_proof_digest=(
            None
            if exact_satisfied_governance_proof is None
            else exact_satisfied_governance_proof.proof_digest
        ),
        approval_bundle_digest=(
            None if approval_bundle is None else approval_bundle.bundle_digest
        ),
        publication_authorization_digest=(
            None
            if publication_authorization is None
            else publication_authorization.authorization_digest
        ),
        action_result_digests=tuple(
            sorted(record.result_digest for record in action_results)
        ),
        terminal_phase=terminal_phase,
        result=result,
        uncertainty=uncertainty,
        possibly_mutated=possibly_mutated,
        next_action=next_action,
        observation_digests=tuple(
            sorted(record.observation_digest for record in observations)
        ),
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


def _validate_publication_result(
    *,
    action_result: ActionResult,
    action: PublicationAction,
    publication_snapshot: PublicationSnapshot,
    qualification_decision: QualificationDecision,
    qualification_snapshot: QualificationSnapshot | None,
    release_artifact: ReleaseArtifact | None,
    destination_operation_profile: DestinationOperationProfile | None,
) -> None:
    if (
        action_result.action_id != action.action_id
        or action_result.action_digest != action.action_digest
        or action_result.lock_group != action.serialization_projection
    ):
        raise ValueError("Live finalization Action Result binding mismatch")
    receipt = action_result.receipt
    if receipt is None:
        return
    projection, admitted_artifact = _validate_publication_action_context(
        action=action,
        publication_snapshot=publication_snapshot,
        qualification_decision=qualification_decision,
        qualification_snapshot=qualification_snapshot,
        release_artifact=release_artifact,
        destination_operation_profile=destination_operation_profile,
        context="Live finalization Receipt",
    )
    if (
        receipt.coordinate != projection.coordinate
        or receipt.mutable_resource_keys != action.mutable_resource_keys
        or receipt.artifact_transport != admitted_artifact.transport
        or receipt.artifact_content_sha256
        != admitted_artifact.content.content_sha256
        or receipt.artifact_content_sha512
        != admitted_artifact.content.content_sha512
        or receipt.witness_digest != admitted_artifact.witness_digest
    ):
        raise ValueError("Live finalization Receipt binding mismatch")


def _validate_finalization_references(
    *,
    publication_snapshot: PublicationSnapshot | None,
    approval_bundle: ApprovalBundle | None,
    publication_authorization: PublicationAuthorization | None,
    publication_snapshot_reference: ArtifactReference | None,
    approval_bundle_reference: ArtifactReference | None,
) -> None:
    if approval_bundle is None:
        if publication_snapshot_reference is not None:
            raise ValueError("Live finalization Publication reference mismatch")
        if approval_bundle_reference is not None:
            raise ValueError("Live finalization Approval reference mismatch")
        return
    if (
        publication_snapshot is None
        or type(publication_snapshot_reference) is not ArtifactReference
        or publication_snapshot_reference.payload_digest
        != publication_snapshot.snapshot_digest
        or approval_bundle.publication_snapshot_reference
        != publication_snapshot_reference
    ):
        raise ValueError("Live finalization Approval Bundle mismatch")
    if (
        type(approval_bundle_reference) is not ArtifactReference
        or approval_bundle_reference.payload_digest
        != approval_bundle.bundle_digest
    ):
        raise ValueError("Live finalization Approval reference mismatch")
    if (
        publication_authorization is not None
        and publication_authorization.approval_bundle_reference
        != approval_bundle_reference
    ):
        raise ValueError("Live finalization Publication Authorization mismatch")


def _validate_finalization_inputs(
    *,
    attempt: ReleaseAttemptIdentity,
    qualification_decision: QualificationDecision,
    publication_snapshot: PublicationSnapshot | None,
    exact_satisfied_governance_proof: (ExactSatisfiedGovernanceProof | None),
    approval_bundle: ApprovalBundle | None,
    publication_authorization: PublicationAuthorization | None,
    action_results: tuple[ActionResult, ...],
    publication_snapshot_reference: ArtifactReference | None,
    approval_bundle_reference: ArtifactReference | None,
) -> None:
    if type(attempt) is not ReleaseAttemptIdentity:
        raise TypeError("Live finalization requires an exact Attempt")
    if type(qualification_decision) is not QualificationDecision:
        raise TypeError(
            "Live finalization requires an exact Qualification Decision"
        )
    if qualification_decision.subject != attempt:
        raise ValueError("Live finalization Qualification binding mismatch")
    if publication_snapshot is not None and (
        type(publication_snapshot) is not PublicationSnapshot
        or publication_snapshot.attempt != attempt
        or publication_snapshot.qualification_decision_digest
        != qualification_decision.decision_digest
        or publication_snapshot.qualification_snapshot_digest
        != qualification_decision.qualification_snapshot_digest
    ):
        raise ValueError("Live finalization Publication binding mismatch")
    if exact_satisfied_governance_proof is not None and (
        type(exact_satisfied_governance_proof)
        is not ExactSatisfiedGovernanceProof
        or publication_snapshot is None
        or exact_satisfied_governance_proof.attempt != attempt
        or exact_satisfied_governance_proof.publication_snapshot
        != publication_snapshot
    ):
        raise ValueError(
            "Live finalization exact-satisfied Governance proof mismatch"
        )
    if approval_bundle is not None and (
        type(approval_bundle) is not ApprovalBundle
        or approval_bundle.attempt != attempt
    ):
        raise ValueError("Live finalization Approval Bundle mismatch")
    if publication_authorization is not None and (
        type(publication_authorization) is not PublicationAuthorization
        or approval_bundle is None
        or publication_authorization.attempt != attempt
    ):
        raise ValueError("Live finalization Publication Authorization mismatch")
    _validate_finalization_references(
        publication_snapshot=publication_snapshot,
        approval_bundle=approval_bundle,
        publication_authorization=publication_authorization,
        publication_snapshot_reference=publication_snapshot_reference,
        approval_bundle_reference=approval_bundle_reference,
    )
    for record in action_results:
        if (
            type(record) is not ActionResult
            or publication_snapshot is None
            or record.attempt != attempt
            or record.publication_snapshot_digest
            != publication_snapshot.snapshot_digest
        ):
            raise ValueError("Live finalization Action Result mismatch")


def finalize_attempt_outcome(  # noqa: C901, PLR0911, PLR0912
    *,
    attempt: ReleaseAttemptIdentity,
    qualification_decision: QualificationDecision,
    publication_snapshot: PublicationSnapshot | None,
    exact_satisfied_governance_proof: (
        ExactSatisfiedGovernanceProof | None
    ) = None,
    approval_bundle: ApprovalBundle | None,
    publication_authorization: PublicationAuthorization | None,
    action_results: tuple[ActionResult, ...],
    qualification_snapshot: QualificationSnapshot | None = None,
    release_artifact: ReleaseArtifact | None = None,
    destination_operation_profile: DestinationOperationProfile | None = None,
    publication_snapshot_reference: ArtifactReference | None = None,
    approval_bundle_reference: ArtifactReference | None = None,
    observations: tuple[ProjectionObservation, ...] = (),
    publication_preparation_interrupted: bool = False,
    platform_terminated: bool = False,
    publication_may_have_started: bool = False,
) -> AttemptOutcome:
    """Finalize from current-DAG records without remote history queries."""
    for value in (
        publication_preparation_interrupted,
        platform_terminated,
        publication_may_have_started,
    ):
        if type(value) is not bool:
            raise TypeError("Platform termination facts must be Booleans")
    for name, values in (
        ("observations", observations),
        ("action results", action_results),
    ):
        if type(values) is not tuple:
            message = f"Live finalization {name} must be an exact tuple"
            raise TypeError(message)
    _validate_finalization_inputs(
        attempt=attempt,
        qualification_decision=qualification_decision,
        publication_snapshot=publication_snapshot,
        exact_satisfied_governance_proof=(exact_satisfied_governance_proof),
        approval_bundle=approval_bundle,
        publication_authorization=publication_authorization,
        action_results=action_results,
        publication_snapshot_reference=publication_snapshot_reference,
        approval_bundle_reference=approval_bundle_reference,
    )
    for observation in observations:
        if (
            type(observation) is not ProjectionObservation
            or observation.subject != attempt
            or observation.target != attempt.execution.target
            or observation.purpose != "live-release"
            or observation.qualification_snapshot_digest
            != qualification_decision.qualification_snapshot_digest
        ):
            raise ValueError("Live finalization Observation mismatch")
    if qualification_decision.terminal_result != "success":
        if any(
            (
                observations,
                publication_snapshot is not None,
                exact_satisfied_governance_proof is not None,
                approval_bundle is not None,
                publication_authorization is not None,
                action_results,
                publication_preparation_interrupted,
                platform_terminated,
                publication_may_have_started,
            )
        ):
            raise ValueError(
                "Unsuccessful qualification cannot bind publication records"
            )
        return _outcome(
            attempt=attempt,
            qualification_decision=qualification_decision,
            publication_snapshot=None,
            approval_bundle=None,
            publication_authorization=None,
            terminal_phase="qualification",
            result=qualification_decision.terminal_result,
            uncertainty=qualification_decision.terminal_result == "incomplete",
            possibly_mutated=False,
            next_action=qualification_decision.next_action,
        )
    if publication_preparation_interrupted:
        if any(
            (
                publication_snapshot is not None,
                exact_satisfied_governance_proof is not None,
                approval_bundle is not None,
                publication_authorization is not None,
                action_results,
                platform_terminated,
                publication_may_have_started,
            )
        ):
            raise ValueError(
                "Publication preparation interruption is contradictory"
            )
        blocking = {
            observation.value.classification for observation in observations
        } & {"partial", "conflicting", "unknown", "unprovable"}
        uncertain = bool(blocking & {"unknown", "unprovable"})
        return _outcome(
            attempt=attempt,
            qualification_decision=qualification_decision,
            publication_snapshot=None,
            approval_bundle=None,
            publication_authorization=None,
            observations=observations,
            terminal_phase=(
                "observation" if blocking else "publication-preparation"
            ),
            result=("incomplete" if uncertain or not blocking else "failure"),
            uncertainty=uncertain or not blocking,
            possibly_mutated=False,
            next_action="reconcile" if blocking else "new-attempt",
        )
    if observations:
        raise ValueError(
            "Direct Observations require publication preparation interruption"
        )
    if type(publication_snapshot) is not PublicationSnapshot:
        raise TypeError(
            "Successful qualification requires Publication Snapshot"
        )
    actions = publication_snapshot.materialized_actions
    if not actions:
        if any(
            (
                approval_bundle is not None,
                publication_authorization is not None,
                action_results,
                publication_may_have_started,
            )
        ):
            raise ValueError(
                "Exact-satisfied no-op cannot bind publication authority"
            )
        if exact_satisfied_governance_proof is None:
            return _outcome(
                attempt=attempt,
                qualification_decision=qualification_decision,
                publication_snapshot=publication_snapshot,
                approval_bundle=None,
                publication_authorization=None,
                terminal_phase="exact-satisfied-proof-missing",
                result="incomplete",
                uncertainty=True,
                possibly_mutated=False,
                next_action="new-attempt",
            )
        return _outcome(
            attempt=attempt,
            qualification_decision=qualification_decision,
            publication_snapshot=publication_snapshot,
            exact_satisfied_governance_proof=(exact_satisfied_governance_proof),
            approval_bundle=None,
            publication_authorization=None,
            terminal_phase="finalized-no-op",
            result="success",
            uncertainty=False,
            possibly_mutated=False,
            next_action="none",
        )
    if exact_satisfied_governance_proof is not None:
        raise ValueError(
            "Action-bearing publication cannot bind exact-satisfied proof"
        )
    if approval_bundle is None or publication_authorization is None:
        if action_results or publication_may_have_started:
            raise ValueError(
                "Publication activity cannot precede complete authorization"
            )
        return _outcome(
            attempt=attempt,
            qualification_decision=qualification_decision,
            publication_snapshot=publication_snapshot,
            approval_bundle=approval_bundle,
            publication_authorization=publication_authorization,
            terminal_phase="approval-contract",
            result="incomplete",
            uncertainty=True,
            possibly_mutated=False,
            next_action="new-attempt",
        )
    action = actions[0]
    if len(action_results) > 1:
        raise ValueError("Live finalization permits one Action Result")
    for result_record in action_results:
        _validate_publication_result(
            action_result=result_record,
            action=action,
            publication_snapshot=publication_snapshot,
            qualification_decision=qualification_decision,
            qualification_snapshot=qualification_snapshot,
            release_artifact=release_artifact,
            destination_operation_profile=destination_operation_profile,
        )
    if platform_terminated:
        possibly_mutated = bool(publication_may_have_started or action_results)
        return _outcome(
            attempt=attempt,
            qualification_decision=qualification_decision,
            publication_snapshot=publication_snapshot,
            approval_bundle=approval_bundle,
            publication_authorization=publication_authorization,
            action_results=action_results,
            terminal_phase=(
                "post-publication-termination"
                if possibly_mutated
                else "pre-publication-termination"
            ),
            result=(
                "incomplete-possibly-mutated"
                if possibly_mutated
                else "incomplete"
            ),
            uncertainty=True,
            possibly_mutated=possibly_mutated,
            next_action="new-attempt",
        )
    if not action_results:
        return _outcome(
            attempt=attempt,
            qualification_decision=qualification_decision,
            publication_snapshot=publication_snapshot,
            approval_bundle=approval_bundle,
            publication_authorization=publication_authorization,
            terminal_phase="publication-result-missing",
            result="incomplete",
            uncertainty=True,
            possibly_mutated=publication_may_have_started,
            next_action="new-attempt",
        )
    result_record = action_results[0]
    possibly_mutated = (
        result_record.mutation_disposition
        in {"created", "exact-race-accepted", "possibly-mutated"}
        and result_record.receipt is None
    )
    succeeded = (
        result_record.outcome == "success" and result_record.receipt is not None
    )
    return _outcome(
        attempt=attempt,
        qualification_decision=qualification_decision,
        publication_snapshot=publication_snapshot,
        approval_bundle=approval_bundle,
        publication_authorization=publication_authorization,
        action_results=action_results,
        terminal_phase="finalized",
        result=(
            "success"
            if succeeded
            else (
                "incomplete-possibly-mutated" if possibly_mutated else "failure"
            )
        ),
        uncertainty=possibly_mutated,
        possibly_mutated=possibly_mutated,
        next_action="none" if succeeded else "new-attempt",
    )


__all__ = [
    "PublicRevisionCheckout",
    "fetch_exact_public_revision",
    "finalize_attempt_outcome",
    "form_approval_bundle",
    "form_exact_satisfied_governance_proof",
    "form_publication_authorization",
    "validate_approval_bundle_closure",
]
