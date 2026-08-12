"""Strict Workflow Delivery v3 authoring and target-tree discovery."""

from __future__ import annotations

import re
import subprocess
from dataclasses import dataclass
from pathlib import Path, PurePosixPath
from typing import TYPE_CHECKING

import yaml

from three_workflow_delivery_v3.catalogs import (
    BUILD_DEFINITIONS,
    DESTINATION_DEFINITIONS,
    QUALITY_DEFINITIONS,
    QUALITY_PRESETS,
    RELEASE_POLICIES,
    require_catalog_id,
)

if TYPE_CHECKING:
    from three_workflow_delivery_v3.canonical import JsonValue

RELEASE_UNIT_BASENAME = "workflow-delivery.release-unit.yml"
QUALITY_BASENAME = "workflow-delivery.quality.yml"
RELEASE_UNIT_SCHEMA = "workflow-delivery/v3/release-unit"
QUALITY_SCHEMA = "workflow-delivery/v3/quality-selection"
RELEASE_POLICY_SCHEMA = "workflow-delivery/v3/release-policy"
FIRST_SLICE_RELEASE_UNIT = "hcoona-release-smoke-npm"
FIRST_SLICE_PACKAGE = "@hcoona/hcoona-release-smoke-npm"
FIRST_SLICE_POLICY_PATH = (
    "eng/workflow-delivery/v3/policies/hcoona-release-smoke-npm.yml"
)
GOVERNANCE_REPOSITORY = "hcoona/three"
GOVERNANCE_REF = "refs/heads/main"
GOVERNANCE_PATH = (
    ".github/workflow-delivery/governance/hcoona-release-smoke-npm.json"
)
GOVERNANCE_MAX_AGE_DAYS = 90

_ID_PATTERN = re.compile(r"[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\Z")
_SHA_PATTERN = re.compile(r"[0-9a-f]{40}\Z")


class _UniqueKeyLoader(yaml.SafeLoader):
    """Safe YAML loader rejecting duplicate mapping keys."""


def _construct_unique_mapping(
    loader: _UniqueKeyLoader,
    node: yaml.MappingNode,
    deep: bool = False,  # noqa: FBT001, FBT002
) -> dict[object, object]:
    loader.flatten_mapping(node)
    result: dict[object, object] = {}
    for key_node, value_node in node.value:
        key = loader.construct_object(key_node, deep=deep)
        if key in result:
            message = f"duplicate YAML mapping key: {key!r}"
            raise ValueError(message)
        result[key] = loader.construct_object(value_node, deep=deep)
    return result


_UniqueKeyLoader.add_constructor(
    yaml.resolver.BaseResolver.DEFAULT_MAPPING_TAG,
    _construct_unique_mapping,
)


@dataclass(frozen=True, slots=True)
class OutputDeclaration:
    """One logical output declared by a Release Unit build."""

    output_id: str
    role: str
    kind: str


@dataclass(frozen=True, slots=True)
class BuildDeclaration:
    """One Build Definition selection and its complete output set."""

    build_id: str
    definition: str
    entry_point: str
    outputs: tuple[OutputDeclaration, ...]


@dataclass(frozen=True, slots=True)
class ReleaseUnitDescriptor:
    """Strict v3 Release Unit declaration."""

    path: str
    release_unit: str
    builds: tuple[BuildDeclaration, ...]


@dataclass(frozen=True, slots=True)
class QualitySelection:
    """Cascading ecosystem-to-static-preset selection."""

    path: str
    ecosystems: tuple[tuple[str, str], ...]

    def preset_for(self, ecosystem: str) -> str:
        """Return the selected preset for one ecosystem."""
        for name, preset in self.ecosystems:
            if name == ecosystem:
                return preset
        message = f"quality selection has no ecosystem: {ecosystem}"
        raise ValueError(message)


@dataclass(frozen=True, slots=True)
class GovernanceSource:
    """Immutable first-slice protected Governance source contract."""

    repository: str
    ref: str
    path: str
    max_age_days: int


@dataclass(frozen=True, slots=True)
class Projection:
    """One channel-owned logical destination projection."""

    destination: str
    artifact: str
    package: str


