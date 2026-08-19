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
_WORKFLOW_CANCELLATION_AUTHORITIES = (
    "admit",
    "qualification-finalizer",
    "observe-github-packages",
    "materialize-publication",
    "approval-finalizer",
    "publish-github-packages",
)
_WORKFLOW_CANCELLATION_FACT = (
    "needs.workflow-cancellation.outputs.workflow-cancelled || 'false'"
)
_LEGACY_WORKFLOW_CANCELLATION_FACT = (
    "steps.workflow-cancellation.outputs.workflow-cancelled || 'false'"
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
    "workflow-cancellation",
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
    assert _needs(jobs["workflow-cancellation"]) == (
        _WORKFLOW_CANCELLATION_AUTHORITIES
    )
    assert _needs(jobs["release-finalizer"]) == (
        *_WORKFLOW_CANCELLATION_AUTHORITIES,
        "workflow-cancellation",
    )


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
    assert {
        name: qualification_finalizer["outputs"][name]
        for name in (
            "decision-artifact-id",
            "decision-artifact-name",
            "build-evidence-artifact-id",
            "build-evidence-artifact-name",
            "project-test-evidence-artifact-id",
            "project-test-evidence-artifact-name",
            "artifact-contents-evidence-artifact-id",
            "artifact-contents-evidence-artifact-name",
            "install-import-evidence-artifact-id",
            "install-import-evidence-artifact-name",
            "qualification-snapshot-artifact-id",
            "qualification-snapshot-artifact-name",
            "adapter-context-artifact-id",
            "adapter-context-artifact-name",
            "release-artifact-artifact-id",
            "release-artifact-artifact-name",
        )
    } == {
        "decision-artifact-id": "${{ steps.upload.outputs.artifact-id }}",
        "decision-artifact-name": (
            "${{ steps.finalize.outputs.qualification-decision-artifact-name }}"
        ),
        "build-evidence-artifact-id": (
            "${{ needs.build-tarball.outputs.build-evidence-artifact-id }}"
        ),
        "build-evidence-artifact-name": (
            "${{ needs.build-tarball.outputs.build-evidence-artifact-name }}"
        ),
        "project-test-evidence-artifact-id": (
            "${{ needs.project-test.outputs.evidence-artifact-id }}"
        ),
        "project-test-evidence-artifact-name": (
            "${{ needs.project-test.outputs.evidence-artifact-name }}"
        ),
        "artifact-contents-evidence-artifact-id": (
            "${{ needs.npm-artifact-qualification.outputs."
            "contents-evidence-artifact-id }}"
        ),
        "artifact-contents-evidence-artifact-name": (
            "${{ needs.npm-artifact-qualification.outputs."
            "contents-evidence-artifact-name }}"
        ),
        "install-import-evidence-artifact-id": (
            "${{ needs.npm-artifact-qualification.outputs."
            "install-evidence-artifact-id }}"
        ),
        "install-import-evidence-artifact-name": (
            "${{ needs.npm-artifact-qualification.outputs."
            "install-evidence-artifact-name }}"
        ),
        "qualification-snapshot-artifact-id": (
            "${{ needs.build-tarball.outputs."
            "qualification-snapshot-artifact-id }}"
        ),
        "qualification-snapshot-artifact-name": (
            "${{ needs.build-tarball.outputs."
            "qualification-snapshot-artifact-name }}"
        ),
        "adapter-context-artifact-id": (
            "${{ needs.build-tarball.outputs.adapter-context-artifact-id }}"
        ),
        "adapter-context-artifact-name": (
            "${{ needs.build-tarball.outputs.adapter-context-artifact-name }}"
        ),
        "release-artifact-artifact-id": (
            "${{ needs.build-tarball.outputs.release-artifact-artifact-id }}"
        ),
        "release-artifact-artifact-name": (
            "${{ needs.build-tarball.outputs.release-artifact-artifact-name }}"
        ),
    }
    assert {
        name: expression
        for name, expression in qualification_finalizer["outputs"].items()
        if name.endswith("digest")
    } == {
        "decision-artifact-digest": (
            "${{ steps.upload.outputs.artifact-digest }}"
        ),
        "decision-digest": (
            "${{ steps.finalize.outputs.qualification-decision-digest }}"
        ),
        "build-evidence-artifact-digest": (
            "${{ needs.build-tarball.outputs.build-evidence-artifact-digest }}"
        ),
        "build-evidence-digest": (
            "${{ needs.build-tarball.outputs.build-evidence-digest }}"
        ),
        "project-test-evidence-artifact-digest": (
            "${{ needs.project-test.outputs.evidence-artifact-digest }}"
        ),
        "project-test-evidence-digest": (
            "${{ needs.project-test.outputs.evidence-digest }}"
        ),
        "artifact-contents-evidence-artifact-digest": (
            "${{ needs.npm-artifact-qualification.outputs."
            "contents-evidence-artifact-digest }}"
        ),
        "artifact-contents-evidence-digest": (
            "${{ needs.npm-artifact-qualification.outputs."
            "contents-evidence-digest }}"
        ),
        "install-import-evidence-artifact-digest": (
            "${{ needs.npm-artifact-qualification.outputs."
            "install-evidence-artifact-digest }}"
        ),
        "install-import-evidence-digest": (
            "${{ needs.npm-artifact-qualification.outputs."
            "install-evidence-digest }}"
        ),
        "qualification-snapshot-artifact-digest": (
            "${{ needs.build-tarball.outputs."
            "qualification-snapshot-artifact-digest }}"
        ),
        "qualification-snapshot-digest": (
            "${{ needs.build-tarball.outputs.qualification-snapshot-digest }}"
        ),
        "adapter-context-artifact-digest": (
            "${{ needs.build-tarball.outputs.adapter-context-artifact-digest }}"
        ),
        "adapter-context-digest": (
            "${{ needs.build-tarball.outputs.adapter-context-digest }}"
        ),
        "release-artifact-artifact-digest": (
            "${{ needs.build-tarball.outputs."
            "release-artifact-artifact-digest }}"
        ),
        "release-artifact-digest": (
            "${{ needs.build-tarball.outputs.release-artifact-digest }}"
        ),
        "tarball-artifact-digest": (
            "${{ needs.build-tarball.outputs.tarball-artifact-digest }}"
        ),
    }
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
        "observe-github-packages",
        "materialize-publication",
        "approval-finalizer",
        "publish-github-packages",
        "workflow-cancellation",
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
            "always() && needs.qualification-finalizer.outputs."
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
        "always() && needs.materialize-publication.outputs."
        "publication-snapshot-artifact-id != ''"
    )
    assert publication_download["with"]["artifact-ids"] == (
        "${{ needs.materialize-publication.outputs."
        "publication-snapshot-artifact-id }}"
    )
    finalize = _run(_step(release_finalizer, "Finalize Attempt Outcome"))
    assert (
        'if [[ -n "${{ needs.materialize-publication.outputs.'
        'publication-snapshot-artifact-id }}" ]]' in finalize
    )
    assert finalize.count("--publication-snapshot ") == 1
    for option, output in (
        ("publication-snapshot-digest", "publication-snapshot-digest"),
        (
            "publication-snapshot-artifact-id",
            "publication-snapshot-artifact-id",
        ),
        (
            "publication-snapshot-artifact-digest",
            "publication-snapshot-artifact-digest",
        ),
    ):
        assert (
            f'--{option} "${{{{ needs.materialize-publication.outputs.'
            f'{output} }}}}"' in finalize
        )
    assert (
        '--publication-snapshot ".wdv3/input/'
        "${{ needs.materialize-publication.outputs."
        'publication-snapshot-artifact-name }}"' in finalize
    )
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
        *_WORKFLOW_CANCELLATION_AUTHORITIES,
        "workflow-cancellation",
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


def test_publication_preparation_interruption_uses_direct_platform_facts() -> (
    None
):
    jobs = _document(CALLEE)["jobs"]
    finalizer = jobs["release-finalizer"]
    finalize = _step(finalizer, "Finalize Attempt Outcome")
    command = _run(finalize)

    assert _needs(finalizer) == (
        *_WORKFLOW_CANCELLATION_AUTHORITIES,
        "workflow-cancellation",
    )
    for fact in (
        "needs.qualification-finalizer.outputs.qualification-result",
        "needs.observe-github-packages.result",
        "needs.materialize-publication.result",
        (
            "needs.materialize-publication.outputs."
            "publication-snapshot-artifact-id"
        ),
        "needs.publish-github-packages.result",
        _WORKFLOW_CANCELLATION_FACT,
    ):
        assert fact in command
    assert all(
        step.get("name") != "Record workflow cancellation"
        for step in _steps(finalizer)
    )
    assert jobs["workflow-cancellation"]["if"] == "cancelled()"
    assert _LEGACY_WORKFLOW_CANCELLATION_FACT not in command
    assert finalize["if"] == "success() || cancelled()"
    assert (
        'qualification_result}" == "success" && -z "${snapshot_id}"' in command
    )
    assert 'materialization_result}" == "success"' in command
    assert "snapshot_name=" not in command
    assert "snapshot_payload_digest=" not in command
    assert 'publish_result}" != "skipped"' in command
    assert "Publication Snapshot transport is only partially absent" in command
    assert "Publication preparation state is not an admitted interruption" in (
        command
    )
    assert "Publication preparation interruption has downstream lineage" in (
        command
    )
    assert "args+=(--publication-preparation-interrupted)" in command
    assert command.index("args+=(--publication-preparation-interrupted)") < (
        command.index("three-workflow-delivery-v3 release finalize-live")
    )


