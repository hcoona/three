"""Native NuGet package, frozen build, and clean consumer scenarios."""

# Trusted test fixtures invoke pinned SDK and Git without a shell.
# ruff: noqa: S603, S607

import hashlib
import io
import json
import shutil
import subprocess
import zipfile
from pathlib import Path

import pytest
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
        ("git", "checkout", "--quiet", "--detach"), cwd=source, check=True
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
