"""Exact-target Node and NBGV Provider for the first v3 slice."""

from __future__ import annotations

import hashlib
import json
import re
import subprocess
from collections.abc import Callable, Iterator
from contextlib import contextmanager
from dataclasses import dataclass
from pathlib import Path, PurePosixPath
from tempfile import TemporaryDirectory

from three_workflow_delivery_v3.canonical import JsonValue, canonical_sha256
from three_workflow_delivery_v3.catalogs import catalog_digest
from three_workflow_delivery_v3.repository.descriptors import (
    FIRST_SLICE_PACKAGE,
)

type CommandRunner = Callable[[tuple[str, ...], Path], str]

PROVIDER_LOGICAL_ID = "node/pnpm-nbgv-v1"
PROVIDER_IMPLEMENTATION_ID = "three-workflow-delivery-v3/node-pnpm-nbgv-v1"
PROVIDER_EXECUTION_MODE = "target-evaluating"
PROVIDER_EXECUTION_CLASS = "target-evaluation/unprivileged-v1"
NODE_PROVIDER_RESULT_SCHEMA = "workflow-delivery/v3/node-provider-result"
NODE_PROVIDER_FACT_BUNDLE_SCHEMA = (
    "workflow-delivery/v3/node-provider-fact-bundle"
)

_SHA_PATTERN = re.compile(r"[0-9a-f]{40}\Z")
_DIGEST_PATTERN = re.compile(r"sha256:[0-9a-f]{64}\Z")
_CANONICAL_VERSION_PATTERN = re.compile(
    r"(?:0|[1-9][0-9]*)(?:\.(?:0|[1-9][0-9]*)){1,3}\Z"
)
_SEMVER_NUMERIC_IDENTIFIER = r"(?:0|[1-9][0-9]*)"
_SEMVER_PRERELEASE_IDENTIFIER = (
    rf"(?:{_SEMVER_NUMERIC_IDENTIFIER}|"
    r"[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*)"
)
_SEMVER_BUILD_IDENTIFIER = r"[0-9A-Za-z-]+"
_NPM_VERSION_PATTERN = re.compile(
    rf"{_SEMVER_NUMERIC_IDENTIFIER}\."
    rf"{_SEMVER_NUMERIC_IDENTIFIER}\."
    rf"{_SEMVER_NUMERIC_IDENTIFIER}"
    rf"(?:-{_SEMVER_PRERELEASE_IDENTIFIER}"
    rf"(?:\.{_SEMVER_PRERELEASE_IDENTIFIER})*)?"
    rf"(?:\+{_SEMVER_BUILD_IDENTIFIER}"
    rf"(?:\.{_SEMVER_BUILD_IDENTIFIER})*)?\Z"
)
_PROVIDER_TOOLCHAIN_NAMES = ("node", "pnpm")
_TOOLCHAIN_ENTRY_FIELD_COUNT = 2
_FIRST_SLICE_BUILD_CAPABILITIES = ("node/npm-package-v1",)
FIRST_SLICE_REQUIRED_GLOBAL_INPUTS = (
    "package.json",
    "pnpm-lock.yaml",
    "pnpm-workspace.yaml",
)
AUTHORITATIVE_REMOTE = "origin"
TAG_REFSPEC = "refs/tags/*:refs/tags/*"
_PURPOSES = frozenset(
    {
        "ci-pr-slice-shadow",
        "slice-validation",
        "live-release",
        "release-simulation",
        "destination-acceptance",
    }
)
NBGV_ENVIRONMENT_ALLOWLIST = (
    "APPDATA",
    "COMSPEC",
    "DOTNET_CLI_HOME",
    "DOTNET_ROOT",
    "DOTNET_ROOT_X64",
    "DOTNET_ROOT_X86",
    "HOME",
    "HOMEDRIVE",
    "HOMEPATH",
    "LANG",
    "LC_ALL",
    "LOCALAPPDATA",
    "NUGET_PACKAGES",
    "PATH",
    "PATHEXT",
    "SystemRoot",
    "TEMP",
    "TMP",
    "TMPDIR",
    "USERPROFILE",
    "WINDIR",
)
_NBGV_NEUTRAL_ENVIRONMENT = {
    "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
    "DOTNET_NOLOGO": "1",
    "IGNORE_GITHUB_REF": "true",
}
_NBGV_PROGRAM = f"""\
const allowedEnvironment = new Set(
  {json.dumps(NBGV_ENVIRONMENT_ALLOWLIST)}.map(name => name.toUpperCase())
);
for (const name of Object.keys(process.env)) {{
  if (!allowedEnvironment.has(name.toUpperCase())) {{
    delete process.env[name];
  }}
}}
Object.assign(process.env, {json.dumps(_NBGV_NEUTRAL_ENVIRONMENT)});
const nbgv = await import('nerdbank-gitversioning');
const facts = await nbgv.getVersion(process.cwd());
process.stdout.write(JSON.stringify(facts));
"""


@dataclass(frozen=True, slots=True)
class CheckoutMaterialization:
    """Trusted checkout-step contract supplied to the Provider."""

    fetch_depth: int
    credentials_persisted: bool


