"""Scenario tests for the purpose-bound Repository Model compiler."""

from __future__ import annotations

import hashlib
import shutil
import subprocess
import sys
from collections.abc import Callable
from dataclasses import replace
from pathlib import Path
from typing import cast

import pytest
import yaml
from three_workflow_delivery_v3.canonical import JsonValue, canonical_sha256
from three_workflow_delivery_v3.catalogs import catalog_digest
from three_workflow_delivery_v3.repository.compiler import (
    AdmittedNodeProviderFactBundle,
    CompilationContext,
    FactBundleAdmissionContext,
    ProviderRequest,
    ProviderRequestManifest,
    admit_node_provider_fact_bundle,
    compile_repository_model,
    first_slice_provider_manifest,
    provider_binding,
    repository_model_snapshot_from_document,
)
from three_workflow_delivery_v3.repository.descriptors import (
    FIRST_SLICE_PACKAGE,
    FIRST_SLICE_POLICY_PATH,
    FIRST_SLICE_RELEASE_UNIT,
)
from three_workflow_delivery_v3.repository.node_provider import (
    AUTHORITATIVE_REMOTE,
    PROVIDER_EXECUTION_CLASS,
    PROVIDER_EXECUTION_MODE,
    PROVIDER_IMPLEMENTATION_ID,
    PROVIDER_LOGICAL_ID,
    TAG_REFSPEC,
    CheckoutEvidence,
    GlobalInput,
    NbgvFacts,
    NodeProviderFactBundle,
    NodeProviderResult,
    ProjectNode,
    ProviderBinding,
    validate_provider_toolchain,
)

SOURCE_REPO_ROOT = Path(__file__).resolve().parents[6]
REPO_ROOT = SOURCE_REPO_ROOT
PRODUCT_PATH = "src/public/lib/hcoona-release-smoke-npm"
TARGET = "e" * 40
NPM_VERSION = "1.2.3-beta.42.ge123456"
RUN_ATTEMPT = 3
REPLAY_ATTEMPT = 4
REQUEST_ARTIFACT_ID = 101
REQUEST_ARTIFACT_DIGEST = "sha256:" + ("7" * 64)
TRANSPORT_ID = 202
TRANSPORT_DIGEST = "sha256:" + ("8" * 64)

type ProviderBindingMutation = Callable[[ProviderBinding], ProviderBinding]
type NodeProviderResultMutation = Callable[
    [NodeProviderResult], NodeProviderResult
]
type ProviderRequestMutation = Callable[[ProviderRequest], ProviderRequest]


def _mutate_provider_binding(
    result: NodeProviderResult,
    mutation: ProviderBindingMutation,
) -> NodeProviderResult:
    return replace(result, binding=mutation(result.binding))


def _mutate_provider_result(
    result: NodeProviderResult,
    mutation: NodeProviderResultMutation,
) -> NodeProviderResult:
    return mutation(result)


def _mutate_provider_request(
    request: ProviderRequest,
    mutation: ProviderRequestMutation,
) -> ProviderRequest:
    return mutation(request)


def _with_other_catalog_digest(
    binding: ProviderBinding,
) -> ProviderBinding:
    return replace(binding, catalog_digest="sha256:" + ("b" * 64))


def _with_other_request_digest(
    binding: ProviderBinding,
) -> ProviderBinding:
    return replace(binding, request_digest="sha256:" + ("c" * 64))


def _with_nonterminal_outcome(
    result: NodeProviderResult,
) -> NodeProviderResult:
    return replace(result, outcome="blocked")


def _with_unresolved_workspace_graph(
    result: NodeProviderResult,
) -> NodeProviderResult:
    return replace(result, unresolved=("workspace graph unknown",))


def _with_other_provider_logical_id(
    request: ProviderRequest,
) -> ProviderRequest:
    return replace(request, provider_logical_id="target/provider-v1")


def _with_other_provider_implementation_id(
    request: ProviderRequest,
) -> ProviderRequest:
    return replace(request, provider_implementation_id="target/module.py")


def _with_side_effecting_execution_mode(
    request: ProviderRequest,
) -> ProviderRequest:
    return replace(request, execution_mode="side-effecting")


def _replace_sole_project_node(
    result: NodeProviderResult,
    project: ProjectNode,
) -> NodeProviderResult:
    return replace(result, project_nodes=(project,))


def _with_other_project_id(
    result: NodeProviderResult,
) -> NodeProviderResult:
    project = replace(
        result.project_nodes[0],
        project_id="@hcoona/other",
    )
    return _replace_sole_project_node(result, project)


def _with_other_package_name(
    result: NodeProviderResult,
) -> NodeProviderResult:
    project = replace(
        result.project_nodes[0],
        package_name="@hcoona/other",
    )
    return _replace_sole_project_node(result, project)


def _with_other_project_path(
    result: NodeProviderResult,
) -> NodeProviderResult:
    project = replace(
        result.project_nodes[0],
        path="src/public/lib/other",
    )
    return _replace_sole_project_node(result, project)


def _with_other_manifest_path(
    result: NodeProviderResult,
) -> NodeProviderResult:
    project = replace(
        result.project_nodes[0],
        manifest_path="src/public/lib/other/package.json",
    )
    return _replace_sole_project_node(result, project)


def _with_private_project(
    result: NodeProviderResult,
) -> NodeProviderResult:
    project = replace(result.project_nodes[0], private=True)
    return _replace_sole_project_node(result, project)


def _without_project_nodes(
    result: NodeProviderResult,
) -> NodeProviderResult:
    return replace(result, project_nodes=())


def _with_duplicate_project_node(
    result: NodeProviderResult,
) -> NodeProviderResult:
    project = result.project_nodes[0]
    return replace(result, project_nodes=(project, project))


def _with_workspace_dependencies(
    result: NodeProviderResult,
    workspace_dependencies: tuple[str, ...],
) -> NodeProviderResult:
    return replace(
        result,
        project_nodes=(
            replace(
                result.project_nodes[0],
                workspace_dependencies=workspace_dependencies,
            ),
        ),
    )


def _run(repo: Path, *command: str) -> str:
    return subprocess.run(  # noqa: S603
        command,
        cwd=repo,
        check=True,
        capture_output=True,
        text=True,
    ).stdout.strip()


def _target_bytes(repo: Path, target: str, path: str) -> bytes:
    return subprocess.run(  # noqa: S603
        ("git", "show", f"{target}:{path}"),  # noqa: S607
        cwd=repo,
        check=True,
        capture_output=True,
    ).stdout


def _content_digest(content: bytes) -> str:
    return f"sha256:{hashlib.sha256(content).hexdigest()}"


