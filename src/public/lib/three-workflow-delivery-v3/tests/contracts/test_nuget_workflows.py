"""Ordinary NuGet permissions, transfers and native command glue."""

from __future__ import annotations

# ruff: noqa: D103, S603
import json
import os
import shutil
import subprocess
from pathlib import Path

import pytest
import yaml

ROOT = Path(__file__).resolve().parents[6]
CALLER = ".github/workflows/workflow-delivery-v3-nuget-buddy-smoke.yml"
CALLEE = ".github/workflows/workflow-delivery-v3-nuget-live-attempt.yml"
SHA = "a" * 40
RETENTION_DAYS = 45
UPLOAD_COUNT = 5
DOWNLOAD_COUNT = 8


@pytest.fixture(scope="module")
def workflows():
    return tuple(
        yaml.safe_load((ROOT / path).read_text(encoding="utf-8"))
        for path in (CALLER, CALLEE)
    )


def _steps(workflow):
    return [
        step
        for job in workflow["jobs"].values()
        for step in job.get("steps", [])
    ]


def _step(workflow, name):
    return next(step for step in _steps(workflow) if step["name"] == name)


def _pwsh(root, source, env=None):
    executable = shutil.which("pwsh")
    assert executable, "The repository PowerShell toolchain is required"
    script = root / "scenario.ps1"
    script.write_text(
        "$ErrorActionPreference = 'Stop'\n"
        + source
        + "\nif (Test-Path -LiteralPath variable:\\LASTEXITCODE) "
        "{ exit $LASTEXITCODE }\n",
        encoding="utf-8",
    )
    return subprocess.run(
        [
            executable,
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-File",
            str(script),
        ],
        cwd=root,
        env={**os.environ, **(env or {})},
        check=False,
        capture_output=True,
        text=True,
        timeout=30,
    )


def _source_env(root):
    return {
        "RUNNER_TEMP": str(root),
        "SELECTED_REVISION": SHA,
        "SELECTED_REF": "refs/heads/development",
        "DRY_RUN": "false",
        "GITHUB_SHA": SHA,
        "GITHUB_REF": "refs/heads/development",
        "GITHUB_REPOSITORY": "hcoona/three",
        "GITHUB_RUN_ID": "123",
        "GITHUB_RUN_ATTEMPT": "1",
        "GITHUB_OUTPUT": str(root / "output"),
        "GITHUB_STEP_SUMMARY": str(root / "summary"),
    }


def _assert_success(result):
    assert result.returncode == 0, result.stdout + result.stderr


def test_nuget_single_concurrency_owner_and_four_reusable_jobs(workflows):
    caller, callee = workflows
    assert (
        caller["on"]["workflow_dispatch"]["inputs"]["dry-run"]["default"]
        is True
    )
    assert caller["jobs"]["release"]["uses"] == "./" + CALLEE
    assert caller["concurrency"] == {
        "group": (
            "workflow-delivery-nuget.pkg.github.com-hcoona-"
            "hcoona.releasesmoke.githubpackages"
        ),
        "cancel-in-progress": False,
    }
    assert "concurrency" not in callee
    assert set(callee["jobs"]) == {
        "prepare",
        "observe",
        "publish",
        "verify-result",
    }
    assert all("concurrency" not in job for job in callee["jobs"].values())


def test_nuget_environment_and_write_capability_only_on_publisher(workflows):
    for name, job in workflows[1]["jobs"].items():
        assert job["runs-on"] == "windows-2022"
        assert "continue-on-error" not in job
        assert not any("continue-on-error" in step for step in job["steps"])
        assert job["permissions"]["contents"] == "read"
        if name == "publish":
            assert job["environment"] == "workflow-delivery-v3-buddy-approval"
            assert job["permissions"]["packages"] == "write"
            assert "inputs.dry-run == false" in job["if"]
            assert "write-needed == 'true'" in job["if"]
        else:
            assert "environment" not in job
            assert job["permissions"].get("packages") == (
                None if name == "prepare" else "read"
            )


