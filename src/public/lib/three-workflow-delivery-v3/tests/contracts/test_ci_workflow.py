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
    } == {
        "request": {
            "actions": "read",
            "contents": "read",
        }
    }


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
    checkout = next(
        step for step in _steps(request) if step.get("uses") == CHECKOUT
    )
    command = _run(request, "Form exact candidate and comparison")
    clock = _run(request, "Record platform workflow creation")

    assert checkout["with"] == {
        "fetch-depth": 0,
        "persist-credentials": False,
        "ref": "${{ github.sha }}",
    }
    assert "eng/scripts/workflow_delivery_v3_run_created_epoch.py" in clock
    assert "/actions/runs/" not in clock
    assert "json.load" not in clock
    assert "date --date=" not in clock
    assert "curl" not in clock
    assert "ACTIONS_RUNTIME_TOKEN" not in clock
    assert "ACTIONS_RESULTS_URL" not in clock
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
    """Use the exact PR comparison and force internal steps manually."""
    root = _run(
        _document()["jobs"]["root-hk"],
        "Run permanent root HK and consumer policy",
    )
    hk = HK_CONFIG.read_text(encoding="utf-8")

    assert 'if [[ "${GITHUB_EVENT_NAME}" == "pull_request" ]]' in root
    assert '--from-ref "${BASE_SHA}" --to-ref "${HEAD_SHA}"' in root
    assert "workflow_delivery_v3_hk.py" in root
    assert "mise exec -- hk --no-progress check --all" in root
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
    """Keep only canonical finalization in the active workflow."""
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
    set_plus_index = command.index("set +e")
    finalize_index = command.index('"${cli[@]}" ci finalize')
    capture_index = command.index("finalizer_exit=$?")
    set_minus_index = command.index("set -e", capture_index)
    output_index = command.index(
        'echo "finalizer-exit=${finalizer_exit}" >> "${GITHUB_OUTPUT}"'
    )
    assert (
        set_plus_index
        < finalize_index
        < capture_index
        < set_minus_index
        < output_index
    )
    finalizer_summary = '--github-step-summary "${GITHUB_STEP_SUMMARY}"'
    finalizer_summary_end = command.index(
        finalizer_summary, finalize_index
    ) + len(finalizer_summary)
    assert command[finalizer_summary_end:capture_index].strip() == ""
    assert command.rstrip().endswith(
        'echo "finalizer-exit=${finalizer_exit}" >> "${GITHUB_OUTPUT}"'
    )
    assert 'exit "${finalizer_exit}"' not in command
    for forbidden in (
        "pr-552",
        "793c7255",
        "191abc82",
        "dev/shuaizhang/design-workflows",
    ):
        assert forbidden not in command


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
        "github-token:",
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


ROOT_HK_ADMISSION_COUNT = 2
FROZEN_INSTALL_COUNT = 2


def test_request_scopes_github_token_to_metadata_step() -> None:
    """Expose the platform token only to the governed metadata lookup."""
    document = _document()
    github_expression = "${{ github.token }}"
    request = document["jobs"]["request"]
    metadata = next(
        step
        for step in _steps(request)
        if step["name"] == "Record platform workflow creation"
    )

    assert WORKFLOW.read_text(encoding="utf-8").count(github_expression) == 1
    assert metadata["env"] == {"WDV3_GITHUB_TOKEN": github_expression}
    assert github_expression not in document["env"].values()
    assert all(
        github_expression not in job.get("env", {}).values()
        for job in document["jobs"].values()
    )
    assert [
        (job_name, step["name"], key)
        for job_name, job in document["jobs"].items()
        for step in _steps(job)
        for key, value in step.get("env", {}).items()
        if value == github_expression
    ] == [
        (
            "request",
            "Record platform workflow creation",
            "WDV3_GITHUB_TOKEN",
        )
    ]


def test_request_checks_out_before_running_governed_metadata_helper() -> None:
    """Use a credentialless checkout before the root-HK-governed helper."""
    request = _document()["jobs"]["request"]
    steps = _steps(request)
    checkout = next(step for step in steps if step.get("uses") == CHECKOUT)
    metadata = next(
        step
        for step in steps
        if step["name"] == "Record platform workflow creation"
    )
    command = cast("str", metadata["run"])
    hk = HK_CONFIG.read_text(encoding="utf-8")

    assert steps.index(checkout) < steps.index(metadata)
    assert checkout["with"] == {
        "fetch-depth": 0,
        "persist-credentials": False,
        "ref": "${{ github.sha }}",
    }
    assert metadata["id"] == "clock"
    assert command == (
        'started_at="$(\n'
        "  python3 eng/scripts/workflow_delivery_v3_run_created_epoch.py\n"
        ')"\n'
        'echo "started-at=${started_at}" >> "${GITHUB_OUTPUT}"\n'
    )
    for forbidden in ("curl", "gh api", "urllib", "requests", "actions/runs"):
        assert forbidden not in command.lower()

    inventory_index = hk.index("local workflow_delivery_v3_files")
    helper_index = hk.index(
        '"eng/scripts/workflow_delivery_v3_run_created_epoch.py",'
    )
    control_index = hk.index('["v3-control-pytest"]')
    glob_index = hk.index("glob = workflow_delivery_v3_files", control_index)
    check_index = hk.index(
        "pytest -q src/public/lib/three-workflow-delivery-v3/tests",
        glob_index,
    )
    assert (
        inventory_index
        < helper_index
        < control_index
        < glob_index
        < check_index
    )


