"""Closed canonical deserialization for transported Release records."""

from __future__ import annotations

from dataclasses import dataclass
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3.records.artifacts import (
    ArtifactContentIdentity,
    ArtifactReference,
    ArtifactTransportIdentity,
    artifact_reference_from_document,
)
from three_workflow_delivery_v3.records.release import (
    APPROVAL_BUNDLE_SCHEMA,
    ARTIFACT_VARIANT_IDENTITY_SCHEMA,
    ATTEMPT_OUTCOME_SCHEMA,
    BUDDY_EXECUTION_IDENTITY_SCHEMA,
    DESTINATION_PROJECTION_SCHEMA,
    EXACT_SATISFIED_FINALIZATION_PROOF_SCHEMA,
    EXTERNAL_PACKAGE_COORDINATE_SCHEMA,
    HYPOTHETICAL_ACTION_SCHEMA,
    MUTATION_MAY_HAVE_STARTED_SCHEMA,
    OBLIGATION_DISPOSITION_SCHEMA,
    OBSERVATION_REQUEST_FACTS_SCHEMA,
    OBSERVATION_RESPONSE_FACTS_SCHEMA,
    OFFICIAL_EXECUTION_IDENTITY_SCHEMA,
    OFFICIAL_PRODUCT_IDENTITY_SCHEMA,
    POTENTIAL_ACTION_CONTRACT_SCHEMA,
    PROJECTION_OBSERVATION_SCHEMA,
    PUBLICATION_ACTION_SCHEMA,
    PUBLICATION_AUTHORIZATION_SCHEMA,
    PUBLICATION_OBSERVATION_REFERENCE_SCHEMA,
    PUBLICATION_RESULT_SCHEMA,
    PUBLICATION_SNAPSHOT_SCHEMA,
    QUALIFICATION_DECISION_SCHEMA,
    QUALIFICATION_EVIDENCE_SCHEMA,
    QUALIFICATION_SNAPSHOT_SCHEMA,
    RELEASE_ARTIFACT_SCHEMA,
    RELEASE_ATTEMPT_BINDING_SCHEMA,
    RELEASE_ATTEMPT_IDENTITY_SCHEMA,
    RELEASE_BUILD_IDENTITY_SCHEMA,
    RELEASE_BUILD_REQUEST_SCHEMA,
    RELEASE_INTENT_SCHEMA,
    RELEASE_OBLIGATION_SCHEMA,
    RELEASE_OUTPUT_IDENTITY_SCHEMA,
    REMOTE_STATE_OBSERVATION_SCHEMA,
    SIMULATION_BINDING_SCHEMA,
    SIMULATION_IDENTITY_SCHEMA,
    SIMULATION_OUTCOME_SCHEMA,
    ApprovalBoundary,
    ApprovalBundle,
    ArtifactVariantIdentity,
    AttemptOutcome,
    BuddyExecutionIdentity,
    DestinationProjection,
    DestinationReadback,
    DirectPredecessor,
    ExactSatisfiedFinalizationProof,
    ExternalPackageCoordinate,
    GovernanceProof,
    HypotheticalAction,
    MutationMayHaveStartedMarker,
    ObligationDisposition,
    ObservationRequestFacts,
    ObservationResponseFacts,
    ObservationValue,
    OfficialExecutionIdentity,
    OfficialProductIdentity,
    PackageControlProof,
    PackageControlSubject,
    PotentialActionContract,
    ProfileMatchEvidence,
    ProjectionObservation,
    PublicationAction,
    PublicationAuthorization,
    PublicationDiagnostics,
    PublicationObservationReference,
    PublicationResult,
    PublicationSnapshot,
    QualificationDecision,
    QualificationEvidence,
    QualificationSnapshot,
    ReleaseArtifact,
    ReleaseAttemptBinding,
    ReleaseAttemptIdentity,
    ReleaseBuildIdentity,
    ReleaseBuildRequest,
    ReleaseIntent,
    ReleaseObligation,
    ReleaseOutputIdentity,
    RemoteStateObservation,
    SimulationBinding,
    SimulationIdentity,
    SimulationOutcome,
)
from three_workflow_delivery_v3.repository.node_provider import NbgvFacts

if TYPE_CHECKING:
    from collections.abc import Callable

    from three_workflow_delivery_v3.canonical import JsonValue
    from three_workflow_delivery_v3.records.release import (
        QualificationSubject,
        ReleaseRecord,
    )

_ARTIFACT_TRANSPORT_FIELDS = frozenset(
    {
        "artifact-id",
        "artifact-name",
        "artifact-url",
        "transport-digest",
        "producer",
        "workflow-run-id",
    }
)
_ARTIFACT_CONTENT_FIELDS = frozenset(
    {
        "output-id",
        "logical-role",
        "media-kind",
        "basename",
        "byte-size",
        "content-sha256",
        "content-sha512",
    }
)


@dataclass(frozen=True, slots=True)
class ReleaseAdmissionBindings:
    """Caller-selected current bindings for one transported Release record."""

    purpose: str
    workflow_run_id: int
    run_attempt: int | None
    target: str
    producer: str | None = None

    def __post_init__(self) -> None:
        """Reject caller bindings outside the purpose-selected field set."""
        if self.purpose == "live-release":
            if self.run_attempt is not None:
                message = "live Release admission cannot bind run_attempt"
                raise ValueError(message)
            return
        if self.purpose != "release-simulation":
            message = "Release admission purpose is not in the closed set"
            raise ValueError(message)
        if type(self.run_attempt) is not int or self.run_attempt <= 0:
            message = (
                "simulation Release admission run_attempt must be a "
                "positive non-Boolean integer"
            )
            raise ValueError(message)


def _object(value: JsonValue, *, field: str) -> dict[str, JsonValue]:
    if not isinstance(value, dict):
        message = f"{field} must be an object"
        raise TypeError(message)
    return value


def _closed(
    value: JsonValue,
    *,
    field: str,
    schema: str | None,
    fields: frozenset[str],
) -> dict[str, JsonValue]:
    document = _object(value, field=field)
    expected = fields | ({"schema"} if schema is not None else set())
    missing = expected - document.keys()
    if missing:
        name = sorted(missing)[0]
        message = f"{field} missing required field: {name}"
        raise ValueError(message)
    unknown = document.keys() - expected
    if unknown:
        name = sorted(unknown)[0]
        message = f"{field} unknown field: {name}"
        raise ValueError(message)
    if schema is not None and document["schema"] != schema:
        message = f"{field} has the wrong schema"
        raise ValueError(message)
    return document


def _string(value: JsonValue, *, field: str) -> str:
    if type(value) is not str:
        message = f"{field} must be a string"
        raise TypeError(message)
    return value


def _integer(value: JsonValue, *, field: str) -> int:
    if type(value) is not int:
        message = f"{field} must be an integer"
        raise TypeError(message)
    return value


def _boolean(value: JsonValue, *, field: str) -> bool:
    if type(value) is not bool:
        message = f"{field} must be a Boolean"
        raise TypeError(message)
    return value


def _array(value: JsonValue, *, field: str) -> list[JsonValue]:
    if not isinstance(value, list):
        message = f"{field} must be an array"
        raise TypeError(message)
    return value


def _strings(value: JsonValue, *, field: str) -> tuple[str, ...]:
    return tuple(
        _string(item, field=f"{field}[{index}]")
        for index, item in enumerate(_array(value, field=field))
    )


def _pairs(value: JsonValue, *, field: str) -> tuple[tuple[str, str], ...]:
    pairs: list[tuple[str, str]] = []
    for index, item in enumerate(_array(value, field=field)):
        pair = _array(item, field=f"{field}[{index}]")
        if len(pair) != 2:  # noqa: PLR2004
            message = f"{field}[{index}] must contain exactly two strings"
            raise ValueError(message)
        pairs.append(
            (
                _string(pair[0], field=f"{field}[{index}][0]"),
                _string(pair[1], field=f"{field}[{index}][1]"),
            )
        )
    return tuple(pairs)


def _nested_strings(
    value: JsonValue,
    *,
    field: str,
) -> tuple[tuple[str, tuple[str, ...]], ...]:
    pairs: list[tuple[str, tuple[str, ...]]] = []
    for index, item in enumerate(_array(value, field=field)):
        pair = _array(item, field=f"{field}[{index}]")
        if len(pair) != 2:  # noqa: PLR2004
            message = f"{field}[{index}] must contain exactly two values"
            raise ValueError(message)
        pairs.append(
            (
                _string(pair[0], field=f"{field}[{index}][0]"),
                _strings(pair[1], field=f"{field}[{index}][1]"),
            )
        )
    return tuple(pairs)


def _nullable_integer(value: JsonValue, *, field: str) -> int | None:
    return None if value is None else _integer(value, field=field)


def _nullable_string(value: JsonValue, *, field: str) -> str | None:
    return None if value is None else _string(value, field=field)


def _artifact_reference(value: JsonValue) -> ArtifactReference:
    return artifact_reference_from_document(value)


