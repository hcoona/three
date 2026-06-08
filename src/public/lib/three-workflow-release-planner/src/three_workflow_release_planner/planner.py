"""Planner core for workflow-release plan and selector emission."""

from __future__ import annotations

import hashlib
import json
import os
import re
import shutil
import subprocess
import tomllib
from collections.abc import Callable, Mapping, Sequence
from contextlib import suppress
from dataclasses import dataclass
from pathlib import Path
from typing import Literal, cast

from three_workflow_release_authoring import (
    AuthoringSnapshot,
    AuthoringValidationError,
    ProjectDescriptor,
    TargetInstance,
    TargetUsage,
    validate_authoring,
)
from three_workflow_release_authoring import (
    diagnostics_document as authoring_diagnostics_document,
)
from three_workflow_release_contracts import (
    ContractValidationError,
    validate_contract,
)
from three_workflow_release_contracts.contracts import (
    _EXPECTED_CONTRACTS,
)

Json = dict[str, object]
RemoteObservation = Literal[
    "absent", "exact-satisfied", "partial", "conflicting"
]
_TOPOLOGIES = (
    "external-oidc-caller-workflow",
    "external-oidc-entry-workflow",
    "external-oidc-reusable-workflow",
    "github-token",
)
_SIGNER_WORKFLOW = "hcoona/three/.github/workflows/release-publish-node.yml"
_PYPI_NAME_RE = re.compile(r"^[A-Za-z0-9](?:[A-Za-z0-9._-]*[A-Za-z0-9])?$")
_NPM_NAME_RE = re.compile(r"^(?:@[a-z0-9][a-z0-9._-]*/)?[a-z0-9][a-z0-9._-]*$")
_GEM_NAME_RE = re.compile(r"^[a-z0-9._-]+$")


@dataclass(frozen=True, slots=True)
class PlannerInputs:
    """Inputs that affect first-delivery planner execution."""

    request: Mapping[str, object]
    repo_root: Path
    dry_run: bool = False
    validation_build: bool = False
    dotnet_metadata: Mapping[str, object] | None = None
    remote_observations: Mapping[str, RemoteObservation] | None = None
    official_frozen_versions: Mapping[str, Sequence[str]] | None = None


class PlannerError(ValueError):
    """Raised when planning fails closed with diagnostics."""

    def __init__(self, diagnostics: Sequence[Json]) -> None:
        """Initialize with one or more planner diagnostics."""
        self.diagnostics = tuple(diagnostics)
        message = "; ".join(str(item["message"]) for item in diagnostics)
        super().__init__(message)

    def document(self) -> Json:
        """Return the closed planner diagnostics document."""
        return diagnostics_document(self.diagnostics)


@dataclass(frozen=True, slots=True)
class PlanningResult:
    """Successful planner outputs."""

    plan: Json
    execution_sets: Json


def plan_from_repo(inputs: PlannerInputs) -> PlanningResult:
    """Validate authoring in *repo_root* and plan a release request."""
    try:
        snapshot = validate_authoring(inputs.repo_root)
    except AuthoringValidationError as exc:
        diagnostics = authoring_diagnostics_document(exc.issues)["diagnostics"]
        if isinstance(diagnostics, list):
            raise PlannerError([dict(item) for item in diagnostics]) from exc
        raise
    return plan_release(snapshot, inputs)


def plan_release(
    snapshot: AuthoringSnapshot,
    inputs: PlannerInputs,
) -> PlanningResult:
    """Build a frozen release plan and execution-set routing document."""
    diagnostics: list[Json] = []
    _validate_runtime_flags(inputs, diagnostics)
    try:
        validate_contract(inputs.request)
    except ContractValidationError as exc:
        diagnostics.append(
            _diagnostic(
                "REQ_INVALID_INPUT",
                "validation",
                "request",
                "planner request violates the closed contract",
                details={"issues": [issue.message for issue in exc.issues]},
            )
        )
    profile = str(inputs.request.get("profile", ""))
    force = _request_force(inputs.request)
    if profile == "official" and force:
        diagnostics.append(
            _diagnostic(
                "REQ_FORCE_FOR_OFFICIAL",
                "validation",
                "request",
                "force is not valid for official planning",
                details={},
            )
        )
    selected_project_ids = _selected_project_ids(
        snapshot, inputs.request, diagnostics
    )
    if diagnostics:
        raise PlannerError(diagnostics)
    metadata_input = snapshot.dotnet_metadata_input(
        str(inputs.request["commit-sha"])
    )
    dotnet_projects = {
        project_id
        for project_id in selected_project_ids
        if snapshot.projects[project_id].ecosystem == "dotnet"
    }
    dotnet_metadata = _validated_dotnet_metadata(
        inputs.dotnet_metadata,
        metadata_input,
        dotnet_projects,
        diagnostics,
    )
    if diagnostics:
        raise PlannerError(diagnostics)
    builder = _PlanBuilder(
        snapshot, inputs, selected_project_ids, dotnet_metadata
    )
    result = builder.build()
    validate_contract(result.plan)
    validate_contract(result.execution_sets)
    return result


def diagnostics_document(diagnostics: Sequence[Mapping[str, object]]) -> Json:
    """Create the closed planner diagnostics container."""
    document: Json = {
        "api-version": "three.release.planner-diagnostics/v1alpha1",
        "kind": "planner-diagnostics",
        "diagnostics": [dict(item) for item in diagnostics],
    }
    validate_contract(document)
    return document


