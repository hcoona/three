"""Focused history-only admission tests for Workflow Delivery v3 commit 8."""

from __future__ import annotations

# ruff: noqa: D103, E501
import hashlib
import json
import zipfile
from dataclasses import replace
from datetime import UTC, datetime
from io import BytesIO

import pytest
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
)
from three_workflow_delivery_v3.records.bindings import (
    AdmissionMode,
    ExecutionHistoryContext,
    HistoryLineage,
    PlatformJobFacts,
    PlatformRunFacts,
    admit,
)
from three_workflow_delivery_v3.records.release import (
    AttemptOutcome,
    BuddyExecutionIdentity,
    ExecutionHistoryAdmissionSnapshot,
    HistoricalExecutionRecord,
    ReleaseAttemptBinding,
    ReleaseAttemptIdentity,
    admit_release_record,
)
from three_workflow_delivery_v3.records.release_transport import (
    ReleaseAdmissionBindings,
    release_record_from_document,
)
from three_workflow_delivery_v3.release.identity import (
    derive_buddy_execution_identity,
    derive_release_attempt_binding,
    normalize_buddy_live_intent,
)
from three_workflow_delivery_v3.release.live import (
    discover_execution_history,
    form_execution_history_admission_snapshot,
)

TARGET = "1" * 40
PRIOR_ATTEMPT = 2
DUPLICATE_JOB_COUNT = 2
EXECUTION = "buddy:hcoona-release-smoke-npm:" + TARGET
CONTROL = "control:" + ("2" * 64)
ARTIFACT_DIGEST = "sha256:" + ("3" * 64)
PLATFORM_METADATA = (
    ("artifact-created-at", "2026-08-13T12:00:00Z"),
    ("artifact-expired", False),
)
HISTORY_SNAPSHOT_ARTIFACT_ID = 1101
LINEAGE = HistoryLineage(
    release_execution=EXECUTION,
    purpose="live-release",
    target=TARGET,
    control_identity=CONTROL,
)


def _payload() -> dict[str, JsonValue]:
    return {
        "execution": EXECUTION,
        "target": TARGET,
        "producer": "diagnostic-prior-producer",
        "run_attempt": 1,
        "reusable_workflow": "diagnostic/reusable.yml",
        "purpose": "live-release",
        "control": CONTROL,
    }


def _facts(
    payload: dict[str, JsonValue],
    *,
    source_workflow_run_id: int = 400,
    source_run_attempt: int = 1,
    current_workflow_run_id: int = 401,
    current_run_attempt: int = 2,
) -> tuple[
    ExecutionHistoryContext,
    PlatformRunFacts,
    PlatformJobFacts,
]:
    context = ExecutionHistoryContext(
        lineage=LINEAGE,
        operation="admit",
        attempt_created=False,
        artifact_id=801,
        artifact_digest=ARTIFACT_DIGEST,
        payload_digest=canonical_sha256(payload),
        source_workflow_run_id=source_workflow_run_id,
        current_workflow_run_id=current_workflow_run_id,
        current_run_attempt=current_run_attempt,
        exposed_platform_metadata=PLATFORM_METADATA,
    )
    run = PlatformRunFacts(
        workflow_run_id=source_workflow_run_id,
        head_sha=TARGET,
        run_attempt=source_run_attempt,
        exposed_metadata=PLATFORM_METADATA,
    )
    job = PlatformJobFacts(
        job_id=901,
        conclusion="success",
        phase="finalized",
    )
    return context, run, job


def _admit(  # noqa: PLR0913
    payload: dict[str, JsonValue],
    context: ExecutionHistoryContext,
    run: PlatformRunFacts,
    job: PlatformJobFacts,
    *,
    requires_current_authority: bool = False,
    verified_prior_attempts: tuple[int, ...] = (),
):
    return admit(
        mode=AdmissionMode.EXECUTION_HISTORY,
        payload=payload,
        artifact_id=context.artifact_id,
        artifact_digest=context.artifact_digest,
        history=context,
        expected_history_lineage=LINEAGE,
        platform_run=run,
        platform_job=job,
        requires_current_authority=requires_current_authority,
        verified_prior_attempts=verified_prior_attempts,
    )


def test_history_preserves_trusted_attribution_and_separate_platform_facts() -> (
    None
):
    payload = _payload()
    context, run, job = _facts(payload)

    admitted = _admit(payload, context, run, job)

    assert admitted.mode is AdmissionMode.EXECUTION_HISTORY
    assert admitted.history_only is True
    assert admitted.release_execution == LINEAGE.release_execution
    assert admitted.target == LINEAGE.target
    assert admitted.control_identity == LINEAGE.control_identity
    assert admitted.artifact_digest == ARTIFACT_DIGEST
    assert admitted.payload_digest == canonical_sha256(payload)
    assert admitted.platform_run == run
    assert admitted.platform_job == job
    assert admitted.diagnostic_claims == (
        ("producer", "diagnostic-prior-producer"),
        ("run_attempt", 1),
        ("reusable_workflow", "diagnostic/reusable.yml"),
        ("purpose", "live-release"),
        ("control", CONTROL),
    )


def test_history_payload_cannot_select_its_own_authority() -> None:
    payload = _payload()
    context, run, job = _facts(payload)
    payload["admission_mode"] = "current-authority"

    with pytest.raises(ValueError, match="unknown field: admission_mode"):
        _admit(payload, context, run, job)


def test_history_producer_and_workflow_claims_remain_diagnostic_only() -> None:
    payload = _payload()
    payload["producer"] = "self-asserted-other-producer"
    payload["run_attempt"] = 99
    payload["reusable_workflow"] = "self/asserted-other.yml"
    context, run, job = _facts(payload)

    admitted = _admit(payload, context, run, job)

    assert admitted.release_execution == LINEAGE.release_execution
    assert admitted.target == LINEAGE.target
    assert admitted.control_identity == LINEAGE.control_identity
    assert admitted.platform_run == run
    assert admitted.platform_job == job
    assert admitted.diagnostic_claims[:3] == (
        ("producer", "self-asserted-other-producer"),
        ("run_attempt", 99),
        ("reusable_workflow", "self/asserted-other.yml"),
    )


