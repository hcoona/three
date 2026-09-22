"""General-CI consumers and executable command boundaries during coexistence."""

from __future__ import annotations

import json
import os
import shutil
import subprocess
from pathlib import Path
from typing import Any

import pytest
import yaml

from .workflow_shell import executable, resolve, run_step

REPO_ROOT = Path(__file__).resolve().parents[6]
CHILD_FAILURE = 73
NODE_PACKAGE_DIR = "src/public/lib/hexo-renderer-asciidoc"
NODE_WORK_COMMANDS = (
    ("pnpm", "--dir", NODE_PACKAGE_DIR, "run", "typecheck"),
    ("pnpm", "run", "test"),
    ("pnpm", "run", "build"),
    ("pnpm", "--dir", NODE_PACKAGE_DIR, "run", "validate:packed-artifact"),
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
        "steps.scope.outputs.run == 'true'",
    )
    assert scope.get("continue-on-error", False) is False


def _bindings(tmp_path: Path) -> dict[str, str]:
    return {
        "github.workspace": str(tmp_path),
        "secrets.GITHUB_TOKEN": "fixture-token",
        "github.event.pull_request.base.sha": "a" * 40,
        "github.event.pull_request.head.sha": "b" * 40,
        "github.event.before": "c" * 40,
        "github.sha": "d" * 40,
        "needs.scope.outputs.base": "a" * 40,
        "needs.scope.outputs.candidate": "d" * 40,
        "needs.scope.outputs.full": "true",
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
    }


