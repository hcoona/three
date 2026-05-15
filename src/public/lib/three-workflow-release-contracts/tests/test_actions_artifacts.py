"""GitHub Actions artifact helper tests."""

from __future__ import annotations

import pytest
from three_workflow_release_contracts import (
    ArtifactAdmission,
    ContractValidationError,
    GitHubActionsArtifactMetadata,
    actions_download_artifact_inputs,
    admit_exactly_one_artifact,
    artifact_download_request,
    artifact_metadata_has_authoritative_producer_identity,
    artifact_physical_name,
    artifact_zip_api_path,
    collect_artifacts_by_name,
    normalize_github_actions_artifact,
)

DEFAULT_ARTIFACT_ID = 1001
DEFAULT_WORKFLOW_RUN_ID = 2002
DEFAULT_REPOSITORY_ID = 3003
OTHER_ARTIFACT_ID = 3
ADMITTED_ARTIFACT_ID = 7004860103
DUPLICATE_FIRST_ID = 1
DUPLICATE_SECOND_ID = 2
INVALID_ARTIFACT_ID = 0


def _artifact(
    *,
    artifact_id: int = DEFAULT_ARTIFACT_ID,
    name: str | None = None,
    created_at: str = "2026-05-14T21:09:21Z",
    digest: str | None = None,
    expired: bool = False,
) -> dict[str, object]:
    physical_name = name or artifact_physical_name(_logical_ref())
    return {
        "id": artifact_id,
        "node_id": f"ART_{artifact_id}",
        "name": physical_name,
        "size_in_bytes": 42,
        "url": (
            "https://api.github.com/repos/hcoona/three/actions/artifacts/"
            f"{artifact_id}"
        ),
        "archive_download_url": (
            "https://api.github.com/repos/hcoona/three/actions/artifacts/"
            f"{artifact_id}/zip"
        ),
        "expired": expired,
        "created_at": created_at,
        "updated_at": "2026-05-14T21:10:21Z",
        "expires_at": "2026-08-12T21:09:21Z",
        "digest": digest,
        "workflow_run": {
            "id": DEFAULT_WORKFLOW_RUN_ID,
            "head_branch": "main",
            "head_sha": "a" * 40,
            "repository_id": DEFAULT_REPOSITORY_ID,
            "head_repository_id": DEFAULT_REPOSITORY_ID,
        },
    }


def _logical_ref() -> str:
    return "ci-validation/planning/123/4/validation-plan.json"


def _metadata(
    *,
    name: str | None = None,
    expired: bool | None = False,
) -> GitHubActionsArtifactMetadata:
    return GitHubActionsArtifactMetadata(
        artifact_id=ADMITTED_ARTIFACT_ID,
        name=name or artifact_physical_name(_logical_ref()),
        created_at="2026-05-14T21:09:21Z",
        expired=expired,
    )


def test_metadata_normalization_preserves_evidence() -> None:
    """Normalize API metadata without losing evidence fields."""
    digest = "sha256:" + "0" * 64

    metadata = normalize_github_actions_artifact(
        _artifact(digest=digest),
    )

    assert metadata.artifact_id == DEFAULT_ARTIFACT_ID
    assert metadata.created_at == "2026-05-14T21:09:21Z"
    assert metadata.updated_at == "2026-05-14T21:10:21Z"
    assert metadata.expires_at == "2026-08-12T21:09:21Z"
    assert metadata.digest == digest
    assert metadata.workflow_run_id == DEFAULT_WORKFLOW_RUN_ID
    assert metadata.workflow_run_head_sha == "a" * 40


@pytest.mark.parametrize(
    "patch",
    [
        {"id": True},
        {"id": 0},
        {"name": ""},
        {"created_at": "not-a-timestamp"},
        {"created_at": "2026-99-99T99:99:99Z"},
        {"digest": "0" * 64},
        {"workflow_run": "not-an-object"},
    ],
)
def test_artifact_metadata_normalization_rejects_invalid_metadata(
    patch: dict[str, object],
) -> None:
    """Fail closed when the API artifact shape is not trustworthy."""
    artifact = _artifact()
    artifact.update(patch)

    with pytest.raises(ContractValidationError):
        normalize_github_actions_artifact(artifact)


@pytest.mark.parametrize("artifact", [None, object(), [], "not-an-object"])
def test_metadata_normalization_rejects_malformed_top_level(
    artifact: object,
) -> None:
    """Malformed top-level metadata inputs fail closed."""
    with pytest.raises(ContractValidationError):
        normalize_github_actions_artifact(artifact)


def test_metadata_normalization_accepts_offset_timestamp() -> None:
    """Accept real RFC 3339 timestamps with numeric offsets."""
    metadata = normalize_github_actions_artifact(
        _artifact(created_at="2026-05-14T21:09:21+08:00"),
    )

    assert metadata.created_at == "2026-05-14T21:09:21+08:00"