@dataclass(frozen=True, slots=True)
class ProviderBinding:
    """Purpose and current-run binding for one Provider request."""

    request_id: str
    purpose: str
    workflow_run_id: int
    run_attempt: int
    target: str
    producer: str
    control: str
    catalog_digest: str
    request_digest: str


@dataclass(frozen=True, slots=True)
class CheckoutEvidence:
    """Verified exact-target and full-history checkout facts."""

    target: str
    head: str
    shallow: bool
    ancestry_complete: bool
    tags_complete: bool
    credentials_persisted: bool
    authoritative_remote: str
    authoritative_remote_url: str
    tag_refspec: str


@dataclass(frozen=True, slots=True)
class GlobalInput:
    """One exact repository-global input affecting Project Nodes."""

    path: str
    content_digest: str
    project_ids: tuple[str, ...]

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical global-input fact."""
        project_ids: list[JsonValue] = list(self.project_ids)
        return {
            "path": self.path,
            "content-digest": self.content_digest,
            "project-ids": project_ids,
        }


@dataclass(frozen=True, slots=True)
class ProjectNode:
    """Normalized first-slice PNPM Project Node."""

    project_id: str
    package_name: str
    path: str
    manifest_path: str
    private: bool
    workspace_dependencies: tuple[str, ...]


@dataclass(frozen=True, slots=True)
class NbgvFacts:
    """Authoritative canonical and native facts from one Node API call."""

    canonical_version: str
    sem_ver1: str
    sem_ver2: str
    version_height: int
    git_commit_id: str
    public_release: bool
    npm_package_version: str
    node_api_result_digest: str

    def to_document(self) -> dict[str, JsonValue]:
        """Return the immutable fact set without changing projections."""
        canonical: dict[str, JsonValue] = {
            "version": self.canonical_version,
            "semVer1": self.sem_ver1,
            "semVer2": self.sem_ver2,
            "versionHeight": self.version_height,
            "gitCommitId": self.git_commit_id,
            "publicRelease": self.public_release,
        }
        native: dict[str, JsonValue] = {
            "npmPackageVersion": self.npm_package_version,
        }
        return {
            "canonical": canonical,
            "native": native,
            "node-api-result-digest": self.node_api_result_digest,
        }


@dataclass(frozen=True, slots=True)
class NodeProviderResult:
    """One terminal target-bound Node/NBGV Provider Result."""

    binding: ProviderBinding
    provider_logical_id: str
    provider_implementation_id: str
    execution_mode: str
    execution_class: str
    toolchain: tuple[tuple[str, str], ...]
    manifest_digest: str
    configuration_digest: str
    checkout: CheckoutEvidence
    project_nodes: tuple[ProjectNode, ...]
    global_inputs: tuple[GlobalInput, ...]
    build_capabilities: tuple[str, ...]
    nbgv: NbgvFacts
    unresolved: tuple[str, ...]
    conflicts: tuple[str, ...]
    outcome: str
    diagnostic_reference: str | None

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical Provider Result payload."""
        binding: dict[str, JsonValue] = {
            "request-id": self.binding.request_id,
            "purpose": self.binding.purpose,
            "workflow-run-id": self.binding.workflow_run_id,
            "run-attempt": self.binding.run_attempt,
            "target": self.binding.target,
            "producer": self.binding.producer,
            "control": self.binding.control,
            "catalog-digest": self.binding.catalog_digest,
            "request-digest": self.binding.request_digest,
        }
        toolchain: dict[str, JsonValue] = dict(self.toolchain)
        provider: dict[str, JsonValue] = {
            "logical-id": self.provider_logical_id,
            "implementation-id": self.provider_implementation_id,
            "execution-mode": self.execution_mode,
            "execution-class": self.execution_class,
            "toolchain": toolchain,
        }
        input_digests: dict[str, JsonValue] = {
            "manifest": self.manifest_digest,
            "configuration": self.configuration_digest,
        }
        checkout: dict[str, JsonValue] = {
            "target": self.checkout.target,
            "head": self.checkout.head,
            "shallow": self.checkout.shallow,
            "ancestry-complete": self.checkout.ancestry_complete,
            "tags-complete": self.checkout.tags_complete,
            "credentials-persisted": self.checkout.credentials_persisted,
            "authoritative-remote": self.checkout.authoritative_remote,
            "authoritative-remote-url": (
                self.checkout.authoritative_remote_url
            ),
            "tag-refspec": self.checkout.tag_refspec,
        }
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
        global_inputs: list[JsonValue] = [
            item.to_document() for item in self.global_inputs
        ]
        build_capabilities: list[JsonValue] = list(self.build_capabilities)
        unresolved: list[JsonValue] = list(self.unresolved)
        conflicts: list[JsonValue] = list(self.conflicts)
        return {
            "schema": NODE_PROVIDER_RESULT_SCHEMA,
            "binding": binding,
            "provider": provider,
            "input-digests": input_digests,
            "checkout": checkout,
            "project-nodes": projects,
            "global-inputs": global_inputs,
            "build-capabilities": build_capabilities,
            "nbgv": self.nbgv.to_document(),
            "unresolved": unresolved,
            "conflicts": conflicts,
            "outcome": self.outcome,
            "diagnostic-reference": self.diagnostic_reference,
        }

    @property
    def result_digest(self) -> str:
        """Return the canonical terminal Provider Result digest."""
        return canonical_sha256(self.to_document())


