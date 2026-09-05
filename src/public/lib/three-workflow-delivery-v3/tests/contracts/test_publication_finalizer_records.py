"""Scenario contracts for publication and exact-satisfied finalization."""

# ruff: noqa: D103

from __future__ import annotations

from copy import deepcopy
from dataclasses import FrozenInstanceError, replace

import pytest
from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.records.artifacts import ArtifactReference
from three_workflow_delivery_v3.records.release import (
    ApprovalBoundary,
    BuddyExecutionIdentity,
    DestinationReadback,
    DirectPredecessor,
    ExactSatisfiedFinalizationProof,
    GovernanceProof,
    MutationMayHaveStartedMarker,
    PackageControlProof,
    PackageControlSubject,
    ProfileMatchEvidence,
    PublicationDiagnostics,
    PublicationResult,
    ReleaseAttemptIdentity,
    admit_release_record,
    release_record_digest,
)
from three_workflow_delivery_v3.records.release_transport import (
    ReleaseAdmissionBindings,
    release_record_from_document,
)

_DIAGNOSTIC_COUNT_LIMIT = 16
_DIAGNOSTIC_ENTRY_BYTE_LIMIT = 2048
_DIAGNOSTIC_TOTAL_BYTE_LIMIT = 8192
_TARGET = "a" * 40
_ELIGIBILITY_ANCESTOR = "b" * 40
_CURRENT_MAIN_SHA = "c" * 40
_ALTERNATE_TARGET = "9" * 40
_WORKFLOW_RUN_ID = 101
_ALTERNATE_WORKFLOW_RUN_ID = 202
_CONTROL = f"workflow-delivery-v3:{_TARGET}"

_PACKAGE = "@hcoona/hcoona-release-smoke-npm"
_VERSION = "0.0.0-wdv3-acceptance.1"
_TAG = f"buddy-sha-{_TARGET}"
_REGISTRY = "https://npm.pkg.github.com"
_TARBALL = "out/hcoona-hcoona-release-smoke-npm-0.0.0-wdv3-acceptance.1.tgz"
_API_ENDPOINT = (
    "https://api.github.com/users/hcoona/packages/npm/hcoona-release-smoke-npm"
)
_REGISTRY_ENDPOINT = (
    "https://npm.pkg.github.com/@hcoona%2Fhcoona-release-smoke-npm"
)
_ENDPOINTS = (_API_ENDPOINT, _REGISTRY_ENDPOINT)


def _sha256(character):
    return "sha256:" + (character * 64)


def _sha512(character):
    return "sha512:" + (character * 128)


_ARTIFACT_DIGEST = _sha256("1")
_AUTHORIZATION_PAYLOAD_DIGEST = _sha256("2")
_MARKER_PAYLOAD_DIGEST = _sha256("3")
_SNAPSHOT_PAYLOAD_DIGEST = _sha256("4")
_CANONICAL_GOVERNANCE_DIGEST = _sha256("5")
_API_RESPONSE_DIGEST = _sha256("6")
_REGISTRY_RESPONSE_DIGEST = _sha256("7")
_PROFILE_DIGEST = _sha256("8")
_CONTENT_SHA256 = _sha256("9")
_CONTENT_SHA512 = _sha512("a")
_WITNESS_DIGEST = _sha256("b")
_VERSION_RESPONSE_DIGEST = _sha256("c")
_TAG_RESPONSE_DIGEST = _sha256("d")
_PUBLICATION_RESPONSE_DIGEST = _sha256("e")

_GOVERNANCE_OBSERVED_AT = "2026-09-05T01:22:33.100Z"
_PACKAGE_CONTROL_OBSERVED_AT = "2026-09-05T01:22:34.200Z"
_PROFILE_MATCHED_AT = "2026-09-05T01:22:35.300Z"
_READBACK_OBSERVED_AT = "2026-09-05T01:22:36.400Z"
_PROVED_AT = "2026-09-05T01:22:37.500Z"
_GOVERNANCE_EXPIRES_AT = "2026-09-05T01:30:00Z"

_GOVERNANCE_PROVENANCE = (
    ("blob-oid", "d" * 40),
    ("canonical-content-digest", _CANONICAL_GOVERNANCE_DIGEST),
    ("eligibility-main-sha", _ELIGIBILITY_ANCESTOR),
    ("git-object-format", "sha1"),
    (
        "path",
        (".github/workflow-delivery/governance/hcoona-release-smoke-npm.json"),
    ),
    ("ref", "refs/heads/main"),
    ("repository", "hcoona/three"),
)
_PACKAGE_FACTS = (
    ("exposed-access", ("admin", "read", "write")),
    ("owner", ("hcoona",)),
    ("repository-association", ("hcoona/three",)),
    ("visibility", ("public",)),
)
_PACKAGE_RESPONSE_DIGESTS = (
    (_API_ENDPOINT, _API_RESPONSE_DIGEST),
    (_REGISTRY_ENDPOINT, _REGISTRY_RESPONSE_DIGEST),
)
_PROFILE_COMMAND = (
    "npm",
    "publish",
    _TARBALL,
    "--registry",
    _REGISTRY,
    "--tag",
    _TAG,
    "--ignore-scripts",
    "--fetch-retries=0",
)
_PROFILE_CONFIGURATION = (
    ("fetch-retries", "0"),
    ("ignore-scripts", "true"),
    ("registry", _REGISTRY),
    ("tag", _TAG),
)

_READBACK_FACTS = (
    _CONTENT_SHA256,
    _CONTENT_SHA512,
    _WITNESS_DIGEST,
    _TARGET,
)
_REFERENCE_FIELDS = (
    "artifact-id",
    "artifact-digest",
    "artifact-url",
    "payload-path",
    "payload-digest",
)
_PREDECESSOR_KINDS = (
    "publication-result",
    "mutation-marker",
    "exact-satisfied-finalization-proof",
    "zero-action-publication-snapshot",
    "publication-authorization",
    "approval-bundle",
    "action-bearing-publication-snapshot",
    "blocking-observation",
    "qualification-decision",
)
_DEFAULT = object()


class _DerivedArtifactReference(ArtifactReference):
    __slots__ = ()


def _artifact_reference(
    *,
    payload_path="publication/publication-authorization.json",
    payload_digest=_AUTHORIZATION_PAYLOAD_DIGEST,
):
    return ArtifactReference(
        artifact_id=701,
        artifact_digest=_ARTIFACT_DIGEST,
        artifact_url="https://example.test/actions/artifacts/701",
        payload_path=payload_path,
        payload_digest=payload_digest,
    )


def _derived_artifact_reference():
    reference = _artifact_reference()
    return _DerivedArtifactReference(
        artifact_id=reference.artifact_id,
        artifact_digest=reference.artifact_digest,
        artifact_url=reference.artifact_url,
        payload_path=reference.payload_path,
        payload_digest=reference.payload_digest,
    )


def _attempt(*, target=_TARGET, workflow_run_id=_WORKFLOW_RUN_ID):
    return ReleaseAttemptIdentity(
        execution=BuddyExecutionIdentity(
            channel="buddy",
            release_unit="hcoona-release-smoke-npm",
            target=target,
        ),
        workflow_run_id=workflow_run_id,
    )


def _approval_boundary():
    return ApprovalBoundary(
        environment="workflow-delivery-v3-buddy-approval",
        job="approve-publication",
        sentinel_name="WDV3_APPROVAL_ENVIRONMENT_MARKER",
        sentinel_value="workflow-delivery-v3-buddy-approval/v1",
        sentinel_result="success",
    )