def test_plan_forwards_existing_provider_artifact_identity() -> None:
    """Forward the existing Provider platform identity without Plan growth."""
    jobs = _document()["jobs"]
    discover = jobs["discover-node"]
    plan = jobs["plan"]
    command = _run(plan, "Compile model and close Plan")

    assert discover["outputs"]["provider-artifact-id"] == (
        "${{ steps.upload.outputs.artifact-id }}"
    )
    assert discover["outputs"]["provider-artifact-digest"] == (
        "${{ steps.upload.outputs.artifact-digest }}"
    )
    assert plan["outputs"]["provider-artifact-id"] == (
        "${{ needs.discover-node.outputs.provider-artifact-id }}"
    )
    assert plan["outputs"]["provider-artifact-digest"] == (
        "${{ needs.discover-node.outputs.provider-artifact-digest }}"
    )
    assert re.findall(
        r"^\s+(--[a-z0-9-]+)\b",
        command,
        flags=re.MULTILINE,
    ) == [
        "--request",
        "--provider-result",
        "--request-artifact-id",
        "--request-artifact-digest",
        "--provider-artifact-id",
        "--provider-artifact-digest",
        "--output",
        "--adapter-context-output",
        "--github-output",
    ]
    assert (
        '--provider-artifact-id "${{ needs.discover-node.outputs.'
        'provider-artifact-id }}"'
    ) in command
    assert (
        '--provider-artifact-digest "${{ needs.discover-node.outputs.'
        'provider-artifact-digest }}"'
    ) in command
    assert "--schema" not in command
    assert "--obligation" not in command


def test_root_hk_downloads_and_admits_exact_provider_and_plan_artifacts() -> (
    None
):
    """Admit the forwarded Provider and Plan platform identities before HK."""
    root = _document()["jobs"]["root-hk"]
    steps = _steps(root)
    plan_download = next(
        step for step in steps if step["name"] == "Download Plan by artifact ID"
    )
    provider_download = next(
        step
        for step in steps
        if step["name"] == "Download Provider Result by artifact ID"
    )
    admit = next(
        step for step in steps if step["name"] == "Admit root-HK inputs"
    )
    execute = next(step for step in steps if step.get("id") == "execute")
    command = cast("str", admit["run"])

    assert plan_download["uses"] == DOWNLOAD
    assert plan_download["with"] == {
        "artifact-ids": "${{ needs.plan.outputs.plan-artifact-id }}",
        "path": ".wdv3/input",
        "skip-decompress": True,
        "digest-mismatch": "error",
    }
    assert provider_download["uses"] == DOWNLOAD
    assert provider_download["with"] == {
        "artifact-ids": "${{ needs.plan.outputs.provider-artifact-id }}",
        "path": ".wdv3/input",
        "skip-decompress": True,
        "digest-mismatch": "error",
    }
    assert (
        command.count("three-workflow-delivery-v3 ci admit-payload")
        == ROOT_HK_ADMISSION_COUNT
    )
    plan_input = (
        '--input ".wdv3/input/wdv3-${GITHUB_RUN_ID}-'
        '${GITHUB_RUN_ATTEMPT}-plan.json"'
    )
    plan_digest = (
        '--expected-digest "${{ needs.plan.outputs.plan-artifact-digest }}"'
    )
    provider_input = (
        '--input ".wdv3/input/wdv3-${GITHUB_RUN_ID}-'
        '${GITHUB_RUN_ATTEMPT}-provider.json"'
    )
    provider_digest = (
        '--expected-digest "${{ needs.plan.outputs.provider-artifact-digest }}"'
    )
    assert command.count("--expected-digest") == ROOT_HK_ADMISSION_COUNT
    assert (
        command.index(plan_input)
        < command.index(plan_digest)
        < command.index(provider_input)
        < command.index(provider_digest)
    )
    assert (
        steps.index(plan_download)
        < steps.index(provider_download)
        < steps.index(admit)
        < steps.index(execute)
    )


