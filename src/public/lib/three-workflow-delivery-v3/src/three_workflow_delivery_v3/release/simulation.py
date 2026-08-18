"""Release Adapter context and commit-7 simulation transport records."""

from __future__ import annotations

from dataclasses import dataclass
from typing import TYPE_CHECKING, cast

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
    HYPOTHETICAL_ACTIONS_REPORT_PRODUCER,
    NPMJS_OBSERVER_PRODUCER,
    HypotheticalAction,
    ProjectionObservation,
    QualificationDecision,
    QualificationSnapshot,
    ReleaseAttemptIdentity,
    SimulationBinding,
    SimulationIdentity,
)
from three_workflow_delivery_v3.records.release_transport import (
    release_record_from_document,
    simulation_identity_from_document,
)

if TYPE_CHECKING:
    from three_workflow_delivery_v3.canonical import JsonValue

RELEASE_ADAPTER_CONTEXT_SCHEMA = "workflow-delivery/v3/release-adapter-context"
SIMULATION_OBSERVATION_SET_SCHEMA = (
    "workflow-delivery/v3/simulation-observation-set"
)
HYPOTHETICAL_ACTIONS_REPORT_SCHEMA = (
    "workflow-delivery/v3/hypothetical-actions-report"
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


def _array(value: JsonValue, *, field: str) -> list[JsonValue]:
    if not isinstance(value, list):
        message = f"{field} must be an array"
        raise TypeError(message)
    return value


def _require_exact_producer(
    actual: str,
    expected: str,
    *,
    field: str,
) -> None:
    if actual != expected:
        message = f"{field} producer must be {expected}"
        raise ValueError(message)


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


@dataclass(frozen=True, slots=True)
class SimulationObservationSet:
    """Commit-7 physical transport bundle for simulation observations."""

    simulation: SimulationIdentity
    purpose: str
    target: str
    producer: str
    workflow_run_id: int
    run_attempt: int
    qualification_snapshot_digest: str
    qualification_decision_digest: str
    observations: tuple[ProjectionObservation, ...]

    def __post_init__(self) -> None:
        """Reject stale, live, non-canonical, or substituted observations."""
        if type(self.simulation) is not SimulationIdentity:
            message = "Observation set requires SimulationIdentity"
            raise TypeError(message)
        if self.purpose != "release-simulation":
            message = "Observation set purpose must be release-simulation"
            raise ValueError(message)
        _string(self.target, field="observation set.target")
        _string(self.producer, field="observation set.producer")
        _require_exact_producer(
            self.producer,
            NPMJS_OBSERVER_PRODUCER,
            field="Observation set",
        )
        if self.workflow_run_id != self.simulation.workflow_run_id:
            message = "Observation set workflow_run_id binding mismatch"
            raise ValueError(message)
        if self.run_attempt != self.simulation.run_attempt:
            message = "Observation set run_attempt binding mismatch"
            raise ValueError(message)
        _positive(self.workflow_run_id, field="observation set.workflow_run_id")
        _positive(self.run_attempt, field="observation set.run_attempt")
        _string(
            self.qualification_snapshot_digest,
            field="observation set.snapshot",
        )
        _string(
            self.qualification_decision_digest,
            field="observation set.decision",
        )
        if type(self.observations) is not tuple:
            message = "Observation set observations must be a tuple"
            raise TypeError(message)
        seen: set[str] = set()
        for observation in self.observations:
            if type(observation) is not ProjectionObservation:
                message = "Observation set item has wrong runtime type"
                raise TypeError(message)
            if (
                observation.subject != self.simulation
                or observation.purpose != self.purpose
                or observation.target != self.target
                or observation.producer != self.producer
                or observation.qualification_snapshot_digest
                != self.qualification_snapshot_digest
            ):
                message = "Observation set item binding mismatch"
                raise ValueError(message)
            digest = observation.observation_digest
            if digest in seen:
                message = "Observation set contains duplicate observations"
                raise ValueError(message)
            seen.add(digest)

    @property
    def observation_digests(self) -> tuple[str, ...]:
        """Return exact canonical observation digests in bundle order."""
        return tuple(
            observation.observation_digest for observation in self.observations
        )

    def to_document(self) -> dict[str, JsonValue]:
        """Return the closed canonical observation bundle."""
        return cast(
            "dict[str, JsonValue]",
            {
                "schema": SIMULATION_OBSERVATION_SET_SCHEMA,
                "simulation": self.simulation.to_document(),
                "purpose": self.purpose,
                "target": self.target,
                "producer": self.producer,
                "workflow-run-id": self.workflow_run_id,
                "run-attempt": self.run_attempt,
                "qualification-snapshot-digest": (
                    self.qualification_snapshot_digest
                ),
                "qualification-decision-digest": (
                    self.qualification_decision_digest
                ),
                "observation-digests": list(self.observation_digests),
                "observations": [
                    observation.to_document()
                    for observation in self.observations
                ],
            },
        )

    @property
    def set_digest(self) -> str:
        """Return the canonical observation bundle digest."""
        return canonical_sha256(self.to_document())


@dataclass(frozen=True, slots=True)
class HypotheticalActionsReport:
    """Commit-7 physical transport report for simulation-only actions."""

    simulation: SimulationIdentity
    purpose: str
    target: str
    producer: str
    workflow_run_id: int
    run_attempt: int
    qualification_snapshot_digest: str
    qualification_decision_digest: str
    observation_set_digest: str
    observation_digests: tuple[str, ...]
    actions: tuple[HypotheticalAction, ...]
    publication_snapshot_emitted: bool

    def __post_init__(self) -> None:  # noqa: C901
        """Reject live lineage, stale inputs, or action substitution."""
        if type(self.simulation) is not SimulationIdentity:
            message = "Hypothetical actions report requires SimulationIdentity"
            raise TypeError(message)
        if self.purpose != "release-simulation":
            message = "Hypothetical actions report purpose mismatch"
            raise ValueError(message)
        _string(self.target, field="hypothetical actions report.target")
        _string(self.producer, field="hypothetical actions report.producer")
        _require_exact_producer(
            self.producer,
            HYPOTHETICAL_ACTIONS_REPORT_PRODUCER,
            field="Hypothetical actions report",
        )
        if self.workflow_run_id != self.simulation.workflow_run_id:
            message = "Actions report workflow_run_id binding mismatch"
            raise ValueError(message)
        if self.run_attempt != self.simulation.run_attempt:
            message = "Actions report run_attempt binding mismatch"
            raise ValueError(message)
        _positive(self.workflow_run_id, field="actions report.workflow_run_id")
        _positive(self.run_attempt, field="actions report.run_attempt")
        _string(
            self.qualification_snapshot_digest,
            field="actions report.snapshot",
        )
        _string(
            self.qualification_decision_digest,
            field="actions report.decision",
        )
        _string(
            self.observation_set_digest,
            field="actions report.observation_set_digest",
        )
        if type(self.observation_digests) is not tuple:
            message = "Actions report observation digests must be a tuple"
            raise TypeError(message)
        if type(self.actions) is not tuple:
            message = "Actions report actions must be a tuple"
            raise TypeError(message)
        for action in self.actions:
            if type(action) is not HypotheticalAction:
                message = "Actions report item has wrong runtime type"
                raise TypeError(message)
            if (
                action.simulation != self.simulation
                or action.qualification_snapshot_digest
                != self.qualification_snapshot_digest
                or action.qualification_decision_digest
                != self.qualification_decision_digest
            ):
                message = "Actions report item binding mismatch"
                raise ValueError(message)
        if self.publication_snapshot_emitted is not False:
            message = "Simulation action report cannot bind PublicationSnapshot"
            raise ValueError(message)

    @property
    def action_digests(self) -> tuple[str, ...]:
        """Return exact canonical action digests in report order."""
        return tuple(
            canonical_sha256(action.to_document()) for action in self.actions
        )

    def to_document(self) -> dict[str, JsonValue]:
        """Return the closed canonical action report."""
        return cast(
            "dict[str, JsonValue]",
            {
                "schema": HYPOTHETICAL_ACTIONS_REPORT_SCHEMA,
                "simulation": self.simulation.to_document(),
                "purpose": self.purpose,
                "target": self.target,
                "producer": self.producer,
                "workflow-run-id": self.workflow_run_id,
                "run-attempt": self.run_attempt,
                "qualification-snapshot-digest": (
                    self.qualification_snapshot_digest
                ),
                "qualification-decision-digest": (
                    self.qualification_decision_digest
                ),
                "observation-set-digest": self.observation_set_digest,
                "observation-digests": list(self.observation_digests),
                "action-digests": list(self.action_digests),
                "actions": [action.to_document() for action in self.actions],
                "publication-snapshot-emitted": (
                    self.publication_snapshot_emitted
                ),
            },
        )

    @property
    def report_digest(self) -> str:
        """Return the canonical action report digest."""
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


def simulation_observation_set_from_bytes(
    content: bytes,
    *,
    snapshot: QualificationSnapshot,
    decision: QualificationDecision,
    expected_digest: str,
) -> SimulationObservationSet:
    """Admit a canonical commit-7 observation bundle."""
    document = _closed(
        parse_canonical_json(content),
        field="Simulation observation set",
        schema=SIMULATION_OBSERVATION_SET_SCHEMA,
        fields=frozenset(
            {
                "simulation",
                "purpose",
                "target",
                "producer",
                "workflow-run-id",
                "run-attempt",
                "qualification-snapshot-digest",
                "qualification-decision-digest",
                "observation-digests",
                "observations",
            }
        ),
    )
    simulation_document = document["simulation"]
    if not isinstance(simulation_document, dict):
        message = "Observation set simulation must be an object"
        raise TypeError(message)
    observations_value = document["observations"]
    if not isinstance(observations_value, list):
        message = "Observation set observations must be an array"
        raise TypeError(message)
    observations: list[ProjectionObservation] = []
    for index, item in enumerate(observations_value):
        if not isinstance(item, dict):
            message = f"Observation set observations[{index}] must be object"
            raise TypeError(message)
        observation = cast(
            "ProjectionObservation",
            release_record_from_document(
                item,
                expected_type=ProjectionObservation,
            ),
        )
        observations.append(observation)
    bundle = SimulationObservationSet(
        simulation=simulation_identity_from_document(simulation_document),
        purpose=_string(document["purpose"], field="observation set.purpose"),
        target=_string(document["target"], field="observation set.target"),
        producer=_string(
            document["producer"],
            field="observation set.producer",
        ),
        workflow_run_id=_positive(
            document["workflow-run-id"],
            field="observation set.workflow-run-id",
        ),
        run_attempt=_positive(
            document["run-attempt"],
            field="observation set.run-attempt",
        ),
        qualification_snapshot_digest=_string(
            document["qualification-snapshot-digest"],
            field="observation set.snapshot",
        ),
        qualification_decision_digest=_string(
            document["qualification-decision-digest"],
            field="observation set.decision",
        ),
        observations=tuple(observations),
    )
    if document["observation-digests"] != list(bundle.observation_digests):
        message = "Observation set digest list mismatch"
        raise ValueError(message)
    _validate_boundary_basis(
        snapshot,
        decision,
        bundle.simulation,
        bundle.qualification_snapshot_digest,
        bundle.qualification_decision_digest,
    )
    if bundle.target != snapshot.target:
        message = "Observation set target binding mismatch"
        raise ValueError(message)
    if bundle.set_digest != expected_digest:
        message = "Observation set canonical digest mismatch"
        raise ValueError(message)
    if bundle.to_document() != document:
        message = "Observation set is not normalized"
        raise ValueError(message)
    return bundle


def hypothetical_actions_report_from_bytes(
    content: bytes,
    *,
    snapshot: QualificationSnapshot,
    decision: QualificationDecision,
    observations: SimulationObservationSet,
    expected_digest: str,
) -> HypotheticalActionsReport:
    """Admit a canonical commit-7 hypothetical action report."""
    document = _closed(
        parse_canonical_json(content),
        field="Hypothetical actions report",
        schema=HYPOTHETICAL_ACTIONS_REPORT_SCHEMA,
        fields=frozenset(
            {
                "simulation",
                "purpose",
                "target",
                "producer",
                "workflow-run-id",
                "run-attempt",
                "qualification-snapshot-digest",
                "qualification-decision-digest",
                "observation-set-digest",
                "observation-digests",
                "action-digests",
                "actions",
                "publication-snapshot-emitted",
            }
        ),
    )
    simulation_document = document["simulation"]
    if not isinstance(simulation_document, dict):
        message = "Actions report simulation must be an object"
        raise TypeError(message)
    actions_value = document["actions"]
    if not isinstance(actions_value, list):
        message = "Actions report actions must be an array"
        raise TypeError(message)
    actions: list[HypotheticalAction] = []
    for index, item in enumerate(actions_value):
        if not isinstance(item, dict):
            message = f"Actions report actions[{index}] must be object"
            raise TypeError(message)
        action = cast(
            "HypotheticalAction",
            release_record_from_document(
                item,
                expected_type=HypotheticalAction,
            ),
        )
        actions.append(action)
    publication = document["publication-snapshot-emitted"]
    if type(publication) is not bool:
        message = "publication-snapshot-emitted must be Boolean"
        raise TypeError(message)
    report = HypotheticalActionsReport(
        simulation=simulation_identity_from_document(simulation_document),
        purpose=_string(document["purpose"], field="actions report.purpose"),
        target=_string(document["target"], field="actions report.target"),
        producer=_string(document["producer"], field="actions report.producer"),
        workflow_run_id=_positive(
            document["workflow-run-id"],
            field="actions report.workflow-run-id",
        ),
        run_attempt=_positive(
            document["run-attempt"],
            field="actions report.run-attempt",
        ),
        qualification_snapshot_digest=_string(
            document["qualification-snapshot-digest"],
            field="actions report.snapshot",
        ),
        qualification_decision_digest=_string(
            document["qualification-decision-digest"],
            field="actions report.decision",
        ),
        observation_set_digest=_string(
            document["observation-set-digest"],
            field="actions report.observation-set",
        ),
        observation_digests=tuple(
            _string(item, field=f"actions report.observation-digests[{index}]")
            for index, item in enumerate(
                _array(
                    document["observation-digests"],
                    field="observation-digests",
                )
            )
        ),
        actions=tuple(actions),
        publication_snapshot_emitted=publication,
    )
    if document["action-digests"] != list(report.action_digests):
        message = "Actions report digest list mismatch"
        raise ValueError(message)
    _validate_boundary_basis(
        snapshot,
        decision,
        report.simulation,
        report.qualification_snapshot_digest,
        report.qualification_decision_digest,
    )
    if (
        report.target != snapshot.target
        or report.observation_set_digest != observations.set_digest
        or report.observation_digests != observations.observation_digests
    ):
        message = "Actions report observation binding mismatch"
        raise ValueError(message)
    if report.report_digest != expected_digest:
        message = "Actions report canonical digest mismatch"
        raise ValueError(message)
    if report.to_document() != document:
        message = "Actions report is not normalized"
        raise ValueError(message)
    return report


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
    """Render a deterministic human summary for the commit-7 simulation."""
    if not isinstance(snapshot.subject, SimulationBinding):
        message = "Simulation summary requires simulation Snapshot"
        raise TypeError(message)
    obligations = "\n".join(
        f"- `{item.obligation.obligation_id}`: `{item.outcome}`"
        for item in decision.obligation_dispositions
    )
    observations = _array(
        outcome_document["observation-digests"],
        field="outcome.observation-digests",
    )
    actions = _array(
        outcome_document["hypothetical-actions"],
        field="outcome.hypothetical-actions",
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
        f"- Observation records: `{len(observations)}`\n"
        f"- Hypothetical actions: `{len(actions)}`\n"
        f"- Terminal result: `{outcome_document['terminal-result']}`\n"
        f"- Failure class: `{outcome_document['failure-class']}`\n"
        f"- Next action: `{outcome_document['next-action']}`\n\n"
        "## Qualification obligations\n\n"
        f"{obligations}\n"
    )


__all__ = [
    "HYPOTHETICAL_ACTIONS_REPORT_SCHEMA",
    "RELEASE_ADAPTER_CONTEXT_SCHEMA",
    "SIMULATION_OBSERVATION_SET_SCHEMA",
    "HypotheticalActionsReport",
    "ReleaseAdapterContext",
    "SimulationObservationSet",
    "hypothetical_actions_report_from_bytes",
    "release_adapter_context_from_bytes",
    "render_simulation_summary",
    "simulation_observation_set_from_bytes",
]
