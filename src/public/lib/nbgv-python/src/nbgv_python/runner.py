"""Runtime helpers that execute the `nbgv` CLI and parse its output."""

from __future__ import annotations

import json
import subprocess
from pathlib import Path
from typing import TYPE_CHECKING

from .command import discover_command
from .errors import NbgvCommandError, NbgvJsonError
from .models import GitVersion

if TYPE_CHECKING:
    from collections.abc import Iterable, Sequence


def _coerce_path(value: Path | str | None) -> str | None:
    """Convert *value* to a string path understood by `subprocess`."""
    if value is None:
        return None
    return str(Path(value))


class NbgvRunner:
    """High-level wrapper responsible for invoking the `nbgv` CLI."""

    def __init__(self, command: str | Sequence[str] | None = None) -> None:
        """Initialize the runner with an optional command override."""
        self._command = tuple(discover_command(command))

    @property
    def command(self) -> tuple[str, ...]:
        """Return the resolved base command tokens."""
        return self._command

    def run(
        self,
        args: Sequence[str],
        *,
        cwd: Path | str | None = None,
        capture_output: bool = True,
    ) -> subprocess.CompletedProcess[str]:
        """Execute `nbgv` with *args* and return the completed process."""
        return self._execute(args, cwd=cwd, capture_output=capture_output)

    def get_version(self, project_dir: Path | str = ".") -> GitVersion:
        """Return version metadata for *project_dir*."""
        project_path = Path(project_dir).expanduser().resolve(strict=False)
        args = (
            "get-version",
            "--format",
            "json",
            "--project",
            str(project_path),
        )
        process = self._execute(
            args,
            cwd=None,
            capture_output=True,
        )
        if process.stdout is None:  # pragma: no cover
            msg = "stdout is None"
            raise RuntimeError(msg)
        try:
            payload = json.loads(process.stdout)
        except json.JSONDecodeError as exc:
            message = "Failed to parse JSON emitted by 'nbgv get-version'"
            raise NbgvJsonError(message, process.stdout) from exc
        return GitVersion.from_payload(payload)

    def forward(
        self,
        args: Iterable[str] | None = None,
        *,
        cwd: Path | str | None = None,
    ) -> int:
        """Forward arguments directly to `nbgv` without capturing output.

        Raises:
            NbgvCommandError: When the underlying CLI exits with a
            non-zero code.
        """
        iterable = tuple(args or ())
        process = self._execute(iterable, cwd=cwd, capture_output=False)
        return process.returncode

    def _execute(
        self,
        args: Sequence[str],
        *,
        cwd: Path | str | None,
        capture_output: bool,
    ) -> subprocess.CompletedProcess[str]:
        """Run the command and raise `NbgvCommandError` on failure."""
        full_command = (*self._command, *args)
        try:
            return subprocess.run(  # noqa: S603 (intentional invocation)
                full_command,
                cwd=_coerce_path(cwd),
                check=True,
                capture_output=capture_output,
                text=True,
            )
        except subprocess.CalledProcessError as exc:
            stdout = exc.stdout if capture_output else None
            stderr = exc.stderr if capture_output else None
            raise NbgvCommandError(
                full_command, exc.returncode, stdout, stderr
            ) from exc


__all__ = [
    "NbgvRunner",
]