class _PlanBuilder:
    """Stateful plan graph builder for one request."""

    def __init__(
        self,
        snapshot: AuthoringSnapshot,
        inputs: PlannerInputs,
        selected_project_ids: Sequence[str],
        dotnet_metadata: Mapping[str, Mapping[str, object]],
    ) -> None:
        self.snapshot = snapshot
        self.inputs = inputs
        self.selected_project_ids = tuple(selected_project_ids)
        self.dotnet_metadata = dotnet_metadata
        self.variants: dict[str, Json] = {}
        self.artifacts: dict[str, Json] = {}
        self.publish_nodes: dict[str, Json] = {}
        self.target_snapshots: dict[str, Json] = {}
        self.project_snapshots: dict[str, Json] = {}
        self.artifact_ids_by_descriptor: dict[tuple[str, str], str] = {}
        self.variant_ids_by_descriptor: dict[tuple[str, str], str] = {}
        self.pypi_filenames_by_project: dict[
            tuple[str, str, tuple[str, ...]], Mapping[str, str]
        ] = {}
        self.rubygems_metadata_by_project: dict[
            str, Mapping[str, str] | None
        ] = {}
        self.selected_commit_files: dict[
            str, tuple[str | None, str | None]
        ] = {}
        self.selected_pyprojects: dict[
            str, tuple[Mapping[str, object] | None, str | None]
        ] = {}
        self.diagnostics: list[Json] = []

    def _runtime_dir(self) -> Path:
        return (
            self.inputs.repo_root
            / ".copilot"
            / "three-workflow-release-planner"
        )

    def build(self) -> PlanningResult:
        """Build and return validated planner outputs."""
        self._validate_profile_coexistence()
        if self.diagnostics:
            raise PlannerError(self.diagnostics)
        for project_id in self.selected_project_ids:
            self._add_project(self.snapshot.projects[project_id])
        if self.diagnostics:
            raise PlannerError(self.diagnostics)
        plan_id = _plan_id(
            str(self.inputs.request["profile"]),
            str(self.inputs.request["commit-sha"]),
            self.selected_project_ids,
            {"force": _request_force(self.inputs.request)},
        )
        plan: Json = {
            "api-version": "three.release.plan/v1alpha1",
            "kind": "release-plan",
            "envelope": {
                "plan-id": plan_id,
                "profile": self.inputs.request["profile"],
                "commit-sha": self.inputs.request["commit-sha"],
                "request-flags": {"force": _request_force(self.inputs.request)},
                "requested-project-ids": _request_project_ids(
                    self.inputs.request
                ),
                "selected-project-ids": list(self.selected_project_ids),
                "authoring-inputs": self.snapshot.planner_authoring_inputs(),
                "projects": _sorted_dict(self.project_snapshots),
            },
            "graph": {
                "variants": _sorted_dict(self.variants),
                "artifacts": _sorted_dict(self.artifacts),
                "publish-nodes": _sorted_dict(self.publish_nodes),
                "target-instance-snapshots": _sorted_dict(
                    self.target_snapshots
                ),
            },
        }
        execution_sets = self._execution_sets(plan_id)
        return PlanningResult(plan=plan, execution_sets=execution_sets)

    def _validate_profile_coexistence(self) -> None:
        """Reject deferred package identity conflicts between profiles."""
        selected_profile = str(self.inputs.request["profile"])
        reported: set[tuple[str, str, str, str]] = set()
        for project_id in self.selected_project_ids:
            project = self.snapshot.projects[project_id]
            for selected_target in project.profiles[selected_profile]:
                selected_instance = self.snapshot.target_instances[
                    selected_target.uses
                ]
                if (
                    selected_instance.family == "github-release"
                    or selected_instance.capabilities.get(
                        "profile-coexistence-rule"
                    )
                    != "requires-distinct-name"
                ):
                    continue
                selected_identity = self._profile_coexistence_identity(
                    project, selected_target, selected_instance
                )
                if selected_identity is None:
                    continue
                for (
                    other_profile,
                    other_instance,
                    package_name,
                ) in self._profile_coexistence_conflicts(
                    project,
                    selected_profile,
                    selected_instance,
                    selected_identity,
                ):
                    report_key = (
                        project.project_id,
                        selected_profile,
                        selected_target.uses,
                        other_profile,
                    )
                    if report_key in reported:
                        continue
                    reported.add(report_key)
                    self.diagnostics.append(
                        _diagnostic(
                            "PUBLISH_IDENTITY_CONFLICT",
                            "normalization",
                            "project",
                            "buddy and official resolve the same "
                            "package-registry identity",
                            project_id=project.project_id,
                            details={
                                "profile": selected_profile,
                                "target": selected_instance.catalog_ref,
                                "conflicting-profile": other_profile,
                                "conflicting-target": (
                                    other_instance.catalog_ref
                                ),
                                "family": selected_instance.family,
                                "destination": dict(
                                    selected_instance.destination
                                ),
                                "package-name": package_name,
                            },
                        )
                    )

    def _profile_coexistence_identity(
        self,
        project: ProjectDescriptor,
        target: TargetUsage,
        instance: TargetInstance,
    ) -> tuple[str, object, object, str] | None:
        package_name = self._package_name(project, target, instance)
        if package_name is None:
            return None
        return _coexistence_identity(instance, package_name)

    def _profile_coexistence_conflicts(
        self,
        project: ProjectDescriptor,
        selected_profile: str,
        selected_instance: TargetInstance,
        selected_identity: tuple[str, object, object, str],
    ) -> list[tuple[str, TargetInstance, str]]:
        conflicts: list[tuple[str, TargetInstance, str]] = []
        for other_profile, targets in project.profiles.items():
            if other_profile == selected_profile:
                continue
            for other_target in targets:
                other_instance = self.snapshot.target_instances[
                    other_target.uses
                ]
                if _registry_key(other_instance) != _registry_key(
                    selected_instance
                ):
                    continue
                other_name = self._package_name(
                    project, other_target, other_instance
                )
                if (
                    other_name is not None
                    and _coexistence_identity(other_instance, other_name)
                    == selected_identity
                ):
                    conflicts.append(
                        (other_profile, other_instance, other_name)
                    )
        return conflicts

    def _add_project(self, project: ProjectDescriptor) -> None:
        version = self._resolved_version(project)
        if version is None:
            return
        if self._is_blocked_buddy_force(project, version):
            return
        project_variant_ids: list[str] = []
        project_publish_node_ids: list[str] = []
        for variant in project.variants:
            variant_id = _stable_id(
                "variant",
                {
                    "project-id": project.project_id,
                    "dimensions": dict(variant.dimensions),
                },
            )
            self.variant_ids_by_descriptor[(project.project_id, variant.id)] = (
                variant_id
            )
            artifact_ids: list[str] = []
            for artifact in variant.artifacts:
                artifact_id = _stable_id(
                    "artifact",
                    {
                        "project-id": project.project_id,
                        "variant-id": variant_id,
                        "descriptor-handle": artifact.id,
                        "role": artifact.role,
                        "kind-family": artifact.kind_family,
                        "concrete-kind": artifact.concrete_kind,
                        "projection": dict(sorted(artifact.projection.items())),
                    },
                )
                self.artifact_ids_by_descriptor[
                    (project.project_id, artifact.id)
                ] = artifact_id
                artifact_ids.append(artifact_id)
            self.variants[variant_id] = {
                "project-id": project.project_id,
                "descriptor-handle": variant.id,
                "dimensions": dict(sorted(variant.dimensions.items())),
                "artifact-ids": artifact_ids,
            }
            for artifact in variant.artifacts:
                artifact_id = self.artifact_ids_by_descriptor[
                    (project.project_id, artifact.id)
                ]
                self.artifacts[artifact_id] = {
                    "project-id": project.project_id,
                    "variant-id": variant_id,
                    "descriptor-handle": artifact.id,
                    "role": artifact.role,
                    "kind-family": artifact.kind_family,
                    "concrete-kind": artifact.concrete_kind,
                    "produced-from-artifact-ids": [
                        self.artifact_ids_by_descriptor[
                            (project.project_id, item)
                        ]
                        for item in artifact.produced_from
                    ],
                    "projection": dict(sorted(artifact.projection.items())),
                }
                if artifact.companions:
                    self.artifacts[artifact_id]["companions"] = [
                        {
                            "path": companion.path,
                            "role": companion.role,
                            "required": companion.required,
                        }
                        for companion in artifact.companions
                    ]
            project_variant_ids.append(variant_id)
        for index, target in enumerate(
            project.profiles[str(self.inputs.request["profile"])]
        ):
            node_id = self._add_publish_node(project, target, index, version)
            if node_id is not None:
                project_publish_node_ids.append(node_id)
        self.project_snapshots[project.project_id] = {
            "display-name": project.display_name,
            "ecosystem": project.ecosystem,
            "release-kind": project.release_kind,
            "descriptor-path": project.descriptor_path,
            "release-root": project.release_root,
            "source": {
                "primary-manifest-path": project.primary_manifest_path,
                "auxiliary-input-paths": list(project.auxiliary_input_paths),
                "version-authority-kind": project.version_authority_kind,
            },
            "resolved-version": version,
            "variant-ids": sorted(project_variant_ids),
            "publish-node-ids": sorted(project_publish_node_ids),
        }

    def _add_publish_node(
        self,
        project: ProjectDescriptor,
        target: TargetUsage,
        index: int,
        version: str,
    ) -> str | None:
        instance = self.snapshot.target_instances[target.uses]
        self._snapshot_target(instance)
        artifact_ids = [
            self.artifact_ids_by_descriptor[(project.project_id, artifact_id)]
            for artifact_id in target.artifacts
        ]
        identity = self._resolved_identity(
            project, target, instance, version, artifact_ids
        )
        if identity is None:
            return None
        projection = self._projection(
            project, target, instance, version, artifact_ids
        )
        if projection is None:
            return None
        node_id = _stable_id(
            "publish-node",
            {
                "project-id": project.project_id,
                "profile": self.inputs.request["profile"],
                "descriptor-target-index": index,
                "target-instance-snapshot-id": instance.catalog_ref,
                "artifact-ids": artifact_ids,
                "projection": projection,
            },
        )
        disposition = self._publish_outcome(
            project, instance, node_id, identity
        )
        if disposition is None:
            return None
        node: Json = {
            "publish-node-id": node_id,
            "project-id": project.project_id,
            "profile": self.inputs.request["profile"],
            "descriptor-target-index": index,
            "target-instance-snapshot-id": instance.catalog_ref,
            "artifact-ids": artifact_ids,
            "publish-disposition": disposition[0],
            "resolved-publish-identity": identity,
            "projection": projection,
        }
        if disposition[0] == "publish":
            node["publish-mode"] = disposition[1]
        if instance.family == "github-release":
            node["desired-publish-state"] = {
                "release-state": "release"
                if self.inputs.request["profile"] == "official"
                else "prerelease"
            }
            node["attestation"] = {"signer-workflow": _SIGNER_WORKFLOW}
        self.publish_nodes[node_id] = node
        return node_id

    def _snapshot_target(self, instance: TargetInstance) -> None:
        if instance.catalog_ref in self.target_snapshots:
            return
        self.target_snapshots[instance.catalog_ref] = {
            "family": instance.family,
            "instance-id": instance.instance_id,
            "catalog-ref": instance.catalog_ref,
            "contract": _contract_for(instance.contract),
            "destination": dict(instance.destination),
            "capabilities": dict(instance.capabilities),
        }

    def _resolved_version(self, project: ProjectDescriptor) -> str | None:
        if project.version_authority_kind == "nbgv-python-pyproject-version":
            data, error = self._selected_pyproject_data(
                project.primary_manifest_path
            )
            if data is not None:
                project_data = data.get("project")
                if isinstance(project_data, Mapping):
                    version = project_data.get("version")
                    if isinstance(version, str) and version:
                        return version
            self.diagnostics.append(
                _diagnostic(
                    "VERSION_AUTHORITY_FAILED",
                    "normalization",
                    "project",
                    "nbgv-python version could not be read from the "
                    "requested-commit pyproject.toml",
                    project_id=project.project_id,
                    details={
                        "primary-manifest-path": project.primary_manifest_path,
                        "error": error or "project.version is missing",
                    },
                )
            )
            return None
        if project.ecosystem == "dotnet":
            metadata = self.dotnet_metadata.get(project.project_id)
            version = metadata.get("resolved-version") if metadata else None
            if isinstance(version, str) and version:
                return version
            self.diagnostics.append(
                _diagnostic(
                    "DOTNET_METADATA_FAILED",
                    "normalization",
                    "project",
                    "required .NET metadata is missing resolved-version",
                    project_id=project.project_id,
                    details={
                        "primary-manifest-path": project.primary_manifest_path
                    },
                )
            )
            return None
        if project.version_authority_kind == "build-system-nbgv":
            return self._nbgv_version(project)
        self.diagnostics.append(
            _diagnostic(
                "VERSION_AUTHORITY_FAILED",
                "normalization",
                "project",
                "unknown version authority kind",
                project_id=project.project_id,
                details={
                    "version-authority-kind": project.version_authority_kind
                },
            )
        )
        return None

    def _nbgv_version(self, project: ProjectDescriptor) -> str | None:
        dotnet = shutil.which("dotnet") or "dotnet"
        try:
            result = subprocess.run(  # noqa: S603
                [
                    dotnet,
                    "tool",
                    "run",
                    "nbgv",
                    "--",
                    "get-version",
                    str(self.inputs.request["commit-sha"]),
                    "-p",
                    project.release_root,
                    "--format",
                    "json",
                ],
                cwd=self.inputs.repo_root,
                check=False,
                capture_output=True,
                text=True,
            )
        except OSError as exc:
            return self._record_nbgv_failure(project, str(exc))
        if result.returncode == 0:
            try:
                version = json.loads(result.stdout)["SemVer2"]
            except (KeyError, TypeError, json.JSONDecodeError) as exc:
                return self._record_nbgv_failure(project, str(exc))
            if isinstance(version, str) and version:
                return version
        return self._record_nbgv_failure(
            project, result.stderr.strip() or result.stdout.strip()
        )

    def _record_nbgv_failure(
        self, project: ProjectDescriptor, error: str
    ) -> None:
        self.diagnostics.append(
            _diagnostic(
                "VERSION_AUTHORITY_FAILED",
                "normalization",
                "project",
                "build-system NBGV version authority could not be resolved",
                project_id=project.project_id,
                details={
                    "version-authority-kind": project.version_authority_kind,
                    "error": error,
                },
            )
        )

    def _is_blocked_buddy_force(
        self, project: ProjectDescriptor, version: str
    ) -> bool:
        if self.inputs.request["profile"] != "buddy" or not _request_force(
            self.inputs.request
        ):
            return False
        frozen = self.inputs.official_frozen_versions
        if frozen is None or project.project_id not in frozen:
            self.diagnostics.append(
                _diagnostic(
                    "OFFICIAL_FROZEN_VERSION",
                    "classification",
                    "project",
                    "buddy force requires official-frozen evidence coverage",
                    project_id=project.project_id,
                    details={
                        "resolved-version": version,
                        "evidence": "missing",
                    },
                )
            )
            return True
        frozen_versions = frozen[project.project_id]
        if (
            isinstance(frozen_versions, str)
            or not isinstance(frozen_versions, Sequence)
            or any(
                not isinstance(item, str) or not item
                for item in frozen_versions
            )
        ):
            self.diagnostics.append(
                _diagnostic(
                    "OFFICIAL_FROZEN_VERSION",
                    "classification",
                    "project",
                    "buddy force requires well-formed official-frozen evidence",
                    project_id=project.project_id,
                    details={
                        "resolved-version": version,
                        "evidence": "malformed",
                    },
                )
            )
            return True
        if version in set(frozen_versions):
            self.diagnostics.append(
                _diagnostic(
                    "OFFICIAL_FROZEN_VERSION",
                    "classification",
                    "project",
                    "buddy force cannot target an official-frozen version",
                    project_id=project.project_id,
                    details={"resolved-version": version},
                )
            )
            return True
        return False

    def _has_remote_observation(
        self,
        project: ProjectDescriptor,
        instance: TargetInstance,
        node_id: str,
        identity: Mapping[str, object],
    ) -> bool:
        observations = self.inputs.remote_observations
        if self.inputs.dry_run and observations is None:
            return True
        if observations is not None and node_id in observations:
            return True
        if _is_external_oidc_instance(instance):
            return True
        self.diagnostics.append(
            _diagnostic(
                "REMOTE_CLASSIFICATION_FAILED",
                "classification",
                "publish-node",
                "remote publication observation is missing",
                project_id=project.project_id,
                publish_node_id=node_id,
                target_instance_snapshot_id=instance.catalog_ref,
                resolved_publish_identity=dict(identity),
                details={"remote-observation": "missing"},
            )
        )
        return False

    def _resolved_identity(
        self,
        project: ProjectDescriptor,
        target: TargetUsage,
        instance: TargetInstance,
        version: str,
        artifact_ids: Sequence[str],
    ) -> Json | None:
        if instance.family == "github-release":
            return {"release-tag": f"release/{project.project_id}/v{version}"}
        name = self._package_name(project, target, instance)
        if name is None:
            return None
        if (
            instance.family == "pypi"
            and project.version_authority_kind == "build-system-nbgv"
        ):
            pypi_version = self._pypi_identity_version(
                project, version, artifact_ids
            )
            if pypi_version is None:
                return None
            version = pypi_version
        if (
            instance.family == "rubygems"
            and project.version_authority_kind == "build-system-nbgv"
        ):
            metadata = self._rubygems_metadata(project)
            if metadata is None:
                return None
            version = metadata["version"]
        return {"package-name": name, "version": version}

    def _package_name(
        self,
        project: ProjectDescriptor,
        target: TargetUsage,
        instance: TargetInstance,
    ) -> str | None:
        if instance.family == "nuget":
            metadata = self.dotnet_metadata.get(project.project_id, {})
            package_id = metadata.get("package-id")
            if isinstance(package_id, str) and package_id:
                return package_id
            self.diagnostics.append(
                _diagnostic(
                    "DOTNET_METADATA_FAILED",
                    "normalization",
                    "project",
                    "required .NET package-id is missing",
                    project_id=project.project_id,
                    details={"target": instance.catalog_ref},
                )
            )
            return None
        if instance.family == "pypi":
            name = self._pyproject_name(project)
            if name is not None:
                return _pep503_name(name)
        if instance.family == "npm":
            name = self._npm_target_package_name(project, target)
            if isinstance(name, str) and _NPM_NAME_RE.match(name):
                return name
        if instance.family == "rubygems":
            metadata = self._rubygems_metadata(project)
            name = (
                metadata["name"]
                if metadata is not None
                else self._gemspec_name(project)
            )
            if name is not None and _GEM_NAME_RE.match(name):
                return name
        self.diagnostics.append(
            _diagnostic(
                "PUBLISH_IDENTITY_CONFLICT",
                "normalization",
                "project",
                "package-registry identity could not be resolved",
                project_id=project.project_id,
                details={"target": instance.catalog_ref},
            )
        )
        return None

    def _npm_target_package_name(
        self, project: ProjectDescriptor, target: TargetUsage
    ) -> str | None:
        """Resolve npm package name, preferring artifact-level projection."""
        if len(target.artifacts) == 1:
            artifact = project.artifacts_by_id.get(target.artifacts[0])
            if artifact is not None:
                projected = artifact.projection.get("package-name")
                if isinstance(projected, str):
                    return projected
        projected = target.projection.get("package-name")
        if isinstance(projected, str):
            return projected
        return self._package_json_name(project)

    def _projection(  # noqa: C901
        self,
        project: ProjectDescriptor,
        target: TargetUsage,
        instance: TargetInstance,
        version: str,
        artifact_ids: Sequence[str],
    ) -> Json | None:
        if instance.family == "github-release":
            names = {
                artifact_id: self._artifact_filename(
                    project, artifact_id, version, artifact_ids
                )
                for artifact_id in artifact_ids
            }
            if any(value is None for value in names.values()) or len(
                set(names.values())
            ) != len(names):
                self.diagnostics.append(
                    _diagnostic(
                        "PLAN_INTERNAL_INVARIANT",
                        "validation",
                        "project",
                        "GitHub Release asset names could not be uniquely "
                        "resolved",
                        project_id=project.project_id,
                        details={"target": instance.catalog_ref},
                    )
                )
                return None
            label_projection = target.projection.get("asset-labels", {})
            labels: dict[str, str] = {}
            if isinstance(label_projection, Mapping):
                for descriptor_id, label in label_projection.items():
                    key = self.artifact_ids_by_descriptor[
                        (project.project_id, str(descriptor_id))
                    ]
                    if isinstance(label, str):
                        labels[key] = label
            return {
                "asset-names-by-artifact-id": dict(sorted(names.items())),
                "asset-labels-by-artifact-id": dict(sorted(labels.items())),
            }
        if instance.family in {"nuget", "pypi", "npm", "rubygems"}:
            filenames = self._final_distribution_filenames(
                project, version, artifact_ids, instance
            )
            if filenames is None:
                return None
            if any(not value for value in filenames.values()):
                self.diagnostics.append(
                    _diagnostic(
                        "PYPI_FILENAME_COMPUTE_FAILED"
                        if instance.family == "pypi"
                        else "PLAN_INTERNAL_INVARIANT",
                        "normalization",
                        "project",
                        "final distribution filename could not be resolved",
                        project_id=project.project_id,
                        target_instance_snapshot_id=instance.catalog_ref,
                        details={"target": instance.catalog_ref},
                    )
                )
                return None
            projection: dict[str, object] = {
                "final-distribution-filenames-by-artifact-id": dict(
                    sorted(filenames.items())
                )
            }
            if instance.family == "npm":
                projected = self._npm_target_package_name(project, target)
                if isinstance(projected, str):
                    projection["package-name"] = projected
            return projection
        return {}

    def _final_distribution_filenames(
        self,
        project: ProjectDescriptor,
        version: str,
        artifact_ids: Sequence[str],
        instance: TargetInstance,
    ) -> Mapping[str, str] | None:
        if instance.family == "pypi":
            pypi_names = self._pypi_filenames(project, version, artifact_ids)
            if pypi_names is None:
                return None
            return {
                artifact_id: pypi_names[
                    str(self.artifacts[artifact_id]["concrete-kind"])
                ]
                for artifact_id in artifact_ids
            }
        return {
            artifact_id: self._artifact_filename(
                project, artifact_id, version, artifact_ids
            )
            or ""
            for artifact_id in artifact_ids
        }

    def _pypi_filenames(
        self,
        project: ProjectDescriptor,
        version: str,
        artifact_ids: Sequence[str],
    ) -> Mapping[str, str] | None:
        expected = {
            str(self.artifacts[artifact_id]["concrete-kind"])
            for artifact_id in artifact_ids
        }
        cache_key = (project.project_id, version, tuple(sorted(expected)))
        cached = self.pypi_filenames_by_project.get(cache_key)
        if cached is None:
            cached = self._compute_pypi_filenames(project, expected)
            if cached is None:
                return None
            self.pypi_filenames_by_project[cache_key] = cached
        if not expected <= set(cached):
            self._record_pypi_filename_failure(
                project,
                "build output did not contain every expected distribution kind",
                expected=sorted(expected),
                actual=sorted(cached),
            )
            return None
        return cached

    def _pypi_identity_version(
        self,
        project: ProjectDescriptor,
        version: str,
        artifact_ids: Sequence[str],
    ) -> str | None:
        filenames = self._pypi_filenames(project, version, artifact_ids)
        if filenames is None:
            return None
        wheel = filenames.get("wheel")
        sdist = filenames.get("sdist")
        if wheel is None:
            self._record_pypi_filename_failure(
                project,
                "build output did not contain a wheel name",
                actual=sorted(filenames),
            )
            return None
        wheel_version = self._pypi_wheel_version(project, wheel)
        sdist_version = (
            self._pypi_sdist_version(project, sdist)
            if sdist is not None
            else wheel_version
        )
        if wheel_version is None or wheel_version != sdist_version:
            self._record_pypi_filename_failure(
                project,
                "build output did not contain a consistent PyPI version",
                wheel=wheel,
                sdist=sdist,
                wheel_version=wheel_version,
                sdist_version=sdist_version,
            )
            return None
        return wheel_version

    def _pypi_wheel_version(
        self, project: ProjectDescriptor, filename: str
    ) -> str | None:
        package_name = self._pyproject_name(project)
        if package_name is None or not filename.endswith(".whl"):
            return None
        prefix = f"{_wheel_distribution_name(package_name)}-"
        if not filename.startswith(prefix):
            return None
        parts = filename.removesuffix(".whl")[len(prefix) :].split("-")
        if len(parts) not in {4, 5} or not parts[0]:
            return None
        if parts[-3:] != ["py3", "none", "any"]:
            return None
        return parts[0]

    def _pypi_sdist_version(
        self, project: ProjectDescriptor, filename: str
    ) -> str | None:
        package_name = self._pyproject_name(project)
        if package_name is None or not filename.endswith(".tar.gz"):
            return None
        stem = filename.removesuffix(".tar.gz")
        for prefix in _sdist_distribution_name_prefixes(package_name):
            marker = f"{prefix}-"
            if stem.startswith(marker):
                version = stem[len(marker) :]
                return version or None
        return None

    def _compute_pypi_filenames(
        self, project: ProjectDescriptor, expected_kinds: set[str]
    ) -> Mapping[str, str] | None:
        if "wheel" not in expected_kinds:
            self._record_pypi_filename_failure(
                project,
                "PyPI publish nodes must include a wheel artifact",
                expected=sorted(expected_kinds),
            )
            return None
        build_dir = (
            self._runtime_dir()
            / "pypi-filenames"
            / _digest(
                {
                    "project-id": project.project_id,
                    "manifest": project.primary_manifest_path,
                }
            )
        ).resolve()
        checkout_dir = self._materialize_pypi_build_checkout(project)
        if checkout_dir is None:
            return None
        shutil.rmtree(build_dir, ignore_errors=True)
        build_dir.mkdir(parents=True, exist_ok=True)
        uv = shutil.which("uv") or "uv"
        build_args = [
            uv,
            "build",
            "--out-dir",
            str(build_dir),
            "--no-create-gitignore",
            "--clear",
            project.release_root,
        ]
        if "wheel" in expected_kinds:
            build_args.insert(2, "--wheel")
        if "sdist" in expected_kinds:
            build_args.insert(3, "--sdist")
        try:
            try:
                result = subprocess.run(  # noqa: S603
                    build_args,
                    cwd=checkout_dir,
                    check=False,
                    capture_output=True,
                    text=True,
                )
            except OSError as exc:
                self._record_pypi_filename_failure(project, str(exc))
                return None
            if result.returncode != 0:
                self._record_pypi_filename_failure(
                    project, result.stderr.strip() or result.stdout.strip()
                )
                return None
            return self._read_pypi_build_outputs(
                project, build_dir, expected_kinds
            )
        finally:
            shutil.rmtree(build_dir, ignore_errors=True)
            self._remove_pypi_build_checkout(checkout_dir)

    def _materialize_pypi_build_checkout(
        self,
        project: ProjectDescriptor,
        *,
        checkout_kind: str = "pypi-checkouts",
        record_failure: Callable[[ProjectDescriptor, str], None] | None = None,
    ) -> Path | None:
        record_failure = record_failure or self._record_pypi_filename_failure
        checkout_dir = (
            self._runtime_dir()
            / checkout_kind
            / _digest(
                {
                    "project-id": project.project_id,
                    "commit-sha": str(self.inputs.request["commit-sha"]),
                }
            )
        ).resolve()
        self._remove_pypi_build_checkout(checkout_dir)
        git = shutil.which("git") or "git"
        try:
            result = subprocess.run(  # noqa: S603
                [
                    git,
                    "worktree",
                    "add",
                    "--detach",
                    "--force",
                    str(checkout_dir),
                    str(self.inputs.request["commit-sha"]),
                ],
                cwd=self.inputs.repo_root,
                check=False,
                capture_output=True,
                env={**os.environ, "GIT_LFS_SKIP_SMUDGE": "1"},
                text=True,
            )
        except OSError as exc:
            record_failure(project, str(exc))
            return None
        if result.returncode != 0:
            record_failure(
                project, result.stderr.strip() or result.stdout.strip()
            )
            return None
        self._remove_checkout_biome_config(checkout_dir)
        return checkout_dir

    def _remove_checkout_biome_config(self, checkout_dir: Path) -> None:
        for filename in ("biome.json", "biome.jsonc"):
            with suppress(OSError):
                (checkout_dir / filename).unlink()

    def _remove_pypi_build_checkout(self, checkout_dir: Path) -> None:
        git = shutil.which("git") or "git"
        with suppress(OSError):
            subprocess.run(  # noqa: S603
                [git, "worktree", "remove", "--force", str(checkout_dir)],
                cwd=self.inputs.repo_root,
                check=False,
                capture_output=True,
                text=True,
            )
        shutil.rmtree(checkout_dir, ignore_errors=True)
        with suppress(OSError):
            subprocess.run(  # noqa: S603
                [git, "worktree", "prune"],
                cwd=self.inputs.repo_root,
                check=False,
                capture_output=True,
                text=True,
            )

    def _read_pypi_build_outputs(
        self,
        project: ProjectDescriptor,
        build_dir: Path,
        expected_kinds: set[str],
    ) -> Mapping[str, str] | None:
        wheels = sorted(path.name for path in build_dir.glob("*.whl"))
        sdists = sorted(path.name for path in build_dir.glob("*.tar.gz"))
        if (
            len(wheels) != 1
            or ("sdist" in expected_kinds and len(sdists) != 1)
            or ("sdist" not in expected_kinds and len(sdists) > 1)
        ):
            self._record_pypi_filename_failure(
                project,
                "build output did not contain expected PyPI distributions",
                wheels=wheels,
                sdists=sdists,
                expected=sorted(expected_kinds),
            )
            return None
        if self._pypi_wheel_version(project, wheels[0]) is None:
            self._record_pypi_filename_failure(
                project,
                "build output wheel must be a py3-none-any distribution",
                wheel=wheels[0],
            )
            return None
        result = {"wheel": wheels[0]}
        if "sdist" in expected_kinds:
            result["sdist"] = sdists[0]
        return result

    def _record_pypi_filename_failure(
        self,
        project: ProjectDescriptor,
        error: str,
        **details: object,
    ) -> None:
        self.diagnostics.append(
            _diagnostic(
                "PYPI_FILENAME_COMPUTE_FAILED",
                "normalization",
                "project",
                "PyPI final distribution filenames could not be computed",
                project_id=project.project_id,
                details={"error": error, **details},
            )
        )

    def _artifact_filename(  # noqa: C901, PLR0911
        self,
        project: ProjectDescriptor,
        artifact_id: str,
        version: str,
        context_artifact_ids: Sequence[str] | None = None,
    ) -> str | None:
        artifact = self.artifacts[artifact_id]
        concrete = str(artifact["concrete-kind"])
        package_name = self._package_name_for_artifact(project, artifact_id)
        if concrete == "nuget" and package_name:
            return f"{package_name}.{version}.nupkg"
        if concrete == "snupkg" and package_name:
            return f"{package_name}.{version}.snupkg"
        if concrete == "wheel" and package_name:
            names = self._pypi_filenames(
                project, version, context_artifact_ids or [artifact_id]
            )
            return names.get("wheel") if names is not None else None
        if concrete == "sdist" and package_name:
            names = self._pypi_filenames(
                project, version, context_artifact_ids or [artifact_id]
            )
            return names.get("sdist") if names is not None else None
        if concrete == "npm-package" and package_name:
            base = _npm_pack_filename_base(package_name)
            return f"{base}-{version}.tgz"
        if concrete == "browser-zip":
            variant = self.variants[str(artifact["variant-id"])]
            token = _variant_token(variant["dimensions"])
            return f"{project.project_id}-{version}-{token}.zip"
        if concrete == "sources-zip":
            return f"{project.project_id}-{version}-sources.zip"
        if concrete == "rubygem" and package_name:
            if project.version_authority_kind == "build-system-nbgv":
                metadata = self._rubygems_metadata(project)
                return metadata["file_name"] if metadata is not None else None
            return f"{package_name}-{version}.gem"
        variant = self.variants[str(artifact["variant-id"])]
        token = _variant_token(variant["dimensions"])
        if concrete == "executable":
            if artifact.get("companions"):
                return f"{project.project_id}-{version}-{token}.zip"
            suffix = ".exe" if "windows" in token else ""
            return f"{project.project_id}-{version}-{token}{suffix}"
        if concrete == "inno-setup":
            return f"{project.project_id}-{version}-{token}-setup.exe"
        return None

    def _package_name_for_artifact(  # noqa: PLR0911
        self, project: ProjectDescriptor, artifact_id: str
    ) -> str | None:
        artifact = self.artifacts[artifact_id]
        concrete = artifact["concrete-kind"]
        family = {
            "nuget": "nuget",
            "snupkg": "nuget",
            "wheel": "pypi",
            "sdist": "pypi",
            "npm-package": "npm",
            "rubygem": "rubygems",
        }.get(str(concrete))
        if family is None:
            return None
        target = next(
            (
                target
                for target in project.profiles[
                    str(self.inputs.request["profile"])
                ]
                if self.snapshot.target_instances[target.uses].family == family
            ),
            None,
        )
        projected = artifact.get("projection")
        if (
            family == "npm"
            and isinstance(projected, Mapping)
            and isinstance(projected.get("package-name"), str)
        ):
            return str(projected["package-name"])
        if target is None:
            if family == "pypi":
                name = self._pyproject_name(project)
                return _pep503_name(name) if name else None
            if family == "npm":
                return self._package_json_name(project)
            if family == "rubygems":
                metadata = self._rubygems_metadata(project)
                return metadata["name"] if metadata is not None else None
            if family == "nuget":
                metadata = self.dotnet_metadata.get(project.project_id, {})
                package_id = metadata.get("package-id")
                return package_id if isinstance(package_id, str) else None
            return None
        return self._package_name(
            project, target, self.snapshot.target_instances[target.uses]
        )

    def _publish_outcome(  # noqa: PLR0911
        self,
        project: ProjectDescriptor,
        instance: TargetInstance,
        node_id: str,
        identity: Mapping[str, object],
    ) -> tuple[str, str | None] | None:
        if not self._has_remote_observation(
            project, instance, node_id, identity
        ):
            return None
        observations = self.inputs.remote_observations
        observation = (
            "absent"
            if observations is None or node_id not in observations
            else observations[node_id]
        )
        if observation == "exact-satisfied":
            return ("skip-satisfied", None)
        if instance.capabilities["mutability"] == "immutable":
            if observation == "partial":
                self.diagnostics.append(
                    _diagnostic(
                        "IMMUTABLE_PARTIAL_UNSUPPORTED",
                        "classification",
                        "publish-node",
                        "immutable partial publication is unsupported",
                        project_id=project.project_id,
                        publish_node_id=node_id,
                        target_instance_snapshot_id=instance.catalog_ref,
                        resolved_publish_identity=dict(identity),
                        details={},
                    )
                )
                return None
            if observation == "conflicting":
                self.diagnostics.append(
                    _diagnostic(
                        "REMOTE_CONFLICTING",
                        "classification",
                        "publish-node",
                        "immutable publication conflicts with frozen intent",
                        project_id=project.project_id,
                        publish_node_id=node_id,
                        target_instance_snapshot_id=instance.catalog_ref,
                        resolved_publish_identity=dict(identity),
                        details={},
                    )
                )
                return None
        if observation == "conflicting":
            self.diagnostics.append(
                _diagnostic(
                    "REMOTE_CONFLICTING",
                    "classification",
                    "publish-node",
                    "remote publication conflicts with frozen intent",
                    project_id=project.project_id,
                    publish_node_id=node_id,
                    target_instance_snapshot_id=instance.catalog_ref,
                    resolved_publish_identity=dict(identity),
                    details={"remote-observation": observation},
                )
            )
            return None
        if observation == "absent":
            return ("publish", "create-only")
        if (
            observation == "partial"
            and instance.family == "github-release"
            and self.inputs.request["profile"] == "official"
        ):
            self.diagnostics.append(
                _diagnostic(
                    "REMOTE_CONFLICTING",
                    "classification",
                    "publish-node",
                    "GitHub Release partial publication requires manual "
                    "reconciliation before official replay",
                    project_id=project.project_id,
                    publish_node_id=node_id,
                    target_instance_snapshot_id=instance.catalog_ref,
                    resolved_publish_identity=dict(identity),
                    details={"remote-observation": observation},
                )
            )
            return None
        if (
            observation == "partial"
            and self.inputs.request["profile"] == "buddy"
            and _request_force(self.inputs.request)
            and instance.capabilities["mutability"] == "mutable-prerelease"
        ):
            return ("publish", "overwrite-mutable")
        self.diagnostics.append(
            _diagnostic(
                "REMOTE_CONFLICTING",
                "classification",
                "publish-node",
                "remote publication conflicts with frozen intent",
                project_id=project.project_id,
                publish_node_id=node_id,
                target_instance_snapshot_id=instance.catalog_ref,
                resolved_publish_identity=dict(identity),
                details={"remote-observation": observation},
            )
        )
        return None

    def _execution_sets(self, plan_id: str) -> Json:
        publish_intent = sorted(
            node_id
            for node_id, node in self.publish_nodes.items()
            if node["publish-disposition"] == "publish"
        )
        active_nodes: list[str] = [] if self.inputs.dry_run else publish_intent
        selected_gh = sorted(
            node_id
            for node_id, node in self.publish_nodes.items()
            if self.snapshot.target_instances[
                str(node["target-instance-snapshot-id"])
            ].family
            == "github-release"
        )
        active_gh = sorted(set(selected_gh) & set(active_nodes))
        active_variants = sorted(
            {
                str(self.artifacts[artifact_id]["variant-id"])
                for node_id in publish_intent
                for artifact_id in _node_artifact_ids(
                    self.publish_nodes[node_id]
                )
                if not self.inputs.dry_run or self.inputs.validation_build
            }
        )
        selectors: dict[str, list[str]] = {
            topology: [] for topology in _TOPOLOGIES
        }
        for node_id in active_nodes:
            node = self.publish_nodes[node_id]
            instance = self.snapshot.target_instances[
                str(node["target-instance-snapshot-id"])
            ]
            selectors[instance.capabilities["publish-topology"]].append(node_id)
        return {
            "api-version": "three.release.execution-sets/v1alpha1",
            "kind": "execution-sets",
            "plan-id": plan_id,
            "dry-run": self.inputs.dry_run,
            "validation-build": self.inputs.validation_build,
            "publish-intent-node-ids": publish_intent,
            "active-variant-ids": active_variants,
            "active-publish-node-ids": active_nodes,
            "active-publish-selectors": {
                key: sorted(value) for key, value in sorted(selectors.items())
            },
            "skip-satisfied-publish-node-ids": sorted(
                node_id
                for node_id, node in self.publish_nodes.items()
                if node["publish-disposition"] == "skip-satisfied"
            ),
            "selected-github-release-publish-node-ids": selected_gh,
            "active-github-release-publish-node-ids": active_gh,
        }

    def _rubygems_metadata(  # noqa: PLR0911
        self, project: ProjectDescriptor
    ) -> Mapping[str, str] | None:
        if project.project_id in self.rubygems_metadata_by_project:
            return self.rubygems_metadata_by_project[project.project_id]
        checkout_dir = self._materialize_pypi_build_checkout(
            project,
            checkout_kind="rubygems-checkouts",
            record_failure=self._record_rubygems_metadata_failure,
        )
        if checkout_dir is None:
            self.rubygems_metadata_by_project[project.project_id] = None
            return None
        ruby = shutil.which("ruby") or "ruby"
        try:
            result = subprocess.run(  # noqa: S603
                [
                    ruby,
                    "-rrubygems",
                    "-rjson",
                    "-e",
                    (
                        "spec = Gem::Specification.load(ARGV.fetch(0)); "
                        "abort('gemspec did not load') if spec.nil?; "
                        "puts JSON.generate({"
                        "name: spec.name, "
                        "version: spec.version.to_s, "
                        "file_name: spec.file_name})"
                    ),
                    project.primary_manifest_path,
                ],
                cwd=checkout_dir,
                check=False,
                capture_output=True,
                text=True,
            )
        except OSError as exc:
            self._record_rubygems_metadata_failure(project, str(exc))
            self.rubygems_metadata_by_project[project.project_id] = None
            return None
        finally:
            self._remove_pypi_build_checkout(checkout_dir)
        if result.returncode != 0:
            self._record_rubygems_metadata_failure(
                project, result.stderr.strip() or result.stdout.strip()
            )
            self.rubygems_metadata_by_project[project.project_id] = None
            return None
        try:
            payload = json.loads(result.stdout)
        except json.JSONDecodeError as exc:
            self._record_rubygems_metadata_failure(project, str(exc))
            self.rubygems_metadata_by_project[project.project_id] = None
            return None
        name = payload.get("name") if isinstance(payload, Mapping) else None
        version = (
            payload.get("version") if isinstance(payload, Mapping) else None
        )
        file_name = (
            payload.get("file_name") if isinstance(payload, Mapping) else None
        )
        if (
            not isinstance(name, str)
            or not _GEM_NAME_RE.match(name)
            or not isinstance(version, str)
            or not version
            or not isinstance(file_name, str)
            or file_name != f"{name}-{version}.gem"
        ):
            self._record_rubygems_metadata_failure(
                project,
                "gemspec did not produce a valid name, version, and file_name",
            )
            self.rubygems_metadata_by_project[project.project_id] = None
            return None
        metadata = {"name": name, "version": version, "file_name": file_name}
        self.rubygems_metadata_by_project[project.project_id] = metadata
        return metadata

    def _record_rubygems_metadata_failure(
        self, project: ProjectDescriptor, error: str
    ) -> None:
        self.diagnostics.append(
            _diagnostic(
                "VERSION_AUTHORITY_FAILED",
                "normalization",
                "project",
                "RubyGems package metadata could not be resolved",
                project_id=project.project_id,
                details={
                    "primary-manifest-path": project.primary_manifest_path,
                    "error": error,
                },
            )
        )

    def _pyproject_name(self, project: ProjectDescriptor) -> str | None:
        data, _error = self._selected_pyproject_data(
            project.primary_manifest_path
        )
        if data is None:
            return None
        project_data = data.get("project")
        if not isinstance(project_data, Mapping):
            return None
        name = project_data.get("name")
        return (
            name
            if isinstance(name, str) and _PYPI_NAME_RE.match(name)
            else None
        )

    def _selected_pyproject_data(
        self, manifest_path: str
    ) -> tuple[Mapping[str, object] | None, str | None]:
        cached = self.selected_pyprojects.get(manifest_path)
        if cached is not None:
            return cached
        content, error = self._selected_commit_file(manifest_path)
        if content is None:
            result = (None, error)
            self.selected_pyprojects[manifest_path] = result
            return result
        try:
            data = tomllib.loads(content)
        except tomllib.TOMLDecodeError as exc:
            result = (None, str(exc))
            self.selected_pyprojects[manifest_path] = result
            return result
        result = (cast("Mapping[str, object]", data), None)
        self.selected_pyprojects[manifest_path] = result
        return result

    def _selected_commit_file(
        self, manifest_path: str
    ) -> tuple[str | None, str | None]:
        cached = self.selected_commit_files.get(manifest_path)
        if cached is not None:
            return cached
        path = Path(manifest_path)
        normalized = path.as_posix()
        if (
            path.is_absolute()
            or normalized == ".."
            or normalized.startswith("../")
        ):
            result = (None, "manifest path must be repository-relative")
            self.selected_commit_files[manifest_path] = result
            return result
        git = shutil.which("git") or "git"
        try:
            completed = subprocess.run(  # noqa: S603
                [
                    git,
                    "show",
                    f"{self.inputs.request['commit-sha']}:{normalized}",
                ],
                cwd=self.inputs.repo_root,
                check=False,
                capture_output=True,
                text=True,
            )
        except OSError as exc:
            result = (None, str(exc))
            self.selected_commit_files[manifest_path] = result
            return result
        if completed.returncode != 0:
            result = (
                None,
                completed.stderr.strip()
                or completed.stdout.strip()
                or "git show failed",
            )
            self.selected_commit_files[manifest_path] = result
            return result
        result = (completed.stdout, None)
        self.selected_commit_files[manifest_path] = result
        return result

    def _package_json_name(self, project: ProjectDescriptor) -> str | None:
        content, _error = self._selected_commit_file(
            project.primary_manifest_path
        )
        if content is None:
            return None
        try:
            data = json.loads(content)
        except json.JSONDecodeError:
            return None
        name = data.get("name")
        return (
            name if isinstance(name, str) and _NPM_NAME_RE.match(name) else None
        )

    def _gemspec_name(self, project: ProjectDescriptor) -> str | None:
        content, _error = self._selected_commit_file(
            project.primary_manifest_path
        )
        if content is None:
            return None
        match = re.search(r"spec\.name\s*=\s*[\"']([^\"']+)[\"']", content)
        if match is None:
            match = re.search(r"gem_name\s*=\s*[\"']([^\"']+)[\"']", content)
        return match.group(1) if match else None


