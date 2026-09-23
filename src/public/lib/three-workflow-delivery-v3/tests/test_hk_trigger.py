"""Current root-HK and manual static-reference routing contracts."""

from __future__ import annotations

import importlib.util
import json
import os
import re
import shlex
import shutil
import subprocess
import sys
import tomllib
from dataclasses import dataclass
from functools import cache
from pathlib import Path
from typing import TYPE_CHECKING, Any, TypedDict

import pytest
import yaml

from .contracts.workflow_shell import executable, run_step

REPO_ROOT = Path(__file__).resolve().parents[5]
HK_CONFIG = REPO_ROOT / "hk.pkl"


@cache
def _effective_hooks() -> dict[str, Any]:
    result = _run(
        (
            "pkl",
            "eval",
            "-x",
            "new JsonRenderer {}.renderValue(hooks)",
            str(HK_CONFIG),
        ),
        cwd=REPO_ROOT,
    )
    return json.loads(result.stdout)


def test_root_hk_consumes_index_scanner_and_preparation() -> None:
    """Keep source checks in hooks while project suites have explicit owners."""
    hooks = _effective_hooks()
    assert hooks["pre-commit"]["stash"] == "git"
    for hook in ("check", "pre-commit"):
        steps = hooks[hook]["steps"]
        assert "hcoona-release-smoke-npm-consumer-policy" not in steps
        assert "v3-control-pytest" not in steps
        assert all(
            "pytest" not in step.get("check", "") for step in steps.values()
        )
        for name in (STATIC_REFERENCE_STEP_NAME,):
            assert PREPARATION_STEP_NAME in steps[name]["depends"]
        for name in (STATIC_REFERENCE_STEP_NAME, PREPARATION_STEP_NAME):
            assert not steps[name].get("glob")
            assert not steps[name].get("when")
        for step in steps.values():
            command = step.get("check", "")
            assert "workflow_delivery_v3_consumer_policy.py" not in command
            assert "--consumer-policy" not in command


def test_manual_worktree_static_reference_is_a_separate_mise_task(
    tmp_path: Path,
) -> None:
    """Manual inspection keeps its distinct worktree input and preparation."""
    config = tomllib.loads((REPO_ROOT / "mise.toml").read_text())
    task = config["tasks"]["check:static-reference-worktree"]
    assert "prepare:static-reference-authorities" in task["depends"]
    log = tmp_path / "argv.json"
    executable(
        tmp_path / "bin" / "uv",
        "import json, os, sys\n"
        "from pathlib import Path\n"
        f"Path({str(log)!r}).write_text(json.dumps("
        "{'argv': [Path(sys.argv[0]).name, *sys.argv[1:]], "
        "'cwd': os.getcwd()}))\n",
    )
    result = run_step(
        {"run": task["run"]},
        cwd=REPO_ROOT,
        env={"PATH": f"{tmp_path / 'bin'}{os.pathsep}{os.environ['PATH']}"},
        bindings={},
    )
    assert result.returncode == 0, result.stderr
    observation = json.loads(log.read_text())
    assert Path(observation["cwd"]) == REPO_ROOT
    command = observation["argv"]
    assert command[:2] == ["uv", "run"]
    assert (
        command[command.index("--package") + 1] == "three-workflow-delivery-v3"
    )
    assert command.index("python") < command.index(
        STATIC_REFERENCE_IMPLEMENTATION.as_posix()
    )
    assert command[command.index("--source-kind") + 1] == "worktree"
    assert command[command.index("--repository-root") + 1] == "."
    assert "--target" not in command


