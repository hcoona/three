"""One qualified Ruby gem and immutable current-run publication authority."""

from __future__ import annotations

from dataclasses import dataclass
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3._ruby_native import ruby_digest, ruby_object
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.records.artifacts import (
    ArtifactReference,
    _digest,
)
from three_workflow_delivery_v3.release.ruby_governance import (
    RUBY_PUBLISHER,
    RubyGovernance,
)

if TYPE_CHECKING:
    from datetime import datetime

    from three_workflow_delivery_v3.adapters.ruby_registry import (
        RubyRegistryObservation,
    )
    from three_workflow_delivery_v3.adapters.rubygems import RubyRegistry
    from three_workflow_delivery_v3.records.release import (
        ReleaseAttemptIdentity,
    )
    from three_workflow_delivery_v3.release.ruby_qualification import (
        RubyQualificationDecision,
    )


def _instant(value: datetime) -> str:
    if value.tzinfo is None:
        message = "Ruby publication requires timezone-aware time"
        raise ValueError(message)
    return value.isoformat().replace("+00:00", "Z")


def _reference(
    reference: ArtifactReference, document: dict[str, JsonValue]
) -> None:
    if type(
        reference
    ) is not ArtifactReference or reference.payload_digest != canonical_sha256(
        document
    ):
        message = "Ruby immutable reference differs from its payload"
        raise ValueError(message)


def exact_ruby_observation(
    native: RubyRegistryObservation, decision: RubyQualificationDecision
) -> bool:
    """Require original bytes, native identity and the qualified witness."""
    artifact = decision.artifact
    distribution = native.distribution
    return (
        native.registry == decision.snapshot.governance.registry
        and native.version == artifact.witness.nbgv.native_version
        and native.classification == "exact"
        and native.failure_kind is None
        and distribution is not None
        and distribution.filename == artifact.filename
        and distribution.digest == artifact.reference.payload_digest
        and len(distribution.content) == artifact.byte_size
        and distribution.witness == artifact.witness
    )


@dataclass(frozen=True, slots=True)
class RubyRemoteObservation:
    """Fresh destination state bound to this Attempt's qualification."""

    decision: RubyQualificationDecision
    decision_reference: ArtifactReference
    native: RubyRegistryObservation
    observed_at: datetime

    def __post_init__(self) -> None:
        """Reject foreign destinations, versions and unqualified artifacts."""
        _reference(self.decision_reference, self.decision.to_document())
        artifact = self.decision.artifact
        self.decision.snapshot.governance.require_live(self.observed_at)
        if (
            self.native.registry != self.decision.snapshot.governance.registry
            or self.native.version != artifact.witness.nbgv.native_version
        ):
            message = "Ruby observation destination or version mismatch"
            raise ValueError(message)
        _instant(self.observed_at)

    @property
    def classification(self) -> str:
        """Admit only missing state or a verified original."""
        if (
            self.native.classification == "missing"
            and self.native.distribution is None
            and self.native.failure_kind is None
        ):
            return "missing"
        if exact_ruby_observation(self.native, self.decision):
            return "exact-satisfied"
        return "blocked"

    def to_document(self) -> dict[str, JsonValue]:
        """Retain original noncredential responses under current lineage."""
        return {
            "schema": "workflow-delivery/v3/ruby-remote-state-observation",
            "attempt": self.decision.snapshot.attempt.to_document(),
            "decision-reference": self.decision_reference.to_document(),
            "native": self.native.to_document(),
            "observed-at": _instant(self.observed_at),
            "classification": self.classification,
            "producer": "observe-ruby",
        }