def _validated_dotnet_metadata(
    dotnet_metadata: Mapping[str, object] | None,
    metadata_input: Mapping[str, object],
    required_project_ids: set[str],
    diagnostics: list[Json],
) -> Mapping[str, Mapping[str, object]]:
    if not required_project_ids:
        return {}
    if dotnet_metadata is None:
        for project_id in sorted(required_project_ids):
            diagnostics.append(
                _diagnostic(
                    "DOTNET_METADATA_FAILED",
                    "normalization",
                    "project",
                    "required .NET metadata file is missing",
                    project_id=project_id,
                    details={},
                )
            )
        return {}
    try:
        validate_contract(dotnet_metadata, metadata_input=metadata_input)
    except ContractValidationError as exc:
        diagnostics.append(
            _diagnostic(
                "DOTNET_METADATA_FAILED",
                "normalization",
                "request",
                ".NET metadata violates the closed contract",
                details={"issues": [issue.message for issue in exc.issues]},
            )
        )
        return {}
    projects = dotnet_metadata.get("projects")
    if not isinstance(projects, Mapping):
        return {}
    return {
        str(project_id): dict(value)
        for project_id, value in projects.items()
        if isinstance(value, Mapping)
    }


def _validate_runtime_flags(
    inputs: PlannerInputs, diagnostics: list[Json]
) -> None:
    if inputs.validation_build and not inputs.dry_run:
        diagnostics.append(
            _diagnostic(
                "REQ_INVALID_INPUT",
                "validation",
                "request",
                "validation-build requires dry-run",
                details={
                    "dry-run": inputs.dry_run,
                    "validation-build": inputs.validation_build,
                },
            )
        )


