"""Current Workflow Delivery v3 static-reference integration scenarios."""

from __future__ import annotations

import json
import shutil
import subprocess
import sys
from dataclasses import dataclass
from functools import cache
from pathlib import Path
from typing import Any, cast

REPO_ROOT = Path(__file__).resolve().parents[6]
BUDDY_WORKFLOW = (
    REPO_ROOT / ".github/workflows/workflow-delivery-v3-buddy-smoke.yml"
)


def test_live_scenario_has_no_external_or_caller_supplied_policy_result() -> (
    None
):
    """Let the evaluator form exact-target evidence in the current run."""
    workflow = BUDDY_WORKFLOW.read_text(encoding="utf-8")

    assert "Prepare static-reference authorities" in workflow
    assert "three-workflow-delivery-v3 release evaluate-live-eligibility" in (
        workflow
    )
    assert '--target "${GITHUB_SHA}"' in workflow
    assert "workflow_delivery_v3_static_reference.py" not in workflow
    assert "--consumer-policy" not in workflow


def test_manual_worktree_scenario_is_isolated_in_mise() -> None:
    """Expose worktree feedback only through its explicit manual task."""
    mise = (REPO_ROOT / "mise.toml").read_text(encoding="utf-8")
    hk = (REPO_ROOT / "hk.pkl").read_text(encoding="utf-8")
    task_start = mise.index('[tasks."check:static-reference-worktree"]')
    task_end = mise.index("\n\n", task_start)
    task = mise[task_start:task_end]

    assert "--source-kind worktree" in task
    assert "prepare:static-reference-authorities" in task
    assert "check:static-reference-worktree" not in hk
    assert "--source-kind worktree" not in hk


HK_CONFIG = REPO_ROOT / "hk.pkl"
HK_SUPPORT = REPO_ROOT / "src/private/lib/hk"
HK_RANGE_HELPER = Path("eng/scripts/workflow_delivery_v3_hk.py")
STATIC_REFERENCE_STEP_NAME = "hcoona-release-smoke-npm-static-reference"

GIT_TRANSITIONS = ("add", "modify", "delete", "rename-out", "rename-in")


@dataclass(frozen=True, slots=True)
class HistoryChange:
    """One real Git-history change used to exercise an HK trigger."""

    kind: str
    path: str
    old_path: str | None = None


def _run(
    command: tuple[str, ...],
    *,
    cwd: Path,
) -> subprocess.CompletedProcess[str]:
    return subprocess.run(  # noqa: S603
        command,
        cwd=cwd,
        check=True,
        capture_output=True,
        text=True,
    )


def _git(repo: Path, *arguments: str) -> subprocess.CompletedProcess[str]:
    return _run(("git", *arguments), cwd=repo)


def _write(repo: Path, relative_path: str, content: bytes | str) -> None:
    path = repo / relative_path
    path.parent.mkdir(parents=True, exist_ok=True)
    if isinstance(content, bytes):
        path.write_bytes(content)
    else:
        path.write_text(content, encoding="utf-8")


def _commit(repo: Path, message: str) -> str:
    _git(repo, "add", "--all")
    _git(repo, "commit", "--quiet", "--message", message)
    return _git(repo, "rev-parse", "HEAD").stdout.strip()


def _initialize_hk_repository(
    repo: Path,
    *,
    baseline_paths: tuple[str, ...] = (),
) -> str:
    repo.mkdir()
    shutil.copy2(HK_CONFIG, repo / "hk.pkl")
    shutil.copytree(HK_SUPPORT, repo / "src/private/lib/hk")
    helper = repo / HK_RANGE_HELPER
    helper.parent.mkdir(parents=True)
    shutil.copy2(REPO_ROOT / HK_RANGE_HELPER, helper)
    for path in baseline_paths:
        _write(repo, path, "baseline\n")
    _git(repo, "init", "--quiet")
    _git(repo, "config", "user.name", "Workflow Delivery Scenario")
    _git(
        repo,
        "config",
        "user.email",
        "workflow-delivery-scenario@example.invalid",
    )
    return _commit(repo, "baseline")


@cache
def _hk_executable() -> str:
    install_root = _run(
        ("mise", "where", "aqua:jdx/hk"),
        cwd=REPO_ROOT,
    ).stdout.strip()
    executable = Path(install_root) / "hk"
    version = _run((str(executable), "--version"), cwd=REPO_ROOT)
    active_version = _run(
        ("mise", "current", "aqua:jdx/hk"),
        cwd=REPO_ROOT,
    ).stdout.strip()
    assert version.stdout.strip() == f"hk {active_version}"
    return str(executable)


