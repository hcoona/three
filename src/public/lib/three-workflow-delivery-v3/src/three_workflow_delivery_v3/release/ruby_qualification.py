"""Ruby Release qualification with independent current-Attempt lineage."""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from subprocess import TimeoutExpired
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3._ruby_native import (
    RUBY_RELEASE_UNIT,
    ruby_object,
    ruby_text,
)
from three_workflow_delivery_v3.adapters.ruby import qualify_ruby_consumer
from three_workflow_delivery_v3.adapters.rubygems import RubyRegistry
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.records.artifacts import (
    artifact_reference_from_document,
)
from three_workflow_delivery_v3.records.release import (
    BuddyExecutionIdentity,
    OfficialExecutionIdentity,
    OfficialProductIdentity,
    ReleaseAttemptIdentity,
    ReleaseIntent,
)
from three_workflow_delivery_v3.records.release_transport import _release_intent
from three_workflow_delivery_v3.records.ruby import (
    RubyArtifact,
    ruby_artifact_from_document,
    validate_ruby_artifact,
    validate_ruby_quality_detail,
)
from three_workflow_delivery_v3.release.ruby_governance import (
    RUBY_WORKFLOW,
    RubyGovernance,
)
from three_workflow_delivery_v3.repository.ruby_model import (
    RUBY_QUALITY,
    RubyRepositoryModelSnapshot,
    ruby_repository_model_from_document,
)

if TYPE_CHECKING:
    from collections.abc import Callable

    from three_workflow_delivery_v3.adapters.ruby import RubyDistribution
    from three_workflow_delivery_v3.records.artifacts import ArtifactReference


@dataclass(frozen=True, slots=True)
class RubyQualificationSnapshot:
    """One admitted Live request and its original gem obligations."""

    intent: ReleaseIntent
    model: RubyRepositoryModelSnapshot
    model_reference: ArtifactReference
    governance: RubyGovernance

    def __post_init__(self) -> None:
        """Close protected-main control, destination and version facts."""
        context = self.model.context
        intent = self.intent
        if (
            intent.repository != "hcoona/three"
            or intent.workflow_path != RUBY_WORKFLOW
            or intent.actor != "hcoona"
            or intent.selected_ref != "refs/heads/main"
            or intent.release_unit != RUBY_RELEASE_UNIT
            or intent.mode != "live"
            or intent.purpose != "live-release"
            or intent.channel != self.governance.registry.channel
            or context.purpose != "live-release"
            or context.request_id != intent.request_id
            or context.workflow_run_id != intent.workflow_run_id
            or context.run_attempt is not None
            or context.target != intent.target
            or context.control != f"workflow-delivery-v3:{intent.target}"
            or self.model_reference.payload_digest != self.model.snapshot_digest
        ):
            message = (
                "Ruby Qualification requires current protected Live bindings"
            )
            raise ValueError(message)
        if (
            parse_canonical_json(self.model.provider.nbgv.raw_bytes)[
                "PublicRelease"
            ]
            is not True
        ):
            message = "Ruby Live requires native public-main NBGV facts"
            raise ValueError(message)
        self.governance.require_live(self.governance.observed_at)

    @property
    def attempt(self) -> ReleaseAttemptIdentity:
        """Use existing channel execution and unique run identity."""
        execution = (
            BuddyExecutionIdentity(
                "buddy", RUBY_RELEASE_UNIT, self.intent.target
            )
            if self.intent.channel == "buddy"
            else OfficialExecutionIdentity(
                OfficialProductIdentity(
                    "official",
                    RUBY_RELEASE_UNIT,
                    ruby_text(
                        parse_canonical_json(
                            self.model.provider.nbgv.raw_bytes
                        )["SemVer2"]
                    ),
                ),
                self.intent.target,
            )
        )
        return ReleaseAttemptIdentity(execution, self.intent.workflow_run_id)

    def to_document(self) -> dict[str, JsonValue]:
        """Retain Release authority and frozen required quality."""
        return {
            "schema": "workflow-delivery/v3/ruby-qualification-snapshot",
            "intent": self.intent.to_document(),
            "attempt": self.attempt.to_document(),
            "model": self.model.to_document(),
            "model-reference": self.model_reference.to_document(),
            "governance": self.governance.document,
            "governance-source-commit": self.governance.source_commit,
            "governance-observed-at": self.governance.observed_at.isoformat(),
            "obligations": cast("list[JsonValue]", list(RUBY_QUALITY)),
            "producer": "plan-ruby-release",
        }

    @property
    def snapshot_digest(self) -> str:
        """Return the immutable first-snapshot identity."""
        return canonical_sha256(self.to_document())


