"""Contract tests for the bounded Workflow Delivery v3 CI workflow."""

from __future__ import annotations

import json
import os
import re
from pathlib import Path
from typing import TYPE_CHECKING, Any, cast

import pytest
import yaml

from .workflow_shell import executable, resolve, run_step

if TYPE_CHECKING:
    import subprocess

REPO_ROOT = Path(__file__).resolve().parents[6]
WORKFLOW = REPO_ROOT / ".github/workflows/workflow-delivery-v3-ci.yml"
STATIC_LANES = (
    "root-hk",
    "project-build",
    "project-test",
    "npm-artifact-build",
)
CHECK_NAME = "Workflow Delivery v3 / hcoona-release-smoke-npm (shadow)"
RETENTION_DAYS = 45
CHILD_FAILURE = 73
CHECKOUT = "actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1"
UV = "astral-sh/setup-uv@20cfd1bf945f4377ade1205e4dbc17946fc9a30d"
MISE = "jdx/mise-action@3c2e0cf82a5b2e5249f0d3635a4d83d0ae861518"
UPLOAD = "actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a"
DOWNLOAD = "actions/download-artifact@3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c"


def _document() -> dict[str, Any]:
    document = yaml.safe_load(WORKFLOW.read_text(encoding="utf-8"))
    assert isinstance(document, dict)
    return document


def _events(document: dict[str, Any]) -> dict[str, Any]:
    events = cast("dict[Any, Any]", document).get("on")
    if events is None:
        events = cast("dict[Any, Any]", document).get(True)
    assert isinstance(events, dict)
    return events


def _steps(job: dict[str, Any]) -> list[dict[str, Any]]:
    steps = job["steps"]
    assert isinstance(steps, list)
    return steps


def _run(job: dict[str, Any], name: str) -> str:
    step = next(item for item in _steps(job) if item["name"] == name)
    command = step["run"]
    assert isinstance(command, str)
    return command


def _uses_steps(document: dict[str, Any]) -> list[dict[str, Any]]:
    return [
        step
        for job in document["jobs"].values()
        for step in _steps(job)
        if "uses" in step
    ]


class WorkflowBoundary:
    """Record external commands while executing the workflow's real shell."""

    def __init__(self, root: Path) -> None:
        """Provide distinct identities and controlled external commands."""
        self.root = root
        self.document = _document()
        self.bindings = {
            (
                "github.event_name == 'pull_request' && "
                "'ci-pr-slice-shadow' || 'slice-validation'"
            ): "ci-pr-slice-shadow",
            (
                "github.event_name == 'pull_request' && format('pr-{0}', "
                "github.event.pull_request.number) || "
                "format('slice-validation-{0}', github.run_id)"
            ): "pr-712",
            "github.event.pull_request.base.sha": "a" * 40,
            "github.event.pull_request.head.sha": "b" * 40,
            "github.event.pull_request.number": "712",
            "github.run_id": "9031",
            "github.run_attempt": "2",
            "needs.plan.outputs.plan-digest": "sha256:" + "1" * 64,
            "needs.plan.outputs.plan-artifact-digest": "2" * 64,
            "needs.plan.outputs.started-at": "1789600000",
            **{
                f"needs.{lane}.outputs.artifact-digest": str(index) * 64
                for index, lane in enumerate(STATIC_LANES, start=3)
            },
        }
        self.env = {
            "PATH": f"{root / 'bin'}{os.pathsep}{os.environ['PATH']}",
            "GITHUB_RUN_ID": "9031",
            "GITHUB_RUN_ATTEMPT": "2",
            "GITHUB_EVENT_NAME": "pull_request",
            "GITHUB_API_URL": "https://api.github.com",
            "GITHUB_OUTPUT": str(root / "outputs"),
            "GITHUB_STEP_SUMMARY": str(root / "summary"),
            "COMMAND_LOG": str(root / "commands.jsonl"),
        }
        recorder = """import json, os, sys
from pathlib import Path
command = [Path(sys.argv[0]).name, *sys.argv[1:]]
with open(os.environ["COMMAND_LOG"], "a", encoding="utf-8") as stream:
    stream.write(json.dumps({"argv": command, "cwd": os.getcwd()}) + "\\n")
if command == ["node", "--version"]:
    print(os.environ.get("NODE_VERSION", "v24.1.0"))
elif command == ["pnpm", "--version"]:
    print(os.environ.get("PNPM_VERSION", "10.1.0"))
if "node-adapter" in command:
    for flag in ("--output", "--tarball-output"):
        if flag in command:
            output = Path(command[command.index(flag) + 1])
            output.parent.mkdir(parents=True, exist_ok=True)
            output.write_text("adapter-produced-bytes", encoding="utf-8")
if "finalize" in command:
    for flag in ("--decision-output", "--summary-output"):
        output = Path(command[command.index(flag) + 1])
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text("finalizer-produced-bytes", encoding="utf-8")
    sys.exit(int(os.environ.get("FINALIZER_STATUS", "0")))
failure = os.environ.get("FAIL_COMMAND")
if failure and failure in " ".join(command):
    sys.exit(73)
"""
        for name in ("uv", "mise", "node", "pnpm"):
            executable(root / "bin" / name, recorder)

    def step(self, job: str, name: str) -> dict[str, Any]:
        """Find one consumed workflow step."""
        return next(
            step
            for step in self.document["jobs"][job]["steps"]
            if step["name"] == name
        )

    def run(self, job: str, name: str) -> subprocess.CompletedProcess[str]:
        """Execute one checked-in body with its effective environment."""
        return run_step(
            self.step(job, name),
            cwd=self.root,
            env=self.env,
            bindings=self.bindings,
            workflow=self.document,
            job=self.document["jobs"][job],
        )

    def calls(self) -> list[list[str]]:
        """Read commands and require execution in the fixture repository."""
        log = Path(self.env["COMMAND_LOG"])
        if not log.exists():
            return []
        observations = [
            json.loads(line) for line in log.read_text().splitlines()
        ]
        assert all(Path(item["cwd"]) == self.root for item in observations)
        return [item["argv"] for item in observations]

    def payload(self, role: str, *, directory: str = "final") -> Path:
        """Place a downloaded payload at the workflow transport boundary."""
        path = self.root / f".wdv3/{directory}/wdv3-9031-2-{role}.json"
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text("{}", encoding="utf-8")
        return path


