"""Frozen NuGet package construction and separate content/consumer checks."""

from __future__ import annotations

import base64
import hashlib
import json
import re
from dataclasses import dataclass
from pathlib import Path, PurePosixPath
from tempfile import TemporaryDirectory
from xml.sax.saxutils import escape

from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.repository.dotnet_provider import (
    DOTNET_PACKAGE,
    DOTNET_PROJECT_PATH,
    DOTNET_PROJECT_ROOT,
    DOTNET_RELEASE_UNIT,
    DOTNET_TOOLCHAIN,
    DotnetNbgvFacts,
    NativeNuGetHelper,
    dotnet_nbgv_facts_from_document,
    neutral_dotnet_environment,
    run_native,
    validate_dotnet_nbgv_facts,
)

BUILD_DEFINITION = "dotnet/nuget-package-v1"
WITNESS_PATH = "workflow-delivery/provenance.json"


@dataclass(frozen=True, slots=True)
class DotnetPackageTargetWitness:
    """Execution-independent target identity embedded before packing."""

    target: str
    release_unit: str
    nbgv: DotnetNbgvFacts
    build_definition: str
    catalog_digest: str
    control_digest: str
    purpose: str

    def to_document(self) -> dict[str, JsonValue]:
        """Return the explicit NuGet witness variant."""
        return {
            "schema": "workflow-delivery/v3/package-target-witness",
            "target": self.target,
            "release-unit": self.release_unit,
            "nbgv": self.nbgv.to_document(),
            "build-definition": self.build_definition,
            "catalog-digest": self.catalog_digest,
            "control-digest": self.control_digest,
            "purpose": self.purpose,
        }

    @property
    def canonical_bytes(self) -> bytes:
        """Return precisely the bytes admitted by the content obligation."""
        validate_dotnet_package_target_witness(self)
        return canonicalize(self.to_document())


def validate_dotnet_package_target_witness(
    witness: DotnetPackageTargetWitness,
) -> None:
    """Keep NuGet witness facts separate from npm and mutable execution IDs."""
    if (
        type(witness) is not DotnetPackageTargetWitness
        or witness.release_unit != DOTNET_RELEASE_UNIT
        or witness.build_definition != BUILD_DEFINITION
        or witness.purpose
        not in {
            "ci-pr-slice-shadow",
            "slice-validation",
            "live-release",
            "release-simulation",
            "destination-acceptance",
        }
    ):
        message = "unsupported NuGet Package Target Witness"
        raise ValueError(message)
    validate_dotnet_nbgv_facts(witness.nbgv, target=witness.target)
    if any(
        not re.fullmatch(r"sha256:[0-9a-f]{64}", value)
        for value in (witness.catalog_digest, witness.control_digest)
    ):
        message = "invalid NuGet witness control binding"
        raise ValueError(message)


def dotnet_package_target_witness_from_document(
    document: JsonValue,
) -> DotnetPackageTargetWitness:
    """Read the closed canonical NuGet witness schema."""
    expected = {
        "schema",
        "target",
        "release-unit",
        "nbgv",
        "build-definition",
        "catalog-digest",
        "control-digest",
        "purpose",
    }
    if (
        not isinstance(document, dict)
        or set(document) != expected
        or document["schema"] != "workflow-delivery/v3/package-target-witness"
    ):
        message = "invalid NuGet witness schema"
        raise ValueError(message)
    texts = {key: value for key, value in document.items() if key != "nbgv"}
    if any(not isinstance(value, str) for value in texts.values()):
        message = "invalid NuGet witness string"
        raise ValueError(message)
    witness = DotnetPackageTargetWitness(
        str(document["target"]),
        str(document["release-unit"]),
        dotnet_nbgv_facts_from_document(document["nbgv"]),
        str(document["build-definition"]),
        str(document["catalog-digest"]),
        str(document["control-digest"]),
        str(document["purpose"]),
    )
    validate_dotnet_package_target_witness(witness)
    return witness


@dataclass(frozen=True, slots=True)
class DotnetArtifactExpectation:
    """Frozen package identity, assembly, and canonical witness."""

    package_name: str
    nuget_package_version: str
    normalized_package_id: str
    normalized_version: str
    witness_bytes: bytes
    assembly_name: str = DOTNET_PACKAGE


@dataclass(frozen=True, slots=True)
class DotnetArtifactManifest:
    """Whole-archive integrity and declared entry closure."""

    basename: str
    entries: tuple[str, ...]
    sha256: str
    sha512: str
    byte_size: int


