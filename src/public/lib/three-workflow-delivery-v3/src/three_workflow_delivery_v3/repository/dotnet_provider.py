"""Exact-target MSBuild/NBGV evaluation and immutable .NET Provider facts."""

from __future__ import annotations

import hashlib
import json
import os
import re
import subprocess
from dataclasses import dataclass, fields
from pathlib import Path, PurePosixPath
from tempfile import TemporaryDirectory
from typing import cast

from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    parse_json_strict,
)
from three_workflow_delivery_v3.repository.node_provider import (
    NBGV_ENVIRONMENT_ALLOWLIST,
    CheckoutEvidence,
    CheckoutMaterialization,
    GlobalInput,
    ProviderBinding,
    _isolated_exact_target_repository,
    _provider_binding_document,
    _run_command,
    validate_checkout_evidence,
    validate_global_input,
    validate_provider_binding,
    verify_exact_checkout,
)

DOTNET_PROVIDER_LOGICAL_ID = "dotnet/msbuild-nbgv-v1"
DOTNET_PROVIDER_IMPLEMENTATION_ID = (
    "three-workflow-delivery-v3/dotnet-msbuild-nbgv-v1"
)
DOTNET_PROVIDER_EXECUTION_MODE = "target-evaluating"
DOTNET_PROVIDER_EXECUTION_CLASS = "target-evaluation/unprivileged-v1"
DOTNET_PROVIDER_RESULT_SCHEMA = "workflow-delivery/v3/dotnet-provider-result"
DOTNET_PROVIDER_FACT_BUNDLE_SCHEMA = (
    "workflow-delivery/v3/dotnet-provider-fact-bundle"
)
DOTNET_RELEASE_UNIT = "hcoona-release-smoke-github-packages"
DOTNET_PACKAGE = "Hcoona.ReleaseSmoke.GithubPackages"
DOTNET_PROJECT_ROOT = f"src/public/lib/{DOTNET_RELEASE_UNIT}"
DOTNET_PROJECT_PATH = f"{DOTNET_PROJECT_ROOT}/{DOTNET_RELEASE_UNIT}.csproj"
DOTNET_ENTRY_POINT = DOTNET_PROJECT_PATH
DOTNET_TOOLCHAIN = (
    ("dotnet", "10.0.300"),
    ("msbuild", "18.6.3"),
    ("nbgv", "3.10.94"),
    ("nuget", "7.9.0"),
)
_PAIR_LENGTH = 2
_DIGEST = re.compile(r"sha256:[0-9a-f]{64}\Z")
_SHA = re.compile(r"[0-9a-f]{40}\Z")
_GLOBAL_INPUTS = (
    "global.json",
    "nuget.config",
    "Directory.Build.props",
    "Directory.Packages.props",
    "version.json",
    ".editorconfig",
    "stylecop.json",
    ".config/dotnet-tools.json",
    "src/Directory.Build.props",
    "src/Directory.Packages.props",
    "src/public/Directory.Build.props",
    "src/public/Directory.Packages.props",
    "src/public/lib/Directory.Build.props",
    "src/public/lib/Directory.Packages.props",
)


def neutral_dotnet_environment() -> dict[str, str]:
    """Remove cloud refs and credentials before any target-defined execution."""
    allowed = {name.upper() for name in NBGV_ENVIRONMENT_ALLOWLIST}
    # NuGet's Windows machine-wide settings path uses these OS directory roots.
    allowed.update(("PROGRAMFILES(X86)", "PROGRAMFILES"))
    environment = {
        key: value
        for key, value in os.environ.items()
        if key.upper() in allowed
    }
    environment.update(
        DOTNET_CLI_TELEMETRY_OPTOUT="1",
        DOTNET_NOLOGO="1",
        IGNORE_GITHUB_REF="true",
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1",
    )
    return environment


def run_native(
    command: tuple[str, ...],
    cwd: Path,
    environment: dict[str, str] | None = None,
    *,
    diagnostics: Path | None = None,
) -> str:
    """Execute one unprivileged native command without shell interpretation."""
    if diagnostics is not None:
        diagnostics.mkdir(parents=True, exist_ok=False)
        (diagnostics / "command.json").write_text(
            json.dumps({"argv": command, "cwd": str(cwd)}, indent=2),
            encoding="utf-8",
        )
    try:
        result = subprocess.run(  # noqa: S603
            command,
            cwd=cwd,
            env=environment or neutral_dotnet_environment(),
            check=False,
            capture_output=True,
            text=True,
            timeout=300,
        )
    except (OSError, subprocess.TimeoutExpired) as error:
        if diagnostics is not None:
            (diagnostics / "failure.txt").write_text(
                str(error), encoding="utf-8"
            )
            if isinstance(error, subprocess.TimeoutExpired):
                for name, value in (
                    ("stdout", error.stdout),
                    ("stderr", error.stderr),
                ):
                    (diagnostics / f"{name}.txt").write_bytes(
                        value.encode()
                        if isinstance(value, str)
                        else value or b""
                    )
        raise
    if diagnostics is not None:
        (diagnostics / "stdout.txt").write_text(result.stdout, encoding="utf-8")
        (diagnostics / "stderr.txt").write_text(result.stderr, encoding="utf-8")
        (diagnostics / "exit-code.txt").write_text(
            str(result.returncode), encoding="ascii"
        )
    if result.returncode:
        message = (
            f"native command failed: {command[0]} {command[1]} "
            f"(exit {result.returncode})"
        )
        raise ValueError(message)
    return result.stdout