@dataclass(frozen=True, slots=True)
class RubyPublicationSnapshot:
    """One original-gem action or zero actions; no partial-set semantics."""

    observation: RubyRemoteObservation
    observation_reference: ArtifactReference

    def __post_init__(self) -> None:
        """Block conflicting and unknown native state before approval."""
        _reference(self.observation_reference, self.observation.to_document())
        if self.observation.classification not in {
            "missing",
            "exact-satisfied",
        }:
            message = "Ruby conflicting or unknown state blocks publication"
            raise ValueError(message)

    @property
    def action_required(self) -> bool:
        """An exact original schedules neither approval nor a credential job."""
        return self.observation.classification == "missing"

    @property
    def attempt(self) -> ReleaseAttemptIdentity:
        """Return the existing V3 execution/run identity."""
        return self.observation.decision.snapshot.attempt

    @property
    def registry(self) -> RubyRegistry:
        """Select the independently admitted destination profile."""
        return self.observation.decision.snapshot.governance.registry

    def to_document(self) -> dict[str, JsonValue]:
        """Seal one binary POST and its exact artifact identity."""
        artifact = self.observation.decision.artifact
        action: JsonValue = None
        if self.action_required:
            action = {
                "kind": "ruby-gem",
                "profile-digest": self.registry.profile_digest,
                "mutable-resource": self.registry.origin
                + "/gems/"
                + artifact.filename,
                "filename": artifact.filename,
                "artifact-reference": artifact.reference.to_document(),
            }
        return {
            "schema": "workflow-delivery/v3/ruby-publication-snapshot",
            "attempt": self.attempt.to_document(),
            "observation-reference": self.observation_reference.to_document(),
            "action": action,
            "producer": "prepare-ruby-publication",
        }


def render_ruby_approval_summary(snapshot: RubyPublicationSnapshot) -> bytes:
    """Give the reviewer the complete singular effect and its failure bounds."""
    artifact = snapshot.observation.decision.artifact
    lines = [
        "Ruby original gem approval",
        f"Target: {snapshot.attempt.execution.target}",
        f"Run: {snapshot.attempt.workflow_run_id}",
        f"Version: {artifact.witness.nbgv.native_version}",
        f"Destination: {snapshot.registry.origin}",
        f"Profile: {snapshot.registry.profile_digest}",
        f"Upload: {artifact.filename} {artifact.reference.payload_digest}",
        (
            "Upload the original gem once, then verify exact bytes and "
            "witness. Only HTTP 200 admits bounded visibility reads. "
            "Rejection or ambiguity remains failure. No retry, rollback, "
            "deletion or credential fallback."
        ),
    ]
    return ("\n".join(lines) + "\n").encode()


@dataclass(frozen=True, slots=True)
class RubyApprovalBundle:
    """Immutable complete effect summary before Environment approval."""

    snapshot: RubyPublicationSnapshot
    snapshot_reference: ArtifactReference
    summary_reference: ArtifactReference

    def __post_init__(self) -> None:
        """Reject summary drift and approval for zero actions."""
        _reference(self.snapshot_reference, self.snapshot.to_document())
        if (
            not self.snapshot.action_required
            or self.summary_reference.payload_digest
            != ruby_digest(self.summary)
        ):
            message = "Ruby Approval Bundle requires the exact action summary"
            raise ValueError(message)

    @property
    def summary(self) -> bytes:
        """Render the same content bound by the summary artifact."""
        return render_ruby_approval_summary(self.snapshot)

    def to_document(self) -> dict[str, JsonValue]:
        """Bind the selected Environment to immutable current-run inputs."""
        return {
            "schema": "workflow-delivery/v3/ruby-approval-bundle",
            "attempt": self.snapshot.attempt.to_document(),
            "snapshot-reference": self.snapshot_reference.to_document(),
            "summary-reference": self.summary_reference.to_document(),
            "environment": self.snapshot.registry.environment,
            "producer": "prepare-ruby-publication",
        }


