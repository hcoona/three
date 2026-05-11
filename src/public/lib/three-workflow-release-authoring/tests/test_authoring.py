"""Tests for workflow-release authoring validation."""

from __future__ import annotations

import copy
import shutil
import subprocess
from pathlib import Path
from typing import Any

import pytest
import yaml
from three_workflow_release_authoring import (
    REQUIRED_DESCRIPTOR_ROOTS,
    AuthoringValidationError,
    diagnostics_document,
    validate_authoring,
    validate_authoring_documents,
    validate_project_descriptor_document,
    validate_target_catalog_document,
)
from three_workflow_release_authoring.authoring import (
    _load_yaml_file,
)

REPO_ROOT = Path(__file__).parents[5]
CATALOG = REPO_ROOT / "eng/release/target-instances.yml"
NBGV_DESCRIPTOR = (
    REPO_ROOT / "src/public/lib/hcoona-release-smoke-pypi/three.release.yml"
)
HJG_DESCRIPTOR = REPO_ROOT / "src/public/lib/Hjg.Pngcs/three.release.yml"
QIDIAN_DESCRIPTOR = (
    REPO_ROOT / "src/private/app/qidian-novel-downloader/three.release.yml"
)
FIXTURES = Path(__file__).parent / "fixtures"


def _tracked_files() -> set[str]:
    """Return git candidate paths for the test repository."""
    git = shutil.which("git") or "git"
    output = subprocess.check_output(  # noqa: S603
        [git, "ls-files", "--cached", "--others", "--exclude-standard", "-z"],
        cwd=REPO_ROOT,
    )
    return {path for path in output.decode("utf-8").split("\0") if path}


def _descriptor_documents() -> dict[str, dict[str, Any]]:
    """Load all first-delivery descriptor documents from the worktree."""
    return {
        f"{root}/three.release.yml": _load_yaml(
            REPO_ROOT / root / "three.release.yml"
        )
        for root in REQUIRED_DESCRIPTOR_ROOTS
    }


def _load_yaml(path: Path) -> dict[str, Any]:
    """Load one YAML file as a mapping."""
    document = yaml.safe_load(path.read_text(encoding="utf-8"))
    assert isinstance(document, dict)
    return document


def test_current_repository_authoring_is_valid() -> None:
    """Validate checked-in descriptors and shared catalog."""
    snapshot = validate_authoring(REPO_ROOT)
    assert sorted(snapshot.projects) == [
        "asciidoctor-latexmath",
        "circular-list",
        "hcoona-release-smoke",
        "hcoona-release-smoke-dotnet-executable",
        "hcoona-release-smoke-github-packages",
        "hcoona-release-smoke-github-release",
        "hcoona-release-smoke-inno",
        "hcoona-release-smoke-npm",
        "hcoona-release-smoke-npm-dual",
        "hcoona-release-smoke-nuget",
        "hcoona-release-smoke-pypi",
        "hcoona-release-smoke-rubygems",
        "hcoona-release-smoke-wxt",
        "hexo-renderer-asciidoc",
        "hjg-pngcs",
        "image-occlusion-editor",
        "markdown-hybrid-search-mcp",
        "memoization",
        "memoization-generators",
        "microsoft-extensions-logging-mstest",
        "microsoft-extensions-logging-xunit",
        "microsoft-extensions-options-dedup-change-extensions",
        "nbgv-python",
        "phi-failure-detector",
        "phi-failure-detector-console",
        "qidian-novel-downloader",
        "steam-account-history-to-csv",
        "vscode-copilot-telegram-hook",
        "webhdfs-extensions-file-providers",
    ]
    assert {
        project.release_root for project in snapshot.projects.values()
    } == REQUIRED_DESCRIPTOR_ROOTS
    assert snapshot.planner_authoring_inputs() == {
        "descriptor-api-version": "three.release/v1alpha1",
        "catalog-path": "eng/release/target-instances.yml",
    }