def test_publication_preparation_rejects_crossed_interruption_states(
    tmp_path: Path,
) -> None:
    execution = _phase2_execute_finalizer_shell(
        tmp_path,
        _phase2_finalizer_facts(
            **{
                "needs.observe-github-packages.result": "cancelled",
                "needs.materialize-publication.result": "failure",
            }
        ),
    )

    assert execution["status"] != 0
    assert execution["invocations"] == ()
    assert "not an admitted interruption" in execution["output"]


def test_publication_preparation_diagnostics_are_retained_before_failure() -> (
    None
):
    finalizer = _document(CALLEE)["jobs"]["release-finalizer"]
    command = _run(_step(finalizer, "Finalize Attempt Outcome"))

    assert "## Publication preparation interruption" in command
    for diagnostic in (
        "Qualification result:",
        "Observation job result:",
        "Materialization job result:",
        "Durable Publication Snapshot: absent",
        "Capability path started: no",
        "Workflow cancellation observed:",
    ):
        assert diagnostic in command
    assert "tee -a .wdv3/final-attempt/attempt-summary.md" in command
    assert '>> "${GITHUB_STEP_SUMMARY}"' in command
    assert command.index("tee -a .wdv3/final-attempt/attempt-summary.md") < (
        command.index('outcome_digest="$(sha256sum')
    )


def test_release_finalizer_propagates_failure_after_retention() -> None:
    finalizer = _document(CALLEE)["jobs"]["release-finalizer"]
    steps = _steps(finalizer)
    finalize = _step(finalizer, "Finalize Attempt Outcome")
    upload = _step(finalizer, "Upload final Attempt Outcome and summary")
    propagate = _step(finalizer, "Propagate finalization status")
    names = [step["name"] for step in steps]

    assert names.index(finalize["name"]) < names.index(upload["name"])
    assert names.index(upload["name"]) < names.index(propagate["name"])
    assert propagate["if"] == "always()"
    command = _run(propagate)
    assert "steps.finalize.outcome" in command
    assert "steps.upload-final.outcome" in command
    assert "steps.finalize.outputs.finalizer-status" in command
    assert '!= "0"' in command
    assert "exit 1" in command


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


def test_release_finalizer_platform_fact_mapping_executes_workflow_shell(
    tmp_path: Path,
) -> None:
    execution = _phase2_execute_finalizer_shell(
        tmp_path,
        _phase2_finalizer_facts(
            **(
                _PHASE2_POST_SNAPSHOT_OVERRIDES
                | _PHASE2_RESULT_BUNDLE_OVERRIDES
                | {
                    "needs.publish-github-packages.outputs.mutation-marker-artifact-id": "734",
                    "needs.publish-github-packages.result": "failure",
                }
            )
        ),
    )

    argv = _phase2_assert_successful_finalizer(execution)
    assert argv.count("--capability-may-have-started") == 1
    assert "--platform-terminated" not in argv
    assert "--publication-preparation-interrupted" not in argv


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


_PHASE2_FINALIZER_EXPRESSION = re.compile(r"\$\{\{\s*(?P<fact>.*?)\s*\}\}")
_PHASE2_SNAPSHOT_NAME = "phase2-publication-snapshot.json"
_PHASE2_SNAPSHOT_PAYLOAD_DIGEST = "sha256:" + ("b" * 64)
_PHASE2_SNAPSHOT_ARTIFACT_ID = "731"
_PHASE2_SNAPSHOT_UPLOAD_DIGEST = "sha256:" + ("c" * 64)


def _phase2_finalizer_facts(**overrides: str) -> dict[str, str]:
    record_digest = "sha256:" + ("a" * 64)
    upload_digest = "sha256:" + ("d" * 64)
    facts = {
        "inputs.target-sha": "1" * 40,
        "needs.admit.outputs.attempt-artifact-digest": upload_digest,
        "needs.admit.outputs.attempt-artifact-id": "101",
        "needs.admit.outputs.attempt-artifact-name": "attempt-binding.json",
        "needs.admit.outputs.attempt-digest": record_digest,
        "needs.approval-finalizer.outputs.authorization-artifact-digest": "",
        "needs.approval-finalizer.outputs.authorization-artifact-id": "",
        "needs.approval-finalizer.outputs.authorization-artifact-name": "",
        "needs.approval-finalizer.outputs.authorization-digest": "",
        "needs.approval-finalizer.outputs.capability-decision-artifact-digest": "",
        "needs.approval-finalizer.outputs.capability-decision-artifact-id": "",
        "needs.approval-finalizer.outputs.capability-decision-artifact-name": "",
        "needs.approval-finalizer.outputs.capability-decision-digest": "",
        "needs.approval-finalizer.outputs.publication-snapshot-artifact-id": "",
        "needs.materialize-publication.outputs.publication-snapshot-artifact-digest": "",
        "needs.materialize-publication.outputs.publication-snapshot-artifact-id": "",
        "needs.materialize-publication.outputs.publication-snapshot-artifact-name": "",
        "needs.materialize-publication.outputs.publication-snapshot-digest": "",
        "needs.materialize-publication.result": "skipped",
        "needs.observe-github-packages.result": "failure",
        "needs.publish-github-packages.outputs.capability-group-bundle-artifact-digest": "",
        "needs.publish-github-packages.outputs.capability-group-bundle-artifact-id": "",
        "needs.publish-github-packages.outputs.capability-group-bundle-artifact-name": "",
        "needs.publish-github-packages.outputs.capability-group-bundle-digest": "",
        "needs.publish-github-packages.outputs.mutation-marker-artifact-id": "",
        "needs.publish-github-packages.outputs.receipt-artifact-digest": "",
        "needs.publish-github-packages.outputs.receipt-artifact-id": "",
        "needs.publish-github-packages.outputs.receipt-artifact-name": "",
        "needs.publish-github-packages.outputs.receipt-digest": "",
        "needs.publish-github-packages.result": "skipped",
        "needs.qualification-finalizer.outputs.artifact-contents-evidence-artifact-digest": upload_digest,
        "needs.qualification-finalizer.outputs.artifact-contents-evidence-artifact-id": "104",
        "needs.qualification-finalizer.outputs.artifact-contents-evidence-artifact-name": "artifact-contents-evidence.json",
        "needs.qualification-finalizer.outputs.artifact-contents-evidence-digest": record_digest,
        "needs.qualification-finalizer.outputs.build-evidence-artifact-digest": upload_digest,
        "needs.qualification-finalizer.outputs.build-evidence-artifact-id": "102",
        "needs.qualification-finalizer.outputs.build-evidence-artifact-name": "build-evidence.json",
        "needs.qualification-finalizer.outputs.build-evidence-digest": record_digest,
        "needs.qualification-finalizer.outputs.decision-artifact-digest": upload_digest,
        "needs.qualification-finalizer.outputs.decision-artifact-id": "108",
        "needs.qualification-finalizer.outputs.decision-artifact-name": "qualification-decision.json",
        "needs.qualification-finalizer.outputs.decision-digest": record_digest,
        "needs.qualification-finalizer.outputs.install-import-evidence-artifact-digest": upload_digest,
        "needs.qualification-finalizer.outputs.install-import-evidence-artifact-id": "105",
        "needs.qualification-finalizer.outputs.install-import-evidence-artifact-name": "install-import-evidence.json",
        "needs.qualification-finalizer.outputs.install-import-evidence-digest": record_digest,
        "needs.qualification-finalizer.outputs.project-test-evidence-artifact-digest": upload_digest,
        "needs.qualification-finalizer.outputs.project-test-evidence-artifact-id": "103",
        "needs.qualification-finalizer.outputs.project-test-evidence-artifact-name": "project-test-evidence.json",
        "needs.qualification-finalizer.outputs.project-test-evidence-digest": record_digest,
        "needs.qualification-finalizer.outputs.qualification-result": "success",
        "needs.qualification-finalizer.outputs.qualification-snapshot-artifact-digest": upload_digest,
        "needs.qualification-finalizer.outputs.qualification-snapshot-artifact-id": "107",
        "needs.qualification-finalizer.outputs.qualification-snapshot-artifact-name": "qualification-snapshot.json",
        "needs.qualification-finalizer.outputs.qualification-snapshot-digest": record_digest,
        "needs.qualification-finalizer.outputs.release-artifact-artifact-digest": upload_digest,
        "needs.qualification-finalizer.outputs.release-artifact-artifact-id": "106",
        "needs.qualification-finalizer.outputs.release-artifact-artifact-name": "release-artifact.json",
        "needs.qualification-finalizer.outputs.release-artifact-digest": record_digest,
        "needs.workflow-cancellation.outputs.workflow-cancelled || 'false'": "false",
    }
    unknown = set(overrides) - facts.keys()
    assert not unknown, f"unknown finalizer facts: {sorted(unknown)}"
    facts.update(overrides)
    return facts


def _phase2_render_finalizer_run(facts: dict[str, str]) -> str:
    run = _run(
        _step(
            _document(CALLEE)["jobs"]["release-finalizer"],
            "Finalize Attempt Outcome",
        )
    )
    expressions = {
        match.group("fact").strip()
        for match in _PHASE2_FINALIZER_EXPRESSION.finditer(run)
    }
    assert expressions == set(facts)
    rendered = _PHASE2_FINALIZER_EXPRESSION.sub(
        lambda match: facts[match.group("fact").strip()],
        run,
    )
    assert "${{" not in rendered
    return rendered


