"""Strict immutable records for Workflow Delivery v3 Release commit 6."""

from __future__ import annotations

import re
from dataclasses import dataclass
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3.canonical import (
    canonical_sha256,
    parse_canonical_json,
)
from three_workflow_delivery_v3.records.artifacts import (
    ArtifactContentIdentity,
    ArtifactTransportIdentity,
)
from three_workflow_delivery_v3.repository.node_provider import (
    NbgvFacts,
    validate_nbgv_facts,
)

if TYPE_CHECKING:
    from three_workflow_delivery_v3.canonical import JsonValue
    from three_workflow_delivery_v3.records.release_transport import (
        ReleaseAdmissionBindings,
    )

RELEASE_INTENT_SCHEMA = "workflow-delivery/v3/release-intent"
OFFICIAL_PRODUCT_IDENTITY_SCHEMA = (
    "workflow-delivery/v3/official-product-identity"
)
OFFICIAL_EXECUTION_IDENTITY_SCHEMA = (
    "workflow-delivery/v3/official-execution-identity"
)
BUDDY_EXECUTION_IDENTITY_SCHEMA = (
    "workflow-delivery/v3/buddy-execution-identity"
)
RELEASE_ATTEMPT_IDENTITY_SCHEMA = (
    "workflow-delivery/v3/release-attempt-identity"
)
SIMULATION_IDENTITY_SCHEMA = "workflow-delivery/v3/simulation-identity"
SIMULATION_BINDING_SCHEMA = "workflow-delivery/v3/simulation-binding"
EXTERNAL_PACKAGE_COORDINATE_SCHEMA = (
    "workflow-delivery/v3/external-package-coordinate"
)
RELEASE_BUILD_IDENTITY_SCHEMA = "workflow-delivery/v3/release-build-identity"
ARTIFACT_VARIANT_IDENTITY_SCHEMA = (
    "workflow-delivery/v3/artifact-variant-identity"
)
RELEASE_OUTPUT_IDENTITY_SCHEMA = "workflow-delivery/v3/release-output-identity"
RELEASE_BUILD_REQUEST_SCHEMA = "workflow-delivery/v3/release-build-request"
RELEASE_OBLIGATION_SCHEMA = "workflow-delivery/v3/release-obligation"
DESTINATION_PROJECTION_SCHEMA = "workflow-delivery/v3/destination-projection"
POTENTIAL_ACTION_CONTRACT_SCHEMA = (
    "workflow-delivery/v3/potential-action-contract"
)
QUALIFICATION_SNAPSHOT_SCHEMA = "workflow-delivery/v3/qualification-snapshot"
RELEASE_ARTIFACT_SCHEMA = "workflow-delivery/v3/release-artifact"
QUALIFICATION_EVIDENCE_SCHEMA = "workflow-delivery/v3/qualification-evidence"
OBLIGATION_DISPOSITION_SCHEMA = (
    "workflow-delivery/v3/release-obligation-disposition"
)
QUALIFICATION_DECISION_SCHEMA = "workflow-delivery/v3/qualification-decision"
OBSERVATION_VALUE_SCHEMA = "workflow-delivery/v3/observation-value"
OBSERVATION_REQUEST_FACTS_SCHEMA = (
    "workflow-delivery/v3/observation-request-facts"
)
OBSERVATION_RESPONSE_FACTS_SCHEMA = (
    "workflow-delivery/v3/observation-response-facts"
)
PROJECTION_OBSERVATION_SCHEMA = "workflow-delivery/v3/projection-observation"
HYPOTHETICAL_ACTION_SCHEMA = "workflow-delivery/v3/hypothetical-action"
PUBLICATION_ACTION_SCHEMA = "workflow-delivery/v3/publication-action"
PUBLICATION_SNAPSHOT_SCHEMA = "workflow-delivery/v3/publication-snapshot"
SIMULATION_OUTCOME_SCHEMA = "workflow-delivery/v3/simulation-outcome"
NPMJS_OBSERVATION_CONTRACT_ID = "npm/npmjs-public-observation-v1"
NPMJS_OBSERVER_PRODUCER = "observe-npmjs"
HYPOTHETICAL_ACTIONS_REPORT_PRODUCER = "materialize-hypothetical-actions"

OFFICIAL_SIMULATION_WORKFLOW_PATH = (
    ".github/workflows/workflow-delivery-v3-official-simulate.yml"
)

_SHA_PATTERN = re.compile(r"[0-9a-f]{40}\Z")
_SHA256_PATTERN = re.compile(r"sha256:[0-9a-f]{64}\Z")
_SHA512_PATTERN = re.compile(r"sha512:[0-9a-f]{128}\Z")
_REQUEST_ID_PATTERN = re.compile(r"release-request:[0-9a-f]{64}\Z")
_SIMULATION_ID_PATTERN = re.compile(r"release-simulation:[0-9a-f]{64}\Z")
_SELECTED_REF_PREFIXES = ("refs/heads/", "refs/tags/")
_CHANNELS = frozenset({"buddy", "official"})
_MODES = frozenset({"live", "simulation"})
_PURPOSES = frozenset({"live-release", "release-simulation"})
_OBSERVATION_CLASSIFICATIONS = frozenset(
    {
        "absent",
        "exact-satisfied",
        "partial",
        "conflicting",
        "unknown",
        "unprovable",
    }
)
_QUALIFICATION_OUTCOMES = frozenset({"satisfied", "failed", "incomplete"})
_QUALIFICATION_RESULTS = frozenset({"success", "failure", "incomplete"})
_PAIR_SIZE = 2


def _exact(value: object, expected: type[object], *, field: str) -> None:
    if type(value) is not expected:
        message = f"{field} has the wrong runtime type"
        raise TypeError(message)


def _string(value: object, *, field: str) -> str:
    _exact(value, str, field=field)
    accepted = cast("str", value)
    if not accepted or accepted != accepted.strip():
        message = f"{field} must be a nonempty exact string"
        raise ValueError(message)
    return accepted


def _choice(value: object, choices: frozenset[str], *, field: str) -> str:
    accepted = _string(value, field=field)
    if accepted not in choices:
        message = f"{field} has an invalid closed value"
        raise ValueError(message)
    return accepted


def _positive(value: object, *, field: str) -> int:
    _exact(value, int, field=field)
    accepted = cast("int", value)
    if accepted <= 0:
        message = f"{field} must be positive"
        raise ValueError(message)
    return accepted


def _nonnegative(value: object, *, field: str) -> int:
    _exact(value, int, field=field)
    accepted = cast("int", value)
    if accepted < 0:
        message = f"{field} must be nonnegative"
        raise ValueError(message)
    return accepted


def _sha(value: object, *, field: str) -> str:
    accepted = _string(value, field=field)
    if _SHA_PATTERN.fullmatch(accepted) is None:
        message = f"{field} must be 40 lowercase hexadecimal characters"
        raise ValueError(message)
    return accepted


def _digest(
    value: object,
    *,
    field: str,
    sha512: bool = False,
) -> str:
    accepted = _string(value, field=field)
    pattern = _SHA512_PATTERN if sha512 else _SHA256_PATTERN
    if pattern.fullmatch(accepted) is None:
        algorithm = "SHA-512" if sha512 else "SHA-256"
        message = f"{field} must be a prefixed lowercase {algorithm}"
        raise ValueError(message)
    return accepted


def _string_tuple(
    value: object,
    *,
    field: str,
    unique: bool = True,
    sorted_values: bool = False,
) -> tuple[str, ...]:
    _exact(value, tuple, field=field)
    values = cast("tuple[object, ...]", value)
    accepted = tuple(
        _string(item, field=f"{field}[{index}]")
        for index, item in enumerate(values)
    )
    if unique and len(set(accepted)) != len(accepted):
        message = f"{field} contains duplicate values"
        raise ValueError(message)
    if sorted_values and accepted != tuple(sorted(accepted)):
        message = f"{field} must use canonical sorted order"
        raise ValueError(message)
    return accepted


def _pairs(
    value: object,
    *,
    field: str,
    sorted_values: bool = True,
) -> tuple[tuple[str, str], ...]:
    _exact(value, tuple, field=field)
    pairs = cast("tuple[object, ...]", value)
    accepted: list[tuple[str, str]] = []
    for index, pair in enumerate(pairs):
        _exact(pair, tuple, field=f"{field}[{index}]")
        values = cast("tuple[object, ...]", pair)
        if len(values) != _PAIR_SIZE:
            message = f"{field}[{index}] must contain exactly two strings"
            raise ValueError(message)
        accepted.append(
            (
                _string(values[0], field=f"{field}[{index}][0]"),
                _string(values[1], field=f"{field}[{index}][1]"),
            )
        )
    if len({name for name, _ in accepted}) != len(accepted):
        message = f"{field} contains duplicate keys"
        raise ValueError(message)
    result = tuple(accepted)
    if sorted_values and result != tuple(sorted(result)):
        message = f"{field} must use canonical sorted order"
        raise ValueError(message)
    return result


def _subject_document(
    subject: SimulationIdentity | ReleaseAttemptIdentity,
) -> dict[str, JsonValue]:
    return subject.to_document()


def _subject_run(
    subject: SimulationIdentity | ReleaseAttemptIdentity,
) -> tuple[int, int]:
    return subject.workflow_run_id, subject.run_attempt


