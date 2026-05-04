"""Immutable registry and GitHub Release asset proof helpers."""

from __future__ import annotations

import json
import re
from collections import defaultdict
from collections.abc import Mapping, Sequence
from pathlib import Path, PurePosixPath

from three_workflow_release_contracts import validate_contract

type Json = dict[str, object]

_IMMUTABLE_FAMILIES = {"nuget", "pypi", "npm", "rubygems"}
_GITHUB_PROOF_PREDICATE = "https://slsa.dev/provenance/v1"
_GITHUB_ASSET_DIGEST_RE = re.compile(r"^sha256:([0-9a-f]{64})$")


class ProofError(ValueError):
    """Raised when proof material is missing, conflicting, or invalid."""

    def __init__(
        self,
        message: str,
        *,
        code: str = "PROOF_INVALID",
        details: Mapping[str, object] | None = None,
    ) -> None:
        """Initialize a fail-closed proof error."""
        super().__init__(message)
        self.code = code
        self.details = dict(details or {})


def immutable_proofs(  # noqa: PLR0913
    *,
    plan: Mapping[str, object],
    build_result: Mapping[str, object],
    publish_node_id: str,
    run: Mapping[str, object],
    build_result_artifact_name: str,
    build_result_artifact_id: int,
    bundle_artifact_name: str,
) -> list[Json]:
    """Create validated immutable-proof wrappers for one publish node."""
    validate_contract(dict(plan))
    validate_contract(dict(build_result))
    _require_live_run(run)
    node = _publish_node(plan, publish_node_id)
    target = _target(plan, node)
    family = _string(target.get("family"), "target.family")
    if family not in _IMMUTABLE_FAMILIES:
        raise ProofError(
            "immutable proof requires an immutable package-registry node",
            details={"publish-node-id": publish_node_id, "family": family},
        )
    if _capability(target, "mutability") != "immutable":
        raise ProofError(
            "immutable proof requires target mutability immutable",
            details={"publish-node-id": publish_node_id},
        )
    identity = _mapping(
        node.get("resolved-publish-identity"),
        "publish-node.resolved-publish-identity",
    )
    package_name = _string(identity.get("package-name"), "package-name")
    version = _string(identity.get("version"), "version")
    build_artifacts = _mapping(build_result.get("artifacts"), "build-result.artifacts")
    proofs: list[Json] = []
    for artifact_id in _string_list(node.get("artifact-ids"), "artifact-ids"):
        variant_id = _artifact_variant(plan, artifact_id)
        _require_equal(
            build_result.get("plan-id"),
            _plan_id(plan),
            "build result plan does not match the selected plan",
        )
        _require_equal(
            build_result.get("project-id"),
            node.get("project-id"),
            "build result project does not match the publish node",
        )
        _require_equal(
            build_result.get("variant-id"),
            variant_id,
            "build result variant does not match the planned artifact",
        )
        receipt = _mapping(
            build_artifacts.get(artifact_id),
            f"build-result.artifacts[{artifact_id}]",
        )
        proof: Json = {
            "api-version": "three.release.immutable-proof/v1alpha1",
            "kind": "immutable-proof",
            "binding": {
                "publish-node-id": publish_node_id,
                "artifact-id": artifact_id,
                "package-name": package_name,
                "version": version,
            },
            "plan-id": _plan_id(plan),
            "project-id": _string(node.get("project-id"), "project-id"),
            "variant-id": variant_id,
            "build-result-artifact-name": build_result_artifact_name,
            "build-result-artifact-id": build_result_artifact_id,
            "bundle-artifact-name": bundle_artifact_name,
            "run": dict(run),
            "artifact": _artifact_receipt(receipt),
        }
        validate_contract(proof)
        proofs.append(proof)
    return proofs