def _selected_project_ids(
    snapshot: AuthoringSnapshot,
    request: Mapping[str, object],
    diagnostics: list[Json],
) -> tuple[str, ...]:
    requested = request.get("requested-project-ids")
    if not isinstance(requested, list):
        return ()
    if not requested:
        return tuple(sorted(snapshot.projects))
    selected: list[str] = []
    for item in requested:
        if not isinstance(item, str) or item not in snapshot.projects:
            diagnostics.append(
                _diagnostic(
                    "REQ_PROJECT_NOT_FOUND",
                    "validation",
                    "project",
                    "requested project is not an in-scope releasable project",
                    project_id=str(item),
                    details={"project-id": item},
                )
            )
        else:
            selected.append(item)
    return tuple(sorted(selected))


def _request_force(request: Mapping[str, object]) -> bool:
    flags = request.get("request-flags")
    return bool(isinstance(flags, Mapping) and flags.get("force") is True)


def _request_project_ids(request: Mapping[str, object]) -> list[str]:
    requested = request.get("requested-project-ids")
    return (
        [item for item in requested if isinstance(item, str)]
        if isinstance(requested, list)
        else []
    )


def _registry_key(instance: TargetInstance) -> tuple[str, object, object]:
    return (
        instance.family,
        instance.destination.get("host"),
        instance.destination.get("owner"),
    )