def _approval_boundary(value: JsonValue) -> ApprovalBoundary:
    document = _closed(
        value,
        field="approval boundary",
        schema=None,
        fields=frozenset(
            {
                "environment",
                "job",
                "sentinel-name",
                "sentinel-value",
                "sentinel-result",
            }
        ),
    )
    return ApprovalBoundary(
        environment=_string(
            document["environment"],
            field="approval boundary.environment",
        ),
        job=_string(document["job"], field="approval boundary.job"),
        sentinel_name=_string(
            document["sentinel-name"],
            field="approval boundary.sentinel-name",
        ),
        sentinel_value=_string(
            document["sentinel-value"],
            field="approval boundary.sentinel-value",
        ),
        sentinel_result=_string(
            document["sentinel-result"],
            field="approval boundary.sentinel-result",
        ),
    )


def _governance_proof(value: JsonValue) -> GovernanceProof:
    document = _closed(
        value,
        field="Governance proof",
        schema=None,
        fields=frozenset(
            {
                "provenance",
                "current-main-sha",
                "observed-at",
                "expires-at",
                "live-enabled",
            }
        ),
    )
    return GovernanceProof(
        provenance=_pairs(
            document["provenance"],
            field="Governance proof.provenance",
        ),
        current_main_sha=_string(
            document["current-main-sha"],
            field="Governance proof.current-main-sha",
        ),
        observed_at=_string(
            document["observed-at"],
            field="Governance proof.observed-at",
        ),
        expires_at=_string(
            document["expires-at"],
            field="Governance proof.expires-at",
        ),
        live_enabled=_boolean(
            document["live-enabled"],
            field="Governance proof.live-enabled",
        ),
    )


def _package_control_subject(value: JsonValue) -> PackageControlSubject:
    document = _closed(
        value,
        field="Package-Control subject",
        schema=None,
        fields=frozenset({"destination-id", "registry", "normalized-package"}),
    )
    return PackageControlSubject(
        destination_id=_string(
            document["destination-id"],
            field="Package-Control subject.destination-id",
        ),
        registry=_string(
            document["registry"],
            field="Package-Control subject.registry",
        ),
        normalized_package=_string(
            document["normalized-package"],
            field="Package-Control subject.normalized-package",
        ),
    )


def _package_control_proof(value: JsonValue) -> PackageControlProof:
    document = _closed(
        value,
        field="Package-Control Proof",
        schema=None,
        fields=frozenset(
            {
                "subject",
                "observed-at",
                "endpoints",
                "facts",
                "response-digests",
            }
        ),
    )
    return PackageControlProof(
        subject=_package_control_subject(document["subject"]),
        observed_at=_string(
            document["observed-at"],
            field="Package-Control Proof.observed-at",
        ),
        endpoints=_strings(
            document["endpoints"],
            field="Package-Control Proof.endpoints",
        ),
        facts=_nested_strings(
            document["facts"],
            field="Package-Control Proof.facts",
        ),
        response_digests=_pairs(
            document["response-digests"],
            field="Package-Control Proof.response-digests",
        ),
    )


def _profile_match(value: JsonValue) -> ProfileMatchEvidence:
    document = _closed(
        value,
        field="profile match",
        schema=None,
        fields=frozenset(
            {
                "destination-operation-profile-digest",
                "node-version",
                "npm-version",
                "command",
                "configuration",
                "matched-at",
            }
        ),
    )
    return ProfileMatchEvidence(
        destination_operation_profile_digest=_string(
            document["destination-operation-profile-digest"],
            field="profile match.destination-operation-profile-digest",
        ),
        node_version=_string(
            document["node-version"],
            field="profile match.node-version",
        ),
        npm_version=_string(
            document["npm-version"],
            field="profile match.npm-version",
        ),
        command=_strings(
            document["command"],
            field="profile match.command",
        ),
        configuration=_pairs(
            document["configuration"],
            field="profile match.configuration",
        ),
        matched_at=_string(
            document["matched-at"],
            field="profile match.matched-at",
        ),
    )


def _destination_readback(value: JsonValue) -> DestinationReadback:
    document = _closed(
        value,
        field="destination readback",
        schema=None,
        fields=frozenset(
            {
                "package",
                "version",
                "classification",
                "content-sha256",
                "content-sha512",
                "witness-digest",
                "witness-target",
                "tag",
                "tag-state",
                "tag-version",
                "observed-at",
                "response-digests",
            }
        ),
    )
    return DestinationReadback(
        package=_string(
            document["package"],
            field="destination readback.package",
        ),
        version=_string(
            document["version"],
            field="destination readback.version",
        ),
        classification=_string(
            document["classification"],
            field="destination readback.classification",
        ),
        content_sha256=_nullable_string(
            document["content-sha256"],
            field="destination readback.content-sha256",
        ),
        content_sha512=_nullable_string(
            document["content-sha512"],
            field="destination readback.content-sha512",
        ),
        witness_digest=_nullable_string(
            document["witness-digest"],
            field="destination readback.witness-digest",
        ),
        witness_target=_nullable_string(
            document["witness-target"],
            field="destination readback.witness-target",
        ),
        tag=_string(document["tag"], field="destination readback.tag"),
        tag_state=_string(
            document["tag-state"],
            field="destination readback.tag-state",
        ),
        tag_version=_nullable_string(
            document["tag-version"],
            field="destination readback.tag-version",
        ),
        observed_at=_string(
            document["observed-at"],
            field="destination readback.observed-at",
        ),
        response_digests=_pairs(
            document["response-digests"],
            field="destination readback.response-digests",
        ),
    )


def _publication_diagnostics(value: JsonValue) -> PublicationDiagnostics:
    document = _closed(
        value,
        field="publication diagnostics",
        schema=None,
        fields=frozenset({"entries", "truncated"}),
    )
    return PublicationDiagnostics(
        entries=_strings(
            document["entries"],
            field="publication diagnostics.entries",
        ),
        truncated=_boolean(
            document["truncated"],
            field="publication diagnostics.truncated",
        ),
    )


def _records(
    value: JsonValue,
    *,
    field: str,
    parser: Callable[[JsonValue], object],
) -> tuple[object, ...]:
    return tuple(parser(item) for item in _array(value, field=field))


def _nbgv(value: JsonValue) -> NbgvFacts:
    document = _closed(
        value,
        field="NBGV",
        schema=None,
        fields=frozenset({"canonical", "native", "node-api-result-digest"}),
    )
    canonical = _closed(
        document["canonical"],
        field="NBGV canonical",
        schema=None,
        fields=frozenset(
            {
                "version",
                "semVer1",
                "semVer2",
                "versionHeight",
                "gitCommitId",
                "publicRelease",
            }
        ),
    )
    native = _closed(
        document["native"],
        field="NBGV native",
        schema=None,
        fields=frozenset({"npmPackageVersion"}),
    )
    return NbgvFacts(
        canonical_version=_string(
            canonical["version"],
            field="NBGV canonical.version",
        ),
        sem_ver1=_string(
            canonical["semVer1"],
            field="NBGV canonical.semVer1",
        ),
        sem_ver2=_string(
            canonical["semVer2"],
            field="NBGV canonical.semVer2",
        ),
        version_height=_integer(
            canonical["versionHeight"],
            field="NBGV canonical.versionHeight",
        ),
        git_commit_id=_string(
            canonical["gitCommitId"],
            field="NBGV canonical.gitCommitId",
        ),
        public_release=_boolean(
            canonical["publicRelease"],
            field="NBGV canonical.publicRelease",
        ),
        npm_package_version=_string(
            native["npmPackageVersion"],
            field="NBGV native.npmPackageVersion",
        ),
        node_api_result_digest=_string(
            document["node-api-result-digest"],
            field="NBGV node-api-result-digest",
        ),
    )


def _release_intent(value: JsonValue) -> ReleaseIntent:
    document = _closed(
        value,
        field="ReleaseIntent",
        schema=RELEASE_INTENT_SCHEMA,
        fields=frozenset(
            {
                "repository",
                "workflow-path",
                "workflow-ref",
                "workflow-sha",
                "request-id",
                "actor",
                "workflow-run-id",
                "event-kind",
                "selected-ref",
                "target",
                "channel",
                "mode",
                "purpose",
                "release-unit",
            }
        ),
    )
    return ReleaseIntent(
        repository=_string(document["repository"], field="intent.repository"),
        workflow_path=_string(
            document["workflow-path"],
            field="intent.workflow-path",
        ),
        workflow_ref=_string(
            document["workflow-ref"],
            field="intent.workflow-ref",
        ),
        workflow_sha=_string(
            document["workflow-sha"],
            field="intent.workflow-sha",
        ),
        request_id=_string(document["request-id"], field="intent.request-id"),
        actor=_string(document["actor"], field="intent.actor"),
        workflow_run_id=_integer(
            document["workflow-run-id"],
            field="intent.workflow-run-id",
        ),
        event_kind=_string(
            document["event-kind"],
            field="intent.event-kind",
        ),
        selected_ref=_string(
            document["selected-ref"],
            field="intent.selected-ref",
        ),
        target=_string(document["target"], field="intent.target"),
        channel=_string(document["channel"], field="intent.channel"),
        mode=_string(document["mode"], field="intent.mode"),
        purpose=_string(document["purpose"], field="intent.purpose"),
        release_unit=_string(
            document["release-unit"],
            field="intent.release-unit",
        ),
    )