@dataclass(frozen=True, slots=True)
class NodeProviderFactBundle:
    """Immutable transport schema for one target-evaluating Provider Result."""

    schema: str
    binding: ProviderBinding
    manifest_digest: str
    manifest_entry_id: str
    request_artifact_id: int
    request_artifact_digest: str
    provider_result: NodeProviderResult
    provider_result_digest: str
    transport_id: int
    transport_digest: str

    def to_document(self) -> dict[str, JsonValue]:
        """Return the complete canonical Fact Bundle payload."""
        binding: dict[str, JsonValue] = {
            "request-id": self.binding.request_id,
            "purpose": self.binding.purpose,
            "workflow-run-id": self.binding.workflow_run_id,
            "run-attempt": self.binding.run_attempt,
            "target": self.binding.target,
            "producer": self.binding.producer,
            "control": self.binding.control,
            "catalog-digest": self.binding.catalog_digest,
            "request-digest": self.binding.request_digest,
        }
        request_artifact: dict[str, JsonValue] = {
            "artifact-id": self.request_artifact_id,
            "artifact-digest": self.request_artifact_digest,
        }
        provider_result: dict[str, JsonValue] = {
            "payload": self.provider_result.to_document(),
            "payload-canonical-digest": self.provider_result_digest,
        }
        transport: dict[str, JsonValue] = {
            "artifact-id": self.transport_id,
            "artifact-digest": self.transport_digest,
        }
        return {
            "schema": self.schema,
            "binding": binding,
            "provider-request-manifest-digest": self.manifest_digest,
            "provider-request-entry-id": self.manifest_entry_id,
            "request-artifact": request_artifact,
            "provider-result": provider_result,
            "transport": transport,
        }

    @property
    def bundle_digest(self) -> str:
        """Return the canonical immutable Fact Bundle digest."""
        return canonical_sha256(self.to_document())


def create_node_provider_fact_bundle(  # noqa: PLR0913
    result: NodeProviderResult,
    *,
    manifest_digest: str,
    manifest_entry_id: str,
    request_artifact_id: int,
    request_artifact_digest: str,
    transport_id: int,
    transport_digest: str,
) -> NodeProviderFactBundle:
    """Create one immutable target-evaluating Provider Fact Bundle."""
    validate_node_provider_result(result)
    _digest(manifest_digest, field="Fact Bundle manifest_digest")
    _nonempty_string(
        manifest_entry_id,
        field="Fact Bundle manifest_entry_id",
    )
    _positive_integer(
        request_artifact_id,
        field="Fact Bundle request_artifact_id",
    )
    _digest(
        request_artifact_digest,
        field="Fact Bundle request_artifact_digest",
    )
    _positive_integer(transport_id, field="Fact Bundle transport_id")
    _digest(transport_digest, field="Fact Bundle transport_digest")
    return NodeProviderFactBundle(
        schema=NODE_PROVIDER_FACT_BUNDLE_SCHEMA,
        binding=result.binding,
        manifest_digest=manifest_digest,
        manifest_entry_id=manifest_entry_id,
        request_artifact_id=request_artifact_id,
        request_artifact_digest=request_artifact_digest,
        provider_result=result,
        provider_result_digest=result.result_digest,
        transport_id=transport_id,
        transport_digest=transport_digest,
    )


def _run_command(command: tuple[str, ...], cwd: Path) -> str:
    try:
        return subprocess.run(  # noqa: S603
            command,
            cwd=cwd,
            check=True,
            capture_output=True,
            text=True,
        ).stdout
    except subprocess.CalledProcessError as error:
        stderr = error.stderr.strip()
        message = f"Provider command failed: {command[0]}: {stderr}"
        raise ValueError(message) from error


def _positive_integer(value: object, *, field: str) -> int:
    if type(value) is not int or value <= 0:
        message = f"{field} must be a positive non-Boolean integer"
        raise ValueError(message)
    return value


def _full_sha(value: object, *, field: str) -> str:
    if type(value) is not str or _SHA_PATTERN.fullmatch(value) is None:
        message = f"{field} must be a full lowercase commit SHA"
        raise ValueError(message)
    return value


def _digest(value: object, *, field: str) -> str:
    if type(value) is not str or _DIGEST_PATTERN.fullmatch(value) is None:
        message = f"{field} must be a prefixed lowercase SHA-256"
        raise ValueError(message)
    return value


def _nonempty_string(value: object, *, field: str) -> str:
    if type(value) is not str or not value or value != value.strip():
        message = f"{field} must be a nonempty exact string"
        raise TypeError(message)
    return value


def _string_tuple(value: object, *, field: str) -> tuple[str, ...]:
    if type(value) is not tuple:
        message = f"{field} must be a tuple"
        raise TypeError(message)
    for item in value:
        _nonempty_string(item, field=f"{field} item")
    if len(set(value)) != len(value):
        message = f"{field} contains duplicate values"
        raise ValueError(message)
    return value