@dataclass(frozen=True, slots=True)
class RubyPublicationAuthorization:
    """Current Environment approval, persisted before OIDC acquisition."""

    bundle: RubyApprovalBundle
    bundle_reference: ArtifactReference
    approval_evidence: bytes
    completed_at: datetime

    def __post_init__(self) -> None:
        """Close native deployment, reviewer and Environment evidence."""
        _reference(self.bundle_reference, self.bundle.to_document())
        snapshot = self.bundle.snapshot
        proof = ruby_object(
            parse_canonical_json(self.approval_evidence),
            {
                "repository",
                "run-id",
                "run-attempt",
                "target",
                "environment",
                "environment-id",
                "deployment-id",
                "reviewer-id",
                "reviewer",
                "state",
                "native-response-digest",
                "sentinel",
            },
        )
        configuration = cast(
            "dict[str, JsonValue]",
            snapshot.observation.decision.snapshot.governance.document[
                "configuration"
            ],
        )
        expected: dict[str, JsonValue] = {
            "repository": "hcoona/three",
            "run-id": snapshot.attempt.workflow_run_id,
            "run-attempt": 1,
            "target": snapshot.attempt.execution.target,
            "environment": snapshot.registry.environment,
            "environment-id": configuration["environment-id"],
            "reviewer-id": 712433,
            "reviewer": "hcoona",
            "state": "approved",
            "sentinel": snapshot.registry.environment + "/v1",
        }
        if (
            any(
                canonicalize(proof[k]) != canonicalize(v)
                for k, v in expected.items()
            )
            or type(proof["deployment-id"]) is not int
            or cast("int", proof["deployment-id"]) <= 0
        ):
            message = "Ruby publisher native current-run approval mismatch"
            raise ValueError(message)

        _digest(
            proof["native-response-digest"], field="approval response digest"
        )
        if self.completed_at < snapshot.observation.observed_at:
            message = "Ruby Authorization predates its Snapshot"
            raise ValueError(message)
        snapshot.observation.decision.snapshot.governance.require_live(
            self.completed_at
        )

    def to_document(self) -> dict[str, JsonValue]:
        """Persist authority before obtaining assertion or registry token."""
        return {
            "schema": "workflow-delivery/v3/ruby-publication-authorization",
            "attempt": self.bundle.snapshot.attempt.to_document(),
            "bundle-reference": self.bundle_reference.to_document(),
            "approval-evidence": parse_canonical_json(self.approval_evidence),
            "completed-at": _instant(self.completed_at),
            "producer": RUBY_PUBLISHER,
        }


@dataclass(frozen=True, slots=True)
class RubyMutationMarker:
    """Durable authority after a fresh missing-state and Governance check."""

    authorization: RubyPublicationAuthorization
    authorization_reference: ArtifactReference
    fresh_governance: RubyGovernance
    pre_state: RubyRegistryObservation
    observed_at: datetime

    def __post_init__(self) -> None:
        """Even a newly exact gem blocks this already approved action."""
        _reference(
            self.authorization_reference, self.authorization.to_document()
        )
        snapshot = self.authorization.bundle.snapshot
        snapshot.observation.decision.snapshot.governance.require_same_live(
            self.fresh_governance, self.observed_at
        )
        if (
            self.pre_state.registry != snapshot.registry
            or self.pre_state.version != snapshot.observation.native.version
            or self.pre_state.classification != "missing"
            or self.pre_state.distribution is not None
            or self.pre_state.failure_kind is not None
            or self.observed_at < self.authorization.completed_at
            or self.fresh_governance.observed_at
            < self.authorization.completed_at
        ):
            message = "Ruby pre-marker state or authority drifted"
            raise ValueError(message)

    @property
    def attempt(self) -> ReleaseAttemptIdentity:
        """Retain shared Attempt identity through the mutation boundary."""
        return self.authorization.bundle.snapshot.attempt

    def to_document(self) -> dict[str, JsonValue]:
        """Persist one effect boundary; absence of Result remains unknown."""
        return {
            "schema": "workflow-delivery/v3/ruby-mutation-may-have-started",
            "attempt": self.attempt.to_document(),
            "authorization-reference": (
                self.authorization_reference.to_document()
            ),
            "governance-digest": self.fresh_governance.digest,
            "governance-source-commit": self.fresh_governance.source_commit,
            "governance-observed-at": _instant(
                self.fresh_governance.observed_at
            ),
            "profile-digest": self.pre_state.registry.profile_digest,
            "pre-state": self.pre_state.to_document(),
            "observed-at": _instant(self.observed_at),
            "producer": RUBY_PUBLISHER,
        }