def _simulation_identity(value: JsonValue) -> SimulationIdentity:
    document = _closed(
        value,
        field="SimulationIdentity",
        schema=SIMULATION_IDENTITY_SCHEMA,
        fields=frozenset(
            {
                "namespace",
                "request-id",
                "workflow-run-id",
                "run-attempt",
                "identity",
            }
        ),
    )
    return SimulationIdentity(
        namespace=_string(
            document["namespace"],
            field="simulation.namespace",
        ),
        request_id=_string(
            document["request-id"],
            field="simulation.request-id",
        ),
        workflow_run_id=_integer(
            document["workflow-run-id"],
            field="simulation.workflow-run-id",
        ),
        run_attempt=_integer(
            document["run-attempt"],
            field="simulation.run-attempt",
        ),
        identity=_string(
            document["identity"],
            field="simulation.identity",
        ),
    )


def _official_product(value: JsonValue) -> OfficialProductIdentity:
    document = _closed(
        value,
        field="OfficialProductIdentity",
        schema=OFFICIAL_PRODUCT_IDENTITY_SCHEMA,
        fields=frozenset({"channel", "release-unit", "canonical-version"}),
    )
    return OfficialProductIdentity(
        channel=_string(document["channel"], field="product.channel"),
        release_unit=_string(
            document["release-unit"],
            field="product.release-unit",
        ),
        canonical_version=_string(
            document["canonical-version"],
            field="product.canonical-version",
        ),
    )


def _buddy_execution(value: JsonValue) -> BuddyExecutionIdentity:
    buddy = _closed(
        value,
        field="BuddyExecutionIdentity",
        schema=BUDDY_EXECUTION_IDENTITY_SCHEMA,
        fields=frozenset({"channel", "release-unit", "target"}),
    )
    return BuddyExecutionIdentity(
        channel=_string(buddy["channel"], field="execution.channel"),
        release_unit=_string(
            buddy["release-unit"],
            field="execution.release-unit",
        ),
        target=_string(buddy["target"], field="execution.target"),
    )


def _release_attempt(value: JsonValue) -> ReleaseAttemptIdentity:
    document = _closed(
        value,
        field="ReleaseAttemptIdentity",
        schema=RELEASE_ATTEMPT_IDENTITY_SCHEMA,
        fields=frozenset({"execution", "workflow-run-id"}),
    )
    execution_document = _object(
        document["execution"],
        field="attempt.execution",
    )
    schema = execution_document.get("schema")
    if schema == OFFICIAL_EXECUTION_IDENTITY_SCHEMA:
        official = _closed(
            execution_document,
            field="OfficialExecutionIdentity",
            schema=OFFICIAL_EXECUTION_IDENTITY_SCHEMA,
            fields=frozenset({"product", "target"}),
        )
        execution = OfficialExecutionIdentity(
            product=_official_product(official["product"]),
            target=_string(official["target"], field="execution.target"),
        )
    elif schema == BUDDY_EXECUTION_IDENTITY_SCHEMA:
        execution = _buddy_execution(execution_document)
    else:
        message = "ReleaseAttemptIdentity execution has the wrong schema"
        raise ValueError(message)
    return ReleaseAttemptIdentity(
        execution=execution,
        workflow_run_id=_integer(
            document["workflow-run-id"],
            field="attempt.workflow-run-id",
        ),
    )


def _subject(value: JsonValue) -> SimulationIdentity | ReleaseAttemptIdentity:
    document = _object(value, field="qualification subject")
    schema = document.get("schema")
    if schema == SIMULATION_IDENTITY_SCHEMA:
        return _simulation_identity(document)
    if schema == RELEASE_ATTEMPT_IDENTITY_SCHEMA:
        return _release_attempt(document)
    message = "qualification subject has the wrong schema"
    raise ValueError(message)


def _simulation_binding(value: JsonValue) -> SimulationBinding:
    document = _closed(
        value,
        field="SimulationBinding",
        schema=SIMULATION_BINDING_SCHEMA,
        fields=frozenset(
            {
                "simulation",
                "intent-digest",
                "repository-model-digest",
                "purpose",
                "target",
                "channel",
                "release-unit",
                "control",
            }
        ),
    )
    return SimulationBinding(
        simulation=_simulation_identity(document["simulation"]),
        intent_digest=_string(
            document["intent-digest"],
            field="binding.intent-digest",
        ),
        repository_model_digest=_string(
            document["repository-model-digest"],
            field="binding.repository-model-digest",
        ),
        purpose=_string(document["purpose"], field="binding.purpose"),
        target=_string(document["target"], field="binding.target"),
        channel=_string(document["channel"], field="binding.channel"),
        release_unit=_string(
            document["release-unit"],
            field="binding.release-unit",
        ),
        control=_string(document["control"], field="binding.control"),
    )


def _build(value: JsonValue) -> ReleaseBuildIdentity:
    document = _closed(
        value,
        field="ReleaseBuildIdentity",
        schema=RELEASE_BUILD_IDENTITY_SCHEMA,
        fields=frozenset(
            {"release-unit", "build-id", "definition-id", "project-id"}
        ),
    )
    return ReleaseBuildIdentity(
        release_unit=_string(
            document["release-unit"],
            field="build.release-unit",
        ),
        build_id=_string(document["build-id"], field="build.build-id"),
        definition_id=_string(
            document["definition-id"],
            field="build.definition-id",
        ),
        project_id=_string(
            document["project-id"],
            field="build.project-id",
        ),
    )


def _variant(value: JsonValue) -> ArtifactVariantIdentity:
    document = _closed(
        value,
        field="ArtifactVariantIdentity",
        schema=ARTIFACT_VARIANT_IDENTITY_SCHEMA,
        fields=frozenset({"build", "variant-id", "dimensions"}),
    )
    return ArtifactVariantIdentity(
        build=_build(document["build"]),
        variant_id=_string(
            document["variant-id"],
            field="variant.variant-id",
        ),
        dimensions=_pairs(document["dimensions"], field="variant.dimensions"),
    )


def _output(value: JsonValue) -> ReleaseOutputIdentity:
    document = _closed(
        value,
        field="ReleaseOutputIdentity",
        schema=RELEASE_OUTPUT_IDENTITY_SCHEMA,
        fields=frozenset(
            {"variant", "output-id", "logical-role", "media-kind"}
        ),
    )
    return ReleaseOutputIdentity(
        variant=_variant(document["variant"]),
        output_id=_string(
            document["output-id"],
            field="output.output-id",
        ),
        logical_role=_string(
            document["logical-role"],
            field="output.logical-role",
        ),
        media_kind=_string(
            document["media-kind"],
            field="output.media-kind",
        ),
    )


def _build_request(value: JsonValue) -> ReleaseBuildRequest:
    document = _closed(
        value,
        field="ReleaseBuildRequest",
        schema=RELEASE_BUILD_REQUEST_SCHEMA,
        fields=frozenset(
            {
                "build",
                "variant",
                "output",
                "repository-model-digest",
                "definition-digest",
                "npm-package-version",
                "witness-digest",
                "declared-inputs",
                "adapter-id",
            }
        ),
    )
    return ReleaseBuildRequest(
        build=_build(document["build"]),
        variant=_variant(document["variant"]),
        output=_output(document["output"]),
        repository_model_digest=_string(
            document["repository-model-digest"],
            field="build request.repository-model-digest",
        ),
        definition_digest=_string(
            document["definition-digest"],
            field="build request.definition-digest",
        ),
        npm_package_version=_string(
            document["npm-package-version"],
            field="build request.npm-package-version",
        ),
        witness_digest=_string(
            document["witness-digest"],
            field="build request.witness-digest",
        ),
        declared_inputs=_strings(
            document["declared-inputs"],
            field="build request.declared-inputs",
        ),
        adapter_id=_string(
            document["adapter-id"],
            field="build request.adapter-id",
        ),
    )


def _obligation(value: JsonValue) -> ReleaseObligation:
    document = _closed(
        value,
        field="ReleaseObligation",
        schema=RELEASE_OBLIGATION_SCHEMA,
        fields=frozenset(
            {
                "obligation-id",
                "definition-id",
                "definition-digest",
                "subject-kind",
                "subject-digest",
                "target",
                "dimensions",
                "runner",
                "prerequisites",
                "required",
                "request-digest",
                "expected-evidence-id",
            }
        ),
    )
    return ReleaseObligation(
        obligation_id=_string(
            document["obligation-id"],
            field="obligation.obligation-id",
        ),
        definition_id=_string(
            document["definition-id"],
            field="obligation.definition-id",
        ),
        definition_digest=_string(
            document["definition-digest"],
            field="obligation.definition-digest",
        ),
        subject_kind=_string(
            document["subject-kind"],
            field="obligation.subject-kind",
        ),
        subject_digest=_string(
            document["subject-digest"],
            field="obligation.subject-digest",
        ),
        target=_string(document["target"], field="obligation.target"),
        dimensions=_pairs(
            document["dimensions"],
            field="obligation.dimensions",
        ),
        runner=_string(document["runner"], field="obligation.runner"),
        prerequisites=_strings(
            document["prerequisites"],
            field="obligation.prerequisites",
        ),
        required=_boolean(
            document["required"],
            field="obligation.required",
        ),
        request_digest=_string(
            document["request-digest"],
            field="obligation.request-digest",
        ),
        expected_evidence_id=_string(
            document["expected-evidence-id"],
            field="obligation.expected-evidence-id",
        ),
    )