def github_release_asset_proofs(
    *,
    publish_request: Mapping[str, object],
    publish_result: Mapping[str, object],
    run: Mapping[str, object],
) -> list[Json]:
    """Create validated GitHub Release asset proof wrappers."""
    validate_contract(dict(publish_request))
    validate_contract(dict(publish_result))
    _require_live_run(run)
    node = _mapping(publish_request.get("publish-node"), "publish-node")
    target = _mapping(
        publish_request.get("target-instance-snapshot"),
        "target-instance-snapshot",
    )
    destination = _mapping(target.get("destination"), "destination")
    source_repository = (
        f"{_string(destination.get('owner'), 'destination.owner')}/"
        f"{_string(destination.get('repo'), 'destination.repo')}"
    )
    source_digest = _string(publish_request.get("commit-sha"), "commit-sha")
    node_attestation = _mapping(node.get("attestation"), "publish-node.attestation")
    signer_workflow = _string(
        node_attestation.get("signer-workflow"),
        "publish-node.attestation.signer-workflow",
    )
    identity = _mapping(
        node.get("resolved-publish-identity"),
        "publish-node.resolved-publish-identity",
    )
    release_tag = _string(identity.get("release-tag"), "release-tag")
    projection = _mapping(node.get("projection"), "publish-node.projection")
    asset_names = _mapping(
        projection.get("asset-names-by-artifact-id"),
        "asset-names-by-artifact-id",
    )
    result_evidence = _mapping(publish_result.get("evidence"), "evidence")
    attestations = _mapping(
        result_evidence.get("asset-attestations"),
        "evidence.asset-attestations",
    )
    artifacts = _mapping(publish_request.get("artifacts"), "artifacts")
    publish_node_id = _string(publish_request.get("publish-node-id"), "publish-node-id")
    _require_publish_result_matches_request(publish_request, publish_result, node)
    proofs: list[Json] = []
    for artifact_id in _string_list(node.get("artifact-ids"), "artifact-ids"):
        artifact_input = _mapping(
            artifacts.get(artifact_id),
            f"artifacts[{artifact_id}]",
        )
        artifact = _artifact_receipt(artifact_input)
        evidence = _mapping(
            attestations.get(artifact_id),
            f"evidence.asset-attestations[{artifact_id}]",
        )
        asset_name = _string(asset_names.get(artifact_id), "asset-name")
        _require_equal(
            evidence.get("asset-name"),
            asset_name,
            "attested asset name does not match the plan",
        )
        sha256 = _string(artifact.get("sha256"), "artifact.sha256")
        _require_equal(
            evidence.get("sha256"),
            sha256,
            "attested digest does not match the artifact receipt",
        )
        _require_equal(
            evidence.get("signer-workflow"),
            signer_workflow,
            "attested signer workflow does not match the plan",
        )
        _require_equal(
            evidence.get("source-repository"),
            source_repository,
            "attested source repository does not match the target",
        )
        _require_equal(
            evidence.get("source-digest"),
            source_digest,
            "attested source digest does not match the request",
        )
        _require_equal(
            evidence.get("predicate-type"),
            _GITHUB_PROOF_PREDICATE,
            "attested predicate type is not SLSA build provenance",
        )
        attestation: Json = {
            "predicate-type": _GITHUB_PROOF_PREDICATE,
            "subject-name": asset_name,
            "subject-digest": f"sha256:{sha256}",
            "signer-workflow": signer_workflow,
            "source-repository": source_repository,
            "source-digest": source_digest,
            "attestation-id": _string(evidence.get("attestation-id"), "attestation-id"),
            "attestation-url": _string(
                evidence.get("attestation-url"), "attestation-url"
            ),
        }
        proof: Json = {
            "api-version": "three.release.github-release-asset-proof/v1alpha1",
            "kind": "github-release-asset-proof",
            "binding": {
                "publish-node-id": publish_node_id,
                "artifact-id": artifact_id,
                "release-tag": release_tag,
                "asset-name": asset_name,
            },
            "plan-id": _string(publish_request.get("plan-id"), "plan-id"),
            "project-id": _string(node.get("project-id"), "project-id"),
            "variant-id": _artifact_variant_from_request(artifact_input),
            "run": dict(run),
            "artifact": artifact,
            "attestation": attestation,
        }
        validate_contract(proof)
        proofs.append(proof)
    if len(proofs) != len(attestations):
        raise ProofError(
            "GitHub Release proof evidence has extra or missing assets",
            details={"publish-node-id": publish_node_id},
        )
    return proofs


