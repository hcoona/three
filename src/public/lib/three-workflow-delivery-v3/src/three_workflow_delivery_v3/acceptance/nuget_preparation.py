"""Protected NuGet fixture preparation; no destination or Release authority."""

from __future__ import annotations

import argparse
import hashlib
import io
import os
import re
import stat
import zipfile
from dataclasses import dataclass
from pathlib import Path, PurePosixPath
from typing import TYPE_CHECKING

from three_workflow_delivery_v3.acceptance.nuget_fixture import (
    NuGetFixtureRequest,
    prepare_nuget_fixture_pair,
)
from three_workflow_delivery_v3.adapters.dotnet import (
    DotnetBuildRequest,
    DotnetPackageTargetWitness,
)
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.catalogs import catalog_digest
from three_workflow_delivery_v3.records.artifacts import ArtifactReference
from three_workflow_delivery_v3.repository import compiler, dotnet_provider
from three_workflow_delivery_v3.repository.node_provider import (
    CheckoutMaterialization,
)

if TYPE_CHECKING:
    from collections.abc import Mapping, Sequence

WORKFLOW_PATH = ".github/workflows/workflow-delivery-v3-nuget-fixtures.yml"
REQUEST_SCHEMA = "workflow-delivery/v3/nuget-fixture-preparation-request"
SETUP_SCHEMA = "workflow-delivery/v3/nuget-fixture-setup"
COMPLETION_SCHEMA = "workflow-delivery/v3/nuget-fixture-preparation"
_ARCHIVE_LIMIT = 512 * 1024 * 1024
_ENTRY_LIMIT = 1024
_HELPER_DLL = "WorkflowDeliveryV3DotnetProvider.dll"


def _digest(content: bytes) -> str:
    return "sha256:" + hashlib.sha256(content).hexdigest()


def _object(value: JsonValue) -> dict[str, JsonValue]:
    if not isinstance(value, dict):
        msg = "fixture preparation requires an object"
        raise TypeError(msg)
    return value


@dataclass(frozen=True, slots=True)
class NuGetPreparationRequest:
    """One current preparation request, distinct from later native requests."""

    tooling_sha: str
    target: str
    generation: str
    workflow_run_id: int

    def __post_init__(self) -> None:
        """Close exact inputs without inferring destination absence."""
        if any(
            type(value) is not str
            or re.fullmatch(r"[0-9a-f]{40}", value) is None
            for value in (self.tooling_sha, self.target)
        ):
            msg = "fixture preparation requires exact commit SHAs"
            raise ValueError(msg)
        if (
            type(self.generation) is not str
            or re.fullmatch(r"[a-z0-9][a-z0-9-]{0,63}", self.generation) is None
        ):
            msg = "unsupported NuGet fixture generation label"
            raise ValueError(msg)
        if type(self.workflow_run_id) is not int or self.workflow_run_id <= 0:
            msg = "fixture preparation run ID must be positive"
            raise ValueError(msg)

    def to_document(self) -> dict[str, JsonValue]:
        """Bind the fixed protected workflow and selected package explicitly."""
        return {
            "schema": REQUEST_SCHEMA,
            "repository": "hcoona/three",
            "actor-id": "712433",
            "workflow-path": WORKFLOW_PATH,
            "tooling-sha": self.tooling_sha,
            "target": self.target,
            "generation": self.generation,
            "workflow-run-id": self.workflow_run_id,
            "run-attempt": 1,
            "package": dotnet_provider.DOTNET_PACKAGE,
            "purpose": "destination-acceptance",
        }

    @property
    def request_digest(self) -> str:
        """Identify the exact current preparation request."""
        return canonical_sha256(self.to_document())

    @property
    def context(self) -> compiler.CompilationContext:
        """Use the existing native compiler with acceptance-only provenance."""
        return compiler.CompilationContext(
            request_id=self.request_digest,
            purpose="destination-acceptance",
            workflow_run_id=self.workflow_run_id,
            run_attempt=1,
            target=self.target,
            producer="compile-nuget-fixtures",
            control=f"workflow-delivery-v3:{self.tooling_sha}",
            catalog_digest=catalog_digest(),
        )

    @property
    def provider_manifest(self) -> compiler.ProviderRequestManifest:
        """Close the target-evaluating request before native work."""
        return compiler.nuget_provider_manifest(
            self.context, provider_producer="discover-nuget-fixtures"
        )


