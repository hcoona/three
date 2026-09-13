"""Actual PowerShell transport contracts without dispatch or native effects."""

from __future__ import annotations

import hashlib
import json
import zipfile
from pathlib import Path

import pytest
import yaml
from three_workflow_delivery_v3.acceptance.nuget_probe import WORKFLOW_PATH
from three_workflow_delivery_v3.records.artifacts import (
    artifact_reference_from_document,
)

from ..contracts.test_nuget_workflows import _assert_success, _pwsh

ROOT = Path(__file__).resolve().parents[6]


@pytest.fixture(scope="module")
def workflow():
    """Load the actual Windows entry under test."""
    return yaml.safe_load((ROOT / WORKFLOW_PATH).read_text(encoding="utf-8"))


def test_probe_workflow_limits_publication_authority_to_publisher_job(workflow):
    """Only the publisher job has effective package-write permission."""
    assert set(workflow["on"]) == {"workflow_dispatch"}
    assert workflow["permissions"] == {}
    assert set(workflow["jobs"]) == {"prepare", "publish"}
    assert workflow["jobs"]["prepare"]["permissions"] == {
        "contents": "read",
        "actions": "read",
    }
    assert workflow["jobs"]["publish"]["permissions"] == {
        "contents": "read",
        "packages": "write",
    }
    assert workflow["concurrency"]["cancel-in-progress"] is False
    assert workflow["env"] == {
        "GIT_CONFIG_COUNT": "1",
        "GIT_CONFIG_KEY_0": "core.autocrlf",
        "GIT_CONFIG_VALUE_0": "false",
    }
    # Pinned checkout/setup actions can receive the publisher job token through
    # their input defaults. This scan covers explicit command environments only.
    explicit_tokens = []
    for name, job in workflow["jobs"].items():
        assert job["runs-on"] == "windows-2022"
        assert "environment" not in job
        for clause in (
            "github.event_name == 'workflow_dispatch'",
            "github.repository == 'hcoona/three'",
            "github.repository_id == '1102295886'",
            "github.actor_id == '712433'",
            "github.ref == 'refs/heads/main'",
            "github.ref_protected",
            "github.sha == fromJSON(inputs.probe_spec).toolingSha",
            "github.run_attempt == 1",
        ):
            assert clause in job["if"]
        assert "||" not in job["if"]
        for step in job["steps"]:
            environment = step.get("env", {})
            assert not {"GH_TOKEN", "NUGET_AUTH_TOKEN"}.intersection(
                environment
            )
            if "GITHUB_TOKEN" in environment:
                explicit_tokens.append(
                    (name, step["name"], environment["GITHUB_TOKEN"])
                )
            source = step.get("run", "")
            assert not any(
                command in source
                for command in (
                    "dotnet build",
                    "dotnet pack",
                    "dotnet tool restore",
                    "gh auth",
                    "workflow run",
                )
            )
            if step.get("uses", "").startswith("actions/checkout@"):
                assert step["with"]["ref"] == "${{ github.sha }}"
                assert step["with"]["fetch-depth"] == 0
                assert step["with"]["persist-credentials"] is False
    assert explicit_tokens == [
        ("publish", "Invoke the bound probe once", "${{ github.token }}")
    ]
    publisher = workflow["jobs"]["publish"]
    assert publisher["needs"] == "prepare"
    assert not any(
        "setup-dotnet" in step.get("uses", "") for step in publisher["steps"]
    )


def test_probe_workflow_binds_immutable_inputs(workflow):
    """Original producer IDs and current-run transport remain distinct."""
    for name, job in workflow["jobs"].items():
        downloads = [
            step["with"]
            for step in job["steps"]
            if step.get("uses", "").startswith("actions/download-artifact@")
        ]
        expected = {"fixture", "helper"} if name == "prepare" else {"prepared"}
        assert {
            item["path"].rsplit("/", 1)[1] for item in downloads
        } == expected
        for item in downloads:
            assert item["skip-decompress"] is True
            assert item["digest-mismatch"] == "error"
            assert "-id }}" in item["artifact-ids"]
            if name == "prepare":
                assert (
                    item["run-id"] == "${{ steps.bind.outputs.producer-run }}"
                )
                assert item["repository"] == "hcoona/three"
                assert item["github-token"] == "${{ github.token }}"
            else:
                assert "run-id" not in item
                assert (
                    item["artifact-ids"]
                    == "${{ needs.prepare.outputs.input-id }}"
                )
        for step in job["steps"]:
            if step.get("uses", "").startswith("actions/upload-artifact@"):
                assert step["with"]["archive"] is False
                assert step["with"]["overwrite"] is False
                assert step["with"]["retention-days"] == 45  # noqa: PLR2004
                assert step["with"]["if-no-files-found"] == "error"
                if "diagnostics" in step["name"]:
                    assert step["if"] == "always()"