def _changed_paths(repo: Path, base: str, head: str) -> tuple[str, ...]:
    result = _run(
        (
            sys.executable,
            str(repo / HK_RANGE_HELPER),
            "--repository",
            str(repo),
            "--from-ref",
            base,
            "--to-ref",
            head,
        ),
        cwd=repo,
    )
    return tuple(json.loads(result.stdout))


def _hk_step_from_result(
    result: subprocess.CompletedProcess[str],
    step_name: str,
) -> dict[str, Any]:
    plan = cast("dict[str, Any]", json.loads(result.stdout))
    steps = cast("list[dict[str, Any]]", plan["steps"])

    assert plan["hook"] == "check"
    assert plan["runType"] == "check"
    assert "small" in cast("list[str]", plan["profiles"])
    assert len(steps) == 1
    assert steps[0]["name"] == step_name
    return steps[0]


def _hk_step_for_range(
    repo: Path,
    base: str,
    head: str,
    step_name: str,
) -> dict[str, Any]:
    result = _run(
        (
            sys.executable,
            str(repo / HK_RANGE_HELPER),
            "--repository",
            str(repo),
            "--from-ref",
            base,
            "--to-ref",
            head,
            "--",
            _hk_executable(),
            "--no-progress",
            "check",
            "--plan",
            "--json",
            "--step",
            step_name,
        ),
        cwd=repo,
    )
    return _hk_step_from_result(result, step_name)


def _hk_step_for_all(repo: Path, step_name: str) -> dict[str, Any]:
    result = _run(
        (
            _hk_executable(),
            "--no-progress",
            "check",
            "--plan",
            "--json",
            "--step",
            step_name,
            "--all",
        ),
        cwd=repo,
    )
    return _hk_step_from_result(result, step_name)


def _history_change(path: str, transition: str) -> HistoryChange:
    archive_path = "archive/static-reference-trigger.txt"
    if transition == "rename-out":
        return HistoryChange("rename", archive_path, old_path=path)
    if transition == "rename-in":
        return HistoryChange("rename", path, old_path=archive_path)
    return HistoryChange(transition, path)


def _apply_history_change(repo: Path, change: HistoryChange) -> None:
    if change.kind == "add":
        _write(repo, change.path, "added static-reference trigger\n")
    elif change.kind == "modify":
        _write(repo, change.path, "modified static-reference trigger\n")
    elif change.kind == "delete":
        (repo / change.path).unlink()
    elif change.kind == "rename":
        assert change.old_path is not None
        (repo / change.path).parent.mkdir(parents=True, exist_ok=True)
        _git(repo, "mv", change.old_path, change.path)
    else:
        message = f"unsupported scenario transition: {change.kind}"
        raise AssertionError(message)


def test_ci_scenario_static_reference_trigger_is_unconditional(
    tmp_path: Path,
) -> None:
    """Retain Git-transition coverage without a broad consumer glob."""
    surface_path = "unrelated/static-reference-trigger.txt"

    for transition in GIT_TRANSITIONS:
        change = _history_change(surface_path, transition)
        baseline_paths = (
            (change.old_path or change.path,)
            if change.kind in {"modify", "delete", "rename"}
            else ()
        )
        repo = tmp_path / transition
        base = _initialize_hk_repository(
            repo,
            baseline_paths=baseline_paths,
        )
        _apply_history_change(repo, change)
        head = _commit(repo, f"static-reference {transition}")
        paths = _changed_paths(repo, base, head)
        step = _hk_step_for_range(
            repo,
            base,
            head,
            STATIC_REFERENCE_STEP_NAME,
        )
        expected_paths = (
            (cast("str", change.old_path), change.path)
            if change.kind == "rename"
            else (change.path,)
        )

        assert paths == expected_paths
        assert step["name"] == STATIC_REFERENCE_STEP_NAME
        assert step["status"] == "included"
        assert step["fileCount"] == len(expected_paths)

    manual_repo = tmp_path / "slice-validation"
    _initialize_hk_repository(manual_repo)
    manual = _hk_step_for_all(manual_repo, STATIC_REFERENCE_STEP_NAME)

    assert manual["name"] == STATIC_REFERENCE_STEP_NAME
    assert manual["status"] == "included"
    assert cast("int", manual["fileCount"]) > 0
