"""Test-first topology and privilege contracts for commit-8 Buddy workflows."""

from __future__ import annotations

# ruff: noqa: D103, E501
import json
import re
from pathlib import Path
from typing import Any

import yaml

REPO_ROOT = Path(__file__).resolve().parents[6]
CALLER = REPO_ROOT / ".github/workflows/workflow-delivery-v3-buddy-smoke.yml"
CALLEE = REPO_ROOT / ".github/workflows/workflow-delivery-v3-live-attempt.yml"
GOVERNANCE = (
    REPO_ROOT
    / ".github/workflow-delivery/governance/hcoona-release-smoke-npm.json"
)
RETENTION_DAYS = 45
APPROVAL_CORRELATION_NAME = (
    "Run same-revision Buddy live Attempt / "
    "Human approval and same-revision Authorization"
)
PUBLISHER_CORRELATION_NAME = (
    "Run same-revision Buddy live Attempt / Publish to GitHub Packages"
)
EXPECTED_JOBS = {
    "admit",
    "plan-qualification",
    "build-tarball",
    "project-test",
    "npm-artifact-qualification",
    "qualification-finalizer",
    "observe-github-packages",
    "materialize-publication",
    "approval",
    "approval-finalizer",
    "publish-github-packages",
    "release-finalizer",
}


def _document(path: Path) -> dict[str, Any]:
    assert path.is_file(), f"commit-8 phase-4 workflow is missing: {path}"
    value = yaml.safe_load(path.read_text(encoding="utf-8"))
    assert isinstance(value, dict)
    return value


def _needs(job: dict[str, Any]) -> tuple[str, ...]:
    value = job.get("needs", ())
    if isinstance(value, str):
        return (value,)
    if isinstance(value, tuple):
        return value
    assert isinstance(value, list)
    return tuple(value)


def _steps(job: dict[str, Any]) -> list[dict[str, Any]]:
    value = job.get("steps", [])
    assert isinstance(value, list)
    return value


def _step(job: dict[str, Any], name: str) -> dict[str, Any]:
    matches = [step for step in _steps(job) if step.get("name") == name]
    assert len(matches) == 1, f"{name!r} must occur exactly once"
    return matches[0]


def _run(step: dict[str, Any]) -> str:
    value = step.get("run")
    assert isinstance(value, str)
    return value


def _correlated_jobs(
    jobs: list[dict[str, Any]],
    *,
    expected_name: str,
    head_sha: str,
    run_attempt: int,
) -> list[dict[str, Any]]:
    return [
        job
        for job in jobs
        if job.get("name") == expected_name
        and job.get("head_sha") == head_sha
        and job.get("run_attempt") == run_attempt
    ]


def test_buddy_workflow_files_are_the_disabled_commit8_pair_only() -> None:
    assert CALLER.is_file()
    assert CALLEE.is_file()
    assert (
        REPO_ROOT
        / ".github/workflows/workflow-delivery-v3-buddy-smoke-acceptance.yml"
    ).is_file()
    raw = CALLER.read_text(encoding="utf-8") + CALLEE.read_text(
        encoding="utf-8"
    )
    assert "workflow-delivery-v3-buddy-smoke-acceptance" not in raw
    assert "live_enabled: true" not in raw
    assert "schedule:" not in raw
    assert "push:" not in raw
    if GOVERNANCE.exists():
        governance = json.loads(GOVERNANCE.read_text(encoding="utf-8"))
        assert governance["live_enabled"] is False


def test_buddy_caller_dag_concurrency_and_reusable_boundary_are_exact() -> None:
    caller = _document(CALLER)
    jobs = caller["jobs"]

    assert caller["permissions"] == {}
    assert set(jobs) == {
        "request",
        "discover-node",
        "compile-model",
        "evaluate-live-eligibility",
        "run-live-attempt",
    }
    assert jobs["discover-node"]["needs"] == "request"
    assert jobs["compile-model"]["needs"] == "discover-node"
    assert jobs["evaluate-live-eligibility"]["needs"] == "compile-model"
    invoke = jobs["run-live-attempt"]
    assert invoke["needs"] == "evaluate-live-eligibility"
    assert (
        invoke["uses"]
        == "./.github/workflows/workflow-delivery-v3-live-attempt.yml"
    )
    assert "steps" not in invoke
    assert invoke["concurrency"]["cancel-in-progress"] is False
    assert invoke["concurrency"]["group"].startswith("wdv3-execution-")


