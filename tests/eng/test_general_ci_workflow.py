"""General-CI consumers and executable command boundaries."""

from __future__ import annotations

import json
import os
import re
import shutil
import subprocess
import tomllib
from pathlib import Path, PurePosixPath
from typing import Any

import pytest
import yaml

from .ci_workflow_shell import executable, resolve, run_step

REPO_ROOT = Path(__file__).resolve().parents[2]
CHILD_FAILURE = 73
RETIRED_BUDDY_WORKFLOWS = ("buddy", "release-buddy", "legacy-buddy")
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
        "disabled_tools": os.environ.get("MISE_DISABLE_TOOLS"),
    }) + "\n")
failure = json.loads(os.environ.get("FAIL_COMMAND", "[]"))
if failure and command[:len(failure)] == failure:
    sys.exit(73)
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


def _locked(tool: str) -> str:
    entries = tomllib.loads((REPO_ROOT / "mise.lock").read_text())["tools"]
    (version,) = {entry["version"] for entry in entries[tool]}
    return version


def _committed_versions(job: dict[str, Any]) -> dict[str, str]:
    """Run the job's version reader against the checked-out root."""
    step = next(step for step in job["steps"] if step.get("id") == "tools")
    result = run_step(
        {"run": step["run"].replace('"$GITHUB_OUTPUT"', "/dev/stdout")},
        cwd=REPO_ROOT,
        env={},
        bindings={},
    )
    assert result.returncode == 0, result.stderr
    return dict(line.split("=", 1) for line in result.stdout.splitlines())


def _setup_input(
    job: dict[str, Any], action: str, key: str, versions: dict[str, str]
) -> str:
    value = _action(job, action)["with"][key]
    return resolve(
        value, {f"steps.tools.outputs.{k}": v for k, v in versions.items()}
    )


def _required_step(scope: dict[str, Any]) -> None:
    """These bodies rely on the ordinary success dependency, not a scheduler."""
    assert scope.get("if", "success()") in (
        True,
        "true",
        "success()",
        "${{ success() }}",
        "success() && !cancelled() && steps.scope.outputs.run == 'true'",
    )
    assert scope.get("continue-on-error", False) is False


def _python_retention_step(step: dict[str, Any]) -> None:
    if step["with"]["name"] == "python-release-preparation":
        assert step["if"] == (
            "always() && steps.plan.outputs.native_preparation == 'true'"
        )
        assert step["with"]["path"] == (
            "${{ steps.endpoints.outputs.candidate_directory }}"
            "/artifacts/python-release-preparation/"
        )
        assert step["with"]["if-no-files-found"] == "error"
    elif step["with"]["name"] == "ruby-release-preparation":
        assert step["if"] == (
            "always() && steps.plan.outputs.native_ruby_preparation == 'true'"
        )
        assert step["with"]["path"].splitlines() == [
            (
                "${{ steps.endpoints.outputs.candidate_directory }}"
                "/artifacts/ruby-release-preparation/"
            ),
            (
                "${{ steps.endpoints.outputs.candidate_directory }}"
                "/tests/private/app/workflow-delivery/bin/**/"
                "ruby-release-preparation-tests1-native/"
            ),
        ]
        assert step["with"]["if-no-files-found"] == "error"
    else:
        assert step["if"] == "always()"
    assert step["uses"].startswith("actions/upload-artifact@")
    assert not step.get("continue-on-error", False)


def _required_context_step(key: str, step: dict[str, Any]) -> None:
    if key == "native-preparation-tests":
        conditions = {
            "Setup selected native tools": (
                "steps.plan.outputs.python == 'true' || "
                "steps.plan.outputs.ruby == 'true'"
            ),
            "Execute required original Python preparation scenario": (
                "steps.plan.outputs.python == 'true'"
            ),
            "Execute original Ruby preparation scenario": (
                "${{ !cancelled() && steps.plan.outputs.ruby == 'true' }}"
            ),
            "Retain original preparation plans, reports and packages": (
                "always()"
            ),
            "Reject canceled qualification": "cancelled()",
        }
        assert step.get("if", "success()") == conditions.get(
            step["name"], "success()"
        )
        assert not step.get("continue-on-error", False)
    elif key == "validation":
        assert step["if"] in {"always()", "cancelled()"}
        assert not step.get("continue-on-error", False)
    elif key == "ruby-group" and step["name"] in {
        "Install system dependencies",
        "Ensure fd is available",
    }:
        assert step["if"] == (
            "success() && steps.plan.outputs.latexmath == 'true'"
        )
        assert not step.get("continue-on-error", False)
    elif (
        key == "node-tests"
        and step["name"] == "Prepare selected Hexo native bundle"
    ):
        assert step["if"] == (
            "success() && !cancelled() && steps.scope.outputs.run == 'true' && "
            "contains(fromJSON(needs.node-group.outputs.retained_directories), "
            "'src/public/lib/hexo-renderer-asciidoc')"
        )
        assert not step.get("continue-on-error", False)
    elif (key == "scope" and step["name"] == "Setup basis resource SDK") or (
        key == "python-tests"
        and step["name"] == "Setup candidate NBGV and control SDK"
    ):
        output = "materialize" if key == "scope" else "endpoints"
        assert step["if"] == f"steps.{output}.outputs.basis_separate == 'true'"
        assert step["uses"] == "actions/setup-dotnet@v6"
        assert step["with"]["global-json-file"] == (
            "${{ steps.materialize.outputs.basis_directory }}/global.json"
            if key == "scope"
            else "${{ steps.endpoints.outputs.candidate_directory }}"
            "/global.json"
        )
        assert not step.get("continue-on-error", False)
    elif key == "python-tests" and "Retain" in step["name"]:
        _python_retention_step(step)
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
        "needs.node-group.outputs.retained_directories": json.dumps(
            NODE_RETAINED
        ),
        "needs.node-group.outputs.adopted_directory": NODE_ADOPTED,
        "runner.temp": str(tmp_path / "runner-state"),
    }


