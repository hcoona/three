"""Utilities for resolving the nbgv command line executable."""

from __future__ import annotations

import os
import shlex
import shutil
from collections.abc import Callable, Iterable, Mapping, Sequence

from .errors import NbgvNotFoundError

ENV_COMMAND_OVERRIDE = "NBGV_PYTHON_COMMAND"


def _is_string_sequence(value: object) -> bool:
    """Return True when *value* behaves like a sequence of strings."""
    if isinstance(value, (bytes, bytearray)):
        return False
    if isinstance(value, str):
        return False
    return isinstance(value, Iterable)


def parse_command_tokens(command: str | Sequence[str]) -> list[str]:
    """Normalise a command specification into a list of tokens."""
    if isinstance(command, str):
        text = command.strip()
        if not text:
            msg = "Command string cannot be empty"
            raise ValueError(msg)
        tokens = shlex.split(text, posix=os.name != "nt")
    else:
        if not _is_string_sequence(command):
            msg = "Command must be a string or an iterable of strings"
            raise TypeError(msg)
        tokens = [str(part) for part in command if str(part).strip()]
    cleaned: list[str] = []
    for token in tokens:
        stripped = token.strip()
        if (
            len(stripped) >= 2  # noqa: PLR2004
            and stripped[0] == stripped[-1]
            and stripped[0] in {'"', "'"}
        ):
            stripped = stripped[1:-1]
        if stripped:
            cleaned.append(stripped)
    if not cleaned:
        msg = "Command sequence cannot be empty"
        raise ValueError(msg)
    return cleaned


def discover_command(
    command: str | Sequence[str] | None = None,
    *,
    env: Mapping[str, str] | None = None,
    which: Callable[[str], str | None] = shutil.which,
) -> list[str]:
    """Return the command tokens that should be used to call `nbgv`."""
    if command is not None:
        return parse_command_tokens(command)

    environment = os.environ if env is None else env
    override = environment.get(ENV_COMMAND_OVERRIDE)
    if override:
        return parse_command_tokens(override)

    resolved = which("nbgv")
    if resolved:
        return [resolved]

    dotnet_path = which("dotnet")
    if dotnet_path:
        return [dotnet_path, "tool", "run", "nbgv"]

    search_paths = []
    path_value = environment.get("PATH", "")
    if path_value:
        search_paths = path_value.split(os.pathsep)
    raise NbgvNotFoundError(search_paths)


__all__ = [
    "ENV_COMMAND_OVERRIDE",
    "discover_command",
    "parse_command_tokens",
]
