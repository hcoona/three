"""Test-first live authorization and finalization scenarios for commit 8."""

from __future__ import annotations

# ruff: noqa: D102, D103, D107, E501, FBT001, PLR2004
import hashlib
import inspect
import json
from argparse import Namespace
from dataclasses import replace
from typing import Any

import pytest
import three_workflow_delivery_v3.cli as cli_module
from three_workflow_delivery_v3.canonical import canonical_sha256, canonicalize
from three_workflow_delivery_v3.records.release import (
    ActionResult,
    AttemptOutcome,
    AuthorizationRecord,
    BuddyExecutionIdentity,
    CapabilityAdmissionDecision,
    CapabilityGroupResultBundle,
    ExternalPackageCoordinate,
    HistoricalExecutionRecord,
    PublicationAction,
    PublicationObservationReference,
    PublicationSnapshot,
    Receipt,
    ReceiptTransportReference,
    ReleaseAttemptIdentity,
    publication_action_inputs,
    publication_capability_group,
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
CONTROL = "control:" + ("c" * 64)
SNAPSHOT_BYTES = b'{"schema":"workflow-delivery/v3/publication-snapshot"}'
SNAPSHOT_DIGEST = f"sha256:{hashlib.sha256(SNAPSHOT_BYTES).hexdigest()}"
SUMMARY = (
    b"# Buddy publication review\n\nSnapshot: "
    + SNAPSHOT_DIGEST.encode()
    + b"\n"
)
SUMMARY_DIGEST = f"sha256:{hashlib.sha256(SUMMARY).hexdigest()}"
EXPECTED_LIVE_API = (
    "admit_live_capability",
    "discover_execution_history",
    "fetch_exact_public_revision",
    "form_authorization_record",
    "materialize_reviewer_artifact",
)


class RecordingHistoryClient:
    """Strict injectable history fake with cursor exhaustion."""

    def __init__(self, fault: str | None = None) -> None:
        self.fault = fault
        self.calls: list[tuple[str, object]] = []

    def list_runs(self, cursor: str | None) -> dict[str, object]:
        self.calls.append(("runs", cursor))
        if self.fault in {"denied", "rate-limited"}:
            raise RuntimeError(self.fault)
        if cursor is None:
            return {"items": ({"id": 41},), "next": "runs-2"}
        if self.fault == "duplicate":
            return {"items": ({"id": 41},), "next": None}
        if self.fault == "truncated":
            return {
                "items": ({"id": 42},),
                "next": None,
                "complete": False,
            }
        return {"items": ({"id": 42},), "next": None}

    def list_artifacts(
        self, run_id: int, cursor: str | None
    ) -> dict[str, object]:
        self.calls.append(("artifacts", (run_id, cursor)))
        if cursor is None:
            return {
                "items": ({"id": run_id * 100 + 1, "expired": False},),
                "next": f"artifacts-{run_id}-2",
            }
        return {
            "items": ({"id": run_id * 100 + 2, "expired": False},),
            "next": None,
        }

    def list_jobs(self, run_id: int, cursor: str | None) -> dict[str, object]:
        self.calls.append(("jobs", (run_id, cursor)))
        if cursor is None:
            return {
                "items": (
                    {
                        "id": run_id * 1000 + 1,
                        "phase": "quality",
                        "conclusion": "success",
                    },
                ),
                "next": f"jobs-{run_id}-2",
            }
        return {
            "items": (
                {
                    "id": run_id * 1000 + 2,
                    "phase": "release-finalizer",
                    "conclusion": "success",
                },
            ),
            "next": None,
        }

    def get_run_attempt(
        self,
        run_id: int,
        run_attempt: int,
    ) -> dict[str, object]:
        self.calls.append(("run-attempt", (run_id, run_attempt)))
        return {
            "id": run_id,
            "node_id": f"WFR_{run_id}",
            "head_sha": TARGET,
            "run_attempt": run_attempt,
            "status": "completed",
            "conclusion": "success",
        }

    def list_attempt_jobs(
        self,
        run_id: int,
        _run_attempt: int,
        cursor: str | None,
    ) -> dict[str, object]:
        return self.list_jobs(run_id, cursor)

    def download_artifact(self, artifact_id: int) -> bytes:
        self.calls.append(("download", artifact_id))
        run_id = artifact_id // 100
        record = HistoricalExecutionRecord(
            execution=BuddyExecutionIdentity(
                channel="buddy",
                release_unit="hcoona-release-smoke-npm",
                target=TARGET,
            ),
            artifact_id=artifact_id,
            artifact_digest="sha256:" + ("3" * 64),
            payload_digest="sha256:" + ("4" * 64),
            source_workflow_run_id=run_id,
            source_workflow_run_node_id=f"WFR_{run_id}",
            source_head_sha=TARGET,
            artifact_metadata=(("expired", "false"),),
            run_metadata=(("conclusion", "success"),),
            queried_run_attempt=1,
            queried_job_id=run_id * 1000 + 2,
            queried_job_conclusion="success",
            queried_phase="release-finalizer",
            diagnostic_claims=(("producer", "historical-finalizer"),),
        )
        return json.dumps(
            record.to_document(),
            separators=(",", ":"),
            sort_keys=True,
        ).encode()


def _require_api(name: str) -> Any:
    value = getattr(live, name, None)
    assert callable(value), (
        f"commit-8 phase-3 production API is missing: {name}"
    )
    return value


def _closure(scenario, *, with_action: bool):
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
                run_attempt=attempt.run_attempt,
                producer=scenario.artifact.transport.producer,
            ),
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
                capability_group=publication_capability_group(projection),
                capability_requirements=publication_capability_requirements(
                    projection
                ),
                expected_result=publication_expected_result(projection),
                receipt_contract=publication_receipt_contract(projection),
            ),
        )
    projection = snapshot.destination_projections[0]
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
        run_attempt=attempt.run_attempt,
        approval_job_id=711,
        approval_job="approval",
        environment="workflow-delivery-v3-buddy-smoke-approval",
        channel="buddy",
        completed_at="2026-08-13T16:00:00Z",
        producer="approval",
        control=CONTROL,
    )
    return attempt, decision, publication, authorization


def test_commit8_phase3_live_api_is_explicit_and_injectable() -> None:
    missing = tuple(
        name for name in EXPECTED_LIVE_API if not hasattr(live, name)
    )
    assert missing == (), f"missing commit-8 phase-3 API: {missing}"
    history_parameters = inspect.signature(
        _require_api("discover_execution_history")
    ).parameters
    assert {"client", "execution", "current_workflow_run_id"} <= set(
        history_parameters
    )