def test_root_hk_live_static_reference_uses_git_target_evidence_only(
    tmp_path: Path,
) -> None:
    """Keep root-HK feedback out of Live's exact-target evidence boundary."""
    static_step = _effective_hooks()["check"]["steps"][
        STATIC_REFERENCE_STEP_NAME
    ]["check"]
    log = tmp_path / "commands.jsonl"
    recorder = (
        "import json, os, sys\n"
        "from pathlib import Path\n"
        f"with Path({str(log)!r}).open('a', encoding='utf-8') as stream:\n"
        "    stream.write(json.dumps({'argv': [Path(sys.argv[0]).name, "
        "*sys.argv[1:]], 'cwd': os.getcwd()}) + '\\n')\n"
    )
    for name in ("uv", "mise"):
        executable(tmp_path / "bin" / name, recorder)
    result = run_step(
        {"run": static_step},
        cwd=REPO_ROOT,
        env={"PATH": f"{tmp_path / 'bin'}{os.pathsep}{os.environ['PATH']}"},
        bindings={},
    )
    assert result.returncode == 0, result.stderr
    observations = [json.loads(line) for line in log.read_text().splitlines()]
    assert observations
    for observation in observations:
        assert Path(observation["cwd"]) == REPO_ROOT
        arguments = observation["argv"]
        assert arguments[:2] == ["uv", "run"]
        assert STATIC_REFERENCE_IMPLEMENTATION.as_posix() in arguments
        assert arguments[arguments.index("--repository-root") + 1] == "."
        assert arguments[arguments.index("--source-kind") + 1] == "index"
        assert "--target" not in arguments
        assert all(
            not argument.startswith("--target=") for argument in arguments
        )


if TYPE_CHECKING:
    from collections.abc import Sequence


HK_SUPPORT = REPO_ROOT / "src/private/lib/hk"
HK_RANGE_HELPER = Path("eng/scripts/workflow_delivery_v3_hk.py")
PREPARATION_STEP_NAME = "static-reference-authority-preparation"
STATIC_REFERENCE_STEP_NAME = "hcoona-release-smoke-npm-static-reference"
STATIC_REFERENCE_IMPLEMENTATION = Path(
    "eng/scripts/workflow_delivery_v3_static_reference.py",
)
SCHOLARLY_STEP_NAME = "scholarly-publication-plugin-check"
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
            _write(repo, path, f"baseline: {path}\n")
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
    return _named_helper_step_plan(repo, base, head, STATIC_REFERENCE_STEP_NAME)


@pytest.mark.parametrize(
    ("hook_name", "step_name", "child_exit"),
    [
        ("check", step_name, child_exit)
        for child_exit in (0, 73)
        for step_name in (
            STATIC_REFERENCE_STEP_NAME,
            PREPARATION_STEP_NAME,
        )
    ]
    + [("pre-commit", STATIC_REFERENCE_STEP_NAME, 0)],
)
def test_root_hk_executes_consumed_commands(
    tmp_path: Path,
    hook_name: str,
    step_name: str,
    child_exit: int,
) -> None:
    """Preserve scanner inputs and child status through the watchdog."""
    command = _effective_hooks()[hook_name]["steps"][step_name]["check"]
    log = tmp_path / "argv.json"
    recorder = (
        "import json, os, sys\n"
        "from pathlib import Path\n"
        "observation = {'argv': [Path(sys.argv[0]).name, *sys.argv[1:]], "
        "'isolated': os.getsid(0) == os.getpid() "
        "if os.name != 'nt' else None}\n"
        f"Path({str(log)!r}).write_text(json.dumps(observation))\n"
        f"sys.exit({child_exit})\n"
    )
    for name in ("uv", "mise"):
        executable(tmp_path / "bin" / name, recorder)
    result = run_step(
        {"run": command},
        cwd=REPO_ROOT,
        env={"PATH": f"{tmp_path / 'bin'}{os.pathsep}{os.environ['PATH']}"},
        bindings={},
    )
    assert result.returncode == child_exit, result.stderr
    observation = json.loads(log.read_text())
    arguments = observation["argv"]
    assert re.search(r"timeout=[1-9][0-9]*s", result.stdout)
    if os.name != "nt":
        assert observation["isolated"] is True
    if step_name == PREPARATION_STEP_NAME:
        assert arguments == [
            "mise",
            "run",
            "prepare:static-reference-authorities",
        ]
    else:
        assert arguments[0] == "uv"
        assert (
            arguments[arguments.index("--package") + 1]
            == "three-workflow-delivery-v3"
        )
        assert (
            "eng/scripts/workflow_delivery_v3_static_reference.py" in arguments
        )
        assert arguments[arguments.index("--repository-root") + 1] == "."
        assert arguments[arguments.index("--source-kind") + 1] == "index"
        assert "--target" not in arguments


