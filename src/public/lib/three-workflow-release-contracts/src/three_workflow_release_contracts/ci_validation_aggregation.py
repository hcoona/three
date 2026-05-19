"""Receipt-manifest and aggregate evidence helpers for CI validation."""

from __future__ import annotations

import hashlib
import re
from collections.abc import Mapping, Sequence
from dataclasses import dataclass
from typing import Literal, cast

from three_workflow_release_contracts.ci_validation import (
    API_VERSIONS_BY_KIND,
    DETAILS_BY_DIAGNOSTIC_CODE,
    REGISTERED_CI_VALIDATION_DIAGNOSTIC_CODES,
    REGISTERED_CI_VALIDATION_DIAGNOSTIC_DETAILS,
    CiValidationKind,
    CommonEnvelope,
    DiagnosticDetail,
    DiagnosticFamily,
    DiagnosticSeverity,
    DiagnosticVerdictEffect,
    artifact_physical_name,
    canonical_json_bytes,
    canonical_json_digest,
    validate_artifact_logical_ref,
    validate_artifact_physical_name,
    validate_common_envelope,
)
from three_workflow_release_contracts.ci_validation_assignments import (
    ci_validation_writer_observation_artifact_ref,
    validate_ci_validation_selector_assignments,
)
from three_workflow_release_contracts.ci_validation_plans import (
    ci_validation_plan_digest,
    validate_ci_validation_plan,
)
from three_workflow_release_contracts.ci_validation_receipts import (
    ci_validation_receipt_content_digest,
    load_ci_validation_receipt_payload,
    validate_ci_validation_receipt,
)
from three_workflow_release_contracts.ci_validation_requests import (
    ci_validation_diagnostic,
    ci_validation_receipt_manifest_artifact_ref,
)
from three_workflow_release_contracts.contracts import (
    ContractValidationError,
    ValidationIssue,
)

ReceiptAdmissibility = Literal["valid", "inadmissible"]
AggregateVerdict = Literal["passed", "failed"]
EvidenceResultOutcome = Literal["satisfied", "missing", "skipped", "failed"]
FailureKind = Literal[
    "invalid-plan",
    "required-evidence-missing",
    "required-evidence-skipped",
    "blocking-validation-failure",
    "inadmissible-receipt",
    "final-evidence-failure",
    "fail-closed",
]

_LOCAL_ID_RE = re.compile(r"^[a-z0-9][a-z0-9._-]{0,127}$")
_DIGEST_RE = re.compile(r"^[0-9a-f]{64}$")
_WRITER_ID_RE = re.compile(r"^github-actions-job:[0-9a-f]{64}$")
_RECEIPT_REF_RE = re.compile(
    r"^ci-validation/receipts/([^/]+)/([^/]+)/([^/]+)/receipt\.json$"
)
_TERMINAL_WORK_GROUP_KIND = "evidence-aggregation"
_EXECUTABLE_WORK_GROUP_KINDS = frozenset(
    {
        "lightweight-preflight",
        "ecosystem-gate",
        "descriptor-validation",
        "release-shaped-artifact",
        "workflow-release-tooling",
    }
)
_FAILURE_KINDS = frozenset(
    {
        "invalid-plan",
        "required-evidence-missing",
        "required-evidence-skipped",
        "blocking-validation-failure",
        "inadmissible-receipt",
        "final-evidence-failure",
        "fail-closed",
    }
)
_VALID_RECEIPT_SELECTOR_ASSIGNMENT_MSG = (
    "must match selector assignment for valid observed receipts"
)
_VALID_RECEIPT_SELECTOR_CONTEXT_MSG = (
    "requires verified selector assignment context for valid observed receipts"
)
_RESULT_OUTCOMES = frozenset({"satisfied", "missing", "skipped", "failed"})
_RECEIPT_ADMISSIBILITIES = frozenset({"valid", "inadmissible"})

_BLOCKING_VALIDATION_FAILURE_FAMILIES = (
    DiagnosticFamily.DESCRIPTOR_INVALID.value,
    DiagnosticFamily.ARTIFACT_SHAPE_UNCONFIRMED.value,
    DiagnosticFamily.VALIDATION_WORK_FAILED.value,
)

_MODES = frozenset({"pull_request", "push", "scheduled_full", "unknown"})
_AFFECTED_STATUSES = frozenset(
    {"available", "unavailable", "not-applicable", "unknown"}
)

_MANIFEST_ROOT_KEYS = frozenset(
    {
        "api-version",
        "kind",
        "created-at",
        "repository",
        "run",
        "schema-diagnostics",
        "plan-id",
        "plan-digest",
        "receipt-namespace-closure",
        "entries",
    }
)
_MANIFEST_CLOSURE_KEYS = frozenset(
    {"source", "closed-receipt-count", "observed-entry-ids"}
)
_MANIFEST_ENTRY_KEYS = frozenset(
    {
        "observed-entry-id",
        "artifact-ref",
        "physical-artifact-name",
        "artifact-instance-id",
        "assignment-id",
        "writer-work-group-id",
        "trusted-writer-id",
        "observed-writer-id",
        "writer-observation-ref",
        "receipt-id",
        "receipt-content-digest",
    }
)
_AGGREGATE_ROOT_KEYS = frozenset(
    {
        "api-version",
        "kind",
        "created-at",
        "repository",
        "run",
        "schema-diagnostics",
        "plan-id",
        "plan-digest",
        "mode",
        "receipt-manifest",
        "validation-tree",
        "affected-range",
        "request",
        "scheduled-full",
        "verdict",
        "reason",
        "diagnostics",
        "observed-receipts",
        "evidence-results",
        "failures",
        "work-groups",
        "proof-admissibility",
    }
)
_REASON_KEYS = frozenset(
    {
        "invalid-plan",
        "fail-closed",
        "required-evidence-missing",
        "required-evidence-skipped",
        "blocking-validation-failure",
        "inadmissible-receipt",
        "final-evidence-failure",
    }
)
_AGGREGATE_MANIFEST_KEYS = frozenset({"artifact-ref", "content-digest"})
_VALIDATION_TREE_KEYS = frozenset({"commit-sha", "ref"})
_AFFECTED_RANGE_KEYS = frozenset(
    {"status", "base-sha", "base-tip-sha", "head-sha", "changed-files-hash"}
)
_REQUEST_KEYS = frozenset({"artifact-ref", "request-digest"})
_SCHEDULED_FULL_KEYS = frozenset({"enabled"})
_OBSERVED_RECEIPT_KEYS = frozenset(
    {
        "observed-entry-id",
        "artifact-ref",
        "physical-artifact-name",
        "artifact-instance-id",
        "receipt-id",
        "work-group-id",
        "receipt-content-digest",
        "admissibility",
        "diagnostics",
    }
)
_EVIDENCE_RESULT_KEYS = frozenset(
    {
        "evidence-expectation-id",
        "work-group-id",
        "receipt-id",
        "observed-entry-id",
        "receipt-artifact-ref",
        "receipt-content-digest",
        "outcome",
        "diagnostics",
    }
)
_FAILURE_KEYS = frozenset(
    {
        "kind",
        "work-group-id",
        "evidence-expectation-id",
        "receipt-id",
        "observed-entry-id",
        "receipt-artifact-ref",
        "receipt-content-digest",
        "diagnostic",
        "message",
    }
)
_WORK_GROUP_COUNTS_KEYS = frozenset(
    {
        "executable-required",
        "required-succeeded",
        "required-failed",
        "required-skipped",
        "required-missing",
        "terminal-aggregation",
    }
)
_DIAGNOSTIC_KEYS = frozenset(
    {
        "diagnostic-id",
        "code",
        "detail",
        "message",
        "source",
        "severity",
        "verdict-effect",
    }
)
_DIAGNOSTIC_SOURCE_KEYS = frozenset({"type", "id"})


@dataclass(frozen=True, slots=True)
class CiValidationObservedReceiptInput:
    """One manifest entry plus independently observed readable payloads."""

    manifest_entry: Mapping[str, object]
    receipt: Mapping[str, object] | None = None
    raw_receipt_bytes: bytes | None = None
    validation_result: Mapping[str, object] | None = None


def ci_validation_observed_entry_id(
    *,
    run_id: str,
    run_attempt: str,
    artifact_ref: str | None,
    artifact_instance_id: str,
) -> str:
    """Return the stable observation ID for a receipt-like artifact."""
    issues: list[ValidationIssue] = []
    _validate_non_empty_string(run_id, "run-id", issues)
    _validate_non_empty_string(run_attempt, "run-attempt", issues)
    _validate_non_empty_string(
        artifact_instance_id, "artifact-instance-id", issues
    )
    if artifact_ref is not None:
        _validate_artifact_ref(artifact_ref, "artifact-ref", issues)
    if issues:
        raise ContractValidationError(issues)
    digest = canonical_json_digest(
        {
            "artifact-ref": artifact_ref,
            "artifact-instance-id": artifact_instance_id,
            "run-attempt": run_attempt,
            "run-id": run_id,
        }
    )
    return f"receipt-{digest}"


def ci_validation_receipt_manifest_content_digest(
    raw_manifest_bytes: bytes,
) -> str:
    """Return the SHA-256 digest for raw receipt-manifest bytes."""
    if not isinstance(raw_manifest_bytes, bytes):
        raise ContractValidationError(
            [ValidationIssue("raw-manifest-bytes", "must be bytes")]
        )
    return hashlib.sha256(raw_manifest_bytes).hexdigest()


def ci_validation_receipt_manifest_payload_digest(
    manifest: Mapping[str, object],
) -> str:
    """Return the canonical payload digest for a receipt manifest."""
    return _payload_digest(manifest, "manifest")


def ci_validation_aggregate_content_digest(raw_aggregate_bytes: bytes) -> str:
    """Return the SHA-256 digest for raw aggregate bytes."""
    if not isinstance(raw_aggregate_bytes, bytes):
        raise ContractValidationError(
            [ValidationIssue("raw-aggregate-bytes", "must be bytes")]
        )
    return hashlib.sha256(raw_aggregate_bytes).hexdigest()


def ci_validation_aggregate_payload_digest(
    aggregate: Mapping[str, object],
) -> str:
    """Return the canonical payload digest for an aggregate report."""
    return _payload_digest(aggregate, "aggregate")


def freeze_ci_validation_receipt_manifest(  # noqa: PLR0913
    *,
    plan: Mapping[str, object] | None,
    entries: Sequence[Mapping[str, object]],
    created_at: str,
    repository_owner: str | None = None,
    repository_name: str | None = None,
    workflow: str | None = None,
    run_id: str | None = None,
    run_attempt: str | None = None,
    changed_files_snapshot: Mapping[str, object] | None = None,
    fact_snapshot: Mapping[str, object] | None = None,
    pull_request_merge_commit_verification: Mapping[str, object] | None = None,
) -> dict[str, object]:
    """Freeze the closed receipt namespace manifest with canonical ordering."""
    envelope = _manifest_envelope_from_plan_or_args(
        plan=plan,
        repository_owner=repository_owner,
        repository_name=repository_name,
        workflow=workflow,
        run_id=run_id,
        run_attempt=run_attempt,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        pull_request_merge_commit_verification=(
            pull_request_merge_commit_verification
        ),
    )
    issues: list[ValidationIssue] = []
    _validate_non_empty_string(created_at, "created-at", issues)
    sorted_entries = [dict(item) for item in entries]
    for index, entry in enumerate(sorted_entries):
        _validate_manifest_entry(entry, f"entries[{index}]", issues)
    if issues:
        raise ContractValidationError(issues)
    sorted_entries.sort(key=lambda item: str(item.get("observed-entry-id")))
    manifest = {
        "api-version": API_VERSIONS_BY_KIND[
            CiValidationKind.RECEIPT_MANIFEST.value
        ],
        "kind": CiValidationKind.RECEIPT_MANIFEST.value,
        "created-at": created_at,
        "repository": {
            "owner": envelope.repository_owner,
            "name": envelope.repository_name,
        },
        "run": {
            "workflow": envelope.workflow,
            "run-id": envelope.run_id,
            "run-attempt": envelope.run_attempt,
        },
        "schema-diagnostics": [],
        "plan-id": plan.get("plan-id") if plan is not None else None,
        "plan-digest": _verified_plan_digest(plan)
        if plan is not None
        else None,
        "receipt-namespace-closure": {
            "source": "aggregate-evidence",
            "closed-receipt-count": len(sorted_entries),
            "observed-entry-ids": [
                item["observed-entry-id"] for item in sorted_entries
            ],
        },
        "entries": sorted_entries,
    }
    validate_ci_validation_receipt_manifest(
        manifest,
        plan=plan,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        pull_request_merge_commit_verification=(
            pull_request_merge_commit_verification
        ),
    )
    return manifest


def validate_ci_validation_receipt_manifest(  # noqa: C901,PLR0912,PLR0913,PLR0915
    manifest: object,
    *,
    plan: Mapping[str, object] | None = None,
    changed_files_snapshot: Mapping[str, object] | None = None,
    fact_snapshot: Mapping[str, object] | None = None,
    pull_request_merge_commit_verification: Mapping[str, object] | None = None,
    expected_run_id: str | None = None,
    expected_run_attempt: str | None = None,
) -> None:
    """Validate a receipt manifest and its namespace closure."""
    if not isinstance(manifest, Mapping):
        raise ContractValidationError(
            [ValidationIssue("$", "must be an object")]
        )
    issues: list[ValidationIssue] = []
    _validate_canonical(manifest, "$", issues)
    envelope = _envelope_or_collect(
        manifest,
        CiValidationKind.RECEIPT_MANIFEST,
        issues,
    )
    _validate_root_keys(manifest, _MANIFEST_ROOT_KEYS, "$", issues)
    if (
        expected_run_id is not None
        and envelope is not None
        and envelope.run_id != expected_run_id
    ):
        issues.append(
            ValidationIssue("$.run.run-id", "must match expected run")
        )
    if (
        expected_run_attempt is not None
        and envelope is not None
        and envelope.run_attempt != expected_run_attempt
    ):
        issues.append(
            ValidationIssue(
                "$.run.run-attempt", "must match expected run attempt"
            )
        )
    if plan is None:
        if manifest.get("plan-id") is not None:
            issues.append(
                ValidationIssue("$.plan-id", "must be null without plan")
            )
        if manifest.get("plan-digest") is not None:
            issues.append(
                ValidationIssue("$.plan-digest", "must be null without plan")
            )
    else:
        plan_envelope = _validated_plan_envelope(
            plan,
            issues,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            pull_request_merge_commit_verification=(
                pull_request_merge_commit_verification
            ),
            expected_run_id=expected_run_id,
            expected_run_attempt=expected_run_attempt,
        )
        if envelope is not None and plan_envelope is not None:
            _validate_envelope_matches(envelope, plan_envelope, issues)
        if manifest.get("plan-id") != plan.get("plan-id"):
            issues.append(ValidationIssue("$.plan-id", "must match plan"))
        if manifest.get("plan-digest") != _verified_plan_digest(plan):
            issues.append(ValidationIssue("$.plan-digest", "must match plan"))
    entries_value = manifest.get("entries")
    entries: list[Mapping[str, object]] = []
    if not isinstance(entries_value, Sequence) or isinstance(
        entries_value, str | bytes
    ):
        issues.append(ValidationIssue("$.entries", "must be an array"))
    else:
        seen: set[str] = set()
        previous: str | None = None
        for index, entry in enumerate(entries_value):
            path = f"$.entries[{index}]"
            if not isinstance(entry, Mapping):
                issues.append(ValidationIssue(path, "must be an object"))
                continue
            _validate_manifest_entry(entry, path, issues)
            if envelope is not None:
                _validate_manifest_receipt_artifact_ref(
                    entry, envelope, path, issues
                )
                _validate_manifest_observed_entry_id(
                    entry, envelope, path, issues
                )
            entry_id = entry.get("observed-entry-id")
            if isinstance(entry_id, str):
                if entry_id in seen:
                    issues.append(
                        ValidationIssue(
                            f"{path}.observed-entry-id", "must be unique"
                        )
                    )
                seen.add(entry_id)
                if previous is not None and previous > entry_id:
                    issues.append(
                        ValidationIssue("$.entries", "must be sorted")
                    )
                previous = entry_id
            entries.append(entry)
    _validate_manifest_closure(
        manifest.get("receipt-namespace-closure"), entries, issues
    )
    if issues:
        raise ContractValidationError(issues)


def freeze_ci_validation_aggregate(  # noqa: PLR0913
    *,
    plan: Mapping[str, object],
    receipt_manifest: Mapping[str, object],
    selector_assignments_manifest: Mapping[str, object],
    observed_receipts: Sequence[
        CiValidationObservedReceiptInput | Mapping[str, object]
    ],
    created_at: str,
    changed_files_snapshot: Mapping[str, object] | None = None,
    fact_snapshot: Mapping[str, object] | None = None,
    pull_request_merge_commit_verification: Mapping[str, object] | None = None,
    final_evidence_diagnostics: Sequence[Mapping[str, object]] = (),
) -> dict[str, object]:
    """Freeze the final aggregate verdict for a structurally valid plan."""
    validate_ci_validation_plan(
        plan,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        pull_request_merge_commit_verification=(
            pull_request_merge_commit_verification
        ),
    )
    validate_ci_validation_selector_assignments(
        selector_assignments_manifest,
        plan=plan,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        pull_request_merge_commit_verification=(
            pull_request_merge_commit_verification
        ),
    )
    validate_ci_validation_receipt_manifest(
        receipt_manifest,
        plan=plan,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        pull_request_merge_commit_verification=(
            pull_request_merge_commit_verification
        ),
    )
    issues: list[ValidationIssue] = []
    _validate_non_empty_string(created_at, "created-at", issues)
    if issues:
        raise ContractValidationError(issues)

    envelope = _envelope(plan, CiValidationKind.PLAN)
    manifest_digest = ci_validation_receipt_manifest_payload_digest(
        receipt_manifest
    )
    inputs = _normalize_observed_inputs(observed_receipts)
    summaries, receipts_by_entry = _receipt_summaries(
        plan=plan,
        selector_assignments_manifest=selector_assignments_manifest,
        observed_inputs=inputs,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        pull_request_merge_commit_verification=(
            pull_request_merge_commit_verification
        ),
    )
    summaries = _apply_duplicate_admissibility(
        plan=plan,
        summaries=summaries,
        receipts_by_entry=receipts_by_entry,
        observed_inputs=inputs,
        selector_assignments_manifest=selector_assignments_manifest,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        pull_request_merge_commit_verification=(
            pull_request_merge_commit_verification
        ),
    )
    evidence_results, failures = _evidence_results_and_failures(
        plan=plan,
        summaries=summaries,
        receipts_by_entry=receipts_by_entry,
        observed_inputs=inputs,
        selector_assignments_manifest=selector_assignments_manifest,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        pull_request_merge_commit_verification=(
            pull_request_merge_commit_verification
        ),
    )
    fail_closed_failures = _fail_closed_failures(plan)
    final_failures = _final_evidence_failures(final_evidence_diagnostics)
    failures.extend(fail_closed_failures)
    failures.extend(final_failures)
    diagnostics = _aggregate_diagnostics(summaries, evidence_results, failures)
    counts = _work_group_counts(evidence_results)
    reason = {
        "invalid-plan": False,
        "fail-closed": bool(fail_closed_failures),
        "required-evidence-missing": any(
            item["kind"] == "required-evidence-missing" for item in failures
        ),
        "required-evidence-skipped": any(
            item["kind"] == "required-evidence-skipped" for item in failures
        ),
        "blocking-validation-failure": any(
            item["kind"] == "blocking-validation-failure" for item in failures
        ),
        "inadmissible-receipt": any(
            item["kind"] == "inadmissible-receipt" for item in failures
        ),
        "final-evidence-failure": bool(final_failures),
    }
    aggregate = {
        "api-version": API_VERSIONS_BY_KIND[CiValidationKind.AGGREGATE.value],
        "kind": CiValidationKind.AGGREGATE.value,
        "created-at": created_at,
        "repository": {
            "owner": envelope.repository_owner,
            "name": envelope.repository_name,
        },
        "run": {
            "workflow": envelope.workflow,
            "run-id": envelope.run_id,
            "run-attempt": envelope.run_attempt,
        },
        "schema-diagnostics": [],
        "plan-id": plan["plan-id"],
        "plan-digest": _verified_plan_digest(plan),
        "mode": plan["mode"],
        "receipt-manifest": {
            "artifact-ref": ci_validation_receipt_manifest_artifact_ref(
                run_id=envelope.run_id,
                run_attempt=envelope.run_attempt,
            ),
            "content-digest": manifest_digest,
        },
        "validation-tree": _aggregate_validation_tree(plan),
        "affected-range": _aggregate_affected_range(plan),
        "request": _aggregate_request(plan),
        "scheduled-full": _aggregate_scheduled_full(plan),
        "verdict": "failed" if any(reason.values()) else "passed",
        "reason": reason,
        "diagnostics": _sort_diagnostics(diagnostics),
        "observed-receipts": _sort_observed_receipts(summaries),
        "evidence-results": _sort_evidence_results(evidence_results),
        "failures": _sort_failures(failures),
        "work-groups": counts,
        "proof-admissibility": "validation-only",
    }
    validate_ci_validation_aggregate(
        aggregate,
        plan=plan,
        receipt_manifest=receipt_manifest,
        selector_assignments_manifest=selector_assignments_manifest,
        observed_receipts=inputs,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        pull_request_merge_commit_verification=(
            pull_request_merge_commit_verification
        ),
    )
    return aggregate


