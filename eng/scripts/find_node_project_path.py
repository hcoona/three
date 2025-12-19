# /// script
# requires-python = ">=3.14"
# dependencies = []
# ///

"""Find Node.js project directory by name in package.json files.

We consider a project a match if `package.json` has a top-level `name` exactly
matching the requested project name.
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any


def main() -> None:
    """Search for a package.json with a matching top-level `name` field."""
    parser = argparse.ArgumentParser(
        description=(
            "Find Node.js project directory by name in package.json files."
        )
    )
    parser.add_argument("project_name", help="The package.json name to find")
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

    for path in root.rglob("package.json"):
        # Avoid scanning huge dependency folders if they exist in the workspace.
        # (GitHub runners and clean checkouts usually don't have these,
        # but keep it safe.)
        if any(
            part in {"node_modules", ".git", ".output"} for part in path.parts
        ):
            continue

        try:
            data: dict[str, Any] = json.loads(path.read_text(encoding="utf-8"))
        except (json.JSONDecodeError, OSError) as exc:
            print(f"Skip {path}: {exc}", file=sys.stderr)
            continue

        if data.get("name") == name:
            matches.append(path.parent)

    if not matches:
        sys.exit(f"No package.json with name={name} found")

    matches.sort(key=lambda p: len(str(p)))
    print(matches[0])


if __name__ == "__main__":
    main()