def _governance_proof(*, object_format="sha1"):
    if object_format == "sha256":
        provenance = tuple(
            (
                name,
                (
                    "d" * 64
                    if name == "blob-oid"
                    else "b" * 64
                    if name == "eligibility-main-sha"
                    else "sha256"
                    if name == "git-object-format"
                    else value
                ),
            )
            for name, value in _GOVERNANCE_PROVENANCE
        )
        current_main_sha = "c" * 64
    else:
        provenance = _GOVERNANCE_PROVENANCE
        current_main_sha = _CURRENT_MAIN_SHA
    return GovernanceProof(
        provenance=provenance,
        current_main_sha=current_main_sha,
        observed_at=_GOVERNANCE_OBSERVED_AT,
        expires_at=_GOVERNANCE_EXPIRES_AT,
        live_enabled=True,
    )


def _package_control_subject(*, package=_PACKAGE):
    return PackageControlSubject(
        destination_id="github-packages",
        registry=_REGISTRY,
        normalized_package=package,
    )


def _package_control_proof(*, subject=None):
    return PackageControlProof(
        subject=subject or _package_control_subject(),
        observed_at=_PACKAGE_CONTROL_OBSERVED_AT,
        endpoints=_ENDPOINTS,
        facts=_PACKAGE_FACTS,
        response_digests=_PACKAGE_RESPONSE_DIGESTS,
    )


def _profile_match():
    return ProfileMatchEvidence(
        destination_operation_profile_digest=_PROFILE_DIGEST,
        node_version="24.19.0",
        npm_version="11.17.0",
        command=_PROFILE_COMMAND,
        configuration=_PROFILE_CONFIGURATION,
        matched_at=_PROFILE_MATCHED_AT,
    )


def _readback(  # noqa: PLR0913
    *,
    classification="exact-satisfied",
    tag_state="present",
    evidence=None,
    witness_target=_TARGET,
    package=_PACKAGE,
    observed_at=_READBACK_OBSERVED_AT,
):
    if evidence is None:
        if classification == "absent":
            evidence = (None, None, None, None)
        elif classification in {"exact-satisfied", "conflicting"}:
            evidence = (
                _CONTENT_SHA256,
                _CONTENT_SHA512,
                _WITNESS_DIGEST,
                witness_target,
            )
        elif classification == "partial":
            evidence = (_CONTENT_SHA256, None, None, witness_target)
        elif classification == "unprovable":
            evidence = (None, _CONTENT_SHA512, _WITNESS_DIGEST, None)
        else:
            evidence = (None, None, None, None)
    (
        content_sha256,
        content_sha512,
        witness_digest,
        evidence_target,
    ) = evidence
    return DestinationReadback(
        package=package,
        version=_VERSION,
        classification=classification,
        content_sha256=content_sha256,
        content_sha512=content_sha512,
        witness_digest=witness_digest,
        witness_target=evidence_target,
        tag=_TAG,
        tag_state=tag_state,
        tag_version=_VERSION if tag_state == "present" else None,
        observed_at=observed_at,
        response_digests=(
            ("exact-version", _VERSION_RESPONSE_DIGEST),
            ("target-tag", _TAG_RESPONSE_DIGEST),
        ),
    )


def _diagnostics():
    return PublicationDiagnostics(
        entries=(
            "npm publish completed",
            "authoritative exact-version readback observed",
        ),
        truncated=False,
    )


def _marker(*, attempt=None):
    selected_attempt = attempt or _attempt()
    return MutationMayHaveStartedMarker(
        attempt=selected_attempt,
        publication_authorization_reference=_artifact_reference(),
        governance_proof=_governance_proof(),
        package_control_proof=_package_control_proof(),
        profile_match=_profile_match(),
        producer="publish-github-packages",
        control=(f"workflow-delivery-v3:{selected_attempt.execution.target}"),
        workflow_run_id=selected_attempt.workflow_run_id,
    )


def _publication_result(*, attempt=None, readback=_DEFAULT):
    selected_attempt = attempt or _attempt()
    selected_readback = (
        _readback(witness_target=selected_attempt.execution.target)
        if readback is _DEFAULT
        else readback
    )
    return PublicationResult(
        attempt=selected_attempt,
        mutation_marker_reference=_artifact_reference(
            payload_path="publication/mutation-marker.json",
            payload_digest=_MARKER_PAYLOAD_DIGEST,
        ),
        command_classification="definitive-success",
        post_action_readback=selected_readback,
        result="published",
        mutation_classification="mutated",
        response_identity=_PUBLICATION_RESPONSE_DIGEST,
        diagnostics=_diagnostics(),
        producer="publish-github-packages",
        control=(f"workflow-delivery-v3:{selected_attempt.execution.target}"),
        workflow_run_id=selected_attempt.workflow_run_id,
    )


def _failed_result(command, mutation, *, readback=None):
    return replace(
        _publication_result(),
        command_classification=command,
        post_action_readback=readback,
        result="failed",
        mutation_classification=mutation,
        response_identity=(
            None if command == "not-initiated" else _PUBLICATION_RESPONSE_DIGEST
        ),
    )


def _finalization_proof(*, attempt=None):
    selected_attempt = attempt or _attempt()
    return ExactSatisfiedFinalizationProof(
        attempt=selected_attempt,
        publication_snapshot_reference=_artifact_reference(
            payload_path="publication/publication-snapshot.json",
            payload_digest=_SNAPSHOT_PAYLOAD_DIGEST,
        ),
        governance_proof=_governance_proof(),
        package_control_proof=_package_control_proof(),
        exact_version_readback=_readback(
            witness_target=selected_attempt.execution.target,
        ),
        proved_at=_PROVED_AT,
        producer="prove-exact-satisfied",
        control=(f"workflow-delivery-v3:{selected_attempt.execution.target}"),
        workflow_run_id=selected_attempt.workflow_run_id,
    )


def _predecessor():
    return DirectPredecessor(
        kind="publication-result",
        reference=_artifact_reference(
            payload_path="publication/publication-result.json",
            payload_digest=_sha256("f"),
        ),
    )


def _fact_values(proof, name, values):
    return tuple(
        (key, values if key == name else current)
        for key, current in proof.facts
    )


def _nested_member(document, path):
    current = document
    for part in path:
        current = current[part]
    return current


def _set_nested_member(document, path, value):
    parent = _nested_member(document, path[:-1])
    parent[path[-1]] = value


def test_publication_finalizer_records_are_frozen_and_slotted():
    scenarios = (
        (_approval_boundary, "environment"),
        (_governance_proof, "provenance"),
        (_package_control_subject, "normalized_package"),
        (_package_control_proof, "facts"),
        (_profile_match, "command"),
        (_readback, "classification"),
        (_diagnostics, "entries"),
        (_predecessor, "kind"),
        (_marker, "producer"),
        (_publication_result, "result"),
        (_finalization_proof, "proved_at"),
    )

    for factory, field_name in scenarios:
        record = factory()
        assert "__slots__" in type(record).__dict__
        assert not hasattr(record, "__dict__")
        with pytest.raises(FrozenInstanceError, match="cannot assign"):
            setattr(record, field_name, getattr(record, field_name))


