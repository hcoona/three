"""HK conformance widens to all covered files when a linter lock changes."""

from __future__ import annotations

import json
import os
import subprocess
import sys
from pathlib import Path
from typing import Any

import pytest

REPO_ROOT = Path(__file__).resolve().parents[2]
HELPER = REPO_ROOT / "eng/scripts/workflow_delivery_v3_hk.py"
SEEDED = "SEEDED_FINDING"
HK_EXEC = "python eng/scripts/hk_exec.py"
# Commands keep the evaluated root hk.pkl forms of the steps they stand for.
FILES = "--hk-files {{files}}"
RUFF_FORMAT = f"{HK_EXEC} uv run ruff format --quiet --force-exclude"
STEPS: dict[str, dict[str, Any]] = {
    "ruff": {
        "glob": ["*.py"],
        "check": f"{HK_EXEC} uv run ruff check --force-exclude {FILES}",
        "fix": f"{HK_EXEC} uv run ruff check --force-exclude --fix {FILES}",
    },
    "ruff_format": {
        "glob": ["*.py"],
        "check": f"{RUFF_FORMAT} --check {FILES}",
        "check_diff": f"{RUFF_FORMAT} --diff {FILES}",
    },
    "markdown-prettier": {
        "glob": ["*.md"],
        "check": f"{HK_EXEC} pnpm exec -- prettier --check {FILES}",
    },
    "shellcheck": {
        "glob": ["*.sh"],
        "check": f"{HK_EXEC} shellcheck {FILES}",
    },
    "uv-lock": {"glob": ["uv.lock"], "check": "uv lock --check"},
}
MISE_TOOLS = {"shellcheck": "github:koalaman/shellcheck", "pkl": "pkl"}
HK_COMMAND = ("hk", "check", "--check", "--no-stage")

FAKE_MISE = r"""
import json
import os
import sys

arguments = sys.argv[1:]
if arguments[:4] == ["exec", "--locked", "pkl", "--"]:
    steps = json.loads(os.environ["FIXTURE_STEPS"])
    print(json.dumps({"hooks": {"check": {"steps": steps}}}))
elif arguments[:2] == ["which", "--plugin"]:
    tool = json.loads(os.environ["FIXTURE_MISE_TOOLS"]).get(arguments[2])
    if tool is None:
        sys.exit("mise ERROR no tool provides the executable")
    print(tool)
else:
    sys.exit(f"unexpected mise call: {arguments}")
"""
FAKE_PNPM = r"""
import json
import sys
from pathlib import Path

import yaml

assert sys.argv[1:] == ["list", "--lockfile-only", "--json", "--depth", "0"]
lock = yaml.safe_load(Path("pnpm-lock.yaml").read_text(encoding="utf-8"))
root = lock["importers"]["."]
print(json.dumps([{
    group: {
        name: {"version": item["version"].split("(")[0]}
        for name, item in root.get(group, {}).items()
    }
    for group in ("dependencies", "devDependencies")
}]))
"""
# Applies step globs to the selected files and fails on a seeded finding.
FAKE_HK = r"""
import json
import os
import subprocess
import sys
from pathlib import Path, PurePosixPath

arguments = sys.argv[1:]
steps = json.loads(os.environ["FIXTURE_STEPS"])
if "--" in arguments:
    files = arguments[arguments.index("--") + 1:]
elif "--files0-from" in arguments:
    path = Path(arguments[arguments.index("--files0-from") + 1])
    files = [item for item in path.read_text().split("\0") if item]
else:
    assert "--all" in arguments
    files = subprocess.run(
        ["git", "ls-files"], check=True, capture_output=True, text=True
    ).stdout.split()
selected = [item.split("=", 1)[1] for item in arguments
            if item.startswith("--step=")]
skipped = {item.split("=", 1)[1] for item in arguments
           if item.startswith("--skip-step=")}
ran = {}
for name, step in steps.items():
    if (selected and name not in selected) or name in skipped:
        continue
    matched = sorted(
        item for item in files
        if any(PurePosixPath(item).match(glob) for glob in step["glob"])
    )
    if matched:
        ran[name] = matched
with Path(os.environ["COMMAND_LOG"]).open("a", encoding="utf-8") as stream:
    command = ["hk", *arguments]
    stream.write(json.dumps({"arguments": command, "ran": ran}) + "\n")
sys.exit(int(any(
    "SEEDED_FINDING" in Path(item).read_text(encoding="utf-8")
    for matched in ran.values()
    for item in matched
    if Path(item).exists()
)))
"""


