"""Native npm Buddy caller boundaries."""

from __future__ import annotations

import json
from pathlib import Path

import pytest
import yaml

from .workflow_shell import run_step

REPO_ROOT = Path(__file__).resolve().parents[6]
CALLER = REPO_ROOT / ".github/workflows/workflow-delivery-v3-buddy-smoke.yml"


def _workflow() -> dict:
    return yaml.safe_load(CALLER.read_text(encoding="utf-8"))


def test_actual_publisher_owns_native_environment_and_write_capability() -> (
    None
):
    """Approval protects publication; consumers have read capability."""
    workflow = _workflow()
    jobs = workflow["jobs"]
    assert workflow["permissions"] == {"contents": "read"}
    assert (
        jobs["publish"]["environment"] == "workflow-delivery-v3-buddy-approval"
    )
    assert jobs["publish"]["permissions"] == {
        "contents": "read",
        "packages": "write",
    }
    for name in ("observe", "verify"):
        assert jobs[name]["permissions"] == {
            "contents": "read",
            "packages": "read",
        }
        assert "environment" not in jobs[name]
    assert "inputs.dry-run == false" in jobs["publish"]["if"]
    assert "write-needed == 'true'" in jobs["publish"]["if"]
    assert "needs.publish.result == 'skipped'" in jobs["verify"]["if"]
    assert not workflow["concurrency"]["cancel-in-progress"]
    assert "github.ref" not in workflow["concurrency"]["group"]
    assert "version" not in workflow["concurrency"]["group"]


def test_selected_control_transfers_before_product_and_by_original_id() -> None:
    """A receiver consumes the actual immutable producer, not a display name."""
    jobs = _workflow()["jobs"]
    steps = jobs["prepare"]["steps"]
    names = [step["name"] for step in steps]
    assert (
        names.index("Build control before product execution")
        < names.index("Transfer selected control before product execution")
        < names.index("Prepare frozen dependencies without product scripts")
    )
    assert (
        "github.sha"
        in next(step for step in steps if "checkout@" in step.get("uses", ""))[
            "with"
        ]["ref"]
    )
    for name in ("observe", "publish", "verify"):
        downloads = [
            step
            for step in jobs[name]["steps"]
            if "download-artifact@" in step.get("uses", "")
        ]
        assert downloads
        assert all(
            "artifact-ids" in step["with"] and "name" not in step["with"]
            for step in downloads
        )
        assert all(
            step["with"]["digest-mismatch"] == "error" for step in downloads
        )
        assert any(
            "producer.json" in step.get("run", "")
            and "GITHUB_SHA" in step["run"]
            for step in jobs[name]["steps"]
        )
    assert not (
        REPO_ROOT / ".github/workflows/workflow-delivery-v3-live-attempt.yml"
    ).exists()


@pytest.mark.parametrize(
    ("dry_run", "attempt", "success"),
    [("true", "2", True), ("false", "1", True), ("false", "2", False)],
)
def test_live_rerun_guard_executes_actual_shell(
    tmp_path: Path, dry_run: str, attempt: str, *, success: bool
) -> None:
    """Allow development dry run and stop unsupported live rerun."""
    step = _workflow()["jobs"]["prepare"]["steps"][0]
    result = run_step(
        step,
        cwd=tmp_path,
        env={"GITHUB_RUN_ATTEMPT": attempt},
        bindings={"inputs.dry-run": dry_run},
    )
    assert (result.returncode == 0) == success


@pytest.mark.parametrize(
    ("state", "write"), [("Absent", "true"), ("Exact", "false")]
)
def test_actual_observation_output_drives_gate_choice(
    tmp_path: Path, state: str, write: str
) -> None:
    """The native original observation selects the necessary write gate."""
    directory = tmp_path / "observation"
    directory.mkdir()
    (directory / "observation.json").write_text(
        json.dumps({"observation": {"files": [{"state": state}]}})
    )
    output = tmp_path / "output"
    step = next(
        step
        for step in _workflow()["jobs"]["observe"]["steps"]
        if step.get("id") == "state"
    )
    result = run_step(
        step,
        cwd=tmp_path,
        env={"RUNNER_TEMP": str(tmp_path), "GITHUB_OUTPUT": str(output)},
        bindings={"inputs.dry-run": "false"},
    )
    assert result.returncode == 0, result.stderr
    assert output.read_text() == f"write-needed={write}\n"


def test_remote_consumer_is_required_and_outside_publisher() -> None:
    """Require scoped installation for the selected request."""
    jobs = _workflow()["jobs"]
    publisher = "\n".join(
        step.get("run", "") for step in jobs["publish"]["steps"]
    )
    verify = "\n".join(step.get("run", "") for step in jobs["verify"]["steps"])
    assert "release publish-github-npm" in publisher
    assert "release consume-github-npm" not in publisher
    assert "release consume-github-npm" in verify
    assert (
        verify.index("before-consumer.json")
        < verify.index("release consume-github-npm")
        < verify.index("after-consumer.json")
        < verify.index("release complete-github-npm")
    )
    assert "continue-on-error" not in CALLER.read_text()
    assert "approvalMirror" not in CALLER.read_text()
