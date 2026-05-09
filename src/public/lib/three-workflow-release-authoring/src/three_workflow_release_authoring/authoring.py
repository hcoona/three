"""Workflow release descriptor and target catalog authoring validation."""

from __future__ import annotations

import json
import re
import shutil
import subprocess
import tomllib
from collections.abc import Callable, Iterable, Mapping, Sequence
from dataclasses import dataclass
from pathlib import Path, PurePosixPath
from typing import Any

import yaml
from three_workflow_release_contracts import validate_contract

API_VERSION = "three.release/v1alpha1"
CATALOG_PATH = "eng/release/target-instances.yml"
DESCRIPTOR_NAME = "three.release.yml"
DOTNET_METADATA_INPUT_API_VERSION = (
    "three.release.dotnet-planner-metadata-input/v1alpha1"
)
_CANONICAL_ID_RE = re.compile(r"^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$")
_NPM_NAME_RE = re.compile(r"^(?:@[a-z0-9][a-z0-9._-]*/)?[a-z0-9][a-z0-9._-]*$")
_PYPI_NAME_RE = re.compile(r"^[A-Za-z0-9](?:[A-Za-z0-9._-]*[A-Za-z0-9])?$")
_GITHUB_SLUG_RE = re.compile(r"^[A-Za-z0-9_.-]+$")
_GEM_NAME_RE = re.compile(r"^[a-z0-9._-]+$")
_WINDOWS_DRIVE_PATH_RE = re.compile(r"^[A-Za-z]:")
type _ArtifactTuple = tuple[str, str, str]
type _VariantDimensions = tuple[tuple[str, str], ...]
type _SemanticArtifact = tuple[_VariantDimensions, _ArtifactTuple]
_ALLOWED_PROJECT_SCOPES = (
    "src/public/",
    "src/private/app/qidian-novel-downloader/",
    "src/private/app/vscode-copilot-telegram-hook/",
)
_FROZEN_DESCRIPTOR_IDENTITY_BY_ROOT = {
    "src/public/app/ImageOcclusionEditor": (
        "image-occlusion-editor",
        "ImageOcclusionEditorWinUI3/ImageOcclusionEditorWinUI3.csproj",
    ),
    "src/public/app/PhiFailureDetector.Console": (
        "phi-failure-detector-console",
        "PhiFailureDetector.ConsoleApp.csproj",
    ),
    "src/public/app/markdown-hybrid-search-mcp": (
        "markdown-hybrid-search-mcp",
        "pyproject.toml",
    ),
    "src/public/lib/CircularList": ("circular-list", "CircularList.csproj"),
    "src/public/lib/Hjg.Pngcs": ("hjg-pngcs", "Hjg.Pngcs.csproj"),
    "src/public/lib/Memoization": ("memoization", "Memoization.csproj"),
    "src/public/lib/Memoization.Generators": (
        "memoization-generators",
        "Memoization.Generators.csproj",
    ),
    "src/public/lib/MicrosoftExtensions.Logging.MSTest": (
        "microsoft-extensions-logging-mstest",
        "MicrosoftExtensions.Logging.MSTest.csproj",
    ),
    "src/public/lib/MicrosoftExtensions.Logging.Xunit": (
        "microsoft-extensions-logging-xunit",
        "MicrosoftExtensions.Logging.Xunit.csproj",
    ),
    "src/public/lib/MicrosoftExtensions.Options.DedupChangeExtensions": (
        "microsoft-extensions-options-dedup-change-extensions",
        "MicrosoftExtensions.Options.DedupChangeExtensions.csproj",
    ),
    "src/public/lib/PhiFailureDetector": (
        "phi-failure-detector",
        "PhiFailureDetector.csproj",
    ),
    "src/public/lib/WebHdfs.Extensions.FileProviders": (
        "webhdfs-extensions-file-providers",
        "WebHdfs.Extensions.FileProviders.csproj",
    ),
    "src/public/lib/asciidoctor-latexmath": (
        "asciidoctor-latexmath",
        "asciidoctor-latexmath.gemspec",
    ),
    "src/public/lib/hcoona-release-smoke": (
        "hcoona-release-smoke",
        "pyproject.toml",
    ),
    "src/public/lib/hcoona-release-smoke-github-packages": (
        "hcoona-release-smoke-github-packages",
        "hcoona-release-smoke-github-packages.csproj",
    ),
    "src/public/lib/hcoona-release-smoke-github-release": (
        "hcoona-release-smoke-github-release",
        "hcoona-release-smoke-github-release.csproj",
    ),
    "src/public/lib/hcoona-release-smoke-npm": (
        "hcoona-release-smoke-npm",
        "package.json",
    ),
    "src/public/lib/hcoona-release-smoke-nuget": (
        "hcoona-release-smoke-nuget",
        "hcoona-release-smoke-nuget.csproj",
    ),
    "src/public/lib/hcoona-release-smoke-pypi": (
        "hcoona-release-smoke-pypi",
        "pyproject.toml",
    ),
    "src/public/lib/hcoona-release-smoke-rubygems": (
        "hcoona-release-smoke-rubygems",
        "hcoona-release-smoke-rubygems.gemspec",
    ),
    "src/public/lib/hexo-renderer-asciidoc": (
        "hexo-renderer-asciidoc",
        "package.json",
    ),
    "src/public/lib/nbgv-python": ("nbgv-python", "pyproject.toml"),
    "src/public/lib/steam-account-history-to-csv": (
        "steam-account-history-to-csv",
        "package.json",
    ),
    "src/private/app/qidian-novel-downloader": (
        "qidian-novel-downloader",
        "QidianNovelDownloader.csproj",
    ),
    "src/private/app/vscode-copilot-telegram-hook": (
        "vscode-copilot-telegram-hook",
        "VSCodeCopilotTelegramHook.csproj",
    ),
}
_REQUIRED_DESCRIPTOR_ROOTS = frozenset(_FROZEN_DESCRIPTOR_IDENTITY_BY_ROOT)
REQUIRED_DESCRIPTOR_ROOTS = _REQUIRED_DESCRIPTOR_ROOTS
"""Frozen first-delivery descriptor roots required by author-time validation."""
_GITHUB_RELEASE_ONLY_TARGETS = {
    "buddy": frozenset({"github-release/public"}),
    "official": frozenset({"github-release/public"}),
}
_GITHUB_RELEASE_AND_PYPI_TARGETS = {
    "buddy": frozenset({"github-release/public"}),
    "official": frozenset({"github-release/public", "pypi/pypi"}),
}
_ZERO_TARGETS = {
    "buddy": frozenset(),
    "official": frozenset(),
}
_NUGET_PACKAGE_TUPLES = (
    ("primary-package", "package", "nuget"),
    ("symbols", "package", "snupkg"),
)
_PYTHON_PACKAGE_TUPLES = (
    ("primary-package", "package", "wheel"),
    ("primary-package", "package", "sdist"),
)
_NPM_PACKAGE_TUPLES = (("primary-package", "package", "npm-package"),)
_RUBYGEMS_PACKAGE_TUPLES = (("primary-package", "package", "rubygem"),)
_INSTALLER_TUPLES = (("installer", "installer", "inno-setup"),)
_ONE_EXECUTABLE_TUPLE = (("primary-binary", "binary", "executable"),)
_D_DEFAULT: tuple[tuple[str, str], ...] = ()
_D_WIN_X64 = (("os", "windows"), ("rid", "win-x64"))
_D_LINUX_X64 = (("os", "linux"), ("rid", "linux-x64"))
_D_MACOS_X64 = (("os", "macos"), ("rid", "osx-x64"))
_DEFAULT_NUGET_PACKAGE = tuple(
    (_D_DEFAULT, tuple_key) for tuple_key in _NUGET_PACKAGE_TUPLES
)
_DEFAULT_PYTHON_PACKAGE = tuple(
    (_D_DEFAULT, tuple_key) for tuple_key in _PYTHON_PACKAGE_TUPLES
)
_DEFAULT_NPM_PACKAGE = tuple(
    (_D_DEFAULT, tuple_key) for tuple_key in _NPM_PACKAGE_TUPLES
)
_DEFAULT_RUBYGEMS_PACKAGE = tuple(
    (_D_DEFAULT, tuple_key) for tuple_key in _RUBYGEMS_PACKAGE_TUPLES
)
_WIN_X64_INSTALLER = ((_D_WIN_X64, _INSTALLER_TUPLES[0]),)
_WIN_X64_EXECUTABLE = ((_D_WIN_X64, _ONE_EXECUTABLE_TUPLE[0]),)
_LINUX_X64_EXECUTABLE = ((_D_LINUX_X64, _ONE_EXECUTABLE_TUPLE[0]),)
_MACOS_X64_EXECUTABLE = ((_D_MACOS_X64, _ONE_EXECUTABLE_TUPLE[0]),)
_TWO_EXECUTABLES = _WIN_X64_EXECUTABLE + _LINUX_X64_EXECUTABLE
_THREE_EXECUTABLES = _TWO_EXECUTABLES + _MACOS_X64_EXECUTABLE
_GITHUB_RELEASE_ARTIFACTS = {
    "buddy": {"github-release/public": _DEFAULT_NUGET_PACKAGE},
    "official": {"github-release/public": _DEFAULT_NUGET_PACKAGE},
}
_PYTHON_ARTIFACTS = {
    "buddy": {"github-release/public": _DEFAULT_PYTHON_PACKAGE},
    "official": {
        "github-release/public": _DEFAULT_PYTHON_PACKAGE,
        "pypi/pypi": _DEFAULT_PYTHON_PACKAGE,
    },
}
_PYTHON_GITHUB_RELEASE_ARTIFACTS = {
    "buddy": {"github-release/public": _DEFAULT_PYTHON_PACKAGE},
    "official": {"github-release/public": _DEFAULT_PYTHON_PACKAGE},
}
_NPM_GITHUB_RELEASE_ARTIFACTS = {
    "buddy": {"github-release/public": _DEFAULT_NPM_PACKAGE},
    "official": {"github-release/public": _DEFAULT_NPM_PACKAGE},
}
_RUBYGEMS_GITHUB_RELEASE_ARTIFACTS = {
    "buddy": {"github-release/public": _DEFAULT_RUBYGEMS_PACKAGE},
    "official": {"github-release/public": _DEFAULT_RUBYGEMS_PACKAGE},
}
_NUGET_ORG_ARTIFACTS = {
    "buddy": {
        "github-release/public": _DEFAULT_NUGET_PACKAGE,
        "nuget/github-packages": (
            (_D_DEFAULT, ("primary-package", "package", "nuget")),
        ),
    },
    "official": {
        "github-release/public": _DEFAULT_NUGET_PACKAGE,
        "nuget/nuget-org": (
            (_D_DEFAULT, ("primary-package", "package", "nuget")),
        ),
    },
}
_NPMJS_ARTIFACTS = {
    "buddy": {
        "github-release/public": _DEFAULT_NPM_PACKAGE,
        "npm/github-packages": _DEFAULT_NPM_PACKAGE,
    },
    "official": {
        "github-release/public": _DEFAULT_NPM_PACKAGE,
        "npm/npmjs": _DEFAULT_NPM_PACKAGE,
    },
}
_RUBYGEMS_ORG_ARTIFACTS = {
    "buddy": {
        "github-release/public": _DEFAULT_RUBYGEMS_PACKAGE,
        "rubygems/github-packages": _DEFAULT_RUBYGEMS_PACKAGE,
    },
    "official": {
        "github-release/public": _DEFAULT_RUBYGEMS_PACKAGE,
        "rubygems/rubygems-org": _DEFAULT_RUBYGEMS_PACKAGE,
    },
}
_GITHUB_PACKAGES_NUGET_ARTIFACTS = {
    "buddy": {
        "github-release/public": _DEFAULT_NUGET_PACKAGE,
        "nuget/github-packages": (
            (_D_DEFAULT, ("primary-package", "package", "nuget")),
        ),
    },
    "official": {
        "github-release/public": _DEFAULT_NUGET_PACKAGE,
        "nuget/github-packages": (
            (_D_DEFAULT, ("primary-package", "package", "nuget")),
        ),
    },
}
_INSTALLER_ARTIFACTS = {
    "buddy": {"github-release/public": _WIN_X64_INSTALLER},
    "official": {"github-release/public": _WIN_X64_INSTALLER},
}
_NO_ARTIFACTS = {"buddy": {}, "official": {}}
_THREE_EXECUTABLE_ARTIFACTS = {
    "buddy": {"github-release/public": _THREE_EXECUTABLES},
    "official": {"github-release/public": _THREE_EXECUTABLES},
}
_TWO_EXECUTABLE_ARTIFACTS = {
    "buddy": {"github-release/public": _TWO_EXECUTABLES},
    "official": {"github-release/public": _TWO_EXECUTABLES},
}
_FROZEN_PROFILE_TARGETS_BY_PROJECT_ID = {
    "asciidoctor-latexmath": _GITHUB_RELEASE_ONLY_TARGETS,
    "circular-list": _GITHUB_RELEASE_ONLY_TARGETS,
    "hcoona-release-smoke": _ZERO_TARGETS,
    "hcoona-release-smoke-github-packages": {
        "buddy": frozenset({"github-release/public", "nuget/github-packages"}),
        "official": frozenset(
            {"github-release/public", "nuget/github-packages"}
        ),
    },
    "hcoona-release-smoke-github-release": _GITHUB_RELEASE_ONLY_TARGETS,
    "hcoona-release-smoke-npm": {
        "buddy": frozenset({"github-release/public", "npm/github-packages"}),
        "official": frozenset({"github-release/public", "npm/npmjs"}),
    },
    "hcoona-release-smoke-nuget": {
        "buddy": frozenset({"github-release/public", "nuget/github-packages"}),
        "official": frozenset({"github-release/public", "nuget/nuget-org"}),
    },
    "hcoona-release-smoke-pypi": _GITHUB_RELEASE_AND_PYPI_TARGETS,
    "hcoona-release-smoke-rubygems": {
        "buddy": frozenset(
            {"github-release/public", "rubygems/github-packages"}
        ),
        "official": frozenset(
            {"github-release/public", "rubygems/rubygems-org"}
        ),
    },
    "hexo-renderer-asciidoc": _GITHUB_RELEASE_ONLY_TARGETS,
    "hjg-pngcs": _GITHUB_RELEASE_ONLY_TARGETS,
    "image-occlusion-editor": _GITHUB_RELEASE_ONLY_TARGETS,
    "markdown-hybrid-search-mcp": _ZERO_TARGETS,
    "memoization": _GITHUB_RELEASE_ONLY_TARGETS,
    "memoization-generators": _ZERO_TARGETS,
    "microsoft-extensions-logging-mstest": _GITHUB_RELEASE_ONLY_TARGETS,
    "microsoft-extensions-logging-xunit": _GITHUB_RELEASE_ONLY_TARGETS,
    "microsoft-extensions-options-dedup-change-extensions": (
        _GITHUB_RELEASE_ONLY_TARGETS
    ),
    "nbgv-python": _GITHUB_RELEASE_ONLY_TARGETS,
    "phi-failure-detector": _GITHUB_RELEASE_ONLY_TARGETS,
    "phi-failure-detector-console": _ZERO_TARGETS,
    "qidian-novel-downloader": _GITHUB_RELEASE_ONLY_TARGETS,
    "steam-account-history-to-csv": _ZERO_TARGETS,
    "vscode-copilot-telegram-hook": _GITHUB_RELEASE_ONLY_TARGETS,
    "webhdfs-extensions-file-providers": _GITHUB_RELEASE_ONLY_TARGETS,
}
_FROZEN_TARGET_ARTIFACT_SEMANTICS_BY_PROJECT_ID = {
    "asciidoctor-latexmath": _RUBYGEMS_GITHUB_RELEASE_ARTIFACTS,
    "circular-list": _GITHUB_RELEASE_ARTIFACTS,
    "hcoona-release-smoke": _NO_ARTIFACTS,
    "hcoona-release-smoke-github-packages": _GITHUB_PACKAGES_NUGET_ARTIFACTS,
    "hcoona-release-smoke-github-release": _GITHUB_RELEASE_ARTIFACTS,
    "hcoona-release-smoke-npm": _NPMJS_ARTIFACTS,
    "hcoona-release-smoke-nuget": _NUGET_ORG_ARTIFACTS,
    "hcoona-release-smoke-pypi": _PYTHON_ARTIFACTS,
    "hcoona-release-smoke-rubygems": _RUBYGEMS_ORG_ARTIFACTS,
    "hexo-renderer-asciidoc": _NPM_GITHUB_RELEASE_ARTIFACTS,
    "hjg-pngcs": _GITHUB_RELEASE_ARTIFACTS,
    "image-occlusion-editor": _INSTALLER_ARTIFACTS,
    "markdown-hybrid-search-mcp": _NO_ARTIFACTS,
    "memoization": _GITHUB_RELEASE_ARTIFACTS,
    "memoization-generators": _NO_ARTIFACTS,
    "microsoft-extensions-logging-mstest": _GITHUB_RELEASE_ARTIFACTS,
    "microsoft-extensions-logging-xunit": _GITHUB_RELEASE_ARTIFACTS,
    "microsoft-extensions-options-dedup-change-extensions": (
        _GITHUB_RELEASE_ARTIFACTS
    ),
    "nbgv-python": _PYTHON_GITHUB_RELEASE_ARTIFACTS,
    "phi-failure-detector": _GITHUB_RELEASE_ARTIFACTS,
    "phi-failure-detector-console": _NO_ARTIFACTS,
    "qidian-novel-downloader": _THREE_EXECUTABLE_ARTIFACTS,
    "steam-account-history-to-csv": _NO_ARTIFACTS,
    "vscode-copilot-telegram-hook": _TWO_EXECUTABLE_ARTIFACTS,
    "webhdfs-extensions-file-providers": _GITHUB_RELEASE_ARTIFACTS,
}
_PROJECT_ECOSYSTEMS = {"dotnet", "python", "node", "ruby"}
_RELEASE_KINDS = {"lib", "app", "tool", "extension", "generator"}
_VERSION_AUTHORITIES = {"build-system-nbgv", "nbgv-python-pyproject-version"}
_PROFILES = ("buddy", "official")
_TARGET_FAMILIES = {"github-release", "nuget", "pypi", "npm", "rubygems"}
_CONTRACT_BY_FAMILY = {
    "github-release": "github-release-assets",
    "nuget": "nuget-publish",
    "pypi": "pypi-publish",
    "npm": "npm-publish",
    "rubygems": "rubygems-publish",
}
_ALLOWED_TUPLES_BY_CONTRACT = {
    "github-release-assets": {
        ("primary-package", "package", "nuget"),
        ("symbols", "package", "snupkg"),
        ("primary-package", "package", "wheel"),
        ("primary-package", "package", "sdist"),
        ("primary-package", "package", "npm-package"),
        ("primary-package", "package", "rubygem"),
        ("primary-binary", "binary", "executable"),
        ("installer", "installer", "inno-setup"),
    },
    "nuget-publish": {
        ("primary-package", "package", "nuget"),
        ("symbols", "package", "snupkg"),
    },
    "pypi-publish": {
        ("primary-package", "package", "wheel"),
        ("primary-package", "package", "sdist"),
    },
    "npm-publish": {("primary-package", "package", "npm-package")},
    "rubygems-publish": {("primary-package", "package", "rubygem")},
}
_CAPABILITY_ASSIGNMENTS = {
    ("github-release", "github"): {
        "mutability": "mutable-prerelease",
        "name-uniqueness-scope": "release-tag",
        "version-uniqueness-rule": "tag",
        "profile-coexistence-rule": "not-applicable",
        "credential-posture": "github-token",
        "publish-topology": "github-token",
    },
    ("nuget", "nuget.org"): {
        "mutability": "immutable",
        "name-uniqueness-scope": "package-name",
        "version-uniqueness-rule": "package-name-plus-version",
        "profile-coexistence-rule": "requires-distinct-name",
        "credential-posture": "oidc",
        "publish-topology": "external-oidc-entry-workflow",
    },
    ("nuget", "nuget.pkg.github.com"): {
        "mutability": "immutable",
        "name-uniqueness-scope": "package-name-with-owner",
        "version-uniqueness-rule": "package-name-plus-version",
        "profile-coexistence-rule": "same-name-allowed",
        "credential-posture": "github-token",
        "publish-topology": "github-token",
    },
    ("pypi", "pypi.org"): {
        "mutability": "immutable",
        "name-uniqueness-scope": "package-name",
        "version-uniqueness-rule": "package-name-plus-version",
        "profile-coexistence-rule": "requires-distinct-name",
        "credential-posture": "oidc",
        "publish-topology": "external-oidc-entry-workflow",
    },
    ("npm", "registry.npmjs.org"): {
        "mutability": "immutable",
        "name-uniqueness-scope": "package-name",
        "version-uniqueness-rule": "package-name-plus-version",
        "profile-coexistence-rule": "requires-distinct-name",
        "credential-posture": "oidc",
        "publish-topology": "external-oidc-entry-workflow",
    },
    ("npm", "npm.pkg.github.com"): {
        "mutability": "immutable",
        "name-uniqueness-scope": "package-name-with-owner",
        "version-uniqueness-rule": "package-name-plus-version",
        "profile-coexistence-rule": "same-name-allowed",
        "credential-posture": "github-token",
        "publish-topology": "github-token",
    },
    ("rubygems", "rubygems.org"): {
        "mutability": "immutable",
        "name-uniqueness-scope": "package-name",
        "version-uniqueness-rule": "package-name-plus-version",
        "profile-coexistence-rule": "requires-distinct-name",
        "credential-posture": "oidc",
        "publish-topology": "external-oidc-reusable-workflow",
    },
    ("rubygems", "rubygems.pkg.github.com"): {
        "mutability": "immutable",
        "name-uniqueness-scope": "package-name-with-owner",
        "version-uniqueness-rule": "package-name-plus-version",
        "profile-coexistence-rule": "same-name-allowed",
        "credential-posture": "github-token",
        "publish-topology": "github-token",
    },
}