def admit_preparation_request(
    content: bytes, platform: Mapping[str, str]
) -> NuGetPreparationRequest:
    """Require exact request bytes and actual caller-provided Actions context.

    The protected workflow supplies these platform facts. Local supplied-fact
    checks do not independently establish their GitHub provenance.
    """
    document = _object(parse_canonical_json(content))
    try:
        tooling = document["tooling-sha"]
        target = document["target"]
        generation = document["generation"]
        run_id = document["workflow-run-id"]
    except KeyError as error:
        msg = "incomplete fixture preparation request"
        raise ValueError(msg) from error
    if (
        not isinstance(tooling, str)
        or not isinstance(target, str)
        or not isinstance(generation, str)
        or type(run_id) is not int
    ):
        msg = "invalid fixture preparation request types"
        raise ValueError(msg)
    request = NuGetPreparationRequest(tooling, target, generation, run_id)
    if content != canonicalize(request.to_document()):
        msg = "fixture preparation request closure mismatch"
        raise ValueError(msg)
    expected = {
        "GITHUB_REPOSITORY": "hcoona/three",
        "GITHUB_ACTOR_ID": "712433",
        "GITHUB_EVENT_NAME": "workflow_dispatch",
        "GITHUB_REF": "refs/heads/main",
        "GITHUB_SHA": tooling,
        "GITHUB_WORKFLOW_SHA": tooling,
        "GITHUB_WORKFLOW_REF": f"hcoona/three/{WORKFLOW_PATH}@refs/heads/main",
        "GITHUB_RUN_ID": str(run_id),
        "GITHUB_RUN_ATTEMPT": "1",
        "RUNNER_OS": "Windows",
    }
    if any(platform.get(key) != value for key, value in expected.items()):
        msg = "fixture preparation platform binding mismatch"
        raise ValueError(msg)
    return request


def read_uploaded(path: Path, reference: ArtifactReference) -> bytes:
    """Verify a raw immutable upload selected by its current-DAG reference."""
    content = path.read_bytes()
    if (
        path.name != reference.payload_path
        or _digest(content) != reference.payload_digest
        or reference.artifact_digest != reference.payload_digest
    ):
        msg = "fixture input artifact binding mismatch"
        raise ValueError(msg)
    return content


def _archive_files(content: bytes) -> dict[str, bytes]:
    if len(content) > _ARCHIVE_LIMIT:
        msg = "fixture transport exceeds its byte bound"
        raise ValueError(msg)
    with zipfile.ZipFile(io.BytesIO(content)) as archive:
        entries = archive.infolist()
        if (
            len(entries) > _ENTRY_LIMIT
            or sum(item.file_size for item in entries) > _ARCHIVE_LIMIT
        ):
            msg = "fixture transport exceeds its entry/byte bound"
            raise ValueError(msg)
        files: dict[str, bytes] = {}
        names: set[str] = set()
        for entry in entries:
            if entry.is_dir():
                continue
            name = entry.filename
            relative = PurePosixPath(name)
            if (
                relative.is_absolute()
                or relative.as_posix() != name
                or ".." in relative.parts
                or "\\" in name
                or ":" in name
                or name.casefold() in names
                or stat.S_ISLNK(entry.external_attr >> 16)
            ):
                msg = "unsafe or duplicate fixture transport path"
                raise ValueError(msg)
            names.add(name.casefold())
            files[name] = archive.read(entry)
    return files


def write_archive(path: Path, files: Mapping[str, bytes]) -> None:
    """Write a new mechanical envelope without altering its contained bytes."""
    if path.exists():
        msg = "fixture output already exists"
        raise ValueError(msg)
    partial = path.with_suffix(path.suffix + ".partial")
    with zipfile.ZipFile(
        partial, "x", compression=zipfile.ZIP_DEFLATED
    ) as archive:
        for name, content in sorted(files.items()):
            archive.writestr(name, content)
    partial.rename(path)


def _retained_files(directory: Path) -> dict[str, bytes]:
    return {
        path.relative_to(directory).as_posix(): path.read_bytes()
        for path in sorted(directory.rglob("*"))
        if path.is_file() and not path.is_symlink()
    }


def materialize_helper(
    path: Path, reference: ArtifactReference, directory: Path
) -> dotnet_provider.NativeNuGetHelper:
    """Use the complete same-revision runtime from the unprivileged producer."""
    files = _archive_files(read_uploaded(path, reference))
    if not {
        _HELPER_DLL,
        "WorkflowDeliveryV3DotnetProvider.deps.json",
        "WorkflowDeliveryV3DotnetProvider.runtimeconfig.json",
    }.issubset(files):
        msg = "incomplete trusted NuGet helper runtime"
        raise ValueError(msg)
    directory.mkdir(parents=True, exist_ok=False)
    for name, content in files.items():
        destination = directory / name
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(content)
    return dotnet_provider.NativeNuGetHelper(directory / _HELPER_DLL)


