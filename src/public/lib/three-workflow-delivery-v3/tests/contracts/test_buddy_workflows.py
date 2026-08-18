"""Test-first topology and privilege contracts for commit-8 Buddy workflows."""

from __future__ import annotations

# ruff: noqa: D103, E501
# ruff: noqa: PLR0915
import json
import re
from pathlib import Path, PurePosixPath
from typing import Any, cast

import pytest
import yaml

REPO_ROOT = Path(__file__).resolve().parents[6]
CALLER = REPO_ROOT / ".github/workflows/workflow-delivery-v3-buddy-smoke.yml"
CALLEE = REPO_ROOT / ".github/workflows/workflow-delivery-v3-live-attempt.yml"
GOVERNANCE = (
    REPO_ROOT
    / ".github/workflow-delivery/governance/hcoona-release-smoke-npm.json"
)
RETENTION_DAYS = 45
CHECKOUT = "actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1"
UV = "astral-sh/setup-uv@20cfd1bf945f4377ade1205e4dbc17946fc9a30d"
MISE = "jdx/mise-action@3c2e0cf82a5b2e5249f0d3635a4d83d0ae861518"
UPLOAD = "actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a"
DOWNLOAD = "actions/download-artifact@3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c"
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


def _raw_artifact_name(settings: dict[str, Any]) -> str:
    """Model upload-artifact v7 archive:false physical naming."""
    assert settings["archive"] is False
    path = settings["path"]
    assert isinstance(path, str)
    entries = path.splitlines()
    assert len(entries) == 1
    entry = entries[0]
    assert entry
    assert entry == entry.strip()
    assert path == entry
    assert not entry.endswith("/")
    assert not any(character in entry for character in "*?[")
    artifact_name = settings["name"]
    assert isinstance(artifact_name, str)
    assert artifact_name
    assert PurePosixPath(entry).name == artifact_name
    return PurePosixPath(path).name


def _artifact_steps(
    document: dict[str, Any],
    action: str,
) -> list[dict[str, Any]]:
    return [
        step
        for job in document["jobs"].values()
        for step in _steps(job)
        if step.get("uses") == action
    ]


def _assert_existing_raw_uploads_keep_physical_names(
    document: dict[str, Any],
) -> None:
    excluded = {
        "Upload reviewer artifact",
        "Upload Authorization Record",
        "Upload mutation may-have-started marker",
        "Upload final Attempt Outcome and summary",
    }
    expected = {
        "Upload Execution History Admission Snapshot",
        "Upload Release Attempt binding",
        "Upload Qualification Snapshot",
        "Upload Adapter context",
        "Upload exact npm tarball",
        "Upload Release Artifact record",
        "Upload build Evidence",
        "Upload project-test Evidence",
        "Upload artifact-contents Evidence",
        "Upload install-import Evidence",
        "Upload Qualification Decision",
        "Upload Observation Record set",
        "Upload Publication Snapshot",
        "Upload Capability Admission Decision",
        "Upload exact Receipt",
        "Upload Capability Group Result Bundle",
    }
    uploads = [
        step
        for step in _artifact_steps(document, UPLOAD)
        if step["name"] not in excluded
    ]

    assert {step["name"] for step in uploads} == expected
    for step in uploads:
        assert step["with"]["archive"] is False
        assert _raw_artifact_name(step["with"]) == step["with"]["name"]


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
        "build-tarball",
        "project-test",
        "npm-artifact-qualification",
    }
    assert _needs(jobs["observe-github-packages"]) == (
        "qualification-finalizer",
    )
    assert jobs["observe-github-packages"]["if"] == (
        "needs.qualification-finalizer.outputs.qualification-result "
        "== 'success'"
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
        "admit",
        "qualification-finalizer",
        "approval-finalizer",
        "publish-github-packages",
    }


def test_shared_qualification_commands_admit_live_purpose_explicitly() -> None:
    jobs = _document(CALLEE)["jobs"]
    commands = (
        ("build-tarball", "Build tarball"),
        (
            "build-tarball",
            "Form Release Artifact and build Evidence after upload",
        ),
        ("project-test", "Run project-test mechanics"),
        ("npm-artifact-qualification", "Run artifact-contents mechanics"),
        (
            "npm-artifact-qualification",
            "Form incomplete artifact-contents Evidence",
        ),
        ("npm-artifact-qualification", "Run install-import mechanics"),
        (
            "npm-artifact-qualification",
            "Form incomplete install-import Evidence",
        ),
        ("qualification-finalizer", "Close qualification Decision"),
    )

    for job_name, step_name in commands:
        run = _run(_step(jobs[job_name], step_name))
        assert '--purpose "${WDV3_PURPOSE}"' in run
        assert run.count('--purpose "${WDV3_PURPOSE}"') == 1