def test_dotnet_metadata_input_is_closed_and_authoring_derived() -> None:
    """Emit the validate-authoring to Windows metadata handoff."""
    snapshot = validate_authoring(REPO_ROOT)
    document = snapshot.dotnet_metadata_input("0" * 40)
    assert document["projects"] == {
        "hjg-pngcs": {
            "descriptor-path": "src/public/lib/Hjg.Pngcs/three.release.yml",
            "primary-manifest-path": (
                "src/public/lib/Hjg.Pngcs/Hjg.Pngcs.csproj"
            ),
            "requires-package-id": True,
        },
        "circular-list": {
            "descriptor-path": "src/public/lib/CircularList/three.release.yml",
            "primary-manifest-path": (
                "src/public/lib/CircularList/CircularList.csproj"
            ),
            "requires-package-id": True,
        },
        "hcoona-release-smoke-github-packages": {
            "descriptor-path": (
                "src/public/lib/hcoona-release-smoke-github-packages/"
                "three.release.yml"
            ),
            "primary-manifest-path": (
                "src/public/lib/hcoona-release-smoke-github-packages/"
                "hcoona-release-smoke-github-packages.csproj"
            ),
            "requires-package-id": True,
        },
        "hcoona-release-smoke-github-release": {
            "descriptor-path": (
                "src/public/lib/hcoona-release-smoke-github-release/"
                "three.release.yml"
            ),
            "primary-manifest-path": (
                "src/public/lib/hcoona-release-smoke-github-release/"
                "hcoona-release-smoke-github-release.csproj"
            ),
            "requires-package-id": True,
        },
        "hcoona-release-smoke-dotnet-executable": {
            "descriptor-path": (
                "src/public/lib/hcoona-release-smoke-dotnet-executable/"
                "three.release.yml"
            ),
            "primary-manifest-path": (
                "src/public/lib/hcoona-release-smoke-dotnet-executable/"
                "hcoona-release-smoke-dotnet-executable.csproj"
            ),
            "requires-package-id": False,
        },
        "hcoona-release-smoke-inno": {
            "descriptor-path": (
                "src/public/lib/hcoona-release-smoke-inno/three.release.yml"
            ),
            "primary-manifest-path": (
                "src/public/lib/hcoona-release-smoke-inno/"
                "hcoona-release-smoke-inno.csproj"
            ),
            "requires-package-id": False,
        },
        "hcoona-release-smoke-nuget": {
            "descriptor-path": (
                "src/public/lib/hcoona-release-smoke-nuget/three.release.yml"
            ),
            "primary-manifest-path": (
                "src/public/lib/hcoona-release-smoke-nuget/"
                "hcoona-release-smoke-nuget.csproj"
            ),
            "requires-package-id": True,
        },
        "image-occlusion-editor": {
            "descriptor-path": (
                "src/public/app/ImageOcclusionEditor/three.release.yml"
            ),
            "primary-manifest-path": (
                "src/public/app/ImageOcclusionEditor/"
                "ImageOcclusionEditorWinUI3/ImageOcclusionEditorWinUI3.csproj"
            ),
            "requires-package-id": False,
        },
        "memoization": {
            "descriptor-path": "src/public/lib/Memoization/three.release.yml",
            "primary-manifest-path": (
                "src/public/lib/Memoization/Memoization.csproj"
            ),
            "requires-package-id": True,
        },
        "memoization-generators": {
            "descriptor-path": (
                "src/public/lib/Memoization.Generators/three.release.yml"
            ),
            "primary-manifest-path": (
                "src/public/lib/Memoization.Generators/"
                "Memoization.Generators.csproj"
            ),
            "requires-package-id": False,
        },
        "microsoft-extensions-logging-mstest": {
            "descriptor-path": (
                "src/public/lib/MicrosoftExtensions.Logging.MSTest/"
                "three.release.yml"
            ),
            "primary-manifest-path": (
                "src/public/lib/MicrosoftExtensions.Logging.MSTest/"
                "MicrosoftExtensions.Logging.MSTest.csproj"
            ),
            "requires-package-id": True,
        },
        "microsoft-extensions-logging-xunit": {
            "descriptor-path": (
                "src/public/lib/MicrosoftExtensions.Logging.Xunit/"
                "three.release.yml"
            ),
            "primary-manifest-path": (
                "src/public/lib/MicrosoftExtensions.Logging.Xunit/"
                "MicrosoftExtensions.Logging.Xunit.csproj"
            ),
            "requires-package-id": True,
        },
        "microsoft-extensions-options-dedup-change-extensions": {
            "descriptor-path": (
                "src/public/lib/"
                "MicrosoftExtensions.Options.DedupChangeExtensions/"
                "three.release.yml"
            ),
            "primary-manifest-path": (
                "src/public/lib/"
                "MicrosoftExtensions.Options.DedupChangeExtensions/"
                "MicrosoftExtensions.Options.DedupChangeExtensions.csproj"
            ),
            "requires-package-id": True,
        },
        "phi-failure-detector": {
            "descriptor-path": (
                "src/public/lib/PhiFailureDetector/three.release.yml"
            ),
            "primary-manifest-path": (
                "src/public/lib/PhiFailureDetector/PhiFailureDetector.csproj"
            ),
            "requires-package-id": True,
        },
        "phi-failure-detector-console": {
            "descriptor-path": (
                "src/public/app/PhiFailureDetector.Console/three.release.yml"
            ),
            "primary-manifest-path": (
                "src/public/app/PhiFailureDetector.Console/"
                "PhiFailureDetector.ConsoleApp.csproj"
            ),
            "requires-package-id": False,
        },
        "qidian-novel-downloader": {
            "descriptor-path": (
                "src/private/app/qidian-novel-downloader/three.release.yml"
            ),
            "primary-manifest-path": (
                "src/private/app/qidian-novel-downloader/"
                "QidianNovelDownloader.csproj"
            ),
            "requires-package-id": False,
        },
        "vscode-copilot-telegram-hook": {
            "descriptor-path": (
                "src/private/app/vscode-copilot-telegram-hook/three.release.yml"
            ),
            "primary-manifest-path": (
                "src/private/app/vscode-copilot-telegram-hook/"
                "VSCodeCopilotTelegramHook.csproj"
            ),
            "requires-package-id": False,
        },
        "webhdfs-extensions-file-providers": {
            "descriptor-path": (
                "src/public/lib/WebHdfs.Extensions.FileProviders/"
                "three.release.yml"
            ),
            "primary-manifest-path": (
                "src/public/lib/WebHdfs.Extensions.FileProviders/"
                "WebHdfs.Extensions.FileProviders.csproj"
            ),
            "requires-package-id": True,
        },
    }