def classify_immutable_observations(
    *,
    plan: Mapping[str, object],
    remote_members: Mapping[str, object],
    proofs: Sequence[Mapping[str, object]],
    build_result_receipts: Sequence[Mapping[str, object]] = (),
) -> dict[str, str]:
    """Classify immutable package nodes from remote members and proof wrappers."""
    validate_contract(dict(plan))
    proof_index = _immutable_proof_index(proofs, plan, build_result_receipts)
    observations: dict[str, str] = {}
    for node_id, node in _publish_nodes(plan).items():
        target = _target(plan, node)
        if _capability(target, "mutability") != "immutable":
            continue
        identity = _mapping(
            node.get("resolved-publish-identity"),
            f"publish-nodes[{node_id}].resolved-publish-identity",
        )
        package_name = _string(identity.get("package-name"), "package-name")
        version = _string(identity.get("version"), "version")
        members = _remote_members(remote_members.get(node_id), node_id)
        if not members:
            observations[node_id] = "absent"
            continue
        filenames = _final_distribution_names(node, node_id)
        planned_names = set(filenames.values())
        remote_names = set(members)
        present_ids = [
            artifact_id
            for artifact_id, filename in filenames.items()
            if filename in members
        ]
        if remote_names - planned_names:
            observations[node_id] = "conflicting"
            continue
        if not present_ids:
            observations[node_id] = "absent"
            continue
        for artifact_id in present_ids:
            key = (node_id, artifact_id, package_name, version)
            digest = proof_index.get(key)
            if digest is None:
                raise ProofError(
                    "immutable digest proof is unavailable",
                    code="IMMUTABLE_PROOF_UNAVAILABLE",
                    details={
                        "publish-node-id": node_id,
                        "artifact-id": artifact_id,
                    },
                )
            if digest != members[filenames[artifact_id]]:
                observations[node_id] = "conflicting"
                break
        else:
            if len(present_ids) == len(filenames):
                observations[node_id] = "exact-satisfied"
            else:
                observations[node_id] = "partial"
    return observations


def classify_github_release_observations(
    *,
    plan: Mapping[str, object],
    remote_releases: Mapping[str, object],
    proofs: Sequence[Mapping[str, object]],
) -> dict[str, str]:
    """Classify GitHub Release nodes from normalized release state and proofs."""
    validate_contract(dict(plan))
    proof_index = _github_asset_proof_index(proofs, plan)
    observations: dict[str, str] = {}
    for node_id, node in _publish_nodes(plan).items():
        target = _target(plan, node)
        if _string(target.get("family"), "target.family") != "github-release":
            continue
        remote = remote_releases.get(node_id)
        if remote is None:
            observations[node_id] = "absent"
            continue
        if not isinstance(remote, Mapping):
            raise ProofError(
                "GitHub Release remote state must be an object",
                details={"publish-node-id": node_id},
            )
        remote_state = _github_remote_release_state(node_id, node, remote)
        if _github_release_exact(node_id, node, target, plan, remote, proof_index):
            observations[node_id] = "exact-satisfied"
            continue
        desired = _mapping(
            node.get("desired-publish-state"),
            "desired-publish-state",
        )
        desired_state = _string(desired.get("release-state"), "release-state")
        if remote_state == "release" and desired_state == "prerelease":
            observations[node_id] = "conflicting"
        elif remote_state == "release" and desired_state == "release":
            observations[node_id] = "conflicting"
        else:
            observations[node_id] = "partial"
    return observations


