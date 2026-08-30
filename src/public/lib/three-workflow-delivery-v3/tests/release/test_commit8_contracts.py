"""Focused strict contract tests for Workflow Delivery v3 commit 8."""

from __future__ import annotations

# ruff: noqa: D103, ISC004
from dataclasses import FrozenInstanceError, fields, replace

import pytest
from three_workflow_delivery_v3.canonical import JsonValue, canonical_sha256
from three_workflow_delivery_v3.records import release as release_records
from three_workflow_delivery_v3.records.artifacts import (
    ArtifactTransportIdentity,
)
from three_workflow_delivery_v3.records.bindings import (
    AdmissionMode,
    CurrentAuthorityContext,
    admit,
)
from three_workflow_delivery_v3.records.release import (
    ActionResult,
    AttemptOutcome,
    AuthorizationRecord,
    BuddyExecutionIdentity,
    CapabilityAdmissionDecision,
    CapabilityGroupResultBundle,
    ExternalPackageCoordinate,
    PublicationObservationReference,
    PublicationSnapshot,
    Receipt,
    ReleaseAttemptBinding,
    ReleaseAttemptIdentity,
    publication_capability_requirements,
    publication_lock_group,
    publication_mutable_resource_key_basis,
    publication_mutable_resource_keys,
)
from three_workflow_delivery_v3.records.release_transport import (
    release_record_from_document,
)
from three_workflow_delivery_v3.release.identity import (
    derive_buddy_execution_identity,
    normalize_buddy_live_intent,
)
from three_workflow_delivery_v3.release.live import finalize_attempt_outcome

TARGET = "a" * 40
ARTIFACT_DIGEST = "sha256:" + ("b" * 64)
CONTROL = "control:" + ("c" * 64)
COMPLETE_RESOURCE_KEY_COUNT = 2
EXECUTION = BuddyExecutionIdentity(
    channel="buddy",
    release_unit="hcoona-release-smoke-npm",
    target=TARGET,
)
ATTEMPT = ReleaseAttemptIdentity(
    execution=EXECUTION,
    workflow_run_id=101,
    run_attempt=2,
)

COMMIT8_RECORD_TYPES = (
    "HistoricalExecutionRecord",
    "ExecutionHistoryAdmissionSnapshot",
    "ReleaseAttemptBinding",
    "AuthorizationRecord",
    "CapabilityAdmissionDecision",
    "ActionResult",
    "CapabilityGroupResultBundle",
    "Receipt",
    "AttemptOutcome",
)


