"""NuGet compilation scenarios using native facts and actual Git targets."""

from __future__ import annotations

import hashlib
import shutil
import subprocess
from dataclasses import replace
from pathlib import Path
from typing import Any, cast

import pytest
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
)
from three_workflow_delivery_v3.catalogs import catalog_digest
from three_workflow_delivery_v3.repository import compiler, dotnet_provider
from three_workflow_delivery_v3.repository.descriptors import (
    FIRST_SLICE_PACKAGE,
    FIRST_SLICE_RELEASE_UNIT,
    NUGET_PACKAGE,
    NUGET_POLICY_PATH,
    NUGET_RELEASE_UNIT,
)
from three_workflow_delivery_v3.repository.node_provider import (
    AUTHORITATIVE_REMOTE,
    TAG_REFSPEC,
    CheckoutEvidence,
    GlobalInput,
    NbgvFacts,
    ProjectNode,
)

SOURCE_ROOT = Path(__file__).resolve().parents[6]
PROJECT_ROOT = dotnet_provider.DOTNET_PROJECT_ROOT
ENTRY_POINT = dotnet_provider.DOTNET_ENTRY_POINT
NATIVE_VERSION = "1.2.3-beta.42+Build.Meta"
NORMALIZED_VERSION = "1.2.3-beta.42"
DIGEST = "sha256:" + "a" * 64
OTHER_DIGEST = "sha256:" + "b" * 64


def _git(repo: Path, *args: str) -> str:
    return subprocess.run(  # noqa: S603
        ("git", *args),  # noqa: S607
        cwd=repo,
        check=True,
        capture_output=True,
        text=True,
    ).stdout.strip()


def _blob_digest(repo: Path, target: str, path: str) -> str:
    content = subprocess.run(  # noqa: S603
        ("git", "show", f"{target}:{path}"),  # noqa: S607
        cwd=repo,
        check=True,
        capture_output=True,
    ).stdout
    return "sha256:" + hashlib.sha256(content).hexdigest()


def _reject_native(*_args: object, **_kwargs: object) -> None:
    pytest.fail("the compiler must not execute native target code")


@pytest.fixture(scope="module")
def native_scenario_basis(tmp_path_factory: pytest.TempPathFactory):
    """Construct the immutable source and supplied facts once."""
    with pytest.MonkeyPatch.context() as patch:
        return _native_scenario(
            tmp_path_factory.mktemp("nuget-compiler-basis"), patch
        )


@pytest.fixture
def native_scenario(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    native_scenario_basis,
):
    """Keep each scenario's Git/worktree changes and native guard isolated."""
    source, context, manifest, result = native_scenario_basis
    repo = tmp_path / "repo"
    shutil.copytree(source, repo)
    monkeypatch.setattr(dotnet_provider, "run_native", _reject_native)
    return repo, context, manifest, result


