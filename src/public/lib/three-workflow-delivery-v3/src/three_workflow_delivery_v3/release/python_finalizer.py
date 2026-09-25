"""Python variant admission into the shared scalar Attempt Outcome contract."""

from __future__ import annotations

from dataclasses import dataclass
from typing import TYPE_CHECKING

from three_workflow_delivery_v3.records.release import (
    AttemptOutcome,
    DirectPredecessor,
)
from three_workflow_delivery_v3.release.python_publication import (
    PythonApprovalBundle,
    PythonMutationMarker,
    PythonPublicationAuthorization,
    PythonPublicationResult,
    PythonPublicationSnapshot,
    PythonRemoteObservation,
    _exact_files,
    _instant,
    _reference,
    audit_python_publication_result,
)

if TYPE_CHECKING:
    from datetime import datetime

    from three_workflow_delivery_v3.adapters.pypi import PythonIndexObservation
    from three_workflow_delivery_v3.canonical import JsonValue
    from three_workflow_delivery_v3.records.artifacts import ArtifactReference
    from three_workflow_delivery_v3.records.release_transport import (
        ReleaseAdmissionBindings,
    )
    from three_workflow_delivery_v3.release.python_governance import (
        PythonGovernance,
    )
    from three_workflow_delivery_v3.release.python_qualification import (
        PythonQualificationDecision,
    )


@dataclass(frozen=True, slots=True)
class PythonExactSatisfiedProof:
    """Fresh zero-action whole-set proof without Environment or credentials."""

    snapshot: PythonPublicationSnapshot
    snapshot_reference: ArtifactReference
    fresh_governance: PythonGovernance
    native: PythonIndexObservation
    observed_at: datetime

    def __post_init__(self) -> None:
        """Reject reused observation, changed Governance and partial state."""
        _reference(self.snapshot_reference, self.snapshot.to_document())
        decision = self.snapshot.observation.decision
        decision.snapshot.governance.require_same_live(
            self.fresh_governance, self.observed_at
        )
        if (
            self.snapshot.action_required
            or self.observed_at <= self.snapshot.observation.observed_at
            or self.fresh_governance.observed_at
            <= self.snapshot.observation.observed_at
            or not _exact_files(self.native, decision)
        ):
            message = (
                "Python zero-action proof is not fresh complete exact state"
            )
            raise ValueError(message)

    def to_document(self) -> dict[str, JsonValue]:
        """Retain the current Snapshot predecessor and fresh exact readback."""
        return {
            "schema": "workflow-delivery/v3/python-exact-satisfied-proof",
            "attempt": self.snapshot.attempt.to_document(),
            "snapshot-reference": self.snapshot_reference.to_document(),
            "governance-digest": self.fresh_governance.digest,
            "governance-source-commit": self.fresh_governance.source_commit,
            "governance-observed-at": _instant(
                self.fresh_governance.observed_at
            ),
            "native": self.native.to_document(),
            "observed-at": _instant(self.observed_at),
            "producer": "finalize-attempt",
        }


@dataclass(frozen=True, slots=True)
class PythonFinalizationInputs:
    """Transient admitted records, preserving all direct predecessor links."""

    decision: PythonQualificationDecision
    decision_reference: ArtifactReference
    observation: tuple[PythonRemoteObservation, ArtifactReference] | None = None
    publication: tuple[PythonPublicationSnapshot, ArtifactReference] | None = (
        None
    )
    bundle: tuple[PythonApprovalBundle, ArtifactReference] | None = None
    authorization: (
        tuple[PythonPublicationAuthorization, ArtifactReference] | None
    ) = None
    exact_proof: tuple[PythonExactSatisfiedProof, ArtifactReference] | None = (
        None
    )
    terminal: (
        tuple[PythonPublicationResult | PythonMutationMarker, ArtifactReference]
        | None
    ) = None
    result_marker: tuple[PythonMutationMarker, ArtifactReference] | None = None


