"""Tests for workflow-release proof wrapper and classifier helpers."""

from __future__ import annotations

import json
from collections.abc import Mapping
from copy import deepcopy
from pathlib import Path
from typing import Any, cast

import pytest
from three_workflow_release_contracts import (
    ArtifactNameInputs,
    ContractValidationError,
    artifact_name,
    github_release_asset_binding_json,
    validate_contract,
)

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
        "workflow": "release-orchestrate.yml",
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


def test_github_release_asset_proof_wraps_github_release_attestation_evidence() -> None:
    """Emit asset proofs from GitHub Release receipt and attestation evidence."""
    request = _load("publish-request.json")
    result = _github_release_result_for_request()
    attestations = _github_release_asset_attestations()

    proofs = github_release_asset_proofs(
        publish_request=request,
        github_release_result=result,
        asset_attestations=attestations,
        run=_run(),
    )

    assert len(proofs) == 2
    for proof in proofs:
        validate_contract(proof)


def test_github_release_asset_proof_accepts_release_asset_union_for_split_node() -> (
    None
):
    """Per-node proof generation accepts sibling assets in release-level result."""
    result = _github_release_result_for_request()
    attestations = _github_release_asset_attestations()
    generated: list[dict[str, Any]] = []

    for node_id, artifact_id in [
        ("publish-node/gh-package", "artifact/package"),
        ("publish-node/gh-symbols", "artifact/symbols"),
    ]:
        request = _github_release_publish_request_for_artifact(
            node_id,
            artifact_id,
        )
        generated.extend(
            github_release_asset_proofs(
                publish_request=request,
                github_release_result=result,
                asset_attestations={artifact_id: attestations[artifact_id]},
                run=_run(),
            ),
        )

    assert [
        proof["binding"]
        for proof in sorted(
            generated,
            key=lambda proof: str(proof["binding"]["artifact-id"]),
        )
    ] == [
        {
            "publish-node-id": "publish-node/gh-package",
            "artifact-id": "artifact/package",
            "release-tag": "release/example/v1.2.3",
            "asset-name": "Example.1.2.3.nupkg",
        },
        {
            "publish-node-id": "publish-node/gh-symbols",
            "artifact-id": "artifact/symbols",
            "release-tag": "release/example/v1.2.3",
            "asset-name": "Example.1.2.3.snupkg",
        },
    ]
    for proof in generated:
        validate_contract(proof)


def test_github_release_asset_proof_separates_target_and_source_sha() -> None:
    """GitHub Release proofs bind release target and attestation source separately."""
    request = _load("publish-request.json")
    request["attestation-source-sha"] = "d" * 40
    result = _github_release_result_for_request()
    attestations = _github_release_asset_attestations()
    for evidence in attestations.values():
        evidence["source-digest"] = "d" * 40
    run = _run()
    run["head-sha"] = "d" * 40

    proofs = github_release_asset_proofs(
        publish_request=request,
        github_release_result=result,
        asset_attestations=attestations,
        run=run,
    )

    assert proofs
    for proof in proofs:
        validate_contract(proof)
        run = cast("Mapping[str, object]", proof["run"])
        attestation = cast("Mapping[str, object]", proof["attestation"])
        assert proof["release-target-sha"] == "a" * 40
        assert run["head-sha"] == "d" * 40
        assert attestation["source-digest"] == "d" * 40


def test_github_release_asset_proof_requires_run_repository_binding() -> None:
    """Proof sidecars must be emitted by the target GitHub Release repository."""
    run = _run()
    run["repository"] = "hcoona/other"

    with pytest.raises(ProofError, match="run repository"):
        github_release_asset_proofs(
            publish_request=_load("publish-request.json"),
            github_release_result=_github_release_result_for_request(),
            asset_attestations=_github_release_asset_attestations(),
            run=run,
        )


