"""General-CI consumers and executable command boundaries during coexistence."""

from __future__ import annotations

import json
import os
import shutil
import sys
import tomllib
from pathlib import Path
from typing import TYPE_CHECKING, Any

import pytest
import yaml
from three_workflow_delivery_v3.repository.python_provider import (
    PYTHON_TOOLCHAIN,
)

from .workflow_shell import executable, resolve, run_step

if TYPE_CHECKING:
    import subprocess

REPO_ROOT = Path(__file__).resolve().parents[6]
CHILD_FAILURE = 73
PYTHON_NATIVE_ROOT = "tests/private/app/workflow-delivery/Native/Python"
PYTHON_NATIVE_DIRECTORY = "src/private/app/workflow-delivery/Native/Python/uv"
PYTHON_NATIVE_CONDITION = (
    "success() && !cancelled() && steps.scope.outputs.run == 'true' "
    "&& contains(fromJson(needs.scope.outputs.python_roots), "
    f"'{PYTHON_NATIVE_ROOT}')"
)
NODE_PACKAGE_DIR = "src/public/lib/hexo-renderer-asciidoc"
NODE_RETAINED = [NODE_PACKAGE_DIR, "src/retained"]
NODE_ADOPTED = (
    "tests/private/app/workflow-delivery/fixtures/products/"
    ""
    "hcoona-release-smoke-npm"
)
TOOL_COMMANDS = {
    "dotnet": "dotnet",
    "go": "go",
    "java": "java",
    "node": "node",
    "powershell": "pwsh",
    "python": "python3",
    "ruby": "ruby",
}
COMMAND_RECORDER = r"""
import json
import os
import sys
import tomllib
from pathlib import Path

command = [Path(sys.argv[0]).name, *sys.argv[1:]]
with Path(os.environ["COMMAND_LOG"]).open("a", encoding="utf-8") as stream:
    stream.write(json.dumps({
        "command": command,
        "cwd": str(Path.cwd()),
        "profile": os.environ.get("HK_PROFILE"),
        "auto_install": os.environ.get("MISE_TASK_RUN_AUTO_INSTALL"),
    }) + "\n")
failure = json.loads(os.environ.get("FAIL_COMMAND", "[]"))
if failure and command[:len(failure)] == failure:
    sys.exit(73)
if command[:2] == ["mise", "link"]:
    # Preexisting bindings require the caller to request replacement.
    if "--force" not in command:
        sys.exit(74)
if "--version" in command:
    print("fixture-version")
"""


@pytest.fixture
def workflow() -> dict[str, Any]:
    """Load the current general-CI workflow."""
    return yaml.safe_load((REPO_ROOT / ".github/workflows/ci.yml").read_text())


def _action(job: dict[str, Any], action: str) -> dict[str, Any]:
    return next(
        step
        for step in job["steps"]
        if step.get("uses", "").split("@", 1)[0] == action
    )


def _required_step(scope: dict[str, Any]) -> None:
    """These bodies rely on the ordinary success dependency, not a scheduler."""
    assert scope.get("if", "success()") in (
        True,
        "true",
        "success()",
        "${{ success() }}",
        PYTHON_NATIVE_CONDITION,
        "success() && !cancelled() && steps.scope.outputs.run == 'true'",
        (
            "success() && !cancelled() && steps.scope.outputs.run == 'true' "
            "&& needs.scope.outputs.python_dotnet == 'true'"
        ),
        (
            "success() && !cancelled() && steps.scope.outputs.run == 'true' "
            "&& needs.scope.outputs.python_v3 == 'true'"
        ),
    )
    assert scope.get("continue-on-error", False) is False


def _required_context_step(key: str, step: dict[str, Any]) -> None:
    if key == "validation":
        assert step["if"] in {"always()", "cancelled()"}
        assert not step.get("continue-on-error", False)
    elif key == "scope" and step["name"] == "Setup basis resource SDK":
        assert step["if"] == (
            "steps.materialize.outputs.basis_separate == 'true'"
        )
        assert step["uses"] == "actions/setup-dotnet@v6"
        assert step["with"]["global-json-file"] == (
            "${{ steps.materialize.outputs.basis_directory }}" + "/global.json"
        )
        assert not step.get("continue-on-error", False)
    elif key == "dotnet-tests" and step["name"] in {
        "Prepare NuGet authority dump collection",
        "Retain NuGet authority diagnostics",
    }:
        assert step["continue-on-error"] is True
        if "run" in step:
            assert step["shell"] == "pwsh"
            assert step["run"] == (
                "dotnet tool install dotnet-dump --version 10.0.745401 "
                "--tool-path (Split-Path $env:NUGET_AUTHORITY_DUMP_TOOL)"
            )
        else:
            assert step["uses"].startswith("actions/upload-artifact@")
    elif "Retain" not in step["name"] and step.get("if") != "cancelled()":
        _required_step(step)


def _bindings(tmp_path: Path) -> dict[str, str]:
    return {
        "github.workspace": str(tmp_path),
        "github.token": "fixture-token",
        "needs.scope.result": "success",
        "needs.scope.outputs.dotnet_artifact_id": "1054",
        "steps.control.outputs.application": str(
            tmp_path
            / "runner-state/dotnet-group/transfer/control/WorkflowDelivery.dll"
        ),
        "steps.control.outputs.transfer": str(
            tmp_path / "runner-state/dotnet-group/transfer"
        ),
        "steps.dotnet-group.outputs.request": str(
            tmp_path / "runner-state/dotnet-group/request.json"
        ),
        "secrets.GITHUB_TOKEN": "fixture-token",
        "github.event.pull_request.base.sha": "a" * 40,
        "github.event.pull_request.head.sha": "b" * 40,
        "github.event.before": "c" * 40,
        "github.sha": "d" * 40,
        "needs.scope.outputs.base": "a" * 40,
        "needs.scope.outputs.candidate": "d" * 40,
        "needs.scope.outputs.full": "true",
        "needs.scope.outputs.python_packages": json.dumps(
            ["hcoona-three-monorepo", "three-workflow-delivery-v3"]
        ),
        "needs.scope.outputs.python_v3": "true",
        "needs.node-group.outputs.retained_directories": json.dumps(
            NODE_RETAINED
        ),
        "needs.node-group.outputs.adopted_directory": NODE_ADOPTED,
        "runner.temp": str(tmp_path / "runner-state"),
        "needs.scope.outputs.python_dotnet": "true",
        "needs.scope.outputs.python_roots": json.dumps(
            ["tests/eng/test_ci_scope.py"]
        ),
    }


