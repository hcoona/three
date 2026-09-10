"""Native NuGet package, frozen build, and clean consumer scenarios."""

# Trusted test fixtures invoke pinned SDK and Git without a shell.
# ruff: noqa: S603, S607

import hashlib
import io
import json
import os
import shutil
import subprocess
import zipfile
from pathlib import Path

import pytest
from three_workflow_delivery_v3.adapters import dotnet as dotnet_adapter
from three_workflow_delivery_v3.adapters.dotnet import (
    BUILD_DEFINITION,
    DotnetBuildRequest,
    DotnetBuildResult,
    DotnetPackageTargetWitness,
    build_dotnet_package,
    dotnet_package_target_witness_from_document,
    qualify_nuget_artifact_contents,
    qualify_nuget_restore_build_invoke,
)
from three_workflow_delivery_v3.canonical import parse_canonical_json
from three_workflow_delivery_v3.catalogs import catalog_digest
from three_workflow_delivery_v3.repository import compiler, dotnet_provider
from three_workflow_delivery_v3.repository.dotnet_provider import (
    DOTNET_PROJECT_ROOT,
    DOTNET_RELEASE_UNIT,
    NativeNuGetHelper,
    provide_dotnet_repository_facts,
)
from three_workflow_delivery_v3.repository.node_provider import (
    CheckoutMaterialization,
    ProviderBinding,
)


@pytest.fixture(scope="module")
def native_helper(
    tmp_path_factory: pytest.TempPathFactory,
) -> NativeNuGetHelper:
    """Build trusted test tooling once, outside any publication operation."""
    root = Path(__file__).resolve().parents[6]
    project = (
        root
        / "src/private/app/workflow-delivery-v3-dotnet-provider"
        / "WorkflowDeliveryV3DotnetProvider.csproj"
    )
    evidence = tmp_path_factory.mktemp("dotnet-helper")
    subprocess.run(
        (
            "dotnet",
            "build",
            str(project),
            "--configuration",
            "Release",
            f"-bl:{evidence / 'helper.binlog'}",
        ),
        cwd=root,
        check=True,
        capture_output=True,
        text=True,
    )
    return NativeNuGetHelper(
        project.parent
        / "bin/Release/net10.0/WorkflowDeliveryV3DotnetProvider.dll"
    )


@pytest.mark.parametrize(
    ("left_id", "left_version", "right_id", "right_version", "normalized"),
    [
        (
            "Example.Smoke",
            "01.2.3+first",
            "example.smoke",
            "1.2.3+second",
            "1.2.3",
        ),
        (
            "EXAMPLE.SMOKE",
            "1.2.3-BETA",
            "example.smoke",
            "1.2.3-beta",
            "1.2.3-beta",
        ),
        ("Example.Smoke", "1.2.3.0", "example.smoke", "1.2.3", "1.2.3"),
    ],
)
def test_official_native_identity_equivalence(  # noqa: PLR0913, PLR0917
    native_helper: NativeNuGetHelper,
    left_id: str,
    left_version: str,
    right_id: str,
    right_version: str,
    normalized: str,
) -> None:
    """NuGet equivalence produces one key while retaining display values."""
    left = native_helper.normalize_identity(left_id, left_version)
    right = native_helper.normalize_identity(right_id, right_version)
    assert (
        left["normalizedPackageId"]
        == right["normalizedPackageId"]
        == "example.smoke"
    )
    assert left["normalizedVersion"] == right["normalizedVersion"] == normalized
    assert left["displayPackageId"] == left_id
    assert left["displayVersion"] == left_version


