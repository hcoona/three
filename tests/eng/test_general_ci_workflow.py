"""General-CI consumers and executable command boundaries."""

from __future__ import annotations

import json
import re
import shlex
from pathlib import Path
from typing import TYPE_CHECKING, Any

import pytest
import yaml

from .ci_workflow_shell import (
    COMMAND_RECORDER,
    WorkflowShell,
    action_steps,
    artifact_upload,
    command_log,
    dependencies,
    effective_env,
    executable,
    fixture_path,
    load_script,
    recorded_commands,
    resolve,
    run_step,
)

if TYPE_CHECKING:
    import subprocess

REPO_ROOT = Path(__file__).resolve().parents[2]
CHILD_FAILURE = 73
RETIRED_BUDDY_WORKFLOWS = ("buddy", "release-buddy", "legacy-buddy")
SKIPPED_TEST_SUMMARY = "./eng/scripts/Write-SkippedTestSummary.ps1 -Path "
NODE_PACKAGE_DIR = "src/public/lib/hexo-renderer-asciidoc"
DOTNET_GROUP = "node+npm+python"
NODE_RETAINED = {
    "22.x": [NODE_PACKAGE_DIR, "src/retained"],
    "24.x": [NODE_PACKAGE_DIR, "src/retained"],
    "26.x": [NODE_PACKAGE_DIR, "src/retained"],
    "28.x": [],
}
NODE_ADOPTED = (
    "tests/private/app/workflow-delivery/fixtures/products/"
    "hcoona-release-smoke-npm"
)
NODE_JQ_COMMAND = (
    COMMAND_RECORDER
    + r"""
if "--arg" in sys.argv:
    selector = sys.argv[sys.argv.index("--arg") + 2]
    value = json.loads(os.environ["RETAINED_DIRECTORIES"])[selector]
    print(json.dumps(value, separators=(",", ":")))
elif sys.argv[-1] == "length":
    print(len(json.loads(sys.stdin.read())))
else:
    print("unsupported jq fixture command", file=sys.stderr)
    sys.exit(2)
"""
)
HK_CHECK = shlex.split(
    "hk --profile small --profile medium --profile large check"
)


@pytest.fixture
def workflow() -> dict[str, Any]:
    """Load the current general-CI workflow."""
    return _workflow("ci")


def _workflow(name: str) -> dict[str, Any]:
    path = REPO_ROOT / ".github/workflows" / f"{name}.yml"
    return yaml.safe_load(path.read_text())


def _action(job: dict[str, Any], action: str) -> dict[str, Any]:
    return next(
        step
        for step in job["steps"]
        if step.get("uses", "").split("@", 1)[0] == action
    )


def _step_id(job: dict[str, Any], identifier: str) -> dict[str, Any]:
    return next(s for s in job["steps"] if s.get("id") == identifier)


def _assert_checkout(job, *, ref=None, full_history=False):
    inputs = _action(job, "actions/checkout")["with"]
    assert inputs["persist-credentials"] is False
    if full_history:
        assert inputs["fetch-depth"] == 0
    if ref is not None:
        assert inputs["ref"] == ref


def _scope_inputs(workflow, name, **outputs):
    jobs = workflow["jobs"]
    bindings = {}
    for key, output in outputs.items():
        assert jobs["scope"]["outputs"][output] == (
            f"${{{{ steps.select.outputs.{output} }}}}"
        )
        bindings[key] = f"${{{{ needs.scope.outputs.{output} }}}}"
    assert bindings.items() <= jobs[name]["steps"][0]["env"].items()
    return bindings


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


def _assert_skipped_test_summary(step: dict[str, Any]) -> None:
    """Report skipped test names after an executed test step."""
    assert step["if"].startswith("always() && ")
    assert step["if"].endswith(("outcome != 'skipped'", "outcome == 'success'"))
    assert step["shell"] == "pwsh"
    assert step["run"].startswith(SKIPPED_TEST_SUMMARY)
    assert "uses" not in step
    assert not step.get("continue-on-error", False)


def _bindings(tmp_path: Path) -> dict[str, str]:
    temp = tmp_path / "runner-state"
    group = temp / "dotnet-group"
    control = group / "transfer"
    return {
        "github.workspace": str(tmp_path),
        "github.token": "fixture-token",
        "needs.scope.result": "success",
        "needs.scope.outputs.dotnet_artifact_id": "1054",
        "steps.control.outputs.application": str(
            control / "control/WorkflowDelivery.dll"
        ),
        "steps.control.outputs.transfer": str(control),
        "steps.dotnet-group.outputs.request": str(group / "request.json"),
        "secrets.GITHUB_TOKEN": "fixture-token",
        "github.event.pull_request.base.sha": "a" * 40,
        "github.event.pull_request.head.sha": "b" * 40,
        "github.event.before": "c" * 40,
        "github.sha": "d" * 40,
        "needs.scope.outputs.base": "a" * 40,
        "needs.scope.outputs.candidate": "d" * 40,
        "needs.scope.outputs.full": "true",
        "matrix.group.key": DOTNET_GROUP,
        "needs.node-group.outputs.retained_directories": json.dumps(
            NODE_RETAINED
        ),
        "needs.node-group.outputs.adopted_directory": NODE_ADOPTED,
        "matrix.node-version": "22.x",
        "runner.temp": str(temp),
    }


def _commands(tmp_path: Path) -> dict[str, str]:
    return recorded_commands(
        tmp_path,
        *shlex.split(
            "dotnet go java node pwsh python3 ruby mise pnpm npm uv hk python"
        ),
    )


def _node_commands(tmp_path: Path) -> dict[str, str]:
    env = _commands(tmp_path)
    executable(Path(env["PATH"]) / "jq", NODE_JQ_COMMAND)
    return env


def _observations(env: dict[str, str]) -> list[dict[str, Any]]:
    log = Path(env["COMMAND_LOG"])
    observations = command_log(log)
    assert all(Path(row["cwd"]) == log.parent for row in observations)
    return observations


def _calls(env: dict[str, str]) -> list[list[str]]:
    return [row["command"] for row in _observations(env)]


@pytest.fixture
def ci_shell(workflow, tmp_path):
    """Run checked-in CI steps with scenario-local runner bindings."""
    return WorkflowShell(workflow, tmp_path, _bindings(tmp_path))


def _run_bash_steps(
    ci_shell: WorkflowShell,
    name: str,
    env: dict[str, str],
    steps: list[dict[str, Any]] | None = None,
    *,
    bindings: dict[str, str] | None = None,
) -> subprocess.CompletedProcess[str]:
    if steps is None:
        steps = ci_shell.workflow["jobs"][name]["steps"]
    required = [
        step
        for step in steps
        if "run" in step
        and step.get("id") != "scope"
        and step.get("if") != "cancelled()"
        and not step["run"].startswith(SKIPPED_TEST_SUMMARY)
    ]
    for step in required:
        _required_step(step)
    return ci_shell.run_required(
        name,
        required,
        env=env,
        bindings={
            "needs.scope.outputs.full": env.get("CI_MODE", "true"),
            "matrix.node-version": env.get("NODE_VERSION", "22.x"),
        }
        | (bindings or {}),
    )


# Merge gate and cancellation