def test_descriptor_extra_fields_are_rejected() -> None:
    """Reject descriptor shape extensions at the authoring boundary."""
    document = _load_yaml(NBGV_DESCRIPTOR)
    document["unexpected"] = True
    with pytest.raises(AuthoringValidationError) as error:
        validate_project_descriptor_document(
            "src/public/lib/hcoona-release-smoke-pypi/three.release.yml",
            document,
            tracked_files=_tracked_files(),
        )
    assert error.value.issues[0].code == "DESC_SCHEMA_INVALID"


def test_required_descriptor_roots_must_all_be_present() -> None:
    """Reject missing frozen first-delivery descriptor roots."""
    descriptors = _descriptor_documents()
    del descriptors["src/public/lib/CircularList/three.release.yml"]
    with pytest.raises(AuthoringValidationError) as error:
        validate_authoring_documents(
            descriptors,
            _load_yaml(CATALOG),
            tracked_files=_tracked_files(),
            repo_root=REPO_ROOT,
        )
    assert any(
        issue.path == "src/public/lib/CircularList/three.release.yml"
        and issue.code == "DESC_STATIC_INVALID"
        for issue in error.value.issues
    )


def test_missing_profile_is_diagnostic_not_cross_validation_crash() -> None:
    """Reject partial profile schema without crashing frozen validation."""
    descriptors = _descriptor_documents()
    circular = descriptors["src/public/lib/CircularList/three.release.yml"]
    del circular["profiles"]["official"]
    with pytest.raises(AuthoringValidationError) as error:
        validate_authoring_documents(
            descriptors,
            _load_yaml(CATALOG),
            tracked_files=_tracked_files(),
            repo_root=REPO_ROOT,
        )
    assert any(
        issue.code == "DESC_SCHEMA_INVALID"
        and issue.path
        == "src/public/lib/CircularList/three.release.yml.profiles.official"
        for issue in error.value.issues
    )


def test_descriptor_diagnostics_derive_project_scope_from_root() -> None:
    """Render descriptor-root issues as project-scoped diagnostics."""
    descriptors = _descriptor_documents()
    del descriptors["src/public/lib/CircularList/three.release.yml"]
    with pytest.raises(AuthoringValidationError) as error:
        validate_authoring_documents(
            descriptors,
            _load_yaml(CATALOG),
            tracked_files=_tracked_files(),
            repo_root=REPO_ROOT,
        )
    diagnostics = diagnostics_document(error.value.issues)["diagnostics"]
    assert isinstance(diagnostics, list)
    circular = next(
        diagnostic
        for diagnostic in diagnostics
        if diagnostic["details"]["path"]
        == "src/public/lib/CircularList/three.release.yml"
    )
    assert circular["scope-kind"] == "project"
    assert circular["project-id"] == "circular-list"


def test_descriptor_root_project_identity_is_frozen() -> None:
    """Reject swapping project IDs between frozen descriptor roots."""
    descriptors = _descriptor_documents()
    circular = descriptors["src/public/lib/CircularList/three.release.yml"]
    memoization = descriptors["src/public/lib/Memoization/three.release.yml"]
    circular["project"]["id"] = "memoization"
    memoization["project"]["id"] = "circular-list"
    with pytest.raises(AuthoringValidationError) as error:
        validate_authoring_documents(
            descriptors,
            _load_yaml(CATALOG),
            tracked_files=_tracked_files(),
            repo_root=REPO_ROOT,
        )
    assert any(
        issue.path == "src/public/lib/CircularList/three.release.yml.project.id"
        and "frozen descriptor root identity" in issue.message
        for issue in error.value.issues
    )
    assert any(
        issue.path == "src/public/lib/Memoization/three.release.yml.project.id"
        and "frozen descriptor root identity" in issue.message
        for issue in error.value.issues
    )


def test_identity_drift_diagnostic_uses_expected_project_scope() -> None:
    """Report root-bound identity drift on the frozen expected project id."""
    descriptors = _descriptor_documents()
    circular = descriptors["src/public/lib/CircularList/three.release.yml"]
    circular["project"]["id"] = "memoization"
    with pytest.raises(AuthoringValidationError) as error:
        validate_authoring_documents(
            descriptors,
            _load_yaml(CATALOG),
            tracked_files=_tracked_files(),
            repo_root=REPO_ROOT,
        )
    diagnostics = diagnostics_document(error.value.issues)["diagnostics"]
    assert isinstance(diagnostics, list)
    circular_issue = next(
        diagnostic
        for diagnostic in diagnostics
        if diagnostic["details"]["path"]
        == "src/public/lib/CircularList/three.release.yml.project.id"
    )
    assert circular_issue["scope-kind"] == "project"
    assert circular_issue["project-id"] == "circular-list"


