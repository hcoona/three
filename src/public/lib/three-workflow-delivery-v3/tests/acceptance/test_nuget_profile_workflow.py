"""Actual profile workflow contracts with no dispatch or native collection."""

from __future__ import annotations

import hashlib
import json
import zipfile
from pathlib import Path

import pytest
import yaml
from three_workflow_delivery_v3.acceptance.nuget_profile import WORKFLOW_PATH
from three_workflow_delivery_v3.canonical import canonical_sha256, canonicalize

from ..contracts.test_nuget_workflows import _assert_success, _pwsh
from .test_nuget_profile import _platform

ROOT = Path(__file__).resolve().parents[6]


@pytest.fixture(scope="module")
def workflow():
    """Load the exact observer workflow rather than a modeled job."""
    return yaml.safe_load((ROOT / WORKFLOW_PATH).read_text(encoding="utf-8"))


def test_profile_workflow_is_credential_free_and_attempt_bound(workflow):
    """Only platform checkout/upload actions receive their ordinary tokens."""
    assert set(workflow["on"]) == {"workflow_dispatch"}
    assert set(workflow["on"]["workflow_dispatch"]["inputs"]) == {
        "profile_spec"
    }
    assert (
        workflow["on"]["workflow_dispatch"]["inputs"]["profile_spec"][
            "required"
        ]
        is True
    )
    assert workflow["permissions"] == {}
    assert set(workflow["jobs"]) == {"observe"}
    assert workflow["concurrency"]["cancel-in-progress"] is False
    assert workflow["defaults"]["run"]["shell"] == "pwsh"
    assert workflow["env"] == {
        "GIT_CONFIG_COUNT": "1",
        "GIT_CONFIG_KEY_0": "core.autocrlf",
        "GIT_CONFIG_VALUE_0": "false",
    }
    job = workflow["jobs"]["observe"]
    assert job["permissions"] == {"contents": "read"}
    assert job["runs-on"] == "windows-2022"
    assert job["timeout-minutes"] == 10  # noqa: PLR2004
    assert "environment" not in job
    assert "env" not in job
    for clause in (
        "github.event_name == 'workflow_dispatch'",
        "github.repository == 'hcoona/three'",
        "github.repository_id == '1102295886'",
        "github.actor_id == '712433'",
        "github.ref == 'refs/heads/main'",
        "github.ref_protected",
        "github.sha == fromJSON(inputs.profile_spec).toolingSha",
        "github.run_attempt == 1",
    ):
        assert clause in job["if"]
    assert "||" not in job["if"]
    assert {step["uses"] for step in job["steps"] if "uses" in step} == {
        "actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1",
        "astral-sh/setup-uv@20cfd1bf945f4377ade1205e4dbc17946fc9a30d",
        "actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a",
    }
    for step in job["steps"]:
        source = step.get("run", "")
        assert not any(
            forbidden in source
            for forbidden in (
                "dotnet ",
                "setup-dotnet",
                "nuget_probe",
                "nuget_preparation",
                "nuget_suite",
                "gh auth",
                "workflow run",
                "${{ github.token }}",
                "GITHUB_TOKEN",
                "GH_TOKEN",
                "NUGET_AUTH_TOKEN",
            )
        )
        assert set(step.get("env", {})).issubset({"WDV3_PROFILE_SPEC"})
        if step.get("uses", "").startswith("actions/checkout@"):
            assert step["with"] == {
                "ref": "${{ github.sha }}",
                "path": "tooling",
                "fetch-depth": 0,
                "persist-credentials": False,
            }
        if step.get("uses", "").startswith("astral-sh/setup-uv@"):
            assert step["with"]["version"] == "0.12.7"
    sync = next(
        step
        for step in job["steps"]
        if step["name"] == "Prepare the locked Python environment"
    )
    assert (
        "uv sync --directory tooling --locked --python 3.13.12 "
        "--package three-workflow-delivery-v3"
    ) in sync["run"]