def test_required_general_ci_checks_remain_eligible(workflow):
    """CI stays eligible on main without granting its jobs write access."""
    events = workflow.get("on", workflow.get(True))
    assert set(events) == {"push", "pull_request", "workflow_dispatch"}
    for event in ("pull_request", "push"):
        assert events[event] == {"branches": ["main"]}
    for job in workflow["jobs"].values():
        permissions = job.get("permissions", workflow["permissions"])
        assert isinstance(permissions, dict)
        assert permissions.get("contents") == "read"
        assert "write" not in permissions.values()
        assert not job.get("continue-on-error", False)
        assert not any(
            step.get("continue-on-error", False) for step in job["steps"]
        )


def _probe_value(expr: str, probe: Path, flag_value: str) -> str:
    fixed = {
        "github.workspace": str(probe),
        "runner.temp": str(probe),
        "matrix.node-version": "22.x",
    }
    if expr in fixed:
        return fixed[expr]
    if expr.endswith(".retained_directories"):
        return '{"22.x": ["src/pkg"]}'
    if expr.endswith(".result"):
        return "success"
    return "1" if expr.endswith("_id") else flag_value


def _step_gates_upstream(
    workflow: dict[str, Any],
    job: dict[str, Any],
    step: dict[str, Any],
    target_expr: str,
    probe: Path,
) -> bool:
    merged_env = effective_env(workflow, job, step)
    step_raw = json.dumps([merged_env, step["run"]])
    if target_expr not in set(re.findall(r"\$\{\{\s*(.*?)\s*\}\}", step_raw)):
        return False
    effective_wd = (
        step.get("working-directory")
        or job.get("defaults", {}).get("run", {}).get("working-directory")
        or workflow.get("defaults", {}).get("run", {}).get("working-directory")
        or "."
    )
    full_raw = json.dumps([merged_env, effective_wd, step["run"]])
    expressions = set(re.findall(r"\$\{\{\s*(.*?)\s*\}\}", full_raw))
    for flag_value in ("false", "true"):
        case_dir = probe / flag_value
        case_dir.mkdir(parents=True, exist_ok=True)
        env = _node_commands(case_dir)
        base = {e: _probe_value(e, case_dir, flag_value) for e in expressions}
        ok, bad = (
            run_step(
                step,
                cwd=case_dir,
                env=env | {"GITHUB_OUTPUT": str(case_dir / f"{status}-output")},
                bindings=base | {target_expr: status},
                workflow=workflow,
                job=job,
            ).returncode
            for status in ("success", "failure")
        )
        if ok != 0 or bad == 0:
            return False
    return True


def _step_propagates_upstream_failure(
    workflow: dict[str, Any],
    consumer_name: str,
    upstream: str,
    tmp_path: Path,
) -> bool:
    job = workflow["jobs"][consumer_name]
    if job.get("if") in (None, True, "success()"):
        return True
    default_shell = (
        job.get("defaults", {})
        .get("run", {})
        .get("shell", workflow.get("defaults", {}).get("run", {}).get("shell"))
    )
    target_expr = f"needs.{upstream}.result"
    return any(
        _step_gates_upstream(
            workflow,
            job,
            step,
            target_expr,
            tmp_path / f"propagate-{consumer_name}-{upstream}-{index}",
        )
        for index, step in enumerate(job["steps"])
        if "run" in step
        and not step.get("continue-on-error", False)
        and step.get("if") in (None, True, "success()")
        and step.get("shell", default_shell) in (None, "bash")
    )


def test_validate_transitively_gates_every_ci_job(workflow, tmp_path):
    """Validate is the only merge gate, and upstream failures must reach it."""
    jobs = workflow["jobs"]
    direct = set(dependencies(jobs["validation"]))
    gated = {"validation"} | direct
    changed = True
    while changed:
        changed = False
        for consumer_name in list(gated):
            if consumer_name == "validation":
                continue
            for upstream in dependencies(jobs[consumer_name]):
                if upstream not in gated and _step_propagates_upstream_failure(
                    workflow, consumer_name, upstream, tmp_path
                ):
                    gated.add(upstream)
                    changed = True
    assert set(jobs) <= gated


def test_required_validate_rejects_missing_or_failed_consumers(
    workflow, tmp_path
):
    """Keep every selected validation consumer inside the required check."""
    job = workflow["jobs"]["validation"]
    assert job["name"] == "Validate"
    assert job["if"] == "always()"
    successful = {f"needs.{key}.result": "success" for key in dependencies(job)}
    assert successful
    gates = [
        step
        for step in job["steps"]
        if "run" in step and step.get("if") == "always()"
    ]
    assert gates
    shell = WorkflowShell(workflow, tmp_path, {})

    def gate_passes(bindings):
        return all(
            shell.run("validation", step, bindings=bindings).returncode == 0
            for step in gates
        )

    assert gate_passes(successful)
    for dependency in successful:
        for result in ("failure", "cancelled", "skipped", ""):
            assert not gate_passes(successful | {dependency: result}), (
                dependency,
                result,
            )


def test_canceled_ci_work_stops_and_cannot_report_success(workflow, ci_shell):
    """Use documented cancelable jobs; shell checks do not model Actions."""
    assert workflow["concurrency"]["group"] == (
        "ci-${{ github.workflow }}-"
        "${{ github.head_ref || github.ref || github.run_id }}"
    )
    assert workflow["concurrency"]["cancel-in-progress"] is True
    for name, job in workflow["jobs"].items():
        if name == "scope":
            continue
        assert job["if"] == (
            "always()" if name == "validation" else "${{ !cancelled() }}"
        )
        assert any(
            ci_shell.run(name, step).returncode != 0
            for step in job["steps"]
            if "run" in step and step.get("if") == "cancelled()"
        ), name


# Scope selection, bootstrap, and endpoint preparation


