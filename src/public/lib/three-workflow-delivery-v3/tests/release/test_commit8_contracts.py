"""Focused strict contract tests for Workflow Delivery v3 commit 8."""

from __future__ import annotations

# ruff: noqa: D103, ISC004
from copy import deepcopy
from dataclasses import FrozenInstanceError, fields, replace

import pytest
from three_workflow_delivery_v3 import platform as platform_api
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
)
from three_workflow_delivery_v3.platform import github as github_platform
from three_workflow_delivery_v3.records import release as release_records
from three_workflow_delivery_v3.records.artifacts import (
    ArtifactReference,
    ArtifactTransportIdentity,
)
from three_workflow_delivery_v3.records.bindings import (
    CurrentAuthorityContext,
    admit,
)
from three_workflow_delivery_v3.records.release import (
    CONDITIONAL_NPM_VERSION_AND_TAG_OPERATION,
    ActionResult,
    ApprovalBoundary,
    ApprovalBundle,
    AttemptOutcome,
    BuddyExecutionIdentity,
    ExactSatisfiedGovernanceProof,
    ExternalPackageCoordinate,
    GovernanceProof,
    PublicationAction,
    PublicationAuthorization,
    PublicationObservationReference,
    PublicationSnapshot,
    Receipt,
    ReleaseAttemptBinding,
    ReleaseAttemptIdentity,
    ReleaseRecord,
    SimulationBinding,
    publication_action_inputs,
    publication_capability_requirements,
    publication_expected_result,
    publication_lock_group,
    publication_lock_projection,
    publication_mutable_resource_key_basis,
    publication_mutable_resource_keys,
    publication_receipt_contract,
    release_artifact_transport_name,
)
from three_workflow_delivery_v3.records.release_transport import (
    release_record_from_document,
)
from three_workflow_delivery_v3.release.eligibility import (
    LiveEligibilityAdmissionMode,
)
from three_workflow_delivery_v3.release.identity import (
    derive_buddy_execution_identity,
    normalize_buddy_live_intent,
)
from three_workflow_delivery_v3.release.live import (
    finalize_attempt_outcome,
    form_approval_bundle,
    validate_approval_bundle_closure,
)

TARGET = "a" * 40
ARTIFACT_DIGEST = "sha256:" + ("b" * 64)
CONTROL = f"workflow-delivery-v3:{TARGET}"
COMPLETE_RESOURCE_KEY_COUNT = 2
EXECUTION = BuddyExecutionIdentity(
    channel="buddy",
    release_unit="hcoona-release-smoke-npm",
    target=TARGET,
)
ATTEMPT = ReleaseAttemptIdentity(
    execution=EXECUTION,
    workflow_run_id=101,
)

COMMIT8_RECORD_TYPES = (
    "ReleaseAttemptBinding",
    "ApprovalBundle",
    "PublicationAuthorization",
    "ExactSatisfiedGovernanceProof",
    "ActionResult",
    "Receipt",
    "AttemptOutcome",
)

RETIRED_RECORD_TYPES = (
    "HistoricalExecutionRecord",
    "ExecutionHistoryAdmissionSnapshot",
    "ReceiptTransportReference",
)

RETIRED_PLATFORM_HISTORY_TYPES = (
    "GitHubActionsHistoryClient",
    "GitHubArtifact",
    "GitHubArtifactArchiveShapeError",
    "GitHubArtifactDownload",
    "GitHubJob",
    "GitHubJobStep",
    "GitHubPage",
    "GitHubRun",
    "GitHubRunAttemptFact",
    "admit_artifact_download",
    "iter_all_artifacts",
    "iter_all_attempt_jobs",
    "iter_all_jobs",
    "iter_all_runs",
)


def _current_payload() -> dict[str, JsonValue]:
    return {
        "release_execution": canonical_sha256(EXECUTION.to_document()),
        "purpose": "live-release",
        "request": "release-request:" + ("d" * 64),
        "workflow_run_id": ATTEMPT.workflow_run_id,
        "attempt": canonical_sha256(ATTEMPT.to_document()),
        "target": TARGET,
        "producer": "commit8-contract-producer",
        "control": CONTROL,
    }


def _current_context(
    payload: dict[str, JsonValue],
) -> CurrentAuthorityContext:
    return CurrentAuthorityContext(
        release_execution=str(payload["release_execution"]),
        purpose=str(payload["purpose"]),
        request=str(payload["request"]),
        workflow_run_id=ATTEMPT.workflow_run_id,
        run_attempt=None,
        attempt=str(payload["attempt"]),
        target=str(payload["target"]),
        producer=str(payload["producer"]),
        control=str(payload["control"]),
        artifact_id=701,
        artifact_digest=ARTIFACT_DIGEST,
        payload_digest=canonical_sha256(payload),
    )


def test_commit8_record_contract_api_is_available() -> None:
    missing = tuple(
        name
        for name in COMMIT8_RECORD_TYPES
        if not hasattr(release_records, name)
    )

    assert missing == (), f"missing commit-8 record contracts: {missing}"


def test_live_eligibility_freshness_modes_are_closed() -> None:
    assert tuple(mode.value for mode in LiveEligibilityAdmissionMode) == (
        "current-freshness",
        "authorization-replay",
    )
    assert (
        LiveEligibilityAdmissionMode("authorization-replay")
        is LiveEligibilityAdmissionMode.AUTHORIZATION_REPLAY
    )


def test_execution_history_platform_api_is_retired() -> None:
    assert platform_api.__all__ == []
    assert all(
        not hasattr(platform_api, name) and not hasattr(github_platform, name)
        for name in RETIRED_PLATFORM_HISTORY_TYPES
    )


def test_retired_record_contracts_are_not_available() -> None:
    present = tuple(
        name for name in RETIRED_RECORD_TYPES if hasattr(release_records, name)
    )

    assert present == (), (
        f"retired record contracts remain available: {present}"
    )


def test_attempt_identity_is_exact_frozen_and_workflow_run_bound() -> None:
    assert tuple(field.name for field in fields(ATTEMPT)) == (
        "execution",
        "workflow_run_id",
    )
    assert ATTEMPT.to_document() == {
        "schema": "workflow-delivery/v3/release-attempt-identity",
        "execution": EXECUTION.to_document(),
        "workflow-run-id": 101,
    }
    assert hasattr(ReleaseAttemptIdentity, "__slots__")
    with pytest.raises(FrozenInstanceError):
        ATTEMPT.workflow_run_id = 102  # type: ignore[misc]


def test_exact_current_attempt_authority_preserves_every_trusted_binding() -> (
    None
):
    payload = _current_payload()
    context = _current_context(payload)

    admitted = admit(
        payload=payload,
        artifact_id=context.artifact_id,
        artifact_digest=context.artifact_digest,
        current=context,
    )

    assert admitted.release_execution == context.release_execution
    assert admitted.purpose == "live-release"
    assert admitted.target == TARGET
    assert admitted.control_identity == CONTROL
    assert admitted.artifact_digest == ARTIFACT_DIGEST
    assert admitted.payload_digest == canonical_sha256(payload)


@pytest.mark.parametrize(
    ("field", "replacement"),
    [
        ("release_execution", "sha256:" + ("e" * 64)),
        ("request", "release-request:" + ("e" * 64)),
        ("workflow_run_id", 102),
        ("attempt", "sha256:" + ("e" * 64)),
        ("target", "e" * 40),
        ("producer", "substituted-producer"),
        ("control", "control:" + ("e" * 64)),
    ],
)
def test_current_attempt_authority_rejects_every_binding_substitution(
    field: str,
    replacement: str | int,
) -> None:
    payload = _current_payload()
    context = _current_context(payload)
    payload[field] = replacement

    with pytest.raises(ValueError, match=rf"binding mismatch: {field}"):
        admit(
            payload=payload,
            artifact_id=context.artifact_id,
            artifact_digest=context.artifact_digest,
            current=context,
        )


def test_live_current_authority_rejects_run_attempt_field() -> None:
    payload = _current_payload()
    context = _current_context(payload)
    payload["run_attempt"] = 2

    with pytest.raises(ValueError, match="unknown field: run_attempt"):
        admit(
            payload=payload,
            artifact_id=context.artifact_id,
            artifact_digest=context.artifact_digest,
            current=context,
        )


def test_current_attempt_authority_rejects_transport_substitution() -> None:
    payload = _current_payload()
    context = _current_context(payload)

    with pytest.raises(ValueError, match="artifact_id"):
        admit(
            payload=payload,
            artifact_id=context.artifact_id + 1,
            artifact_digest=context.artifact_digest,
            current=context,
        )
    with pytest.raises(ValueError, match="artifact_digest"):
        admit(
            payload=payload,
            artifact_id=context.artifact_id,
            artifact_digest="sha256:" + ("f" * 64),
            current=context,
        )


