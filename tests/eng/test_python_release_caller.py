"""Controlled ordinary Python Release caller and workflow effect boundaries."""

from __future__ import annotations

import copy
import importlib.util
import json
import re
import shlex
from pathlib import Path

import pytest
import yaml

_PHYSICAL_CONCURRENCY_GROUP = (
    "${{ format('workflow-delivery-{0}-hcoona-release-smoke-python', "
    "inputs.registry == 'testpypi' && 'test.pypi.org' || 'pypi.org') }}"
)

ROOT = Path(__file__).resolve().parents[2]
NATIVE = (
    ROOT / "src/private/app/workflow-delivery/Native/Python/release_caller.py"
)
_spec = importlib.util.spec_from_file_location(
    "ordinary_python_release_caller", NATIVE
)
assert _spec is not None
assert _spec.loader is not None
caller = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(caller)


def read(path):
    """Read a disposable JSON transfer."""
    return json.loads(path.read_text(encoding="utf-8"))


def write(path, value):
    """Write a disposable JSON transfer."""
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value), encoding="utf-8")


@pytest.fixture
def native(tmp_path, monkeypatch):
    """Provide disposable runner/source state; no executable is invoked."""
    runner = tmp_path / "runner"
    source = tmp_path / "source"
    (runner / "control").mkdir(parents=True)
    source.mkdir()
    environment = {
        "RUNNER_TEMP": str(runner),
        "GITHUB_WORKSPACE": str(source),
        "GITHUB_REPOSITORY": "hcoona/three",
        "GITHUB_SHA": "a" * 40,
        "GITHUB_REF": "refs/heads/development",
        "GITHUB_RUN_ID": "123456",
        "GITHUB_RUN_ATTEMPT": "1",
        "DESTINATION": "testpypi",
        "DRY_RUN": "true",
        "MISE_DATA_DIR": str(tmp_path / "mise"),
        "UV_EXECUTABLE": str(tmp_path / "tools/uv"),
        "PYTHON_EXECUTABLE": str(tmp_path / "tools/python"),
        "GITHUB_OUTPUT": str(runner / "github-output"),
    }
    monkeypatch.setattr(caller.os, "environ", environment)
    return runner, source, environment


@pytest.mark.parametrize(
    ("destination", "channel"), [("testpypi", "buddy"), ("pypi", "official")]
)
@pytest.mark.parametrize("dry", [True, False])
def test_request_transfers_exact_source_destination_mode_and_producer(
    native, destination, channel, dry
):
    """Request transfers exact source destination mode and producer."""
    runner, source, environment = native
    environment.update(DESTINATION=destination, DRY_RUN=str(dry).lower())
    caller.main(["request"])
    expected = {
        "revision": "a" * 40,
        "selectedRef": "refs/heads/development",
        "unit": "hcoona-release-smoke-python",
        "declaration": (
            "tests/private/app/workflow-delivery/fixtures/products/hcoona-release-smoke-python/"
            "workflow-delivery.release-unit.yml"
        ),
        "channel": channel,
        "destination": destination,
        "dryRun": dry,
    }
    assert read(runner / "prepared/request.json") == {
        "subject": expected,
        "checkout": str(source),
        "scratch": str(runner / "preparation-scratch"),
        "miseDataDirectory": environment["MISE_DATA_DIR"],
    }
    producer = {"subject": expected, "run": "123456", "attempt": "1"}
    assert read(runner / "control/producer.json") == producer
    assert read(runner / "prepared/producer.json") == producer
    assert list((runner / "preparation-scratch").iterdir()) == []
    assert list(source.iterdir()) == []
    with pytest.raises(FileExistsError):
        caller.main(["request"])
    assert read(runner / "prepared/producer.json") == producer


@pytest.mark.parametrize("defect", ["repository", "destination", "live-rerun"])
def test_request_rejects_unsupported_repository_destination_or_live_rerun(
    native, defect
):
    """Request rejects unsupported repository destination or live rerun."""
    runner, _, environment = native
    if defect == "repository":
        environment["GITHUB_REPOSITORY"] = "another/repository"
    elif defect == "destination":
        environment["DESTINATION"] = "another-index"
    else:
        environment.update(DRY_RUN="false", GITHUB_RUN_ATTEMPT="2")
    with pytest.raises(ValueError, match=r"Unsupported|Live rerun"):
        caller.main(["request"])
    assert not (runner / "prepared").exists()
    assert not (runner / "control/producer.json").exists()