@dataclass(frozen=True, slots=True)
class RubyQualificationEvidence:
    """One Release-only obligation result over its original transport."""

    snapshot_digest: str
    definition: str
    artifact: RubyArtifact
    result: str
    detail: bytes

    def __post_init__(self) -> None:
        """Reject unknown obligations, outcomes and noncanonical evidence."""
        if self.definition not in RUBY_QUALITY or self.result not in {
            "passed",
            "failed",
        }:
            message = "invalid Ruby Release Evidence obligation or result"
            raise ValueError(message)
        validate_ruby_quality_detail(
            self.artifact, self.definition, self.result, self.detail
        )

    def to_document(self) -> dict[str, JsonValue]:
        """Keep Release Evidence in its own namespace; CI cannot substitute."""
        return {
            "schema": "workflow-delivery/v3/ruby-qualification-evidence",
            "snapshot-digest": self.snapshot_digest,
            "definition": self.definition,
            "artifact": self.artifact.to_document(),
            "result": self.result,
            "detail": parse_canonical_json(self.detail),
            "producer": "qualify-ruby-release",
        }

    @property
    def evidence_digest(self) -> str:
        """Return the exact canonical Evidence identity."""
        return canonical_sha256(self.to_document())


def qualify_ruby_release(
    snapshot: RubyQualificationSnapshot,
    artifact: RubyArtifact,
    payload: bytes,
    *,
    consumer: Callable[
        [RubyDistribution], dict[str, JsonValue]
    ] = qualify_ruby_consumer,
) -> tuple[RubyQualificationEvidence, ...]:
    """Inspect the original gem and run its separate clean native consumer."""
    validate_ruby_artifact(
        artifact,
        witness=snapshot.model.build_request().witness,
        context=snapshot.model.context,
    )
    inspected = artifact.inspect(payload)
    evidence = [
        RubyQualificationEvidence(
            snapshot.snapshot_digest,
            RUBY_QUALITY[0],
            artifact,
            "passed",
            canonicalize({"digest": inspected.digest}),
        )
    ]
    try:
        detail: dict[str, JsonValue] = consumer(inspected)
        validate_ruby_quality_detail(
            artifact, RUBY_QUALITY[1], "passed", canonicalize(detail)
        )
        outcome = "passed"
    except (
        OSError,
        TypeError,
        ValueError,
        RuntimeError,
        TimeoutExpired,
    ) as error:
        detail = {"error-kind": type(error).__name__}
        outcome = "failed"
    evidence.append(
        RubyQualificationEvidence(
            snapshot.snapshot_digest,
            RUBY_QUALITY[1],
            artifact,
            outcome,
            canonicalize(detail),
        )
    )
    return tuple(evidence)


