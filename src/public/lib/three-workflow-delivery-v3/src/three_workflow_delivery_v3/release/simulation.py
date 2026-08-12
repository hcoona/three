"""Commit-6 simulation Adapter context and explicit stop-line records."""

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
    QualificationDecision,
    QualificationSnapshot,
    SimulationBinding,
    SimulationIdentity,
)
from three_workflow_delivery_v3.records.release_transport import (
    simulation_identity_from_document,
)

if TYPE_CHECKING:
    from three_workflow_delivery_v3.canonical import JsonValue

RELEASE_ADAPTER_CONTEXT_SCHEMA = "workflow-delivery/v3/release-adapter-context"
SIMULATION_OBSERVATION_BOUNDARY_SCHEMA = (
    "workflow-delivery/v3/simulation-observation-boundary"
)
HYPOTHETICAL_ACTIONS_BOUNDARY_SCHEMA = (
    "workflow-delivery/v3/hypothetical-actions-boundary"
)
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

    simulation: SimulationIdentity
    qualification_snapshot_digest: str
    repository_model_digest: str
    project_path: str
    source_date_epoch: int
    node_version: str
    pnpm_version: str
    npm_version: str
    witness: PackageTargetWitness

    def __post_init__(self) -> None:
        """Reject context facts outside the current simulation."""
        if type(self.simulation) is not SimulationIdentity:
            message = "Release Adapter context requires SimulationIdentity"
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
        if self.witness.purpose != "release-simulation":
            message = "Release Adapter context witness is cross-purpose"
            raise ValueError(message)

    def to_document(self) -> dict[str, JsonValue]:
        """Return the closed canonical Adapter context."""
        return {
            "schema": RELEASE_ADAPTER_CONTEXT_SCHEMA,
            "simulation": self.simulation.to_document(),
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


@dataclass(frozen=True, slots=True)
class SimulationObservationBoundary:
    """Explicit non-authoritative observation-unavailable boundary."""

    simulation: SimulationIdentity
    qualification_snapshot_digest: str
    qualification_decision_digest: str
    status: str
    authoritative: bool
    network_performed: bool
    reason: str
    next_action: str

    def __post_init__(self) -> None:
        """Reject any false observation claim at the commit-6 stop line."""
        if type(self.simulation) is not SimulationIdentity:
            message = "Observation boundary requires SimulationIdentity"
            raise TypeError(message)
        if self.status != "unavailable":
            message = "Observation boundary status must be unavailable"
            raise ValueError(message)
        if self.authoritative is not False:
            message = "Observation boundary cannot be authoritative"
            raise ValueError(message)
        if self.network_performed is not False:
            message = "Observation boundary cannot perform network access"
            raise ValueError(message)
        if self.reason != "observation-adapter-not-implemented":
            message = "Observation boundary reason is not commit-6 closed"
            raise ValueError(message)
        if self.next_action != "implement-observation-adapter":
            message = "Observation boundary next action is not closed"
            raise ValueError(message)
        _string(
            self.qualification_snapshot_digest,
            field="observation boundary.snapshot",
        )
        _string(
            self.qualification_decision_digest,
            field="observation boundary.decision",
        )

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical unavailable boundary."""
        return {
            "schema": SIMULATION_OBSERVATION_BOUNDARY_SCHEMA,
            "simulation": self.simulation.to_document(),
            "qualification-snapshot-digest": (
                self.qualification_snapshot_digest
            ),
            "qualification-decision-digest": (
                self.qualification_decision_digest
            ),
            "status": self.status,
            "authoritative": self.authoritative,
            "network-performed": self.network_performed,
            "reason": self.reason,
            "next-action": self.next_action,
        }

    @property
    def boundary_digest(self) -> str:
        """Return the canonical observation boundary digest."""
        return canonical_sha256(self.to_document())


@dataclass(frozen=True, slots=True)
class HypotheticalActionsBoundary:
    """Explicit empty action boundary while observation is unsupported."""

    simulation: SimulationIdentity
    qualification_snapshot_digest: str
    qualification_decision_digest: str
    observation_boundary_digest: str
    status: str
    actions: tuple[object, ...]
    publication_snapshot_emitted: bool

    def __post_init__(self) -> None:
        """Reject actions or a live second Snapshot at commit 6."""
        if type(self.simulation) is not SimulationIdentity:
            message = "Actions boundary requires SimulationIdentity"
            raise TypeError(message)
        if self.status != "unsupported-observation":
            message = "Actions boundary status is not commit-6 closed"
            raise ValueError(message)
        if self.actions != ():
            message = "Actions boundary must be empty at commit 6"
            raise ValueError(message)
        if self.publication_snapshot_emitted is not False:
            message = "Simulation cannot emit PublicationSnapshot"
            raise ValueError(message)
        for field, value in (
            (
                "qualification_snapshot_digest",
                self.qualification_snapshot_digest,
            ),
            (
                "qualification_decision_digest",
                self.qualification_decision_digest,
            ),
            (
                "observation_boundary_digest",
                self.observation_boundary_digest,
            ),
        ):
            _string(value, field=f"actions boundary.{field}")

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical empty hypothetical-actions boundary."""
        return {
            "schema": HYPOTHETICAL_ACTIONS_BOUNDARY_SCHEMA,
            "simulation": self.simulation.to_document(),
            "qualification-snapshot-digest": (
                self.qualification_snapshot_digest
            ),
            "qualification-decision-digest": (
                self.qualification_decision_digest
            ),
            "observation-boundary-digest": (self.observation_boundary_digest),
            "status": self.status,
            "actions": [],
            "publication-snapshot-emitted": (self.publication_snapshot_emitted),
        }

    @property
    def boundary_digest(self) -> str:
        """Return the canonical action boundary digest."""
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
                "simulation",
                "qualification-snapshot-digest",
                "repository-model-digest",
                "project-path",
                "source-date-epoch",
                "toolchain",
                "witness",
            }
        ),
    )
    simulation_document = document["simulation"]
    if not isinstance(simulation_document, dict):
        message = "Release Adapter context simulation must be an object"
        raise TypeError(message)
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
        simulation=simulation_identity_from_document(simulation_document),
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
    if not isinstance(snapshot.subject, SimulationBinding):
        message = "Release Adapter context requires simulation Snapshot"
        raise TypeError(message)
    if (
        context.simulation != snapshot.subject.simulation
        or context.qualification_snapshot_digest != snapshot.snapshot_digest
        or context.repository_model_digest != snapshot.repository_model_digest
        or context.witness.target != snapshot.target
        or context.witness.release_unit != snapshot.release_unit
        or context.witness.nbgv != snapshot.nbgv
        or context.witness.purpose != snapshot.subject.purpose
        or canonical_sha256(context.witness.to_document())
        != snapshot.build_requests[0].witness_digest
    ):
        message = "Release Adapter context does not match current Snapshot"
        raise ValueError(message)
    return context


