"""Current-DAG live authorization and finalization scenarios."""

from __future__ import annotations

# ruff: noqa: D103, FBT001, PLR2004
import hashlib
from dataclasses import replace
from typing import Any

import pytest
from three_workflow_delivery_v3.canonical import canonical_sha256, canonicalize
from three_workflow_delivery_v3.records.release import (
    PUBLISHER_GOVERNANCE_RECHECK_FAILED_BEFORE_RUNNER,
    ActionResult,
    AttemptOutcome,
    AuthorizationRecord,
    BuddyExecutionIdentity,
    CapabilityAdmissionDecision,
    ExternalPackageCoordinate,
    ObservationRequestFacts,
    ObservationResponseFacts,
    ObservationValue,
    ProjectionObservation,
    PublicationAction,
    PublicationObservationReference,
    PublicationSnapshot,
    Receipt,
    ReleaseAttemptIdentity,
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
from three_workflow_delivery_v3.release import live
from three_workflow_delivery_v3.release.live import finalize_attempt_outcome

TARGET = "a" * 40
SNAPSHOT_BYTES = b'{"schema":"workflow-delivery/v3/publication-snapshot"}'
SNAPSHOT_DIGEST = f"sha256:{hashlib.sha256(SNAPSHOT_BYTES).hexdigest()}"
SUMMARY = (
    b"# Buddy publication review\n\nSnapshot: "
    + SNAPSHOT_DIGEST.encode()
    + b"\n"
)
SUMMARY_DIGEST = f"sha256:{hashlib.sha256(SUMMARY).hexdigest()}"
EXPECTED_LIVE_API = (
    "LiveCapabilityAdmissionResult",
    "PublicRevisionCheckout",
    "ReviewerArtifact",
    "ReviewerPayload",
    "admit_live_capability",
    "bind_reviewer_artifact",
    "fetch_exact_public_revision",
    "finalize_attempt_outcome",
    "form_authorization_record",
    "materialize_reviewer_artifact",
    "materialize_reviewer_payload",
)


def _control(attempt: ReleaseAttemptIdentity) -> str:
    return f"workflow-delivery-v3:{attempt.execution.target}"


def _require_api(name: str) -> Any:
    value = getattr(live, name, None)
    assert callable(value), f"live production API is missing: {name}"
    return value


def _closure(scenario, *, with_action: bool):
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
        operation="npm-publish-create-only",
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
    snapshot = replace(
        scenario.snapshot,
        subject=attempt,
        channel="buddy",
        destination_projections=(projection,),
        potential_actions=(potential_action,),
    )
    decision = replace(
        scenario.decision,
        subject=attempt,
        qualification_snapshot_digest=snapshot.snapshot_digest,
    )
    actions: tuple[PublicationAction, ...] = ()
    if with_action:
        transport = replace(
            scenario.artifact.transport,
            artifact_name=release_artifact_transport_name(
                repository=scenario.artifact.repository,
                purpose="live-release",
                output=scenario.artifact.output,
                qualification_snapshot_digest=snapshot.snapshot_digest,
                workflow_run_id=attempt.workflow_run_id,
                run_attempt=None,
                producer=scenario.artifact.transport.producer,
            ),
            run_attempt=None,
        )
        provenance = scenario.artifact.provenance_document()
        provenance["subject"] = attempt.to_document()
        provenance["purpose"] = "live-release"
        provenance["qualification-snapshot-digest"] = snapshot.snapshot_digest
        provenance["transport"] = transport.to_document()
        artifact = replace(
            scenario.artifact,
            subject=attempt,
            purpose="live-release",
            qualification_snapshot_digest=snapshot.snapshot_digest,
            transport=transport,
            provenance_digest=canonical_sha256(provenance),
        )
        actions = (
            PublicationAction(
                action_id=projection.potential_action_id,
                projection=projection,
                operation=projection.operation,
                artifact=artifact,
                artifact_digest=artifact.artifact_digest,
                artifact_output=artifact.output,
                prerequisites=(),
                action_inputs=publication_action_inputs(projection, artifact),
                mutable_resource_keys=publication_mutable_resource_keys(
                    projection, artifact
                ),
                lock_projection=publication_lock_projection(projection),
                lock_group=publication_lock_group(projection),
                capability_requirements=publication_capability_requirements(
                    projection
                ),
                expected_result=publication_expected_result(projection),
                receipt_contract=publication_receipt_contract(projection),
            ),
        )
    publication = PublicationSnapshot(
        attempt=attempt,
        qualification_snapshot_digest=snapshot.snapshot_digest,
        qualification_decision_digest=decision.decision_digest,
        qualification_result="success",
        projection_ids=(projection.projection_id,),
        artifact_digests=(
            actions[0].artifact_digest
            if actions
            else scenario.artifact.artifact_digest,
        ),
        artifact_output_ids=(
            actions[0].artifact_output.output_id
            if actions
            else scenario.artifact.output.output_id,
        ),
        observation_references=(
            PublicationObservationReference(
                projection_id=projection.projection_id,
                observation_digest="sha256:" + ("f" * 64),
                classification="absent" if actions else "exact-satisfied",
            ),
        ),
        materialized_actions=actions,
    )
    authorization = AuthorizationRecord(
        attempt=attempt,
        publication_snapshot_digest=publication.snapshot_digest,
        reviewer_summary_artifact_id=710,
        reviewer_summary_upload_digest="sha256:" + ("2" * 64),
        reviewer_summary_payload_digest=SUMMARY_DIGEST,
        workflow_run_id=attempt.workflow_run_id,
        approval_job_id=711,
        approval_job="approval",
        environment="workflow-delivery-v3-buddy-approval",
        channel="buddy",
        completed_at="2026-08-13T16:00:00Z",
        producer="approval",
        control=_control(attempt),
    )
    return attempt, decision, publication, authorization


def _reviewer(publication: PublicationSnapshot):
    return _require_api("materialize_reviewer_artifact")(
        snapshot_bytes=canonicalize(publication.to_document()),
        summary_bytes=SUMMARY,
        artifact_id=710,
        upload_digest="sha256:" + ("2" * 64),
    )


def _capability(
    attempt: ReleaseAttemptIdentity,
    publication: PublicationSnapshot,
    authorization: AuthorizationRecord,
) -> CapabilityAdmissionDecision:
    return _require_api("admit_live_capability")(
        attempt=attempt,
        authorization=authorization,
        publication_snapshot=publication,
        reviewer_artifact=_reviewer(publication),
        control=authorization.control,
    )


def _successful_action_result(
    publication: PublicationSnapshot,
) -> ActionResult:
    action = publication.materialized_actions[0]
    assert action.artifact.content.content_sha512 is not None
    receipt = Receipt(
        attempt=publication.attempt,
        publication_snapshot_digest=publication.snapshot_digest,
        action_id=action.action_id,
        action_digest=action.action_digest,
        coordinate=action.projection.coordinate,
        mutable_resource_keys=action.mutable_resource_keys,
        lock_group=action.lock_group,
        artifact_transport=action.artifact.transport,
        artifact_content_sha256=action.artifact.content.content_sha256,
        artifact_content_sha512=action.artifact.content.content_sha512,
        witness_digest=action.artifact.witness_digest,
        creation_result="created",
        tag_mapping=(
            (
                "buddy-sha-" + publication.attempt.execution.target,
                action.projection.coordinate.native_version,
            ),
        ),
        response_identity_digest="sha256:" + ("9" * 64),
        producer="publish-github-packages",
        control=_control(publication.attempt),
        workflow_run_id=publication.attempt.workflow_run_id,
    )
    return ActionResult(
        attempt=publication.attempt,
        publication_snapshot_digest=publication.snapshot_digest,
        action_id=action.action_id,
        action_digest=action.action_digest,
        lock_group=action.lock_group,
        outcome="success",
        mutation_disposition="created",
        response_identity_digest=receipt.response_identity_digest,
        receipt=receipt,
        diagnostic_reference=None,
        producer="publish-github-packages",
        control=_control(publication.attempt),
        workflow_run_id=publication.attempt.workflow_run_id,
    )


def _blocking_observation(
    *,
    attempt: ReleaseAttemptIdentity,
    qualification_decision,
    projection,
    classification: str,
) -> ProjectionObservation:
    desired_state_digest = "sha256:" + ("9" * 64)
    value = ObservationValue(
        classification=classification,
        owner=None,
        coordinate=None,
        content_sha512=None,
        witness_digest=None,
        routing=(),
    )
    request_facts = ObservationRequestFacts(
        qualification_snapshot_digest=(
            qualification_decision.qualification_snapshot_digest
        ),
        projection_digest=projection.projection_digest,
        desired_state_digest=desired_state_digest,
        method="GET",
        url="https://api.github.com/users/hcoona/packages/npm/"
        "hcoona-release-smoke-npm/versions",
        headers=(),
    )
    response_facts = ObservationResponseFacts(
        stage="synthetic",
        requested_url=request_facts.url,
        final_url=request_facts.url,
        redirects=(),
        status=200,
        selected_headers=(),
        truncated=False,
        body_sha256=None,
        status_detail=classification,
    )
    response_digest = canonical_sha256(
        {
            "schema": "workflow-delivery/v3/observation-response",
            "request-digest": request_facts.request_digest,
            "facts": response_facts.to_document(),
            "value": value.to_document(),
        }
    )
    return ProjectionObservation(
        subject=attempt,
        purpose="live-release",
        target=attempt.execution.target,
        producer="observe-github-packages",
        qualification_snapshot_digest=(
            qualification_decision.qualification_snapshot_digest
        ),
        projection=projection,
        desired_state_digest=desired_state_digest,
        observation_contract_id=projection.observation_contract_id,
        request_facts=request_facts,
        request_digest=request_facts.request_digest,
        response_facts=response_facts,
        response_digest=response_digest,
        value=value,
    )


def test_live_api_has_no_history_query_surface() -> None:
    assert tuple(live.__all__) == EXPECTED_LIVE_API
    assert not hasattr(live, "discover_execution_history")
    assert not hasattr(live, "form_execution_history_admission_snapshot")


def test_reviewer_artifact_preserves_exact_bytes_and_bindings() -> None:
    artifact = _require_api("materialize_reviewer_artifact")(
        snapshot_bytes=SNAPSHOT_BYTES,
        summary_bytes=SUMMARY,
        artifact_id=710,
        upload_digest="sha256:" + ("2" * 64),
    )

    assert artifact.summary_bytes == SUMMARY
    assert artifact.snapshot_bytes == SNAPSHOT_BYTES
    assert artifact.snapshot_payload_digest == SNAPSHOT_DIGEST
    assert artifact.summary_payload_digest == SUMMARY_DIGEST
    assert artifact.artifact_id == 710


@pytest.mark.parametrize(
    "target",
    ["a" * 39, "A" * 40, "a" * 41, "refs/heads/main"],
)
def test_anonymous_fetch_rejects_non_exact_target_without_transport(
    target: str,
) -> None:
    calls: list[tuple[str, ...]] = []

    with pytest.raises(ValueError, match="40-character lowercase SHA"):
        _require_api("fetch_exact_public_revision")(
            target=target,
            run=lambda argv: calls.append(tuple(argv)),
        )

    assert calls == []


def test_anonymous_fetch_verifies_exact_commit_and_detached_head() -> None:
    calls: list[tuple[str, ...]] = []

    def runner(argv: tuple[str, ...]) -> str:
        calls.append(argv)
        if argv[-2:] == ("rev-parse", "HEAD"):
            return TARGET
        if argv[-3:] == ("symbolic-ref", "-q", "HEAD"):
            return ""
        return ""

    checkout = _require_api("fetch_exact_public_revision")(
        target=TARGET,
        run=runner,
    )

    assert checkout.target == TARGET
    assert checkout.detached is True
    assert any("https://github.com/hcoona/three.git" in call for call in calls)
    assert all("GITHUB_TOKEN" not in " ".join(call) for call in calls)


@pytest.mark.parametrize(
    ("substitution", "diagnostic"),
    [
        ("disabled", "governance-live-disabled"),
        ("expired", "governance-attestation-expired"),
        ("resolved-commit", "governance-provenance-changed"),
        ("blob", "governance-provenance-changed"),
        ("content", "governance-content-changed"),
        ("binding", "governance-binding-changed"),
    ],
)
def test_governance_substitution_blocks_current_attempt(
    substitution: str,
    diagnostic: str,
) -> None:
    result = _require_api("admit_live_capability")(
        substitution=substitution,
        restored=True,
    )

    assert result.current_attempt.authorizing is False
    assert diagnostic in result.current_attempt.diagnostics
    assert result.restored_attempt.attempt != result.current_attempt.attempt
    assert result.restored_attempt.authorizing is True


def test_exact_noop_requires_authorization_and_no_publication_results(
    qualified_simulation,
) -> None:
    attempt, decision, publication, authorization = _closure(
        qualified_simulation,
        with_action=False,
    )

    outcome = finalize_attempt_outcome(
        attempt=attempt,
        qualification_decision=decision,
        publication_snapshot=publication,
        authorization=authorization,
        capability_decisions=(),
        action_results=(),
    )

    assert outcome.result == "success"
    assert outcome.terminal_phase == "finalized-no-op"
    assert outcome.capability_admission_digests == ()
    assert outcome.action_result_digests == ()


@pytest.mark.parametrize(
    ("terminal_result", "failure_class", "next_action", "uncertainty"),
    [
        (
            "failure",
            "quality-failure",
            "fix-quality-failure-and-rerun",
            False,
        ),
        ("incomplete", "incomplete-qualification", "new-attempt", True),
    ],
)
def test_unsuccessful_qualification_terminalizes_without_publication(
    qualified_simulation,
    terminal_result: str,
    failure_class: str,
    next_action: str,
    uncertainty: bool,
) -> None:
    attempt, decision, publication, _authorization = _closure(
        qualified_simulation,
        with_action=False,
    )
    unsuccessful = replace(
        decision,
        terminal_result=terminal_result,
        failure_class=failure_class,
        next_action=next_action,
    )

    outcome = finalize_attempt_outcome(
        attempt=attempt,
        qualification_decision=unsuccessful,
        publication_snapshot=None,
        authorization=None,
        capability_decisions=(),
        action_results=(),
    )

    assert isinstance(outcome, AttemptOutcome)
    assert outcome.terminal_phase == "qualification"
    assert outcome.result == terminal_result
    assert outcome.uncertainty is uncertainty
    assert outcome.next_action == next_action

    with pytest.raises(ValueError, match="cannot bind publication records"):
        finalize_attempt_outcome(
            attempt=attempt,
            qualification_decision=unsuccessful,
            publication_snapshot=publication,
            authorization=None,
            capability_decisions=(),
            action_results=(),
        )


def test_publication_preparation_interruption_has_no_downstream_lineage(
    qualified_simulation,
) -> None:
    attempt, decision, publication, authorization = _closure(
        qualified_simulation,
        with_action=True,
    )
    capability = _capability(attempt, publication, authorization)
    result = _successful_action_result(publication)

    outcome = finalize_attempt_outcome(
        attempt=attempt,
        qualification_decision=decision,
        publication_snapshot=None,
        authorization=None,
        capability_decisions=(),
        action_results=(),
        publication_preparation_interrupted=True,
    )

    assert outcome.terminal_phase == "publication-preparation"
    assert outcome.result == "incomplete"
    assert outcome.next_action == "new-attempt"

    for downstream in (
        {"publication_snapshot": publication},
        {"authorization": authorization},
        {"capability_decisions": (capability,)},
        {"action_results": (result,)},
        {"platform_terminated": True},
        {"capability_may_have_started": True},
    ):
        arguments: dict[str, object] = {
            "attempt": attempt,
            "qualification_decision": decision,
            "publication_snapshot": None,
            "authorization": None,
            "capability_decisions": (),
            "action_results": (),
            "publication_preparation_interrupted": True,
        }
        arguments.update(downstream)
        with pytest.raises(ValueError, match="contradictory records"):
            finalize_attempt_outcome(**arguments)  # type: ignore[arg-type]


@pytest.mark.parametrize(
    ("classification", "result", "uncertainty"),
    [
        ("partial", "failure", False),
        ("conflicting", "failure", False),
        ("unknown", "incomplete", True),
        ("unprovable", "incomplete", True),
    ],
)
def test_blocking_observation_requires_reconciliation(
    qualified_simulation,
    classification: str,
    result: str,
    uncertainty: bool,
) -> None:
    attempt, decision, publication, _authorization = _closure(
        qualified_simulation,
        with_action=True,
    )
    observation = _blocking_observation(
        attempt=attempt,
        qualification_decision=decision,
        projection=publication.materialized_actions[0].projection,
        classification=classification,
    )

    outcome = finalize_attempt_outcome(
        attempt=attempt,
        qualification_decision=decision,
        publication_snapshot=None,
        authorization=None,
        capability_decisions=(),
        action_results=(),
        observations=(observation,),
        publication_preparation_interrupted=True,
    )

    assert outcome.observation_digests == (observation.observation_digest,)
    assert outcome.terminal_phase == "observation"
    assert outcome.result == result
    assert outcome.uncertainty is uncertainty
    assert outcome.next_action == "reconcile"


def test_diagnostic_only_rejection_never_schedules_capability() -> None:
    calls: list[str] = []

    with pytest.raises(ValueError, match="diagnostic-only"):
        _require_api("form_authorization_record")(
            approval_result="deployment-review-denied",
            diagnostic={"review-id": 91},
            schedule_capability=lambda: calls.append("scheduled"),
        )

    assert calls == []


def test_successful_approval_forms_bound_authorization_without_scheduling(
    qualified_simulation,
) -> None:
    attempt, _, publication, _ = _closure(
        qualified_simulation,
        with_action=False,
    )
    calls: list[str] = []

    authorization = _require_api("form_authorization_record")(
        approval_result="success",
        attempt=attempt,
        publication_snapshot=publication,
        reviewer_artifact=_reviewer(publication),
        approval_job_id=711,
        completed_at="2026-08-13T16:00:00Z",
        control=_control(attempt),
        schedule_capability=lambda: calls.append("scheduled"),
    )

    assert authorization.attempt == attempt
    assert (
        authorization.publication_snapshot_digest == publication.snapshot_digest
    )
    assert authorization.environment == "workflow-delivery-v3-buddy-approval"
    assert calls == []


def test_successful_approval_rejects_substituted_control(
    qualified_simulation,
) -> None:
    attempt, _, publication, _ = _closure(
        qualified_simulation,
        with_action=False,
    )

    with pytest.raises(ValueError, match="Authorization control binding"):
        _require_api("form_authorization_record")(
            approval_result="success",
            attempt=attempt,
            publication_snapshot=publication,
            reviewer_artifact=_reviewer(publication),
            approval_job_id=711,
            completed_at="2026-08-13T16:00:00Z",
            control=f"workflow-delivery-v3:{'0' * 40}",
        )


def test_capability_admission_closes_action_and_resource_sets(
    qualified_simulation,
) -> None:
    attempt, _, publication, authorization = _closure(
        qualified_simulation,
        with_action=True,
    )
    action = publication.materialized_actions[0]

    decision = _capability(attempt, publication, authorization)

    assert decision.authorizing is True
    assert decision.action_digests == (action.action_digest,)
    assert decision.artifact_digests == (action.artifact_digest,)
    assert decision.resource_key_sets == (
        (action.action_id, action.mutable_resource_keys),
    )
    assert decision.lock_groups == ((action.action_id, action.lock_group),)


def test_capability_admission_rejects_substituted_control(
    qualified_simulation,
) -> None:
    attempt, _, publication, authorization = _closure(
        qualified_simulation,
        with_action=True,
    )

    with pytest.raises(
        ValueError,
        match="Capability admission control binding mismatch",
    ):
        _require_api("admit_live_capability")(
            attempt=attempt,
            authorization=authorization,
            publication_snapshot=publication,
            reviewer_artifact=_reviewer(publication),
            control=f"workflow-delivery-v3:{'0' * 40}",
        )


def test_missing_authorization_is_replayable_until_downstream_evidence_exists(
    qualified_simulation,
) -> None:
    attempt, decision, publication, authorization = _closure(
        qualified_simulation,
        with_action=True,
    )

    replayable = finalize_attempt_outcome(
        attempt=attempt,
        qualification_decision=decision,
        publication_snapshot=publication,
        authorization=None,
        capability_decisions=(),
        action_results=(),
    )
    contradictory = finalize_attempt_outcome(
        attempt=attempt,
        qualification_decision=decision,
        publication_snapshot=publication,
        authorization=None,
        capability_decisions=(
            _capability(attempt, publication, authorization),
        ),
        action_results=(),
    )

    assert replayable.result == "unknown-replayable-approval-contract"
    assert replayable.possibly_mutated is False
    assert contradictory.result == "incomplete-possibly-mutated"
    assert contradictory.terminal_phase == "authorization-contradiction"
    assert contradictory.possibly_mutated is True


def test_live_finalizer_consumes_direct_action_result_with_embedded_receipt(
    qualified_simulation,
) -> None:
    attempt, decision, publication, authorization = _closure(
        qualified_simulation,
        with_action=True,
    )
    capability = _capability(attempt, publication, authorization)
    result = _successful_action_result(publication)

    outcome = finalize_attempt_outcome(
        attempt=attempt,
        qualification_decision=decision,
        publication_snapshot=publication,
        authorization=authorization,
        capability_decisions=(capability,),
        action_results=(result,),
    )

    assert outcome.result == "success"
    assert outcome.action_result_digests == (result.result_digest,)


def test_live_finalizer_recomputes_action_result_and_receipt_bindings(
    qualified_simulation,
) -> None:
    attempt, decision, publication, authorization = _closure(
        qualified_simulation,
        with_action=True,
    )
    capability = _capability(attempt, publication, authorization)
    result = _successful_action_result(publication)
    assert result.receipt is not None

    substituted_capability = replace(
        capability,
        resource_key_sets=(
            (result.action_id, ("external-package-coordinate:x",)),
        ),
    )
    with pytest.raises(ValueError, match="Capability Decision is not exact"):
        finalize_attempt_outcome(
            attempt=attempt,
            qualification_decision=decision,
            publication_snapshot=publication,
            authorization=authorization,
            capability_decisions=(substituted_capability,),
            action_results=(result,),
        )

    substituted_digest = "sha256:" + ("8" * 64)
    substituted_receipt = replace(
        result.receipt,
        action_digest=substituted_digest,
    )
    substituted_result = replace(
        result,
        action_digest=substituted_digest,
        receipt=substituted_receipt,
    )
    with pytest.raises(ValueError, match="Action Result binding mismatch"):
        finalize_attempt_outcome(
            attempt=attempt,
            qualification_decision=decision,
            publication_snapshot=publication,
            authorization=authorization,
            capability_decisions=(capability,),
            action_results=(substituted_result,),
        )

    substituted_receipt = replace(
        result.receipt,
        artifact_transport=replace(
            result.receipt.artifact_transport,
            artifact_id=999,
        ),
    )
    with pytest.raises(ValueError, match="Receipt binding mismatch"):
        finalize_attempt_outcome(
            attempt=attempt,
            qualification_decision=decision,
            publication_snapshot=publication,
            authorization=authorization,
            capability_decisions=(capability,),
            action_results=(replace(result, receipt=substituted_receipt),),
        )


def test_one_direct_result_is_required_per_action(
    qualified_simulation,
) -> None:
    attempt, decision, publication, authorization = _closure(
        qualified_simulation,
        with_action=True,
    )

    with pytest.raises(ValueError, match="one direct result per action"):
        finalize_attempt_outcome(
            attempt=attempt,
            qualification_decision=decision,
            publication_snapshot=publication,
            authorization=authorization,
            capability_decisions=(
                _capability(attempt, publication, authorization),
            ),
            action_results=(),
        )


def test_receipt_loss_after_possible_mutation_requires_reobservation() -> None:
    attempt = ReleaseAttemptIdentity(
        execution=BuddyExecutionIdentity(
            channel="buddy",
            release_unit="hcoona-release-smoke-npm",
            target=TARGET,
        ),
        workflow_run_id=101,
    )
    incomplete = ActionResult(
        attempt=attempt,
        publication_snapshot_digest="sha256:" + ("1" * 64),
        action_id="publish-github-packages",
        action_digest="sha256:" + ("2" * 64),
        lock_group="destination-package:lock",
        outcome="incomplete",
        mutation_disposition="possibly-mutated",
        response_identity_digest=None,
        receipt=None,
        diagnostic_reference="receipt-lost",
        producer="publish-github-packages",
        control=_control(attempt),
        workflow_run_id=101,
    )

    assert incomplete.receipt is None
    with pytest.raises(ValueError, match="embedded Receipt"):
        replace(incomplete, outcome="success")


@pytest.mark.parametrize(
    ("started", "result", "phase", "next_action"),
    [
        (
            False,
            "replayable-no-side-effect",
            "pre-capability-termination",
            "replay",
        ),
        (
            True,
            "incomplete-possibly-mutated",
            "post-capability-termination",
            "reobserve-and-replay",
        ),
    ],
)
def test_platform_termination_maps_by_capability_phase(
    qualified_simulation,
    started: bool,
    result: str,
    phase: str,
    next_action: str,
) -> None:
    attempt, decision, publication, authorization = _closure(
        qualified_simulation,
        with_action=False,
    )

    outcome = finalize_attempt_outcome(
        attempt=attempt,
        qualification_decision=decision,
        publication_snapshot=publication,
        authorization=authorization,
        capability_decisions=(),
        action_results=(),
        platform_terminated=True,
        capability_may_have_started=started,
    )

    assert (outcome.result, outcome.terminal_phase, outcome.next_action) == (
        result,
        phase,
        next_action,
    )
    assert outcome.possibly_mutated is started


def test_direct_action_result_is_platform_start_evidence(
    qualified_simulation,
) -> None:
    attempt, decision, publication, authorization = _closure(
        qualified_simulation,
        with_action=True,
    )
    result = _successful_action_result(publication)

    outcome = finalize_attempt_outcome(
        attempt=attempt,
        qualification_decision=decision,
        publication_snapshot=publication,
        authorization=authorization,
        capability_decisions=(),
        action_results=(result,),
        platform_terminated=True,
    )

    assert outcome.result == "incomplete-possibly-mutated"
    assert outcome.possibly_mutated is True
    assert outcome.action_result_digests == (result.result_digest,)


def test_platform_termination_rejects_multiple_direct_results(
    qualified_simulation,
) -> None:
    attempt, decision, publication, authorization = _closure(
        qualified_simulation,
        with_action=True,
    )
    result = _successful_action_result(publication)
    assert result.receipt is not None
    substituted_digest = "sha256:" + ("8" * 64)
    second_result = replace(
        result,
        response_identity_digest=substituted_digest,
        receipt=replace(
            result.receipt,
            response_identity_digest=substituted_digest,
        ),
    )

    with pytest.raises(ValueError, match="at most one direct Action Result"):
        finalize_attempt_outcome(
            attempt=attempt,
            qualification_decision=decision,
            publication_snapshot=publication,
            authorization=authorization,
            capability_decisions=(),
            action_results=(result, second_result),
            platform_terminated=True,
        )


def test_platform_termination_rejects_multiple_capability_decisions(
    qualified_simulation,
) -> None:
    attempt, decision, publication, authorization = _closure(
        qualified_simulation,
        with_action=True,
    )
    capability = _capability(attempt, publication, authorization)
    second_capability = replace(
        capability,
        live_eligibility_artifact_id=(
            capability.live_eligibility_artifact_id + 1
        ),
    )

    with pytest.raises(ValueError, match="at most one Capability Admission"):
        finalize_attempt_outcome(
            attempt=attempt,
            qualification_decision=decision,
            publication_snapshot=publication,
            authorization=authorization,
            capability_decisions=(capability, second_capability),
            action_results=(),
            platform_terminated=True,
        )


def test_platform_termination_rejects_misbound_action_result(
    qualified_simulation,
) -> None:
    attempt, decision, publication, authorization = _closure(
        qualified_simulation,
        with_action=True,
    )
    result = _successful_action_result(publication)
    assert result.receipt is not None
    substituted_digest = "sha256:" + ("8" * 64)
    substituted_lock = "destination-package:substituted"
    candidates = (
        replace(
            result,
            action_id="publish-substituted",
            receipt=replace(
                result.receipt,
                action_id="publish-substituted",
            ),
        ),
        replace(
            result,
            action_digest=substituted_digest,
            receipt=replace(
                result.receipt,
                action_digest=substituted_digest,
            ),
        ),
        replace(
            result,
            lock_group=substituted_lock,
            receipt=replace(
                result.receipt,
                lock_group=substituted_lock,
            ),
        ),
    )

    for candidate in candidates:
        with pytest.raises(ValueError, match="Action Result binding mismatch"):
            finalize_attempt_outcome(
                attempt=attempt,
                qualification_decision=decision,
                publication_snapshot=publication,
                authorization=authorization,
                capability_decisions=(),
                action_results=(candidate,),
                platform_terminated=True,
            )


@pytest.mark.parametrize(
    ("diagnostic", "phase", "next_action"),
    [
        (
            PUBLISHER_GOVERNANCE_RECHECK_FAILED_BEFORE_RUNNER,
            "capability-blocked",
            "new-attempt",
        ),
        ("create-conflict", "finalized", "replay"),
    ],
)
def test_failed_no_side_effect_result_classifies_replay_policy(
    qualified_simulation,
    diagnostic: str,
    phase: str,
    next_action: str,
) -> None:
    attempt, decision, publication, authorization = _closure(
        qualified_simulation,
        with_action=True,
    )
    capability = _capability(attempt, publication, authorization)
    action = publication.materialized_actions[0]
    result = ActionResult(
        attempt=attempt,
        publication_snapshot_digest=publication.snapshot_digest,
        action_id=action.action_id,
        action_digest=action.action_digest,
        lock_group=action.lock_group,
        outcome="failed",
        mutation_disposition="no-side-effect",
        response_identity_digest=None,
        receipt=None,
        diagnostic_reference=diagnostic,
        producer="publish-github-packages",
        control=_control(attempt),
        workflow_run_id=attempt.workflow_run_id,
    )

    outcome = finalize_attempt_outcome(
        attempt=attempt,
        qualification_decision=decision,
        publication_snapshot=publication,
        authorization=authorization,
        capability_decisions=(capability,),
        action_results=(result,),
    )

    assert outcome.result == "failure"
    assert outcome.terminal_phase == phase
    assert outcome.next_action == next_action
    assert outcome.possibly_mutated is False


def test_mixed_attempt_action_result_is_rejected(
    qualified_simulation,
) -> None:
    attempt, decision, publication, authorization = _closure(
        qualified_simulation,
        with_action=True,
    )
    result = _successful_action_result(publication)
    assert result.receipt is not None
    mixed_attempt = replace(
        attempt,
        workflow_run_id=attempt.workflow_run_id + 1,
    )
    mixed_receipt = replace(
        result.receipt,
        attempt=mixed_attempt,
        artifact_transport=replace(
            result.receipt.artifact_transport,
            workflow_run_id=mixed_attempt.workflow_run_id,
        ),
        workflow_run_id=mixed_attempt.workflow_run_id,
    )
    mixed_result = replace(
        result,
        attempt=mixed_attempt,
        receipt=mixed_receipt,
        workflow_run_id=mixed_attempt.workflow_run_id,
    )

    with pytest.raises(
        ValueError,
        match="Mixed-attempt failed-job reruns are not admissible",
    ):
        finalize_attempt_outcome(
            attempt=attempt,
            qualification_decision=decision,
            publication_snapshot=publication,
            authorization=authorization,
            capability_decisions=(
                _capability(attempt, publication, authorization),
            ),
            action_results=(mixed_result,),
        )