def test_approval_boundary_emits_the_exact_authority_projection():
    assert _approval_boundary().to_document() == {
        "environment": "workflow-delivery-v3-buddy-approval",
        "job": "approve-publication",
        "sentinel-name": "WDV3_APPROVAL_ENVIRONMENT_MARKER",
        "sentinel-value": "workflow-delivery-v3-buddy-approval/v1",
        "sentinel-result": "success",
    }


def test_governance_proof_emits_compact_canonical_pairs():
    document = _governance_proof().to_document()

    assert document == {
        "provenance": [list(pair) for pair in _GOVERNANCE_PROVENANCE],
        "current-main-sha": _CURRENT_MAIN_SHA,
        "observed-at": _GOVERNANCE_OBSERVED_AT,
        "expires-at": _GOVERNANCE_EXPIRES_AT,
        "live-enabled": True,
    }
    assert all(type(pair) is list for pair in document["provenance"])


def test_package_control_proof_emits_first_slice_authority_shape():
    document = _package_control_proof().to_document()

    assert document == {
        "subject": {
            "destination-id": "github-packages",
            "registry": _REGISTRY,
            "normalized-package": _PACKAGE,
        },
        "observed-at": _PACKAGE_CONTROL_OBSERVED_AT,
        "endpoints": list(_ENDPOINTS),
        "facts": [[name, list(values)] for name, values in _PACKAGE_FACTS],
        "response-digests": [list(pair) for pair in _PACKAGE_RESPONSE_DIGESTS],
    }
    assert document["endpoints"][0].startswith(
        "https://api.github.com/users/hcoona/packages/",
    )


def test_profile_match_emits_resolved_first_slice_command():
    document = _profile_match().to_document()

    assert document == {
        "destination-operation-profile-digest": _PROFILE_DIGEST,
        "node-version": "24.19.0",
        "npm-version": "11.17.0",
        "command": list(_PROFILE_COMMAND),
        "configuration": [list(pair) for pair in _PROFILE_CONFIGURATION],
        "matched-at": _PROFILE_MATCHED_AT,
    }
    assert document["command"][-2:] == [
        "--ignore-scripts",
        "--fetch-retries=0",
    ]


def test_destination_readback_emits_exact_version_and_paired_tag_shape():
    document = _readback().to_document()

    assert document == {
        "package": _PACKAGE,
        "version": _VERSION,
        "classification": "exact-satisfied",
        "content-sha256": _CONTENT_SHA256,
        "content-sha512": _CONTENT_SHA512,
        "witness-digest": _WITNESS_DIGEST,
        "witness-target": _TARGET,
        "tag": _TAG,
        "tag-state": "present",
        "tag-version": _VERSION,
        "observed-at": _READBACK_OBSERVED_AT,
        "response-digests": [
            ["exact-version", _VERSION_RESPONSE_DIGEST],
            ["target-tag", _TAG_RESPONSE_DIGEST],
        ],
    }


def test_publication_diagnostics_emits_bounded_ordered_entries():
    assert _diagnostics().to_document() == {
        "entries": [
            "npm publish completed",
            "authoritative exact-version readback observed",
        ],
        "truncated": False,
    }


def test_direct_predecessor_emits_exact_reference_slots():
    document = _predecessor().to_document()

    assert tuple(document) == ("kind", "reference")
    assert tuple(document["reference"]) == _REFERENCE_FIELDS
    assert document["reference"] == {
        "artifact-id": 701,
        "artifact-digest": _ARTIFACT_DIGEST,
        "artifact-url": "https://example.test/actions/artifacts/701",
        "payload-path": "publication/publication-result.json",
        "payload-digest": _sha256("f"),
    }


@pytest.mark.parametrize(
    ("factory", "schema", "fields", "reference_field"),
    [
        (
            _marker,
            ("workflow-delivery/v3/github-packages-mutation-may-have-started"),
            (
                "schema",
                "attempt",
                "publication-authorization-reference",
                "governance-proof",
                "package-control-proof",
                "profile-match",
                "producer",
                "control",
                "workflow-run-id",
            ),
            "publication-authorization-reference",
        ),
        (
            _publication_result,
            "workflow-delivery/v3/publication-result",
            (
                "schema",
                "attempt",
                "mutation-marker-reference",
                "command-classification",
                "post-action-readback",
                "result",
                "mutation-classification",
                "response-identity",
                "diagnostics",
                "producer",
                "control",
                "workflow-run-id",
            ),
            "mutation-marker-reference",
        ),
        (
            _finalization_proof,
            "workflow-delivery/v3/exact-satisfied-finalization-proof",
            (
                "schema",
                "attempt",
                "publication-snapshot-reference",
                "governance-proof",
                "package-control-proof",
                "exact-version-readback",
                "proved-at",
                "producer",
                "control",
                "workflow-run-id",
            ),
            "publication-snapshot-reference",
        ),
    ],
)
def test_top_level_records_emit_closed_canonical_shapes(
    factory,
    schema,
    fields,
    reference_field,
):
    document = factory().to_document()

    assert document["schema"] == schema
    assert tuple(document) == fields
    assert tuple(document[reference_field]) == _REFERENCE_FIELDS
    assert "schema" not in document[reference_field]


@pytest.mark.parametrize(
    ("field_name", "replacement"),
    [
        ("environment", "workflow-delivery-v3-buddy-approval-v2"),
        ("job", "publish"),
        ("sentinel_name", "WDV3_OTHER_MARKER"),
        ("sentinel_value", "workflow-delivery-v3-buddy-approval/v2"),
        ("sentinel_result", "failure"),
    ],
)
def test_approval_boundary_rejects_any_modified_constant(
    field_name,
    replacement,
):
    with pytest.raises(ValueError, match="is not exact"):
        replace(
            _approval_boundary(),
            **{field_name: replacement},
        )


@pytest.mark.parametrize(
    ("object_format", "expected_length"),
    [("sha1", 40), ("sha256", 64)],
)
def test_governance_proof_supports_exact_sha1_and_sha256_generations(
    object_format,
    expected_length,
):
    proof = _governance_proof(object_format=object_format)
    document = proof.to_document()

    assert dict(proof.provenance)["git-object-format"] == object_format
    assert len(document["current-main-sha"]) == expected_length
    assert document["provenance"][3] == [
        "git-object-format",
        object_format,
    ]


@pytest.mark.parametrize(
    "provenance",
    [
        _GOVERNANCE_PROVENANCE[:-1],
        (*_GOVERNANCE_PROVENANCE, ("unexpected", "value")),
        (
            *_GOVERNANCE_PROVENANCE[:2],
            _GOVERNANCE_PROVENANCE[1],
            *_GOVERNANCE_PROVENANCE[2:],
        ),
        (
            _GOVERNANCE_PROVENANCE[1],
            _GOVERNANCE_PROVENANCE[0],
            *_GOVERNANCE_PROVENANCE[2:],
        ),
    ],
    ids=("missing", "extra", "duplicate", "unsorted"),
)
def test_governance_proof_rejects_open_or_noncanonical_provenance(
    provenance,
):
    with pytest.raises(ValueError, match=r"provenance|incomplete"):
        replace(_governance_proof(), provenance=provenance)


