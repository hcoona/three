"""Shared changed-path admission for Workflow Delivery v3 CI records."""

from __future__ import annotations

from pathlib import PurePosixPath

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
_SCRIPT_SUFFIXES = (
    "bat",
    "bash",
    "cjs",
    "cmd",
    "js",
    "mjs",
    "ps1",
    "py",
    "sh",
    "ts",
    "zsh",
)


def _root_and_recursive(*patterns: str) -> tuple[str, ...]:
    return (*patterns, *(f"**/{pattern}" for pattern in patterns))


def _script_patterns() -> tuple[str, ...]:
    names = tuple(
        f"{prefix}*.{suffix}"
        for prefix in ("bootstrap", "install", "setup")
        for suffix in _SCRIPT_SUFFIXES
    )
    postinstall = tuple(
        f"postinstall*.{suffix}" for suffix in ("cjs", "js", "mjs", "ts")
    )
    return _root_and_recursive(*names, *postinstall)


CI_CONSUMER_POLICY_SURFACE_PATTERNS = (
    *_root_and_recursive(
        "Directory.Packages.props",
        "package.json",
        "packages.config",
        "pyproject.toml",
        "requirements*.txt",
        "setup.py",
        "*.csproj",
        "*.fsproj",
        "*.vbproj",
    ),
    (
        "src/public/lib/three-workflow-delivery-v3/tests/fixtures/release/"
        "consumer-policy-acceptance.json"
    ),
    *_root_and_recursive(
        "bun.lock",
        "npm-shrinkwrap.json",
        "package-lock.json",
        "packages.lock.json",
        "pnpm-lock.yaml",
        "poetry.lock",
        "uv.lock",
        "yarn.lock",
    ),
    *_root_and_recursive(
        ".github/workflows/*.yaml",
        ".github/workflows/*.yml",
    ),
    ".github/actions/**/action.yaml",
    ".github/actions/**/action.yml",
    *_script_patterns(),
    "eng/scripts/workflow_delivery_v3_consumer_policy.py",
    *_root_and_recursive(
        ".github/dependabot.yaml",
        ".github/dependabot.yml",
        ".npmrc",
        ".pnpmfile.cjs",
        ".yarnrc",
        ".yarnrc.yml",
        "NuGet.config",
        "bunfig.toml",
        "nuget.config",
        "pnpm-workspace.yaml",
        "renovate.json",
    ),
    ".gitattributes",
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