def test_dry_rerun_keeps_actual_attempt(native):
    """Dry rerun keeps actual attempt."""
    runner, _, environment = native
    environment["GITHUB_RUN_ATTEMPT"] = "2"
    caller.main(["request"])
    assert read(runner / "prepared/producer.json")["attempt"] == "2"
    assert read(runner / "prepared/request.json")["subject"]["dryRun"] is True


def transferred(runner):
    """Construct the four disposable producer transfers."""
    caller.main(["request"])
    write(
        runner / "prepared/preparation.json",
        {"prepared": {"subject": caller.subject(), "files": []}},
    )
    caller.main(["phase", "observation"])
    caller.main(["phase", "publication"])


@pytest.mark.parametrize(
    "kind", ["control", "prepared", "observation", "publication"]
)
@pytest.mark.parametrize(
    "field",
    [
        "run",
        "attempt",
        "revision",
        "selectedRef",
        "unit",
        "declaration",
        "channel",
        "destination",
        "dryRun",
    ],
)
def test_validate_rejects_transferred_producer_substitution(
    native, kind, field
):
    """Validate rejects transferred producer substitution."""
    runner, _, _ = native
    transferred(runner)
    caller.main(
        ["validate", "control", "prepared", "observation", "publication"]
    )
    path = runner / kind / "producer.json"
    bad = read(path)
    if field in ("run", "attempt"):
        bad[field] = "another-run"
    else:
        bad["subject"][field] = False if field == "dryRun" else "substituted"
    write(path, bad)
    with pytest.raises(ValueError, match="producer mismatch"):
        caller.main(
            ["validate", "control", "prepared", "observation", "publication"]
        )
    assert read(path) == bad


@pytest.mark.parametrize(
    "field",
    [
        "revision",
        "selectedRef",
        "unit",
        "declaration",
        "channel",
        "destination",
        "dryRun",
    ],
)
def test_validate_and_originals_reject_preparation_subject_substitution(
    native, field
):
    """Validate and originals reject preparation subject substitution."""
    runner, _, _ = native
    transferred(runner)
    bad = {
        "prepared": {"subject": copy.deepcopy(caller.subject()), "files": []}
    }
    bad["prepared"]["subject"][field] = (
        False if field == "dryRun" else "substituted"
    )
    write(runner / "prepared/preparation.json", bad)
    with pytest.raises(ValueError, match="subject mismatch"):
        caller.main(["validate", "prepared"])
    with pytest.raises(ValueError, match="subject mismatch"):
        caller.main(["originals"])
    assert not (runner / "prepared/archives").exists()


def test_originals_copy_only_source_relative_preparation_bytes(native):
    """Originals copy only source relative preparation bytes."""
    runner, source, _ = native
    caller.main(["request"])
    originals = {
        "archives/nested/package.whl": b"original wheel",
        "archives/package.tar.gz": b"original sdist",
    }
    for relative, contents in originals.items():
        path = runner / "preparation-scratch" / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(contents)
        wrong = source / relative
        wrong.parent.mkdir(parents=True, exist_ok=True)
        wrong.write_bytes(b"wrong checkout original")
    write(
        runner / "prepared/preparation.json",
        {
            "prepared": {
                "subject": caller.subject(),
                "files": [{"path": path} for path in originals],
            }
        },
    )
    caller.main(["originals"])
    for relative, contents in originals.items():
        assert (runner / "prepared" / relative).read_bytes() == contents
        assert (source / relative).read_bytes() == b"wrong checkout original"


@pytest.mark.parametrize("escape", ["absolute", "parent", "symlink"])
def test_originals_reject_source_escape_without_copy(native, escape):
    """Originals reject source escape without copy."""
    runner, source, _ = native
    caller.main(["request"])
    outside = source / "outside.whl"
    outside.write_bytes(b"outside original")
    relative = "../source/outside.whl"
    if escape == "absolute":
        relative = str(outside)
    elif escape == "symlink":
        relative = "escaped.whl"
        (runner / "preparation-scratch" / relative).symlink_to(outside)
    write(
        runner / "prepared/preparation.json",
        {
            "prepared": {
                "subject": caller.subject(),
                "files": [{"path": relative}],
            }
        },
    )
    with pytest.raises(ValueError, match="leaves preparation"):
        caller.main(["originals"])
    assert not (runner / "prepared/escaped.whl").exists()
    assert outside.read_bytes() == b"outside original"


