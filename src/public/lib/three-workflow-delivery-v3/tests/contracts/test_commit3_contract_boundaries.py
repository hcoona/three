"""Contract tests for Workflow Delivery v3 implementation commit 3."""

from __future__ import annotations

import hashlib
import json
import os
import subprocess
from dataclasses import (
    FrozenInstanceError,
    replace,
)
from pathlib import Path
from typing import Any, cast

import pytest
from three_workflow_delivery_v3.canonical import canonical_sha256, canonicalize
from three_workflow_delivery_v3.catalogs import catalog_digest
from three_workflow_delivery_v3.release import eligibility as eligibility_module
from three_workflow_delivery_v3.release.eligibility import (
    LiveEligibilityContext,
    release_policy_digest,
)
from three_workflow_delivery_v3.repository import (
    node_provider as node_provider_module,
)
from three_workflow_delivery_v3.repository.compiler import (
    AdmittedRepositoryModelSnapshot,
    CompilationContext,
    CompiledBuild,
    CompiledOutput,
    CompiledQualitySelection,
    CompiledReleaseUnit,
    FactBundleAdmissionContext,
    ProviderRequestManifest,
    RepositoryModelSnapshot,
    admit_node_provider_fact_bundle,
    compile_release_policy,
    first_slice_provider_manifest,
    provider_binding,
    repository_model_snapshot_from_document,
    validate_compilation_context,
    validate_first_slice_repository_model_snapshot,
)
from three_workflow_delivery_v3.repository.descriptors import (
    FIRST_SLICE_POLICY_PATH,
    FIRST_SLICE_RELEASE_UNIT,
    load_release_policy,
)
from three_workflow_delivery_v3.repository.node_provider import (
    AUTHORITATIVE_REMOTE,
    PROVIDER_EXECUTION_CLASS,
    PROVIDER_EXECUTION_MODE,
    PROVIDER_IMPLEMENTATION_ID,
    PROVIDER_LOGICAL_ID,
    TAG_REFSPEC,
    CheckoutEvidence,
    CheckoutMaterialization,
    GlobalInput,
    NbgvFacts,
    NodeProviderFactBundle,
    NodeProviderResult,
    ProjectNode,
    create_node_provider_fact_bundle,
    provide_node_repository_facts,
    validate_node_provider_result,
)

REPO_ROOT = Path(__file__).resolve().parents[6]
PRODUCT_PATH = "src/public/lib/hcoona-release-smoke-npm"
TARGET = "e" * 40
TRANSPORT_ID = 202
BOOLEAN_ID_SURROGATE = True
NPM_VERSION = "1.2.3-beta.42.ge123456"
SHA256_A = "sha256:" + ("a" * 64)
SHA256_B = "sha256:" + ("b" * 64)
SHA256_C = "sha256:" + ("c" * 64)

_NBGV_RECOGNIZED_REF_VARIABLES = frozenset(
    {
        "APPVEYOR",
        "APPVEYOR_PULL_REQUEST_NUMBER",
        "APPVEYOR_REPO_BRANCH",
        "APPVEYOR_REPO_TAG",
        "APPVEYOR_REPO_TAG_NAME",
        "BITBUCKET_BRANCH",
        "BITBUCKET_COMMIT",
        "BITBUCKET_PIPELINE_UUID",
        "BITBUCKET_PR_ID",
        "BUILD_GIT_BRANCH",
        "BUILD_SOURCEBRANCH",
        "BUILD_VCS_NUMBER",
        "CI_COMMIT_REF_NAME",
        "CI_COMMIT_SHA",
        "CI_COMMIT_TAG",
        "GITHUB_ACTIONS",
        "GITHUB_BASE_REF",
        "GITHUB_HEAD_REF",
        "GITHUB_REF",
        "GITHUB_SHA",
        "GITLAB_CI",
        "IGNORE_GITHUB_REF",
        "JENKINS_URL",
        "TRAVIS",
        "TRAVIS_BRANCH",
        "TRAVIS_COMMIT",
        "TRAVIS_PULL_REQUEST_BRANCH",
        "TRAVIS_TAG",
    }
)


def _run(repo: Path, *command: str) -> str:
    return subprocess.run(  # noqa: S603
        command,
        cwd=repo,
        check=True,
        capture_output=True,
        text=True,
    ).stdout.strip()


def _context(*, target: str = TARGET) -> CompilationContext:
    return CompilationContext(
        request_id="release-request-42",
        purpose="live-release",
        workflow_run_id=7101,
        run_attempt=None,
        target=target,
        producer="compile-model",
        control=f"workflow-delivery-v3:{target}",
        catalog_digest=catalog_digest(),
    )


def _manifest(context: CompilationContext) -> ProviderRequestManifest:
    return first_slice_provider_manifest(
        context,
        provider_producer="discover-node",
    )