def _native_scenario(tmp_path: Path, monkeypatch: pytest.MonkeyPatch):
    """Construct one isolated native source repository and provider result."""
    repo = tmp_path / "repo"
    repo.mkdir()
    _git(repo, "init", "--quiet")
    _git(repo, "config", "user.name", "Workflow Delivery Test")
    _git(repo, "config", "user.email", "workflow-delivery@example.invalid")
    paths = (
        "global.json",
        "nuget.config",
        "version.json",
        "Directory.Build.props",
        "Directory.Packages.props",
        "src/Directory.Build.props",
        "src/public/Directory.Build.props",
        "src/public/lib/Directory.Build.props",
        NUGET_POLICY_PATH,
    )
    for relative in paths:
        destination = repo / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(SOURCE_ROOT / relative, destination)
    shutil.copytree(SOURCE_ROOT / PROJECT_ROOT, repo / PROJECT_ROOT)
    _git(repo, "add", "--all")
    _git(repo, "commit", "--quiet", "--message", "native compiler fixture")
    target = _git(repo, "rev-parse", "HEAD")
    context = compiler.CompilationContext(
        request_id="nuget-release-request-42",
        purpose="live-release",
        workflow_run_id=7101,
        run_attempt=None,
        target=target,
        producer="compile-nuget-model",
        control=f"workflow-delivery-v3:{target}",
        catalog_digest=catalog_digest(),
    )
    manifest = compiler.nuget_provider_manifest(
        context, provider_producer="discover-dotnet"
    )
    source_paths = dotnet_provider.dotnet_provider_input_candidates(
        tuple(_git(repo, "ls-tree", "-r", "--name-only", target).splitlines())
    )
    inputs = tuple(
        (path, _blob_digest(repo, target, path)) for path in source_paths
    )
    manifest_digest, configuration_digest = (
        dotnet_provider.dotnet_input_digests(inputs)
    )
    result = dotnet_provider.DotnetProviderResult(
        binding=compiler.provider_binding(manifest, "dotnet-nuget-slice"),
        provider_logical_id=dotnet_provider.DOTNET_PROVIDER_LOGICAL_ID,
        provider_implementation_id=dotnet_provider.DOTNET_PROVIDER_IMPLEMENTATION_ID,
        execution_mode=dotnet_provider.DOTNET_PROVIDER_EXECUTION_MODE,
        execution_class=dotnet_provider.DOTNET_PROVIDER_EXECUTION_CLASS,
        toolchain=dotnet_provider.DOTNET_TOOLCHAIN,
        manifest_digest=manifest_digest,
        configuration_digest=configuration_digest,
        checkout=CheckoutEvidence(
            target=target,
            head=target,
            shallow=False,
            ancestry_complete=True,
            tags_complete=True,
            credentials_persisted=False,
            authoritative_remote=AUTHORITATIVE_REMOTE,
            authoritative_remote_url="file:///authoritative.git",
            tag_refspec=TAG_REFSPEC,
        ),
        project_nodes=(
            dotnet_provider.DotnetProjectNode(
                project_id=NUGET_RELEASE_UNIT,
                package_name=NUGET_PACKAGE,
                path=PROJECT_ROOT,
                manifest_path=ENTRY_POINT,
                target_framework="net10.0",
                packable=True,
                include_symbols=False,
                project_references=(),
                normalized_package_id="hcoona.releasesmoke.githubpackages",
                normalized_version=NORMALIZED_VERSION,
            ),
        ),
        global_inputs=tuple(
            GlobalInput(path, digest, (NUGET_RELEASE_UNIT,))
            for path, digest in inputs
        ),
        build_capabilities=("dotnet/nuget-package-v1",),
        nbgv=dotnet_provider.DotnetNbgvFacts(
            canonical_version="1.2.3",
            sem_ver1="1.2.3-beta-0042",
            sem_ver2=NATIVE_VERSION,
            version_height=42,
            git_commit_id=target,
            public_release=False,
            nuget_package_version=NATIVE_VERSION,
            assembly_version="1.2.0.0",
            assembly_file_version="1.2.3.42",
            assembly_informational_version=f"1.2.3-beta.42+{target}",
            native_result_digest=DIGEST,
        ),
        unresolved=(),
        conflicts=(),
        outcome="success",
        diagnostic_reference=None,
        source_input_manifest=inputs,
        native_evaluation_digest=DIGEST,
    )

    monkeypatch.setattr(dotnet_provider, "run_native", _reject_native)
    return repo, context, manifest, result


def _bundle(manifest, result):
    bundle = dotnet_provider.DotnetProviderFactBundle(
        schema=dotnet_provider.DOTNET_PROVIDER_FACT_BUNDLE_SCHEMA,
        binding=result.binding,
        provider_result=result,
        provider_result_digest=result.result_digest,
        manifest_digest=manifest.manifest_digest,
        manifest_entry_id=manifest.requests[0].entry_id,
        request_artifact_id=101,
        request_artifact_digest=DIGEST,
        transport_id=202,
        transport_digest=OTHER_DIGEST,
    )
    admission = compiler.FactBundleAdmissionContext(
        request_artifact_id=101,
        request_artifact_digest=DIGEST,
        transport_id=202,
        transport_digest=OTHER_DIGEST,
        bundle_digest=bundle.bundle_digest,
    )
    return bundle, admission