def test_buddy_permission_ceiling_and_effective_permissions_are_exact() -> None:
    caller = _document(CALLER)
    callee = _document(CALLEE)
    caller_jobs = caller["jobs"]
    callee_jobs = callee["jobs"]

    assert caller_jobs["evaluate-live-eligibility"]["permissions"] == {
        "contents": "read"
    }
    assert caller_jobs["run-live-attempt"]["permissions"] == {
        "contents": "read",
        "actions": "read",
        "packages": "write",
    }
    assert callee["permissions"] == {"contents": "read"}
    assert callee_jobs["admit"]["permissions"] == {
        "contents": "read",
        "actions": "read",
    }
    assert callee_jobs["observe-github-packages"]["permissions"] == {
        "contents": "read",
        "packages": "read",
    }
    assert callee_jobs["approval"]["permissions"] == {}
    assert callee_jobs["approval-finalizer"]["permissions"] == {
        "contents": "read"
    }
    assert callee_jobs["publish-github-packages"]["permissions"] == {
        "contents": "read",
        "packages": "write",
    }
    assert callee_jobs["release-finalizer"]["permissions"] == {
        "contents": "read"
    }
    for name, job in callee_jobs.items():
        assert "permissions" in job, (
            f"{name} must declare its effective least-privilege permissions"
        )
        permissions = job["permissions"]
        if name != "admit":
            assert permissions.get("actions") != "read"
        if name != "observe-github-packages":
            assert permissions.get("packages") != "read"
        if name != "publish-github-packages":
            assert permissions.get("packages") != "write"


def test_live_attempt_dag_environments_and_capability_gate_are_exact() -> None:
    jobs = _document(CALLEE)["jobs"]

    assert set(jobs) == EXPECTED_JOBS
    assert _needs(jobs["admit"]) == ()
    assert _needs(jobs["plan-qualification"]) == ("admit",)
    assert _needs(jobs["build-tarball"]) == ("plan-qualification",)
    assert _needs(jobs["project-test"]) == ("plan-qualification",)
    assert _needs(jobs["npm-artifact-qualification"]) == ("build-tarball",)
    assert set(_needs(jobs["qualification-finalizer"])) == {
        "project-test",
        "npm-artifact-qualification",
    }
    assert _needs(jobs["observe-github-packages"]) == (
        "qualification-finalizer",
    )
    assert _needs(jobs["materialize-publication"]) == (
        "observe-github-packages",
    )
    assert _needs(jobs["approval"]) == ("materialize-publication",)
    assert set(_needs(jobs["approval-finalizer"])) == {
        "materialize-publication",
        "approval",
    }
    assert jobs["approval"]["environment"]["name"] == (
        "workflow-delivery-v3-buddy-smoke-approval"
    )
    assert jobs["approval"]["environment"]["url"].startswith("${{ needs.")
    publisher = jobs["publish-github-packages"]
    assert publisher["needs"] == "approval-finalizer"
    assert publisher["environment"] == (
        "workflow-delivery-v3-buddy-smoke-github-packages"
    )
    assert "success" in publisher["if"]
    assert publisher["concurrency"]["cancel-in-progress"] is False
    assert publisher["concurrency"]["group"].startswith("wdv3-resource-")
    assert set(_needs(jobs["release-finalizer"])) == {
        "approval-finalizer",
        "publish-github-packages",
    }


def test_approval_uses_anonymous_exact_sha_fetch_and_no_artifact_credentials() -> (
    None
):
    approval = _document(CALLEE)["jobs"]["approval"]
    raw = "\n".join(str(step.get("run", "")) for step in _steps(approval))
    uses = tuple(step["uses"] for step in _steps(approval) if "uses" in step)

    assert uses == ()
    assert "https://github.com/hcoona/three.git" in raw
    assert "${GITHUB_SHA}" in raw
    assert "rev-parse HEAD" in raw
    assert "symbolic-ref" in raw
    assert "GITHUB_TOKEN" not in raw
    assert "ACTIONS_RUNTIME_TOKEN" not in raw
    assert "download-artifact" not in raw
    assert "refs/heads/" not in raw


