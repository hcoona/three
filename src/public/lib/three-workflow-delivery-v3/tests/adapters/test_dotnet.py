"""Native NuGet package, frozen build, and clean consumer scenarios."""

# Trusted test fixtures invoke pinned SDK and Git without a shell.
# ruff: noqa: S603, S607

import hashlib
import io
import json
import os
import re
import shutil
import subprocess
import zipfile
from dataclasses import replace
from pathlib import Path

import pytest
from three_workflow_delivery_v3.acceptance import nuget_fixture
from three_workflow_delivery_v3.acceptance.nuget_fixture import (
    NuGetFixturePair,
    NuGetFixtureRequest,
    inspect_nuget_fixture_pair,
    prepare_nuget_fixture_pair,
)
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


def test_frozen_native_package_and_clean_consumer(
    native_helper: NativeNuGetHelper,
    frozen_package: tuple[DotnetBuildResult, Path],
    tmp_path: Path,
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
        evidence_directory=tmp_path / "consumer",
    )
    assert consumer.project_id == DOTNET_RELEASE_UNIT
    assert consumer.package_sha256 == result.manifest.sha256
    assert (
        consumer.witness_sha256
        == "sha256:" + hashlib.sha256(result.witness).hexdigest()
    )
    assert consumer.normalized_version == result.expectation.normalized_version