def freeze_ci_validation_invalid_plan_aggregate(  # noqa: PLR0913
    *,
    created_at: str,
    repository_owner: str,
    repository_name: str,
    workflow: str,
    run_id: str,
    run_attempt: str,
    diagnostic_detail: str = DiagnosticDetail.STRUCTURALLY_INVALID.value,
    plan: Mapping[str, object] | None = None,
    receipt_manifest: Mapping[str, object] | None = None,
    observed_receipts: Sequence[Mapping[str, object]] = (),
    post_plan_contract_invalid: bool = False,
    changed_files_snapshot: Mapping[str, object] | None = None,
    fact_snapshot: Mapping[str, object] | None = None,
    pull_request_merge_commit_verification: Mapping[str, object] | None = None,
) -> dict[str, object]:
    """Freeze the invalid-plan/post-plan-contract-invalid aggregate mode."""
    issues: list[ValidationIssue] = []
    for key, value in {
        "created-at": created_at,
        "repository-owner": repository_owner,
        "repository-name": repository_name,
        "workflow": workflow,
        "run-id": run_id,
        "run-attempt": run_attempt,
    }.items():
        _validate_non_empty_string(value, key, issues)
    if (
        diagnostic_detail
        not in DETAILS_BY_DIAGNOSTIC_CODE[DiagnosticFamily.INVALID_PLAN.value]
    ):
        issues.append(
            ValidationIssue(
                "diagnostic-detail", "is not valid for invalid-plan"
            )
        )
    if issues:
        raise ContractValidationError(issues)
    diagnostic = _diagnostic(
        diagnostic_id=f"invalid-plan/{diagnostic_detail}",
        code=DiagnosticFamily.INVALID_PLAN.value,
        detail=diagnostic_detail,
        message="CI validation plan evidence is not authoritative",
    )
    failure = _failure(
        kind="invalid-plan",
        diagnostic=diagnostic,
        message="CI validation plan evidence is not authoritative",
    )
    observed = [
        _inspection_observed_receipt(item)
        for item in observed_receipts
        if isinstance(item, Mapping)
    ]
    plan_fields = _verified_plan_fields_for_invalid_mode(
        plan if post_plan_contract_invalid else None,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        pull_request_merge_commit_verification=(
            pull_request_merge_commit_verification
        ),
    )
    manifest_ref = None
    manifest_digest = None
    if receipt_manifest is not None:
        manifest_ref = ci_validation_receipt_manifest_artifact_ref(
            run_id=run_id,
            run_attempt=run_attempt,
        )
        manifest_digest = ci_validation_receipt_manifest_payload_digest(
            receipt_manifest
        )
    aggregate = {
        "api-version": API_VERSIONS_BY_KIND[CiValidationKind.AGGREGATE.value],
        "kind": CiValidationKind.AGGREGATE.value,
        "created-at": created_at,
        "repository": {"owner": repository_owner, "name": repository_name},
        "run": {
            "workflow": workflow,
            "run-id": run_id,
            "run-attempt": run_attempt,
        },
        "schema-diagnostics": [],
        "plan-id": plan_fields["plan-id"],
        "plan-digest": plan_fields["plan-digest"],
        "mode": plan_fields["mode"],
        "receipt-manifest": {
            "artifact-ref": manifest_ref,
            "content-digest": manifest_digest,
        },
        "validation-tree": plan_fields["validation-tree"],
        "affected-range": plan_fields["affected-range"],
        "request": plan_fields["request"],
        "scheduled-full": plan_fields["scheduled-full"],
        "verdict": "failed",
        "reason": {
            "invalid-plan": True,
            "fail-closed": False,
            "required-evidence-missing": False,
            "required-evidence-skipped": False,
            "blocking-validation-failure": False,
            "inadmissible-receipt": False,
            "final-evidence-failure": False,
        },
        "diagnostics": sorted(
            _aggregate_diagnostics(observed, [], [failure]),
            key=lambda item: str(item["diagnostic-id"]),
        ),
        "observed-receipts": _sort_observed_receipts(observed),
        "evidence-results": [],
        "failures": [failure],
        "work-groups": _zero_work_group_counts(),
        "proof-admissibility": "validation-only",
    }
    validate_ci_validation_aggregate(
        aggregate,
        plan=plan if plan_fields["plan-id"] is not None else None,
        receipt_manifest=receipt_manifest,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        pull_request_merge_commit_verification=(
            pull_request_merge_commit_verification
        ),
    )
    return aggregate


def validate_ci_validation_aggregate(  # noqa: C901,PLR0913
    aggregate: object,
    *,
    plan: Mapping[str, object] | None = None,
    receipt_manifest: Mapping[str, object] | None = None,
    selector_assignments_manifest: Mapping[str, object] | None = None,
    observed_receipts: Sequence[
        CiValidationObservedReceiptInput | Mapping[str, object]
    ]
    | None = None,
    changed_files_snapshot: Mapping[str, object] | None = None,
    fact_snapshot: Mapping[str, object] | None = None,
    pull_request_merge_commit_verification: Mapping[str, object] | None = None,
    expected_run_id: str | None = None,
    expected_run_attempt: str | None = None,
) -> None:
    """Validate a CI validation aggregate report."""
    if not isinstance(aggregate, Mapping):
        raise ContractValidationError(
            [ValidationIssue("$", "must be an object")]
        )
    issues: list[ValidationIssue] = []
    _validate_canonical(aggregate, "$", issues)
    envelope = _envelope_or_collect(
        aggregate, CiValidationKind.AGGREGATE, issues
    )
    _validate_root_keys(aggregate, _AGGREGATE_ROOT_KEYS, "$", issues)
    if (
        expected_run_id is not None
        and envelope is not None
        and envelope.run_id != expected_run_id
    ):
        issues.append(
            ValidationIssue("$.run.run-id", "must match expected run")
        )
    if (
        expected_run_attempt is not None
        and envelope is not None
        and envelope.run_attempt != expected_run_attempt
    ):
        issues.append(
            ValidationIssue(
                "$.run.run-attempt", "must match expected run attempt"
            )
        )
    if plan is not None:
        plan_envelope = _validated_plan_envelope(
            plan,
            issues,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            pull_request_merge_commit_verification=(
                pull_request_merge_commit_verification
            ),
            expected_run_id=expected_run_id,
            expected_run_attempt=expected_run_attempt,
        )
        if envelope is not None and plan_envelope is not None:
            _validate_envelope_matches(envelope, plan_envelope, issues)
        _validate_aggregate_plan_bindings(aggregate, plan, issues)
        if not _is_invalid_plan_aggregate(aggregate):
            _validate_evidence_results_match_plan(aggregate, plan, issues)
    elif plan is None:
        if not _is_invalid_plan_aggregate(aggregate):
            issues.append(
                ValidationIssue(
                    "$.plan-id",
                    "requires a validated plan for non-invalid aggregate",
                )
            )
        _validate_invalid_plan_unverified_fields(aggregate, issues)
    if selector_assignments_manifest is not None:
        _validate_supplied_selector_assignments_manifest(
            selector_assignments_manifest,
            plan=plan,
            envelope=envelope,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            pull_request_merge_commit_verification=(
                pull_request_merge_commit_verification
            ),
            issues=issues,
        )
    if receipt_manifest is not None:
        _validate_supplied_receipt_manifest(
            receipt_manifest,
            plan=plan,
            envelope=envelope,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            pull_request_merge_commit_verification=(
                pull_request_merge_commit_verification
            ),
            issues=issues,
        )
        _validate_manifest_binding(
            aggregate,
            receipt_manifest,
            selector_assignments_manifest,
            issues,
        )
    _validate_aggregate_shapes(aggregate, envelope, issues)
    _validate_receipt_manifest_ref_binding(aggregate, envelope, issues)
    _validate_aggregate_consistency(
        aggregate,
        plan,
        _normalize_observed_inputs(observed_receipts)
        if observed_receipts is not None
        else None,
        selector_assignments_manifest,
        changed_files_snapshot,
        fact_snapshot,
        pull_request_merge_commit_verification,
        issues,
    )
    if issues:
        raise ContractValidationError(issues)


def _payload_digest(value: Mapping[str, object], name: str) -> str:
    try:
        return hashlib.sha256(canonical_json_bytes(value)).hexdigest()
    except (TypeError, ValueError) as error:
        raise ContractValidationError(
            [ValidationIssue(name, str(error))]
        ) from error


def _receipt_digest_matches_payload(
    entry: Mapping[str, object],
    raw_receipt_bytes: bytes | None,
) -> bool:
    try:
        if raw_receipt_bytes is None:
            return False
        observed_digest = ci_validation_receipt_content_digest(
            raw_receipt_bytes
        )
    except (ContractValidationError, TypeError, ValueError):
        return False
    return entry.get("receipt-content-digest") == observed_digest


def _receipt_payload_matches_observed_bytes(
    entry: Mapping[str, object],
    receipt: Mapping[str, object],
    raw_receipt_bytes: bytes | None,
) -> bool:
    if raw_receipt_bytes is None or not _receipt_digest_matches_payload(
        entry, raw_receipt_bytes
    ):
        return False
    try:
        return load_ci_validation_receipt_payload(raw_receipt_bytes) == receipt
    except (ContractValidationError, TypeError, ValueError):
        return False


def _manifest_envelope_from_plan_or_args(  # noqa: PLR0913
    *,
    plan: Mapping[str, object] | None,
    repository_owner: str | None,
    repository_name: str | None,
    workflow: str | None,
    run_id: str | None,
    run_attempt: str | None,
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    pull_request_merge_commit_verification: Mapping[str, object] | None,
) -> CommonEnvelope:
    if plan is not None:
        issues: list[ValidationIssue] = []
        envelope = _validated_plan_envelope(
            plan,
            issues,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            pull_request_merge_commit_verification=(
                pull_request_merge_commit_verification
            ),
        )
        if issues or envelope is None:
            raise ContractValidationError(issues)
        return envelope
    issues = []
    for key, value in {
        "repository-owner": repository_owner,
        "repository-name": repository_name,
        "workflow": workflow,
        "run-id": run_id,
        "run-attempt": run_attempt,
    }.items():
        _validate_non_empty_string(value, key, issues)
    if issues:
        raise ContractValidationError(issues)
    return CommonEnvelope(
        api_version=API_VERSIONS_BY_KIND[
            CiValidationKind.RECEIPT_MANIFEST.value
        ],
        kind=CiValidationKind.RECEIPT_MANIFEST.value,
        created_at="1970-01-01T00:00:00Z",
        repository_owner=cast("str", repository_owner),
        repository_name=cast("str", repository_name),
        workflow=cast("str", workflow),
        run_id=cast("str", run_id),
        run_attempt=cast("str", run_attempt),
    )


def _normalize_observed_inputs(
    observed_receipts: Sequence[
        CiValidationObservedReceiptInput | Mapping[str, object]
    ],
) -> list[CiValidationObservedReceiptInput]:
    normalized: list[CiValidationObservedReceiptInput] = []
    for item in observed_receipts:
        if isinstance(item, CiValidationObservedReceiptInput):
            normalized.append(item)
        elif isinstance(item, Mapping):
            entry = item.get("manifest-entry")
            receipt = item.get("receipt")
            raw_receipt_bytes = item.get("raw-receipt-bytes")
            validation_result = item.get("validation-result")
            if not isinstance(entry, Mapping):
                entry = item
            normalized.append(
                CiValidationObservedReceiptInput(
                    manifest_entry=entry,
                    receipt=receipt if isinstance(receipt, Mapping) else None,
                    raw_receipt_bytes=raw_receipt_bytes
                    if isinstance(raw_receipt_bytes, bytes)
                    else None,
                    validation_result=validation_result
                    if isinstance(validation_result, Mapping)
                    else None,
                )
            )
    return normalized


def _receipt_summaries(  # noqa: C901,PLR0913
    *,
    plan: Mapping[str, object],
    selector_assignments_manifest: Mapping[str, object],
    observed_inputs: Sequence[CiValidationObservedReceiptInput],
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    pull_request_merge_commit_verification: Mapping[str, object] | None,
) -> tuple[list[dict[str, object]], dict[str, Mapping[str, object]]]:
    assignments = _assignments_by_work_group(selector_assignments_manifest)
    summaries: list[dict[str, object]] = []
    receipts_by_entry: dict[str, Mapping[str, object]] = {}
    for item in observed_inputs:
        entry = item.manifest_entry
        entry_id = str(entry.get("observed-entry-id"))
        receipt = item.receipt
        diagnostic: dict[str, object] | None = None
        valid = False
        work_group_id = _entry_work_group_id(entry, receipt)
        if receipt is None:
            diagnostic = _inadmissible_diagnostic(
                entry_id,
                DiagnosticDetail.MALFORMED_RECEIPT.value,
                "Receipt artifact is not readable as a valid payload",
            )
        elif work_group_id is None:
            diagnostic = _inadmissible_diagnostic(
                entry_id,
                DiagnosticDetail.UNKNOWN_WORK_GROUP.value,
                "Receipt does not identify an executable work group",
            )
        elif _work_group_kind(plan, work_group_id) == _TERMINAL_WORK_GROUP_KIND:
            diagnostic = _inadmissible_diagnostic(
                entry_id,
                DiagnosticDetail.UNEXPECTED_RECEIPT.value,
                "Terminal aggregation work group must not emit a receipt",
            )
        elif entry.get("receipt-content-digest") is None:
            diagnostic = _inadmissible_diagnostic(
                entry_id,
                DiagnosticDetail.MALFORMED_RECEIPT.value,
                "Readable receipt artifact is missing observed content digest",
            )
        elif not _receipt_payload_matches_observed_bytes(
            entry,
            receipt,
            item.raw_receipt_bytes,
        ):
            diagnostic = _inadmissible_diagnostic(
                entry_id,
                DiagnosticDetail.MISMATCHED_EVIDENCE_PAYLOAD.value,
                "Receipt content digest does not match readable payload",
            )
        elif work_group_id not in assignments:
            diagnostic = _inadmissible_diagnostic(
                entry_id,
                DiagnosticDetail.UNKNOWN_WORK_GROUP.value,
                "Receipt work group is not assigned",
            )
        elif not _manifest_entry_matches_assignment(
            entry, assignments[work_group_id]
        ):
            diagnostic = _inadmissible_diagnostic(
                entry_id,
                DiagnosticDetail.MISMATCHED_WRITER_IDENTITY.value,
                "Receipt manifest writer identity does not match assignment",
            )
        else:
            try:
                validate_ci_validation_receipt(
                    receipt,
                    plan=plan,
                    selector_assignments_manifest=selector_assignments_manifest,
                    assignment=assignments[work_group_id],
                    changed_files_snapshot=changed_files_snapshot,
                    fact_snapshot=fact_snapshot,
                    pull_request_merge_commit_verification=(
                        pull_request_merge_commit_verification
                    ),
                )
            except ContractValidationError as error:
                diagnostic = _inadmissible_diagnostic(
                    entry_id,
                    _receipt_error_detail(error),
                    "Receipt payload is not admissible",
                )
            else:
                valid = True
                receipts_by_entry[entry_id] = receipt
        diagnostics = [] if diagnostic is None else [diagnostic]
        summaries.append(
            {
                "observed-entry-id": entry.get("observed-entry-id"),
                "artifact-ref": entry.get("artifact-ref"),
                "physical-artifact-name": entry.get("physical-artifact-name"),
                "artifact-instance-id": entry.get("artifact-instance-id"),
                "receipt-id": entry.get("receipt-id"),
                "work-group-id": work_group_id,
                "receipt-content-digest": entry.get("receipt-content-digest"),
                "admissibility": "valid" if valid else "inadmissible",
                "diagnostics": diagnostics,
            }
        )
    return summaries, receipts_by_entry


def _apply_duplicate_admissibility(  # noqa: PLR0913
    *,
    plan: Mapping[str, object],
    summaries: list[dict[str, object]],
    receipts_by_entry: dict[str, Mapping[str, object]],
    observed_inputs: Sequence[CiValidationObservedReceiptInput],
    selector_assignments_manifest: Mapping[str, object],
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    pull_request_merge_commit_verification: Mapping[str, object] | None,
) -> list[dict[str, object]]:
    valid_by_work_group: dict[str, list[dict[str, object]]] = {}
    for summary in summaries:
        if summary.get("admissibility") == "valid" and isinstance(
            summary.get("work-group-id"), str
        ):
            valid_by_work_group.setdefault(
                cast("str", summary["work-group-id"]), []
            ).append(summary)
    for work_group_id, group in valid_by_work_group.items():
        sorted_group = sorted(
            group, key=lambda item: str(item["observed-entry-id"])
        )
        if len(sorted_group) <= 1:
            continue
        keep_valid = _valid_reused_chain_entry_ids_for_duplicate_group(
            plan=plan,
            work_group_id=work_group_id,
            summaries=sorted_group,
            receipts_by_entry=receipts_by_entry,
            observed_inputs=observed_inputs,
            selector_assignments_manifest=selector_assignments_manifest,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            pull_request_merge_commit_verification=(
                pull_request_merge_commit_verification
            ),
        )
        if keep_valid is None:
            keep_valid = {cast("str", sorted_group[0]["observed-entry-id"])}
        for summary in sorted_group:
            entry_id = cast("str", summary["observed-entry-id"])
            if entry_id in keep_valid:
                continue
            _mark_duplicate_summary_inadmissible(
                summary, receipts_by_entry, entry_id
            )
    return summaries


def _mark_duplicate_summary_inadmissible(
    summary: dict[str, object],
    receipts_by_entry: dict[str, Mapping[str, object]],
    entry_id: str,
) -> None:
    summary["admissibility"] = "inadmissible"
    receipts_by_entry.pop(entry_id, None)
    summary["diagnostics"] = [
        _inadmissible_diagnostic(
            entry_id,
            DiagnosticDetail.DUPLICATE_RECEIPT.value,
            "More than one admissible receipt matched expectation",
        )
    ]


