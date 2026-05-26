"""Trusted CI validation writer identity helpers."""

from __future__ import annotations

from collections.abc import Mapping
from typing import cast

from three_workflow_release_contracts.ci_validation import canonical_json_digest
from three_workflow_release_contracts.contracts import (
    ContractValidationError,
    ValidationIssue,
)

_WRITER_IDENTITY_API_VERSION = "three.ci.validation.writer-id/v1alpha1"


def ci_validation_writer_id(
    *,
    workflow: str,
    job: str,
    matrix: Mapping[str, object] | None = None,
) -> str:
    """Return the trusted writer ID from GitHub Actions job context."""
    issues: list[ValidationIssue] = []
    if not isinstance(workflow, str) or workflow == "":
        issues.append(ValidationIssue("workflow", "must be a string"))
    if not isinstance(job, str) or job == "":
        issues.append(ValidationIssue("job", "must be a string"))
    matrix_value: Mapping[str, object]
    if matrix is None:
        matrix_value = {}
    elif not isinstance(matrix, Mapping):
        issues.append(ValidationIssue("matrix", "must be an object"))
        matrix_value = {}
    else:
        matrix_value = matrix
        _validate_string_keys(
            cast("Mapping[object, object]", matrix_value),
            "matrix",
            issues,
        )
    if issues:
        raise ContractValidationError(issues)
    try:
        digest = canonical_json_digest(
            {
                "api-version": _WRITER_IDENTITY_API_VERSION,
                "workflow": workflow,
                "job": job,
                "matrix": dict(matrix_value),
            },
        )
    except (TypeError, ValueError) as error:
        raise ContractValidationError(
            [ValidationIssue("matrix", str(error))],
        ) from error
    return f"github-actions-job:{digest}"


def _validate_string_keys(
    value: Mapping[object, object],
    path: str,
    issues: list[ValidationIssue],
) -> None:
    for key, nested in value.items():
        if not isinstance(key, str):
            issues.append(ValidationIssue(path, "keys must be strings"))
        if isinstance(nested, Mapping):
            _validate_string_keys(
                cast("Mapping[object, object]", nested),
                f"{path}.{key}",
                issues,
            )