def _option(command: list[str], name: str) -> str:
    return command[command.index(name) + 1]


def _assert_ci_command(command: list[str], operation: str) -> None:
    assert command[0] == "uv"
    assert _option(command, "--package") == "three-workflow-delivery-v3"
    cli = command.index(
        "three-workflow-delivery-v3", command.index("--package") + 2
    )
    assert command[cli + 1 : cli + 3] == ["ci", operation]


def _assert_lane_binding(
    boundary: WorkflowBoundary,
    command: list[str],
    lane: str,
    plan: Path,
) -> None:
    _assert_ci_command(command, "lane-result")
    assert boundary.root / _option(command, "--plan") == plan
    assert (
        _option(command, "--plan-digest")
        == boundary.bindings["needs.plan.outputs.plan-digest"]
    )
    assert _option(command, "--lane-id") == lane
    upload = next(
        step
        for step in _steps(boundary.document["jobs"][lane])
        if step.get("id") == "upload"
    )
    assert _option(command, "--output") == resolve(
        upload["with"]["path"], boundary.bindings
    )


@pytest.fixture
def boundary(tmp_path: Path) -> WorkflowBoundary:
    """Provide an isolated command boundary for the current scenario."""
    return WorkflowBoundary(tmp_path)


def _provider(boundary: WorkflowBoundary) -> None:
    boundary.payload("provider", directory="input").write_text(
        json.dumps(
            {"provider": {"toolchain": {"node": "v24.1.0", "pnpm": "10.1.0"}}}
        ),
        encoding="utf-8",
    )


@pytest.mark.parametrize("event", ["pull_request", "workflow_dispatch"])
def test_root_hk_executes_admitted_toolchain_and_selected_mode(
    boundary: WorkflowBoundary,
    event: str,
) -> None:
    """Prepare the admitted toolchain before incremental or full HK."""
    _provider(boundary)
    boundary.env["GITHUB_EVENT_NAME"] = event
    result = boundary.run(
        "root-hk", "Run permanent root HK and static-reference policy"
    )
    assert result.returncode == 0, result.stderr
    calls = boundary.calls()
    hk = next(call for call in calls if "hk" in call)
    preparation = [
        ["mise", "run", "prepare:static-reference-authorities"],
        ["mise", "run", "bootstrap:dotnet-tools"],
        [
            "pnpm",
            "--dir",
            "src/public/lib/hexo-renderer-asciidoc/examples/hexo-site",
            "install",
            "--frozen-lockfile",
            "--ignore-scripts",
        ],
    ]
    for command in preparation:
        assert calls.index(command) < calls.index(hk)
        for version in (["node", "--version"], ["pnpm", "--version"]):
            assert calls.index(version) < calls.index(command)
    assert (
        _option(hk, "--skip-step") == "static-reference-authority-preparation"
    )
    assert "check" in hk
    if event == "pull_request":
        assert "eng/scripts/workflow_delivery_v3_hk.py" in hk
        assert _option(hk, "--repository") == "."
        assert _option(hk, "--from-ref") == "a" * 40
        assert _option(hk, "--to-ref") == "b" * 40
        assert hk[hk.index("--") + 1 : hk.index("--") + 5] == [
            "mise",
            "exec",
            "--",
            "hk",
        ]
        assert "--all" not in hk
    else:
        assert hk[:4] == ["mise", "exec", "--", "hk"]
        assert "--all" in hk