def test_live_compiler_and_qualification_toolchains_are_exact() -> None:
    caller_jobs = _document(CALLER)["jobs"]
    callee_jobs = _document(CALLEE)["jobs"]
    compiler = _run(
        _step(
            caller_jobs["discover-node"],
            "Run Node Provider once",
        )
    )
    intent_admission = _run(
        _step(
            caller_jobs["discover-node"],
            "Admit current Release Intent",
        )
    )

    assert '--purpose "${WDV3_PURPOSE}"' in intent_admission
    assert "--compiler-producer compile-live-model" in compiler
    assert "--compiler-producer compile-model" not in compiler
    for job_name in (
        "plan-qualification",
        "npm-artifact-qualification",
        "publish-github-packages",
    ):
        mise_steps = [
            step
            for step in _steps(callee_jobs[job_name])
            if step.get("uses") == MISE
        ]
        assert len(mise_steps) == 1
        assert mise_steps[0]["with"] == {
            "experimental": True,
            "install": True,
        }
        if job_name == "publish-github-packages":
            steps = _steps(callee_jobs[job_name])
            assert steps.index(mise_steps[0]) < steps.index(
                _step(
                    callee_jobs[job_name],
                    "Preflight publication without npm mutation",
                )
            )


def test_live_build_uses_the_planned_tarball_artifact_name() -> None:
    jobs = _document(CALLEE)["jobs"]
    plan = jobs["plan-qualification"]
    build = jobs["build-tarball"]

    assert plan["outputs"]["tarball-artifact-name"] == (
        "${{ steps.plan.outputs.tarball-artifact-name }}"
    )
    assert build["outputs"]["tarball-artifact-name"] == (
        "${{ needs.plan-qualification.outputs.tarball-artifact-name }}"
    )
    assert _step(build, "Build tarball")["env"]["TARBALL_NAME"] == (
        "${{ needs.plan-qualification.outputs.tarball-artifact-name }}"
    )