def test_phase_and_tool_configs_generate_fresh_owned_inputs_without_uv(native):
    """Phase and tool configs generate fresh owned inputs without uv."""
    runner, source, environment = native
    transferred(runner)
    caller.main(["phase", "verification"])
    assert read(runner / "verification/producer.json") == caller.producer()
    caller.main(["tool-request"])
    assert read(runner / "index-tool-request.json") == {
        "uv": environment["UV_EXECUTABLE"],
        "interpreter": environment["PYTHON_EXECUTABLE"],
        "requirements": str(runner / "control/index-requirements.txt"),
        "scratch": str(runner / "index-tool-scratch"),
    }
    installed = str(runner / "index-tool-scratch/venv/bin/python")
    write(runner / "index-tool-result.json", {"interpreter": installed})
    caller.main(["index-config", "initial-read"])
    assert read(runner / "index-tools.json") == {
        "interpreter": installed,
        "helper": str(runner / "control/packages.py"),
        "scratch": str(runner / "initial-read"),
    }
    caller.main(["publisher-config"])
    assert read(runner / "publisher-tools.json") == {
        "uv": environment["UV_EXECUTABLE"],
        "scratch": str(runner / "publisher-scratch"),
    }
    environment.update(
        PATH="controlled-path",
        DOTNET_ROOT="controlled-dotnet",
        ACTIONS_ID_TOKEN_REQUEST_TOKEN="never-forward",  # noqa: S106 - Controlled sentinel, not a credential.
        UV_CONFIG_FILE="never-forward",
        PYTHON_KEYRING_BACKEND="never-forward",
    )
    for kind in ("python-wheel", "python-sdist"):
        caller.main(["consumer-config", kind])
        assert read(runner / (kind + "-tools.json")) == {
            "checkout": str(source),
            "scratch": str(runner / (kind + "-scratch")),
            "uv": environment["UV_EXECUTABLE"],
            "interpreter": environment["PYTHON_EXECUTABLE"],
            "helper": str(runner / "control/packages.py"),
            "deadlineSeconds": 180,
            "environment": {
                "PATH": "controlled-path",
                "DOTNET_ROOT": "controlled-dotnet",
            },
            "inheritEnvironment": False,
        }
        assert list((runner / (kind + "-scratch")).iterdir()) == []
    for operation in (
        ["tool-request"],
        ["index-config", "initial-read"],
        ["publisher-config"],
        ["consumer-config", "python-wheel"],
        ["phase", "verification"],
    ):
        with pytest.raises(FileExistsError):
            caller.main(operation)
    assert not Path(environment["UV_EXECUTABLE"]).exists()
    assert list((runner / "index-tool-scratch").iterdir()) == []
    assert list((runner / "publisher-scratch").iterdir()) == []


@pytest.mark.parametrize(
    ("states", "needed"),
    [
        (["Exact", "Exact"], False),
        (["Absent", "Exact"], True),
        (["Exact", "Absent"], True),
        (["Absent", "Absent"], True),
    ],
)
def test_state_reports_only_actual_missing_originals(native, states, needed):
    """State reports only actual missing originals."""
    runner, _, environment = native
    write(
        runner / "observation/observation.json",
        {
            "observation": {
                "subject": caller.subject(),
                "files": [{"state": state} for state in states],
            }
        },
    )
    caller.main(["state"])
    assert (
        Path(environment["GITHUB_OUTPUT"]).read_text()
        == "write-needed=" + str(needed).lower() + "\n"
    )
    value = read(runner / "observation/observation.json")
    value["observation"]["subject"]["revision"] = "b" * 40
    write(runner / "observation/observation.json", value)
    with pytest.raises(ValueError, match="subject mismatch"):
        caller.main(["state"])
    assert (
        Path(environment["GITHUB_OUTPUT"]).read_text().count("write-needed=")
        == 1
    )


@pytest.mark.parametrize(
    "args",
    [
        [],
        ["unknown"],
        ["validate"],
        ["consumer-config", "ruby-gem"],
        ["phase"],
        ["index-config"],
    ],
)
def test_unsupported_operations_have_no_effect(native, args):
    """Unsupported operations have no effect."""
    runner, source, _ = native
    with pytest.raises(ValueError, match="Unsupported"):
        caller.main(args)
    assert list(runner.iterdir()) == [runner / "control"]
    assert list(source.iterdir()) == []


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
