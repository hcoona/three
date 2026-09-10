"""Keep fixture repositories independent of the invoking Git hook."""

from __future__ import annotations

import subprocess
from typing import TYPE_CHECKING

import pytest

if TYPE_CHECKING:
    from collections.abc import Iterator


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
