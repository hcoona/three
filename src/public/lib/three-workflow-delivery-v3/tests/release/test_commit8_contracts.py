"""Focused strict contract tests for Workflow Delivery v3 commit 8."""

from __future__ import annotations

# ruff: noqa: D103, ISC004
from dataclasses import FrozenInstanceError, fields, replace

import pytest
from three_workflow_delivery_v3 import platform as platform_api
from three_workflow_delivery_v3.canonical import JsonValue, canonical_sha256
from three_workflow_delivery_v3.platform import github as github_platform
from three_workflow_delivery_v3.records import release as release_records
from three_workflow_delivery_v3.records.artifacts import (
    ArtifactTransportIdentity,
)
from three_workflow_delivery_v3.records.bindings import (
    CurrentAuthorityContext,
    admit,
)
from three_workflow_delivery_v3.records.release import (
    ActionResult,
    AttemptOutcome,
    AuthorizationRecord,
    BuddyExecutionIdentity,
    CapabilityAdmissionDecision,
    ExternalPackageCoordinate,
    PublicationObservationReference,
    PublicationSnapshot,
    Receipt,
    ReleaseAttemptBinding,
    ReleaseAttemptIdentity,
    ReleaseRecord,
    SimulationBinding,
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
    "AuthorizationRecord",
    "CapabilityAdmissionDecision",
    "ActionResult",
    "Receipt",
    "AttemptOutcome",
)