def _observations(env: dict[str, str]) -> list[dict[str, Any]]:
    log = Path(env["COMMAND_LOG"])
    observations = (
        [json.loads(line) for line in log.read_text().splitlines()]
        if log.exists()
        else []
    )
    for observation in observations:
        assert Path(observation["cwd"]) == log.parent
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
        if "run" not in step or step.get("id") == "scope":
            continue
        _required_step(step)
        result = run_step(
            step,
            cwd=tmp_path,
            env=env,
            bindings=_bindings(tmp_path)
            | {"needs.scope.outputs.full": env.get("CI_MODE", "true")},
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
    job = workflow["jobs"]["validation"]
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
            assert job["if"] == "always()"
            assert job["needs"] == "scope"
        permissions = job.get("permissions", workflow["permissions"])
        assert isinstance(permissions, dict)
        assert permissions.get("contents") == "read"
        assert "write" not in permissions.values()
        for step in job["steps"]:
            if "Retain" not in step["name"]:
                _required_step(step)
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


def test_node_checks_execute_workspace_and_package_work(
    workflow: dict[str, Any],
    tmp_path: Path,
) -> None:
    """Both Node contexts reach frozen install and required Node work."""
    job = workflow["jobs"]["node-tests"]
    setup = _action(job, "actions/setup-node")
    for version in ("22.x", "24.x"):
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
    for command in NODE_WORK_COMMANDS:
        assert calls.index(
            ["pnpm", "install", "--frozen-lockfile"]
        ) < calls.index(list(command))
        assert calls.index(["dotnet", "tool", "restore"]) < calls.index(
            list(command)
        )


@pytest.mark.parametrize(
    "failure",
    [
        ("pnpm", "install"),
        ("dotnet", "tool", "restore"),
        *NODE_WORK_COMMANDS,
    ],
    ids=[
        "pnpm-install",
        "dotnet-tools",
        "typecheck",
        "workspace-test",
        "workspace-build",
        "packed-artifact",
    ],
)
def test_node_check_propagates_required_command_failure(
    workflow: dict[str, Any],
    tmp_path: Path,
    failure: tuple[str, ...],
) -> None:
    """Fail the Node boundary at each required dependency or work command."""
    env = _commands(tmp_path)
    env["FAIL_COMMAND"] = json.dumps(failure)
    job = workflow["jobs"]["node-tests"]
    result = _run_bash_steps(job["steps"], workflow, job, tmp_path, env)
    assert result.returncode == CHILD_FAILURE, result.stderr
    calls = [item["command"] for item in _observations(env)]
    assert calls[-1][: len(failure)] == list(failure)


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
    assert {"hk", "pkl"} <= set(mise["install_args"].split())
    assert mise.get("install", True) is not False
    assert (
        prerequisites["actions/setup-python"]["with"]["python-version-file"]
        == ".python-version"
    )
    assert "python-version" not in prerequisites["astral-sh/setup-uv"]["with"]


def test_general_python_ci_prepares_static_reference_authorities(
    workflow: dict[str, Any],
    tmp_path: Path,
) -> None:
    """Complete frozen dependencies and preparation before invoking pytest."""
    job = workflow["jobs"]["python-tests"]
    env = _commands(tmp_path)
    result = _run_bash_steps(job["steps"], workflow, job, tmp_path, env)
    assert result.returncode == 0, result.stderr
    observations = _observations(env)
    calls = [item["command"] for item in observations]
    preparation = calls.index(
        ["mise", "run", "prepare:static-reference-authorities"]
    )
    for prerequisite in (
        ["dotnet", "tool", "restore"],
        ["pnpm", "install", "--frozen-lockfile"],
        ["uv", "sync", "--frozen", "--all-packages"],
    ):
        assert calls.index(prerequisite) < preparation
    assert observations[preparation]["auto_install"] == "false"
    assert preparation < calls.index(
        ["uv", "run", "--frozen", "--all-packages", "python", "-"]
    )


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
    job = workflow["jobs"]["python-tests"]
    result = _run_bash_steps(job["steps"], workflow, job, tmp_path, env)
    assert result.returncode == CHILD_FAILURE, result.stderr
    calls = [item["command"] for item in _observations(env)]
    assert calls[-1][: len(failure)] == failure
    if failure[0] != "mise" and failure != ["uv", "run"]:
        assert not any(command[:2] == ["mise", "run"] for command in calls)
    if failure != ["uv", "run"]:
        assert not any(command[:2] == ["uv", "run"] for command in calls)


def _dotnet_projects(tmp_path: Path) -> dict[str, Path]:
    declarations = {
        "mstest": '<Project Sdk="MSTest.Sdk" />',
        "xunit-mtp": (
            '<Project><PackageReference Include="xunit.v3.mtp-v2" /></Project>'
        ),
        "vstest": (
            "<Project><PackageReference "
            'Include="xunit.runner.visualstudio" /></Project>'
        ),
        "library": '<Project Sdk="Microsoft.NET.Sdk" />',
    }
    projects = {}
    for name, content in declarations.items():
        path = tmp_path / "tests" / name / f"{name}.csproj"
        path.parent.mkdir(parents=True)
        path.write_text(content)
        projects[name] = path
    return projects


def _run_dotnet(
    workflow: dict[str, Any],
    tmp_path: Path,
    env: dict[str, str],
) -> subprocess.CompletedProcess[str]:
    job = workflow["jobs"]["dotnet-tests"]
    pwsh = shutil.which("pwsh")
    assert pwsh is not None, "The managed CI toolchain must provide PowerShell."
    result = None
    for step in job["steps"]:
        if "run" not in step or step.get("id") == "scope":
            continue
        _required_step(step)
        process_env = {**os.environ, **env}
        shell = "pwsh"
        directory = "."
        for scope in (workflow, job, step):
            process_env.update(
                {
                    key: resolve(str(value), _bindings(tmp_path))
                    for key, value in scope.get("env", {}).items()
                }
            )
            defaults = scope.get("defaults", {}).get("run", {})
            shell = defaults.get("shell", shell)
            directory = defaults.get("working-directory", directory)
        shell = step.get("shell", shell)
        directory = step.get("working-directory", directory)
        assert shell == "pwsh", f"Unsupported shell: {shell}"
        working_directory = tmp_path / resolve(directory, _bindings(tmp_path))
        body = resolve(step["run"], _bindings(tmp_path))
        # Actions prepends error handling and propagates native command status.
        script = (
            "$ErrorActionPreference = 'stop'\n"
            + body
            + (
                "\nif (Test-Path -LiteralPath variable:\\LASTEXITCODE) "
                "{ exit $LASTEXITCODE }\n"
            )
        )
        path = tmp_path / "step.ps1"
        path.write_text(script)
        result = subprocess.run(  # noqa: S603 - checked-in shell, controlled tools
            [
                pwsh,
                "-NoLogo",
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                f". '{path}'",
            ],
            cwd=working_directory,
            env=process_env,
            text=True,
            capture_output=True,
            check=False,
            timeout=30,
        )
        if result.returncode:
            break
    assert result is not None
    return result


def test_dotnet_check_executes_restore_build_and_supported_test_projects(
    workflow: dict[str, Any],
    tmp_path: Path,
) -> None:
    """Run locked build and both test platforms with real project selection."""
    job = workflow["jobs"]["dotnet-tests"]
    assert job["runs-on"].startswith("windows-")
    assert (
        _action(job, "actions/setup-dotnet")["with"]["global-json-file"]
        == "global.json"
    )
    projects = _dotnet_projects(tmp_path)
    env = _commands(tmp_path)
    result = _run_dotnet(workflow, tmp_path, env)
    assert result.returncode == 0, result.stderr
    calls = [item["command"] for item in _observations(env)]
    restore = calls.index(
        ["dotnet", "restore", "dirs.proj", "-p:RestoreLockedMode=true"]
    )
    build = calls.index(
        [
            "dotnet",
            "build",
            "dirs.proj",
            "-c",
            "Debug",
            "--no-restore",
            "-p:RestoreLockedMode=true",
        ]
    )
    assert calls.index(["dotnet", "tool", "restore"]) < restore < build
    expected = [
        [
            "dotnet",
            "test",
            "--project",
            str(projects[name]),
            "-c",
            "Debug",
            "--no-build",
            "-p:RestoreLockedMode=true",
        ]
        for name in ("mstest", "xunit-mtp")
    ]
    expected.append(
        [
            "dotnet",
            "vstest",
            str(projects["vstest"].parent / "bin/Debug/net10.0/vstest.dll"),
        ]
    )
    assert {tuple(command) for command in calls[build + 1 :]} == {
        tuple(command) for command in expected
    }


@pytest.mark.parametrize("command", ["test", "vstest"])
def test_dotnet_check_propagates_test_failure(
    workflow: dict[str, Any],
    tmp_path: Path,
    command: str,
) -> None:
    """Keep failed MTP and VSTest native commands visible as failed steps."""
    _dotnet_projects(tmp_path)
    env = _commands(tmp_path)
    env["FAIL_COMMAND"] = json.dumps(["dotnet", command])
    result = _run_dotnet(workflow, tmp_path, env)
    # The Actions PowerShell -Command wrapper maps failed script exits to 1.
    assert result.returncode == 1, result.stderr
    assert _observations(env)[-1]["command"][:2] == ["dotnet", command]


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
        if name == "scope":
            continue
        assert job["needs"] == "scope"
        assert job["if"] == "always()"
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
            | {"needs.scope.result": selection, key: applicable},
            workflow=workflow,
            job=job,
        )
        assert (result.returncode == 0) is (run is not None), result.stderr
        if run is not None:
            assert output.read_text().strip() == f"run={run}"
        else:
            assert not output.exists()
        for step in job["steps"][1:]:
            assert "steps.scope.outputs.run == 'true'" in step["if"]
            assert not step.get("continue-on-error", False)


def test_python_test_entry_runs_only_selected_roots_and_propagates_failure(
    workflow, tmp_path
):
    """Execute the embedded entry point with passing and failing roots."""
    job = workflow["jobs"]["python-tests"]
    step = next(item for item in job["steps"] if item["name"] == "Run tests")
    executable(
        tmp_path / "bin" / "uv",
        "import os, sys\n"
        "assert sys.argv[1:5] == ['run', '--frozen', "
        "'--all-packages', 'python']\n"
        "os.execv(sys.executable, [sys.executable, *sys.argv[5:]])\n",
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