@dataclass(frozen=True, slots=True)
class AuthoringIssue:
    """One authoring validation issue."""

    code: str
    path: str
    message: str
    project_id: str | None = None


class AuthoringValidationError(ValueError):
    """Raised when workflow-release authoring files are invalid."""

    def __init__(self, issues: Sequence[AuthoringIssue]) -> None:
        """Initialize an authoring validation error."""
        self.issues = tuple(issues)
        joined = "; ".join(
            f"{issue.code} {issue.path}: {issue.message}" for issue in issues
        )
        super().__init__(joined)


@dataclass(frozen=True, slots=True)
class Companion:
    """Normalized executable companion declaration."""

    path: str
    role: str
    required: bool


@dataclass(frozen=True, slots=True)
class Artifact:
    """Normalized descriptor artifact declaration."""

    id: str
    role: str
    kind_family: str
    concrete_kind: str
    produced_from: tuple[str, ...]
    variant_id: str
    companions: tuple[Companion, ...]

    @property
    def tuple_key(self) -> tuple[str, str, str]:
        """Return the current-scope artifact compatibility tuple."""
        return (self.role, self.kind_family, self.concrete_kind)


@dataclass(frozen=True, slots=True)
class Variant:
    """Normalized descriptor variant declaration."""

    id: str
    dimensions: Mapping[str, str]
    artifacts: tuple[Artifact, ...]