def test_history_exhausts_every_run_artifact_and_job_page_by_id() -> None:
    client = RecordingHistoryClient()
    discover = _require_api("discover_execution_history")

    snapshot = discover(
        client=client,
        execution=BuddyExecutionIdentity(
            channel="buddy",
            release_unit="hcoona-release-smoke-npm",
            target=TARGET,
        ),
        request_id="release-request:" + ("1" * 64),
        current_workflow_run_id=50,
        current_run_attempt=2,
    )

    assert [call for call in client.calls if call[0] == "runs"] == [
        ("runs", None),
        ("runs", "runs-2"),
    ]
    assert [call for call in client.calls if call[0] == "artifacts"] == [
        ("artifacts", (41, None)),
        ("artifacts", (41, "artifacts-41-2")),
        ("artifacts", (42, None)),
        ("artifacts", (42, "artifacts-42-2")),
    ]
    assert [call for call in client.calls if call[0] == "jobs"] == [
        ("jobs", (41, None)),
        ("jobs", (41, "jobs-41-2")),
        ("jobs", (42, None)),
        ("jobs", (42, "jobs-42-2")),
    ]
    assert [call[1] for call in client.calls if call[0] == "download"] == [
        4101,
        4102,
        4201,
        4202,
    ]
    assert snapshot.pagination_basis
    assert tuple(record.artifact_id for record in snapshot.records) == (
        4101,
        4102,
        4201,
        4202,
    )


@pytest.mark.parametrize(
    "fault",
    ["duplicate", "rate-limited", "denied", "truncated"],
)
def test_history_duplicate_rate_denial_and_truncation_fail_before_attempt(
    fault: str,
) -> None:
    client = RecordingHistoryClient(fault=fault)
    discover = _require_api("discover_execution_history")

    with pytest.raises(ValueError, match=fault):
        discover(
            client=client,
            execution=BuddyExecutionIdentity(
                channel="buddy",
                release_unit="hcoona-release-smoke-npm",
                target=TARGET,
            ),
            request_id="release-request:" + ("1" * 64),
            current_workflow_run_id=50,
            current_run_attempt=2,
        )
    assert not any(call[0] == "create-attempt" for call in client.calls)


