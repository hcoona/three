"""Strict Python domain replay from explicit current-run immutable inputs."""

from __future__ import annotations

from datetime import datetime
from typing import TYPE_CHECKING

from three_workflow_delivery_v3.adapters.pypi import (
    PythonIndexObservation,
    PythonRegistry,
)
from three_workflow_delivery_v3.canonical import JsonValue, canonicalize
from three_workflow_delivery_v3.records.artifacts import (
    artifact_reference_from_document,
)
from three_workflow_delivery_v3.records.python import (
    python_artifact_from_document,
)
from three_workflow_delivery_v3.records.release_transport import _release_intent
from three_workflow_delivery_v3.release.python_governance import (
    PythonGovernance,
)
from three_workflow_delivery_v3.release.python_publication import (
    PythonApprovalBundle,
    PythonMutationMarker,
    PythonPublicationAuthorization,
    PythonPublicationSnapshot,
    PythonRemoteObservation,
)
from three_workflow_delivery_v3.release.python_qualification import (
    PythonQualificationDecision,
    PythonQualificationEvidence,
    PythonQualificationSnapshot,
)
from three_workflow_delivery_v3.repository.python_model import (
    python_repository_model_from_document,
)
from three_workflow_delivery_v3.repository.python_provider import (
    python_object,
    python_text,
)

if TYPE_CHECKING:
    from three_workflow_delivery_v3.adapters.python import PythonDistribution
    from three_workflow_delivery_v3.records.artifacts import ArtifactReference


def _normalized(value: JsonValue, expected: dict[str, JsonValue]) -> None:
    if canonicalize(value) != canonicalize(expected):
        message = "Python serialized domain record differs from current inputs"
        raise ValueError(message)


