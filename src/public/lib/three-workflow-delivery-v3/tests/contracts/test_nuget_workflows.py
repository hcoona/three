"""Native workflow authority and glue, without destination effects."""

from __future__ import annotations

# ruff: noqa: D103, S603, SLF001
import hashlib
import json
import os
import re
import shutil
import subprocess
import zipfile
from pathlib import Path

import pytest
import yaml
from three_workflow_delivery_v3 import cli
from three_workflow_delivery_v3.adapters.nuget_github_packages import (
    NUGET_PYTHON_VERSION,
)

ROOT = Path(__file__).resolve().parents[6]
CALLER_PATH = ".github/workflows/workflow-delivery-v3-nuget-buddy-smoke.yml"
CALLEE_PATH = ".github/workflows/workflow-delivery-v3-nuget-live-attempt.yml"
SHA = "a" * 40
RETENTION_DAYS = 45
INVOCATION = (
    "uv run --python 3.13.12 --package $env:WDV3_PACKAGE "
    "three-workflow-delivery-v3 @arguments"
)


@pytest.fixture(scope="module")
def workflows():
    return tuple(
        yaml.safe_load((ROOT / path).read_text(encoding="utf-8"))
        for path in (CALLER_PATH, CALLEE_PATH)
    )


def _steps(workflow):
    return [
        (job, step)
        for job in workflow["jobs"].values()
        for step in job.get("steps", [])
    ]


def _command(workflow, command):
    tokens = "\n".join(f"  '{part}'," for part in command.split())
    matches = [
        (job, step)
        for job, step in _steps(workflow)
        if tokens in step.get("run", "")
    ]
    assert len(matches) == 1, command
    return matches[0]


def _step_id(job, identity):
    return next(step for step in job["steps"] if step.get("id") == identity)


