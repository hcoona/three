"""Commit-9 contracts for CODEOWNERS final-match coverage."""

from __future__ import annotations

import re
import subprocess
from dataclasses import dataclass
from pathlib import Path

import pytest

REPO_ROOT = Path(__file__).resolve().parents[6]
CODEOWNERS_PATH = REPO_ROOT / ".github/CODEOWNERS"
REQUIRED_OWNER = "@hcoona"
GOVERNANCE_PATH = (
    ".github/workflow-delivery/governance/hcoona-release-smoke-npm.json"
)
ROOT_PYTHON_INPUTS = ("pyproject.toml", "uv.lock")
SYNTHETIC_FUTURE_SURFACES = (
    "eng/workflow-delivery/v3/future-policy.yml",
    "src/private/lib/hk/Commit9Future.pkl",
    "src/workflow-delivery.release-unit.yml",
    "src/workflow-delivery.quality.yml",
    "src/public/app/new-product/workflow-delivery.release-unit.yml",
    "src/private/app/new-product/config/workflow-delivery.quality.yml",
    ".github/workflows/workflow-delivery-v3-future.yml",
    ".github/actions/workflow-delivery-v3-future/action.yml",
    ".github/actions/workflow-delivery-v3/future/action.yml",
)


@dataclass(frozen=True, slots=True)
class CodeOwnersRule:
    """One ordered CODEOWNERS rule."""

    pattern: str
    owners: tuple[str, ...]


def _parse_rules(content: str) -> tuple[CodeOwnersRule, ...]:
    rules: list[CodeOwnersRule] = []
    for raw_line in content.splitlines():
        line = raw_line.split("#", 1)[0].strip()
        if not line:
            continue
        fields = line.split()
        rules.append(CodeOwnersRule(fields[0], tuple(fields[1:])))
    return tuple(rules)


def _pattern_expression(pattern: str) -> re.Pattern[str]:
    """Match the demonstrated rooted, subtree, and descriptor shapes."""
    rooted = pattern.startswith("/")
    normalized = pattern.removeprefix("/")
    if not rooted and "/" not in normalized:
        normalized = f"**/{normalized}"
    expression: list[str] = []
    index = 0
    while index < len(normalized):
        character = normalized[index]
        if normalized[index : index + 3] == "**/":
            expression.append("(?:.*/)?")
            index += 3
            continue
        if normalized[index : index + 2] == "**":
            expression.append(".*")
            index += 2
            continue
        if character == "*":
            expression.append("[^/]*")
        elif character == "?":
            expression.append("[^/]")
        else:
            expression.append(re.escape(character))
        index += 1
    return re.compile(rf"^{''.join(expression)}$")


def _final_owners(
    rules: tuple[CodeOwnersRule, ...],
    path: str,
) -> tuple[str, ...]:
    owners: tuple[str, ...] = ()
    for rule in rules:
        if _pattern_expression(rule.pattern).fullmatch(path) is not None:
            owners = rule.owners
    return owners


def _coverage_failures(
    rules: tuple[CodeOwnersRule, ...],
    paths: set[str],
) -> dict[str, tuple[str, ...]]:
    return {
        path: owners
        for path in sorted(paths)
        if (owners := _final_owners(rules, path)) != (REQUIRED_OWNER,)
    }


def _workspace_paths() -> set[str]:
    result = subprocess.run(
        (  # noqa: S607
            "git",
            "ls-files",
            "--cached",
            "--others",
            "--exclude-standard",
        ),
        cwd=REPO_ROOT,
        check=True,
        capture_output=True,
        text=True,
    )
    return {
        path
        for path in result.stdout.splitlines()
        if (REPO_ROOT / path).exists()
    }


def _descriptor_paths(paths: set[str]) -> set[str]:
    basenames = {
        "workflow-delivery.release-unit.yml",
        "workflow-delivery.quality.yml",
    }
    return {
        path
        for path in paths
        if path.startswith("src/") and Path(path).name in basenames
    }


def _governed_categories(paths: set[str]) -> dict[str, set[str]]:
    return {
        "v3-package": {
            path
            for path in paths
            if path.startswith("src/public/lib/three-workflow-delivery-v3/")
        },
        "v3-engineering": {
            path
            for path in paths
            if path.startswith("eng/workflow-delivery/v3/")
        },
        "descriptors": _descriptor_paths(paths),
        "hk-surfaces": {
            path
            for path in paths
            if path
            in {
                "hk.pkl",
                "eng/scripts/hk_exec.py",
                "eng/scripts/workflow_delivery_v3_hk.py",
            }
            or path.startswith("src/private/lib/hk/")
        },
        "root-python-inputs": set(ROOT_PYTHON_INPUTS),
        "v3-workflows": {
            path
            for path in paths
            if path.startswith(".github/workflows/workflow-delivery-v3")
        },
        "v3-actions": {
            path
            for path in paths
            if path.startswith(".github/actions/")
            and "workflow-delivery-v3" in path
        },
        "v3-direct-scripts": {
            path
            for path in paths
            if path.startswith("eng/scripts/workflow_delivery_v3")
        },
        "codeowners": {".github/CODEOWNERS"},
        "protected-governance": {GOVERNANCE_PATH},
    }