def _coexistence_identity(
    instance: TargetInstance, package_name: str
) -> tuple[str, object, object, str]:
    name = (
        package_name.casefold() if instance.family == "nuget" else package_name
    )
    return (*_registry_key(instance), name)


def _node_artifact_ids(node: Mapping[str, object]) -> list[str]:
    artifact_ids = node.get("artifact-ids")
    if isinstance(artifact_ids, list):
        return [item for item in artifact_ids if isinstance(item, str)]
    return []


def _is_external_oidc_instance(instance: TargetInstance) -> bool:
    return instance.capabilities.get("credential-posture") == "oidc"


def _plan_id(
    profile: str,
    commit_sha: str,
    selected_project_ids: Sequence[str],
    flags: Mapping[str, object],
) -> str:
    return "plan/" + _digest(
        {
            "profile": profile,
            "commit-sha": commit_sha,
            "selected-project-ids": list(selected_project_ids),
            "request-flags": dict(flags),
        }
    )


def _stable_id(prefix: str, payload: Mapping[str, object]) -> str:
    return f"{prefix}/{_digest(payload)}"


def _digest(payload: Mapping[str, object]) -> str:
    encoded = json.dumps(
        payload, ensure_ascii=False, separators=(",", ":"), sort_keys=True
    )
    return hashlib.sha256(encoded.encode("utf-8")).hexdigest()