@pytest.mark.parametrize("tool", ["node", "pnpm"])
def test_root_hk_rejects_toolchain_mismatch(
    boundary: WorkflowBoundary, tool: str
) -> None:
    """Reject either mismatched tool before preparation or HK."""
    _provider(boundary)
    boundary.env[f"{tool.upper()}_VERSION"] = "wrong-version"
    result = boundary.run(
        "root-hk", "Run permanent root HK and static-reference policy"
    )
    assert result.returncode != 0
    assert f"{tool} differs from the admitted Provider fact" in result.stderr
    assert all(
        call in (["node", "--version"], ["pnpm", "--version"])
        for call in boundary.calls()
    )


@pytest.mark.parametrize(
    ("event", "failure"),
    [
        ("pull_request", "mise run prepare:static-reference-authorities"),
        ("pull_request", "mise run bootstrap:dotnet-tools"),
        ("pull_request", "install --frozen-lockfile"),
        ("pull_request", "workflow_delivery_v3_hk.py"),
        ("workflow_dispatch", "mise exec -- hk"),
    ],
)
def test_root_hk_propagates_boundary_failure(
    boundary: WorkflowBoundary, event: str, failure: str
) -> None:
    """Preserve failure at each distinct prerequisite and HK boundary."""
    _provider(boundary)
    boundary.env["GITHUB_EVENT_NAME"] = event
    boundary.env["FAIL_COMMAND"] = failure
    result = boundary.run(
        "root-hk", "Run permanent root HK and static-reference policy"
    )
    assert result.returncode == CHILD_FAILURE
    assert failure in " ".join(boundary.calls()[-1])


@pytest.mark.parametrize("failure", [None, "plan", "provider"])
def test_root_hk_admits_exact_plan_and_provider(
    boundary: WorkflowBoundary,
    failure: str | None,
) -> None:
    """Bind both downloaded inputs to their own artifact digests."""
    boundary.bindings["needs.plan.outputs.provider-artifact-digest"] = "8" * 64
    expected = {
        boundary.payload("plan", directory="input"): boundary.bindings[
            "needs.plan.outputs.plan-artifact-digest"
        ],
        boundary.payload("provider", directory="input"): "8" * 64,
    }
    if failure:
        boundary.env["FAIL_COMMAND"] = f"wdv3-9031-2-{failure}.json"
    result = boundary.run("root-hk", "Admit root-HK inputs")
    assert result.returncode == (CHILD_FAILURE if failure else 0), result.stderr
    calls = boundary.calls()
    for call in calls:
        _assert_ci_command(call, "admit-payload")
    if failure:
        assert _option(calls[-1], "--input").endswith(f"-{failure}.json")
        if failure == "plan":
            expected = {
                path: digest
                for path, digest in expected.items()
                if path.name.endswith("-plan.json")
            }
    assert {
        boundary.root / _option(call, "--input"): _option(
            call, "--expected-digest"
        )
        for call in calls
    } == expected


@pytest.mark.parametrize("outcome", ["success", "failure", None])
def test_root_hk_closes_selected_outcome_or_empty_lane(
    boundary: WorkflowBoundary,
    outcome: str | None,
) -> None:
    """Carry HK status or close an unselected lane without an outcome."""
    plan = boundary.payload("plan", directory="input")
    boundary.bindings.update(
        {
            "needs.plan.outputs.root-hk-selected": str(
                outcome is not None
            ).lower(),
            "steps.execute.outcome": outcome or "skipped",
        }
    )
    result = boundary.run("root-hk", "Form root-hk lane result")
    assert result.returncode == 0, result.stderr
    [command] = boundary.calls()
    _assert_lane_binding(boundary, command, "root-hk", plan)
    if outcome is None:
        assert "--outcome" not in command
    else:
        assert _option(command, "--outcome") == outcome