def finalize_python_attempt_outcome(  # noqa: C901, PLR0912, PLR0913, PLR0915
    inputs: PythonFinalizationInputs,
    *,
    current: ReleaseAdmissionBindings,
    run_attempt: int,
    publisher_conclusion: str,
    publication_step_outcome: str | None,
    publication_terminal_reference: str | None,
    observation_conclusion: str | None = None,
) -> AttemptOutcome | None:
    """Admit Python lineage and reuse the existing scalar/Outcome semantics."""
    from three_workflow_delivery_v3.release.attempt_finalizer import (  # noqa: PLC0415
        parse_publication_terminal_reference,
    )

    reference = parse_publication_terminal_reference(
        publication_terminal_reference,
        publisher_conclusion=publisher_conclusion,
    )
    snapshot = inputs.decision.snapshot
    attempt = snapshot.attempt
    if (
        type(run_attempt) is not int
        or run_attempt != 1
        or current.purpose != "live-release"
        or current.workflow_run_id != attempt.workflow_run_id
        or current.run_attempt is not None
        or current.target != attempt.execution.target
    ):
        message = "Python Finalizer current Attempt or control mismatch"
        raise ValueError(message)
    _reference(inputs.decision_reference, inputs.decision.to_document())
    allowed = {"success", "failure", "cancelled", "skipped", None, ""}
    if (
        publication_step_outcome not in allowed
        or observation_conclusion not in allowed
    ):
        message = "Python Finalizer requires direct platform conclusions"
        raise ValueError(message)
    if publisher_conclusion == "skipped" and publication_step_outcome not in {
        None,
        "",
        "skipped",
    }:
        message = "Skipped Python publisher has an executed publication step"
        raise ValueError(message)
    for pair in (
        inputs.observation,
        inputs.publication,
        inputs.bundle,
        inputs.authorization,
        inputs.exact_proof,
        inputs.terminal,
        inputs.result_marker,
    ):
        if pair is not None:
            _reference(pair[1], pair[0].to_document())
    if inputs.decision.result != "passed":
        if any(
            (
                inputs.observation,
                inputs.publication,
                inputs.bundle,
                inputs.authorization,
                inputs.exact_proof,
                inputs.terminal,
                inputs.result_marker,
            )
        ):
            message = (
                "Failed Python Qualification cannot have downstream authority"
            )
            raise ValueError(message)
        return None
    predecessor = DirectPredecessor(
        "qualification-decision", inputs.decision_reference
    )
    if inputs.observation is not None:
        observation, observation_ref = inputs.observation
        if (
            observation.decision != inputs.decision
            or observation.decision_reference != inputs.decision_reference
            or observation_conclusion == "skipped"
        ):
            message = "Python Finalizer observation lineage mismatch"
            raise ValueError(message)
        predecessor = DirectPredecessor("blocking-observation", observation_ref)
    if inputs.publication is not None:
        publication, publication_ref = inputs.publication
        if inputs.observation != (
            publication.observation,
            publication.observation_reference,
        ):
            message = "Python Finalizer Snapshot lineage mismatch"
            raise ValueError(message)
        predecessor = DirectPredecessor(
            "action-bearing-publication-snapshot"
            if publication.action_required
            else "zero-action-publication-snapshot",
            publication_ref,
        )
    if inputs.bundle is not None:
        bundle, bundle_ref = inputs.bundle
        if inputs.publication != (bundle.snapshot, bundle.snapshot_reference):
            message = "Python Finalizer Bundle lineage mismatch"
            raise ValueError(message)
        predecessor = DirectPredecessor("approval-bundle", bundle_ref)
    if inputs.authorization is not None:
        authorization, auth_ref = inputs.authorization
        if inputs.bundle != (
            authorization.bundle,
            authorization.bundle_reference,
        ):
            message = "Python Finalizer Authorization lineage mismatch"
            raise ValueError(message)
        predecessor = DirectPredecessor("publication-authorization", auth_ref)
    disposition = "failed-before-publication"
    possibly_mutated = False
    if inputs.terminal is not None:
        terminal, terminal_ref = inputs.terminal
        if (
            terminal_ref != reference
            or terminal.attempt != attempt
            or publisher_conclusion == "skipped"
            or inputs.exact_proof is not None
        ):
            message = "Python terminal scalar or current Attempt mismatch"
            raise ValueError(message)
        if type(terminal) is PythonPublicationResult:
            if (
                inputs.result_marker is None
                or terminal.mutation_marker_reference != inputs.result_marker[1]
            ):
                message = "Python Result lacks its exact marker predecessor"
                raise ValueError(message)
            marker = inputs.result_marker[0]
            audit_python_publication_result(terminal, marker)
            predecessor = DirectPredecessor("publication-result", terminal_ref)
            disposition = (
                "published"
                if terminal.result == "published"
                else "publication-failed"
            )
            possibly_mutated = (
                terminal.result != "published"
                and terminal.mutation_classification
                in {"mutated", "possibly-mutated"}
            )
        elif type(terminal) is PythonMutationMarker:
            if inputs.result_marker is not None:
                message = (
                    "Marker-only terminal cannot have a Result predecessor"
                )
                raise ValueError(message)
            marker = terminal
            predecessor = DirectPredecessor("mutation-marker", terminal_ref)
            disposition, possibly_mutated = "unknown", True
        else:
            message = (
                "Foreign terminal variant cannot finalize a Python Attempt"
            )
            raise ValueError(message)
        if marker.attempt != attempt or inputs.authorization != (
            marker.authorization,
            marker.authorization_reference,
        ):
            message = "Python marker has no exact current Authorization"
            raise ValueError(message)
    else:
        if reference is not None or inputs.result_marker is not None:
            message = "Python terminal reference has no admitted record"
            raise ValueError(message)
        if inputs.exact_proof is not None:
            proof, proof_ref = inputs.exact_proof
            if (
                inputs.publication != (proof.snapshot, proof.snapshot_reference)
                or inputs.bundle is not None
                or inputs.authorization is not None
                or publisher_conclusion != "skipped"
            ):
                message = "Python exact proof has foreign authority or lineage"
                raise ValueError(message)
            predecessor = DirectPredecessor(
                "exact-satisfied-finalization-proof", proof_ref
            )
            disposition = "exact-satisfied"
        elif (
            inputs.publication is not None
            and not inputs.publication[0].action_required
        ):
            if publisher_conclusion != "skipped":
                message = "Python zero-action Snapshot scheduled a publisher"
                raise ValueError(message)
            disposition = "unknown"
        elif publication_step_outcome in {"success", "failure", "cancelled"}:
            disposition, possibly_mutated = "unknown", True
    return AttemptOutcome(
        attempt,
        disposition,
        possibly_mutated,
        predecessor,
        "finalize-attempt",
        snapshot.model.context.control,
        attempt.workflow_run_id,
    )