@pytest.mark.parametrize(
    ("provenance", "current_main_sha"),
    [
        (
            tuple(
                (
                    name,
                    "sha512" if name == "git-object-format" else value,
                )
                for name, value in _GOVERNANCE_PROVENANCE
            ),
            _CURRENT_MAIN_SHA,
        ),
        (_GOVERNANCE_PROVENANCE, "C" * 40),
        (_GOVERNANCE_PROVENANCE, "c" * 39),
    ],
    ids=("unknown-object-format", "uppercase", "wrong-length"),
)
def test_governance_proof_rejects_malformed_current_main_identity(
    provenance,
    current_main_sha,
):
    with pytest.raises(ValueError, match="current main SHA is malformed"):
        replace(
            _governance_proof(),
            provenance=provenance,
            current_main_sha=current_main_sha,
        )


@pytest.mark.parametrize("field_name", ["observed_at", "expires_at"])
def test_governance_proof_requires_canonical_utc_timestamp_shape(
    field_name,
):
    with pytest.raises(ValueError, match="RFC 3339 UTC timestamp"):
        replace(
            _governance_proof(),
            **{field_name: "2026-09-05 01:22:33Z"},
        )


def test_governance_proof_preserves_fractional_ordering():
    proof = replace(
        _governance_proof(),
        observed_at="2026-09-05T01:22:33.09Z",
        expires_at="2026-09-05T01:22:33.1Z",
    )

    assert proof.to_document()["observed-at"].endswith(".09Z")
    assert proof.to_document()["expires-at"].endswith(".1Z")


@pytest.mark.parametrize(
    ("observed_at", "expires_at"),
    [
        (
            "2026-09-05T01:22:33.1Z",
            "2026-09-05T01:22:33.10Z",
        ),
        (
            "2026-09-05T01:22:33.9Z",
            "2026-09-05T01:22:33.10Z",
        ),
    ],
    ids=("equal-at-different-precision", "reversed"),
)
def test_governance_proof_rejects_non_strict_fractional_interval(
    observed_at,
    expires_at,
):
    with pytest.raises(ValueError, match="must precede expiry"):
        replace(
            _governance_proof(),
            observed_at=observed_at,
            expires_at=expires_at,
        )


@pytest.mark.parametrize("live_enabled", [False, 1])
def test_governance_proof_requires_exact_boolean_true(live_enabled):
    error_type = TypeError if live_enabled == 1 else ValueError
    with pytest.raises(error_type, match=r"Live enabled|runtime type"):
        replace(_governance_proof(), live_enabled=live_enabled)


def test_governance_eligibility_sha_can_be_a_continuity_ancestor():
    marker = _marker()

    assert (
        dict(marker.governance_proof.provenance)["eligibility-main-sha"]
        == _ELIGIBILITY_ANCESTOR
    )
    assert marker.attempt.execution.target == _TARGET
    assert _ELIGIBILITY_ANCESTOR != _TARGET


@pytest.mark.parametrize(
    "package",
    ["@Hcoona/hcoona-release-smoke-npm", ""],
    ids=("uppercase", "empty"),
)
def test_package_control_subject_requires_normalized_nonempty_package(
    package,
):
    with pytest.raises(ValueError, match=r"normalized|nonempty"):
        _package_control_subject(package=package)


@pytest.mark.parametrize(
    "endpoints",
    [
        (),
        (_API_ENDPOINT, _API_ENDPOINT),
        tuple(reversed(_ENDPOINTS)),
    ],
    ids=("empty", "duplicate", "unsorted"),
)
def test_package_control_proof_requires_sorted_authoritative_endpoints(
    endpoints,
):
    with pytest.raises(ValueError, match=r"endpoints|authoritative"):
        replace(_package_control_proof(), endpoints=endpoints)


@pytest.mark.parametrize(
    "facts",
    [
        _PACKAGE_FACTS[:-1],
        (*_PACKAGE_FACTS, ("unexpected", ("value",))),
        (
            *_PACKAGE_FACTS[:2],
            _PACKAGE_FACTS[1],
            *_PACKAGE_FACTS[2:],
        ),
        (
            _PACKAGE_FACTS[1],
            _PACKAGE_FACTS[0],
            *_PACKAGE_FACTS[2:],
        ),
    ],
    ids=("missing", "extra", "duplicate", "unsorted"),
)
def test_package_control_proof_requires_the_exact_fact_set(facts):
    with pytest.raises(ValueError, match=r"facts|incomplete"):
        replace(_package_control_proof(), facts=facts)


@pytest.mark.parametrize(
    ("fact_name", "values"),
    [
        ("owner", ("hcoona", "octocat")),
        ("visibility", ("internal", "public")),
        ("repository-association", ()),
        ("exposed-access", ()),
    ],
)
def test_package_control_proof_enforces_fact_cardinality(
    fact_name,
    values,
):
    proof = _package_control_proof()

    with pytest.raises(ValueError, match="cardinality"):
        replace(
            proof,
            facts=_fact_values(proof, fact_name, values),
        )


@pytest.mark.parametrize(
    "values",
    [
        ("hcoona/three", "hcoona/three"),
        ("hcoona/three-mirror", "hcoona/three"),
    ],
    ids=("duplicate", "unsorted"),
)
def test_package_control_fact_values_are_canonical(values):
    proof = _package_control_proof()

    with pytest.raises(ValueError, match=r"duplicate|sorted order"):
        replace(
            proof,
            facts=_fact_values(
                proof,
                "repository-association",
                values,
            ),
        )


@pytest.mark.parametrize(
    "response_digests",
    [
        _PACKAGE_RESPONSE_DIGESTS[:1],
        (
            *_PACKAGE_RESPONSE_DIGESTS,
            ("https://uploads.github.test/package", _sha256("f")),
        ),
        tuple(reversed(_PACKAGE_RESPONSE_DIGESTS)),
    ],
    ids=("missing-endpoint", "extra-endpoint", "wrong-order"),
)
def test_package_control_responses_bind_exactly_to_authority_endpoints(
    response_digests,
):
    with pytest.raises(
        ValueError,
        match=r"endpoints mismatch|sorted order",
    ):
        replace(
            _package_control_proof(),
            response_digests=response_digests,
        )


@pytest.mark.parametrize(
    "response_digests",
    [
        (
            (_API_ENDPOINT, "6" * 64),
            _PACKAGE_RESPONSE_DIGESTS[1],
        ),
        (
            _PACKAGE_RESPONSE_DIGESTS[0],
            (_REGISTRY_ENDPOINT, _sha256("G")),
        ),
    ],
    ids=("missing-prefix", "uppercase"),
)
def test_package_control_responses_require_canonical_digests(
    response_digests,
):
    with pytest.raises(ValueError, match="prefixed lowercase SHA-256"):
        replace(
            _package_control_proof(),
            response_digests=response_digests,
        )


@pytest.mark.parametrize(
    "digest",
    ["8" * 64, 8],
    ids=("missing-prefix", "wrong-type"),
)
def test_profile_match_requires_a_canonical_profile_digest(digest):
    error_type = TypeError if type(digest) is int else ValueError
    with pytest.raises(error_type, match=r"profile|runtime type"):
        replace(
            _profile_match(),
            destination_operation_profile_digest=digest,
        )


@pytest.mark.parametrize(
    ("field_name", "replacement"),
    [("command", ()), ("configuration", ())],
)
def test_profile_match_requires_nonempty_command_and_configuration(
    field_name,
    replacement,
):
    with pytest.raises(ValueError, match="cannot be empty"):
        replace(
            _profile_match(),
            **{field_name: replacement},
        )