@pytest.mark.parametrize("failure", [False, True])
def test_scope_selection_runs_once_in_the_control_application(
    ci_shell, tmp_path, failure
):
    """Select from native facts in one process and propagate its failure."""
    job = ci_shell.workflow["jobs"]["scope"]
    assert not action_steps(job, "actions/setup-python")
    assert not action_steps(job, "astral-sh/setup-uv")
    assert "PYTHONUTF8" not in job.get("env", {})
    metadata = _step_id(job, "tools")
    assert metadata["run"] == (
        "python eng/scripts/locked_tool_versions.py dotnet-frameworks "
        '>> "$GITHUB_OUTPUT"'
    )
    _required_step(metadata)
    assert not any(
        "python" in step.get("run", "") or "uv " in step.get("run", "")
        for step in job["steps"]
        if step is not metadata
    )
    env = _commands(tmp_path)
    request = str(tmp_path / "runner temp/ci-control-inputs/request.json")
    bindings = _bindings(tmp_path)
    selector = [
        "dotnet",
        bindings["steps.control.outputs.application"],
        "ci",
        "caller-dotnet",
        "select",
        str(tmp_path),
        bindings["steps.dotnet-group.outputs.request"],
        request,
        bindings["steps.control.outputs.transfer"],
        "artifacts/ci-scope.json",
    ]
    if failure:
        env["FAIL_COMMAND"] = json.dumps(selector)
    result = ci_shell.run(
        "scope",
        _step_id(job, "select"),
        env=env,
        bindings={"steps.materialize.outputs.request": request},
    )
    assert result.returncode == (CHILD_FAILURE if failure else 0), result.stderr
    assert _calls(env) == [selector]


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
    ci_shell, tmp_path, selection, applicable, run
):
    """Distinguish valid non-applicability from lost required work."""
    dedicated = shlex.split(
        "scope validation node-group ruby-group dotnet-tests dotnet-results "
        "python-tests native-preparation-tests"
    )
    for name, job in ci_shell.workflow["jobs"].items():
        if name in dedicated:
            continue
        assert "scope" in dependencies(job)
        if name == "node-tests":
            assert "node-group" in dependencies(job)
        assert job["if"] == "${{ !cancelled() }}"
        guard = job["steps"][0]
        assert guard["id"] == "scope"
        output = tmp_path / name
        key = (
            guard["env"]["APPLICABLE"].removeprefix("${{ ").removesuffix(" }}")
        )
        env = {"GITHUB_OUTPUT": str(output)}
        if name == "node-tests":
            env = _node_commands(tmp_path) | {"GITHUB_OUTPUT": str(output)}
        result = ci_shell.run(
            name,
            guard,
            env=env,
            bindings={
                "needs.scope.result": selection,
                key: applicable,
                "needs.node-group.result": "success",
            }
            | NUGET_SELECTION,
        )
        assert (result.returncode == 0) is (run is not None), result.stderr
        if run is not None:
            assert output.read_text().strip() == f"run={run}"
        else:
            assert not output.exists()
        for step in job["steps"][1:-1]:
            assert "steps.scope.outputs.run == 'true'" in step["if"]


def _scope_endpoint_commands(
    tmp_path: Path, line_ending: str = "\r\n"
) -> tuple[dict[str, str], list[Path]]:
    env = _commands(tmp_path)
    tools = Path(env["PATH"])
    fixture_path(tools, "mkdir", "basename")
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


def _prepare_endpoints(ci_shell, env):
    step = _step_id(ci_shell.workflow["jobs"]["scope"], "dotnet-group")
    return ci_shell.run("scope", step, env=env)


def test_native_framework_install_precedes_control_preparation(workflow):
    """Provision the framework provider before materializing control."""
    for name, boundary in (("scope", "control"), ("node-group", "execute")):
        job = workflow["jobs"][name]
        args = _action(job, "jdx/mise-action")["with"]["install_args"].split()
        assert args[:3] == ["--jobs", "1", "dotnet"]
        assert set(args[2:]) == {"dotnet", "node", "pnpm"}
        prepare = next(
            step
            for step in job["steps"]
            if step.get("run", "").startswith("mise install")
        )
        assert job["steps"].index(prepare) < job["steps"].index(
            _step_id(job, boundary)
        )
        assert prepare["run"] == (
            "mise install --monorepo --locked --jobs 1 dotnet"
        )


@pytest.mark.parametrize("line_ending", ["\n", "\r\n"], ids=["lf", "crlf"])
def test_scope_prepares_exact_native_endpoints_from_native_line_records(
    ci_shell, tmp_path, line_ending
):
    """Preserve exact native endpoint paths across shell setup."""
    env, endpoints = _scope_endpoint_commands(tmp_path, line_ending)
    result = _prepare_endpoints(ci_shell, env)
    assert result.returncode == 0, result.stderr
    observations = command_log(Path(env["COMMAND_LOG"]))
    native = [row for row in observations if row["command"][0] == "mise"]
    assert {Path(row["cwd"]) for row in native} == set(endpoints)
    for endpoint in endpoints:
        commands = [
            row["command"] for row in native if row["cwd"] == str(endpoint)
        ]
        assert commands[0] == shlex.split(
            "mise install --monorepo --locked --jobs 1 dotnet"
        )
        assert commands[1] == shlex.split(
            "mise install --locked dotnet node pnpm pkl"
        )
        assert ["dotnet", "tool", "restore"] not in [
            command[-3:] for command in commands
        ]
        assert commands[2][:8] == shlex.split(
            "mise exec --locked dotnet -- dotnet restore dirs.proj"
        )
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
        ["mise", "exec", "--locked", "dotnet", "--", "dotnet", "restore"],
    ],
    ids=["install", "graph-restore"],
)
def test_scope_endpoint_native_failure_blocks_dependent_preparation(
    ci_shell, tmp_path, failed_command
):
    """Keep every native prerequisite fatal before dependent preparation."""
    env, endpoints = _scope_endpoint_commands(tmp_path, "\n")
    env["FAIL_COMMAND"] = json.dumps(failed_command)
    result = _prepare_endpoints(ci_shell, env)
    assert result.returncode == CHILD_FAILURE
    observations = command_log(Path(env["COMMAND_LOG"]))
    assert observations[-1]["command"][: len(failed_command)] == failed_command
    assert all(row["command"][0] != "dotnet" for row in observations)
    assert {Path(row["cwd"]) for row in observations[1:]} == {endpoints[0]}


@pytest.mark.parametrize("case", ["empty", "partial", "unwritable"])
def test_scope_blocks_native_preparation_after_failed_extraction(
    ci_shell, tmp_path, case
):
    """Never use a failed extraction, even if it emits partial records."""
    env, _ = _scope_endpoint_commands(tmp_path)
    if case == "unwritable":
        temp = Path(env["RUNNER_TEMP"])
        scratch = temp / "dotnet-group/endpoint-preparation/directories.txt"
        scratch.mkdir(parents=True)
    else:
        env.update(
            JQ_EXIT=str(CHILD_FAILURE),
            JQ_EMIT_ROWS=str(case == "partial").lower(),
        )
    result = _prepare_endpoints(ci_shell, env)
    if case == "unwritable":
        assert result.returncode != 0
        assert _calls(env) == []
    else:
        assert result.returncode == CHILD_FAILURE
        assert [call[0] for call in _calls(env)] == ["jq"]


def _run_validation(
    ci_shell: WorkflowShell,
    env: dict[str, str],
) -> subprocess.CompletedProcess[str]:
    job = ci_shell.workflow["jobs"]["conformance"]
    mise = _action(job, "jdx/mise-action")
    assert mise["with"]["experimental"] is True
    assert mise["with"]["install"] is False
    boundary = job["steps"].index(mise)
    return _run_bash_steps(
        ci_shell, "conformance", env, job["steps"][boundary + 1 :]
    )


@pytest.mark.parametrize(("shell", "exit_code"), [(None, 0), ("bash", 1)])
def test_workflow_shell_uses_only_fixture_bindings(
    tmp_path, monkeypatch, shell, exit_code
):
    """Host startup hooks and Actions variables cannot affect shell outcomes."""
    startup = tmp_path / "host-startup"
    startup.write_text("exit 93\n")
    for key in ("BASH_ENV", "ENV", "CI", "GITHUB_WORKSPACE", "RUNNER_TEMP"):
        monkeypatch.setenv(key, str(startup))
    step = {
        "run": (
            '[[ -z "${ENV}${CI}${GITHUB_WORKSPACE}${RUNNER_TEMP}" ]] '
            "|| exit 91\n"
            '[[ "$GITHUB_EVENT_NAME" == pull_request ]] || exit 92\n'
            '[[ "$DEFAULT:$JOB_ONLY:$LAYER" == workflow:job:step ]] '
            "|| exit 94\n"
            "false | true"
        ),
        "shell": shell,
        "env": {"LAYER": "${{ fixture.layer }}"},
    }
    result = run_step(
        step,
        cwd=tmp_path,
        env={"GITHUB_EVENT_NAME": "pull_request"},
        bindings={"fixture.layer": "step"},
        workflow={"env": {"DEFAULT": "workflow", "LAYER": "${{ shadowed }}"}},
        job={"env": {"JOB_ONLY": "job", "LAYER": "${{ also.shadowed }}"}},
    )
    assert result.returncode == exit_code, result.stderr