@dataclass(frozen=True, slots=True)
class ChannelPolicy:
    """Channel-owned quality and projection policy."""

    quality: tuple[str, ...]
    projections: tuple[Projection, ...]


_APPROVED_RELEASE_QUALITY = (
    "node/project-test-v1",
    "node/npm-artifact-contents-v1",
    "node/npm-install-import-v1",
)
_APPROVED_RELEASE_PROJECTIONS = {
    "buddy": (
        Projection(
            destination="npm/github-packages-hcoona-three-v1",
            artifact="npm-tarball",
            package=FIRST_SLICE_PACKAGE,
        ),
    ),
    "official": (
        Projection(
            destination="npm/npmjs-public-v1",
            artifact="npm-tarball",
            package=FIRST_SLICE_PACKAGE,
        ),
    ),
}


@dataclass(frozen=True, slots=True)
class ReleasePolicy:
    """Strict first-slice Release policy."""

    path: str
    release_unit: str
    governance: GovernanceSource
    channels: tuple[tuple[str, ChannelPolicy], ...]

    def channel(self, name: str) -> ChannelPolicy:
        """Return one statically named channel."""
        for channel_name, policy in self.channels:
            if channel_name == name:
                return policy
        message = f"release policy has no channel: {name}"
        raise ValueError(message)


def _yaml_value(value: object, *, context: str) -> JsonValue:
    if value is None or isinstance(value, bool | int | float | str):
        return value
    if isinstance(value, list):
        return [
            _yaml_value(item, context=f"{context} array item") for item in value
        ]
    if isinstance(value, dict):
        document: dict[str, JsonValue] = {}
        for name, item in value.items():
            if not isinstance(name, str):
                message = f"{context} object keys must be strings"
                raise TypeError(message)
            document[name] = _yaml_value(item, context=f"{context}.{name}")
        return document
    message = f"{context} contains a non-JSON YAML value"
    raise TypeError(message)


def _parse_yaml(content: str, *, source: str) -> dict[str, JsonValue]:
    try:
        loaded: object = yaml.load(
            content,
            Loader=_UniqueKeyLoader,  # noqa: S506
        )
    except yaml.YAMLError as error:
        message = f"malformed YAML authoring: {source}"
        raise ValueError(message) from error
    normalized = _yaml_value(loaded, context=source)
    if not isinstance(normalized, dict):
        message = f"YAML authoring must be an object: {source}"
        raise TypeError(message)
    return normalized


def _load_yaml(path: Path) -> dict[str, JsonValue]:
    return _parse_yaml(
        path.read_text(encoding="utf-8"),
        source=path.as_posix(),
    )


def _closed(
    document: dict[str, JsonValue],
    *,
    required: tuple[str, ...],
    context: str,
) -> None:
    missing = set(required) - document.keys()
    if missing:
        message = f"{context} missing required field: {sorted(missing)[0]}"
        raise ValueError(message)
    unknown = document.keys() - set(required)
    if unknown:
        message = f"{context} unknown field: {sorted(unknown)[0]}"
        raise ValueError(message)


def _mapping(value: JsonValue, *, context: str) -> dict[str, JsonValue]:
    if not isinstance(value, dict):
        message = f"{context} must be an object"
        raise TypeError(message)
    return value


def _sequence(value: JsonValue, *, context: str) -> list[JsonValue]:
    if not isinstance(value, list):
        message = f"{context} must be an array"
        raise TypeError(message)
    return value


def _string(value: JsonValue, *, context: str) -> str:
    if not isinstance(value, str) or not value:
        message = f"{context} must be a nonempty string"
        raise TypeError(message)
    return value


def _identity(value: JsonValue, *, context: str) -> str:
    identity = _string(value, context=context)
    if _ID_PATTERN.fullmatch(identity) is None:
        message = f"{context} is not a canonical identity"
        raise ValueError(message)
    return identity


def _relative_path(value: JsonValue, *, context: str) -> str:
    path = _string(value, context=context)
    pure_path = PurePosixPath(path)
    if (
        pure_path.is_absolute()
        or ".." in pure_path.parts
        or "\\" in path
        or path != pure_path.as_posix()
    ):
        message = f"{context} must be a normalized relative POSIX path"
        raise ValueError(message)
    return path