def _admitted(context, manifest, result):
    bundle, admission = _bundle(manifest, result)
    return compiler.admit_dotnet_provider_fact_bundle(
        bundle, context=context, manifest=manifest, admission=admission
    )


def _compile(scenario, *, result=None):
    repo, context, manifest, original = scenario
    admitted = _admitted(
        context, manifest, original if result is None else result
    )
    return compiler.compile_dotnet_repository_model(
        repo, context, manifest, [admitted]
    )


def test_dotnet_compiler_closes_native_build_and_policy(
    native_scenario,
) -> None:
    """Derive the NuGet artifact and Buddy policy from target authoring."""
    snapshot = _compile(native_scenario)
    project = snapshot.project_nodes[0]
    build = snapshot.release_units[0].builds[0]
    assert snapshot.ready is True
    assert snapshot.unresolved == ()
    assert type(project) is dotnet_provider.DotnetProjectNode
    assert project.normalized_package_id == "hcoona.releasesmoke.githubpackages"
    assert project.normalized_version == NORMALIZED_VERSION
    assert build.definition == "dotnet/nuget-package-v1"
    assert build.entry_point == ENTRY_POINT
    assert build.outputs == (
        compiler.CompiledOutput(
            "nuget-package", "primary-package", "nuget-package"
        ),
    )
    assert build.required_native_projections == ("NuGetPackageVersion",)
    assert snapshot.quality[0].required == (
        "dotnet/nuget-artifact-contents-v1",
        "dotnet/nuget-restore-build-invoke-v1",
    )
    assert snapshot.release_policy_path == NUGET_POLICY_PATH
    assert snapshot.release_policy is not None
    assert tuple(name for name, _ in snapshot.release_policy.channels) == (
        "buddy",
    )
    projection = snapshot.release_policy.channel("buddy").projections[0]
    assert projection.destination == "nuget/github-packages-hcoona-three-v1"
    assert projection.package == NUGET_PACKAGE
    assert snapshot.reverse_index == (
        (NUGET_RELEASE_UNIT, (f"{NUGET_RELEASE_UNIT}/nuget-package",)),
    )
    assert snapshot.provider_result_digests == (
        native_scenario[3].result_digest,
    )