@pytest.mark.parametrize("status", [0, 17])
def test_profile_workflow_preserves_spec_bytes_and_failure(
    workflow, tmp_path, status
):
    """Preserve literal UTF-8 environment input through actual PowerShell."""
    step = next(
        step
        for step in workflow["jobs"]["observe"]["steps"]
        if step["name"]
        == "Observe the current profile without package credentials"
    )
    assert step["env"] == {"WDV3_PROFILE_SPEC": "${{ inputs.profile_spec }}"}
    assert "${{ inputs." not in step["run"]
    workspace = tmp_path / "workspace with spaces"
    (workspace / ".wdv3/evidence").mkdir(parents=True)
    resources = {
        "serviceIndexSha256": "c" * 64,
        "packageBaseAddress": "https://nuget.pkg.github.com/hcoona/é/",
        "packagePublish": "https://nuget.pkg.github.com/hcoona/$(literal)",
    }
    content = canonicalize(
        {
            "toolingSha": "a" * 40,
            "preflightSha256": "b" * 64,
            "resources": resources,
            "resourcesDigest": canonical_sha256(resources),
        }
    )
    source = (
        "function uv { ConvertTo-Json -InputObject @($args) -Compress | "
        "Add-Content -LiteralPath 'arguments.jsonl' -Encoding utf8; "
        "$global:LASTEXITCODE = " + str(status) + " }\n" + step["run"]
    )
    result = _pwsh(
        tmp_path,
        source,
        {
            "GITHUB_WORKSPACE": str(workspace),
            "WDV3_PROFILE_SPEC": content.decode("utf-8"),
        },
    )

    assert result.returncode == status, result.stderr
    assert (workspace / ".wdv3/evidence/spec.json").read_bytes() == content
    calls = [
        json.loads(line)
        for line in (tmp_path / "arguments.jsonl")
        .read_text(encoding="utf-8")
        .splitlines()
    ]
    assert len(calls) == 1
    arguments = calls[0]
    assert (
        arguments.count("three_workflow_delivery_v3.acceptance.nuget_profile")
        == 1
    )
    assert arguments[arguments.index("-m") + 1] == (
        "three_workflow_delivery_v3.acceptance.nuget_profile"
    )
    for argument, suffix in (
        ("--spec", "/.wdv3/evidence/spec.json"),
        ("--checkout", "/tooling"),
        ("--evidence", "/.wdv3/evidence/observation"),
    ):
        assert (
            arguments[arguments.index(argument) + 1] == str(workspace) + suffix
        )
    assert "--no-sync" in arguments
    assert arguments[arguments.index("--python") + 1] == "3.13.12"
    assert not (workspace / ".wdv3/evidence/observation").exists()


def test_profile_workflow_retains_immutable_observation(workflow, tmp_path):
    """The raw archive retains available bytes even when observation failed."""
    job = workflow["jobs"]["observe"]
    seal = next(step for step in job["steps"] if step.get("id") == "seal")
    upload = next(
        step
        for step in job["steps"]
        if step["name"] == "Retain immutable profile evidence"
    )
    assert seal["if"] == "always()"
    assert upload["if"] == "always() && steps.seal.outcome == 'success'"
    assert upload["with"] == {
        "name": "${{ steps.seal.outputs.name }}",
        "path": "${{ steps.seal.outputs.path }}",
        "if-no-files-found": "error",
        "retention-days": 45,
        "overwrite": False,
        "archive": False,
        "include-hidden-files": True,
    }
    evidence = tmp_path / ".wdv3/evidence"
    (evidence / "observation").mkdir(parents=True)
    originals = {
        "platform.json": b'{"controlled":"platform"}\r\n',
        "observation/failure.json": b'{"nativeAdmissionEstablished":false}',
    }
    for name, data in originals.items():
        (evidence / name).write_bytes(data)
    outputs = tmp_path / "outputs"

    _assert_success(
        _pwsh(
            tmp_path,
            seal["run"],
            {"GITHUB_RUN_ID": "81", "GITHUB_OUTPUT": str(outputs)},
        )
    )

    values = dict(
        line.split("=", 1) for line in outputs.read_text().splitlines()
    )
    payload = tmp_path / values["path"]
    digest = hashlib.sha256(payload.read_bytes()).hexdigest()
    assert values["name"] == f"wdv3-nuget-profile-r81-{digest}.zip"
    assert payload.name == values["name"]
    assert values["digest"] == "sha256:" + digest
    with zipfile.ZipFile(payload) as archive:
        assert {
            name.replace("\\", "/"): archive.read(name)
            for name in archive.namelist()
            if not name.endswith("/")
        } == originals
    assert {
        name: (evidence / name).read_bytes() for name in originals
    } == originals


def test_profile_workflow_retains_only_public_platform_fields(
    workflow, tmp_path
):
    """Failure diagnostics preserve run identity without an environment dump."""
    step = workflow["jobs"]["observe"]["steps"][0]
    assert step["name"] == "Retain actual profile platform context"
    platform = _platform()

    _assert_success(
        _pwsh(
            tmp_path,
            step["run"],
            {**platform, "CONTROLLED_PRIVATE_VALUE": "never-retain-this"},
        )
    )

    context = json.loads(
        (tmp_path / ".wdv3/evidence/platform.json").read_text()
    )
    assert context == platform
    assert "never-retain-this" not in json.dumps(context)