@dataclass(frozen=True, slots=True)
class ReleaseIntent:
    """Normalized manual Release request without product or Attempt identity."""

    repository: str
    workflow_path: str
    workflow_ref: str
    workflow_sha: str
    request_id: str
    actor: str
    workflow_run_id: int
    run_attempt: int
    event_kind: str
    selected_ref: str
    target: str
    channel: str
    mode: str
    purpose: str
    release_unit: str

    def __post_init__(self) -> None:
        """Reject an open, mismatched, or non-manual Intent."""
        repository = _string(self.repository, field="intent.repository")
        owner, separator, name = repository.partition("/")
        if not separator or not owner or not name or "/" in name:
            message = "intent.repository must be owner/repository"
            raise ValueError(message)
        _string(self.workflow_path, field="intent.workflow_path")
        workflow_ref = _string(
            self.workflow_ref,
            field="intent.workflow_ref",
        )
        if not workflow_ref.startswith(_SELECTED_REF_PREFIXES):
            message = "intent.workflow_ref must be a branch or tag ref"
            raise ValueError(message)
        _sha(self.workflow_sha, field="intent.workflow_sha")
        request_id = _string(self.request_id, field="intent.request_id")
        if _REQUEST_ID_PATTERN.fullmatch(request_id) is None:
            message = "intent.request_id is not canonical"
            raise ValueError(message)
        _string(self.actor, field="intent.actor")
        _positive(self.workflow_run_id, field="intent.workflow_run_id")
        _positive(self.run_attempt, field="intent.run_attempt")
        if self.event_kind != "workflow_dispatch":
            message = "intent.event_kind must be workflow_dispatch"
            raise ValueError(message)
        selected_ref = _string(
            self.selected_ref,
            field="intent.selected_ref",
        )
        if not selected_ref.startswith(_SELECTED_REF_PREFIXES):
            message = "intent.selected_ref must be a branch or tag ref"
            raise ValueError(message)
        _sha(self.target, field="intent.target")
        _choice(self.channel, _CHANNELS, field="intent.channel")
        _choice(self.mode, _MODES, field="intent.mode")
        _choice(self.purpose, _PURPOSES, field="intent.purpose")
        _string(self.release_unit, field="intent.release_unit")
        if self.workflow_ref != self.selected_ref:
            message = "Intent workflow ref must equal the selected ref"
            raise ValueError(message)
        if self.workflow_sha != self.target:
            message = "Intent workflow SHA must equal the selected target"
            raise ValueError(message)
        expected_purpose = (
            "release-simulation"
            if self.mode == "simulation"
            else "live-release"
        )
        if self.purpose != expected_purpose:
            message = "Intent purpose does not match mode"
            raise ValueError(message)

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical Release Intent."""
        return {
            "schema": RELEASE_INTENT_SCHEMA,
            "repository": self.repository,
            "workflow-path": self.workflow_path,
            "workflow-ref": self.workflow_ref,
            "workflow-sha": self.workflow_sha,
            "request-id": self.request_id,
            "actor": self.actor,
            "workflow-run-id": self.workflow_run_id,
            "run-attempt": self.run_attempt,
            "event-kind": self.event_kind,
            "selected-ref": self.selected_ref,
            "target": self.target,
            "channel": self.channel,
            "mode": self.mode,
            "purpose": self.purpose,
            "release-unit": self.release_unit,
        }

    @property
    def intent_digest(self) -> str:
        """Return the canonical Intent digest."""
        return canonical_sha256(self.to_document())


@dataclass(frozen=True, slots=True, order=True)
class OfficialProductIdentity:
    """Official channel, Release Unit, and canonical NBGV product identity."""

    channel: str
    release_unit: str
    canonical_version: str

    def __post_init__(self) -> None:
        """Reject non-Official product identity values."""
        if self.channel != "official":
            message = "Official Product Identity channel must be official"
            raise ValueError(message)
        _string(self.release_unit, field="official product.release_unit")
        _string(
            self.canonical_version,
            field="official product.canonical_version",
        )

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical Official Product Identity."""
        return {
            "schema": OFFICIAL_PRODUCT_IDENTITY_SCHEMA,
            "channel": self.channel,
            "release-unit": self.release_unit,
            "canonical-version": self.canonical_version,
        }


@dataclass(frozen=True, slots=True, order=True)
class OfficialExecutionIdentity:
    """Official Product Identity plus immutable target."""

    product: OfficialProductIdentity
    target: str

    def __post_init__(self) -> None:
        """Reject malformed Official execution identity."""
        _exact(
            self.product,
            OfficialProductIdentity,
            field="official execution.product",
        )
        _sha(self.target, field="official execution.target")

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical Official Execution Identity."""
        return {
            "schema": OFFICIAL_EXECUTION_IDENTITY_SCHEMA,
            "product": self.product.to_document(),
            "target": self.target,
        }


@dataclass(frozen=True, slots=True, order=True)
class BuddyExecutionIdentity:
    """Buddy channel, Release Unit, and immutable target."""

    channel: str
    release_unit: str
    target: str

    def __post_init__(self) -> None:
        """Reject non-Buddy execution identity values."""
        if self.channel != "buddy":
            message = "Buddy Execution Identity channel must be buddy"
            raise ValueError(message)
        _string(self.release_unit, field="buddy execution.release_unit")
        _sha(self.target, field="buddy execution.target")

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical Buddy Execution Identity."""
        return {
            "schema": BUDDY_EXECUTION_IDENTITY_SCHEMA,
            "channel": self.channel,
            "release-unit": self.release_unit,
            "target": self.target,
        }


type ReleaseExecutionIdentity = (
    OfficialExecutionIdentity | BuddyExecutionIdentity
)


@dataclass(frozen=True, slots=True)
class ReleaseAttemptIdentity:
    """Live Release Execution identity plus current workflow run attempt."""

    execution: ReleaseExecutionIdentity
    workflow_run_id: int
    run_attempt: int

    def __post_init__(self) -> None:
        """Reject malformed live Attempt identity primitives."""
        if type(self.execution) not in {
            OfficialExecutionIdentity,
            BuddyExecutionIdentity,
        }:
            message = "Release Attempt execution has the wrong runtime type"
            raise TypeError(message)
        _positive(self.workflow_run_id, field="attempt.workflow_run_id")
        _positive(self.run_attempt, field="attempt.run_attempt")

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical live Attempt Identity."""
        return {
            "schema": RELEASE_ATTEMPT_IDENTITY_SCHEMA,
            "execution": self.execution.to_document(),
            "workflow-run-id": self.workflow_run_id,
            "run-attempt": self.run_attempt,
        }


@dataclass(frozen=True, slots=True, order=True)
class SimulationIdentity:
    """Request-scoped, separately namespaced simulation pass identity."""

    namespace: str
    request_id: str
    workflow_run_id: int
    run_attempt: int
    identity: str

    def __post_init__(self) -> None:
        """Reject identities outside the simulation namespace."""
        if self.namespace != "release-simulation":
            message = "Simulation Identity namespace is not release-simulation"
            raise ValueError(message)
        if (
            _REQUEST_ID_PATTERN.fullmatch(
                _string(self.request_id, field="simulation.request_id")
            )
            is None
        ):
            message = "Simulation Identity request_id is not canonical"
            raise ValueError(message)
        _positive(self.workflow_run_id, field="simulation.workflow_run_id")
        _positive(self.run_attempt, field="simulation.run_attempt")
        if (
            _SIMULATION_ID_PATTERN.fullmatch(
                _string(self.identity, field="simulation.identity")
            )
            is None
        ):
            message = "Simulation Identity value is not canonical"
            raise ValueError(message)

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical Simulation Identity."""
        return {
            "schema": SIMULATION_IDENTITY_SCHEMA,
            "namespace": self.namespace,
            "request-id": self.request_id,
            "workflow-run-id": self.workflow_run_id,
            "run-attempt": self.run_attempt,
            "identity": self.identity,
        }


@dataclass(frozen=True, slots=True)
class SimulationBinding:
    """Simulation Identity bound to Intent and admitted Repository Model."""

    simulation: SimulationIdentity
    intent_digest: str
    repository_model_digest: str
    purpose: str
    target: str
    channel: str
    release_unit: str
    control: str

    def __post_init__(self) -> None:
        """Reject cross-purpose or malformed simulation bindings."""
        _exact(
            self.simulation,
            SimulationIdentity,
            field="simulation binding.simulation",
        )
        _digest(self.intent_digest, field="simulation binding.intent_digest")
        _digest(
            self.repository_model_digest,
            field="simulation binding.repository_model_digest",
        )
        if self.purpose != "release-simulation":
            message = "Simulation Binding purpose must be release-simulation"
            raise ValueError(message)
        _sha(self.target, field="simulation binding.target")
        _choice(self.channel, _CHANNELS, field="simulation binding.channel")
        _string(self.release_unit, field="simulation binding.release_unit")
        _string(self.control, field="simulation binding.control")

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical Simulation Binding."""
        return {
            "schema": SIMULATION_BINDING_SCHEMA,
            "simulation": self.simulation.to_document(),
            "intent-digest": self.intent_digest,
            "repository-model-digest": self.repository_model_digest,
            "purpose": self.purpose,
            "target": self.target,
            "channel": self.channel,
            "release-unit": self.release_unit,
            "control": self.control,
        }

    @property
    def binding_digest(self) -> str:
        """Return the canonical Simulation Binding digest."""
        return canonical_sha256(self.to_document())


@dataclass(frozen=True, slots=True, order=True)
class ExternalPackageCoordinate:
    """Channel-isolated destination, package, and native version address."""

    channel: str
    destination_id: str
    package_name: str
    native_version: str

    def __post_init__(self) -> None:
        """Reject incomplete external package coordinates."""
        _choice(self.channel, _CHANNELS, field="coordinate.channel")
        _string(self.destination_id, field="coordinate.destination_id")
        _string(self.package_name, field="coordinate.package_name")
        _string(self.native_version, field="coordinate.native_version")

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical External Package Coordinate."""
        return {
            "schema": EXTERNAL_PACKAGE_COORDINATE_SCHEMA,
            "channel": self.channel,
            "destination-id": self.destination_id,
            "package-name": self.package_name,
            "native-version": self.native_version,
        }


@dataclass(frozen=True, slots=True, order=True)
class ReleaseBuildIdentity:
    """One selected Release Unit build identity."""

    release_unit: str
    build_id: str
    definition_id: str
    project_id: str

    def __post_init__(self) -> None:
        """Reject incomplete build identities."""
        _string(self.release_unit, field="build identity.release_unit")
        _string(self.build_id, field="build identity.build_id")
        _string(self.definition_id, field="build identity.definition_id")
        _string(self.project_id, field="build identity.project_id")

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical Release Build Identity."""
        return {
            "schema": RELEASE_BUILD_IDENTITY_SCHEMA,
            "release-unit": self.release_unit,
            "build-id": self.build_id,
            "definition-id": self.definition_id,
            "project-id": self.project_id,
        }


@dataclass(frozen=True, slots=True, order=True)
class ArtifactVariantIdentity:
    """One concrete dimensional variant of a selected build."""

    build: ReleaseBuildIdentity
    variant_id: str
    dimensions: tuple[tuple[str, str], ...]

    def __post_init__(self) -> None:
        """Reject malformed or noncanonical variants."""
        _exact(
            self.build,
            ReleaseBuildIdentity,
            field="variant identity.build",
        )
        _string(self.variant_id, field="variant identity.variant_id")
        _pairs(self.dimensions, field="variant identity.dimensions")

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical Artifact Variant Identity."""
        dimensions: list[JsonValue] = [
            [name, value] for name, value in self.dimensions
        ]
        return {
            "schema": ARTIFACT_VARIANT_IDENTITY_SCHEMA,
            "build": self.build.to_document(),
            "variant-id": self.variant_id,
            "dimensions": dimensions,
        }


