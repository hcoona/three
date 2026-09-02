"""Shared changed-path admission for Workflow Delivery v3 CI records."""

from __future__ import annotations

from pathlib import PurePosixPath

from three_workflow_delivery_v3.release.static_reference_source import (
    STATIC_REFERENCE_BASENAMES,
    select_static_reference_path,
)


def _root_and_recursive(*patterns: str) -> tuple[str, ...]:
    return (*patterns, *(f"**/{pattern}" for pattern in patterns))


def _install_bootstrap_patterns() -> tuple[str, ...]:
    suffixes = (
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
    names = [
        f"{prefix}*.{suffix}"
        for prefix in ("bootstrap", "install", "setup")
        for suffix in suffixes
    ]
    names.extend(
        f"postinstall*.{suffix}" for suffix in ("cjs", "js", "mjs", "ts")
    )
    return _root_and_recursive(*names)


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
        ".gitattributes",
        ".agents/skills/scholarly-pdf-reconstruction",
        ".agents/skills/scholarly-print-assembly",
        ".agents/skills/scholarly-render-qa",
        "apm.lock.yaml",
        "apm.yml",
        "biome.jsonc",
        "dirs.proj",
        "global.json",
        "global.pkl",
        "hk.pkl",
        "nuget.config",
        "pyproject.toml",
        "src/public/lib/hexo-renderer-asciidoc/README.md",
        "src/public/lib/hexo-renderer-asciidoc/README.npm.md",
        ("src/public/lib/hexo-renderer-asciidoc/examples/hexo-site/README.md"),
        (
            "src/public/lib/hexo-renderer-asciidoc/examples/hexo-site/"
            "source/_posts/hello-from-asciidoc.adoc"
        ),
        (
            "src/public/lib/hexo-renderer-asciidoc/examples/hexo-site/"
            "source/_posts/renderer-tour.adoc"
        ),
        (
            "src/public/lib/hexo-renderer-asciidoc/examples/hexo-site/"
            "source/about/index.adoc"
        ),
        "stylecop.json",
        "uv.lock",
    }
)
_REPOSITORY_CONFORMANCE_PATTERNS = (
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
        "bun.lock",
        "npm-shrinkwrap.json",
        "package-lock.json",
        "packages.lock.json",
        "pnpm-lock.yaml",
        "poetry.lock",
        "uv.lock",
        "yarn.lock",
        ".github/workflows/*.yaml",
        ".github/workflows/*.yml",
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
        ".gitattributes",
    ),
    ".github/actions/**/action.yaml",
    ".github/actions/**/action.yml",
    *_install_bootstrap_patterns(),
)
_STATIC_REFERENCE_CONTROL_PATHS = frozenset({"Directory.Packages.props"})
_STATIC_REFERENCE_CONTROL_PREFIXES = (
    "src/private/app/workflow-delivery-v3-nuget-authority/",
)
CI_STATIC_REFERENCE_BASENAMES = STATIC_REFERENCE_BASENAMES


def is_static_reference_surface_path(path: str) -> bool:
    """Return whether a path has a retained static-reference basename."""
    return select_static_reference_path(path) is not None


def is_static_reference_control_path(path: str) -> bool:
    """Return whether a path defines the retained authority implementation."""
    return path in _STATIC_REFERENCE_CONTROL_PATHS or path.startswith(
        _STATIC_REFERENCE_CONTROL_PREFIXES
    )


def is_repository_only_path(path: str) -> bool:
    """Admit a classified path for root repository conformance only."""
    if is_static_reference_surface_path(path):
        return True
    matches_conformance_pattern = any(
        PurePosixPath(path).full_match(pattern)
        for pattern in _REPOSITORY_CONFORMANCE_PATTERNS
    )
    if path.startswith(".github/workflows/"):
        return matches_conformance_pattern or path.endswith(
            (".md", ".yml", ".yaml")
        )
    if path.startswith(".github/"):
        return True
    return (
        path in _REPOSITORY_ONLY_PATHS
        or path.startswith(_REPOSITORY_ONLY_PREFIXES)
        or matches_conformance_pattern
    )


__all__ = [
    "CI_STATIC_REFERENCE_BASENAMES",
    "is_repository_only_path",
    "is_static_reference_control_path",
    "is_static_reference_surface_path",
]