def _commands(tmp_path: Path, *, missing: str | None = None) -> dict[str, str]:
    tools = tmp_path / "setup tools"
    tools.mkdir()
    for name in {
        *TOOL_COMMANDS.values(),
        "mise",
        "pnpm",
        "npm",
        "uv",
        "hk",
        "python",
        "rustup",
        "cargo",
    }:
        if name != missing:
            executable(tools / name, COMMAND_RECORDER)
    bash = shutil.which("bash")
    assert bash is not None
    (tools / "bash").symlink_to(bash)
    return {
        "PATH": str(tools),
        "COMMAND_LOG": str(tmp_path / "commands.jsonl"),
        "GITHUB_ENV": str(tmp_path / "github-env"),
        "GITHUB_EVENT_NAME": "pull_request",
        "RUNNER_TEMP": str(tmp_path / "runner-state"),
    }


def _observations(env: dict[str, str]) -> list[dict[str, Any]]:
    log = Path(env["COMMAND_LOG"])
    observations = (
        [json.loads(line) for line in log.read_text().splitlines()]
        if log.exists()
        else []
    )
    for observation in observations:
        expected = log.parent
        if observation["command"][0] in {"rustup", "cargo"}:
            expected /= PYTHON_NATIVE_DIRECTORY
        assert Path(observation["cwd"]) == expected
    return observations


def _run_bash_steps(
    steps: list[dict[str, Any]],
    workflow: dict[str, Any],
    job: dict[str, Any],
    tmp_path: Path,
    env: dict[str, str],
) -> subprocess.CompletedProcess[str]:
    result = None
    for step in steps:
        if (
            "run" not in step
            or step.get("id") == "scope"
            or step.get("if") == "cancelled()"
        ):
            continue
        _required_step(step)
        result = run_step(
            step,
            cwd=tmp_path,
            env=env,
            bindings=_bindings(tmp_path)
            | {
                "needs.scope.outputs.full": env.get("CI_MODE", "true"),
                "needs.scope.outputs.python_packages": env.get(
                    "SELECTED_PACKAGES",
                    _bindings(tmp_path)["needs.scope.outputs.python_packages"],
                ),
                "needs.scope.outputs.python_roots": env.get(
                    "SELECTED_PYTHON_ROOTS",
                    _bindings(tmp_path)["needs.scope.outputs.python_roots"],
                ),
            },
            workflow=workflow,
            job=job,
        )
        if result.returncode:
            break
        environment_file = Path(env["GITHUB_ENV"])
        if environment_file.exists():
            for line in environment_file.read_text().splitlines():
                key, separator, value = line.partition("=")
                assert separator
                assert key.isidentifier()
                env[key] = value
    assert result is not None
    return result


def _run_validation(
    workflow: dict[str, Any],
    tmp_path: Path,
    env: dict[str, str],
) -> subprocess.CompletedProcess[str]:
    job = workflow["jobs"]["conformance"]
    mise = _action(job, "jdx/mise-action")
    assert mise["with"]["experimental"] is True
    assert mise["with"]["install"] is False
    boundary = job["steps"].index(mise)
    capture = _run_bash_steps(
        job["steps"][:boundary], workflow, job, tmp_path, env
    )
    if capture.returncode:
        return capture
    shims = tmp_path / "competing shims"
    shims.mkdir()
    for command in TOOL_COMMANDS.values():
        executable(shims / command, "raise SystemExit(91)")
    env["PATH"] = f"{shims}{os.pathsep}{env['PATH']}"
    return _run_bash_steps(
        job["steps"][boundary + 1 :], workflow, job, tmp_path, env
    )


def test_required_general_ci_checks_remain_eligible(
    workflow: dict[str, Any],
) -> None:
    """Protect the required-context subset observed in the main Ruleset."""
    events = workflow.get("on", workflow.get(True))
    for event in ("pull_request", "push"):
        assert "main" in events[event]["branches"]
        assert not events[event].get("paths")
        assert not events[event].get("paths-ignore")
    jobs = workflow["jobs"]
    contexts = {job["name"]: key for key, job in jobs.items()}
    node = jobs["node-tests"]
    for version in node["strategy"]["matrix"]["node-version"]:
        contexts[resolve(node["name"], {"matrix.node-version": version})] = (
            "node-tests"
        )
    required = {
        "Validate": "validation",
        "Build & Test (.NET 10)": "dotnet-tests",
        "Test (Python 3.14)": "python-tests",
        "Test & Build (Node.js 22.x)": "node-tests",
        "Test & Build (Node.js 24.x)": "node-tests",
    }
    assert required.items() <= contexts.items()
    for excluded in node["strategy"]["matrix"].get("exclude", []):
        assert excluded.get("node-version") not in ("22.x", "24.x")
    pending = [contexts[name] for name in required]
    visited = set()
    while pending:
        key = pending.pop()
        if key in visited:
            continue
        visited.add(key)
        job = jobs[key]
        if key == "scope":
            _required_step(job)
        else:
            assert job["if"] == (
                "always()" if key == "validation" else "${{ !cancelled() }}"
            )
            assert job["needs"] == (
                ["conformance", "scholarly-tests", "nuget-reproducibility"]
                if key == "validation"
                else ["scope", "node-group"]
                if key == "node-tests"
                else "scope"
            )
        permissions = job.get("permissions", workflow["permissions"])
        assert isinstance(permissions, dict)
        assert permissions.get("contents") == "read"
        assert "write" not in permissions.values()
        for step in job["steps"]:
            _required_context_step(key, step)
        needs = job.get("needs", [])
        pending.extend([needs] if isinstance(needs, str) else needs)