@pytest.mark.parametrize("preparation_exit", [0, 73])
def test_hk_orders_preparation_and_propagates_composite_failure(
    tmp_path: Path, preparation_exit: int
) -> None:
    """Schedule the consumed dependency closure through real HK."""
    repo = tmp_path / "repo"
    _initialize_empty_repository(repo)
    steps = _effective_hooks()["check"]["steps"]
    closure: set[str] = set()

    def include(name: str) -> None:
        if name not in closure:
            closure.add(name)
            for dependency in steps[name]["depends"]:
                include(dependency)

    include(STATIC_REFERENCE_STEP_NAME)
    log = tmp_path / "events.jsonl"
    recorder = tmp_path / "record.py"
    recorder.write_text(
        "import json, sys\n"
        f"with open({str(log)!r}, 'a') as stream:\n"
        "    stream.write(json.dumps(sys.argv[1]) + '\\n')\n"
        f"sys.exit({preparation_exit} if sys.argv[1] == "
        f"{PREPARATION_STEP_NAME!r} else 0)\n",
        encoding="utf-8",
    )
    registrations = []
    for name in sorted(closure):
        command = shlex.join([sys.executable, str(recorder), name])
        registrations.append(
            f"[{json.dumps(name)}] = "
            f'(root.hooks["check"].steps[{json.dumps(name)}]) '
            f"{{ check = {json.dumps(command)} }}"
        )
    configuration = REPO_ROOT / "src/private/lib/hk/Config.pkl"
    (repo / "hk.pkl").write_text(
        f"amends {json.dumps(str(configuration))}\n"
        f"import {json.dumps(str(HK_CONFIG))} as root\n"
        'hooks { ["check"] { steps {\n'
        + "\n".join(registrations)
        + "\n} } }\n",
        encoding="utf-8",
    )
    _write(
        repo,
        "src/public/lib/three-workflow-delivery-v3/tests/boundary.py",
        "# selected input\n",
    )
    _git(repo, "add", "--all")
    result = subprocess.run(  # noqa: S603
        [_hk_executable(), "--no-progress", "check", "--all", "--no-fail-fast"],
        cwd=repo,
        capture_output=True,
        text=True,
        check=False,
        timeout=60,
    )
    assert (result.returncode == 0) is (preparation_exit == 0), result.stderr
    events = [json.loads(line) for line in log.read_text().splitlines()]
    assert PREPARATION_STEP_NAME in events
    for consumer in (STATIC_REFERENCE_STEP_NAME,):
        if not preparation_exit:
            assert consumer in events
        if consumer in events:
            assert events.index(PREPARATION_STEP_NAME) < events.index(consumer)


@pytest.mark.parametrize("step_name", ["ruff", "ruff_format"])
def test_real_hk_plan_selects_python_and_notebook_inputs(
    tmp_path: Path, step_name: str
) -> None:
    """Both Ruff consumers receive Python and notebook paths with spaces."""
    repo = tmp_path / "repo"
    _initialize_repository(repo)
    selected = ("src/lab/example script.py", "src/lab/example notebook.ipynb")
    unrelated = "docs/wiki/example.txt"
    for path in (*selected, unrelated):
        _write(repo, path, "fixture\n")
    _git(repo, "add", "--all")
    plan = _named_step_plan(repo, step_name, *selected, unrelated)
    assert plan["status"] == "included"
    assert plan["fileCount"] == len(selected)
    excluded = _named_step_plan(repo, step_name, unrelated)
    assert excluded["status"] == "skipped"
    assert excluded["fileCount"] == 0


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