@dataclass(frozen=True, slots=True)
class TargetUsage:
    """Normalized descriptor target usage declaration."""

    uses: str
    artifacts: tuple[str, ...]
    projection: Mapping[str, object]
    projection_present: bool


@dataclass(frozen=True, slots=True)
class ProjectDescriptor:
    """Normalized release project descriptor."""

    project_id: str
    display_name: str
    ecosystem: str
    release_kind: str
    descriptor_path: str
    release_root: str
    primary_manifest_path: str
    auxiliary_input_paths: tuple[str, ...]
    version_authority_kind: str
    variants: tuple[Variant, ...]
    profiles: Mapping[str, tuple[TargetUsage, ...]]

    @property
    def artifacts_by_id(self) -> Mapping[str, Artifact]:
        """Return descriptor artifacts keyed by descriptor-local id."""
        return {
            artifact.id: artifact
            for variant in self.variants
            for artifact in variant.artifacts
        }


@dataclass(frozen=True, slots=True)
class TargetInstance:
    """Normalized shared target-instance catalog entry."""

    family: str
    instance_id: str
    catalog_ref: str
    contract: str
    destination: Mapping[str, object]
    capabilities: Mapping[str, str]


@dataclass(frozen=True, slots=True)
class AuthoringSnapshot:
    """Validated normalized authoring inputs for later planning."""

    descriptor_api_version: str
    catalog_path: str
    projects: Mapping[str, ProjectDescriptor]
    target_instances: Mapping[str, TargetInstance]

    def dotnet_metadata_input(self, commit_sha: str) -> dict[str, object]:
        """Build the closed Windows .NET metadata input handoff."""
        projects: dict[str, object] = {}
        for project_id in sorted(self.projects):
            descriptor = self.projects[project_id]
            if descriptor.ecosystem != "dotnet":
                continue
            projects[project_id] = {
                "descriptor-path": descriptor.descriptor_path,
                "primary-manifest-path": descriptor.primary_manifest_path,
                "requires-package-id": _requires_package_id(descriptor, self),
            }
        document: dict[str, object] = {
            "api-version": DOTNET_METADATA_INPUT_API_VERSION,
            "kind": "dotnet-planner-metadata-input",
            "commit-sha": commit_sha,
            "projects": projects,
        }
        validate_contract(document)
        return document

    def planner_authoring_inputs(self) -> dict[str, object]:
        """Return the plan envelope authoring-inputs value."""
        return {
            "descriptor-api-version": self.descriptor_api_version,
            "catalog-path": self.catalog_path,
        }


class _IssueCollector:
    """Mutable issue collector used during validation."""

    def __init__(self) -> None:
        """Create an empty issue collector."""
        self.issues: list[AuthoringIssue] = []

    def add(
        self,
        code: str,
        path: str,
        message: str,
        *,
        project_id: str | None = None,
    ) -> None:
        """Record an authoring issue."""
        self.issues.append(AuthoringIssue(code, path, message, project_id))


def validate_authoring(
    repo_root: Path | str = ".",
    *,
    tracked_files: Iterable[str] | None = None,
) -> AuthoringSnapshot:
    """Discover and validate checked-in release descriptors and catalog."""
    root = Path(repo_root)
    tracked = (
        set(tracked_files) if tracked_files is not None else _git_files(root)
    )
    catalog_doc = _load_yaml_file(
        root / CATALOG_PATH, "CATALOG_SCHEMA_INVALID", CATALOG_PATH
    )
    descriptor_paths = sorted(
        path for path in tracked if PurePosixPath(path).name == DESCRIPTOR_NAME
    )
    descriptor_docs = {
        path: _load_yaml_file(root / path, "DESC_SCHEMA_INVALID", path)
        for path in descriptor_paths
        if path.startswith("src/")
    }
    return validate_authoring_documents(
        descriptor_docs,
        catalog_doc,
        tracked_files=tracked,
        catalog_path=CATALOG_PATH,
        repo_root=root,
    )


def validate_authoring_documents(
    descriptor_documents: Mapping[str, object],
    catalog_document: object,
    *,
    tracked_files: Iterable[str],
    catalog_path: str = CATALOG_PATH,
    repo_root: Path | str = ".",
) -> AuthoringSnapshot:
    """Validate already-loaded authoring documents."""
    issues = _IssueCollector()
    tracked = set(tracked_files)
    _validate_descriptor_discovery(set(descriptor_documents), tracked, issues)
    catalog = _parse_catalog(catalog_document, issues, catalog_path)
    projects = {
        path: project
        for path, project in (
            (
                path,
                _parse_descriptor(path, document, tracked, issues),
            )
            for path, document in descriptor_documents.items()
        )
        if project is not None
    }
    if not any(
        issue.code == "CATALOG_SCHEMA_INVALID" for issue in issues.issues
    ):
        _validate_project_set(
            projects,
            catalog,
            issues,
            repo_root=Path(repo_root),
        )
    if issues.issues:
        raise AuthoringValidationError(issues.issues)
    return AuthoringSnapshot(
        descriptor_api_version=API_VERSION,
        catalog_path=catalog_path,
        projects={
            project.project_id: project
            for _, project in sorted(
                projects.items(), key=lambda item: item[1].project_id
            )
        },
        target_instances=dict(sorted(catalog.items())),
    )


def validate_project_descriptor_document(
    descriptor_path: str,
    document: object,
    *,
    tracked_files: Iterable[str],
) -> ProjectDescriptor:
    """Validate one project descriptor without catalog references."""
    issues = _IssueCollector()
    descriptor = _parse_descriptor(
        descriptor_path,
        document,
        set(tracked_files),
        issues,
    )
    if issues.issues or descriptor is None:
        raise AuthoringValidationError(issues.issues)
    return descriptor


def validate_target_catalog_document(
    document: object,
    *,
    catalog_path: str = CATALOG_PATH,
) -> Mapping[str, TargetInstance]:
    """Validate one shared target-instance catalog document."""
    issues = _IssueCollector()
    catalog = _parse_catalog(document, issues, catalog_path)
    if issues.issues:
        raise AuthoringValidationError(issues.issues)
    return catalog


def diagnostics_document(issues: Sequence[AuthoringIssue]) -> dict[str, object]:
    """Convert authoring issues to the planner diagnostics container shape."""
    diagnostics: list[dict[str, object]] = []
    for issue in issues:
        project_id = _project_id_for_issue(issue)
        diagnostic: dict[str, object] = {
            "api-version": "three.release.planner-diagnostic/v1alpha1",
            "kind": "planner-diagnostic",
            "code": issue.code,
            "message": issue.message,
            "phase": "validation",
            "scope-kind": "project" if project_id else "request",
            "blocking": True,
            "details": {"path": issue.path},
        }
        if project_id:
            diagnostic["project-id"] = project_id
        diagnostics.append(diagnostic)
    document: dict[str, object] = {
        "api-version": "three.release.planner-diagnostics/v1alpha1",
        "kind": "planner-diagnostics",
        "diagnostics": diagnostics,
    }
    validate_contract(document)
    return document


def _project_id_for_issue(issue: AuthoringIssue) -> str | None:
    """Return an explicit or descriptor-root-derived issue project id."""
    if issue.project_id:
        return issue.project_id
    if issue.code not in {
        "DESC_SCHEMA_INVALID",
        "DESC_STATIC_INVALID",
        "CATALOG_REF_NOT_FOUND",
    }:
        return None
    return _project_id_for_descriptor_path(issue.path)


def _project_id_for_descriptor_path(path: str) -> str | None:
    """Derive a frozen project id from a descriptor-scoped issue path."""
    for root, (project_id, _) in _FROZEN_DESCRIPTOR_IDENTITY_BY_ROOT.items():
        descriptor_path = f"{root}/{DESCRIPTOR_NAME}"
        if path == descriptor_path or path.startswith(f"{descriptor_path}."):
            return project_id
    return None


def _load_yaml_file(path: Path, code: str, issue_path: str) -> object:
    """Load one YAML authoring file."""
    try:
        return yaml.safe_load(path.read_text(encoding="utf-8"))
    except UnicodeDecodeError as exc:
        message = f"must be UTF-8 encoded: {exc.reason}"
    except yaml.YAMLError as exc:
        message = f"must be valid YAML: {exc}"
    except OSError as exc:
        message = f"could not be read: {exc.strerror or exc}"
    raise AuthoringValidationError(
        [AuthoringIssue(code=code, path=issue_path, message=message)]
    )


def _git_files(root: Path) -> set[str]:
    """Return repository candidate paths relative to the repository root."""
    git = shutil.which("git") or "git"
    result = subprocess.run(  # noqa: S603
        [git, "ls-files", "--cached", "--others", "--exclude-standard", "-z"],
        cwd=root,
        check=True,
        stdout=subprocess.PIPE,
    )
    return {path for path in result.stdout.decode("utf-8").split("\0") if path}


def _validate_descriptor_discovery(
    loaded_paths: set[str], tracked: set[str], issues: _IssueCollector
) -> None:
    """Validate checked-in descriptor placement before schema parsing."""
    all_descriptors = sorted(
        path for path in tracked if PurePosixPath(path).name == DESCRIPTOR_NAME
    )
    for path in all_descriptors:
        if not path.startswith("src/"):
            issues.add(
                "DESC_STATIC_INVALID",
                path,
                "checked-in descriptors must be under src/",
            )
        elif not _is_in_current_scope(path):
            issues.add(
                "DESC_STATIC_INVALID",
                path,
                "descriptor is outside the first-delivery authoring scope",
            )
    missing_loads = {
        path for path in all_descriptors if path.startswith("src/")
    }
    for path in sorted(missing_loads - loaded_paths):
        issues.add("DESC_SCHEMA_INVALID", path, "descriptor was not loaded")
    loaded_roots = {
        str(PurePosixPath(path).parent)
        for path in loaded_paths
        if PurePosixPath(path).name == DESCRIPTOR_NAME
    }
    for root in sorted(_REQUIRED_DESCRIPTOR_ROOTS - loaded_roots):
        issues.add(
            "DESC_STATIC_INVALID",
            f"{root}/{DESCRIPTOR_NAME}",
            "required first-delivery descriptor is missing",
        )