def test_validation_uses_captured_tools_before_install_and_hk(
    workflow: dict[str, Any],
    tmp_path: Path,
) -> None:
    """Bind the setup executables despite new shims and stale mise bindings."""
    env = _commands(tmp_path)
    result = _run_validation(workflow, tmp_path, env)
    assert result.returncode == 0, result.stderr
    calls = [item["command"] for item in _observations(env)]
    links = [command for command in calls if command[:2] == ["mise", "link"]]
    bound = {}
    for command in links:
        assert "--force" in command
        selector, path = [arg for arg in command[2:] if not arg.startswith("-")]
        bound[selector.split("@", 1)[0].removeprefix("core:")] = path
        assert calls.index(command) < calls.index(
            ["mise", "install", "--locked"]
        )
    assert bound == {
        role: str(tmp_path / "setup tools" / command)
        for role, command in TOOL_COMMANDS.items()
    }
    install = calls.index(["mise", "install", "--locked"])
    bootstrap = calls.index(["mise", "bootstrap"])
    hk = next(
        index for index, call in enumerate(calls) if call[:2] == ["hk", "check"]
    )
    assert install < bootstrap < hk


@pytest.mark.parametrize("failure", ["capture", "link", "install"])
def test_validation_bootstrap_failure_stops_hk(
    workflow: dict[str, Any],
    tmp_path: Path,
    failure: str,
) -> None:
    """Keep validation blocked when setup capture or installation fails."""
    env = _commands(tmp_path, missing="ruby" if failure == "capture" else None)
    if failure != "capture":
        env["FAIL_COMMAND"] = json.dumps(["mise", failure])
    result = _run_validation(workflow, tmp_path, env)
    assert result.returncode == (1 if failure == "capture" else 73)
    calls = [item["command"] for item in _observations(env)]
    assert ["mise", "bootstrap"] not in calls
    assert not any(command[0] == "hk" for command in calls)
    if failure in ("capture", "link"):
        assert not any(command[:2] == ["mise", "install"] for command in calls)


@pytest.mark.parametrize("full", [True, False])
@pytest.mark.parametrize("exit_code", [0, 73])
def test_validation_hk_uses_tested_comparison_and_propagates_failure(
    workflow,
    tmp_path,
    full,
    exit_code,
):
    """The tested candidate and explicit full mode reach the HK boundary."""
    env = _commands(tmp_path)
    env["CI_MODE"] = str(full).lower()
    child = "hk" if full else "python"
    if exit_code:
        env["FAIL_COMMAND"] = json.dumps([child])
    result = _run_validation(workflow, tmp_path, env)
    assert result.returncode == exit_code, result.stderr
    observed = next(
        item for item in _observations(env) if item["command"][0] == child
    )
    command = observed["command"]
    if full:
        assert "--all" in command
    else:
        assert command[1] == "eng/scripts/workflow_delivery_v3_hk.py"
        assert command[command.index("--from-ref") + 1] == "a" * 40
        assert command[command.index("--to-ref") + 1] == "d" * 40
    assert {"small", "medium", "large"} <= set(observed["profile"].split(","))


def _retained_command(tmp_path: Path) -> list[str]:
    return [
        "python",
        "eng/scripts/run_node_ci_group.py",
        "retained",
        "--directories",
        json.dumps(NODE_RETAINED),
        "--adopted-directory",
        NODE_ADOPTED,
        "--directory",
        str(tmp_path / "runner-state/retained-node"),
    ]


def test_node_checks_execute_selected_retained_members(
    workflow: dict[str, Any],
    tmp_path: Path,
) -> None:
    """Both retained contexts install before native filtered execution."""
    job = workflow["jobs"]["node-tests"]
    setup = _action(job, "actions/setup-node")
    for version in job["strategy"]["matrix"]["node-version"]:
        assert (
            resolve(
                setup["with"]["node-version"], {"matrix.node-version": version}
            )
            == version
        )
    env = _commands(tmp_path)
    result = _run_bash_steps(job["steps"], workflow, job, tmp_path, env)
    assert result.returncode == 0, result.stderr
    calls = [item["command"] for item in _observations(env)]
    execution = calls.index(_retained_command(tmp_path))
    assert calls.index(["pnpm", "install", "--frozen-lockfile"]) < execution
    assert calls.index(["dotnet", "tool", "restore"]) < execution
    assert ["pnpm", "run", "test"] not in calls
    assert ["pnpm", "run", "build"] not in calls


@pytest.mark.parametrize(
    "failure",
    [
        ("pnpm", "install"),
        ("dotnet", "tool", "restore"),
        ("python", "eng/scripts/run_node_ci_group.py", "retained"),
    ],
    ids=["pnpm-install", "dotnet-tools", "retained-execution"],
)
def test_node_check_propagates_required_command_failure(
    workflow: dict[str, Any],
    tmp_path: Path,
    failure: tuple[str, ...],
) -> None:
    """Fail the retained boundary on dependency or selected work failure."""
    env = _commands(tmp_path)
    env["FAIL_COMMAND"] = json.dumps(failure)
    job = workflow["jobs"]["node-tests"]
    result = _run_bash_steps(job["steps"], workflow, job, tmp_path, env)
    assert result.returncode == CHILD_FAILURE, result.stderr
    calls = [item["command"] for item in _observations(env)]
    assert calls[-1][: len(failure)] == list(failure)