def _current_payload() -> dict[str, JsonValue]:
    return {
        "release_execution": canonical_sha256(EXECUTION.to_document()),
        "purpose": "live-release",
        "request": "release-request:" + ("d" * 64),
        "workflow_run_id": ATTEMPT.workflow_run_id,
        "run_attempt": ATTEMPT.run_attempt,
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
        run_attempt=ATTEMPT.run_attempt,
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


def test_attempt_identity_is_exact_frozen_and_current_attempt_bound() -> None:
    assert tuple(field.name for field in fields(ATTEMPT)) == (
        "execution",
        "workflow_run_id",
        "run_attempt",
    )
    assert ATTEMPT.to_document() == {
        "schema": "workflow-delivery/v3/release-attempt-identity",
        "execution": EXECUTION.to_document(),
        "workflow-run-id": 101,
        "run-attempt": 2,
    }
    assert hasattr(ReleaseAttemptIdentity, "__slots__")
    with pytest.raises(FrozenInstanceError):
        ATTEMPT.run_attempt = 3  # type: ignore[misc]


def test_exact_current_attempt_authority_preserves_every_trusted_binding() -> (
    None
):
    payload = _current_payload()
    context = _current_context(payload)

    admitted = admit(
        mode=AdmissionMode.CURRENT_AUTHORITY,
        payload=payload,
        artifact_id=context.artifact_id,
        artifact_digest=context.artifact_digest,
        current=context,
    )

    assert admitted.mode is AdmissionMode.CURRENT_AUTHORITY
    assert admitted.history_only is False
    assert admitted.release_execution == context.release_execution
    assert admitted.purpose == "live-release"
    assert admitted.target == TARGET
    assert admitted.control_identity == CONTROL
    assert admitted.artifact_digest == ARTIFACT_DIGEST
    assert admitted.payload_digest == canonical_sha256(payload)
    assert admitted.platform_run is None
    assert admitted.platform_job is None
    assert admitted.diagnostic_claims == ()


@pytest.mark.parametrize(
    ("field", "replacement"),
    [
        ("release_execution", "sha256:" + ("e" * 64)),
        ("request", "release-request:" + ("e" * 64)),
        ("workflow_run_id", 102),
        ("run_attempt", 1),
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
            mode=AdmissionMode.CURRENT_AUTHORITY,
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
            mode=AdmissionMode.CURRENT_AUTHORITY,
            payload=payload,
            artifact_id=context.artifact_id + 1,
            artifact_digest=context.artifact_digest,
            current=context,
        )
    with pytest.raises(ValueError, match="artifact_digest"):
        admit(
            mode=AdmissionMode.CURRENT_AUTHORITY,
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
            mode=AdmissionMode.CURRENT_AUTHORITY,
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
            mode=AdmissionMode.CURRENT_AUTHORITY,
            payload=payload,
            artifact_id=context.artifact_id,
            artifact_digest=context.artifact_digest,
            current=context,
        )


def _authorization() -> AuthorizationRecord:
    return AuthorizationRecord(
        attempt=ATTEMPT,
        publication_snapshot_digest="sha256:" + ("1" * 64),
        reviewer_summary_artifact_id=710,
        reviewer_summary_upload_digest="sha256:" + ("2" * 64),
        reviewer_summary_payload_digest="sha256:" + ("3" * 64),
        workflow_run_id=ATTEMPT.workflow_run_id,
        run_attempt=ATTEMPT.run_attempt,
        approval_job_id=711,
        approval_job="approval",
        environment="workflow-delivery-v3-buddy-approval",
        channel="buddy",
        completed_at="2026-08-13T16:00:00Z",
        producer="approval",
        control=CONTROL,
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
        attestation_provenance=(
            ("blob-oid", "blob"),
            ("content-sha256", "sha256:" + ("4" * 64)),
            ("path", ".github/governance.json"),
            ("ref", "refs/heads/main"),
            ("repository", "hcoona/three"),
            ("resolved-commit", TARGET),
        ),
        history_snapshot_artifact_id=710,
        history_snapshot_artifact_digest="sha256:" + ("5" * 64),
    )


def _capability_decision(
    *, result: str = "success"
) -> CapabilityAdmissionDecision:
    return CapabilityAdmissionDecision(
        attempt=ATTEMPT,
        authorization_digest=_authorization().authorization_digest,
        publication_snapshot_digest="sha256:" + ("1" * 64),
        reviewer_summary_artifact_id=710,
        reviewer_summary_upload_digest="sha256:" + ("2" * 64),
        reviewer_summary_payload_digest="sha256:" + ("3" * 64),
        action_digests=("sha256:" + ("4" * 64),),
        artifact_digests=("sha256:" + ("5" * 64),),
        resource_key_sets=(
            (
                "action:publish",
                (
                    "external-package-coordinate:key",
                    "npm-dist-tag:key",
                ),
            ),
        ),
        lock_groups=(("action:publish", "destination-package:lock"),),
        capability_group_manifest=(
            ("group:github-packages", ("action:publish",)),
        ),
        live_eligibility_artifact_id=712,
        live_eligibility_artifact_digest="sha256:" + ("6" * 64),
        governance_provenance=(
            ("blob-oid", "blob"),
            ("content-sha256", "sha256:" + ("7" * 64)),
            ("path", ".github/governance.json"),
            ("ref", "refs/heads/main"),
            ("repository", "hcoona/three"),
            ("resolved-commit", TARGET),
        ),
        governance_content_sha256="sha256:" + ("7" * 64),
        governance_expires_at="2026-09-01T00:00:00Z",
        governance_live_enabled=result == "success",
        producer="approval-finalizer",
        control=CONTROL,
        workflow_run_id=ATTEMPT.workflow_run_id,
        run_attempt=ATTEMPT.run_attempt,
        result=result,
        diagnostics=()
        if result == "success"
        else ("governance-live-disabled",),
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
        run_attempt=ATTEMPT.run_attempt,
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
        run_attempt=ATTEMPT.run_attempt,
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
        receipt_artifact_id=730 if with_receipt else None,
        receipt_artifact_name="receipt.json" if with_receipt else None,
        receipt_artifact_digest=(
            "sha256:" + ("d" * 64) if with_receipt else None
        ),
        receipt_payload_digest=(
            receipt.receipt_digest if with_receipt else None
        ),
        receipt_digest=receipt.receipt_digest if with_receipt else None,
        diagnostic_reference=None if with_receipt else "lost-receipt",
        producer=receipt.producer,
        control=CONTROL,
        workflow_run_id=ATTEMPT.workflow_run_id,
        run_attempt=ATTEMPT.run_attempt,
    )


def _group_bundle() -> CapabilityGroupResultBundle:
    return CapabilityGroupResultBundle(
        attempt=ATTEMPT,
        publication_snapshot_digest="sha256:" + ("1" * 64),
        capability_group="group:github-packages",
        planned_action_ids=("action:publish",),
        action_results=(_action_result(),),
        completion_state="complete",
        producer="publish-github-packages",
        control=CONTROL,
        workflow_run_id=ATTEMPT.workflow_run_id,
        run_attempt=ATTEMPT.run_attempt,
    )


def _attempt_outcome() -> AttemptOutcome:
    return AttemptOutcome(
        attempt=ATTEMPT,
        qualification_decision_digest="sha256:" + ("d" * 64),
        publication_snapshot_digest="sha256:" + ("1" * 64),
        authorization_digest=_authorization().authorization_digest,
        capability_admission_digests=(_capability_decision().decision_digest,),
        capability_group_bundle_digests=("sha256:" + ("e" * 64),),
        receipt_digests=(_receipt().receipt_digest,),
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
        authorization_digest=None,
        capability_admission_digests=(),
        capability_group_bundle_digests=(),
        receipt_digests=(),
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
        authorization_digest=None,
        capability_admission_digests=(),
        capability_group_bundle_digests=(),
        receipt_digests=(),
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
        authorization_digest=None,
        capability_admission_digests=(),
        capability_group_bundle_digests=(),
        receipt_digests=(),
        terminal_phase="observation",
        result="failure",
        uncertainty=False,
        possibly_mutated=False,
        next_action="reconcile",
        observation_digests=("sha256:" + ("f" * 64),),
    )


@pytest.mark.parametrize(
    "record",
    [
        _attempt_binding(),
        _authorization(),
        _capability_decision(),
        _receipt(),
        _action_result(),
        _group_bundle(),
        _attempt_outcome(),
        _qualification_outcome(),
        _publication_preparation_outcome(),
        _observation_outcome(),
    ],
)
def test_commit8_records_round_trip_through_closed_transport(record) -> None:
    admitted = release_record_from_document(
        record.to_document(),
        expected_type=type(record),
    )

    assert admitted == record
    assert admitted.to_document() == record.to_document()


@pytest.mark.parametrize(
    ("record", "field", "replacement", "message"),
    [
        (
            _attempt_binding(),
            "history_snapshot_artifact_digest",
            "not-a-digest",
            "history_snapshot_artifact_digest",
        ),
        (_authorization(), "run_attempt", 3, "current Attempt"),
        pytest.param(
            _authorization(),
            "environment",
            "workflow-delivery-v3-buddy-smoke-approval",
            "Authorization approval producer/job/Environment is not exact",
            id="authorization-old-transitional-environment",
        ),
        pytest.param(
            _authorization(),
            "environment",
            "workflow-delivery-v3-buddy-github-packages",
            "Authorization approval producer/job/Environment is not exact",
            id="authorization-capability-environment",
        ),
        pytest.param(
            _authorization(),
            "environment",
            "Workflow-delivery-v3-buddy-approval",
            "Authorization approval producer/job/Environment is not exact",
            id="authorization-case-altered-environment",
        ),
        (
            _capability_decision(),
            "governance_live_enabled",
            False,
            "fresh Governance",
        ),
        (
            _receipt(),
            "mutable_resource_keys",
            ("coordinate:key",),
            "coordinate-plus-tag",
        ),
        (
            _action_result(),
            "receipt_digest",
            None,
            "Receipt reference",
        ),
        (
            _group_bundle(),
            "planned_action_ids",
            ("action:other",),
            "action set is not exact",
        ),
        (
            _attempt_outcome(),
            "possibly_mutated",
            True,
            "Successful Attempt Outcome",
        ),
        (
            _qualification_outcome(),
            "receipt_digests",
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
            "authorization_digest",
            "sha256:" + ("f" * 64),
            r"(?i)publication[- ]preparation",
            id="authorization-digest",
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
            "capability_admission_digests",
            ("sha256:" + ("f" * 64),),
            r"(?i)publication[- ]preparation",
            id="capability-admission-digests",
        ),
        pytest.param(
            _publication_preparation_outcome(),
            "capability_group_bundle_digests",
            ("sha256:" + ("f" * 64),),
            r"(?i)publication[- ]preparation",
            id="capability-group-bundle-digests",
        ),
        pytest.param(
            _publication_preparation_outcome(),
            "receipt_digests",
            ("sha256:" + ("f" * 64),),
            r"(?i)publication[- ]preparation",
            id="receipt-digests",
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


def test_diagnostic_review_cannot_authorize() -> None:
    with pytest.raises(ValueError, match="Only successful approval"):
        replace(_authorization(), result="denied")
    with pytest.raises(ValueError, match="Only successful approval"):
        replace(_authorization(), result="deployment-review-approved")


def test_blocked_capability_decision_is_non_authorizing_and_attempt_local() -> (
    None
):
    blocked = _capability_decision(result="blocked")

    assert blocked.authorizing is False
    with pytest.raises(ValueError, match="current Attempt"):
        replace(blocked, run_attempt=ATTEMPT.run_attempt + 1)


def test_group_bundle_requires_exact_action_set_equality() -> None:
    result = _action_result()
    with pytest.raises(ValueError, match="action set is not exact"):
        CapabilityGroupResultBundle(
            attempt=ATTEMPT,
            publication_snapshot_digest=result.publication_snapshot_digest,
            capability_group="group:github-packages",
            planned_action_ids=("action:extra", "action:publish"),
            action_results=(result,),
            completion_state="complete",
            producer=result.producer,
            control=CONTROL,
            workflow_run_id=ATTEMPT.workflow_run_id,
            run_attempt=ATTEMPT.run_attempt,
        )


def test_lost_receipt_after_possible_mutation_can_never_be_success() -> None:
    incomplete = _action_result(
        outcome="incomplete",
        mutation_disposition="possibly-mutated",
        with_receipt=False,
    )

    assert incomplete.outcome == "incomplete"
    with pytest.raises(ValueError, match="durable Receipt"):
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
        run_attempt=2,
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
        run_attempt=scenario.binding.simulation.run_attempt,
    )
    projection = replace(
        scenario.snapshot.destination_projections[0],
        destination_id="npm/github-packages-hcoona-three-v1",
        registry="https://npm.pkg.github.com",
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


def _live_noop_closure(scenario):
    attempt = ReleaseAttemptIdentity(
        execution=BuddyExecutionIdentity(
            channel="buddy",
            release_unit=scenario.snapshot.release_unit,
            target=scenario.snapshot.target,
        ),
        workflow_run_id=scenario.binding.simulation.workflow_run_id,
        run_attempt=scenario.binding.simulation.run_attempt,
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
    decision = replace(
        scenario.decision,
        subject=attempt,
        qualification_snapshot_digest=live_snapshot.snapshot_digest,
    )
    publication = PublicationSnapshot(
        attempt=attempt,
        qualification_snapshot_digest=live_snapshot.snapshot_digest,
        qualification_decision_digest=decision.decision_digest,
        qualification_result="success",
        projection_ids=(projection.projection_id,),
        artifact_digests=(scenario.artifact.artifact_digest,),
        artifact_output_ids=(scenario.artifact.output.output_id,),
        observation_references=(
            PublicationObservationReference(
                projection_id=projection.projection_id,
                observation_digest="sha256:" + ("f" * 64),
                classification="exact-satisfied",
            ),
        ),
        materialized_actions=(),
    )
    authorization = replace(
        _authorization(),
        attempt=attempt,
        publication_snapshot_digest=publication.snapshot_digest,
        workflow_run_id=attempt.workflow_run_id,
        run_attempt=attempt.run_attempt,
    )
    return attempt, decision, publication, authorization


def test_exact_preobserved_noop_requires_authorization_and_zero_capability(
    qualified_simulation,
) -> None:
    attempt, decision, publication, authorization = _live_noop_closure(
        qualified_simulation
    )

    outcome = finalize_attempt_outcome(
        attempt=attempt,
        qualification_decision=decision,
        publication_snapshot=publication,
        authorization=authorization,
        capability_decisions=(),
        group_bundles=(),
        receipts=(),
    )

    assert outcome.result == "success"
    assert outcome.terminal_phase == "finalized-no-op"
    assert outcome.capability_admission_digests == ()
    assert outcome.capability_group_bundle_digests == ()
    assert outcome.receipt_digests == ()
    unknown = finalize_attempt_outcome(
        attempt=attempt,
        qualification_decision=decision,
        publication_snapshot=publication,
        authorization=None,
        capability_decisions=(),
        group_bundles=(),
        receipts=(),
    )

    assert unknown.result == "unknown-replayable-approval-contract"
    assert unknown.terminal_phase == "approval-contract"
    assert unknown.authorization_digest is None
    assert unknown.possibly_mutated is False


def test_publication_snapshot_action_set_exactly_matches_absent_observations(
    qualified_simulation,
) -> None:
    _attempt, _decision, publication, _authorization = _live_noop_closure(
        qualified_simulation
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
    ("capability_started", "result", "next_action"),
    [
        (False, "replayable-no-side-effect", "replay"),
        (True, "incomplete-possibly-mutated", "reobserve-and-replay"),
    ],
)
def test_platform_termination_maps_by_capability_phase(
    qualified_simulation,
    capability_started: bool,  # noqa: FBT001
    result: str,
    next_action: str,
) -> None:
    attempt, decision, publication, authorization = _live_noop_closure(
        qualified_simulation
    )

    outcome = finalize_attempt_outcome(
        attempt=attempt,
        qualification_decision=decision,
        publication_snapshot=publication,
        authorization=authorization,
        capability_decisions=(),
        group_bundles=(),
        receipts=(),
        platform_terminated=True,
        capability_may_have_started=capability_started,
    )

    assert outcome.result == result
    assert outcome.next_action == next_action
    assert outcome.possibly_mutated is capability_started


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("terminal_phase", "post-capability-termination"),
        ("uncertainty", True),
        ("possibly_mutated", True),
        ("next_action", "reobserve-and-replay"),
        ("capability_group_bundle_digests", ("sha256:" + ("7" * 64),)),
        ("receipt_digests", ("sha256:" + ("8" * 64),)),
    ],
)
def test_replayable_no_side_effect_outcome_requires_exact_safe_state(
    qualified_simulation,
    field: str,
    value: object,
) -> None:
    attempt, decision, publication, authorization = _live_noop_closure(
        qualified_simulation
    )
    outcome = finalize_attempt_outcome(
        attempt=attempt,
        qualification_decision=decision,
        publication_snapshot=publication,
        authorization=authorization,
        capability_decisions=(),
        group_bundles=(),
        receipts=(),
        platform_terminated=True,
        capability_may_have_started=False,
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
        run_attempt=2,
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
            run_attempt=1,
        ),
        normalize_buddy_live_intent(
            repository="hcoona/three",
            selected_ref="refs/tags/release-candidate",
            target=TARGET,
            actor="actor-two",
            workflow_run_id=202,
            run_attempt=2,
        ),
        normalize_buddy_live_intent(
            repository="hcoona/three",
            selected_ref="refs/heads/hotfix/concurrency",
            target=TARGET,
            actor="actor-three",
            workflow_run_id=203,
            run_attempt=7,
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
        "run_attempt",
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
            run_attempt=1,
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
