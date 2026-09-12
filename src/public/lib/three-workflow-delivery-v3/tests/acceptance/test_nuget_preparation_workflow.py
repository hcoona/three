"""Local Windows shell and transport contracts, without Actions execution."""

from __future__ import annotations

import hashlib
import json
import zipfile
from pathlib import Path

import pytest
import yaml
from three_workflow_delivery_v3.acceptance.nuget_preparation import (
    WORKFLOW_PATH,
)

from ..contracts.test_nuget_workflows import _assert_success, _pwsh

ROOT = Path(__file__).resolve().parents[6]
RETENTION_DAYS = 45


@pytest.fixture(scope="module")
def workflow():
    """Read the actual workflow as the contract under test."""
    return yaml.safe_load((ROOT / WORKFLOW_PATH).read_text(encoding="utf-8"))


def test_fixture_workflow_requires_protected_current_unprivileged_windows(
    workflow,
):
    """Every producer and consumer rejects unrelated identities and reruns."""
    assert set(workflow["on"]) == {"workflow_dispatch"}
    assert set(workflow["on"]["workflow_dispatch"]["inputs"]) == {
        "expected_tooling_sha",
        "target_sha",
        "generation",
    }
    assert workflow["permissions"] == {}
    for job in workflow["jobs"].values():
        assert job["runs-on"] == "windows-2022"
        assert job["permissions"] == {"contents": "read"}
        assert "environment" not in job
        for clause in (
            "github.event_name == 'workflow_dispatch'",
            "github.repository == 'hcoona/three'",
            "github.actor_id == '712433'",
            "github.ref == 'refs/heads/main'",
            "github.sha == inputs.expected_tooling_sha",
            "github.run_attempt == 1",
        ):
            assert clause in job["if"]
        assert "||" not in job["if"]
        for step in job["steps"]:
            assert not {
                "GITHUB_TOKEN",
                "GH_TOKEN",
                "NUGET_AUTH_TOKEN",
            }.intersection(step.get("env", {}))
            if step.get("uses", "").startswith("actions/checkout@"):
                assert step["with"]["persist-credentials"] is False
                assert step["with"]["fetch-depth"] == 0
                assert step["with"]["ref"] in {
                    "${{ github.sha }}",
                    "${{ inputs.target_sha }}",
                }


def test_fixture_workflow_binds_prebuilt_helper_and_immutable_inputs(workflow):
    """Setup and offline preparation consume current ID-selected raw bytes."""
    for name in ("setup", "prepare"):
        job = workflow["jobs"][name]
        downloads = [
            step
            for step in job["steps"]
            if step.get("uses", "").startswith("actions/download-artifact@")
        ]
        roles = {"request", "helper"} | (
            {"setup"} if name == "prepare" else set()
        )
        assert {
            step["with"]["path"].rsplit("/", 1)[1] for step in downloads
        } == roles
        for step in downloads:
            values = step["with"]
            assert values["skip-decompress"] is True
            assert values["digest-mismatch"] == "error"
            assert "-id }}" in values["artifact-ids"]
        source = "\n".join(step.get("run", "") for step in job["steps"])
        assert "dotnet build" not in source
        assert "--no-sync" in source
        assert "--helper-digest" in source
        assert "--request-digest" in source
        if name == "prepare":
            assert "dotnet tool restore" not in source
            assert "--setup-digest" in source
    for job in workflow["jobs"].values():
        for step in job["steps"]:
            if step.get("uses", "").startswith("actions/upload-artifact@"):
                assert step["with"]["overwrite"] is False
                assert step["with"]["archive"] is False
                assert step["with"]["retention-days"] == RETENTION_DAYS
                assert step["with"]["if-no-files-found"] == "error"
                if "diagnostics" in step["name"]:
                    assert step["if"] == "always()"
                else:
                    assert "if" not in step