def _commands(tmp_path: Path) -> dict[str, str]:
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
        executable(tools / name, COMMAND_RECORDER)
    bash = shutil.which("bash")
    assert bash is not None
    (tools / "bash").symlink_to(bash)
    return {
        "PATH": str(tools),
        "COMMAND_LOG": str(tmp_path / "commands.jsonl"),
        "GITHUB_ENV": str(tmp_path / "github-env"),
        "GITHUB_OUTPUT": str(tmp_path / "github-output"),
        "GITHUB_EVENT_NAME": "pull_request",
        "RUNNER_TEMP": str(tmp_path / "runner-state"),
        "GITHUB_WORKSPACE": str(tmp_path),
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
    group = jobs["node-group"]
    assert group["env"]["MISE_EXEC_AUTO_INSTALL"] == "0"
    assert set(
        _action(group, "jdx/mise-action")["with"]["install_args"].split()
    ) == {"dotnet", "node", "pnpm"}
    entry = next(
        step["run"] for step in group["steps"] if step.get("id") == "execute"
    )
    assert "uv " not in entry
    assert "python" not in entry
    assert 'ci caller-node plan "$state" "$scope"' in entry
    contexts = {job["name"]: key for key, job in jobs.items()}
    node = jobs["node-tests"]
    for version in node["strategy"]["matrix"]["node-version"]:
        contexts[resolve(node["name"], {"matrix.node-version": version})] = (
            "node-tests"
        )
    required = {
        "Validate": "validation",
        "Build & Test (.NET 10)": "dotnet-tests",
        "Test (Python)": "python-tests",
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
                [
                    "conformance",
                    "scholarly-tests",
                    "nuget-native-preparation",
                    "native-preparation-tests",
                ]
                if key == "validation"
                else ["scope", "ruby-group"]
                if key == "node-group"
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


def test_validation_installs_locked_tools_before_bootstrap_and_hk(
    workflow: dict[str, Any],
    tmp_path: Path,
) -> None:
    """Locked mise tools install before bootstrap and HK, without relabels."""
    env = _commands(tmp_path)
    result = _run_validation(workflow, tmp_path, env)
    assert result.returncode == 0, result.stderr
    calls = [item["command"] for item in _observations(env)]
    assert not any(command[:2] == ["mise", "link"] for command in calls)
    install = calls.index(["mise", "install", "--locked"])
    bootstrap = calls.index(["mise", "bootstrap"])
    hk = next(index for index, call in enumerate(calls) if call[0] == "hk")
    assert calls[hk][:8] == [
        "hk",
        "--profile",
        "small",
        "--profile",
        "medium",
        "--profile",
        "large",
        "check",
    ]
    assert install < bootstrap < hk
    for item in _observations(env):
        if item["command"][0] in {"mise", "hk"}:
            disabled = item["disabled_tools"].split(",")
            assert "gopass" in disabled
            # The locked mise Pkl serves HK; no second Pkl setup exists.
            assert "pkl" not in disabled


def test_validation_setup_runtimes_follow_committed_versions(
    workflow: dict[str, Any],
) -> None:
    """Setup actions receive the root mise.lock selection, read once."""
    job = workflow["jobs"]["conformance"]
    assert not any(
        step.get("uses", "").startswith("pkl-community/setup-pkl@")
        for step in job["steps"]
    )
    versions = _committed_versions(job)
    assert _setup_input(job, "pnpm/action-setup", "version", versions) == (
        _locked("pnpm")
    )
    assert _setup_input(
        job, "actions/setup-node", "node-version", versions
    ) == (_locked("node"))
    assert _setup_input(job, "ruby/setup-ruby", "ruby-version", versions) == (
        _locked("ruby")
    )


@pytest.mark.parametrize("failure", ["install", "bootstrap"])
def test_validation_bootstrap_failure_stops_hk(
    workflow: dict[str, Any],
    tmp_path: Path,
    failure: str,
) -> None:
    """Keep validation blocked when tool installation or bootstrap fails."""
    env = _commands(tmp_path)
    env["FAIL_COMMAND"] = json.dumps(["mise", failure])
    result = _run_validation(workflow, tmp_path, env)
    assert result.returncode == CHILD_FAILURE
    calls = [item["command"] for item in _observations(env)]
    assert not any(command[0] == "hk" for command in calls)
    if failure == "install":
        assert ["mise", "bootstrap"] not in calls


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
        hk = command
    else:
        assert command[1] == "eng/scripts/workflow_delivery_v3_hk.py"
        assert command[command.index("--from-ref") + 1] == "a" * 40
        assert command[command.index("--to-ref") + 1] == "d" * 40
        hk = command[command.index("--") + 1 :]
    assert hk == [
        "hk",
        "--profile",
        "small",
        "--profile",
        "medium",
        "--profile",
        "large",
        "check",
        "--check",
        "--no-stage",
        "--no-progress",
        "--no-fail-fast",
        *(["--all"] if full else []),
    ]
    assert {"small", "medium", "large"} <= set(observed["profile"].split(","))


def _retained_command(tmp_path: Path) -> list[str]:
    return [
        "dotnet",
        str(
            tmp_path
            / "runner-state/retained-control/control/WorkflowDelivery.dll"
        ),
        "ci",
        "caller-node",
        "retained",
        str(tmp_path),
        str(tmp_path / "runner-state/retained-node"),
        json.dumps(NODE_RETAINED),
        NODE_ADOPTED,
        NODE_PACKAGE_DIR,
        str(tmp_path / "setup tools/pnpm"),
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
        ("dotnet",),
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
    assert job["needs"] == ["scope", "ruby-group"]
    assert job["if"] == "${{ !cancelled() }}"
    assert "needs.scope.outputs.node" not in json.dumps(job)
    upload = _action(workflow["jobs"]["scope"], "actions/upload-artifact")
    assert upload["id"] == "retain"
    assert workflow["jobs"]["scope"]["outputs"]["artifact_id"] == (
        "${{ steps.retain.outputs.artifact-id }}"
    )
    download = next(
        step
        for step in job["steps"]
        if step.get("with", {}).get("path") == "artifacts/node-scope"
    )
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
            "needs.ruby-group.result": "success",
            "needs.ruby-group.outputs.node_artifact_id": "1154",
        },
        workflow=workflow,
        job=job,
    )
    assert (result.returncode == 0) is succeeds, result.stderr
    checkout = _action(job, "actions/checkout")
    assert checkout["with"]["persist-credentials"] is False
    assert checkout["with"]["fetch-depth"] == 0
    assert job["permissions"] == {"contents": "read", "actions": "read"}
    assert not [
        step for step in job["steps"] if "GH_TOKEN" in step.get("env", {})
    ]


def test_python_check_has_consumed_toolchain_prerequisites(workflow):
    """Committed SDKs and query tools precede selected adapter preparation."""
    job = workflow["jobs"]["python-tests"]
    steps = job["steps"]
    materialize = next(step for step in steps if step.get("id") == "endpoints")
    plan = next(step for step in steps if step.get("id") == "plan")
    sdks = [
        step
        for step in steps
        if step.get("uses", "").startswith("actions/setup-dotnet@")
    ]
    control_sdk, basis_sdk, candidate_sdk = sdks
    assert steps.index(control_sdk) < steps.index(materialize)
    assert control_sdk["with"]["global-json-file"] == "global.json"
    assert all(
        steps.index(materialize) < steps.index(sdk) < steps.index(plan)
        for sdk in (basis_sdk, candidate_sdk)
    )
    assert (
        basis_sdk["with"]["global-json-file"]
        == "${{ steps.endpoints.outputs.basis_directory }}/global.json"
    )
    assert (
        candidate_sdk["with"]["global-json-file"]
        == "${{ steps.endpoints.outputs.candidate_directory }}/global.json"
    )
    assert (
        candidate_sdk["if"]
        == "steps.endpoints.outputs.basis_separate == 'true'"
    )
    checkout = _action(job, "actions/checkout")
    assert checkout["with"]["fetch-depth"] == 0
    assert checkout["with"]["persist-credentials"] is False
    python = _action(job, "actions/setup-python")
    uv = _action(job, "astral-sh/setup-uv")
    assert steps.index(python) < steps.index(materialize)
    assert steps.index(uv) < steps.index(plan)
    assert python["with"]["python-version-file"] == ".python-version"
    assert "python-version" not in uv["with"]
    assert (
        tomllib.loads((REPO_ROOT / uv["with"]["version-file"]).read_text())[
            "required-version"
        ]
        == "=="
        + tomllib.loads((REPO_ROOT / "mise.lock").read_text())["tools"]["uv"][
            0
        ]["version"]
    )
    assert not any(
        step.get("uses", "").startswith(
            ("ruby/setup-ruby@", "jdx/mise-action@")
        )
        for step in steps
    )
    execute = next(
        step
        for step in steps
        if "ci caller-python execute" in step.get("run", "")
    )
    assert steps.index(plan) < steps.index(execute)


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
    executable(Path(env["PATH"]) / "jq", COMMAND_RECORDER + "\nprint(0)\n")
    return env


def _scope_endpoint_commands(
    tmp_path: Path, line_ending: str
) -> tuple[dict[str, str], list[Path]]:
    env = _commands(tmp_path)
    tools = Path(env["PATH"])
    for name in ("mkdir", "basename"):
        command = shutil.which(name)
        assert command is not None
        (tools / name).symlink_to(command)
    endpoints = [tmp_path / "basis \\native", tmp_path / "candidate checkout"]
    for endpoint in endpoints:
        endpoint.mkdir()
    env.update(
        NATIVE_ENDPOINTS=json.dumps([str(path) for path in endpoints]),
        NATIVE_LINE_ENDING=line_ending,
    )
    executable(
        tools / "jq",
        COMMAND_RECORDER
        + r"""
if os.environ.get("JQ_EMIT_ROWS", "true") == "true":
    ending = os.environ["NATIVE_LINE_ENDING"]
    for endpoint in json.loads(os.environ["NATIVE_ENDPOINTS"]):
        sys.stdout.buffer.write((endpoint + ending).encode("utf-8"))
    sys.stdout.buffer.flush()
sys.exit(int(os.environ.get("JQ_EXIT", "0")))
""",
    )
    return env, endpoints


def _scope_endpoint_step(workflow: dict[str, Any]) -> dict[str, Any]:
    return next(
        step
        for step in workflow["jobs"]["scope"]["steps"]
        if step.get("id") == "dotnet-group"
    )


@pytest.mark.parametrize("line_ending", ["\n", "\r\n"], ids=["lf", "crlf"])
def test_scope_prepares_exact_native_endpoints_from_native_line_records(
    workflow: dict[str, Any], tmp_path: Path, line_ending: str
) -> None:
    """Preserve exact native endpoint paths across shell setup."""
    env, endpoints = _scope_endpoint_commands(tmp_path, line_ending)
    result = run_step(
        _scope_endpoint_step(workflow),
        cwd=tmp_path,
        env=env,
        bindings=_bindings(tmp_path),
        workflow=workflow,
        job=workflow["jobs"]["scope"],
    )
    assert result.returncode == 0, result.stderr
    observations = [
        json.loads(line)
        for line in Path(env["COMMAND_LOG"]).read_text().splitlines()
    ]
    native = [row for row in observations if row["command"][0] == "mise"]
    assert {Path(row["cwd"]) for row in native} == set(endpoints)
    for endpoint in endpoints:
        commands = [
            row["command"] for row in native if row["cwd"] == str(endpoint)
        ]
        assert commands[:2] == [
            ["mise", "install", "--locked", "dotnet", "node", "pnpm", "pkl"],
            [
                "mise",
                "exec",
                "--locked",
                "dotnet",
                "--",
                "dotnet",
                "tool",
                "restore",
            ],
        ]
        assert commands[2][:8] == [
            "mise",
            "exec",
            "--locked",
            "dotnet",
            "--",
            "dotnet",
            "restore",
            "dirs.proj",
        ]
        assert "-property:RestoreLockedMode=true" in commands[2]
    assert observations[-1]["command"][2:5] == [
        "ci",
        "caller-dotnet",
        "prepare",
    ]
    assert observations[-1]["cwd"] == str(tmp_path)


@pytest.mark.parametrize(
    "failed_command",
    [
        ["mise", "install"],
        [
            "mise",
            "exec",
            "--locked",
            "dotnet",
            "--",
            "dotnet",
            "tool",
            "restore",
        ],
        ["mise", "exec", "--locked", "dotnet", "--", "dotnet", "restore"],
    ],
    ids=["install", "tool-restore", "graph-restore"],
)
def test_scope_endpoint_native_failure_blocks_dependent_preparation(
    workflow: dict[str, Any],
    tmp_path: Path,
    failed_command: list[str],
) -> None:
    """Keep every native prerequisite fatal before dependent preparation.

    Endpoint-record line endings are covered by the line-record test above.
    """
    env, endpoints = _scope_endpoint_commands(tmp_path, "\n")
    env["FAIL_COMMAND"] = json.dumps(failed_command)
    result = run_step(
        _scope_endpoint_step(workflow),
        cwd=tmp_path,
        env=env,
        bindings=_bindings(tmp_path),
        workflow=workflow,
        job=workflow["jobs"]["scope"],
    )
    assert result.returncode == CHILD_FAILURE
    observations = [
        json.loads(line)
        for line in Path(env["COMMAND_LOG"]).read_text().splitlines()
    ]
    assert observations[-1]["command"][: len(failed_command)] == failed_command
    assert all(row["command"][0] != "dotnet" for row in observations)
    assert {Path(row["cwd"]) for row in observations[1:]} == {endpoints[0]}


@pytest.mark.parametrize(
    "emit_rows", ["false", "true"], ids=["empty", "partial"]
)
def test_scope_failed_endpoint_producer_blocks_all_preparation(
    workflow: dict[str, Any], tmp_path: Path, emit_rows: str
) -> None:
    """Reject unsuccessful extraction even when it emits partial rows."""
    env, _ = _scope_endpoint_commands(tmp_path, "\r\n")
    env.update(JQ_EXIT=str(CHILD_FAILURE), JQ_EMIT_ROWS=emit_rows)
    result = run_step(
        _scope_endpoint_step(workflow),
        cwd=tmp_path,
        env=env,
        bindings=_bindings(tmp_path),
        workflow=workflow,
        job=workflow["jobs"]["scope"],
    )
    assert result.returncode == CHILD_FAILURE
    observations = _observations(env)
    assert [row["command"][0] for row in observations] == ["jq"]


def test_scope_unwritable_endpoint_scratch_blocks_all_preparation(
    workflow: dict[str, Any], tmp_path: Path
) -> None:
    """Do not start native work when the owned extraction file cannot open."""
    env, _ = _scope_endpoint_commands(tmp_path, "\r\n")
    scratch = Path(env["RUNNER_TEMP"]) / "dotnet-group/endpoint-preparation"
    (scratch / "directories.txt").mkdir(parents=True)
    result = run_step(
        _scope_endpoint_step(workflow),
        cwd=tmp_path,
        env=env,
        bindings=_bindings(tmp_path),
        workflow=workflow,
        job=workflow["jobs"]["scope"],
    )
    assert result.returncode != 0
    assert _observations(env) == []


def _dotnet_receiver_command(tmp_path: Path) -> list[str]:
    temporary = tmp_path / "runner-state"
    return [
        "dotnet",
        str(temporary / "dotnet-plan-transfer/control/WorkflowDelivery.dll"),
        "ci",
        "caller-dotnet",
        "execute",
        str(tmp_path),
        str(temporary / "dotnet-plan-transfer"),
        str(temporary / "dotnet-execution"),
        "a" * 40,
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
    assert producer["shell"] == "pwsh"
    assert "Bootstrap-CiControl.ps1" in producer["run"]
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
    assert not [
        step for step in job["steps"] if "GH_TOKEN" in step.get("env", {})
    ]
    execute = next(
        step for step in job["steps"] if " execute " in step.get("run", "")
    )
    assert scope["steps"].index(producer) < scope["steps"].index(upload)
    assert job["steps"].index(download) < job["steps"].index(execute)
    env = _dotnet_commands(tmp_path)
    result = _run_dotnet(workflow, tmp_path, env)
    assert result.returncode == 0, result.stderr
    calls = [item["command"] for item in _observations(env)]
    assert calls[-1] == _dotnet_receiver_command(tmp_path)
    assert not any("caller-artifact" in call for call in calls)


def test_dotnet_check_propagates_required_command_failure(
    workflow: dict[str, Any],
    tmp_path: Path,
) -> None:
    """Propagate the receiver's failure."""
    env = _dotnet_commands(tmp_path)
    env["FAIL_COMMAND"] = json.dumps(_dotnet_receiver_command(tmp_path))
    result = _run_dotnet(workflow, tmp_path, env)
    assert result.returncode == CHILD_FAILURE, result.stderr
    calls = [item["command"] for item in _observations(env)]
    assert calls[-1] == _dotnet_receiver_command(tmp_path)


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
    else:
        calls = [item["command"] for item in _observations(env)]
        assert calls[-1] == _dotnet_receiver_command(tmp_path)


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
        if name in {
            "scope",
            "validation",
            "node-group",
            "ruby-group",
            "dotnet-tests",
            "python-tests",
            "native-preparation-tests",
        }:
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
        "nuget-native-preparation",
        "native-preparation-tests",
    ]
    assert job["if"] == "always()"
    step = job["steps"][0]
    assert step["if"] == "always()"
    assert not step.get("continue-on-error", False)
    successful = {
        "needs.conformance.result": "success",
        "needs.scholarly-tests.result": "success",
        "needs.nuget-native-preparation.result": "success",
        "needs.native-preparation-tests.result": "success",
    }
    scenarios = [successful]
    for dependency in (
        "conformance",
        "scholarly-tests",
        "nuget-native-preparation",
        "native-preparation-tests",
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


def _cancelable_dotnet_step(step):
    """Preserve required native result retention and failure handling."""
    if "Retain" in step["name"]:
        assert step["if"] == "always()"
        assert step["uses"].startswith("actions/upload-artifact@")
        assert not step.get("continue-on-error", False)
    else:
        _required_step(step)


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
                _cancelable_dotnet_step(step)
            elif name == "python-tests":
                _required_context_step(name, step)
            elif name in {
                "node-group",
                "ruby-group",
                "native-preparation-tests",
            }:
                if name != "native-preparation-tests":
                    assert step.get("if", "success()").split(" && ")[0] in {
                        "success()",
                        "always()",
                    }
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


@pytest.mark.parametrize("failure", [False, True])
def test_scope_selection_runs_once_in_the_control_application(
    workflow, tmp_path, failure
):
    """Select from native facts in one process and propagate its failure."""
    job = workflow["jobs"]["scope"]
    assert not any(
        step.get("uses", "").startswith(
            ("actions/setup-python@", "astral-sh/setup-uv@")
        )
        for step in job["steps"]
    )
    assert "PYTHONUTF8" not in job.get("env", {})
    assert not any(
        "python" in step.get("run", "") or "uv " in step.get("run", "")
        for step in job["steps"]
    )
    select = next(step for step in job["steps"] if step.get("id") == "select")
    env = _commands(tmp_path)
    request = str(tmp_path / "runner temp/ci-control-inputs/request.json")
    selector = [
        "dotnet",
        _bindings(tmp_path)["steps.control.outputs.application"],
        "ci",
        "caller-dotnet",
        "select",
        str(tmp_path),
        _bindings(tmp_path)["steps.dotnet-group.outputs.request"],
        request,
        _bindings(tmp_path)["steps.control.outputs.transfer"],
        "artifacts/ci-scope.json",
    ]
    if failure:
        env["FAIL_COMMAND"] = json.dumps(selector)
    result = run_step(
        select,
        cwd=tmp_path,
        env=env,
        bindings=_bindings(tmp_path)
        | {"steps.materialize.outputs.request": request},
        workflow=workflow,
        job=job,
    )
    assert result.returncode == (CHILD_FAILURE if failure else 0), result.stderr
    assert [item["command"] for item in _observations(env)] == [selector]


@pytest.mark.parametrize(
    "scenario",
    [
        ("success", "10", "11", True),
        ("failure", "10", "11", False),
        ("cancelled", "10", "11", False),
        ("success", "", "11", False),
        ("success", "10", "", False),
    ],
)
def test_python_group_requires_native_comparison_not_legacy_flags(
    workflow, tmp_path, scenario
):
    """Every successful transfer enters Linux analysis without old flags."""
    selection, scope, control, passed = scenario
    job = workflow["jobs"]["python-tests"]
    steps = job["steps"]
    contract = steps[0]
    result = run_step(
        contract,
        cwd=tmp_path,
        env={},
        bindings=_bindings(tmp_path)
        | {
            "needs.scope.result": selection,
            "needs.scope.outputs.artifact_id": scope,
            "needs.scope.outputs.dotnet_artifact_id": control,
        },
        workflow=workflow,
        job=job,
    )
    assert (result.returncode == 0) is passed
    assert "APPLICABLE" not in contract["env"]
    materialize = next(
        i for i, step in enumerate(steps) if step.get("id") == "endpoints"
    )
    sdks = [
        i
        for i, step in enumerate(steps)
        if step.get("uses", "").startswith("actions/setup-dotnet@")
    ]
    assert sdks
    assert sdks[0] < materialize
    assert all(materialize < i for i in sdks[1:])
    assert all(
        "steps.endpoints.outputs." in steps[i]["with"]["global-json-file"]
        for i in sdks[1:]
    )
    plan = next(step for step in steps if step.get("id") == "plan")
    assert "ci caller-python plan " in plan["run"]
    assert "if" not in plan
    receivers = [
        step
        for step in steps
        if step.get("uses", "").startswith("actions/download-artifact@")
    ]
    assert {step["with"]["artifact-ids"] for step in receivers} == {
        "${{ needs.scope.outputs.artifact_id }}",
        "${{ needs.scope.outputs.dotnet_artifact_id }}",
    }
    assert all(
        "name" not in step["with"] and "pattern" not in step["with"]
        for step in receivers
    )
    assert job["permissions"] == {"contents": "read", "actions": "read"}


@pytest.mark.parametrize(
    ("selection", "scope", "control", "succeeds"),
    [
        ("success", "11", "12", True),
        ("failure", "11", "12", False),
        ("success", "", "12", False),
        ("success", "11", "", False),
    ],
)
def test_native_ruby_scope_and_control_guard(  # noqa: PLR0913, PLR0917 - Guard matrix.
    workflow, tmp_path, selection, scope, control, succeeds
):
    """Lost scope or candidate control stops before native preparation."""
    job = workflow["jobs"]["ruby-group"]
    result = run_step(
        job["steps"][0],
        cwd=tmp_path,
        env=os.environ.copy(),
        bindings={
            "needs.scope.result": selection,
            "needs.scope.outputs.artifact_id": scope,
            "needs.scope.outputs.dotnet_artifact_id": control,
        },
        workflow=workflow,
        job=job,
    )
    assert (result.returncode == 0) is succeeds


def test_native_ruby_transfers_original_control_and_complete_plan(workflow):
    """One receiver and plan feed the sole native Ruby PR owner."""
    job = workflow["jobs"]["ruby-group"]
    assert job["needs"] == "scope"
    assert job["permissions"] == {"contents": "read", "actions": "read"}
    assert "environment" not in job
    runs = [step["run"] for step in job["steps"] if "run" in step]
    entry = [run for run in runs if "ci caller-ruby" in run]
    assert len(entry) == 3
    assert " materialize " in entry[0]
    assert "artifact.json" not in json.dumps(job)
    assert "gh api" not in json.dumps(job)
    assert " plan " in entry[1]
    assert " execute " in entry[2]
    assert all('"$RUNNER_TEMP/ruby-group"' in run for run in entry)
    assert all("continue-on-error" not in step for step in job["steps"])
    assert "ruby-tests" not in workflow["jobs"]
    assert not (
        REPO_ROOT / ".github/workflows/workflow-delivery-v3-ruby-ci.yml"
    ).exists()


def test_native_ruby_retains_failed_outcomes_and_uses_native_runtime(workflow):
    """Native runtime and failure carriers remain visible."""
    job = workflow["jobs"]["ruby-group"]
    versions = _committed_versions(job)
    assert _setup_input(job, "ruby/setup-ruby", "ruby-version", versions) == (
        _locked("ruby")
    )
    assert not any(
        step.get("uses", "").startswith("actions/setup-python@")
        for step in job["steps"]
    )
    retained = next(
        step
        for step in job["steps"]
        if step.get("with", {}).get("name") == "ruby-group-results"
    )
    assert retained["if"] == "always()"
    assert retained["with"]["name"] == "ruby-group-results"
    assert "*.json" in retained["with"]["path"]
    assert "*.stderr" in retained["with"]["path"]
    assert job["steps"][-1]["if"] == "cancelled()"


@pytest.mark.parametrize(
    ("ruby_result", "artifact", "succeeds"),
    [
        ("success", "19", True),
        ("failure", "19", False),
        ("skipped", "19", False),
        ("success", "", False),
        ("success", "0", False),
    ],
)
def test_node_receives_only_successful_original_ruby_artifact(
    workflow, tmp_path, ruby_result, artifact, succeeds
):
    """Require original Ruby facts before Node product execution."""
    ruby = workflow["jobs"]["ruby-group"]
    node = workflow["jobs"]["node-group"]
    transfer = next(
        step for step in ruby["steps"] if step.get("id") == "node-facts"
    )
    assert transfer.get("if", "success()") == "success()"
    assert transfer["with"] == {
        "name": "ruby-node-inputs",
        "path": "${{ runner.temp }}/ruby-group/node-inputs.json",
        "if-no-files-found": "error",
        "retention-days": 7,
    }
    assert (
        ruby["outputs"]["node_artifact_id"]
        == "${{ steps.node-facts.outputs.artifact-id }}"
    )
    receive = next(
        step
        for step in node["steps"]
        if step.get("with", {}).get("path") == "artifacts/node-ruby"
    )
    assert (
        receive["with"]["artifact-ids"]
        == "${{ needs.ruby-group.outputs.node_artifact_id }}"
    )
    assert "name" not in receive["with"]
    assert "pattern" not in receive["with"]
    entry = next(
        step["run"] for step in node["steps"] if step.get("id") == "execute"
    )
    assert '"$GITHUB_WORKSPACE/artifacts/node-ruby/node-inputs.json"' in entry
    assert "caller-artifact" not in entry
    assert "run_ruby_ci_group.py" not in json.dumps(node)
    result = run_step(
        node["steps"][0],
        cwd=tmp_path,
        env={},
        bindings={
            "needs.scope.result": "success",
            "needs.scope.outputs.artifact_id": "17",
            "needs.ruby-group.result": ruby_result,
            "needs.ruby-group.outputs.node_artifact_id": artifact,
        },
        workflow=workflow,
        job=node,
    )
    assert (result.returncode == 0) is succeeds


def test_retained_hexo_prepares_only_its_selected_native_bundle(workflow):
    """Bundler owns the locked environment for selected Hexo checks."""
    job = workflow["jobs"]["node-tests"]
    ruby = _action(job, "ruby/setup-ruby")
    assert "steps.scope.outputs.run == 'true'" in ruby["if"]
    assert (
        "contains(fromJSON(needs.node-group.outputs.retained_directories), "
        "'src/public/lib/hexo-renderer-asciidoc')" in ruby["if"]
    )
    assert ruby["with"] == {
        "ruby-version": "$" + "{{ steps.tools.outputs.ruby }}",
        "working-directory": NODE_PACKAGE_DIR,
        "bundler-cache": True,
    }
    versions = _committed_versions(job)
    assert _setup_input(job, "ruby/setup-ruby", "ruby-version", versions) == (
        _locked("ruby")
    )
    assert _setup_input(job, "pnpm/action-setup", "version", versions) == (
        _locked("pnpm")
    )


CONTROL_PROJECT = (
    "tests/private/app/workflow-delivery/WorkflowDelivery.Tests.csproj"
)


def _step_using(job: dict[str, Any], action: str) -> dict[str, Any]:
    return next(
        step for step in job["steps"] if step.get("uses", "").startswith(action)
    )


def _native_test(job: dict[str, Any]) -> str:
    step = next(
        step for step in job["steps"] if "dotnet test" in step.get("run", "")
    )
    assert step["shell"] == "pwsh"
    return step["run"]


def test_nuget_native_preparation_runs_selected_rows(workflow):
    """Per-PR CI runs only the two native NuGet preparation rows."""
    job = workflow["jobs"]["nuget-native-preparation"]
    assert job["name"] == "NuGet native preparation (Windows)"
    assert job["runs-on"].startswith("windows-")
    assert job["steps"][0]["env"]["APPLICABLE"] == (
        "${{ needs.scope.outputs.nuget_native_preparation }}"
    )
    assert workflow["jobs"]["scope"]["outputs"]["nuget_native_preparation"] == (
        "${{ steps.select.outputs.nuget_native_preparation }}"
    )
    assert "nuget-native-preparation" in workflow["jobs"]["validation"]["needs"]
    checkout = _step_using(job, "actions/checkout@")
    assert checkout["with"]["persist-credentials"] is False
    run = _native_test(job)
    assert CONTROL_PROJECT in run
    assert "--filter 'TestCategory=NuGetNativePreparation'" in run
    assert "-- --minimum-expected-tests 2" in run
    assert "--report-trx" in run
    assert "native-preparation.binlog" in run
    assert "exit $LASTEXITCODE" in run
    upload = _step_using(job, "actions/upload-artifact@")
    assert upload["if"] == "always() && steps.scope.outputs.run == 'true'"
    assert upload["with"]["name"] == "nuget-native-preparation-windows"
    assert upload["with"]["path"] == "artifacts/nuget-native-preparation"
    assert upload["with"]["if-no-files-found"] == "error"
    assert upload["with"]["retention-days"] == 30


def test_nuget_reproducibility_runs_on_dispatch_and_main_changes(workflow):
    """Ordinary PRs do not re-prove established .NET reproducibility."""
    assert "nuget-reproducibility" not in workflow["jobs"]
    path = (
        REPO_ROOT
        / ".github/workflows/workflow-delivery-v3-nuget-reproducibility.yml"
    )
    repro = yaml.safe_load(path.read_text())
    triggers = repro.get("on", repro.get(True))
    assert set(triggers) == {"workflow_dispatch", "push"}
    assert triggers["push"]["branches"] == ["main"]
    assert {
        "global.json",
        "Directory.Build.props",
        "Directory.Packages.props",
        "tests/Directory.Build.props",
        "nuget.config",
        "version.json",
        ".gitattributes",
        "stylecop.json",
        ".editorconfig",
        "src/private/app/workflow-delivery/Release/NuGet*.cs",
        "src/private/app/workflow-delivery/Destinations/GithubNuGet*.cs",
        (
            "tests/private/app/workflow-delivery/fixtures/products/"
            "hcoona-release-smoke-github-packages/**"
        ),
        ".github/workflows/workflow-delivery-v3-nuget-reproducibility.yml",
    } <= set(triggers["push"]["paths"])
    for pattern in triggers["push"]["paths"]:
        assert any(REPO_ROOT.glob(pattern)), pattern
    assert repro["concurrency"]["cancel-in-progress"] is False
    assert repro["permissions"] == {"contents": "read"}
    job = repro["jobs"]["nuget-reproducibility"]
    assert job["runs-on"].startswith("windows-")
    assert job["timeout-minutes"] == 90
    checkout = _step_using(job, "actions/checkout@")
    assert checkout["with"]["ref"] == "${{ inputs.ref || github.sha }}"
    assert checkout["with"]["fetch-depth"] == 0
    assert checkout["with"]["persist-credentials"] is False
    run = _native_test(job)
    assert CONTROL_PROJECT in run
    assert "--filter 'TestCategory=NuGetNativeScenarios'" in run
    assert "-- --minimum-expected-tests 3" in run
    assert "--report-trx" in run
    assert "native-scenarios.binlog" in run
    assert "exit $LASTEXITCODE" in run
    upload = _step_using(job, "actions/upload-artifact@")
    assert upload["if"] == "always()"
    assert upload["with"]["name"] == "nuget-reproducibility-windows"
    assert upload["with"]["path"] == "artifacts/nuget-reproducibility"
    assert upload["with"]["if-no-files-found"] == "error"
    assert upload["with"]["retention-days"] == 30
    pins = {
        step["uses"]
        for workflow_job in workflow["jobs"].values()
        for step in workflow_job.get("steps", ())
        if "uses" in step
    }
    assert all(step["uses"] in pins for step in job["steps"] if "uses" in step)


def test_retired_buddy_routes_stay_absent_and_v3_buddy_stays_dedicated():
    """Retired v1 Buddy entries have no compatibility route."""
    workflows = REPO_ROOT / ".github/workflows"
    for name in RETIRED_BUDDY_WORKFLOWS:
        for suffix in (".yml", ".yaml"):
            assert not (workflows / (name + suffix)).exists(), name + suffix
    buddy = yaml.safe_load(
        (workflows / "workflow-delivery-v3-buddy-smoke.yml").read_text()
    )
    assert "workflow_dispatch" in buddy.get("on", buddy.get(True))
    assert not any("uses" in job for job in buddy["jobs"].values())


def _hk_check_steps() -> dict[str, Any]:
    mise = shutil.which("mise")
    assert mise is not None
    evaluated = subprocess.run(  # noqa: S603 - Locked native Pkl evaluation.
        [
            mise,
            "exec",
            "--locked",
            "pkl",
            "--",
            "pkl",
            "eval",
            "-f",
            "json",
            "hk.pkl",
        ],
        cwd=REPO_ROOT,
        capture_output=True,
        text=True,
        check=True,
    )
    return json.loads(evaluated.stdout)["hooks"]["check"]["steps"]


def _ci_hk_profiles(workflow: dict[str, Any]) -> set[str]:
    runs = " ".join(
        step.get("run", "") for step in workflow["jobs"]["conformance"]["steps"]
    )
    profiles = set(re.findall(r"--profile (\w+)", runs))
    assert profiles
    return profiles


def test_hk_actionlint_keeps_covering_root_workflows(workflow):
    """CiScope's root-workflow conformance owner copies this HK step glob."""
    step = _hk_check_steps()["actionlint"]
    assert set(step["profiles"]) & _ci_hk_profiles(workflow)
    for name in ("ci.yml", "codeql.yaml"):
        path = PurePosixPath(".github/workflows", name)
        assert any(path.full_match(glob) for glob in step["glob"]), name
        assert not any(path.full_match(glob) for glob in step["exclude"])


# CiScope.LintedShell copies these shellcheck/shfmt exclusions.
COPIED_SHELL_EXCLUSIONS = {
    "**/.github/**",
    "**/.specify/**",
    "**/generated/**",
    ".agents/**",
}


def _can_exclude_shell(pattern: str) -> bool:
    """Report whether an exclusion can match a shell script.

    A final ``**`` matches any file; otherwise the final segment must itself
    match a ``.sh`` or ``.bash`` name.
    """
    last = pattern.rsplit("/", 1)[-1]
    return (
        last == "**"
        or last.endswith((".sh", ".bash"))
        or any(
            PurePosixPath(name).full_match(last) for name in ("x.sh", "x.bash")
        )
    )


@pytest.mark.parametrize(
    ("pattern", "expected"),
    [
        ("**/.github/**", True),
        (".agents/**", True),
        ("**/*.sh", True),
        ("tools/build.bash", True),
        ("**/specs/**/*.md", False),
        ("global.json", False),
        ("**/mise.lock", False),
    ],
)
def test_shell_exclusion_relevance(pattern, expected):
    """Only exclusions whose final segment can name a shell script count."""
    assert _can_exclude_shell(pattern) is expected


def test_hk_shell_linters_keep_covering_copied_scripts(workflow):
    """CiScope's shell conformance owner copies this HK glob and exclusions."""
    steps = _hk_check_steps()
    profiles = _ci_hk_profiles(workflow)
    for name in ("shellcheck", "shfmt"):
        step = steps[name]
        assert set(step["profiles"]) & profiles, name
        for path in ("x.sh", "x.bash", "a/b/x.sh", "a/b/x.bash"):
            assert any(
                PurePosixPath(path).full_match(glob) for glob in step["glob"]
            ), (name, path)
        relevant = {
            pattern
            for pattern in step["exclude"]
            if _can_exclude_shell(pattern)
        }
        assert relevant == COPIED_SHELL_EXCLUSIONS, name