def test_unsuccessful_live_qualification_retains_a_publication_free_outcome() -> (
    None
):
    jobs = _document(CALLEE)["jobs"]
    npm_qualification = jobs["npm-artifact-qualification"]
    qualification_finalizer = jobs["qualification-finalizer"]
    release_finalizer = jobs["release-finalizer"]

    assert npm_qualification["if"] == (
        "always() && needs.build-tarball.result != 'skipped'"
    )
    prerequisite = (
        "needs.build-tarball.outputs.release-artifact-artifact-id != '' && "
        "needs.build-tarball.outputs.tarball-artifact-id != ''"
    )
    blocked = (
        "needs.build-tarball.outputs.release-artifact-artifact-id == '' || "
        "needs.build-tarball.outputs.tarball-artifact-id == ''"
    )
    assert (
        _step(
            npm_qualification,
            "Download exact tarball by artifact ID",
        )["if"]
        == "needs.build-tarball.outputs.tarball-artifact-id != ''"
    )
    assert _step(
        npm_qualification,
        "Download Release Artifact record by artifact ID",
    )["if"] == (
        "needs.build-tarball.outputs.release-artifact-artifact-id != ''"
    )
    assert (
        _step(
            npm_qualification,
            "Run artifact-contents mechanics",
        )["if"]
        == prerequisite
    )
    assert (
        _step(
            npm_qualification,
            "Run install-import mechanics",
        )["if"]
        == prerequisite
    )
    assert (
        _step(
            npm_qualification,
            "Form incomplete artifact-contents Evidence",
        )["if"]
        == blocked
    )
    assert (
        _step(
            npm_qualification,
            "Form incomplete install-import Evidence",
        )["if"]
        == blocked
    )
    assert (
        "steps.incomplete-contents.outputs"
        in (npm_qualification["outputs"]["contents-evidence-digest"])
    )
    assert (
        "steps.incomplete-install.outputs"
        in (npm_qualification["outputs"]["install-evidence-digest"])
    )

    assert qualification_finalizer["if"] == (
        "always() && needs.build-tarball.result != 'skipped'"
    )
    assert set(_needs(qualification_finalizer)) == {
        "build-tarball",
        "project-test",
        "npm-artifact-qualification",
    }
    assert qualification_finalizer["outputs"][
        "qualification-snapshot-artifact-id"
    ] == (
        "${{ needs.build-tarball.outputs.qualification-snapshot-artifact-id }}"
    )
    assert (
        qualification_finalizer["outputs"]["build-evidence-artifact-id"]
        == "${{ needs.build-tarball.outputs.build-evidence-artifact-id }}"
    )
    assert (
        qualification_finalizer["outputs"]["project-test-evidence-artifact-id"]
        == "${{ needs.project-test.outputs.evidence-artifact-id }}"
    )
    assert qualification_finalizer["outputs"][
        "artifact-contents-evidence-artifact-id"
    ] == (
        "${{ needs.npm-artifact-qualification.outputs."
        "contents-evidence-artifact-id }}"
    )
    assert qualification_finalizer["outputs"][
        "install-import-evidence-artifact-id"
    ] == (
        "${{ needs.npm-artifact-qualification.outputs."
        "install-evidence-artifact-id }}"
    )
    close = _run(_step(qualification_finalizer, "Close qualification Decision"))
    assert "needs.build-tarball.outputs.build-evidence" in close
    assert "needs.build-tarball.outputs.release-artifact" in close

    assert jobs["observe-github-packages"]["if"] == (
        "needs.qualification-finalizer.outputs.qualification-result "
        "== 'success'"
    )
    assert jobs["approval-finalizer"]["if"] == (
        "always() && needs.materialize-publication.result != 'skipped'"
    )
    assert {
        name: jobs["approval-finalizer"]["outputs"][name]
        for name in (
            "publication-snapshot-artifact-id",
            "publication-snapshot-artifact-digest",
            "publication-snapshot-artifact-name",
            "publication-snapshot-digest",
        )
    } == {
        "publication-snapshot-artifact-id": (
            "${{ needs.materialize-publication.outputs."
            "publication-snapshot-artifact-id }}"
        ),
        "publication-snapshot-artifact-digest": (
            "${{ needs.materialize-publication.outputs."
            "publication-snapshot-artifact-digest }}"
        ),
        "publication-snapshot-artifact-name": (
            "${{ needs.materialize-publication.outputs."
            "publication-snapshot-artifact-name }}"
        ),
        "publication-snapshot-digest": (
            "${{ needs.materialize-publication.outputs."
            "publication-snapshot-digest }}"
        ),
    }
    assert set(_needs(release_finalizer)) == {
        "admit",
        "qualification-finalizer",
        "approval-finalizer",
        "publish-github-packages",
    }

    attempt_download = _step(
        release_finalizer,
        "Download Release Attempt binding by artifact ID",
    )
    assert attempt_download["with"] == {
        "artifact-ids": "${{ needs.admit.outputs.attempt-artifact-id }}",
        "path": ".wdv3/input",
        "skip-decompress": True,
        "digest-mismatch": "error",
    }
    snapshot_download = _step(
        release_finalizer,
        "Download Qualification Snapshot by artifact ID",
    )
    assert snapshot_download["with"] == {
        "artifact-ids": (
            "${{ needs.qualification-finalizer.outputs."
            "qualification-snapshot-artifact-id }}"
        ),
        "path": ".wdv3/input",
        "skip-decompress": True,
        "digest-mismatch": "error",
    }
    decision_download = _step(
        release_finalizer,
        "Download Qualification Decision by artifact ID",
    )
    assert decision_download["with"] == {
        "artifact-ids": (
            "${{ needs.qualification-finalizer.outputs.decision-artifact-id }}"
        ),
        "path": ".wdv3/input",
        "skip-decompress": True,
        "digest-mismatch": "error",
    }
    for step_name, output_prefix in (
        ("Download build Evidence by artifact ID", "build-evidence"),
        (
            "Download project-test Evidence by artifact ID",
            "project-test-evidence",
        ),
        (
            "Download artifact-contents Evidence by artifact ID",
            "artifact-contents-evidence",
        ),
        (
            "Download install-import Evidence by artifact ID",
            "install-import-evidence",
        ),
        (
            "Download Release Artifact record by artifact ID",
            "release-artifact",
        ),
    ):
        download = _step(release_finalizer, step_name)
        artifact_id = (
            "${{ needs.qualification-finalizer.outputs."
            + output_prefix
            + "-artifact-id }}"
        )
        assert download["if"] == (
            "needs.qualification-finalizer.outputs."
            f"{output_prefix}-artifact-id != ''"
        )
        assert download["with"] == {
            "artifact-ids": artifact_id,
            "path": ".wdv3/input",
            "skip-decompress": True,
            "digest-mismatch": "error",
        }
    publication_download = _step(
        release_finalizer,
        "Download Publication Snapshot by artifact ID",
    )
    assert publication_download["if"] == (
        "needs.approval-finalizer.outputs."
        "publication-snapshot-artifact-id != ''"
    )
    finalize = _run(_step(release_finalizer, "Finalize Attempt Outcome"))
    assert (
        'if [[ -n "${{ needs.approval-finalizer.outputs.'
        'publication-snapshot-artifact-id }}" ]]' in finalize
    )
    assert finalize.count("--publication-snapshot ") == 1
    assert (
        "--attempt-binding "
        '".wdv3/input/${{ needs.admit.outputs.attempt-artifact-name }}"'
        in finalize
    )
    assert (
        "--qualification-snapshot "
        '".wdv3/input/${{ needs.qualification-finalizer.outputs.'
        'qualification-snapshot-artifact-name }}"' in finalize
    )
    assert (
        "--qualification-decision "
        '".wdv3/input/${{ needs.qualification-finalizer.outputs.'
        'decision-artifact-name }}"' in finalize
    )
    for role in (
        "build-evidence",
        "project-test-evidence",
        "artifact-contents-evidence",
        "install-import-evidence",
        "release-artifact",
    ):
        assert f"add_record {role} " in finalize
    assert "needs.approval-finalizer.outputs.decision-" not in finalize


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


