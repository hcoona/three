"""Release Intent normalization and post-admission simulation identity."""

from __future__ import annotations

from three_workflow_delivery_v3.canonical import canonical_sha256, canonicalize
from three_workflow_delivery_v3.records.release import (
    OFFICIAL_SIMULATION_WORKFLOW_PATH,
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


__all__ = [
    "OFFICIAL_SIMULATION_PRODUCER",
    "derive_simulation_binding",
    "normalize_official_simulation_intent",
]