def _sorted_dict(value: Mapping[str, object]) -> Json:
    return {key: value[key] for key in sorted(value)}


def _pep503_name(name: str) -> str:
    return re.sub(r"[-_.]+", "-", name).lower()


def _wheel_component(name: str) -> str:
    return re.sub(r"[-_.]+", "_", name).lower()


def _wheel_distribution_name(name: str) -> str:
    return _wheel_component(name)


def _sdist_distribution_name_prefixes(name: str) -> tuple[str, ...]:
    return tuple(
        dict.fromkeys(
            (
                _wheel_component(name),
                _pep503_name(name),
                name,
            )
        )
    )


def _version_component(version: str) -> str:
    return version.replace("-", "_")


def _variant_token(dimensions: object) -> str:
    if not isinstance(dimensions, Mapping) or not dimensions:
        return "default"
    return "-".join(str(value) for _, value in sorted(dimensions.items()))


def _contract_for(contract_id: str) -> Json:
    expected = cast("Mapping[str, object]", _EXPECTED_CONTRACTS[contract_id])
    return {"id": contract_id, **expected}


def _diagnostic(  # noqa: PLR0913
    code: str,
    phase: str,
    scope_kind: str,
    message: str,
    *,
    project_id: str | None = None,
    publish_node_id: str | None = None,
    target_instance_snapshot_id: str | None = None,
    resolved_publish_identity: Mapping[str, object] | None = None,
    details: Mapping[str, object],
) -> Json:
    diagnostic: Json = {
        "api-version": "three.release.planner-diagnostic/v1alpha1",
        "kind": "planner-diagnostic",
        "code": code,
        "message": message,
        "phase": phase,
        "scope-kind": scope_kind,
        "blocking": True,
        "details": dict(details),
    }
    if project_id is not None:
        diagnostic["project-id"] = project_id
    if publish_node_id is not None:
        diagnostic["publish-node-id"] = publish_node_id
    if target_instance_snapshot_id is not None:
        diagnostic["target-instance-snapshot-id"] = target_instance_snapshot_id
    if resolved_publish_identity is not None:
        diagnostic["resolved-publish-identity"] = dict(
            resolved_publish_identity
        )
    validate_contract(diagnostics_document_unchecked([diagnostic]))
    return diagnostic


def diagnostics_document_unchecked(
    diagnostics: Sequence[Mapping[str, object]],
) -> Json:
    """Create a diagnostics document without recursive diagnostic validation."""
    return {
        "api-version": "three.release.planner-diagnostics/v1alpha1",
        "kind": "planner-diagnostics",
        "diagnostics": [dict(item) for item in diagnostics],
    }


def _npm_pack_filename_base(package_name: str) -> str:
    """Return the npm pack filename base for a package name."""
    if package_name.startswith("@") and "/" in package_name:
        return package_name[1:].replace("/", "-")
    return package_name