@pytest.mark.parametrize(
    ("lanes", "event", "status"),
    [
        (STATIC_LANES, "pull_request", 0),
        (("root-hk", "project-test"), "pull_request", 73),
        ((), "workflow_dispatch", 0),
    ],
)
def test_finalizer_admits_available_payloads_and_preserves_exit(
    boundary: WorkflowBoundary,
    lanes: tuple[str, ...],
    event: str,
    status: int,
) -> None:
    """Admit exact available inputs and retain output before propagation."""
    plan = boundary.payload("plan")
    lane_paths = {lane: boundary.payload(f"{lane}-result") for lane in lanes}
    boundary.env.update(GITHUB_EVENT_NAME=event, FINALIZER_STATUS=str(status))
    result = boundary.run(
        "required-finalizer", "Admit available results and finalize"
    )
    assert result.returncode == 0, result.stderr
    calls = boundary.calls()
    final = next(call for call in calls if "finalize" in call)
    admissions = [call for call in calls if "admit-payload" in call]
    _assert_ci_command(final, "finalize")
    for call in admissions:
        _assert_ci_command(call, "admit-payload")
    expected = {
        plan: boundary.bindings["needs.plan.outputs.plan-artifact-digest"],
        **{
            path: boundary.bindings[f"needs.{lane}.outputs.artifact-digest"]
            for lane, path in lane_paths.items()
        },
    }
    assert {
        boundary.root / _option(call, "--input"): _option(
            call, "--expected-digest"
        )
        for call in admissions
    } == expected
    assert all(calls.index(call) < calls.index(final) for call in admissions)
    assert boundary.root / _option(final, "--plan") == plan
    assert (
        _option(final, "--plan-digest")
        == boundary.bindings["needs.plan.outputs.plan-digest"]
    )
    assert {
        boundary.root / final[index + 1]
        for index, value in enumerate(final)
        if value == "--lane-result"
    } == set(lane_paths.values())
    assert (
        _option(final, "--started-at")
        == boundary.bindings["needs.plan.outputs.started-at"]
    )
    assert (
        _option(final, "--github-step-summary")
        == boundary.env["GITHUB_STEP_SUMMARY"]
    )
    assert {"--supersession-state", "--elapsed-seconds"}.isdisjoint(final)
    if event == "pull_request":
        assert _option(final, "--pull-request-number") == "712"
        assert _option(final, "--github-api-url") == "https://api.github.com"
    else:
        assert {"--pull-request-number", "--github-api-url"}.isdisjoint(final)
    outputs = dict(
        line.split("=", 1)
        for line in Path(boundary.env["GITHUB_OUTPUT"]).read_text().splitlines()
    )
    boundary.bindings["steps.finalize.outputs.finalizer-exit"] = outputs[
        "finalizer-exit"
    ]
    propagated = boundary.run(
        "required-finalizer", "Propagate canonical CI Slice Decision"
    )
    assert propagated.returncode == status
    for flag, name in (
        ("--decision-output", "Upload canonical CI Slice Decision"),
        ("--summary-output", "Upload canonical CI Slice Summary"),
    ):
        produced = boundary.root / _option(final, flag)
        upload = boundary.step("required-finalizer", name)
        assert produced == boundary.root / resolve(
            upload["with"]["path"], boundary.bindings
        )
        assert produced.read_text() == "finalizer-produced-bytes"


@pytest.mark.parametrize("role", ["plan", "project-test-result"])
def test_finalizer_stops_on_admission_failure(
    boundary: WorkflowBoundary, role: str
) -> None:
    """Prevent finalization after a rejected Plan or lane payload."""
    boundary.payload("plan")
    boundary.payload("project-test-result")
    boundary.env["FAIL_COMMAND"] = f"wdv3-9031-2-{role}.json"
    result = boundary.run(
        "required-finalizer", "Admit available results and finalize"
    )
    assert result.returncode == CHILD_FAILURE
    assert not any("finalize" in call for call in boundary.calls())
    assert not Path(boundary.env["GITHUB_OUTPUT"]).exists()


def test_decision_absence_reports_contract_failure(
    boundary: WorkflowBoundary,
) -> None:
    """Explain the missing Decision without manufacturing a record."""
    result = boundary.run(
        "required-finalizer",
        "Report noncanonical contract failure without a Decision",
    )
    assert result.returncode == 1
    summary = Path(boundary.env["GITHUB_STEP_SUMMARY"]).read_text()
    assert "Noncanonical contract failure" in summary
    assert "no canonical CI Decision was produced" in summary
    assert not boundary.calls()
    assert not (boundary.root / ".wdv3").exists()


