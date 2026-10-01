"""Ruby variant admission into the shared scalar Attempt Outcome contract."""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from typing import TYPE_CHECKING

from three_workflow_delivery_v3._ruby_native import ruby_object, ruby_text
from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.records.artifacts import (
    artifact_reference_from_document,
)
from three_workflow_delivery_v3.records.release import (
    AttemptOutcome,
    DirectPredecessor,
)
from three_workflow_delivery_v3.release.ruby_audit import (
    audit_ruby_publication_result,
)
from three_workflow_delivery_v3.release.ruby_execution import (
    RubyPublicationResult,
)
from three_workflow_delivery_v3.release.ruby_publication import (
    RubyApprovalBundle,
    RubyMutationMarker,
    RubyPublicationAuthorization,
    RubyPublicationSnapshot,
    RubyRemoteObservation,
    _instant,
    _reference,
    exact_ruby_observation,
)
from three_workflow_delivery_v3.release.ruby_readback import (
    replay_ruby_observation,
)

if TYPE_CHECKING:
    from three_workflow_delivery_v3.adapters.ruby_registry import (
        RubyRegistryObservation,
    )
    from three_workflow_delivery_v3.canonical import JsonValue
    from three_workflow_delivery_v3.records.artifacts import ArtifactReference
    from three_workflow_delivery_v3.records.release_transport import (
        ReleaseAdmissionBindings,
    )
    from three_workflow_delivery_v3.release.ruby_governance import (
        RubyGovernance,
    )
    from three_workflow_delivery_v3.release.ruby_qualification import (
        RubyQualificationDecision,
    )


