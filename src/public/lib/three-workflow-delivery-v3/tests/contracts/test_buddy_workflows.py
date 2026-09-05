"""Test-first topology and privilege contracts for commit-8 Buddy workflows."""

from __future__ import annotations

# ruff: noqa: D103, E501, ISC004, PLR0917, S607
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
APPROVAL_ENVIRONMENT_NAME = "workflow-delivery-v3-buddy-approval"
APPROVAL_ENVIRONMENT_MARKER = f"{APPROVAL_ENVIRONMENT_NAME}/v1"
ATTEMPT_ONE_CONDITION = "github.run_attempt == 1"
EXPECTED_VALID_IDENTITY_CONDITION = (
    "always() && github.run_attempt == 1 && "
    "needs.admit.outputs.identity-admitted == 'true'"
)
EXPECTED_CALLER_JOB_CONDITIONS = {
    "request": ATTEMPT_ONE_CONDITION,
    "discover-node": ATTEMPT_ONE_CONDITION,
    "compile-model": ATTEMPT_ONE_CONDITION,
    "evaluate-live-eligibility": ATTEMPT_ONE_CONDITION,
    "run-live-attempt": (
        "github.run_attempt == 1 && "
        "needs.evaluate-live-eligibility.outputs.live-result == 'admitted'"
    ),
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


def _transitive_needs(
    jobs: dict[str, Any],
    job_name: str,
) -> set[str]:
    dependencies: set[str] = set()
    pending = list(_needs(jobs[job_name]))
    while pending:
        dependency = pending.pop()
        if dependency in dependencies:
            continue
        dependencies.add(dependency)
        pending.extend(_needs(jobs[dependency]))
    return dependencies


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


def _condition_conjuncts(job: dict[str, Any]) -> set[str]:
    condition = job.get("if")
    assert isinstance(condition, str)
    assert "||" not in condition
    return {term.strip() for term in condition.split("&&")}


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


def test_buddy_workflow_files_are_the_disabled_commit8_pair_only() -> None:
    assert CALLER.is_file()
    assert CALLEE.is_file()
    assert (
        REPO_ROOT
        / ".github/workflows/workflow-delivery-v3-buddy-smoke-acceptance.yml"
    ).exists() is False
    assert (
        REPO_ROOT / ".github/workflows/"
        "workflow-delivery-v3-buddy-smoke-acceptance-retry-2.yml"
    ).exists() is False
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
    assert invoke["permissions"] == {
        "contents": "read",
        "actions": "read",
        "packages": "read",
    }
    assert invoke["concurrency"]["cancel-in-progress"] is False
    assert invoke["concurrency"]["group"].startswith("wdv3-execution-")

    compile_model = jobs["compile-model"]
    compile_step = _step(
        compile_model,
        "Compile without rerunning Provider",
    )
    compile_shell = _run(compile_step)
    evaluate = jobs["evaluate-live-eligibility"]

    assert compile_step["id"] == "compile"
    assert "release compile-live-model \\" in compile_shell
    assert '--github-output "${GITHUB_OUTPUT}"' in compile_shell
    assert compile_model["outputs"]["execution-concurrency-key"] == (
        "${{ steps.compile.outputs.execution-concurrency-key }}"
    )
    assert evaluate["outputs"]["execution-concurrency-key"] == (
        "${{ needs.compile-model.outputs.execution-concurrency-key }}"
    )
    assert invoke["concurrency"] == {
        "group": (
            "wdv3-execution-${{ "
            "needs.evaluate-live-eligibility.outputs.execution-concurrency-key }}"
        ),
        "cancel-in-progress": False,
    }
    assert invoke["if"] == EXPECTED_CALLER_JOB_CONDITIONS["run-live-attempt"]
    assert "runs-on" not in invoke
    for job_name in (
        "request",
        "discover-node",
        "compile-model",
        "evaluate-live-eligibility",
    ):
        assert "concurrency" not in jobs[job_name]

    assert "${{ needs.discover-node.outputs.request-id }}" not in compile_shell
    assert "printf " not in compile_shell
    assert [
        line.strip()
        for line in compile_shell.splitlines()
        if "sha256sum" in line
    ] == ["digest=\"$(sha256sum .wdv3/repository-model.json | cut -d' ' -f1)\""]
    assert [
        line.strip()
        for line in compile_shell.splitlines()
        if "execution-concurrency-key" in line or "execution_key" in line
    ] == []


def test_live_eligibility_block_is_uploaded_before_status_propagates() -> None:
    """Retain a valid blocked Decision before surfacing domain exit one."""
    caller = _document(CALLER)
    job = caller["jobs"]["evaluate-live-eligibility"]
    steps = job["steps"]
    evaluate = _step(job, "Evaluate fixed-source live eligibility")
    upload = _step(job, "Upload Live Eligibility Decision")
    propagate = _step(job, "Propagate Live Eligibility status")
    command = _run(evaluate)
    propagation = _run(propagate)
    domain_command_count = 1

    assert command.count("set +e") == domain_command_count
    assert command.count("set -e") == domain_command_count
    assert "eligibility_status=$?" in command
    assert '"${eligibility_status}" != "0"' in command
    assert '"${eligibility_status}" != "1"' in command
    assert '"${decision_result}" != "pass"' in command
    assert '"${decision_result}" != "blocked"' in command
    assert 'echo "eligibility-status=${eligibility_status}"' in command
    assert "--consumer-policy" not in command
    assert "consumer_status" not in command
    assert "consumer_result" not in command
    assert command.index("eligibility_status=$?") < command.index(
        'echo "live-eligibility-artifact-name=${artifact_name}"'
    )
    assert steps.index(evaluate) < steps.index(upload) < steps.index(propagate)
    assert propagate["if"] == "always()"
    assert "steps.eligibility.outcome" in propagation
    assert "steps.upload.outcome" in propagation
    assert "steps.eligibility.outputs.eligibility-status" in propagation
    assert "1) exit 1" in propagation
    assert job["outputs"]["live-result"] == (
        "${{ steps.eligibility.outputs.live-result }}"
    )


def test_live_eligibility_installs_only_the_static_authority_toolchain() -> (
    None
):
    """Do not couple eligibility to unrelated repository tools."""
    job = _document(CALLER)["jobs"]["evaluate-live-eligibility"]
    toolchain = _step(job, "Install exact toolchain")
    preparation = _step(job, "Prepare static-reference authorities")

    assert toolchain["uses"] == MISE
    assert toolchain["with"] == {
        "experimental": True,
        "install": True,
        "install_args": "core:dotnet node pnpm",
    }
    assert preparation["env"] == {
        "MISE_TASK_RUN_AUTO_INSTALL": "false",
    }
    assert _run(preparation) == (
        "mise run prepare:static-reference-authorities"
    )


def test_destination_observer_maps_the_effective_github_token() -> None:
    caller = _document(CALLER)["jobs"]["run-live-attempt"]
    observer_job = _document(CALLEE)["jobs"]["observe-github-packages"]
    observer = _step(
        observer_job,
        "Observe exact GitHub Packages state",
    )

    assert caller["permissions"]["packages"] == "read"
    assert observer_job["permissions"] == {
        "contents": "read",
        "packages": "read",
    }
    assert observer["env"] == {"GITHUB_TOKEN": "${{ github.token }}"}
    assert '--github-token "${GITHUB_TOKEN}"' in _run(observer)


def test_blocking_observation_is_retained_before_status_propagation() -> None:
    jobs = _document(CALLEE)["jobs"]
    observer = jobs["observe-github-packages"]
    observe = _step(observer, "Observe exact GitHub Packages state")
    upload = _step(observer, "Upload Observation Record set")
    propagate = _step(observer, "Propagate observation status")
    step_names = tuple(step["name"] for step in _steps(observer))

    assert observe["continue-on-error"] is True
    observe_run = _run(observe)
    assert observe_run.index("set +e") < observe_run.index(
        "three-workflow-delivery-v3 release observe-github-packages"
    )
    assert observe_run.index("observation_status=$?") < observe_run.index(
        "sha256sum .wdv3/observation-set.json"
    )
    assert observe_run.index("mv .wdv3/observation-set.json") < (
        observe_run.index("observation-set-artifact-name=${artifact_name}")
    )
    assert observe_run.rstrip().endswith('exit "${observation_status}"')
    assert upload["if"] == (
        "always() && steps.observe.outputs.observation-set-artifact-name != ''"
    )
    assert propagate["if"] == "always()"
    propagate_run = _run(propagate)
    for fact in (
        "steps.observe.outcome",
        "steps.upload.outcome",
        "steps.observe.outputs.observation-status",
    ):
        assert fact in propagate_run
    assert (
        step_names.index("Observe exact GitHub Packages state")
        < (step_names.index("Upload Observation Record set"))
        < step_names.index("Propagate observation status")
    )

    finalizer = jobs["release-finalizer"]
    download = _step(
        finalizer,
        "Download Observation Record by artifact ID",
    )
    assert download["if"] == (
        "always() && needs.observe-github-packages.outputs."
        "observation-set-artifact-id != ''"
    )
    command = _run(_step(finalizer, "Finalize Attempt Outcome"))
    assert (
        'add_record observation ".wdv3/input/${{ '
        "needs.observe-github-packages.outputs."
        "observation-set-artifact-name }}"
    ) in command
    assert 'if [[ -z "${snapshot_id}" ]]' not in command
    assert command.count("add_record observation ") == 1


def test_blocking_observation_shell_names_record_before_failure(
    tmp_path: Path,
) -> None:
    import hashlib  # noqa: PLC0415
    import os  # noqa: PLC0415
    import subprocess  # noqa: PLC0415

    run = _run(
        _step(
            _document(CALLEE)["jobs"]["observe-github-packages"],
            "Observe exact GitHub Packages state",
        )
    )
    expression = re.compile(r"\$\{\{\s*(?P<fact>.*?)\s*\}\}")
    rendered = expression.sub(
        lambda match: (
            "a" * 40
            if match.group("fact").strip() == "inputs.target-sha"
            else (
                "input.json"
                if match.group("fact").strip().endswith("artifact-name")
                else (
                    "1"
                    if match.group("fact").strip().endswith("artifact-id")
                    else "sha256:" + ("b" * 64)
                )
            )
        ),
        run,
    )
    bin_directory = tmp_path / "bin"
    bin_directory.mkdir()
    uv = bin_directory / "uv"
    uv.write_text(
        r"""#!/usr/bin/env python3
import os
import pathlib
import sys

args = sys.argv[1:]
output = pathlib.Path(args[args.index("--output") + 1])
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text('{"schema":"workflow-delivery/v3/projection-observation"}\n', encoding="utf-8")
github_output = pathlib.Path(args[args.index("--github-output") + 1])
with github_output.open("a", encoding="utf-8") as handle:
    handle.write("observation-set-digest=sha256:" + ("c" * 64) + "\n")
raise SystemExit(1)
""",
        encoding="utf-8",
    )
    uv.chmod(0o755)
    github_output = tmp_path / "github-output.txt"
    environment = os.environ | {
        "GITHUB_OUTPUT": str(github_output),
        "GITHUB_RUN_ATTEMPT": "3",
        "GITHUB_RUN_ID": "424242",
        "GITHUB_TOKEN": "test-token",
        "PATH": f"{bin_directory}{os.pathsep}{os.environ['PATH']}",
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
            rendered,
        ),
        check=False,
        cwd=tmp_path,
        env=environment,
        capture_output=True,
        text=True,
        timeout=10,
    )

    outputs = {
        key: value
        for line in github_output.read_text(encoding="utf-8").splitlines()
        for key, value in (line.split("=", 1),)
    }
    artifact = tmp_path / ".wdv3" / outputs["observation-set-artifact-name"]
    payload_digest = hashlib.sha256(artifact.read_bytes()).hexdigest()
    assert completed.returncode == 1
    assert outputs["observation-status"] == "1"
    assert artifact.name.endswith(f"-{payload_digest}.json")


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