def test_reviewer_artifact_preserves_exact_bytes_and_all_digest_bindings() -> (
    None
):
    materialize = _require_api("materialize_reviewer_artifact")

    artifact = materialize(
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
    assert artifact.upload_digest == "sha256:" + ("2" * 64)


@pytest.mark.parametrize(
    "target",
    ["a" * 39, "A" * 40, "a" * 41, "refs/heads/main"],
)
def test_anonymous_fetch_rejects_every_non_exact_target_without_transport(
    target: str,
) -> None:
    calls: list[tuple[str, ...]] = []
    fetch = _require_api("fetch_exact_public_revision")

    with pytest.raises(ValueError, match="40-character lowercase SHA"):
        fetch(target=target, run=lambda argv: calls.append(tuple(argv)))
    assert calls == []


def test_anonymous_fetch_verifies_exact_commit_and_detached_head_without_network() -> (
    None
):
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
    assert calls
    assert all("GITHUB_TOKEN" not in " ".join(call) for call in calls)
    assert all("checkout@" not in " ".join(call) for call in calls)
    assert any(TARGET in call for call in calls)
    assert any("https://github.com/hcoona/three.git" in call for call in calls)
    assert any(call[-2:] == ("rev-parse", "HEAD") for call in calls)
    assert any(call[-3:] == ("symbolic-ref", "-q", "HEAD") for call in calls)
    assert all(
        "refs/heads/" not in argument for call in calls for argument in call
    )


@pytest.mark.parametrize(
    "substitution",
    ["disabled", "expired", "resolved-commit", "blob", "content", "binding"],
)
def test_governance_freshness_substitution_blocks_and_restoration_needs_new_attempt(
    substitution: str,
) -> None:
    admit = _require_api("admit_live_capability")
    result = admit(substitution=substitution, restored=True)
    typed_diagnostics = {
        "disabled": "governance-live-disabled",
        "expired": "governance-attestation-expired",
        "resolved-commit": "governance-provenance-changed",
        "blob": "governance-provenance-changed",
        "content": "governance-content-changed",
        "binding": "governance-binding-changed",
    }

    assert result.current_attempt.result == "blocked"
    assert result.current_attempt.authorizing is False
    assert typed_diagnostics[substitution] in result.current_attempt.diagnostics
    assert result.restored_attempt.attempt != result.current_attempt.attempt
    assert result.restored_attempt.authorizing is True


def test_exact_noop_still_requires_authorization_and_emits_no_capability(
    qualified_simulation,
) -> None:
    attempt, decision, publication, authorization = _closure(
        qualified_simulation, with_action=False
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
    assert outcome.receipt_digests == ()


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
        group_bundles=(),
        receipts=(),
    )

    assert isinstance(outcome, AttemptOutcome)
    assert outcome.publication_snapshot_digest is None
    assert outcome.terminal_phase == "qualification"
    assert outcome.result == terminal_result
    assert outcome.uncertainty is uncertainty
    assert outcome.possibly_mutated is False
    assert outcome.next_action == next_action

    with pytest.raises(
        ValueError,
        match="cannot bind publication records",
    ):
        finalize_attempt_outcome(
            attempt=attempt,
            qualification_decision=unsuccessful,
            publication_snapshot=publication,
            authorization=None,
            capability_decisions=(),
            group_bundles=(),
            receipts=(),
        )


def test_successful_qualification_requires_publication_snapshot(
    qualified_simulation,
) -> None:
    attempt, decision, _publication, _authorization = _closure(
        qualified_simulation,
        with_action=False,
    )

    with pytest.raises(
        TypeError,
        match="Successful qualification requires Publication Snapshot",
    ):
        finalize_attempt_outcome(
            attempt=attempt,
            qualification_decision=decision,
            publication_snapshot=None,
            authorization=None,
            capability_decisions=(),
            group_bundles=(),
            receipts=(),
        )


def test_publication_preparation_interruption_terminalizes_without_snapshot(
    qualified_simulation,
) -> None:
    attempt, decision, publication, authorization = _closure(
        qualified_simulation,
        with_action=True,
    )
    reviewer = _require_api("materialize_reviewer_artifact")(
        snapshot_bytes=canonicalize(publication.to_document()),
        summary_bytes=SUMMARY,
        artifact_id=710,
        upload_digest="sha256:" + ("2" * 64),
    )
    capability_decision = _require_api("admit_live_capability")(
        attempt=attempt,
        authorization=authorization,
        publication_snapshot=publication,
        reviewer_artifact=reviewer,
    )
    _, _, bundle, receipt = _successful_action_records(publication)
    receipt_transport = _receipt_transport(receipt)

    outcome = finalize_attempt_outcome(
        attempt=attempt,
        qualification_decision=decision,
        publication_snapshot=None,
        authorization=None,
        capability_decisions=(),
        group_bundles=(),
        receipts=(),
        publication_preparation_interrupted=True,
    )

    assert outcome.publication_snapshot_digest is None
    assert outcome.authorization_digest is None
    assert outcome.terminal_phase == "publication-preparation"
    assert outcome.result == "incomplete"
    assert outcome.uncertainty is True
    assert outcome.possibly_mutated is False
    assert outcome.next_action == "new-attempt"

    def reject_contradiction(  # noqa: PLR0913
        *,
        publication_snapshot: PublicationSnapshot | None = None,
        supplied_authorization: AuthorizationRecord | None = None,
        capability_decisions: tuple[CapabilityAdmissionDecision, ...] = (),
        group_bundles: tuple[CapabilityGroupResultBundle, ...] = (),
        receipts: tuple[Receipt, ...] = (),
        receipt_transport_references: tuple[
            ReceiptTransportReference, ...
        ] = (),
        platform_terminated: bool = False,
        capability_may_have_started: bool = False,
    ) -> None:
        with pytest.raises(
            ValueError,
            match="contradictory records",
        ):
            finalize_attempt_outcome(
                attempt=attempt,
                qualification_decision=decision,
                publication_snapshot=publication_snapshot,
                authorization=supplied_authorization,
                capability_decisions=capability_decisions,
                group_bundles=group_bundles,
                receipts=receipts,
                receipt_transport_references=receipt_transport_references,
                publication_preparation_interrupted=True,
                platform_terminated=platform_terminated,
                capability_may_have_started=capability_may_have_started,
            )

    reject_contradiction(publication_snapshot=publication)
    reject_contradiction(supplied_authorization=authorization)
    reject_contradiction(capability_decisions=(capability_decision,))
    reject_contradiction(group_bundles=(bundle,))
    reject_contradiction(receipts=(receipt,))
    reject_contradiction(receipt_transport_references=(receipt_transport,))
    reject_contradiction(platform_terminated=True)
    reject_contradiction(capability_may_have_started=True)


def test_publication_preparation_interruption_rejects_non_boolean_fact(
    qualified_simulation,
) -> None:
    attempt, decision, _publication, _authorization = _closure(
        qualified_simulation,
        with_action=False,
    )

    with pytest.raises(TypeError, match="exact Booleans"):
        finalize_attempt_outcome(
            attempt=attempt,
            qualification_decision=decision,
            publication_snapshot=None,
            authorization=None,
            capability_decisions=(),
            group_bundles=(),
            receipts=(),
            publication_preparation_interrupted="true",  # type: ignore[arg-type]
        )


def test_diagnostic_only_rejection_never_authorizes_or_starts_capability() -> (
    None
):
    form = _require_api("form_authorization_record")
    calls: list[str] = []

    with pytest.raises(ValueError, match="diagnostic-only"):
        form(
            approval_result="deployment-review-denied",
            diagnostic={"review-id": 91},
            schedule_capability=lambda: calls.append("scheduled"),
        )
    assert calls == []


def test_publication_snapshot_requires_exact_qualification_lineage(
    qualified_simulation,
) -> None:
    attempt, decision, publication, authorization = _closure(
        qualified_simulation,
        with_action=False,
    )
    substituted = replace(
        publication,
        qualification_snapshot_digest="sha256:" + ("9" * 64),
    )
    rebound_authorization = replace(
        authorization,
        publication_snapshot_digest=substituted.snapshot_digest,
    )

    with pytest.raises(
        ValueError,
        match="Qualification binding mismatch",
    ):
        finalize_attempt_outcome(
            attempt=attempt,
            qualification_decision=decision,
            publication_snapshot=substituted,
            authorization=rebound_authorization,
            capability_decisions=(),
            group_bundles=(),
            receipts=(),
        )


def test_successful_approval_only_forms_bound_authorization_without_scheduling(
    qualified_simulation,
) -> None:
    attempt, _, publication, _ = _closure(
        qualified_simulation, with_action=False
    )
    reviewer = _require_api("materialize_reviewer_artifact")(
        snapshot_bytes=canonicalize(publication.to_document()),
        summary_bytes=SUMMARY,
        artifact_id=710,
        upload_digest="sha256:" + ("2" * 64),
    )
    calls: list[str] = []

    authorization = _require_api("form_authorization_record")(
        approval_result="success",
        attempt=attempt,
        publication_snapshot=publication,
        reviewer_artifact=reviewer,
        approval_job_id=711,
        completed_at="2026-08-13T16:00:00Z",
        control=CONTROL,
        schedule_capability=lambda: calls.append("scheduled"),
    )

    assert authorization.attempt == attempt
    assert authorization.result == "success"
    assert (
        authorization.publication_snapshot_digest == publication.snapshot_digest
    )
    assert authorization.reviewer_summary_artifact_id == 710
    assert authorization.reviewer_summary_payload_digest == SUMMARY_DIGEST
    assert authorization.approval_job_id == 711
    assert authorization.environment == (
        "workflow-delivery-v3-buddy-smoke-approval"
    )
    assert calls == []


def test_capability_admission_closes_exact_planned_action_and_resource_sets(
    qualified_simulation,
) -> None:
    attempt, _, publication, authorization = _closure(
        qualified_simulation, with_action=True
    )
    reviewer = _require_api("materialize_reviewer_artifact")(
        snapshot_bytes=canonicalize(publication.to_document()),
        summary_bytes=SUMMARY,
        artifact_id=710,
        upload_digest="sha256:" + ("2" * 64),
    )
    action = publication.materialized_actions[0]

    decision = _require_api("admit_live_capability")(
        attempt=attempt,
        authorization=authorization,
        publication_snapshot=publication,
        reviewer_artifact=reviewer,
    )

    assert decision.result == "success"
    assert decision.authorizing is True
    assert decision.action_digests == (action.action_digest,)
    assert decision.artifact_digests == (action.artifact_digest,)
    assert decision.resource_key_sets == (
        (action.action_id, action.mutable_resource_keys),
    )
    assert decision.lock_groups == ((action.action_id, action.lock_group),)
    assert decision.capability_group_manifest == (
        (action.capability_group, (action.action_id,)),
    )
    empty_success = replace(
        decision,
        action_digests=(),
        artifact_digests=(),
        resource_key_sets=(),
        lock_groups=(),
        capability_group_manifest=(),
    )
    assert empty_success.authorizing is False

    substituted_reviewer = _require_api("materialize_reviewer_artifact")(
        snapshot_bytes=b"{}",
        summary_bytes=SUMMARY,
        artifact_id=710,
        upload_digest="sha256:" + ("2" * 64),
    )
    with pytest.raises(ValueError, match="reviewer artifact mismatch"):
        _require_api("admit_live_capability")(
            attempt=attempt,
            authorization=authorization,
            publication_snapshot=publication,
            reviewer_artifact=substituted_reviewer,
        )


def _successful_action_records(publication: PublicationSnapshot):
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
        control=CONTROL,
        workflow_run_id=publication.attempt.workflow_run_id,
        run_attempt=publication.attempt.run_attempt,
    )
    result = ActionResult(
        attempt=publication.attempt,
        publication_snapshot_digest=publication.snapshot_digest,
        action_id=action.action_id,
        action_digest=action.action_digest,
        lock_group=action.lock_group,
        outcome="success",
        mutation_disposition="created",
        response_identity_digest=receipt.response_identity_digest,
        receipt_artifact_id=730,
        receipt_artifact_name="receipt.json",
        receipt_artifact_digest="sha256:" + ("d" * 64),
        receipt_payload_digest=receipt.receipt_digest,
        receipt_digest=receipt.receipt_digest,
        diagnostic_reference=None,
        producer="publish-github-packages",
        control=CONTROL,
        workflow_run_id=publication.attempt.workflow_run_id,
        run_attempt=publication.attempt.run_attempt,
    )
    bundle = CapabilityGroupResultBundle(
        attempt=publication.attempt,
        publication_snapshot_digest=publication.snapshot_digest,
        capability_group=action.capability_group,
        planned_action_ids=(action.action_id,),
        action_results=(result,),
        completion_state="complete",
        producer="publish-github-packages",
        control=CONTROL,
        workflow_run_id=publication.attempt.workflow_run_id,
        run_attempt=publication.attempt.run_attempt,
    )
    return action, result, bundle, receipt