def _github_release_exact(
    node_id: str,
    node: Mapping[str, object],
    target: Mapping[str, object],
    plan: Mapping[str, object],
    remote: Mapping[str, object],
    proof_index: Mapping[
        tuple[str, str, str, str],
        Mapping[str, object] | None,
    ],
) -> bool:
    desired = _mapping(node.get("desired-publish-state"), "desired-publish-state")
    if remote.get("release-state") != desired.get("release-state"):
        return False
    projection = _mapping(node.get("projection"), "projection")
    names = _mapping(
        projection.get("asset-names-by-artifact-id"),
        "asset-names-by-artifact-id",
    )
    labels = _mapping(
        projection.get("asset-labels-by-artifact-id"),
        "asset-labels-by-artifact-id",
    )
    identity = _mapping(node.get("resolved-publish-identity"), "identity")
    release_tag = _string(identity.get("release-tag"), "release-tag")
    remote_assets = _remote_assets(remote.get("assets"), node_id)
    if set(remote_assets) != {str(name) for name in names.values()}:
        return False
    for artifact_id, raw_name in names.items():
        if not isinstance(artifact_id, str) or not isinstance(raw_name, str):
            return False
        remote_asset = remote_assets[raw_name]
        planned_label = labels.get(artifact_id)
        if planned_label is None:
            if remote_asset.get("label") not in (None, ""):
                return False
        elif remote_asset.get("label") != planned_label:
            return False
        node_attestation = _mapping(node.get("attestation"), "node.attestation")
        destination = _mapping(target.get("destination"), "target.destination")
        source_repository = (
            f"{_string(destination.get('owner'), 'destination.owner')}/"
            f"{_string(destination.get('repo'), 'destination.repo')}"
        )
        remote_sha256 = _remote_asset_sha256(remote_asset)
        if remote_sha256 is None or not isinstance(remote_asset.get("byte-size"), int):
            return False
        if not _remote_verified_attestation_matches(
            remote_asset,
            raw_name,
            remote_sha256,
            _string(node_attestation.get("signer-workflow"), "signer-workflow"),
            source_repository,
            _plan_commit_sha(plan),
        ):
            return False
        key = (node_id, artifact_id, release_tag, raw_name)
        if key in proof_index:
            proof = proof_index[key]
            if proof is None:
                raise ProofError(
                    "GitHub Release asset proofs conflict for the current plan",
                    code="GITHUB_RELEASE_ASSET_PROOF_CONFLICT",
                    details={
                        "publish-node-id": node_id,
                        "artifact-id": artifact_id,
                        "asset-name": raw_name,
                    },
                )
            if not _github_asset_proof_matches_remote(
                proof,
                remote_asset,
                raw_name,
                remote_sha256,
                _string(node_attestation.get("signer-workflow"), "signer-workflow"),
                source_repository,
                _plan_commit_sha(plan),
            ):
                return False
    return True


def _github_remote_release_state(
    node_id: str,
    node: Mapping[str, object],
    remote: Mapping[str, object],
) -> str:
    """Return a normalized GitHub Release state or fail closed."""
    state = remote.get("release-state")
    if state in {"prerelease", "release"}:
        return str(state)
    identity = _mapping(node.get("resolved-publish-identity"), "identity")
    raise ProofError(
        "GitHub Release remote state is unclassifiable",
        code="REMOTE_NORMALIZATION_FAILED",
        details={
            "publish-node-id": node_id,
            "release-tag": identity.get("release-tag"),
            "remote-release-state": state,
        },
    )


def _remote_verified_attestation_matches(
    remote_asset: Mapping[str, object],
    subject_name: str,
    subject_sha256: str,
    signer_workflow: str,
    source_repository: str,
    source_digest: str,
) -> bool:
    """Return whether current remote-asset attestation verification matches."""
    verified = remote_asset.get("verified-attestation")
    if not isinstance(verified, Mapping):
        return False
    return (
        verified.get("verified") is True
        and verified.get("predicate-type") == _GITHUB_PROOF_PREDICATE
        and verified.get("subject-name") == subject_name
        and verified.get("subject-digest") == f"sha256:{subject_sha256}"
        and verified.get("signer-workflow") == signer_workflow
        and verified.get("source-repository") == source_repository
        and verified.get("source-digest") == source_digest
    )


def _immutable_proof_index(
    proofs: Sequence[Mapping[str, object]],
    plan: Mapping[str, object],
    build_result_receipts: Sequence[Mapping[str, object]],
) -> dict[tuple[str, str, str, str], str | None]:
    digests: dict[tuple[str, str, str, str], set[str]] = defaultdict(set)
    nodes = _publish_nodes(plan)
    receipts = _build_result_receipt_index(build_result_receipts)
    for proof in proofs:
        validate_contract(dict(proof))
        if proof.get("kind") != "immutable-proof":
            continue
        run = _mapping(proof.get("run"), "run")
        if not _is_admissible_run(run):
            continue
        binding = _mapping(proof.get("binding"), "binding")
        artifact = _mapping(proof.get("artifact"), "artifact")
        key = (
            _string(binding.get("publish-node-id"), "publish-node-id"),
            _string(binding.get("artifact-id"), "artifact-id"),
            _string(binding.get("package-name"), "package-name"),
            _string(binding.get("version"), "version"),
        )
        node = nodes.get(key[0])
        if node is None or not _immutable_proof_matches_plan(
            proof,
            binding,
            run,
            plan,
            node,
            key,
        ):
            continue
        if not _immutable_proof_matches_build_result(
            proof,
            binding,
            artifact,
            plan,
            node,
            key,
            receipts,
        ):
            continue
        digests[key].add(_string(artifact.get("sha256"), "sha256"))
    return {
        key: next(iter(values)) if len(values) == 1 else None
        for key, values in digests.items()
    }