def _valid_reused_chain_entry_ids_for_duplicate_group(  # noqa: PLR0913
    *,
    plan: Mapping[str, object],
    work_group_id: str,
    summaries: Sequence[Mapping[str, object]],
    receipts_by_entry: Mapping[str, Mapping[str, object]],
    observed_inputs: Sequence[CiValidationObservedReceiptInput],
    selector_assignments_manifest: Mapping[str, object],
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    pull_request_merge_commit_verification: Mapping[str, object] | None,
) -> set[str] | None:
    if _work_group_kind(plan, work_group_id) != "release-shaped-artifact":
        return None
    group_entry_ids = {
        cast("str", summary["observed-entry-id"])
        for summary in summaries
        if isinstance(summary.get("observed-entry-id"), str)
    }
    chain_candidates: list[set[str]] = []
    for summary in summaries:
        entry_id = summary.get("observed-entry-id")
        if not isinstance(entry_id, str):
            continue
        receipt = receipts_by_entry.get(entry_id)
        if receipt is None or receipt.get("outcome") != "success":
            continue
        chain = _release_shaped_success_source_chain_entry_ids(
            receipt=receipt,
            plan=plan,
            selector_assignments_manifest=selector_assignments_manifest,
            work_group_id=work_group_id,
            entry_id=entry_id,
            receipts_by_entry=receipts_by_entry,
            observed_inputs=observed_inputs,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            pull_request_merge_commit_verification=(
                pull_request_merge_commit_verification
            ),
            visited_receipt_digests=set(),
        )
        if chain and chain <= group_entry_ids:
            chain_candidates.append({entry_id, *chain})
    maximal_candidates = _maximal_entry_id_sets(chain_candidates)
    if len(maximal_candidates) != 1:
        return None
    return maximal_candidates[0]


def _maximal_entry_id_sets(candidates: Sequence[set[str]]) -> list[set[str]]:
    unique: list[set[str]] = []
    for candidate in candidates:
        if not any(candidate == existing for existing in unique):
            unique.append(candidate)
    return [
        candidate
        for candidate in unique
        if not any(candidate < other for other in unique)
    ]


def _valid_evidence_summary_by_work_group(  # noqa: PLR0913
    *,
    plan: Mapping[str, object],
    work_group_id: str,
    summaries: Sequence[Mapping[str, object]],
    receipts_by_entry: Mapping[str, Mapping[str, object]],
    observed_inputs: Sequence[CiValidationObservedReceiptInput],
    selector_assignments_manifest: Mapping[str, object],
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    pull_request_merge_commit_verification: Mapping[str, object] | None,
) -> Mapping[str, object] | None:
    valid = [
        summary
        for summary in summaries
        if summary.get("admissibility") == "valid"
        and summary.get("work-group-id") == work_group_id
    ]
    if len(valid) <= 1:
        return valid[0] if valid else None
    if _work_group_kind(plan, work_group_id) == "release-shaped-artifact":
        candidates: list[tuple[Mapping[str, object], set[str]]] = []
        for summary in valid:
            entry_id = summary.get("observed-entry-id")
            if not isinstance(entry_id, str):
                continue
            receipt = receipts_by_entry.get(entry_id)
            if receipt is None:
                continue
            chain = _release_shaped_success_source_chain_entry_ids(
                receipt=receipt,
                plan=plan,
                selector_assignments_manifest=selector_assignments_manifest,
                work_group_id=work_group_id,
                entry_id=entry_id,
                receipts_by_entry=receipts_by_entry,
                observed_inputs=observed_inputs,
                changed_files_snapshot=changed_files_snapshot,
                fact_snapshot=fact_snapshot,
                pull_request_merge_commit_verification=(
                    pull_request_merge_commit_verification
                ),
                visited_receipt_digests=set(),
            )
            if chain:
                candidates.append((summary, {entry_id, *chain}))
        maximal_candidates = _maximal_entry_id_sets(
            [candidate for _summary, candidate in candidates]
        )
        if len(maximal_candidates) == 1:
            maximal = maximal_candidates[0]
            for summary, candidate in candidates:
                if candidate == maximal:
                    return summary
    return sorted(valid, key=lambda item: str(item["observed-entry-id"]))[0]


def _evidence_results_and_failures(  # noqa: PLR0913
    *,
    plan: Mapping[str, object],
    summaries: Sequence[Mapping[str, object]],
    receipts_by_entry: Mapping[str, Mapping[str, object]],
    observed_inputs: Sequence[CiValidationObservedReceiptInput],
    selector_assignments_manifest: Mapping[str, object],
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    pull_request_merge_commit_verification: Mapping[str, object] | None,
) -> tuple[list[dict[str, object]], list[dict[str, object]]]:
    failures: list[dict[str, object]] = []
    results: list[dict[str, object]] = []
    for summary in summaries:
        if summary.get("admissibility") == "inadmissible":
            for diagnostic in _diagnostics(summary.get("diagnostics")):
                failures.append(
                    _failure(
                        kind="inadmissible-receipt",
                        work_group_id=_nullable_str(
                            summary.get("work-group-id")
                        ),
                        receipt_id=_nullable_str(summary.get("receipt-id")),
                        observed_entry_id=_nullable_str(
                            summary.get("observed-entry-id")
                        ),
                        receipt_artifact_ref=_nullable_str(
                            summary.get("artifact-ref")
                        ),
                        receipt_content_digest=_nullable_str(
                            summary.get("receipt-content-digest")
                        ),
                        diagnostic=diagnostic,
                        message="Observed receipt is inadmissible",
                    )
                )
    for expectation in _evidence_expectations(plan):
        expectation_id = cast("str", expectation["evidence-expectation-id"])
        work_group_id = cast("str", expectation["work-group-id"])
        summary = _valid_evidence_summary_by_work_group(
            plan=plan,
            work_group_id=work_group_id,
            summaries=summaries,
            receipts_by_entry=receipts_by_entry,
            observed_inputs=observed_inputs,
            selector_assignments_manifest=selector_assignments_manifest,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            pull_request_merge_commit_verification=(
                pull_request_merge_commit_verification
            ),
        )
        if summary is None:
            diagnostic = _diagnostic(
                diagnostic_id=f"required-evidence-missing/{expectation_id}",
                code=DiagnosticFamily.REQUIRED_EVIDENCE_MISSING.value,
                detail=None,
                message="Required evidence receipt is missing",
            )
            result = _evidence_result(
                expectation_id=expectation_id,
                work_group_id=work_group_id,
                outcome="missing",
                diagnostic=diagnostic,
            )
            failures.append(
                _failure(
                    kind="required-evidence-missing",
                    work_group_id=work_group_id,
                    evidence_expectation_id=expectation_id,
                    diagnostic=diagnostic,
                    message="Required evidence receipt is missing",
                )
            )
            results.append(result)
            continue
        entry_id = cast("str", summary["observed-entry-id"])
        receipt = receipts_by_entry[entry_id]
        receipt_outcome = receipt.get("outcome")
        if receipt_outcome == "success":
            if (
                _work_group_kind(plan, work_group_id)
                == "release-shaped-artifact"
                and not _release_shaped_success_source_is_admissible(
                    receipt=receipt,
                    plan=plan,
                    selector_assignments_manifest=(
                        selector_assignments_manifest
                    ),
                    work_group_id=work_group_id,
                    entry_id=entry_id,
                    receipts_by_entry=receipts_by_entry,
                    observed_inputs=observed_inputs,
                    changed_files_snapshot=changed_files_snapshot,
                    fact_snapshot=fact_snapshot,
                    pull_request_merge_commit_verification=(
                        pull_request_merge_commit_verification
                    ),
                    visited_receipt_digests=set(),
                )
            ):
                diagnostic = _release_shaped_source_failure_diagnostic(
                    expectation_id=expectation_id,
                    work_group_id=work_group_id,
                )
                results.append(
                    _evidence_result(
                        expectation_id=expectation_id,
                        work_group_id=work_group_id,
                        outcome="failed",
                        summary=summary,
                        diagnostic=diagnostic,
                    )
                )
                failures.append(
                    _failure(
                        kind="blocking-validation-failure",
                        work_group_id=work_group_id,
                        evidence_expectation_id=expectation_id,
                        receipt_id=_nullable_str(summary.get("receipt-id")),
                        observed_entry_id=entry_id,
                        receipt_artifact_ref=_nullable_str(
                            summary.get("artifact-ref")
                        ),
                        receipt_content_digest=_nullable_str(
                            summary.get("receipt-content-digest")
                        ),
                        diagnostic=diagnostic,
                        message=(
                            "Release-shaped success lacks admissible source "
                            "evidence"
                        ),
                    )
                )
                continue
            results.append(
                _evidence_result(
                    expectation_id=expectation_id,
                    work_group_id=work_group_id,
                    outcome="satisfied",
                    summary=summary,
                )
            )
        elif receipt_outcome == "skipped":
            diagnostic = _diagnostic(
                diagnostic_id=f"required-evidence-skipped/{expectation_id}",
                code=DiagnosticFamily.REQUIRED_EVIDENCE_SKIPPED.value,
                detail=None,
                message="Required evidence receipt was skipped",
            )
            results.append(
                _evidence_result(
                    expectation_id=expectation_id,
                    work_group_id=work_group_id,
                    outcome="skipped",
                    summary=summary,
                    diagnostic=diagnostic,
                )
            )
            failures.append(
                _failure(
                    kind="required-evidence-skipped",
                    work_group_id=work_group_id,
                    evidence_expectation_id=expectation_id,
                    receipt_id=_nullable_str(summary.get("receipt-id")),
                    observed_entry_id=entry_id,
                    receipt_artifact_ref=_nullable_str(
                        summary.get("artifact-ref")
                    ),
                    receipt_content_digest=_nullable_str(
                        summary.get("receipt-content-digest")
                    ),
                    diagnostic=diagnostic,
                    message="Required evidence receipt was skipped",
                )
            )
        else:
            diagnostic = _blocking_validation_failure_diagnostic(
                receipt=receipt,
                expectation_id=expectation_id,
                work_group_id=work_group_id,
            )
            results.append(
                _evidence_result(
                    expectation_id=expectation_id,
                    work_group_id=work_group_id,
                    outcome="failed",
                    summary=summary,
                    diagnostic=diagnostic,
                )
            )
            failures.append(
                _failure(
                    kind="blocking-validation-failure",
                    work_group_id=work_group_id,
                    evidence_expectation_id=expectation_id,
                    receipt_id=_nullable_str(summary.get("receipt-id")),
                    observed_entry_id=entry_id,
                    receipt_artifact_ref=_nullable_str(
                        summary.get("artifact-ref")
                    ),
                    receipt_content_digest=_nullable_str(
                        summary.get("receipt-content-digest")
                    ),
                    diagnostic=diagnostic,
                    message="Required validation receipt failed",
                )
            )
    return results, failures


def _release_shaped_success_source_is_admissible(  # noqa: PLR0913
    *,
    receipt: Mapping[str, object],
    plan: Mapping[str, object],
    selector_assignments_manifest: Mapping[str, object],
    work_group_id: str,
    entry_id: str,
    receipts_by_entry: Mapping[str, Mapping[str, object]],
    observed_inputs: Sequence[CiValidationObservedReceiptInput],
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    pull_request_merge_commit_verification: Mapping[str, object] | None,
    visited_receipt_digests: set[str],
) -> bool:
    return (
        _release_shaped_success_source_chain_entry_ids(
            receipt=receipt,
            plan=plan,
            selector_assignments_manifest=selector_assignments_manifest,
            work_group_id=work_group_id,
            entry_id=entry_id,
            receipts_by_entry=receipts_by_entry,
            observed_inputs=observed_inputs,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            pull_request_merge_commit_verification=(
                pull_request_merge_commit_verification
            ),
            visited_receipt_digests=visited_receipt_digests,
        )
        is not None
    )


def _release_shaped_success_source_chain_entry_ids(  # noqa: C901,PLR0911,PLR0913
    *,
    receipt: Mapping[str, object],
    plan: Mapping[str, object],
    selector_assignments_manifest: Mapping[str, object],
    work_group_id: str,
    entry_id: str,
    receipts_by_entry: Mapping[str, Mapping[str, object]],
    observed_inputs: Sequence[CiValidationObservedReceiptInput],
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    pull_request_merge_commit_verification: Mapping[str, object] | None,
    visited_receipt_digests: set[str],
) -> set[str] | None:
    assignment = _assignments_by_work_group(selector_assignments_manifest).get(
        work_group_id
    )
    if not _release_shaped_receipt_validates_against_current_context(
        receipt=receipt,
        plan=plan,
        selector_assignments_manifest=selector_assignments_manifest,
        assignment=assignment,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        pull_request_merge_commit_verification=(
            pull_request_merge_commit_verification
        ),
    ):
        return None
    assignment = cast("Mapping[str, object]", assignment)
    detail = _release_shaped_receipt_detail(receipt)
    if detail is None:
        return None
    evidence_source = detail.get("evidence-source")
    observed_input = _observed_input_by_entry_id(observed_inputs).get(entry_id)
    if evidence_source == "no-publish-validation":
        if _no_publish_release_shaped_source_is_admissible(
            receipt=receipt,
            detail=detail,
            source_validation_result=(
                observed_input.validation_result
                if observed_input is not None
                else None
            ),
        ):
            return set()
        return None
    if evidence_source != "reused-validation-receipt":
        return None
    reused_receipt = detail.get("reused-receipt")
    if not isinstance(reused_receipt, Mapping):
        return None
    prior_input = _observed_reused_receipt_input(
        reused_receipt,
        observed_inputs,
        observed_commit_sha=_receipt_observed_commit_sha(receipt),
        work_group_id=work_group_id,
        expected_writer_id=assignment.get("trusted-writer-id"),
    )
    if prior_input is None:
        return None
    prior_entry_id = prior_input.manifest_entry.get("observed-entry-id")
    if not isinstance(prior_entry_id, str) or prior_entry_id == entry_id:
        return None
    prior_receipt = receipts_by_entry.get(prior_entry_id)
    if prior_receipt is None:
        return None
    prior_digest = prior_input.manifest_entry.get("receipt-content-digest")
    if (
        not isinstance(prior_digest, str)
        or prior_digest in visited_receipt_digests
        or prior_receipt.get("work-group-id") != work_group_id
        or not _release_shaped_reused_receipt_matches_source_results(
            current_detail=detail,
            source_receipt=prior_receipt,
        )
    ):
        return None
    try:
        validate_ci_validation_receipt(
            prior_receipt,
            plan=plan,
            selector_assignments_manifest=selector_assignments_manifest,
            assignment=assignment,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            pull_request_merge_commit_verification=(
                pull_request_merge_commit_verification
            ),
        )
    except ContractValidationError:
        return None
    prior_chain = _release_shaped_success_source_chain_entry_ids(
        receipt=prior_receipt,
        plan=plan,
        selector_assignments_manifest=selector_assignments_manifest,
        work_group_id=work_group_id,
        entry_id=prior_entry_id,
        receipts_by_entry=receipts_by_entry,
        observed_inputs=observed_inputs,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        pull_request_merge_commit_verification=(
            pull_request_merge_commit_verification
        ),
        visited_receipt_digests=visited_receipt_digests | {prior_digest},
    )
    if prior_chain is None:
        return None
    return {prior_entry_id, *prior_chain}


def _release_shaped_receipt_validates_against_current_context(  # noqa: PLR0913
    *,
    receipt: Mapping[str, object],
    plan: Mapping[str, object],
    selector_assignments_manifest: Mapping[str, object],
    assignment: Mapping[str, object] | None,
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    pull_request_merge_commit_verification: Mapping[str, object] | None,
) -> bool:
    if assignment is None:
        return False
    try:
        validate_ci_validation_receipt(
            receipt,
            plan=plan,
            selector_assignments_manifest=selector_assignments_manifest,
            assignment=assignment,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            pull_request_merge_commit_verification=(
                pull_request_merge_commit_verification
            ),
        )
    except ContractValidationError:
        return False
    return True


def _release_shaped_receipt_detail(
    receipt: Mapping[str, object],
) -> Mapping[str, object] | None:
    evidence = receipt.get("evidence")
    if not isinstance(evidence, Mapping):
        return None
    category_result = evidence.get("category-result")
    if not isinstance(category_result, Mapping):
        return None
    detail = category_result.get("detail")
    return detail if isinstance(detail, Mapping) else None


def _release_shaped_reused_results_match_source(
    *,
    current_detail: Mapping[str, object],
    source_detail: Mapping[str, object],
) -> bool:
    current_results = current_detail.get("artifact-obligation-results")
    source_results = source_detail.get("artifact-obligation-results")
    return (
        isinstance(current_results, Sequence)
        and not isinstance(current_results, str | bytes)
        and all(isinstance(item, Mapping) for item in current_results)
        and isinstance(source_results, Sequence)
        and not isinstance(source_results, str | bytes)
        and all(isinstance(item, Mapping) for item in source_results)
        and [dict(item) for item in current_results]
        == [dict(item) for item in source_results]
    )


def _release_shaped_reused_receipt_matches_source_results(
    *,
    current_detail: Mapping[str, object],
    source_receipt: Mapping[str, object],
) -> bool:
    source_detail = _release_shaped_receipt_detail(source_receipt)
    return (
        source_detail is not None
        and _release_shaped_reused_results_match_source(
            current_detail=current_detail,
            source_detail=source_detail,
        )
    )


def _observed_input_by_entry_id(
    observed_inputs: Sequence[CiValidationObservedReceiptInput],
) -> dict[str, CiValidationObservedReceiptInput]:
    return {
        entry_id: item
        for item in observed_inputs
        if isinstance(
            entry_id := item.manifest_entry.get("observed-entry-id"),
            str,
        )
    }


def _no_publish_release_shaped_source_is_admissible(  # noqa: PLR0911
    *,
    receipt: Mapping[str, object],
    detail: Mapping[str, object],
    source_validation_result: Mapping[str, object] | None,
) -> bool:
    source_proof = detail.get("source-proof")
    if not isinstance(source_proof, Mapping):
        return False
    if (
        source_proof.get("kind") != "no-publish-validation-result"
        or source_proof.get("work-group-id") != receipt.get("work-group-id")
        or source_proof.get("coverage-target") != receipt.get("coverage-target")
        or source_proof.get("observed-commit-sha")
        != _receipt_observed_commit_sha(receipt)
    ):
        return False
    if source_validation_result is None or not (
        source_validation_result.get("outcome") == "success"
        and source_validation_result.get("work-group-id")
        == receipt.get("work-group-id")
        and source_validation_result.get("kind") == "release-shaped-artifact"
        and source_validation_result.get("coverage-target")
        == receipt.get("coverage-target")
        and source_validation_result.get("observed-commit-sha")
        == _receipt_observed_commit_sha(receipt)
    ):
        return False
    source_command = _no_publish_source_command_from_validation_result(
        source_validation_result
    )
    if source_command is None:
        return False
    if source_command.get("source-proof") != source_proof or source_command.get(
        "artifact-obligation-results"
    ) != detail.get("artifact-obligation-results"):
        return False
    proof_digests = source_proof.get("artifact-digests")
    if not isinstance(proof_digests, Sequence) or isinstance(
        proof_digests, str | bytes
    ):
        return False
    if not all(isinstance(item, Mapping) for item in proof_digests):
        return False
    return _release_shaped_digest_proof_entries_from_results(
        cast(
            "Sequence[Mapping[str, object]]",
            source_command["artifact-obligation-results"],
        )
    ) == [
        dict(item)
        for item in cast("Sequence[Mapping[str, object]]", proof_digests)
    ]


def _no_publish_source_command_from_validation_result(
    validation_result: Mapping[str, object],
) -> Mapping[str, object] | None:
    commands = validation_result.get("commands")
    if not isinstance(commands, Sequence) or isinstance(commands, str | bytes):
        return None
    if len(commands) != 1:
        return None
    command = commands[0]
    if not (
        isinstance(command, Mapping)
        and command.get("outcome") == "success"
        and command.get("evidence-source") == "no-publish-validation"
        and isinstance(command.get("source-proof"), Mapping)
        and isinstance(command.get("artifact-obligation-results"), Sequence)
        and not isinstance(
            command.get("artifact-obligation-results"), str | bytes
        )
    ):
        return None
    return command