def test_github_release_asset_proof_accepts_replace_authoritative_release() -> None:
    """Official replace-authoritative receipts generate positive asset proofs."""
    request = _load("publish-request.json")
    request["profile"] = "official"
    request["publish-node"]["profile"] = "official"
    request["publish-node"]["publish-mode"] = "replace-authoritative"
    request["publish-node"]["desired-publish-state"] = {"release-state": "release"}
    result = _github_release_result_for_request()
    result["releaseExisted"] = True

    proofs = github_release_asset_proofs(
        publish_request=request,
        github_release_result=result,
        asset_attestations=_github_release_asset_attestations(),
        run=_run(),
    )

    assert len(proofs) == 2
    for proof in proofs:
        validate_contract(proof)


@pytest.mark.parametrize(
    ("field", "bad_value"),
    [
        ("tagName", "release/example/v9.9.9"),
        ("targetSha", "ffffffffffffffffffffffffffffffffffffffff"),
    ],
)
def test_github_release_asset_proof_rejects_github_release_result_mismatch(
    field: str,
    bad_value: str,
) -> None:
    """GitHub Release result identity must match the request before proof emission."""
    request = _load("publish-request.json")
    result = _github_release_result_for_request()
    result[field] = bad_value

    with pytest.raises(ProofError, match="GitHub Release result"):
        github_release_asset_proofs(
            publish_request=request,
            github_release_result=result,
            asset_attestations=_github_release_asset_attestations(),
            run=_run(),
        )


@pytest.mark.parametrize(
    ("field", "bad_value", "message"),
    [
        ("name", "Example.1.2.3-other.nupkg", "asset set"),
        ("size", 124, "asset size"),
        ("sha256", "d" * 64, "asset sha256"),
    ],
)
def test_github_release_asset_proof_rejects_result_asset_mismatch(
    field: str,
    bad_value: object,
    message: str,
) -> None:
    """GitHub Release result asset receipt must match request asset evidence."""
    request = _load("publish-request.json")
    result = _github_release_result_for_request()
    result["assets"][0][field] = bad_value

    with pytest.raises(ProofError, match=message):
        github_release_asset_proofs(
            publish_request=request,
            github_release_result=result,
            asset_attestations=_github_release_asset_attestations(),
            run=_run(),
        )


def test_github_release_asset_proof_requires_publish_request_evidence() -> None:
    """GitHub Release receipts must be checked against build receipt evidence."""
    request = _load("publish-request.json")
    request["artifacts"]["artifact/package"].pop("byte-size")

    with pytest.raises((ContractValidationError, ProofError), match="byte-size"):
        github_release_asset_proofs(
            publish_request=request,
            github_release_result=_github_release_result_for_request(),
            asset_attestations=_github_release_asset_attestations(),
            run=_run(),
        )


def test_github_release_asset_proof_allows_create_only_proof_recovery() -> None:
    """Create-only may recover proof wrappers when payload already exists exactly."""
    result = _github_release_result_for_request()
    result["releaseExisted"] = True

    proofs = github_release_asset_proofs(
        publish_request=_load("publish-request.json"),
        github_release_result=result,
        asset_attestations=_github_release_asset_attestations(),
        run=_run(),
    )

    asset_names = {
        cast("Mapping[str, object]", proof["binding"])["asset-name"] for proof in proofs
    }
    assert asset_names == {
        "Example.1.2.3.nupkg",
        "Example.1.2.3.snupkg",
    }


