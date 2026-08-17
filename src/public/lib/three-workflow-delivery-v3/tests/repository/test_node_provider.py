"""Scenario tests for the exact-target Node/NBGV Provider."""

from __future__ import annotations

import json
import os
import shutil
import subprocess
from collections.abc import Callable, Sequence
from dataclasses import dataclass, replace
from pathlib import Path
from tempfile import TemporaryDirectory
from typing import cast
from urllib.parse import unquote, urlsplit

import pytest
from three_workflow_delivery_v3.canonical import (
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.catalogs import catalog_digest
from three_workflow_delivery_v3.repository.node_provider import (
    AUTHORITATIVE_REMOTE,
    NBGV_ENVIRONMENT_ALLOWLIST,
    TAG_REFSPEC,
    CheckoutMaterialization,
    NodeProviderResult,
    ProjectNode,
    ProviderBinding,
    provide_node_repository_facts,
)

REPO_ROOT = Path(__file__).resolve().parents[6]
PROJECT_PATH = "src/public/lib/hcoona-release-smoke-npm"
REQUEST_DIGEST = "sha256:" + ("a" * 64)
VERSION_HEIGHT = 42
_NBGV_CI_VARIABLES = frozenset(
    {
        "APPVEYOR",
        "APPVEYOR_PULL_REQUEST_NUMBER",
        "APPVEYOR_REPO_BRANCH",
        "APPVEYOR_REPO_TAG",
        "APPVEYOR_REPO_TAG_NAME",
        "BUILD_GIT_BRANCH",
        "BUILD_SOURCEBRANCH",
        "BUILD_VCS_NUMBER",
        "CIRCLE_BRANCH",
        "CIRCLE_SHA1",
        "CIRCLE_TAG",
        "CIRCLECI",
        "CI_COMMIT_REF_NAME",
        "CI_COMMIT_SHA",
        "CI_COMMIT_TAG",
        "GITHUB_ACTIONS",
        "GITHUB_BASE_REF",
        "GITHUB_EVENT_NAME",
        "GITHUB_HEAD_REF",
        "GITHUB_REF",
        "GITHUB_SHA",
        "GITLAB_CI",
        "GIT_BRANCH",
        "GIT_COMMIT",
        "GIT_LOCAL_BRANCH",
        "JENKINS_URL",
        "SYSTEM_TEAMPROJECTID",
    }
)
NBGV_INSTALLATION = (
    REPO_ROOT / PROJECT_PATH / "node_modules/nerdbank-gitversioning"
).resolve()
_CONTRACT_FIXTURE_DIRECTORY = (
    Path(__file__).resolve().parents[1] / "fixtures/repository"
)
_CONTRACT_FIXTURE_DIGESTS = {
    "node-provider-result": (
        "sha256:"
        "fe55544be8ed1c0666dec70195675ec88941b2dcae2ff44d79d1bb79873fe440"
    ),
    "node-provider-fact-bundle": (
        "sha256:"
        "7f691ee4d1a47ed9442372a51ad2a84794f001136dc8935ac66d8ec135ef7745"
    ),
}
_PHASE1_PREPARATION_FETCH_COMMAND = (
    "git",
    "fetch",
    "--force",
    "--prune",
    "--no-tags",
    AUTHORITATIVE_REMOTE,
    TAG_REFSPEC,
)

type RecordedCommand = tuple[tuple[str, ...], Path]
type ProviderRunner = Callable[[tuple[str, ...], Path], str]
type ProviderBindingMutation = Callable[[ProviderBinding], ProviderBinding]


@pytest.mark.parametrize(
    ("fixture_name", "expected_digest"),
    _CONTRACT_FIXTURE_DIGESTS.items(),
)
def test_provider_contract_fixtures_are_canonical_with_golden_digests(
    fixture_name: str,
    expected_digest: str,
) -> None:
    """Pin canonical Provider Result and Fact Bundle payload bytes."""
    fixture = _CONTRACT_FIXTURE_DIRECTORY / f"{fixture_name}.json"
    sidecar = (
        (_CONTRACT_FIXTURE_DIRECTORY / f"{fixture_name}.sha256")
        .read_text(encoding="ascii")
        .strip()
    )
    document = fixture.read_bytes()
    parsed = parse_canonical_json(document)

    assert canonicalize(parsed) == document
    assert canonical_sha256(parsed) == expected_digest
    assert sidecar == expected_digest


def test_fact_bundle_fixture_preserves_complete_provider_result_payload() -> (
    None
):
    """Keep Bundle payload and digest exact and substitution-free."""
    result = parse_canonical_json(
        (_CONTRACT_FIXTURE_DIRECTORY / "node-provider-result.json").read_bytes()
    )
    bundle = parse_canonical_json(
        (
            _CONTRACT_FIXTURE_DIRECTORY / "node-provider-fact-bundle.json"
        ).read_bytes()
    )
    wrapped = bundle["provider-result"]

    assert isinstance(wrapped, dict)
    assert wrapped["payload"] == result
    assert wrapped["payload-canonical-digest"] == canonical_sha256(result)
    assert bundle["schema"] == (
        "workflow-delivery/v3/node-provider-fact-bundle"
    )
    assert bundle["binding"] == result["binding"]
    assert bundle["provider-request-entry-id"] == "node-first-slice"
    assert bundle["request-artifact"] == {
        "artifact-digest": "sha256:" + ("b" * 64),
        "artifact-id": 801,
    }
    assert bundle["transport"] == {
        "artifact-digest": "sha256:" + ("c" * 64),
        "artifact-id": 901,
    }
    input_digests = result["input-digests"]
    assert isinstance(input_digests, dict)
    assert input_digests["manifest"] == "sha256:" + ("b" * 64)
    assert input_digests["configuration"] == canonical_sha256(
        {
            "schema": "workflow-delivery/v3/node-provider-configuration",
            "global-inputs": result["global-inputs"],
        }
    )
    assert result["global-inputs"] == [
        {
            "content-digest": "sha256:" + ("a" * 64),
            "path": path,
            "project-ids": ["@hcoona/hcoona-release-smoke-npm"],
        }
        for path in (
            "package.json",
            "pnpm-lock.yaml",
            "pnpm-workspace.yaml",
            "src/public/lib/hcoona-release-smoke-npm/version.json",
            "version.json",
        )
    ]


def _boolean_integer_fixture(*, value: bool) -> int:
    """Preserve a runtime Boolean for integer-validation regressions."""
    return value


@dataclass(frozen=True, slots=True)
class _SelectedNbgvFacts:
    version: str
    sem_ver1: str
    sem_ver2: str
    version_height: int
    git_commit_id: str
    public_release: bool
    npm_package_version: str


@dataclass(frozen=True, slots=True)
class _DetachedHeadState:
    commit: str
    detached: bool


@dataclass(frozen=True, slots=True)
class _LocalCloneTopology:
    bare_remote: Path
    complete_clone: Path
    no_tags_clone: Path
    target: str


@dataclass(frozen=True, slots=True)
class _WorkspaceDependencyCase:
    sections: tuple[tuple[str, tuple[str, ...]], ...]
    version: str = "workspace:*"


class _RecordingSubprocessRunner:
    """Record Provider commands while delegating to real local processes."""

    def __init__(self) -> None:
        self.commands: list[RecordedCommand] = []

    def __call__(self, command: tuple[str, ...], cwd: Path) -> str:
        self.commands.append((command, cwd))
        try:
            return subprocess.run(  # noqa: S603
                command,
                cwd=cwd,
                check=True,
                capture_output=True,
                text=True,
            ).stdout
        except subprocess.CalledProcessError as error:
            stderr = (error.stderr or "").strip()
            message = f"Provider command failed: {command[0]}: {stderr}"
            raise ValueError(message) from error


class _FailTagFetchRunner:
    """Inject one failure only when production attempts the exact tag fetch."""

    def __init__(self, delegate: _RecordingSubprocessRunner) -> None:
        self.delegate = delegate
        self.failed_fetch_calls: list[tuple[str, ...]] = []

    @property
    def commands(self) -> list[RecordedCommand]:
        return self.delegate.commands

    def __call__(self, command: tuple[str, ...], cwd: Path) -> str:
        if command[:2] == ("git", "fetch") and TAG_REFSPEC in command:
            self.delegate.commands.append((command, cwd))
            self.failed_fetch_calls.append(command)
            message = "injected tag fetch failure"
            raise ValueError(message)
        return self.delegate(command, cwd)


def _noninteractive_environment() -> dict[str, str]:
    environment = os.environ.copy()
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


def _run_fixture_command(command: tuple[str, ...], cwd: Path) -> str:
    try:
        return subprocess.run(  # noqa: S603
            command,
            cwd=cwd,
            env=_noninteractive_environment(),
            check=True,
            capture_output=True,
            text=True,
        ).stdout
    except subprocess.CalledProcessError as error:
        stderr = (error.stderr or "").strip()
        message = f"fixture command failed: {' '.join(command)}: {stderr}"
        raise AssertionError(message) from error


def _create_local_clone_topology(
    tmp_path: Path,
    *,
    public_release_tag: bool = True,
) -> _LocalCloneTopology:
    """Create complete and no-tags clones without preparing the latter."""
    if not (NBGV_INSTALLATION / "package.json").is_file():
        message = "the installed nerdbank-gitversioning package is missing"
        raise AssertionError(message)

    seed = tmp_path / "seed"
    project = seed / PROJECT_PATH
    project.mkdir(parents=True)
    (seed / "package.json").write_text(
        json.dumps({"name": "provider-fixture-root", "private": True}) + "\n",
        encoding="utf-8",
    )
    (seed / "pnpm-workspace.yaml").write_text(
        f"packages:\n  - {PROJECT_PATH}\n",
        encoding="utf-8",
    )
    (seed / "version.json").write_text(
        json.dumps(
            {
                "version": "1.2.3-beta.{height}",
                "gitCommitIdShortFixedLength": 7,
                "publicReleaseRefSpec": [
                    "^refs/heads/main$",
                    "^refs/tags/release/.+/v.+$",
                ],
                "inherit": False,
            },
            indent=2,
        )
        + "\n",
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
                        f"file:{NBGV_INSTALLATION.as_posix()}"
                    )
                },
            },
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )
    _run_fixture_command(
        (
            "pnpm",
            "install",
            "--lockfile-only",
            "--ignore-scripts",
            "--offline",
        ),
        seed,
    )

    _run_fixture_command(("git", "init", "--initial-branch=main"), seed)
    _run_fixture_command(
        ("git", "config", "user.name", "Workflow Delivery Tests"),
        seed,
    )
    _run_fixture_command(
        ("git", "config", "user.email", "tests@example.invalid"),
        seed,
    )
    _run_fixture_command(("git", "config", "commit.gpgSign", "false"), seed)
    _run_fixture_command(("git", "config", "tag.gpgSign", "false"), seed)
    _run_fixture_command(("git", "add", "."), seed)
    _run_fixture_command(
        ("git", "commit", "--no-verify", "-m", "fixture base"),
        seed,
    )
    (seed / "target.txt").write_text("exact target\n", encoding="utf-8")
    _run_fixture_command(("git", "add", "target.txt"), seed)
    _run_fixture_command(
        ("git", "commit", "--no-verify", "-m", "fixture target"),
        seed,
    )
    target = _run_fixture_command(
        ("git", "rev-parse", "HEAD"),
        seed,
    ).strip()
    tag_name = (
        "release/provider-fixture/v1.2.3"
        if public_release_tag
        else "provider-fixture/target"
    )
    _run_fixture_command(("git", "tag", "--no-sign", tag_name), seed)

    bare_remote = tmp_path / "remote.git"
    complete_clone = tmp_path / "complete"
    no_tags_clone = tmp_path / "no-tags"
    _run_fixture_command(
        ("git", "clone", "--bare", seed.as_uri(), str(bare_remote)),
        tmp_path,
    )
    remote_url = bare_remote.resolve().as_uri()
    _assert_local_remote_url(remote_url)
    _run_fixture_command(
        ("git", "clone", remote_url, str(complete_clone)),
        tmp_path,
    )
    _run_fixture_command(
        ("git", "clone", "--no-tags", remote_url, str(no_tags_clone)),
        tmp_path,
    )
    _run_fixture_command(
        ("git", "switch", "--detach", target),
        complete_clone,
    )
    _run_fixture_command(
        ("git", "switch", "--detach", target),
        no_tags_clone,
    )
    return _LocalCloneTopology(
        bare_remote=bare_remote,
        complete_clone=complete_clone,
        no_tags_clone=no_tags_clone,
        target=target,
    )


def _read_detached_head(repo: Path) -> _DetachedHeadState:
    commit = _run_fixture_command(("git", "rev-parse", "HEAD"), repo).strip()
    symbolic = subprocess.run(
        ("git", "symbolic-ref", "--quiet", "HEAD"),
        cwd=repo,
        env=_noninteractive_environment(),
        check=False,
        capture_output=True,
        text=True,
    )
    if symbolic.returncode not in {0, 1}:
        message = f"could not determine detached HEAD state: {symbolic.stderr}"
        raise AssertionError(message)
    return _DetachedHeadState(commit=commit, detached=symbolic.returncode == 1)


def _selected_nbgv_facts(
    result: NodeProviderResult,
) -> _SelectedNbgvFacts:
    return _SelectedNbgvFacts(
        version=result.nbgv.canonical_version,
        sem_ver1=result.nbgv.sem_ver1,
        sem_ver2=result.nbgv.sem_ver2,
        version_height=result.nbgv.version_height,
        git_commit_id=result.nbgv.git_commit_id,
        public_release=result.nbgv.public_release,
        npm_package_version=result.nbgv.npm_package_version,
    )


def _assert_local_remote_url(url: str) -> None:
    parsed = urlsplit(url)
    if parsed.scheme:
        if parsed.scheme != "file" or parsed.netloc not in {"", "localhost"}:
            message = f"network remote URL is forbidden: {url}"
            raise AssertionError(message)
        local_path = Path(unquote(parsed.path))
    else:
        if ":" in url and not Path(url).is_absolute():
            message = f"SCP-like remote URL is forbidden: {url}"
            raise AssertionError(message)
        local_path = Path(url).expanduser().resolve()
    if not local_path.is_absolute():
        message = f"remote URL must resolve to a local absolute path: {url}"
        raise AssertionError(message)


def _tag_fetch_calls(
    commands: Sequence[RecordedCommand],
) -> tuple[tuple[str, ...], ...]:
    return tuple(
        command for command, _ in commands if command[:2] == ("git", "fetch")
    )


def _assert_single_isolated_tag_fetch(
    commands: Sequence[RecordedCommand],
    caller_root: Path,
) -> tuple[str, ...]:
    fetch_records = tuple(
        record for record in commands if record[0][:2] == ("git", "fetch")
    )
    assert len(fetch_records) == _EXPECTED_PREPARATION_FETCH_COUNT
    command, cwd = fetch_records[0]
    assert command == _PHASE1_PREPARATION_FETCH_COMMAND
    resolved_caller = caller_root.resolve()
    resolved_cwd = cwd.resolve()
    assert resolved_cwd != resolved_caller
    assert not resolved_cwd.is_relative_to(resolved_caller)
    return command


def _pnpm_metadata_calls(
    commands: Sequence[RecordedCommand],
) -> tuple[tuple[str, ...], ...]:
    return tuple(
        command
        for command, _ in commands
        if command and command[0] == "pnpm" and "list" in command
    )


def _installed_nbgv_calls(
    commands: Sequence[RecordedCommand],
) -> tuple[tuple[str, ...], ...]:
    return tuple(
        command
        for command, _ in commands
        if command[:3] == ("node", "--input-type=module", "-e")
    )


def _assert_no_nbgv_fallback_calls(
    commands: Sequence[RecordedCommand],
) -> None:
    installed_calls = _installed_nbgv_calls(commands)
    for command, _ in commands:
        executable = Path(command[0]).name
        assert executable not in {"nbgv", "nbgv-setversion"}
        assert not (
            executable == "dotnet" and any(part == "nbgv" for part in command)
        )
        if executable == "node" and command != ("node", "--version"):
            assert command in installed_calls


def _tag_refs(repo: Path) -> tuple[str, ...]:
    output = _run_fixture_command(
        ("git", "for-each-ref", "--format=%(refname)", "refs/tags"),
        repo,
    )
    return tuple(sorted(output.splitlines()))


def _is_shallow(repo: Path) -> bool:
    value = _run_fixture_command(
        ("git", "rev-parse", "--is-shallow-repository"),
        repo,
    ).strip()
    if value not in {"true", "false"}:
        message = f"unexpected shallow state: {value}"
        raise AssertionError(message)
    return value == "true"


def _remote_url(repo: Path) -> str:
    return _run_fixture_command(
        ("git", "remote", "get-url", "origin"),
        repo,
    ).strip()