def test_official_service_resources_require_exact_supported_resources(
    native_helper: NativeNuGetHelper,
) -> None:
    """Official resource interpretation closes discovery before any mutation."""
    resources = [
        {
            "@id": "https://nuget.pkg.github.com/hcoona/download/",
            "@type": "PackageBaseAddress/3.0.0",
        },
        {
            "@id": "https://nuget.pkg.github.com/hcoona/",
            "@type": "PackagePublish/2.0.0",
        },
    ]

    def payload(items: list[dict[str, str]]) -> bytes:
        return json.dumps({"version": "3.0.0", "resources": items}).encode()

    assert native_helper.service_resources(payload(resources)) == {
        "packageBaseAddress": resources[0]["@id"],
        "packagePublish": resources[1]["@id"],
    }
    with pytest.raises(ValueError, match="native command failed"):
        native_helper.service_resources(payload(resources[:1]))
    conflict = {
        "@id": "https://other.invalid/upload",
        "@type": "PackagePublish/2.0.0",
    }
    with pytest.raises(ValueError, match="native command failed"):
        native_helper.service_resources(payload([*resources, conflict]))


@pytest.fixture(scope="module")
def frozen_package(
    native_helper: NativeNuGetHelper,
    tmp_path_factory: pytest.TempPathFactory,
) -> tuple[DotnetBuildResult, Path]:
    """Evaluate a disposable exact target with the proposed product."""
    repo = Path(__file__).resolve().parents[6]
    root = tmp_path_factory.mktemp("dotnet-native")
    source = root / "source"
    subprocess.run(
        ("git", "clone", "--quiet", "--no-local", str(repo), str(source)),
        env={**os.environ, "GIT_LFS_SKIP_SMUDGE": "1"},
        check=True,
    )
    for path in (repo / DOTNET_PROJECT_ROOT).iterdir():
        if path.is_file():
            destination = source / DOTNET_PROJECT_ROOT / path.name
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(path, destination)
    subprocess.run(("git", "add", DOTNET_PROJECT_ROOT), cwd=source, check=True)
    subprocess.run(
        (
            "git",
            "-c",
            "user.name=Native Test",
            "-c",
            "user.email=native-test@example.invalid",
            "commit",
            "--quiet",
            "--no-verify",
            "--allow-empty",
            "-m",
            "Exercise the proposed native smoke target",
        ),
        cwd=source,
        check=True,
    )
    subprocess.run(
        ("git", "checkout", "--quiet", "--detach"),
        cwd=source,
        env={**os.environ, "GIT_LFS_SKIP_SMUDGE": "1"},
        check=True,
    )
    target = subprocess.check_output(
        ("git", "rev-parse", "HEAD"), cwd=source, text=True
    ).strip()
    binding = ProviderBinding(
        "native-test",
        "release-simulation",
        1,
        1,
        target,
        "dotnet-provider",
        target,
        catalog_digest(),
        "sha256:" + "c" * 64,
    )
    provider = provide_dotnet_repository_facts(
        source,
        binding,
        CheckoutMaterialization(0, credentials_persisted=False),
        helper=native_helper,
        evidence_directory=root / "provider",
    )
    assert provider.checkout.head == target
    assert provider.checkout.ancestry_complete is True
    assert provider.checkout.tags_complete is True
    assert provider.checkout.credentials_persisted is False
    facts = provider.nbgv
    inputs = provider.source_input_manifest
    witness = DotnetPackageTargetWitness(
        facts.git_commit_id,
        DOTNET_RELEASE_UNIT,
        facts,
        BUILD_DEFINITION,
        "sha256:" + "a" * 64,
        "sha256:" + "b" * 64,
        "release-simulation",
    )
    result = build_dotnet_package(
        DotnetBuildRequest(
            source,
            tuple(path for path, _ in inputs),
            inputs,
            witness,
            native_helper,
            root / "build",
        )
    )
    return result, root