@pytest.mark.parametrize(
    ("publish_mode", "release_existed", "profile", "release_state"),
    [
        ("overwrite-mutable", False, "buddy", "prerelease"),
        ("replace-authoritative", False, "official", "release"),
    ],
)
def test_github_release_asset_proof_rejects_release_existed_mode_mismatch(
    publish_mode: str,
    release_existed: bool,
    profile: str,
    release_state: str,
) -> None:
    """GitHub Release result releaseExisted must match the frozen publish mode."""
    request = _load("publish-request.json")
    request["profile"] = profile
    request["publish-node"]["profile"] = profile
    request["publish-node"]["publish-mode"] = publish_mode
    request["publish-node"]["desired-publish-state"] = {
        "release-state": release_state,
    }
    if publish_mode == "overwrite-mutable":
        request["publish-node"]["overwrite-mutable-authorization"] = {
            "kind": "planner-validated-buddy-force",
            "profile": "buddy",
            "force": True,
            "remote-observation": "partial",
            "mutability": "mutable-prerelease",
        }
    result = _github_release_result_for_request()
    result["releaseExisted"] = release_existed

    with pytest.raises(ProofError, match="releaseExisted"):
        github_release_asset_proofs(
            publish_request=request,
            github_release_result=result,
            asset_attestations=_github_release_asset_attestations(),
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
    result = _github_release_result_for_request()
    attestations = _github_release_asset_attestations()
    package_evidence = attestations["artifact/package"]
    if predicate_type is None:
        del package_evidence["predicate-type"]
    else:
        package_evidence["predicate-type"] = predicate_type

    with pytest.raises(ProofError, match="predicate type"):
        github_release_asset_proofs(
            publish_request=request,
            github_release_result=result,
            asset_attestations=attestations,
            run=_run(),
        )


def test_github_release_classification_requires_asset_proofs_for_exact() -> None:
    """Missing GitHub Release proof wrappers block exact-satisfied replay."""
    plan = _load("release-plan.json")
    remote = _github_release_remote_exact()

    assert classify_github_release_observations(
        plan=plan,
        remote_releases=remote,
        proofs=[],
    ) == {"publish-node/gh": "partial"}


def test_github_release_classification_uses_proof_and_remote_verification_for_exact() -> (
    None
):
    """Require exact state, asset set, labels, digest, signer, source, and proofs."""
    plan = _load("release-plan.json")
    remote = _github_release_remote_exact()

    assert classify_github_release_observations(
        plan=plan,
        remote_releases=remote,
        proofs=_github_release_asset_proof_pair(),
    ) == {"publish-node/gh": "exact-satisfied"}


def test_github_release_classification_exact_with_distinct_target_and_source_sha() -> (
    None
):
    """Exact GitHub Release replay accepts separated target and attestation source."""
    proofs = _github_release_asset_proof_pair()
    for proof in proofs:
        proof["run"]["head-sha"] = "d" * 40
        proof["attestation"]["source-digest"] = "d" * 40
    remote = _github_release_remote_exact()
    for asset in remote["publish-node/gh"]["assets"]:
        asset["verified-attestation"]["source-digest"] = "d" * 40

    assert classify_github_release_observations(
        plan=_load("release-plan.json"),
        remote_releases=remote,
        proofs=proofs,
    ) == {"publish-node/gh": "exact-satisfied"}


def test_github_release_classification_exact_uses_release_level_asset_union() -> None:
    """Same-release GitHub nodes accept the shared release-level payload union."""
    plan = _github_release_plan_with_split_nodes()
    remote = _github_release_remote_for_split_nodes()
    proofs = _github_release_split_node_asset_proofs()

    assert classify_github_release_observations(
        plan=plan,
        remote_releases=remote,
        proofs=proofs,
    ) == {
        "publish-node/gh-package": "exact-satisfied",
        "publish-node/gh-symbols": "exact-satisfied",
    }


def test_github_release_classification_counts_extra_asset_outside_union() -> None:
    """Payload assets outside the same-release planned union remain non-exact."""
    plan = _github_release_plan_with_split_nodes()
    remote = _github_release_remote_for_split_nodes()
    for release in remote.values():
        release["assets"].append(
            {
                "name": "Example.1.2.3.extra.zip",
                "label": "",
                "byte-size": 789,
                "digest": "sha256:" + ("d" * 64),
                "verified-attestation": _verified_attestation(
                    "Example.1.2.3.extra.zip",
                    "d" * 64,
                ),
            },
        )

    assert classify_github_release_observations(
        plan=plan,
        remote_releases=remote,
        proofs=_github_release_split_node_asset_proofs(),
    ) == {
        "publish-node/gh-package": "partial",
        "publish-node/gh-symbols": "partial",
    }


def test_github_release_classification_ignores_proof_sidecar_assets_for_exact() -> None:
    """Release asset proof wrappers do not count as extra payload assets."""
    proofs = _github_release_asset_proof_pair()
    remote = _github_release_remote_exact()
    sidecar_name = _github_release_asset_proof_sidecar_name(proofs[0])
    remote["publish-node/gh"]["assets"].append(
        {
            "name": sidecar_name,
            "label": "",
            "byte-size": 999,
            "digest": "sha256:" + ("d" * 64),
            "verified-attestation": _verified_attestation(
                sidecar_name,
                "d" * 64,
            ),
        },
    )

    assert classify_github_release_observations(
        plan=_load("release-plan.json"),
        remote_releases=remote,
        proofs=proofs,
    ) == {"publish-node/gh": "exact-satisfied"}


@pytest.mark.parametrize(
    "sidecar_name",
    [
        "release-github-release-asset-proof-v1-0123456789abcdef01234567-123-1.json",
        "release-github-release-asset-proof-v1-malformed.json",
    ],
)
def test_github_release_classification_counts_unvalidated_sidecar_asset_as_extra(
    sidecar_name: str,
) -> None:
    """Sidecar-shaped assets are extra payload until matched to valid proof."""
    remote = _github_release_remote_exact()
    remote["publish-node/gh"]["assets"].append(
        {
            "name": sidecar_name,
            "label": "",
            "byte-size": 999,
            "digest": "sha256:" + ("d" * 64),
            "verified-attestation": _verified_attestation(
                sidecar_name,
                "d" * 64,
            ),
        },
    )

    assert classify_github_release_observations(
        plan=_load("release-plan.json"),
        remote_releases=remote,
        proofs=_github_release_asset_proof_pair(),
    ) == {"publish-node/gh": "partial"}


@pytest.mark.parametrize(
    ("binding_field", "binding_value"),
    [
        ("publish-node-id", "publish-node/rebound"),
        ("artifact-id", "artifact/symbols"),
    ],
)
def test_github_release_classification_counts_rebound_sidecar_asset_as_extra(
    binding_field: str,
    binding_value: str,
) -> None:
    """Schema-valid sidecars are ignorable only for the active node artifact map."""
    proofs = _github_release_asset_proof_pair()
    rebound_proof = deepcopy(proofs[0])
    binding = cast("dict[str, Any]", rebound_proof["binding"])
    binding[binding_field] = binding_value
    sidecar_name = _github_release_asset_proof_sidecar_name(rebound_proof)
    remote = _github_release_remote_exact()
    remote["publish-node/gh"]["assets"].append(
        {
            "name": sidecar_name,
            "label": "",
            "byte-size": 999,
            "digest": "sha256:" + ("d" * 64),
            "verified-attestation": _verified_attestation(
                sidecar_name,
                "d" * 64,
            ),
        },
    )

    assert classify_github_release_observations(
        plan=_load("release-plan.json"),
        remote_releases=remote,
        proofs=[rebound_proof, *proofs],
    ) == {"publish-node/gh": "partial"}


@pytest.mark.parametrize(
    ("run_id", "attempt"),
    [
        (124, 1),
        (123, 2),
        (124, 2),
    ],
)
def test_github_release_classification_counts_run_suffix_mismatched_sidecar_as_extra(
    run_id: int,
    attempt: int,
) -> None:
    """Sidecar filename matching includes binding digest, run id, and attempt."""
    proofs = _github_release_asset_proof_pair()
    sidecar_name = _github_release_asset_proof_sidecar_name_with_run_suffix(
        proofs[0],
        run_id=run_id,
        attempt=attempt,
    )
    remote = _github_release_remote_exact()
    remote["publish-node/gh"]["assets"].append(
        {
            "name": sidecar_name,
            "label": "",
            "byte-size": 999,
            "digest": "sha256:" + ("d" * 64),
            "verified-attestation": _verified_attestation(
                sidecar_name,
                "d" * 64,
            ),
        },
    )

    assert classify_github_release_observations(
        plan=_load("release-plan.json"),
        remote_releases=remote,
        proofs=proofs,
    ) == {"publish-node/gh": "partial"}


@pytest.mark.parametrize(
    "field_path",
    [
        ("run", "repository"),
        ("attestation", "source-repository"),
    ],
)
def test_github_release_classification_counts_foreign_repository_sidecar_as_extra(
    field_path: tuple[str, str],
) -> None:
    """Proof sidecars are ignorable only when bound to the target repository."""
    current_proofs = _github_release_asset_proof_pair()
    foreign_sidecar = deepcopy(current_proofs[0])
    foreign_sidecar["plan-id"] = "plan/old"
    foreign_sidecar["run"]["run-id"] = 122
    foreign_sidecar["run"]["head-sha"] = "f" * 40
    foreign_sidecar[field_path[0]][field_path[1]] = "hcoona/other"
    sidecar_name = _github_release_asset_proof_sidecar_name(foreign_sidecar)
    remote = _github_release_remote_exact()
    remote["publish-node/gh"]["assets"].append(
        {
            "name": sidecar_name,
            "label": "",
            "byte-size": 999,
            "digest": "sha256:" + ("d" * 64),
            "verified-attestation": _verified_attestation(
                sidecar_name,
                "d" * 64,
            ),
        },
    )

    assert classify_github_release_observations(
        plan=_load("release-plan.json"),
        remote_releases=remote,
        proofs=[foreign_sidecar, *current_proofs],
    ) == {"publish-node/gh": "partial"}


def test_github_release_ignores_old_plan_same_target_sidecar_with_current_proofs() -> (
    None
):
    """Historical same-target sidecars do not poison current exact proof replay."""
    current_proofs = _github_release_asset_proof_pair()
    historical_sidecar = deepcopy(current_proofs[0])
    historical_sidecar["plan-id"] = "plan/old"
    historical_sidecar["run"]["run-id"] = 122
    sidecar_name = _github_release_asset_proof_sidecar_name(historical_sidecar)
    remote = _github_release_remote_exact()
    remote["publish-node/gh"]["assets"].append(
        {
            "name": sidecar_name,
            "label": "",
            "byte-size": 999,
            "digest": "sha256:" + ("d" * 64),
            "verified-attestation": _verified_attestation(
                sidecar_name,
                "d" * 64,
            ),
        },
    )

    assert classify_github_release_observations(
        plan=_load("release-plan.json"),
        remote_releases=deepcopy(remote),
        proofs=[historical_sidecar],
    ) == {"publish-node/gh": "partial"}
    assert classify_github_release_observations(
        plan=_load("release-plan.json"),
        remote_releases=remote,
        proofs=[historical_sidecar, *current_proofs],
    ) == {"publish-node/gh": "exact-satisfied"}


def test_github_release_counts_wrong_project_historical_sidecar_with_current_proofs() -> (
    None
):
    """Historical sidecars are ignorable only for the current project."""
    current_proofs = _github_release_asset_proof_pair()
    historical_sidecar = deepcopy(current_proofs[0])
    historical_sidecar["plan-id"] = "plan/old"
    historical_sidecar["project-id"] = "other-project"
    historical_sidecar["run"]["run-id"] = 122
    sidecar_name = _github_release_asset_proof_sidecar_name(historical_sidecar)
    remote = _github_release_remote_exact()
    remote["publish-node/gh"]["assets"].append(
        {
            "name": sidecar_name,
            "label": "",
            "byte-size": 999,
            "digest": "sha256:" + ("d" * 64),
            "verified-attestation": _verified_attestation(
                sidecar_name,
                "d" * 64,
            ),
        },
    )

    assert classify_github_release_observations(
        plan=_load("release-plan.json"),
        remote_releases=remote,
        proofs=[historical_sidecar, *current_proofs],
    ) == {"publish-node/gh": "partial"}


def test_github_release_counts_wrong_variant_historical_sidecar_with_current_proofs() -> (
    None
):
    """Historical sidecars are ignorable only for the current artifact variant."""
    current_proofs = _github_release_asset_proof_pair()
    historical_sidecar = deepcopy(current_proofs[0])
    historical_sidecar["plan-id"] = "plan/old"
    historical_sidecar["variant-id"] = "variant/other"
    historical_sidecar["run"]["run-id"] = 122
    sidecar_name = _github_release_asset_proof_sidecar_name(historical_sidecar)
    remote = _github_release_remote_exact()
    remote["publish-node/gh"]["assets"].append(
        {
            "name": sidecar_name,
            "label": "",
            "byte-size": 999,
            "digest": "sha256:" + ("d" * 64),
            "verified-attestation": _verified_attestation(
                sidecar_name,
                "d" * 64,
            ),
        },
    )

    assert classify_github_release_observations(
        plan=_load("release-plan.json"),
        remote_releases=remote,
        proofs=[historical_sidecar, *current_proofs],
    ) == {"publish-node/gh": "partial"}


def test_github_release_counts_stale_target_sidecar_even_with_current_proofs() -> None:
    """Wrong-target sidecars remain visible extras and cannot prove exact payload."""
    current_proofs = _github_release_asset_proof_pair()
    stale_proof = deepcopy(current_proofs[0])
    stale_proof["plan-id"] = "plan/old"
    stale_proof["release-target-sha"] = "f" * 40
    stale_proof["run"]["run-id"] = 122
    stale_proof["run"]["run-attempt"] = 1
    stale_proof["run"]["head-sha"] = "f" * 40
    stale_sidecar_name = _github_release_asset_proof_sidecar_name(stale_proof)
    remote = _github_release_remote_exact()
    remote["publish-node/gh"]["assets"].append(
        {
            "name": stale_sidecar_name,
            "label": "",
            "byte-size": 999,
            "digest": "sha256:" + ("d" * 64),
            "verified-attestation": _verified_attestation(
                stale_sidecar_name,
                "d" * 64,
            ),
        },
    )

    assert classify_github_release_observations(
        plan=_load("release-plan.json"),
        remote_releases=deepcopy(remote),
        proofs=[stale_proof],
    ) == {"publish-node/gh": "partial"}
    assert classify_github_release_observations(
        plan=_load("release-plan.json"),
        remote_releases=remote,
        proofs=[stale_proof, *current_proofs],
    ) == {"publish-node/gh": "partial"}


def test_github_release_counts_source_digest_mismatched_sidecar_with_current_proofs() -> (
    None
):
    """Current-target sidecars remain visible when proof provenance disagrees."""
    current_proofs = _github_release_asset_proof_pair()
    mismatched_sidecar = deepcopy(current_proofs[0])
    mismatched_sidecar["run"]["run-id"] = 124
    mismatched_sidecar["run"]["head-sha"] = "d" * 40
    sidecar_name = _github_release_asset_proof_sidecar_name(mismatched_sidecar)
    remote = _github_release_remote_exact()
    remote["publish-node/gh"]["assets"].append(
        {
            "name": sidecar_name,
            "label": "",
            "byte-size": 999,
            "digest": "sha256:" + ("d" * 64),
            "verified-attestation": _verified_attestation(
                sidecar_name,
                "d" * 64,
            ),
        },
    )

    assert classify_github_release_observations(
        plan=_load("release-plan.json"),
        remote_releases=remote,
        proofs=[mismatched_sidecar, *current_proofs],
    ) == {"publish-node/gh": "partial"}


def test_github_release_asset_proofs_are_required_for_each_asset() -> None:
    """Every planned GitHub Release asset needs an admissible proof wrapper."""
    plan = _load("release-plan.json")
    proofs = [_load("github-release-asset-proof.json")]
    proofs[0]["run"] = _run()
    remote = _github_release_remote_exact()

    assert classify_github_release_observations(
        plan=plan,
        remote_releases=remote,
        proofs=proofs,
    ) == {"publish-node/gh": "partial"}

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
    """Non-admissible wrappers cannot make current remote proof exact."""
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
    ) == {"publish-node/gh": "partial"}


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
    if attestation_field == "source-digest":
        conflicting["run"]["head-sha"] = bad_value
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
    ) == {"publish-node/gh": "partial"}


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
    ("field", "value"),
    [
        ("byte-size", 999),
        ("sha256", "d" * 64),
    ],
)
def test_github_release_exact_requires_build_receipt_evidence(
    field: str,
    value: object,
) -> None:
    """Remote exact classification is bound to proof build receipt bytes."""
    plan = _load("release-plan.json")
    proofs = _github_release_asset_proof_pair()
    package_proof = proofs[0]
    package_proof["artifact"][field] = value

    assert classify_github_release_observations(
        plan=plan,
        remote_releases=_github_release_remote_exact(),
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


def test_github_release_classification_promotes_official_prerelease_partial() -> None:
    """A non-exact same-tag prerelease for desired release is authoritative partial."""
    plan = _load("release-plan.json")
    node = plan["graph"]["publish-nodes"]["publish-node/gh"]
    node["desired-publish-state"] = {"release-state": "release"}
    remote = _github_release_remote_exact()
    remote["publish-node/gh"]["assets"] = []

    assert classify_github_release_observations(
        plan=plan,
        remote_releases=remote,
        proofs=[],
    ) == {"publish-node/gh": "partial-authoritative"}


def _github_release_result_for_request() -> dict[str, Any]:
    """Return a GitHub Release result fixture matching publish-request assets."""
    result = _load("github-release-result.json")
    result["assets"] = [
        {
            "name": "Example.1.2.3.nupkg",
            "size": 123,
            "sha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
        },
        {
            "name": "Example.1.2.3.snupkg",
            "size": 456,
            "sha256": "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
        },
    ]
    return result


def _github_release_asset_attestations() -> dict[str, Any]:
    """Return GitHub Release asset attestation evidence keyed by artifact ID."""
    return {
        "artifact/package": {
            "asset-name": "Example.1.2.3.nupkg",
            "sha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            "predicate-type": "https://slsa.dev/provenance/v1",
            "signer-workflow": "hcoona/three/.github/workflows/release-orchestrate.yml",
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
            "signer-workflow": "hcoona/three/.github/workflows/release-orchestrate.yml",
            "source-repository": "hcoona/three",
            "source-digest": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "attestation-id": "att-2",
            "attestation-url": "https://github.com/hcoona/three/attestations/2",
            "bundle-path": "attestations/Example.1.2.3.snupkg.json",
        },
    }


def _github_release_publish_request_for_artifact(
    node_id: str,
    artifact_id: str,
) -> dict[str, Any]:
    """Return a GitHub Release publish request narrowed to one artifact."""
    request = _load("publish-request.json")
    request["publish-node-id"] = node_id
    project = cast("dict[str, Any]", request["project"])
    publish_node_ids = cast("list[str]", project["publish-node-ids"])
    publish_node_ids[:] = [node_id]
    node = cast("dict[str, Any]", request["publish-node"])
    node["publish-node-id"] = node_id
    node["artifact-ids"] = [artifact_id]
    request["artifacts"] = {artifact_id: request["artifacts"][artifact_id]}
    projection = cast("dict[str, dict[str, Any]]", node["projection"])
    for by_artifact_id in projection.values():
        by_artifact_id_keys = list(by_artifact_id)
        for key in by_artifact_id_keys:
            if key != artifact_id:
                del by_artifact_id[key]
    validate_contract(request)
    return request


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
    topology = {
        "npm": "external-oidc-caller-workflow",
        "rubygems": "external-oidc-reusable-workflow",
    }[family]

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


def _github_release_plan_with_split_nodes() -> dict[str, Any]:
    """Return a plan with two same-release GitHub nodes and disjoint assets."""
    plan = deepcopy(_load("release-plan.json"))
    graph = cast("dict[str, Any]", plan["graph"])
    publish_nodes = cast("dict[str, dict[str, Any]]", graph["publish-nodes"])
    base_node = publish_nodes.pop("publish-node/gh")
    package_node = base_node
    symbols_node = deepcopy(base_node)

    _narrow_github_release_node(
        package_node,
        node_id="publish-node/gh-package",
        artifact_id="artifact/package",
    )
    _narrow_github_release_node(
        symbols_node,
        node_id="publish-node/gh-symbols",
        artifact_id="artifact/symbols",
    )
    publish_nodes["publish-node/gh-package"] = package_node
    publish_nodes["publish-node/gh-symbols"] = symbols_node

    project = cast("dict[str, Any]", plan["envelope"]["projects"]["example"])
    publish_node_ids = cast("list[str]", project["publish-node-ids"])
    publish_node_ids[:] = [
        "publish-node/gh-package",
        "publish-node/gh-symbols",
        "publish-node/nuget",
    ]
    validate_contract(plan)
    return plan


def _narrow_github_release_node(
    node: dict[str, Any],
    *,
    node_id: str,
    artifact_id: str,
) -> None:
    node["publish-node-id"] = node_id
    node["artifact-ids"] = [artifact_id]
    projection = cast("dict[str, dict[str, Any]]", node["projection"])
    for by_artifact_id in projection.values():
        for key in list(by_artifact_id):
            if key != artifact_id:
                del by_artifact_id[key]


def _github_release_remote_for_split_nodes() -> dict[str, Any]:
    """Return one release-level remote state observed for two split nodes."""
    release = _github_release_remote_exact()["publish-node/gh"]
    return {
        "publish-node/gh-package": deepcopy(release),
        "publish-node/gh-symbols": deepcopy(release),
    }


def _github_release_split_node_asset_proofs() -> list[dict[str, Any]]:
    """Return per-node proofs for split package and symbols GitHub nodes."""
    proofs = _github_release_asset_proof_pair()
    proofs[0]["binding"]["publish-node-id"] = "publish-node/gh-package"
    proofs[1]["binding"]["publish-node-id"] = "publish-node/gh-symbols"
    return proofs


def _github_release_asset_proof_pair() -> list[dict[str, Any]]:
    """Return matching proof wrappers for package and symbols release assets."""
    proofs = [_load("github-release-asset-proof.json")]
    proofs[0]["run"] = _run()
    proofs[0]["release-target-sha"] = "a" * 40
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


def _github_release_asset_proof_sidecar_name(proof: dict[str, Any]) -> str:
    """Return the persisted GitHub Release proof sidecar asset name."""
    run = cast("dict[str, Any]", proof["run"])
    binding = cast("dict[str, Any]", proof["binding"])
    binding_json = github_release_asset_binding_json(
        publish_node_id=str(binding["publish-node-id"]),
        artifact_id=str(binding["artifact-id"]),
        release_tag=str(binding["release-tag"]),
        asset_name=str(binding["asset-name"]),
    )
    name = artifact_name(
        "github-release-asset-proof",
        ArtifactNameInputs(
            run_id=int(run["run-id"]),
            attempt=int(run["run-attempt"]),
            binding_json=binding_json,
        ),
    )
    return f"{name}.json"


def _github_release_asset_proof_sidecar_name_with_run_suffix(
    proof: dict[str, Any],
    *,
    run_id: int,
    attempt: int,
) -> str:
    """Return a proof-bound name with a different run suffix."""
    name = _github_release_asset_proof_sidecar_name(proof)
    stem = name.removesuffix(".json")
    prefix, _old_run_id, _old_attempt = stem.rsplit("-", 2)
    return f"{prefix}-{run_id}-{attempt}.json"


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
        "signer-workflow": "hcoona/three/.github/workflows/release-orchestrate.yml",
        "source-repository": "hcoona/three",
        "source-digest": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
    }