def _configure_local_only_environment(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    for name, value in _noninteractive_environment().items():
        if name in {
            "GIT_TERMINAL_PROMPT",
            "GCM_INTERACTIVE",
            "GIT_ASKPASS",
            "SSH_ASKPASS",
            "npm_config_offline",
        }:
            monkeypatch.setenv(name, value)


def _head() -> str:
    return subprocess.run(
        ("git", "rev-parse", "HEAD"),
        cwd=REPO_ROOT,
        check=True,
        capture_output=True,
        text=True,
    ).stdout.strip()


def _binding(target: str = "e" * 40) -> ProviderBinding:
    return ProviderBinding(
        request_id="release-request-42",
        purpose="live-release",
        workflow_run_id=7101,
        run_attempt=3,
        target=target,
        producer="discover-node",
        control=f"workflow-delivery-v3:{target}",
        catalog_digest=catalog_digest(),
        request_digest=REQUEST_DIGEST,
    )


def _with_invalid_purpose(binding: ProviderBinding) -> ProviderBinding:
    return replace(binding, purpose="unknown")


def _with_invalid_workflow_run_id(
    binding: ProviderBinding,
) -> ProviderBinding:
    return replace(binding, workflow_run_id=0)


def _with_invalid_run_attempt(binding: ProviderBinding) -> ProviderBinding:
    return replace(
        binding,
        run_attempt=_boolean_integer_fixture(value=True),
    )


def _with_invalid_target(binding: ProviderBinding) -> ProviderBinding:
    return replace(binding, target="E" * 40)


def _with_invalid_catalog_digest(
    binding: ProviderBinding,
) -> ProviderBinding:
    return replace(binding, catalog_digest="sha256:" + ("b" * 64))


def _with_invalid_request_digest(
    binding: ProviderBinding,
) -> ProviderBinding:
    return replace(binding, request_digest="a" * 64)


def _materialization() -> CheckoutMaterialization:
    return CheckoutMaterialization(
        fetch_depth=0,
        credentials_persisted=False,
    )


def _boolean_fetch_depth_materialization() -> CheckoutMaterialization:
    return CheckoutMaterialization(
        fetch_depth=_boolean_integer_fixture(value=False),
        credentials_persisted=False,
    )


def _nbgv_document(target: str = "e" * 40) -> dict[str, object]:
    return {
        "version": "1.2.3",
        "semVer1": "1.2.3-beta-0042-e123456",
        "semVer2": "1.2.3-beta.42.ge123456",
        "versionHeight": 42,
        "gitCommitId": target,
        "publicRelease": False,
        "npmPackageVersion": "1.2.3-beta.42.ge123456",
    }


class RecordingRunner:
    """Deterministic command boundary for Provider contract tests."""

    def __init__(
        self,
        repo_root: Path,
        project_root: Path,
        *,
        target: str = "e" * 40,
    ) -> None:
        """Initialize deterministic command outputs and call recording."""
        self.repo_root = repo_root
        self.project_root = project_root
        self.target = target
        self.commands: list[tuple[tuple[str, ...], Path]] = []
        self.resolved = target
        self.head = target
        self.shallow = "false"
        self.ancestry = f"{target} {'d' * 40}\n"
        self.objects = f"{target}\n"
        self.remote_url = "file:///authoritative-remote.git"
        self.dirty_tracked_inputs = ""
        self.post_pnpm_status = ""
        self.evaluation_root: Path | None = None
        self.nbgv: object = _nbgv_document(target)
        self.pnpm: object = [
            {
                "name": "@hcoona/hcoona-release-smoke-npm",
                "path": str(project_root),
                "private": False,
                "devDependencies": {
                    "nerdbank-gitversioning": {
                        "version": "3.10.91",
                        "path": "/external/nerdbank-gitversioning",
                    },
                },
            },
        ]

    def __call__(  # noqa: C901, PLR0911, PLR0912
        self,
        command: tuple[str, ...],
        cwd: Path,
    ) -> str:
        """Return the configured output for one expected Provider command."""
        self.commands.append((command, cwd))
        if command[:2] == ("git", "clone"):
            isolated_root = Path(command[-1])
            shutil.copytree(self.repo_root, isolated_root)
            self.evaluation_root = isolated_root
            return ""
        if command[:3] == ("git", "remote", "set-url"):
            return ""
        if command[:3] == ("git", "checkout", "--detach"):
            return ""
        if command[:3] == ("git", "rev-parse", "--verify"):
            return f"{self.resolved}\n"
        if command == ("git", "rev-parse", "HEAD"):
            return f"{self.head}\n"
        if command == ("git", "remote", "get-url", AUTHORITATIVE_REMOTE):
            return f"{self.remote_url}\n"
        if command == (
            "git",
            "fetch",
            "--force",
            "--prune",
            "--no-tags",
            AUTHORITATIVE_REMOTE,
            TAG_REFSPEC,
        ):
            return ""
        if command == ("git", "rev-parse", "--is-shallow-repository"):
            return f"{self.shallow}\n"
        if command[:3] == ("git", "rev-list", "--parents"):
            return self.ancestry
        if command[:3] == ("git", "rev-list", "--objects"):
            return self.objects
        if command[:4] == (
            "git",
            "diff",
            "--name-only",
            "--diff-filter=ACDMRTUXB",
        ):
            return self.dirty_tracked_inputs
        if command == (
            "git",
            "status",
            "--porcelain=v1",
            "--ignored",
            "--untracked-files=all",
        ):
            return self.post_pnpm_status
        if command == (
            "pnpm",
            "install",
            "--frozen-lockfile",
            "--ignore-scripts",
            "--ignore-pnpmfile",
        ):
            return ""
        if command[0] == "pnpm" and "list" in command:
            output = json.dumps(self.pnpm)
            if self.evaluation_root is not None:
                output = output.replace(
                    str(self.repo_root),
                    str(self.evaluation_root),
                )
            return output
        if command[:3] == ("node", "--input-type=module", "-e"):
            return json.dumps(self.nbgv)
        if command == ("node", "--version"):
            return "v24.14.0\n"
        if command == ("pnpm", "--version"):
            return "11.21.0\n"
        message = f"unexpected command: {command}"
        raise AssertionError(message)

    @property
    def nbgv_calls(self) -> tuple[tuple[str, ...], ...]:
        """Return only installed Node API invocations."""
        return tuple(
            command
            for command, _ in self.commands
            if command[:3] == ("node", "--input-type=module", "-e")
        )


def _scenario(
    tmp_path: Path,
) -> tuple[Path, Path, RecordingRunner, ProviderBinding]:
    repo = tmp_path / "repo"
    project = repo / PROJECT_PATH
    project.mkdir(parents=True)
    (project / "package.json").write_text("{}\n", encoding="utf-8")
    (project / "version.json").write_text(
        '{"inherit":true}\n',
        encoding="utf-8",
    )
    (repo / "package.json").write_text(
        '{"name":"provider-fixture-root","private":true}\n',
        encoding="utf-8",
    )
    (repo / "pnpm-lock.yaml").write_text(
        "lockfileVersion: '9.0'\n",
        encoding="utf-8",
    )
    (repo / "pnpm-workspace.yaml").write_text(
        f"packages:\n  - {PROJECT_PATH}\n",
        encoding="utf-8",
    )
    (repo / "version.json").write_text(
        '{"version":"1.2.3"}\n',
        encoding="utf-8",
    )
    runner = RecordingRunner(repo, project)
    return repo, project, runner, _binding()


def test_provider_compiles_pnpm_and_nbgv_facts_once_for_exact_target(
    tmp_path: Path,
) -> None:
    """Emit one successful target-bound Provider Result."""
    repo, project, runner, binding = _scenario(tmp_path)

    result = provide_node_repository_facts(
        repo,
        PROJECT_PATH,
        binding,
        _materialization(),
        runner=runner,
    )

    assert result.binding == binding
    assert result.checkout.head == binding.target
    assert result.checkout.ancestry_complete
    assert result.checkout.tags_complete
    assert result.checkout.credentials_persisted is False
    assert result.checkout.authoritative_remote == AUTHORITATIVE_REMOTE
    assert result.checkout.authoritative_remote_url == runner.remote_url
    assert result.checkout.tag_refspec == TAG_REFSPEC
    assert result.project_nodes[0].package_name == (
        "@hcoona/hcoona-release-smoke-npm"
    )
    assert result.project_nodes[0].path == PROJECT_PATH
    assert result.project_nodes[0].manifest_path == (
        f"{PROJECT_PATH}/package.json"
    )
    assert result.project_nodes[0].workspace_dependencies == ()
    assert result.manifest_digest.startswith("sha256:")
    assert result.configuration_digest.startswith("sha256:")
    assert tuple(item.path for item in result.global_inputs) == (
        "package.json",
        "pnpm-lock.yaml",
        "pnpm-workspace.yaml",
        f"{PROJECT_PATH}/version.json",
        "version.json",
    )
    assert result.build_capabilities == ("node/npm-package-v1",)
    assert result.outcome == "success"
    assert result.unresolved == ()
    assert result.conflicts == ()
    assert result.diagnostic_reference is None
    assert len(runner.nbgv_calls) == 1
    assert runner.nbgv_calls[0][3].count("getVersion(") == 1
    nbgv_records = [
        record for record in runner.commands if record[0] in runner.nbgv_calls
    ]
    assert len(nbgv_records) == 1
    assert nbgv_records[0][1] != project
    assert nbgv_records[0][1] != repo
    assert runner.commands[-2:] == [
        (("node", "--version"), repo),
        (("pnpm", "--version"), repo),
    ]
    assert runner.evaluation_root is not None
    assert not runner.evaluation_root.exists()


def test_provider_invokes_installed_node_nbgv_api_without_cli_fallback(
    tmp_path: Path,
) -> None:
    """Import the installed API and never invoke the NBGV CLI."""
    repo, project, runner, binding = _scenario(tmp_path)

    provide_node_repository_facts(
        repo,
        PROJECT_PATH,
        binding,
        _materialization(),
        runner=runner,
    )

    assert len(runner.nbgv_calls) == 1
    program = runner.nbgv_calls[0][3]
    assert "allowedEnvironment" in program
    assert "delete process.env[name]" in program
    assert '"IGNORE_GITHUB_REF": "true"' in program
    assert "PATH" in NBGV_ENVIRONMENT_ALLOWLIST
    assert "await import('nerdbank-gitversioning')" in program
    assert "nbgv.getVersion(process.cwd())" in program
    assert all(
        command[:2] != ("nbgv", "get-version") for command, _ in runner.commands
    )
    assert all(
        "exec nbgv" not in " ".join(command) for command, _ in runner.commands
    )
    assert runner.nbgv_calls[0][0] == "node"
    nbgv_cwds = [
        cwd for command, cwd in runner.commands if command in runner.nbgv_calls
    ]
    assert len(nbgv_cwds) == 1
    assert nbgv_cwds[0] != project
    assert nbgv_cwds[0] != repo


def test_provider_freezes_required_npm_package_version(
    tmp_path: Path,
) -> None:
    """Preserve canonical and native facts plus the original API digest."""
    repo, _, runner, binding = _scenario(tmp_path)

    result = provide_node_repository_facts(
        repo,
        PROJECT_PATH,
        binding,
        _materialization(),
        runner=runner,
    )

    assert result.nbgv.canonical_version == "1.2.3"
    assert result.nbgv.sem_ver1 == "1.2.3-beta-0042-e123456"
    assert result.nbgv.sem_ver2 == "1.2.3-beta.42.ge123456"
    assert result.nbgv.npm_package_version == "1.2.3-beta.42.ge123456"
    assert result.nbgv.git_commit_id == binding.target
    assert result.nbgv.version_height == VERSION_HEIGHT
    assert result.nbgv.node_api_result_digest.startswith("sha256:")
    nbgv_document = result.to_document()["nbgv"]
    assert isinstance(nbgv_document, dict)
    assert nbgv_document["native"] == {
        "npmPackageVersion": "1.2.3-beta.42.ge123456",
    }
    assert result.result_digest.startswith("sha256:")
    assert result.result_digest != result.nbgv.node_api_result_digest


@pytest.mark.parametrize(
    ("failure", "message"),
    [
        ("resolved", "HEAD is not pinned to the exact target"),
        ("head", "HEAD is not pinned to the exact target"),
        ("shallow-proof", "shallow state is not provable"),
        ("shallow", "checkout history is shallow"),
        ("ancestry-empty", "checkout ancestry is incomplete"),
        ("ancestry-target", "checkout ancestry is incomplete"),
        ("objects", "checkout ancestry contains missing objects"),
    ],
    ids=[
        "resolved-target",
        "head-target",
        "shallow-proof",
        "shallow",
        "empty-ancestry",
        "wrong-ancestry-root",
        "missing-object",
    ],
)
def test_provider_rejects_incomplete_checkout_before_nbgv(
    tmp_path: Path,
    failure: str,
    message: str,
) -> None:
    """Reject target, shallow, and ancestry failures before versioning."""
    repo, _, runner, binding = _scenario(tmp_path)
    if failure == "resolved":
        runner.resolved = "d" * 40
    elif failure == "head":
        runner.head = "d" * 40
    elif failure == "shallow-proof":
        runner.shallow = "unknown"
    elif failure == "shallow":
        runner.shallow = "true"
    elif failure == "ancestry-empty":
        runner.ancestry = ""
    elif failure == "ancestry-target":
        runner.ancestry = f"{'d' * 40} {'c' * 40}\n"
    else:
        runner.objects = "?missing-object\n"

    with pytest.raises(ValueError, match=message):
        provide_node_repository_facts(
            repo,
            PROJECT_PATH,
            binding,
            _materialization(),
            runner=runner,
        )

    assert runner.nbgv_calls == ()
    assert all(command[0] != "pnpm" for command, _ in runner.commands)


@pytest.mark.parametrize(
    "path",
    [
        "src/public/lib/hcoona-release-smoke-npm/package.json",
        "version.json",
    ],
    ids=["package-json", "version-json"],
)
def test_provider_rejects_dirty_tracked_metadata_before_pnpm_and_nbgv(
    tmp_path: Path,
    path: str,
) -> None:
    """Reject mutable tracked Provider inputs before package/version facts."""
    repo, _, runner, binding = _scenario(tmp_path)
    runner.dirty_tracked_inputs = f"{path}\n"

    with pytest.raises(
        ValueError,
        match="tracked Provider input differs from the exact target",
    ):
        provide_node_repository_facts(
            repo,
            PROJECT_PATH,
            binding,
            _materialization(),
            runner=runner,
        )

    assert runner.nbgv_calls == ()
    assert all(command[0] != "pnpm" for command, _ in runner.commands)


def test_provider_ignores_untracked_and_irrelevant_dirty_worktree_state(
    tmp_path: Path,
) -> None:
    """Only dirty tracked provider inputs block exact-target fact discovery."""
    repo, _, runner, binding = _scenario(tmp_path)

    result = provide_node_repository_facts(
        repo,
        PROJECT_PATH,
        binding,
        _materialization(),
        runner=runner,
    )

    assert result.outcome == "success"
    diff_calls = [
        command
        for command, _ in runner.commands
        if command[:4]
        == ("git", "diff", "--name-only", "--diff-filter=ACDMRTUXB")
    ]
    assert len(diff_calls) == 1
    assert "--" in diff_calls[0]
    assert "version.json" in diff_calls[0]
    assert PROJECT_PATH in diff_calls[0]
    assert all("docs/wiki/overview.md" not in command for command in diff_calls)
    assert len(runner.nbgv_calls) == 1


@pytest.mark.parametrize(
    ("materialization", "message"),
    [
        (
            CheckoutMaterialization(
                fetch_depth=1,
                credentials_persisted=False,
            ),
            "fetch-depth 0",
        ),
        (
            _boolean_fetch_depth_materialization(),
            "fetch-depth 0",
        ),
        (
            CheckoutMaterialization(
                fetch_depth=0,
                credentials_persisted=True,
            ),
            "disable persisted credentials",
        ),
    ],
    ids=["fetch-depth", "Boolean-fetch-depth", "credentials"],
)
def test_provider_rejects_incomplete_materialization_before_nbgv(
    tmp_path: Path,
    materialization: CheckoutMaterialization,
    message: str,
) -> None:
    """Reject an incomplete caller checkout contract before any tool use."""
    repo, _, runner, binding = _scenario(tmp_path)

    with pytest.raises(ValueError, match=message):
        provide_node_repository_facts(
            repo,
            PROJECT_PATH,
            binding,
            materialization,
            runner=runner,
        )

    assert runner.commands == []
    assert runner.nbgv_calls == ()


def test_provider_installs_metadata_tools_with_scripts_disabled(
    tmp_path: Path,
) -> None:
    """Disable PNPM hooks and prove clean inputs immediately before NBGV."""
    repo, _, runner, binding = _scenario(tmp_path)

    provide_node_repository_facts(
        repo,
        PROJECT_PATH,
        binding,
        _materialization(),
        runner=runner,
    )

    install = (
        "pnpm",
        "install",
        "--frozen-lockfile",
        "--ignore-scripts",
        "--ignore-pnpmfile",
    )
    install_cwds = [
        cwd for command, cwd in runner.commands if command == install
    ]
    assert len(install_cwds) == 1
    assert install_cwds[0] != repo
    nbgv_index = next(
        index
        for index, (command, _) in enumerate(runner.commands)
        if command in runner.nbgv_calls
    )
    install_index = next(
        index
        for index, (command, _) in enumerate(runner.commands)
        if command == install
    )
    list_index = next(
        index
        for index, (command, _) in enumerate(runner.commands)
        if command and command[0] == "pnpm" and "list" in command
    )
    clean_index = next(
        index
        for index, (command, _) in enumerate(runner.commands)
        if command
        == (
            "git",
            "status",
            "--porcelain=v1",
            "--ignored",
            "--untracked-files=all",
        )
    )
    assert "--config.ignore-pnpmfile=true" in runner.commands[list_index][0]
    assert install_index < list_index < clean_index < nbgv_index
    assert all(
        command[:2] not in {("pnpm", "build"), ("pnpm", "test")}
        for command, _ in runner.commands
    )


@pytest.mark.parametrize(
    "status",
    [
        f" M {PROJECT_PATH}/package.json\n",
        f"?? {PROJECT_PATH}/version.json\n",
        f"!! {PROJECT_PATH}/version.json\n",
    ],
    ids=["tracked-package", "untracked-version", "ignored-version"],
)
def test_provider_rejects_pnpm_source_mutation_before_nbgv(
    tmp_path: Path,
    status: str,
) -> None:
    """Reject any post-PNPM source change before authoritative versioning."""
    repo, _, runner, binding = _scenario(tmp_path)
    runner.post_pnpm_status = status

    with pytest.raises(
        ValueError,
        match="changed the isolated exact-target source",
    ):
        provide_node_repository_facts(
            repo,
            PROJECT_PATH,
            binding,
            _materialization(),
            runner=runner,
        )

    assert runner.nbgv_calls == ()
    assert len(_pnpm_metadata_calls(runner.commands)) == 1
    assert runner.evaluation_root is not None
    assert not runner.evaluation_root.exists()


def test_provider_rejects_nbgv_target_mismatch(tmp_path: Path) -> None:
    """Reject version facts computed for any commit except the target."""
    repo, _, runner, binding = _scenario(tmp_path)
    runner.nbgv = _nbgv_document("d" * 40)

    with pytest.raises(
        ValueError,
        match="NBGV Node API result is not bound to the exact target",
    ):
        provide_node_repository_facts(
            repo,
            PROJECT_PATH,
            binding,
            _materialization(),
            runner=runner,
        )

    assert len(runner.nbgv_calls) == 1
    assert all(
        command != ("node", "--version") for command, _ in runner.commands
    )


def test_provider_rejects_missing_npm_projection_without_semver_fallback(
    tmp_path: Path,
) -> None:
    """Do not substitute semVer2 when npmPackageVersion is absent."""
    repo, _, runner, binding = _scenario(tmp_path)
    nbgv = _nbgv_document()
    del nbgv["npmPackageVersion"]
    runner.nbgv = nbgv

    with pytest.raises(
        ValueError,
        match="missing required string fact: npmPackageVersion",
    ):
        provide_node_repository_facts(
            repo,
            PROJECT_PATH,
            binding,
            _materialization(),
            runner=runner,
        )

    assert nbgv["semVer2"] == "1.2.3-beta.42.ge123456"
    assert len(runner.nbgv_calls) == 1


@pytest.mark.parametrize(
    "pnpm",
    [
        [],
        [
            {
                "name": "@hcoona/hcoona-release-smoke-npm",
                "path": "/wrong/path",
            },
        ],
        [
            {
                "name": "@hcoona/hcoona-release-smoke-npm",
                "path": "/first",
            },
            {
                "name": "@hcoona/hcoona-release-smoke-npm",
                "path": "/second",
            },
        ],
    ],
    ids=["missing", "wrong-path", "ambiguous"],
)
def test_provider_rejects_malformed_or_ambiguous_pnpm_metadata(
    tmp_path: Path,
    pnpm: object,
) -> None:
    """Require exactly one PNPM Project Node at the selected path."""
    repo, _, runner, binding = _scenario(tmp_path)
    runner.pnpm = pnpm

    with pytest.raises((TypeError, ValueError), match="PNPM"):
        provide_node_repository_facts(
            repo,
            PROJECT_PATH,
            binding,
            _materialization(),
            runner=runner,
        )

    assert runner.nbgv_calls == ()


@pytest.mark.parametrize(
    "pnpm",
    [
        {},
        [
            {
                "name": "@hcoona/hcoona-release-smoke-npm",
                "path": 42,
            }
        ],
        [
            {
                "name": "@hcoona/hcoona-release-smoke-npm",
                "path": "PROJECT",
                "private": "false",
            }
        ],
        [
            {
                "name": "@hcoona/hcoona-release-smoke-npm",
                "path": "PROJECT",
                "dependencies": [],
            }
        ],
    ],
    ids=["not-array", "path-type", "private-type", "dependency-map-type"],
)
def test_provider_rejects_invalid_pnpm_fact_types(
    tmp_path: Path,
    pnpm: object,
) -> None:
    """Reject loosely typed PNPM metadata rather than coercing it."""
    repo, project, runner, binding = _scenario(tmp_path)
    runner.pnpm = json.loads(json.dumps(pnpm).replace("PROJECT", str(project)))

    with pytest.raises((TypeError, ValueError), match="PNPM"):
        provide_node_repository_facts(
            repo,
            PROJECT_PATH,
            binding,
            _materialization(),
            runner=runner,
        )

    assert runner.nbgv_calls == ()


def _workspace_dependency_document(
    repo: Path,
    project: Path,
    case: _WorkspaceDependencyCase,
) -> list[dict[str, object]]:
    package: dict[str, object] = {
        "name": "@hcoona/hcoona-release-smoke-npm",
        "path": str(project),
        "private": False,
    }
    for section, dependency_names in case.sections:
        dependencies: dict[str, object] = {}
        for dependency_name in dependency_names:
            dependencies[dependency_name] = {
                "path": str(repo / "src/public/lib" / dependency_name),
                "version": case.version,
            }
        package[section] = dependencies
    return [package]


@pytest.mark.parametrize(
    "case",
    [
        _WorkspaceDependencyCase(
            sections=(("dependencies", ("linked-dependency",)),),
        ),
        _WorkspaceDependencyCase(
            sections=(("devDependencies", ("linked-dev-dependency",)),),
        ),
        _WorkspaceDependencyCase(
            sections=(
                ("optionalDependencies", ("linked-optional-dependency",)),
            ),
        ),
        _WorkspaceDependencyCase(
            sections=(("peerDependencies", ("linked-peer-dependency",)),),
        ),
        _WorkspaceDependencyCase(
            sections=(
                ("dependencies", ("linked-one", "linked-two")),
                ("devDependencies", ("linked-three",)),
            ),
            version="link:../linked-workspace",
        ),
    ],
    ids=[
        "dependencies",
        "devDependencies",
        "optionalDependencies",
        "peerDependencies",
        "multiple-dependencies",
    ],
)
def test_provider_rejects_nonempty_workspace_dependency_set_before_nbgv(
    tmp_path: Path,
    case: _WorkspaceDependencyCase,
) -> None:
    """Commit 3 permits one Project Node and no workspace graph closure."""
    repo, project, runner, binding = _scenario(tmp_path)
    runner.pnpm = _workspace_dependency_document(repo, project, case)
    result: NodeProviderResult | None = None
    error: ValueError | None = None

    try:
        result = provide_node_repository_facts(
            repo,
            PROJECT_PATH,
            binding,
            _materialization(),
            runner=runner,
        )
    except ValueError as caught:
        error = caught

    assert len(_pnpm_metadata_calls(runner.commands)) == 1
    if result is not None:
        pytest.fail(
            "commit 3 must reject a nonempty workspace dependency set "
            "before NBGV, but the Provider emitted "
            f"{result.project_nodes[0].workspace_dependencies!r} and made "
            f"{len(runner.nbgv_calls)} NBGV call(s)"
        )
    assert error is not None
    assert "commit 3 permits exactly one Project Node" in str(error)
    assert runner.nbgv_calls == ()
    _assert_no_nbgv_fallback_calls(runner.commands)


def test_provider_accepts_external_dependencies_as_empty_workspace_dependency_set(  # noqa: E501
    tmp_path: Path,
) -> None:
    """Keep ordinary external packages outside the workspace edge set."""
    repo, project, runner, binding = _scenario(tmp_path)
    runner.pnpm = [
        {
            "name": "@hcoona/hcoona-release-smoke-npm",
            "path": str(project),
            "private": False,
            "dependencies": {
                "runtime-external": {
                    "path": "/external/runtime",
                    "version": "workspace:*",
                },
            },
            "devDependencies": {
                "dev-external": {
                    "path": "/external/dev",
                    "version": "link:../dev-external",
                },
            },
            "optionalDependencies": {
                "optional-external": {
                    "path": str(repo / "node_modules/.pnpm/optional-external"),
                    "version": "3.4.5",
                },
            },
            "peerDependencies": {
                "peer-external": {
                    "path": "/external/peer",
                    "version": "4.5.6",
                },
            },
        }
    ]

    result = provide_node_repository_facts(
        repo,
        PROJECT_PATH,
        binding,
        _materialization(),
        runner=runner,
    )

    assert result.outcome == "success"
    assert result.project_nodes[0].workspace_dependencies == ()
    assert len(_pnpm_metadata_calls(runner.commands)) == 1
    assert len(runner.nbgv_calls) == 1
    _assert_no_nbgv_fallback_calls(runner.commands)


@pytest.mark.parametrize(
    ("field", "value", "message"),
    [
        ("version", None, "required string fact: version"),
        ("semVer1", "", "required string fact: semVer1"),
        ("semVer2", 1, "required string fact: semVer2"),
        ("versionHeight", -1, "positive non-Boolean integer"),
        ("versionHeight", True, "positive non-Boolean integer"),
        ("publicRelease", "false", "publicRelease must be Boolean"),
    ],
    ids=[
        "version",
        "semver1",
        "semver2",
        "negative-height",
        "Boolean-height",
        "public-release",
    ],
)
def test_provider_rejects_invalid_required_nbgv_facts(
    tmp_path: Path,
    field: str,
    value: object,
    message: str,
) -> None:
    """Require the complete typed canonical/native NBGV fact contract."""
    repo, _, runner, binding = _scenario(tmp_path)
    nbgv = _nbgv_document()
    nbgv[field] = value
    runner.nbgv = nbgv

    with pytest.raises((TypeError, ValueError), match=message):
        provide_node_repository_facts(
            repo,
            PROJECT_PATH,
            binding,
            _materialization(),
            runner=runner,
        )

    assert len(runner.nbgv_calls) == 1
    assert all(
        command != ("node", "--version") for command, _ in runner.commands
    )


@pytest.mark.parametrize(
    ("project_path", "message"),
    [
        ("../outside", "escapes the repository"),
        ("src/public/lib/missing", "package.json is missing"),
    ],
    ids=["escape", "missing-manifest"],
)
def test_provider_rejects_invalid_project_selection_before_checkout(
    tmp_path: Path,
    project_path: str,
    message: str,
) -> None:
    """Reject an escaping path or a target tree without the selected project."""
    repo, _, runner, binding = _scenario(tmp_path)

    with pytest.raises(ValueError, match=message):
        provide_node_repository_facts(
            repo,
            project_path,
            binding,
            _materialization(),
            runner=runner,
        )

    if project_path == "../outside":
        assert runner.commands == []
    else:
        assert runner.nbgv_calls == ()
        assert all(command[0] != "pnpm" for command, _ in runner.commands)
        assert any(
            command[:2] == ("git", "clone") for command, _ in runner.commands
        )
        assert runner.evaluation_root is not None
        assert not runner.evaluation_root.exists()


@pytest.mark.parametrize(
    ("mutation", "message"),
    [
        (_with_invalid_purpose, "closed purpose"),
        (_with_invalid_workflow_run_id, "positive non-Boolean"),
        (_with_invalid_run_attempt, "positive non-Boolean"),
        (_with_invalid_target, "full lowercase"),
        (_with_invalid_catalog_digest, "current static catalog"),
        (_with_invalid_request_digest, "prefixed lowercase"),
    ],
    ids=[
        "purpose",
        "run-id",
        "attempt",
        "target",
        "catalog",
        "request-digest",
    ],
)
def test_provider_rejects_invalid_request_binding_before_commands(
    tmp_path: Path,
    mutation: ProviderBindingMutation,
    message: str,
) -> None:
    """Validate every authority primitive before target evaluation."""
    repo, _, runner, binding = _scenario(tmp_path)
    binding = mutation(binding)

    with pytest.raises(ValueError, match=message):
        provide_node_repository_facts(
            repo,
            PROJECT_PATH,
            binding,
            _materialization(),
            runner=runner,
        )

    assert runner.commands == []


def test_installed_nbgv_api_returns_exact_head_and_native_projection() -> None:
    """Exercise the installed Node API against real complete local history."""
    target = _head()

    result = provide_node_repository_facts(
        REPO_ROOT,
        PROJECT_PATH,
        _binding(target),
        _materialization(),
    )

    assert result.checkout.target == target
    assert result.checkout.head == target
    assert result.checkout.shallow is False
    assert result.nbgv.git_commit_id == target
    assert result.nbgv.npm_package_version
    assert result.nbgv.npm_package_version != "0.0.0-placeholder"
    assert result.nbgv.node_api_result_digest.startswith("sha256:")
    assert result.project_nodes[0].package_name == (
        "@hcoona/hcoona-release-smoke-npm"
    )


@pytest.mark.parametrize(
    "overrides",
    [
        pytest.param(
            {
                "GITHUB_ACTIONS": "true",
                "GITHUB_REF": "refs/heads/main",
                "GITHUB_SHA": "{target}",
            },
            id="github",
        ),
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
                "SYSTEM_TEAMPROJECTID": "project",
                "BUILD_SOURCEBRANCH": "refs/heads/main",
            },
            id="azure",
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
                "BUILD_VCS_NUMBER": "{target}",
                "BUILD_GIT_BRANCH": "refs/heads/main",
            },
            id="teamcity",
        ),
        pytest.param(
            {
                "JENKINS_URL": "https://jenkins.example.invalid/",
                "GIT_COMMIT": "{target}",
                "GIT_LOCAL_BRANCH": "main",
            },
            id="jenkins",
        ),
        pytest.param(
            {
                "CIRCLECI": "true",
                "CIRCLE_BRANCH": "main",
                "CIRCLE_SHA1": "{target}",
            },
            id="circle",
        ),
    ],
)
def test_detached_target_nbgv_facts_ignore_conflicting_ci_environment(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    overrides: dict[str, str],
) -> None:
    """Keep exact NBGV facts independent of ambient cloud-build refs."""
    topology = _create_local_clone_topology(
        tmp_path,
        public_release_tag=False,
    )
    repo = topology.complete_clone
    expected_head = _DetachedHeadState(
        commit=topology.target,
        detached=True,
    )
    _configure_local_only_environment(monkeypatch)
    for variable in _NBGV_CI_VARIABLES:
        monkeypatch.delenv(variable, raising=False)

    assert _read_detached_head(repo) == expected_head
    baseline_runner = _RecordingSubprocessRunner()
    baseline = provide_node_repository_facts(
        repo,
        PROJECT_PATH,
        _binding(topology.target),
        _materialization(),
        runner=baseline_runner,
    )
    assert _read_detached_head(repo) == expected_head

    for name, value in overrides.items():
        monkeypatch.setenv(name, value.format(target=topology.target))
    assert _read_detached_head(repo) == expected_head
    conflicting_runner = _RecordingSubprocessRunner()
    conflicting = provide_node_repository_facts(
        repo,
        PROJECT_PATH,
        _binding(topology.target),
        _materialization(),
        runner=conflicting_runner,
    )
    assert _read_detached_head(repo) == expected_head

    baseline_calls = _installed_nbgv_calls(baseline_runner.commands)
    conflicting_calls = _installed_nbgv_calls(conflicting_runner.commands)
    assert len(baseline_calls) == 1
    assert len(conflicting_calls) == 1
    assert baseline_calls[0][3].count("getVersion(process.cwd())") == 1
    assert conflicting_calls[0][3].count("getVersion(process.cwd())") == 1
    _assert_no_nbgv_fallback_calls(baseline_runner.commands)
    _assert_no_nbgv_fallback_calls(conflicting_runner.commands)
    assert _selected_nbgv_facts(conflicting) == _selected_nbgv_facts(baseline)


