"""Current-DAG live authorization and finalization scenarios."""

from __future__ import annotations

# ruff: noqa: D103, FBT001
from dataclasses import replace
from datetime import UTC, datetime, timedelta
from typing import TYPE_CHECKING, Any

import pytest
from three_workflow_delivery_v3.adapters import github_packages
from three_workflow_delivery_v3.canonical import canonical_sha256
from three_workflow_delivery_v3.records.artifacts import ArtifactReference
from three_workflow_delivery_v3.records.release import (
    CONDITIONAL_NPM_VERSION_AND_TAG_OPERATION,
    ActionResult,
    ApprovalBundle,
    AttemptOutcome,
    BuddyExecutionIdentity,
    ExactSatisfiedGovernanceProof,
    ExternalPackageCoordinate,
    ObservationRequestFacts,
    ObservationResponseFacts,
    ObservationValue,
    ProjectionObservation,
    PublicationAction,
    PublicationAuthorization,
    PublicationObservationReference,
    PublicationSnapshot,
    Receipt,
    ReleaseAttemptBinding,
    ReleaseAttemptIdentity,
    form_publication_action,
    publication_capability_requirements,
    publication_mutable_resource_key_basis,
    release_artifact_transport_name,
)
from three_workflow_delivery_v3.release import live
from three_workflow_delivery_v3.release.eligibility import (
    AccessGrant,
    AccessInventory,
    ApprovalEnvironmentAttestation,
    ApprovalEnvironmentReviewer,
    ApprovalEnvironmentVariable,
    ArtifactRetentionAttestation,
    DestinationPrimitiveAttestation,
    EnabledGovernanceActivation,
    GovernanceAttestation,
    GovernanceObservation,
    NativeEvidence,
    PackagePrincipalAttestation,
    WriterInventoryEntry,
    governance_observation_provenance,
)
from three_workflow_delivery_v3.release.identity import (
    normalize_buddy_live_intent,
)
from three_workflow_delivery_v3.release.live import finalize_attempt_outcome
from three_workflow_delivery_v3.repository.descriptors import (
    GOVERNANCE_MAX_AGE_DAYS,
    GOVERNANCE_PATH,
    GOVERNANCE_REF,
    GOVERNANCE_REPOSITORY,
    GovernanceSource,
)

if TYPE_CHECKING:
    from collections.abc import Callable

TARGET = "a" * 40
SUMMARY_DIGEST = "sha256:" + ("5" * 64)
EXPECTED_LIVE_API = (
    "PublicRevisionCheckout",
    "fetch_exact_public_revision",
    "finalize_attempt_outcome",
    "form_approval_bundle",
    "form_exact_satisfied_governance_proof",
    "form_publication_authorization",
    "validate_approval_bundle_closure",
)


def _control(attempt: ReleaseAttemptIdentity) -> str:
    return f"workflow-delivery-v3:{attempt.execution.target}"


def _require_api(name: str) -> Any:
    value = getattr(live, name, None)
    assert callable(value), f"live production API is missing: {name}"
    return value


def _intent(attempt: ReleaseAttemptIdentity):
    return normalize_buddy_live_intent(
        repository="hcoona/three",
        selected_ref="refs/heads/feature/release",
        target=attempt.execution.target,
        actor="hcoona",
        workflow_run_id=attempt.workflow_run_id,
    )


def _governance(
    *,
    live_enabled: bool = True,
    blob_oid: str = "b" * 40,
) -> GovernanceObservation:
    now = datetime(2026, 8, 13, 15, 59, tzinfo=UTC)
    evidence = (
        NativeEvidence(
            endpoint="GET /repos/hcoona/three/environments/approval",
            captured_at=now,
            response_digest="sha256:" + ("5" * 64),
        ),
    )
    activation = EnabledGovernanceActivation(
        approval_environment=ApprovalEnvironmentAttestation(
            name="workflow-delivery-v3-buddy-approval",
            environment_id=20895030723,
            required_reviewers=(
                ApprovalEnvironmentReviewer(
                    login="hcoona",
                    reviewer_id=712433,
                ),
            ),
            prevent_self_review=False,
            can_admins_bypass=False,
            wait_timer_minutes=0,
            deployment_policy="all",
            secret_count=0,
            variables=(
                ApprovalEnvironmentVariable(
                    name="WDV3_APPROVAL_ENVIRONMENT_MARKER",
                    value="workflow-delivery-v3-buddy-approval/v1",
                    scope="environment",
                ),
            ),
            same_name_repository_variable_absent=True,
            same_name_organization_variable="",
            evidence=evidence,
        ),
        artifact_retention=ArtifactRetentionAttestation(
            endpoint=(
                "GET /repos/hcoona/three/actions/permissions/"
                "artifact-and-log-retention"
            ),
            captured_at=now,
            days=90,
            response_digest="sha256:" + ("6" * 64),
        ),
        destination_primitive=DestinationPrimitiveAttestation(
            primitive_id="test/conditional-version-and-tag-v1",
            operation=CONDITIONAL_NPM_VERSION_AND_TAG_OPERATION,
            captured_at=now,
            race_inputs=(("coordinate", "@hcoona/test"),),
            race_results=(("result", "non-overwrite"),),
            evidence_digest="sha256:" + ("7" * 64),
        ),
    )
    attestation = GovernanceAttestation(
        release_policy="hcoona-release-smoke-npm",
        package="@hcoona/hcoona-release-smoke-npm",
        issuer="hcoona",
        inspected_at=now - timedelta(days=1),
        expires_at=now + timedelta(days=30),
        accepted_writers=(WriterInventoryEntry(login="hcoona", role="Admin"),),
        accepted_publisher="hcoona",
        access_inventory=AccessInventory(
            repository=(AccessGrant(subject="hcoona", access="Admin"),),
            package=(AccessGrant(subject="hcoona", access="write"),),
            manage_actions=(AccessGrant(subject="hcoona", access="write"),),
        ),
        package_principal=PackagePrincipalAttestation(
            repository="hcoona/three",
            intended_coordinate="@hcoona/hcoona-release-smoke-npm",
            known_wider_reach=("@hcoona/hexo-renderer-asciidoc",),
        ),
        limitations=("Test-only complete Governance.",),
        activation=activation,
        live_enabled=live_enabled,
    )
    return GovernanceObservation(
        source=GovernanceSource(
            repository=GOVERNANCE_REPOSITORY,
            ref=GOVERNANCE_REF,
            path=GOVERNANCE_PATH,
            max_age_days=GOVERNANCE_MAX_AGE_DAYS,
        ),
        eligibility_main_sha=TARGET,
        current_main_sha="c" * 40,
        object_format="sha1",
        blob_oid=blob_oid,
        canonical_content_digest=attestation.content_digest,
        observed_at=now,
        attestation=attestation,
    )