def _immutable_proof_matches_plan(
    proof: Mapping[str, object],
    binding: Mapping[str, object],
    run: Mapping[str, object],
    plan: Mapping[str, object],
    node: Mapping[str, object],
    key: tuple[str, str, str, str],
) -> bool:
    """Return whether a proof wrapper is admissible for this plan node."""
    artifact_ids = _string_list(node.get("artifact-ids"), "artifact-ids")
    if key[1] not in artifact_ids:
        return False
    identity = _mapping(
        node.get("resolved-publish-identity"),
        "publish-node.resolved-publish-identity",
    )
    try:
        variant_id = _artifact_variant(plan, key[1])
    except ProofError:
        return False
    return (
        proof.get("plan-id") == _plan_id(plan)
        and proof.get("project-id") == node.get("project-id")
        and proof.get("variant-id") == variant_id
        and run.get("head-sha") == _plan_commit_sha(plan)
        and binding.get("publish-node-id") == key[0]
        and binding.get("artifact-id") == key[1]
        and binding.get("package-name") == identity.get("package-name")
        and binding.get("version") == identity.get("version")
    )


def _build_result_receipt_index(
    receipts: Sequence[Mapping[str, object]],
) -> dict[tuple[str, int], Mapping[str, object]]:
    indexed: dict[tuple[str, int], Mapping[str, object]] = {}
    expected_keys = {
        "build-result-artifact-name",
        "build-result-artifact-id",
        "build-result",
    }
    for receipt in receipts:
        if set(receipt) != expected_keys:
            raise ProofError(
                "build-result receipt input must be a closed object",
                code="IMMUTABLE_BUILD_RESULT_RECEIPT_INVALID",
            )
        name = _string(
            receipt.get("build-result-artifact-name"),
            "build-result-artifact-name",
        )
        artifact_id = _int(
            receipt.get("build-result-artifact-id"),
            "build-result-artifact-id",
        )
        build_result = _mapping(receipt.get("build-result"), "build-result")
        validate_contract(dict(build_result))
        key = (name, artifact_id)
        if key in indexed:
            raise ProofError(
                "build-result receipt inputs contain duplicate artifact identities",
                code="IMMUTABLE_BUILD_RESULT_RECEIPT_CONFLICT",
                details={
                    "build-result-artifact-name": name,
                    "build-result-artifact-id": artifact_id,
                },
            )
        indexed[key] = build_result
    return indexed


def _immutable_proof_matches_build_result(
    proof: Mapping[str, object],
    binding: Mapping[str, object],
    artifact: Mapping[str, object],
    plan: Mapping[str, object],
    node: Mapping[str, object],
    key: tuple[str, str, str, str],
    build_results: Mapping[tuple[str, int], Mapping[str, object]],
) -> bool:
    """Return whether a wrapper is supported by its referenced build-result."""
    build_result_name = _string(
        proof.get("build-result-artifact-name"),
        "build-result-artifact-name",
    )
    build_result_id = _int(
        proof.get("build-result-artifact-id"),
        "build-result-artifact-id",
    )
    build_result = build_results.get((build_result_name, build_result_id))
    if build_result is None:
        return False
    try:
        variant_id = _artifact_variant(plan, key[1])
        artifacts = _mapping(build_result.get("artifacts"), "build-result.artifacts")
        receipt = _mapping(
            artifacts.get(key[1]),
            f"build-result.artifacts[{key[1]}]",
        )
    except ProofError:
        return False
    return (
        build_result.get("plan-id") == _plan_id(plan)
        and build_result.get("project-id") == node.get("project-id")
        and build_result.get("variant-id") == variant_id
        and binding.get("artifact-id") == key[1]
        and receipt.get("sha256") == artifact.get("sha256")
        and receipt.get("byte-size") == artifact.get("byte-size")
    )