def test_direct_artifact_metadata_instances_validate_invariants() -> None:
    """Reject invalid direct metadata instances before helpers can use them."""
    with pytest.raises(ContractValidationError):
        GitHubActionsArtifactMetadata(
            artifact_id=INVALID_ARTIFACT_ID,
            name=artifact_physical_name(_logical_ref()),
            created_at="2026-05-14T21:09:21Z",
        )
    with pytest.raises(ContractValidationError):
        GitHubActionsArtifactMetadata(
            artifact_id=DEFAULT_ARTIFACT_ID,
            name="",
            created_at="2026-05-14T21:09:21Z",
        )
    with pytest.raises(ContractValidationError):
        GitHubActionsArtifactMetadata(
            artifact_id=DEFAULT_ARTIFACT_ID,
            name=artifact_physical_name(_logical_ref()),
            created_at="2026-99-99T99:99:99Z",
        )


def test_collect_artifacts_by_name_preserves_duplicate_instances() -> None:
    """Group as name to instances instead of losing duplicates in a dict."""
    logical_ref = _logical_ref()
    physical_name = artifact_physical_name(logical_ref)

    groups = collect_artifacts_by_name(
        [
            _artifact(artifact_id=DUPLICATE_FIRST_ID, name=physical_name),
            _artifact(artifact_id=DUPLICATE_SECOND_ID, name=physical_name),
            _artifact(artifact_id=OTHER_ARTIFACT_ID, name="unrelated"),
        ],
    )

    assert [item.artifact_id for item in groups[physical_name]] == [
        DUPLICATE_FIRST_ID,
        DUPLICATE_SECOND_ID,
    ]
    assert groups["unrelated"][0].artifact_id == OTHER_ARTIFACT_ID


def test_collect_artifacts_by_name_rejects_non_mapping_element() -> None:
    """Malformed raw artifact list elements fail closed."""
    with pytest.raises(ContractValidationError, match=r"\$\[0\]"):
        collect_artifacts_by_name([object()])


@pytest.mark.parametrize("artifacts", [None, object(), "not-an-iterable"])
def test_collect_artifacts_by_name_rejects_malformed_top_level(
    artifacts: object,
) -> None:
    """Malformed top-level artifact collections fail closed."""
    with pytest.raises(ContractValidationError):
        collect_artifacts_by_name(artifacts)


def test_admit_exactly_one_artifact_accepts_single_candidate() -> None:
    """Admit exactly one artifact with the Group 1 physical name."""
    logical_ref = _logical_ref()
    groups = collect_artifacts_by_name(
        [_artifact(artifact_id=ADMITTED_ARTIFACT_ID)],
    )

    admission = admit_exactly_one_artifact(groups, logical_ref=logical_ref)

    assert admission.logical_ref == logical_ref
    assert admission.physical_name == artifact_physical_name(logical_ref)
    assert admission.artifact.artifact_id == ADMITTED_ARTIFACT_ID


def test_admit_exactly_one_fails_closed_for_missing() -> None:
    """Missing candidate names fail closed instead of falling back by role."""
    groups = collect_artifacts_by_name([_artifact(name="unrelated")])

    with pytest.raises(ContractValidationError, match="missing candidate"):
        admit_exactly_one_artifact(groups, logical_ref=_logical_ref())


def test_admit_exactly_one_fails_closed_for_duplicate() -> None:
    """Duplicate same-name instances fail closed before download."""
    logical_ref = _logical_ref()
    physical_name = artifact_physical_name(logical_ref)
    groups = collect_artifacts_by_name(
        [
            _artifact(artifact_id=DUPLICATE_FIRST_ID, name=physical_name),
            _artifact(artifact_id=DUPLICATE_SECOND_ID, name=physical_name),
        ],
    )

    with pytest.raises(ContractValidationError, match="duplicate candidate"):
        admit_exactly_one_artifact(groups, logical_ref=logical_ref)


def test_admit_exactly_one_fails_closed_for_expired_candidate() -> None:
    """Expired same-name candidates are not live/downloadable evidence."""
    groups = collect_artifacts_by_name([_artifact(expired=True)])

    with pytest.raises(ContractValidationError, match="expired candidate"):
        admit_exactly_one_artifact(groups, logical_ref=_logical_ref())


def test_admit_exactly_one_fails_closed_for_missing_expired_state() -> None:
    """Missing expiration state cannot prove the artifact is live."""
    artifact = _artifact()
    del artifact["expired"]
    groups = collect_artifacts_by_name([artifact])

    with pytest.raises(ContractValidationError, match="missing live-state"):
        admit_exactly_one_artifact(groups, logical_ref=_logical_ref())