def _provider_inputs(
    repo: Path,
    target: str,
) -> tuple[str, str, tuple[GlobalInput, ...]]:
    manifest_digest = _content_digest(
        _target_bytes(repo, target, f"{PRODUCT_PATH}/package.json")
    )
    global_inputs = tuple(
        GlobalInput(
            path=path,
            content_digest=_content_digest(_target_bytes(repo, target, path)),
            project_ids=("@hcoona/hcoona-release-smoke-npm",),
        )
        for path in (
            "package.json",
            "pnpm-lock.yaml",
            "pnpm-workspace.yaml",
            f"{PRODUCT_PATH}/version.json",
            "version.json",
        )
    )
    configuration_digest = canonical_sha256(
        {
            "schema": "workflow-delivery/v3/node-provider-configuration",
            "global-inputs": [
                global_input.to_document() for global_input in global_inputs
            ],
        }
    )
    return manifest_digest, configuration_digest, global_inputs


def _initialize_repository(repo: Path) -> None:
    _run(repo, "git", "init", "--quiet")
    _run(repo, "git", "config", "user.name", "Workflow Delivery Test")
    _run(
        repo,
        "git",
        "config",
        "user.email",
        "workflow-delivery@example.invalid",
    )


def _commit_all(repo: Path) -> str:
    _run(repo, "git", "add", "--all")
    _run(repo, "git", "commit", "--quiet", "--message", "fixture")
    return _run(repo, "git", "rev-parse", "HEAD")