def _phase2_execute_finalizer_shell(
    tmp_path: Path,
    facts: dict[str, str],
) -> dict[str, Any]:
    import os  # noqa: PLC0415
    import subprocess  # noqa: PLC0415

    run = _phase2_render_finalizer_run(facts)
    input_directory = tmp_path / ".wdv3/input"
    input_directory.mkdir(parents=True)
    for expression, value in facts.items():
        if expression.endswith("-artifact-name") and value:
            (input_directory / value).write_text("{}\n", encoding="utf-8")

    bin_directory = tmp_path / "bin"
    bin_directory.mkdir()
    invocations = tmp_path / "cli-invocations.jsonl"
    uv = bin_directory / "uv"
    uv.write_text(
        r"""#!/usr/bin/env python3
import json
import os
import pathlib
import sys

args = sys.argv[1:]
invocations = pathlib.Path(os.environ["PHASE2_CLI_INVOCATIONS"])
with invocations.open("a", encoding="utf-8") as handle:
    handle.write(json.dumps(args) + "\n")

def write_flag(flag, content):
    path = pathlib.Path(args[args.index(flag) + 1])
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")

write_flag("--outcome-output", '{"phase":"publication-preparation"}\n')
write_flag("--summary-output", "# Attempt summary\n")
github_output = pathlib.Path(args[args.index("--github-output") + 1])
with github_output.open("a", encoding="utf-8") as handle:
    handle.write("cli-boundary-invoked=true\n")
raise SystemExit(int(os.environ.get("PHASE2_CLI_STATUS", "0")))
""",
        encoding="utf-8",
    )
    uv.chmod(0o755)

    github_output = tmp_path / "github-output.txt"
    github_summary = tmp_path / "github-step-summary.md"
    environment = os.environ | {
        "GITHUB_OUTPUT": str(github_output),
        "GITHUB_RUN_ATTEMPT": "3",
        "GITHUB_RUN_ID": "424242",
        "GITHUB_STEP_SUMMARY": str(github_summary),
        "PATH": f"{bin_directory}{os.pathsep}{os.environ['PATH']}",
        "PHASE2_CLI_INVOCATIONS": str(invocations),
        "PHASE2_CLI_STATUS": "0",
        "WDV3_PACKAGE": "three-workflow-delivery-v3",
    }
    completed = subprocess.run(  # noqa: S603
        (
            "bash",
            "--noprofile",
            "--norc",
            "-euo",
            "pipefail",
            "-c",
            run,
        ),
        check=False,
        cwd=tmp_path,
        env=environment,
        capture_output=True,
        text=True,
        timeout=10,
    )
    invocation_rows = (
        tuple(
            tuple(json.loads(line))
            for line in invocations.read_text(encoding="utf-8").splitlines()
        )
        if invocations.exists()
        else ()
    )
    return {
        "github_output": (
            github_output.read_text(encoding="utf-8")
            if github_output.exists()
            else ""
        ),
        "github_summary": github_summary,
        "invocations": invocation_rows,
        "output": completed.stdout + completed.stderr,
        "status": completed.returncode,
        "summary": tmp_path / ".wdv3/final-attempt/attempt-summary.md",
    }


def _phase2_assert_successful_finalizer(
    execution: dict[str, Any],
) -> tuple[str, ...]:
    assert execution["status"] == 0, execution["output"]
    assert len(execution["invocations"]) == 1
    argv = execution["invocations"][0]
    assert argv[:8] == (
        "run",
        "--python",
        "3.13",
        "--package",
        "three-workflow-delivery-v3",
        "three-workflow-delivery-v3",
        "release",
        "finalize-live",
    )
    assert "cli-boundary-invoked=true" in execution["github_output"]
    assert "finalizer-status=0" in execution["github_output"]
    assert re.search(
        r"final-artifact-name=wdv3-live-buddy-attempt-outcome-"
        r"r424242-ra3-[0-9a-f]{64}",
        execution["github_output"],
    )
    return argv


@pytest.mark.parametrize(
    (
        "workflow_cancelled",
        "observation_result",
        "materialization_result",
    ),
    [
        ("false", "failure", "skipped"),
        ("false", "failure", "cancelled"),
        ("false", "cancelled", "skipped"),
        ("false", "cancelled", "cancelled"),
        ("false", "success", "failure"),
        ("false", "success", "cancelled"),
        ("true", "skipped", "skipped"),
        ("true", "success", "skipped"),
    ],
    ids=[
        "observation-failure__materialization-skipped",
        "observation-failure__materialization-cancelled",
        "observation-cancelled__materialization-skipped",
        "observation-cancelled__materialization-cancelled",
        "observation-success__snapshot-upload-failure",
        "observation-success__materialization-cancelled",
        "workflow-cancelled__observation-skipped__materialization-skipped",
        "workflow-cancelled__observation-success__materialization-skipped",
    ],
)
def test_publication_preparation_classifier_executes_workflow_shell(
    tmp_path: Path,
    workflow_cancelled: str,
    observation_result: str,
    materialization_result: str,
) -> None:
    facts = _phase2_finalizer_facts(
        **{
            "needs.materialize-publication.result": materialization_result,
            "needs.observe-github-packages.result": observation_result,
            "needs.workflow-cancellation.outputs.workflow-cancelled || 'false'": workflow_cancelled,
        }
    )

    execution = _phase2_execute_finalizer_shell(tmp_path, facts)

    argv = _phase2_assert_successful_finalizer(execution)
    assert argv.count("--publication-preparation-interrupted") == 1
    assert "--platform-terminated" not in argv
    assert {
        "--authorization",
        "--capability-decision",
        "--capability-group-bundle",
        "--capability-may-have-started",
        "--publication-snapshot",
        "--receipt",
    }.isdisjoint(argv)


def test_successful_observation_cancellation_retains_exact_job_diagnostics(
    tmp_path: Path,
) -> None:
    execution = _phase2_execute_finalizer_shell(
        tmp_path,
        _phase2_finalizer_facts(
            **{
                "needs.observe-github-packages.result": "success",
                "needs.materialize-publication.result": "skipped",
                "needs.publish-github-packages.result": "cancelled",
                "needs.workflow-cancellation.outputs.workflow-cancelled || 'false'": "true",
            }
        ),
    )

    argv = _phase2_assert_successful_finalizer(execution)
    assert argv.count("--publication-preparation-interrupted") == 1
    assert {
        "--authorization",
        "--capability-decision",
        "--capability-group-bundle",
        "--capability-may-have-started",
        "--platform-terminated",
        "--publication-snapshot",
        "--receipt",
    }.isdisjoint(argv)

    expected_diagnostics = (
        "\n## Publication preparation interruption\n\n"
        "- Qualification result: success\n"
        "- Observation job result: success\n"
        "- Materialization job result: skipped\n"
        "- Publisher job result: cancelled\n"
        "- Durable Publication Snapshot: absent\n"
        "- Capability path started: no\n"
        "- Workflow cancellation observed: true\n"
    )
    assert execution["summary"].read_text(encoding="utf-8") == (
        "# Attempt summary\n" + expected_diagnostics
    )
    assert (
        execution["github_summary"].read_text(encoding="utf-8")
        == expected_diagnostics
    )


@pytest.mark.parametrize(
    ("overrides", "diagnostic_tokens"),
    [
        (
            {
                "needs.observe-github-packages.result": "success",
                "needs.materialize-publication.result": "skipped",
            },
            ("Publication preparation state", "admitted interruption"),
        ),
        (
            {
                "needs.observe-github-packages.result": "success",
                "needs.materialize-publication.result": "success",
            },
            ("Snapshot",),
        ),
        (
            {
                "needs.observe-github-packages.result": "success",
                "needs.materialize-publication.result": "failure",
                "needs.materialize-publication.outputs.publication-snapshot-artifact-id": _PHASE2_SNAPSHOT_ARTIFACT_ID,
            },
            ("Snapshot", "transport"),
        ),
        (
            {
                "needs.observe-github-packages.result": "success",
                "needs.materialize-publication.result": "failure",
                "needs.materialize-publication.outputs.publication-snapshot-artifact-digest": _PHASE2_SNAPSHOT_UPLOAD_DIGEST,
            },
            ("Snapshot", "transport"),
        ),
        (
            {"needs.publish-github-packages.result": "success"},
            ("publisher",),
        ),
        (
            {"needs.publish-github-packages.result": "failure"},
            ("publisher",),
        ),
    ],
    ids=[
        "unexplained-observation-skip",
        "materialization-success-without-durable-snapshot",
        "snapshot-artifact-id-without-upload-digest",
        "snapshot-upload-digest-without-artifact-id",
        "publisher-success",
        "publisher-failure",
    ],
)
def test_publication_preparation_classifier_rejects_invalid_workflow_facts(
    tmp_path: Path,
    overrides: dict[str, str],
    diagnostic_tokens: tuple[str, ...],
) -> None:
    execution = _phase2_execute_finalizer_shell(
        tmp_path,
        _phase2_finalizer_facts(**overrides),
    )

    assert execution["status"] != 0
    assert execution["invocations"] == ()
    diagnostic = execution["output"].casefold()
    for token in diagnostic_tokens:
        assert token.casefold() in diagnostic