def test_current_attempt_authority_rejects_payload_digest_substitution() -> (
    None
):
    payload = _current_payload()
    context = replace(
        _current_context(payload),
        payload_digest="sha256:" + ("f" * 64),
    )

    with pytest.raises(ValueError, match="payload integrity mismatch"):
        admit(
            payload=payload,
            artifact_id=context.artifact_id,
            artifact_digest=context.artifact_digest,
            current=context,
        )


def test_payload_cannot_select_or_weaken_current_authority() -> None:
    payload = _current_payload()
    context = _current_context(payload)
    payload["admission_mode"] = "execution-history"

    with pytest.raises(ValueError, match="unknown field: admission_mode"):
        admit(
            payload=payload,
            artifact_id=context.artifact_id,
            artifact_digest=context.artifact_digest,
            current=context,
        )


def _governance_provenance() -> tuple[tuple[str, str], ...]:
    return (
        ("blob-oid", "b" * 40),
        ("canonical-content-digest", "sha256:" + ("4" * 64)),
        ("eligibility-main-sha", TARGET),
        ("git-object-format", "sha1"),
        (
            "path",
            ".github/workflow-delivery/governance/"
            "hcoona-release-smoke-npm.json",
        ),
        ("ref", "refs/heads/main"),
        ("repository", "hcoona/three"),
    )


def _attempt_binding() -> ReleaseAttemptBinding:
    return ReleaseAttemptBinding(
        intent_digest="sha256:" + ("0" * 64),
        request_id="release-request:" + ("f" * 64),
        execution=EXECUTION,
        attempt=ATTEMPT,
        repository_model_digest="sha256:" + ("1" * 64),
        live_eligibility_artifact_id=709,
        live_eligibility_artifact_digest="sha256:" + ("2" * 64),
        live_eligibility_payload_digest="sha256:" + ("3" * 64),
        attestation_provenance=_governance_provenance(),
    )


def _receipt() -> Receipt:
    coordinate = ExternalPackageCoordinate(
        channel="buddy",
        destination_id="npm/github-packages-hcoona-three-v1",
        package_name="@hcoona/hcoona-release-smoke-npm",
        native_version="1.2.3-gabc123",
    )
    transport = ArtifactTransportIdentity(
        artifact_id=720,
        artifact_name="release.tgz",
        artifact_url="https://example.test/artifacts/720",
        transport_digest="sha256:" + ("8" * 64),
        producer="build",
        workflow_run_id=ATTEMPT.workflow_run_id,
        run_attempt=None,
    )
    return Receipt(
        attempt=ATTEMPT,
        publication_snapshot_digest="sha256:" + ("1" * 64),
        action_id="action:publish",
        action_digest="sha256:" + ("4" * 64),
        coordinate=coordinate,
        mutable_resource_keys=(
            "external-package-coordinate:key",
            "npm-dist-tag:key",
        ),
        lock_group="destination-package:lock",
        artifact_transport=transport,
        artifact_content_sha256="sha256:" + ("9" * 64),
        artifact_content_sha512="sha512:" + ("a" * 128),
        witness_digest="sha256:" + ("b" * 64),
        creation_result="created",
        tag_mapping=(("buddy-sha-" + TARGET, coordinate.native_version),),
        response_identity_digest="sha256:" + ("c" * 64),
        producer="publish-github-packages",
        control=CONTROL,
        workflow_run_id=ATTEMPT.workflow_run_id,
    )


def _action_result(
    *,
    outcome: str = "success",
    mutation_disposition: str = "created",
    with_receipt: bool = True,
) -> ActionResult:
    receipt = _receipt()
    return ActionResult(
        attempt=ATTEMPT,
        publication_snapshot_digest=receipt.publication_snapshot_digest,
        action_id=receipt.action_id,
        action_digest=receipt.action_digest,
        lock_group=receipt.lock_group,
        outcome=outcome,
        mutation_disposition=mutation_disposition,
        response_identity_digest=(
            receipt.response_identity_digest if with_receipt else None
        ),
        receipt=receipt if with_receipt else None,
        diagnostic_reference=None if with_receipt else "lost-receipt",
        producer=receipt.producer,
        control=CONTROL,
        workflow_run_id=ATTEMPT.workflow_run_id,
    )


def _attempt_outcome() -> AttemptOutcome:
    return AttemptOutcome(
        attempt=ATTEMPT,
        qualification_decision_digest="sha256:" + ("d" * 64),
        publication_snapshot_digest="sha256:" + ("1" * 64),
        exact_satisfied_governance_proof_digest=None,
        approval_bundle_digest="sha256:" + ("2" * 64),
        publication_authorization_digest="sha256:" + ("3" * 64),
        action_result_digests=(_action_result().result_digest,),
        terminal_phase="finalized",
        result="success",
        uncertainty=False,
        possibly_mutated=False,
        next_action="none",
    )


def _qualification_outcome() -> AttemptOutcome:
    return AttemptOutcome(
        attempt=ATTEMPT,
        qualification_decision_digest="sha256:" + ("d" * 64),
        publication_snapshot_digest=None,
        exact_satisfied_governance_proof_digest=None,
        approval_bundle_digest=None,
        publication_authorization_digest=None,
        action_result_digests=(),
        terminal_phase="qualification",
        result="incomplete",
        uncertainty=True,
        possibly_mutated=False,
        next_action="new-attempt",
    )


def _publication_preparation_outcome() -> AttemptOutcome:
    return AttemptOutcome(
        attempt=ATTEMPT,
        qualification_decision_digest="sha256:" + ("d" * 64),
        publication_snapshot_digest=None,
        exact_satisfied_governance_proof_digest=None,
        approval_bundle_digest=None,
        publication_authorization_digest=None,
        action_result_digests=(),
        terminal_phase="publication-preparation",
        result="incomplete",
        uncertainty=True,
        possibly_mutated=False,
        next_action="new-attempt",
    )


def _observation_outcome() -> AttemptOutcome:
    return AttemptOutcome(
        attempt=ATTEMPT,
        qualification_decision_digest="sha256:" + ("d" * 64),
        publication_snapshot_digest=None,
        exact_satisfied_governance_proof_digest=None,
        approval_bundle_digest=None,
        publication_authorization_digest=None,
        action_result_digests=(),
        terminal_phase="observation",
        result="failure",
        uncertainty=False,
        possibly_mutated=False,
        next_action="reconcile",
        observation_digests=("sha256:" + ("f" * 64),),
    )


@pytest.mark.parametrize(
    "record_name",
    [
        "attempt-binding",
        "approval-bundle",
        "publication-authorization",
        "exact-satisfied-proof",
        "action-result",
        "successful-action-outcome",
        "successful-no-op-outcome",
        "qualification-outcome",
        "publication-preparation-outcome",
        "observation-outcome",
    ],
)
def test_commit8_records_round_trip_through_closed_transport(
    qualified_simulation,
    record_name: str,
) -> None:
    record = _transport_records(qualified_simulation)[record_name]
    admitted = release_record_from_document(
        record.to_document(),
        expected_type=type(record),
    )

    assert admitted == record
    assert admitted.to_document() == record.to_document()


NEW_AUTHORITY_RECORDS = (
    "approval-bundle",
    "publication-authorization",
    "exact-satisfied-proof",
)


@pytest.mark.parametrize("record_name", NEW_AUTHORITY_RECORDS)
def test_new_authority_transport_rejects_unknown_fields(
    qualified_simulation,
    record_name: str,
) -> None:
    record = _transport_records(qualified_simulation)[record_name]
    document = deepcopy(record.to_document())
    document["unexpected-authority"] = "not-admitted"

    with pytest.raises(ValueError, match="unknown field"):
        release_record_from_document(
            document,
            expected_type=type(record),
        )


@pytest.mark.parametrize("record_name", NEW_AUTHORITY_RECORDS)
def test_new_authority_transport_rejects_wrong_schema(
    qualified_simulation,
    record_name: str,
) -> None:
    record = _transport_records(qualified_simulation)[record_name]
    document = deepcopy(record.to_document())
    document["schema"] = "workflow-delivery/v3/substituted"

    with pytest.raises(ValueError, match="wrong schema"):
        release_record_from_document(
            document,
            expected_type=type(record),
        )


@pytest.mark.parametrize(
    ("record_name", "field"),
    [
        ("approval-bundle", "producer"),
        ("publication-authorization", "completed-at"),
        ("exact-satisfied-proof", "proved-at"),
    ],
)
def test_new_authority_transport_rejects_wrong_field_type(
    qualified_simulation,
    record_name: str,
    field: str,
) -> None:
    record = _transport_records(qualified_simulation)[record_name]
    document = deepcopy(record.to_document())
    document[field] = 17

    with pytest.raises(TypeError, match="must be a string"):
        release_record_from_document(
            document,
            expected_type=type(record),
        )


@pytest.mark.parametrize(
    ("record_name", "wrong_type"),
    [
        ("approval-bundle", PublicationAuthorization),
        ("publication-authorization", ExactSatisfiedGovernanceProof),
        ("exact-satisfied-proof", ApprovalBundle),
    ],
)
def test_new_authority_transport_rejects_wrong_expected_type(
    qualified_simulation,
    record_name: str,
    wrong_type: type[ReleaseRecord],
) -> None:
    document = _transport_records(qualified_simulation)[
        record_name
    ].to_document()

    with pytest.raises(
        ValueError,
        match=r"(missing required field|wrong schema)",
    ):
        release_record_from_document(
            document,
            expected_type=wrong_type,
        )