def test_frozen_native_package_and_clean_consumer(
    native_helper: NativeNuGetHelper,
    frozen_package: tuple[DotnetBuildResult, Path],
) -> None:
    """The package consumes frozen versions without NBGV recomputation."""
    result, root = frozen_package
    assert result.manifest.basename.endswith(".nupkg")
    assert result.manifest.byte_size == len(result.package)
    assert (
        result.manifest.sha256
        == "sha256:" + hashlib.sha256(result.package).hexdigest()
    )
    for operation in ("restore", "build", "pack"):
        audit = json.loads(
            (root / "build" / f"{operation}-audit.json").read_text()
        )
        assert audit["completed"] is True
        assert audit["succeeded"] is True
        assert audit["nbgvExecuted"] is False
        assert all(
            "nerdbank.gitversioning" not in path.lower()
            for path in audit["imports"]
        )
    provider = native_helper.audit_binlog(root / "provider/provider.binlog")
    assert provider["nbgvExecuted"] is True
    consumer = qualify_nuget_restore_build_invoke(
        result.package,
        result.expectation,
        native_helper,
        evidence_directory=root / "consumer",
    )
    assert consumer.project_id == DOTNET_RELEASE_UNIT
    assert consumer.package_sha256 == result.manifest.sha256
    assert (
        consumer.witness_sha256
        == "sha256:" + hashlib.sha256(result.witness).hexdigest()
    )
    assert consumer.normalized_version == result.expectation.normalized_version


@pytest.mark.parametrize(
    "substitution", ["witness", "extra-entry", "duplicate-entry"]
)
def test_nuget_contents_reject_witness_and_entry_substitution(
    native_helper: NativeNuGetHelper,
    frozen_package: tuple[DotnetBuildResult, Path],
    substitution: str,
) -> None:
    """Content evidence cannot be replaced by marker behavior alone."""
    result, _ = frozen_package
    buffer = io.BytesIO()
    with (
        zipfile.ZipFile(io.BytesIO(result.package)) as original,
        zipfile.ZipFile(buffer, "w") as changed,
    ):
        for entry in original.infolist():
            content = original.read(entry)
            if (
                substitution == "witness"
                and entry.filename == "workflow-delivery/provenance.json"
            ):
                content = b"{}"
            changed.writestr(entry, content)
        if substitution == "extra-entry":
            changed.writestr("tools/undeclared.ps1", "exit 0")
        if substitution == "duplicate-entry":
            changed.writestr(
                "WORKFLOW-DELIVERY/PROVENANCE.JSON", result.witness
            )
    with pytest.raises(
        ValueError, match=r"witness|entry closure|native command failed"
    ):
        qualify_nuget_artifact_contents(
            buffer.getvalue(), result.expectation, native_helper
        )


@pytest.mark.parametrize(
    ("package_id", "version"),
    [("invalid/package", "1.2.3"), ("Example.Smoke", "1.2.3-beta..1")],
)
def test_official_identity_rejects_invalid_native_coordinates(
    native_helper: NativeNuGetHelper,
    package_id: str,
    version: str,
) -> None:
    """Malformed coordinates never acquire normalized resource identities."""
    with pytest.raises(ValueError, match="native command failed"):
        native_helper.normalize_identity(package_id, version)


def test_frozen_build_rejects_missing_native_inputs(
    native_helper: NativeNuGetHelper,
    frozen_package: tuple[DotnetBuildResult, Path],
    tmp_path: Path,
) -> None:
    """Frozen mode rejects missing native version inputs."""
    _, root = frozen_package
    source = root / "source"
    project = source / DOTNET_PROJECT_ROOT / f"{DOTNET_RELEASE_UNIT}.csproj"
    log = tmp_path / "missing-frozen-inputs.binlog"
    completed = subprocess.run(
        (
            "dotnet",
            "restore",
            str(project),
            "-property:WorkflowDeliveryFrozenBuild=true",
            f"-property:BaseIntermediateOutputPath={tmp_path / 'obj'}/",
            f"-property:MSBuildProjectExtensionsPath={tmp_path / 'obj'}/",
            f"-bl:{log}",
        ),
        cwd=source,
        check=False,
        capture_output=True,
        text=True,
    )
    assert completed.returncode != 0
    audit = native_helper.audit_binlog(log)
    assert audit["succeeded"] is False
    assert audit["errors"] == [
        "Frozen Workflow Delivery version inputs are required."
    ]


