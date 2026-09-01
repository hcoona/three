"""Repository delivery contracts for the current static-reference route."""

from __future__ import annotations

from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[6]
WORKFLOWS = (
    REPO_ROOT / ".github/workflows/workflow-delivery-v3-ci.yml",
    REPO_ROOT / ".github/workflows/workflow-delivery-v3-buddy-smoke.yml",
)


def test_workflow_delivery_workflows_do_not_pass_consumer_policy() -> None:
    """Keep the removed option out of both delivery workflows."""
    documents = {
        path.name: path.read_text(encoding="utf-8") for path in WORKFLOWS
    }

    assert set(documents) == {
        "workflow-delivery-v3-ci.yml",
        "workflow-delivery-v3-buddy-smoke.yml",
    }
    assert all("--consumer-policy" not in text for text in documents.values())
    assert all("consumer_policy" not in text for text in documents.values())


def test_buddy_workflow_delegates_static_reference_to_live() -> None:
    """Let Live Eligibility own its exact-target scan."""
    workflow = WORKFLOWS[1].read_text(encoding="utf-8")
    evaluation_start = workflow.index(
        "- name: Evaluate fixed-source live eligibility"
    )
    evaluation_end = workflow.index(
        "\n      - name:",
        evaluation_start + 1,
    )
    evaluation_step = workflow[evaluation_start:evaluation_end]

    assert "mise run prepare:static-reference-authorities" in workflow
    assert (
        "three-workflow-delivery-v3 release evaluate-live-eligibility"
        in evaluation_step
    )
    assert '--target "${GITHUB_SHA}"' in evaluation_step
    assert "workflow_delivery_v3_static_reference.py" not in evaluation_step
    assert "--source-kind" not in evaluation_step
    assert "--consumer-policy" not in evaluation_step


def test_ci_workflow_has_no_worktree_static_reference_route() -> None:
    """Reserve worktree scanning for the manual mise task."""
    workflow = WORKFLOWS[0].read_text(encoding="utf-8")
    execute_start = workflow.index(
        "- name: Run permanent root HK and static-reference policy"
    )
    execute_end = workflow.index("\n      - name:", execute_start + 1)
    execute_step = workflow[execute_start:execute_end]

    assert "mise exec -- hk --no-progress check" in execute_step
    assert "mise exec -- hk --no-progress check --all" in execute_step
    assert "--source-kind worktree" not in execute_step
    assert "check:static-reference-worktree" not in execute_step
    assert "--consumer-policy" not in execute_step
