"""Normal-Live approval, authorization, and current-DAG finalization."""

# ruff: noqa: EM101, PLR0913, TRY003

from __future__ import annotations

import base64
import hashlib
import re
from dataclasses import dataclass
from typing import TYPE_CHECKING

from three_workflow_delivery_v3.records.artifacts import (
    ArtifactTransportIdentity,
)
from three_workflow_delivery_v3.records.release import (
    ActionResult,
    ApprovalBundle,
    AttemptOutcome,
    ExactSatisfiedGovernanceProof,
    ProjectionObservation,
    PublicationAction,
    PublicationAuthorization,
    PublicationSnapshot,
    QualificationDecision,
    ReleaseAttemptBinding,
    ReleaseAttemptIdentity,
    ReviewerSummaryArtifact,
)
from three_workflow_delivery_v3.release.eligibility import (
    GovernanceObservation,
    governance_observation_provenance,
)

if TYPE_CHECKING:
    from collections.abc import Callable

    from three_workflow_delivery_v3.canonical import JsonValue

_SHA_PATTERN = re.compile(r"[0-9a-f]{40}\Z")
_DIGEST_PATTERN = re.compile(r"sha256:[0-9a-f]{64}\Z")
_PUBLIC_REPOSITORY_URL = "https://github.com/hcoona/three.git"
_APPROVAL_ENVIRONMENT = "workflow-delivery-v3-buddy-approval"
_APPROVAL_JOB = "approve-publication"


