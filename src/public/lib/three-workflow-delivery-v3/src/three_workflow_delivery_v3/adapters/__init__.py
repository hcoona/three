"""Unprivileged build and quality Adapters."""

from __future__ import annotations

from importlib import import_module

from three_workflow_delivery_v3.adapters.node import (
    ArtifactExpectation,
    ArtifactManifest,
    BuildRequest,
    BuildResult,
    InstallImportResult,
    PackageTargetWitness,
    RuntimeRequest,
    build_node_package,
    qualify_npm_artifact_contents,
    qualify_npm_install_import,
    run_node_project_build,
    run_node_project_tests,
)
from three_workflow_delivery_v3.adapters.npmjs import (
    HttpResponse,
    HttpTransport,
    NpmjsNetworkError,
    NpmjsPolicyError,
    NpmjsTimeoutError,
    NpmjsTruncatedResponseError,
    StdlibHttpTransport,
    observe_npmjs_projection,
)

_GITHUB_PACKAGES_EXPORTS = frozenset(
    {
        "GITHUB_PACKAGES_DESTINATION_ID",
        "GITHUB_PACKAGES_OBSERVATION_CONTRACT_ID",
        "GITHUB_PACKAGES_OPERATION",
        "GITHUB_PACKAGES_PACKAGE",
        "GITHUB_PACKAGES_REGISTRY",
        "GitHubPackagesHttpResponse",
        "GitHubPackagesPublishPreflight",
        "GitHubPackagesNetworkError",
        "GitHubPackagesPolicyError",
        "GitHubPackagesTimeoutError",
        "GitHubPackagesTransport",
        "MutationMayHaveStartedMarker",
        "PublicationExecutionResult",
        "PublishCommandResult",
        "PublishRunner",
        "classify_github_packages_probe",
        "classify_publish_result",
        "form_mutation_may_have_started_marker",
        "observe_github_packages_projection",
        "preflight_github_packages_action",
        "publish_github_packages_action",
    }
)


def __getattr__(name: str) -> object:
    """Load the live Adapter lazily to avoid the qualification import cycle."""
    if name not in _GITHUB_PACKAGES_EXPORTS:
        raise AttributeError(name)

    github_packages = import_module(
        "three_workflow_delivery_v3.adapters.github_packages"
    )
    value = getattr(github_packages, name)
    globals()[name] = value
    return value


__all__ = [
    "GITHUB_PACKAGES_DESTINATION_ID",
    "GITHUB_PACKAGES_OBSERVATION_CONTRACT_ID",
    "GITHUB_PACKAGES_OPERATION",
    "GITHUB_PACKAGES_PACKAGE",
    "GITHUB_PACKAGES_REGISTRY",
    "ArtifactExpectation",
    "ArtifactManifest",
    "BuildRequest",
    "BuildResult",
    "GitHubPackagesHttpResponse",
    "GitHubPackagesNetworkError",
    "GitHubPackagesPolicyError",
    "GitHubPackagesPublishPreflight",
    "GitHubPackagesTimeoutError",
    "GitHubPackagesTransport",
    "HttpResponse",
    "HttpTransport",
    "InstallImportResult",
    "MutationMayHaveStartedMarker",
    "NpmjsNetworkError",
    "NpmjsPolicyError",
    "NpmjsTimeoutError",
    "NpmjsTruncatedResponseError",
    "PackageTargetWitness",
    "PublicationExecutionResult",
    "PublishCommandResult",
    "PublishRunner",
    "RuntimeRequest",
    "StdlibHttpTransport",
    "build_node_package",
    "classify_github_packages_probe",
    "classify_publish_result",
    "form_mutation_may_have_started_marker",
    "observe_github_packages_projection",
    "observe_npmjs_projection",
    "preflight_github_packages_action",
    "publish_github_packages_action",
    "qualify_npm_artifact_contents",
    "qualify_npm_install_import",
    "run_node_project_build",
    "run_node_project_tests",
]