def test_release_finalizer_propagates_failure_after_retention() -> None:
    finalizer = _document(CALLEE)["jobs"]["release-finalizer"]
    steps = _steps(finalizer)
    finalize = _step(finalizer, "Finalize Attempt Outcome")
    outcome_upload = _step(finalizer, "Upload final Attempt Outcome")
    summary_upload = _step(finalizer, "Upload final Attempt summary")
    propagate = _step(finalizer, "Propagate finalization status")
    names = [step["name"] for step in steps]

    assert names.index(finalize["name"]) < names.index(outcome_upload["name"])
    assert names.index(outcome_upload["name"]) < names.index(
        summary_upload["name"]
    )
    assert names.index(summary_upload["name"]) < names.index(propagate["name"])
    assert propagate["if"] == "always()"
    command = _run(propagate)
    assert "steps.finalize.outcome" in command
    assert "steps.upload-final-outcome.outcome" in command
    assert "steps.upload-final-summary.outcome" in command
    assert "steps.finalize.outputs.finalizer-status" in command
    assert '!= "0"' in command
    assert "exit 1" in command


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

    assert finalizer["if"] == EXPECTED_VALID_IDENTITY_CONDITION
    assert finalize["continue-on-error"] is True
    assert {step["name"] for step in uploads} == {
        "Upload final Attempt Outcome",
        "Upload final Attempt summary",
    }
    for step in uploads:
        assert step["if"] == "always()"
        assert step["with"]["retention-days"] == RETENTION_DAYS
        assert step["with"]["if-no-files-found"] == "error"


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
    outcome_upload = uploads["Upload final Attempt Outcome"]
    summary_upload = uploads["Upload final Attempt summary"]
    assert outcome_upload["with"]["overwrite"] is False
    assert summary_upload["with"]["overwrite"] is False
    assert _raw_artifact_name(outcome_upload["with"]) == (
        "${{ steps.finalize.outputs.outcome-artifact-name }}"
    )
    assert _raw_artifact_name(summary_upload["with"]) == (
        "${{ steps.finalize.outputs.summary-artifact-name }}"
    )


def test_live_attempt_requires_no_actions_read_permission() -> None:
    jobs = _document(CALLEE)["jobs"]
    actions_read_jobs = {
        name
        for name, job in jobs.items()
        if job.get("permissions", {}).get("actions") == "read"
    }

    assert actions_read_jobs == set()
    assert jobs["release-finalizer"]["permissions"] == {"contents": "read"}


def test_user_item13_finalizer_always_retains_outcome_summary_with_exact_contract() -> (
    None
):
    finalizer = _document(CALLEE)["jobs"]["release-finalizer"]
    outcome_upload = _step(finalizer, "Upload final Attempt Outcome")
    summary_upload = _step(finalizer, "Upload final Attempt summary")
    command = _run(_step(finalizer, "Finalize Attempt Outcome"))

    assert finalizer["if"] == EXPECTED_VALID_IDENTITY_CONDITION
    assert finalizer["permissions"] == {"contents": "read"}
    assert (
        "--outcome-output .wdv3/final-attempt/attempt-outcome.json" in command
    )
    assert "--summary-output .wdv3/final-attempt/attempt-summary.md" in command
    assert outcome_upload["if"] == "always()"
    assert outcome_upload["with"] == {
        "name": "${{ steps.finalize.outputs.outcome-artifact-name }}",
        "path": (
            ".wdv3/final-attempt/"
            "${{ steps.finalize.outputs.outcome-artifact-name }}"
        ),
        "if-no-files-found": "error",
        "retention-days": RETENTION_DAYS,
        "overwrite": False,
        "archive": False,
        "include-hidden-files": True,
    }
    assert summary_upload["if"] == "always()"
    assert summary_upload["with"] == {
        "name": "${{ steps.finalize.outputs.summary-artifact-name }}",
        "path": (
            ".wdv3/final-attempt/"
            "${{ steps.finalize.outputs.summary-artifact-name }}"
        ),
        "if-no-files-found": "error",
        "retention-days": RETENTION_DAYS,
        "overwrite": False,
        "archive": False,
        "include-hidden-files": True,
    }


_PHASE2_FINALIZER_EXPRESSION = re.compile(r"\$\{\{\s*(?P<fact>.*?)\s*\}\}")


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
    github_output_text = (
        github_output.read_text(encoding="utf-8")
        if github_output.exists()
        else ""
    )
    output_values = {
        key: value
        for line in github_output_text.splitlines()
        for key, value in (line.split("=", 1),)
    }
    final_attempt = tmp_path / ".wdv3/final-attempt"
    return {
        "github_output": github_output_text,
        "github_summary": github_summary,
        "invocations": invocation_rows,
        "outcome": final_attempt
        / output_values.get("outcome-artifact-name", "missing-outcome"),
        "output": completed.stdout + completed.stderr,
        "status": completed.returncode,
        "summary": final_attempt
        / output_values.get("summary-artifact-name", "missing-summary"),
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
        r"outcome-artifact-name=wdv3-live-buddy-attempt-outcome-"
        r"r424242-[0-9a-f]{64}\.json",
        execution["github_output"],
    )
    assert re.search(
        r"summary-artifact-name=wdv3-live-buddy-attempt-summary-"
        r"r424242-[0-9a-f]{64}\.md",
        execution["github_output"],
    )
    return argv


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


