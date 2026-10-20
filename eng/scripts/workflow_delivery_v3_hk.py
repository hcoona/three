"""Keep complete Git impact paths separate from existing HK file operands.

When a comparison changes a committed tool lock, HK file steps whose linter
version differs between the endpoints, or whose linter cannot be resolved,
run on all their files in a second HK invocation.
"""

from __future__ import annotations

import argparse
import json
import re
import shlex
import subprocess
import sys
import tempfile
import tomllib
from pathlib import Path
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from collections.abc import Sequence

_HIGH_SURROGATE_START = 0xD800
_LOW_SURROGATE_END = 0xDFFF

# Provider of each launcher form and the committed lock that pins its tools.
PROVIDER_LOCKS = {
    "mise": "mise.lock",
    "uv": "uv.lock",
    "pnpm": "pnpm-lock.yaml",
}
PKL = ("mise", "exec", "--locked", "pkl", "--", "pkl", "eval", "-f", "json")
STEP_COMMANDS = ("check", "check_diff", "fix")
FILES_PLACEHOLDER = re.compile(r"\{\{\s*files\s*\}\}")


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


def command_linter(command: str) -> tuple[str, str] | None:
    """Return the provider and linter an `hk_exec.py` command runs."""
    words = shlex.split(command)
    if len(words) < 3 or words[1] != "eng/scripts/hk_exec.py":  # noqa: PLR2004
        return None
    rest = words[2:]
    if rest[:2] == ["uv", "run"]:
        provider, rest = "uv", rest[2:]
    elif rest[:3] == ["pnpm", "exec", "--"]:
        provider, rest = "pnpm", rest[3:]
    else:
        provider = "mise"
    if not rest or rest[0].startswith("-"):
        return None
    return provider, rest[0]


def file_step_linters(
    steps: dict[str, dict],
) -> dict[str, frozenset[tuple[str, str] | None]]:
    """Map each HK file step to its linters; `None` marks an unresolved one."""
    mapping = {}
    for name, step in steps.items():
        prefix = step.get("prefix")
        commands = [
            f"{prefix} {step[key]}" if prefix else step[key]
            for key in STEP_COMMANDS
            if step.get(key)
        ]
        if any(FILES_PLACEHOLDER.search(command) for command in commands):
            mapping[name] = frozenset(map(command_linter, commands))
    return mapping


def _evaluated_steps(repository: Path) -> dict[str, dict]:
    result = subprocess.run(
        (*PKL, "hk.pkl"),
        cwd=repository,
        check=True,
        stdout=subprocess.PIPE,
        text=True,
    )
    return json.loads(result.stdout)["hooks"]["check"]["steps"]


class _Endpoint:
    """Read committed locks at one comparison endpoint."""

    def __init__(self, repository: Path, revision: str) -> None:
        # An empty revision reads the index, as `git show :path` does.
        self.repository = repository
        self.revision = revision

    def read(self, path: str) -> bytes | None:
        result = subprocess.run(
            ("git", "show", f"{self.revision}:{path}"),
            cwd=self.repository,
            check=False,
            capture_output=True,
        )
        return result.stdout if result.returncode == 0 else None

    def pnpm_dependencies(self) -> dict[str, str] | None:
        """List the root importer's locked dependencies with `pnpm list`."""
        files = {
            name: self.read(name) for name in ("package.json", "pnpm-lock.yaml")
        }
        if None in files.values():
            return None
        with tempfile.TemporaryDirectory(prefix="hk-pnpm-") as temporary:
            for name, content in files.items():
                (Path(temporary) / name).write_bytes(content or b"")
            result = subprocess.run(
                (
                    "pnpm",
                    "list",
                    "--lockfile-only",
                    "--json",
                    "--depth",
                    "0",
                ),
                cwd=temporary,
                check=False,
                capture_output=True,
                text=True,
            )
        if result.returncode:
            return None
        (project,) = json.loads(result.stdout)
        return {
            name: dependency["version"]
            for group in (
                "dependencies",
                "devDependencies",
                "optionalDependencies",
            )
            for name, dependency in project.get(group, {}).items()
        }


def _normalize_python_name(name: str) -> str:
    return re.sub(r"[-_.]+", "-", name).lower()