@dataclass(frozen=True, slots=True)
class DotnetBuildRequest:
    """Frozen source inputs for an isolated native Build invocation."""

    source_root: Path
    declared_inputs: tuple[str, ...]
    source_input_manifest: tuple[tuple[str, str], ...]
    witness: DotnetPackageTargetWitness
    helper: NativeNuGetHelper
    evidence_directory: Path


@dataclass(frozen=True, slots=True)
class DotnetBuildResult:
    """Original immutable archive and verified build provenance."""

    package: bytes
    manifest: DotnetArtifactManifest
    expectation: DotnetArtifactExpectation
    witness: bytes
    source_input_manifest: tuple[tuple[str, str], ...]
    toolchain: tuple[tuple[str, str], ...]


@dataclass(frozen=True, slots=True)
class DotnetConsumerResult:
    """Evidence of fresh exact-version restore, build, and marker execution."""

    project_id: str
    witness_sha256: str
    package_sha256: str
    normalized_package_id: str
    normalized_version: str


@dataclass(frozen=True, slots=True)
class DotnetFixturePackMetadata:
    """The only pack overrides allowed for the acceptance comparison archive."""

    package_id: str
    version: str
    description: str

    @property
    def properties(self) -> tuple[str, ...]:
        """Supply literal properties without additional MSBuild operands."""
        values = {
            "PackageId": self.package_id,
            "PackageVersion": self.version,
            "PackageDescription": self.description,
        }
        if any(
            not value or any(char in value for char in ";\r\n%$")
            for value in values.values()
        ):
            message = "unsupported fixture pack metadata"
            raise ValueError(message)
        return tuple(
            f"-property:{key}={value}" for key, value in values.items()
        )


@dataclass(frozen=True, slots=True)
class DotnetPackOutput:
    """An original pack filename and its unmodified archive bytes."""

    basename: str
    content: bytes


def _digest(content: bytes) -> str:
    return "sha256:" + hashlib.sha256(content).hexdigest()


def qualify_nuget_artifact_contents(  # noqa: C901
    package: bytes,
    expectation: DotnetArtifactExpectation,
    helper: NativeNuGetHelper,
) -> DotnetArtifactManifest:
    """Inspect official identity, exact payload scope, and canonical witness."""
    if (
        type(expectation) is not DotnetArtifactExpectation
        or expectation.package_name != DOTNET_PACKAGE
        or expectation.assembly_name != DOTNET_PACKAGE
    ):
        message = "unsupported NuGet artifact expectation"
        raise ValueError(message)
    witness = dotnet_package_target_witness_from_document(
        parse_canonical_json(expectation.witness_bytes)
    )
    if witness.nbgv.nuget_package_version != expectation.nuget_package_version:
        message = "NuGet witness and frozen package version disagree"
        raise ValueError(message)
    expected_identity = helper.normalize_identity(
        expectation.package_name, expectation.nuget_package_version
    )
    if (
        expected_identity["normalizedPackageId"],
        expected_identity["normalizedVersion"],
    ) != (expectation.normalized_package_id, expectation.normalized_version):
        message = "frozen NuGet normalized coordinate mismatch"
        raise ValueError(message)
    facts = helper.inspect_package(package)
    identity = facts.get("identity")
    if not isinstance(identity, dict) or (
        identity.get("normalizedPackageId"),
        identity.get("normalizedVersion"),
    ) != (expectation.normalized_package_id, expectation.normalized_version):
        message = "native package identity conflicts with frozen projection"
        raise ValueError(message)
    if (
        facts.get("frameworks") != ["net10.0"]
        or facts.get("dependencies") != []
    ):
        message = "NuGet package framework/dependency closure mismatch"
        raise ValueError(message)
    if facts.get("assembly") != {
        "name": expectation.assembly_name,
        "version": witness.nbgv.assembly_version,
        "fileVersion": witness.nbgv.assembly_file_version,
        "informationalVersion": witness.nbgv.assembly_informational_version,
    }:
        message = "NuGet assembly does not consume the frozen native versions"
        raise ValueError(message)
    encoded_witness = facts.get("witnessBase64")
    if (
        not isinstance(encoded_witness, str)
        or base64.b64decode(encoded_witness, validate=True)
        != expectation.witness_bytes
    ):
        message = "NuGet package witness bytes mismatch"
        raise ValueError(message)
    entries = facts.get("entries")
    if not isinstance(entries, list) or any(
        not isinstance(item, str) for item in entries
    ):
        message = "NuGet package entries missing"
        raise ValueError(message)
    names = tuple(str(item) for item in entries)
    required = {
        "_rels/.rels",
        "[Content_Types].xml",
        f"{DOTNET_PACKAGE}.nuspec",
        "README.md",
        f"lib/net10.0/{DOTNET_PACKAGE}.dll",
        f"lib/net10.0/{DOTNET_PACKAGE}.xml",
        WITNESS_PATH,
    }
    core = [
        name
        for name in names
        if re.fullmatch(
            r"package/services/metadata/core-properties/[0-9a-f]+\.psmdcp", name
        )
    ]
    if len(core) != 1 or set(names) != required | set(core):
        message = "NuGet package declared entry closure mismatch"
        raise ValueError(message)
    repository = facts.get("repository")
    if (
        not isinstance(repository, dict)
        or repository.get("url") != "https://github.com/hcoona/three.git"
        or repository.get("commit") != witness.target
    ):
        message = "NuGet package source metadata mismatch"
        raise ValueError(message)
    return DotnetArtifactManifest(
        f"{DOTNET_PACKAGE}.{expectation.normalized_version}.nupkg",
        names,
        _digest(package),
        "sha512:" + hashlib.sha512(package).hexdigest(),
        len(package),
    )