def test_history_cannot_satisfy_current_authority() -> None:
    payload = _payload()
    context, run, job = _facts(payload)

    with pytest.raises(
        ValueError,
        match="history cannot satisfy current authority",
    ):
        _admit(
            payload,
            context,
            run,
            job,
            requires_current_authority=True,
        )


@pytest.mark.parametrize(
    ("changed_field", "message"),
    [
        ("workflow-run-id", "source_workflow_run_id"),
        ("head-sha", "head_sha"),
        ("platform-metadata", "exposed_platform_metadata"),
    ],
)
def test_history_rejects_each_trusted_attribution_substitution(
    changed_field: str,
    message: str,
) -> None:
    payload = _payload()
    context, run, job = _facts(payload)
    changed_context = context
    changed_run = run
    if changed_field == "workflow-run-id":
        changed_run = replace(run, workflow_run_id=402)
    elif changed_field == "head-sha":
        changed_run = replace(run, head_sha="4" * 40)
    else:
        changed_context = replace(
            context,
            exposed_platform_metadata=(("artifact-expired", True),),
        )

    with pytest.raises(ValueError, match=message):
        _admit(payload, changed_context, changed_run, job)


def test_history_rejects_artifact_transport_substitution() -> None:
    payload = _payload()
    context, run, job = _facts(payload)

    with pytest.raises(ValueError, match="artifact_id"):
        admit(
            mode=AdmissionMode.EXECUTION_HISTORY,
            payload=payload,
            artifact_id=context.artifact_id + 1,
            artifact_digest=context.artifact_digest,
            history=context,
            expected_history_lineage=LINEAGE,
            platform_run=run,
            platform_job=job,
        )


def test_history_rejects_payload_digest_substitution() -> None:
    payload = _payload()
    context, run, job = _facts(payload)
    context = replace(
        context,
        payload_digest="sha256:" + ("5" * 64),
    )

    with pytest.raises(ValueError, match="payload integrity mismatch"):
        _admit(payload, context, run, job)


@pytest.mark.parametrize(
    ("operation", "attempt_created"),
    [
        ("finalize", False),
        ("admit", True),
    ],
)
def test_history_is_confined_to_pre_attempt_live_admit(
    operation: str,
    attempt_created: bool,  # noqa: FBT001
) -> None:
    payload = _payload()
    context, run, job = _facts(payload)
    context = replace(
        context,
        operation=operation,
        attempt_created=attempt_created,
    )

    with pytest.raises(ValueError, match="pre-Attempt live admit"):
        _admit(payload, context, run, job)


def test_same_run_history_requires_verified_earlier_attempt_existence() -> None:
    payload = _payload()
    context, run, job = _facts(
        payload,
        source_workflow_run_id=500,
        source_run_attempt=1,
        current_workflow_run_id=500,
        current_run_attempt=2,
    )

    with pytest.raises(ValueError, match="lacks a verified prior"):
        _admit(payload, context, run, job)

    admitted = _admit(
        payload,
        context,
        run,
        job,
        verified_prior_attempts=(1,),
    )
    assert admitted.platform_run == run
    assert admitted.history_only is True


@pytest.mark.parametrize(
    ("conclusion", "phase", "message"),
    [
        ("failure", "finalized", "conclusion"),
        ("success", "publish", "phase"),
    ],
)
def test_history_requires_separately_queried_successful_finalized_phase(
    conclusion: str,
    phase: str,
    message: str,
) -> None:
    payload = _payload()
    context, run, job = _facts(payload)
    job = replace(job, conclusion=conclusion, phase=phase)

    with pytest.raises(ValueError, match=message):
        _admit(payload, context, run, job)


def _history_record(
    *,
    artifact_id: int = 1001,
    source_run_id: int = 600,
    run_attempt: int = 1,
) -> HistoricalExecutionRecord:
    execution = BuddyExecutionIdentity(
        channel="buddy",
        release_unit="hcoona-release-smoke-npm",
        target=TARGET,
    )
    return HistoricalExecutionRecord(
        execution=execution,
        artifact_id=artifact_id,
        artifact_digest="sha256:" + ("6" * 64),
        payload_digest="sha256:" + ("7" * 64),
        source_workflow_run_id=source_run_id,
        source_workflow_run_node_id=f"WFR_{source_run_id}",
        source_head_sha=TARGET,
        artifact_metadata=(("expired", "false"),),
        run_metadata=(("conclusion", "success"),),
        queried_run_attempt=run_attempt,
        queried_job_id=artifact_id + 100,
        queried_job_conclusion="success",
        queried_phase="finalized",
        diagnostic_claims=(
            ("producer", "self-asserted"),
            ("reusable-workflow", "diagnostic-only.yml"),
        ),
    )


def _payload_bytes(record: HistoricalExecutionRecord) -> bytes:
    return json.dumps(
        record.to_document(),
        separators=(",", ":"),
        sort_keys=True,
    ).encode()