def test_duplicate_project_id_diagnostics_are_project_scoped() -> None:
    """Report duplicate project IDs on each offending descriptor project.id."""
    descriptors = _descriptor_documents()
    descriptors["src/public/lib/CircularList/three.release.yml"]["project"][
        "id"
    ] = "memoization"
    with pytest.raises(AuthoringValidationError) as error:
        validate_authoring_documents(
            descriptors,
            _load_yaml(CATALOG),
            tracked_files=_tracked_files(),
            repo_root=REPO_ROOT,
        )
    diagnostics = diagnostics_document(error.value.issues)["diagnostics"]
    assert isinstance(diagnostics, list)
    duplicate_diagnostics = [
        diagnostic
        for diagnostic in diagnostics
        if diagnostic["message"] == "duplicate project id: 'memoization'"
    ]
    assert {
        diagnostic["details"]["path"] for diagnostic in duplicate_diagnostics
    } == {
        "src/public/lib/CircularList/three.release.yml.project.id",
        "src/public/lib/Memoization/three.release.yml.project.id",
    }
    assert all(
        diagnostic["scope-kind"] == "project"
        and isinstance(diagnostic.get("project-id"), str)
        and diagnostic["details"]["path"].endswith(".project.id")
        for diagnostic in duplicate_diagnostics
    )


def test_descriptor_root_primary_manifest_identity_is_frozen() -> None:
    """Reject primary manifest drift from the frozen root binding."""
    descriptors = _descriptor_documents()
    circular = descriptors["src/public/lib/CircularList/three.release.yml"]
    circular["source"]["primary-manifest"] = "README.md"
    with pytest.raises(AuthoringValidationError) as error:
        validate_authoring_documents(
            descriptors,
            _load_yaml(CATALOG),
            tracked_files=_tracked_files(),
            repo_root=REPO_ROOT,
        )
    assert any(
        issue.path
        == (
            "src/public/lib/CircularList/"
            "three.release.yml.source.primary-manifest"
        )
        and "frozen descriptor root identity" in issue.message
        for issue in error.value.issues
    )


def test_catalog_capabilities_must_match_family_and_host() -> None:
    """Reject target catalog topology drift."""
    document = _load_yaml(CATALOG)
    mutated = copy.deepcopy(document)
    npmjs = mutated["families"]["npm"]["instances"][0]
    npmjs["capabilities"]["publish-topology"] = "external-oidc-caller-workflow"
    with pytest.raises(AuthoringValidationError) as error:
        validate_target_catalog_document(mutated)
    assert any(
        issue.code == "CATALOG_SCHEMA_INVALID" for issue in error.value.issues
    )


@pytest.mark.parametrize(
    "mutate",
    [
        lambda catalog: catalog["families"]["npm"].__setitem__(
            "instances", "not-a-list"
        ),
        lambda catalog: catalog["families"]["npm"]["instances"][0].__setitem__(
            "id", "NpmJs"
        ),
        lambda catalog: catalog["families"]["npm"]["instances"][0].__setitem__(
            "contract", 123
        ),
        lambda catalog: catalog["families"]["npm"]["instances"][0][
            "capabilities"
        ].__setitem__("publish-topology", 123),
    ],
)
def test_catalog_schema_failures_use_catalog_diagnostic_codes(mutate) -> None:
    """Report catalog schema failures as catalog diagnostics."""
    catalog = _load_yaml(CATALOG)
    mutate(catalog)
    with pytest.raises(AuthoringValidationError) as error:
        validate_target_catalog_document(catalog)
    assert error.value.issues
    assert any(
        issue.code == "CATALOG_SCHEMA_INVALID" for issue in error.value.issues
    )


@pytest.mark.parametrize(
    ("fixture", "code"),
    [
        ("invalid-yaml.yml", "DESC_SCHEMA_INVALID"),
        ("invalid-utf8.yml", "CATALOG_SCHEMA_INVALID"),
    ],
)
def test_yaml_load_failures_are_authoring_diagnostics(
    fixture: str, code: str
) -> None:
    """Convert YAML read and parse failures into schema diagnostics."""
    with pytest.raises(AuthoringValidationError) as error:
        _load_yaml_file(FIXTURES / fixture, code, fixture)
    assert error.value.issues
    assert {issue.code for issue in error.value.issues} == {code}


def test_catalog_mapping_keys_must_be_strings_without_crashing() -> None:
    """Reject heterogeneous catalog mapping keys without sorting crashes."""
    catalog = _load_yaml(CATALOG)
    catalog["families"][1] = {"instances": []}
    with pytest.raises(AuthoringValidationError) as error:
        validate_target_catalog_document(catalog)
    assert any(
        issue.code == "CATALOG_SCHEMA_INVALID"
        and "keys must be non-empty strings" in issue.message
        for issue in error.value.issues
    )


def test_malformed_referenced_catalog_contract_is_diagnostic_not_crash() -> (
    None
):
    """Reject bad catalog contracts before project cross-validation."""
    descriptors = _descriptor_documents()
    catalog = _load_yaml(CATALOG)
    catalog["families"]["npm"]["instances"][0]["contract"] = "bad-contract"
    with pytest.raises(AuthoringValidationError) as error:
        validate_authoring_documents(
            descriptors,
            catalog,
            tracked_files=_tracked_files(),
            repo_root=REPO_ROOT,
        )
    assert error.value.issues
    assert any(
        issue.code == "CATALOG_SCHEMA_INVALID" for issue in error.value.issues
    )


def test_duplicate_variant_dimensions_are_rejected() -> None:
    """Reject semantically duplicate variants in one descriptor."""
    document = _load_yaml(HJG_DESCRIPTOR)
    duplicate = copy.deepcopy(document["variants"][0])
    duplicate["id"] = "package-copy"
    document["variants"].append(duplicate)
    with pytest.raises(AuthoringValidationError) as error:
        validate_project_descriptor_document(
            "src/public/lib/Hjg.Pngcs/three.release.yml",
            document,
            tracked_files=_tracked_files(),
        )
    assert any(
        "variant dimensions" in issue.message for issue in error.value.issues
    )


