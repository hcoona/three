"""Scenario tests for the purpose-bound Repository Model compiler."""

from __future__ import annotations

import hashlib
import shutil
import subprocess
import sys
from collections.abc import Callable
from dataclasses import replace
from pathlib import Path
from typing import Any, cast

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
    create_node_provider_fact_bundle,
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
type CheckoutEvidenceMutation = Callable[[CheckoutEvidence], CheckoutEvidence]
type NodeProviderResultMutation = Callable[
    [NodeProviderResult], NodeProviderResult
]
type ProviderRequestMutation = Callable[[ProviderRequest], ProviderRequest]


def _mutate_provider_binding(
    result: NodeProviderResult,
    mutation: ProviderBindingMutation,
) -> NodeProviderResult:
    return replace(result, binding=mutation(result.binding))


def _mutate_checkout_evidence(
    result: NodeProviderResult,
    mutation: CheckoutEvidenceMutation,
) -> NodeProviderResult:
    return replace(result, checkout=mutation(result.checkout))


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


def _with_other_request_id(
    binding: ProviderBinding,
) -> ProviderBinding:
    return replace(binding, request_id="other-request")


def _with_other_workflow_run_id(
    binding: ProviderBinding,
) -> ProviderBinding:
    return replace(binding, workflow_run_id=7102)


def _with_other_binding_target(
    binding: ProviderBinding,
) -> ProviderBinding:
    return replace(binding, target="d" * 40)


def _with_other_provider_producer(
    binding: ProviderBinding,
) -> ProviderBinding:
    return replace(binding, producer="other-provider-job")


def _with_other_control(
    binding: ProviderBinding,
) -> ProviderBinding:
    return replace(binding, control="other-control")


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


def _with_other_checkout_target(
    checkout: CheckoutEvidence,
) -> CheckoutEvidence:
    return replace(checkout, target="d" * 40)


def _with_other_checkout_head(
    checkout: CheckoutEvidence,
) -> CheckoutEvidence:
    return replace(checkout, head="d" * 40)


def _with_shallow_checkout(
    checkout: CheckoutEvidence,
) -> CheckoutEvidence:
    return replace(checkout, shallow=True)


def _with_incomplete_ancestry(
    checkout: CheckoutEvidence,
) -> CheckoutEvidence:
    return replace(checkout, ancestry_complete=False)


def _with_incomplete_tags(
    checkout: CheckoutEvidence,
) -> CheckoutEvidence:
    return replace(checkout, tags_complete=False)


def _with_persisted_credentials(
    checkout: CheckoutEvidence,
) -> CheckoutEvidence:
    return replace(checkout, credentials_persisted=True)


def _with_other_authoritative_remote(
    checkout: CheckoutEvidence,
) -> CheckoutEvidence:
    return replace(checkout, authoritative_remote="upstream")


def _with_empty_authoritative_remote_url(
    checkout: CheckoutEvidence,
) -> CheckoutEvidence:
    return replace(checkout, authoritative_remote_url="")


def _with_other_tag_refspec(
    checkout: CheckoutEvidence,
) -> CheckoutEvidence:
    return replace(checkout, tag_refspec="refs/tags/release/*:refs/tags/*")


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