def test_build_rejects_substituted_source_before_native_execution(
    native_helper: NativeNuGetHelper,
    frozen_package: tuple[DotnetBuildResult, Path],
    tmp_path: Path,
) -> None:
    """A changed source file cannot use an admitted Provider manifest."""
    result, root = frozen_package
    source = root / "source"
    for path, _ in result.source_input_manifest:
        destination = tmp_path / "source" / path
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source / path, destination)
    changed = tmp_path / "source" / DOTNET_PROJECT_ROOT / "Smoke.cs"
    changed.write_text("substituted product source", encoding="utf-8")
    evidence = tmp_path / "forbidden-evidence"
    request = DotnetBuildRequest(
        tmp_path / "source",
        tuple(path for path, _ in result.source_input_manifest),
        result.source_input_manifest,
        dotnet_package_target_witness_from_document(
            parse_canonical_json(result.witness)
        ),
        native_helper,
        evidence,
    )
    with pytest.raises(ValueError, match="source input digest mismatch"):
        build_dotnet_package(request)
    assert not evidence.exists()


def _git_bytes(repo: Path, *arguments: str) -> bytes:
    return subprocess.run(
        ("git", *arguments),
        cwd=repo,
        env={**os.environ, "GIT_LFS_SKIP_SMUDGE": "1"},
        check=True,
        capture_output=True,
    ).stdout


def _autocrlf_source(scratch: Path) -> Path:
    repo = Path(__file__).resolve().parents[6]
    seed = scratch / "seed"
    _git_bytes(scratch, "clone", "--quiet", "--no-local", str(repo), str(seed))
    shutil.copyfile(repo / ".gitattributes", seed / ".gitattributes")
    (seed / "autocrlf-control.txt").write_bytes(b"unselected\ncontrol\n")
    _git_bytes(seed, "add", ".gitattributes", "autocrlf-control.txt")
    _git_bytes(
        seed,
        "-c",
        "user.name=Native Test",
        "-c",
        "user.email=native-test@example.invalid",
        "commit",
        "--quiet",
        "--no-verify",
        "-m",
        "Exercise LF source policy",
    )
    source = scratch / "source"
    _git_bytes(
        scratch, "clone", "--quiet", "--no-local", str(seed), str(source)
    )
    return source


def _assert_canonical_inputs(root: Path, blobs: dict[str, bytes]) -> None:
    for path, content in blobs.items():
        assert b"\r\n" not in content, path
        assert (root / path).read_bytes() == content, path


def _compile_native_provider(
    source: Path,
    context: compiler.CompilationContext,
    manifest: compiler.ProviderRequestManifest,
    provider: dotnet_provider.DotnetProviderResult,
) -> compiler.RepositoryModelSnapshot:
    request_digest = "sha256:" + "a" * 64
    transport_digest = "sha256:" + "b" * 64
    bundle = dotnet_provider.create_dotnet_provider_fact_bundle(
        provider,
        manifest_digest=manifest.manifest_digest,
        manifest_entry_id=manifest.requests[0].entry_id,
        request_artifact_id=101,
        request_artifact_digest=request_digest,
        transport_id=202,
        transport_digest=transport_digest,
    )
    admission = compiler.FactBundleAdmissionContext(
        request_artifact_id=101,
        request_artifact_digest=request_digest,
        transport_id=202,
        transport_digest=transport_digest,
        bundle_digest=bundle.bundle_digest,
    )
    admitted = compiler.admit_dotnet_provider_fact_bundle(
        bundle, context=context, manifest=manifest, admission=admission
    )
    return compiler.compile_dotnet_repository_model(
        source, context, manifest, [admitted]
    )