def _github_asset_proof_index(
    proofs: Sequence[Mapping[str, object]],
    plan: Mapping[str, object],
) -> dict[tuple[str, str, str, str], Mapping[str, object]]:
    indexed: dict[
        tuple[str, str, str, str],
        list[Mapping[str, object]],
    ] = defaultdict(list)
    nodes = _publish_nodes(plan)
    for proof in proofs:
        validate_contract(dict(proof))
        if proof.get("kind") != "github-release-asset-proof":
            continue
        run = _mapping(proof.get("run"), "run")
        if not _is_admissible_run(run):
            continue
        binding = _mapping(proof.get("binding"), "binding")
        key = (
            _string(binding.get("publish-node-id"), "publish-node-id"),
            _string(binding.get("artifact-id"), "artifact-id"),
            _string(binding.get("release-tag"), "release-tag"),
            _string(binding.get("asset-name"), "asset-name"),
        )
        node = nodes.get(key[0])
        if node is None or not _github_asset_proof_matches_plan(
            proof,
            binding,
            run,
            plan,
            node,
            key,
        ):
            continue
        indexed[key].append(proof)
    proof_index: dict[tuple[str, str, str, str], Mapping[str, object]] = {}
    for key, values in indexed.items():
        if not _same_github_asset_proofs(values):
            raise ProofError(
                "GitHub Release asset proofs conflict for the current plan",
                code="GITHUB_RELEASE_ASSET_PROOF_CONFLICT",
                details={
                    "publish-node-id": key[0],
                    "artifact-id": key[1],
                    "release-tag": key[2],
                    "asset-name": key[3],
                },
            )
        proof_index[key] = values[0]
    return proof_index


def _github_asset_proof_matches_plan(
    proof: Mapping[str, object],
    binding: Mapping[str, object],
    run: Mapping[str, object],
    plan: Mapping[str, object],
    node: Mapping[str, object],
    key: tuple[str, str, str, str],
) -> bool:
    """Return whether a GitHub Release proof wrapper fits this plan node."""
    target = _target(plan, node)
    if target.get("family") != "github-release":
        return False
    artifact_ids = _string_list(node.get("artifact-ids"), "artifact-ids")
    if key[1] not in artifact_ids:
        return False
    identity = _mapping(node.get("resolved-publish-identity"), "identity")
    projection = _mapping(node.get("projection"), "projection")
    names = _mapping(
        projection.get("asset-names-by-artifact-id"),
        "asset-names-by-artifact-id",
    )
    try:
        variant_id = _artifact_variant(plan, key[1])
    except ProofError:
        return False
    return (
        proof.get("plan-id") == _plan_id(plan)
        and proof.get("project-id") == node.get("project-id")
        and proof.get("variant-id") == variant_id
        and run.get("head-sha") == _plan_commit_sha(plan)
        and binding.get("publish-node-id") == key[0]
        and binding.get("artifact-id") == key[1]
        and binding.get("release-tag") == identity.get("release-tag")
        and binding.get("asset-name") == names.get(key[1])
    )


def _github_asset_proof_matches_remote(
    proof: Mapping[str, object],
    remote_asset: Mapping[str, object],
    asset_name: str,
    remote_sha256: str,
    signer_workflow: str,
    source_repository: str,
    source_digest: str,
) -> bool:
    """Return whether an admissible wrapper corroborates current remote evidence."""
    artifact = _mapping(proof.get("artifact"), "proof.artifact")
    attestation = _mapping(proof.get("attestation"), "proof.attestation")
    return (
        remote_asset.get("byte-size") == artifact.get("byte-size")
        and artifact.get("sha256") == remote_sha256
        and attestation.get("predicate-type") == _GITHUB_PROOF_PREDICATE
        and attestation.get("subject-name") == asset_name
        and attestation.get("subject-digest") == f"sha256:{remote_sha256}"
        and attestation.get("signer-workflow") == signer_workflow
        and attestation.get("source-repository") == source_repository
        and attestation.get("source-digest") == source_digest
    )


def _same_github_asset_proofs(values: Sequence[Mapping[str, object]]) -> bool:
    if not values:
        return False
    first_artifact = _mapping(values[0].get("artifact"), "artifact")
    first_attestation = _mapping(values[0].get("attestation"), "attestation")
    artifact_fields = ("sha256", "byte-size")
    attestation_fields = (
        "predicate-type",
        "subject-name",
        "subject-digest",
        "signer-workflow",
        "source-repository",
        "source-digest",
    )
    for value in values[1:]:
        artifact = _mapping(value.get("artifact"), "artifact")
        attestation = _mapping(value.get("attestation"), "attestation")
        if any(
            artifact.get(field) != first_artifact.get(field)
            for field in artifact_fields
        ):
            return False
        if any(
            attestation.get(field) != first_attestation.get(field)
            for field in attestation_fields
        ):
            return False
    return True