def _nested_document_keys(value: JsonValue) -> set[str]:
    if isinstance(value, dict):
        return set(value) | {
            key
            for child in value.values()
            for key in _nested_document_keys(child)
        }
    if isinstance(value, list):
        return {key for child in value for key in _nested_document_keys(child)}
    return set()


def test_approval_bundle_has_no_approval_fact(
    qualified_simulation,
) -> None:
    bundle = _transport_records(qualified_simulation)["approval-bundle"]
    document = bundle.to_document()

    assert set(document) == {
        "schema",
        "attempt",
        "publication-snapshot-reference",
        "reviewer-summary-reference",
        "producer",
        "control",
        "workflow-run-id",
    }
    assert {
        "approval-result",
        "completed-at",
        "approver",
        "approval-deployment",
    }.isdisjoint(document)


def test_publication_authorization_omits_ambient_authority(
    qualified_simulation,
) -> None:
    authorization = _transport_records(qualified_simulation)[
        "publication-authorization"
    ]
    keys = _nested_document_keys(authorization.to_document())

    assert authorization.approval_boundary.sentinel_result == "success"
    assert {
        "approver",
        "secret",
        "secrets",
        "history",
        "run-attempt",
        "approval-bundle",
        "publication-snapshot",
        "qualification-decision",
    }.isdisjoint(keys)


@pytest.mark.parametrize(
    "substitution",
    [
        None,
        "attempt",
        "publication_snapshot_reference",
        "reviewer_summary_reference",
    ],
    ids=(
        "exact-closure",
        "attempt-substitution",
        "snapshot-substitution",
        "reviewer-substitution",
    ),
)
def test_approval_bundle_requires_exact_resolved_references(
    qualified_simulation,
    substitution,
) -> None:
    _, attempt_binding, decision, publication = _live_closure(
        qualified_simulation,
        with_action=True,
    )
    bundle = _approval_bundle(attempt_binding, decision, publication)
    closure = {
        "approval_bundle": bundle,
        "intent": _intent(attempt_binding.attempt),
        "attempt_binding": attempt_binding,
        "qualification_decision": decision,
        "publication_snapshot": publication,
        "publication_snapshot_reference": _snapshot_reference(publication),
        "reviewer_summary_reference": _reviewer_reference(),
        "control": bundle.control,
    }
    if substitution == "attempt":
        closure["approval_bundle"] = replace(
            bundle,
            attempt=replace(
                bundle.attempt,
                execution=replace(
                    bundle.attempt.execution,
                    release_unit="substituted-release-unit",
                ),
            ),
        )
    elif substitution is not None:
        closure[substitution] = replace(
            closure[substitution],
            artifact_digest="sha256:" + ("e" * 64),
        )
    if substitution is None:
        assert validate_approval_bundle_closure(**closure) is None
        return

    with pytest.raises(ValueError, match="resolved closure mismatch"):
        validate_approval_bundle_closure(**closure)


@pytest.mark.parametrize(
    "substitution",
    ["intent-digest", "request-id", "execution"],
)
def test_approval_bundle_rejects_intent_attempt_substitution(
    qualified_simulation,
    substitution: str,
) -> None:
    _, attempt_binding, decision, publication = _live_closure(
        qualified_simulation,
        with_action=True,
    )
    if substitution == "intent-digest":
        substituted = replace(
            attempt_binding,
            intent_digest="sha256:" + ("e" * 64),
        )
    elif substitution == "request-id":
        substituted = replace(
            attempt_binding,
            request_id="release-request:" + ("e" * 64),
        )
    else:
        execution = replace(
            attempt_binding.execution,
            release_unit="substituted-release-unit",
        )
        substituted = replace(
            attempt_binding,
            execution=execution,
            attempt=replace(attempt_binding.attempt, execution=execution),
        )

    with pytest.raises(
        ValueError,
        match=r"^Approval Bundle Intent binding mismatch$",
    ):
        form_approval_bundle(
            intent=_intent(attempt_binding.attempt),
            attempt_binding=substituted,
            qualification_decision=decision,
            publication_snapshot=publication,
            publication_snapshot_reference=_snapshot_reference(publication),
            reviewer_summary_reference=_reviewer_reference(),
            control=f"workflow-delivery-v3:{publication.attempt.execution.target}",
        )


def test_approval_bundle_constructor_rejects_admitted_artifact_substitution(
    qualified_simulation,
) -> None:
    (
        _attempt,
        attempt_binding,
        decision,
        publication,
    ) = _live_closure(qualified_simulation, with_action=True)
    decision = replace(
        decision,
        admitted_artifact_digests=("sha256:" + ("e" * 64),),
    )
    publication = replace(
        publication,
        qualification_decision_digest=decision.decision_digest,
    )

    assert decision.admitted_artifact_digests != publication.artifact_digests
    with pytest.raises(
        ValueError,
        match=r"^Approval Bundle qualification closure mismatch$",
    ):
        form_approval_bundle(
            intent=_intent(attempt_binding.attempt),
            attempt_binding=attempt_binding,
            qualification_decision=decision,
            publication_snapshot=publication,
            publication_snapshot_reference=_snapshot_reference(publication),
            reviewer_summary_reference=_reviewer_reference(),
            control=f"workflow-delivery-v3:{publication.attempt.execution.target}",
        )


def test_approval_bundle_transport_rejects_copied_ancestor_fields(
    qualified_simulation,
) -> None:
    bundle = _transport_records(qualified_simulation)["approval-bundle"]
    assert isinstance(bundle, ApprovalBundle)
    document = deepcopy(bundle.to_document())
    document["qualification-decision"] = {"copied": True}
    with pytest.raises(
        ValueError,
        match=r"unknown field",
    ):
        release_record_from_document(
            document,
            expected_type=ApprovalBundle,
        )


def test_publication_authorization_rejects_governance_substitution(
    qualified_simulation,
) -> None:
    authorization = _transport_records(qualified_simulation)[
        "publication-authorization"
    ]
    assert isinstance(authorization, PublicationAuthorization)
    provenance = dict(authorization.governance_proof.provenance)
    provenance["blob-oid"] = "E" * 40
    substituted = tuple(sorted(provenance.items()))

    with pytest.raises(ValueError, match="blob_oid is malformed"):
        replace(
            authorization,
            governance_proof=replace(
                authorization.governance_proof,
                provenance=substituted,
            ),
        )

    document = deepcopy(authorization.to_document())
    governance = document["governance-proof"]
    assert isinstance(governance, dict)
    governance["provenance"] = [list(item) for item in substituted]
    with pytest.raises(ValueError, match="blob_oid is malformed"):
        release_record_from_document(
            document,
            expected_type=PublicationAuthorization,
        )


def test_exact_satisfied_proof_rejects_action_or_control_substitution(
    qualified_simulation,
) -> None:
    records = _transport_records(qualified_simulation)
    proof = records["exact-satisfied-proof"]
    assert isinstance(proof, ExactSatisfiedGovernanceProof)
    (
        _attempt,
        _binding,
        _decision,
        action_publication,
    ) = _live_closure(qualified_simulation, with_action=True)

    with pytest.raises(ValueError, match="actionless exact"):
        replace(
            proof,
            publication_snapshot=action_publication,
        )
    with pytest.raises(ValueError, match="control mismatch"):
        replace(proof, control=f"workflow-delivery-v3:{'0' * 40}")

    document = deepcopy(proof.to_document())
    action_snapshot_document = action_publication.to_document()
    assert action_snapshot_document["materialized-actions"]
    document["publication-snapshot"] = action_snapshot_document
    with pytest.raises(ValueError, match="actionless exact"):
        release_record_from_document(
            document,
            expected_type=ExactSatisfiedGovernanceProof,
        )


def test_receipt_is_not_a_top_level_transport_record() -> None:
    with pytest.raises(ValueError, match="unsupported transported Release"):
        release_record_from_document(
            _receipt().to_document(),
            expected_type=Receipt,  # type: ignore[arg-type]
        )