@pytest.mark.parametrize(
    "configuration",
    [
        (
            ("fetch-retries",),
            *_PROFILE_CONFIGURATION[1:],
        ),
        (
            _PROFILE_CONFIGURATION[0],
            _PROFILE_CONFIGURATION[0],
            *_PROFILE_CONFIGURATION[1:],
        ),
        tuple(reversed(_PROFILE_CONFIGURATION)),
    ],
    ids=("wrong-pair-size", "duplicate-key", "unsorted"),
)
def test_profile_match_configuration_uses_canonical_pairs(configuration):
    with pytest.raises(
        ValueError,
        match=r"two strings|duplicate|sorted",
    ):
        replace(_profile_match(), configuration=configuration)


@pytest.mark.parametrize(
    ("factory", "field_name"),
    [
        (_package_control_proof, "observed_at"),
        (_profile_match, "matched_at"),
        (_readback, "observed_at"),
    ],
)
def test_embedded_evidence_requires_canonical_utc_timestamp_shape(
    factory,
    field_name,
):
    with pytest.raises(ValueError, match="RFC 3339 UTC timestamp"):
        replace(
            factory(),
            **{field_name: "2026-09-05T01:22:35+00:00"},
        )


@pytest.mark.parametrize(
    ("classification", "tag_state", "expected_evidence_count"),
    [
        ("absent", "absent", 0),
        ("exact-satisfied", "present", 4),
        ("partial", "unreadable", 2),
        ("conflicting", "absent", 4),
        ("unknown", "present", 0),
        ("unprovable", "unreadable", 2),
    ],
)
def test_destination_readback_accepts_paired_classification_and_tag_states(
    classification,
    tag_state,
    expected_evidence_count,
):
    readback = _readback(
        classification=classification,
        tag_state=tag_state,
    )
    document = readback.to_document()

    evidence = (
        document["content-sha256"],
        document["content-sha512"],
        document["witness-digest"],
        document["witness-target"],
    )
    assert document["classification"] == classification
    assert sum(value is not None for value in evidence) == (
        expected_evidence_count
    )
    assert document["tag-state"] == tag_state
    assert (document["tag-version"] is not None) == (tag_state == "present")


@pytest.mark.parametrize(
    ("field_name", "value"),
    [
        ("content_sha256", _CONTENT_SHA256),
        ("content_sha512", _CONTENT_SHA512),
        ("witness_digest", _WITNESS_DIGEST),
        ("witness_target", _TARGET),
    ],
)
def test_absent_readback_rejects_every_version_evidence_slot(
    field_name,
    value,
):
    with pytest.raises(ValueError, match="cannot contain version facts"):
        replace(
            _readback(classification="absent", tag_state="absent"),
            **{field_name: value},
        )


@pytest.mark.parametrize(
    ("classification", "field_name"),
    [
        ("exact-satisfied", "content_sha256"),
        ("conflicting", "content_sha512"),
        ("exact-satisfied", "witness_digest"),
        ("conflicting", "witness_target"),
    ],
)
def test_conclusive_readback_requires_every_version_evidence_slot(
    classification,
    field_name,
):
    with pytest.raises(ValueError, match="complete version facts"):
        replace(
            _readback(classification=classification),
            **{field_name: None},
        )


@pytest.mark.parametrize(
    ("field_name", "replacement", "message"),
    [
        ("content_sha256", "9" * 64, "SHA-256"),
        ("content_sha512", _sha512("A"), "SHA-512"),
        ("witness_target", "A" * 40, "40 lowercase"),
    ],
)
def test_destination_readback_rejects_malformed_version_evidence(
    field_name,
    replacement,
    message,
):
    with pytest.raises(ValueError, match=message):
        replace(
            _readback(),
            **{field_name: replacement},
        )


@pytest.mark.parametrize(
    ("tag_state", "tag_version"),
    [
        ("present", None),
        ("absent", _VERSION),
        ("unreadable", _VERSION),
    ],
)
def test_destination_readback_enforces_tag_version_binding(
    tag_state,
    tag_version,
):
    with pytest.raises(ValueError, match=r"tag|Tag"):
        replace(
            _readback(),
            tag_state=tag_state,
            tag_version=tag_version,
        )


@pytest.mark.parametrize(
    ("field_name", "replacement"),
    [
        ("classification", "satisfied"),
        ("tag_state", "missing"),
    ],
)
def test_destination_readback_rejects_open_classifications(
    field_name,
    replacement,
):
    with pytest.raises(ValueError, match="invalid closed value"):
        replace(
            _readback(),
            **{field_name: replacement},
        )


def test_destination_readback_requires_normalized_package():
    with pytest.raises(ValueError, match="package is not normalized"):
        replace(
            _readback(),
            package="@Hcoona/hcoona-release-smoke-npm",
        )


@pytest.mark.parametrize(
    "response_digests",
    [
        (),
        (
            ("exact-version", _VERSION_RESPONSE_DIGEST),
            ("exact-version", _TAG_RESPONSE_DIGEST),
        ),
        (
            ("exact-version", _VERSION_RESPONSE_DIGEST),
            ("target-tag", "d" * 64),
        ),
    ],
    ids=("empty", "duplicate-key", "malformed-digest"),
)
def test_destination_readback_requires_bound_canonical_responses(
    response_digests,
):
    with pytest.raises(
        ValueError,
        match=r"response|duplicate|SHA-256",
    ):
        replace(
            _readback(),
            response_digests=response_digests,
        )


@pytest.mark.parametrize(
    "entries",
    [
        tuple(f"diagnostic-{index:02d}" for index in range(16)),
        ("é" * 1024,),
        (
            "a" * 2048,
            "b" * 2048,
            "c" * 2048,
            "d" * 2048,
        ),
    ],
    ids=("count-limit", "utf8-entry-limit", "total-limit"),
)
def test_publication_diagnostics_accepts_exact_utf8_boundaries(entries):
    diagnostics = PublicationDiagnostics(entries=entries, truncated=True)
    document = diagnostics.to_document()

    assert document["entries"] == list(entries)
    assert document["truncated"] is True
    assert len(document["entries"]) <= _DIAGNOSTIC_COUNT_LIMIT
    assert (
        max(len(entry.encode()) for entry in entries)
        <= _DIAGNOSTIC_ENTRY_BYTE_LIMIT
    )
    assert (
        sum(len(entry.encode()) for entry in entries)
        <= _DIAGNOSTIC_TOTAL_BYTE_LIMIT
    )


@pytest.mark.parametrize(
    ("entries", "message"),
    [
        (
            tuple(f"diagnostic-{index:02d}" for index in range(17)),
            "too many entries",
        ),
        (("é" * 1024 + "a",), "entry exceeds"),
        (
            (
                "a" * 2048,
                "b" * 2048,
                "c" * 2048,
                "d" * 2048,
                "e",
            ),
            "total byte limit",
        ),
    ],
    ids=("count", "utf8-entry-bytes", "total-bytes"),
)
def test_publication_diagnostics_rejects_first_byte_over_each_limit(
    entries,
    message,
):
    with pytest.raises(ValueError, match=message):
        PublicationDiagnostics(entries=entries, truncated=False)


@pytest.mark.parametrize(
    ("entries", "truncated"),
    [
        (("",), False),
        ((101,), False),
        (("diagnostic",), 0),
    ],
    ids=("empty-entry", "non-string-entry", "non-boolean-truncated"),
)
def test_publication_diagnostics_rejects_invalid_entry_or_flag_type(
    entries,
    truncated,
):
    error_type = ValueError if entries == ("",) else TypeError
    with pytest.raises(error_type, match=r"nonempty|runtime type"):
        PublicationDiagnostics(entries=entries, truncated=truncated)