def _object(
    value: JsonValue, *, keys: set[str] | None = None
) -> dict[str, JsonValue]:
    if not isinstance(value, dict) or (keys is not None and set(value) != keys):
        message = "native fact object has an unexpected schema"
        raise ValueError(message)
    return value


def _text(value: JsonValue) -> str:
    if type(value) is not str or not value or value.strip() != value:
        message = "native fact must be a nonempty string"
        raise ValueError(message)
    return value


def _digest(value: str) -> None:
    if not _DIGEST.fullmatch(value):
        message = "invalid native fact digest"
        raise ValueError(message)


@dataclass(frozen=True, slots=True)
class NativeNuGetHelper:
    """Invoke a previously built trusted helper; never build in a reader."""

    helper_dll: Path

    def _invoke(self, *arguments: str) -> dict[str, JsonValue]:
        if not self.helper_dll.is_file() or not self.helper_dll.is_absolute():
            message = "native helper must be an existing absolute DLL path"
            raise ValueError(message)
        return _object(
            parse_json_strict(
                run_native(
                    ("dotnet", str(self.helper_dll), *arguments),
                    self.helper_dll.parent,
                )
            )
        )

    def normalize_identity(
        self, package_id: str, version: str
    ) -> dict[str, JsonValue]:
        """Preserve display values and apply official NuGet equivalence."""
        return self._invoke("normalize-identity", package_id, version)

    def _file_operation(
        self, operation: str, content: bytes
    ) -> dict[str, JsonValue]:
        with TemporaryDirectory(prefix="wdv3-native-reader-") as temporary:
            path = Path(temporary) / "input"
            path.write_bytes(content)
            return self._invoke(operation, str(path))

    def inspect_package(self, content: bytes) -> dict[str, JsonValue]:
        """Read package contents through NuGet.Packaging."""
        return self._file_operation("inspect-package", content)

    def service_resources(self, index: bytes) -> dict[str, JsonValue]:
        """Interpret the supported NuGet service resources."""
        return self._file_operation("service-resources", index)

    def audit_binlog(self, path: Path) -> dict[str, JsonValue]:
        """Read native MSBuild task events without evaluating projects."""
        return self._invoke("audit-binlog", str(path.resolve()))


@dataclass(frozen=True, slots=True)
class DotnetNbgvFacts:
    """Frozen original native projection and canonical lineage."""

    canonical_version: str
    sem_ver1: str
    sem_ver2: str
    version_height: int
    git_commit_id: str
    public_release: bool
    nuget_package_version: str
    assembly_version: str
    assembly_file_version: str
    assembly_informational_version: str
    native_result_digest: str

    def to_document(self) -> dict[str, JsonValue]:
        """Serialize the distinct .NET native fact shape."""
        return {
            "canonical": {
                "version": self.canonical_version,
                "semVer1": self.sem_ver1,
                "semVer2": self.sem_ver2,
                "versionHeight": self.version_height,
                "gitCommitId": self.git_commit_id,
                "publicRelease": self.public_release,
            },
            "native": {
                "nugetPackageVersion": self.nuget_package_version,
                "assemblyVersion": self.assembly_version,
                "assemblyFileVersion": self.assembly_file_version,
                "assemblyInformationalVersion": (
                    self.assembly_informational_version
                ),
            },
            "native-result-digest": self.native_result_digest,
        }


def validate_dotnet_nbgv_facts(facts: DotnetNbgvFacts, *, target: str) -> None:
    """Reject malformed and substituted native lineage."""
    if (
        type(facts) is not DotnetNbgvFacts
        or not _SHA.fullmatch(target)
        or facts.git_commit_id != target
    ):
        message = ".NET NBGV target binding mismatch"
        raise ValueError(message)
    if (
        type(facts.version_height) is not int
        or facts.version_height < 0
        or type(facts.public_release) is not bool
    ):
        message = "invalid canonical NBGV facts"
        raise ValueError(message)
    for field in fields(facts):
        if field.name not in {"version_height", "public_release"}:
            _text(getattr(facts, field.name))
    _digest(facts.native_result_digest)


