"""Pure commit-8 live admission and finalization helpers."""

# ruff: noqa: EM101, PLR0913, TRY003

from __future__ import annotations

import base64
import hashlib
import re
from dataclasses import dataclass
from datetime import UTC, datetime
from typing import TYPE_CHECKING

from three_workflow_delivery_v3.records.release import (
    PUBLISHER_GOVERNANCE_RECHECK_FAILED_BEFORE_RUNNER,
    ActionResult,
    AttemptOutcome,
    AuthorizationRecord,
    BuddyExecutionIdentity,
    CapabilityAdmissionDecision,
    ProjectionObservation,
    PublicationAction,
    PublicationSnapshot,
    QualificationDecision,
    ReleaseAttemptIdentity,
)

if TYPE_CHECKING:
    from collections.abc import Callable

    from three_workflow_delivery_v3.canonical import JsonValue

_SHA_PATTERN = re.compile(r"[0-9a-f]{40}\Z")
_DIGEST_PATTERN = re.compile(r"sha256:[0-9a-f]{64}\Z")
_PUBLIC_REPOSITORY_URL = "https://github.com/hcoona/three.git"
_DEFAULT_TARGET = "a" * 40
_DEFAULT_CONTROL = f"workflow-delivery-v3:{_DEFAULT_TARGET}"
_DEFAULT_EXPIRES_AT = "2026-09-01T00:00:00Z"
_DEFAULT_OBSERVED_AT = datetime(2026, 8, 13, 0, 0, 0, tzinfo=UTC)
_GOVERNANCE_PROVENANCE_FIELDS = frozenset(
    {
        "repository",
        "ref",
        "path",
        "resolved-commit",
        "blob-oid",
        "content-sha256",
    }
)
_SUBSTITUTION_DIAGNOSTICS = {
    "disabled": "governance-live-disabled",
    "expired": "governance-attestation-expired",
    "resolved-commit": "governance-provenance-changed",
    "blob": "governance-provenance-changed",
    "content": "governance-content-changed",
    "binding": "governance-binding-changed",
}


@dataclass(frozen=True, slots=True)
class ReviewerArtifact:
    """Exact reviewer-summary transport and immutable bytes."""

    snapshot_bytes: bytes
    summary_bytes: bytes
    snapshot_payload_digest: str
    summary_payload_digest: str
    artifact_id: int
    upload_digest: str


