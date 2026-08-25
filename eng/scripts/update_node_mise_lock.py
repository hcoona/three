"""Regenerate the root Node lock and reject workflow/lock drift."""

from __future__ import annotations

import argparse
import re
import subprocess
import tomllib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SEMVER = re.compile(r"\A\d+\.\d+\.\d+\Z")
WORKFLOW_PIN = re.compile(
    r"^\s+node-version:\s*['\"](\d+\.\d+\.\d+)['\"]\s*$",
    re.MULTILINE,
)


def _locked_node() -> str:
    entries = (
        tomllib.loads((ROOT / "mise.lock").read_text(encoding="utf-8"))
        .get("tools", {})
        .get("node")
    )
    if not isinstance(entries, list) or len(entries) != 1:
        message = "mise.lock must contain exactly one [[tools.node]] entry"
        raise ValueError(message)
    entry = entries[0]
    if (
        not isinstance(entry, dict)
        or entry.get("backend") != "core:node"
        or not isinstance(entry.get("version"), str)
        or not SEMVER.fullmatch(entry["version"])
    ):
        message = (
            "mise.lock [[tools.node]] must use core:node with exact semver"
        )
        raise ValueError(message)
    return entry["version"]


def _validate_workflow(node: str) -> None:
    pins = tuple(
        WORKFLOW_PIN.findall(
            (ROOT / ".github/workflows/ci.yml").read_text(encoding="utf-8")
        )
    )
    if pins != (node, node):
        message = (
            ".github/workflows/ci.yml must contain exactly two exact "
            f"setup-node pins matching mise.lock Node {node}; found {pins!r}"
        )
        raise ValueError(message)


def _run(expected: str | None) -> str:
    if expected:
        if not SEMVER.fullmatch(expected):
            message = f"expected Node must be exact semver, found {expected!r}"
            raise ValueError(message)
        subprocess.run(
            (
                "mise",
                "lock",
                "--bump",
                "--minimum-release-age",
                "3d",
                "node",
            ),
            cwd=ROOT,
            check=True,
        )

    locked = _locked_node()
    if expected and locked != expected:
        message = (
            f"Renovate proposed Node {expected}, but Mise resolved {locked}. "
            "Refusing split toolchain authority."
        )
        raise ValueError(message)
    _validate_workflow(locked)
    return locked


def main() -> int:
    """Update the lock when requested, then validate observable drift."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--expected-node")
    arguments = parser.parse_args()
    try:
        locked = _run(arguments.expected_node)
    except (ValueError, subprocess.CalledProcessError) as error:
        raise SystemExit(str(error)) from error
    print(f"mise.lock and production workflow pins agree on Node {locked}.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