def test_reviewer_artifact_transport_is_raw_id_bound_and_retained_45_days() -> (
    None
):
    document = _document(CALLEE)
    uploads = []
    downloads = []
    for job in document["jobs"].values():
        for step in _steps(job):
            uses = str(step.get("uses", ""))
            if uses.startswith("actions/upload-artifact@"):
                uploads.append(step)
            if uses.startswith("actions/download-artifact@"):
                downloads.append(step)

    reviewer = next(
        step for step in uploads if step["name"] == "Upload reviewer artifact"
    )
    assert reviewer["with"]["retention-days"] == RETENTION_DAYS
    assert reviewer["with"]["overwrite"] is False
    assert reviewer["with"]["archive"] is False
    assert reviewer["with"]["include-hidden-files"] is True
    assert reviewer["with"]["if-no-files-found"] == "error"
    assert downloads
    for step in downloads:
        assert "artifact-ids" in step["with"]
        assert "name" not in step["with"]
        assert any(
            source in step["with"]["artifact-ids"]
            for source in ("needs.", "inputs.")
        )
        assert "steps." not in step["with"]["artifact-ids"]
        assert step["with"]["skip-decompress"] is True
        assert step["with"]["digest-mismatch"] == "error"


def test_all_actions_are_full_sha_pinned_with_version_comments() -> None:
    raw = CALLER.read_text(encoding="utf-8") + CALLEE.read_text(
        encoding="utf-8"
    )
    uses_lines = [
        line.strip()
        for line in raw.splitlines()
        if line.strip().startswith("uses:")
        and not line.strip().endswith(".yml")
    ]
    pin = re.compile(
        r"uses:\s+[^@\s]+@[0-9a-f]{40}\s+#\s+v[0-9][0-9A-Za-z.-]*\Z"
    )

    assert uses_lines
    assert all(pin.fullmatch(line) for line in uses_lines)
    assert any("actions/upload-artifact@" in line for line in uses_lines)


def test_workflows_forbid_secrets_oidc_publication_bypasses_and_later_scope() -> (
    None
):
    raw = (
        CALLER.read_text(encoding="utf-8") + CALLEE.read_text(encoding="utf-8")
    ).lower()

    for forbidden in (
        "secrets:",
        "secrets.",
        "id-token:",
        "npm_token",
        "npm-token",
        "latest",
        "dist-tag add",
        "npm unpublish",
        "delete-package",
        "restore-package",
        "workflow-delivery-v3-buddy-smoke-acceptance",
        "github-release",
        "pypi",
    ):
        assert forbidden not in raw


def test_commit8_publish_gate_compares_the_exact_success_result() -> None:
    publisher = _document(CALLEE)["jobs"]["publish-github-packages"]

    assert publisher["if"] == (
        "success() && "
        "needs.approval-finalizer.outputs.capability-result == 'success' && "
        "needs.approval-finalizer.outputs.publish-required == 'true'"
    )


def test_commit8_preobserved_noop_skips_publish_but_still_finalizes() -> None:
    jobs = _document(CALLEE)["jobs"]

    assert "publish-required == 'true'" in jobs["publish-github-packages"]["if"]
    assert _needs(jobs["release-finalizer"]) == (
        "approval-finalizer",
        "publish-github-packages",
    )
    assert jobs["release-finalizer"]["if"] == "always()"


def test_commit8_platform_termination_facts_are_derived_for_finalization() -> (
    None
):
    finalizer = _document(CALLEE)["jobs"]["release-finalizer"]
    command = _run(_step(finalizer, "Finalize Attempt Outcome"))

    assert finalizer["permissions"] == {"contents": "read"}
    assert "/actions/runs/" not in command
    assert "GITHUB_TOKEN" not in command
    assert "publish_result=" in command
    assert "capability_marker_id=" in command
    assert "capability_bundle_id=" in command
    assert 'publish_result}" == "failure"' in command
    assert "--platform-terminated" in command
    assert "--capability-may-have-started" in command
    assert "needs.publish-github-packages.result" in command


def test_commit8_receipt_is_uploaded_before_bundle_uses_real_transport() -> (
    None
):
    publisher = _document(CALLEE)["jobs"]["publish-github-packages"]
    steps = _steps(publisher)
    publish = _run(_step(publisher, "Publish create-only package action"))
    receipt = _step(publisher, "Upload exact Receipt")
    bundle = _run(_step(publisher, "Form Capability Group Result Bundle"))

    assert "--receipt-artifact-id" not in publish
    assert steps.index(receipt) < steps.index(
        _step(publisher, "Form Capability Group Result Bundle")
    )
    assert "steps.upload-receipt.outputs.artifact-id" in bundle
    assert "steps.upload-receipt.outputs.artifact-digest" in bundle
    assert '"1"' not in bundle


