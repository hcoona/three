"""GitHub Actions artifact enumeration and admission helpers."""

from __future__ import annotations

import re
from collections import defaultdict
from collections.abc import Iterable, Mapping, Sequence
from dataclasses import dataclass
from datetime import datetime

from three_workflow_release_contracts.ci_validation import (
    artifact_physical_name,
    validate_artifact_physical_name,
)
from three_workflow_release_contracts.contracts import (
    ContractValidationError,
    ValidationIssue,
)

_RFC3339_TIMESTAMP_RE = re.compile(
    r"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}"
    r"(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$",
)
_GITHUB_DIGEST_RE = re.compile(r"^sha256:[0-9a-f]{64}$")
_GITHUB_SLUG_RE = re.compile(r"^[A-Za-z0-9_.-]+$")


@dataclass(frozen=True, slots=True)
class GitHubActionsArtifactMetadata:
    """Normalized GitHub Actions artifact instance metadata."""

    artifact_id: int
    name: str
    created_at: str
    node_id: str | None = None
    size_in_bytes: int | None = None
    url: str | None = None
    archive_download_url: str | None = None
    expired: bool | None = None
    updated_at: str | None = None
    expires_at: str | None = None
    digest: str | None = None
    workflow_run_id: int | None = None
    workflow_run_head_branch: str | None = None
    workflow_run_head_sha: str | None = None
    workflow_run_repository_id: int | None = None
    workflow_run_head_repository_id: int | None = None

    def __post_init__(self) -> None:
        """Validate direct dataclass construction invariants."""
        issues: list[ValidationIssue] = []
        _validate_int_value(
            self.artifact_id,
            "artifact-id",
            issues,
            minimum=1,
        )
        _validate_str_value(
            self.name,
            "artifact-name",
            issues,
            non_empty=True,
        )
        _validate_timestamp_value(
            self.created_at,
            "created-at",
            issues,
            required=True,
        )
        _validate_optional_str_value(self.node_id, "node-id", issues)
        _validate_optional_int_value(
            self.size_in_bytes,
            "size-in-bytes",
            issues,
            minimum=0,
        )
        _validate_optional_str_value(self.url, "url", issues)
        _validate_optional_str_value(
            self.archive_download_url,
            "archive-download-url",
            issues,
        )
        _validate_optional_bool_value(self.expired, "expired", issues)
        _validate_timestamp_value(self.updated_at, "updated-at", issues)
        _validate_timestamp_value(self.expires_at, "expires-at", issues)
        _validate_optional_digest_value(self.digest, "digest", issues)
        _validate_optional_int_value(
            self.workflow_run_id,
            "workflow-run-id",
            issues,
            minimum=1,
        )
        _validate_optional_str_value(
            self.workflow_run_head_branch,
            "workflow-run-head-branch",
            issues,
        )
        _validate_optional_str_value(
            self.workflow_run_head_sha,
            "workflow-run-head-sha",
            issues,
        )
        _validate_optional_int_value(
            self.workflow_run_repository_id,
            "workflow-run-repository-id",
            issues,
            minimum=1,
        )
        _validate_optional_int_value(
            self.workflow_run_head_repository_id,
            "workflow-run-head-repository-id",
            issues,
            minimum=1,
        )
        if issues:
            raise ContractValidationError(issues)


type ArtifactCandidate = GitHubActionsArtifactMetadata | Mapping[str, object]
type ArtifactGroups = Mapping[str, tuple[ArtifactCandidate, ...]]