def test_dotnet_manifest_closes_native_request(native_scenario) -> None:
    """Bind the native execution profile and separate it from a Node request."""
    _, context, manifest, _ = native_scenario
    request = manifest.requests[0]
    context_document = manifest.to_document()["context"]
    expected: dict[str, JsonValue] = {
        "schema": "workflow-delivery/v3/dotnet-provider-request",
        "context": context_document,
        "entry-id": "dotnet-nuget-slice",
        "provider-logical-id": "dotnet/msbuild-nbgv-v1",
        "provider-implementation-id": (
            "three-workflow-delivery-v3/dotnet-msbuild-nbgv-v1"
        ),
        "execution-mode": "target-evaluating",
        "producer": "discover-dotnet",
        "discovery-basis": {
            "package": NUGET_PACKAGE,
            "entry-point": ENTRY_POINT,
            "configuration": "Release",
            "target-framework": "net10.0",
            "runner-platform": "windows",
            "toolchain": dict(dotnet_provider.DOTNET_TOOLCHAIN),
            "build-definition": "dotnet/nuget-package-v1",
            "output-scope": ["nuget-package"],
        },
    }
    assert request.request_digest == canonical_sha256(expected)
    assert (
        request.expected_result_identity
        == f"dotnet/msbuild-nbgv-v1:{context.request_id}"
    )
    node_request = compiler.first_slice_provider_manifest(
        context, provider_producer="discover-dotnet"
    )
    assert manifest.manifest_digest != node_request.manifest_digest


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("entry_id", "node-first-slice"),
        ("provider_logical_id", "node/pnpm-nbgv-v1"),
        ("provider_implementation_id", "other-implementation"),
        ("execution_mode", "side-effecting"),
        ("request_digest", OTHER_DIGEST),
        ("expected_result_identity", "dotnet/msbuild-nbgv-v1:other"),
    ],
)
def test_dotnet_manifest_rejects_substitution(
    native_scenario, field, value
) -> None:
    """Reject a changed request even with a valid bound Bundle."""
    _, context, manifest, result = native_scenario
    bundle, admission = _bundle(manifest, result)
    changed = replace(
        manifest, requests=(replace(manifest.requests[0], **{field: value}),)
    )
    with pytest.raises(ValueError, match="closed canonical request"):
        compiler.admit_dotnet_provider_fact_bundle(
            bundle, context=context, manifest=changed, admission=admission
        )


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("schema", "workflow-delivery/v3/node-provider-fact-bundle"),
        ("manifest_digest", OTHER_DIGEST),
        ("manifest_entry_id", "node-first-slice"),
        ("request_artifact_id", 303),
        ("request_artifact_digest", OTHER_DIGEST),
        ("transport_id", 404),
        ("transport_digest", DIGEST),
        ("provider_result_digest", OTHER_DIGEST),
    ],
)
def test_dotnet_bundle_rejects_binding_and_integrity_substitution(
    native_scenario, field, value
) -> None:
    """Reject substitutions despite a recomputed outer digest."""
    _, context, manifest, result = native_scenario
    bundle, admission = _bundle(manifest, result)
    changed = replace(bundle, **{field: value})
    admission = replace(admission, bundle_digest=changed.bundle_digest)
    with pytest.raises(
        ValueError, match=r"schema identity|integrity or transport"
    ):
        compiler.admit_dotnet_provider_fact_bundle(
            changed, context=context, manifest=manifest, admission=admission
        )


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("request_id", "another-request"),
        ("workflow_run_id", 7201),
        ("producer", "another-producer"),
        ("control", "another-control"),
        ("catalog_digest", OTHER_DIGEST),
        ("request_digest", OTHER_DIGEST),
    ],
)
def test_dotnet_bundle_rejects_other_authority(
    native_scenario, field, value
) -> None:
    """Reject internally consistent facts belonging to another authority."""
    _, context, manifest, result = native_scenario
    changed = replace(result, binding=replace(result.binding, **{field: value}))
    bundle, admission = _bundle(manifest, changed)
    with pytest.raises(ValueError, match=r"authority binding|catalog digest"):
        compiler.admit_dotnet_provider_fact_bundle(
            bundle, context=context, manifest=manifest, admission=admission
        )


@pytest.mark.parametrize(
    ("purpose", "attempt"), [("slice-validation", 1), ("ci-pr-slice-shadow", 2)]
)
def test_dotnet_bundle_rejects_cross_purpose(
    native_scenario, purpose, attempt
) -> None:
    """Reject qualification facts offered as current live Release evidence."""
    _, context, manifest, result = native_scenario
    changed = replace(
        result,
        binding=replace(result.binding, purpose=purpose, run_attempt=attempt),
    )
    bundle, admission = _bundle(manifest, changed)
    with pytest.raises(ValueError, match="authority binding"):
        compiler.admit_dotnet_provider_fact_bundle(
            bundle, context=context, manifest=manifest, admission=admission
        )


def test_dotnet_compiler_uses_exact_git_target_inputs(native_scenario) -> None:
    """Ignore dirty target code and policy."""
    repo, *_ = native_scenario
    expected = _compile(native_scenario)
    (repo / ENTRY_POINT).write_text(
        "not MSBuild XML and never evaluated", encoding="utf-8"
    )
    (repo / NUGET_POLICY_PATH).write_text(
        "unreviewed worktree policy", encoding="utf-8"
    )
    actual = _compile(native_scenario)
    assert actual.snapshot_digest == expected.snapshot_digest
    assert actual.release_policy == expected.release_policy
    assert type(actual.nbgv) is dotnet_provider.DotnetNbgvFacts
    assert actual.nbgv.nuget_package_version == NATIVE_VERSION