def test_reviewer_archive_is_decompressed_with_transport_and_payload_bindings() -> (
    None
):
    document = _document(CALLEE)
    materializer = document["jobs"]["materialize-publication"]
    finalizer = document["jobs"]["approval-finalizer"]
    materializer_steps = _steps(materializer)
    approval_job = document["jobs"]["approval"]
    materialize_step = _step(
        materializer,
        "Materialize immutable publication and reviewer payload",
    )
    names_step = _step(materializer, "Materialize exact publication basenames")
    reviewer_upload = _step(materializer, "Upload reviewer artifact")
    reviewer_download = _step(
        finalizer,
        "Download reviewer payload by artifact ID",
    )
    bind_step = _step(
        materializer,
        "Bind reviewer artifact transport to exact payloads",
    )
    authorization_formatter_step = _step(
        approval_job,
        "Fetch exact public target and format Authorization",
    )
    capability_finalizer_step = _step(
        finalizer,
        "Admit exact capability closure",
    )
    materialize = _run(materialize_step)
    authorization_formatter = _run(authorization_formatter_step)
    capability_finalize = _run(capability_finalizer_step)
    names = _run(_step(materializer, "Materialize exact publication basenames"))
    bind = _run(bind_step)
    admit = _run(_step(finalizer, "Admit exact capability closure"))
    history = _run(
        _step(
            document["jobs"]["admit"],
            "Discover exhaustive retained execution history",
        )
    )

    upload_settings = reviewer_upload["with"]
    assert reviewer_upload["uses"] == UPLOAD
    assert upload_settings["name"] == "${{ steps.names.outputs.reviewer-name }}"
    assert upload_settings["path"] == (
        ".wdv3/${{ steps.names.outputs.reviewer-name }}"
    )
    assert upload_settings.get("archive", True) is True
    assert upload_settings["retention-days"] == RETENTION_DAYS
    assert upload_settings["overwrite"] is False
    assert upload_settings["include-hidden-files"] is True
    assert upload_settings["if-no-files-found"] == "error"
    assert materializer["outputs"]["reviewer-artifact-digest"] == (
        "${{ steps.upload-reviewer.outputs.artifact-digest }}"
    )
    assert materializer["outputs"]["reviewer-artifact-id"] == (
        "${{ steps.upload-reviewer.outputs.artifact-id }}"
    )
    for payload_name in (
        "publication-snapshot.json",
        "reviewer-summary.md",
        "reviewer-formatter-input.json",
    ):
        assert f"${{reviewer_name}}/{payload_name}" in names
    assert materialize_step["id"] == "materialize"
    assert names_step["id"] == "names"
    assert reviewer_upload["id"] == "upload-reviewer"
    assert bind_step["id"] == "bind"
    assert (
        materializer_steps.index(materialize_step)
        < materializer_steps.index(names_step)
        < materializer_steps.index(reviewer_upload)
        < materializer_steps.index(bind_step)
    )
    assert materializer_steps.index(reviewer_upload) < materializer_steps.index(
        bind_step
    )
    assert (
        "--formatter-input-output .wdv3/reviewer-formatter-input.json"
        in materialize
    )
    assert (
        "mv .wdv3/reviewer-formatter-input.json "
        '".wdv3/${reviewer_name}/reviewer-formatter-input.json"' in names
    )
    assert (
        'cp ".wdv3/${snapshot_name}" '
        '".wdv3/${reviewer_name}/publication-snapshot.json"' in names
    )
    assert (
        "--formatter-input "
        '".wdv3/${{ steps.names.outputs.reviewer-name }}/'
        'reviewer-formatter-input.json"' in bind
    )
    assert (
        "--publication-snapshot "
        '".wdv3/${{ steps.names.outputs.reviewer-name }}/'
        'publication-snapshot.json"' in bind
    )
    assert (
        '--publication-snapshot ".wdv3/${{ steps.names.outputs.'
        'publication-snapshot-name }}"' not in bind
    )
    assert "--output .wdv3/bound-reviewer-formatter-input.json" in bind
    assert (
        'echo "reviewer-formatter-input-base64=$(base64 -w0 '
        '.wdv3/bound-reviewer-formatter-input.json)" >> "${GITHUB_OUTPUT}"'
        in bind
    )
    assert bind.count("base64 -w0") == 1

    materializer_formatter_output = (
        "${{ steps.bind.outputs.reviewer-formatter-input-base64 }}"
    )
    materializer_formatter_need = (
        "${{ needs.materialize-publication.outputs."
        "reviewer-formatter-input-base64 }}"
    )
    approval_formatter_need = (
        "${{ needs.approval.outputs.reviewer-formatter-input-base64 }}"
    )
    assert (
        materializer["outputs"]["reviewer-formatter-input-base64"]
        == materializer_formatter_output
    )
    assert (
        approval_job["outputs"]["reviewer-formatter-input-base64"]
        == materializer_formatter_need
    )
    assert authorization_formatter_step["env"] == {
        "REVIEWER_FORMATTER_INPUT_BASE64": materializer_formatter_need,
    }
    assert capability_finalizer_step["env"] == {
        "AUTHORIZATION_BASE64": (
            "${{ needs.approval.outputs.authorization-base64 }}"
        ),
        "REVIEWER_FORMATTER_INPUT_BASE64": approval_formatter_need,
        "GITHUB_TOKEN": "${{ github.token }}",
    }

    decoded_formatter_path = ".wdv3/bound-reviewer-formatter-input.json"
    decode_formatter = (
        "printf '%s' \"${REVIEWER_FORMATTER_INPUT_BASE64}\" | "
        f"base64 -d > {decoded_formatter_path}"
    )
    assert authorization_formatter.count(decode_formatter) == 1
    assert capability_finalize.count(decode_formatter) == 1
    assert (
        f"--formatter-input {decoded_formatter_path}" in authorization_formatter
    )
    for consumer in (authorization_formatter, capability_finalize):
        assert "base64 -d > .wdv3/reviewer" not in consumer
        assert f"base64 -w0 {decoded_formatter_path}" not in consumer

    download_settings = reviewer_download["with"]
    assert reviewer_download["uses"] == DOWNLOAD
    assert download_settings["artifact-ids"] == (
        "${{ needs.materialize-publication.outputs.reviewer-artifact-id }}"
    )
    assert download_settings["path"] == ".wdv3/reviewer"
    assert download_settings.get("skip-decompress", False) is False
    assert download_settings["digest-mismatch"] == "error"
    assert '--reviewer-summary ".wdv3/reviewer/reviewer-summary.md"' in admit
    assert (
        '--reviewer-artifact-id "${{ steps.upload-reviewer.outputs.artifact-id }}"'
        in bind
    )
    assert (
        "--reviewer-artifact-digest "
        '"${{ steps.upload-reviewer.outputs.artifact-digest }}"' in bind
    )
    assert (
        "--snapshot-payload-digest "
        '"${{ steps.materialize.outputs.publication-snapshot-payload-digest }}"'
        in bind
    )
    assert (
        '--summary-payload-digest "${{ steps.materialize.outputs.reviewer-digest }}"'
        in bind
    )
    assert (
        "--reviewer-summary-artifact-digest "
        '"${{ needs.materialize-publication.outputs.reviewer-artifact-digest }}"'
        in admit
    )
    assert re.findall(
        r"\bthree-workflow-delivery-v3 release ([a-z0-9-]+)",
        history,
    ) == ["discover-execution-history"]
    assert "--output .wdv3/execution-history-admission.json" in history

    for step in _artifact_steps(document, DOWNLOAD):
        assert "artifact-ids" in step["with"]
        assert "name" not in step["with"]
        assert any(
            source in step["with"]["artifact-ids"]
            for source in ("needs.", "inputs.")
        )
        assert "steps." not in step["with"]["artifact-ids"]
        assert step["with"]["digest-mismatch"] == "error"
        if step["name"] != "Download reviewer payload by artifact ID":
            assert step["with"]["skip-decompress"] is True