@dataclass(frozen=True, slots=True)
class ArtifactAdmission:
    """One exact-count admitted logical artifact candidate."""

    logical_ref: str
    physical_name: str
    artifact: GitHubActionsArtifactMetadata

    def __post_init__(self) -> None:
        """Validate direct admission construction invariants."""
        issues: list[ValidationIssue] = []
        if not isinstance(self.artifact, GitHubActionsArtifactMetadata):
            issues.append(
                ValidationIssue(
                    "artifact",
                    "must be GitHubActionsArtifactMetadata",
                ),
            )
        expected_physical_name: str | None = None
        try:
            expected_physical_name = artifact_physical_name(self.logical_ref)
        except ContractValidationError as error:
            issues.extend(error.issues)
        _validate_str_value(
            self.physical_name,
            "physical-artifact-name",
            issues,
            non_empty=True,
        )
        if (
            expected_physical_name is not None
            and self.physical_name != expected_physical_name
        ):
            issues.append(
                ValidationIssue(
                    "physical-artifact-name",
                    "must match logical artifact ref physical name",
                ),
            )
        if isinstance(self.artifact, GitHubActionsArtifactMetadata):
            if self.artifact.name != self.physical_name:
                issues.append(
                    ValidationIssue(
                        "artifact.name",
                        "must match admitted physical name",
                    ),
                )
            if self.artifact.expired is not False:
                issues.append(
                    ValidationIssue(
                        "artifact.expired",
                        "must be false for admitted artifacts",
                    ),
                )
        if issues:
            raise ContractValidationError(issues)


@dataclass(frozen=True, slots=True)
class ArtifactDownloadRequest:
    """ID-addressed artifact download request data."""

    artifact_id: int
    name: str
    archive_download_url: str | None = None
    api_path: str | None = None


def normalize_github_actions_artifact(
    artifact: object,
    *,
    path: str = "$",
) -> GitHubActionsArtifactMetadata:
    """Normalize one artifact object from the GitHub Actions artifact API."""
    issues: list[ValidationIssue] = []
    if not isinstance(artifact, Mapping):
        raise ContractValidationError(
            [ValidationIssue(path, "must be an artifact object")],
        )
    artifact_id = _required_int(artifact, "id", f"{path}.id", issues)
    name = _required_str(artifact, "name", f"{path}.name", issues)
    created_at = _required_timestamp(
        artifact,
        "created_at",
        f"{path}.created_at",
        issues,
    )
    node_id = _optional_str(artifact, "node_id", f"{path}.node_id", issues)
    size_in_bytes = _optional_int(
        artifact,
        "size_in_bytes",
        f"{path}.size_in_bytes",
        issues,
        minimum=0,
    )
    url = _optional_str(artifact, "url", f"{path}.url", issues)
    archive_download_url = _optional_str(
        artifact,
        "archive_download_url",
        f"{path}.archive_download_url",
        issues,
    )
    expired = _optional_bool(artifact, "expired", f"{path}.expired", issues)
    updated_at = _optional_timestamp(
        artifact,
        "updated_at",
        f"{path}.updated_at",
        issues,
    )
    expires_at = _optional_timestamp(
        artifact,
        "expires_at",
        f"{path}.expires_at",
        issues,
    )
    digest = _optional_digest(artifact, "digest", f"{path}.digest", issues)
    workflow_run = artifact.get("workflow_run")
    workflow_run_id: int | None = None
    workflow_run_head_branch: str | None = None
    workflow_run_head_sha: str | None = None
    workflow_run_repository_id: int | None = None
    workflow_run_head_repository_id: int | None = None
    if workflow_run is not None:
        if not isinstance(workflow_run, Mapping):
            issues.append(
                ValidationIssue(f"{path}.workflow_run", "must be an object"),
            )
        else:
            workflow_run_id = _optional_int(
                workflow_run,
                "id",
                f"{path}.workflow_run.id",
                issues,
                minimum=1,
            )
            workflow_run_head_branch = _optional_str(
                workflow_run,
                "head_branch",
                f"{path}.workflow_run.head_branch",
                issues,
            )
            workflow_run_head_sha = _optional_str(
                workflow_run,
                "head_sha",
                f"{path}.workflow_run.head_sha",
                issues,
            )
            workflow_run_repository_id = _optional_int(
                workflow_run,
                "repository_id",
                f"{path}.workflow_run.repository_id",
                issues,
                minimum=1,
            )
            workflow_run_head_repository_id = _optional_int(
                workflow_run,
                "head_repository_id",
                f"{path}.workflow_run.head_repository_id",
                issues,
                minimum=1,
            )
    if issues:
        raise ContractValidationError(issues)
    if artifact_id is None or name is None or created_at is None:
        msg = "required artifact metadata unexpectedly missing"
        raise AssertionError(msg)
    return GitHubActionsArtifactMetadata(
        artifact_id=artifact_id,
        name=name,
        created_at=created_at,
        node_id=node_id,
        size_in_bytes=size_in_bytes,
        url=url,
        archive_download_url=archive_download_url,
        expired=expired,
        updated_at=updated_at,
        expires_at=expires_at,
        digest=digest,
        workflow_run_id=workflow_run_id,
        workflow_run_head_branch=workflow_run_head_branch,
        workflow_run_head_sha=workflow_run_head_sha,
        workflow_run_repository_id=workflow_run_repository_id,
        workflow_run_head_repository_id=workflow_run_head_repository_id,
    )


