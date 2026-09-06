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
        "ACCEPTANCE_PACKAGE_COORDINATE",
        "ACCEPTANCE_SCENARIOS",
        "ACCEPTANCE_TAGS",
        "FixedCoordinateAcceptanceProbeResult",
        "GitHubPackagesHttpResponse",
        "GitHubPackagesNetworkError",
        "GitHubPackagesPolicyError",
        "GitHubPackagesTimeoutError",
        "GitHubPackagesTransport",
        "ValidatedAcceptanceRequestProof",
        "GitHubPackagesActiveState",
        "read_github_packages_active_state",
        "run_fixed_coordinate_acceptance_probe",
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
    "ACCEPTANCE_PACKAGE_COORDINATE",
    "ACCEPTANCE_SCENARIOS",
    "ACCEPTANCE_TAGS",
    "GITHUB_PACKAGES_DESTINATION_ID",
    "GITHUB_PACKAGES_OBSERVATION_CONTRACT_ID",
    "GITHUB_PACKAGES_OPERATION",
    "GITHUB_PACKAGES_PACKAGE",
    "GITHUB_PACKAGES_REGISTRY",
    "ArtifactExpectation",
    "ArtifactManifest",
    "BuildRequest",
    "BuildResult",
    "FixedCoordinateAcceptanceProbeResult",
    "GitHubPackagesActiveState",
    "GitHubPackagesHttpResponse",
    "GitHubPackagesNetworkError",
    "GitHubPackagesPolicyError",
    "GitHubPackagesTimeoutError",
    "GitHubPackagesTransport",
    "HttpResponse",
    "HttpTransport",
    "InstallImportResult",
    "NpmjsNetworkError",
    "NpmjsPolicyError",
    "NpmjsTimeoutError",
    "NpmjsTruncatedResponseError",
    "PackageTargetWitness",
    "RuntimeRequest",
    "StdlibHttpTransport",
    "ValidatedAcceptanceRequestProof",
    "build_node_package",
    "observe_npmjs_projection",
    "qualify_npm_artifact_contents",
    "qualify_npm_install_import",
    "read_github_packages_active_state",
    "run_fixed_coordinate_acceptance_probe",
    "run_node_project_build",
    "run_node_project_tests",
]