def dotnet_nbgv_facts_from_document(value: JsonValue) -> DotnetNbgvFacts:
    """Read only the closed .NET NBGV variant."""
    document = _object(
        value, keys={"canonical", "native", "native-result-digest"}
    )
    canonical = _object(
        document["canonical"],
        keys={
            "version",
            "semVer1",
            "semVer2",
            "versionHeight",
            "gitCommitId",
            "publicRelease",
        },
    )
    native = _object(
        document["native"],
        keys={
            "nugetPackageVersion",
            "assemblyVersion",
            "assemblyFileVersion",
            "assemblyInformationalVersion",
        },
    )
    facts = DotnetNbgvFacts(
        _text(canonical["version"]),
        _text(canonical["semVer1"]),
        _text(canonical["semVer2"]),
        cast("int", canonical["versionHeight"]),
        _text(canonical["gitCommitId"]),
        cast("bool", canonical["publicRelease"]),
        _text(native["nugetPackageVersion"]),
        _text(native["assemblyVersion"]),
        _text(native["assemblyFileVersion"]),
        _text(native["assemblyInformationalVersion"]),
        _text(document["native-result-digest"]),
    )
    validate_dotnet_nbgv_facts(facts, target=facts.git_commit_id)
    return facts


@dataclass(frozen=True, slots=True)
class DotnetProjectNode:
    """Native evaluated project and normalized coordinate."""

    project_id: str
    package_name: str
    path: str
    manifest_path: str
    target_framework: str
    packable: bool
    include_symbols: bool
    project_references: tuple[str, ...]
    normalized_package_id: str
    normalized_version: str

    def to_document(self) -> dict[str, JsonValue]:
        """Serialize bounded evaluated facts."""
        return {
            "project-id": self.project_id,
            "package-name": self.package_name,
            "path": self.path,
            "manifest-path": self.manifest_path,
            "target-framework": self.target_framework,
            "packable": self.packable,
            "include-symbols": self.include_symbols,
            "project-references": [
                str(item) for item in self.project_references
            ],
            "normalized-package-id": self.normalized_package_id,
            "normalized-version": self.normalized_version,
        }


def validate_dotnet_project_node(project: DotnetProjectNode) -> None:
    """Admit only the selected one-framework, one-package smoke library."""
    if type(project) is not DotnetProjectNode or (
        project.project_id,
        project.package_name,
        project.path,
        project.manifest_path,
        project.target_framework,
        project.packable,
        project.include_symbols,
        project.project_references,
        project.normalized_package_id,
    ) != (
        DOTNET_RELEASE_UNIT,
        DOTNET_PACKAGE,
        DOTNET_PROJECT_ROOT,
        DOTNET_PROJECT_PATH,
        "net10.0",
        True,
        False,
        (),
        DOTNET_PACKAGE.lower(),
    ):
        message = "evaluated .NET project is outside the admitted closure"
        raise ValueError(message)
    if (
        type(project.packable) is not bool
        or type(project.include_symbols) is not bool
    ):
        message = "native pack facts must be booleans"
        raise ValueError(message)
    for value in (
        project.project_id,
        project.package_name,
        project.path,
        project.manifest_path,
        project.target_framework,
        project.normalized_package_id,
        project.normalized_version,
    ):
        _text(value)


def dotnet_project_node_from_document(value: JsonValue) -> DotnetProjectNode:
    """Read the exact evaluated project schema."""
    names = (
        "project-id",
        "package-name",
        "path",
        "manifest-path",
        "target-framework",
        "packable",
        "include-symbols",
        "project-references",
        "normalized-package-id",
        "normalized-version",
    )
    document = _object(value, keys=set(names))
    references = document["project-references"]
    if not isinstance(references, list) or any(
        not isinstance(item, str) for item in references
    ):
        message = "invalid project references"
        raise ValueError(message)
    project = DotnetProjectNode(
        _text(document["project-id"]),
        _text(document["package-name"]),
        _text(document["path"]),
        _text(document["manifest-path"]),
        _text(document["target-framework"]),
        cast("bool", document["packable"]),
        cast("bool", document["include-symbols"]),
        tuple(cast("list[str]", references)),
        _text(document["normalized-package-id"]),
        _text(document["normalized-version"]),
    )
    validate_dotnet_project_node(project)
    return project