def _coordinate(value: JsonValue) -> ExternalPackageCoordinate:
    document = _closed(
        value,
        field="ExternalPackageCoordinate",
        schema=EXTERNAL_PACKAGE_COORDINATE_SCHEMA,
        fields=frozenset(
            {"channel", "destination-id", "package-name", "native-version"}
        ),
    )
    return ExternalPackageCoordinate(
        channel=_string(document["channel"], field="coordinate.channel"),
        destination_id=_string(
            document["destination-id"],
            field="coordinate.destination-id",
        ),
        package_name=_string(
            document["package-name"],
            field="coordinate.package-name",
        ),
        native_version=_string(
            document["native-version"],
            field="coordinate.native-version",
        ),
    )


def _projection(value: JsonValue) -> DestinationProjection:
    document = _closed(
        value,
        field="DestinationProjection",
        schema=DESTINATION_PROJECTION_SCHEMA,
        fields=frozenset(
            {
                "projection-id",
                "destination-id",
                "registry",
                "coordinate",
                "output",
                "operation",
                "observation-contract-id",
                "potential-action-id",
            }
        ),
    )
    return DestinationProjection(
        projection_id=_string(
            document["projection-id"],
            field="projection.projection-id",
        ),
        destination_id=_string(
            document["destination-id"],
            field="projection.destination-id",
        ),
        registry=_string(document["registry"], field="projection.registry"),
        coordinate=_coordinate(document["coordinate"]),
        output=_output(document["output"]),
        operation=_string(
            document["operation"],
            field="projection.operation",
        ),
        observation_contract_id=_string(
            document["observation-contract-id"],
            field="projection.observation-contract-id",
        ),
        potential_action_id=_string(
            document["potential-action-id"],
            field="projection.potential-action-id",
        ),
    )


def _potential_action(value: JsonValue) -> PotentialActionContract:
    document = _closed(
        value,
        field="PotentialActionContract",
        schema=POTENTIAL_ACTION_CONTRACT_SCHEMA,
        fields=frozenset(
            {
                "contract-id",
                "projection-id",
                "operation",
                "output",
                "prerequisites",
                "capability-requirements",
                "mutable-resource-key-basis",
            }
        ),
    )
    return PotentialActionContract(
        contract_id=_string(
            document["contract-id"],
            field="potential action.contract-id",
        ),
        projection_id=_string(
            document["projection-id"],
            field="potential action.projection-id",
        ),
        operation=_string(
            document["operation"],
            field="potential action.operation",
        ),
        output=_output(document["output"]),
        prerequisites=_strings(
            document["prerequisites"],
            field="potential action.prerequisites",
        ),
        capability_requirements=_strings(
            document["capability-requirements"],
            field="potential action.capability-requirements",
        ),
        mutable_resource_key_basis=_strings(
            document["mutable-resource-key-basis"],
            field="potential action.mutable-resource-key-basis",
        ),
    )


def _qualification_snapshot(value: JsonValue) -> QualificationSnapshot:
    document = _closed(
        value,
        field="QualificationSnapshot",
        schema=QUALIFICATION_SNAPSHOT_SCHEMA,
        fields=frozenset(
            {
                "subject",
                "repository",
                "repository-model-digest",
                "release-policy-digest",
                "target",
                "channel",
                "release-unit",
                "nbgv",
                "builds",
                "variants",
                "outputs",
                "build-requests",
                "destination-projections",
                "potential-actions",
                "obligations",
                "expected-evidence-ids",
                "ready",
            }
        ),
    )
    builds = cast(
        "tuple[ReleaseBuildIdentity, ...]",
        _records(document["builds"], field="snapshot.builds", parser=_build),
    )
    variants = cast(
        "tuple[ArtifactVariantIdentity, ...]",
        _records(
            document["variants"],
            field="snapshot.variants",
            parser=_variant,
        ),
    )
    outputs = cast(
        "tuple[ReleaseOutputIdentity, ...]",
        _records(
            document["outputs"],
            field="snapshot.outputs",
            parser=_output,
        ),
    )
    build_requests = cast(
        "tuple[ReleaseBuildRequest, ...]",
        _records(
            document["build-requests"],
            field="snapshot.build-requests",
            parser=_build_request,
        ),
    )
    projections = cast(
        "tuple[DestinationProjection, ...]",
        _records(
            document["destination-projections"],
            field="snapshot.destination-projections",
            parser=_projection,
        ),
    )
    actions = cast(
        "tuple[PotentialActionContract, ...]",
        _records(
            document["potential-actions"],
            field="snapshot.potential-actions",
            parser=_potential_action,
        ),
    )
    obligations = cast(
        "tuple[ReleaseObligation, ...]",
        _records(
            document["obligations"],
            field="snapshot.obligations",
            parser=_obligation,
        ),
    )
    subject_document = _object(document["subject"], field="snapshot.subject")
    subject: QualificationSubject
    if subject_document.get("schema") == SIMULATION_BINDING_SCHEMA:
        subject = _simulation_binding(subject_document)
    else:
        subject = _release_attempt(subject_document)
    return QualificationSnapshot(
        subject=subject,
        repository=_string(
            document["repository"],
            field="snapshot.repository",
        ),
        repository_model_digest=_string(
            document["repository-model-digest"],
            field="snapshot.repository-model-digest",
        ),
        release_policy_digest=_string(
            document["release-policy-digest"],
            field="snapshot.release-policy-digest",
        ),
        target=_string(document["target"], field="snapshot.target"),
        channel=_string(document["channel"], field="snapshot.channel"),
        release_unit=_string(
            document["release-unit"],
            field="snapshot.release-unit",
        ),
        nbgv=_nbgv(document["nbgv"]),
        builds=builds,
        variants=variants,
        outputs=outputs,
        build_requests=build_requests,
        destination_projections=projections,
        potential_actions=actions,
        obligations=obligations,
        expected_evidence_ids=_strings(
            document["expected-evidence-ids"],
            field="snapshot.expected-evidence-ids",
        ),
        ready=_boolean(document["ready"], field="snapshot.ready"),
    )


def _transport(
    value: JsonValue,
    *,
    purpose: str,
) -> ArtifactTransportIdentity:
    if purpose not in {"live-release", "release-simulation"}:
        message = "Artifact transport purpose is not in the closed set"
        raise ValueError(message)
    fields = _ARTIFACT_TRANSPORT_FIELDS
    if purpose == "release-simulation":
        fields |= {"run-attempt"}
    document = _closed(
        value,
        field="ArtifactTransportIdentity",
        schema=None,
        fields=fields,
    )
    return ArtifactTransportIdentity(
        artifact_id=_integer(
            document["artifact-id"],
            field="transport.artifact-id",
        ),
        artifact_name=_string(
            document["artifact-name"],
            field="transport.artifact-name",
        ),
        artifact_url=_string(
            document["artifact-url"],
            field="transport.artifact-url",
        ),
        transport_digest=_string(
            document["transport-digest"],
            field="transport.transport-digest",
        ),
        producer=_string(
            document["producer"],
            field="transport.producer",
        ),
        workflow_run_id=_integer(
            document["workflow-run-id"],
            field="transport.workflow-run-id",
        ),
        run_attempt=(
            None
            if purpose == "live-release"
            else _integer(
                document["run-attempt"],
                field="transport.run-attempt",
            )
        ),
    )


def _content(value: JsonValue) -> ArtifactContentIdentity:
    document = _closed(
        value,
        field="ArtifactContentIdentity",
        schema=None,
        fields=_ARTIFACT_CONTENT_FIELDS,
    )
    return ArtifactContentIdentity(
        output_id=_string(
            document["output-id"],
            field="content.output-id",
        ),
        logical_role=_string(
            document["logical-role"],
            field="content.logical-role",
        ),
        media_kind=_string(
            document["media-kind"],
            field="content.media-kind",
        ),
        basename=_string(document["basename"], field="content.basename"),
        byte_size=_integer(
            document["byte-size"],
            field="content.byte-size",
        ),
        content_sha256=_string(
            document["content-sha256"],
            field="content.content-sha256",
        ),
        content_sha512=_nullable_string(
            document["content-sha512"],
            field="content.content-sha512",
        ),
    )