@pytest.mark.parametrize(
    ("changes", "accepted"),
    [
        ({}, True),
        ({"GITHUB_RUN_ATTEMPT": "2"}, False),
        ({"GITHUB_RUN_ATTEMPT": "2", "DRY_RUN": "true"}, True),
        ({"SELECTED_REVISION": "b" * 40}, False),
        ({"SELECTED_REF": "refs/heads/main"}, False),
        ({"GITHUB_REPOSITORY": "other/three"}, False),
    ],
)
def test_nuget_development_refs_and_dry_reruns(
    workflows, tmp_path, changes, accepted
):
    result = _pwsh(
        tmp_path,
        _step(workflows[1], "Validate selected Buddy request")["run"],
        {**_source_env(tmp_path), **changes},
    )
    assert (result.returncode == 0) is accepted, result.stdout + result.stderr


def test_nuget_control_is_transferred_before_product_execution(workflows):
    names = [step["name"] for step in workflows[1]["jobs"]["prepare"]["steps"]]
    assert (
        names.index("Build complete control before product execution")
        < names.index("Retain actual control results")
        < names.index("Qualify complete selected NuGet release")
    )
    for name in ("observe", "publish", "verify-result"):
        job = workflows[1]["jobs"][name]
        source = "\n".join(step.get("run", "") for step in job["steps"])
        assert "actions/checkout" not in json.dumps(job)
        assert "dotnet build " not in source
        assert "prepare-nuget" not in source
    preparation = _step(workflows[1], "Qualify complete selected NuGet release")
    assert "GITHUB_TOKEN" not in preparation.get("env", {})
    assert "release prepare-nuget" in preparation["run"]


def test_nuget_transfers_are_immutable_and_retained(workflows):
    uploads = downloads = 0
    for step in _steps(workflows[1]):
        action = step.get("uses", "")
        if action.startswith("actions/upload-artifact@"):
            uploads += 1
            assert step["with"]["overwrite"] is False
            assert step["with"]["retention-days"] == RETENTION_DAYS
            assert step["with"]["if-no-files-found"] == "error"
        elif action.startswith("actions/download-artifact@"):
            downloads += 1
            assert step["with"]["artifact-ids"]
            assert step["with"]["digest-mismatch"] == "error"
            assert (
                not {"name", "pattern", "run-id", "repository"}
                & step["with"].keys()
            )
    assert uploads == UPLOAD_COUNT
    assert downloads == DOWNLOAD_COUNT


@pytest.mark.parametrize(
    "mutation",
    [
        None,
        "revision",
        "selectedRef",
        "run",
        "attempt",
        "unit",
        "dryRun",
        "extra",
        "subject-revision",
        "subject-selectedRef",
        "subject-unit",
        "subject-dryRun",
        "subject-channel",
        "subject-destination",
        "subject-declaration",
    ],
)
def test_nuget_receiving_jobs_bind_actual_producer_and_subject(
    workflows, tmp_path, mutation
):
    producer = {
        "revision": SHA,
        "selectedRef": "refs/heads/development",
        "run": "123",
        "attempt": "1",
        "unit": "hcoona-release-smoke-github-packages",
        "dryRun": False,
    }
    for kind in ("control", "prepared"):
        directory = tmp_path / kind
        directory.mkdir()
        actual = dict(producer)
        if (
            kind == "prepared"
            and mutation
            and not mutation.startswith("subject-")
        ):
            actual[mutation] = True if mutation == "dryRun" else "changed"
        (directory / "producer.json").write_text(
            json.dumps(actual), encoding="utf-8"
        )
    subject = {
        "revision": SHA,
        "selectedRef": "refs/heads/development",
        "dryRun": False,
        "unit": producer["unit"],
        "channel": "buddy",
        "destination": "github-packages-nuget",
        "declaration": (
            "src/public/lib/hcoona-release-smoke-github-packages/"
            "workflow-delivery.release-unit.yml"
        ),
    }
    if mutation and mutation.startswith("subject-"):
        key = mutation.removeprefix("subject-")
        subject[key] = True if key == "dryRun" else "changed"
    (tmp_path / "prepared/preparation.json").write_text(
        json.dumps({"prepared": {"subject": subject}}), encoding="utf-8"
    )
    result = _pwsh(
        tmp_path,
        _step(workflows[1], "Validate transferred source, mode and producer")[
            "run"
        ],
        _source_env(tmp_path),
    )
    assert (result.returncode == 0) is (mutation is None), (
        result.stdout + result.stderr
    )


