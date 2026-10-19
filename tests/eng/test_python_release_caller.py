"""Existing Python Release workflow permission and transfer contracts."""

import json
import os
import re
import shlex
import shutil
import subprocess
import sys
import tomllib
from pathlib import Path

import pytest
import yaml

ROOT = Path(__file__).resolve().parents[2]
EXPRESSION = re.compile(r"\$\{\{\s*(\S+)\s*\}\}")
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


def test_workflow_binds_the_dispatched_registered_unit(workflow):
    """Every job receives the dispatched unit; its group binds it."""
    unit = workflow["on"]["workflow_dispatch"]["inputs"]["unit"]
    assert unit["type"] == "choice"
    assert unit["required"] is True
    assert unit["options"] == ["hcoona-release-smoke-python", "nbgv-python"]
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
    assert "consume-python" not in scripts(job)
    assert "observe-python" in scripts(job)
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


def committed_versions(workflow):
    """Run prepare's version reader against the checked-out root."""
    step = next(
        step
        for step in workflow["jobs"]["prepare"]["steps"]
        if step.get("id") == "tools"
    )
    result = subprocess.run(  # noqa: S603
        [
            shutil.which("bash"),
            "-ec",
            step["run"].replace('"$GITHUB_OUTPUT"', "/dev/stdout"),
        ],
        cwd=ROOT,
        capture_output=True,
        text=True,
        check=True,
    )
    versions = dict(line.split("=", 1) for line in result.stdout.splitlines())
    lock = tomllib.loads((ROOT / "mise.lock").read_text())["tools"]
    sdk = json.loads((ROOT / "global.json").read_text())["sdk"]["version"]
    assert versions == {
        "dotnet": sdk,
        "python": lock["python"][0]["version"],
        "uv": lock["uv"][0]["version"],
    }
    return versions


def bound_version(workflow, job_name, value, versions):
    """Bind prepare's step outputs, directly or through its job outputs."""
    expression = EXPRESSION.fullmatch(value)[1]
    if job_name != "prepare":
        output = expression.removeprefix("needs.prepare.outputs.")
        declared = workflow["jobs"]["prepare"]["outputs"][output]
        expression = EXPRESSION.fullmatch(declared)[1]
    return versions[expression.removeprefix("steps.tools.outputs.")]


@pytest.mark.parametrize(
    "job_name", ["prepare", "observe", "publish", "verify-result"]
)
def test_workflow_selected_control_executable_reaches_caller(
    workflow, tmp_path, job_name
):
    """Committed runtime setup supplies each job's first control invocation."""
    job = workflow["jobs"][job_name]
    setup = next(
        step for step in job["steps"] if "mise install" in step.get("run", "")
    )
    versions = committed_versions(workflow)
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
        f"#!{sys.executable}\nimport json, os, sys\n"
        "from pathlib import Path\n"
        "with Path(os.environ['MISE_LOG']).open('a') as log:\n"
        "    log.write(json.dumps(sys.argv[1:]) + '\\n')\n"
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
        MISE_LOG=str(tmp_path / "mise.jsonl"),
    )
    # The install step receives the committed versions read once in prepare.
    environment.update(
        {
            key: bound_version(workflow, job_name, value, versions)
            for key, value in setup.get("env", {}).items()
        }
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
    calls = [
        json.loads(line)
        for line in (tmp_path / "mise.jsonl").read_text().splitlines()
    ]
    install = next(call for call in calls if call[0] == "install")
    assert sorted(item for item in install if "@" in item) == sorted(
        f"{tool}@{version}" for tool, version in versions.items()
    )
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
    assert arguments[4] == ("request" if job_name == "prepare" else "receive")