def collect_artifacts_by_name(
    artifacts: object,
) -> dict[str, tuple[GitHubActionsArtifactMetadata, ...]]:
    """Group run-scoped artifact instances by physical artifact name."""
    if not isinstance(artifacts, Iterable) or isinstance(
        artifacts,
        str | bytes,
    ):
        raise ContractValidationError(
            [ValidationIssue("artifacts", "must be a non-string iterable")],
        )
    grouped: defaultdict[str, list[GitHubActionsArtifactMetadata]] = (
        defaultdict(list)
    )
    for index, artifact in enumerate(artifacts):
        if isinstance(artifact, GitHubActionsArtifactMetadata):
            metadata = artifact
        elif isinstance(artifact, Mapping):
            metadata = normalize_github_actions_artifact(
                artifact,
                path=f"$[{index}]",
            )
        else:
            raise ContractValidationError(
                [
                    ValidationIssue(
                        f"$[{index}]",
                        "must be GitHubActionsArtifactMetadata or artifact "
                        "object",
                    ),
                ],
            )
        grouped[metadata.name].append(metadata)
    return {name: tuple(items) for name, items in grouped.items()}


def admit_exactly_one_artifact(
    groups: object,
    *,
    logical_ref: str,
) -> ArtifactAdmission:
    """Admit one artifact instance for a logical ref, or fail closed."""
    physical_name = artifact_physical_name(logical_ref)
    validate_artifact_physical_name(physical_name)
    if not isinstance(groups, Mapping):
        raise ContractValidationError(
            [ValidationIssue("artifact-groups", "must be a mapping")],
        )
    raw_candidates = groups.get(physical_name, ())
    if not isinstance(raw_candidates, Sequence) or isinstance(
        raw_candidates,
        str | bytes,
    ):
        raise ContractValidationError(
            [
                ValidationIssue(
                    "artifact-candidates",
                    "must be a non-string sequence",
                ),
            ],
        )
    candidates = raw_candidates
    if len(candidates) != 1:
        detail = "missing" if len(candidates) == 0 else "duplicate"
        raise ContractValidationError(
            [
                ValidationIssue(
                    "artifact-candidate",
                    f"{detail} candidate for {physical_name}: "
                    f"expected exactly one, found {len(candidates)}",
                ),
            ],
        )
    raw_candidate = candidates[0]
    if isinstance(raw_candidate, GitHubActionsArtifactMetadata):
        candidate = raw_candidate
    elif isinstance(raw_candidate, Mapping):
        candidate = normalize_github_actions_artifact(
            raw_candidate,
            path="artifact-candidate",
        )
    else:
        raise ContractValidationError(
            [
                ValidationIssue(
                    "artifact-candidate",
                    "must be GitHubActionsArtifactMetadata or artifact object",
                ),
            ],
        )
    if candidate.expired is True:
        raise ContractValidationError(
            [
                ValidationIssue(
                    "artifact-candidate",
                    f"expired candidate for {physical_name}: "
                    "expected one live artifact",
                ),
            ],
        )
    if candidate.expired is not False:
        raise ContractValidationError(
            [
                ValidationIssue(
                    "artifact-candidate",
                    f"missing live-state for {physical_name}: "
                    "expired must be false",
                ),
            ],
        )
    return ArtifactAdmission(
        logical_ref=logical_ref,
        physical_name=physical_name,
        artifact=candidate,
    )