@pytest.mark.parametrize(
    ("lane", "status"),
    [("project-build", 0), ("project-test", 73), ("npm-artifact-build", 0)],
)
def test_node_lane_closes_the_adapter_output(
    boundary: WorkflowBoundary, lane: str, status: int
) -> None:
    """Pass the actual adapter output into its Plan-bound lane closure."""
    plan = boundary.payload("plan", directory="input")
    context = boundary.payload("adapter-context", directory="input")
    boundary.bindings[f"needs.plan.outputs.{lane}-selected"] = "true"
    boundary.bindings.update(
        {
            f"steps.upload-tarball.outputs.{field}": ""
            for field in ("artifact-id", "artifact-url", "artifact-digest")
        }
    )
    if status:
        boundary.env["FAIL_COMMAND"] = "node-adapter"
    job = boundary.document["jobs"][lane]
    adapter = next(
        step for step in _steps(job) if step["name"].startswith("Run ")
    )
    close = next(step for step in _steps(job) if "lane result" in step["name"])
    assert adapter["if"] == f"needs.plan.outputs.{lane}-selected == 'true'"
    assert adapter["continue-on-error"] is True
    assert close["if"] == "always()"
    assert _steps(job).index(adapter) < _steps(job).index(close)
    result = boundary.run(lane, adapter["name"])
    assert result.returncode == status, result.stderr
    result = boundary.run(lane, close["name"])
    assert result.returncode == 0, result.stderr
    adapter_call, closure = boundary.calls()
    _assert_ci_command(adapter_call, "node-adapter")
    _assert_lane_binding(boundary, closure, lane, plan)
    assert boundary.root / _option(adapter_call, "--plan") == plan
    assert _option(adapter_call, "--lane-id") == lane
    assert (
        _option(adapter_call, "--plan-digest")
        == boundary.bindings["needs.plan.outputs.plan-digest"]
    )
    assert boundary.root / _option(adapter_call, "--adapter-context") == context
    assert _option(adapter_call, "--output") == _option(
        closure, "--mechanical-result"
    )
    assert (
        boundary.root / _option(closure, "--mechanical-result")
    ).read_text() == "adapter-produced-bytes"
    if lane == "npm-artifact-build":
        upload = boundary.step(lane, "Upload retained npm tarball")
        assert _option(adapter_call, "--tarball-output") == resolve(
            upload["with"]["path"], boundary.bindings
        )


@pytest.mark.parametrize(
    ("selected", "uploaded"),
    [(True, True), (True, False), (False, False), (False, True)],
)
def test_npm_lane_binds_uploaded_artifact_identity(
    boundary: WorkflowBoundary, *, selected: bool, uploaded: bool
) -> None:
    """Bind uploaded identity only for selected work with an artifact."""
    lane = "npm-artifact-build"
    plan = boundary.payload("plan", directory="input")
    boundary.bindings[f"needs.plan.outputs.{lane}-selected"] = str(
        selected
    ).lower()
    identity = {
        "artifact-id": "8129",
        "artifact-url": "https://github.com/hcoona/three/actions/runs/9031/artifacts/8129",
        "artifact-digest": "9" * 64,
    }
    boundary.bindings.update(
        {
            f"steps.upload-tarball.outputs.{key}": value if uploaded else ""
            for key, value in identity.items()
        }
    )
    result = boundary.run(lane, "Form npm artifact lane result")
    assert result.returncode == 0, result.stderr
    [command] = boundary.calls()
    _assert_lane_binding(boundary, command, lane, plan)
    assert ("--mechanical-result" in command) is selected
    if uploaded and selected:
        for key, value in identity.items():
            assert _option(command, f"--{key}") == value
        upload = boundary.step(lane, "Upload retained npm tarball")
        assert _option(command, "--artifact-name") == resolve(
            upload["with"]["name"], boundary.bindings
        )
    else:
        assert {
            "--artifact-id",
            "--artifact-name",
            "--artifact-url",
            "--artifact-digest",
        }.isdisjoint(command)
    assert {"--output-id", "--logical-role", "--media-kind"}.isdisjoint(command)


@pytest.mark.parametrize("lane", ["project-build", "project-test"])
def test_unselected_node_lane_closes_without_adapter_output(
    boundary: WorkflowBoundary,
    lane: str,
) -> None:
    """An unselected lane must not consume a nonexistent mechanical result."""
    plan = boundary.payload("plan", directory="input")
    boundary.bindings[f"needs.plan.outputs.{lane}-selected"] = "false"
    result = boundary.run(lane, f"Form {lane} lane result")
    assert result.returncode == 0, result.stderr
    [command] = boundary.calls()
    _assert_lane_binding(boundary, command, lane, plan)
    assert "--mechanical-result" not in command


def test_workflow_exposes_only_approved_events_and_permissions() -> None:
    """Keep shadow/manual entry and its minimal authority boundary."""
    document = _document()

    assert _events(document) == {
        "pull_request": None,
        "workflow_dispatch": None,
    }
    assert document["permissions"] == {"contents": "read"}
    assert {
        name: job["permissions"]
        for name, job in document["jobs"].items()
        if "permissions" in job
    } == {"request": {"actions": "read"}}


def test_concurrency_binds_pr_number_or_manual_target_sha() -> None:
    """Supersede only the same PR or the same selected manual target."""
    concurrency = _document()["concurrency"]

    assert concurrency["cancel-in-progress"] is True
    assert "wdv3-ci-pr-{0}" in concurrency["group"]
    assert "github.event.pull_request.number" in concurrency["group"]
    assert "wdv3-ci-slice-validation-{0}" in concurrency["group"]
    assert "github.sha" in concurrency["group"]


