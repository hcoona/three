"""Purpose-bound Repository Model compiler for the first v3 slice."""

from __future__ import annotations

import hashlib
import re
import subprocess
from dataclasses import dataclass
from pathlib import Path
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3.canonical import (
    canonical_sha256,
    parse_canonical_json,
)
from three_workflow_delivery_v3.catalogs import (
    BUILD_DEFINITIONS,
    QUALITY_PRESETS,
    catalog_digest,
)
from three_workflow_delivery_v3.repository.descriptors import (
    FIRST_SLICE_PACKAGE,
    FIRST_SLICE_POLICY_PATH,
    FIRST_SLICE_RELEASE_UNIT,
    MissingFirstSliceAuthoringError,
    ReleasePolicy,
    load_first_slice_authoring,
)
from three_workflow_delivery_v3.repository.node_provider import (
    AUTHORITATIVE_REMOTE,
    FIRST_SLICE_REQUIRED_GLOBAL_INPUTS,
    NODE_PROVIDER_FACT_BUNDLE_SCHEMA,
    PROVIDER_EXECUTION_CLASS,
    PROVIDER_EXECUTION_MODE,
    PROVIDER_IMPLEMENTATION_ID,
    PROVIDER_LOGICAL_ID,
    TAG_REFSPEC,
    GlobalInput,
    NbgvFacts,
    NodeProviderFactBundle,
    NodeProviderResult,
    ProjectNode,
    ProviderBinding,
    node_provider_version_input_candidates,
    validate_nbgv_facts,
    validate_node_provider_result,
    validate_project_node,
    validate_provider_binding,
    validate_provider_toolchain,
)

if TYPE_CHECKING:
    from collections.abc import Sequence

    from three_workflow_delivery_v3.canonical import JsonValue

_SHA_PATTERN = re.compile(r"[0-9a-f]{40}\Z")
_DIGEST_PATTERN = re.compile(r"sha256:[0-9a-f]{64}\Z")
_REVERSE_INDEX_ENTRY_FIELD_COUNT = 2
_CHANNEL_ENTRY_FIELD_COUNT = 2
_PURPOSES = frozenset(
    {
        "ci-pr-slice-shadow",
        "slice-validation",
        "live-release",
        "release-simulation",
    }
)


@dataclass(frozen=True, slots=True)
class CompilationContext:
    """Authority binding for one request-local Snapshot compilation."""

    request_id: str
    purpose: str
    workflow_run_id: int
    run_attempt: int | None
    target: str
    producer: str
    control: str
    catalog_digest: str
    channel: str | None = None
    release_unit: str | None = None


@dataclass(frozen=True, slots=True)
class ProviderRequest:
    """One closed target-evaluating Provider request."""

    entry_id: str
    provider_logical_id: str
    provider_implementation_id: str
    execution_mode: str
    producer: str
    request_digest: str
    expected_result_identity: str

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical manifest entry."""
        return {
            "entry-id": self.entry_id,
            "provider-logical-id": self.provider_logical_id,
            "provider-implementation-id": self.provider_implementation_id,
            "execution-mode": self.execution_mode,
            "producer": self.producer,
            "request-digest": self.request_digest,
            "expected-result-identity": self.expected_result_identity,
        }


@dataclass(frozen=True, slots=True)
class ProviderRequestManifest:
    """Closed set of Provider requests for one compilation."""

    context: CompilationContext
    requests: tuple[ProviderRequest, ...]

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical manifest payload."""
        return {
            "schema": "workflow-delivery/v3/provider-request-manifest",
            "context": _context_document(self.context),
            "requests": [
                request.to_document()
                for request in sorted(
                    self.requests,
                    key=lambda item: item.entry_id,
                )
            ],
        }

    @property
    def manifest_digest(self) -> str:
        """Return the closed manifest digest."""
        return canonical_sha256(self.to_document())


@dataclass(frozen=True, slots=True)
class FactBundleAdmissionContext:
    """Trusted artifact and canonical payload identities for one Bundle."""

    request_artifact_id: int
    request_artifact_digest: str
    transport_id: int
    transport_digest: str
    bundle_digest: str


@dataclass(frozen=True, slots=True)
class AdmittedNodeProviderFactBundle:
    """Strictly admitted target-evaluating Provider input for compilation."""

    bundle: NodeProviderFactBundle
    admission: FactBundleAdmissionContext

    @property
    def provider_result(self) -> NodeProviderResult:
        """Return the admitted Provider Result payload."""
        return self.bundle.provider_result


@dataclass(frozen=True, slots=True)
class CompiledOutput:
    """One output identity in the closed artifact scope."""

    output_id: str
    role: str
    kind: str


@dataclass(frozen=True, slots=True)
class CompiledBuild:
    """One closed Build Definition selection."""

    build_id: str
    definition: str
    project_id: str
    entry_point: str
    outputs: tuple[CompiledOutput, ...]
    required_native_projections: tuple[str, ...]


@dataclass(frozen=True, slots=True)
class CompiledReleaseUnit:
    """One complete Release Unit build and artifact scope."""

    release_unit: str
    descriptor_path: str
    builds: tuple[CompiledBuild, ...]


@dataclass(frozen=True, slots=True)
class CompiledQualitySelection:
    """Expanded static Quality preset for one ecosystem."""

    path: str
    ecosystem: str
    preset: str
    required: tuple[str, ...]
    advisory: tuple[str, ...]


@dataclass(frozen=True, slots=True)
class CompiledGovernanceSource:
    """Compiled immutable Governance source from target Release policy."""

    repository: str
    ref: str
    path: str
    max_age_days: int

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical compiled Governance source."""
        return {
            "repository": self.repository,
            "ref": self.ref,
            "path": self.path,
            "max-age-days": self.max_age_days,
        }


@dataclass(frozen=True, slots=True)
class CompiledProjection:
    """Compiled immutable channel projection."""

    destination: str
    artifact: str
    package: str

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical compiled projection."""
        return {
            "destination": self.destination,
            "artifact": self.artifact,
            "package": self.package,
        }


@dataclass(frozen=True, slots=True)
class CompiledChannelPolicy:
    """Compiled immutable channel quality and projection closure."""

    quality: tuple[str, ...]
    projections: tuple[CompiledProjection, ...]

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical compiled channel policy."""
        return cast(
            "dict[str, JsonValue]",
            {
                "quality": list(self.quality),
                "projections": [
                    projection.to_document() for projection in self.projections
                ],
            },
        )


@dataclass(frozen=True, slots=True)
class CompiledReleasePolicy:
    """Complete target-authored Release policy frozen into the Snapshot."""

    path: str
    release_unit: str
    governance: CompiledGovernanceSource
    channels: tuple[tuple[str, CompiledChannelPolicy], ...]

    def channel(self, name: str) -> CompiledChannelPolicy:
        """Return one compiled channel policy."""
        for channel_name, policy in self.channels:
            if channel_name == name:
                return policy
        message = f"compiled Release policy has no channel: {name}"
        raise ValueError(message)

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical complete compiled Release policy."""
        return {
            "schema": "workflow-delivery/v3/compiled-release-policy",
            "path": self.path,
            "release-unit": self.release_unit,
            "governance": self.governance.to_document(),
            "channels": {
                name: channel.to_document() for name, channel in self.channels
            },
        }

    @property
    def policy_digest(self) -> str:
        """Return the canonical compiled Release policy digest."""
        return canonical_sha256(self.to_document())