def test_validation_installs_locked_tools_before_bootstrap_and_hk(
    workflow, tmp_path, ci_shell
):
    """Locked mise tools install before bootstrap and HK, without relabels."""
    assert not action_steps(
        workflow["jobs"]["conformance"], "pkl-community/setup-pkl"
    )
    env = _commands(tmp_path)
    result = _run_validation(ci_shell, env)
    assert result.returncode == 0, result.stderr
    calls = _calls(env)
    assert not any(command[:2] == ["mise", "link"] for command in calls)
    install = calls.index(["mise", "install", "--locked"])
    bootstrap = calls.index(["mise", "bootstrap"])
    hk = next(index for index, call in enumerate(calls) if call[0] == "hk")
    assert calls[hk][:8] == HK_CHECK
    assert install < bootstrap < hk
    for item in _observations(env):
        if item["command"][0] in {"mise", "hk"}:
            disabled = item["disabled_tools"].split(",")
            assert "gopass" in disabled
            # The locked mise Pkl serves HK; no second Pkl setup exists.
            assert "pkl" not in disabled


@pytest.mark.parametrize("failure", ["install", "bootstrap"])
def test_validation_bootstrap_failure_stops_hk(ci_shell, tmp_path, failure):
    """Keep validation blocked when tool installation or bootstrap fails."""
    env = _commands(tmp_path)
    env["FAIL_COMMAND"] = json.dumps(["mise", failure])
    result = _run_validation(ci_shell, env)
    assert result.returncode == CHILD_FAILURE
    calls = _calls(env)
    assert not any(command[0] == "hk" for command in calls)
    if failure == "install":
        assert ["mise", "bootstrap"] not in calls


@pytest.mark.parametrize("full", [True, False])
@pytest.mark.parametrize("exit_code", [0, 73])
def test_validation_hk_uses_tested_comparison_and_propagates_failure(
    ci_shell, tmp_path, full, exit_code
):
    """The tested candidate and explicit full mode reach the HK boundary."""
    env = _commands(tmp_path)
    env["CI_MODE"] = str(full).lower()
    child = "hk" if full else "python"
    if exit_code:
        env["FAIL_COMMAND"] = json.dumps([child])
    result = _run_validation(ci_shell, env)
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
    flags = shlex.split("--check --no-stage --no-progress --no-fail-fast")
    assert hk == [*HK_CHECK, *flags, *(["--all"] if full else [])]
    assert {"small", "medium", "large"} <= set(observed["profile"].split(","))


# Managed ecosystem CI groups: .NET, Node, Python, Ruby


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


def _dotnet_command(cwd, mode):
    temp = cwd / "runner-state"
    plan = temp / "dotnet-plan-transfer"
    args = [
        "dotnet",
        str(plan / "control/WorkflowDelivery.dll"),
        "ci",
        "caller-dotnet",
        mode,
        str(cwd),
        str(plan),
    ]
    revisions = ["a" * 40, "d" * 40]
    if mode == "join":
        paths = ("dotnet-group-results", "dotnet-results")
        return [*args, *(str(temp / path) for path in paths), *revisions]
    execution = temp / "dotnet-execution" / DOTNET_GROUP
    return [*args, str(execution), *revisions, DOTNET_GROUP]


def test_dotnet_check_receives_original_plan_and_complete_control(workflow):
    """Transfer the producer's full output by ID and bind receiver lifetime."""
    scope = workflow["jobs"]["scope"]
    job = workflow["jobs"]["dotnet-tests"]
    assert job["runs-on"].startswith("windows-")
    assert job["permissions"] == {"contents": "read", "actions": "read"}
    assert "needs.scope.outputs.dotnet }}" not in json.dumps(job)
    assert _action(job, "actions/setup-dotnet")["with"] == {
        "global-json-file": "global.json",
        "dotnet-version": "${{ needs.scope.outputs.dotnet_frameworks }}",
    }
    _assert_checkout(job, ref="${{ github.sha }}", full_history=True)
    producer = _step_id(scope, "control")
    assert producer["shell"] == "pwsh"
    assert "Bootstrap-CiControl.ps1" in producer["run"]
    upload = artifact_upload(scope, "dotnet_artifact_id")
    assert upload["with"]["path"] == "${{ steps.control.outputs.transfer }}/"
    assert upload["with"]["if-no-files-found"] == "error"
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


def test_dotnet_groups_follow_the_plan_and_provision_planned_tools(workflow):
    """Each planned tool group is one job using committed tool versions."""
    scope = workflow["jobs"]["scope"]
    job = workflow["jobs"]["dotnet-tests"]
    assert scope["outputs"]["dotnet_groups"] == (
        "${{ steps.select.outputs.dotnet_groups }}"
    )
    assert job["strategy"]["fail-fast"] is False
    assert job["strategy"]["matrix"]["group"] == (
        "${{ fromJSON(needs.scope.outputs.dotnet_groups) }}"
    )
    execute = next(
        step for step in job["steps"] if " execute " in step.get("run", "")
    )
    for action, tool, version in (
        ("actions/setup-python", "python", "python-version"),
        ("actions/setup-node", "node", "node-version"),
    ):
        step = _action(job, action)
        assert step["if"] == f"matrix.group.tools.{tool}"
        assert step["with"][version] == f"${{{{ matrix.group.tools.{tool} }}}}"
        assert job["steps"].index(step) < job["steps"].index(execute)


def test_dotnet_results_receives_only_same_run_inputs(workflow):
    """Receive the original plan by ID and only its same-run matrix results."""
    job = workflow["jobs"]["dotnet-results"]
    assert job["runs-on"].startswith("ubuntu-")
    assert job["permissions"] == {"contents": "read", "actions": "read"}
    assert {"scope", "dotnet-tests"} <= set(dependencies(job))
    downloads = action_steps(job, "actions/download-artifact")
    by_path = {step["with"]["path"]: step for step in downloads}
    expected = {
        f"{RUNNER_TEMP}/dotnet-plan-transfer": {
            "artifact-ids": "${{ needs.scope.outputs.dotnet_artifact_id }}",
        },
        f"{RUNNER_TEMP}/dotnet-group-results": {
            "pattern": "dotnet-group-results-*",
        },
    }
    assert len(downloads) == len(expected)
    assert by_path.keys() == expected.keys()
    steps = job["steps"]
    join_index = next(
        index
        for index, step in enumerate(steps)
        if "ci caller-dotnet join " in step.get("run", "")
    )
    for path, selector in expected.items():
        step = by_path[path]
        assert steps.index(step) < join_index
        assert "if" not in step
        assert not step.get("continue-on-error", False)
        # Exact inputs forbid cross-run selectors and digest-error overrides.
        assert {"digest-mismatch": "error", **step["with"]} == {
            **selector,
            "path": path,
            "merge-multiple": True,
            "digest-mismatch": "error",
        }
    group = workflow["jobs"]["dotnet-tests"]
    assert any(
        step["with"]["name"] == "dotnet-group-results-${{ matrix.group.key }}"
        and step["with"]["path"] == "${{ runner.temp }}/dotnet-execution/"
        for step in action_steps(group, "actions/upload-artifact")
    )