@pytest.mark.parametrize(
    ("selection", "artifact", "succeeds"),
    [
        ("success", "123", True),
        ("failure", "123", False),
        ("skipped", "123", False),
        ("success", "", False),
        ("success", "0", False),
        ("success", "named-artifact", False),
    ],
)
def test_node_group_requires_received_immutable_scope(
    workflow,
    tmp_path,
    selection,
    artifact,
    succeeds,
):
    """Group always plans and consumes the actual scope upload identity."""
    job = workflow["jobs"]["node-group"]
    assert job["needs"] == "scope"
    assert job["if"] == "${{ !cancelled() }}"
    assert "needs.scope.outputs.node" not in json.dumps(job)
    upload = _action(workflow["jobs"]["scope"], "actions/upload-artifact")
    assert upload["id"] == "retain"
    assert workflow["jobs"]["scope"]["outputs"]["artifact_id"] == (
        "${{ steps.retain.outputs.artifact-id }}"
    )
    download = _action(job, "actions/download-artifact")
    assert download["with"]["artifact-ids"] == (
        "${{ needs.scope.outputs.artifact_id }}"
    )
    assert "name" not in download["with"]
    result = run_step(
        job["steps"][0],
        cwd=tmp_path,
        env={},
        bindings={
            "needs.scope.result": selection,
            "needs.scope.outputs.artifact_id": artifact,
        },
        workflow=workflow,
        job=job,
    )
    assert (result.returncode == 0) is succeeds, result.stderr
    checkout = _action(job, "actions/checkout")
    assert checkout["with"]["persist-credentials"] is False
    assert checkout["with"]["fetch-depth"] == 0
    assert job["permissions"] == {"contents": "read", "actions": "read"}
    token_steps = [
        step for step in job["steps"] if "GH_TOKEN" in step.get("env", {})
    ]
    assert len(token_steps) == 1
    assert "gh api" in token_steps[0]["run"]


def test_python_check_has_consumed_toolchain_prerequisites(
    workflow: dict[str, Any],
) -> None:
    """Bind the setup inputs consumed by Python bootstrap and preparation."""
    job = workflow["jobs"]["python-tests"]
    steps = job["steps"]
    first_run = next(
        index
        for index, step in enumerate(steps)
        if "run" in step and step.get("id") != "scope"
    )
    prerequisites = {
        name: _action(job, name)
        for name in (
            "actions/checkout",
            "actions/setup-dotnet",
            "pnpm/action-setup",
            "actions/setup-node",
            "jdx/mise-action",
            "actions/setup-python",
            "astral-sh/setup-uv",
        )
    }
    assert all(steps.index(step) < first_run for step in prerequisites.values())
    assert prerequisites["actions/checkout"]["with"]["fetch-depth"] == 0
    assert (
        prerequisites["actions/setup-dotnet"]["with"]["global-json-file"]
        == "global.json"
    )
    assert prerequisites["pnpm/action-setup"]["with"]["version"]
    assert prerequisites["actions/setup-node"]["with"]["node-version"]
    mise = prerequisites["jdx/mise-action"]["with"]
    assert mise["experimental"] is True
    assert {"aqua:jdx/hk", "pkl"} <= set(mise["install_args"].split())
    assert mise.get("install", True) is not False
    assert (
        prerequisites["actions/setup-python"]["with"]["python-version-file"]
        == ".python-version"
    )
    assert "python-version" not in prerequisites["astral-sh/setup-uv"]["with"]
    assert (
        tomllib.loads(
            (
                REPO_ROOT
                / prerequisites["astral-sh/setup-uv"]["with"]["version-file"]
            ).read_text()
        )["required-version"]
        == "==" + dict(PYTHON_TOOLCHAIN)["uv"]
    )
    for action, flag in (
        ("actions/setup-dotnet", "python_dotnet"),
        ("pnpm/action-setup", "python_v3"),
        ("actions/setup-node", "python_v3"),
        ("jdx/mise-action", "python_v3"),
    ):
        assert prerequisites[action]["if"] == (
            "success() && !cancelled() && steps.scope.outputs.run == 'true' "
            f"&& needs.scope.outputs.{flag} == 'true'"
        )
    for name, flag in (
        ("Restore .NET tools", "python_dotnet"),
        ("Install Node dependencies", "python_v3"),
        ("Prepare static-reference authorities", "python_v3"),
    ):
        step = next(step for step in steps if step["name"] == name)
        assert step["if"].endswith(f"needs.scope.outputs.{flag} == 'true'")


def _run_python(workflow, tmp_path, env, *, native=True):
    executable(
        Path(env["PATH"]) / "python",
        "import os, sys\n"
        f"os.execv({sys.executable!r}, [{sys.executable!r}, *sys.argv[1:]])",
    )
    job = workflow["jobs"]["python-tests"]
    roots = json.loads(
        env.get(
            "SELECTED_PYTHON_ROOTS",
            _bindings(tmp_path)["needs.scope.outputs.python_roots"],
        )
    )
    # Choose the known selected-consumer branch; do not emulate Actions.
    steps = [
        step
        for step in job["steps"]
        if (
            PYTHON_NATIVE_ROOT in roots
            if PYTHON_NATIVE_ROOT in step.get("if", "")
            else native
            or "needs.scope.outputs.python_" not in step.get("if", "")
        )
    ]
    return _run_bash_steps(steps, workflow, job, tmp_path, env)


@pytest.mark.parametrize("selected", [False, True])
@pytest.mark.parametrize("native", [False, True])
def test_python_native_helper_checks_use_selected_scope(
    workflow: dict[str, Any],
    tmp_path: Path,
    *,
    selected: bool,
    native: bool,
) -> None:
    """Helper checks follow their selected root independently of V3 setup."""
    env = _commands(tmp_path)
    env["SELECTED_PYTHON_ROOTS"] = json.dumps(
        [PYTHON_NATIVE_ROOT] if selected else ["tests/eng/test_ci_scope.py"]
    )
    env["SELECTED_PACKAGES"] = json.dumps(
        ["hcoona-three-monorepo", "three-workflow-delivery-v3"]
        if native
        else ["hcoona-three-monorepo"]
    )
    (tmp_path / PYTHON_NATIVE_DIRECTORY).mkdir(parents=True)
    steps = workflow["jobs"]["python-tests"]["steps"]
    helper_steps = [
        step for step in steps if PYTHON_NATIVE_ROOT in step.get("if", "")
    ]
    assert [step["name"] for step in helper_steps] == [
        "Prepare Python native helper toolchain",
        "Check Python native helper source",
    ]
    for step in helper_steps:
        assert step["if"] == PYTHON_NATIVE_CONDITION
        assert step["working-directory"] == PYTHON_NATIVE_DIRECTORY
        _required_step(step)
    result = _run_python(workflow, tmp_path, env, native=native)
    assert result.returncode == 0, result.stderr
    calls = [item["command"] for item in _observations(env)]
    helper_calls = [call for call in calls if call[0] in {"rustup", "cargo"}]
    assert helper_calls == (
        [
            ["rustup", "show"],
            ["cargo", "fmt", "--all", "--", "--check"],
            ["cargo", "build", "--locked", "--all-targets", "--jobs", "4"],
            ["cargo", "test", "--locked", "--all-targets", "--jobs", "4"],
            [
                "cargo",
                "clippy",
                "--locked",
                "--all-targets",
                "--jobs",
                "4",
                "--",
                "-D",
                "warnings",
            ],
        ]
        if selected
        else []
    )
    if selected:
        assert calls.index(helper_calls[-1]) < calls.index(
            ["uv", "run", "--no-sync", "python", "-"]
        )


