"""Keep complete Git impact paths separate from existing HK file operands."""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
import tempfile
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


def staged_paths(repository: Path) -> tuple[str, ...]:
    """Read both sides of staged renames and retain staged deletions."""
    result = subprocess.run(
        (
            "git",
            "diff",
            "--cached",
            "--name-status",
            "--find-renames",
            "-z",
            "--",
        ),
        cwd=repository,
        check=True,
        capture_output=True,
    )
    return parse_name_status(result.stdout)


def _write_files(directory: Path, paths: tuple[str, ...]) -> Path:
    path = directory / "files.nul"
    path.write_bytes(b"".join(item.encode("utf-8") + b"\0" for item in paths))
    return path


def _dispatch_impact(repository: Path, hook: str, *, plan: bool) -> int:
    """Delegate matching to HK using complete paths, without file linters."""
    raw_paths = sys.stdin.buffer.read().decode("utf-8", "strict")
    if raw_paths and not raw_paths.endswith("\0"):
        message = "impact input must be NUL-terminated"
        raise ChangedPathError(message)
    selected_paths = tuple(
        _canonical_repo_path(path) for path in raw_paths.split("\0")[:-1]
    )
    if hook == "pre-commit":
        # The outer pre-commit hook has already stashed unstaged changes.
        paths = tuple(
            dict.fromkeys((*selected_paths, *staged_paths(repository)))
        )
    else:
        paths = selected_paths
    with tempfile.TemporaryDirectory(prefix="hk-impact-") as temporary:
        files = _write_files(Path(temporary), paths)
        command = [
            "hk",
            "--profile",
            "small",
            "run",
            "impact-check",
            "--check",
            "--no-stage",
            "--no-progress",
            "--files0-from",
            str(files),
        ]
        if plan:
            command.extend(("--plan", "--json"))
        return subprocess.run(command, cwd=repository, check=False).returncode


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repository", type=Path, default=Path.cwd())
    parser.add_argument("--from-ref")
    parser.add_argument("--to-ref")
    parser.add_argument("--staged", action="store_true")
    parser.add_argument("--files0", action="store_true")
    parser.add_argument("--dispatch-impact", action="store_true")
    parser.add_argument("--hook", choices=("pre-commit", "check", "fix"))
    parser.add_argument("--plan-impact", action="store_true")
    parser.add_argument(
        "command",
        nargs=argparse.REMAINDER,
        help="Optional HK command; changed paths are appended to it.",
    )
    return parser


def main(arguments: Sequence[str] | None = None) -> int:
    """Print complete paths, invoke outer HK, or dispatch impact validation."""
    parser = _parser()
    options = parser.parse_args(arguments)
    repository = options.repository.resolve()
    if options.dispatch_impact:
        if not options.hook or any(
            (
                options.staged,
                options.from_ref,
                options.to_ref,
                options.files0,
                options.command,
            )
        ):
            parser.error(
                "dispatch requires --hook and does not accept a diff or command"
            )
        return _dispatch_impact(
            repository, options.hook, plan=options.plan_impact
        )
    if options.hook or options.plan_impact:
        parser.error("--hook and --plan-impact require --dispatch-impact")
    if options.staged:
        if options.from_ref or options.to_ref:
            parser.error("--staged cannot be combined with refs")
        paths = staged_paths(repository)
    else:
        if not options.from_ref or not options.to_ref:
            parser.error("supply --staged or both --from-ref and --to-ref")
        paths = changed_paths(repository, options.from_ref, options.to_ref)
    command: list[str] = options.command
    if command and command[0] == "--":
        command = command[1:]
    if not command:
        json.dump(paths, sys.stdout)
        sys.stdout.write("\n")
        return 0
    if options.files0:
        with tempfile.TemporaryDirectory(prefix="hk-changes-") as temporary:
            files = _write_files(Path(temporary), paths)
            return subprocess.run(
                (*command, "--files0-from", str(files)),
                cwd=repository,
                check=False,
            ).returncode
    result = subprocess.run(
        (*command, "--", *paths),
        cwd=repository,
        check=False,
    )
    return result.returncode


if __name__ == "__main__":
    sys.exit(main())