@dataclass(frozen=True, slots=True)
class RepositoryModelSnapshot:
    """Immutable request-local Repository Model Snapshot."""

    context: CompilationContext
    manifest_digest: str
    provider_result_digests: tuple[str, ...]
    project_nodes: tuple[ProjectNode, ...]
    release_units: tuple[CompiledReleaseUnit, ...]
    quality: tuple[CompiledQualitySelection, ...]
    release_policy_path: str
    release_policy: CompiledReleasePolicy | None
    nbgv: NbgvFacts
    reverse_index: tuple[tuple[str, tuple[str, ...]], ...]
    unresolved: tuple[str, ...]
    ready: bool

    def to_document(self) -> dict[str, JsonValue]:
        """Return the complete canonical Snapshot payload."""
        provider_result_digests: list[JsonValue] = list(
            self.provider_result_digests
        )
        projects: list[JsonValue] = []
        for project in self.project_nodes:
            workspace_dependencies: list[JsonValue] = list(
                project.workspace_dependencies
            )
            project_document: dict[str, JsonValue] = {
                "project-id": project.project_id,
                "package-name": project.package_name,
                "path": project.path,
                "manifest-path": project.manifest_path,
                "private": project.private,
                "workspace-dependencies": workspace_dependencies,
            }
            projects.append(project_document)

        release_units: list[JsonValue] = []
        for release_unit in self.release_units:
            builds: list[JsonValue] = []
            for build in release_unit.builds:
                outputs: list[JsonValue] = []
                for output in build.outputs:
                    output_document: dict[str, JsonValue] = {
                        "output-id": output.output_id,
                        "role": output.role,
                        "kind": output.kind,
                    }
                    outputs.append(output_document)
                required_projections: list[JsonValue] = list(
                    build.required_native_projections
                )
                build_document: dict[str, JsonValue] = {
                    "build-id": build.build_id,
                    "definition": build.definition,
                    "project-id": build.project_id,
                    "entry-point": build.entry_point,
                    "outputs": outputs,
                    "required-native-projections": required_projections,
                }
                builds.append(build_document)
            release_unit_document: dict[str, JsonValue] = {
                "release-unit": release_unit.release_unit,
                "descriptor-path": release_unit.descriptor_path,
                "builds": builds,
            }
            release_units.append(release_unit_document)

        quality: list[JsonValue] = []
        for selection in self.quality:
            required: list[JsonValue] = list(selection.required)
            advisory: list[JsonValue] = list(selection.advisory)
            selection_document: dict[str, JsonValue] = {
                "path": selection.path,
                "ecosystem": selection.ecosystem,
                "preset": selection.preset,
                "required": required,
                "advisory": advisory,
            }
            quality.append(selection_document)

        reverse_index: dict[str, JsonValue] = {}
        for project_id, reverse_build_ids in self.reverse_index:
            build_ids: list[JsonValue] = []
            for build_id in reverse_build_ids:
                build_ids.append(build_id)
            reverse_index[project_id] = build_ids
        unresolved: list[JsonValue] = list(self.unresolved)
        return {
            "schema": "workflow-delivery/v3/repository-model-snapshot",
            "context": _context_document(self.context),
            "provider-request-manifest-digest": self.manifest_digest,
            "provider-result-digests": provider_result_digests,
            "project-nodes": projects,
            "release-units": release_units,
            "quality": quality,
            "release-policy-path": self.release_policy_path,
            "release-policy": (
                None
                if self.release_policy is None
                else self.release_policy.to_document()
            ),
            "nbgv": self.nbgv.to_document(),
            "reverse-index": reverse_index,
            "unresolved": unresolved,
            "ready": self.ready,
        }

    @property
    def snapshot_digest(self) -> str:
        """Return the complete canonical Snapshot digest."""
        return canonical_sha256(self.to_document())


@dataclass(frozen=True, slots=True)
class AdmittedRepositoryModelSnapshot:
    """Current-purpose canonical Repository Model transport admission."""

    snapshot: RepositoryModelSnapshot
    canonical_digest: str
    canonical_bytes: bytes

    def __post_init__(self) -> None:
        """Reject forged wrappers that did not pass canonical admission."""
        if type(self.snapshot) is not RepositoryModelSnapshot:
            message = "Repository Model admission has the wrong Snapshot type"
            raise TypeError(message)
        if (
            type(self.canonical_digest) is not str
            or _DIGEST_PATTERN.fullmatch(self.canonical_digest) is None
            or type(self.canonical_bytes) is not bytes
        ):
            message = "Repository Model admission integrity failed"
            raise ValueError(message)
        document = parse_canonical_json(self.canonical_bytes)
        if (
            document != self.snapshot.to_document()
            or self.snapshot.snapshot_digest != self.canonical_digest
        ):
            message = "Repository Model admission integrity failed"
            raise ValueError(message)
        validate_first_slice_repository_model_snapshot(self.snapshot)


def _document(
    value: JsonValue,
    *,
    field: str,
    keys: frozenset[str],
) -> dict[str, JsonValue]:
    if type(value) is not dict:
        message = f"Repository Model Snapshot {field} must be an object"
        raise TypeError(message)
    document = cast("dict[str, JsonValue]", value)
    if document.keys() != keys:
        missing = keys - document.keys()
        if missing:
            message = (
                f"Repository Model Snapshot {field} missing field: "
                f"{sorted(missing)[0]}"
            )
        else:
            unknown = document.keys() - keys
            message = (
                f"Repository Model Snapshot {field} unknown field: "
                f"{sorted(unknown)[0]}"
            )
        raise ValueError(message)
    return document


def _array(value: JsonValue, *, field: str) -> list[JsonValue]:
    if type(value) is not list:
        message = f"Repository Model Snapshot {field} must be an array"
        raise TypeError(message)
    return cast("list[JsonValue]", value)


def _document_string(value: JsonValue, *, field: str) -> str:
    if type(value) is not str:
        message = f"Repository Model Snapshot {field} must be a string"
        raise TypeError(message)
    return value


def _document_integer(value: JsonValue, *, field: str) -> int:
    if type(value) is not int:
        message = f"Repository Model Snapshot {field} must be an integer"
        raise TypeError(message)
    return value


def _document_boolean(value: JsonValue, *, field: str) -> bool:
    if type(value) is not bool:
        message = f"Repository Model Snapshot {field} must be Boolean"
        raise TypeError(message)
    return value


def _document_optional_string(
    value: JsonValue,
    *,
    field: str,
) -> str | None:
    if value is None:
        return None
    return _document_string(value, field=field)


def _string_array(value: JsonValue, *, field: str) -> tuple[str, ...]:
    return tuple(
        _document_string(item, field=f"{field}[{index}]")
        for index, item in enumerate(_array(value, field=field))
    )


def _compilation_context_from_document(
    value: JsonValue,
) -> CompilationContext:
    if type(value) is not dict:
        message = "Repository Model Snapshot context must be an object"
        raise TypeError(message)
    raw_document = cast("dict[str, JsonValue]", value)
    if "purpose" not in raw_document:
        message = "Repository Model Snapshot context missing field: purpose"
        raise ValueError(message)
    purpose = _document_string(
        raw_document["purpose"],
        field="context.purpose",
    )
    if purpose not in _PURPOSES:
        message = "compilation purpose is not in the closed set"
        raise ValueError(message)
    context_keys = {
        "request-id",
        "purpose",
        "workflow-run-id",
        "target",
        "producer",
        "control",
        "catalog-digest",
        "channel",
        "release-unit",
    }
    if purpose != "live-release":
        context_keys.add("run-attempt")
    document = _document(
        value,
        field="context",
        keys=frozenset(context_keys),
    )
    return CompilationContext(
        request_id=_document_string(
            document["request-id"],
            field="context.request-id",
        ),
        purpose=purpose,
        workflow_run_id=_document_integer(
            document["workflow-run-id"],
            field="context.workflow-run-id",
        ),
        run_attempt=(
            None
            if purpose == "live-release"
            else _document_integer(
                document["run-attempt"],
                field="context.run-attempt",
            )
        ),
        target=_document_string(
            document["target"],
            field="context.target",
        ),
        producer=_document_string(
            document["producer"],
            field="context.producer",
        ),
        control=_document_string(
            document["control"],
            field="context.control",
        ),
        catalog_digest=_document_string(
            document["catalog-digest"],
            field="context.catalog-digest",
        ),
        channel=_document_optional_string(
            document["channel"],
            field="context.channel",
        ),
        release_unit=_document_optional_string(
            document["release-unit"],
            field="context.release-unit",
        ),
    )