def test_missing_required_package_tuple_is_diagnostic_not_crash() -> None:
    """Reject incomplete package target aggregate rules without KeyError."""
    descriptors = _descriptor_documents()
    nbgv = descriptors[
        "src/public/lib/hcoona-release-smoke-pypi/three.release.yml"
    ]
    nbgv["profiles"]["official"]["targets"][1]["artifacts"] = ["sdist"]
    with pytest.raises(AuthoringValidationError) as error:
        validate_authoring_documents(
            descriptors,
            _load_yaml(CATALOG),
            tracked_files=_tracked_files(),
            repo_root=REPO_ROOT,
        )
    assert any(
        issue.code == "DESC_STATIC_INVALID"
        and "required artifact tuple count" in issue.message
        for issue in error.value.issues
    )


@pytest.mark.parametrize(
    "companion_path",
    ["*.dbg", "playwright.ps1", "playwright.sh"],
)
def test_descriptor_accepts_root_level_companion_paths(
    companion_path: str,
) -> None:
    """Accept root-level executable companion paths and globs."""
    document = _load_yaml(QIDIAN_DESCRIPTOR)
    companion = document["variants"][1]["artifacts"][0]["companions"][0]
    companion["path"] = companion_path
    validate_project_descriptor_document(
        "src/private/app/qidian-novel-downloader/three.release.yml",
        document,
        tracked_files=_tracked_files(),
    )


@pytest.mark.parametrize("companion_path", ["C:secret", ".", "..", "**"])
def test_descriptor_rejects_unsafe_companion_paths(
    companion_path: str,
) -> None:
    """Reject companion paths that are not safe root-level output matches."""
    document = _load_yaml(QIDIAN_DESCRIPTOR)
    companion = document["variants"][1]["artifacts"][0]["companions"][0]
    companion["path"] = companion_path
    with pytest.raises(AuthoringValidationError) as error:
        validate_project_descriptor_document(
            "src/private/app/qidian-novel-downloader/three.release.yml",
            document,
            tracked_files=_tracked_files(),
        )
    assert any(
        issue.code == "DESC_SCHEMA_INVALID"
        and issue.path.endswith(".companions[0].path")
        for issue in error.value.issues
    )


def test_npm_manifest_fallback_package_name_is_validated() -> None:
    """Validate package.json fallback names for npm GitHub Packages scope."""
    descriptors = _descriptor_documents()
    hexo = descriptors[
        "src/public/lib/hcoona-release-smoke-npm/three.release.yml"
    ]
    hexo["profiles"]["official"]["targets"][1]["uses"] = "npm/github-packages"
    hexo["profiles"]["official"]["targets"][1]["projection"] = {
        "package-name": "hcoona-release-smoke-npm"
    }
    with pytest.raises(AuthoringValidationError) as error:
        validate_authoring_documents(
            descriptors,
            _load_yaml(CATALOG),
            tracked_files=_tracked_files(),
            repo_root=REPO_ROOT,
        )
    assert any(
        issue.code == "DESC_STATIC_INVALID"
        and "scope must match" in issue.message
        for issue in error.value.issues
    )


def test_artifact_level_npm_projection_allows_distinct_duplicate_tuples() -> (
    None
):
    """Allow one variant to produce separate npm tarballs by package name."""
    snapshot = validate_authoring(REPO_ROOT)
    project = snapshot.projects["hcoona-release-smoke-npm-dual"]
    artifacts = project.variants[0].artifacts

    assert [artifact.id for artifact in artifacts] == [
        "npm-package",
        "npm-package-github",
    ]
    assert [artifact.projection["package-name"] for artifact in artifacts] == [
        "hcoona-release-smoke-npm-dual",
        "@hcoona/hcoona-release-smoke-npm-dual",
    ]


def test_artifact_level_npm_projection_validates_github_scope() -> None:
    """Validate artifact-level package projection against GitHub Packages."""
    descriptors = _descriptor_documents()
    smoke = descriptors[
        "src/public/lib/hcoona-release-smoke-npm-dual/three.release.yml"
    ]
    smoke["variants"][0]["artifacts"][1]["projection"]["package-name"] = (
        "@wrong/hcoona-release-smoke-npm-dual"
    )

    with pytest.raises(AuthoringValidationError) as error:
        validate_authoring_documents(
            descriptors,
            _load_yaml(CATALOG),
            tracked_files=_tracked_files(),
            repo_root=REPO_ROOT,
        )

    assert any(
        issue.code == "DESC_STATIC_INVALID"
        and "scope must match" in issue.message
        for issue in error.value.issues
    )