@dataclass(frozen=True, slots=True)
class DotnetProviderResult:
    """One request-bound terminal .NET Provider result."""

    binding: ProviderBinding
    provider_logical_id: str
    provider_implementation_id: str
    execution_mode: str
    execution_class: str
    toolchain: tuple[tuple[str, str], ...]
    manifest_digest: str
    configuration_digest: str
    checkout: CheckoutEvidence
    project_nodes: tuple[DotnetProjectNode, ...]
    global_inputs: tuple[GlobalInput, ...]
    build_capabilities: tuple[str, ...]
    nbgv: DotnetNbgvFacts
    unresolved: tuple[str, ...]
    conflicts: tuple[str, ...]
    outcome: str
    diagnostic_reference: str | None
    source_input_manifest: tuple[tuple[str, str], ...]
    native_evaluation_digest: str

    def to_document(self) -> dict[str, JsonValue]:
        """Return the immutable .NET result with explicit native case."""
        return {
            "schema": DOTNET_PROVIDER_RESULT_SCHEMA,
            "binding": _provider_binding_document(self.binding),
            "provider": {
                "logical-id": self.provider_logical_id,
                "implementation-id": self.provider_implementation_id,
                "execution-mode": self.execution_mode,
                "execution-class": self.execution_class,
                "toolchain": dict(self.toolchain),
            },
            "input-digests": {
                "manifest": self.manifest_digest,
                "configuration": self.configuration_digest,
            },
            "checkout": {
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
            },
            "project-nodes": [
                project.to_document() for project in self.project_nodes
            ],
            "global-inputs": [
                item.to_document() for item in self.global_inputs
            ],
            "build-capabilities": [
                str(item) for item in self.build_capabilities
            ],
            "nbgv": self.nbgv.to_document(),
            "unresolved": [str(item) for item in self.unresolved],
            "conflicts": [str(item) for item in self.conflicts],
            "outcome": self.outcome,
            "diagnostic-reference": self.diagnostic_reference,
            "source-input-manifest": [
                {"path": path, "content-digest": digest}
                for path, digest in self.source_input_manifest
            ],
            "native-evaluation-digest": self.native_evaluation_digest,
        }

    @property
    def result_digest(self) -> str:
        """Digest the exact typed result payload."""
        return canonical_sha256(self.to_document())


def _validate_provider_collections(result: DotnetProviderResult) -> None:
    for collection in (
        result.toolchain,
        result.project_nodes,
        result.global_inputs,
        result.build_capabilities,
        result.unresolved,
        result.conflicts,
        result.source_input_manifest,
    ):
        if type(collection) is not tuple:
            message = "Provider collections must use immutable tuples"
            raise TypeError(message)
    for pair in (*result.toolchain, *result.source_input_manifest):
        if type(pair) is not tuple or len(pair) != _PAIR_LENGTH:
            message = "Provider pair must have exactly two immutable members"
            raise TypeError(message)
        for value in pair:
            _text(value)
    for item in result.global_inputs:
        validate_global_input(item)


def validate_dotnet_provider_result(result: DotnetProviderResult) -> None:
    """Validate supplied facts without loading target code."""
    if type(result) is not DotnetProviderResult:
        message = "expected exact .NET Provider result type"
        raise ValueError(message)
    _validate_provider_collections(result)
    validate_provider_binding(result.binding)
    validate_checkout_evidence(result.checkout)
    validate_dotnet_nbgv_facts(result.nbgv, target=result.binding.target)
    if (
        result.provider_logical_id,
        result.provider_implementation_id,
        result.execution_mode,
        result.execution_class,
        result.toolchain,
        result.build_capabilities,
        result.unresolved,
        result.conflicts,
        result.outcome,
        result.diagnostic_reference,
    ) != (
        DOTNET_PROVIDER_LOGICAL_ID,
        DOTNET_PROVIDER_IMPLEMENTATION_ID,
        DOTNET_PROVIDER_EXECUTION_MODE,
        DOTNET_PROVIDER_EXECUTION_CLASS,
        DOTNET_TOOLCHAIN,
        ("dotnet/nuget-package-v1",),
        (),
        (),
        "success",
        None,
    ):
        message = "unsupported .NET Provider execution facts"
        raise ValueError(message)
    if (
        result.checkout.target != result.binding.target
        or len(result.project_nodes) != 1
    ):
        message = ".NET Provider target/project closure mismatch"
        raise ValueError(message)
    validate_dotnet_project_node(result.project_nodes[0])
    expected_globals = tuple(
        GlobalInput(path, digest, (DOTNET_RELEASE_UNIT,))
        for path, digest in result.source_input_manifest
    )
    if result.global_inputs != expected_globals:
        message = "Provider input digests do not match source attribution"
        raise ValueError(message)
    for value in (
        result.manifest_digest,
        result.configuration_digest,
        result.native_evaluation_digest,
    ):
        _digest(value)
    if (
        not result.source_input_manifest
        or tuple(sorted(result.source_input_manifest))
        != result.source_input_manifest
        or len(dict(result.source_input_manifest))
        != len(result.source_input_manifest)
    ):
        message = "source input manifest must be nonempty, unique, and ordered"
        raise ValueError(message)
    for path, digest in result.source_input_manifest:
        if (
            PurePosixPath(path).is_absolute()
            or ".." in PurePosixPath(path).parts
        ):
            message = "invalid source input path"
            raise ValueError(message)
        _digest(digest)