@pytest.mark.parametrize(
    "failure",
    [
        ["rustup", "show"],
        *[
            ["cargo", operation]
            for operation in ("fmt", "build", "test", "clippy")
        ],
    ],
    ids=["toolchain", "fmt", "build", "test", "clippy"],
)
def test_python_native_helper_propagates_command_failure(
    workflow: dict[str, Any],
    tmp_path: Path,
    failure: list[str],
) -> None:
    """A failed helper check prevents later preparation and pytest."""
    env = _commands(tmp_path)
    env["SELECTED_PYTHON_ROOTS"] = json.dumps([PYTHON_NATIVE_ROOT])
    env["FAIL_COMMAND"] = json.dumps(failure)
    (tmp_path / PYTHON_NATIVE_DIRECTORY).mkdir(parents=True)
    result = _run_python(workflow, tmp_path, env, native=False)
    assert result.returncode == CHILD_FAILURE, result.stderr
    calls = [item["command"] for item in _observations(env)]
    assert calls[-1][: len(failure)] == failure
    assert not any(call[0] == "uv" for call in calls)


@pytest.mark.parametrize("native", [False, True])
def test_general_python_ci_prepares_static_reference_authorities(
    workflow: dict[str, Any],
    tmp_path: Path,
    *,
    native: bool,
) -> None:
    """Prepare native consumers only when selected, before running pytest."""
    env = _commands(tmp_path)
    packages = ["hcoona-three-monorepo"]
    if native:
        packages.append("three-workflow-delivery-v3")
    env["SELECTED_PACKAGES"] = json.dumps(packages)
    result = _run_python(workflow, tmp_path, env, native=native)
    assert result.returncode == 0, result.stderr
    observations = _observations(env)
    calls = [item["command"] for item in observations]
    sync = [
        "uv",
        "sync",
        "--frozen",
        *[arg for package in packages for arg in ("--package", package)],
    ]
    test = ["uv", "run", "--no-sync", "python", "-"]
    assert calls.index(sync) < calls.index(test)
    if not native:
        assert calls == [sync, test]
        return
    preparation = calls.index(
        ["mise", "run", "prepare:static-reference-authorities"]
    )
    for prerequisite in (
        ["dotnet", "tool", "restore"],
        ["pnpm", "install", "--frozen-lockfile"],
        sync,
    ):
        assert calls.index(prerequisite) < preparation
    assert observations[preparation]["auto_install"] == "false"
    assert preparation < calls.index(test)


@pytest.mark.parametrize(
    "failure",
    [
        ["dotnet", "tool", "restore"],
        ["pnpm", "install"],
        ["uv", "sync"],
        ["mise", "run"],
        ["uv", "run"],
    ],
    ids=["dotnet-tools", "pnpm-install", "uv-sync", "preparation", "pytest"],
)
def test_python_check_propagates_command_failure(
    workflow: dict[str, Any],
    tmp_path: Path,
    failure: list[str],
) -> None:
    """Propagate each distinct dependency, preparation and test failure."""
    env = _commands(tmp_path)
    env["FAIL_COMMAND"] = json.dumps(failure)
    result = _run_python(workflow, tmp_path, env)
    assert result.returncode != 0, result.stderr
    calls = [item["command"] for item in _observations(env)]
    assert calls[-1][: len(failure)] == failure
    if failure[0] != "mise" and failure != ["uv", "run"]:
        assert not any(command[:2] == ["mise", "run"] for command in calls)
    if failure != ["uv", "run"]:
        assert not any(command[:2] == ["uv", "run"] for command in calls)


def _run_dotnet(
    workflow: dict[str, Any],
    tmp_path: Path,
    env: dict[str, str],
    *,
    bindings: dict[str, str] | None = None,
) -> subprocess.CompletedProcess[str]:
    """Exercise required receiver bodies through the maintained shell helper."""
    job = workflow["jobs"]["dotnet-tests"]
    result = None
    for step in job["steps"]:
        if "run" not in step or step.get("if") == "cancelled()":
            continue
        if step.get("continue-on-error", False):
            _required_context_step("dotnet-tests", step)
            continue
        _required_step(step)
        result = run_step(
            step,
            cwd=tmp_path,
            env=env,
            bindings=_bindings(tmp_path) | (bindings or {}),
            workflow=workflow,
            job=job,
        )
        if result.returncode:
            break
    assert result is not None
    return result


def _dotnet_commands(tmp_path: Path) -> dict[str, str]:
    env = _commands(tmp_path)
    Path(env["RUNNER_TEMP"]).mkdir()
    env.update(
        GITHUB_REPOSITORY="hcoona/three",
        GITHUB_REPOSITORY_ID="12345",
        GITHUB_RUN_ID="67890",
    )
    executable(
        Path(env["PATH"]) / "gh",
        COMMAND_RECORDER + "\nprint('fixture-artifact-metadata')\n",
    )
    return env


def _dotnet_receiver_command(
    tmp_path: Path, artifact: str = "1054"
) -> list[str]:
    temporary = tmp_path / "runner-state"
    return [
        "python",
        "eng/scripts/run_dotnet_ci_group.py",
        "execute",
        "--transfer",
        str(temporary / "dotnet-plan-transfer"),
        "--directory",
        str(temporary / "dotnet-execution"),
        "--artifact-metadata",
        str(temporary / "dotnet-plan-artifact.json"),
        "--artifact-id",
        artifact,
        "--run-id",
        "67890",
        "--repository-id",
        "12345",
        "--basis",
        "a" * 40,
        "--candidate",
        "d" * 40,
    ]