def _receipt_transport(receipt: Receipt) -> ReceiptTransportReference:
    return ReceiptTransportReference(
        action_id=receipt.action_id,
        artifact_id=730,
        artifact_name="receipt.json",
        upload_digest="sha256:" + ("d" * 64),
        payload_digest=receipt.receipt_digest,
    )


def test_missing_authorization_without_denial_is_unknown_replayable_contract(
    qualified_simulation,
) -> None:
    attempt, decision, publication, _authorization = _closure(
        qualified_simulation, with_action=False
    )

    outcome = finalize_attempt_outcome(
        attempt=attempt,
        qualification_decision=decision,
        publication_snapshot=publication,
        authorization=None,
        capability_decisions=(),
        group_bundles=(),
        receipts=(),
    )

    assert outcome.authorization_digest is None
    assert outcome.result == "unknown-replayable-approval-contract"
    assert outcome.terminal_phase == "approval-contract"
    assert outcome.next_action == "replay"
    assert outcome.possibly_mutated is False


def test_missing_authorization_with_post_authorization_evidence_is_mutated(
    qualified_simulation,
) -> None:
    attempt, decision, publication, authorization = _closure(
        qualified_simulation, with_action=True
    )
    reviewer = _require_api("materialize_reviewer_artifact")(
        snapshot_bytes=canonicalize(publication.to_document()),
        summary_bytes=SUMMARY,
        artifact_id=710,
        upload_digest="sha256:" + ("2" * 64),
    )
    capability = _require_api("admit_live_capability")(
        attempt=attempt,
        authorization=authorization,
        publication_snapshot=publication,
        reviewer_artifact=reviewer,
    )

    outcome = finalize_attempt_outcome(
        attempt=attempt,
        qualification_decision=decision,
        publication_snapshot=publication,
        authorization=None,
        capability_decisions=(capability,),
        group_bundles=(),
        receipts=(),
    )

    assert outcome.result == "incomplete-possibly-mutated"
    assert outcome.terminal_phase == "authorization-contradiction"
    assert outcome.possibly_mutated is True
    assert outcome.next_action == "reobserve-and-replay"


def test_live_finalizer_recomputes_action_result_receipt_transport_closure(
    qualified_simulation,
) -> None:
    attempt, decision, publication, authorization = _closure(
        qualified_simulation, with_action=True
    )
    reviewer = _require_api("materialize_reviewer_artifact")(
        snapshot_bytes=canonicalize(publication.to_document()),
        summary_bytes=SUMMARY,
        artifact_id=710,
        upload_digest="sha256:" + ("2" * 64),
    )
    capability = _require_api("admit_live_capability")(
        attempt=attempt,
        authorization=authorization,
        publication_snapshot=publication,
        reviewer_artifact=reviewer,
    )
    _action, result, bundle, receipt = _successful_action_records(publication)

    outcome = finalize_attempt_outcome(
        attempt=attempt,
        qualification_decision=decision,
        publication_snapshot=publication,
        authorization=authorization,
        capability_decisions=(capability,),
        group_bundles=(bundle,),
        receipts=(receipt,),
        receipt_transport_references=(_receipt_transport(receipt),),
    )

    assert outcome.result == "success"
    with pytest.raises(ValueError, match="Receipt binding"):
        finalize_attempt_outcome(
            attempt=attempt,
            qualification_decision=decision,
            publication_snapshot=publication,
            authorization=authorization,
            capability_decisions=(capability,),
            group_bundles=(bundle,),
            receipts=(receipt,),
            receipt_transport_references=(
                replace(
                    _receipt_transport(receipt),
                    artifact_name="substituted.json",
                ),
            ),
        )
    substituted_capability = replace(
        capability,
        resource_key_sets=(
            (result.action_id, ("external-package-coordinate:x",)),
        ),
    )
    with pytest.raises(ValueError, match="Capability Decisions"):
        finalize_attempt_outcome(
            attempt=attempt,
            qualification_decision=decision,
            publication_snapshot=publication,
            authorization=authorization,
            capability_decisions=(substituted_capability,),
            group_bundles=(bundle,),
            receipts=(receipt,),
            receipt_transport_references=(_receipt_transport(receipt),),
        )

    substituted_result = replace(
        result,
        action_digest="sha256:" + ("8" * 64),
    )
    substituted_bundle = CapabilityGroupResultBundle(
        attempt=publication.attempt,
        publication_snapshot_digest=publication.snapshot_digest,
        capability_group=bundle.capability_group,
        planned_action_ids=bundle.planned_action_ids,
        action_results=(substituted_result,),
        completion_state="complete",
        producer=bundle.producer,
        control=bundle.control,
        workflow_run_id=bundle.workflow_run_id,
        run_attempt=bundle.run_attempt,
    )
    with pytest.raises(ValueError, match="Action Result binding"):
        finalize_attempt_outcome(
            attempt=attempt,
            qualification_decision=decision,
            publication_snapshot=publication,
            authorization=authorization,
            capability_decisions=(capability,),
            group_bundles=(substituted_bundle,),
            receipts=(receipt,),
            receipt_transport_references=(_receipt_transport(receipt),),
        )

    substituted_receipt = replace(
        receipt,
        artifact_transport=replace(receipt.artifact_transport, artifact_id=999),
    )
    with pytest.raises(ValueError, match="Receipt binding"):
        finalize_attempt_outcome(
            attempt=attempt,
            qualification_decision=decision,
            publication_snapshot=publication,
            authorization=authorization,
            capability_decisions=(capability,),
            group_bundles=(bundle,),
            receipts=(substituted_receipt,),
            receipt_transport_references=(
                _receipt_transport(substituted_receipt),
            ),
        )


def test_receipt_loss_after_possible_mutation_requires_reobservation() -> None:
    attempt = ReleaseAttemptIdentity(
        execution=BuddyExecutionIdentity(
            channel="buddy",
            release_unit="hcoona-release-smoke-npm",
            target=TARGET,
        ),
        workflow_run_id=101,
        run_attempt=2,
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
        receipt_artifact_id=None,
        receipt_artifact_name=None,
        receipt_artifact_digest=None,
        receipt_payload_digest=None,
        receipt_digest=None,
        diagnostic_reference="receipt-lost",
        producer="publish-github-packages",
        control=CONTROL,
        workflow_run_id=101,
        run_attempt=2,
    )

    assert incomplete.outcome == "incomplete"
    assert incomplete.mutation_disposition == "possibly-mutated"
    assert incomplete.receipt_digest is None
    with pytest.raises(ValueError, match="durable Receipt"):
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
def test_platform_termination_mapping_is_capability_phase_exact(
    qualified_simulation,
    started: bool,
    result: str,
    phase: str,
    next_action: str,
) -> None:
    attempt, decision, publication, authorization = _closure(
        qualified_simulation, with_action=False
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
        capability_may_have_started=started,
    )

    assert (outcome.result, outcome.terminal_phase, outcome.next_action) == (
        result,
        phase,
        next_action,
    )
    assert outcome.possibly_mutated is started


