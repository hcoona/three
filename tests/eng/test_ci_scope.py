"""Affected execution decisions and real Git comparison failures."""

from __future__ import annotations

import importlib.util
import json
import os
import subprocess
import sys
import tomllib
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "eng/scripts"
SPEC = importlib.util.spec_from_file_location(
    "ci_scope", SCRIPTS / "ci_scope.py"
)
assert SPEC is not None
assert SPEC.loader is not None
sys.path.insert(0, str(SCRIPTS))
try:
    scope = importlib.util.module_from_spec(SPEC)
    SPEC.loader.exec_module(scope)
finally:
    sys.path.remove(str(SCRIPTS))
V3_TESTS = scope.V3 + "/tests"
AZURE_TESTS = scope.AZURE + "/python/tests"
NBGV_TESTS = "src/public/lib/nbgv-python/tests"


@pytest.mark.parametrize(
    ("path", "jobs", "roots"),
    [
        ("docs/README.md", set(), set()),
        (scope.V3 + "/docs/requirements.md", set(), set()),
        ("src/public/lib/CircularList/CircularList.cs", {"dotnet"}, set()),
        (
            "src/public/lib/CircularList/Directory.Build.props",
            {"dotnet"},
            set(),
        ),
        (
            "src/public/lib/CircularList/workflow-delivery.quality.yml",
            {"dotnet"},
            set(),
        ),
        (
            "src/private/app/git-commit-heatmap/README.md",
            {"python"},
            {"src/private/app/git-commit-heatmap/tests"},
        ),
        (
            "src/public/lib/nbgv-python/src/nbgv_python/cli.py",
            {"python", "azureauth"},
            {NBGV_TESTS, AZURE_TESTS},
        ),
        (
            "src/private/app/workflow-delivery-v3-nuget-consumer/Program.cs",
            {"dotnet", "python"},
            {V3_TESTS},
        ),
        (
            "tests/private/app/workflow-delivery-v3-nuget-authority/ProgramTests.cs",
            {"dotnet", "python"},
            {V3_TESTS},
        ),
        ("src/public/lib/hexo-renderer-asciidoc/README.md", {"node"}, set()),
        ("src/public/lib/asciidoctor-latexmath/README.adoc", {"ruby"}, set()),
        (
            scope.SCHOLARLY + "/tests/test_assemble_print.py",
            {"scholarly"},
            set(),
        ),
        (
            "eng/scripts/azureauth-credprovider/New-FoundationArtifact.ps1",
            {"azureauth", "python"},
            {AZURE_TESTS},
        ),
        (
            ".github/workflows/workflow-delivery-v3-ci.yml",
            {"python"},
            {V3_TESTS},
        ),
    ],
)
def test_selects_consumers_without_unrelated_suites(path, jobs, roots):
    """Different projects and consumed metadata have independent CI owners."""
    selected = scope.select(ROOT, (path,), base="HEAD")
    assert {
        key for key, value in selected["scopes"].items() if value
    } == jobs | {"validation"}
    assert set(selected["python_roots"]) == roots
    assert all(path in why for why in selected["python_reasons"].values())


def test_shared_tool_inputs_select_native_consumers_and_full_is_explicit():
    """Native build inputs reach Python consumers; full includes every suite."""
    selected = scope.select(ROOT, ("Directory.Packages.props",), base="HEAD")
    assert {V3_TESTS, AZURE_TESTS} <= set(selected["python_roots"])
    assert selected["scopes"]["dotnet"]
    assert selected["scopes"]["azureauth"]
    full = scope.select(ROOT, (), base="", full=True)
    assert all(full["scopes"].values())
    config = tomllib.loads((ROOT / "pyproject.toml").read_text())
    assert (
        full["python_roots"]
        == config["tool"]["pytest"]["ini_options"]["testpaths"]
    )
    empty = scope.select(ROOT, (), base="HEAD")
    assert not empty["python_roots"]
    assert not empty["scopes"]["python"]


def _git(root, *arguments):
    return subprocess.run(  # noqa: S603 - Fixed tool/script and owned fixture arguments.
        [  # noqa: S607 - Git is the integration boundary.
            "git",
            "-c",
            "core.hooksPath=/dev/null",
            "-c",
            "user.name=CI scope test",
            "-c",
            "user.email=ci@example.invalid",
            *arguments,
        ],
        cwd=root,
        check=True,
        capture_output=True,
        text=True,
    ).stdout.strip()


@pytest.fixture
def comparison(tmp_path):
    """Create an owned repository with a genuine rename and deletion."""
    _git(tmp_path, "init", "-q")
    (tmp_path / "pyproject.toml").write_text(
        '[tool.uv.workspace]\nmembers = ["src/python"]\n'
        '[tool.pytest.ini_options]\ntestpaths = ["src/python/tests"]\n'
    )
    python = tmp_path / "src/python"
    python.mkdir(parents=True)
    (python / "pyproject.toml").write_text('[project]\nname = "sample"\n')
    (python / "old.py").write_text("retained = True\n" * 10)
    (python / "deleted.py").write_text("removed = True\n")
    node = tmp_path / "src/node"
    node.mkdir()
    (node / "package.json").write_text('{"name":"node"}')
    _git(tmp_path, "add", ".")
    _git(tmp_path, "commit", "-qm", "Base")
    base = _git(tmp_path, "rev-parse", "HEAD")
    (python / "old.py").rename(node / "renamed.txt")
    (python / "deleted.py").unlink()
    _git(tmp_path, "add", "-A")
    _git(tmp_path, "commit", "-qm", "Move and delete inputs")
    return tmp_path, base, _git(tmp_path, "rev-parse", "HEAD")


def _select_cli(root, *arguments):
    return subprocess.run(  # noqa: S603 - Fixed tool/script and owned fixture arguments.
        [
            sys.executable,
            str(SCRIPTS / "ci_scope.py"),
            "--repository",
            str(root),
            *arguments,
        ],
        env=dict(os.environ, GITHUB_OUTPUT=str(root / "outputs")),
        check=False,
        capture_output=True,
        text=True,
    )


def test_git_range_retains_deleted_and_both_rename_paths(comparison):
    """A rename away still schedules its old owner and the destination owner."""
    root, base, candidate = comparison
    result = _select_cli(root, "--from-ref", base, "--to-ref", candidate)
    assert result.returncode == 0, result.stderr
    selected = json.loads(result.stdout)
    assert set(selected["changed_paths"]) == {
        "src/python/deleted.py",
        "src/python/old.py",
        "src/node/renamed.txt",
    }
    assert selected["candidate"] == candidate
    assert selected["base"] == base
    assert selected["scopes"]["node"]
    assert selected["python_roots"] == ["src/python/tests"]
    assert "python=true\n" in (root / "outputs").read_text()


@pytest.mark.parametrize("failure", ["missing", "invalid", "wrong-candidate"])
def test_selection_failure_never_emits_successful_applicability(
    comparison, failure
):
    """Missing Git evidence cannot turn required checks into green skips."""
    root, base, _ = comparison
    arguments = {
        "missing": [],
        "invalid": ["--from-ref", "absent-ref"],
        "wrong-candidate": ["--from-ref", base, "--to-ref", base],
    }[failure]
    result = _select_cli(root, *arguments)
    assert result.returncode != 0
    assert not (root / "outputs").exists()
    full = _select_cli(root, "--full")
    assert full.returncode == 0, full.stderr
    assert all(json.loads(full.stdout)["scopes"].values())
