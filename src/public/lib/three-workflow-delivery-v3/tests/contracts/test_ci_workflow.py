"""Contract tests for the bounded Workflow Delivery v3 CI workflow."""

from __future__ import annotations

import re
from pathlib import Path
from typing import Any, cast

import yaml

REPO_ROOT = Path(__file__).resolve().parents[6]
WORKFLOW = REPO_ROOT / ".github/workflows/workflow-delivery-v3-ci.yml"
HK_CONFIG = REPO_ROOT / "hk.pkl"
STATIC_LANES = (
    "root-hk",
    "project-build",
    "project-test",
    "npm-artifact-build",
)
CHECK_NAME = "Workflow Delivery v3 / hcoona-release-smoke-npm (shadow)"
MAX_WORKFLOW_LINES = 700
RETENTION_DAYS = 45
FROZEN_INSTALL_COUNT = 2
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


def test_workflow_is_bounded_and_exposes_only_approved_events() -> None:
    """Retain one small shadow/manual workflow with no operator scope input."""
    document = _document()

    assert (
        len(WORKFLOW.read_text(encoding="utf-8").splitlines())
        <= MAX_WORKFLOW_LINES
    )
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


def test_job_dag_is_exactly_request_discovery_plan_lanes_finalizer() -> None:
    """Keep the approved static topology and one always-run stable final job."""
    jobs = _document()["jobs"]

    assert set(jobs) == {
        "request",
        "discover-node",
        "plan",
        *STATIC_LANES,
        "required-finalizer",
    }
    assert "needs" not in jobs["request"]
    assert jobs["discover-node"]["needs"] == "request"
    assert jobs["plan"]["needs"] == "discover-node"
    assert all(jobs[lane]["needs"] == "plan" for lane in STATIC_LANES)
    assert set(jobs["required-finalizer"]["needs"]) == {
        "plan",
        *STATIC_LANES,
    }
    assert jobs["required-finalizer"]["if"] == "always()"
    assert jobs["required-finalizer"]["name"] == CHECK_NAME


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
    assert '"+${TARGET_REF}:refs/remotes/origin/wdv3-target"' in checkout
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


def test_no_job_uploads_and_downloads_its_own_artifact() -> None:
    """Keep transport only where another job genuinely consumes the payload."""
    for job in _document()["jobs"].values():
        uploads = [step for step in _steps(job) if step.get("uses") == UPLOAD]
        downloads = [
            step for step in _steps(job) if step.get("uses") == DOWNLOAD
        ]
        if uploads and downloads:
            assert all(
                "steps." not in step["with"]["artifact-ids"]
                for step in downloads
            )


def test_static_lanes_form_evidence_or_empty_results() -> None:
    """Close every static lane through the thin Plan-bound lane command."""
    jobs = _document()["jobs"]
    root = _run(jobs["root-hk"], "Form root-hk lane result")

    assert "ci lane-result" in root
    assert "--lane-id root-hk" in root
    assert '--outcome "${{ steps.execute.outcome }}"' in root
    for lane in STATIC_LANES[1:]:
        job = jobs[lane]
        close_step = next(
            step for step in _steps(job) if "lane result" in step["name"]
        )
        assert "ci lane-result" in close_step["run"]
        assert f"--lane-id {lane}" in close_step["run"]
        assert "--mechanical-result" in close_step["run"]


