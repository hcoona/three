"""Shared changed-path admission for Workflow Delivery v3 CI records."""

from __future__ import annotations

from pathlib import PurePosixPath

from three_workflow_delivery_v3.release.consumer_policy import (
    DEPENDENCY_SURFACE_CATALOG,
)

_REPOSITORY_ONLY_PREFIXES = (
    ".testagent/",
    "docs/",
    "eng/",
    "LICENSES/",
    "tests/",
)
_REPOSITORY_ONLY_PATHS = frozenset(
    {
        "AGENTS.md",
        "COPYING",
        "COPYING.LESSER",
        "Directory.Build.props",
        "Directory.Build.targets",
        "LICENSE",
        "README.md",
        "biome.jsonc",
        "dirs.proj",
        "global.json",
        "global.pkl",
        "hk.pkl",
        "nuget.config",
        "pyproject.toml",
        "stylecop.json",
        "uv.lock",
    }
)
CI_CONSUMER_POLICY_SURFACE_PATTERNS = tuple(
    pattern
    for rule in DEPENDENCY_SURFACE_CATALOG
    for pattern in rule.path_patterns
)


def _matches(path: str, pattern: str) -> bool:
    candidate = PurePosixPath(path)
    if pattern.startswith("**/"):
        return candidate.match(pattern) or candidate.match(pattern[3:])
    return len(candidate.parts) == len(
        PurePosixPath(pattern).parts
    ) and candidate.match(pattern)


def is_consumer_policy_surface_path(path: str) -> bool:
    """Return whether a path belongs to the consumer-policy catalog."""
    return any(
        _matches(path, pattern)
        for pattern in CI_CONSUMER_POLICY_SURFACE_PATTERNS
    )


def is_repository_only_path(path: str) -> bool:
    """Admit a classified path for root repository conformance only."""
    if path.startswith(".github/workflows/"):
        return path.endswith((".yml", ".yaml"))
    if path.startswith(".github/"):
        return True
    return (
        path in _REPOSITORY_ONLY_PATHS
        or path.startswith(_REPOSITORY_ONLY_PREFIXES)
        or is_consumer_policy_surface_path(path)
    )


__all__ = [
    "CI_CONSUMER_POLICY_SURFACE_PATTERNS",
    "is_consumer_policy_surface_path",
    "is_repository_only_path",
]