def test_persisted_release_records_require_target_derived_control(
    qualified_simulation,
) -> None:
    wrong_control = f"workflow-delivery-v3:{'0' * 40}"
    records = _transport_records(qualified_simulation)
    failed_result = _action_result(
        outcome="failed",
        mutation_disposition="no-side-effect",
        with_receipt=False,
    )

    with pytest.raises(ValueError, match="control target binding mismatch"):
        replace(qualified_simulation.binding, control=wrong_control)
    with pytest.raises(ValueError, match="control target binding mismatch"):
        replace(_receipt(), control=wrong_control)
    with pytest.raises(ValueError, match="control target binding mismatch"):
        replace(failed_result, control=wrong_control)
    for name in (
        "approval-bundle",
        "publication-authorization",
        "exact-satisfied-proof",
    ):
        with pytest.raises(ValueError, match="control"):
            replace(records[name], control=wrong_control)

    def assert_transport_rejected(
        record: ReleaseRecord,
        expected_type: type[ReleaseRecord],
        message: str = "control target binding mismatch",
    ) -> None:
        document = record.to_document()
        document["control"] = wrong_control
        with pytest.raises(
            ValueError,
            match=message,
        ):
            release_record_from_document(
                document,
                expected_type=expected_type,
            )

    assert_transport_rejected(
        qualified_simulation.binding,
        SimulationBinding,
    )
    for name in (
        "approval-bundle",
        "publication-authorization",
    ):
        record = records[name]
        assert_transport_rejected(record, type(record))
    proof = records["exact-satisfied-proof"]
    assert_transport_rejected(proof, type(proof), "control mismatch")
    assert_transport_rejected(failed_result, ActionResult)

    action_result_document = _action_result().to_document()
    receipt_document = action_result_document["receipt"]
    assert isinstance(receipt_document, dict)
    receipt_document["control"] = wrong_control
    with pytest.raises(ValueError, match="control target binding mismatch"):
        release_record_from_document(
            action_result_document,
            expected_type=ActionResult,
        )


@pytest.mark.parametrize(
    ("record", "field", "replacement", "message"),
    [
        (
            _receipt(),
            "mutable_resource_keys",
            ("coordinate:key",),
            "coordinate-plus-tag",
        ),
        (
            _action_result(),
            "receipt",
            None,
            "embedded Receipt",
        ),
        (
            _attempt_outcome(),
            "possibly_mutated",
            True,
            "Successful Attempt Outcome",
        ),
        (
            _attempt_outcome(),
            "action_result_digests",
            (),
            "Action Result lineage",
        ),
        (
            _qualification_outcome(),
            "action_result_digests",
            ("sha256:" + ("e" * 64),),
            "qualification-only",
        ),
        (
            _qualification_outcome(),
            "next_action",
            "observe-destinations",
            "qualification-only",
        ),
        pytest.param(
            _publication_preparation_outcome(),
            "uncertainty",
            False,
            r"(?i)publication[- ]preparation",
            id="uncertainty",
        ),
        pytest.param(
            _publication_preparation_outcome(),
            "exact_satisfied_governance_proof_digest",
            "sha256:" + ("f" * 64),
            "only valid for successful no-op",
            id="exact-satisfied-governance-proof-digest",
        ),
        pytest.param(
            _publication_preparation_outcome(),
            "publication_snapshot_digest",
            "sha256:" + ("f" * 64),
            r"(?i)publication[- ]preparation",
            id="publication-snapshot-digest",
        ),
        pytest.param(
            _publication_preparation_outcome(),
            "approval_bundle_digest",
            "sha256:" + ("f" * 64),
            r"(?i)publication[- ]preparation",
            id="approval-bundle-digest",
        ),
        pytest.param(
            _publication_preparation_outcome(),
            "publication_authorization_digest",
            "sha256:" + ("f" * 64),
            r"(?i)publication[- ]preparation",
            id="publication-authorization-digest",
        ),
        pytest.param(
            _publication_preparation_outcome(),
            "action_result_digests",
            ("sha256:" + ("f" * 64),),
            r"(?i)publication[- ]preparation",
            id="action-result-digests",
        ),
        pytest.param(
            _publication_preparation_outcome(),
            "result",
            "failure",
            r"(?i)publication[- ]preparation",
            id="result",
        ),
        pytest.param(
            _publication_preparation_outcome(),
            "possibly_mutated",
            True,
            r"(?i)publication[- ]preparation",
            id="possibly-mutated",
        ),
        pytest.param(
            _publication_preparation_outcome(),
            "next_action",
            "none",
            r"(?i)publication[- ]preparation",
            id="next-action",
        ),
    ],
)
def test_commit8_records_reject_independent_binding_substitutions(
    record,
    field: str,
    replacement,
    message: str,
) -> None:
    with pytest.raises(ValueError, match=message):
        replace(record, **{field: replacement})


def test_action_result_rejects_substituted_parent_receipt_binding() -> None:
    substituted_digest = "sha256:" + ("e" * 64)

    with pytest.raises(ValueError, match="embedded Receipt binding mismatch"):
        replace(
            _action_result(),
            response_identity_digest=substituted_digest,
        )

    document = _action_result().to_document()
    document["response-identity-digest"] = substituted_digest
    with pytest.raises(ValueError, match="embedded Receipt binding mismatch"):
        release_record_from_document(
            document,
            expected_type=ActionResult,
        )


@pytest.mark.parametrize(
    ("field", "document_field", "value"),
    [
        (
            "approval_bundle_digest",
            "approval-bundle-digest",
            None,
        ),
        (
            "publication_authorization_digest",
            "publication-authorization-digest",
            None,
        ),
        ("action_result_digests", "action-result-digests", ()),
        (
            "action_result_digests",
            "action-result-digests",
            (
                "sha256:" + ("e" * 64),
                "sha256:" + ("f" * 64),
            ),
        ),
    ],
)
def test_successful_outcome_requires_exact_direct_lineage(
    field: str,
    document_field: str,
    value: str | tuple[str, ...] | None,
) -> None:
    with pytest.raises(ValueError, match="lineage"):
        replace(_attempt_outcome(), **{field: value})

    document = _attempt_outcome().to_document()
    document[document_field] = (
        list(value) if isinstance(value, tuple) else value
    )
    with pytest.raises(ValueError, match="lineage"):
        release_record_from_document(
            document,
            expected_type=AttemptOutcome,
        )


@pytest.mark.parametrize(
    "outcome",
    [
        pytest.param(
            replace(_attempt_outcome(), result="failure"),
            id="failure",
        ),
        pytest.param(
            replace(
                _attempt_outcome(),
                result="incomplete-possibly-mutated",
                uncertainty=True,
                possibly_mutated=True,
                next_action="reobserve-and-replay",
            ),
            id="incomplete-possibly-mutated",
        ),
    ],
)
def test_non_successful_outcome_rejects_multiple_direct_results(
    outcome: AttemptOutcome,
) -> None:
    result_digests = (
        "sha256:" + ("e" * 64),
        "sha256:" + ("f" * 64),
    )

    with pytest.raises(ValueError, match="at most one direct Action Result"):
        replace(outcome, action_result_digests=result_digests)

    document = outcome.to_document()
    transport_values = document["action-result-digests"]
    assert isinstance(transport_values, list)
    transport_values.clear()
    transport_values.extend(result_digests)
    with pytest.raises(ValueError, match="at most one direct Action Result"):
        release_record_from_document(
            document,
            expected_type=AttemptOutcome,
        )


def test_successful_noop_outcome_requires_only_fresh_proof_lineage() -> None:
    outcome = AttemptOutcome(
        attempt=ATTEMPT,
        qualification_decision_digest="sha256:" + ("d" * 64),
        publication_snapshot_digest="sha256:" + ("1" * 64),
        exact_satisfied_governance_proof_digest=("sha256:" + ("2" * 64)),
        approval_bundle_digest=None,
        publication_authorization_digest=None,
        action_result_digests=(),
        terminal_phase="finalized-no-op",
        result="success",
        uncertainty=False,
        possibly_mutated=False,
        next_action="none",
    )

    assert outcome.exact_satisfied_governance_proof_digest is not None
    for field, value in (
        ("exact_satisfied_governance_proof_digest", None),
        ("approval_bundle_digest", "sha256:" + ("3" * 64)),
        (
            "publication_authorization_digest",
            "sha256:" + ("4" * 64),
        ),
        ("action_result_digests", ("sha256:" + ("5" * 64),)),
    ):
        with pytest.raises(ValueError, match="no-op Attempt Outcome"):
            replace(outcome, **{field: value})


def test_lost_receipt_after_possible_mutation_can_never_be_success() -> None:
    incomplete = _action_result(
        outcome="incomplete",
        mutation_disposition="possibly-mutated",
        with_receipt=False,
    )

    assert incomplete.outcome == "incomplete"
    with pytest.raises(ValueError, match="embedded Receipt"):
        replace(incomplete, outcome="success")


def test_buddy_request_normalization_and_execution_derivation_are_strict() -> (
    None
):
    intent = normalize_buddy_live_intent(
        repository="hcoona/three",
        selected_ref="refs/heads/feature",
        target=TARGET,
        actor="reviewed-actor",
        workflow_run_id=101,
    )

    assert derive_buddy_execution_identity(intent) == EXECUTION
    assert intent.request_id.startswith("release-request:")
    with pytest.raises(ValueError, match="purpose does not match"):
        derive_buddy_execution_identity(replace(intent, mode="simulation"))