class _LockedVersions:
    """Resolve a linter's locked versions from its provider's committed lock."""

    def __init__(self, repository: Path) -> None:
        self.repository = repository
        self._mise_tools: dict[str, str | None] = {}

    def _mise_tool(self, executable: str) -> str | None:
        if executable not in self._mise_tools:
            result = subprocess.run(
                ("mise", "which", "--plugin", executable),
                cwd=self.repository,
                check=False,
                capture_output=True,
                text=True,
            )
            tool = result.stdout.strip() if result.returncode == 0 else ""
            self._mise_tools[executable] = tool or None
        return self._mise_tools[executable]

    def versions(
        self, endpoint: _Endpoint, provider: str, linter: str
    ) -> frozenset[str] | None:
        """Return the linter's locked versions, or `None` when unresolved."""
        found: set[str] = set()
        if provider == "pnpm":
            dependencies = endpoint.pnpm_dependencies() or {}
            found = {
                version
                for name, version in dependencies.items()
                if linter in {name, name.rpartition("/")[2]}
            }
        else:
            content = endpoint.read(PROVIDER_LOCKS[provider])
            if content is None:
                return None
            lock = tomllib.loads(content.decode("utf-8"))
            if provider == "uv":
                found = {
                    package["version"]
                    for package in lock.get("package", [])
                    if _normalize_python_name(package["name"])
                    == _normalize_python_name(linter)
                }
            elif tool := self._mise_tool(linter):
                found = {
                    entry["version"]
                    for entry in lock.get("tools", {}).get(tool, [])
                }
        return frozenset(found) or None


def widened_steps(
    repository: Path,
    endpoints: tuple[str, str],
    paths: tuple[str, ...],
) -> dict[str, str]:
    """Return file steps that run on all files, each with its reason."""
    if not set(paths) & set(PROVIDER_LOCKS.values()):
        return {}
    base, candidate = (_Endpoint(repository, item) for item in endpoints)
    locked = _LockedVersions(repository)
    cache: dict[tuple[str, str], str | None] = {}

    def change(linter: tuple[str, str] | None) -> str | None:
        if linter is None:
            return "unresolved command"
        if linter not in cache:
            provider, name = linter
            before = locked.versions(base, provider, name)
            after = locked.versions(candidate, provider, name)
            lock = PROVIDER_LOCKS[provider]
            if before is None or after is None:
                cache[linter] = f"{name} unresolved in {lock}"
            elif before != after:
                cache[linter] = (
                    f"{name} {', '.join(sorted(before))} -> "
                    f"{', '.join(sorted(after))} in {lock}"
                )
            else:
                cache[linter] = None
        return cache[linter]

    widened = {}
    steps = file_step_linters(_evaluated_steps(repository))
    for step, linters in steps.items():
        reasons = {reason for linter in linters if (reason := change(linter))}
        if reasons:
            widened[step] = "; ".join(sorted(reasons))
    return widened


def _write_files(directory: Path, paths: tuple[str, ...]) -> Path:
    path = directory / "files.nul"
    path.write_bytes(b"".join(item.encode("utf-8") + b"\0" for item in paths))
    return path


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repository", type=Path, default=Path.cwd())
    parser.add_argument("--from-ref")
    parser.add_argument("--to-ref")
    parser.add_argument("--staged", action="store_true")
    parser.add_argument("--files0", action="store_true")
    parser.add_argument(
        "command",
        nargs=argparse.REMAINDER,
        help="Optional HK command; changed paths are appended to it.",
    )
    return parser


def main(arguments: Sequence[str] | None = None) -> int:
    """Print complete paths or invoke HK with the selected Git history."""
    parser = _parser()
    options = parser.parse_args(arguments)
    repository = options.repository.resolve()
    if options.staged:
        if options.from_ref or options.to_ref:
            parser.error("--staged cannot be combined with refs")
        paths = staged_paths(repository)
        endpoints = ("HEAD", "")
    else:
        if not options.from_ref or not options.to_ref:
            parser.error("supply --staged or both --from-ref and --to-ref")
        endpoints = (
            _resolve_commit(repository, options.from_ref),
            _resolve_commit(repository, options.to_ref),
        )
        paths = changed_paths(repository, *endpoints)
    command: list[str] = options.command
    if command and command[0] == "--":
        command = command[1:]
    if not command:
        json.dump(paths, sys.stdout)
        sys.stdout.write("\n")
        return 0
    widened = widened_steps(repository, endpoints, paths)
    for step, reason in widened.items():
        print(f"HK runs {step} on all its files: {reason}", file=sys.stderr)
    skipped = [f"--skip-step={step}" for step in widened]
    changed = _run_changed(repository, [*command, *skipped], paths, options)
    if not widened:
        return changed
    selected = [f"--step={step}" for step in widened]
    complete = subprocess.run(
        (*command, "--all", *selected),
        cwd=repository,
        check=False,
    ).returncode
    return changed or complete


def _run_changed(
    repository: Path,
    command: Sequence[str],
    paths: tuple[str, ...],
    options: argparse.Namespace,
) -> int:
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