def test_real_hk_helper_treats_option_like_paths_as_files(
    tmp_path: Path,
) -> None:
    """Prevent changed repository paths from becoming HK CLI options."""
    repo = tmp_path / "repo"
    base = _initialize_repository(repo)
    option_like = "--skip-step=hcoona-release-smoke-npm-static-reference"
    governed = "src/public/lib/three-workflow-delivery-v3/src/control.py"
    _write(repo, option_like, "not an option\n")
    _write(repo, governed, "governed\n")
    head = _commit(repo, "option-like path")

    paths = _helper_changed_paths(repo, base, head)
    step = _helper_step_plan(repo, base, head)

    assert paths == (option_like, governed)
    assert step["status"] == "included"
    assert step["fileCount"] == len(paths)


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
    static_reference = _named_helper_step_plan(
        repo,
        base,
        head,
        STATIC_REFERENCE_STEP_NAME,
    )

    assert preparation["status"] == "included"
    assert preparation["fileCount"] == 1
    assert static_reference["status"] == "included"


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
    static_reference = _named_helper_step_plan(
        repo,
        base,
        head,
        STATIC_REFERENCE_STEP_NAME,
    )

    assert preparation["status"] == "included"
    assert preparation["fileCount"] == 1
    assert static_reference["status"] == "included"
    assert static_reference["fileCount"] == 1


def test_static_reference_is_one_internal_root_hk_step_not_ci_obligation() -> (
    None
):
    """Keep the explicit index scan inside root HK, not in a fifth CI lane."""
    if str(REPO_ROOT) not in sys.path:
        sys.path.insert(0, str(REPO_ROOT))
    from three_workflow_delivery_v3.records.ci import (  # noqa: PLC0415
        CI_LANE_IDS,
    )

    assert CI_LANE_IDS == (
        "root-hk",
        "project-build",
        "project-test",
        "npm-artifact-build",
    )
    assert STATIC_REFERENCE_STEP_NAME not in CI_LANE_IDS


