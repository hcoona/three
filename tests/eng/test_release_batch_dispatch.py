"""Batch Release dispatch scenarios against a substituted gh."""

from __future__ import annotations

import json
import os
import subprocess
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "eng/scripts/release_batch_dispatch.py"

# Serves scripted dispatch responses per workflow and status sequences per run.
FAKE_GH = r"""
import json
import os
import sys

state_path = os.environ["FAKE_GH_STATE"]
with open(state_path, encoding="utf-8") as stream:
    state = json.load(stream)
arguments = sys.argv[1:]
body = json.loads(sys.stdin.read()) if "--input" in arguments else None
with open(os.environ["FAKE_GH_LOG"], "a", encoding="utf-8") as stream:
    stream.write(json.dumps({"arguments": arguments, "body": body}) + "\n")
path = next(a for a in arguments if a.startswith("repos/"))
if path.endswith("/dispatches"):
    workflow = path.split("/")[-2]
    response = state["dispatch"][workflow]
    if "error" in response:
        sys.exit(response["error"])
    print(json.dumps(response))
else:
    run_id = path.rsplit("/", 1)[1]
    sequence = state["runs"][run_id]
    current = sequence.pop(0) if len(sequence) > 1 else sequence[0]
    with open(state_path, "w", encoding="utf-8") as stream:
        json.dump(state, stream)
    if "error" in current:
        sys.exit(current["error"])
    print(json.dumps(current))
"""

PYTHON = (
    "python:unit=hcoona-release-smoke-python,registry=testpypi,dry-run=true"
)
RUBY = (
    "ruby:destination=github-packages,unit=hcoona-release-smoke-ruby,"
    "dry-run=true"
)
NPM = "npm:registry=github-packages,unit=hcoona-release-smoke-npm,dry-run=true"
QUEUED = {"status": "queued", "conclusion": None}
RUNNING = {"status": "in_progress", "conclusion": None}


def done(conclusion):
    """Return a completed run with the given conclusion."""
    return {"status": "completed", "conclusion": conclusion}


def created(run_id):
    """Return GitHub's dispatch response with run details."""
    return {
        "workflow_run_id": run_id,
        "run_url": f"https://api.github.com/repos/o/r/actions/runs/{run_id}",
        "html_url": f"https://github.com/o/r/actions/runs/{run_id}",
    }


def batch(tmp_path, state, *arguments):
    """Run the entry point with a fake gh first on PATH."""
    bin_directory = tmp_path / "bin"
    bin_directory.mkdir()
    fake = bin_directory / "gh"
    fake.write_text(f"#!{sys.executable}\n{FAKE_GH}", encoding="utf-8")
    fake.chmod(0o755)
    (tmp_path / "state.json").write_text(json.dumps(state), encoding="utf-8")
    log = tmp_path / "gh.jsonl"
    log.touch()
    environment = {
        **os.environ,
        "PATH": f"{bin_directory}{os.pathsep}{os.environ['PATH']}",
        "FAKE_GH_STATE": str(tmp_path / "state.json"),
        "FAKE_GH_LOG": str(log),
    }
    completed = subprocess.run(  # noqa: S603
        [sys.executable, SCRIPT, "--poll-interval", "0", *arguments],
        capture_output=True,
        text=True,
        env=environment,
        check=False,
    )
    calls = [json.loads(line) for line in log.read_text().splitlines()]
    return completed, calls


def summary(stdout):
    """Map each summary row's run ID to its conclusion."""
    rows = stdout.split("Run ID", 1)[1].splitlines()[1:]
    return {
        row.split()[0]: row.split()[1]
        for row in rows
        if row and not row.startswith(" ")
    }


pytestmark = pytest.mark.skipif(
    sys.platform == "win32", reason="the substituted gh is a POSIX executable"
)


def test_dispatches_each_entry_once_and_waits_for_every_run(tmp_path):
    """Each entry becomes one identified run, awaited to its conclusion."""
    state = {
        "dispatch": {
            "workflow-delivery-v3-python-release.yml": created(101),
            "workflow-delivery-v3-ruby-release.yml": created(102),
            "workflow-delivery-v3-npm-release.yml": created(103),
        },
        "runs": {
            "101": [QUEUED, RUNNING, done("success")],
            "102": [RUNNING, RUNNING, RUNNING, done("success")],
            "103": [done("success")],
        },
    }
    completed, calls = batch(
        tmp_path, state, "--ref", "main", PYTHON, RUBY, NPM
    )

    assert completed.returncode == 0, completed.stderr
    dispatches = [call for call in calls if call["body"]]
    assert [call["body"] for call in dispatches] == [
        {
            "ref": "main",
            "inputs": {
                "unit": "hcoona-release-smoke-python",
                "registry": "testpypi",
                "dry-run": "true",
            },
            "return_run_details": True,
        },
        {
            "ref": "main",
            "inputs": {
                "destination": "github-packages",
                "unit": "hcoona-release-smoke-ruby",
                "dry-run": "true",
            },
            "return_run_details": True,
        },
        {
            "ref": "main",
            "inputs": {
                "registry": "github-packages",
                "unit": "hcoona-release-smoke-npm",
                "dry-run": "true",
            },
            "return_run_details": True,
        },
    ]
    assert all(
        call["arguments"][:3] == ["api", "--method", "POST"]
        for call in dispatches
    )
    assert summary(completed.stdout) == {
        "101": "success",
        "102": "success",
        "103": "success",
    }
    for run_id in ("101", "102", "103"):
        assert (
            f"https://github.com/o/r/actions/runs/{run_id}" in completed.stdout
        )