@dataclass(frozen=True, slots=True, order=True)
class ReleaseOutputIdentity:
    """One exact logical output of one concrete artifact variant."""

    variant: ArtifactVariantIdentity
    output_id: str
    logical_role: str
    media_kind: str

    def __post_init__(self) -> None:
        """Reject incomplete output identities."""
        _exact(
            self.variant,
            ArtifactVariantIdentity,
            field="output identity.variant",
        )
        _string(self.output_id, field="output identity.output_id")
        _string(self.logical_role, field="output identity.logical_role")
        _string(self.media_kind, field="output identity.media_kind")

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical Release Output Identity."""
        return {
            "schema": RELEASE_OUTPUT_IDENTITY_SCHEMA,
            "variant": self.variant.to_document(),
            "output-id": self.output_id,
            "logical-role": self.logical_role,
            "media-kind": self.media_kind,
        }


def release_artifact_transport_name(  # noqa: PLR0913
    *,
    repository: str,
    purpose: str,
    output: ReleaseOutputIdentity,
    qualification_snapshot_digest: str,
    workflow_run_id: int,
    run_attempt: int,
    producer: str,
) -> str:
    """Return the deterministic current-attempt physical artifact name."""
    repository_value = _string(repository, field="artifact name.repository")
    owner, separator, name = repository_value.partition("/")
    if not separator or not owner or not name or "/" in name:
        message = "artifact name repository must be owner/repository"
        raise ValueError(message)
    purpose_value = _choice(
        purpose,
        _PURPOSES,
        field="artifact name.purpose",
    )
    _exact(output, ReleaseOutputIdentity, field="artifact name.output")
    _digest(
        qualification_snapshot_digest,
        field="artifact name.qualification_snapshot_digest",
    )
    run_id = _positive(workflow_run_id, field="artifact name.workflow_run_id")
    attempt = _positive(run_attempt, field="artifact name.run_attempt")
    producer_value = _string(producer, field="artifact name.producer")
    name_digest = canonical_sha256(
        {
            "schema": "workflow-delivery/v3/release-artifact-transport-name",
            "repository": repository_value,
            "purpose": purpose_value,
            "output": output.to_document(),
            "qualification-snapshot-digest": qualification_snapshot_digest,
            "producer": producer_value,
            "workflow-run-id": run_id,
            "run-attempt": attempt,
        }
    )
    snapshot_digest = qualification_snapshot_digest.removeprefix("sha256:")
    return (
        f"wdv3-{purpose_value}-{output.logical_role}-ra{attempt}-"
        f"{snapshot_digest[:16]}-{name_digest.removeprefix('sha256:')}.tgz"
    )


@dataclass(frozen=True, slots=True)
class ReleaseBuildRequest:
    """Snapshot-frozen exact build Adapter request contract."""

    build: ReleaseBuildIdentity
    variant: ArtifactVariantIdentity
    output: ReleaseOutputIdentity
    repository_model_digest: str
    definition_digest: str
    npm_package_version: str
    witness_digest: str
    declared_inputs: tuple[str, ...]
    adapter_id: str

    def __post_init__(self) -> None:
        """Reject a build request with inconsistent nested identities."""
        _exact(self.build, ReleaseBuildIdentity, field="build request.build")
        _exact(
            self.variant,
            ArtifactVariantIdentity,
            field="build request.variant",
        )
        _exact(
            self.output,
            ReleaseOutputIdentity,
            field="build request.output",
        )
        if (
            self.variant.build != self.build
            or self.output.variant != self.variant
        ):
            message = "Build Request identity chain is inconsistent"
            raise ValueError(message)
        _digest(
            self.repository_model_digest,
            field="build request.repository_model_digest",
        )
        _digest(
            self.definition_digest,
            field="build request.definition_digest",
        )
        _string(
            self.npm_package_version,
            field="build request.npm_package_version",
        )
        _digest(self.witness_digest, field="build request.witness_digest")
        inputs = _string_tuple(
            self.declared_inputs,
            field="build request.declared_inputs",
        )
        if inputs != tuple(sorted(inputs)):
            message = "Build Request declared_inputs must be sorted"
            raise ValueError(message)
        _string(self.adapter_id, field="build request.adapter_id")

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical Build Request contract."""
        return cast(
            "dict[str, JsonValue]",
            {
                "schema": RELEASE_BUILD_REQUEST_SCHEMA,
                "build": self.build.to_document(),
                "variant": self.variant.to_document(),
                "output": self.output.to_document(),
                "repository-model-digest": self.repository_model_digest,
                "definition-digest": self.definition_digest,
                "npm-package-version": self.npm_package_version,
                "witness-digest": self.witness_digest,
                "declared-inputs": list(self.declared_inputs),
                "adapter-id": self.adapter_id,
            },
        )

    @property
    def request_digest(self) -> str:
        """Return the canonical Build Request digest."""
        return canonical_sha256(self.to_document())


@dataclass(frozen=True, slots=True)
class ReleaseObligation:
    """One required definition, target, and dimensional qualification."""

    obligation_id: str
    definition_id: str
    definition_digest: str
    subject_kind: str
    subject_digest: str
    target: str
    dimensions: tuple[tuple[str, str], ...]
    runner: str
    prerequisites: tuple[str, ...]
    required: bool
    request_digest: str
    expected_evidence_id: str

    def __post_init__(self) -> None:
        """Reject advisory, malformed, or noncanonical obligations."""
        _string(self.obligation_id, field="obligation.obligation_id")
        _string(self.definition_id, field="obligation.definition_id")
        _digest(self.definition_digest, field="obligation.definition_digest")
        _string(self.subject_kind, field="obligation.subject_kind")
        _digest(self.subject_digest, field="obligation.subject_digest")
        _sha(self.target, field="obligation.target")
        _pairs(self.dimensions, field="obligation.dimensions")
        _string(self.runner, field="obligation.runner")
        _string_tuple(
            self.prerequisites,
            field="obligation.prerequisites",
        )
        if type(self.required) is not bool or self.required is not True:
            message = "Release obligations must be required"
            raise ValueError(message)
        _digest(self.request_digest, field="obligation.request_digest")
        _string(
            self.expected_evidence_id,
            field="obligation.expected_evidence_id",
        )

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical Release Obligation."""
        dimensions: list[JsonValue] = [
            [name, value] for name, value in self.dimensions
        ]
        return cast(
            "dict[str, JsonValue]",
            {
                "schema": RELEASE_OBLIGATION_SCHEMA,
                "obligation-id": self.obligation_id,
                "definition-id": self.definition_id,
                "definition-digest": self.definition_digest,
                "subject-kind": self.subject_kind,
                "subject-digest": self.subject_digest,
                "target": self.target,
                "dimensions": dimensions,
                "runner": self.runner,
                "prerequisites": list(self.prerequisites),
                "required": self.required,
                "request-digest": self.request_digest,
                "expected-evidence-id": self.expected_evidence_id,
            },
        )


@dataclass(frozen=True, slots=True)
class DestinationProjection:
    """One complete logical desired destination projection."""

    projection_id: str
    destination_id: str
    registry: str
    coordinate: ExternalPackageCoordinate
    output: ReleaseOutputIdentity
    operation: str
    observation_contract_id: str
    potential_action_id: str

    def __post_init__(self) -> None:
        """Reject incomplete or cross-channel destination projections."""
        _string(self.projection_id, field="projection.projection_id")
        _string(self.destination_id, field="projection.destination_id")
        registry = _string(self.registry, field="projection.registry")
        if not registry.startswith("https://"):
            message = "projection.registry must be HTTPS"
            raise ValueError(message)
        _exact(
            self.coordinate,
            ExternalPackageCoordinate,
            field="projection.coordinate",
        )
        _exact(self.output, ReleaseOutputIdentity, field="projection.output")
        _string(self.operation, field="projection.operation")
        _string(
            self.observation_contract_id,
            field="projection.observation_contract_id",
        )
        _string(
            self.potential_action_id,
            field="projection.potential_action_id",
        )
        if self.destination_id != self.coordinate.destination_id:
            message = "Destination Projection coordinate binding mismatch"
            raise ValueError(message)

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical Destination Projection."""
        return {
            "schema": DESTINATION_PROJECTION_SCHEMA,
            "projection-id": self.projection_id,
            "destination-id": self.destination_id,
            "registry": self.registry,
            "coordinate": self.coordinate.to_document(),
            "output": self.output.to_document(),
            "operation": self.operation,
            "observation-contract-id": self.observation_contract_id,
            "potential-action-id": self.potential_action_id,
        }

    @property
    def projection_digest(self) -> str:
        """Return the canonical projection digest."""
        return canonical_sha256(self.to_document())