_PHASE2_POST_SNAPSHOT_OVERRIDES = {
    "needs.observe-github-packages.result": "success",
    "needs.materialize-publication.result": "success",
    "needs.materialize-publication.outputs.publication-snapshot-artifact-digest": _PHASE2_SNAPSHOT_UPLOAD_DIGEST,
    "needs.materialize-publication.outputs.publication-snapshot-artifact-id": _PHASE2_SNAPSHOT_ARTIFACT_ID,
    "needs.materialize-publication.outputs.publication-snapshot-artifact-name": _PHASE2_SNAPSHOT_NAME,
    "needs.materialize-publication.outputs.publication-snapshot-digest": _PHASE2_SNAPSHOT_PAYLOAD_DIGEST,
    "needs.approval-finalizer.outputs.publication-snapshot-artifact-id": _PHASE2_SNAPSHOT_ARTIFACT_ID,
    "needs.approval-finalizer.outputs.authorization-artifact-digest": "sha256:"
    + ("e" * 64),
    "needs.approval-finalizer.outputs.authorization-artifact-id": "732",
    "needs.approval-finalizer.outputs.authorization-artifact-name": "authorization.json",
    "needs.approval-finalizer.outputs.authorization-digest": "sha256:"
    + ("f" * 64),
    "needs.approval-finalizer.outputs.capability-decision-artifact-digest": "sha256:"
    + ("2" * 64),
    "needs.approval-finalizer.outputs.capability-decision-artifact-id": "733",
    "needs.approval-finalizer.outputs.capability-decision-artifact-name": "capability-admission.json",
    "needs.approval-finalizer.outputs.capability-decision-digest": "sha256:"
    + ("3" * 64),
}
_PHASE2_RESULT_BUNDLE_OVERRIDES = {
    "needs.publish-github-packages.outputs.capability-group-bundle-artifact-digest": "sha256:"
    + ("4" * 64),
    "needs.publish-github-packages.outputs.capability-group-bundle-artifact-id": "735",
    "needs.publish-github-packages.outputs.capability-group-bundle-artifact-name": "capability-result-bundle.json",
    "needs.publish-github-packages.outputs.capability-group-bundle-digest": "sha256:"
    + ("5" * 64),
}


@pytest.mark.parametrize(
    ("overrides", "expected_flags", "diagnostic_tokens"),
    [
        (
            {
                "needs.observe-github-packages.result": "skipped",
                "needs.materialize-publication.result": "skipped",
                "needs.publish-github-packages.result": "cancelled",
                "needs.workflow-cancellation.outputs.workflow-cancelled || 'false'": "true",
            },
            ("--publication-preparation-interrupted",),
            (),
        ),
        (
            {
                "needs.observe-github-packages.result": "success",
                "needs.materialize-publication.result": "skipped",
                "needs.publish-github-packages.result": "cancelled",
                "needs.workflow-cancellation.outputs.workflow-cancelled || 'false'": "true",
            },
            ("--publication-preparation-interrupted",),
            (),
        ),
        (
            {
                "needs.observe-github-packages.result": "failure",
                "needs.materialize-publication.result": "skipped",
                "needs.publish-github-packages.result": "cancelled",
            },
            None,
            (
                "Publication preparation interruption did not skip the publisher",
            ),
        ),
        (
            {
                "needs.observe-github-packages.result": "skipped",
                "needs.materialize-publication.result": "skipped",
                "needs.publish-github-packages.result": "cancelled",
                "needs.approval-finalizer.outputs.publication-snapshot-artifact-id": "forwarded-snapshot-731",
                "needs.workflow-cancellation.outputs.workflow-cancelled || 'false'": "true",
            },
            None,
            ("downstream lineage",),
        ),
        (
            {
                "needs.observe-github-packages.result": "skipped",
                "needs.materialize-publication.result": "skipped",
                "needs.publish-github-packages.result": "cancelled",
                "needs.approval-finalizer.outputs.authorization-artifact-id": "authorization-732",
                "needs.workflow-cancellation.outputs.workflow-cancelled || 'false'": "true",
            },
            None,
            ("downstream lineage",),
        ),
        (
            {
                "needs.observe-github-packages.result": "skipped",
                "needs.materialize-publication.result": "skipped",
                "needs.publish-github-packages.result": "cancelled",
                "needs.approval-finalizer.outputs.capability-decision-artifact-id": "capability-admission-733",
                "needs.workflow-cancellation.outputs.workflow-cancelled || 'false'": "true",
            },
            None,
            ("downstream lineage",),
        ),
        (
            {
                "needs.observe-github-packages.result": "skipped",
                "needs.materialize-publication.result": "skipped",
                "needs.publish-github-packages.result": "cancelled",
                "needs.publish-github-packages.outputs.mutation-marker-artifact-id": "mutation-marker-734",
                "needs.workflow-cancellation.outputs.workflow-cancelled || 'false'": "true",
            },
            None,
            ("downstream lineage",),
        ),
        (
            {
                "needs.observe-github-packages.result": "skipped",
                "needs.materialize-publication.result": "skipped",
                "needs.publish-github-packages.result": "cancelled",
                "needs.publish-github-packages.outputs.capability-group-bundle-artifact-id": "result-bundle-735",
                "needs.workflow-cancellation.outputs.workflow-cancelled || 'false'": "true",
            },
            None,
            ("downstream lineage",),
        ),
        (
            {
                "needs.observe-github-packages.result": "skipped",
                "needs.materialize-publication.result": "skipped",
                "needs.publish-github-packages.result": "cancelled",
                "needs.publish-github-packages.outputs.receipt-artifact-id": "receipt-736",
                "needs.workflow-cancellation.outputs.workflow-cancelled || 'false'": "true",
            },
            None,
            ("downstream lineage",),
        ),
        (
            _PHASE2_POST_SNAPSHOT_OVERRIDES
            | {
                "needs.publish-github-packages.result": "cancelled",
            },
            ("--platform-terminated",),
            (),
        ),
        (
            _PHASE2_POST_SNAPSHOT_OVERRIDES,
            (),
            (),
        ),
        (
            _PHASE2_POST_SNAPSHOT_OVERRIDES
            | _PHASE2_RESULT_BUNDLE_OVERRIDES
            | {
                "needs.publish-github-packages.outputs.mutation-marker-artifact-id": "734",
                "needs.publish-github-packages.result": "success",
            },
            ("--capability-may-have-started",),
            (),
        ),
        (
            _PHASE2_POST_SNAPSHOT_OVERRIDES
            | _PHASE2_RESULT_BUNDLE_OVERRIDES
            | {"needs.publish-github-packages.result": "failure"},
            (),
            (),
        ),
        (
            _PHASE2_POST_SNAPSHOT_OVERRIDES
            | {"needs.publish-github-packages.result": "failure"},
            ("--platform-terminated",),
            (),
        ),
        (
            _PHASE2_POST_SNAPSHOT_OVERRIDES
            | {
                "needs.publish-github-packages.outputs.mutation-marker-artifact-id": "734",
                "needs.publish-github-packages.result": "failure",
            },
            (
                "--platform-terminated",
                "--capability-may-have-started",
            ),
            (),
        ),
        (
            _PHASE2_POST_SNAPSHOT_OVERRIDES
            | {
                "needs.publish-github-packages.outputs.mutation-marker-artifact-id": "734",
                "needs.publish-github-packages.result": "cancelled",
            },
            (
                "--platform-terminated",
                "--capability-may-have-started",
            ),
            (),
        ),
    ],
    ids=[
        "whole-run-cancelled-unstarted",
        "whole-run-cancelled-after-successful-observation",
        "cancelled-without-workflow-ownership",
        "cancelled-with-forwarded-snapshot",
        "cancelled-with-authorization",
        "cancelled-with-capability-admission",
        "cancelled-with-mutation-marker",
        "cancelled-with-result-bundle",
        "cancelled-with-receipt",
        "post-snapshot-cancelled",
        "post-snapshot-skipped",
        "post-snapshot-success-with-mutation-marker",
        "post-snapshot-failure-with-result-bundle",
        "post-snapshot-failure-without-result-bundle",
        "post-snapshot-failure-with-mutation-marker",
        "post-snapshot-cancelled-with-mutation-marker",
    ],
)
def test_publisher_result_truth_table_executes_workflow_shell(
    tmp_path: Path,
    overrides: dict[str, str],
    expected_flags: tuple[str, ...] | None,
    diagnostic_tokens: tuple[str, ...],
) -> None:
    execution = _phase2_execute_finalizer_shell(
        tmp_path,
        _phase2_finalizer_facts(**overrides),
    )

    if expected_flags is None:
        assert execution["status"] != 0
        assert execution["invocations"] == ()
        diagnostic = execution["output"].casefold()
        for token in diagnostic_tokens:
            assert token.casefold() in diagnostic
        return

    argv = _phase2_assert_successful_finalizer(execution)
    semantic_flags = {
        "--capability-may-have-started",
        "--platform-terminated",
        "--publication-preparation-interrupted",
    }
    for flag in semantic_flags:
        assert argv.count(flag) == (1 if flag in expected_flags else 0)
    if "--publication-preparation-interrupted" in expected_flags:
        assert {
            "--authorization",
            "--capability-decision",
            "--capability-group-bundle",
            "--capability-may-have-started",
            "--publication-snapshot",
            "--receipt",
        }.isdisjoint(argv)
        return

    expected_snapshot_arguments = {
        "--publication-snapshot": f".wdv3/input/{_PHASE2_SNAPSHOT_NAME}",
        "--publication-snapshot-digest": _PHASE2_SNAPSHOT_PAYLOAD_DIGEST,
        "--publication-snapshot-artifact-id": _PHASE2_SNAPSHOT_ARTIFACT_ID,
        "--publication-snapshot-artifact-digest": _PHASE2_SNAPSHOT_UPLOAD_DIGEST,
    }
    for flag, value in expected_snapshot_arguments.items():
        assert argv.count(flag) == 1
        assert argv[argv.index(flag) + 1] == value
    bundle_id = overrides.get(
        "needs.publish-github-packages.outputs."
        "capability-group-bundle-artifact-id",
        "",
    )
    assert ("--capability-group-bundle" in argv) is bool(bundle_id)