def _frozen_properties(
    witness: DotnetPackageTargetWitness, intermediate: Path, witness_path: Path
) -> tuple[str, ...]:
    facts = witness.nbgv
    properties = {
        "WorkflowDeliveryFrozenBuild": "true",
        "WorkflowDeliveryNuGetVersion": facts.nuget_package_version,
        "WorkflowDeliveryAssemblyVersion": facts.assembly_version,
        "WorkflowDeliveryFileVersion": facts.assembly_file_version,
        "WorkflowDeliveryInformationalVersion": (
            facts.assembly_informational_version
        ),
        "WorkflowDeliveryWitnessPath": str(witness_path),
        "BaseIntermediateOutputPath": str(intermediate) + "/",
        "MSBuildProjectExtensionsPath": str(intermediate) + "/",
        "RepositoryCommit": witness.target,
        "SourceRevisionId": witness.target,
        "Configuration": "Release",
        "RestoreLockedMode": "true",
    }
    if any(
        ";" in value or "\n" in value or "\r" in value
        for value in properties.values()
    ):
        message = "unsupported MSBuild property operand"
        raise ValueError(message)
    return tuple(
        f"-property:{key}={value}" for key, value in properties.items()
    )


def _capture_build_inputs(request: DotnetBuildRequest) -> dict[str, bytes]:
    if (
        request.declared_inputs
        != tuple(path for path, _ in request.source_input_manifest)
        or tuple(sorted(request.declared_inputs)) != request.declared_inputs
        or len(set(request.declared_inputs)) != len(request.declared_inputs)
    ):
        message = "Build declared inputs disagree with the Provider manifest"
        raise ValueError(message)
    captured: dict[str, bytes] = {}
    for path, digest in request.source_input_manifest:
        relative = PurePosixPath(path)
        source = request.source_root / path
        if (
            relative.is_absolute()
            or ".." in relative.parts
            or source.is_symlink()
            or not source.resolve().is_relative_to(
                request.source_root.resolve()
            )
        ):
            message = "Build input escapes its exact source tree"
            raise ValueError(message)
        content = source.read_bytes()
        if _digest(content) != digest:
            message = "Build source input digest mismatch"
            raise ValueError(message)
        captured[path] = content
    if (
        DOTNET_PROJECT_PATH not in captured
        or f"{DOTNET_PROJECT_ROOT}/packages.lock.json" not in captured
    ):
        message = "Build requires exact project and locked dependencies"
        raise ValueError(message)
    return captured


def _offline_build_inputs(
    archives: tuple[Path, ...],
) -> dict[str, bytes]:
    captured: dict[str, bytes] = {}
    for archive in archives:
        if (
            not archive.is_absolute()
            or archive.is_symlink()
            or not archive.is_file()
            or archive.suffix != ".nupkg"
            or archive.name.casefold() in {name.casefold() for name in captured}
        ):
            message = "fixture dependency must be a distinct local nupkg"
            raise ValueError(message)
        captured[archive.name] = archive.read_bytes()
    return captured