def test_failed_publisher_with_marker_and_no_bundle_finalizes_both_facts(
    qualified_simulation,
) -> None:
    attempt, decision, publication, authorization = _closure(
        qualified_simulation,
        with_action=True,
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
        capability_may_have_started=True,
    )

    assert (
        outcome.result,
        outcome.terminal_phase,
        outcome.next_action,
        outcome.possibly_mutated,
    ) == (
        "incomplete-possibly-mutated",
        "post-capability-termination",
        "reobserve-and-replay",
        True,
    )


def test_after_marker_governance_failure_requires_reobservation(
    qualified_simulation,
) -> None:
    attempt, decision, publication, authorization = _closure(
        qualified_simulation,
        with_action=True,
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
        capability_may_have_started=True,
    )

    assert outcome.result == "incomplete-possibly-mutated"
    assert outcome.terminal_phase == "post-capability-termination"
    assert outcome.possibly_mutated is True
    assert outcome.next_action == "reobserve-and-replay"


@pytest.mark.parametrize(
    ("failure_mode", "terminal_bytes", "expected_diagnostic"),
    [
        pytest.param(
            "after-marker-before-npm",
            None,
            "terminal-state-missing-or-malformed-after-start",
            id="after-marker-before-npm",
        ),
        pytest.param(
            "runner-mutated-then-raised",
            None,
            "terminal-state-missing-or-malformed-after-start",
            id="runner-mutated-then-raised",
        ),
        pytest.param(
            "lost-response-or-readback-error",
            None,
            "terminal-state-missing-or-malformed-after-start",
            id="lost-response-or-readback-error",
        ),
        pytest.param(
            "state-persistence-failed",
            None,
            "terminal-state-missing-or-malformed-after-start",
            id="state-persistence-failed",
        ),
        pytest.param(
            "truncated-terminal-state",
            b'{"schema":',
            "terminal-state-missing-or-malformed-after-start",
            id="truncated-terminal-state",
        ),
        (
            "substituted-terminal-state",
            json.dumps(
                {
                    "schema": "workflow-delivery/v3/deferred-publication-result",
                    "action-id": "wrong",
                }
            ).encode(),
            "terminal-state-missing-or-malformed-after-start",
        ),
    ],
)
def test_start_marker_without_valid_terminal_state_is_possibly_mutated(  # noqa: PLR0913
    qualified_simulation,
    monkeypatch: pytest.MonkeyPatch,
    tmp_path,
    failure_mode: str,
    terminal_bytes: bytes | None,
    expected_diagnostic: str,
) -> None:
    assert failure_mode
    _attempt, _decision, publication, _authorization = _closure(
        qualified_simulation,
        with_action=True,
    )
    state_path = tmp_path / "terminal.json"
    if terminal_bytes is not None:
        state_path.write_bytes(terminal_bytes)
    monkeypatch.setattr(
        cli_module,
        "_load_publication_snapshot",
        lambda _arguments: publication,
    )
    monkeypatch.setattr(
        cli_module, "_record_outputs", lambda *_args, **_kwargs: None
    )
    result_path = tmp_path / "result.json"
    bundle_path = tmp_path / "bundle.json"
    status = cli_module._release_form_github_packages_result_command(  # noqa: SLF001
        Namespace(
            target=TARGET,
            execution_state=(
                str(state_path) if terminal_bytes is not None else None
            ),
            mutation_marker="marker.json",
            mutation_marker_artifact_id=77,
            publish_step_outcome="failure",
            receipt=None,
            receipt_digest=None,
            receipt_artifact_id=None,
            receipt_artifact_digest=None,
            result_output=str(result_path),
            bundle_output=str(bundle_path),
            github_output=None,
        )
    )
    result = json.loads(result_path.read_bytes())
    bundle = json.loads(bundle_path.read_bytes())

    assert status == 1
    assert result["outcome"] == "incomplete"
    assert result["mutation-disposition"] == "possibly-mutated"
    assert result["diagnostic-reference"] == expected_diagnostic
    assert bundle["completion-state"] == "incomplete"


def test_preflight_failure_without_marker_is_proven_no_side_effect(
    qualified_simulation,
    monkeypatch: pytest.MonkeyPatch,
    tmp_path,
) -> None:
    _attempt, _decision, publication, _authorization = _closure(
        qualified_simulation,
        with_action=True,
    )
    monkeypatch.setattr(
        cli_module,
        "_load_publication_snapshot",
        lambda _arguments: publication,
    )
    monkeypatch.setattr(
        cli_module, "_record_outputs", lambda *_args, **_kwargs: None
    )
    result_path = tmp_path / "result.json"
    status = cli_module._release_form_github_packages_result_command(  # noqa: SLF001
        Namespace(
            target=TARGET,
            execution_state=None,
            mutation_marker=None,
            mutation_marker_artifact_id=None,
            publish_step_outcome="skipped",
            receipt=None,
            receipt_digest=None,
            receipt_artifact_id=None,
            receipt_artifact_digest=None,
            result_output=str(result_path),
            bundle_output=str(tmp_path / "bundle.json"),
            github_output=None,
        )
    )
    result = json.loads(result_path.read_bytes())

    assert status == 1
    assert result["outcome"] == "failed"
    assert result["mutation-disposition"] == "no-side-effect"
    assert (
        result["diagnostic-reference"]
        == "preflight-failed-before-mutation-start"
    )


def test_receipt_persistence_failure_after_start_is_possibly_mutated(
    qualified_simulation,
    monkeypatch: pytest.MonkeyPatch,
    tmp_path,
) -> None:
    _attempt, _decision, publication, _authorization = _closure(
        qualified_simulation,
        with_action=True,
    )
    action = publication.materialized_actions[0]
    state_path = tmp_path / "terminal.json"
    state_path.write_text(
        json.dumps(
            {
                "schema": "workflow-delivery/v3/deferred-publication-result",
                "action-id": action.action_id,
                "action-digest": action.action_digest,
                "lock-group": action.lock_group,
                "outcome": "success",
                "mutation-disposition": "created",
                "response-identity-digest": "sha256:" + ("1" * 64),
                "receipt-digest": "sha256:" + ("2" * 64),
                "diagnostic-reference": None,
                "control": CONTROL,
            }
        ),
        encoding="utf-8",
    )
    monkeypatch.setattr(
        cli_module,
        "_load_publication_snapshot",
        lambda _arguments: publication,
    )
    monkeypatch.setattr(
        cli_module, "_record_outputs", lambda *_args, **_kwargs: None
    )
    result_path = tmp_path / "result.json"
    status = cli_module._release_form_github_packages_result_command(  # noqa: SLF001
        Namespace(
            target=TARGET,
            execution_state=str(state_path),
            mutation_marker="marker.json",
            mutation_marker_artifact_id=77,
            publish_step_outcome="success",
            receipt=None,
            receipt_digest=None,
            receipt_artifact_id=None,
            receipt_artifact_digest=None,
            result_output=str(result_path),
            bundle_output=str(tmp_path / "bundle.json"),
            github_output=None,
        )
    )
    result = json.loads(result_path.read_bytes())

    assert status == 1
    assert result["outcome"] == "incomplete"
    assert result["mutation-disposition"] == "possibly-mutated"
    assert (
        result["diagnostic-reference"]
        == "receipt-persistence-failed-after-start"
    )


