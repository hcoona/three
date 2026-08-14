"""Release Intent normalization and post-admission simulation identity."""

from __future__ import annotations

from three_workflow_delivery_v3.canonical import canonical_sha256, canonicalize
from three_workflow_delivery_v3.records.release import (
    OFFICIAL_SIMULATION_WORKFLOW_PATH,
    BuddyExecutionIdentity,
    ExecutionHistoryAdmissionSnapshot,
    ReleaseAttemptBinding,
    ReleaseAttemptIdentity,
    ReleaseIntent,
    SimulationBinding,
    SimulationIdentity,
)
from three_workflow_delivery_v3.repository.compiler import (
    AdmittedRepositoryModelSnapshot,
    validate_first_slice_repository_model_snapshot,
)
from three_workflow_delivery_v3.repository.descriptors import (
    FIRST_SLICE_RELEASE_UNIT,
)

OFFICIAL_SIMULATION_PRODUCER = "compile-simulation-model"
BUDDY_LIVE_WORKFLOW_PATH = (
    ".github/workflows/workflow-delivery-v3-buddy-smoke.yml"
)


def _request_id(
    repository: str,
    workflow_path: str,
    workflow_run_id: int,
) -> str:
    digest = canonical_sha256(
        {
            "schema": "workflow-delivery/v3/release-request-identity",
            "repository": repository,
            "workflow-path": workflow_path,
            "workflow-run-id": workflow_run_id,
        }
    )
    return f"release-request:{digest.removeprefix('sha256:')}"


def normalize_official_simulation_intent(  # noqa: PLR0913
    *,
    repository: str,
    selected_ref: str,
    target: str,
    actor: str,
    workflow_run_id: int,
    run_attempt: int,
) -> ReleaseIntent:
    """Normalize the fixed Official simulation workflow_dispatch Intent."""
    return ReleaseIntent(
        repository=repository,
        workflow_path=OFFICIAL_SIMULATION_WORKFLOW_PATH,
        workflow_ref=selected_ref,
        workflow_sha=target,
        request_id=_request_id(
            repository,
            OFFICIAL_SIMULATION_WORKFLOW_PATH,
            workflow_run_id,
        ),
        actor=actor,
        workflow_run_id=workflow_run_id,
        run_attempt=run_attempt,
        event_kind="workflow_dispatch",
        selected_ref=selected_ref,
        target=target,
        channel="official",
        mode="simulation",
        purpose="release-simulation",
        release_unit=FIRST_SLICE_RELEASE_UNIT,
    )


def derive_simulation_binding(
    intent: ReleaseIntent,
    admitted_repository_model: AdmittedRepositoryModelSnapshot,
) -> SimulationBinding:
    """Derive Simulation Identity only after exact current model admission."""
    if type(intent) is not ReleaseIntent:
        message = "Simulation identity requires an exact ReleaseIntent"
        raise TypeError(message)
    if type(admitted_repository_model) is not AdmittedRepositoryModelSnapshot:
        message = (
            "Simulation identity requires an admitted Repository Model Snapshot"
        )
        raise TypeError(message)
    snapshot = admitted_repository_model.snapshot
    if (
        snapshot.snapshot_digest != admitted_repository_model.canonical_digest
        or canonicalize(snapshot.to_document())
        != admitted_repository_model.canonical_bytes
    ):
        message = (
            "Simulation identity Repository Model admission integrity failed"
        )
        raise ValueError(message)
    validate_first_slice_repository_model_snapshot(snapshot)
    if (
        intent.workflow_path != OFFICIAL_SIMULATION_WORKFLOW_PATH
        or intent.channel != "official"
        or intent.mode != "simulation"
        or intent.purpose != "release-simulation"
        or intent.release_unit != FIRST_SLICE_RELEASE_UNIT
    ):
        message = "Simulation identity requires the exact Official Intent"
        raise ValueError(message)
    context = snapshot.context
    expected_control = f"workflow-delivery-v3:{intent.target}"
    bindings = (
        ("request_id", context.request_id, intent.request_id),
        ("purpose", context.purpose, intent.purpose),
        ("workflow_run_id", context.workflow_run_id, intent.workflow_run_id),
        ("run_attempt", context.run_attempt, intent.run_attempt),
        ("target", context.target, intent.target),
        ("producer", context.producer, OFFICIAL_SIMULATION_PRODUCER),
        ("control", context.control, expected_control),
        ("channel", context.channel, intent.channel),
        ("release_unit", context.release_unit, intent.release_unit),
    )
    for field, actual, expected in bindings:
        if actual != expected:
            message = (
                "Simulation identity Repository Model binding mismatch: "
                f"{field}"
            )
            raise ValueError(message)
    identity_digest = canonical_sha256(
        {
            "schema": "workflow-delivery/v3/simulation-identity-basis",
            "namespace": "release-simulation",
            "request-id": intent.request_id,
            "workflow-run-id": context.workflow_run_id,
            "run-attempt": context.run_attempt,
        }
    )
    simulation = SimulationIdentity(
        namespace="release-simulation",
        request_id=intent.request_id,
        workflow_run_id=context.workflow_run_id,
        run_attempt=context.run_attempt,
        identity=(
            f"release-simulation:{identity_digest.removeprefix('sha256:')}"
        ),
    )
    return SimulationBinding(
        simulation=simulation,
        intent_digest=intent.intent_digest,
        repository_model_digest=(admitted_repository_model.canonical_digest),
        purpose="release-simulation",
        target=intent.target,
        channel=intent.channel,
        release_unit=intent.release_unit,
        control=context.control,
    )