def _offline_build_environment(root: Path) -> dict[str, str]:
    environment = neutral_dotnet_environment()
    home = root / "home"
    home.mkdir()
    environment.update(
        HOME=str(home),
        USERPROFILE=str(home),
        DOTNET_CLI_HOME=str(home),
        NUGET_PACKAGES=str(root / "dependency-cache"),
        NUGET_HTTP_CACHE_PATH=str(root / "http-cache"),
        NUGET_PLUGINS_CACHE_PATH=str(root / "plugin-cache"),
    )
    return environment


def _offline_restore_inputs(
    root: Path, dependencies: dict[str, bytes]
) -> tuple[Path, dict[str, str], tuple[str, ...]]:
    feed = root / "local-feed"
    feed.mkdir()
    for name, content in dependencies.items():
        (feed / name).write_bytes(content)
    config = root / "offline.nuget.config"
    config.write_text(
        "<configuration><packageSources><clear/>"
        f'<add key="fixture" value="{escape(str(feed))}"/>'
        "</packageSources><auditSources><clear/></auditSources>"
        "<fallbackPackageFolders><clear/></fallbackPackageFolders>"
        "<packageSourceMapping><clear/>"
        '<packageSource key="fixture"><package pattern="*"/>'
        "</packageSource></packageSourceMapping></configuration>",
        encoding="utf-8",
    )
    environment = _offline_build_environment(root)
    properties = (
        "-property:NuGetAudit=false",
        "-property:RestoreSources=" + str(feed),
        "-property:RestoreFallbackFolders=",
        "-property:RestoreAdditionalProjectFallbackFolders=",
        "-property:RestorePackagesPath=" + environment["NUGET_PACKAGES"],
    )
    return config, environment, properties


def _native_with_diagnostics(
    command: tuple[str, ...],
    root: Path,
    environment: dict[str, str],
    diagnostics: Path | None,
) -> str:
    if diagnostics is None:
        return run_native(command, root, environment)
    return run_native(command, root, environment, diagnostics=diagnostics)


def _audit_frozen_operation(
    helper: NativeNuGetHelper, log: Path, *, forbid_compile: bool
) -> None:
    audit = helper.audit_binlog(log)
    log.with_name(log.stem + "-audit.json").write_text(
        json.dumps(audit, indent=2), encoding="utf-8"
    )
    if (
        audit.get("completed") is not True
        or audit.get("succeeded") is not True
        or audit.get("nbgvExecuted") is not False
    ):
        message = "frozen Build recomputed NBGV or lacks complete proof"
        raise ValueError(message)
    if forbid_compile and (
        not isinstance(audit.get("tasks"), list) or "Csc" in audit["tasks"]
    ):
        message = "fixture pack reran compilation or lacks task evidence"
        raise ValueError(message)


def _pack_output(directory: Path) -> DotnetPackOutput:
    packages = tuple(directory.iterdir())
    if len(packages) != 1 or packages[0].suffix != ".nupkg":
        message = "Build must emit exactly one nupkg and no snupkg"
        raise ValueError(message)
    return DotnetPackOutput(packages[0].name, packages[0].read_bytes())


