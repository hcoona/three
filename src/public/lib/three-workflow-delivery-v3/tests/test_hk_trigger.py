"""Current root-HK and manual static-reference routing contracts."""

from __future__ import annotations

import ast
import importlib.util
import json
import shutil
import subprocess
import sys
from dataclasses import dataclass
from functools import cache
from pathlib import Path
from typing import TYPE_CHECKING, TypedDict

import pytest
import yaml

REPO_ROOT = Path(__file__).resolve().parents[5]
HK_CONFIG = REPO_ROOT / "hk.pkl"


def _hk_step_block(step_name: str) -> str:
    hk_config = HK_CONFIG.read_text(encoding="utf-8")
    start = hk_config.index(f'["{step_name}"]')
    end = hk_config.index("\n  }\n", start) + len("\n  }\n")
    return hk_config[start:end]


def _static_reference_hk_block() -> str:
    return _hk_step_block("hcoona-release-smoke-npm-static-reference")


def test_root_hk_unconditionally_invokes_static_reference_for_index() -> None:
    """Bind the permanent root-HK step to one explicit index scan."""
    static_step = _static_reference_hk_block()
    expected_invocation = (
        "python eng/scripts/hk_exec.py --timeout-seconds 300 "
        "uv run --python 3.13 --package three-workflow-delivery-v3 "
        "python eng/scripts/workflow_delivery_v3_static_reference.py "
        "--repository-root . --source-kind index"
    )

    assert f'check =\n      "{expected_invocation}"' in static_step
    assert (
        static_step.count(
            "eng/scripts/workflow_delivery_v3_static_reference.py"
        )
        == 1
    )
    assert static_step.count("--source-kind index") == 1
    assert "worktree" not in static_step
    assert "git-target" not in static_step
    assert "--target" not in static_step
    assert "when =" not in static_step


def test_manual_worktree_static_reference_is_a_separate_mise_task() -> None:
    """Keep manual worktree inspection out of permanent root HK."""
    mise_config = (REPO_ROOT / "mise.toml").read_text(encoding="utf-8")
    task_start = mise_config.index('[tasks."check:static-reference-worktree"]')
    task_end = mise_config.index("\n\n", task_start)
    task = mise_config[task_start:task_end]
    static_step = _static_reference_hk_block()

    assert 'description = "Check bounded static references' in task
    assert 'depends = ["prepare:static-reference-authorities"]' in task
    assert (
        "workflow_delivery_v3_static_reference.py "
        "--repository-root . --source-kind worktree"
    ) in task
    assert task.count("--source-kind worktree") == 1
    assert "worktree" not in static_step


def test_root_hk_static_reference_step_has_no_consumer_policy_route() -> None:
    """Do not retain the superseded root-HK policy step or option."""
    hk_config = HK_CONFIG.read_text(encoding="utf-8")
    static_step = _static_reference_hk_block()

    assert "hcoona-release-smoke-npm-consumer-policy" not in hk_config
    assert "workflow_delivery_v3_consumer_policy.py" not in hk_config
    assert "--consumer-policy" not in hk_config
    assert "--consumer-policy" not in static_step


def test_root_hk_live_static_reference_uses_git_target_evidence_only() -> None:
    """Keep root-HK feedback out of Live's exact-target evidence boundary."""
    static_step = _static_reference_hk_block()
    buddy_workflow = (
        REPO_ROOT / ".github/workflows/workflow-delivery-v3-buddy-smoke.yml"
    ).read_text(encoding="utf-8")
    eligibility_path = (
        REPO_ROOT / "src/public/lib/three-workflow-delivery-v3/src/"
        "three_workflow_delivery_v3/release/eligibility.py"
    )
    syntax = ast.parse(eligibility_path.read_text(encoding="utf-8"))
    evaluator = next(
        node
        for node in syntax.body
        if isinstance(node, ast.FunctionDef)
        and node.name == "evaluate_live_eligibility"
    )
    scan_calls = tuple(
        node
        for node in ast.walk(evaluator)
        if isinstance(node, ast.Call)
        and isinstance(node.func, ast.Name)
        and node.func.id == "scan_bounded_static_references"
    )

    assert "git-target" not in static_step
    assert "--target" not in static_step
    assert "workflow_delivery_v3_static_reference.py" not in buddy_workflow
    assert "three-workflow-delivery-v3 release evaluate-live-eligibility" in (
        buddy_workflow
    )
    assert '--target "${GITHUB_SHA}"' in buddy_workflow
    assert len(scan_calls) == 1
    keywords = {
        keyword.arg: keyword.value
        for keyword in scan_calls[0].keywords
        if keyword.arg is not None
    }
    assert ast.literal_eval(keywords["source_kind"]) == "git-target"
    assert ast.unparse(keywords["target"]) == "context.target"


if TYPE_CHECKING:
    from collections.abc import Sequence


_COMMIT9_CONTRACT_SPEC = importlib.util.spec_from_file_location(
    "_commit9_codeowners_contract",
    Path(__file__).parent / "contracts/test_commit9_codeowners.py",
)
assert _COMMIT9_CONTRACT_SPEC is not None
assert _COMMIT9_CONTRACT_SPEC.loader is not None
_COMMIT9_CONTRACT = importlib.util.module_from_spec(_COMMIT9_CONTRACT_SPEC)
sys.modules[_COMMIT9_CONTRACT_SPEC.name] = _COMMIT9_CONTRACT
_COMMIT9_CONTRACT_SPEC.loader.exec_module(_COMMIT9_CONTRACT)
SYNTHETIC_FUTURE_SURFACES = _COMMIT9_CONTRACT.SYNTHETIC_FUTURE_SURFACES
_governed_categories = _COMMIT9_CONTRACT._governed_categories  # noqa: SLF001
_governed_surface_inventory = (
    _COMMIT9_CONTRACT._governed_surface_inventory  # noqa: SLF001
)

HK_SUPPORT = REPO_ROOT / "src/private/lib/hk"
HK_RANGE_HELPER = Path("eng/scripts/workflow_delivery_v3_hk.py")
STEP_NAME = "v3-control-pytest"
PREPARATION_STEP_NAME = "static-reference-authority-preparation"
STATIC_REFERENCE_STEP_NAME = "hcoona-release-smoke-npm-static-reference"
STATIC_REFERENCE_IMPLEMENTATION = Path(
    "eng/scripts/workflow_delivery_v3_static_reference.py",
)
RETIRED_CONSUMER_POLICY_SURFACES = frozenset(
    {
        "eng/scripts/workflow_delivery_v3_consumer_policy.py",
        (
            "src/public/lib/three-workflow-delivery-v3/src/"
            "three_workflow_delivery_v3/release/consumer_policy.py"
        ),
        (
            "src/public/lib/three-workflow-delivery-v3/src/"
            "three_workflow_delivery_v3/release/javascript_consumer.py"
        ),
        (
            "src/public/lib/three-workflow-delivery-v3/tests/ci/"
            "test_consumer_policy.py"
        ),
        (
            "src/public/lib/three-workflow-delivery-v3/tests/fixtures/release/"
            "consumer-policy-acceptance.json"
        ),
    },
)
SCHOLARLY_STEP_NAME = "scholarly-publication-plugin-ci"
SCHOLARLY_SKILL_ROOTS = (
    ".agents/skills/scholarly-pdf-reconstruction",
    ".agents/skills/scholarly-print-assembly",
    ".agents/skills/scholarly-render-qa",
)
SCHOLARLY_SURFACE_PATHS = (
    ".agents/skills/scholarly-pdf-reconstruction/SKILL.md",
    ".agents/skills/scholarly-print-assembly/scripts/assemble_print.py",
    ".agents/skills/scholarly-render-qa/assets/release-manifest.schema.json",
    "apm.lock.yaml",
    "apm.yml",
    "mise.toml",
    "pyproject.toml",
    "src/private/lib/scholarly-publication/tests/test_validate_package.py",
)
GOVERNED_PATHS = (
    ".gitattributes",
    "Directory.Packages.props",
    "src/public/lib/three-workflow-delivery-v3/src/control.py",
    "src/public/app/example/workflow-delivery.release-unit.yml",
    "src/private/app/example/workflow-delivery.quality.yml",
    "eng/workflow-delivery/v3/policies/hcoona-release-smoke-npm.yml",
    ".github/workflow-delivery/governance/hcoona-release-smoke-npm.json",
    ".github/workflows/workflow-delivery-v3-ci.yml",
    ".github/workflows/workflow-delivery-v3-live-attempt.yaml",
    ".github/actions/workflow-delivery-v3-compile/action.yml",
    ".github/actions/workflow-delivery-v3/publish/action.yml",
    ".github/CODEOWNERS",
    "pyproject.toml",
    "uv.lock",
    ".python-version",
    "uv.toml",
    "pytest.ini",
    "ruff.toml",
    "pyrefly.toml",
    "conftest.py",
    "hk.pkl",
    "src/private/lib/hk/AddedTriggerFixture.pkl",
    "eng/scripts/hk_exec.py",
    "eng/scripts/workflow_delivery_v3_prepare_static_reference.py",
    "eng/scripts/workflow_delivery_v3_run_created_epoch.py",
    STATIC_REFERENCE_IMPLEMENTATION.as_posix(),
    "eng/scripts/workflow_delivery_v3_static_reference_node.mjs",
    HK_RANGE_HELPER.as_posix(),
    "src/private/app/workflow-delivery-v3-nuget-authority/Program.cs",
)