def test_probe_seal_preserves_original_payload(workflow, tmp_path):
    """Transport naming does not change the prepared ZIP's actual bytes."""
    step = next(
        step
        for step in workflow["jobs"]["prepare"]["steps"]
        if step.get("id") == "seal"
    )
    content = b"controlled immutable ZIP\x00\xff"
    payload = tmp_path / "prepared pair.zip"
    payload.write_bytes(content)
    output = tmp_path / "outputs"
    result = _pwsh(
        tmp_path,
        step["run"],
        {
            "WDV3_PAYLOAD": str(payload),
            "GITHUB_RUN_ID": "81",
            "GITHUB_OUTPUT": str(output),
        },
    )
    _assert_success(result)
    values = dict(
        line.split("=", 1) for line in output.read_text().splitlines()
    )
    digest = hashlib.sha256(content).hexdigest()
    assert values["name"] == f"wdv3-nuget-probe-input-r81-{digest}.zip"
    assert values["digest"] == "sha256:" + digest
    assert (tmp_path / values["path"]).read_bytes() == content
    assert payload.read_bytes() == content


def test_probe_reference_preserves_actual_upload_identity(workflow, tmp_path):
    """Raw action output becomes a canonical, separately bound reference."""
    step = next(
        step
        for step in workflow["jobs"]["publish"]["steps"]
        if step["name"] == "Bind the actual prepared artifact reference"
    )
    evidence = tmp_path / ".wdv3/evidence"
    evidence.mkdir(parents=True)
    environment = {
        "GITHUB_WORKSPACE": str(tmp_path),
        "WDV3_INPUT_NAME": "prepared.zip",
        "WDV3_INPUT_ID": "201",
        "WDV3_INPUT_DIGEST": "sha256:" + "a" * 64,
        "WDV3_INPUT_TRANSPORT_DIGEST": "b" * 64,
        "WDV3_INPUT_URL": "https://github.com/hcoona/three/actions/runs/81/artifacts/201",
    }
    _assert_success(_pwsh(tmp_path, step["run"], environment))
    reference = artifact_reference_from_document(
        json.loads((evidence / "reference.json").read_bytes())
    )
    assert reference.to_document() == {
        "artifact-id": 201,
        "artifact-digest": "sha256:" + "b" * 64,
        "artifact-url": environment["WDV3_INPUT_URL"],
        "payload-path": "prepared.zip",
        "payload-digest": "sha256:" + "a" * 64,
    }


@pytest.mark.parametrize("status", [0, 17])
def test_probe_shell_preserves_paths_and_failure(workflow, tmp_path, status):
    """The invocation receives exact paths and its failure reaches Actions."""
    step = next(
        step
        for step in workflow["jobs"]["publish"]["steps"]
        if step["name"] == "Invoke the bound probe once"
    )
    environment = {
        "GITHUB_WORKSPACE": str(tmp_path / "workspace with spaces"),
        "WDV3_INPUT_NAME": "original.zip",
    }
    source = (
        "function uv { $args | ConvertTo-Json | Set-Content 'arguments.json'; "
        "$global:LASTEXITCODE = " + str(status) + " }\n" + step["run"]
    )
    result = _pwsh(tmp_path, source, environment)
    assert result.returncode == status, result.stderr
    arguments = json.loads((tmp_path / "arguments.json").read_text())
    assert arguments.count("publish") == 1
    assert (
        arguments[arguments.index("--prepared") + 1]
        == environment["GITHUB_WORKSPACE"]
        + "/.wdv3/input/prepared/original.zip"
    )
    assert (
        arguments[arguments.index("--evidence") + 1]
        == environment["GITHUB_WORKSPACE"] + "/.wdv3/evidence/publish"
    )
    assert (
        arguments[arguments.index("-m") + 1]
        == "three_workflow_delivery_v3.acceptance.nuget_probe"
    )
    assert "--no-sync" in arguments


def test_probe_failure_diagnostics_keep_both_jobs_and_original_bytes(
    workflow, tmp_path
):
    """Retain partial evidence under distinct immutable raw basenames."""
    names = []
    for job_name, job in workflow["jobs"].items():
        workspace = tmp_path / job_name
        evidence = workspace / ".wdv3/evidence"
        evidence.mkdir(parents=True)
        content = b"controlled partial diagnostics\r\n"
        (evidence / "failure.json").write_bytes(content)
        step = next(
            step
            for step in job["steps"]
            if step["name"] == "Archive available probe diagnostics"
        )
        upload = next(
            step
            for step in job["steps"]
            if step["name"] == "Retain immutable probe diagnostics"
        )
        _assert_success(_pwsh(workspace, step["run"], {"GITHUB_RUN_ID": "81"}))
        path = workspace / upload["with"]["path"].replace(
            "${{ github.run_id }}", "81"
        )
        with zipfile.ZipFile(path) as archive:
            assert archive.namelist() == ["failure.json"]
            assert archive.read("failure.json") == content
        assert (
            upload["with"]["name"].replace("${{ github.run_id }}", "81")
            == path.name
        )
        names.append(path.name)
    assert len(set(names)) == len(names)
