"""Pure commit-8 live admission and finalization helpers."""

# ruff: noqa: EM101, PLC0415, PLR0913, TRY003

from __future__ import annotations

import hashlib
import json
import re
from dataclasses import dataclass
from datetime import UTC, datetime, timedelta
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3.platform.github import (
    GitHubActionsHistoryClient,
    GitHubArtifact,
    GitHubArtifactArchiveShapeError,
    GitHubJob,
    GitHubJobStep,
    GitHubRun,
    GitHubRunAttemptFact,
    admit_artifact_download,
    iter_all_artifacts,
    iter_all_attempt_jobs,
    iter_all_runs,
)
from three_workflow_delivery_v3.records.release import (
    ATTEMPT_OUTCOME_SCHEMA,
    CAPABILITY_ADMISSION_DECISION_SCHEMA,
    CAPABILITY_GROUP_RESULT_BUNDLE_SCHEMA,
    HISTORICAL_EXECUTION_RECORD_SCHEMA,
    PUBLISHER_GOVERNANCE_RECHECK_FAILED_BEFORE_RUNNER,
    RECEIPT_SCHEMA,
    RELEASE_ATTEMPT_BINDING_SCHEMA,
    ActionResult,
    AttemptOutcome,
    AuthorizationRecord,
    BuddyExecutionIdentity,
    CapabilityAdmissionDecision,
    CapabilityGroupResultBundle,
    ExecutionHistoryAdmissionSnapshot,
    HistoricalExecutionRecord,
    ProjectionObservation,
    PublicationSnapshot,
    QualificationDecision,
    Receipt,
    ReceiptTransportReference,
    ReleaseAttemptBinding,
    ReleaseAttemptIdentity,
)
from three_workflow_delivery_v3.records.release_transport import (
    release_record_from_document,
)

if TYPE_CHECKING:
    from collections.abc import Callable

    from three_workflow_delivery_v3.canonical import JsonValue

_SHA_PATTERN = re.compile(r"[0-9a-f]{40}\Z")
_DIGEST_PATTERN = re.compile(r"sha256:[0-9a-f]{64}\Z")
_PUBLIC_REPOSITORY_URL = "https://github.com/hcoona/three.git"
_DEFAULT_CONTROL = "control:" + ("0" * 64)
_DEFAULT_REQUEST = "release-request:" + ("0" * 64)
_DEFAULT_TARGET = "a" * 40
_DEFAULT_EXPIRES_AT = "2026-09-01T00:00:00Z"
_DEFAULT_OBSERVED_AT = datetime(2026, 8, 13, 0, 0, 0, tzinfo=UTC)
_GOVERNANCE_PROVENANCE_FIELDS = frozenset(
    {
        "repository",
        "ref",
        "path",
        "resolved-commit",
        "blob-oid",
        "content-sha256",
    }
)
_SUBSTITUTION_DIAGNOSTICS = {
    "disabled": "governance-live-disabled",
    "expired": "governance-attestation-expired",
    "resolved-commit": "governance-provenance-changed",
    "blob": "governance-provenance-changed",
    "content": "governance-content-changed",
    "binding": "governance-binding-changed",
}
_HISTORY_FINALIZER_PHASES = frozenset(
    {
        "Finalize live Attempt outcome",
        "release-finalizer",
        "finalizer",
        "finalized",
    }
)
_HISTORY_PUBLISHER_PHASES = frozenset(
    {
        "Publish to GitHub Packages",
        "publish-github-packages",
        "publisher",
        "publish",
    }
)
_HISTORY_BINDING_JOB_PHASE = "Admit live Attempt and retained history"
_HISTORY_BINDING_STEP = "Bind current live Attempt"
_HISTORY_RETENTION_WINDOW = timedelta(days=45)
_HISTORICAL_SCHEMA_TYPES = {
    HISTORICAL_EXECUTION_RECORD_SCHEMA: HistoricalExecutionRecord,
    RELEASE_ATTEMPT_BINDING_SCHEMA: ReleaseAttemptBinding,
    ATTEMPT_OUTCOME_SCHEMA: AttemptOutcome,
    CAPABILITY_ADMISSION_DECISION_SCHEMA: CapabilityAdmissionDecision,
    CAPABILITY_GROUP_RESULT_BUNDLE_SCHEMA: CapabilityGroupResultBundle,
    RECEIPT_SCHEMA: Receipt,
}


@dataclass(frozen=True, slots=True)
class ReviewerArtifact:
    """Exact reviewer-summary transport and immutable bytes."""

    snapshot_bytes: bytes
    summary_bytes: bytes
    snapshot_payload_digest: str
    summary_payload_digest: str
    artifact_id: int
    upload_digest: str