def _is_in_current_scope(descriptor_path: str) -> bool:
    """Return whether a descriptor path is in first-delivery scope."""
    return any(
        descriptor_path.startswith(scope) for scope in _ALLOWED_PROJECT_SCOPES
    )


def _parse_descriptor(
    descriptor_path: str,
    document: object,
    tracked: set[str],
    issues: _IssueCollector,
) -> ProjectDescriptor | None:
    """Parse and validate one project descriptor."""
    obj = _mapping(document, descriptor_path, issues, "DESC_SCHEMA_INVALID")
    if obj is None:
        return None
    _exact_keys(
        obj,
        {"api-version", "kind", "project", "source", "variants", "profiles"},
        descriptor_path,
        issues,
        "DESC_SCHEMA_INVALID",
    )
    if obj.get("api-version") != API_VERSION:
        issues.add(
            "DESC_SCHEMA_INVALID",
            f"{descriptor_path}.api-version",
            "invalid api-version",
        )
    if obj.get("kind") != "project":
        issues.add(
            "DESC_SCHEMA_INVALID", f"{descriptor_path}.kind", "must be project"
        )
    project = _project_block(obj.get("project"), descriptor_path, issues)
    source = _source_block(obj.get("source"), descriptor_path, issues)
    variants = _variants_block(obj.get("variants"), descriptor_path, issues)
    profiles = _profiles_block(obj.get("profiles"), descriptor_path, issues)
    if (
        project is None
        or source is None
        or variants is None
        or profiles is None
    ):
        return None
    release_root = str(PurePosixPath(descriptor_path).parent)
    primary = _resolve_source_path(
        descriptor_path,
        release_root,
        source["primary-manifest"],
        tracked,
        issues,
        field="source.primary-manifest",
    )
    auxiliary = tuple(
        path
        for item in source["auxiliary-inputs"]
        if (
            path := _resolve_source_path(
                descriptor_path,
                release_root,
                item,
                tracked,
                issues,
                field="source.auxiliary-inputs",
            )
        )
    )
    _validate_manifest_type(
        descriptor_path,
        project["ecosystem"],
        source["primary-manifest"],
        primary,
        issues,
        project_id=project["id"],
    )
    _validate_version_authority(descriptor_path, project, source, issues)
    if primary is None:
        return None
    return ProjectDescriptor(
        project_id=project["id"],
        display_name=project["display-name"],
        ecosystem=project["ecosystem"],
        release_kind=project["release-kind"],
        descriptor_path=descriptor_path,
        release_root=release_root,
        primary_manifest_path=primary,
        auxiliary_input_paths=tuple(sorted(auxiliary)),
        version_authority_kind=source["version-authority"],
        variants=tuple(variants),
        profiles=profiles,
    )


def _project_block(
    value: object, descriptor_path: str, issues: _IssueCollector
) -> dict[str, str] | None:
    """Validate and normalize the project block."""
    obj = _mapping(
        value, f"{descriptor_path}.project", issues, "DESC_SCHEMA_INVALID"
    )
    if obj is None:
        return None
    _exact_keys(
        obj,
        {"id", "display-name", "ecosystem", "release-kind"},
        f"{descriptor_path}.project",
        issues,
        "DESC_SCHEMA_INVALID",
    )
    out: dict[str, str] = {}
    for key in ("id", "display-name", "ecosystem", "release-kind"):
        out[key] = _required_string(
            obj, key, f"{descriptor_path}.project", issues
        )
    _canonical_id(out["id"], f"{descriptor_path}.project.id", issues)
    _enum(
        out["ecosystem"],
        _PROJECT_ECOSYSTEMS,
        f"{descriptor_path}.project.ecosystem",
        issues,
    )
    _enum(
        out["release-kind"],
        _RELEASE_KINDS,
        f"{descriptor_path}.project.release-kind",
        issues,
    )
    return out


def _source_block(
    value: object, descriptor_path: str, issues: _IssueCollector
) -> dict[str, Any] | None:
    """Validate and normalize the source block."""
    obj = _mapping(
        value, f"{descriptor_path}.source", issues, "DESC_SCHEMA_INVALID"
    )
    if obj is None:
        return None
    _exact_keys(
        obj,
        {"primary-manifest"},
        f"{descriptor_path}.source",
        issues,
        "DESC_SCHEMA_INVALID",
        optional={"auxiliary-inputs", "version-authority"},
    )
    primary = _required_string(
        obj, "primary-manifest", f"{descriptor_path}.source", issues
    )
    _validate_relative_path(
        primary, f"{descriptor_path}.source.primary-manifest", issues
    )
    raw_aux = obj.get("auxiliary-inputs", [])
    aux_items = _array(
        raw_aux, f"{descriptor_path}.source.auxiliary-inputs", issues
    )
    auxiliary: list[str] = []
    if aux_items is not None:
        for index, item in enumerate(aux_items):
            path = _string(
                item,
                f"{descriptor_path}.source.auxiliary-inputs[{index}]",
                issues,
            )
            _validate_relative_path(
                path,
                f"{descriptor_path}.source.auxiliary-inputs[{index}]",
                issues,
            )
            auxiliary.append(path)
    authority = obj.get("version-authority", "build-system-nbgv")
    authority_str = _string(
        authority, f"{descriptor_path}.source.version-authority", issues
    )
    _enum(
        authority_str,
        _VERSION_AUTHORITIES,
        f"{descriptor_path}.source.version-authority",
        issues,
    )
    return {
        "primary-manifest": primary,
        "auxiliary-inputs": auxiliary,
        "version-authority": authority_str,
    }


def _variants_block(
    value: object, descriptor_path: str, issues: _IssueCollector
) -> list[Variant] | None:
    """Validate and normalize the variants block."""
    items = _array(value, f"{descriptor_path}.variants", issues)
    if items is None:
        return None
    if not items:
        issues.add(
            "DESC_SCHEMA_INVALID",
            f"{descriptor_path}.variants",
            "must not be empty",
        )
    variants: list[Variant] = []
    for index, item in enumerate(items):
        variant = _variant_item(
            item, f"{descriptor_path}.variants[{index}]", issues
        )
        if variant is not None:
            variants.append(variant)
    ids = [variant.id for variant in variants]
    _duplicates(ids, f"{descriptor_path}.variants", issues, "variant id")
    dimensions = [
        tuple(sorted(variant.dimensions.items())) for variant in variants
    ]
    _duplicates(
        dimensions, f"{descriptor_path}.variants", issues, "variant dimensions"
    )
    artifact_ids = [
        artifact.id for variant in variants for artifact in variant.artifacts
    ]
    _duplicates(
        artifact_ids, f"{descriptor_path}.variants", issues, "artifact id"
    )
    return variants


def _variant_item(
    value: object, path: str, issues: _IssueCollector
) -> Variant | None:
    """Validate and normalize one variant."""
    obj = _mapping(value, path, issues, "DESC_SCHEMA_INVALID")
    if obj is None:
        return None
    _exact_keys(
        obj,
        {"id", "dimensions", "artifacts"},
        path,
        issues,
        "DESC_SCHEMA_INVALID",
    )
    variant_id = _required_string(obj, "id", path, issues)
    _canonical_id(variant_id, f"{path}.id", issues)
    dimensions = _string_map(
        obj.get("dimensions"), f"{path}.dimensions", issues
    )
    artifact_items = _array(obj.get("artifacts"), f"{path}.artifacts", issues)
    artifacts: list[Artifact] = []
    if artifact_items is not None:
        for index, artifact_value in enumerate(artifact_items):
            artifact = _artifact_item(
                artifact_value,
                f"{path}.artifacts[{index}]",
                variant_id,
                issues,
            )
            if artifact is not None:
                artifacts.append(artifact)
    _duplicates(
        [artifact.tuple_key for artifact in artifacts],
        f"{path}.artifacts",
        issues,
        "artifact tuple",
    )
    for artifact in artifacts:
        if artifact.companions and artifact.concrete_kind != "executable":
            issues.add(
                "DESC_SCHEMA_INVALID",
                f"{path}.artifacts.{artifact.id}.companions",
                "companions are only valid for executable artifacts",
            )
        for source_id in artifact.produced_from:
            if source_id not in {item.id for item in artifacts}:
                issues.add(
                    "DESC_SCHEMA_INVALID",
                    f"{path}.artifacts.{artifact.id}.produced-from",
                    f"unknown sibling artifact id {source_id!r}",
                )
    return Variant(variant_id, dimensions, tuple(artifacts))


def _artifact_item(
    value: object, path: str, variant_id: str, issues: _IssueCollector
) -> Artifact | None:
    """Validate and normalize one artifact."""
    obj = _mapping(value, path, issues, "DESC_SCHEMA_INVALID")
    if obj is None:
        return None
    _exact_keys(
        obj,
        {"id", "role", "kind-family", "concrete-kind"},
        path,
        issues,
        "DESC_SCHEMA_INVALID",
        optional={"produced-from", "companions"},
    )
    artifact_id = _required_string(obj, "id", path, issues)
    _canonical_id(artifact_id, f"{path}.id", issues)
    role = _required_string(obj, "role", path, issues)
    family = _required_string(obj, "kind-family", path, issues)
    concrete = _required_string(obj, "concrete-kind", path, issues)
    if (role, family, concrete) not in _all_allowed_artifact_tuples():
        issues.add(
            "DESC_SCHEMA_INVALID",
            path,
            "artifact tuple is out of current scope",
        )
    raw_sources = obj.get("produced-from", [])
    source_items = _array(raw_sources, f"{path}.produced-from", issues)
    produced_from: list[str] = []
    if source_items is not None:
        for index, item in enumerate(source_items):
            source_id = _string(item, f"{path}.produced-from[{index}]", issues)
            _canonical_id(source_id, f"{path}.produced-from[{index}]", issues)
            produced_from.append(source_id)
    companions = _companion_items(obj.get("companions", []), path, issues)
    return Artifact(
        artifact_id,
        role,
        family,
        concrete,
        tuple(produced_from),
        variant_id,
        tuple(companions),
    )


