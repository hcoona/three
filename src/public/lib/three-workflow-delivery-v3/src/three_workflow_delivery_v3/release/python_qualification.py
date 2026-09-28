"""Python Release qualification with independent current-Attempt lineage."""

from __future__ import annotations

from dataclasses import dataclass
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3.adapters.python import qualify_python_consumer
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.records.python import (
    PythonArtifact,
    validate_python_artifact_pair,
)
from three_workflow_delivery_v3.records.release import (
    BuddyExecutionIdentity,
    OfficialExecutionIdentity,
    OfficialProductIdentity,
    ReleaseAttemptIdentity,
    ReleaseIntent,
)
from three_workflow_delivery_v3.release.python_governance import (
    PYTHON_WORKFLOW,
    PythonGovernance,
)
from three_workflow_delivery_v3.repository.python_model import (
    PYTHON_QUALITY,
    PythonRepositoryModelSnapshot,
)
from three_workflow_delivery_v3.repository.python_provider import (
    PYTHON_RELEASE_UNIT,
    python_digest,
    python_text,
    require_public_python_version,
)

if TYPE_CHECKING:
    from collections.abc import Callable

    from three_workflow_delivery_v3.adapters.python import (
        PythonConsumerResult,
        PythonDistribution,
    )
    from three_workflow_delivery_v3.records.artifacts import ArtifactReference


