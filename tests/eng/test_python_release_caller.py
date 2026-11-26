"""Existing Python Release workflow permission and transfer contracts."""

import json
import shlex
from pathlib import Path

import pytest
import yaml

from .ci_workflow_shell import (
    action_steps,
    assert_release_transfers,
    condition,
    dependencies,
    selected_control_call,
)

ROOT = Path(__file__).resolve().parents[2]
_PHYSICAL_CONCURRENCY_GROUP = (
    "${{ format('workflow-delivery-{0}-{1}', "
    "inputs.registry == 'testpypi' && 'test.pypi.org' || 'pypi.org', "
    "inputs.unit) }}"
)


@pytest.fixture
def workflow():
    """Load the current workflow as semantic YAML values."""
    return yaml.safe_load(
        (
            ROOT / ".github/workflows/workflow-delivery-v3-python-release.yml"
        ).read_text()
    )


def scripts(job):
    """Collect job scripts without depending on YAML layout."""
    return "\n".join(step.get("run", "") for step in job["steps"])


def test_workflow_publisher_owns_write_identity_and_environment(
    workflow,
):
    """Workflow publisher owns write identity and environment."""
    jobs = workflow["jobs"]
    assert workflow["permissions"] == {}
    assert "secrets." not in json.dumps(workflow["env"])
    for name, job in jobs.items():
        assert job["permissions"] == (
            {"contents": "read", "id-token": "write"}
            if name == "publish"
            else {"contents": "read"}
        )
        assert ("environment" in job) == (name == "publish")
        for checkout in action_steps(job, "actions/checkout"):
            assert checkout["with"]["persist-credentials"] is False
        if name != "publish":
            assert "secrets." not in json.dumps(job)
            for publisher_only in ("publisher-config", "publish-python"):
                assert publisher_only not in scripts(job)
    environment = jobs["publish"]["environment"]["name"]
    assert "inputs.registry" in environment
    assert "workflow-delivery-v3-python-testpypi" in environment
    assert "workflow-delivery-v3-python-pypi" in environment
    assert {"prepare", "observe"} <= set(dependencies(jobs["publish"]))


def test_workflow_binds_the_dispatched_registered_unit(workflow):
    """Every job receives the dispatched unit; its group binds it."""
    unit = workflow["on"]["workflow_dispatch"]["inputs"]["unit"]
    assert unit["type"] == "string"
    assert unit["required"] is True
    assert "options" not in unit
    assert workflow["env"]["UNIT"] == "${{ inputs.unit }}"
    assert "inputs.unit" in workflow["concurrency"]["group"]


@pytest.mark.parametrize(
    ("dry", "missing", "allowed"),
    [
        (False, True, True),
        (False, False, False),
        (True, True, False),
        (True, False, False),
    ],
)
def test_workflow_publisher_gate_excludes_dry_and_initial_exact(
    workflow, dry, missing, allowed
):
    """Workflow publisher gate excludes dry and initial exact."""
    assert (
        condition(
            workflow["jobs"]["publish"]["if"],
            {
                "inputs.dry-run": dry,
                "needs.observe.outputs.write-needed": str(missing).lower(),
                "cancelled": False,
            },
        )
        is allowed
    )


@pytest.mark.parametrize(
    ("publish_result", "publication_id"),
    [("success", "42"), ("failure", "42"), ("failure", ""), ("skipped", "")],
)
def test_workflow_verification_runs_after_handled_failure_or_missing_result(
    workflow, publish_result, publication_id
):
    """Workflow verification runs after handled failure or missing result."""
    job = workflow["jobs"]["verify-result"]
    assert {"prepare", "observe", "publish"} <= set(dependencies(job))
    values = {
        "cancelled": False,
        "inputs.dry-run": False,
        "needs.prepare.result": "success",
        "needs.observe.result": "success",
        "needs.publish.result": publish_result,
        "needs.publish.outputs.publication-id": publication_id,
    }
    assert condition(job["if"], values, success=publish_result == "success")
    publication = next(
        step
        for step in action_steps(job, "actions/download-artifact")
        if "publication-id" in step["with"]["artifact-ids"]
    )
    assert condition(publication["if"], values) is bool(publication_id)
    assert not condition(job["if"], {**values, "cancelled": True})
    assert not condition(job["if"], {**values, "inputs.dry-run": True})
    assert not condition(
        job["if"], {**values, "needs.prepare.result": "failure"}
    )
    assert not condition(
        job["if"], {**values, "needs.observe.result": "failure"}
    )
    assert "consume-python" not in scripts(job)
    assert "observe-python" in scripts(job)
    assert "complete-python" in scripts(job)
    assert "continue-on-error" not in job


def test_workflow_immutable_artifacts_digest_rejection_and_45_day_retention(
    workflow,
):
    """Workflow immutable artifacts digest rejection and 45 day retention."""
    assert_release_transfers(workflow)


def test_workflow_frozen_recipe_and_registry_package_concurrency(
    workflow,
):
    """Workflow frozen recipe and registry package concurrency."""
    lines = scripts(workflow["jobs"]["prepare"]).splitlines()
    export_index, export = next(
        (index, shlex.split(line))
        for index, line in enumerate(lines)
        if " export " in line
    )
    for option in (
        "--no-config",
        "--frozen",
        "--no-emit-workspace",
        "--no-emit-local",
    ):
        assert option in export
    assert export[export.index("--only-group") + 1] == "dev"
    assert export[export.index("--format") + 1] == "requirements.txt"
    assert "--no-hashes" not in export
    prepare_index = next(
        index
        for index, line in enumerate(lines)
        if " release prepare-python " in line
    )
    assert export_index < prepare_index
    for name in ("observe", "publish", "verify-result"):
        assert "prepare-python-index-tools" in scripts(workflow["jobs"][name])
    concurrency = workflow["concurrency"]
    assert concurrency["cancel-in-progress"] is False
    group = concurrency["group"]
    assert group == _PHYSICAL_CONCURRENCY_GROUP
    assert not any(
        field in group
        for field in ("github.run_id", "github.ref", "inputs.dry-run")
    )


@pytest.mark.parametrize(
    ("job_name", "stage"),
    [
        ("prepare", ["request"]),
        ("observe", ["receive", "observation"]),
        ("publish", ["receive", "publication"]),
        ("verify-result", ["receive", "verification"]),
    ],
)
def test_workflow_selected_control_executable_reaches_caller(
    workflow, tmp_path, job_name, stage
):
    """Committed runtime setup supplies each job's first control invocation."""
    arguments = selected_control_call(
        workflow,
        job_name,
        root=ROOT,
        cwd=tmp_path,
        tools=("python", "uv"),
    )
    assert arguments == [
        str(tmp_path / "control/WorkflowDelivery.dll"),
        "release",
        "job-inputs",
        "python",
        *stage,
    ]