RETIRED_RECORD_TYPES = (
    "HistoricalExecutionRecord",
    "ExecutionHistoryAdmissionSnapshot",
    "ReceiptTransportReference",
    "CapabilityGroupResultBundle",
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


def _authorization() -> AuthorizationRecord:
    return AuthorizationRecord(
        attempt=ATTEMPT,
        publication_snapshot_digest="sha256:" + ("1" * 64),
        reviewer_summary_artifact_id=710,
        reviewer_summary_upload_digest="sha256:" + ("2" * 64),
        reviewer_summary_payload_digest="sha256:" + ("3" * 64),
        workflow_run_id=ATTEMPT.workflow_run_id,
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
        authorization_digest=_authorization().authorization_digest,
        capability_admission_digests=(_capability_decision().decision_digest,),
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
        authorization_digest=None,
        capability_admission_digests=(),
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
        authorization_digest=None,
        capability_admission_digests=(),
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
        authorization_digest=None,
        capability_admission_digests=(),
        action_result_digests=(),
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
        _action_result(),
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
    failed_result = _action_result(
        outcome="failed",
        mutation_disposition="no-side-effect",
        with_receipt=False,
    )

    with pytest.raises(ValueError, match="control target binding mismatch"):
        replace(qualified_simulation.binding, control=wrong_control)
    with pytest.raises(ValueError, match="control target binding mismatch"):
        replace(_authorization(), control=wrong_control)
    with pytest.raises(ValueError, match="control target binding mismatch"):
        replace(_capability_decision(), control=wrong_control)
    with pytest.raises(ValueError, match="control target binding mismatch"):
        replace(_receipt(), control=wrong_control)
    with pytest.raises(ValueError, match="control target binding mismatch"):
        replace(failed_result, control=wrong_control)

    def assert_transport_rejected(
        record: ReleaseRecord,
        expected_type: type[ReleaseRecord],
    ) -> None:
        document = record.to_document()
        document["control"] = wrong_control
        with pytest.raises(
            ValueError,
            match="control target binding mismatch",
        ):
            release_record_from_document(
                document,
                expected_type=expected_type,
            )

    assert_transport_rejected(
        qualified_simulation.binding,
        SimulationBinding,
    )
    assert_transport_rejected(_authorization(), AuthorizationRecord)
    assert_transport_rejected(
        _capability_decision(),
        CapabilityAdmissionDecision,
    )
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
            "one direct Action Result lineage",
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
    ("field", "document_field", "values"),
    [
        (
            "capability_admission_digests",
            "capability-admission-digests",
            (),
        ),
        (
            "capability_admission_digests",
            "capability-admission-digests",
            (
                "sha256:" + ("e" * 64),
                "sha256:" + ("f" * 64),
            ),
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
    values: tuple[str, ...],
) -> None:
    def replace_outcome() -> AttemptOutcome:
        if field == "capability_admission_digests":
            return replace(
                _attempt_outcome(),
                capability_admission_digests=values,
            )
        return replace(
            _attempt_outcome(),
            action_result_digests=values,
        )

    with pytest.raises(ValueError, match="lineage"):
        replace_outcome()

    document = _attempt_outcome().to_document()
    transport_values = document[document_field]
    assert isinstance(transport_values, list)
    transport_values.clear()
    transport_values.extend(values)
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
def test_non_successful_outcome_rejects_multiple_capability_admissions(
    outcome: AttemptOutcome,
) -> None:
    capability_digests = (
        "sha256:" + ("e" * 64),
        "sha256:" + ("f" * 64),
    )

    with pytest.raises(ValueError, match="at most one Capability Admission"):
        replace(
            outcome,
            capability_admission_digests=capability_digests,
        )

    document = outcome.to_document()
    transport_values = document["capability-admission-digests"]
    assert isinstance(transport_values, list)
    transport_values.clear()
    transport_values.extend(capability_digests)
    with pytest.raises(ValueError, match="at most one Capability Admission"):
        release_record_from_document(
            document,
            expected_type=AttemptOutcome,
        )


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
        replace(
            blocked,
            attempt=replace(
                blocked.attempt,
                workflow_run_id=blocked.attempt.workflow_run_id + 1,
            ),
        )


@pytest.mark.parametrize("result", ["success", "blocked"])
def test_capability_decision_allows_transitional_zero_action_closure(
    result: str,
) -> None:
    decision = _capability_decision(result=result)
    zero_action = replace(
        decision,
        action_digests=(),
        artifact_digests=(),
        resource_key_sets=(),
        lock_groups=(),
    )

    assert zero_action.authorizing is False
    assert (
        release_record_from_document(
            zero_action.to_document(),
            expected_type=CapabilityAdmissionDecision,
        )
        == zero_action
    )


@pytest.mark.parametrize("result", ["success", "blocked"])
def test_capability_decision_rejects_multiple_action_closures(
    result: str,
) -> None:
    decision = _capability_decision(result=result)

    with pytest.raises(
        ValueError,
        match="permits at most one action closure",
    ):
        replace(
            decision,
            action_digests=(
                decision.action_digests[0],
                "sha256:" + ("8" * 64),
            ),
            artifact_digests=(
                decision.artifact_digests[0],
                "sha256:" + ("9" * 64),
            ),
            resource_key_sets=(
                *decision.resource_key_sets,
                ("action:publish-second", ("resource:second",)),
            ),
            lock_groups=(
                *decision.lock_groups,
                ("action:publish-second", "destination-package:second"),
            ),
        )


@pytest.mark.parametrize("result", ["success", "blocked"])
def test_capability_decision_transport_rejects_multiple_action_closures(
    result: str,
) -> None:
    decision = _capability_decision(result=result)
    document = decision.to_document()
    document["action-digests"] = [
        decision.action_digests[0],
        "sha256:" + ("8" * 64),
    ]
    document["artifact-digests"] = [
        decision.artifact_digests[0],
        "sha256:" + ("9" * 64),
    ]
    document["resource-key-sets"] = [
        [
            action_id,
            list(resource_keys),
        ]
        for action_id, resource_keys in (
            *decision.resource_key_sets,
            ("action:publish-second", ("resource:second",)),
        )
    ]
    document["lock-groups"] = [
        list(lock_group)
        for lock_group in (
            *decision.lock_groups,
            ("action:publish-second", "destination-package:second"),
        )
    ]

    with pytest.raises(
        ValueError,
        match="permits at most one action closure",
    ):
        release_record_from_document(
            document,
            expected_type=CapabilityAdmissionDecision,
        )


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
        control=f"workflow-delivery-v3:{attempt.execution.target}",
        workflow_run_id=attempt.workflow_run_id,
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
        action_results=(),
    )

    assert outcome.result == "success"
    assert outcome.terminal_phase == "finalized-no-op"
    assert outcome.capability_admission_digests == ()
    assert outcome.action_result_digests == ()
    unknown = finalize_attempt_outcome(
        attempt=attempt,
        qualification_decision=decision,
        publication_snapshot=publication,
        authorization=None,
        capability_decisions=(),
        action_results=(),
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
        action_results=(),
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
        ("action_result_digests", ("sha256:" + ("7" * 64),)),
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
        action_results=(),
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