def load_release_unit(
    path: Path,
    *,
    _target_content: str | None = None,
    _target_path: str | None = None,
) -> ReleaseUnitDescriptor:
    """Load one strict v3 Release Unit descriptor."""
    document = (
        _load_yaml(path)
        if _target_content is None
        else _parse_yaml(
            _target_content,
            source=_target_path or path.as_posix(),
        )
    )
    _closed(
        document,
        required=("schema", "release-unit", "builds"),
        context="release-unit descriptor",
    )
    if document["schema"] != RELEASE_UNIT_SCHEMA:
        message = "release-unit descriptor has the wrong schema"
        raise ValueError(message)
    release_unit = _identity(
        document["release-unit"],
        context="release-unit",
    )
    builds: list[BuildDeclaration] = []
    build_ids: set[str] = set()
    output_ids: set[str] = set()
    for index, value in enumerate(
        _sequence(document["builds"], context="builds"),
    ):
        build = _mapping(value, context=f"builds[{index}]")
        _closed(
            build,
            required=("id", "definition", "entry-point", "outputs"),
            context=f"builds[{index}]",
        )
        build_id = _identity(build["id"], context=f"builds[{index}].id")
        if build_id in build_ids:
            message = f"duplicate build identity: {build_id}"
            raise ValueError(message)
        build_ids.add(build_id)
        definition = _string(
            build["definition"],
            context=f"builds[{index}].definition",
        )
        require_catalog_id(
            BUILD_DEFINITIONS,
            definition,
            kind="Build Definition",
        )
        entry_point = _relative_path(
            build["entry-point"],
            context=f"builds[{index}].entry-point",
        )
        outputs: list[OutputDeclaration] = []
        for output_index, output_value in enumerate(
            _sequence(
                build["outputs"],
                context=f"builds[{index}].outputs",
            ),
        ):
            output = _mapping(
                output_value,
                context=f"builds[{index}].outputs[{output_index}]",
            )
            _closed(
                output,
                required=("id", "role", "kind"),
                context=f"builds[{index}].outputs[{output_index}]",
            )
            output_id = _identity(
                output["id"],
                context=f"builds[{index}].outputs[{output_index}].id",
            )
            if output_id in output_ids:
                message = f"duplicate output identity: {output_id}"
                raise ValueError(message)
            output_ids.add(output_id)
            outputs.append(
                OutputDeclaration(
                    output_id=output_id,
                    role=_identity(
                        output["role"],
                        context=(
                            f"builds[{index}].outputs[{output_index}].role"
                        ),
                    ),
                    kind=_identity(
                        output["kind"],
                        context=(
                            f"builds[{index}].outputs[{output_index}].kind"
                        ),
                    ),
                )
            )
        if not outputs:
            message = f"build has no outputs: {build_id}"
            raise ValueError(message)
        builds.append(
            BuildDeclaration(
                build_id=build_id,
                definition=definition,
                entry_point=entry_point,
                outputs=tuple(outputs),
            )
        )
    if not builds:
        message = "release-unit descriptor must declare a build"
        raise ValueError(message)
    return ReleaseUnitDescriptor(
        path=_target_path or path.as_posix(),
        release_unit=release_unit,
        builds=tuple(builds),
    )


def load_quality_selection(
    path: Path,
    *,
    _target_content: str | None = None,
    _target_path: str | None = None,
) -> QualitySelection:
    """Load one strict cascading quality selection."""
    document = (
        _load_yaml(path)
        if _target_content is None
        else _parse_yaml(
            _target_content,
            source=_target_path or path.as_posix(),
        )
    )
    _closed(
        document,
        required=("schema", "ecosystems"),
        context="quality selection",
    )
    if document["schema"] != QUALITY_SCHEMA:
        message = "quality selection has the wrong schema"
        raise ValueError(message)
    ecosystem_document = _mapping(
        document["ecosystems"],
        context="quality selection ecosystems",
    )
    ecosystems: list[tuple[str, str]] = []
    for ecosystem in sorted(ecosystem_document):
        selection = _mapping(
            ecosystem_document[ecosystem],
            context=f"quality ecosystem {ecosystem}",
        )
        _closed(
            selection,
            required=("preset",),
            context=f"quality ecosystem {ecosystem}",
        )
        preset = _string(
            selection["preset"],
            context=f"quality ecosystem {ecosystem}.preset",
        )
        require_catalog_id(
            QUALITY_PRESETS,
            preset,
            kind="Quality preset",
        )
        ecosystems.append((ecosystem, preset))
    if not ecosystems:
        message = "quality selection must select an ecosystem preset"
        raise ValueError(message)
    return QualitySelection(
        path=_target_path or path.as_posix(),
        ecosystems=tuple(ecosystems),
    )