def prepare_native_inputs(  # noqa: PLR0913, PLR0917
    request: NuGetPreparationRequest,
    source_root: Path,
    helper: dotnet_provider.NativeNuGetHelper,
    helper_reference: ArtifactReference,
    directory: Path,
    output: Path,
) -> None:
    """Acquire native facts and dependencies before offline fixture packing."""
    directory.mkdir(parents=True, exist_ok=False)
    manifest = request.provider_manifest
    dependencies = directory / "packages"
    evidence = directory / "native"
    result = dotnet_provider.provide_dotnet_repository_facts(
        source_root,
        compiler.provider_binding(manifest, "dotnet-nuget-slice"),
        CheckoutMaterialization(fetch_depth=0, credentials_persisted=False),
        helper=helper,
        evidence_directory=evidence,
        dependency_directory=dependencies,
    )
    files = {
        "request.json": canonicalize(request.to_document()),
        "provider.json": canonicalize(result.to_document()),
        **{
            "native/" + name: content
            for name, content in _retained_files(evidence).items()
        },
    }
    archives = sorted(dependencies.rglob("*.nupkg"))
    if not archives or len({path.name.casefold() for path in archives}) != len(
        archives
    ):
        msg = "native setup did not produce distinct dependencies"
        raise ValueError(msg)
    for path in archives:
        if path.is_symlink() or not path.resolve().is_relative_to(dependencies):
            msg = "native dependency archive escapes setup"
            raise ValueError(msg)
        files["dependencies/" + path.name] = path.read_bytes()
    files["setup.json"] = canonicalize(
        {
            "schema": SETUP_SCHEMA,
            "request-digest": request.request_digest,
            "provider-manifest-digest": manifest.manifest_digest,
            "provider-result-digest": result.result_digest,
            "helper": helper_reference.to_document(),
            "files": {
                name: _digest(content) for name, content in files.items()
            },
        }
    )
    write_archive(output, files)


def prepare_from_native_inputs(  # noqa: PLR0913, PLR0917
    request: NuGetPreparationRequest,
    request_reference: ArtifactReference,
    setup_path: Path,
    setup_reference: ArtifactReference,
    helper: dotnet_provider.NativeNuGetHelper,
    helper_reference: ArtifactReference,
    source_root: Path,
    directory: Path,
    output: Path,
) -> None:
    """Admit immutable native inputs, then retain a complete offline pair."""
    files = _archive_files(read_uploaded(setup_path, setup_reference))
    try:
        setup_bytes = files.pop("setup.json")
        _object(parse_canonical_json(setup_bytes))
        result = dotnet_provider.dotnet_provider_result_from_document(
            parse_canonical_json(files["provider.json"])
        )
    except KeyError as error:
        msg = "incomplete fixture setup transport"
        raise ValueError(msg) from error
    manifest = request.provider_manifest
    if (
        setup_bytes
        != canonicalize(
            {
                "schema": SETUP_SCHEMA,
                "request-digest": request.request_digest,
                "provider-manifest-digest": manifest.manifest_digest,
                "provider-result-digest": result.result_digest,
                "helper": helper_reference.to_document(),
                "files": {
                    name: _digest(content) for name, content in files.items()
                },
            }
        )
        or files.get("request.json") != canonicalize(request.to_document())
        or request_reference.payload_digest != request.request_digest
        or request_reference.artifact_digest != request.request_digest
    ):
        msg = "fixture setup request/native/helper binding mismatch"
        raise ValueError(msg)
    bundle = dotnet_provider.create_dotnet_provider_fact_bundle(
        result,
        manifest_digest=manifest.manifest_digest,
        manifest_entry_id="dotnet-nuget-slice",
        request_artifact_id=request_reference.artifact_id,
        request_artifact_digest=request_reference.artifact_digest,
        transport_id=setup_reference.artifact_id,
        transport_digest=setup_reference.artifact_digest,
    )
    admitted = compiler.admit_dotnet_provider_fact_bundle(
        bundle,
        context=request.context,
        manifest=manifest,
        admission=compiler.FactBundleAdmissionContext(
            request_reference.artifact_id,
            request_reference.artifact_digest,
            setup_reference.artifact_id,
            setup_reference.artifact_digest,
            bundle.bundle_digest,
        ),
    )
    snapshot = compiler.compile_dotnet_repository_model(
        source_root, request.context, manifest, [admitted]
    )
    model = compiler.admit_repository_model_snapshot(
        canonicalize(snapshot.to_document()),
        expected_context=request.context,
        expected_digest=snapshot.snapshot_digest,
    )
    witness = DotnetPackageTargetWitness(
        target=request.target,
        release_unit=dotnet_provider.DOTNET_RELEASE_UNIT,
        nbgv=result.nbgv,
        build_definition=model.snapshot.release_units[0].builds[0].definition,
        catalog_digest=request.context.catalog_digest,
        control_digest=canonical_sha256(
            {
                "schema": "workflow-delivery/v3/control-identity",
                "identity": request.context.control,
            }
        ),
        purpose="destination-acceptance",
    )
    directory.mkdir(parents=True, exist_ok=False)
    dependencies = directory / "dependencies"
    dependencies.mkdir()
    for name, content in files.items():
        if name.startswith("dependencies/"):
            relative = PurePosixPath(name)
            if (
                relative.parent.as_posix() != "dependencies"
                or relative.suffix != ".nupkg"
            ):
                msg = "invalid setup dependency path"
                raise ValueError(msg)
            (dependencies / relative.name).write_bytes(content)
    build = DotnetBuildRequest(
        source_root,
        tuple(path for path, _ in result.source_input_manifest),
        result.source_input_manifest,
        witness,
        helper,
        directory / "pair",
    )
    preparation: dict[str, JsonValue] = {
        "schema": COMPLETION_SCHEMA,
        "request": request.to_document(),
        "request-artifact": request_reference.to_document(),
        "setup-artifact": setup_reference.to_document(),
        "helper-artifact": helper_reference.to_document(),
        "provider-bundle": bundle.to_document(),
        "repository-model": model.snapshot.to_document(),
        "witness": witness.to_document(),
    }
    # Retain the closed Build request even if packing or consumer checks fail.
    (directory / "preparation-request.json").write_bytes(
        canonicalize(preparation)
    )
    pair = prepare_nuget_fixture_pair(
        NuGetFixtureRequest(
            build,
            tuple(sorted(dependencies.glob("*.nupkg"))),
            request.generation,
        )
    )
    outputs = _retained_files(directory)
    outputs["original/" + pair.original.basename] = pair.original.content
    outputs["comparison/" + pair.comparison.basename] = pair.comparison.content
    preparation["inspection"] = pair.inspection.to_document()
    preparation["files"] = {
        name: _digest(content) for name, content in outputs.items()
    }
    outputs["preparation.json"] = canonicalize(preparation)
    write_archive(output, outputs)


