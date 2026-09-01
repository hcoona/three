"""Current static-reference changed-path admission contracts."""

from __future__ import annotations

import pytest
from three_workflow_delivery_v3.ci.path_admission import (
    CI_STATIC_REFERENCE_BASENAMES,
    is_repository_only_path,
    is_static_reference_control_path,
    is_static_reference_surface_path,
)


@pytest.mark.parametrize(
    "path",
    [
        "package.json",
        "nested/package.json",
        "packages.config",
        "nested/packages.lock.json",
        "pnpm-lock.yaml",
        "nested/pnpm-workspace.yaml",
    ],
)
def test_static_reference_basename_is_repository_only(path: str) -> None:
    """Select every retained static-reference basename at any depth."""
    assert is_static_reference_surface_path(path)
    assert is_repository_only_path(path)


@pytest.mark.parametrize(
    "path",
    [
        "package-lock.json",
        "uv.lock",
        "yarn.lock",
        "Directory.Packages.props",
        "project.csproj",
        "requirements.txt",
        "script.ps1",
    ],
)
def test_superseded_consumer_surfaces_are_not_static_reference_inputs(
    path: str,
) -> None:
    """Do not preserve the broad consumer-policy path catalog."""
    assert not is_static_reference_surface_path(path)


@pytest.mark.parametrize(
    "path",
    [
        "nested/package-lock.json",
        "src/private/app/tool/pyproject.toml",
        "src/lab/TaskAssigner/TaskAssigner.csproj",
        "nested/requirements-dev.txt",
        "tools/bootstrap.ps1",
        "nested/.npmrc",
        "nested/.gitattributes",
        ".github/workflows/release/package-lock.json",
        ".github/workflows/release/tool.csproj",
        ".github/workflows/release/bootstrap.ps1",
        ".github/workflows/release/.npmrc",
    ],
)
def test_non_scanned_dependency_surfaces_remain_repository_only(
    path: str,
) -> None:
    """Preserve CI admission independently from scanner selection."""
    assert is_repository_only_path(path)
    assert not is_static_reference_control_path(path)
    assert not is_static_reference_surface_path(path)


def test_static_reference_basename_catalog_is_exact() -> None:
    """Keep CI admission aligned with the bounded scanner families."""
    assert {
        "package.json",
        "packages.config",
        "packages.lock.json",
        "pnpm-lock.yaml",
        "pnpm-workspace.yaml",
    } == CI_STATIC_REFERENCE_BASENAMES


def test_static_reference_authority_sources_are_control_not_scan_surfaces() -> (
    None
):
    """Route the exact authority implementation through full v3 control CI."""
    for path in (
        "Directory.Packages.props",
        ("src/private/app/workflow-delivery-v3-nuget-authority/Program.cs"),
        (
            "src/private/app/workflow-delivery-v3-nuget-authority/"
            "WorkflowDeliveryV3NuGetAuthority.csproj"
        ),
    ):
        assert is_static_reference_control_path(path)
        assert not is_static_reference_surface_path(path)


def test_hexo_migration_documents_are_repository_only() -> None:
    """Classify local-file guidance without inventing Hexo project scope."""
    for path in (
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
    ):
        assert is_repository_only_path(path)
        assert not is_static_reference_control_path(path)
        assert not is_static_reference_surface_path(path)


def test_workflow_documents_remain_repository_only_not_scanner_inputs() -> None:
    """Route workflow changes through root conformance without scanning YAML."""
    for path in (
        ".github/workflows/workflow-delivery-v3-ci.yml",
        ".github/workflows/workflow-delivery-v3-buddy-smoke.yml",
    ):
        assert is_repository_only_path(path)
        assert not is_static_reference_surface_path(path)


def test_root_gitattributes_is_repository_only_not_a_policy_surface() -> None:
    """Run root conformance for attributes without scanning their content."""
    assert is_repository_only_path(".gitattributes")
    assert not is_static_reference_surface_path(".gitattributes")
    assert is_repository_only_path("nested/.gitattributes")
    assert not is_static_reference_surface_path("nested/.gitattributes")


def test_workflow_descendants_use_the_exact_static_selector_semantics() -> None:
    """Preserve NuGet selection while reserving workflow pnpm basenames."""
    nuget_lock = ".github/workflows/release/nested/packages.lock.json"
    pnpm_lock = ".github/workflows/release/nested/pnpm-lock.yaml"
    pnpm_workspace = ".github/workflows/release/nested/pnpm-workspace.yaml"

    assert is_static_reference_surface_path(nuget_lock)
    assert is_repository_only_path(nuget_lock)
    for path in (pnpm_lock, pnpm_workspace):
        assert not is_static_reference_surface_path(path)
        assert is_repository_only_path(path)


def test_recursive_composite_action_pattern_matches_every_depth() -> None:
    """Retain repository admission without scanning composite actions."""
    for path in (
        ".github/actions/direct/action.yml",
        ".github/actions/team/direct/action.yml",
        ".github/actions/org/team/direct/action.yml",
    ):
        assert is_repository_only_path(path)
        assert not is_static_reference_surface_path(path)


def test_workflow_documentation_is_repository_only() -> None:
    """Retain workflow-document locality without scanning workflow content."""
    documentation = ".github/workflows/docs/DESIGN.md"
    helper = ".github/workflows/helper.py"

    assert is_repository_only_path(documentation)
    assert not is_static_reference_surface_path(documentation)
    assert not is_repository_only_path(helper)
    assert not is_static_reference_surface_path(helper)

    for workflow in (
        ".github/workflows/consumer.yml",
        ".github/workflows/consumer.yaml",
    ):
        assert is_repository_only_path(workflow)
        assert not is_static_reference_surface_path(workflow)