def test_publication_snapshot_lifecycle_and_transport_identity_are_exact() -> (
    None
):
    materializer = _document(CALLEE)["jobs"]["materialize-publication"]
    lifecycle_ids = {
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


def test_publication_materializer_binds_selected_ref_to_immutable_intent() -> (
    None
):
    materializer = _document(CALLEE)["jobs"]["materialize-publication"]
    download = _step(
        materializer, "Download Intent Model and Eligibility by artifact ID"
    )
    run = _run(
        _step(
            materializer,
            "Materialize immutable publication and reviewer payload",
        )
    )

    assert download["with"] == {
        "artifact-ids": (
            "${{ inputs.intent-artifact-id }},"
            "${{ inputs.repository-model-artifact-id }},"
            "${{ inputs.live-eligibility-artifact-id }}"
        ),
        "path": ".wdv3/input",
        "merge-multiple": True,
        "skip-decompress": True,
        "digest-mismatch": "error",
    }
    expected_arguments = {
        "--selected-ref": '"${{ inputs.selected-ref }}"',
        "--intent": '".wdv3/input/${{ inputs.intent-artifact-name }}"',
        "--intent-digest": '"${{ inputs.intent-digest }}"',
        "--intent-artifact-id": '"${{ inputs.intent-artifact-id }}"',
        "--intent-artifact-digest": ('"${{ inputs.intent-artifact-digest }}"'),
    }
    for option, expression in expected_arguments.items():
        assert f"{option} {expression}" in run


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
    assert "add_record publication-snapshot " in run
    for fact in (
        "publication-snapshot-artifact-name",
        "publication-snapshot-digest",
        "publication-snapshot-artifact-id",
        "publication-snapshot-artifact-digest",
        "publication-snapshot-artifact-url",
    ):
        assert f"needs.materialize-publication.outputs.{fact}" in run
        assert f"needs.approval-finalizer.outputs.{fact}" not in run
    assert "--publication-snapshot-artifact-url " in run
    assert "--publication-snapshot-payload-path " in run


@pytest.mark.parametrize(
    (
        "finalizer_status",
        "finalize_outcome",
        "upload_outcomes",
        "expected_status",
    ),
    [
        pytest.param(
            "0",
            "success",
            ("success", "success"),
            0,
            id="all-success",
        ),
        pytest.param(
            "17",
            "success",
            ("success", "success"),
            1,
            id="finalizer-status-nonzero",
        ),
        pytest.param(
            "0",
            "failure",
            ("success", "success"),
            1,
            id="finalize-step-failure",
        ),
        pytest.param(
            "0",
            "success",
            ("failure", "success"),
            1,
            id="outcome-upload-failure",
        ),
        pytest.param(
            "0",
            "success",
            ("success", "failure"),
            1,
            id="summary-upload-failure",
        ),
        pytest.param(
            "0",
            "success",
            ("cancelled", "success"),
            1,
            id="outcome-upload-cancelled",
        ),
        pytest.param(
            "0",
            "success",
            ("success", "skipped"),
            1,
            id="summary-upload-skipped",
        ),
    ],
)
def test_propagation_fails_after_successful_retention(
    tmp_path: Path,
    finalizer_status: str,
    finalize_outcome: str,
    upload_outcomes: tuple[str, str],
    expected_status: int,
) -> None:
    finalizer = _document(CALLEE)["jobs"]["release-finalizer"]
    steps = _steps(finalizer)
    finalize = _step(finalizer, "Finalize Attempt Outcome")
    outcome_upload = _step(finalizer, "Upload final Attempt Outcome")
    summary_upload = _step(finalizer, "Upload final Attempt summary")
    propagate = _step(finalizer, "Propagate finalization status")

    assert (
        steps.index(finalize)
        < steps.index(outcome_upload)
        < steps.index(summary_upload)
        < steps.index(propagate)
    )
    assert finalize["continue-on-error"] is True
    for upload in (outcome_upload, summary_upload):
        assert upload["if"] == "always()"
        assert upload["with"]["archive"] is False
        assert upload["with"]["retention-days"] == RETENTION_DAYS
        assert upload["with"]["if-no-files-found"] == "error"
    assert propagate["if"] == "always()"

    execution = _phase3_execute_workflow_run(
        tmp_path,
        _run(propagate),
        {
            "steps.finalize.outcome": finalize_outcome,
            "steps.finalize.outputs.finalizer-status": finalizer_status,
            "steps.upload-final-outcome.outcome": upload_outcomes[0],
            "steps.upload-final-summary.outcome": upload_outcomes[1],
        },
    )

    assert execution["status"] == expected_status, execution["output"]
    assert execution["output"] == ""


_EXACT_TARGET_CHECKOUT_NAME = "Check out exact selected target"
_LIVE_ATTEMPT_LOCAL_USES = (
    "./.github/workflows/workflow-delivery-v3-live-attempt.yml"
)
_CALLEE_TARGET_SHA = "${{ inputs.target-sha }}"
_SAME_REVISION_GUARD_NAME = "Require same-revision Buddy caller"


def test_live_attempt_has_only_local_same_commit_buddy_caller() -> None:
    callee_suffix = f"/{CALLEE.relative_to(REPO_ROOT).as_posix()}"
    references: list[tuple[str, str, str]] = []

    for path in sorted(
        {
            *CALLER.parent.rglob("*.yml"),
            *CALLER.parent.rglob("*.yaml"),
        }
    ):
        jobs = _document(path).get("jobs", {})
        assert isinstance(jobs, dict), path
        for job_name, job in jobs.items():
            if not isinstance(job, dict):
                continue
            uses = job.get("uses")
            if isinstance(uses, str) and uses.partition("@")[0].endswith(
                callee_suffix
            ):
                references.append(
                    (
                        path.relative_to(REPO_ROOT).as_posix(),
                        str(job_name),
                        uses,
                    )
                )

    assert references == [
        (
            ".github/workflows/workflow-delivery-v3-buddy-smoke.yml",
            "run-live-attempt",
            _LIVE_ATTEMPT_LOCAL_USES,
        )
    ]


def test_buddy_target_sha_binding_chain_is_exact(tmp_path: Path) -> None:
    caller_jobs = _document(CALLER)["jobs"]
    request = _run(
        _step(caller_jobs["request"], "Normalize fixed live request")
    )

    assert re.findall(r'--target "([^"]+)"', request) == ["${GITHUB_SHA}"]
    assert re.findall(r'echo "target-sha=([^"]+)"', request) == [
        "${GITHUB_SHA}"
    ]
    assert {
        "request": caller_jobs["request"]["outputs"]["target-sha"],
        "discover-node": caller_jobs["discover-node"]["outputs"]["target-sha"],
        "compile-model": caller_jobs["compile-model"]["outputs"]["target-sha"],
        "evaluate-live-eligibility": caller_jobs["evaluate-live-eligibility"][
            "outputs"
        ]["target-sha"],
        "run-live-attempt": caller_jobs["run-live-attempt"]["with"][
            "target-sha"
        ],
    } == {
        "request": "${{ steps.request.outputs.target-sha }}",
        "discover-node": "${{ needs.request.outputs.target-sha }}",
        "compile-model": "${{ needs.discover-node.outputs.target-sha }}",
        "evaluate-live-eligibility": (
            "${{ needs.compile-model.outputs.target-sha }}"
        ),
        "run-live-attempt": (
            "${{ needs.evaluate-live-eligibility.outputs.target-sha }}"
        ),
    }

    callee_jobs = _document(CALLEE)["jobs"]
    admit = callee_jobs["admit"]
    admit_steps = _steps(admit)
    guard = _step(admit, _SAME_REVISION_GUARD_NAME)
    checkout = _step(admit, _EXACT_TARGET_CHECKOUT_NAME)
    bind = _step(admit, "Bind current live Attempt")
    upload = _step(admit, "Upload Release Attempt binding")
    positions = [
        admit_steps.index(step) for step in (guard, checkout, bind, upload)
    ]

    assert admit_steps[0] == guard
    assert positions == sorted(positions)
    assert guard["id"] == "identity"
    assert admit["outputs"]["identity-admitted"] == (
        "${{ steps.identity.outputs.identity-admitted }}"
    )
    assert guard["shell"] == "bash"
    assert guard["env"] == {
        "CALLER_REPOSITORY": "${{ github.repository }}",
        "CALLER_SHA": "${{ github.sha }}",
        "CALLER_WORKFLOW_SHA": "${{ github.workflow_sha }}",
        "TARGET_SHA": _CALLEE_TARGET_SHA,
    }
    guard_command = _run(guard)
    assert guard_command.splitlines() == [
        "set -euo pipefail",
        '[[ "${CALLER_REPOSITORY}" == "hcoona/three" ]]',
        '[[ "${TARGET_SHA}" =~ ^[0-9a-f]{40}$ ]]',
        '[[ "${TARGET_SHA}" == "${CALLER_SHA}" ]]',
        '[[ "${TARGET_SHA}" == "${CALLER_WORKFLOW_SHA}" ]]',
        'echo "identity-admitted=true" >> "${GITHUB_OUTPUT}"',
    ]
    assert "${{" not in guard_command

    identity_sha = "a" * 40
    valid_output = tmp_path / "valid-identity-output.txt"
    valid_environment = {
        "CALLER_REPOSITORY": "hcoona/three",
        "CALLER_SHA": identity_sha,
        "CALLER_WORKFLOW_SHA": identity_sha,
        "TARGET_SHA": identity_sha,
        "GITHUB_OUTPUT": str(valid_output),
    }
    valid_execution = _phase3_execute_workflow_run(
        tmp_path,
        guard_command,
        {},
        environment=valid_environment,
    )
    assert valid_execution["status"] == 0, valid_execution["output"]
    assert valid_execution["output"] == ""
    assert valid_output.read_bytes() == b"identity-admitted=true\n"

    invalid_identities = (
        {"CALLER_REPOSITORY": "example/other"},
        {"TARGET_SHA": "A" * 40},
        {"CALLER_SHA": "b" * 40},
        {"CALLER_WORKFLOW_SHA": "c" * 40},
    )
    for index, invalid_identity in enumerate(invalid_identities):
        invalid_output = tmp_path / f"invalid-identity-{index}.txt"
        invalid_execution = _phase3_execute_workflow_run(
            tmp_path,
            guard_command,
            {},
            environment=valid_environment
            | invalid_identity
            | {"GITHUB_OUTPUT": str(invalid_output)},
        )
        assert invalid_execution["status"] != 0
        assert invalid_execution["output"] == ""
        assert not invalid_output.exists()

    release_commands = [
        str(step["run"])
        for job in callee_jobs.values()
        for step in _steps(job)
        if "three-workflow-delivery-v3 release " in str(step.get("run", ""))
    ]
    assert release_commands
    assert [
        re.findall(r'--target "([^"]+)"', command)
        for command in release_commands
    ] == [[_CALLEE_TARGET_SHA]] * len(release_commands)

    assert (
        callee_jobs["release-finalizer"]["if"]
        == EXPECTED_VALID_IDENTITY_CONDITION
    )
    publisher = callee_jobs["publish-github-packages"]
    assert "success()" in _condition_conjuncts(publisher)
    assert "admit" in _transitive_needs(
        callee_jobs,
        "publish-github-packages",
    )


def test_temporary_acceptance_workflows_are_absent_with_disabled_normal_buddy() -> (
    None
):
    workflows = REPO_ROOT / ".github/workflows"
    temporary_workflows = tuple(
        sorted(
            {
                *workflows.glob(
                    "workflow-delivery-v3-buddy-smoke-acceptance*.yml"
                ),
                *workflows.glob(
                    "workflow-delivery-v3-buddy-smoke-acceptance*.yaml"
                ),
            }
        )
    )
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
    raw = CALLER.read_text(encoding="utf-8") + CALLEE.read_text(
        encoding="utf-8"
    )

    assert temporary_workflows == ()
    assert caller_triggers == {"workflow_dispatch": None}
    assert isinstance(callee_triggers, dict)
    assert set(callee_triggers) == {"workflow_call"}
    assert "schedule:" not in raw
    assert "push:" not in raw
    assert "live_enabled: true" not in raw
    assert "workflow-delivery-v3-buddy-smoke-acceptance-retry-5.yml" not in raw
    assert GOVERNANCE.is_file()
    governance = json.loads(GOVERNANCE.read_text(encoding="utf-8"))
    assert governance["live_enabled"] is False


def test_current_authoritative_buddy_jobs_each_guard_attempt_one() -> None:
    documents = {
        "caller": _document(CALLER),
        "callee": _document(CALLEE),
    }

    for document_name, document in documents.items():
        for job_name, job in document["jobs"].items():
            assert ATTEMPT_ONE_CONDITION in _condition_conjuncts(job), (
                f"{document_name}:{job_name} must independently reject reruns"
            )


def test_qualification_routing_preserves_failure_semantics() -> None:
    jobs = _document(CALLEE)["jobs"]

    for job_name in ("npm-artifact-qualification", "qualification-finalizer"):
        assert {
            "always()",
            "needs.build-tarball.result != 'skipped'",
        } <= _condition_conjuncts(jobs[job_name])
    assert (
        "needs.qualification-finalizer.outputs.qualification-result "
        "== 'success'" in _condition_conjuncts(jobs["observe-github-packages"])
    )


def test_workflow_cancellation_marker_runs_only_on_cancellation() -> None:
    cancellation = _document(CALLEE)["jobs"]["workflow-cancellation"]

    assert "cancelled()" in _condition_conjuncts(cancellation)


def test_approve_publication_is_the_only_environment_job_and_sentinel_is_first() -> (
    None
):
    jobs = _document(CALLEE)["jobs"]
    environment_jobs = {
        name: job["environment"]
        for name, job in jobs.items()
        if "environment" in job
    }

    assert environment_jobs == {
        "approve-publication": {
            "name": APPROVAL_ENVIRONMENT_NAME,
            "url": (
                "${{ needs.materialize-publication.outputs."
                "reviewer-artifact-url }}"
            ),
        }
    }
    assert all(
        "environment" not in job for job in _document(CALLER)["jobs"].values()
    )
    approval = jobs["approve-publication"]
    assert (
        "needs.materialize-publication.outputs.publish-required == 'true'"
        in _condition_conjuncts(approval)
    )
    executable_steps = [
        step for step in _steps(approval) if "run" in step or "uses" in step
    ]
    sentinel = _step(approval, "Verify approval Environment marker")
    assert executable_steps[0] is sentinel
    assert sentinel == {
        "name": "Verify approval Environment marker",
        "id": "approval-environment-marker",
        "shell": "bash",
        "env": {
            "ACTUAL_ENVIRONMENT_MARKER": (
                "${{ vars.WDV3_APPROVAL_ENVIRONMENT_MARKER }}"
            )
        },
        "run": (
            'if [[ "${ACTUAL_ENVIRONMENT_MARKER}" != '
            '"workflow-delivery-v3-buddy-approval/v1" ]]; then\n'
            '  echo "::error::Environment marker does not match the approval '
            'Environment contract" >&2\n'
            "  exit 1\n"
            "fi\n"
        ),
    }


@pytest.mark.parametrize(
    ("actual_marker", "expected_status"),
    [
        pytest.param(APPROVAL_ENVIRONMENT_MARKER, 0, id="exact"),
        pytest.param("", 1, id="missing"),
        pytest.param("wrong-environment/v1", 1, id="wrong"),
        pytest.param(APPROVAL_ENVIRONMENT_MARKER.upper(), 1, id="case-altered"),
    ],
)
def test_approval_environment_sentinel_executes_case_sensitive(
    tmp_path: Path,
    actual_marker: str,
    expected_status: int,
) -> None:
    sentinel = _step(
        _document(CALLEE)["jobs"]["approve-publication"],
        "Verify approval Environment marker",
    )

    execution = _phase3_execute_workflow_run(
        tmp_path,
        _run(sentinel),
        {},
        environment={"ACTUAL_ENVIRONMENT_MARKER": actual_marker},
    )

    assert execution["status"] == expected_status
    assert ("Environment marker does not match" in execution["output"]) is (
        expected_status != 0
    )


def test_reviewer_identity_and_approval_bundle_are_durable_before_wait() -> (
    None
):
    jobs = _document(CALLEE)["jobs"]
    materializer = jobs["materialize-publication"]
    approval = jobs["approve-publication"]
    steps = _steps(materializer)
    upload_reviewer = _step(materializer, "Upload reviewer summary")
    form_bundle = _step(
        materializer,
        "Form complete pre-wait Approval Bundle",
    )
    upload_bundle = _step(materializer, "Upload Approval Bundle")
    publish_summary = _step(
        materializer,
        "Publish completed reviewer summary and artifact link",
    )

    assert "environment" not in materializer
    assert _needs(approval) == ("admit", "materialize-publication")
    assert (
        steps.index(upload_reviewer)
        < steps.index(form_bundle)
        < steps.index(upload_bundle)
        < steps.index(publish_summary)
    )
    publish_condition = "steps.materialize.outputs.publish-required == 'true'"
    for step in (
        upload_reviewer,
        form_bundle,
        upload_bundle,
    ):
        assert step["if"] == publish_condition
    assert publish_summary["if"] == "steps.upload-bundle.outcome == 'success'"

    bundle_command = _run(form_bundle)
    assert "release form-approval-bundle" in bundle_command
    for option in (
        "--attempt-binding",
        "--qualification-decision",
        "--publication-snapshot",
        "--publication-snapshot-digest",
        "--publication-snapshot-artifact-id",
        "--publication-snapshot-artifact-digest",
        "--publication-snapshot-artifact-url",
        "--publication-snapshot-payload-path",
        "--reviewer-summary",
        "--reviewer-summary-digest",
        "--reviewer-summary-artifact-id",
        "--reviewer-summary-artifact-digest",
        "--reviewer-summary-artifact-url",
        "--reviewer-summary-payload-path",
        "--control",
    ):
        assert bundle_command.count(f"{option} ") == 1
    assert "bind-reviewer-artifact" not in str(materializer)
    assert "reviewer-formatter-input" not in str(materializer)
    assert '--reviewer-summary ".wdv3/reviewer-summary.md"' in bundle_command
    assert upload_reviewer["with"] == {
        "name": "reviewer-summary.md",
        "path": ".wdv3/reviewer-summary.md",
        "if-no-files-found": "error",
        "retention-days": RETENTION_DAYS,
        "overwrite": False,
        "archive": False,
        "include-hidden-files": True,
    }
    assert materializer["outputs"]["approval-bundle-artifact-id"] == (
        "${{ steps.upload-bundle.outputs.artifact-id }}"
    )
    assert materializer["outputs"]["approval-bundle-artifact-digest"] == (
        "${{ steps.upload-bundle.outputs.artifact-digest }}"
    )
    assert materializer["outputs"]["approval-bundle-artifact-url"] == (
        "${{ steps.upload-bundle.outputs.artifact-url }}"
    )
    assert materializer["outputs"]["approval-bundle-artifact-name"] == (
        "${{ steps.form-bundle.outputs.approval-bundle-artifact-name }}"
    )
    assert materializer["outputs"]["approval-bundle-digest"] == (
        "${{ steps.form-bundle.outputs.approval-bundle-digest }}"
    )
    assert upload_bundle["uses"] == UPLOAD
    assert upload_bundle["with"] == {
        "name": "${{ steps.form-bundle.outputs.approval-bundle-artifact-name }}",
        "path": (
            ".wdv3/"
            "${{ steps.form-bundle.outputs.approval-bundle-artifact-name }}"
        ),
        "if-no-files-found": "error",
        "retention-days": RETENTION_DAYS,
        "overwrite": False,
        "archive": False,
        "include-hidden-files": True,
    }


def test_approve_publication_freshly_admits_governance_and_emits_sole_authorization() -> (
    None
):
    document = _document(CALLEE)
    approval = document["jobs"]["approve-publication"]
    steps = _steps(approval)
    download = _step(approval, "Download authorization closure by artifact ID")
    authorize = _step(approval, "Form sole Publication Authorization")
    upload = _step(approval, "Upload Publication Authorization")

    assert [step["name"] for step in steps] == [
        "Verify approval Environment marker",
        "Check out exact selected target",
        "Install uv",
        "Download authorization closure by artifact ID",
        "Form sole Publication Authorization",
        "Upload Publication Authorization",
    ]
    assert download["with"] == {
        "artifact-ids": (
            "${{ inputs.intent-artifact-id }},"
            "${{ inputs.repository-model-artifact-id }},"
            "${{ inputs.live-eligibility-artifact-id }},"
            "${{ needs.admit.outputs.attempt-artifact-id }},"
            "${{ needs.materialize-publication.outputs."
            "qualification-snapshot-artifact-id }},"
            "${{ needs.materialize-publication.outputs."
            "decision-artifact-id }},"
            "${{ needs.materialize-publication.outputs."
            "release-artifact-artifact-id }},"
            "${{ needs.materialize-publication.outputs."
            "observation-artifact-id }},"
            "${{ needs.materialize-publication.outputs."
            "publication-snapshot-artifact-id }},"
            "${{ needs.materialize-publication.outputs."
            "approval-bundle-artifact-id }},"
            "${{ needs.materialize-publication.outputs."
            "reviewer-artifact-id }}"
        ),
        "path": ".wdv3/input",
        "merge-multiple": True,
        "skip-decompress": True,
        "digest-mismatch": "error",
    }
    assert authorize["id"] == "authorize"
    assert authorize["env"] == {"GITHUB_TOKEN": "${{ github.token }}"}
    command = _run(authorize)
    assert (
        command.count(
            "three-workflow-delivery-v3 release form-publication-authorization"
        )
        == 1
    )
    for option in (
        "--github-token",
        "--intent",
        "--repository-model",
        "--attempt-binding",
        "--attempt-binding-digest",
        "--attempt-binding-artifact-id",
        "--attempt-binding-artifact-digest",
        "--approval-bundle",
        "--approval-bundle-digest",
        "--approval-bundle-artifact-id",
        "--approval-bundle-artifact-digest",
        "--approval-bundle-artifact-url",
        "--approval-bundle-payload-path",
        "--qualification-snapshot",
        "--qualification-snapshot-digest",
        "--qualification-snapshot-artifact-id",
        "--qualification-snapshot-artifact-digest",
        "--qualification-decision",
        "--release-artifact",
        "--release-artifact-digest",
        "--release-artifact-artifact-id",
        "--release-artifact-artifact-digest",
        "--publication-snapshot",
        "--publication-snapshot-digest",
        "--publication-snapshot-artifact-id",
        "--publication-snapshot-artifact-digest",
        "--publication-snapshot-artifact-url",
        "--publication-snapshot-payload-path",
        "--reviewer-summary",
        "--reviewer-summary-digest",
        "--reviewer-summary-artifact-id",
        "--reviewer-summary-artifact-digest",
        "--reviewer-summary-artifact-url",
        "--reviewer-summary-payload-path",
        "--live-eligibility-decision",
        "--live-eligibility-artifact-id",
        "--live-eligibility-artifact-digest",
        "--live-eligibility-payload-digest",
        "--approval-boundary-sentinel-result",
        "--control",
    ):
        assert command.count(f"{option} ") == 1
    assert (
        "--qualification-snapshot-artifact-id "
        '"${{ needs.materialize-publication.outputs.'
        'qualification-snapshot-artifact-id }}"' in command
    )
    assert (
        "--release-artifact-artifact-id "
        '"${{ needs.materialize-publication.outputs.'
        'release-artifact-artifact-id }}"' in command
    )
    assert '--reviewer-summary ".wdv3/input/reviewer-summary.md"' in command
    assert (
        "--approval-boundary-sentinel-result "
        '"${{ steps.approval-environment-marker.outcome }}"' in command
    )
    assert "--authorized-at" not in command
    assert "authorized_at=" not in command
    assert "date -u" not in command
    assert "--output .wdv3/publication-authorization.json" in command
    assert steps.index(authorize) < steps.index(upload)
    assert upload["uses"] == UPLOAD
    assert upload["with"] == {
        "name": (
            "${{ steps.authorize.outputs."
            "publication-authorization-artifact-name }}"
        ),
        "path": (
            ".wdv3/${{ steps.authorize.outputs."
            "publication-authorization-artifact-name }}"
        ),
        "if-no-files-found": "error",
        "retention-days": RETENTION_DAYS,
        "overwrite": False,
        "archive": False,
        "include-hidden-files": True,
    }
    authorization_commands = [
        step
        for job in document["jobs"].values()
        for step in _steps(job)
        if "form-publication-authorization" in str(step.get("run", ""))
    ]
    authorization_uploads = [
        step
        for job in document["jobs"].values()
        for step in _steps(job)
        if step.get("name") == "Upload Publication Authorization"
    ]
    assert authorization_commands == [authorize]
    assert authorization_uploads == [upload]


def test_normal_live_has_no_retired_authority_or_history_surface() -> None:
    document = _document(CALLEE)
    jobs = document["jobs"]
    raw = (
        CALLER.read_text(encoding="utf-8") + CALLEE.read_text(encoding="utf-8")
    ).casefold()

    assert "approval-finalizer" not in jobs
    assert "approval" not in jobs
    assert "capability" not in raw
    assert "authorization_formatter" not in raw
    assert "authorization-formatter" not in raw
    assert "/actions/runs/" not in raw
    assert "/attempts/" not in raw
    assert "jobs_url" not in raw
    assert "workflow-delivery-v3-buddy-github-packages" not in raw
    assert "upload authorization record" not in raw
    assert all(
        job.get("permissions", {}).get("packages") != "write"
        for job in jobs.values()
    )


def test_normal_live_pair_has_no_package_write_permission() -> None:
    documents = {
        "caller": _document(CALLER),
        "callee": _document(CALLEE),
    }
    workflow_text = "".join(
        path.read_text(encoding="utf-8") for path in (CALLER, CALLEE)
    ).casefold()
    package_grants = {
        f"{workflow_name}:{job_name}": permissions["packages"]
        for workflow_name, document in (
            ("caller", documents["caller"]),
            ("callee", documents["callee"]),
        )
        for job_name, job in document["jobs"].items()
        if "packages" in (permissions := job.get("permissions", {}))
    }

    assert package_grants == {
        "caller:run-live-attempt": "read",
        "callee:observe-github-packages": "read",
        "callee:prove-exact-satisfied": "read",
    }
    assert all(
        document.get("permissions", {}).get("packages") != "write"
        for document in documents.values()
    )
    assert all(permission != "write" for permission in package_grants.values())
    assert "packages: write" not in workflow_text


def test_exact_satisfied_path_has_fresh_proof_without_mutation_authority() -> (
    None
):
    jobs = _document(CALLEE)["jobs"]
    proof_job = jobs["prove-exact-satisfied"]
    steps = _steps(proof_job)
    download = _step(
        proof_job,
        "Download exact-satisfied closure by artifact ID",
    )
    prove = _step(proof_job, "Form exact-satisfied finalization proof")
    upload = _step(proof_job, "Upload exact-satisfied finalization proof")

    assert "environment" not in proof_job
    assert proof_job["permissions"] == {"contents": "read", "packages": "read"}
    assert (
        "needs.materialize-publication.outputs.publish-required == 'false'"
        in _condition_conjuncts(proof_job)
    )
    assert set(_needs(proof_job)) == {
        "admit",
        "plan-qualification",
        "materialize-publication",
        "approve-publication",
        "publish-github-packages",
    }
    assert (
        "needs.publish-github-packages.result == 'skipped'"
        in _condition_conjuncts(proof_job)
    )
    assert (
        "needs.approve-publication.result == 'skipped'"
        in _condition_conjuncts(proof_job)
    )
    assert (
        '--publisher-conclusion "${{ needs.publish-github-packages.result }}"'
        in prove["run"]
    )
    assert download["with"] == {
        "artifact-ids": (
            "${{ inputs.intent-artifact-id }},"
            "${{ inputs.repository-model-artifact-id }},"
            "${{ inputs.live-eligibility-artifact-id }},"
            "${{ needs.admit.outputs.attempt-artifact-id }},"
            "${{ needs.plan-qualification.outputs.adapter-context-artifact-id }},"
            "${{ needs.materialize-publication.outputs."
            "qualification-snapshot-artifact-id }},"
            "${{ needs.materialize-publication.outputs."
            "decision-artifact-id }},"
            "${{ needs.materialize-publication.outputs."
            "release-artifact-artifact-id }},"
            "${{ needs.materialize-publication.outputs."
            "observation-artifact-id }},"
            "${{ needs.materialize-publication.outputs."
            "publication-snapshot-artifact-id }}"
        ),
        "path": ".wdv3/input",
        "merge-multiple": True,
        "skip-decompress": True,
        "digest-mismatch": "error",
    }
    assert prove["env"] == {"GITHUB_TOKEN": "${{ github.token }}"}
    command = _run(prove)
    assert (
        command.count(
            "three-workflow-delivery-v3 release prove-exact-satisfied"
        )
        == 1
    )
    for option in (
        "--github-token",
        "--intent",
        "--repository-model",
        "--attempt-binding",
        "--publication-snapshot",
        "--qualification-snapshot",
        "--qualification-decision",
        "--qualification-decision-artifact-url",
        "--qualification-decision-payload-path",
        "--release-artifact",
        "--observation",
        "--live-eligibility-decision",
        "--control",
    ):
        assert command.count(f"{option} ") == 1
    assert "--proved-at" not in command
    assert "proved_at=" not in command
    assert "date -u" not in command
    proof_text = str(steps).casefold()
    for forbidden in (
        "environment",
        "approval-bundle",
        "publication-authorization",
        "approve-publication",
        "reviewer",
    ):
        assert forbidden not in proof_text
    assert "--publication-snapshot-artifact-url " in command
    assert "--publication-snapshot-payload-path " in command
    assert "--adapter-context " in command
    assert steps.index(download) < steps.index(prove) < steps.index(upload)
    assert upload["with"]["archive"] is False


def test_action_path_reaches_read_only_fail_closed_publisher_preflight() -> (
    None
):
    jobs = _document(CALLEE)["jobs"]
    approval = jobs["approve-publication"]
    publisher = jobs["publish-github-packages"]
    steps = _steps(publisher)
    download = _step(publisher, "Download publisher closure by artifact ID")
    preflight = _step(
        publisher,
        "Reject unimplemented conditional destination primitive",
    )

    assert publisher["name"] == ("Reject unsupported GitHub Packages primitive")
    assert set(approval["outputs"]) == {
        "publication-authorization-artifact-id",
        "publication-authorization-artifact-digest",
        "publication-authorization-artifact-name",
        "publication-authorization-digest",
    }
    assert _needs(publisher) == (
        "approve-publication",
        "materialize-publication",
    )
    assert {
        "materialize-publication",
        "approve-publication",
    } <= _transitive_needs(jobs, "publish-github-packages")
    assert publisher["permissions"] == {"contents": "read"}
    assert "environment" not in publisher
    assert {
        "needs.approve-publication.result == 'success'",
    } <= _condition_conjuncts(publisher)
    assert "publish-required" not in publisher["if"]
    assert publisher["concurrency"] == {
        "group": (
            "wdv3-resource-${{ needs.materialize-publication.outputs."
            "resource-concurrency-key }}"
        ),
        "cancel-in-progress": False,
    }
    required_ids = (
        "${{ needs.materialize-publication.outputs."
        "qualification-snapshot-artifact-id }}",
        "${{ needs.materialize-publication.outputs.decision-artifact-id }}",
        "${{ needs.materialize-publication.outputs.adapter-context-artifact-id }}",
        "${{ needs.materialize-publication.outputs.release-artifact-artifact-id }}",
        "${{ needs.materialize-publication.outputs."
        "publication-snapshot-artifact-id }}",
        "${{ needs.materialize-publication.outputs.approval-bundle-artifact-id }}",
        "${{ needs.approve-publication.outputs."
        "publication-authorization-artifact-id }}",
        "${{ needs.materialize-publication.outputs.reviewer-artifact-id }}",
    )
    artifact_ids = download["with"]["artifact-ids"].split(",")
    assert set(artifact_ids) == set(required_ids)
    assert download["with"]["path"] == ".wdv3/input"
    assert download["with"]["merge-multiple"] is True
    assert download["with"]["skip-decompress"] is True
    assert download["with"]["digest-mismatch"] == "error"
    command = _run(preflight)
    assert "env" not in preflight
    assert "continue-on-error" not in preflight
    assert steps.index(download) < steps.index(preflight)
    assert (
        "three-workflow-delivery-v3 release preflight-github-packages"
        in command
    )
    for option in (
        "--publication-snapshot",
        "--publication-snapshot-digest",
        "--publication-snapshot-artifact-id",
        "--publication-snapshot-artifact-digest",
        "--publication-snapshot-artifact-url",
        "--publication-snapshot-payload-path",
        "--approval-bundle",
        "--approval-bundle-digest",
        "--approval-bundle-artifact-id",
        "--approval-bundle-artifact-digest",
        "--approval-bundle-artifact-url",
        "--approval-bundle-payload-path",
        "--reviewer-summary",
        "--reviewer-summary-digest",
        "--reviewer-summary-artifact-id",
        "--reviewer-summary-artifact-digest",
        "--reviewer-summary-artifact-url",
        "--reviewer-summary-payload-path",
        "--publication-authorization",
    ):
        assert command.count(f"{option} ") == 1
    assert '--reviewer-summary ".wdv3/input/reviewer-summary.md"' in command
    for forbidden in (
        "--github-token",
        "--tarball",
        "--preflight-output",
        "release publish-github-packages",
        "npm publish",
        "mark-github-packages-mutation-start",
        "mutation-may-have-started",
        "action-result",
    ):
        assert forbidden not in command
    assert not any(step.get("uses") == UPLOAD for step in steps)


def test_finalizer_consumes_current_branch_authorities_only() -> None:
    jobs = _document(CALLEE)["jobs"]
    finalizer = jobs["release-finalizer"]
    expected_downloads = {
        "Download Approval Bundle by artifact ID": (
            "materialize-publication",
            "approval-bundle",
        ),
        "Download Publication Authorization by artifact ID": (
            "approve-publication",
            "publication-authorization",
        ),
        "Download exact-satisfied finalization proof by artifact ID": (
            "prove-exact-satisfied",
            "exact-satisfied-finalization-proof",
        ),
    }

    for step_name, (producer, role) in expected_downloads.items():
        download = _step(finalizer, step_name)
        assert download["if"] == (
            f"always() && needs.{producer}.outputs.{role}-artifact-id != ''"
        )
        assert download["with"] == {
            "artifact-ids": (
                f"${{{{ needs.{producer}.outputs.{role}-artifact-id }}}}"
            ),
            "path": ".wdv3/input",
            "skip-decompress": True,
            "digest-mismatch": "error",
        }

    command = _run(_step(finalizer, "Finalize Attempt Outcome"))
    for role, producer in (
        ("publication-snapshot", "materialize-publication"),
        ("approval-bundle", "materialize-publication"),
        ("publication-authorization", "approve-publication"),
        ("exact-satisfied-finalization-proof", "prove-exact-satisfied"),
    ):
        assert (
            f'add_record {role} ".wdv3/input/${{{{ needs.{producer}.outputs.'
            f'{role}-artifact-name }}}}"'
        ) in command
    for role in ("publication-snapshot", "approval-bundle"):
        assert (
            f"needs.materialize-publication.outputs.{role}-artifact-url"
            in command
        )
        assert (
            f"needs.materialize-publication.outputs.{role}-artifact-name"
            in command
        )
    command_casefold = command.casefold()
    for forbidden in (
        "approval-finalizer",
        "capability-decision",
        "capability-admission",
        "add_record authorization ",
        "mutation-marker",
        "action-result",
    ):
        assert forbidden not in command_casefold


def test_current_authority_uploads_use_raw_transport() -> None:
    document = _document(CALLEE)
    uploads = _artifact_steps(document, UPLOAD)
    assert uploads
    raw_names = []
    for step in uploads:
        assert step["with"]["archive"] is False
        raw_names.append(_raw_artifact_name(step["with"]))
    assert len(raw_names) == len(set(raw_names))
    materializer = document["jobs"]["materialize-publication"]
    assert "reviewer-artifact-name" not in materializer["outputs"]
    reviewer_upload = _step(materializer, "Upload reviewer summary")
    assert reviewer_upload["with"]["name"] == "reviewer-summary.md"


def test_live_observation_authority_closes_every_current_consumer() -> None:
    jobs = _document(CALLEE)["jobs"]
    assert jobs["qualification-finalizer"]["outputs"][
        "decision-artifact-url"
    ] == ("${{ steps.upload.outputs.artifact-url }}")
    consumers = (
        (
            "observe-github-packages",
            "Observe exact GitHub Packages state",
            "qualification-finalizer",
        ),
        (
            "materialize-publication",
            "Materialize immutable publication and reviewer payload",
            "observe-github-packages",
        ),
        (
            "approve-publication",
            "Form sole Publication Authorization",
            "materialize-publication",
        ),
        (
            "prove-exact-satisfied",
            "Form exact-satisfied finalization proof",
            "materialize-publication",
        ),
        (
            "release-finalizer",
            "Finalize Attempt Outcome",
            "qualification-finalizer",
        ),
    )
    for name, step_name, producer in consumers:
        job = jobs[name]
        command = _run(_step(job, step_name))
        assert producer in _needs(job)
        assert "admit" in _needs(job)
        downloads = [
            reference
            for step in _steps(job)
            if step.get("uses") == DOWNLOAD
            for reference in step["with"]["artifact-ids"].split(",")
        ]
        for role in ("intent", "repository-model", "live-eligibility"):
            assert downloads.count(f"${{{{ inputs.{role}-artifact-id }}}}") == 1
        assert (
            downloads.count("${{ needs.admit.outputs.attempt-artifact-id }}")
            == 1
        )
        assert (
            downloads.count(
                f"${{{{ needs.{producer}.outputs.decision-artifact-id }}}}"
            )
            == 1
        )
        for option, output in (
            ("artifact-id", "artifact-id"),
            ("artifact-digest", "artifact-digest"),
            ("artifact-url", "artifact-url"),
            ("payload-path", "artifact-name"),
            ("digest", "digest"),
        ):
            assert (
                f'--qualification-decision-{option} "${{{{ needs.{producer}.outputs.decision-{output} }}}}"'
            ) in command
        for option in (
            "intent",
            "intent-digest",
            "intent-artifact-id",
            "intent-artifact-digest",
            "repository-model",
            "repository-model-digest",
            "repository-model-artifact-id",
            "repository-model-artifact-digest",
            "live-eligibility-decision",
            "live-eligibility-artifact-id",
            "live-eligibility-artifact-digest",
            "live-eligibility-payload-digest",
            "attempt-binding",
            "attempt-binding-digest",
            "attempt-binding-artifact-id",
            "attempt-binding-artifact-digest",
        ):
            assert command.count(f"--{option} ") == 1
    for consumer, producer in (
        ("observe-github-packages", "qualification-finalizer"),
        ("materialize-publication", "observe-github-packages"),
    ):
        assert jobs[consumer]["outputs"]["decision-artifact-url"] == (
            f"${{{{ needs.{producer}.outputs.decision-artifact-url }}}}"
        )


def test_current_authority_jobs_install_no_mutating_toolchain() -> None:
    caller_jobs = _document(CALLER)["jobs"]
    callee_jobs = _document(CALLEE)["jobs"]
    compiler = _run(
        _step(caller_jobs["discover-node"], "Run Node Provider once")
    )
    intent_admission = _run(
        _step(caller_jobs["discover-node"], "Admit current Release Intent")
    )

    assert '--purpose "${WDV3_PURPOSE}"' in intent_admission
    assert "--compiler-producer compile-live-model" in compiler
    assert "--compiler-producer compile-model" not in compiler
    for job_name in (
        "approve-publication",
        "prove-exact-satisfied",
        "publish-github-packages",
    ):
        assert all(
            step.get("uses") != MISE for step in _steps(callee_jobs[job_name])
        )


def test_current_live_checkouts_use_exact_selected_target() -> None:
    jobs = _document(CALLEE)["jobs"]
    checkouts = [
        step
        for job_name, job in jobs.items()
        for step in _steps(job)
        if step.get("uses") == CHECKOUT
    ]

    assert checkouts
    for checkout in checkouts:
        assert checkout["with"] == {
            "fetch-depth": 0,
            "persist-credentials": False,
            "ref": "${{ github.sha }}",
        }


def test_current_buddy_target_identity_chain_reaches_both_authority_paths() -> (
    None
):
    caller_jobs = _document(CALLER)["jobs"]
    callee_jobs = _document(CALLEE)["jobs"]
    guard = _step(callee_jobs["admit"], "Require same-revision Buddy caller")
    guard_command = _run(guard)

    assert guard is _steps(callee_jobs["admit"])[0]
    assert guard["env"] == {
        "CALLER_REPOSITORY": "${{ github.repository }}",
        "CALLER_SHA": "${{ github.sha }}",
        "CALLER_WORKFLOW_SHA": "${{ github.workflow_sha }}",
        "TARGET_SHA": "${{ inputs.target-sha }}",
    }
    assert '[[ "${TARGET_SHA}" == "${CALLER_SHA}" ]]' in guard_command
    assert '[[ "${TARGET_SHA}" == "${CALLER_WORKFLOW_SHA}" ]]' in (
        guard_command
    )
    assert caller_jobs["run-live-attempt"]["with"]["target-sha"] == (
        "${{ needs.evaluate-live-eligibility.outputs.target-sha }}"
    )
    for job_name in ("approve-publication", "prove-exact-satisfied"):
        assert "admit" in _transitive_needs(callee_jobs, job_name)

    target_arguments = [
        target
        for job in callee_jobs.values()
        for step in _steps(job)
        if "three-workflow-delivery-v3 release " in str(step.get("run", ""))
        for target in re.findall(
            r'--target "([^"]+)"',
            str(step.get("run", "")),
        )
    ]
    assert target_arguments
    assert set(target_arguments) == {"${{ inputs.target-sha }}"}


def test_completed_pre_wait_bundle_gates_reviewer_summary_link(
    tmp_path: Path,
) -> None:
    materializer = _document(CALLEE)["jobs"]["materialize-publication"]
    steps = _steps(materializer)
    upload_reviewer = _step(materializer, "Upload reviewer summary")
    form_bundle = _step(
        materializer,
        "Form complete pre-wait Approval Bundle",
    )
    upload_bundle = _step(materializer, "Upload Approval Bundle")
    summary = _step(
        materializer,
        "Publish completed reviewer summary and artifact link",
    )

    assert (
        steps.index(upload_reviewer)
        < steps.index(form_bundle)
        < steps.index(upload_bundle)
        < steps.index(summary)
    )
    assert summary["if"] == "steps.upload-bundle.outcome == 'success'"

    reviewer = tmp_path / ".wdv3" / "reviewer-summary.md"
    reviewer.parent.mkdir(parents=True)
    reviewer_bytes = b"# Immutable reviewer summary\n\n- Action count: 1\n"
    reviewer.write_bytes(reviewer_bytes)
    artifact_url = (
        "https://github.com/hcoona/three/actions/runs/424242/artifacts/987654"
    )
    github_summary = tmp_path / "github-step-summary.md"
    github_summary.write_bytes(b"# Prior summary\n")

    execution = _phase3_execute_workflow_run(
        tmp_path,
        _run(summary),
        {"steps.upload-reviewer.outputs.artifact-url": artifact_url},
        environment={"GITHUB_STEP_SUMMARY": str(github_summary)},
    )

    assert execution["status"] == 0, execution["output"]
    summary_bytes = github_summary.read_bytes()
    assert reviewer_bytes in summary_bytes
    assert artifact_url.encode() in summary_bytes
    assert artifact_url.encode() not in reviewer.read_bytes()


def _current_finalizer_facts(
    *,
    authority_path: str,
) -> dict[str, str]:
    record_digest = "sha256:" + ("a" * 64)
    upload_digest = "sha256:" + ("d" * 64)
    facts = {
        "inputs.target-sha": "1" * 40,
        "needs.publish-github-packages.result": "skipped",
        "needs.admit.outputs.attempt-artifact-digest": upload_digest,
        "needs.admit.outputs.attempt-artifact-id": "101",
        "needs.admit.outputs.attempt-artifact-name": "attempt-binding.json",
        "needs.admit.outputs.attempt-digest": record_digest,
        "needs.materialize-publication.outputs.approval-bundle-artifact-digest": "",
        "needs.materialize-publication.outputs.approval-bundle-artifact-id": "",
        "needs.materialize-publication.outputs.approval-bundle-artifact-name": "",
        "needs.materialize-publication.outputs.approval-bundle-artifact-url": "",
        "needs.materialize-publication.outputs.approval-bundle-digest": "",
        "needs.materialize-publication.outputs.publication-snapshot-artifact-digest": upload_digest,
        "needs.materialize-publication.outputs.publication-snapshot-artifact-id": "731",
        "needs.materialize-publication.outputs.publication-snapshot-artifact-name": "publication-snapshot.json",
        "needs.materialize-publication.outputs.publication-snapshot-artifact-url": "https://example.test/artifacts/731",
        "needs.materialize-publication.outputs.publication-snapshot-digest": record_digest,
        "needs.materialize-publication.result": "success",
        "needs.observe-github-packages.outputs.observation-set-artifact-digest": upload_digest,
        "needs.observe-github-packages.outputs.observation-set-artifact-id": "730",
        "needs.observe-github-packages.outputs.observation-set-artifact-name": "observation.json",
        "needs.observe-github-packages.outputs.observation-set-digest": record_digest,
        "needs.observe-github-packages.result": "success",
        "needs.approve-publication.outputs.publication-authorization-artifact-digest": "",
        "needs.approve-publication.outputs.publication-authorization-artifact-id": "",
        "needs.approve-publication.outputs.publication-authorization-artifact-name": "",
        "needs.approve-publication.outputs.publication-authorization-digest": "",
        "needs.prove-exact-satisfied.outputs.exact-satisfied-finalization-proof-artifact-digest": "",
        "needs.prove-exact-satisfied.outputs.exact-satisfied-finalization-proof-artifact-id": "",
        "needs.prove-exact-satisfied.outputs.exact-satisfied-finalization-proof-artifact-name": "",
        "needs.prove-exact-satisfied.outputs.exact-satisfied-finalization-proof-digest": "",
        "needs.qualification-finalizer.outputs.qualification-result": "success",
        "needs.qualification-finalizer.outputs.qualification-snapshot-artifact-digest": upload_digest,
        "needs.qualification-finalizer.outputs.qualification-snapshot-artifact-id": "107",
        "needs.qualification-finalizer.outputs.qualification-snapshot-artifact-name": "qualification-snapshot.json",
        "needs.qualification-finalizer.outputs.qualification-snapshot-digest": record_digest,
        "needs.qualification-finalizer.outputs.decision-artifact-digest": upload_digest,
        "needs.qualification-finalizer.outputs.decision-artifact-id": "108",
        "needs.qualification-finalizer.outputs.decision-artifact-name": "qualification-decision.json",
        "needs.qualification-finalizer.outputs.decision-artifact-url": "https://github.com/hcoona/three/actions/runs/424242/artifacts/108",
        "needs.qualification-finalizer.outputs.decision-digest": record_digest,
    }
    for index, role in enumerate(
        ("intent", "repository-model", "live-eligibility"),
        start=801,
    ):
        facts.update(
            {
                f"inputs.{role}-artifact-id": str(index),
                f"inputs.{role}-artifact-digest": upload_digest,
                f"inputs.{role}-artifact-name": f"{role}.json",
                f"inputs.{role}-digest": record_digest,
            }
        )
    for index, role in enumerate(
        (
            "build-evidence",
            "project-test-evidence",
            "artifact-contents-evidence",
            "install-import-evidence",
            "release-artifact",
        ),
        start=102,
    ):
        facts.update(
            {
                f"needs.qualification-finalizer.outputs.{role}-artifact-digest": upload_digest,
                f"needs.qualification-finalizer.outputs.{role}-artifact-id": str(
                    index
                ),
                f"needs.qualification-finalizer.outputs.{role}-artifact-name": f"{role}.json",
                f"needs.qualification-finalizer.outputs.{role}-digest": record_digest,
            }
        )

    if authority_path == "action":
        facts.update(
            {
                "needs.materialize-publication.outputs.approval-bundle-artifact-digest": upload_digest,
                "needs.materialize-publication.outputs.approval-bundle-artifact-id": "732",
                "needs.materialize-publication.outputs.approval-bundle-artifact-name": "approval-bundle.json",
                "needs.materialize-publication.outputs.approval-bundle-artifact-url": "https://example.test/artifacts/732",
                "needs.materialize-publication.outputs.approval-bundle-digest": record_digest,
                "needs.approve-publication.outputs.publication-authorization-artifact-digest": upload_digest,
                "needs.approve-publication.outputs.publication-authorization-artifact-id": "733",
                "needs.approve-publication.outputs.publication-authorization-artifact-name": "publication-authorization.json",
                "needs.approve-publication.outputs.publication-authorization-digest": record_digest,
            }
        )
    else:
        assert authority_path == "exact-satisfied"
        facts.update(
            {
                "needs.prove-exact-satisfied.outputs.exact-satisfied-finalization-proof-artifact-digest": upload_digest,
                "needs.prove-exact-satisfied.outputs.exact-satisfied-finalization-proof-artifact-id": "734",
                "needs.prove-exact-satisfied.outputs.exact-satisfied-finalization-proof-artifact-name": "exact-satisfied-proof.json",
                "needs.prove-exact-satisfied.outputs.exact-satisfied-finalization-proof-digest": record_digest,
            }
        )

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
    assert set(facts) == expressions
    return facts


@pytest.mark.parametrize(
    ("authority_path", "required_roles", "forbidden_roles"),
    [
        pytest.param(
            "action",
            (
                "--approval-bundle",
                "--publication-authorization",
                "--observation",
            ),
            ("--exact-satisfied-finalization-proof",),
            id="action",
        ),
        pytest.param(
            "exact-satisfied",
            ("--exact-satisfied-finalization-proof", "--observation"),
            (
                "--approval-bundle",
                "--publication-authorization",
            ),
            id="exact-satisfied",
        ),
    ],
)
def test_finalizer_shell_selects_only_completed_authority_branch(
    tmp_path: Path,
    authority_path: str,
    required_roles: tuple[str, ...],
    forbidden_roles: tuple[str, ...],
) -> None:
    execution = _phase2_execute_finalizer_shell(
        tmp_path,
        _current_finalizer_facts(authority_path=authority_path),
    )

    argv = _phase2_assert_successful_finalizer(execution)
    assert argv.count("--publication-snapshot") == 1
    for role in required_roles:
        assert argv.count(role) == 1
    for role in forbidden_roles:
        assert role not in argv


def _current_missing_authority_facts(
    *,
    snapshot_shape: str,
) -> dict[str, str]:
    source_path = (
        "action" if snapshot_shape == "action-bearing" else "exact-satisfied"
    )
    facts = _current_finalizer_facts(authority_path=source_path)
    for producer, role in (
        ("materialize-publication", "approval-bundle"),
        ("approve-publication", "publication-authorization"),
        ("prove-exact-satisfied", "exact-satisfied-finalization-proof"),
    ):
        for suffix in (
            "artifact-digest",
            "artifact-id",
            "artifact-name",
            "digest",
        ):
            facts[f"needs.{producer}.outputs.{role}-{suffix}"] = ""
    facts[
        "needs.materialize-publication.outputs."
        "publication-snapshot-artifact-name"
    ] = f"{snapshot_shape}-publication-snapshot.json"
    return facts


def _execute_current_missing_authority_finalizer(
    tmp_path: Path,
    *,
    snapshot_shape: str,
) -> dict[str, Any]:
    import os  # noqa: PLC0415
    import subprocess  # noqa: PLC0415

    facts = _current_missing_authority_facts(
        snapshot_shape=snapshot_shape,
    )
    run = _phase2_render_finalizer_run(facts)
    input_directory = tmp_path / ".wdv3/input"
    input_directory.mkdir(parents=True)
    snapshot_name = facts[
        "needs.materialize-publication.outputs."
        "publication-snapshot-artifact-name"
    ]
    for expression, value in facts.items():
        if (
            expression.endswith("-artifact-name")
            and value
            and value != snapshot_name
        ):
            (input_directory / value).write_text("{}\n", encoding="utf-8")
    action_count = 1 if snapshot_shape == "action-bearing" else 0
    (input_directory / snapshot_name).write_text(
        json.dumps(
            {
                "schema": "workflow-delivery/v3/publication-snapshot",
                "materialized-actions": [
                    {"action-id": "publish-github-packages"}
                    for _ in range(action_count)
                ],
            }
        )
        + "\n",
        encoding="utf-8",
    )

    bin_directory = tmp_path / "bin"
    bin_directory.mkdir()
    invocations = tmp_path / "current-finalizer-invocations.jsonl"
    uv = bin_directory / "uv"
    uv.write_text(
        r"""#!/usr/bin/env python3
import json
import os
import pathlib
import sys

args = sys.argv[1:]
invocations = pathlib.Path(os.environ["CURRENT_FINALIZER_INVOCATIONS"])
with invocations.open("a", encoding="utf-8") as handle:
    handle.write(json.dumps(args) + "\n")

snapshot_path = pathlib.Path(args[args.index("--publication-snapshot") + 1])
snapshot = json.loads(snapshot_path.read_text(encoding="utf-8"))
action_count = len(snapshot["materialized-actions"])
if action_count:
    result = "incomplete"
    phase = "approval-contract"
    uncertainty = True
    next_action = "new-attempt"
else:
    result = "replayable-no-side-effect"
    phase = "pre-authorization-termination"
    uncertainty = False
    next_action = "replay"

outcome_path = pathlib.Path(args[args.index("--outcome-output") + 1])
outcome_path.parent.mkdir(parents=True, exist_ok=True)
outcome_path.write_text(
    json.dumps(
        {
            "terminal-phase": phase,
            "result": result,
            "uncertainty": uncertainty,
            "possibly-mutated": False,
            "next-action": next_action,
        },
        sort_keys=True,
    )
    + "\n",
    encoding="utf-8",
)
summary_path = pathlib.Path(args[args.index("--summary-output") + 1])
summary_path.write_text(
    f"# Attempt summary\n\n- Result: {result}\n",
    encoding="utf-8",
)
github_output = pathlib.Path(args[args.index("--github-output") + 1])
with github_output.open("a", encoding="utf-8") as handle:
    handle.write(f"result={result}\n")
raise SystemExit(1)
""",
        encoding="utf-8",
    )
    uv.chmod(0o755)

    github_output = tmp_path / "current-finalizer-output.txt"
    github_summary = tmp_path / "current-finalizer-summary.md"
    environment = os.environ | {
        "CURRENT_FINALIZER_INVOCATIONS": str(invocations),
        "GITHUB_OUTPUT": str(github_output),
        "GITHUB_RUN_ATTEMPT": "1",
        "GITHUB_RUN_ID": "424242",
        "GITHUB_STEP_SUMMARY": str(github_summary),
        "PATH": f"{bin_directory}{os.pathsep}{os.environ['PATH']}",
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
    final_attempt = tmp_path / ".wdv3/final-attempt"
    return {
        "github_output": output_values,
        "invocations": tuple(
            tuple(json.loads(line))
            for line in invocations.read_text(encoding="utf-8").splitlines()
        ),
        "outcome": final_attempt / output_values["outcome-artifact-name"],
        "output": completed.stdout + completed.stderr,
        "status": completed.returncode,
        "summary": final_attempt / output_values["summary-artifact-name"],
    }


@pytest.mark.parametrize(
    (
        "snapshot_shape",
        "expected_result",
        "expected_phase",
        "expected_uncertainty",
        "expected_next_action",
    ),
    [
        pytest.param(
            "action-bearing",
            "incomplete",
            "approval-contract",
            True,
            "new-attempt",
            id="action-bearing-missing-authority",
        ),
        pytest.param(
            "actionless",
            "replayable-no-side-effect",
            "pre-authorization-termination",
            False,
            "replay",
            id="actionless-missing-proof",
        ),
    ],
)
def test_finalizer_missing_authority_uses_current_safe_result(  # noqa: PLR0913
    tmp_path: Path,
    snapshot_shape: str,
    expected_result: str,
    expected_phase: str,
    expected_uncertainty: bool,  # noqa: FBT001
    expected_next_action: str,
) -> None:
    execution = _execute_current_missing_authority_finalizer(
        tmp_path,
        snapshot_shape=snapshot_shape,
    )

    assert execution["status"] == 1, execution["output"]
    assert len(execution["invocations"]) == 1
    argv = execution["invocations"][0]
    assert argv.count("--publication-snapshot") == 1
    assert {
        "--approval-bundle",
        "--publication-authorization",
        "--exact-satisfied-finalization-proof",
    }.isdisjoint(argv)
    outcome = json.loads(execution["outcome"].read_text(encoding="utf-8"))
    assert outcome == {
        "next-action": expected_next_action,
        "possibly-mutated": False,
        "result": expected_result,
        "terminal-phase": expected_phase,
        "uncertainty": expected_uncertainty,
    }
    assert execution["github_output"]["result"] == expected_result
    assert execution["github_output"]["finalizer-status"] == "1"
    assert f"- Result: {expected_result}\n" in execution["summary"].read_text(
        encoding="utf-8"
    )