@dataclass(frozen=True, slots=True)
class PotentialActionContract:
    """Pre-observation operation, capability, and key derivation contract."""

    contract_id: str
    projection_id: str
    operation: str
    output: ReleaseOutputIdentity
    prerequisites: tuple[str, ...]
    capability_requirements: tuple[str, ...]
    mutable_resource_key_basis: tuple[str, ...]

    def __post_init__(self) -> None:
        """Reject incomplete pre-observation action contracts."""
        _string(self.contract_id, field="potential action.contract_id")
        _string(self.projection_id, field="potential action.projection_id")
        _string(self.operation, field="potential action.operation")
        _exact(
            self.output,
            ReleaseOutputIdentity,
            field="potential action.output",
        )
        _string_tuple(
            self.prerequisites,
            field="potential action.prerequisites",
        )
        _string_tuple(
            self.capability_requirements,
            field="potential action.capability_requirements",
        )
        _string_tuple(
            self.mutable_resource_key_basis,
            field="potential action.mutable_resource_key_basis",
        )

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical Potential Action Contract."""
        return cast(
            "dict[str, JsonValue]",
            {
                "schema": POTENTIAL_ACTION_CONTRACT_SCHEMA,
                "contract-id": self.contract_id,
                "projection-id": self.projection_id,
                "operation": self.operation,
                "output": self.output.to_document(),
                "prerequisites": list(self.prerequisites),
                "capability-requirements": list(self.capability_requirements),
                "mutable-resource-key-basis": list(
                    self.mutable_resource_key_basis
                ),
            },
        )


type QualificationSubject = SimulationBinding | ReleaseAttemptIdentity


@dataclass(frozen=True, slots=True)
class QualificationSnapshot:
    """First sealed Release Snapshot for build and qualification only."""

    subject: QualificationSubject
    repository: str
    repository_model_digest: str
    release_policy_digest: str
    target: str
    channel: str
    release_unit: str
    nbgv: NbgvFacts
    builds: tuple[ReleaseBuildIdentity, ...]
    variants: tuple[ArtifactVariantIdentity, ...]
    outputs: tuple[ReleaseOutputIdentity, ...]
    build_requests: tuple[ReleaseBuildRequest, ...]
    destination_projections: tuple[DestinationProjection, ...]
    potential_actions: tuple[PotentialActionContract, ...]
    obligations: tuple[ReleaseObligation, ...]
    expected_evidence_ids: tuple[str, ...]
    ready: bool

    def __post_init__(self) -> None:
        """Reject an open, cyclic, or internally inconsistent Snapshot."""
        if type(self.subject) not in {
            SimulationBinding,
            ReleaseAttemptIdentity,
        }:
            message = "Qualification Snapshot subject has the wrong type"
            raise TypeError(message)
        repository = _string(
            self.repository,
            field="qualification.repository",
        )
        owner, separator, name = repository.partition("/")
        if not separator or not owner or not name or "/" in name:
            message = "qualification.repository must be owner/repository"
            raise ValueError(message)
        _digest(
            self.repository_model_digest,
            field="qualification.repository_model_digest",
        )
        _digest(
            self.release_policy_digest,
            field="qualification.release_policy_digest",
        )
        _sha(self.target, field="qualification.target")
        _choice(self.channel, _CHANNELS, field="qualification.channel")
        _string(self.release_unit, field="qualification.release_unit")
        validate_nbgv_facts(self.nbgv, target=self.target)
        _exact(self.builds, tuple, field="qualification.builds")
        _exact(self.variants, tuple, field="qualification.variants")
        _exact(self.outputs, tuple, field="qualification.outputs")
        _exact(
            self.build_requests,
            tuple,
            field="qualification.build_requests",
        )
        _exact(
            self.destination_projections,
            tuple,
            field="qualification.destination_projections",
        )
        _exact(
            self.potential_actions,
            tuple,
            field="qualification.potential_actions",
        )
        _exact(self.obligations, tuple, field="qualification.obligations")
        _string_tuple(
            self.expected_evidence_ids,
            field="qualification.expected_evidence_ids",
        )
        if type(self.ready) is not bool or self.ready is not True:
            message = "Qualification Snapshot must be a ready closed Plan"
            raise ValueError(message)
        self._validate_identity_closure()
        self._validate_obligation_dag()

    def _validate_identity_closure(self) -> None:  # noqa: C901
        if len(set(self.builds)) != len(self.builds):
            message = "Qualification Snapshot contains duplicate builds"
            raise ValueError(message)
        if len(set(self.variants)) != len(self.variants):
            message = "Qualification Snapshot contains duplicate variants"
            raise ValueError(message)
        if len(set(self.outputs)) != len(self.outputs):
            message = "Qualification Snapshot contains duplicate outputs"
            raise ValueError(message)
        if any(variant.build not in self.builds for variant in self.variants):
            message = "Qualification Snapshot variant build is not planned"
            raise ValueError(message)
        if any(output.variant not in self.variants for output in self.outputs):
            message = "Qualification Snapshot output variant is not planned"
            raise ValueError(message)
        if any(
            request.build not in self.builds
            or request.variant not in self.variants
            or request.output not in self.outputs
            or request.repository_model_digest != self.repository_model_digest
            for request in self.build_requests
        ):
            message = "Qualification Snapshot Build Request is not closed"
            raise ValueError(message)
        projection_ids = {
            projection.projection_id
            for projection in self.destination_projections
        }
        if len(projection_ids) != len(self.destination_projections):
            message = "Qualification Snapshot contains duplicate projections"
            raise ValueError(message)
        if any(
            projection.output not in self.outputs
            for projection in self.destination_projections
        ):
            message = "Qualification Snapshot projection output is not planned"
            raise ValueError(message)
        action_ids = {action.contract_id for action in self.potential_actions}
        if len(action_ids) != len(self.potential_actions):
            message = (
                "Qualification Snapshot contains duplicate potential actions"
            )
            raise ValueError(message)
        if any(
            action.projection_id not in projection_ids
            or action.output not in self.outputs
            for action in self.potential_actions
        ):
            message = "Qualification Snapshot potential action is not closed"
            raise ValueError(message)
        if isinstance(self.subject, SimulationBinding) and (
            self.subject.repository_model_digest != self.repository_model_digest
            or self.subject.target != self.target
            or self.subject.channel != self.channel
            or self.subject.release_unit != self.release_unit
        ):
            message = (
                "Qualification Snapshot Simulation Binding is inconsistent"
            )
            raise ValueError(message)

    def _validate_obligation_dag(self) -> None:
        by_id = {
            obligation.obligation_id: obligation
            for obligation in self.obligations
        }
        if len(by_id) != len(self.obligations):
            message = "Qualification Snapshot has duplicate obligations"
            raise ValueError(message)
        evidence_ids = tuple(
            obligation.expected_evidence_id for obligation in self.obligations
        )
        if evidence_ids != self.expected_evidence_ids:
            message = "Qualification Snapshot expected Evidence set mismatch"
            raise ValueError(message)
        for obligation in self.obligations:
            if any(
                prerequisite not in by_id
                for prerequisite in obligation.prerequisites
            ):
                message = "Qualification Snapshot has unknown prerequisite"
                raise ValueError(message)
        visited: set[str] = set()
        active: set[str] = set()

        def visit(obligation_id: str) -> None:
            if obligation_id in active:
                message = "Qualification Snapshot obligation DAG has a cycle"
                raise ValueError(message)
            if obligation_id in visited:
                return
            active.add(obligation_id)
            for prerequisite in by_id[obligation_id].prerequisites:
                visit(prerequisite)
            active.remove(obligation_id)
            visited.add(obligation_id)

        for obligation_id in by_id:
            visit(obligation_id)

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical Qualification Snapshot."""
        return cast(
            "dict[str, JsonValue]",
            {
                "schema": QUALIFICATION_SNAPSHOT_SCHEMA,
                "subject": self.subject.to_document(),
                "repository": self.repository,
                "repository-model-digest": self.repository_model_digest,
                "release-policy-digest": self.release_policy_digest,
                "target": self.target,
                "channel": self.channel,
                "release-unit": self.release_unit,
                "nbgv": self.nbgv.to_document(),
                "builds": [build.to_document() for build in self.builds],
                "variants": [
                    variant.to_document() for variant in self.variants
                ],
                "outputs": [output.to_document() for output in self.outputs],
                "build-requests": [
                    request.to_document() for request in self.build_requests
                ],
                "destination-projections": [
                    projection.to_document()
                    for projection in self.destination_projections
                ],
                "potential-actions": [
                    action.to_document() for action in self.potential_actions
                ],
                "obligations": [
                    obligation.to_document() for obligation in self.obligations
                ],
                "expected-evidence-ids": list(self.expected_evidence_ids),
                "ready": self.ready,
            },
        )

    @property
    def snapshot_digest(self) -> str:
        """Return the canonical Qualification Snapshot digest."""
        return canonical_sha256(self.to_document())


