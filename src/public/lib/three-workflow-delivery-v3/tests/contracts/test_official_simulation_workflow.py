"""Official dry-run caller boundaries and actual selected request generation."""

from __future__ import annotations

import json
import os
import subprocess
import sys
from pathlib import Path
from typing import Any

import yaml

REPO_ROOT = Path(__file__).resolve().parents[6]
WORKFLOW = (
    REPO_ROOT / ".github/workflows/workflow-delivery-v3-official-simulate.yml"
)
RETENTION_DAYS = 45


def _document() -> dict[str, Any]:
    return yaml.safe_load(WORKFLOW.read_text(encoding="utf-8"))


def test_selected_development_request_is_generated_from_native_context(
    tmp_path: Path,
) -> None:
    """Execute the actual request producer for a development ref."""
    steps = _document()["jobs"]["simulate"]["steps"]
    script = next(step["run"] for step in steps if step.get("id") == "prepare")
    python = script.split("python3 - <<'PYTHON'\n", 1)[1].split("\nPYTHON", 1)[
        0
    ]
    (tmp_path / "release-results").mkdir()
    environment = dict(
        os.environ,
        RUNNER_TEMP=str(tmp_path),
        GITHUB_SHA="b" * 40,
        GITHUB_REF="refs/heads/development",
        GITHUB_WORKSPACE=str(REPO_ROOT),
    )
    # Execute only the selected repository's request-generation fixture.
    completed = subprocess.run(  # noqa: S603
        [sys.executable, "-c", python],
        env=environment,
        capture_output=True,
        text=True,
        check=False,
    )
    assert completed.returncode == 0, completed.stderr
    request = json.loads(
        (tmp_path / "release-results/request.json").read_text()
    )
    assert request["subject"] == {
        "revision": "b" * 40,
        "selectedRef": "refs/heads/development",
        "unit": "hcoona-release-smoke-npm",
        "declaration": (
            "tests/private/app/workflow-delivery/fixtures/products/"
            "hcoona-release-smoke-npm/workflow-delivery.release-unit.yml"
        ),
        "channel": "official",
        "destination": "npmjs",
        "dryRun": True,
    }
    assert request["checkout"] == str(REPO_ROOT)
    assert request["scratch"] == str(tmp_path / "release-originals")


def test_dry_run_has_no_write_gate_and_retains_complete_journey() -> None:
    """Complete qualification precedes observation and hypothetical results."""
    document = _document()
    assert document.get("on", document.get(True)) == {"workflow_dispatch": None}
    assert document["permissions"] == {"contents": "read"}
    for job in document["jobs"].values():
        assert "environment" not in job
        assert "permissions" not in job
    steps = document["jobs"]["simulate"]["steps"]
    commands = [step["run"] for step in steps if "run" in step]
    control = next(
        index
        for index, command in enumerate(commands)
        if "dotnet build" in command
    )
    preparation = next(
        index
        for index, command in enumerate(commands)
        if "release prepare-npm" in command
    )
    observation = next(
        index
        for index, command in enumerate(commands)
        if "release observe-npmjs" in command
    )
    result = next(
        index
        for index, command in enumerate(commands)
        if "release describe-npmjs" in command
    )
    assert control < preparation < observation < result
    checkout = next(
        step
        for step in steps
        if step.get("uses", "").startswith("actions/checkout@")
    )
    assert checkout["with"] == {
        "fetch-depth": 0,
        "persist-credentials": False,
        "ref": "${{ github.sha }}",
    }
    dependencies = next(
        command
        for command in commands
        if "install --frozen-lockfile" in command
    )
    assert "--ignore-scripts" in dependencies
    assert "env -i" in dependencies
    assert "--npmrc-auth-file" in dependencies
    upload = next(
        step
        for step in steps
        if step.get("uses", "").startswith("actions/upload-artifact@")
    )
    assert upload["if"] == "always()"
    assert upload["with"]["retention-days"] == RETENTION_DAYS
    assert upload["with"]["if-no-files-found"] == "error"
    assert "release-results/*.json" in upload["with"]["path"]
    assert "release-originals/original/package.tgz" in upload["with"]["path"]
    raw = WORKFLOW.read_text()
    for retired in (
        "Qualification Snapshot",
        "Repository Model",
        "Release Intent",
        "admit-intent",
        "continue-on-error",
        "GITHUB_TOKEN",
        "id-token:",
        "packages:",
    ):
        assert retired not in raw