def artifact_zip_api_path(
    *,
    owner: object,
    repository: object,
    artifact_id: object,
) -> str:
    """Return the REST API path for an ID-addressed artifact ZIP download."""
    _validate_github_slug(owner, "owner")
    _validate_github_slug(repository, "repository")
    if not isinstance(artifact_id, int) or isinstance(artifact_id, bool):
        raise ContractValidationError(
            [ValidationIssue("artifact-id", "must be an integer")],
        )
    if artifact_id < 1:
        raise ContractValidationError(
            [ValidationIssue("artifact-id", "must be >= 1")],
        )
    return f"/repos/{owner}/{repository}/actions/artifacts/{artifact_id}/zip"


def artifact_download_request(
    admission: ArtifactAdmission,
    *,
    owner: str | None = None,
    repository: str | None = None,
) -> ArtifactDownloadRequest:
    """Prepare ID-preferred API input for downloading an admitted artifact."""
    api_path = None
    if owner is not None or repository is not None:
        if owner is None or repository is None:
            raise ContractValidationError(
                [
                    ValidationIssue(
                        "repository",
                        "owner and repository must be supplied together",
                    ),
                ],
            )
        api_path = artifact_zip_api_path(
            owner=owner,
            repository=repository,
            artifact_id=admission.artifact.artifact_id,
        )
    return ArtifactDownloadRequest(
        artifact_id=admission.artifact.artifact_id,
        name=admission.artifact.name,
        archive_download_url=admission.artifact.archive_download_url,
        api_path=api_path,
    )


def actions_download_artifact_inputs(
    admission: ArtifactAdmission,
    *,
    path: str | None = None,
) -> dict[str, str]:
    """Prepare `actions/download-artifact@v8` inputs using artifact ID."""
    inputs = {"artifact-ids": str(admission.artifact.artifact_id)}
    if path is not None:
        inputs["path"] = path
    return inputs


def artifact_metadata_has_authoritative_producer_identity(
    artifact: GitHubActionsArtifactMetadata | Mapping[str, object],
) -> bool:
    """Return whether artifact metadata proves uploader job identity."""
    if not isinstance(artifact, GitHubActionsArtifactMetadata):
        normalize_github_actions_artifact(artifact)
    return False


def _required_str(
    obj: Mapping[str, object],
    key: str,
    path: str,
    issues: list[ValidationIssue],
) -> str | None:
    if key not in obj:
        issues.append(ValidationIssue(path, "is required"))
        return None
    return _optional_str(obj, key, path, issues, non_empty=True)


def _optional_str(
    obj: Mapping[str, object],
    key: str,
    path: str,
    issues: list[ValidationIssue],
    *,
    non_empty: bool = False,
) -> str | None:
    if key not in obj or obj[key] is None:
        return None
    value = obj[key]
    if not isinstance(value, str):
        issues.append(ValidationIssue(path, "must be a string"))
        return None
    if non_empty and value == "":
        issues.append(ValidationIssue(path, "must not be empty"))
        return None
    return value


def _required_int(
    obj: Mapping[str, object],
    key: str,
    path: str,
    issues: list[ValidationIssue],
) -> int | None:
    if key not in obj:
        issues.append(ValidationIssue(path, "is required"))
        return None
    return _optional_int(obj, key, path, issues, minimum=1)


def _optional_int(
    obj: Mapping[str, object],
    key: str,
    path: str,
    issues: list[ValidationIssue],
    *,
    minimum: int | None = None,
) -> int | None:
    if key not in obj or obj[key] is None:
        return None
    value = obj[key]
    if not isinstance(value, int) or isinstance(value, bool):
        issues.append(ValidationIssue(path, "must be an integer"))
        return None
    if minimum is not None and value < minimum:
        issues.append(ValidationIssue(path, f"must be >= {minimum}"))
        return None
    return value


def _optional_bool(
    obj: Mapping[str, object],
    key: str,
    path: str,
    issues: list[ValidationIssue],
) -> bool | None:
    if key not in obj or obj[key] is None:
        return None
    value = obj[key]
    if not isinstance(value, bool):
        issues.append(ValidationIssue(path, "must be a boolean"))
        return None
    return value