def _release_artifact(value: JsonValue) -> ReleaseArtifact:
    raw_document = _object(value, field="ReleaseArtifact")
    if "purpose" not in raw_document:
        message = "ReleaseArtifact missing field: purpose"
        raise ValueError(message)
    purpose = _string(raw_document["purpose"], field="artifact.purpose")
    if purpose not in {"live-release", "release-simulation"}:
        message = "Release Artifact purpose is not in the closed set"
        raise ValueError(message)
    document = _closed(
        value,
        field="ReleaseArtifact",
        schema=RELEASE_ARTIFACT_SCHEMA,
        fields=frozenset(
            {
                "subject",
                "repository",
                "qualification-snapshot-digest",
                "repository-model-digest",
                "target",
                "purpose",
                "output",
                "build-request-digest",
                "transport",
                "content",
                "witness-digest",
                "source-input-manifest",
                "toolchain",
                "entries",
                "lifecycle-scripts",
                "provenance-digest",
            }
        ),
    )
    return ReleaseArtifact(
        subject=_subject(document["subject"]),
        repository=_string(
            document["repository"],
            field="artifact.repository",
        ),
        qualification_snapshot_digest=_string(
            document["qualification-snapshot-digest"],
            field="artifact.qualification-snapshot-digest",
        ),
        repository_model_digest=_string(
            document["repository-model-digest"],
            field="artifact.repository-model-digest",
        ),
        target=_string(document["target"], field="artifact.target"),
        purpose=purpose,
        output=_output(document["output"]),
        build_request_digest=_string(
            document["build-request-digest"],
            field="artifact.build-request-digest",
        ),
        transport=_transport(document["transport"], purpose=purpose),
        content=_content(document["content"]),
        entries=_strings(document["entries"], field="artifact.entries"),
        lifecycle_scripts=_pairs(
            document["lifecycle-scripts"],
            field="artifact.lifecycle-scripts",
        ),
        witness_digest=_string(
            document["witness-digest"],
            field="artifact.witness-digest",
        ),
        source_input_manifest=_pairs(
            document["source-input-manifest"],
            field="artifact.source-input-manifest",
        ),
        toolchain=_pairs(document["toolchain"], field="artifact.toolchain"),
        provenance_digest=_string(
            document["provenance-digest"],
            field="artifact.provenance-digest",
        ),
    )


def _qualification_evidence(value: JsonValue) -> QualificationEvidence:
    raw_document = _object(value, field="QualificationEvidence")
    if "subject" not in raw_document:
        message = "QualificationEvidence missing field: subject"
        raise ValueError(message)
    subject = _subject(raw_document["subject"])
    fields = {
        "evidence-id",
        "subject",
        "qualification-snapshot-digest",
        "obligation",
        "producer",
        "workflow-run-id",
        "raw-result",
        "normalized-outcome",
        "artifact-digests",
        "result-facts",
        "diagnostics",
    }
    if isinstance(subject, SimulationIdentity):
        fields.add("run-attempt")
    document = _closed(
        value,
        field="QualificationEvidence",
        schema=QUALIFICATION_EVIDENCE_SCHEMA,
        fields=frozenset(fields),
    )
    return QualificationEvidence(
        evidence_id=_string(
            document["evidence-id"],
            field="evidence.evidence-id",
        ),
        subject=subject,
        qualification_snapshot_digest=_string(
            document["qualification-snapshot-digest"],
            field="evidence.qualification-snapshot-digest",
        ),
        obligation=_obligation(document["obligation"]),
        producer=_string(document["producer"], field="evidence.producer"),
        workflow_run_id=_integer(
            document["workflow-run-id"],
            field="evidence.workflow-run-id",
        ),
        run_attempt=(
            _integer(
                document["run-attempt"],
                field="evidence.run-attempt",
            )
            if isinstance(subject, SimulationIdentity)
            else None
        ),
        raw_result=_string(
            document["raw-result"],
            field="evidence.raw-result",
        ),
        normalized_outcome=_string(
            document["normalized-outcome"],
            field="evidence.normalized-outcome",
        ),
        artifact_digests=_strings(
            document["artifact-digests"],
            field="evidence.artifact-digests",
        ),
        result_facts=_pairs(
            document["result-facts"],
            field="evidence.result-facts",
        ),
        diagnostics=_strings(
            document["diagnostics"],
            field="evidence.diagnostics",
        ),
    )


def _disposition(value: JsonValue) -> ObligationDisposition:
    document = _closed(
        value,
        field="ObligationDisposition",
        schema=OBLIGATION_DISPOSITION_SCHEMA,
        fields=frozenset(
            {"obligation", "outcome", "evidence-digests", "explanation"}
        ),
    )
    return ObligationDisposition(
        obligation=_obligation(document["obligation"]),
        outcome=_string(document["outcome"], field="disposition.outcome"),
        evidence_digests=_strings(
            document["evidence-digests"],
            field="disposition.evidence-digests",
        ),
        explanation=_string(
            document["explanation"],
            field="disposition.explanation",
        ),
    )


def _qualification_decision(value: JsonValue) -> QualificationDecision:
    document = _closed(
        value,
        field="QualificationDecision",
        schema=QUALIFICATION_DECISION_SCHEMA,
        fields=frozenset(
            {
                "subject",
                "qualification-snapshot-digest",
                "obligation-dispositions",
                "admitted-evidence-digests",
                "admitted-artifact-digests",
                "terminal-result",
                "failure-class",
                "next-action",
            }
        ),
    )
    dispositions = cast(
        "tuple[ObligationDisposition, ...]",
        _records(
            document["obligation-dispositions"],
            field="decision.obligation-dispositions",
            parser=_disposition,
        ),
    )
    return QualificationDecision(
        subject=_subject(document["subject"]),
        qualification_snapshot_digest=_string(
            document["qualification-snapshot-digest"],
            field="decision.qualification-snapshot-digest",
        ),
        obligation_dispositions=dispositions,
        admitted_evidence_digests=_strings(
            document["admitted-evidence-digests"],
            field="decision.admitted-evidence-digests",
        ),
        admitted_artifact_digests=_strings(
            document["admitted-artifact-digests"],
            field="decision.admitted-artifact-digests",
        ),
        terminal_result=_string(
            document["terminal-result"],
            field="decision.terminal-result",
        ),
        failure_class=_string(
            document["failure-class"],
            field="decision.failure-class",
        ),
        next_action=_string(
            document["next-action"],
            field="decision.next-action",
        ),
    )


def _observation_value(value: JsonValue) -> ObservationValue:
    document = _closed(
        value,
        field="ObservationValue",
        schema="workflow-delivery/v3/observation-value",
        fields=frozenset(
            {
                "classification",
                "owner",
                "coordinate",
                "content-sha512",
                "witness-digest",
                "routing",
            }
        ),
    )
    coordinate_value = document["coordinate"]
    coordinate = (
        None if coordinate_value is None else _coordinate(coordinate_value)
    )
    return ObservationValue(
        classification=_string(
            document["classification"],
            field="observation.classification",
        ),
        owner=_nullable_string(document["owner"], field="observation.owner"),
        coordinate=coordinate,
        content_sha512=_nullable_string(
            document["content-sha512"],
            field="observation.content-sha512",
        ),
        witness_digest=_nullable_string(
            document["witness-digest"],
            field="observation.witness-digest",
        ),
        routing=_pairs(document["routing"], field="observation.routing"),
    )


def _observation_request_facts(
    value: JsonValue,
) -> ObservationRequestFacts:
    document = _closed(
        value,
        field="ObservationRequestFacts",
        schema=OBSERVATION_REQUEST_FACTS_SCHEMA,
        fields=frozenset(
            {
                "qualification-snapshot-digest",
                "projection-digest",
                "desired-state-digest",
                "method",
                "url",
                "headers",
            }
        ),
    )
    return ObservationRequestFacts(
        qualification_snapshot_digest=_string(
            document["qualification-snapshot-digest"],
            field="observation request.qualification-snapshot-digest",
        ),
        projection_digest=_string(
            document["projection-digest"],
            field="observation request.projection-digest",
        ),
        desired_state_digest=_string(
            document["desired-state-digest"],
            field="observation request.desired-state-digest",
        ),
        method=_string(
            document["method"],
            field="observation request.method",
        ),
        url=_string(document["url"], field="observation request.url"),
        headers=_pairs(
            document["headers"],
            field="observation request.headers",
        ),
    )


def _observation_response_facts(
    value: JsonValue,
) -> ObservationResponseFacts:
    document = _closed(
        value,
        field="ObservationResponseFacts",
        schema=OBSERVATION_RESPONSE_FACTS_SCHEMA,
        fields=frozenset(
            {
                "stage",
                "requested-url",
                "final-url",
                "redirects",
                "status",
                "selected-headers",
                "truncated",
                "body-sha256",
                "status-detail",
                "metadata-body-sha256",
                "metadata-package",
                "metadata-version",
                "dist-tarball",
                "dist-integrity",
                "tarball-content-sha512",
                "tarball-byte-size",
                "remote-witness-digest",
            }
        ),
    )
    status = document["status"]
    if type(status) is int:
        parsed_status: int | str = _integer(
            status,
            field="observation response.status",
        )
    else:
        parsed_status = _string(
            status,
            field="observation response.status",
        )
    truncated = document["truncated"]
    return ObservationResponseFacts(
        stage=_string(document["stage"], field="observation response.stage"),
        requested_url=_string(
            document["requested-url"],
            field="observation response.requested-url",
        ),
        final_url=_nullable_string(
            document["final-url"],
            field="observation response.final-url",
        ),
        redirects=_strings(
            document["redirects"],
            field="observation response.redirects",
        ),
        status=parsed_status,
        selected_headers=_pairs(
            document["selected-headers"],
            field="observation response.selected-headers",
        ),
        truncated=(
            None
            if truncated is None
            else _boolean(
                truncated,
                field="observation response.truncated",
            )
        ),
        body_sha256=_nullable_string(
            document["body-sha256"],
            field="observation response.body-sha256",
        ),
        status_detail=_nullable_string(
            document["status-detail"],
            field="observation response.status-detail",
        ),
        metadata_body_sha256=_nullable_string(
            document["metadata-body-sha256"],
            field="observation response.metadata-body-sha256",
        ),
        metadata_package=_nullable_string(
            document["metadata-package"],
            field="observation response.metadata-package",
        ),
        metadata_version=_nullable_string(
            document["metadata-version"],
            field="observation response.metadata-version",
        ),
        dist_tarball=_nullable_string(
            document["dist-tarball"],
            field="observation response.dist-tarball",
        ),
        dist_integrity=_nullable_string(
            document["dist-integrity"],
            field="observation response.dist-integrity",
        ),
        tarball_content_sha512=_nullable_string(
            document["tarball-content-sha512"],
            field="observation response.tarball-content-sha512",
        ),
        tarball_byte_size=(
            None
            if document["tarball-byte-size"] is None
            else _integer(
                document["tarball-byte-size"],
                field="observation response.tarball-byte-size",
            )
        ),
        remote_witness_digest=_nullable_string(
            document["remote-witness-digest"],
            field="observation response.remote-witness-digest",
        ),
    )


