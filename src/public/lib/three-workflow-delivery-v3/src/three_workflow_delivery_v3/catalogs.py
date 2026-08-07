"""Static same-revision catalogs for the first Workflow Delivery v3 slice."""

from __future__ import annotations

import json
from dataclasses import asdict, dataclass
from types import MappingProxyType
from typing import TYPE_CHECKING

from three_workflow_delivery_v3.canonical import canonical_sha256

if TYPE_CHECKING:
    from collections.abc import Mapping

    from three_workflow_delivery_v3.canonical import JsonValue


@dataclass(frozen=True, slots=True)
class BuildDefinition:
    """Mechanical contract for one artifact build."""

    logical_id: str
    ecosystem: str
    operation: str
    implementation_id: str
    execution_class: str
    capability_requirements: tuple[str, ...]
    output_kinds: tuple[str, ...]
    required_native_projections: tuple[str, ...]


@dataclass(frozen=True, slots=True)
class QualityDefinition:
    """Mechanical contract for one quality check."""

    logical_id: str
    subject: str
    operation: str
    implementation_id: str
    execution_class: str
    capability_requirements: tuple[str, ...]


@dataclass(frozen=True, slots=True)
class QualityPreset:
    """Static expansion selected by quality authoring."""

    logical_id: str
    required: tuple[str, ...]
    advisory: tuple[str, ...] = ()


@dataclass(frozen=True, slots=True)
class DestinationDefinition:
    """Logical destination without an executable Adapter implementation."""

    logical_id: str
    ecosystem: str
    registry: str
    supported_channels: tuple[str, ...]
    execution_class: str
    capability_requirements: tuple[str, ...]
    live_mutation_status: str


@dataclass(frozen=True, slots=True)
class ExecutionClassDefinition:
    """Closed execution trust and side-effect contract."""

    logical_id: str
    evaluates_target_content: bool
    permits_side_effects: bool
    privilege: str


@dataclass(frozen=True, slots=True)
class CapabilityDefinition:
    """Closed platform capability requirement."""

    logical_id: str
    github_permissions: tuple[tuple[str, str], ...]
    permits_mutation: bool


@dataclass(frozen=True, slots=True)
class ReleasePolicyRegistration:
    """Static registration for one context-owned Release policy."""

    logical_id: str
    release_unit: str
    path: str


type CatalogRecord = (
    BuildDefinition
    | QualityDefinition
    | QualityPreset
    | DestinationDefinition
    | ExecutionClassDefinition
    | CapabilityDefinition
    | ReleasePolicyRegistration
)


BUILD_DEFINITIONS: Mapping[str, BuildDefinition] = MappingProxyType(
    {
        "node/npm-package-v1": BuildDefinition(
            logical_id="node/npm-package-v1",
            ecosystem="node",
            operation="npm-package",
            implementation_id="node/npm-package-v1",
            execution_class="target-execution/unprivileged-v1",
            capability_requirements=(),
            output_kinds=("npm-tarball",),
            required_native_projections=("npmPackageVersion",),
        ),
    }
)

QUALITY_DEFINITIONS: Mapping[str, QualityDefinition] = MappingProxyType(
    {
        definition.logical_id: definition
        for definition in (
            QualityDefinition(
                "node/project-build-v1",
                "project-node",
                "project-build",
                "node/project-build-v1",
                "target-execution/unprivileged-v1",
                (),
            ),
            QualityDefinition(
                "node/project-test-v1",
                "project-node",
                "project-test",
                "node/project-test-v1",
                "target-execution/unprivileged-v1",
                (),
            ),
            QualityDefinition(
                "repository/source-tree-conformance-v1",
                "repository",
                "source-tree-conformance",
                "repository/source-tree-conformance-v1",
                "control/read-only-v1",
                (),
            ),
            QualityDefinition(
                "node/npm-artifact-v1",
                "release-unit-variant",
                "npm-artifact",
                "node/npm-artifact-v1",
                "target-execution/unprivileged-v1",
                (),
            ),
            QualityDefinition(
                "node/npm-artifact-contents-v1",
                "npm-tarball",
                "npm-artifact-contents",
                "node/npm-artifact-contents-v1",
                "target-execution/unprivileged-v1",
                (),
            ),
            QualityDefinition(
                "node/npm-install-import-v1",
                "npm-tarball",
                "npm-install-import",
                "node/npm-install-import-v1",
                "target-execution/unprivileged-v1",
                (),
            ),
        )
    }
)

QUALITY_PRESETS: Mapping[str, QualityPreset] = MappingProxyType(
    {
        "node/hcoona-release-smoke-npm-v1": QualityPreset(
            logical_id="node/hcoona-release-smoke-npm-v1",
            required=(
                "node/project-build-v1",
                "node/project-test-v1",
            ),
        ),
    }
)