def _dependencies(job: dict[str, Any]) -> set[str]:
    needs = job.get("needs", [])
    return {needs} if isinstance(needs, str) else set(needs)


def _ancestors(jobs: dict[str, Any], job_id: str) -> set[str]:
    ancestors: set[str] = set()
    pending = _dependencies(jobs[job_id])
    while pending:
        dependency = pending.pop()
        assert dependency in jobs, f"Unknown dependency: {dependency}"
        assert dependency != job_id, f"Cyclic dependency: {job_id}"
        if dependency not in ancestors:
            ancestors.add(dependency)
            pending.update(_dependencies(jobs[dependency]))
    return ancestors


def test_qualification_precedes_one_stable_always_run_final_check() -> None:
    """Wait for qualification inputs and results before the stable check."""
    jobs = _document()["jobs"]
    final_checks = [
        job for job in jobs.values() if job.get("name") == CHECK_NAME
    ]

    assert final_checks == [jobs["required-finalizer"]]
    assert final_checks[0]["if"] == "always()"
    assert "request" in _ancestors(jobs, "discover-node")
    assert {"request", "discover-node"} <= _ancestors(jobs, "plan")
    for lane in STATIC_LANES:
        assert "plan" in _ancestors(jobs, lane)
    assert {"plan", *STATIC_LANES} <= _ancestors(jobs, "required-finalizer")

    # GitHub exposes output/result bindings only for direct dependencies.
    for job in jobs.values():
        referenced_jobs = set(
            re.findall(r"\bneeds\.([\w-]+)\.", json.dumps(job))
        )
        assert referenced_jobs <= _dependencies(job)


def test_candidate_uses_exact_pr_range_and_tested_merge_target() -> None:
    """Bind paths to base/head while checkout and execution use github.sha."""
    jobs = _document()["jobs"]
    request = jobs["request"]
    steps = _steps(request)
    checkout = _run(
        request,
        "Check out tested merge candidate without credentials",
    )
    command = _run(request, "Form exact candidate and comparison")
    clock = _run(request, "Record platform workflow creation")
    metadata = next(step for step in steps if step.get("id") == "clock")
    setup_uv = next(step for step in steps if step.get("uses") == UV)

    assert "git fetch --force --tags --no-recurse-submodules origin" in checkout
    assert '"+${TARGET_SHA}:refs/remotes/origin/wdv3-target"' in checkout
    assert "git checkout --detach refs/remotes/origin/wdv3-target" in checkout
    assert 'test "$(git rev-parse HEAD)" = "${TARGET_SHA}"' in checkout
    assert "eng/scripts/workflow_delivery_v3_run_created_epoch.py" in clock
    assert "/actions/runs/" not in clock
    assert "json.load" not in clock
    assert "date --date=" not in clock
    assert "curl" not in clock
    assert "ACTIONS_RUNTIME_TOKEN" not in clock
    assert "ACTIONS_RESULTS_URL" not in clock
    assert (
        WORKFLOW.read_text(encoding="utf-8").count("${{ github.token }}") == 1
    )
    assert metadata["env"] == {"WDV3_GITHUB_TOKEN": "${{ github.token }}"}
    assert setup_uv["with"]["github-token"] == ""
    assert '--base-sha "${BASE_SHA}"' in command
    assert '--head-sha "${HEAD_SHA}"' in command
    assert '--target "${GITHUB_SHA}"' in command
    assert '--selected-ref "${GITHUB_REF}"' in command
    assert "workflow_delivery_v3_hk.py" not in command
    assert "ci candidate" in command


def test_discovery_and_plan_reuse_same_revision_core_apis() -> None:
    """Consume the Provider Result once and close the model-backed Plan."""
    jobs = _document()["jobs"]
    discover = _run(jobs["discover-node"], "Produce exact Provider Result")
    plan = _run(jobs["plan"], "Compile model and close Plan")

    assert "repository provide-node" in discover
    assert '--target "${GITHUB_SHA}"' in discover
    assert "--compiler-producer plan" in discover
    assert "--provider-producer discover-node" in discover
    assert "ci plan" in plan
    assert "--request-artifact-id" in plan
    assert "--request-artifact-digest" in plan
    assert "--provider-artifact-id" in plan
    assert "--provider-artifact-digest" in plan
    assert jobs["plan"]["outputs"]["provider-artifact-id"] == (
        "${{ needs.discover-node.outputs.provider-artifact-id }}"
    )
    assert jobs["plan"]["outputs"]["provider-artifact-digest"] == (
        "${{ needs.discover-node.outputs.provider-artifact-digest }}"
    )