@dataclass(frozen=True, slots=True)
class ReviewerPayload:
    """Credential-free immutable reviewer payload closure."""

    snapshot_bytes: bytes
    summary_bytes: bytes
    snapshot_payload_digest: str
    summary_payload_digest: str

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical formatter input carried across the approval."""
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


@dataclass(frozen=True, slots=True)
class LiveCapabilityAdmissionResult:
    """Current blocked admission plus restored new-attempt admission."""

    current_attempt: CapabilityAdmissionDecision
    restored_attempt: CapabilityAdmissionDecision


def _sha256_bytes(payload: bytes) -> str:
    return f"sha256:{hashlib.sha256(payload).hexdigest()}"


def _require_digest(value: str, *, field: str) -> str:
    if type(value) is not str or _DIGEST_PATTERN.fullmatch(value) is None:
        message = f"{field} must be a prefixed lowercase SHA-256"
        raise ValueError(message)
    return value


def _require_positive_integer(value: int, *, field: str) -> int:
    if type(value) is not int or value <= 0:
        message = f"{field} must be a positive integer"
        raise ValueError(message)
    return value


def _parse_utc(value: str, *, field: str) -> datetime:
    if type(value) is not str:
        message = f"{field} must be a UTC instant"
        raise TypeError(message)
    try:
        return datetime.strptime(value, "%Y-%m-%dT%H:%M:%SZ").replace(
            tzinfo=UTC
        )
    except ValueError as error:
        message = f"{field} must be a UTC second-precision instant"
        raise ValueError(message) from error


def _governance_diagnostics(
    *,
    provenance: tuple[tuple[str, str], ...],
    content_sha256: str,
    expires_at: str,
    live_enabled: bool,
    observed_at: datetime,
    expected_provenance: tuple[tuple[str, str], ...] | None,
    expected_content_sha256: str | None,
    expected_expires_at: str | None,
    expected_live_enabled: bool | None,
) -> tuple[str, ...]:
    if observed_at.tzinfo is None or observed_at.utcoffset() is None:
        message = "fresh Governance observation time must be timezone-aware"
        raise ValueError(message)
    diagnostics: list[str] = []
    if {name for name, _ in provenance} != _GOVERNANCE_PROVENANCE_FIELDS:
        diagnostics.append("governance-provenance-changed")
    elif dict(provenance)["content-sha256"] != content_sha256:
        diagnostics.append("governance-content-changed")
    if expected_provenance is not None and provenance != expected_provenance:
        diagnostics.append("governance-provenance-changed")
    if (
        expected_content_sha256 is not None
        and content_sha256 != expected_content_sha256
    ):
        diagnostics.append("governance-content-changed")
    if expected_expires_at is not None and expires_at != expected_expires_at:
        diagnostics.append("governance-binding-changed")
    if (
        expected_live_enabled is not None
        and live_enabled is not expected_live_enabled
    ):
        diagnostics.append("governance-binding-changed")
    if not live_enabled:
        diagnostics.append("governance-live-disabled")
    if _parse_utc(expires_at, field="governance_expires_at") <= observed_at:
        diagnostics.append("governance-attestation-expired")
    return tuple(dict.fromkeys(diagnostics))


def materialize_reviewer_artifact(
    *,
    snapshot_bytes: bytes,
    summary_bytes: bytes,
    artifact_id: int,
    upload_digest: str,
) -> ReviewerArtifact:
    """Preserve exact reviewer Snapshot/summary bytes and transport digest."""
    if type(snapshot_bytes) is not bytes or type(summary_bytes) is not bytes:
        message = "reviewer artifact inputs must be exact bytes"
        raise TypeError(message)
    return ReviewerArtifact(
        snapshot_bytes=snapshot_bytes,
        summary_bytes=summary_bytes,
        snapshot_payload_digest=_sha256_bytes(snapshot_bytes),
        summary_payload_digest=_sha256_bytes(summary_bytes),
        artifact_id=_require_positive_integer(
            artifact_id,
            field="artifact_id",
        ),
        upload_digest=_require_digest(upload_digest, field="upload_digest"),
    )


def materialize_reviewer_payload(
    *,
    snapshot_bytes: bytes,
    summary_bytes: bytes,
) -> ReviewerPayload:
    """Materialize exact reviewer bytes before Actions assigns transport IDs."""
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
    artifact_id: int,
    upload_digest: str,
    snapshot_payload_digest: str,
    summary_payload_digest: str,
) -> ReviewerArtifact:
    """Bind returned Actions identity only to the exact pre-upload payload."""
    if type(payload) is not ReviewerPayload:
        raise TypeError("reviewer binding requires an exact payload")
    if (
        snapshot_payload_digest != payload.snapshot_payload_digest
        or summary_payload_digest != payload.summary_payload_digest
    ):
        raise ValueError("reviewer payload digest substitution")
    return materialize_reviewer_artifact(
        snapshot_bytes=payload.snapshot_bytes,
        summary_bytes=payload.summary_bytes,
        artifact_id=artifact_id,
        upload_digest=upload_digest,
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
        message = "fetched commit does not match exact target"
        raise ValueError(message)
    if symbolic:
        message = "fetched revision is not detached HEAD"
        raise ValueError(message)
    return PublicRevisionCheckout(
        target=target,
        repository_url=_PUBLIC_REPOSITORY_URL,
        head=head,
        detached=True,
        commands=commands,
    )


def form_authorization_record(
    *,
    approval_result: str,
    attempt: ReleaseAttemptIdentity | None = None,
    publication_snapshot: PublicationSnapshot | None = None,
    reviewer_artifact: ReviewerArtifact | None = None,
    approval_job_id: int = 1,
    completed_at: str = "2026-08-13T16:00:00Z",
    control: str | None = None,
    diagnostic: object | None = None,
    schedule_capability: Callable[[], object] | None = None,
) -> AuthorizationRecord:
    """Form Authorization only for successful current Environment approval."""
    del diagnostic, schedule_capability
    if approval_result != "success":
        message = "GitHub deployment-review denial is diagnostic-only"
        raise ValueError(message)
    if (
        type(attempt) is not ReleaseAttemptIdentity
        or type(publication_snapshot) is not PublicationSnapshot
        or type(reviewer_artifact) is not ReviewerArtifact
    ):
        message = "successful authorization requires exact current bindings"
        raise TypeError(message)
    if publication_snapshot.attempt != attempt:
        message = "Authorization Publication Snapshot Attempt mismatch"
        raise ValueError(message)
    expected_control = f"workflow-delivery-v3:{attempt.execution.target}"
    if control is not None and control != expected_control:
        message = "Authorization control binding mismatch"
        raise ValueError(message)
    return AuthorizationRecord(
        attempt=attempt,
        publication_snapshot_digest=publication_snapshot.snapshot_digest,
        reviewer_summary_artifact_id=reviewer_artifact.artifact_id,
        reviewer_summary_upload_digest=reviewer_artifact.upload_digest,
        reviewer_summary_payload_digest=(
            reviewer_artifact.summary_payload_digest
        ),
        workflow_run_id=attempt.workflow_run_id,
        approval_job_id=approval_job_id,
        approval_job="approval",
        environment="workflow-delivery-v3-buddy-approval",
        channel="buddy",
        completed_at=completed_at,
        producer="approval",
        control=expected_control,
    )


def _demo_attempt(*, workflow_run_id: int = 1) -> ReleaseAttemptIdentity:
    return ReleaseAttemptIdentity(
        execution=BuddyExecutionIdentity(
            channel="buddy",
            release_unit="hcoona-release-smoke-npm",
            target=_DEFAULT_TARGET,
        ),
        workflow_run_id=workflow_run_id,
    )


def _capability_decision(
    *,
    attempt: ReleaseAttemptIdentity,
    authorization_digest: str,
    publication_snapshot_digest: str,
    reviewer_artifact: ReviewerArtifact,
    result: str,
    diagnostics: tuple[str, ...],
    live_eligibility_artifact_id: int,
    live_eligibility_artifact_digest: str,
    governance_provenance: tuple[tuple[str, str], ...],
    governance_content_sha256: str,
    governance_expires_at: str,
    governance_live_enabled: bool,
    control: str,
    action_digests: tuple[str, ...] = (),
    artifact_digests: tuple[str, ...] = (),
    resource_key_sets: tuple[tuple[str, tuple[str, ...]], ...] = (),
    lock_groups: tuple[tuple[str, str], ...] = (),
) -> CapabilityAdmissionDecision:
    return CapabilityAdmissionDecision(
        attempt=attempt,
        authorization_digest=authorization_digest,
        publication_snapshot_digest=publication_snapshot_digest,
        reviewer_summary_artifact_id=reviewer_artifact.artifact_id,
        reviewer_summary_upload_digest=reviewer_artifact.upload_digest,
        reviewer_summary_payload_digest=reviewer_artifact.summary_payload_digest,
        action_digests=action_digests,
        artifact_digests=artifact_digests,
        resource_key_sets=resource_key_sets,
        lock_groups=lock_groups,
        live_eligibility_artifact_id=live_eligibility_artifact_id,
        live_eligibility_artifact_digest=live_eligibility_artifact_digest,
        governance_provenance=governance_provenance,
        governance_content_sha256=governance_content_sha256,
        governance_expires_at=governance_expires_at,
        governance_live_enabled=governance_live_enabled,
        producer="approval-finalizer",
        control=control,
        workflow_run_id=attempt.workflow_run_id,
        result=result,
        diagnostics=diagnostics,
    )


def admit_live_capability(
    *,
    attempt: ReleaseAttemptIdentity | None = None,
    authorization: AuthorizationRecord | None = None,
    publication_snapshot: PublicationSnapshot | None = None,
    reviewer_artifact: ReviewerArtifact | None = None,
    live_eligibility_artifact_id: int = 1,
    live_eligibility_artifact_digest: str = "sha256:" + ("1" * 64),
    governance_provenance: tuple[tuple[str, str], ...] = (
        ("blob-oid", "blob"),
        ("content-sha256", "sha256:" + ("2" * 64)),
        (
            "path",
            (
                ".github/workflow-delivery/governance/"
                "hcoona-release-smoke-npm.json"
            ),
        ),
        ("ref", "refs/heads/main"),
        ("repository", "hcoona/three"),
        ("resolved-commit", _DEFAULT_TARGET),
    ),
    governance_content_sha256: str = "sha256:" + ("2" * 64),
    governance_expires_at: str = _DEFAULT_EXPIRES_AT,
    governance_live_enabled: bool = True,
    governance_observed_at: datetime = _DEFAULT_OBSERVED_AT,
    expected_governance_provenance: tuple[tuple[str, str], ...] | None = None,
    expected_governance_content_sha256: str | None = None,
    expected_governance_expires_at: str | None = None,
    expected_governance_live_enabled: bool | None = None,
    control: str | None = None,
    substitution: str | None = None,
    restored: bool = False,
) -> CapabilityAdmissionDecision | LiveCapabilityAdmissionResult:
    """Admit capability only on exact fresh Governance and bindings."""
    if substitution is not None:
        current = _demo_attempt()
        synthetic_control = _DEFAULT_CONTROL if control is None else control
        reviewer = materialize_reviewer_artifact(
            snapshot_bytes=b"{}",
            summary_bytes=b"{}",
            artifact_id=1,
            upload_digest="sha256:" + ("3" * 64),
        )
        typed = _SUBSTITUTION_DIAGNOSTICS.get(substitution)
        if typed is None:
            message = "unsupported Governance substitution"
            raise ValueError(message)
        blocked = _capability_decision(
            attempt=current,
            authorization_digest="sha256:" + ("4" * 64),
            publication_snapshot_digest="sha256:" + ("5" * 64),
            reviewer_artifact=reviewer,
            result="blocked",
            diagnostics=(typed,),
            live_eligibility_artifact_id=1,
            live_eligibility_artifact_digest="sha256:" + ("1" * 64),
            governance_provenance=governance_provenance,
            governance_content_sha256=governance_content_sha256,
            governance_expires_at=governance_expires_at,
            governance_live_enabled=substitution != "disabled",
            control=synthetic_control,
        )
        restored_decision = _capability_decision(
            attempt=_demo_attempt(workflow_run_id=2 if restored else 1),
            authorization_digest="sha256:" + ("4" * 64),
            publication_snapshot_digest="sha256:" + ("5" * 64),
            reviewer_artifact=reviewer,
            result="success",
            diagnostics=(),
            live_eligibility_artifact_id=1,
            live_eligibility_artifact_digest="sha256:" + ("1" * 64),
            governance_provenance=governance_provenance,
            governance_content_sha256=governance_content_sha256,
            governance_expires_at=governance_expires_at,
            governance_live_enabled=True,
            control=synthetic_control,
            action_digests=("sha256:" + ("6" * 64),),
            artifact_digests=("sha256:" + ("7" * 64),),
            resource_key_sets=(("action:restored", ("resource:restored",)),),
            lock_groups=(("action:restored", "lock:restored"),),
        )
        return LiveCapabilityAdmissionResult(
            current_attempt=blocked,
            restored_attempt=restored_decision,
        )
    if (
        type(attempt) is not ReleaseAttemptIdentity
        or type(authorization) is not AuthorizationRecord
        or type(publication_snapshot) is not PublicationSnapshot
        or type(reviewer_artifact) is not ReviewerArtifact
    ):
        message = "capability admission requires exact current bindings"
        raise TypeError(message)
    if (
        authorization.attempt != attempt
        or publication_snapshot.attempt != attempt
        or authorization.publication_snapshot_digest
        != publication_snapshot.snapshot_digest
    ):
        message = "Capability admission current binding mismatch"
        raise ValueError(message)
    expected_control = f"workflow-delivery-v3:{attempt.execution.target}"
    if authorization.control != expected_control or (
        control is not None and control != expected_control
    ):
        message = "Capability admission control binding mismatch"
        raise ValueError(message)
    if (
        authorization.reviewer_summary_artifact_id
        != reviewer_artifact.artifact_id
        or authorization.reviewer_summary_upload_digest
        != reviewer_artifact.upload_digest
        or authorization.reviewer_summary_payload_digest
        != reviewer_artifact.summary_payload_digest
        or reviewer_artifact.snapshot_payload_digest
        != publication_snapshot.snapshot_digest
    ):
        message = "Capability admission reviewer artifact mismatch"
        raise ValueError(message)
    actions = publication_snapshot.materialized_actions
    action_digests = tuple(sorted(action.action_digest for action in actions))
    artifact_digests = tuple(
        sorted(action.artifact_digest for action in actions)
    )
    resource_key_sets = tuple(
        sorted(
            (action.action_id, action.mutable_resource_keys)
            for action in actions
        )
    )
    lock_groups = tuple(
        sorted((action.action_id, action.lock_group) for action in actions)
    )
    diagnostics = _governance_diagnostics(
        provenance=governance_provenance,
        content_sha256=governance_content_sha256,
        expires_at=governance_expires_at,
        live_enabled=governance_live_enabled,
        observed_at=governance_observed_at,
        expected_provenance=expected_governance_provenance,
        expected_content_sha256=expected_governance_content_sha256,
        expected_expires_at=expected_governance_expires_at,
        expected_live_enabled=expected_governance_live_enabled,
    )
    return _capability_decision(
        attempt=attempt,
        authorization_digest=authorization.authorization_digest,
        publication_snapshot_digest=publication_snapshot.snapshot_digest,
        reviewer_artifact=reviewer_artifact,
        result="success" if not diagnostics else "blocked",
        diagnostics=diagnostics,
        live_eligibility_artifact_id=live_eligibility_artifact_id,
        live_eligibility_artifact_digest=live_eligibility_artifact_digest,
        governance_provenance=governance_provenance,
        governance_content_sha256=governance_content_sha256,
        governance_expires_at=governance_expires_at,
        governance_live_enabled=governance_live_enabled,
        control=expected_control,
        action_digests=action_digests,
        artifact_digests=artifact_digests,
        resource_key_sets=resource_key_sets,
        lock_groups=lock_groups,
    )


def _outcome(
    *,
    attempt: ReleaseAttemptIdentity,
    qualification_decision: QualificationDecision,
    publication_snapshot: PublicationSnapshot | None,
    authorization: AuthorizationRecord | None,
    capability_decisions: tuple[CapabilityAdmissionDecision, ...] = (),
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
        authorization_digest=(
            None
            if authorization is None
            else authorization.authorization_digest
        ),
        capability_admission_digests=tuple(
            sorted(
                decision.decision_digest for decision in capability_decisions
            )
        ),
        action_result_digests=tuple(
            sorted(result.result_digest for result in action_results)
        ),
        terminal_phase=terminal_phase,
        result=result,
        uncertainty=uncertainty,
        possibly_mutated=possibly_mutated,
        next_action=next_action,
        observation_digests=tuple(
            sorted(
                observation.observation_digest for observation in observations
            )
        ),
    )


def _validate_publication_result(
    *,
    action_result: ActionResult,
    action: object,
) -> None:
    if type(action) is not PublicationAction:
        raise TypeError("Live finalization Action has the wrong type")
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


def finalize_attempt_outcome(  # noqa: C901, PLR0911, PLR0912, PLR0915
    *,
    attempt: ReleaseAttemptIdentity,
    qualification_decision: QualificationDecision,
    publication_snapshot: PublicationSnapshot | None,
    authorization: AuthorizationRecord | None,
    capability_decisions: tuple[CapabilityAdmissionDecision, ...],
    action_results: tuple[ActionResult, ...],
    observations: tuple[ProjectionObservation, ...] = (),
    publication_preparation_interrupted: bool = False,
    platform_terminated: bool = False,
    capability_may_have_started: bool = False,
) -> AttemptOutcome:
    """Finalize from current-DAG records without remote history queries."""
    if type(attempt) is not ReleaseAttemptIdentity:
        raise TypeError("Live finalization requires an exact Attempt")
    if type(qualification_decision) is not QualificationDecision:
        raise TypeError(
            "Live finalization requires an exact Qualification Decision"
        )
    if qualification_decision.subject != attempt:
        raise ValueError("Live finalization Qualification binding mismatch")
    if any(
        type(value) is not bool
        for value in (
            publication_preparation_interrupted,
            platform_terminated,
            capability_may_have_started,
        )
    ):
        raise TypeError("Platform termination facts must be exact Booleans")
    for name, values in (
        ("observations", observations),
        ("capability decisions", capability_decisions),
        ("action results", action_results),
    ):
        if type(values) is not tuple:
            message = f"Live finalization {name} must be an exact tuple"
            raise TypeError(message)
    observation_projections: set[str] = set()
    for observation in observations:
        if type(observation) is not ProjectionObservation:
            raise TypeError("Live finalization Observation has the wrong type")
        if (
            observation.subject != attempt
            or observation.target != attempt.execution.target
            or observation.purpose != "live-release"
            or observation.qualification_snapshot_digest
            != qualification_decision.qualification_snapshot_digest
        ):
            raise ValueError("Live finalization Observation binding mismatch")
        projection_id = observation.projection.projection_id
        if projection_id in observation_projections:
            raise ValueError(
                "Live finalization Observations repeat a projection"
            )
        observation_projections.add(projection_id)
    if qualification_decision.terminal_result != "success":
        if (
            observations
            or publication_snapshot is not None
            or authorization is not None
            or capability_decisions
            or action_results
            or publication_preparation_interrupted
            or platform_terminated
            or capability_may_have_started
        ):
            raise ValueError(
                "Unsuccessful qualification cannot bind publication records"
            )
        return _outcome(
            attempt=attempt,
            qualification_decision=qualification_decision,
            publication_snapshot=None,
            authorization=None,
            terminal_phase="qualification",
            result=qualification_decision.terminal_result,
            uncertainty=qualification_decision.terminal_result == "incomplete",
            possibly_mutated=False,
            next_action=qualification_decision.next_action,
        )
    if publication_preparation_interrupted:
        if (
            publication_snapshot is not None
            or authorization is not None
            or capability_decisions
            or action_results
            or platform_terminated
            or capability_may_have_started
        ):
            raise ValueError(
                "Publication preparation interruption has contradictory records"
            )
        blocking = {
            observation.value.classification for observation in observations
        } & {"partial", "conflicting", "unknown", "unprovable"}
        if blocking:
            uncertainty = bool(blocking & {"unknown", "unprovable"})
            return _outcome(
                attempt=attempt,
                qualification_decision=qualification_decision,
                publication_snapshot=None,
                authorization=None,
                observations=observations,
                terminal_phase="observation",
                result="incomplete" if uncertainty else "failure",
                uncertainty=uncertainty,
                possibly_mutated=False,
                next_action="reconcile",
            )
        return _outcome(
            attempt=attempt,
            qualification_decision=qualification_decision,
            publication_snapshot=None,
            authorization=None,
            observations=observations,
            terminal_phase="publication-preparation",
            result="incomplete",
            uncertainty=True,
            possibly_mutated=False,
            next_action="new-attempt",
        )
    if observations:
        raise ValueError(
            "Direct Observations require publication preparation interruption"
        )
    if type(publication_snapshot) is not PublicationSnapshot:
        raise TypeError(
            "Successful qualification requires Publication Snapshot"
        )
    if publication_snapshot.attempt != attempt:
        raise ValueError(
            "Live finalization Publication Snapshot Attempt mismatch"
        )
    if (
        publication_snapshot.qualification_decision_digest
        != qualification_decision.decision_digest
        or publication_snapshot.qualification_snapshot_digest
        != qualification_decision.qualification_snapshot_digest
    ):
        raise ValueError("Live finalization Qualification binding mismatch")
    if authorization is not None:
        if type(authorization) is not AuthorizationRecord:
            raise TypeError(
                "Live finalization Authorization has the wrong type"
            )
        if (
            authorization.attempt != attempt
            or authorization.publication_snapshot_digest
            != publication_snapshot.snapshot_digest
        ):
            raise ValueError("Live finalization Authorization binding mismatch")
    for decision in capability_decisions:
        if type(decision) is not CapabilityAdmissionDecision:
            raise TypeError(
                "Live finalization Capability Decision has the wrong type"
            )
        if (
            decision.attempt != attempt
            or decision.publication_snapshot_digest
            != publication_snapshot.snapshot_digest
        ):
            raise ValueError(
                "Mixed-attempt failed-job reruns are not admissible"
            )
    for result_record in action_results:
        if type(result_record) is not ActionResult:
            raise TypeError(
                "Live finalization Action Result has the wrong type"
            )
        if (
            result_record.attempt != attempt
            or result_record.publication_snapshot_digest
            != publication_snapshot.snapshot_digest
        ):
            raise ValueError(
                "Mixed-attempt failed-job reruns are not admissible"
            )
    expected_control = f"workflow-delivery-v3:{attempt.execution.target}"
    if authorization is not None and authorization.control != expected_control:
        raise ValueError("Authorization control binding mismatch")
    if any(
        decision.control != expected_control
        for decision in capability_decisions
    ):
        raise ValueError("Capability Decision control binding mismatch")
    if any(
        result_record.control != expected_control
        for result_record in action_results
    ):
        raise ValueError("Action Result control binding mismatch")
    actions = publication_snapshot.materialized_actions
    action_by_id = {action.action_id: action for action in actions}
    for result_record in action_results:
        action = action_by_id.get(result_record.action_id)
        if action is None:
            raise ValueError("Live finalization Action Result binding mismatch")
        _validate_publication_result(
            action_result=result_record,
            action=action,
        )
    if platform_terminated:
        possibly_mutated = bool(capability_may_have_started or action_results)
        return _outcome(
            attempt=attempt,
            qualification_decision=qualification_decision,
            publication_snapshot=publication_snapshot,
            authorization=authorization,
            capability_decisions=capability_decisions,
            action_results=action_results,
            terminal_phase=(
                "post-capability-termination"
                if possibly_mutated
                else "pre-capability-termination"
            ),
            result=(
                "incomplete-possibly-mutated"
                if possibly_mutated
                else "replayable-no-side-effect"
            ),
            uncertainty=possibly_mutated,
            possibly_mutated=possibly_mutated,
            next_action=(
                "reobserve-and-replay" if possibly_mutated else "replay"
            ),
        )
    if authorization is None:
        contradictory = bool(capability_decisions or action_results)
        return _outcome(
            attempt=attempt,
            qualification_decision=qualification_decision,
            publication_snapshot=publication_snapshot,
            authorization=None,
            capability_decisions=capability_decisions,
            action_results=action_results,
            terminal_phase=(
                "authorization-contradiction"
                if contradictory
                else "approval-contract"
            ),
            result=(
                "incomplete-possibly-mutated"
                if contradictory
                else "unknown-replayable-approval-contract"
            ),
            uncertainty=True,
            possibly_mutated=contradictory,
            next_action=("reobserve-and-replay" if contradictory else "replay"),
        )
    if not actions:
        if capability_decisions or action_results:
            raise ValueError(
                "Exact pre-observed no-op cannot emit publication records"
            )
        return _outcome(
            attempt=attempt,
            qualification_decision=qualification_decision,
            publication_snapshot=publication_snapshot,
            authorization=authorization,
            terminal_phase="finalized-no-op",
            result="success",
            uncertainty=False,
            possibly_mutated=False,
            next_action="none",
        )
    blocked_decisions = tuple(
        decision
        for decision in capability_decisions
        if not decision.authorizing
    )
    if blocked_decisions:
        if len(blocked_decisions) != len(capability_decisions):
            raise ValueError("Live finalization Capability Decisions conflict")
        if action_results:
            raise ValueError(
                "Blocked capability cannot have publication results"
            )
        return _outcome(
            attempt=attempt,
            qualification_decision=qualification_decision,
            publication_snapshot=publication_snapshot,
            authorization=authorization,
            capability_decisions=capability_decisions,
            terminal_phase="capability-blocked",
            result="failure",
            uncertainty=False,
            possibly_mutated=False,
            next_action="new-attempt",
        )
    if len(capability_decisions) != 1:
        raise ValueError(
            "Live finalization requires one exact Capability Decision"
        )
    decision = capability_decisions[0]
    expected_action_digests = tuple(
        sorted(action.action_digest for action in actions)
    )
    expected_artifact_digests = tuple(
        sorted(action.artifact_digest for action in actions)
    )
    expected_resource_key_sets = tuple(
        sorted(
            (action.action_id, action.mutable_resource_keys)
            for action in actions
        )
    )
    expected_lock_groups = tuple(
        sorted((action.action_id, action.lock_group) for action in actions)
    )
    if (
        not decision.authorizing
        or decision.authorization_digest != authorization.authorization_digest
        or decision.action_digests != expected_action_digests
        or decision.artifact_digests != expected_artifact_digests
        or decision.resource_key_sets != expected_resource_key_sets
        or decision.lock_groups != expected_lock_groups
    ):
        raise ValueError("Live finalization Capability Decision is not exact")
    result_by_action = {
        result_record.action_id: result_record
        for result_record in action_results
    }
    if (
        len(action_by_id) != len(actions)
        or len(result_by_action) != len(action_results)
        or result_by_action.keys() != action_by_id.keys()
    ):
        raise ValueError(
            "Live finalization requires one direct result per action"
        )
    missing_receipt_after_possible_mutation = any(
        result_record.mutation_disposition
        in {"created", "exact-race-accepted", "possibly-mutated"}
        and result_record.receipt is None
        for result_record in action_results
    )
    all_success = all(
        result_record.outcome == "success" and result_record.receipt is not None
        for result_record in action_results
    )
    publisher_governance_blocked = (
        not missing_receipt_after_possible_mutation
        and any(
            result_record.outcome == "failed"
            and result_record.mutation_disposition == "no-side-effect"
            and result_record.diagnostic_reference
            == PUBLISHER_GOVERNANCE_RECHECK_FAILED_BEFORE_RUNNER
            for result_record in action_results
        )
    )
    return _outcome(
        attempt=attempt,
        qualification_decision=qualification_decision,
        publication_snapshot=publication_snapshot,
        authorization=authorization,
        capability_decisions=capability_decisions,
        action_results=action_results,
        terminal_phase=(
            "capability-blocked"
            if publisher_governance_blocked
            else "finalized"
        ),
        result=(
            "success"
            if all_success
            else (
                "incomplete-possibly-mutated"
                if missing_receipt_after_possible_mutation
                else "failure"
            )
        ),
        uncertainty=missing_receipt_after_possible_mutation,
        possibly_mutated=missing_receipt_after_possible_mutation,
        next_action=(
            "none"
            if all_success
            else (
                "new-attempt"
                if publisher_governance_blocked
                else (
                    "reobserve-and-replay"
                    if missing_receipt_after_possible_mutation
                    else "replay"
                )
            )
        ),
    )


__all__ = [
    "LiveCapabilityAdmissionResult",
    "PublicRevisionCheckout",
    "ReviewerArtifact",
    "ReviewerPayload",
    "admit_live_capability",
    "bind_reviewer_artifact",
    "fetch_exact_public_revision",
    "finalize_attempt_outcome",
    "form_authorization_record",
    "materialize_reviewer_artifact",
    "materialize_reviewer_payload",
]
