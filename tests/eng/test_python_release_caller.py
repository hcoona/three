"""Existing Python Release workflow permission and transfer contracts."""

import json
import os
import re
import shlex
import shutil
import subprocess
import sys
from pathlib import Path

import pytest
import yaml

ROOT = Path(__file__).resolve().parents[2]
_PHYSICAL_CONCURRENCY_GROUP = (
    "${{ format('workflow-delivery-{0}-hcoona-release-smoke-python', "
    "inputs.registry == 'testpypi' && 'test.pypi.org' || 'pypi.org') }}"
)


@pytest.fixture
def workflow():
    """Load the current workflow as semantic YAML values."""
    return yaml.safe_load(
        (
            ROOT / ".github/workflows/workflow-delivery-v3-python-smoke.yml"
        ).read_text()
    )


def actions(job, action):
    """Select action use sites without coupling to step names."""
    return [
        step
        for step in job["steps"]
        if step.get("uses", "").startswith("actions/" + action + "@")
    ]


def scripts(job):
    """Collect job scripts without depending on YAML layout."""
    return "\n".join(step.get("run", "") for step in job["steps"])


def condition(expression, values):
    """Evaluate the bounded native boolean gate, independent of text spacing."""
    text = expression.removeprefix("${{").removesuffix("}}")
    text = text.replace("always()", "True").replace(
        "cancelled()", str(values["cancelled"])
    )
    text = re.sub(
        r"\b(?:inputs|needs)\.[\w.-]+",
        lambda match: repr(values[match[0]]),
        text,
    )
    text = re.sub(r"\btrue\b", "True", text)
    text = re.sub(r"\bfalse\b", "False", text)
    text = text.replace("&&", " and ").replace("||", " or ")
    text = re.sub(r"!(?!=)", " not ", text)
    return bool(eval(text, {"__builtins__": {}}, {}))  # noqa: S307 - Controlled boolean workflow expression.