def test_actions_are_full_sha_pinned_with_current_version_comments() -> None:
    """Reject tags, branches, and pins without reviewable version comments."""
    raw = WORKFLOW.read_text(encoding="utf-8")
    uses_lines = [
        line.strip()
        for line in raw.splitlines()
        if line.strip().startswith("uses:")
    ]
    pattern = re.compile(
        r"uses:\s+[^@\s]+@[0-9a-f]{40}\s+#\s+v[0-9][0-9A-Za-z.-]*\Z"
    )

    assert uses_lines
    assert all(pattern.fullmatch(line) for line in uses_lines)
    assert {step["uses"] for step in _uses_steps(_document())} == {
        CHECKOUT,
        UV,
        MISE,
        UPLOAD,
        DOWNLOAD,
    }


def test_stock_raw_artifacts_propagate_exact_ids_and_digests() -> None:
    """Use immutable raw stock artifacts and verify their upload digests."""
    document = _document()
    upload_steps = [
        step for step in _uses_steps(document) if step["uses"] == UPLOAD
    ]
    download_steps = [
        step for step in _uses_steps(document) if step["uses"] == DOWNLOAD
    ]
    raw = WORKFLOW.read_text(encoding="utf-8")

    assert {step["name"] for step in upload_steps} == {
        "Upload request",
        "Upload Provider Result",
        "Upload Plan",
        "Upload Adapter context",
        "Upload root-hk lane result",
        "Upload project-build lane result",
        "Upload project-test lane result",
        "Upload retained npm tarball",
        "Upload npm artifact lane result",
        "Upload canonical CI Slice Decision",
        "Upload canonical CI Slice Summary",
    }
    for step in upload_steps:
        settings = step["with"]
        assert settings["retention-days"] == RETENTION_DAYS
        assert settings["archive"] is False
        assert settings["include-hidden-files"] is True
        assert settings["if-no-files-found"] == "error"
        assert "${{ github.run_id }}" in settings["name"]
        assert "${{ github.run_attempt }}" in settings["name"]
        assert "${{ github.run_id }}" in settings["path"]
        assert "${{ github.run_attempt }}" in settings["path"]
    for step in download_steps:
        settings = step["with"]
        assert "artifact-ids" in settings
        assert settings["skip-decompress"] is True
        assert settings["digest-mismatch"] == "error"
        assert "name" not in settings
        assert "github-token" not in settings
        assert "steps." not in settings["artifact-ids"]
    assert "outputs.artifact-id" in raw
    assert "outputs.artifact-digest" in raw
    assert "ci admit-payload" in raw
    assert "--expected-digest" in raw


def test_adapter_context_is_emitted_and_used_only_for_ready_selected_work() -> (
    None
):
    """Let blocked Plans close empty lanes without an Adapter context."""
    jobs = _document()["jobs"]
    plan_steps = _steps(jobs["plan"])
    upload = next(
        step for step in plan_steps if step["name"] == "Upload Adapter context"
    )

    assert jobs["plan"]["outputs"]["plan-ready"] == (
        "${{ steps.plan.outputs.plan-ready }}"
    )
    assert upload["if"] == (
        "steps.plan.outputs.plan-ready == 'true' && "
        "hashFiles(format('.wdv3/wdv3-{0}-{1}-adapter-context.json', "
        "github.run_id, github.run_attempt)) != ''"
    )
    for lane in STATIC_LANES[1:]:
        steps = _steps(jobs[lane])
        download = next(
            step for step in steps if step["name"] == "Download Adapter context"
        )
        admit = _run(jobs[lane], "Admit inputs")
        assert download["if"] == (
            f"needs.plan.outputs.{lane}-selected == 'true'"
        )
        assert (
            f'if [[ "${{{{ needs.plan.outputs.{lane}-selected }}}}" '
            '== "true" ]]'
        ) in admit


def test_npm_lane_uploads_tarball_before_forming_exact_artifact_evidence() -> (
    None
):
    """Bind the retained raw tarball platform outputs only after upload."""
    job = _document()["jobs"]["npm-artifact-build"]
    steps = _steps(job)
    upload = next(
        step for step in steps if step["name"] == "Upload retained npm tarball"
    )
    assert steps.index(upload) < next(
        index
        for index, step in enumerate(steps)
        if step["name"] == "Form npm artifact lane result"
    )
    assert upload["with"]["name"].endswith("-npm-tarball.tgz")