def _projection_observation(value: JsonValue) -> ProjectionObservation:
    document = _closed(
        value,
        field="ProjectionObservation",
        schema=PROJECTION_OBSERVATION_SCHEMA,
        fields=frozenset(
            {
                "subject",
                "purpose",
                "target",
                "producer",
                "qualification-snapshot-digest",
                "projection",
                "desired-state-digest",
                "observation-contract-id",
                "request-facts",
                "request-digest",
                "response-facts",
                "response-digest",
                "value",
            }
        ),
    )
    return ProjectionObservation(
        subject=_simulation_identity(document["subject"]),
        purpose=_string(document["purpose"], field="observation.purpose"),
        target=_string(document["target"], field="observation.target"),
        producer=_string(document["producer"], field="observation.producer"),
        qualification_snapshot_digest=_string(
            document["qualification-snapshot-digest"],
            field="observation.qualification-snapshot-digest",
        ),
        projection=_projection(document["projection"]),
        desired_state_digest=_string(
            document["desired-state-digest"],
            field="observation.desired-state-digest",
        ),
        observation_contract_id=_string(
            document["observation-contract-id"],
            field="observation.observation-contract-id",
        ),
        request_facts=_observation_request_facts(document["request-facts"]),
        request_digest=_string(
            document["request-digest"],
            field="observation.request-digest",
        ),
        response_facts=_observation_response_facts(document["response-facts"]),
        response_digest=_string(
            document["response-digest"],
            field="observation.response-digest",
        ),
        value=_observation_value(document["value"]),
    )


def _remote_state_observation(value: JsonValue) -> RemoteStateObservation:
    field = "Remote-State Observation"
    document = _closed(
        value,
        field=field,
        schema=REMOTE_STATE_OBSERVATION_SCHEMA,
        fields=frozenset(
            {
                "attempt",
                "qualification-decision-reference",
                "desired-subject",
                "desired-version",
                "desired-content-sha256",
                "desired-content-sha512",
                "desired-witness-digest",
                "classification",
                "package-control",
                "active-readback",
                "response-identity",
                "diagnostics",
                "producer",
                "control",
                "workflow-run-id",
            }
        ),
    )
    return RemoteStateObservation(
        attempt=_release_attempt(document["attempt"]),
        qualification_decision_reference=_artifact_reference(
            document["qualification-decision-reference"]
        ),
        desired_subject=_package_control_subject(document["desired-subject"]),
        desired_version=_string(
            document["desired-version"],
            field=f"{field}.desired-version",
        ),
        desired_content_sha256=_string(
            document["desired-content-sha256"],
            field=f"{field}.desired-content-sha256",
        ),
        desired_content_sha512=_string(
            document["desired-content-sha512"],
            field=f"{field}.desired-content-sha512",
        ),
        desired_witness_digest=_string(
            document["desired-witness-digest"],
            field=f"{field}.desired-witness-digest",
        ),
        classification=_string(
            document["classification"],
            field=f"{field}.classification",
        ),
        package_control=(
            None
            if document["package-control"] is None
            else _package_control_proof(document["package-control"])
        ),
        active_readback=(
            None
            if document["active-readback"] is None
            else _destination_readback(document["active-readback"])
        ),
        response_identity=_nullable_string(
            document["response-identity"],
            field=f"{field}.response-identity",
        ),
        diagnostics=_publication_diagnostics(document["diagnostics"]),
        producer=_string(document["producer"], field=f"{field}.producer"),
        control=_string(document["control"], field=f"{field}.control"),
        workflow_run_id=_integer(
            document["workflow-run-id"],
            field=f"{field}.workflow-run-id",
        ),
    )


def _hypothetical_action(value: JsonValue) -> HypotheticalAction:
    document = _closed(
        value,
        field="HypotheticalAction",
        schema=HYPOTHETICAL_ACTION_SCHEMA,
        fields=frozenset(
            {
                "simulation",
                "qualification-snapshot-digest",
                "qualification-decision-digest",
                "projection-id",
                "potential-action",
                "artifact-digest",
                "mutable-resource-keys",
                "capability-requirements",
            }
        ),
    )
    return HypotheticalAction(
        simulation=_simulation_identity(document["simulation"]),
        qualification_snapshot_digest=_string(
            document["qualification-snapshot-digest"],
            field="action.qualification-snapshot-digest",
        ),
        qualification_decision_digest=_string(
            document["qualification-decision-digest"],
            field="action.qualification-decision-digest",
        ),
        projection_id=_string(
            document["projection-id"],
            field="action.projection-id",
        ),
        potential_action=_potential_action(document["potential-action"]),
        artifact_digest=_string(
            document["artifact-digest"],
            field="action.artifact-digest",
        ),
        mutable_resource_keys=_strings(
            document["mutable-resource-keys"],
            field="action.mutable-resource-keys",
        ),
        capability_requirements=_strings(
            document["capability-requirements"],
            field="action.capability-requirements",
        ),
    )


def _publication_action(value: JsonValue) -> PublicationAction:
    document = _closed(
        value,
        field="publication action",
        schema=PUBLICATION_ACTION_SCHEMA,
        fields=frozenset(
            {
                "action-id",
                "destination-operation-profile-digest",
                "package",
                "version",
                "tarball-reference",
                "tag",
                "mutable-resource-keys",
                "serialization-projection",
            }
        ),
    )
    return PublicationAction(
        action_id=_string(
            document["action-id"],
            field="publication action.action-id",
        ),
        destination_operation_profile_digest=_string(
            document["destination-operation-profile-digest"],
            field=("publication action.destination-operation-profile-digest"),
        ),
        package=_string(
            document["package"],
            field="publication action.package",
        ),
        version=_string(
            document["version"],
            field="publication action.version",
        ),
        tarball_reference=artifact_reference_from_document(
            document["tarball-reference"]
        ),
        tag=_string(
            document["tag"],
            field="publication action.tag",
        ),
        mutable_resource_keys=_strings(
            document["mutable-resource-keys"],
            field="publication action.mutable-resource-keys",
        ),
        serialization_projection=_string(
            document["serialization-projection"],
            field="publication action.serialization-projection",
        ),
    )


def _publication_observation_reference(
    value: JsonValue,
) -> PublicationObservationReference:
    document = _closed(
        value,
        field="publication observation reference",
        schema=PUBLICATION_OBSERVATION_REFERENCE_SCHEMA,
        fields=frozenset(
            {"projection-id", "observation-digest", "classification"}
        ),
    )
    return PublicationObservationReference(
        projection_id=_string(
            document["projection-id"],
            field="publication observation reference.projection-id",
        ),
        observation_digest=_string(
            document["observation-digest"],
            field="publication observation reference.observation-digest",
        ),
        classification=_string(
            document["classification"],
            field="publication observation reference.classification",
        ),
    )


def _publication_snapshot(value: JsonValue) -> PublicationSnapshot:
    document = _closed(
        value,
        field="publication snapshot",
        schema=PUBLICATION_SNAPSHOT_SCHEMA,
        fields=frozenset(
            {
                "attempt",
                "qualification-snapshot-digest",
                "qualification-decision-digest",
                "qualification-result",
                "projection-ids",
                "artifact-digests",
                "artifact-output-ids",
                "observation-references",
                "materialized-actions",
            }
        ),
    )
    return PublicationSnapshot(
        attempt=_release_attempt(document["attempt"]),
        qualification_snapshot_digest=_string(
            document["qualification-snapshot-digest"],
            field="publication snapshot.qualification-snapshot-digest",
        ),
        qualification_decision_digest=_string(
            document["qualification-decision-digest"],
            field="publication snapshot.qualification-decision-digest",
        ),
        qualification_result=_string(
            document["qualification-result"],
            field="publication snapshot.qualification-result",
        ),
        projection_ids=_strings(
            document["projection-ids"],
            field="publication snapshot.projection-ids",
        ),
        artifact_digests=_strings(
            document["artifact-digests"],
            field="publication snapshot.artifact-digests",
        ),
        artifact_output_ids=_strings(
            document["artifact-output-ids"],
            field="publication snapshot.artifact-output-ids",
        ),
        observation_references=tuple(
            _publication_observation_reference(item)
            for item in _array(
                document["observation-references"],
                field="publication snapshot.observation-references",
            )
        ),
        materialized_actions=tuple(
            _publication_action(item)
            for item in _array(
                document["materialized-actions"],
                field="publication snapshot.materialized-actions",
            )
        ),
    )