def test_autocrlf_preserves_provider_compiler_and_build_source_bytes(
    native_helper: NativeNuGetHelper,
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Global Windows conversion preserves the exact evaluated/built blobs."""
    configuration = tmp_path / "global.gitconfig"
    configuration.write_bytes(b"[core]\n\tautocrlf = true\n")
    monkeypatch.setenv("GIT_CONFIG_GLOBAL", str(configuration))
    monkeypatch.setenv("GIT_CONFIG_NOSYSTEM", "1")
    source = _autocrlf_source(tmp_path)
    target = _git_bytes(source, "rev-parse", "HEAD").decode().strip()
    blobs = {
        path: _git_bytes(source, "show", f"{target}:{path}")
        for path in dotnet_provider.dotnet_provider_input_paths(source)
    }
    _assert_canonical_inputs(source, blobs)
    context = compiler.CompilationContext(
        request_id="autocrlf-native-test",
        purpose="release-simulation",
        workflow_run_id=1,
        run_attempt=1,
        target=target,
        producer="compile-native-test",
        control=f"workflow-delivery-v3:{target}",
        catalog_digest=catalog_digest(),
        channel="buddy",
        release_unit=DOTNET_RELEASE_UNIT,
    )
    manifest = compiler.nuget_provider_manifest(
        context, provider_producer="discover-dotnet"
    )
    evaluate = dotnet_provider.evaluate_dotnet_project
    evaluated: list[Path] = []

    def inspect_evaluation(
        root: Path, helper: NativeNuGetHelper, evidence: Path
    ) -> tuple[
        dotnet_provider.DotnetProjectNode, dotnet_provider.DotnetNbgvFacts, str
    ]:
        assert (
            _git_bytes(root, "config", "--get", "core.autocrlf").strip()
            == b"true"
        )
        assert (
            _git_bytes(
                root, "config", "--global", "--get", "core.autocrlf"
            ).strip()
            == b"true"
        )
        assert (
            root / "autocrlf-control.txt"
        ).read_bytes() == b"unselected\r\ncontrol\r\n"
        _assert_canonical_inputs(root, blobs)
        evaluated.append(root)
        return evaluate(root, helper, evidence)

    monkeypatch.setattr(
        dotnet_provider, "evaluate_dotnet_project", inspect_evaluation
    )
    provider = provide_dotnet_repository_facts(
        source,
        compiler.provider_binding(manifest, "dotnet-nuget-slice"),
        CheckoutMaterialization(0, credentials_persisted=False),
        helper=native_helper,
        evidence_directory=tmp_path / "provider",
    )
    assert len(evaluated) == 1
    assert evaluated[0] != source
    snapshot = _compile_native_provider(source, context, manifest, provider)
    assert snapshot.ready is True
    assert snapshot.nbgv == provider.nbgv
    _assert_canonical_inputs(source, blobs)
    execute = dotnet_adapter.run_native
    built: list[str] = []

    def inspect_build(
        command: tuple[str, ...], root: Path, environment: dict[str, str]
    ) -> str:
        if command[1] in {"restore", "build", "pack"}:
            assert root != source
            _assert_canonical_inputs(root, blobs)
            built.append(command[1])
        return execute(command, root, environment)

    monkeypatch.setattr(dotnet_adapter, "run_native", inspect_build)
    witness = DotnetPackageTargetWitness(
        target,
        DOTNET_RELEASE_UNIT,
        provider.nbgv,
        BUILD_DEFINITION,
        context.catalog_digest,
        "sha256:" + "c" * 64,
        context.purpose,
    )
    result = build_dotnet_package(
        DotnetBuildRequest(
            source,
            tuple(blobs),
            provider.source_input_manifest,
            witness,
            native_helper,
            tmp_path / "build",
        )
    )
    assert built == ["restore", "build", "pack"]
    assert result.source_input_manifest == provider.source_input_manifest
    assert (
        result.manifest.sha256
        == "sha256:" + hashlib.sha256(result.package).hexdigest()
    )