def observation_boundary_from_bytes(
    content: bytes,
    *,
    snapshot: QualificationSnapshot,
    decision: QualificationDecision,
    expected_digest: str,
) -> SimulationObservationBoundary:
    """Admit the exact current commit-6 observation boundary."""
    document = _closed(
        parse_canonical_json(content),
        field="Simulation observation boundary",
        schema=SIMULATION_OBSERVATION_BOUNDARY_SCHEMA,
        fields=frozenset(
            {
                "simulation",
                "qualification-snapshot-digest",
                "qualification-decision-digest",
                "status",
                "authoritative",
                "network-performed",
                "reason",
                "next-action",
            }
        ),
    )
    simulation_document = document["simulation"]
    if not isinstance(simulation_document, dict):
        message = "Observation boundary simulation must be an object"
        raise TypeError(message)
    authoritative = document["authoritative"]
    network_performed = document["network-performed"]
    if type(authoritative) is not bool or type(network_performed) is not bool:
        message = "Observation boundary Boolean fields are malformed"
        raise TypeError(message)
    boundary = SimulationObservationBoundary(
        simulation=simulation_identity_from_document(simulation_document),
        qualification_snapshot_digest=_string(
            document["qualification-snapshot-digest"],
            field="observation boundary.snapshot",
        ),
        qualification_decision_digest=_string(
            document["qualification-decision-digest"],
            field="observation boundary.decision",
        ),
        status=_string(
            document["status"],
            field="observation boundary.status",
        ),
        authoritative=authoritative,
        network_performed=network_performed,
        reason=_string(
            document["reason"],
            field="observation boundary.reason",
        ),
        next_action=_string(
            document["next-action"],
            field="observation boundary.next-action",
        ),
    )
    _validate_boundary_basis(
        snapshot,
        decision,
        boundary.simulation,
        boundary.qualification_snapshot_digest,
        boundary.qualification_decision_digest,
    )
    if boundary.boundary_digest != expected_digest:
        message = "Observation boundary canonical digest mismatch"
        raise ValueError(message)
    return boundary