def test_project_lanes_execute_only_approved_commit4_adapters() -> None:
    """Run the static Build, Test, and npm artifact Adapter lane identities."""
    jobs = _document()["jobs"]

    for lane in STATIC_LANES[1:]:
        adapter = next(
            step
            for step in _steps(jobs[lane])
            if step["name"].startswith("Run ")
        )
        assert adapter["continue-on-error"] is True
        assert "ci node-adapter" in adapter["run"]
        assert f"--lane-id {lane}" in adapter["run"]
    assert (
        '--tarball-output ".wdv3/wdv3-${GITHUB_RUN_ID}-'
        '${GITHUB_RUN_ATTEMPT}-npm-tarball.tgz"'
    ) in _run(
        jobs["npm-artifact-build"],
        "Run npm artifact build Adapter",
    )


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
    close = _run(job, "Form npm artifact lane result")
    finalizer = _run(
        _document()["jobs"]["required-finalizer"],
        "Admit available results and finalize",
    )

    assert steps.index(upload) < next(
        index
        for index, step in enumerate(steps)
        if step["name"] == "Form npm artifact lane result"
    )
    assert upload["with"]["retention-days"] == RETENTION_DAYS
    assert upload["with"]["archive"] is False
    assert upload["with"]["name"].endswith("-npm-tarball.tgz")
    assert "--artifact-id" in close
    assert "steps.upload-tarball.outputs.artifact-id" in close
    assert "--artifact-digest" in close
    assert "steps.upload-tarball.outputs.artifact-digest" in close
    assert "--artifact-name" in close
    assert "wdv3-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}-npm-tarball.tgz" in (
        close
    )
    assert "--artifact-url" in close
    assert "steps.upload-tarball.outputs.artifact-url" in close
    for plan_bound_field in (
        "--output-id",
        "--logical-role",
        "--media-kind",
    ):
        assert plan_bound_field not in close
    assert "upload-tarball" not in finalizer
    assert "npm-tarball.tgz" not in finalizer


def test_project_test_failure_is_carried_to_the_finalizer() -> None:
    """Retain a completed failing test result instead of losing the Evidence."""
    jobs = _document()["jobs"]
    test_job = jobs["project-test"]
    adapter = _run(test_job, "Run project test Adapter")
    close = _run(test_job, "Form project-test lane result")
    final = _run(
        jobs["required-finalizer"],
        "Admit available results and finalize",
    )

    assert "--output .wdv3/project-test-adapter.json" in adapter
    assert "--mechanical-result .wdv3/project-test-adapter.json" in close
    assert "project-test:${TEST_DIGEST}" in final
    assert "ci finalize" in final


def test_root_hk_preserves_incremental_and_manual_consumer_gate_modes() -> None:
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
    command = cast("str", execute["run"])
    admission = cast("str", admit["run"])
    hk = HK_CONFIG.read_text(encoding="utf-8")

    assert provider_download["with"]["artifact-ids"] == (
        "${{ needs.plan.outputs.provider-artifact-id }}"
    )
    assert (
        ".wdv3/input/wdv3-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}-provider.json"
    ) in admission
    assert (
        '--expected-digest "${{ needs.plan.outputs.provider-artifact-digest }}"'
    ) in admission
    assert (
        steps.index(provider_download)
        < steps.index(admit)
        < steps.index(execute)
    )
    assert execute["continue-on-error"] is True
    assert (
        command.count("--frozen-lockfile --ignore-scripts")
        == FROZEN_INSTALL_COUNT
    )
    verification = command.index("if toolchain[name] != os.environ[")
    root_install = command.index(
        "pnpm install --frozen-lockfile --ignore-scripts"
    )
    hexo_install = command.index(
        "pnpm --dir src/public/lib/hexo-renderer-asciidoc/examples/hexo-site"
    )
    incremental_hk = command.index("workflow_delivery_v3_hk.py")
    manual_hk = command.index("mise exec -- hk --no-progress check --all")
    assert (
        command.index('actual_node="$(node --version)"')
        < command.index('actual_pnpm="$(pnpm --version)"')
        < verification
        < root_install
        < hexo_install
        < incremental_hk
        < manual_hk
    )
    assert 'if [[ "${GITHUB_EVENT_NAME}" == "pull_request" ]]' in command
    assert '--from-ref "${BASE_SHA}" --to-ref "${HEAD_SHA}"' in command
    assert lane_result["if"] == "always()"
    assert upload["if"].startswith("always() &&")
    assert steps.index(execute) < steps.index(lane_result) < steps.index(upload)
    assert '["v3-control-pytest"]' in hk
    assert '["hcoona-release-smoke-npm-consumer-policy"]' in hk