def _relative_path(value: object, *, field: str) -> str:
    path = _nonempty_string(value, field=field)
    pure_path = PurePosixPath(path)
    if (
        pure_path.is_absolute()
        or ".." in pure_path.parts
        or "\\" in path
        or path != pure_path.as_posix()
    ):
        message = f"{field} must be a normalized relative POSIX path"
        raise ValueError(message)
    return path


def validate_nbgv_facts(facts: NbgvFacts, *, target: str) -> None:
    """Validate the complete target-bound NBGV fact contract."""
    if type(facts) is not NbgvFacts:
        message = "NBGV facts must use the exact NbgvFacts runtime type"
        raise TypeError(message)
    _full_sha(target, field="target")
    if (
        type(facts.canonical_version) is not str
        or _CANONICAL_VERSION_PATTERN.fullmatch(facts.canonical_version) is None
    ):
        message = "NBGV canonical version has invalid syntax"
        raise ValueError(message)
    for field, value in (
        ("semVer1", facts.sem_ver1),
        ("semVer2", facts.sem_ver2),
    ):
        if type(value) is not str or not value or value != value.strip():
            message = f"NBGV {field} must be a nonempty string"
            raise ValueError(message)
    if (
        type(facts.npm_package_version) is not str
        or _NPM_VERSION_PATTERN.fullmatch(facts.npm_package_version) is None
    ):
        message = "NBGV npmPackageVersion is not a native npm version"
        raise ValueError(message)
    _full_sha(facts.git_commit_id, field="NBGV gitCommitId")
    if facts.git_commit_id != target:
        message = "NBGV Node API result is not bound to the exact target"
        raise ValueError(message)
    _positive_integer(facts.version_height, field="NBGV versionHeight")
    if type(facts.public_release) is not bool:
        message = "NBGV publicRelease must be Boolean"
        raise TypeError(message)
    _digest(
        facts.node_api_result_digest,
        field="NBGV node-api-result-digest",
    )


def validate_provider_toolchain(
    toolchain: tuple[tuple[str, str], ...],
) -> None:
    """Require the exact closed Node-then-PNPM toolchain contract."""
    if type(toolchain) is not tuple or len(toolchain) != len(
        _PROVIDER_TOOLCHAIN_NAMES
    ):
        message = "Provider toolchain must contain exactly node then pnpm"
        raise ValueError(message)
    for index, entry in enumerate(toolchain):
        if (
            type(entry) is not tuple
            or len(entry) != _TOOLCHAIN_ENTRY_FIELD_COUNT
            or not all(type(value) is str for value in entry)
        ):
            message = "Provider toolchain entries must be string pairs"
            raise TypeError(message)
        name, version = entry
        if name != _PROVIDER_TOOLCHAIN_NAMES[index]:
            message = "Provider toolchain must contain exactly node then pnpm"
            raise ValueError(message)
        if not version or version != version.strip():
            message = f"Provider toolchain {name} version must be nonempty"
            raise ValueError(message)


def validate_provider_binding(binding: ProviderBinding) -> None:
    """Validate primitive and same-revision Provider request bindings."""
    if type(binding) is not ProviderBinding:
        message = "Provider binding must use the exact ProviderBinding type"
        raise TypeError(message)
    _nonempty_string(binding.request_id, field="Provider request_id")
    _nonempty_string(binding.purpose, field="Provider purpose")
    if binding.purpose not in _PURPOSES:
        message = "Provider purpose is not in the closed purpose set"
        raise ValueError(message)
    _positive_integer(binding.workflow_run_id, field="workflow_run_id")
    _positive_integer(binding.run_attempt, field="run_attempt")
    _full_sha(binding.target, field="target")
    _nonempty_string(binding.producer, field="Provider producer")
    _nonempty_string(binding.control, field="Provider control")
    _digest(binding.catalog_digest, field="catalog_digest")
    if binding.catalog_digest != catalog_digest():
        message = "Provider catalog digest is not the current static catalog"
        raise ValueError(message)
    _digest(binding.request_digest, field="request_digest")


def validate_checkout_evidence(checkout: CheckoutEvidence) -> None:
    """Validate every exact runtime field in Provider checkout evidence."""
    if type(checkout) is not CheckoutEvidence:
        message = "checkout evidence must use the exact runtime type"
        raise TypeError(message)
    _full_sha(checkout.target, field="checkout target")
    _full_sha(checkout.head, field="checkout head")
    for field, value in (
        ("shallow", checkout.shallow),
        ("ancestry_complete", checkout.ancestry_complete),
        ("tags_complete", checkout.tags_complete),
        ("credentials_persisted", checkout.credentials_persisted),
    ):
        if type(value) is not bool:
            message = f"checkout {field} must be an exact Boolean"
            raise TypeError(message)
    _nonempty_string(
        checkout.authoritative_remote,
        field="checkout authoritative_remote",
    )
    _nonempty_string(
        checkout.authoritative_remote_url,
        field="checkout authoritative_remote_url",
    )
    _nonempty_string(checkout.tag_refspec, field="checkout tag_refspec")


