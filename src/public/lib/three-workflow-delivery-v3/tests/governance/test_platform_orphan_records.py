"""Focused contracts for the Platform-Orphan phase-1 records."""

# ruff: noqa: D103

from __future__ import annotations

import hashlib
import stat
from copy import deepcopy
from pathlib import Path
from typing import Any

import pytest
from three_workflow_delivery_v3 import records
from three_workflow_delivery_v3.canonical import (
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.records import (
    PLATFORM_ORPHAN_ACTIVE_AUTHORITY_SHA256,
    PLATFORM_ORPHAN_AUTHORITY_PATH,
    PLATFORM_ORPHAN_RESULT_PATH,
    PlatformOrphanActiveAuthority,
    PlatformOrphanConsumedAudit,
    PlatformOrphanReconciliationResult,
    admit_platform_orphan_active_authority,
    admit_platform_orphan_consumed_audit,
    admit_platform_orphan_reconciliation_result,
)

REPO_ROOT = Path(__file__).resolve().parents[6]
ACTIVE_PATH = (
    Path(__file__).resolve().parents[1]
    / "fixtures/governance/platform-orphan-active-authority.json"
)
ACTIVE_BLOB_OID = "d492d25111231e6c4f4d7c6bb6083e98001aa4fa"
ACTIVE_RAW_SHA256 = (
    "aecb66a29813c654d804c3fda95a2ba2c5149fbf2479223a1adbf51ba0e4419c"
)
SHA256_A = "sha256:" + ("a" * 64)
EXPECTED_SHA512 = (
    "sha512:"
    "080c3d828a30d73d1febc3b6773015fafb529cf3a2be81fe597e83a83a589d32"
    "c1be62e933fb38ac4a77f9cb561c6399d3b2e6fe9179b3e4aed93087007140f2"
)
CONTROL_COMMIT = "c" * 40
BLOB_OID = "d492d25111231e6c4f4d7c6bb6083e98001aa4fa"
INVOCATION_ID = "12345678-1234-5678-9234-567812345678"
PLATFORM_ORPHAN_EXPORTS = {
    "PLATFORM_ORPHAN_ACTIVE_AUTHORITY_SHA256",
    "PLATFORM_ORPHAN_ACTIVE_SCHEMA",
    "PLATFORM_ORPHAN_AUDIT_SCHEMA",
    "PLATFORM_ORPHAN_AUTHORITY_PATH",
    "PLATFORM_ORPHAN_REF",
    "PLATFORM_ORPHAN_REPOSITORY",
    "PLATFORM_ORPHAN_RESULT_PATH",
    "PLATFORM_ORPHAN_RESULT_SCHEMA",
    "PLATFORM_ORPHAN_RUN_ID",
    "PlatformOrphanActiveAuthority",
    "PlatformOrphanConsumedAudit",
    "PlatformOrphanReconciliationResult",
    "admit_platform_orphan_active_authority",
    "admit_platform_orphan_consumed_audit",
    "admit_platform_orphan_reconciliation_result",
}


def _active_document() -> dict[str, Any]:
    return parse_canonical_json(ACTIVE_PATH.read_bytes())


def _platform_state() -> dict[str, Any]:
    return {
        "run_id": 32809578776,
        "run_attempt": 1,
        "run_status": "queued",
        "run_conclusion": None,
        "run_updated_at": "2026-08-25T04:35:59Z",
        "workflow_id": 341728447,
        "workflow_state": "disabled_manually",
        "job_count": 0,
        "pending_deployment_count": 0,
        "artifact_count": 0,
        "workflow_source_absent": True,
        "environment_absent": True,
        "transition_ref_absent": True,
        "jobs_page_count": 1,
        "artifact_page_count": 1,
    }


def _destination_state() -> dict[str, Any]:
    return {
        "package_coordinate": (
            "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.5"
        ),
        "repository_association": "hcoona/three",
        "expected_tag": "wdv3-acceptance-5",
        "target_sha": "b031e5e0bd98a95943a03a1529b64e856e1a8aa1",
        "classification": "exact",
        "manifest_version": "0.0.0-wdv3-acceptance.5",
        "tag_projection": "match",
        "tarball_sha512": EXPECTED_SHA512,
        "manifest_digest": SHA256_A,
        "package_target_witness_digest": SHA256_A,
    }


def _observation(
    phase: str,
    observed_at: str,
    state: dict[str, Any],
) -> dict[str, Any]:
    return {
        "phase": phase,
        "observed_at": observed_at,
        "state": deepcopy(state),
        "state_sha256": canonical_sha256(state),
    }


def _requests(
    *,
    jobs_page_count: int = 1,
    artifact_page_count: int = 1,
    manifest_present: bool = True,
    tarball_path: str = (
        "/@hcoona/hcoona-release-smoke-npm/-/"
        "hcoona-release-smoke-npm-0.0.0-wdv3-acceptance.5.tgz"
    ),
) -> list[dict[str, Any]]:
    api = "https://api.github.com"
    npm = "https://npm.pkg.github.com"
    source: list[tuple[str, str, int, int, str | None]] = [
        (api, "/repos/hcoona/three/branches/main", 200, 1, None),
        (api, "/repos/hcoona/three/git/ref/heads/main", 200, 1, None),
        (
            api,
            (
                "/repos/hcoona/three/contents/.github/workflow-delivery/"
                "governance/platform-orphan-run-32809578776.json"
            ),
            200,
            1,
            None,
        ),
    ]
    observations: list[tuple[str, str, int, int, str | None]] = [
        (
            api,
            (
                "/repos/hcoona/three/contents/.github/workflows/"
                "workflow-delivery-v3-buddy-smoke-acceptance-retry-2.yml"
            ),
            404,
            1,
            None,
        ),
        (
            api,
            "/repos/hcoona/three/actions/runs/32809578776",
            200,
            1,
            None,
        ),
        (
            api,
            "/repos/hcoona/three/actions/workflows/341728447",
            200,
            1,
            None,
        ),
    ]
    observations.extend(
        (
            api,
            "/repos/hcoona/three/actions/runs/32809578776/jobs",
            200,
            page,
            f"filter=all&per_page=100&page={page}",
        )
        for page in range(1, jobs_page_count + 1)
    )
    observations.extend(
        (
            api,
            "/repos/hcoona/three/actions/runs/32809578776/artifacts",
            200,
            page,
            f"per_page=100&page={page}",
        )
        for page in range(1, artifact_page_count + 1)
    )
    observations.extend(
        [
            (
                api,
                (
                    "/repos/hcoona/three/actions/runs/32809578776/"
                    "pending_deployments"
                ),
                200,
                1,
                None,
            ),
            (
                api,
                (
                    "/repos/hcoona/three/environments/"
                    "workflow-delivery-v3-buddy-smoke-acceptance-retry-2"
                ),
                404,
                1,
                None,
            ),
            (
                api,
                (
                    "/repos/hcoona/three/git/ref/heads/"
                    "workflow-delivery-v3-acceptance-retry-2-transition"
                ),
                404,
                1,
                None,
            ),
            (
                api,
                "/users/hcoona/packages/npm/hcoona-release-smoke-npm",
                200,
                1,
                None,
            ),
            (
                npm,
                ("/@hcoona%2Fhcoona-release-smoke-npm/0.0.0-wdv3-acceptance.5"),
                200 if manifest_present else 404,
                1,
                None,
            ),
            (
                npm,
                "/-/package/@hcoona%2Fhcoona-release-smoke-npm/dist-tags",
                200,
                1,
                None,
            ),
        ]
    )
    if manifest_present:
        observations.append(
            (
                npm,
                tarball_path,
                200,
                1,
                None,
            )
        )
    return [
        {
            "sequence": sequence,
            "phase": phase,
            "method": "GET",
            "origin": origin,
            "path": path,
            "page_cursor": cursor,
            "page_index": page,
            "http_status": status,
            "complete": True,
        }
        for sequence, (
            phase,
            (origin, path, status, page, cursor),
        ) in enumerate(
            (
                *(("initial", request) for request in source),
                *(("initial", request) for request in observations),
                *(("final", request) for request in observations),
                *(("final", request) for request in source),
            ),
            start=1,
        )
    ]


def _candidate_document() -> dict[str, Any]:
    platform = _platform_state()
    destination = _destination_state()
    candidate: dict[str, Any] = {
        "schema": (
            "workflow-delivery/v3/"
            "platform-orphan-acceptance-reconciliation-result"
        ),
        "version": 1,
        "producer": {
            "id": "three-workflow-delivery-v3/platform-orphan-reconcile",
            "entry_point": (
                "three-workflow-delivery-v3 governance "
                "reconcile-platform-orphan-32809578776"
            ),
            "repository": "hcoona/three",
            "ref": "refs/heads/main",
            "control_commit": CONTROL_COMMIT,
        },
        "invocation": {
            "id": INVOCATION_ID,
            "started_at": "2026-09-08T04:35:59Z",
            "completed_at": "2026-09-08T04:36:01Z",
        },
        "authority": {
            "repository": "hcoona/three",
            "ref": "refs/heads/main",
            "path": PLATFORM_ORPHAN_AUTHORITY_PATH,
            "initial_commit": CONTROL_COMMIT,
            "initial_blob_oid": BLOB_OID,
            "initial_content_sha256": (PLATFORM_ORPHAN_ACTIVE_AUTHORITY_SHA256),
            "final_commit": CONTROL_COMMIT,
            "final_blob_oid": BLOB_OID,
            "final_content_sha256": (PLATFORM_ORPHAN_ACTIVE_AUTHORITY_SHA256),
            "parent_main_commit": CONTROL_COMMIT,
        },
        "acceptance": {
            "run_id": 32805739095,
            "run_attempt": 1,
            "target_sha": "b031e5e0bd98a95943a03a1529b64e856e1a8aa1",
            "workflow_sha": ("953c1db0712f6ff4d41b7e6a35767d71a2b19c4d"),
            "review_artifact_id": 9548188898,
            "review_artifact_sha256": (
                "sha256:"
                "b7386651bea7c441a038c61c7d143596490a985eca33efaee7d1ede8d9701bc4"
            ),
            "probe_artifact_id": 9548197128,
            "probe_artifact_sha256": (
                "sha256:"
                "c2153d565cb1380fdf9d86fbe777fb104b6a9a4de9ecc181bbc1b84ba12ca75c"
            ),
            "probe_record_sha256": SHA256_A,
            "governance_artifact_id": 9548202666,
            "governance_artifact_sha256": (
                "sha256:"
                "9e1aaf6701d166db0188ad7a9dce784bdaed4034e6a276545dd2a6351b3dab37"
            ),
            "governance_record_sha256": SHA256_A,
        },
        "requests": _requests(),
        "platform_observations": [
            _observation("initial", "2026-09-08T04:36:00Z", platform),
            _observation("final", "2026-09-08T04:36:01Z", platform),
        ],
        "destination_observations": [
            _observation("initial", "2026-09-08T04:36:00Z", destination),
            _observation("final", "2026-09-08T04:36:01Z", destination),
        ],
        "result": {
            "terminalization_blocker_exclusion": ("admitted:run:32809578776"),
            "reconciliation_authority": "not-granted-by-exception",
            "acceptance_result": "unsuccessful",
            "platform_cleanup": "incomplete-with-admitted-orphan",
            "run_terminal": False,
            "release_lineage": "none",
            "package_classification": "exact",
            "package_mutation": "prohibited",
            "live_activation": "prohibited",
            "diagnostics": ["platform-orphan-admitted"],
        },
    }
    candidate["result_digest"] = canonical_sha256(candidate)
    return candidate


def _admit_candidate(
    document: dict[str, Any],
) -> PlatformOrphanReconciliationResult:
    return admit_platform_orphan_reconciliation_result(canonicalize(document))


def _audit_document(candidate: dict[str, Any]) -> dict[str, Any]:
    audit = _active_document()
    audit["schema"] = "workflow-delivery/v3/platform-orphan-exception-audit"
    audit["exception"]["state"] = "consumed"
    audit["consumption"] = {
        "invocation_id": INVOCATION_ID,
        "producer_control_commit": CONTROL_COMMIT,
        "active_authority_sha256": (PLATFORM_ORPHAN_ACTIVE_AUTHORITY_SHA256),
        "result_path": PLATFORM_ORPHAN_RESULT_PATH,
        "result_sha256": canonical_sha256(candidate),
    }
    return audit


def _admit_repository_lifecycle(
    authority_content: bytes,
    result_content: bytes | None,
) -> str:
    if result_content is None:
        admit_platform_orphan_active_authority(authority_content)
        return "active"
    result = admit_platform_orphan_reconciliation_result(result_content)
    admit_platform_orphan_consumed_audit(authority_content, result=result)
    return "consumed"


def _read_regular_repository_file(
    repository: Path,
    relative_path: str,
    *,
    required: bool,
) -> bytes | None:
    path = repository / relative_path
    try:
        mode = path.lstat().st_mode
    except FileNotFoundError:
        assert not required, f"{relative_path} is absent"
        return None
    assert stat.S_ISREG(mode), f"{relative_path} is not a regular file"
    return path.read_bytes()


def _admit_repository_lifecycle_paths(repository: Path) -> str:
    authority_content = _read_regular_repository_file(
        repository,
        PLATFORM_ORPHAN_AUTHORITY_PATH,
        required=True,
    )
    assert authority_content is not None
    result_content = _read_regular_repository_file(
        repository,
        PLATFORM_ORPHAN_RESULT_PATH,
        required=False,
    )
    return _admit_repository_lifecycle(authority_content, result_content)


def test_active_fixture_is_exact_pinned_authority() -> None:
    content = ACTIVE_PATH.read_bytes()
    authority = admit_platform_orphan_active_authority(content)
    blob_oid = hashlib.sha1(
        f"blob {len(content)}\0".encode() + content,
        usedforsecurity=False,
    ).hexdigest()

    assert isinstance(authority, PlatformOrphanActiveAuthority)
    assert canonicalize(authority.to_document()) == content
    assert blob_oid == ACTIVE_BLOB_OID
    assert hashlib.sha256(content).hexdigest() == ACTIVE_RAW_SHA256
    assert authority.authority_digest == f"sha256:{ACTIVE_RAW_SHA256}"
    assert authority.authority_digest == PLATFORM_ORPHAN_ACTIVE_AUTHORITY_SHA256


def test_repository_platform_orphan_lifecycle_is_admissible() -> None:
    state = _admit_repository_lifecycle_paths(REPO_ROOT)

    if state == "active":
        assert state == "active"
        assert (
            REPO_ROOT / PLATFORM_ORPHAN_AUTHORITY_PATH
        ).read_bytes() == ACTIVE_PATH.read_bytes()
    else:
        assert state == "consumed"


@pytest.mark.parametrize(
    "path",
    [PLATFORM_ORPHAN_AUTHORITY_PATH, PLATFORM_ORPHAN_RESULT_PATH],
)
@pytest.mark.parametrize("kind", ["directory", "symlink"])
def test_repository_lifecycle_rejects_non_regular_fixed_path(
    tmp_path: Path,
    path: str,
    kind: str,
) -> None:
    authority = tmp_path / PLATFORM_ORPHAN_AUTHORITY_PATH
    authority.parent.mkdir(parents=True)
    authority.write_bytes(ACTIVE_PATH.read_bytes())
    fixed_path = tmp_path / path
    if fixed_path.exists():
        fixed_path.unlink()
    if kind == "directory":
        fixed_path.mkdir()
    else:
        target = tmp_path / "symlink-target"
        target.write_bytes(ACTIVE_PATH.read_bytes())
        fixed_path.symlink_to(target)

    with pytest.raises(AssertionError, match="is not a regular file"):
        _admit_repository_lifecycle_paths(tmp_path)


def test_mixed_platform_orphan_lifecycle_states_are_rejected() -> None:
    candidate = _candidate_document()
    result_content = canonicalize(candidate)
    audit_content = canonicalize(_audit_document(candidate))

    with pytest.raises((TypeError, ValueError)):
        _admit_repository_lifecycle(ACTIVE_PATH.read_bytes(), result_content)
    with pytest.raises((TypeError, ValueError)):
        _admit_repository_lifecycle(audit_content, None)


def test_platform_orphan_public_exports_are_complete() -> None:
    assert set(records.__all__) >= PLATFORM_ORPHAN_EXPORTS
    assert all(hasattr(records, name) for name in PLATFORM_ORPHAN_EXPORTS)


@pytest.mark.parametrize(
    ("path", "value", "error"),
    [
        (("extra",), True, "not exact"),
        (("run", "id"), "32809578776", "not exact"),
        (
            ("authority", "eligible_after"),
            "2026-09-08T04:35:59+00:00",
            "not exact",
        ),
        (("version",), True, "not exact"),
        (("execution", "jobs"), False, "not exact"),
    ],
)
def test_active_singleton_rejects_unknown_type_and_noncanonical_time(
    path: tuple[str, ...],
    value: object,
    error: str,
) -> None:
    document = _active_document()
    cursor = document
    for name in path[:-1]:
        cursor = cursor[name]
    cursor[path[-1]] = value

    with pytest.raises((TypeError, ValueError), match=error):
        admit_platform_orphan_active_authority(canonicalize(document))


def test_candidate_admits_fixed_order_and_both_digest_preimages() -> None:
    document = _candidate_document()
    result = _admit_candidate(document)
    preimage = deepcopy(document)
    del preimage["result_digest"]

    assert isinstance(result, PlatformOrphanReconciliationResult)
    assert result.result_digest == canonical_sha256(preimage)
    assert result.content_digest == canonical_sha256(document)
    assert result.result_digest != result.content_digest


@pytest.mark.parametrize("mutation", ["schema", "result-digest"])
def test_candidate_rejects_schema_and_result_digest_drift(
    mutation: str,
) -> None:
    document = _candidate_document()
    if mutation == "schema":
        document["schema"] = "workflow-delivery/v3/not-platform-orphan"
        preimage = deepcopy(document)
        preimage.pop("result_digest")
        document["result_digest"] = canonical_sha256(preimage)
    else:
        document["result_digest"] = SHA256_A

    with pytest.raises(ValueError, match=r"schema|result_digest"):
        _admit_candidate(document)


@pytest.mark.parametrize(
    ("classification", "tag_projection", "manifest_version", "tarball"),
    [
        ("exact", "match", "unexpected", EXPECTED_SHA512),
        ("exact", "match", "0.0.0-wdv3-acceptance.5", "sha512:" + ("c" * 128)),
        ("absent", "mismatch", None, None),
        ("partial", "mismatch", None, None),
    ],
)
def test_candidate_rejects_incoherent_package_classification(
    classification: str,
    tag_projection: str,
    manifest_version: str | None,
    tarball: str | None,
) -> None:
    document = _candidate_document()
    for observation in document["destination_observations"]:
        state = observation["state"]
        state["classification"] = classification
        state["tag_projection"] = tag_projection
        state["manifest_version"] = manifest_version
        state["tarball_sha512"] = tarball
        observation["state_sha256"] = canonical_sha256(state)
    document["result"]["package_classification"] = classification
    document["result"]["diagnostics"] = sorted(
        [f"package-{classification}", "platform-orphan-admitted"]
    )
    document["requests"] = _requests(
        manifest_present=classification != "absent"
    )
    preimage = deepcopy(document)
    preimage.pop("result_digest")
    document["result_digest"] = canonical_sha256(preimage)

    with pytest.raises(
        ValueError,
        match=r"inconsistent|incomplete|manifest_version",
    ):
        _admit_candidate(document)


@pytest.mark.parametrize(
    ("classification", "tag_projection", "evidence_state"),
    [
        ("absent", "missing", "absent"),
        ("partial", "missing", "present"),
    ],
)
def test_candidate_admits_existing_destination_classifier_states(
    classification: str,
    tag_projection: str,
    evidence_state: str,
) -> None:
    document = _candidate_document()
    for observation in document["destination_observations"]:
        state = observation["state"]
        state["classification"] = classification
        state["tag_projection"] = tag_projection
        if evidence_state == "absent":
            for name in (
                "manifest_version",
                "tarball_sha512",
                "manifest_digest",
                "package_target_witness_digest",
            ):
                state[name] = None
        observation["state_sha256"] = canonical_sha256(state)
    document["result"]["package_classification"] = classification
    document["result"]["diagnostics"] = sorted(
        [f"package-{classification}", "platform-orphan-admitted"]
    )
    document["requests"] = _requests(
        manifest_present=evidence_state != "absent"
    )
    preimage = deepcopy(document)
    preimage.pop("result_digest")
    document["result_digest"] = canonical_sha256(preimage)

    assert _admit_candidate(document).to_document() == document


@pytest.mark.parametrize("tag_projection", ["match", "mismatch"])
def test_candidate_admits_absent_manifest_with_present_tag_as_conflict(
    tag_projection: str,
) -> None:
    document = _candidate_document()
    for observation in document["destination_observations"]:
        state = observation["state"]
        state["classification"] = "conflicting"
        state["tag_projection"] = tag_projection
        for name in (
            "manifest_version",
            "tarball_sha512",
            "manifest_digest",
            "package_target_witness_digest",
        ):
            state[name] = None
        observation["state_sha256"] = canonical_sha256(state)
    document["result"]["package_classification"] = "conflicting"
    document["result"]["diagnostics"] = [
        "package-conflicting",
        "platform-orphan-admitted",
    ]
    document["requests"] = _requests(manifest_present=False)
    preimage = deepcopy(document)
    preimage.pop("result_digest")
    document["result_digest"] = canonical_sha256(preimage)

    assert _admit_candidate(document).to_document() == document


def test_candidate_admits_differing_manifest_integrity_as_conflict() -> None:
    document = _candidate_document()
    actual = "sha512:" + ("c" * 128)
    for observation in document["destination_observations"]:
        state = observation["state"]
        state["classification"] = "conflicting"
        state["tarball_sha512"] = actual
        observation["state_sha256"] = canonical_sha256(state)
    document["result"]["package_classification"] = "conflicting"
    document["result"]["diagnostics"] = [
        "package-conflicting",
        "platform-orphan-admitted",
    ]
    preimage = deepcopy(document)
    preimage.pop("result_digest")
    document["result_digest"] = canonical_sha256(preimage)

    assert _admit_candidate(document).to_document() == document


@pytest.mark.parametrize(
    "path",
    [
        "/@hcoona/other/-/other-0.0.0-wdv3-acceptance.5.tgz",
        (
            "/@hcoona/hcoona-release-smoke-npm/-/"
            "hcoona-release-smoke-npm-0.0.0-wdv3-acceptance.6.tgz"
        ),
        (
            "/@hcoona/hcoona-release-smoke-npm/-/"
            "hcoona-release-smoke-npm-0.0.0-wdv3-acceptance.5.6.tgz"
        ),
        (
            "/download/@hcoona/hcoona-release-smoke-npm/"
            "0.0.0-wdv3-acceptance.5/id/extra"
        ),
        (
            "/download/@hcoona/hcoona-release-smoke-npm/"
            "0.0.0-wdv3-acceptance.5/id%2Fextra"
        ),
        (
            "/download/@hcoona/hcoona-release-smoke-npm/"
            "0.0.0-wdv3-acceptance.5/%69d"
        ),
        (
            "/@hcoona/hcoona-release-smoke-npm/-/"
            "hcoona-release-smoke-npm-0.0.0-wdv3-acceptance.5%2Etgz"
        ),
        (
            "/@hcoona/hcoona-release-smoke-npm/-/"
            "hcoona-release-smoke-npm-0.0.0-wdv3-acceptance.5.tgz%2Fextra"
        ),
    ],
)
def test_candidate_rejects_unsafe_manifest_derived_tarball_path(
    path: str,
) -> None:
    document = _candidate_document()
    document["requests"] = _requests(tarball_path=path)
    preimage = deepcopy(document)
    preimage.pop("result_digest")
    document["result_digest"] = canonical_sha256(preimage)

    with pytest.raises(ValueError, match=r"tarball path|fixed \.5"):
        _admit_candidate(document)


def test_candidate_derives_safe_nonconventional_tarball_ledger_path() -> None:
    document = _candidate_document()
    path = (
        "/download/@hcoona/hcoona-release-smoke-npm/"
        "0.0.0-wdv3-acceptance.5/7f0a2c91-acde-4b55"
    )
    document["requests"] = _requests(tarball_path=path)
    preimage = deepcopy(document)
    preimage.pop("result_digest")
    document["result_digest"] = canonical_sha256(preimage)

    assert _admit_candidate(document).to_document() == document


def test_candidate_requires_identical_manifest_tarball_path_across_passes() -> (
    None
):
    document = _candidate_document()
    tarballs = [
        request
        for request in document["requests"]
        if request["origin"] == "https://npm.pkg.github.com"
        and request["path"].endswith(".tgz")
    ]
    tarballs[1]["path"] = (
        "/@hcoona/hcoona-release-smoke-npm/-/"
        "hcoona-release-smoke-npm-0.0.0-wdv3-acceptance.6.tgz"
    )
    preimage = deepcopy(document)
    preimage.pop("result_digest")
    document["result_digest"] = canonical_sha256(preimage)

    with pytest.raises(ValueError, match=r"tarball path|fixed \.5"):
        _admit_candidate(document)


@pytest.mark.parametrize(
    ("classification", "tag_projection", "evidence_state"),
    [
        ("conflicting", "match", "present"),
        ("conflicting", "missing", "present"),
        ("conflicting", "missing", "absent"),
    ],
)
def test_candidate_rejects_misclassified_destination_facts(
    classification: str,
    tag_projection: str,
    evidence_state: str,
) -> None:
    document = _candidate_document()
    for observation in document["destination_observations"]:
        state = observation["state"]
        state["classification"] = classification
        state["tag_projection"] = tag_projection
        if evidence_state == "absent":
            for name in (
                "manifest_version",
                "tarball_sha512",
                "manifest_digest",
                "package_target_witness_digest",
            ):
                state[name] = None
        observation["state_sha256"] = canonical_sha256(state)
    document["result"]["package_classification"] = classification
    document["requests"] = _requests(
        manifest_present=evidence_state == "present"
    )
    preimage = deepcopy(document)
    preimage.pop("result_digest")
    document["result_digest"] = canonical_sha256(preimage)

    with pytest.raises(ValueError, match="inconsistent with observed facts"):
        _admit_candidate(document)


@pytest.mark.parametrize(
    "path",
    [
        "/repos/hcoona/three/actions/runs/32809578776?token=secret",
        "/repos/hcoona/three/actions/runs/32809578776#fragment",
        "/repos/hcoona/three/actions/runs/32809578776 bad",
        "/@hcoona%2Gpackage",
        "/packages/é",
    ],
)
def test_candidate_rejects_nonencoded_request_paths(path: str) -> None:
    document = _candidate_document()
    document["requests"][0]["path"] = path
    preimage = deepcopy(document)
    preimage.pop("result_digest")
    document["result_digest"] = canonical_sha256(preimage)

    with pytest.raises(ValueError, match="absolute encoded path"):
        _admit_candidate(document)


def test_candidate_rejects_equal_but_unbound_authority_blob_oids() -> None:
    document = _candidate_document()
    document["authority"]["initial_blob_oid"] = "d" * 40
    document["authority"]["final_blob_oid"] = "d" * 40
    preimage = deepcopy(document)
    preimage.pop("result_digest")
    document["result_digest"] = canonical_sha256(preimage)

    with pytest.raises(ValueError, match=r"blob OID.*active singleton"):
        _admit_candidate(document)


@pytest.mark.parametrize(
    "mutation",
    ["missing", "extra", "status", "origin", "page-count", "page-cursor"],
)
def test_candidate_rejects_incomplete_or_off_policy_request_ledger(
    mutation: str,
) -> None:
    document = _candidate_document()
    if mutation == "missing":
        del document["requests"][4]
        for sequence, request in enumerate(document["requests"], start=1):
            request["sequence"] = sequence
    elif mutation == "extra":
        extra = deepcopy(document["requests"][-1])
        extra["sequence"] += 1
        extra["path"] = "/unrelated"
        document["requests"].append(extra)
    elif mutation == "status":
        document["requests"][4]["http_status"] = 599
    elif mutation == "origin":
        document["requests"][-1]["origin"] = (
            "https://objects.githubusercontent.com"
        )
    elif mutation == "page-count":
        for observation in document["platform_observations"]:
            observation["state"]["jobs_page_count"] = 2
            observation["state_sha256"] = canonical_sha256(observation["state"])
    else:
        jobs = next(
            request
            for request in document["requests"]
            if request["path"].endswith("/jobs")
        )
        jobs["page_cursor"] = "filter=all&per_page=100&page=2"
    preimage = deepcopy(document)
    preimage.pop("result_digest")
    document["result_digest"] = canonical_sha256(preimage)

    with pytest.raises(ValueError, match="fixed observation ledger"):
        _admit_candidate(document)


def test_candidate_bounds_pagination_before_expanding_expected_ledger() -> None:
    document = _candidate_document()
    page_count = len(document["requests"]) + 1
    for observation in document["platform_observations"]:
        observation["state"]["jobs_page_count"] = page_count
        observation["state_sha256"] = canonical_sha256(observation["state"])
    preimage = deepcopy(document)
    preimage.pop("result_digest")
    document["result_digest"] = canonical_sha256(preimage)

    with pytest.raises(ValueError, match="page counts exceed request ledger"):
        _admit_candidate(document)


@pytest.mark.parametrize(
    ("collection", "initial_at", "final_at"),
    [
        (
            "platform_observations",
            "2026-09-08T04:35:58Z",
            "2026-09-08T04:36:01Z",
        ),
        (
            "platform_observations",
            "2026-09-08T04:36:01Z",
            "2026-09-08T04:36:00Z",
        ),
        (
            "destination_observations",
            "2026-09-08T04:36:00Z",
            "2026-09-08T04:36:02Z",
        ),
    ],
)
def test_candidate_rejects_observations_outside_invocation_order(
    collection: str,
    initial_at: str,
    final_at: str,
) -> None:
    document = _candidate_document()
    document[collection][0]["observed_at"] = initial_at
    document[collection][1]["observed_at"] = final_at
    preimage = deepcopy(document)
    preimage.pop("result_digest")
    document["result_digest"] = canonical_sha256(preimage)

    with pytest.raises(ValueError, match=r"timestamps.*invocation order"):
        _admit_candidate(document)


def test_candidate_rejects_interleaved_observation_phases() -> None:
    document = _candidate_document()
    document["destination_observations"][0]["observed_at"] = (
        "2026-09-08T04:36:01Z"
    )
    document["platform_observations"][1]["observed_at"] = "2026-09-08T04:36:00Z"
    preimage = deepcopy(document)
    preimage.pop("result_digest")
    document["result_digest"] = canonical_sha256(preimage)

    with pytest.raises(ValueError, match="complete phase order"):
        _admit_candidate(document)


@pytest.mark.parametrize(
    "mutation",
    ["unknown-member", "wrong-array-order", "wrong-state-digest"],
)
def test_candidate_rejects_closed_order_and_state_digest_drift(
    mutation: str,
) -> None:
    document = _candidate_document()
    if mutation == "unknown-member":
        document["result"]["extra"] = True
    elif mutation == "wrong-array-order":
        document["platform_observations"].reverse()
    else:
        document["platform_observations"][0]["state_sha256"] = SHA256_A
    preimage = deepcopy(document)
    preimage.pop("result_digest")
    document["result_digest"] = canonical_sha256(preimage)

    with pytest.raises(
        ValueError,
        match=r"unknown closed fields|phase must be|state_sha256",
    ):
        _admit_candidate(document)


def test_consumed_audit_requires_reciprocal_candidate_bindings() -> None:
    candidate_document = _candidate_document()
    candidate = _admit_candidate(candidate_document)
    audit = admit_platform_orphan_consumed_audit(
        canonicalize(_audit_document(candidate_document)),
        result=candidate,
    )

    assert isinstance(audit, PlatformOrphanConsumedAudit)
    audit_document = audit.to_document()
    exception = audit_document["exception"]
    assert isinstance(exception, dict)
    assert exception["state"] == "consumed"


def test_consumed_audit_rejects_result_digest_cross_binding_drift() -> None:
    candidate_document = _candidate_document()
    candidate = _admit_candidate(candidate_document)
    audit_document = _audit_document(candidate_document)
    audit_document["consumption"]["result_sha256"] = SHA256_A

    with pytest.raises(ValueError, match="complete result digest"):
        admit_platform_orphan_consumed_audit(
            canonicalize(audit_document),
            result=candidate,
        )


@pytest.mark.parametrize(
    ("field", "value", "error"),
    [
        (
            "invocation_id",
            "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
            "consumption invocation",
        ),
        ("producer_control_commit", "e" * 40, "producer control"),
        ("active_authority_sha256", SHA256_A, "active authority"),
    ],
)
def test_consumed_audit_rejects_reciprocal_binding_drift(
    field: str,
    value: str,
    error: str,
) -> None:
    candidate_document = _candidate_document()
    candidate = _admit_candidate(candidate_document)
    audit_document = _audit_document(candidate_document)
    audit_document["consumption"][field] = value

    with pytest.raises(ValueError, match=error):
        admit_platform_orphan_consumed_audit(
            canonicalize(audit_document),
            result=candidate,
        )


def test_consumed_audit_rejects_wrong_schema() -> None:
    candidate_document = _candidate_document()
    candidate = _admit_candidate(candidate_document)
    audit_document = _audit_document(candidate_document)
    audit_document["schema"] = "workflow-delivery/v3/not-platform-orphan"

    with pytest.raises(ValueError, match="schema"):
        admit_platform_orphan_consumed_audit(
            canonicalize(audit_document),
            result=candidate,
        )


def test_consumed_audit_revalidates_constructible_result_wrapper() -> None:
    candidate_document = _candidate_document()
    audit_document = _audit_document(candidate_document)
    invalid_result = PlatformOrphanReconciliationResult(
        {"result_digest": SHA256_A}
    )

    with pytest.raises(ValueError, match="missing required fields"):
        admit_platform_orphan_consumed_audit(
            canonicalize(audit_document),
            result=invalid_result,
        )