def _companion_items(
    value: object, path: str, issues: _IssueCollector
) -> list[Companion]:
    """Validate and normalize executable companion declarations."""
    items = _array(value, f"{path}.companions", issues)
    companions: list[Companion] = []
    if items is None:
        return companions
    seen: list[str] = []
    for index, item in enumerate(items):
        item_path = f"{path}.companions[{index}]"
        obj = _mapping(item, item_path, issues, "DESC_SCHEMA_INVALID")
        if obj is None:
            continue
        _exact_keys(
            obj,
            {"path", "role"},
            item_path,
            issues,
            "DESC_SCHEMA_INVALID",
            optional={"required"},
        )
        companion_path = _required_string(obj, "path", item_path, issues)
        role = _required_string(obj, "role", item_path, issues)
        required = obj.get("required", True)
        if not isinstance(required, bool):
            issues.add(
                "DESC_SCHEMA_INVALID",
                f"{item_path}.required",
                "must be a boolean",
            )
            required = True
        _root_level_companion_path(companion_path, f"{item_path}.path", issues)
        _canonical_id(role, f"{item_path}.role", issues)
        seen.append(companion_path)
        companions.append(Companion(companion_path, role, bool(required)))
    _duplicates(seen, f"{path}.companions", issues, "companion path")
    return companions


def _root_level_companion_path(
    value: str, path: str, issues: _IssueCollector
) -> None:
    """Validate a descriptor-root output companion path or glob."""
    if (
        not value
        or value.startswith("/")
        or "\\" in value
        or "/" in value
        or _WINDOWS_DRIVE_PATH_RE.match(value)
    ):
        issues.add(
            "DESC_SCHEMA_INVALID",
            path,
            "must be a non-empty root-level relative path or glob",
        )
        return
    if value in {".", "..", "**"}:
        issues.add(
            "DESC_SCHEMA_INVALID",
            path,
            "must not be dot, dot-dot, or recursive glob",
        )


def _profiles_block(
    value: object, descriptor_path: str, issues: _IssueCollector
) -> Mapping[str, tuple[TargetUsage, ...]] | None:
    """Validate and normalize the profiles block."""
    obj = _mapping(
        value, f"{descriptor_path}.profiles", issues, "DESC_SCHEMA_INVALID"
    )
    if obj is None:
        return None
    _exact_keys(
        obj,
        set(_PROFILES),
        f"{descriptor_path}.profiles",
        issues,
        "DESC_SCHEMA_INVALID",
    )
    profiles: dict[str, tuple[TargetUsage, ...]] = {}
    for profile in _PROFILES:
        profiles[profile] = ()
    for profile in _PROFILES:
        profile_obj = _mapping(
            obj.get(profile),
            f"{descriptor_path}.profiles.{profile}",
            issues,
            "DESC_SCHEMA_INVALID",
        )
        if profile_obj is None:
            continue
        _exact_keys(
            profile_obj,
            {"targets"},
            f"{descriptor_path}.profiles.{profile}",
            issues,
            "DESC_SCHEMA_INVALID",
        )
        target_items = _array(
            profile_obj.get("targets"),
            f"{descriptor_path}.profiles.{profile}.targets",
            issues,
        )
        targets: list[TargetUsage] = []
        if target_items is not None:
            for index, item in enumerate(target_items):
                target = _target_usage_item(
                    item,
                    f"{descriptor_path}.profiles.{profile}.targets[{index}]",
                    issues,
                )
                if target is not None:
                    targets.append(target)
        profiles[profile] = tuple(targets)
    return profiles


def _target_usage_item(
    value: object, path: str, issues: _IssueCollector
) -> TargetUsage | None:
    """Validate and normalize one profile target usage."""
    obj = _mapping(value, path, issues, "DESC_SCHEMA_INVALID")
    if obj is None:
        return None
    _exact_keys(
        obj,
        {"uses", "artifacts"},
        path,
        issues,
        "DESC_SCHEMA_INVALID",
        optional={"projection"},
    )
    uses = _required_string(obj, "uses", path, issues)
    if uses.count("/") != 1:
        issues.add(
            "DESC_SCHEMA_INVALID", f"{path}.uses", "must be family/instance-id"
        )
    else:
        family, instance = uses.split("/", maxsplit=1)
        _enum(family, _TARGET_FAMILIES, f"{path}.uses", issues)
        _canonical_id(instance, f"{path}.uses", issues)
    artifact_items = _array(obj.get("artifacts"), f"{path}.artifacts", issues)
    artifact_ids: list[str] = []
    if artifact_items is not None:
        if not artifact_items:
            issues.add(
                "DESC_SCHEMA_INVALID", f"{path}.artifacts", "must not be empty"
            )
        for index, item in enumerate(artifact_items):
            artifact_id = _string(item, f"{path}.artifacts[{index}]", issues)
            _canonical_id(artifact_id, f"{path}.artifacts[{index}]", issues)
            artifact_ids.append(artifact_id)
    _duplicates(artifact_ids, f"{path}.artifacts", issues, "artifact reference")
    projection_present = "projection" in obj
    projection = obj.get("projection", {})
    projection_obj = _mapping(
        projection, f"{path}.projection", issues, "DESC_SCHEMA_INVALID"
    )
    return TargetUsage(
        uses,
        tuple(artifact_ids),
        projection_obj or {},
        projection_present,
    )


def _parse_catalog(
    document: object, issues: _IssueCollector, catalog_path: str
) -> Mapping[str, TargetInstance]:
    """Parse and validate the shared target-instance catalog."""
    obj = _mapping(document, catalog_path, issues, "CATALOG_SCHEMA_INVALID")
    if obj is None:
        return {}
    _exact_keys(
        obj,
        {"api-version", "kind", "families"},
        catalog_path,
        issues,
        "CATALOG_SCHEMA_INVALID",
    )
    if obj.get("api-version") != API_VERSION:
        issues.add(
            "CATALOG_SCHEMA_INVALID",
            f"{catalog_path}.api-version",
            "invalid api-version",
        )
    if obj.get("kind") != "target-instance-catalog":
        issues.add(
            "CATALOG_SCHEMA_INVALID",
            f"{catalog_path}.kind",
            "must be target-instance-catalog",
        )
    families = _mapping(
        obj.get("families"),
        f"{catalog_path}.families",
        issues,
        "CATALOG_SCHEMA_INVALID",
    )
    if families is None:
        return {}
    _exact_keys(
        families,
        _TARGET_FAMILIES,
        f"{catalog_path}.families",
        issues,
        "CATALOG_SCHEMA_INVALID",
    )
    instances: dict[str, TargetInstance] = {}
    for family in sorted(
        key
        for key in families
        if isinstance(key, str) and key in _TARGET_FAMILIES
    ):
        family_obj = _mapping(
            families[family],
            f"{catalog_path}.families.{family}",
            issues,
            "CATALOG_SCHEMA_INVALID",
        )
        if family_obj is None:
            continue
        _exact_keys(
            family_obj,
            {"instances"},
            f"{catalog_path}.families.{family}",
            issues,
            "CATALOG_SCHEMA_INVALID",
        )
        raw_instances = _array(
            family_obj.get("instances"),
            f"{catalog_path}.families.{family}.instances",
            issues,
            code="CATALOG_SCHEMA_INVALID",
        )
        if raw_instances is None:
            continue
        ids: list[str] = []
        for index, value in enumerate(raw_instances):
            instance = _catalog_instance(
                family,
                value,
                f"{catalog_path}.families.{family}.instances[{index}]",
                issues,
            )
            if instance is not None:
                ids.append(instance.instance_id)
                instances[instance.catalog_ref] = instance
        _duplicates(
            ids,
            f"{catalog_path}.families.{family}.instances",
            issues,
            "instance id",
            code="CATALOG_SCHEMA_INVALID",
        )
    return instances


def _catalog_instance(
    family: str, value: object, path: str, issues: _IssueCollector
) -> TargetInstance | None:
    """Validate and normalize one catalog target instance."""
    obj = _mapping(value, path, issues, "CATALOG_SCHEMA_INVALID")
    if obj is None:
        return None
    _exact_keys(
        obj,
        {"id", "contract", "destination", "capabilities"},
        path,
        issues,
        "CATALOG_SCHEMA_INVALID",
    )
    instance_id = _required_string(
        obj, "id", path, issues, code="CATALOG_SCHEMA_INVALID"
    )
    _canonical_id(
        instance_id, f"{path}.id", issues, code="CATALOG_SCHEMA_INVALID"
    )
    contract = _required_string(
        obj, "contract", path, issues, code="CATALOG_SCHEMA_INVALID"
    )
    if contract != _CONTRACT_BY_FAMILY[family]:
        issues.add(
            "CATALOG_SCHEMA_INVALID",
            f"{path}.contract",
            "does not match target family",
        )
    destination = _destination(
        family, obj.get("destination"), f"{path}.destination", issues
    )
    capabilities = _capabilities(
        family,
        destination,
        obj.get("capabilities"),
        f"{path}.capabilities",
        issues,
    )
    return TargetInstance(
        family=family,
        instance_id=instance_id,
        catalog_ref=f"{family}/{instance_id}",
        contract=contract,
        destination=destination,
        capabilities=capabilities,
    )


def _destination(
    family: str, value: object, path: str, issues: _IssueCollector
) -> Mapping[str, object]:
    """Validate a catalog destination object."""
    obj = _mapping(value, path, issues, "CATALOG_SCHEMA_INVALID")
    if obj is None:
        return {}
    if family == "github-release":
        _exact_keys(
            obj,
            {"host", "owner", "repo"},
            path,
            issues,
            "CATALOG_SCHEMA_INVALID",
        )
        if (
            obj.get("host") != "github"
            or obj.get("owner") != "hcoona"
            or obj.get("repo") != "three"
        ):
            issues.add(
                "CATALOG_SCHEMA_INVALID",
                path,
                "must target hcoona/three on github",
            )
        _github_slug(obj.get("owner"), f"{path}.owner", issues)
        _github_slug(obj.get("repo"), f"{path}.repo", issues)
    elif family in {"nuget", "npm", "rubygems"}:
        _host_destination(family, obj, path, issues)
    elif family == "pypi":
        _exact_keys(obj, {"host"}, path, issues, "CATALOG_SCHEMA_INVALID")
        if obj.get("host") != "pypi.org":
            issues.add(
                "CATALOG_SCHEMA_INVALID", f"{path}.host", "must be pypi.org"
            )
    return obj


def _host_destination(
    family: str, obj: Mapping[str, object], path: str, issues: _IssueCollector
) -> None:
    """Validate package-registry host destinations with optional owner."""
    hosts = {
        "nuget": ("nuget.org", "nuget.pkg.github.com"),
        "npm": ("registry.npmjs.org", "npm.pkg.github.com"),
        "rubygems": ("rubygems.org", "rubygems.pkg.github.com"),
    }[family]
    host = obj.get("host")
    allowed = {"host", "owner"} if host == hosts[1] else {"host"}
    _exact_keys(
        obj,
        {"host"},
        path,
        issues,
        "CATALOG_SCHEMA_INVALID",
        optional=allowed - {"host"},
    )
    if host not in hosts:
        issues.add("CATALOG_SCHEMA_INVALID", f"{path}.host", "invalid host")
    if host == hosts[1]:
        _github_slug(obj.get("owner"), f"{path}.owner", issues)
    elif "owner" in obj:
        issues.add(
            "CATALOG_SCHEMA_INVALID",
            f"{path}.owner",
            "is forbidden for this host",
        )