def dotnet_provider_result_from_document(
    value: JsonValue,
) -> DotnetProviderResult:
    """Parse the closed native result without evaluating its target.

    This validates representation. The consumer separately admits the expected
    request, manifest, source inputs, and immutable transport bindings.
    """
    document = _object(value)
    binding = _object(document["binding"])
    provider = _object(document["provider"])
    digests = _object(document["input-digests"])
    checkout = _object(document["checkout"])

    def array(item: JsonValue) -> list[JsonValue]:
        if type(item) is not list:
            message = "native Provider collections must be JSON arrays"
            raise ValueError(message)
        return item

    result = DotnetProviderResult(
        binding=ProviderBinding(
            request_id=_text(binding["request-id"]),
            purpose=_text(binding["purpose"]),
            workflow_run_id=cast("int", binding["workflow-run-id"]),
            run_attempt=cast("int | None", binding.get("run-attempt")),
            target=_text(binding["target"]),
            producer=_text(binding["producer"]),
            control=_text(binding["control"]),
            catalog_digest=_text(binding["catalog-digest"]),
            request_digest=_text(binding["request-digest"]),
        ),
        provider_logical_id=_text(provider["logical-id"]),
        provider_implementation_id=_text(provider["implementation-id"]),
        execution_mode=_text(provider["execution-mode"]),
        execution_class=_text(provider["execution-class"]),
        toolchain=tuple(
            sorted(
                (name, _text(version))
                for name, version in _object(provider["toolchain"]).items()
            )
        ),
        manifest_digest=_text(digests["manifest"]),
        configuration_digest=_text(digests["configuration"]),
        checkout=CheckoutEvidence(
            target=_text(checkout["target"]),
            head=_text(checkout["head"]),
            shallow=cast("bool", checkout["shallow"]),
            ancestry_complete=cast("bool", checkout["ancestry-complete"]),
            tags_complete=cast("bool", checkout["tags-complete"]),
            credentials_persisted=cast(
                "bool", checkout["credentials-persisted"]
            ),
            authoritative_remote=_text(checkout["authoritative-remote"]),
            authoritative_remote_url=_text(
                checkout["authoritative-remote-url"]
            ),
            tag_refspec=_text(checkout["tag-refspec"]),
        ),
        project_nodes=tuple(
            dotnet_project_node_from_document(item)
            for item in array(document["project-nodes"])
        ),
        global_inputs=tuple(
            GlobalInput(
                path=_text(item["path"]),
                content_digest=_text(item["content-digest"]),
                project_ids=tuple(
                    _text(project) for project in array(item["project-ids"])
                ),
            )
            for item in (
                _object(item) for item in array(document["global-inputs"])
            )
        ),
        build_capabilities=tuple(
            _text(item) for item in array(document["build-capabilities"])
        ),
        nbgv=dotnet_nbgv_facts_from_document(document["nbgv"]),
        unresolved=tuple(_text(item) for item in array(document["unresolved"])),
        conflicts=tuple(_text(item) for item in array(document["conflicts"])),
        outcome=_text(document["outcome"]),
        diagnostic_reference=cast(
            "str | None", document["diagnostic-reference"]
        ),
        source_input_manifest=tuple(
            (_text(item["path"]), _text(item["content-digest"]))
            for item in (
                _object(item)
                for item in array(document["source-input-manifest"])
            )
        ),
        native_evaluation_digest=_text(document["native-evaluation-digest"]),
    )
    validate_dotnet_provider_result(result)
    if result.to_document() != document:
        message = "native Provider result is not the exact normalized schema"
        raise ValueError(message)
    return result