@pytest.mark.parametrize(
    ("manifest_path", "replacement"),
    [
        ("src/public/lib/hcoona-release-smoke-npm/package.json", "{"),
        ("src/public/lib/hcoona-release-smoke-pypi/pyproject.toml", "[project"),
        (
            "src/public/lib/hcoona-release-smoke-rubygems/"
            "hcoona-release-smoke-rubygems.gemspec",
            OSError("blocked"),
        ),
    ],
)
def test_manifest_resolution_failures_are_diagnostics(
    monkeypatch: pytest.MonkeyPatch,
    manifest_path: str,
    replacement: str | OSError,
) -> None:
    """Convert manifest read and parse failures into descriptor diagnostics."""
    original_read_text = Path.read_text

    def fake_read_text(path: Path, *args: Any, **kwargs: Any) -> str:
        if path == REPO_ROOT / manifest_path:
            if isinstance(replacement, OSError):
                raise replacement
            return replacement
        return original_read_text(path, *args, **kwargs)

    monkeypatch.setattr(Path, "read_text", fake_read_text)
    with pytest.raises(AuthoringValidationError) as error:
        validate_authoring_documents(
            _descriptor_documents(),
            _load_yaml(CATALOG),
            tracked_files=_tracked_files(),
            repo_root=REPO_ROOT,
        )
    assert any(
        issue.code == "DESC_STATIC_INVALID"
        and issue.path == manifest_path
        and issue.project_id is not None
        and "manifest could not be" in issue.message
        for issue in error.value.issues
    )


@pytest.mark.parametrize(
    ("manifest_path", "replacement", "message"),
    [
        (
            "src/public/lib/hcoona-release-smoke-pypi/pyproject.toml",
            '[project]\nversion = "0"\n',
            "PyPI package name is missing",
        ),
        (
            "src/public/lib/hcoona-release-smoke-pypi/pyproject.toml",
            '[project]\nname = 123\nversion = "0"\n',
            "PyPI package name must be a string",
        ),
        (
            "src/public/lib/hcoona-release-smoke-pypi/pyproject.toml",
            '[project]\nname = "bad name!"\nversion = "0"\n',
            "PyPI package name has invalid syntax",
        ),
        (
            "src/public/lib/hcoona-release-smoke-npm/package.json",
            '{"name": 123}',
            "npm package name must be a string",
        ),
        (
            "src/public/lib/hcoona-release-smoke-npm/package.json",
            '{"name": "Bad Name!"}',
            "npm package name has invalid syntax",
        ),
        (
            "src/public/lib/hcoona-release-smoke-rubygems/"
            "hcoona-release-smoke-rubygems.gemspec",
            'Gem::Specification.new do |spec|\n  spec.version = "0"\nend\n',
            "RubyGems package name is missing",
        ),
        (
            "src/public/lib/hcoona-release-smoke-rubygems/"
            "hcoona-release-smoke-rubygems.gemspec",
            (
                '# spec.name = "asciidoctor-latexmath"\n'
                'Gem::Specification.new do |spec|\n  spec.version = "0"\nend\n'
            ),
            "RubyGems package name is missing",
        ),
        (
            "src/public/lib/hcoona-release-smoke-rubygems/"
            "hcoona-release-smoke-rubygems.gemspec",
            (
                'other.name = "asciidoctor-latexmath"\n'
                'Gem::Specification.new do |spec|\n  spec.version = "0"\nend\n'
            ),
            "RubyGems package name is missing",
        ),
        (
            "src/public/lib/hcoona-release-smoke-rubygems/"
            "hcoona-release-smoke-rubygems.gemspec",
            (
                'lambda do\n  gem_name = "asciidoctor-latexmath"\nend\n'
                "Gem::Specification.new do |spec|\n"
                "  spec.name = gem_name\nend\n"
            ),
            "RubyGems package name is missing",
        ),
        (
            "src/public/lib/hcoona-release-smoke-rubygems/"
            "hcoona-release-smoke-rubygems.gemspec",
            (
                'def helper\n  gem_name = "asciidoctor-latexmath"\nend\n'
                "Gem::Specification.new do |spec|\n"
                "  spec.name = gem_name\nend\n"
            ),
            "RubyGems package name is missing",
        ),
        (
            "src/public/lib/hcoona-release-smoke-rubygems/"
            "hcoona-release-smoke-rubygems.gemspec",
            (
                'class GemName\n  gem_name = "asciidoctor-latexmath"\nend\n'
                "Gem::Specification.new do |spec|\n"
                "  spec.name = gem_name\nend\n"
            ),
            "RubyGems package name is missing",
        ),
        (
            "src/public/lib/hcoona-release-smoke-rubygems/"
            "hcoona-release-smoke-rubygems.gemspec",
            (
                'if true\n  gem_name = "asciidoctor-latexmath"\nend\n'
                "Gem::Specification.new do |spec|\n"
                "  spec.name = gem_name\nend\n"
            ),
            "RubyGems package name is missing",
        ),
        (
            "src/public/lib/hcoona-release-smoke-rubygems/"
            "hcoona-release-smoke-rubygems.gemspec",
            (
                "Gem::Specification.new do |spec|\n"
                '  if true\n    spec.name = "asciidoctor-latexmath"\n'
                "  end\nend\n"
            ),
            "RubyGems package name is missing",
        ),
        (
            "src/public/lib/hcoona-release-smoke-rubygems/"
            "hcoona-release-smoke-rubygems.gemspec",
            (
                'gem_name = "asciidoctor-latexmath"\n'
                'if ENV["BAD"]\n  gem_name = "BadGem"\nend\n'
                "Gem::Specification.new do |spec|\n"
                "  spec.name = gem_name\nend\n"
            ),
            "RubyGems package name is missing",
        ),
        (
            "src/public/lib/hcoona-release-smoke-rubygems/"
            "hcoona-release-smoke-rubygems.gemspec",
            (
                "Gem::Specification.new do |spec|\n"
                '  gem_name = "asciidoctor-latexmath"\n'
                '  if ENV["BAD"]\n    gem_name = "BadGem"\n  end\n'
                "  spec.name = gem_name\nend\n"
            ),
            "RubyGems package name is missing",
        ),
        (
            "src/public/lib/hcoona-release-smoke-rubygems/"
            "hcoona-release-smoke-rubygems.gemspec",
            (
                'gem_name = "asciidoctor-latexmath"\n'
                "Gem::Specification.new do |spec|\n"
                "  spec.name = gem_name.upcase\nend\n"
            ),
            "RubyGems package name is missing",
        ),
        (
            "src/public/lib/hcoona-release-smoke-rubygems/"
            "hcoona-release-smoke-rubygems.gemspec",
            (
                "Gem::Specification.new do |spec|\n"
                "  spec.name = gem_name\nend\n"
                'gem_name = "asciidoctor-latexmath"\n'
            ),
            "RubyGems package name is missing",
        ),
        (
            "src/public/lib/hcoona-release-smoke-rubygems/"
            "hcoona-release-smoke-rubygems.gemspec",
            ('Gem::Specification.new do |spec|\n  spec.name = "BadGem"\nend\n'),
            "RubyGems package name has invalid syntax",
        ),
    ],
)
def test_manifest_package_names_are_validated(
    monkeypatch: pytest.MonkeyPatch,
    manifest_path: str,
    replacement: str,
    message: str,
) -> None:
    """Reject missing and malformed static package-registry manifest names."""
    original_read_text = Path.read_text

    def fake_read_text(path: Path, *args: Any, **kwargs: Any) -> str:
        if path == REPO_ROOT / manifest_path:
            return replacement
        return original_read_text(path, *args, **kwargs)

    monkeypatch.setattr(Path, "read_text", fake_read_text)
    with pytest.raises(AuthoringValidationError) as error:
        validate_authoring_documents(
            _descriptor_documents(),
            _load_yaml(CATALOG),
            tracked_files=_tracked_files(),
            repo_root=REPO_ROOT,
        )
    assert any(
        issue.code == "DESC_STATIC_INVALID"
        and issue.path == manifest_path
        and issue.message == message
        and issue.project_id is not None
        for issue in error.value.issues
    )