def _closure_details(scenario, *, with_action: bool):
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
    snapshot = replace(
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
    decision = replace(
        scenario.decision,
        subject=attempt,
        qualification_snapshot_digest=snapshot.snapshot_digest,
        admitted_artifact_digests=(artifact.artifact_digest,),
    )
    actions: tuple[PublicationAction, ...] = ()
    if with_action:
        actions = (
            form_publication_action(
                destination_operation_profile=(
                    github_packages.github_packages_destination_operation_profile()
                ),
                projection=projection,
                artifact=artifact,
            ),
        )
    publication = PublicationSnapshot(
        attempt=attempt,
        qualification_snapshot_digest=snapshot.snapshot_digest,
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
    governance = _governance()
    intent = _intent(attempt)
    binding = ReleaseAttemptBinding(
        intent_digest=intent.intent_digest,
        request_id=intent.request_id,
        execution=attempt.execution,
        attempt=attempt,
        repository_model_digest="sha256:" + ("2" * 64),
        live_eligibility_artifact_id=709,
        live_eligibility_artifact_digest="sha256:" + ("3" * 64),
        live_eligibility_payload_digest="sha256:" + ("4" * 64),
        attestation_provenance=governance_observation_provenance(governance),
    )
    return (
        attempt,
        binding,
        decision,
        publication,
        projection,
        artifact,
        snapshot,
    )


def _closure(scenario, *, with_action: bool):
    return _closure_details(scenario, with_action=with_action)[:4]


def _bundle(
    attempt_binding: ReleaseAttemptBinding,
    qualification_decision,
    publication: PublicationSnapshot,
    reviewer_summary_reference: ArtifactReference | None = None,
) -> ApprovalBundle:
    return _require_api("form_approval_bundle")(
        intent=_intent(publication.attempt),
        attempt_binding=attempt_binding,
        qualification_decision=qualification_decision,
        publication_snapshot=publication,
        publication_snapshot_reference=_snapshot_reference(publication),
        reviewer_summary_reference=(
            reviewer_summary_reference or _reviewer_reference()
        ),
        control=_control(publication.attempt),
    )


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
        payload_digest=SUMMARY_DIGEST,
    )


def _bundle_reference(bundle: ApprovalBundle) -> ArtifactReference:
    return ArtifactReference(
        artifact_id=712,
        artifact_digest="sha256:" + ("6" * 64),
        artifact_url="https://example.test/artifacts/712",
        payload_path="approval-bundle.json",
        payload_digest=bundle.bundle_digest,
    )


def _authorization(
    bundle: ApprovalBundle,
    governance: GovernanceObservation | None = None,
) -> PublicationAuthorization:
    return _require_api("form_publication_authorization")(
        approval_bundle=bundle,
        approval_bundle_reference=_bundle_reference(bundle),
        approval_boundary_sentinel_result="success",
        governance=governance or _governance(),
        completed_at="2026-08-13T16:00:00Z",
        control=bundle.control,
    )


def _proof(
    publication: PublicationSnapshot,
    governance: GovernanceObservation | None = None,
) -> ExactSatisfiedGovernanceProof:
    return _require_api("form_exact_satisfied_governance_proof")(
        publication_snapshot=publication,
        governance=governance or _governance(),
        proved_at="2026-08-13T16:00:00Z",
        control=_control(publication.attempt),
    )


def _successful_action_result(
    publication: PublicationSnapshot,
    projection,
    artifact,
) -> ActionResult:
    action = publication.materialized_actions[0]
    assert artifact.content.content_sha512 is not None
    receipt = Receipt(
        attempt=publication.attempt,
        publication_snapshot_digest=publication.snapshot_digest,
        action_id=action.action_id,
        action_digest=action.action_digest,
        coordinate=projection.coordinate,
        mutable_resource_keys=action.mutable_resource_keys,
        lock_group=action.serialization_projection,
        artifact_transport=artifact.transport,
        artifact_content_sha256=artifact.content.content_sha256,
        artifact_content_sha512=artifact.content.content_sha512,
        witness_digest=artifact.witness_digest,
        creation_result="created",
        tag_mapping=((action.tag, action.version),),
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
        lock_group=action.serialization_projection,
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


def test_exact_noop_requires_fresh_proof_and_no_mutation_authority(
    qualified_simulation,
) -> None:
    attempt, _binding, decision, publication = _closure(
        qualified_simulation,
        with_action=False,
    )
    proof = _proof(publication)

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
    attempt, _binding, decision, publication = _closure(
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
        exact_satisfied_governance_proof=None,
        approval_bundle=None,
        publication_authorization=None,
        action_results=(),
    )

    assert isinstance(outcome, AttemptOutcome)
    assert outcome.terminal_phase == "qualification"
    assert outcome.result == terminal_result
    assert outcome.uncertainty is uncertainty
    assert outcome.next_action == next_action

    with pytest.raises(
        ValueError,
        match="Publication binding mismatch",
    ):
        finalize_attempt_outcome(
            attempt=attempt,
            qualification_decision=unsuccessful,
            publication_snapshot=publication,
            exact_satisfied_governance_proof=None,
            approval_bundle=None,
            publication_authorization=None,
            action_results=(),
        )


def test_publication_preparation_interruption_has_no_downstream_lineage(
    qualified_simulation,
) -> None:
    (
        attempt,
        binding,
        decision,
        publication,
        projection,
        artifact,
        _qualification_snapshot,
    ) = _closure_details(
        qualified_simulation,
        with_action=True,
    )
    bundle = _bundle(binding, decision, publication)
    authorization = _authorization(bundle)
    result = _successful_action_result(publication, projection, artifact)

    outcome = finalize_attempt_outcome(
        attempt=attempt,
        qualification_decision=decision,
        publication_snapshot=None,
        exact_satisfied_governance_proof=None,
        approval_bundle=None,
        publication_authorization=None,
        action_results=(),
        publication_preparation_interrupted=True,
    )

    assert outcome.terminal_phase == "publication-preparation"
    assert outcome.result == "incomplete"
    assert outcome.next_action == "new-attempt"

    for downstream in (
        {"publication_snapshot": publication},
        {"approval_bundle": bundle},
        {"publication_authorization": authorization},
        {"action_results": (result,)},
        {"platform_terminated": True},
        {"publication_may_have_started": True},
    ):
        arguments: dict[str, object] = {
            "attempt": attempt,
            "qualification_decision": decision,
            "publication_snapshot": None,
            "exact_satisfied_governance_proof": None,
            "approval_bundle": None,
            "publication_authorization": None,
            "action_results": (),
            "publication_preparation_interrupted": True,
        }
        arguments.update(downstream)
        with pytest.raises(
            ValueError,
            match=r"(mismatch|interruption is contradictory)",
        ):
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
    (
        attempt,
        _binding,
        decision,
        _publication,
        projection,
        _artifact,
        _qualification_snapshot,
    ) = _closure_details(
        qualified_simulation,
        with_action=True,
    )
    observation = _blocking_observation(
        attempt=attempt,
        qualification_decision=decision,
        projection=projection,
        classification=classification,
    )

    outcome = finalize_attempt_outcome(
        attempt=attempt,
        qualification_decision=decision,
        publication_snapshot=None,
        exact_satisfied_governance_proof=None,
        approval_bundle=None,
        publication_authorization=None,
        action_results=(),
        observations=(observation,),
        publication_preparation_interrupted=True,
    )

    assert outcome.observation_digests == (observation.observation_digest,)
    assert outcome.terminal_phase == "observation"
    assert outcome.result == result
    assert outcome.uncertainty is uncertainty
    assert outcome.next_action == "reconcile"


def test_approval_bundle_closes_one_action_before_wait(
    qualified_simulation,
) -> None:
    attempt, binding, decision, publication = _closure(
        qualified_simulation,
        with_action=True,
    )
    reviewer = _reviewer_reference()

    bundle = _bundle(binding, decision, publication, reviewer)

    assert bundle.attempt == attempt
    assert bundle.publication_snapshot_reference == _snapshot_reference(
        publication
    )
    assert bundle.reviewer_summary_reference == reviewer
    assert decision.admitted_artifact_digests == publication.artifact_digests
    assert len(publication.artifact_digests) == 1
    assert {
        "approval-result",
        "completed-at",
        "approver",
        "environment",
        "approval-job",
        "publication-snapshot",
        "qualification-decision",
    }.isdisjoint(bundle.to_document())


def test_approval_admission_rejects_substituted_action_profile_digest(
    qualified_simulation,
) -> None:
    (
        attempt,
        binding,
        decision,
        original_publication,
        _projection,
        artifact,
        qualification_snapshot,
    ) = _closure_details(
        qualified_simulation,
        with_action=True,
    )
    action = replace(
        original_publication.materialized_actions[0],
        destination_operation_profile_digest="sha256:" + ("0" * 64),
    )
    publication = replace(
        original_publication,
        materialized_actions=(action,),
    )
    bundle = _bundle(binding, decision, publication)

    with pytest.raises(
        ValueError,
        match=r"^Publication Action is not an exact profile instantiation$",
    ):
        live.validate_approval_bundle_closure(
            approval_bundle=bundle,
            intent=_intent(attempt),
            attempt_binding=binding,
            qualification_decision=decision,
            qualification_snapshot=qualification_snapshot,
            release_artifact=artifact,
            destination_operation_profile=(
                github_packages.github_packages_destination_operation_profile()
            ),
            publication_snapshot=publication,
            publication_snapshot_reference=_snapshot_reference(publication),
            reviewer_summary_reference=bundle.reviewer_summary_reference,
            control=bundle.control,
        )


@pytest.mark.parametrize(
    "substitution",
    ["qualification-snapshot", "release-artifact"],
)
def test_approval_admission_rejects_substituted_qualification_context(
    qualified_simulation,
    substitution: str,
) -> None:
    (
        attempt,
        binding,
        decision,
        publication,
        _projection,
        artifact,
        qualification_snapshot,
    ) = _closure_details(
        qualified_simulation,
        with_action=True,
    )
    bundle = _bundle(binding, decision, publication)
    if substitution == "qualification-snapshot":
        qualification_snapshot = replace(
            qualification_snapshot,
            subject=replace(
                attempt,
                workflow_run_id=attempt.workflow_run_id + 1,
            ),
        )
    else:
        artifact = replace(
            artifact,
            entries=tuple(sorted((*artifact.entries, "substituted-entry"))),
        )

    with pytest.raises(
        ValueError,
        match=r"^Approval Bundle action qualification context mismatch$",
    ):
        live.validate_approval_bundle_closure(
            approval_bundle=bundle,
            intent=_intent(attempt),
            attempt_binding=binding,
            qualification_decision=decision,
            qualification_snapshot=qualification_snapshot,
            release_artifact=artifact,
            destination_operation_profile=(
                github_packages.github_packages_destination_operation_profile()
            ),
            publication_snapshot=publication,
            publication_snapshot_reference=_snapshot_reference(publication),
            reviewer_summary_reference=bundle.reviewer_summary_reference,
            control=bundle.control,
        )


def test_publication_authorization_binds_bundle_and_fresh_governance(
    qualified_simulation,
) -> None:
    attempt, binding, decision, publication = _closure(
        qualified_simulation,
        with_action=True,
    )
    bundle = _bundle(binding, decision, publication)
    governance = _governance()

    authorization = _authorization(bundle, governance)

    assert authorization.attempt == attempt
    assert authorization.approval_bundle_reference == _bundle_reference(bundle)
    assert authorization.governance_proof.provenance == (
        governance_observation_provenance(governance)
    )
    assert authorization.governance_proof.current_main_sha == (
        governance.current_main_sha
    )
    assert authorization.approval_boundary.to_document() == {
        "environment": "workflow-delivery-v3-buddy-approval",
        "job": "approve-publication",
        "sentinel-name": "WDV3_APPROVAL_ENVIRONMENT_MARKER",
        "sentinel-value": "workflow-delivery-v3-buddy-approval/v1",
        "sentinel-result": "success",
    }


def test_publication_authorization_rejects_substituted_freshness(
    qualified_simulation,
) -> None:
    _attempt, binding, decision, publication = _closure(
        qualified_simulation,
        with_action=True,
    )
    bundle = _bundle(binding, decision, publication)

    with pytest.raises(ValueError, match="control target binding mismatch"):
        _require_api("form_publication_authorization")(
            approval_bundle=bundle,
            approval_bundle_reference=_bundle_reference(bundle),
            approval_boundary_sentinel_result="success",
            governance=_governance(),
            completed_at="2026-08-13T16:00:00Z",
            control=f"workflow-delivery-v3:{'0' * 40}",
        )

    with pytest.raises(ValueError, match="Bundle reference mismatch"):
        _require_api("form_publication_authorization")(
            approval_bundle=bundle,
            approval_bundle_reference=replace(
                _bundle_reference(bundle),
                payload_digest="sha256:" + ("d" * 64),
            ),
            approval_boundary_sentinel_result="success",
            governance=_governance(),
            completed_at="2026-08-13T16:00:00Z",
            control=bundle.control,
        )


def test_exact_satisfied_proof_rejects_action_bearing_snapshot(
    qualified_simulation,
) -> None:
    _attempt, _binding, _decision, publication = _closure(
        qualified_simulation,
        with_action=True,
    )

    with pytest.raises(ValueError, match="actionless exact"):
        _proof(publication)


def test_action_bearing_missing_authority_is_incomplete(
    qualified_simulation,
) -> None:
    (
        attempt,
        _binding,
        decision,
        publication,
        projection,
        artifact,
        _qualification_snapshot,
    ) = _closure_details(
        qualified_simulation,
        with_action=True,
    )

    outcome = finalize_attempt_outcome(
        attempt=attempt,
        qualification_decision=decision,
        publication_snapshot=publication,
        exact_satisfied_governance_proof=None,
        approval_bundle=None,
        publication_authorization=None,
        action_results=(),
    )

    assert outcome.result == "incomplete"
    assert outcome.terminal_phase == "approval-contract"
    assert outcome.uncertainty is True
    assert outcome.possibly_mutated is False
    assert outcome.next_action == "new-attempt"

    with pytest.raises(
        ValueError,
        match="cannot precede complete authorization",
    ):
        finalize_attempt_outcome(
            attempt=attempt,
            qualification_decision=decision,
            publication_snapshot=publication,
            exact_satisfied_governance_proof=None,
            approval_bundle=None,
            publication_authorization=None,
            action_results=(
                _successful_action_result(publication, projection, artifact),
            ),
        )


def test_action_bearing_bundle_without_authorization_is_incomplete(
    qualified_simulation,
) -> None:
    attempt, binding, decision, publication = _closure(
        qualified_simulation,
        with_action=True,
    )
    bundle = _bundle(binding, decision, publication)

    outcome = finalize_attempt_outcome(
        attempt=attempt,
        qualification_decision=decision,
        publication_snapshot=publication,
        exact_satisfied_governance_proof=None,
        approval_bundle=bundle,
        publication_authorization=None,
        action_results=(),
        publication_snapshot_reference=_snapshot_reference(publication),
        approval_bundle_reference=_bundle_reference(bundle),
    )

    assert outcome.result == "incomplete"
    assert outcome.terminal_phase == "approval-contract"
    assert outcome.approval_bundle_digest == bundle.bundle_digest
    assert outcome.publication_authorization_digest is None
    assert outcome.uncertainty is True
    assert outcome.possibly_mutated is False
    assert outcome.next_action == "new-attempt"


def test_live_finalizer_consumes_direct_action_result_with_embedded_receipt(
    qualified_simulation,
) -> None:
    (
        attempt,
        binding,
        decision,
        publication,
        projection,
        artifact,
        qualification_snapshot,
    ) = _closure_details(
        qualified_simulation,
        with_action=True,
    )
    bundle = _bundle(binding, decision, publication)
    authorization = _authorization(bundle)
    result = _successful_action_result(publication, projection, artifact)

    outcome = finalize_attempt_outcome(
        attempt=attempt,
        qualification_decision=decision,
        publication_snapshot=publication,
        exact_satisfied_governance_proof=None,
        approval_bundle=bundle,
        publication_authorization=authorization,
        action_results=(result,),
        qualification_snapshot=qualification_snapshot,
        release_artifact=artifact,
        destination_operation_profile=(
            github_packages.github_packages_destination_operation_profile()
        ),
        publication_snapshot_reference=_snapshot_reference(publication),
        approval_bundle_reference=_bundle_reference(bundle),
    )

    assert outcome.result == "success"
    assert outcome.approval_bundle_digest == bundle.bundle_digest
    assert (
        outcome.publication_authorization_digest
        == authorization.authorization_digest
    )
    assert outcome.action_result_digests == (result.result_digest,)


def test_live_finalizer_recomputes_action_result_and_receipt_bindings(
    qualified_simulation,
) -> None:
    (
        attempt,
        binding,
        decision,
        publication,
        projection,
        artifact,
        qualification_snapshot,
    ) = _closure_details(
        qualified_simulation,
        with_action=True,
    )
    bundle = _bundle(binding, decision, publication)
    authorization = _authorization(bundle)
    result = _successful_action_result(publication, projection, artifact)
    assert result.receipt is not None

    substituted_bundle = replace(
        bundle,
        reviewer_summary_reference=replace(
            bundle.reviewer_summary_reference,
            payload_digest="sha256:" + ("7" * 64),
        ),
    )
    substituted_authorization = replace(
        authorization,
        approval_bundle_reference=_bundle_reference(substituted_bundle),
    )
    with pytest.raises(
        ValueError,
        match="Publication Authorization mismatch",
    ):
        finalize_attempt_outcome(
            attempt=attempt,
            qualification_decision=decision,
            publication_snapshot=publication,
            exact_satisfied_governance_proof=None,
            approval_bundle=bundle,
            publication_authorization=substituted_authorization,
            action_results=(result,),
            qualification_snapshot=qualification_snapshot,
            release_artifact=artifact,
            destination_operation_profile=(
                github_packages.github_packages_destination_operation_profile()
            ),
            publication_snapshot_reference=_snapshot_reference(publication),
            approval_bundle_reference=_bundle_reference(bundle),
        )

    with pytest.raises(
        ValueError,
        match="Publication Authorization mismatch",
    ):
        finalize_attempt_outcome(
            attempt=attempt,
            qualification_decision=decision,
            publication_snapshot=publication,
            exact_satisfied_governance_proof=None,
            approval_bundle=bundle,
            publication_authorization=replace(
                authorization,
                approval_bundle_reference=replace(
                    authorization.approval_bundle_reference,
                    artifact_id=999,
                ),
            ),
            action_results=(result,),
            qualification_snapshot=qualification_snapshot,
            release_artifact=artifact,
            destination_operation_profile=(
                github_packages.github_packages_destination_operation_profile()
            ),
            publication_snapshot_reference=_snapshot_reference(publication),
            approval_bundle_reference=_bundle_reference(bundle),
        )

    with pytest.raises(ValueError, match="Approval Bundle mismatch"):
        finalize_attempt_outcome(
            attempt=attempt,
            qualification_decision=decision,
            publication_snapshot=publication,
            exact_satisfied_governance_proof=None,
            approval_bundle=replace(
                bundle,
                publication_snapshot_reference=replace(
                    bundle.publication_snapshot_reference,
                    artifact_id=999,
                ),
            ),
            publication_authorization=authorization,
            action_results=(result,),
            qualification_snapshot=qualification_snapshot,
            release_artifact=artifact,
            destination_operation_profile=(
                github_packages.github_packages_destination_operation_profile()
            ),
            publication_snapshot_reference=_snapshot_reference(publication),
            approval_bundle_reference=_bundle_reference(bundle),
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
            exact_satisfied_governance_proof=None,
            approval_bundle=bundle,
            publication_authorization=authorization,
            action_results=(substituted_result,),
            qualification_snapshot=qualification_snapshot,
            release_artifact=artifact,
            destination_operation_profile=(
                github_packages.github_packages_destination_operation_profile()
            ),
            publication_snapshot_reference=_snapshot_reference(publication),
            approval_bundle_reference=_bundle_reference(bundle),
        )

    substituted_action_id = "publish-substituted"
    substituted_receipt = replace(
        result.receipt,
        action_id=substituted_action_id,
    )
    substituted_result = replace(
        result,
        action_id=substituted_action_id,
        receipt=substituted_receipt,
    )
    with pytest.raises(ValueError, match="Action Result binding mismatch"):
        finalize_attempt_outcome(
            attempt=attempt,
            qualification_decision=decision,
            publication_snapshot=publication,
            exact_satisfied_governance_proof=None,
            approval_bundle=bundle,
            publication_authorization=authorization,
            action_results=(substituted_result,),
            qualification_snapshot=qualification_snapshot,
            release_artifact=artifact,
            destination_operation_profile=(
                github_packages.github_packages_destination_operation_profile()
            ),
            publication_snapshot_reference=_snapshot_reference(publication),
            approval_bundle_reference=_bundle_reference(bundle),
        )


@pytest.mark.parametrize(
    "mutate",
    [
        pytest.param(
            lambda receipt: replace(
                receipt,
                artifact_transport=replace(
                    receipt.artifact_transport,
                    artifact_id=999,
                ),
            ),
            id="artifact-id",
        ),
        pytest.param(
            lambda receipt: replace(
                receipt,
                coordinate=replace(receipt.coordinate, channel="official"),
            ),
            id="coordinate-channel",
        ),
        pytest.param(
            lambda receipt: replace(
                receipt,
                coordinate=replace(
                    receipt.coordinate,
                    destination_id="npm/substituted",
                ),
            ),
            id="coordinate-destination",
        ),
        pytest.param(
            lambda receipt: replace(
                receipt,
                artifact_transport=replace(
                    receipt.artifact_transport,
                    artifact_name="substituted.tgz",
                ),
            ),
            id="transport-name",
        ),
        pytest.param(
            lambda receipt: replace(
                receipt,
                artifact_transport=replace(
                    receipt.artifact_transport,
                    producer="substituted-producer",
                ),
            ),
            id="transport-producer",
        ),
        pytest.param(
            lambda receipt: replace(
                receipt,
                mutable_resource_keys=(
                    "external-package-coordinate:" + ("0" * 64),
                    "npm-dist-tag:" + ("1" * 64),
                ),
            ),
            id="mutable-resource-keys",
        ),
        pytest.param(
            lambda receipt: replace(
                receipt,
                artifact_content_sha256="sha256:" + ("0" * 64),
            ),
            id="content-sha256",
        ),
        pytest.param(
            lambda receipt: replace(
                receipt,
                artifact_content_sha512="sha512:" + ("0" * 128),
            ),
            id="content-sha512",
        ),
        pytest.param(
            lambda receipt: replace(
                receipt,
                witness_digest="sha256:" + ("0" * 64),
            ),
            id="witness-digest",
        ),
    ],
)
def test_live_finalizer_rejects_substituted_receipt_ancestor_fact(
    qualified_simulation,
    mutate: Callable[[Receipt], Receipt],
) -> None:
    (
        attempt,
        binding,
        decision,
        publication,
        projection,
        artifact,
        qualification_snapshot,
    ) = _closure_details(
        qualified_simulation,
        with_action=True,
    )
    bundle = _bundle(binding, decision, publication)
    authorization = _authorization(bundle)
    result = _successful_action_result(publication, projection, artifact)
    assert result.receipt is not None
    substituted_receipt = mutate(result.receipt)

    with pytest.raises(ValueError, match="Receipt binding mismatch"):
        finalize_attempt_outcome(
            attempt=attempt,
            qualification_decision=decision,
            publication_snapshot=publication,
            exact_satisfied_governance_proof=None,
            approval_bundle=bundle,
            publication_authorization=authorization,
            action_results=(replace(result, receipt=substituted_receipt),),
            qualification_snapshot=qualification_snapshot,
            release_artifact=artifact,
            destination_operation_profile=(
                github_packages.github_packages_destination_operation_profile()
            ),
            publication_snapshot_reference=_snapshot_reference(publication),
            approval_bundle_reference=_bundle_reference(bundle),
        )


@pytest.mark.parametrize(
    "missing",
    ["qualification-snapshot", "release-artifact"],
)
def test_live_finalizer_requires_each_context_for_success_receipt(
    qualified_simulation,
    missing: str,
) -> None:
    (
        attempt,
        binding,
        decision,
        publication,
        projection,
        artifact,
        qualification_snapshot,
    ) = _closure_details(
        qualified_simulation,
        with_action=True,
    )
    bundle = _bundle(binding, decision, publication)
    authorization = _authorization(bundle)
    result = _successful_action_result(publication, projection, artifact)

    with pytest.raises(
        TypeError,
        match="Receipt requires exact qualification context",
    ):
        finalize_attempt_outcome(
            attempt=attempt,
            qualification_decision=decision,
            publication_snapshot=publication,
            exact_satisfied_governance_proof=None,
            approval_bundle=bundle,
            publication_authorization=authorization,
            action_results=(result,),
            qualification_snapshot=(
                None
                if missing == "qualification-snapshot"
                else qualification_snapshot
            ),
            release_artifact=(
                None if missing == "release-artifact" else artifact
            ),
            destination_operation_profile=(
                github_packages.github_packages_destination_operation_profile()
            ),
            publication_snapshot_reference=_snapshot_reference(publication),
            approval_bundle_reference=_bundle_reference(bundle),
        )


def test_live_finalizer_requires_profile_for_success_receipt(
    qualified_simulation,
) -> None:
    (
        attempt,
        binding,
        decision,
        publication,
        projection,
        artifact,
        qualification_snapshot,
    ) = _closure_details(
        qualified_simulation,
        with_action=True,
    )
    bundle = _bundle(binding, decision, publication)
    authorization = _authorization(bundle)
    result = _successful_action_result(publication, projection, artifact)

    with pytest.raises(
        TypeError,
        match="Receipt requires an exact Destination Operation Profile",
    ):
        finalize_attempt_outcome(
            attempt=attempt,
            qualification_decision=decision,
            publication_snapshot=publication,
            exact_satisfied_governance_proof=None,
            approval_bundle=bundle,
            publication_authorization=authorization,
            action_results=(result,),
            qualification_snapshot=qualification_snapshot,
            release_artifact=artifact,
            publication_snapshot_reference=_snapshot_reference(publication),
            approval_bundle_reference=_bundle_reference(bundle),
        )


def test_live_finalizer_rejects_substituted_action_profile_digest(
    qualified_simulation,
) -> None:
    (
        attempt,
        binding,
        decision,
        original_publication,
        projection,
        artifact,
        qualification_snapshot,
    ) = _closure_details(
        qualified_simulation,
        with_action=True,
    )
    original_action = original_publication.materialized_actions[0]
    action = replace(
        original_action,
        destination_operation_profile_digest="sha256:" + ("0" * 64),
    )
    publication = replace(
        original_publication,
        materialized_actions=(action,),
    )
    bundle = _bundle(binding, decision, publication)
    authorization = _authorization(bundle)
    result = _successful_action_result(publication, projection, artifact)

    with pytest.raises(
        ValueError,
        match="Publication Action is not an exact profile instantiation",
    ):
        finalize_attempt_outcome(
            attempt=attempt,
            qualification_decision=decision,
            publication_snapshot=publication,
            exact_satisfied_governance_proof=None,
            approval_bundle=bundle,
            publication_authorization=authorization,
            action_results=(result,),
            qualification_snapshot=qualification_snapshot,
            release_artifact=artifact,
            destination_operation_profile=(
                github_packages.github_packages_destination_operation_profile()
            ),
            publication_snapshot_reference=_snapshot_reference(publication),
            approval_bundle_reference=_bundle_reference(bundle),
        )


@pytest.mark.parametrize(
    "authority",
    ["approval-bundle", "publication-authorization"],
)
def test_live_finalizer_rejects_substituted_authority_attempt(
    qualified_simulation,
    authority: str,
) -> None:
    attempt, binding, decision, publication = _closure(
        qualified_simulation,
        with_action=True,
    )
    bundle = _bundle(binding, decision, publication)
    authorization = _authorization(bundle)
    substituted_attempt = replace(
        attempt,
        execution=replace(
            attempt.execution,
            release_unit="substituted-release-unit",
        ),
    )
    if authority == "approval-bundle":
        bundle = replace(bundle, attempt=substituted_attempt)
        authorization = replace(
            authorization,
            approval_bundle_reference=_bundle_reference(bundle),
        )
        expected_error = r"^Live finalization Approval Bundle mismatch$"
    else:
        authorization = replace(
            authorization,
            attempt=substituted_attempt,
        )
        expected_error = (
            r"^Live finalization Publication Authorization mismatch$"
        )

    with pytest.raises(ValueError, match=expected_error):
        finalize_attempt_outcome(
            attempt=attempt,
            qualification_decision=decision,
            publication_snapshot=publication,
            exact_satisfied_governance_proof=None,
            approval_bundle=bundle,
            publication_authorization=authorization,
            action_results=(),
            publication_snapshot_reference=_snapshot_reference(publication),
            approval_bundle_reference=_bundle_reference(bundle),
        )


@pytest.mark.parametrize(
    "authority",
    ["approval-bundle", "publication-authorization"],
)
def test_publisher_rejects_substituted_authority_attempt(
    qualified_simulation,
    authority: str,
) -> None:
    (
        attempt,
        binding,
        decision,
        publication,
        projection,
        artifact,
        _qualification_snapshot,
    ) = _closure_details(
        qualified_simulation,
        with_action=True,
    )
    bundle = _bundle(binding, decision, publication)
    authorization = _authorization(bundle)
    action = publication.materialized_actions[0]
    potential_action = replace(
        qualified_simulation.snapshot.potential_actions[0],
        contract_id=projection.potential_action_id,
        projection_id=projection.projection_id,
        operation=projection.operation,
        output=projection.output,
        capability_requirements=publication_capability_requirements(projection),
        mutable_resource_key_basis=publication_mutable_resource_key_basis(
            projection
        ),
    )
    qualification_snapshot = replace(
        qualified_simulation.snapshot,
        subject=attempt,
        channel="buddy",
        destination_projections=(projection,),
        potential_actions=(potential_action,),
    )

    def validate(
        candidate_bundle: ApprovalBundle,
        candidate_authorization: PublicationAuthorization,
    ) -> None:
        github_packages._validate_publish_preconditions(  # noqa: SLF001
            publication_snapshot=publication,
            approval_bundle=candidate_bundle,
            reviewer_summary_reference=bundle.reviewer_summary_reference,
            authorization=candidate_authorization,
            action=action,
            qualification_snapshot=qualification_snapshot,
            qualification_decision=decision,
            artifact=artifact,
            expectation=qualified_simulation.expectation,
        )

    assert validate(bundle, authorization) is None

    substituted_attempt = replace(
        attempt,
        execution=replace(
            attempt.execution,
            release_unit="substituted-release-unit",
        ),
    )
    if authority == "approval-bundle":
        substituted_bundle = replace(bundle, attempt=substituted_attempt)
        substituted_authorization = replace(
            authorization,
            approval_bundle_reference=replace(
                authorization.approval_bundle_reference,
                payload_digest=substituted_bundle.bundle_digest,
            ),
        )
    else:
        substituted_bundle = bundle
        substituted_authorization = replace(
            authorization,
            attempt=substituted_attempt,
        )

    with pytest.raises(
        ValueError,
        match=r"^publication precondition binding mismatch$",
    ):
        validate(substituted_bundle, substituted_authorization)


def test_missing_action_result_is_incomplete_after_authorization(
    qualified_simulation,
) -> None:
    attempt, binding, decision, publication = _closure(
        qualified_simulation,
        with_action=True,
    )
    bundle = _bundle(binding, decision, publication)
    authorization = _authorization(bundle)

    outcome = finalize_attempt_outcome(
        attempt=attempt,
        qualification_decision=decision,
        publication_snapshot=publication,
        exact_satisfied_governance_proof=None,
        approval_bundle=bundle,
        publication_authorization=authorization,
        action_results=(),
        publication_snapshot_reference=_snapshot_reference(publication),
        approval_bundle_reference=_bundle_reference(bundle),
    )

    assert outcome.result == "incomplete"
    assert outcome.terminal_phase == "publication-result-missing"
    assert outcome.uncertainty is True
    assert outcome.possibly_mutated is False
    assert outcome.next_action == "new-attempt"


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
    ("started", "result", "phase"),
    [
        (
            False,
            "incomplete",
            "pre-publication-termination",
        ),
        (
            True,
            "incomplete-possibly-mutated",
            "post-publication-termination",
        ),
    ],
)
def test_platform_termination_maps_by_publication_phase(
    qualified_simulation,
    started: bool,
    result: str,
    phase: str,
) -> None:
    attempt, binding, decision, publication = _closure(
        qualified_simulation,
        with_action=True,
    )
    bundle = _bundle(binding, decision, publication)
    authorization = _authorization(bundle)

    outcome = finalize_attempt_outcome(
        attempt=attempt,
        qualification_decision=decision,
        publication_snapshot=publication,
        exact_satisfied_governance_proof=None,
        approval_bundle=bundle,
        publication_authorization=authorization,
        action_results=(),
        publication_snapshot_reference=_snapshot_reference(publication),
        approval_bundle_reference=_bundle_reference(bundle),
        platform_terminated=True,
        publication_may_have_started=started,
    )

    assert (outcome.result, outcome.terminal_phase) == (
        result,
        phase,
    )
    assert outcome.next_action == "new-attempt"
    assert outcome.possibly_mutated is started


def test_platform_termination_rejects_multiple_direct_results(
    qualified_simulation,
) -> None:
    (
        attempt,
        binding,
        decision,
        publication,
        projection,
        artifact,
        _qualification_snapshot,
    ) = _closure_details(
        qualified_simulation,
        with_action=True,
    )
    bundle = _bundle(binding, decision, publication)
    authorization = _authorization(bundle)
    result = _successful_action_result(publication, projection, artifact)
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

    with pytest.raises(ValueError, match="one Action Result"):
        finalize_attempt_outcome(
            attempt=attempt,
            qualification_decision=decision,
            publication_snapshot=publication,
            exact_satisfied_governance_proof=None,
            approval_bundle=bundle,
            publication_authorization=authorization,
            action_results=(result, second_result),
            publication_snapshot_reference=_snapshot_reference(publication),
            approval_bundle_reference=_bundle_reference(bundle),
            platform_terminated=True,
        )


def test_action_bearing_publication_rejects_exact_satisfied_proof(
    qualified_simulation,
) -> None:
    attempt, binding, decision, publication = _closure(
        qualified_simulation,
        with_action=True,
    )
    bundle = _bundle(binding, decision, publication)
    authorization = _authorization(bundle)
    _noop_attempt, _noop_binding, _noop_decision, noop = _closure(
        qualified_simulation,
        with_action=False,
    )
    proof = _proof(noop)

    with pytest.raises(ValueError, match="Governance proof mismatch"):
        finalize_attempt_outcome(
            attempt=attempt,
            qualification_decision=decision,
            publication_snapshot=publication,
            exact_satisfied_governance_proof=proof,
            approval_bundle=bundle,
            publication_authorization=authorization,
            action_results=(),
            publication_snapshot_reference=_snapshot_reference(publication),
            approval_bundle_reference=_bundle_reference(bundle),
            platform_terminated=True,
        )


def test_platform_termination_rejects_misbound_action_result(
    qualified_simulation,
) -> None:
    (
        attempt,
        binding,
        decision,
        publication,
        projection,
        artifact,
        _qualification_snapshot,
    ) = _closure_details(
        qualified_simulation,
        with_action=True,
    )
    bundle = _bundle(binding, decision, publication)
    authorization = _authorization(bundle)
    result = _successful_action_result(publication, projection, artifact)
    assert result.receipt is not None
    substituted_digest = "sha256:" + ("8" * 64)
    substituted_lock = "destination-package:substituted"
    candidates = (
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
                exact_satisfied_governance_proof=None,
                approval_bundle=bundle,
                publication_authorization=authorization,
                action_results=(candidate,),
                publication_snapshot_reference=_snapshot_reference(publication),
                approval_bundle_reference=_bundle_reference(bundle),
                platform_terminated=True,
            )


@pytest.mark.parametrize(
    "diagnostic",
    [
        "governance-recheck-failed-before-runner",
        "create-conflict",
    ],
)
def test_failed_no_side_effect_result_is_terminal_failure(
    qualified_simulation,
    diagnostic: str,
) -> None:
    attempt, binding, decision, publication = _closure(
        qualified_simulation,
        with_action=True,
    )
    bundle = _bundle(binding, decision, publication)
    authorization = _authorization(bundle)
    action = publication.materialized_actions[0]
    result = ActionResult(
        attempt=attempt,
        publication_snapshot_digest=publication.snapshot_digest,
        action_id=action.action_id,
        action_digest=action.action_digest,
        lock_group=action.serialization_projection,
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
        exact_satisfied_governance_proof=None,
        approval_bundle=bundle,
        publication_authorization=authorization,
        action_results=(result,),
        publication_snapshot_reference=_snapshot_reference(publication),
        approval_bundle_reference=_bundle_reference(bundle),
    )

    assert outcome.result == "failure"
    assert outcome.terminal_phase == "finalized"
    assert outcome.next_action == "new-attempt"
    assert outcome.uncertainty is False
    assert outcome.possibly_mutated is False


def test_mixed_attempt_action_result_is_rejected(
    qualified_simulation,
) -> None:
    (
        attempt,
        binding,
        decision,
        publication,
        projection,
        artifact,
        _qualification_snapshot,
    ) = _closure_details(
        qualified_simulation,
        with_action=True,
    )
    bundle = _bundle(binding, decision, publication)
    authorization = _authorization(bundle)
    result = _successful_action_result(publication, projection, artifact)
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
        match="Action Result mismatch",
    ):
        finalize_attempt_outcome(
            attempt=attempt,
            qualification_decision=decision,
            publication_snapshot=publication,
            exact_satisfied_governance_proof=None,
            approval_bundle=bundle,
            publication_authorization=authorization,
            action_results=(mixed_result,),
            publication_snapshot_reference=_snapshot_reference(publication),
            approval_bundle_reference=_bundle_reference(bundle),
        )