def test_mutation_marker_accepts_a_coherently_rebound_current_attempt():
    attempt = _attempt(
        target=_ALTERNATE_TARGET,
        workflow_run_id=_ALTERNATE_WORKFLOW_RUN_ID,
    )

    marker = _marker(attempt=attempt)
    document = marker.to_document()

    assert document["attempt"]["execution"]["target"] == _ALTERNATE_TARGET
    assert document["control"] == (f"workflow-delivery-v3:{_ALTERNATE_TARGET}")
    assert document["workflow-run-id"] == _ALTERNATE_WORKFLOW_RUN_ID


@pytest.mark.parametrize(
    ("field_name", "replacement", "message"),
    [
        ("producer", "publish-github-package", "producer is not exact"),
        ("control", "workflow-delivery-v3:" + ("8" * 40), "target binding"),
        ("workflow_run_id", 202, "Attempt binding mismatch"),
    ],
)
def test_mutation_marker_binds_producer_control_and_current_run(
    field_name,
    replacement,
    message,
):
    with pytest.raises(ValueError, match=message):
        replace(
            _marker(),
            **{field_name: replacement},
        )


@pytest.mark.parametrize(
    ("field_name", "replacement"),
    [
        ("attempt", object()),
        (
            "publication_authorization_reference",
            _derived_artifact_reference(),
        ),
        ("package_control_proof", object()),
    ],
)
def test_mutation_marker_requires_exact_authority_value_types(
    field_name,
    replacement,
):
    with pytest.raises(TypeError, match="wrong runtime type"):
        replace(
            _marker(),
            **{field_name: replacement},
        )


@pytest.mark.parametrize(
    ("command", "mutation"),
    [
        ("not-initiated", "not-mutated"),
        ("definitive-success", "possibly-mutated"),
        ("definitive-success", "mutated"),
        ("definitive-non-success", "not-mutated"),
        ("definitive-non-success", "possibly-mutated"),
        ("definitive-non-success", "mutated"),
        ("ambiguous", "possibly-mutated"),
        ("ambiguous", "mutated"),
    ],
)
def test_failed_publication_result_accepts_conservative_state_matrix(
    command,
    mutation,
):
    result = _failed_result(command, mutation)
    document = result.to_document()

    assert document["result"] == "failed"
    assert (
        document["command-classification"],
        document["mutation-classification"],
    ) == (command, mutation)
    assert document["post-action-readback"] is None
    assert (document["response-identity"] is None) == (
        command == "not-initiated"
    )


@pytest.mark.parametrize(
    ("command", "mutation", "message"),
    [
        (
            "not-initiated",
            "possibly-mutated",
            "direct no-mutation evidence",
        ),
        ("not-initiated", "mutated", "direct no-mutation evidence"),
        (
            "definitive-success",
            "not-mutated",
            "Definitive success",
        ),
        ("ambiguous", "not-mutated", "possibly mutated"),
    ],
)
def test_failed_publication_result_rejects_nonconservative_state_matrix(
    command,
    mutation,
    message,
):
    with pytest.raises(ValueError, match=message):
        replace(
            _publication_result(),
            command_classification=command,
            post_action_readback=None,
            result="failed",
            mutation_classification=mutation,
            response_identity=None,
        )


@pytest.mark.parametrize(
    "readback",
    [
        None,
        _readback(classification="absent", tag_state="absent"),
        _readback(classification="conflicting", tag_state="unreadable"),
        _readback(classification="unknown", tag_state="present"),
    ],
    ids=("none", "absent", "conflicting", "unknown"),
)
def test_failed_publication_result_retains_representative_readback_states(
    readback,
):
    result = _failed_result(
        "definitive-non-success",
        "possibly-mutated",
        readback=readback,
    )
    document = result.to_document()

    assert document["result"] == "failed"
    assert (document["post-action-readback"] is None) == (readback is None)
    if readback is not None:
        assert document["post-action-readback"]["classification"] == (
            readback.classification
        )


@pytest.mark.parametrize(
    "changes",
    [
        {"command_classification": "not-initiated"},
        {"mutation_classification": "possibly-mutated"},
        {"post_action_readback": None},
        {
            "post_action_readback": _readback(
                classification="partial",
                tag_state="unreadable",
            ),
        },
    ],
    ids=("wrong-command", "wrong-mutation", "missing-readback", "nonexact"),
)
def test_published_result_requires_success_mutation_and_exact_readback(
    changes,
):
    with pytest.raises(ValueError, match="Published Result requires"):
        replace(_publication_result(), **changes)


def test_published_result_rejects_readback_for_a_different_target():
    mismatched = replace(
        _readback(),
        witness_target=_ALTERNATE_TARGET,
    )

    with pytest.raises(ValueError, match="exact target readback"):
        replace(
            _publication_result(),
            post_action_readback=mismatched,
        )


@pytest.mark.parametrize(
    ("changes", "message"),
    [
        (
            {
                "command_classification": "not-initiated",
                "post_action_readback": None,
                "result": "failed",
                "mutation_classification": "not-mutated",
                "response_identity": _PUBLICATION_RESPONSE_DIGEST,
            },
            "direct no-mutation evidence",
        ),
        (
            {"response_identity": "e" * 64},
            "prefixed lowercase SHA-256",
        ),
    ],
    ids=("not-initiated-with-response", "malformed-response"),
)
def test_publication_result_constrains_response_identity(changes, message):
    with pytest.raises(ValueError, match=message):
        replace(_publication_result(), **changes)


@pytest.mark.parametrize(
    ("field_name", "replacement"),
    [
        ("command_classification", "successful"),
        ("result", "success"),
        ("mutation_classification", "mutation-confirmed"),
    ],
)
def test_publication_result_rejects_open_state_values(
    field_name,
    replacement,
):
    with pytest.raises(ValueError, match="invalid closed value"):
        replace(
            _publication_result(),
            **{field_name: replacement},
        )


@pytest.mark.parametrize(
    ("field_name", "replacement", "message"),
    [
        ("producer", "publish-npm", "producer is not exact"),
        ("control", "workflow-delivery-v3:" + ("8" * 40), "target binding"),
        ("workflow_run_id", 202, "Attempt binding mismatch"),
    ],
)
def test_publication_result_binds_producer_control_and_current_run(
    field_name,
    replacement,
    message,
):
    with pytest.raises(ValueError, match=message):
        replace(
            _publication_result(),
            **{field_name: replacement},
        )


@pytest.mark.parametrize(
    ("field_name", "replacement"),
    [
        ("mutation_marker_reference", _derived_artifact_reference()),
        ("post_action_readback", object()),
    ],
)
def test_publication_result_requires_exact_nested_value_types(
    field_name,
    replacement,
):
    with pytest.raises(TypeError, match="wrong runtime type"):
        replace(
            _publication_result(),
            **{field_name: replacement},
        )


def test_published_result_accepts_a_coherently_rebound_current_attempt():
    attempt = _attempt(
        target=_ALTERNATE_TARGET,
        workflow_run_id=_ALTERNATE_WORKFLOW_RUN_ID,
    )

    result = _publication_result(attempt=attempt)
    document = result.to_document()

    assert document["post-action-readback"]["witness-target"] == (
        _ALTERNATE_TARGET
    )
    assert document["control"] == (f"workflow-delivery-v3:{_ALTERNATE_TARGET}")
    assert document["workflow-run-id"] == _ALTERNATE_WORKFLOW_RUN_ID


