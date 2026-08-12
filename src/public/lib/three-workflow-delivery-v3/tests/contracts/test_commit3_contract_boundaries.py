"""Contract tests for Workflow Delivery v3 implementation commit 3."""

from __future__ import annotations

import inspect
import json
import os
import subprocess
from dataclasses import (
    FrozenInstanceError,
    asdict,
    dataclass,
    fields,
    is_dataclass,
    replace,
)
from pathlib import Path
from types import SimpleNamespace
from typing import Any, Self, cast

import pytest
from three_workflow_delivery_v3 import repository as repository_module
from three_workflow_delivery_v3.canonical import canonical_sha256
from three_workflow_delivery_v3.catalogs import catalog_digest
from three_workflow_delivery_v3.release import eligibility as eligibility_module
from three_workflow_delivery_v3.release.eligibility import (
    LiveEligibilityContext,
    release_policy_digest,
)
from three_workflow_delivery_v3.repository import compiler as compiler_module
from three_workflow_delivery_v3.repository import (
    node_provider as node_provider_module,
)
from three_workflow_delivery_v3.repository.compiler import (
    CompilationContext,
    CompiledBuild,
    CompiledOutput,
    CompiledQualitySelection,
    CompiledReleaseUnit,
    FactBundleAdmissionContext,
    ProviderRequestManifest,
    RepositoryModelSnapshot,
    compile_release_policy,
    first_slice_provider_manifest,
    provider_binding,
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
    NodeProviderResult,
    ProjectNode,
    create_node_provider_fact_bundle,
    provide_node_repository_facts,
)