def _required_timestamp(
    obj: Mapping[str, object],
    key: str,
    path: str,
    issues: list[ValidationIssue],
) -> str | None:
    value = _required_str(obj, key, path, issues)
    if value is not None:
        _validate_rfc3339_timestamp(value, path, issues)
    return value


def _optional_timestamp(
    obj: Mapping[str, object],
    key: str,
    path: str,
    issues: list[ValidationIssue],
) -> str | None:
    value = _optional_str(obj, key, path, issues)
    if value is not None:
        _validate_rfc3339_timestamp(value, path, issues)
    return value


def _optional_digest(
    obj: Mapping[str, object],
    key: str,
    path: str,
    issues: list[ValidationIssue],
) -> str | None:
    value = _optional_str(obj, key, path, issues)
    if value is not None and _GITHUB_DIGEST_RE.fullmatch(value) is None:
        issues.append(
            ValidationIssue(path, "must be sha256: followed by 64 hex chars"),
        )
    return value


def _validate_github_slug(value: object, path: str) -> None:
    if not isinstance(value, str):
        raise ContractValidationError(
            [ValidationIssue(path, "must be a string")],
        )
    if _GITHUB_SLUG_RE.fullmatch(value) is None:
        raise ContractValidationError(
            [
                ValidationIssue(
                    path,
                    "must be a GitHub owner or repository slug",
                ),
            ],
        )


def _validate_int_value(
    value: object,
    path: str,
    issues: list[ValidationIssue],
    *,
    minimum: int | None = None,
) -> None:
    if not isinstance(value, int) or isinstance(value, bool):
        issues.append(ValidationIssue(path, "must be an integer"))
    elif minimum is not None and value < minimum:
        issues.append(ValidationIssue(path, f"must be >= {minimum}"))


def _validate_optional_int_value(
    value: object,
    path: str,
    issues: list[ValidationIssue],
    *,
    minimum: int | None = None,
) -> None:
    if value is not None:
        _validate_int_value(value, path, issues, minimum=minimum)


def _validate_str_value(
    value: object,
    path: str,
    issues: list[ValidationIssue],
    *,
    non_empty: bool = False,
) -> None:
    if not isinstance(value, str):
        issues.append(ValidationIssue(path, "must be a string"))
    elif non_empty and value == "":
        issues.append(ValidationIssue(path, "must not be empty"))


def _validate_optional_str_value(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if value is not None:
        _validate_str_value(value, path, issues)


def _validate_optional_bool_value(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if value is not None and not isinstance(value, bool):
        issues.append(ValidationIssue(path, "must be a boolean"))


def _validate_timestamp_value(
    value: object,
    path: str,
    issues: list[ValidationIssue],
    *,
    required: bool = False,
) -> None:
    if value is None and not required:
        return
    if not isinstance(value, str):
        issues.append(ValidationIssue(path, "must be a string"))
        return
    _validate_rfc3339_timestamp(value, path, issues)


def _validate_optional_digest_value(
    value: object,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if value is None:
        return
    if not isinstance(value, str):
        issues.append(ValidationIssue(path, "must be a string"))
    elif _GITHUB_DIGEST_RE.fullmatch(value) is None:
        issues.append(
            ValidationIssue(path, "must be sha256: followed by 64 hex chars"),
        )


def _validate_rfc3339_timestamp(
    value: str,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if _RFC3339_TIMESTAMP_RE.fullmatch(value) is None:
        issues.append(ValidationIssue(path, "must be an RFC 3339 timestamp"))
        return
    parseable_value = value.removesuffix("Z") + (
        "+00:00" if value.endswith("Z") else ""
    )
    try:
        parsed = datetime.fromisoformat(parseable_value)
    except ValueError:
        issues.append(
            ValidationIssue(path, "must be a real RFC 3339 timestamp"),
        )
        return
    if parsed.tzinfo is None:
        issues.append(ValidationIssue(path, "must include a timezone"))