def test_workflow_publisher_owns_write_identity_and_environment(
    workflow,
):
    """Workflow publisher owns write identity and environment."""
    jobs = workflow["jobs"]
    assert set(jobs) == {"prepare", "observe", "publish", "verify-result"}
    assert workflow["permissions"] == {}
    for name, job in jobs.items():
        assert job["permissions"] == (
            {"contents": "read", "id-token": "write"}
            if name == "publish"
            else {"contents": "read"}
        )
        assert ("environment" in job) == (name == "publish")
    environment = jobs["publish"]["environment"]["name"]
    assert "inputs.registry" in environment
    assert "workflow-delivery-v3-python-testpypi" in environment
    assert "workflow-delivery-v3-python-pypi" in environment
    assert set(jobs["publish"]["needs"]) == {"prepare", "observe"}
    for name in ("observe", "publish", "verify-result"):
        assert not actions(jobs[name], "upload-artifact")[0].get(
            "continue-on-error", False
        )


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
    assert set(job["needs"]) == {"prepare", "observe", "publish"}
    values = {
        "cancelled": False,
        "inputs.dry-run": False,
        "needs.prepare.result": "success",
        "needs.observe.result": "success",
        "needs.publish.result": publish_result,
        "needs.publish.outputs.publication-id": publication_id,
    }
    assert condition(job["if"], values)
    publication = next(
        step
        for step in actions(job, "download-artifact")
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
    assert "consume-python" in scripts(job)
    assert "python-wheel" in scripts(job)
    assert "python-sdist" in scripts(job)
    assert "complete-python" in scripts(job)
    assert "continue-on-error" not in job


def test_workflow_immutable_artifacts_digest_rejection_and_45_day_retention(
    workflow,
):
    """Workflow immutable artifacts digest rejection and 45 day retention."""
    uploads = []
    downloads = []
    for job_name, job in workflow["jobs"].items():
        for step in actions(job, "upload-artifact"):
            uploads.append((job_name, step))
            assert step["with"]["retention-days"] == 45
            assert step["with"]["overwrite"] is False
            assert step["with"]["if-no-files-found"] == "error"
            assert step["if"] == "always()"
            assert "github.run_id" in step["with"]["name"]
            assert "github.run_attempt" in step["with"]["name"]
        for step in actions(job, "download-artifact"):
            downloads.append(step)
            transfer = step["with"]
            assert "name" not in transfer
            assert transfer["digest-mismatch"] == "error"
            match = re.fullmatch(
                r"\$\{\{\s*needs\.([\w-]+)\.outputs\.([\w-]+)\s*\}\}",
                transfer["artifact-ids"],
            )
            assert match is not None
            producer, output = match.groups()
            expression = workflow["jobs"][producer]["outputs"][output]
            identifier = re.fullmatch(
                r"\$\{\{\s*steps\.([\w-]+)\.outputs\.artifact-id\s*\}\}",
                expression,
            )
            assert identifier is not None
            assert any(
                upload["id"] == identifier[1]
                for upload in actions(
                    workflow["jobs"][producer], "upload-artifact"
                )
            )
    assert len(uploads) == 5
    assert len(downloads) == 9
    artifact_names = [step["with"]["name"] for _, step in uploads]
    assert len(set(artifact_names)) == len(artifact_names)


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
    "job_name", ["prepare", "observe", "publish", "verify-result"]
)
def test_workflow_selected_control_executable_reaches_caller(
    workflow, tmp_path, job_name
):
    """Selected runtime setup supplies each job's first control invocation."""
    job = workflow["jobs"][job_name]
    setup = next(
        step for step in job["steps"] if "mise install" in step.get("run", "")
    )
    command = next(
        line.strip()
        for step in job["steps"]
        for line in step.get("run", "").splitlines()
        if line.strip().startswith('"$DOTNET_EXECUTABLE"')
    )
    tools = tmp_path / "tools"
    tools.mkdir()
    selected = tools / "selected-dotnet"
    selected.write_text(
        f"#!{sys.executable}\nimport json, os, sys\n"
        "from pathlib import Path\n"
        "Path(os.environ['COMMAND_LOG']).write_text(json.dumps(sys.argv[1:]))\n"
    )
    selected.chmod(0o755)
    mise = tools / "mise"
    mise.write_text(
        f"#!{sys.executable}\nimport os, sys\n"
        "if sys.argv[1] == 'which': print(os.environ['SELECTED_DOTNET'])\n"
    )
    mise.chmod(0o755)
    environment = {
        key: value
        for key, value in os.environ.items()
        if key
        not in {"DOTNET_EXECUTABLE", "UV_EXECUTABLE", "PYTHON_EXECUTABLE"}
    }
    environment.update(
        PATH=str(tools) + os.pathsep + environment.get("PATH", ""),
        SELECTED_DOTNET=str(selected),
        GITHUB_ENV=str(tmp_path / "github-env"),
        GITHUB_PATH=str(tmp_path / "github-path"),
        RUNNER_TEMP=str(tmp_path),
        COMMAND_LOG=str(tmp_path / "command.json"),
    )
    result = subprocess.run(  # noqa: S603
        [
            shutil.which("bash"),
            "--noprofile",
            "--norc",
            "-eo",
            "pipefail",
            "-c",
            setup["run"],
        ],
        cwd=tmp_path,
        env=environment,
        capture_output=True,
        text=True,
        check=False,
        timeout=20,
    )
    assert result.returncode == 0, result.stderr
    environment.update(
        line.split("=", 1)
        for line in (tmp_path / "github-env").read_text().splitlines()
    )
    result = subprocess.run(  # noqa: S603
        [
            shutil.which("bash"),
            "--noprofile",
            "--norc",
            "-eo",
            "pipefail",
            "-c",
            "set -u\n" + command,
        ],
        cwd=tmp_path,
        env=environment,
        capture_output=True,
        text=True,
        check=False,
        timeout=20,
    )
    assert result.returncode == 0, result.stderr
    arguments = json.loads((tmp_path / "command.json").read_text())
    assert arguments[:4] == [
        str(tmp_path / "control/WorkflowDelivery.dll"),
        "release",
        "job-inputs",
        "python",
    ]
    assert arguments[4] == ("request" if job_name == "prepare" else "validate")