def validate_project_node(project: ProjectNode) -> None:
    """Validate every exact runtime field in one Project Node."""
    if type(project) is not ProjectNode:
        message = "Project Node must use the exact ProjectNode runtime type"
        raise TypeError(message)
    _nonempty_string(project.project_id, field="Project Node project_id")
    _nonempty_string(project.package_name, field="Project Node package_name")
    _relative_path(project.path, field="Project Node path")
    _relative_path(project.manifest_path, field="Project Node manifest_path")
    if type(project.private) is not bool:
        message = "Project Node private must be an exact Boolean"
        raise TypeError(message)
    dependencies = _string_tuple(
        project.workspace_dependencies,
        field="Project Node workspace_dependencies",
    )
    if dependencies != tuple(sorted(dependencies)):
        message = "Project Node workspace_dependencies must be sorted"
        raise ValueError(message)


def validate_global_input(global_input: GlobalInput) -> None:
    """Validate one exact global-input digest and Project Node relationship."""
    if type(global_input) is not GlobalInput:
        message = "global input must use the exact GlobalInput runtime type"
        raise TypeError(message)
    _relative_path(global_input.path, field="global input path")
    _digest(global_input.content_digest, field="global input content_digest")
    project_ids = _string_tuple(
        global_input.project_ids,
        field="global input project_ids",
    )
    if project_ids != tuple(sorted(project_ids)):
        message = "global input project_ids must be sorted"
        raise ValueError(message)


def validate_node_provider_result(result: NodeProviderResult) -> None:
    """Validate every exact Provider Result runtime field and intrinsic fact."""
    if type(result) is not NodeProviderResult:
        message = "Provider Result must use the exact NodeProviderResult type"
        raise TypeError(message)
    validate_provider_binding(result.binding)
    for field, value in (
        ("provider_logical_id", result.provider_logical_id),
        ("provider_implementation_id", result.provider_implementation_id),
        ("execution_mode", result.execution_mode),
        ("execution_class", result.execution_class),
        ("outcome", result.outcome),
    ):
        _nonempty_string(value, field=f"Provider Result {field}")
    validate_provider_toolchain(result.toolchain)
    _digest(result.manifest_digest, field="Provider Result manifest_digest")
    _digest(
        result.configuration_digest,
        field="Provider Result configuration_digest",
    )
    validate_checkout_evidence(result.checkout)
    if type(result.project_nodes) is not tuple:
        message = "Provider Result project_nodes must be a tuple"
        raise TypeError(message)
    for project in result.project_nodes:
        validate_project_node(project)
    if type(result.global_inputs) is not tuple:
        message = "Provider Result global_inputs must be a tuple"
        raise TypeError(message)
    for global_input in result.global_inputs:
        validate_global_input(global_input)
    if tuple(item.path for item in result.global_inputs) != tuple(
        sorted(item.path for item in result.global_inputs)
    ):
        message = "Provider Result global_inputs must be sorted by path"
        raise ValueError(message)
    _string_tuple(
        result.build_capabilities,
        field="Provider Result build_capabilities",
    )
    validate_nbgv_facts(result.nbgv, target=result.binding.target)
    _string_tuple(result.unresolved, field="Provider Result unresolved")
    _string_tuple(result.conflicts, field="Provider Result conflicts")
    if result.diagnostic_reference is not None:
        _nonempty_string(
            result.diagnostic_reference,
            field="Provider Result diagnostic_reference",
        )


def _exact_head(
    repo_root: Path,
    target: str,
    *,
    runner: CommandRunner,
    mismatch_message: str,
) -> str:
    resolved = runner(
        ("git", "rev-parse", "--verify", f"{target}^{{commit}}"),
        repo_root,
    ).strip()
    head = runner(("git", "rev-parse", "HEAD"), repo_root).strip()
    if resolved != target or head != target:
        raise ValueError(mismatch_message)
    return head


def _verify_complete_history(
    repo_root: Path,
    target: str,
    *,
    runner: CommandRunner,
) -> None:
    shallow_text = runner(
        ("git", "rev-parse", "--is-shallow-repository"),
        repo_root,
    ).strip()
    if shallow_text not in {"true", "false"}:
        message = "checkout shallow state is not provable"
        raise ValueError(message)
    if shallow_text == "true":
        message = "checkout history is shallow"
        raise ValueError(message)
    ancestry = runner(("git", "rev-list", "--parents", target), repo_root)
    if not ancestry.strip() or not ancestry.splitlines()[0].startswith(target):
        message = "checkout ancestry is incomplete"
        raise ValueError(message)
    objects = runner(
        ("git", "rev-list", "--objects", "--missing=print", target),
        repo_root,
    )
    if any(line.startswith("?") for line in objects.splitlines()):
        message = "checkout ancestry contains missing objects"
        raise ValueError(message)


def _authoritative_remote_url(
    repo_root: Path,
    *,
    runner: CommandRunner,
) -> str:
    remote_url = runner(
        ("git", "remote", "get-url", AUTHORITATIVE_REMOTE),
        repo_root,
    ).strip()
    if not remote_url:
        message = "authoritative checkout remote URL is empty"
        raise ValueError(message)
    return remote_url


def _inspect_source_checkout(
    repo_root: Path,
    target: str,
    *,
    runner: CommandRunner,
) -> str:
    _exact_head(
        repo_root,
        target,
        runner=runner,
        mismatch_message="checkout HEAD is not pinned to the exact target",
    )
    _verify_complete_history(repo_root, target, runner=runner)
    return _authoritative_remote_url(repo_root, runner=runner)