@dataclass(frozen=True, slots=True)
class ReviewerPayload:
    """Credential-free immutable reviewer payload closure."""

    snapshot_bytes: bytes
    summary_bytes: bytes
    snapshot_payload_digest: str
    summary_payload_digest: str

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical formatter input carried across the approval."""
        import base64

        return {
            "schema": "workflow-delivery/v3/reviewer-formatter-input",
            "snapshot-base64": base64.b64encode(self.snapshot_bytes).decode(),
            "summary-base64": base64.b64encode(self.summary_bytes).decode(),
            "snapshot-payload-digest": self.snapshot_payload_digest,
            "summary-payload-digest": self.summary_payload_digest,
        }


@dataclass(frozen=True, slots=True)
class PublicRevisionCheckout:
    """Anonymous exact public revision materialization facts."""

    target: str
    repository_url: str
    head: str
    detached: bool
    commands: tuple[tuple[str, ...], ...]


@dataclass(frozen=True, slots=True)
class LiveCapabilityAdmissionResult:
    """Current blocked admission plus restored new-attempt admission."""

    current_attempt: CapabilityAdmissionDecision
    restored_attempt: CapabilityAdmissionDecision


def _sha256_bytes(payload: bytes) -> str:
    return f"sha256:{hashlib.sha256(payload).hexdigest()}"


def _require_digest(value: str, *, field: str) -> str:
    if type(value) is not str or _DIGEST_PATTERN.fullmatch(value) is None:
        message = f"{field} must be a prefixed lowercase SHA-256"
        raise ValueError(message)
    return value


def _require_positive_integer(value: int, *, field: str) -> int:
    if type(value) is not int or value <= 0:
        message = f"{field} must be a positive integer"
        raise ValueError(message)
    return value


def _parse_utc(value: str, *, field: str) -> datetime:
    if type(value) is not str:
        message = f"{field} must be a UTC instant"
        raise TypeError(message)
    try:
        return datetime.strptime(value, "%Y-%m-%dT%H:%M:%SZ").replace(
            tzinfo=UTC
        )
    except ValueError as error:
        message = f"{field} must be a UTC second-precision instant"
        raise ValueError(message) from error


def _governance_diagnostics(
    *,
    provenance: tuple[tuple[str, str], ...],
    content_sha256: str,
    expires_at: str,
    live_enabled: bool,
    observed_at: datetime,
    expected_provenance: tuple[tuple[str, str], ...] | None,
    expected_content_sha256: str | None,
    expected_expires_at: str | None,
    expected_live_enabled: bool | None,
) -> tuple[str, ...]:
    if observed_at.tzinfo is None or observed_at.utcoffset() is None:
        message = "fresh Governance observation time must be timezone-aware"
        raise ValueError(message)
    diagnostics: list[str] = []
    if {name for name, _ in provenance} != _GOVERNANCE_PROVENANCE_FIELDS:
        diagnostics.append("governance-provenance-changed")
    else:
        values = dict(provenance)
        if values["content-sha256"] != content_sha256:
            diagnostics.append("governance-content-changed")
    if expected_provenance is not None and provenance != expected_provenance:
        diagnostics.append("governance-provenance-changed")
    if (
        expected_content_sha256 is not None
        and content_sha256 != expected_content_sha256
    ):
        diagnostics.append("governance-content-changed")
    if expected_expires_at is not None and expires_at != expected_expires_at:
        diagnostics.append("governance-binding-changed")
    if (
        expected_live_enabled is not None
        and live_enabled is not expected_live_enabled
    ):
        diagnostics.append("governance-binding-changed")
    if not live_enabled:
        diagnostics.append("governance-live-disabled")
    if _parse_utc(expires_at, field="governance_expires_at") <= observed_at:
        diagnostics.append("governance-attestation-expired")
    return tuple(dict.fromkeys(diagnostics))


def _metadata(item: object) -> tuple[tuple[str, str], ...]:
    if isinstance(item, GitHubRun | GitHubArtifact | GitHubJob):
        return tuple(sorted(item.metadata))
    if type(item) is not dict:
        return ()
    pairs: list[tuple[str, str]] = []
    for key, value in item.items():
        if isinstance(value, str | int | bool):
            pairs.append((key.replace("_", "-"), str(value).lower()))
    return tuple(sorted(pairs))


def _integer_field(item: object, *names: str, context: str) -> int:
    for name in names:
        if hasattr(item, name):
            value = getattr(item, name)
        elif type(item) is dict:
            value = item.get(name)
        else:
            continue
        if type(value) is int and value > 0:
            return value
    message = f"{context} malformed"
    raise ValueError(message)


def _string_field(
    item: object,
    *names: str,
    default: str | None = None,
    context: str,
) -> str:
    for name in names:
        if hasattr(item, name):
            value = getattr(item, name)
        elif type(item) is dict:
            value = item.get(name)
        else:
            continue
        if type(value) is str and value:
            return value
    if default is not None:
        return default
    message = f"{context} malformed"
    raise ValueError(message)


def _run_attempt(item: object) -> int:
    if isinstance(item, GitHubRun):
        value = item.run_attempt
    elif type(item) is dict:
        value = item.get("run_attempt", 1)
    else:
        value = 1
    if type(value) is not int or value <= 0:
        message = "run run_attempt malformed"
        raise ValueError(message)
    return value


def _run_attempt_fact(
    item: object,
    *,
    run_id: int,
    run_attempt: int,
    listed_node_id: str,
    head_sha: str,
) -> GitHubRunAttemptFact:
    if isinstance(item, GitHubRunAttemptFact):
        fact = item
    elif type(item) is dict:
        conclusion = item.get("conclusion")
        metadata = item.get("metadata", ())
        if type(metadata) is dict:
            metadata = tuple(
                sorted(
                    (str(key), str(value)) for key, value in metadata.items()
                )
            )
        if (
            type(item.get("id", item.get("run_id"))) is not int
            or type(item.get("run_attempt")) is not int
            or type(item.get("node_id")) is not str
            or type(item.get("head_sha")) is not str
            or type(item.get("status")) is not str
            or not item.get("status")
            or (conclusion is not None and type(conclusion) is not str)
            or type(metadata) is not tuple
            or any(
                type(pair) is not tuple
                or len(pair) != len(("key", "value"))
                or any(type(value) is not str for value in pair)
                for pair in metadata
            )
        ):
            raise ValueError("exact run-attempt fact is malformed")
        fact = GitHubRunAttemptFact(
            run_id=cast("int", item.get("id", item.get("run_id"))),
            node_id=cast("str", item["node_id"]),
            head_sha=cast("str", item["head_sha"]),
            run_attempt=cast("int", item["run_attempt"]),
            status=cast("str", item["status"]),
            conclusion=cast("str | None", conclusion),
            metadata=cast("tuple[tuple[str, str], ...]", metadata),
        )
    else:
        raise ValueError("exact run-attempt fact is malformed")
    if (
        fact.run_id != run_id
        or fact.run_attempt != run_attempt
        or fact.node_id != listed_node_id
        or fact.head_sha != head_sha
    ):
        raise ValueError("exact run-attempt fact conflicts")
    return fact


def _listed_artifact_digest(item: object) -> str | None:
    if isinstance(item, GitHubArtifact):
        value: object | None = item.upload_digest
    elif type(item) is dict:
        primary = item.get("digest")
        value = (
            primary
            if primary is not None
            else item.get("archive_download_digest")
        )
    else:
        value = getattr(item, "upload_digest", None)
    if value is None:
        return None
    if type(value) is not str:
        raise ValueError("artifact digest is malformed")
    return _require_digest(value, field="artifact digest")


def _artifact_expired(item: object) -> bool:
    if isinstance(item, GitHubArtifact):
        value: object = item.expired
    elif type(item) is dict:
        if "expired" not in item:
            raise ValueError("artifact expiry is missing")
        value = item["expired"]
    else:
        value = getattr(item, "expired", None)
    if type(value) is not bool:
        raise ValueError("artifact expiry is malformed")
    return value


@dataclass(frozen=True, slots=True)
class _HistoricalPayloadFacts:
    diagnostic_claims: tuple[tuple[str, str], ...]
    declared_run_attempt: int | None
    attempt_binding: bool


def _record_attempt(record: object) -> ReleaseAttemptIdentity | None:
    attempt = getattr(record, "attempt", None)
    if isinstance(attempt, ReleaseAttemptIdentity):
        return attempt
    return None


def _historical_payload_facts(  # noqa: C901
    payload: bytes,
    *,
    execution: BuddyExecutionIdentity,
    source_workflow_run_id: int,
) -> _HistoricalPayloadFacts | None:
    try:
        parsed = json.loads(payload.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError):
        return None
    if type(parsed) is not dict:
        return None
    schema = parsed.get("schema")
    if type(schema) is not str or schema not in _HISTORICAL_SCHEMA_TYPES:
        return None
    expected_type = _HISTORICAL_SCHEMA_TYPES[schema]
    try:
        record = release_record_from_document(
            parsed,
            expected_type=expected_type,
        )
    except (TypeError, ValueError) as error:
        message = "recognized history artifact payload is malformed"
        raise ValueError(message) from error
    if isinstance(record, HistoricalExecutionRecord):
        if (
            record.execution != execution
            or record.source_head_sha != execution.target
        ):
            message = "recognized history artifact payload conflicts"
            raise ValueError(message)
        claims = dict(record.diagnostic_claims)
        if claims.get("purpose", "live-release") != "live-release":
            message = "recognized history artifact payload purpose conflicts"
            raise ValueError(message)
        return _HistoricalPayloadFacts(
            diagnostic_claims=record.diagnostic_claims,
            declared_run_attempt=record.queried_run_attempt,
            attempt_binding=False,
        )
    if isinstance(record, ReleaseAttemptBinding):
        if (
            record.execution != execution
            or record.attempt.execution != execution
            or record.attempt.workflow_run_id != source_workflow_run_id
        ):
            message = "recognized history artifact payload conflicts"
            raise ValueError(message)
        return _HistoricalPayloadFacts(
            diagnostic_claims=(),
            declared_run_attempt=record.attempt.run_attempt,
            attempt_binding=True,
        )
    attempt = _record_attempt(record)
    if attempt is None or attempt.execution != execution:
        message = "recognized history artifact payload conflicts"
        raise ValueError(message)
    return _HistoricalPayloadFacts(
        diagnostic_claims=(),
        declared_run_attempt=attempt.run_attempt,
        attempt_binding=False,
    )


def _normalized_history_phase(phase: str) -> str:
    for expected in (
        _HISTORY_BINDING_JOB_PHASE,
        "Finalize live Attempt outcome",
        "Publish to GitHub Packages",
    ):
        if phase == expected or phase.endswith(f" / {expected}"):
            return expected
    return phase


def _job_steps(job: object) -> tuple[GitHubJobStep, ...]:
    if isinstance(job, GitHubJob):
        return job.steps
    if type(job) is not dict or type(job.get("steps")) not in {tuple, list}:
        raise ValueError("job steps are malformed")
    normalized: list[GitHubJobStep] = []
    for item in job["steps"]:
        if type(item) is not dict:
            raise ValueError("job step is malformed")
        name = item.get("name")
        status = item.get("status")
        conclusion = item.get("conclusion")
        started_at = item.get("started_at")
        completed_at = item.get("completed_at")
        if (
            type(name) is not str
            or not name
            or type(status) is not str
            or not status
            or (conclusion is not None and type(conclusion) is not str)
            or (started_at is not None and type(started_at) is not str)
            or (completed_at is not None and type(completed_at) is not str)
        ):
            raise ValueError("job step is malformed")
        normalized.append(
            GitHubJobStep(
                name=name,
                status=status,
                conclusion=cast("str | None", conclusion),
                started_at=cast("str | None", started_at),
                completed_at=cast("str | None", completed_at),
            )
        )
    return tuple(normalized)


def _job_completed_at(job: object) -> str | None:
    if isinstance(job, GitHubJob):
        value: object = job.completed_at
    elif type(job) is dict and "completed_at" in job:
        value = job["completed_at"]
    else:
        raise ValueError("job completion timestamp is missing")
    if value is not None and type(value) is not str:
        raise ValueError("job completion timestamp is malformed")
    return cast("str | None", value)


def _recent_binding_expected(
    jobs: tuple[object, ...],
    *,
    observed_at: datetime,
) -> bool:
    admit_jobs = [
        job
        for job in jobs
        if _normalized_history_phase(
            _string_field(job, "phase", "name", context="job")
        )
        == _HISTORY_BINDING_JOB_PHASE
    ]
    if len(admit_jobs) > 1:
        raise ValueError("duplicate binding-admission job")
    if not admit_jobs:
        return False
    job = admit_jobs[0]
    binding_steps = [
        step for step in _job_steps(job) if step.name == _HISTORY_BINDING_STEP
    ]
    if len(binding_steps) > 1:
        raise ValueError("duplicate binding-formation step")
    formed_at: str | None = None
    if binding_steps and binding_steps[0].conclusion == "success":
        formed_at = binding_steps[0].completed_at
        if formed_at is None:
            raise ValueError("binding formation completion time is missing")
    elif (
        _string_field(
            job,
            "conclusion",
            default="unknown",
            context="job",
        )
        == "success"
    ):
        formed_at = _job_completed_at(job)
        if formed_at is None:
            raise ValueError("binding job completion time is missing")
    if formed_at is None:
        return False
    formed = _parse_utc(formed_at, field="binding_formed_at")
    if formed > observed_at:
        raise ValueError("binding formation time is later than observation")
    return observed_at - formed < _HISTORY_RETENTION_WINDOW


def _select_job(
    jobs: tuple[object, ...],
) -> tuple[object | None, tuple[tuple[str, str], ...]]:
    by_id: dict[int, object] = {}
    finalizers: list[object] = []
    publishers: list[object] = []
    for job in jobs:
        job_id = _integer_field(job, "id", "job_id", context="job")
        if job_id in by_id:
            message = "duplicate job"
            raise ValueError(message)
        by_id[job_id] = job
        phase = _normalized_history_phase(
            _string_field(
                job,
                "phase",
                "name",
                default="",
                context="job",
            )
        )
        if phase in _HISTORY_FINALIZER_PHASES:
            finalizers.append(job)
        elif phase in _HISTORY_PUBLISHER_PHASES:
            publishers.append(job)
    if len(finalizers) > 1:
        message = "duplicate finalizer job"
        raise ValueError(message)
    if len(publishers) > 1:
        message = "duplicate publisher job"
        raise ValueError(message)
    facts: list[tuple[str, str]] = []
    for role, candidates in (
        ("finalizer", finalizers),
        ("publisher", publishers),
    ):
        if candidates:
            candidate = candidates[0]
            facts.extend(
                (
                    (
                        f"{role}-job-id",
                        str(
                            _integer_field(
                                candidate, "id", "job_id", context="job"
                            )
                        ),
                    ),
                    (
                        f"{role}-job-conclusion",
                        _string_field(
                            candidate,
                            "conclusion",
                            default="unknown",
                            context="job",
                        ),
                    ),
                    (
                        f"{role}-job-phase",
                        _normalized_history_phase(
                            _string_field(
                                candidate,
                                "phase",
                                "name",
                                context="job",
                            )
                        ),
                    ),
                )
            )
    selected = (
        finalizers[0] if finalizers else (publishers[0] if publishers else None)
    )
    return selected, tuple(sorted(facts))


def form_execution_history_admission_snapshot(  # noqa: C901, PLR0912
    *,
    authority: str,
    request_id: str,
    current_workflow_run_id: int,
    current_run_attempt: int,
    execution: BuddyExecutionIdentity,
    query_basis: tuple[str, ...],
    pagination_basis: tuple[str, ...],
    records: tuple[HistoricalExecutionRecord, ...],
    queries_complete: bool,
    pagination_complete: bool,
    malformed_results: bool,
    expected_result_count: int,
    attempt_created: bool,
    verified_prior_attempts: tuple[int, ...] = (),
) -> ExecutionHistoryAdmissionSnapshot:
    """Admit exhaustive history only under caller-selected history authority."""
    if authority != "execution-history":
        message = "History authority must be caller-selected execution-history"
        raise ValueError(message)
    if attempt_created:
        message = "Execution history cannot be admitted after Attempt creation"
        raise ValueError(message)
    if (
        type(queries_complete) is not bool
        or type(pagination_complete) is not bool
        or type(malformed_results) is not bool
    ):
        message = "History query completion facts must be exact Booleans"
        raise TypeError(message)
    if not queries_complete or not pagination_complete:
        message = "Execution history query result is truncated or incomplete"
        raise ValueError(message)
    if malformed_results:
        message = "Execution history query result is malformed"
        raise ValueError(message)
    if (
        type(expected_result_count) is not int
        or expected_result_count < 0
        or expected_result_count != len(records)
    ):
        message = "Execution history query result count is incomplete"
        raise ValueError(message)
    if type(verified_prior_attempts) is not tuple or any(
        type(attempt) is not int or attempt <= 0
        for attempt in verified_prior_attempts
    ):
        message = "Verified prior attempts must be positive exact integers"
        raise TypeError(message)
    if len(set(verified_prior_attempts)) != len(verified_prior_attempts):
        message = "Verified prior attempts contain duplicates"
        raise ValueError(message)
    verified = frozenset(verified_prior_attempts)
    for record in records:
        if type(record) is not HistoricalExecutionRecord:
            message = "Execution history contains a malformed record"
            raise TypeError(message)
        if record.execution != execution:
            message = "Execution history contains cross-Execution history"
            raise ValueError(message)
        if record.source_head_sha != execution.target:
            message = "Execution history contains a cross-target record"
            raise ValueError(message)
        if record.source_workflow_run_id == current_workflow_run_id and (
            record.queried_run_attempt >= current_run_attempt
            or record.queried_run_attempt not in verified
        ):
            message = (
                "Same-run history lacks separately verified prior-attempt "
                "existence"
            )
            raise ValueError(message)
    sorted_records = tuple(
        sorted(
            records,
            key=lambda record: (
                record.source_workflow_run_id,
                record.queried_run_attempt,
                record.artifact_id,
                record.record_digest,
            ),
        )
    )
    return ExecutionHistoryAdmissionSnapshot(
        request_id=request_id,
        current_workflow_run_id=current_workflow_run_id,
        current_run_attempt=current_run_attempt,
        execution=execution,
        query_basis=tuple(sorted(query_basis)),
        pagination_basis=tuple(sorted(pagination_basis)),
        records=sorted_records,
    )


def discover_execution_history(  # noqa: C901, PLR0912, PLR0915
    *,
    client: GitHubActionsHistoryClient,
    execution: BuddyExecutionIdentity,
    request_id: str = _DEFAULT_REQUEST,
    current_workflow_run_id: int,
    current_run_attempt: int,
    observed_at: datetime | None = None,
) -> ExecutionHistoryAdmissionSnapshot:
    """Exhaustively admit retained GitHub run/artifact/job history."""
    if type(execution) is not BuddyExecutionIdentity:
        message = "history discovery requires exact Buddy Execution"
        raise TypeError(message)
    _require_positive_integer(
        current_workflow_run_id,
        field="current_workflow_run_id",
    )
    _require_positive_integer(current_run_attempt, field="current_run_attempt")
    if observed_at is None:
        observed_at = datetime.now(tz=UTC)
    if type(observed_at) is not datetime:
        raise TypeError("history observation time must be a datetime")
    if observed_at.tzinfo is None or observed_at.utcoffset() is None:
        raise ValueError("history observation time must be timezone-aware")
    observed_at = observed_at.astimezone(UTC)
    try:
        runs = iter_all_runs(client)
    except ValueError as error:
        message = str(error)
        if "truncated" in message:
            truncated = "truncated"
            raise ValueError(truncated) from error
        raise
    run_ids: set[int] = set()
    records: list[HistoricalExecutionRecord] = []
    verified_prior_attempts: set[int] = set()
    for run in runs:
        run_id = _integer_field(run, "id", "run_id", context="run")
        if run_id in run_ids:
            message = "duplicate run"
            raise ValueError(message)
        run_ids.add(run_id)
        head_sha = _string_field(
            run,
            "head_sha",
            "head-sha",
            default=execution.target,
            context="run",
        )
        if head_sha != execution.target:
            continue
        run_attempt = _run_attempt(run)
        current_run = run_id == current_workflow_run_id
        if current_run and run_attempt > current_run_attempt:
            continue
        listed_node_id = _string_field(
            run,
            "node_id",
            "node-id",
            default=f"WFR_{run_id}",
            context="run",
        )
        latest_prior_attempt = run_attempt
        if current_run:
            latest_prior_attempt = min(
                run_attempt,
                current_run_attempt - 1,
            )
        jobs_by_attempt: dict[
            int, tuple[object | None, tuple[tuple[str, str], ...]]
        ] = {}
        facts_by_attempt: dict[int, GitHubRunAttemptFact] = {}
        expected_binding_attempts: set[int] = set()
        for prior_attempt in range(1, latest_prior_attempt + 1):
            try:
                exact_fact = _run_attempt_fact(
                    client.get_run_attempt(run_id, prior_attempt),
                    run_id=run_id,
                    run_attempt=prior_attempt,
                    listed_node_id=listed_node_id,
                    head_sha=head_sha,
                )
                exact_jobs = iter_all_attempt_jobs(
                    client,
                    run_id,
                    prior_attempt,
                )
            except (RuntimeError, ValueError, TypeError) as error:
                raise ValueError(
                    "exact run-attempt proof is missing or invalid"
                ) from error
            facts_by_attempt[prior_attempt] = exact_fact
            jobs_by_attempt[prior_attempt] = _select_job(exact_jobs)
            if _recent_binding_expected(
                exact_jobs,
                observed_at=observed_at,
            ):
                expected_binding_attempts.add(prior_attempt)
            if current_run:
                verified_prior_attempts.add(prior_attempt)
        artifacts = iter_all_artifacts(client, run_id)
        artifact_ids: set[int] = set()
        candidates: list[
            tuple[object, int, str, bytes, _HistoricalPayloadFacts]
        ] = []
        for artifact in artifacts:
            artifact_id = _integer_field(
                artifact,
                "id",
                "artifact_id",
                context="artifact",
            )
            if artifact_id in artifact_ids:
                message = "duplicate artifact"
                raise ValueError(message)
            artifact_ids.add(artifact_id)
            if _artifact_expired(artifact):
                continue
            try:
                archive_bytes = client.download_artifact(artifact_id)
            except RuntimeError as error:
                message = str(error)
                raise ValueError(message) from error
            if type(archive_bytes) is not bytes:
                message = "malformed artifact download"
                raise TypeError(message)
            try:
                download = admit_artifact_download(
                    archive_bytes,
                    expected_digest=_listed_artifact_digest(artifact),
                )
            except GitHubArtifactArchiveShapeError:
                continue
            except RuntimeError as error:
                message = str(error)
                raise ValueError(message) from error
            payload = download.payload
            facts = _historical_payload_facts(
                payload,
                execution=execution,
                source_workflow_run_id=run_id,
            )
            if facts is None:
                continue
            if facts.declared_run_attempt is None:
                raise ValueError(
                    "recognized history artifact lacks run-attempt selector"
                )
            queried_run_attempt = facts.declared_run_attempt
            if queried_run_attempt > run_attempt:
                raise ValueError(
                    "recognized history artifact run attempt exceeds latest "
                    "watermark"
                )
            if current_run and queried_run_attempt >= current_run_attempt:
                continue
            candidates.append(
                (
                    artifact,
                    artifact_id,
                    download.archive_digest,
                    payload,
                    facts,
                )
            )
        binding_candidates: dict[
            int,
            list[tuple[object, int, str, bytes, _HistoricalPayloadFacts]],
        ] = {}
        for candidate in candidates:
            facts = candidate[-1]
            if facts.attempt_binding:
                attempt = cast("int", facts.declared_run_attempt)
                binding_candidates.setdefault(attempt, []).append(candidate)
        if any(
            len(attempt_candidates) != 1
            for attempt_candidates in binding_candidates.values()
        ):
            raise ValueError("ambiguous retained Release Attempt binding")
        missing_bindings = sorted(
            attempt
            for attempt in expected_binding_attempts
            if attempt not in binding_candidates
        )
        if missing_bindings:
            raise ValueError(
                "recent run is missing an expected non-expired "
                "Release Attempt binding"
            )
        if not candidates:
            continue
        for (
            artifact,
            artifact_id,
            artifact_digest,
            payload,
            facts,
        ) in candidates:
            queried_run_attempt = cast("int", facts.declared_run_attempt)
            exact_fact = facts_by_attempt[queried_run_attempt]
            selected_job, job_facts = jobs_by_attempt[queried_run_attempt]
            job_id = (
                None
                if selected_job is None
                else _integer_field(
                    selected_job,
                    "id",
                    "job_id",
                    context="job",
                )
            )
            conclusion = (
                None
                if selected_job is None
                else _string_field(
                    selected_job,
                    "conclusion",
                    default="unknown",
                    context="job",
                )
            )
            phase = (
                None
                if selected_job is None
                else _normalized_history_phase(
                    _string_field(
                        selected_job,
                        "phase",
                        "name",
                        context="job",
                    )
                )
            )
            record = HistoricalExecutionRecord(
                execution=execution,
                artifact_id=artifact_id,
                artifact_digest=artifact_digest,
                payload_digest=_sha256_bytes(payload),
                source_workflow_run_id=run_id,
                source_workflow_run_node_id=exact_fact.node_id,
                source_head_sha=exact_fact.head_sha,
                artifact_metadata=_metadata(artifact),
                run_metadata=tuple(
                    sorted(
                        (
                            ("attempt-conclusion", str(exact_fact.conclusion)),
                            ("attempt-status", exact_fact.status),
                            *exact_fact.metadata,
                        )
                    )
                ),
                queried_run_attempt=queried_run_attempt,
                queried_job_id=job_id,
                queried_job_conclusion=conclusion,
                queried_phase=phase,
                diagnostic_claims=tuple(
                    sorted((*facts.diagnostic_claims, *job_facts))
                ),
            )
            records.append(record)
    return form_execution_history_admission_snapshot(
        authority="execution-history",
        request_id=request_id,
        current_workflow_run_id=current_workflow_run_id,
        current_run_attempt=current_run_attempt,
        execution=execution,
        query_basis=("run:artifacts", "run:jobs", "workflow:runs"),
        pagination_basis=(
            "artifacts:exhausted",
            "jobs:exhausted",
            "runs:exhausted",
        ),
        records=tuple(records),
        queries_complete=True,
        pagination_complete=True,
        malformed_results=False,
        expected_result_count=len(records),
        attempt_created=False,
        verified_prior_attempts=tuple(sorted(verified_prior_attempts)),
    )


def materialize_reviewer_artifact(
    *,
    snapshot_bytes: bytes,
    summary_bytes: bytes,
    artifact_id: int,
    upload_digest: str,
) -> ReviewerArtifact:
    """Preserve exact reviewer Snapshot/summary bytes and transport digest."""
    if type(snapshot_bytes) is not bytes or type(summary_bytes) is not bytes:
        message = "reviewer artifact inputs must be exact bytes"
        raise TypeError(message)
    return ReviewerArtifact(
        snapshot_bytes=snapshot_bytes,
        summary_bytes=summary_bytes,
        snapshot_payload_digest=_sha256_bytes(snapshot_bytes),
        summary_payload_digest=_sha256_bytes(summary_bytes),
        artifact_id=_require_positive_integer(
            artifact_id,
            field="artifact_id",
        ),
        upload_digest=_require_digest(upload_digest, field="upload_digest"),
    )


def materialize_reviewer_payload(
    *,
    snapshot_bytes: bytes,
    summary_bytes: bytes,
) -> ReviewerPayload:
    """Materialize exact reviewer bytes before Actions assigns transport IDs."""
    if type(snapshot_bytes) is not bytes or type(summary_bytes) is not bytes:
        raise TypeError("reviewer payload inputs must be exact bytes")
    return ReviewerPayload(
        snapshot_bytes=snapshot_bytes,
        summary_bytes=summary_bytes,
        snapshot_payload_digest=_sha256_bytes(snapshot_bytes),
        summary_payload_digest=_sha256_bytes(summary_bytes),
    )


def bind_reviewer_artifact(
    *,
    payload: ReviewerPayload,
    artifact_id: int,
    upload_digest: str,
    snapshot_payload_digest: str,
    summary_payload_digest: str,
) -> ReviewerArtifact:
    """Bind returned Actions identity only to the exact pre-upload payload."""
    if type(payload) is not ReviewerPayload:
        raise TypeError("reviewer binding requires an exact payload")
    if (
        snapshot_payload_digest != payload.snapshot_payload_digest
        or summary_payload_digest != payload.summary_payload_digest
    ):
        raise ValueError("reviewer payload digest substitution")
    return materialize_reviewer_artifact(
        snapshot_bytes=payload.snapshot_bytes,
        summary_bytes=payload.summary_bytes,
        artifact_id=artifact_id,
        upload_digest=upload_digest,
    )


def fetch_exact_public_revision(
    *,
    target: str,
    run: Callable[[tuple[str, ...]], str],
) -> PublicRevisionCheckout:
    """Anonymously fetch only the exact public commit SHA and verify HEAD."""
    if type(target) is not str or _SHA_PATTERN.fullmatch(target) is None:
        message = "target must be a 40-character lowercase SHA"
        raise ValueError(message)
    commands = (
        ("git", "init", "."),
        ("git", "remote", "add", "origin", _PUBLIC_REPOSITORY_URL),
        ("git", "fetch", "--no-tags", "--depth=1", "origin", target),
        ("git", "checkout", "--detach", target),
        ("git", "rev-parse", "HEAD"),
        ("git", "symbolic-ref", "-q", "HEAD"),
    )
    head = ""
    symbolic = ""
    for command in commands:
        result = run(command)
        if type(result) is not str:
            message = "public revision runner returned a malformed result"
            raise TypeError(message)
        if command[-2:] == ("rev-parse", "HEAD"):
            head = result.strip()
        elif command[-3:] == ("symbolic-ref", "-q", "HEAD"):
            symbolic = result.strip()
    if head != target:
        message = "fetched commit does not match exact target"
        raise ValueError(message)
    if symbolic:
        message = "fetched revision is not detached HEAD"
        raise ValueError(message)
    return PublicRevisionCheckout(
        target=target,
        repository_url=_PUBLIC_REPOSITORY_URL,
        head=head,
        detached=True,
        commands=commands,
    )


def form_authorization_record(
    *,
    approval_result: str,
    attempt: ReleaseAttemptIdentity | None = None,
    publication_snapshot: PublicationSnapshot | None = None,
    reviewer_artifact: ReviewerArtifact | None = None,
    approval_job_id: int = 1,
    completed_at: str = "2026-08-13T16:00:00Z",
    control: str = _DEFAULT_CONTROL,
    diagnostic: object | None = None,
    schedule_capability: Callable[[], object] | None = None,
) -> AuthorizationRecord:
    """Form Authorization only for successful current Environment approval."""
    del diagnostic, schedule_capability
    if approval_result != "success":
        message = "GitHub deployment-review denial is diagnostic-only"
        raise ValueError(message)
    if (
        type(attempt) is not ReleaseAttemptIdentity
        or type(publication_snapshot) is not PublicationSnapshot
        or type(reviewer_artifact) is not ReviewerArtifact
    ):
        message = "successful authorization requires exact current bindings"
        raise TypeError(message)
    if publication_snapshot.attempt != attempt:
        message = "Authorization Publication Snapshot Attempt mismatch"
        raise ValueError(message)
    return AuthorizationRecord(
        attempt=attempt,
        publication_snapshot_digest=publication_snapshot.snapshot_digest,
        reviewer_summary_artifact_id=reviewer_artifact.artifact_id,
        reviewer_summary_upload_digest=reviewer_artifact.upload_digest,
        reviewer_summary_payload_digest=(
            reviewer_artifact.summary_payload_digest
        ),
        workflow_run_id=attempt.workflow_run_id,
        run_attempt=attempt.run_attempt,
        approval_job_id=approval_job_id,
        approval_job="approval",
        environment="workflow-delivery-v3-buddy-smoke-approval",
        channel="buddy",
        completed_at=completed_at,
        producer="approval",
        control=control,
    )


def _demo_attempt(*, run_attempt: int = 1) -> ReleaseAttemptIdentity:
    return ReleaseAttemptIdentity(
        execution=BuddyExecutionIdentity(
            channel="buddy",
            release_unit="hcoona-release-smoke-npm",
            target=_DEFAULT_TARGET,
        ),
        workflow_run_id=1,
        run_attempt=run_attempt,
    )


def _capability_decision(
    *,
    attempt: ReleaseAttemptIdentity,
    authorization_digest: str,
    publication_snapshot_digest: str,
    reviewer_artifact: ReviewerArtifact,
    result: str,
    diagnostics: tuple[str, ...],
    live_eligibility_artifact_id: int,
    live_eligibility_artifact_digest: str,
    governance_provenance: tuple[tuple[str, str], ...],
    governance_content_sha256: str,
    governance_expires_at: str,
    governance_live_enabled: bool,
    control: str,
    action_digests: tuple[str, ...] = (),
    artifact_digests: tuple[str, ...] = (),
    resource_key_sets: tuple[tuple[str, tuple[str, ...]], ...] = (),
    lock_groups: tuple[tuple[str, str], ...] = (),
    capability_group_manifest: tuple[tuple[str, tuple[str, ...]], ...] = (),
) -> CapabilityAdmissionDecision:
    return CapabilityAdmissionDecision(
        attempt=attempt,
        authorization_digest=authorization_digest,
        publication_snapshot_digest=publication_snapshot_digest,
        reviewer_summary_artifact_id=reviewer_artifact.artifact_id,
        reviewer_summary_upload_digest=reviewer_artifact.upload_digest,
        reviewer_summary_payload_digest=reviewer_artifact.summary_payload_digest,
        action_digests=action_digests,
        artifact_digests=artifact_digests,
        resource_key_sets=resource_key_sets,
        lock_groups=lock_groups,
        capability_group_manifest=capability_group_manifest,
        live_eligibility_artifact_id=live_eligibility_artifact_id,
        live_eligibility_artifact_digest=live_eligibility_artifact_digest,
        governance_provenance=governance_provenance,
        governance_content_sha256=governance_content_sha256,
        governance_expires_at=governance_expires_at,
        governance_live_enabled=governance_live_enabled,
        producer="approval-finalizer",
        control=control,
        workflow_run_id=attempt.workflow_run_id,
        run_attempt=attempt.run_attempt,
        result=result,
        diagnostics=diagnostics,
    )


def admit_live_capability(
    *,
    attempt: ReleaseAttemptIdentity | None = None,
    authorization: AuthorizationRecord | None = None,
    publication_snapshot: PublicationSnapshot | None = None,
    reviewer_artifact: ReviewerArtifact | None = None,
    live_eligibility_artifact_id: int = 1,
    live_eligibility_artifact_digest: str = "sha256:" + ("1" * 64),
    governance_provenance: tuple[tuple[str, str], ...] = (
        ("blob-oid", "blob"),
        ("content-sha256", "sha256:" + ("2" * 64)),
        (
            "path",
            ".github/workflow-delivery/governance/"
            "hcoona-release-smoke-npm.json",
        ),
        ("ref", "refs/heads/main"),
        ("repository", "hcoona/three"),
        ("resolved-commit", _DEFAULT_TARGET),
    ),
    governance_content_sha256: str = "sha256:" + ("2" * 64),
    governance_expires_at: str = _DEFAULT_EXPIRES_AT,
    governance_live_enabled: bool = True,
    governance_observed_at: datetime = _DEFAULT_OBSERVED_AT,
    expected_governance_provenance: tuple[tuple[str, str], ...] | None = None,
    expected_governance_content_sha256: str | None = None,
    expected_governance_expires_at: str | None = None,
    expected_governance_live_enabled: bool | None = None,
    control: str = _DEFAULT_CONTROL,
    substitution: str | None = None,
    restored: bool = False,
) -> CapabilityAdmissionDecision | LiveCapabilityAdmissionResult:
    """Admit capability only on exact fresh Governance and bindings."""
    if substitution is not None:
        current = _demo_attempt()
        reviewer = materialize_reviewer_artifact(
            snapshot_bytes=b"{}",
            summary_bytes=b"{}",
            artifact_id=1,
            upload_digest="sha256:" + ("3" * 64),
        )
        typed = _SUBSTITUTION_DIAGNOSTICS.get(substitution)
        if typed is None:
            message = "unsupported Governance substitution"
            raise ValueError(message)
        blocked = _capability_decision(
            attempt=current,
            authorization_digest="sha256:" + ("4" * 64),
            publication_snapshot_digest="sha256:" + ("5" * 64),
            reviewer_artifact=reviewer,
            result="blocked",
            diagnostics=(typed,),
            live_eligibility_artifact_id=1,
            live_eligibility_artifact_digest="sha256:" + ("1" * 64),
            governance_provenance=governance_provenance,
            governance_content_sha256=governance_content_sha256,
            governance_expires_at=governance_expires_at,
            governance_live_enabled=substitution != "disabled",
            control=control,
        )
        restored_decision = _capability_decision(
            attempt=_demo_attempt(run_attempt=2 if restored else 1),
            authorization_digest="sha256:" + ("4" * 64),
            publication_snapshot_digest="sha256:" + ("5" * 64),
            reviewer_artifact=reviewer,
            result="success",
            diagnostics=(),
            live_eligibility_artifact_id=1,
            live_eligibility_artifact_digest="sha256:" + ("1" * 64),
            governance_provenance=governance_provenance,
            governance_content_sha256=governance_content_sha256,
            governance_expires_at=governance_expires_at,
            governance_live_enabled=True,
            control=control,
            action_digests=("sha256:" + ("6" * 64),),
            artifact_digests=("sha256:" + ("7" * 64),),
            resource_key_sets=(("action:restored", ("resource:restored",)),),
            lock_groups=(("action:restored", "lock:restored"),),
            capability_group_manifest=(
                ("capability:restored", ("action:restored",)),
            ),
        )
        return LiveCapabilityAdmissionResult(
            current_attempt=blocked,
            restored_attempt=restored_decision,
        )
    if (
        type(attempt) is not ReleaseAttemptIdentity
        or type(authorization) is not AuthorizationRecord
        or type(publication_snapshot) is not PublicationSnapshot
        or type(reviewer_artifact) is not ReviewerArtifact
    ):
        message = "capability admission requires exact current bindings"
        raise TypeError(message)
    if (
        authorization.attempt != attempt
        or publication_snapshot.attempt != attempt
        or authorization.publication_snapshot_digest
        != publication_snapshot.snapshot_digest
    ):
        message = "Capability admission current binding mismatch"
        raise ValueError(message)
    if (
        authorization.reviewer_summary_artifact_id
        != reviewer_artifact.artifact_id
        or authorization.reviewer_summary_upload_digest
        != reviewer_artifact.upload_digest
        or authorization.reviewer_summary_payload_digest
        != reviewer_artifact.summary_payload_digest
        or reviewer_artifact.snapshot_payload_digest
        != publication_snapshot.snapshot_digest
    ):
        message = "Capability admission reviewer artifact mismatch"
        raise ValueError(message)
    actions = publication_snapshot.materialized_actions
    action_digests = tuple(sorted(action.action_digest for action in actions))
    artifact_digests = tuple(
        sorted(action.artifact_digest for action in actions)
    )
    resource_key_sets = tuple(
        sorted(
            (action.action_id, action.mutable_resource_keys)
            for action in actions
        )
    )
    lock_groups = tuple(
        sorted((action.action_id, action.lock_group) for action in actions)
    )
    groups = {
        action.capability_group: tuple(
            sorted(
                candidate.action_id
                for candidate in actions
                if candidate.capability_group == action.capability_group
            )
        )
        for action in actions
    }
    diagnostics = _governance_diagnostics(
        provenance=governance_provenance,
        content_sha256=governance_content_sha256,
        expires_at=governance_expires_at,
        live_enabled=governance_live_enabled,
        observed_at=governance_observed_at,
        expected_provenance=expected_governance_provenance,
        expected_content_sha256=expected_governance_content_sha256,
        expected_expires_at=expected_governance_expires_at,
        expected_live_enabled=expected_governance_live_enabled,
    )
    return _capability_decision(
        attempt=attempt,
        authorization_digest=authorization.authorization_digest,
        publication_snapshot_digest=publication_snapshot.snapshot_digest,
        reviewer_artifact=reviewer_artifact,
        result="success" if not diagnostics else "blocked",
        diagnostics=diagnostics,
        live_eligibility_artifact_id=live_eligibility_artifact_id,
        live_eligibility_artifact_digest=live_eligibility_artifact_digest,
        governance_provenance=governance_provenance,
        governance_content_sha256=governance_content_sha256,
        governance_expires_at=governance_expires_at,
        governance_live_enabled=governance_live_enabled,
        control=control,
        action_digests=action_digests,
        artifact_digests=artifact_digests,
        resource_key_sets=resource_key_sets,
        lock_groups=lock_groups,
        capability_group_manifest=tuple(sorted(groups.items())),
    )


def finalize_attempt_outcome(  # noqa: C901, PLR0911, PLR0912, PLR0915
    *,
    attempt: ReleaseAttemptIdentity,
    qualification_decision: QualificationDecision,
    publication_snapshot: PublicationSnapshot | None,
    authorization: AuthorizationRecord | None,
    capability_decisions: tuple[CapabilityAdmissionDecision, ...],
    group_bundles: tuple[CapabilityGroupResultBundle, ...],
    receipts: tuple[Receipt, ...],
    observations: tuple[ProjectionObservation, ...] = (),
    receipt_transport_references: tuple[ReceiptTransportReference, ...] = (),
    publication_preparation_interrupted: bool = False,
    platform_terminated: bool = False,
    capability_may_have_started: bool = False,
) -> AttemptOutcome:
    """Finalize without remote queries or inference from job success."""
    if type(attempt) is not ReleaseAttemptIdentity:
        message = "Live finalization requires an exact Attempt"
        raise TypeError(message)
    if type(qualification_decision) is not QualificationDecision:
        message = "Live finalization requires an exact Qualification Decision"
        raise TypeError(message)
    if qualification_decision.subject != attempt:
        message = "Live finalization Qualification binding mismatch"
        raise ValueError(message)
    if any(
        type(value) is not bool
        for value in (
            publication_preparation_interrupted,
            platform_terminated,
            capability_may_have_started,
        )
    ):
        message = "Platform termination facts must be exact Booleans"
        raise TypeError(message)
    collections = (
        ("observations", observations),
        ("capability decisions", capability_decisions),
        ("group bundles", group_bundles),
        ("receipts", receipts),
        ("receipt transport references", receipt_transport_references),
    )
    for name, values in collections:
        if type(values) is not tuple:
            message = f"Live finalization {name} must be an exact tuple"
            raise TypeError(message)
    observation_projections: set[str] = set()
    for observation in observations:
        if type(observation) is not ProjectionObservation:
            message = "Live finalization Observation has the wrong type"
            raise TypeError(message)
        if (
            observation.subject != attempt
            or observation.target != attempt.execution.target
            or observation.purpose != "live-release"
            or observation.qualification_snapshot_digest
            != qualification_decision.qualification_snapshot_digest
        ):
            message = "Live finalization Observation binding mismatch"
            raise ValueError(message)
        projection_id = observation.projection.projection_id
        if projection_id in observation_projections:
            message = "Live finalization Observations repeat a projection"
            raise ValueError(message)
        observation_projections.add(projection_id)
    if qualification_decision.terminal_result != "success":
        if (
            observations
            or publication_snapshot is not None
            or authorization is not None
            or capability_decisions
            or group_bundles
            or receipts
            or receipt_transport_references
            or publication_preparation_interrupted
            or platform_terminated
            or capability_may_have_started
        ):
            message = (
                "Unsuccessful qualification cannot bind publication records"
            )
            raise ValueError(message)
        return AttemptOutcome(
            attempt=attempt,
            qualification_decision_digest=qualification_decision.decision_digest,
            publication_snapshot_digest=None,
            authorization_digest=None,
            capability_admission_digests=(),
            capability_group_bundle_digests=(),
            receipt_digests=(),
            terminal_phase="qualification",
            result=qualification_decision.terminal_result,
            uncertainty=qualification_decision.terminal_result == "incomplete",
            possibly_mutated=False,
            next_action=qualification_decision.next_action,
        )
    if publication_preparation_interrupted:
        if (
            publication_snapshot is not None
            or authorization is not None
            or capability_decisions
            or group_bundles
            or receipts
            or receipt_transport_references
            or platform_terminated
            or capability_may_have_started
        ):
            message = (
                "Publication preparation interruption has contradictory records"
            )
            raise ValueError(message)
        observation_digests = tuple(
            sorted(
                observation.observation_digest for observation in observations
            )
        )
        blocking_classifications = {
            observation.value.classification for observation in observations
        } & {"partial", "conflicting", "unknown", "unprovable"}
        if blocking_classifications:
            uncertainty = bool(
                blocking_classifications & {"unknown", "unprovable"}
            )
            return AttemptOutcome(
                attempt=attempt,
                qualification_decision_digest=(
                    qualification_decision.decision_digest
                ),
                publication_snapshot_digest=None,
                authorization_digest=None,
                capability_admission_digests=(),
                capability_group_bundle_digests=(),
                receipt_digests=(),
                terminal_phase="observation",
                result="incomplete" if uncertainty else "failure",
                uncertainty=uncertainty,
                possibly_mutated=False,
                next_action="reconcile",
                observation_digests=observation_digests,
            )
        return AttemptOutcome(
            attempt=attempt,
            qualification_decision_digest=qualification_decision.decision_digest,
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
            observation_digests=observation_digests,
        )
    if observations:
        message = (
            "Direct Observations require publication preparation interruption"
        )
        raise ValueError(message)
    if type(publication_snapshot) is not PublicationSnapshot:
        message = "Successful qualification requires Publication Snapshot"
        raise TypeError(message)
    if publication_snapshot.attempt != attempt:
        message = "Live finalization Publication Snapshot Attempt mismatch"
        raise ValueError(message)
    if (
        publication_snapshot.qualification_decision_digest
        != qualification_decision.decision_digest
        or publication_snapshot.qualification_snapshot_digest
        != qualification_decision.qualification_snapshot_digest
    ):
        message = "Live finalization Qualification binding mismatch"
        raise ValueError(message)
    if authorization is not None:
        if type(authorization) is not AuthorizationRecord:
            message = "Live finalization Authorization has the wrong type"
            raise TypeError(message)
        if (
            authorization.attempt != attempt
            or authorization.publication_snapshot_digest
            != publication_snapshot.snapshot_digest
        ):
            message = "Live finalization Authorization binding mismatch"
            raise ValueError(message)
    for reference in receipt_transport_references:
        if type(reference) is not ReceiptTransportReference:
            message = "Live finalization Receipt transport has the wrong type"
            raise TypeError(message)
    for record in (*capability_decisions, *group_bundles, *receipts):
        if record.attempt != attempt:
            message = "Mixed-attempt failed-job reruns are not admissible"
            raise ValueError(message)
        if (
            record.publication_snapshot_digest
            != publication_snapshot.snapshot_digest
        ):
            message = "Live finalization Snapshot binding mismatch"
            raise ValueError(message)

    if platform_terminated:
        possibly_mutated = capability_may_have_started
        return AttemptOutcome(
            attempt=attempt,
            qualification_decision_digest=qualification_decision.decision_digest,
            publication_snapshot_digest=publication_snapshot.snapshot_digest,
            authorization_digest=(
                authorization.authorization_digest
                if authorization is not None
                else None
            ),
            capability_admission_digests=tuple(
                sorted(
                    decision.decision_digest
                    for decision in capability_decisions
                )
            ),
            capability_group_bundle_digests=tuple(
                sorted(bundle.bundle_digest for bundle in group_bundles)
            ),
            receipt_digests=tuple(
                sorted(receipt.receipt_digest for receipt in receipts)
            ),
            terminal_phase=(
                "post-capability-termination"
                if possibly_mutated
                else "pre-capability-termination"
            ),
            result=(
                "incomplete-possibly-mutated"
                if possibly_mutated
                else "replayable-no-side-effect"
            ),
            uncertainty=possibly_mutated,
            possibly_mutated=possibly_mutated,
            next_action="reobserve-and-replay"
            if possibly_mutated
            else "replay",
        )

    if authorization is None:
        contradictory_evidence = bool(
            capability_decisions
            or group_bundles
            or receipts
            or receipt_transport_references
        )
        return AttemptOutcome(
            attempt=attempt,
            qualification_decision_digest=qualification_decision.decision_digest,
            publication_snapshot_digest=publication_snapshot.snapshot_digest,
            authorization_digest=None,
            capability_admission_digests=tuple(
                sorted(
                    decision.decision_digest
                    for decision in capability_decisions
                )
            ),
            capability_group_bundle_digests=tuple(
                sorted(bundle.bundle_digest for bundle in group_bundles)
            ),
            receipt_digests=tuple(
                sorted(receipt.receipt_digest for receipt in receipts)
            ),
            terminal_phase=(
                "authorization-contradiction"
                if contradictory_evidence
                else "approval-contract"
            ),
            result=(
                "incomplete-possibly-mutated"
                if contradictory_evidence
                else "unknown-replayable-approval-contract"
            ),
            uncertainty=True,
            possibly_mutated=contradictory_evidence,
            next_action=(
                "reobserve-and-replay" if contradictory_evidence else "replay"
            ),
        )
    actions = publication_snapshot.materialized_actions
    if not actions:
        if capability_decisions or group_bundles or receipts:
            message = "Exact pre-observed no-op cannot emit capability records"
            raise ValueError(message)
        return AttemptOutcome(
            attempt=attempt,
            qualification_decision_digest=qualification_decision.decision_digest,
            publication_snapshot_digest=publication_snapshot.snapshot_digest,
            authorization_digest=authorization.authorization_digest,
            capability_admission_digests=(),
            capability_group_bundle_digests=(),
            receipt_digests=(),
            terminal_phase="finalized-no-op",
            result="success",
            uncertainty=False,
            possibly_mutated=False,
            next_action="none",
        )
    blocked_decisions = tuple(
        decision
        for decision in capability_decisions
        if not decision.authorizing
    )
    if blocked_decisions:
        if len(blocked_decisions) != len(capability_decisions):
            message = "Live finalization Capability Decisions conflict"
            raise ValueError(message)
        if group_bundles or receipts:
            message = "Blocked capability cannot have publication results"
            raise ValueError(message)
        return AttemptOutcome(
            attempt=attempt,
            qualification_decision_digest=qualification_decision.decision_digest,
            publication_snapshot_digest=publication_snapshot.snapshot_digest,
            authorization_digest=authorization.authorization_digest,
            capability_admission_digests=tuple(
                sorted(
                    decision.decision_digest
                    for decision in capability_decisions
                )
            ),
            capability_group_bundle_digests=(),
            receipt_digests=(),
            terminal_phase="capability-blocked",
            result="failure",
            uncertainty=False,
            possibly_mutated=False,
            next_action="new-attempt",
        )

    expected_groups = {
        action.capability_group: tuple(
            sorted(
                candidate.action_id
                for candidate in actions
                if candidate.capability_group == action.capability_group
            )
        )
        for action in actions
    }
    decision_groups = {
        group
        for decision in capability_decisions
        for group, _ in decision.capability_group_manifest
        if decision.authorizing
    }
    if decision_groups != expected_groups.keys():
        message = "Live finalization Capability Decisions are not exact"
        raise ValueError(message)
    expected_action_digests = tuple(
        sorted(action.action_digest for action in actions)
    )
    expected_artifact_digests = tuple(
        sorted(action.artifact_digest for action in actions)
    )
    expected_resource_key_sets = tuple(
        sorted(
            (action.action_id, action.mutable_resource_keys)
            for action in actions
        )
    )
    expected_lock_groups = tuple(
        sorted((action.action_id, action.lock_group) for action in actions)
    )
    expected_capability_group_manifest = tuple(sorted(expected_groups.items()))
    if any(
        not decision.authorizing
        or decision.authorization_digest != authorization.authorization_digest
        or decision.action_digests != expected_action_digests
        or decision.artifact_digests != expected_artifact_digests
        or decision.resource_key_sets != expected_resource_key_sets
        or decision.lock_groups != expected_lock_groups
        or decision.capability_group_manifest
        != expected_capability_group_manifest
        for decision in capability_decisions
    ):
        message = "Live finalization Capability Decisions are not exact"
        raise ValueError(message)
    bundle_by_group = {
        bundle.capability_group: bundle for bundle in group_bundles
    }
    if len(bundle_by_group) != len(group_bundles) or (
        bundle_by_group.keys() != expected_groups.keys()
    ):
        message = "Live finalization requires exactly one bundle per group"
        raise ValueError(message)
    for group, action_ids in expected_groups.items():
        if bundle_by_group[group].planned_action_ids != action_ids:
            message = "Live finalization bundle action set is not exact"
            raise ValueError(message)

    receipt_by_action = {receipt.action_id: receipt for receipt in receipts}
    receipt_transport_by_action = {
        reference.action_id: reference
        for reference in receipt_transport_references
    }
    action_ids = {action.action_id for action in actions}
    if (
        len(receipt_by_action) != len(receipts)
        or receipt_by_action.keys() - action_ids
        or len(receipt_transport_by_action) != len(receipt_transport_references)
        or receipt_transport_by_action.keys() != receipt_by_action.keys()
    ):
        message = "Live finalization Receipt set contains duplicates or extras"
        raise ValueError(message)
    result_by_action: dict[str, ActionResult] = {
        result.action_id: result
        for bundle in group_bundles
        for result in bundle.action_results
    }
    action_by_id = {action.action_id: action for action in actions}
    for action_id, result in result_by_action.items():
        action = action_by_id.get(action_id)
        if (
            action is None
            or result.action_digest != action.action_digest
            or result.lock_group != action.lock_group
        ):
            message = "Live finalization Action Result binding mismatch"
            raise ValueError(message)
        receipt = receipt_by_action.get(action_id)
        receipt_transport = receipt_transport_by_action.get(action_id)
        if receipt is not None and (
            result.receipt_digest != receipt.receipt_digest
            or result.response_identity_digest
            != receipt.response_identity_digest
            or receipt.action_digest != action.action_digest
            or receipt.coordinate != action.projection.coordinate
            or receipt.mutable_resource_keys != action.mutable_resource_keys
            or receipt.lock_group != action.lock_group
            or receipt.artifact_transport != action.artifact.transport
            or receipt.artifact_content_sha256
            != action.artifact.content.content_sha256
            or receipt.artifact_content_sha512
            != action.artifact.content.content_sha512
            or receipt.witness_digest != action.artifact.witness_digest
            or receipt_transport is None
            or result.receipt_artifact_id != receipt_transport.artifact_id
            or result.receipt_artifact_name != receipt_transport.artifact_name
            or result.receipt_artifact_digest != receipt_transport.upload_digest
            or result.receipt_payload_digest != receipt_transport.payload_digest
            or receipt_transport.payload_digest != receipt.receipt_digest
        ):
            message = "Live finalization Receipt binding mismatch"
            raise ValueError(message)
    missing_receipt_after_possible_mutation = any(
        result.mutation_disposition
        in {"created", "exact-race-accepted", "possibly-mutated"}
        and result.action_id not in receipt_by_action
        for result in result_by_action.values()
    )
    all_success = (
        set(result_by_action) == action_ids
        and set(receipt_by_action) == action_ids
        and all(
            result.outcome == "success" for result in result_by_action.values()
        )
        and all(
            bundle.completion_state == "complete" for bundle in group_bundles
        )
    )
    publisher_governance_blocked = (
        not missing_receipt_after_possible_mutation
        and any(
            result.outcome == "failed"
            and result.mutation_disposition == "no-side-effect"
            and result.diagnostic_reference
            == PUBLISHER_GOVERNANCE_RECHECK_FAILED_BEFORE_RUNNER
            for result in result_by_action.values()
        )
    )
    return AttemptOutcome(
        attempt=attempt,
        qualification_decision_digest=qualification_decision.decision_digest,
        publication_snapshot_digest=publication_snapshot.snapshot_digest,
        authorization_digest=authorization.authorization_digest,
        capability_admission_digests=tuple(
            sorted(
                decision.decision_digest for decision in capability_decisions
            )
        ),
        capability_group_bundle_digests=tuple(
            sorted(bundle.bundle_digest for bundle in group_bundles)
        ),
        receipt_digests=tuple(
            sorted(receipt.receipt_digest for receipt in receipts)
        ),
        terminal_phase=(
            "capability-blocked"
            if publisher_governance_blocked
            else "finalized"
        ),
        result=(
            "success"
            if all_success
            else (
                "incomplete-possibly-mutated"
                if missing_receipt_after_possible_mutation
                else "failure"
            )
        ),
        uncertainty=missing_receipt_after_possible_mutation,
        possibly_mutated=missing_receipt_after_possible_mutation,
        next_action=(
            "none"
            if all_success
            else (
                "new-attempt"
                if publisher_governance_blocked
                else (
                    "reobserve-and-replay"
                    if missing_receipt_after_possible_mutation
                    else "replay"
                )
            )
        ),
    )


__all__ = [
    "LiveCapabilityAdmissionResult",
    "PublicRevisionCheckout",
    "ReviewerArtifact",
    "ReviewerPayload",
    "admit_live_capability",
    "bind_reviewer_artifact",
    "discover_execution_history",
    "fetch_exact_public_revision",
    "finalize_attempt_outcome",
    "form_authorization_record",
    "form_execution_history_admission_snapshot",
    "materialize_reviewer_artifact",
    "materialize_reviewer_payload",
]