@pytest.mark.parametrize(
    "field",
    [
        "manifest_digest",
        "configuration_digest",
        "source_input_manifest",
        "global_inputs",
    ],
)
def test_dotnet_compiler_rejects_target_input_substitution(
    native_scenario, field
) -> None:
    """Rehash target Git blobs of the supplied Provider digests."""
    result = native_scenario[3]
    value: Any = OTHER_DIGEST
    if field == "source_input_manifest":
        value = tuple(
            (path, OTHER_DIGEST if path == ENTRY_POINT else digest)
            for path, digest in result.source_input_manifest
        )
    elif field == "global_inputs":
        value = result.global_inputs[:-1]
    changed = replace(result, **{field: value})
    with pytest.raises(ValueError, match="input digests do not match"):
        _compile(native_scenario, result=changed)


@pytest.mark.parametrize("count", [0, 2])
def test_dotnet_compiler_rejects_bundle_closure(native_scenario, count) -> None:
    """Require exactly one terminal result before compiling any build."""
    repo, context, manifest, result = native_scenario
    admitted = _admitted(context, manifest, result)
    with pytest.raises(ValueError, match="exactly one admitted"):
        compiler.compile_dotnet_repository_model(
            repo, context, manifest, [admitted] * count
        )


def test_dotnet_compiler_rejects_node_bundle(native_scenario) -> None:
    """Keep Node and Dotnet transport admission mutually exclusive."""
    repo, context, manifest, result = native_scenario
    bundle, admission = _bundle(manifest, result)
    masquerading = compiler.AdmittedNodeProviderFactBundle(
        cast("Any", bundle), admission
    )
    with pytest.raises(TypeError, match=r"admitted \.NET Fact Bundle"):
        compiler.compile_dotnet_repository_model(
            repo, context, manifest, cast("Any", [masquerading])
        )
    node_manifest = compiler.first_slice_provider_manifest(
        context, provider_producer="discover-dotnet"
    )
    with pytest.raises(TypeError, match="admitted Fact Bundle"):
        compiler.admit_node_provider_fact_bundle(
            cast("Any", bundle),
            context=context,
            manifest=node_manifest,
            admission=admission,
        )


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("target_framework", "net8.0"),
        ("include_symbols", True),
        ("packable", False),
        ("project_references", ("other.csproj",)),
        ("normalized_package_id", "other.package"),
        ("normalized_version", ""),
    ],
)
def test_dotnet_compiler_rejects_incomplete_native_scope(
    native_scenario, field, value
) -> None:
    """Reject expanded or missing native build scope."""
    result = native_scenario[3]
    project = replace(result.project_nodes[0], **{field: value})
    with pytest.raises(ValueError, match=r"closure|nonempty"):
        _compile(
            native_scenario, result=replace(result, project_nodes=(project,))
        )


def test_dotnet_snapshot_preserves_native_facts_and_authority(
    native_scenario,
) -> None:
    """Carry native metadata through canonical admission without npm aliases."""
    snapshot = _compile(native_scenario)
    document: Any = snapshot.to_document()
    parsed = compiler.repository_model_snapshot_from_document(document)
    admitted = compiler.admit_repository_model_snapshot(
        canonicalize(document),
        expected_context=snapshot.context,
        expected_digest=snapshot.snapshot_digest,
    )
    assert admitted.snapshot == snapshot == parsed
    assert type(parsed.nbgv) is dotnet_provider.DotnetNbgvFacts
    assert parsed.nbgv.nuget_package_version == NATIVE_VERSION
    assert parsed.nbgv.assembly_file_version == "1.2.3.42"
    assert parsed.nbgv.assembly_informational_version.endswith(
        snapshot.context.target
    )
    assert "run-attempt" not in document["context"]
    assert "npmPackageVersion" not in document["nbgv"]["native"]
    assert "private" not in document["project-nodes"][0]
    with pytest.raises(ValueError, match="requires Node facts"):
        compiler.validate_first_slice_repository_model_snapshot(snapshot)