@dataclass(frozen=True, slots=True)
class DotnetProviderFactBundle:
    """Immutable .NET Provider transport and request bindings."""

    schema: str
    binding: ProviderBinding
    manifest_digest: str
    manifest_entry_id: str
    request_artifact_id: int
    request_artifact_digest: str
    provider_result: DotnetProviderResult
    provider_result_digest: str
    transport_id: int
    transport_digest: str

    def to_document(self) -> dict[str, JsonValue]:
        """Serialize the exact Provider Fact Bundle."""
        return {
            "schema": self.schema,
            "binding": _provider_binding_document(self.binding),
            "provider-request-manifest-digest": self.manifest_digest,
            "provider-request-entry-id": self.manifest_entry_id,
            "request-artifact": {
                "artifact-id": self.request_artifact_id,
                "artifact-digest": self.request_artifact_digest,
            },
            "provider-result": {
                "payload": self.provider_result.to_document(),
                "payload-canonical-digest": self.provider_result_digest,
            },
            "transport": {
                "artifact-id": self.transport_id,
                "artifact-digest": self.transport_digest,
            },
        }

    @property
    def bundle_digest(self) -> str:
        """Digest the complete Bundle payload."""
        return canonical_sha256(self.to_document())


def create_dotnet_provider_fact_bundle(  # noqa: PLR0913
    result: DotnetProviderResult,
    *,
    manifest_digest: str,
    manifest_entry_id: str,
    request_artifact_id: int,
    request_artifact_digest: str,
    transport_id: int,
    transport_digest: str,
) -> DotnetProviderFactBundle:
    """Create a strictly bound immutable .NET Bundle."""
    validate_dotnet_provider_result(result)
    for value in (manifest_digest, request_artifact_digest, transport_digest):
        _digest(value)
    if not manifest_entry_id or any(
        type(value) is not int or value <= 0
        for value in (request_artifact_id, transport_id)
    ):
        message = "invalid Provider Bundle request/transport identity"
        raise ValueError(message)
    return DotnetProviderFactBundle(
        DOTNET_PROVIDER_FACT_BUNDLE_SCHEMA,
        result.binding,
        manifest_digest,
        manifest_entry_id,
        request_artifact_id,
        request_artifact_digest,
        result,
        result.result_digest,
        transport_id,
        transport_digest,
    )


def dotnet_provider_input_candidates(paths: tuple[str, ...]) -> tuple[str, ...]:
    """Select the same bounded source closure from a target Git tree."""
    return tuple(
        sorted(
            path
            for path in paths
            if path in _GLOBAL_INPUTS
            or path.endswith((".props", ".targets"))
            or path.startswith(DOTNET_PROJECT_ROOT + "/")
        )
    )


def dotnet_provider_input_paths(repo_root: Path) -> tuple[str, ...]:
    """Return only tracked exact-target inputs for the selected project."""
    paths = _run_command(("git", "ls-files", "-z"), repo_root).split("\0")
    return dotnet_provider_input_candidates(
        tuple(path for path in paths if path)
    )


def dotnet_input_digests(
    inputs: tuple[tuple[str, str], ...],
) -> tuple[str, str]:
    """Define stable source preimages shared by Provider and compiler."""
    manifest = dict(inputs)
    if DOTNET_PROJECT_PATH not in manifest:
        message = "selected .NET project is missing from inputs"
        raise ValueError(message)
    return manifest[DOTNET_PROJECT_PATH], canonical_sha256(
        {
            "schema": "workflow-delivery/v3/dotnet-provider-configuration",
            "source-input-manifest": [
                {"path": path, "content-digest": digest}
                for path, digest in inputs
            ],
        }
    )


_EVALUATED_PROPERTIES = (
    "BuildVersion",
    "GitVersionHeight",
    "GitCommitId",
    "PublicRelease",
    "NuGetPackageVersion",
    "AssemblyVersion",
    "AssemblyFileVersion",
    "AssemblyInformationalVersion",
    "PackageId",
    "TargetFramework",
    "TargetFrameworks",
    "RuntimeIdentifier",
    "RuntimeIdentifiers",
    "IsPackable",
    "IncludeSymbols",
    "AssemblyName",
    "OutputType",
    "PackageVersion",
    "NETCoreSdkVersion",
    "MSBuildVersion",
    "RepositoryUrl",
)


def _validate_native_imports(
    repo_root: Path, helper: NativeNuGetHelper, log: Path
) -> dict[str, JsonValue]:
    audit = helper.audit_binlog(log)
    if (
        not audit.get("completed")
        or not audit.get("succeeded")
        or audit.get("nbgvExecuted") is not True
    ):
        message = "Provider did not execute the native NBGV target"
        raise ValueError(message)
    inputs = set(dotnet_provider_input_paths(repo_root))
    imports = audit.get("imports")
    if not isinstance(imports, list):
        message = "native imported configuration evidence is missing"
        raise TypeError(message)
    nbgv_targets = [
        Path(item)
        for item in imports
        if isinstance(item, str)
        and Path(item).name == "Nerdbank.GitVersioning.targets"
    ]
    if (
        len(nbgv_targets) != 1
        or nbgv_targets[0].parent.parent.name != "3.10.94"
    ):
        message = "native evaluation did not import pinned NBGV targets"
        raise ValueError(message)
    for imported in imports:
        if isinstance(imported, str) and Path(imported).is_relative_to(
            repo_root
        ):
            relative = Path(imported).relative_to(repo_root).as_posix()
            if relative not in inputs and "/obj/" not in relative:
                message = "native evaluation imported undeclared configuration"
                raise ValueError(message)
    return audit