def _require_publish_result_matches_request(
    request: Mapping[str, object],
    result: Mapping[str, object],
    node: Mapping[str, object],
) -> None:
    """Fail closed unless publish result identity matches the request."""
    checks = (
        ("plan-id", request.get("plan-id")),
        ("project-id", node.get("project-id")),
        ("publish-node-id", request.get("publish-node-id")),
        (
            "target-instance-snapshot-id",
            node.get("target-instance-snapshot-id"),
        ),
    )
    for field, expected in checks:
        _require_equal(
            result.get(field),
            expected,
            f"publish result {field} does not match the request",
        )
    request_identity = _mapping(
        node.get("resolved-publish-identity"),
        "publish-node.resolved-publish-identity",
    )
    result_identity = _mapping(
        result.get("resolved-publish-identity"),
        "publish-result.resolved-publish-identity",
    )
    _require_equal(
        dict(result_identity),
        dict(request_identity),
        "publish result identity does not match the request",
    )


def _remote_asset_sha256(asset: Mapping[str, object]) -> str | None:
    """Normalize GitHub REST release asset digest evidence to a SHA-256 hex value."""
    digest = asset.get("digest")
    match = (
        _GITHUB_ASSET_DIGEST_RE.fullmatch(digest) if isinstance(digest, str) else None
    )
    return match.group(1) if match is not None else None


def _remote_members(value: object, node_id: str) -> dict[str, str]:
    if value is None:
        return {}
    if not isinstance(value, Sequence) or isinstance(value, (str, bytes)):
        raise ProofError(
            "remote members must be an array",
            details={"publish-node-id": node_id},
        )
    members: dict[str, str] = {}
    for item in value:
        if not isinstance(item, Mapping):
            raise ProofError("remote member must be an object")
        filename = _string(item.get("filename"), "remote filename")
        digest = _string(item.get("sha256"), "remote sha256")
        if filename in members and members[filename] != digest:
            raise ProofError(
                "remote members contain conflicting duplicate filenames",
                details={"publish-node-id": node_id, "filename": filename},
            )
        members[filename] = digest
    return members


def _remote_assets(value: object, node_id: str) -> dict[str, Mapping[str, object]]:
    if not isinstance(value, Sequence) or isinstance(value, (str, bytes)):
        raise ProofError(
            "remote assets must be an array",
            details={"publish-node-id": node_id},
        )
    assets: dict[str, Mapping[str, object]] = {}
    for item in value:
        if not isinstance(item, Mapping):
            raise ProofError("remote asset must be an object")
        name = _string(item.get("name"), "remote asset name")
        if name in assets:
            raise ProofError(
                "remote assets contain duplicate names",
                details={"publish-node-id": node_id, "name": name},
            )
        assets[name] = item
    return assets


def _final_distribution_names(
    node: Mapping[str, object], node_id: str
) -> dict[str, str]:
    projection = _mapping(
        node.get("projection"),
        f"publish-nodes[{node_id}].projection",
    )
    raw = _mapping(
        projection.get("final-distribution-filenames-by-artifact-id"),
        "final-distribution-filenames-by-artifact-id",
    )
    expected = set(_string_list(node.get("artifact-ids"), "artifact-ids"))
    if set(raw) != expected:
        raise ProofError(
            "immutable node lacks a complete final filename map",
            details={"publish-node-id": node_id},
        )
    names: dict[str, str] = {}
    for artifact_id, value in raw.items():
        if not isinstance(artifact_id, str) or not isinstance(value, str) or not value:
            raise ProofError("final distribution filenames must be non-empty strings")
        if PurePosixPath(value).name != value:
            raise ProofError("final distribution filenames must be basenames")
        names[artifact_id] = value
    return names


def _artifact_receipt(value: Mapping[str, object]) -> Json:
    receipt: Json = {
        "bundle-relative-path": _string(
            value.get("bundle-relative-path"), "bundle-relative-path"
        ),
        "sha256": _string(value.get("sha256"), "sha256"),
        "byte-size": _int(value.get("byte-size"), "byte-size"),
    }
    archive = value.get("archive")
    if isinstance(archive, Mapping):
        receipt["archive"] = dict(archive)
    return receipt


