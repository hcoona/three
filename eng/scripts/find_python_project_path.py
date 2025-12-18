# /// script
# requires-python = ">=3.14"
# dependencies = []
# ///

"""Find project directory by name in pyproject.toml files."""

from __future__ import annotations

import argparse
import sys
import tomllib
from pathlib import Path
from typing import Any


def main() -> None:
    """Find project directory by name in pyproject.toml files."""
    parser = argparse.ArgumentParser(
        description="Find project directory by name in pyproject.toml files."
    )
    parser.add_argument("project_name", help="The name of the project to find")
    parser.add_argument(
        "--root",
        default=Path(),
        type=Path,
        help="Root directory to search in",
    )
    args = parser.parse_args()

    name: str = args.project_name
    root: Path = args.root
    matches: list[Path] = []

    for path in root.rglob("pyproject.toml"):
        try:
            data: dict[str, Any] = tomllib.loads(
                path.read_text(encoding="utf-8")
            )
        except (tomllib.TOMLDecodeError, OSError) as exc:
            print(f"Skip {path}: {exc}", file=sys.stderr)
            continue

        project_table = data.get("project", {})
        if (
            isinstance(project_table, dict)
            and project_table.get("name") == name
        ):
            matches.append(path.parent)

    if not matches:
        sys.exit(f"No pyproject.toml with project.name={name} found")

    # Sort by path length to find the shortest path
    # (likely the root of the project if nested)
    matches.sort(key=lambda p: len(str(p)))
    print(matches[0])


if __name__ == "__main__":
    main()
