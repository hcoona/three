# /// script
# requires-python = ">=3.14"
# dependencies = []
# ///

"""Prepare or restore a Node package name for GitHub Packages publishing.

GitHub Packages npm registry requires a scoped package name. This script can
optionally add a scope for publishing, and restore the original name after.
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any


def read_package_json(path: Path) -> dict[str, Any]:
    """Read a JSON file that must contain a top-level object."""
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        sys.exit(f"Failed to read {path}: {exc}")
    if not isinstance(data, dict):
        sys.exit(f"Invalid package.json structure in {path}")
    return data


def write_package_json(path: Path, data: dict[str, Any]) -> None:
    """Write a JSON object to disk using npm-friendly formatting."""
    path.write_text(
        json.dumps(data, indent=2, ensure_ascii=True) + "\n", encoding="utf-8"
    )


def normalize_scope(scope: str) -> str:
    """Normalize and validate an npm scope string (without the leading '@')."""
    normalized = scope.strip()
    if normalized.startswith("@"):
        normalized = normalized[1:]
    normalized = normalized.lower()
    if not normalized:
        sys.exit("Scope is empty after normalization")
    if "/" in normalized:
        sys.exit("Scope must not include '/' characters")
    return normalized


def write_outputs(output_path: Path, values: dict[str, str]) -> None:
    """Write key=value pairs to a GitHub Actions output file."""
    lines = [f"{key}={value}" for key, value in values.items()]
    output_path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def prepare(
    package_dir: Path, scope: str, output: Path | None, state: Path
) -> None:
    """Prepare a package for publishing by applying an npm scope when needed."""
    package_json = package_dir / "package.json"
    if not package_json.exists():
        sys.exit(f"Missing package.json at {package_json}")

    data = read_package_json(package_json)
    name = data.get("name")
    if not isinstance(name, str) or not name:
        sys.exit("package.json name must be a non-empty string")

    original_name = name
    publish_name = original_name
    changed = False

    if not original_name.startswith("@"):
        normalized_scope = normalize_scope(scope)
        publish_name = f"@{normalized_scope}/{original_name}"
        data["name"] = publish_name
        changed = True
        write_package_json(package_json, data)

    state_payload = {
        "original_name": original_name,
        "publish_name": publish_name,
        "changed": changed,
    }
    state.write_text(
        json.dumps(state_payload, indent=2, ensure_ascii=True) + "\n",
        encoding="utf-8",
    )

    if output is not None:
        write_outputs(
            output,
            {
                "original_name": original_name,
                "publish_name": publish_name,
                "name_changed": str(changed).lower(),
            },
        )

    if changed:
        print(
            f"Updated package name for publish: {original_name} -> "
            f"{publish_name}"
        )
    else:
        print(f"Package name already scoped: {publish_name}")


def restore(package_dir: Path, state: Path) -> None:
    """Restore a previously modified package name using the state file."""
    if not state.exists():
        sys.exit(f"State file not found: {state}")

    payload = read_package_json(state)
    original_name = payload.get("original_name")
    publish_name = payload.get("publish_name")
    changed = payload.get("changed")

    if not isinstance(original_name, str) or not original_name:
        sys.exit("Invalid original_name in state file")

    if not isinstance(changed, bool):
        sys.exit("Invalid changed flag in state file")

    if not changed:
        print("No package name change to restore")
        return

    package_json = package_dir / "package.json"
    if not package_json.exists():
        sys.exit(f"Missing package.json at {package_json}")

    data = read_package_json(package_json)
    data["name"] = original_name
    write_package_json(package_json, data)
    print(f"Restored package name: {publish_name} -> {original_name}")


def main() -> None:
    """CLI entry point."""
    parser = argparse.ArgumentParser(
        description=(
            "Prepare or restore a Node package name for GitHub Packages "
            "publishing."
        )
    )
    parser.add_argument(
        "--package-dir",
        required=True,
        type=Path,
        help="Directory containing package.json",
    )
    parser.add_argument(
        "--scope",
        default="",
        help="Scope to apply when preparing (without @)",
    )
    parser.add_argument(
        "--output",
        type=Path,
        help="Optional output file for key=value pairs (GITHUB_OUTPUT)",
    )
    parser.add_argument(
        "--state-file",
        type=Path,
        required=True,
        help="Path to store or load state for restore",
    )
    parser.add_argument(
        "--restore",
        action="store_true",
        help="Restore the original package name from the state file",
    )

    args = parser.parse_args()

    package_dir: Path = args.package_dir
    state_file: Path = args.state_file

    if args.restore:
        restore(package_dir, state_file)
        return

    if not args.scope:
        sys.exit("--scope is required when preparing")

    prepare(package_dir, args.scope, args.output, state_file)


if __name__ == "__main__":
    main()