def verify_exact_checkout(
    repo_root: Path,
    target: str,
    materialization: CheckoutMaterialization,
    *,
    runner: CommandRunner = _run_command,
) -> CheckoutEvidence:
    """Prepare and verify exact HEAD plus complete authoritative tags."""
    _full_sha(target, field="target")
    _validate_checkout_materialization(materialization)

    _exact_head(
        repo_root,
        target,
        runner=runner,
        mismatch_message="checkout HEAD is not pinned to the exact target",
    )

    remote_url = _authoritative_remote_url(repo_root, runner=runner)
    try:
        runner(
            (
                "git",
                "fetch",
                "--force",
                "--prune",
                "--no-tags",
                AUTHORITATIVE_REMOTE,
                TAG_REFSPEC,
            ),
            repo_root,
        )
    except ValueError as error:
        message = "authoritative tag fetch failed"
        raise ValueError(message) from error

    head = _exact_head(
        repo_root,
        target,
        runner=runner,
        mismatch_message=(
            "tag preparation changed or lost the exact target checkout"
        ),
    )
    _verify_complete_history(repo_root, target, runner=runner)
    return CheckoutEvidence(
        target=target,
        head=head,
        shallow=False,
        ancestry_complete=True,
        tags_complete=True,
        credentials_persisted=False,
        authoritative_remote=AUTHORITATIVE_REMOTE,
        authoritative_remote_url=remote_url,
        tag_refspec=TAG_REFSPEC,
    )


def _validate_checkout_materialization(
    materialization: CheckoutMaterialization,
) -> None:
    if type(materialization) is not CheckoutMaterialization:
        message = (
            "materialization must use the exact CheckoutMaterialization "
            "runtime type"
        )
        raise ValueError(message)
    try:
        fetch_depth = materialization.fetch_depth
    except AttributeError:
        message = "CheckoutMaterialization fetch_depth is missing"
        raise ValueError(message) from None
    if type(fetch_depth) is not int or fetch_depth != 0:
        message = (
            "checkout must use fetch-depth 0; CheckoutMaterialization "
            "fetch_depth must be the exact int value 0"
        )
        raise ValueError(message)
    try:
        credentials_persisted = materialization.credentials_persisted
    except AttributeError:
        message = "CheckoutMaterialization credentials_persisted is missing"
        raise ValueError(message) from None
    if (
        type(credentials_persisted) is not bool
        or credentials_persisted is not False
    ):
        message = (
            "CheckoutMaterialization credentials_persisted must disable "
            "persisted credentials with exact Boolean False"
        )
        raise ValueError(message)


def _reject_dirty_tracked_provider_inputs(
    repo_root: Path,
    project_path: str,
    *,
    runner: CommandRunner,
) -> None:
    relevant_paths = (
        "package.json",
        "pnpm-lock.yaml",
        "pnpm-workspace.yaml",
        "version.json",
        project_path,
    )
    output = runner(
        (
            "git",
            "diff",
            "--name-only",
            "--diff-filter=ACDMRTUXB",
            "HEAD",
            "--",
            *relevant_paths,
        ),
        repo_root,
    )
    dirty = tuple(path for path in output.splitlines() if path)
    if dirty:
        message = (
            f"tracked Provider input differs from the exact target: {dirty[0]}"
        )
        raise ValueError(message)


def _reject_mutated_isolated_source(
    repo_root: Path,
    *,
    runner: CommandRunner,
) -> None:
    output = runner(
        (
            "git",
            "status",
            "--porcelain=v1",
            "--ignored",
            "--untracked-files=all",
        ),
        repo_root,
    )
    unexpected: list[str] = []
    for line in output.splitlines():
        path = line[3:]
        if "node_modules" in Path(path).parts:
            continue
        unexpected.append(path)
    if unexpected:
        message = (
            "Provider preparation changed the isolated exact-target source: "
            f"{unexpected[0]}"
        )
        raise ValueError(message)


def _content_digest(path: Path) -> str:
    try:
        content = path.read_bytes()
    except OSError as error:
        message = f"Provider input cannot be read: {path.as_posix()}"
        raise ValueError(message) from error
    return f"sha256:{hashlib.sha256(content).hexdigest()}"


def node_provider_version_input_candidates(
    project_path: str,
) -> tuple[str, ...]:
    """Return the project-to-root candidate NBGV configuration lineage."""
    normalized = _relative_path(project_path, field="project path")
    current = PurePosixPath(normalized)
    candidates: list[str] = []
    while True:
        candidates.append((current / "version.json").as_posix())
        if current == PurePosixPath("."):
            break
        current = current.parent
    return tuple(candidates)


def _version_input_paths(
    repo_root: Path,
    project_root: Path,
) -> tuple[str, ...]:
    project_path = project_root.relative_to(repo_root).as_posix()
    paths = [
        path
        for path in node_provider_version_input_candidates(project_path)
        if (repo_root / path).is_file()
    ]
    if not paths:
        message = "Provider cannot resolve an effective version.json lineage"
        raise ValueError(message)
    return tuple(sorted(paths))