@pytest.mark.parametrize("failure", [False, True])
def test_dotnet_joins_results_against_original_plan(
    ci_shell, tmp_path, failure
):
    """Join against the original plan and propagate incomplete results."""
    env = _dotnet_commands(tmp_path)
    command = _dotnet_command(tmp_path, "join")
    if failure:
        env["FAIL_COMMAND"] = json.dumps(command)
    result = _run_bash_steps(ci_shell, "dotnet-results", env)
    assert result.returncode == (CHILD_FAILURE if failure else 0), result.stderr
    assert _calls(env) == [command]


@pytest.mark.parametrize(
    ("selection", "artifact", "exit_code"),
    [
        ("success", "1054", 0),
        ("success", "1054", CHILD_FAILURE),
        ("success", "1", 0),
        ("failure", "1054", None),
        ("cancelled", "1054", None),
        ("skipped", "1054", None),
        ("", "1054", None),
        ("success", "", None),
        ("success", "0", None),
        ("success", "-1", None),
        ("success", "1x", None),
        ("success", "01", None),
    ],
)
def test_dotnet_group_requires_received_original_plan(
    ci_shell, tmp_path, selection, artifact, exit_code
):
    """Reject invalid IDs; run the original plan and surface child failure."""
    env = _dotnet_commands(tmp_path)
    command = _dotnet_command(tmp_path, "execute")
    if exit_code == CHILD_FAILURE:
        env["FAIL_COMMAND"] = json.dumps(command)
    result = _run_bash_steps(
        ci_shell,
        "dotnet-tests",
        env,
        bindings={
            "needs.scope.result": selection,
            "needs.scope.outputs.dotnet_artifact_id": artifact,
        },
    )
    calls = _calls(env)
    if exit_code is None:
        assert result.returncode != 0, result.stderr
        assert calls == []
    else:
        assert result.returncode == exit_code, result.stderr
        assert calls[-1] == command
        assert not any("caller-artifact" in call for call in calls)


def _retained_command(tmp_path: Path, version: str) -> list[str]:
    state = tmp_path / "runner-state"
    control = state / "retained-control/control/WorkflowDelivery.dll"
    return [
        "dotnet",
        str(control),
        *shlex.split("ci caller-node retained"),
        str(tmp_path),
        str(state / "retained-node"),
        json.dumps(NODE_RETAINED[version], separators=(",", ":")),
        NODE_ADOPTED,
        NODE_PACKAGE_DIR,
        str(tmp_path / "setup tools/pnpm"),
    ]


def test_node_matrix_consumes_the_complete_planned_runtime_selectors(
    workflow: dict[str, Any],
) -> None:
    """Workflow Delivery plans the matrix; the workflow repeats no selector."""
    group = workflow["jobs"]["node-group"]
    job = workflow["jobs"]["node-tests"]
    assert group["outputs"]["node_versions"] == (
        "${{ steps.execute.outputs.node_versions }}"
    )
    planned = "fromJSON(needs.node-group.outputs.node_versions)"
    assert job["strategy"]["matrix"]["node-version"] == f"${{{{ {planned} }}}}"
    assert "node-group" in dependencies(job)


@pytest.mark.parametrize("version", ["22.x", "24.x", "26.x"])
def test_node_checks_execute_selected_retained_members(
    workflow, ci_shell, tmp_path, version
):
    """Both retained contexts install before native filtered execution."""
    job = workflow["jobs"]["node-tests"]
    assert not action_steps(job, "ruby/setup-ruby")
    setup = _action(job, "actions/setup-node")
    assert (
        resolve(setup["with"]["node-version"], {"matrix.node-version": version})
        == version
    )
    env = _node_commands(tmp_path)
    env["NODE_VERSION"] = version
    result = _run_bash_steps(ci_shell, "node-tests", env)
    assert result.returncode == 0, result.stderr
    calls = _calls(env)
    execution = calls.index(_retained_command(tmp_path, version))
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
    ci_shell, tmp_path, failure
):
    """Fail the retained boundary on dependency or selected work failure."""
    env = _node_commands(tmp_path)
    env["FAIL_COMMAND"] = json.dumps(failure)
    result = _run_bash_steps(ci_shell, "node-tests", env)
    assert result.returncode == CHILD_FAILURE, result.stderr
    calls = _calls(env)
    assert calls[-1][: len(failure)] == list(failure)


def test_node_check_skips_an_empty_runtime_variant(
    workflow, ci_shell, tmp_path
):
    """A valid selector with no retained members does not provision a runner."""
    job = workflow["jobs"]["node-tests"]
    guard = job["steps"][0]
    output = tmp_path / "scope-output"
    env = _node_commands(tmp_path)
    env["GITHUB_OUTPUT"] = str(output)
    applicable = (
        guard["env"]["APPLICABLE"].removeprefix("${{ ").removesuffix(" }}")
    )

    result = ci_shell.run(
        "node-tests",
        guard,
        env=env,
        bindings={
            "matrix.node-version": "28.x",
            "needs.node-group.result": "success",
            applicable: "true",
        },
    )

    assert result.returncode == 0, result.stderr
    assert output.read_text().strip() == "run=false"
    assert [call[0] for call in _calls(env)] == ["jq", "jq"]
    assert all(
        "steps.scope.outputs.run == 'true'" in step["if"]
        for step in job["steps"][1:-1]
    )


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
    workflow, ci_shell, selection, artifact, succeeds
):
    """Group always plans and consumes the actual scope upload identity."""
    job = workflow["jobs"]["node-group"]
    assert "scope" in dependencies(job)
    assert job["env"]["MISE_EXEC_AUTO_INSTALL"] == "0"
    assert "needs.scope.outputs.node" not in json.dumps(job)
    assert "ruby" not in json.dumps(job).lower()
    entry = _step_id(job, "execute")["run"]
    assert "uv " not in entry
    assert "python" not in entry
    assert 'ci caller-node plan "$state" "$scope"' in entry
    artifact_upload(workflow["jobs"]["scope"], "artifact_id")
    inputs = [s["with"] for s in action_steps(job, "actions/download-artifact")]
    download = next(i for i in inputs if i["path"] == "artifacts/node-scope")
    assert download["artifact-ids"] == "${{ needs.scope.outputs.artifact_id }}"
    assert "name" not in download
    result = ci_shell.run(
        "node-group",
        job["steps"][0],
        bindings={
            "needs.scope.result": selection,
            "needs.scope.outputs.artifact_id": artifact,
        },
    )
    assert (result.returncode == 0) is succeeds, result.stderr
    _assert_checkout(job, full_history=True)
    assert job["permissions"] == {"contents": "read", "actions": "read"}
    assert not [
        step for step in job["steps"] if "GH_TOKEN" in step.get("env", {})
    ]