def test_dotnet_check_receives_original_plan_and_complete_control(
    workflow: dict[str, Any],
    tmp_path: Path,
) -> None:
    """Transfer the producer's full output by ID and bind receiver lifetime."""
    scope = workflow["jobs"]["scope"]
    job = workflow["jobs"]["dotnet-tests"]
    assert job["runs-on"].startswith("windows-")
    assert job["permissions"] == {"contents": "read", "actions": "read"}
    assert "needs.scope.outputs.dotnet }}" not in json.dumps(job)
    assert _action(job, "actions/setup-dotnet")["with"] == {
        "global-json-file": "global.json"
    }
    checkout = _action(job, "actions/checkout")
    assert checkout["with"]["ref"] == "${{ github.sha }}"
    assert checkout["with"]["persist-credentials"] is False
    assert checkout["with"]["fetch-depth"] == 0
    producer = next(
        step for step in scope["steps"] if step.get("id") == "control"
    )
    assert producer["run"].strip() == (
        "python eng/scripts/run_dotnet_ci_group.py bootstrap "
        '--directory "$RUNNER_TEMP/dotnet-group"'
    )
    upload = next(
        step for step in scope["steps"] if step.get("id") == "retain-dotnet"
    )
    assert upload["uses"].startswith("actions/upload-artifact@")
    assert upload["with"]["path"] == "${{ steps.control.outputs.transfer }}/"
    assert upload["with"]["if-no-files-found"] == "error"
    assert scope["outputs"]["dotnet_artifact_id"] == (
        "${{ steps.retain-dotnet.outputs.artifact-id }}"
    )
    download = _action(job, "actions/download-artifact")
    assert download["with"] == {
        "artifact-ids": "${{ needs.scope.outputs.dotnet_artifact_id }}",
        "path": "${{ runner.temp }}/dotnet-plan-transfer",
        "merge-multiple": True,
    }
    metadata = next(
        step for step in job["steps"] if "GH_TOKEN" in step.get("env", {})
    )
    expression = "${{ github.token }}"
    assert metadata["env"]["GH_TOKEN"] == expression
    execute = next(
        step for step in job["steps"] if " execute " in step.get("run", "")
    )
    assert scope["steps"].index(producer) < scope["steps"].index(upload)
    assert (
        job["steps"].index(metadata)
        < job["steps"].index(download)
        < job["steps"].index(execute)
    )
    env = _dotnet_commands(tmp_path)
    result = _run_dotnet(workflow, tmp_path, env)
    assert result.returncode == 0, result.stderr
    assert [item["command"] for item in _observations(env)] == [
        ["gh", "api", "repos/hcoona/three/actions/artifacts/1054"],
        _dotnet_receiver_command(tmp_path),
    ]
    assert (
        tmp_path / "runner-state/dotnet-plan-artifact.json"
    ).read_text() == ("fixture-artifact-metadata\n")


@pytest.mark.parametrize("failure", ["metadata", "execute"])
def test_dotnet_check_propagates_required_command_failure(
    workflow: dict[str, Any],
    tmp_path: Path,
    failure: str,
) -> None:
    """Propagate required metadata and receiver failures."""
    env = _dotnet_commands(tmp_path)
    command = (
        ["gh", "api", "repos/hcoona/three/actions/artifacts/1054"]
        if failure == "metadata"
        else _dotnet_receiver_command(tmp_path)
    )
    env["FAIL_COMMAND"] = json.dumps(command)
    result = _run_dotnet(workflow, tmp_path, env)
    assert result.returncode == CHILD_FAILURE, result.stderr
    calls = [item["command"] for item in _observations(env)]
    assert calls == [
        ["gh", "api", "repos/hcoona/three/actions/artifacts/1054"],
        *(
            []
            if failure == "metadata"
            else [_dotnet_receiver_command(tmp_path)]
        ),
    ]


@pytest.mark.parametrize(
    ("selection", "artifact", "succeeds"),
    [
        ("success", "1054", True),
        ("success", "1", True),
        ("failure", "1054", False),
        ("cancelled", "1054", False),
        ("skipped", "1054", False),
        ("", "1054", False),
        ("success", "", False),
        ("success", "0", False),
        ("success", "-1", False),
        ("success", "1x", False),
        ("success", "01", False),
    ],
)
def test_dotnet_group_requires_received_original_plan(
    workflow, tmp_path, selection, artifact, succeeds
):
    """Reject failed planning or invalid native IDs before any receiver work."""
    env = _dotnet_commands(tmp_path)
    result = _run_dotnet(
        workflow,
        tmp_path,
        env,
        bindings={
            "needs.scope.result": selection,
            "needs.scope.outputs.dotnet_artifact_id": artifact,
        },
    )
    assert (result.returncode == 0) is succeeds, result.stderr
    if not succeeds:
        assert _observations(env) == []
        assert not (
            tmp_path / "runner-state/dotnet-plan-artifact.json"
        ).exists()
    else:
        assert [item["command"] for item in _observations(env)] == [
            ["gh", "api", f"repos/hcoona/three/actions/artifacts/{artifact}"],
            _dotnet_receiver_command(tmp_path, artifact),
        ]