def _phase3_render_workflow_run(
    run: str,
    facts: dict[str, str],
) -> str:
    expressions = {
        match.group("fact").strip()
        for match in _PHASE2_FINALIZER_EXPRESSION.finditer(run)
    }
    assert expressions == set(facts)
    rendered = _PHASE2_FINALIZER_EXPRESSION.sub(
        lambda match: facts[match.group("fact").strip()],
        run,
    )
    assert "${{" not in rendered
    return rendered


def _phase3_execute_workflow_run(
    tmp_path: Path,
    run: str,
    facts: dict[str, str],
    *,
    environment: dict[str, str] | None = None,
) -> dict[str, Any]:
    import os  # noqa: PLC0415
    import subprocess  # noqa: PLC0415

    rendered = _phase3_render_workflow_run(run, facts)
    completed = subprocess.run(  # noqa: S603
        (
            "bash",
            "--noprofile",
            "--norc",
            "-euo",
            "pipefail",
            "-c",
            rendered,
        ),
        check=False,
        cwd=tmp_path,
        env=os.environ | (environment or {}),
        capture_output=True,
        text=True,
        timeout=10,
    )
    return {
        "output": completed.stdout + completed.stderr,
        "rendered": rendered,
        "status": completed.returncode,
    }


def _phase3_execute_incomplete_finalizer_shell(
    tmp_path: Path,
    facts: dict[str, str],
) -> dict[str, Any]:
    import os  # noqa: PLC0415
    import subprocess  # noqa: PLC0415

    run = _phase2_render_finalizer_run(facts)
    input_directory = tmp_path / ".wdv3/input"
    input_directory.mkdir(parents=True)
    for expression, value in facts.items():
        if expression.endswith("-artifact-name") and value:
            (input_directory / value).write_text("{}\n", encoding="utf-8")

    bin_directory = tmp_path / "bin"
    bin_directory.mkdir()
    invocations = tmp_path / "cli-invocations.jsonl"
    uv = bin_directory / "uv"
    uv.write_text(
        r"""#!/usr/bin/env python3
import json
import os
import pathlib
import sys

args = sys.argv[1:]
invocations = pathlib.Path(os.environ["PHASE3_CLI_INVOCATIONS"])
with invocations.open("a", encoding="utf-8") as handle:
    handle.write(json.dumps(args) + "\n")

def write_flag(flag, content):
    path = pathlib.Path(args[args.index(flag) + 1])
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")

write_flag(
    "--outcome-output",
    '{"phase":"publication-preparation","result":"incomplete"}\n',
)
attempt_summary = "# Attempt summary\n\n- CLI finalization status: incomplete\n"
write_flag("--summary-output", attempt_summary)
github_summary = pathlib.Path(args[args.index("--github-step-summary") + 1])
with github_summary.open("a", encoding="utf-8") as handle:
    handle.write(attempt_summary)
status = os.environ["PHASE3_CLI_STATUS"]
github_output = pathlib.Path(args[args.index("--github-output") + 1])
with github_output.open("a", encoding="utf-8") as handle:
    handle.write(f"artifact-name={os.environ['PHASE3_ARTIFACT_NAME']}\n")
    handle.write(f"status={status}\n")
    handle.write("cli-boundary-invoked=true\n")
raise SystemExit(int(status))
""",
        encoding="utf-8",
    )
    uv.chmod(0o755)

    github_output = tmp_path / "github-output.txt"
    github_summary = tmp_path / "github-step-summary.md"
    environment = os.environ | {
        "GITHUB_OUTPUT": str(github_output),
        "GITHUB_RUN_ATTEMPT": "3",
        "GITHUB_RUN_ID": "424242",
        "GITHUB_STEP_SUMMARY": str(github_summary),
        "PATH": f"{bin_directory}{os.pathsep}{os.environ['PATH']}",
        "PHASE3_ARTIFACT_NAME": "phase3-retained-attempt-outcome",
        "PHASE3_CLI_INVOCATIONS": str(invocations),
        "PHASE3_CLI_STATUS": "1",
        "WDV3_PACKAGE": "three-workflow-delivery-v3",
    }
    completed = subprocess.run(  # noqa: S603
        (
            "bash",
            "--noprofile",
            "--norc",
            "-euo",
            "pipefail",
            "-c",
            run,
        ),
        check=False,
        cwd=tmp_path,
        env=environment,
        capture_output=True,
        text=True,
        timeout=10,
    )
    output_values = {
        key: value
        for line in github_output.read_text(encoding="utf-8").splitlines()
        for key, value in (line.split("=", 1),)
    }
    return {
        "github_output": output_values,
        "github_summary": github_summary,
        "invocations": tuple(
            tuple(json.loads(line))
            for line in invocations.read_text(encoding="utf-8").splitlines()
        ),
        "outcome": tmp_path / ".wdv3/final-attempt/attempt-outcome.json",
        "output": completed.stdout + completed.stderr,
        "status": completed.returncode,
        "summary": tmp_path / ".wdv3/final-attempt/attempt-summary.md",
    }


def test_publication_snapshot_lifecycle_and_transport_identity_are_exact() -> (
    None
):
    materializer = _document(CALLEE)["jobs"]["materialize-publication"]
    lifecycle_ids = {
        "bind",
        "materialize",
        "names",
        "upload-reviewer",
        "upload-snapshot",
    }

    assert tuple(
        step["id"]
        for step in _steps(materializer)
        if step.get("id") in lifecycle_ids
    ) == (
        "materialize",
        "names",
        "upload-snapshot",
        "upload-reviewer",
        "bind",
    )
    assert materializer["outputs"]["publication-snapshot-artifact-id"] == (
        "${{ steps.upload-snapshot.outputs.artifact-id }}"
    )
    assert materializer["outputs"]["publication-snapshot-artifact-digest"] == (
        "${{ steps.upload-snapshot.outputs.artifact-digest }}"
    )
    assert materializer["outputs"]["publication-snapshot-digest"] == (
        "${{ steps.materialize.outputs.publication-snapshot-digest }}"
    )


def test_release_finalizer_downloads_snapshot_directly_from_materialization() -> (
    None
):
    finalizer = _document(CALLEE)["jobs"]["release-finalizer"]
    download = _step(
        finalizer,
        "Download Publication Snapshot by artifact ID",
    )
    run = _run(_step(finalizer, "Finalize Attempt Outcome"))

    assert "materialize-publication" in _needs(finalizer)
    assert download["if"] == (
        "always() && needs.materialize-publication.outputs."
        "publication-snapshot-artifact-id != ''"
    )
    assert download["with"]["artifact-ids"] == (
        "${{ needs.materialize-publication.outputs."
        "publication-snapshot-artifact-id }}"
    )
    expected_arguments = (
        (
            "--publication-snapshot",
            '".wdv3/input/${{ needs.materialize-publication.outputs.'
            'publication-snapshot-artifact-name }}"',
        ),
        (
            "--publication-snapshot-digest",
            '"${{ needs.materialize-publication.outputs.'
            'publication-snapshot-digest }}"',
        ),
        (
            "--publication-snapshot-artifact-id",
            '"${{ needs.materialize-publication.outputs.'
            'publication-snapshot-artifact-id }}"',
        ),
        (
            "--publication-snapshot-artifact-digest",
            '"${{ needs.materialize-publication.outputs.'
            'publication-snapshot-artifact-digest }}"',
        ),
    )
    for option, expression in expected_arguments:
        assert f"{option} {expression}" in run
        assert f'{option} "${{{{ needs.approval-finalizer.outputs.' not in run


def test_durable_snapshot_survives_later_reviewer_failure(
    tmp_path: Path,
) -> None:
    facts = _phase2_finalizer_facts(
        **{
            "needs.materialize-publication.outputs.publication-snapshot-artifact-digest": _PHASE2_SNAPSHOT_UPLOAD_DIGEST,
            "needs.materialize-publication.outputs.publication-snapshot-artifact-id": _PHASE2_SNAPSHOT_ARTIFACT_ID,
            "needs.materialize-publication.outputs.publication-snapshot-artifact-name": _PHASE2_SNAPSHOT_NAME,
            "needs.materialize-publication.outputs.publication-snapshot-digest": _PHASE2_SNAPSHOT_PAYLOAD_DIGEST,
            "needs.materialize-publication.result": "failure",
            "needs.observe-github-packages.result": "success",
        }
    )

    execution = _phase2_execute_finalizer_shell(tmp_path, facts)

    argv = _phase2_assert_successful_finalizer(execution)
    expected_snapshot_arguments = {
        "--publication-snapshot": f".wdv3/input/{_PHASE2_SNAPSHOT_NAME}",
        "--publication-snapshot-artifact-digest": _PHASE2_SNAPSHOT_UPLOAD_DIGEST,
        "--publication-snapshot-artifact-id": _PHASE2_SNAPSHOT_ARTIFACT_ID,
        "--publication-snapshot-digest": _PHASE2_SNAPSHOT_PAYLOAD_DIGEST,
    }
    for flag, value in expected_snapshot_arguments.items():
        assert argv.count(flag) == 1
        assert argv[argv.index(flag) + 1] == value
    assert "--publication-preparation-interrupted" not in argv
    assert "--platform-terminated" not in argv