def evaluate_dotnet_project(  # noqa: C901, PLR0915
    repo_root: Path,
    helper: NativeNuGetHelper,
    evidence_directory: Path,
    *,
    dependency_directory: Path | None = None,
) -> tuple[DotnetProjectNode, DotnetNbgvFacts, str]:
    """Evaluate real native targets; this runs only in the unprivileged zone."""
    evidence_directory.mkdir(parents=True, exist_ok=False)
    restore_properties: tuple[str, ...] = ()
    if dependency_directory is not None:
        if not dependency_directory.is_absolute():
            msg = "dependency directory must be absolute"
            raise ValueError(msg)
        dependency_directory.mkdir(parents=True, exist_ok=False)
        restore_properties = (
            "-property:RestorePackagesPath=" + str(dependency_directory),
            "-property:RestoreFallbackFolders=",
            "-property:RestoreAdditionalProjectFallbackFolders=",
        )
    project = repo_root / DOTNET_PROJECT_PATH
    environment = neutral_dotnet_environment()
    version = run_native(
        ("dotnet", "--version"), repo_root, environment
    ).strip()
    if version != "10.0.300":
        message = ".NET SDK does not match the pinned toolchain"
        raise ValueError(message)
    nbgv_version = run_native(
        ("dotnet", "nbgv", "--version"), repo_root, environment
    ).strip()
    if nbgv_version.partition("+")[0] != "3.10.94":
        message = "NBGV CLI does not match the pinned toolchain"
        raise ValueError(message)
    log = evidence_directory / "provider.binlog"
    command = (
        "dotnet",
        "msbuild",
        str(project),
        "-restore",
        "-target:GetBuildVersion",
        "-property:Configuration=Release",
        "-property:RestoreLockedMode=true",
        *restore_properties,
        "-getProperty:" + ",".join(_EVALUATED_PROPERTIES),
        "-getItem:ProjectReference,PackageReference",
        "-bl:" + str(log),
    )
    native = _object(
        parse_json_strict(
            run_native(
                command,
                repo_root,
                environment,
                diagnostics=(
                    evidence_directory / "evaluation"
                    if dependency_directory is not None
                    else None
                ),
            )
        )
    )
    properties = _object(native["Properties"])
    items = _object(native["Items"])
    if (
        properties.get("NETCoreSdkVersion") != "10.0.300"
        or properties.get("MSBuildVersion") != "18.6.3"
    ):
        message = "native evaluation used an unsupported toolchain"
        raise ValueError(message)
    if (
        items.get("ProjectReference") != []
        or any(
            properties.get(name)
            for name in (
                "TargetFrameworks",
                "RuntimeIdentifier",
                "RuntimeIdentifiers",
            )
        )
        or properties.get("OutputType") != "Library"
    ):
        message = "native project exceeds the selected dependency/output scope"
        raise ValueError(message)
    cli = _object(
        parse_json_strict(
            run_native(
                (
                    "dotnet",
                    "nbgv",
                    "get-version",
                    "--project",
                    str(project.parent),
                    "--format",
                    "json",
                ),
                repo_root,
                environment,
            )
        )
    )
    for native_key, cli_key in (
        ("BuildVersion", "Version"),
        ("GitVersionHeight", "VersionHeight"),
        ("GitCommitId", "GitCommitId"),
        ("NuGetPackageVersion", "NuGetPackageVersion"),
        ("AssemblyVersion", "AssemblyVersion"),
        ("AssemblyFileVersion", "AssemblyFileVersion"),
        ("AssemblyInformationalVersion", "AssemblyInformationalVersion"),
    ):
        if str(properties.get(native_key)) != str(cli.get(cli_key)):
            message = "MSBuild and official NBGV facts conflict"
            raise ValueError(message)
    if (
        str(properties.get("PublicRelease")).lower()
        != str(cli.get("PublicRelease")).lower()
    ):
        message = "NBGV public release facts conflict"
        raise ValueError(message)
    identity = helper.normalize_identity(
        _text(properties["PackageId"]), _text(properties["NuGetPackageVersion"])
    )
    project_node = DotnetProjectNode(
        DOTNET_RELEASE_UNIT,
        _text(properties["PackageId"]),
        DOTNET_PROJECT_ROOT,
        DOTNET_PROJECT_PATH,
        _text(properties["TargetFramework"]),
        properties["IsPackable"] == "true",
        properties["IncludeSymbols"] == "true",
        (),
        _text(identity["normalizedPackageId"]),
        _text(identity["normalizedVersion"]),
    )
    validate_dotnet_project_node(project_node)
    references = items.get("PackageReference")
    if not isinstance(references, list):
        message = "native package references missing"
        raise TypeError(message)
    dependencies: list[JsonValue] = []
    for item in references:
        reference = _object(item)
        dependencies.append(
            {
                name: reference.get(name, "")
                for name in (
                    "Identity",
                    "Version",
                    "IncludeAssets",
                    "ExcludeAssets",
                    "PrivateAssets",
                )
            }
        )
    effective: dict[str, JsonValue] = {
        "properties": properties,
        "package-references": dependencies,
        "identity": identity,
    }
    evaluation_digest = canonical_sha256(effective)
    facts = DotnetNbgvFacts(
        _text(cli["Version"]),
        _text(cli["SemVer1"]),
        _text(cli["SemVer2"]),
        cast("int", cli["VersionHeight"]),
        _text(cli["GitCommitId"]),
        cast("bool", cli["PublicRelease"]),
        _text(cli["NuGetPackageVersion"]),
        _text(cli["AssemblyVersion"]),
        _text(cli["AssemblyFileVersion"]),
        _text(cli["AssemblyInformationalVersion"]),
        canonical_sha256({"nbgv": cli, "evaluation": effective}),
    )
    validate_dotnet_nbgv_facts(facts, target=facts.git_commit_id)
    audit = _validate_native_imports(repo_root, helper, log)
    (evidence_directory / "native-facts.json").write_text(
        json.dumps(
            {"nbgv": cli, "evaluation": effective, "audit": audit}, indent=2
        ),
        encoding="utf-8",
    )
    if dependency_directory is not None:
        assets = project.parent / "obj" / "project.assets.json"
        (evidence_directory / "project.assets.json").write_bytes(
            assets.read_bytes()
        )
    return project_node, facts, evaluation_digest