@pytest.fixture(autouse=True)
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
    manifest_digest, configuration_digest, global_inputs = _provider_inputs(
        repo_root or REPO_ROOT,
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
    bundle = create_node_provider_fact_bundle(
        result,
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


def test_compiler_closes_first_slice_repository_model() -> None:
    """Close Project Node, Release Unit, output, Quality, and reverse index."""
    context, manifest, result = _scenario()

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
    result = _result(context, manifest)

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
    result = _result(context, manifest)

    with pytest.raises(ValueError, match="malformed YAML authoring"):
        _compile(REPO_ROOT, context, manifest, result)


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
    context, manifest, result = _scenario()

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

    with pytest.raises(
        ValueError,
        match="duplicate Release Unit identity: hcoona-release-smoke-npm",
    ):
        _compile(repo, context, manifest, result)


def test_compiler_preserves_provider_nbgv_facts_without_recomputation() -> None:
    """Retain the exact Provider fact object and native projection."""
    context, manifest, result = _scenario()

    snapshot = _compile(REPO_ROOT, context, manifest, result)

    assert snapshot.nbgv is result.nbgv
    assert snapshot.nbgv.canonical_version == "1.2.3"
    assert snapshot.nbgv.sem_ver2 == NPM_VERSION
    assert snapshot.nbgv.npm_package_version == NPM_VERSION
    assert snapshot.nbgv.node_api_result_digest == "sha256:" + ("a" * 64)
    assert snapshot.to_document()["nbgv"] == result.nbgv.to_document()
    nbgv_document = snapshot.to_document()["nbgv"]
    assert isinstance(nbgv_document, dict)
    assert "manifest-version" not in nbgv_document


def test_compiler_omits_run_attempt_from_live_snapshot() -> None:
    """Digest every current live request and same-revision authority binding."""
    context, manifest, result = _scenario()

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


def test_simulation_rerun_compiles_distinct_snapshot() -> None:
    """Bind each simulation rerun to its own complete authority closure."""
    old_context, old_manifest, old_result = _scenario(
        _context(purpose="release-simulation", run_attempt=RUN_ATTEMPT)
    )
    new_context, new_manifest, new_result = _scenario(
        _context(purpose="release-simulation", run_attempt=REPLAY_ATTEMPT)
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


def test_snapshot_parser_rejects_live_run_attempt() -> None:
    """Reject a retired normal-Live run-attempt field as unknown."""
    context, manifest, result = _scenario()
    document = _compile(REPO_ROOT, context, manifest, result).to_document()
    context_document = cast("dict[str, JsonValue]", document["context"])
    context_document["run-attempt"] = RUN_ATTEMPT

    with pytest.raises(ValueError, match="unknown field: run-attempt"):
        repository_model_snapshot_from_document(document)


def test_snapshot_parser_requires_simulation_run_attempt() -> None:
    """Keep the simulation pass identity in every Repository Model."""
    context, manifest, result = _scenario(
        _context(purpose="release-simulation")
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

    with pytest.raises(
        ValueError,
        match=(
            r"(?:Fact Bundle authority binding"
            r"|not bound to the exact target"
            r"|catalog digest is not the current static catalog)"
        ),
    ):
        _compile(REPO_ROOT, context, manifest, result)


@pytest.mark.parametrize(
    "mutation",
    [
        _with_other_request_id,
        _with_other_workflow_run_id,
        _with_other_binding_target,
        _with_other_provider_producer,
        _with_other_control,
        _with_other_catalog_digest,
        _with_other_request_digest,
    ],
    ids=[
        "request",
        "run",
        "target",
        "producer",
        "control",
        "catalog",
        "request-digest",
    ],
)
def test_compiler_rejects_differently_bound_provider_result(
    mutation: ProviderBindingMutation,
) -> None:
    """Reject every differently bound Provider Result authority field."""
    context, manifest, result = _scenario()
    result = _mutate_provider_binding(result, mutation)

    with pytest.raises(
        ValueError,
        match=(
            r"(?:Fact Bundle authority binding"
            r"|not bound to the exact target"
            r"|catalog digest is not the current static catalog)"
        ),
    ):
        _compile(REPO_ROOT, context, manifest, result)


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
    ("field", "surrogate"),
    [
        ("schema", True),
        ("binding", {}),
        ("manifest_digest", True),
        ("manifest_entry_id", 1),
        ("request_artifact_id", True),
        ("request_artifact_digest", 1),
        ("provider_result", {}),
        ("provider_result_digest", True),
        ("transport_id", 1.0),
        ("transport_digest", False),
    ],
)
def test_fact_bundle_admission_requires_exact_runtime_field_types(
    field: str,
    surrogate: object,
) -> None:
    """Reject Boolean, number, and container surrogates in every Bundle role."""
    context, manifest, result = _scenario()
    bundle, admission = _bundle_admission_inputs(manifest, result)
    forged = replace(bundle, **{field: cast("Any", surrogate)})

    with pytest.raises((TypeError, ValueError)):
        admit_node_provider_fact_bundle(
            forged,
            context=context,
            manifest=manifest,
            admission=admission,
        )


@pytest.mark.parametrize(
    ("field", "surrogate"),
    [
        ("request_artifact_id", True),
        ("request_artifact_digest", 1),
        ("transport_id", 1.0),
        ("transport_digest", False),
        ("bundle_digest", True),
    ],
)
def test_fact_bundle_admission_context_requires_exact_runtime_field_types(
    field: str,
    surrogate: object,
) -> None:
    """Reject trusted-context type surrogates before Bundle comparison."""
    context, manifest, result = _scenario()
    bundle, admission = _bundle_admission_inputs(manifest, result)
    forged = replace(admission, **{field: cast("Any", surrogate)})

    with pytest.raises((TypeError, ValueError)):
        admit_node_provider_fact_bundle(
            bundle,
            context=context,
            manifest=manifest,
            admission=forged,
        )


def test_compiler_rejects_missing_duplicate_and_unexpected_provider_results() -> (  # noqa: E501
    None
):
    """Require exactly one expected result and no extras."""
    context, manifest, result = _scenario()
    unexpected = replace(
        result,
        provider_logical_id="node/unexpected-provider-v1",
    )

    admitted = _admitted_bundle(context, manifest, result)
    for bundles in ([], [admitted, admitted]):
        with pytest.raises(
            ValueError,
            match="exactly one admitted Fact Bundle",
        ):
            compile_repository_model(
                REPO_ROOT,
                context,
                manifest,
                bundles,
            )
    with pytest.raises(ValueError, match="Provider Result identity mismatch"):
        _admitted_bundle(context, manifest, unexpected)
    with pytest.raises(TypeError, match="admitted Fact Bundle"):
        compile_repository_model(
            REPO_ROOT,
            context,
            manifest,
            [result],  # type: ignore[list-item]
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
def test_compiler_rejects_unresolved_or_nonterminal_provider_result(
    mutation: NodeProviderResultMutation,
) -> None:
    """Block a partial Repository Model instead of weakening closure."""
    context, manifest, valid_result = _scenario()
    invalid_result = _mutate_provider_result(valid_result, mutation)

    with pytest.raises(
        ValueError,
        match="not a resolved terminal success",
    ):
        _compile(REPO_ROOT, context, manifest, invalid_result)


def test_compiler_rejects_missing_native_projection_without_fallback() -> None:
    """Reject empty npmPackageVersion even when canonical semVer2 exists."""
    context, manifest, result = _scenario()
    result = replace(
        result,
        nbgv=replace(result.nbgv, npm_package_version=""),
    )

    with pytest.raises(ValueError, match="native npm version"):
        _compile(REPO_ROOT, context, manifest, result)

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

    with pytest.raises(ValueError, match="build has no outputs"):
        _compile(repo, context, manifest, result)

    assert result.outcome == "success"
    assert result.nbgv.npm_package_version == NPM_VERSION


def test_compiler_does_not_create_attempt_or_simulation_identity() -> None:
    """Stop at the pre-identity Repository Model boundary."""
    context, manifest, result = _scenario()

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


def test_simulation_snapshot_binds_selection_without_future_identity() -> None:
    """Compile simulation without inventing a future Simulation Identity."""
    context, manifest, result = _scenario(
        _context(purpose="release-simulation")
    )

    snapshot = _compile(REPO_ROOT, context, manifest, result)

    assert snapshot.context.purpose == "release-simulation"
    assert snapshot.context.channel == "official"
    assert snapshot.context.release_unit == "hcoona-release-smoke-npm"
    assert "simulation-identity" not in snapshot.to_document()
    assert snapshot.nbgv.npm_package_version == NPM_VERSION


def test_manifest_is_closed_before_provider_execution() -> None:
    """Digest the exact Provider implementation and current authority inputs."""
    context, manifest, result = _scenario()
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
    substituted = replace(manifest.requests[0], entry_id="other-node")
    manifest = replace(manifest, requests=(substituted,))

    with pytest.raises(
        ValueError,
        match="not the canonical first slice",
    ):
        _compile(REPO_ROOT, context, manifest, result)


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
    """Reject stale or arbitrary digests even when the result repeats them."""
    context, manifest, _ = _scenario(
        _context(purpose="release-simulation", run_attempt=REPLAY_ATTEMPT)
    )
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
    result = _result(context, manifest)

    with pytest.raises(
        ValueError,
        match="digest is not bound to the canonical first-slice request",
    ):
        _compile(REPO_ROOT, context, manifest, result)


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
    substituted = _mutate_provider_request(manifest.requests[0], mutation)
    manifest = replace(manifest, requests=(substituted,))

    with pytest.raises(
        ValueError,
        match="unsupported Provider implementation",
    ):
        _compile(REPO_ROOT, context, manifest, result)


@pytest.mark.parametrize(
    "mutation",
    [
        _with_other_checkout_target,
        _with_other_checkout_head,
        _with_shallow_checkout,
        _with_incomplete_ancestry,
        _with_incomplete_tags,
        _with_persisted_credentials,
        _with_other_authoritative_remote,
        _with_empty_authoritative_remote_url,
        _with_other_tag_refspec,
    ],
    ids=[
        "target",
        "head",
        "shallow",
        "ancestry",
        "tags",
        "credentials",
        "remote",
        "remote-url",
        "tag-refspec",
    ],
)
def test_compiler_revalidates_every_checkout_evidence_primitive(
    mutation: CheckoutEvidenceMutation,
) -> None:
    """Reject fabricated or incomplete Provider checkout evidence."""
    context, manifest, result = _scenario()
    result = _mutate_checkout_evidence(result, mutation)

    with pytest.raises(
        (TypeError, ValueError),
        match=(
            r"(?:full-history checkout evidence"
            r"|checkout authoritative_remote_url)"
        ),
    ):
        _compile(REPO_ROOT, context, manifest, result)


@pytest.mark.parametrize(
    "mutation",
    [
        _with_other_project_id,
        _with_other_package_name,
        _with_other_project_path,
        _with_other_manifest_path,
        _with_private_project,
    ],
    ids=["project-id", "package-name", "path", "manifest", "private"],
)
def test_compiler_rejects_substituted_first_slice_project_node(
    mutation: NodeProviderResultMutation,
) -> None:
    """Bind the compiled build to the exact non-private Project Node."""
    context, manifest, result = _scenario()
    result = _mutate_provider_result(result, mutation)

    with pytest.raises(
        ValueError,
        match=(
            r"(?:Project Node (?:identity/path|cannot be private)"
            r"|Project Node private must be exactly false"
            r"|does not resolve to the Project Node)"
        ),
    ):
        _compile(REPO_ROOT, context, manifest, result)


@pytest.mark.parametrize(
    "mutation",
    [_without_project_nodes, _with_duplicate_project_node],
    ids=["missing", "duplicate"],
)
def test_compiler_requires_exactly_one_project_node(
    mutation: NodeProviderResultMutation,
) -> None:
    """Reject missing and duplicate first-slice Project Nodes."""
    context, manifest, result = _scenario()
    result = _mutate_provider_result(result, mutation)

    with pytest.raises(ValueError, match="exactly one Project Node"):
        _compile(REPO_ROOT, context, manifest, result)


def test_compiler_rejects_nbgv_target_substitution() -> None:
    """Revalidate the frozen NBGV target at the compiler boundary."""
    context, manifest, result = _scenario()
    result = replace(
        result,
        nbgv=replace(result.nbgv, git_commit_id="d" * 40),
    )

    with pytest.raises(ValueError, match="exact target"):
        _compile(REPO_ROOT, context, manifest, result)


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
def test_compiler_rejects_nonempty_workspace_dependency_set(
    workspace_dependencies: tuple[str, ...],
) -> None:
    """Reject workspace closure while commit 3 permits one Project Node."""
    context, manifest, valid_result = _scenario()
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

    with pytest.raises(
        ValueError,
        match=(
            r"commit 3 permits exactly one Project Node"
            r".*no workspace closure"
        ),
    ):
        _compile(REPO_ROOT, context, manifest, result)


@pytest.fixture
def valid_compilation_inputs() -> tuple[
    CompilationContext,
    ProviderRequestManifest,
]:
    """Return exact target authoring and request inputs for Phase 3."""
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
    """Return a literal complete result independent of Provider execution."""
    context, manifest = valid_compilation_inputs
    return _result(context, manifest)


def _assert_phase3_compile_rejected(
    context: CompilationContext,
    manifest: ProviderRequestManifest,
    result: NodeProviderResult,
    *,
    match: str,
) -> None:
    snapshot = None

    with pytest.raises((TypeError, ValueError), match=match):
        snapshot = _compile(REPO_ROOT, context, manifest, result)

    assert snapshot is None


@pytest.mark.parametrize(
    ("field", "value", "message"),
    [
        ("canonical_version", "", r"(?:canonical|NBGV.*version)"),
        ("canonical_version", 123, r"(?:canonical|NBGV.*version)"),
        ("canonical_version", "1", r"(?:canonical|NBGV.*version)"),
        ("canonical_version", "1.2.3.4.5", r"(?:canonical|NBGV.*version)"),
        ("canonical_version", "01.2.3", r"(?:canonical|NBGV.*version)"),
        ("canonical_version", "1.02.3", r"(?:canonical|NBGV.*version)"),
        ("canonical_version", "1.-2.3", r"(?:canonical|NBGV.*version)"),
        ("canonical_version", "1.two.3", r"(?:canonical|NBGV.*version)"),
        ("canonical_version", " 1.2.3", r"(?:canonical|NBGV.*version)"),
        ("sem_ver1", "", r"(?:semVer1|sem_ver1)"),
        ("sem_ver1", 123, r"(?:semVer1|sem_ver1)"),
        ("sem_ver2", "", r"(?:semVer2|sem_ver2)"),
        ("sem_ver2", 123, r"(?:semVer2|sem_ver2)"),
        (
            "npm_package_version",
            "",
            r"(?:npmPackageVersion|npm_package_version)",
        ),
        (
            "npm_package_version",
            "^1.2.3",
            r"(?:npmPackageVersion|npm_package_version)",
        ),
        (
            "npm_package_version",
            "latest",
            r"(?:npmPackageVersion|npm_package_version)",
        ),
        (
            "npm_package_version",
            "https://registry.npmjs.org/package",
            r"(?:npmPackageVersion|npm_package_version)",
        ),
        (
            "npm_package_version",
            "v1.2.3",
            r"(?:npmPackageVersion|npm_package_version)",
        ),
        (
            "npm_package_version",
            " 1.2.3",
            r"(?:npmPackageVersion|npm_package_version)",
        ),
        (
            "npm_package_version",
            "1.2",
            r"(?:npmPackageVersion|npm_package_version)",
        ),
        (
            "npm_package_version",
            "01.2.3",
            r"(?:npmPackageVersion|npm_package_version)",
        ),
        (
            "npm_package_version",
            "1.2.3-01",
            r"(?:npmPackageVersion|npm_package_version)",
        ),
        (
            "npm_package_version",
            "1.2.3+",
            r"(?:npmPackageVersion|npm_package_version)",
        ),
        (
            "npm_package_version",
            "1.2.3-alpha..1",
            r"(?:npmPackageVersion|npm_package_version)",
        ),
        (
            "npm_package_version",
            123,
            r"(?:npmPackageVersion|npm_package_version)",
        ),
        (
            "npm_package_version",
            None,
            r"(?:npmPackageVersion|npm_package_version)",
        ),
        (
            "git_commit_id",
            "e" * 39,
            r"(?:gitCommitId|git_commit_id|compilation target|exact target)",
        ),
        (
            "git_commit_id",
            "E" * 40,
            r"(?:gitCommitId|git_commit_id|compilation target|exact target)",
        ),
        (
            "git_commit_id",
            "g" * 40,
            r"(?:gitCommitId|git_commit_id|compilation target|exact target)",
        ),
        (
            "git_commit_id",
            ("e" * 40) + " ",
            r"(?:gitCommitId|git_commit_id|compilation target|exact target)",
        ),
        (
            "git_commit_id",
            "d" * 40,
            r"(?:gitCommitId|git_commit_id|compilation target|exact target)",
        ),
        ("version_height", 0, r"(?:versionHeight|version_height)"),
        ("version_height", -1, r"(?:versionHeight|version_height)"),
        ("version_height", True, r"(?:versionHeight|version_height)"),
        ("version_height", False, r"(?:versionHeight|version_height)"),
        ("version_height", "42", r"(?:versionHeight|version_height)"),
        ("version_height", 42.5, r"(?:versionHeight|version_height)"),
        ("version_height", [], r"(?:versionHeight|version_height)"),
        ("public_release", "false", r"(?:publicRelease|public_release)"),
        ("public_release", 0, r"(?:publicRelease|public_release)"),
        ("public_release", 1, r"(?:publicRelease|public_release)"),
        ("public_release", None, r"(?:publicRelease|public_release)"),
        (
            "node_api_result_digest",
            "a" * 64,
            r"(?:node-api-result-digest|node_api_result_digest|digest)",
        ),
        (
            "node_api_result_digest",
            "sha256:" + ("a" * 63),
            r"(?:node-api-result-digest|node_api_result_digest|digest)",
        ),
        (
            "node_api_result_digest",
            "sha256:" + ("a" * 65),
            r"(?:node-api-result-digest|node_api_result_digest|digest)",
        ),
        (
            "node_api_result_digest",
            "sha256:" + ("A" * 64),
            r"(?:node-api-result-digest|node_api_result_digest|digest)",
        ),
        (
            "node_api_result_digest",
            "sha256:" + ("g" * 64),
            r"(?:node-api-result-digest|node_api_result_digest|digest)",
        ),
        (
            "node_api_result_digest",
            " sha256:" + ("a" * 64),
            r"(?:node-api-result-digest|node_api_result_digest|digest)",
        ),
        (
            "node_api_result_digest",
            123,
            r"(?:node-api-result-digest|node_api_result_digest|digest)",
        ),
    ],
    ids=[
        "version-empty",
        "version-non-string",
        "version-one-component",
        "version-five-components",
        "version-leading-zero-major",
        "version-leading-zero-minor",
        "version-negative-component",
        "version-nonnumeric-component",
        "version-whitespace-padded",
        "semver1-empty",
        "semver1-non-string",
        "semver2-empty",
        "semver2-non-string",
        "npm-empty",
        "npm-range",
        "npm-tag",
        "npm-url",
        "npm-v-prefixed",
        "npm-whitespace-padded",
        "npm-malformed",
        "npm-leading-zero-major",
        "npm-leading-zero-prerelease",
        "npm-empty-build",
        "npm-empty-prerelease-identifier",
        "npm-non-string",
        "npm-missing-no-fallback",
        "git-short",
        "git-uppercase",
        "git-nonhex",
        "git-whitespace-padded",
        "git-target-mismatch",
        "height-zero",
        "height-negative",
        "height-true",
        "height-false",
        "height-string",
        "height-float",
        "height-list",
        "public-release-string",
        "public-release-integer",
        "public-release-one",
        "public-release-none",
        "digest-missing-prefix",
        "digest-short",
        "digest-long",
        "digest-uppercase",
        "digest-nonhex",
        "digest-whitespace",
        "digest-non-string",
    ],
)
def test_compiler_rejects_malformed_provider_nbgv_result(
    valid_compilation_inputs: tuple[
        CompilationContext,
        ProviderRequestManifest,
    ],
    valid_node_provider_result: NodeProviderResult,
    field: str,
    value: object,
    message: str,
) -> None:
    """Independently revalidate every forged NBGV fact before readiness."""
    context, manifest = valid_compilation_inputs
    forged_facts = cast(
        "NbgvFacts",
        replace(
            cast("Any", valid_node_provider_result.nbgv),
            **{field: value},
        ),
    )
    forged_result = replace(
        valid_node_provider_result,
        nbgv=forged_facts,
    )

    _assert_phase3_compile_rejected(
        context,
        manifest,
        forged_result,
        match=message,
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
def test_compiler_rejects_substituted_provider_execution_class(
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

    _assert_phase3_compile_rejected(
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
def test_compiler_rejects_noncanonical_provider_toolchain(
    valid_compilation_inputs: tuple[
        CompilationContext,
        ProviderRequestManifest,
    ],
    valid_node_provider_result: NodeProviderResult,
    toolchain: tuple[tuple[str, str], ...],
) -> None:
    """Require the exact closed ordered nonempty Node-then-PNPM tuple."""
    context, manifest = valid_compilation_inputs
    forged_result = replace(
        valid_node_provider_result,
        toolchain=toolchain,
    )

    _assert_phase3_compile_rejected(
        context,
        manifest,
        forged_result,
        match="toolchain",
    )


def _phase3_with_other_binding(
    result: NodeProviderResult,
) -> NodeProviderResult:
    return replace(
        result,
        binding=replace(result.binding, request_id="forged-request"),
    )


def _phase3_with_other_checkout(
    result: NodeProviderResult,
) -> NodeProviderResult:
    return replace(
        result,
        checkout=replace(result.checkout, head="d" * 40),
    )


def _phase3_with_other_result_identity(
    result: NodeProviderResult,
) -> NodeProviderResult:
    return replace(
        result,
        provider_implementation_id="forged/provider-v1",
    )


def _phase3_with_other_target_fact(
    result: NodeProviderResult,
) -> NodeProviderResult:
    return replace(
        result,
        nbgv=replace(result.nbgv, git_commit_id="d" * 40),
    )


def _phase3_without_native_projection(
    result: NodeProviderResult,
) -> NodeProviderResult:
    return replace(
        result,
        nbgv=replace(result.nbgv, npm_package_version=""),
    )


@pytest.mark.parametrize(
    ("mutation", "message"),
    [
        (_phase3_with_other_binding, "binding mismatch"),
        (_phase3_with_other_checkout, "full-history checkout evidence"),
        (_phase3_with_other_result_identity, "identity mismatch"),
        (_with_nonterminal_outcome, "resolved terminal success"),
        (_with_unresolved_workspace_graph, "resolved terminal success"),
        (_with_other_project_id, "Project Node"),
        (_phase3_with_other_target_fact, "exact target"),
        (_phase3_without_native_projection, "npmPackageVersion"),
    ],
    ids=[
        "binding",
        "checkout",
        "result-identity",
        "outcome",
        "unresolved",
        "project-node",
        "target-equality",
        "native-projection",
    ],
)
def test_compiler_preserves_existing_provider_result_guards(
    valid_compilation_inputs: tuple[
        CompilationContext,
        ProviderRequestManifest,
    ],
    valid_node_provider_result: NodeProviderResult,
    mutation: NodeProviderResultMutation,
    message: str,
) -> None:
    """Keep every preexisting compiler guard while adding full validation."""
    context, manifest = valid_compilation_inputs
    forged_result = mutation(valid_node_provider_result)

    _assert_phase3_compile_rejected(
        context,
        manifest,
        forged_result,
        match=message,
    )


@pytest.mark.parametrize(
    ("canonical_version", "npm_package_version"),
    [
        ("1.2", "1.2.3+build.7"),
        ("1.2.3.4", "1.2.3-beta.2+build.7"),
    ],
    ids=["two-component-and-build", "four-component-and-prerelease-build"],
)
def test_compiler_accepts_exact_provider_result_and_validates_snapshot(
    valid_compilation_inputs: tuple[
        CompilationContext,
        ProviderRequestManifest,
    ],
    valid_node_provider_result: NodeProviderResult,
    canonical_version: str,
    npm_package_version: str,
) -> None:
    """Compile and directly validate positive canonical grammar boundaries."""
    from three_workflow_delivery_v3.repository.compiler import (  # noqa: PLC0415
        validate_first_slice_repository_model_snapshot,
    )

    context, manifest = valid_compilation_inputs
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


_CHECKOUT_BOOLEAN_REQUIREMENTS: tuple[tuple[str, bool], ...] = (
    ("shallow", False),
    ("ancestry_complete", True),
    ("tags_complete", True),
    ("credentials_persisted", False),
)

_NON_BOOLEAN_SURROGATES: tuple[tuple[str, object], ...] = (
    ("int-zero", 0),
    ("int-one", 1),
    ("none", None),
    ("float-zero", 0.0),
    ("float-one", 1.0),
    ("string-empty", ""),
    ("string-nonempty", "surrogate"),
    ("list-empty", []),
    ("list-nonempty", [False]),
    ("tuple-empty", ()),
    ("tuple-nonempty", (False,)),
    ("mapping-empty", {}),
    ("mapping-nonempty", {"surrogate": False}),
)


@pytest.mark.parametrize(
    ("field", "required_value", "surrogate"),
    [
        pytest.param(
            field,
            required_value,
            surrogate,
            id=f"{field.replace('_', '-')}-{surrogate_id}",
        )
        for field, required_value in _CHECKOUT_BOOLEAN_REQUIREMENTS
        for surrogate_id, surrogate in _NON_BOOLEAN_SURROGATES
    ],
)
def test_compiler_requires_exact_checkout_evidence_boolean_types_and_values(
    valid_compilation_inputs: tuple[
        CompilationContext,
        ProviderRequestManifest,
    ],
    valid_node_provider_result: NodeProviderResult,
    field: str,
    required_value: object,
    surrogate: object,
) -> None:
    """Reject every non-Boolean surrogate at the compiler boundary."""
    context, manifest = valid_compilation_inputs
    valid_checkout = valid_node_provider_result.checkout

    assert type(surrogate) is not bool
    assert type(required_value) is bool
    assert type(getattr(valid_checkout, field)) is bool
    assert getattr(valid_checkout, field) is required_value

    forged_checkout = cast(
        "CheckoutEvidence",
        replace(
            cast("Any", valid_checkout),
            **{field: surrogate},
        ),
    )
    forged_result = replace(
        valid_node_provider_result,
        checkout=forged_checkout,
    )

    assert getattr(forged_checkout, field) is surrogate
    for other_field, _ in _CHECKOUT_BOOLEAN_REQUIREMENTS:
        if other_field != field:
            assert getattr(forged_checkout, other_field) is getattr(
                valid_checkout,
                other_field,
            )
    assert forged_result.binding is valid_node_provider_result.binding
    assert forged_result.toolchain is valid_node_provider_result.toolchain
    assert (
        forged_result.project_nodes is valid_node_provider_result.project_nodes
    )
    assert forged_result.nbgv is valid_node_provider_result.nbgv

    _assert_phase3_compile_rejected(
        context,
        manifest,
        forged_result,
        match=r"checkout .* must be an exact Boolean",
    )