@pytest.mark.parametrize("field", ["nbgv", "project-nodes", "release-units"])
def test_dotnet_snapshot_rejects_cross_ecosystem_substitution(
    native_scenario, field
) -> None:
    """Reject mixed native facts and units during Snapshot decoding."""
    snapshot = _compile(native_scenario)
    document: Any = snapshot.to_document()
    if field == "nbgv":
        document[field] = NbgvFacts(
            canonical_version="1.2.3",
            sem_ver1="1.2.3",
            sem_ver2="1.2.3",
            version_height=42,
            git_commit_id=snapshot.context.target,
            public_release=False,
            npm_package_version="1.2.3",
            node_api_result_digest=DIGEST,
        ).to_document()
    elif field == "project-nodes":
        project = ProjectNode(
            project_id=FIRST_SLICE_PACKAGE,
            package_name=FIRST_SLICE_PACKAGE,
            path="src/public/lib/hcoona-release-smoke-npm",
            manifest_path="src/public/lib/hcoona-release-smoke-npm/package.json",
            private=False,
            workspace_dependencies=(),
        )
        document[field] = [
            {
                "project-id": project.project_id,
                "package-name": project.package_name,
                "path": project.path,
                "manifest-path": project.manifest_path,
                "private": False,
                "workspace-dependencies": [],
            }
        ]
    else:
        document[field][0]["release-unit"] = FIRST_SLICE_RELEASE_UNIT
    with pytest.raises(ValueError, match="mixes ecosystem"):
        compiler.repository_model_snapshot_from_document(document)


@pytest.mark.parametrize("field", ["request_id", "target", "digest"])
def test_dotnet_snapshot_rejects_context_and_digest_substitution(
    native_scenario, field
) -> None:
    """Require current context and exact bytes at Snapshot admission."""
    snapshot = _compile(native_scenario)
    context = snapshot.context
    digest = snapshot.snapshot_digest
    if field == "digest":
        digest = OTHER_DIGEST
    elif field == "target":
        context = replace(context, target="b" * 40)
    else:
        context = replace(context, request_id="other-request")
    with pytest.raises(ValueError, match=r"context binding|canonical digest"):
        compiler.admit_repository_model_snapshot(
            canonicalize(snapshot.to_document()),
            expected_context=context,
            expected_digest=digest,
        )


def test_dotnet_simulation_keeps_its_unit_and_attempt(native_scenario) -> None:
    """Bind the NuGet Buddy simulation to its own attempt and selection."""
    repo, original_context, _, result = native_scenario
    context = replace(
        original_context,
        purpose="release-simulation",
        run_attempt=2,
        channel="buddy",
        release_unit=NUGET_RELEASE_UNIT,
    )
    manifest = compiler.nuget_provider_manifest(
        context, provider_producer="discover-dotnet"
    )
    result = replace(
        result,
        binding=compiler.provider_binding(manifest, "dotnet-nuget-slice"),
    )
    snapshot = _compile((repo, context, manifest, result))
    document: Any = snapshot.to_document()
    assert document["context"]["run-attempt"] == context.run_attempt
    assert snapshot.context.release_unit == NUGET_RELEASE_UNIT
    with pytest.raises(ValueError, match="only the Buddy"):
        compiler.nuget_provider_manifest(
            replace(context, channel="official"),
            provider_producer="discover-dotnet",
        )
    with pytest.raises(ValueError, match="first Release Unit"):
        compiler.first_slice_provider_manifest(
            context, provider_producer="discover-node"
        )
    node_manifest = compiler.first_slice_provider_manifest(
        original_context, provider_producer="discover-node"
    )
    with pytest.raises(ValueError, match="first Release Unit"):
        compiler.compile_repository_model(
            repo,
            context,
            replace(node_manifest, context=context),
            [],
        )