@dataclass(frozen=True, slots=True)
class ReleaseArtifact:
    """Exact publishable bytes, transport, manifest, and provenance."""

    subject: SimulationIdentity | ReleaseAttemptIdentity
    repository: str
    qualification_snapshot_digest: str
    repository_model_digest: str
    target: str
    purpose: str
    output: ReleaseOutputIdentity
    build_request_digest: str
    transport: ArtifactTransportIdentity
    content: ArtifactContentIdentity
    entries: tuple[str, ...]
    lifecycle_scripts: tuple[tuple[str, str], ...]
    witness_digest: str
    source_input_manifest: tuple[tuple[str, str], ...]
    toolchain: tuple[tuple[str, str], ...]
    provenance_digest: str

    def __post_init__(self) -> None:  # noqa: C901, PLR0915
        """Reject substituted bytes, transport, or provenance bindings."""
        if type(self.subject) not in {
            SimulationIdentity,
            ReleaseAttemptIdentity,
        }:
            message = "Release Artifact subject has the wrong runtime type"
            raise TypeError(message)
        repository = _string(self.repository, field="artifact.repository")
        owner, separator, name = repository.partition("/")
        if not separator or not owner or not name or "/" in name:
            message = "Release Artifact repository must be owner/repository"
            raise ValueError(message)
        _digest(
            self.qualification_snapshot_digest,
            field="artifact.qualification_snapshot_digest",
        )
        _digest(
            self.repository_model_digest,
            field="artifact.repository_model_digest",
        )
        _sha(self.target, field="artifact.target")
        _choice(self.purpose, _PURPOSES, field="artifact.purpose")
        _exact(self.output, ReleaseOutputIdentity, field="artifact.output")
        _digest(
            self.build_request_digest,
            field="artifact.build_request_digest",
        )
        _exact(
            self.transport,
            ArtifactTransportIdentity,
            field="artifact.transport",
        )
        _exact(
            self.content,
            ArtifactContentIdentity,
            field="artifact.content",
        )
        if (
            self.content.output_id != self.output.output_id
            or self.content.logical_role != self.output.logical_role
            or self.content.media_kind != self.output.media_kind
            or self.content.content_sha512 is None
        ):
            message = "Release Artifact content/output binding mismatch"
            raise ValueError(message)
        _string_tuple(
            self.entries,
            field="artifact.entries",
            sorted_values=True,
        )
        _pairs(
            self.lifecycle_scripts,
            field="artifact.lifecycle_scripts",
        )
        _digest(self.witness_digest, field="artifact.witness_digest")
        manifest = _pairs(
            self.source_input_manifest,
            field="artifact.source_input_manifest",
        )
        for _, digest in manifest:
            _digest(digest, field="artifact.source_input_manifest.digest")
        _pairs(self.toolchain, field="artifact.toolchain", sorted_values=False)
        _digest(
            self.provenance_digest,
            field="artifact.provenance_digest",
        )
        workflow_run_id, run_attempt = _subject_run(self.subject)
        if (
            self.transport.workflow_run_id != workflow_run_id
            or self.transport.run_attempt != run_attempt
        ):
            message = "Release Artifact transport is from another run attempt"
            raise ValueError(message)
        if self.transport.producer != "build-tarball":
            message = "Release Artifact transport producer is not exact"
            raise ValueError(message)
        expected_url = (
            f"https://github.com/{self.repository}/actions/runs/"
            f"{workflow_run_id}/artifacts/{self.transport.artifact_id}"
        )
        if self.transport.artifact_url != expected_url:
            message = "Release Artifact transport URL is not exact"
            raise ValueError(message)
        expected_name = release_artifact_transport_name(
            repository=self.repository,
            purpose=self.purpose,
            output=self.output,
            qualification_snapshot_digest=(self.qualification_snapshot_digest),
            workflow_run_id=workflow_run_id,
            run_attempt=run_attempt,
            producer=self.transport.producer,
        )
        if self.transport.artifact_name != expected_name:
            message = "Release Artifact transport name is not exact"
            raise ValueError(message)
        if isinstance(self.subject, SimulationIdentity):
            if self.purpose != "release-simulation":
                message = "Simulation Artifact has a cross-purpose binding"
                raise ValueError(message)
        elif self.purpose != "live-release":
            message = "Live Release Artifact has a cross-purpose binding"
            raise ValueError(message)
        expected_provenance = canonical_sha256(self.provenance_document())
        if self.provenance_digest != expected_provenance:
            message = "Release Artifact provenance digest mismatch"
            raise ValueError(message)

    def provenance_document(self) -> dict[str, JsonValue]:
        """Return the exact internal artifact provenance basis."""
        return {
            "schema": "workflow-delivery/v3/release-artifact-provenance",
            "subject": _subject_document(self.subject),
            "repository": self.repository,
            "qualification-snapshot-digest": (
                self.qualification_snapshot_digest
            ),
            "repository-model-digest": self.repository_model_digest,
            "target": self.target,
            "purpose": self.purpose,
            "output": self.output.to_document(),
            "build-request-digest": self.build_request_digest,
            "transport": self.transport.to_document(),
            "content": self.content.to_document(),
            "witness-digest": self.witness_digest,
            "source-input-manifest": [
                [path, digest] for path, digest in self.source_input_manifest
            ],
            "toolchain": [[name, version] for name, version in self.toolchain],
        }

    def to_document(self) -> dict[str, JsonValue]:
        """Return the complete canonical Release Artifact."""
        document = self.provenance_document()
        document["schema"] = RELEASE_ARTIFACT_SCHEMA
        document.update(
            cast(
                "dict[str, JsonValue]",
                {
                    "entries": list(self.entries),
                    "lifecycle-scripts": [
                        [name, command]
                        for name, command in self.lifecycle_scripts
                    ],
                    "provenance-digest": self.provenance_digest,
                },
            )
        )
        return document

    @property
    def artifact_digest(self) -> str:
        """Return the canonical Release Artifact record digest."""
        return canonical_sha256(self.to_document())


@dataclass(frozen=True, slots=True)
class QualificationEvidence:
    """Mechanical result for one exact current Release obligation."""

    evidence_id: str
    subject: SimulationIdentity | ReleaseAttemptIdentity
    qualification_snapshot_digest: str
    obligation: ReleaseObligation
    producer: str
    workflow_run_id: int
    run_attempt: int
    raw_result: str
    normalized_outcome: str
    artifact_digests: tuple[str, ...]
    result_facts: tuple[tuple[str, str], ...]
    diagnostics: tuple[str, ...]

    def __post_init__(self) -> None:
        """Reject Evidence outside closed current-attempt mechanics."""
        _string(self.evidence_id, field="evidence.evidence_id")
        if type(self.subject) not in {
            SimulationIdentity,
            ReleaseAttemptIdentity,
        }:
            message = "Qualification Evidence subject has the wrong type"
            raise TypeError(message)
        _digest(
            self.qualification_snapshot_digest,
            field="evidence.qualification_snapshot_digest",
        )
        _exact(
            self.obligation,
            ReleaseObligation,
            field="evidence.obligation",
        )
        _string(self.producer, field="evidence.producer")
        _positive(self.workflow_run_id, field="evidence.workflow_run_id")
        _positive(self.run_attempt, field="evidence.run_attempt")
        _string(self.raw_result, field="evidence.raw_result")
        _choice(
            self.normalized_outcome,
            _QUALIFICATION_OUTCOMES,
            field="evidence.normalized_outcome",
        )
        for index, digest in enumerate(
            _string_tuple(
                self.artifact_digests,
                field="evidence.artifact_digests",
            )
        ):
            _digest(digest, field=f"evidence.artifact_digests[{index}]")
        _pairs(self.result_facts, field="evidence.result_facts")
        _string_tuple(
            self.diagnostics,
            field="evidence.diagnostics",
            unique=False,
        )
        subject_run_id, subject_attempt = _subject_run(self.subject)
        if (
            self.workflow_run_id != subject_run_id
            or self.run_attempt != subject_attempt
        ):
            message = "Qualification Evidence is from another run attempt"
            raise ValueError(message)

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical Qualification Evidence."""
        return cast(
            "dict[str, JsonValue]",
            {
                "schema": QUALIFICATION_EVIDENCE_SCHEMA,
                "evidence-id": self.evidence_id,
                "subject": _subject_document(self.subject),
                "qualification-snapshot-digest": (
                    self.qualification_snapshot_digest
                ),
                "obligation": self.obligation.to_document(),
                "producer": self.producer,
                "workflow-run-id": self.workflow_run_id,
                "run-attempt": self.run_attempt,
                "raw-result": self.raw_result,
                "normalized-outcome": self.normalized_outcome,
                "artifact-digests": list(self.artifact_digests),
                "result-facts": [
                    [name, value] for name, value in self.result_facts
                ],
                "diagnostics": list(self.diagnostics),
            },
        )

    @property
    def evidence_digest(self) -> str:
        """Return the canonical Evidence digest."""
        return canonical_sha256(self.to_document())


@dataclass(frozen=True, slots=True)
class ObligationDisposition:
    """Final closed disposition for one planned Release obligation."""

    obligation: ReleaseObligation
    outcome: str
    evidence_digests: tuple[str, ...]
    explanation: str

    def __post_init__(self) -> None:
        """Reject an invalid final disposition."""
        _exact(
            self.obligation,
            ReleaseObligation,
            field="disposition.obligation",
        )
        _choice(
            self.outcome,
            _QUALIFICATION_OUTCOMES,
            field="disposition.outcome",
        )
        for index, digest in enumerate(
            _string_tuple(
                self.evidence_digests,
                field="disposition.evidence_digests",
            )
        ):
            _digest(digest, field=f"disposition.evidence_digests[{index}]")
        _string(self.explanation, field="disposition.explanation")

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical obligation disposition."""
        return cast(
            "dict[str, JsonValue]",
            {
                "schema": OBLIGATION_DISPOSITION_SCHEMA,
                "obligation": self.obligation.to_document(),
                "outcome": self.outcome,
                "evidence-digests": list(self.evidence_digests),
                "explanation": self.explanation,
            },
        )


@dataclass(frozen=True, slots=True)
class QualificationDecision:
    """Final Decision admitting exact Evidence for every obligation position."""

    subject: SimulationIdentity | ReleaseAttemptIdentity
    qualification_snapshot_digest: str
    obligation_dispositions: tuple[ObligationDisposition, ...]
    admitted_evidence_digests: tuple[str, ...]
    admitted_artifact_digests: tuple[str, ...]
    terminal_result: str
    failure_class: str
    next_action: str

    def __post_init__(self) -> None:
        """Reject inconsistent Decision closure."""
        if type(self.subject) not in {
            SimulationIdentity,
            ReleaseAttemptIdentity,
        }:
            message = "Qualification Decision subject has the wrong type"
            raise TypeError(message)
        _digest(
            self.qualification_snapshot_digest,
            field="decision.qualification_snapshot_digest",
        )
        _exact(
            self.obligation_dispositions,
            tuple,
            field="decision.obligation_dispositions",
        )
        if any(
            type(disposition) is not ObligationDisposition
            for disposition in self.obligation_dispositions
        ):
            message = "Qualification Decision disposition has wrong type"
            raise TypeError(message)
        for field, values in (
            ("admitted_evidence_digests", self.admitted_evidence_digests),
            ("admitted_artifact_digests", self.admitted_artifact_digests),
        ):
            for index, digest in enumerate(
                _string_tuple(values, field=f"decision.{field}")
            ):
                _digest(digest, field=f"decision.{field}[{index}]")
        _choice(
            self.terminal_result,
            _QUALIFICATION_RESULTS,
            field="decision.terminal_result",
        )
        _string(self.failure_class, field="decision.failure_class")
        _string(self.next_action, field="decision.next_action")

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical Qualification Decision."""
        return cast(
            "dict[str, JsonValue]",
            {
                "schema": QUALIFICATION_DECISION_SCHEMA,
                "subject": _subject_document(self.subject),
                "qualification-snapshot-digest": (
                    self.qualification_snapshot_digest
                ),
                "obligation-dispositions": [
                    disposition.to_document()
                    for disposition in self.obligation_dispositions
                ],
                "admitted-evidence-digests": list(
                    self.admitted_evidence_digests
                ),
                "admitted-artifact-digests": list(
                    self.admitted_artifact_digests
                ),
                "terminal-result": self.terminal_result,
                "failure-class": self.failure_class,
                "next-action": self.next_action,
            },
        )

    @property
    def decision_digest(self) -> str:
        """Return the canonical Qualification Decision digest."""
        return canonical_sha256(self.to_document())


@dataclass(frozen=True, slots=True)
class ObservationValue:
    """Adapter-owned classified observation value without interpretation."""

    classification: str
    owner: str | None
    coordinate: ExternalPackageCoordinate | None
    content_sha512: str | None
    witness_digest: str | None
    routing: tuple[tuple[str, str], ...]

    def __post_init__(self) -> None:
        """Reject malformed classified observation facts."""
        _choice(
            self.classification,
            _OBSERVATION_CLASSIFICATIONS,
            field="observation value.classification",
        )
        if self.owner is not None:
            _string(self.owner, field="observation value.owner")
        if self.coordinate is not None:
            _exact(
                self.coordinate,
                ExternalPackageCoordinate,
                field="observation value.coordinate",
            )
        if self.content_sha512 is not None:
            _digest(
                self.content_sha512,
                field="observation value.content_sha512",
                sha512=True,
            )
        if self.witness_digest is not None:
            _digest(
                self.witness_digest,
                field="observation value.witness_digest",
            )
        _pairs(self.routing, field="observation value.routing")
        present = (
            self.owner,
            self.coordinate,
            self.content_sha512,
            self.witness_digest,
        )
        if self.classification == "absent" and (
            any(item is not None for item in present) or self.routing
        ):
            message = "absent Observation Value cannot claim remote state"
            raise ValueError(message)
        if self.classification == "exact-satisfied" and any(
            item is None for item in present
        ):
            message = "exact Observation Value requires complete remote facts"
            raise ValueError(message)

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical classified observation value."""
        coordinate: JsonValue = (
            None if self.coordinate is None else self.coordinate.to_document()
        )
        return {
            "schema": OBSERVATION_VALUE_SCHEMA,
            "classification": self.classification,
            "owner": self.owner,
            "coordinate": coordinate,
            "content-sha512": self.content_sha512,
            "witness-digest": self.witness_digest,
            "routing": [[name, value] for name, value in self.routing],
        }