def test_python_check_has_consumed_toolchain_prerequisites(workflow):
    """Committed SDKs and query tools precede selected adapter preparation."""
    job = workflow["jobs"]["python-tests"]
    steps = job["steps"]
    materialize, plan = (_step_id(job, key) for key in ("endpoints", "plan"))
    control_sdk, basis_sdk, candidate_sdk = action_steps(
        job, "actions/setup-dotnet"
    )
    assert steps.index(control_sdk) < steps.index(materialize)
    assert control_sdk["with"]["global-json-file"] == "global.json"
    for role, sdk in (("basis", basis_sdk), ("candidate", candidate_sdk)):
        assert steps.index(materialize) < steps.index(sdk) < steps.index(plan)
        assert sdk["with"]["global-json-file"] == (
            f"${{{{ steps.endpoints.outputs.{role}_directory }}}}/global.json"
        )
    assert (
        candidate_sdk["if"]
        == "steps.endpoints.outputs.basis_separate == 'true'"
    )
    _assert_checkout(job, full_history=True)
    python = _action(job, "actions/setup-python")
    uv = _action(job, "astral-sh/setup-uv")
    assert steps.index(python) < steps.index(materialize)
    assert steps.index(uv) < steps.index(plan)
    assert python["with"]["python-version-file"] == ".python-version"
    assert "python-version" not in uv["with"]
    assert not action_steps(job, "ruby/setup-ruby")
    assert not action_steps(job, "jdx/mise-action")


NATIVE_CALLERS = [("python-tests", "python"), ("ruby-group", "ruby")]
SCOPE_ARTIFACT = "needs.scope.outputs.artifact_id"
CONTROL_ARTIFACT = "needs.scope.outputs.dotnet_artifact_id"
RUNNER_TEMP = "${{ runner.temp }}"


@pytest.mark.parametrize("name", [name for name, _ in NATIVE_CALLERS])
@pytest.mark.parametrize(
    "scenario",
    [
        ("success", "11", "12", True),
        ("failure", "11", "12", False),
        ("cancelled", "11", "12", False),
        ("success", "", "12", False),
        ("success", "11", "", False),
    ],
)
def test_native_caller_requires_original_scope_and_control(
    ci_shell, name, scenario
):
    """Lost scope or control blocks native endpoint preparation."""
    selection, scope, control, succeeds = scenario
    result = ci_shell.run(
        name,
        ci_shell.workflow["jobs"][name]["steps"][0],
        bindings={
            "needs.scope.result": selection,
            SCOPE_ARTIFACT: scope,
            CONTROL_ARTIFACT: control,
        },
    )
    assert (result.returncode == 0) is succeeds, result.stderr


@pytest.mark.parametrize(("name", "ecosystem"), NATIVE_CALLERS)
def test_native_caller_uses_same_run_artifacts(workflow, name, ecosystem):
    """Receive the original IDs and retain work even when execution fails."""
    job = workflow["jobs"][name]
    assert "scope" in dependencies(job)
    assert job["permissions"] == {"contents": "read", "actions": "read"}
    assert "environment" not in job
    receivers = [
        s["with"] for s in action_steps(job, "actions/download-artifact")
    ]
    assert {
        (f"${{{{ {SCOPE_ARTIFACT} }}}}", f"{RUNNER_TEMP}/{ecosystem}-scope"),
        (
            f"${{{{ {CONTROL_ARTIFACT} }}}}",
            f"{RUNNER_TEMP}/{ecosystem}-control",
        ),
    } <= {(data["artifact-ids"], data["path"]) for data in receivers}
    assert all(
        not {"name", "pattern", "run-id", "repository", "github-token"}
        & data.keys()
        for data in receivers
    )
    if ecosystem == "ruby":
        assert not action_steps(job, "actions/setup-python")
    (retained,) = action_steps(job, "actions/upload-artifact")
    assert retained["if"] == "always()"
    assert retained["with"]["name"] == f"{ecosystem}-group-results"
    assert "*.json" in retained["with"]["path"]
    assert "*.stderr" in retained["with"]["path"]


def _native_caller_commands(cwd, ecosystem):
    env = _commands(cwd)
    if ecosystem == "ruby":
        tools = Path(env["PATH"])
        executable(tools / "ruby", "print('2.7.2', end='')")
        for tool in ("gem", "bundle"):
            executable(tools / tool, "pass")
    return env


@pytest.mark.parametrize(("name", "ecosystem"), NATIVE_CALLERS)
@pytest.mark.parametrize("failure", [False, True])
def test_native_caller_runs_original_plan(ci_shell, name, ecosystem, failure):
    """Materialize, plan and execute the original input; surface failure."""
    caller = f"caller-{ecosystem}"
    steps = [
        s
        for s in ci_shell.workflow["jobs"][name]["steps"][1:]
        if caller in s.get("run", "")
    ]
    assert all(s.get("if") in (None, True, "success()") for s in steps)
    env = _native_caller_commands(ci_shell.cwd, ecosystem)
    temp = Path(env["RUNNER_TEMP"])
    control = temp / f"{ecosystem}-control"
    prefix = [
        "dotnet",
        str(control / "control/WorkflowDelivery.dll"),
        "ci",
        caller,
    ]
    scope = str(temp / f"{ecosystem}-scope/ci-scope.json")
    group = str(temp / f"{ecosystem}-group")
    expected = [
        [*prefix, "materialize", str(ci_shell.cwd), scope, str(control), group],
        [*prefix, "plan", group, scope],
        [*prefix, "execute", group],
    ]
    if failure:
        env["FAIL_COMMAND"] = json.dumps(expected[-1])
    result = _run_bash_steps(ci_shell, name, env, steps)
    assert result.returncode == (CHILD_FAILURE if failure else 0), result.stderr
    materialize, plan, execute = _calls(env)
    assert materialize == expected[0]
    assert plan[:7] == expected[1]
    assert execute[:6] == expected[2]
    if ecosystem == "ruby":
        assert plan[-1] == "2.7.2"
        assert execute == expected[2]


# Native preparation qualification and reproducibility


NUGET_SELECTION = {
    "needs.scope.outputs.nuget_native_preparation_filter": "TestCategory=X",
    "needs.scope.outputs.nuget_native_preparation_tests": "1",
}


@pytest.mark.parametrize(
    ("selection_filter", "tests"),
    [("", "1"), ("TestCategory=X", ""), ("TestCategory=X", "0")],
)
def test_nuget_preparation_guard_rejects_missing_rows(
    ci_shell, tmp_path, selection_filter, tests
):
    """Applicable NuGet preparation needs its row filter and count."""
    name = "nuget-native-preparation"
    job = ci_shell.workflow["jobs"][name]
    output = tmp_path / "output"
    result = ci_shell.run(
        name,
        job["steps"][0],
        env={"GITHUB_OUTPUT": str(output)},
        bindings={
            "needs.scope.result": "success",
            "needs.scope.outputs.nuget_native_preparation": "true",
        }
        | dict(zip(NUGET_SELECTION, (selection_filter, tests), strict=True)),
    )
    assert result.returncode != 0
    assert not output.exists()