@pytest.mark.parametrize("state", ["exact", "absent"])
@pytest.mark.parametrize("dry_run", [True, False])
def test_nuget_state_description_separates_hypothetical_writes(
    workflows, tmp_path, state, dry_run
):
    directory = tmp_path / "observation"
    directory.mkdir()
    (directory / "observation.json").write_text(
        json.dumps({"observation": {"files": [{"state": state}]}}),
        encoding="utf-8",
    )
    boundary = """
function mise {
  Add-Content -LiteralPath "$env:RUNNER_TEMP/calls" -Value 'describe'
  '{"ready":true}'
  $global:LASTEXITCODE = 0
}
"""
    result = _pwsh(
        tmp_path,
        boundary
        + _step(workflows[1], "Describe native state and dry-run writes")[
            "run"
        ],
        {**_source_env(tmp_path), "DRY_RUN": str(dry_run).lower()},
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert (tmp_path / "output").read_text().strip() == (
        "write-needed=" + str(state == "absent").lower()
    )
    assert (tmp_path / "calls").exists() is dry_run
    assert (directory / "dry-run.json").exists() is dry_run
    if dry_run:
        assert (
            "no publication or live completion claim"
            in (tmp_path / "summary").read_text()
        )


def test_nuget_verification_requires_actual_platform_predecessors(workflows):
    condition = workflows[1]["jobs"]["verify-result"]["if"]
    for expected in (
        "inputs.dry-run == false",
        "needs.prepare.result == 'success'",
        "needs.observe.result == 'success'",
        "needs.publish.result == 'success'",
        (
            "needs.observe.outputs.write-needed == 'false' && "
            "needs.publish.result == 'skipped'"
        ),
    ):
        assert expected in condition
    assert "failure" not in condition
    source = _step(
        workflows[1], "Verify original remote bytes and native scoped consumer"
    )["run"]
    assert '"$env:RUNNER_TEMP/observation/observation.json"' in source
    assert "$publication = '-'" in source
    assert source.index("release consume-github-nuget") < source.index(
        "release complete-github-nuget"
    )


@pytest.mark.parametrize(
    ("status", "payload", "completion", "expected"),
    [
        (0, '{"check":{"status":"passed"}}', True, 0),
        (1, '{"check":{"status":"failed"}}', True, 1),
        (1, '{"check":{"status":"cancelled"}}', True, 1),
        (2, "{}", False, 2),
        (1, "{", False, 1),
        (0, '{"check":{"status":"failed"}}', False, 1),
    ],
)
def test_nuget_handled_consumer_failure_reaches_truthful_completion(  # noqa: PLR0913, PLR0917
    workflows,
    tmp_path,
    status,
    payload,
    completion,
    expected,
):
    boundary = """
function mise {
  $command = $args[([array]::IndexOf($args, 'release') + 1)]
  Add-Content -LiteralPath "$env:RUNNER_TEMP/calls" -Value $command
  $global:LASTEXITCODE = 0
  switch ($command) {
    'consume-github-nuget' {
      $env:PAYLOAD; $global:LASTEXITCODE = [int]$env:STATUS
    }
    'complete-github-nuget' { '{}'; $global:LASTEXITCODE = [int]$env:STATUS }
    default { '{}' }
  }
}
"""
    source = _step(
        workflows[1], "Verify original remote bytes and native scoped consumer"
    )["run"]
    result = _pwsh(
        tmp_path,
        boundary + source,
        {
            **_source_env(tmp_path),
            "STATUS": str(status),
            "PAYLOAD": payload,
            "WRITE_NEEDED": "false",
        },
    )
    calls = (tmp_path / "calls").read_text().splitlines()
    assert ("complete-github-nuget" in calls) is completion, (
        result.stdout + result.stderr
    )
    assert result.returncode == expected, result.stdout + result.stderr


def test_nuget_workflow_scripts_parse_in_powershell(workflows, tmp_path):
    scripts = [step["run"] for step in _steps(workflows[1]) if "run" in step]
    (tmp_path / "scripts.json").write_text(
        json.dumps(scripts), encoding="utf-8"
    )
    result = _pwsh(
        tmp_path,
        """
foreach ($source in (Get-Content scripts.json -Raw | ConvertFrom-Json)) {
  $tokens = $null; $errors = $null
  [void][System.Management.Automation.Language.Parser]::ParseInput(
    $source, [ref]$tokens, [ref]$errors)
  if ($errors.Count) { throw ($errors | Out-String) }
}
""",
    )
    assert result.returncode == 0, result.stdout + result.stderr