@pytest.mark.parametrize(
    ("selection", "applicable", "run"),
    [
        ("success", "true", "true"),
        ("success", "false", "false"),
        ("failure", "false", None),
        ("cancelled", "false", None),
        ("skipped", "", None),
        ("success", "", None),
    ],
)
def test_ci_scope_guard_rejects_missing_or_failed_selection(
    workflow, tmp_path, selection, applicable, run
):
    """Distinguish valid non-applicability from lost required work."""
    for name, job in workflow["jobs"].items():
        if name in {"scope", "validation", "node-group", "dotnet-tests"}:
            continue
        assert job["needs"] == (
            ["scope", "node-group"] if name == "node-tests" else "scope"
        )
        assert job["if"] == "${{ !cancelled() }}"
        guard = job["steps"][0]
        assert guard["id"] == "scope"
        output = tmp_path / name
        key = (
            guard["env"]["APPLICABLE"].removeprefix("${{ ").removesuffix(" }}")
        )
        result = run_step(
            guard,
            cwd=tmp_path,
            env={"GITHUB_OUTPUT": str(output)},
            bindings=_bindings(tmp_path)
            | {
                "needs.scope.result": selection,
                key: applicable,
                "needs.node-group.result": "success",
            },
            workflow=workflow,
            job=job,
        )
        assert (result.returncode == 0) is (run is not None), result.stderr
        if run is not None:
            assert output.read_text().strip() == f"run={run}"
        else:
            assert not output.exists()
        for step in job["steps"][1:-1]:
            assert "steps.scope.outputs.run == 'true'" in step["if"]
            assert not step.get("continue-on-error", False)


def test_required_validate_rejects_missing_or_failed_consumers(
    workflow, tmp_path
):
    """Keep every selected validation consumer inside the required check."""
    job = workflow["jobs"]["validation"]
    assert job["name"] == "Validate"
    assert job["needs"] == [
        "conformance",
        "scholarly-tests",
        "nuget-reproducibility",
    ]
    assert job["if"] == "always()"
    step = job["steps"][0]
    assert step["if"] == "always()"
    assert not step.get("continue-on-error", False)
    successful = {
        "needs.conformance.result": "success",
        "needs.scholarly-tests.result": "success",
        "needs.nuget-reproducibility.result": "success",
    }
    scenarios = [successful]
    for dependency in (
        "conformance",
        "scholarly-tests",
        "nuget-reproducibility",
    ):
        for result in ("failure", "cancelled", "skipped", ""):
            scenarios.append(
                successful | {f"needs.{dependency}.result": result}
            )
    for bindings in scenarios:
        result = run_step(
            step,
            cwd=tmp_path,
            env={},
            bindings=bindings,
            workflow=workflow,
            job=job,
        )
        assert (result.returncode == 0) is (bindings == successful)
        if bindings != successful:
            assert "Required validation did not complete" in result.stderr


def test_nuget_reproducibility_selects_native_windows_evidence(workflow):
    """Required Windows validation covers native bytes and Attempt recovery."""
    job = workflow["jobs"]["nuget-reproducibility"]
    assert job["runs-on"].startswith("windows-")
    assert job["steps"][0]["env"]["APPLICABLE"] == (
        "${{ needs.scope.outputs.python_dotnet }}"
    )
    checkout = _action(job, "actions/checkout")
    assert checkout["with"]["fetch-depth"] == 0
    assert checkout["with"]["persist-credentials"] is False
    execute = next(
        step for step in job["steps"] if "pytest" in step.get("run", "")
    )
    assert execute["shell"] == "pwsh"
    for path in (
        "tests/adapters/test_dotnet.py",
        "tests/release/test_nuget_native_recovery.py",
    ):
        assert path in execute["run"]
    for selector in (
        "reproduc",
        "target_timestamp",
        "negative_controls",
        "fresh_nuget_attempt",
    ):
        assert selector in execute["run"]
    assert "--basetemp" in execute["run"]
    assert "--junitxml" in execute["run"]
    upload = _action(job, "actions/upload-artifact")
    assert upload["if"] == "always() && steps.scope.outputs.run == 'true'"
    assert upload["with"]["if-no-files-found"] == "error"
    assert upload["with"]["path"] == "artifacts/nuget-reproducibility"
    assert job["env"]["WDV3_NUGET_REPRO_EVIDENCE"] == (
        "${{ github.workspace }}/artifacts/nuget-reproducibility"
    )


def test_python_scope_guard_rejects_missing_preparation(workflow, tmp_path):
    """Missing preparation selection cannot silently skip required setup."""
    job = workflow["jobs"]["python-tests"]
    for flag in ("python_v3", "python_dotnet"):
        output = tmp_path / flag
        result = run_step(
            job["steps"][0],
            cwd=tmp_path,
            env={"GITHUB_OUTPUT": str(output)},
            bindings=_bindings(tmp_path)
            | {
                "needs.scope.result": "success",
                "needs.scope.outputs.python": "true",
                f"needs.scope.outputs.{flag}": "",
            },
            workflow=workflow,
            job=job,
        )
        assert result.returncode != 0
        assert "Missing Python preparation selection" in result.stderr
        assert not output.exists()


def test_canceled_ci_work_stops_and_cannot_report_success(workflow, tmp_path):
    """Use documented cancelable jobs; shell checks do not model Actions."""
    assert workflow["concurrency"]["cancel-in-progress"] is True
    for name, job in workflow["jobs"].items():
        if name == "scope":
            continue
        assert job["if"] == (
            "always()" if name == "validation" else "${{ !cancelled() }}"
        )
        for step in job["steps"][1:-1]:
            if name == "dotnet-tests":
                if step["name"] == "Retain NuGet authority diagnostics":
                    assert step["continue-on-error"] is True
                    assert step["if"] == (
                        "(failure() || "
                        "hashFiles('artifacts/nuget-authority-diagnostics/"
                        "**/slow-process.txt') != '')"
                    )
                elif step["name"] == "Prepare NuGet authority dump collection":
                    assert step["continue-on-error"] is True
                    assert step["shell"] == "pwsh"
                    assert step["if"] == (
                        "${{ success() && "
                        "needs.scope.outputs.dotnet == 'true' }}"
                    )
                elif "Retain" in step["name"]:
                    assert step["if"] == "always()"
                    assert step["uses"].startswith("actions/upload-artifact@")
                    assert not step.get("continue-on-error", False)
                else:
                    _required_step(step)
            elif name == "node-group":
                assert step.get("if", "success()") in {"success()", "always()"}
                assert not step.get("continue-on-error", False)
            elif step["if"].startswith("always() &&"):
                assert step.get("uses", "").startswith(
                    "actions/upload-artifact@"
                )
            else:
                assert step["if"].startswith(
                    "success() && !cancelled() && "
                    "steps.scope.outputs.run == 'true'"
                )
        terminal = job["steps"][-1]
        assert terminal["if"] == "cancelled()"
        assert not terminal.get("continue-on-error", False)
        result = run_step(
            terminal,
            cwd=tmp_path,
            env={},
            bindings=_bindings(tmp_path),
            workflow=workflow,
            job=job,
        )
        assert result.returncode != 0
        assert "canceled before a complete result" in result.stderr