def provide_dotnet_repository_facts(  # noqa: PLR0913
    repo_root: Path,
    binding: ProviderBinding,
    materialization: CheckoutMaterialization,
    *,
    helper: NativeNuGetHelper,
    evidence_directory: Path,
    dependency_directory: Path | None = None,
) -> DotnetProviderResult:
    """Evaluate an isolated exact target with complete history and tags."""
    validate_provider_binding(binding)
    source_checkout = verify_exact_checkout(
        repo_root, binding.target, materialization
    )
    with _isolated_exact_target_repository(
        repo_root,
        binding.target,
        source_checkout.authoritative_remote_url,
        runner=_run_command,
    ) as isolated:
        checkout = verify_exact_checkout(
            isolated, binding.target, materialization
        )
        paths = dotnet_provider_input_paths(isolated)
        inputs = tuple(
            (
                path,
                "sha256:"
                + hashlib.sha256((isolated / path).read_bytes()).hexdigest(),
            )
            for path in paths
        )
        if dependency_directory is None:
            project, nbgv, native_digest = evaluate_dotnet_project(
                isolated, helper, evidence_directory
            )
        else:
            project, nbgv, native_digest = evaluate_dotnet_project(
                isolated,
                helper,
                evidence_directory,
                dependency_directory=dependency_directory,
            )
        if inputs != tuple(
            (
                path,
                "sha256:"
                + hashlib.sha256((isolated / path).read_bytes()).hexdigest(),
            )
            for path in paths
        ):
            message = "native evaluation mutated declared source inputs"
            raise ValueError(message)
    manifest_digest, configuration_digest = dotnet_input_digests(inputs)
    result = DotnetProviderResult(
        binding,
        DOTNET_PROVIDER_LOGICAL_ID,
        DOTNET_PROVIDER_IMPLEMENTATION_ID,
        DOTNET_PROVIDER_EXECUTION_MODE,
        DOTNET_PROVIDER_EXECUTION_CLASS,
        DOTNET_TOOLCHAIN,
        manifest_digest,
        configuration_digest,
        checkout,
        (project,),
        tuple(
            GlobalInput(path, digest, (DOTNET_RELEASE_UNIT,))
            for path, digest in inputs
        ),
        ("dotnet/nuget-package-v1",),
        nbgv,
        (),
        (),
        "success",
        None,
        inputs,
        native_digest,
    )
    validate_dotnet_provider_result(result)
    return result