def test_finalizer_detects_missing_lane_artifacts() -> None:
    """Let the core Finalizer admit only available Plan-bound lane results."""
    job = _document()["jobs"]["required-finalizer"]
    command = _run(job, "Admit available results and finalize")

    assert 'if [[ -f "${path}" ]]' in command
    assert 'args+=(--lane-result "${path}")' in command
    assert '--plan-digest "${PLAN_DIGEST}"' in command
    assert "--pull-request-number" in command
    assert "github.event.pull_request.number" in command
    assert '--github-api-url "${GITHUB_API_URL}"' in command
    assert "--supersession-state" not in command
    assert '--started-at "${STARTED_AT}"' in command
    assert "--elapsed-seconds" not in command
    assert "date +%s" not in command
    assert '--github-step-summary "${GITHUB_STEP_SUMMARY}"' in command
    assert (
        'decision=".wdv3/wdv3-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}-'
        'ci-slice-decision.json"' in command
    )
    assert (
        'summary=".wdv3/wdv3-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}-'
        'ci-slice-summary.json"' in command
    )
    assert '--decision-output "${decision}"' in command
    assert '--summary-output "${summary}"' in command


def test_finalizer_removes_precoexistence_projection_from_active_workflow() -> (
    None
):
    """Capture canonical finalization without the obsolete projection."""
    job = _document()["jobs"]["required-finalizer"]
    step = next(
        item
        for item in _steps(job)
        if item["name"] == "Admit available results and finalize"
    )
    command = cast("str", step["run"])

    assert step["id"] == "finalize"
    assert "continue-on-error" not in step
    assert {"BASE_SHA", "HEAD_SHA", "TESTED_MERGE_SHA"}.isdisjoint(step["env"])
    assert "ci project-bootstrap-shadow" not in command
    finalize_index = command.index('"${cli[@]}" ci finalize')
    capture_index = command.index("finalizer_exit=$?")
    output_index = command.index(
        'echo "finalizer-exit=${finalizer_exit}" >> "${GITHUB_OUTPUT}"'
    )
    assert command.index("set +e") < finalize_index < capture_index
    assert capture_index < command.index("set -e", capture_index) < output_index
    assert 'exit "${finalizer_exit}"' not in command


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

    assert (
        steps.index(finalize)
        < steps.index(decision)
        < steps.index(summary)
        < steps.index(propagation)
        < steps.index(guard)
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
        assert step["with"] == {
            "name": (
                "wdv3-${{ github.run_id }}-${{ github.run_attempt }}-"
                f"{role}"
            ),
            "path": (
                ".wdv3/wdv3-${{ github.run_id }}-"
                "${{ github.run_attempt }}-"
                f"{role}.json"
            ),
            "if-no-files-found": "error",
            "retention-days": RETENTION_DAYS,
            "overwrite": False,
            "archive": False,
            "include-hidden-files": True,
        }
    assert propagation["if"] == (
        "always() && hashFiles(format('.wdv3/wdv3-{0}-{1}-"
        "ci-slice-decision.json', github.run_id, github.run_attempt)) != ''"
    )
    assert propagation["env"] == {
        "FINALIZER_EXIT": "${{ steps.finalize.outputs.finalizer-exit }}"
    }
    assert propagation["run"] == 'exit "${FINALIZER_EXIT}"\n'


def test_decision_absence_always_writes_noncanonical_contract_summary() -> None:
    """Explain every pre-Decision failure without fabricating a Decision."""
    job = _document()["jobs"]["required-finalizer"]
    step = next(
        item
        for item in _steps(job)
        if item["name"]
        == "Report noncanonical contract failure without a Decision"
    )
    command = cast("str", step["run"])

    assert _steps(job).index(step) == len(_steps(job)) - 1
    assert step["if"] == (
        "always() && hashFiles(format('.wdv3/wdv3-{0}-{1}-"
        "ci-slice-decision.json', github.run_id, github.run_attempt)) == ''"
    )
    assert "GITHUB_STEP_SUMMARY" in command
    assert "Noncanonical contract failure" in command
    assert "no canonical CI Decision was produced" in command
    assert "ci finalize" not in command
    assert "ci-slice-decision.json" not in command
    assert command.rstrip().endswith("exit 1")


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