def _pwsh(root, source, env=None):
    executable = shutil.which("pwsh")
    assert executable, "The repository's PowerShell toolchain is required"
    script = root / "scenario.ps1"
    script.write_text(
        "$ErrorActionPreference = 'Stop'\n" + source, encoding="utf-8"
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


def _assert_success(result):
    assert result.returncode == 0, result.stdout + result.stderr


def test_nuget_workflow_entry_requires_current_protected_main(
    workflows, tmp_path
):
    caller, callee = workflows
    assert caller["on"] == {"workflow_dispatch": None}
    assert set(callee["on"]) == {"workflow_call"}
    invoke = next(job for job in caller["jobs"].values() if "uses" in job)
    assert invoke["uses"] == "./" + CALLEE_PATH
    assert "steps" not in invoke
    assert "live-result == 'admitted'" in invoke["if"]
    request_job, request = _command(
        caller, "release nuget normalize-live-request"
    )
    assert "'--selected-ref',\n  $env:GITHUB_REF" in request["run"]
    assert request_job["permissions"] == {"contents": "read"}
    admit = callee["jobs"]["admit"]
    guard = _step_id(admit, "identity")
    assert admit["steps"][0] is guard
    expected = {
        "GITHUB_REPOSITORY": "hcoona/three",
        "GITHUB_ACTOR": "hcoona",
        "GITHUB_REF": "refs/heads/main",
        "GITHUB_SHA": SHA,
        "WDV3_TARGET": SHA,
        "WDV3_SELECTED_REF": "refs/heads/main",
        "WDV3_WORKFLOW_SHA": SHA,
        "WDV3_WORKFLOW_REF": f"hcoona/three/{CALLER_PATH}@refs/heads/main",
    }
    cases = [expected]
    for key, value in (
        ("GITHUB_REPOSITORY", "outsider/three"),
        ("GITHUB_ACTOR", "outsider"),
        ("GITHUB_REF", "refs/heads/topic"),
        ("WDV3_SELECTED_REF", "refs/heads/topic"),
        ("WDV3_TARGET", "b" * 40),
        ("WDV3_WORKFLOW_SHA", "b" * 40),
        ("WDV3_WORKFLOW_REF", "hcoona/three/npm.yml@refs/heads/main"),
    ):
        cases.append({**expected, key: value})
    (tmp_path / "cases.json").write_text(json.dumps(cases), encoding="utf-8")
    # The real guard has no native command. Catch each rejected identity in
    # one process; the output file establishes which case was admitted.
    source = (
        """
$results = foreach ($case in (Get-Content cases.json -Raw | ConvertFrom-Json)) {
  foreach ($property in $case.PSObject.Properties) {
    [Environment]::SetEnvironmentVariable($property.Name, $property.Value)
  }
  $env:GITHUB_OUTPUT = Join-Path $PWD 'guard-output'
  try {
"""
        + guard["run"]
        + """
    $true
  } catch { $false }
}
ConvertTo-Json -InputObject @($results) -Compress
"""
    )
    result = _pwsh(tmp_path, source)
    _assert_success(result)
    assert json.loads(result.stdout) == [True] + [False] * (len(cases) - 1)
    assert (tmp_path / "guard-output").read_text().splitlines() == [
        "identity-admitted=true"
    ]


def test_nuget_authority_jobs_reject_reruns(workflows):
    for workflow in workflows:
        for job in workflow["jobs"].values():
            assert "github.run_attempt == 1" in job["if"]
            assert "||" not in job["if"]


def test_nuget_workflow_uses_pinned_native_python(workflows):
    invocations = []
    for workflow in workflows:
        for _, step in _steps(workflow):
            source = step.get("run", "")
            if "three-workflow-delivery-v3 @arguments" in source:
                invocations.append(source)
                assert f"--python {NUGET_PYTHON_VERSION} " in source
                assert INVOCATION in source
    assert invocations


@pytest.mark.parametrize(
    ("status", "decision", "expected"),
    [
        (0, "pass", 0),
        (1, "blocked", 0),
        (0, "blocked", 1),
        (1, "pass", 1),
        (2, "blocked", 2),
        (1, None, 1),
    ],
)
def test_nuget_blocked_eligibility_retains_decision_and_fails(
    workflows, tmp_path, status, decision, expected
):
    job, step = _command(
        workflows[0], "release nuget evaluate-live-eligibility"
    )
    output = tmp_path / "github-output"
    (tmp_path / ".wdv3").mkdir()
    if decision is not None:
        (tmp_path / ".wdv3/eligibility.json").write_text(
            json.dumps({"result": decision}), encoding="utf-8"
        )
    # Replace the external command at its boundary; execute the actual status
    # and Decision handling. No native service or target evaluation occurs.
    assert step["run"].count(INVOCATION) == 1
    source = step["run"].replace(INVOCATION, f"$global:LASTEXITCODE = {status}")
    result = _pwsh(tmp_path, source, {"GITHUB_OUTPUT": str(output)})
    assert result.returncode == expected, result.stdout + result.stderr
    upload = _step_id(job, "upload-eligibility")
    assert "if" not in upload  # Expected blocked status must reach upload.
    propagate = job["steps"][-1]
    assert propagate["if"] == "always()"
    if expected == 0:
        assert output.read_text().strip() == f"eligibility-status={status}"
        result = _pwsh(
            tmp_path,
            propagate["run"],
            {
                "WDV3_EVALUATION_OUTCOME": "success",
                "WDV3_UPLOAD_OUTCOME": "success",
                "WDV3_ELIGIBILITY_STATUS": str(status),
            },
        )
        assert result.returncode == status
    else:
        assert not output.exists()


def test_nuget_workflows_confine_publication_authority(workflows):
    writes = []
    for workflow in workflows:
        assert workflow["permissions"] == {}
        for name, job in workflow["jobs"].items():
            permissions = job["permissions"]
            assert permissions.get("id-token", "none") == "none"
            assert permissions["contents"] == "read"
            if permissions.get("packages") == "write":
                writes.append((name, job))
                if "uses" in job:
                    assert "steps" not in job
                else:
                    assert (
                        _command(workflow, "release nuget execute-publication")[
                            0
                        ]
                        is job
                    )
            for step in job.get("steps", []):
                assert "secrets." not in json.dumps(step)
    assert [name for name, _ in writes] == [
        "run-live-attempt",
        "publish-github-packages",
    ]


@pytest.mark.parametrize(
    ("evaluation", "upload", "status"),
    [
        ("failure", "skipped", ""),
        ("success", "failure", "0"),
        ("success", "success", ""),
    ],
)
def test_nuget_eligibility_rejects_failed_or_missing_status(
    workflows, tmp_path, evaluation, upload, status
):
    job, _ = _command(workflows[0], "release nuget evaluate-live-eligibility")
    result = _pwsh(
        tmp_path,
        job["steps"][-1]["run"],
        {
            "WDV3_EVALUATION_OUTCOME": evaluation,
            "WDV3_UPLOAD_OUTCOME": upload,
            "WDV3_ELIGIBILITY_STATUS": status,
        },
    )
    assert result.returncode != 0
    assert "NuGet Eligibility" in result.stderr


def test_nuget_target_execution_requires_windows_without_publication_permission(
    workflows,
):
    caller, callee = workflows
    for workflow, command in (
        (caller, "repository provide-dotnet"),
        (callee, "release nuget run-build"),
        (callee, "release nuget artifact-contents"),
        (callee, "release nuget restore-build-invoke"),
    ):
        job, _ = _command(workflow, command)
        assert job["runs-on"].startswith("windows-")
        assert job["permissions"].get("packages", "none") == "none"
        assert not any(
            "GITHUB_TOKEN" in step.get("env", {}) for step in job["steps"]
        )


def test_nuget_workflows_transport_prebuilt_helper(workflows):
    caller, callee = workflows
    helper_builds = [
        (job, step)
        for job, step in _steps(caller)
        if "dotnet build 'src/private/app/workflow-delivery-v3-dotnet-provider/"
        in step.get("run", "")
    ]
    assert len(helper_builds) == 1
    job, build = helper_builds[0]
    assert job["permissions"] == {"contents": "read"}
    assert "Compress-Archive" in build["run"]
    assert "bin/Release/net10.0/*'" in build["run"]
    for workflow in (caller, callee):
        for job, step in _steps(workflow):
            if "'--helper-dll'" not in step.get("run", ""):
                continue
            readers = [
                s for s in job["steps"] if "Expand-Archive" in s.get("run", "")
            ]
            assert len(readers) == 1
            reader = readers[0]
            assert "Get-FileHash" in reader["run"]
            assert "throw 'Trusted helper digest mismatch'" in reader["run"]
            assert "runtimeconfig.json" in reader["run"]
            assert "deps.json" in reader["run"]
            assert job["steps"].index(reader) < job["steps"].index(step)
            assert "helper-digest" in reader["env"]["WDV3_HELPER_DIGEST"]


@pytest.mark.parametrize("valid_digest", [True, False])
def test_nuget_helper_transport_rejects_changed_archive(
    workflows, tmp_path, valid_digest
):
    job, _ = _command(workflows[1], "release nuget prepare-publication")
    reader = next(
        s for s in job["steps"] if "Expand-Archive" in s.get("run", "")
    )
    archive = tmp_path / "helper.zip"
    files = {
        "WorkflowDeliveryV3DotnetProvider.dll": b"modeled-helper-bytes",
        "WorkflowDeliveryV3DotnetProvider.deps.json": b'{"modeled":true}',
        "WorkflowDeliveryV3DotnetProvider.runtimeconfig.json": b"{}",
        "dependency.dll": b"modeled-dependency-bytes",
    }
    with zipfile.ZipFile(archive, "w") as bundle:
        for name, content in files.items():
            bundle.writestr(name, content)
    digest = hashlib.sha256(archive.read_bytes()).hexdigest()
    result = _pwsh(
        tmp_path,
        reader["run"],
        {
            "WDV3_HELPER_ARCHIVE": str(archive),
            "WDV3_HELPER_DIGEST": "sha256:"
            + (digest if valid_digest else "0" * 64),
        },
    )
    extracted = tmp_path / ".wdv3/helper"
    if valid_digest:
        _assert_success(result)
        assert {p.name: p.read_bytes() for p in extracted.iterdir()} == files
    else:
        assert result.returncode != 0
        assert "Trusted helper digest mismatch" in result.stderr
        assert not extracted.exists()


def test_nuget_decision_and_publication_jobs_do_not_build_targets(workflows):
    target_commands = {"provide-dotnet", "run-build", "restore-build-invoke"}
    for workflow in workflows:
        for job in workflow["jobs"].values():
            scripts = "\n".join(s.get("run", "") for s in job.get("steps", []))
            if any(f"  '{command}'," in scripts for command in target_commands):
                continue
            # Only the dedicated unprivileged helper producer may run build.
            if "Compress-Archive" in scripts:
                assert job["permissions"] == {"contents": "read"}
                continue
            assert not re.search(
                r"(?im)^\s*dotnet\s+(build|pack|restore|msbuild)\b", scripts
            )
            assert "nbgv" not in scripts.lower()


def test_nuget_workflows_bind_current_immutable_records(workflows):
    uploads = downloads = 0
    for workflow in workflows:
        for _, step in _steps(workflow):
            action = step.get("uses", "")
            if action.startswith("actions/upload-artifact@"):
                uploads += 1
                settings = step["with"]
                assert settings["archive"] is False
                assert settings["overwrite"] is False
                assert settings["retention-days"] == RETENTION_DAYS
                assert settings["if-no-files-found"] == "error"
            if action.startswith("actions/download-artifact@"):
                downloads += 1
                settings = step["with"]
                assert settings["artifact-ids"]
                assert settings["skip-decompress"] is True
                assert settings["digest-mismatch"] == "error"
                assert (
                    not {"name", "pattern", "run-id", "repository"}
                    & settings.keys()
                )
            if action.startswith("actions/checkout@"):
                assert step["with"] == {
                    "ref": "${{ github.sha }}",
                    "fetch-depth": 0,
                    "persist-credentials": False,
                }
    assert uploads
    assert downloads


def test_nuget_workflows_preserve_original_package_and_three_evidence_records(
    workflows, tmp_path
):
    callee = workflows[1]
    build, _ = _command(callee, "release nuget run-build")
    seal = _step_id(build, "seal-package")
    original = bytes(range(256)) * 3
    source = tmp_path / "original.nupkg"
    source.write_bytes(original)
    output = tmp_path / "github-output"
    result = _pwsh(
        tmp_path,
        seal["run"],
        {
            "WDV3_PAYLOAD": str(source),
            "WDV3_FIXED_NAME": "planned-package.nupkg",
            "GITHUB_OUTPUT": str(output),
        },
    )
    _assert_success(result)
    assert (
        tmp_path / ".wdv3/upload/planned-package.nupkg"
    ).read_bytes() == original
    fields = dict(
        line.split("=", 1) for line in output.read_text().splitlines()
    )
    assert fields["name"] == "planned-package.nupkg"
    assert fields["digest"] == "sha256:" + hashlib.sha256(original).hexdigest()
    for command in ("finalize-qualification", "finalize-live"):
        _, step = _command(callee, "release nuget " + command)
        for role in (
            "build-evidence",
            "artifact-contents-evidence",
            "consumer-evidence",
        ):
            assert f"'--{role}'" in step["run"]
            assert f"'--{role}-artifact-id'" in step["run"]
            assert f"'--{role}-artifact-digest'" in step["run"]
            assert f"'--{role}-digest'" in step["run"]


def test_nuget_approval_and_proof_follow_action_count(workflows):
    callee = workflows[1]
    approval, _ = _command(
        callee, "release nuget form-publication-authorization"
    )
    assert "publish-required == 'true'" in approval["if"]
    assert (
        approval["environment"]["name"] == "workflow-delivery-v3-buddy-approval"
    )
    assert "reviewer-summary-url" in approval["environment"]["url"]
    assert "WDV3_APPROVAL_ENVIRONMENT_MARKER" in json.dumps(
        approval["steps"][0]
    )
    proof, step = _command(callee, "release nuget prove-exact-satisfied")
    assert "always()" in proof["if"]
    assert "publish-required == 'false'" in proof["if"]
    assert "needs.publish-github-packages.result == 'skipped'" in proof["if"]
    assert (
        step["env"]["WDV3_ARG_PUBLISHER_CONCLUSION"]
        == "${{ needs.publish-github-packages.result }}"
    )
    assert proof["permissions"]["packages"] == "read"


def test_nuget_publication_requires_admitted_uploaded_marker(workflows):
    publish, execute = _command(
        workflows[1], "release nuget execute-publication"
    )
    steps = publish["steps"]
    previous = -1
    for identity in (
        "prepare",
        "upload-mutation-marker",
        "download-mutation-marker",
        "admit-marker",
        "publish",
    ):
        step = _step_id(publish, identity)
        position = steps.index(step)
        assert previous < position
        assert (
            "if" not in step
        )  # Default success() cannot bypass predecessor failure.
        previous = position
    download = _step_id(publish, "download-mutation-marker")
    assert (
        download["with"]["artifact-ids"]
        == "${{ steps.upload-mutation-marker.outputs.artifact-id }}"
    )
    assert (
        execute["env"]["WDV3_ARG_PUBLICATION_TERMINAL_REFERENCE"]
        == "${{ steps.admit-marker.outputs.publication-terminal-reference }}"
    )
    assert (
        "'--terminal-directory',\n  '.wdv3/persisted-mutation-marker'"
        in execute["run"]
    )
    assert publish["concurrency"]["cancel-in-progress"] is False
    assert "resource-concurrency-key" in publish["concurrency"]["group"]
    assert (
        publish["outputs"]["publication-step-outcome"]
        == "${{ steps.publish.outcome }}"
    )


def test_nuget_finalizer_retains_failed_or_missing_terminal_evidence(workflows):
    callee = workflows[1]
    publish, _ = _command(callee, "release nuget execute-publication")
    upload = _step_id(publish, "upload-publication-result")
    assert (
        upload["if"]
        == "always() && steps.publish.outputs.publication-result-digest != ''"
    )
    assert publish["outputs"]["publication-terminal-reference"] == (
        "${{ steps.admit-result.outputs.publication-terminal-reference || "
        "steps.admit-marker.outputs.publication-terminal-reference || 'null' }}"
    )
    finalizer, step = _command(callee, "release nuget finalize-live")
    assert "always()" in finalizer["if"]
    assert "identity-admitted == 'true'" in finalizer["if"]
    assert step["env"]["WDV3_ARG_PUBLICATION_TERMINAL_REFERENCE"].endswith(
        "|| 'null' }}"
    )
    assert (
        step["env"]["WDV3_ARG_PUBLISHER_CONCLUSION"]
        == "${{ needs.publish-github-packages.result }}"
    )
    assert (
        step["env"]["WDV3_ARG_OBSERVATION_CONCLUSION"]
        == "${{ needs.observe-github-packages.result }}"
    )
    assert "'--marker-directory'" in step["run"]
    assert "'--publication-step-outcome'" in step["run"]
    for role in ("attempt-outcome", "attempt-summary"):
        assert _step_id(finalizer, "upload-" + role)["if"] == (
            "always() && steps.finalize.outputs.attempt-outcome-digest != ''"
        )


def test_nuget_workflow_scripts_parse_in_powershell(workflows, tmp_path):
    scripts = [
        step["run"] for w in workflows for _, step in _steps(w) if "run" in step
    ]
    (tmp_path / "scripts.json").write_text(
        json.dumps(scripts), encoding="utf-8"
    )
    result = _pwsh(
        tmp_path,
        """
$scripts = Get-Content scripts.json -Raw | ConvertFrom-Json
foreach ($source in $scripts) {
  $tokens = $null; $errors = $null
  [void][System.Management.Automation.Language.Parser]::ParseInput(
    $source, [ref]$tokens, [ref]$errors)
  if ($errors.Count) { throw ($errors | Out-String) }
}
$scripts.Count
""",
    )
    _assert_success(result)
    assert int(result.stdout) == len(scripts)


def _modeled_argument(key, value, *, present):
    if key.endswith(("_CONCLUSION", "SENTINEL_RESULT", "STEP_OUTCOME")):
        return "success"
    if "OPTIONAL_ID" in key or key.endswith("_PRESENT"):
        return "123" if present else ""
    if key == "WDV3_ARG_PUBLICATION_TERMINAL_REFERENCE":
        return "null"
    return re.sub(r"\$\{\{.*?\}\}", "123", value)


def test_nuget_workflow_cli_invocations_match_current_parser(
    workflows, tmp_path
):
    cases = []
    for workflow in workflows:
        for _, step in _steps(workflow):
            source = step.get("run", "")
            if INVOCATION not in source:
                continue
            for present in (False, True):
                environment = {
                    key: _modeled_argument(key, value, present=present)
                    for key, value in step.get("env", {}).items()
                }
                cases.append(
                    {"source": source.split(INVOCATION)[0], "env": environment}
                )
    assert cases
    (tmp_path / "cases.json").write_text(json.dumps(cases), encoding="utf-8")
    result = _pwsh(
        tmp_path,
        """
$env:GITHUB_RUN_ID = '123'; $env:GITHUB_RUN_ATTEMPT = '1'
$env:GITHUB_SHA = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
$env:GITHUB_REPOSITORY = 'hcoona/three'; $env:GITHUB_REF = 'refs/heads/main'
$env:GITHUB_ACTOR = 'hcoona'; $env:GITHUB_OUTPUT = 'output'
$env:GITHUB_STEP_SUMMARY = 'summary'
$results = foreach ($case in (Get-Content cases.json -Raw | ConvertFrom-Json)) {
  foreach ($property in $case.env.PSObject.Properties) {
    [Environment]::SetEnvironmentVariable($property.Name, $property.Value)
  }
  $source = $case.source + "`nWrite-Output -NoEnumerate `$arguments"
  $values = & ([scriptblock]::Create($source))
  [pscustomobject]@{ arguments = @($values) }
}
ConvertTo-Json -InputObject @($results) -Depth 8 -Compress
""",
    )
    _assert_success(result)
    captured = json.loads(result.stdout)
    assert len(captured) == len(cases)
    parser = cli._parser()
    for case in captured:
        arguments = case["arguments"]
        assert all(isinstance(value, str) and value for value in arguments)
        parsed = parser.parse_args(arguments)
        assert callable(parsed.handler)