def test_completed_materialization_summary_links_immutable_reviewer_artifact(
    tmp_path: Path,
) -> None:
    materializer = _document(CALLEE)["jobs"]["materialize-publication"]
    steps = _steps(materializer)
    upload = _step(materializer, "Upload reviewer artifact")
    summary = _step(
        materializer,
        "Publish completed reviewer summary and artifact link",
    )
    bind = _step(
        materializer,
        "Bind reviewer artifact transport to exact payloads",
    )

    assert steps.index(upload) < steps.index(bind) < steps.index(summary)
    assert summary["if"] == "steps.upload-reviewer.outcome == 'success'"
    run = _run(summary)
    assert "${{ steps.upload-reviewer.outputs.artifact-url }}" in run
    assert "${GITHUB_STEP_SUMMARY}" in run

    reviewer_name = "phase3-reviewer-payload"
    reviewer_directory = tmp_path / ".wdv3" / reviewer_name
    reviewer_directory.mkdir(parents=True)
    reviewer = reviewer_directory / "reviewer-summary.md"
    reviewer_bytes = (
        b"# Immutable reviewer summary\n\n"
        b"- Target: 1111111111111111111111111111111111111111\n"
        b"- Coordinate: @hcoona/release-smoke@0.1.0\n"
    )
    reviewer.write_bytes(reviewer_bytes)
    artifact_url = (
        "https://github.com/hcoona/three/actions/runs/424242/artifacts/987654"
    )
    github_summary = tmp_path / "github-step-summary.md"
    prior_summary = (
        b"# Prior job summary\n\n- Qualification diagnostics retained\n"
    )
    github_summary.write_bytes(prior_summary)

    execution = _phase3_execute_workflow_run(
        tmp_path,
        run,
        {
            "steps.names.outputs.reviewer-name": reviewer_name,
            "steps.upload-reviewer.outputs.artifact-url": artifact_url,
        },
        environment={"GITHUB_STEP_SUMMARY": str(github_summary)},
    )

    assert execution["status"] == 0, execution["output"]
    assert reviewer.read_bytes() == reviewer_bytes
    job_summary = github_summary.read_bytes()
    assert job_summary.startswith(prior_summary + reviewer_bytes)
    assert artifact_url.encode() in job_summary
    assert artifact_url.encode() not in reviewer.read_bytes()
    reviewer_summary_writers = [
        (job_name, step.get("name"))
        for job_name, job in _document(CALLEE)["jobs"].items()
        for step in _steps(job)
        for shell in (step.get("run"),)
        if isinstance(shell, str)
        if "reviewer-summary.md" in shell and "GITHUB_STEP_SUMMARY" in shell
    ]
    assert reviewer_summary_writers == [
        (
            "materialize-publication",
            "Publish completed reviewer summary and artifact link",
        )
    ]
    redirection_to_reviewer = re.compile(
        r"(?:>>?|tee(?:\s+-a)?)\s+[\"']?[^\n\"']*reviewer-summary\.md"
    )
    for step in steps:
        shell = step.get("run")
        if isinstance(shell, str):
            assert redirection_to_reviewer.search(shell) is None


def test_incomplete_preparation_retains_diagnostics_before_job_failure(
    tmp_path: Path,
) -> None:
    import hashlib  # noqa: PLC0415

    finalizer = _document(CALLEE)["jobs"]["release-finalizer"]
    finalize = _step(finalizer, "Finalize Attempt Outcome")
    upload = _step(finalizer, "Upload final Attempt Outcome and summary")
    retained_records = (
        ("build-evidence", "build-evidence.json", "102", "0", "5"),
        (
            "project-test-evidence",
            "project-test-evidence.json",
            "103",
            "1",
            "6",
        ),
        (
            "artifact-contents-evidence",
            "artifact-contents-evidence.json",
            "104",
            "2",
            "7",
        ),
        (
            "install-import-evidence",
            "install-import-evidence.json",
            "105",
            "3",
            "8",
        ),
        ("release-artifact", "release-artifact.json", "106", "4", "9"),
    )
    sentinels = {
        sentinel
        for *_, record_sentinel, upload_sentinel in retained_records
        for sentinel in (record_sentinel, upload_sentinel)
    }
    assert len(sentinels) == 2 * len(retained_records)
    facts = _phase2_finalizer_facts(
        **{
            key: value
            for role, _, _, record_sentinel, upload_sentinel in retained_records
            for key, value in (
                (
                    f"needs.qualification-finalizer.outputs.{role}-digest",
                    f"sha256:{record_sentinel * 64}",
                ),
                (
                    "needs.qualification-finalizer.outputs."
                    f"{role}-artifact-digest",
                    f"sha256:{upload_sentinel * 64}",
                ),
            )
        }
    )
    execution = _phase3_execute_incomplete_finalizer_shell(
        tmp_path,
        facts,
    )

    assert finalize["continue-on-error"] is True
    assert upload["with"]["path"] == ".wdv3/final-attempt"
    assert execution["status"] == 1
    assert len(execution["invocations"]) == 1
    argv = execution["invocations"][0]
    expected_record_argv = tuple(
        argument
        for role, filename, artifact_id, record_sentinel, upload_sentinel in (
            retained_records
        )
        for argument in (
            f"--{role}",
            f".wdv3/input/{filename}",
            f"--{role}-digest",
            f"sha256:{record_sentinel * 64}",
            f"--{role}-artifact-id",
            artifact_id,
            f"--{role}-artifact-digest",
            f"sha256:{upload_sentinel * 64}",
        )
    )
    assert (
        argv[
            argv.index("--build-evidence") : argv.index(
                "--publication-preparation-interrupted"
            )
        ]
        == expected_record_argv
    )
    assert argv.count("--publication-preparation-interrupted") == 1
    assert "--platform-terminated" not in argv
    assert execution["outcome"].is_file()
    assert execution["summary"].is_file()
    retained_summary = execution["summary"].read_text(encoding="utf-8")
    job_summary = execution["github_summary"].read_text(encoding="utf-8")
    for diagnostic in (
        "## Publication preparation interruption",
        "- Qualification result: success",
        "- Observation job result: failure",
        "- Materialization job result: skipped",
        "- Publisher job result: skipped",
        "- Durable Publication Snapshot: absent",
        "- Capability path started: no",
        "- Workflow cancellation observed: false",
    ):
        assert diagnostic in retained_summary
        assert diagnostic in job_summary
    assert retained_summary == job_summary
    outputs = execution["github_output"]
    assert outputs["artifact-name"] == "phase3-retained-attempt-outcome"
    assert outputs["status"] == "1"
    assert outputs["cli-boundary-invoked"] == "true"
    assert outputs["finalizer-status"] == "1"
    outcome_digest = hashlib.sha256(
        execution["outcome"].read_bytes()
    ).hexdigest()
    assert outputs["final-artifact-name"] == (
        "wdv3-live-buddy-attempt-outcome-r424242-ra3-" + outcome_digest
    )


@pytest.mark.parametrize(
    (
        "finalizer_status",
        "finalize_outcome",
        "upload_outcome",
        "expected_status",
    ),
    [
        pytest.param("0", "success", "success", 0, id="all-success"),
        pytest.param(
            "17",
            "success",
            "success",
            1,
            id="finalizer-status-nonzero",
        ),
        pytest.param(
            "0",
            "failure",
            "success",
            1,
            id="finalize-step-failure",
        ),
        pytest.param(
            "0",
            "success",
            "failure",
            1,
            id="upload-step-failure",
        ),
    ],
)
def test_propagation_fails_after_successful_retention(
    tmp_path: Path,
    finalizer_status: str,
    finalize_outcome: str,
    upload_outcome: str,
    expected_status: int,
) -> None:
    finalizer = _document(CALLEE)["jobs"]["release-finalizer"]
    steps = _steps(finalizer)
    finalize = _step(finalizer, "Finalize Attempt Outcome")
    upload = _step(finalizer, "Upload final Attempt Outcome and summary")
    propagate = _step(finalizer, "Propagate finalization status")

    assert steps.index(finalize) < steps.index(upload) < steps.index(propagate)
    assert finalize["continue-on-error"] is True
    assert upload["if"] == "always()"
    assert upload["with"]["path"] == ".wdv3/final-attempt"
    assert upload["with"]["retention-days"] == RETENTION_DAYS
    assert upload["with"]["if-no-files-found"] == "error"
    assert propagate["if"] == "always()"

    execution = _phase3_execute_workflow_run(
        tmp_path,
        _run(propagate),
        {
            "steps.finalize.outcome": finalize_outcome,
            "steps.finalize.outputs.finalizer-status": finalizer_status,
            "steps.upload-final.outcome": upload_outcome,
        },
    )

    assert execution["status"] == expected_status, execution["output"]
    assert execution["output"] == ""