def hypothetical_actions_boundary_from_bytes(
    content: bytes,
    *,
    snapshot: QualificationSnapshot,
    decision: QualificationDecision,
    observation: SimulationObservationBoundary,
    expected_digest: str,
) -> HypotheticalActionsBoundary:
    """Admit the exact empty commit-6 hypothetical-actions boundary."""
    document = _closed(
        parse_canonical_json(content),
        field="Hypothetical actions boundary",
        schema=HYPOTHETICAL_ACTIONS_BOUNDARY_SCHEMA,
        fields=frozenset(
            {
                "simulation",
                "qualification-snapshot-digest",
                "qualification-decision-digest",
                "observation-boundary-digest",
                "status",
                "actions",
                "publication-snapshot-emitted",
            }
        ),
    )
    simulation_document = document["simulation"]
    if not isinstance(simulation_document, dict):
        message = "Actions boundary simulation must be an object"
        raise TypeError(message)
    if document["actions"] != []:
        message = "Hypothetical actions boundary must contain an empty array"
        raise ValueError(message)
    publication = document["publication-snapshot-emitted"]
    if type(publication) is not bool:
        message = "publication-snapshot-emitted must be Boolean"
        raise TypeError(message)
    boundary = HypotheticalActionsBoundary(
        simulation=simulation_identity_from_document(simulation_document),
        qualification_snapshot_digest=_string(
            document["qualification-snapshot-digest"],
            field="actions boundary.snapshot",
        ),
        qualification_decision_digest=_string(
            document["qualification-decision-digest"],
            field="actions boundary.decision",
        ),
        observation_boundary_digest=_string(
            document["observation-boundary-digest"],
            field="actions boundary.observation",
        ),
        status=_string(document["status"], field="actions boundary.status"),
        actions=(),
        publication_snapshot_emitted=publication,
    )
    _validate_boundary_basis(
        snapshot,
        decision,
        boundary.simulation,
        boundary.qualification_snapshot_digest,
        boundary.qualification_decision_digest,
    )
    if boundary.observation_boundary_digest != observation.boundary_digest:
        message = "Actions boundary observation binding mismatch"
        raise ValueError(message)
    if boundary.boundary_digest != expected_digest:
        message = "Actions boundary canonical digest mismatch"
        raise ValueError(message)
    return boundary


def _validate_boundary_basis(
    snapshot: QualificationSnapshot,
    decision: QualificationDecision,
    simulation: SimulationIdentity,
    snapshot_digest: str,
    decision_digest: str,
) -> None:
    if not isinstance(snapshot.subject, SimulationBinding):
        message = "Simulation boundary requires simulation Snapshot"
        raise TypeError(message)
    if (
        simulation != snapshot.subject.simulation
        or snapshot_digest != snapshot.snapshot_digest
        or decision_digest != decision.decision_digest
        or decision.subject != simulation
        or decision.qualification_snapshot_digest != snapshot.snapshot_digest
    ):
        message = "Simulation boundary current binding mismatch"
        raise ValueError(message)


def render_simulation_summary(
    snapshot: QualificationSnapshot,
    decision: QualificationDecision,
    outcome_document: dict[str, JsonValue],
) -> str:
    """Render a deterministic human summary for the commit-6 simulation."""
    if not isinstance(snapshot.subject, SimulationBinding):
        message = "Simulation summary requires simulation Snapshot"
        raise TypeError(message)
    obligations = "\n".join(
        f"- `{item.obligation.obligation_id}`: `{item.outcome}`"
        for item in decision.obligation_dispositions
    )
    return (
        "# Workflow Delivery v3 Official simulation\n\n"
        f"- Simulation: `{snapshot.subject.simulation.identity}`\n"
        f"- Target: `{snapshot.target}`\n"
        "- Mode: `official/simulation`\n"
        f"- Canonical version: `{snapshot.nbgv.canonical_version}`\n"
        f"- npm version: `{snapshot.nbgv.npm_package_version}`\n"
        f"- Repository Model: `{snapshot.repository_model_digest}`\n"
        f"- Qualification Snapshot: `{snapshot.snapshot_digest}`\n"
        f"- Qualification Decision: `{decision.decision_digest}`\n"
        f"- Qualification result: `{decision.terminal_result}`\n"
        "- Observation: `unavailable (commit 6)`\n"
        "- Hypothetical actions: `0`\n"
        f"- Terminal result: `{outcome_document['terminal-result']}`\n"
        f"- Failure class: `{outcome_document['failure-class']}`\n"
        f"- Next action: `{outcome_document['next-action']}`\n\n"
        "## Qualification obligations\n\n"
        f"{obligations}\n"
    )


__all__ = [
    "HYPOTHETICAL_ACTIONS_BOUNDARY_SCHEMA",
    "RELEASE_ADAPTER_CONTEXT_SCHEMA",
    "SIMULATION_OBSERVATION_BOUNDARY_SCHEMA",
    "HypotheticalActionsBoundary",
    "ReleaseAdapterContext",
    "SimulationObservationBoundary",
    "hypothetical_actions_boundary_from_bytes",
    "observation_boundary_from_bytes",
    "release_adapter_context_from_bytes",
    "render_simulation_summary",
]