@dataclass(frozen=True, slots=True)
class ObservationRequestFacts:
    """Closed canonical facts for the Adapter's credential-free request."""

    qualification_snapshot_digest: str
    projection_digest: str
    desired_state_digest: str
    method: str
    url: str
    headers: tuple[tuple[str, str], ...]

    def __post_init__(self) -> None:
        """Reject malformed or open request facts."""
        _digest(
            self.qualification_snapshot_digest,
            field="observation request.qualification_snapshot_digest",
        )
        _digest(
            self.projection_digest,
            field="observation request.projection_digest",
        )
        _digest(
            self.desired_state_digest,
            field="observation request.desired_state_digest",
        )
        if _string(self.method, field="observation request.method") != "GET":
            message = "observation request method must be GET"
            raise ValueError(message)
        _string(self.url, field="observation request.url")
        _pairs(self.headers, field="observation request.headers")

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical request facts."""
        return {
            "schema": OBSERVATION_REQUEST_FACTS_SCHEMA,
            "qualification-snapshot-digest": (
                self.qualification_snapshot_digest
            ),
            "projection-digest": self.projection_digest,
            "desired-state-digest": self.desired_state_digest,
            "method": self.method,
            "url": self.url,
            "headers": [[name, value] for name, value in self.headers],
        }

    @property
    def request_digest(self) -> str:
        """Return the digest derived from the retained request facts."""
        return canonical_sha256(self.to_document())


@dataclass(frozen=True, slots=True)
class ObservationResponseFacts:
    """Closed bounded facts retained from the remote response."""

    stage: str
    requested_url: str
    final_url: str | None
    redirects: tuple[str, ...]
    status: int | str
    selected_headers: tuple[tuple[str, str], ...]
    truncated: bool | None
    body_sha256: str | None
    status_detail: str | None = None
    metadata_body_sha256: str | None = None
    metadata_package: str | None = None
    metadata_version: str | None = None
    dist_tarball: str | None = None
    dist_integrity: str | None = None
    tarball_content_sha512: str | None = None
    tarball_byte_size: int | None = None
    remote_witness_digest: str | None = None

    def __post_init__(self) -> None:  # noqa: C901
        """Reject malformed, unbounded, or loosely typed response facts."""
        _choice(
            self.stage,
            frozenset({"metadata", "tarball", "synthetic"}),
            field="observation response.stage",
        )
        _string(
            self.requested_url,
            field="observation response.requested_url",
        )
        if self.final_url is not None:
            _string(self.final_url, field="observation response.final_url")
        _string_tuple(
            self.redirects,
            field="observation response.redirects",
            unique=False,
        )
        if type(self.status) is int:
            _nonnegative(self.status, field="observation response.status")
        elif type(self.status) is str:
            _string(self.status, field="observation response.status")
        else:
            message = "observation response.status has the wrong runtime type"
            raise TypeError(message)
        _pairs(
            self.selected_headers,
            field="observation response.selected_headers",
        )
        if self.truncated is not None:
            _exact(
                self.truncated,
                bool,
                field="observation response.truncated",
            )
        for field, value in (
            ("status_detail", self.status_detail),
            ("metadata_package", self.metadata_package),
            ("metadata_version", self.metadata_version),
            ("dist_tarball", self.dist_tarball),
            ("dist_integrity", self.dist_integrity),
        ):
            if value is not None:
                _string(value, field=f"observation response.{field}")
        for field, value in (
            ("body_sha256", self.body_sha256),
            ("metadata_body_sha256", self.metadata_body_sha256),
            ("remote_witness_digest", self.remote_witness_digest),
        ):
            if value is not None:
                _digest(value, field=f"observation response.{field}")
        if self.tarball_content_sha512 is not None:
            _digest(
                self.tarball_content_sha512,
                field="observation response.tarball_content_sha512",
                sha512=True,
            )
        if self.tarball_byte_size is not None:
            _nonnegative(
                self.tarball_byte_size,
                field="observation response.tarball_byte_size",
            )

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical bounded response facts."""
        document: dict[str, JsonValue] = {}
        document["schema"] = OBSERVATION_RESPONSE_FACTS_SCHEMA
        document["stage"] = self.stage
        document["requested-url"] = self.requested_url
        document["final-url"] = self.final_url
        redirects: list[JsonValue] = []
        redirects.extend(self.redirects)
        document["redirects"] = redirects
        document["status"] = self.status
        selected_headers: list[JsonValue] = []
        for name, value in self.selected_headers:
            pair: list[JsonValue] = [name, value]
            selected_headers.append(pair)
        document["selected-headers"] = selected_headers
        document["truncated"] = self.truncated
        document["body-sha256"] = self.body_sha256
        document["status-detail"] = self.status_detail
        document["metadata-body-sha256"] = self.metadata_body_sha256
        document["metadata-package"] = self.metadata_package
        document["metadata-version"] = self.metadata_version
        document["dist-tarball"] = self.dist_tarball
        document["dist-integrity"] = self.dist_integrity
        document["tarball-content-sha512"] = self.tarball_content_sha512
        document["tarball-byte-size"] = self.tarball_byte_size
        document["remote-witness-digest"] = self.remote_witness_digest
        return document


@dataclass(frozen=True, slots=True)
class ProjectionObservation:
    """One admitted Adapter classification for one logical projection."""

    subject: SimulationIdentity | ReleaseAttemptIdentity
    purpose: str
    target: str
    producer: str
    qualification_snapshot_digest: str
    projection: DestinationProjection
    desired_state_digest: str
    observation_contract_id: str
    request_facts: ObservationRequestFacts
    request_digest: str
    response_facts: ObservationResponseFacts
    response_digest: str
    value: ObservationValue

    def __post_init__(self) -> None:
        """Reject substituted projection or request/response bindings."""
        if type(self.subject) not in {
            SimulationIdentity,
            ReleaseAttemptIdentity,
        }:
            message = "Projection Observation subject has wrong type"
            raise TypeError(message)
        purpose = _choice(
            self.purpose,
            _PURPOSES,
            field="observation.purpose",
        )
        expected_purpose = (
            "release-simulation"
            if type(self.subject) is SimulationIdentity
            else "live-release"
        )
        if purpose != expected_purpose:
            message = "Projection Observation purpose binding mismatch"
            raise ValueError(message)
        _sha(self.target, field="observation.target")
        _string(self.producer, field="observation.producer")
        _digest(
            self.qualification_snapshot_digest,
            field="observation.qualification_snapshot_digest",
        )
        _exact(
            self.projection,
            DestinationProjection,
            field="observation.projection",
        )
        _digest(
            self.desired_state_digest,
            field="observation.desired_state_digest",
        )
        _string(
            self.observation_contract_id,
            field="observation.observation_contract_id",
        )
        _exact(
            self.request_facts,
            ObservationRequestFacts,
            field="observation.request_facts",
        )
        _digest(self.request_digest, field="observation.request_digest")
        _exact(
            self.response_facts,
            ObservationResponseFacts,
            field="observation.response_facts",
        )
        _digest(self.response_digest, field="observation.response_digest")
        _exact(self.value, ObservationValue, field="observation.value")
        if (
            self.observation_contract_id
            != self.projection.observation_contract_id
        ):
            message = "Projection Observation contract binding mismatch"
            raise ValueError(message)
        if (
            self.request_facts.qualification_snapshot_digest
            != self.qualification_snapshot_digest
            or self.request_facts.projection_digest
            != self.projection.projection_digest
            or self.request_facts.desired_state_digest
            != self.desired_state_digest
        ):
            message = "Projection Observation request facts binding mismatch"
            raise ValueError(message)
        if self.request_digest != self.request_facts.request_digest:
            message = "Projection Observation request digest mismatch"
            raise ValueError(message)
        expected_response_digest = canonical_sha256(
            {
                "schema": "workflow-delivery/v3/observation-response",
                "request-digest": self.request_digest,
                "facts": self.response_facts.to_document(),
                "value": self.value.to_document(),
            }
        )
        if self.response_digest != expected_response_digest:
            message = "Projection Observation response digest mismatch"
            raise ValueError(message)

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical Projection Observation."""
        return {
            "schema": PROJECTION_OBSERVATION_SCHEMA,
            "subject": _subject_document(self.subject),
            "purpose": self.purpose,
            "target": self.target,
            "producer": self.producer,
            "qualification-snapshot-digest": (
                self.qualification_snapshot_digest
            ),
            "projection": self.projection.to_document(),
            "desired-state-digest": self.desired_state_digest,
            "observation-contract-id": self.observation_contract_id,
            "request-facts": self.request_facts.to_document(),
            "request-digest": self.request_digest,
            "response-facts": self.response_facts.to_document(),
            "response-digest": self.response_digest,
            "value": self.value.to_document(),
        }

    @property
    def observation_digest(self) -> str:
        """Return the canonical Observation digest."""
        return canonical_sha256(self.to_document())


@dataclass(frozen=True, slots=True)
class HypotheticalAction:
    """Purpose-discriminated action report with no live authority."""

    simulation: SimulationIdentity
    qualification_snapshot_digest: str
    qualification_decision_digest: str
    projection_id: str
    potential_action: PotentialActionContract
    artifact_digest: str
    mutable_resource_keys: tuple[str, ...]
    capability_requirements: tuple[str, ...]

    def __post_init__(self) -> None:
        """Reject live identity or incomplete hypothetical action bindings."""
        _exact(
            self.simulation,
            SimulationIdentity,
            field="hypothetical action.simulation",
        )
        _digest(
            self.qualification_snapshot_digest,
            field="hypothetical action.qualification_snapshot_digest",
        )
        _digest(
            self.qualification_decision_digest,
            field="hypothetical action.qualification_decision_digest",
        )
        _string(
            self.projection_id,
            field="hypothetical action.projection_id",
        )
        _exact(
            self.potential_action,
            PotentialActionContract,
            field="hypothetical action.potential_action",
        )
        _digest(
            self.artifact_digest,
            field="hypothetical action.artifact_digest",
        )
        _string_tuple(
            self.mutable_resource_keys,
            field="hypothetical action.mutable_resource_keys",
        )
        _string_tuple(
            self.capability_requirements,
            field="hypothetical action.capability_requirements",
        )
        if (
            self.projection_id != self.potential_action.projection_id
            or self.capability_requirements
            != self.potential_action.capability_requirements
        ):
            message = "Hypothetical Action contract binding mismatch"
            raise ValueError(message)

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical Hypothetical Action."""
        return cast(
            "dict[str, JsonValue]",
            {
                "schema": HYPOTHETICAL_ACTION_SCHEMA,
                "simulation": self.simulation.to_document(),
                "qualification-snapshot-digest": (
                    self.qualification_snapshot_digest
                ),
                "qualification-decision-digest": (
                    self.qualification_decision_digest
                ),
                "projection-id": self.projection_id,
                "potential-action": self.potential_action.to_document(),
                "artifact-digest": self.artifact_digest,
                "mutable-resource-keys": list(self.mutable_resource_keys),
                "capability-requirements": list(self.capability_requirements),
            },
        )