def pack_frozen_dotnet_archives(
    request: DotnetBuildRequest,
    *,
    comparison: DotnetFixturePackMetadata | None = None,
    dependency_archives: tuple[Path, ...] | None = None,
) -> tuple[DotnetPackOutput, ...]:
    """Compile once and pack original archives before disposing compiled inputs.

    The optional second output is acceptance-only. Its local dependency feed
    and isolated caches never replace ordinary Release restore configuration.
    """
    validate_dotnet_package_target_witness(request.witness)
    captured = _capture_build_inputs(request)
    fixture = comparison is not None
    if fixture != (dependency_archives is not None) or (
        fixture and request.witness.purpose != "destination-acceptance"
    ):
        message = (
            "paired packing requires offline destination-acceptance inputs"
        )
        raise ValueError(message)
    comparison_properties = comparison.properties if comparison else ()
    dependencies = _offline_build_inputs(dependency_archives or ())
    request.evidence_directory.mkdir(parents=True, exist_ok=False)
    outputs: list[DotnetPackOutput] = []
    with TemporaryDirectory(prefix="wdv3-nuget-build-") as temporary:
        root = Path(temporary)
        stage = root / "stage"
        for path, content in captured.items():
            destination = stage / path
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes(content)
        witness_path = root / "provenance.json"
        witness_path.write_bytes(request.witness.canonical_bytes)
        intermediate = root / "frozen-obj"
        output = root / "packages"
        properties = _frozen_properties(
            request.witness, intermediate, witness_path
        )
        environment = neutral_dotnet_environment()
        config = stage / "nuget.config"
        if fixture:
            config, environment, offline_properties = _offline_restore_inputs(
                root, dependencies
            )
            properties += offline_properties
            (request.evidence_directory / "offline.nuget.config").write_bytes(
                config.read_bytes()
            )
            (request.evidence_directory / "inputs.json").write_text(
                json.dumps(
                    {
                        "sourceInputs": request.source_input_manifest,
                        "witness": request.witness.to_document(),
                        "toolchain": DOTNET_TOOLCHAIN,
                        "dependencyArchives": {
                            name: {
                                "sha256": _digest(content),
                                "size": len(content),
                            }
                            for name, content in dependencies.items()
                        },
                        "properties": properties,
                        "comparisonProperties": comparison_properties,
                        "environment": environment,
                    },
                    indent=2,
                ),
                encoding="utf-8",
            )
        if (
            _native_with_diagnostics(
                ("dotnet", "--version"),
                stage,
                environment,
                request.evidence_directory / "sdk" if fixture else None,
            ).strip()
            != "10.0.300"
        ):
            message = "Build SDK does not match the frozen toolchain"
            raise ValueError(message)
        project = str(stage / DOTNET_PROJECT_PATH)
        operations: list[tuple[str, str, tuple[str, ...], tuple[str, ...]]] = [
            ("restore", "restore", ("--configfile", str(config)), ()),
            ("build", "build", ("--no-restore",), ()),
            (
                "pack",
                "pack",
                ("--no-restore", "--no-build", "--output", str(output)),
                (),
            ),
        ]
        if fixture:
            operations.append(
                (
                    "pack-comparison",
                    "pack",
                    (
                        "--no-restore",
                        "--no-build",
                        "--output",
                        str(root / "comparison"),
                    ),
                    comparison_properties,
                )
            )
        for label, operation, options, overrides in operations:
            log = request.evidence_directory / f"{label}.binlog"
            _native_with_diagnostics(
                (
                    "dotnet",
                    operation,
                    project,
                    *options,
                    *properties,
                    *overrides,
                    "-bl:" + str(log),
                ),
                stage,
                environment,
                request.evidence_directory / label if fixture else None,
            )
            _audit_frozen_operation(
                request.helper,
                log,
                forbid_compile=fixture and operation == "pack",
            )
            if operation == "pack":
                archive = _pack_output(Path(options[-1]))
                outputs.append(archive)
                if fixture:
                    (
                        request.evidence_directory / label / archive.basename
                    ).write_bytes(archive.content)
        lock = stage / DOTNET_PROJECT_ROOT / "packages.lock.json"
        if (
            lock.read_bytes()
            != captured[f"{DOTNET_PROJECT_ROOT}/packages.lock.json"]
        ):
            message = "frozen Build changed the locked dependency graph"
            raise ValueError(message)
    return tuple(outputs)


def build_dotnet_package(request: DotnetBuildRequest) -> DotnetBuildResult:
    """Apply frozen native facts and preserve the original Release archive."""
    (output,) = pack_frozen_dotnet_archives(request)
    package = output.content
    identity = request.helper.normalize_identity(
        DOTNET_PACKAGE, request.witness.nbgv.nuget_package_version
    )
    expectation = DotnetArtifactExpectation(
        DOTNET_PACKAGE,
        request.witness.nbgv.nuget_package_version,
        str(identity["normalizedPackageId"]),
        str(identity["normalizedVersion"]),
        request.witness.canonical_bytes,
    )
    manifest = qualify_nuget_artifact_contents(
        package, expectation, request.helper
    )
    return DotnetBuildResult(
        package,
        manifest,
        expectation,
        expectation.witness_bytes,
        request.source_input_manifest,
        DOTNET_TOOLCHAIN,
    )