def _executable(path: Path, body: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(f"#!{sys.executable}\n{body}\n", encoding="utf-8")
    path.chmod(0o755)


def _git(repository: Path, *arguments: str) -> str:
    return subprocess.run(  # noqa: S603
        ("git", *arguments),  # noqa: S607
        cwd=repository,
        check=True,
        capture_output=True,
        text=True,
    ).stdout.strip()


def _uv_lock(versions: dict[str, str]) -> str:
    return "version = 1\n" + "".join(
        f'\n[[package]]\nname = "{name}"\nversion = "{version}"\n'
        for name, version in versions.items()
    )


def _mise_lock(versions: dict[str, str]) -> str:
    return "".join(
        f'[[tools."{tool}"]]\nversion = "{version}"\nbackend = "{tool}"\n\n'
        for tool, version in versions.items()
    )


def _pnpm_lock(versions: dict[str, str]) -> str:
    entries = "".join(
        f"      '{name}':\n        specifier: {version}\n"
        f"        version: {version}\n"
        for name, version in versions.items()
    )
    return (
        "lockfileVersion: '9.0'\n\nimporters:\n\n  .:\n"
        f"    devDependencies:\n{entries}"
    )


BASE_LOCKS = {
    "uv.lock": {"ruff": "0.17.0", "pytest": "9.1.0"},
    "mise.lock": {"github:koalaman/shellcheck": "0.11.0", "pkl": "0.32.1"},
    "pnpm-lock.yaml": {"prettier": "3.9.9", "ap-style-title-case": "2.0.0"},
}
WRITERS = {"uv.lock": _uv_lock, "mise.lock": _mise_lock}


def _write_locks(repository: Path, locks: dict[str, dict[str, str]]) -> None:
    for name, versions in locks.items():
        writer = WRITERS.get(name, _pnpm_lock)
        (repository / name).write_text(writer(versions), encoding="utf-8")


@pytest.fixture
def fixture(tmp_path: Path) -> dict[str, Any]:
    """Commit a base with an existing finding and controlled HK tools."""
    repository = tmp_path / "repository"
    repository.mkdir()
    _git(repository, "init", "--quiet", "--initial-branch=main")
    _git(repository, "config", "user.email", "fixture@example.invalid")
    _git(repository, "config", "user.name", "Fixture")
    files = {
        "hk.pkl": "// Evaluated by the controlled mise.\n",
        "package.json": "{}\n",
        "changed.py": "VALUE = 1\n",
        "unchanged.py": f"# {SEEDED} predates the comparison.\n",
        "README.md": "# Fixture\n",
        "build.sh": "echo build\n",
    }
    for name, content in files.items():
        (repository / name).write_text(content, encoding="utf-8")
    _write_locks(repository, BASE_LOCKS)
    _git(repository, "add", "--all")
    _git(repository, "commit", "--quiet", "--message", "Base")
    tools = tmp_path / "tools"
    for name, body in {
        "mise": FAKE_MISE,
        "pnpm": FAKE_PNPM,
        "hk": FAKE_HK,
    }.items():
        _executable(tools / name, body)
    env = {
        **os.environ,
        "PATH": f"{tools}{os.pathsep}{os.environ['PATH']}",
        "TMPDIR": str(tmp_path),
        "COMMAND_LOG": str(tmp_path / "hk.jsonl"),
        "FIXTURE_STEPS": json.dumps(STEPS),
        "FIXTURE_MISE_TOOLS": json.dumps(MISE_TOOLS),
    }
    return {"repository": repository, "env": env, "log": tmp_path / "hk.jsonl"}


def _commit_candidate(
    repository: Path, locks: dict[str, dict[str, str]]
) -> tuple[str, str]:
    base = _git(repository, "rev-parse", "HEAD")
    (repository / "changed.py").write_text("VALUE = 2\n", encoding="utf-8")
    _write_locks(repository, locks)
    _git(repository, "commit", "--quiet", "--all", "--message", "Candidate")
    return base, _git(repository, "rev-parse", "HEAD")


def _run(
    fixture: dict[str, Any], endpoints: tuple[str, str]
) -> tuple[subprocess.CompletedProcess[str], list[dict[str, Any]]]:
    base, candidate = endpoints
    result = subprocess.run(  # noqa: S603
        (
            sys.executable,
            str(HELPER),
            "--repository",
            str(fixture["repository"]),
            "--from-ref",
            base,
            "--to-ref",
            candidate,
            "--files0",
            "--",
            *HK_COMMAND,
        ),
        env=fixture["env"],
        check=False,
        capture_output=True,
        text=True,
    )
    log: Path = fixture["log"]
    return result, [json.loads(line) for line in log.read_text().splitlines()]


def _bumped(lock: str, name: str, version: str) -> dict[str, dict[str, str]]:
    return {**BASE_LOCKS, lock: {**BASE_LOCKS[lock], name: version}}


def test_ruff_bump_checks_all_covered_files_and_fails_on_unchanged_finding(
    fixture: dict[str, Any],
) -> None:
    """Both ruff steps reach the unchanged file; other steps keep scope."""
    endpoints = _commit_candidate(
        fixture["repository"], _bumped("uv.lock", "ruff", "0.17.1")
    )
    result, runs = _run(fixture, endpoints)
    assert result.returncode == 1
    assert "ruff 0.17.0 -> 0.17.1 in uv.lock" in result.stderr
    changed, complete = runs
    assert changed["ran"] == {"uv-lock": ["uv.lock"]}
    assert complete["arguments"][: len(HK_COMMAND)] == list(HK_COMMAND)
    assert "--all" in complete["arguments"]
    assert complete["ran"] == {
        "ruff": ["changed.py", "unchanged.py"],
        "ruff_format": ["changed.py", "unchanged.py"],
    }


@pytest.mark.parametrize(
    ("lock", "name", "version", "step"),
    [
        ("pnpm-lock.yaml", "prettier", "3.9.10", "markdown-prettier"),
        ("mise.lock", "github:koalaman/shellcheck", "0.11.1", "shellcheck"),
    ],
)
def test_each_provider_lock_widens_only_its_linter_steps(
    fixture: dict[str, Any], lock: str, name: str, version: str, step: str
) -> None:
    """A pnpm or mise linter bump widens the steps that invoke it."""
    endpoints = _commit_candidate(
        fixture["repository"], _bumped(lock, name, version)
    )
    result, runs = _run(fixture, endpoints)
    assert result.returncode == 0, result.stderr
    changed, complete = runs
    assert f"--skip-step={step}" in changed["arguments"]
    assert changed["ran"] == {
        "ruff": ["changed.py"],
        "ruff_format": ["changed.py"],
    }
    assert list(complete["ran"]) == [step]


def test_unrelated_lock_change_keeps_changed_file_scope(
    fixture: dict[str, Any],
) -> None:
    """Launcher, runtime and other packages do not widen any step."""
    locks = _bumped("uv.lock", "pytest", "9.2.0")
    locks["mise.lock"] = {**locks["mise.lock"], "uv": "0.13.1"}
    locks["pnpm-lock.yaml"] = {
        **locks["pnpm-lock.yaml"],
        "ap-style-title-case": "2.1.0",
    }
    endpoints = _commit_candidate(fixture["repository"], locks)
    result, runs = _run(fixture, endpoints)
    assert result.returncode == 0, result.stderr
    (changed,) = runs
    assert changed["arguments"][: len(HK_COMMAND) + 1] == [
        *HK_COMMAND,
        "--files0-from",
    ]
    assert changed["ran"] == {
        "ruff": ["changed.py"],
        "ruff_format": ["changed.py"],
        "uv-lock": ["uv.lock"],
    }


@pytest.mark.parametrize(
    ("step", "check"),
    [
        ("markdown-prettier", "python eng/scripts/hk_actionlint.py {{files}}"),
        ("markdown-prettier", f"{HK_EXEC} pnpm exec -- taplo check {FILES}"),
        ("shellcheck", None),
    ],
    ids=["unresolved-command", "unlocked-pnpm-bin", "unresolved-mise-tool"],
)
def test_unresolved_mapping_widens_when_a_tool_lock_changes(
    fixture: dict[str, Any], step: str, check: str | None
) -> None:
    """A step with no resolvable linter lock runs on all its files."""
    if check:
        steps = {**STEPS, step: {**STEPS[step], "check": check}}
        fixture["env"]["FIXTURE_STEPS"] = json.dumps(steps)
    else:
        tools = {
            name: tool for name, tool in MISE_TOOLS.items() if name != step
        }
        fixture["env"]["FIXTURE_MISE_TOOLS"] = json.dumps(tools)
    endpoints = _commit_candidate(
        fixture["repository"], _bumped("uv.lock", "pytest", "9.2.0")
    )
    result, runs = _run(fixture, endpoints)
    assert result.returncode == 0, result.stderr
    changed, complete = runs
    assert f"--skip-step={step}" in changed["arguments"]
    assert list(complete["ran"]) == [step]