def _project_node_from_document(value: JsonValue, *, index: int) -> ProjectNode:
    field = f"project-nodes[{index}]"
    document = _document(
        value,
        field=field,
        keys=frozenset(
            {
                "project-id",
                "package-name",
                "path",
                "manifest-path",
                "private",
                "workspace-dependencies",
            }
        ),
    )
    return ProjectNode(
        project_id=_document_string(
            document["project-id"],
            field=f"{field}.project-id",
        ),
        package_name=_document_string(
            document["package-name"],
            field=f"{field}.package-name",
        ),
        path=_document_string(document["path"], field=f"{field}.path"),
        manifest_path=_document_string(
            document["manifest-path"],
            field=f"{field}.manifest-path",
        ),
        private=_document_boolean(
            document["private"],
            field=f"{field}.private",
        ),
        workspace_dependencies=_string_array(
            document["workspace-dependencies"],
            field=f"{field}.workspace-dependencies",
        ),
    )


def _compiled_output_from_document(
    value: JsonValue,
    *,
    field: str,
) -> CompiledOutput:
    document = _document(
        value,
        field=field,
        keys=frozenset({"output-id", "role", "kind"}),
    )
    return CompiledOutput(
        output_id=_document_string(
            document["output-id"],
            field=f"{field}.output-id",
        ),
        role=_document_string(document["role"], field=f"{field}.role"),
        kind=_document_string(document["kind"], field=f"{field}.kind"),
    )


def _compiled_build_from_document(
    value: JsonValue,
    *,
    field: str,
) -> CompiledBuild:
    document = _document(
        value,
        field=field,
        keys=frozenset(
            {
                "build-id",
                "definition",
                "project-id",
                "entry-point",
                "outputs",
                "required-native-projections",
            }
        ),
    )
    return CompiledBuild(
        build_id=_document_string(
            document["build-id"],
            field=f"{field}.build-id",
        ),
        definition=_document_string(
            document["definition"],
            field=f"{field}.definition",
        ),
        project_id=_document_string(
            document["project-id"],
            field=f"{field}.project-id",
        ),
        entry_point=_document_string(
            document["entry-point"],
            field=f"{field}.entry-point",
        ),
        outputs=tuple(
            _compiled_output_from_document(
                output,
                field=f"{field}.outputs[{index}]",
            )
            for index, output in enumerate(
                _array(document["outputs"], field=f"{field}.outputs")
            )
        ),
        required_native_projections=_string_array(
            document["required-native-projections"],
            field=f"{field}.required-native-projections",
        ),
    )


def _compiled_release_unit_from_document(
    value: JsonValue,
    *,
    index: int,
) -> CompiledReleaseUnit:
    field = f"release-units[{index}]"
    document = _document(
        value,
        field=field,
        keys=frozenset({"release-unit", "descriptor-path", "builds"}),
    )
    return CompiledReleaseUnit(
        release_unit=_document_string(
            document["release-unit"],
            field=f"{field}.release-unit",
        ),
        descriptor_path=_document_string(
            document["descriptor-path"],
            field=f"{field}.descriptor-path",
        ),
        builds=tuple(
            _compiled_build_from_document(
                build,
                field=f"{field}.builds[{build_index}]",
            )
            for build_index, build in enumerate(
                _array(document["builds"], field=f"{field}.builds")
            )
        ),
    )


def _compiled_quality_from_document(
    value: JsonValue,
    *,
    index: int,
) -> CompiledQualitySelection:
    field = f"quality[{index}]"
    document = _document(
        value,
        field=field,
        keys=frozenset({"path", "ecosystem", "preset", "required", "advisory"}),
    )
    return CompiledQualitySelection(
        path=_document_string(document["path"], field=f"{field}.path"),
        ecosystem=_document_string(
            document["ecosystem"],
            field=f"{field}.ecosystem",
        ),
        preset=_document_string(document["preset"], field=f"{field}.preset"),
        required=_string_array(
            document["required"],
            field=f"{field}.required",
        ),
        advisory=_string_array(
            document["advisory"],
            field=f"{field}.advisory",
        ),
    )


def _compiled_projection_from_document(
    value: JsonValue,
    *,
    field: str,
) -> CompiledProjection:
    document = _document(
        value,
        field=field,
        keys=frozenset({"destination", "artifact", "package"}),
    )
    return CompiledProjection(
        destination=_document_string(
            document["destination"],
            field=f"{field}.destination",
        ),
        artifact=_document_string(
            document["artifact"],
            field=f"{field}.artifact",
        ),
        package=_document_string(
            document["package"],
            field=f"{field}.package",
        ),
    )


def _compiled_channel_policy_from_document(
    value: JsonValue,
    *,
    field: str,
) -> CompiledChannelPolicy:
    document = _document(
        value,
        field=field,
        keys=frozenset({"quality", "projections"}),
    )
    return CompiledChannelPolicy(
        quality=_string_array(
            document["quality"],
            field=f"{field}.quality",
        ),
        projections=tuple(
            _compiled_projection_from_document(
                projection,
                field=f"{field}.projections[{index}]",
            )
            for index, projection in enumerate(
                _array(
                    document["projections"],
                    field=f"{field}.projections",
                )
            )
        ),
    )


def _compiled_release_policy_from_document(
    value: JsonValue,
) -> CompiledReleasePolicy | None:
    if value is None:
        return None
    document = _document(
        value,
        field="release-policy",
        keys=frozenset(
            {
                "schema",
                "path",
                "release-unit",
                "governance",
                "channels",
            }
        ),
    )
    if document["schema"] != "workflow-delivery/v3/compiled-release-policy":
        message = "Repository Model Snapshot Release policy schema mismatch"
        raise ValueError(message)
    governance = _document(
        document["governance"],
        field="release-policy.governance",
        keys=frozenset({"repository", "ref", "path", "max-age-days"}),
    )
    channels = _document(
        document["channels"],
        field="release-policy.channels",
        keys=frozenset({"buddy", "official"}),
    )
    return CompiledReleasePolicy(
        path=_document_string(
            document["path"],
            field="release-policy.path",
        ),
        release_unit=_document_string(
            document["release-unit"],
            field="release-policy.release-unit",
        ),
        governance=CompiledGovernanceSource(
            repository=_document_string(
                governance["repository"],
                field="release-policy.governance.repository",
            ),
            ref=_document_string(
                governance["ref"],
                field="release-policy.governance.ref",
            ),
            path=_document_string(
                governance["path"],
                field="release-policy.governance.path",
            ),
            max_age_days=_document_integer(
                governance["max-age-days"],
                field="release-policy.governance.max-age-days",
            ),
        ),
        channels=tuple(
            (
                name,
                _compiled_channel_policy_from_document(
                    channels[name],
                    field=f"release-policy.channels.{name}",
                ),
            )
            for name in ("buddy", "official")
        ),
    )