def test_publisher_preflight_rejects_substituted_action_profile_digest(
    qualified_simulation,
) -> None:
    (
        _attempt,
        binding,
        decision,
        original_publication,
        _projection,
        artifact,
        qualification_snapshot,
    ) = _closure_details(
        qualified_simulation,
        with_action=True,
    )
    original_action = original_publication.materialized_actions[0]
    action = replace(
        original_action,
        destination_operation_profile_digest="sha256:" + ("0" * 64),
    )
    publication = replace(
        original_publication,
        materialized_actions=(action,),
    )
    bundle = _bundle(binding, decision, publication)
    authorization = _authorization(bundle)

    assert (
        bundle.publication_snapshot_reference.payload_digest
        == publication.snapshot_digest
    )
    assert (
        authorization.approval_bundle_reference.payload_digest
        == bundle.bundle_digest
    )

    with pytest.raises(
        ValueError,
        match=r"^Publication Action is not an exact profile instantiation$",
    ):
        github_packages.preflight_github_packages_action(
            publication_snapshot=publication,
            approval_bundle=bundle,
            reviewer_summary_reference=bundle.reviewer_summary_reference,
            authorization=authorization,
            action=action,
            qualification_snapshot=qualification_snapshot,
            qualification_decision=decision,
            artifact=artifact,
            expectation=qualified_simulation.expectation,
        )