REPO_ROOT = Path(__file__).resolve().parents[6]
PRODUCT_PATH = "src/public/lib/hcoona-release-smoke-npm"
TARGET = "e" * 40
RUN_ATTEMPT = 3
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
        run_attempt=RUN_ATTEMPT,
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
        toolchain=(("node", "v24.14.0"), ("pnpm", "11.17.0")),
        manifest_digest=SHA256_B,
        configuration_digest=canonical_sha256(
            {
                "schema": "workflow-delivery/v3/node-provider-configuration",
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


class _TupleSubclass(tuple):
    """Tuple subclass that must not satisfy exact tuple admission."""


class _DigestAccessTrap:
    """Raise if Live Eligibility computes a digest before admission."""

    @property
    def release_unit(self) -> str:
        """Fail if digest serialization reads a release-unit id."""
        message = "digest serialization reached an unadmitted record"
        raise AssertionError(message)

    @property
    def descriptor_path(self) -> str:
        """Fail if digest serialization reads a descriptor path."""
        message = "digest serialization reached an unadmitted record"
        raise AssertionError(message)

    @property
    def builds(self) -> tuple[CompiledBuild, ...]:
        """Fail if digest serialization reads build declarations."""
        message = "digest serialization reached an unadmitted record"
        raise AssertionError(message)


class _ReleasePolicyPathMutationTrap(str):
    """Mutate the admitted closure if release-policy equality is invoked."""

    snapshot: RepositoryModelSnapshot
    comparison_triggered: bool

    def __new__(
        cls,
        snapshot: RepositoryModelSnapshot,
    ) -> Self:
        instance = str.__new__(cls, "surrogate-release-policy.yml")
        instance.snapshot = snapshot
        instance.comparison_triggered = False
        return instance

    def __ne__(self, _other: object) -> bool:
        self.comparison_triggered = True
        object.__setattr__(
            self.snapshot,
            "release_units",
            [*self.snapshot.release_units],
        )
        object.__setattr__(
            self.snapshot,
            "release_policy_path",
            FIRST_SLICE_POLICY_PATH,
        )
        return False


def _record_field_values(value: object) -> dict[str, object]:
    return {
        field.name: getattr(value, field.name)
        for field in fields(cast("Any", value))
    }


def _subclass_record(value: object) -> object:
    record_type = type(value)
    subclass: Any = type(
        f"_Runtime{record_type.__name__}Subclass",
        (record_type,),
        {},
    )
    return subclass(**_record_field_values(value))


def _duck_record(value: object) -> object:
    return SimpleNamespace(**_record_field_values(value))


def _mapping_record(value: object) -> object:
    return _record_field_values(value)


def _list_record(value: object) -> object:
    return list(_record_field_values(value).values())


def _tuple_surrogate(value: tuple[Any, ...], kind: str) -> object:
    if kind == "list":
        return [*value]
    return _TupleSubclass(value)


def _snapshot_with_tuple_surrogate(  # noqa: C901, PLR0911, PLR0912
    snapshot: RepositoryModelSnapshot,
    path: str,
    kind: str,
) -> RepositoryModelSnapshot:
    if path == "provider_result_digests":
        return replace(
            snapshot,
            provider_result_digests=cast(
                "Any",
                _tuple_surrogate(snapshot.provider_result_digests, kind),
            ),
        )
    if path == "project_nodes":
        return replace(
            snapshot,
            project_nodes=cast(
                "Any",
                _tuple_surrogate(snapshot.project_nodes, kind),
            ),
        )
    if path == "release_units":
        return replace(
            snapshot,
            release_units=cast(
                "Any",
                _tuple_surrogate(snapshot.release_units, kind),
            ),
        )
    if path == "project_nodes.workspace_dependencies":
        project = snapshot.project_nodes[0]
        return replace(
            snapshot,
            project_nodes=(
                replace(
                    project,
                    workspace_dependencies=cast(
                        "Any",
                        _tuple_surrogate(project.workspace_dependencies, kind),
                    ),
                ),
            ),
        )
    if path == "release_units.builds":
        release_unit = snapshot.release_units[0]
        return replace(
            snapshot,
            release_units=(
                replace(
                    release_unit,
                    builds=cast(
                        "Any",
                        _tuple_surrogate(release_unit.builds, kind),
                    ),
                ),
            ),
        )
    if path == "release_units.builds.outputs":
        release_unit = snapshot.release_units[0]
        build = release_unit.builds[0]
        return replace(
            snapshot,
            release_units=(
                replace(
                    release_unit,
                    builds=(
                        replace(
                            build,
                            outputs=cast(
                                "Any",
                                _tuple_surrogate(build.outputs, kind),
                            ),
                        ),
                    ),
                ),
            ),
        )
    if path == "release_units.builds.required_native_projections":
        release_unit = snapshot.release_units[0]
        build = release_unit.builds[0]
        return replace(
            snapshot,
            release_units=(
                replace(
                    release_unit,
                    builds=(
                        replace(
                            build,
                            required_native_projections=cast(
                                "Any",
                                _tuple_surrogate(
                                    build.required_native_projections,
                                    kind,
                                ),
                            ),
                        ),
                    ),
                ),
            ),
        )
    if path == "quality":
        return replace(
            snapshot,
            quality=cast("Any", _tuple_surrogate(snapshot.quality, kind)),
        )
    if path == "quality.required":
        quality = snapshot.quality[0]
        return replace(
            snapshot,
            quality=(
                replace(
                    quality,
                    required=cast(
                        "Any",
                        _tuple_surrogate(quality.required, kind),
                    ),
                ),
            ),
        )
    if path == "quality.advisory":
        quality = snapshot.quality[0]
        return replace(
            snapshot,
            quality=(
                replace(
                    quality,
                    advisory=cast(
                        "Any",
                        _tuple_surrogate(quality.advisory, kind),
                    ),
                ),
            ),
        )
    if snapshot.release_policy is None:
        message = "ready Snapshot lacks compiled policy"
        raise AssertionError(message)
    if path == "release_policy.channels":
        return replace(
            snapshot,
            release_policy=replace(
                snapshot.release_policy,
                channels=cast(
                    "Any",
                    _tuple_surrogate(
                        snapshot.release_policy.channels,
                        kind,
                    ),
                ),
            ),
        )
    if path == "release_policy.channel_entry":
        entry = snapshot.release_policy.channels[0]
        return replace(
            snapshot,
            release_policy=replace(
                snapshot.release_policy,
                channels=(
                    cast("Any", _tuple_surrogate(entry, kind)),
                    snapshot.release_policy.channels[1],
                ),
            ),
        )
    if path == "release_policy.channel.quality":
        name, channel = snapshot.release_policy.channels[0]
        return replace(
            snapshot,
            release_policy=replace(
                snapshot.release_policy,
                channels=(
                    (
                        name,
                        replace(
                            channel,
                            quality=cast(
                                "Any",
                                _tuple_surrogate(channel.quality, kind),
                            ),
                        ),
                    ),
                    snapshot.release_policy.channels[1],
                ),
            ),
        )
    if path == "release_policy.channel.projections":
        name, channel = snapshot.release_policy.channels[0]
        return replace(
            snapshot,
            release_policy=replace(
                snapshot.release_policy,
                channels=(
                    (
                        name,
                        replace(
                            channel,
                            projections=cast(
                                "Any",
                                _tuple_surrogate(channel.projections, kind),
                            ),
                        ),
                    ),
                    snapshot.release_policy.channels[1],
                ),
            ),
        )
    if path == "reverse_index":
        return replace(
            snapshot,
            reverse_index=cast(
                "Any",
                _tuple_surrogate(snapshot.reverse_index, kind),
            ),
        )
    if path == "reverse_index.entry":
        entry = snapshot.reverse_index[0]
        return replace(
            snapshot,
            reverse_index=(cast("Any", _tuple_surrogate(entry, kind)),),
        )
    if path == "reverse_index.build_ids":
        project_id, build_ids = snapshot.reverse_index[0]
        return replace(
            snapshot,
            reverse_index=(
                (
                    project_id,
                    cast("Any", _tuple_surrogate(build_ids, kind)),
                ),
            ),
        )
    if path == "unresolved":
        return replace(
            snapshot,
            unresolved=cast(
                "Any",
                _tuple_surrogate(snapshot.unresolved, kind),
            ),
        )
    message = f"unknown tuple substitution path: {path}"
    raise AssertionError(message)


def _snapshot_with_project_node(
    snapshot: RepositoryModelSnapshot,
    project: object,
) -> RepositoryModelSnapshot:
    return replace(snapshot, project_nodes=(cast("Any", project),))


def _snapshot_with_release_unit(
    snapshot: RepositoryModelSnapshot,
    release_unit: object,
) -> RepositoryModelSnapshot:
    return replace(snapshot, release_units=(cast("Any", release_unit),))


def _snapshot_with_build(
    snapshot: RepositoryModelSnapshot,
    build: object,
) -> RepositoryModelSnapshot:
    release_unit = snapshot.release_units[0]
    return replace(
        snapshot,
        release_units=(replace(release_unit, builds=(cast("Any", build),)),),
    )


def _snapshot_with_output(
    snapshot: RepositoryModelSnapshot,
    output: object,
) -> RepositoryModelSnapshot:
    release_unit = snapshot.release_units[0]
    build = release_unit.builds[0]
    return replace(
        snapshot,
        release_units=(
            replace(
                release_unit,
                builds=(replace(build, outputs=(cast("Any", output),)),),
            ),
        ),
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
        run_attempt=snapshot.context.run_attempt,
        selected_ref="refs/heads/feature/ref-neutral",
        target=snapshot.context.target,
        repository_model_digest=snapshot.snapshot_digest,
        producer="evaluate-live-eligibility",
        control=snapshot.context.control,
        release_policy_digest=release_policy_digest(policy),
        catalog_digest=catalog_digest(),
    )


def _validate_live_context(
    context: LiveEligibilityContext,
    snapshot: RepositoryModelSnapshot,
) -> None:
    policy = load_release_policy(REPO_ROOT / FIRST_SLICE_POLICY_PATH)
    eligibility_module._validate_live_context(  # noqa: SLF001
        context,
        snapshot,
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


def _declared_nbgv_environment_allowlist() -> frozenset[str]:
    candidates = (
        "NBGV_ENVIRONMENT_ALLOWLIST",
        "REF_NEUTRAL_ENVIRONMENT_ALLOWLIST",
        "_NBGV_ENVIRONMENT_ALLOWLIST",
        "_REF_NEUTRAL_ENVIRONMENT_ALLOWLIST",
    )
    for name in candidates:
        value = getattr(node_provider_module, name, None)
        if isinstance(value, (tuple, frozenset, set)):
            assert all(isinstance(item, str) and item for item in value)
            return frozenset(value)
    message = (
        "Node Provider must declare and use an explicit ref-neutral NBGV "
        "environment allowlist"
    )
    raise AssertionError(message)


def test_nbgv_provider_declares_explicit_ref_neutral_environment_allowlist() -> (  # noqa: E501
    None
):
    """Exclude every NBGV-recognized CI ref input from authoritative facts."""
    allowlist = _declared_nbgv_environment_allowlist()

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
        "run-attempt",
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
        "pnpm": "11.17.0",
    }
    assert document["global-inputs"] == [
        item.to_document() for item in result.global_inputs
    ]
    assert document["build-capabilities"] == ["node/npm-package-v1"]
    assert document["diagnostic-reference"] is None
    assert result.result_digest.startswith("sha256:")


@dataclass(frozen=True, slots=True)
class _FactBundleBoundary:
    bundle_type: Any
    admission: Any
    field_by_role: dict[str, str]


def _normalized(value: object) -> str:
    return "".join(
        character for character in str(value).casefold() if character.isalnum()
    )


def _public_boundary_objects() -> dict[str, object]:
    modules = (repository_module, node_provider_module, compiler_module)
    objects: dict[str, object] = {}
    for module in modules:
        exported = getattr(module, "__all__", None)
        if exported is None:
            names = tuple(
                name for name in vars(module) if not name.startswith("_")
            )
        else:
            names = tuple(exported)
        for name in names:
            if hasattr(module, name):
                objects[f"{module.__name__}.{name}"] = getattr(module, name)
    return objects


def _semantic_role(name: str, annotation: object) -> str | None:
    text = _normalized(f"{name} {annotation!r}")
    role_conditions = (
        ("binding", "binding" in text),
        ("manifest_digest", "manifest" in text and "digest" in text),
        (
            "manifest_entry",
            "manifest" in text and ("entry" in text or "request" in text),
        ),
        ("request_artifact_digest", "artifact" in text and "digest" in text),
        ("request_artifact_id", "artifact" in text and "id" in text),
        (
            "provider_result_digest",
            "provider" in text and "result" in text and "digest" in text,
        ),
        ("provider_result", "provider" in text and "result" in text),
        ("transport_digest", "transport" in text and "digest" in text),
        ("transport_id", "transport" in text and "id" in text),
    )
    for role, condition in role_conditions:
        if condition:
            return role
    return None


def _fact_bundle_field_roles(bundle_type: Any) -> dict[str, str]:
    role_to_field: dict[str, str] = {}
    for field in fields(bundle_type):
        role = _semantic_role(field.name, field.type)
        if role is not None:
            role_to_field[role] = field.name
    return role_to_field


def _fact_bundle_boundary() -> _FactBundleBoundary:
    required_roles = {
        "binding",
        "manifest_digest",
        "manifest_entry",
        "request_artifact_id",
        "request_artifact_digest",
        "provider_result",
        "provider_result_digest",
        "transport_id",
        "transport_digest",
    }
    public_objects = _public_boundary_objects()
    bundle_candidates: list[tuple[str, Any, dict[str, str]]] = []
    for name, value in public_objects.items():
        if not isinstance(value, type) or not is_dataclass(value):
            continue
        roles = _fact_bundle_field_roles(value)
        semantic_text = _normalized(
            f"{name} {inspect.getdoc(value) or ''} {sorted(roles)}",
        )
        if (
            "fact" in semantic_text and "bundle" in semantic_text
        ) or required_roles.issubset(roles):
            bundle_candidates.append((name, value, roles))
    for _, bundle_type, roles in bundle_candidates:
        if required_roles.issubset(roles):
            admission = _find_fact_bundle_admission(public_objects, bundle_type)
            return _FactBundleBoundary(bundle_type, admission, roles)
    candidates = ", ".join(sorted(public_objects)) or "<none>"
    message = (
        "No public immutable Fact Bundle semantic boundary carries the "
        f"approved roles {sorted(required_roles)}. Public repository "
        f"boundaries: {candidates}"
    )
    raise AssertionError(message)


def _find_fact_bundle_admission(
    public_objects: dict[str, object],
    bundle_type: Any,
) -> Any:
    bundle_type_name = _normalized(bundle_type.__name__)
    for name, value in public_objects.items():
        if inspect.isclass(value) or not callable(value):
            continue
        try:
            signature = str(inspect.signature(value))
        except (TypeError, ValueError):
            signature = ""
        semantic_text = _normalized(
            f"{name} {inspect.getdoc(value) or ''} {signature}",
        )
        has_bundle = (
            "bundle" in semantic_text or bundle_type_name in semantic_text
        )
        has_admission = any(
            token in semantic_text
            for token in ("admit", "admission", "consume", "validate")
        )
        if has_bundle and has_admission:
            return value
    message = (
        "No public Fact Bundle admission boundary accepts the discovered "
        f"{bundle_type.__name__} semantic bundle"
    )
    raise AssertionError(message)


def _fact_bundle(
    boundary: _FactBundleBoundary,
    manifest: ProviderRequestManifest,
    result: NodeProviderResult,
) -> object:
    values_by_role: dict[str, object] = {
        "binding": result.binding,
        "manifest_digest": manifest.manifest_digest,
        "manifest_entry": manifest.requests[0].entry_id,
        "request_artifact_id": 101,
        "request_artifact_digest": SHA256_A,
        "provider_result": result,
        "provider_result_digest": result.result_digest,
        "transport_id": TRANSPORT_ID,
        "transport_digest": SHA256_B,
    }
    values = {
        field: values_by_role[role]
        for role, field in boundary.field_by_role.items()
    }
    if "schema" in {field.name for field in fields(boundary.bundle_type)}:
        values["schema"] = "workflow-delivery/v3/node-provider-fact-bundle"
    return boundary.bundle_type(**values)


def _semantic_document(value: object) -> object:
    to_document = getattr(value, "to_document", None)
    if callable(to_document):
        return to_document()
    if is_dataclass(value):
        return asdict(cast("Any", value))
    return value


def _semantic_leaf_values(value: object) -> tuple[object, ...]:
    if isinstance(value, dict):
        leaves: list[object] = []
        for item in value.values():
            leaves.extend(_semantic_leaf_values(item))
        return tuple(leaves)
    if isinstance(value, list | tuple):
        leaves = []
        for item in value:
            leaves.extend(_semantic_leaf_values(item))
        return tuple(leaves)
    return (value,)


def _admission_argument(
    parameter: inspect.Parameter,
    boundary: _FactBundleBoundary,
    bundle: object,
    context: CompilationContext,
    manifest: ProviderRequestManifest,
) -> object:
    text = _normalized(f"{parameter.name} {parameter.annotation!r}")
    bundle_type_name = _normalized(boundary.bundle_type.__name__)
    if "admission" in text:
        baseline_result = _provider_result(context, manifest)
        baseline_bundle = create_node_provider_fact_bundle(
            baseline_result,
            manifest_digest=manifest.manifest_digest,
            manifest_entry_id=manifest.requests[0].entry_id,
            request_artifact_id=101,
            request_artifact_digest=SHA256_A,
            transport_id=TRANSPORT_ID,
            transport_digest=SHA256_B,
        )
        return FactBundleAdmissionContext(
            request_artifact_id=101,
            request_artifact_digest=SHA256_A,
            transport_id=TRANSPORT_ID,
            transport_digest=SHA256_B,
            bundle_digest=baseline_bundle.bundle_digest,
        )
    if parameter.name == "bundle" or bundle_type_name in text:
        return bundle
    if "context" in text or "compilation" in text:
        return context
    if "manifest" in text:
        return manifest
    message = (
        f"Fact Bundle admission parameter is not semantic: {parameter.name}"
    )
    raise AssertionError(message)


def _admit_fact_bundle(
    boundary: _FactBundleBoundary,
    bundle: object,
    *,
    context: CompilationContext,
    manifest: ProviderRequestManifest,
) -> object:
    try:
        signature = inspect.signature(boundary.admission)
    except (TypeError, ValueError):
        return boundary.admission(bundle, context=context, manifest=manifest)
    positional: list[object] = []
    keywords: dict[str, object] = {}
    for parameter in signature.parameters.values():
        if parameter.kind in {
            inspect.Parameter.VAR_POSITIONAL,
            inspect.Parameter.VAR_KEYWORD,
        }:
            continue
        if (
            parameter.default is not inspect.Parameter.empty
            and _semantic_role(parameter.name, parameter.annotation) is None
            and "context" not in _normalized(parameter.name)
            and "manifest" not in _normalized(parameter.name)
            and "bundle" not in _normalized(parameter.name)
        ):
            continue
        argument = _admission_argument(
            parameter,
            boundary,
            bundle,
            context,
            manifest,
        )
        if parameter.kind is inspect.Parameter.POSITIONAL_ONLY:
            positional.append(argument)
        else:
            keywords[parameter.name] = argument
    return boundary.admission(*positional, **keywords)


def _extract_admitted_result(admitted: object) -> NodeProviderResult:
    if isinstance(admitted, NodeProviderResult):
        return admitted
    if is_dataclass(admitted):
        for field in fields(admitted):
            value = getattr(admitted, field.name)
            if isinstance(value, NodeProviderResult):
                return value
    for name in ("provider_result", "result"):
        value = getattr(admitted, name, None)
        if isinstance(value, NodeProviderResult):
            return value
    message = "Fact Bundle admission did not expose Provider Result"
    raise AssertionError(message)


def _bundle_role_value(
    boundary: _FactBundleBoundary,
    bundle: object,
    role: str,
) -> object:
    return getattr(bundle, boundary.field_by_role[role])


def _replace_bundle_role(
    boundary: _FactBundleBoundary,
    bundle: object,
    role: str,
    value: object,
) -> object:
    return replace(
        cast("Any", bundle),
        **{boundary.field_by_role[role]: value},
    )


def test_fact_bundle_schema_binds_complete_approved_contract() -> None:
    """Require every approved Fact Bundle authority and integrity semantic."""
    context = _context()
    manifest = _manifest(context)
    result = _provider_result(context, manifest)
    boundary = _fact_bundle_boundary()

    bundle = _fact_bundle(boundary, manifest, result)
    document = _semantic_document(bundle)
    admitted = _admit_fact_bundle(
        boundary,
        bundle,
        context=context,
        manifest=manifest,
    )
    leaves = set(_semantic_leaf_values(document))
    required_values = {
        context.request_id,
        context.purpose,
        context.workflow_run_id,
        context.run_attempt,
        context.target,
        result.binding.producer,
        context.control,
        context.catalog_digest,
        manifest.manifest_digest,
        manifest.requests[0].entry_id,
        manifest.requests[0].request_digest,
        result.provider_logical_id,
        result.provider_implementation_id,
        result.execution_mode,
        result.execution_class,
        "v24.14.0",
        "11.17.0",
        result.result_digest,
        SHA256_A,
        101,
        SHA256_B,
        TRANSPORT_ID,
    }

    assert required_values <= leaves
    assert _extract_admitted_result(admitted) is result
    assert _bundle_role_value(boundary, bundle, "binding") == result.binding
    assert _bundle_role_value(boundary, bundle, "transport_id") == TRANSPORT_ID
    with pytest.raises((AttributeError, FrozenInstanceError)):
        setattr(bundle, boundary.field_by_role["transport_id"], 203)


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
    boundary = _fact_bundle_boundary()
    bundle = _fact_bundle(boundary, manifest, result)
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
        bundle = _replace_bundle_role(
            boundary,
            bundle,
            "binding",
            replace(
                cast("Any", _bundle_role_value(boundary, bundle, "binding")),
                **{field: cast("Any", value)},
            ),
        )
    elif mutation == "type-artifact-id":
        bundle = _replace_bundle_role(
            boundary,
            bundle,
            "request_artifact_id",
            BOOLEAN_ID_SURROGATE,
        )
    elif mutation == "type-transport-id":
        bundle = _replace_bundle_role(
            boundary,
            bundle,
            "transport_id",
            BOOLEAN_ID_SURROGATE,
        )
    elif mutation == "manifest-digest":
        bundle = _replace_bundle_role(
            boundary,
            bundle,
            "manifest_digest",
            SHA256_C,
        )
    elif mutation == "manifest-entry":
        bundle = _replace_bundle_role(
            boundary,
            bundle,
            "manifest_entry",
            "other-entry",
        )
    elif mutation == "artifact-digest":
        bundle = _replace_bundle_role(
            boundary,
            bundle,
            "request_artifact_digest",
            SHA256_C,
        )
    elif mutation == "provider-result-digest":
        bundle = _replace_bundle_role(
            boundary,
            bundle,
            "provider_result_digest",
            SHA256_C,
        )
    elif mutation == "transport-digest":
        bundle = _replace_bundle_role(
            boundary,
            bundle,
            "transport_digest",
            SHA256_C,
        )
    else:
        forged_result = replace(result, outcome="blocked")
        assert forged_result.result_digest != result.result_digest
        bundle = _replace_bundle_role(
            boundary,
            bundle,
            "provider_result",
            forged_result,
        )
        bundle = _replace_bundle_role(
            boundary,
            bundle,
            "provider_result_digest",
            forged_result.result_digest,
        )

    admitted = None
    with pytest.raises((TypeError, ValueError)):
        admitted = _admit_fact_bundle(
            boundary,
            bundle,
            context=context,
            manifest=manifest,
        )

    assert admitted is None


def _write_authoritative_compiler_fixture(repo: Path) -> str:
    _run(repo, "git", "init", "--quiet")
    _run(repo, "git", "config", "user.name", "Workflow Delivery Test")
    _run(
        repo,
        "git",
        "config",
        "user.email",
        "workflow-delivery@example.invalid",
    )
    source_product = REPO_ROOT / PRODUCT_PATH
    destination_product = repo / PRODUCT_PATH
    destination_product.mkdir(parents=True)
    for name in (
        "package.json",
        "workflow-delivery.release-unit.yml",
        "workflow-delivery.quality.yml",
    ):
        (destination_product / name).write_bytes(
            (source_product / name).read_bytes()
        )
    policy_path = repo / FIRST_SLICE_POLICY_PATH
    policy_path.parent.mkdir(parents=True)
    policy_path.write_bytes((REPO_ROOT / FIRST_SLICE_POLICY_PATH).read_bytes())
    _run(repo, "git", "add", "--all")
    _run(repo, "git", "commit", "--quiet", "--message", "fixture")
    return _run(repo, "git", "rev-parse", "HEAD")


def test_authoritative_compiler_rejects_unadmitted_target_evaluating_result(
    tmp_path: Path,
) -> None:
    """Do not let a raw target-evaluating Result cross the Decision boundary."""
    repo = tmp_path / "compiler-repository"
    repo.mkdir()
    target = _write_authoritative_compiler_fixture(repo)
    context = _context(target=target)
    manifest = _manifest(context)
    raw_result = _provider_result(context, manifest)
    snapshot = None

    with pytest.raises(
        (TypeError, ValueError),
        match="admitted Fact Bundle",
    ):
        snapshot = compiler_module.compile_repository_model(
            repo,
            context,
            manifest,
            cast("Any", [raw_result]),
        )

    assert snapshot is None


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
        compiler_module._validate_result(  # noqa: SLF001
            context,
            manifest.requests[0],
            forged,
        )

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
    context = replace(_live_context(snapshot), selected_ref=selected_ref)

    _validate_live_context(context, snapshot)

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
    context = replace(
        _live_context(snapshot),
        **{field: cast("Any", value)},
    )

    with pytest.raises((TypeError, ValueError)):
        _validate_live_context(context, snapshot)

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
    assert forged.run_attempt == RUN_ATTEMPT


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


@pytest.mark.parametrize("kind", ["list", "tuple-subclass"])
@pytest.mark.parametrize("path", ["project_nodes", "release_units"])
def test_repository_model_admission_rejects_top_level_tuple_surrogates(
    path: str,
    kind: str,
) -> None:
    """Reject top-level Project Node and Release Unit tuple surrogates."""
    snapshot = _snapshot()
    forged = _snapshot_with_tuple_surrogate(snapshot, path, kind)

    with pytest.raises(TypeError, match="tuple"):
        validate_first_slice_repository_model_snapshot(forged)

    validate_first_slice_repository_model_snapshot(snapshot)
    assert snapshot.project_nodes[0].project_id == (
        "@hcoona/hcoona-release-smoke-npm"
    )
    assert snapshot.release_units[0].release_unit == FIRST_SLICE_RELEASE_UNIT


@pytest.mark.parametrize("kind", ["list", "tuple-subclass"])
@pytest.mark.parametrize(
    "path",
    [
        "provider_result_digests",
        "project_nodes.workspace_dependencies",
        "release_units.builds",
        "release_units.builds.outputs",
        "release_units.builds.required_native_projections",
        "quality",
        "quality.required",
        "quality.advisory",
        "release_policy.channels",
        "release_policy.channel_entry",
        "release_policy.channel.quality",
        "release_policy.channel.projections",
        "reverse_index",
        "reverse_index.entry",
        "reverse_index.build_ids",
        "unresolved",
    ],
)
def test_repository_model_snapshot_admission_rejects_nested_tuple_substitutions(
    path: str,
    kind: str,
) -> None:
    """Reject every Snapshot tuple field when replaced by a surrogate."""
    snapshot = _snapshot()
    forged = _snapshot_with_tuple_surrogate(snapshot, path, kind)

    with pytest.raises(TypeError, match="tuple"):
        validate_first_slice_repository_model_snapshot(forged)

    validate_first_slice_repository_model_snapshot(snapshot)
    assert snapshot.release_units[0].builds[0].outputs[0].kind == (
        "npm-tarball"
    )
    assert snapshot.quality[0].required == (
        "node/project-build-v1",
        "node/project-test-v1",
    )


@pytest.mark.parametrize(
    "surrogate_factory",
    [
        pytest.param(_subclass_record, id="subclass"),
        pytest.param(_duck_record, id="duck"),
        pytest.param(_mapping_record, id="mapping"),
        pytest.param(_list_record, id="list"),
    ],
)
def test_snapshot_admission_and_live_eligibility_reject_top_level_surrogates(
    surrogate_factory: Any,
) -> None:
    """Reject every top-level Snapshot record surrogate at both boundaries."""
    snapshot = _snapshot()
    context = _live_context(snapshot)
    forged = cast("Any", surrogate_factory(snapshot))

    with pytest.raises(TypeError, match="wrong runtime type"):
        validate_first_slice_repository_model_snapshot(forged)
    with pytest.raises(TypeError, match="wrong runtime type"):
        _validate_live_context(context, forged)

    validate_first_slice_repository_model_snapshot(snapshot)
    _validate_live_context(context, snapshot)


@pytest.mark.parametrize(
    ("record_path", "surrogate_factory"),
    [
        pytest.param("context", _subclass_record, id="context-subclass"),
        pytest.param("context", _duck_record, id="context-duck"),
        pytest.param("context", _mapping_record, id="context-mapping"),
        pytest.param("context", _list_record, id="context-list"),
        pytest.param("project", _subclass_record, id="project-subclass"),
        pytest.param("project", _duck_record, id="project-duck"),
        pytest.param("project", _mapping_record, id="project-mapping"),
        pytest.param("project", _list_record, id="project-list"),
        pytest.param(
            "release-unit",
            _subclass_record,
            id="release-unit-subclass",
        ),
        pytest.param("release-unit", _duck_record, id="release-unit-duck"),
        pytest.param(
            "release-unit",
            _mapping_record,
            id="release-unit-mapping",
        ),
        pytest.param("release-unit", _list_record, id="release-unit-list"),
        pytest.param("build", _subclass_record, id="build-subclass"),
        pytest.param("build", _duck_record, id="build-duck"),
        pytest.param("build", _mapping_record, id="build-mapping"),
        pytest.param("build", _list_record, id="build-list"),
        pytest.param("output", _subclass_record, id="output-subclass"),
        pytest.param("output", _duck_record, id="output-duck"),
        pytest.param("output", _mapping_record, id="output-mapping"),
        pytest.param("output", _list_record, id="output-list"),
        pytest.param("quality", _subclass_record, id="quality-subclass"),
        pytest.param("quality", _duck_record, id="quality-duck"),
        pytest.param("quality", _mapping_record, id="quality-mapping"),
        pytest.param("quality", _list_record, id="quality-list"),
        pytest.param(
            "release-policy",
            _subclass_record,
            id="release-policy-subclass",
        ),
        pytest.param(
            "release-policy",
            _duck_record,
            id="release-policy-duck",
        ),
        pytest.param(
            "release-policy",
            _mapping_record,
            id="release-policy-mapping",
        ),
        pytest.param(
            "release-policy",
            _list_record,
            id="release-policy-list",
        ),
        pytest.param(
            "governance",
            _subclass_record,
            id="governance-subclass",
        ),
        pytest.param("governance", _duck_record, id="governance-duck"),
        pytest.param("governance", _mapping_record, id="governance-mapping"),
        pytest.param("governance", _list_record, id="governance-list"),
        pytest.param("nbgv", _subclass_record, id="nbgv-subclass"),
        pytest.param("nbgv", _duck_record, id="nbgv-duck"),
        pytest.param("nbgv", _mapping_record, id="nbgv-mapping"),
        pytest.param("nbgv", _list_record, id="nbgv-list"),
    ],
)
def test_repository_model_snapshot_admission_rejects_record_surrogates(
    record_path: str,
    surrogate_factory: Any,
) -> None:
    """Reject subclasses, duck records, mappings, and lists at record fields."""
    snapshot = _snapshot()
    if record_path == "context":
        forged = replace(
            snapshot,
            context=cast("Any", surrogate_factory(snapshot.context)),
        )
    elif record_path == "project":
        forged = _snapshot_with_project_node(
            snapshot,
            surrogate_factory(snapshot.project_nodes[0]),
        )
    elif record_path == "release-unit":
        forged = _snapshot_with_release_unit(
            snapshot,
            surrogate_factory(snapshot.release_units[0]),
        )
    elif record_path == "build":
        forged = _snapshot_with_build(
            snapshot,
            surrogate_factory(snapshot.release_units[0].builds[0]),
        )
    elif record_path == "output":
        forged = _snapshot_with_output(
            snapshot,
            surrogate_factory(snapshot.release_units[0].builds[0].outputs[0]),
        )
    elif record_path == "quality":
        forged = _snapshot_with_quality(
            snapshot,
            surrogate_factory(snapshot.quality[0]),
        )
    elif record_path == "release-policy":
        assert snapshot.release_policy is not None
        forged = replace(
            snapshot,
            release_policy=cast(
                "Any",
                surrogate_factory(snapshot.release_policy),
            ),
        )
    elif record_path == "governance":
        assert snapshot.release_policy is not None
        forged = replace(
            snapshot,
            release_policy=replace(
                snapshot.release_policy,
                governance=cast(
                    "Any",
                    surrogate_factory(snapshot.release_policy.governance),
                ),
            ),
        )
    else:
        forged = replace(
            snapshot,
            nbgv=cast("Any", surrogate_factory(snapshot.nbgv)),
        )

    with pytest.raises((TypeError, ValueError)):
        validate_first_slice_repository_model_snapshot(forged)

    validate_first_slice_repository_model_snapshot(snapshot)
    assert snapshot.nbgv.npm_package_version == NPM_VERSION
    assert snapshot.release_units[0].builds[0].outputs[0].output_id == (
        "npm-tarball"
    )


def test_live_eligibility_validates_snapshot_admission_before_digest_use() -> (
    None
):
    """Reject an unadmitted record before snapshot digest serialization."""
    snapshot = _snapshot()
    context = _live_context(snapshot)
    forged = replace(
        snapshot,
        release_units=(cast("Any", _DigestAccessTrap()),),
    )

    with pytest.raises(TypeError, match="Release Unit"):
        _validate_live_context(context, forged)

    assert context.repository_model_digest == snapshot.snapshot_digest
    assert snapshot.ready is True


def test_live_eligibility_rejects_digest_equivalent_list_backed_snapshot() -> (
    None
):
    """Block a tuple-to-list TOCTOU mutation that preserves JSON digest."""
    snapshot = _snapshot()
    context = _live_context(snapshot)
    forged = replace(
        snapshot,
        release_units=cast("Any", [*snapshot.release_units]),
    )

    assert forged.snapshot_digest == context.repository_model_digest
    with pytest.raises(TypeError, match=r"release_units.*exact tuple"):
        _validate_live_context(context, forged)

    validate_first_slice_repository_model_snapshot(snapshot)
    assert snapshot.release_units[0].builds[0].build_id == "npm-package"


def test_live_eligibility_blocks_toctou_mutation_during_snapshot_admission() -> (  # noqa: E501
    None
):
    """Reject release-policy surrogates before equality can mutate closure."""
    snapshot = _snapshot()
    context = _live_context(snapshot)
    valid_digest = snapshot.snapshot_digest
    release_units = snapshot.release_units

    assert context.repository_model_digest == valid_digest
    _validate_live_context(context, snapshot)

    trap = _ReleasePolicyPathMutationTrap(snapshot)
    object.__setattr__(snapshot, "release_policy_path", cast("Any", trap))

    with pytest.raises(TypeError, match="release_policy_path"):
        _validate_live_context(context, snapshot)

    assert trap.comparison_triggered is False
    assert snapshot.release_policy_path is trap
    assert type(snapshot.release_units) is tuple
    assert snapshot.release_units is release_units
    assert context.repository_model_digest == valid_digest


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
        "sha256:4dab3519f0d30d29e275f032ce2cd5a1dcf017bde4cbba67b4d556f0810d3f4d"
    )


def test_exact_provider_result_and_repository_model_admission_preserve_concrete_facts() -> (  # noqa: E501
    None
):
    """Positive controls pin exact identities, projections, and closure."""
    context = _context()
    manifest = _manifest(context)
    result = _provider_result(context, manifest)
    snapshot = _snapshot()

    compiler_module._validate_result(  # noqa: SLF001
        context,
        manifest.requests[0],
        result,
    )
    validate_first_slice_repository_model_snapshot(snapshot)

    assert result.checkout.authoritative_remote_url == (
        "file:///authoritative-remote.git"
    )
    assert result.project_nodes[0].private is False
    assert result.nbgv.git_commit_id == TARGET
    assert result.nbgv.npm_package_version == NPM_VERSION
    assert result.result_digest == (
        "sha256:d91a11cc7373641f3298bcf5970aab0b0a2ab9e4def3e37615ce8ad2183a52f7"
    )
    assert snapshot.release_units[0].builds[0].build_id == "npm-package"
    assert snapshot.quality[0].preset == "node/hcoona-release-smoke-npm-v1"
    assert snapshot.ready is True
    assert snapshot.snapshot_digest == (
        "sha256:4dab3519f0d30d29e275f032ce2cd5a1dcf017bde4cbba67b4d556f0810d3f4d"
    )