def test_admit_rejects_direct_unknown_expired_state() -> None:
    """Direct metadata with unknown expiration state is not admissible."""
    groups = collect_artifacts_by_name(
        [
            GitHubActionsArtifactMetadata(
                artifact_id=ADMITTED_ARTIFACT_ID,
                name=artifact_physical_name(_logical_ref()),
                created_at="2026-05-14T21:09:21Z",
                expired=None,
            ),
        ],
    )

    with pytest.raises(ContractValidationError, match="missing live-state"):
        admit_exactly_one_artifact(groups, logical_ref=_logical_ref())


def test_admit_rejects_invalid_grouped_candidate() -> None:
    """Invalid grouped candidates fail closed with validation errors."""
    physical_name = artifact_physical_name(_logical_ref())
    groups = {physical_name: ({"id": ADMITTED_ARTIFACT_ID},)}

    with pytest.raises(ContractValidationError, match="artifact-candidate"):
        admit_exactly_one_artifact(groups, logical_ref=_logical_ref())


@pytest.mark.parametrize(
    "groups",
    [
        [],
        {artifact_physical_name(_logical_ref()): None},
        {artifact_physical_name(_logical_ref()): "not-a-sequence"},
        {artifact_physical_name(_logical_ref()): {"id": ADMITTED_ARTIFACT_ID}},
    ],
)
def test_admit_rejects_malformed_groups(groups: object) -> None:
    """Malformed grouped input fails closed with validation errors."""
    with pytest.raises(ContractValidationError):
        admit_exactly_one_artifact(groups, logical_ref=_logical_ref())


def test_direct_admission_validates_invariants() -> None:
    """Reject direct admissions that would bypass exact live-name binding."""
    logical_ref = _logical_ref()
    physical_name = artifact_physical_name(logical_ref)

    with pytest.raises(ContractValidationError, match="physical name"):
        ArtifactAdmission(
            logical_ref=logical_ref,
            physical_name=artifact_physical_name(
                "ci-validation/planning/123/4/other.json",
            ),
            artifact=_metadata(),
        )
    with pytest.raises(ContractValidationError, match=r"artifact\.name"):
        ArtifactAdmission(
            logical_ref=logical_ref,
            physical_name=physical_name,
            artifact=_metadata(name="unrelated"),
        )
    with pytest.raises(ContractValidationError, match=r"artifact\.expired"):
        ArtifactAdmission(
            logical_ref=logical_ref,
            physical_name=physical_name,
            artifact=_metadata(expired=True),
        )
    with pytest.raises(ContractValidationError, match=r"artifact\.expired"):
        ArtifactAdmission(
            logical_ref=logical_ref,
            physical_name=physical_name,
            artifact=_metadata(expired=None),
        )


def test_download_request_prefers_id_addressed_inputs() -> None:
    """Prepare download data from the admitted artifact ID."""
    logical_ref = _logical_ref()
    admission = admit_exactly_one_artifact(
        collect_artifacts_by_name(
            [_artifact(artifact_id=ADMITTED_ARTIFACT_ID)],
        ),
        logical_ref=logical_ref,
    )

    request = artifact_download_request(
        admission,
        owner="hcoona",
        repository="three",
    )

    assert request.artifact_id == ADMITTED_ARTIFACT_ID
    assert request.name == artifact_physical_name(logical_ref)
    assert request.api_path == (
        f"/repos/hcoona/three/actions/artifacts/{ADMITTED_ARTIFACT_ID}/zip"
    )
    assert actions_download_artifact_inputs(admission, path="downloaded") == {
        "artifact-ids": str(ADMITTED_ARTIFACT_ID),
        "path": "downloaded",
    }


def test_artifact_zip_api_path_rejects_invalid_download_inputs() -> None:
    """Reject invalid API path inputs before constructing download commands."""
    with pytest.raises(ContractValidationError):
        artifact_zip_api_path(
            owner="hcoona",
            repository="three",
            artifact_id=INVALID_ARTIFACT_ID,
        )
    with pytest.raises(ContractValidationError):
        artifact_zip_api_path(
            owner="hcoona",
            repository="three",
            artifact_id=True,
        )
    with pytest.raises(ContractValidationError):
        artifact_zip_api_path(
            owner=123,
            repository="three",
            artifact_id=ADMITTED_ARTIFACT_ID,
        )
    with pytest.raises(ContractValidationError):
        artifact_zip_api_path(
            owner="hcoona",
            repository=123,
            artifact_id=ADMITTED_ARTIFACT_ID,
        )


def test_metadata_has_no_authoritative_producer_identity() -> None:
    """Artifact API fields are not uploader job or matrix proof."""
    artifact = GitHubActionsArtifactMetadata(
        artifact_id=DUPLICATE_FIRST_ID,
        name=artifact_physical_name(_logical_ref()),
        created_at="2026-05-14T21:09:21Z",
    )

    assert not artifact_metadata_has_authoritative_producer_identity(artifact)
    assert not artifact_metadata_has_authoritative_producer_identity(
        _artifact(),
    )