@pytest.mark.parametrize(
    "classification",
    ["absent", "partial", "conflicting"],
)
def test_finalization_proof_requires_exact_satisfied_classification(
    classification,
):
    readback = _readback(
        classification=classification,
        tag_state="absent",
    )

    with pytest.raises(ValueError, match="requires exact readback"):
        replace(
            _finalization_proof(),
            exact_version_readback=readback,
        )


def test_finalization_proof_accepts_observations_at_proof_time():
    proof = replace(
        _finalization_proof(),
        governance_proof=replace(
            _governance_proof(),
            observed_at=_PROVED_AT,
        ),
        package_control_proof=replace(
            _package_control_proof(),
            observed_at=_PROVED_AT,
        ),
        exact_version_readback=replace(
            _readback(),
            observed_at=_PROVED_AT,
        ),
    )
    document = proof.to_document()

    assert document["proved-at"] == _PROVED_AT
    assert {
        document["governance-proof"]["observed-at"],
        document["package-control-proof"]["observed-at"],
        document["exact-version-readback"]["observed-at"],
    } == {_PROVED_AT}


@pytest.mark.parametrize(
    "field_name",
    [
        "governance_proof",
        "package_control_proof",
        "exact_version_readback",
    ],
)
def test_finalization_proof_rejects_each_later_observation_position(
    field_name,
):
    proof = _finalization_proof()
    later = replace(
        getattr(proof, field_name),
        observed_at="2026-09-05T01:22:37.500001Z",
    )

    with pytest.raises(ValueError, match="cannot precede fresh"):
        replace(
            proof,
            **{field_name: later},
        )


def test_finalization_proof_preserves_fractional_timing_boundaries():
    observed_at = "2026-09-05T01:22:37.499999Z"
    proved_at = "2026-09-05T01:22:37.500000Z"
    expires_at = "2026-09-05T01:22:37.500001Z"

    proof = replace(
        _finalization_proof(),
        governance_proof=replace(
            _governance_proof(),
            observed_at=observed_at,
            expires_at=expires_at,
        ),
        package_control_proof=replace(
            _package_control_proof(),
            observed_at=observed_at,
        ),
        exact_version_readback=replace(
            _readback(),
            observed_at=observed_at,
        ),
        proved_at=proved_at,
    )
    document = proof.to_document()

    assert document["governance-proof"]["observed-at"] == observed_at
    assert document["proved-at"] == proved_at
    assert document["governance-proof"]["expires-at"] == expires_at


@pytest.mark.parametrize(
    "expires_at",
    [
        _PROVED_AT,
        "2026-09-05T01:22:37.499999Z",
    ],
    ids=("equal", "before"),
)
def test_finalization_proof_must_precede_governance_expiry(expires_at):
    governance = replace(
        _governance_proof(),
        expires_at=expires_at,
    )

    with pytest.raises(ValueError, match="precede Governance expiry"):
        replace(
            _finalization_proof(),
            governance_proof=governance,
        )


def test_finalization_proof_rejects_readback_for_a_different_target():
    readback = replace(
        _readback(),
        witness_target=_ALTERNATE_TARGET,
    )

    with pytest.raises(ValueError, match="readback target mismatch"):
        replace(
            _finalization_proof(),
            exact_version_readback=readback,
        )


def test_finalization_proof_rejects_package_control_subject_mismatch():
    other_package = "@hcoona/other-release-smoke-npm"
    readback = replace(_readback(), package=other_package)

    with pytest.raises(ValueError, match="package subject mismatch"):
        replace(
            _finalization_proof(),
            exact_version_readback=readback,
        )


@pytest.mark.parametrize(
    ("field_name", "replacement", "message"),
    [
        ("producer", "finalize", "producer is not exact"),
        ("control", "workflow-delivery-v3:" + ("8" * 40), "target binding"),
        ("workflow_run_id", 202, "Attempt binding mismatch"),
    ],
)
def test_finalization_proof_binds_producer_control_and_current_run(
    field_name,
    replacement,
    message,
):
    with pytest.raises(ValueError, match=message):
        replace(
            _finalization_proof(),
            **{field_name: replacement},
        )


@pytest.mark.parametrize(
    ("field_name", "replacement"),
    [
        ("publication_snapshot_reference", _derived_artifact_reference()),
        ("exact_version_readback", object()),
    ],
)
def test_finalization_proof_requires_exact_nested_value_types(
    field_name,
    replacement,
):
    with pytest.raises(TypeError, match="wrong runtime type"):
        replace(
            _finalization_proof(),
            **{field_name: replacement},
        )


@pytest.mark.parametrize(
    ("proved_at", "error_type"),
    [
        ("2026-09-05 01:22:37.500Z", ValueError),
        (101, TypeError),
    ],
)
def test_finalization_proof_requires_canonical_proved_at(
    proved_at,
    error_type,
):
    with pytest.raises(error_type, match=r"RFC 3339|runtime type"):
        replace(_finalization_proof(), proved_at=proved_at)


def test_finalization_proof_accepts_a_coherently_rebound_current_attempt():
    attempt = _attempt(
        target=_ALTERNATE_TARGET,
        workflow_run_id=_ALTERNATE_WORKFLOW_RUN_ID,
    )

    proof = _finalization_proof(attempt=attempt)
    document = proof.to_document()

    assert document["exact-version-readback"]["witness-target"] == (
        _ALTERNATE_TARGET
    )
    assert document["control"] == (f"workflow-delivery-v3:{_ALTERNATE_TARGET}")
    assert document["workflow-run-id"] == _ALTERNATE_WORKFLOW_RUN_ID


@pytest.mark.parametrize("kind", _PREDECESSOR_KINDS)
def test_direct_predecessor_accepts_every_closed_kind(kind):
    document = replace(_predecessor(), kind=kind).to_document()

    assert document["kind"] == kind
    assert tuple(document["reference"]) == _REFERENCE_FIELDS
    assert document["reference"]["payload-digest"] == _sha256("f")


@pytest.mark.parametrize(
    ("field_name", "replacement", "error_type", "message"),
    [
        ("kind", "publication-results", ValueError, "invalid closed value"),
        (
            "reference",
            _derived_artifact_reference(),
            TypeError,
            "wrong runtime type",
        ),
    ],
)
def test_direct_predecessor_rejects_open_kind_or_reference_subclass(
    field_name,
    replacement,
    error_type,
    message,
):
    with pytest.raises(error_type, match=message):
        replace(
            _predecessor(),
            **{field_name: replacement},
        )


_TRANSPORT_RECORDS = (
    (
        "mutation-marker",
        _marker,
        MutationMayHaveStartedMarker,
    ),
    (
        "publication-result",
        _publication_result,
        PublicationResult,
    ),
    (
        "exact-finalization-proof",
        _finalization_proof,
        ExactSatisfiedFinalizationProof,
    ),
)


def _null_publication_result():
    return _failed_result("not-initiated", "not-mutated")


@pytest.mark.parametrize(
    ("factory", "record_type"),
    [
        pytest.param(factory, record_type, id=record_id)
        for record_id, factory, record_type in _TRANSPORT_RECORDS
    ],
)
def test_release_transport_round_trips_each_top_level_record(
    factory,
    record_type,
):
    record = factory()
    document = deepcopy(record.to_document())

    parsed = release_record_from_document(
        document,
        expected_type=record_type,
    )

    assert type(parsed) is record_type
    assert parsed == record
    assert parsed.to_document() == document