def _capabilities(
    family: str,
    destination: Mapping[str, object],
    value: object,
    path: str,
    issues: _IssueCollector,
) -> Mapping[str, str]:
    """Validate current-scope static target capabilities."""
    obj = _mapping(value, path, issues, "CATALOG_SCHEMA_INVALID")
    if obj is None:
        return {}
    required = {
        "mutability",
        "name-uniqueness-scope",
        "version-uniqueness-rule",
        "profile-coexistence-rule",
        "credential-posture",
        "publish-topology",
    }
    _exact_keys(obj, required, path, issues, "CATALOG_SCHEMA_INVALID")
    actual = {
        key: _string(
            obj.get(key),
            f"{path}.{key}",
            issues,
            code="CATALOG_SCHEMA_INVALID",
        )
        for key in required
    }
    expected = _CAPABILITY_ASSIGNMENTS.get(
        (family, str(destination.get("host")))
    )
    if expected is not None and actual != expected:
        issues.add(
            "CATALOG_SCHEMA_INVALID",
            path,
            "capabilities do not match family and host",
        )
    return actual


def _validate_project_set(
    projects_by_path: Mapping[str, ProjectDescriptor],
    catalog: Mapping[str, TargetInstance],
    issues: _IssueCollector,
    *,
    repo_root: Path,
) -> None:
    """Run cross-file static authoring validation."""
    _validate_duplicate_project_ids(projects_by_path.values(), issues)
    _validate_nested_roots(projects_by_path.values(), issues)
    for project in projects_by_path.values():
        _validate_frozen_descriptor_identity(project, issues)
        _validate_frozen_profile_targets(project, issues)
        if any(
            artifact.concrete_kind == "npm-package"
            for artifact in project.artifacts_by_id.values()
        ):
            _manifest_npm_name(project, repo_root, issues)
        _validate_descriptor_targets(
            project, catalog, issues, repo_root=repo_root
        )
    _validate_static_coexistence(
        projects_by_path.values(),
        catalog,
        issues,
        repo_root=repo_root,
    )


def _validate_duplicate_project_ids(
    projects: Iterable[ProjectDescriptor], issues: _IssueCollector
) -> None:
    """Record duplicate project-id diagnostics at each descriptor path."""
    by_project_id: dict[str, list[ProjectDescriptor]] = {}
    for project in projects:
        by_project_id.setdefault(project.project_id, []).append(project)
    for project_id, duplicates in by_project_id.items():
        if len(duplicates) <= 1:
            continue
        for project in duplicates:
            issues.add(
                "DESC_STATIC_INVALID",
                f"{project.descriptor_path}.project.id",
                f"duplicate project id: {project_id!r}",
                project_id=(
                    _project_id_for_descriptor_path(project.descriptor_path)
                    or project.project_id
                ),
            )


def _validate_nested_roots(
    projects: Iterable[ProjectDescriptor], issues: _IssueCollector
) -> None:
    """Reject ancestor/descendant descriptor roots."""
    roots = sorted(project.release_root for project in projects)
    for index, root in enumerate(roots):
        prefix = f"{root}/"
        for other in roots[index + 1 :]:
            if other.startswith(prefix):
                issues.add(
                    "DESC_STATIC_INVALID",
                    other,
                    "descriptor roots must not be nested",
                )


def _validate_frozen_descriptor_identity(
    project: ProjectDescriptor, issues: _IssueCollector
) -> None:
    """Validate frozen root, project-id, and manifest bindings."""
    expected = _FROZEN_DESCRIPTOR_IDENTITY_BY_ROOT.get(project.release_root)
    if expected is None:
        issues.add(
            "DESC_STATIC_INVALID",
            project.descriptor_path,
            "descriptor root is not in the frozen first-delivery "
            "identity table",
            project_id=project.project_id,
        )
        return
    expected_project_id, expected_manifest = expected
    if project.project_id != expected_project_id:
        issues.add(
            "DESC_STATIC_INVALID",
            f"{project.descriptor_path}.project.id",
            "project id must match the frozen descriptor root identity",
            project_id=expected_project_id,
        )
    expected_manifest_path = f"{project.release_root}/{expected_manifest}"
    if project.primary_manifest_path != expected_manifest_path:
        issues.add(
            "DESC_STATIC_INVALID",
            f"{project.descriptor_path}.source.primary-manifest",
            "primary manifest must match the frozen descriptor root identity",
            project_id=expected_project_id,
        )


def _validate_frozen_profile_targets(
    project: ProjectDescriptor, issues: _IssueCollector
) -> None:
    """Validate the frozen first-delivery per-project target baseline."""
    expected_profiles = _FROZEN_PROFILE_TARGETS_BY_PROJECT_ID.get(
        project.project_id
    )
    if expected_profiles is None:
        issues.add(
            "DESC_STATIC_INVALID",
            project.descriptor_path,
            "project is not in the frozen first-delivery target baseline",
            project_id=project.project_id,
        )
        return
    for profile, expected_targets in expected_profiles.items():
        actual_targets = {
            target.uses for target in project.profiles.get(profile, ())
        }
        if actual_targets != expected_targets:
            issues.add(
                "DESC_STATIC_INVALID",
                f"{project.descriptor_path}.profiles.{profile}.targets",
                "targets must match the frozen first-delivery baseline",
                project_id=project.project_id,
            )
    expected_artifacts = _FROZEN_TARGET_ARTIFACT_SEMANTICS_BY_PROJECT_ID.get(
        project.project_id,
        {},
    )
    artifacts_by_id = project.artifacts_by_id
    variant_dimensions = {
        variant.id: tuple(sorted(variant.dimensions.items()))
        for variant in project.variants
    }
    for profile, targets_by_uses in expected_artifacts.items():
        targets = {
            target.uses: target for target in project.profiles.get(profile, ())
        }
        for uses, expected_semantics in targets_by_uses.items():
            target = targets.get(uses)
            if target is None:
                continue
            actual_semantics = [
                (
                    variant_dimensions.get(artifact.variant_id, ()),
                    artifact.tuple_key,
                )
                for artifact_id in target.artifacts
                if (artifact := artifacts_by_id.get(artifact_id)) is not None
            ]
            if _semantic_counts(actual_semantics) != _semantic_counts(
                expected_semantics
            ):
                issues.add(
                    "DESC_STATIC_INVALID",
                    f"{project.descriptor_path}.profiles.{profile}.targets",
                    "target artifact semantics must match the frozen "
                    "first-delivery baseline",
                    project_id=project.project_id,
                )


def _semantic_counts(
    artifacts: Iterable[_SemanticArtifact],
) -> Mapping[_SemanticArtifact, int]:
    """Return counts for semantic artifact identities."""
    counts: dict[_SemanticArtifact, int] = {}
    for artifact in artifacts:
        counts[artifact] = counts.get(artifact, 0) + 1
    return counts


def _validate_descriptor_targets(
    project: ProjectDescriptor,
    catalog: Mapping[str, TargetInstance],
    issues: _IssueCollector,
    *,
    repo_root: Path,
) -> None:
    """Validate one descriptor against target catalog rules."""
    artifacts = project.artifacts_by_id
    for profile, targets in project.profiles.items():
        _validate_profile_targets(
            project,
            profile,
            targets,
            catalog,
            artifacts,
            issues,
            repo_root=repo_root,
        )


def _validate_profile_targets(  # noqa: PLR0913
    project: ProjectDescriptor,
    profile: str,
    targets: Sequence[TargetUsage],
    catalog: Mapping[str, TargetInstance],
    artifacts: Mapping[str, Artifact],
    issues: _IssueCollector,
    *,
    repo_root: Path,
) -> None:
    """Validate one profile target list."""
    _duplicates(
        [target.uses for target in targets],
        f"{project.descriptor_path}.profiles.{profile}.targets",
        issues,
        "target reference",
    )
    families: list[str] = []
    for index, target in enumerate(targets):
        path = f"{project.descriptor_path}.profiles.{profile}.targets[{index}]"
        instance = catalog.get(target.uses)
        if instance is None:
            issues.add(
                "CATALOG_REF_NOT_FOUND",
                f"{path}.uses",
                "target reference not found",
                project_id=project.project_id,
            )
            continue
        families.append(instance.family)
        if profile == "buddy" and instance.family == "pypi":
            issues.add(
                "DESC_STATIC_INVALID",
                path,
                "buddy profile may not publish to PyPI",
                project_id=project.project_id,
            )
        selected = _resolve_target_artifacts(
            project, target, artifacts, path, issues
        )
        _validate_projection(
            project,
            target,
            instance,
            selected,
            path,
            issues,
            repo_root=repo_root,
        )
        _validate_contract_compatibility(
            project, instance, selected, path, issues
        )
    if (
        any(family != "github-release" for family in families)
        and families.count("github-release") != 1
    ):
        issues.add(
            "DESC_STATIC_INVALID",
            f"{project.descriptor_path}.profiles.{profile}.targets",
            "non-GitHub targets require exactly one github-release target",
            project_id=project.project_id,
        )


def _resolve_target_artifacts(
    project: ProjectDescriptor,
    target: TargetUsage,
    artifacts: Mapping[str, Artifact],
    path: str,
    issues: _IssueCollector,
) -> list[Artifact]:
    """Resolve target artifact handles."""
    selected: list[Artifact] = []
    for artifact_id in target.artifacts:
        artifact = artifacts.get(artifact_id)
        if artifact is None:
            issues.add(
                "DESC_STATIC_INVALID",
                f"{path}.artifacts",
                f"unknown artifact id {artifact_id!r}",
                project_id=project.project_id,
            )
        else:
            selected.append(artifact)
    return selected


def _validate_projection(  # noqa: PLR0913
    project: ProjectDescriptor,
    target: TargetUsage,
    instance: TargetInstance,
    artifacts: Sequence[Artifact],
    path: str,
    issues: _IssueCollector,
    *,
    repo_root: Path,
) -> None:
    """Validate family-specific descriptor projection data."""
    projection = target.projection
    if instance.family == "github-release":
        _github_release_projection(project, target, projection, path, issues)
    elif instance.family == "npm":
        _npm_projection(
            project,
            projection,
            artifacts,
            instance,
            path,
            issues,
            repo_root=repo_root,
        )
    elif target.projection_present:
        issues.add(
            "DESC_SCHEMA_INVALID",
            f"{path}.projection",
            "projection is not allowed for this family",
            project_id=project.project_id,
        )


def _github_release_projection(
    project: ProjectDescriptor,
    target: TargetUsage,
    projection: Mapping[str, object],
    path: str,
    issues: _IssueCollector,
) -> None:
    """Validate GitHub Release asset-label projection."""
    _exact_keys(
        projection,
        set(),
        f"{path}.projection",
        issues,
        "DESC_SCHEMA_INVALID",
        optional={"asset-labels"},
    )
    labels = projection.get("asset-labels", {})
    label_map = _mapping(
        labels, f"{path}.projection.asset-labels", issues, "DESC_SCHEMA_INVALID"
    )
    if label_map is None:
        return
    for key, label in label_map.items():
        if key not in target.artifacts:
            issues.add(
                "DESC_STATIC_INVALID",
                f"{path}.projection.asset-labels.{key}",
                "label key must be in target artifacts",
                project_id=project.project_id,
            )
        _string(label, f"{path}.projection.asset-labels.{key}", issues)