def _provider_input_facts(
    repo_root: Path,
    project_root: Path,
) -> tuple[str, str, tuple[GlobalInput, ...]]:
    manifest_path = project_root / "package.json"
    manifest_digest = _content_digest(manifest_path)
    global_paths = (
        *FIRST_SLICE_REQUIRED_GLOBAL_INPUTS,
        *_version_input_paths(repo_root, project_root),
    )
    unique_paths = tuple(sorted(set(global_paths)))
    global_inputs: list[GlobalInput] = []
    for path in unique_paths:
        absolute_path = repo_root / path
        if not absolute_path.is_file():
            message = f"required Provider global input is missing: {path}"
            raise ValueError(message)
        global_inputs.append(
            GlobalInput(
                path=path,
                content_digest=_content_digest(absolute_path),
                project_ids=(FIRST_SLICE_PACKAGE,),
            )
        )
    documents: list[JsonValue] = [item.to_document() for item in global_inputs]
    configuration_digest = canonical_sha256(
        {
            "schema": "workflow-delivery/v3/node-provider-configuration",
            "global-inputs": documents,
        }
    )
    return manifest_digest, configuration_digest, tuple(global_inputs)


@contextmanager
def _isolated_exact_target_repository(
    repo_root: Path,
    target: str,
    authoritative_remote_url: str,
    *,
    runner: CommandRunner,
) -> Iterator[Path]:
    with TemporaryDirectory(
        prefix="workflow-delivery-v3-node-provider-",
    ) as temporary_directory:
        isolated_root = Path(temporary_directory) / "target"
        runner(
            (
                "git",
                "clone",
                "--no-local",
                "--no-checkout",
                "--no-tags",
                str(repo_root.resolve()),
                str(isolated_root),
            ),
            repo_root,
        )
        runner(
            (
                "git",
                "remote",
                "set-url",
                AUTHORITATIVE_REMOTE,
                authoritative_remote_url,
            ),
            isolated_root,
        )
        runner(
            ("git", "checkout", "--detach", target),
            isolated_root,
        )
        yield isolated_root


def _json_output(output: str, *, context: str) -> JsonValue:
    try:
        parsed: object = json.loads(output)
    except json.JSONDecodeError as error:
        message = f"{context} did not emit valid JSON"
        raise ValueError(message) from error
    return _json_value(parsed, context=context)


def _json_value(value: object, *, context: str) -> JsonValue:
    if value is None or isinstance(value, bool | int | float | str):
        return value
    if isinstance(value, list):
        return [
            _json_value(item, context=f"{context} array item") for item in value
        ]
    if isinstance(value, dict):
        document: dict[str, JsonValue] = {}
        for name, item in value.items():
            if not isinstance(name, str):
                message = f"{context} object keys must be strings"
                raise TypeError(message)
            document[name] = _json_value(
                item,
                context=f"{context}.{name}",
            )
        return document
    message = f"{context} contains a non-JSON value"
    raise TypeError(message)


def _workspace_dependencies(
    package: dict[str, JsonValue],
    repo_root: Path,
) -> tuple[str, ...]:
    workspace_dependencies: set[str] = set()
    for dependency_kind in (
        "dependencies",
        "devDependencies",
        "optionalDependencies",
        "peerDependencies",
    ):
        dependencies = package.get(dependency_kind, {})
        if not isinstance(dependencies, dict):
            message = f"PNPM {dependency_kind} must be an object"
            raise TypeError(message)
        for name, dependency in dependencies.items():
            if not isinstance(dependency, dict):
                message = f"PNPM {dependency_kind} entry is malformed"
                raise TypeError(message)
            dependency_path = dependency.get("path")
            version = dependency.get("version")
            if (
                isinstance(dependency_path, str)
                and isinstance(version, str)
                and version.startswith(("link:", "workspace:"))
                and Path(dependency_path)
                .resolve()
                .is_relative_to(repo_root.resolve())
            ):
                workspace_dependencies.add(name)
    return tuple(sorted(workspace_dependencies))


def _project_node(
    repo_root: Path,
    project_root: Path,
    output: str,
) -> ProjectNode:
    document = _json_output(output, context="PNPM metadata")
    if not isinstance(document, list):
        message = "PNPM metadata must be an array"
        raise TypeError(message)
    matches = [
        value
        for value in document
        if isinstance(value, dict) and value.get("name") == FIRST_SLICE_PACKAGE
    ]
    if len(matches) != 1:
        message = "PNPM metadata must contain exactly one first-slice package"
        raise ValueError(message)
    package = matches[0]
    package_path = package.get("path")
    if not isinstance(package_path, str):
        message = "PNPM package path must be a string"
        raise TypeError(message)
    if Path(package_path).resolve() != project_root.resolve():
        message = "PNPM package path does not match the selected project"
        raise ValueError(message)
    private = package.get("private", False)
    if type(private) is not bool:
        message = "PNPM package private fact must be Boolean"
        raise TypeError(message)
    relative_path = (
        project_root.resolve().relative_to(repo_root.resolve()).as_posix()
    )
    return ProjectNode(
        project_id=FIRST_SLICE_PACKAGE,
        package_name=FIRST_SLICE_PACKAGE,
        path=relative_path,
        manifest_path=f"{relative_path}/package.json",
        private=private,
        workspace_dependencies=_workspace_dependencies(package, repo_root),
    )