def test_buddy_complete_keys_are_distinct_from_conservative_group(
    qualified_simulation,
) -> None:
    scenario = qualified_simulation
    attempt = ReleaseAttemptIdentity(
        execution=BuddyExecutionIdentity(
            channel="buddy",
            release_unit="hcoona-release-smoke-npm",
            target=scenario.snapshot.target,
        ),
        workflow_run_id=scenario.binding.simulation.workflow_run_id,
    )
    projection = replace(
        scenario.snapshot.destination_projections[0],
        destination_id="npm/github-packages-hcoona-three-v1",
        registry="https://npm.pkg.github.com",
        operation=CONDITIONAL_NPM_VERSION_AND_TAG_OPERATION,
        coordinate=ExternalPackageCoordinate(
            channel="buddy",
            destination_id="npm/github-packages-hcoona-three-v1",
            package_name="@HCOONA/Hcoona-Release-Smoke-Npm",
            native_version=scenario.snapshot.nbgv.npm_package_version,
        ),
    )

    keys = publication_mutable_resource_keys(projection, attempt)
    group = publication_lock_group(projection)
    normalized_projection = replace(
        projection,
        coordinate=replace(
            projection.coordinate,
            package_name=projection.coordinate.package_name.lower(),
        ),
    )

    assert len(keys) == COMPLETE_RESOURCE_KEY_COUNT
    assert keys[0].startswith("external-package-coordinate:")
    assert keys[1].startswith("npm-dist-tag:")
    assert group == publication_lock_group(normalized_projection)
    assert group not in keys


def _intent(attempt: ReleaseAttemptIdentity):
    return normalize_buddy_live_intent(
        repository="hcoona/three",
        selected_ref="refs/heads/feature/release",
        target=attempt.execution.target,
        actor="hcoona",
        workflow_run_id=attempt.workflow_run_id,
    )


def _live_closure(scenario, *, with_action: bool):
    attempt = ReleaseAttemptIdentity(
        execution=BuddyExecutionIdentity(
            channel="buddy",
            release_unit=scenario.snapshot.release_unit,
            target=scenario.snapshot.target,
        ),
        workflow_run_id=scenario.binding.simulation.workflow_run_id,
    )
    original = scenario.snapshot.destination_projections[0]
    coordinate = ExternalPackageCoordinate(
        channel="buddy",
        destination_id="npm/github-packages-hcoona-three-v1",
        package_name="@hcoona/hcoona-release-smoke-npm",
        native_version=scenario.snapshot.nbgv.npm_package_version,
    )
    projection = replace(
        original,
        projection_id="projection:npm:github-packages",
        destination_id=coordinate.destination_id,
        registry="https://npm.pkg.github.com",
        coordinate=coordinate,
        operation=CONDITIONAL_NPM_VERSION_AND_TAG_OPERATION,
        observation_contract_id="npm/github-packages-observation-v1",
        potential_action_id="publish-github-packages",
    )
    potential_action = replace(
        scenario.snapshot.potential_actions[0],
        contract_id=projection.potential_action_id,
        projection_id=projection.projection_id,
        operation=projection.operation,
        output=projection.output,
        capability_requirements=publication_capability_requirements(projection),
        mutable_resource_key_basis=publication_mutable_resource_key_basis(
            projection
        ),
    )
    live_snapshot = replace(
        scenario.snapshot,
        subject=attempt,
        channel="buddy",
        destination_projections=(projection,),
        potential_actions=(potential_action,),
    )
    transport = replace(
        scenario.artifact.transport,
        artifact_name=release_artifact_transport_name(
            repository=scenario.artifact.repository,
            purpose="live-release",
            output=scenario.artifact.output,
            qualification_snapshot_digest=live_snapshot.snapshot_digest,
            workflow_run_id=attempt.workflow_run_id,
            run_attempt=None,
            producer=scenario.artifact.transport.producer,
        ),
        run_attempt=None,
    )
    provenance = scenario.artifact.provenance_document()
    provenance["subject"] = attempt.to_document()
    provenance["purpose"] = "live-release"
    provenance["qualification-snapshot-digest"] = live_snapshot.snapshot_digest
    provenance["transport"] = transport.to_document()
    artifact = replace(
        scenario.artifact,
        subject=attempt,
        purpose="live-release",
        qualification_snapshot_digest=live_snapshot.snapshot_digest,
        transport=transport,
        provenance_digest=canonical_sha256(provenance),
    )
    decision = replace(
        scenario.decision,
        subject=attempt,
        qualification_snapshot_digest=live_snapshot.snapshot_digest,
        admitted_artifact_digests=(artifact.artifact_digest,),
    )
    actions: tuple[PublicationAction, ...] = ()
    if with_action:
        actions = (
            PublicationAction(
                action_id=projection.potential_action_id,
                projection=projection,
                operation=projection.operation,
                artifact=artifact,
                artifact_digest=artifact.artifact_digest,
                artifact_output=artifact.output,
                prerequisites=(),
                action_inputs=publication_action_inputs(
                    projection,
                    artifact,
                ),
                mutable_resource_keys=publication_mutable_resource_keys(
                    projection,
                    artifact,
                ),
                lock_projection=publication_lock_projection(projection),
                lock_group=publication_lock_group(projection),
                capability_requirements=(
                    publication_capability_requirements(projection)
                ),
                expected_result=publication_expected_result(projection),
                receipt_contract=publication_receipt_contract(projection),
            ),
        )
    publication = PublicationSnapshot(
        attempt=attempt,
        qualification_snapshot_digest=live_snapshot.snapshot_digest,
        qualification_decision_digest=decision.decision_digest,
        qualification_result="success",
        projection_ids=(projection.projection_id,),
        artifact_digests=(artifact.artifact_digest,),
        artifact_output_ids=(artifact.output.output_id,),
        observation_references=(
            PublicationObservationReference(
                projection_id=projection.projection_id,
                observation_digest="sha256:" + ("f" * 64),
                classification="absent" if actions else "exact-satisfied",
            ),
        ),
        materialized_actions=actions,
    )
    intent = _intent(attempt)
    attempt_binding = ReleaseAttemptBinding(
        intent_digest=intent.intent_digest,
        request_id=intent.request_id,
        execution=attempt.execution,
        attempt=attempt,
        repository_model_digest="sha256:" + ("1" * 64),
        live_eligibility_artifact_id=709,
        live_eligibility_artifact_digest="sha256:" + ("2" * 64),
        live_eligibility_payload_digest="sha256:" + ("3" * 64),
        attestation_provenance=_governance_provenance(),
    )
    return attempt, attempt_binding, decision, publication


def _snapshot_reference(
    publication: PublicationSnapshot,
) -> ArtifactReference:
    return ArtifactReference(
        artifact_id=711,
        artifact_digest="sha256:" + ("4" * 64),
        artifact_url="https://example.test/artifacts/711",
        payload_path="publication-snapshot.json",
        payload_digest=publication.snapshot_digest,
    )


def _reviewer_reference() -> ArtifactReference:
    return ArtifactReference(
        artifact_id=710,
        artifact_digest="sha256:" + ("2" * 64),
        artifact_url="https://example.test/artifacts/710",
        payload_path="reviewer-summary.md",
        payload_digest="sha256:" + ("5" * 64),
    )


def _approval_bundle(
    attempt_binding: ReleaseAttemptBinding,
    decision,
    publication: PublicationSnapshot,
) -> ApprovalBundle:
    return form_approval_bundle(
        intent=_intent(publication.attempt),
        attempt_binding=attempt_binding,
        qualification_decision=decision,
        publication_snapshot=publication,
        publication_snapshot_reference=_snapshot_reference(publication),
        reviewer_summary_reference=_reviewer_reference(),
        control=f"workflow-delivery-v3:{publication.attempt.execution.target}",
    )


def _publication_authorization(
    bundle: ApprovalBundle,
) -> PublicationAuthorization:
    return PublicationAuthorization(
        attempt=bundle.attempt,
        approval_bundle_reference=ArtifactReference(
            artifact_id=712,
            artifact_digest="sha256:" + ("6" * 64),
            artifact_url="https://example.test/artifacts/712",
            payload_path="approval-bundle.json",
            payload_digest=bundle.bundle_digest,
        ),
        approval_boundary=ApprovalBoundary(
            environment="workflow-delivery-v3-buddy-approval",
            job="approve-publication",
            sentinel_name="WDV3_APPROVAL_ENVIRONMENT_MARKER",
            sentinel_value="workflow-delivery-v3-buddy-approval/v1",
            sentinel_result="success",
        ),
        governance_proof=GovernanceProof(
            provenance=_governance_provenance(),
            current_main_sha="c" * 40,
            observed_at="2026-08-13T15:59:00Z",
            expires_at="2026-09-01T00:00:00Z",
            live_enabled=True,
        ),
        completed_at="2026-08-13T16:00:00Z",
        producer="approve-publication",
        control=bundle.control,
        workflow_run_id=bundle.workflow_run_id,
    )


