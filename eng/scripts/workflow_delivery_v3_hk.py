"""Run HK for every path reported by a real Git name-status range."""

from __future__ import annotations

import argparse
import importlib
import json
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import TYPE_CHECKING, Protocol

if TYPE_CHECKING:
    from collections.abc import Sequence

_HIGH_SURROGATE_START = 0xD800
_LOW_SURROGATE_END = 0xDFFF
_COMMIT_OID_LENGTH = 40


class ChangedPathError(ValueError):
    """Raised when Git returns an unsafe or malformed changed path."""


@dataclass(frozen=True, slots=True)
class ChangedRange:
    """One fully resolved Git commit range and its changed paths."""

    base_oid: str
    head_oid: str
    paths: tuple[str, ...]


class HistoryValidator(Protocol):
    """Callable merge-time history validator."""

    def __call__(
        self,
        repository: Path,
        base_oid: str,
        head_oid: str,
    ) -> None:
        """Validate one explicit base/head history range."""


_PLATFORM_ORPHAN_PATHS = frozenset(
    {
        ".github/workflow-delivery/governance/platform-orphan-run-32809578776.json",
        ".github/workflow-delivery/governance/platform-orphan-run-32809578776-result.json",
    }
)


def _is_platform_orphan_path(path: str) -> bool:
    return any(
        path == fixed_path or path.startswith(f"{fixed_path}/")
        for fixed_path in _PLATFORM_ORPHAN_PATHS
    )


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
        or "\\" in path
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
    oid = result.stdout.strip()
    if len(oid) != _COMMIT_OID_LENGTH or any(
        character not in "0123456789abcdef" for character in oid
    ):
        message = f"Git returned an invalid commit OID for {ref!r}"
        raise ChangedPathError(message)
    return oid


def changed_range(repository: Path, from_ref: str, to_ref: str) -> ChangedRange:
    """Resolve and retain both commit OIDs with their changed paths."""
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
    return ChangedRange(
        base_oid=from_oid,
        head_oid=to_oid,
        paths=parse_name_status(result.stdout),
    )


def changed_paths(
    repository: Path, from_ref: str, to_ref: str
) -> tuple[str, ...]:
    """Read changed paths from Git, including deletes and rename-away paths."""
    return changed_range(repository, from_ref, to_ref).paths


def _validate_affected_history(
    repository: Path,
    resolved: ChangedRange,
    *,
    validator: HistoryValidator | None = None,
) -> None:
    if not any(_is_platform_orphan_path(path) for path in resolved.paths):
        return
    if validator is None:
        history_module = importlib.import_module(
            "three_workflow_delivery_v3.governance.platform_orphan_history",
        )
        validator = history_module.validate_platform_orphan_history
    validator(repository, resolved.base_oid, resolved.head_oid)


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
    resolved = changed_range(
        options.repository,
        options.from_ref,
        options.to_ref,
    )
    _validate_affected_history(options.repository, resolved)
    paths = resolved.paths
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