def _release_shaped_digest_proof_entries_from_results(
    results: Sequence[Mapping[str, object]],
) -> list[dict[str, object]]:
    entries: list[dict[str, object]] = []
    for result in results:
        artifact = result.get("artifact")
        if not isinstance(artifact, Mapping):
            continue
        observed = artifact.get("observed")
        if not isinstance(observed, Mapping):
            continue
        digests = observed.get("digests")
        if not isinstance(digests, Sequence) or isinstance(
            digests, str | bytes
        ):
            continue
        for digest in digests:
            if not isinstance(digest, Mapping):
                continue
            entries.append(
                {
                    "artifact-ref": digest.get("artifact-ref"),
                    "algorithm": digest.get("algorithm"),
                    "digest": digest.get("digest"),
                }
            )
    return sorted(entries, key=lambda item: str(item["artifact-ref"]))


def _observed_reused_receipt_input(
    reused_receipt: Mapping[str, object],
    observed_inputs: Sequence[CiValidationObservedReceiptInput],
    *,
    observed_commit_sha: str | None,
    work_group_id: str,
    expected_writer_id: object,
) -> CiValidationObservedReceiptInput | None:
    if (
        observed_commit_sha is None
        or reused_receipt.get("observed-commit-sha") != observed_commit_sha
    ):
        return None
    artifact_ref = reused_receipt.get("artifact-ref")
    receipt_id = reused_receipt.get("receipt-id")
    content_digest = reused_receipt.get("receipt-content-digest")
    if not all(
        isinstance(item, str) and item
        for item in (artifact_ref, receipt_id, content_digest)
    ):
        return None
    for observed in observed_inputs:
        if (
            observed.manifest_entry.get("artifact-ref") == artifact_ref
            and observed.manifest_entry.get("receipt-id") == receipt_id
            and observed.manifest_entry.get("receipt-content-digest")
            == content_digest
            and _observed_receipt_manifest_matches_trusted_writer(
                observed.manifest_entry,
                work_group_id=work_group_id,
                expected_writer_id=expected_writer_id,
            )
        ):
            return observed
    return None


def _observed_receipt_manifest_matches_trusted_writer(
    manifest_entry: Mapping[str, object],
    *,
    work_group_id: str,
    expected_writer_id: object,
) -> bool:
    return (
        isinstance(expected_writer_id, str)
        and bool(expected_writer_id)
        and manifest_entry.get("writer-work-group-id") == work_group_id
        and manifest_entry.get("trusted-writer-id") == expected_writer_id
        and manifest_entry.get("observed-writer-id") == expected_writer_id
    )


def _receipt_observed_commit_sha(receipt: Mapping[str, object]) -> str | None:
    execution_tree = receipt.get("execution-tree")
    if not isinstance(execution_tree, Mapping):
        return None
    value = execution_tree.get("observed-commit-sha")
    return value if isinstance(value, str) else None


def _release_shaped_source_failure_diagnostic(
    *, expectation_id: str, work_group_id: str
) -> dict[str, object]:
    return _diagnostic(
        diagnostic_id=(
            f"blocking-validation-failure/{expectation_id}/"
            "release-shaped-source"
        ),
        code=DiagnosticFamily.VALIDATION_WORK_FAILED.value,
        detail=DiagnosticDetail.TOOLING.value,
        message=(
            "Release-shaped success requires independently observed "
            "no-publish source proof or an admissible reused receipt chain"
        ),
        source_type="work-group",
        source_id=work_group_id,
    )


def _blocking_validation_failure_diagnostic(
    *, receipt: Mapping[str, object], expectation_id: str, work_group_id: str
) -> dict[str, object]:
    diagnostics = _receipt_blocking_failure_diagnostics(receipt)
    if diagnostics:
        return diagnostics[0]
    return _diagnostic(
        diagnostic_id=f"blocking-validation-failure/{expectation_id}",
        code=DiagnosticFamily.VALIDATION_WORK_FAILED.value,
        detail=DiagnosticDetail.TOOLING.value,
        message="Required validation receipt failed",
        source_type="work-group",
        source_id=work_group_id,
    )


def _receipt_blocking_failure_diagnostics(
    value: object,
) -> list[dict[str, object]]:
    by_priority: dict[str, list[dict[str, object]]] = {
        family: [] for family in _BLOCKING_VALIDATION_FAILURE_FAMILIES
    }
    for diagnostic in _walk_diagnostics(value):
        code = diagnostic.get("code")
        if (
            code in by_priority
            and diagnostic.get("verdict-effect")
            == DiagnosticVerdictEffect.FAILED.value
        ):
            by_priority[cast("str", code)].append(diagnostic)
    return [
        diagnostic
        for family in _BLOCKING_VALIDATION_FAILURE_FAMILIES
        for diagnostic in by_priority[family]
    ]


def _walk_diagnostics(value: object) -> list[dict[str, object]]:
    found: list[dict[str, object]] = []
    if isinstance(value, Mapping):
        if _looks_like_diagnostic(value):
            found.append(dict(value))
        for child in value.values():
            found.extend(_walk_diagnostics(child))
    elif isinstance(value, Sequence) and not isinstance(value, str | bytes):
        for child in value:
            found.extend(_walk_diagnostics(child))
    return found


def _looks_like_diagnostic(value: Mapping[object, object]) -> bool:
    return (
        "diagnostic-id" in value
        and "code" in value
        and "verdict-effect" in value
    )


def _evidence_result(
    *,
    expectation_id: str,
    work_group_id: str,
    outcome: EvidenceResultOutcome,
    summary: Mapping[str, object] | None = None,
    diagnostic: Mapping[str, object] | None = None,
) -> dict[str, object]:
    diagnostics = [] if diagnostic is None else [dict(diagnostic)]
    return {
        "evidence-expectation-id": expectation_id,
        "work-group-id": work_group_id,
        "receipt-id": _nullable_str(summary.get("receipt-id"))
        if summary
        else None,
        "observed-entry-id": _nullable_str(summary.get("observed-entry-id"))
        if summary
        else None,
        "receipt-artifact-ref": _nullable_str(summary.get("artifact-ref"))
        if summary
        else None,
        "receipt-content-digest": _nullable_str(
            summary.get("receipt-content-digest")
        )
        if summary
        else None,
        "outcome": outcome,
        "diagnostics": diagnostics,
    }


def _fail_closed_failures(
    plan: Mapping[str, object],
) -> list[dict[str, object]]:
    if plan.get("verdict-intent") != "fail-closed":
        return []
    failures = []
    for diagnostic in _diagnostics(plan.get("diagnostics")):
        if (
            diagnostic.get("verdict-effect")
            != DiagnosticVerdictEffect.FAIL_CLOSED.value
        ):
            continue
        failures.append(
            _failure(
                kind="fail-closed",
                diagnostic=diagnostic,
                message="Planner emitted a fail-closed diagnostic",
            )
        )
    if not failures:
        diagnostic = _diagnostic(
            diagnostic_id="invalid-plan/fail-closed-without-diagnostic",
            code=DiagnosticFamily.INVALID_PLAN.value,
            detail=DiagnosticDetail.STRUCTURALLY_INVALID.value,
            message=(
                "Fail-closed plan did not preserve a fail-closed diagnostic"
            ),
        )
        failures.append(
            _failure(
                kind="invalid-plan",
                diagnostic=diagnostic,
                message=(
                    "Fail-closed plan did not preserve a fail-closed diagnostic"
                ),
            )
        )
    return failures


def _final_evidence_failures(
    diagnostics: Sequence[Mapping[str, object]],
) -> list[dict[str, object]]:
    failures: list[dict[str, object]] = []
    issues: list[ValidationIssue] = []
    for diagnostic in diagnostics:
        if not isinstance(diagnostic, Mapping):
            continue
        _validate_final_evidence_diagnostic(
            diagnostic,
            f"final-evidence-diagnostics[{len(failures)}]",
            issues,
        )
        failures.append(
            _failure(
                kind="final-evidence-failure",
                diagnostic=diagnostic,
                message="Final evidence reconciliation failed",
            )
        )
    if issues:
        raise ContractValidationError(issues)
    return failures


def _aggregate_diagnostics(
    summaries: Sequence[Mapping[str, object]],
    results: Sequence[Mapping[str, object]],
    failures: Sequence[Mapping[str, object]],
) -> list[dict[str, object]]:
    diagnostics: list[dict[str, object]] = []
    for collection in (summaries, results):
        for item in collection:
            diagnostics.extend(_diagnostics(item.get("diagnostics")))
    for failure in failures:
        diagnostic = failure.get("diagnostic")
        if isinstance(diagnostic, Mapping):
            diagnostics.append(dict(diagnostic))
    return list(
        {str(item["diagnostic-id"]): item for item in diagnostics}.values()
    )


def _work_group_counts(
    results: Sequence[Mapping[str, object]],
) -> dict[str, object]:
    return {
        "executable-required": len(results),
        "required-succeeded": sum(
            item.get("outcome") == "satisfied" for item in results
        ),
        "required-failed": sum(
            item.get("outcome") == "failed" for item in results
        ),
        "required-skipped": sum(
            item.get("outcome") == "skipped" for item in results
        ),
        "required-missing": sum(
            item.get("outcome") == "missing" for item in results
        ),
        "terminal-aggregation": "present",
    }


def _zero_work_group_counts() -> dict[str, object]:
    return {
        "executable-required": 0,
        "required-succeeded": 0,
        "required-failed": 0,
        "required-skipped": 0,
        "required-missing": 0,
        "terminal-aggregation": "present",
    }


def _aggregate_validation_tree(plan: Mapping[str, object]) -> dict[str, object]:
    tree = cast("Mapping[str, object]", plan["validation-tree"])
    return {"commit-sha": tree.get("commit-sha"), "ref": tree.get("ref")}


def _aggregate_affected_range(plan: Mapping[str, object]) -> dict[str, object]:
    affected = cast("Mapping[str, object]", plan["affected-range"])
    return {
        "status": affected.get("status"),
        "base-sha": affected.get("base-sha"),
        "base-tip-sha": affected.get("base-tip-sha"),
        "head-sha": affected.get("head-sha"),
        "changed-files-hash": affected.get("changed-files-hash"),
    }


def _aggregate_request(plan: Mapping[str, object]) -> dict[str, object]:
    request = cast("Mapping[str, object]", plan["request"])
    return {
        "artifact-ref": request.get("artifact-ref"),
        "request-digest": request.get("request-digest"),
    }


def _aggregate_scheduled_full(plan: Mapping[str, object]) -> dict[str, object]:
    scheduled = cast("Mapping[str, object]", plan["scheduled-full"])
    return {"enabled": scheduled.get("enabled")}


def _verified_plan_fields_for_invalid_mode(
    plan: Mapping[str, object] | None,
    *,
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    pull_request_merge_commit_verification: Mapping[str, object] | None,
) -> dict[str, object]:
    if plan is None:
        return _unknown_plan_fields()
    issues: list[ValidationIssue] = []
    _validated_plan_envelope(
        plan,
        issues,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        pull_request_merge_commit_verification=(
            pull_request_merge_commit_verification
        ),
    )
    if issues:
        return _unknown_plan_fields()
    return {
        "plan-id": plan.get("plan-id"),
        "plan-digest": plan.get("plan-digest"),
        "mode": plan.get("mode", "unknown"),
        "validation-tree": _aggregate_validation_tree(plan),
        "affected-range": _aggregate_affected_range(plan),
        "request": _aggregate_request(plan),
        "scheduled-full": _aggregate_scheduled_full(plan),
    }


def _unknown_plan_fields() -> dict[str, object]:
    return {
        "plan-id": None,
        "plan-digest": None,
        "mode": "unknown",
        "validation-tree": {"commit-sha": None, "ref": None},
        "affected-range": {
            "status": "unknown",
            "base-sha": None,
            "base-tip-sha": None,
            "head-sha": None,
            "changed-files-hash": None,
        },
        "request": {"artifact-ref": None, "request-digest": None},
        "scheduled-full": {"enabled": None},
    }


def _inspection_observed_receipt(
    entry: Mapping[str, object],
) -> dict[str, object]:
    diagnostic = _inadmissible_diagnostic(
        str(entry.get("observed-entry-id", "unknown")),
        DiagnosticDetail.MALFORMED_RECEIPT.value,
        "Receipt was observed while plan authority was invalid",
    )
    return {
        "observed-entry-id": entry.get("observed-entry-id"),
        "artifact-ref": entry.get("artifact-ref"),
        "physical-artifact-name": entry.get("physical-artifact-name"),
        "artifact-instance-id": entry.get("artifact-instance-id"),
        "receipt-id": entry.get("receipt-id"),
        "work-group-id": _work_group_id_from_ref(entry.get("artifact-ref")),
        "receipt-content-digest": entry.get("receipt-content-digest"),
        "admissibility": "inadmissible",
        "diagnostics": [diagnostic],
    }