def _exact_satisfied_proof(
    publication: PublicationSnapshot,
) -> ExactSatisfiedGovernanceProof:
    return ExactSatisfiedGovernanceProof(
        attempt=publication.attempt,
        publication_snapshot=publication,
        governance_provenance=_governance_provenance(),
        governance_current_main_sha="c" * 40,
        governance_expires_at="2026-09-01T00:00:00Z",
        governance_live_enabled=True,
        governance_observed_at="2026-08-13T15:59:00Z",
        proved_at="2026-08-13T16:00:00Z",
        producer="prove-exact-satisfied",
        control=f"workflow-delivery-v3:{publication.attempt.execution.target}",
    )


def _publication_action_result(
    publication: PublicationSnapshot,
    *,
    outcome: str = "success",
    mutation_disposition: str = "created",
) -> ActionResult:
    action = publication.materialized_actions[0]
    assert action.artifact.content.content_sha512 is not None
    with_receipt = outcome == "success"
    receipt = (
        Receipt(
            attempt=publication.attempt,
            publication_snapshot_digest=publication.snapshot_digest,
            action_id=action.action_id,
            action_digest=action.action_digest,
            coordinate=action.projection.coordinate,
            mutable_resource_keys=action.mutable_resource_keys,
            lock_group=action.lock_group,
            artifact_transport=action.artifact.transport,
            artifact_content_sha256=(action.artifact.content.content_sha256),
            artifact_content_sha512=(action.artifact.content.content_sha512),
            witness_digest=action.artifact.witness_digest,
            creation_result=mutation_disposition,
            tag_mapping=(
                (
                    "buddy-sha-" + publication.attempt.execution.target,
                    action.projection.coordinate.native_version,
                ),
            ),
            response_identity_digest="sha256:" + ("9" * 64),
            producer="publish-github-packages",
            control=(
                f"workflow-delivery-v3:{publication.attempt.execution.target}"
            ),
            workflow_run_id=publication.attempt.workflow_run_id,
        )
        if with_receipt
        else None
    )
    return ActionResult(
        attempt=publication.attempt,
        publication_snapshot_digest=publication.snapshot_digest,
        action_id=action.action_id,
        action_digest=action.action_digest,
        lock_group=action.lock_group,
        outcome=outcome,
        mutation_disposition=mutation_disposition,
        response_identity_digest=(
            None if receipt is None else receipt.response_identity_digest
        ),
        receipt=receipt,
        diagnostic_reference=None if receipt is not None else "publication",
        producer="publish-github-packages",
        control=(
            f"workflow-delivery-v3:{publication.attempt.execution.target}"
        ),
        workflow_run_id=publication.attempt.workflow_run_id,
    )


def _transport_records(scenario) -> dict[str, ReleaseRecord]:
    (
        _attempt,
        attempt_binding,
        decision,
        action_publication,
    ) = _live_closure(scenario, with_action=True)
    bundle = _approval_bundle(
        attempt_binding,
        decision,
        action_publication,
    )
    authorization = _publication_authorization(bundle)
    action_result = _publication_action_result(action_publication)
    (
        _noop_attempt,
        _noop_binding,
        noop_decision,
        noop_publication,
    ) = _live_closure(scenario, with_action=False)
    proof = _exact_satisfied_proof(noop_publication)
    action_outcome = AttemptOutcome(
        attempt=action_publication.attempt,
        qualification_decision_digest=decision.decision_digest,
        publication_snapshot_digest=action_publication.snapshot_digest,
        exact_satisfied_governance_proof_digest=None,
        approval_bundle_digest=bundle.bundle_digest,
        publication_authorization_digest=authorization.authorization_digest,
        action_result_digests=(action_result.result_digest,),
        terminal_phase="finalized",
        result="success",
        uncertainty=False,
        possibly_mutated=False,
        next_action="none",
    )
    noop_outcome = AttemptOutcome(
        attempt=noop_publication.attempt,
        qualification_decision_digest=noop_decision.decision_digest,
        publication_snapshot_digest=noop_publication.snapshot_digest,
        exact_satisfied_governance_proof_digest=proof.proof_digest,
        approval_bundle_digest=None,
        publication_authorization_digest=None,
        action_result_digests=(),
        terminal_phase="finalized-no-op",
        result="success",
        uncertainty=False,
        possibly_mutated=False,
        next_action="none",
    )
    return {
        "attempt-binding": attempt_binding,
        "approval-bundle": bundle,
        "publication-authorization": authorization,
        "exact-satisfied-proof": proof,
        "action-result": action_result,
        "successful-action-outcome": action_outcome,
        "successful-no-op-outcome": noop_outcome,
        "qualification-outcome": _qualification_outcome(),
        "publication-preparation-outcome": (_publication_preparation_outcome()),
        "observation-outcome": _observation_outcome(),
    }


def test_exact_preobserved_noop_requires_fresh_governance_proof(
    qualified_simulation,
) -> None:
    attempt, _binding, decision, publication = _live_closure(
        qualified_simulation,
        with_action=False,
    )
    proof = _exact_satisfied_proof(publication)

    outcome = finalize_attempt_outcome(
        attempt=attempt,
        qualification_decision=decision,
        publication_snapshot=publication,
        exact_satisfied_governance_proof=proof,
        approval_bundle=None,
        publication_authorization=None,
        action_results=(),
    )

    assert outcome.result == "success"
    assert outcome.terminal_phase == "finalized-no-op"
    assert outcome.exact_satisfied_governance_proof_digest == proof.proof_digest
    assert outcome.approval_bundle_digest is None
    assert outcome.publication_authorization_digest is None
    assert outcome.action_result_digests == ()
    missing_proof = finalize_attempt_outcome(
        attempt=attempt,
        qualification_decision=decision,
        publication_snapshot=publication,
        exact_satisfied_governance_proof=None,
        approval_bundle=None,
        publication_authorization=None,
        action_results=(),
    )

    assert missing_proof.terminal_phase == "exact-satisfied-proof-missing"
    assert missing_proof.result == "incomplete"
    assert missing_proof.uncertainty is True
    assert missing_proof.possibly_mutated is False
    assert missing_proof.next_action == "new-attempt"
    assert missing_proof.publication_snapshot_digest == (
        publication.snapshot_digest
    )
    assert missing_proof.exact_satisfied_governance_proof_digest is None


def test_exact_noop_rejects_environment_authorization_and_result_lineage(
    qualified_simulation,
) -> None:
    (
        attempt,
        noop_binding,
        decision,
        publication,
    ) = _live_closure(qualified_simulation, with_action=False)
    (
        _action_attempt,
        action_binding,
        action_decision,
        action_publication,
    ) = _live_closure(qualified_simulation, with_action=True)
    action_bundle = _approval_bundle(
        action_binding,
        action_decision,
        action_publication,
    )
    action_authorization = _publication_authorization(action_bundle)
    action_result = _publication_action_result(action_publication)
    proof = _exact_satisfied_proof(publication)
    proof_keys = _nested_document_keys(proof.to_document())

    assert noop_binding.attempt == attempt
    assert {
        "environment",
        "approval-job",
        "approval-bundle",
        "publication-authorization",
        "action-result",
        "action-result-digests",
    }.isdisjoint(proof_keys)
    for downstream in (
        {"approval_bundle": action_bundle},
        {"publication_authorization": action_authorization},
        {"action_results": (action_result,)},
    ):
        arguments: dict[str, object] = {
            "attempt": attempt,
            "qualification_decision": decision,
            "publication_snapshot": publication,
            "exact_satisfied_governance_proof": proof,
            "approval_bundle": None,
            "publication_authorization": None,
            "action_results": (),
        }
        arguments.update(downstream)
        with pytest.raises(ValueError, match=r"(mismatch|no-op)"):
            finalize_attempt_outcome(**arguments)  # type: ignore[arg-type]


def test_publication_snapshot_action_set_exactly_matches_absent_observations(
    qualified_simulation,
) -> None:
    _attempt, _binding, _decision, publication = _live_closure(
        qualified_simulation,
        with_action=False,
    )
    reference = publication.observation_references[0]

    with pytest.raises(ValueError, match="exactly cover absent"):
        replace(
            publication,
            observation_references=(
                replace(reference, classification="absent"),
            ),
        )


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("terminal_phase", "approval-contract"),
        ("uncertainty", True),
        ("possibly_mutated", True),
        ("next_action", "new-attempt"),
        ("action_result_digests", ("sha256:" + ("7" * 64),)),
    ],
)
def test_replayable_no_side_effect_outcome_requires_exact_safe_state(
    qualified_simulation,
    field: str,
    value: object,
) -> None:
    attempt, _binding, decision, publication = _live_closure(
        qualified_simulation,
        with_action=False,
    )
    outcome = AttemptOutcome(
        attempt=attempt,
        qualification_decision_digest=decision.decision_digest,
        publication_snapshot_digest=publication.snapshot_digest,
        exact_satisfied_governance_proof_digest=None,
        approval_bundle_digest=None,
        publication_authorization_digest=None,
        action_result_digests=(),
        terminal_phase="pre-authorization-termination",
        result="replayable-no-side-effect",
        uncertainty=False,
        possibly_mutated=False,
        next_action="replay",
    )

    assert outcome.result == "replayable-no-side-effect"
    assert (
        release_record_from_document(
            outcome.to_document(),
            expected_type=AttemptOutcome,
        )
        == outcome
    )
    with pytest.raises(
        ValueError,
        match="Replayable no-side-effect outcome is not exact",
    ):
        replace(outcome, **{field: value})  # type: ignore[bad-argument-type]