@pytest.mark.parametrize(
    ("factory", "record_type"),
    [
        pytest.param(factory, record_type, id=record_id)
        for record_id, factory, record_type in _TRANSPORT_RECORDS
    ],
)
def test_top_level_records_admit_under_current_live_bindings(
    factory,
    record_type,
):
    record = factory()

    admitted = admit_release_record(
        canonicalize(record.to_document()),
        expected_type=record_type,
        expected_digest=release_record_digest(record),
        expected_bindings=ReleaseAdmissionBindings(
            purpose="live-release",
            workflow_run_id=record.workflow_run_id,
            run_attempt=None,
            target=record.attempt.execution.target,
            producer=record.producer,
        ),
    )

    assert admitted == record


def test_publication_result_transport_round_trips_required_null_fields():
    document = _null_publication_result().to_document()

    parsed = release_record_from_document(
        deepcopy(document),
        expected_type=PublicationResult,
    )

    assert parsed.post_action_readback is None
    assert parsed.response_identity is None
    assert parsed.to_document()["post-action-readback"] is None
    assert parsed.to_document()["response-identity"] is None


@pytest.mark.parametrize(
    ("factory", "record_type", "field_name"),
    [
        (
            _marker,
            MutationMayHaveStartedMarker,
            "publication-authorization-reference",
        ),
        (
            _null_publication_result,
            PublicationResult,
            "post-action-readback",
        ),
        (
            _null_publication_result,
            PublicationResult,
            "response-identity",
        ),
        (
            _finalization_proof,
            ExactSatisfiedFinalizationProof,
            "exact-version-readback",
        ),
    ],
)
def test_release_transport_requires_fields_even_when_values_may_be_null(
    factory,
    record_type,
    field_name,
):
    document = factory().to_document()
    document.pop(field_name)

    with pytest.raises(
        ValueError, match=f"missing required field: {field_name}"
    ):
        release_record_from_document(
            document,
            expected_type=record_type,
        )


@pytest.mark.parametrize(
    ("factory", "record_type"),
    [
        pytest.param(factory, record_type, id=record_id)
        for record_id, factory, record_type in _TRANSPORT_RECORDS
    ],
)
def test_release_transport_rejects_unknown_top_level_fields(
    factory,
    record_type,
):
    document = factory().to_document()
    document["unexpected"] = "open-schema"

    with pytest.raises(ValueError, match="unknown field: unexpected"):
        release_record_from_document(
            document,
            expected_type=record_type,
        )


@pytest.mark.parametrize(
    ("factory", "record_type"),
    [
        pytest.param(factory, record_type, id=record_id)
        for record_id, factory, record_type in _TRANSPORT_RECORDS
    ],
)
def test_release_transport_rejects_wrong_top_level_schemas(
    factory,
    record_type,
):
    document = factory().to_document()
    document["schema"] += "-near-miss"

    with pytest.raises(ValueError, match="wrong schema"):
        release_record_from_document(
            document,
            expected_type=record_type,
        )


@pytest.mark.parametrize("missing_field", _REFERENCE_FIELDS)
def test_release_transport_requires_every_artifact_lineage_slot(
    missing_field,
):
    document = _marker().to_document()
    reference = document["publication-authorization-reference"]
    reference.pop(missing_field)

    with pytest.raises(
        ValueError,
        match=f"artifact reference missing required field: {missing_field}",
    ):
        release_record_from_document(
            document,
            expected_type=MutationMayHaveStartedMarker,
        )


def test_release_transport_rejects_unknown_artifact_lineage_slot():
    document = _publication_result().to_document()
    document["mutation-marker-reference"]["schema"] = (
        "workflow-delivery/v3/not-an-artifact-reference-schema"
    )

    with pytest.raises(ValueError, match="artifact reference unknown field"):
        release_record_from_document(
            document,
            expected_type=PublicationResult,
        )


@pytest.mark.parametrize(
    ("factory", "record_type", "path", "change", "field_name", "message"),
    [
        (
            _marker,
            MutationMayHaveStartedMarker,
            ("governance-proof",),
            "unknown",
            "schema",
            "Governance proof unknown field: schema",
        ),
        (
            _publication_result,
            PublicationResult,
            ("diagnostics",),
            "missing",
            "truncated",
            "publication diagnostics missing required field: truncated",
        ),
        (
            _finalization_proof,
            ExactSatisfiedFinalizationProof,
            ("package-control-proof", "subject"),
            "unknown",
            "owner",
            "Package-Control subject unknown field: owner",
        ),
        (
            _finalization_proof,
            ExactSatisfiedFinalizationProof,
            ("exact-version-readback",),
            "missing",
            "response-digests",
            "destination readback missing required field: response-digests",
        ),
    ],
)
def test_release_transport_rejects_representative_nested_schema_openings(  # noqa: PLR0913, PLR0917
    factory,
    record_type,
    path,
    change,
    field_name,
    message,
):
    document = factory().to_document()
    nested = _nested_member(document, path)
    if change == "missing":
        nested.pop(field_name)
    else:
        nested[field_name] = "open-schema"

    with pytest.raises(ValueError, match=message):
        release_record_from_document(
            document,
            expected_type=record_type,
        )


@pytest.mark.parametrize(
    ("factory", "record_type", "path", "replacement", "message"),
    [
        (
            _marker,
            MutationMayHaveStartedMarker,
            ("attempt",),
            [],
            "ReleaseAttemptIdentity must be an object",
        ),
        (
            _marker,
            MutationMayHaveStartedMarker,
            ("package-control-proof", "endpoints"),
            _ENDPOINTS,
            "endpoints must be an array",
        ),
        (
            _publication_result,
            PublicationResult,
            ("post-action-readback",),
            [],
            "destination readback must be an object",
        ),
        (
            _publication_result,
            PublicationResult,
            ("diagnostics", "entries"),
            _diagnostics().entries,
            "entries must be an array",
        ),
        (
            _finalization_proof,
            ExactSatisfiedFinalizationProof,
            ("exact-version-readback", "response-digests"),
            _readback().response_digests,
            "response-digests must be an array",
        ),
    ],
)
def test_release_transport_rejects_representative_nested_json_types(
    factory,
    record_type,
    path,
    replacement,
    message,
):
    document = factory().to_document()
    _set_nested_member(document, path, replacement)

    with pytest.raises(TypeError, match=message):
        release_record_from_document(
            document,
            expected_type=record_type,
        )


@pytest.mark.parametrize(
    ("factory", "record_type", "field_name", "replacement", "message"),
    [
        (
            _marker,
            MutationMayHaveStartedMarker,
            "producer",
            101,
            "producer must be a string",
        ),
        (
            _publication_result,
            PublicationResult,
            "workflow-run-id",
            True,
            "workflow-run-id must be an integer",
        ),
        (
            _finalization_proof,
            ExactSatisfiedFinalizationProof,
            "proved-at",
            None,
            "proved-at must be a string",
        ),
    ],
)
def test_release_transport_rejects_wrong_top_level_primitive_types(
    factory,
    record_type,
    field_name,
    replacement,
    message,
):
    document = factory().to_document()
    document[field_name] = replacement

    with pytest.raises(TypeError, match=message):
        release_record_from_document(
            document,
            expected_type=record_type,
        )