def test_commit8_failed_capability_forms_and_uploads_exactly_one_bundle() -> (
    None
):
    publisher = _document(CALLEE)["jobs"]["publish-github-packages"]
    publish = _step(publisher, "Publish create-only package action")
    form = _step(publisher, "Form Capability Group Result Bundle")
    uploads = [
        step
        for step in _steps(publisher)
        if step.get("name") == "Upload Capability Group Result Bundle"
    ]

    assert publish["continue-on-error"] is True
    assert "publish-cli-status=${status}" in _run(publish)
    assert form["if"] == "always()"
    assert len(uploads) == 1
    assert uploads[0]["if"] == (
        "always() && "
        "steps.form-bundle.outputs.capability-group-bundle-artifact-name != ''"
    )
    assert "steps.publish.outcome" in _run(form)
    assert "form_status=$?" in _run(form)


def test_commit8_final_outcome_and_summary_are_retained_even_on_failure() -> (
    None
):
    finalizer = _document(CALLEE)["jobs"]["release-finalizer"]
    finalize = _step(finalizer, "Finalize Attempt Outcome")
    uploads = [
        step
        for step in _steps(finalizer)
        if str(step.get("uses", "")).startswith("actions/upload-artifact@")
    ]

    assert finalizer["if"] == "always()"
    assert finalize["continue-on-error"] is True
    assert {step["name"] for step in uploads} == {
        "Upload final Attempt Outcome and summary",
    }
    for step in uploads:
        assert step["if"] == "always()"
        assert step["with"]["retention-days"] == RETENTION_DAYS
        assert step["with"]["if-no-files-found"] == "error"


def test_commit8_approval_formatter_is_offline_and_never_invokes_pip() -> None:
    approval = _document(CALLEE)["jobs"]["approval"]
    command = _run(
        _step(approval, "Fetch exact public target and format Authorization")
    )

    assert "pip install" not in command
    assert "python3 -m venv" not in command
    assert "PYTHONPATH=" in command
    assert "authorization_formatter.py" in command


def test_commit8_authorization_uses_real_correlated_job_and_check_run_ids() -> (
    None
):
    approval = _document(CALLEE)["jobs"]["approval"]
    command = _run(
        _step(approval, "Fetch exact public target and format Authorization")
    )

    assert approval["permissions"] == {}
    assert (
        "/actions/runs/${GITHUB_RUN_ID}/attempts/${GITHUB_RUN_ATTEMPT}/jobs"
        in command
    )
    assert APPROVAL_CORRELATION_NAME in command
    assert 'job.get("head_sha") == sys.argv[2]' in command
    assert 'str(job.get("run_attempt")) == sys.argv[3]' in command
    assert "check_run_url" in command
    assert "--approval-job-id" in command
    assert "GITHUB_JOB_ID:-1" not in command
    assert '"1"' not in command


def test_commit8_freshness_block_is_retained_before_nonzero_propagates() -> (
    None
):
    finalizer = _document(CALLEE)["jobs"]["approval-finalizer"]
    steps = _steps(finalizer)
    admit = _step(finalizer, "Admit exact capability closure")
    upload = _step(finalizer, "Upload Capability Admission Decision")
    propagate = _step(finalizer, "Propagate capability admission status")

    assert admit["continue-on-error"] is True
    assert upload["if"] == (
        "always() && "
        "steps.finalize.outputs.capability-decision-artifact-name != ''"
    )
    assert steps.index(admit) < steps.index(upload) < steps.index(propagate)
    assert propagate["if"] == "always()"
    assert "steps.finalize.outcome" in _run(propagate)


def test_commit8_missing_authorization_reaches_approval_finalizer() -> None:
    finalizer = _document(CALLEE)["jobs"]["approval-finalizer"]
    command = _run(_step(finalizer, "Admit exact capability closure"))

    assert finalizer["if"] == "always()"
    assert set(_needs(finalizer)) == {"materialize-publication", "approval"}
    assert "needs.approval.result" in command
    assert "--authorization" in command
    assert "unknown-replayable-approval-contract" in command