def _npm_projection(  # noqa: PLR0913
    project: ProjectDescriptor,
    projection: Mapping[str, object],
    artifacts: Sequence[Artifact],
    instance: TargetInstance,
    path: str,
    issues: _IssueCollector,
    *,
    repo_root: Path,
) -> None:
    """Validate npm package-name projection."""
    _exact_keys(
        projection,
        set(),
        f"{path}.projection",
        issues,
        "DESC_SCHEMA_INVALID",
        optional={"package-name"},
    )
    package_name = projection.get("package-name")
    if package_name is not None:
        name = _string(package_name, f"{path}.projection.package-name", issues)
        name_path = f"{path}.projection.package-name"
    else:
        name = _manifest_npm_name(project, repo_root, issues)
        name_path = f"{project.primary_manifest_path}.name"
    if name is None:
        issues.add(
            "DESC_STATIC_INVALID",
            name_path,
            "npm package name could not be resolved",
            project_id=project.project_id,
        )
    else:
        _npm_name(name, name_path, issues)
        if instance.destination.get("host") == "npm.pkg.github.com":
            expected_scope = f"@{instance.destination.get('owner')}"
            if not name.startswith(f"{expected_scope}/"):
                issues.add(
                    "DESC_STATIC_INVALID",
                    name_path,
                    "scope must match catalog destination owner",
                    project_id=project.project_id,
                )
    if len(artifacts) != 1 or artifacts[0].tuple_key != (
        "primary-package",
        "package",
        "npm-package",
    ):
        issues.add(
            "DESC_STATIC_INVALID",
            path,
            "npm projection requires one npm-package artifact",
            project_id=project.project_id,
        )


def _validate_contract_compatibility(
    project: ProjectDescriptor,
    instance: TargetInstance,
    artifacts: Sequence[Artifact],
    path: str,
    issues: _IssueCollector,
) -> None:
    """Validate target artifact tuples against catalog contract rules."""
    allowed = _ALLOWED_TUPLES_BY_CONTRACT[instance.contract]
    for artifact in artifacts:
        if artifact.tuple_key not in allowed:
            issues.add(
                "DESC_STATIC_INVALID",
                path,
                "artifact tuple is incompatible with target contract",
                project_id=project.project_id,
            )
    counts: dict[tuple[str, str, str], int] = {}
    for artifact in artifacts:
        counts[artifact.tuple_key] = counts.get(artifact.tuple_key, 0) + 1
    variants = {artifact.variant_id for artifact in artifacts}
    if instance.contract == "github-release-assets":
        return
    if len(variants) > 1:
        issues.add(
            "DESC_STATIC_INVALID",
            path,
            "target artifacts must come from one variant",
            project_id=project.project_id,
        )
    expected = {
        "nuget-publish": (
            ("primary-package", "package", "nuget"),
            1,
            ("symbols", "package", "snupkg"),
            1,
        ),
        "pypi-publish": (
            ("primary-package", "package", "wheel"),
            1,
            ("primary-package", "package", "sdist"),
            1,
        ),
        "npm-publish": (
            ("primary-package", "package", "npm-package"),
            1,
            None,
            0,
        ),
        "rubygems-publish": (
            ("primary-package", "package", "rubygem"),
            1,
            None,
            0,
        ),
    }[instance.contract]
    required_tuple, required_count, optional_tuple, optional_max = expected
    if counts.get(required_tuple, 0) != required_count:
        issues.add(
            "DESC_STATIC_INVALID",
            path,
            "required artifact tuple count is invalid",
            project_id=project.project_id,
        )
    for tuple_key, count in counts.items():
        if tuple_key == required_tuple:
            continue
        if tuple_key == optional_tuple and count <= optional_max:
            continue
        issues.add(
            "DESC_STATIC_INVALID",
            path,
            "optional artifact tuple count is invalid",
            project_id=project.project_id,
        )


def _validate_static_coexistence(
    projects: Iterable[ProjectDescriptor],
    catalog: Mapping[str, TargetInstance],
    issues: _IssueCollector,
    *,
    repo_root: Path,
) -> None:
    """Reject static buddy/official package identity conflicts."""
    for project in projects:
        seen: dict[tuple[str, str, object, str], str] = {}
        for profile, targets in project.profiles.items():
            for target in targets:
                instance = catalog.get(target.uses)
                if instance is None or instance.family == "github-release":
                    continue
                name = _static_package_name(
                    project, target, instance, repo_root, issues
                )
                if name is None:
                    continue
                identity = (
                    instance.family,
                    str(instance.destination.get("host")),
                    instance.destination.get("owner"),
                    name,
                )
                rule = instance.capabilities.get("profile-coexistence-rule")
                if (
                    rule == "requires-distinct-name"
                    and identity in seen
                    and seen[identity] != profile
                ):
                    issues.add(
                        "DESC_STATIC_INVALID",
                        project.descriptor_path,
                        "buddy and official resolve the same package-registry "
                        "identity",
                        project_id=project.project_id,
                    )
                seen[identity] = profile


def _static_package_name(
    project: ProjectDescriptor,
    target: TargetUsage,
    instance: TargetInstance,
    repo_root: Path,
    issues: _IssueCollector,
) -> str | None:
    """Return statically available package names for coexistence checks."""
    manifest = repo_root / project.primary_manifest_path
    name: str | None = None
    if instance.family == "pypi":
        try:
            data = tomllib.loads(manifest.read_text(encoding="utf-8"))
        except (OSError, UnicodeDecodeError, tomllib.TOMLDecodeError) as exc:
            _manifest_read_issue(
                issues, project, project.primary_manifest_path, exc
            )
            return None
        project_data = data.get("project")
        if isinstance(project_data, Mapping):
            raw_name = project_data.get("name")
            if _validate_manifest_name(
                raw_name,
                "PyPI package name",
                _PYPI_NAME_RE,
                lambda message: _manifest_name_issue(
                    issues, project, project.primary_manifest_path, message
                ),
            ) and isinstance(raw_name, str):
                name = _pep503_name(raw_name)
        else:
            _manifest_name_issue(
                issues,
                project,
                project.primary_manifest_path,
                "PyPI package name is missing",
            )
    elif instance.family == "npm":
        projected = target.projection.get("package-name")
        if isinstance(projected, str):
            name = projected
        else:
            name = _manifest_npm_name(project, repo_root, issues)
    elif instance.family == "rubygems":
        name = _read_gemspec_name(manifest, issues, project)
    return name