def test_root_hk_validates_toolchain_and_materializes_dependencies_before_hk() -> (  # noqa: E501
    None
):
    """Validate facts and install both frozen trees inside execution."""
    root = _document()["jobs"]["root-hk"]
    steps = _steps(root)
    execute_steps = [step for step in steps if step.get("id") == "execute"]

    assert len(execute_steps) == 1
    execute = execute_steps[0]
    command = cast("str", execute["run"])
    assert execute["name"] == "Run permanent root HK and consumer policy"
    assert execute["if"] == "needs.plan.outputs.root-hk-selected == 'true'"
    assert execute["continue-on-error"] is True
    assert 'toolchain = document["provider"]["toolchain"]' in command
    assert '("node", r"v[0-9][0-9A-Za-z.+-]{0,63}")' in command
    assert '("pnpm", r"[0-9][0-9A-Za-z.+-]{0,63}")' in command
    assert (
        "if not isinstance(value, str) or re.fullmatch(pattern, value) is None:"
    ) in command
    assert command.count("node --version") == 1
    assert command.count("pnpm --version") == 1
    assert (
        command.count("--frozen-lockfile --ignore-scripts")
        == FROZEN_INSTALL_COUNT
    )

    validation_index = command.index("if not isinstance(value, str)")
    node_fact_index = command.index('actual_node="$(node --version)"')
    pnpm_fact_index = command.index('actual_pnpm="$(pnpm --version)"')
    node_equality_index = command.index(
        'if [[ "${actual_node}" != "${expected_node}" ]]'
    )
    pnpm_equality_index = command.index(
        'if [[ "${actual_pnpm}" != "${expected_pnpm}" ]]'
    )
    root_install_index = command.index(
        "pnpm install --frozen-lockfile --ignore-scripts"
    )
    hexo_install_index = command.index(
        "pnpm --dir src/public/lib/hexo-renderer-asciidoc/examples/hexo-site"
    )
    hexo_flags_index = command.index(
        "install --frozen-lockfile --ignore-scripts",
        hexo_install_index,
    )
    incremental_hk_index = command.index(
        "python eng/scripts/workflow_delivery_v3_hk.py"
    )
    manual_hk_index = command.index("mise exec -- hk --no-progress check --all")
    assert (
        validation_index
        < node_fact_index
        < pnpm_fact_index
        < node_equality_index
        < pnpm_equality_index
        < root_install_index
        < hexo_install_index
        < hexo_flags_index
        < incremental_hk_index
        < manual_hk_index
    )
    for step in steps:
        if step is execute:
            continue
        other_command = cast("str", step.get("run", ""))
        assert "pnpm install --frozen-lockfile --ignore-scripts" not in (
            other_command
        )
        assert (
            "pnpm --dir "
            "src/public/lib/hexo-renderer-asciidoc/examples/hexo-site"
            not in other_command
        )


def test_root_hk_lane_result_and_upload_remain_failure_tolerant() -> None:
    """Always close and upload the existing root-HK result after execution."""
    root = _document()["jobs"]["root-hk"]
    steps = _steps(root)
    execute = next(step for step in steps if step.get("id") == "execute")
    lane_result = next(
        step for step in steps if step["name"] == "Form root-hk lane result"
    )
    upload = next(
        step for step in steps if step["name"] == "Upload root-hk lane result"
    )

    assert execute["continue-on-error"] is True
    assert lane_result["if"] == "always()"
    assert '--outcome "${{ steps.execute.outcome }}"' in lane_result["run"]
    assert upload["id"] == "upload"
    assert upload["if"] == (
        "always() && hashFiles(format('.wdv3/wdv3-{0}-{1}-"
        "root-hk-result.json', github.run_id, github.run_attempt)) != ''"
    )
    assert upload["uses"] == UPLOAD
    assert upload["with"] == {
        "name": (
            "wdv3-${{ github.run_id }}-${{ github.run_attempt }}-root-hk-result"
        ),
        "path": (
            ".wdv3/wdv3-${{ github.run_id }}-${{ github.run_attempt }}-"
            "root-hk-result.json"
        ),
        "if-no-files-found": "error",
        "retention-days": RETENTION_DAYS,
        "archive": False,
        "include-hidden-files": True,
    }
    assert steps.index(execute) < steps.index(lane_result) < steps.index(upload)


def test_finalizer_propagates_only_a_valid_original_exit_status() -> None:
    """Reject malformed output and propagate only the captured 0..255 status."""
    finalizer = _document()["jobs"]["required-finalizer"]
    propagation = next(
        step
        for step in _steps(finalizer)
        if step["name"] == "Propagate canonical CI Slice Decision"
    )
    command = cast("str", propagation["run"])

    assert propagation["if"] == (
        "always() && hashFiles(format('.wdv3/wdv3-{0}-{1}-"
        "ci-slice-decision.json', github.run_id, github.run_attempt)) != ''"
    )
    assert propagation["env"] == {
        "FINALIZER_EXIT": "${{ steps.finalize.outputs.finalizer-exit }}"
    }
    assert "continue-on-error" not in propagation
    assert command == (
        'if [[ ! "${FINALIZER_EXIT}" =~ ^[0-9]+$ ]] || '
        "(( FINALIZER_EXIT > 255 )); then\n"
        '  echo "Finalizer exit status is missing or invalid" >&2\n'
        "  exit 1\n"
        "fi\n"
        'exit "${FINALIZER_EXIT}"\n'
    )