def _governance_source(document: JsonValue) -> GovernanceSource:
    governance = _mapping(document, context="governance")
    _closed(governance, required=("attestation",), context="governance")
    attestation = _mapping(
        governance["attestation"],
        context="governance.attestation",
    )
    _closed(
        attestation,
        required=("repository", "ref", "path", "max-age-days"),
        context="governance.attestation",
    )
    max_age_days = attestation["max-age-days"]
    if isinstance(max_age_days, bool) or not isinstance(max_age_days, int):
        message = "governance.attestation.max-age-days must be an integer"
        raise TypeError(message)
    source = GovernanceSource(
        repository=_string(
            attestation["repository"],
            context="governance.attestation.repository",
        ),
        ref=_string(
            attestation["ref"],
            context="governance.attestation.ref",
        ),
        path=_relative_path(
            attestation["path"],
            context="governance.attestation.path",
        ),
        max_age_days=max_age_days,
    )
    expected = GovernanceSource(
        GOVERNANCE_REPOSITORY,
        GOVERNANCE_REF,
        GOVERNANCE_PATH,
        GOVERNANCE_MAX_AGE_DAYS,
    )
    if source != expected:
        message = "release policy Governance source is not the fixed contract"
        raise ValueError(message)
    return source


def _channel_policy(name: str, value: JsonValue) -> ChannelPolicy:
    document = _mapping(value, context=f"channels.{name}")
    _closed(
        document,
        required=("quality", "projections"),
        context=f"channels.{name}",
    )
    quality = tuple(
        _string(item, context=f"channels.{name}.quality")
        for item in _sequence(
            document["quality"],
            context=f"channels.{name}.quality",
        )
    )
    for quality_id in quality:
        require_catalog_id(
            QUALITY_DEFINITIONS,
            quality_id,
            kind="Quality Definition",
        )
    if quality != _APPROVED_RELEASE_QUALITY:
        message = f"channels.{name}.quality must match the exact approved list"
        raise ValueError(message)
    projections: list[Projection] = []
    for index, projection_value in enumerate(
        _sequence(
            document["projections"],
            context=f"channels.{name}.projections",
        ),
    ):
        projection = _mapping(
            projection_value,
            context=f"channels.{name}.projections[{index}]",
        )
        _closed(
            projection,
            required=("destination", "artifact", "package"),
            context=f"channels.{name}.projections[{index}]",
        )
        destination = _string(
            projection["destination"],
            context=f"channels.{name}.projections[{index}].destination",
        )
        require_catalog_id(
            DESTINATION_DEFINITIONS,
            destination,
            kind="Destination",
        )
        projections.append(
            Projection(
                destination=destination,
                artifact=_identity(
                    projection["artifact"],
                    context=f"channels.{name}.projections[{index}].artifact",
                ),
                package=_string(
                    projection["package"],
                    context=f"channels.{name}.projections[{index}].package",
                ),
            )
        )
    projection_tuple = tuple(projections)
    for projection in projection_tuple:
        if (
            name
            not in DESTINATION_DEFINITIONS[
                projection.destination
            ].supported_channels
        ):
            message = (
                f"channels.{name}.projection destination "
                f"{projection.destination} does not support {name}"
            )
            raise ValueError(message)
        if projection.package != FIRST_SLICE_PACKAGE:
            message = f"channels.{name}.projection package binding is not exact"
            raise ValueError(message)
    if projection_tuple != _APPROVED_RELEASE_PROJECTIONS[name]:
        message = (
            f"channels.{name}.projections must match the exact approved list"
        )
        raise ValueError(message)
    return ChannelPolicy(quality=quality, projections=projection_tuple)