def _simulation_outcome(value: JsonValue) -> SimulationOutcome:
    document = _closed(
        value,
        field="SimulationOutcome",
        schema=SIMULATION_OUTCOME_SCHEMA,
        fields=frozenset(
            {
                "binding",
                "qualification-snapshot-digest",
                "qualification-decision-digest",
                "observation-digests",
                "hypothetical-actions",
                "terminal-result",
                "failure-class",
                "next-action",
            }
        ),
    )
    actions = cast(
        "tuple[HypotheticalAction, ...]",
        _records(
            document["hypothetical-actions"],
            field="outcome.hypothetical-actions",
            parser=_hypothetical_action,
        ),
    )
    return SimulationOutcome(
        binding=_simulation_binding(document["binding"]),
        qualification_snapshot_digest=_string(
            document["qualification-snapshot-digest"],
            field="outcome.qualification-snapshot-digest",
        ),
        qualification_decision_digest=_string(
            document["qualification-decision-digest"],
            field="outcome.qualification-decision-digest",
        ),
        observation_digests=_strings(
            document["observation-digests"],
            field="outcome.observation-digests",
        ),
        hypothetical_actions=actions,
        terminal_result=_string(
            document["terminal-result"],
            field="outcome.terminal-result",
        ),
        failure_class=_string(
            document["failure-class"],
            field="outcome.failure-class",
        ),
        next_action=_string(
            document["next-action"],
            field="outcome.next-action",
        ),
    )


def _release_attempt_binding(value: JsonValue) -> ReleaseAttemptBinding:
    document = _closed(
        value,
        field="release attempt binding",
        schema=RELEASE_ATTEMPT_BINDING_SCHEMA,
        fields=frozenset(
            {
                "intent-digest",
                "request-id",
                "execution",
                "attempt",
                "repository-model-digest",
                "live-eligibility-artifact-id",
                "live-eligibility-artifact-digest",
                "live-eligibility-payload-digest",
                "attestation-provenance",
            }
        ),
    )
    return ReleaseAttemptBinding(
        intent_digest=_string(
            document["intent-digest"],
            field="attempt binding.intent-digest",
        ),
        request_id=_string(
            document["request-id"],
            field="attempt binding.request-id",
        ),
        execution=_buddy_execution(document["execution"]),
        attempt=_release_attempt(document["attempt"]),
        repository_model_digest=_string(
            document["repository-model-digest"],
            field="attempt binding.repository-model-digest",
        ),
        live_eligibility_artifact_id=_integer(
            document["live-eligibility-artifact-id"],
            field="attempt binding.live-eligibility-artifact-id",
        ),
        live_eligibility_artifact_digest=_string(
            document["live-eligibility-artifact-digest"],
            field="attempt binding.live-eligibility-artifact-digest",
        ),
        live_eligibility_payload_digest=_string(
            document["live-eligibility-payload-digest"],
            field="attempt binding.live-eligibility-payload-digest",
        ),
        attestation_provenance=_pairs(
            document["attestation-provenance"],
            field="attempt binding.attestation-provenance",
        ),
    )


def _approval_bundle(value: JsonValue) -> ApprovalBundle:
    document = _closed(
        value,
        field="approval bundle",
        schema=APPROVAL_BUNDLE_SCHEMA,
        fields=frozenset(
            {
                "attempt",
                "publication-snapshot-reference",
                "reviewer-summary-reference",
                "producer",
                "control",
                "workflow-run-id",
            }
        ),
    )
    return ApprovalBundle(
        attempt=_release_attempt(document["attempt"]),
        publication_snapshot_reference=artifact_reference_from_document(
            document["publication-snapshot-reference"]
        ),
        reviewer_summary_reference=artifact_reference_from_document(
            document["reviewer-summary-reference"]
        ),
        producer=_string(
            document["producer"],
            field="approval bundle.producer",
        ),
        control=_string(
            document["control"],
            field="approval bundle.control",
        ),
        workflow_run_id=_integer(
            document["workflow-run-id"],
            field="approval bundle.workflow-run-id",
        ),
    )


def _publication_authorization(
    value: JsonValue,
) -> PublicationAuthorization:
    document = _closed(
        value,
        field="publication authorization",
        schema=PUBLICATION_AUTHORIZATION_SCHEMA,
        fields=frozenset(
            {
                "attempt",
                "approval-bundle-reference",
                "approval-boundary",
                "governance-proof",
                "completed-at",
                "producer",
                "control",
                "workflow-run-id",
            }
        ),
    )
    return PublicationAuthorization(
        attempt=_release_attempt(document["attempt"]),
        approval_bundle_reference=artifact_reference_from_document(
            document["approval-bundle-reference"]
        ),
        approval_boundary=_approval_boundary(document["approval-boundary"]),
        governance_proof=_governance_proof(document["governance-proof"]),
        completed_at=_string(
            document["completed-at"],
            field="publication authorization.completed-at",
        ),
        producer=_string(
            document["producer"],
            field="publication authorization.producer",
        ),
        control=_string(
            document["control"],
            field="publication authorization.control",
        ),
        workflow_run_id=_integer(
            document["workflow-run-id"],
            field="publication authorization.workflow-run-id",
        ),
    )


def _mutation_may_have_started_marker(
    value: JsonValue,
) -> MutationMayHaveStartedMarker:
    document = _closed(
        value,
        field="mutation-may-have-started marker",
        schema=MUTATION_MAY_HAVE_STARTED_SCHEMA,
        fields=frozenset(
            {
                "attempt",
                "publication-authorization-reference",
                "governance-proof",
                "package-control-proof",
                "profile-match",
                "producer",
                "control",
                "workflow-run-id",
            }
        ),
    )
    return MutationMayHaveStartedMarker(
        attempt=_release_attempt(document["attempt"]),
        publication_authorization_reference=_artifact_reference(
            document["publication-authorization-reference"]
        ),
        governance_proof=_governance_proof(document["governance-proof"]),
        package_control_proof=_package_control_proof(
            document["package-control-proof"]
        ),
        profile_match=_profile_match(document["profile-match"]),
        producer=_string(
            document["producer"],
            field="mutation marker.producer",
        ),
        control=_string(
            document["control"],
            field="mutation marker.control",
        ),
        workflow_run_id=_integer(
            document["workflow-run-id"],
            field="mutation marker.workflow-run-id",
        ),
    )


def _publication_result(value: JsonValue) -> PublicationResult:
    document = _closed(
        value,
        field="Publication Result",
        schema=PUBLICATION_RESULT_SCHEMA,
        fields=frozenset(
            {
                "attempt",
                "mutation-marker-reference",
                "command-classification",
                "post-action-readback",
                "result",
                "mutation-classification",
                "response-identity",
                "diagnostics",
                "producer",
                "control",
                "workflow-run-id",
            }
        ),
    )
    return PublicationResult(
        attempt=_release_attempt(document["attempt"]),
        mutation_marker_reference=_artifact_reference(
            document["mutation-marker-reference"]
        ),
        command_classification=_string(
            document["command-classification"],
            field="Publication Result.command-classification",
        ),
        post_action_readback=(
            None
            if document["post-action-readback"] is None
            else _destination_readback(document["post-action-readback"])
        ),
        result=_string(
            document["result"],
            field="Publication Result.result",
        ),
        mutation_classification=_string(
            document["mutation-classification"],
            field="Publication Result.mutation-classification",
        ),
        response_identity=_nullable_string(
            document["response-identity"],
            field="Publication Result.response-identity",
        ),
        diagnostics=_publication_diagnostics(document["diagnostics"]),
        producer=_string(
            document["producer"],
            field="Publication Result.producer",
        ),
        control=_string(
            document["control"],
            field="Publication Result.control",
        ),
        workflow_run_id=_integer(
            document["workflow-run-id"],
            field="Publication Result.workflow-run-id",
        ),
    )