def python_qualification_snapshot_from_document(
    value: JsonValue,
) -> PythonQualificationSnapshot:
    """Reject foreign or altered purpose, Governance and model contracts."""
    doc = python_object(
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
    registry = PythonRegistry(
        "testpypi" if intent.channel == "buddy" else "pypi"
    )
    snapshot = PythonQualificationSnapshot(
        intent,
        python_repository_model_from_document(doc["model"]),
        artifact_reference_from_document(doc["model-reference"]),
        PythonGovernance(
            registry,
            canonicalize(doc["governance"]),
            python_text(doc["governance-source-commit"]),
            datetime.fromisoformat(python_text(doc["governance-observed-at"])),
        ),
    )
    _normalized(doc, snapshot.to_document())
    return snapshot


def python_qualification_evidence_from_document(
    value: JsonValue,
) -> PythonQualificationEvidence:
    """Read one Release evidence record, never a CI coercion."""
    doc = python_object(
        value,
        {
            "schema",
            "snapshot-digest",
            "definition",
            "artifacts",
            "result",
            "detail",
            "producer",
        },
    )
    if not isinstance(doc["artifacts"], list):
        message = "Python Release artifacts must be a list"
        raise TypeError(message)
    result = PythonQualificationEvidence(
        python_text(doc["snapshot-digest"]),
        python_text(doc["definition"]),
        tuple(python_artifact_from_document(a) for a in doc["artifacts"]),
        python_text(doc["result"]),
        canonicalize(doc["detail"]),
    )
    _normalized(doc, result.to_document())
    return result


def admit_python_qualification_decision(
    value: JsonValue,
    snapshot: PythonQualificationSnapshot,
    evidence: tuple[PythonQualificationEvidence, ...],
) -> PythonQualificationDecision:
    """Replay the deterministic Decision against its explicit predecessors."""
    result = PythonQualificationDecision(snapshot, evidence)
    _normalized(value, result.to_document())
    return result


def python_native_observation_from_document(
    value: JsonValue,
    originals: tuple[PythonDistribution, ...],
) -> PythonIndexObservation:
    """Resolve matching observed file identities to already admitted bytes.

    Changed native bytes cannot enter a publication lineage. Original bytes
    merely reconstruct a prior admitted reader record; this is not a new
    destination observation or a clean destination-consumer proof.
    """
    doc = python_object(
        value,
        {"registry", "version", "index-digest", "classification", "files"},
    )
    if not isinstance(doc["files"], list):
        message = "Python observed files must be a list"
        raise TypeError(message)
    files = []
    for entry in doc["files"]:
        item = python_object(entry, {"variant", "filename", "digest"})
        matches = [
            o
            for o in originals
            if o.variant == item["variant"]
            and o.filename == item["filename"]
            and o.digest == item["digest"]
        ]
        if len(matches) != 1:
            message = "Python observed file is not the approved original"
            raise ValueError(message)
        files.append(matches[0])
    result = PythonIndexObservation(
        PythonRegistry(python_text(doc["registry"])),
        python_text(doc["version"]),
        python_text(doc["index-digest"]),
        tuple(files),
        python_text(doc["classification"]),
    )
    _normalized(doc, result.to_document())
    return result


def python_remote_observation_from_document(
    value: JsonValue,
    decision: PythonQualificationDecision,
    decision_reference: ArtifactReference,
    originals: tuple[PythonDistribution, ...],
) -> PythonRemoteObservation:
    """Admit a reader record against its exact decision and original bytes."""
    doc = python_object(
        value,
        {
            "schema",
            "attempt",
            "decision-reference",
            "native",
            "observed-at",
            "classification",
            "producer",
        },
    )
    result = PythonRemoteObservation(
        decision,
        decision_reference,
        python_native_observation_from_document(doc["native"], originals),
        datetime.fromisoformat(python_text(doc["observed-at"])),
    )
    _normalized(doc, result.to_document())
    return result


def admit_python_publication_snapshot(
    value: JsonValue,
    observation: PythonRemoteObservation,
    reference: ArtifactReference,
) -> PythonPublicationSnapshot:
    """Replay zero-or-one action closure from one immutable observation."""
    result = PythonPublicationSnapshot(observation, reference)
    _normalized(value, result.to_document())
    return result


def python_approval_bundle_from_document(
    value: JsonValue,
    snapshot: PythonPublicationSnapshot,
    reference: ArtifactReference,
) -> PythonApprovalBundle:
    """Replay the exact set Bundle with its reviewer-summary reference."""
    doc = python_object(
        value,
        {
            "schema",
            "attempt",
            "snapshot-reference",
            "summary-reference",
            "environment",
            "producer",
        },
    )
    result = PythonApprovalBundle(
        snapshot,
        reference,
        artifact_reference_from_document(doc["summary-reference"]),
    )
    _normalized(doc, result.to_document())
    return result


def python_authorization_from_document(
    value: JsonValue, bundle: PythonApprovalBundle, reference: ArtifactReference
) -> PythonPublicationAuthorization:
    """Replay only the current Bundle's native approval record."""
    doc = python_object(
        value,
        {
            "schema",
            "attempt",
            "bundle-reference",
            "approval-evidence",
            "completed-at",
            "producer",
        },
    )
    result = PythonPublicationAuthorization(
        bundle,
        reference,
        canonicalize(doc["approval-evidence"]),
        datetime.fromisoformat(python_text(doc["completed-at"])),
    )
    _normalized(doc, result.to_document())
    return result


def python_marker_from_document(
    value: JsonValue,
    authorization: PythonPublicationAuthorization,
    reference: ArtifactReference,
) -> PythonMutationMarker:
    """Read the exact marker; subsequent destination reads cannot replace it."""
    doc = python_object(
        value,
        {
            "schema",
            "attempt",
            "authorization-reference",
            "governance-digest",
            "governance-source-commit",
            "governance-observed-at",
            "profile-digest",
            "absence",
            "observed-at",
            "producer",
        },
    )
    initial = (
        authorization.bundle.snapshot.observation.decision.snapshot.governance
    )
    observed_at = datetime.fromisoformat(python_text(doc["observed-at"]))
    fresh = PythonGovernance(
        initial.registry,
        initial.content,
        python_text(doc["governance-source-commit"]),
        datetime.fromisoformat(python_text(doc["governance-observed-at"])),
    )
    result = PythonMutationMarker(
        authorization,
        reference,
        fresh,
        python_native_observation_from_document(doc["absence"], ()),
        observed_at,
    )
    _normalized(doc, result.to_document())
    return result