def load_release_policy(
    path: Path,
    *,
    _target_content: str | None = None,
    _target_path: str | None = None,
) -> ReleasePolicy:
    """Load the strict first-slice Release policy."""
    document = (
        _load_yaml(path)
        if _target_content is None
        else _parse_yaml(
            _target_content,
            source=_target_path or path.as_posix(),
        )
    )
    _closed(
        document,
        required=("schema", "release-unit", "governance", "channels"),
        context="release policy",
    )
    if document["schema"] != RELEASE_POLICY_SCHEMA:
        message = "release policy has the wrong schema"
        raise ValueError(message)
    release_unit = _identity(
        document["release-unit"],
        context="release policy release-unit",
    )
    require_catalog_id(
        RELEASE_POLICIES,
        release_unit,
        kind="Release policy",
    )
    if release_unit != FIRST_SLICE_RELEASE_UNIT:
        message = "release policy is not for the first-slice Release Unit"
        raise ValueError(message)
    channels_document = _mapping(
        document["channels"],
        context="release policy channels",
    )
    if set(channels_document) != {"buddy", "official"}:
        message = "release policy channels must be exactly buddy and official"
        raise ValueError(message)
    channels = tuple(
        (
            name,
            _channel_policy(name, channels_document[name]),
        )
        for name in ("buddy", "official")
    )
    policy = ReleasePolicy(
        path=_target_path or path.as_posix(),
        release_unit=release_unit,
        governance=_governance_source(document["governance"]),
        channels=channels,
    )
    for _, channel in policy.channels:
        for projection in channel.projections:
            if projection.package != FIRST_SLICE_PACKAGE:
                message = "release policy package binding is not exact"
                raise ValueError(message)
    return policy


def _git_paths(repo_root: Path, target: str) -> tuple[str, ...]:
    if _SHA_PATTERN.fullmatch(target) is None:
        message = "descriptor target must be a full lowercase commit SHA"
        raise ValueError(message)
    try:
        resolved = subprocess.run(  # noqa: S603
            ("git", "rev-parse", "--verify", f"{target}^{{commit}}"),
            cwd=repo_root,
            check=True,
            capture_output=True,
            text=True,
        ).stdout.strip()
        result = subprocess.run(  # noqa: S603
            ("git", "ls-tree", "-r", "--name-only", "-z", target),
            cwd=repo_root,
            check=True,
            capture_output=True,
        )
    except subprocess.CalledProcessError as error:
        message = f"cannot enumerate descriptor target tree: {target}"
        raise ValueError(message) from error
    if resolved != target:
        message = "descriptor target did not resolve to the exact commit"
        raise ValueError(message)
    return tuple(
        sorted(
            path for path in result.stdout.decode("utf-8").split("\0") if path
        )
    )


def _git_target_file(repo_root: Path, target: str, path: str) -> str:
    try:
        result = subprocess.run(  # noqa: S603
            ("git", "show", f"{target}:{path}"),
            cwd=repo_root,
            check=True,
            capture_output=True,
        )
    except subprocess.CalledProcessError as error:
        message = f"cannot read target authoring file: {path}"
        raise ValueError(message) from error
    return result.stdout.decode("utf-8")


def _discover_release_units(
    repo_root: Path,
    target: str,
    paths: tuple[str, ...],
) -> tuple[ReleaseUnitDescriptor, ...]:
    descriptor_paths = tuple(
        path
        for path in paths
        if PurePosixPath(path).name == RELEASE_UNIT_BASENAME
    )
    descriptors = tuple(
        load_release_unit(
            repo_root / path,
            _target_content=_git_target_file(repo_root, target, path),
            _target_path=path,
        )
        for path in descriptor_paths
    )
    identities: set[str] = set()
    for descriptor in descriptors:
        if descriptor.release_unit in identities:
            message = (
                f"duplicate Release Unit identity: {descriptor.release_unit}"
            )
            raise ValueError(message)
        identities.add(descriptor.release_unit)
    return descriptors


def discover_release_units(
    repo_root: Path,
    target: str,
) -> tuple[ReleaseUnitDescriptor, ...]:
    """Discover strict Release Unit descriptors from the exact target tree."""
    return _discover_release_units(
        repo_root,
        target,
        _git_paths(repo_root, target),
    )


class MissingFirstSliceAuthoringError(ValueError):
    """The target identity is valid but required authoring is absent."""


