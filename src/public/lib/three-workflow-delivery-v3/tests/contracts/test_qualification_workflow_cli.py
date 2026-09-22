"""Current Bash producer arguments meet the actual qualification CLI parser."""

from __future__ import annotations

import json
import os
from pathlib import Path

import pytest
import yaml
from three_workflow_delivery_v3 import cli as cli_module

from .workflow_shell import executable, run_step

REPO_ROOT = Path(__file__).resolve().parents[6]
LIVE = ".github/workflows/workflow-delivery-v3-live-attempt.yml"
SIMULATION = ".github/workflows/workflow-delivery-v3-official-simulate.yml"

LIVE_PLAN = {
    "qualification_snapshot": ".wdv3/input/live qualification.json",
    "qualification_snapshot_digest": "sha256:" + "01" * 32,
    "qualification_snapshot_artifact_id": 7101,
    "qualification_snapshot_artifact_digest": "sha256:" + "02" * 32,
    "adapter_context": ".wdv3/input/live adapter.json",
    "adapter_context_digest": "sha256:" + "03" * 32,
    "adapter_context_artifact_id": 7102,
    "adapter_context_artifact_digest": "sha256:" + "04" * 32,
}
SIMULATION_PLAN = {
    "qualification_snapshot": ".wdv3/input/simulation qualification.json",
    "qualification_snapshot_digest": "sha256:" + "05" * 32,
    "qualification_snapshot_artifact_id": 7201,
    "qualification_snapshot_artifact_digest": "sha256:" + "06" * 32,
    "adapter_context": ".wdv3/input/simulation adapter.json",
    "adapter_context_digest": "sha256:" + "07" * 32,
    "adapter_context_artifact_id": 7202,
    "adapter_context_artifact_digest": "sha256:" + "08" * 32,
}
BUILD_SNAPSHOT = {
    "qualification_snapshot": ".wdv3/input/build qualification.json",
    "qualification_snapshot_digest": "sha256:" + "09" * 32,
    "qualification_snapshot_artifact_id": 7301,
    "qualification_snapshot_artifact_digest": "sha256:" + "0a" * 32,
}
BUILD_ARTIFACT = {
    **BUILD_SNAPSHOT,
    "adapter_context": ".wdv3/input/build adapter.json",
    "adapter_context_digest": "sha256:" + "0b" * 32,
    "adapter_context_artifact_id": 7302,
    "adapter_context_artifact_digest": "sha256:" + "0c" * 32,
    "release_artifact": ".wdv3/input/release artifact.json",
    "release_artifact_digest": "sha256:" + "0d" * 32,
    "release_artifact_artifact_id": 7303,
    "release_artifact_artifact_digest": "sha256:" + "0e" * 32,
    "tarball": ".wdv3/input/package fixture.tgz",
}