def test_dotnet_manifest_revalidates_context_primitive_types(
    native_scenario,
) -> None:
    """Reject equal-valued floats in the manifest context."""
    _, context, manifest, result = native_scenario
    altered_context = replace(
        context, workflow_run_id=cast("Any", float(context.workflow_run_id))
    )
    altered_manifest = replace(manifest, context=altered_context)
    bundle, admission = _bundle(manifest, result)
    with pytest.raises(ValueError, match="positive integer"):
        compiler.admit_dotnet_provider_fact_bundle(
            bundle,
            context=context,
            manifest=altered_manifest,
            admission=admission,
        )


def test_dotnet_compiler_rehashes_internally_consistent_foreign_inputs(
    native_scenario,
) -> None:
    """Reject redigested source facts that do not match target Git blobs."""
    result = native_scenario[3]
    inputs = tuple(
        (path, OTHER_DIGEST if path == ENTRY_POINT else digest)
        for path, digest in result.source_input_manifest
    )
    manifest_digest, configuration_digest = (
        dotnet_provider.dotnet_input_digests(inputs)
    )
    changed = replace(
        result,
        source_input_manifest=inputs,
        manifest_digest=manifest_digest,
        configuration_digest=configuration_digest,
        global_inputs=tuple(
            GlobalInput(path, digest, (NUGET_RELEASE_UNIT,))
            for path, digest in inputs
        ),
    )
    with pytest.raises(
        ValueError, match="input digests do not match the exact target"
    ):
        _compile(native_scenario, result=changed)


@pytest.mark.parametrize(
    "field",
    ["ready", "build", "quality", "policy", "reverse-index", "native-target"],
)
def test_dotnet_snapshot_revalidates_qualified_closure(
    native_scenario, field
) -> None:
    """Reject altered qualified scope with a current canonical digest."""
    snapshot = _compile(native_scenario)
    if field == "ready":
        snapshot = replace(snapshot, ready=False)
    elif field == "build":
        unit = snapshot.release_units[0]
        build = replace(
            unit.builds[0],
            outputs=(compiler.CompiledOutput("symbols", "symbols", "snupkg"),),
        )
        snapshot = replace(
            snapshot, release_units=(replace(unit, builds=(build,)),)
        )
    elif field == "quality":
        snapshot = replace(
            snapshot, quality=(replace(snapshot.quality[0], required=()),)
        )
    elif field == "policy":
        assert snapshot.release_policy is not None
        policy = replace(
            snapshot.release_policy,
            governance=replace(
                snapshot.release_policy.governance,
                path=".github/workflow-delivery/governance/hcoona-release-smoke-npm.json",
            ),
        )
        snapshot = replace(snapshot, release_policy=policy)
    elif field == "reverse-index":
        snapshot = replace(snapshot, reverse_index=((NUGET_RELEASE_UNIT, ()),))
    else:
        snapshot = replace(
            snapshot, nbgv=replace(snapshot.nbgv, git_commit_id="b" * 40)
        )
    with pytest.raises(ValueError, match=r"closure|mismatch"):
        compiler.admit_repository_model_snapshot(
            canonicalize(snapshot.to_document()),
            expected_context=snapshot.context,
            expected_digest=snapshot.snapshot_digest,
        )


def test_dotnet_bundle_rejects_previous_simulation_attempt(
    native_scenario,
) -> None:
    """Do not adopt a previous simulation's internally consistent Bundle."""
    _, context, _, result = native_scenario
    previous = replace(
        context,
        purpose="release-simulation",
        run_attempt=1,
        channel="buddy",
        release_unit=NUGET_RELEASE_UNIT,
    )
    current = replace(previous, run_attempt=2)
    previous_manifest = compiler.nuget_provider_manifest(
        previous, provider_producer="discover-dotnet"
    )
    current_manifest = compiler.nuget_provider_manifest(
        current, provider_producer="discover-dotnet"
    )
    result = replace(
        result,
        binding=compiler.provider_binding(
            previous_manifest, "dotnet-nuget-slice"
        ),
    )
    bundle, admission = _bundle(previous_manifest, result)
    with pytest.raises(ValueError, match="authority binding"):
        compiler.admit_dotnet_provider_fact_bundle(
            bundle,
            context=current,
            manifest=current_manifest,
            admission=admission,
        )