def test_no_tags_clone_preparation_fetches_exact_tag_refspec_without_moving_target(  # noqa: E501
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Prepare complete tags only in isolation and preserve the caller."""
    topology = _create_local_clone_topology(tmp_path)
    _configure_local_only_environment(monkeypatch)
    for variable in (
        "GITHUB_ACTIONS",
        "GITHUB_REF",
        "GITHUB_HEAD_REF",
        "GITHUB_BASE_REF",
    ):
        monkeypatch.delenv(variable, raising=False)
    complete_tags = _tag_refs(topology.complete_clone)
    before_tags = _tag_refs(topology.no_tags_clone)
    expected_head = _DetachedHeadState(
        commit=topology.target,
        detached=True,
    )
    assert _is_shallow(topology.complete_clone) is False
    assert _is_shallow(topology.no_tags_clone) is False
    assert _read_detached_head(topology.complete_clone) == expected_head
    assert _read_detached_head(topology.no_tags_clone) == expected_head
    assert complete_tags
    assert before_tags == ()
    _assert_local_remote_url(_remote_url(topology.no_tags_clone))

    runner = _RecordingSubprocessRunner()
    result = provide_node_repository_facts(
        topology.no_tags_clone,
        PROJECT_PATH,
        _binding(topology.target),
        _materialization(),
        runner=runner,
    )

    after_head = _read_detached_head(topology.no_tags_clone)
    after_tags = _tag_refs(topology.no_tags_clone)
    fetch_call = _assert_single_isolated_tag_fetch(
        runner.commands,
        topology.no_tags_clone,
    )
    assert result.checkout.head == topology.target
    assert result.checkout.authoritative_remote == AUTHORITATIVE_REMOTE
    assert result.checkout.authoritative_remote_url == _remote_url(
        topology.no_tags_clone
    )
    assert result.checkout.tag_refspec == TAG_REFSPEC
    checkout_document = result.to_document()["checkout"]
    assert isinstance(checkout_document, dict)
    assert checkout_document["authoritative-remote"] == AUTHORITATIVE_REMOTE
    assert checkout_document["authoritative-remote-url"] == (
        result.checkout.authoritative_remote_url
    )
    assert checkout_document["tag-refspec"] == TAG_REFSPEC
    assert after_head == expected_head
    assert fetch_call.count(TAG_REFSPEC) == 1
    assert after_tags == before_tags
    installed_calls = _installed_nbgv_calls(runner.commands)
    assert len(installed_calls) == 1
    assert installed_calls[0][3].count("getVersion(process.cwd())") == 1
    _assert_no_nbgv_fallback_calls(runner.commands)


def test_no_tags_clone_after_preparation_matches_complete_clone_nbgv_facts(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Match complete-clone facts only through production tag preparation."""
    topology = _create_local_clone_topology(tmp_path)
    _configure_local_only_environment(monkeypatch)
    for variable in (
        "GITHUB_ACTIONS",
        "GITHUB_REF",
        "GITHUB_HEAD_REF",
        "GITHUB_BASE_REF",
    ):
        monkeypatch.delenv(variable, raising=False)
    expected_head = _DetachedHeadState(
        commit=topology.target,
        detached=True,
    )
    complete_tags_before = _tag_refs(topology.complete_clone)
    no_tags_before = _tag_refs(topology.no_tags_clone)
    complete_runner = _RecordingSubprocessRunner()
    no_tags_runner = _RecordingSubprocessRunner()
    complete = provide_node_repository_facts(
        topology.complete_clone,
        PROJECT_PATH,
        _binding(topology.target),
        _materialization(),
        runner=complete_runner,
    )
    no_tags = provide_node_repository_facts(
        topology.no_tags_clone,
        PROJECT_PATH,
        _binding(topology.target),
        _materialization(),
        runner=no_tags_runner,
    )

    assert _read_detached_head(topology.complete_clone) == expected_head
    assert _read_detached_head(topology.no_tags_clone) == expected_head
    assert _tag_refs(topology.complete_clone) == complete_tags_before
    assert _tag_refs(topology.no_tags_clone) == no_tags_before
    complete_calls = _installed_nbgv_calls(complete_runner.commands)
    no_tags_calls = _installed_nbgv_calls(no_tags_runner.commands)
    assert len(complete_calls) == 1
    assert len(no_tags_calls) == 1
    assert complete_calls[0][3].count("getVersion(process.cwd())") == 1
    assert no_tags_calls[0][3].count("getVersion(process.cwd())") == 1
    _assert_no_nbgv_fallback_calls(complete_runner.commands)
    _assert_no_nbgv_fallback_calls(no_tags_runner.commands)
    fetch_call = _assert_single_isolated_tag_fetch(
        no_tags_runner.commands,
        topology.no_tags_clone,
    )
    assert fetch_call.count(TAG_REFSPEC) == 1
    assert _selected_nbgv_facts(no_tags) == _selected_nbgv_facts(complete)


@pytest.mark.parametrize(
    "failure",
    [
        "missing-remote",
        "unusable-local-remote",
        "injected-fetch-failure",
    ],
)
def test_no_tags_clone_preparation_failure_blocks_before_pnpm_and_nbgv(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    failure: str,
) -> None:
    """Fail closed on each independent local tag-preparation failure."""
    topology = _create_local_clone_topology(tmp_path)
    repo = topology.no_tags_clone
    _configure_local_only_environment(monkeypatch)
    for variable in (
        "GITHUB_ACTIONS",
        "GITHUB_REF",
        "GITHUB_HEAD_REF",
        "GITHUB_BASE_REF",
    ):
        monkeypatch.delenv(variable, raising=False)
    expected_head = _DetachedHeadState(
        commit=topology.target,
        detached=True,
    )
    base_runner = _RecordingSubprocessRunner()
    injected_runner: _FailTagFetchRunner | None = None
    selected_runner: ProviderRunner = base_runner
    if failure == "missing-remote":
        _run_fixture_command(("git", "remote", "remove", "origin"), repo)
        assert _run_fixture_command(("git", "remote"), repo).strip() == ""
    elif failure == "unusable-local-remote":
        unusable_remote = (tmp_path / "missing-remote.git").resolve().as_uri()
        _assert_local_remote_url(unusable_remote)
        _run_fixture_command(
            ("git", "remote", "set-url", "origin", unusable_remote),
            repo,
        )
        assert not (tmp_path / "missing-remote.git").exists()
    else:
        _assert_local_remote_url(_remote_url(repo))
        injected_runner = _FailTagFetchRunner(base_runner)
        selected_runner = injected_runner
    assert _tag_refs(repo) == ()
    assert _read_detached_head(repo) == expected_head
    result: NodeProviderResult | None = None
    error: ValueError | None = None

    try:
        result = provide_node_repository_facts(
            repo,
            PROJECT_PATH,
            _binding(topology.target),
            _materialization(),
            runner=selected_runner,
        )
    except ValueError as caught:
        error = caught

    assert _read_detached_head(repo) == expected_head
    assert _tag_refs(repo) == ()
    fetch_calls = _tag_fetch_calls(base_runner.commands)
    pnpm_calls = tuple(
        command
        for command, _ in base_runner.commands
        if command and command[0] == "pnpm"
    )
    installed_calls = _installed_nbgv_calls(base_runner.commands)
    if failure == "missing-remote":
        assert fetch_calls == ()
    else:
        assert len(fetch_calls) == 1, (
            f"{failure} did not reach the exact production tag fetch; "
            f"observed {fetch_calls!r}, PNPM calls={len(pnpm_calls)}, "
            f"installed NBGV calls={len(installed_calls)}, "
            f"result emitted={result is not None}"
        )
        _assert_single_isolated_tag_fetch(
            base_runner.commands,
            repo,
        )
    assert pnpm_calls == (), (
        "checkout preparation failure reached PNPM: "
        f"{len(pnpm_calls)} call(s), installed NBGV calls="
        f"{len(installed_calls)}, result emitted={result is not None}"
    )
    assert installed_calls == ()
    _assert_no_nbgv_fallback_calls(base_runner.commands)
    assert result is None
    assert error is not None
    if failure == "missing-remote":
        assert "remote" in str(error).lower()
    else:
        assert "fetch" in str(error).lower()
    if injected_runner is not None:
        assert injected_runner.failed_fetch_calls == [fetch_calls[0]]


type _CompleteFactTuple = tuple[
    str,
    str,
    str,
    int,
    str,
    bool,
    str,
    str,
]

_OFFLINE_ENVIRONMENT = {
    "CI": "1",
    "COREPACK_ENABLE_DOWNLOAD_PROMPT": "0",
    "GCM_INTERACTIVE": "Never",
    "GIT_ASKPASS": "/bin/false",
    "GIT_CONFIG_COUNT": "6",
    "GIT_CONFIG_KEY_0": "credential.helper",
    "GIT_CONFIG_KEY_1": "core.askPass",
    "GIT_CONFIG_KEY_2": "protocol.file.allow",
    "GIT_CONFIG_KEY_3": "protocol.http.allow",
    "GIT_CONFIG_KEY_4": "protocol.https.allow",
    "GIT_CONFIG_KEY_5": "protocol.ssh.allow",
    "GIT_CONFIG_VALUE_0": "",
    "GIT_CONFIG_VALUE_1": "/bin/false",
    "GIT_CONFIG_VALUE_2": "always",
    "GIT_CONFIG_VALUE_3": "never",
    "GIT_CONFIG_VALUE_4": "never",
    "GIT_CONFIG_VALUE_5": "never",
    "GIT_SSH_COMMAND": "/bin/false",
    "GIT_TERMINAL_PROMPT": "0",
    "NPM_CONFIG_AUDIT": "false",
    "NPM_CONFIG_FUND": "false",
    "NPM_CONFIG_IGNORE_SCRIPTS": "true",
    "NPM_CONFIG_OFFLINE": "true",
    "SSH_ASKPASS": "/bin/false",
    "SSH_ASKPASS_REQUIRE": "force",
    "npm_config_offline": "true",
}
_RECORDED_ENVIRONMENT_NAMES = tuple(sorted(_OFFLINE_ENVIRONMENT))
_ANNOTATED_FIXTURE_TAG = "refs/tags/history/provider-fixture-base"
_LIGHTWEIGHT_FIXTURE_TAG = "refs/tags/release/provider-fixture/v1.2.3"
_FIXTURE_HISTORY_COUNT = 2
_EXPECTED_PREPARATION_FETCH_COUNT = 1
_GIT_PREPARATION_VERIFY_COUNT = 2
_EXPECTED_FIXTURE_TAGS = (
    _ANNOTATED_FIXTURE_TAG,
    _LIGHTWEIGHT_FIXTURE_TAG,
)
_VERSION_OVERRIDE_BYTES = (
    json.dumps(
        {
            "version": "9.8.7-ambient.{height}",
            "gitCommitIdShortFixedLength": 9,
            "publicReleaseRefSpec": ["^refs/heads/ambient-only$"],
            "inherit": False,
        },
        indent=2,
    )
    + "\n"
).encode()
_PNPM_OVERRIDE_BYTES = b"""\
module.exports = {
  hooks: {
    readPackage(pkg) {
      if (pkg.name === '@hcoona/hcoona-release-smoke-npm') {
        pkg.dependencies = {
          ...(pkg.dependencies || {}),
          'isolated-provider-fixture-root': 'workspace:*',
        };
      }
      return pkg;
    },
  },
};
"""


@dataclass(frozen=True, slots=True)
class _RealLocalNbgvRepository:
    bare_origin: Path
    baseline_checkout: Path
    caller_checkout: Path
    base: str
    target: str
    tags: tuple[str, ...]
    baseline_facts: _CompleteFactTuple
    baseline_project_nodes: tuple[ProjectNode, ...]


@dataclass(frozen=True, slots=True)
class _AmbientOverride:
    kind: str
    relative_path: str
    contents: bytes
    ignored: bool


@dataclass(frozen=True, slots=True)
class _SourceCheckoutSnapshot:
    entries: tuple[tuple[str, str, bytes], ...]
    status: str
    worktrees: tuple[Path, ...]
    artifact_states: tuple[tuple[str, str, bytes], ...]


@dataclass(frozen=True, slots=True)
class _EvaluationDirectoryProbe:
    root: Path
    head: str
    detached: bool
    shallow: bool
    history_count: int
    base_is_ancestor: bool
    merge_base: str
    objects: tuple[str, ...]
    tags: tuple[str, ...]
    annotated_tag_type: str
    lightweight_tag_type: str
    remote_url: str
    isolated_git_directory: bool
    registered_worktrees: tuple[Path, ...]


@dataclass(frozen=True, slots=True)
class _EvaluationContext:
    caller_checkout: Path
    target: str
    base: str
    expected_tags: tuple[str, ...]
    bare_origin: Path
    environment: dict[str, str]


@dataclass(frozen=True, slots=True)
class _IsolatedRecordedCommand:
    command: tuple[str, ...]
    cwd: Path
    environment: tuple[tuple[str, str], ...]


def _offline_provider_environment(
    monkeypatch: pytest.MonkeyPatch,
) -> dict[str, str]:
    environment = os.environ.copy()
    environment.update(_OFFLINE_ENVIRONMENT)
    for name, value in _OFFLINE_ENVIRONMENT.items():
        monkeypatch.setenv(name, value)
    for name in (
        "GITHUB_ACTIONS",
        "GITHUB_BASE_REF",
        "GITHUB_HEAD_REF",
        "GITHUB_REF",
        "HTTP_PROXY",
        "HTTPS_PROXY",
        "SSH_AUTH_SOCK",
        "http_proxy",
        "https_proxy",
    ):
        monkeypatch.delenv(name, raising=False)
        environment.pop(name, None)
    return environment


def _run_real_local_command(
    command: tuple[str, ...],
    cwd: Path,
    environment: dict[str, str],
) -> str:
    try:
        return subprocess.run(  # noqa: S603
            command,
            cwd=cwd,
            env=environment,
            check=True,
            capture_output=True,
            text=True,
        ).stdout
    except subprocess.CalledProcessError as error:
        stderr = (error.stderr or "").strip()
        stdout = (error.stdout or "").strip()
        message = (
            f"local fixture command failed: {' '.join(command)}: "
            f"{stderr or stdout}"
        )
        raise AssertionError(message) from error


def _complete_fact_tuple(result: NodeProviderResult) -> _CompleteFactTuple:
    return (
        result.nbgv.canonical_version,
        result.nbgv.sem_ver1,
        result.nbgv.sem_ver2,
        result.nbgv.version_height,
        result.nbgv.git_commit_id,
        result.nbgv.public_release,
        result.nbgv.npm_package_version,
        result.nbgv.node_api_result_digest,
    )


def _worktree_paths(
    repo: Path,
    environment: dict[str, str],
) -> tuple[Path, ...]:
    output = _run_real_local_command(
        ("git", "worktree", "list", "--porcelain"),
        repo,
        environment,
    )
    return tuple(
        Path(line.removeprefix("worktree ")).resolve()
        for line in output.splitlines()
        if line.startswith("worktree ")
    )


def _real_local_nbgv_repository(
    tmp_path: Path,
    environment: dict[str, str],
) -> _RealLocalNbgvRepository:
    if not (NBGV_INSTALLATION / "package.json").is_file():
        message = "the installed nerdbank-gitversioning package is missing"
        raise AssertionError(message)

    seed = tmp_path / "seed"
    project = seed / PROJECT_PATH
    project.mkdir(parents=True)
    (seed / "package.json").write_text(
        json.dumps(
            {
                "name": "isolated-provider-fixture-root",
                "private": True,
            },
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )
    (seed / "pnpm-workspace.yaml").write_text(
        f"packages:\n  - {PROJECT_PATH}\n",
        encoding="utf-8",
    )
    (seed / "version.json").write_text(
        json.dumps(
            {
                "version": "1.2.3-beta.{height}",
                "gitCommitIdShortFixedLength": 7,
                "publicReleaseRefSpec": [
                    "^refs/heads/main$",
                    "^refs/tags/release/provider-fixture/v.+$",
                ],
                "inherit": False,
            },
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )
    (project / "package.json").write_text(
        json.dumps(
            {
                "name": "@hcoona/hcoona-release-smoke-npm",
                "version": "0.0.0-placeholder",
                "type": "module",
                "private": False,
                "devDependencies": {
                    "nerdbank-gitversioning": (
                        f"file:{NBGV_INSTALLATION.as_posix()}"
                    )
                },
            },
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )
    _run_real_local_command(
        (
            "pnpm",
            "install",
            "--lockfile-only",
            "--ignore-scripts",
            "--offline",
        ),
        seed,
        environment,
    )
    _run_real_local_command(
        ("git", "init", "--initial-branch=main"),
        seed,
        environment,
    )
    for command in (
        ("git", "config", "user.name", "Workflow Delivery Tests"),
        ("git", "config", "user.email", "tests@example.invalid"),
        ("git", "config", "commit.gpgSign", "false"),
        ("git", "config", "tag.gpgSign", "false"),
        ("git", "add", "."),
        ("git", "commit", "--no-verify", "-m", "fixture base"),
    ):
        _run_real_local_command(command, seed, environment)
    base = _run_real_local_command(
        ("git", "rev-parse", "HEAD"),
        seed,
        environment,
    ).strip()
    _run_real_local_command(
        (
            "git",
            "tag",
            "--annotate",
            "--no-sign",
            "-m",
            "provider fixture base",
            _ANNOTATED_FIXTURE_TAG.removeprefix("refs/tags/"),
            base,
        ),
        seed,
        environment,
    )
    (seed / "target.txt").write_text(
        "authoritative exact target\n",
        encoding="utf-8",
    )
    for command in (
        ("git", "add", "target.txt"),
        ("git", "commit", "--no-verify", "-m", "fixture exact target"),
    ):
        _run_real_local_command(command, seed, environment)
    target = _run_real_local_command(
        ("git", "rev-parse", "HEAD"),
        seed,
        environment,
    ).strip()
    _run_real_local_command(
        (
            "git",
            "tag",
            "--no-sign",
            _LIGHTWEIGHT_FIXTURE_TAG.removeprefix("refs/tags/"),
            target,
        ),
        seed,
        environment,
    )

    bare_origin = tmp_path / "origin.git"
    _run_real_local_command(
        ("git", "init", "--bare", str(bare_origin)),
        tmp_path,
        environment,
    )
    origin_url = bare_origin.resolve().as_uri()
    _assert_local_remote_url(origin_url)
    _run_real_local_command(
        ("git", "remote", "add", "origin", origin_url),
        seed,
        environment,
    )
    _run_real_local_command(
        ("git", "push", "--all", "origin"),
        seed,
        environment,
    )
    _run_real_local_command(
        ("git", "push", "--tags", "origin"),
        seed,
        environment,
    )

    baseline_checkout = tmp_path / "baseline"
    caller_checkout = tmp_path / "caller"
    for checkout in (baseline_checkout, caller_checkout):
        _run_real_local_command(
            ("git", "clone", origin_url, str(checkout)),
            tmp_path,
            environment,
        )
        _run_real_local_command(
            ("git", "switch", "--detach", target),
            checkout,
            environment,
        )
        assert _remote_url(checkout) == origin_url
        assert _read_detached_head(checkout) == _DetachedHeadState(
            commit=target,
            detached=True,
        )
        assert _tag_refs(checkout) == _EXPECTED_FIXTURE_TAGS

    baseline_context = _EvaluationContext(
        caller_checkout=baseline_checkout,
        target=target,
        base=base,
        expected_tags=_EXPECTED_FIXTURE_TAGS,
        bare_origin=bare_origin,
        environment=environment,
    )
    baseline_runner = _DelegatingRecordingRunner(context=baseline_context)
    baseline = provide_node_repository_facts(
        baseline_checkout,
        PROJECT_PATH,
        _binding(target),
        _materialization(),
        runner=baseline_runner,
    )
    baseline_facts = _complete_fact_tuple(baseline)
    assert baseline_facts[4] == target
    assert baseline.project_nodes[0].private is False
    assert baseline.project_nodes[0].workspace_dependencies == ()
    assert baseline.checkout.head == target
    assert baseline.checkout.shallow is False
    return _RealLocalNbgvRepository(
        bare_origin=bare_origin,
        baseline_checkout=baseline_checkout,
        caller_checkout=caller_checkout,
        base=base,
        target=target,
        tags=_EXPECTED_FIXTURE_TAGS,
        baseline_facts=baseline_facts,
        baseline_project_nodes=baseline.project_nodes,
    )


def _path_state(path: Path) -> tuple[str, bytes]:
    if path.is_symlink():
        return ("symlink", os.fsencode(path.readlink()))
    if path.is_file():
        return ("file", path.read_bytes())
    if path.is_dir():
        return ("directory", b"")
    return ("absent", b"")


def _source_checkout_snapshot(
    repo: Path,
    environment: dict[str, str],
) -> _SourceCheckoutSnapshot:
    entries: list[tuple[str, str, bytes]] = []
    for path in sorted(repo.rglob("*")):
        relative = path.relative_to(repo)
        if ".git" in relative.parts:
            continue
        if "node_modules" in relative.parts[:-1]:
            continue
        kind, contents = _path_state(path)
        entries.append((relative.as_posix(), kind, contents))
    status = _run_real_local_command(
        (
            "git",
            "status",
            "--porcelain=v1",
            "--ignored",
            "--untracked-files=all",
        ),
        repo,
        environment,
    )
    artifact_paths = (
        ".pnpmfile.cjs",
        "node_modules",
        "package-lock.json",
        "package.json",
        "pnpm-lock.yaml",
        "pnpm-workspace.yaml",
        "version.json",
        f"{PROJECT_PATH}/node_modules",
        f"{PROJECT_PATH}/package-lock.json",
        f"{PROJECT_PATH}/package.json",
        f"{PROJECT_PATH}/pnpm-lock.yaml",
        f"{PROJECT_PATH}/pnpm-workspace.yaml",
        f"{PROJECT_PATH}/version.json",
    )
    artifact_states = tuple(
        (relative_path, *_path_state(repo / relative_path))
        for relative_path in artifact_paths
    )
    return _SourceCheckoutSnapshot(
        entries=tuple(entries),
        status=status,
        worktrees=_worktree_paths(repo, environment),
        artifact_states=artifact_states,
    )


def _write_ambient_override(
    repo: Path,
    *,
    kind: str,
    ignored: bool,
) -> _AmbientOverride:
    if kind == "version-json":
        relative_path = f"{PROJECT_PATH}/version.json"
        contents = _VERSION_OVERRIDE_BYTES
    elif kind == "pnpm-metadata":
        relative_path = ".pnpmfile.cjs"
        contents = _PNPM_OVERRIDE_BYTES
    else:
        message = f"unknown ambient override kind: {kind}"
        raise AssertionError(message)
    if ignored:
        exclude = repo / ".git/info/exclude"
        existing = exclude.read_text(encoding="utf-8")
        exclude.write_text(
            f"{existing.rstrip()}\n/{relative_path}\n",
            encoding="utf-8",
        )
    path = repo / relative_path
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(contents)
    return _AmbientOverride(
        kind=kind,
        relative_path=relative_path,
        contents=contents,
        ignored=ignored,
    )


def _assert_override_snapshot(
    snapshot: _SourceCheckoutSnapshot,
    override: _AmbientOverride,
) -> None:
    expected_prefix = "!!" if override.ignored else "??"
    assert (
        f"{expected_prefix} {override.relative_path}"
        in snapshot.status.splitlines()
    )
    matching_entries = tuple(
        entry
        for entry in snapshot.entries
        if entry[0] == override.relative_path
    )
    assert matching_entries == (
        (override.relative_path, "file", override.contents),
    )


def _direct_ambient_control(
    repository: _RealLocalNbgvRepository,
    override: _AmbientOverride,
    environment: dict[str, str],
) -> None:
    caller = repository.caller_checkout
    if override.kind == "version-json":
        api_url = (NBGV_INSTALLATION / "index.js").resolve().as_uri()
        project = (caller / PROJECT_PATH).resolve()
        program = (
            f"const nbgv = await import({json.dumps(api_url)});"
            f"const facts = await nbgv.getVersion({json.dumps(str(project))});"
            "process.stdout.write(JSON.stringify(facts));"
        )
        output = _run_real_local_command(
            ("node", "--input-type=module", "-e", program),
            project,
            environment,
        )
        document = json.loads(output)
        assert isinstance(document, dict)
        assert document["version"] == "9.8.7"
        assert document["gitCommitId"] == repository.target
        direct_facts = (
            document["version"],
            document["semVer1"],
            document["semVer2"],
            document["versionHeight"],
            document["gitCommitId"],
            document["publicRelease"],
            document["npmPackageVersion"],
        )
        assert direct_facts != repository.baseline_facts[:-1]
        return

    copied_inputs = (
        "package.json",
        "pnpm-lock.yaml",
        "pnpm-workspace.yaml",
        override.relative_path,
        f"{PROJECT_PATH}/package.json",
    )
    with TemporaryDirectory(
        prefix="ambient-pnpm-control-",
        dir=caller.parent,
    ) as temporary_directory:
        control_root = Path(temporary_directory)
        for relative_path in copied_inputs:
            destination = control_root / relative_path
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes((caller / relative_path).read_bytes())
        _run_real_local_command(
            (
                "pnpm",
                "install",
                "--lockfile-only",
                "--no-frozen-lockfile",
                "--ignore-scripts",
                "--offline",
            ),
            control_root,
            environment,
        )
        output = _run_real_local_command(
            (
                "pnpm",
                "--dir",
                ".",
                "--filter",
                "@hcoona/hcoona-release-smoke-npm...",
                "list",
                "--json",
                "--depth",
                "Infinity",
            ),
            control_root,
            environment,
        )
        document = json.loads(output)
        assert isinstance(document, list)
        assert len(document) == 1
        package = document[0]
        assert isinstance(package, dict)
        assert package["name"] == "@hcoona/hcoona-release-smoke-npm"
        assert package["path"] == str((control_root / PROJECT_PATH).resolve())
        assert package["private"] is False
        assert package["dependencies"] == {
            "isolated-provider-fixture-root": {
                "from": "isolated-provider-fixture-root",
                "version": "link:../../../..",
                "path": str(control_root.resolve()),
            }
        }
        assert repository.baseline_project_nodes[0].workspace_dependencies == ()
    assert not control_root.exists()


def _probe_command(
    command: tuple[str, ...],
    cwd: Path,
    environment: dict[str, str],
) -> str:
    return _run_real_local_command(command, cwd, environment).strip()


def _evaluation_directory_probe(
    root: Path,
    context: _EvaluationContext,
) -> _EvaluationDirectoryProbe:
    symbolic = subprocess.run(
        ("git", "symbolic-ref", "--quiet", "HEAD"),
        cwd=root,
        env=context.environment,
        check=False,
        capture_output=True,
        text=True,
    )
    assert symbolic.returncode == 1
    head = _probe_command(
        ("git", "rev-parse", "HEAD"),
        root,
        context.environment,
    )
    resolved = _probe_command(
        (
            "git",
            "rev-parse",
            "--verify",
            f"{context.target}^{{commit}}",
        ),
        root,
        context.environment,
    )
    shallow_text = _probe_command(
        ("git", "rev-parse", "--is-shallow-repository"),
        root,
        context.environment,
    )
    assert resolved == context.target
    assert head == context.target
    assert shallow_text == "false"
    history_count = int(
        _probe_command(
            ("git", "rev-list", "--count", context.target),
            root,
            context.environment,
        )
    )
    ancestor = subprocess.run(  # noqa: S603
        (
            "git",
            "merge-base",
            "--is-ancestor",
            context.base,
            context.target,
        ),
        cwd=root,
        env=context.environment,
        check=False,
        capture_output=True,
        text=True,
    )
    assert ancestor.returncode == 0
    merge_base = _probe_command(
        ("git", "merge-base", "HEAD", context.target),
        root,
        context.environment,
    )
    objects = (context.base, context.target)
    for object_name in (
        *objects,
        f"{_ANNOTATED_FIXTURE_TAG}^{{tag}}",
        f"{_LIGHTWEIGHT_FIXTURE_TAG}^{{commit}}",
    ):
        _probe_command(
            ("git", "cat-file", "-e", object_name),
            root,
            context.environment,
        )
    tags_output = _probe_command(
        (
            "git",
            "for-each-ref",
            "--format=%(refname)",
            "refs/tags",
        ),
        root,
        context.environment,
    )
    tags = tuple(sorted(tags_output.splitlines()))
    annotated_tag_type = _probe_command(
        ("git", "cat-file", "-t", _ANNOTATED_FIXTURE_TAG),
        root,
        context.environment,
    )
    lightweight_tag_type = _probe_command(
        ("git", "cat-file", "-t", _LIGHTWEIGHT_FIXTURE_TAG),
        root,
        context.environment,
    )
    remote_url = _probe_command(
        ("git", "remote", "get-url", "origin"),
        root,
        context.environment,
    )
    assert history_count == _FIXTURE_HISTORY_COUNT
    assert merge_base == context.target
    assert tags == context.expected_tags
    assert annotated_tag_type == "tag"
    assert lightweight_tag_type == "commit"
    assert remote_url == context.bare_origin.resolve().as_uri()
    return _EvaluationDirectoryProbe(
        root=root,
        head=head,
        detached=True,
        shallow=False,
        history_count=history_count,
        base_is_ancestor=True,
        merge_base=merge_base,
        objects=objects,
        tags=tags,
        annotated_tag_type=annotated_tag_type,
        lightweight_tag_type=lightweight_tag_type,
        remote_url=remote_url,
        isolated_git_directory=(root / ".git").is_dir(),
        registered_worktrees=_worktree_paths(
            context.caller_checkout,
            context.environment,
        ),
    )


def _evaluation_root(command: tuple[str, ...], cwd: Path) -> Path | None:
    if (
        command
        and command[0] == "pnpm"
        and command
        != (
            "pnpm",
            "--version",
        )
    ):
        return cwd.resolve()
    if command[:3] != ("node", "--input-type=module", "-e"):
        return None
    root = cwd.resolve()
    for _ in Path(PROJECT_PATH).parts:
        root = root.parent
    return root


class _DelegatingRecordingRunner:
    def __init__(
        self,
        *,
        context: _EvaluationContext,
        failure_boundary: str | None = None,
    ) -> None:
        self.context = context
        self.caller_checkout = context.caller_checkout.resolve()
        self.environment = context.environment.copy()
        self.failure_boundary = failure_boundary
        self.commands: list[_IsolatedRecordedCommand] = []
        self.probes: dict[Path, _EvaluationDirectoryProbe] = {}
        self.remote_urls: list[str] = []
        self.git_preparation_failure_roots: list[Path] = []

    @property
    def evaluation_roots(self) -> tuple[Path, ...]:
        return tuple(sorted(self.probes))

    def __call__(self, command: tuple[str, ...], cwd: Path) -> str:
        resolved_cwd = cwd.resolve()
        environment = tuple(
            (name, self.environment[name])
            for name in _RECORDED_ENVIRONMENT_NAMES
        )
        self.commands.append(
            _IsolatedRecordedCommand(
                command=command,
                cwd=resolved_cwd,
                environment=environment,
            )
        )
        if (
            self.failure_boundary == "git-preparation"
            and command[:3] == ("git", "rev-parse", "--verify")
            and resolved_cwd != self.caller_checkout
            and (resolved_cwd / ".git").is_dir()
            and sum(
                record.command[:3] == ("git", "rev-parse", "--verify")
                and record.cwd == resolved_cwd
                for record in self.commands
            )
            >= _GIT_PREPARATION_VERIFY_COUNT
        ):
            self.probes[resolved_cwd] = _evaluation_directory_probe(
                resolved_cwd,
                self.context,
            )
            self.git_preparation_failure_roots.append(resolved_cwd)
            message = "injected Git exact-target preparation failure"
            raise ValueError(message)
        evaluation_root = _evaluation_root(command, resolved_cwd)
        if evaluation_root is not None and evaluation_root not in self.probes:
            self.probes[evaluation_root] = _evaluation_directory_probe(
                evaluation_root,
                self.context,
            )
        if (
            self.failure_boundary == "pnpm-metadata"
            and command
            and command[0] == "pnpm"
            and command != ("pnpm", "--version")
        ):
            message = "injected PNPM metadata preparation failure"
            raise ValueError(message)
        if self.failure_boundary == "nbgv-invocation" and command[:3] == (
            "node",
            "--input-type=module",
            "-e",
        ):
            message = "injected NBGV invocation failure"
            raise ValueError(message)
        try:
            output = subprocess.run(  # noqa: S603
                command,
                cwd=resolved_cwd,
                env=self.environment,
                check=True,
                capture_output=True,
                text=True,
            ).stdout
        except subprocess.CalledProcessError as error:
            stderr = (error.stderr or "").strip()
            message = f"Provider command failed: {command[0]}: {stderr}"
            raise ValueError(message) from error
        if command == (
            "git",
            "remote",
            "get-url",
            AUTHORITATIVE_REMOTE,
        ):
            self.remote_urls.append(output.strip())
        if self.failure_boundary == "result-parsing" and command[:3] == (
            "node",
            "--input-type=module",
            "-e",
        ):
            return "{injected-invalid-nbgv-json"
        return output


def _provider_metadata_records(
    runner: _DelegatingRecordingRunner,
) -> tuple[_IsolatedRecordedCommand, ...]:
    return tuple(
        record
        for record in runner.commands
        if (
            record.command
            and record.command[0] == "pnpm"
            and record.command != ("pnpm", "--version")
        )
        or record.command[:3] == ("node", "--input-type=module", "-e")
    )


def _assert_offline_isolated_evaluation(
    repository: _RealLocalNbgvRepository,
    runner: _DelegatingRecordingRunner,
    *,
    expect_nbgv: bool,
    expect_metadata: bool = True,
) -> None:
    assert len(runner.evaluation_roots) == 1
    evaluation_root = runner.evaluation_roots[0]
    assert evaluation_root != repository.caller_checkout.resolve()
    probe = runner.probes[evaluation_root]
    assert probe.head == repository.target
    assert probe.detached is True
    assert probe.shallow is False
    assert probe.history_count == _FIXTURE_HISTORY_COUNT
    assert probe.base_is_ancestor is True
    assert probe.merge_base == repository.target
    assert probe.objects == (repository.base, repository.target)
    assert probe.tags == repository.tags
    assert probe.annotated_tag_type == "tag"
    assert probe.lightweight_tag_type == "commit"
    assert probe.isolated_git_directory is True
    assert evaluation_root not in probe.registered_worktrees
    assert probe.remote_url == repository.bare_origin.resolve().as_uri()

    metadata_records = _provider_metadata_records(runner)
    assert all(
        record.cwd != repository.caller_checkout.resolve()
        for record in metadata_records
    )
    assert all(
        _evaluation_root(record.command, record.cwd) == evaluation_root
        for record in metadata_records
    )
    nbgv_records = tuple(
        record
        for record in metadata_records
        if record.command[:3] == ("node", "--input-type=module", "-e")
    )
    assert len(nbgv_records) == (1 if expect_nbgv else 0)
    if nbgv_records:
        assert (
            nbgv_records[0].command[3].count("getVersion(process.cwd())") == 1
        )
    pnpm_records = tuple(
        record
        for record in metadata_records
        if record.command and record.command[0] == "pnpm"
    )
    assert bool(pnpm_records) is expect_metadata
    if expect_metadata and runner.failure_boundary != "pnpm-metadata":
        assert any(
            "--ignore-scripts" in record.command for record in pnpm_records
        )
        assert any("list" in record.command for record in pnpm_records)

    for record in runner.commands:
        assert dict(record.environment) == {
            name: _OFFLINE_ENVIRONMENT[name]
            for name in _RECORDED_ENVIRONMENT_NAMES
        }
        command_text = " ".join(record.command).lower()
        assert "http://" not in command_text
        assert "https://" not in command_text
        assert "ssh://" not in command_text
        assert "git@" not in command_text
    assert bool(runner.remote_urls)
    for remote_url in runner.remote_urls:
        _assert_local_remote_url(remote_url)
        assert remote_url == repository.bare_origin.resolve().as_uri()
    assert repository.bare_origin.parent == (repository.caller_checkout.parent)


@pytest.mark.parametrize(
    "override_kind",
    ["version-json", "pnpm-metadata"],
    ids=["version-json", "pnpm-metadata"],
)
def test_isolated_exact_target_nbgv_facts_ignore_untracked_override(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    override_kind: str,
) -> None:
    """Ignore effective untracked NBGV and PNPM inputs at exact target."""
    environment = _offline_provider_environment(monkeypatch)
    repository = _real_local_nbgv_repository(tmp_path, environment)
    override = _write_ambient_override(
        repository.caller_checkout,
        kind=override_kind,
        ignored=False,
    )
    before = _source_checkout_snapshot(
        repository.caller_checkout,
        environment,
    )
    _assert_override_snapshot(before, override)
    _direct_ambient_control(repository, override, environment)
    assert (
        _source_checkout_snapshot(repository.caller_checkout, environment)
        == before
    )
    context = _EvaluationContext(
        caller_checkout=repository.caller_checkout,
        target=repository.target,
        base=repository.base,
        expected_tags=repository.tags,
        bare_origin=repository.bare_origin,
        environment=environment,
    )
    runner = _DelegatingRecordingRunner(context=context)
    result: NodeProviderResult | None = None
    error: ValueError | None = None

    try:
        result = provide_node_repository_facts(
            repository.caller_checkout,
            PROJECT_PATH,
            _binding(repository.target),
            _materialization(),
            runner=runner,
        )
    except ValueError as caught:
        error = caught

    after = _source_checkout_snapshot(
        repository.caller_checkout,
        environment,
    )
    assert after == before
    _assert_override_snapshot(after, override)
    assert error is None, f"isolated Provider failed: {error}"
    assert result is not None
    assert _complete_fact_tuple(result) == repository.baseline_facts
    assert result.project_nodes == repository.baseline_project_nodes
    assert result.nbgv.git_commit_id == repository.target
    assert result.checkout.head == repository.target
    _assert_offline_isolated_evaluation(
        repository,
        runner,
        expect_nbgv=True,
    )
    assert all(not path.exists() for path in runner.evaluation_roots)


@pytest.mark.parametrize(
    "override_kind",
    ["version-json", "pnpm-metadata"],
    ids=["version-json", "pnpm-metadata"],
)
def test_isolated_exact_target_nbgv_facts_ignore_ignored_override(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    override_kind: str,
) -> None:
    """Ignore effective Git-ignored NBGV and PNPM inputs at exact target."""
    environment = _offline_provider_environment(monkeypatch)
    repository = _real_local_nbgv_repository(tmp_path, environment)
    override = _write_ambient_override(
        repository.caller_checkout,
        kind=override_kind,
        ignored=True,
    )
    before = _source_checkout_snapshot(
        repository.caller_checkout,
        environment,
    )
    _assert_override_snapshot(before, override)
    _direct_ambient_control(repository, override, environment)
    assert (
        _source_checkout_snapshot(repository.caller_checkout, environment)
        == before
    )
    context = _EvaluationContext(
        caller_checkout=repository.caller_checkout,
        target=repository.target,
        base=repository.base,
        expected_tags=repository.tags,
        bare_origin=repository.bare_origin,
        environment=environment,
    )
    runner = _DelegatingRecordingRunner(context=context)
    result: NodeProviderResult | None = None
    error: ValueError | None = None

    try:
        result = provide_node_repository_facts(
            repository.caller_checkout,
            PROJECT_PATH,
            _binding(repository.target),
            _materialization(),
            runner=runner,
        )
    except ValueError as caught:
        error = caught

    after = _source_checkout_snapshot(
        repository.caller_checkout,
        environment,
    )
    assert after == before
    _assert_override_snapshot(after, override)
    assert error is None, f"isolated Provider failed: {error}"
    assert result is not None
    assert _complete_fact_tuple(result) == repository.baseline_facts
    assert result.project_nodes == repository.baseline_project_nodes
    assert result.nbgv.git_commit_id == repository.target
    assert result.checkout.head == repository.target
    _assert_offline_isolated_evaluation(
        repository,
        runner,
        expect_nbgv=True,
    )
    assert all(not path.exists() for path in runner.evaluation_roots)


@pytest.mark.parametrize(
    ("failure_boundary", "expected_error"),
    [
        (None, None),
        (
            "git-preparation",
            "injected Git exact-target preparation failure",
        ),
        ("pnpm-metadata", "injected PNPM metadata preparation failure"),
        ("nbgv-invocation", "injected NBGV invocation failure"),
        ("result-parsing", "NBGV Node API did not emit valid JSON"),
    ],
    ids=[
        "success",
        "git-preparation",
        "pnpm-metadata",
        "nbgv-invocation",
        "result-parsing",
    ],
)
def test_isolated_exact_target_materialization_preserves_source_and_cleans_up(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    failure_boundary: str | None,
    expected_error: str | None,
) -> None:
    """Clean temporary exact-target repositories on every completion path."""
    environment = _offline_provider_environment(monkeypatch)
    repository = _real_local_nbgv_repository(tmp_path, environment)
    overrides = (
        _write_ambient_override(
            repository.caller_checkout,
            kind="version-json",
            ignored=False,
        ),
        _write_ambient_override(
            repository.caller_checkout,
            kind="pnpm-metadata",
            ignored=True,
        ),
    )
    before = _source_checkout_snapshot(
        repository.caller_checkout,
        environment,
    )
    for override in overrides:
        _assert_override_snapshot(before, override)
    context = _EvaluationContext(
        caller_checkout=repository.caller_checkout,
        target=repository.target,
        base=repository.base,
        expected_tags=repository.tags,
        bare_origin=repository.bare_origin,
        environment=environment,
    )
    runner = _DelegatingRecordingRunner(
        context=context,
        failure_boundary=failure_boundary,
    )
    result: NodeProviderResult | None = None
    error: AssertionError | ValueError | None = None

    try:
        result = provide_node_repository_facts(
            repository.caller_checkout,
            PROJECT_PATH,
            _binding(repository.target),
            _materialization(),
            runner=runner,
        )
    except (AssertionError, ValueError) as caught:
        error = caught
    finally:
        after = _source_checkout_snapshot(
            repository.caller_checkout,
            environment,
        )
        remaining_evaluation_paths = tuple(
            path for path in runner.evaluation_roots if path.exists()
        )

    assert after == before
    assert after.worktrees == before.worktrees
    for override in overrides:
        _assert_override_snapshot(after, override)
    assert runner.evaluation_roots, (error, tuple(runner.commands))
    assert remaining_evaluation_paths == ()
    _assert_offline_isolated_evaluation(
        repository,
        runner,
        expect_nbgv=failure_boundary
        not in {"git-preparation", "pnpm-metadata"},
        expect_metadata=failure_boundary != "git-preparation",
    )
    if failure_boundary == "git-preparation":
        assert tuple(runner.git_preparation_failure_roots) == (
            runner.evaluation_roots
        )
        assert all(
            root not in runner.probes[root].registered_worktrees
            for root in runner.git_preparation_failure_roots
        )
        assert _provider_metadata_records(runner) == ()
    if expected_error is None:
        assert error is None, f"isolated Provider failed: {error}"
        assert result is not None
        assert _complete_fact_tuple(result) == repository.baseline_facts
        assert result.nbgv.git_commit_id == repository.target
        assert result.checkout.head == repository.target
    else:
        assert result is None
        assert isinstance(error, ValueError)
        assert expected_error in str(error)


SHA256_HEX_LENGTH = 64


class _Phase3RecordingRunner(RecordingRunner):
    """Allow Phase 3 tests to vary only the reported tool versions."""

    def __init__(
        self,
        repo_root: Path,
        project_root: Path,
        *,
        target: str = "e" * 40,
    ) -> None:
        super().__init__(repo_root, project_root, target=target)
        self.node_version_output = "v24.14.0\n"
        self.pnpm_version_output = "11.21.0\n"

    def __call__(
        self,
        command: tuple[str, ...],
        cwd: Path,
    ) -> str:
        if command == ("node", "--version"):
            self.commands.append((command, cwd))
            return self.node_version_output
        if command == ("pnpm", "--version"):
            self.commands.append((command, cwd))
            return self.pnpm_version_output
        return super().__call__(command, cwd)


@pytest.fixture
def valid_provider_node_api_payload() -> dict[str, object]:
    """Return a fresh literal payload satisfying the complete Phase 3 shape."""
    return {
        "version": "1.2.3",
        "semVer1": "1.2.3-beta-0042-e123456",
        "semVer2": "1.2.3-beta.42.ge123456",
        "versionHeight": 42,
        "gitCommitId": "e" * 40,
        "publicRelease": False,
        "npmPackageVersion": "1.2.3-beta.42.ge123456",
    }


def _phase3_provider_scenario(
    tmp_path: Path,
) -> tuple[Path, _Phase3RecordingRunner, ProviderBinding]:
    repo = tmp_path / "repo"
    project = repo / PROJECT_PATH
    project.mkdir(parents=True)
    (project / "package.json").write_text("{}\n", encoding="utf-8")
    (repo / "package.json").write_text(
        '{"name":"provider-fixture-root","private":true}\n',
        encoding="utf-8",
    )
    (repo / "pnpm-lock.yaml").write_text(
        "lockfileVersion: '9.0'\n",
        encoding="utf-8",
    )
    (repo / "pnpm-workspace.yaml").write_text(
        f"packages:\n  - {PROJECT_PATH}\n",
        encoding="utf-8",
    )
    (repo / "version.json").write_text(
        '{"version":"1.2.3"}\n',
        encoding="utf-8",
    )
    runner = _Phase3RecordingRunner(repo, project)
    return repo, runner, _binding()


def _assert_phase3_provider_payload_rejected(
    repo: Path,
    runner: _Phase3RecordingRunner,
    binding: ProviderBinding,
    *,
    match: str,
) -> None:
    result: NodeProviderResult | None = None

    with pytest.raises((TypeError, ValueError), match=match):
        result = provide_node_repository_facts(
            repo,
            PROJECT_PATH,
            binding,
            _materialization(),
            runner=runner,
        )

    assert result is None
    assert len(runner.nbgv_calls) == 1
    assert (("node", "--version"), repo) not in runner.commands
    assert (("pnpm", "--version"), repo) not in runner.commands


@pytest.mark.parametrize(
    ("field", "value", "accepted"),
    [
        ("version", "1.2", True),
        ("version", "1.2.3", True),
        ("version", "1.2.3.4", True),
        ("version", "", False),
        ("version", 123, False),
        ("version", "1", False),
        ("version", "1.2.3.4.5", False),
        ("version", "01.2.3", False),
        ("version", "1.02.3", False),
        ("version", "1.-2.3", False),
        ("version", "1.two.3", False),
        ("version", " 1.2.3", False),
        ("semVer1", "", False),
        ("semVer1", 123, False),
        ("semVer2", "", False),
        ("semVer2", 123, False),
    ],
    ids=[
        "version-valid-two-components",
        "version-valid-three-components",
        "version-valid-four-components",
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
    ],
)
def test_provider_rejects_malformed_nbgv_version_contract(
    tmp_path: Path,
    valid_provider_node_api_payload: dict[str, object],
    field: str,
    value: object,
    *,
    accepted: bool,
) -> None:
    """Accept only canonical numeric versions and required SemVer strings."""
    repo, runner, binding = _phase3_provider_scenario(tmp_path)
    valid_provider_node_api_payload[field] = value
    runner.nbgv = valid_provider_node_api_payload

    if accepted:
        result = provide_node_repository_facts(
            repo,
            PROJECT_PATH,
            binding,
            _materialization(),
            runner=runner,
        )

        assert result.nbgv.canonical_version == value
        assert (
            result.nbgv.sem_ver1 == (valid_provider_node_api_payload["semVer1"])
        )
        assert (
            result.nbgv.sem_ver2 == (valid_provider_node_api_payload["semVer2"])
        )
        digest_prefix, digest_hex = result.nbgv.node_api_result_digest.split(
            ":", maxsplit=1
        )
        assert digest_prefix == "sha256"
        assert len(digest_hex) == SHA256_HEX_LENGTH
        assert digest_hex == digest_hex.lower()
        assert set(digest_hex) <= set("0123456789abcdef")
        assert result.outcome == "success"
        return

    _assert_phase3_provider_payload_rejected(
        repo,
        runner,
        binding,
        match=r"(?:version|semVer1|semVer2)",
    )


@pytest.mark.parametrize(
    ("npm_package_version", "accepted"),
    [
        ("1.2.3-beta.4", True),
        ("1.2.3+build.5", True),
        ("1.2.3-beta.4+build.5", True),
        ("", False),
        ("^1.2.3", False),
        ("latest", False),
        ("https://registry.npmjs.org/package", False),
        ("v1.2.3", False),
        (" 1.2.3", False),
        ("1.2", False),
        ("01.2.3", False),
        ("1.2.3-01", False),
        ("1.2.3-", False),
        ("1.2.3+", False),
        ("1.2.3-alpha..1", False),
        ("1.2.3-alpha_beta", False),
        (123, False),
    ],
    ids=[
        "valid-prerelease",
        "valid-build",
        "valid-prerelease-build",
        "empty",
        "range",
        "tag",
        "url",
        "v-prefixed",
        "whitespace-padded",
        "malformed",
        "leading-zero-major",
        "leading-zero-prerelease",
        "empty-prerelease",
        "empty-build",
        "empty-prerelease-identifier",
        "invalid-prerelease-character",
        "non-string",
    ],
)
def test_provider_rejects_malformed_npm_package_version_contract(
    tmp_path: Path,
    valid_provider_node_api_payload: dict[str, object],
    npm_package_version: object,
    *,
    accepted: bool,
) -> None:
    """Retain one native npm SemVer and reject every non-version form."""
    repo, runner, binding = _phase3_provider_scenario(tmp_path)
    valid_provider_node_api_payload["semVer2"] = "7.8.9"
    valid_provider_node_api_payload["npmPackageVersion"] = npm_package_version
    runner.nbgv = valid_provider_node_api_payload

    if accepted:
        result = provide_node_repository_facts(
            repo,
            PROJECT_PATH,
            binding,
            _materialization(),
            runner=runner,
        )

        assert result.nbgv.npm_package_version == npm_package_version
        assert result.nbgv.sem_ver2 == "7.8.9"
        assert result.nbgv.npm_package_version != result.nbgv.sem_ver2
        assert len(runner.nbgv_calls) == 1
        return

    _assert_phase3_provider_payload_rejected(
        repo,
        runner,
        binding,
        match="npmPackageVersion",
    )


@pytest.mark.parametrize(
    "git_commit_id",
    [
        "e" * 39,
        "E" * 40,
        "g" * 40,
        f"{'e' * 40} ",
        123,
        "d" * 40,
    ],
    ids=[
        "short",
        "uppercase",
        "nonhex",
        "whitespace-padded",
        "non-string",
        "target-mismatch",
    ],
)
def test_provider_rejects_malformed_target_git_commit_id(
    tmp_path: Path,
    valid_provider_node_api_payload: dict[str, object],
    git_commit_id: object,
) -> None:
    """Require the exact target as one full lowercase hexadecimal SHA."""
    repo, runner, binding = _phase3_provider_scenario(tmp_path)
    valid_provider_node_api_payload["gitCommitId"] = git_commit_id
    runner.nbgv = valid_provider_node_api_payload

    _assert_phase3_provider_payload_rejected(
        repo,
        runner,
        binding,
        match=r"(?:gitCommitId|exact target)",
    )


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("versionHeight", 0),
        ("versionHeight", -1),
        ("versionHeight", True),
        ("versionHeight", False),
        ("versionHeight", "42"),
        ("versionHeight", 42.5),
        ("publicRelease", "false"),
        ("publicRelease", 0),
        ("publicRelease", None),
        ("publicRelease", []),
    ],
    ids=[
        "height-zero",
        "height-negative",
        "height-true",
        "height-false",
        "height-string",
        "height-non-integral",
        "public-release-string",
        "public-release-integer",
        "public-release-none",
        "public-release-other",
    ],
)
def test_provider_rejects_invalid_nbgv_scalar_contract(
    tmp_path: Path,
    valid_provider_node_api_payload: dict[str, object],
    field: str,
    value: object,
) -> None:
    """Require a positive non-Boolean height and Boolean public release."""
    repo, runner, binding = _phase3_provider_scenario(tmp_path)
    valid_provider_node_api_payload[field] = value
    runner.nbgv = valid_provider_node_api_payload

    _assert_phase3_provider_payload_rejected(
        repo,
        runner,
        binding,
        match=field,
    )


@pytest.mark.parametrize(
    ("tool", "output"),
    [
        ("node", ""),
        ("node", " \n"),
        ("pnpm", ""),
        ("pnpm", "\t\n"),
    ],
    ids=[
        "node-empty",
        "node-whitespace",
        "pnpm-empty",
        "pnpm-whitespace",
    ],
)
def test_provider_rejects_empty_toolchain_version(
    tmp_path: Path,
    valid_provider_node_api_payload: dict[str, object],
    tool: str,
    output: str,
) -> None:
    """Reject empty or whitespace-only Node and PNPM version evidence."""
    repo, runner, binding = _phase3_provider_scenario(tmp_path)
    runner.nbgv = valid_provider_node_api_payload
    if tool == "node":
        runner.node_version_output = output
    else:
        runner.pnpm_version_output = output
    result: NodeProviderResult | None = None

    with pytest.raises(ValueError, match=rf"(?:{tool}|toolchain).*version"):
        result = provide_node_repository_facts(
            repo,
            PROJECT_PATH,
            binding,
            _materialization(),
            runner=runner,
        )

    assert result is None
    assert len(runner.nbgv_calls) == 1
    assert ((tool, "--version"), repo) in runner.commands


def test_provider_rejects_missing_native_npm_package_version_without_fallback(
    tmp_path: Path,
    valid_provider_node_api_payload: dict[str, object],
) -> None:
    """Never derive npmPackageVersion from canonical or package metadata."""
    repo, runner, binding = _phase3_provider_scenario(tmp_path)
    del valid_provider_node_api_payload["npmPackageVersion"]
    valid_provider_node_api_payload["semVer1"] = "8.9.0"
    valid_provider_node_api_payload["semVer2"] = "8.9.0-beta.1"
    assert isinstance(runner.pnpm, list)
    package = runner.pnpm[0]
    assert isinstance(package, dict)
    package["version"] = "8.9.0-beta.1"
    runner.nbgv = valid_provider_node_api_payload

    _assert_phase3_provider_payload_rejected(
        repo,
        runner,
        binding,
        match="npmPackageVersion",
    )

    assert valid_provider_node_api_payload["semVer1"] == "8.9.0"
    assert valid_provider_node_api_payload["semVer2"] == "8.9.0-beta.1"
    assert package["version"] == "8.9.0-beta.1"


@dataclass(frozen=True, slots=True)
class _TagObjectIdentity:
    direct_object_id: str
    peeled_object_id: str | None
    direct_object_type: str
    peeled_object_type: str | None


@dataclass(frozen=True, slots=True)
class _SymbolicHeadSnapshot:
    returncode: int
    stdout: bytes
    stderr: bytes


@dataclass(frozen=True, slots=True)
class _CallerGitStateSnapshot:
    source: _SourceCheckoutSnapshot
    porcelain_status: bytes
    index_path: Path
    index_bytes: bytes
    index_entries: bytes
    head_commit: str
    head_path: Path
    head_bytes: bytes
    symbolic_head: _SymbolicHeadSnapshot
    local_config_path: Path
    local_config_bytes: bytes
    semantic_local_config: bytes
    refs: dict[str, _TagObjectIdentity]
    scenario_tag: _TagObjectIdentity | None


@dataclass(frozen=True, slots=True)
class _TemporaryTagProbe:
    root: Path
    tag_identities: dict[str, _TagObjectIdentity]
    pre_evaluation_command: tuple[str, ...]


@dataclass(frozen=True, slots=True)
class _TagInvarianceArrangement:
    repository: _RealLocalNbgvRepository
    scenario_tag_ref: str
    caller_before: _CallerGitStateSnapshot
    authoritative_before: dict[str, _TagObjectIdentity]
    tmp_path: Path


def _phase1_process(
    command: tuple[str, ...],
    cwd: Path,
    environment: dict[str, str],
) -> subprocess.CompletedProcess[bytes]:
    return subprocess.run(  # noqa: S603
        command,
        cwd=cwd,
        env=environment,
        check=False,
        capture_output=True,
    )


def _phase1_command_bytes(
    command: tuple[str, ...],
    cwd: Path,
    environment: dict[str, str],
) -> bytes:
    completed = _phase1_process(command, cwd, environment)
    if completed.returncode != 0:
        stderr = completed.stderr.decode(errors="replace").strip()
        message = f"fixture command failed: {' '.join(command)}: {stderr}"
        raise AssertionError(message)
    return completed.stdout


def _ref_identity_map(
    repository: Path,
    namespace: str,
    environment: dict[str, str],
) -> dict[str, _TagObjectIdentity]:
    output = _phase1_command_bytes(
        (
            "git",
            "for-each-ref",
            (
                "--format=%(refname)%00%(objectname)%00%(*objectname)"
                "%00%(objecttype)%00%(*objecttype)"
            ),
            namespace,
        ),
        repository,
        environment,
    ).decode()
    identities: dict[str, _TagObjectIdentity] = {}
    for row in output.splitlines():
        (
            ref_name,
            direct_object_id,
            peeled_object_id,
            direct_object_type,
            peeled_object_type,
        ) = row.split("\0")
        identities[ref_name] = _TagObjectIdentity(
            direct_object_id=direct_object_id,
            peeled_object_id=peeled_object_id or None,
            direct_object_type=direct_object_type,
            peeled_object_type=peeled_object_type or None,
        )
    return {name: identities[name] for name in sorted(identities)}


def _tag_ref_identity_map(
    repository: Path,
    environment: dict[str, str],
) -> dict[str, _TagObjectIdentity]:
    return _ref_identity_map(repository, "refs/tags", environment)


def _authoritative_tag_identity_map(
    origin: Path,
    environment: dict[str, str],
) -> dict[str, _TagObjectIdentity]:
    remote_url = origin.resolve().as_uri()
    _assert_local_remote_url(remote_url)
    output = _run_real_local_command(
        ("git", "ls-remote", "--tags", remote_url),
        origin.parent,
        environment,
    )
    direct: dict[str, str] = {}
    peeled: dict[str, str] = {}
    for row in output.splitlines():
        object_id, ref_name = row.split("\t")
        if ref_name.endswith("^{}"):
            peeled[ref_name.removesuffix("^{}")] = object_id
        else:
            direct[ref_name] = object_id
    assert set(peeled) <= set(direct)

    identities: dict[str, _TagObjectIdentity] = {}
    for ref_name in sorted(direct):
        direct_object_id = direct[ref_name]
        peeled_object_id = peeled.get(ref_name)
        direct_object_type = _run_real_local_command(
            ("git", "cat-file", "-t", direct_object_id),
            origin,
            environment,
        ).strip()
        peeled_object_type = (
            _run_real_local_command(
                ("git", "cat-file", "-t", peeled_object_id),
                origin,
                environment,
            ).strip()
            if peeled_object_id is not None
            else None
        )
        identities[ref_name] = _TagObjectIdentity(
            direct_object_id=direct_object_id,
            peeled_object_id=peeled_object_id,
            direct_object_type=direct_object_type,
            peeled_object_type=peeled_object_type,
        )
    return identities


def _caller_git_state_snapshot(
    repository: Path,
    scenario_tag_ref: str,
    environment: dict[str, str],
) -> _CallerGitStateSnapshot:
    git_paths: dict[str, Path] = {}
    for name in ("config", "index", "HEAD"):
        output = _run_real_local_command(
            ("git", "rev-parse", "--git-path", name),
            repository,
            environment,
        ).strip()
        path = Path(output)
        if not path.is_absolute():
            path = repository / path
        git_paths[name] = path.resolve()
    symbolic_head = _phase1_process(
        ("git", "symbolic-ref", "--quiet", "HEAD"),
        repository,
        environment,
    )
    assert symbolic_head.returncode in {0, 1}
    refs = _ref_identity_map(repository, "refs", environment)
    return _CallerGitStateSnapshot(
        source=_source_checkout_snapshot(repository, environment),
        porcelain_status=_phase1_command_bytes(
            (
                "git",
                "status",
                "--porcelain=v1",
                "-z",
                "--untracked-files=all",
            ),
            repository,
            environment,
        ),
        index_path=git_paths["index"],
        index_bytes=git_paths["index"].read_bytes(),
        index_entries=_phase1_command_bytes(
            ("git", "ls-files", "--stage", "-z"),
            repository,
            environment,
        ),
        head_commit=_run_real_local_command(
            ("git", "rev-parse", "HEAD"),
            repository,
            environment,
        ).strip(),
        head_path=git_paths["HEAD"],
        head_bytes=git_paths["HEAD"].read_bytes(),
        symbolic_head=_SymbolicHeadSnapshot(
            returncode=symbolic_head.returncode,
            stdout=symbolic_head.stdout,
            stderr=symbolic_head.stderr,
        ),
        local_config_path=git_paths["config"],
        local_config_bytes=git_paths["config"].read_bytes(),
        semantic_local_config=_phase1_command_bytes(
            ("git", "config", "--local", "--null", "--list"),
            repository,
            environment,
        ),
        refs=refs,
        scenario_tag=refs.get(scenario_tag_ref),
    )


def _phase1_remote_path(url: str) -> Path:
    _assert_local_remote_url(url)
    parsed = urlsplit(url)
    if parsed.scheme:
        return Path(unquote(parsed.path)).resolve()
    return Path(url).expanduser().resolve()


def _arrange_tag_invariance_case(
    repository: _RealLocalNbgvRepository,
    scenario: str,
    environment: dict[str, str],
) -> str:
    caller = repository.caller_checkout
    baseline = repository.baseline_checkout
    for checkout in (caller, baseline):
        for command in (
            ("git", "config", "user.name", "Workflow Delivery Tests"),
            ("git", "config", "user.email", "tests@example.invalid"),
            ("git", "config", "tag.gpgSign", "false"),
        ):
            _run_real_local_command(command, checkout, environment)

    scenario_tag_ref = f"refs/tags/provider-invariance/{scenario}"
    scenario_tag_name = scenario_tag_ref.removeprefix("refs/tags/")
    if scenario == "conflicting-authoritative-tag":
        _run_real_local_command(
            (
                "git",
                "tag",
                "--annotate",
                "--no-sign",
                "--message",
                "authoritative conflicting tag",
                scenario_tag_name,
                repository.target,
            ),
            baseline,
            environment,
        )
        _run_real_local_command(
            (
                "git",
                "push",
                "origin",
                f"{scenario_tag_ref}:{scenario_tag_ref}",
            ),
            baseline,
            environment,
        )
    elif scenario != "local-only-tag":
        message = f"unknown tag invariance scenario: {scenario}"
        raise AssertionError(message)

    _run_real_local_command(
        (
            "git",
            "tag",
            "--annotate",
            "--no-sign",
            "--message",
            f"caller {scenario}",
            scenario_tag_name,
            repository.base,
        ),
        caller,
        environment,
    )
    _run_real_local_command(
        ("git", "config", "phase1.caller-state", f"sentinel-{scenario}"),
        caller,
        environment,
    )
    staged_path = caller / "caller-state" / "staged.txt"
    staged_path.parent.mkdir()
    staged_path.write_text("staged caller state\n", encoding="utf-8")
    _run_real_local_command(
        ("git", "add", "caller-state/staged.txt"),
        caller,
        environment,
    )
    (caller / "target.txt").write_text(
        "unstaged caller state\n",
        encoding="utf-8",
    )
    (caller / "caller-state" / "untracked.bin").write_bytes(
        b"\x00untracked caller state\xff"
    )
    return scenario_tag_ref


def _prepare_tag_invariance_arrangement(
    tmp_path: Path,
    scenario: str,
    environment: dict[str, str],
) -> _TagInvarianceArrangement:
    repository = _real_local_nbgv_repository(tmp_path, environment)
    scenario_tag_ref = _arrange_tag_invariance_case(
        repository,
        scenario,
        environment,
    )
    caller_before = _caller_git_state_snapshot(
        repository.caller_checkout,
        scenario_tag_ref,
        environment,
    )
    authoritative_before = _authoritative_tag_identity_map(
        repository.bare_origin,
        environment,
    )
    assert caller_before.scenario_tag is not None
    assert caller_before.scenario_tag.direct_object_type == "tag"
    assert caller_before.scenario_tag.peeled_object_type == "commit"
    assert b"A  caller-state/staged.txt\0" in caller_before.porcelain_status
    assert b" M target.txt\0" in caller_before.porcelain_status
    assert b"?? caller-state/untracked.bin\0" in caller_before.porcelain_status
    assert (
        b"phase1.caller-state\nsentinel-" in caller_before.semantic_local_config
    )
    if scenario == "local-only-tag":
        assert scenario_tag_ref not in authoritative_before
    else:
        authoritative_tag = authoritative_before[scenario_tag_ref]
        assert authoritative_tag.direct_object_type == "tag"
        assert authoritative_tag.peeled_object_type == "commit"
        assert (
            caller_before.scenario_tag.direct_object_id
            != authoritative_tag.direct_object_id
        )
        assert (
            caller_before.scenario_tag.peeled_object_id
            != authoritative_tag.peeled_object_id
        )
    return _TagInvarianceArrangement(
        repository=repository,
        scenario_tag_ref=scenario_tag_ref,
        caller_before=caller_before,
        authoritative_before=authoritative_before,
        tmp_path=tmp_path,
    )


class _TagIdentityRecordingRunner:
    def __init__(self, delegate: _DelegatingRecordingRunner) -> None:
        self.delegate = delegate
        self.probes: dict[Path, _TemporaryTagProbe] = {}

    @property
    def commands(self) -> list[_IsolatedRecordedCommand]:
        return self.delegate.commands

    def __call__(self, command: tuple[str, ...], cwd: Path) -> str:
        evaluation_root = _evaluation_root(command, cwd.resolve())
        if evaluation_root is not None and evaluation_root not in self.probes:
            self.probes[evaluation_root] = _TemporaryTagProbe(
                root=evaluation_root,
                tag_identities=_tag_ref_identity_map(
                    evaluation_root,
                    self.delegate.environment,
                ),
                pre_evaluation_command=command,
            )
        return self.delegate(command, cwd)


def _phase1_check(
    checks: dict[str, tuple[bool, object]],
) -> None:
    failures = {
        requirement: evidence
        for requirement, (satisfied, evidence) in checks.items()
        if not satisfied
    }
    assert failures == {}, f"Provider Git-state invariance failures: {failures}"


def _changed_ref_identities(
    before: dict[str, _TagObjectIdentity],
    after: dict[str, _TagObjectIdentity],
) -> dict[
    str,
    tuple[_TagObjectIdentity | None, _TagObjectIdentity | None],
]:
    return {
        ref_name: (before.get(ref_name), after.get(ref_name))
        for ref_name in sorted(set(before) | set(after))
        if before.get(ref_name) != after.get(ref_name)
    }


def _assert_tag_invariance_result(
    arrangement: _TagInvarianceArrangement,
    caller_after: _CallerGitStateSnapshot,
    authoritative_after: dict[str, _TagObjectIdentity],
    runner: _TagIdentityRecordingRunner,
    result: NodeProviderResult,
) -> None:
    repository = arrangement.repository
    tmp_path = arrangement.tmp_path
    caller_before = arrangement.caller_before
    scenario_tag_ref = arrangement.scenario_tag_ref
    assert len(runner.probes) == 1
    temporary_probe = next(iter(runner.probes.values()))
    preparation_fetches = tuple(
        record
        for record in runner.commands
        if record.command[:2] == ("git", "fetch")
        and TAG_REFSPEC in record.command
    )
    remote_paths = tuple(
        _phase1_remote_path(url)
        for url in (
            _remote_url(repository.caller_checkout),
            _remote_url(repository.baseline_checkout),
            temporary_probe.root.as_uri(),
            repository.bare_origin.as_uri(),
        )
    )
    common_checks: dict[str, tuple[bool, object]] = {
        "complete caller snapshot": (
            caller_after == caller_before,
            "structural before/after snapshots differ",
        ),
        "caller recursive working-tree bytes": (
            caller_after.source.entries == caller_before.source.entries,
            (caller_before.source.entries, caller_after.source.entries),
        ),
        "caller source status": (
            caller_after.source.status == caller_before.source.status,
            (caller_before.source.status, caller_after.source.status),
        ),
        "caller porcelain status bytes": (
            caller_after.porcelain_status == caller_before.porcelain_status,
            (
                caller_before.porcelain_status,
                caller_after.porcelain_status,
            ),
        ),
        "caller index": (
            caller_after.index_path == caller_before.index_path
            and caller_after.index_bytes == caller_before.index_bytes
            and caller_after.index_entries == caller_before.index_entries,
            (
                caller_before.index_path,
                caller_before.index_bytes,
                caller_before.index_entries,
                caller_after.index_path,
                caller_after.index_bytes,
                caller_after.index_entries,
            ),
        ),
        "caller HEAD commit": (
            caller_after.head_commit == caller_before.head_commit,
            (caller_before.head_commit, caller_after.head_commit),
        ),
        "caller raw HEAD": (
            caller_after.head_path == caller_before.head_path
            and caller_after.head_bytes == caller_before.head_bytes,
            (
                caller_before.head_path,
                caller_before.head_bytes,
                caller_after.head_path,
                caller_after.head_bytes,
            ),
        ),
        "caller symbolic or detached HEAD": (
            caller_after.symbolic_head == caller_before.symbolic_head,
            (caller_before.symbolic_head, caller_after.symbolic_head),
        ),
        "caller raw local config": (
            caller_after.local_config_path == caller_before.local_config_path
            and caller_after.local_config_bytes
            == caller_before.local_config_bytes,
            (
                caller_before.local_config_path,
                caller_before.local_config_bytes,
                caller_after.local_config_path,
                caller_after.local_config_bytes,
            ),
        ),
        "caller semantic local config": (
            caller_after.semantic_local_config
            == caller_before.semantic_local_config,
            (
                caller_before.semantic_local_config,
                caller_after.semantic_local_config,
            ),
        ),
        "caller complete refs": (
            caller_after.refs == caller_before.refs,
            _changed_ref_identities(
                caller_before.refs,
                caller_after.refs,
            ),
        ),
        "caller scenario tag identity": (
            caller_after.scenario_tag == caller_before.scenario_tag,
            (caller_before.scenario_tag, caller_after.scenario_tag),
        ),
        "authoritative remote unchanged": (
            authoritative_after == arrangement.authoritative_before,
            (arrangement.authoritative_before, authoritative_after),
        ),
        "temporary complete authoritative tags": (
            temporary_probe.tag_identities == arrangement.authoritative_before,
            (
                arrangement.authoritative_before,
                temporary_probe.tag_identities,
            ),
        ),
        "one exact preparation fetch": (
            len(preparation_fetches) == 1
            and all(
                record.command == _PHASE1_PREPARATION_FETCH_COMMAND
                for record in preparation_fetches
            ),
            tuple(
                (record.command, record.cwd) for record in preparation_fetches
            ),
        ),
        "every preparation fetch cwd is temporary": (
            bool(preparation_fetches)
            and all(
                record.cwd == temporary_probe.root
                and record.cwd != repository.caller_checkout.resolve()
                and not record.cwd.is_relative_to(
                    repository.caller_checkout.resolve()
                )
                for record in preparation_fetches
            ),
            tuple(record.cwd for record in preparation_fetches),
        ),
        "temporary repository is under tmp_path": (
            temporary_probe.root.is_relative_to(tmp_path.resolve()),
            temporary_probe.root,
        ),
        "all repository and remote paths are under tmp_path": (
            all(
                path.is_relative_to(tmp_path.resolve()) for path in remote_paths
            ),
            remote_paths,
        ),
        "evaluation starts after temporary tag preparation": (
            temporary_probe.pre_evaluation_command[:2] == ("pnpm", "install"),
            temporary_probe.pre_evaluation_command,
        ),
        "result remains exact-target and full-history": (
            result.checkout.target == repository.target
            and result.checkout.head == repository.target
            and result.checkout.shallow is False
            and result.checkout.ancestry_complete is True
            and result.checkout.tags_complete is True
            and result.checkout.credentials_persisted is False
            and result.nbgv.git_commit_id == repository.target,
            (result.checkout, result.nbgv.git_commit_id),
        ),
        "no network or timing command": (
            all(
                record.command[0] != "sleep"
                and "http://" not in " ".join(record.command).lower()
                and "https://" not in " ".join(record.command).lower()
                and "ssh://" not in " ".join(record.command).lower()
                and "git@" not in " ".join(record.command).lower()
                for record in runner.commands
            ),
            tuple(record.command for record in runner.commands),
        ),
        "temporary repository cleaned": (
            not temporary_probe.root.exists(),
            temporary_probe.root,
        ),
    }
    caller_tag = caller_before.scenario_tag
    if scenario_tag_ref not in arrangement.authoritative_before:
        common_checks["local-only tag stays outside temporary"] = (
            caller_tag is not None
            and scenario_tag_ref not in temporary_probe.tag_identities
            and scenario_tag_ref not in authoritative_after,
            (
                caller_tag,
                temporary_probe.tag_identities.get(scenario_tag_ref),
                authoritative_after.get(scenario_tag_ref),
            ),
        )
    else:
        authoritative_tag = arrangement.authoritative_before[scenario_tag_ref]
        common_checks["conflict resolves authoritative in temporary"] = (
            caller_tag is not None
            and caller_tag.direct_object_id
            != authoritative_tag.direct_object_id
            and temporary_probe.tag_identities.get(scenario_tag_ref)
            == authoritative_tag
            and temporary_probe.tag_identities[
                scenario_tag_ref
            ].direct_object_id
            != caller_tag.direct_object_id,
            (
                caller_tag,
                authoritative_tag,
                temporary_probe.tag_identities.get(scenario_tag_ref),
            ),
        )
    _phase1_check(common_checks)


@pytest.mark.parametrize(
    "scenario",
    ["local-only-tag", "conflicting-authoritative-tag"],
    ids=["local-only-tag", "conflicting-authoritative-tag"],
)
def test_isolated_tag_preparation_preserves_caller_git_state(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    scenario: str,
) -> None:
    """Prepare authoritative tags only in the disposable exact-target repo."""
    provider_temporary_root = (
        tmp_path / "provider-temporary" / "nested" / "root"
    )
    provider_temporary_root.mkdir(parents=True)
    (tmp_path / "workspace").symlink_to(REPO_ROOT.parents[1])
    monkeypatch.setenv("TMPDIR", str(provider_temporary_root))
    monkeypatch.setattr(
        __import__("tempfile"),
        "tempdir",
        str(provider_temporary_root),
    )
    environment = _offline_provider_environment(monkeypatch)
    arrangement = _prepare_tag_invariance_arrangement(
        tmp_path,
        scenario,
        environment,
    )
    repository = arrangement.repository
    authoritative_tags = tuple(arrangement.authoritative_before)
    assert set(repository.tags) <= set(authoritative_tags)
    for checkout in (
        repository.caller_checkout,
        repository.baseline_checkout,
    ):
        remote_path = _phase1_remote_path(_remote_url(checkout))
        assert remote_path == repository.bare_origin.resolve()
        assert remote_path.is_relative_to(tmp_path.resolve())

    context = _EvaluationContext(
        caller_checkout=repository.caller_checkout,
        target=repository.target,
        base=repository.base,
        expected_tags=authoritative_tags,
        bare_origin=repository.bare_origin,
        environment=environment,
    )
    delegate = _DelegatingRecordingRunner(context=context)
    runner = _TagIdentityRecordingRunner(delegate)
    result = provide_node_repository_facts(
        repository.caller_checkout,
        PROJECT_PATH,
        _binding(repository.target),
        _materialization(),
        runner=runner,
    )
    caller_after = _caller_git_state_snapshot(
        repository.caller_checkout,
        arrangement.scenario_tag_ref,
        environment,
    )
    authoritative_after = _authoritative_tag_identity_map(
        repository.bare_origin,
        environment,
    )

    _assert_offline_isolated_evaluation(
        replace(repository, tags=authoritative_tags),
        delegate,
        expect_nbgv=True,
    )
    assert _complete_fact_tuple(result)[:-1] == repository.baseline_facts[:-1]
    assert result.project_nodes == repository.baseline_project_nodes
    _assert_tag_invariance_result(
        arrangement,
        caller_after,
        authoritative_after,
        runner,
        result,
    )


class _MaterializationEqualitySurrogate:
    """Record any forbidden equality-based boundary validation."""

    __hash__ = None

    def __init__(self) -> None:
        self.comparison_count = 0

    def __eq__(self, other: object) -> bool:
        self.comparison_count += 1
        return other in (0, False)


class _ZeroIntSubtype(int):
    """A zero-valued integer that is not the exact integer runtime type."""


class _StructuralCheckoutMaterialization:
    """Expose the expected attributes without the required nominal type."""

    def __init__(self) -> None:
        self.fetch_depth = 0
        self.credentials_persisted = False


@dataclass(frozen=True, slots=True)
class _LookalikeCheckoutMaterialization:
    fetch_depth: int
    credentials_persisted: bool


@dataclass(frozen=True, slots=True)
class _ExtraFieldCheckoutMaterialization:
    fetch_depth: int
    credentials_persisted: bool
    source: str


class _CheckoutMaterializationSubtype(CheckoutMaterialization):
    """Remain structurally valid while violating the exact-type contract."""


class _MaterializationBoundaryRunner:
    """Record and reject any command attempted past an invalid boundary."""

    def __init__(self) -> None:
        self.commands: list[RecordedCommand] = []

    def __call__(self, command: tuple[str, ...], cwd: Path) -> str:
        self.commands.append((command, cwd))
        message = "invalid materialization reached a runner-backed operation"
        raise AssertionError(message)


type _InvalidMaterializationFactory = Callable[
    [], tuple[object, _MaterializationEqualitySurrogate | None]
]


def _materialization_field_case(
    field: str,
    value_factory: Callable[[], object],
) -> tuple[object, _MaterializationEqualitySurrogate | None]:
    value = value_factory()
    values = {
        "fetch_depth": 0,
        "credentials_persisted": False,
        field: value,
    }
    materialization = CheckoutMaterialization(
        fetch_depth=cast("int", values["fetch_depth"]),
        credentials_persisted=cast("bool", values["credentials_persisted"]),
    )
    equality_surrogate = (
        value if type(value) is _MaterializationEqualitySurrogate else None
    )
    return materialization, equality_surrogate


def _uninitialized_materialization() -> tuple[
    object, _MaterializationEqualitySurrogate | None
]:
    return object.__new__(CheckoutMaterialization), None


def _materialization_missing_fetch_depth() -> tuple[
    object, _MaterializationEqualitySurrogate | None
]:
    materialization = object.__new__(CheckoutMaterialization)
    object.__setattr__(materialization, "credentials_persisted", False)
    return materialization, None


def _materialization_missing_credentials() -> tuple[
    object, _MaterializationEqualitySurrogate | None
]:
    materialization = object.__new__(CheckoutMaterialization)
    object.__setattr__(materialization, "fetch_depth", 0)
    return materialization, None


@pytest.mark.parametrize(
    ("materialization_factory", "message"),
    [
        (
            lambda: _materialization_field_case(
                "fetch_depth",
                lambda: 0.0,
            ),
            r"fetch[-_]depth",
        ),
        (
            lambda: _materialization_field_case(
                "fetch_depth",
                lambda: -0.0,
            ),
            r"fetch[-_]depth",
        ),
        (
            lambda: _materialization_field_case(
                "fetch_depth",
                lambda: 1.0,
            ),
            r"fetch[-_]depth",
        ),
        (
            lambda: _materialization_field_case(
                "fetch_depth",
                lambda: False,
            ),
            r"fetch[-_]depth",
        ),
        (
            lambda: _materialization_field_case(
                "fetch_depth",
                lambda: True,
            ),
            r"fetch[-_]depth",
        ),
        (
            lambda: _materialization_field_case(
                "fetch_depth",
                _MaterializationEqualitySurrogate,
            ),
            r"fetch[-_]depth",
        ),
        (
            lambda: _materialization_field_case(
                "fetch_depth",
                lambda: _ZeroIntSubtype(0),
            ),
            r"fetch[-_]depth",
        ),
        (
            lambda: _materialization_field_case(
                "fetch_depth",
                lambda: "0",
            ),
            r"fetch[-_]depth",
        ),
        (
            lambda: _materialization_field_case(
                "fetch_depth",
                lambda: None,
            ),
            r"fetch[-_]depth",
        ),
        (
            lambda: _materialization_field_case(
                "fetch_depth",
                object,
            ),
            r"fetch[-_]depth",
        ),
        (
            lambda: _materialization_field_case(
                "credentials_persisted",
                lambda: True,
            ),
            r"credentials_persisted|persisted credentials",
        ),
        (
            lambda: _materialization_field_case(
                "credentials_persisted",
                lambda: 0,
            ),
            r"credentials_persisted|persisted credentials",
        ),
        (
            lambda: _materialization_field_case(
                "credentials_persisted",
                lambda: 1,
            ),
            r"credentials_persisted|persisted credentials",
        ),
        (
            lambda: _materialization_field_case(
                "credentials_persisted",
                lambda: 0.0,
            ),
            r"credentials_persisted|persisted credentials",
        ),
        (
            lambda: _materialization_field_case(
                "credentials_persisted",
                lambda: "false",
            ),
            r"credentials_persisted|persisted credentials",
        ),
        (
            lambda: _materialization_field_case(
                "credentials_persisted",
                lambda: None,
            ),
            r"credentials_persisted|persisted credentials",
        ),
        (
            lambda: _materialization_field_case(
                "credentials_persisted",
                _MaterializationEqualitySurrogate,
            ),
            r"credentials_persisted|persisted credentials",
        ),
        (
            lambda: _materialization_field_case(
                "credentials_persisted",
                object,
            ),
            r"credentials_persisted|persisted credentials",
        ),
        (
            lambda: (
                {
                    "fetch_depth": 0,
                    "credentials_persisted": False,
                },
                None,
            ),
            "CheckoutMaterialization",
        ),
        (
            lambda: (_StructuralCheckoutMaterialization(), None),
            "CheckoutMaterialization",
        ),
        (
            lambda: (
                _LookalikeCheckoutMaterialization(
                    fetch_depth=0,
                    credentials_persisted=False,
                ),
                None,
            ),
            "CheckoutMaterialization",
        ),
        (
            lambda: (
                _ExtraFieldCheckoutMaterialization(
                    fetch_depth=0,
                    credentials_persisted=False,
                    source="caller-checkout",
                ),
                None,
            ),
            "CheckoutMaterialization",
        ),
        (
            lambda: (
                _CheckoutMaterializationSubtype(
                    fetch_depth=0,
                    credentials_persisted=False,
                ),
                None,
            ),
            "CheckoutMaterialization",
        ),
        (
            lambda: (CheckoutMaterialization, None),
            "CheckoutMaterialization",
        ),
        (
            lambda: (object(), None),
            "CheckoutMaterialization",
        ),
        (
            _uninitialized_materialization,
            r"CheckoutMaterialization|fetch[-_]depth",
        ),
        (
            _materialization_missing_fetch_depth,
            r"CheckoutMaterialization|fetch[-_]depth",
        ),
        (
            _materialization_missing_credentials,
            r"CheckoutMaterialization|credentials_persisted",
        ),
    ],
    ids=[
        "fetch-depth-float-zero",
        "fetch-depth-float-negative-zero",
        "fetch-depth-float-one",
        "fetch-depth-bool-false",
        "fetch-depth-bool-true",
        "fetch-depth-zero-equality-surrogate",
        "fetch-depth-int-subclass-zero",
        "fetch-depth-string-zero",
        "fetch-depth-none",
        "fetch-depth-opaque",
        "credentials-bool-true",
        "credentials-int-zero",
        "credentials-int-one",
        "credentials-float-zero",
        "credentials-string-false",
        "credentials-none",
        "credentials-false-equality-surrogate",
        "credentials-opaque",
        "materialization-mapping",
        "materialization-structural-object",
        "materialization-lookalike-dataclass",
        "materialization-lookalike-dataclass-extra-field",
        "materialization-subtype",
        "materialization-class-object",
        "materialization-opaque-object",
        "materialization-exact-uninitialized",
        "materialization-exact-missing-fetch-depth",
        "materialization-exact-missing-credentials",
    ],
)
def test_provider_rejects_non_exact_checkout_materialization_before_git(
    tmp_path: Path,
    materialization_factory: _InvalidMaterializationFactory,
    message: str,
) -> None:
    """Reject non-exact caller checkout contracts without field coercion."""
    materialization, equality_surrogate = materialization_factory()
    runner = _MaterializationBoundaryRunner()

    with pytest.raises(ValueError, match=message) as error:
        provide_node_repository_facts(
            tmp_path,
            PROJECT_PATH,
            _binding(),
            cast("CheckoutMaterialization", materialization),
            runner=runner,
        )

    assert type(error.value) is ValueError
    assert runner.commands == []
    if equality_surrogate is not None:
        assert equality_surrogate.comparison_count == 0