CONTROL_PROJECT = (
    "tests/private/app/workflow-delivery/WorkflowDelivery.Tests.csproj"
)


def _native_test(job: dict[str, Any]) -> dict[str, Any]:
    step = next(
        step for step in job["steps"] if "dotnet test" in step.get("run", "")
    )
    assert step["shell"] == "pwsh"
    assert CONTROL_PROJECT in step["run"]
    assert "exit $LASTEXITCODE" in step["run"]
    return step


def _assert_native_evidence(job, path, *, gate="always()", days=None):
    upload = _action(job, "actions/upload-artifact")
    assert upload["if"] == gate
    assert upload["with"]["path"] == f"artifacts/{path}"
    assert upload["with"]["if-no-files-found"] == "error"
    if days is not None:
        assert upload["with"]["retention-days"] == days


def _normalized_if(expression: str) -> str:
    body = expression.strip().removeprefix("${{").removesuffix("}}").strip()
    return " ".join(body.split())


@pytest.mark.parametrize(
    ("results_directory", "expected_if"),
    [
        ("python", "steps.plan.outputs.python == 'true'"),
        ("ruby", "!cancelled() && steps.plan.outputs.ruby == 'true'"),
        ("node", "!cancelled() && steps.plan.outputs.node == 'true'"),
        (
            "browser-store",
            "!cancelled() && steps.plan.outputs.browser == 'true'",
        ),
    ],
)
def test_native_preparation_runs_only_selected_scenarios(
    workflow, results_directory, expected_if
):
    """Each native test step runs for its own selected plan output."""
    job = workflow["jobs"]["native-preparation-tests"]
    directory = (
        '--results-directory "$RUNNER_TEMP/native-preparation/'
        f'{results_directory}"'
    )
    step = next(s for s in job["steps"] if directory in s.get("run", ""))
    assert _normalized_if(step["if"]) == expected_if


def test_native_preparation_provisions_selected_tools_and_retains_failures(
    workflow,
):
    """Provision the union of selected tools and keep an auditable outcome."""
    job = workflow["jobs"]["native-preparation-tests"]
    assert _normalized_if(job["if"]) == "!cancelled()"
    setup = _action(job, "jdx/mise-action")
    assert _normalized_if(setup["if"]) == (
        "steps.plan.outputs.python == 'true' || "
        "steps.plan.outputs.ruby == 'true' || "
        "steps.plan.outputs.node == 'true' || "
        "steps.plan.outputs.browser == 'true'"
    )
    assert " ".join(setup["with"]["install_args"].split()) == (
        "--jobs 1 dotnet "
        "${{ steps.plan.outputs.python == 'true' && 'python uv' || '' }} "
        "${{ steps.plan.outputs.ruby == 'true' && 'ruby' || '' }} "
        "${{ (steps.plan.outputs.node == 'true' || "
        "steps.plan.outputs.browser == 'true') && 'node pnpm' || '' }}"
    )
    skipped = next(
        s
        for s in job["steps"]
        if "Write-SkippedTestSummary.ps1" in s.get("run", "")
    )
    assert (
        _normalized_if(skipped["if"])
        == "always() && steps.plan.outcome == 'success'"
    )
    upload = _action(job, "actions/upload-artifact")
    assert upload["with"]["name"] == "native-preparation-results"
    assert _normalized_if(upload["if"]) == "always()"
    assert upload["with"]["if-no-files-found"] == "error"


def test_nuget_native_preparation_runs_selected_rows(workflow):
    """Per-PR CI runs the selected units' native NuGet preparation rows."""
    name = "nuget-native-preparation"
    job = workflow["jobs"][name]
    assert job["runs-on"].startswith("windows-")
    selection = _scope_inputs(
        workflow,
        name,
        APPLICABLE="nuget_native_preparation",
        FILTER="nuget_native_preparation_filter",
        TESTS="nuget_native_preparation_tests",
    )
    _assert_checkout(job)
    test = _native_test(job)
    run = test["run"]
    assert {k: selection[k] for k in ("FILTER", "TESTS")} == test["env"]
    assert '--filter "$env:FILTER"' in run
    assert '-- --minimum-expected-tests "$env:TESTS"' in run
    assert "--report-trx" in run
    assert "native-preparation.binlog" in run
    _assert_native_evidence(
        job,
        "nuget-native-preparation",
        gate="always() && steps.scope.outputs.run == 'true'",
        days=30,
    )


def test_github_release_native_preparation_runs_the_real_recipe(workflow):
    """Per-PR CI qualifies the real ImageOcclusionEditor recipe on Windows."""
    name = "github-release-native-preparation"
    job = workflow["jobs"][name]
    assert job["runs-on"].startswith("windows-")
    _scope_inputs(
        workflow, name, APPLICABLE="github_release_native_preparation"
    )
    _assert_checkout(job, full_history=True)
    assert "permissions" not in job
    run = _native_test(job)["run"]
    assert "--filter 'TestCategory=GithubReleaseNativePreparation'" in run
    assert "-- --minimum-expected-tests 1" in run
    _assert_native_evidence(
        job,
        name,
        gate="always() && steps.scope.outputs.run == 'true'",
    )


def test_ci_dotnet_test_steps_name_skipped_tests(workflow):
    """Every CI .NET test execution writes TRX that a later step reports."""
    covered = set()
    for name, job in workflow["jobs"].items():
        steps = job["steps"]
        for index, step in enumerate(steps):
            run = step.get("run", "")
            if "dotnet test" not in run and "caller-dotnet execute" not in run:
                continue
            assert "--report-trx" in run or (
                "ThreeTestResultsDirectory" in step["env"]
            )
            summary = next(
                later
                for later in steps[index + 1 :]
                if later.get("run", "").startswith(SKIPPED_TEST_SUMMARY)
            )
            _assert_skipped_test_summary(summary)
            covered.add(name)
    assert covered


def test_nuget_reproducibility_runs_on_dispatch_and_main_changes(workflow):
    """Ordinary PRs do not re-prove established .NET reproducibility."""
    assert "nuget-reproducibility" not in workflow["jobs"]
    repro = _workflow("workflow-delivery-v3-nuget-reproducibility")
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
        "src/private/app/workflow-delivery/Ecosystems/NuGet*.cs",
        "src/private/app/workflow-delivery/Destinations/*NuGet*.cs",
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
    _assert_checkout(
        job, ref="${{ inputs.ref || github.sha }}", full_history=True
    )
    run = _native_test(job)["run"]
    assert "--filter 'TestCategory=NuGetNativeScenarios'" in run
    assert "-- --minimum-expected-tests 3" in run
    assert "--report-trx" in run
    assert "native-scenarios.binlog" in run
    _assert_native_evidence(job, "nuget-reproducibility", days=30)


# Renovate compatibility and its three-day cooldown


@pytest.fixture
def renovate_action():
    """Load the composite runtime shared by PRs and scheduled Renovate."""
    path = REPO_ROOT / ".github/actions/setup-renovate/action.yml"
    return yaml.safe_load(path.read_text())["runs"]


@pytest.fixture
def renovate_project(tmp_path):
    """Provide the composite action's checkout-relative project directory."""
    project = tmp_path / "eng/renovate"
    project.mkdir(parents=True)
    return project