def _string_fact(document: dict[str, JsonValue], name: str) -> str:
    value = document.get(name)
    if not isinstance(value, str) or not value:
        message = f"NBGV Node API missing required string fact: {name}"
        raise ValueError(message)
    return value


def _nbgv_facts(output: str, target: str) -> NbgvFacts:
    document = _json_output(output, context="NBGV Node API")
    if not isinstance(document, dict):
        message = "NBGV Node API result must be an object"
        raise TypeError(message)
    version_height = _positive_integer(
        document.get("versionHeight"),
        field="NBGV versionHeight",
    )
    public_release = document.get("publicRelease")
    if not isinstance(public_release, bool):
        message = "NBGV publicRelease must be Boolean"
        raise TypeError(message)
    facts = NbgvFacts(
        canonical_version=_string_fact(document, "version"),
        sem_ver1=_string_fact(document, "semVer1"),
        sem_ver2=_string_fact(document, "semVer2"),
        version_height=version_height,
        git_commit_id=_string_fact(document, "gitCommitId"),
        public_release=public_release,
        npm_package_version=_string_fact(document, "npmPackageVersion"),
        node_api_result_digest=canonical_sha256(document),
    )
    validate_nbgv_facts(facts, target=target)
    return facts


def _toolchain_version(
    runner: CommandRunner,
    tool: str,
    repo_root: Path,
) -> str:
    version = runner((tool, "--version"), repo_root).strip()
    if not version:
        message = f"Provider toolchain {tool} version must be nonempty"
        raise ValueError(message)
    return version


def provide_node_repository_facts(
    repo_root: Path,
    project_path: str,
    binding: ProviderBinding,
    materialization: CheckoutMaterialization,
    *,
    runner: CommandRunner = _run_command,
) -> NodeProviderResult:
    """Compile one exact-target Node/NBGV Provider Result."""
    validate_provider_binding(binding)
    _validate_checkout_materialization(materialization)
    project_root = (repo_root / project_path).resolve()
    try:
        project_root.relative_to(repo_root.resolve())
    except ValueError as error:
        message = "Node project path escapes the repository"
        raise ValueError(message) from error
    authoritative_remote_url = _inspect_source_checkout(
        repo_root,
        binding.target,
        runner=runner,
    )
    with _isolated_exact_target_repository(
        repo_root,
        binding.target,
        authoritative_remote_url,
        runner=runner,
    ) as evaluation_root:
        evaluation_project_root = (evaluation_root / project_path).resolve()
        if not (evaluation_project_root / "package.json").is_file():
            message = "Node project package.json is missing"
            raise ValueError(message)
        checkout = verify_exact_checkout(
            evaluation_root,
            binding.target,
            materialization,
            runner=runner,
        )
        _reject_dirty_tracked_provider_inputs(
            evaluation_root,
            project_path,
            runner=runner,
        )
        runner(
            (
                "pnpm",
                "install",
                "--frozen-lockfile",
                "--ignore-scripts",
                "--ignore-pnpmfile",
            ),
            evaluation_root,
        )
        pnpm_output = runner(
            (
                "pnpm",
                "--config.ignore-pnpmfile=true",
                "--dir",
                ".",
                "--filter",
                f"{FIRST_SLICE_PACKAGE}...",
                "list",
                "--json",
                "--depth",
                "Infinity",
            ),
            evaluation_root,
        )
        project_node = _project_node(
            evaluation_root,
            evaluation_project_root,
            pnpm_output,
        )
        if project_node.workspace_dependencies:
            message = (
                "commit 3 permits exactly one Project Node "
                "and no workspace closure"
            )
            raise ValueError(message)
        _reject_mutated_isolated_source(
            evaluation_root,
            runner=runner,
        )
        (
            manifest_digest,
            configuration_digest,
            global_inputs,
        ) = _provider_input_facts(
            evaluation_root,
            evaluation_project_root,
        )
        nbgv_output = runner(
            ("node", "--input-type=module", "-e", _NBGV_PROGRAM),
            evaluation_project_root,
        )
        nbgv = _nbgv_facts(nbgv_output, binding.target)

    toolchain = (
        ("node", _toolchain_version(runner, "node", repo_root)),
        ("pnpm", _toolchain_version(runner, "pnpm", repo_root)),
    )
    validate_provider_toolchain(toolchain)
    result = NodeProviderResult(
        binding=binding,
        provider_logical_id=PROVIDER_LOGICAL_ID,
        provider_implementation_id=PROVIDER_IMPLEMENTATION_ID,
        execution_mode=PROVIDER_EXECUTION_MODE,
        execution_class=PROVIDER_EXECUTION_CLASS,
        toolchain=toolchain,
        manifest_digest=manifest_digest,
        configuration_digest=configuration_digest,
        checkout=checkout,
        project_nodes=(project_node,),
        global_inputs=global_inputs,
        build_capabilities=_FIRST_SLICE_BUILD_CAPABILITIES,
        nbgv=nbgv,
        unresolved=(),
        conflicts=(),
        outcome="success",
        diagnostic_reference=None,
    )
    validate_node_provider_result(result)
    return result
