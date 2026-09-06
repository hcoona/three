"""Current-DAG live authorization and finalization scenarios."""

from __future__ import annotations

# ruff: noqa: D103
from dataclasses import replace
from datetime import UTC, datetime, timedelta
from typing import Any

import pytest
from three_workflow_delivery_v3.adapters import github_packages
from three_workflow_delivery_v3.canonical import canonical_sha256
from three_workflow_delivery_v3.records.artifacts import ArtifactReference
from three_workflow_delivery_v3.records.release import (
    CONDITIONAL_NPM_VERSION_AND_TAG_OPERATION,
    ApprovalBundle,
    BuddyExecutionIdentity,
    ExactSatisfiedFinalizationProof,
    ExternalPackageCoordinate,
    GovernanceProof,
    PublicationAction,
    PublicationAuthorization,
    PublicationObservationReference,
    PublicationSnapshot,
    ReleaseAttemptBinding,
    ReleaseAttemptIdentity,
    form_publication_action,
    publication_capability_requirements,
    publication_mutable_resource_key_basis,
    release_artifact_transport_name,
)
from three_workflow_delivery_v3.release import live
from three_workflow_delivery_v3.release.eligibility import (
    EnabledGovernanceActivation,
    GovernanceObservation,
    governance_observation_provenance,
    parse_governance_attestation,
)
from three_workflow_delivery_v3.release.exact_satisfied import (
    validate_exact_satisfied_snapshot,
)
from three_workflow_delivery_v3.release.identity import (
    normalize_buddy_live_intent,
)
from three_workflow_delivery_v3.repository.descriptors import (
    GOVERNANCE_MAX_AGE_DAYS,
    GOVERNANCE_PATH,
    GOVERNANCE_REF,
    GOVERNANCE_REPOSITORY,
    GovernanceSource,
)

from .test_eligibility import (
    _admit_test_destination_primitive,
    _attestation_content,
    _ready_activation,
)
from .test_observation_admission import (
    _observation,
)
from .test_observation_admission import (
    observation_case as observation_case,  # noqa: PLC0414
)

TARGET = "a" * 40
SUMMARY_DIGEST = "sha256:" + ("5" * 64)
EXPECTED_LIVE_API = (
    "PublicRevisionCheckout",
    "fetch_exact_public_revision",
    "form_approval_bundle",
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
    attestation = parse_governance_attestation(
        _attestation_content(
            inspected_at=(now - timedelta(days=1)).strftime(
                "%Y-%m-%dT%H:%M:%SZ"
            ),
            expires_at=(now + timedelta(days=30)).strftime(
                "%Y-%m-%dT%H:%M:%SZ"
            ),
            activation=_ready_activation(),
            live_enabled=live_enabled,
        )
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
    with pytest.MonkeyPatch.context() as monkeypatch:
        _admit_test_destination_primitive(monkeypatch)
        return _require_api("form_publication_authorization")(
            approval_bundle=bundle,
            approval_bundle_reference=_bundle_reference(bundle),
            approval_boundary_sentinel_result="success",
            governance=governance or _governance(),
            destination_operation_profile_digest=(
                github_packages.github_packages_destination_operation_profile().profile_digest
            ),
            completed_at="2026-08-13T16:00:00Z",
            control=bundle.control,
        )


def _proof(
    publication: PublicationSnapshot,
    governance: GovernanceObservation | None = None,
) -> ExactSatisfiedFinalizationProof:
    from ..contracts.test_publication_finalizer_records import (  # noqa: PLC0415
        _finalization_proof,
    )

    fresh = governance or _governance()
    proof = _finalization_proof(attempt=publication.attempt)
    observed_at = fresh.observed_at.isoformat().replace("+00:00", "Z")
    return replace(
        proof,
        publication_snapshot_reference=_snapshot_reference(publication),
        governance_proof=GovernanceProof(
            provenance=governance_observation_provenance(fresh),
            current_main_sha=fresh.current_main_sha,
            observed_at=observed_at,
            expires_at=fresh.attestation.expires_at.isoformat().replace(
                "+00:00", "Z"
            ),
            live_enabled=fresh.attestation.live_enabled,
        ),
        package_control_proof=replace(
            proof.package_control_proof, observed_at=observed_at
        ),
        exact_version_readback=replace(
            proof.exact_version_readback, observed_at=observed_at
        ),
        proved_at="2026-08-13T16:00:00Z",
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
    monkeypatch,
) -> None:
    _admit_test_destination_primitive(monkeypatch)
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
            destination_operation_profile_digest=(
                publication.materialized_actions[
                    0
                ].destination_operation_profile_digest
            ),
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
            destination_operation_profile_digest=(
                publication.materialized_actions[
                    0
                ].destination_operation_profile_digest
            ),
            completed_at="2026-08-13T16:00:00Z",
            control=bundle.control,
        )


def test_exact_satisfied_proof_rejects_action_bearing_snapshot(
    qualified_simulation,
    observation_case,
) -> None:
    _attempt, _binding, _decision, publication = _closure(
        qualified_simulation,
        with_action=True,
    )

    with pytest.raises(ValueError, match="zero-action Snapshot"):
        validate_exact_satisfied_snapshot(
            **observation_case.arguments(),
            publication_snapshot=publication,
            publication_snapshot_reference=_snapshot_reference(publication),
            observation=_observation(observation_case),
        )


@pytest.mark.parametrize(
    ("state", "message"),
    [
        ("disabled-ready", "fresh enabled Governance"),
        ("expired-native", "unexpired native acceptance"),
        ("different-profile", "profile differs"),
        ("unadmitted-suite", "not implemented"),
    ],
)
def test_approval_rechecks_current_v2_action_authority(
    qualified_simulation, monkeypatch, state, message
) -> None:
    """Fresh Authorization requires the current Action's admitted acceptance."""
    _admit_test_destination_primitive(monkeypatch)
    _attempt, binding, decision, publication = _closure(
        qualified_simulation, with_action=True
    )
    bundle = _bundle(binding, decision, publication)
    governance = _governance(live_enabled=state != "disabled-ready")
    attestation = governance.attestation
    activation = attestation.activation
    assert isinstance(activation, EnabledGovernanceActivation)
    primitive = activation.destination_primitive
    profile_digest = publication.materialized_actions[
        0
    ].destination_operation_profile_digest
    if state == "expired-native":
        primitive = replace(
            primitive,
            captured_at=governance.observed_at - timedelta(days=90),
        )
        _admit_test_destination_primitive(monkeypatch, primitive=primitive)
    elif state == "unadmitted-suite":
        primitive = replace(
            primitive, native_acceptance_suite_version="unadmitted"
        )
    elif state == "different-profile":
        profile_digest = "sha256:" + "e" * 64
    attestation = replace(
        attestation,
        activation=replace(activation, destination_primitive=primitive),
    )
    governance = replace(
        governance,
        attestation=attestation,
        canonical_content_digest=attestation.content_digest,
    )
    with pytest.raises(ValueError, match=message):
        live.form_publication_authorization(
            approval_bundle=bundle,
            approval_bundle_reference=_bundle_reference(bundle),
            approval_boundary_sentinel_result="success",
            governance=governance,
            destination_operation_profile_digest=profile_digest,
            completed_at=(governance.observed_at + timedelta(seconds=1))
            .isoformat()
            .replace("+00:00", "Z"),
            control=bundle.control,
        )