def _binding_payload(
    *,
    source_run_id: int,
    run_attempt: int,
) -> bytes:
    del run_attempt
    execution = BuddyExecutionIdentity(
        channel="buddy",
        release_unit="hcoona-release-smoke-npm",
        target=TARGET,
    )
    binding = ReleaseAttemptBinding(
        intent_digest="sha256:" + ("0" * 64),
        request_id="release-request:" + ("f" * 64),
        execution=execution,
        attempt=ReleaseAttemptIdentity(
            execution=execution,
            workflow_run_id=source_run_id,
        ),
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
    return json.dumps(
        binding.to_document(),
        separators=(",", ":"),
        sort_keys=True,
    ).encode()


def _outcome_payload(
    *,
    source_run_id: int,
    run_attempt: int,
) -> bytes:
    del run_attempt
    execution = BuddyExecutionIdentity(
        channel="buddy",
        release_unit="hcoona-release-smoke-npm",
        target=TARGET,
    )
    outcome = AttemptOutcome(
        attempt=ReleaseAttemptIdentity(
            execution=execution,
            workflow_run_id=source_run_id,
        ),
        qualification_decision_digest="sha256:" + ("d" * 64),
        publication_snapshot_digest=None,
        authorization_digest=None,
        capability_admission_digests=(),
        capability_group_bundle_digests=(),
        receipt_digests=(),
        terminal_phase="qualification",
        result="failure",
        uncertainty=False,
        possibly_mutated=False,
        next_action="fix-quality-failure-and-rerun",
    )
    return json.dumps(
        outcome.to_document(),
        separators=(",", ":"),
        sort_keys=True,
    ).encode()


def _archive_bytes(
    payload: bytes,
    *,
    extra_payload: bytes | None = None,
) -> bytes:
    buffer = BytesIO()
    with zipfile.ZipFile(buffer, "w") as archive:
        archive.writestr("attempt-outcome.json", payload)
        if extra_payload is not None:
            archive.writestr("extra.json", extra_payload)
    return buffer.getvalue()


def _listed_artifact(
    artifact_id: int,
    **facts: object,
) -> dict[str, object]:
    return {"id": artifact_id, "expired": False, **facts}


def _binding_job(
    *,
    job_id: int,
    binding_conclusion: str,
    completed_at: str,
    job_conclusion: str = "failure",
) -> dict[str, object]:
    return {
        "id": job_id,
        "phase": (
            "Run same-revision Buddy live Attempt / "
            "Admit live Attempt and retained history"
        ),
        "status": "completed",
        "conclusion": job_conclusion,
        "started_at": completed_at,
        "completed_at": completed_at,
        "steps": (
            {
                "name": "Bind current live Attempt",
                "status": "completed",
                "conclusion": binding_conclusion,
                "started_at": completed_at,
                "completed_at": completed_at,
            },
        ),
    }


class _DiscoveryClient:
    def __init__(
        self,
        *,
        runs: tuple[dict[str, object], ...],
        artifacts: dict[int, tuple[dict[str, object], ...]] | None = None,
        jobs: dict[int, tuple[dict[str, object], ...]] | None = None,
        attempt_jobs: (
            dict[tuple[int, int], tuple[dict[str, object], ...]] | None
        ) = None,
        payloads: dict[int, bytes | RuntimeError] | None = None,
    ) -> None:
        self.runs = runs
        self.artifacts = artifacts or {}
        self.jobs = jobs or {}
        self.attempt_jobs = attempt_jobs or {}
        self.payloads = payloads or {}
        self.calls: list[tuple[str, object]] = []

    def list_runs(self, cursor: str | None) -> dict[str, object]:
        self.calls.append(("runs", cursor))
        return {"items": self.runs, "next": None}

    def list_artifacts(
        self,
        run_id: int,
        cursor: str | None,
    ) -> dict[str, object]:
        self.calls.append(("artifacts", (run_id, cursor)))
        return {"items": self.artifacts.get(run_id, ()), "next": None}

    def list_jobs(
        self,
        run_id: int,
        cursor: str | None,
    ) -> dict[str, object]:
        self.calls.append(("jobs", (run_id, cursor)))
        return {"items": self.jobs.get(run_id, ()), "next": None}

    def list_attempt_jobs(
        self,
        run_id: int,
        run_attempt: int,
        cursor: str | None,
    ) -> dict[str, object]:
        self.calls.append(("attempt-jobs", (run_id, run_attempt, cursor)))
        jobs = self.attempt_jobs.get(
            (run_id, run_attempt),
            self.jobs.get(run_id, ()),
        )
        return {
            "items": jobs,
            "next": None,
        }

    def get_run_attempt(
        self,
        run_id: int,
        run_attempt: int,
    ) -> dict[str, object]:
        self.calls.append(("run-attempt", (run_id, run_attempt)))
        run = next(
            candidate
            for candidate in self.runs
            if candidate.get("id") == run_id
        )
        latest_attempt = run.get("run_attempt", 1)
        if type(latest_attempt) is not int or run_attempt > latest_attempt:
            message = "referenced run attempt does not exist"
            raise RuntimeError(message)
        return {
            "id": run_id,
            "node_id": run.get("node_id", f"WFR_{run_id}"),
            "head_sha": run.get("head_sha", TARGET),
            "run_attempt": run_attempt,
            "status": "completed",
            "conclusion": "success",
        }

    def download_artifact(self, artifact_id: int) -> bytes:
        self.calls.append(("download", artifact_id))
        payload = self.payloads[artifact_id]
        if isinstance(payload, RuntimeError):
            raise payload
        return payload


def _discover(
    client: _DiscoveryClient,
    *,
    current_workflow_run_id: int = 900,
    current_run_attempt: int = 2,
    observed_at: datetime | None = None,
) -> ExecutionHistoryAdmissionSnapshot:
    return discover_execution_history(
        client=client,
        execution=BuddyExecutionIdentity(
            channel="buddy",
            release_unit="hcoona-release-smoke-npm",
            target=TARGET,
        ),
        request_id="release-request:" + ("8" * 64),
        current_workflow_run_id=current_workflow_run_id,
        current_run_attempt=current_run_attempt,
        observed_at=observed_at,
    )


def test_discovery_filters_different_target_runs_without_artifact_or_job_queries() -> (
    None
):
    client = _DiscoveryClient(
        runs=({"id": 910, "head_sha": "4" * 40, "run_attempt": 1},),
    )

    snapshot = _discover(client)

    assert snapshot.records == ()
    assert client.calls == [("runs", None)]


@pytest.mark.parametrize(
    "history_state",
    ["missing", "unrelated", "expired"],
)
def test_recent_successful_binding_formation_requires_retained_binding(
    history_state: str,
) -> None:
    artifact_id = 92101
    artifacts: tuple[dict[str, object], ...] = ()
    payloads: dict[int, bytes | RuntimeError] = {}
    if history_state == "unrelated":
        artifacts = (_listed_artifact(artifact_id),)
        payloads[artifact_id] = b'{"schema":"unrelated"}'
    elif history_state == "expired":
        artifacts = (_listed_artifact(artifact_id, expired=True),)
        payloads[artifact_id] = _binding_payload(
            source_run_id=921,
            run_attempt=1,
        )
    client = _DiscoveryClient(
        runs=({"id": 921, "head_sha": TARGET, "run_attempt": 1},),
        artifacts={921: artifacts},
        attempt_jobs={
            (921, 1): (
                _binding_job(
                    job_id=921001,
                    binding_conclusion="success",
                    completed_at="2026-08-19T12:00:00Z",
                ),
            )
        },
        payloads=payloads,
    )

    with pytest.raises(
        ValueError,
        match="missing an expected non-expired Release Attempt binding",
    ):
        _discover(
            client,
            observed_at=datetime(2026, 8, 20, tzinfo=UTC),
        )

    assert ("run-attempt", (921, 1)) in client.calls
    assert ("attempt-jobs", (921, 1, None)) in client.calls
    if history_state == "expired":
        assert ("download", artifact_id) not in client.calls


def test_recent_successful_admit_job_requires_binding_without_step_fallback() -> (
    None
):
    client = _DiscoveryClient(
        runs=({"id": 922, "head_sha": TARGET, "run_attempt": 1},),
        artifacts={922: ()},
        attempt_jobs={
            (922, 1): (
                {
                    "id": 922001,
                    "phase": "Admit live Attempt and retained history",
                    "status": "completed",
                    "conclusion": "success",
                    "started_at": "2026-08-19T12:00:00Z",
                    "completed_at": "2026-08-19T12:05:00Z",
                    "steps": (),
                },
            )
        },
    )

    with pytest.raises(
        ValueError,
        match="missing an expected non-expired Release Attempt binding",
    ):
        _discover(
            client,
            observed_at=datetime(2026, 8, 20, tzinfo=UTC),
        )


def test_run_stopped_before_binding_formation_remains_skippable() -> None:
    client = _DiscoveryClient(
        runs=({"id": 923, "head_sha": TARGET, "run_attempt": 1},),
        artifacts={923: ()},
        attempt_jobs={
            (923, 1): (
                _binding_job(
                    job_id=923001,
                    binding_conclusion="cancelled",
                    completed_at="2026-08-19T12:00:00Z",
                    job_conclusion="cancelled",
                ),
            )
        },
    )

    snapshot = _discover(
        client,
        observed_at=datetime(2026, 8, 20, tzinfo=UTC),
    )

    assert snapshot.records == ()


def test_missing_binding_after_retention_remains_unavailable_history() -> None:
    client = _DiscoveryClient(
        runs=({"id": 924, "head_sha": TARGET, "run_attempt": 1},),
        artifacts={924: ()},
        attempt_jobs={
            (924, 1): (
                _binding_job(
                    job_id=924001,
                    binding_conclusion="success",
                    completed_at="2026-06-01T12:00:00Z",
                ),
            )
        },
    )

    snapshot = _discover(
        client,
        observed_at=datetime(2026, 8, 20, tzinfo=UTC),
    )

    assert snapshot.records == ()


def test_recent_retained_binding_without_attempt_number_is_not_history() -> (
    None
):
    client = _DiscoveryClient(
        runs=({"id": 925, "head_sha": TARGET, "run_attempt": 1},),
        artifacts={925: (_listed_artifact(92501),)},
        attempt_jobs={
            (925, 1): (
                _binding_job(
                    job_id=925001,
                    binding_conclusion="success",
                    completed_at="2026-08-19T12:00:00Z",
                    job_conclusion="success",
                ),
            )
        },
        payloads={
            92501: _binding_payload(
                source_run_id=925,
                run_attempt=1,
            )
        },
    )

    with pytest.raises(
        ValueError,
        match="recognized history artifact lacks run-attempt selector",
    ):
        _discover(
            client,
            observed_at=datetime(2026, 8, 20, tzinfo=UTC),
        )


def test_attempt_outcome_without_attempt_number_is_not_execution_history() -> (
    None
):
    artifact_id = 92701
    payload = _outcome_payload(source_run_id=927, run_attempt=1)
    digest = f"sha256:{hashlib.sha256(payload).hexdigest()}"
    client = _DiscoveryClient(
        runs=({"id": 927, "head_sha": TARGET, "run_attempt": 1},),
        artifacts={927: (_listed_artifact(artifact_id, digest=digest),)},
        attempt_jobs={(927, 1): ()},
        payloads={artifact_id: payload},
    )

    with pytest.raises(
        ValueError,
        match="recognized history artifact lacks run-attempt selector",
    ):
        _discover(client)


def test_exact_attempt_job_truncation_fails_before_artifact_gating(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    client = _DiscoveryClient(
        runs=({"id": 926, "head_sha": TARGET, "run_attempt": 1},),
        artifacts={926: ()},
    )

    monkeypatch.setattr(
        client,
        "list_attempt_jobs",
        lambda _run_id, _run_attempt, _cursor: {
            "items": (),
            "next": None,
            "complete": False,
        },
    )

    with pytest.raises(
        ValueError,
        match="exact run-attempt proof is missing or invalid",
    ):
        _discover(client)
    assert not any(call[0] == "artifacts" for call in client.calls)


def test_discovery_skips_unrelated_json_non_json_and_multifile_artifacts() -> (
    None
):
    multi_file_archive = _archive_bytes(b"{}", extra_payload=b"{}")
    client = _DiscoveryClient(
        runs=({"id": 911, "head_sha": TARGET, "run_attempt": 1},),
        artifacts={
            911: (
                _listed_artifact(91101),
                _listed_artifact(91102),
                _listed_artifact(
                    91103,
                    digest=(
                        f"sha256:"
                        f"{hashlib.sha256(multi_file_archive).hexdigest()}"
                    ),
                ),
            ),
        },
        payloads={
            91101: b"not json",
            91102: b'{"schema":"workflow-delivery/v3/unrelated"}',
            91103: multi_file_archive,
        },
    )

    snapshot = _discover(client)

    assert snapshot.records == ()
    assert [call[0] for call in client.calls] == [
        "runs",
        "run-attempt",
        "attempt-jobs",
        "artifacts",
        "download",
        "download",
        "download",
    ]


def test_discovery_verifies_archive_digest_before_extracting_payload() -> None:
    record = _history_record(
        artifact_id=91111,
        source_run_id=911,
        run_attempt=1,
    )
    payload = _payload_bytes(record)
    archive = _archive_bytes(payload)
    archive_digest = f"sha256:{hashlib.sha256(archive).hexdigest()}"
    client = _DiscoveryClient(
        runs=({"id": 911, "head_sha": TARGET, "run_attempt": 1},),
        artifacts={911: (_listed_artifact(91111, digest=archive_digest),)},
        attempt_jobs={(911, 1): ()},
        payloads={91111: archive},
    )

    snapshot = _discover(client)

    admitted = snapshot.records[0]
    assert admitted.artifact_digest == archive_digest
    assert admitted.payload_digest == (
        f"sha256:{hashlib.sha256(payload).hexdigest()}"
    )
    assert admitted.artifact_digest != admitted.payload_digest

    client.artifacts[911] = (_listed_artifact(91111, digest=None),)
    fallback = _discover(client)
    assert fallback.records[0].artifact_digest == archive_digest

    client.artifacts[911] = (
        _listed_artifact(91111, digest="sha256:" + ("0" * 64)),
    )
    with pytest.raises(ValueError, match="archive digest mismatch"):
        _discover(client)


@pytest.mark.parametrize(
    "artifact",
    [
        _listed_artifact(91121, digest=False),
        _listed_artifact(91121, digest=0),
        _listed_artifact(91121, digest=""),
        _listed_artifact(91121, digest=[]),
        _listed_artifact(91121, digest="sha256:" + ("A" * 64)),
        _listed_artifact(91121, archive_download_digest=False),
        _listed_artifact(
            91121,
            digest=None,
            archive_download_digest=[],
        ),
    ],
)
def test_discovery_rejects_present_malformed_artifact_digest(
    artifact: dict[str, object],
) -> None:
    client = _DiscoveryClient(
        runs=({"id": 911, "head_sha": TARGET, "run_attempt": 1},),
        artifacts={911: (artifact,)},
        payloads={91121: b"not json"},
    )

    with pytest.raises(ValueError, match="artifact digest"):
        _discover(client)


def test_discovery_retains_expired_artifacts_as_unavailable_history() -> None:
    retained = _history_record(
        artifact_id=91132,
        source_run_id=911,
        run_attempt=1,
    )
    client = _DiscoveryClient(
        runs=({"id": 911, "head_sha": TARGET, "run_attempt": 1},),
        artifacts={
            911: (
                _listed_artifact(91131, expired=True),
                _listed_artifact(91132),
            )
        },
        attempt_jobs={(911, 1): ()},
        payloads={
            91131: RuntimeError("expired artifact must not be downloaded"),
            91132: _payload_bytes(retained),
        },
    )

    snapshot = _discover(client)

    assert tuple(record.artifact_id for record in snapshot.records) == (91132,)
    assert [call for call in client.calls if call[0] == "download"] == [
        ("download", 91132)
    ]


@pytest.mark.parametrize(
    "artifact",
    [
        {"id": 91141},
        {"id": 91141, "expired": None},
        {"id": 91141, "expired": "true"},
        {"id": 91141, "expired": 1},
    ],
)
def test_discovery_rejects_missing_or_malformed_artifact_expiry(
    artifact: dict[str, object],
) -> None:
    client = _DiscoveryClient(
        runs=({"id": 911, "head_sha": TARGET, "run_attempt": 1},),
        artifacts={911: (artifact,)},
        payloads={91141: b"not json"},
    )

    with pytest.raises(ValueError, match="artifact expiry"):
        _discover(client)
    assert ("download", 91141) not in client.calls


def test_discovery_does_not_treat_false_expiry_download_410_as_unavailable() -> (
    None
):
    client = _DiscoveryClient(
        runs=({"id": 911, "head_sha": TARGET, "run_attempt": 1},),
        artifacts={911: (_listed_artifact(91151),)},
        payloads={
            91151: RuntimeError("GitHub REST returned HTTP 410"),
        },
    )

    with pytest.raises(ValueError, match="HTTP 410"):
        _discover(client)


@pytest.mark.parametrize(
    ("payload", "message"),
    [
        (
            b'{"schema":"workflow-delivery/v3/historical-execution-record"}',
            "recognized history artifact payload is malformed",
        ),
        (
            _payload_bytes(
                replace(
                    _history_record(source_run_id=912),
                    execution=BuddyExecutionIdentity(
                        channel="buddy",
                        release_unit="hcoona-release-smoke-npm",
                        target="5" * 40,
                    ),
                    source_head_sha="5" * 40,
                )
            ),
            "recognized history artifact payload conflicts",
        ),
        (
            _payload_bytes(
                replace(
                    _history_record(source_run_id=912),
                    diagnostic_claims=(
                        ("producer", "self-asserted"),
                        ("purpose", "release-simulation"),
                    ),
                )
            ),
            "purpose conflicts",
        ),
    ],
)
def test_discovery_fails_recognized_malformed_or_conflicting_history_schemas(
    payload: bytes,
    message: str,
) -> None:
    client = _DiscoveryClient(
        runs=({"id": 912, "head_sha": TARGET, "run_attempt": 1},),
        artifacts={912: (_listed_artifact(91201),)},
        payloads={91201: payload},
    )

    with pytest.raises(ValueError, match=message):
        _discover(client)


@pytest.mark.parametrize(
    ("jobs", "expected_job_id", "expected_phase"),
    [
        (
            (
                {
                    "id": 913001,
                    "phase": "release-finalizer",
                    "conclusion": "success",
                },
                {
                    "id": 913002,
                    "phase": "release-finalizer",
                    "conclusion": "success",
                },
            ),
            None,
            None,
        ),
        (
            ({"id": 913003, "phase": "quality", "conclusion": "success"},),
            None,
            None,
        ),
        (
            (
                {
                    "id": 913004,
                    "phase": "publish-github-packages",
                    "conclusion": "cancelled",
                },
            ),
            913004,
            "publish-github-packages",
        ),
    ],
)
def test_discovery_retains_optional_terminal_phase_facts(
    jobs: tuple[dict[str, object], ...],
    expected_job_id: int | None,
    expected_phase: str | None,
) -> None:
    record = _history_record(
        artifact_id=91301,
        source_run_id=913,
        run_attempt=1,
    )
    client = _DiscoveryClient(
        runs=({"id": 913, "head_sha": TARGET, "run_attempt": 1},),
        artifacts={913: (_listed_artifact(91301),)},
        jobs={913: jobs},
        payloads={91301: _payload_bytes(record)},
    )

    if len(jobs) == DUPLICATE_JOB_COUNT:
        with pytest.raises(ValueError, match="duplicate finalizer"):
            _discover(client)
        return
    snapshot = _discover(client)
    assert snapshot.records[0].queried_job_id == expected_job_id
    assert snapshot.records[0].queried_phase == expected_phase


@pytest.mark.parametrize(
    ("phase", "normalized"),
    [
        (
            (
                "Run same-revision Buddy live Attempt / "
                "Finalize live Attempt outcome"
            ),
            "Finalize live Attempt outcome",
        ),
        (
            "Run same-revision Buddy live Attempt / Publish to GitHub Packages",
            "Publish to GitHub Packages",
        ),
    ],
)
def test_discovery_normalizes_exact_yaml_job_display_names(
    phase: str,
    normalized: str,
) -> None:
    record = _history_record(
        artifact_id=91601,
        source_run_id=916,
        run_attempt=1,
    )
    client = _DiscoveryClient(
        runs=({"id": 916, "head_sha": TARGET, "run_attempt": 1},),
        artifacts={916: (_listed_artifact(91601),)},
        jobs={
            916: ({"id": 916001, "phase": phase, "conclusion": "cancelled"},)
        },
        payloads={91601: _payload_bytes(record)},
    )

    snapshot = _discover(client)

    assert snapshot.records[0].queried_phase == normalized
    assert snapshot.records[0].queried_job_conclusion == "cancelled"


def test_same_run_queries_every_prior_attempt_once() -> None:
    prior = _history_record(
        artifact_id=91701,
        source_run_id=917,
        run_attempt=PRIOR_ATTEMPT,
    )
    client = _DiscoveryClient(
        runs=({"id": 917, "head_sha": TARGET, "run_attempt": 3},),
        artifacts={917: (_listed_artifact(91701),)},
        attempt_jobs={(917, PRIOR_ATTEMPT): ()},
        payloads={91701: _payload_bytes(prior)},
    )

    snapshot = _discover(
        client,
        current_workflow_run_id=917,
        current_run_attempt=3,
    )

    assert snapshot.records[0].queried_run_attempt == PRIOR_ATTEMPT
    assert snapshot.records[0].queried_job_id is None
    assert [call for call in client.calls if call[0] == "attempt-jobs"] == [
        ("attempt-jobs", (917, 1, None)),
        ("attempt-jobs", (917, PRIOR_ATTEMPT, None)),
    ]
    assert [call for call in client.calls if call[0] == "run-attempt"] == [
        ("run-attempt", (917, 1)),
        ("run-attempt", (917, PRIOR_ATTEMPT)),
    ]


def test_same_run_prior_attempt_enumerates_current_artifacts_without_attempt_provenance() -> (
    None
):
    prior = _history_record(
        artifact_id=91401,
        source_run_id=914,
        run_attempt=1,
    )
    current = _history_record(
        artifact_id=91402,
        source_run_id=914,
        run_attempt=2,
    )
    client = _DiscoveryClient(
        runs=({"id": 914, "head_sha": TARGET, "run_attempt": 2},),
        artifacts={
            914: (
                _listed_artifact(91401),
                _listed_artifact(91402),
            )
        },
        jobs={
            914: (
                {
                    "id": 914001,
                    "phase": "release-finalizer",
                    "conclusion": "success",
                },
            ),
        },
        attempt_jobs={
            (914, 1): (
                {
                    "id": 913001,
                    "phase": "release-finalizer",
                    "conclusion": "success",
                },
            ),
        },
        payloads={
            91401: _payload_bytes(prior),
            91402: _payload_bytes(current),
        },
    )

    snapshot = _discover(
        client,
        current_workflow_run_id=914,
        current_run_attempt=2,
    )

    assert [call for call in client.calls if call[0] == "artifacts"] == [
        ("artifacts", (914, None))
    ]
    assert [call for call in client.calls if call[0] == "attempt-jobs"] == [
        ("attempt-jobs", (914, 1, None))
    ]
    assert [call for call in client.calls if call[0] == "download"] == [
        ("download", 91401),
        ("download", 91402),
    ]
    assert tuple(record.artifact_id for record in snapshot.records) == (91401,)
    assert snapshot.records[0].queried_run_attempt == 1
    document = snapshot.records[0].to_document()
    assert "artifact-run-attempt" not in document
    assert "artifact-job-id" not in document
    assert "reusable-workflow-provenance" not in document
    assert ("reusable-workflow", "diagnostic-only.yml") in snapshot.records[
        0
    ].diagnostic_claims


def test_same_run_prior_attempt_fails_closed_when_run_level_proof_is_missing() -> (
    None
):
    prior = _history_record(
        artifact_id=91501,
        source_run_id=915,
        run_attempt=2,
    )
    client = _DiscoveryClient(
        runs=({"id": 915, "head_sha": TARGET, "run_attempt": 1},),
        artifacts={915: (_listed_artifact(91501),)},
        payloads={91501: _payload_bytes(prior)},
    )

    with pytest.raises(ValueError, match="exceeds latest watermark"):
        _discover(
            client,
            current_workflow_run_id=915,
            current_run_attempt=3,
        )


def test_noncurrent_run_uses_each_declared_attempt_and_caches_each_key() -> (
    None
):
    attempt1 = _history_record(
        artifact_id=91801,
        source_run_id=918,
        run_attempt=1,
    )
    attempt3 = _history_record(
        artifact_id=91802,
        source_run_id=918,
        run_attempt=3,
    )
    client = _DiscoveryClient(
        runs=(
            {
                "id": 918,
                "node_id": "WFR_918",
                "head_sha": TARGET,
                "run_attempt": 3,
            },
        ),
        artifacts={
            918: (
                _listed_artifact(91801),
                _listed_artifact(91802),
                _listed_artifact(91803),
            ),
        },
        attempt_jobs={
            (918, 1): (
                {
                    "id": 918001,
                    "phase": "Finalize live Attempt outcome",
                    "conclusion": "success",
                },
            ),
            (918, 3): (
                {
                    "id": 918003,
                    "phase": "Finalize live Attempt outcome",
                    "conclusion": "success",
                },
            ),
        },
        payloads={
            91801: _payload_bytes(attempt1),
            91802: _payload_bytes(attempt3),
            91803: _payload_bytes(replace(attempt1, artifact_id=91803)),
        },
    )

    snapshot = _discover(client)

    assert tuple(record.queried_run_attempt for record in snapshot.records) == (
        1,
        1,
        3,
    )
    assert [call for call in client.calls if call[0] == "run-attempt"] == [
        ("run-attempt", (918, 1)),
        ("run-attempt", (918, 2)),
        ("run-attempt", (918, 3)),
    ]
    assert [call for call in client.calls if call[0] == "attempt-jobs"] == [
        ("attempt-jobs", (918, 1, None)),
        ("attempt-jobs", (918, 2, None)),
        ("attempt-jobs", (918, 3, None)),
    ]


@pytest.mark.parametrize(
    ("changed", "message"),
    [
        ({"id": 920}, "conflicts"),
        ({"node_id": "WFR_other"}, "conflicts"),
        ({"head_sha": "9" * 40}, "conflicts"),
        ({"run_attempt": 2}, "conflicts"),
        ({"status": None}, "malformed"),
    ],
)
def test_exact_run_attempt_fact_mismatch_fails_closed(
    monkeypatch: pytest.MonkeyPatch,
    changed: dict[str, object],
    message: str,
) -> None:
    record = _history_record(
        artifact_id=91901,
        source_run_id=919,
        run_attempt=1,
    )
    client = _DiscoveryClient(
        runs=(
            {
                "id": 919,
                "node_id": "WFR_919",
                "head_sha": TARGET,
                "run_attempt": 1,
            },
        ),
        artifacts={919: (_listed_artifact(91901),)},
        attempt_jobs={(919, 1): ()},
        payloads={91901: _payload_bytes(record)},
    )
    original = client.get_run_attempt

    def changed_fact(run_id: int, run_attempt: int) -> dict[str, object]:
        return {**original(run_id, run_attempt), **changed}

    monkeypatch.setattr(client, "get_run_attempt", changed_fact)

    with pytest.raises(
        ValueError,
        match="exact run-attempt proof is missing or invalid",
    ) as error:
        _discover(client)
    assert message in str(error.value.__cause__)


def test_history_snapshot_sorts_records_and_round_trips_closed_schema() -> None:
    execution = _history_record().execution
    later = _history_record(artifact_id=1002, source_run_id=601)
    earlier = _history_record(artifact_id=1001, source_run_id=600)

    snapshot = form_execution_history_admission_snapshot(
        authority="execution-history",
        request_id="release-request:" + ("8" * 64),
        current_workflow_run_id=700,
        current_run_attempt=2,
        execution=execution,
        query_basis=("workflow:runs", "run:jobs", "run:artifacts"),
        pagination_basis=("runs:exhausted", "jobs:exhausted"),
        records=(later, earlier),
        queries_complete=True,
        pagination_complete=True,
        malformed_results=False,
        expected_result_count=2,
        attempt_created=False,
    )
    admitted = release_record_from_document(
        snapshot.to_document(),
        expected_type=ExecutionHistoryAdmissionSnapshot,
    )

    assert snapshot.records == (earlier, later)
    assert admitted == snapshot
    assert snapshot.to_document()["authority"] == "execution-history"
    assert all(record.history_only for record in snapshot.records)

    substituted_authority = snapshot.to_document()
    substituted_authority["authority"] = "current-authority"
    with pytest.raises(ValueError, match="wrong authority"):
        release_record_from_document(
            substituted_authority,
            expected_type=ExecutionHistoryAdmissionSnapshot,
        )


def test_history_snapshot_admission_binds_complete_current_identity() -> None:
    record = _history_record()
    snapshot = form_execution_history_admission_snapshot(
        authority="execution-history",
        request_id="release-request:" + ("8" * 64),
        current_workflow_run_id=700,
        current_run_attempt=2,
        execution=record.execution,
        query_basis=("workflow:runs", "run:jobs", "run:artifacts"),
        pagination_basis=("runs:exhausted", "jobs:exhausted"),
        records=(record,),
        queries_complete=True,
        pagination_complete=True,
        malformed_results=False,
        expected_result_count=1,
        attempt_created=False,
    )
    bindings = ReleaseAdmissionBindings(
        purpose="live-release",
        workflow_run_id=snapshot.current_workflow_run_id,
        run_attempt=None,
        target=snapshot.execution.target,
        request_id=snapshot.request_id,
        execution=snapshot.execution,
    )
    canonical_bytes = canonicalize(snapshot.to_document())

    admitted = admit_release_record(
        canonical_bytes,
        expected_type=ExecutionHistoryAdmissionSnapshot,
        expected_digest=snapshot.snapshot_digest,
        expected_bindings=bindings,
    )

    assert admitted == snapshot
    for changed, field in (
        (
            replace(
                bindings,
                request_id="release-request:" + ("9" * 64),
            ),
            "request_id",
        ),
        (
            replace(
                bindings,
                execution=replace(
                    snapshot.execution,
                    release_unit="other-release-unit",
                ),
            ),
            "execution",
        ),
        (
            replace(
                bindings,
                workflow_run_id=bindings.workflow_run_id + 1,
            ),
            "workflow_run_id",
        ),
        (replace(bindings, target="f" * 40), "target"),
    ):
        with pytest.raises(ValueError, match=field):
            admit_release_record(
                canonical_bytes,
                expected_type=ExecutionHistoryAdmissionSnapshot,
                expected_digest=snapshot.snapshot_digest,
                expected_bindings=changed,
            )

    for incomplete, field in (
        (replace(bindings, request_id=None), "request_id"),
        (replace(bindings, execution=None), "execution"),
    ):
        with pytest.raises(ValueError, match=field):
            admit_release_record(
                canonical_bytes,
                expected_type=ExecutionHistoryAdmissionSnapshot,
                expected_digest=snapshot.snapshot_digest,
                expected_bindings=incomplete,
            )


def test_historical_record_rejects_independent_transport_substitution() -> None:
    record = _history_record()

    with pytest.raises(ValueError, match="artifact_digest"):
        replace(record, artifact_digest="not-a-digest")


@pytest.mark.parametrize(
    ("changes", "message"),
    [
        ({"authority": "current-authority"}, "caller-selected"),
        ({"queries_complete": False}, "truncated or incomplete"),
        ({"pagination_complete": False}, "truncated or incomplete"),
        ({"malformed_results": True}, "malformed"),
        ({"expected_result_count": 2}, "count is incomplete"),
        ({"attempt_created": True}, "after Attempt creation"),
    ],
)
def test_history_snapshot_rejects_incomplete_or_substituted_query_results(
    changes: dict[str, object],
    message: str,
) -> None:
    record = _history_record()
    arguments: dict[str, object] = {
        "authority": "execution-history",
        "request_id": "release-request:" + ("8" * 64),
        "current_workflow_run_id": 700,
        "current_run_attempt": 2,
        "execution": record.execution,
        "query_basis": ("workflow:runs",),
        "pagination_basis": ("runs:exhausted",),
        "records": (record,),
        "queries_complete": True,
        "pagination_complete": True,
        "malformed_results": False,
        "expected_result_count": 1,
        "attempt_created": False,
    }
    arguments.update(changes)

    with pytest.raises((TypeError, ValueError), match=message):
        form_execution_history_admission_snapshot(**arguments)  # type: ignore[arg-type]


def test_history_snapshot_rejects_duplicate_cross_execution_and_target() -> (
    None
):
    record = _history_record()
    common = {
        "authority": "execution-history",
        "request_id": "release-request:" + ("8" * 64),
        "current_workflow_run_id": 700,
        "current_run_attempt": 2,
        "execution": record.execution,
        "query_basis": ("workflow:runs",),
        "pagination_basis": ("runs:exhausted",),
        "queries_complete": True,
        "pagination_complete": True,
        "malformed_results": False,
        "attempt_created": False,
    }
    with pytest.raises(ValueError, match="duplicate"):
        form_execution_history_admission_snapshot(
            **common,
            records=(record, record),
            expected_result_count=2,
        )
    other_execution = replace(
        record.execution,
        release_unit="other-release-unit",
    )
    cross_execution = replace(record, execution=other_execution)
    with pytest.raises(ValueError, match="cross-Execution"):
        form_execution_history_admission_snapshot(
            **common,
            records=(cross_execution,),
            expected_result_count=1,
        )


def test_same_run_prior_attempt_remains_history_only_without_provenance_claims() -> (
    None
):
    record = _history_record(source_run_id=700, run_attempt=1)

    snapshot = form_execution_history_admission_snapshot(
        authority="execution-history",
        request_id="release-request:" + ("8" * 64),
        current_workflow_run_id=700,
        current_run_attempt=2,
        execution=record.execution,
        query_basis=("workflow:runs",),
        pagination_basis=("runs:exhausted",),
        records=(record,),
        queries_complete=True,
        pagination_complete=True,
        malformed_results=False,
        expected_result_count=1,
        attempt_created=False,
        verified_prior_attempts=(1,),
    )

    document = snapshot.records[0].to_document()
    assert document["history-only"] is True
    assert "artifact-job-id" not in document
    assert "artifact-run-attempt" not in document
    assert document["diagnostic-claims"] != []


def test_attempt_binding_requires_exact_live_intent_and_execution() -> None:
    intent = normalize_buddy_live_intent(
        repository="hcoona/three",
        selected_ref="refs/heads/feature",
        target=TARGET,
        actor="reviewed-actor",
        workflow_run_id=700,
    )
    execution = derive_buddy_execution_identity(intent)
    provenance = (
        ("blob-oid", "blob"),
        ("content-sha256", "sha256:" + ("9" * 64)),
        ("path", ".github/governance.json"),
        ("ref", "refs/heads/main"),
        ("repository", "hcoona/three"),
        ("resolved-commit", TARGET),
    )

    binding = derive_release_attempt_binding(
        intent=intent,
        execution=execution,
        repository_model_digest="sha256:" + ("a" * 64),
        live_eligibility_artifact_id=1100,
        live_eligibility_artifact_digest="sha256:" + ("b" * 64),
        live_eligibility_payload_digest="sha256:" + ("c" * 64),
        attestation_provenance=provenance,
    )

    assert binding.attempt.execution == execution
    assert binding.request_id == intent.request_id
    assert binding.attempt.workflow_run_id == intent.workflow_run_id
    assert "history-snapshot-artifact-id" not in binding.to_document()
    with pytest.raises(ValueError, match="pre-Attempt admission mismatch"):
        derive_release_attempt_binding(
            intent=intent,
            execution=replace(execution, target="f" * 40),
            repository_model_digest=binding.repository_model_digest,
            live_eligibility_artifact_id=binding.live_eligibility_artifact_id,
            live_eligibility_artifact_digest=(
                binding.live_eligibility_artifact_digest
            ),
            live_eligibility_payload_digest=(
                binding.live_eligibility_payload_digest
            ),
            attestation_provenance=provenance,
        )
