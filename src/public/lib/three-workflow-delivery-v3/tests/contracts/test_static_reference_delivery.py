"""Repository delivery contracts for the current static-reference route."""

from __future__ import annotations

import json
import os
from pathlib import Path

import yaml

from .workflow_shell import executable, run_step

REPO_ROOT = Path(__file__).resolve().parents[6]
WORKFLOWS = (
    REPO_ROOT / ".github/workflows/workflow-delivery-v3-ci.yml",
    REPO_ROOT / ".github/workflows/workflow-delivery-v3-buddy-smoke.yml",
)


def test_buddy_workflow_delegates_static_reference_to_live(
    tmp_path: Path,
) -> None:
    """Prepare the authorities before the selected Live eligibility entry."""
    document = yaml.safe_load(WORKFLOWS[1].read_text(encoding="utf-8"))
    job = document["jobs"]["evaluate-live-eligibility"]
    assert job["if"] == "github.run_attempt == 1"
    steps = job["steps"]
    eligibility_positions = [
        index
        for index, step in enumerate(steps)
        if step.get("id") == "eligibility"
    ]
    assert len(eligibility_positions) == 1
    log = tmp_path / "commands.jsonl"
    executable(
        tmp_path / "bin" / "mise",
        "import json, sys\n"
        "from pathlib import Path\n"
        f"with Path({str(log)!r}).open('a', encoding='utf-8') as stream:\n"
        "    stream.write(json.dumps([Path(sys.argv[0]).name, "
        "*sys.argv[1:]]) + '\\n')\n",
    )
    for step in steps[: eligibility_positions[0]]:
        if "run" not in step:
            continue
        assert "if" not in step, "A preceding run step needs condition review"
        result = run_step(
            step,
            cwd=tmp_path,
            env={
                "PATH": f"{tmp_path / 'bin'}{os.pathsep}{os.environ['PATH']}",
                "GITHUB_RUN_ATTEMPT": "1",
            },
            bindings={},
            workflow=document,
            job=job,
        )
        assert result.returncode == 0, result.stderr
    commands = [json.loads(line) for line in log.read_text().splitlines()]
    assert any(
        command[:3] == ["mise", "run", "prepare:static-reference-authorities"]
        for command in commands
    )