@pytest.mark.parametrize(
    "expected_skipped_value",
    [
        pytest.param(
            "false",
            id="non-cancelled-witness-skipped-defaults-false",
        ),
    ],
)
def test_workflow_cancellation_witness_has_exact_job_contract(
    tmp_path: Path,
    expected_skipped_value: str,
) -> None:
    jobs = _document(CALLEE)["jobs"]

    assert set(jobs) == EXPECTED_JOBS
    witness = jobs["workflow-cancellation"]
    assert witness["if"] == "cancelled()"
    assert _needs(witness) == _WORKFLOW_CANCELLATION_AUTHORITIES
    assert witness["permissions"] == {}
    assert [step.get("name") for step in _steps(witness)] == [
        "Record workflow cancellation"
    ]
    recorder = _step(witness, "Record workflow cancellation")
    recorder_id = recorder.get("id")
    assert isinstance(recorder_id, str)
    assert recorder_id
    assert witness["outputs"] == {
        "workflow-cancelled": (
            f"${{{{ steps.{recorder_id}.outputs.workflow-cancelled }}}}"
        )
    }
    run = _run(recorder)
    assert run == 'echo "workflow-cancelled=true" >> "${GITHUB_OUTPUT}"'

    finalizer = jobs["release-finalizer"]
    assert _needs(finalizer) == (
        *_WORKFLOW_CANCELLATION_AUTHORITIES,
        "workflow-cancellation",
    )
    assert all(
        step.get("name") != "Record workflow cancellation"
        for step in _steps(finalizer)
    )
    finalizer_run = _run(_step(finalizer, "Finalize Attempt Outcome"))
    assert finalizer_run.count(_WORKFLOW_CANCELLATION_FACT) == 1
    assert _LEGACY_WORKFLOW_CANCELLATION_FACT not in finalizer_run

    github_output = tmp_path / "cancellation-output.txt"
    recorder_execution = _phase3_execute_workflow_run(
        tmp_path,
        run,
        {},
        environment={"GITHUB_OUTPUT": str(github_output)},
    )
    assert recorder_execution["status"] == 0, recorder_execution["output"]
    assert recorder_execution["rendered"] == run
    assert github_output.read_bytes() == b"workflow-cancelled=true\n"

    facts = _phase2_finalizer_facts(**_PHASE2_POST_SNAPSHOT_OVERRIDES)
    assert facts[_WORKFLOW_CANCELLATION_FACT] == expected_skipped_value
    assert _LEGACY_WORKFLOW_CANCELLATION_FACT not in facts
    finalizer_execution = _phase2_execute_finalizer_shell(tmp_path, facts)
    argv = _phase2_assert_successful_finalizer(finalizer_execution)
    assert argv.count("--publication-snapshot") == 1
    assert {
        "--capability-may-have-started",
        "--platform-terminated",
        "--publication-preparation-interrupted",
    }.isdisjoint(argv)


def test_durable_snapshot_reviewer_failure_omits_preparation_diagnostics(
    tmp_path: Path,
) -> None:
    prior_summary = "# Prior qualification summary\n"
    github_summary = tmp_path / "github-step-summary.md"
    github_summary.write_text(prior_summary, encoding="utf-8")
    facts = _phase2_finalizer_facts(
        **{
            "needs.materialize-publication.outputs.publication-snapshot-artifact-digest": _PHASE2_SNAPSHOT_UPLOAD_DIGEST,
            "needs.materialize-publication.outputs.publication-snapshot-artifact-id": _PHASE2_SNAPSHOT_ARTIFACT_ID,
            "needs.materialize-publication.outputs.publication-snapshot-artifact-name": _PHASE2_SNAPSHOT_NAME,
            "needs.materialize-publication.outputs.publication-snapshot-digest": _PHASE2_SNAPSHOT_PAYLOAD_DIGEST,
            "needs.materialize-publication.result": "failure",
            "needs.observe-github-packages.result": "success",
        }
    )

    execution = _phase2_execute_finalizer_shell(tmp_path, facts)

    argv = _phase2_assert_successful_finalizer(execution)
    assert "--publication-preparation-interrupted" not in argv
    assert "--platform-terminated" not in argv
    job_summary = github_summary.read_text(encoding="utf-8")
    assert job_summary == prior_summary
    assert "Publication preparation interruption" not in job_summary


@pytest.mark.parametrize(
    ("step_name", "expected_action", "expected_condition"),
    [
        pytest.param(
            "Check out exact selected target",
            CHECKOUT,
            "always()",
            id="checkout-target",
        ),
        pytest.param(
            "Install uv",
            UV,
            "always()",
            id="install-uv",
        ),
        pytest.param(
            "Download Release Attempt binding by artifact ID",
            DOWNLOAD,
            "always() && needs.admit.outputs.attempt-artifact-id != ''",
            id="attempt-binding",
        ),
        pytest.param(
            "Download Qualification Snapshot by artifact ID",
            DOWNLOAD,
            (
                "always() && needs.qualification-finalizer.outputs."
                "qualification-snapshot-artifact-id != ''"
            ),
            id="qualification-snapshot",
        ),
        pytest.param(
            "Download Qualification Decision by artifact ID",
            DOWNLOAD,
            (
                "always() && needs.qualification-finalizer.outputs."
                "decision-artifact-id != ''"
            ),
            id="qualification-decision",
        ),
        pytest.param(
            "Download build Evidence by artifact ID",
            DOWNLOAD,
            (
                "always() && needs.qualification-finalizer.outputs."
                "build-evidence-artifact-id != ''"
            ),
            id="build",
        ),
        pytest.param(
            "Download project-test Evidence by artifact ID",
            DOWNLOAD,
            (
                "always() && needs.qualification-finalizer.outputs."
                "project-test-evidence-artifact-id != ''"
            ),
            id="project-test",
        ),
        pytest.param(
            "Download artifact-contents Evidence by artifact ID",
            DOWNLOAD,
            (
                "always() && needs.qualification-finalizer.outputs."
                "artifact-contents-evidence-artifact-id != ''"
            ),
            id="artifact-contents",
        ),
        pytest.param(
            "Download install-import Evidence by artifact ID",
            DOWNLOAD,
            (
                "always() && needs.qualification-finalizer.outputs."
                "install-import-evidence-artifact-id != ''"
            ),
            id="install-import",
        ),
        pytest.param(
            "Download Release Artifact record by artifact ID",
            DOWNLOAD,
            (
                "always() && needs.qualification-finalizer.outputs."
                "release-artifact-artifact-id != ''"
            ),
            id="release-artifact",
        ),
        pytest.param(
            "Download Publication Snapshot by artifact ID",
            DOWNLOAD,
            (
                "always() && needs.materialize-publication.outputs."
                "publication-snapshot-artifact-id != ''"
            ),
            id="publication-snapshot",
        ),
        pytest.param(
            "Download Authorization Record by artifact ID",
            DOWNLOAD,
            (
                "always() && needs.approval-finalizer.outputs."
                "authorization-artifact-id != ''"
            ),
            id="authorization",
        ),
        pytest.param(
            "Download Capability Admission Decision by artifact ID",
            DOWNLOAD,
            (
                "always() && needs.approval-finalizer.outputs."
                "capability-decision-artifact-id != ''"
            ),
            id="capability-admission-decision",
        ),
        pytest.param(
            "Download capability result bundle by artifact ID",
            DOWNLOAD,
            (
                "always() && needs.publish-github-packages.outputs."
                "capability-group-bundle-artifact-id != ''"
            ),
            id="capability-result-bundle",
        ),
        pytest.param(
            "Download Receipt by artifact ID",
            DOWNLOAD,
            (
                "always() && needs.publish-github-packages.outputs."
                "receipt-artifact-id != ''"
            ),
            id="receipt",
        ),
    ],
)
def test_release_finalizer_prerequisite_actions_are_cancellation_admitting(
    step_name: str,
    expected_action: str,
    expected_condition: str,
) -> None:
    finalizer = _document(CALLEE)["jobs"]["release-finalizer"]
    prerequisite = _step(finalizer, step_name)

    assert prerequisite["uses"] == expected_action
    assert prerequisite["if"] == expected_condition


_PHASE2_ABSENT_QUALIFICATION_RECORDS = {
    f"needs.qualification-finalizer.outputs.{record}-{field}": ""
    for record in (
        "build-evidence",
        "project-test-evidence",
        "artifact-contents-evidence",
        "install-import-evidence",
        "release-artifact",
    )
    for field in (
        "artifact-digest",
        "artifact-id",
        "artifact-name",
        "digest",
    )
}


def _phase2_cancelled_unsuccessful_qualification_facts(
    qualification_result: str,
    *,
    workflow_cancelled: str = "true",
    lineage: dict[str, str] | None = None,
) -> dict[str, str]:
    return _phase2_finalizer_facts(
        **(
            _PHASE2_ABSENT_QUALIFICATION_RECORDS
            | {
                "needs.materialize-publication.result": "skipped",
                "needs.observe-github-packages.result": "skipped",
                "needs.publish-github-packages.result": "cancelled",
                "needs.qualification-finalizer.outputs.qualification-result": qualification_result,
                "needs.workflow-cancellation.outputs.workflow-cancelled || 'false'": workflow_cancelled,
            }
            | (lineage or {})
        )
    )


