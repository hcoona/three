# /// script
# requires-python = ">=3.14"
# dependencies = []
# ///

"""Prepare or restore a Node package name for publishing.

This script can stamp the exact planner-authorized package name or optionally
add a scope for GitHub Packages publishing, then restore the original name
after packing.
"""

from __future__ import annotations

import argparse
import base64
import binascii
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
    package_dir: Path,
    scope: str,
    package_name: str,
    output: Path | None,
    state: Path,
) -> None:
    """Prepare a package by stamping the publish name when needed."""
    package_json = package_dir / "package.json"
    if not package_json.exists():
        sys.exit(f"Missing package.json at {package_json}")

    try:
        original_package_json = package_json.read_bytes()
    except OSError as exc:
        sys.exit(f"Failed to read {package_json}: {exc}")

    data = read_package_json(package_json)
    name = data.get("name")
    if not isinstance(name, str) or not name:
        sys.exit("package.json name must be a non-empty string")

    original_name = name
    publish_name = package_name.strip() if package_name else original_name
    changed = False

    if package_name and not publish_name:
        sys.exit("Package name is empty after trimming")

    if not package_name and not original_name.startswith("@"):
        normalized_scope = normalize_scope(scope)
        publish_name = f"@{normalized_scope}/{original_name}"

    if original_name != publish_name:
        data["name"] = publish_name
        changed = True
        write_package_json(package_json, data)

    state_payload = {
        "original_name": original_name,
        "original_package_json_b64": base64.b64encode(
            original_package_json
        ).decode("ascii"),
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
        print(f"Package name already matches publish name: {publish_name}")


def restore(package_dir: Path, state: Path) -> None:
    """Restore a previously modified package name using the state file."""
    if not state.exists():
        sys.exit(f"State file not found: {state}")

    payload = read_package_json(state)
    original_name = payload.get("original_name")
    original_package_json_b64 = payload.get("original_package_json_b64")
    publish_name = payload.get("publish_name")
    changed = payload.get("changed")

    if not isinstance(original_name, str) or not original_name:
        sys.exit("Invalid original_name in state file")

    if not isinstance(changed, bool):
        sys.exit("Invalid changed flag in state file")

    if not changed:
        print("No package name change to restore")
        return

    if not isinstance(original_package_json_b64, str):
        sys.exit("Invalid original_package_json_b64 in state file")

    try:
        original_package_json = base64.b64decode(
            original_package_json_b64, validate=True
        )
    except (ValueError, binascii.Error) as exc:
        sys.exit(f"Invalid original_package_json_b64 in state file: {exc}")

    package_json = package_dir / "package.json"
    if not package_json.exists():
        sys.exit(f"Missing package.json at {package_json}")

    package_json.write_bytes(original_package_json)
    print(f"Restored package name: {publish_name} -> {original_name}")


def main() -> None:
    """CLI entry point."""
    parser = argparse.ArgumentParser(
        description=(
            "Prepare or restore a Node package name for release packing and "
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
        help="Scope to apply when preparing for GitHub Packages (without @)",
    )
    parser.add_argument(
        "--package-name",
        default="",
        help="Exact planner-authorized package name to stamp before packing",
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

    if args.package_name and args.scope:
        sys.exit("--package-name and --scope are mutually exclusive")

    if not args.package_name and not args.scope:
        sys.exit("--package-name or --scope is required when preparing")

    prepare(package_dir, args.scope, args.package_name, args.output, state_file)


if __name__ == "__main__":
    main()