def test_authorization_raw_upload_materializes_exact_attempt_basename() -> None:
    document = _document(CALLEE)
    _assert_existing_raw_uploads_keep_physical_names(document)
    jobs = document["jobs"]
    approval_job = jobs["approval"]
    authorization_step = _step(
        approval_job,
        "Fetch exact public target and format Authorization",
    )
    approval = _run(
        _step(
            jobs["approval"],
            "Fetch exact public target and format Authorization",
        )
    )
    approval_finalizer = jobs["approval-finalizer"]
    approval_finalizer_steps = _steps(approval_finalizer)
    admit_step = _step(
        approval_finalizer,
        "Admit exact capability closure",
    )
    admit = _run(_step(approval_finalizer, "Admit exact capability closure"))
    authorization_upload = _step(
        approval_finalizer,
        "Upload Authorization Record",
    )
    publisher = jobs["publish-github-packages"]
    downstream_consumers = (
        _run(_step(publisher, "Preflight publication without npm mutation")),
        _run(_step(publisher, "Publish create-only package action")),
        _run(_step(jobs["release-finalizer"], "Finalize Attempt Outcome")),
    )

    authorization_name = (
        "${{ needs.approval.outputs.authorization-artifact-name }}"
    )
    authorization_path = f".wdv3/{authorization_name}"
    assert authorization_step["id"] == "authorize"
    assert approval_finalizer["needs"] == [
        "materialize-publication",
        "approval",
    ]
    assert "--output .wdv3/authorization.json --github-output" not in approval
    assert "--output .wdv3/authorization.json \\" in approval
    assert (
        "digest=\"$(sha256sum .wdv3/authorization.json | cut -d' ' -f1)\""
        in approval
    )
    assert (
        'name="wdv3-live-buddy-authorization-r${GITHUB_RUN_ID}-'
        'ra${GITHUB_RUN_ATTEMPT}-${digest}.json"' in approval
    )
    assert 'mv .wdv3/authorization.json ".wdv3/${name}"' in approval
    assert 'base64 -w0 ".wdv3/${name}"' in approval
    assert 'authorization_base64="$(base64 -w0 ".wdv3/${name}")"' in approval
    assert approval.count("base64 -w0") == 1
    assert "base64 -w0 .wdv3/authorization.json" not in approval
    assert 'base64 -w0 ".wdv3/authorization.json"' not in approval
    assert (
        'echo "authorization-artifact-name=${name}" >> "${GITHUB_OUTPUT}"'
        in approval
    )
    assert (
        'echo "authorization-base64=${authorization_base64}" '
        '>> "${GITHUB_OUTPUT}"' in approval
    )
    assert approval_job["outputs"]["authorization-artifact-name"] == (
        "${{ steps.authorize.outputs.authorization-artifact-name }}"
    )
    assert approval_job["outputs"]["authorization-base64"] == (
        "${{ steps.authorize.outputs.authorization-base64 }}"
    )
    assert admit_step["env"]["AUTHORIZATION_BASE64"] == (
        "${{ needs.approval.outputs.authorization-base64 }}"
    )
    authorization_decode = (
        "printf '%s' \"${AUTHORIZATION_BASE64}\" | base64 -d > "
        f'"{authorization_path}"'
    )
    assert admit.count(authorization_decode) == 1
    assert approval_finalizer_steps.index(
        admit_step
    ) < approval_finalizer_steps.index(authorization_upload)
    assert authorization_upload["id"] == "upload-authorization"
    assert authorization_upload["with"]["name"] == authorization_name
    assert authorization_upload["with"]["path"] == authorization_path
    assert authorization_upload["with"] == {
        "name": authorization_name,
        "path": authorization_path,
        "if-no-files-found": "error",
        "retention-days": RETENTION_DAYS,
        "overwrite": False,
        "archive": False,
        "include-hidden-files": True,
    }
    assert approval_finalizer["outputs"]["authorization-artifact-name"] == (
        authorization_name
    )
    assert approval_finalizer["outputs"]["authorization-artifact-id"] == (
        "${{ steps.upload-authorization.outputs.artifact-id }}"
    )
    assert approval_finalizer["outputs"]["authorization-artifact-digest"] == (
        "${{ steps.upload-authorization.outputs.artifact-digest }}"
    )
    assert (
        _raw_artifact_name(authorization_upload["with"]) == authorization_name
    )
    for prefix in ("base64 -d >", "--authorization", "sha256sum"):
        assert re.search(
            rf"{re.escape(prefix)}\s+\"?{re.escape(authorization_path)}\"?",
            admit,
        )
    assert ".wdv3/authorization.json" not in admit
    downloaded_path = (
        ".wdv3/input/${{ needs.approval-finalizer.outputs."
        "authorization-artifact-name }}"
    )
    consumer_sites = tuple(
        (job_name, step["name"])
        for job_name in ("publish-github-packages", "release-finalizer")
        for step in _steps(jobs[job_name])
        if isinstance(step.get("run"), str) and downloaded_path in _run(step)
    )
    assert consumer_sites == (
        (
            "publish-github-packages",
            "Preflight publication without npm mutation",
        ),
        ("publish-github-packages", "Publish create-only package action"),
        ("release-finalizer", "Finalize Attempt Outcome"),
    )
    downstream_commands = "\n".join(downstream_consumers)
    assert downstream_commands.count(downloaded_path) == len(consumer_sites)
    assert ".wdv3/input/authorization.json" not in downstream_commands
    for consumer in downstream_consumers:
        assert downloaded_path in consumer
        assert ".wdv3/input/authorization.json" not in consumer
        assert (
            "--authorization-artifact-id "
            '"${{ needs.approval-finalizer.outputs.authorization-artifact-id }}"'
            in consumer
        )
        assert (
            "--authorization-artifact-digest "
            '"${{ needs.approval-finalizer.outputs.'
            'authorization-artifact-digest }}"' in consumer
        )