def _nbgv_from_document(value: JsonValue) -> NbgvFacts:
    document = _document(
        value,
        field="nbgv",
        keys=frozenset({"canonical", "native", "node-api-result-digest"}),
    )
    canonical = _document(
        document["canonical"],
        field="nbgv.canonical",
        keys=frozenset(
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
    native = _document(
        document["native"],
        field="nbgv.native",
        keys=frozenset({"npmPackageVersion"}),
    )
    return NbgvFacts(
        canonical_version=_document_string(
            canonical["version"],
            field="nbgv.canonical.version",
        ),
        sem_ver1=_document_string(
            canonical["semVer1"],
            field="nbgv.canonical.semVer1",
        ),
        sem_ver2=_document_string(
            canonical["semVer2"],
            field="nbgv.canonical.semVer2",
        ),
        version_height=_document_integer(
            canonical["versionHeight"],
            field="nbgv.canonical.versionHeight",
        ),
        git_commit_id=_document_string(
            canonical["gitCommitId"],
            field="nbgv.canonical.gitCommitId",
        ),
        public_release=_document_boolean(
            canonical["publicRelease"],
            field="nbgv.canonical.publicRelease",
        ),
        npm_package_version=_document_string(
            native["npmPackageVersion"],
            field="nbgv.native.npmPackageVersion",
        ),
        node_api_result_digest=_document_string(
            document["node-api-result-digest"],
            field="nbgv.node-api-result-digest",
        ),
    )


def repository_model_snapshot_from_document(
    value: JsonValue,
) -> RepositoryModelSnapshot:
    """Deserialize the exact closed Repository Model Snapshot schema."""
    document = _document(
        value,
        field="record",
        keys=frozenset(
            {
                "schema",
                "context",
                "provider-request-manifest-digest",
                "provider-result-digests",
                "project-nodes",
                "release-units",
                "quality",
                "release-policy-path",
                "release-policy",
                "nbgv",
                "reverse-index",
                "unresolved",
                "ready",
            }
        ),
    )
    if document["schema"] != "workflow-delivery/v3/repository-model-snapshot":
        message = "Repository Model Snapshot schema identity mismatch"
        raise ValueError(message)
    reverse_index_document = _document(
        document["reverse-index"],
        field="reverse-index",
        keys=frozenset(
            cast("dict[str, JsonValue]", document["reverse-index"]).keys()
        )
        if type(document["reverse-index"]) is dict
        else frozenset(),
    )
    reverse_index = tuple(
        (
            project_id,
            _string_array(
                reverse_index_document[project_id],
                field=f"reverse-index.{project_id}",
            ),
        )
        for project_id in sorted(reverse_index_document)
    )
    snapshot = RepositoryModelSnapshot(
        context=_compilation_context_from_document(document["context"]),
        manifest_digest=_document_string(
            document["provider-request-manifest-digest"],
            field="provider-request-manifest-digest",
        ),
        provider_result_digests=_string_array(
            document["provider-result-digests"],
            field="provider-result-digests",
        ),
        project_nodes=tuple(
            _project_node_from_document(project, index=index)
            for index, project in enumerate(
                _array(document["project-nodes"], field="project-nodes")
            )
        ),
        release_units=tuple(
            _compiled_release_unit_from_document(release_unit, index=index)
            for index, release_unit in enumerate(
                _array(document["release-units"], field="release-units")
            )
        ),
        quality=tuple(
            _compiled_quality_from_document(selection, index=index)
            for index, selection in enumerate(
                _array(document["quality"], field="quality")
            )
        ),
        release_policy_path=_document_string(
            document["release-policy-path"],
            field="release-policy-path",
        ),
        release_policy=_compiled_release_policy_from_document(
            document["release-policy"]
        ),
        nbgv=_nbgv_from_document(document["nbgv"]),
        reverse_index=reverse_index,
        unresolved=_string_array(
            document["unresolved"],
            field="unresolved",
        ),
        ready=_document_boolean(document["ready"], field="ready"),
    )
    if snapshot.to_document() != document:
        message = "Repository Model Snapshot document is not normalized"
        raise ValueError(message)
    return snapshot


def admit_repository_model_snapshot(
    canonical_bytes: bytes,
    *,
    expected_context: CompilationContext,
    expected_digest: str,
) -> AdmittedRepositoryModelSnapshot:
    """Admit one canonical, exact-current, ready first-slice Snapshot."""
    if type(canonical_bytes) is not bytes:
        message = "Repository Model Snapshot transport must be exact bytes"
        raise TypeError(message)
    validate_compilation_context(expected_context)
    if (
        type(expected_digest) is not str
        or _DIGEST_PATTERN.fullmatch(expected_digest) is None
    ):
        message = "Repository Model Snapshot expected digest is malformed"
        raise ValueError(message)
    document = parse_canonical_json(canonical_bytes)
    snapshot = repository_model_snapshot_from_document(document)
    if snapshot.context != expected_context:
        message = "Repository Model Snapshot current context binding mismatch"
        raise ValueError(message)
    actual_digest = snapshot.snapshot_digest
    if actual_digest != expected_digest:
        message = "Repository Model Snapshot canonical digest mismatch"
        raise ValueError(message)
    validate_first_slice_repository_model_snapshot(snapshot)
    return AdmittedRepositoryModelSnapshot(
        snapshot=snapshot,
        canonical_digest=actual_digest,
        canonical_bytes=canonical_bytes,
    )


def validate_first_slice_repository_model_snapshot(  # noqa: C901, PLR0912, PLR0915
    snapshot: RepositoryModelSnapshot,
) -> None:
    """Fail closed unless a Snapshot contains the exact first-slice closure."""
    if type(snapshot) is not RepositoryModelSnapshot:
        message = "Repository Model Snapshot has the wrong runtime type"
        raise TypeError(message)
    validate_compilation_context(snapshot.context)
    _exact_tuple(
        snapshot.provider_result_digests,
        field="provider_result_digests",
    )
    _exact_tuple(snapshot.unresolved, field="unresolved")
    if (
        snapshot.ready is not True
        or type(snapshot.ready) is not bool
        or snapshot.unresolved
        or len(snapshot.provider_result_digests) != 1
        or type(snapshot.manifest_digest) is not str
        or _DIGEST_PATTERN.fullmatch(snapshot.manifest_digest) is None
        or any(
            type(digest) is not str or _DIGEST_PATTERN.fullmatch(digest) is None
            for digest in snapshot.provider_result_digests
        )
    ):
        message = "Repository Model Snapshot is not a ready first-slice closure"
        raise ValueError(message)
    _exact_tuple(snapshot.project_nodes, field="project_nodes")
    if len(snapshot.project_nodes) != 1:
        message = "Repository Model Snapshot must contain one Project Node"
        raise ValueError(message)
    project = snapshot.project_nodes[0]
    validate_project_node(project)
    if project.private is not False:
        message = "Repository Model Snapshot Project Node closure mismatch"
        raise ValueError(message)
    expected_project = ProjectNode(
        project_id=FIRST_SLICE_PACKAGE,
        package_name=FIRST_SLICE_PACKAGE,
        path="src/public/lib/hcoona-release-smoke-npm",
        manifest_path="src/public/lib/hcoona-release-smoke-npm/package.json",
        private=False,
        workspace_dependencies=(),
    )
    if project != expected_project:
        message = "Repository Model Snapshot Project Node closure mismatch"
        raise ValueError(message)
    _exact_tuple(snapshot.release_units, field="release_units")
    if len(snapshot.release_units) != 1:
        message = "Repository Model Snapshot must contain one Release Unit"
        raise ValueError(message)
    release_unit = snapshot.release_units[0]
    _validate_compiled_release_unit(release_unit)
    if (
        release_unit.release_unit != FIRST_SLICE_RELEASE_UNIT
        or release_unit.descriptor_path
        != (
            "src/public/lib/hcoona-release-smoke-npm/"
            "workflow-delivery.release-unit.yml"
        )
        or len(release_unit.builds) != 1
    ):
        message = "Repository Model Snapshot Release Unit closure mismatch"
        raise ValueError(message)
    build = release_unit.builds[0]
    definition = BUILD_DEFINITIONS["node/npm-package-v1"]
    if (
        build.build_id != "npm-package"
        or build.definition != definition.logical_id
        or build.project_id != FIRST_SLICE_PACKAGE
        or build.entry_point
        != "src/public/lib/hcoona-release-smoke-npm/package.json"
        or build.required_native_projections
        != definition.required_native_projections
        or len(build.outputs) != 1
    ):
        message = "Repository Model Snapshot Build closure mismatch"
        raise ValueError(message)
    output = build.outputs[0]
    if (
        output.output_id,
        output.role,
        output.kind,
    ) != ("npm-tarball", "primary-package", "npm-tarball"):
        message = "Repository Model Snapshot output closure mismatch"
        raise ValueError(message)
    _compiled_string(
        snapshot.release_policy_path,
        field="release_policy_path",
    )
    if snapshot.release_policy_path != FIRST_SLICE_POLICY_PATH:
        message = "Repository Model Snapshot Release policy path mismatch"
        raise ValueError(message)
    if snapshot.release_policy is None:
        message = "Repository Model Snapshot Release policy is incomplete"
        raise ValueError(message)
    validate_compiled_release_policy(snapshot.release_policy)
    if (
        snapshot.release_policy.path != snapshot.release_policy_path
        or snapshot.release_policy
        != _expected_first_slice_compiled_release_policy()
    ):
        message = "Repository Model Snapshot Release policy closure mismatch"
        raise ValueError(message)
    _exact_tuple(snapshot.quality, field="quality")
    preset = QUALITY_PRESETS["node/hcoona-release-smoke-npm-v1"]
    expected_quality = (
        CompiledQualitySelection(
            path=(
                "src/public/lib/hcoona-release-smoke-npm/"
                "workflow-delivery.quality.yml"
            ),
            ecosystem="node",
            preset=preset.logical_id,
            required=preset.required,
            advisory=preset.advisory,
        ),
    )
    for selection in snapshot.quality:
        _validate_compiled_quality_selection(selection)
    if snapshot.quality != expected_quality:
        message = "Repository Model Snapshot Quality closure mismatch"
        raise ValueError(message)
    _validate_reverse_index(snapshot.reverse_index)
    if snapshot.reverse_index != (
        (FIRST_SLICE_PACKAGE, (f"{FIRST_SLICE_RELEASE_UNIT}/npm-package",)),
    ):
        message = "Repository Model Snapshot reverse index mismatch"
        raise ValueError(message)
    try:
        validate_nbgv_facts(
            snapshot.nbgv,
            target=snapshot.context.target,
        )
    except (TypeError, ValueError) as error:
        message = "Repository Model Snapshot NBGV facts are incomplete"
        raise ValueError(message) from error


def _exact_tuple(value: object, *, field: str) -> None:
    if type(value) is not tuple:
        message = f"Repository Model Snapshot {field} must be an exact tuple"
        raise TypeError(message)


def _string_tuple(value: object, *, field: str) -> None:
    _exact_tuple(value, field=field)
    checked = cast("tuple[object, ...]", value)
    if not all(type(item) is str for item in checked):
        message = (
            f"Repository Model Snapshot {field} must contain exact strings"
        )
        raise TypeError(message)


def _compiled_string(value: object, *, field: str) -> None:
    if type(value) is not str or not value:
        message = (
            f"Repository Model Snapshot {field} must be a nonempty exact string"
        )
        raise TypeError(message)


def _validate_compiled_output(output: CompiledOutput) -> None:
    if type(output) is not CompiledOutput:
        message = (
            "Repository Model Snapshot output must use the exact "
            "CompiledOutput runtime type"
        )
        raise TypeError(message)
    _compiled_string(output.output_id, field="output_id")
    _compiled_string(output.role, field="output role")
    _compiled_string(output.kind, field="output kind")


def _validate_compiled_build(build: CompiledBuild) -> None:
    if type(build) is not CompiledBuild:
        message = (
            "Repository Model Snapshot build must use the exact "
            "CompiledBuild runtime type"
        )
        raise TypeError(message)
    _compiled_string(build.build_id, field="build_id")
    _compiled_string(build.definition, field="build definition")
    _compiled_string(build.project_id, field="build project_id")
    _compiled_string(build.entry_point, field="build entry_point")
    _exact_tuple(build.outputs, field="build outputs")
    for output in build.outputs:
        _validate_compiled_output(output)
    _string_tuple(
        build.required_native_projections,
        field="build required_native_projections",
    )


def _validate_compiled_release_unit(
    release_unit: CompiledReleaseUnit,
) -> None:
    if type(release_unit) is not CompiledReleaseUnit:
        message = (
            "Repository Model Snapshot Release Unit must use the exact "
            "CompiledReleaseUnit runtime type"
        )
        raise TypeError(message)
    _compiled_string(release_unit.release_unit, field="release_unit")
    _compiled_string(release_unit.descriptor_path, field="descriptor_path")
    _exact_tuple(release_unit.builds, field="Release Unit builds")
    for build in release_unit.builds:
        _validate_compiled_build(build)


def _validate_compiled_quality_selection(
    selection: CompiledQualitySelection,
) -> None:
    if type(selection) is not CompiledQualitySelection:
        message = (
            "Repository Model Snapshot Quality selection must use the exact "
            "CompiledQualitySelection runtime type"
        )
        raise TypeError(message)
    _compiled_string(selection.path, field="quality path")
    _compiled_string(selection.ecosystem, field="quality ecosystem")
    _compiled_string(selection.preset, field="quality preset")
    _string_tuple(selection.required, field="quality required capabilities")
    _string_tuple(selection.advisory, field="quality advisory capabilities")


def validate_compiled_release_policy(
    policy: CompiledReleasePolicy,
) -> None:
    """Validate the strict immutable compiled Release policy shape."""
    if type(policy) is not CompiledReleasePolicy:
        message = (
            "Repository Model Snapshot Release policy must use the exact "
            "CompiledReleasePolicy runtime type"
        )
        raise TypeError(message)
    _compiled_string(policy.path, field="Release policy path")
    _compiled_string(policy.release_unit, field="Release policy unit")
    if type(policy.governance) is not CompiledGovernanceSource:
        message = (
            "Repository Model Snapshot Governance source must use the exact "
            "CompiledGovernanceSource runtime type"
        )
        raise TypeError(message)
    _compiled_string(
        policy.governance.repository,
        field="Governance repository",
    )
    _compiled_string(policy.governance.ref, field="Governance ref")
    _compiled_string(policy.governance.path, field="Governance path")
    _positive_integer(
        policy.governance.max_age_days,
        field="Governance max_age_days",
    )
    _exact_tuple(policy.channels, field="Release policy channels")
    channel_names: list[str] = []
    for index, entry in enumerate(policy.channels):
        _exact_tuple(entry, field=f"Release policy channels[{index}]")
        if len(entry) != _CHANNEL_ENTRY_FIELD_COUNT:
            message = "Repository Model Snapshot channel entry is invalid"
            raise ValueError(message)
        name, channel = entry
        _compiled_string(name, field="Release policy channel name")
        channel_names.append(name)
        if type(channel) is not CompiledChannelPolicy:
            message = (
                "Repository Model Snapshot channel policy must use the exact "
                "CompiledChannelPolicy runtime type"
            )
            raise TypeError(message)
        _string_tuple(channel.quality, field=f"{name} policy quality")
        _exact_tuple(
            channel.projections,
            field=f"{name} policy projections",
        )
        for projection in channel.projections:
            if type(projection) is not CompiledProjection:
                message = (
                    "Repository Model Snapshot projection must use the exact "
                    "CompiledProjection runtime type"
                )
                raise TypeError(message)
            _compiled_string(
                projection.destination,
                field=f"{name} projection destination",
            )
            _compiled_string(
                projection.artifact,
                field=f"{name} projection artifact",
            )
            _compiled_string(
                projection.package,
                field=f"{name} projection package",
            )
    if channel_names != ["buddy", "official"]:
        message = (
            "Repository Model Snapshot Release policy channels are not exact"
        )
        raise ValueError(message)


def _expected_first_slice_compiled_release_policy() -> CompiledReleasePolicy:
    quality = (
        "node/project-test-v1",
        "node/npm-artifact-contents-v1",
        "node/npm-install-import-v1",
    )
    return CompiledReleasePolicy(
        path=FIRST_SLICE_POLICY_PATH,
        release_unit=FIRST_SLICE_RELEASE_UNIT,
        governance=CompiledGovernanceSource(
            repository="hcoona/three",
            ref="refs/heads/main",
            path=(
                ".github/workflow-delivery/governance/"
                "hcoona-release-smoke-npm.json"
            ),
            max_age_days=90,
        ),
        channels=(
            (
                "buddy",
                CompiledChannelPolicy(
                    quality=quality,
                    projections=(
                        CompiledProjection(
                            destination=("npm/github-packages-hcoona-three-v1"),
                            artifact="npm-tarball",
                            package=FIRST_SLICE_PACKAGE,
                        ),
                    ),
                ),
            ),
            (
                "official",
                CompiledChannelPolicy(
                    quality=quality,
                    projections=(
                        CompiledProjection(
                            destination="npm/npmjs-public-v1",
                            artifact="npm-tarball",
                            package=FIRST_SLICE_PACKAGE,
                        ),
                    ),
                ),
            ),
        ),
    )


def _validate_reverse_index(
    reverse_index: tuple[tuple[str, tuple[str, ...]], ...],
) -> None:
    _exact_tuple(reverse_index, field="reverse_index")
    for entry in reverse_index:
        _exact_tuple(entry, field="reverse_index entry")
        if len(entry) != _REVERSE_INDEX_ENTRY_FIELD_COUNT:
            message = "Repository Model Snapshot reverse_index entry is invalid"
            raise ValueError(message)
        project_id, build_ids = entry
        _compiled_string(project_id, field="reverse_index project_id")
        _string_tuple(build_ids, field="reverse_index build_ids")


def _context_document(context: CompilationContext) -> dict[str, JsonValue]:
    document: dict[str, JsonValue] = {
        "request-id": context.request_id,
        "purpose": context.purpose,
        "workflow-run-id": context.workflow_run_id,
        "target": context.target,
        "producer": context.producer,
        "control": context.control,
        "catalog-digest": context.catalog_digest,
        "channel": context.channel,
        "release-unit": context.release_unit,
    }
    if context.run_attempt is not None:
        document["run-attempt"] = context.run_attempt
    return document


def _positive_integer(value: object, *, field: str) -> None:
    if type(value) is not int or value <= 0:
        message = f"compilation {field} must be a positive integer"
        raise ValueError(message)


def _nonempty_string(value: object, *, field: str) -> str:
    if type(value) is not str or not value or value != value.strip():
        message = f"compilation {field} must be a nonempty exact string"
        raise TypeError(message)
    return value


def _validate_context_run_attempt(context: CompilationContext) -> None:
    if context.purpose == "live-release":
        if context.run_attempt is not None:
            message = "live compilation cannot bind run_attempt"
            raise ValueError(message)
        return
    _positive_integer(context.run_attempt, field="run_attempt")


def validate_compilation_context(context: CompilationContext) -> None:
    """Validate the canonical purpose-bound CompilationContext invariants."""
    if type(context) is not CompilationContext:
        message = "compilation context has the wrong runtime type"
        raise TypeError(message)
    _nonempty_string(context.request_id, field="request_id")
    _nonempty_string(context.purpose, field="purpose")
    if context.purpose not in _PURPOSES:
        message = "compilation purpose is not in the closed set"
        raise ValueError(message)
    _positive_integer(context.workflow_run_id, field="workflow_run_id")
    _validate_context_run_attempt(context)
    if (
        type(context.target) is not str
        or _SHA_PATTERN.fullmatch(context.target) is None
    ):
        message = "compilation target must be a full lowercase commit SHA"
        raise ValueError(message)
    _nonempty_string(context.producer, field="producer")
    _nonempty_string(context.control, field="control")
    if (
        type(context.catalog_digest) is not str
        or _DIGEST_PATTERN.fullmatch(context.catalog_digest) is None
        or context.catalog_digest != catalog_digest()
    ):
        message = "compilation catalog digest is not the static catalog"
        raise ValueError(message)
    if context.purpose == "release-simulation":
        if type(context.channel) is not str:
            message = "simulation compilation channel must be a string"
            raise TypeError(message)
        if context.channel not in {"buddy", "official"}:
            message = "simulation compilation requires a closed channel"
            raise ValueError(message)
        if (
            type(context.release_unit) is not str
            or context.release_unit != FIRST_SLICE_RELEASE_UNIT
        ):
            message = "simulation compilation requires the first Release Unit"
            raise ValueError(message)
    elif context.channel is not None or context.release_unit is not None:
        message = "non-simulation compilation cannot bind simulation selection"
        raise ValueError(message)


def _first_slice_provider_request_document(
    context: CompilationContext,
    *,
    provider_producer: str,
) -> dict[str, JsonValue]:
    return {
        "schema": "workflow-delivery/v3/node-provider-request",
        "context": _context_document(context),
        "entry-id": "node-first-slice",
        "provider-logical-id": PROVIDER_LOGICAL_ID,
        "provider-implementation-id": PROVIDER_IMPLEMENTATION_ID,
        "execution-mode": PROVIDER_EXECUTION_MODE,
        "producer": provider_producer,
        "discovery-basis": {
            "package": "@hcoona/hcoona-release-smoke-npm",
            "entry-point": (
                "src/public/lib/hcoona-release-smoke-npm/package.json"
            ),
        },
    }


def first_slice_provider_manifest(
    context: CompilationContext,
    *,
    provider_producer: str,
) -> ProviderRequestManifest:
    """Close the one approved Provider request for this compilation."""
    validate_compilation_context(context)
    _nonempty_string(provider_producer, field="Provider producer")
    request_document = _first_slice_provider_request_document(
        context,
        provider_producer=provider_producer,
    )
    request_digest = canonical_sha256(request_document)
    return ProviderRequestManifest(
        context=context,
        requests=(
            ProviderRequest(
                entry_id="node-first-slice",
                provider_logical_id=PROVIDER_LOGICAL_ID,
                provider_implementation_id=PROVIDER_IMPLEMENTATION_ID,
                execution_mode=PROVIDER_EXECUTION_MODE,
                producer=provider_producer,
                request_digest=request_digest,
                expected_result_identity=(
                    f"{PROVIDER_LOGICAL_ID}:{context.request_id}"
                ),
            ),
        ),
    )


def provider_binding(
    manifest: ProviderRequestManifest,
    entry_id: str,
) -> ProviderBinding:
    """Create the exact Provider binding for one manifest entry."""
    if type(manifest) is not ProviderRequestManifest:
        message = "Provider Request Manifest has the wrong runtime type"
        raise TypeError(message)
    _nonempty_string(entry_id, field="Provider manifest entry_id")
    requests = [
        request for request in manifest.requests if request.entry_id == entry_id
    ]
    if len(requests) != 1:
        message = f"manifest entry is not unique: {entry_id}"
        raise ValueError(message)
    request = requests[0]
    context = manifest.context
    return ProviderBinding(
        request_id=context.request_id,
        purpose=context.purpose,
        workflow_run_id=context.workflow_run_id,
        run_attempt=context.run_attempt,
        target=context.target,
        producer=request.producer,
        control=context.control,
        catalog_digest=context.catalog_digest,
        request_digest=request.request_digest,
    )


def _validate_manifest(  # noqa: C901
    context: CompilationContext,
    manifest: ProviderRequestManifest,
) -> None:
    if type(manifest) is not ProviderRequestManifest:
        message = "Provider Request Manifest has the wrong runtime type"
        raise TypeError(message)
    if type(manifest.context) is not CompilationContext:
        message = "Provider Request Manifest context has the wrong type"
        raise TypeError(message)
    if type(manifest.requests) is not tuple:
        message = "Provider Request Manifest requests must be a tuple"
        raise TypeError(message)
    if manifest.context != context:
        message = "Provider Request Manifest context mismatch"
        raise ValueError(message)
    if len(manifest.requests) != 1:
        message = "first-slice manifest must contain exactly one Provider"
        raise ValueError(message)
    request = manifest.requests[0]
    if type(request) is not ProviderRequest:
        message = "Provider request has the wrong runtime type"
        raise TypeError(message)
    for field, value in (
        ("entry_id", request.entry_id),
        ("provider_logical_id", request.provider_logical_id),
        ("provider_implementation_id", request.provider_implementation_id),
        ("execution_mode", request.execution_mode),
        ("producer", request.producer),
        ("request_digest", request.request_digest),
        ("expected_result_identity", request.expected_result_identity),
    ):
        _nonempty_string(value, field=f"Provider request {field}")
    expected = (
        PROVIDER_LOGICAL_ID,
        PROVIDER_IMPLEMENTATION_ID,
        PROVIDER_EXECUTION_MODE,
    )
    actual = (
        request.provider_logical_id,
        request.provider_implementation_id,
        request.execution_mode,
    )
    if actual != expected:
        message = "manifest selected an unsupported Provider implementation"
        raise ValueError(message)
    if request.entry_id != "node-first-slice":
        message = "manifest Provider request is not the canonical first slice"
        raise ValueError(message)
    if (
        _DIGEST_PATTERN.fullmatch(request.request_digest) is None
        or request.expected_result_identity
        != f"{PROVIDER_LOGICAL_ID}:{context.request_id}"
    ):
        message = "manifest Provider request is malformed"
        raise ValueError(message)
    expected_digest = canonical_sha256(
        _first_slice_provider_request_document(
            context,
            provider_producer=request.producer,
        )
    )
    if request.request_digest != expected_digest:
        message = (
            "manifest Provider request digest is not bound to the canonical "
            "first-slice request"
        )
        raise ValueError(message)


def _validate_fact_bundle_admission_context(
    admission: FactBundleAdmissionContext,
) -> None:
    if type(admission) is not FactBundleAdmissionContext:
        message = "Fact Bundle admission context has the wrong runtime type"
        raise TypeError(message)
    for field, value in (
        ("request_artifact_id", admission.request_artifact_id),
        ("transport_id", admission.transport_id),
    ):
        _positive_integer(value, field=f"Fact Bundle {field}")
    for field, value in (
        ("request_artifact_digest", admission.request_artifact_digest),
        ("transport_digest", admission.transport_digest),
        ("bundle_digest", admission.bundle_digest),
    ):
        if type(value) is not str or _DIGEST_PATTERN.fullmatch(value) is None:
            message = f"Fact Bundle {field} must be a SHA-256 digest"
            raise ValueError(message)


def _validate_fact_bundle_schema(bundle: NodeProviderFactBundle) -> None:
    if type(bundle) is not NodeProviderFactBundle:
        message = "compiler requires an admitted Fact Bundle"
        raise TypeError(message)
    if (
        type(bundle.schema) is not str
        or bundle.schema != NODE_PROVIDER_FACT_BUNDLE_SCHEMA
    ):
        message = "Fact Bundle schema identity mismatch"
        raise ValueError(message)
    validate_provider_binding(bundle.binding)
    for field, value in (
        ("manifest_digest", bundle.manifest_digest),
        ("request_artifact_digest", bundle.request_artifact_digest),
        ("provider_result_digest", bundle.provider_result_digest),
        ("transport_digest", bundle.transport_digest),
    ):
        if type(value) is not str or _DIGEST_PATTERN.fullmatch(value) is None:
            message = f"Fact Bundle {field} must be a SHA-256 digest"
            raise ValueError(message)
    _nonempty_string(
        bundle.manifest_entry_id,
        field="Fact Bundle manifest_entry_id",
    )
    _positive_integer(
        bundle.request_artifact_id,
        field="Fact Bundle request_artifact_id",
    )
    _positive_integer(
        bundle.transport_id,
        field="Fact Bundle transport_id",
    )
    validate_node_provider_result(bundle.provider_result)
    if bundle.provider_result_digest != bundle.provider_result.result_digest:
        message = "Fact Bundle Provider Result payload digest mismatch"
        raise ValueError(message)
    if bundle.binding != bundle.provider_result.binding:
        message = "Fact Bundle binding does not match Provider Result"
        raise ValueError(message)


def admit_node_provider_fact_bundle(
    bundle: NodeProviderFactBundle,
    *,
    context: CompilationContext,
    manifest: ProviderRequestManifest,
    admission: FactBundleAdmissionContext,
) -> AdmittedNodeProviderFactBundle:
    """Strictly admit one current-purpose/current-attempt Fact Bundle."""
    validate_compilation_context(context)
    _validate_manifest(context, manifest)
    _validate_fact_bundle_admission_context(admission)
    _validate_fact_bundle_schema(bundle)
    request = manifest.requests[0]
    expected_binding = provider_binding(manifest, request.entry_id)
    if bundle.binding != expected_binding:
        message = "Fact Bundle authority binding mismatch"
        raise ValueError(message)
    checks = (
        (
            "provider request manifest digest",
            bundle.manifest_digest,
            manifest.manifest_digest,
        ),
        (
            "provider request entry",
            bundle.manifest_entry_id,
            request.entry_id,
        ),
        (
            "request artifact ID",
            bundle.request_artifact_id,
            admission.request_artifact_id,
        ),
        (
            "request artifact digest",
            bundle.request_artifact_digest,
            admission.request_artifact_digest,
        ),
        (
            "transport ID",
            bundle.transport_id,
            admission.transport_id,
        ),
        (
            "transport digest",
            bundle.transport_digest,
            admission.transport_digest,
        ),
        (
            "Bundle digest",
            bundle.bundle_digest,
            admission.bundle_digest,
        ),
    )
    for field, actual, expected in checks:
        if actual != expected:
            message = f"Fact Bundle {field} binding mismatch"
            raise ValueError(message)
    _validate_result(context, request, bundle.provider_result)
    return AdmittedNodeProviderFactBundle(bundle=bundle, admission=admission)


def _result_identity(result: NodeProviderResult) -> str:
    return f"{result.provider_logical_id}:{result.binding.request_id}"


def _validate_result(
    context: CompilationContext,
    request: ProviderRequest,
    result: NodeProviderResult,
) -> None:
    validate_node_provider_result(result)
    expected_binding = ProviderBinding(
        request_id=context.request_id,
        purpose=context.purpose,
        workflow_run_id=context.workflow_run_id,
        run_attempt=context.run_attempt,
        target=context.target,
        producer=request.producer,
        control=context.control,
        catalog_digest=context.catalog_digest,
        request_digest=request.request_digest,
    )
    if result.binding != expected_binding:
        message = "Provider Result binding mismatch"
        raise ValueError(message)
    if (
        result.provider_logical_id != request.provider_logical_id
        or result.provider_implementation_id
        != request.provider_implementation_id
        or result.execution_mode != request.execution_mode
        or _result_identity(result) != request.expected_result_identity
    ):
        message = "Provider Result identity mismatch"
        raise ValueError(message)
    if (
        result.outcome != "success"
        or result.unresolved
        or result.conflicts
        or result.diagnostic_reference is not None
    ):
        message = "Provider Result is not a resolved terminal success"
        raise ValueError(message)
    if result.execution_class != PROVIDER_EXECUTION_CLASS:
        message = "Provider Result execution class mismatch"
        raise ValueError(message)
    validate_provider_toolchain(result.toolchain)
    if result.build_capabilities != ("node/npm-package-v1",):
        message = "Provider Result build capability closure mismatch"
        raise ValueError(message)
    if any(project.private is not False for project in result.project_nodes):
        message = "Provider Result Project Node private must be exactly false"
        raise ValueError(message)
    if (
        result.checkout.target != context.target
        or result.checkout.head != context.target
        or result.checkout.shallow is not False
        or result.checkout.ancestry_complete is not True
        or result.checkout.tags_complete is not True
        or result.checkout.credentials_persisted is not False
        or result.checkout.authoritative_remote != AUTHORITATIVE_REMOTE
        or type(result.checkout.authoritative_remote_url) is not str
        or not result.checkout.authoritative_remote_url
        or result.checkout.authoritative_remote_url
        != result.checkout.authoritative_remote_url.strip()
        or result.checkout.tag_refspec != TAG_REFSPEC
    ):
        message = "Provider Result lacks exact full-history checkout evidence"
        raise ValueError(message)
    if result.nbgv.git_commit_id != context.target:
        message = "Provider NBGV facts do not bind the compilation target"
        raise ValueError(message)
    if (
        type(result.nbgv.npm_package_version) is not str
        or not result.nbgv.npm_package_version
    ):
        message = "Provider Result is missing npmPackageVersion"
        raise ValueError(message)
    validate_nbgv_facts(result.nbgv, target=context.target)


def _git_target_file_bytes(
    repo_root: Path,
    target: str,
    path: str,
) -> bytes:
    try:
        return subprocess.run(  # noqa: S603
            ("git", "show", f"{target}:{path}"),  # noqa: S607
            cwd=repo_root,
            check=True,
            capture_output=True,
        ).stdout
    except subprocess.CalledProcessError as error:
        message = f"Provider input is missing from the exact target: {path}"
        raise ValueError(message) from error


def _git_target_paths(repo_root: Path, target: str) -> frozenset[str]:
    try:
        result = subprocess.run(  # noqa: S603
            ("git", "ls-tree", "-r", "--name-only", target),  # noqa: S607
            cwd=repo_root,
            check=True,
            capture_output=True,
            text=True,
        )
    except subprocess.CalledProcessError as error:
        message = "Provider input tree cannot be read from the exact target"
        raise ValueError(message) from error
    return frozenset(result.stdout.splitlines())


def _content_digest(content: bytes) -> str:
    return f"sha256:{hashlib.sha256(content).hexdigest()}"


def _expected_provider_input_facts(
    repo_root: Path,
    target: str,
) -> tuple[str, str, tuple[GlobalInput, ...]]:
    project_path = "src/public/lib/hcoona-release-smoke-npm/package.json"
    manifest_digest = _content_digest(
        _git_target_file_bytes(repo_root, target, project_path)
    )
    target_paths = _git_target_paths(repo_root, target)
    version_paths = tuple(
        path
        for path in node_provider_version_input_candidates(
            "src/public/lib/hcoona-release-smoke-npm"
        )
        if path in target_paths
    )
    if not version_paths:
        message = "Provider cannot resolve an effective version.json lineage"
        raise ValueError(message)
    global_paths = tuple(
        sorted({*FIRST_SLICE_REQUIRED_GLOBAL_INPUTS, *version_paths})
    )
    global_inputs = tuple(
        GlobalInput(
            path=path,
            content_digest=_content_digest(
                _git_target_file_bytes(repo_root, target, path)
            ),
            project_ids=(FIRST_SLICE_PACKAGE,),
        )
        for path in global_paths
    )
    configuration_digest = canonical_sha256(
        {
            "schema": "workflow-delivery/v3/node-provider-configuration",
            "global-inputs": [
                global_input.to_document() for global_input in global_inputs
            ],
        }
    )
    return manifest_digest, configuration_digest, global_inputs


def _validate_result_input_facts(
    repo_root: Path,
    context: CompilationContext,
    result: NodeProviderResult,
) -> None:
    expected = _expected_provider_input_facts(repo_root, context.target)
    actual = (
        result.manifest_digest,
        result.configuration_digest,
        result.global_inputs,
    )
    if actual != expected:
        message = "Provider Result input digests do not match the exact target"
        raise ValueError(message)


def _relative(repo_root: Path, path: str) -> str:
    candidate = Path(path)
    if not candidate.is_absolute():
        return candidate.as_posix()
    try:
        return candidate.relative_to(repo_root).as_posix()
    except ValueError as error:
        message = f"authoring path is outside repository: {path}"
        raise ValueError(message) from error


def compile_release_policy(policy: ReleasePolicy) -> CompiledReleasePolicy:
    """Freeze one normalized target Release policy into Snapshot values."""
    if type(policy) is not ReleasePolicy:
        message = "compiler requires an exact ReleasePolicy"
        raise TypeError(message)
    compiled = CompiledReleasePolicy(
        path=policy.path,
        release_unit=policy.release_unit,
        governance=CompiledGovernanceSource(
            repository=policy.governance.repository,
            ref=policy.governance.ref,
            path=policy.governance.path,
            max_age_days=policy.governance.max_age_days,
        ),
        channels=tuple(
            (
                name,
                CompiledChannelPolicy(
                    quality=channel.quality,
                    projections=tuple(
                        CompiledProjection(
                            destination=projection.destination,
                            artifact=projection.artifact,
                            package=projection.package,
                        )
                        for projection in channel.projections
                    ),
                ),
            )
            for name, channel in policy.channels
        ),
    )
    validate_compiled_release_policy(compiled)
    return compiled


def _compile_release_unit(
    repo_root: Path,
    target: str,
    project: ProjectNode,
) -> tuple[
    CompiledReleaseUnit,
    CompiledQualitySelection,
    CompiledReleasePolicy,
]:
    descriptor, quality, policy = load_first_slice_authoring(repo_root, target)
    if project.private is not False:
        message = "first-slice Project Node cannot be private"
        raise ValueError(message)
    descriptor_root = Path(_relative(repo_root, descriptor.path)).parent
    if (
        project.project_id != FIRST_SLICE_PACKAGE
        or project.package_name != FIRST_SLICE_PACKAGE
        or project.path != descriptor_root.as_posix()
    ):
        message = "first-slice Project Node identity/path is not exact"
        raise ValueError(message)
    compiled_builds: list[CompiledBuild] = []
    for build in descriptor.builds:
        entry_path = (descriptor_root / build.entry_point).as_posix()
        if entry_path != project.manifest_path:
            message = "Build entry point does not resolve to the Project Node"
            raise ValueError(message)
        definition = BUILD_DEFINITIONS[build.definition]
        compiled_builds.append(
            CompiledBuild(
                build_id=build.build_id,
                definition=build.definition,
                project_id=project.project_id,
                entry_point=entry_path,
                outputs=tuple(
                    CompiledOutput(
                        output_id=output.output_id,
                        role=output.role,
                        kind=output.kind,
                    )
                    for output in build.outputs
                ),
                required_native_projections=(
                    definition.required_native_projections
                ),
            )
        )
    preset_id = quality.preset_for("node")
    preset = QUALITY_PRESETS[preset_id]
    compiled_quality = CompiledQualitySelection(
        path=_relative(repo_root, quality.path),
        ecosystem="node",
        preset=preset.logical_id,
        required=preset.required,
        advisory=preset.advisory,
    )
    return (
        CompiledReleaseUnit(
            release_unit=descriptor.release_unit,
            descriptor_path=_relative(repo_root, descriptor.path),
            builds=tuple(compiled_builds),
        ),
        compiled_quality,
        compile_release_policy(policy),
    )


def compile_repository_model(
    repo_root: Path,
    context: CompilationContext,
    manifest: ProviderRequestManifest,
    bundles: Sequence[AdmittedNodeProviderFactBundle],
) -> RepositoryModelSnapshot:
    """Compile one complete purpose-bound first-slice Snapshot."""
    validate_compilation_context(context)
    _validate_manifest(context, manifest)
    request = manifest.requests[0]
    if len(bundles) != 1:
        message = (
            "compilation requires exactly one admitted Fact Bundle and no "
            "unexpected inputs"
        )
        raise ValueError(message)
    admitted = bundles[0]
    if type(admitted) is not AdmittedNodeProviderFactBundle:
        message = "compiler requires an admitted Fact Bundle"
        raise TypeError(message)
    admitted = admit_node_provider_fact_bundle(
        admitted.bundle,
        context=context,
        manifest=manifest,
        admission=admitted.admission,
    )
    result = admitted.provider_result
    if result.provider_logical_id != request.provider_logical_id:
        message = "admitted Fact Bundle Provider identity mismatch"
        raise ValueError(message)
    _validate_result_input_facts(repo_root, context, result)
    if len(result.project_nodes) != 1:
        message = "first-slice Provider must emit exactly one Project Node"
        raise ValueError(message)
    project = result.project_nodes[0]
    if project.workspace_dependencies:
        message = (
            "commit 3 permits exactly one Project Node and no workspace closure"
        )
        raise ValueError(message)
    try:
        release_unit, quality, compiled_policy = _compile_release_unit(
            repo_root,
            context.target,
            project,
        )
    except MissingFirstSliceAuthoringError as error:
        return RepositoryModelSnapshot(
            context=context,
            manifest_digest=manifest.manifest_digest,
            provider_result_digests=(result.result_digest,),
            project_nodes=result.project_nodes,
            release_units=(),
            quality=(),
            release_policy_path=FIRST_SLICE_POLICY_PATH,
            release_policy=None,
            nbgv=result.nbgv,
            reverse_index=((project.project_id, ()),),
            unresolved=(str(error),),
            ready=False,
        )
    if not release_unit.builds or any(
        not build.outputs for build in release_unit.builds
    ):
        message = "Repository Model build and artifact scope is incomplete"
        raise ValueError(message)
    if any(
        "npmPackageVersion" in build.required_native_projections
        and not result.nbgv.npm_package_version
        for build in release_unit.builds
    ):
        message = "Repository Model lacks required native projection"
        raise ValueError(message)
    reverse_builds = tuple(
        f"{release_unit.release_unit}/{build.build_id}"
        for build in release_unit.builds
    )
    return RepositoryModelSnapshot(
        context=context,
        manifest_digest=manifest.manifest_digest,
        provider_result_digests=(result.result_digest,),
        project_nodes=result.project_nodes,
        release_units=(release_unit,),
        quality=(quality,),
        release_policy_path=compiled_policy.path,
        release_policy=compiled_policy,
        nbgv=result.nbgv,
        reverse_index=((project.project_id, reverse_builds),),
        unresolved=(),
        ready=True,
    )