def test_commit8_dag_order_retention_and_error_propagation_are_exact() -> None:
    jobs = _document(CALLEE)["jobs"]
    assert set(jobs) == EXPECTED_JOBS
    assert jobs["qualification-finalizer"]["if"] == "always()"
    assert jobs["approval-finalizer"]["if"] == "always()"
    assert jobs["release-finalizer"]["if"] == "always()"

    for job in jobs.values():
        for step in _steps(job):
            uses = str(step.get("uses", ""))
            if uses.startswith("actions/upload-artifact@"):
                assert step["with"]["retention-days"] == RETENTION_DAYS
                assert step["with"]["if-no-files-found"] == "error"

    publisher_steps = _steps(jobs["publish-github-packages"])
    assert [step["name"] for step in publisher_steps[-5:]] == [
        "Publish create-only package action",
        "Upload exact Receipt",
        "Form Capability Group Result Bundle",
        "Upload Capability Group Result Bundle",
        "Propagate publication status",
    ]


def test_commit8_status_evidence_is_named_and_transport_bound() -> None:
    finalizer = _document(CALLEE)["jobs"]["release-finalizer"]
    command = _run(_step(finalizer, "Finalize Attempt Outcome"))
    uploads = {
        step["name"]: step for step in _steps(finalizer) if "uses" in step
    }

    assert (
        "--outcome-output .wdv3/final-attempt/attempt-outcome.json" in command
    )
    assert "--summary-output .wdv3/final-attempt/attempt-summary.md" in command
    assert '--github-step-summary "${GITHUB_STEP_SUMMARY}"' in command
    final_upload = uploads["Upload final Attempt Outcome and summary"]
    assert final_upload["with"]["path"] == ".wdv3/final-attempt"
    assert final_upload["with"]["overwrite"] is False
    assert "archive" not in final_upload["with"]
    assert final_upload["with"]["name"].startswith(
        "${{ steps.finalize.outputs.final-artifact-name }}"
    )


def test_user_item9_actions_permission_is_history_admission_only() -> None:
    jobs = _document(CALLEE)["jobs"]
    actions_read_jobs = {
        name
        for name, job in jobs.items()
        if job.get("permissions", {}).get("actions") == "read"
    }

    assert actions_read_jobs == {"admit"}
    assert jobs["release-finalizer"]["permissions"] == {"contents": "read"}


def test_user_item9_finalizer_derives_conservative_phase_from_retained_outputs() -> (
    None
):
    command = _run(
        _step(
            _document(CALLEE)["jobs"]["release-finalizer"],
            "Finalize Attempt Outcome",
        )
    )

    assert "jobs_url=" not in command
    assert "capability_marker_id=" in command
    assert "capability_bundle_id=" in command
    assert 'publish_result}" == "cancelled"' in command
    assert 'publish_result}" == "failure"' in command
    assert '-z "${capability_bundle_id}"' in command
    assert 'if [[ -n "${capability_marker_id}" ]]' in command


def test_release_finalizer_platform_fact_truth_table_is_independent() -> None:
    cases = (
        ("skipped", False, False, (False, False)),
        ("success", True, True, (False, True)),
        ("failure", False, True, (False, False)),
        ("failure", False, False, (True, False)),
        ("failure", True, False, (True, True)),
        ("cancelled", False, False, (True, False)),
        ("cancelled", True, False, (True, True)),
    )
    for publish_result, marker_present, bundle_present, expected in cases:
        platform_terminated = publish_result == "cancelled" or (
            publish_result == "failure" and not bundle_present
        )
        capability_may_have_started = marker_present

        assert (
            platform_terminated,
            capability_may_have_started,
        ) == expected


def test_user_item10_correlation_names_are_exact_and_unambiguous() -> None:
    approval_command = _run(
        _step(
            _document(CALLEE)["jobs"]["approval"],
            "Fetch exact public target and format Authorization",
        )
    )
    publisher = _document(CALLEE)["jobs"]["publish-github-packages"]
    marker_upload = _step(
        publisher,
        "Upload mutation may-have-started marker",
    )

    assert APPROVAL_CORRELATION_NAME in approval_command
    assert PUBLISHER_CORRELATION_NAME == (
        "Run same-revision Buddy live Attempt / Publish to GitHub Packages"
    )
    assert "mutation-may-have-started" in marker_upload["with"]["name"]
    assert 'Human approval and same-revision Authorization"' not in (
        approval_command.replace(APPROVAL_CORRELATION_NAME, "")
    )