def _manifest_npm_name(
    project: ProjectDescriptor, repo_root: Path, issues: _IssueCollector
) -> str | None:
    """Return package.json name for a node descriptor if it can be read."""
    manifest = repo_root / project.primary_manifest_path
    try:
        data = json.loads(manifest.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        _manifest_read_issue(
            issues, project, project.primary_manifest_path, exc
        )
        return None
    if not isinstance(data, Mapping):
        return None
    name = data.get("name")
    if not _validate_manifest_name(
        name,
        "npm package name",
        _NPM_NAME_RE,
        lambda message: _manifest_name_issue(
            issues, project, project.primary_manifest_path, message
        ),
    ) or not isinstance(name, str):
        return None
    return name


def _requires_package_id(
    project: ProjectDescriptor, snapshot: AuthoringSnapshot
) -> bool:
    """Return whether a .NET metadata input entry needs PackageId."""
    if any(
        artifact.concrete_kind in {"nuget", "snupkg"}
        for artifact in project.artifacts_by_id.values()
    ):
        return True
    return any(
        snapshot.target_instances[target.uses].family == "nuget"
        for targets in project.profiles.values()
        for target in targets
    )


def _resolve_source_path(  # noqa: PLR0913
    descriptor_path: str,
    release_root: str,
    relative_path: str,
    tracked: set[str],
    issues: _IssueCollector,
    *,
    field: str,
) -> str | None:
    """Resolve a descriptor-root-relative source file path."""
    full = f"{release_root}/{relative_path}"
    if full not in tracked:
        issues.add(
            "DESC_STATIC_INVALID",
            f"{descriptor_path}.{field}",
            f"{relative_path!r} is not a checked-in file",
        )
        return None
    return full


def _validate_manifest_type(  # noqa: PLR0913
    descriptor_path: str,
    ecosystem: str,
    authored_path: str,
    primary_path: str | None,
    issues: _IssueCollector,
    *,
    project_id: str,
) -> None:
    """Validate current-scope ecosystem-to-primary-manifest mapping."""
    if primary_path is None:
        return
    valid = (
        (ecosystem == "dotnet" and primary_path.endswith(".csproj"))
        or (ecosystem == "python" and authored_path == "pyproject.toml")
        or (ecosystem == "node" and authored_path == "package.json")
        or (ecosystem == "ruby" and primary_path.endswith(".gemspec"))
    )
    if not valid:
        issues.add(
            "DESC_STATIC_INVALID",
            f"{descriptor_path}.source.primary-manifest",
            "does not match project ecosystem",
            project_id=project_id,
        )


def _validate_version_authority(
    descriptor_path: str,
    project: Mapping[str, str],
    source: Mapping[str, Any],
    issues: _IssueCollector,
) -> None:
    """Validate current-scope version-authority restrictions."""
    if source["version-authority"] != "nbgv-python-pyproject-version":
        return
    if not (
        project["id"] == "nbgv-python"
        and project["ecosystem"] == "python"
        and source["primary-manifest"] == "pyproject.toml"
    ):
        issues.add(
            "DESC_STATIC_INVALID",
            f"{descriptor_path}.source.version-authority",
            "nbgv-python-pyproject-version is only valid for nbgv-python",
            project_id=project["id"],
        )


def _validate_relative_path(
    path: str, issue_path: str, issues: _IssueCollector
) -> None:
    """Validate a descriptor-root-relative normalized POSIX path."""
    if (
        path == ""
        or path.startswith("/")
        or "\\" in path
        or re.match(r"^[A-Za-z]:", path)
    ):
        issues.add(
            "DESC_SCHEMA_INVALID",
            issue_path,
            "must be a relative normalized path",
        )
        return
    raw_parts = path.split("/")
    if any(part in {"", ".", ".."} for part in raw_parts):
        issues.add(
            "DESC_SCHEMA_INVALID",
            issue_path,
            "must not contain empty, . or .. segments",
        )
        return
    parts = PurePosixPath(path).parts
    if any(part in {"", ".", ".."} for part in parts):
        issues.add(
            "DESC_SCHEMA_INVALID",
            issue_path,
            "must not contain empty, . or .. segments",
        )


def _all_allowed_artifact_tuples() -> set[tuple[str, str, str]]:
    """Return all artifact tuples accepted by current-scope contracts."""
    return set().union(*_ALLOWED_TUPLES_BY_CONTRACT.values())


def _pep503_name(name: str) -> str:
    """Normalize a Python project name using PEP 503 rules."""
    return re.sub(r"[-_.]+", "-", name).lower()


def _read_gemspec_name(
    path: Path, issues: _IssueCollector, project: ProjectDescriptor
) -> str | None:
    """Read supported Gem::Specification name assignments from a gemspec."""
    try:
        content = path.read_text(encoding="utf-8")
    except (OSError, UnicodeDecodeError) as exc:
        _manifest_read_issue(
            issues, project, project.primary_manifest_path, exc
        )
        return None
    raw_name = _extract_gemspec_name(content)
    if not _validate_manifest_name(
        raw_name,
        "RubyGems package name",
        _GEM_NAME_RE,
        lambda message: _manifest_name_issue(
            issues, project, project.primary_manifest_path, message
        ),
    ):
        return None
    return raw_name


def _extract_gemspec_name(content: str) -> str | None:
    """Extract safe literal Gem::Specification.name assignments."""
    variables: dict[str, str] = {}
    receiver: str | None = None
    block_depth = 0
    pre_block_depth = 0
    for raw_line in content.splitlines():
        line = _strip_ruby_comment(raw_line).strip()
        if not line:
            continue
        if receiver is None:
            found_receiver = _gemspec_block_receiver(line)
            if found_receiver is not None and pre_block_depth == 0:
                receiver = found_receiver
                block_depth = 1
                continue
            pre_block_depth = _record_pre_gemspec_line(
                line, variables, pre_block_depth
            )
            continue
        block_depth, name, is_closed = _record_gemspec_block_line(
            line, receiver, variables, block_depth
        )
        if is_closed:
            return None
        if name is not None:
            return name
    return None


def _record_pre_gemspec_line(
    line: str, variables: dict[str, str], pre_block_depth: int
) -> int:
    """Record supported pre-gemspec literals and return updated depth."""
    if _ruby_taints_variables(line):
        variables.clear()
    else:
        _record_ruby_literal_assignment(line, variables, pre_block_depth)
    return max(0, pre_block_depth + _ruby_depth_delta(line))


def _record_gemspec_block_line(
    line: str,
    receiver: str,
    variables: dict[str, str],
    block_depth: int,
) -> tuple[int, str | None, bool]:
    """Record supported gemspec-block literals and extract a name."""
    if _ruby_taints_variables(line):
        variables.clear()
    block_depth += _ruby_depth_delta(line)
    if block_depth <= 0:
        return block_depth, None, True
    if block_depth != 1:
        return block_depth, None, False
    if _record_ruby_literal_assignment(line, variables, 0):
        return block_depth, None, False
    return (
        block_depth,
        _gemspec_name_assignment(line, receiver, variables),
        False,
    )


def _gemspec_block_receiver(line: str) -> str | None:
    """Return the receiver variable from a Gem::Specification block."""
    match = re.match(
        r"(?:[A-Za-z_]\w*\s*=\s*)?"
        r"Gem::Specification\.new\s+do\s+\|([A-Za-z_]\w*)\|",
        line,
    )
    return match.group(1) if match else None


def _gemspec_name_assignment(
    line: str, receiver: str, variables: Mapping[str, str]
) -> str | None:
    """Return a supported Gem::Specification name assignment value."""
    direct_match = re.match(
        rf"{re.escape(receiver)}\.name\s*=\s*['\"]([^'\"]+)['\"]\s*$",
        line,
    )
    if direct_match:
        return direct_match.group(1)
    variable_match = re.match(
        rf"{re.escape(receiver)}\.name\s*=\s*([A-Za-z_]\w*)\s*$",
        line,
    )
    return variables.get(variable_match.group(1)) if variable_match else None


def _record_ruby_literal_assignment(
    line: str, variables: dict[str, str], depth: int
) -> bool:
    """Record a top-level or active gemspec-block literal assignment."""
    if depth != 0:
        return False
    assignment = _ruby_literal_assignment(line)
    if assignment is None:
        return False
    variables[assignment[0]] = assignment[1]
    return True


def _strip_ruby_comment(line: str) -> str:
    """Remove Ruby comments outside single- or double-quoted strings."""
    quote: str | None = None
    escaped = False
    for index, char in enumerate(line):
        if escaped:
            escaped = False
            continue
        if char == "\\":
            escaped = quote is not None
            continue
        if char in {"'", '"'}:
            quote = None if quote == char else char if quote is None else quote
            continue
        if char == "#" and quote is None:
            return line[:index]
    return line


def _ruby_literal_assignment(line: str) -> tuple[str, str] | None:
    """Parse a simple Ruby string literal assignment."""
    match = re.match(r"^\s*([A-Za-z_]\w*)\s*=\s*['\"]([^'\"]+)['\"]\s*$", line)
    return (match.group(1), match.group(2)) if match else None


def _ruby_depth_delta(line: str) -> int:
    """Return simple Ruby block-depth delta for unsupported scopes."""
    opens = len(re.findall(r"\bdo\b", line))
    opens += 1 if _ruby_scope_opens(line) else 0
    closes = 1 if re.fullmatch(r"end\b.*", line) else 0
    return opens - closes


def _ruby_scope_opens(line: str) -> bool:
    """Return whether a Ruby line opens unsupported control flow or scope."""
    return (
        re.match(
            r"^\s*(?:def|class|module|if|unless|case|while|until|for|begin)\b",
            line,
        )
        is not None
    )


def _ruby_taints_variables(line: str) -> bool:
    """Return whether a Ruby line enters unsupported dynamic execution."""
    return _ruby_scope_opens(line) or re.search(r"\bdo\b", line) is not None


def _validate_manifest_name(
    value: object,
    label: str,
    pattern: re.Pattern[str],
    add_issue: Callable[[str], None],
) -> bool:
    """Validate a manifest-owned package-registry name."""
    if value is None or value == "":
        add_issue(f"{label} is missing")
        return False
    if not isinstance(value, str):
        add_issue(f"{label} must be a string")
        return False
    if not pattern.fullmatch(value):
        add_issue(f"{label} has invalid syntax")
        return False
    return True


def _manifest_name_issue(
    issues: _IssueCollector,
    project: ProjectDescriptor,
    manifest_path: str,
    message: str,
) -> None:
    """Record manifest-owned package name validation failures."""
    issues.add(
        "DESC_STATIC_INVALID",
        manifest_path,
        message,
        project_id=project.project_id,
    )


def _manifest_read_issue(
    issues: _IssueCollector,
    project: ProjectDescriptor,
    manifest_path: str,
    exc: OSError
    | UnicodeDecodeError
    | json.JSONDecodeError
    | tomllib.TOMLDecodeError,
) -> None:
    """Record manifest read or parse failures as descriptor diagnostics."""
    if isinstance(exc, OSError):
        message = f"manifest could not be read: {exc.strerror or exc}"
    else:
        message = f"manifest could not be parsed: {exc}"
    issues.add(
        "DESC_STATIC_INVALID",
        manifest_path,
        message,
        project_id=project.project_id,
    )


def _mapping(
    value: object, path: str, issues: _IssueCollector, code: str
) -> Mapping[str, object] | None:
    """Validate an object is a mapping."""
    if not isinstance(value, Mapping):
        issues.add(code, path, "must be a mapping")
        return None
    return value


def _array(
    value: object,
    path: str,
    issues: _IssueCollector,
    *,
    code: str = "DESC_SCHEMA_INVALID",
) -> Sequence[object] | None:
    """Validate an object is an array."""
    if not isinstance(value, list):
        issues.add(code, path, "must be a sequence")
        return None
    return value


def _string(
    value: object,
    path: str,
    issues: _IssueCollector,
    *,
    code: str = "DESC_SCHEMA_INVALID",
) -> str:
    """Validate and return a non-empty string."""
    if not isinstance(value, str) or value == "":
        issues.add(code, path, "must be a non-empty string")
        return ""
    return value


def _required_string(
    obj: Mapping[str, object],
    key: str,
    path: str,
    issues: _IssueCollector,
    *,
    code: str = "DESC_SCHEMA_INVALID",
) -> str:
    """Validate and return a required non-empty string field."""
    return _string(obj.get(key), f"{path}.{key}", issues, code=code)


def _string_map(
    value: object, path: str, issues: _IssueCollector
) -> Mapping[str, str]:
    """Validate a mapping whose keys and values are strings."""
    obj = _mapping(value, path, issues, "DESC_SCHEMA_INVALID")
    result: dict[str, str] = {}
    if obj is None:
        return result
    for key, item in obj.items():
        if not isinstance(key, str) or key == "":
            issues.add(
                "DESC_SCHEMA_INVALID", path, "keys must be non-empty strings"
            )
            continue
        result[key] = _string(item, f"{path}.{key}", issues)
    return result


def _exact_keys(  # noqa: PLR0913
    obj: Mapping[str, object],
    required: set[str],
    path: str,
    issues: _IssueCollector,
    code: str,
    *,
    optional: set[str] | None = None,
) -> None:
    """Validate a closed mapping key set."""
    optional = optional or set()
    keys: set[str] = set()
    for key in obj:
        if not isinstance(key, str) or key == "":
            issues.add(code, path, "keys must be non-empty strings")
            continue
        keys.add(key)
    for key in sorted(required - keys):
        issues.add(code, f"{path}.{key}", "is required")
    for key in sorted(keys - required - optional):
        issues.add(code, f"{path}.{key}", "is not allowed")


def _enum(
    value: str,
    allowed: set[str],
    path: str,
    issues: _IssueCollector,
    *,
    code: str = "DESC_SCHEMA_INVALID",
) -> None:
    """Validate a string enum value."""
    if value and value not in allowed:
        issues.add(code, path, f"must be one of {sorted(allowed)}")


def _canonical_id(
    value: str,
    path: str,
    issues: _IssueCollector,
    *,
    code: str = "DESC_SCHEMA_INVALID",
) -> None:
    """Validate a current-scope canonical id."""
    if value and not _CANONICAL_ID_RE.fullmatch(value):
        issues.add(code, path, "must be a canonical lowercase hyphen id")


def _github_slug(
    value: object,
    path: str,
    issues: _IssueCollector,
    *,
    code: str = "CATALOG_SCHEMA_INVALID",
) -> None:
    """Validate a GitHub owner or repository slug."""
    text = _string(value, path, issues, code=code)
    if text and not _GITHUB_SLUG_RE.fullmatch(text):
        issues.add(code, path, "must be a GitHub slug")


def _npm_name(value: str, path: str, issues: _IssueCollector) -> None:
    """Validate a current-scope lowercase npm package name."""
    if value and not _NPM_NAME_RE.fullmatch(value):
        issues.add(
            "DESC_SCHEMA_INVALID", path, "must be a lowercase npm package name"
        )


def _duplicates(
    values: Iterable[object],
    path: str,
    issues: _IssueCollector,
    label: str,
    *,
    code: str = "DESC_STATIC_INVALID",
) -> None:
    """Record duplicate values in a collection."""
    counts: dict[object, int] = {}
    for value in values:
        counts[value] = counts.get(value, 0) + 1
    for value, count in counts.items():
        if count > 1:
            issues.add(code, path, f"duplicate {label}: {value!r}")