def test_native_provider_captures_offline_dependency_inputs(
    frozen_package: tuple[DotnetBuildResult, Path],
) -> None:
    """The native locked setup retains packages in its explicit fresh cache."""
    _, root = frozen_package
    assets = json.loads((root / "provider/project.assets.json").read_bytes())
    cache = root / "provider-packages"
    assert Path(assets["project"]["restore"]["packagesPath"]) == cache
    libraries = {
        name: item
        for name, item in assets["libraries"].items()
        if item["type"] == "package"
    }
    archives = tuple(cache.rglob("*.nupkg"))
    assert len(archives) == len(libraries)
    assert libraries
    for name, item in libraries.items():
        package, version = name.lower().split("/")
        archive = cache / item["path"] / f"{package}.{version}.nupkg"
        assert archive.is_file()
        assert zipfile.is_zipfile(archive)
    command = json.loads(
        (root / "provider/evaluation/command.json").read_bytes()
    )
    assert "-property:RestoreLockedMode=true" in command["argv"]
    assert "-property:RestorePackagesPath=" + str(cache) in command["argv"]
    assert (root / "provider/evaluation/exit-code.txt").read_text() == "0"


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
    package, root = frozen_package
    source = tmp_path / "source"
    for path, _ in package.source_input_manifest:
        destination = source / path
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(root / "source" / path, destination)
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
    assert any(
        all(
            re.search(pattern, error, flags=re.IGNORECASE)
            for pattern in (
                r"\bfrozen\b",
                r"\bversion\W+inputs\b",
                r"\brequired\b",
            )
        )
        for error in audit["errors"]
    )


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
    attributes = [
        path
        for path in _git_bytes(repo, "ls-files", "-z").decode().split("\0")
        if Path(path).name == ".gitattributes"
    ]
    for path in attributes:
        destination = seed / path
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(repo / path, destination)
    shutil.copyfile(repo / "global.json", seed / "global.json")
    (seed / "autocrlf-control.txt").write_bytes(b"unselected\ncontrol\n")
    _git_bytes(seed, "add", *attributes, "global.json", "autocrlf-control.txt")
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
    """Windows conversion preserves admitted input and adapter source bytes."""
    configuration = tmp_path / "global.gitconfig"
    configuration.write_bytes(b"[core]\n\tautocrlf = true\n")
    monkeypatch.setenv("GIT_CONFIG_GLOBAL", str(configuration))
    monkeypatch.setenv("GIT_CONFIG_NOSYSTEM", "1")
    source = _autocrlf_source(tmp_path)
    target = _git_bytes(source, "rev-parse", "HEAD").decode().strip()
    provider_paths = dotnet_provider.dotnet_provider_input_paths(source)
    adapter_path = (
        "src/public/lib/three-workflow-delivery-v3/src/"
        "three_workflow_delivery_v3/adapters/nuget_github_packages.py"
    )
    provider_blobs = {
        path: _git_bytes(source, "show", f"{target}:{path}")
        for path in provider_paths
    }
    blobs = {
        **provider_blobs,
        adapter_path: _git_bytes(source, "show", f"{target}:{adapter_path}"),
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
            _assert_canonical_inputs(root, provider_blobs)
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
            provider_paths,
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


@pytest.fixture(scope="module")
def fixture_request(
    frozen_package: tuple[DotnetBuildResult, Path],
    native_helper: NativeNuGetHelper,
    tmp_path_factory: pytest.TempPathFactory,
) -> NuGetFixtureRequest:
    """Reuse admitted native inputs and existing local dependency archives."""
    package, root = frozen_package
    witness = replace(
        dotnet_package_target_witness_from_document(
            parse_canonical_json(package.witness)
        ),
        purpose="destination-acceptance",
    )
    source = root / "source"
    archives = tuple(sorted((root / "provider-packages").rglob("*.nupkg")))
    assert archives
    assert all(archive.is_file() for archive in archives)
    return NuGetFixtureRequest(
        DotnetBuildRequest(
            source,
            tuple(path for path, _ in package.source_input_manifest),
            package.source_input_manifest,
            witness,
            native_helper,
            tmp_path_factory.mktemp("paired-fixture") / "evidence",
        ),
        archives,
        "local-sdk-scenario",
    )


@pytest.fixture(scope="module")
def paired_fixture(fixture_request: NuGetFixtureRequest) -> NuGetFixturePair:
    """Exercise one local SDK pair; this is no destination or Windows claim."""
    return prepare_nuget_fixture_pair(fixture_request)


def test_native_nuget_fixture_pair_preserves_original_a(
    fixture_request: NuGetFixtureRequest, paired_fixture: NuGetFixturePair
) -> None:
    """A remains its original pack output and passes a clean local consumer."""
    evidence = fixture_request.build.evidence_directory
    for label, output, manifest in (
        ("pack", paired_fixture.original, paired_fixture.inspection.original),
        (
            "pack-comparison",
            paired_fixture.comparison,
            paired_fixture.inspection.comparison,
        ),
    ):
        assert (
            evidence / label / output.basename
        ).read_bytes() == output.content
        assert manifest.byte_size == len(output.content)
        assert (
            manifest.sha256
            == "sha256:" + hashlib.sha256(output.content).hexdigest()
        )
        assert (
            manifest.sha512
            == "sha512:" + hashlib.sha512(output.content).hexdigest()
        )
        with zipfile.ZipFile(io.BytesIO(output.content)) as archive:
            assert (
                archive.read(dotnet_adapter.WITNESS_PATH)
                == fixture_request.build.witness.canonical_bytes
            )
    assert paired_fixture.consumer.project_id == DOTNET_RELEASE_UNIT
    assert (
        paired_fixture.consumer.package_sha256
        == paired_fixture.inspection.original.sha256
    )
    retained = parse_canonical_json((evidence / "fixtures.json").read_bytes())
    assert retained["inspection"] == paired_fixture.inspection.to_document()
    assert (
        retained["consumer"]["packageSha256"]
        == paired_fixture.consumer.package_sha256
    )


def test_native_nuget_fixture_pair_uses_equivalent_metadata(
    fixture_request: NuGetFixtureRequest, paired_fixture: NuGetFixturePair
) -> None:
    """Real NuGet packing retains equivalent displays and identical payloads."""
    inspection = paired_fixture.inspection
    for key in ("normalizedPackageId", "normalizedVersion"):
        assert (
            inspection.original_identity[key]
            == inspection.comparison_identity[key]
        )
    for key in ("displayPackageId", "displayVersion"):
        assert (
            inspection.original_identity[key]
            != inspection.comparison_identity[key]
        )
    assert paired_fixture.original.content != paired_fixture.comparison.content
    evidence = fixture_request.build.evidence_directory
    for operation in ("restore", "build", "pack", "pack-comparison"):
        audit = json.loads((evidence / f"{operation}-audit.json").read_bytes())
        assert audit["completed"] is True
        assert audit["succeeded"] is True
        assert audit["nbgvExecuted"] is False
        if operation.startswith("pack"):
            assert "Csc" not in audit["tasks"]
        assert (evidence / f"{operation}.binlog").stat().st_size > 0
        assert (evidence / operation / "exit-code.txt").read_text() == "0"
    with (
        zipfile.ZipFile(io.BytesIO(paired_fixture.original.content)) as left,
        zipfile.ZipFile(io.BytesIO(paired_fixture.comparison.content)) as right,
    ):
        for name in inspection.original.entries:
            if name.startswith("lib/") or name in {
                "README.md",
                dotnet_adapter.WITNESS_PATH,
            }:
                assert left.read(name) == right.read(name)
    expectation = replace(
        _fixture_expectation(fixture_request, paired_fixture),
        package_name=fixture_request.comparison_metadata.package_id,
        nuget_package_version=fixture_request.comparison_metadata.version,
    )
    with pytest.raises(
        ValueError, match="unsupported NuGet artifact expectation"
    ):
        qualify_nuget_artifact_contents(
            paired_fixture.comparison.content,
            expectation,
            fixture_request.build.helper,
        )


def _fixture_expectation(
    request: NuGetFixtureRequest, pair: NuGetFixturePair
) -> dotnet_adapter.DotnetArtifactExpectation:
    return dotnet_adapter.DotnetArtifactExpectation(
        dotnet_provider.DOTNET_PACKAGE,
        request.build.witness.nbgv.nuget_package_version,
        str(pair.inspection.original_identity["normalizedPackageId"]),
        str(pair.inspection.original_identity["normalizedVersion"]),
        request.build.witness.canonical_bytes,
    )


def test_nuget_fixture_preparation_uses_offline_sources(
    fixture_request: NuGetFixtureRequest,
    paired_fixture: NuGetFixturePair,
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Fresh caches and local sources cannot hide a missing dependency."""
    assert paired_fixture.consumer.project_id == DOTNET_RELEASE_UNIT
    evidence = fixture_request.build.evidence_directory
    inputs = json.loads((evidence / "inputs.json").read_bytes())
    assert set(inputs["dependencyArchives"]) == {
        archive.name for archive in fixture_request.dependency_archives
    }
    assert "-property:NuGetAudit=false" in inputs["properties"]
    assert "-property:RestoreLockedMode=true" in inputs["properties"]
    config = (evidence / "offline.nuget.config").read_text()
    assert "https:" not in config
    assert "<auditSources><clear/></auditSources>" in config
    restore = json.loads((evidence / "restore/command.json").read_bytes())
    assert restore["argv"][restore["argv"].index("--configfile") + 1].endswith(
        "offline.nuget.config"
    )
    assert inputs["environment"]["NUGET_PACKAGES"] != str(
        fixture_request.dependency_archives[0].parents[1]
    )
    monkeypatch.setenv("GITHUB_TOKEN", "must-not-reach-fixture")
    execute = dotnet_adapter.run_native

    def guarded(command, cwd, environment, *, diagnostics=None):
        assert "GITHUB_TOKEN" not in environment
        return execute(command, cwd, environment, diagnostics=diagnostics)

    monkeypatch.setattr(dotnet_adapter, "run_native", guarded)
    missing = replace(
        fixture_request,
        dependency_archives=(),
        build=replace(
            fixture_request.build, evidence_directory=tmp_path / "missing"
        ),
    )
    with pytest.raises(
        ValueError, match="native command failed: dotnet restore"
    ):
        prepare_nuget_fixture_pair(missing)
    assert not (missing.build.evidence_directory / "fixtures.json").exists()
    assert (
        missing.build.evidence_directory / "restore/exit-code.txt"
    ).read_text() != "0"
    diagnostics = (
        missing.build.evidence_directory / "restore/stdout.txt"
    ).read_text()
    assert "NU1101" in diagnostics


@pytest.mark.parametrize(
    "substitution", ["source", "manifest", "purpose", "target", "dependency"]
)
def test_nuget_fixture_rejects_mismatched_frozen_inputs(
    fixture_request: NuGetFixtureRequest,
    substitution: str,
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Invalid frozen inputs stop before native execution or evidence."""
    build = replace(
        fixture_request.build, evidence_directory=tmp_path / "rejected"
    )
    request = replace(fixture_request, build=build)
    if substitution == "source":
        first, *rest = build.source_input_manifest
        build = replace(
            build,
            source_input_manifest=((first[0], "sha256:" + "0" * 64), *rest),
        )
    elif substitution == "manifest":
        build = replace(build, declared_inputs=())
    elif substitution == "purpose":
        build = replace(
            build, witness=replace(build.witness, purpose="live-release")
        )
    elif substitution == "target":
        build = replace(build, witness=replace(build.witness, target="0" * 40))
    else:
        request = replace(
            request, dependency_archives=(tmp_path / "missing.nupkg",)
        )
    request = replace(request, build=build)

    def forbidden(*_args, **_kwargs):
        pytest.fail("invalid frozen fixture input reached native execution")

    monkeypatch.setattr(dotnet_adapter, "run_native", forbidden)
    expected = {
        "source": "source input digest mismatch",
        "manifest": "declared inputs disagree",
        "purpose": "requires offline destination-acceptance",
        "target": "target binding mismatch",
        "dependency": "distinct local nupkg",
    }
    with pytest.raises(ValueError, match=expected[substitution]):
        prepare_nuget_fixture_pair(request)
    assert not build.evidence_directory.exists()


@pytest.mark.parametrize(
    "substitution",
    [
        "readme",
        "witness",
        "entry",
        "version",
        "description",
        "assembly",
        "metadata",
        "relationships",
        "relationships-nuspec-target",
        "relationships-core-target",
        "core",
        "content-types",
    ],
)
def test_nuget_fixture_inspection_rejects_substitution(  # noqa: C901
    fixture_request: NuGetFixtureRequest,
    paired_fixture: NuGetFixturePair,
    substitution: str,
) -> None:
    """Changed content cannot be used as the admitted comparison candidate."""
    altered = io.BytesIO()
    with (
        zipfile.ZipFile(
            io.BytesIO(paired_fixture.comparison.content)
        ) as source,
        zipfile.ZipFile(altered, "w") as target,
    ):
        for name in source.namelist():
            content = source.read(name)
            if substitution == "readme" and name == "README.md":
                content += b"changed payload"
            elif (
                substitution == "witness"
                and name == dotnet_adapter.WITNESS_PATH
            ):
                content = b"{}"
            elif substitution in {"version", "description"} and name.endswith(
                ".nuspec"
            ):
                metadata = fixture_request.comparison_metadata
                old = (
                    metadata.version
                    if substitution == "version"
                    else metadata.description
                )
                content = content.replace(old.encode(), b"changed")
            elif substitution == "assembly" and name.endswith(".dll"):
                content = b"not an assembly"
            elif substitution == "metadata" and name.endswith(".nuspec"):
                content = content.replace(b"<authors>", b"<authors>changed ")
            elif substitution == "relationships" and name == "_rels/.rels":
                content = content.replace(b"Type=", b"ChangedType=")
            elif (
                substitution
                in {
                    "relationships-nuspec-target",
                    "relationships-core-target",
                }
                and name == "_rels/.rels"
            ):
                suffix = (
                    ".nuspec"
                    if substitution == "relationships-nuspec-target"
                    else ".psmdcp"
                )
                original_entry = next(
                    entry
                    for entry in paired_fixture.inspection.original.entries
                    if entry.endswith(suffix)
                )
                comparison_entry = next(
                    entry
                    for entry in source.namelist()
                    if entry.endswith(suffix)
                )
                if substitution == "relationships-core-target":
                    original_entry = (
                        "package/services/metadata/core-properties/"
                        "unadmitted.psmdcp"
                    )
                assert original_entry != comparison_entry
                actual_target = f'Target="/{comparison_entry}"'.encode()
                assert actual_target in content
                content = content.replace(
                    actual_target, f'Target="/{original_entry}"'.encode()
                )
            elif substitution == "core" and name.endswith(".psmdcp"):
                content = content.replace(b"<keywords>", b"<keywords>changed")
            elif (
                substitution == "content-types"
                and name == "[Content_Types].xml"
            ):
                content += b" "
            target.writestr(name, content)
        if substitution == "entry":
            target.writestr("unexpected.txt", b"extra")
    expected = {
        "readme": "compiled inputs or fixed package content",
        "witness": "frozen payload facts differ",
        "entry": "entry closure mismatch",
        "version": "native command failed",
        "description": "discarded its declared metadata",
        "assembly": "native command failed",
        "metadata": "undeclared package metadata",
        "relationships": "package relationships changed",
        "relationships-nuspec-target": "package relationships changed",
        "relationships-core-target": "package relationships changed",
        "core": "undeclared package metadata",
        "content-types": "compiled inputs or fixed package content",
    }
    with pytest.raises(ValueError, match=expected[substitution]):
        inspect_nuget_fixture_pair(
            paired_fixture.original,
            replace(paired_fixture.comparison, content=altered.getvalue()),
            _fixture_expectation(fixture_request, paired_fixture),
            fixture_request.comparison_metadata,
            fixture_request.build.helper,
        )


@pytest.mark.parametrize(
    "failure",
    ["incomplete", "failed", "nbgv", "recompile", "output", "consumer"],
)
def test_nuget_fixture_preparation_stops_on_incomplete_evidence(  # noqa: C901
    fixture_request: NuGetFixtureRequest,
    paired_fixture: NuGetFixturePair,
    failure: str,
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Modeled native failures expose diagnostics but never a complete pair."""
    request = replace(
        fixture_request,
        build=replace(
            fixture_request.build, evidence_directory=tmp_path / "failed"
        ),
    )

    execute = dotnet_adapter.run_native

    def command(argv, cwd, environment, *, diagnostics=None):
        if argv[0] == "git":
            return execute(argv, cwd, environment, diagnostics=diagnostics)
        assert diagnostics is not None
        diagnostics.mkdir(parents=True)
        (diagnostics / "stdout.txt").write_text("modeled command evidence")
        if argv[1] == "--version":
            return "10.0.401"
        log = Path(next(item[4:] for item in argv if item.startswith("-bl:")))
        log.write_bytes(b"modeled binlog; not native evidence")
        if argv[1] == "pack":
            output = Path(argv[argv.index("--output") + 1])
            output.mkdir()
            archive = (
                paired_fixture.comparison
                if log.stem == "pack-comparison"
                else paired_fixture.original
            )
            (output / archive.basename).write_bytes(archive.content)
            if failure == "output":
                (output / "unexpected.snupkg").write_bytes(b"extra")
        return ""

    def audit(_helper, log):
        observed = {
            "completed": True,
            "succeeded": True,
            "nbgvExecuted": False,
            "tasks": [],
        }
        if log.stem == "build":
            if failure == "incomplete":
                observed["completed"] = False
            elif failure == "failed":
                observed["succeeded"] = False
            elif failure == "nbgv":
                observed["nbgvExecuted"] = True
        if failure == "recompile" and log.stem == "pack-comparison":
            observed["tasks"] = ["Csc"]
        return observed

    def reject_consumer(*_args, **_kwargs):
        message = "modeled clean consumer failure"
        raise ValueError(message)

    monkeypatch.setattr(dotnet_adapter, "run_native", command)
    monkeypatch.setattr(NativeNuGetHelper, "audit_binlog", audit)
    monkeypatch.setattr(
        nuget_fixture, "qualify_nuget_restore_build_invoke", reject_consumer
    )
    expected = {
        "incomplete": "lacks complete proof",
        "failed": "lacks complete proof",
        "nbgv": "recomputed NBGV",
        "recompile": "reran compilation",
        "output": "exactly one nupkg",
        "consumer": "clean consumer failure",
    }
    with pytest.raises(ValueError, match=expected[failure]):
        prepare_nuget_fixture_pair(request)
    evidence = request.build.evidence_directory
    assert not (evidence / "fixtures.json").exists()
    assert (
        evidence / "build/stdout.txt"
    ).read_text() == "modeled command evidence"
    retained_audit = json.loads((evidence / "build-audit.json").read_bytes())
    if failure == "incomplete":
        assert retained_audit["completed"] is False
    elif failure == "failed":
        assert retained_audit["succeeded"] is False
    elif failure == "nbgv":
        assert retained_audit["nbgvExecuted"] is True
    elif failure == "consumer":
        assert (
            evidence / "pack" / paired_fixture.original.basename
        ).read_bytes() == paired_fixture.original.content


@pytest.mark.parametrize("failure", ["exit", "timeout", "start"])
def test_native_fixture_diagnostics_survive_process_failure(
    failure: str, tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    """Failure output remains available even when no binlog can be produced."""
    command = ("fixture-test-native", "build")

    def fail(*_args, **_kwargs):
        if failure == "timeout":
            raise subprocess.TimeoutExpired(
                command, 300, b"partial out", b"partial err"
            )
        if failure == "start":
            message = "fixture executable missing"
            raise OSError(message)
        return subprocess.CompletedProcess(
            command, 7, "partial out", "partial err"
        )

    monkeypatch.setattr(dotnet_provider.subprocess, "run", fail)
    expected = {
        "exit": (ValueError, "exit 7"),
        "timeout": (subprocess.TimeoutExpired, "timed out"),
        "start": (OSError, "fixture executable missing"),
    }
    error, message = expected[failure]
    evidence = tmp_path / "command"
    with pytest.raises(error, match=message):
        dotnet_provider.run_native(command, tmp_path, diagnostics=evidence)
    retained = json.loads((evidence / "command.json").read_bytes())
    assert retained["argv"] == list(command)
    if failure != "start":
        assert (evidence / "stdout.txt").read_text() == "partial out"
        assert (evidence / "stderr.txt").read_text() == "partial err"
    if failure == "exit":
        assert (evidence / "exit-code.txt").read_text() == "7"
    else:
        assert message in (evidence / "failure.txt").read_text()
    with pytest.raises(FileExistsError):
        dotnet_provider.run_native(command, tmp_path, diagnostics=evidence)


def _archive_inventory(package: bytes):
    with zipfile.ZipFile(io.BytesIO(package)) as archive:
        return [
            (
                item.filename,
                item.date_time,
                item.compress_type,
                item.external_attr,
                hashlib.sha256(archive.read(item)).hexdigest(),
            )
            for item in archive.infolist()
        ]


def test_frozen_reproducibility_preserves_original_archives(
    native_nuget_reproducibility,
):
    """Distinct actual roots and staged mtimes cannot affect original bytes."""
    campaign = native_nuget_reproducibility
    first, second = (
        campaign.results[name] for name in ("first", "second-longer-root")
    )
    assert first.package == second.package
    assert first.manifest == second.manifest
    assert first.witness == second.witness
    assert _archive_inventory(first.package) == _archive_inventory(
        second.package
    )
    roots, mtimes, caches = [], [], []
    inventories = []
    for label, result in (("first", first), ("second-longer-root", second)):
        evidence = campaign.evidence / label
        assert (
            evidence / result.manifest.basename
        ).read_bytes() == result.package
        staged = json.loads((evidence / "staged-inputs.json").read_text())
        roots.append(staged["root"])
        mtimes.append(staged["source_mtime"])
        assert {v["mtime"] for v in staged["files"].values()} == {
            staged["source_mtime"]
        }
        assert {
            name: value["sha256"] for name, value in staged["files"].items()
        } == dict(result.source_input_manifest)
        calls = json.loads((evidence / "commands.json").read_text())
        caches.append({call["cache"] for call in calls})
        files = list((evidence / "binaries").iterdir())
        assert {file.suffix for file in files} == {".dll", ".pdb"}
        inventories.append({file.name: file.read_bytes() for file in files})
        with zipfile.ZipFile(io.BytesIO(result.package)) as package:
            dlls = [
                name for name in package.namelist() if name.endswith(".dll")
            ]
            assert len(dlls) == 1
            assert package.read(dlls[0]) == inventories[-1][Path(dlls[0]).name]
    assert roots[0] != roots[1]
    assert mtimes[0] != mtimes[1]
    assert caches[0].isdisjoint(caches[1])
    assert inventories[0] == inventories[1]


def test_frozen_reproducibility_retains_native_audits_and_stable_debug_paths(
    native_nuget_reproducibility,
):
    """Official log/PDB readers prove frozen execution and normalized paths."""
    campaign = native_nuget_reproducibility
    for label in campaign.results:
        evidence = campaign.evidence / label
        for operation in ("restore", "build", "pack"):
            assert (evidence / f"{operation}.binlog").stat().st_size > 0
            audit = json.loads(
                (evidence / f"{operation}-audit.json").read_text()
            )
            assert audit["completed"] is True
            assert audit["succeeded"] is True
            assert audit["errors"] == []
            assert audit["nbgvExecuted"] is False
            if operation == "pack":
                assert "Csc" not in audit["tasks"]
        decoded = json.loads((evidence / "debug-paths.json").read_text())
        pdb = [item for item in decoded if item["kind"] == "portable-pdb"]
        pe = [item for item in decoded if item["kind"] == "pe-codeview"]
        assert len(pdb) == len(pe) == 1
        assert pdb[0]["documents"]
        assert pe[0]["entries"]
        paths = [
            *pdb[0]["documents"],
            *(item["path"] for item in pe[0]["entries"]),
        ]
        assert all(path.replace("\\", "/").startswith("/_/") for path in paths)
    provider = campaign.requests["first"].helper.audit_binlog(
        campaign.evidence / "provider/provider.binlog"
    )
    assert provider["nbgvExecuted"] is True


def test_frozen_reproducibility_uses_target_timestamp(
    native_nuget_reproducibility,
):
    """Target metadata controls timestamps despite varied mtimes."""
    campaign = native_nuget_reproducibility
    for label in ("first", "second-longer-root"):
        calls = json.loads(
            (campaign.evidence / label / "commands.json").read_text()
        )
        operations = [
            call
            for call in calls
            if call["argv"][1] in {"restore", "build", "pack"}
        ]
        assert {call["argv"][1] for call in operations} == {
            "restore",
            "build",
            "pack",
        }
        for call in operations:
            assert (
                f"-property:DeterministicTimestamp={campaign.timestamp}"
                in call["argv"]
            )
            assert "-property:Deterministic=true" in call["argv"]
        request = campaign.requests[label]
        assert (
            int(
                _git_bytes(
                    request.source_root,
                    "show",
                    "--no-patch",
                    "--format=%ct",
                    request.witness.target,
                ).strip()
            )
            == campaign.timestamp
        )


@pytest.mark.parametrize("control", ["source-control", "timestamp-control"])
def test_frozen_negative_controls_change_original_archive(
    native_nuget_reproducibility, control
):
    """Each control changes output and preserves the frozen witness."""
    campaign = native_nuget_reproducibility
    baseline, changed = campaign.results["first"], campaign.results[control]
    assert baseline.package != changed.package
    assert baseline.manifest.sha256 != changed.manifest.sha256
    assert baseline.witness == changed.witness
    assert baseline.toolchain == changed.toolchain
    baseline_inputs, control_inputs = (
        dict(baseline.source_input_manifest),
        dict(changed.source_input_manifest),
    )
    if control == "source-control":
        assert {
            name
            for name in baseline_inputs
            if baseline_inputs[name] != control_inputs[name]
        } == {f"{DOTNET_PROJECT_ROOT}/Smoke.cs"}
    else:
        assert baseline_inputs == control_inputs
        before = _archive_inventory(baseline.package)
        after = _archive_inventory(changed.package)
        assert [(name, digest) for name, _, _, _, digest in before] == [
            (name, digest) for name, _, _, _, digest in after
        ]
        assert [timestamp for _, timestamp, *_ in before] != [
            timestamp for _, timestamp, *_ in after
        ]


def test_reproducible_original_archive_passes_clean_consumer(
    native_nuget_reproducibility,
):
    """The exact original archive restores and invokes from a clean cache."""
    campaign = native_nuget_reproducibility
    result = campaign.results["first"]
    consumer = qualify_nuget_restore_build_invoke(
        result.package,
        result.expectation,
        campaign.requests["first"].helper,
        evidence_directory=campaign.evidence / "clean-consumer",
    )
    assert consumer.project_id == DOTNET_RELEASE_UNIT
    assert consumer.package_sha256 == result.manifest.sha256
    assert (
        consumer.witness_sha256
        == "sha256:" + hashlib.sha256(result.witness).hexdigest()
    )
    assert consumer.normalized_version == result.expectation.normalized_version


@pytest.mark.parametrize("timestamp", ["not-a-timestamp", "-1", "253402300800"])
def test_target_timestamp_rejects_invalid_git_metadata(
    frozen_source, monkeypatch, tmp_path, timestamp
):
    """Malformed target metadata cannot fall back to clock or native Build."""
    request, _, _ = frozen_source
    request = replace(request, evidence_directory=tmp_path / "forbidden-build")

    def malformed(command, *_args, **_kwargs):
        assert command[0] == "git", "Invalid timestamp reached a native build"
        assert request.witness.target in command
        return timestamp

    monkeypatch.setattr(dotnet_adapter, "run_native", malformed)
    with pytest.raises(ValueError, match="invalid commit timestamp"):
        build_dotnet_package(request)
    assert not request.evidence_directory.exists()


def test_target_timestamp_rejects_missing_exact_commit(frozen_source, tmp_path):
    """A missing witness target cannot fall back to the checkout HEAD."""
    request, _, _ = frozen_source
    missing = "f" * 40
    witness = replace(
        request.witness,
        target=missing,
        nbgv=replace(request.witness.nbgv, git_commit_id=missing),
    )
    request = replace(
        request,
        witness=witness,
        evidence_directory=tmp_path / "forbidden-build",
    )
    with pytest.raises(ValueError, match="native command failed"):
        build_dotnet_package(request)
    assert not request.evidence_directory.exists()


@pytest.mark.parametrize("timestamp", [0, 253402300799])
def test_target_timestamp_accepts_metadata_boundaries(
    frozen_source, monkeypatch, timestamp
):
    """Both admitted timestamp endpoints remain inclusive."""
    request, _, _ = frozen_source
    monkeypatch.setattr(
        dotnet_adapter, "run_native", lambda *_args: f"{timestamp}\n"
    )
    assert dotnet_adapter._target_timestamp(request) == timestamp  # noqa: SLF001


def test_target_timestamp_ignores_unrelated_checkout_head(
    frozen_source, tmp_path
):
    """A later local commit cannot override the exact frozen target's time."""
    request, _, _ = frozen_source
    source = tmp_path / "other-head"
    _git_bytes(
        tmp_path,
        "clone",
        "--quiet",
        "--bare",
        "--shared",
        str(request.source_root),
        str(source),
    )
    target = request.witness.target
    expected = int(
        _git_bytes(source, "show", "--no-patch", "--format=%ct", target).strip()
    )
    changed_time = expected + 86400
    tree = (
        _git_bytes(source, "rev-parse", f"{target}^{{tree}}").decode().strip()
    )
    later = subprocess.run(
        (
            "git",
            "-c",
            "user.name=Native Test",
            "-c",
            "user.email=native-test@example.invalid",
            "commit-tree",
            tree,
            "-p",
            target,
            "-m",
            "Unrelated later checkout",
        ),
        cwd=source,
        env={
            **os.environ,
            "GIT_AUTHOR_DATE": f"{changed_time} +0000",
            "GIT_COMMITTER_DATE": f"{changed_time} +0000",
        },
        check=True,
        capture_output=True,
        text=True,
    ).stdout.strip()
    _git_bytes(source, "update-ref", "HEAD", later)
    assert (
        int(
            _git_bytes(
                source, "show", "--no-patch", "--format=%ct", "HEAD"
            ).strip()
        )
        == changed_time
    )
    assert (
        dotnet_adapter._target_timestamp(  # noqa: SLF001 - exact-target core behavior
            replace(request, source_root=source)
        )
        == expected
    )
    # Local replacement refs must not rewrite immutable target metadata.
    _git_bytes(source, "replace", target, later)
    assert (
        int(_git_bytes(source, "show", "--no-patch", "--format=%ct", target))
        == changed_time
    )
    assert (
        dotnet_adapter._target_timestamp(  # noqa: SLF001 - exact-target core behavior
            replace(request, source_root=source)
        )
        == expected
    )