def _require_live_run(run: Mapping[str, object]) -> None:
    if not _is_admissible_run(run):
        raise ProofError(
            "proof requires live non-dry-run, non-validation run provenance",
            code="PROOF_RUN_NOT_ADMISSIBLE",
        )


def _is_admissible_run(run: Mapping[str, object]) -> bool:
    return (
        run.get("live") is True
        and run.get("dry-run") is False
        and run.get("validation-only") is False
    )


def _publish_nodes(plan: Mapping[str, object]) -> dict[str, Mapping[str, object]]:
    graph = _mapping(plan.get("graph"), "graph")
    nodes = _mapping(graph.get("publish-nodes"), "graph.publish-nodes")
    typed_nodes: dict[str, Mapping[str, object]] = {}
    for key, value in nodes.items():
        if not isinstance(key, str):
            raise ProofError("publish node ids must be strings")
        typed_nodes[key] = _mapping(value, f"publish-nodes[{key}]")
    return typed_nodes


def _publish_node(plan: Mapping[str, object], node_id: str) -> Mapping[str, object]:
    nodes = _publish_nodes(plan)
    if node_id not in nodes:
        raise ProofError("publish node is not present in the plan")
    return nodes[node_id]


def _target(
    plan: Mapping[str, object], node: Mapping[str, object]
) -> Mapping[str, object]:
    graph = _mapping(plan.get("graph"), "graph")
    targets = _mapping(
        graph.get("target-instance-snapshots"),
        "graph.target-instance-snapshots",
    )
    target_id = _string(
        node.get("target-instance-snapshot-id"), "target-instance-snapshot-id"
    )
    return _mapping(
        targets.get(target_id),
        f"target-instance-snapshots[{target_id}]",
    )


def _capability(target: Mapping[str, object], key: str) -> str:
    capabilities = _mapping(target.get("capabilities"), "target.capabilities")
    return _string(capabilities.get(key), f"target.capabilities.{key}")


def _plan_id(plan: Mapping[str, object]) -> str:
    envelope = _mapping(plan.get("envelope"), "envelope")
    return _string(envelope.get("plan-id"), "envelope.plan-id")


def _plan_commit_sha(plan: Mapping[str, object]) -> str:
    envelope = _mapping(plan.get("envelope"), "envelope")
    return _string(envelope.get("commit-sha"), "envelope.commit-sha")


def _artifact_variant(plan: Mapping[str, object], artifact_id: str) -> str:
    graph = _mapping(plan.get("graph"), "graph")
    artifacts = _mapping(graph.get("artifacts"), "graph.artifacts")
    artifact = _mapping(artifacts.get(artifact_id), f"artifacts[{artifact_id}]")
    return _string(artifact.get("variant-id"), "variant-id")


def _artifact_variant_from_request(artifact_input: Mapping[str, object]) -> str:
    artifact = _mapping(artifact_input.get("artifact"), "artifact")
    return _string(artifact.get("variant-id"), "variant-id")


def _mapping(value: object, path: str) -> Mapping[str, object]:
    if not isinstance(value, Mapping):
        raise ProofError(f"{path} must be an object")
    return value


def _string(value: object, path: str) -> str:
    if not isinstance(value, str) or not value:
        raise ProofError(f"{path} must be a non-empty string")
    return value


def _int(value: object, path: str) -> int:
    if not isinstance(value, int) or isinstance(value, bool) or value < 0:
        raise ProofError(f"{path} must be a non-negative integer")
    return value


def _string_list(value: object, path: str) -> list[str]:
    if not isinstance(value, list) or not all(
        isinstance(item, str) and item for item in value
    ):
        raise ProofError(f"{path} must be an array of non-empty strings")
    return value


def _require_equal(left: object, right: object, message: str) -> None:
    if left != right:
        raise ProofError(message, details={"expected": right, "actual": left})


def load_json(path: str) -> Json:
    """Load one JSON object file."""
    document = json.loads(Path(path).read_text(encoding="utf-8"))
    if not isinstance(document, dict):
        raise ProofError(f"{path} must contain a JSON object")
    return document