@pytest.mark.parametrize(
    (
        "workflow_path",
        "job_name",
        "step_name",
        "purpose",
        "target",
        "attempt",
        "expected",
    ),
    [
        pytest.param(
            LIVE,
            "project-test",
            "Run project-test mechanics",
            "live-release",
            "a" * 40,
            1,
            {
                **LIVE_PLAN,
                "release_command": "run-project-test",
                "output": ".wdv3/project-test-evidence.json",
                "repo_root": ".",
            },
            id="live-project-test",
        ),
        pytest.param(
            LIVE,
            "npm-artifact-qualification",
            "Run artifact-contents mechanics",
            "live-release",
            "a" * 40,
            1,
            {
                **BUILD_ARTIFACT,
                "release_command": "run-artifact-contents",
                "output": ".wdv3/artifact-contents-evidence.json",
            },
            id="live-artifact-contents",
        ),
        pytest.param(
            LIVE,
            "npm-artifact-qualification",
            "Form incomplete artifact-contents Evidence",
            "live-release",
            "a" * 40,
            1,
            {
                **BUILD_SNAPSHOT,
                "release_command": "form-incomplete-evidence",
                "obligation_id": "release:quality:npm-artifact-contents",
                "output_role": "artifact-contents-evidence",
                "output": ".wdv3/artifact-contents-evidence.json",
            },
            id="live-incomplete-contents",
        ),
        pytest.param(
            LIVE,
            "npm-artifact-qualification",
            "Run install-import mechanics",
            "live-release",
            "a" * 40,
            1,
            {
                **BUILD_ARTIFACT,
                "release_command": "run-install-import",
                "output": ".wdv3/install-import-evidence.json",
            },
            id="live-install-import",
        ),
        pytest.param(
            LIVE,
            "npm-artifact-qualification",
            "Form incomplete install-import Evidence",
            "live-release",
            "a" * 40,
            1,
            {
                **BUILD_SNAPSHOT,
                "release_command": "form-incomplete-evidence",
                "obligation_id": "release:quality:npm-install-import",
                "output_role": "install-import-evidence",
                "output": ".wdv3/install-import-evidence.json",
            },
            id="live-incomplete-install",
        ),
        pytest.param(
            SIMULATION,
            "project-test",
            "Run project-test mechanics",
            "release-simulation",
            "c" * 40,
            2,
            {
                **SIMULATION_PLAN,
                "release_command": "run-project-test",
                "output": ".wdv3/project-test-evidence.json",
                "repo_root": ".",
            },
            id="simulation-project-test",
        ),
        pytest.param(
            SIMULATION,
            "npm-artifact-qualification",
            "Run artifact-contents mechanics",
            "release-simulation",
            "c" * 40,
            2,
            {
                **BUILD_ARTIFACT,
                "release_command": "run-artifact-contents",
                "output": ".wdv3/artifact-contents-evidence.json",
            },
            id="simulation-artifact-contents",
        ),
        pytest.param(
            SIMULATION,
            "npm-artifact-qualification",
            "Form incomplete artifact-contents Evidence",
            "release-simulation",
            "c" * 40,
            2,
            {
                **BUILD_SNAPSHOT,
                "release_command": "form-incomplete-evidence",
                "obligation_id": "release:quality:npm-artifact-contents",
                "output_role": "artifact-contents-evidence",
                "output": ".wdv3/artifact-contents-evidence.json",
            },
            id="simulation-incomplete-contents",
        ),
        pytest.param(
            SIMULATION,
            "npm-artifact-qualification",
            "Run install-import mechanics",
            "release-simulation",
            "c" * 40,
            2,
            {
                **BUILD_ARTIFACT,
                "release_command": "run-install-import",
                "output": ".wdv3/install-import-evidence.json",
            },
            id="simulation-install-import",
        ),
        pytest.param(
            SIMULATION,
            "npm-artifact-qualification",
            "Form incomplete install-import Evidence",
            "release-simulation",
            "c" * 40,
            2,
            {
                **BUILD_SNAPSHOT,
                "release_command": "form-incomplete-evidence",
                "obligation_id": "release:quality:npm-install-import",
                "output_role": "install-import-evidence",
                "output": ".wdv3/install-import-evidence.json",
            },
            id="simulation-incomplete-install",
        ),
    ],
)
def test_qualification_workflow_invocation_reaches_current_parser(  # noqa: PLR0913
    tmp_path: Path,
    *,
    workflow_path: str,
    job_name: str,
    step_name: str,
    purpose: str,
    target: str,
    attempt: int,
    expected: dict[str, str | int],
) -> None:
    """Run each literal producer site without invoking a domain handler."""
    document = yaml.safe_load((REPO_ROOT / workflow_path).read_text())
    job = document["jobs"][job_name]
    step = next(step for step in job["steps"] if step.get("name") == step_name)
    command_log = tmp_path / "command.json"
    github_output = tmp_path / "github-output"
    recorder = """import json, os, sys
from pathlib import Path
args = sys.argv[1:]
command = args[args.index("release"):]
Path(os.environ["COMMAND_LOG"]).write_text(
    json.dumps({"argv": command, "cwd": os.getcwd()})
)
if command[:2] == ["release", "run-project-test"]:
    output = Path(command[command.index("--output") + 1])
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(b'{}\\n')
"""
    executable(tmp_path / "bin" / "uv", recorder)
    bindings = {
        "inputs.target-sha": "a" * 40,
        (
            "needs.plan-qualification.outputs."
            "qualification-snapshot-artifact-name"
        ): "live qualification.json",
        (
            "needs.plan-qualification.outputs.qualification-snapshot-digest"
        ): "sha256:" + "01" * 32,
        (
            "needs.plan-qualification.outputs."
            "qualification-snapshot-artifact-id"
        ): "7101",
        (
            "needs.plan-qualification.outputs."
            "qualification-snapshot-artifact-digest"
        ): "sha256:" + "02" * 32,
        (
            "needs.plan-qualification.outputs.adapter-context-artifact-name"
        ): "live adapter.json",
        ("needs.plan-qualification.outputs.adapter-context-digest"): "sha256:"
        + "03" * 32,
        (
            "needs.plan-qualification.outputs.adapter-context-artifact-id"
        ): "7102",
        (
            "needs.plan-qualification.outputs.adapter-context-artifact-digest"
        ): "sha256:" + "04" * 32,
        (
            "needs.plan-simulation.outputs.qualification-snapshot-artifact-name"
        ): "simulation qualification.json",
        (
            "needs.plan-simulation.outputs.qualification-snapshot-digest"
        ): "sha256:" + "05" * 32,
        (
            "needs.plan-simulation.outputs.qualification-snapshot-artifact-id"
        ): "7201",
        (
            "needs.plan-simulation.outputs."
            "qualification-snapshot-artifact-digest"
        ): "sha256:" + "06" * 32,
        (
            "needs.plan-simulation.outputs.adapter-context-artifact-name"
        ): "simulation adapter.json",
        ("needs.plan-simulation.outputs.adapter-context-digest"): "sha256:"
        + "07" * 32,
        ("needs.plan-simulation.outputs.adapter-context-artifact-id"): "7202",
        (
            "needs.plan-simulation.outputs.adapter-context-artifact-digest"
        ): "sha256:" + "08" * 32,
        (
            "needs.build-tarball.outputs.qualification-snapshot-artifact-name"
        ): "build qualification.json",
        ("needs.build-tarball.outputs.qualification-snapshot-digest"): "sha256:"
        + "09" * 32,
        (
            "needs.build-tarball.outputs.qualification-snapshot-artifact-id"
        ): "7301",
        (
            "needs.build-tarball.outputs.qualification-snapshot-artifact-digest"
        ): "sha256:" + "0a" * 32,
        (
            "needs.build-tarball.outputs.adapter-context-artifact-name"
        ): "build adapter.json",
        ("needs.build-tarball.outputs.adapter-context-digest"): "sha256:"
        + "0b" * 32,
        ("needs.build-tarball.outputs.adapter-context-artifact-id"): "7302",
        (
            "needs.build-tarball.outputs.adapter-context-artifact-digest"
        ): "sha256:" + "0c" * 32,
        (
            "needs.build-tarball.outputs.release-artifact-artifact-name"
        ): "release artifact.json",
        ("needs.build-tarball.outputs.release-artifact-digest"): "sha256:"
        + "0d" * 32,
        ("needs.build-tarball.outputs.release-artifact-artifact-id"): "7303",
        (
            "needs.build-tarball.outputs.release-artifact-artifact-digest"
        ): "sha256:" + "0e" * 32,
        (
            "needs.build-tarball.outputs.tarball-artifact-name"
        ): "package fixture.tgz",
    }
    result = run_step(
        step,
        cwd=tmp_path,
        env={
            "PATH": f"{tmp_path / 'bin'}{os.pathsep}{os.environ['PATH']}",
            "GITHUB_RUN_ID": "9031",
            "GITHUB_RUN_ATTEMPT": str(attempt),
            "GITHUB_SHA": "b" * 40 if workflow_path == LIVE else "c" * 40,
            "GITHUB_OUTPUT": str(github_output),
            "COMMAND_LOG": str(command_log),
        },
        bindings=bindings,
        workflow=document,
        job=job,
    )
    assert result.returncode == 0, result.stderr
    captured = json.loads(command_log.read_text())
    arguments = cli_module._parser().parse_args(captured["argv"])  # noqa: SLF001
    expected_arguments = {
        "context": "release",
        "workflow_run_id": 9031,
        "run_attempt": attempt,
        "target": target,
        "purpose": purpose,
        "github_output": str(github_output),
        **expected,
    }
    assert {
        key: getattr(arguments, key) for key in expected_arguments
    } == expected_arguments
    assert Path(captured["cwd"]) == tmp_path
    if "repo_root" in expected:
        assert (
            Path(captured["cwd"]) / arguments.repo_root
        ).resolve() == tmp_path