def test_missing_rubygems_manifest_is_diagnostic(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Reject missing gemspec during RubyGems package identity resolution."""
    manifest_path = (
        "src/public/lib/hcoona-release-smoke-rubygems/"
        "hcoona-release-smoke-rubygems.gemspec"
    )
    manifest = REPO_ROOT / manifest_path
    original_exists = Path.exists
    original_read_text = Path.read_text

    def fake_exists(path: Path) -> bool:
        if path == manifest:
            return False
        return original_exists(path)

    def fake_read_text(path: Path, *args: Any, **kwargs: Any) -> str:
        if path == manifest:
            raise FileNotFoundError
        return original_read_text(path, *args, **kwargs)

    monkeypatch.setattr(Path, "exists", fake_exists)
    monkeypatch.setattr(Path, "read_text", fake_read_text)
    with pytest.raises(AuthoringValidationError) as error:
        validate_authoring_documents(
            _descriptor_documents(),
            _load_yaml(CATALOG),
            tracked_files=_tracked_files(),
            repo_root=REPO_ROOT,
        )
    assert any(
        issue.code == "DESC_STATIC_INVALID"
        and issue.path == manifest_path
        and issue.project_id == "hcoona-release-smoke-rubygems"
        and "manifest could not be read" in issue.message
        for issue in error.value.issues
    )


def test_frozen_profile_target_baseline_is_enforced() -> None:
    """Reject target instances outside the frozen per-project baseline."""
    descriptors = _descriptor_documents()
    github_release = descriptors[
        "src/public/lib/hcoona-release-smoke-github-release/three.release.yml"
    ]
    github_release["profiles"]["official"]["targets"].append(
        {"uses": "nuget/nuget-org", "artifacts": ["nuget"]}
    )
    with pytest.raises(AuthoringValidationError) as error:
        validate_authoring_documents(
            descriptors,
            _load_yaml(CATALOG),
            tracked_files=_tracked_files(),
            repo_root=REPO_ROOT,
        )
    assert any(
        issue.code == "DESC_STATIC_INVALID"
        and "frozen first-delivery baseline" in issue.message
        for issue in error.value.issues
    )


def test_frozen_target_artifact_baseline_requires_symbol_package() -> None:
    """Reject GitHub Release package target missing its frozen snupkg member."""
    descriptors = _descriptor_documents()
    circular = descriptors["src/public/lib/CircularList/three.release.yml"]
    circular["profiles"]["official"]["targets"][0]["artifacts"] = ["nuget"]
    with pytest.raises(AuthoringValidationError) as error:
        validate_authoring_documents(
            descriptors,
            _load_yaml(CATALOG),
            tracked_files=_tracked_files(),
            repo_root=REPO_ROOT,
        )
    assert any(
        issue.code == "DESC_STATIC_INVALID"
        and "target artifact semantics" in issue.message
        for issue in error.value.issues
    )


def test_frozen_target_artifact_baseline_requires_registry_symbols() -> None:
    """Reject registry NuGet targets missing their snupkg member."""
    descriptors = _descriptor_documents()
    github_packages = descriptors[
        "src/public/lib/hcoona-release-smoke-github-packages/three.release.yml"
    ]
    github_packages["profiles"]["official"]["targets"][1]["artifacts"] = [
        "nuget"
    ]
    with pytest.raises(AuthoringValidationError) as error:
        validate_authoring_documents(
            descriptors,
            _load_yaml(CATALOG),
            tracked_files=_tracked_files(),
            repo_root=REPO_ROOT,
        )
    assert any(
        issue.code == "DESC_STATIC_INVALID"
        and "target artifact semantics" in issue.message
        for issue in error.value.issues
    )


def test_frozen_target_artifact_baseline_requires_variant_dimensions() -> None:
    """Reject target artifact semantic drift caused by variant dimensions."""
    descriptors = _descriptor_documents()
    qidian = descriptors[
        "src/private/app/qidian-novel-downloader/three.release.yml"
    ]
    qidian["variants"][0]["dimensions"] = {
        "os": "freebsd",
        "rid": "freebsd-x64",
    }
    with pytest.raises(AuthoringValidationError) as error:
        validate_authoring_documents(
            descriptors,
            _load_yaml(CATALOG),
            tracked_files=_tracked_files(),
            repo_root=REPO_ROOT,
        )
    assert any(
        issue.code == "DESC_STATIC_INVALID"
        and "target artifact semantics" in issue.message
        for issue in error.value.issues
    )


def test_empty_projection_is_rejected_when_projection_must_be_absent() -> None:
    """Reject empty projection mappings for absent-projection families."""
    descriptors = _descriptor_documents()
    nbgv = descriptors[
        "src/public/lib/hcoona-release-smoke-pypi/three.release.yml"
    ]
    nbgv["profiles"]["official"]["targets"][1]["projection"] = {}
    with pytest.raises(AuthoringValidationError) as error:
        validate_authoring_documents(
            descriptors,
            _load_yaml(CATALOG),
            tracked_files=_tracked_files(),
            repo_root=REPO_ROOT,
        )
    assert any(
        issue.code == "DESC_SCHEMA_INVALID"
        and "projection is not allowed" in issue.message
        for issue in error.value.issues
    )


def test_static_coexistence_conflict_uses_descriptor_diagnostic() -> None:
    """Report static coexistence failures as descriptor static validation."""
    descriptors = _descriptor_documents()
    nbgv = descriptors[
        "src/public/lib/hcoona-release-smoke-pypi/three.release.yml"
    ]
    nbgv["profiles"]["buddy"]["targets"].append(
        {"uses": "pypi/pypi", "artifacts": ["wheel", "sdist"]}
    )
    with pytest.raises(AuthoringValidationError) as error:
        validate_authoring_documents(
            descriptors,
            _load_yaml(CATALOG),
            tracked_files=_tracked_files(),
            repo_root=REPO_ROOT,
        )
    codes = {issue.code for issue in error.value.issues}
    assert "DESC_STATIC_INVALID" in codes
    assert "PUBLISH_IDENTITY_CONFLICT" not in codes


@pytest.mark.parametrize("bad_path", ["./pyproject.toml", "pyproject.toml/"])
def test_raw_path_segments_are_rejected(bad_path: str) -> None:
    """Reject raw path forms that pathlib would otherwise normalize."""
    document = _load_yaml(NBGV_DESCRIPTOR)
    document["source"]["primary-manifest"] = bad_path
    with pytest.raises(AuthoringValidationError) as error:
        validate_project_descriptor_document(
            "src/public/lib/hcoona-release-smoke-pypi/three.release.yml",
            document,
            tracked_files=_tracked_files(),
        )
    assert any(
        issue.code == "DESC_SCHEMA_INVALID" for issue in error.value.issues
    )


def test_buddy_pypi_target_is_rejected() -> None:
    """Reject current-scope Python buddy publication to PyPI."""
    catalog = _load_yaml(CATALOG)
    descriptor = _load_yaml(NBGV_DESCRIPTOR)
    descriptor["profiles"]["buddy"]["targets"].append(
        {"uses": "pypi/pypi", "artifacts": ["wheel", "sdist"]}
    )
    with pytest.raises(AuthoringValidationError) as error:
        validate_authoring_documents(
            {
                (
                    "src/public/lib/hcoona-release-smoke-pypi/three.release.yml"
                ): descriptor,
            },
            catalog,
            tracked_files=_tracked_files(),
        )
    assert any(
        issue.code == "DESC_STATIC_INVALID" for issue in error.value.issues
    )


def test_authoring_issues_render_as_planner_diagnostics() -> None:
    """Expose authoring failures through the frozen diagnostics contract."""
    catalog = _load_yaml(CATALOG)
    catalog["families"]["pypi"]["instances"][0]["contract"] = "npm-publish"
    with pytest.raises(AuthoringValidationError) as error:
        validate_target_catalog_document(catalog)
    diagnostics = diagnostics_document(error.value.issues)
    assert diagnostics["kind"] == "planner-diagnostics"
    assert diagnostics["diagnostics"]