def test_mutation_marker_raw_upload_and_consumers_use_attempt_basename() -> (
    None
):
    document = _document(CALLEE)
    _assert_existing_raw_uploads_keep_physical_names(document)
    publisher = document["jobs"]["publish-github-packages"]
    publisher_steps = _steps(publisher)
    marker_step = _step(
        publisher,
        "Form mutation may-have-started marker",
    )
    marker = _run(_step(publisher, "Form mutation may-have-started marker"))
    marker_upload = _step(
        publisher,
        "Upload mutation may-have-started marker",
    )
    publish = _run(_step(publisher, "Publish create-only package action"))
    bundle = _run(_step(publisher, "Form Capability Group Result Bundle"))

    marker_name = (
        "wdv3-live-buddy-mutation-may-have-started-"
        "r${{ github.run_id }}-ra${{ github.run_attempt }}"
    )
    marker_path = f".wdv3/{marker_name}"
    marker_shell_name = (
        "wdv3-live-buddy-mutation-may-have-started-"
        "r${GITHUB_RUN_ID}-ra${GITHUB_RUN_ATTEMPT}"
    )
    marker_shell_path = f".wdv3/{marker_shell_name}"
    assert marker_step["id"] == "mark-mutation"
    assert marker_upload["id"] == "upload-mutation-marker"
    assert publisher_steps.index(marker_step) < publisher_steps.index(
        marker_upload
    )
    assert f'--marker-output "{marker_shell_path}"' in marker
    assert marker_upload["with"]["name"] == marker_name
    assert marker_upload["with"]["path"] == marker_path
    assert marker_upload["with"] == {
        "name": marker_name,
        "path": marker_path,
        "if-no-files-found": "error",
        "retention-days": RETENTION_DAYS,
        "overwrite": False,
        "archive": False,
        "include-hidden-files": True,
    }
    assert _raw_artifact_name(marker_upload["with"]) == marker_name
    assert publisher["outputs"]["mutation-marker-artifact-id"] == (
        "${{ steps.upload-mutation-marker.outputs.artifact-id }}"
    )
    assert publisher["outputs"]["mutation-marker-artifact-digest"] == (
        "${{ steps.upload-mutation-marker.outputs.artifact-digest }}"
    )
    assert (
        f'marker_name="{marker_shell_name}"' in marker
        or marker_shell_path in marker
    )
    assert (
        f"--marker-output {marker_shell_path}" in marker
        or f'--marker-output "{marker_shell_path}"' in marker
        or (
            "mv .wdv3/mutation-may-have-started.json "
            f'"{marker_shell_path}"' in marker
        )
        or (
            f'marker_name="{marker_shell_name}"' in marker
            and (
                "mv .wdv3/mutation-may-have-started.json "
                '".wdv3/${marker_name}"' in marker
            )
        )
    )
    for consumer in (publish, bundle):
        assert marker_path in consumer
        assert ".wdv3/mutation-may-have-started.json" not in consumer
    assert f'--mutation-marker "{marker_path}"' in publish
    assert f'--mutation-marker "{marker_path}"' in bundle
    assert (
        "--mutation-marker-artifact-id "
        '"${{ steps.upload-mutation-marker.outputs.artifact-id }}"' in bundle
    )
    assert (
        '--mutation-marker-artifact-id "${{ steps.upload-mutation-marker.outputs.'
        'artifact-id }}"' in publish
    )
    assert (
        "--mutation-marker-artifact-digest "
        '"${{ steps.upload-mutation-marker.outputs.artifact-digest }}"'
        in publish
    )


