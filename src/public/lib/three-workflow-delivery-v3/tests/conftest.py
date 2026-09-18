"""Isolate fixture repositories and own native-tool temporary files."""

from __future__ import annotations

import subprocess
from typing import TYPE_CHECKING

import pytest

if TYPE_CHECKING:
    from collections.abc import Iterator


@pytest.fixture(scope="session", autouse=True)
def isolate_native_tool_temporary_files(
    tmp_path_factory: pytest.TempPathFactory,
) -> Iterator[None]:
    """Include native subprocess scratch and caches in pytest retention."""
    temporary = tmp_path_factory.mktemp("native-tools")
    with pytest.MonkeyPatch.context() as environment:
        for name in ("TMPDIR", "TMP", "TEMP"):
            environment.setenv(name, str(temporary))
        yield


@pytest.fixture(scope="session", autouse=True)
def isolate_hook_repository_environment() -> Iterator[None]:
    """Clear Git's repository-local variables before fixture Git commands.

    Git exports these variables to hooks. In a linked worktree, inherited
    GIT_DIR can redirect a temporary fixture commit into the real repository
    and recursively invoke its hooks. Ask Git for its documented local set;
    individual tests can still inject ambient variables with monkeypatch.
    """
    names = subprocess.run(
        ("git", "rev-parse", "--local-env-vars"),  # noqa: S607
        check=True,
        capture_output=True,
        text=True,
    ).stdout.splitlines()
    with pytest.MonkeyPatch.context() as environment:
        for name in names:
            environment.delenv(name, raising=False)
        yield