def test_one_failure_does_not_stop_the_other_runs(tmp_path):
    """A rejected dispatch or failed run leaves the others running."""
    state = {
        "dispatch": {
            "workflow-delivery-v3-python-release.yml": created(201),
            "workflow-delivery-v3-ruby-release.yml": {
                "error": "HTTP 422: bad ref"
            },
            "workflow-delivery-v3-npm-release.yml": created(203),
        },
        "runs": {
            "201": [RUNNING, done("failure")],
            "203": [QUEUED, RUNNING, RUNNING, done("success")],
        },
    }
    completed, _ = batch(tmp_path, state, "--ref", "main", PYTHON, RUBY, NPM)

    assert completed.returncode == 1
    assert summary(completed.stdout) == {
        "201": "failure",
        "-": "not-dispatched",
        "203": "success",
    }
    assert "HTTP 422: bad ref" in completed.stdout


@pytest.mark.parametrize(
    ("dispatch", "runs", "conclusion"),
    [
        ({"run_url": "only"}, {}, "not-dispatched"),
        (created(301), {"301": [RUNNING, {"error": "HTTP 502"}]}, "unknown"),
        (created(301), {"301": [done("cancelled")]}, "cancelled"),
    ],
    ids=["unidentified-run", "unreadable-run", "cancelled-run"],
)
def test_runs_without_a_known_success_fail_the_batch(
    tmp_path, dispatch, runs, conclusion
):
    """Only a known success counts; unknown identity or state fails."""
    state = {
        "dispatch": {"workflow-delivery-v3-python-release.yml": dispatch},
        "runs": runs,
    }
    completed, _ = batch(tmp_path, state, "--ref", "main", PYTHON)

    assert completed.returncode == 1
    assert list(summary(completed.stdout).values()) == [conclusion]


@pytest.mark.parametrize(
    "invalid",
    [
        "pypi:unit=nbgv-python,registry=pypi,dry-run=true",
        "python:unit=nbgv-python,registry=testpypi",
        "python:unit=nbgv-python,registry=npmjs,dry-run=true",
        "python:unit=nbgv-python,registry=testpypi,dry-run=yes",
        "python:unit=nbgv-python,registry=testpypi,dry-run=true,channel=buddy",
        "python:unit=nbgv-python,unit=nbgv-python,registry=testpypi,dry-run=true",
        "python:unit=nbgv-python,registry=testpypi,dry-run",
        "python",
    ],
    ids=[
        "unknown-caller",
        "implicit-dry-run",
        "unknown-choice",
        "non-boolean",
        "undeclared-input",
        "duplicate-input",
        "missing-value",
        "no-inputs",
    ],
)
def test_invalid_entry_dispatches_nothing(tmp_path, invalid):
    """Any invalid entry stops the batch before the first dispatch."""
    completed, calls = batch(
        tmp_path, {"dispatch": {}, "runs": {}}, "--ref", "main", PYTHON, invalid
    )

    assert completed.returncode == 2
    assert "nothing was dispatched" in completed.stderr
    assert calls == []


def test_entries_follow_every_release_caller_input_shape(tmp_path):
    """Callers with other input names are validated from their workflows."""
    entries = [
        "browser-store:store=firefox-amo,channel=buddy,dry-run=true",
        (
            "github:destination=github-release-prerelease,"
            "unit=image-occlusion-editor,dry-run=true"
        ),
        "nuget:registry=github-packages,unit=memoization,dry-run=true",
    ]
    state = {
        "dispatch": {
            "workflow-delivery-v3-browser-store-release.yml": created(401),
            "workflow-delivery-v3-github-release.yml": created(402),
            "workflow-delivery-v3-nuget-release.yml": created(403),
        },
        "runs": {run_id: [done("success")] for run_id in ("401", "402", "403")},
    }
    completed, calls = batch(tmp_path, state, "--ref", "feature", *entries)

    assert completed.returncode == 0, completed.stderr
    assert [call["arguments"][3] for call in calls if call["body"]] == [
        "repos/{owner}/{repo}/actions/workflows/"
        f"workflow-delivery-v3-{caller}-release.yml/dispatches"
        for caller in ("browser-store", "github", "nuget")
    ]
