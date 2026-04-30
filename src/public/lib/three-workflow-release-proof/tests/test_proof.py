"""Tests for workflow-release proof wrapper and classifier helpers."""

from __future__ import annotations

import json
from copy import deepcopy
from pathlib import Path
from typing import Any, cast

import pytest
from three_workflow_release_contracts import validate_contract
from three_workflow_release_proof import (
    ProofError,
    classify_github_release_observations,
    classify_immutable_observations,
    github_release_asset_proofs,
    immutable_proofs,
)

FIXTURE_ROOT = (
    Path(__file__).parents[1]
    / ".."
    / "three-workflow-release-contracts"
    / "tests"
    / "fixtures"
    / "valid"
).resolve()


def _load(name: str) -> dict[str, Any]:
    """Load a contract fixture."""
    return json.loads((FIXTURE_ROOT / name).read_text(encoding="utf-8"))


def _run() -> dict[str, Any]:
    """Return admissible live run provenance."""
    return {
        "repository": "hcoona/three",
        "workflow": "release-publish-node.yml",
        "run-id": 123,
        "run-attempt": 1,
        "head-sha": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        "dry-run": False,
        "validation-only": False,
        "live": True,
    }


def test_immutable_proof_wraps_build_receipt_for_versioned_binding() -> None:
    """Emit a closed immutable proof bound to publish node, artifact, name, version."""
    proofs = immutable_proofs(
        plan=_load("release-plan.json"),
        build_result=_load("build-result.json"),
        publish_node_id="publish-node/nuget",
        run=_run(),
        build_result_artifact_name="release-build-result-v1-123-1-abc",
        build_result_artifact_id=123,
        bundle_artifact_name="release-build-bundle-v1-123-1-abc",
    )

    assert len(proofs) == 1
    proof = proofs[0]
    validate_contract(proof)
    assert proof["binding"] == {
        "publish-node-id": "publish-node/nuget",
        "artifact-id": "artifact/package",
        "package-name": "Example",
        "version": "1.2.3",
    }


@pytest.mark.parametrize(
    ("field", "bad_value"),
    [
        ("plan-id", "plan/other"),
        ("project-id", "other-project"),
        ("variant-id", "variant/other"),
    ],
)
def test_immutable_proof_rejects_mismatched_build_result_identity(
    field: str,
    bad_value: str,
) -> None:
    """Build receipts cannot be rebound across plan, project, or variant."""
    build_result = _load("build-result.json")
    build_result[field] = bad_value

    with pytest.raises(ProofError, match="build result"):
        immutable_proofs(
            plan=_load("release-plan.json"),
            build_result=build_result,
            publish_node_id="publish-node/nuget",
            run=_run(),
            build_result_artifact_name="release-build-result-v1-123-1-abc",
            build_result_artifact_id=123,
            bundle_artifact_name="release-build-bundle-v1-123-1-abc",
        )