def _provider_result(
    context: CompilationContext,
    manifest: ProviderRequestManifest,
) -> NodeProviderResult:
    global_inputs = tuple(
        GlobalInput(
            path=path,
            content_digest=SHA256_A,
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
    return NodeProviderResult(
        binding=provider_binding(manifest, "node-first-slice"),
        provider_logical_id=PROVIDER_LOGICAL_ID,
        provider_implementation_id=PROVIDER_IMPLEMENTATION_ID,
        execution_mode=PROVIDER_EXECUTION_MODE,
        execution_class=PROVIDER_EXECUTION_CLASS,
        toolchain=(("node", "v24.14.0"), ("pnpm", "11.21.0")),
        manifest_digest=SHA256_B,
        configuration_digest=canonical_sha256(
            {
                "schema": ("workflow-delivery/v3/node-provider-configuration"),
                "global-inputs": [item.to_document() for item in global_inputs],
            }
        ),
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
            node_api_result_digest=SHA256_A,
        ),
        unresolved=(),
        conflicts=(),
        outcome="success",
        diagnostic_reference=None,
    )


def _snapshot() -> RepositoryModelSnapshot:
    context = _context()
    return RepositoryModelSnapshot(
        context=context,
        manifest_digest=SHA256_A,
        provider_result_digests=(SHA256_B,),
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
        release_units=(
            CompiledReleaseUnit(
                release_unit=FIRST_SLICE_RELEASE_UNIT,
                descriptor_path=(
                    f"{PRODUCT_PATH}/workflow-delivery.release-unit.yml"
                ),
                builds=(
                    CompiledBuild(
                        build_id="npm-package",
                        definition="node/npm-package-v1",
                        project_id="@hcoona/hcoona-release-smoke-npm",
                        entry_point=f"{PRODUCT_PATH}/package.json",
                        outputs=(
                            CompiledOutput(
                                output_id="npm-tarball",
                                role="primary-package",
                                kind="npm-tarball",
                            ),
                        ),
                        required_native_projections=("npmPackageVersion",),
                    ),
                ),
            ),
        ),
        quality=(
            CompiledQualitySelection(
                path=f"{PRODUCT_PATH}/workflow-delivery.quality.yml",
                ecosystem="node",
                preset="node/hcoona-release-smoke-npm-v1",
                required=("node/project-build-v1", "node/project-test-v1"),
                advisory=(),
            ),
        ),
        release_policy_path=FIRST_SLICE_POLICY_PATH,
        release_policy=compile_release_policy(
            load_release_policy(
                REPO_ROOT / FIRST_SLICE_POLICY_PATH,
                _target_path=FIRST_SLICE_POLICY_PATH,
            )
        ),
        nbgv=NbgvFacts(
            canonical_version="1.2.3",
            sem_ver1="1.2.3-beta-0042-e123456",
            sem_ver2=NPM_VERSION,
            version_height=42,
            git_commit_id=context.target,
            public_release=False,
            npm_package_version=NPM_VERSION,
            node_api_result_digest=SHA256_C,
        ),
        reverse_index=(
            (
                "@hcoona/hcoona-release-smoke-npm",
                (f"{FIRST_SLICE_RELEASE_UNIT}/npm-package",),
            ),
        ),
        unresolved=(),
        ready=True,
    )


def _snapshot_with_build(
    snapshot: RepositoryModelSnapshot,
    build: object,
) -> RepositoryModelSnapshot:
    release_unit = snapshot.release_units[0]
    return replace(
        snapshot,
        release_units=(replace(release_unit, builds=(cast("Any", build),)),),
    )


def _snapshot_with_quality(
    snapshot: RepositoryModelSnapshot,
    selection: object,
) -> RepositoryModelSnapshot:
    return replace(snapshot, quality=(cast("Any", selection),))


def _live_context(
    snapshot: RepositoryModelSnapshot,
) -> LiveEligibilityContext:
    policy = load_release_policy(REPO_ROOT / FIRST_SLICE_POLICY_PATH)
    return LiveEligibilityContext(
        purpose="live-release",
        request_id=snapshot.context.request_id,
        workflow_run_id=snapshot.context.workflow_run_id,
        selected_ref="refs/heads/feature/ref-neutral",
        target=snapshot.context.target,
        repository_model_digest=snapshot.snapshot_digest,
        producer="evaluate-live-eligibility",
        control=snapshot.context.control,
        release_policy_digest=release_policy_digest(policy),
        catalog_digest=catalog_digest(),
    )


def _admitted_model(
    snapshot: RepositoryModelSnapshot,
) -> AdmittedRepositoryModelSnapshot:
    return AdmittedRepositoryModelSnapshot(
        snapshot=snapshot,
        canonical_bytes=canonicalize(snapshot.to_document()),
        canonical_digest=snapshot.snapshot_digest,
    )


def _validate_live_context(
    context: LiveEligibilityContext,
    repository_model: AdmittedRepositoryModelSnapshot,
) -> None:
    policy = load_release_policy(REPO_ROOT / FIRST_SLICE_POLICY_PATH)
    eligibility_module._validate_live_context(  # noqa: SLF001
        context,
        repository_model,
        policy,
    )


def _clean_nbgv_environment() -> dict[str, str]:
    environment = os.environ.copy()
    for name in _NBGV_RECOGNIZED_REF_VARIABLES:
        environment.pop(name, None)
    environment.update(
        {
            "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
            "DOTNET_NOLOGO": "1",
        }
    )
    return environment


def _provider_fixture_environment() -> dict[str, str]:
    environment = _clean_nbgv_environment()
    environment.update(
        {
            "GIT_TERMINAL_PROMPT": "0",
            "GCM_INTERACTIVE": "Never",
            "GIT_ASKPASS": "/bin/false",
            "SSH_ASKPASS": "/bin/false",
            "npm_config_offline": "true",
        }
    )
    return environment


def _run_with_environment(
    repo: Path,
    command: tuple[str, ...],
    environment: dict[str, str],
) -> str:
    return subprocess.run(  # noqa: S603
        command,
        cwd=repo,
        env=environment,
        check=True,
        capture_output=True,
        text=True,
    ).stdout.strip()


@pytest.fixture
def nbgv_provider_repository(tmp_path: Path) -> tuple[Path, str]:
    """Create a clean local Provider repository with real PNPM/NBGV inputs."""
    environment = _provider_fixture_environment()
    seed = tmp_path / "seed"
    project = seed / PRODUCT_PATH
    project.mkdir(parents=True)
    installed_nbgv = (
        REPO_ROOT
        / "node_modules"
        / ".pnpm"
        / "node_modules"
        / "nerdbank-gitversioning"
    ).resolve()
    assert installed_nbgv.is_dir()
    (seed / "package.json").write_text(
        json.dumps({"name": "provider-fixture-root", "private": True}) + "\n",
        encoding="utf-8",
    )
    (seed / "pnpm-workspace.yaml").write_text(
        f"packages:\n  - {PRODUCT_PATH}\n",
        encoding="utf-8",
    )
    (seed / "version.json").write_text(
        json.dumps(
            {
                "version": "1.2",
                "publicReleaseRefSpec": ["^refs/heads/main$"],
            }
        ),
        encoding="utf-8",
    )
    (project / "package.json").write_text(
        json.dumps(
            {
                "name": "@hcoona/hcoona-release-smoke-npm",
                "version": "0.0.0-placeholder",
                "type": "module",
                "devDependencies": {
                    "nerdbank-gitversioning": (
                        f"file:{installed_nbgv.as_posix()}"
                    ),
                },
            },
        )
        + "\n",
        encoding="utf-8",
    )
    _run_with_environment(
        seed,
        (
            "pnpm",
            "install",
            "--lockfile-only",
            "--ignore-scripts",
            "--ignore-pnpmfile",
            "--offline",
        ),
        environment,
    )
    _run_with_environment(
        seed,
        ("git", "init", "--quiet", "--initial-branch=feature/ref-neutral"),
        environment,
    )
    _run_with_environment(
        seed,
        ("git", "config", "user.name", "Workflow Delivery Test"),
        environment,
    )
    _run_with_environment(
        seed,
        (
            "git",
            "config",
            "user.email",
            "workflow-delivery@example.invalid",
        ),
        environment,
    )
    _run_with_environment(seed, ("git", "add", "."), environment)
    _run_with_environment(
        seed,
        ("git", "commit", "--quiet", "--message", "fixture"),
        environment,
    )
    target = _run_with_environment(
        seed,
        ("git", "rev-parse", "HEAD"),
        environment,
    )
    bare_remote = tmp_path / "authoritative.git"
    checkout = tmp_path / "checkout"
    _run_with_environment(
        tmp_path,
        ("git", "clone", "--bare", seed.as_uri(), str(bare_remote)),
        environment,
    )
    _run_with_environment(
        tmp_path,
        ("git", "clone", bare_remote.resolve().as_uri(), str(checkout)),
        environment,
    )
    assert (
        _run_with_environment(
            checkout,
            ("git", "rev-parse", "HEAD"),
            environment,
        )
        == target
    )
    return checkout, target


class _RecordingProviderRunner:
    """Record production Provider command boundaries while running them."""

    def __init__(self) -> None:
        self.commands: list[tuple[tuple[str, ...], Path]] = []

    def __call__(self, command: tuple[str, ...], cwd: Path) -> str:
        self.commands.append((command, cwd))
        return subprocess.run(  # noqa: S603
            command,
            cwd=cwd,
            check=True,
            capture_output=True,
            text=True,
        ).stdout


def _real_provider_nbgv_facts(
    repo: Path,
    target: str,
) -> tuple[dict[str, object], _RecordingProviderRunner]:
    context = _context(target=target)
    manifest = _manifest(context)
    runner = _RecordingProviderRunner()
    result = provide_node_repository_facts(
        repo,
        PRODUCT_PATH,
        provider_binding(manifest, "node-first-slice"),
        CheckoutMaterialization(
            fetch_depth=0,
            credentials_persisted=False,
        ),
        runner=runner,
    )
    facts = result.nbgv
    return {
        "canonical-version": facts.canonical_version,
        "sem-ver1": facts.sem_ver1,
        "sem-ver2": facts.sem_ver2,
        "version-height": facts.version_height,
        "git-commit-id": facts.git_commit_id,
        "public-release": facts.public_release,
        "npm-package-version": facts.npm_package_version,
        "node-api-result-digest": facts.node_api_result_digest,
    }, runner


def _configure_clean_provider_environment(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    for name in _NBGV_RECOGNIZED_REF_VARIABLES:
        monkeypatch.delenv(name, raising=False)
    for name, value in _provider_fixture_environment().items():
        if name in {
            "DOTNET_CLI_TELEMETRY_OPTOUT",
            "DOTNET_NOLOGO",
            "GIT_TERMINAL_PROMPT",
            "GCM_INTERACTIVE",
            "GIT_ASKPASS",
            "SSH_ASKPASS",
            "npm_config_offline",
        }:
            monkeypatch.setenv(name, value)


def test_nbgv_provider_declares_explicit_ref_neutral_environment_allowlist() -> (  # noqa: E501
    None
):
    """Exclude every NBGV-recognized CI ref input from authoritative facts."""
    allowlist = frozenset(node_provider_module.NBGV_ENVIRONMENT_ALLOWLIST)

    assert "PATH" in allowlist
    assert allowlist.isdisjoint(_NBGV_RECOGNIZED_REF_VARIABLES)
    assert "IGNORE_GITHUB_REF" not in allowlist


@pytest.mark.parametrize(
    "overrides",
    [
        pytest.param(
            {
                "GITLAB_CI": "true",
                "CI_COMMIT_REF_NAME": "main",
                "CI_COMMIT_SHA": "{target}",
            },
            id="gitlab",
        ),
        pytest.param(
            {
                "APPVEYOR": "True",
                "APPVEYOR_REPO_BRANCH": "main",
            },
            id="appveyor",
        ),
        pytest.param(
            {
                "TRAVIS": "true",
                "TRAVIS_BRANCH": "main",
                "TRAVIS_COMMIT": "{target}",
            },
            id="travis",
        ),
    ],
)
def test_real_nbgv_facts_ignore_recognized_ci_ref_environment(
    nbgv_provider_repository: tuple[Path, str],
    monkeypatch: pytest.MonkeyPatch,
    overrides: dict[str, str],
) -> None:
    """Prove Provider-launched NBGV facts ignore ambient CI refs."""
    repo, target = nbgv_provider_repository
    _configure_clean_provider_environment(monkeypatch)
    baseline, baseline_runner = _real_provider_nbgv_facts(repo, target)
    contaminated = {
        name: value.format(target=target) for name, value in overrides.items()
    }
    for name, value in contaminated.items():
        monkeypatch.setenv(name, value)

    actual, actual_runner = _real_provider_nbgv_facts(repo, target)

    baseline_nbgv_calls = [
        command
        for command, _ in baseline_runner.commands
        if command[:3] == ("node", "--input-type=module", "-e")
    ]
    actual_nbgv_calls = [
        command
        for command, _ in actual_runner.commands
        if command[:3] == ("node", "--input-type=module", "-e")
    ]
    assert len(baseline_nbgv_calls) == 1
    assert len(actual_nbgv_calls) == 1
    assert baseline_nbgv_calls[0][3].count("getVersion(process.cwd())") == 1
    assert actual_nbgv_calls[0][3].count("getVersion(process.cwd())") == 1
    assert baseline["git-commit-id"] == target
    assert baseline["version-height"] == 1
    assert baseline["public-release"] is False
    assert str(baseline["canonical-version"]).startswith("1.2.1")
    assert baseline["sem-ver1"] == f"1.2.1-{target[:10]}"
    assert baseline["sem-ver2"] == f"1.2.1-g{target[:10]}"
    assert baseline["npm-package-version"] == f"1.2.1-g{target[:10]}"
    assert str(baseline["node-api-result-digest"]).startswith("sha256:")
    assert actual == baseline
    assert _run(repo, "git", "rev-parse", "HEAD") == target
    assert _run(repo, "git", "symbolic-ref", "HEAD") == (
        "refs/heads/feature/ref-neutral"
    )


def test_node_provider_result_schema_contains_every_approved_field() -> None:
    """Cover the complete approved Provider Result schema, not a subset."""
    context = _context()
    result = _provider_result(context, _manifest(context))
    document = result.to_document()
    expected_top_level = {
        "schema",
        "binding",
        "provider",
        "input-digests",
        "checkout",
        "project-nodes",
        "global-inputs",
        "build-capabilities",
        "nbgv",
        "unresolved",
        "conflicts",
        "outcome",
        "diagnostic-reference",
    }

    assert document["schema"] == "workflow-delivery/v3/node-provider-result"
    assert set(document) == expected_top_level
    assert set(cast("dict[str, object]", document["binding"])) == {
        "request-id",
        "purpose",
        "workflow-run-id",
        "target",
        "producer",
        "control",
        "catalog-digest",
        "request-digest",
    }
    assert cast("dict[str, object]", document["input-digests"]) == {
        "manifest": SHA256_B,
        "configuration": result.configuration_digest,
    }
    assert cast("dict[str, object]", document["provider"])["toolchain"] == {
        "node": "v24.14.0",
        "pnpm": "11.21.0",
    }
    assert document["global-inputs"] == [
        item.to_document() for item in result.global_inputs
    ]
    assert document["build-capabilities"] == ["node/npm-package-v1"]
    assert document["diagnostic-reference"] is None
    assert result.result_digest.startswith("sha256:")


def _fact_bundle(
    manifest: ProviderRequestManifest,
    result: NodeProviderResult,
) -> NodeProviderFactBundle:
    return create_node_provider_fact_bundle(
        result,
        manifest_digest=manifest.manifest_digest,
        manifest_entry_id=manifest.requests[0].entry_id,
        request_artifact_id=101,
        request_artifact_digest=SHA256_A,
        transport_id=TRANSPORT_ID,
        transport_digest=SHA256_B,
    )


def _admission_context(
    bundle: NodeProviderFactBundle,
) -> FactBundleAdmissionContext:
    return FactBundleAdmissionContext(
        request_artifact_id=101,
        request_artifact_digest=SHA256_A,
        transport_id=TRANSPORT_ID,
        transport_digest=SHA256_B,
        bundle_digest=bundle.bundle_digest,
    )


def test_fact_bundle_schema_binds_complete_approved_contract() -> None:
    """Bind literal wire fields and canonical values, allowing value copies."""
    context = _context()
    manifest = _manifest(context)
    result = _provider_result(context, manifest)
    bundle = _fact_bundle(manifest, result)
    admission = _admission_context(bundle)

    admitted = admit_node_provider_fact_bundle(
        bundle,
        context=context,
        manifest=manifest,
        admission=admission,
    )

    expected_binding = {
        "request-id": "release-request-42",
        "purpose": "live-release",
        "workflow-run-id": 7101,
        "target": TARGET,
        "producer": "discover-node",
        "control": f"workflow-delivery-v3:{TARGET}",
        "catalog-digest": context.catalog_digest,
        "request-digest": manifest.requests[0].request_digest,
    }
    expected_inputs = [
        {
            "path": path,
            "content-digest": SHA256_A,
            "project-ids": ["@hcoona/hcoona-release-smoke-npm"],
        }
        for path in (
            "package.json",
            "pnpm-lock.yaml",
            "pnpm-workspace.yaml",
            f"{PRODUCT_PATH}/version.json",
            "version.json",
        )
    ]
    expected_result = {
        "schema": "workflow-delivery/v3/node-provider-result",
        "binding": expected_binding,
        "provider": {
            "logical-id": "node/pnpm-nbgv-v1",
            "implementation-id": "three-workflow-delivery-v3/node-pnpm-nbgv-v1",
            "execution-mode": "target-evaluating",
            "execution-class": "target-evaluation/unprivileged-v1",
            "toolchain": {"node": "v24.14.0", "pnpm": "11.21.0"},
        },
        "input-digests": {
            "manifest": SHA256_B,
            "configuration": canonical_sha256(
                {
                    "schema": (
                        "workflow-delivery/v3/node-provider-configuration"
                    ),
                    "global-inputs": expected_inputs,
                }
            ),
        },
        "checkout": {
            "target": TARGET,
            "head": TARGET,
            "shallow": False,
            "ancestry-complete": True,
            "tags-complete": True,
            "credentials-persisted": False,
            "authoritative-remote": "origin",
            "authoritative-remote-url": "file:///authoritative-remote.git",
            "tag-refspec": "refs/tags/*:refs/tags/*",
        },
        "project-nodes": [
            {
                "project-id": "@hcoona/hcoona-release-smoke-npm",
                "package-name": "@hcoona/hcoona-release-smoke-npm",
                "path": PRODUCT_PATH,
                "manifest-path": f"{PRODUCT_PATH}/package.json",
                "private": False,
                "workspace-dependencies": [],
            }
        ],
        "global-inputs": expected_inputs,
        "build-capabilities": ["node/npm-package-v1"],
        "nbgv": {
            "canonical": {
                "version": "1.2.3",
                "semVer1": "1.2.3-beta-0042-e123456",
                "semVer2": NPM_VERSION,
                "versionHeight": 42,
                "gitCommitId": TARGET,
                "publicRelease": False,
            },
            "native": {"npmPackageVersion": NPM_VERSION},
            "node-api-result-digest": SHA256_A,
        },
        "unresolved": [],
        "conflicts": [],
        "outcome": "success",
        "diagnostic-reference": None,
    }
    expected_document = {
        "schema": "workflow-delivery/v3/node-provider-fact-bundle",
        "binding": expected_binding,
        "provider-request-manifest-digest": manifest.manifest_digest,
        "provider-request-entry-id": "node-first-slice",
        "request-artifact": {"artifact-id": 101, "artifact-digest": SHA256_A},
        "provider-result": {
            "payload": expected_result,
            "payload-canonical-digest": canonical_sha256(expected_result),
        },
        "transport": {"artifact-id": TRANSPORT_ID, "artifact-digest": SHA256_B},
    }
    expected_bytes = json.dumps(
        expected_document,
        sort_keys=True,
        separators=(",", ":"),
    ).encode("utf-8")

    assert bundle.to_document() == expected_document
    assert canonicalize(bundle.to_document()) == expected_bytes
    assert (
        bundle.bundle_digest
        == f"sha256:{hashlib.sha256(expected_bytes).hexdigest()}"
    )
    assert admitted.provider_result.to_document() == expected_result
    assert admitted.bundle.to_document() == expected_document
    assert admitted.admission == admission
    assert not hasattr(bundle, "__dict__")
    with pytest.raises(FrozenInstanceError):
        setattr(bundle, "transport_id", 203)  # noqa: B010


@pytest.mark.parametrize(
    "mutation",
    [
        "binding-request",
        "binding-purpose",
        "binding-run",
        "binding-attempt",
        "binding-target",
        "binding-producer",
        "binding-control",
        "binding-catalog-digest",
        "binding-request-digest",
        "type-run",
        "type-artifact-id",
        "type-transport-id",
        "manifest-digest",
        "manifest-entry",
        "artifact-digest",
        "provider-result-digest",
        "transport-digest",
        "provider-result",
    ],
)
def test_fact_bundle_admission_rejects_binding_type_digest_and_result_substitutions(  # noqa: E501
    mutation: str,
) -> None:
    """Reject every authority, runtime-type, integrity, and payload forgery."""
    context = _context()
    manifest = _manifest(context)
    result = _provider_result(context, manifest)
    bundle = _fact_bundle(manifest, result)
    admission = _admission_context(bundle)
    binding_mutations: dict[str, tuple[str, object]] = {
        "binding-request": ("request_id", "other-request"),
        "binding-purpose": ("purpose", "release-simulation"),
        "binding-run": ("workflow_run_id", 7102),
        "binding-attempt": ("run_attempt", 2),
        "binding-target": ("target", "d" * 40),
        "binding-producer": ("producer", "other-provider"),
        "binding-control": ("control", "other-control"),
        "binding-catalog-digest": ("catalog_digest", SHA256_C),
        "binding-request-digest": ("request_digest", SHA256_C),
        "type-run": ("workflow_run_id", True),
    }
    if mutation in binding_mutations:
        field, value = binding_mutations[mutation]
        bundle = replace(
            bundle,
            binding=replace(bundle.binding, **{field: cast("Any", value)}),
        )
    elif mutation == "type-artifact-id":
        bundle = replace(bundle, request_artifact_id=BOOLEAN_ID_SURROGATE)
    elif mutation == "type-transport-id":
        bundle = replace(bundle, transport_id=BOOLEAN_ID_SURROGATE)
    elif mutation == "manifest-digest":
        bundle = replace(bundle, manifest_digest=SHA256_C)
    elif mutation == "manifest-entry":
        bundle = replace(bundle, manifest_entry_id="other-entry")
    elif mutation == "artifact-digest":
        bundle = replace(bundle, request_artifact_digest=SHA256_C)
    elif mutation == "provider-result-digest":
        bundle = replace(bundle, provider_result_digest=SHA256_C)
    elif mutation == "transport-digest":
        bundle = replace(bundle, transport_digest=SHA256_C)
    else:
        forged_result = replace(result, outcome="blocked")
        assert forged_result.result_digest != result.result_digest
        bundle = replace(
            bundle,
            provider_result=forged_result,
            provider_result_digest=forged_result.result_digest,
        )

    with pytest.raises((TypeError, ValueError)):
        admit_node_provider_fact_bundle(
            bundle,
            context=context,
            manifest=manifest,
            admission=admission,
        )


@pytest.mark.parametrize(
    "value",
    [
        pytest.param("", id="empty"),
        pytest.param(1, id="integer"),
        pytest.param(True, id="boolean"),
        pytest.param(1.0, id="float"),
        pytest.param(" ", id="whitespace"),
        pytest.param(["file:///remote.git"], id="list"),
        pytest.param(("file:///remote.git",), id="tuple"),
        pytest.param({"url": "file:///remote.git"}, id="mapping"),
        pytest.param(None, id="none"),
    ],
)
def test_provider_result_admission_requires_nonempty_string_authoritative_remote_url(  # noqa: E501
    value: object,
) -> None:
    """Require a concrete string URL, never a truthy surrogate."""
    context = _context()
    manifest = _manifest(context)
    valid = _provider_result(context, manifest)
    forged = replace(
        valid,
        checkout=replace(
            valid.checkout,
            authoritative_remote_url=cast("Any", value),
        ),
    )

    with pytest.raises(
        (TypeError, ValueError),
        match="checkout authoritative_remote_url",
    ):
        validate_node_provider_result(forged)

    assert forged.checkout.authoritative_remote_url == value
    assert forged.binding is valid.binding
    assert forged.nbgv is valid.nbgv


@pytest.mark.parametrize(
    "value",
    [
        pytest.param(True, id="true"),
        pytest.param(0, id="zero"),
        pytest.param(1, id="one"),
        pytest.param(0.0, id="zero-float"),
        pytest.param("", id="empty-string"),
        pytest.param("false", id="string"),
        pytest.param([], id="list"),
        pytest.param({}, id="mapping"),
        pytest.param(None, id="none"),
    ],
)
def test_repository_model_admission_requires_private_exactly_false(
    value: object,
) -> None:
    """Reject every non-Boolean-false Project Node privacy value."""
    snapshot = _snapshot()
    project = snapshot.project_nodes[0]
    forged = replace(
        snapshot,
        project_nodes=(replace(project, private=cast("Any", value)),),
    )

    with pytest.raises(
        (TypeError, ValueError),
        match=r"Project Node (?:private|closure mismatch)",
    ):
        validate_first_slice_repository_model_snapshot(forged)

    validate_first_slice_repository_model_snapshot(snapshot)
    assert snapshot.project_nodes[0].private is False


def _mutate_build_closure(
    snapshot: RepositoryModelSnapshot,
    mutation: str,
) -> RepositoryModelSnapshot:
    release_unit = snapshot.release_units[0]
    build = release_unit.builds[0]
    if mutation == "missing":
        builds = ()
    elif mutation == "extra":
        builds = (build, replace(build, build_id="extra-build"))
    elif mutation == "renamed":
        builds = (replace(build, build_id="renamed-npm-package"),)
    elif mutation == "duplicate":
        builds = (build, build)
    else:
        builds = (replace(build, definition="node/substituted-v1"),)
    return replace(
        snapshot,
        release_units=(replace(release_unit, builds=builds),),
    )


@pytest.mark.parametrize(
    "mutation",
    ["missing", "extra", "renamed", "duplicate", "substituted"],
)
def test_repository_model_admission_rejects_non_exact_build_closure(
    mutation: str,
) -> None:
    """Close the first slice to the exact singleton npm-package build."""
    snapshot = _snapshot()
    forged = _mutate_build_closure(snapshot, mutation)

    with pytest.raises(
        ValueError,
        match=r"(?:Release Unit|Build) closure mismatch",
    ):
        validate_first_slice_repository_model_snapshot(forged)

    validate_first_slice_repository_model_snapshot(snapshot)
    assert snapshot.release_units[0].builds[0].build_id == "npm-package"


def _mutate_quality_closure(
    snapshot: RepositoryModelSnapshot,
    mutation: str,
) -> RepositoryModelSnapshot:
    quality = snapshot.quality[0]
    if mutation == "missing":
        selections = ()
    elif mutation == "extra":
        selections = (
            quality,
            replace(quality, path=f"{PRODUCT_PATH}/extra-quality.yml"),
        )
    elif mutation == "renamed":
        selections = (replace(quality, preset="node/renamed-v1"),)
    elif mutation == "duplicate":
        selections = (quality, quality)
    else:
        selections = (replace(quality, ecosystem="python"),)
    return replace(snapshot, quality=selections)


@pytest.mark.parametrize(
    "mutation",
    ["missing", "extra", "renamed", "duplicate", "substituted"],
)
def test_repository_model_admission_rejects_non_exact_quality_preset_closure(
    mutation: str,
) -> None:
    """Close quality to the exact singleton first-slice Node preset."""
    snapshot = _snapshot()
    forged = _mutate_quality_closure(snapshot, mutation)

    with pytest.raises(ValueError, match="Quality closure mismatch"):
        validate_first_slice_repository_model_snapshot(forged)

    validate_first_slice_repository_model_snapshot(snapshot)
    assert snapshot.quality[0].preset == "node/hcoona-release-smoke-npm-v1"


@pytest.mark.parametrize(
    "selected_ref",
    [
        pytest.param("refs/heads/feature/ref-neutral", id="branch"),
        pytest.param("refs/tags/release/v1.2.3", id="tag"),
    ],
)
def test_live_context_accepts_canonical_selected_refs(
    selected_ref: str,
) -> None:
    """Accept nonempty canonical branch and tag refs as exact strings."""
    snapshot = _snapshot()
    repository_model = _admitted_model(snapshot)
    context = replace(_live_context(snapshot), selected_ref=selected_ref)

    _validate_live_context(context, repository_model)

    assert context.selected_ref == selected_ref
    assert context.producer == "evaluate-live-eligibility"


@pytest.mark.parametrize(
    ("field", "value"),
    [
        pytest.param("selected_ref", "", id="selected-ref-empty"),
        pytest.param("selected_ref", 1, id="selected-ref-integer"),
        pytest.param("selected_ref", True, id="selected-ref-boolean"),
        pytest.param("selected_ref", "main", id="selected-ref-short-name"),
        pytest.param(
            "selected_ref",
            "refs/heads/",
            id="selected-ref-empty-branch",
        ),
        pytest.param(
            "selected_ref",
            "refs/pull/1/head",
            id="selected-ref-unsupported-namespace",
        ),
        pytest.param(
            "selected_ref",
            "refs/heads/feature..invalid",
            id="selected-ref-double-dot",
        ),
        pytest.param(
            "selected_ref",
            "refs/heads/feature.lock",
            id="selected-ref-lock-suffix",
        ),
        pytest.param(
            "selected_ref",
            "refs/heads/feature@{invalid",
            id="selected-ref-reflog-syntax",
        ),
        pytest.param(
            "selected_ref",
            "refs/heads/feature invalid",
            id="selected-ref-space",
        ),
        pytest.param("producer", "", id="producer-empty"),
        pytest.param("producer", " ", id="producer-whitespace"),
        pytest.param("producer", 1, id="producer-integer"),
        pytest.param("producer", True, id="producer-boolean"),
        pytest.param("producer", ["job"], id="producer-list"),
    ],
)
def test_live_context_requires_exact_strings_and_valid_selected_ref(
    field: str,
    value: object,
) -> None:
    """Reject malformed refs and truthy numeric/Boolean producer surrogates."""
    snapshot = _snapshot()
    repository_model = _admitted_model(snapshot)
    context = replace(
        _live_context(snapshot),
        **{field: cast("Any", value)},
    )

    with pytest.raises((TypeError, ValueError)):
        _validate_live_context(context, repository_model)

    assert snapshot.ready is True
    assert snapshot.context.target == TARGET


@pytest.mark.parametrize(
    "value",
    [
        pytest.param(1, id="integer"),
        pytest.param(True, id="boolean"),
        pytest.param(" ", id="whitespace"),
        pytest.param(["compile-model"], id="list"),
        pytest.param({"job": "compile-model"}, id="mapping"),
    ],
)
def test_compilation_context_requires_exact_string_producer(
    value: object,
) -> None:
    """Reject truthy producer values that are not nonempty strings."""
    forged = replace(_context(), producer=cast("Any", value))

    with pytest.raises((TypeError, ValueError)):
        validate_compilation_context(forged)

    assert forged.target == TARGET
    assert forged.run_attempt is None


@pytest.mark.parametrize(
    "value",
    [
        pytest.param(False, id="false"),
        pytest.param(0, id="zero"),
        pytest.param(1, id="one"),
        pytest.param("true", id="string"),
        pytest.param([True], id="list"),
        pytest.param((True,), id="tuple"),
        pytest.param({"ready": True}, id="mapping"),
        pytest.param(None, id="none"),
    ],
)
def test_repository_model_admission_requires_ready_exactly_true(
    value: object,
) -> None:
    """Reject false and truthy non-Boolean readiness substitutes."""
    snapshot = _snapshot()
    forged = replace(snapshot, ready=cast("Any", value))

    with pytest.raises(ValueError, match="ready first-slice closure"):
        validate_first_slice_repository_model_snapshot(forged)

    validate_first_slice_repository_model_snapshot(snapshot)
    assert snapshot.ready is True
    assert snapshot.unresolved == ()


def test_repository_model_rejects_digest_equivalent_list_backed_snapshot() -> (
    None
):
    """Block a tuple-to-list TOCTOU mutation that preserves JSON digest."""
    snapshot = _snapshot()
    forged = replace(
        snapshot,
        release_units=cast("Any", [*snapshot.release_units]),
    )

    assert forged.to_document() == snapshot.to_document()
    assert forged.snapshot_digest == snapshot.snapshot_digest
    with pytest.raises(TypeError, match=r"release_units.*exact tuple"):
        validate_first_slice_repository_model_snapshot(forged)

    validate_first_slice_repository_model_snapshot(snapshot)
    assert snapshot.release_units[0].builds[0].build_id == "npm-package"


def test_repository_model_parser_owns_values_across_document_reuse() -> None:
    """Keep parsed values stable when source and exported documents change."""
    expected = _snapshot()
    expected_bytes = canonicalize(expected.to_document())
    expected_digest = expected.snapshot_digest
    source = cast("dict[str, Any]", expected.to_document())

    parsed = repository_model_snapshot_from_document(source)

    assert parsed == expected
    assert canonicalize(parsed.to_document()) == expected_bytes
    assert parsed.snapshot_digest == expected_digest
    validate_first_slice_repository_model_snapshot(parsed)

    source["provider-result-digests"].append(SHA256_C)
    source["release-units"][0]["builds"][0]["outputs"].clear()
    source["release-policy"]["channels"]["buddy"]["projections"][0][
        "package"
    ] = "@hcoona/changed-source"

    assert canonicalize(source) != expected_bytes
    assert parsed == expected
    assert canonicalize(parsed.to_document()) == expected_bytes
    assert parsed.snapshot_digest == expected_digest
    validate_first_slice_repository_model_snapshot(parsed)

    exported = cast("dict[str, Any]", parsed.to_document())
    exported["provider-result-digests"].clear()
    exported["release-units"][0]["builds"][0]["outputs"][0]["output-id"] = (
        "changed-output"
    )
    exported["release-policy"]["channels"]["buddy"]["projections"].clear()

    assert canonicalize(exported) != expected_bytes
    assert parsed == expected
    assert canonicalize(parsed.to_document()) == expected_bytes
    assert parsed.snapshot_digest == expected_digest
    validate_first_slice_repository_model_snapshot(parsed)


def test_repository_model_valid_tuples_keep_canonical_json_arrays() -> None:
    """Keep accepted tuple fields serialized as canonical JSON arrays."""
    snapshot = _snapshot()

    validate_first_slice_repository_model_snapshot(snapshot)
    document = snapshot.to_document()
    assert snapshot.release_policy is not None

    assert document["project-nodes"] == [
        {
            "project-id": "@hcoona/hcoona-release-smoke-npm",
            "package-name": "@hcoona/hcoona-release-smoke-npm",
            "path": PRODUCT_PATH,
            "manifest-path": f"{PRODUCT_PATH}/package.json",
            "private": False,
            "workspace-dependencies": [],
        },
    ]
    assert document["release-units"] == [
        {
            "release-unit": FIRST_SLICE_RELEASE_UNIT,
            "descriptor-path": (
                f"{PRODUCT_PATH}/workflow-delivery.release-unit.yml"
            ),
            "builds": [
                {
                    "build-id": "npm-package",
                    "definition": "node/npm-package-v1",
                    "project-id": "@hcoona/hcoona-release-smoke-npm",
                    "entry-point": f"{PRODUCT_PATH}/package.json",
                    "outputs": [
                        {
                            "output-id": "npm-tarball",
                            "role": "primary-package",
                            "kind": "npm-tarball",
                        },
                    ],
                    "required-native-projections": ["npmPackageVersion"],
                },
            ],
        },
    ]
    assert document["release-policy"] == {
        "schema": "workflow-delivery/v3/compiled-release-policy",
        "path": FIRST_SLICE_POLICY_PATH,
        "release-unit": FIRST_SLICE_RELEASE_UNIT,
        "governance": {
            "repository": "hcoona/three",
            "ref": "refs/heads/main",
            "path": (
                ".github/workflow-delivery/governance/"
                "hcoona-release-smoke-npm.json"
            ),
            "max-age-days": 90,
        },
        "channels": {
            name: channel.to_document()
            for name, channel in snapshot.release_policy.channels
        },
    }
    assert snapshot.snapshot_digest == (
        "sha256:b28688ff9a6530564456da5eb7decf61957e8e590da8afe657bb5157463fc8dc"
    )


def test_exact_provider_result_and_repository_model_admission_preserve_concrete_facts() -> (  # noqa: E501
    None
):
    """Positive controls pin exact identities, projections, and closure."""
    context = _context()
    manifest = _manifest(context)
    result = _provider_result(context, manifest)
    snapshot = _snapshot()
    bundle = _fact_bundle(manifest, result)
    admission = _admission_context(bundle)

    admitted = admit_node_provider_fact_bundle(
        bundle,
        context=context,
        manifest=manifest,
        admission=admission,
    )
    validate_first_slice_repository_model_snapshot(snapshot)

    assert admitted.bundle == bundle
    assert admitted.admission == admission
    assert result.checkout.authoritative_remote_url == (
        "file:///authoritative-remote.git"
    )
    assert result.project_nodes[0].private is False
    assert result.nbgv.git_commit_id == TARGET
    assert result.nbgv.npm_package_version == NPM_VERSION
    assert result.result_digest == (
        "sha256:6dc1cf66e83f30f9d1730cd0f2eb0c6401b4412711f722490f45179188bedb8f"
    )
    assert snapshot.release_units[0].builds[0].build_id == "npm-package"
    assert snapshot.quality[0].preset == "node/hcoona-release-smoke-npm-v1"
    assert snapshot.ready is True
    assert snapshot.snapshot_digest == (
        "sha256:b28688ff9a6530564456da5eb7decf61957e8e590da8afe657bb5157463fc8dc"
    )