def _exact_satisfied_finalization_proof(
    value: JsonValue,
) -> ExactSatisfiedFinalizationProof:
    document = _closed(
        value,
        field="exact-satisfied finalization proof",
        schema=EXACT_SATISFIED_FINALIZATION_PROOF_SCHEMA,
        fields=frozenset(
            {
                "attempt",
                "publication-snapshot-reference",
                "governance-proof",
                "package-control-proof",
                "exact-version-readback",
                "proved-at",
                "producer",
                "control",
                "workflow-run-id",
            }
        ),
    )
    return ExactSatisfiedFinalizationProof(
        attempt=_release_attempt(document["attempt"]),
        publication_snapshot_reference=_artifact_reference(
            document["publication-snapshot-reference"]
        ),
        governance_proof=_governance_proof(document["governance-proof"]),
        package_control_proof=_package_control_proof(
            document["package-control-proof"]
        ),
        exact_version_readback=_destination_readback(
            document["exact-version-readback"]
        ),
        proved_at=_string(
            document["proved-at"],
            field="exact-satisfied finalization proof.proved-at",
        ),
        producer=_string(
            document["producer"],
            field="exact-satisfied finalization proof.producer",
        ),
        control=_string(
            document["control"],
            field="exact-satisfied finalization proof.control",
        ),
        workflow_run_id=_integer(
            document["workflow-run-id"],
            field="exact-satisfied finalization proof.workflow-run-id",
        ),
    )


def _attempt_outcome(value: JsonValue) -> AttemptOutcome:
    document = _closed(
        value,
        field="attempt outcome",
        schema=ATTEMPT_OUTCOME_SCHEMA,
        fields=frozenset(
            {
                "attempt",
                "disposition",
                "possibly-mutated",
                "direct-predecessor",
                "producer",
                "control",
                "workflow-run-id",
            }
        ),
    )
    predecessor = document["direct-predecessor"]
    if type(predecessor) is not dict or set(predecessor) != {
        "kind",
        "reference",
    }:
        message = "Attempt Outcome requires one closed direct predecessor"
        raise ValueError(message)
    return AttemptOutcome(
        attempt=_release_attempt(document["attempt"]),
        disposition=_string(
            document["disposition"], field="attempt outcome.disposition"
        ),
        possibly_mutated=_boolean(
            document["possibly-mutated"],
            field="attempt outcome.possibly-mutated",
        ),
        direct_predecessor=DirectPredecessor(
            kind=_string(predecessor["kind"], field="direct predecessor.kind"),
            reference=artifact_reference_from_document(
                predecessor["reference"]
            ),
        ),
        producer=_string(
            document["producer"], field="attempt outcome.producer"
        ),
        control=_string(document["control"], field="attempt outcome.control"),
        workflow_run_id=_integer(
            document["workflow-run-id"], field="attempt outcome.workflow-run-id"
        ),
    )


_PARSERS: dict[type[object], Callable[[JsonValue], ReleaseRecord]] = {
    ReleaseIntent: _release_intent,
    ReleaseAttemptBinding: _release_attempt_binding,
    SimulationBinding: _simulation_binding,
    QualificationSnapshot: _qualification_snapshot,
    ReleaseArtifact: _release_artifact,
    QualificationEvidence: _qualification_evidence,
    QualificationDecision: _qualification_decision,
    ProjectionObservation: _projection_observation,
    RemoteStateObservation: _remote_state_observation,
    HypotheticalAction: _hypothetical_action,
    PublicationAction: _publication_action,
    PublicationSnapshot: _publication_snapshot,
    SimulationOutcome: _simulation_outcome,
    ApprovalBundle: _approval_bundle,
    PublicationAuthorization: _publication_authorization,
    MutationMayHaveStartedMarker: _mutation_may_have_started_marker,
    PublicationResult: _publication_result,
    ExactSatisfiedFinalizationProof: _exact_satisfied_finalization_proof,
    AttemptOutcome: _attempt_outcome,
}


def release_record_from_document(
    document: dict[str, JsonValue],
    *,
    expected_type: type[ReleaseRecord],
) -> ReleaseRecord:
    """Deserialize one closed Release record selected by the trusted caller."""
    parser = _PARSERS.get(expected_type)
    if parser is None:
        message = f"unsupported transported Release record: {expected_type!r}"
        raise ValueError(message)
    record = parser(document)
    if type(record) is not expected_type:
        message = "Release record deserializer returned the wrong runtime type"
        raise TypeError(message)
    if record.to_document() != document:
        message = "Release record is not normalized"
        raise ValueError(message)
    return record


def simulation_identity_from_document(
    document: dict[str, JsonValue],
) -> SimulationIdentity:
    """Deserialize one closed Simulation Identity."""
    identity = _simulation_identity(document)
    if identity.to_document() != document:
        message = "Simulation Identity is not normalized"
        raise ValueError(message)
    return identity


def _record_bindings(  # noqa: C901, PLR0911, PLR0912
    record: ReleaseRecord,
) -> tuple[str, int, int | None, str, str | None]:
    if isinstance(record, ReleaseIntent):
        return (
            record.purpose,
            record.workflow_run_id,
            None,
            record.target,
            None,
        )
    if isinstance(record, ReleaseAttemptBinding):
        attempt = record.attempt
        return (
            "live-release",
            attempt.workflow_run_id,
            None,
            record.execution.target,
            None,
        )
    if isinstance(record, SimulationBinding):
        return (
            record.purpose,
            record.simulation.workflow_run_id,
            record.simulation.run_attempt,
            record.target,
            None,
        )
    if isinstance(record, QualificationSnapshot):
        subject = (
            record.subject.simulation
            if isinstance(record.subject, SimulationBinding)
            else record.subject
        )
        purpose = (
            record.subject.purpose
            if isinstance(record.subject, SimulationBinding)
            else "live-release"
        )
        return (
            purpose,
            subject.workflow_run_id,
            (
                subject.run_attempt
                if isinstance(subject, SimulationIdentity)
                else None
            ),
            record.target,
            None,
        )
    if isinstance(record, ReleaseArtifact):
        return (
            record.purpose,
            record.transport.workflow_run_id,
            record.transport.run_attempt,
            record.target,
            record.transport.producer,
        )
    if isinstance(record, QualificationEvidence):
        purpose = (
            "release-simulation"
            if isinstance(record.subject, SimulationIdentity)
            else "live-release"
        )
        return (
            purpose,
            record.workflow_run_id,
            record.run_attempt,
            record.obligation.target,
            record.producer,
        )
    if isinstance(record, QualificationDecision):
        purpose = (
            "release-simulation"
            if isinstance(record.subject, SimulationIdentity)
            else "live-release"
        )
        return (
            purpose,
            record.subject.workflow_run_id,
            (
                record.subject.run_attempt
                if isinstance(record.subject, SimulationIdentity)
                else None
            ),
            record.obligation_dispositions[0].obligation.target,
            None,
        )
    if isinstance(record, ProjectionObservation):
        return (
            record.purpose,
            record.subject.workflow_run_id,
            (
                record.subject.run_attempt
                if isinstance(record.subject, SimulationIdentity)
                else None
            ),
            record.target,
            record.producer,
        )
    if isinstance(record, PublicationAction):
        message = "Publication Action has no standalone current bindings"
        raise ValueError(message)  # noqa: TRY004
    if isinstance(record, PublicationSnapshot):
        return (
            "live-release",
            record.attempt.workflow_run_id,
            None,
            record.attempt.execution.target,
            None,
        )
    if isinstance(record, ApprovalBundle):
        return (
            "live-release",
            record.attempt.workflow_run_id,
            None,
            record.attempt.execution.target,
            record.producer,
        )
    if isinstance(record, PublicationAuthorization):
        return (
            "live-release",
            record.attempt.workflow_run_id,
            None,
            record.attempt.execution.target,
            record.producer,
        )
    if isinstance(
        record,
        RemoteStateObservation
        | MutationMayHaveStartedMarker
        | PublicationResult,
    ):
        return (
            "live-release",
            record.workflow_run_id,
            None,
            record.attempt.execution.target,
            record.producer,
        )
    if isinstance(record, ExactSatisfiedFinalizationProof):
        return (
            "live-release",
            record.workflow_run_id,
            None,
            record.attempt.execution.target,
            record.producer,
        )
    if isinstance(record, AttemptOutcome):
        return (
            "live-release",
            record.attempt.workflow_run_id,
            None,
            record.attempt.execution.target,
            record.producer,
        )
    if isinstance(record, SimulationOutcome):
        simulation = record.binding.simulation
        return (
            record.binding.purpose,
            simulation.workflow_run_id,
            simulation.run_attempt,
            record.binding.target,
            None,
        )
    message = f"unsupported Release record bindings: {type(record)!r}"
    raise ValueError(message)


def validate_release_admission_bindings(
    record: ReleaseRecord,
    expected: ReleaseAdmissionBindings,
) -> None:
    """Reject payload-selected current authority facts."""
    purpose, workflow_run_id, run_attempt, target, producer = _record_bindings(
        record
    )
    checks = (
        ("purpose", purpose, expected.purpose),
        ("workflow_run_id", workflow_run_id, expected.workflow_run_id),
        ("target", target, expected.target),
    )
    for field, actual, wanted in checks:
        if actual != wanted:
            message = f"Release record current binding mismatch: {field}"
            raise ValueError(message)
    if not isinstance(record, ReleaseIntent) and (
        run_attempt != expected.run_attempt
    ):
        message = "Release record current binding mismatch: run_attempt"
        raise ValueError(message)
    if expected.producer is not None and producer != expected.producer:
        message = "Release record current binding mismatch: producer"
        raise ValueError(message)


__all__ = [
    "ReleaseAdmissionBindings",
    "release_record_from_document",
    "simulation_identity_from_document",
    "validate_release_admission_bindings",
]