def test_whole_release_replay_rejects_mixed_attempt_capability_records(
    qualified_simulation,
) -> None:
    attempt, decision, publication, authorization = _closure(
        qualified_simulation, with_action=False
    )
    other_attempt = replace(attempt, run_attempt=attempt.run_attempt + 1)
    foreign = CapabilityAdmissionDecision(
        attempt=other_attempt,
        authorization_digest=authorization.authorization_digest,
        publication_snapshot_digest=publication.snapshot_digest,
        reviewer_summary_artifact_id=710,
        reviewer_summary_upload_digest="sha256:" + ("2" * 64),
        reviewer_summary_payload_digest=SUMMARY_DIGEST,
        action_digests=(),
        artifact_digests=(),
        resource_key_sets=(),
        lock_groups=(),
        capability_group_manifest=(),
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
        governance_live_enabled=True,
        producer="approval-finalizer",
        control=CONTROL,
        workflow_run_id=other_attempt.workflow_run_id,
        run_attempt=other_attempt.run_attempt,
        result="success",
        diagnostics=(),
    )

    with pytest.raises(ValueError, match="Mixed-attempt failed-job reruns"):
        finalize_attempt_outcome(
            attempt=attempt,
            qualification_decision=decision,
            publication_snapshot=publication,
            authorization=authorization,
            capability_decisions=(foreign,),
            group_bundles=(),
            receipts=(),
        )


def _form_post_marker_result(  # noqa: PLR0913
    *,
    qualified_simulation,
    monkeypatch: pytest.MonkeyPatch,
    tmp_path,
    state: dict[str, object] | None,
    unreadable: bool = False,
    raw_state: bytes | None = None,
):
    _attempt, _decision, publication, _authorization = _closure(
        qualified_simulation,
        with_action=True,
    )
    action = publication.materialized_actions[0]
    state_path = tmp_path / "terminal.json"
    if raw_state is not None:
        state_path.write_bytes(raw_state)
    elif state is not None:
        state_path.write_bytes(json.dumps(state).encode())
    if unreadable:
        original_read_bytes = type(state_path).read_bytes

        def read_bytes(path):
            if path == state_path:
                raise OSError
            return original_read_bytes(path)

        monkeypatch.setattr(type(state_path), "read_bytes", read_bytes)
    monkeypatch.setattr(
        cli_module,
        "_load_publication_snapshot",
        lambda _arguments: publication,
    )
    monkeypatch.setattr(
        cli_module, "_record_outputs", lambda *_args, **_kwargs: None
    )
    result_path = tmp_path / "result.json"
    bundle_path = tmp_path / "bundle.json"
    status = cli_module._release_form_github_packages_result_command(  # noqa: SLF001
        Namespace(
            target=TARGET,
            execution_state=(
                str(state_path)
                if state is not None or raw_state is not None
                else None
            ),
            mutation_marker="marker.json",
            mutation_marker_artifact_id=77,
            publish_step_outcome="failure",
            receipt=None,
            receipt_digest=None,
            receipt_artifact_id=None,
            receipt_artifact_digest=None,
            result_output=str(result_path),
            bundle_output=str(bundle_path),
            github_output=None,
        )
    )
    return (
        status,
        action,
        publication,
        json.loads(result_path.read_bytes()),
        json.loads(bundle_path.read_bytes()),
    )


def _post_marker_state(action, **changes: object) -> dict[str, object]:
    state: dict[str, object] = {
        "schema": "workflow-delivery/v3/deferred-publication-result",
        "action-id": action.action_id,
        "action-digest": action.action_digest,
        "lock-group": action.lock_group,
        "outcome": "failed",
        "mutation-disposition": "no-side-effect",
        "response-identity-digest": None,
        "receipt-digest": None,
        "diagnostic-reference": "publisher-governance-recheck-blocked",
        "control": CONTROL,
    }
    state.update(changes)
    return state


@pytest.mark.parametrize(
    "diagnostic",
    ["governance-recheck-failed-before-runner", "create-conflict"],
)
def test_post_marker_no_side_effect_terminal_state_allowlist_forms_failed_bundle(
    qualified_simulation,
    monkeypatch: pytest.MonkeyPatch,
    tmp_path,
    diagnostic: str,
) -> None:
    _attempt, _decision, publication, _authorization = _closure(
        qualified_simulation,
        with_action=True,
    )
    action = publication.materialized_actions[0]
    status, bound_action, bound_publication, result, bundle = (
        _form_post_marker_result(
            qualified_simulation=qualified_simulation,
            monkeypatch=monkeypatch,
            tmp_path=tmp_path,
            state=_post_marker_state(
                action,
                **{"diagnostic-reference": diagnostic},
            ),
        )
    )

    assert status == 1
    assert bound_action == action
    assert result["outcome"] == "failed"
    assert result["mutation-disposition"] == "no-side-effect"
    assert result["diagnostic-reference"] == diagnostic
    assert result["response-identity-digest"] is None
    assert result["receipt-digest"] is None
    assert result["receipt-artifact-id"] is None
    assert result["receipt-payload-digest"] is None
    assert result["action-id"] == action.action_id
    assert result["action-digest"] == action.action_digest
    assert result["lock-group"] == action.lock_group
    assert result["publication-snapshot-digest"] == (
        bound_publication.snapshot_digest
    )
    assert result["attempt"] == bound_publication.attempt.to_document()
    assert bundle["completion-state"] == "failed"
    assert bundle["action-results"] == [result]
    assert bundle["planned-action-ids"] == [action.action_id]


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("diagnostic-reference", "Governance-recheck-failed-before-runner"),
        (
            "diagnostic-reference",
            "prefix-governance-recheck-failed-before-runner",
        ),
        (
            "diagnostic-reference",
            "governance-recheck-failed-before-runner-suffix",
        ),
        ("diagnostic-reference", "governance-recheck-failed-after-runner"),
        ("outcome", "incomplete"),
        ("outcome", "success"),
        ("mutation-disposition", "created"),
        ("mutation-disposition", "exact-race-accepted"),
        ("mutation-disposition", "possibly-mutated"),
    ],
)
def test_post_marker_governance_terminal_state_lookalikes_are_possibly_mutated(
    qualified_simulation,
    monkeypatch: pytest.MonkeyPatch,
    tmp_path,
    field: str,
    value: str,
) -> None:
    _attempt, _decision, publication, _authorization = _closure(
        qualified_simulation,
        with_action=True,
    )
    action = publication.materialized_actions[0]
    status, _action, _publication, result, bundle = _form_post_marker_result(
        qualified_simulation=qualified_simulation,
        monkeypatch=monkeypatch,
        tmp_path=tmp_path,
        state=_post_marker_state(action, **{field: value}),
    )

    assert status == 1
    assert result["outcome"] == "incomplete"
    assert result["mutation-disposition"] == "possibly-mutated"
    assert result["diagnostic-reference"] == (
        "terminal-state-missing-or-malformed-after-start"
    )
    assert result["response-identity-digest"] is None
    assert result["receipt-digest"] is None
    assert result["receipt-artifact-id"] is None
    assert result["receipt-payload-digest"] is None
    assert bundle["completion-state"] == "incomplete"
    assert bundle["action-results"] == [result]