@pytest.mark.parametrize("full", ["false", "true"])
@pytest.mark.parametrize("failure", [None, "evaluate", "select"])
def test_scope_selection_uses_project_python_and_tested_comparison(
    workflow, tmp_path, full, failure
):
    """Select from evaluated endpoint inputs and propagate command failures."""
    job = workflow["jobs"]["scope"]
    setup = _action(job, "actions/setup-python")
    select = next(step for step in job["steps"] if step.get("id") == "select")
    assert setup["with"]["python-version-file"] == ".python-version"
    assert job["steps"].index(setup) < job["steps"].index(select)
    parser_setup = _action(job, "astral-sh/setup-uv")
    parser = next(
        step
        for step in job["steps"]
        if step["name"] == "Prepare existing root selection parser"
    )
    assert job["steps"].index(parser_setup) < job["steps"].index(parser)
    assert job["steps"].index(parser) < job["steps"].index(select)
    assert parser["run"].strip() == (
        "uv sync --frozen --only-group dev "
        "--no-install-project --no-install-workspace"
    )
    env = _commands(tmp_path)
    runner_temp = tmp_path / "runner temp"
    request = str(runner_temp / "ci-control-inputs/request.json")
    response = str(runner_temp / "ci-control-inputs/response.json")
    env["RUNNER_TEMP"] = str(runner_temp)
    evaluate = [
        "python",
        "eng/scripts/prepare_ci_control_inputs.py",
        "--application",
        _bindings(tmp_path)["steps.control.outputs.application"],
        "evaluate",
        "--request",
        request,
        "--response",
        response,
    ]
    selector = [
        "uv",
        "run",
        "--frozen",
        "--no-sync",
        "python",
        "eng/scripts/ci_scope.py",
        *(
            ["--full"]
            if full == "true"
            else ["--from-ref", "a" * 40, "--to-ref", "d" * 40]
        ),
        "--control-inputs",
        response,
        "--output",
        "artifacts/ci-scope.json",
        "--application",
        _bindings(tmp_path)["steps.control.outputs.application"],
        "--dotnet-group-request",
        _bindings(tmp_path)["steps.dotnet-group.outputs.request"],
    ]
    if failure is not None:
        env["FAIL_COMMAND"] = json.dumps(
            evaluate if failure == "evaluate" else selector
        )
    result = run_step(
        select,
        cwd=tmp_path,
        env=env,
        bindings=_bindings(tmp_path)
        | {
            "steps.materialize.outputs.request": request,
            "steps.materialize.outputs.basis": ("d" if full == "true" else "a")
            * 40,
            "steps.materialize.outputs.candidate": "d" * 40,
            "steps.materialize.outputs.full": full,
        },
        workflow=workflow,
        job=job,
    )
    assert result.returncode == (0 if failure is None else CHILD_FAILURE), (
        result.stderr
    )
    assert [item["command"] for item in _observations(env)] == [
        ["python", "--version"],
        evaluate,
        *([] if failure == "evaluate" else [selector]),
    ]


def test_python_test_entry_runs_only_selected_roots_and_propagates_failure(
    workflow, tmp_path
):
    """Execute the embedded entry point with passing and failing roots."""
    job = workflow["jobs"]["python-tests"]
    step = next(item for item in job["steps"] if item["name"] == "Run tests")
    executable(
        tmp_path / "bin" / "uv",
        "import os, sys\n"
        "assert sys.argv[1:4] == ['run', '--no-sync', 'python']\n"
        "os.execv(sys.executable, [sys.executable, *sys.argv[4:]])\n",
    )
    selected = tmp_path / "selected"
    selected.mkdir()
    (selected / "test_example.py").write_text(
        "def test_selected():\n    assert True\n"
    )
    unrelated = tmp_path / "unrelated"
    unrelated.mkdir()
    (unrelated / "test_unrelated.py").write_text(
        "raise RuntimeError('must not collect')\n"
    )
    (tmp_path / "pyproject.toml").write_text(
        '[tool.pytest.ini_options]\ntestpaths = ["selected", "unrelated"]\n'
    )
    for failing in (False, True):
        if failing:
            (selected / "test_example.py").write_text(
                "def test_selected():\n    assert False\n"
            )
        result = run_step(
            step,
            cwd=tmp_path,
            env={
                "PATH": f"{tmp_path / 'bin'}{os.pathsep}{os.environ['PATH']}",
                "PYTEST_ADDOPTS": "-p no:cacheprovider",
            },
            bindings=_bindings(tmp_path)
            | {"needs.scope.outputs.python_roots": '["selected"]'},
            workflow=workflow,
            job=job,
        )
        assert (result.returncode != 0) is failing, result.stderr
        assert "must not collect" not in result.stdout + result.stderr
        assert (tmp_path / "artifacts/test-results/python.xml").is_file()

    for inventory, roots in (
        ("[]", '["selected"]'),
        ('"selected"', '["selected"]'),
        ('[""]', '["selected"]'),
        ("[true]", '["selected"]'),
        ('["selected"]', "[]"),
        ('["selected"]', '"selected"'),
    ):
        (tmp_path / "pyproject.toml").write_text(
            f"[tool.pytest.ini_options]\ntestpaths = {inventory}\n"
        )
        artifact = tmp_path / "artifacts/test-results/python.xml"
        artifact.unlink(missing_ok=True)
        result = run_step(
            step,
            cwd=tmp_path,
            env={"PATH": f"{tmp_path / 'bin'}{os.pathsep}{os.environ['PATH']}"},
            bindings=_bindings(tmp_path)
            | {"needs.scope.outputs.python_roots": roots},
            workflow=workflow,
            job=job,
        )
        assert result.returncode != 0
        assert "Python test" in result.stderr
        assert not artifact.exists()