def test_buddy_execution_identity_document_and_concurrency_key_are_exact() -> (
    None
):
    intent = normalize_buddy_live_intent(
        repository="hcoona/three",
        selected_ref="refs/heads/feature",
        target=TARGET,
        actor="reviewed-actor",
        workflow_run_id=101,
    )
    document = derive_buddy_execution_identity(intent).to_document()
    expected_document = {
        "schema": "workflow-delivery/v3/buddy-execution-identity",
        "channel": "buddy",
        "release-unit": "hcoona-release-smoke-npm",
        "target": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
    }
    excluded_members = {
        "canonical-version",
        "native-version",
        "version",
        "external-package-coordinate",
        "package-coordinate",
        "destination-adapter",
        "destination-projection",
        "request",
        "request-id",
        "workflow-run-id",
        "run-attempt",
    }

    assert document == expected_document
    assert set(document).isdisjoint(excluded_members)
    digest = canonical_sha256(document)
    assert (
        digest == "sha256:a71c896702fc7f6869d6dc6714840eba7393c9e98eaf820d"
        "3254299d664534a6"
    )
    assert (
        digest.removeprefix("sha256:")
        == "a71c896702fc7f6869d6dc6714840eba7393c9e98eaf820d3254299d664534a6"
    )


def test_three_same_target_dispatches_share_one_caller_group_for_github_coalescing() -> (  # noqa: E501
    None
):
    intents = (
        normalize_buddy_live_intent(
            repository="hcoona/three",
            selected_ref="refs/heads/feature/one",
            target=TARGET,
            actor="actor-one",
            workflow_run_id=201,
        ),
        normalize_buddy_live_intent(
            repository="hcoona/three",
            selected_ref="refs/tags/release-candidate",
            target=TARGET,
            actor="actor-two",
            workflow_run_id=202,
        ),
        normalize_buddy_live_intent(
            repository="hcoona/three",
            selected_ref="refs/heads/hotfix/concurrency",
            target=TARGET,
            actor="actor-three",
            workflow_run_id=203,
        ),
    )
    expected_document = {
        "schema": "workflow-delivery/v3/buddy-execution-identity",
        "channel": "buddy",
        "release-unit": "hcoona-release-smoke-npm",
        "target": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
    }

    documents = tuple(
        derive_buddy_execution_identity(intent).to_document()
        for intent in intents
    )
    digests = tuple(canonical_sha256(document) for document in documents)
    keys = tuple(digest.removeprefix("sha256:") for digest in digests)
    groups = tuple(f"wdv3-execution-{key}" for key in keys)

    for field in (
        "request_id",
        "workflow_run_id",
        "selected_ref",
        "actor",
    ):
        values = {getattr(intent, field) for intent in intents}
        assert len(values) == len(intents)
    assert documents == (expected_document,) * 3
    assert (
        digests
        == (
            "sha256:a71c896702fc7f6869d6dc6714840eba7393c9e98eaf820d"
            "3254299d664534a6",
        )
        * 3
    )
    assert (
        groups
        == (
            "wdv3-execution-"
            "a71c896702fc7f6869d6dc6714840eba7393c9e98eaf820d3254299d664534a6",
        )
        * 3
    )
    # Equality proves only eligibility for GitHub's documented same-group
    # coalescing; it does not model scheduler ordering or fairness.
    assert len(set(groups)) == 1


def test_different_buddy_targets_derive_different_execution_concurrency_keys() -> (  # noqa: E501
    None
):
    intents = tuple(
        normalize_buddy_live_intent(
            repository="hcoona/three",
            selected_ref="refs/heads/feature",
            target=target,
            actor="reviewed-actor",
            workflow_run_id=301,
        )
        for target in (TARGET, "b" * 40)
    )
    documents = tuple(
        derive_buddy_execution_identity(intent).to_document()
        for intent in intents
    )
    digests = tuple(canonical_sha256(document) for document in documents)
    keys = tuple(digest.removeprefix("sha256:") for digest in digests)
    groups = tuple(f"wdv3-execution-{key}" for key in keys)

    assert documents == (
        {
            "schema": "workflow-delivery/v3/buddy-execution-identity",
            "channel": "buddy",
            "release-unit": "hcoona-release-smoke-npm",
            "target": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        },
        {
            "schema": "workflow-delivery/v3/buddy-execution-identity",
            "channel": "buddy",
            "release-unit": "hcoona-release-smoke-npm",
            "target": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
        },
    )
    assert digests == (
        "sha256:a71c896702fc7f6869d6dc6714840eba7393c9e98eaf820d"
        "3254299d664534a6",
        "sha256:9eeac4fd6533b5afb39ebb70ed223833578e268b6d9b0bd4"
        "6111687465778bd6",
    )
    assert keys == (
        "a71c896702fc7f6869d6dc6714840eba7393c9e98eaf820d3254299d664534a6",
        "9eeac4fd6533b5afb39ebb70ed223833578e268b6d9b0bd46111687465778bd6",
    )
    assert groups == (
        "wdv3-execution-"
        "a71c896702fc7f6869d6dc6714840eba7393c9e98eaf820d3254299d664534a6",
        "wdv3-execution-"
        "9eeac4fd6533b5afb39ebb70ed223833578e268b6d9b0bd46111687465778bd6",
    )
    assert {document["target"] for document in documents} == {
        "a" * 40,
        "b" * 40,
    }
    assert len(set(digests)) == len(intents)
    assert len(set(keys)) == len(intents)
    assert len(set(groups)) == len(intents)


def _sha256_authority_records(
    qualified_simulation,
) -> dict[
    str,
    PublicationAuthorization | ExactSatisfiedGovernanceProof,
]:
    records = _transport_records(qualified_simulation)
    authorization = records["publication-authorization"]
    proof = records["exact-satisfied-proof"]
    assert isinstance(authorization, PublicationAuthorization)
    assert isinstance(proof, ExactSatisfiedGovernanceProof)

    provenance = dict(_governance_provenance())
    provenance["blob-oid"] = "b" * 64
    provenance["eligibility-main-sha"] = "a" * 64
    provenance["git-object-format"] = "sha256"
    sha256_provenance = tuple(sorted(provenance.items()))

    return {
        "publication-authorization": replace(
            authorization,
            governance_proof=replace(
                authorization.governance_proof,
                provenance=sha256_provenance,
                current_main_sha="a" * 64,
            ),
        ),
        "exact-satisfied-governance-proof": replace(
            proof,
            governance_provenance=sha256_provenance,
            governance_current_main_sha="a" * 64,
        ),
    }


@pytest.mark.parametrize(
    "record_name",
    [
        pytest.param(
            "publication-authorization",
            id="publication-authorization",
        ),
        pytest.param(
            "exact-satisfied-governance-proof",
            id="exact-satisfied-governance-proof",
        ),
    ],
)
def test_new_authority_records_round_trip_sha256_governance_provenance(
    qualified_simulation,
    record_name: str,
) -> None:
    record = _sha256_authority_records(qualified_simulation)[record_name]

    parsed = release_record_from_document(
        record.to_document(),
        expected_type=type(record),
    )

    assert parsed == record
    if isinstance(parsed, PublicationAuthorization):
        provenance = dict(parsed.governance_proof.provenance)
        current_main_sha = parsed.governance_proof.current_main_sha
    else:
        assert isinstance(parsed, ExactSatisfiedGovernanceProof)
        provenance = dict(parsed.governance_provenance)
        current_main_sha = parsed.governance_current_main_sha
    assert provenance["git-object-format"] == "sha256"
    assert current_main_sha == "a" * 64
    assert provenance["blob-oid"] == "b" * 64


@pytest.mark.parametrize(
    ("record_name", "message"),
    [
        pytest.param(
            "exact-satisfied-proof",
            r"^Exact-satisfied Governance proof requires Live enabled$",
            id="exact-satisfied-governance-proof",
        ),
    ],
)
def test_new_authority_records_reject_disabled_governance(
    qualified_simulation,
    record_name: str,
    message: str,
) -> None:
    record = _transport_records(qualified_simulation)[record_name]

    with pytest.raises(ValueError, match=message):
        replace(record, governance_live_enabled=False)


def test_publication_authorization_rejects_disabled_governance(
    qualified_simulation,
) -> None:
    authorization = _transport_records(qualified_simulation)[
        "publication-authorization"
    ]
    assert isinstance(authorization, PublicationAuthorization)

    with pytest.raises(ValueError, match="requires Live enabled"):
        replace(
            authorization,
            governance_proof=replace(
                authorization.governance_proof,
                live_enabled=False,
            ),
        )