@dataclass(frozen=True, slots=True)
class HistoryChange:
    """One real Git-history change supplied to HK."""

    kind: str
    path: str
    old_path: str | None = None


class HkStepJson(TypedDict):
    """Relevant fields in one HK JSON plan step."""

    name: str
    status: str
    fileCount: int


class HkPlanJson(TypedDict):
    """Relevant fields in an HK JSON plan."""

    hook: str
    runType: str
    profiles: list[str]
    steps: list[HkStepJson]


def _run(
    command: Sequence[str],
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


def _write(repo: Path, relative_path: str, content: str) -> None:
    path = repo / relative_path
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")


def _commit(repo: Path, message: str) -> str:
    _git(repo, "add", "--all")
    _git(repo, "commit", "--quiet", "--message", message)
    return _git(repo, "rev-parse", "HEAD").stdout.strip()


def _initialize_repository(
    repo: Path,
    *,
    baseline_paths: Sequence[str] = (),
) -> str:
    repo.mkdir()
    shutil.copy2(HK_CONFIG, repo / "hk.pkl")
    shutil.copytree(HK_SUPPORT, repo / "src/private/lib/hk")
    helper = repo / HK_RANGE_HELPER
    helper.parent.mkdir(parents=True)
    shutil.copy2(REPO_ROOT / HK_RANGE_HELPER, helper)
    for path in baseline_paths:
        if not (repo / path).exists():
            _write(repo, path, "baseline\n")
    _git(repo, "init", "--quiet")
    _git(repo, "config", "user.name", "Workflow Delivery Test")
    _git(
        repo,
        "config",
        "user.email",
        "workflow-delivery@example.invalid",
    )
    return _commit(repo, "baseline")


def _initialize_empty_repository(repo: Path) -> str:
    repo.mkdir()
    _git(repo, "init", "--quiet")
    _git(repo, "config", "user.name", "Workflow Delivery Test")
    _git(
        repo,
        "config",
        "user.email",
        "workflow-delivery@example.invalid",
    )
    _git(repo, "commit", "--quiet", "--allow-empty", "--message", "baseline")
    return _git(repo, "rev-parse", "HEAD").stdout.strip()


@cache
def _hk_executable() -> str:
    install_root = _run(
        ("mise", "where", "hk"),
        cwd=REPO_ROOT,
    ).stdout.strip()
    executable = Path(install_root) / "hk"
    version = _run((str(executable), "--version"), cwd=REPO_ROOT)
    active_version = _run(
        ("mise", "current", "hk"),
        cwd=REPO_ROOT,
    ).stdout.strip()
    assert version.stdout.strip() == f"hk {active_version}"
    return str(executable)


def _named_step_from_plan(
    result: subprocess.CompletedProcess[str],
    step_name: str,
    required_profile: str = "small",
) -> HkStepJson:
    plan: HkPlanJson = json.loads(result.stdout)
    assert plan["hook"] == "check"
    assert plan["runType"] == "check"
    assert required_profile in plan["profiles"]
    assert len(plan["steps"]) == 1
    step = plan["steps"][0]
    assert step["name"] == step_name
    return step


def _step_from_plan(result: subprocess.CompletedProcess[str]) -> HkStepJson:
    return _named_step_from_plan(result, STEP_NAME)


def _step_plan(repo: Path, *arguments: str) -> HkStepJson:
    result = _run(
        (
            _hk_executable(),
            "--no-progress",
            "check",
            "--plan",
            "--json",
            "--step",
            STEP_NAME,
            *arguments,
        ),
        cwd=repo,
    )
    return _step_from_plan(result)


def _named_step_plan(
    repo: Path,
    step_name: str,
    *arguments: str,
    required_profile: str = "small",
) -> HkStepJson:
    result = _run(
        (
            _hk_executable(),
            "--no-progress",
            "check",
            "--plan",
            "--json",
            "--step",
            step_name,
            *arguments,
        ),
        cwd=repo,
    )
    return _named_step_from_plan(result, step_name, required_profile)


def _helper_changed_paths(
    repo: Path,
    base: str,
    head: str,
) -> tuple[str, ...]:
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
    paths: list[str] = json.loads(result.stdout)
    return tuple(paths)


def _named_helper_step_plan(
    repo: Path,
    base: str,
    head: str,
    step_name: str,
    *,
    required_profile: str = "small",
) -> HkStepJson:
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
    return _named_step_from_plan(result, step_name, required_profile)


def _helper_step_plan(repo: Path, base: str, head: str) -> HkStepJson:
    return _named_helper_step_plan(repo, base, head, STEP_NAME)


def _apply_change(repo: Path, change: HistoryChange) -> None:
    if change.kind == "add":
        _write(repo, change.path, "added\n")
    elif change.kind == "modify":
        _write(repo, change.path, "modified\n")
    elif change.kind == "delete":
        (repo / change.path).unlink()
    elif change.kind == "rename":
        assert change.old_path is not None
        (repo / change.path).parent.mkdir(parents=True, exist_ok=True)
        _git(repo, "mv", change.old_path, change.path)
    else:
        message = f"unsupported test change: {change.kind}"
        raise AssertionError(message)


def _execution_copies(repo: Path) -> dict[str, bytes]:
    operational_paths = {
        "hk.pkl",
        HK_RANGE_HELPER.as_posix(),
        *(
            path.relative_to(repo).as_posix()
            for path in (repo / "src/private/lib/hk").rglob("*")
            if path.is_file()
        ),
    }
    return {
        path: (repo / path).read_bytes() for path in sorted(operational_paths)
    }


def _restore_execution_copies(
    repo: Path,
    copies: dict[str, bytes],
) -> None:
    for relative_path, content in copies.items():
        path = repo / relative_path
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(content)


def _commit9_surfaces() -> tuple[str, ...]:
    return tuple(
        sorted(
            set(_governed_surface_inventory())
            - RETIRED_CONSUMER_POLICY_SURFACES,
        ),
    )


def _assert_commit9_inventory(surfaces: tuple[str, ...]) -> None:
    categories = _governed_categories(set(surfaces))
    for category in categories.values():
        assert category
    assert set(SYNTHETIC_FUTURE_SURFACES) <= set(surfaces)
    assert set(surfaces).isdisjoint(RETIRED_CONSUMER_POLICY_SURFACES)
    assert all(
        path in SYNTHETIC_FUTURE_SURFACES or (REPO_ROOT / path).exists()
        for path in surfaces
    )
    assert {
        ".github/CODEOWNERS",
        ".github/workflow-delivery/governance/hcoona-release-smoke-npm.json",
        "hk.pkl",
        "eng/scripts/hk_exec.py",
        "eng/scripts/workflow_delivery_v3_hk.py",
        "eng/scripts/workflow_delivery_v3_static_reference.py",
        "pyproject.toml",
        "uv.lock",
    } <= set(surfaces)
    assert any(path.startswith("src/private/lib/hk/") for path in surfaces)
    assert any(
        "/catalog" in path or path.endswith("/catalogs.py") for path in surfaces
    )
    assert any("/tests/" in path for path in surfaces)


def _modify_history_path(repo: Path, path: str) -> None:
    destination = repo / path
    if path.endswith(".pkl"):
        addition = b"\n// commit-9 governed modification\n"
    elif path.endswith(".py"):
        addition = b"\n# commit-9 governed modification\n"
    else:
        addition = b"\ncommit-9 governed modification\n"
    destination.write_bytes(destination.read_bytes() + addition)


def _batched_history_changes(
    kind: str,
    surfaces: tuple[str, ...],
) -> tuple[HistoryChange, ...]:
    if kind in {"add", "modify", "delete"}:
        return tuple(HistoryChange(kind, path) for path in surfaces)
    if kind == "rename-out":
        return tuple(
            HistoryChange(
                "rename",
                f".commit9-history/rename-out/{index:04d}.txt",
                old_path=path,
            )
            for index, path in enumerate(surfaces)
        )
    if kind == "rename-in":
        return tuple(
            HistoryChange(
                "rename",
                path,
                old_path=f".commit9-history/rename-in/{index:04d}.txt",
            )
            for index, path in enumerate(surfaces)
        )
    message = f"unsupported batched history kind: {kind}"
    raise AssertionError(message)


def _apply_batched_changes(
    repo: Path,
    changes: tuple[HistoryChange, ...],
) -> None:
    for change in changes:
        if change.kind == "modify" or (
            change.kind == "add" and (repo / change.path).exists()
        ):
            _modify_history_path(repo, change.path)
        else:
            _apply_change(repo, change)


def _materialize_add_surfaces(
    repo: Path,
    surfaces: tuple[str, ...],
) -> None:
    shutil.copy2(HK_CONFIG, repo / "hk.pkl")
    shutil.copytree(HK_SUPPORT, repo / "src/private/lib/hk")
    helper = repo / HK_RANGE_HELPER
    helper.parent.mkdir(parents=True)
    shutil.copy2(REPO_ROOT / HK_RANGE_HELPER, helper)
    for path in surfaces:
        if not (repo / path).exists():
            _write(repo, path, "added\n")


def _name_status(
    repo: Path,
    base: str,
    head: str,
) -> tuple[tuple[str, str], ...]:
    lines = _git(
        repo,
        "diff",
        "--name-status",
        "--no-renames",
        base,
        head,
    ).stdout.splitlines()
    return tuple((status, path) for status, path in map(str.split, lines))


def _run_helper_without_check(
    repo: Path,
    *arguments: str,
) -> subprocess.CompletedProcess[str]:
    return subprocess.run(  # noqa: S603
        (
            sys.executable,
            str(repo / HK_RANGE_HELPER),
            "--repository",
            str(repo),
            *arguments,
        ),
        cwd=repo,
        check=False,
        capture_output=True,
        text=True,
    )


def test_real_hk_plan_triggers_for_governed_path(tmp_path: Path) -> None:
    """Let real HK match every bounded governed path family."""
    repo = tmp_path / "repo"
    base = _initialize_repository(repo)
    for path in GOVERNED_PATHS:
        if path == "hk.pkl":
            config = (repo / path).read_text(encoding="utf-8")
            _write(repo, path, config + "\n// governed change\n")
        elif path == HK_RANGE_HELPER.as_posix():
            helper = (repo / path).read_text(encoding="utf-8")
            _write(repo, path, helper + "\n# governed change\n")
        else:
            _write(repo, path, "governed\n")
    head = _commit(repo, "add governed paths")
    paths = _helper_changed_paths(repo, base, head)

    step = _helper_step_plan(repo, base, head)

    assert set(paths) == set(GOVERNED_PATHS)
    assert step["status"] == "included"
    assert step["fileCount"] == len(GOVERNED_PATHS)


@pytest.mark.parametrize(
    "change",
    [
        HistoryChange(
            "add",
            "src/public/app/example/workflow-delivery.release-unit.yml",
        ),
        HistoryChange(
            "modify",
            "src/private/app/example/workflow-delivery.quality.yml",
        ),
        HistoryChange(
            "delete",
            "src/public/app/example/workflow-delivery.release-unit.yml",
        ),
        HistoryChange(
            "rename",
            "archive/release-unit.yml",
            old_path=(
                "src/public/app/example/workflow-delivery.release-unit.yml"
            ),
        ),
        HistoryChange(
            "rename",
            "src/public/app/example/workflow-delivery.quality.yml",
            old_path="archive/quality.yml",
        ),
    ],
    ids=["add", "modify", "delete", "rename-out", "rename-in"],
)
def test_real_hk_plan_triggers_for_descriptor_git_history(
    tmp_path: Path,
    change: HistoryChange,
) -> None:
    """Plan the step from real add, modify, delete, and rename histories."""
    repo = tmp_path / "repo"
    baseline_paths = (
        (change.old_path or change.path,)
        if change.kind in {"modify", "delete", "rename"}
        else ()
    )
    base = _initialize_repository(repo, baseline_paths=baseline_paths)
    _apply_change(repo, change)
    head = _commit(repo, change.kind)
    paths = _helper_changed_paths(repo, base, head)

    step = _helper_step_plan(repo, base, head)

    if change.kind == "rename":
        assert paths == (change.old_path, change.path)
    else:
        assert paths == (change.path,)
    assert step["status"] == "included"
    assert step["fileCount"] == 1


def test_real_hk_plan_runs_for_full_slice_validation_equivalent(
    tmp_path: Path,
) -> None:
    """Select the v3 step during the manual slice's root-HK full run."""
    repo = tmp_path / "repo"
    _initialize_repository(repo)

    step = _step_plan(repo, "--all")

    assert step["status"] == "included"
    assert step["fileCount"] > 0


def test_real_hk_plan_skips_unrelated_product_source(tmp_path: Path) -> None:
    """Do not select v3 control tests for unrelated product source alone."""
    repo = tmp_path / "repo"
    base = _initialize_repository(repo)
    unrelated = "src/public/lib/hcoona-release-smoke/src/index.ts"
    _write(repo, unrelated, "export const value = 1;\n")
    head = _commit(repo, "unrelated product change")
    paths = _helper_changed_paths(repo, base, head)

    step = _helper_step_plan(repo, base, head)

    assert paths == (unrelated,)
    assert step["status"] == "skipped"
    assert step["fileCount"] == 0


@pytest.mark.parametrize("kind", ["add", "modify", "delete"])
def test_real_v3_control_pytest_selects_every_codeowners_surface_for_history_kind(  # noqa: E501
    tmp_path: Path,
    kind: str,
) -> None:
    """Cross-check the shared ownership inventory through real Git and HK."""
    surfaces = _commit9_surfaces()
    _assert_commit9_inventory(surfaces)
    repo = tmp_path / "repo"
    if kind == "add":
        base = _initialize_empty_repository(repo)
        _materialize_add_surfaces(repo, surfaces)
    else:
        base = _initialize_repository(repo, baseline_paths=surfaces)
    execution_copies = _execution_copies(repo)
    changes = _batched_history_changes(kind, surfaces)

    if kind != "add":
        _apply_batched_changes(repo, changes)
    head = _commit(repo, f"commit-9 {kind} surface batch")
    if kind == "delete":
        _restore_execution_copies(repo, execution_copies)

    paths = _helper_changed_paths(repo, base, head)
    step = _helper_step_plan(repo, base, head)

    assert paths == surfaces
    assert _name_status(repo, base, head) == tuple(
        ({"add": "A", "modify": "M", "delete": "D"}[kind], path)
        for path in surfaces
    )
    assert step["status"] == "included"
    assert step["fileCount"] == len(surfaces)
    if kind == "delete":
        assert _git(repo, "status", "--porcelain").stdout


@pytest.mark.parametrize("kind", ["rename-out", "rename-in"])
def test_real_v3_control_pytest_selects_governed_side_of_batched_rename(
    tmp_path: Path,
    kind: str,
) -> None:
    """Count only the governed side while retaining both names from Git."""
    surfaces = _commit9_surfaces()
    _assert_commit9_inventory(surfaces)
    repo = tmp_path / "repo"
    _initialize_repository(repo, baseline_paths=surfaces)
    execution_copies = _execution_copies(repo)
    changes = _batched_history_changes(kind, surfaces)
    if kind == "rename-in":
        for change in changes:
            assert change.old_path is not None
            (repo / change.old_path).parent.mkdir(parents=True, exist_ok=True)
            _git(repo, "mv", change.path, change.old_path)
        base = _commit(repo, "commit-9 rename-in baseline")
    else:
        base = _git(repo, "rev-parse", "HEAD").stdout.strip()

    _apply_batched_changes(repo, changes)
    head = _commit(repo, f"commit-9 {kind} surface batch")
    if kind == "rename-out":
        _restore_execution_copies(repo, execution_copies)

    paths = _helper_changed_paths(repo, base, head)
    step = _helper_step_plan(repo, base, head)
    expected_paths = tuple(
        path
        for change in changes
        for path in (change.old_path, change.path)
        if path is not None
    )

    assert paths == expected_paths
    assert set(surfaces) <= set(paths)
    assert step["status"] == "included"
    assert step["fileCount"] == len(surfaces)
    if kind == "rename-out":
        assert _git(repo, "status", "--porcelain").stdout


def test_real_hk_helper_treats_option_like_paths_as_files(
    tmp_path: Path,
) -> None:
    """Prevent changed repository paths from becoming HK CLI options."""
    repo = tmp_path / "repo"
    base = _initialize_repository(repo)
    option_like = "--skip-step=v3-control-pytest"
    governed = "src/public/lib/three-workflow-delivery-v3/src/control.py"
    _write(repo, option_like, "not an option\n")
    _write(repo, governed, "governed\n")
    head = _commit(repo, "option-like path")

    paths = _helper_changed_paths(repo, base, head)
    step = _helper_step_plan(repo, base, head)

    assert paths == (option_like, governed)
    assert step["status"] == "included"
    assert step["fileCount"] == 1


def test_script_reports_exact_paths_for_normal_commit_range(
    tmp_path: Path,
) -> None:
    """Report only the path changed between two concrete commit OIDs."""
    repo = tmp_path / "repo"
    base_oid = _initialize_repository(repo)
    _write(repo, "range-only.txt", "range change\n")
    head_oid = _commit(repo, "range change")

    result = _run_helper_without_check(
        repo,
        "--from-ref",
        base_oid,
        "--to-ref",
        head_oid,
    )

    assert result.returncode == 0
    assert result.stderr == ""
    assert tuple(json.loads(result.stdout)) == ("range-only.txt",)


@pytest.mark.parametrize(
    "ref_option",
    ["--from-ref", "--to-ref"],
    ids=["from-ref", "to-ref"],
)
def test_script_rejects_option_like_ref(
    tmp_path: Path,
    ref_option: str,
) -> None:
    """Reject either ref when Git would otherwise interpret it as an option."""
    repo = tmp_path / "repo"
    base_oid = _initialize_repository(repo)
    _write(repo, "committed-only.txt", "committed change\n")
    head_oid = _commit(repo, "committed change")
    assert _git(repo, "status", "--porcelain").stdout == ""
    arguments = {
        "--from-ref": base_oid,
        "--to-ref": head_oid,
    }
    arguments[ref_option] = "--cached"

    result = _run_helper_without_check(
        repo,
        f"--from-ref={arguments['--from-ref']}",
        f"--to-ref={arguments['--to-ref']}",
    )

    assert result.returncode != 0
    assert result.stdout.strip() == ""


@pytest.mark.parametrize(
    "ref_option",
    ["--from-ref", "--to-ref"],
    ids=["from-ref", "to-ref"],
)
def test_script_does_not_write_output_for_option_like_ref(
    tmp_path: Path,
    ref_option: str,
) -> None:
    """Do not let a ref value become Git's arbitrary output-file option."""
    repo = tmp_path / "repo"
    _initialize_repository(repo)
    output_path = tmp_path / "unexpected-diff-output"
    arguments = {
        "--from-ref": "HEAD",
        "--to-ref": "HEAD",
    }
    arguments[ref_option] = f"--output={output_path}"

    result = _run_helper_without_check(
        repo,
        f"--from-ref={arguments['--from-ref']}",
        f"--to-ref={arguments['--to-ref']}",
    )

    assert result.returncode != 0
    assert result.stdout.strip() == ""
    assert not output_path.exists()


@pytest.mark.parametrize(
    "ref_option",
    ["--from-ref", "--to-ref"],
    ids=["from-ref", "to-ref"],
)
def test_script_rejects_non_commit_ref(
    tmp_path: Path,
    ref_option: str,
) -> None:
    """Require both resolved refs to peel to commit objects."""
    repo = tmp_path / "repo"
    _initialize_repository(repo)
    _write(repo, "blob-source", "not a commit\n")
    blob_oid = _git(repo, "hash-object", "-w", "blob-source").stdout.strip()
    blob_ref = "refs/tags/workflow-delivery-v3-blob"
    _git(repo, "update-ref", blob_ref, blob_oid)
    arguments = {
        "--from-ref": "HEAD",
        "--to-ref": "HEAD",
    }
    arguments[ref_option] = blob_ref

    result = _run_helper_without_check(
        repo,
        f"--from-ref={arguments['--from-ref']}",
        f"--to-ref={arguments['--to-ref']}",
    )

    assert result.returncode != 0
    assert result.stdout.strip() == ""
    assert blob_ref in result.stderr


def test_script_resolves_ref_when_name_collides_with_path(
    tmp_path: Path,
) -> None:
    """Resolve an ambiguous name as the requested ref, not the path."""
    repo = tmp_path / "repo"
    _initialize_repository(repo)
    _write(repo, "topic", "before\n")
    base_oid = _commit(repo, "add topic")
    _git(repo, "branch", "topic", base_oid)
    _write(repo, "topic", "after\n")
    _commit(repo, "change topic")

    result = _run_helper_without_check(
        repo,
        "--from-ref",
        "topic",
        "--to-ref",
        "HEAD",
    )

    assert result.returncode == 0
    assert result.stderr == ""
    assert tuple(json.loads(result.stdout)) == ("topic",)


def test_script_reports_invalid_ref(tmp_path: Path) -> None:
    """Surface Git diagnostics when the requested ref does not exist."""
    repo = tmp_path / "repo"
    _initialize_repository(repo)
    missing_ref = "refs/heads/workflow-delivery-v3-missing-ref"

    result = _run_helper_without_check(
        repo,
        "--from-ref",
        missing_ref,
        "--to-ref",
        "HEAD",
    )

    assert result.returncode != 0
    assert result.stdout.strip() == ""
    assert missing_ref in result.stderr
    assert "Command '('git', 'rev-parse', '--verify'" in result.stderr


@pytest.mark.parametrize(
    ("path", "content"),
    [
        pytest.param("mise.toml", "[tools]\n", id="mise-toml"),
        pytest.param("mise.lock", "# test fixture\n", id="mise-lock"),
    ],
)
def test_root_mise_path_includes_v3_control_pytest(
    tmp_path: Path,
    path: str,
    content: str,
) -> None:
    """Include the v3 control test for each root mise file independently."""
    repo = tmp_path / "repo"
    base = _initialize_repository(repo)
    _write(repo, path, content)
    head = _commit(repo, f"add {path}")

    paths = _helper_changed_paths(repo, base, head)
    step = _helper_step_plan(repo, base, head)

    assert paths == (path,)
    assert step["status"] == "included"
    assert step["fileCount"] == 1


def test_v3_collection_roots_include_commit3_contract_boundary_suite() -> None:
    """Keep the relocated commit-3 suite in every managed collection root."""
    import tomllib  # noqa: PLC0415

    package_test_root = Path(
        "src/public/lib/three-workflow-delivery-v3/tests",
    )
    package_root = package_test_root.parent
    destination = (
        package_test_root / "contracts/test_commit3_contract_boundaries.py"
    )
    old_orphan = Path(
        "tests/workflow_delivery_v3/test_commit3_contract_boundaries.py",
    )
    sentinel = (
        f"{destination.as_posix()}::"
        "test_nbgv_provider_declares_explicit_ref_neutral_environment_allowlist"
    )

    assert (REPO_ROOT / destination).is_file()
    assert not (REPO_ROOT / old_orphan).exists()

    root_config = tomllib.loads(
        (REPO_ROOT / "pyproject.toml").read_text(encoding="utf-8"),
    )
    pytest_options = root_config["tool"]["pytest"]["ini_options"]
    configured_testpaths = tuple(
        Path(path) for path in pytest_options["testpaths"]
    )
    assert package_test_root in configured_testpaths
    assert destination.is_relative_to(package_test_root)
    assert pytest_options["addopts"] == "--import-mode=importlib"

    hk_config = HK_CONFIG.read_text(encoding="utf-8")
    v3_config_start = hk_config.index(
        "local workflow_delivery_v3_validation",
    )
    v3_config_end = hk_config.index(
        "local dotenv_linter",
        v3_config_start,
    )
    v3_config = hk_config[v3_config_start:v3_config_end]
    assert '["v3-control-pytest"]' in v3_config
    assert f'"{package_root.as_posix()}/**"' in v3_config
    assert destination.is_relative_to(package_root)
    assert (
        "uv run --python 3.13 --package three-workflow-delivery-v3 "
        f"pytest -q {package_test_root.as_posix()}"
    ) in v3_config

    collection = _run(
        (
            "uv",
            "run",
            "--isolated",
            "--python",
            "3.13",
            "--package",
            "three-workflow-delivery-v3",
            "pytest",
            "--collect-only",
            "-q",
            package_test_root.as_posix(),
        ),
        cwd=REPO_ROOT,
    )
    assert sentinel in collection.stdout.splitlines()


def test_real_hk_plan_triggers_scholarly_suite_for_bounded_surfaces(
    tmp_path: Path,
) -> None:
    """Select the dedicated suite for every scholarly package path family."""
    repo = tmp_path / "repo"
    base = _initialize_repository(repo)
    for path in SCHOLARLY_SURFACE_PATHS:
        _write(repo, path, "scholarly publication surface\n")
    head = _commit(repo, "scholarly publication surfaces")

    paths = _helper_changed_paths(repo, base, head)
    step = _named_helper_step_plan(
        repo,
        base,
        head,
        SCHOLARLY_STEP_NAME,
    )

    assert paths == tuple(sorted(SCHOLARLY_SURFACE_PATHS))
    assert step["status"] == "included"
    assert step["fileCount"] == len(SCHOLARLY_SURFACE_PATHS)


def test_real_hk_plan_triggers_for_deployed_skill_root_symlinks(
    tmp_path: Path,
) -> None:
    """Route committed skill-root symlinks through the dedicated suite."""
    baseline_paths = tuple(
        f"{skill_root}/SKILL.md" for skill_root in SCHOLARLY_SKILL_ROOTS
    )
    repo = tmp_path / "repo"
    base = _initialize_repository(repo, baseline_paths=baseline_paths)

    link_target = repo / ".symlink-target"
    link_target.write_text("../canonical-skill\n", encoding="utf-8")
    link_blob = _git(repo, "hash-object", "-w", str(link_target)).stdout.strip()
    link_target.unlink()
    for skill_root in SCHOLARLY_SKILL_ROOTS:
        _git(repo, "rm", "--quiet", "-r", skill_root)
        _git(
            repo,
            "update-index",
            "--add",
            "--cacheinfo",
            f"120000,{link_blob},{skill_root}",
        )
    _git(
        repo,
        "commit",
        "--quiet",
        "--message",
        "replace skill roots with symlinks",
    )
    head = _git(repo, "rev-parse", "HEAD").stdout.strip()

    expected_paths = tuple(sorted((*SCHOLARLY_SKILL_ROOTS, *baseline_paths)))
    paths = _helper_changed_paths(repo, base, head)
    step = _named_helper_step_plan(
        repo,
        base,
        head,
        SCHOLARLY_STEP_NAME,
    )

    for skill_root in SCHOLARLY_SKILL_ROOTS:
        tree_entry = _git(
            repo,
            "ls-tree",
            head,
            "--",
            skill_root,
        ).stdout
        assert tree_entry.startswith("120000 blob ")
    assert paths == expected_paths
    assert step["status"] == "included"
    assert step["fileCount"] == len(expected_paths)


def test_real_hk_plan_keeps_scholarly_suite_outside_optional_profiles(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Run the package gate even when only an unrelated profile is enabled."""
    repo = tmp_path / "repo"
    base = _initialize_repository(repo)
    path = ".agents/skills/scholarly-print-assembly/SKILL.md"
    _write(repo, path, "scholarly publication surface\n")
    head = _commit(repo, "scholarly publication surface")
    monkeypatch.setenv("HK_PROFILE", "medium")

    paths = _helper_changed_paths(repo, base, head)
    step = _named_helper_step_plan(
        repo,
        base,
        head,
        SCHOLARLY_STEP_NAME,
        required_profile="medium",
    )

    assert paths == (path,)
    assert step["status"] == "included"
    assert step["fileCount"] == 1


def test_real_hk_plan_skips_sibling_packages_for_scholarly_suite(
    tmp_path: Path,
) -> None:
    """Keep the dedicated suite bounded to the scholarly package."""
    unrelated_paths = (
        ".agents/skills/scholarly-unrelated/SKILL.md",
        "src/private/lib/unrelated-package/source.py",
    )
    repo = tmp_path / "repo"
    base = _initialize_repository(repo)
    for path in unrelated_paths:
        _write(repo, path, "unrelated package surface\n")
    head = _commit(repo, "unrelated package surfaces")

    paths = _helper_changed_paths(repo, base, head)
    step = _named_helper_step_plan(
        repo,
        base,
        head,
        SCHOLARLY_STEP_NAME,
    )

    assert paths == tuple(sorted(unrelated_paths))
    assert step["status"] == "skipped"
    assert step["fileCount"] == 0


def test_gitattributes_selects_both_v3_internal_steps(
    tmp_path: Path,
) -> None:
    """Select control and the unconditional index scan for LF-policy changes."""
    repo = tmp_path / "repo"
    base = _initialize_repository(repo)
    path = ".gitattributes"
    _write(repo, path, "static-reference text eol=lf\n")
    head = _commit(repo, "static-reference attributes")

    paths = _helper_changed_paths(repo, base, head)
    control = _named_helper_step_plan(repo, base, head, STEP_NAME)
    static_reference = _named_helper_step_plan(
        repo,
        base,
        head,
        STATIC_REFERENCE_STEP_NAME,
    )

    assert paths == (path,)
    assert control["status"] == static_reference["status"] == "included"
    assert control["fileCount"] == static_reference["fileCount"] == 1
    assert "--source-kind index" in _static_reference_hk_block()


def test_real_hk_plan_slice_validation_runs_both_internal_steps(
    tmp_path: Path,
) -> None:
    """Make the full-slice signal include control and the exact index scan."""
    repo = tmp_path / "repo"
    _initialize_repository(repo)

    control = _named_step_plan(repo, STEP_NAME, "--all")
    static_reference = _named_step_plan(
        repo,
        STATIC_REFERENCE_STEP_NAME,
        "--all",
    )

    assert control["status"] == static_reference["status"] == "included"
    assert control["fileCount"] > 0
    assert static_reference["fileCount"] == control["fileCount"]
    assert "--source-kind index" in _static_reference_hk_block()


def test_real_hk_plan_retains_complete_v3_control_trigger_inventory(
    tmp_path: Path,
) -> None:
    """Retain every governed v3 family beside the unconditional index scan."""
    repo = tmp_path / "repo"
    base = _initialize_repository(repo)
    governed_paths = (
        *GOVERNED_PATHS,
        "mise.toml",
        "mise.lock",
    )
    for path in governed_paths:
        if path == "hk.pkl":
            config = (repo / path).read_text(encoding="utf-8")
            _write(repo, path, config + "\n// retained trigger inventory\n")
        elif path == HK_RANGE_HELPER.as_posix():
            helper = (repo / path).read_text(encoding="utf-8")
            _write(repo, path, helper + "\n# retained trigger inventory\n")
        else:
            _write(repo, path, "governed\n")
    head = _commit(repo, "complete v3 trigger inventory")

    paths = _helper_changed_paths(repo, base, head)
    control = _named_helper_step_plan(repo, base, head, STEP_NAME)
    static_reference = _named_helper_step_plan(
        repo,
        base,
        head,
        STATIC_REFERENCE_STEP_NAME,
    )

    assert set(paths) == set(governed_paths)
    assert control["status"] == static_reference["status"] == "included"
    assert control["fileCount"] == len(governed_paths)
    assert static_reference["fileCount"] == len(governed_paths)
    assert "--source-kind index" in _static_reference_hk_block()


def test_real_hk_plan_policy_only_selects_v3_control_not_unrelated_product_source(  # noqa: E501
    tmp_path: Path,
) -> None:
    """Keep control bounded while the explicit index scan remains universal."""
    policy_repo = tmp_path / "policy-repo"
    policy_base = _initialize_repository(policy_repo)
    policy_path = (
        "eng/workflow-delivery/v3/policies/hcoona-release-smoke-npm.yml"
    )
    _write(policy_repo, policy_path, "policy\n")
    policy_head = _commit(policy_repo, "policy change")

    policy_control = _named_helper_step_plan(
        policy_repo,
        policy_base,
        policy_head,
        STEP_NAME,
    )
    policy_static_reference = _named_helper_step_plan(
        policy_repo,
        policy_base,
        policy_head,
        STATIC_REFERENCE_STEP_NAME,
    )

    product_repo = tmp_path / "product-repo"
    product_base = _initialize_repository(product_repo)
    product_path = "src/public/lib/hcoona-release-smoke/src/index.ts"
    _write(product_repo, product_path, "export const value = 1;\n")
    product_head = _commit(product_repo, "unrelated product source")
    product_control = _named_helper_step_plan(
        product_repo,
        product_base,
        product_head,
        STEP_NAME,
    )
    product_static_reference = _named_helper_step_plan(
        product_repo,
        product_base,
        product_head,
        STATIC_REFERENCE_STEP_NAME,
    )

    assert policy_control["status"] == "included"
    assert policy_control["fileCount"] == 1
    assert product_control["status"] == "skipped"
    assert product_control["fileCount"] == 0
    assert policy_static_reference["status"] == "included"
    assert policy_static_reference["fileCount"] == 1
    assert product_static_reference["status"] == "included"
    assert product_static_reference["fileCount"] == 1
    assert "--source-kind index" in _static_reference_hk_block()


@pytest.mark.parametrize(
    "path",
    [
        "Directory.Packages.props",
        "src/private/app/workflow-delivery-v3-nuget-authority/Program.cs",
        (
            "src/public/lib/three-workflow-delivery-v3/src/"
            "three_workflow_delivery_v3/release/static_reference_policy.py"
        ),
    ],
    ids=["central-package-versions", "nuget-authority", "policy-digest"],
)
def test_real_hk_plan_prepares_changed_authority_before_consumers(
    tmp_path: Path,
    path: str,
) -> None:
    """Prepare once when a changed input can stale the authority closure."""
    repo = tmp_path / "repo"
    base = _initialize_repository(repo)
    _write(repo, path, "changed authority input\n")
    head = _commit(repo, "authority preparation input")

    preparation = _named_helper_step_plan(
        repo,
        base,
        head,
        PREPARATION_STEP_NAME,
    )
    control = _named_helper_step_plan(repo, base, head, STEP_NAME)
    static_reference = _named_helper_step_plan(
        repo,
        base,
        head,
        STATIC_REFERENCE_STEP_NAME,
    )

    assert preparation["status"] == "included"
    assert preparation["fileCount"] == 1
    assert control["status"] == "included"
    assert static_reference["status"] == "included"
    expected_dependency = f'depends = List("{PREPARATION_STEP_NAME}")'
    assert expected_dependency in _hk_step_block(STEP_NAME)
    assert expected_dependency in _static_reference_hk_block()


def test_real_hk_plan_prepares_authority_for_unrelated_root_hk_path(
    tmp_path: Path,
) -> None:
    """Prepare the authority whenever the unconditional root scan runs."""
    repo = tmp_path / "repo"
    base = _initialize_repository(repo)
    _write(repo, "docs/wiki/README.md", "documentation-only change\n")
    head = _commit(repo, "unrelated root HK input")

    preparation = _named_helper_step_plan(
        repo,
        base,
        head,
        PREPARATION_STEP_NAME,
    )
    control = _named_helper_step_plan(repo, base, head, STEP_NAME)
    static_reference = _named_helper_step_plan(
        repo,
        base,
        head,
        STATIC_REFERENCE_STEP_NAME,
    )

    assert preparation["status"] == "included"
    assert preparation["fileCount"] == 1
    assert control["status"] == "skipped"
    assert control["fileCount"] == 0
    assert static_reference["status"] == "included"
    assert static_reference["fileCount"] == 1
    assert "glob =" not in _hk_step_block(PREPARATION_STEP_NAME)


def test_static_reference_is_one_internal_root_hk_step_not_ci_obligation() -> (
    None
):
    """Keep the explicit index scan inside root HK, not in a fifth CI lane."""
    if str(REPO_ROOT) not in sys.path:
        sys.path.insert(0, str(REPO_ROOT))
    from three_workflow_delivery_v3.records.ci import (  # noqa: PLC0415
        CI_LANE_IDS,
    )

    hk_config = HK_CONFIG.read_text(encoding="utf-8")
    v3_start = hk_config.index("local workflow_delivery_v3_validation")
    v3_end = hk_config.index("local dotenv_linter", v3_start)
    v3_config = hk_config[v3_start:v3_end]
    static_step = _static_reference_hk_block()

    assert v3_config.count(f'["{STATIC_REFERENCE_STEP_NAME}"]') == 1
    assert (
        "python eng/scripts/workflow_delivery_v3_static_reference.py "
        "--repository-root . --source-kind index"
    ) in static_step
    assert "--timeout-seconds 300" in static_step
    assert "worktree" not in static_step
    assert CI_LANE_IDS == (
        "root-hk",
        "project-build",
        "project-test",
        "npm-artifact-build",
    )
    assert STATIC_REFERENCE_STEP_NAME not in CI_LANE_IDS


def test_testagent_markdown_exclusion_is_local_to_two_markdown_steps() -> None:
    """Exclude append-only artifacts only from markdownlint/prettier."""
    hk_config = HK_CONFIG.read_text(encoding="utf-8")
    markdown_start = hk_config.index("local markdown_linters")
    markdown_end = hk_config.index("local pkl_linters", markdown_start)
    markdown_config = hk_config[markdown_start:markdown_end]
    remaining_config = hk_config[:markdown_start] + hk_config[markdown_end:]
    expected_exclusion_reference_count = 3
    exclusion_line = (
        "    exclude = general_exclude_list + "
        "markdown_append_only_artifact_exclude"
    )

    assert hk_config.count('".testagent/**"') == 1
    assert markdown_config.count("markdown_append_only_artifact_exclude") == (
        expected_exclusion_reference_count
    )
    assert (
        'local markdown_append_only_artifact_exclude = List(".testagent/**")'
    ) in markdown_config
    assert (
        '["markdownlint-cli2"] {\n'
        '    profiles = List("small")\n'
        f"{exclusion_line}"
    ) in markdown_config
    assert (
        '["markdown-prettier"] {\n'
        '    profiles = List("small")\n'
        f"{exclusion_line}"
    ) in markdown_config
    assert ".testagent/**" not in remaining_config
    assert "markdown_append_only_artifact_exclude" not in remaining_config


@pytest.mark.parametrize(
    "path",
    [
        STATIC_REFERENCE_IMPLEMENTATION.as_posix(),
        "hk.pkl",
        "Directory.Packages.props",
        "src/private/app/workflow-delivery-v3-nuget-authority/Program.cs",
    ],
    ids=[
        "implementation",
        "root-hk-configuration",
        "central-package-versions",
        "nuget-authority",
    ],
)
def test_real_hk_plan_triggers_static_reference_for_definition_changes(
    tmp_path: Path,
    path: str,
) -> None:
    """Run the index scan when its implementation or registration changes."""
    repo = tmp_path / "repo"
    base = _initialize_repository(repo)
    if path == "hk.pkl":
        config = (repo / path).read_text(encoding="utf-8")
        _write(repo, path, config + "\n// static-reference definition\n")
    else:
        _write(repo, path, "static-reference implementation\n")
    head = _commit(repo, "static-reference definition")

    paths = _helper_changed_paths(repo, base, head)
    static_reference = _named_helper_step_plan(
        repo,
        base,
        head,
        STATIC_REFERENCE_STEP_NAME,
    )
    control = _named_helper_step_plan(repo, base, head, STEP_NAME)

    assert paths == (path,)
    assert static_reference["status"] == "included"
    assert static_reference["fileCount"] == 1
    assert control["status"] == "included"
    assert control["fileCount"] == 1
    assert "--source-kind index" in _static_reference_hk_block()


def test_acceptance_fixture_gitignore_negations_are_exact_and_narrow() -> None:
    """Expose only the four acceptance fixture files required by the probe."""
    fixture_root = (
        "src/public/lib/three-workflow-delivery-v3/tests/fixtures/acceptance/"
        "npm-publish-request"
    )
    required_paths = (
        f"{fixture_root}/package.tgz",
        f"{fixture_root}/package/dist/acceptance-witness.json",
        f"{fixture_root}/package/dist/index.js",
    )
    still_ignored_paths = (
        f"{fixture_root}/other.tgz",
        f"{fixture_root}/other/dist/index.js",
    )

    result = _run(
        (
            "git",
            "check-ignore",
            "--no-index",
            *required_paths,
            *still_ignored_paths,
        ),
        cwd=REPO_ROOT,
    )
    ignored_paths = tuple(result.stdout.splitlines())

    assert result.returncode == 0
    assert result.stderr == ""
    assert ignored_paths == still_ignored_paths


def test_acceptance_fixture_required_files_are_visible_to_git() -> None:
    """Keep the captured request closure visible without broad exceptions."""
    fixture_root = (
        "src/public/lib/three-workflow-delivery-v3/tests/fixtures/acceptance/"
        "npm-publish-request"
    )
    expected_paths = (
        f"{fixture_root}/capture.json",
        f"{fixture_root}/package.tgz",
        f"{fixture_root}/package/dist/acceptance-witness.json",
        f"{fixture_root}/package/dist/index.js",
    )

    result = _git(
        REPO_ROOT,
        "ls-files",
        "--cached",
        "--others",
        "--exclude-standard",
        "--",
        *expected_paths,
    )
    visible_paths = tuple(result.stdout.splitlines())

    assert visible_paths == expected_paths
    assert all((REPO_ROOT / path).is_file() for path in visible_paths)


def test_testagent_markdown_exclusion_remains_local_to_two_steps() -> None:
    """Pin both Markdown selectors while keeping the exclusion local."""
    expected_markdown_step_count = 2
    hk_config = HK_CONFIG.read_text(encoding="utf-8")
    markdown_start = hk_config.index("local markdown_linters")
    markdown_end = hk_config.index("local pkl_linters", markdown_start)
    markdown_config = hk_config[markdown_start:markdown_end]
    remaining_config = hk_config[:markdown_start] + hk_config[markdown_end:]
    exclusion = (
        "    exclude = general_exclude_list + "
        "markdown_append_only_artifact_exclude"
    )

    assert markdown_config.count(exclusion) == expected_markdown_step_count
    assert (
        markdown_config.count('glob = List("*.md")')
        == expected_markdown_step_count
    )
    assert (
        markdown_config.count('stage = List("*.md")')
        == expected_markdown_step_count
    )
    assert (
        '["markdownlint-cli2"] {\n'
        '    profiles = List("small")\n'
        f"{exclusion}\n"
        "    batch = true\n"
        '    glob = List("*.md")\n'
        '    stage = List("*.md")'
    ) in markdown_config
    assert (
        '["markdown-prettier"] {\n'
        '    profiles = List("small")\n'
        f"{exclusion}\n"
        '    glob = List("*.md")\n'
        '    stage = List("*.md")'
    ) in markdown_config
    assert ".testagent/**" not in remaining_config
    assert "markdown_append_only_artifact_exclude" not in remaining_config


def test_legacy_pngchunk_ztxt_ba_line_and_typos_exception_are_exact() -> None:
    """Preserve the historical identifier and its file-specific exception."""
    legacy_path = "src/public/lib/Hjg.Pngcs/Chunks/PngChunkZTXT.cs"
    legacy_lines = (
        (REPO_ROOT / legacy_path)
        .read_text(
            encoding="utf-8",
        )
        .splitlines()
    )
    typos_config = (REPO_ROOT / ".typos.toml").read_text(encoding="utf-8")
    exact_exception = f'  "{legacy_path}",'
    legacy_identifier = "b" + "a"

    assert legacy_lines[45] == (
        f"            MemoryStream {legacy_identifier} = new MemoryStream();"
    )
    assert typos_config.count(exact_exception) == 1


def test_typos_legacy_identifier_exceptions_are_file_specific() -> None:
    """Reject wildcard Pngcs or repository-wide identifier exemptions."""
    minimum_specific_exception_count = 3
    typos_config = (REPO_ROOT / ".typos.toml").read_text(encoding="utf-8")
    exclusion_block = typos_config.split("extend-exclude = [", 1)[1].split(
        "]",
        1,
    )[0]
    pngcs_exclusions = tuple(
        line.strip().rstrip(",").strip("\"'")
        for line in exclusion_block.splitlines()
        if "src/public/lib/Hjg.Pngcs/" in line
    )

    assert "src/public/lib/Hjg.Pngcs/Chunks/PngChunkZTXT.cs" in pngcs_exclusions
    assert "src/public/lib/Hjg.Pngcs/Chunks/ChunkRaw.cs" in pngcs_exclusions
    assert len(pngcs_exclusions) >= minimum_specific_exception_count
    assert all("*" not in path and "?" not in path for path in pngcs_exclusions)
    legacy_identifier = "b" + "a"
    assert not any(
        line.lstrip()
        .casefold()
        .startswith(
            (
                f"{legacy_identifier} =",
                f"{legacy_identifier}=",
            )
        )
        for line in typos_config.splitlines()
    )
    assert rf"\b{legacy_identifier}\b" not in typos_config.casefold()


def test_historical_status_identifier_and_typos_scope_are_exact() -> None:
    """Preserve the historical line while limiting the exception to one file."""
    status_path = ".testagent/status.md"
    status_lines = (
        (REPO_ROOT / status_path).read_text(encoding="utf-8").splitlines()
    )
    legacy_identifier = "b" + "a"
    typos_config = (REPO_ROOT / ".typos.toml").read_text(encoding="utf-8")

    assert (
        status_lines[2899] == f"hex fixture substring `{legacy_identifier}` in"
    )
    assert typos_config.count(f"  '{status_path}',") == 1
    assert ".testagent/**" not in typos_config


def test_hk_helper_propagates_exact_child_exit_code_and_changed_paths(
    tmp_path: Path,
) -> None:
    """Propagate the child status after appending the exact Git path list."""
    repo = tmp_path / "repo"
    base_oid = _initialize_repository(repo)
    changed_paths = ("alpha change.txt", "nested/zeta.py")
    for path in changed_paths:
        _write(repo, path, f"changed: {path}\n")
    head_oid = _commit(repo, "child exit propagation")
    distinctive_exit_code = 73
    child_program = (
        "import json, sys; "
        "print(json.dumps(sys.argv[1:], separators=(',', ':'))); "
        f"sys.exit({distinctive_exit_code})"
    )

    result = _run_helper_without_check(
        repo,
        "--from-ref",
        base_oid,
        "--to-ref",
        head_oid,
        "--",
        sys.executable,
        "-c",
        child_program,
    )

    assert result.returncode == distinctive_exit_code
    assert result.stderr == ""
    assert tuple(json.loads(result.stdout)) == ("--", *changed_paths)


def test_parse_name_status_preserves_posix_backslash_component() -> None:
    """Treat backslash as a literal component character in Git paths."""
    helper_spec = importlib.util.spec_from_file_location(
        "_workflow_delivery_v3_hk_backslash_test",
        REPO_ROOT / HK_RANGE_HELPER,
    )
    assert helper_spec is not None
    assert helper_spec.loader is not None
    helper_module = importlib.util.module_from_spec(helper_spec)
    helper_spec.loader.exec_module(helper_module)

    assert helper_module.parse_name_status(
        b"M\0literal\\component/package.json\0"
    ) == (r"literal\component/package.json",)


@pytest.mark.parametrize(
    ("name_status", "expected_message", "expected_cause_type"),
    [
        pytest.param(
            b"M\0invalid-\xff.py\0",
            "Git returned a non-UTF-8 changed path",
            UnicodeDecodeError,
            id="non-utf8",
        ),
    ],
)
def test_parse_name_status_rejects_unsafe_path_before_child_execution(
    monkeypatch: pytest.MonkeyPatch,
    name_status: bytes,
    expected_message: str,
    expected_cause_type: type[BaseException] | None,
) -> None:
    """Reject unsafe Git bytes before invoking the requested child command."""
    from types import SimpleNamespace  # noqa: PLC0415

    helper_spec = importlib.util.spec_from_file_location(
        "_workflow_delivery_v3_hk_under_test",
        REPO_ROOT / HK_RANGE_HELPER,
    )
    assert helper_spec is not None
    assert helper_spec.loader is not None
    helper_module = importlib.util.module_from_spec(helper_spec)
    helper_spec.loader.exec_module(helper_module)

    from_oid = "1" * 40
    to_oid = "2" * 40
    git_commands: list[tuple[str, ...]] = []

    def fake_run(
        command: Sequence[str],
        **_kwargs: object,
    ) -> SimpleNamespace:
        arguments = tuple(command)
        git_commands.append(arguments)
        if arguments[:2] == ("git", "rev-parse"):
            resolved_oid = (
                from_oid if arguments[-1] == "base^{commit}" else to_oid
            )
            return SimpleNamespace(stdout=f"{resolved_oid}\n")
        if arguments[:2] == ("git", "diff"):
            return SimpleNamespace(stdout=name_status)
        return SimpleNamespace(returncode=91)

    monkeypatch.setattr(
        helper_module,
        "subprocess",
        SimpleNamespace(run=fake_run),
    )

    with pytest.raises(helper_module.ChangedPathError) as error:
        helper_module.main(
            (
                "--repository",
                ".",
                "--from-ref",
                "base",
                "--to-ref",
                "head",
                "--",
                sys.executable,
                "-c",
                "raise SystemExit(91)",
            ),
        )

    assert type(error.value) is helper_module.ChangedPathError
    assert str(error.value) == expected_message
    actual_cause_type = (
        type(error.value.__cause__)
        if error.value.__cause__ is not None
        else None
    )
    assert actual_cause_type is expected_cause_type
    assert tuple(git_commands) == (
        (
            "git",
            "rev-parse",
            "--verify",
            "--end-of-options",
            "base^{commit}",
        ),
        (
            "git",
            "rev-parse",
            "--verify",
            "--end-of-options",
            "head^{commit}",
        ),
        (
            "git",
            "diff",
            "--name-status",
            "--find-renames",
            "-z",
            "--end-of-options",
            from_oid,
            to_oid,
            "--",
        ),
    )


def test_mise_bootstrap_preparation_chain_and_manual_worktree_are_exact() -> (
    None
):
    """Pin authority preparation into bootstrap and the separate manual task."""
    import tomllib  # noqa: PLC0415

    mise_config = tomllib.loads(
        (REPO_ROOT / "mise.toml").read_text(encoding="utf-8"),
    )
    tasks = mise_config["tasks"]
    preparation_name = "prepare:static-reference-authorities"
    bootstrap_dependencies = tuple(tasks["bootstrap"]["depends"])
    node_bootstrap = tasks["bootstrap:node"]
    preparation = tasks[preparation_name]
    manual_worktree = tasks["check:static-reference-worktree"]

    assert bootstrap_dependencies.count("bootstrap:node") == 1
    assert tuple(node_bootstrap["depends"]) == (preparation_name,)
    assert node_bootstrap["run"] == "pnpm -r rebuild --pending"
    assert preparation["run"] == (
        "uv run --isolated --frozen --python 3.13 "
        "--package three-workflow-delivery-v3 python -B "
        "eng/scripts/workflow_delivery_v3_prepare_static_reference.py"
    )
    assert tuple(manual_worktree["depends"]) == (preparation_name,)
    assert manual_worktree["run"] == (
        "uv run --python 3.13 --package three-workflow-delivery-v3 "
        "python eng/scripts/workflow_delivery_v3_static_reference.py "
        "--repository-root . --source-kind worktree"
    )
    assert manual_worktree["run"] != preparation["run"]

    workspace = yaml.safe_load(
        (REPO_ROOT / "pnpm-workspace.yaml").read_text(encoding="utf-8")
    )
    assert workspace["allowBuilds"]["msgpackr-extract"] is True