def qualify_nuget_restore_build_invoke(
    package: bytes,
    expectation: DotnetArtifactExpectation,
    helper: NativeNuGetHelper,
    *,
    evidence_directory: Path,
    offline_fixture: bool = False,
) -> DotnetConsumerResult:
    """Restore the exact archive into a new cache and invoke its marker API.

    Offline acceptance additionally retains diagnostics and disables remote
    vulnerability auditing for this fixture. Ordinary Release is unchanged.
    """
    if (
        offline_fixture
        and dotnet_package_target_witness_from_document(
            parse_canonical_json(expectation.witness_bytes)
        ).purpose
        != "destination-acceptance"
    ):
        message = "offline consumer requires destination-acceptance purpose"
        raise ValueError(message)
    manifest = qualify_nuget_artifact_contents(package, expectation, helper)
    evidence_directory.mkdir(parents=True, exist_ok=False)
    with TemporaryDirectory(prefix="wdv3-nuget-consumer-") as temporary:
        root = Path(temporary)
        source = root / "source"
        source.mkdir()
        (source / manifest.basename).write_bytes(package)
        cache = root / "packages"
        home = root / "home"
        home.mkdir()
        environment = neutral_dotnet_environment()
        environment.update(
            HOME=str(home),
            USERPROFILE=str(home),
            DOTNET_CLI_HOME=str(home),
            NUGET_PACKAGES=str(cache),
            NUGET_HTTP_CACHE_PATH=str(root / "http-cache"),
            NUGET_PLUGINS_CACHE_PATH=str(root / "plugin-cache"),
        )
        (root / "global.json").write_text(
            '{"sdk":{"version":"10.0.300","rollForward":"disable"}}',
            encoding="utf-8",
        )
        (root / "nuget.config").write_text(
            "<configuration><packageSources><clear/>"
            f'<add key="qualified" value="{escape(str(source))}"/>'
            "</packageSources><packageSourceMapping><clear/>"
            '<packageSource key="qualified"><package pattern="*"/>'
            "</packageSource></packageSourceMapping></configuration>",
            encoding="utf-8",
        )
        (root / "consumer.csproj").write_text(
            '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
            "<OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>"
            "<RestoreFallbackFolders></RestoreFallbackFolders>"
            "<RestoreAdditionalProjectFallbackFolders>"
            "</RestoreAdditionalProjectFallbackFolders></PropertyGroup><ItemGroup>"
            f'<PackageReference Include="{escape(expectation.package_name)}" '
            f'Version="[{escape(expectation.nuget_package_version)}]"/>'
            "</ItemGroup></Project>",
            encoding="utf-8",
        )
        (root / "Program.cs").write_text(
            "System.Console.Write(HcoonaReleaseSmokeGithubPackages.Smoke.ProjectId);",
            encoding="utf-8",
        )
        if offline_fixture:
            for name in (
                "global.json",
                "nuget.config",
                "consumer.csproj",
                "Program.cs",
            ):
                (evidence_directory / name).write_bytes(
                    (root / name).read_bytes()
                )
        for operation, options in (
            (
                "restore",
                (
                    "--configfile",
                    str(root / "nuget.config"),
                    "--packages",
                    str(cache),
                    "--no-cache",
                ),
            ),
            ("build", ("--no-restore",)),
        ):
            _native_with_diagnostics(
                (
                    "dotnet",
                    operation,
                    str(root / "consumer.csproj"),
                    *options,
                    *(
                        ("-property:NuGetAudit=false",)
                        if offline_fixture
                        else ()
                    ),
                    "-bl:"
                    + str(evidence_directory / f"consumer-{operation}.binlog"),
                ),
                root,
                environment,
                evidence_directory / f"consumer-{operation}"
                if offline_fixture
                else None,
            )
            if offline_fixture:
                audit = helper.audit_binlog(
                    evidence_directory / f"consumer-{operation}.binlog"
                )
                (
                    evidence_directory / f"consumer-{operation}-audit.json"
                ).write_text(json.dumps(audit, indent=2), encoding="utf-8")
                if (
                    audit.get("completed") is not True
                    or audit.get("succeeded") is not True
                ):
                    message = "fixture consumer lacks complete build evidence"
                    raise ValueError(message)
        selected = (
            cache
            / expectation.normalized_package_id
            / expectation.normalized_version
        )
        actual = selected / (
            f"{expectation.normalized_package_id}."
            f"{expectation.normalized_version}.nupkg"
        )
        if (
            actual.read_bytes() != package
            or (selected / WITNESS_PATH).read_bytes()
            != expectation.witness_bytes
        ):
            message = (
                "consumer did not select the exact qualified archive/witness"
            )
            raise ValueError(message)
        project_id = _native_with_diagnostics(
            ("dotnet", str(root / "bin/Debug/net10.0/consumer.dll")),
            root,
            environment,
            evidence_directory / "consumer-invoke" if offline_fixture else None,
        )
        if project_id != DOTNET_RELEASE_UNIT:
            message = "consumer smoke marker API returned an unexpected value"
            raise ValueError(message)
    return DotnetConsumerResult(
        project_id,
        _digest(expectation.witness_bytes),
        manifest.sha256,
        expectation.normalized_package_id,
        expectation.normalized_version,
    )
