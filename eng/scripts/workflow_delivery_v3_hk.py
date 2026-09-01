"""Run HK for every path reported by a real Git name-status range."""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
from pathlib import Path
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from collections.abc import Sequence

_HIGH_SURROGATE_START = 0xD800
_LOW_SURROGATE_END = 0xDFFF


class ChangedPathError(ValueError):
    """Raised when Git returns an unsafe or malformed changed path."""


def _canonical_repo_path(path: str) -> str:
    if any(
        _HIGH_SURROGATE_START <= ord(character) <= _LOW_SURROGATE_END
        for character in path
    ):
        message = "changed path contains a Unicode surrogate"
        raise ChangedPathError(message)
    if (
        not path
        or path.startswith(("/", "./"))
        or path.endswith("/")
        or any(part in {"", ".", ".."} for part in path.split("/"))
    ):
        message = f"non-canonical changed path: {path!r}"
        raise ChangedPathError(message)
    return path


def parse_name_status(output: bytes) -> tuple[str, ...]:
    """Parse `git diff --name-status -z`, retaining both rename sides."""
    try:
        fields = output.decode("utf-8", "strict").split("\0")
    except UnicodeDecodeError as error:
        message = "Git returned a non-UTF-8 changed path"
        raise ChangedPathError(message) from error
    if fields and fields[-1] == "":
        fields.pop()

    paths: list[str] = []
    seen: set[str] = set()
    index = 0
    while index < len(fields):
        status = fields[index]
        index += 1
        if not status:
            message = "Git returned an empty name-status field"
            raise ChangedPathError(message)
        path_count = 2 if status.startswith(("R", "C")) else 1
        if index + path_count > len(fields):
            message = f"incomplete Git name-status record: {status!r}"
            raise ChangedPathError(message)
        for raw_path in fields[index : index + path_count]:
            path = _canonical_repo_path(raw_path)
            if path not in seen:
                seen.add(path)
                paths.append(path)
        index += path_count
    return tuple(paths)


def _resolve_commit(repository: Path, ref: str) -> str:
    """Resolve one user-supplied ref to an unambiguous commit object ID."""
    result = subprocess.run(
        (
            "git",
            "rev-parse",
            "--verify",
            "--end-of-options",
            f"{ref}^{{commit}}",
        ),
        cwd=repository,
        check=True,
        capture_output=True,
        text=True,
    )
    return result.stdout.strip()


def changed_paths(
    repository: Path, from_ref: str, to_ref: str
) -> tuple[str, ...]:
    """Read changed paths from Git, including deletes and rename-away paths."""
    from_oid = _resolve_commit(repository, from_ref)
    to_oid = _resolve_commit(repository, to_ref)
    result = subprocess.run(
        (
            "git",
            "diff",
            "--name-status",
            "--find-renames",
            "-z",
            "--end-of-options",
            from_oid,
            to_oid,
            "--",
        ),
        cwd=repository,
        check=True,
        capture_output=True,
    )
    return parse_name_status(result.stdout)


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repository", type=Path, default=Path.cwd())
    parser.add_argument("--from-ref", required=True)
    parser.add_argument("--to-ref", required=True)
    parser.add_argument(
        "command",
        nargs=argparse.REMAINDER,
        help="Optional HK command; changed paths are appended to it.",
    )
    return parser


def main(arguments: Sequence[str] | None = None) -> int:
    """Print changed paths or run the supplied HK command for those paths."""
    options = _parser().parse_args(arguments)
    paths = changed_paths(
        options.repository,
        options.from_ref,
        options.to_ref,
    )
    command: list[str] = options.command
    if command and command[0] == "--":
        command = command[1:]
    if not command:
        json.dump(paths, sys.stdout)
        sys.stdout.write("\n")
        return 0
    result = subprocess.run(
        (*command, "--", *paths),
        cwd=options.repository,
        check=False,
    )
    return result.returncode


if __name__ == "__main__":
    sys.exit(main())