def _write(path: Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")


def _write_first_slice_authoring(
    repo: Path,
    *,
    descriptor: object | None = None,
    include_entry_point: bool = True,
) -> None:
    source_product = SOURCE_REPO_ROOT / PRODUCT_PATH
    target_product = repo / PRODUCT_PATH
    if descriptor is None:
        descriptor_content = (
            source_product / "workflow-delivery.release-unit.yml"
        ).read_text(encoding="utf-8")
    else:
        descriptor_content = yaml.safe_dump(descriptor, sort_keys=False)
    _write(
        target_product / "workflow-delivery.release-unit.yml",
        descriptor_content,
    )
    _write(
        target_product / "workflow-delivery.quality.yml",
        (source_product / "workflow-delivery.quality.yml").read_text(
            encoding="utf-8"
        ),
    )
    if include_entry_point:
        _write(
            target_product / "package.json",
            (source_product / "package.json").read_text(encoding="utf-8"),
        )
    _write(
        target_product / "version.json",
        (source_product / "version.json").read_text(encoding="utf-8"),
    )
    _write(
        repo / FIRST_SLICE_POLICY_PATH,
        (SOURCE_REPO_ROOT / FIRST_SLICE_POLICY_PATH).read_text(
            encoding="utf-8"
        ),
    )
    for name in (
        "package.json",
        "pnpm-lock.yaml",
        "pnpm-workspace.yaml",
        f"{PRODUCT_PATH}/version.json",
        "version.json",
    ):
        _write(
            repo / name,
            (SOURCE_REPO_ROOT / name).read_text(encoding="utf-8"),
        )


@pytest.fixture
def target_authoring_tree(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Compile scenarios against an actual target tree, not the worktree."""
    repo = tmp_path / "target-repo"
    repo.mkdir()
    _initialize_repository(repo)
    _write_first_slice_authoring(repo)
    target = _commit_all(repo)
    module = sys.modules[__name__]
    monkeypatch.setattr(module, "REPO_ROOT", repo)
    monkeypatch.setattr(module, "TARGET", target)


def _context(
    *,
    purpose: str = "live-release",
    run_attempt: int | None = None,
    target: str | None = None,
) -> CompilationContext:
    simulation = purpose == "release-simulation"
    if purpose != "live-release" and run_attempt is None:
        run_attempt = RUN_ATTEMPT
    selected_target = target or TARGET
    return CompilationContext(
        request_id="release-request-42",
        purpose=purpose,
        workflow_run_id=7101,
        run_attempt=run_attempt,
        target=selected_target,
        producer="compile-model",
        control=f"workflow-delivery-v3:{selected_target}",
        catalog_digest=catalog_digest(),
        channel="official" if simulation else None,
        release_unit="hcoona-release-smoke-npm" if simulation else None,
    )


def _expected_provider_request_document(
    context: CompilationContext,
    *,
    provider_producer: str,
) -> dict[str, JsonValue]:
    context_document: dict[str, JsonValue] = {
        "request-id": context.request_id,
        "purpose": context.purpose,
        "workflow-run-id": context.workflow_run_id,
        "target": context.target,
        "producer": context.producer,
        "control": context.control,
        "catalog-digest": context.catalog_digest,
        "channel": context.channel,
        "release-unit": context.release_unit,
    }
    if context.run_attempt is not None:
        context_document["run-attempt"] = context.run_attempt
    return {
        "schema": "workflow-delivery/v3/node-provider-request",
        "context": context_document,
        "entry-id": "node-first-slice",
        "provider-logical-id": PROVIDER_LOGICAL_ID,
        "provider-implementation-id": PROVIDER_IMPLEMENTATION_ID,
        "execution-mode": PROVIDER_EXECUTION_MODE,
        "producer": provider_producer,
        "discovery-basis": {
            "package": "@hcoona/hcoona-release-smoke-npm",
            "entry-point": f"{PRODUCT_PATH}/package.json",
        },
    }


def _result(
    context: CompilationContext,
    manifest: ProviderRequestManifest,
    *,
    repo_root: Path | None = None,
) -> NodeProviderResult:
    if repo_root is None:
        # Intrinsic and admission cases use in-memory facts.
        manifest_digest = "sha256:" + "b" * 64
        configuration_digest = "sha256:" + "c" * 64
        global_inputs = ()
    else:
        manifest_digest, configuration_digest, global_inputs = _provider_inputs(
            repo_root,
            context.target,
        )
    return NodeProviderResult(
        binding=provider_binding(manifest, "node-first-slice"),
        provider_logical_id=PROVIDER_LOGICAL_ID,
        provider_implementation_id=PROVIDER_IMPLEMENTATION_ID,
        execution_mode=PROVIDER_EXECUTION_MODE,
        execution_class=PROVIDER_EXECUTION_CLASS,
        toolchain=(("node", "v24.14.0"), ("pnpm", "11.21.0")),
        manifest_digest=manifest_digest,
        configuration_digest=configuration_digest,
        checkout=CheckoutEvidence(
            target=context.target,
            head=context.target,
            shallow=False,
            ancestry_complete=True,
            tags_complete=True,
            credentials_persisted=False,
            authoritative_remote=AUTHORITATIVE_REMOTE,
            authoritative_remote_url="file:///authoritative-remote.git",
            tag_refspec=TAG_REFSPEC,
        ),
        project_nodes=(
            ProjectNode(
                project_id="@hcoona/hcoona-release-smoke-npm",
                package_name="@hcoona/hcoona-release-smoke-npm",
                path=PRODUCT_PATH,
                manifest_path=f"{PRODUCT_PATH}/package.json",
                private=False,
                workspace_dependencies=(),
            ),
        ),
        global_inputs=global_inputs,
        build_capabilities=("node/npm-package-v1",),
        nbgv=NbgvFacts(
            canonical_version="1.2.3",
            sem_ver1="1.2.3-beta-0042-e123456",
            sem_ver2=NPM_VERSION,
            version_height=42,
            git_commit_id=context.target,
            public_release=False,
            npm_package_version=NPM_VERSION,
            node_api_result_digest="sha256:" + ("a" * 64),
        ),
        unresolved=(),
        conflicts=(),
        outcome="success",
        diagnostic_reference=None,
    )


def _admitted_bundle(
    context: CompilationContext,
    manifest: ProviderRequestManifest,
    result: NodeProviderResult,
) -> AdmittedNodeProviderFactBundle:
    bundle, admission = _bundle_admission_inputs(manifest, result)
    return admit_node_provider_fact_bundle(
        bundle,
        context=context,
        manifest=manifest,
        admission=admission,
    )


def _bundle_admission_inputs(
    manifest: ProviderRequestManifest,
    result: NodeProviderResult,
) -> tuple[NodeProviderFactBundle, FactBundleAdmissionContext]:
    bundle = NodeProviderFactBundle(
        schema="workflow-delivery/v3/node-provider-fact-bundle",
        binding=result.binding,
        provider_result=result,
        provider_result_digest=result.result_digest,
        manifest_digest=manifest.manifest_digest,
        manifest_entry_id=manifest.requests[0].entry_id,
        request_artifact_id=REQUEST_ARTIFACT_ID,
        request_artifact_digest=REQUEST_ARTIFACT_DIGEST,
        transport_id=TRANSPORT_ID,
        transport_digest=TRANSPORT_DIGEST,
    )
    admission = FactBundleAdmissionContext(
        request_artifact_id=REQUEST_ARTIFACT_ID,
        request_artifact_digest=REQUEST_ARTIFACT_DIGEST,
        transport_id=TRANSPORT_ID,
        transport_digest=TRANSPORT_DIGEST,
        bundle_digest=bundle.bundle_digest,
    )
    return bundle, admission


def _compile(
    repo: Path,
    context: CompilationContext,
    manifest: ProviderRequestManifest,
    result: NodeProviderResult,
):
    return compile_repository_model(
        repo,
        context,
        manifest,
        [_admitted_bundle(context, manifest, result)],
    )


def _scenario(
    context: CompilationContext | None = None,
    *,
    repo_root: Path | None = None,
) -> tuple[CompilationContext, ProviderRequestManifest, NodeProviderResult]:
    selected = context or _context()
    manifest = first_slice_provider_manifest(
        selected,
        provider_producer="discover-node",
    )
    return (
        selected,
        manifest,
        _result(
            selected,
            manifest,
            repo_root=repo_root,
        ),
    )


@pytest.mark.usefixtures("target_authoring_tree")
def test_compiler_closes_first_slice_repository_model() -> None:
    """Close Project Node, Release Unit, output, Quality, and reverse index."""
    context, manifest, result = _scenario(repo_root=REPO_ROOT)

    snapshot = _compile(REPO_ROOT, context, manifest, result)

    assert snapshot.ready
    assert snapshot.unresolved == ()
    assert snapshot.context == context
    assert snapshot.manifest_digest == manifest.manifest_digest
    assert snapshot.provider_result_digests == (result.result_digest,)
    assert snapshot.project_nodes == result.project_nodes
    assert tuple(item.path for item in result.global_inputs) == (
        "package.json",
        "pnpm-lock.yaml",
        "pnpm-workspace.yaml",
        f"{PRODUCT_PATH}/version.json",
        "version.json",
    )
    assert snapshot.project_nodes[0].manifest_path == (
        f"{PRODUCT_PATH}/package.json"
    )
    assert snapshot.release_units[0].release_unit == (
        "hcoona-release-smoke-npm"
    )
    build = snapshot.release_units[0].builds[0]
    assert build.build_id == "npm-package"
    assert build.definition == "node/npm-package-v1"
    assert build.project_id == "@hcoona/hcoona-release-smoke-npm"
    assert build.entry_point == f"{PRODUCT_PATH}/package.json"
    assert tuple(
        (output.output_id, output.role, output.kind) for output in build.outputs
    ) == (("npm-tarball", "primary-package", "npm-tarball"),)
    assert build.required_native_projections == ("npmPackageVersion",)
    assert snapshot.quality[0].preset == ("node/hcoona-release-smoke-npm-v1")
    assert snapshot.quality[0].required == (
        "node/project-build-v1",
        "node/project-test-v1",
    )
    assert snapshot.release_policy is not None
    assert snapshot.release_policy.path == FIRST_SLICE_POLICY_PATH
    assert snapshot.release_policy.release_unit == FIRST_SLICE_RELEASE_UNIT
    assert snapshot.release_policy.governance.to_document() == {
        "repository": "hcoona/three",
        "ref": "refs/heads/main",
        "path": (
            ".github/workflow-delivery/governance/hcoona-release-smoke-npm.json"
        ),
        "max-age-days": 90,
    }
    assert tuple(name for name, _ in snapshot.release_policy.channels) == (
        "buddy",
        "official",
    )
    assert snapshot.release_policy.policy_digest.startswith("sha256:")
    assert snapshot.reverse_index == (
        (
            "@hcoona/hcoona-release-smoke-npm",
            ("hcoona-release-smoke-npm/npm-package",),
        ),
    )
    assert snapshot.snapshot_digest.startswith("sha256:")


@pytest.mark.parametrize(
    ("relative_path", "diagnostic"),
    [
        (
            f"{PRODUCT_PATH}/workflow-delivery.release-unit.yml",
            "first-slice Release Unit descriptor is missing",
        ),
        (
            f"{PRODUCT_PATH}/workflow-delivery.quality.yml",
            (
                "Quality selection does not exist: "
                f"{PRODUCT_PATH}/workflow-delivery.quality.yml"
            ),
        ),
        (
            FIRST_SLICE_POLICY_PATH,
            f"Release policy does not exist: {FIRST_SLICE_POLICY_PATH}",
        ),
    ],
)
@pytest.mark.usefixtures("target_authoring_tree")
def test_missing_target_authoring_returns_incomplete_snapshot(
    relative_path: str,
    diagnostic: str,
) -> None:
    """Report semantic incompleteness without losing target identity."""
    (REPO_ROOT / relative_path).unlink()
    target = _commit_all(REPO_ROOT)
    context = _context(target=target)
    manifest = first_slice_provider_manifest(
        context,
        provider_producer="discover-node",
    )
    result = _result(context, manifest, repo_root=REPO_ROOT)

    snapshot = _compile(REPO_ROOT, context, manifest, result)

    assert snapshot.ready is False
    assert snapshot.context == context
    assert snapshot.project_nodes == result.project_nodes
    assert snapshot.release_units == ()
    assert snapshot.quality == ()
    assert snapshot.release_policy_path == FIRST_SLICE_POLICY_PATH
    assert snapshot.release_policy is None
    assert snapshot.reverse_index == ((FIRST_SLICE_PACKAGE, ()),)
    assert snapshot.unresolved == (diagnostic,)
    assert snapshot.snapshot_digest.startswith("sha256:")


@pytest.mark.usefixtures("target_authoring_tree")
def test_malformed_target_authoring_remains_a_hard_failure() -> None:
    """Do not downgrade malformed authoring into semantic incompleteness."""
    quality = REPO_ROOT / PRODUCT_PATH / "workflow-delivery.quality.yml"
    _write(quality, "schema: [unterminated")
    target = _commit_all(REPO_ROOT)
    context = _context(target=target)
    manifest = first_slice_provider_manifest(
        context,
        provider_producer="discover-node",
    )
    result = _result(context, manifest, repo_root=REPO_ROOT)

    admitted = _admitted_bundle(context, manifest, result)

    with pytest.raises(ValueError, match="malformed YAML authoring"):
        compile_repository_model(REPO_ROOT, context, manifest, [admitted])


@pytest.mark.usefixtures("target_authoring_tree")
def test_compiler_uses_target_authoring_not_dirty_worktree() -> None:
    """Compile from the bound target tree, not changed local authoring files."""
    descriptor_path = (
        REPO_ROOT / PRODUCT_PATH / ("workflow-delivery.release-unit.yml")
    )
    quality_path = REPO_ROOT / PRODUCT_PATH / "workflow-delivery.quality.yml"
    _write(
        descriptor_path,
        descriptor_path.read_text(encoding="utf-8").replace(
            "hcoona-release-smoke-npm",
            "dirty-worktree-unit",
        ),
    )
    _write(
        quality_path,
        quality_path.read_text(encoding="utf-8").replace(
            "node/hcoona-release-smoke-npm-v1",
            "node/dirty-worktree-v1",
        ),
    )
    context, manifest, result = _scenario(repo_root=REPO_ROOT)

    snapshot = _compile(REPO_ROOT, context, manifest, result)

    assert snapshot.release_units[0].release_unit == (
        "hcoona-release-smoke-npm"
    )
    assert snapshot.quality[0].preset == "node/hcoona-release-smoke-npm-v1"
    assert "dirty-worktree-unit" in descriptor_path.read_text(encoding="utf-8")


def test_compiler_rejects_duplicate_release_units_in_target_tree(
    tmp_path: Path,
) -> None:
    """Fail closed when the target tree declares one Release Unit twice."""
    repo = tmp_path / "duplicate-repo"
    repo.mkdir()
    _initialize_repository(repo)
    _write_first_slice_authoring(repo)
    _write(
        repo / "other/workflow-delivery.release-unit.yml",
        (
            SOURCE_REPO_ROOT
            / PRODUCT_PATH
            / "workflow-delivery.release-unit.yml"
        ).read_text(encoding="utf-8"),
    )
    _write(repo / "other/package.json", "{}\n")
    target = _commit_all(repo)
    context, manifest, result = _scenario(
        _context(target=target),
        repo_root=repo,
    )

    admitted = _admitted_bundle(context, manifest, result)

    with pytest.raises(
        ValueError,
        match="duplicate Release Unit identity: hcoona-release-smoke-npm",
    ):
        compile_repository_model(repo, context, manifest, [admitted])


@pytest.mark.usefixtures("target_authoring_tree")
def test_compiler_preserves_provider_nbgv_facts_without_recomputation() -> None:
    """Retain the original Provider fact values and native projection."""
    context, manifest, result = _scenario(repo_root=REPO_ROOT)

    snapshot = _compile(REPO_ROOT, context, manifest, result)

    assert snapshot.nbgv == result.nbgv
    assert snapshot.nbgv.canonical_version == "1.2.3"
    assert snapshot.nbgv.sem_ver2 == NPM_VERSION
    assert snapshot.nbgv.npm_package_version == NPM_VERSION
    assert snapshot.nbgv.node_api_result_digest == "sha256:" + ("a" * 64)
    assert snapshot.to_document()["nbgv"] == result.nbgv.to_document()
    nbgv_document = snapshot.to_document()["nbgv"]
    assert isinstance(nbgv_document, dict)
    assert "manifest-version" not in nbgv_document


@pytest.mark.usefixtures("target_authoring_tree")
def test_compiler_omits_run_attempt_from_live_snapshot() -> None:
    """Digest every current live request and same-revision authority binding."""
    context, manifest, result = _scenario(repo_root=REPO_ROOT)

    snapshot = _compile(REPO_ROOT, context, manifest, result)
    document = snapshot.to_document()

    assert document["context"] == {
        "request-id": "release-request-42",
        "purpose": "live-release",
        "workflow-run-id": 7101,
        "target": TARGET,
        "producer": "compile-model",
        "control": f"workflow-delivery-v3:{TARGET}",
        "catalog-digest": catalog_digest(),
        "channel": None,
        "release-unit": None,
    }
    assert document["provider-request-manifest-digest"] == (
        manifest.manifest_digest
    )
    assert document["provider-result-digests"] == [result.result_digest]
    assert document["ready"] is True


@pytest.mark.usefixtures("target_authoring_tree")
def test_simulation_rerun_compiles_distinct_snapshot() -> None:
    """Bind each simulation rerun to its own complete authority closure."""
    old_context, old_manifest, old_result = _scenario(
        _context(purpose="release-simulation", run_attempt=RUN_ATTEMPT),
        repo_root=REPO_ROOT,
    )
    new_context, new_manifest, new_result = _scenario(
        _context(purpose="release-simulation", run_attempt=REPLAY_ATTEMPT),
        repo_root=REPO_ROOT,
    )

    old_snapshot = _compile(
        REPO_ROOT,
        old_context,
        old_manifest,
        old_result,
    )
    new_snapshot = _compile(
        REPO_ROOT,
        new_context,
        new_manifest,
        new_result,
    )

    assert old_snapshot.context.run_attempt == RUN_ATTEMPT
    assert new_snapshot.context.run_attempt == REPLAY_ATTEMPT
    assert old_manifest.manifest_digest != new_manifest.manifest_digest
    assert old_result.result_digest != new_result.result_digest
    assert old_snapshot.snapshot_digest != new_snapshot.snapshot_digest
    assert old_snapshot.nbgv == new_snapshot.nbgv
    assert old_snapshot.nbgv.npm_package_version == NPM_VERSION


@pytest.mark.usefixtures("target_authoring_tree")
def test_snapshot_parser_rejects_live_run_attempt() -> None:
    """Reject a retired normal-Live run-attempt field as unknown."""
    context, manifest, result = _scenario(repo_root=REPO_ROOT)
    document = _compile(REPO_ROOT, context, manifest, result).to_document()
    context_document = cast("dict[str, JsonValue]", document["context"])
    context_document["run-attempt"] = RUN_ATTEMPT

    with pytest.raises(ValueError, match="unknown field: run-attempt"):
        repository_model_snapshot_from_document(document)


@pytest.mark.usefixtures("target_authoring_tree")
def test_snapshot_parser_requires_simulation_run_attempt() -> None:
    """Keep the simulation pass identity in every Repository Model."""
    context, manifest, result = _scenario(
        _context(purpose="release-simulation"), repo_root=REPO_ROOT
    )
    document = _compile(REPO_ROOT, context, manifest, result).to_document()
    context_document = cast("dict[str, JsonValue]", document["context"])
    del context_document["run-attempt"]

    with pytest.raises(ValueError, match="missing field: run-attempt"):
        repository_model_snapshot_from_document(document)


@pytest.mark.parametrize(
    "result_purpose",
    ["release-simulation", "live-release"],
    ids=["prior-attempt", "cross-purpose"],
)
def test_compiler_rejects_prior_attempt_and_cross_purpose_result(
    result_purpose: str,
) -> None:
    """Reject a prior-attempt or other-purpose Fact Bundle equivalent."""
    context, manifest, _ = _scenario(
        _context(purpose="release-simulation", run_attempt=REPLAY_ATTEMPT)
    )
    result_context = _context(
        purpose=result_purpose,
        run_attempt=(
            RUN_ATTEMPT if result_purpose == "release-simulation" else None
        ),
    )
    result_manifest = first_slice_provider_manifest(
        result_context,
        provider_producer="discover-node",
    )
    result = _result(result_context, result_manifest)

    admitted = _admitted_bundle(result_context, result_manifest, result)

    with pytest.raises(
        ValueError,
        match=(
            r"(?:Fact Bundle authority binding"
            r"|not bound to the exact target"
            r"|catalog digest is not the current static catalog)"
        ),
    ):
        compile_repository_model(REPO_ROOT, context, manifest, [admitted])


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("request_id", "other-request"),
        ("workflow_run_id", 7102),
        ("target", "d" * 40),
        ("producer", "other-provider-job"),
        ("control", "other-control"),
    ],
    ids=["request", "run", "target", "producer", "control"],
)
def test_compiler_rejects_differently_bound_provider_result(
    field: str,
    value: str | int,
) -> None:
    """Reject intact facts admitted for a different valid consumer context."""
    context, manifest, result = _scenario()
    admitted = _admitted_bundle(context, manifest, result)
    if field == "producer":
        producer = str(value)
    else:
        context = replace(context, **{field: value})
        producer = "discover-node"
    manifest = first_slice_provider_manifest(
        context, provider_producer=producer
    )

    with pytest.raises(ValueError, match="Fact Bundle authority binding"):
        compile_repository_model(REPO_ROOT, context, manifest, [admitted])


@pytest.mark.parametrize(
    "mutation",
    [_with_other_catalog_digest, _with_other_request_digest],
    ids=["catalog", "request-digest"],
)
def test_fact_bundle_admission_rejects_invalid_initial_request_binding(
    mutation: ProviderBindingMutation,
) -> None:
    """Initial admission rejects unsupported catalog and request identities."""
    context, manifest, result = _scenario()
    bundle, admission = _bundle_admission_inputs(
        manifest, _mutate_provider_binding(result, mutation)
    )

    with pytest.raises(ValueError, match=r"authority binding|catalog digest"):
        admit_node_provider_fact_bundle(
            bundle, context=context, manifest=manifest, admission=admission
        )


@pytest.mark.parametrize(
    "mutation",
    ["schema", "bundle-digest"],
)
def test_fact_bundle_admission_rejects_schema_and_digest_substitution(
    mutation: str,
) -> None:
    """Reject a substituted Bundle schema or trusted canonical digest."""
    context, manifest, result = _scenario()
    bundle, admission = _bundle_admission_inputs(manifest, result)
    if mutation == "schema":
        bundle = replace(bundle, schema="workflow-delivery/v3/other-bundle")
    else:
        admission = replace(
            admission,
            bundle_digest="sha256:" + ("9" * 64),
        )

    with pytest.raises(ValueError, match=r"Fact Bundle .*mismatch"):
        admit_node_provider_fact_bundle(
            bundle,
            context=context,
            manifest=manifest,
            admission=admission,
        )


@pytest.mark.parametrize(
    ("field", "message"),
    [
        (
            "request_artifact_id",
            "Fact Bundle request artifact ID binding mismatch",
        ),
        ("transport_id", "Fact Bundle transport ID binding mismatch"),
    ],
)
def test_fact_bundle_admission_rejects_different_expected_artifact_ids(
    field: str,
    message: str,
) -> None:
    """Require each valid artifact ID to match its current admission context."""
    context, manifest, result = _scenario()
    bundle, admission = _bundle_admission_inputs(manifest, result)
    changed = replace(admission, **{field: getattr(admission, field) + 1})

    with pytest.raises(ValueError, match=message):
        admit_node_provider_fact_bundle(
            bundle,
            context=context,
            manifest=manifest,
            admission=changed,
        )


def test_compiler_rejects_missing_and_duplicate_provider_results() -> None:
    """Require exactly one admitted result and no extras."""
    context, manifest, result = _scenario()
    admitted = _admitted_bundle(context, manifest, result)
    for bundles in ([], [admitted, admitted]):
        with pytest.raises(
            ValueError, match="exactly one admitted Fact Bundle"
        ):
            compile_repository_model(REPO_ROOT, context, manifest, bundles)


def test_fact_bundle_admission_rejects_unexpected_provider_identity() -> None:
    """The selected request admits only its expected Provider identity."""
    context, manifest, result = _scenario()
    unexpected = replace(
        result, provider_logical_id="node/unexpected-provider-v1"
    )
    bundle, admission = _bundle_admission_inputs(manifest, unexpected)

    with pytest.raises(ValueError, match="Provider Result identity mismatch"):
        admit_node_provider_fact_bundle(
            bundle, context=context, manifest=manifest, admission=admission
        )

    assert result.result_digest != unexpected.result_digest


@pytest.mark.parametrize(
    "mutation",
    [
        _with_nonterminal_outcome,
        _with_unresolved_workspace_graph,
    ],
    ids=["nonterminal", "unresolved"],
)
def test_fact_bundle_admission_rejects_unresolved_or_nonterminal_result(
    mutation: NodeProviderResultMutation,
) -> None:
    """Block a partial Repository Model instead of weakening closure."""
    context, manifest, valid_result = _scenario()
    invalid_result = _mutate_provider_result(valid_result, mutation)

    bundle, admission = _bundle_admission_inputs(manifest, invalid_result)

    with pytest.raises(
        ValueError,
        match="not a resolved terminal success",
    ):
        admit_node_provider_fact_bundle(
            bundle, context=context, manifest=manifest, admission=admission
        )


def test_fact_bundle_admission_rejects_missing_native_version() -> None:
    """Reject empty npmPackageVersion even when canonical semVer2 exists."""
    context, manifest, result = _scenario()
    result = replace(
        result,
        nbgv=replace(result.nbgv, npm_package_version=""),
    )

    bundle, admission = _bundle_admission_inputs(manifest, result)

    with pytest.raises(ValueError, match="native npm version"):
        admit_node_provider_fact_bundle(
            bundle, context=context, manifest=manifest, admission=admission
        )

    assert result.nbgv.sem_ver2 == NPM_VERSION
    assert result.nbgv.canonical_version == "1.2.3"


def test_compiler_rejects_incomplete_build_or_artifact_scope(
    tmp_path: Path,
) -> None:
    """Reject a descriptor that does not close its output set."""
    repo = tmp_path / "repo"
    repo.mkdir()
    _initialize_repository(repo)
    source_product = REPO_ROOT / PRODUCT_PATH
    target_product = repo / PRODUCT_PATH
    target_product.mkdir(parents=True)
    shutil.copy2(source_product / "package.json", target_product)
    shutil.copy2(
        source_product / "workflow-delivery.quality.yml",
        target_product,
    )
    descriptor = yaml.safe_load(
        (source_product / "workflow-delivery.release-unit.yml").read_text(
            encoding="utf-8"
        )
    )
    descriptor["builds"][0]["outputs"] = []
    (target_product / "workflow-delivery.release-unit.yml").write_text(
        yaml.safe_dump(descriptor, sort_keys=False),
        encoding="utf-8",
    )
    policy = repo / FIRST_SLICE_POLICY_PATH
    policy.parent.mkdir(parents=True)
    shutil.copy2(REPO_ROOT / FIRST_SLICE_POLICY_PATH, policy)
    for name in (
        "package.json",
        "pnpm-lock.yaml",
        "pnpm-workspace.yaml",
        f"{PRODUCT_PATH}/version.json",
        "version.json",
    ):
        shutil.copy2(REPO_ROOT / name, repo / name)
    target = _commit_all(repo)
    context, manifest, result = _scenario(
        _context(target=target),
        repo_root=repo,
    )

    admitted = _admitted_bundle(context, manifest, result)

    with pytest.raises(ValueError, match="build has no outputs"):
        compile_repository_model(repo, context, manifest, [admitted])

    assert result.outcome == "success"
    assert result.nbgv.npm_package_version == NPM_VERSION


@pytest.mark.usefixtures("target_authoring_tree")
def test_compiler_does_not_create_attempt_or_simulation_identity() -> None:
    """Stop at the pre-identity Repository Model boundary."""
    context, manifest, result = _scenario(repo_root=REPO_ROOT)

    snapshot = _compile(REPO_ROOT, context, manifest, result)
    document = snapshot.to_document()

    assert "attempt" not in document
    assert "simulation-identity" not in document
    assert "release-execution" not in document
    assert set(document) == {
        "schema",
        "context",
        "provider-request-manifest-digest",
        "provider-result-digests",
        "project-nodes",
        "release-units",
        "quality",
        "release-policy-path",
        "release-policy",
        "nbgv",
        "reverse-index",
        "unresolved",
        "ready",
    }


@pytest.mark.usefixtures("target_authoring_tree")
def test_simulation_snapshot_binds_selection_without_future_identity() -> None:
    """Compile simulation without inventing a future Simulation Identity."""
    context, manifest, result = _scenario(
        _context(purpose="release-simulation"), repo_root=REPO_ROOT
    )

    snapshot = _compile(REPO_ROOT, context, manifest, result)

    assert snapshot.context.purpose == "release-simulation"
    assert snapshot.context.channel == "official"
    assert snapshot.context.release_unit == "hcoona-release-smoke-npm"
    assert "simulation-identity" not in snapshot.to_document()
    assert snapshot.nbgv.npm_package_version == NPM_VERSION


@pytest.mark.usefixtures("target_authoring_tree")
def test_manifest_is_closed_before_provider_execution() -> None:
    """Digest the exact Provider implementation and current authority inputs."""
    context, manifest, result = _scenario(repo_root=REPO_ROOT)
    request = manifest.requests[0]

    assert request.entry_id == "node-first-slice"
    assert request.provider_logical_id == PROVIDER_LOGICAL_ID
    assert request.provider_implementation_id == PROVIDER_IMPLEMENTATION_ID
    assert request.execution_mode == PROVIDER_EXECUTION_MODE
    assert request.producer == "discover-node"
    assert request.request_digest.startswith("sha256:")
    assert request.request_digest == canonical_sha256(
        _expected_provider_request_document(
            context,
            provider_producer="discover-node",
        )
    )
    assert request.expected_result_identity == (
        "node/pnpm-nbgv-v1:release-request-42"
    )
    manifest_context = manifest.to_document()["context"]
    assert isinstance(manifest_context, dict)
    assert "run-attempt" not in manifest_context
    simulation_manifest = first_slice_provider_manifest(
        _context(purpose="release-simulation"),
        provider_producer="discover-node",
    )
    simulation_context = simulation_manifest.to_document()["context"]
    assert isinstance(simulation_context, dict)
    assert simulation_context["run-attempt"] == RUN_ATTEMPT
    assert manifest.manifest_digest.startswith("sha256:")
    assert (
        _compile(REPO_ROOT, context, manifest, result).manifest_digest
        == manifest.manifest_digest
    )


def test_compiler_rejects_manifest_entry_id_substitution() -> None:
    """Reject a request that is not the exact approved first-slice entry."""
    context, manifest, result = _scenario()
    admitted = _admitted_bundle(context, manifest, result)
    substituted = replace(manifest.requests[0], entry_id="other-node")
    manifest = replace(manifest, requests=(substituted,))

    with pytest.raises(
        ValueError,
        match="not the canonical first slice",
    ):
        compile_repository_model(REPO_ROOT, context, manifest, [admitted])


@pytest.mark.parametrize(
    "replacement_digest",
    [
        "sha256:" + ("b" * 64),
        "stale",
    ],
    ids=["arbitrary-well-shaped", "stale-prior-attempt"],
)
def test_compiler_rejects_manifest_digest_not_bound_to_canonical_preimage(
    replacement_digest: str,
) -> None:
    """Validate the current canonical manifest before reusing admitted facts."""
    context, manifest, result = _scenario(
        _context(purpose="release-simulation", run_attempt=REPLAY_ATTEMPT)
    )
    admitted = _admitted_bundle(context, manifest, result)
    if replacement_digest == "stale":
        _, stale_manifest, _ = _scenario(
            _context(purpose="release-simulation", run_attempt=RUN_ATTEMPT)
        )
        replacement_digest = stale_manifest.requests[0].request_digest
    substituted = replace(
        manifest.requests[0],
        request_digest=replacement_digest,
    )
    manifest = replace(manifest, requests=(substituted,))

    with pytest.raises(
        ValueError,
        match="digest is not bound to the canonical first-slice request",
    ):
        compile_repository_model(REPO_ROOT, context, manifest, [admitted])


@pytest.mark.parametrize(
    "mutation",
    [
        _with_other_provider_logical_id,
        _with_other_provider_implementation_id,
        _with_side_effecting_execution_mode,
    ],
    ids=["logical-id", "implementation-id", "execution-mode"],
)
def test_compiler_rejects_manifest_implementation_substitution(
    mutation: ProviderRequestMutation,
) -> None:
    """Reject every target-selected Provider implementation primitive."""
    context, manifest, result = _scenario()
    admitted = _admitted_bundle(context, manifest, result)
    substituted = _mutate_provider_request(manifest.requests[0], mutation)
    manifest = replace(manifest, requests=(substituted,))

    with pytest.raises(
        ValueError,
        match="unsupported Provider implementation",
    ):
        compile_repository_model(REPO_ROOT, context, manifest, [admitted])


@pytest.mark.parametrize(
    "mutation",
    [
        _with_other_project_id,
        _with_other_package_name,
        _with_other_project_path,
        _with_other_manifest_path,
    ],
    ids=["project-id", "package-name", "path", "manifest"],
)
@pytest.mark.usefixtures("target_authoring_tree")
def test_compiler_rejects_substituted_first_slice_project_node(
    mutation: NodeProviderResultMutation,
) -> None:
    """Compare the admitted Project Node with actual target authoring."""
    context, manifest, result = _scenario(repo_root=REPO_ROOT)
    result = _mutate_provider_result(result, mutation)

    admitted = _admitted_bundle(context, manifest, result)

    with pytest.raises(
        ValueError,
        match=(
            r"(?:Project Node identity/path"
            r"|does not resolve to the Project Node)"
        ),
    ):
        compile_repository_model(REPO_ROOT, context, manifest, [admitted])


@pytest.mark.parametrize(
    "mutation",
    [_without_project_nodes, _with_duplicate_project_node],
    ids=["missing", "duplicate"],
)
@pytest.mark.usefixtures("target_authoring_tree")
def test_compiler_requires_exactly_one_project_node(
    mutation: NodeProviderResultMutation,
) -> None:
    """Reject missing and duplicate first-slice Project Nodes."""
    context, manifest, result = _scenario(repo_root=REPO_ROOT)
    result = _mutate_provider_result(result, mutation)

    admitted = _admitted_bundle(context, manifest, result)

    with pytest.raises(ValueError, match="exactly one Project Node"):
        compile_repository_model(REPO_ROOT, context, manifest, [admitted])


def test_fact_bundle_admission_rejects_nbgv_target_substitution() -> None:
    """Reject supplied NBGV facts for a different target at admission."""
    context, manifest, result = _scenario()
    result = replace(
        result,
        nbgv=replace(result.nbgv, git_commit_id="d" * 40),
    )

    bundle, admission = _bundle_admission_inputs(manifest, result)

    with pytest.raises(ValueError, match="exact target"):
        admit_node_provider_fact_bundle(
            bundle, context=context, manifest=manifest, admission=admission
        )


def test_manifest_rejects_invalid_purpose_and_simulation_selection() -> None:
    """Close purpose-specific context before Provider execution."""
    live = _context()
    simulation = _context(purpose="release-simulation")
    invalid_contexts = (
        replace(live, purpose="unknown"),
        replace(live, channel="official"),
        replace(simulation, channel=None),
        replace(simulation, channel="preview"),
        replace(simulation, release_unit="other-unit"),
    )

    for context in invalid_contexts:
        with pytest.raises((TypeError, ValueError), match="compilation"):
            first_slice_provider_manifest(
                context,
                provider_producer="discover-node",
            )

    with pytest.raises(TypeError, match="producer"):
        first_slice_provider_manifest(live, provider_producer="")


@pytest.mark.parametrize(
    "workspace_dependencies",
    [
        ("@hcoona/linked-one",),
        (
            "@hcoona/linked-one",
            "@hcoona/linked-three",
            "@hcoona/linked-two",
        ),
    ],
    ids=["singleton", "multiple"],
)
@pytest.mark.usefixtures("target_authoring_tree")
def test_compiler_rejects_nonempty_workspace_dependency_set(
    workspace_dependencies: tuple[str, ...],
) -> None:
    """Reject workspace closure while commit 3 permits one Project Node."""
    context, manifest, valid_result = _scenario(repo_root=REPO_ROOT)
    empty_snapshot = _compile(
        REPO_ROOT,
        context,
        manifest,
        valid_result,
    )
    result = _with_workspace_dependencies(
        valid_result,
        workspace_dependencies,
    )

    assert empty_snapshot.ready
    assert empty_snapshot.project_nodes[0].workspace_dependencies == ()
    assert result.project_nodes[0].workspace_dependencies == (
        workspace_dependencies
    )

    admitted = _admitted_bundle(context, manifest, result)

    with pytest.raises(
        ValueError,
        match=(
            r"commit 3 permits exactly one Project Node"
            r".*no workspace closure"
        ),
    ):
        compile_repository_model(REPO_ROOT, context, manifest, [admitted])


@pytest.fixture
def valid_compilation_inputs() -> tuple[
    CompilationContext,
    ProviderRequestManifest,
]:
    """Return a context-bound request without reading repository authoring."""
    context = _context()
    manifest = first_slice_provider_manifest(
        context,
        provider_producer="discover-node",
    )
    return context, manifest


@pytest.fixture
def valid_node_provider_result(
    valid_compilation_inputs: tuple[
        CompilationContext,
        ProviderRequestManifest,
    ],
) -> NodeProviderResult:
    """Return literal admission inputs independent of Provider execution."""
    context, manifest = valid_compilation_inputs
    return _result(context, manifest)


def _assert_fact_bundle_admission_rejected(
    context: CompilationContext,
    manifest: ProviderRequestManifest,
    result: NodeProviderResult,
    *,
    match: str,
) -> None:
    bundle, admission = _bundle_admission_inputs(manifest, result)

    with pytest.raises((TypeError, ValueError), match=match):
        admit_node_provider_fact_bundle(
            bundle, context=context, manifest=manifest, admission=admission
        )


@pytest.mark.parametrize(
    "execution_class",
    [
        "target-evaluation/privileged-v1",
        "control/unprivileged-v1",
        "target-execution/unprivileged-v1",
        "",
        "unknown/execution-class-v1",
    ],
    ids=[
        "privileged",
        "control",
        "target-execution",
        "empty",
        "unknown",
    ],
)
def test_fact_bundle_admission_rejects_substituted_provider_execution_class(
    valid_compilation_inputs: tuple[
        CompilationContext,
        ProviderRequestManifest,
    ],
    valid_node_provider_result: NodeProviderResult,
    execution_class: str,
) -> None:
    """Accept only target-evaluation/unprivileged-v1 execution evidence."""
    context, manifest = valid_compilation_inputs
    forged_result = replace(
        valid_node_provider_result,
        execution_class=execution_class,
    )

    _assert_fact_bundle_admission_rejected(
        context,
        manifest,
        forged_result,
        match=r"execution.class",
    )


@pytest.mark.parametrize(
    "toolchain",
    [
        (("pnpm", "11.21.0"),),
        (("node", "v24.14.0"),),
        (
            ("node", "v24.14.0"),
            ("pnpm", "11.21.0"),
            ("python", "3.13.5"),
        ),
        (
            ("node", "v24.14.0"),
            ("node", "v22.17.0"),
            ("pnpm", "11.21.0"),
        ),
        (
            ("node", "v24.14.0"),
            ("pnpm", "11.21.0"),
            ("pnpm", "10.12.1"),
        ),
        (("pnpm", "11.21.0"), ("node", "v24.14.0")),
        (("nodejs", "v24.14.0"), ("pnpm", "11.21.0")),
        (("node", "v24.14.0"), ("pnpm-cli", "11.21.0")),
        (("", "v24.14.0"), ("pnpm", "11.21.0")),
        (("node", ""), ("pnpm", "11.21.0")),
        (("node", " v24.14.0"), ("pnpm", "11.21.0")),
        (("node", "v24.14.0"), ("pnpm", "11.21.0 ")),
        (("node", "v24.14.0"), ("pnpm", " \t")),
    ],
    ids=[
        "missing-node",
        "missing-pnpm",
        "extra-entry",
        "duplicate-node",
        "duplicate-pnpm",
        "reordered",
        "renamed-node",
        "renamed-pnpm",
        "empty-name",
        "empty-version",
        "padded-node-version",
        "padded-pnpm-version",
        "whitespace-version",
    ],
)
def test_provider_toolchain_rejects_noncanonical_values(
    toolchain: tuple[tuple[str, str], ...],
) -> None:
    """Own the closed toolchain value matrix at its intrinsic validator."""
    with pytest.raises((TypeError, ValueError), match="toolchain"):
        validate_provider_toolchain(toolchain)


def test_fact_bundle_admission_rejects_noncanonical_provider_toolchain(
    valid_compilation_inputs: tuple[
        CompilationContext,
        ProviderRequestManifest,
    ],
    valid_node_provider_result: NodeProviderResult,
) -> None:
    """Connect the intrinsic toolchain rule to actual Bundle admission."""
    context, manifest = valid_compilation_inputs
    validate_provider_toolchain((("node", "v24.14.0"), ("pnpm", "11.21.0")))
    forged_result = replace(
        valid_node_provider_result,
        toolchain=(("pnpm", "11.21.0"), ("node", "v24.14.0")),
    )

    _assert_fact_bundle_admission_rejected(
        context,
        manifest,
        forged_result,
        match="toolchain",
    )


def test_fact_bundle_admission_rejects_provider_implementation_mismatch(
    valid_compilation_inputs: tuple[
        CompilationContext,
        ProviderRequestManifest,
    ],
    valid_node_provider_result: NodeProviderResult,
) -> None:
    """Reject a result whose implementation differs from its request."""
    context, manifest = valid_compilation_inputs
    forged_result = replace(
        valid_node_provider_result,
        provider_implementation_id="forged/provider-v1",
    )

    _assert_fact_bundle_admission_rejected(
        context,
        manifest,
        forged_result,
        match="identity mismatch",
    )


@pytest.mark.parametrize(
    ("canonical_version", "npm_package_version"),
    [
        ("1.2", "1.2.3+build.7"),
        ("1.2.3.4", "1.2.3-beta.2+build.7"),
    ],
    ids=["two-component-and-build", "four-component-and-prerelease-build"],
)
@pytest.mark.usefixtures("target_authoring_tree")
def test_compiler_accepts_exact_provider_result_and_validates_snapshot(
    valid_compilation_inputs: tuple[
        CompilationContext,
        ProviderRequestManifest,
    ],
    canonical_version: str,
    npm_package_version: str,
) -> None:
    """Compile and directly validate positive canonical grammar boundaries."""
    from three_workflow_delivery_v3.repository.compiler import (  # noqa: PLC0415
        validate_first_slice_repository_model_snapshot,
    )

    context, manifest = valid_compilation_inputs
    valid_node_provider_result = _result(context, manifest, repo_root=REPO_ROOT)
    selected_result = replace(
        valid_node_provider_result,
        nbgv=replace(
            valid_node_provider_result.nbgv,
            canonical_version=canonical_version,
            npm_package_version=npm_package_version,
        ),
    )

    snapshot = _compile(
        REPO_ROOT,
        context,
        manifest,
        selected_result,
    )

    assert snapshot.ready is True
    assert snapshot.context.target == selected_result.binding.target
    assert snapshot.nbgv == selected_result.nbgv
    assert snapshot.nbgv.canonical_version == canonical_version
    assert snapshot.nbgv.npm_package_version == npm_package_version
    assert snapshot.nbgv.to_document() == selected_result.nbgv.to_document()
    assert selected_result.execution_class == (
        "target-evaluation/unprivileged-v1"
    )
    assert selected_result.toolchain == (
        ("node", "v24.14.0"),
        ("pnpm", "11.21.0"),
    )
    assert snapshot.provider_result_digests == (selected_result.result_digest,)
    assert snapshot.unresolved == ()

    validate_first_slice_repository_model_snapshot(snapshot)


def test_fact_bundle_admission_rejects_private_project() -> None:
    """Selected native facts must describe a non-private publishable project."""
    context, manifest, result = _scenario()
    bundle, admission = _bundle_admission_inputs(
        manifest, _with_private_project(result)
    )

    with pytest.raises(
        ValueError, match="Project Node private must be exactly false"
    ):
        admit_node_provider_fact_bundle(
            bundle, context=context, manifest=manifest, admission=admission
        )


@pytest.mark.usefixtures("target_authoring_tree")
def test_compiler_rehashes_internally_consistent_foreign_inputs() -> None:
    """Independently compare admitted supplied facts with actual Git bytes."""
    context, manifest, result = _scenario(repo_root=REPO_ROOT)
    inputs = tuple(
        replace(item, content_digest="sha256:" + "9" * 64)
        if item.path == "pnpm-lock.yaml"
        else item
        for item in result.global_inputs
    )
    changed = replace(
        result,
        global_inputs=inputs,
        configuration_digest=canonical_sha256(
            {
                "schema": "workflow-delivery/v3/node-provider-configuration",
                "global-inputs": [item.to_document() for item in inputs],
            }
        ),
    )
    admitted = _admitted_bundle(context, manifest, changed)

    with pytest.raises(
        ValueError, match="input digests do not match the exact target"
    ):
        compile_repository_model(REPO_ROOT, context, manifest, [admitted])