@pytest.mark.parametrize(
    ("job_name", "step_name", "expected_ids"),
    [
        (
            "publish-github-packages",
            "Download publisher closure by artifact ID",
            (
                "${{ needs.approval-finalizer.outputs."
                "qualification-snapshot-artifact-id }}",
                "${{ needs.approval-finalizer.outputs.decision-artifact-id }}",
                "${{ needs.approval-finalizer.outputs.adapter-context-artifact-id }}",
                "${{ needs.approval-finalizer.outputs."
                "release-artifact-artifact-id }}",
                "${{ needs.approval-finalizer.outputs."
                "publication-snapshot-artifact-id }}",
                "${{ needs.approval-finalizer.outputs.authorization-artifact-id }}",
                "${{ needs.approval-finalizer.outputs."
                "capability-decision-artifact-id }}",
                "${{ needs.approval-finalizer.outputs.tarball-artifact-id }}",
            ),
        ),
    ],
    ids=("publisher-closure",),
)
def test_authority_record_multidownload_is_comma_delimited_flat_merged_raw(
    job_name: str,
    step_name: str,
    expected_ids: tuple[str, ...],
) -> None:
    step = _step(_document(CALLEE)["jobs"][job_name], step_name)

    assert step["uses"] == DOWNLOAD
    assert step["with"] == {
        "artifact-ids": ",".join(expected_ids),
        "path": ".wdv3/input",
        "merge-multiple": True,
        "skip-decompress": True,
        "digest-mismatch": "error",
    }


