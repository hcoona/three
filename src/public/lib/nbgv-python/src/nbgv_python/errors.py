"""Custom exception types used by the nbgv-python package."""

from __future__ import annotations

from dataclasses import dataclass
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from collections.abc import Sequence


class NbgvError(RuntimeError):
    """Base exception for all wrapper-related failures."""


class NbgvNotFoundError(NbgvError):
    """Raised when the nbgv CLI executable cannot be resolved."""

    def __init__(self, search_paths: Sequence[str] | None = None) -> None:
        """Initialize the error with the paths searched."""
        self.search_paths = list(search_paths or [])
        super().__init__(
            "Unable to locate the 'nbgv' CLI. Install it with "
            "'dotnet tool install -g nbgv', add it to PATH, or set "
            "'NBGV_PYTHON_COMMAND'."
        )


@dataclass(slots=True)
class NbgvCommandError(NbgvError):
    """Represents a non-zero exit code from the nbgv CLI."""

    command: tuple[str, ...]
    returncode: int
    stdout: str | None
    stderr: str | None

    def __str__(self) -> str:  # pragma: no cover - format logic only
        """Return a string representation of the error."""
        pieces: list[str] = [
            "nbgv command failed",
            f"exit code={self.returncode}",
            f"command={' '.join(self.command)}",
        ]
        if self.stderr:
            pieces.append(f"stderr={self.stderr.strip()}")
        return "; ".join(pieces)


class NbgvJsonError(NbgvError):
    """Raised when CLI JSON output cannot be parsed."""

    def __init__(self, message: str, raw_output: str) -> None:
        """Initialize the error with the message and raw output."""
        super().__init__(message)
        self.raw_output = raw_output


class NbgvVersionNormalizationError(NbgvError):
    """Raised when a version field cannot be normalized to a PEP 440 version."""

    def __init__(self, *, field: str, value: object, message: str) -> None:
        """Initialize the error.

        Captures the source field name, the original value, and a human-readable
        error message.
        """
        self.field = field
        self.value = value
        super().__init__(message)