class MissingFirstSliceDescriptorError(MissingFirstSliceAuthoringError):
    """The target identity is valid but its required descriptor is absent."""


class MissingFirstSliceQualitySelectionError(MissingFirstSliceAuthoringError):
    """The target identity is valid but its Quality selection is absent."""


class MissingFirstSliceReleasePolicyError(MissingFirstSliceAuthoringError):
    """The target identity is valid but its Release policy is absent."""


def load_first_slice_authoring(  # noqa: C901
    repo_root: Path,
    target: str,
) -> tuple[ReleaseUnitDescriptor, QualitySelection, ReleasePolicy]:
    """Load and correlate exact target-tree first-slice authoring."""
    target_paths = _git_paths(repo_root, target)
    target_path_set = frozenset(target_paths)
    descriptors = _discover_release_units(repo_root, target, target_paths)
    if not descriptors:
        message = "first-slice Release Unit descriptor is missing"
        raise MissingFirstSliceDescriptorError(message)
    if len(descriptors) != 1:
        message = (
            "first-slice authoring must contain exactly one Release Unit "
            "descriptor"
        )
        raise ValueError(message)
    expected_root = PurePosixPath("src/public/lib/hcoona-release-smoke-npm")
    expected_descriptor = (expected_root / RELEASE_UNIT_BASENAME).as_posix()
    matches = tuple(
        descriptor
        for descriptor in descriptors
        if descriptor.path == expected_descriptor
    )
    if len(matches) != 1:
        message = "first-slice Release Unit descriptor is missing"
        raise MissingFirstSliceDescriptorError(message)
    descriptor = matches[0]
    descriptor_root = expected_root
    quality_path = (descriptor_root / QUALITY_BASENAME).as_posix()
    if quality_path not in target_path_set:
        message = f"Quality selection does not exist: {quality_path}"
        raise MissingFirstSliceQualitySelectionError(message)
    if FIRST_SLICE_POLICY_PATH not in target_path_set:
        message = f"Release policy does not exist: {FIRST_SLICE_POLICY_PATH}"
        raise MissingFirstSliceReleasePolicyError(message)
    quality = load_quality_selection(
        repo_root / quality_path,
        _target_content=_git_target_file(repo_root, target, quality_path),
        _target_path=quality_path,
    )
    policy = load_release_policy(
        repo_root / FIRST_SLICE_POLICY_PATH,
        _target_content=_git_target_file(
            repo_root,
            target,
            FIRST_SLICE_POLICY_PATH,
        ),
        _target_path=FIRST_SLICE_POLICY_PATH,
    )
    if descriptor.release_unit != policy.release_unit:
        message = "Release Unit descriptor and policy identity mismatch"
        raise ValueError(message)
    if (
        len(descriptor.builds) != 1
        or descriptor.builds[0].build_id != "npm-package"
        or descriptor.builds[0].definition != "node/npm-package-v1"
        or descriptor.builds[0].entry_point != "package.json"
    ):
        message = " ".join(
            (
                "first-slice build selection must be exactly singleton",
                "npm-package",
            )
        )
        raise ValueError(message)
    if quality.ecosystems != (("node", "node/hcoona-release-smoke-npm-v1"),):
        message = (
            "first-slice Quality selection must be exactly singleton "
            "node/hcoona-release-smoke-npm-v1"
        )
        raise ValueError(message)
    for build in descriptor.builds:
        entry_path = (descriptor_root / build.entry_point).as_posix()
        if entry_path not in target_path_set:
            message = f"Build entry point does not exist: {build.entry_point}"
            raise ValueError(message)
        definition = BUILD_DEFINITIONS[build.definition]
        if {output.kind for output in build.outputs} != set(
            definition.output_kinds
        ):
            message = f"Build output kinds do not match: {build.build_id}"
            raise ValueError(message)
        expected_outputs = (("npm-tarball", "primary-package", "npm-tarball"),)
        actual_outputs = tuple(
            (output.output_id, output.role, output.kind)
            for output in build.outputs
        )
        if actual_outputs != expected_outputs:
            message = (
                "Build outputs do not match approved first-slice output: "
                f"{build.build_id}"
            )
            raise ValueError(message)
    return descriptor, quality, policy