def publication_action_inputs(
    projection: DestinationProjection,
    artifact: ReleaseArtifact,
) -> tuple[tuple[str, str], ...]:
    """Return the exact ordered concrete Publication Action inputs."""
    _exact(
        projection,
        DestinationProjection,
        field="publication action inputs.projection",
    )
    _exact(
        artifact,
        ReleaseArtifact,
        field="publication action inputs.artifact",
    )
    content_sha512 = artifact.content.content_sha512
    if content_sha512 is None:
        message = "Publication Action artifact requires SHA-512"
        raise ValueError(message)
    return (
        ("artifact-content-sha256", artifact.content.content_sha256),
        ("artifact-content-sha512", content_sha512),
        ("artifact-digest", artifact.artifact_digest),
        ("coordinate", canonical_sha256(projection.coordinate.to_document())),
        ("operation", projection.operation),
        ("output-id", artifact.output.output_id),
        ("projection-digest", projection.projection_digest),
        ("transport-artifact-id", str(artifact.transport.artifact_id)),
        ("witness-digest", artifact.witness_digest),
    )


def publication_mutable_resource_keys(
    projection: DestinationProjection,
) -> tuple[str, ...]:
    """Return exact complete mutable keys for supported commit-6 actions."""
    _exact(
        projection,
        DestinationProjection,
        field="publication mutable keys.projection",
    )
    if projection.operation != "npm-publish-create-only":
        message = "Publication Action operation is unsupported in commit 6"
        raise ValueError(message)
    coordinate_digest = canonical_sha256(projection.coordinate.to_document())
    key_suffix = coordinate_digest.removeprefix("sha256:")
    return (f"external-package-coordinate:{key_suffix}",)


def publication_lock_projection(projection: DestinationProjection) -> str:
    """Return the exact conservative destination/package lock projection."""
    _exact(
        projection,
        DestinationProjection,
        field="publication lock projection.projection",
    )
    digest = canonical_sha256(
        {
            "schema": "workflow-delivery/v3/conservative-lock-projection",
            "destination-id": projection.destination_id,
            "package-name": projection.coordinate.package_name,
        }
    )
    return f"destination-package:{digest.removeprefix('sha256:')}"


def publication_lock_group(projection: DestinationProjection) -> str:
    """Return the exact conservative platform serialization group."""
    return publication_lock_projection(projection)


def publication_capability_group(projection: DestinationProjection) -> str:
    """Return the exact supported capability group identity."""
    _exact(
        projection,
        DestinationProjection,
        field="publication capability group.projection",
    )
    return f"capability-group:{projection.destination_id}:package-publication"


def publication_capability_requirements(
    projection: DestinationProjection,
) -> tuple[str, ...]:
    """Return exact requirements for supported commit-6 materialization."""
    _exact(
        projection,
        DestinationProjection,
        field="publication capability requirements.projection",
    )
    if (
        projection.coordinate.channel == "official"
        and projection.destination_id == "npm/npmjs-public-v1"
        and projection.registry == "https://registry.npmjs.org"
        and projection.operation == "npm-publish-create-only"
    ):
        return ("npmjs/trusted-publishing-oidc-v1",)
    message = "Publication Action capability is unsupported in commit 6"
    raise ValueError(message)


def publication_expected_result(projection: DestinationProjection) -> str:
    """Return the exact expected result contract for a supported action."""
    _exact(
        projection,
        DestinationProjection,
        field="publication expected result.projection",
    )
    if projection.operation == "npm-publish-create-only":
        return "created-or-exact"
    message = "Publication Action expected result is unsupported in commit 6"
    raise ValueError(message)


def publication_receipt_contract(projection: DestinationProjection) -> str:
    """Return the exact Receipt contract for a supported action."""
    _exact(
        projection,
        DestinationProjection,
        field="publication receipt contract.projection",
    )
    if projection.operation == "npm-publish-create-only":
        return "npm/package-publication-receipt-v1"
    message = "Publication Action Receipt is unsupported in commit 6"
    raise ValueError(message)


