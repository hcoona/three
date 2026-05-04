"""Workflow-release authoring validation APIs."""

from __future__ import annotations

from three_workflow_release_authoring.authoring import (
    API_VERSION,
    CATALOG_PATH,
    DOTNET_METADATA_INPUT_API_VERSION,
    REQUIRED_DESCRIPTOR_ROOTS,
    Artifact,
    AuthoringIssue,
    AuthoringSnapshot,
    AuthoringValidationError,
    Companion,
    ProjectDescriptor,
    TargetInstance,
    TargetUsage,
    Variant,
    diagnostics_document,
    validate_authoring,
    validate_authoring_documents,
    validate_project_descriptor_document,
    validate_target_catalog_document,
)

__all__ = [
    "API_VERSION",
    "CATALOG_PATH",
    "DOTNET_METADATA_INPUT_API_VERSION",
    "REQUIRED_DESCRIPTOR_ROOTS",
    "Artifact",
    "AuthoringIssue",
    "AuthoringSnapshot",
    "AuthoringValidationError",
    "Companion",
    "ProjectDescriptor",
    "TargetInstance",
    "TargetUsage",
    "Variant",
    "diagnostics_document",
    "validate_authoring",
    "validate_authoring_documents",
    "validate_project_descriptor_document",
    "validate_target_catalog_document",
]