DESTINATION_DEFINITIONS: Mapping[str, DestinationDefinition] = MappingProxyType(
    {
        definition.logical_id: definition
        for definition in (
            DestinationDefinition(
                "npm/github-packages-hcoona-three-v1",
                "npm",
                "https://npm.pkg.github.com",
                ("buddy",),
                "side-effect/privileged-v1",
                ("github/packages-write-v1",),
                "requires-create-or-exact-acceptance",
            ),
            DestinationDefinition(
                "npm/npmjs-public-v1",
                "npm",
                "https://registry.npmjs.org",
                ("official",),
                "side-effect/privileged-v1",
                ("npmjs/trusted-publishing-oidc-v1",),
                "simulation-only-in-first-slice",
            ),
        )
    }
)

EXECUTION_CLASSES: Mapping[str, ExecutionClassDefinition] = MappingProxyType(
    {
        definition.logical_id: definition
        for definition in (
            ExecutionClassDefinition(
                logical_id="control/read-only-v1",
                evaluates_target_content=False,
                permits_side_effects=False,
                privilege="read-only",
            ),
            ExecutionClassDefinition(
                logical_id="target-evaluation/unprivileged-v1",
                evaluates_target_content=True,
                permits_side_effects=False,
                privilege="unprivileged",
            ),
            ExecutionClassDefinition(
                logical_id="target-execution/unprivileged-v1",
                evaluates_target_content=True,
                permits_side_effects=False,
                privilege="unprivileged",
            ),
            ExecutionClassDefinition(
                logical_id="side-effect/privileged-v1",
                evaluates_target_content=False,
                permits_side_effects=True,
                privilege="privileged",
            ),
        )
    }
)

CAPABILITIES: Mapping[str, CapabilityDefinition] = MappingProxyType(
    {
        definition.logical_id: definition
        for definition in (
            CapabilityDefinition(
                logical_id="github/contents-read-v1",
                github_permissions=(("contents", "read"),),
                permits_mutation=False,
            ),
            CapabilityDefinition(
                logical_id="github/actions-read-v1",
                github_permissions=(("actions", "read"),),
                permits_mutation=False,
            ),
            CapabilityDefinition(
                logical_id="github/packages-read-v1",
                github_permissions=(("packages", "read"),),
                permits_mutation=False,
            ),
            CapabilityDefinition(
                logical_id="github/packages-write-v1",
                github_permissions=(
                    ("contents", "read"),
                    ("packages", "write"),
                ),
                permits_mutation=True,
            ),
            CapabilityDefinition(
                logical_id="npmjs/trusted-publishing-oidc-v1",
                github_permissions=(
                    ("contents", "read"),
                    ("id-token", "write"),
                ),
                permits_mutation=True,
            ),
        )
    }
)

RELEASE_POLICIES: Mapping[str, ReleasePolicyRegistration] = MappingProxyType(
    {
        "hcoona-release-smoke-npm": ReleasePolicyRegistration(
            logical_id="hcoona-release-smoke-npm",
            release_unit="hcoona-release-smoke-npm",
            path=(
                "eng/workflow-delivery/v3/policies/hcoona-release-smoke-npm.yml"
            ),
        ),
    }
)


def _record_document(record: CatalogRecord) -> dict[str, JsonValue]:
    document: dict[str, JsonValue] = json.loads(json.dumps(asdict(record)))
    return document


def catalog_document() -> dict[str, JsonValue]:
    """Return the complete canonically ordered first-slice catalog."""
    sections: tuple[tuple[str, Mapping[str, CatalogRecord]], ...] = (
        ("build-definitions", BUILD_DEFINITIONS),
        ("quality-definitions", QUALITY_DEFINITIONS),
        ("quality-presets", QUALITY_PRESETS),
        ("destination-definitions", DESTINATION_DEFINITIONS),
        ("execution-classes", EXECUTION_CLASSES),
        ("capabilities", CAPABILITIES),
        ("release-policies", RELEASE_POLICIES),
    )
    document: dict[str, JsonValue] = {
        "schema": "workflow-delivery/v3/static-catalog"
    }
    for name, records in sections:
        document[name] = {
            logical_id: _record_document(records[logical_id])
            for logical_id in sorted(records)
        }
    return document


def catalog_digest() -> str:
    """Return the immutable same-revision catalog digest."""
    return canonical_sha256(catalog_document())


def require_catalog_id(
    records: Mapping[str, object],
    logical_id: str,
    *,
    kind: str,
) -> None:
    """Reject an author-selected logical ID outside a static allowlist."""
    if logical_id not in records:
        message = f"unknown {kind} catalog ID: {logical_id}"
        raise ValueError(message)
