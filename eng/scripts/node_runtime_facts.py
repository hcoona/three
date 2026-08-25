"""Keep Node workflow, Mise lock, and reviewed npm evidence consistent."""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import tomllib
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[2]
NODE_BACKEND = "core:node"
PNPM_BACKEND = "aqua:pnpm/pnpm"
RENOVATE_COMMAND = (
    "uv run --no-project --python 3.13 python "
    "eng/scripts/node_runtime_facts.py update-renovate-lock "
    "--expected-node {{newVersion}}"
)
SEMVER = re.compile(r"\A\d+\.\d+\.\d+\Z")
WORKFLOW_PIN = re.compile(
    r"^\s+node-version:\s*['\"](\d+\.\d+\.\d+)['\"]\s*$",
    re.MULTILINE,
)


class AuthorityError(ValueError):
    """Report a fail-closed toolchain authority error."""


def _locked_version(tool: str, backend: str) -> str:
    lock = ROOT / "mise.lock"
    entries = (
        tomllib.loads(lock.read_text(encoding="utf-8"))
        .get("tools", {})
        .get(tool)
    )
    if not isinstance(entries, list) or len(entries) != 1:
        message = f"{lock} must contain exactly one [[tools.{tool}]] entry"
        raise AuthorityError(message)
    entry = entries[0]
    if (
        not isinstance(entry, dict)
        or entry.get("backend") != backend
        or not isinstance(entry.get("version"), str)
        or not SEMVER.fullmatch(entry["version"])
    ):
        message = (
            f"{lock} [[tools.{tool}]] must use {backend!r} with exact semver"
        )
        raise AuthorityError(message)
    return entry["version"]


def _workflow_pins() -> tuple[str, ...]:
    workflow = ROOT / ".github/workflows/ci.yml"
    return tuple(WORKFLOW_PIN.findall(workflow.read_text(encoding="utf-8")))


def _validate_workflow(expected: str) -> None:
    pins = _workflow_pins()
    if pins != (expected, expected):
        message = (
            ".github/workflows/ci.yml must contain exactly two exact "
            f"setup-node pins matching mise.lock Node {expected}; "
            f"found {pins!r}"
        )
        raise AuthorityError(message)


def _version(*command: str) -> str:
    return subprocess.run(
        command,
        cwd=ROOT,
        check=True,
        capture_output=True,
        text=True,
    ).stdout.strip()


def _runtime() -> tuple[str, str, str]:
    node = _locked_version("node", NODE_BACKEND)
    pnpm = _locked_version("pnpm", PNPM_BACKEND)
    actual_node = _version("node", "--version").removeprefix("v")
    actual_pnpm = _version("pnpm", "--version")
    if (actual_node, actual_pnpm) != (node, pnpm):
        message = (
            "Installed Node/PNPM does not match mise.lock. "
            "Run `mise install --locked node pnpm` and retry."
        )
        raise AuthorityError(message)
    return node, pnpm, _version("npm", "--version")


def _capture_path(node: str, npm: str) -> Path:
    return (
        ROOT / "src/public/lib/three-workflow-delivery-v3/tests/fixtures/"
        "acceptance/npm-publish-request" / f"capture-node-{node}-npm-{npm}.json"
    )


def _validate_capture(node: str, npm: str) -> None:
    path = _capture_path(node, npm)
    command = "`mise run update-node-runtime-evidence`"
    if not path.is_file():
        message = (
            f"Active npm request evidence is missing: {path}. Run {command}, "
            "inspect request, integrity, lifecycle, and credential-free "
            "changes, then commit the new versioned fixture."
        )
        raise AuthorityError(message)
    metadata = json.loads(path.read_text(encoding="utf-8")).get("metadata")
    if not isinstance(metadata, dict) or (
        metadata.get("node-version"),
        metadata.get("npm-version"),
    ) != (f"v{node}", npm):
        message = (
            f"Active npm request evidence is stale: {path}. Run {command}, "
            "review the semantic diff, and retry."
        )
        raise AuthorityError(message)


def _validate_renovate() -> None:
    rules = json.loads(
        (ROOT / "renovate.json").read_text(encoding="utf-8")
    ).get("packageRules")
    if not isinstance(rules, list):
        message = "renovate.json packageRules must be a list"
        raise AuthorityError(message)
    node_rules: list[dict[str, Any]] = [
        rule
        for rule in rules
        if isinstance(rule, dict) and rule.get("groupName") == "Node Runtime"
    ]
    expected = {
        "matchManagers": ["github-actions", "mise"],
        "matchDatasources": ["github-releases", "node-version"],
        "matchDepNames": ["node"],
        "matchFileNames": [".github/workflows/ci.yml", "mise.toml"],
        "automerge": False,
        "postUpgradeTasks": {
            "commands": [RENOVATE_COMMAND],
            "fileFilters": ["mise.lock"],
            "executionMode": "branch",
        },
    }
    if len(node_rules) != 1 or any(
        node_rules[0].get(name) != value for name, value in expected.items()
    ):
        message = (
            "renovate.json must contain one reviewed Node Runtime group with "
            "bounded root lock regeneration and automerge disabled"
        )
        raise AuthorityError(message)
    allowlist = (ROOT / ".github/renovate/global.cjs").read_text(
        encoding="utf-8"
    )
    if (
        "node_runtime_facts\\.py update-renovate-lock "
        "--expected-node \\d+\\.\\d+\\.\\d+" not in allowlist
    ):
        message = "Renovate must allow only exact-semver Node lock regeneration"
        raise AuthorityError(message)


def _check() -> None:
    node, pnpm, npm = _runtime()
    _validate_workflow(node)
    _validate_renovate()
    _validate_capture(node, npm)
    print(
        f"Node {node}, PNPM {pnpm}, bundled npm {npm}: "
        "lock, workflow, Renovate, and capture agree."
    )


def _update_lock(expected: str) -> None:
    if not SEMVER.fullmatch(expected):
        message = f"expected Node must be exact semver, found {expected!r}"
        raise AuthorityError(message)
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
    locked = _locked_version("node", NODE_BACKEND)
    if locked != expected:
        message = (
            f"Renovate proposed Node {expected}, but Mise resolved {locked}. "
            "Refusing split toolchain authority."
        )
        raise AuthorityError(message)
    _validate_workflow(locked)
    _validate_renovate()
    print(
        f"Updated root mise.lock for Node {locked}. Semantic npm request "
        "evidence remains review-gated; run "
        "`mise run update-node-runtime-evidence` when CI requests it."
    )


def main() -> int:
    """Run the requested authority check or bounded lock update."""
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    commands.add_parser("check")
    update = commands.add_parser("update-renovate-lock")
    update.add_argument("--expected-node", required=True)
    arguments = parser.parse_args()
    try:
        if arguments.command == "check":
            _check()
        else:
            _update_lock(arguments.expected_node)
    except (AuthorityError, subprocess.CalledProcessError) as error:
        raise SystemExit(str(error)) from error
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