@dataclass(frozen=True, slots=True)
class RubyQualificationDecision:
    """Close the first Snapshot without reading or mutating a destination."""

    snapshot: RubyQualificationSnapshot
    evidence: tuple[RubyQualificationEvidence, ...]

    def __post_init__(self) -> None:
        """Reject duplicate, cross-run or conflicting artifacts."""
        seen: set[str] = set()
        artifact_ids: set[str] = set()
        for item in self.evidence:
            if (
                type(item) is not RubyQualificationEvidence
                or item.snapshot_digest != self.snapshot.snapshot_digest
                or item.definition in seen
            ):
                message = "Ruby Release Evidence differs from current Snapshot"
                raise ValueError(message)
            seen.add(item.definition)
            validate_ruby_artifact(
                item.artifact,
                witness=self.snapshot.model.build_request().witness,
                context=self.snapshot.model.context,
            )
            artifact_ids.add(item.artifact.artifact_digest)
        if len(artifact_ids) > 1:
            message = "Ruby Release obligations refer to different builds"
            raise ValueError(message)

    @property
    def result(self) -> str:
        """Fail or remain incomplete unless both obligations passed."""
        if any(e.result == "failed" for e in self.evidence):
            return "failed"
        return (
            "passed"
            if {e.definition for e in self.evidence} == set(RUBY_QUALITY)
            else "incomplete"
        )

    @property
    def artifact(self) -> RubyArtifact:
        """Expose the qualified gem only after complete successful evidence."""
        if self.result != "passed":
            message = "Ruby publication requires successful Qualification"
            raise ValueError(message)
        return self.evidence[0].artifact

    def to_document(self) -> dict[str, JsonValue]:
        """Serialize only the current Snapshot's qualification conclusion."""
        return {
            "schema": "workflow-delivery/v3/ruby-qualification-decision",
            "snapshot-digest": self.snapshot.snapshot_digest,
            "attempt": self.snapshot.attempt.to_document(),
            "result": self.result,
            "evidence-digests": cast(
                "list[JsonValue]", [e.evidence_digest for e in self.evidence]
            ),
            "producer": "finalize-ruby-qualification",
        }

    @property
    def decision_digest(self) -> str:
        """Return the current qualification decision identity."""
        return canonical_sha256(self.to_document())


def _normalized(value: JsonValue, expected: dict[str, JsonValue]) -> None:
    if canonicalize(value) != canonicalize(expected):
        message = "Ruby serialized domain record differs from current inputs"
        raise ValueError(message)


def ruby_qualification_snapshot_from_document(
    value: JsonValue,
) -> RubyQualificationSnapshot:
    """Reject foreign or altered purpose, Governance and model contracts."""
    doc = ruby_object(
        value,
        {
            "schema",
            "intent",
            "attempt",
            "model",
            "model-reference",
            "governance",
            "governance-source-commit",
            "governance-observed-at",
            "obligations",
            "producer",
        },
    )
    intent = _release_intent(doc["intent"])
    registry = RubyRegistry(
        "github-packages" if intent.channel == "buddy" else "rubygems"
    )
    snapshot = RubyQualificationSnapshot(
        intent,
        ruby_repository_model_from_document(doc["model"]),
        artifact_reference_from_document(doc["model-reference"]),
        RubyGovernance(
            registry,
            canonicalize(doc["governance"]),
            ruby_text(doc["governance-source-commit"]),
            datetime.fromisoformat(ruby_text(doc["governance-observed-at"])),
        ),
    )
    _normalized(doc, snapshot.to_document())
    return snapshot


def ruby_qualification_evidence_from_document(
    value: JsonValue,
) -> RubyQualificationEvidence:
    """Read one Release evidence record, never a CI coercion."""
    doc = ruby_object(
        value,
        {
            "schema",
            "snapshot-digest",
            "definition",
            "artifact",
            "result",
            "detail",
            "producer",
        },
    )
    result = RubyQualificationEvidence(
        ruby_text(doc["snapshot-digest"]),
        ruby_text(doc["definition"]),
        ruby_artifact_from_document(doc["artifact"]),
        ruby_text(doc["result"]),
        canonicalize(doc["detail"]),
    )
    _normalized(doc, result.to_document())
    return result


def admit_ruby_qualification_decision(
    value: JsonValue,
    snapshot: RubyQualificationSnapshot,
    evidence: tuple[RubyQualificationEvidence, ...],
) -> RubyQualificationDecision:
    """Replay the deterministic Decision against its explicit predecessors."""
    result = RubyQualificationDecision(snapshot, evidence)
    _normalized(value, result.to_document())
    return result