def _validate_manifest_binding(
    aggregate: Mapping[str, object],
    receipt_manifest: Mapping[str, object],
    selector_assignments_manifest: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> None:
    envelope = _envelope_or_collect(
        aggregate, CiValidationKind.AGGREGATE, issues
    )
    if envelope is None:
        return
    expected_ref = ci_validation_receipt_manifest_artifact_ref(
        run_id=envelope.run_id,
        run_attempt=envelope.run_attempt,
    )
    binding = aggregate.get("receipt-manifest")
    if not isinstance(binding, Mapping):
        issues.append(
            ValidationIssue("$.receipt-manifest", "must be an object")
        )
        return
    if binding.get("artifact-ref") != expected_ref:
        issues.append(
            ValidationIssue(
                "$.receipt-manifest.artifact-ref", "must be contract-owned"
            )
        )
    if binding.get(
        "content-digest"
    ) != ci_validation_receipt_manifest_payload_digest(receipt_manifest):
        issues.append(
            ValidationIssue(
                "$.receipt-manifest.content-digest", "must match manifest"
            )
        )
    _validate_observed_receipts_match_manifest(
        aggregate.get("observed-receipts"),
        receipt_manifest.get("entries"),
        issues,
    )
    _validate_valid_observed_receipt_manifest_writer_bindings(
        aggregate.get("observed-receipts"),
        receipt_manifest.get("entries"),
        selector_assignments_manifest,
        envelope,
        issues,
    )


def _validate_supplied_selector_assignments_manifest(  # noqa: PLR0913
    selector_assignments_manifest: Mapping[str, object],
    *,
    plan: Mapping[str, object] | None,
    envelope: CommonEnvelope | None,
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    pull_request_merge_commit_verification: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> None:
    if plan is None:
        issues.append(
            ValidationIssue(
                "selector-assignments.$",
                "requires a validated plan",
            )
        )
        return
    try:
        validate_ci_validation_selector_assignments(
            selector_assignments_manifest,
            plan=plan,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            pull_request_merge_commit_verification=(
                pull_request_merge_commit_verification
            ),
            expected_run_id=envelope.run_id if envelope is not None else None,
            expected_run_attempt=(
                envelope.run_attempt if envelope is not None else None
            ),
        )
    except ContractValidationError as error:
        issues.extend(
            ValidationIssue(f"selector-assignments.{issue.path}", issue.message)
            for issue in error.issues
        )


def _validate_supplied_receipt_manifest(  # noqa: PLR0913
    receipt_manifest: Mapping[str, object],
    *,
    plan: Mapping[str, object] | None,
    envelope: CommonEnvelope | None,
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    pull_request_merge_commit_verification: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> None:
    try:
        validate_ci_validation_receipt_manifest(
            receipt_manifest,
            plan=plan,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            pull_request_merge_commit_verification=(
                pull_request_merge_commit_verification
            ),
            expected_run_id=envelope.run_id if envelope is not None else None,
            expected_run_attempt=(
                envelope.run_attempt if envelope is not None else None
            ),
        )
    except ContractValidationError as error:
        issues.extend(
            ValidationIssue(f"receipt-manifest.{issue.path}", issue.message)
            for issue in error.issues
        )


def _validate_observed_receipts_match_manifest(
    observed: object,
    manifest_entries: object,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(observed, Sequence) or isinstance(observed, str | bytes):
        return
    if not isinstance(manifest_entries, Sequence) or isinstance(
        manifest_entries, str | bytes
    ):
        return
    observed_projection = [
        _aggregate_receipt_manifest_projection(item)
        for item in observed
        if isinstance(item, Mapping)
    ]
    manifest_projection = [
        _manifest_entry_aggregate_projection(item)
        for item in manifest_entries
        if isinstance(item, Mapping)
    ]
    if observed_projection != manifest_projection:
        issues.append(
            ValidationIssue(
                "$.observed-receipts",
                "must exactly mirror receipt manifest entries",
            )
        )


def _aggregate_receipt_manifest_projection(
    observed: Mapping[str, object],
) -> dict[str, object]:
    return {
        "observed-entry-id": observed.get("observed-entry-id"),
        "artifact-ref": observed.get("artifact-ref"),
        "physical-artifact-name": observed.get("physical-artifact-name"),
        "artifact-instance-id": observed.get("artifact-instance-id"),
        "receipt-id": observed.get("receipt-id"),
        "work-group-id": observed.get("work-group-id"),
        "receipt-content-digest": observed.get("receipt-content-digest"),
    }


def _manifest_entry_aggregate_projection(
    entry: Mapping[str, object],
) -> dict[str, object]:
    return {
        "observed-entry-id": entry.get("observed-entry-id"),
        "artifact-ref": entry.get("artifact-ref"),
        "physical-artifact-name": entry.get("physical-artifact-name"),
        "artifact-instance-id": entry.get("artifact-instance-id"),
        "receipt-id": entry.get("receipt-id"),
        "work-group-id": _work_group_id_from_ref(entry.get("artifact-ref")),
        "receipt-content-digest": entry.get("receipt-content-digest"),
    }


def _validate_valid_observed_receipt_manifest_writer_bindings(
    observed: object,
    manifest_entries: object,
    selector_assignments_manifest: Mapping[str, object] | None,
    envelope: CommonEnvelope,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(observed, Sequence) or isinstance(observed, str | bytes):
        return
    if not isinstance(manifest_entries, Sequence) or isinstance(
        manifest_entries, str | bytes
    ):
        return
    entries_by_id: dict[str, tuple[int, Mapping[str, object]]] = {}
    for index, entry in enumerate(manifest_entries):
        if isinstance(entry, Mapping) and isinstance(
            entry.get("observed-entry-id"), str
        ):
            entries_by_id[cast("str", entry["observed-entry-id"])] = (
                index,
                entry,
            )
    assignments = (
        _assignments_by_work_group(selector_assignments_manifest)
        if selector_assignments_manifest is not None
        else None
    )
    for observed_index, receipt in enumerate(observed):
        if (
            not isinstance(receipt, Mapping)
            or receipt.get("admissibility") != "valid"
        ):
            continue
        entry_id = receipt.get("observed-entry-id")
        if not isinstance(entry_id, str) or entry_id not in entries_by_id:
            continue
        entry_index, entry = entries_by_id[entry_id]
        entry_path = f"receipt-manifest.$.entries[{entry_index}]"
        _validate_valid_manifest_entry_writer_binding(
            entry,
            receipt,
            assignments,
            envelope,
            entry_path,
            f"$.observed-receipts[{observed_index}]",
            issues,
        )


def _validate_valid_manifest_entry_writer_binding(  # noqa: C901,PLR0912,PLR0913
    entry: Mapping[str, object],
    receipt: Mapping[str, object],
    assignments: Mapping[str, Mapping[str, object]] | None,
    envelope: CommonEnvelope,
    entry_path: str,
    receipt_path: str,
    issues: list[ValidationIssue],
) -> None:
    work_group_id = receipt.get("work-group-id")
    if not isinstance(work_group_id, str):
        return
    assignment = None
    if assignments is None:
        issues.append(
            ValidationIssue(
                entry_path,
                _VALID_RECEIPT_SELECTOR_CONTEXT_MSG,
            )
        )
    else:
        assignment = assignments.get(work_group_id)
        if assignment is None:
            issues.append(
                ValidationIssue(
                    entry_path,
                    _VALID_RECEIPT_SELECTOR_ASSIGNMENT_MSG,
                )
            )
    for key in (
        "assignment-id",
        "writer-work-group-id",
        "trusted-writer-id",
        "observed-writer-id",
        "writer-observation-ref",
    ):
        if not isinstance(entry.get(key), str):
            issues.append(
                ValidationIssue(
                    f"{entry_path}.{key}",
                    "must be present for valid observed receipts",
                )
            )
    if assignment is not None:
        for entry_key, assignment_key in (
            ("assignment-id", "assignment-id"),
            ("writer-work-group-id", "work-group-id"),
            ("artifact-ref", "receipt-artifact-ref"),
            ("trusted-writer-id", "trusted-writer-id"),
            ("writer-observation-ref", "writer-observation-ref"),
        ):
            if entry.get(entry_key) != assignment.get(assignment_key):
                issues.append(
                    ValidationIssue(
                        f"{entry_path}.{entry_key}",
                        _VALID_RECEIPT_SELECTOR_ASSIGNMENT_MSG,
                    )
                )
        if entry.get("observed-writer-id") != assignment.get(
            "trusted-writer-id"
        ):
            issues.append(
                ValidationIssue(
                    f"{entry_path}.observed-writer-id",
                    _VALID_RECEIPT_SELECTOR_ASSIGNMENT_MSG,
                )
            )
    if entry.get("assignment-id") != work_group_id:
        issues.append(
            ValidationIssue(
                f"{entry_path}.assignment-id",
                "must match valid observed receipt work group",
            )
        )
    if entry.get("writer-work-group-id") != work_group_id:
        issues.append(
            ValidationIssue(
                f"{entry_path}.writer-work-group-id",
                "must match valid observed receipt work group",
            )
        )
    if entry.get("observed-writer-id") != entry.get("trusted-writer-id"):
        issues.append(
            ValidationIssue(
                f"{entry_path}.observed-writer-id",
                "must match trusted writer for valid observed receipts",
            )
        )
    assignment_id = entry.get("assignment-id")
    if isinstance(assignment_id, str):
        try:
            expected_observation_ref = (
                ci_validation_writer_observation_artifact_ref(
                    run_id=envelope.run_id,
                    run_attempt=envelope.run_attempt,
                    assignment_id=assignment_id,
                )
            )
        except ContractValidationError:
            expected_observation_ref = None
        if (
            expected_observation_ref is not None
            and entry.get("writer-observation-ref") != expected_observation_ref
        ):
            issues.append(
                ValidationIssue(
                    f"{entry_path}.writer-observation-ref",
                    "must match valid observed receipt assignment",
                )
            )
    if entry.get("artifact-ref") != receipt.get("artifact-ref"):
        issues.append(
            ValidationIssue(
                f"{receipt_path}.artifact-ref",
                "must mirror manifest entry writer binding",
            )
        )


def _validate_aggregate_plan_bindings(
    aggregate: Mapping[str, object],
    plan: Mapping[str, object],
    issues: list[ValidationIssue],
) -> None:
    if aggregate.get("plan-id") != plan.get("plan-id"):
        issues.append(ValidationIssue("$.plan-id", "must match plan"))
    if aggregate.get("plan-digest") != _verified_plan_digest(plan):
        issues.append(ValidationIssue("$.plan-digest", "must match plan"))
    for key, expected in {
        "mode": plan.get("mode"),
        "validation-tree": _aggregate_validation_tree(plan),
        "affected-range": _aggregate_affected_range(plan),
        "request": _aggregate_request(plan),
        "scheduled-full": _aggregate_scheduled_full(plan),
    }.items():
        if aggregate.get(key) != expected:
            issues.append(ValidationIssue(f"$.{key}", "must match plan"))


def _validate_evidence_results_match_plan(
    aggregate: Mapping[str, object],
    plan: Mapping[str, object],
    issues: list[ValidationIssue],
) -> None:
    results = aggregate.get("evidence-results")
    if not isinstance(results, Sequence) or isinstance(results, str | bytes):
        return
    result_items = [item for item in results if isinstance(item, Mapping)]
    if len(result_items) != len(_evidence_expectations(plan)):
        issues.append(
            ValidationIssue(
                "$.evidence-results",
                "must have one result per plan evidence expectation",
            )
        )
    expected = {
        expectation.get("evidence-expectation-id"): expectation.get(
            "work-group-id"
        )
        for expectation in _evidence_expectations(plan)
    }
    actual = {
        result.get("evidence-expectation-id"): result.get("work-group-id")
        for result in result_items
    }
    if actual != expected:
        issues.append(
            ValidationIssue(
                "$.evidence-results",
                "must exactly match plan evidence expectations",
            )
        )


def _is_invalid_plan_aggregate(aggregate: Mapping[str, object]) -> bool:
    reason = aggregate.get("reason")
    return isinstance(reason, Mapping) and reason.get("invalid-plan") is True


def _validate_invalid_plan_unverified_fields(
    aggregate: Mapping[str, object],
    issues: list[ValidationIssue],
) -> None:
    reason = aggregate.get("reason")
    if (
        not isinstance(reason, Mapping)
        or reason.get("invalid-plan") is not True
    ):
        return
    expected = _unknown_plan_fields()
    for key in (
        "plan-id",
        "plan-digest",
        "mode",
        "validation-tree",
        "affected-range",
        "request",
        "scheduled-full",
    ):
        if aggregate.get(key) != expected[key]:
            issues.append(
                ValidationIssue(
                    f"$.{key}",
                    "must be null or unknown without a verified plan",
                )
            )


def _validate_receipt_manifest_ref_binding(
    aggregate: Mapping[str, object],
    envelope: CommonEnvelope | None,
    issues: list[ValidationIssue],
) -> None:
    binding = aggregate.get("receipt-manifest")
    if not isinstance(binding, Mapping):
        return
    artifact_ref = binding.get("artifact-ref")
    content_digest = binding.get("content-digest")
    if (artifact_ref is None) != (content_digest is None):
        issues.append(
            ValidationIssue(
                "$.receipt-manifest",
                "artifact-ref and content-digest must both be null or non-null",
            )
        )
        return
    if artifact_ref is not None:
        if envelope is not None:
            expected_ref = ci_validation_receipt_manifest_artifact_ref(
                run_id=envelope.run_id,
                run_attempt=envelope.run_attempt,
            )
            if artifact_ref != expected_ref:
                issues.append(
                    ValidationIssue(
                        "$.receipt-manifest.artifact-ref",
                        "must be contract-owned",
                    )
                )
        return
    reason = aggregate.get("reason")
    if not isinstance(reason, Mapping):
        return
    if aggregate.get("verdict") != "failed" or not (
        reason.get("invalid-plan") is True
        or reason.get("final-evidence-failure") is True
    ):
        issues.append(
            ValidationIssue(
                "$.receipt-manifest.artifact-ref",
                "must be non-null for authoritative aggregate evidence",
            )
        )


def _validate_aggregate_shapes(  # noqa: C901
    aggregate: Mapping[str, object],
    envelope: CommonEnvelope | None,
    issues: list[ValidationIssue],
) -> None:
    _validate_nullable_digest(
        aggregate.get("plan-digest"), "$.plan-digest", issues
    )
    if aggregate.get("mode") not in _MODES:
        issues.append(ValidationIssue("$.mode", "is not registered"))
    if aggregate.get("verdict") not in {"passed", "failed"}:
        issues.append(ValidationIssue("$.verdict", "is not registered"))
    if aggregate.get("proof-admissibility") != "validation-only":
        issues.append(
            ValidationIssue("$.proof-admissibility", "must be validation-only")
        )
    _validate_object(
        aggregate.get("receipt-manifest"),
        _AGGREGATE_MANIFEST_KEYS,
        "$.receipt-manifest",
        issues,
    )
    manifest = aggregate.get("receipt-manifest")
    if isinstance(manifest, Mapping):
        _validate_nullable_artifact_ref(
            manifest.get("artifact-ref"),
            "$.receipt-manifest.artifact-ref",
            issues,
        )
        _validate_nullable_digest(
            manifest.get("content-digest"),
            "$.receipt-manifest.content-digest",
            issues,
        )
    _validate_object(
        aggregate.get("validation-tree"),
        _VALIDATION_TREE_KEYS,
        "$.validation-tree",
        issues,
    )
    _validate_object(
        aggregate.get("affected-range"),
        _AFFECTED_RANGE_KEYS,
        "$.affected-range",
        issues,
    )
    affected = aggregate.get("affected-range")
    if (
        isinstance(affected, Mapping)
        and affected.get("status") not in _AFFECTED_STATUSES
    ):
        issues.append(
            ValidationIssue("$.affected-range.status", "is not registered")
        )
    _validate_object(
        aggregate.get("request"), _REQUEST_KEYS, "$.request", issues
    )
    request = aggregate.get("request")
    if isinstance(request, Mapping):
        _validate_nullable_artifact_ref(
            request.get("artifact-ref"), "$.request.artifact-ref", issues
        )
        _validate_nullable_digest(
            request.get("request-digest"), "$.request.request-digest", issues
        )
    _validate_object(
        aggregate.get("scheduled-full"),
        _SCHEDULED_FULL_KEYS,
        "$.scheduled-full",
        issues,
    )
    scheduled = aggregate.get("scheduled-full")
    if (
        isinstance(scheduled, Mapping)
        and scheduled.get("enabled") is not None
        and not isinstance(scheduled.get("enabled"), bool)
    ):
        issues.append(
            ValidationIssue(
                "$.scheduled-full.enabled", "must be boolean or null"
            )
        )
    reason = aggregate.get("reason")
    _validate_object(reason, _REASON_KEYS, "$.reason", issues)
    if isinstance(reason, Mapping):
        for key in _REASON_KEYS:
            if not isinstance(reason.get(key), bool):
                issues.append(
                    ValidationIssue(f"$.reason.{key}", "must be boolean")
                )
    _validate_diagnostic_array(
        aggregate.get("diagnostics"), "$.diagnostics", issues
    )
    _validate_observed_receipts(
        aggregate.get("observed-receipts"), envelope, issues
    )
    _validate_evidence_results(aggregate.get("evidence-results"), issues)
    _validate_failures(aggregate.get("failures"), issues)
    _validate_work_group_counts(aggregate.get("work-groups"), issues)


def _validate_aggregate_consistency(  # noqa: C901,PLR0912,PLR0913
    aggregate: Mapping[str, object],
    plan: Mapping[str, object] | None,
    observed_inputs: Sequence[CiValidationObservedReceiptInput] | None,
    selector_assignments_manifest: Mapping[str, object] | None,
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    pull_request_merge_commit_verification: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> None:
    reason = aggregate.get("reason")
    failures = aggregate.get("failures")
    results = aggregate.get("evidence-results")
    observed = aggregate.get("observed-receipts")
    if not isinstance(reason, Mapping):
        return
    has_failure_reason = any(reason.get(key) is True for key in _REASON_KEYS)
    if aggregate.get("verdict") == "passed" and has_failure_reason:
        issues.append(
            ValidationIssue(
                "$.verdict", "must be failed when any reason is true"
            )
        )
    if aggregate.get("verdict") == "failed" and not has_failure_reason:
        issues.append(
            ValidationIssue("$.reason", "must explain failed verdict")
        )
    if isinstance(failures, Sequence) and not isinstance(failures, str | bytes):
        kinds = [
            item.get("kind") for item in failures if isinstance(item, Mapping)
        ]
        for reason_key in _REASON_KEYS - {"fail-closed"}:
            if reason_key == "invalid-plan":
                expected = "invalid-plan"
            else:
                expected = reason_key
            if reason.get(reason_key) != (expected in kinds):
                issues.append(
                    ValidationIssue(
                        f"$.reason.{reason_key}", "must match failures"
                    )
                )
        if reason.get("fail-closed") != ("fail-closed" in kinds):
            issues.append(
                ValidationIssue("$.reason.fail-closed", "must match failures")
            )
    if reason.get("invalid-plan") is True:
        _validate_invalid_plan_aggregate_consistency(aggregate, reason, issues)
    else:
        _validate_evidence_result_consistency(
            aggregate.get("evidence-results"),
            aggregate.get("observed-receipts"),
            aggregate.get("failures"),
            aggregate.get("work-groups"),
            reason,
            aggregate.get("verdict"),
            issues,
        )
        _validate_inadmissible_observed_receipts(
            aggregate.get("observed-receipts"),
            aggregate.get("failures"),
            reason,
            aggregate.get("verdict"),
            issues,
        )
        _validate_valid_observed_receipts(
            aggregate.get("observed-receipts"),
            results,
            plan,
            observed_inputs,
            selector_assignments_manifest,
            changed_files_snapshot,
            fact_snapshot,
            pull_request_merge_commit_verification,
            issues,
        )
        _validate_failures_are_justified(
            failures, results, observed, plan, issues
        )
    if isinstance(observed, Sequence) and not isinstance(observed, str | bytes):
        previous: str | None = None
        for item in observed:
            if not isinstance(item, Mapping):
                continue
            entry_id = item.get("observed-entry-id")
            if isinstance(entry_id, str):
                if previous is not None and previous > entry_id:
                    issues.append(
                        ValidationIssue("$.observed-receipts", "must be sorted")
                    )
                previous = entry_id
    _validate_sorted_diagnostics(
        aggregate.get("diagnostics"), "$.diagnostics", issues
    )
    _validate_root_diagnostics_cover_references(
        aggregate.get("diagnostics"),
        results,
        observed,
        failures,
        issues,
    )
    _validate_sorted_evidence_results(results, issues)
    _validate_sorted_failures(failures, issues)


def _validate_invalid_plan_aggregate_consistency(
    aggregate: Mapping[str, object],
    reason: Mapping[object, object],
    issues: list[ValidationIssue],
) -> None:
    for key in _REASON_KEYS - {"invalid-plan"}:
        if reason.get(key) is True:
            issues.append(
                ValidationIssue(
                    f"$.reason.{key}", "must be false for invalid-plan"
                )
            )
    _validate_invalid_plan_failures(aggregate.get("failures"), issues)
    results = aggregate.get("evidence-results")
    if isinstance(results, Sequence) and len(results) != 0:
        issues.append(
            ValidationIssue(
                "$.evidence-results", "must be empty for invalid-plan"
            )
        )
    counts = aggregate.get("work-groups")
    if isinstance(counts, Mapping):
        _validate_invalid_plan_counts(counts, issues)
    _validate_invalid_plan_observed(aggregate.get("observed-receipts"), issues)


def _validate_invalid_plan_failures(
    failures: object, issues: list[ValidationIssue]
) -> None:
    if not isinstance(failures, Sequence) or isinstance(failures, str | bytes):
        return
    if len(failures) != 1:
        issues.append(
            ValidationIssue(
                "$.failures",
                "must contain exactly one invalid-plan failure",
            )
        )
    for failure in failures:
        if (
            isinstance(failure, Mapping)
            and failure.get("kind") != "invalid-plan"
        ):
            issues.append(
                ValidationIssue(
                    "$.failures",
                    "must contain only invalid-plan failures",
                )
            )
            break
        if isinstance(failure, Mapping) and isinstance(
            failure.get("diagnostic"), Mapping
        ):
            _validate_invalid_plan_diagnostic(
                cast("Mapping[str, object]", failure["diagnostic"]),
                "$.failures.diagnostic",
                issues,
            )


def _validate_invalid_plan_observed(
    observed: object, issues: list[ValidationIssue]
) -> None:
    if not isinstance(observed, Sequence) or isinstance(observed, str | bytes):
        return
    for item in observed:
        if (
            isinstance(item, Mapping)
            and item.get("admissibility") != "inadmissible"
        ):
            issues.append(
                ValidationIssue(
                    "$.observed-receipts",
                    "must be inadmissible for invalid-plan",
                )
            )
            break


def _validate_invalid_plan_counts(
    counts: Mapping[object, object], issues: list[ValidationIssue]
) -> None:
    for key in _WORK_GROUP_COUNTS_KEYS - {"terminal-aggregation"}:
        if counts.get(key) != 0:
            issues.append(
                ValidationIssue(
                    f"$.work-groups.{key}",
                    "must be zero for invalid-plan",
                )
            )
    if counts.get("terminal-aggregation") != "present":
        issues.append(
            ValidationIssue(
                "$.work-groups.terminal-aggregation",
                "must be present for invalid-plan",
            )
        )


def _validate_failures_are_justified(
    failures: object,
    results: object,
    observed: object,
    plan: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(failures, Sequence) or isinstance(failures, str | bytes):
        return
    failure_items = [item for item in failures if isinstance(item, Mapping)]
    result_items = (
        [item for item in results if isinstance(item, Mapping)]
        if isinstance(results, Sequence)
        and not isinstance(results, str | bytes)
        else []
    )
    observed_items = (
        [item for item in observed if isinstance(item, Mapping)]
        if isinstance(observed, Sequence)
        and not isinstance(observed, str | bytes)
        else []
    )
    fail_closed_failures = (
        _fail_closed_failures(plan) if plan is not None else []
    )
    for index, failure in enumerate(failure_items):
        if not _failure_is_justified(
            failure, result_items, observed_items, fail_closed_failures
        ):
            issues.append(
                ValidationIssue(
                    f"$.failures[{index}]",
                    "must be justified by aggregate evidence",
                )
            )


def _failure_is_justified(
    failure: Mapping[str, object],
    results: Sequence[Mapping[str, object]],
    observed: Sequence[Mapping[str, object]],
    fail_closed_failures: Sequence[Mapping[str, object]],
) -> bool:
    kind = failure.get("kind")
    if kind in {
        "required-evidence-missing",
        "required-evidence-skipped",
        "blocking-validation-failure",
    }:
        return any(
            _result_matches_failure(result, kind, failure) for result in results
        )
    if kind == "inadmissible-receipt":
        return any(
            _inadmissible_receipt_matches_failure(receipt, failure)
            for receipt in observed
        )
    if kind == "fail-closed":
        return any(
            dict(expected) == dict(failure) for expected in fail_closed_failures
        )
    if kind == "final-evidence-failure":
        diagnostic = failure.get("diagnostic")
        return isinstance(
            diagnostic, Mapping
        ) and _is_final_evidence_diagnostic(diagnostic)
    return False


def _result_matches_failure(
    result: Mapping[str, object],
    failure_kind: object,
    failure: Mapping[str, object],
) -> bool:
    return (
        _failure_kind_for_result_outcome(result.get("outcome")) == failure_kind
        and failure.get("work-group-id") == result.get("work-group-id")
        and failure.get("evidence-expectation-id")
        == result.get("evidence-expectation-id")
        and failure.get("observed-entry-id") == result.get("observed-entry-id")
        and failure.get("receipt-id") == result.get("receipt-id")
        and failure.get("receipt-artifact-ref")
        == result.get("receipt-artifact-ref")
        and failure.get("receipt-content-digest")
        == result.get("receipt-content-digest")
        and _failure_diagnostic_matches(
            failure.get("diagnostic"), result.get("diagnostics")
        )
    )


def _inadmissible_receipt_matches_failure(
    receipt: Mapping[str, object],
    failure: Mapping[str, object],
) -> bool:
    return (
        receipt.get("admissibility") == "inadmissible"
        and failure.get("kind") == "inadmissible-receipt"
        and failure.get("observed-entry-id") == receipt.get("observed-entry-id")
        and failure.get("work-group-id") == receipt.get("work-group-id")
        and failure.get("receipt-id") == receipt.get("receipt-id")
        and failure.get("receipt-artifact-ref") == receipt.get("artifact-ref")
        and failure.get("receipt-content-digest")
        == receipt.get("receipt-content-digest")
        and _failure_diagnostic_matches(
            failure.get("diagnostic"), receipt.get("diagnostics")
        )
    )


def _validate_evidence_result_consistency(  # noqa: PLR0913
    results: object,
    observed: object,
    failures: object,
    counts: object,
    reason: Mapping[object, object],
    verdict: object,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(results, Sequence) or isinstance(results, str | bytes):
        return
    result_items = [item for item in results if isinstance(item, Mapping)]
    outcome_counts = {
        "satisfied": sum(
            item.get("outcome") == "satisfied" for item in result_items
        ),
        "failed": sum(item.get("outcome") == "failed" for item in result_items),
        "skipped": sum(
            item.get("outcome") == "skipped" for item in result_items
        ),
        "missing": sum(
            item.get("outcome") == "missing" for item in result_items
        ),
    }
    if (
        any(outcome_counts[key] for key in ("failed", "skipped", "missing"))
        and verdict != "failed"
    ):
        issues.append(
            ValidationIssue(
                "$.verdict",
                "must fail when evidence results are not satisfied",
            )
        )
    _validate_evidence_counts(outcome_counts, len(result_items), counts, issues)
    failure_items = (
        [item for item in failures if isinstance(item, Mapping)]
        if isinstance(failures, Sequence)
        and not isinstance(failures, str | bytes)
        else []
    )
    for result in result_items:
        outcome = result.get("outcome")
        if outcome == "satisfied":
            _validate_satisfied_evidence_result_diagnostics(result, issues)
        if outcome in {"satisfied", "skipped", "failed"}:
            _validate_receipt_backed_evidence_result(
                result, observed, issues, outcome
            )
        elif outcome == "missing":
            _validate_missing_evidence_result_has_no_valid_receipt(
                result, observed, issues
            )
        failure_kind = _failure_kind_for_result_outcome(outcome)
        if failure_kind is None:
            continue
        if not _has_matching_evidence_failure(
            result, failure_kind, failure_items
        ):
            issues.append(
                ValidationIssue(
                    "$.evidence-results",
                    "non-success results require matching failures",
                )
            )
        if reason.get(failure_kind) is not True:
            issues.append(
                ValidationIssue(
                    f"$.reason.{failure_kind}",
                    "must be true for non-success evidence results",
                )
            )


def _validate_missing_evidence_result_has_no_valid_receipt(
    result: Mapping[str, object],
    observed: object,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(observed, Sequence) or isinstance(observed, str | bytes):
        return
    work_group_id = result.get("work-group-id")
    if not isinstance(work_group_id, str):
        return
    matching_valid = [
        item
        for item in observed
        if isinstance(item, Mapping)
        and item.get("admissibility") == "valid"
        and item.get("work-group-id") == work_group_id
    ]
    if matching_valid:
        issues.append(
            ValidationIssue(
                "$.evidence-results",
                "missing evidence result requires zero valid observed receipts",
            )
        )


def _validate_satisfied_evidence_result_diagnostics(
    result: Mapping[str, object],
    issues: list[ValidationIssue],
) -> None:
    diagnostics = result.get("diagnostics")
    if not isinstance(diagnostics, Sequence) or isinstance(
        diagnostics, str | bytes
    ):
        return
    verdict_affecting_effects = {
        DiagnosticVerdictEffect.FAILED.value,
        DiagnosticVerdictEffect.FAIL_CLOSED.value,
    }
    for index, diagnostic in enumerate(diagnostics):
        if (
            isinstance(diagnostic, Mapping)
            and diagnostic.get("verdict-effect") in verdict_affecting_effects
        ):
            issues.append(
                ValidationIssue(
                    f"$.evidence-results.diagnostics[{index}].verdict-effect",
                    "must not affect verdict for satisfied evidence",
                )
            )


def _validate_receipt_backed_evidence_result(
    result: Mapping[str, object],
    observed: object,
    issues: list[ValidationIssue],
    outcome: object,
) -> None:
    outcome_label = f"{outcome} evidence"
    for key in (
        "observed-entry-id",
        "receipt-id",
        "receipt-artifact-ref",
        "receipt-content-digest",
    ):
        if not isinstance(result.get(key), str):
            issues.append(
                ValidationIssue(
                    f"$.evidence-results.{key}",
                    f"must be present for {outcome_label}",
                )
            )
    if not isinstance(observed, Sequence) or isinstance(observed, str | bytes):
        return
    matches = [
        item
        for item in observed
        if isinstance(item, Mapping)
        and item.get("observed-entry-id") == result.get("observed-entry-id")
    ]
    if len(matches) != 1:
        issues.append(
            ValidationIssue(
                "$.evidence-results",
                f"{outcome_label} requires exactly one observed receipt",
            )
        )
        return
    receipt = matches[0]
    if receipt.get("admissibility") != "valid":
        issues.append(
            ValidationIssue(
                "$.evidence-results",
                f"{outcome_label} requires a valid observed receipt",
            )
        )
    expected_pairs = (
        ("work-group-id", "work-group-id"),
        ("receipt-id", "receipt-id"),
        ("receipt-artifact-ref", "artifact-ref"),
        ("receipt-content-digest", "receipt-content-digest"),
    )
    for result_key, receipt_key in expected_pairs:
        if result.get(result_key) != receipt.get(receipt_key):
            issues.append(
                ValidationIssue(
                    f"$.evidence-results.{result_key}",
                    "must match observed receipt",
                )
            )


def _validate_inadmissible_observed_receipts(
    observed: object,
    failures: object,
    reason: Mapping[object, object],
    verdict: object,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(observed, Sequence) or isinstance(observed, str | bytes):
        return
    inadmissible = [
        item
        for item in observed
        if isinstance(item, Mapping)
        and item.get("admissibility") == "inadmissible"
    ]
    if not inadmissible:
        return
    if verdict != "failed":
        issues.append(
            ValidationIssue(
                "$.verdict",
                "must fail when observed receipts are inadmissible",
            )
        )
    if reason.get("inadmissible-receipt") is not True:
        issues.append(
            ValidationIssue(
                "$.reason.inadmissible-receipt",
                "must be true for inadmissible observed receipts",
            )
        )
    failure_items = (
        [item for item in failures if isinstance(item, Mapping)]
        if isinstance(failures, Sequence)
        and not isinstance(failures, str | bytes)
        else []
    )
    for receipt in inadmissible:
        if not _has_matching_inadmissible_failure(receipt, failure_items):
            issues.append(
                ValidationIssue(
                    "$.observed-receipts",
                    "inadmissible receipts require matching failures",
                )
            )


def _validate_valid_observed_receipts(  # noqa: C901,PLR0912,PLR0913
    observed: object,
    results: object,
    plan: Mapping[str, object] | None,
    observed_inputs: Sequence[CiValidationObservedReceiptInput] | None,
    selector_assignments_manifest: Mapping[str, object] | None,
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    pull_request_merge_commit_verification: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(observed, Sequence) or isinstance(observed, str | bytes):
        return
    valid_by_work_group: dict[str, list[Mapping[str, object]]] = {}
    evidence_entry_ids = _satisfied_evidence_entry_ids_by_work_group(results)
    evidence_work_groups = (
        {
            expectation.get("work-group-id")
            for expectation in _evidence_expectations(plan)
        }
        if plan is not None
        else None
    )
    for index, item in enumerate(observed):
        if (
            not isinstance(item, Mapping)
            or item.get("admissibility") != "valid"
        ):
            continue
        path = f"$.observed-receipts[{index}]"
        ref_work_group = _work_group_id_from_ref(item.get("artifact-ref"))
        work_group_id = item.get("work-group-id")
        for key in (
            "artifact-ref",
            "work-group-id",
            "receipt-id",
            "receipt-content-digest",
        ):
            if not isinstance(item.get(key), str):
                issues.append(
                    ValidationIssue(
                        f"{path}.{key}",
                        "must be present for valid observed receipts",
                    )
                )
        if ref_work_group is None:
            issues.append(
                ValidationIssue(
                    f"{path}.artifact-ref",
                    "must establish a work group for valid observed receipts",
                )
            )
            continue
        diagnostics = item.get("diagnostics")
        if (
            not isinstance(diagnostics, Sequence)
            or isinstance(diagnostics, str | bytes)
            or len(diagnostics) != 0
        ):
            issues.append(
                ValidationIssue(
                    f"{path}.diagnostics",
                    "must be empty for valid observed receipts",
                )
            )
        if work_group_id != ref_work_group:
            continue
        if (
            plan is not None
            and _work_group_kind(plan, ref_work_group)
            == _TERMINAL_WORK_GROUP_KIND
        ):
            issues.append(
                ValidationIssue(
                    f"{path}.work-group-id",
                    "must not be terminal aggregation for valid "
                    "observed receipts",
                )
            )
        if (
            evidence_work_groups is not None
            and ref_work_group not in evidence_work_groups
        ):
            issues.append(
                ValidationIssue(
                    f"{path}.work-group-id",
                    "must match a plan evidence expectation for valid "
                    "observed receipts",
                )
            )
        valid_by_work_group.setdefault(ref_work_group, []).append(item)
    for work_group_id, receipts in valid_by_work_group.items():
        if len(receipts) <= 1:
            continue
        if not (
            plan is not None
            and _work_group_kind(plan, work_group_id)
            == "release-shaped-artifact"
            and _duplicate_valid_receipts_are_chain_shaped(
                work_group_id=work_group_id,
                receipts=receipts,
                evidence_entry_ids=evidence_entry_ids,
                plan=plan,
                observed_inputs=observed_inputs,
                selector_assignments_manifest=selector_assignments_manifest,
                changed_files_snapshot=changed_files_snapshot,
                fact_snapshot=fact_snapshot,
                pull_request_merge_commit_verification=(
                    pull_request_merge_commit_verification
                ),
            )
        ):
            issues.append(
                ValidationIssue(
                    "$.observed-receipts",
                    "must not contain duplicate valid receipts for a "
                    "work group",
                )
            )
    _validate_satisfied_release_shaped_sources(
        observed=observed,
        results=results,
        plan=plan,
        observed_inputs=observed_inputs,
        selector_assignments_manifest=selector_assignments_manifest,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        pull_request_merge_commit_verification=(
            pull_request_merge_commit_verification
        ),
        issues=issues,
    )


def _validate_satisfied_release_shaped_sources(  # noqa: PLR0913
    *,
    observed: object,
    results: object,
    plan: Mapping[str, object] | None,
    observed_inputs: Sequence[CiValidationObservedReceiptInput] | None,
    selector_assignments_manifest: Mapping[str, object] | None,
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    pull_request_merge_commit_verification: Mapping[str, object] | None,
    issues: list[ValidationIssue],
) -> None:
    if (
        plan is None
        or not isinstance(results, Sequence)
        or isinstance(results, str | bytes)
        or not isinstance(observed, Sequence)
        or isinstance(observed, str | bytes)
    ):
        return
    observed_items = [item for item in observed if isinstance(item, Mapping)]
    release_results = [
        item
        for item in results
        if (
            isinstance(item, Mapping)
            and item.get("outcome") == "satisfied"
            and isinstance(item.get("work-group-id"), str)
            and _work_group_kind(plan, cast("str", item["work-group-id"]))
            == "release-shaped-artifact"
        )
    ]
    if not release_results:
        return
    if observed_inputs is None or selector_assignments_manifest is None:
        issues.append(
            ValidationIssue(
                "$.evidence-results",
                "satisfied release-shaped evidence requires observed source "
                "proof",
            )
        )
        return
    receipts_by_entry = _observed_receipts_by_summary_entry_id(
        observed_items, observed_inputs
    )
    for result in release_results:
        entry_id = result.get("observed-entry-id")
        work_group_id = result.get("work-group-id")
        if not isinstance(entry_id, str) or not isinstance(work_group_id, str):
            continue
        receipt = receipts_by_entry.get(entry_id)
        if receipt is None or not _release_shaped_success_source_is_admissible(
            receipt=receipt,
            plan=plan,
            selector_assignments_manifest=selector_assignments_manifest,
            work_group_id=work_group_id,
            entry_id=entry_id,
            receipts_by_entry=receipts_by_entry,
            observed_inputs=observed_inputs,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            pull_request_merge_commit_verification=(
                pull_request_merge_commit_verification
            ),
            visited_receipt_digests=set(),
        ):
            issues.append(
                ValidationIssue(
                    "$.evidence-results",
                    "satisfied release-shaped evidence requires observed "
                    "source proof",
                )
            )


def _satisfied_evidence_entry_ids_by_work_group(
    results: object,
) -> dict[str, set[str]]:
    if not isinstance(results, Sequence) or isinstance(results, str | bytes):
        return {}
    entry_ids: dict[str, set[str]] = {}
    for result in results:
        if not (
            isinstance(result, Mapping)
            and result.get("outcome") == "satisfied"
            and isinstance(result.get("work-group-id"), str)
            and isinstance(result.get("observed-entry-id"), str)
        ):
            continue
        entry_ids.setdefault(cast("str", result["work-group-id"]), set()).add(
            cast("str", result["observed-entry-id"])
        )
    return entry_ids


def _duplicate_valid_receipts_are_chain_shaped(  # noqa: PLR0913
    *,
    work_group_id: str,
    receipts: Sequence[Mapping[str, object]],
    evidence_entry_ids: Mapping[str, set[str]],
    plan: Mapping[str, object],
    observed_inputs: Sequence[CiValidationObservedReceiptInput] | None,
    selector_assignments_manifest: Mapping[str, object] | None,
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    pull_request_merge_commit_verification: Mapping[str, object] | None,
) -> bool:
    referenced = evidence_entry_ids.get(work_group_id, set())
    receipt_entry_ids = {
        cast("str", receipt["observed-entry-id"])
        for receipt in receipts
        if isinstance(receipt.get("observed-entry-id"), str)
    }
    if len(referenced & receipt_entry_ids) != 1:
        return False
    digests = [
        receipt.get("receipt-content-digest")
        for receipt in receipts
        if isinstance(receipt.get("receipt-content-digest"), str)
    ]
    if len(digests) != len(receipts) or len(set(digests)) != len(digests):
        return False
    if observed_inputs is None or selector_assignments_manifest is None:
        return False
    receipts_by_entry = _observed_receipts_by_summary_entry_id(
        receipts, observed_inputs
    )
    if set(receipts_by_entry) != receipt_entry_ids:
        return False
    proven_chain = _valid_reused_chain_entry_ids_for_duplicate_group(
        plan=plan,
        work_group_id=work_group_id,
        summaries=receipts,
        receipts_by_entry=receipts_by_entry,
        observed_inputs=observed_inputs,
        selector_assignments_manifest=selector_assignments_manifest,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        pull_request_merge_commit_verification=(
            pull_request_merge_commit_verification
        ),
    )
    return proven_chain is not None and receipt_entry_ids <= proven_chain


def _observed_receipts_by_summary_entry_id(
    summaries: Sequence[Mapping[str, object]],
    observed_inputs: Sequence[CiValidationObservedReceiptInput],
) -> dict[str, Mapping[str, object]]:
    summaries_by_entry = {
        cast("str", summary["observed-entry-id"]): summary
        for summary in summaries
        if isinstance(summary.get("observed-entry-id"), str)
    }
    receipts_by_entry: dict[str, Mapping[str, object]] = {}
    for observed in observed_inputs:
        entry_id = observed.manifest_entry.get("observed-entry-id")
        if not isinstance(entry_id, str) or entry_id not in summaries_by_entry:
            continue
        receipt = observed.receipt
        if receipt is None:
            continue
        summary = summaries_by_entry[entry_id]
        if (
            summary.get("artifact-ref")
            == observed.manifest_entry.get("artifact-ref")
            and summary.get("receipt-id")
            == observed.manifest_entry.get("receipt-id")
            and summary.get("receipt-content-digest")
            == observed.manifest_entry.get("receipt-content-digest")
            and _receipt_payload_matches_observed_bytes(
                observed.manifest_entry,
                receipt,
                observed.raw_receipt_bytes,
            )
        ):
            receipts_by_entry[entry_id] = receipt
    return receipts_by_entry


def _has_matching_inadmissible_failure(
    receipt: Mapping[str, object],
    failures: Sequence[Mapping[str, object]],
) -> bool:
    return any(
        failure.get("kind") == "inadmissible-receipt"
        and failure.get("observed-entry-id") == receipt.get("observed-entry-id")
        and failure.get("work-group-id") == receipt.get("work-group-id")
        and failure.get("receipt-id") == receipt.get("receipt-id")
        and failure.get("receipt-artifact-ref") == receipt.get("artifact-ref")
        and failure.get("receipt-content-digest")
        == receipt.get("receipt-content-digest")
        and _failure_diagnostic_matches(
            failure.get("diagnostic"), receipt.get("diagnostics")
        )
        for failure in failures
    )


def _validate_evidence_counts(
    outcome_counts: Mapping[str, int],
    total: int,
    counts: object,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(counts, Mapping):
        return
    expected = {
        "executable-required": total,
        "required-succeeded": outcome_counts["satisfied"],
        "required-failed": outcome_counts["failed"],
        "required-skipped": outcome_counts["skipped"],
        "required-missing": outcome_counts["missing"],
    }
    for key, value in expected.items():
        if counts.get(key) != value:
            issues.append(
                ValidationIssue(
                    f"$.work-groups.{key}",
                    "must match evidence-result outcomes",
                )
            )


def _failure_kind_for_result_outcome(outcome: object) -> str | None:
    if outcome == "missing":
        return "required-evidence-missing"
    if outcome == "skipped":
        return "required-evidence-skipped"
    if outcome == "failed":
        return "blocking-validation-failure"
    return None


def _has_matching_evidence_failure(
    result: Mapping[str, object],
    failure_kind: str,
    failures: Sequence[Mapping[str, object]],
) -> bool:
    return any(
        failure.get("kind") == failure_kind
        and failure.get("work-group-id") == result.get("work-group-id")
        and failure.get("evidence-expectation-id")
        == result.get("evidence-expectation-id")
        and failure.get("observed-entry-id") == result.get("observed-entry-id")
        and failure.get("receipt-id") == result.get("receipt-id")
        and failure.get("receipt-artifact-ref")
        == result.get("receipt-artifact-ref")
        and failure.get("receipt-content-digest")
        == result.get("receipt-content-digest")
        and _failure_diagnostic_matches(
            failure.get("diagnostic"), result.get("diagnostics")
        )
        for failure in failures
    )


def _failure_diagnostic_matches(
    failure_diagnostic: object, referenced_diagnostics: object
) -> bool:
    if not isinstance(failure_diagnostic, Mapping):
        return False
    if not isinstance(referenced_diagnostics, Sequence) or isinstance(
        referenced_diagnostics, str | bytes
    ):
        return False
    return any(
        isinstance(diagnostic, Mapping)
        and dict(diagnostic) == dict(failure_diagnostic)
        for diagnostic in referenced_diagnostics
    )


def _validate_observed_receipts(
    value: object,
    envelope: CommonEnvelope | None,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        issues.append(
            ValidationIssue("$.observed-receipts", "must be an array")
        )
        return
    observed_entry_ids: dict[str, int] = {}
    for index, item in enumerate(value):
        path = f"$.observed-receipts[{index}]"
        if not isinstance(item, Mapping):
            issues.append(ValidationIssue(path, "must be an object"))
            continue
        _validate_object(item, _OBSERVED_RECEIPT_KEYS, path, issues)
        _validate_local_id(
            item.get("observed-entry-id"), f"{path}.observed-entry-id", issues
        )
        _validate_observed_entry_id_is_unique(
            item, observed_entry_ids, index, path, issues
        )
        _validate_nullable_artifact_ref(
            item.get("artifact-ref"), f"{path}.artifact-ref", issues
        )
        if envelope is not None:
            _validate_observed_receipt_artifact_ref(
                item, envelope, path, issues
            )
            _validate_observed_receipt_entry_id(item, envelope, path, issues)
        _validate_non_empty_string(
            item.get("physical-artifact-name"),
            f"{path}.physical-artifact-name",
            issues,
        )
        try:
            validate_artifact_physical_name(item.get("physical-artifact-name"))
        except ContractValidationError as error:
            issues.extend(
                ValidationIssue(f"{path}.physical-artifact-name", e.message)
                for e in error.issues
            )
        artifact_ref = item.get("artifact-ref")
        physical = item.get("physical-artifact-name")
        if isinstance(artifact_ref, str) and physical != artifact_physical_name(
            artifact_ref
        ):
            issues.append(
                ValidationIssue(
                    f"{path}.physical-artifact-name",
                    "must match artifact-ref",
                )
            )
        _validate_non_empty_string(
            item.get("artifact-instance-id"),
            f"{path}.artifact-instance-id",
            issues,
        )
        _validate_nullable_local_id(
            item.get("receipt-id"), f"{path}.receipt-id", issues
        )
        _validate_nullable_local_id(
            item.get("work-group-id"), f"{path}.work-group-id", issues
        )
        _validate_observed_work_group_binding(item, path, issues)
        _validate_nullable_digest(
            item.get("receipt-content-digest"),
            f"{path}.receipt-content-digest",
            issues,
        )
        if (
            item.get("admissibility") == "valid"
            and item.get("artifact-ref") is not None
            and item.get("receipt-id") is not None
            and item.get("receipt-content-digest") is None
        ):
            issues.append(
                ValidationIssue(
                    f"{path}.receipt-content-digest",
                    "must be present for valid readable receipts",
                )
            )
        if item.get("admissibility") not in _RECEIPT_ADMISSIBILITIES:
            issues.append(
                ValidationIssue(f"{path}.admissibility", "is not registered")
            )
        _validate_diagnostic_array(
            item.get("diagnostics"), f"{path}.diagnostics", issues
        )


def _validate_observed_entry_id_is_unique(
    item: Mapping[str, object],
    observed_entry_ids: dict[str, int],
    index: int,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    observed_entry_id = item.get("observed-entry-id")
    if not isinstance(observed_entry_id, str):
        return
    previous_index = observed_entry_ids.get(observed_entry_id)
    if previous_index is None:
        observed_entry_ids[observed_entry_id] = index
        return
    issues.append(
        ValidationIssue(
            f"{path}.observed-entry-id",
            "must be unique within observed-receipts; "
            f"duplicates $.observed-receipts[{previous_index}]",
        )
    )


def _validate_observed_receipt_artifact_ref(
    item: Mapping[str, object],
    envelope: CommonEnvelope,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    artifact_ref = item.get("artifact-ref")
    if artifact_ref is None or not isinstance(artifact_ref, str):
        return
    match = _RECEIPT_REF_RE.fullmatch(artifact_ref)
    if match is None:
        issues.append(
            ValidationIssue(
                f"{path}.artifact-ref",
                "must be a current run-attempt receipt ref",
            )
        )
        return
    run_id, run_attempt, _work_group_id = match.groups()
    if run_id != envelope.run_id:
        issues.append(
            ValidationIssue(f"{path}.artifact-ref", "must match aggregate run")
        )
    if run_attempt != envelope.run_attempt:
        issues.append(
            ValidationIssue(
                f"{path}.artifact-ref", "must match aggregate run attempt"
            )
        )


def _validate_observed_receipt_entry_id(
    item: Mapping[str, object],
    envelope: CommonEnvelope,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    artifact_ref = item.get("artifact-ref")
    artifact_instance_id = item.get("artifact-instance-id")
    if not (
        (artifact_ref is None or isinstance(artifact_ref, str))
        and isinstance(artifact_instance_id, str)
        and artifact_instance_id != ""
    ):
        return
    try:
        expected = ci_validation_observed_entry_id(
            run_id=envelope.run_id,
            run_attempt=envelope.run_attempt,
            artifact_ref=artifact_ref,
            artifact_instance_id=artifact_instance_id,
        )
    except ContractValidationError:
        return
    if item.get("observed-entry-id") != expected:
        issues.append(
            ValidationIssue(
                f"{path}.observed-entry-id",
                "must match canonical derivation",
            )
        )


def _validate_observed_work_group_binding(
    item: Mapping[str, object], path: str, issues: list[ValidationIssue]
) -> None:
    expected = _work_group_id_from_ref(item.get("artifact-ref"))
    actual = item.get("work-group-id")
    if expected is None:
        if actual is not None:
            issues.append(
                ValidationIssue(
                    f"{path}.work-group-id",
                    "must be null without an established receipt artifact-ref",
                )
            )
        return
    if actual != expected:
        issues.append(
            ValidationIssue(
                f"{path}.work-group-id", "must match receipt artifact-ref"
            )
        )


def _validate_evidence_results(
    value: object, issues: list[ValidationIssue]
) -> None:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        issues.append(ValidationIssue("$.evidence-results", "must be an array"))
        return
    seen_ids: set[str] = set()
    for index, item in enumerate(value):
        path = f"$.evidence-results[{index}]"
        if not isinstance(item, Mapping):
            issues.append(ValidationIssue(path, "must be an object"))
            continue
        _validate_object(item, _EVIDENCE_RESULT_KEYS, path, issues)
        _validate_local_id(
            item.get("evidence-expectation-id"),
            f"{path}.evidence-expectation-id",
            issues,
        )
        evidence_id = item.get("evidence-expectation-id")
        if isinstance(evidence_id, str):
            if evidence_id in seen_ids:
                issues.append(
                    ValidationIssue(
                        f"{path}.evidence-expectation-id",
                        "must be unique",
                    )
                )
            seen_ids.add(evidence_id)
        _validate_local_id(
            item.get("work-group-id"), f"{path}.work-group-id", issues
        )
        _validate_nullable_local_id(
            item.get("receipt-id"), f"{path}.receipt-id", issues
        )
        _validate_nullable_local_id(
            item.get("observed-entry-id"), f"{path}.observed-entry-id", issues
        )
        _validate_nullable_artifact_ref(
            item.get("receipt-artifact-ref"),
            f"{path}.receipt-artifact-ref",
            issues,
        )
        _validate_nullable_digest(
            item.get("receipt-content-digest"),
            f"{path}.receipt-content-digest",
            issues,
        )
        if item.get("outcome") not in _RESULT_OUTCOMES:
            issues.append(
                ValidationIssue(f"{path}.outcome", "is not registered")
            )
        _validate_diagnostic_array(
            item.get("diagnostics"), f"{path}.diagnostics", issues
        )


def _validate_failures(value: object, issues: list[ValidationIssue]) -> None:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        issues.append(ValidationIssue("$.failures", "must be an array"))
        return
    for index, item in enumerate(value):
        path = f"$.failures[{index}]"
        if not isinstance(item, Mapping):
            issues.append(ValidationIssue(path, "must be an object"))
            continue
        _validate_object(item, _FAILURE_KEYS, path, issues)
        if item.get("kind") not in _FAILURE_KINDS:
            issues.append(ValidationIssue(f"{path}.kind", "is not registered"))
        _validate_nullable_local_id(
            item.get("work-group-id"), f"{path}.work-group-id", issues
        )
        _validate_nullable_local_id(
            item.get("evidence-expectation-id"),
            f"{path}.evidence-expectation-id",
            issues,
        )
        _validate_nullable_local_id(
            item.get("receipt-id"), f"{path}.receipt-id", issues
        )
        _validate_nullable_local_id(
            item.get("observed-entry-id"), f"{path}.observed-entry-id", issues
        )
        _validate_nullable_artifact_ref(
            item.get("receipt-artifact-ref"),
            f"{path}.receipt-artifact-ref",
            issues,
        )
        _validate_nullable_digest(
            item.get("receipt-content-digest"),
            f"{path}.receipt-content-digest",
            issues,
        )
        if (
            not isinstance(item.get("message"), str)
            or item.get("message") == ""
        ):
            issues.append(
                ValidationIssue(f"{path}.message", "must be a string")
            )
        diagnostic = item.get("diagnostic")
        if isinstance(diagnostic, Mapping):
            _validate_diagnostic(diagnostic, f"{path}.diagnostic", issues)
            _validate_failure_diagnostic_binding(item, diagnostic, path, issues)
        else:
            issues.append(
                ValidationIssue(f"{path}.diagnostic", "must be an object")
            )


def _validate_failure_diagnostic_binding(
    failure: Mapping[str, object],
    diagnostic: Mapping[str, object],
    path: str,
    issues: list[ValidationIssue],
) -> None:
    kind = failure.get("kind")
    if kind == "invalid-plan":
        _validate_invalid_plan_diagnostic(
            diagnostic, f"{path}.diagnostic", issues
        )
    elif kind == "final-evidence-failure":
        _validate_final_evidence_diagnostic(
            diagnostic, f"{path}.diagnostic", issues
        )
    elif kind == "inadmissible-receipt":
        _validate_failure_diagnostic_family(
            diagnostic,
            DiagnosticFamily.INADMISSIBLE_RECEIPT.value,
            f"{path}.diagnostic",
            issues,
        )
    elif kind == "required-evidence-missing":
        _validate_failure_diagnostic_family(
            diagnostic,
            DiagnosticFamily.REQUIRED_EVIDENCE_MISSING.value,
            f"{path}.diagnostic",
            issues,
        )
    elif kind == "required-evidence-skipped":
        _validate_failure_diagnostic_family(
            diagnostic,
            DiagnosticFamily.REQUIRED_EVIDENCE_SKIPPED.value,
            f"{path}.diagnostic",
            issues,
        )
    elif kind == "blocking-validation-failure":
        _validate_blocking_validation_failure_diagnostic(
            diagnostic, f"{path}.diagnostic", issues
        )
    elif kind == "fail-closed":
        _validate_fail_closed_diagnostic(
            diagnostic, f"{path}.diagnostic", issues
        )


def _validate_blocking_validation_failure_diagnostic(
    diagnostic: Mapping[str, object], path: str, issues: list[ValidationIssue]
) -> None:
    code = diagnostic.get("code")
    if code not in _BLOCKING_VALIDATION_FAILURE_FAMILIES:
        issues.append(
            ValidationIssue(
                f"{path}.code",
                "must be a blocking validation diagnostic family for this "
                "failure kind",
            )
        )
        return
    detail = diagnostic.get("detail")
    registered_details = DETAILS_BY_DIAGNOSTIC_CODE.get(cast("str", code))
    if registered_details is not None and detail not in registered_details:
        issues.append(
            ValidationIssue(f"{path}.detail", "must match diagnostic family")
        )
    _validate_failed_diagnostic(diagnostic, path, issues)


def _validate_failure_diagnostic_family(
    diagnostic: Mapping[str, object],
    expected_code: str,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if diagnostic.get("code") != expected_code:
        issues.append(
            ValidationIssue(
                f"{path}.code",
                f"must be {expected_code} for this failure kind",
            )
        )
        return
    detail = diagnostic.get("detail")
    registered_details = DETAILS_BY_DIAGNOSTIC_CODE.get(expected_code)
    if registered_details is not None and detail not in registered_details:
        issues.append(
            ValidationIssue(f"{path}.detail", "must match diagnostic family")
        )
    _validate_failed_diagnostic(diagnostic, path, issues)


def _validate_failed_diagnostic(
    diagnostic: Mapping[str, object],
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if diagnostic.get("verdict-effect") != DiagnosticVerdictEffect.FAILED.value:
        issues.append(
            ValidationIssue(
                f"{path}.verdict-effect",
                "must be failed for failed aggregate failures",
            )
        )


def _validate_fail_closed_diagnostic(
    diagnostic: Mapping[str, object],
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if (
        diagnostic.get("verdict-effect")
        != DiagnosticVerdictEffect.FAIL_CLOSED.value
    ):
        issues.append(
            ValidationIssue(
                f"{path}.verdict-effect",
                "must be fail-closed for fail-closed failures",
            )
        )


def _validate_invalid_plan_diagnostic(
    diagnostic: Mapping[str, object],
    path: str,
    issues: list[ValidationIssue],
) -> None:
    code = diagnostic.get("code")
    if code != DiagnosticFamily.INVALID_PLAN.value:
        issues.append(
            ValidationIssue(
                f"{path}.code",
                "must be invalid-plan for invalid-plan failures",
            )
        )
        return
    detail = diagnostic.get("detail")
    if (
        detail
        not in DETAILS_BY_DIAGNOSTIC_CODE[DiagnosticFamily.INVALID_PLAN.value]
    ):
        issues.append(
            ValidationIssue(f"{path}.detail", "must be an invalid-plan detail")
        )
    _validate_failed_diagnostic(diagnostic, path, issues)


def _is_final_evidence_diagnostic(diagnostic: Mapping[str, object]) -> bool:
    detail = diagnostic.get("detail")
    return (
        diagnostic.get("code") == DiagnosticFamily.FINAL_EVIDENCE_FAILURE.value
        and detail
        in (
            DETAILS_BY_DIAGNOSTIC_CODE[
                DiagnosticFamily.FINAL_EVIDENCE_FAILURE.value
            ]
        )
    )


def _validate_final_evidence_diagnostic(
    diagnostic: Mapping[str, object],
    path: str,
    issues: list[ValidationIssue],
) -> None:
    code = diagnostic.get("code")
    if code != DiagnosticFamily.FINAL_EVIDENCE_FAILURE.value:
        issues.append(
            ValidationIssue(
                f"{path}.code",
                "must be final-evidence-failure for final-evidence failures",
            )
        )
        return
    detail = diagnostic.get("detail")
    if (
        detail
        not in DETAILS_BY_DIAGNOSTIC_CODE[
            DiagnosticFamily.FINAL_EVIDENCE_FAILURE.value
        ]
    ):
        issues.append(
            ValidationIssue(
                f"{path}.detail",
                "must be a final-evidence-failure detail",
            )
        )
    _validate_failed_diagnostic(diagnostic, path, issues)


def _validate_work_group_counts(
    value: object, issues: list[ValidationIssue]
) -> None:
    _validate_object(value, _WORK_GROUP_COUNTS_KEYS, "$.work-groups", issues)
    if not isinstance(value, Mapping):
        return
    for key in _WORK_GROUP_COUNTS_KEYS - {"terminal-aggregation"}:
        count = value.get(key)
        if not isinstance(count, int) or isinstance(count, bool) or count < 0:
            issues.append(
                ValidationIssue(
                    f"$.work-groups.{key}", "must be a non-negative integer"
                )
            )
    if value.get("terminal-aggregation") != "present":
        issues.append(
            ValidationIssue(
                "$.work-groups.terminal-aggregation", "must be present"
            )
        )


def _validate_manifest_entry(
    entry: Mapping[str, object], path: str, issues: list[ValidationIssue]
) -> None:
    _validate_root_keys(entry, _MANIFEST_ENTRY_KEYS, path, issues)
    _validate_local_id(
        entry.get("observed-entry-id"), f"{path}.observed-entry-id", issues
    )
    _validate_nullable_artifact_ref(
        entry.get("artifact-ref"), f"{path}.artifact-ref", issues
    )
    physical = entry.get("physical-artifact-name")
    _validate_non_empty_string(
        physical, f"{path}.physical-artifact-name", issues
    )
    try:
        validate_artifact_physical_name(physical)
    except ContractValidationError as error:
        issues.extend(
            ValidationIssue(f"{path}.physical-artifact-name", e.message)
            for e in error.issues
        )
    artifact_ref = entry.get("artifact-ref")
    if isinstance(artifact_ref, str) and physical != artifact_physical_name(
        artifact_ref
    ):
        issues.append(
            ValidationIssue(
                f"{path}.physical-artifact-name", "must match artifact-ref"
            )
        )
    _validate_non_empty_string(
        entry.get("artifact-instance-id"),
        f"{path}.artifact-instance-id",
        issues,
    )
    _validate_nullable_local_id(
        entry.get("assignment-id"), f"{path}.assignment-id", issues
    )
    _validate_nullable_local_id(
        entry.get("writer-work-group-id"),
        f"{path}.writer-work-group-id",
        issues,
    )
    _validate_nullable_writer_id(
        entry.get("trusted-writer-id"), f"{path}.trusted-writer-id", issues
    )
    _validate_nullable_writer_id(
        entry.get("observed-writer-id"), f"{path}.observed-writer-id", issues
    )
    _validate_nullable_artifact_ref(
        entry.get("writer-observation-ref"),
        f"{path}.writer-observation-ref",
        issues,
    )
    _validate_nullable_local_id(
        entry.get("receipt-id"), f"{path}.receipt-id", issues
    )
    _validate_nullable_digest(
        entry.get("receipt-content-digest"),
        f"{path}.receipt-content-digest",
        issues,
    )
    ref_work_group = _work_group_id_from_ref(artifact_ref)
    if ref_work_group is None:
        for key in (
            "assignment-id",
            "writer-work-group-id",
            "trusted-writer-id",
            "observed-writer-id",
            "writer-observation-ref",
            "receipt-id",
        ):
            if entry.get(key) is not None:
                issues.append(
                    ValidationIssue(
                        f"{path}.{key}",
                        "must be null without an established artifact-ref",
                    )
                )
    if (
        ref_work_group is not None
        and entry.get("writer-work-group-id") is not None
        and entry.get("writer-work-group-id") != ref_work_group
    ):
        issues.append(
            ValidationIssue(
                f"{path}.writer-work-group-id", "must match artifact-ref"
            )
        )


def _validate_manifest_observed_entry_id(
    entry: Mapping[str, object],
    envelope: CommonEnvelope,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    artifact_ref = entry.get("artifact-ref")
    artifact_instance_id = entry.get("artifact-instance-id")
    if not (
        (artifact_ref is None or isinstance(artifact_ref, str))
        and isinstance(artifact_instance_id, str)
        and artifact_instance_id != ""
    ):
        return
    try:
        expected = ci_validation_observed_entry_id(
            run_id=envelope.run_id,
            run_attempt=envelope.run_attempt,
            artifact_ref=artifact_ref,
            artifact_instance_id=artifact_instance_id,
        )
    except ContractValidationError:
        return
    if entry.get("observed-entry-id") != expected:
        issues.append(
            ValidationIssue(
                f"{path}.observed-entry-id",
                "must match canonical derivation",
            )
        )


def _validate_manifest_receipt_artifact_ref(
    entry: Mapping[str, object],
    envelope: CommonEnvelope,
    path: str,
    issues: list[ValidationIssue],
) -> None:
    artifact_ref = entry.get("artifact-ref")
    if artifact_ref is None:
        return
    if not isinstance(artifact_ref, str):
        return
    match = _RECEIPT_REF_RE.fullmatch(artifact_ref)
    if match is None:
        issues.append(
            ValidationIssue(
                f"{path}.artifact-ref",
                "must be a current run-attempt receipt ref",
            )
        )
        return
    run_id, run_attempt, work_group_id = match.groups()
    if run_id != envelope.run_id:
        issues.append(
            ValidationIssue(f"{path}.artifact-ref", "must match manifest run")
        )
    if run_attempt != envelope.run_attempt:
        issues.append(
            ValidationIssue(
                f"{path}.artifact-ref", "must match manifest run attempt"
            )
        )
    if _LOCAL_ID_RE.fullmatch(work_group_id) is None:
        issues.append(
            ValidationIssue(
                f"{path}.artifact-ref", "must contain a path-safe work group"
            )
        )


def _validate_manifest_closure(
    value: object,
    entries: Sequence[Mapping[str, object]],
    issues: list[ValidationIssue],
) -> None:
    _validate_object(
        value, _MANIFEST_CLOSURE_KEYS, "$.receipt-namespace-closure", issues
    )
    if not isinstance(value, Mapping):
        return
    if value.get("source") != "aggregate-evidence":
        issues.append(
            ValidationIssue(
                "$.receipt-namespace-closure.source",
                "must be aggregate-evidence",
            )
        )
    closed_count = value.get("closed-receipt-count")
    if (
        not isinstance(closed_count, int)
        or isinstance(closed_count, bool)
        or closed_count < 0
    ):
        issues.append(
            ValidationIssue(
                "$.receipt-namespace-closure.closed-receipt-count",
                "must be a non-negative integer",
            )
        )
    elif closed_count != len(entries):
        issues.append(
            ValidationIssue(
                "$.receipt-namespace-closure.closed-receipt-count",
                "must equal entries length",
            )
        )
    ids = value.get("observed-entry-ids")
    expected = [item.get("observed-entry-id") for item in entries]
    if ids != expected:
        issues.append(
            ValidationIssue(
                "$.receipt-namespace-closure.observed-entry-ids",
                "must equal entries",
            )
        )


def _assignments_by_work_group(
    manifest: Mapping[str, object],
) -> dict[str, Mapping[str, object]]:
    assignments = manifest.get("assignments")
    if not isinstance(assignments, Sequence) or isinstance(
        assignments, str | bytes
    ):
        return {}
    result: dict[str, Mapping[str, object]] = {}
    for item in assignments:
        if isinstance(item, Mapping) and isinstance(
            item.get("work-group-id"), str
        ):
            result[cast("str", item["work-group-id"])] = item
    return result


def _manifest_entry_matches_assignment(
    entry: Mapping[str, object],
    assignment: Mapping[str, object],
) -> bool:
    return all(
        entry.get(entry_key) == assignment.get(assignment_key)
        for entry_key, assignment_key in (
            ("assignment-id", "assignment-id"),
            ("writer-work-group-id", "work-group-id"),
            ("artifact-ref", "receipt-artifact-ref"),
            ("trusted-writer-id", "trusted-writer-id"),
            ("writer-observation-ref", "writer-observation-ref"),
        )
    ) and entry.get("observed-writer-id") == assignment.get("trusted-writer-id")


def _entry_work_group_id(
    entry: Mapping[str, object], _receipt: Mapping[str, object] | None
) -> str | None:
    from_ref = _work_group_id_from_ref(entry.get("artifact-ref"))
    if from_ref is not None:
        return from_ref
    return None


def _work_group_id_from_ref(value: object) -> str | None:
    if not isinstance(value, str):
        return None
    match = _RECEIPT_REF_RE.fullmatch(value)
    if match is None:
        return None
    return match.group(3)


def _work_group_kind(
    plan: Mapping[str, object], work_group_id: str
) -> str | None:
    groups = plan.get("work-groups")
    if not isinstance(groups, Sequence) or isinstance(groups, str | bytes):
        return None
    for item in groups:
        if (
            isinstance(item, Mapping)
            and item.get("work-group-id") == work_group_id
        ):
            kind = item.get("kind")
            return kind if isinstance(kind, str) else None
    return None


def _evidence_expectations(
    plan: Mapping[str, object],
) -> list[Mapping[str, object]]:
    expectations = plan.get("evidence-expectations")
    if not isinstance(expectations, Sequence) or isinstance(
        expectations, str | bytes
    ):
        return []
    return [item for item in expectations if isinstance(item, Mapping)]


def _receipt_error_detail(error: ContractValidationError) -> str:
    text = " ".join(f"{issue.path} {issue.message}" for issue in error.issues)
    if "plan" in text:
        return DiagnosticDetail.WRONG_PLAN.value
    if "work-group" in text:
        return DiagnosticDetail.MISMATCHED_WORK_GROUP.value
    if "outcome" in text:
        return DiagnosticDetail.MISMATCHED_OUTCOME.value
    return DiagnosticDetail.MISMATCHED_EVIDENCE_PAYLOAD.value


def _inadmissible_diagnostic(
    entry_id: str, detail: str, message: str
) -> dict[str, object]:
    return _diagnostic(
        diagnostic_id=f"inadmissible-receipt/{entry_id}/{detail}",
        code=DiagnosticFamily.INADMISSIBLE_RECEIPT.value,
        detail=detail,
        message=message,
    )


def _diagnostic(  # noqa: PLR0913
    *,
    diagnostic_id: str,
    code: str,
    detail: str | None,
    message: str | None,
    source_type: str = "aggregation",
    source_id: str | None = None,
    severity: str = DiagnosticSeverity.BLOCKING_FAILURE.value,
    verdict_effect: str = DiagnosticVerdictEffect.FAILED.value,
) -> dict[str, object]:
    return ci_validation_diagnostic(
        diagnostic_id=diagnostic_id,
        code=code,
        detail=detail,
        message=message,
        source_type=source_type,
        source_id=source_id,
        severity=severity,
        verdict_effect=verdict_effect,
    )


def _failure(  # noqa: PLR0913
    *,
    kind: FailureKind,
    diagnostic: Mapping[str, object],
    message: str,
    work_group_id: str | None = None,
    evidence_expectation_id: str | None = None,
    receipt_id: str | None = None,
    observed_entry_id: str | None = None,
    receipt_artifact_ref: str | None = None,
    receipt_content_digest: str | None = None,
) -> dict[str, object]:
    return {
        "kind": kind,
        "work-group-id": work_group_id,
        "evidence-expectation-id": evidence_expectation_id,
        "receipt-id": receipt_id,
        "observed-entry-id": observed_entry_id,
        "receipt-artifact-ref": receipt_artifact_ref,
        "receipt-content-digest": receipt_content_digest,
        "diagnostic": dict(diagnostic),
        "message": message,
    }


def _validated_plan_envelope(  # noqa: PLR0913
    plan: Mapping[str, object],
    issues: list[ValidationIssue],
    *,
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    pull_request_merge_commit_verification: Mapping[str, object] | None,
    expected_run_id: str | None = None,
    expected_run_attempt: str | None = None,
) -> CommonEnvelope | None:
    try:
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            pull_request_merge_commit_verification=(
                pull_request_merge_commit_verification
            ),
            expected_run_id=expected_run_id,
            expected_run_attempt=expected_run_attempt,
        )
        return _envelope(plan, CiValidationKind.PLAN)
    except ContractValidationError as error:
        issues.extend(error.issues)
        return None


def _envelope(
    document: Mapping[str, object], kind: CiValidationKind
) -> CommonEnvelope:
    return validate_common_envelope(
        document,
        api_version=API_VERSIONS_BY_KIND[kind.value],
        kind=kind,
    )


def _envelope_or_collect(
    document: Mapping[str, object],
    kind: CiValidationKind,
    issues: list[ValidationIssue],
) -> CommonEnvelope | None:
    try:
        return _envelope(document, kind)
    except ContractValidationError as error:
        issues.extend(error.issues)
        return None


def _verified_plan_digest(plan: Mapping[str, object]) -> str:
    digest = plan.get("plan-digest")
    if not isinstance(digest, str) or _DIGEST_RE.fullmatch(digest) is None:
        raise ContractValidationError(
            [ValidationIssue("plan-digest", "must be a SHA-256 digest")]
        )
    recomputed = ci_validation_plan_digest(plan)
    if digest != recomputed:
        raise ContractValidationError(
            [ValidationIssue("plan-digest", "must match canonical plan")]
        )
    return digest


def _validate_envelope_matches(
    left: CommonEnvelope, right: CommonEnvelope, issues: list[ValidationIssue]
) -> None:
    if left.repository_owner != right.repository_owner:
        issues.append(ValidationIssue("$.repository.owner", "must match plan"))
    if left.repository_name != right.repository_name:
        issues.append(ValidationIssue("$.repository.name", "must match plan"))
    if left.workflow != right.workflow:
        issues.append(ValidationIssue("$.run.workflow", "must match plan"))
    if left.run_id != right.run_id:
        issues.append(ValidationIssue("$.run.run-id", "must match plan"))
    if left.run_attempt != right.run_attempt:
        issues.append(ValidationIssue("$.run.run-attempt", "must match plan"))


def _validate_canonical(
    value: object, path: str, issues: list[ValidationIssue]
) -> None:
    try:
        canonical_json_bytes(value)
    except (TypeError, ValueError) as error:
        issues.append(ValidationIssue(path, str(error)))


def _validate_root_keys(
    document: Mapping[str, object],
    allowed: frozenset[str],
    path: str,
    issues: list[ValidationIssue],
) -> None:
    keys = set()
    for key in document:
        if not isinstance(key, str):
            issues.append(ValidationIssue(path, "keys must be strings"))
            continue
        keys.add(key)
    for key in sorted(keys - allowed):
        issues.append(ValidationIssue(f"{path}.{key}", "is not allowed"))
    for key in sorted(allowed - keys):
        issues.append(ValidationIssue(f"{path}.{key}", "is required"))


def _validate_object(
    value: object,
    allowed: frozenset[str],
    path: str,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(value, Mapping):
        issues.append(ValidationIssue(path, "must be an object"))
        return
    _validate_root_keys(value, allowed, path, issues)


def _validate_diagnostic_array(
    value: object, path: str, issues: list[ValidationIssue]
) -> None:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        issues.append(ValidationIssue(path, "must be an array"))
        return
    previous: str | None = None
    for index, item in enumerate(value):
        item_path = f"{path}[{index}]"
        if not isinstance(item, Mapping):
            issues.append(ValidationIssue(item_path, "must be an object"))
            continue
        _validate_diagnostic(item, item_path, issues)
        diagnostic_id = item.get("diagnostic-id")
        if isinstance(diagnostic_id, str):
            if previous is not None and previous > diagnostic_id:
                issues.append(ValidationIssue(path, "must be sorted"))
            previous = diagnostic_id


def _validate_diagnostic(  # noqa: C901
    diagnostic: Mapping[str, object], path: str, issues: list[ValidationIssue]
) -> None:
    _validate_root_keys(diagnostic, _DIAGNOSTIC_KEYS, path, issues)
    diagnostic_id = diagnostic.get("diagnostic-id")
    if not isinstance(diagnostic_id, str) or diagnostic_id == "":
        issues.append(
            ValidationIssue(f"{path}.diagnostic-id", "must be a string")
        )
    code = diagnostic.get("code")
    if code not in REGISTERED_CI_VALIDATION_DIAGNOSTIC_CODES:
        issues.append(ValidationIssue(f"{path}.code", "is not registered"))
    detail = diagnostic.get("detail")
    if detail is not None:
        if detail not in REGISTERED_CI_VALIDATION_DIAGNOSTIC_DETAILS:
            issues.append(
                ValidationIssue(f"{path}.detail", "is not registered")
            )
        elif isinstance(
            code, str
        ) and detail not in DETAILS_BY_DIAGNOSTIC_CODE.get(code, frozenset()):
            issues.append(
                ValidationIssue(
                    f"{path}.detail", "is not valid for this diagnostic code"
                )
            )
    message = diagnostic.get("message")
    if message is not None and (not isinstance(message, str) or message == ""):
        issues.append(
            ValidationIssue(f"{path}.message", "must be null or non-empty")
        )
    source = diagnostic.get("source")
    if not isinstance(source, Mapping):
        issues.append(ValidationIssue(f"{path}.source", "must be an object"))
    else:
        _validate_root_keys(
            source, _DIAGNOSTIC_SOURCE_KEYS, f"{path}.source", issues
        )
        if source.get("type") not in {
            "aggregation",
            "work-group",
            "request",
            "impact",
            "subject",
            "descriptor",
            "fact-provider",
        }:
            issues.append(
                ValidationIssue(f"{path}.source.type", "is not registered")
            )
        if source.get("id") is not None and (
            not isinstance(source.get("id"), str) or source.get("id") == ""
        ):
            issues.append(
                ValidationIssue(
                    f"{path}.source.id", "must be null or non-empty"
                )
            )
    if diagnostic.get("severity") not in {
        item.value for item in DiagnosticSeverity.__members__.values()
    }:
        issues.append(ValidationIssue(f"{path}.severity", "is not registered"))
    if diagnostic.get("verdict-effect") not in {
        item.value for item in DiagnosticVerdictEffect.__members__.values()
    }:
        issues.append(
            ValidationIssue(f"{path}.verdict-effect", "is not registered")
        )


def _validate_root_diagnostics_cover_references(  # noqa: C901,PLR0912
    root: object,
    results: object,
    observed: object,
    failures: object,
    issues: list[ValidationIssue],
) -> None:
    if not isinstance(root, Sequence) or isinstance(root, str | bytes):
        return
    root_by_id: dict[str, Mapping[str, object]] = {}
    for diagnostic in root:
        if not isinstance(diagnostic, Mapping):
            continue
        diagnostic_id = diagnostic.get("diagnostic-id")
        if not isinstance(diagnostic_id, str):
            continue
        if diagnostic_id in root_by_id:
            issues.append(
                ValidationIssue(
                    "$.diagnostics", "must deduplicate diagnostic-id"
                )
            )
        root_by_id[diagnostic_id] = diagnostic
    referenced_by_id: dict[str, Mapping[str, object]] = {}
    for diagnostic in _referenced_diagnostics(results, observed, failures):
        diagnostic_id = diagnostic.get("diagnostic-id")
        if not isinstance(diagnostic_id, str):
            continue
        existing = referenced_by_id.get(diagnostic_id)
        if existing is not None and dict(existing) != dict(diagnostic):
            issues.append(
                ValidationIssue(
                    "$.diagnostics",
                    "referenced diagnostics must be deduplicated by content",
                )
            )
            continue
        referenced_by_id[diagnostic_id] = diagnostic
    for diagnostic_id, diagnostic in referenced_by_id.items():
        root_diagnostic = root_by_id.get(diagnostic_id)
        if root_diagnostic is None:
            issues.append(
                ValidationIssue(
                    "$.diagnostics", "must include referenced diagnostics"
                )
            )
        elif dict(root_diagnostic) != dict(diagnostic):
            issues.append(
                ValidationIssue(
                    "$.diagnostics", "must match referenced diagnostics"
                )
            )
    for diagnostic_id in root_by_id:
        if diagnostic_id not in referenced_by_id:
            issues.append(
                ValidationIssue(
                    "$.diagnostics", "must only include referenced diagnostics"
                )
            )


def _referenced_diagnostics(
    results: object, observed: object, failures: object
) -> list[Mapping[str, object]]:
    diagnostics: list[Mapping[str, object]] = []
    for collection in (results, observed):
        if not isinstance(collection, Sequence) or isinstance(
            collection, str | bytes
        ):
            continue
        for item in collection:
            if isinstance(item, Mapping):
                diagnostics.extend(
                    item
                    for item in _diagnostics(item.get("diagnostics"))
                    if isinstance(item, Mapping)
                )
    if isinstance(failures, Sequence) and not isinstance(failures, str | bytes):
        for failure in failures:
            if not isinstance(failure, Mapping):
                continue
            diagnostic = failure.get("diagnostic")
            if isinstance(diagnostic, Mapping):
                diagnostics.append(diagnostic)
    return diagnostics


def _validate_sorted_diagnostics(
    value: object, path: str, issues: list[ValidationIssue]
) -> None:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        return
    ids = [
        item_id
        for item in value
        if isinstance(item, Mapping)
        for item_id in [item.get("diagnostic-id")]
    ]
    if any(not isinstance(item_id, str) for item_id in ids):
        return
    sorted_ids = [item_id for item_id in ids if isinstance(item_id, str)]
    if sorted_ids != sorted(sorted_ids):
        issues.append(ValidationIssue(path, "must be sorted by diagnostic-id"))


def _validate_sorted_evidence_results(
    value: object, issues: list[ValidationIssue]
) -> None:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        return
    ids = [
        item_id
        for item in value
        if isinstance(item, Mapping)
        for item_id in [item.get("evidence-expectation-id")]
    ]
    if any(not isinstance(item_id, str) for item_id in ids):
        return
    sorted_ids = [item_id for item_id in ids if isinstance(item_id, str)]
    if sorted_ids != sorted(sorted_ids):
        issues.append(ValidationIssue("$.evidence-results", "must be sorted"))


def _validate_sorted_failures(
    value: object, issues: list[ValidationIssue]
) -> None:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        return
    keys = [
        _failure_sort_key(item) for item in value if isinstance(item, Mapping)
    ]
    if keys != sorted(keys):
        issues.append(ValidationIssue("$.failures", "must be sorted"))


def _sort_diagnostics(
    diagnostics: Sequence[Mapping[str, object]],
) -> list[dict[str, object]]:
    return sorted(
        (dict(item) for item in diagnostics),
        key=lambda item: str(item["diagnostic-id"]),
    )


def _sort_observed_receipts(
    receipts: Sequence[Mapping[str, object]],
) -> list[dict[str, object]]:
    result = []
    for item in receipts:
        copied = dict(item)
        copied["diagnostics"] = _sort_diagnostics(
            _diagnostics(copied.get("diagnostics"))
        )
        result.append(copied)
    return sorted(result, key=lambda item: str(item["observed-entry-id"]))


def _sort_evidence_results(
    results: Sequence[Mapping[str, object]],
) -> list[dict[str, object]]:
    result = []
    for item in results:
        copied = dict(item)
        copied["diagnostics"] = _sort_diagnostics(
            _diagnostics(copied.get("diagnostics"))
        )
        result.append(copied)
    return sorted(result, key=lambda item: str(item["evidence-expectation-id"]))


def _sort_failures(
    failures: Sequence[Mapping[str, object]],
) -> list[dict[str, object]]:
    return sorted((dict(item) for item in failures), key=_failure_sort_key)


def _failure_sort_key(
    item: Mapping[str, object],
) -> tuple[str, str, str, str, str]:
    diagnostic = item.get("diagnostic")
    diagnostic_id = ""
    if isinstance(diagnostic, Mapping) and isinstance(
        diagnostic.get("diagnostic-id"), str
    ):
        diagnostic_id = cast("str", diagnostic["diagnostic-id"])
    return (
        str(item.get("kind")),
        _sort_nullable(item.get("work-group-id")),
        _sort_nullable(item.get("evidence-expectation-id")),
        _sort_nullable(item.get("observed-entry-id")),
        diagnostic_id,
    )


def _sort_nullable(value: object) -> str:
    return "" if value is None else str(value)


def _diagnostics(value: object) -> list[dict[str, object]]:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        return []
    return [dict(item) for item in value if isinstance(item, Mapping)]


def _validate_non_empty_string(
    value: object, path: str, issues: list[ValidationIssue]
) -> None:
    if not isinstance(value, str) or value == "":
        issues.append(ValidationIssue(path, "must be a string"))


def _validate_nullable_non_empty_string(
    value: object, path: str, issues: list[ValidationIssue]
) -> None:
    if value is not None and (not isinstance(value, str) or value == ""):
        issues.append(ValidationIssue(path, "must be null or non-empty string"))


def _validate_local_id(
    value: object, path: str, issues: list[ValidationIssue]
) -> None:
    if not isinstance(value, str) or _LOCAL_ID_RE.fullmatch(value) is None:
        issues.append(ValidationIssue(path, "must be path-safe"))


def _validate_nullable_local_id(
    value: object, path: str, issues: list[ValidationIssue]
) -> None:
    if value is not None:
        _validate_local_id(value, path, issues)


def _validate_nullable_digest(
    value: object, path: str, issues: list[ValidationIssue]
) -> None:
    if value is not None and (
        not isinstance(value, str) or _DIGEST_RE.fullmatch(value) is None
    ):
        issues.append(ValidationIssue(path, "must be null or a SHA-256 digest"))


def _validate_nullable_writer_id(
    value: object, path: str, issues: list[ValidationIssue]
) -> None:
    if value is not None and (
        not isinstance(value, str) or _WRITER_ID_RE.fullmatch(value) is None
    ):
        issues.append(
            ValidationIssue(
                path,
                "must be null or github-actions-job: followed by a "
                "SHA-256 digest",
            )
        )


def _validate_artifact_ref(
    value: object, path: str, issues: list[ValidationIssue]
) -> None:
    if not isinstance(value, str):
        issues.append(ValidationIssue(path, "must be a string"))
        return
    try:
        validate_artifact_logical_ref(value)
    except ContractValidationError as error:
        issues.extend(
            ValidationIssue(path, issue.message) for issue in error.issues
        )


def _validate_nullable_artifact_ref(
    value: object, path: str, issues: list[ValidationIssue]
) -> None:
    if value is not None:
        _validate_artifact_ref(value, path, issues)


def _nullable_str(value: object) -> str | None:
    return value if isinstance(value, str) else None