def _governed_surface_inventory() -> frozenset[str]:
    """Return the shared current, required-absent, and future v3 inventory."""
    paths = _workspace_paths() | set(SYNTHETIC_FUTURE_SURFACES)
    return frozenset(set().union(*_governed_categories(paths).values()))


ACTUAL_RULES = _parse_rules(CODEOWNERS_PATH.read_text(encoding="utf-8"))


def test_codeowners_parser_ignores_github_inline_comments() -> None:
    """Keep inline comments outside the exact final owner tuple."""
    rules = _parse_rules("/governed/** @hcoona # governance explanation\n")

    assert rules == (CodeOwnersRule("/governed/**", (REQUIRED_OWNER,)),)
    assert _final_owners(rules, "governed/file.txt") == (REQUIRED_OWNER,)


def test_actual_codeowners_final_owner_is_exact_for_every_current_and_future_v3_surface() -> (  # noqa: E501
    None
):
    """Require the real final rule to name exactly one owner for every path."""
    paths = _workspace_paths() | set(SYNTHETIC_FUTURE_SURFACES)
    categories = _governed_categories(paths)
    for category in categories.values():
        assert category

    governed_paths = set(_governed_surface_inventory())
    assert set(SYNTHETIC_FUTURE_SURFACES) <= governed_paths
    assert GOVERNANCE_PATH in governed_paths
    assert GOVERNANCE_PATH in _workspace_paths()
    ownership_only_paths = {
        ".github/workflows/buddy.yml",
        ".github/workflows/release-buddy.yml",
        ".github/workflows/legacy-buddy.yml",
        ".github/workflows/compatibility.yml",
        ".github/workflows/new-buddy-compatibility.yml",
    }
    ownership_paths = governed_paths | ownership_only_paths
    assert {
        path: _final_owners(ACTUAL_RULES, path) for path in ownership_paths
    } == dict.fromkeys(ownership_paths, (REQUIRED_OWNER,))


@pytest.mark.parametrize(
    ("pattern", "path", "matches"),
    [
        ("/uv.lock", "uv.lock", True),
        ("/uv.lock", "nested/uv.lock", False),
        ("/governed/exact.py", "governed/exact.py", True),
        ("/governed/exact.py", "nested/governed/exact.py", False),
        ("/governed/**", "governed/nested/file.py", True),
        ("/governed/**", "other/governed/file.py", False),
        (
            "/src/**/workflow-delivery.release-unit.yml",
            "src/workflow-delivery.release-unit.yml",
            True,
        ),
        (
            "/src/**/workflow-delivery.release-unit.yml",
            "src/nested/project/workflow-delivery.release-unit.yml",
            True,
        ),
        (
            "/src/**/workflow-delivery.release-unit.yml",
            "src/nested/not-workflow-delivery.release-unit.yml",
            False,
        ),
        (
            "/src/**/workflow-delivery.release-unit.yml",
            "src/nested/workflow-delivery.release-unit.yml.extra",
            False,
        ),
    ],
)
def test_codeowners_test_oracle_matches_supported_path_shapes(
    pattern: str, path: str, *, matches: bool
) -> None:
    """Distinguish matching and nonmatching paths in the local oracle."""
    rules = (CodeOwnersRule(pattern, (REQUIRED_OWNER,)),)

    assert _final_owners(rules, path) == ((REQUIRED_OWNER,) if matches else ())
    assert _coverage_failures(rules, {path}) == ({} if matches else {path: ()})


@pytest.mark.parametrize(
    "owners",
    [("@replacement-owner",), (REQUIRED_OWNER, "@co-owner")],
    ids=["replacement", "additional-coowner"],
)
def test_codeowners_test_oracle_rejects_later_nonsole_owner(
    owners: tuple[str, ...],
) -> None:
    """A later matching rule determines whether ownership is exactly sole."""
    path = "governed/file.py"
    rules = (
        CodeOwnersRule("/governed/**", (REQUIRED_OWNER,)),
        CodeOwnersRule(f"/{path}", owners),
    )

    assert _final_owners(rules, path) == owners
    assert _coverage_failures(rules, {path}) == {path: owners}