@pytest.mark.parametrize("case", ["unreadable", "malformed", "generic"])
def test_start_marker_without_valid_terminal_state_additional_conservative_cases(
    qualified_simulation,
    monkeypatch: pytest.MonkeyPatch,
    tmp_path,
    case: str,
) -> None:
    _attempt, _decision, publication, _authorization = _closure(
        qualified_simulation,
        with_action=True,
    )
    action = publication.materialized_actions[0]
    state: dict[str, object] | None
    unreadable = case == "unreadable"
    if case == "generic":
        state = _post_marker_state(
            action,
            **{"diagnostic-reference": "generic-platform-failure"},
        )
    elif case == "malformed":
        state = None
    else:
        state = _post_marker_state(action)
    status, _action, _publication, result, bundle = _form_post_marker_result(
        qualified_simulation=qualified_simulation,
        monkeypatch=monkeypatch,
        tmp_path=tmp_path,
        state=state,
        unreadable=unreadable,
        raw_state=b"{not-json}" if case == "malformed" else None,
    )

    assert status == 1
    assert result["outcome"] == "incomplete"
    assert result["mutation-disposition"] == "possibly-mutated"
    assert result["diagnostic-reference"] == (
        "terminal-state-missing-or-malformed-after-start"
    )
    assert result["response-identity-digest"] is None
    assert result["receipt-digest"] is None
    assert result["receipt-artifact-id"] is None
    assert result["receipt-payload-digest"] is None
    assert bundle["completion-state"] == "incomplete"
    assert bundle["action-results"] == [result]


def _failed_action_bundle(
    publication: PublicationSnapshot,
    *,
    diagnostic: str,
) -> CapabilityGroupResultBundle:
    action = publication.materialized_actions[0]
    result = ActionResult(
        attempt=publication.attempt,
        publication_snapshot_digest=publication.snapshot_digest,
        action_id=action.action_id,
        action_digest=action.action_digest,
        lock_group=action.lock_group,
        outcome="failed",
        mutation_disposition="no-side-effect",
        response_identity_digest=None,
        receipt_artifact_id=None,
        receipt_artifact_name=None,
        receipt_artifact_digest=None,
        receipt_payload_digest=None,
        receipt_digest=None,
        diagnostic_reference=diagnostic,
        producer="publish-github-packages",
        control=CONTROL,
        workflow_run_id=publication.attempt.workflow_run_id,
        run_attempt=publication.attempt.run_attempt,
    )
    return CapabilityGroupResultBundle(
        attempt=publication.attempt,
        publication_snapshot_digest=publication.snapshot_digest,
        capability_group=action.capability_group,
        planned_action_ids=(action.action_id,),
        action_results=(result,),
        completion_state="failed",
        producer="publish-github-packages",
        control=CONTROL,
        workflow_run_id=publication.attempt.workflow_run_id,
        run_attempt=publication.attempt.run_attempt,
    )


def test_publisher_governance_blocked_bundle_requires_new_attempt(
    qualified_simulation,
) -> None:
    attempt, decision, publication, authorization = _closure(
        qualified_simulation,
        with_action=True,
    )
    reviewer = _require_api("materialize_reviewer_artifact")(
        snapshot_bytes=canonicalize(publication.to_document()),
        summary_bytes=SUMMARY,
        artifact_id=710,
        upload_digest="sha256:" + ("2" * 64),
    )
    capability = _require_api("admit_live_capability")(
        attempt=attempt,
        authorization=authorization,
        publication_snapshot=publication,
        reviewer_artifact=reviewer,
    )
    bundle = _failed_action_bundle(
        publication,
        diagnostic="governance-recheck-failed-before-runner",
    )

    outcome = finalize_attempt_outcome(
        attempt=attempt,
        qualification_decision=decision,
        publication_snapshot=publication,
        authorization=authorization,
        capability_decisions=(capability,),
        group_bundles=(bundle,),
        receipts=(),
    )

    assert outcome.attempt == attempt
    assert outcome.qualification_decision_digest == decision.decision_digest
    assert outcome.publication_snapshot_digest == publication.snapshot_digest
    assert outcome.authorization_digest == authorization.authorization_digest
    assert outcome.capability_admission_digests == (capability.decision_digest,)
    assert outcome.capability_group_bundle_digests == (bundle.bundle_digest,)
    assert outcome.receipt_digests == ()
    assert outcome.terminal_phase == "capability-blocked"
    assert outcome.result == "failure"
    assert outcome.uncertainty is False
    assert outcome.possibly_mutated is False
    assert outcome.next_action == "new-attempt"


@pytest.mark.parametrize(
    "diagnostic",
    ["create-conflict", "ordinary-publisher-failure"],
)
def test_non_governance_failed_bundle_remains_replayable(
    qualified_simulation,
    diagnostic: str,
) -> None:
    attempt, decision, publication, authorization = _closure(
        qualified_simulation,
        with_action=True,
    )
    reviewer = _require_api("materialize_reviewer_artifact")(
        snapshot_bytes=canonicalize(publication.to_document()),
        summary_bytes=SUMMARY,
        artifact_id=710,
        upload_digest="sha256:" + ("2" * 64),
    )
    capability = _require_api("admit_live_capability")(
        attempt=attempt,
        authorization=authorization,
        publication_snapshot=publication,
        reviewer_artifact=reviewer,
    )
    bundle = _failed_action_bundle(publication, diagnostic=diagnostic)

    outcome = finalize_attempt_outcome(
        attempt=attempt,
        qualification_decision=decision,
        publication_snapshot=publication,
        authorization=authorization,
        capability_decisions=(capability,),
        group_bundles=(bundle,),
        receipts=(),
    )

    assert outcome.terminal_phase == "finalized"
    assert outcome.result == "failure"
    assert outcome.uncertainty is False
    assert outcome.possibly_mutated is False
    assert outcome.next_action == "replay"
    assert outcome.capability_group_bundle_digests == (bundle.bundle_digest,)
    assert outcome.receipt_digests == ()