def normalize_buddy_live_intent(  # noqa: PLR0913
    *,
    repository: str,
    selected_ref: str,
    target: str,
    actor: str,
    workflow_run_id: int,
    run_attempt: int,
) -> ReleaseIntent:
    """Normalize the strict first-slice Buddy workflow_dispatch Intent."""
    return ReleaseIntent(
        repository=repository,
        workflow_path=BUDDY_LIVE_WORKFLOW_PATH,
        workflow_ref=selected_ref,
        workflow_sha=target,
        request_id=_request_id(
            repository,
            BUDDY_LIVE_WORKFLOW_PATH,
            workflow_run_id,
        ),
        actor=actor,
        workflow_run_id=workflow_run_id,
        run_attempt=run_attempt,
        event_kind="workflow_dispatch",
        selected_ref=selected_ref,
        target=target,
        channel="buddy",
        mode="live",
        purpose="live-release",
        release_unit=FIRST_SLICE_RELEASE_UNIT,
    )


def derive_buddy_execution_identity(
    intent: ReleaseIntent,
) -> BuddyExecutionIdentity:
    """Derive Buddy Execution only from a normalized live Intent."""
    if type(intent) is not ReleaseIntent:
        message = "Buddy Execution requires an exact ReleaseIntent"
        raise TypeError(message)
    if (
        intent.workflow_path != BUDDY_LIVE_WORKFLOW_PATH
        or intent.channel != "buddy"
        or intent.mode != "live"
        or intent.purpose != "live-release"
        or intent.release_unit != FIRST_SLICE_RELEASE_UNIT
    ):
        message = "Buddy Execution requires the strict first-slice live Intent"
        raise ValueError(message)
    return BuddyExecutionIdentity(
        channel="buddy",
        release_unit=intent.release_unit,
        target=intent.target,
    )


def derive_release_attempt_binding(  # noqa: PLR0913
    *,
    intent: ReleaseIntent,
    execution: BuddyExecutionIdentity,
    repository_model_digest: str,
    live_eligibility_artifact_id: int,
    live_eligibility_artifact_digest: str,
    live_eligibility_payload_digest: str,
    attestation_provenance: tuple[tuple[str, str], ...],
    history_snapshot: ExecutionHistoryAdmissionSnapshot,
    history_snapshot_artifact_id: int,
    history_snapshot_artifact_digest: str,
) -> ReleaseAttemptBinding:
    """Create the Attempt binding after exact eligibility and history."""
    if type(intent) is not ReleaseIntent:
        message = "Attempt binding requires an exact ReleaseIntent"
        raise TypeError(message)
    if type(execution) is not BuddyExecutionIdentity:
        message = "Attempt binding requires exact Buddy Execution"
        raise TypeError(message)
    if type(history_snapshot) is not ExecutionHistoryAdmissionSnapshot:
        message = "Attempt binding requires admitted execution history"
        raise TypeError(message)
    expected_execution = derive_buddy_execution_identity(intent)
    if (
        execution != expected_execution
        or history_snapshot.execution != execution
        or history_snapshot.request_id != intent.request_id
        or history_snapshot.current_workflow_run_id != intent.workflow_run_id
        or history_snapshot.current_run_attempt != intent.run_attempt
    ):
        message = "Attempt binding pre-Attempt admission mismatch"
        raise ValueError(message)
    attempt = ReleaseAttemptIdentity(
        execution=execution,
        workflow_run_id=intent.workflow_run_id,
        run_attempt=intent.run_attempt,
    )
    return ReleaseAttemptBinding(
        intent_digest=intent.intent_digest,
        request_id=intent.request_id,
        execution=execution,
        attempt=attempt,
        repository_model_digest=repository_model_digest,
        live_eligibility_artifact_id=live_eligibility_artifact_id,
        live_eligibility_artifact_digest=live_eligibility_artifact_digest,
        live_eligibility_payload_digest=live_eligibility_payload_digest,
        attestation_provenance=attestation_provenance,
        history_snapshot_artifact_id=history_snapshot_artifact_id,
        history_snapshot_artifact_digest=history_snapshot_artifact_digest,
    )


__all__ = [
    "BUDDY_LIVE_WORKFLOW_PATH",
    "OFFICIAL_SIMULATION_PRODUCER",
    "derive_buddy_execution_identity",
    "derive_release_attempt_binding",
    "derive_simulation_binding",
    "normalize_buddy_live_intent",
    "normalize_official_simulation_intent",
]
