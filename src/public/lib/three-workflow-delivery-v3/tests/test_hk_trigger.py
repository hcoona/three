"""Integration contracts for the root HK v3 control-test trigger."""

from __future__ import annotations

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

if TYPE_CHECKING:
    from collections.abc import Sequence

REPO_ROOT = Path(__file__).resolve().parents[5]
HK_CONFIG = REPO_ROOT / "hk.pkl"
HK_SUPPORT = REPO_ROOT / "src/private/lib/hk"
HK_RANGE_HELPER = Path("eng/scripts/workflow_delivery_v3_hk.py")
STEP_NAME = "v3-control-pytest"
GOVERNED_PATHS = (
    ".gitattributes",
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
    HK_RANGE_HELPER.as_posix(),
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
        ("mise", "where", "hk@1.53.0"),
        cwd=REPO_ROOT,
    ).stdout.strip()
    executable = Path(install_root) / "hk"
    version = _run((str(executable), "--version"), cwd=REPO_ROOT)
    assert version.stdout.strip() == "hk 1.53.0"
    return str(executable)


def _step_from_plan(result: subprocess.CompletedProcess[str]) -> HkStepJson:
    plan: HkPlanJson = json.loads(result.stdout)
    assert plan["hook"] == "check"
    assert plan["runType"] == "check"
    assert "small" in plan["profiles"]
    assert len(plan["steps"]) == 1
    step = plan["steps"][0]
    assert step["name"] == STEP_NAME
    return step


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


def _helper_step_plan(repo: Path, base: str, head: str) -> HkStepJson:
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
            STEP_NAME,
        ),
        cwd=repo,
    )
    return _step_from_plan(result)


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
    return tuple(sorted(_governed_surface_inventory()))


