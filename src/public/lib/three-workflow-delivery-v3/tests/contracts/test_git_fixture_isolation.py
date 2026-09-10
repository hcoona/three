"""A real hook environment must not redirect fixture Git operations."""

from __future__ import annotations

import os
import subprocess
import sys
from pathlib import Path


def test_hook_environment_keeps_fixture_commits_outside_origin(
    tmp_path: Path,
) -> None:
    """Preserve the invoking repository's config, index, and unborn HEAD."""
    origin = tmp_path / "origin"
    subprocess.run(  # noqa: S603
        ("git", "init", "--quiet", str(origin)),  # noqa: S607
        check=True,
    )
    config = origin / ".git/config"
    before = config.read_bytes()
    head = (origin / ".git/HEAD").read_bytes()
    fixture_test = Path(__file__).with_name("test_nuget_authoring.py")
    environment = {
        **os.environ,
        "GIT_DIR": str(origin / ".git"),
        "GIT_WORK_TREE": str(origin),
        "GIT_INDEX_FILE": str(origin / ".git/index"),
    }
    result = subprocess.run(  # noqa: S603
        (
            sys.executable,
            "-m",
            "pytest",
            "-q",
            str(fixture_test)
            + "::test_nuget_authoring_and_npm_select_independent_units",
            "--basetemp",
            str(tmp_path / "nested-pytest"),
        ),
        env=environment,
        check=False,
        capture_output=True,
        text=True,
        timeout=60,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert "1 passed" in result.stdout
    assert config.read_bytes() == before
    assert (origin / ".git/HEAD").read_bytes() == head
    assert not (origin / ".git/index").exists()
    assert not tuple((origin / ".git/refs/heads").iterdir())