@dataclass(frozen=True, slots=True)
class PublicationAction:
    """Live action with exact artifact, key, and Receipt bindings."""

    action_id: str
    projection: DestinationProjection
    operation: str
    artifact: ReleaseArtifact
    artifact_digest: str
    artifact_output: ReleaseOutputIdentity
    prerequisites: tuple[str, ...]
    action_inputs: tuple[tuple[str, str], ...]
    mutable_resource_keys: tuple[str, ...]
    lock_projection: str
    lock_group: str
    capability_group: str
    capability_requirements: tuple[str, ...]
    expected_result: str
    receipt_contract: str

    def __post_init__(self) -> None:  # noqa: C901, PLR0912, PLR0915
        """Reject incomplete or substituted live action bindings."""
        _string(self.action_id, field="publication action.action_id")
        _exact(
            self.projection,
            DestinationProjection,
            field="publication action.projection",
        )
        _string(self.operation, field="publication action.operation")
        _exact(
            self.artifact,
            ReleaseArtifact,
            field="publication action.artifact",
        )
        _digest(
            self.artifact_digest,
            field="publication action.artifact_digest",
        )
        _exact(
            self.artifact_output,
            ReleaseOutputIdentity,
            field="publication action.artifact_output",
        )
        _string_tuple(
            self.prerequisites,
            field="publication action.prerequisites",
        )
        _pairs(self.action_inputs, field="publication action.action_inputs")
        _string_tuple(
            self.mutable_resource_keys,
            field="publication action.mutable_resource_keys",
        )
        _string(
            self.lock_projection,
            field="publication action.lock_projection",
        )
        _string(self.lock_group, field="publication action.lock_group")
        _string(
            self.capability_group,
            field="publication action.capability_group",
        )
        _string_tuple(
            self.capability_requirements,
            field="publication action.capability_requirements",
        )
        _string(
            self.expected_result,
            field="publication action.expected_result",
        )
        _string(
            self.receipt_contract,
            field="publication action.receipt_contract",
        )
        if self.projection.potential_action_id != self.action_id:
            message = "Publication Action action ID binding mismatch"
            raise ValueError(message)
        if self.projection.operation != self.operation:
            message = "Publication Action operation binding mismatch"
            raise ValueError(message)
        if self.projection.output != self.artifact_output:
            message = "Publication Action projection output binding mismatch"
            raise ValueError(message)
        if (
            self.artifact.output != self.artifact_output
            or self.artifact.artifact_digest != self.artifact_digest
        ):
            message = "Publication Action projection/artifact binding mismatch"
            raise ValueError(message)
        if self.prerequisites != ():
            message = "Publication Action prerequisites are not exact"
            raise ValueError(message)
        if self.action_inputs != publication_action_inputs(
            self.projection,
            self.artifact,
        ):
            message = "Publication Action inputs are not exact"
            raise ValueError(message)
        if self.mutable_resource_keys != publication_mutable_resource_keys(
            self.projection
        ):
            message = "Publication Action mutable keys are not exact"
            raise ValueError(message)
        if self.lock_projection != publication_lock_projection(self.projection):
            message = "Publication Action lock projection is not exact"
            raise ValueError(message)
        if self.lock_group != publication_lock_group(self.projection):
            message = "Publication Action lock group is not exact"
            raise ValueError(message)
        if self.capability_group != publication_capability_group(
            self.projection
        ):
            message = "Publication Action capability group is not exact"
            raise ValueError(message)
        if self.capability_requirements != publication_capability_requirements(
            self.projection
        ):
            message = "Publication Action capability requirements are not exact"
            raise ValueError(message)
        if self.expected_result != publication_expected_result(self.projection):
            message = "Publication Action expected result is not exact"
            raise ValueError(message)
        if self.receipt_contract != publication_receipt_contract(
            self.projection
        ):
            message = "Publication Action Receipt contract is not exact"
            raise ValueError(message)

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical Publication Action."""
        return cast(
            "dict[str, JsonValue]",
            {
                "schema": PUBLICATION_ACTION_SCHEMA,
                "action-id": self.action_id,
                "projection": self.projection.to_document(),
                "operation": self.operation,
                "artifact": self.artifact.to_document(),
                "artifact-digest": self.artifact_digest,
                "artifact-output": self.artifact_output.to_document(),
                "prerequisites": list(self.prerequisites),
                "action-inputs": [
                    [name, value] for name, value in self.action_inputs
                ],
                "mutable-resource-keys": list(self.mutable_resource_keys),
                "lock-projection": self.lock_projection,
                "lock-group": self.lock_group,
                "capability-group": self.capability_group,
                "capability-requirements": list(self.capability_requirements),
                "expected-result": self.expected_result,
                "receipt-contract": self.receipt_contract,
            },
        )

    @property
    def action_digest(self) -> str:
        """Return the canonical Publication Action digest."""
        return canonical_sha256(self.to_document())


@dataclass(frozen=True, slots=True)
class PublicationSnapshot:
    """Second sealed live Snapshot after qualification and observation."""

    attempt: ReleaseAttemptIdentity
    qualification_snapshot_digest: str
    qualification_decision_digest: str
    qualification_result: str
    projection_ids: tuple[str, ...]
    artifact_digests: tuple[str, ...]
    artifact_output_ids: tuple[str, ...]
    observation_digests: tuple[str, ...]
    observation_projection_ids: tuple[str, ...]
    materialized_actions: tuple[PublicationAction, ...]

    def __post_init__(self) -> None:  # noqa: C901
        """Reject placeholder, incomplete, or simulation second Snapshots."""
        _exact(
            self.attempt, ReleaseAttemptIdentity, field="publication.attempt"
        )
        _digest(
            self.qualification_snapshot_digest,
            field="publication.qualification_snapshot_digest",
        )
        _digest(
            self.qualification_decision_digest,
            field="publication.qualification_decision_digest",
        )
        if self.qualification_result != "success":
            message = "Publication Snapshot requires successful qualification"
            raise ValueError(message)
        projection_ids = _string_tuple(
            self.projection_ids,
            field="publication.projection_ids",
        )
        artifact_digests = _string_tuple(
            self.artifact_digests,
            field="publication.artifact_digests",
        )
        for index, digest in enumerate(artifact_digests):
            _digest(digest, field=f"publication.artifact_digests[{index}]")
        output_ids = _string_tuple(
            self.artifact_output_ids,
            field="publication.artifact_output_ids",
        )
        observation_digests = _string_tuple(
            self.observation_digests,
            field="publication.observation_digests",
        )
        for index, digest in enumerate(observation_digests):
            _digest(digest, field=f"publication.observation_digests[{index}]")
        observed = _string_tuple(
            self.observation_projection_ids,
            field="publication.observation_projection_ids",
        )
        if set(observed) != set(projection_ids):
            message = (
                "Publication Snapshot requires exactly one admitted "
                "observation per projection"
            )
            raise ValueError(message)
        if len(observed) != len(projection_ids):
            message = "Publication Snapshot has duplicate observations"
            raise ValueError(message)
        if len(observation_digests) != len(projection_ids):
            message = (
                "Publication Snapshot requires exactly one admitted "
                "observation per projection"
            )
            raise ValueError(message)
        if not artifact_digests or len(artifact_digests) != len(output_ids):
            message = "Publication Snapshot requires complete artifacts"
            raise ValueError(message)
        _exact(
            self.materialized_actions,
            tuple,
            field="publication.materialized_actions",
        )
        if any(
            type(action) is not PublicationAction
            for action in self.materialized_actions
        ):
            message = "Publication Snapshot action has the wrong type"
            raise TypeError(message)
        action_projection_ids = tuple(
            action.projection.projection_id
            for action in self.materialized_actions
        )
        action_ids = {action.action_id for action in self.materialized_actions}
        if len(action_ids) != len(self.materialized_actions):
            message = "Publication Snapshot contains duplicate actions"
            raise ValueError(message)
        if any(
            projection_id not in projection_ids
            for projection_id in action_projection_ids
        ):
            message = "Publication Snapshot action projection is not planned"
            raise ValueError(message)
        if any(
            action.artifact_digest not in artifact_digests
            or action.artifact_output.output_id not in output_ids
            for action in self.materialized_actions
        ):
            message = "Publication Snapshot action artifact is not admitted"
            raise ValueError(message)

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical Publication Snapshot."""
        return cast(
            "dict[str, JsonValue]",
            {
                "schema": PUBLICATION_SNAPSHOT_SCHEMA,
                "attempt": self.attempt.to_document(),
                "qualification-snapshot-digest": (
                    self.qualification_snapshot_digest
                ),
                "qualification-decision-digest": (
                    self.qualification_decision_digest
                ),
                "qualification-result": self.qualification_result,
                "projection-ids": list(self.projection_ids),
                "artifact-digests": list(self.artifact_digests),
                "artifact-output-ids": list(self.artifact_output_ids),
                "observation-digests": list(self.observation_digests),
                "observation-projection-ids": list(
                    self.observation_projection_ids
                ),
                "materialized-actions": [
                    action.to_document() for action in self.materialized_actions
                ],
            },
        )

    @property
    def snapshot_digest(self) -> str:
        """Return the canonical Publication Snapshot digest."""
        return canonical_sha256(self.to_document())


@dataclass(frozen=True, slots=True)
class SimulationOutcome:
    """Terminal non-authoritative simulation result with no live records."""

    binding: SimulationBinding
    qualification_snapshot_digest: str
    qualification_decision_digest: str
    observation_digests: tuple[str, ...]
    hypothetical_actions: tuple[HypotheticalAction, ...]
    terminal_result: str
    failure_class: str
    next_action: str

    def __post_init__(self) -> None:
        """Reject cross-purpose or falsely complete simulation outcomes."""
        _exact(
            self.binding,
            SimulationBinding,
            field="simulation outcome.binding",
        )
        _digest(
            self.qualification_snapshot_digest,
            field="simulation outcome.qualification_snapshot_digest",
        )
        _digest(
            self.qualification_decision_digest,
            field="simulation outcome.qualification_decision_digest",
        )
        for index, digest in enumerate(
            _string_tuple(
                self.observation_digests,
                field="simulation outcome.observation_digests",
            )
        ):
            _digest(
                digest,
                field=f"simulation outcome.observation_digests[{index}]",
            )
        _exact(
            self.hypothetical_actions,
            tuple,
            field="simulation outcome.hypothetical_actions",
        )
        if any(
            type(action) is not HypotheticalAction
            for action in self.hypothetical_actions
        ):
            message = "Simulation Outcome action has the wrong runtime type"
            raise TypeError(message)
        _choice(
            self.terminal_result,
            _QUALIFICATION_RESULTS,
            field="simulation outcome.terminal_result",
        )
        _string(self.failure_class, field="simulation outcome.failure_class")
        _string(self.next_action, field="simulation outcome.next_action")

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical Simulation Outcome."""
        return cast(
            "dict[str, JsonValue]",
            {
                "schema": SIMULATION_OUTCOME_SCHEMA,
                "binding": self.binding.to_document(),
                "qualification-snapshot-digest": (
                    self.qualification_snapshot_digest
                ),
                "qualification-decision-digest": (
                    self.qualification_decision_digest
                ),
                "observation-digests": list(self.observation_digests),
                "hypothetical-actions": [
                    action.to_document() for action in self.hypothetical_actions
                ],
                "terminal-result": self.terminal_result,
                "failure-class": self.failure_class,
                "next-action": self.next_action,
            },
        )

    @property
    def outcome_digest(self) -> str:
        """Return the canonical Simulation Outcome digest."""
        return canonical_sha256(self.to_document())


type ReleaseRecord = (
    ReleaseIntent
    | SimulationBinding
    | QualificationSnapshot
    | ReleaseArtifact
    | QualificationEvidence
    | QualificationDecision
    | ProjectionObservation
    | HypotheticalAction
    | PublicationAction
    | PublicationSnapshot
    | SimulationOutcome
)


def release_record_digest(record: ReleaseRecord) -> str:
    """Return the canonical digest for one transported Release record."""
    return canonical_sha256(record.to_document())


def admit_release_record(
    canonical_bytes: bytes,
    *,
    expected: ReleaseRecord | None = None,
    expected_type: type[ReleaseRecord] | None = None,
    expected_digest: str,
    expected_bindings: ReleaseAdmissionBindings | None = None,
) -> ReleaseRecord:
    """Deserialize and admit canonical bytes under caller-selected bindings."""
    if type(canonical_bytes) is not bytes:
        message = "Release record transport must be exact bytes"
        raise TypeError(message)
    _digest(expected_digest, field="Release record expected_digest")
    document = parse_canonical_json(canonical_bytes)
    if expected is not None:
        if expected_type is not None and expected_type is not type(expected):
            message = "Release record expected type conflicts with record"
            raise ValueError(message)
        selected_type = type(expected)
    elif expected_type is not None:
        selected_type = expected_type
    else:
        message = "Release record admission requires a caller-selected type"
        raise ValueError(message)
    from three_workflow_delivery_v3.records.release_transport import (  # noqa: PLC0415
        release_record_from_document,
        validate_release_admission_bindings,
    )

    admitted = release_record_from_document(
        document,
        expected_type=selected_type,
    )
    if expected is not None and admitted != expected:
        message = "Release record schema or binding mismatch"
        raise ValueError(message)
    actual_digest = release_record_digest(admitted)
    if actual_digest != expected_digest:
        message = "Release record canonical digest mismatch"
        raise ValueError(message)
    if expected_bindings is not None:
        validate_release_admission_bindings(admitted, expected_bindings)
    return expected if expected is not None else admitted


__all__ = [
    "HYPOTHETICAL_ACTIONS_REPORT_PRODUCER",
    "NPMJS_OBSERVATION_CONTRACT_ID",
    "NPMJS_OBSERVER_PRODUCER",
    "OFFICIAL_SIMULATION_WORKFLOW_PATH",
    "ArtifactVariantIdentity",
    "BuddyExecutionIdentity",
    "DestinationProjection",
    "ExternalPackageCoordinate",
    "HypotheticalAction",
    "ObligationDisposition",
    "ObservationValue",
    "OfficialExecutionIdentity",
    "OfficialProductIdentity",
    "PotentialActionContract",
    "ProjectionObservation",
    "PublicationAction",
    "PublicationSnapshot",
    "QualificationDecision",
    "QualificationEvidence",
    "QualificationSnapshot",
    "ReleaseArtifact",
    "ReleaseAttemptIdentity",
    "ReleaseBuildIdentity",
    "ReleaseBuildRequest",
    "ReleaseIntent",
    "ReleaseObligation",
    "ReleaseOutputIdentity",
    "SimulationBinding",
    "SimulationIdentity",
    "SimulationOutcome",
    "admit_release_record",
    "release_artifact_transport_name",
    "release_record_digest",
]