def _assert_commit9_inventory(surfaces: tuple[str, ...]) -> None:
    categories = _governed_categories(set(surfaces))
    for category in categories.values():
        assert category
    assert set(SYNTHETIC_FUTURE_SURFACES) <= set(surfaces)
    assert {
        ".github/CODEOWNERS",
        ".github/workflow-delivery/governance/hcoona-release-smoke-npm.json",
        "hk.pkl",
        "eng/scripts/hk_exec.py",
        "eng/scripts/workflow_delivery_v3_hk.py",
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
    if path.endswith((".pkl",)):
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


CONSUMER_STEP_NAME = "hcoona-release-smoke-npm-consumer-policy"
CONSUMER_POLICY_IMPLEMENTATION = Path(
    "eng/scripts/workflow_delivery_v3_consumer_policy.py",
)
CONSUMER_SURFACE_PATHS = (
    ".gitattributes",
    "src/public/app/consumer/package.json",
    "src/public/app/consumer/pyproject.toml",
    "src/public/app/consumer/consumer.csproj",
    "src/public/app/consumer/pnpm-lock.yaml",
    "src/public/app/consumer/bun.lock",
    "src/public/app/consumer/.github/workflows/install.yml",
    ".github/actions/consumer-install/action.yml",
    "eng/bootstrap/setup-consumer.ps1",
    "eng/bootstrap/install-consumer.cmd",
    "postinstall-consumer.mjs",
    "eng/bootstrap/postinstall-consumer.cjs",
    "eng/bootstrap/postinstall-consumer.ts",
    "src/public/app/consumer/.npmrc",
)


def _named_step_from_plan(
    result: subprocess.CompletedProcess[str],
    step_name: str,
) -> HkStepJson:
    plan: HkPlanJson = json.loads(result.stdout)
    assert plan["hook"] == "check"
    assert plan["runType"] == "check"
    assert "small" in plan["profiles"]
    assert len(plan["steps"]) == 1
    step = plan["steps"][0]
    assert step["name"] == step_name
    return step


def _named_step_plan(
    repo: Path,
    step_name: str,
    *arguments: str,
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
    return _named_step_from_plan(result, step_name)


def _named_helper_step_plan(
    repo: Path,
    base: str,
    head: str,
    step_name: str,
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
    return _named_step_from_plan(result, step_name)


@pytest.mark.parametrize(
    "path",
    CONSUMER_SURFACE_PATHS,
    ids=[
        "git-attributes",
        "node-dependency-manifest",
        "python-dependency-manifest",
        "dotnet-dependency-manifest",
        "lockfile",
        "bun-lock",
        "workflow",
        "composite-action",
        "powershell-install-bootstrap-script",
        "cmd-install-bootstrap-script",
        "postinstall-mjs-root",
        "postinstall-cjs-nested",
        "postinstall-ts-nested",
        "dependency-configuration",
    ],
)
def test_real_hk_plan_triggers_consumer_policy_for_each_cataloged_surface(
    tmp_path: Path,
    path: str,
) -> None:
    """Select the permanent policy for each closed surface category."""
    repo = tmp_path / "repo"
    base = _initialize_repository(repo)
    _write(repo, path, "cataloged surface\n")
    head = _commit(repo, "cataloged dependency surface")

    paths = _helper_changed_paths(repo, base, head)
    step = _named_helper_step_plan(
        repo,
        base,
        head,
        CONSUMER_STEP_NAME,
    )

    assert paths == (path,)
    assert step["status"] == "included"
    assert step["fileCount"] == 1


def test_gitattributes_selects_both_v3_internal_steps(
    tmp_path: Path,
) -> None:
    """Select control tests and the policy gate for LF-policy changes."""
    repo = tmp_path / "repo"
    base = _initialize_repository(repo)
    path = ".gitattributes"
    _write(repo, path, "consumer-policy text eol=lf\n")
    head = _commit(repo, "consumer policy attributes")

    paths = _helper_changed_paths(repo, base, head)
    control = _named_helper_step_plan(repo, base, head, STEP_NAME)
    consumer = _named_helper_step_plan(
        repo,
        base,
        head,
        CONSUMER_STEP_NAME,
    )

    assert paths == (path,)
    assert control["status"] == consumer["status"] == "included"
    assert control["fileCount"] == consumer["fileCount"] == 1


def test_composite_action_manifest_selects_only_consumer_policy(
    tmp_path: Path,
) -> None:
    """Select the policy without broadening the v3 control inventory."""
    repo = tmp_path / "repo"
    base = _initialize_repository(repo)
    path = ".github/actions/consumer-install/action.yaml"
    _write(repo, path, "runs:\n  using: composite\n  steps: []\n")
    head = _commit(repo, "local composite action")

    paths = _helper_changed_paths(repo, base, head)
    consumer = _named_helper_step_plan(
        repo,
        base,
        head,
        CONSUMER_STEP_NAME,
    )
    control = _named_helper_step_plan(repo, base, head, STEP_NAME)

    assert paths == (path,)
    assert consumer["status"] == "included"
    assert consumer["fileCount"] == 1
    assert control["status"] == "skipped"
    assert control["fileCount"] == 0


def test_dangling_catalog_symlink_selects_consumer_policy(
    tmp_path: Path,
) -> None:
    """Include a changed dangling catalog path without following its target."""
    repo = tmp_path / "repo"
    base = _initialize_repository(repo)
    path = "consumer/package.json"
    destination = repo / path
    destination.parent.mkdir(parents=True)
    destination.symlink_to("missing-package.json")
    head = _commit(repo, "dangling catalog surface")

    paths = _helper_changed_paths(repo, base, head)
    step = _named_helper_step_plan(
        repo,
        base,
        head,
        CONSUMER_STEP_NAME,
    )

    assert paths == (path,)
    assert step["status"] == "included"
    assert step["fileCount"] == 1


@pytest.mark.parametrize(
    "change",
    [
        HistoryChange("add", "tools/postinstall-smoke.js"),
        HistoryChange("modify", "consumer/package.json"),
        HistoryChange(
            "delete",
            "consumer/bun.lock",
        ),
        HistoryChange(
            "rename",
            "archive/dependencies.txt",
            old_path="tools/postinstall-smoke.js",
        ),
        HistoryChange(
            "rename",
            "consumer/bun.lock",
            old_path="archive/dependencies.lock",
        ),
    ],
    ids=[
        "postinstall-add",
        "modify",
        "bun-lock-delete",
        "postinstall-rename-out",
        "bun-lock-rename-in",
    ],
)
def test_real_hk_plan_triggers_consumer_policy_for_git_history(
    tmp_path: Path,
    change: HistoryChange,
) -> None:
    """Retain add, modify, delete, and both rename sides in policy scope."""
    repo = tmp_path / "repo"
    baseline_paths = (
        (change.old_path or change.path,)
        if change.kind in {"modify", "delete", "rename"}
        else ()
    )
    base = _initialize_repository(repo, baseline_paths=baseline_paths)
    _apply_change(repo, change)
    head = _commit(repo, f"consumer surface {change.kind}")

    paths = _helper_changed_paths(repo, base, head)
    step = _named_helper_step_plan(
        repo,
        base,
        head,
        CONSUMER_STEP_NAME,
    )

    if change.kind == "rename":
        assert paths == (change.old_path, change.path)
    else:
        assert paths == (change.path,)
    assert step["status"] == "included"
    assert step["fileCount"] == 1


def test_real_hk_plan_slice_validation_runs_both_internal_steps(
    tmp_path: Path,
) -> None:
    """Make the manual full/slice signal include both internal checks."""
    repo = tmp_path / "repo"
    _initialize_repository(repo)

    control = _named_step_plan(repo, STEP_NAME, "--all")
    consumer = _named_step_plan(repo, CONSUMER_STEP_NAME, "--all")

    assert control["status"] == "included"
    assert control["fileCount"] > 0
    assert consumer["status"] == "included"
    assert consumer["fileCount"] > 0


def test_real_hk_plan_retains_complete_v3_control_trigger_inventory(
    tmp_path: Path,
) -> None:
    """Retain every governed v3 path family while adding the policy."""
    repo = tmp_path / "repo"
    base = _initialize_repository(repo)
    governed_paths = (
        *GOVERNED_PATHS,
        "mise.toml",
        "mise.lock",
        CONSUMER_POLICY_IMPLEMENTATION.as_posix(),
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
    step = _named_helper_step_plan(repo, base, head, STEP_NAME)

    assert set(paths) == set(governed_paths)
    assert step["status"] == "included"
    assert step["fileCount"] == len(governed_paths)


def test_real_hk_plan_policy_only_selects_v3_control_not_unrelated_product_source(  # noqa: E501
    tmp_path: Path,
) -> None:
    """Keep policy/control inputs governed without broad product matching."""
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
    policy_consumer = _named_helper_step_plan(
        policy_repo,
        policy_base,
        policy_head,
        CONSUMER_STEP_NAME,
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

    assert policy_control["status"] == "included"
    assert policy_control["fileCount"] == 1
    assert policy_consumer["status"] == "skipped"
    assert policy_consumer["fileCount"] == 0
    assert product_control["status"] == "skipped"
    assert product_control["fileCount"] == 0


def test_consumer_policy_is_one_internal_root_hk_step_not_ci_obligation() -> (
    None
):
    """Keep the policy inside root HK rather than adding a fifth lane."""
    if str(REPO_ROOT) not in sys.path:
        sys.path.insert(0, str(REPO_ROOT))
    from three_workflow_delivery_v3.records.ci import (  # noqa: PLC0415
        CI_LANE_IDS,
    )

    hk_config = HK_CONFIG.read_text(encoding="utf-8")
    v3_start = hk_config.index("local workflow_delivery_v3_validation")
    v3_end = hk_config.index("local dotenv_linter", v3_start)
    v3_config = hk_config[v3_start:v3_end]

    assert v3_config.count(f'["{CONSUMER_STEP_NAME}"]') == 1
    assert (
        "python eng/scripts/workflow_delivery_v3_consumer_policy.py "
        "--repository-root ."
    ) in v3_config
    assert "--timeout-seconds 720" in v3_config
    assert CI_LANE_IDS == (
        "root-hk",
        "project-build",
        "project-test",
        "npm-artifact-build",
    )
    assert CONSUMER_STEP_NAME not in CI_LANE_IDS


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
    ) in (markdown_config)
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
    [CONSUMER_POLICY_IMPLEMENTATION.as_posix(), "hk.pkl"],
    ids=["implementation", "root-hk-configuration"],
)
def test_real_hk_plan_triggers_consumer_policy_for_policy_definition(
    tmp_path: Path,
    path: str,
) -> None:
    """Select the policy when its implementation or registration changes."""
    repo = tmp_path / "repo"
    base = _initialize_repository(repo)
    if path == "hk.pkl":
        config = (repo / path).read_text(encoding="utf-8")
        _write(repo, path, config + "\n// consumer policy definition\n")
    else:
        _write(repo, path, "policy implementation\n")
    head = _commit(repo, "consumer policy definition")

    paths = _helper_changed_paths(repo, base, head)
    consumer = _named_helper_step_plan(
        repo,
        base,
        head,
        CONSUMER_STEP_NAME,
    )
    control = _named_helper_step_plan(repo, base, head, STEP_NAME)

    assert paths == (path,)
    assert consumer["status"] == "included"
    assert consumer["fileCount"] == 1
    assert control["status"] == "included"
    assert control["fileCount"] == 1