def test_fixture_seal_preserves_payload_and_reports_exact_digest(
    workflow, tmp_path
):
    """Content-derived transport naming preserves the complete original ZIP."""
    step = next(
        step
        for step in workflow["jobs"]["prepare"]["steps"]
        if step.get("id") == "seal-prepare"
    )
    payload = tmp_path / "original pair.zip"
    content = b"original transport bytes\x00\xff"
    payload.write_bytes(content)
    outputs = tmp_path / "github-output"
    result = _pwsh(
        tmp_path,
        step["run"],
        {
            "WDV3_PAYLOAD": str(payload),
            "GITHUB_RUN_ID": "71",
            "GITHUB_OUTPUT": str(outputs),
        },
    )
    _assert_success(result)
    values = dict(
        line.split("=", 1) for line in outputs.read_text().splitlines()
    )
    digest = hashlib.sha256(content).hexdigest()
    assert values["digest"] == "sha256:" + digest
    assert values["name"] == f"wdv3-nuget-fixtures-prepare-r71-{digest}.zip"
    assert (tmp_path / values["path"]).read_bytes() == content
    assert payload.read_bytes() == content


def test_fixture_diagnostics_keep_distinct_raw_artifact_names(
    workflow, tmp_path
):
    """Raw uploads use file basenames; every job retains its own diagnostics."""
    names = []
    configured_names = []
    for job_name, job in workflow["jobs"].items():
        workspace = tmp_path / job_name
        evidence = workspace / ".wdv3" / "evidence"
        evidence.mkdir(parents=True)
        content = f"{job_name}: retained partial native diagnostics\n".encode()
        (evidence / "native-command.log").write_bytes(content)
        archive_step = next(
            step
            for step in job["steps"]
            if step["name"] == "Archive available preparation diagnostics"
        )
        upload_step = next(
            step
            for step in job["steps"]
            if step["name"] == "Retain immutable preparation diagnostics"
        )
        result = _pwsh(workspace, archive_step["run"], {"GITHUB_RUN_ID": "71"})
        _assert_success(result)
        path = workspace / upload_step["with"]["path"].replace(
            "${{ github.run_id }}", "71"
        )
        with zipfile.ZipFile(path) as archive:
            assert archive.namelist() == ["native-command.log"]
            assert archive.read("native-command.log") == content
        names.append(path.name)
        configured_names.append(
            upload_step["with"]["name"].replace("${{ github.run_id }}", "71")
        )
    assert len(set(names)) == len(names)
    assert configured_names == names


@pytest.mark.parametrize("status", [0, 17])
def test_fixture_shell_preserves_inputs_and_command_failure(
    workflow, tmp_path, status
):
    """Pass exact paths and identities, preserving a failed command."""
    step = next(
        step
        for step in workflow["jobs"]["prepare"]["steps"]
        if step["name"] == "Prepare offline original fixture pair"
    )
    environment = {"GITHUB_WORKSPACE": str(tmp_path / "workspace with spaces")}
    for role in ("REQUEST", "HELPER", "SETUP"):
        environment.update(
            {
                f"WDV3_{role}_NAME": role.lower() + ".zip",
                f"WDV3_{role}_ID": "123",
                f"WDV3_{role}_TRANSPORT_DIGEST": "sha256:" + "a" * 64,
                f"WDV3_{role}_URL": (
                    "https://github.com/hcoona/three/actions/runs/71/artifacts/123"
                ),
            }
        )
    source = (
        "function uv { $args | ConvertTo-Json | Set-Content 'arguments.json'; "
        "$global:LASTEXITCODE = " + str(status) + " }\n" + step["run"]
    )
    result = _pwsh(tmp_path, source, environment)
    assert result.returncode == status, result.stderr
    arguments = json.loads((tmp_path / "arguments.json").read_text())
    assert arguments[:3] == ["run", "--directory", "tooling"]
    module = arguments.index("-m")
    assert arguments[module + 1 : module + 3] == [
        "three_workflow_delivery_v3.acceptance.nuget_preparation",
        "prepare",
    ]
    for role in ("request", "helper", "setup"):
        index = arguments.index("--" + role)
        assert (
            arguments[index + 1]
            == environment["GITHUB_WORKSPACE"]
            + f"/.wdv3/input/{role}/{role}.zip"
        )
        index = arguments.index("--" + role + "-id")
        assert arguments[index + 1] == "123"
    assert arguments[arguments.index("--output") + 1].endswith(
        "/.wdv3/prepare.zip"
    )


def test_fixture_workflow_scripts_parse_in_powershell(workflow, tmp_path):
    """Parse every actual script without executing setup or native work."""
    scripts = [
        step["run"]
        for job in workflow["jobs"].values()
        for step in job["steps"]
        if "run" in step
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