def test_immutable_classification_requires_admissible_digest_proof() -> None:
    """Fail closed instead of trusting same-name immutable registry members."""
    plan = _load("release-plan.json")
    remote = {
        "publish-node/nuget": [
            {
                "filename": "Example.1.2.3.nupkg",
                "sha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            }
        ]
    }

    with pytest.raises(ProofError) as error:
        classify_immutable_observations(
            plan=plan,
            remote_members=remote,
            proofs=[],
        )

    assert error.value.code == "IMMUTABLE_PROOF_UNAVAILABLE"


def test_immutable_classification_detects_exact_and_partial_states() -> None:
    """Classify exact immutable replay and leave partial as a planner error input."""
    plan = _load("release-plan.json")
    proof = immutable_proofs(
        plan=plan,
        build_result=_load("build-result.json"),
        publish_node_id="publish-node/nuget",
        run=_run(),
        build_result_artifact_name="release-build-result-v1-123-1-abc",
        build_result_artifact_id=123,
        bundle_artifact_name="release-build-bundle-v1-123-1-abc",
    )[0]
    exact = {
        "publish-node/nuget": [
            {
                "filename": "Example.1.2.3.nupkg",
                "sha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            }
        ]
    }

    assert classify_immutable_observations(
        plan=plan,
        remote_members=exact,
        proofs=[proof],
        build_result_receipts=[_immutable_build_result_receipt()],
    ) == {"publish-node/nuget": "exact-satisfied"}

    plan = deepcopy(plan)
    node = plan["graph"]["publish-nodes"]["publish-node/nuget"]
    node["artifact-ids"].append("artifact/symbols")
    node["projection"]["final-distribution-filenames-by-artifact-id"][
        "artifact/symbols"
    ] = "Example.1.2.3.snupkg"
    assert classify_immutable_observations(
        plan=plan,
        remote_members=exact,
        proofs=[proof],
        build_result_receipts=[_immutable_build_result_receipt()],
    ) == {"publish-node/nuget": "partial"}


@pytest.mark.parametrize(
    "run_update",
    [
        {"dry-run": True},
        {"validation-only": True},
        {"live": False},
    ],
)
def test_immutable_classification_ignores_non_admissible_wrappers(
    run_update: dict[str, bool],
) -> None:
    """Dry-run, validation-only, and non-live wrappers do not block valid proof."""
    plan = _load("release-plan.json")
    valid = immutable_proofs(
        plan=plan,
        build_result=_load("build-result.json"),
        publish_node_id="publish-node/nuget",
        run=_run(),
        build_result_artifact_name="release-build-result-v1-123-1-abc",
        build_result_artifact_id=123,
        bundle_artifact_name="release-build-bundle-v1-123-1-abc",
    )[0]
    ignored = cast(dict[str, Any], deepcopy(valid))
    cast(dict[str, Any], ignored["run"]).update(run_update)
    cast(dict[str, Any], ignored["artifact"])["sha256"] = (
        "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd"
    )

    assert classify_immutable_observations(
        plan=plan,
        remote_members=_immutable_remote_exact(),
        proofs=[ignored, valid],
        build_result_receipts=[_immutable_build_result_receipt()],
    ) == {"publish-node/nuget": "exact-satisfied"}


@pytest.mark.parametrize(
    ("family", "filename"),
    [
        ("npm", "example-1.2.3.tgz"),
        ("rubygems", "example-1.2.3.gem"),
    ],
)
def test_immutable_classification_supports_npm_and_rubygems_exact(
    family: str,
    filename: str,
) -> None:
    """Classify exact replay for immutable npm and RubyGems projections."""
    plan = _immutable_plan_for_family(family, filename)
    node_id = f"publish-node/{family}"
    proof = immutable_proofs(
        plan=plan,
        build_result=_load("build-result.json"),
        publish_node_id=node_id,
        run=_run(),
        build_result_artifact_name="release-build-result-v1-123-1-abc",
        build_result_artifact_id=123,
        bundle_artifact_name="release-build-bundle-v1-123-1-abc",
    )[0]

    assert classify_immutable_observations(
        plan=plan,
        remote_members={
            node_id: [
                {
                    "filename": filename,
                    "sha256": (
                        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
                        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
                    ),
                }
            ]
        },
        proofs=[proof],
        build_result_receipts=[_immutable_build_result_receipt()],
    ) == {node_id: "exact-satisfied"}


@pytest.mark.parametrize(
    ("family", "filename"),
    [
        ("npm", "example-1.2.3.tgz"),
        ("rubygems", "example-1.2.3.gem"),
    ],
)
def test_immutable_classification_marks_npm_and_rubygems_absent(
    family: str,
    filename: str,
) -> None:
    """Absent immutable npm and RubyGems versions do not require proof lookup."""
    plan = _immutable_plan_for_family(family, filename)

    assert classify_immutable_observations(
        plan=plan,
        remote_members={},
        proofs=[],
        build_result_receipts=[],
    ) == {f"publish-node/{family}": "absent"}


@pytest.mark.parametrize(
    ("family", "filename"),
    [
        ("npm", "example-1.2.3.tgz"),
        ("rubygems", "example-1.2.3.gem"),
    ],
)
def test_immutable_classification_requires_npm_and_rubygems_proofs(
    family: str,
    filename: str,
) -> None:
    """Present immutable npm and RubyGems members still require digest proof."""
    plan = _immutable_plan_for_family(family, filename)
    node_id = f"publish-node/{family}"

    with pytest.raises(ProofError) as error:
        classify_immutable_observations(
            plan=plan,
            remote_members={
                node_id: [
                    {
                        "filename": filename,
                        "sha256": (
                            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
                            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
                        ),
                    }
                ]
            },
            proofs=[],
            build_result_receipts=[],
        )

    assert error.value.code == "IMMUTABLE_PROOF_UNAVAILABLE"


@pytest.mark.parametrize(
    ("family", "filename"),
    [
        ("npm", "example-1.2.3.tgz"),
        ("rubygems", "example-1.2.3.gem"),
    ],
)
def test_immutable_classification_detects_npm_and_rubygems_conflicts(
    family: str,
    filename: str,
) -> None:
    """Digest mismatches are conflicting for npm and RubyGems remote members."""
    plan = _immutable_plan_for_family(family, filename)
    node_id = f"publish-node/{family}"
    proof = immutable_proofs(
        plan=plan,
        build_result=_load("build-result.json"),
        publish_node_id=node_id,
        run=_run(),
        build_result_artifact_name="release-build-result-v1-123-1-abc",
        build_result_artifact_id=123,
        bundle_artifact_name="release-build-bundle-v1-123-1-abc",
    )[0]

    assert classify_immutable_observations(
        plan=plan,
        remote_members={
            node_id: [
                {
                    "filename": filename,
                    "sha256": (
                        "dddddddddddddddddddddddddddddddd"
                        "dddddddddddddddddddddddddddddddd"
                    ),
                }
            ]
        },
        proofs=[proof],
        build_result_receipts=[_immutable_build_result_receipt()],
    ) == {node_id: "conflicting"}


def test_immutable_classification_requires_referenced_build_result_receipt() -> None:
    """Cached immutable wrappers must resolve their referenced build-result receipt."""
    plan = _load("release-plan.json")
    proof = immutable_proofs(
        plan=plan,
        build_result=_load("build-result.json"),
        publish_node_id="publish-node/nuget",
        run=_run(),
        build_result_artifact_name="release-build-result-v1-123-1-abc",
        build_result_artifact_id=123,
        bundle_artifact_name="release-build-bundle-v1-123-1-abc",
    )[0]

    with pytest.raises(ProofError) as error:
        classify_immutable_observations(
            plan=plan,
            remote_members=_immutable_remote_exact(),
            proofs=[proof],
            build_result_receipts=[],
        )

    assert error.value.code == "IMMUTABLE_PROOF_UNAVAILABLE"


@pytest.mark.parametrize(
    "receipt_update",
    [
        {"build-result-artifact-name": "release-build-result-v1-123-1-other"},
        {"build-result-artifact-id": 456},
        {"build-result.artifacts.artifact/package": None},
        {
            "build-result.artifacts.artifact/package.sha256": (
                "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd"
            )
        },
        {"build-result.artifacts.artifact/package.byte-size": 999},
    ],
)
def test_immutable_classification_rejects_unresolved_build_result_receipt(
    receipt_update: dict[str, Any],
) -> None:
    """Referenced build-result receipts must prove the current artifact bytes."""
    plan = _load("release-plan.json")
    proof = immutable_proofs(
        plan=plan,
        build_result=_load("build-result.json"),
        publish_node_id="publish-node/nuget",
        run=_run(),
        build_result_artifact_name="release-build-result-v1-123-1-abc",
        build_result_artifact_id=123,
        bundle_artifact_name="release-build-bundle-v1-123-1-abc",
    )[0]
    receipt = _immutable_build_result_receipt()
    _apply_dotted_updates(receipt, receipt_update)

    with pytest.raises(ProofError) as error:
        classify_immutable_observations(
            plan=plan,
            remote_members=_immutable_remote_exact(),
            proofs=[proof],
            build_result_receipts=[receipt],
        )

    assert error.value.code == "IMMUTABLE_PROOF_UNAVAILABLE"


@pytest.mark.parametrize(
    ("field_path", "bad_value"),
    [
        (("run", "head-sha"), "dddddddddddddddddddddddddddddddddddddddd"),
        (("plan-id",), "plan/other"),
        (("project-id",), "other-project"),
        (("variant-id",), "variant/other"),
    ],
)
def test_immutable_classification_rejects_stale_or_rebound_proofs(
    field_path: tuple[str, ...],
    bad_value: str,
) -> None:
    """Proof lookup cannot replay wrappers from another run, plan, project, or variant."""
    plan = _load("release-plan.json")
    proof = immutable_proofs(
        plan=plan,
        build_result=_load("build-result.json"),
        publish_node_id="publish-node/nuget",
        run=_run(),
        build_result_artifact_name="release-build-result-v1-123-1-abc",
        build_result_artifact_id=123,
        bundle_artifact_name="release-build-bundle-v1-123-1-abc",
    )[0]
    target: Any = proof
    for step in field_path[:-1]:
        target = target[step]
    target[field_path[-1]] = bad_value

    with pytest.raises(ProofError) as error:
        classify_immutable_observations(
            plan=plan,
            remote_members={
                "publish-node/nuget": [
                    {
                        "filename": "Example.1.2.3.nupkg",
                        "sha256": (
                            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
                            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
                        ),
                    }
                ]
            },
            proofs=[proof],
            build_result_receipts=[_immutable_build_result_receipt()],
        )

    assert error.value.code == "IMMUTABLE_PROOF_UNAVAILABLE"


def test_github_release_asset_proof_wraps_publish_attestation_evidence() -> None:
    """Emit closed asset proof wrappers from publish request/result evidence."""
    request = _load("publish-request.json")
    result = _load("publish-result.json")
    result["evidence"] = {
        "asset-attestations": {
            "artifact/package": {
                "asset-name": "Example.1.2.3.nupkg",
                "sha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                "predicate-type": "https://slsa.dev/provenance/v1",
                "signer-workflow": "hcoona/three/.github/workflows/release-publish-node.yml",
                "source-repository": "hcoona/three",
                "source-digest": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "attestation-id": "att-1",
                "attestation-url": "https://github.com/hcoona/three/attestations/1",
                "bundle-path": "attestations/Example.1.2.3.nupkg.json",
            },
            "artifact/symbols": {
                "asset-name": "Example.1.2.3.snupkg",
                "sha256": "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
                "predicate-type": "https://slsa.dev/provenance/v1",
                "signer-workflow": "hcoona/three/.github/workflows/release-publish-node.yml",
                "source-repository": "hcoona/three",
                "source-digest": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "attestation-id": "att-2",
                "attestation-url": "https://github.com/hcoona/three/attestations/2",
                "bundle-path": "attestations/Example.1.2.3.snupkg.json",
            },
        }
    }

    proofs = github_release_asset_proofs(
        publish_request=request,
        publish_result=result,
        run=_run(),
    )

    assert len(proofs) == 2
    for proof in proofs:
        validate_contract(proof)


@pytest.mark.parametrize(
    ("field", "bad_value"),
    [
        ("plan-id", "plan/other"),
        ("project-id", "other-project"),
        ("publish-node-id", "publish-node/other"),
        ("target-instance-snapshot-id", "github-release/other"),
    ],
)
def test_github_release_asset_proof_rejects_publish_result_mismatch(
    field: str,
    bad_value: str,
) -> None:
    """Publish result identity must match the request before proof emission."""
    request = _load("publish-request.json")
    result = _github_release_publish_result_with_attestations()
    result[field] = bad_value

    with pytest.raises(ProofError, match="publish result"):
        github_release_asset_proofs(
            publish_request=request,
            publish_result=result,
            run=_run(),
        )


def test_github_release_asset_proof_rejects_publish_result_identity_mismatch() -> None:
    """Publish result release identity must match the request release tag."""
    request = _load("publish-request.json")
    result = _github_release_publish_result_with_attestations()
    result["resolved-publish-identity"] = {"release-tag": "release/example/v9.9.9"}

    with pytest.raises(ProofError, match="publish result identity"):
        github_release_asset_proofs(
            publish_request=request,
            publish_result=result,
            run=_run(),
        )


@pytest.mark.parametrize(
    "predicate_type",
    [None, "https://spdx.dev/Document"],
)
def test_github_release_asset_proof_requires_slsa_predicate_type(
    predicate_type: str | None,
) -> None:
    """Asset proof evidence must preserve the verified attestation predicate."""
    request = _load("publish-request.json")
    result = _github_release_publish_result_with_attestations()
    package_evidence = result["evidence"]["asset-attestations"]["artifact/package"]
    if predicate_type is None:
        del package_evidence["predicate-type"]
    else:
        package_evidence["predicate-type"] = predicate_type

    with pytest.raises(ProofError, match="predicate type"):
        github_release_asset_proofs(
            publish_request=request,
            publish_result=result,
            run=_run(),
        )


def test_github_release_classification_uses_remote_verification_for_exact() -> None:
    """Require exact state, asset set, labels, digest, signer, and source."""
    plan = _load("release-plan.json")
    remote = _github_release_remote_exact()

    assert classify_github_release_observations(
        plan=plan,
        remote_releases=remote,
        proofs=[],
    ) == {"publish-node/gh": "exact-satisfied"}


def test_github_release_asset_proofs_are_optional_corroboration() -> None:
    """A missing optional proof wrapper does not block current remote proof."""
    plan = _load("release-plan.json")
    proofs = [_load("github-release-asset-proof.json")]
    proofs[0]["run"] = _run()
    remote = _github_release_remote_exact()

    assert classify_github_release_observations(
        plan=plan,
        remote_releases=remote,
        proofs=proofs,
    ) == {"publish-node/gh": "exact-satisfied"}

    proofs.append(_load("github-release-asset-proof.json"))
    proofs[1]["binding"] = {
        "publish-node-id": "publish-node/gh",
        "artifact-id": "artifact/symbols",
        "release-tag": "release/example/v1.2.3",
        "asset-name": "Example.1.2.3.snupkg",
    }
    proofs[1]["artifact"] = {
        "bundle-relative-path": "dist/Example.1.2.3.snupkg",
        "byte-size": 456,
        "sha256": "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
    }
    proofs[1]["attestation"]["subject-name"] = "Example.1.2.3.snupkg"
    proofs[1]["attestation"]["subject-digest"] = (
        "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"
    )
    validate_contract(proofs[1])

    assert classify_github_release_observations(
        plan=plan,
        remote_releases=remote,
        proofs=proofs,
    ) == {"publish-node/gh": "exact-satisfied"}


def test_github_release_inconsistent_asset_proof_blocks_exact() -> None:
    """Admissible cached wrappers must corroborate current remote evidence."""
    plan = _load("release-plan.json")
    proofs = _github_release_asset_proof_pair()
    proofs[0]["artifact"]["sha256"] = (
        "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd"
    )
    proofs[0]["attestation"]["subject-digest"] = (
        "sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd"
    )

    assert classify_github_release_observations(
        plan=plan,
        remote_releases=_github_release_remote_exact(),
        proofs=proofs,
    ) == {"publish-node/gh": "partial"}


def test_github_release_conflicting_asset_proofs_fail_closed() -> None:
    """Multiple admissible wrappers for one binding cannot disagree."""
    plan = _load("release-plan.json")
    proofs = _github_release_asset_proof_pair()
    conflicting = deepcopy(proofs[0])
    conflicting["artifact"]["sha256"] = (
        "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd"
    )
    conflicting["attestation"]["subject-digest"] = (
        "sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd"
    )
    proofs.append(conflicting)

    with pytest.raises(ProofError) as error:
        classify_github_release_observations(
            plan=plan,
            remote_releases=_github_release_remote_exact(),
            proofs=proofs,
        )

    assert error.value.code == "GITHUB_RELEASE_ASSET_PROOF_CONFLICT"


@pytest.mark.parametrize(
    "remote_update",
    [
        {"publish-node/gh.assets": []},
        {"publish-node/gh.release-state": "release"},
    ],
)
def test_github_release_conflicting_asset_proofs_fail_before_remote_classification(
    remote_update: dict[str, Any],
) -> None:
    """Conflicting current-plan wrappers fail closed even for non-exact remotes."""
    plan = _load("release-plan.json")
    proofs = _github_release_asset_proof_pair()
    conflicting = deepcopy(proofs[0])
    conflicting["artifact"]["sha256"] = (
        "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd"
    )
    conflicting["attestation"]["subject-digest"] = (
        "sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd"
    )
    proofs.append(conflicting)
    remote = _github_release_remote_exact()
    _apply_dotted_updates(remote, remote_update)

    with pytest.raises(ProofError) as error:
        classify_github_release_observations(
            plan=plan,
            remote_releases=remote,
            proofs=proofs,
        )

    assert error.value.code == "GITHUB_RELEASE_ASSET_PROOF_CONFLICT"


@pytest.mark.parametrize(
    "run_update",
    [
        {"dry-run": True},
        {"validation-only": True},
        {"live": False},
    ],
)
def test_github_release_ignores_non_admissible_asset_wrappers(
    run_update: dict[str, bool],
) -> None:
    """Non-admissible optional wrappers do not block current remote proof."""
    proofs = _github_release_asset_proof_pair()
    for proof in proofs:
        proof["run"].update(run_update)
    proofs[0]["artifact"]["sha256"] = (
        "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd"
    )
    proofs[0]["attestation"]["subject-digest"] = (
        "sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd"
    )

    assert classify_github_release_observations(
        plan=_load("release-plan.json"),
        remote_releases=_github_release_remote_exact(),
        proofs=proofs,
    ) == {"publish-node/gh": "exact-satisfied"}


@pytest.mark.parametrize(
    ("attestation_field", "bad_value"),
    [
        ("subject-name", "Example.1.2.3-renamed.nupkg"),
        ("signer-workflow", "hcoona/three/.github/workflows/other.yml"),
        ("source-repository", "hcoona/other"),
        ("source-digest", "dddddddddddddddddddddddddddddddddddddddd"),
    ],
)
def test_github_release_conflicting_attestation_proof_fields_fail_closed(
    attestation_field: str,
    bad_value: str,
) -> None:
    """Duplicate wrappers cannot disagree on attestation fields used for exactness."""
    plan = _load("release-plan.json")
    proofs = _github_release_asset_proof_pair()
    conflicting = deepcopy(proofs[0])
    conflicting["attestation"][attestation_field] = bad_value
    proofs.append(conflicting)

    with pytest.raises(ProofError) as error:
        classify_github_release_observations(
            plan=plan,
            remote_releases=_github_release_remote_exact(),
            proofs=proofs,
        )

    assert error.value.code == "GITHUB_RELEASE_ASSET_PROOF_CONFLICT"


@pytest.mark.parametrize(
    ("field", "bad_value"),
    [
        ("plan-id", "plan/other"),
        ("project-id", "other-project"),
        ("variant-id", "variant/other"),
        ("run.head-sha", "ffffffffffffffffffffffffffffffffffffffff"),
    ],
)
def test_github_release_stale_asset_proofs_are_ignored(
    field: str,
    bad_value: str,
) -> None:
    """Stale same-binding wrappers are not admissible for the current plan."""
    plan = _load("release-plan.json")
    proofs = _github_release_asset_proof_pair()
    for proof in proofs:
        if field == "run.head-sha":
            proof["run"]["head-sha"] = bad_value
        else:
            proof[field] = bad_value

    assert classify_github_release_observations(
        plan=plan,
        remote_releases=_github_release_remote_exact(),
        proofs=proofs,
    ) == {"publish-node/gh": "exact-satisfied"}


@pytest.mark.parametrize(
    "digest_value",
    [
        None,
        "sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd",
        "sha256:BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
        "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
        "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbg",
        "sha512:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
    ],
)
def test_github_release_exact_requires_remote_digest(
    digest_value: str | None,
) -> None:
    """Missing or mismatched GitHub asset digest cannot be exact-satisfied."""
    plan = _load("release-plan.json")
    proofs = _github_release_asset_proof_pair()
    remote = _github_release_remote_exact()
    package_asset = remote["publish-node/gh"]["assets"][0]
    if digest_value is None:
        del package_asset["digest"]
        del package_asset["sha256"]
    else:
        package_asset["digest"] = digest_value
        del package_asset["sha256"]

    assert classify_github_release_observations(
        plan=plan,
        remote_releases=remote,
        proofs=proofs,
    ) == {"publish-node/gh": "partial"}


@pytest.mark.parametrize(
    "verification_update",
    [
        None,
        {"verified": False},
        {"signer-workflow": "hcoona/three/.github/workflows/other.yml"},
    ],
)
def test_github_release_exact_requires_current_attestation_verification(
    verification_update: dict[str, Any] | None,
) -> None:
    """Cached proof wrappers alone cannot make a remote GitHub asset exact."""
    plan = _load("release-plan.json")
    remote = _github_release_remote_exact()
    package_asset = remote["publish-node/gh"]["assets"][0]
    if verification_update is None:
        del package_asset["verified-attestation"]
    else:
        package_asset["verified-attestation"].update(verification_update)

    assert classify_github_release_observations(
        plan=plan,
        remote_releases=remote,
        proofs=_github_release_asset_proof_pair(),
    ) == {"publish-node/gh": "partial"}


def test_github_release_classification_rejects_unknown_remote_state() -> None:
    """Malformed GitHub Release state is unclassifiable, not partial."""
    plan = _load("release-plan.json")
    remote = _github_release_remote_exact()
    remote["publish-node/gh"]["release-state"] = "draft"

    with pytest.raises(ProofError) as error:
        classify_github_release_observations(
            plan=plan,
            remote_releases=remote,
            proofs=_github_release_asset_proof_pair(),
        )

    assert error.value.code == "REMOTE_NORMALIZATION_FAILED"
    assert error.value.details["publish-node-id"] == "publish-node/gh"
    assert error.value.details["release-tag"] == "release/example/v1.2.3"


def test_github_release_classification_conflicts_on_official_non_exact() -> None:
    """An already-official same-tag non-exact release fails closed."""
    plan = _load("release-plan.json")
    node = plan["graph"]["publish-nodes"]["publish-node/gh"]
    node["desired-publish-state"] = {"release-state": "release"}
    remote = {
        "publish-node/gh": {
            "release-state": "release",
            "assets": [],
        }
    }

    assert classify_github_release_observations(
        plan=plan,
        remote_releases=remote,
        proofs=[],
    ) == {"publish-node/gh": "conflicting"}


def _github_release_publish_result_with_attestations() -> dict[str, Any]:
    """Return a publish-result fixture with GitHub Release attestation evidence."""
    result = _load("publish-result.json")
    result["evidence"] = {
        "asset-attestations": {
            "artifact/package": {
                "asset-name": "Example.1.2.3.nupkg",
                "sha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                "predicate-type": "https://slsa.dev/provenance/v1",
                "signer-workflow": "hcoona/three/.github/workflows/release-publish-node.yml",
                "source-repository": "hcoona/three",
                "source-digest": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "attestation-id": "att-1",
                "attestation-url": "https://github.com/hcoona/three/attestations/1",
                "bundle-path": "attestations/Example.1.2.3.nupkg.json",
            },
            "artifact/symbols": {
                "asset-name": "Example.1.2.3.snupkg",
                "sha256": "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
                "predicate-type": "https://slsa.dev/provenance/v1",
                "signer-workflow": "hcoona/three/.github/workflows/release-publish-node.yml",
                "source-repository": "hcoona/three",
                "source-digest": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "attestation-id": "att-2",
                "attestation-url": "https://github.com/hcoona/three/attestations/2",
                "bundle-path": "attestations/Example.1.2.3.snupkg.json",
            },
        }
    }
    return result


def _immutable_build_result_receipt() -> dict[str, Any]:
    """Return a closed build-result receipt input for immutable proof lookup."""
    return {
        "build-result-artifact-name": "release-build-result-v1-123-1-abc",
        "build-result-artifact-id": 123,
        "build-result": _load("build-result.json"),
    }


def _immutable_remote_exact() -> dict[str, Any]:
    """Return exact remote immutable package members for the NuGet publish node."""
    return {
        "publish-node/nuget": [
            {
                "filename": "Example.1.2.3.nupkg",
                "sha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            }
        ]
    }


def _immutable_plan_for_family(family: str, filename: str) -> dict[str, Any]:
    """Return a valid one-artifact immutable plan for an npm or RubyGems target."""
    plan = deepcopy(_load("release-plan.json"))
    graph = plan["graph"]
    publish_nodes = graph["publish-nodes"]
    node = publish_nodes.pop("publish-node/nuget")
    node_id = f"publish-node/{family}"
    snapshot_id = f"{family}/public"
    package_name = "example"
    concrete_kind = {"npm": "npm-package", "rubygems": "rubygem"}[family]
    contract_id = {"npm": "npm-publish", "rubygems": "rubygems-publish"}[family]
    host = {"npm": "registry.npmjs.org", "rubygems": "rubygems.org"}[family]
    topology = (
        "external-oidc-reusable-workflow"
        if family == "rubygems"
        else "external-oidc-entry-workflow"
    )

    node["artifact-ids"] = ["artifact/package"]
    node["publish-node-id"] = node_id
    node["target-instance-snapshot-id"] = snapshot_id
    projection: dict[str, Any] = {
        "final-distribution-filenames-by-artifact-id": {"artifact/package": filename}
    }
    if family == "npm":
        projection["package-name"] = package_name
    node["projection"] = projection
    node["resolved-publish-identity"] = {
        "package-name": package_name,
        "version": "1.2.3",
    }
    publish_nodes[node_id] = node

    graph["artifacts"]["artifact/package"]["concrete-kind"] = concrete_kind
    graph["target-instance-snapshots"].pop("nuget/github-packages")
    graph["target-instance-snapshots"][snapshot_id] = {
        "capabilities": {
            "credential-posture": "oidc",
            "mutability": "immutable",
            "name-uniqueness-scope": "package-name",
            "profile-coexistence-rule": "requires-distinct-name",
            "publish-topology": topology,
            "version-uniqueness-rule": "package-name-plus-version",
        },
        "catalog-ref": snapshot_id,
        "contract": {
            "aggregate-rules": {
                "cross-variant-policy": "forbid",
                "max-artifact-count": 1,
                "min-artifact-count": 1,
                "tuple-rules": [
                    {
                        "concrete-kind": concrete_kind,
                        "kind-family": "package",
                        "max-count": 1,
                        "min-count": 1,
                        "role": "primary-package",
                    }
                ],
            },
            "allowed-artifact-tuples": [
                {
                    "concrete-kind": concrete_kind,
                    "kind-family": "package",
                    "role": "primary-package",
                }
            ],
            "id": contract_id,
        },
        "destination": {"host": host},
        "family": family,
        "instance-id": "public",
    }
    validate_contract(plan)
    return plan


def _apply_dotted_updates(document: dict[str, Any], updates: dict[str, Any]) -> None:
    """Apply dotted-path mutations for focused fixture variations."""
    for dotted_path, value in updates.items():
        target: Any = document
        parts = dotted_path.split(".")
        for part in parts[:-1]:
            target = target[part]
        if value is None:
            del target[parts[-1]]
        else:
            target[parts[-1]] = value


def _github_release_asset_proof_pair() -> list[dict[str, Any]]:
    """Return matching proof wrappers for package and symbols release assets."""
    proofs = [_load("github-release-asset-proof.json")]
    proofs[0]["run"] = _run()
    proofs.append(deepcopy(proofs[0]))
    proofs[1]["binding"] = {
        "publish-node-id": "publish-node/gh",
        "artifact-id": "artifact/symbols",
        "release-tag": "release/example/v1.2.3",
        "asset-name": "Example.1.2.3.snupkg",
    }
    proofs[1]["artifact"] = {
        "bundle-relative-path": "dist/Example.1.2.3.snupkg",
        "byte-size": 456,
        "sha256": "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
    }
    proofs[1]["attestation"]["subject-name"] = "Example.1.2.3.snupkg"
    proofs[1]["attestation"]["subject-digest"] = (
        "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"
    )
    return proofs


def _github_release_remote_exact() -> dict[str, Any]:
    """Return normalized GitHub Release remote state with REST asset digests."""
    return {
        "publish-node/gh": {
            "release-state": "prerelease",
            "assets": [
                {
                    "name": "Example.1.2.3.nupkg",
                    "label": "",
                    "byte-size": 123,
                    "sha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                    "digest": "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                    "verified-attestation": _verified_attestation(
                        "Example.1.2.3.nupkg",
                        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
                        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                    ),
                },
                {
                    "name": "Example.1.2.3.snupkg",
                    "label": "",
                    "byte-size": 456,
                    "sha256": "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
                    "digest": "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
                    "verified-attestation": _verified_attestation(
                        "Example.1.2.3.snupkg",
                        "cccccccccccccccccccccccccccccccc"
                        "cccccccccccccccccccccccccccccccc",
                    ),
                },
            ],
        }
    }


def _verified_attestation(subject_name: str, sha256: str) -> dict[str, Any]:
    """Return a normalized current gh attestation verification observation."""
    return {
        "verified": True,
        "predicate-type": "https://slsa.dev/provenance/v1",
        "subject-name": subject_name,
        "subject-digest": f"sha256:{sha256}",
        "signer-workflow": "hcoona/three/.github/workflows/release-publish-node.yml",
        "source-repository": "hcoona/three",
        "source-digest": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
    }