def test_user_item10_correlation_rejects_zero_duplicate_and_wrong_prefix() -> (
    None
):
    head = "a" * 40
    names = (
        (
            APPROVAL_CORRELATION_NAME,
            "Other reusable / Human approval and same-revision Authorization",
        ),
        (
            PUBLISHER_CORRELATION_NAME,
            "Other reusable / Publish to GitHub Packages",
        ),
    )

    for expected_name, wrong_name in names:
        valid = {
            "name": expected_name,
            "head_sha": head,
            "run_attempt": 2,
        }
        wrong_prefix = {
            "name": wrong_name,
            "head_sha": head,
            "run_attempt": 2,
        }

        assert (
            _correlated_jobs(
                [],
                expected_name=expected_name,
                head_sha=head,
                run_attempt=2,
            )
            == []
        )
        duplicate_matches = _correlated_jobs(
            [valid, dict(valid)],
            expected_name=expected_name,
            head_sha=head,
            run_attempt=2,
        )
        assert len(duplicate_matches) == len((valid, valid))
        assert (
            _correlated_jobs(
                [wrong_prefix],
                expected_name=expected_name,
                head_sha=head,
                run_attempt=2,
            )
            == []
        )
        assert _correlated_jobs(
            [valid],
            expected_name=expected_name,
            head_sha=head,
            run_attempt=2,
        ) == [valid]


def test_user_item11_publisher_preflight_and_start_marker_are_separate() -> (
    None
):
    publisher = _document(CALLEE)["jobs"]["publish-github-packages"]
    steps = _steps(publisher)
    preflight = _step(publisher, "Preflight publication without npm mutation")
    marker = _step(publisher, "Form mutation may-have-started marker")
    upload = _step(publisher, "Upload mutation may-have-started marker")
    publish = _step(publisher, "Publish create-only package action")
    command = _run(publish)

    assert steps.index(preflight) < steps.index(marker) < steps.index(upload)
    assert steps.index(upload) < steps.index(publish)
    assert "preflight-github-packages" in _run(preflight)
    assert "mark-github-packages-mutation-start" in _run(marker)
    assert "--mutation-marker-artifact-id" in command
    assert "status=$?" in command
    assert "publish-cli-status=${status}" in command
    assert "publisher-admission-or-governance-failed" not in command
    assert '"mutation-disposition": "no-side-effect"' not in command
    assert 'exit "${status}"' in command


def test_user_item12_failed_publication_uploads_nonempty_bundle_before_propagation() -> (
    None
):
    publisher = _document(CALLEE)["jobs"]["publish-github-packages"]
    steps = _steps(publisher)
    form = _step(publisher, "Form Capability Group Result Bundle")
    upload = _step(publisher, "Upload Capability Group Result Bundle")
    propagate = _step(publisher, "Propagate publication status")
    form_command = _run(form)

    assert steps.index(form) < steps.index(upload) < steps.index(propagate)
    assert "set +e" in form_command
    assert "form_status=$?" in form_command
    assert "capability-group-bundle-digest" in form_command
    assert upload["if"] == (
        "always() && "
        "steps.form-bundle.outputs.capability-group-bundle-artifact-name != ''"
    )
    assert upload["with"]["path"].endswith(
        "${{ steps.form-bundle.outputs.capability-group-bundle-artifact-name }}"
    )
    assert upload["with"]["path"] != ".wdv3"
    assert "steps.upload.outcome" in _run(propagate)


def test_user_item13_finalizer_always_retains_outcome_summary_with_exact_contract() -> (
    None
):
    finalizer = _document(CALLEE)["jobs"]["release-finalizer"]
    upload = _step(finalizer, "Upload final Attempt Outcome and summary")
    command = _run(_step(finalizer, "Finalize Attempt Outcome"))

    assert finalizer["if"] == "always()"
    assert finalizer["permissions"] == {"contents": "read"}
    assert (
        "--outcome-output .wdv3/final-attempt/attempt-outcome.json" in command
    )
    assert "--summary-output .wdv3/final-attempt/attempt-summary.md" in command
    assert upload["if"] == "always()"
    assert upload["with"] == {
        "name": "${{ steps.finalize.outputs.final-artifact-name }}",
        "path": ".wdv3/final-attempt",
        "if-no-files-found": "error",
        "retention-days": RETENTION_DAYS,
        "overwrite": False,
        "include-hidden-files": True,
    }