@pytest.mark.parametrize(
    "qualification_result",
    [
        pytest.param("failure", id="failure"),
        pytest.param("incomplete", id="incomplete"),
    ],
)
def test_cancelled_unsuccessful_qualification_uses_exact_qualification_only_argv(
    tmp_path: Path,
    qualification_result: str,
) -> None:
    facts = _phase2_cancelled_unsuccessful_qualification_facts(
        qualification_result
    )

    execution = _phase2_execute_finalizer_shell(tmp_path, facts)

    argv = _phase2_assert_successful_finalizer(execution)
    expected_argv = (
        "run",
        "--python",
        "3.13",
        "--package",
        "three-workflow-delivery-v3",
        "three-workflow-delivery-v3",
        "release",
        "finalize-live",
        "--workflow-run-id",
        "424242",
        "--run-attempt",
        "3",
        "--target",
        "1" * 40,
        "--attempt-binding",
        ".wdv3/input/attempt-binding.json",
        "--attempt-binding-digest",
        "sha256:" + ("a" * 64),
        "--attempt-binding-artifact-id",
        "101",
        "--attempt-binding-artifact-digest",
        "sha256:" + ("d" * 64),
        "--qualification-snapshot",
        ".wdv3/input/qualification-snapshot.json",
        "--qualification-snapshot-digest",
        "sha256:" + ("a" * 64),
        "--qualification-snapshot-artifact-id",
        "107",
        "--qualification-snapshot-artifact-digest",
        "sha256:" + ("d" * 64),
        "--qualification-decision",
        ".wdv3/input/qualification-decision.json",
        "--qualification-decision-digest",
        "sha256:" + ("a" * 64),
        "--qualification-decision-artifact-id",
        "108",
        "--qualification-decision-artifact-digest",
        "sha256:" + ("d" * 64),
        "--outcome-output",
        ".wdv3/final-attempt/attempt-outcome.json",
        "--summary-output",
        ".wdv3/final-attempt/attempt-summary.md",
        "--github-step-summary",
        str(tmp_path / "github-step-summary.md"),
        "--github-output",
        str(tmp_path / "github-output.txt"),
    )
    assert argv == expected_argv
    assert "--publication-preparation-interrupted" not in argv
    assert "--platform-terminated" not in argv


@pytest.mark.parametrize(
    "qualification_result",
    [
        pytest.param("failure", id="failure"),
        pytest.param("incomplete", id="incomplete"),
    ],
)
def test_cancelled_unsuccessful_qualification_retains_qualification_record_argv(
    tmp_path: Path,
    qualification_result: str,
) -> None:
    facts = _phase2_finalizer_facts(
        **{
            "needs.materialize-publication.result": "skipped",
            "needs.observe-github-packages.result": "skipped",
            "needs.publish-github-packages.result": "cancelled",
            "needs.qualification-finalizer.outputs.qualification-result": qualification_result,
            "needs.workflow-cancellation.outputs.workflow-cancelled || 'false'": "true",
        }
    )

    execution = _phase2_execute_finalizer_shell(tmp_path, facts)

    argv = _phase2_assert_successful_finalizer(execution)
    retained_records = (
        ("build-evidence", "build-evidence.json", "102"),
        ("project-test-evidence", "project-test-evidence.json", "103"),
        (
            "artifact-contents-evidence",
            "artifact-contents-evidence.json",
            "104",
        ),
        ("install-import-evidence", "install-import-evidence.json", "105"),
        ("release-artifact", "release-artifact.json", "106"),
    )
    expected_record_argv = tuple(
        argument
        for role, filename, artifact_id in retained_records
        for argument in (
            f"--{role}",
            f".wdv3/input/{filename}",
            f"--{role}-digest",
            "sha256:" + ("a" * 64),
            f"--{role}-artifact-id",
            artifact_id,
            f"--{role}-artifact-digest",
            "sha256:" + ("d" * 64),
        )
    )
    record_start = argv.index("--build-evidence")
    record_end = argv.index("--outcome-output")
    assert argv[record_start:record_end] == expected_record_argv
    assert "--platform-terminated" not in argv
    assert "--publication-preparation-interrupted" not in argv


@pytest.mark.parametrize(
    (
        "workflow_cancelled",
        "lineage",
        "expected_record_flag",
        "expected_capability_flag",
    ),
    [
        pytest.param(
            "false",
            {},
            None,
            None,
            id="without-workflow-ownership",
        ),
        pytest.param(
            "true",
            {
                "needs.observe-github-packages.result": "failure",
            },
            None,
            None,
            id="with-observation-work",
        ),
        pytest.param(
            "true",
            {
                "needs.observe-github-packages.result": "success",
            },
            None,
            None,
            id="with-observation-success",
        ),
        pytest.param(
            "true",
            {
                "needs.observe-github-packages.result": "cancelled",
            },
            None,
            None,
            id="with-observation-cancelled",
        ),
        pytest.param(
            "true",
            {
                "needs.materialize-publication.result": "failure",
            },
            None,
            None,
            id="with-materialization-work",
        ),
        pytest.param(
            "true",
            {
                "needs.materialize-publication.result": "success",
            },
            None,
            None,
            id="with-materialization-success",
        ),
        pytest.param(
            "true",
            {
                "needs.materialize-publication.result": "cancelled",
            },
            None,
            None,
            id="with-materialization-cancelled",
        ),
        pytest.param(
            "true",
            {
                "needs.materialize-publication.outputs.publication-snapshot-artifact-digest": _PHASE2_SNAPSHOT_UPLOAD_DIGEST,
                "needs.materialize-publication.outputs.publication-snapshot-artifact-id": _PHASE2_SNAPSHOT_ARTIFACT_ID,
                "needs.materialize-publication.outputs.publication-snapshot-artifact-name": _PHASE2_SNAPSHOT_NAME,
                "needs.materialize-publication.outputs.publication-snapshot-digest": _PHASE2_SNAPSHOT_PAYLOAD_DIGEST,
            },
            "--publication-snapshot",
            None,
            id="with-publication-snapshot",
        ),
        pytest.param(
            "true",
            {
                "needs.materialize-publication.outputs.publication-snapshot-artifact-digest": _PHASE2_SNAPSHOT_UPLOAD_DIGEST,
            },
            None,
            None,
            id="with-orphaned-snapshot-upload-digest",
        ),
        pytest.param(
            "true",
            {
                "needs.approval-finalizer.outputs.publication-snapshot-artifact-id": _PHASE2_SNAPSHOT_ARTIFACT_ID,
            },
            None,
            None,
            id="with-forwarded-snapshot",
        ),
        pytest.param(
            "true",
            {
                "needs.approval-finalizer.outputs.authorization-artifact-digest": "sha256:"
                + ("e" * 64),
                "needs.approval-finalizer.outputs.authorization-artifact-id": "732",
                "needs.approval-finalizer.outputs.authorization-artifact-name": "authorization.json",
                "needs.approval-finalizer.outputs.authorization-digest": "sha256:"
                + ("f" * 64),
            },
            "--authorization",
            None,
            id="with-authorization",
        ),
        pytest.param(
            "true",
            {
                "needs.approval-finalizer.outputs.capability-decision-artifact-digest": "sha256:"
                + ("2" * 64),
                "needs.approval-finalizer.outputs.capability-decision-artifact-id": "733",
                "needs.approval-finalizer.outputs.capability-decision-artifact-name": "capability-admission.json",
                "needs.approval-finalizer.outputs.capability-decision-digest": "sha256:"
                + ("3" * 64),
            },
            "--capability-decision",
            None,
            id="with-capability-admission",
        ),
        pytest.param(
            "true",
            {
                "needs.publish-github-packages.outputs.mutation-marker-artifact-id": "734",
            },
            None,
            "--capability-may-have-started",
            id="with-mutation-marker",
        ),
        pytest.param(
            "true",
            _PHASE2_RESULT_BUNDLE_OVERRIDES,
            "--capability-group-bundle",
            None,
            id="with-result-bundle",
        ),
        pytest.param(
            "true",
            {
                "needs.publish-github-packages.outputs.receipt-artifact-digest": "sha256:"
                + ("6" * 64),
                "needs.publish-github-packages.outputs.receipt-artifact-id": "736",
                "needs.publish-github-packages.outputs.receipt-artifact-name": "receipt.json",
                "needs.publish-github-packages.outputs.receipt-digest": "sha256:"
                + ("7" * 64),
            },
            "--receipt",
            None,
            id="with-receipt",
        ),
    ],
)
@pytest.mark.parametrize(
    "qualification_result",
    [
        pytest.param("failure", id="failure"),
        pytest.param("incomplete", id="incomplete"),
    ],
)
def test_unsuccessful_qualification_cancellation_is_not_clean_with_contradictions(  # noqa: PLR0913
    tmp_path: Path,
    qualification_result: str,
    workflow_cancelled: str,
    lineage: dict[str, str],
    expected_record_flag: str | None,
    expected_capability_flag: str | None,
) -> None:
    facts = _phase2_cancelled_unsuccessful_qualification_facts(
        qualification_result,
        workflow_cancelled=workflow_cancelled,
        lineage=lineage,
    )

    execution = _phase2_execute_finalizer_shell(tmp_path, facts)

    argv = _phase2_assert_successful_finalizer(execution)
    assert argv.count("--platform-terminated") == 1
    assert "--publication-preparation-interrupted" not in argv
    assert argv.count("--capability-may-have-started") == (
        1 if expected_capability_flag is not None else 0
    )
    if expected_record_flag is not None:
        assert argv.count(expected_record_flag) == 1