@pytest.mark.parametrize(
    ("record_name", "enabled_field", "expected_type", "message"),
    [
        pytest.param(
            "exact-satisfied-proof",
            "governance-live-enabled",
            ExactSatisfiedGovernanceProof,
            r"^Exact-satisfied Governance proof requires Live enabled$",
            id="exact-satisfied-governance-proof",
        ),
    ],
)
def test_new_authority_transport_rejects_disabled_governance(
    qualified_simulation,
    record_name: str,
    enabled_field: str,
    expected_type: type[ReleaseRecord],
    message: str,
) -> None:
    document = deepcopy(
        _transport_records(qualified_simulation)[record_name].to_document()
    )
    document[enabled_field] = False

    with pytest.raises(ValueError, match=message):
        release_record_from_document(
            document,
            expected_type=expected_type,
        )


def test_publication_authorization_transport_rejects_disabled_governance(
    qualified_simulation,
) -> None:
    document = deepcopy(
        _transport_records(qualified_simulation)[
            "publication-authorization"
        ].to_document()
    )
    governance = document["governance-proof"]
    assert isinstance(governance, dict)
    governance["live-enabled"] = False

    with pytest.raises(ValueError, match="requires Live enabled"):
        release_record_from_document(
            document,
            expected_type=PublicationAuthorization,
        )


_PUBLICATION_AUTHORIZATION_TIME_ERROR = (
    r"^Publication Authorization requires governance observed_at <= "
    r"completed_at < governance expires_at$"
)
_EXACT_SATISFIED_PROOF_TIME_ERROR = (
    r"^Exact-satisfied Governance proof requires "
    r"governance_observed_at <= proved_at < governance_expires_at$"
)


@pytest.mark.parametrize(
    "completed_at",
    [
        pytest.param(
            "2026-08-13T15:58:59Z",
            id="before-governance-observation",
        ),
        pytest.param(
            "2026-09-01T00:00:00Z",
            id="at-governance-expiry",
        ),
        pytest.param(
            "2026-09-01T00:00:01Z",
            id="after-governance-expiry",
        ),
    ],
)
def test_publication_authorization_rejects_time_outside_governance_window(
    qualified_simulation,
    completed_at: str,
) -> None:
    authorization = _transport_records(qualified_simulation)[
        "publication-authorization"
    ]
    assert isinstance(authorization, PublicationAuthorization)

    with pytest.raises(
        ValueError,
        match=_PUBLICATION_AUTHORIZATION_TIME_ERROR,
    ):
        replace(authorization, completed_at=completed_at)


def test_publication_authorization_accepts_governance_observation_boundary(
    qualified_simulation,
) -> None:
    authorization = _transport_records(qualified_simulation)[
        "publication-authorization"
    ]
    assert isinstance(authorization, PublicationAuthorization)
    boundary = replace(
        authorization,
        completed_at=authorization.governance_proof.observed_at,
    )

    assert (
        release_record_from_document(
            boundary.to_document(),
            expected_type=PublicationAuthorization,
        )
        == boundary
    )


@pytest.mark.parametrize(
    "proved_at",
    [
        pytest.param(
            "2026-08-13T15:58:59Z",
            id="before-governance-observation",
        ),
        pytest.param(
            "2026-09-01T00:00:00Z",
            id="at-governance-expiry",
        ),
        pytest.param(
            "2026-09-01T00:00:01Z",
            id="after-governance-expiry",
        ),
    ],
)
def test_exact_proof_constructor_rejects_time_outside_governance_window(
    qualified_simulation,
    proved_at: str,
) -> None:
    proof = _transport_records(qualified_simulation)["exact-satisfied-proof"]
    assert isinstance(proof, ExactSatisfiedGovernanceProof)

    with pytest.raises(
        ValueError,
        match=_EXACT_SATISFIED_PROOF_TIME_ERROR,
    ):
        replace(proof, proved_at=proved_at)


@pytest.mark.parametrize(
    "proved_at",
    [
        pytest.param(
            "2026-08-13T15:58:59Z",
            id="before-governance-observation",
        ),
        pytest.param(
            "2026-09-01T00:00:00Z",
            id="at-governance-expiry",
        ),
        pytest.param(
            "2026-09-01T00:00:01Z",
            id="after-governance-expiry",
        ),
    ],
)
def test_exact_satisfied_proof_transport_rejects_time_outside_governance_window(
    qualified_simulation,
    proved_at: str,
) -> None:
    proof = _transport_records(qualified_simulation)["exact-satisfied-proof"]
    assert isinstance(proof, ExactSatisfiedGovernanceProof)
    document = deepcopy(proof.to_document())
    document["proved-at"] = proved_at

    with pytest.raises(
        ValueError,
        match=_EXACT_SATISFIED_PROOF_TIME_ERROR,
    ):
        release_record_from_document(
            document,
            expected_type=ExactSatisfiedGovernanceProof,
        )


def test_exact_satisfied_proof_accepts_governance_observation_boundary(
    qualified_simulation,
) -> None:
    proof = _transport_records(qualified_simulation)["exact-satisfied-proof"]
    assert isinstance(proof, ExactSatisfiedGovernanceProof)
    boundary_proof = replace(
        proof,
        proved_at=proof.governance_observed_at,
    )

    parsed = release_record_from_document(
        boundary_proof.to_document(),
        expected_type=ExactSatisfiedGovernanceProof,
    )

    assert boundary_proof.proved_at == proof.governance_observed_at
    assert parsed == boundary_proof


@pytest.mark.parametrize(
    ("observed_at", "proved_at", "expires_at"),
    [
        pytest.param(
            "2026-08-13T15:59:00.900Z",
            "2026-08-13T15:59:00Z",
            "2026-09-01T00:00:00Z",
            id="terminal-before-fractional-observation",
        ),
        pytest.param(
            "2026-08-13T15:59:00Z",
            "2026-09-01T00:00:00.900Z",
            "2026-09-01T00:00:00Z",
            id="fractional-terminal-after-expiry",
        ),
    ],
)
def test_exact_proof_constructor_rejects_invalid_mixed_precision_window(
    qualified_simulation,
    observed_at: str,
    proved_at: str,
    expires_at: str,
) -> None:
    proof = _transport_records(qualified_simulation)["exact-satisfied-proof"]
    assert isinstance(proof, ExactSatisfiedGovernanceProof)

    with pytest.raises(
        ValueError,
        match=_EXACT_SATISFIED_PROOF_TIME_ERROR,
    ):
        replace(
            proof,
            governance_observed_at=observed_at,
            proved_at=proved_at,
            governance_expires_at=expires_at,
        )


@pytest.mark.parametrize(
    ("observed_at", "proved_at", "expires_at"),
    [
        pytest.param(
            "2026-08-13T15:59:00.900Z",
            "2026-08-13T15:59:00Z",
            "2026-09-01T00:00:00Z",
            id="terminal-before-fractional-observation",
        ),
        pytest.param(
            "2026-08-13T15:59:00Z",
            "2026-09-01T00:00:00.900Z",
            "2026-09-01T00:00:00Z",
            id="fractional-terminal-after-expiry",
        ),
    ],
)
def test_exact_satisfied_proof_transport_rejects_invalid_mixed_precision_window(
    qualified_simulation,
    observed_at: str,
    proved_at: str,
    expires_at: str,
) -> None:
    proof = _transport_records(qualified_simulation)["exact-satisfied-proof"]
    assert isinstance(proof, ExactSatisfiedGovernanceProof)
    document = deepcopy(proof.to_document())
    document["governance-observed-at"] = observed_at
    document["proved-at"] = proved_at
    document["governance-expires-at"] = expires_at

    with pytest.raises(
        ValueError,
        match=_EXACT_SATISFIED_PROOF_TIME_ERROR,
    ):
        release_record_from_document(
            document,
            expected_type=ExactSatisfiedGovernanceProof,
        )


@pytest.mark.parametrize(
    ("observed_at", "proved_at", "expires_at"),
    [
        pytest.param(
            "2026-08-13T15:59:00Z",
            "2026-08-13T15:59:00.100Z",
            "2026-09-01T00:00:00Z",
            id="fractional-terminal-after-observation",
        ),
        pytest.param(
            "2026-08-13T15:59:00Z",
            "2026-09-01T00:00:00Z",
            "2026-09-01T00:00:00.100Z",
            id="terminal-before-fractional-expiry",
        ),
    ],
)
def test_exact_satisfied_proof_accepts_valid_mixed_precision_window(
    qualified_simulation,
    observed_at: str,
    proved_at: str,
    expires_at: str,
) -> None:
    proof = _transport_records(qualified_simulation)["exact-satisfied-proof"]
    assert isinstance(proof, ExactSatisfiedGovernanceProof)
    accepted = replace(
        proof,
        governance_observed_at=observed_at,
        proved_at=proved_at,
        governance_expires_at=expires_at,
    )
    document = deepcopy(accepted.to_document())

    assert document["governance-observed-at"] == observed_at
    assert document["proved-at"] == proved_at
    assert document["governance-expires-at"] == expires_at
    assert (
        release_record_from_document(
            document,
            expected_type=ExactSatisfiedGovernanceProof,
        )
        == accepted
    )
