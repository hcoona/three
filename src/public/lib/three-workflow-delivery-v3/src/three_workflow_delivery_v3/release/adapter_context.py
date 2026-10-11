"""Retained Node context consumed by ordinary live qualification."""

from __future__ import annotations

from dataclasses import dataclass
from typing import TYPE_CHECKING

from three_workflow_delivery_v3.adapters.node import (
    PackageTargetWitness,
    package_target_witness_from_document,
    validate_package_target_witness,
)
from three_workflow_delivery_v3.canonical import (
    canonical_sha256,
    parse_canonical_json,
)
from three_workflow_delivery_v3.records.release import (
    QualificationSnapshot,
    ReleaseAttemptIdentity,
    SimulationBinding,
    SimulationIdentity,
)

if TYPE_CHECKING:
    from three_workflow_delivery_v3.canonical import JsonValue

RELEASE_ADAPTER_CONTEXT_SCHEMA = "workflow-delivery/v3/release-adapter-context"
_SHA256_LENGTH = 71


def _string(value: object, *, field: str) -> str:
    if type(value) is not str or not value or value != value.strip():
        message = f"{field} must be a nonempty exact string"
        raise ValueError(message)
    return value


def _positive(value: object, *, field: str) -> int:
    if type(value) is not int or value <= 0:
        message = f"{field} must be a positive non-Boolean integer"
        raise ValueError(message)
    return value


def _closed(
    value: JsonValue,
    *,
    field: str,
    fields: frozenset[str],
    schema: str | None = None,
) -> dict[str, JsonValue]:
    if not isinstance(value, dict):
        message = f"{field} must be an object"
        raise TypeError(message)
    expected = fields | ({"schema"} if schema is not None else set())
    missing = expected - value.keys()
    unknown = value.keys() - expected
    if missing or unknown:
        detail = (
            f"missing {sorted(missing)[0]}"
            if missing
            else f"unknown {sorted(unknown)[0]}"
        )
        message = f"{field} closed schema mismatch: {detail}"
        raise ValueError(message)
    if schema is not None and value["schema"] != schema:
        message = f"{field} schema mismatch"
        raise ValueError(message)
    return value


@dataclass(frozen=True, slots=True)
class ReleaseAdapterContext:
    """Frozen Node execution context emitted with a Qualification Snapshot."""

    subject: SimulationIdentity | ReleaseAttemptIdentity
    qualification_snapshot_digest: str
    repository_model_digest: str
    project_path: str
    source_date_epoch: int
    node_version: str
    pnpm_version: str
    npm_version: str
    witness: PackageTargetWitness

    def __post_init__(self) -> None:
        """Reject context facts outside the current qualification subject."""
        if type(self.subject) not in {
            SimulationIdentity,
            ReleaseAttemptIdentity,
        }:
            message = "Release Adapter context subject has the wrong type"
            raise TypeError(message)
        for field, value in (
            (
                "qualification_snapshot_digest",
                self.qualification_snapshot_digest,
            ),
            ("repository_model_digest", self.repository_model_digest),
        ):
            accepted = _string(value, field=f"adapter context.{field}")
            if (
                not accepted.startswith("sha256:")
                or len(accepted) != _SHA256_LENGTH
            ):
                message = f"adapter context.{field} is not SHA-256"
                raise ValueError(message)
        _string(self.project_path, field="adapter context.project_path")
        _positive(
            self.source_date_epoch,
            field="adapter context.source_date_epoch",
        )
        _string(self.node_version, field="adapter context.node_version")
        _string(self.pnpm_version, field="adapter context.pnpm_version")
        _string(self.npm_version, field="adapter context.npm_version")
        validate_package_target_witness(self.witness)
        expected_purpose = (
            "release-simulation"
            if isinstance(self.subject, SimulationIdentity)
            else "live-release"
        )
        if self.witness.purpose != expected_purpose:
            message = "Release Adapter context witness is cross-purpose"
            raise ValueError(message)

    def to_document(self) -> dict[str, JsonValue]:
        """Return the closed canonical Adapter context."""
        return {
            "schema": RELEASE_ADAPTER_CONTEXT_SCHEMA,
            "subject": self.subject.to_document(),
            "qualification-snapshot-digest": (
                self.qualification_snapshot_digest
            ),
            "repository-model-digest": self.repository_model_digest,
            "project-path": self.project_path,
            "source-date-epoch": self.source_date_epoch,
            "toolchain": {
                "node": self.node_version,
                "pnpm": self.pnpm_version,
                "npm": self.npm_version,
            },
            "witness": self.witness.to_document(),
        }

    @property
    def context_digest(self) -> str:
        """Return the canonical Adapter context digest."""
        return canonical_sha256(self.to_document())


def release_adapter_context_from_bytes(
    content: bytes,
    *,
    snapshot: QualificationSnapshot,
    expected_digest: str,
) -> ReleaseAdapterContext:
    """Admit a canonical Adapter context against the current Snapshot."""
    document = _closed(
        parse_canonical_json(content),
        field="Release Adapter context",
        schema=RELEASE_ADAPTER_CONTEXT_SCHEMA,
        fields=frozenset(
            {
                "subject",
                "qualification-snapshot-digest",
                "repository-model-digest",
                "project-path",
                "source-date-epoch",
                "toolchain",
                "witness",
            }
        ),
    )
    subject_document = document["subject"]
    if not isinstance(subject_document, dict):
        message = "Release Adapter context subject must be an object"
        raise TypeError(message)
    expected_subject = (
        snapshot.subject.simulation
        if isinstance(snapshot.subject, SimulationBinding)
        else snapshot.subject
    )
    if subject_document != expected_subject.to_document():
        message = "Release Adapter context subject does not match Snapshot"
        raise ValueError(message)
    toolchain = _closed(
        document["toolchain"],
        field="Release Adapter context toolchain",
        fields=frozenset({"node", "pnpm", "npm"}),
    )
    witness_document = document["witness"]
    if not isinstance(witness_document, dict):
        message = "Release Adapter context witness must be an object"
        raise TypeError(message)
    context = ReleaseAdapterContext(
        subject=expected_subject,
        qualification_snapshot_digest=_string(
            document["qualification-snapshot-digest"],
            field="adapter context.snapshot",
        ),
        repository_model_digest=_string(
            document["repository-model-digest"],
            field="adapter context.model",
        ),
        project_path=_string(
            document["project-path"],
            field="adapter context.project-path",
        ),
        source_date_epoch=_positive(
            document["source-date-epoch"],
            field="adapter context.source-date-epoch",
        ),
        node_version=_string(toolchain["node"], field="toolchain.node"),
        pnpm_version=_string(toolchain["pnpm"], field="toolchain.pnpm"),
        npm_version=_string(toolchain["npm"], field="toolchain.npm"),
        witness=package_target_witness_from_document(witness_document),
    )
    if context.context_digest != expected_digest:
        message = "Release Adapter context canonical digest mismatch"
        raise ValueError(message)
    if (
        context.subject != expected_subject
        or context.qualification_snapshot_digest != snapshot.snapshot_digest
        or context.repository_model_digest != snapshot.repository_model_digest
        or context.witness.target != snapshot.target
        or context.witness.release_unit != snapshot.release_unit
        or context.witness.nbgv != snapshot.nbgv
        or context.witness.purpose
        != (
            "release-simulation"
            if isinstance(expected_subject, SimulationIdentity)
            else "live-release"
        )
        or canonical_sha256(context.witness.to_document())
        != snapshot.build_requests[0].witness_digest
    ):
        message = "Release Adapter context does not match current Snapshot"
        raise ValueError(message)
    return context
