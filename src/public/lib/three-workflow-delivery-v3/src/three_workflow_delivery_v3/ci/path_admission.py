"""Shared changed-path admission for Workflow Delivery v3 CI records."""

from __future__ import annotations

from pathlib import PurePosixPath

from three_workflow_delivery_v3.release.consumer_policy import (
    DEPENDENCY_SURFACE_CATALOG,
)

_REPOSITORY_ONLY_PREFIXES = (
    ".agents/skills/scholarly-pdf-reconstruction/",
    ".agents/skills/scholarly-print-assembly/",
    ".agents/skills/scholarly-render-qa/",
    ".testagent/",
    "docs/",
    "eng/",
    "LICENSES/",
    "src/private/lib/scholarly-publication/",
    "tests/",
)
_REPOSITORY_ONLY_PATHS = frozenset(
    {
        ".typos.toml",
        "AGENTS.md",
        "COPYING",
        "COPYING.LESSER",
        "Directory.Build.props",
        "Directory.Build.targets",
        "LICENSE",
        "README.md",
        "apm.lock.yaml",
        "apm.yml",
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
    return PurePosixPath(path).full_match(pattern)


def is_consumer_policy_surface_path(path: str) -> bool:
    """Return whether a path belongs to the consumer-policy catalog."""
    return any(
        _matches(path, pattern)
        for pattern in CI_CONSUMER_POLICY_SURFACE_PATTERNS
    )


def is_repository_only_path(path: str) -> bool:
    """Admit a classified path for root repository conformance only."""
    if path.startswith(".github/workflows/"):
        return path.endswith((".md", ".yml", ".yaml"))
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