@dataclass(frozen=True, slots=True)
class ReviewerPayload:
    """Credential-free immutable reviewer payload closure."""

    snapshot_bytes: bytes
    summary_bytes: bytes
    snapshot_payload_digest: str
    summary_payload_digest: str

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical formatter input carried across approval."""
        return {
            "schema": "workflow-delivery/v3/reviewer-formatter-input",
            "snapshot-base64": base64.b64encode(self.snapshot_bytes).decode(),
            "summary-base64": base64.b64encode(self.summary_bytes).decode(),
            "snapshot-payload-digest": self.snapshot_payload_digest,
            "summary-payload-digest": self.summary_payload_digest,
        }


@dataclass(frozen=True, slots=True)
class PublicRevisionCheckout:
    """Anonymous exact public revision materialization facts."""

    target: str
    repository_url: str
    head: str
    detached: bool
    commands: tuple[tuple[str, ...], ...]


def _sha256_bytes(payload: bytes) -> str:
    return f"sha256:{hashlib.sha256(payload).hexdigest()}"


def _require_digest(value: str, *, field: str) -> str:
    if type(value) is not str or _DIGEST_PATTERN.fullmatch(value) is None:
        message = f"{field} must be a prefixed lowercase SHA-256"
        raise ValueError(message)
    return value


def materialize_reviewer_payload(
    *,
    snapshot_bytes: bytes,
    summary_bytes: bytes,
) -> ReviewerPayload:
    """Materialize exact reviewer bytes before transport IDs exist."""
    if type(snapshot_bytes) is not bytes or type(summary_bytes) is not bytes:
        raise TypeError("reviewer payload inputs must be exact bytes")
    return ReviewerPayload(
        snapshot_bytes=snapshot_bytes,
        summary_bytes=summary_bytes,
        snapshot_payload_digest=_sha256_bytes(snapshot_bytes),
        summary_payload_digest=_sha256_bytes(summary_bytes),
    )


def bind_reviewer_artifact(
    *,
    payload: ReviewerPayload,
    attempt: ReleaseAttemptIdentity,
    artifact_id: int,
    artifact_name: str,
    artifact_url: str,
    upload_digest: str,
    workflow_run_id: int,
    snapshot_payload_digest: str,
    summary_payload_digest: str,
) -> ReviewerSummaryArtifact:
    """Bind returned Actions identity to the exact pre-upload payload."""
    if type(payload) is not ReviewerPayload:
        raise TypeError("reviewer binding requires an exact payload")
    if type(attempt) is not ReleaseAttemptIdentity:
        raise TypeError("reviewer binding requires an exact Attempt")
    if (
        snapshot_payload_digest != payload.snapshot_payload_digest
        or summary_payload_digest != payload.summary_payload_digest
    ):
        raise ValueError("reviewer payload digest substitution")
    return ReviewerSummaryArtifact(
        attempt=attempt,
        transport=ArtifactTransportIdentity(
            artifact_id=artifact_id,
            artifact_name=artifact_name,
            artifact_url=artifact_url,
            transport_digest=_require_digest(
                upload_digest,
                field="reviewer artifact upload_digest",
            ),
            producer="materialize-publication",
            workflow_run_id=workflow_run_id,
            run_attempt=None,
        ),
        snapshot_payload_digest=payload.snapshot_payload_digest,
        summary_payload_digest=payload.summary_payload_digest,
    )


def form_approval_bundle(
    *,
    attempt_binding: ReleaseAttemptBinding,
    selected_ref: str,
    qualification_decision: QualificationDecision,
    publication_snapshot: PublicationSnapshot,
    reviewer_summary: ReviewerSummaryArtifact,
    control: str,
) -> ApprovalBundle:
    """Form the complete one-action closure before the Environment wait."""
    return ApprovalBundle(
        attempt_binding=attempt_binding,
        selected_ref=selected_ref,
        qualification_decision=qualification_decision,
        publication_snapshot=publication_snapshot,
        reviewer_summary=reviewer_summary,
        environment=_APPROVAL_ENVIRONMENT,
        approval_job=_APPROVAL_JOB,
        producer="materialize-publication",
        control=control,
    )


def form_publication_authorization(
    *,
    approval_result: str,
    approval_bundle: ApprovalBundle,
    governance: GovernanceObservation,
    completed_at: str,
    control: str,
) -> PublicationAuthorization:
    """Form the sole authorization after successful Environment approval."""
    if approval_result != "success":
        raise ValueError(
            "Environment denial cannot form Publication Authorization"
        )
    if type(approval_bundle) is not ApprovalBundle:
        raise TypeError("Publication Authorization requires Approval Bundle")
    if type(governance) is not GovernanceObservation:
        raise TypeError(
            "Publication Authorization requires fresh Governance proof"
        )
    if not governance.attestation.live_enabled:
        raise ValueError(
            "Publication Authorization requires enabled Governance"
        )
    return PublicationAuthorization(
        approval_bundle=approval_bundle,
        approval_governance_provenance=(
            governance_observation_provenance(governance)
        ),
        approval_governance_current_main_sha=governance.current_main_sha,
        approval_governance_observed_at=governance.observed_at.strftime(
            "%Y-%m-%dT%H:%M:%SZ"
        ),
        approval_governance_expires_at=(
            governance.attestation.expires_at.strftime("%Y-%m-%dT%H:%M:%SZ")
        ),
        approval_governance_live_enabled=(governance.attestation.live_enabled),
        environment=_APPROVAL_ENVIRONMENT,
        approval_job=_APPROVAL_JOB,
        completed_at=completed_at,
        producer=_APPROVAL_JOB,
        control=control,
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


def _validate_publication_result(
    *,
    action_result: ActionResult,
    action: PublicationAction,
) -> None:
    if (
        action_result.action_digest != action.action_digest
        or action_result.lock_group != action.lock_group
    ):
        raise ValueError("Live finalization Action Result binding mismatch")
    receipt = action_result.receipt
    if receipt is not None and (
        receipt.action_digest != action.action_digest
        or receipt.coordinate != action.projection.coordinate
        or receipt.mutable_resource_keys != action.mutable_resource_keys
        or receipt.lock_group != action.lock_group
        or receipt.artifact_transport != action.artifact.transport
        or receipt.artifact_content_sha256
        != action.artifact.content.content_sha256
        or receipt.artifact_content_sha512
        != action.artifact.content.content_sha512
        or receipt.witness_digest != action.artifact.witness_digest
    ):
        raise ValueError("Live finalization Receipt binding mismatch")


def _validate_finalization_inputs(
    *,
    attempt: ReleaseAttemptIdentity,
    qualification_decision: QualificationDecision,
    publication_snapshot: PublicationSnapshot | None,
    exact_satisfied_governance_proof: (ExactSatisfiedGovernanceProof | None),
    approval_bundle: ApprovalBundle | None,
    publication_authorization: PublicationAuthorization | None,
    action_results: tuple[ActionResult, ...],
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
        or publication_snapshot is None
        or approval_bundle.publication_snapshot != publication_snapshot
    ):
        raise ValueError("Live finalization Approval Bundle mismatch")
    if publication_authorization is not None and (
        type(publication_authorization) is not PublicationAuthorization
        or approval_bundle is None
        or publication_authorization.approval_bundle != approval_bundle
    ):
        raise ValueError("Live finalization Publication Authorization mismatch")
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
    if (
        approval_bundle.action != action
        or publication_authorization.action != action
        or not publication_authorization.authorizing
    ):
        raise ValueError("Live finalization Authorization is not exact")
    if len(action_results) > 1:
        raise ValueError("Live finalization permits one Action Result")
    for result_record in action_results:
        _validate_publication_result(
            action_result=result_record,
            action=action,
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
    "ReviewerPayload",
    "bind_reviewer_artifact",
    "fetch_exact_public_revision",
    "finalize_attempt_outcome",
    "form_approval_bundle",
    "form_exact_satisfied_governance_proof",
    "form_publication_authorization",
    "materialize_reviewer_payload",
]