@pytest.mark.parametrize(
    "step_name", ["markdownlint-cli2", "markdown-prettier"]
)
def test_markdown_steps_exclude_testagent_and_keep_staging_scope(
    tmp_path: Path, step_name: str
) -> None:
    """Observe command exclusions separately from explicit staging globs."""
    hooks = _effective_hooks()
    step = hooks["check"]["steps"][step_name]
    for hook in ("check", "pre-commit", "fix"):
        steps = hooks[hook]["steps"]
        assert {
            name
            for name, registered in steps.items()
            if ".testagent/**" in registered.get("exclude", [])
        } == {"markdownlint-cli2", "markdown-prettier"}
        assert steps[step_name]["check"]
        assert steps[step_name]["fix"]
        for selector in ("glob", "exclude", "stage"):
            assert steps[step_name][selector] == step[selector]

    repo = tmp_path / "repo"
    selected = "docs/guide with spaces.md"
    excluded = ".testagent/research.md"
    unrelated = "notes.txt"
    _initialize_repository(repo, baseline_paths=(selected, excluded, unrelated))
    assert _named_step_plan(repo, "typos", excluded)["status"] == "included"
    dirty = {
        path: f"dirty: {path}\n" for path in (selected, excluded, unrelated)
    }
    for path, content in dirty.items():
        _write(repo, path, content)

    log = tmp_path / "events.jsonl"
    recorder = tmp_path / "markdown.py"
    fixed = "# Corrected Markdown\n"
    recorder.write_text(
        "import json, sys\n"
        "from pathlib import Path\n"
        "mode, *files = sys.argv[1:]\n"
        f"with open({str(log)!r}, 'a') as stream:\n"
        "    stream.write(json.dumps({'mode': mode, 'files': files}) + '\\n')\n"
        "if mode == 'fix':\n"
        "    for path in files:\n"
        f"        Path(path).write_text({fixed!r})\n"
        f"sys.exit(any(Path(path).read_text() != {fixed!r} "
        "for path in files))\n",
        encoding="utf-8",
    )
    command = shlex.join([sys.executable, str(recorder)])
    configuration = REPO_ROOT / "src/private/lib/hk/Config.pkl"
    (repo / "hk.pkl").write_text(
        f"amends {json.dumps(str(configuration))}\n"
        f"import {json.dumps(str(HK_CONFIG))} as root\n"
        'hooks { ["check"] { steps {\n'
        f"[{json.dumps(step_name)}] = "
        f'(root.hooks["check"].steps[{json.dumps(step_name)}]) {{\n'
        f"check = {json.dumps(command + ' check {{files}}')}\n"
        f"fix = {json.dumps(command + ' fix {{files}}')}\n"
        "} } } }\n",
        encoding="utf-8",
    )
    for mode in ("check", "fix"):
        result = subprocess.run(  # noqa: S603
            [
                _hk_executable(),
                "--no-progress",
                "--profile",
                "small",
                "check",
                f"--{mode}",
                "--stage",
                "--all",
            ],
            cwd=repo,
            capture_output=True,
            text=True,
            check=False,
            timeout=30,
        )
        assert (result.returncode == 0) is (mode == "fix"), result.stderr
        events = [json.loads(line) for line in log.read_text().splitlines()]
        assert mode in {event["mode"] for event in events}
        assert all(event["files"] == [selected] for event in events)
        if mode == "check":
            assert not _git(repo, "diff", "--cached", "--name-only").stdout
    assert set(
        _git(repo, "diff", "--cached", "--name-only").stdout.splitlines()
    ) == {selected, excluded}
    assert _git(repo, "show", f":{selected}").stdout == fixed
    assert (repo / selected).read_text() == fixed
    for path in (excluded, unrelated):
        assert (repo / path).read_text() == dirty[path]
    # The explicit stage glob also includes already dirty tracked Markdown.
    assert _git(repo, "show", f":{excluded}").stdout == dirty[excluded]
    assert _git(repo, "show", f":{unrelated}").stdout == (
        f"baseline: {unrelated}\n"
    )


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

    assert paths == (path,)
    assert static_reference["status"] == "included"
    assert static_reference["fileCount"] == 1


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