def test_all_actions_are_full_sha_pinned_with_version_comments() -> None:
    raw = CALLER.read_text(encoding="utf-8") + CALLEE.read_text(
        encoding="utf-8"
    )
    documents = (_document(CALLER), _document(CALLEE))
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
    assert {
        str(step["uses"])
        for document in documents
        for job in document["jobs"].values()
        for step in _steps(job)
        if "uses" in step
    } == {CHECKOUT, UV, MISE, UPLOAD, DOWNLOAD}


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
        "admit",
        "qualification-finalizer",
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

    assert finalizer["if"] == (
        "always() && needs.materialize-publication.result != 'skipped'"
    )
    assert set(_needs(finalizer)) == {"materialize-publication", "approval"}
    assert "needs.approval.result" in command
    assert "--authorization" in command
    assert "unknown-replayable-approval-contract" in command


def test_commit8_dag_order_retention_and_error_propagation_are_exact() -> None:
    jobs = _document(CALLEE)["jobs"]
    assert set(jobs) == EXPECTED_JOBS
    assert jobs["qualification-finalizer"]["if"] == (
        "always() && needs.build-tarball.result != 'skipped'"
    )
    assert jobs["approval-finalizer"]["if"] == (
        "always() && needs.materialize-publication.result != 'skipped'"
    )
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
    preflight_command = _run(preflight)
    marker_command = _run(marker)
    preflight_path = ".wdv3/publication-preflight.json"
    marker_name = (
        "wdv3-live-buddy-mutation-may-have-started-"
        "r${{ github.run_id }}-ra${{ github.run_attempt }}"
    )
    marker_path = f".wdv3/{marker_name}"

    assert steps.index(preflight) < steps.index(marker) < steps.index(upload)
    assert steps.index(upload) < steps.index(publish)
    assert f"--preflight-output {preflight_path}" in preflight_command
    assert f"--preflight {preflight_path}" in marker_command
    assert (
        '--marker-output ".wdv3/wdv3-live-buddy-mutation-may-have-started-'
        'r${GITHUB_RUN_ID}-ra${GITHUB_RUN_ATTEMPT}"' in marker_command
    )
    assert marker_path not in preflight_command
    assert ".wdv3/mutation-may-have-started.json" not in marker_command
    assert preflight_path != marker_path
    assert PurePosixPath(preflight_path).name != marker_name
    assert upload["with"] == {
        "name": marker_name,
        "path": marker_path,
        "if-no-files-found": "error",
        "retention-days": RETENTION_DAYS,
        "overwrite": False,
        "archive": False,
        "include-hidden-files": True,
    }
    assert _raw_artifact_name(upload["with"]) == marker_name
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


def test_history_discovery_uses_caller_path_through_reusable_live_attempt_topology() -> (
    None
):
    caller = _document(CALLER)
    callee = _document(CALLEE)
    caller_document = cast("dict[object, Any]", caller)
    callee_document = cast("dict[object, Any]", callee)
    caller_triggers = caller_document.get(
        "on",
        caller_document.get(True),
    )
    callee_triggers = callee_document.get(
        "on",
        callee_document.get(True),
    )
    caller_jobs = caller["jobs"]
    callee_jobs = callee["jobs"]

    assert caller_triggers == {"workflow_dispatch": None}
    assert isinstance(callee_triggers, dict)
    assert set(callee_triggers) == {"workflow_call"}
    assert set(caller_jobs) == {
        "request",
        "discover-node",
        "compile-model",
        "evaluate-live-eligibility",
        "run-live-attempt",
    }
    assert (
        caller_jobs["run-live-attempt"]["uses"]
        == "./.github/workflows/workflow-delivery-v3-live-attempt.yml"
    )
    assert set(callee_jobs) == EXPECTED_JOBS

    command = _run(
        _step(
            callee_jobs["admit"],
            "Discover exhaustive retained execution history",
        )
    )
    assert re.findall(
        r"\bthree-workflow-delivery-v3 release ([a-z0-9-]+)",
        command,
    ) == ["discover-execution-history"]
    assert re.findall(r'--workflow-path "([^"]+)"', command) == [
        ".github/workflows/workflow-delivery-v3-buddy-smoke.yml"
    ]
    assert (
        ".github/workflows/workflow-delivery-v3-live-attempt.yml" not in command
    )