def _reference(arguments: argparse.Namespace, role: str) -> ArtifactReference:
    path = Path(getattr(arguments, role))
    digest = getattr(arguments, role + "_digest")
    if not digest.startswith("sha256:"):
        digest = "sha256:" + digest
    return ArtifactReference(
        getattr(arguments, role + "_id"),
        digest,
        getattr(arguments, role + "_url"),
        path.name,
        digest,
    )


def main(argv: Sequence[str] | None = None) -> int:
    """Expose only request, setup and offline preparation commands."""
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    request_parser = commands.add_parser("request")
    request_parser.add_argument("--tooling-sha", required=True)
    request_parser.add_argument("--target", required=True)
    request_parser.add_argument("--generation", required=True)
    request_parser.add_argument("--output", required=True)
    for name in ("setup", "prepare"):
        child = commands.add_parser(name)
        for role in ("request", "helper") + (
            ("setup",) if name == "prepare" else ()
        ):
            child.add_argument("--" + role, required=True)
            child.add_argument("--" + role + "-id", required=True, type=int)
            child.add_argument("--" + role + "-digest", required=True)
            child.add_argument("--" + role + "-url", required=True)
        child.add_argument("--source-root", required=True)
        child.add_argument("--directory", required=True)
        child.add_argument("--output", required=True)
    arguments = parser.parse_args(argv)
    output = Path(arguments.output).resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    if output.exists():
        msg = "fixture output already exists"
        raise ValueError(msg)
    if arguments.command == "request":
        request = NuGetPreparationRequest(
            arguments.tooling_sha,
            arguments.target,
            arguments.generation,
            int(os.environ["GITHUB_RUN_ID"]),
        )
        content = canonicalize(request.to_document())
        admit_preparation_request(content, os.environ)
        output.write_bytes(content)
        return 0
    request_reference = _reference(arguments, "request")
    request = admit_preparation_request(
        read_uploaded(Path(arguments.request), request_reference), os.environ
    )
    directory = Path(arguments.directory).resolve()
    helper_reference = _reference(arguments, "helper")
    helper = materialize_helper(
        Path(arguments.helper), helper_reference, directory / "helper"
    )
    if arguments.command == "setup":
        prepare_native_inputs(
            request,
            Path(arguments.source_root).resolve(),
            helper,
            helper_reference,
            directory / "setup",
            output,
        )
    else:
        prepare_from_native_inputs(
            request,
            request_reference,
            Path(arguments.setup),
            _reference(arguments, "setup"),
            helper,
            helper_reference,
            Path(arguments.source_root).resolve(),
            directory / "preparation",
            output,
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