def test_after_marker_governance_failure_retains_uncertainty(
    qualified_simulation,
) -> None:
    attempt, decision, publication, authorization = _closure(
        qualified_simulation,
        with_action=True,
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
        capability_may_have_started=True,
    )

    assert outcome.terminal_phase == "post-capability-termination"
    assert outcome.result == "incomplete-possibly-mutated"
    assert outcome.uncertainty is True
    assert outcome.possibly_mutated is True
    assert outcome.next_action == "reobserve-and-replay"


@pytest.mark.parametrize(
    ("terminal_result", "failure_class", "next_action", "operand"),
    [
        pytest.param(
            "failure",
            "quality-failure",
            "fix-quality-failure-and-rerun",
            "publication-snapshot",
            id="failure-publication-snapshot",
        ),
        pytest.param(
            "failure",
            "quality-failure",
            "fix-quality-failure-and-rerun",
            "authorization",
            id="failure-authorization",
        ),
        pytest.param(
            "failure",
            "quality-failure",
            "fix-quality-failure-and-rerun",
            "capability-admission-decision",
            id="failure-capability-admission-decision",
        ),
        pytest.param(
            "failure",
            "quality-failure",
            "fix-quality-failure-and-rerun",
            "capability-group-result-bundle",
            id="failure-capability-group-result-bundle",
        ),
        pytest.param(
            "failure",
            "quality-failure",
            "fix-quality-failure-and-rerun",
            "receipt",
            id="failure-receipt",
        ),
        pytest.param(
            "failure",
            "quality-failure",
            "fix-quality-failure-and-rerun",
            "receipt-transport-reference",
            id="failure-receipt-transport-reference",
        ),
        pytest.param(
            "failure",
            "quality-failure",
            "fix-quality-failure-and-rerun",
            "publication-preparation-interrupted",
            id="failure-publication-preparation-interrupted",
        ),
        pytest.param(
            "failure",
            "quality-failure",
            "fix-quality-failure-and-rerun",
            "platform-terminated",
            id="failure-platform-terminated",
        ),
        pytest.param(
            "failure",
            "quality-failure",
            "fix-quality-failure-and-rerun",
            "capability-may-have-started",
            id="failure-capability-may-have-started",
        ),
        pytest.param(
            "incomplete",
            "incomplete-qualification",
            "new-attempt",
            "publication-snapshot",
            id="incomplete-publication-snapshot",
        ),
        pytest.param(
            "incomplete",
            "incomplete-qualification",
            "new-attempt",
            "authorization",
            id="incomplete-authorization",
        ),
        pytest.param(
            "incomplete",
            "incomplete-qualification",
            "new-attempt",
            "capability-admission-decision",
            id="incomplete-capability-admission-decision",
        ),
        pytest.param(
            "incomplete",
            "incomplete-qualification",
            "new-attempt",
            "capability-group-result-bundle",
            id="incomplete-capability-group-result-bundle",
        ),
        pytest.param(
            "incomplete",
            "incomplete-qualification",
            "new-attempt",
            "receipt",
            id="incomplete-receipt",
        ),
        pytest.param(
            "incomplete",
            "incomplete-qualification",
            "new-attempt",
            "receipt-transport-reference",
            id="incomplete-receipt-transport-reference",
        ),
        pytest.param(
            "incomplete",
            "incomplete-qualification",
            "new-attempt",
            "publication-preparation-interrupted",
            id="incomplete-publication-preparation-interrupted",
        ),
        pytest.param(
            "incomplete",
            "incomplete-qualification",
            "new-attempt",
            "platform-terminated",
            id="incomplete-platform-terminated",
        ),
        pytest.param(
            "incomplete",
            "incomplete-qualification",
            "new-attempt",
            "capability-may-have-started",
            id="incomplete-capability-may-have-started",
        ),
    ],
)
def test_unsuccessful_qualification_rejects_each_independent_publication_operand(
    qualified_simulation,
    terminal_result: str,
    failure_class: str,
    next_action: str,
    operand: str,
) -> None:
    attempt, decision, publication, authorization = _closure(
        qualified_simulation,
        with_action=True,
    )
    reviewer = _require_api("materialize_reviewer_artifact")(
        snapshot_bytes=canonicalize(publication.to_document()),
        summary_bytes=SUMMARY,
        artifact_id=710,
        upload_digest="sha256:" + ("2" * 64),
    )
    capability_decision = _require_api("admit_live_capability")(
        attempt=attempt,
        authorization=authorization,
        publication_snapshot=publication,
        reviewer_artifact=reviewer,
    )
    _action, _result, bundle, receipt = _successful_action_records(publication)
    receipt_transport = _receipt_transport(receipt)
    unsuccessful = replace(
        decision,
        terminal_result=terminal_result,
        failure_class=failure_class,
        next_action=next_action,
    )

    supplied_publication: PublicationSnapshot | None = None
    supplied_authorization: AuthorizationRecord | None = None
    capability_decisions: tuple[CapabilityAdmissionDecision, ...] = ()
    group_bundles: tuple[CapabilityGroupResultBundle, ...] = ()
    receipts: tuple[Receipt, ...] = ()
    receipt_transport_references: tuple[ReceiptTransportReference, ...] = ()
    publication_preparation_interrupted = False
    platform_terminated = False
    capability_may_have_started = False

    if operand == "publication-snapshot":
        supplied_publication = publication
    elif operand == "authorization":
        supplied_authorization = authorization
    elif operand == "capability-admission-decision":
        capability_decisions = (capability_decision,)
    elif operand == "capability-group-result-bundle":
        group_bundles = (bundle,)
    elif operand == "receipt":
        receipts = (receipt,)
    elif operand == "receipt-transport-reference":
        receipt_transport_references = (receipt_transport,)
    elif operand == "publication-preparation-interrupted":
        publication_preparation_interrupted = True
    elif operand == "platform-terminated":
        platform_terminated = True
    elif operand == "capability-may-have-started":
        capability_may_have_started = True
    else:
        message = f"Unhandled unsuccessful guard operand: {operand}"
        raise AssertionError(message)

    supplied_operands = (
        supplied_publication is not None,
        supplied_authorization is not None,
        bool(capability_decisions),
        bool(group_bundles),
        bool(receipts),
        bool(receipt_transport_references),
        publication_preparation_interrupted,
        platform_terminated,
        capability_may_have_started,
    )
    assert sum(supplied_operands) == 1

    with pytest.raises(
        ValueError,
        match=r"^Unsuccessful qualification cannot bind publication records$",
    ) as error:
        finalize_attempt_outcome(
            attempt=attempt,
            qualification_decision=unsuccessful,
            publication_snapshot=supplied_publication,
            authorization=supplied_authorization,
            capability_decisions=capability_decisions,
            group_bundles=group_bundles,
            receipts=receipts,
            receipt_transport_references=receipt_transport_references,
            publication_preparation_interrupted=(
                publication_preparation_interrupted
            ),
            platform_terminated=platform_terminated,
            capability_may_have_started=capability_may_have_started,
        )

    assert str(error.value) == (
        "Unsuccessful qualification cannot bind publication records"
    )