@dataclass(frozen=True, slots=True)
class PythonQualificationSnapshot:
    """One admitted Live request and its original two-format obligations."""

    intent: ReleaseIntent
    model: PythonRepositoryModelSnapshot
    model_reference: ArtifactReference
    governance: PythonGovernance

    def __post_init__(self) -> None:
        """Close protected-main control, destination and version facts."""
        context = self.model.context
        intent = self.intent
        if (
            intent.repository != "hcoona/three"
            or intent.workflow_path != PYTHON_WORKFLOW
            or intent.actor != "hcoona"
            or intent.selected_ref != "refs/heads/main"
            or intent.release_unit != PYTHON_RELEASE_UNIT
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
                "Python Qualification requires current protected Live bindings"
            )
            raise ValueError(message)
        require_public_python_version(self.model.provider.nbgv)
        if (
            parse_canonical_json(self.model.provider.nbgv.raw_bytes)[
                "PublicRelease"
            ]
            is not True
        ):
            message = "Python Live requires native public-main NBGV facts"
            raise ValueError(message)
        self.governance.require_live(self.governance.observed_at)

    @property
    def attempt(self) -> ReleaseAttemptIdentity:
        """Use existing channel execution and unique run identity."""
        execution = (
            BuddyExecutionIdentity(
                "buddy", PYTHON_RELEASE_UNIT, self.intent.target
            )
            if self.intent.channel == "buddy"
            else OfficialExecutionIdentity(
                OfficialProductIdentity(
                    "official",
                    PYTHON_RELEASE_UNIT,
                    python_text(
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
            "schema": "workflow-delivery/v3/python-qualification-snapshot",
            "intent": self.intent.to_document(),
            "attempt": self.attempt.to_document(),
            "model": self.model.to_document(),
            "model-reference": self.model_reference.to_document(),
            "governance": self.governance.document,
            "governance-source-commit": self.governance.source_commit,
            "governance-observed-at": self.governance.observed_at.isoformat(),
            "obligations": cast("list[JsonValue]", list(PYTHON_QUALITY)),
            "producer": "plan-python-release",
        }

    @property
    def snapshot_digest(self) -> str:
        """Return the immutable first-snapshot identity."""
        return canonical_sha256(self.to_document())


@dataclass(frozen=True, slots=True)
class PythonQualificationEvidence:
    """One Release-only obligation result over both original transports."""

    snapshot_digest: str
    definition: str
    artifacts: tuple[PythonArtifact, ...]
    result: str
    detail: bytes

    def __post_init__(self) -> None:
        """Reject unknown obligations, outcomes and noncanonical evidence."""
        if self.definition not in PYTHON_QUALITY or self.result not in {
            "passed",
            "failed",
        }:
            message = "invalid Python Release Evidence obligation or result"
            raise ValueError(message)
        parse_canonical_json(self.detail)

    def to_document(self) -> dict[str, JsonValue]:
        """Keep Release Evidence in its own namespace; CI cannot substitute."""
        return {
            "schema": "workflow-delivery/v3/python-qualification-evidence",
            "snapshot-digest": self.snapshot_digest,
            "definition": self.definition,
            "artifacts": [a.to_document() for a in self.artifacts],
            "result": self.result,
            "detail": parse_canonical_json(self.detail),
            "producer": "qualify-python-release",
        }

    @property
    def evidence_digest(self) -> str:
        """Return the exact canonical Evidence identity."""
        return canonical_sha256(self.to_document())


def qualify_python_release(
    snapshot: PythonQualificationSnapshot,
    artifacts: tuple[PythonArtifact, ...],
    payloads: tuple[bytes, bytes],
    *,
    consumer: Callable[
        [PythonDistribution], PythonConsumerResult
    ] = qualify_python_consumer,
) -> tuple[PythonQualificationEvidence, ...]:
    """Inspect and consume the new Attempt's own original wheel and sdist."""
    validate_python_artifact_pair(
        artifacts,
        witness=snapshot.model.build_request().witness,
        context=snapshot.model.context,
    )
    inspected = tuple(
        a.inspect(p) for a, p in zip(artifacts, payloads, strict=True)
    )
    evidence = [
        PythonQualificationEvidence(
            snapshot.snapshot_digest,
            PYTHON_QUALITY[0],
            artifacts,
            "passed",
            canonicalize({"digests": [d.digest for d in inspected]}),
        )
    ]
    for definition, distribution in zip(
        PYTHON_QUALITY[1:], inspected, strict=True
    ):
        try:
            result = consumer(distribution)
            if (
                result.variant != distribution.variant
                or result.original_digest != distribution.digest
            ):
                message = "Python consumer returned foreign artifact evidence"
                raise ValueError(message)  # noqa: TRY301 - retain failed evidence
            detail: dict[str, JsonValue] = {
                "installed": parse_canonical_json(result.installed),
                "commands": [
                    parse_canonical_json(c) for c in result.command_evidence
                ],
                "rebuilt-wheel-digest": None
                if result.rebuilt_wheel is None
                else python_digest(result.rebuilt_wheel),
            }
            outcome = "passed"
        except (OSError, ValueError, RuntimeError) as error:
            detail = {"error-kind": type(error).__name__}
            outcome = "failed"
        evidence.append(
            PythonQualificationEvidence(
                snapshot.snapshot_digest,
                definition,
                artifacts,
                outcome,
                canonicalize(detail),
            )
        )
    return tuple(evidence)


@dataclass(frozen=True, slots=True)
class PythonQualificationDecision:
    """Close the first Snapshot without reading or mutating a destination."""

    snapshot: PythonQualificationSnapshot
    evidence: tuple[PythonQualificationEvidence, ...]

    def __post_init__(self) -> None:
        """Reject duplicate, cross-run or conflicting artifacts."""
        seen: set[str] = set()
        artifacts: set[tuple[str, ...]] = set()
        for item in self.evidence:
            if (
                type(item) is not PythonQualificationEvidence
                or item.snapshot_digest != self.snapshot.snapshot_digest
                or item.definition in seen
            ):
                message = (
                    "Python Release Evidence differs from current Snapshot"
                )
                raise ValueError(message)
            seen.add(item.definition)
            validate_python_artifact_pair(
                item.artifacts,
                witness=self.snapshot.model.build_request().witness,
                context=self.snapshot.model.context,
            )
            artifacts.add(tuple(a.artifact_digest for a in item.artifacts))
        if len(artifacts) > 1:
            message = "Python Release obligations refer to different builds"
            raise ValueError(message)

    @property
    def result(self) -> str:
        """Fail or remain incomplete unless all three obligations passed."""
        if any(e.result == "failed" for e in self.evidence):
            return "failed"
        return (
            "passed"
            if {e.definition for e in self.evidence} == set(PYTHON_QUALITY)
            else "incomplete"
        )

    @property
    def artifacts(self) -> tuple[PythonArtifact, ...]:
        """Expose the qualified pair only after complete successful evidence."""
        if self.result != "passed":
            message = "Python publication requires successful Qualification"
            raise ValueError(message)
        return self.evidence[0].artifacts

    def to_document(self) -> dict[str, JsonValue]:
        """Serialize only the current Snapshot's qualification conclusion."""
        return {
            "schema": "workflow-delivery/v3/python-qualification-decision",
            "snapshot-digest": self.snapshot.snapshot_digest,
            "attempt": self.snapshot.attempt.to_document(),
            "result": self.result,
            "evidence-digests": cast(
                "list[JsonValue]", [e.evidence_digest for e in self.evidence]
            ),
            "producer": "finalize-python-qualification",
        }

    @property
    def decision_digest(self) -> str:
        """Return the current qualification decision identity."""
        return canonical_sha256(self.to_document())