def test_root_hk_preserves_incremental_and_full_index_modes() -> None:
    """Materialize admitted toolchain dependencies inside the HK boundary."""
    root = _document()["jobs"]["root-hk"]
    steps = _steps(root)
    provider_download = next(
        step
        for step in steps
        if step["name"] == "Download Provider Result by artifact ID"
    )
    admit = next(
        step for step in steps if step["name"] == "Admit root-HK inputs"
    )
    execute = next(step for step in steps if step.get("id") == "execute")
    lane_result = next(
        step for step in steps if step["name"] == "Form root-hk lane result"
    )
    upload = next(
        step for step in steps if step["name"] == "Upload root-hk lane result"
    )

    assert provider_download["with"]["artifact-ids"] == (
        "${{ needs.plan.outputs.provider-artifact-id }}"
    )
    assert (
        steps.index(provider_download)
        < steps.index(admit)
        < steps.index(execute)
    )
    assert execute["continue-on-error"] is True
    assert execute["if"] == "needs.plan.outputs.root-hk-selected == 'true'"
    assert not admit.get("continue-on-error", False)
    assert admit.get("if", "success()") in ("success()", "${{ success() }}")
    assert lane_result["if"] == "always()"
    assert upload["if"].startswith("always() &&")
    assert steps.index(execute) < steps.index(lane_result) < steps.index(upload)


def test_finalizer_persists_canonical_decision_and_summary_before_guard() -> (
    None
):
    """Retain both canonical finalizer records before preserving failure."""
    job = _document()["jobs"]["required-finalizer"]
    steps = _steps(job)
    finalize = next(
        step
        for step in steps
        if step["name"] == "Admit available results and finalize"
    )
    decision = next(
        step
        for step in steps
        if step["name"] == "Upload canonical CI Slice Decision"
    )
    summary = next(
        step
        for step in steps
        if step["name"] == "Upload canonical CI Slice Summary"
    )
    propagation = next(
        step
        for step in steps
        if step["name"] == "Propagate canonical CI Slice Decision"
    )
    guard = next(
        step
        for step in steps
        if step["name"]
        == "Report noncanonical contract failure without a Decision"
    )

    for upload in (decision, summary):
        assert (
            steps.index(finalize)
            < steps.index(upload)
            < steps.index(propagation)
        )
    assert steps.index(propagation) < steps.index(guard)
    assert finalize["id"] == "finalize"
    assert not finalize.get("continue-on-error", False)
    assert {"BASE_SHA", "HEAD_SHA", "TESTED_MERGE_SHA"}.isdisjoint(
        finalize["env"]
    )
    expected = (
        (
            decision,
            "ci-slice-decision",
            (
                "always() && hashFiles(format('.wdv3/wdv3-{0}-{1}-"
                "ci-slice-decision.json', github.run_id, "
                "github.run_attempt)) != ''"
            ),
        ),
        (
            summary,
            "ci-slice-summary",
            (
                "always() && hashFiles(format('.wdv3/wdv3-{0}-{1}-"
                "ci-slice-summary.json', github.run_id, "
                "github.run_attempt)) != ''"
            ),
        ),
    )
    for step, role, condition in expected:
        assert step["if"] == condition
        assert step["uses"] == UPLOAD
        assert step["with"]["name"] == (
            "wdv3-${{ github.run_id }}-${{ github.run_attempt }}-"
            f"{role}"
        )
        assert step["with"]["path"] == (
            ".wdv3/wdv3-${{ github.run_id }}-"
            "${{ github.run_attempt }}-"
            f"{role}.json"
        )
        assert step["with"]["overwrite"] is False
    assert propagation["if"] == (
        "always() && hashFiles(format('.wdv3/wdv3-{0}-{1}-"
        "ci-slice-decision.json', github.run_id, github.run_attempt)) != ''"
    )
    assert propagation["env"] == {
        "FINALIZER_EXIT": "${{ steps.finalize.outputs.finalizer-exit }}"
    }


def test_decision_absence_always_writes_noncanonical_contract_summary() -> None:
    """Explain every pre-Decision failure without fabricating a Decision."""
    job = _document()["jobs"]["required-finalizer"]
    step = next(
        item
        for item in _steps(job)
        if item["name"]
        == "Report noncanonical contract failure without a Decision"
    )
    assert _steps(job).index(step) == len(_steps(job)) - 1
    assert step["if"] == (
        "always() && hashFiles(format('.wdv3/wdv3-{0}-{1}-"
        "ci-slice-decision.json', github.run_id, github.run_attempt)) == ''"
    )


def test_workflow_has_no_transport_credentials_or_commit6_authority() -> None:
    """Keep this workflow shadow-only, credential-minimal, and pre-live."""
    raw = WORKFLOW.read_text(encoding="utf-8")
    lowered = raw.lower()

    for forbidden in (
        "actions_runtime_token",
        "actions_results_url",
        "ci.transport",
        "admit_action_downloaded_artifact",
        "packages:",
        "id-token:",
        "secrets.",
        "environment:",
        "ruleset",
        "final decision",
        "live-release",
    ):
        assert forbidden not in lowered
    assert "python - <<'py'" not in lowered
    assert "python <<'py'" not in lowered