@dataclass(frozen=True, slots=True)
class RubyExactSatisfiedProof:
    """Fresh zero-action original proof without approval or credentials."""

    snapshot: RubyPublicationSnapshot
    snapshot_reference: ArtifactReference
    fresh_governance: RubyGovernance
    native: RubyRegistryObservation
    observed_at: datetime

    def __post_init__(self) -> None:
        """Reject reused observation, changed Governance and inexact state."""
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
            or not exact_ruby_observation(self.native, decision)
        ):
            message = "Ruby zero-action proof is not fresh exact state"
            raise ValueError(message)

    def to_document(self) -> dict[str, JsonValue]:
        """Retain the current Snapshot predecessor and fresh exact readback."""
        return {
            "schema": "workflow-delivery/v3/ruby-exact-satisfied-proof",
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
class RubyFinalizationInputs:
    """Transient admitted records, preserving all direct predecessor links."""

    decision: RubyQualificationDecision
    decision_reference: ArtifactReference
    original_payload: bytes
    observation: tuple[RubyRemoteObservation, ArtifactReference] | None = None
    publication: tuple[RubyPublicationSnapshot, ArtifactReference] | None = None
    bundle: tuple[RubyApprovalBundle, ArtifactReference] | None = None
    authorization: (
        tuple[RubyPublicationAuthorization, ArtifactReference] | None
    ) = None
    exact_proof: tuple[RubyExactSatisfiedProof, ArtifactReference] | None = None
    terminal: (
        tuple[RubyPublicationResult | RubyMutationMarker, ArtifactReference]
        | None
    ) = None
    result_marker: tuple[RubyMutationMarker, ArtifactReference] | None = None


def finalize_ruby_attempt_outcome(  # noqa: C901, PLR0912, PLR0913, PLR0915
    inputs: RubyFinalizationInputs,
    *,
    current: ReleaseAdmissionBindings,
    run_attempt: int,
    publisher_conclusion: str,
    publication_step_outcome: str | None,
    publication_terminal_reference: str | None,
    observation_conclusion: str | None = None,
) -> AttemptOutcome | None:
    """Admit Ruby lineage and reuse the existing scalar/Outcome semantics."""
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
        message = "Ruby Finalizer current Attempt or control mismatch"
        raise ValueError(message)
    _reference(inputs.decision_reference, inputs.decision.to_document())
    allowed = {"success", "failure", "cancelled", "skipped", None, ""}
    if (
        publication_step_outcome not in allowed
        or observation_conclusion not in allowed
    ):
        message = "Ruby Finalizer requires direct platform conclusions"
        raise ValueError(message)
    if publisher_conclusion == "skipped" and publication_step_outcome not in {
        None,
        "",
        "skipped",
    }:
        message = "Skipped Ruby publisher has an executed publication step"
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
                "Failed Ruby Qualification cannot have downstream authority"
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
            message = "Ruby Finalizer observation lineage mismatch"
            raise ValueError(message)
        predecessor = DirectPredecessor("blocking-observation", observation_ref)
    if inputs.publication is not None:
        publication, publication_ref = inputs.publication
        if inputs.observation != (
            publication.observation,
            publication.observation_reference,
        ):
            message = "Ruby Finalizer Snapshot lineage mismatch"
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
            message = "Ruby Finalizer Bundle lineage mismatch"
            raise ValueError(message)
        predecessor = DirectPredecessor("approval-bundle", bundle_ref)
    if inputs.authorization is not None:
        authorization, auth_ref = inputs.authorization
        if inputs.bundle != (
            authorization.bundle,
            authorization.bundle_reference,
        ):
            message = "Ruby Finalizer Authorization lineage mismatch"
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
            message = "Ruby terminal scalar or current Attempt mismatch"
            raise ValueError(message)
        if type(terminal) is RubyPublicationResult:
            if (
                inputs.result_marker is None
                or terminal.mutation_marker_reference != inputs.result_marker[1]
            ):
                message = "Ruby Result lacks its exact marker predecessor"
                raise ValueError(message)
            marker = inputs.result_marker[0]
            if terminal.marker != marker:
                message = "Ruby Result embeds a foreign marker"
                raise ValueError(message)
            audit_ruby_publication_result(terminal, inputs.original_payload)
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
        elif type(terminal) is RubyMutationMarker:
            if inputs.result_marker is not None:
                message = (
                    "Marker-only terminal cannot have a Result predecessor"
                )
                raise ValueError(message)
            marker = terminal
            predecessor = DirectPredecessor("mutation-marker", terminal_ref)
            disposition, possibly_mutated = "unknown", True
        else:
            message = "Foreign terminal variant cannot finalize a Ruby Attempt"
            raise ValueError(message)
        if marker.attempt != attempt or inputs.authorization != (
            marker.authorization,
            marker.authorization_reference,
        ):
            message = "Ruby marker has no exact current Authorization"
            raise ValueError(message)
    else:
        if reference is not None or inputs.result_marker is not None:
            message = "Ruby terminal reference has no admitted record"
            raise ValueError(message)
        if inputs.exact_proof is not None:
            proof, proof_ref = inputs.exact_proof
            if (
                inputs.publication != (proof.snapshot, proof.snapshot_reference)
                or inputs.bundle is not None
                or inputs.authorization is not None
                or publisher_conclusion != "skipped"
            ):
                message = "Ruby exact proof has foreign authority or lineage"
                raise ValueError(message)
            replay_ruby_observation(
                proof.native.to_document(),
                inputs.decision.artifact.inspect(inputs.original_payload),
                proof.snapshot.registry,
            )
            predecessor = DirectPredecessor(
                "exact-satisfied-finalization-proof", proof_ref
            )
            disposition = "exact-satisfied"
        elif (
            inputs.publication is not None
            and not inputs.publication[0].action_required
        ):
            if publisher_conclusion != "skipped":
                message = "Ruby zero-action Snapshot scheduled a publisher"
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


def ruby_exact_proof_from_document(
    value: JsonValue,
    snapshot: RubyPublicationSnapshot,
    fresh_governance: RubyGovernance,
    payload: bytes,
) -> RubyExactSatisfiedProof:
    """Replay a fresh zero-action proof against the qualified original."""
    doc = ruby_object(value)
    proof = RubyExactSatisfiedProof(
        snapshot,
        artifact_reference_from_document(doc.get("snapshot-reference")),
        fresh_governance,
        replay_ruby_observation(
            doc.get("native"),
            snapshot.observation.decision.artifact.inspect(payload),
            snapshot.registry,
        ),
        datetime.fromisoformat(ruby_text(doc.get("observed-at"))),
    )
    if canonicalize(proof.to_document()) != canonicalize(value):
        message = "Ruby exact proof differs from current native evidence"
        raise ValueError(message)
    return proof
