"""Run pytest nodeids referenced by the workflow-release acceptance matrix."""

from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path
from typing import Any

REPO_ROOT = Path(__file__).parents[2]
MATRIX_PATHS = (
    REPO_ROOT / "tests/fixtures/workflow-release-acceptance-matrix.json",
    REPO_ROOT
    / "tests/fixtures/workflow-release-ci-validation-acceptance-matrix.json",
)


def _collect_test_nodeids(document: dict[str, Any]) -> list[str]:
    """Return acceptance evidence pytest nodeids in deterministic order."""
    nodeids = {
        "tests/test_workflow_release_control.py::"
        "test_acceptance_matrix_fixture_tracks_design_scenarios",
        "tests/test_workflow_release_control.py::"
        "test_acceptance_matrix_rows_are_ci_actionable",
        "tests/test_workflow_release_control.py::"
        "test_acceptance_matrix_test_nodeids_are_collected_by_gate",
        "tests/test_workflow_release_control.py::"
        "test_ci_acceptance_matrix_fixture_tracks_lld_scenarios",
        "tests/test_workflow_release_control.py::"
        "test_ci_acceptance_matrix_rows_are_actionable",
        "tests/test_workflow_release_control.py::"
        "test_ci_acceptance_matrix_preserves_no_publish_boundaries",
        "tests/test_workflow_release_control.py::"
        "test_confirmed_scope_descriptor_matrix_matches_current_descriptors",
        "tests/test_workflow_release_control.py::"
        "test_fail_closed_acceptance_rows_match_phase_artifact_contracts",
        "tests/test_workflow_release_control.py::"
        "test_hk_runs_focused_workflow_release_validation",
        "tests/test_workflow_release_control.py::"
        "test_ci_validate_workflow_passes_actionlint_gate",
        "tests/test_workflow_release_control.py::"
        "test_acceptance_gate_rejects_option_like_nodeids_and_uses_separator",
        "tests/test_workflow_release_control.py::"
        "test_official_entry_publish_sets_up_npm_trusted_runtime",
    }
    for row in document["rows"]:
        for references in row["evidence"].values():
            for reference in references:
                if reference["type"] == "test":
                    nodeids.add(reference["value"])
    _validate_nodeids(nodeids)
    return sorted(nodeids)


def _validate_nodeids(nodeids: set[str]) -> None:
    """Reject pytest nodeids that could be parsed as options."""
    invalid = sorted(nodeid for nodeid in nodeids if nodeid.startswith("-"))
    if invalid:
        message = (
            f"acceptance matrix contains option-like test nodeids: {invalid}"
        )
        raise ValueError(message)


def _pytest_command(nodeids: list[str]) -> list[str]:
    """Build the pytest command for acceptance evidence tests."""
    return [
        sys.executable,
        "-m",
        "pytest",
        "--import-mode=importlib",
        "--",
        *nodeids,
    ]


def main() -> int:
    """Run the acceptance evidence pytest nodeids."""
    documents = [
        json.loads(matrix_path.read_text(encoding="utf-8"))
        for matrix_path in MATRIX_PATHS
    ]
    merged_document = {
        "rows": [row for document in documents for row in document["rows"]],
    }
    try:
        nodeids = _collect_test_nodeids(merged_document)
    except ValueError as exc:
        print(str(exc), file=sys.stderr)
        return 2
    return subprocess.run(
        _pytest_command(nodeids),
        cwd=REPO_ROOT,
        check=False,
    ).returncode


if __name__ == "__main__":
    raise SystemExit(main())
