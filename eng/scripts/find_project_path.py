# /// script
# requires-python = ">=3.14"
# dependencies = []
# ///

"""Find a project directory by package identity.

This is a unified detector for projects in this monorepo.

Supported kinds:
- Python: pyproject.toml with [project].name == <project>
- Node: package.json with top-level name == <project>
- Ruby: <project>.gemspec file name (exact match)

Exit code contract:
- 0: unique match; print exactly one JSON object on stdout (single line)
- 2: ambiguous match; print diagnostics to stderr
- 3: not found; print diagnostics to stderr
- 1: unexpected error

The implementation intentionally uses `fd` for file discovery to keep scanning
fast and consistent across runners.
"""

from __future__ import annotations

import argparse
import json
import re
import shutil
import subprocess
import sys
import tomllib
from dataclasses import dataclass
from pathlib import Path
from typing import TYPE_CHECKING, Any

if TYPE_CHECKING:
    from collections.abc import Iterable

_EXCLUDES: tuple[str, ...] = (
    ".git",
    "node_modules",
    "obj",
    "bin",
    ".venv",
    ".tox",
    ".pytest_cache",
    ".ruff_cache",
    ".mypy_cache",
    "__pycache__",
    ".pnpm-store",
    "dist",
    "build",
    "out",
)


@dataclass(frozen=True)
class _Match:
    kind: str
    package_dir: Path
    evidence: Path


def _run_fd(pattern: str, root: Path) -> list[Path]:
    if shutil.which("fd") is None:
        msg = "fd not found on PATH (required for project discovery)"
        raise RuntimeError(msg)

    cmd: list[str] = [
        "fd",
        "--type",
        "f",
        "--hidden",
        "--no-ignore-vcs",
    ]

    for ex in _EXCLUDES:
        cmd.extend(["--exclude", ex])

    cmd.extend([pattern, str(root)])

    proc = subprocess.run(
        cmd,
        check=False,
        capture_output=True,
        text=True,
    )

    if proc.returncode != 0:
        stderr = proc.stderr.strip()
        msg = f"fd failed (exit {proc.returncode}): {stderr}"
        raise RuntimeError(msg)

    paths: list[Path] = []
    for line in proc.stdout.splitlines():
        s = line.strip()
        if not s:
            continue
        paths.append(Path(s))
    return paths


def _safe_read_text(path: Path) -> str | None:
    try:
        return path.read_text(encoding="utf-8")
    except OSError as exc:
        print(f"Skip {path}: {exc}", file=sys.stderr)
        return None


def _python_matches(project: str, candidates: Iterable[Path]) -> list[_Match]:
    matches: list[_Match] = []
    for pyproject in candidates:
        text = _safe_read_text(pyproject)
        if text is None:
            continue
        try:
            data: dict[str, Any] = tomllib.loads(text)
        except tomllib.TOMLDecodeError as exc:
            print(f"Skip {pyproject}: {exc}", file=sys.stderr)
            continue

        project_table = data.get("project")
        if (
            isinstance(project_table, dict)
            and project_table.get("name") == project
        ):
            matches.append(
                _Match(
                    kind="python",
                    package_dir=pyproject.parent,
                    evidence=pyproject,
                )
            )
    return matches


def _node_matches(project: str, candidates: Iterable[Path]) -> list[_Match]:
    matches: list[_Match] = []
    for package_json in candidates:
        text = _safe_read_text(package_json)
        if text is None:
            continue
        try:
            data: dict[str, Any] = json.loads(text)
        except json.JSONDecodeError as exc:
            print(f"Skip {package_json}: {exc}", file=sys.stderr)
            continue

        if data.get("name") == project:
            matches.append(
                _Match(
                    kind="node",
                    package_dir=package_json.parent,
                    evidence=package_json,
                )
            )
    return matches


def _ruby_matches(project: str, candidates: Iterable[Path]) -> list[_Match]:
    matches: list[_Match] = []
    expected = f"{project}.gemspec"
    for gemspec in candidates:
        if gemspec.name != expected:
            # fd pattern should already be exact; keep defensive.
            continue
        matches.append(
            _Match(kind="ruby", package_dir=gemspec.parent, evidence=gemspec)
        )
    return matches


def _format_matches(title: str, matches: list[_Match]) -> str:
    if not matches:
        return f"{title}: none"
    lines = [f"{title}: {len(matches)}"]
    for m in matches:
        lines.append(f"  - {m.package_dir} (via {m.evidence})")
    return "\n".join(lines)


def main() -> None:
    """Find and print the unique project match as a single-line JSON object."""
    parser = argparse.ArgumentParser(
        description="Find package directory and kind for a given project name."
    )
    parser.add_argument("project", help="Project/package name to locate")
    parser.add_argument(
        "--root",
        type=Path,
        default=Path(),
        help="Root directory to search from (default: cwd)",
    )
    args = parser.parse_args()

    project: str = args.project
    root: Path = args.root

    if not re.fullmatch(r"[A-Za-z0-9._-]+", project):
        msg = (
            f"Invalid project name: {project}. Allowed characters: "
            "A-Z a-z 0-9 . _ -"
        )
        print(
            msg,
            file=sys.stderr,
        )
        sys.exit(1)

    try:
        pyprojects = _run_fd(r"^pyproject\.toml$", root)
        package_jsons = _run_fd(r"^package\.json$", root)
        gemspecs = _run_fd(rf"^{re.escape(project)}\.gemspec$", root)

        python = _python_matches(project, pyprojects)
        node = _node_matches(project, package_jsons)
        ruby = _ruby_matches(project, gemspecs)

        all_matches = python + node + ruby

        if not all_matches:
            print(
                "No matching project found. Searched for:\n"
                + _format_matches("python", python)
                + "\n"
                + _format_matches("node", node)
                + "\n"
                + _format_matches("ruby", ruby),
                file=sys.stderr,
            )
            sys.exit(3)

        if len(all_matches) != 1:
            print(
                "Ambiguous project resolution. Found multiple matches:\n"
                + _format_matches("python", python)
                + "\n"
                + _format_matches("node", node)
                + "\n"
                + _format_matches("ruby", ruby),
                file=sys.stderr,
            )
            sys.exit(2)

        m = all_matches[0]
        # Print relative path if possible to keep outputs stable.
        try:
            package_dir = m.package_dir.resolve().relative_to(root.resolve())
        except ValueError:
            package_dir = m.package_dir

        out = {"package_dir": package_dir.as_posix(), "project_kind": m.kind}
        sys.stdout.write(json.dumps(out, separators=(",", ":")))
        sys.stdout.write("\n")
        sys.exit(0)

    except SystemExit:
        raise
    except Exception as exc:  # noqa: BLE001
        print(f"Unexpected error: {exc}", file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()