def test_renovate_compat_shares_the_credential_free_runtime(
    workflow, renovate_action
):
    """The retained check and scheduled workflow use the same runtime setup."""
    _scope_inputs(workflow, "renovate-compat", APPLICABLE="renovate")
    job = workflow["jobs"]["renovate-compat"]
    _assert_checkout(job)
    setup = _action(job, "./.github/actions/setup-renovate")
    assert setup["with"]["repository-node"] == (
        "${{ steps.tools.outputs.node }}"
    )
    text = json.dumps([workflow.get("env"), job, renovate_action]).lower()
    assert "environment" not in job
    assert not any(
        key in text
        for key in ("secrets.", "renovate_token", "github.token", "gh_token")
    )

    scheduled = _workflow("renovate")["jobs"]["renovate"]
    assert (
        _action(scheduled, "./.github/actions/setup-renovate")["with"]
        == (setup["with"])
    )
    assert {
        "${{ steps.engine.outputs.range }}",
        "${{ inputs.repository-node }}",
    } <= {
        step["with"]["node-version"]
        for step in action_steps(renovate_action, "actions/setup-node")
    }


@pytest.mark.parametrize(
    ("engine", "succeeds"),
    [
        ("^24.11.0", True),
        (">=26.0.0 <27", True),
        ("", False),
        ("^24\nrange=26", False),
        (None, False),
    ],
)
def test_renovate_node_range_comes_from_the_lock(
    tmp_path, renovate_action, renovate_project, engine, succeeds
):
    """A missing or multiline engine cannot become a setup-node input."""
    (renovate_project / "package-lock.json").write_text(
        json.dumps(
            {
                "packages": {
                    "node_modules/renovate": {"engines": {"node": engine}}
                }
            }
        )
    )
    output = tmp_path / "output"
    result = run_step(
        _step_id(renovate_action, "engine"),
        cwd=tmp_path,
        env={"GITHUB_OUTPUT": str(output)},
        bindings={},
    )
    assert (result.returncode == 0) is succeeds, result.stderr
    if succeeds:
        assert output.read_text().strip() == f"range={engine}"


@pytest.mark.parametrize(
    ("case", "succeeds"),
    [
        ("matching", True),
        ("different-engine", False),
        ("missing-validator", False),
        ("install-fails", False),
    ],
)
def test_renovate_install_binds_the_runtime_to_the_lock(
    tmp_path, renovate_action, renovate_project, case, succeeds
):
    """An incomplete install or a changed package engine stops the setup."""
    package = renovate_project / "node_modules/renovate"
    (package / "dist").mkdir(parents=True)
    (package / "dist/renovate.js").touch()
    if case != "missing-validator":
        (package / "dist/config-validator.js").touch()
    (package / "package.json").write_text(
        json.dumps(
            {
                "engines": {
                    "node": "^26" if case == "different-engine" else "^24"
                },
                "bin": {
                    "renovate": "dist/renovate.js",
                    "renovate-config-validator": "dist/config-validator.js",
                },
            }
        )
    )
    env = recorded_commands(tmp_path, "npm")
    tools = Path(env["PATH"])
    fixture_path(tools, "python3")
    executable(tools / "node", "print('/fixture/renovate-node')")
    if case == "install-fails":
        env["FAIL_COMMAND"] = json.dumps(["npm", "ci"])
    result = run_step(
        _step_id(renovate_action, "install"),
        cwd=tmp_path,
        env=env,
        bindings={"steps.engine.outputs.range": "^24"},
    )
    assert (result.returncode == 0) is succeeds, result.stderr
    (install,) = command_log(Path(env["COMMAND_LOG"]))
    assert install["command"][:2] == ["npm", "ci"]
    if succeeds:
        lines = dict(
            line.split("=", 1)
            for line in Path(env["GITHUB_OUTPUT"]).read_text().splitlines()
        )
        assert lines == {
            "cli": str((package / "dist/renovate.js").resolve()),
            "validator": str((package / "dist/config-validator.js").resolve()),
            "node": "/fixture/renovate-node",
        }


@pytest.mark.parametrize(
    "override",
    [
        {},
        {"SUPPORTED": "no"},
        {"AGE": "2"},
        {"BEFORE": "2026-01-01"},
        {"EXCLUSIONS": "some-package"},
        {"FAILED_GET": "min-release-age-exclude"},
    ],
    ids=[
        "three-days",
        "unsupported",
        "too-short",
        "before",
        "exclusion",
        "get-fails",
    ],
)
@pytest.mark.usefixtures("renovate_project")
def test_renovate_rejects_unsupported_or_weakened_npm_cooldown(
    tmp_path, renovate_action, override
):
    """The npm used by repository tasks must enforce the complete policy."""
    config = REPO_ROOT / "eng/renovate/.npmrc"
    assert "min-release-age=3" in config.read_text().splitlines()
    step = next(
        s
        for s in renovate_action["steps"]
        if "min-release-age-exclude" in s.get("run", "")
    )
    runner_temp = tmp_path / "runner-temp"
    runner_temp.mkdir()
    tools = tmp_path / "tools"
    executable(
        tools / "npm",
        """
import json
import os
import sys
if sys.argv[1:3] == ["config", "list"]:
    config = {}
    if os.environ["SUPPORTED"] == "yes":
        config["min-release-age"] = None
    print(json.dumps(config))
elif sys.argv[1:3] == ["config", "get"]:
    if sys.argv[3] == os.environ["FAILED_GET"]:
        sys.exit(1)
    values = {
        "min-release-age": os.environ["AGE"],
        "before": os.environ["BEFORE"],
        "min-release-age-exclude": os.environ["EXCLUSIONS"],
    }
    print(values[sys.argv[3]])
else:
    sys.exit(1)
""",
    )
    result = run_step(
        step,
        cwd=tmp_path,
        env={
            "PATH": fixture_path(tools, "bash", "env", "python3"),
            "RUNNER_TEMP": str(runner_temp),
            "SUPPORTED": "yes",
            "AGE": "3",
            "BEFORE": "null",
            "EXCLUSIONS": "",
            "FAILED_GET": "",
            **override,
        },
        bindings={},
    )
    assert (result.returncode == 0) is (not override), result.stderr


# HK scope drift and retired workflow routes


def test_retired_buddy_routes_stay_absent_and_v3_buddy_stays_dedicated():
    """Retired v1 Buddy entries have no compatibility route."""
    workflows = REPO_ROOT / ".github/workflows"
    for name in RETIRED_BUDDY_WORKFLOWS:
        for suffix in (".yml", ".yaml"):
            assert not (workflows / (name + suffix)).exists(), name + suffix
    buddy = _workflow("workflow-delivery-v3-npm-release")
    assert "workflow_dispatch" in buddy.get("on", buddy.get(True))
    assert not any("uses" in job for job in buddy["jobs"].values())


def test_hk_scope_drift_reads_the_profiles_ci_runs():
    """Only profiles on ci.yml hk check commands count."""
    hk_scope_drift = load_script(REPO_ROOT / "eng/scripts/hk_scope_drift.py")
    text = (REPO_ROOT / ".github/workflows/ci.yml").read_text()
    assert {"small", "medium", "large"} <= hk_scope_drift.ci_profiles(text)
    assert hk_scope_drift.ci_profiles("run: other --profile x\n") == set()