def test_hk_helper_rejects_non_utf8_path_before_child_execution(
    monkeypatch: pytest.MonkeyPatch,
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

    def fake_run(
        command: Sequence[str],
        **_kwargs: object,
    ) -> SimpleNamespace:
        arguments = tuple(command)
        if arguments[:2] == ("git", "rev-parse"):
            resolved_oid = (
                from_oid if arguments[-1] == "base^{commit}" else to_oid
            )
            return SimpleNamespace(stdout=f"{resolved_oid}\n")
        if arguments[:2] == ("git", "diff"):
            return SimpleNamespace(stdout=b"M\0invalid-\xff.py\0")
        pytest.fail(f"Unexpected subprocess before path rejection: {arguments}")

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
    assert str(error.value) == "Git returned a non-UTF-8 changed path"
    assert type(error.value.__cause__) is UnicodeDecodeError


def test_mise_bootstrap_preserves_preparation_and_build_permissions(
    tmp_path: Path,
) -> None:
    """Keep frozen authority preparation before permitted native rebuilds."""
    mise_config = tomllib.loads(
        (REPO_ROOT / "mise.toml").read_text(encoding="utf-8"),
    )
    tasks = mise_config["tasks"]
    preparation_name = "prepare:static-reference-authorities"
    bootstrap_dependencies = tuple(tasks["bootstrap"]["depends"])
    node_bootstrap = tasks["bootstrap:node"]
    preparation = tasks[preparation_name]

    assert "bootstrap:node" in bootstrap_dependencies
    assert preparation_name in node_bootstrap["depends"]
    recorder = (
        "import json, os, sys\n"
        "from pathlib import Path\n"
        "with Path(os.environ['COMMAND_LOG']).open('a', "
        "encoding='utf-8') as stream:\n"
        "    stream.write(json.dumps([Path(sys.argv[0]).name, "
        "*sys.argv[1:]]) + '\\n')\n"
    )
    for name in ("uv", "pnpm"):
        executable(tmp_path / "bin" / name, recorder)
    observations = {}
    for name, task in (
        ("preparation", preparation),
        ("rebuild", node_bootstrap),
    ):
        log = tmp_path / f"{name}.jsonl"
        result = run_step(
            {"run": task["run"]},
            cwd=REPO_ROOT,
            env={
                "PATH": f"{tmp_path / 'bin'}{os.pathsep}{os.environ['PATH']}",
                "COMMAND_LOG": str(log),
            },
            bindings={},
        )
        assert result.returncode == 0, result.stderr
        observations[name] = [
            json.loads(line) for line in log.read_text().splitlines()
        ]
        assert observations[name]
    for command in observations["preparation"]:
        assert command[:2] == ["uv", "run"]
        assert "--isolated" in command
        assert "--frozen" in command
        assert "--python" not in command
        assert command[command.index("--package") + 1] == (
            "three-workflow-delivery-v3"
        )
        python_index = command.index("python")
        assert command[python_index:] == [
            "python",
            "-B",
            "eng/scripts/workflow_delivery_v3_prepare_static_reference.py",
        ]
    for command in observations["rebuild"]:
        assert command[0] == "pnpm"
        assert "-r" in command or "--recursive" in command
        assert "rebuild" in command
        assert "--pending" in command

    workspace = yaml.safe_load(
        (REPO_ROOT / "pnpm-workspace.yaml").read_text(encoding="utf-8")
    )
    assert workspace["allowBuilds"]["msgpackr-extract"] is True


def test_real_hk_selects_deleted_buddy_routes(tmp_path: Path) -> None:
    """Keep deleted legacy entry names inside real v3 validation selection."""
    repo = tmp_path / "repo"
    paths = (
        ".github/workflows/buddy.yml",
        ".github/workflows/release-buddy.yml",
    )
    base = _initialize_repository(repo, baseline_paths=paths)
    _git(repo, "rm", "--", *paths)
    head = _commit(repo, "delete retired Buddy routes")

    assert _helper_changed_paths(repo, base, head) == paths
    assert not (repo / paths[0]).exists()
    assert not (repo / paths[1]).exists()
    step = _helper_step_plan(repo, base, head)
    assert step["status"] == "included"
    assert step["fileCount"] == 2  # noqa: PLR2004


def test_real_hk_selects_renamed_buddy_route(tmp_path: Path) -> None:
    """Select both names of a real unchanged-content compatibility rename."""
    repo = tmp_path / "repo"
    old = ".github/workflows/buddy.yml"
    new = ".github/workflows/compatibility.yml"
    base = _initialize_repository(repo, baseline_paths=(old,))
    _git(repo, "mv", "--", old, new)
    head = _commit(repo, "rename retired Buddy route")

    assert (
        _git(
            repo, "diff", "--name-status", "--find-renames", base, head, "--"
        ).stdout
        == f"R100\t{old}\t{new}\n"
    )
    assert _helper_changed_paths(repo, base, head) == (old, new)
    step = _helper_step_plan(repo, base, head)
    assert step["status"] == "included"
    assert step["fileCount"] == 2  # noqa: PLR2004


def test_real_hk_selects_future_buddy_route(tmp_path: Path) -> None:
    """Select a newly introduced workflow without requiring a v3 prefix."""
    repo = tmp_path / "repo"
    base = _initialize_repository(repo)
    path = ".github/workflows/new-buddy-compatibility.yml"
    _write(repo, path, "new compatibility route\n")
    head = _commit(repo, "add compatibility route")

    assert _helper_changed_paths(repo, base, head) == (path,)
    step = _helper_step_plan(repo, base, head)
    assert step["status"] == "included"
    assert step["fileCount"] == 1
