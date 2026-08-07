"""Integration contracts for the root HK v3 control-test trigger."""

from __future__ import annotations

import json
import shutil
import subprocess
import sys
from dataclasses import dataclass
from functools import cache
from pathlib import Path
from typing import TYPE_CHECKING, TypedDict

import pytest

if TYPE_CHECKING:
    from collections.abc import Sequence

REPO_ROOT = Path(__file__).resolve().parents[5]
HK_CONFIG = REPO_ROOT / "hk.pkl"
HK_SUPPORT = REPO_ROOT / "src/private/lib/hk"
HK_RANGE_HELPER = Path("eng/scripts/workflow_delivery_v3_hk.py")
STEP_NAME = "v3-control-pytest"
GOVERNED_PATHS = (
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
