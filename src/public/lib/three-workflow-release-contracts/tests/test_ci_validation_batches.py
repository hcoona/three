"""Execution-batch CI validation contract tests."""

from __future__ import annotations

import hashlib
import importlib.util
from collections.abc import Mapping, Sequence
from copy import deepcopy
from pathlib import Path
from types import ModuleType
from typing import TYPE_CHECKING, Any, TypedDict, cast

import pytest
from three_workflow_release_contracts import (
    API_VERSIONS_BY_KIND,
    DETAILS_BY_DIAGNOSTIC_CODE,
    CiValidationKind,
    ContractValidationError,
    artifact_physical_name,
    canonical_json_bytes,
    canonical_json_digest,
    ci_validation_aggregate_evidence_manifest_artifact_ref,
    ci_validation_aggregate_evidence_manifest_payload_digest,
    ci_validation_aggregate_summary_artifact_ref,
    ci_validation_aggregate_summary_payload_digest,
    ci_validation_batch_evidence_bundle_artifact_ref,
    ci_validation_batch_evidence_bundle_id,
    ci_validation_batch_evidence_bundle_payload_digest,
    ci_validation_batch_evidence_candidate_id,
    ci_validation_changed_files_hash,
    ci_validation_changed_files_snapshot_artifact_ref,
    ci_validation_execution_batch_manifest_artifact_ref,
    ci_validation_execution_batch_manifest_payload_digest,
    ci_validation_execution_batch_matrix,
    ci_validation_fact_snapshot_artifact_ref,
    ci_validation_fact_snapshot_id,
    ci_validation_plan_artifact_ref,
    ci_validation_plan_digest,
    ci_validation_request_artifact_ref,
    ci_validation_request_projection,
    ci_validation_subject_universe_id,
    ci_validation_writer_id,
    freeze_ci_validation_aggregate_evidence_manifest,
    freeze_ci_validation_aggregate_summary,
    freeze_ci_validation_batch_evidence_bundle,
    freeze_ci_validation_execution_batch_manifest,
    materialize_ci_validation_execution_batches,
    validate_ci_validation_aggregate_evidence_manifest,
    validate_ci_validation_aggregate_summary,
    validate_ci_validation_batch_evidence_bundle,
    validate_ci_validation_diagnostic_record,
    validate_ci_validation_execution_batch_manifest,
)
from three_workflow_release_contracts.ci_validation_batches import (
    _envelope,
    _execution_batch_matrix_identity,
    _freeze_ci_validation_aggregate_evidence_manifest,
    _materializer_artifact_obligations_by_work_group,
    _materializer_batch_id,
    _materializer_compatibility_key,
    _materializer_compatibility_profile,
    _validate_admitted_bundles_topologically,
    _validate_ci_validation_aggregate_evidence_manifest,
    _validate_ci_validation_execution_batch_manifest,
)

if TYPE_CHECKING:
    from collections.abc import Callable, Mapping, Sequence

_PLANS_TEST_PATH = Path(__file__).with_name("test_ci_validation_plans.py")
_PLANS_SPEC = importlib.util.spec_from_file_location(
    "test_ci_validation_plans",
    _PLANS_TEST_PATH,
)
assert _PLANS_SPEC is not None
assert _PLANS_SPEC.loader is not None
_PLANS_MODULE = importlib.util.module_from_spec(_PLANS_SPEC)
assert isinstance(_PLANS_MODULE, ModuleType)
_PLANS_SPEC.loader.exec_module(_PLANS_MODULE)

EXPECTED_FINAL_ARTIFACTS = 2
CREATED_AT = cast("str", _PLANS_MODULE.CREATED_AT)
RUN_ATTEMPT = cast("str", _PLANS_MODULE.RUN_ATTEMPT)
RUN_ID = cast("str", _PLANS_MODULE.RUN_ID)
TREE_SHA = cast("str", _PLANS_MODULE.TREE_SHA)
_BATCH_ID = (
    "batch-exec-ecosystem-gate-python-python-"
    "9e950ffb771ae95b7919ec4083bf6258d4dc6f96bdefff99ca4089e3efa74774"
)
_plan_snapshot = _PLANS_MODULE.__dict__["_plan_snapshot"]
_scheduled_full_plan_snapshot = _PLANS_MODULE.__dict__[
    "_scheduled_full_plan_snapshot"
]
_scheduled_full_request = _PLANS_MODULE.__dict__["_scheduled_full_request"]
_OBSOLETE_FINAL_EVIDENCE_DETAILS = (
    "final-manifest-missing",
    "final-manifest-duplicate",
    "final-manifest-unreadable",
    "final-manifest-malformed",
    "final-manifest-non-canonical",
    "final-manifest-digest-mismatch",
    "final-aggregate-missing",
    "final-aggregate-duplicate",
    "final-aggregate-unreadable",
    "final-aggregate-malformed",
    "final-aggregate-non-canonical",
    "final-aggregate-digest-mismatch",
)


class _AuthorizingContextKwargs(TypedDict):
    request: dict[str, object]
    changed_files_snapshot: dict[str, object]
    fact_snapshot: dict[str, object]
    expected_run_id: str
    expected_run_attempt: str


def _diagnostic(  # noqa: PLR0913
    diagnostic_id: str,
    *,
    code: str | None = None,
    detail: str | None = None,
    message: str | None = None,
    severity: str = "blocking-failure",
    verdict_effect: str = "failed",
) -> dict[str, object]:
    diagnostic_code = code or diagnostic_id
    return {
        "diagnostic-id": diagnostic_id,
        "code": diagnostic_code,
        "detail": detail or diagnostic_code,
        "message": diagnostic_id if message is None else message,
        "source": {"type": "aggregation", "id": None},
        "severity": severity,
        "verdict-effect": verdict_effect,
    }


def _schema_diagnostic(diagnostic_id: str, *, detail: str) -> dict[str, object]:
    return _diagnostic(
        diagnostic_id,
        code="final-evidence-failure",
        detail=detail,
        severity="warning",
        verdict_effect="none",
    )


def _plan() -> dict[str, object]:
    snapshot = _plan_snapshot()
    return cast("dict[str, object]", snapshot.plan)


def _request_document() -> dict[str, object]:
    request_factory = cast(
        "Callable[[], dict[str, object]]",
        _PLANS_MODULE.__dict__["_request"],
    )
    return request_factory()


def _changed_files_snapshot_document() -> dict[str, object]:
    snapshot = _plan_snapshot()
    changed_files_snapshot = snapshot.changed_files_snapshot
    assert changed_files_snapshot is not None
    return cast("dict[str, object]", deepcopy(changed_files_snapshot))


def _fact_snapshot_document() -> dict[str, object]:
    snapshot = _plan_snapshot()
    fact_snapshot = snapshot.fact_snapshot
    assert fact_snapshot is not None
    return cast("dict[str, object]", deepcopy(fact_snapshot))


def _authorizing_context_kwargs() -> _AuthorizingContextKwargs:
    return {
        "request": _request_document(),
        "changed_files_snapshot": _changed_files_snapshot_document(),
        "fact_snapshot": _fact_snapshot_document(),
        "expected_run_id": RUN_ID,
        "expected_run_attempt": RUN_ATTEMPT,
    }


def _release_plan_snapshot():
    return _PLANS_MODULE.__dict__["freeze_ci_validation_plan"](
        request=_PLANS_MODULE.__dict__["_normalized_request"](),
        plan_id=cast("str", _PLANS_MODULE.__dict__["PLAN_ID"]),
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_PLANS_MODULE.__dict__["_classification"](),
        subjects=[_PLANS_MODULE.__dict__["_descriptor_backed_subject"]()],
        validation_obligations=[
            _PLANS_MODULE.__dict__["_artifact_validation_obligation"](),
            _PLANS_MODULE.__dict__["_validation_obligation"](),
        ],
        descriptor_obligations=[
            _PLANS_MODULE.__dict__["_descriptor_obligation"]()
        ],
        artifact_obligations=[_PLANS_MODULE.__dict__["_artifact_obligation"]()],
        work_groups=[
            _PLANS_MODULE.__dict__["_artifact_work_group"](),
            _PLANS_MODULE.__dict__["_descriptor_work_group"](),
            _PLANS_MODULE.__dict__["_ecosystem_gate_work_group"](),
        ],
        evidence_expectations=[
            _PLANS_MODULE.__dict__["_artifact_evidence_expectation"](),
            _PLANS_MODULE.__dict__["_descriptor_evidence_expectation"](),
            _PLANS_MODULE.__dict__["_evidence_expectation"](),
        ],
        fact_snapshot_providers=[
            _PLANS_MODULE.__dict__["_descriptor_fact_provider"]()
        ],
    )


def _release_plan_and_manifest() -> tuple[dict[str, object], dict[str, object]]:
    snapshot = _release_plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    materialization = materialize_ci_validation_execution_batches(
        plan=plan,
        request=_request_document(),
        changed_files_snapshot=cast(
            "dict[str, object]", snapshot.changed_files_snapshot
        ),
        fact_snapshot=cast("dict[str, object]", snapshot.fact_snapshot),
        created_at=CREATED_AT,
        execution_workflow="CI Validation",
        expected_run_id=RUN_ID,
        expected_run_attempt=RUN_ATTEMPT,
    )
    return plan, cast("dict[str, object]", materialization.manifest)


def _compatibility_profile() -> dict[str, object]:
    return {
        "ecosystem": "python",
        "setup-profile": "setup-ubuntu-python",
        "setup-profile-digest": (
            "86c40df02f2fa25adfc52b9e71c1d9dc00cbefcdc83c825d0ebadc69ecdf1a2c"
        ),
        "execution-profile": "exec-ecosystem-gate-python",
        "execution-profile-digest": (
            "acd7fc3c7f59881b60b25f216eb7ff93e22362ba23da443c9b4687dc0338d862"
        ),
        "release-shaped-profile": None,
        "release-shaped-profile-digest": None,
    }


def _batch(plan: dict[str, object]) -> dict[str, object]:
    batch_id = (
        "batch-exec-ecosystem-gate-python-python-"
        "9e950ffb771ae95b7919ec4083bf6258d4dc6f96bdefff99ca4089e3efa74774"
    )
    work_group = next(
        item
        for item in cast("list[dict[str, object]]", plan["work-groups"])
        if item["kind"] != "evidence-aggregation"
    )
    evidence = cast("list[dict[str, object]]", plan["evidence-expectations"])[0]
    slot = {
        "coverage-target": work_group["coverage-target"],
        "ecosystem": work_group["ecosystem"],
        "runner-family": work_group["runner-family"],
        "selector-variant": work_group["selector-variant"],
        "evidence": {
            "category": evidence["category"],
            "planned-capabilities": evidence["planned-capabilities"],
            "detail-profile": evidence.get("detail-profile"),
        },
    }
    bundle_ref = ci_validation_batch_evidence_bundle_artifact_ref(
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        batch_id=batch_id,
    )
    matrix_identity = {
        "batch-id": batch_id,
        "runner-family": "ubuntu",
        "expected-batch-evidence-bundle-ref": bundle_ref,
    }
    return {
        "batch-id": batch_id,
        "runner-family": "ubuntu",
        "compatibility-profile": _compatibility_profile(),
        "depends-on-batches": [],
        "ordered-selectors": [
            {
                "work-group-id": work_group["work-group-id"],
                "selector-index": 0,
                "depends-on": work_group["depends-on"],
                "expected-evidence-id": evidence["evidence-expectation-id"],
                "expected-evidence-slot": slot,
            },
        ],
        "expected-batch-evidence-bundle-ref": bundle_ref,
        "batch-writer": {
            "identity-source": "github-actions-job-context",
            "expected-boundary": "execution-batch",
            "expected-job-identity": ci_validation_writer_id(
                workflow="CI Validation",
                job="execution-batch",
                matrix=matrix_identity,
            ),
            "provenance-fields": ["workflow", "job", "matrix"],
        },
    }


def _budget(batch_count: int, *, input_count: int = 5) -> dict[str, object]:
    return {
        "min-total-jobs": batch_count,
        "max-total-jobs": 18,
        "min-windows-jobs": 0,
        "max-windows-jobs": 8,
        "non-batch-control-plane-job-count": 0,
        "actual-total-jobs": batch_count,
        "actual-windows-jobs": 0,
        "max-validation-artifacts": 20,
        "actual-validation-artifacts": input_count + batch_count + 2,
        "expected-input-non-bundle-validation-artifacts": input_count,
        "expected-final-validation-artifacts": 2,
        "expected-non-bundle-validation-artifacts": input_count + 2,
        "pre-final-validation-artifacts": input_count + batch_count,
        "max-execution-batches": 13,
        "actual-execution-batches": batch_count,
        "aggregate-target-duration-seconds": 60,
        "aggregate-max-duration-seconds": 120,
    }


def _manifest(
    plan: dict[str, object],
    *,
    authorizing: bool = True,
) -> dict[str, object]:
    affected_range = cast("dict[str, object]", plan["affected-range"])
    fact_snapshot = cast("dict[str, object]", plan["fact-snapshot"])
    input_count = 3
    if affected_range.get("changed-files-hash") is not None:
        input_count += 1
    if fact_snapshot.get("status") == "available":
        input_count += 1
    if authorizing:
        return freeze_ci_validation_execution_batch_manifest(
            plan=plan,
            **_authorizing_context_kwargs(),
            batches=[_batch(plan)],
            budget=_budget(1, input_count=input_count),
            created_at=CREATED_AT,
        )
    return freeze_ci_validation_execution_batch_manifest(
        plan=plan,
        batches=[_batch(plan)],
        budget=_budget(1, input_count=input_count),
        created_at=CREATED_AT,
        authorizing=False,
    )


def _add_dependent_work_group(plan: dict[str, object]) -> None:
    work_groups = cast("list[dict[str, object]]", plan["work-groups"])
    evidence_expectations = cast(
        "list[dict[str, object]]", plan["evidence-expectations"]
    )
    validation_obligations = cast(
        "list[dict[str, object]]", plan["validation-obligations"]
    )
    base_group = next(
        group
        for group in work_groups
        if group["kind"] != "evidence-aggregation"
    )
    base_group["selector-variant"] = "base"
    dependent_group = cast("dict[str, object]", deepcopy(base_group))
    dependent_group["work-group-id"] = "wg-dependent-gate"
    dependent_group["depends-on"] = [base_group["work-group-id"]]
    dependent_group["ecosystem"] = "javascript"
    dependent_group["selector-variant"] = "dependent"
    work_groups.insert(-1, dependent_group)
    terminal_group = next(
        group
        for group in work_groups
        if group["kind"] == "evidence-aggregation"
    )
    base_group_id = cast("str", base_group["work-group-id"])
    dependent_group_id = cast("str", dependent_group["work-group-id"])
    terminal_group["depends-on"] = sorted([base_group_id, dependent_group_id])
    dependent_evidence = cast(
        "dict[str, object]", deepcopy(evidence_expectations[0])
    )
    dependent_evidence["evidence-expectation-id"] = "evidence-dependent-gate"
    dependent_evidence["work-group-id"] = "wg-dependent-gate"
    evidence_expectations.append(dependent_evidence)
    evidence_expectations.sort(
        key=lambda item: str(item["evidence-expectation-id"])
    )
    dependent_validation = cast(
        "dict[str, object]", deepcopy(validation_obligations[0])
    )
    dependent_validation["validation-obligation-id"] = (
        "validation-dependent-gate"
    )
    dependent_validation["expected-evidence-id"] = "evidence-dependent-gate"
    dependent_validation["work-group-id"] = "wg-dependent-gate"
    validation_obligations.append(dependent_validation)
    validation_obligations.sort(
        key=lambda item: str(item["validation-obligation-id"])
    )
    plan["plan-digest"] = ci_validation_plan_digest(plan)


def _add_transitive_work_group(plan: dict[str, object]) -> None:
    _add_dependent_work_group(plan)
    work_groups = cast("list[dict[str, object]]", plan["work-groups"])
    evidence_expectations = cast(
        "list[dict[str, object]]", plan["evidence-expectations"]
    )
    validation_obligations = cast(
        "list[dict[str, object]]", plan["validation-obligations"]
    )
    dependent_group = next(
        group
        for group in work_groups
        if group["work-group-id"] == "wg-dependent-gate"
    )
    terminal_group = next(
        group
        for group in work_groups
        if group["kind"] == "evidence-aggregation"
    )
    transitive_group = cast("dict[str, object]", deepcopy(dependent_group))
    transitive_group["work-group-id"] = "wg-transitive-gate"
    transitive_group["depends-on"] = ["wg-dependent-gate"]
    transitive_group["ecosystem"] = "ruby"
    transitive_group["selector-variant"] = "transitive"
    work_groups.insert(-1, transitive_group)
    terminal_group["depends-on"] = sorted(
        ["wg-python-gate", "wg-dependent-gate", "wg-transitive-gate"]
    )
    transitive_evidence = cast(
        "dict[str, object]", deepcopy(evidence_expectations[0])
    )
    transitive_evidence["evidence-expectation-id"] = "evidence-transitive-gate"
    transitive_evidence["work-group-id"] = "wg-transitive-gate"
    evidence_expectations.append(transitive_evidence)
    evidence_expectations.sort(
        key=lambda item: str(item["evidence-expectation-id"])
    )
    transitive_validation = cast(
        "dict[str, object]", deepcopy(validation_obligations[0])
    )
    transitive_validation["validation-obligation-id"] = (
        "validation-transitive-gate"
    )
    transitive_validation["expected-evidence-id"] = "evidence-transitive-gate"
    transitive_validation["work-group-id"] = "wg-transitive-gate"
    validation_obligations.append(transitive_validation)
    validation_obligations.sort(
        key=lambda item: str(item["validation-obligation-id"])
    )
    work_groups.sort(key=lambda item: str(item["work-group-id"]))
    plan["plan-digest"] = ci_validation_plan_digest(plan)


def _add_extra_work_group(
    plan: dict[str, object],
    *,
    work_group_id: str,
    evidence_id: str,
) -> None:
    work_groups = cast("list[dict[str, object]]", plan["work-groups"])
    evidence_expectations = cast(
        "list[dict[str, object]]", plan["evidence-expectations"]
    )
    base_group = next(
        group
        for group in work_groups
        if group["kind"] != "evidence-aggregation"
    )
    extra_group = cast("dict[str, object]", deepcopy(base_group))
    extra_group["work-group-id"] = work_group_id
    extra_group["depends-on"] = []
    work_groups.insert(-1, extra_group)
    work_groups.sort(key=lambda item: str(item["work-group-id"]))
    extra_evidence = cast(
        "dict[str, object]", deepcopy(evidence_expectations[0])
    )
    extra_evidence["evidence-expectation-id"] = evidence_id
    extra_evidence["work-group-id"] = work_group_id
    evidence_expectations.append(extra_evidence)
    evidence_expectations.sort(
        key=lambda item: str(item["evidence-expectation-id"])
    )
    plan["plan-digest"] = ci_validation_plan_digest(plan)


def _retarget_batch(
    batch: dict[str, object],
    *,
    batch_id: str,
    work_group_id: str,
    evidence_id: str,
    plan: dict[str, object],
) -> dict[str, object]:
    retargeted = cast("dict[str, object]", deepcopy(batch))
    selector = cast("list[dict[str, object]]", retargeted["ordered-selectors"])[
        0
    ]
    group = next(
        item
        for item in cast("list[dict[str, object]]", plan["work-groups"])
        if item["work-group-id"] == work_group_id
    )
    evidence = next(
        item
        for item in cast(
            "list[dict[str, object]]", plan["evidence-expectations"]
        )
        if item["evidence-expectation-id"] == evidence_id
    )
    selector["work-group-id"] = work_group_id
    selector["depends-on"] = group["depends-on"]
    selector["expected-evidence-id"] = evidence_id
    cast("dict[str, object]", selector["expected-evidence-slot"])[
        "coverage-target"
    ] = group["coverage-target"]
    slot = cast("dict[str, object]", selector["expected-evidence-slot"])
    slot["ecosystem"] = group["ecosystem"]
    cast("dict[str, object]", selector["expected-evidence-slot"])[
        "selector-variant"
    ] = group["selector-variant"]
    cast("dict[str, object]", selector["expected-evidence-slot"])[
        "evidence"
    ] = {
        "category": evidence["category"],
        "planned-capabilities": evidence["planned-capabilities"],
        "detail-profile": evidence.get("detail-profile"),
    }
    artifacts = _materializer_artifact_obligations_by_work_group(plan)
    key = _materializer_compatibility_key(group, artifact_obligations=artifacts)
    groups = {
        cast("str", item["work-group-id"]): item
        for item in cast("list[dict[str, object]]", plan["work-groups"])
    }
    profile = _materializer_compatibility_profile(
        groups=groups,
        work_group_ids=[work_group_id],
        key_payload=key,
    )
    batch_id = _materializer_batch_id(
        profile=profile,
        work_group_ids=[work_group_id],
    )
    bundle_ref = ci_validation_batch_evidence_bundle_artifact_ref(
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        batch_id=batch_id,
    )
    retargeted["batch-id"] = batch_id
    retargeted["compatibility-profile"] = profile
    retargeted["expected-batch-evidence-bundle-ref"] = bundle_ref
    writer = cast("dict[str, object]", retargeted["batch-writer"])
    writer["expected-job-identity"] = ci_validation_writer_id(
        workflow="CI Validation",
        job="execution-batch",
        matrix={
            "batch-id": batch_id,
            "runner-family": retargeted["runner-family"],
            "expected-batch-evidence-bundle-ref": bundle_ref,
        },
    )
    return retargeted


def _retarget_plan_to_ruby(
    plan: dict[str, object],
    fact_snapshot: dict[str, object],
) -> None:
    subjects = cast("list[dict[str, object]]", plan["subjects"])
    work_groups = cast("list[dict[str, object]]", plan["work-groups"])
    evidence_expectations = cast(
        "list[dict[str, object]]", plan["evidence-expectations"]
    )
    validation_obligations = cast(
        "list[dict[str, object]]", plan["validation-obligations"]
    )
    ruby_subject_id = "ruby.src-public-lib-example"
    subject = subjects[0]
    subject["subject-id"] = ruby_subject_id
    subject["ecosystem"] = "ruby"
    cast("dict[str, object]", subject["capabilities"])["type-check"] = False
    cast("dict[str, object]", subject["inclusion"])["reason"] = "ruby workspace"
    cast("dict[str, object]", plan["subject-universe"])["id"] = (
        ci_validation_subject_universe_id(subjects)
    )
    for group in work_groups:
        if group.get("kind") != "evidence-aggregation":
            group["ecosystem"] = "ruby"
            cast("dict[str, object]", group["coverage-target"])["id"] = (
                ruby_subject_id
            )
            cast("dict[str, object]", group["expected-evidence"])[
                "planned-capabilities"
            ] = ["build", "test"]
    for expectation in evidence_expectations:
        cast("dict[str, object]", expectation["coverage-target"])["id"] = (
            ruby_subject_id
        )
        expectation["planned-capabilities"] = ["build", "test"]
    for obligation in validation_obligations:
        cast("dict[str, object]", obligation["coverage-target"])["id"] = (
            ruby_subject_id
        )
    classification = cast("dict[str, object]", plan["classification"])
    impact = cast("list[dict[str, object]]", classification["impacts"])[0]
    cast("dict[str, object]", impact["coverage-target"])["id"] = ruby_subject_id
    provenance = cast(
        "list[dict[str, object]]",
        classification["subject-selection-provenance"],
    )[0]
    provenance["subject-id"] = ruby_subject_id
    providers = cast("list[dict[str, object]]", fact_snapshot["providers"])
    provider = providers[0]
    provider["provider"] = "ruby"
    provider["provider-version"] = "bundler-workspace/v1"
    provider["subjects"] = [ruby_subject_id]
    fact_snapshot_id = ci_validation_fact_snapshot_id(providers)
    fact_snapshot["fact-snapshot-id"] = fact_snapshot_id
    cast("dict[str, object]", plan["fact-snapshot"])["id"] = fact_snapshot_id
    plan["plan-digest"] = ci_validation_plan_digest(plan)


def _dependent_batches(plan: dict[str, object]) -> list[dict[str, object]]:
    base = _batch(plan)
    first = _retarget_batch(
        base,
        batch_id="batch-base-gate",
        work_group_id="wg-python-gate",
        evidence_id="evidence-python-gate",
        plan=plan,
    )
    second = _retarget_batch(
        base,
        batch_id="batch-dependent-gate",
        work_group_id="wg-dependent-gate",
        evidence_id="evidence-dependent-gate",
        plan=plan,
    )
    second["depends-on-batches"] = [first["batch-id"]]
    return [first, second]


def _selector_result(
    plan: dict[str, object],
    manifest: dict[str, object],
    batch_id: str = _BATCH_ID,
) -> dict[str, object]:
    batch = next(
        item
        for item in cast("list[dict[str, object]]", manifest["batches"])
        if item["batch-id"] == batch_id
    )
    selector = cast("list[dict[str, object]]", batch["ordered-selectors"])[0]
    slot = cast("dict[str, object]", selector["expected-evidence-slot"])
    return {
        "work-group-id": selector["work-group-id"],
        "selector-index": selector["selector-index"],
        "expected-evidence-id": selector["expected-evidence-id"],
        "expected-evidence-slot-digest": canonical_json_digest(slot),
        "mode": plan["mode"],
        "validation-tree": plan["validation-tree"],
        "affected-range": _summary_affected_range(plan),
        "scheduled-full": plan["scheduled-full"],
        "coverage-target": slot["coverage-target"],
        "ecosystem": slot["ecosystem"],
        "runner-family": slot["runner-family"],
        "selector-variant": slot["selector-variant"],
        "depends-on": selector["depends-on"],
        "dependency-results": [],
        "outcome": "success",
        "skip-reason": None,
        "evidence": {
            "category": "ecosystem-gate",
            "planned-capabilities": ["build", "test", "type-check"],
            "capability-results": [
                {
                    "capability": "build",
                    "outcome": "success",
                    "diagnostics": [],
                },
                {"capability": "test", "outcome": "success", "diagnostics": []},
                {
                    "capability": "type-check",
                    "outcome": "success",
                    "diagnostics": [],
                },
            ],
            "artifact-refs": [],
        },
        "diagnostics": [],
        "proof-admissibility": "validation-only",
    }


def _bundle(
    plan: dict[str, object],
    manifest: dict[str, object],
) -> dict[str, object]:
    batch = cast("list[dict[str, object]]", manifest["batches"])[0]
    return freeze_ci_validation_batch_evidence_bundle(
        plan=plan,
        execution_batch_manifest=manifest,
        batch_id=_BATCH_ID,
        selector_results=[_selector_result(plan, manifest)],
        writer=_writer_for_batch(manifest, cast("str", batch["batch-id"])),
        execution_tree={
            "observed-commit-sha": TREE_SHA,
            "source": "execution-batch-boundary",
            "verified": True,
        },
        started_at=CREATED_AT,
        completed_at=CREATED_AT,
        created_at=CREATED_AT,
        **_authorizing_context_kwargs(),
    )


def test_batch_bundle_freezer_accepts_g2_plan_manifest_context() -> None:
    """Plan and manifest context stays non-authorizing by default."""
    plan = _plan()
    manifest = _manifest(plan)
    batch = cast("list[dict[str, object]]", manifest["batches"])[0]

    bundle = freeze_ci_validation_batch_evidence_bundle(
        plan=plan,
        execution_batch_manifest=manifest,
        batch_id=cast("str", batch["batch-id"]),
        selector_results=[_selector_result(plan, manifest)],
        writer=_writer_for_batch(manifest, cast("str", batch["batch-id"])),
        execution_tree={
            "observed-commit-sha": TREE_SHA,
            "source": "execution-batch-boundary",
            "verified": True,
        },
        started_at=CREATED_AT,
        completed_at=CREATED_AT,
        created_at=CREATED_AT,
    )

    validate_ci_validation_batch_evidence_bundle(
        bundle,
        plan=plan,
        execution_batch_manifest=manifest,
    )


def test_batch_bundle_expected_run_does_not_authorize_context() -> None:
    """Expected-run binding is separate from request/snapshot authority."""
    plan = _plan()
    manifest = _manifest(plan)
    batch = cast("list[dict[str, object]]", manifest["batches"])[0]
    bundle = freeze_ci_validation_batch_evidence_bundle(
        plan=plan,
        execution_batch_manifest=manifest,
        batch_id=cast("str", batch["batch-id"]),
        selector_results=[_selector_result(plan, manifest)],
        writer=_writer_for_batch(manifest, cast("str", batch["batch-id"])),
        execution_tree={
            "observed-commit-sha": TREE_SHA,
            "source": "execution-batch-boundary",
            "verified": True,
        },
        started_at=CREATED_AT,
        completed_at=CREATED_AT,
        created_at=CREATED_AT,
        expected_run_id=RUN_ID,
        expected_run_attempt=RUN_ATTEMPT,
    )

    validate_ci_validation_batch_evidence_bundle(
        bundle,
        plan=plan,
        execution_batch_manifest=manifest,
        expected_run_id=RUN_ID,
        expected_run_attempt=RUN_ATTEMPT,
    )
    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_batch_evidence_bundle(
            bundle,
            plan=plan,
            execution_batch_manifest=manifest,
            expected_run_id="99999999999",
            expected_run_attempt=RUN_ATTEMPT,
        )

    assert any(issue.path == "$.run.run-id" for issue in error.value.issues)
    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_batch_evidence_bundle(
            bundle,
            plan=plan,
            execution_batch_manifest=manifest,
            expected_run_id=RUN_ID,
            expected_run_attempt="99",
        )

    assert any(
        issue.path == "$.run.run-attempt" for issue in error.value.issues
    )


def _writer_for_batch(
    manifest: dict[str, object],
    batch_id: str,
) -> dict[str, object]:
    batch = next(
        item
        for item in cast("list[dict[str, object]]", manifest["batches"])
        if item["batch-id"] == batch_id
    )
    batch_writer = cast("dict[str, object]", batch["batch-writer"])
    return {
        "identity-source": "github-actions-job-context",
        "expected-boundary": "execution-batch",
        "expected-job-identity": batch_writer["expected-job-identity"],
        "observed-workflow": "CI Validation",
        "observed-job": manifest["execution-job"],
        "observed-matrix": _execution_batch_matrix_identity(batch),
    }


def _input_artifact(
    artifact_ref: str | None,
    *,
    required: bool,
    admissibility: str,
) -> dict[str, object]:
    return {
        "artifact-ref": artifact_ref,
        "artifact-instance-id": "1001" if artifact_ref is not None else None,
        "content-digest": "4" * 64 if artifact_ref is not None else None,
        "required": required,
        "expected-cardinality": 1 if required else 0,
        "admissibility": admissibility,
        "diagnostics": [],
    }


def _aggregate_evidence_manifest(
    plan: dict[str, object],
    manifest: dict[str, object],
    bundle: dict[str, object],
) -> dict[str, object]:
    batch_ref = cast("str", bundle["artifact-ref"])
    batch_digest = ci_validation_batch_evidence_bundle_payload_digest(bundle)
    manifest_digest = ci_validation_execution_batch_manifest_payload_digest(
        manifest,
    )
    physical_name = artifact_physical_name(batch_ref)
    candidate_id = ci_validation_batch_evidence_candidate_id(
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        batch_id=_BATCH_ID,
        artifact_ref=batch_ref,
        artifact_instance_id="2001",
        physical_artifact_name=physical_name,
    )
    input_artifacts = {
        "request": _input_artifact(
            ci_validation_request_artifact_ref(
                run_id=RUN_ID,
                run_attempt=RUN_ATTEMPT,
            ),
            required=True,
            admissibility="valid",
        ),
        "validation-plan": _input_artifact(
            ci_validation_plan_artifact_ref(
                run_id=RUN_ID,
                run_attempt=RUN_ATTEMPT,
            ),
            required=True,
            admissibility="valid",
        ),
        "changed-files-snapshot": _input_artifact(
            ci_validation_changed_files_snapshot_artifact_ref(
                run_id=RUN_ID,
                run_attempt=RUN_ATTEMPT,
            ),
            required=True,
            admissibility="valid",
        ),
        "fact-snapshot": _input_artifact(
            ci_validation_fact_snapshot_artifact_ref(
                run_id=RUN_ID,
                run_attempt=RUN_ATTEMPT,
            ),
            required=True,
            admissibility="valid",
        ),
        "execution-batch-manifest": _input_artifact(
            ci_validation_execution_batch_manifest_artifact_ref(
                run_id=RUN_ID,
                run_attempt=RUN_ATTEMPT,
            ),
            required=True,
            admissibility="valid",
        ),
    }
    cast(
        "dict[str, object]",
        input_artifacts["execution-batch-manifest"],
    )["content-digest"] = manifest_digest
    cast("dict[str, object]", input_artifacts["request"])["content-digest"] = (
        cast(
            "dict[str, object]",
            plan["request"],
        )["request-digest"]
    )
    cast("dict[str, object]", input_artifacts["validation-plan"])[
        "content-digest"
    ] = plan["plan-digest"]
    affected_range = cast("dict[str, object]", plan["affected-range"])
    changed_files_hash = affected_range["changed-files-hash"]
    if isinstance(changed_files_hash, str) and changed_files_hash:
        cast("dict[str, object]", input_artifacts["changed-files-snapshot"])[
            "content-digest"
        ] = changed_files_hash
    elif changed_files_hash is None:
        input_artifacts["changed-files-snapshot"] = _input_artifact(
            None,
            required=False,
            admissibility="not-required",
        )
    else:
        input_artifacts["changed-files-snapshot"] = _input_artifact(
            None,
            required=False,
            admissibility="not-required",
        )
    cast("dict[str, object]", input_artifacts["fact-snapshot"])[
        "content-digest"
    ] = cast("dict[str, object]", plan["fact-snapshot"])["id"]
    fact_snapshot = _fact_snapshot_document()
    fact_snapshot["plan-id"] = plan["plan-id"]
    return freeze_ci_validation_aggregate_evidence_manifest(
        created_at=CREATED_AT,
        repository_owner="hcoona",
        repository_name="three",
        workflow="CI Validation",
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        input_artifacts=input_artifacts,
        batch_bundles=[
            {
                "batch-id": _BATCH_ID,
                "artifact-ref": batch_ref,
                "expected-cardinality": 1,
                "slot-admissibility": "valid",
                "admitted-candidate-id": candidate_id,
                "observed-candidates": [
                    {
                        "candidate-id": candidate_id,
                        "artifact-instance-id": "2001",
                        "content-digest": batch_digest,
                        "producer-verification": "verified",
                        "payload-readable": True,
                        "admissibility": "valid",
                        "diagnostics": [],
                    },
                ],
                "diagnostics": [],
            },
        ],
        unexpected_contract_artifacts=[],
        namespace_overflow={
            "detected": False,
            "observed-prefixed-artifact-count-lower-bound": 6,
            "max-prefixed-validation-artifacts": 18,
            "diagnostics": [],
        },
        pre_final_validation_artifacts=6,
        namespace_closed_at=CREATED_AT,
        plan=plan,
        execution_batch_manifest=manifest,
        request=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=fact_snapshot,
    )


def _summary_affected_range(plan: dict[str, object]) -> dict[str, object]:
    affected = cast("dict[str, object]", plan["affected-range"])
    return {
        "status": affected["status"],
        "base-sha": affected["base-sha"],
        "base-tip-sha": affected["base-tip-sha"],
        "head-sha": affected["head-sha"],
        "changed-files-hash": affected["changed-files-hash"] or None,
    }


def _projection_authority(plan: dict[str, object]) -> dict[str, object]:
    authority = {
        "mode": plan["mode"],
        "validation-tree": dict(
            cast("dict[str, object]", plan["validation-tree"])
        ),
        "affected-range": _summary_affected_range(plan),
        "request": dict(cast("dict[str, object]", plan["request"])),
        "scheduled-full": dict(
            cast("dict[str, object]", plan["scheduled-full"])
        ),
    }
    authority["projection-digest"] = canonical_json_digest(authority)
    return authority


def _refresh_projection_authority_digest(authority: dict[str, object]) -> None:
    authority["projection-digest"] = canonical_json_digest(
        {
            key: value
            for key, value in authority.items()
            if key != "projection-digest"
        }
    )


def _sort_component(value: object) -> str:
    if value is None:
        return ""
    return str(value)


def _summary_failure_sort_key(
    item: dict[str, object],
) -> tuple[str, str, str, str, str, str]:
    return (
        _sort_component(item.get("kind")),
        _sort_component(item.get("evidence-expectation-id")),
        _sort_component(item.get("work-group-id")),
        _sort_component(item.get("batch-id")),
        _sort_component(item.get("bundle-id")),
        canonical_json_digest(item),
    )


def _sort_summary_failures(summary: dict[str, object]) -> None:
    cast("list[dict[str, object]]", summary["failures"]).sort(
        key=_summary_failure_sort_key
    )


def _aggregate_summary(
    plan: dict[str, object],
    aggregate_manifest: dict[str, object],
    bundle: dict[str, object],
    execution_batch_manifest: dict[str, object] | None = None,
) -> dict[str, object]:
    manifest_ref = cast("str", aggregate_manifest["artifact-ref"])
    manifest_digest = ci_validation_aggregate_evidence_manifest_payload_digest(
        aggregate_manifest,
    )
    bundle_id = cast("str", bundle["bundle-id"])
    return freeze_ci_validation_aggregate_summary(
        created_at=CREATED_AT,
        repository_owner="hcoona",
        repository_name="three",
        workflow="CI Validation",
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        aggregate_evidence_manifest={
            "artifact-ref": manifest_ref,
            "artifact-instance-id": "3001",
            "content-digest": manifest_digest,
        },
        final_artifacts={
            "aggregate-evidence-manifest": {
                "artifact-ref": manifest_ref,
                "artifact-instance-id": "3001",
                "content-digest": manifest_digest,
                "producer-verified": True,
            },
            "aggregate-summary": {
                "artifact-ref": ci_validation_aggregate_summary_artifact_ref(
                    run_id=RUN_ID,
                    run_attempt=RUN_ATTEMPT,
                ),
            },
        },
        validation_tree=cast("dict[str, object]", plan["validation-tree"]),
        affected_range=_summary_affected_range(plan),
        request={
            "artifact-ref": ci_validation_request_artifact_ref(
                run_id=RUN_ID,
                run_attempt=RUN_ATTEMPT,
            ),
            "request-digest": cast("dict[str, object]", plan["request"])[
                "request-digest"
            ],
        },
        scheduled_full=cast("dict[str, object]", plan["scheduled-full"]),
        verdict="passed",
        reason={
            "invalid-plan": False,
            "fail-closed": False,
            "required-evidence-missing": False,
            "required-evidence-skipped": False,
            "blocking-validation-failure": False,
            "inadmissible-batch-evidence": False,
            "namespace-closure-failure": False,
            "aggregate-duration-exceeded": False,
            "final-evidence-failure": False,
        },
        budgets={
            "pre-final-validation-artifacts": 6,
            "expected-final-validation-artifacts": 2,
            "expected-actual-validation-artifacts": 8,
            "max-validation-artifacts": 20,
            "actual-execution-batches": 1,
            "actual-total-jobs": 1,
            "actual-windows-jobs": 0,
            "aggregate-duration-seconds": 10,
            "aggregate-target-duration-seconds": 60,
            "aggregate-max-duration-seconds": 120,
        },
        diagnostics=[],
        batch_bundles=[
            {
                "batch-id": _BATCH_ID,
                "artifact-ref": bundle["artifact-ref"],
                "bundle-id": bundle_id,
                "admitted-candidate-id": cast(
                    "list[dict[str, object]]",
                    aggregate_manifest["batch-bundles"],
                )[0]["admitted-candidate-id"],
                "candidate-count": 1,
                "admissibility": "valid",
                "diagnostics": [],
            },
        ],
        evidence_results=[
            {
                "evidence-expectation-id": "evidence-python-gate",
                "work-group-id": "wg-python-gate",
                "batch-id": _BATCH_ID,
                "bundle-id": bundle_id,
                "selector-index": 0,
                "outcome": "satisfied",
                "diagnostics": [],
            },
        ],
        failures=[],
        work_groups={
            "executable-required": 1,
            "required-succeeded": 1,
            "required-failed": 0,
            "required-skipped": 0,
            "required-missing": 0,
            "terminal-aggregation": "present",
        },
        plan=plan,
        aggregate_evidence_manifest_document=aggregate_manifest,
        admitted_batch_evidence_bundles=[bundle],
        execution_batch_manifest=(
            _manifest(plan)
            if execution_batch_manifest is None
            else execution_batch_manifest
        ),
        request_document=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )


def _set_input_absent(
    aggregate_manifest: dict[str, object],
    input_name: str,
    *,
    required: bool,
    admissibility: str,
) -> None:
    artifact = cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            input_name
        ],
    )
    artifact["artifact-ref"] = None
    artifact["artifact-instance-id"] = None
    artifact["content-digest"] = None
    artifact["required"] = required
    artifact["expected-cardinality"] = 1 if required else 0
    artifact["admissibility"] = admissibility


def _zero_batch_execution_manifest(
    manifest: dict[str, object],
) -> dict[str, object]:
    result = deepcopy(manifest)
    result["batches"] = []
    budget = cast("dict[str, object]", manifest["budget"])
    input_count = cast(
        "int",
        budget["expected-input-non-bundle-validation-artifacts"],
    )
    result["budget"] = _budget(0, input_count=input_count)
    return result


def _mark_aggregate_manifest_no_authority(
    aggregate_manifest: dict[str, object],
) -> None:
    aggregate_manifest["batch-bundles"] = []
    aggregate_manifest["projection-authority"] = None
    aggregate_manifest["pre-final-validation-artifacts"] = 5
    cast("dict[str, object]", aggregate_manifest["namespace-overflow"])[
        "observed-prefixed-artifact-count-lower-bound"
    ] = 5
    _set_input_absent(
        aggregate_manifest,
        "validation-plan",
        required=True,
        admissibility="missing",
    )


def _batch_bundle_slot(batch_id: str) -> dict[str, object]:
    artifact_ref = ci_validation_batch_evidence_bundle_artifact_ref(
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        batch_id=batch_id,
    )
    candidate_id = ci_validation_batch_evidence_candidate_id(
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        batch_id=batch_id,
        artifact_ref=artifact_ref,
        artifact_instance_id="2001",
        physical_artifact_name=artifact_physical_name(artifact_ref),
    )
    return {
        "batch-id": batch_id,
        "artifact-ref": artifact_ref,
        "expected-cardinality": 1,
        "slot-admissibility": "valid",
        "admitted-candidate-id": candidate_id,
        "observed-candidates": [
            {
                "candidate-id": candidate_id,
                "artifact-instance-id": "2001",
                "content-digest": "5" * 64,
                "producer-verification": "verified",
                "payload-readable": True,
                "admissibility": "valid",
                "diagnostics": [],
            }
        ],
        "diagnostics": [],
    }


def test_batch_bundle_rejects_planless_non_empty_execution_manifest() -> None:
    """Planless bundle validation cannot authorize non-empty batches."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_batch_evidence_bundle(
            bundle,
            execution_batch_manifest=manifest,
        )

    assert any(issue.path == "authorizing" for issue in exc_info.value.issues)


@pytest.mark.parametrize(
    "missing_key",
    [
        "request",
        "changed_files_snapshot",
        "fact_snapshot",
        "expected_run_id",
        "expected_run_attempt",
    ],
)
def test_public_batch_bundle_validator_requires_authorizing_context(
    missing_key: str,
) -> None:
    """Plan-bound bundle validation requires current-run authority inputs."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    kwargs = dict(_authorizing_context_kwargs())
    kwargs[missing_key] = None

    with pytest.raises(ContractValidationError):
        validate_ci_validation_batch_evidence_bundle(
            bundle,
            plan=plan,
            execution_batch_manifest=manifest,
            **kwargs,
        )


def test_public_batch_bundle_freezer_rejects_partial_authorizing_context() -> (
    None
):
    """Explicit authorizing bundle context must be complete."""
    plan = _plan()
    manifest = _manifest(plan)

    with pytest.raises(ContractValidationError):
        freeze_ci_validation_batch_evidence_bundle(
            plan=plan,
            execution_batch_manifest=manifest,
            batch_id=_BATCH_ID,
            selector_results=[_selector_result(plan, manifest)],
            writer=_writer_for_batch(manifest, _BATCH_ID),
            execution_tree={
                "observed-commit-sha": TREE_SHA,
                "source": "execution-batch-boundary",
                "verified": True,
            },
            started_at=CREATED_AT,
            completed_at=CREATED_AT,
            created_at=CREATED_AT,
            request=_request_document(),
        )


def test_summary_rejects_planless_non_empty_execution_manifest() -> None:
    """Planless summary validation cannot bind non-empty batch manifests."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle, manifest)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            execution_batch_manifest=manifest,
            _require_aggregate_evidence_manifest=False,
        )

    assert any(issue.path == "authorizing" for issue in exc_info.value.issues)


def test_public_aggregate_validator_rejects_bypass_kwargs() -> None:
    """Public aggregate validation does not expose internal bypass flags."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    validator = cast("Any", validate_ci_validation_aggregate_evidence_manifest)

    with pytest.raises(TypeError):
        validator(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
            _require_authoritative_snapshot_inputs=False,
        )


def test_public_aggregate_freezer_rejects_bypass_kwargs() -> None:
    """Public aggregate freezing does not expose internal bypass flags."""
    plan = _plan()
    manifest = _manifest(plan)
    freezer = cast("Any", freeze_ci_validation_aggregate_evidence_manifest)

    with pytest.raises(TypeError):
        freezer(
            created_at=CREATED_AT,
            repository_owner="hcoona",
            repository_name="three",
            workflow="CI Validation",
            run_id=RUN_ID,
            run_attempt=RUN_ATTEMPT,
            input_artifacts={},
            batch_bundles=[],
            unexpected_contract_artifacts=[],
            namespace_overflow={
                "detected": False,
                "observed-prefixed-artifact-count-lower-bound": 0,
                "max-prefixed-validation-artifacts": 18,
                "diagnostics": [],
            },
            pre_final_validation_artifacts=0,
            namespace_closed_at=CREATED_AT,
            plan=plan,
            execution_batch_manifest=manifest,
            _require_authoritative_snapshot_inputs=False,
        )


def test_no_authority_aggregate_rejects_stale_plan_identity() -> None:
    """No-authority aggregate manifests must not carry stale plan identity."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    _mark_aggregate_manifest_no_authority(aggregate_manifest)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            execution_batch_manifest={
                **_zero_batch_execution_manifest(manifest),
                "plan-id": None,
                "plan-digest": None,
            },
        )

    assert any(issue.path == "$.plan-id" for issue in exc_info.value.issues)
    assert any(issue.path == "$.plan-digest" for issue in exc_info.value.issues)


def test_no_authority_summary_rejects_stale_plan_identity() -> None:
    """No-authority aggregate summaries must not carry stale plan identity."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle, manifest)
    _mark_aggregate_manifest_no_authority(aggregate_manifest)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            aggregate_evidence_manifest=aggregate_manifest,
            execution_batch_manifest={
                **_zero_batch_execution_manifest(manifest),
                "plan-id": None,
                "plan-digest": None,
            },
        )

    assert any(issue.path == "$.plan-id" for issue in exc_info.value.issues)
    assert any(issue.path == "$.plan-digest" for issue in exc_info.value.issues)


def test_summary_without_authority_rejects_stale_plan_identity() -> None:
    """Summary-only validation cannot bind stale plan identity."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_missing_aggregate_manifest(summary)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            execution_batch_manifest={
                **_zero_batch_execution_manifest(manifest),
                "plan-id": None,
                "plan-digest": None,
            },
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
            _require_aggregate_evidence_manifest=False,
        )

    assert {
        "$.plan-id",
        "$.plan-digest",
    }.issubset({issue.path for issue in exc_info.value.issues})


def test_summary_freezer_without_authority_nulls_plan_identity() -> None:
    """Summary freezing cannot use stale execution manifest identity."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    template = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_missing_aggregate_manifest(template)

    summary = freeze_ci_validation_aggregate_summary(
        created_at=CREATED_AT,
        repository_owner="hcoona",
        repository_name="three",
        workflow="CI Validation",
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        aggregate_evidence_manifest=cast(
            "dict[str, object]",
            template["aggregate-evidence-manifest"],
        ),
        final_artifacts=cast("dict[str, object]", template["final-artifacts"]),
        validation_tree=cast("dict[str, object]", template["validation-tree"]),
        affected_range=cast("dict[str, object]", template["affected-range"]),
        request=cast("dict[str, object]", template["request"]),
        scheduled_full=cast("dict[str, object]", template["scheduled-full"]),
        verdict=cast("str", template["verdict"]),
        reason=cast("dict[str, object]", template["reason"]),
        budgets=cast("dict[str, object]", template["budgets"]),
        diagnostics=cast("list[dict[str, object]]", template["diagnostics"]),
        batch_bundles=cast(
            "list[dict[str, object]]",
            template["batch-bundles"],
        ),
        evidence_results=cast(
            "list[dict[str, object]]",
            template["evidence-results"],
        ),
        failures=cast("list[dict[str, object]]", template["failures"]),
        work_groups=cast("dict[str, object]", template["work-groups"]),
        execution_batch_manifest={
            **_zero_batch_execution_manifest(manifest),
            "plan-id": None,
            "plan-digest": None,
        },
        request_document=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )

    assert summary["plan-id"] is None
    assert summary["plan-digest"] is None


def _unexpected_artifact(index: int) -> dict[str, object]:
    return {
        "physical-artifact-name": f"three-ci-validation-{index:064x}",
        "artifact-instance-id": f"900{index}",
        "classification": "unexpected",
        "diagnostics": [],
    }


def _unexpected_artifact_sort_key(item: dict[str, object]) -> str:
    return canonical_json_digest(
        {
            "run-id": RUN_ID,
            "run-attempt": RUN_ATTEMPT,
            "physical-artifact-name": _sort_component(
                item.get("physical-artifact-name")
            ),
            "artifact-instance-id": _sort_component(
                item.get("artifact-instance-id")
            ),
            "classification": _sort_component(item.get("classification")),
        }
    )


def _sort_unexpected_artifacts(aggregate_manifest: dict[str, object]) -> None:
    cast(
        "list[dict[str, object]]",
        aggregate_manifest["unexpected-contract-artifacts"],
    ).sort(key=_unexpected_artifact_sort_key)


def _mark_summary_namespace_failure(summary: dict[str, object]) -> None:
    summary["verdict"] = "failed"
    reason = cast("dict[str, object]", summary["reason"])
    reason["fail-closed"] = True
    reason["namespace-closure-failure"] = True
    _append_summary_fail_closed_failure(
        summary,
        _diagnostic(
            "fail-closed/namespace-closure-failure",
            code="namespace-closure-failure",
            detail="unexpected-contract-artifact",
            severity="fail-closed",
            verdict_effect="fail-closed",
        ),
        "Validation artifact namespace forced fail-closed.",
    )
    cast("list[dict[str, object]]", summary["failures"]).append(
        {
            "kind": "namespace-closure-failure",
            "batch-id": None,
            "work-group-id": None,
            "evidence-expectation-id": None,
            "bundle-id": None,
            "diagnostic": _diagnostic(
                "namespace-closure-failure",
                detail="unexpected-contract-artifact",
                severity="fail-closed",
                verdict_effect="fail-closed",
            ),
            "message": "Validation artifact namespace was not closed.",
        }
    )
    _sort_summary_failures(summary)


def _refresh_summary_manifest_digest(
    summary: dict[str, object],
    aggregate_manifest: dict[str, object],
) -> None:
    digest = ci_validation_aggregate_evidence_manifest_payload_digest(
        aggregate_manifest
    )
    cast("dict[str, object]", summary["aggregate-evidence-manifest"])[
        "content-digest"
    ] = digest
    cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )["content-digest"] = digest


def _set_summary_unknown_projection(summary: dict[str, object]) -> None:
    summary["mode"] = "unknown"
    summary["validation-tree"] = {"commit-sha": None, "ref": None}
    summary["affected-range"] = {
        "status": "unknown",
        "base-sha": None,
        "base-tip-sha": None,
        "head-sha": None,
        "changed-files-hash": None,
    }
    summary["request"] = {"artifact-ref": None, "request-digest": None}
    summary["scheduled-full"] = {"enabled": False}


def _mark_summary_required_input_failure(summary: dict[str, object]) -> None:
    summary["verdict"] = "failed"
    reason = cast("dict[str, object]", summary["reason"])
    reason["fail-closed"] = True
    reason["final-evidence-failure"] = True
    _append_summary_fail_closed_failure(
        summary,
        _diagnostic(
            "fail-closed/required-input-artifact-failure",
            code="final-evidence-failure",
            detail="required-input-artifact-failure",
            severity="fail-closed",
            verdict_effect="fail-closed",
        ),
        "Required input artifact forced fail-closed.",
    )
    cast("list[dict[str, object]]", summary["failures"]).append(
        {
            "kind": "final-evidence-failure",
            "batch-id": None,
            "work-group-id": None,
            "evidence-expectation-id": None,
            "bundle-id": None,
            "diagnostic": _diagnostic(
                "required-input-artifact-failure",
                code="final-evidence-failure",
                detail="required-input-artifact-failure",
                severity="fail-closed",
                verdict_effect="fail-closed",
            ),
            "message": "Required input artifact was not valid.",
        }
    )
    _sort_summary_failures(summary)


def _mark_summary_duration_failure(summary: dict[str, object]) -> None:
    summary["verdict"] = "failed"
    cast("dict[str, object]", summary["budgets"])[
        "aggregate-duration-seconds"
    ] = 121
    reason = cast("dict[str, object]", summary["reason"])
    reason["aggregate-duration-exceeded"] = True
    reason["final-evidence-failure"] = True
    cast("list[dict[str, object]]", summary["failures"]).extend(
        [
            {
                "kind": "aggregate-duration-exceeded",
                "batch-id": None,
                "work-group-id": None,
                "evidence-expectation-id": None,
                "bundle-id": None,
                "diagnostic": _diagnostic("aggregate-duration-exceeded"),
                "message": "Aggregate duration exceeded the maximum budget.",
            },
            {
                "kind": "final-evidence-failure",
                "batch-id": None,
                "work-group-id": None,
                "evidence-expectation-id": None,
                "bundle-id": None,
                "diagnostic": _diagnostic(
                    "final-evidence-failure",
                    detail="aggregate-duration-exceeded",
                    severity="fail-closed",
                    verdict_effect="fail-closed",
                ),
                "message": "Aggregate duration exceeded the maximum budget.",
            },
        ]
    )
    _sort_summary_failures(summary)


def _mark_summary_invalid_plan(summary: dict[str, object]) -> None:
    summary["plan-id"] = None
    summary["plan-digest"] = None
    summary["mode"] = "unknown"
    summary["validation-tree"] = {"commit-sha": None, "ref": None}
    summary["affected-range"] = {
        "status": "unknown",
        "base-sha": None,
        "base-tip-sha": None,
        "head-sha": None,
        "changed-files-hash": None,
    }
    summary["request"] = {"artifact-ref": None, "request-digest": None}
    summary["scheduled-full"] = {"enabled": False}
    summary["verdict"] = "failed"
    reason = cast("dict[str, object]", summary["reason"])
    for key in reason:
        reason[key] = False
    reason["invalid-plan"] = True
    manifest_claim = cast(
        "dict[str, object]", summary["aggregate-evidence-manifest"]
    )
    manifest_claim["artifact-instance-id"] = None
    manifest_claim["content-digest"] = None
    final_manifest = cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )
    final_manifest["artifact-instance-id"] = None
    final_manifest["content-digest"] = None
    final_manifest["producer-verified"] = False
    cast("list[dict[str, object]]", summary["batch-bundles"]).clear()
    cast("list[dict[str, object]]", summary["evidence-results"]).clear()
    budgets = cast("dict[str, object]", summary["budgets"])
    budgets["actual-execution-batches"] = 0
    budgets["actual-total-jobs"] = 0
    budgets["actual-windows-jobs"] = 0
    work_groups = cast("dict[str, object]", summary["work-groups"])
    work_groups["executable-required"] = 0
    work_groups["required-succeeded"] = 0
    work_groups["required-failed"] = 0
    work_groups["required-skipped"] = 0
    work_groups["required-missing"] = 0
    cast("list[dict[str, object]]", summary["failures"]).append(
        {
            "kind": "invalid-plan",
            "batch-id": None,
            "work-group-id": None,
            "evidence-expectation-id": None,
            "bundle-id": None,
            "diagnostic": _diagnostic(
                "invalid-plan",
                detail="plan-missing",
                message="No authoritative validation plan was available.",
                severity="fail-closed",
                verdict_effect="fail-closed",
            ),
            "message": "No authoritative validation plan was available.",
        }
    )
    _sort_summary_failures(summary)


def _mark_summary_missing_evidence(summary: dict[str, object]) -> None:
    summary["verdict"] = "failed"
    reason = cast("dict[str, object]", summary["reason"])
    reason["required-evidence-missing"] = True
    reason["inadmissible-batch-evidence"] = True
    evidence_result = cast(
        "list[dict[str, object]]", summary["evidence-results"]
    )[0]
    evidence_result["batch-id"] = None
    evidence_result["bundle-id"] = None
    evidence_result["selector-index"] = None
    evidence_result["outcome"] = "missing"
    work_groups = cast("dict[str, object]", summary["work-groups"])
    work_groups["required-succeeded"] = 0
    work_groups["required-missing"] = 1
    _append_summary_failure(
        summary,
        kind="required-evidence-missing",
        diagnostic_id="required-evidence-missing",
        message="Required evidence was missing.",
        related_ids={
            "evidence-expectation-id": "evidence-python-gate",
            "work-group-id": "wg-python-gate",
        },
    )
    _append_summary_failure(
        summary,
        kind="inadmissible-batch-evidence",
        diagnostic_id="inadmissible-batch-evidence",
        message="Required batch evidence was not admissible.",
        related_ids={"batch-id": _BATCH_ID},
    )


def _mark_summary_skipped_evidence(summary: dict[str, object]) -> None:
    summary["verdict"] = "failed"
    reason = cast("dict[str, object]", summary["reason"])
    reason["required-evidence-skipped"] = True
    evidence_result = cast(
        "list[dict[str, object]]", summary["evidence-results"]
    )[0]
    evidence_result["outcome"] = "skipped"
    work_groups = cast("dict[str, object]", summary["work-groups"])
    work_groups["required-succeeded"] = 0
    work_groups["required-skipped"] = 1
    _append_outcome_failure_for_result(
        summary, evidence_result, kind="required-evidence-skipped"
    )


def _mark_summary_failed_evidence(summary: dict[str, object]) -> None:
    summary["verdict"] = "failed"
    reason = cast("dict[str, object]", summary["reason"])
    reason["blocking-validation-failure"] = True
    evidence_result = cast(
        "list[dict[str, object]]", summary["evidence-results"]
    )[0]
    evidence_result["outcome"] = "failed"
    work_groups = cast("dict[str, object]", summary["work-groups"])
    work_groups["required-succeeded"] = 0
    work_groups["required-failed"] = 1
    _append_outcome_failure_for_result(
        summary, evidence_result, kind="blocking-validation-failure"
    )


def _append_summary_failure(
    summary: dict[str, object],
    *,
    kind: str,
    diagnostic_id: str,
    message: str,
    related_ids: dict[str, str | None] | None = None,
) -> None:
    ids = dict(related_ids or {})
    detail_by_id = {
        "required-evidence-missing": "missing-bundle",
        "inadmissible-batch-evidence": "missing-bundle",
        "required-evidence-skipped": "dependency-blocked",
    }
    diagnostic = _diagnostic(
        diagnostic_id, detail=detail_by_id.get(diagnostic_id)
    )
    if ids.get("work-group-id") is not None:
        diagnostic["source"] = {
            "type": "work-group",
            "id": ids["work-group-id"],
        }
    cast("list[dict[str, object]]", summary["failures"]).append(
        {
            "kind": kind,
            "batch-id": ids.get("batch-id"),
            "work-group-id": ids.get("work-group-id"),
            "evidence-expectation-id": ids.get("evidence-expectation-id"),
            "bundle-id": ids.get("bundle-id"),
            "diagnostic": diagnostic,
            "message": message,
        }
    )
    _sort_summary_failures(summary)


def _append_summary_fail_closed_failure(
    summary: dict[str, object],
    diagnostic: dict[str, object],
    message: str,
) -> None:
    cast("list[dict[str, object]]", summary["failures"]).append(
        {
            "kind": "fail-closed",
            "batch-id": None,
            "work-group-id": None,
            "evidence-expectation-id": None,
            "bundle-id": None,
            "diagnostic": diagnostic,
            "message": message,
        }
    )
    _sort_summary_failures(summary)


def _remove_summary_failure_kind(
    summary: dict[str, object],
    kind: str,
) -> None:
    cast("list[dict[str, object]]", summary["failures"])[:] = [
        failure
        for failure in cast("list[dict[str, object]]", summary["failures"])
        if failure["kind"] != kind
    ]


def _make_summary_batch_evidence_missing(
    summary: dict[str, object],
    aggregate_manifest: dict[str, object],
) -> None:
    slot = cast("list[dict[str, object]]", aggregate_manifest["batch-bundles"])[
        0
    ]
    slot["slot-admissibility"] = "missing"
    slot["admitted-candidate-id"] = None
    slot["observed-candidates"] = []
    _refresh_summary_manifest_digest(summary, aggregate_manifest)
    summary_bundle = cast("list[dict[str, object]]", summary["batch-bundles"])[
        0
    ]
    summary_bundle["bundle-id"] = None
    summary_bundle["admitted-candidate-id"] = None
    summary_bundle["candidate-count"] = 0
    summary_bundle["admissibility"] = "missing"
    evidence_result = cast(
        "list[dict[str, object]]", summary["evidence-results"]
    )[0]
    if evidence_result.get("outcome") != "missing":
        _mark_summary_missing_evidence(summary)


def _failed_bundle(bundle: dict[str, object]) -> dict[str, object]:
    failed_bundle = cast("dict[str, object]", deepcopy(bundle))
    result = cast("list[dict[str, object]]", failed_bundle["selector-results"])[
        0
    ]
    result["outcome"] = "blocking-failure"
    cast(
        "list[dict[str, object]]",
        cast("dict[str, object]", result["evidence"])["capability-results"],
    )[0]["outcome"] = "blocking-failure"
    return failed_bundle


def _skipped_bundle(bundle: dict[str, object]) -> dict[str, object]:
    skipped_bundle = cast("dict[str, object]", deepcopy(bundle))
    result = cast(
        "list[dict[str, object]]", skipped_bundle["selector-results"]
    )[0]
    result["outcome"] = "skipped"
    result["skip-reason"] = "not-applicable"
    for capability_result in cast(
        "list[dict[str, object]]",
        cast("dict[str, object]", result["evidence"])["capability-results"],
    ):
        capability_result["outcome"] = "skipped"
    return skipped_bundle


def _dependent_bundle_fixture() -> tuple[
    dict[str, object],
    dict[str, object],
    dict[str, object],
    dict[str, object],
]:
    plan = _plan()
    _add_dependent_work_group(plan)
    manifest = freeze_ci_validation_execution_batch_manifest(
        plan=plan,
        **_authorizing_context_kwargs(),
        batches=_dependent_batches(plan),
        budget=_budget(2),
        created_at=CREATED_AT,
    )
    base_batch_id, dependent_batch_id = _dependent_batch_ids(manifest)
    base_bundle = _bundle_for_batch(plan, manifest, base_batch_id)
    result = _selector_result(plan, manifest, dependent_batch_id)
    result["dependency-results"] = [
        {
            "work-group-id": "wg-python-gate",
            "source-batch-id": base_batch_id,
            "outcome": "satisfied",
            "admitted-for-gating": True,
        }
    ]
    bundle = freeze_ci_validation_batch_evidence_bundle(
        plan=plan,
        execution_batch_manifest=manifest,
        batch_id=dependent_batch_id,
        selector_results=[result],
        writer=_writer_for_batch(manifest, dependent_batch_id),
        execution_tree={
            "observed-commit-sha": TREE_SHA,
            "source": "execution-batch-boundary",
            "verified": True,
        },
        started_at=CREATED_AT,
        completed_at=CREATED_AT,
        created_at=CREATED_AT,
        dependency_evidence_bundles=[base_bundle],
        **_authorizing_context_kwargs(),
    )
    return plan, manifest, base_bundle, bundle


def _dependent_batch_ids(manifest: dict[str, object]) -> tuple[str, str]:
    by_work_group: dict[str, str] = {}
    for batch in cast("list[dict[str, object]]", manifest["batches"]):
        for selector in cast(
            "list[dict[str, object]]",
            batch["ordered-selectors"],
        ):
            by_work_group[cast("str", selector["work-group-id"])] = cast(
                "str", batch["batch-id"]
            )
    return by_work_group["wg-python-gate"], by_work_group["wg-dependent-gate"]


def _bundle_for_batch(
    plan: dict[str, object],
    manifest: dict[str, object],
    batch_id: str,
    selector_result: dict[str, object] | None = None,
    dependency_evidence_bundles: Sequence[Mapping[str, object]] = (),
) -> dict[str, object]:
    return freeze_ci_validation_batch_evidence_bundle(
        plan=plan,
        execution_batch_manifest=manifest,
        batch_id=batch_id,
        selector_results=[
            _selector_result(plan, manifest, batch_id)
            if selector_result is None
            else selector_result
        ],
        writer=_writer_for_batch(manifest, batch_id),
        execution_tree={
            "observed-commit-sha": TREE_SHA,
            "source": "execution-batch-boundary",
            "verified": True,
        },
        started_at=CREATED_AT,
        completed_at=CREATED_AT,
        created_at=CREATED_AT,
        dependency_evidence_bundles=dependency_evidence_bundles,
        **_authorizing_context_kwargs(),
    )


def _dependent_admitted_fixture() -> tuple[
    dict[str, object],
    dict[str, object],
    dict[str, object],
    dict[str, object],
    dict[str, object],
    dict[str, object],
]:
    plan = _plan()
    _add_dependent_work_group(plan)
    manifest = freeze_ci_validation_execution_batch_manifest(
        plan=plan,
        **_authorizing_context_kwargs(),
        batches=_dependent_batches(plan),
        budget=_budget(2),
        created_at=CREATED_AT,
    )
    base_batch_id, dependent_batch_id = _dependent_batch_ids(manifest)
    base_bundle = _bundle_for_batch(plan, manifest, base_batch_id)
    result = _selector_result(plan, manifest, dependent_batch_id)
    result["dependency-results"] = [
        {
            "work-group-id": "wg-python-gate",
            "source-batch-id": base_batch_id,
            "outcome": "satisfied",
            "admitted-for-gating": True,
        }
    ]
    dependent_bundle = _bundle_for_batch(
        plan,
        manifest,
        dependent_batch_id,
        result,
        dependency_evidence_bundles=[base_bundle],
    )
    aggregate_manifest = _aggregate_evidence_manifest_for_bundles(
        plan,
        manifest,
        [base_bundle, dependent_bundle],
    )
    summary = _aggregate_summary_for_bundles(
        plan,
        aggregate_manifest,
        manifest,
        [base_bundle, dependent_bundle],
    )
    return (
        plan,
        manifest,
        base_bundle,
        dependent_bundle,
        aggregate_manifest,
        summary,
    )


def _aggregate_slot_for_bundle(
    bundle: dict[str, object],
    artifact_instance_id: str,
) -> dict[str, object]:
    batch = cast("dict[str, object]", bundle["batch"])
    batch_id = cast("str", batch["batch-id"])
    artifact_ref = cast("str", bundle["artifact-ref"])
    candidate_id = ci_validation_batch_evidence_candidate_id(
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        batch_id=batch_id,
        artifact_ref=artifact_ref,
        artifact_instance_id=artifact_instance_id,
        physical_artifact_name=artifact_physical_name(artifact_ref),
    )
    content_digest = ci_validation_batch_evidence_bundle_payload_digest(bundle)
    return {
        "batch-id": batch_id,
        "artifact-ref": artifact_ref,
        "expected-cardinality": 1,
        "slot-admissibility": "valid",
        "admitted-candidate-id": candidate_id,
        "observed-candidates": [
            {
                "candidate-id": candidate_id,
                "artifact-instance-id": artifact_instance_id,
                "content-digest": content_digest,
                "producer-verification": "verified",
                "payload-readable": True,
                "admissibility": "valid",
                "diagnostics": [],
            }
        ],
        "diagnostics": [],
    }


def _aggregate_input_artifacts(
    plan: dict[str, object],
    manifest: dict[str, object],
) -> dict[str, object]:
    input_artifacts: dict[str, object] = {
        "request": _input_artifact(
            ci_validation_request_artifact_ref(
                run_id=RUN_ID,
                run_attempt=RUN_ATTEMPT,
            ),
            required=True,
            admissibility="valid",
        ),
        "validation-plan": _input_artifact(
            ci_validation_plan_artifact_ref(
                run_id=RUN_ID,
                run_attempt=RUN_ATTEMPT,
            ),
            required=True,
            admissibility="valid",
        ),
        "changed-files-snapshot": _input_artifact(
            ci_validation_changed_files_snapshot_artifact_ref(
                run_id=RUN_ID,
                run_attempt=RUN_ATTEMPT,
            ),
            required=True,
            admissibility="valid",
        ),
        "fact-snapshot": _input_artifact(
            ci_validation_fact_snapshot_artifact_ref(
                run_id=RUN_ID,
                run_attempt=RUN_ATTEMPT,
            ),
            required=True,
            admissibility="valid",
        ),
        "execution-batch-manifest": _input_artifact(
            ci_validation_execution_batch_manifest_artifact_ref(
                run_id=RUN_ID,
                run_attempt=RUN_ATTEMPT,
            ),
            required=True,
            admissibility="valid",
        ),
    }
    cast("dict[str, object]", input_artifacts["request"])["content-digest"] = (
        cast("dict[str, object]", plan["request"])["request-digest"]
    )
    cast("dict[str, object]", input_artifacts["validation-plan"])[
        "content-digest"
    ] = plan["plan-digest"]
    changed_files_hash = cast("dict[str, object]", plan["affected-range"])[
        "changed-files-hash"
    ]
    if isinstance(changed_files_hash, str) and changed_files_hash:
        cast("dict[str, object]", input_artifacts["changed-files-snapshot"])[
            "content-digest"
        ] = changed_files_hash
    elif changed_files_hash is None:
        input_artifacts["changed-files-snapshot"] = _input_artifact(
            None,
            required=False,
            admissibility="not-required",
        )
    cast("dict[str, object]", input_artifacts["fact-snapshot"])[
        "content-digest"
    ] = cast("dict[str, object]", plan["fact-snapshot"])["id"]
    cast("dict[str, object]", input_artifacts["execution-batch-manifest"])[
        "content-digest"
    ] = ci_validation_execution_batch_manifest_payload_digest(manifest)
    return input_artifacts


def _aggregate_evidence_manifest_for_bundles(
    plan: dict[str, object],
    manifest: dict[str, object],
    bundles: list[dict[str, object]],
) -> dict[str, object]:
    return _freeze_ci_validation_aggregate_evidence_manifest(
        created_at=CREATED_AT,
        repository_owner="hcoona",
        repository_name="three",
        workflow="CI Validation",
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        input_artifacts=_aggregate_input_artifacts(plan, manifest),
        batch_bundles=[
            _aggregate_slot_for_bundle(bundle, f"200{index}")
            for index, bundle in enumerate(bundles, start=1)
        ],
        unexpected_contract_artifacts=[],
        namespace_overflow={
            "detected": False,
            "observed-prefixed-artifact-count-lower-bound": 5 + len(bundles),
            "max-prefixed-validation-artifacts": 18,
            "diagnostics": [],
        },
        pre_final_validation_artifacts=5 + len(bundles),
        namespace_closed_at=CREATED_AT,
        plan=plan,
        execution_batch_manifest=manifest,
        request=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
        _require_authoritative_snapshot_inputs=False,
    )


def _aggregate_summary_for_bundles(
    plan: dict[str, object],
    aggregate_manifest: dict[str, object],
    manifest: dict[str, object],
    bundles: list[dict[str, object]],
) -> dict[str, object]:
    manifest_digest = ci_validation_aggregate_evidence_manifest_payload_digest(
        aggregate_manifest
    )
    manifest_ref = cast("str", aggregate_manifest["artifact-ref"])
    bundle_slots = _rows_by_batch_id(
        cast("list[dict[str, object]]", aggregate_manifest["batch-bundles"])
    )
    summary_bundles = []
    evidence_results = []
    for bundle in bundles:
        batch = cast("dict[str, object]", bundle["batch"])
        batch_id = cast("str", batch["batch-id"])
        selector = cast(
            "dict[str, object]",
            cast("list[dict[str, object]]", bundle["selector-results"])[0],
        )
        outcome = "satisfied" if selector["outcome"] == "success" else "failed"
        summary_bundles.append(
            {
                "batch-id": batch_id,
                "artifact-ref": bundle["artifact-ref"],
                "bundle-id": bundle["bundle-id"],
                "admitted-candidate-id": bundle_slots[batch_id][
                    "admitted-candidate-id"
                ],
                "candidate-count": 1,
                "admissibility": "valid",
                "diagnostics": [],
            }
        )
        evidence_results.append(
            {
                "evidence-expectation-id": selector["expected-evidence-id"],
                "work-group-id": selector["work-group-id"],
                "batch-id": batch_id,
                "bundle-id": bundle["bundle-id"],
                "selector-index": selector["selector-index"],
                "outcome": outcome,
                "diagnostics": selector["diagnostics"],
            }
        )
    return freeze_ci_validation_aggregate_summary(
        created_at=CREATED_AT,
        repository_owner="hcoona",
        repository_name="three",
        workflow="CI Validation",
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        aggregate_evidence_manifest={
            "artifact-ref": manifest_ref,
            "artifact-instance-id": "3001",
            "content-digest": manifest_digest,
        },
        final_artifacts={
            "aggregate-evidence-manifest": {
                "artifact-ref": manifest_ref,
                "artifact-instance-id": "3001",
                "content-digest": manifest_digest,
                "producer-verified": True,
            },
            "aggregate-summary": {
                "artifact-ref": ci_validation_aggregate_summary_artifact_ref(
                    run_id=RUN_ID,
                    run_attempt=RUN_ATTEMPT,
                ),
            },
        },
        validation_tree=cast("dict[str, object]", plan["validation-tree"]),
        affected_range=_summary_affected_range(plan),
        request={
            "artifact-ref": ci_validation_request_artifact_ref(
                run_id=RUN_ID,
                run_attempt=RUN_ATTEMPT,
            ),
            "request-digest": cast("dict[str, object]", plan["request"])[
                "request-digest"
            ],
        },
        scheduled_full=cast("dict[str, object]", plan["scheduled-full"]),
        verdict="passed",
        reason={
            "invalid-plan": False,
            "fail-closed": False,
            "required-evidence-missing": False,
            "required-evidence-skipped": False,
            "blocking-validation-failure": False,
            "inadmissible-batch-evidence": False,
            "namespace-closure-failure": False,
            "aggregate-duration-exceeded": False,
            "final-evidence-failure": False,
        },
        budgets={
            "pre-final-validation-artifacts": 5 + len(bundles),
            "expected-final-validation-artifacts": 2,
            "expected-actual-validation-artifacts": 7 + len(bundles),
            "max-validation-artifacts": 20,
            "actual-execution-batches": len(bundles),
            "actual-total-jobs": len(bundles),
            "actual-windows-jobs": 0,
            "aggregate-duration-seconds": 10,
            "aggregate-target-duration-seconds": 60,
            "aggregate-max-duration-seconds": 120,
        },
        diagnostics=[],
        batch_bundles=summary_bundles,
        evidence_results=evidence_results,
        failures=[],
        work_groups={
            "executable-required": len(bundles),
            "required-succeeded": len(bundles),
            "required-failed": 0,
            "required-skipped": 0,
            "required-missing": 0,
            "terminal-aggregation": "present",
        },
        plan=plan,
        aggregate_evidence_manifest_document=aggregate_manifest,
        admitted_batch_evidence_bundles=bundles,
        execution_batch_manifest=manifest,
        request_document=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )


def _rows_by_batch_id(
    rows: list[dict[str, object]],
) -> dict[str, dict[str, object]]:
    return {cast("str", row["batch-id"]): row for row in rows}


def _append_outcome_failure_for_result(
    summary: dict[str, object],
    result: dict[str, object],
    *,
    kind: str,
) -> None:
    _append_summary_failure(
        summary,
        kind=kind,
        diagnostic_id=kind,
        message=f"{kind} for required evidence.",
        related_ids={
            "evidence-expectation-id": cast(
                "str", result["evidence-expectation-id"]
            ),
            "work-group-id": cast("str", result["work-group-id"]),
            "batch-id": cast("str", result["batch-id"])
            if result["batch-id"] is not None
            else None,
            "bundle-id": cast("str", result["bundle-id"])
            if result["bundle-id"] is not None
            else None,
        },
    )


def test_new_ci_validation_kinds_are_registered() -> None:
    """Register execution-batch contract kinds."""
    assert (
        API_VERSIONS_BY_KIND[CiValidationKind.EXECUTION_BATCH_MANIFEST.value]
        == "three.ci.validation.execution-batch-manifest/v1alpha1"
    )
    assert (
        API_VERSIONS_BY_KIND[CiValidationKind.BATCH_EVIDENCE_BUNDLE.value]
        == "three.ci.validation.batch-evidence-bundle/v1alpha1"
    )
    assert (
        API_VERSIONS_BY_KIND[CiValidationKind.AGGREGATE_EVIDENCE_MANIFEST.value]
        == "three.ci.validation.aggregate-evidence-manifest/v1alpha1"
    )
    assert (
        API_VERSIONS_BY_KIND[CiValidationKind.AGGREGATE_SUMMARY.value]
        == "three.ci.validation.aggregate-summary/v1alpha1"
    )


def test_exported_diagnostic_detail_registry_excludes_null() -> None:
    """Null details are accepted by validators, but not exported as details."""
    assert all(
        None not in details for details in DETAILS_BY_DIAGNOSTIC_CODE.values()
    )
    validate_ci_validation_diagnostic_record(
        {
            **_diagnostic("nullable-detail", code="range-unconfirmed"),
            "detail": None,
        }
    )


def test_g1_batch_diagnostics_accept_nullable_detail() -> None:
    """G1 batch diagnostics preserve shared nullable-detail compatibility."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    diagnostic = _diagnostic(
        "nullable-g1-batch-detail",
        code="inadmissible-batch-evidence",
    )
    diagnostic["detail"] = None
    cast("list[dict[str, object]]", bundle["batch-diagnostics"]).append(
        diagnostic
    )

    validate_ci_validation_batch_evidence_bundle(
        bundle,
        plan=plan,
        execution_batch_manifest=manifest,
        **_authorizing_context_kwargs(),
    )


def test_g1_schema_diagnostics_accept_nullable_detail() -> None:
    """G1 schema diagnostics skip strict detail allowlists for null details."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    diagnostic = _schema_diagnostic(
        "nullable-g1-schema-detail",
        detail="aggregate-summary-without-manifest",
    )
    diagnostic["detail"] = None
    cast("list[dict[str, object]]", summary["schema-diagnostics"]).append(
        diagnostic
    )

    validate_ci_validation_aggregate_summary(
        summary,
        plan=plan,
        aggregate_evidence_manifest=aggregate_manifest,
        admitted_batch_evidence_bundles=[bundle],
        execution_batch_manifest=manifest,
        request=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )


def test_g1_summary_failure_diagnostics_accept_nullable_detail() -> None:
    """G1 summary failure diagnostics keep nullable-detail compatibility."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _make_summary_batch_evidence_missing(summary, aggregate_manifest)
    failure = next(
        item
        for item in cast("list[dict[str, object]]", summary["failures"])
        if item["kind"] == "required-evidence-missing"
    )
    cast("dict[str, object]", failure["diagnostic"])["detail"] = None

    validate_ci_validation_aggregate_summary(
        summary,
        plan=plan,
        aggregate_evidence_manifest=aggregate_manifest,
        admitted_batch_evidence_bundles=[],
        execution_batch_manifest=manifest,
        request=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )


def test_diagnostic_source_rejects_extra_keys() -> None:
    """Diagnostic source objects are closed to type and id."""
    diagnostic = _diagnostic("namespace-closure-failure")
    cast("dict[str, object]", diagnostic["source"])["extra"] = "forged"

    with pytest.raises(ContractValidationError):
        validate_ci_validation_diagnostic_record(diagnostic)


@pytest.mark.parametrize(
    "detail",
    [
        "aggregate-duration-exceeded",
        "aggregate-evidence-manifest-missing",
        "aggregate-summary-without-manifest",
        "namespace-overflow",
        "aggregate-without-manifest",
        "final-manifest-missing",
        "final-aggregate-missing",
    ],
)
def test_shared_final_evidence_registry_accepts_legacy_details(
    detail: str,
) -> None:
    """The shared registry keeps legacy aggregate compatibility details."""
    validate_ci_validation_diagnostic_record(
        _diagnostic(
            "final-evidence",
            code="final-evidence-failure",
            detail=detail,
            severity="fail-closed",
            verdict_effect="fail-closed",
        )
    )


@pytest.mark.parametrize(
    ("code", "detail"),
    [
        ("required-evidence-missing", "missing-bundle"),
        ("required-evidence-skipped", "dependency-blocked"),
        ("inadmissible-batch-evidence", "malformed-bundle"),
        ("inadmissible-batch-evidence", "execution-batch-manifest-malformed"),
        ("namespace-closure-failure", "unexpected-contract-artifact"),
        ("final-evidence-failure", "execution-batch-manifest-missing"),
    ],
)
def test_g1_diagnostic_registry_accepts_lld_details(
    code: str,
    detail: str,
) -> None:
    """G1 diagnostic families have LLD-specific failure-cause details."""
    validate_ci_validation_diagnostic_record(
        _diagnostic(
            "g1-detail",
            code=code,
            detail=detail,
            severity="fail-closed",
            verdict_effect="fail-closed",
        )
    )


@pytest.mark.parametrize(
    ("code", "detail"),
    [
        ("required-evidence-missing", "required-evidence-missing"),
        ("inadmissible-batch-evidence", "inadmissible-batch-evidence"),
        ("namespace-closure-failure", "namespace-closure-failure"),
    ],
)
def test_g1_contract_validators_reject_legacy_self_details(
    code: str,
    detail: str,
) -> None:
    """G1 artifact validators enforce the G1-specific detail vocabulary."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    cast("list[dict[str, object]]", bundle["batch-diagnostics"]).append(
        _diagnostic("legacy-self-detail", code=code, detail=detail)
    )

    with pytest.raises(ContractValidationError):
        validate_ci_validation_batch_evidence_bundle(
            bundle,
            plan=plan,
            execution_batch_manifest=manifest,
            **_authorizing_context_kwargs(),
        )


@pytest.mark.parametrize("detail", _OBSOLETE_FINAL_EVIDENCE_DETAILS)
def test_aggregate_summary_rejects_old_new_g1_final_details(
    detail: str,
) -> None:
    """Aggregate summaries use aggregate-specific G1 final detail spelling."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_missing_aggregate_manifest(summary)
    failure = cast("list[dict[str, object]]", summary["failures"])[-1]
    cast("dict[str, object]", failure["diagnostic"])["detail"] = detail

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


@pytest.mark.parametrize(
    ("code", "wrong_detail"),
    [
        ("namespace-closure-failure", "aggregate-duration-exceeded"),
        ("blocking-validation-failure", "namespace-closure-failure"),
        ("inadmissible-batch-evidence", "blocking-validation-failure"),
        ("aggregate-duration-exceeded", "aggregate-summary-without-manifest"),
    ],
)
def test_strict_g1_diagnostics_reject_wrong_registered_detail(
    code: str,
    wrong_detail: str,
) -> None:
    """Strict G1 families reject registered details for other codes."""
    with pytest.raises(ContractValidationError):
        validate_ci_validation_diagnostic_record(
            _diagnostic(code, code=code, detail=wrong_detail)
        )


def test_artifact_refs_follow_execution_batch_layout() -> None:
    """Expose deterministic logical refs for later workflow groups."""
    assert ci_validation_execution_batch_manifest_artifact_ref(
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
    ) == (
        "ci-validation/execution-batches/25887422010/1/"
        "execution-batch-manifest.json"
    )
    assert ci_validation_batch_evidence_bundle_artifact_ref(
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        batch_id=_BATCH_ID,
    ) == (
        f"ci-validation/bundles/25887422010/1/{_BATCH_ID}/"
        "batch-evidence-bundle.json"
    )
    assert ci_validation_aggregate_evidence_manifest_artifact_ref(
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
    ) == (
        "ci-validation/aggregate/25887422010/1/aggregate-evidence-manifest.json"
    )
    assert (
        ci_validation_aggregate_summary_artifact_ref(
            run_id=RUN_ID,
            run_attempt=RUN_ATTEMPT,
        )
        == "ci-validation/aggregate/25887422010/1/aggregate-summary.json"
    )


def test_execution_batch_manifest_freezes_and_validates() -> None:
    """One executable batch maps to one manifest batch slot."""
    plan = _plan()
    manifest = _manifest(plan)

    validate_ci_validation_execution_batch_manifest(
        manifest,
        plan=plan,
        authorizing=False,
    )

    assert manifest["kind"] == CiValidationKind.EXECUTION_BATCH_MANIFEST.value
    assert (
        cast("dict[str, object]", manifest["budget"])[
            "expected-final-validation-artifacts"
        ]
        == EXPECTED_FINAL_ARTIFACTS
    )
    assert ci_validation_execution_batch_manifest_payload_digest(manifest) == (
        canonical_json_digest(manifest)
    )


def test_execution_batch_manifest_rejects_forged_writer_id() -> None:
    """Batch writer IDs are recomputed from workflow, job, and matrix."""
    plan = _plan()
    manifest = _manifest(plan)
    batch = cast("list[dict[str, object]]", manifest["batches"])[0]
    writer = cast("dict[str, object]", batch["batch-writer"])
    writer["expected-job-identity"] = "github-actions-job:" + "f" * 64

    with pytest.raises(ContractValidationError, match="expected-job-identity"):
        validate_ci_validation_execution_batch_manifest(
            manifest,
            plan=plan,
            authorizing=False,
        )


def test_execution_batch_matrix_recomputes_writer_ids() -> None:
    """Matrix projection emits writer IDs from explicit identity payload."""
    plan = _plan()
    manifest = _manifest(plan)
    batch = cast("list[dict[str, object]]", manifest["batches"])[0]
    row = cast(
        "list[dict[str, object]]",
        ci_validation_execution_batch_matrix(
            manifest,
            plan=plan,
            **_authorizing_context_kwargs(),
        )["include"],
    )[0]

    expected = ci_validation_writer_id(
        workflow="CI Validation",
        job=cast("str", manifest["execution-job"]),
        matrix={
            "batch-id": batch["batch-id"],
            "runner-family": batch["runner-family"],
            "expected-batch-evidence-bundle-ref": batch[
                "expected-batch-evidence-bundle-ref"
            ],
        },
    )
    assert row["identity-matrix"] == {
        "batch-id": batch["batch-id"],
        "runner-family": batch["runner-family"],
        "expected-batch-evidence-bundle-ref": batch[
            "expected-batch-evidence-bundle-ref"
        ],
    }
    assert row["expected-job-identity"] == expected
    full_row_identity = ci_validation_writer_id(
        workflow="CI Validation",
        job=cast("str", manifest["execution-job"]),
        matrix=row,
    )
    assert row["expected-job-identity"] != full_row_identity


def test_materializer_binds_custom_execution_job() -> None:
    """Custom execution job names flow into manifest, writer, and matrix IDs."""
    snapshot = _PLANS_MODULE.__dict__["_plan_snapshot"]()
    execution_job = "custom-execution-batch"

    materialization = materialize_ci_validation_execution_batches(
        plan=cast("dict[str, object]", snapshot.plan),
        request=_request_document(),
        changed_files_snapshot=cast(
            "dict[str, object]", snapshot.changed_files_snapshot
        ),
        fact_snapshot=cast("dict[str, object]", snapshot.fact_snapshot),
        created_at=CREATED_AT,
        execution_workflow="CI Validation",
        execution_job=execution_job,
        expected_run_id=RUN_ID,
        expected_run_attempt=RUN_ATTEMPT,
    )
    manifest = cast("dict[str, object]", materialization.manifest)
    batch = cast("list[dict[str, object]]", manifest["batches"])[0]
    identity = _execution_batch_matrix_identity(batch)
    expected_writer_id = ci_validation_writer_id(
        workflow="CI Validation",
        job=execution_job,
        matrix=identity,
    )
    row = cast(
        "list[dict[str, object]]",
        materialization.matrix["include"],
    )[0]

    assert manifest["execution-job"] == execution_job
    assert (
        cast("dict[str, object]", batch["batch-writer"])[
            "expected-job-identity"
        ]
        == expected_writer_id
    )
    assert row["identity-matrix"] == identity
    assert row["expected-job-identity"] == expected_writer_id


def test_materializer_supports_ruby_batch_compatibility() -> None:
    """Ruby work groups materialize into registered compatibility profiles."""
    snapshot = _PLANS_MODULE.__dict__["_plan_snapshot"]()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    fact_snapshot = cast(
        "dict[str, object]",
        deepcopy(snapshot.fact_snapshot),
    )
    _retarget_plan_to_ruby(plan, fact_snapshot)

    materialization = materialize_ci_validation_execution_batches(
        plan=plan,
        request=_request_document(),
        changed_files_snapshot=cast(
            "dict[str, object]", snapshot.changed_files_snapshot
        ),
        fact_snapshot=fact_snapshot,
        created_at=CREATED_AT,
        execution_workflow="CI Validation",
        expected_run_id=RUN_ID,
        expected_run_attempt=RUN_ATTEMPT,
    )
    batches = cast(
        "list[dict[str, object]]",
        cast("dict[str, object]", materialization.manifest)["batches"],
    )
    ruby_batches = [
        batch
        for batch in batches
        if cast("dict[str, object]", batch["compatibility-profile"])[
            "ecosystem"
        ]
        == "ruby"
    ]

    assert len(ruby_batches) == 1
    assert (
        cast(
            "dict[str, object]",
            ruby_batches[0]["compatibility-profile"],
        )["setup-profile"]
        == "setup-ubuntu-ruby"
    )
    assert any(
        row["batch-id"] == ruby_batches[0]["batch-id"]
        for row in cast(
            "list[dict[str, object]]",
            materialization.matrix["include"],
        )
    )


def test_materializer_rejects_ruby_windows_runner_family() -> None:
    """Ruby materialization inherits the Ubuntu-only runner constraint."""
    snapshot = _PLANS_MODULE.__dict__["_plan_snapshot"]()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    fact_snapshot = cast(
        "dict[str, object]",
        deepcopy(snapshot.fact_snapshot),
    )
    _retarget_plan_to_ruby(plan, fact_snapshot)
    for group in cast("list[dict[str, object]]", plan["work-groups"]):
        if group["kind"] != "evidence-aggregation":
            group["runner-family"] = "windows"
    plan["plan-digest"] = ci_validation_plan_digest(plan)

    with pytest.raises(ContractValidationError, match="runner-family"):
        materialize_ci_validation_execution_batches(
            plan=plan,
            request=_request_document(),
            changed_files_snapshot=cast(
                "dict[str, object]", snapshot.changed_files_snapshot
            ),
            fact_snapshot=fact_snapshot,
            created_at=CREATED_AT,
            execution_workflow="CI Validation",
            expected_run_id=RUN_ID,
            expected_run_attempt=RUN_ATTEMPT,
        )


def test_materializer_requires_explicit_current_run_inputs() -> None:
    """Materialization cannot fall back to stale plan-envelope run context."""
    snapshot = _PLANS_MODULE.__dict__["_plan_snapshot"]()

    with pytest.raises(ContractValidationError, match="expected-run-id"):
        materialize_ci_validation_execution_batches(
            plan=cast("dict[str, object]", snapshot.plan),
            request=_request_document(),
            changed_files_snapshot=cast(
                "dict[str, object]", snapshot.changed_files_snapshot
            ),
            fact_snapshot=cast("dict[str, object]", snapshot.fact_snapshot),
            created_at=CREATED_AT,
            execution_workflow="CI Validation",
            expected_run_attempt=RUN_ATTEMPT,
        )
    with pytest.raises(ContractValidationError, match="expected-run-attempt"):
        materialize_ci_validation_execution_batches(
            plan=cast("dict[str, object]", snapshot.plan),
            request=_request_document(),
            changed_files_snapshot=cast(
                "dict[str, object]", snapshot.changed_files_snapshot
            ),
            fact_snapshot=cast("dict[str, object]", snapshot.fact_snapshot),
            created_at=CREATED_AT,
            execution_workflow="CI Validation",
            expected_run_id=RUN_ID,
        )
    with pytest.raises(ContractValidationError, match="execution-workflow"):
        materialize_ci_validation_execution_batches(
            plan=cast("dict[str, object]", snapshot.plan),
            request=_request_document(),
            changed_files_snapshot=cast(
                "dict[str, object]", snapshot.changed_files_snapshot
            ),
            fact_snapshot=cast("dict[str, object]", snapshot.fact_snapshot),
            created_at=CREATED_AT,
            expected_run_id=RUN_ID,
            expected_run_attempt=RUN_ATTEMPT,
        )


def test_materializer_splits_release_batches_by_receipt_shape_only() -> None:
    """Receipt-only release obligation changes affect profile digests."""
    provider = _PLANS_MODULE.__dict__["_descriptor_fact_provider"]()
    catalog = cast("dict[str, object]", provider["target-catalog"])
    entries = cast("list[dict[str, object]]", catalog["entries"])
    receipt_entry = deepcopy(entries[0])
    receipt_entry["profile"] = "wheel-alt-receipt"
    cast("dict[str, object]", receipt_entry["release-receipt"])[
        "logical-receipt-role"
    ] = "build-alt"
    entries.append(receipt_entry)

    base_obligation = _PLANS_MODULE.__dict__["_artifact_obligation"]()
    receipt_obligation = deepcopy(base_obligation)
    receipt_obligation["artifact-obligation-id"] = (
        "artifact-example-alt-receipt"
    )
    receipt_obligation["validation-obligation-id"] = (
        "validation-artifact-alt-receipt"
    )
    receipt_obligation["profile-coverage"] = ["wheel-alt-receipt"]
    receipt_obligation["work-group-id"] = "wg-artifact-alt-receipt"
    receipt_obligation["expected-evidence-id"] = "evidence-artifact-alt-receipt"
    cast("dict[str, object]", receipt_obligation["release-receipt"])[
        "logical-receipt-role"
    ] = "build-alt"

    receipt_work_group = _PLANS_MODULE.__dict__["_artifact_work_group"]()
    receipt_work_group["work-group-id"] = "wg-artifact-alt-receipt"
    receipt_work_group["coverage-target"] = {
        "type": "artifact-obligation",
        "id": "artifact-example-alt-receipt",
    }
    receipt_evidence = _PLANS_MODULE.__dict__[
        "_artifact_evidence_expectation"
    ]()
    receipt_evidence["evidence-expectation-id"] = (
        "evidence-artifact-alt-receipt"
    )
    receipt_evidence["work-group-id"] = "wg-artifact-alt-receipt"
    receipt_evidence["coverage-target"] = {
        "type": "artifact-obligation",
        "id": "artifact-example-alt-receipt",
    }
    receipt_validation = _PLANS_MODULE.__dict__[
        "_artifact_validation_obligation"
    ]()
    receipt_validation["validation-obligation-id"] = (
        "validation-artifact-alt-receipt"
    )
    receipt_validation["coverage-target"] = {
        "type": "artifact-obligation",
        "id": "artifact-example-alt-receipt",
    }
    receipt_validation["work-group-id"] = "wg-artifact-alt-receipt"
    receipt_validation["expected-evidence-id"] = "evidence-artifact-alt-receipt"

    snapshot = _PLANS_MODULE.__dict__["freeze_ci_validation_plan"](
        request=_PLANS_MODULE.__dict__["_normalized_request"](),
        plan_id=cast("str", _PLANS_MODULE.__dict__["PLAN_ID"]),
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_PLANS_MODULE.__dict__["_classification"](),
        subjects=[_PLANS_MODULE.__dict__["_descriptor_backed_subject"]()],
        validation_obligations=[
            _PLANS_MODULE.__dict__["_artifact_validation_obligation"](),
            receipt_validation,
            _PLANS_MODULE.__dict__["_validation_obligation"](),
        ],
        descriptor_obligations=[
            _PLANS_MODULE.__dict__["_descriptor_obligation"]()
        ],
        artifact_obligations=[base_obligation, receipt_obligation],
        work_groups=[
            _PLANS_MODULE.__dict__["_artifact_work_group"](),
            receipt_work_group,
            _PLANS_MODULE.__dict__["_descriptor_work_group"](),
            _PLANS_MODULE.__dict__["_ecosystem_gate_work_group"](),
        ],
        evidence_expectations=[
            _PLANS_MODULE.__dict__["_artifact_evidence_expectation"](),
            receipt_evidence,
            _PLANS_MODULE.__dict__["_descriptor_evidence_expectation"](),
            _PLANS_MODULE.__dict__["_evidence_expectation"](),
        ],
        fact_snapshot_providers=[provider],
    )
    materialization = materialize_ci_validation_execution_batches(
        plan=cast("dict[str, object]", snapshot.plan),
        request=_request_document(),
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
        created_at=CREATED_AT,
        execution_workflow="CI Validation",
        expected_run_id=RUN_ID,
        expected_run_attempt=RUN_ATTEMPT,
    )

    release_batches = [
        batch
        for batch in cast(
            "list[dict[str, object]]",
            materialization.manifest["batches"],
        )
        if cast("dict[str, object]", batch["compatibility-profile"])[
            "release-shaped-profile"
        ]
        is not None
    ]
    expected_release_batch_count = 2
    assert len(release_batches) == expected_release_batch_count
    assert (
        len({batch["batch-id"] for batch in release_batches})
        == expected_release_batch_count
    )
    assert (
        len(
            {
                cast("dict[str, object]", batch["compatibility-profile"])[
                    "release-shaped-profile-digest"
                ]
                for batch in release_batches
            }
        )
        == expected_release_batch_count
    )


def test_execution_batch_manifest_rejects_selector_loss() -> None:
    """Batch materialization cannot drop selected executable work groups."""
    plan = _plan()
    manifest = _manifest(plan)
    cast("list[dict[str, Any]]", manifest["batches"])[0][
        "ordered-selectors"
    ] = []

    with pytest.raises(ContractValidationError):
        validate_ci_validation_execution_batch_manifest(
            manifest,
            plan=plan,
            authorizing=False,
        )


_PLAN_IDENTIFIER_ARRAYS = (
    ("work-groups", "work-group-id"),
    ("evidence-expectations", "evidence-expectation-id"),
    ("validation-obligations", "validation-obligation-id"),
    ("descriptor-obligations", "descriptor-obligation-id"),
    ("artifact-obligations", "artifact-obligation-id"),
    ("diagnostics", "diagnostic-id"),
)
_MIN_UNORDERED_RECORDS = 2


@pytest.mark.parametrize(("section", "id_key"), _PLAN_IDENTIFIER_ARRAYS)
def test_execution_batch_manifest_rejects_duplicate_plan_identifier_arrays(
    section: str,
    id_key: str,
) -> None:
    """Plan-bound validation rejects every self-digested duplicate id array."""
    plan, manifest = _release_plan_and_manifest()
    records = cast("list[dict[str, object]]", plan[section])
    if records:
        records.append(deepcopy(records[0]))
    else:
        records.extend([{id_key: "duplicate-id"}, {id_key: "duplicate-id"}])
    plan["plan-digest"] = ci_validation_plan_digest(plan)
    manifest["plan-digest"] = plan["plan-digest"]

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_execution_batch_manifest(
            manifest,
            plan=plan,
            authorizing=False,
        )

    assert any(
        issue.path in {f"$.{section}", section} for issue in error.value.issues
    )


@pytest.mark.parametrize(("section", "id_key"), _PLAN_IDENTIFIER_ARRAYS)
def test_execution_batch_manifest_rejects_unordered_plan_identifier_arrays(
    section: str,
    id_key: str,
) -> None:
    """Plan-bound validation rejects every self-digested unordered id array."""
    plan, manifest = _release_plan_and_manifest()
    records = cast("list[dict[str, object]]", plan[section])
    if len(records) >= _MIN_UNORDERED_RECORDS:
        records[0], records[1] = records[1], records[0]
    else:
        records[:] = [{id_key: "zzzz-unordered"}, {id_key: "aaaa-unordered"}]
    plan["plan-digest"] = ci_validation_plan_digest(plan)
    manifest["plan-digest"] = plan["plan-digest"]

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_execution_batch_manifest(
            manifest,
            plan=plan,
            authorizing=False,
        )

    assert any(
        issue.path in {f"$.{section}", section} for issue in error.value.issues
    )


def test_execution_batch_manifest_rejects_structurally_invalid_plan() -> None:
    """Plan-bound validation runs full executable-binding plan validation."""
    plan, manifest = _release_plan_and_manifest()
    validation_obligations = cast(
        "list[dict[str, object]]",
        plan["validation-obligations"],
    )
    obligation = validation_obligations[0]
    obligation["expected-evidence-id"] = "missing-evidence"
    plan["plan-digest"] = ci_validation_plan_digest(plan)
    manifest["plan-digest"] = plan["plan-digest"]

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_execution_batch_manifest(
            manifest,
            plan=plan,
            authorizing=False,
        )

    assert any(
        issue.path.startswith("$.validation-obligations")
        or issue.path == "$.executable-bindings"
        for issue in error.value.issues
    )


@pytest.mark.parametrize("field", ("artifact", "release-receipt"))  # noqa: PT007
@pytest.mark.parametrize("replacement", ("delete", "empty"))  # noqa: PT007
def test_execution_batch_manifest_rejects_malformed_release_obligation_payload(
    field: str,
    replacement: str,
) -> None:
    """Release-shaped artifact obligations require complete payload blocks."""
    plan, manifest = _release_plan_and_manifest()
    artifact_obligations = cast(
        "list[dict[str, object]]",
        plan["artifact-obligations"],
    )
    obligation = artifact_obligations[0]
    if replacement == "delete":
        del obligation[field]
    else:
        obligation[field] = {}
    plan["plan-digest"] = ci_validation_plan_digest(plan)
    manifest["plan-digest"] = plan["plan-digest"]

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_execution_batch_manifest(
            manifest,
            plan=plan,
            authorizing=False,
        )

    expected_path = f"artifact-obligation.{field}"
    assert any(
        issue.path == expected_path
        or issue.path.startswith(f"{expected_path}.")
        for issue in error.value.issues
    )


def _authorize_execution_batch_helper(  # noqa: PLR0913
    helper: str,
    *,
    plan: dict[str, object],
    manifest: dict[str, object],
    request: dict[str, object] | None,
    changed_files_snapshot: dict[str, object] | None,
    fact_snapshot: dict[str, object] | None,
) -> None:
    if helper == "validate":
        validate_ci_validation_execution_batch_manifest(
            manifest,
            plan=plan,
            request=request,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            expected_run_id=RUN_ID,
            expected_run_attempt=RUN_ATTEMPT,
        )
        return
    if helper == "matrix":
        ci_validation_execution_batch_matrix(
            manifest,
            plan=plan,
            request=request,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            expected_run_id=RUN_ID,
            expected_run_attempt=RUN_ATTEMPT,
        )
        return
    freeze_ci_validation_execution_batch_manifest(
        plan=plan,
        request=request,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        expected_run_id=RUN_ID,
        expected_run_attempt=RUN_ATTEMPT,
        batches=cast("list[dict[str, object]]", manifest["batches"]),
        budget=cast("dict[str, object]", manifest["budget"]),
        created_at=CREATED_AT,
        execution_job=cast("str", manifest["execution-job"]),
    )


def _authorize_execution_batch_helper_without_request_current_run(
    helper: str,
    *,
    plan: dict[str, object],
    manifest: dict[str, object],
) -> None:
    changed_files_snapshot = _changed_files_snapshot_document()
    fact_snapshot = _fact_snapshot_document()
    if helper == "validate":
        validate_ci_validation_execution_batch_manifest(
            manifest,
            plan=plan,
            request=None,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            authorizing=True,
        )
        return
    if helper == "matrix":
        ci_validation_execution_batch_matrix(
            manifest,
            plan=plan,
            request=None,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
        )
        return
    freeze_ci_validation_execution_batch_manifest(
        plan=plan,
        request=None,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        batches=cast("list[dict[str, object]]", manifest["batches"]),
        budget=cast("dict[str, object]", manifest["budget"]),
        created_at=CREATED_AT,
        execution_job=cast("str", manifest["execution-job"]),
    )


def _default_authorizing_execution_batch_helper_without_context(
    helper: str,
    *,
    plan: dict[str, object],
    manifest: dict[str, object],
) -> None:
    if helper == "validate":
        validate_ci_validation_execution_batch_manifest(manifest, plan=plan)
        return
    if helper == "matrix":
        ci_validation_execution_batch_matrix(manifest, plan=plan)
        return
    freeze_ci_validation_execution_batch_manifest(
        plan=plan,
        batches=cast("list[dict[str, object]]", manifest["batches"]),
        budget=cast("dict[str, object]", manifest["budget"]),
        created_at=CREATED_AT,
        execution_job=cast("str", manifest["execution-job"]),
    )


@pytest.mark.parametrize("helper", ("validate", "matrix", "freeze"))  # noqa: PT007
def test_plan_bound_helpers_default_to_authorizing_context(
    helper: str,
) -> None:
    """Public helper defaults fail closed without authorization context."""
    plan = _plan()
    manifest = _manifest(plan)

    with pytest.raises(ContractValidationError) as error:
        _default_authorizing_execution_batch_helper_without_context(
            helper,
            plan=plan,
            manifest=manifest,
        )

    issue_paths = {issue.path for issue in error.value.issues}
    assert {"request", "expected-run-id", "expected-run-attempt"}.issubset(
        issue_paths,
    )


@pytest.mark.parametrize("helper", ("validate", "matrix", "freeze"))  # noqa: PT007
def test_plan_bound_authorizing_helpers_reject_missing_request_and_current_run(
    helper: str,
) -> None:
    """Authorizing helpers require explicit request and current-run context."""
    plan = _plan()
    manifest = _manifest(plan)

    with pytest.raises(ContractValidationError) as error:
        _authorize_execution_batch_helper_without_request_current_run(
            helper,
            plan=plan,
            manifest=manifest,
        )

    issue_paths = {issue.path for issue in error.value.issues}
    assert {"request", "expected-run-id", "expected-run-attempt"}.issubset(
        issue_paths,
    )


def test_materializer_rejects_missing_request_and_current_run_context() -> None:
    """Materialization requires the request and current run before batching."""
    snapshot = _PLANS_MODULE.__dict__["_plan_snapshot"]()

    with pytest.raises(ContractValidationError) as error:
        materialize_ci_validation_execution_batches(
            plan=cast("dict[str, object]", snapshot.plan),
            request=cast("Any", None),
            changed_files_snapshot=cast(
                "dict[str, object]", snapshot.changed_files_snapshot
            ),
            fact_snapshot=cast("dict[str, object]", snapshot.fact_snapshot),
            created_at=CREATED_AT,
            execution_workflow="CI Validation",
        )

    issue_paths = {issue.path for issue in error.value.issues}
    assert {"request", "expected-run-id", "expected-run-attempt"}.issubset(
        issue_paths,
    )


@pytest.mark.parametrize("helper", ("validate", "matrix", "freeze"))  # noqa: PT007
def test_plan_bound_helpers_require_companions_for_authorization(
    helper: str,
) -> None:
    """Authorizing low-level helpers fail closed without companion snapshots."""
    plan = _plan()
    manifest = _manifest(plan)

    with pytest.raises(ContractValidationError) as error:
        _authorize_execution_batch_helper(
            helper,
            plan=plan,
            manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=None,
            fact_snapshot=None,
        )

    assert {
        "$.changed-files-snapshot",
        "$.fact-snapshot",
    }.issubset({issue.path for issue in error.value.issues})


@pytest.mark.parametrize("helper", ("validate", "matrix", "freeze"))  # noqa: PT007
def test_plan_bound_helpers_reject_stale_authorizing_request(
    helper: str,
) -> None:
    """Authorizing low-level helpers bind the request to the current run."""
    plan = _plan()
    manifest = _manifest(plan)
    request = _request_document()
    cast("dict[str, object]", request["run"])["run-id"] = "00000000000"
    request["artifact-ref"] = ci_validation_request_artifact_ref(
        run_id="00000000000",
        run_attempt=RUN_ATTEMPT,
    )
    request["request-digest"] = canonical_json_digest(
        ci_validation_request_projection(request)
    )

    with pytest.raises(ContractValidationError) as error:
        _authorize_execution_batch_helper(
            helper,
            plan=plan,
            manifest=manifest,
            request=request,
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path in {"$.run.run-id", "request.artifact-ref"}
        or issue.path == "ci-validation-request"
        for issue in error.value.issues
    )


def test_execution_batch_manifest_accepts_empty_batches_without_plan() -> None:
    """Standalone zero-batch handoff is valid when budget is self-consistent."""
    plan = _plan()
    manifest = _manifest(plan)
    manifest["batches"] = []
    manifest["budget"] = _budget(0)
    absent_identity_manifest = deepcopy(manifest)
    absent_identity_manifest.pop("plan-id")
    absent_identity_manifest.pop("plan-digest")

    validate_ci_validation_execution_batch_manifest(
        absent_identity_manifest,
        authorizing=False,
    )
    manifest["plan-id"] = None
    manifest["plan-digest"] = None
    validate_ci_validation_execution_batch_manifest(
        manifest,
        authorizing=False,
    )
    assert ci_validation_execution_batch_matrix(
        manifest,
        authorizing=False,
    ) == {"include": []}


@pytest.mark.parametrize("authorizing_kwargs", [{}, {"authorizing": True}])
@pytest.mark.parametrize("plan_identity", ["omitted", "none"])
def test_planless_zero_batch_manifest_requires_non_authorizing_mode(
    authorizing_kwargs: dict[str, bool],
    plan_identity: str,
) -> None:
    """Planless no-work handoffs cannot authorize without plan context."""
    plan = _plan()
    manifest = _manifest(plan)
    manifest["batches"] = []
    manifest["budget"] = _budget(0)
    if plan_identity == "omitted":
        manifest.pop("plan-id")
        manifest.pop("plan-digest")
    else:
        manifest["plan-id"] = None
        manifest["plan-digest"] = None

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_execution_batch_manifest(
            manifest,
            **authorizing_kwargs,
        )

    assert any(
        issue.path == "authorizing"
        and "explicit non-authorizing mode" in issue.message
        for issue in error.value.issues
    )


def test_planless_zero_batch_manifest_rejects_stale_plan_identity() -> None:
    """Planless no-work manifests cannot carry authoritative plan identity."""
    plan = _plan()
    manifest = _manifest(plan)
    manifest["batches"] = []
    manifest["budget"] = _budget(0)

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_execution_batch_manifest(
            manifest,
            authorizing=False,
        )

    assert {"$.plan-id", "$.plan-digest"}.issubset(
        {issue.path for issue in error.value.issues}
    )


def test_planless_non_authorizing_batches_require_plan() -> None:
    """Direct validation rejects non-empty batches without a plan."""
    with pytest.raises(ContractValidationError, match="authorizing"):
        validate_ci_validation_execution_batch_manifest(
            _manifest(_plan()),
            authorizing=False,
        )


def test_planless_authorizing_batches_require_plan() -> None:
    """Authorizing validation rejects executable batches without a plan."""
    with pytest.raises(ContractValidationError, match="authorizing"):
        validate_ci_validation_execution_batch_manifest(_manifest(_plan()))


def test_public_execution_batch_validator_rejects_planless_escape_hatch() -> (
    None
):
    """Public callers cannot bypass planless non-empty batch rejection."""
    validator = cast("Any", validate_ci_validation_execution_batch_manifest)
    manifest = _manifest(_plan())

    with pytest.raises(
        TypeError,
        match="_allow_planless_non_authorizing_batches",
    ):
        validator(
            manifest,
            authorizing=False,
            _allow_planless_non_authorizing_batches=True,
        )

    with pytest.raises(
        TypeError,
        match="_allow_planless_non_authorizing_batches",
    ):
        validator(
            manifest,
            authorizing=True,
            _allow_planless_non_authorizing_batches=True,
        )


def test_private_planless_non_authorizing_diagnostic_batches_do_not_raise() -> (
    None
):
    """The private diagnostic path can inspect non-empty planless manifests."""
    _validate_ci_validation_execution_batch_manifest(
        _manifest(_plan()),
        plan=None,
        authorizing=False,
        _allow_planless_non_authorizing_batches=True,
    )


def test_non_authorizing_matrix_rejects_non_empty_batches() -> None:
    """Non-authorizing matrix handoff is limited to no-work manifests."""
    with pytest.raises(ContractValidationError, match="authorizing"):
        ci_validation_execution_batch_matrix(
            _manifest(_plan()),
            authorizing=False,
        )


def test_non_authorizing_freeze_rejects_non_empty_batches() -> None:
    """Non-authorizing freeze cannot create executable batch handoffs."""
    plan = _plan()

    with pytest.raises(ContractValidationError, match="authorizing"):
        freeze_ci_validation_execution_batch_manifest(
            plan=plan,
            batches=[_batch(plan)],
            budget=_budget(1),
            created_at=CREATED_AT,
            authorizing=False,
        )


def test_execution_batch_manifest_rejects_extra_empty_batch() -> None:
    """Extra stale batch nodes cannot hide behind empty selector arrays."""
    plan = _plan()
    manifest = _manifest(plan)
    stale_batch = cast(
        "dict[str, object]",
        deepcopy(cast("list[dict[str, object]]", manifest["batches"])[0]),
    )
    stale_batch["batch-id"] = "batch-stale-gate"
    stale_batch["ordered-selectors"] = []
    stale_batch["depends-on-batches"] = []
    stale_batch["expected-batch-evidence-bundle-ref"] = (
        ci_validation_batch_evidence_bundle_artifact_ref(
            run_id=RUN_ID,
            run_attempt=RUN_ATTEMPT,
            batch_id="batch-stale-gate",
        )
    )
    cast("list[dict[str, object]]", manifest["batches"]).append(stale_batch)
    manifest["budget"] = _budget(2)

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_execution_batch_manifest(
            manifest,
            plan=plan,
            authorizing=False,
        )
    assert any(
        issue.path.endswith(".ordered-selectors")
        for issue in error.value.issues
    )


@pytest.mark.parametrize(
    "contract_name",
    [
        "execution-batch-manifest",
        "batch-evidence-bundle",
        "aggregate-evidence-manifest",
        "aggregate-summary",
    ],
)
def test_g1_contract_roots_reject_extra_keys(contract_name: str) -> None:
    """All G1 contract roots are closed against unregistered keys."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    document_by_name = {
        "execution-batch-manifest": manifest,
        "batch-evidence-bundle": bundle,
        "aggregate-evidence-manifest": aggregate_manifest,
        "aggregate-summary": summary,
    }
    document = document_by_name[contract_name]
    document["forged-top-level-key"] = "forged"

    def validate_document(item: dict[str, object]) -> None:
        if contract_name == "execution-batch-manifest":
            validate_ci_validation_execution_batch_manifest(
                item,
                plan=plan,
                authorizing=False,
            )
        elif contract_name == "batch-evidence-bundle":
            validate_ci_validation_batch_evidence_bundle(
                item,
                plan=plan,
                execution_batch_manifest=manifest,
            )
        elif contract_name == "aggregate-evidence-manifest":
            validate_ci_validation_aggregate_evidence_manifest(
                item,
                plan=plan,
                execution_batch_manifest=manifest,
            )
        else:
            validate_ci_validation_aggregate_summary(
                item,
                plan=plan,
                aggregate_evidence_manifest=aggregate_manifest,
                admitted_batch_evidence_bundles=[bundle],
                execution_batch_manifest=manifest,
                request=_request_document(),
                changed_files_snapshot=_changed_files_snapshot_document(),
            )

    with pytest.raises(ContractValidationError):
        validate_document(document)


def test_execution_batch_manifest_accepts_exact_plan_batch_dag() -> None:
    """Batch DAG dependencies mirror frozen plan selector dependencies."""
    plan = _plan()
    _add_dependent_work_group(plan)
    manifest = freeze_ci_validation_execution_batch_manifest(
        plan=plan,
        **_authorizing_context_kwargs(),
        batches=_dependent_batches(plan),
        budget=_budget(2),
        created_at=CREATED_AT,
    )

    validate_ci_validation_execution_batch_manifest(
        manifest,
        plan=plan,
        authorizing=False,
    )


def test_execution_batch_manifest_rejects_missing_plan_batch_edge() -> None:
    """Cross-batch selector dependencies require a manifest batch edge."""
    plan = _plan()
    _add_dependent_work_group(plan)
    batches = _dependent_batches(plan)
    batches[1]["depends-on-batches"] = []

    with pytest.raises(ContractValidationError):
        freeze_ci_validation_execution_batch_manifest(
            plan=plan,
            batches=batches,
            budget=_budget(2),
            created_at=CREATED_AT,
        )


def test_execution_batch_manifest_rejects_stale_plan_batch_edge() -> None:
    """Every manifest batch edge must come from a selector dependency."""
    plan = _plan()
    _add_dependent_work_group(plan)
    batches = _dependent_batches(plan)
    batches[1]["depends-on-batches"] = []
    cast("list[object]", batches[0]["depends-on-batches"]).append(
        batches[1]["batch-id"]
    )

    with pytest.raises(ContractValidationError):
        freeze_ci_validation_execution_batch_manifest(
            plan=plan,
            batches=batches,
            budget=_budget(2),
            created_at=CREATED_AT,
        )


def test_execution_batch_manifest_rejects_reversed_same_batch_dependency() -> (
    None
):
    """In-batch selector dependencies must put producers before consumers."""
    plan = _plan()
    _add_dependent_work_group(plan)
    batches = _dependent_batches(plan)
    first = batches[0]
    second_selector = cast(
        "list[dict[str, object]]", batches[1]["ordered-selectors"]
    )[0]
    cast("list[dict[str, object]]", first["ordered-selectors"]).insert(
        0, second_selector
    )
    cast("list[dict[str, object]]", first["ordered-selectors"])[1][
        "selector-index"
    ] = 1
    first["depends-on-batches"] = []
    batches = [first]

    with pytest.raises(ContractValidationError):
        freeze_ci_validation_execution_batch_manifest(
            plan=plan,
            batches=batches,
            budget=_budget(1),
            created_at=CREATED_AT,
        )


@pytest.mark.parametrize("field", ["plan-id", "plan-digest"])
def test_execution_batch_manifest_rejects_direct_plan_identity_mismatch(
    field: str,
) -> None:
    """Execution manifests bind plan identity even when mutated directly."""
    plan = _plan()
    manifest = _manifest(plan)
    manifest[field] = "other-plan" if field == "plan-id" else "0" * 64

    with pytest.raises(ContractValidationError):
        validate_ci_validation_execution_batch_manifest(
            manifest,
            plan=plan,
            authorizing=False,
        )


@pytest.mark.parametrize(
    "mutation",
    ["ecosystem", "profile-digest", "batch-id", "release-fields"],
)
def test_execution_batch_manifest_recomputes_plan_bound_batch_identity(
    mutation: str,
) -> None:
    """Plan-bound validation rejects forged compatibility and batch IDs."""
    plan = _plan()
    manifest = _manifest(plan)
    batch = cast("list[dict[str, object]]", manifest["batches"])[0]
    profile = cast("dict[str, object]", batch["compatibility-profile"])
    if mutation == "ecosystem":
        profile["ecosystem"] = "dotnet"
    elif mutation == "profile-digest":
        profile["execution-profile-digest"] = "0" * 64
    elif mutation == "batch-id":
        batch["batch-id"] = "batch-forged-gate"
        batch["expected-batch-evidence-bundle-ref"] = (
            ci_validation_batch_evidence_bundle_artifact_ref(
                run_id=RUN_ID,
                run_attempt=RUN_ATTEMPT,
                batch_id="batch-forged-gate",
            )
        )
    else:
        profile["release-shaped-profile"] = "release-forged"
        profile["release-shaped-profile-digest"] = "1" * 64

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_execution_batch_manifest(
            manifest,
            plan=plan,
            authorizing=False,
        )
    assert any(
        issue.path.endswith(("compatibility-profile", "batch-id"))
        for issue in error.value.issues
    )


def test_execution_batch_manifest_rejects_forged_repartitioning() -> None:
    """Plan-bound validation recomputes exact materializer partitions."""
    plan = _plan()
    _add_extra_work_group(
        plan,
        work_group_id="wg-extra-python-gate",
        evidence_id="evidence-extra-python-gate",
    )
    manifest = _manifest(_plan())
    base_batch = cast("list[dict[str, object]]", manifest["batches"])[0]
    first = _retarget_batch(
        base_batch,
        batch_id="unused",
        work_group_id="wg-python-gate",
        evidence_id="evidence-python-gate",
        plan=plan,
    )
    second = _retarget_batch(
        base_batch,
        batch_id="unused",
        work_group_id="wg-extra-python-gate",
        evidence_id="evidence-extra-python-gate",
        plan=plan,
    )
    manifest["plan-digest"] = plan["plan-digest"]
    manifest["batches"] = sorted(
        [first, second],
        key=lambda batch: cast("str", batch["batch-id"]),
    )
    manifest["budget"] = _budget(2)

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_execution_batch_manifest(
            manifest,
            plan=plan,
            authorizing=False,
        )

    assert any(issue.path == "$.batches" for issue in error.value.issues)


def test_execution_batch_manifest_rejects_malformed_release_group() -> None:
    """Malformed selected release-shaped groups report validation issues."""
    plan = _plan()
    group = next(
        item
        for item in cast("list[dict[str, object]]", plan["work-groups"])
        if item["kind"] != "evidence-aggregation"
    )
    group["kind"] = "release-shaped-artifact"
    del group["coverage-target"]
    plan["plan-digest"] = ci_validation_plan_digest(plan)
    manifest = _manifest(_plan())
    manifest["plan-digest"] = plan["plan-digest"]

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_execution_batch_manifest(
            manifest,
            plan=plan,
            authorizing=False,
        )

    assert any(
        issue.path.endswith(".coverage-target") for issue in error.value.issues
    )


def test_manifest_rejects_release_group_without_obligation() -> None:
    """Release-shaped groups must have artifact and receipt obligations."""
    plan = _plan()
    group = next(
        item
        for item in cast("list[dict[str, object]]", plan["work-groups"])
        if item["kind"] != "evidence-aggregation"
    )
    group["kind"] = "release-shaped-artifact"
    group["coverage-target"] = {
        "type": "artifact-obligation",
        "id": "artifact-missing",
    }
    cast("dict[str, object]", group["expected-evidence"])["category"] = (
        "release-shaped-artifact"
    )
    evidence = cast("list[dict[str, object]]", plan["evidence-expectations"])[0]
    evidence["category"] = "release-shaped-artifact"
    evidence["planned-capabilities"] = None
    evidence["coverage-target"] = group["coverage-target"]
    plan["plan-digest"] = ci_validation_plan_digest(plan)
    manifest = _manifest(_plan())
    manifest["plan-digest"] = plan["plan-digest"]

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_execution_batch_manifest(
            manifest,
            plan=plan,
            authorizing=False,
        )

    assert any(
        "release-shaped groups require one artifact obligation" in issue.message
        for issue in error.value.issues
    )


def test_execution_batch_manifest_rejects_execution_data_in_slot() -> None:
    """Expected evidence slots are pre-execution shape only."""
    plan = _plan()
    batch = _batch(plan)
    selector = cast("list[dict[str, Any]]", batch["ordered-selectors"])[0]
    cast("dict[str, object]", selector["expected-evidence-slot"])["outcome"] = (
        "success"
    )

    with pytest.raises(ContractValidationError):
        freeze_ci_validation_execution_batch_manifest(
            plan=plan,
            batches=[batch],
            budget=_budget(1),
            created_at=CREATED_AT,
        )


def test_execution_batch_manifest_rejects_slot_plan_mismatch() -> None:
    """Frozen selector slots must match the selected plan expectation."""
    plan = _plan()
    batch = _batch(plan)
    selector = cast("list[dict[str, Any]]", batch["ordered-selectors"])[0]
    cast("dict[str, object]", selector["expected-evidence-slot"])[
        "coverage-target"
    ] = {"type": "none", "id": None}

    with pytest.raises(ContractValidationError):
        freeze_ci_validation_execution_batch_manifest(
            plan=plan,
            batches=[batch],
            budget=_budget(1),
            created_at=CREATED_AT,
        )


def test_batch_evidence_bundle_freezes_and_validates() -> None:
    """Batch bundles carry per-selector rows and validation-only proof."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    manifest_digest = ci_validation_execution_batch_manifest_payload_digest(
        manifest,
    )

    validate_ci_validation_batch_evidence_bundle(
        bundle,
        plan=plan,
        execution_batch_manifest=manifest,
        **_authorizing_context_kwargs(),
    )

    assert bundle[
        "artifact-ref"
    ] == ci_validation_batch_evidence_bundle_artifact_ref(
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        batch_id=_BATCH_ID,
    )
    assert bundle["bundle-id"] == ci_validation_batch_evidence_bundle_id(
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        batch_id=_BATCH_ID,
        execution_batch_manifest_digest=manifest_digest,
        artifact_ref=cast("str", bundle["artifact-ref"]),
    )


def test_batch_bundle_rejects_malformed_plan_without_keyerror() -> None:
    """Malformed plan context fails closed through contract validation."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    malformed_plan = deepcopy(plan)
    del malformed_plan["affected-range"]
    malformed_plan["plan-digest"] = ci_validation_plan_digest(malformed_plan)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_batch_evidence_bundle(
            bundle,
            plan=malformed_plan,
            execution_batch_manifest=manifest,
            **_authorizing_context_kwargs(),
        )

    assert any(
        issue.path == "plan.affected-range" for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("artifact-ref", "bad ref"),
        ("content-digest", "not-a-sha256"),
    ],
)
def test_batch_bundle_rejects_malformed_manifest_claim_without_manifest(
    field: str,
    value: object,
) -> None:
    """Standalone bundle validation enforces nested manifest claim shape."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    cast("dict[str, object]", bundle["execution-batch-manifest"])[field] = value

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_batch_evidence_bundle(bundle, plan=plan)

    assert any(
        issue.path == f"$.execution-batch-manifest.{field}"
        for issue in exc_info.value.issues
    )


def test_batch_bundle_rejects_standalone_unverified_nested_provenance() -> None:
    """Bundle artifact-ref and bundle-id need the authoritative manifest."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    bundle["artifact-ref"] = ci_validation_batch_evidence_bundle_artifact_ref(
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        batch_id="batch-forged-gate",
    )
    bundle["bundle-id"] = "bundle-" + ("0" * 64)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_batch_evidence_bundle(bundle, plan=plan)

    assert any(
        issue.path == "$.execution-batch-manifest"
        and "authoritative" in issue.message
        for issue in exc_info.value.issues
    )


def test_batch_bundle_rejects_non_object_batch_without_manifest() -> None:
    """Standalone bundle validation enforces batch projection shape."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    bundle["batch"] = _BATCH_ID

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_batch_evidence_bundle(bundle, plan=plan)

    assert any(issue.path == "$.batch" for issue in exc_info.value.issues)


@pytest.mark.parametrize("detail", _OBSOLETE_FINAL_EVIDENCE_DETAILS)
def test_batch_evidence_bundle_rejects_obsolete_final_evidence_details(
    detail: str,
) -> None:
    """G1 batch diagnostics reject obsolete final evidence detail spelling."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    cast("list[dict[str, object]]", bundle["batch-diagnostics"]).append(
        _diagnostic(
            "obsolete-final-evidence",
            code="final-evidence-failure",
            detail=detail,
            severity="fail-closed",
            verdict_effect="fail-closed",
        )
    )

    with pytest.raises(ContractValidationError):
        validate_ci_validation_batch_evidence_bundle(
            bundle,
            plan=plan,
            execution_batch_manifest=manifest,
            **_authorizing_context_kwargs(),
        )


@pytest.mark.parametrize("detail", _OBSOLETE_FINAL_EVIDENCE_DETAILS)
def test_batch_bundle_schema_diagnostics_reject_obsolete_final_details(
    detail: str,
) -> None:
    """G1 bundle schema diagnostics use the same final detail allowlist."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    cast("list[dict[str, object]]", bundle["schema-diagnostics"]).append(
        _schema_diagnostic("obsolete-final-evidence", detail=detail)
    )

    with pytest.raises(ContractValidationError):
        validate_ci_validation_batch_evidence_bundle(
            bundle,
            plan=plan,
            execution_batch_manifest=manifest,
            **_authorizing_context_kwargs(),
        )


@pytest.mark.parametrize("detail", _OBSOLETE_FINAL_EVIDENCE_DETAILS)
def test_execution_manifest_schema_rejects_obsolete_final_details(
    detail: str,
) -> None:
    """G1 execution manifest schema diagnostics reject obsolete details."""
    plan = _plan()
    manifest = _manifest(plan)
    cast("list[dict[str, object]]", manifest["schema-diagnostics"]).append(
        _schema_diagnostic("obsolete-final-evidence", detail=detail)
    )

    with pytest.raises(ContractValidationError):
        validate_ci_validation_execution_batch_manifest(
            manifest,
            plan=plan,
            authorizing=False,
        )


@pytest.mark.parametrize("detail", _OBSOLETE_FINAL_EVIDENCE_DETAILS)
def test_aggregate_manifest_schema_diagnostics_reject_obsolete_final_details(
    detail: str,
) -> None:
    """G1 aggregate manifest schema diagnostics reject obsolete details."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    cast(
        "list[dict[str, object]]",
        aggregate_manifest["schema-diagnostics"],
    ).append(_schema_diagnostic("obsolete-final-evidence", detail=detail))

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
            changed_files_snapshot=_changed_files_snapshot_document(),
        )


@pytest.mark.parametrize("detail", _OBSOLETE_FINAL_EVIDENCE_DETAILS)
def test_aggregate_summary_schema_diagnostics_reject_obsolete_final_details(
    detail: str,
) -> None:
    """G1 summary schema diagnostics use the same final detail allowlist."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    cast("list[dict[str, object]]", summary["schema-diagnostics"]).append(
        _schema_diagnostic("obsolete-final-evidence", detail=detail)
    )

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_batch_evidence_bundle_rejects_rewritten_slot_digest() -> None:
    """Selector rows must fill the manifest-bound evidence slot."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    cast("list[dict[str, object]]", bundle["selector-results"])[0][
        "expected-evidence-slot-digest"
    ] = "0" * 64

    with pytest.raises(ContractValidationError):
        validate_ci_validation_batch_evidence_bundle(
            bundle,
            plan=plan,
            execution_batch_manifest=manifest,
            **_authorizing_context_kwargs(),
        )


def test_batch_evidence_bundle_rejects_unverified_execution_tree() -> None:
    """Execution tree proof must be verified at the batch boundary."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    cast("dict[str, object]", bundle["execution-tree"])["verified"] = False

    with pytest.raises(ContractValidationError):
        validate_ci_validation_batch_evidence_bundle(
            bundle,
            plan=plan,
            execution_batch_manifest=manifest,
            **_authorizing_context_kwargs(),
        )


def test_batch_evidence_bundle_rejects_execution_tree_commit_mismatch() -> None:
    """Execution tree observation is bound to the validation tree commit."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    cast("dict[str, object]", bundle["execution-tree"])[
        "observed-commit-sha"
    ] = "9" * 40

    with pytest.raises(ContractValidationError):
        validate_ci_validation_batch_evidence_bundle(
            bundle,
            plan=plan,
            execution_batch_manifest=manifest,
            **_authorizing_context_kwargs(),
        )


@pytest.mark.parametrize(
    "diagnostic",
    [
        {"diagnostic-id": "legacy-only"},
        _diagnostic("bad-code", code="unregistered-code"),
        _diagnostic("bad-detail", code="invalid-plan", detail="build"),
        {
            **_diagnostic("missing-source"),
            "source": {"type": "aggregation"},
        },
        {
            **_diagnostic("extra-source-key"),
            "source": {"type": "aggregation", "id": None, "extra": "forged"},
        },
    ],
)
def test_batch_evidence_bundle_rejects_malformed_diagnostics(
    diagnostic: dict[str, object],
) -> None:
    """Batch diagnostics must be complete registered diagnostic records."""
    plan = _plan()
    manifest = _manifest(plan)

    with pytest.raises(ContractValidationError):
        freeze_ci_validation_batch_evidence_bundle(
            plan=plan,
            execution_batch_manifest=manifest,
            batch_id=_BATCH_ID,
            selector_results=[_selector_result(plan, manifest)],
            writer=_writer_for_batch(manifest, _BATCH_ID),
            execution_tree={
                "observed-commit-sha": TREE_SHA,
                "source": "execution-batch-boundary",
                "verified": True,
            },
            started_at=CREATED_AT,
            completed_at=CREATED_AT,
            created_at=CREATED_AT,
            batch_diagnostics=[diagnostic],
            **_authorizing_context_kwargs(),
        )


@pytest.mark.parametrize("field", ["source", "severity", "verdict-effect"])
def test_batch_evidence_bundle_rejects_missing_required_diagnostic_fields(
    field: str,
) -> None:
    """Batch diagnostics independently require source, severity, and effect."""
    plan = _plan()
    manifest = _manifest(plan)
    diagnostic = _diagnostic("inadmissible-batch-evidence")
    del diagnostic[field]

    with pytest.raises(ContractValidationError):
        freeze_ci_validation_batch_evidence_bundle(
            plan=plan,
            execution_batch_manifest=manifest,
            batch_id=_BATCH_ID,
            selector_results=[_selector_result(plan, manifest)],
            writer=_writer_for_batch(manifest, _BATCH_ID),
            execution_tree={
                "observed-commit-sha": TREE_SHA,
                "source": "execution-batch-boundary",
                "verified": True,
            },
            started_at=CREATED_AT,
            completed_at=CREATED_AT,
            created_at=CREATED_AT,
            batch_diagnostics=[diagnostic],
            **_authorizing_context_kwargs(),
        )


def test_batch_evidence_bundle_accepts_canonical_diagnostics() -> None:
    """Canonical batch diagnostics are accepted across nested evidence rows."""
    plan = _plan()
    manifest = _manifest(plan)
    result = _selector_result(plan, manifest)
    result["diagnostics"] = [
        _diagnostic(
            "selector-build", code="validation-work-failed", detail="build"
        )
    ]

    bundle = freeze_ci_validation_batch_evidence_bundle(
        plan=plan,
        execution_batch_manifest=manifest,
        batch_id=_BATCH_ID,
        selector_results=[result],
        writer=_writer_for_batch(manifest, _BATCH_ID),
        execution_tree={
            "observed-commit-sha": TREE_SHA,
            "source": "execution-batch-boundary",
            "verified": True,
        },
        started_at=CREATED_AT,
        completed_at=CREATED_AT,
        created_at=CREATED_AT,
        batch_diagnostics=[
            _diagnostic(
                "batch-inadmissible",
                code="inadmissible-batch-evidence",
                detail="malformed-bundle",
            )
        ],
        **_authorizing_context_kwargs(),
    )

    validate_ci_validation_batch_evidence_bundle(
        bundle,
        plan=plan,
        execution_batch_manifest=manifest,
        **_authorizing_context_kwargs(),
    )


@pytest.mark.parametrize(
    ("mutator", "expected_path"),
    [
        (
            lambda result: result.pop("dependency-results"),
            ".dependency-results",
        ),
        (
            lambda result: result.__setitem__("dependency-results", []),
            ".dependency-results",
        ),
        (
            lambda result: cast(
                "list[dict[str, object]]", result["dependency-results"]
            ).append(
                {
                    "work-group-id": "wg-forged-gate",
                    "source-batch-id": "batch-forged-gate",
                    "outcome": "satisfied",
                    "admitted-for-gating": True,
                }
            ),
            ".dependency-results",
        ),
        (
            lambda result: cast(
                "dict[str, object]",
                cast("list[dict[str, object]]", result["dependency-results"])[
                    0
                ],
            ).__setitem__("source-batch-id", "batch-forged-gate"),
            ".source-batch-id",
        ),
        (
            lambda result: cast(
                "dict[str, object]",
                cast("list[dict[str, object]]", result["dependency-results"])[
                    0
                ],
            ).update({"admitted-for-gating": False}),
            ".admitted-for-gating",
        ),
    ],
)
def test_batch_evidence_bundle_rejects_dependency_result_mismatches(
    mutator: Callable[[dict[str, object]], None],
    expected_path: str,
) -> None:
    """Selector dependency results must exactly bind batch topology."""
    plan, manifest, base_bundle, bundle = _dependent_bundle_fixture()
    result = cast("list[dict[str, object]]", bundle["selector-results"])[0]
    mutator(result)

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_batch_evidence_bundle(
            bundle,
            plan=plan,
            execution_batch_manifest=manifest,
            dependency_evidence_bundles=[base_bundle],
            **_authorizing_context_kwargs(),
        )
    assert any(expected_path in issue.path for issue in error.value.issues)


def test_bundle_rejects_dependency_block_without_diagnostic() -> None:
    """Dependency-blocked selector skips must carry a skipped diagnostic."""
    plan, manifest, _base_bundle, bundle = _dependent_bundle_fixture()
    result = cast("list[dict[str, object]]", bundle["selector-results"])[0]
    dependency = cast(
        "dict[str, object]",
        cast("list[dict[str, object]]", result["dependency-results"])[0],
    )
    dependency["outcome"] = "failed"
    dependency["admitted-for-gating"] = False
    result["outcome"] = "skipped"
    result["skip-reason"] = "dependency-blocked"
    for capability_result in cast(
        "list[dict[str, object]]",
        cast("dict[str, object]", result["evidence"])["capability-results"],
    ):
        capability_result["outcome"] = "skipped"

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_batch_evidence_bundle(
            bundle,
            plan=plan,
            execution_batch_manifest=manifest,
            **_authorizing_context_kwargs(),
        )
    assert any(
        issue.path.endswith(".diagnostics") for issue in error.value.issues
    )


def test_batch_evidence_bundle_rejects_missing_dependency_without_evidence() -> (  # noqa: E501
    None
):
    """Missing cross-batch rows still require upstream evidence."""
    plan, manifest, _base_bundle, bundle = _dependent_bundle_fixture()
    result = cast("list[dict[str, object]]", bundle["selector-results"])[0]
    dependency = cast(
        "dict[str, object]",
        cast("list[dict[str, object]]", result["dependency-results"])[0],
    )
    dependency["outcome"] = "missing"
    dependency["admitted-for-gating"] = False
    result["outcome"] = "skipped"
    result["skip-reason"] = "dependency-blocked"
    result["diagnostics"] = [
        _diagnostic(
            "dependency-blocked",
            code="validation-work-skipped",
            detail="dependency-blocked",
        )
    ]
    for capability_result in cast(
        "list[dict[str, object]]",
        cast("dict[str, object]", result["evidence"])["capability-results"],
    ):
        capability_result["outcome"] = "skipped"

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_batch_evidence_bundle(
            bundle,
            plan=plan,
            execution_batch_manifest=manifest,
            **_authorizing_context_kwargs(),
        )

    assert any(
        "requires authoritative upstream bundle evidence" in issue.message
        for issue in error.value.issues
    )


def test_batch_evidence_bundle_rejects_failed_non_admitted_dependency() -> None:
    """Cross-batch failed rows cannot spoof a fail-closed dependency block."""
    plan, manifest, _base_bundle, bundle = _dependent_bundle_fixture()
    result = cast("list[dict[str, object]]", bundle["selector-results"])[0]
    dependency = cast(
        "dict[str, object]",
        cast("list[dict[str, object]]", result["dependency-results"])[0],
    )
    dependency["outcome"] = "failed"
    dependency["admitted-for-gating"] = False
    result["outcome"] = "skipped"
    result["skip-reason"] = "dependency-blocked"
    result["diagnostics"] = [
        _diagnostic(
            "dependency-blocked",
            code="validation-work-skipped",
            detail="dependency-blocked",
        )
    ]
    for capability_result in cast(
        "list[dict[str, object]]",
        cast("dict[str, object]", result["evidence"])["capability-results"],
    ):
        capability_result["outcome"] = "skipped"

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_batch_evidence_bundle(
            bundle,
            plan=plan,
            execution_batch_manifest=manifest,
            **_authorizing_context_kwargs(),
        )

    assert any(
        issue.path.endswith(".dependency-results[0].admitted-for-gating")
        for issue in error.value.issues
    )


def test_batch_evidence_bundle_accepts_admitted_failed_dependency() -> None:
    """Admitted failed dependencies do not force dependency-blocked skips."""
    plan, manifest, base_bundle, bundle, _aggregate_manifest, _summary = (
        _dependent_admitted_fixture()
    )
    failed_base_bundle = _failed_bundle(base_bundle)
    result = cast("list[dict[str, object]]", bundle["selector-results"])[0]
    dependency = cast(
        "dict[str, object]",
        cast("list[dict[str, object]]", result["dependency-results"])[0],
    )
    dependency["outcome"] = "failed"
    dependency["admitted-for-gating"] = True

    validate_ci_validation_batch_evidence_bundle(
        bundle,
        plan=plan,
        execution_batch_manifest=manifest,
        dependency_evidence_bundles=[failed_base_bundle],
        **_authorizing_context_kwargs(),
    )


def test_aggregate_summary_accepts_satisfied_admitted_dependency() -> None:
    """Dependency rows match the actual admitted upstream selector result."""
    (
        plan,
        manifest,
        base_bundle,
        dependent_bundle,
        aggregate_manifest,
        summary,
    ) = _dependent_admitted_fixture()

    validate_ci_validation_aggregate_summary(
        summary,
        plan=plan,
        aggregate_evidence_manifest=aggregate_manifest,
        admitted_batch_evidence_bundles=[base_bundle, dependent_bundle],
        execution_batch_manifest=manifest,
        request=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )


def test_aggregate_summary_accepts_non_topological_admitted_bundles() -> None:
    """Admitted bundles validate by dependency topology, not input order."""
    (
        plan,
        manifest,
        base_bundle,
        dependent_bundle,
        aggregate_manifest,
        summary,
    ) = _dependent_admitted_fixture()

    validate_ci_validation_aggregate_summary(
        summary,
        plan=plan,
        aggregate_evidence_manifest=aggregate_manifest,
        admitted_batch_evidence_bundles=[dependent_bundle, base_bundle],
        execution_batch_manifest=manifest,
        request=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )


def test_admitted_bundle_topology_errors_include_argument_path() -> None:
    """No-progress admitted bundle diagnostics identify the API argument."""
    (
        plan,
        manifest,
        _base_bundle,
        dependent_bundle,
        _aggregate_manifest,
        _summary,
    ) = _dependent_admitted_fixture()
    issues: list[Any] = []

    _validate_admitted_bundles_topologically(
        [dependent_bundle],
        plan=plan,
        request=_request_document(),
        execution_batch_manifest=manifest,
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
        envelope=_envelope(
            dependent_bundle,
            CiValidationKind.BATCH_EVIDENCE_BUNDLE,
        ),
        issues=issues,
    )

    expected_path = (
        "admitted_batch_evidence_bundles[0]"
        ".selector-results[0].dependency-results[0]"
    )
    assert any(issue.path == expected_path for issue in issues)


def test_batch_bundle_freezer_rejects_spoofed_cross_batch_dependency() -> None:
    """Cross-batch admitted dependencies require upstream bundle evidence."""
    plan = _plan()
    _add_dependent_work_group(plan)
    manifest = freeze_ci_validation_execution_batch_manifest(
        plan=plan,
        **_authorizing_context_kwargs(),
        batches=_dependent_batches(plan),
        budget=_budget(2),
        created_at=CREATED_AT,
    )
    base_batch_id, dependent_batch_id = _dependent_batch_ids(manifest)
    result = _selector_result(plan, manifest, dependent_batch_id)
    result["dependency-results"] = [
        {
            "work-group-id": "wg-python-gate",
            "source-batch-id": base_batch_id,
            "outcome": "satisfied",
            "admitted-for-gating": True,
        }
    ]

    with pytest.raises(ContractValidationError) as error:
        freeze_ci_validation_batch_evidence_bundle(
            plan=plan,
            execution_batch_manifest=manifest,
            batch_id=dependent_batch_id,
            selector_results=[result],
            writer=_writer_for_batch(manifest, dependent_batch_id),
            execution_tree={
                "observed-commit-sha": TREE_SHA,
                "source": "execution-batch-boundary",
                "verified": True,
            },
            started_at=CREATED_AT,
            completed_at=CREATED_AT,
            created_at=CREATED_AT,
            **_authorizing_context_kwargs(),
        )

    assert any(
        "requires authoritative upstream bundle evidence" in issue.message
        for issue in error.value.issues
    )


def test_batch_bundle_validator_rejects_spoofed_cross_batch_dependency() -> (
    None
):
    """Validated upstream evidence, not payload claims, authorizes admission."""
    (
        plan,
        manifest,
        base_bundle,
        dependent_bundle,
        _aggregate_manifest,
        _summary,
    ) = _dependent_admitted_fixture()
    failed_base_bundle = _failed_bundle(base_bundle)

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_batch_evidence_bundle(
            dependent_bundle,
            plan=plan,
            execution_batch_manifest=manifest,
            dependency_evidence_bundles=[failed_base_bundle],
            **_authorizing_context_kwargs(),
        )

    assert any(
        issue.path.endswith(".dependency-results[0].outcome")
        for issue in error.value.issues
    )


def test_batch_bundle_prefixes_invalid_dependency_bundle_paths() -> None:
    """Nested dependency bundle diagnostics use valid prefixed JSON paths."""
    (
        plan,
        manifest,
        base_bundle,
        dependent_bundle,
        _aggregate_manifest,
        _summary,
    ) = _dependent_admitted_fixture()
    invalid_base_bundle = deepcopy(base_bundle)
    selector = cast(
        "list[dict[str, object]]",
        invalid_base_bundle["selector-results"],
    )[0]
    selector["dependency-results"] = [
        {
            "work-group-id": "wg-forged",
            "source-batch-id": "batch-forged",
            "outcome": "satisfied",
            "admitted-for-gating": True,
        }
    ]

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_batch_evidence_bundle(
            dependent_bundle,
            plan=plan,
            execution_batch_manifest=manifest,
            dependency_evidence_bundles=[invalid_base_bundle],
            **_authorizing_context_kwargs(),
        )

    assert any(
        issue.path
        == (
            "dependency_evidence_bundles[0]"
            ".selector-results[0].dependency-results[0]"
        )
        for issue in error.value.issues
    )
    assert all(
        "$." not in issue.path
        for issue in error.value.issues
        if issue.path.startswith("dependency_evidence_bundles[")
    )


def test_public_validator_rejects_admitted_cross_batch_dependency_no_evidence() -> (  # noqa: E501
    None
):
    """Public validation requires evidence for admitted dependencies."""
    (
        plan,
        manifest,
        _base_bundle,
        dependent_bundle,
        _aggregate_manifest,
        _summary,
    ) = _dependent_admitted_fixture()

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_batch_evidence_bundle(
            dependent_bundle,
            plan=plan,
            execution_batch_manifest=manifest,
            dependency_evidence_bundles=[],
            **_authorizing_context_kwargs(),
        )

    assert any(
        "requires authoritative upstream bundle evidence" in issue.message
        for issue in error.value.issues
    )


def test_non_authorizing_bundle_allows_cross_batch_dependency_without_evidence() -> (  # noqa: E501
    None
):
    """G2 validation keeps topology checks without gating evidence."""
    (
        plan,
        manifest,
        _base_bundle,
        dependent_bundle,
        _aggregate_manifest,
        _summary,
    ) = _dependent_admitted_fixture()

    validate_ci_validation_batch_evidence_bundle(
        dependent_bundle,
        plan=plan,
        execution_batch_manifest=manifest,
    )


def test_public_batch_validator_rejects_dependency_evidence_escape_hatch() -> (
    None
):
    """Public bundle validation always enforces dependency evidence."""
    (
        plan,
        manifest,
        _base_bundle,
        dependent_bundle,
        _aggregate_manifest,
        _summary,
    ) = _dependent_admitted_fixture()
    validator = cast("Any", validate_ci_validation_batch_evidence_bundle)

    with pytest.raises(TypeError, match="_require_dependency_evidence"):
        validator(
            dependent_bundle,
            plan=plan,
            execution_batch_manifest=manifest,
            dependency_evidence_bundles=[],
            _require_dependency_evidence=False,
            **_authorizing_context_kwargs(),
        )


def test_batch_evidence_rejects_duplicate_authoritative_upstream() -> None:
    """Duplicate upstream authority fails closed instead of overwriting."""
    (
        plan,
        manifest,
        base_bundle,
        dependent_bundle,
        _aggregate_manifest,
        _summary,
    ) = _dependent_admitted_fixture()
    failed_base_bundle = _failed_bundle(base_bundle)

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_batch_evidence_bundle(
            dependent_bundle,
            plan=plan,
            execution_batch_manifest=manifest,
            dependency_evidence_bundles=[failed_base_bundle, base_bundle],
            **_authorizing_context_kwargs(),
        )

    assert any(
        "duplicate authoritative upstream bundle evidence" in issue.message
        for issue in error.value.issues
    )


def test_batch_evidence_requires_transitive_dependency_evidence() -> None:
    """C cannot launder B evidence unless B is validated against A."""
    plan = _plan()
    _add_transitive_work_group(plan)
    materialization = materialize_ci_validation_execution_batches(
        plan=plan,
        **_authorizing_context_kwargs(),
        created_at=CREATED_AT,
        execution_workflow="CI Validation",
    )
    manifest = cast("dict[str, object]", materialization.manifest)
    batch_by_group: dict[str, str] = {}
    for batch in cast("list[dict[str, object]]", manifest["batches"]):
        selector = cast("list[dict[str, object]]", batch["ordered-selectors"])[
            0
        ]
        batch_by_group[cast("str", selector["work-group-id"])] = cast(
            "str", batch["batch-id"]
        )
    base_bundle = _bundle_for_batch(
        plan, manifest, batch_by_group["wg-python-gate"]
    )
    dependent_result = _selector_result(
        plan, manifest, batch_by_group["wg-dependent-gate"]
    )
    dependent_result["dependency-results"] = [
        {
            "work-group-id": "wg-python-gate",
            "source-batch-id": batch_by_group["wg-python-gate"],
            "outcome": "satisfied",
            "admitted-for-gating": True,
        }
    ]
    dependent_bundle = _bundle_for_batch(
        plan,
        manifest,
        batch_by_group["wg-dependent-gate"],
        dependent_result,
        dependency_evidence_bundles=[base_bundle],
    )
    transitive_result = _selector_result(
        plan, manifest, batch_by_group["wg-transitive-gate"]
    )
    transitive_result["dependency-results"] = [
        {
            "work-group-id": "wg-dependent-gate",
            "source-batch-id": batch_by_group["wg-dependent-gate"],
            "outcome": "satisfied",
            "admitted-for-gating": True,
        }
    ]
    transitive_bundle = _bundle_for_batch(
        plan,
        manifest,
        batch_by_group["wg-transitive-gate"],
        transitive_result,
        dependency_evidence_bundles=[base_bundle, dependent_bundle],
    )

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_batch_evidence_bundle(
            transitive_bundle,
            plan=plan,
            execution_batch_manifest=manifest,
            dependency_evidence_bundles=[dependent_bundle],
            **_authorizing_context_kwargs(),
        )
    assert any(
        "requires authoritative upstream bundle evidence" in issue.message
        for issue in error.value.issues
    )

    validate_ci_validation_batch_evidence_bundle(
        transitive_bundle,
        plan=plan,
        execution_batch_manifest=manifest,
        dependency_evidence_bundles=[base_bundle, dependent_bundle],
        **_authorizing_context_kwargs(),
    )


def test_public_validator_rejects_dependency_rows_without_manifest_context() -> (  # noqa: E501
    None
):
    """Dependency rows fail closed without authoritative batch context."""
    (
        plan,
        _manifest,
        base_bundle,
        dependent_bundle,
        _aggregate_manifest,
        _summary,
    ) = _dependent_admitted_fixture()

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_batch_evidence_bundle(
            dependent_bundle,
            plan=plan,
            dependency_evidence_bundles=[base_bundle],
            **_authorizing_context_kwargs(),
        )

    assert any(
        "requires authoritative execution-batch manifest" in issue.message
        for issue in error.value.issues
    )


def test_summary_rejects_satisfied_dependency_when_upstream_failed() -> None:
    """Dependency rows cannot claim satisfied for failed upstream evidence."""
    (
        plan,
        manifest,
        base_bundle,
        dependent_bundle,
        aggregate_manifest,
        summary,
    ) = _dependent_admitted_fixture()
    failed_base_bundle = _failed_bundle(base_bundle)

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[
                failed_base_bundle,
                dependent_bundle,
            ],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )
    assert any(
        issue.path.endswith(".dependency-results[0].outcome")
        for issue in error.value.issues
    )


def test_summary_rejects_satisfied_dependency_when_upstream_missing() -> None:
    """Dependency rows cannot claim satisfied for missing upstream evidence."""
    (
        plan,
        manifest,
        _base_bundle,
        dependent_bundle,
        aggregate_manifest,
        summary,
    ) = _dependent_admitted_fixture()

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[dependent_bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )
    assert any(
        issue.path.endswith(".dependency-results[0].outcome")
        for issue in error.value.issues
    )


def test_summary_does_not_block_admitted_failed_dependency() -> None:
    """Blocking-failure upstream evidence remains admitted for dependencies."""
    (
        plan,
        manifest,
        base_bundle,
        dependent_bundle,
        _aggregate_manifest,
        summary,
    ) = _dependent_admitted_fixture()
    failed_base_bundle = _failed_bundle(base_bundle)
    result = cast(
        "dict[str, object]",
        cast("list[dict[str, object]]", dependent_bundle["selector-results"])[
            0
        ],
    )
    dependency = cast(
        "dict[str, object]",
        cast("list[dict[str, object]]", result["dependency-results"])[0],
    )
    dependency["outcome"] = "failed"
    dependency["admitted-for-gating"] = True
    aggregate_manifest = _aggregate_evidence_manifest_for_bundles(
        plan,
        manifest,
        [failed_base_bundle, dependent_bundle],
    )
    _refresh_summary_manifest_digest(summary, aggregate_manifest)

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[
                failed_base_bundle,
                dependent_bundle,
            ],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )
    _, dependent_batch_id = _dependent_batch_ids(manifest)
    dependent_selector_path = (
        f"admitted_batch_evidence_bundles[{dependent_batch_id}]"
        ".selector-results[0]"
    )
    assert not any(
        ".dependency-results[0]" in issue.path for issue in error.value.issues
    )
    assert not any(
        issue.path == f"{dependent_selector_path}.outcome"
        for issue in error.value.issues
    )
    assert not any(
        issue.path == f"{dependent_selector_path}.skip-reason"
        and "dependency-blocked" in issue.message
        for issue in error.value.issues
    )
    assert not any(
        issue.path == f"{dependent_selector_path}.diagnostics"
        and "dependency-blocked" in issue.message
        for issue in error.value.issues
    )


def test_summary_rejects_failed_dependency_when_upstream_succeeded() -> None:
    """Dependency rows cannot claim failed for successful upstream evidence."""
    (
        plan,
        manifest,
        base_bundle,
        dependent_bundle,
        aggregate_manifest,
        summary,
    ) = _dependent_admitted_fixture()
    result = cast(
        "dict[str, object]",
        cast("list[dict[str, object]]", dependent_bundle["selector-results"])[
            0
        ],
    )
    dependency = cast(
        "dict[str, object]",
        cast("list[dict[str, object]]", result["dependency-results"])[0],
    )
    dependency["outcome"] = "failed"
    dependency["admitted-for-gating"] = False
    _, dependent_batch_id = _dependent_batch_ids(manifest)
    aggregate_slot = _rows_by_batch_id(
        cast("list[dict[str, object]]", aggregate_manifest["batch-bundles"])
    )[dependent_batch_id]
    aggregate_candidate = cast(
        "dict[str, object]",
        cast(
            "list[dict[str, object]]",
            aggregate_slot["observed-candidates"],
        )[0],
    )
    aggregate_candidate["content-digest"] = (
        ci_validation_batch_evidence_bundle_payload_digest(dependent_bundle)
    )
    _refresh_summary_manifest_digest(summary, aggregate_manifest)

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[base_bundle, dependent_bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )
    assert any(
        issue.path.endswith(".dependency-results[0].outcome")
        for issue in error.value.issues
    )
    assert any(
        issue.path.endswith(".dependency-results[0].admitted-for-gating")
        for issue in error.value.issues
    )


def test_aggregate_evidence_manifest_freezes_and_validates() -> None:
    """Aggregate evidence closes pre-final inputs and bundle slots."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)

    validate_ci_validation_aggregate_evidence_manifest(
        aggregate_manifest,
        plan=plan,
        execution_batch_manifest=manifest,
        request=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )

    assert aggregate_manifest[
        "artifact-ref"
    ] == ci_validation_aggregate_evidence_manifest_artifact_ref(
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
    )
    assert aggregate_manifest["proof-admissibility"] == "validation-only"


def test_aggregate_manifest_accepts_canonical_request_context_with_plan() -> (
    None
):
    """Supplied request context is recomputed and bound even with a plan."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)

    validate_ci_validation_aggregate_evidence_manifest(
        aggregate_manifest,
        plan=plan,
        execution_batch_manifest=manifest,
        request=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )


def test_aggregate_manifest_rejects_malformed_request_context_with_plan() -> (
    None
):
    """Malformed supplied request context fails closed even with a plan."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    request = _request_document()
    del request["mode"]

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
            request=request,
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_aggregate_manifest_rejects_contradictory_request_with_plan() -> None:
    """Copied request digest cannot authorize contradictory request context."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    request = _request_document()
    request["mode"] = "push"

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
            request=request,
        )


def test_aggregate_manifest_accepts_canonical_changed_files_snapshot() -> None:
    """Supplied changed-files snapshot context is recomputed and bound."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)

    validate_ci_validation_aggregate_evidence_manifest(
        aggregate_manifest,
        plan=plan,
        execution_batch_manifest=manifest,
        request=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )


def test_aggregate_manifest_accepts_canonical_fact_snapshot() -> None:
    """Supplied fact snapshot context is recomputed and bound."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)

    validate_ci_validation_aggregate_evidence_manifest(
        aggregate_manifest,
        plan=plan,
        execution_batch_manifest=manifest,
        request=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )


def test_aggregate_manifest_changed_files_provenance_gates_fact_binding() -> (
    None
):
    """Nested changed-files companion failures block fact context binding."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    changed_files_snapshot = _changed_files_snapshot_document()
    changed_files_snapshot["changed-files-hash"] = "0" * 64
    fact_input = cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            "fact-snapshot"
        ],
    )
    fact_input["content-digest"] = "0" * 64

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=_fact_snapshot_document(),
        )

    issue_pairs = [
        (issue.path, issue.message) for issue in exc_info.value.issues
    ]
    assert (
        "changed_files_snapshot.changed-files-hash",
        "does not match hash-payload",
    ) in issue_pairs
    assert (
        "fact_snapshot.plan-id",
        "requires proven plan identity",
    ) in issue_pairs
    assert not any(
        issue.path == "fact_snapshot.fact-snapshot-id"
        and issue.message == "does not match providers"
        for issue in exc_info.value.issues
    )
    assert not any(
        issue.path == "$.input-artifacts.fact-snapshot.content-digest"
        and issue.message == "must match fact_snapshot"
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    ("mutation", "expected_path"),
    [
        (
            lambda snapshot: cast(
                "dict[str, object]",
                snapshot["repository"],
            ).__setitem__("owner", "other-owner"),
            "changed_files_snapshot.repository.owner",
        ),
        (
            lambda snapshot: cast(
                "dict[str, object]",
                snapshot["repository"],
            ).__setitem__("name", "other-repo"),
            "changed_files_snapshot.repository.name",
        ),
        (
            lambda snapshot: cast(
                "dict[str, object]",
                snapshot["run"],
            ).__setitem__("workflow", "Other Workflow"),
            "changed_files_snapshot.run.workflow",
        ),
        (
            lambda snapshot: cast(
                "dict[str, object]",
                snapshot["run"],
            ).__setitem__("run-id", "999"),
            "changed_files_snapshot.run.run-id",
        ),
        (
            lambda snapshot: cast(
                "dict[str, object]",
                snapshot["run"],
            ).__setitem__("run-attempt", "2"),
            "changed_files_snapshot.run.run-attempt",
        ),
    ],
)
def test_aggregate_manifest_changed_files_envelope_gates_fact_binding(
    mutation: Callable[[dict[str, object]], None],
    expected_path: str,
) -> None:
    """Changed-files run provenance blocks aggregate fact context binding."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    changed_files_snapshot = _changed_files_snapshot_document()
    mutation(changed_files_snapshot)
    fact_input = cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            "fact-snapshot"
        ],
    )
    fact_input["content-digest"] = "0" * 64

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=_fact_snapshot_document(),
        )

    issue_pairs = [
        (issue.path, issue.message) for issue in exc_info.value.issues
    ]
    assert any(path == expected_path for path, _ in issue_pairs)
    assert (
        "fact_snapshot.plan-id",
        "requires proven plan identity",
    ) in issue_pairs
    assert not any(
        issue.path
        in {
            "$.fact-snapshot.fact-snapshot-id",
            "fact_snapshot.fact-snapshot-id",
        }
        and issue.message == "does not match providers"
        for issue in exc_info.value.issues
    )
    assert not any(
        issue.path == "$.input-artifacts.fact-snapshot.content-digest"
        and issue.message == "must match fact_snapshot"
        for issue in exc_info.value.issues
    )


def test_aggregate_manifest_omitted_changed_files_gates_fact_binding() -> None:
    """Required changed-files companions block aggregate fact binding."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    fact_input = cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            "fact-snapshot"
        ],
    )
    fact_input["content-digest"] = "0" * 64

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
            fact_snapshot=_fact_snapshot_document(),
        )

    issue_pairs = [
        (issue.path, issue.message) for issue in exc_info.value.issues
    ]
    assert ("$.changed-files-snapshot", "companion is required") in issue_pairs
    assert (
        "fact_snapshot.plan-id",
        "requires proven plan identity",
    ) in issue_pairs
    assert not any(
        issue.path
        in {
            "$.fact-snapshot.fact-snapshot-id",
            "fact_snapshot.fact-snapshot-id",
        }
        and issue.message == "does not match providers"
        for issue in exc_info.value.issues
    )
    assert not any(
        issue.path == "$.input-artifacts.fact-snapshot.content-digest"
        and issue.message == "must match fact_snapshot"
        for issue in exc_info.value.issues
    )


def test_aggregate_manifest_rejects_fact_snapshot_id_provider_mismatch() -> (
    None
):
    """Fact snapshot IDs must be proven by their providers."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    fact_snapshot = _fact_snapshot_document()
    fact_snapshot["fact-snapshot-id"] = "0" * 64

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=fact_snapshot,
        )

    issue_pairs = [
        (issue.path, issue.message) for issue in exc_info.value.issues
    ]
    assert (
        "$.fact-snapshot.fact-snapshot-id",
        "does not match providers",
    ) in issue_pairs
    assert (
        "$.fact-snapshot.id",
        "must match companion fact snapshot",
    ) in issue_pairs
    assert (
        "fact_snapshot.plan-id",
        "requires proven plan identity",
    ) in issue_pairs
    assert not any(
        issue.path == "$.input-artifacts.fact-snapshot.content-digest"
        and issue.message == "must match fact_snapshot"
        for issue in exc_info.value.issues
    )


def test_aggregate_manifest_malformed_fact_snapshot_id_gates_binding() -> None:
    """Malformed fact snapshot IDs stop before provider digest comparison."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    fact_snapshot = _fact_snapshot_document()
    fact_snapshot["fact-snapshot-id"] = "not-a-digest"

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=fact_snapshot,
        )

    assert any(
        issue.path == "fact_snapshot.fact-snapshot-id"
        and issue.message == "must be a SHA-256 digest"
        for issue in exc_info.value.issues
    )
    assert not any(
        issue.path == "fact_snapshot.fact-snapshot-id"
        and issue.message == "does not match providers"
        for issue in exc_info.value.issues
    )


def test_aggregate_manifest_rejects_fact_snapshot_plan_id_mismatch() -> None:
    """Fact snapshot context must match aggregate plan provenance."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    fact_snapshot = _fact_snapshot_document()
    fact_snapshot["plan-id"] = "other-plan"

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=fact_snapshot,
        )

    issue_pairs = [
        (issue.path, issue.message) for issue in exc_info.value.issues
    ]
    assert ("$.fact-snapshot.plan-id", "must match plan") in issue_pairs
    assert (
        "fact_snapshot.plan-id",
        "requires proven plan identity",
    ) in issue_pairs
    assert not any(
        issue.path == "$.input-artifacts.fact-snapshot.content-digest"
        and issue.message == "must match fact_snapshot"
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    ("mutation", "expected_path"),
    [
        (
            lambda fact_snapshot: cast(
                "dict[str, object]",
                fact_snapshot["repository"],
            ).__setitem__("owner", None),
            "fact_snapshot.$.repository.owner",
        ),
        (
            lambda fact_snapshot: fact_snapshot.__setitem__(
                "artifact-ref",
                ci_validation_fact_snapshot_artifact_ref(
                    run_id="999999",
                    run_attempt=RUN_ATTEMPT,
                ),
            ),
            "fact_snapshot.artifact-ref",
        ),
    ],
)
def test_aggregate_manifest_fact_snapshot_provenance_gates_input_binding(
    mutation: Callable[[dict[str, object]], None],
    expected_path: str,
) -> None:
    """Fact snapshot provenance failures prevent context ID binding."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    fact_snapshot = _fact_snapshot_document()
    mutation(fact_snapshot)
    fact_snapshot["fact-snapshot-id"] = "0" * 64
    fact_input = cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            "fact-snapshot"
        ],
    )
    fact_input["content-digest"] = "0" * 64

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            execution_batch_manifest=manifest,
            request=_request_document(),
            fact_snapshot=fact_snapshot,
        )

    issue_paths = [issue.path for issue in exc_info.value.issues]
    assert expected_path in issue_paths
    assert "fact_snapshot.fact-snapshot-id" not in issue_paths
    assert "$.input-artifacts.fact-snapshot.content-digest" not in issue_paths


def test_aggregate_manifest_ignores_unproven_execution_plan_id() -> None:
    """Execution manifest plan IDs are not provenance until validated."""
    plan = _plan()
    manifest = _manifest(plan)
    invalid_manifest = cast("dict[str, object]", deepcopy(manifest))
    cast("dict[str, object]", invalid_manifest["budget"])[
        "actual-total-jobs"
    ] = "one"
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    fact_snapshot = _fact_snapshot_document()
    fact_snapshot["fact-snapshot-id"] = "0" * 64
    fact_input = cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            "fact-snapshot"
        ],
    )
    fact_input["content-digest"] = "0" * 64

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            execution_batch_manifest=invalid_manifest,
            request=_request_document(),
            fact_snapshot=fact_snapshot,
        )

    issue_pairs = [
        (issue.path, issue.message) for issue in exc_info.value.issues
    ]
    assert (
        "fact_snapshot.plan-id",
        "requires proven plan identity",
    ) in issue_pairs
    assert not any(
        issue.path == "fact_snapshot.fact-snapshot-id"
        and issue.message == "does not match providers"
        for issue in exc_info.value.issues
    )
    assert not any(
        issue.path == "$.input-artifacts.fact-snapshot.content-digest"
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize("changed_files_case", ["missing", "invalid"])
def test_planless_aggregate_requires_changed_files_proof_for_fact_binding(
    changed_files_case: str,
) -> None:
    """Planless execution manifest identity needs changed-files provenance."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    fact_snapshot = _fact_snapshot_document()
    fact_snapshot["fact-snapshot-id"] = "0" * 64
    fact_input = cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            "fact-snapshot"
        ],
    )
    fact_input["content-digest"] = "0" * 64
    changed_files_snapshot = None
    if changed_files_case == "invalid":
        changed_files_snapshot = _changed_files_snapshot_document()
        changed_files_snapshot["changed-files-hash"] = "0" * 64

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
        )

    issue_pairs = [
        (issue.path, issue.message) for issue in exc_info.value.issues
    ]
    assert (
        "fact_snapshot.plan-id",
        "requires proven plan identity",
    ) in issue_pairs
    if changed_files_case == "invalid":
        assert (
            "changed_files_snapshot.changed-files-hash",
            "does not match hash-payload",
        ) in issue_pairs
    assert not any(
        issue.path == "fact_snapshot.fact-snapshot-id"
        and issue.message == "does not match providers"
        for issue in exc_info.value.issues
    )
    assert not any(
        issue.path == "$.input-artifacts.fact-snapshot.content-digest"
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    ("mutation", "expected_issue"),
    [
        (
            lambda snapshot: snapshot.__setitem__(
                "artifact-ref",
                ci_validation_changed_files_snapshot_artifact_ref(
                    run_id="999999",
                    run_attempt=RUN_ATTEMPT,
                ),
            ),
            ("changed_files_snapshot.artifact-ref", "must match current run"),
        ),
        (
            lambda snapshot: cast(
                "dict[str, object]",
                snapshot["hash-payload"],
            ).__setitem__("api-version", "v0.invalid"),
            (
                "changed_files_snapshot.hash-payload.api-version",
                "must match changed-files snapshot api-version",
            ),
        ),
    ],
)
def test_planless_changed_files_shape_issues_gate_fact_binding(
    mutation: Callable[[dict[str, object]], None],
    expected_issue: tuple[str, str],
) -> None:
    """Changed-files shape/provenance issues cannot authorize planless facts."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    changed_files_snapshot = _changed_files_snapshot_document()
    mutation(changed_files_snapshot)
    fact_snapshot = _fact_snapshot_document()
    fact_snapshot["fact-snapshot-id"] = "0" * 64
    fact_input = cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            "fact-snapshot"
        ],
    )
    fact_input["content-digest"] = "0" * 64

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
        )

    issue_pairs = [
        (issue.path, issue.message) for issue in exc_info.value.issues
    ]
    assert expected_issue in issue_pairs
    assert (
        "fact_snapshot.plan-id",
        "requires proven plan identity",
    ) in issue_pairs
    assert not any(
        issue.path == "fact_snapshot.fact-snapshot-id"
        and issue.message == "does not match providers"
        for issue in exc_info.value.issues
    )
    assert not any(
        issue.path == "$.input-artifacts.fact-snapshot.content-digest"
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    ("input_case", "expected_issue"),
    [
        (
            "missing",
            ("$.input-artifacts.changed-files-snapshot", "is required"),
        ),
        (
            "inadmissible",
            (
                "$.input-artifacts.changed-files-snapshot.admissibility",
                "must be valid when changed_files_snapshot is supplied",
            ),
        ),
        (
            "stale",
            (
                "$.input-artifacts.changed-files-snapshot.artifact-ref",
                "must match current run when changed_files_snapshot "
                "is supplied",
            ),
        ),
        (
            "null",
            (
                "$.input-artifacts.changed-files-snapshot.content-digest",
                "must match changed_files_snapshot",
            ),
        ),
    ],
)
def test_planless_changed_files_input_artifact_gates_fact_binding(
    input_case: str,
    expected_issue: tuple[str, str],
) -> None:
    """Planless fact binding requires a current changed-files input row."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    inputs = cast(
        "dict[str, object]",
        aggregate_manifest["input-artifacts"],
    )
    if input_case == "missing":
        del inputs["changed-files-snapshot"]
    elif input_case == "inadmissible":
        _set_input_absent(
            aggregate_manifest,
            "changed-files-snapshot",
            required=True,
            admissibility="inadmissible",
        )
    else:
        changed_files_input = cast(
            "dict[str, object]",
            inputs["changed-files-snapshot"],
        )
        if input_case == "stale":
            changed_files_input["artifact-ref"] = (
                ci_validation_changed_files_snapshot_artifact_ref(
                    run_id="999999",
                    run_attempt=RUN_ATTEMPT,
                )
            )
        else:
            changed_files_input["content-digest"] = None
    fact_snapshot = _fact_snapshot_document()
    fact_snapshot["fact-snapshot-id"] = "0" * 64
    fact_input = cast(
        "dict[str, object]",
        inputs["fact-snapshot"],
    )
    fact_input["content-digest"] = "0" * 64

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=fact_snapshot,
        )

    issue_pairs = [
        (issue.path, issue.message) for issue in exc_info.value.issues
    ]
    assert expected_issue in issue_pairs
    assert (
        "fact_snapshot.plan-id",
        "requires proven plan identity",
    ) in issue_pairs
    assert not any(
        issue.path == "fact_snapshot.fact-snapshot-id"
        and issue.message == "does not match providers"
        for issue in exc_info.value.issues
    )
    assert not any(
        issue.path == "$.input-artifacts.fact-snapshot.content-digest"
        for issue in exc_info.value.issues
    )


def _set_input_required_false(artifact: dict[str, object]) -> None:
    artifact["required"] = False


@pytest.mark.parametrize(
    "mutation",
    [
        _set_input_required_false,
        lambda artifact: artifact.__setitem__("expected-cardinality", 0),
        lambda artifact: artifact.pop("artifact-instance-id"),
        lambda artifact: artifact.__setitem__("artifact-instance-id", None),
        lambda artifact: artifact.__setitem__("artifact-instance-id", ""),
    ],
)
def test_planless_changed_files_structural_input_gates_fact_binding(
    mutation: Callable[[dict[str, object]], None],
) -> None:
    """Structurally invalid changed-files input rows cannot prove facts."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    inputs = cast("dict[str, object]", aggregate_manifest["input-artifacts"])
    mutation(cast("dict[str, object]", inputs["changed-files-snapshot"]))
    fact_snapshot = _fact_snapshot_document()
    fact_snapshot["fact-snapshot-id"] = "0" * 64
    fact_input = cast("dict[str, object]", inputs["fact-snapshot"])
    fact_input["content-digest"] = "0" * 64

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=fact_snapshot,
        )

    issue_pairs = [
        (issue.path, issue.message) for issue in exc_info.value.issues
    ]
    assert (
        "fact_snapshot.plan-id",
        "requires proven plan identity",
    ) in issue_pairs
    assert not any(
        issue.path == "fact_snapshot.fact-snapshot-id"
        and issue.message == "does not match providers"
        for issue in exc_info.value.issues
    )
    assert not any(
        issue.path == "$.input-artifacts.fact-snapshot.content-digest"
        for issue in exc_info.value.issues
    )


def test_plan_fact_digest_omitted_after_changed_files_proof_failure() -> None:
    """Plan fact input digests do not bind without changed-files proof."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    changed_files_snapshot = _changed_files_snapshot_document()
    changed_files_snapshot["changed-files-hash"] = "0" * 64
    fact_input = cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            "fact-snapshot"
        ],
    )
    fact_input["content-digest"] = "0" * 64

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=_fact_snapshot_document(),
        )

    issue_pairs = [
        (issue.path, issue.message) for issue in exc_info.value.issues
    ]
    assert (
        "changed_files_snapshot.changed-files-hash",
        "does not match hash-payload",
    ) in issue_pairs
    assert (
        "fact_snapshot.plan-id",
        "requires proven plan identity",
    ) in issue_pairs
    assert not any(
        issue.path == "$.input-artifacts.fact-snapshot.content-digest"
        for issue in exc_info.value.issues
    )


def test_aggregate_manifest_rejects_valid_fact_input_without_fact_context() -> (
    None
):
    """Valid fact inputs require authoritative fact snapshot context."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
            changed_files_snapshot=_changed_files_snapshot_document(),
        )

    assert any(
        issue.path == "$.input-artifacts.fact-snapshot.admissibility"
        and "authoritative fact snapshot" in issue.message
        for issue in exc_info.value.issues
    )


def test_summary_revalidation_rejects_valid_fact_input_without_context() -> (
    None
):
    """Summary revalidation preserves direct valid fact input proof checks."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)

    with pytest.raises(ContractValidationError) as direct_exc:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
            changed_files_snapshot=_changed_files_snapshot_document(),
        )
    with pytest.raises(ContractValidationError) as summary_exc:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
        )

    for error in (direct_exc.value, summary_exc.value):
        assert any(
            issue.path == "$.input-artifacts.fact-snapshot.admissibility"
            and "fact" in issue.message
            for issue in error.issues
        )


def test_summary_revalidation_forwards_fact_snapshot_context() -> None:
    """Summary reports the same malformed fact context as direct."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    fact_snapshot = _fact_snapshot_document()
    fact_snapshot["fact-snapshot-id"] = "0" * 64

    with pytest.raises(ContractValidationError) as direct_exc:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=fact_snapshot,
        )
    with pytest.raises(ContractValidationError) as summary_exc:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=fact_snapshot,
        )

    for error in (direct_exc.value, summary_exc.value):
        assert any(
            issue.path
            in {
                "$.fact-snapshot.fact-snapshot-id",
                "fact_snapshot.fact-snapshot-id",
            }
            and issue.message == "does not match providers"
            for issue in error.issues
        )


def test_invalid_supplied_plan_cannot_authorize_bundle_admission() -> None:
    """Self-digested malformed plans cannot admit projection bundles."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    del plan["work-groups"]
    del plan["evidence-expectations"]
    plan["plan-digest"] = ci_validation_plan_digest(plan)
    aggregate_manifest["plan-digest"] = plan["plan-digest"]
    cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            "validation-plan"
        ],
    )["content-digest"] = plan["plan-digest"]
    _set_input_absent(
        aggregate_manifest,
        "fact-snapshot",
        required=True,
        admissibility="missing",
    )

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
        )

    issue_pairs = [
        (issue.path, issue.message) for issue in exc_info.value.issues
    ]
    assert (
        "$.input-artifacts.validation-plan",
        "must be valid and match supplied plan to authorize projection",
    ) in issue_pairs
    assert (
        "$.batch-bundles[0].slot-admissibility",
        "requires authoritative plan or projection authority",
    ) in issue_pairs


def test_companion_invalid_supplied_plan_cannot_authorize_projection() -> None:
    """Matching plan input is not enough when required companions are absent."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    _set_input_absent(
        aggregate_manifest,
        "fact-snapshot",
        required=True,
        admissibility="missing",
    )

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
        )

    issue_pairs = [
        (issue.path, issue.message) for issue in exc_info.value.issues
    ]
    assert (
        "$.input-artifacts.validation-plan",
        "must be valid and match supplied plan to authorize projection",
    ) in issue_pairs
    assert (
        "$.batch-bundles[0].slot-admissibility",
        "requires authoritative plan or projection authority",
    ) in issue_pairs


def test_supplied_plan_without_request_context_rejects_projection() -> None:
    """Supplied-plan projection needs a proven current-run request context."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    issue_pairs = [
        (issue.path, issue.message) for issue in exc_info.value.issues
    ]
    assert (
        "$.input-artifacts.validation-plan",
        "must be valid and match supplied plan to authorize projection",
    ) in issue_pairs
    assert (
        "$.batch-bundles[0].slot-admissibility",
        "requires authoritative plan or projection authority",
    ) in issue_pairs


@pytest.mark.parametrize(
    "input_name",
    ["changed-files-snapshot", "fact-snapshot"],
)
@pytest.mark.parametrize("admissibility", ["missing", "inadmissible"])
def test_required_snapshot_input_must_be_valid_to_authorize_projection(
    input_name: str,
    admissibility: str,
) -> None:
    """Required snapshot companions must be proven by valid aggregate inputs."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    if admissibility == "missing":
        _set_input_absent(
            aggregate_manifest,
            input_name,
            required=True,
            admissibility="missing",
        )
    else:
        cast(
            "dict[str, object]",
            cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
                input_name
            ],
        )["admissibility"] = "inadmissible"

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    issue_pairs = [
        (issue.path, issue.message) for issue in exc_info.value.issues
    ]
    assert (
        "$.input-artifacts.validation-plan",
        "must be valid and match supplied plan to authorize projection",
    ) in issue_pairs
    assert (
        "$.batch-bundles[0].slot-admissibility",
        "requires authoritative plan or projection authority",
    ) in issue_pairs


def test_aggregate_manifest_valid_plan_input_requires_plan_context() -> None:
    """A valid validation-plan input cannot fall back to planless authority."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    issue_pairs = [
        (issue.path, issue.message) for issue in exc_info.value.issues
    ]
    assert (
        "$.input-artifacts.validation-plan",
        "valid admissibility requires supplied validated current-run "
        "plan context",
    ) in issue_pairs
    assert (
        "$.batch-bundles[0].slot-admissibility",
        "requires authoritative plan or projection authority",
    ) in issue_pairs


def test_aggregate_manifest_rejects_bad_plan_without_authority() -> None:
    """Malformed supplied plans cannot be laundered through no-authority."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    malformed_plan = deepcopy(plan)
    del malformed_plan["validation-tree"]
    malformed_plan["plan-digest"] = ci_validation_plan_digest(malformed_plan)
    aggregate_manifest["plan-digest"] = malformed_plan["plan-digest"]
    cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            "validation-plan"
        ],
    )["content-digest"] = malformed_plan["plan-digest"]
    slot = cast(
        "dict[str, object]",
        cast("list[dict[str, object]]", aggregate_manifest["batch-bundles"])[0],
    )
    slot["slot-admissibility"] = "missing"
    slot["admitted-candidate-id"] = None
    slot["observed-candidates"] = []
    aggregate_manifest["projection-authority"] = None

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=malformed_plan,
            execution_batch_manifest=manifest,
            request=_request_document(),
        )

    assert any(
        issue.path == "$.validation-tree" for issue in exc_info.value.issues
    )


def test_aggregate_binds_fact_without_changed_files_input() -> None:
    """Plans that do not require changed-files still bind fact snapshots."""
    snapshot = _scheduled_full_plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    cast("dict[str, object]", plan["affected-range"])["changed-files-hash"] = (
        None
    )
    plan["plan-digest"] = ci_validation_plan_digest(plan)
    input_artifacts = _aggregate_input_artifacts(plan, {"placeholder": True})
    _set_input_absent(
        {"input-artifacts": input_artifacts},
        "execution-batch-manifest",
        required=True,
        admissibility="missing",
    )
    aggregate_manifest = freeze_ci_validation_aggregate_evidence_manifest(
        created_at=CREATED_AT,
        repository_owner="hcoona",
        repository_name="three",
        workflow="CI Validation",
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        input_artifacts=input_artifacts,
        batch_bundles=[],
        unexpected_contract_artifacts=[],
        namespace_overflow={
            "detected": False,
            "observed-prefixed-artifact-count-lower-bound": 4,
            "max-prefixed-validation-artifacts": 18,
            "diagnostics": [],
        },
        pre_final_validation_artifacts=4,
        namespace_closed_at=CREATED_AT,
        plan=plan,
        request=cast("dict[str, object]", _scheduled_full_request()),
        fact_snapshot=snapshot.fact_snapshot,
    )
    fact_input = cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            "fact-snapshot"
        ],
    )
    fact_input["content-digest"] = "0" * 64

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            fact_snapshot=snapshot.fact_snapshot,
        )

    assert any(
        issue.path == "$.input-artifacts.fact-snapshot.content-digest"
        and issue.message == "must match fact_snapshot"
        for issue in exc_info.value.issues
    )
    assert not any(
        issue.path.startswith("$.input-artifacts.changed-files-snapshot")
        for issue in exc_info.value.issues
    )
    assert not any(
        issue.path == "fact_snapshot.plan-id"
        and issue.message == "requires proven plan identity"
        for issue in exc_info.value.issues
    )


def test_aggregate_available_empty_hash_requires_changed_files() -> None:
    """Malformed available affected ranges cannot downgrade snapshot proof."""
    plan = _plan()
    cast("dict[str, object]", plan["affected-range"])["changed-files-hash"] = ""
    plan["plan-digest"] = ci_validation_plan_digest(plan)
    manifest = _manifest(_plan())
    manifest["plan-digest"] = plan["plan-digest"]
    input_artifacts = _aggregate_input_artifacts(plan, manifest)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            freeze_ci_validation_aggregate_evidence_manifest(
                created_at=CREATED_AT,
                repository_owner="hcoona",
                repository_name="three",
                workflow="CI Validation",
                run_id=RUN_ID,
                run_attempt=RUN_ATTEMPT,
                input_artifacts=input_artifacts,
                batch_bundles=[],
                unexpected_contract_artifacts=[],
                namespace_overflow={
                    "detected": False,
                    "observed-prefixed-artifact-count-lower-bound": 4,
                    "max-prefixed-validation-artifacts": 18,
                    "diagnostics": [],
                },
                pre_final_validation_artifacts=4,
                namespace_closed_at=CREATED_AT,
                plan=plan,
                request=_request_document(),
                fact_snapshot=_fact_snapshot_document(),
            ),
            plan=plan,
            fact_snapshot=_fact_snapshot_document(),
        )

    issue_pairs = [
        (issue.path, issue.message) for issue in exc_info.value.issues
    ]
    assert (
        "$.input-artifacts.changed-files-snapshot.content-digest",
        "must match frozen input digest",
    ) not in issue_pairs
    assert any(
        path.startswith("$.affected-range.changed-files-hash")
        for path, _ in issue_pairs
    )


def test_projection_allows_no_changed_files_required_authority() -> None:
    """Projection skips changed-files proof when authority needs none."""
    snapshot = _scheduled_full_plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    input_artifacts = _aggregate_input_artifacts(plan, {"placeholder": True})
    input_artifacts["execution-batch-manifest"] = _input_artifact(
        None,
        required=True,
        admissibility="missing",
    )
    aggregate_manifest = freeze_ci_validation_aggregate_evidence_manifest(
        created_at=CREATED_AT,
        repository_owner="hcoona",
        repository_name="three",
        workflow="CI Validation",
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        input_artifacts=input_artifacts,
        batch_bundles=[],
        unexpected_contract_artifacts=[],
        namespace_overflow={
            "detected": False,
            "observed-prefixed-artifact-count-lower-bound": 4,
            "max-prefixed-validation-artifacts": 18,
            "diagnostics": [],
        },
        pre_final_validation_artifacts=4,
        namespace_closed_at=CREATED_AT,
        plan=plan,
        request=cast("dict[str, object]", _scheduled_full_request()),
        fact_snapshot=cast("dict[str, object]", snapshot.fact_snapshot),
    )

    validate_ci_validation_aggregate_evidence_manifest(
        aggregate_manifest,
        plan=plan,
        request=cast("dict[str, object]", _scheduled_full_request()),
        fact_snapshot=cast("dict[str, object]", snapshot.fact_snapshot),
    )


def test_no_authority_aggregate_fact_snapshot_requires_proven_plan_id() -> None:
    """Planless fact snapshot binding requires proven plan identity."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    _set_input_absent(
        aggregate_manifest,
        "validation-plan",
        required=True,
        admissibility="missing",
    )
    aggregate_manifest["plan-id"] = None
    aggregate_manifest["plan-digest"] = None
    aggregate_manifest["projection-authority"] = None
    fact_input = cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            "fact-snapshot"
        ],
    )
    fact_input["content-digest"] = "0" * 64

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            request=_request_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    issue_pairs = [
        (issue.path, issue.message) for issue in exc_info.value.issues
    ]
    issue_paths = [path for path, _ in issue_pairs]
    assert (
        "fact_snapshot.plan-id",
        "requires proven plan identity",
    ) in issue_pairs
    assert "fact_snapshot.fact-snapshot-id" not in issue_paths
    assert "$.input-artifacts.fact-snapshot.content-digest" not in issue_paths


def test_no_authority_valid_fact_input_requires_fact_context() -> None:
    """Planless no-authority manifests cannot retain valid fact inputs."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    _set_input_absent(
        aggregate_manifest,
        "validation-plan",
        required=True,
        admissibility="missing",
    )
    aggregate_manifest["plan-id"] = None
    aggregate_manifest["plan-digest"] = None
    aggregate_manifest["projection-authority"] = None

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            request=_request_document(),
        )

    assert any(
        issue.path == "$.input-artifacts.fact-snapshot.admissibility"
        and "authoritative fact snapshot" in issue.message
        for issue in exc_info.value.issues
    )


def test_aggregate_manifest_malformed_plan_id_gates_fact_snapshot_binding() -> (
    None
):
    """Malformed matching plan IDs cannot authorize provider binding."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    fact_snapshot = _fact_snapshot_document()
    malformed_plan_id = "Bad Plan"
    manifest["plan-id"] = malformed_plan_id
    aggregate_manifest["plan-id"] = malformed_plan_id
    cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            "execution-batch-manifest"
        ],
    )["content-digest"] = ci_validation_execution_batch_manifest_payload_digest(
        manifest
    )
    fact_snapshot["plan-id"] = malformed_plan_id
    fact_snapshot["fact-snapshot-id"] = "0" * 64
    fact_input = cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            "fact-snapshot"
        ],
    )
    fact_input["content-digest"] = "0" * 64

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            execution_batch_manifest=manifest,
            request=_request_document(),
            fact_snapshot=fact_snapshot,
        )

    issue_pairs = [
        (issue.path, issue.message) for issue in exc_info.value.issues
    ]
    assert (
        "fact_snapshot.plan-id",
        "requires proven plan identity",
    ) in issue_pairs
    assert (
        "execution_batch_manifest.plan-id",
        "must be a stable plan identifier",
    ) in issue_pairs
    assert not any(
        issue.path == "fact_snapshot.fact-snapshot-id"
        and issue.message == "does not match providers"
        for issue in exc_info.value.issues
    )
    assert not any(
        issue.path == "$.input-artifacts.fact-snapshot.content-digest"
        for issue in exc_info.value.issues
    )


def test_aggregate_manifest_malformed_plan_requires_proven_fact_plan_id() -> (
    None
):
    """Self-consistent but malformed plans cannot authorize fact binding."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    fact_snapshot = _fact_snapshot_document()
    del plan["work-groups"]
    del plan["evidence-expectations"]
    plan["plan-digest"] = ci_validation_plan_digest(plan)
    aggregate_manifest["plan-digest"] = plan["plan-digest"]
    cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            "validation-plan"
        ],
    )["content-digest"] = plan["plan-digest"]

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=fact_snapshot,
        )

    issue_pairs = [
        (issue.path, issue.message) for issue in exc_info.value.issues
    ]
    assert ("$.work-groups", "is required") in issue_pairs
    assert ("$.evidence-expectations", "is required") in issue_pairs
    assert (
        "fact_snapshot.plan-id",
        "requires proven plan identity",
    ) in issue_pairs
    assert not any(
        issue.path == "fact_snapshot.fact-snapshot-id"
        and issue.message == "does not match providers"
        for issue in exc_info.value.issues
    )
    assert not any(
        issue.path == "$.input-artifacts.fact-snapshot.content-digest"
        and issue.message == "must match fact_snapshot"
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    ("plan_mutation", "expected_plan_issue"),
    [
        (
            "fact-id-mismatch",
            ("$.fact-snapshot.id", "must match companion fact snapshot"),
        ),
        ("fact-status-invalid", ("$.fact-snapshot.status", "is invalid")),
    ],
)
def test_aggregate_manifest_plan_fact_snapshot_errors_gate_fact_binding(
    plan_mutation: str,
    expected_plan_issue: tuple[str, str],
) -> None:
    """Plan-owned fact snapshot declaration errors block fact binding."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    fact_snapshot = _fact_snapshot_document()
    fact_declaration = cast("dict[str, object]", plan["fact-snapshot"])
    if plan_mutation == "fact-id-mismatch":
        fact_declaration["id"] = "0" * 64
        cast(
            "dict[str, object]",
            cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
                "fact-snapshot"
            ],
        )["content-digest"] = "0" * 64
    else:
        fact_declaration["status"] = "invalid"
    plan["plan-digest"] = ci_validation_plan_digest(plan)
    aggregate_manifest["plan-digest"] = plan["plan-digest"]
    cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            "validation-plan"
        ],
    )["content-digest"] = plan["plan-digest"]

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=fact_snapshot,
        )

    issue_pairs = [
        (issue.path, issue.message) for issue in exc_info.value.issues
    ]
    assert expected_plan_issue in issue_pairs
    assert (
        "fact_snapshot.plan-id",
        "requires proven plan identity",
    ) in issue_pairs
    assert not any(
        issue.path == "fact_snapshot.fact-snapshot-id"
        and issue.message
        in {"does not match providers", "must match plan-frozen input digest"}
        for issue in exc_info.value.issues
    )
    assert not any(
        issue.path == "$.input-artifacts.fact-snapshot.content-digest"
        and issue.message == "must match fact_snapshot"
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize("invalid_path", ["/evil", "../evil"])
def test_aggregate_manifest_rejects_invalid_changed_files_snapshot_path(
    invalid_path: str,
) -> None:
    """Invalid changed-files paths gate downstream fact binding."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    changed_files_snapshot = _changed_files_snapshot_document()
    fact_snapshot = _fact_snapshot_document()
    hash_payload = cast(
        "dict[str, object]",
        changed_files_snapshot["hash-payload"],
    )
    hash_payload["changed-files"] = [invalid_path]
    changed_files_snapshot["changed-files-hash"] = canonical_json_digest(
        hash_payload
    )
    fact_snapshot["fact-snapshot-id"] = "0" * 64
    cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            "fact-snapshot"
        ],
    )["content-digest"] = "0" * 64

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
        )

    assert any(
        issue.path == "changed_files_snapshot.hash-payload.changed-files[0]"
        for issue in exc_info.value.issues
    )
    assert not any(
        issue.path == "fact_snapshot.fact-snapshot-id"
        and issue.message == "does not match providers"
        for issue in exc_info.value.issues
    )
    assert not any(
        issue.path == "$.input-artifacts.fact-snapshot.content-digest"
        for issue in exc_info.value.issues
    )


def test_aggregate_manifest_rejects_noncanonical_changed_files_payload() -> (
    None
):
    """Changed-files hash-payload must equal its canonical payload."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    changed_files_snapshot = _changed_files_snapshot_document()
    fact_snapshot = _fact_snapshot_document()
    hash_payload = cast(
        "dict[str, object]",
        changed_files_snapshot["hash-payload"],
    )
    changed_files = cast("list[str]", hash_payload["changed-files"])
    hash_payload["changed-files"] = [*changed_files, changed_files[0]]
    changed_files_snapshot["changed-files-hash"] = (
        ci_validation_changed_files_hash(changed_files)
    )
    fact_snapshot["fact-snapshot-id"] = "0" * 64

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
        )

    issue_paths = [issue.path for issue in exc_info.value.issues]
    assert "changed_files_snapshot.hash-payload.changed-files" in issue_paths
    assert "fact_snapshot.fact-snapshot-id" not in issue_paths


def test_aggregate_manifest_rejects_contradictory_changed_files_payload() -> (
    None
):
    """Changed-files hash must be recomputed from supplied hash-payload."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    changed_files_snapshot = _changed_files_snapshot_document()
    hash_payload = cast(
        "dict[str, object]",
        changed_files_snapshot["hash-payload"],
    )
    hash_payload["changed-files"] = ["README.md"]

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
            changed_files_snapshot=changed_files_snapshot,
        )


def test_aggregate_manifest_rejects_noncanonical_fact_snapshot_providers() -> (
    None
):
    """Fact snapshot providers must be in canonical provider order."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    fact_snapshot = _fact_snapshot_document()
    python_provider = deepcopy(
        cast("list[dict[str, object]]", fact_snapshot["providers"])[0]
    )
    dotnet_provider = deepcopy(python_provider)
    dotnet_provider["provider"] = "dotnet"
    dotnet_provider["provider-version"] = "dotnet/v1"
    dotnet_provider["roots"] = []
    dotnet_provider["subjects"] = []
    canonical_providers = [dotnet_provider, python_provider]
    fact_snapshot["fact-snapshot-id"] = ci_validation_fact_snapshot_id(
        canonical_providers
    )
    fact_snapshot["providers"] = [python_provider, dotnet_provider]
    cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            "fact-snapshot"
        ],
    )["content-digest"] = fact_snapshot["fact-snapshot-id"]

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            execution_batch_manifest=manifest,
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=fact_snapshot,
        )

    issue_pairs = [
        (issue.path, issue.message) for issue in exc_info.value.issues
    ]
    assert (
        "fact_snapshot.providers",
        "must be ordered uniquely by provider",
    ) in issue_pairs
    assert not any(
        issue.path == "$.input-artifacts.fact-snapshot.content-digest"
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    ("field", "value"),
    [
        (
            "artifact-ref",
            "ci-validation/execution-batches/999/1/"
            "execution-batch-manifest.json",
        ),
        ("admissibility", "inadmissible"),
        ("required", False),
        ("expected-cardinality", 0),
        ("artifact-instance-id", None),
        ("artifact-instance-id", ""),
        ("content-digest", "0" * 64),
    ],
)
def test_planless_fact_binding_requires_execution_manifest_input_proof(
    field: str,
    value: object,
) -> None:
    """Planless execution manifest identity needs current-run input proof."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            "execution-batch-manifest"
        ],
    )[field] = value
    fact_snapshot = _fact_snapshot_document()
    fact_snapshot["fact-snapshot-id"] = "0" * 64

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            execution_batch_manifest=manifest,
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=fact_snapshot,
        )

    issue_pairs = [
        (issue.path, issue.message) for issue in exc_info.value.issues
    ]
    assert (
        "fact_snapshot.plan-id",
        "requires proven plan identity",
    ) in issue_pairs
    assert not any(
        issue.path == "fact_snapshot.fact-snapshot-id"
        and issue.message == "does not match providers"
        for issue in exc_info.value.issues
    )


def test_planless_non_empty_exec_manifest_cannot_authorize_fact_binding() -> (
    None
):
    """Diagnostic validation cannot prove plan identity without a plan."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    fact_snapshot = _fact_snapshot_document()
    fact_snapshot["plan-id"] = plan["plan-id"]

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            execution_batch_manifest=manifest,
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=fact_snapshot,
        )

    issue_pairs = [
        (issue.path, issue.message) for issue in exc_info.value.issues
    ]
    assert (
        "authorizing",
        "requires plan context for non-empty execution batches",
    ) in issue_pairs
    assert (
        "fact_snapshot.plan-id",
        "requires proven plan identity",
    ) in issue_pairs
    assert (
        "fact_snapshot.fact-snapshot-id",
        "does not match providers",
    ) not in issue_pairs


@pytest.mark.parametrize("admissibility", ["missing", "inadmissible"])
@pytest.mark.parametrize(
    "input_name", ["request", "changed-files-snapshot", "fact-snapshot"]
)
def test_aggregate_manifest_rejects_supplied_context_absent_input_artifact(
    input_name: str,
    admissibility: str,
) -> None:
    """Supplied context documents require matching valid input artifacts."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    _set_input_absent(
        aggregate_manifest,
        input_name,
        required=True,
        admissibility=admissibility,
    )
    request_context = _request_document() if input_name == "request" else None
    changed_files_snapshot_context = (
        _changed_files_snapshot_document()
        if input_name in {"changed-files-snapshot", "fact-snapshot"}
        else None
    )
    fact_snapshot_context = (
        _fact_snapshot_document() if input_name == "fact-snapshot" else None
    )
    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
            request=request_context,
            changed_files_snapshot=changed_files_snapshot_context,
            fact_snapshot=fact_snapshot_context,
        )

    assert any(
        issue.path == f"$.input-artifacts.{input_name}.admissibility"
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize("stale_field", ["run-id", "run-attempt"])
@pytest.mark.parametrize(
    "input_name", ["request", "changed-files-snapshot", "fact-snapshot"]
)
def test_aggregate_manifest_rejects_supplied_context_stale_input_artifact_ref(
    input_name: str,
    stale_field: str,
) -> None:
    """Supplied context input artifact refs must bind to the current run."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    wrong_run_id = "999999"
    wrong_run_attempt = "99"
    run_id = wrong_run_id if stale_field == "run-id" else RUN_ID
    run_attempt = (
        wrong_run_attempt if stale_field == "run-attempt" else RUN_ATTEMPT
    )
    if input_name == "request":
        stale_ref = ci_validation_request_artifact_ref(
            run_id=run_id,
            run_attempt=run_attempt,
        )
        request_context = _request_document()
        changed_files_snapshot_context = None
        fact_snapshot_context = None
    elif input_name == "changed-files-snapshot":
        stale_ref = ci_validation_changed_files_snapshot_artifact_ref(
            run_id=run_id,
            run_attempt=run_attempt,
        )
        request_context = None
        changed_files_snapshot_context = _changed_files_snapshot_document()
        fact_snapshot_context = None
    else:
        stale_ref = ci_validation_fact_snapshot_artifact_ref(
            run_id=run_id,
            run_attempt=run_attempt,
        )
        request_context = None
        changed_files_snapshot_context = None
        fact_snapshot_context = _fact_snapshot_document()
    cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            input_name
        ],
    )["artifact-ref"] = stale_ref

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
            request=request_context,
            changed_files_snapshot=changed_files_snapshot_context,
            fact_snapshot=fact_snapshot_context,
        )

    assert any(
        issue.path == f"$.input-artifacts.{input_name}.artifact-ref"
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    ("input_name", "section", "key", "replacement", "expected_path"),
    [
        (
            "request",
            "repository",
            "owner",
            "other-owner",
            "request.$.repository.owner",
        ),
        (
            "request",
            "repository",
            "name",
            "other-repo",
            "request.$.repository.name",
        ),
        (
            "request",
            "run",
            "workflow",
            "Other Workflow",
            "request.$.run.workflow",
        ),
        (
            "request",
            "run",
            "run-id",
            "999999",
            "request.$.run.run-id",
        ),
        (
            "request",
            "run",
            "run-attempt",
            "99",
            "request.$.run.run-attempt",
        ),
        (
            "changed-files-snapshot",
            "repository",
            "owner",
            "other-owner",
            "changed_files_snapshot.repository.owner",
        ),
        (
            "changed-files-snapshot",
            "repository",
            "name",
            "other-repo",
            "changed_files_snapshot.repository.name",
        ),
        (
            "changed-files-snapshot",
            "run",
            "workflow",
            "Other Workflow",
            "changed_files_snapshot.run.workflow",
        ),
        (
            "changed-files-snapshot",
            "run",
            "run-id",
            "999999",
            "changed_files_snapshot.run.run-id",
        ),
        (
            "changed-files-snapshot",
            "run",
            "run-attempt",
            "99",
            "changed_files_snapshot.run.run-attempt",
        ),
        (
            "fact-snapshot",
            "repository",
            "owner",
            "other-owner",
            "fact_snapshot.repository.owner",
        ),
        (
            "fact-snapshot",
            "repository",
            "name",
            "other-repo",
            "fact_snapshot.repository.name",
        ),
        (
            "fact-snapshot",
            "run",
            "workflow",
            "Other Workflow",
            "fact_snapshot.run.workflow",
        ),
        (
            "fact-snapshot",
            "run",
            "run-id",
            "999999",
            "fact_snapshot.run.run-id",
        ),
        (
            "fact-snapshot",
            "run",
            "run-attempt",
            "99",
            "fact_snapshot.run.run-attempt",
        ),
    ],
)
def test_aggregate_manifest_rejects_supplied_context_envelope_mismatch(
    input_name: str,
    section: str,
    key: str,
    replacement: str,
    expected_path: str,
) -> None:
    """Supplied contexts must share aggregate manifest provenance."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    request_context = _request_document()
    changed_files_snapshot_context = _changed_files_snapshot_document()
    fact_snapshot_context = _fact_snapshot_document()
    if input_name == "request":
        supplied_context = request_context
    elif input_name == "changed-files-snapshot":
        supplied_context = changed_files_snapshot_context
    else:
        supplied_context = fact_snapshot_context
    cast("dict[str, object]", supplied_context[section])[key] = replacement

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
            request=request_context if input_name == "request" else None,
            changed_files_snapshot=changed_files_snapshot_context
            if input_name == "changed-files-snapshot"
            else None,
            fact_snapshot=fact_snapshot_context
            if input_name == "fact-snapshot"
            else None,
        )

    assert any(issue.path == expected_path for issue in exc_info.value.issues)


@pytest.mark.parametrize(
    ("section", "key", "replacement", "expected_path"),
    [
        ("repository", "owner", "other-owner", "$.repository.owner"),
        ("repository", "name", "other-repo", "$.repository.name"),
        ("run", "workflow", "Other Workflow", "$.run.workflow"),
        ("run", "run-id", "999999", "$.run.run-id"),
        ("run", "run-attempt", "99", "$.run.run-attempt"),
    ],
)
def test_aggregate_manifest_rejects_supplied_exec_manifest_envelope_mismatch(
    section: str,
    key: str,
    replacement: str,
    expected_path: str,
) -> None:
    """Supplied execution manifests must share aggregate manifest provenance."""
    plan = _plan()
    manifest = _manifest(plan)
    supplied_manifest = deepcopy(manifest)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    cast("dict[str, object]", supplied_manifest[section])[key] = replacement
    cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            "execution-batch-manifest"
        ],
    )["content-digest"] = ci_validation_execution_batch_manifest_payload_digest(
        supplied_manifest
    )

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            execution_batch_manifest=supplied_manifest,
            request=_request_document(),
        )

    assert any(issue.path == expected_path for issue in exc_info.value.issues)


def test_aggregate_manifest_rejects_missing_projection_authority() -> None:
    """Authoritative aggregate evidence manifests require plan authority."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    aggregate_manifest["projection-authority"] = None

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
            request=_request_document(),
        )


def test_aggregate_manifest_rejects_forged_projection_authority_with_plan() -> (
    None
):
    """Aggregate evidence manifest authority must be plan-derived."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    authority = cast(
        "dict[str, object]",
        aggregate_manifest["projection-authority"],
    )
    authority["mode"] = "push"
    authority["projection-digest"] = canonical_json_digest(
        {
            key: value
            for key, value in authority.items()
            if key != "projection-digest"
        }
    )

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
            request=_request_document(),
        )


@pytest.mark.parametrize(
    ("field", "forged_value"),
    [
        ("mode", "scheduled_full"),
        ("validation-tree", {"commit-sha": "9" * 40, "ref": "refs/heads/main"}),
        ("scheduled-full", {"enabled": True}),
    ],
)
def test_planless_manifest_rejects_forged_projection_authority_fields(
    field: str,
    forged_value: object,
) -> None:
    """Planless authority must match recomputed supplied request projection."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    authority = cast(
        "dict[str, object]",
        aggregate_manifest["projection-authority"],
    )
    authority[field] = forged_value
    _refresh_projection_authority_digest(authority)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path == f"$.projection-authority.{field}"
        for issue in exc_info.value.issues
    )


def test_standalone_manifest_rejects_planless_valid_plan_authority() -> None:
    """Request proof cannot replace a supplied valid validation-plan context."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    _set_input_absent(
        aggregate_manifest,
        "fact-snapshot",
        required=True,
        admissibility="missing",
    )

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
        )

    assert any(
        issue.path == "$.input-artifacts.validation-plan"
        and "supplied validated current-run plan context" in issue.message
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    ("field", "value"),
    [
        (
            "artifact-ref",
            ci_validation_plan_artifact_ref(
                run_id="999999",
                run_attempt=RUN_ATTEMPT,
            ),
        ),
        ("content-digest", "0" * 64),
        ("admissibility", "missing"),
        ("admissibility", "inadmissible"),
    ],
)
def test_standalone_aggregate_manifest_rejects_unbound_plan_authority(
    field: str,
    value: object,
) -> None:
    """Standalone authority requires valid current-run plan provenance."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    validation_plan = cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            "validation-plan"
        ],
    )
    validation_plan[field] = value

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


@pytest.mark.parametrize(
    ("field", "value"),
    [("plan-id", None), ("plan-digest", "0" * 64)],
)
def test_standalone_aggregate_manifest_rejects_unbound_plan_identity(
    field: str,
    value: object,
) -> None:
    """Standalone plan authority must bind to manifest plan identity."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    aggregate_manifest[field] = value

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_standalone_aggregate_manifest_rejects_missing_authority() -> None:
    """Planless validation rejects authoritative manifests without authority."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    aggregate_manifest["projection-authority"] = None

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            execution_batch_manifest=manifest,
        )


def test_freeze_aggregate_manifest_rejects_planless_authority_gap() -> None:
    """Freezing cannot emit valid authoritative manifests without authority."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)

    with pytest.raises(ContractValidationError):
        freeze_ci_validation_aggregate_evidence_manifest(
            created_at=CREATED_AT,
            repository_owner="hcoona",
            repository_name="three",
            workflow="CI Validation",
            run_id=RUN_ID,
            run_attempt=RUN_ATTEMPT,
            input_artifacts=cast(
                "dict[str, object]",
                aggregate_manifest["input-artifacts"],
            ),
            batch_bundles=cast(
                "list[dict[str, object]]",
                aggregate_manifest["batch-bundles"],
            ),
            unexpected_contract_artifacts=[],
            namespace_overflow=cast(
                "dict[str, object]",
                aggregate_manifest["namespace-overflow"],
            ),
            pre_final_validation_artifacts=cast(
                "int",
                aggregate_manifest["pre-final-validation-artifacts"],
            ),
            namespace_closed_at=CREATED_AT,
            execution_batch_manifest=manifest,
        )


def test_aggregate_evidence_manifest_rejects_self_digest_fields() -> None:
    """The pre-final manifest is not a final self-reference record."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    aggregate_manifest["content-digest"] = "0" * 64

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
        )


@pytest.mark.parametrize("mode", ["delete-required", "add-extra"])
def test_aggregate_evidence_manifest_input_artifacts_are_closed(
    mode: str,
) -> None:
    """Input artifact slot names are exact and closed."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    input_artifacts = cast(
        "dict[str, object]", aggregate_manifest["input-artifacts"]
    )
    if mode == "delete-required":
        del input_artifacts["request"]
    else:
        input_artifacts["extra-input"] = _input_artifact(
            None,
            required=False,
            admissibility="not-required",
        )

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
        )


@pytest.mark.parametrize(
    ("input_name", "artifact_instance_id"),
    [
        ("request", None),
        ("request", ""),
        ("validation-plan", None),
        ("validation-plan", ""),
        ("changed-files-snapshot", None),
        ("changed-files-snapshot", ""),
        ("fact-snapshot", None),
        ("fact-snapshot", ""),
        ("execution-batch-manifest", None),
        ("execution-batch-manifest", ""),
    ],
)
def test_aggregate_evidence_manifest_rejects_missing_valid_input_instance_id(
    input_name: str,
    artifact_instance_id: object,
) -> None:
    """Required valid input artifacts must carry non-empty instance ids."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    input_artifact = cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            input_name
        ],
    )
    input_artifact["artifact-instance-id"] = artifact_instance_id

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
        )

    assert any(
        issue.path == f"$.input-artifacts.{input_name}.artifact-instance-id"
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    ("input_name", "artifact_ref"),
    [
        (
            "validation-plan",
            ci_validation_plan_artifact_ref(
                run_id="999999",
                run_attempt=RUN_ATTEMPT,
            ),
        ),
        (
            "execution-batch-manifest",
            ci_validation_execution_batch_manifest_artifact_ref(
                run_id="999999",
                run_attempt=RUN_ATTEMPT,
            ),
        ),
        (
            "changed-files-snapshot",
            ci_validation_changed_files_snapshot_artifact_ref(
                run_id="999999",
                run_attempt=RUN_ATTEMPT,
            ),
        ),
        (
            "fact-snapshot",
            ci_validation_fact_snapshot_artifact_ref(
                run_id="999999",
                run_attempt=RUN_ATTEMPT,
            ),
        ),
    ],
)
def test_aggregate_evidence_manifest_rejects_wrong_plan_or_manifest_input_ref(
    input_name: str,
    artifact_ref: str,
) -> None:
    """Required input artifacts are bound to current run refs."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    input_artifact = cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            input_name
        ],
    )
    input_artifact["artifact-ref"] = artifact_ref

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
        )

    assert any(
        issue.path == f"$.input-artifacts.{input_name}.artifact-ref"
        for issue in exc_info.value.issues
    )


def test_aggregate_evidence_manifest_rejects_wrong_input_run_ref() -> None:
    """Input artifact slots are bound to the current run envelope."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    request = cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            "request"
        ],
    )
    request["artifact-ref"] = ci_validation_request_artifact_ref(
        run_id="999",
        run_attempt=RUN_ATTEMPT,
    )

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
        )

    assert any(
        issue.path == "$.input-artifacts.request.artifact-ref"
        for issue in exc_info.value.issues
    )


def test_aggregate_evidence_manifest_rejects_wrong_manifest_digest() -> None:
    """Execution-batch manifest input digest proves the exact input manifest."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    batch_input = cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            "execution-batch-manifest"
        ],
    )
    batch_input["content-digest"] = "0" * 64

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
        )


def test_aggregate_manifest_requires_execution_manifest_without_context() -> (
    None
):
    """G1 aggregate evidence always reserves the execution manifest input."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    _set_input_absent(
        aggregate_manifest,
        "execution-batch-manifest",
        required=False,
        admissibility="not-required",
    )
    aggregate_manifest["pre-final-validation-artifacts"] = 5

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
        )

    assert any(
        issue.path.startswith("$.input-artifacts.execution-batch-manifest")
        for issue in exc_info.value.issues
    )


def test_aggregate_manifest_rejects_noncanonical_batch_bundle_order() -> None:
    """Aggregate evidence bundle slots use canonical deterministic ordering."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    cast("list[dict[str, object]]", aggregate_manifest["batch-bundles"])[:] = [
        _batch_bundle_slot("batch-z-gate"),
        _batch_bundle_slot("batch-a-gate"),
    ]
    aggregate_manifest["pre-final-validation-artifacts"] = 7

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
        )

    assert any(
        issue.path == "$.batch-bundles" and issue.message == "must be sorted"
        for issue in exc_info.value.issues
    )


def test_aggregate_manifest_batch_slot_json_unsafe_fails_closed() -> None:
    """JSON-unsafe aggregate manifest slots raise contract errors."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    slot = cast(
        "dict[str, object]",
        cast("list[dict[str, object]]", aggregate_manifest["batch-bundles"])[0],
    )
    slot["diagnostics"] = [float("nan")]

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
        )

    assert any(issue.path == "$" for issue in exc_info.value.issues)


def test_aggregate_manifest_freezer_emits_canonical_batch_bundle_order() -> (
    None
):
    """Aggregate evidence freezing sorts bundle slots canonically."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    _set_input_absent(
        aggregate_manifest,
        "execution-batch-manifest",
        required=True,
        admissibility="inadmissible",
    )
    cast(
        "dict[str, object]",
        aggregate_manifest["namespace-overflow"],
    )["observed-prefixed-artifact-count-lower-bound"] = 7
    batch_bundles = [
        _batch_bundle_slot("batch-z-gate"),
        _batch_bundle_slot("batch-a-gate"),
    ]
    for slot in batch_bundles:
        slot["slot-admissibility"] = "inadmissible"
        slot["admitted-candidate-id"] = None
        candidate = cast(
            "dict[str, object]",
            cast("list[dict[str, object]]", slot["observed-candidates"])[0],
        )
        candidate["content-digest"] = None
        candidate["producer-verification"] = "not-checked"
        candidate["payload-readable"] = False
        candidate["admissibility"] = "inadmissible"
    frozen = freeze_ci_validation_aggregate_evidence_manifest(
        created_at=CREATED_AT,
        repository_owner="hcoona",
        repository_name="three",
        workflow="CI Validation",
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        input_artifacts=cast(
            "dict[str, object]",
            aggregate_manifest["input-artifacts"],
        ),
        batch_bundles=[
            *batch_bundles,
        ],
        unexpected_contract_artifacts=[],
        namespace_overflow=cast(
            "dict[str, object]",
            aggregate_manifest["namespace-overflow"],
        ),
        pre_final_validation_artifacts=7,
        namespace_closed_at=CREATED_AT,
        plan=plan,
        request=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )

    assert [
        item["batch-id"]
        for item in cast("list[dict[str, object]]", frozen["batch-bundles"])
    ] == ["batch-a-gate", "batch-z-gate"]


def test_aggregate_evidence_manifest_rejects_missing_or_duplicate_batches() -> (
    None
):
    """Batch bundle slots must exactly cover manifest batches once."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    batch_bundles = cast(
        "list[dict[str, object]]", aggregate_manifest["batch-bundles"]
    )
    batch_bundles.append(dict(batch_bundles[0]))

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
        )


def test_aggregate_evidence_manifest_rejects_removed_batch_bundle() -> None:
    """Batch bundle slots cannot omit execution manifest batches."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    cast("list[dict[str, object]]", aggregate_manifest["batch-bundles"]).clear()

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
        )


def test_aggregate_evidence_manifest_rejects_bundle_not_required() -> None:
    """Bundle admissibility excludes the input-only not-required state."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    cast("list[dict[str, object]]", aggregate_manifest["batch-bundles"])[0][
        "slot-admissibility"
    ] = "not-required"

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
        )


def test_aggregate_manifest_rejects_valid_bundle_without_exec_manifest() -> (
    None
):
    """Valid bundle slots require an authoritative execution manifest."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    _set_input_absent(
        aggregate_manifest,
        "execution-batch-manifest",
        required=True,
        admissibility="missing",
    )

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
        )

    assert any(
        issue.path == "$.batch-bundles[0].slot-admissibility"
        for issue in exc_info.value.issues
    )


def test_aggregate_manifest_rejects_planless_valid_bundle_with_exec() -> None:
    """Valid bundles require projection authority, not just exec manifest."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    _set_input_absent(
        aggregate_manifest,
        "validation-plan",
        required=True,
        admissibility="missing",
    )
    aggregate_manifest["plan-id"] = None
    aggregate_manifest["plan-digest"] = None
    aggregate_manifest["projection-authority"] = None

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path == "$.batch-bundles[0].slot-admissibility"
        for issue in exc_info.value.issues
    )
    assert any(
        issue.path == "$.batch-bundles[0].observed-candidates[0].admissibility"
        for issue in exc_info.value.issues
    )


def test_aggregate_manifest_rejects_unverified_admitted_candidate() -> None:
    """Admitted candidate must be unique, readable, verified, and valid."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    candidate = cast(
        "dict[str, object]",
        cast(
            "list[dict[str, object]]",
            cast(
                "list[dict[str, object]]", aggregate_manifest["batch-bundles"]
            )[0]["observed-candidates"],
        )[0],
    )
    candidate["producer-verification"] = "producer-unverified"

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
        )


def test_aggregate_manifest_rejects_wrong_inadmissible_candidate_id() -> None:
    """Every observed candidate id binds deterministic candidate identity."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    candidates = cast(
        "list[dict[str, object]]",
        cast("list[dict[str, object]]", aggregate_manifest["batch-bundles"])[0][
            "observed-candidates"
        ],
    )
    candidates.append(
        {
            "candidate-id": "candidate-z",
            "artifact-instance-id": None,
            "content-digest": None,
            "producer-verification": "not-checked",
            "payload-readable": False,
            "admissibility": "inadmissible",
            "diagnostics": [],
        }
    )

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
        )

    assert any(
        issue.path == "$.batch-bundles[0].observed-candidates[1].candidate-id"
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("artifact-instance-id", None),
        ("content-digest", None),
        ("content-digest", "not-a-digest"),
        ("candidate-id", "candidate-" + "0" * 64),
    ],
)
def test_aggregate_manifest_rejects_unbound_admitted_candidate(
    field: str,
    value: object,
) -> None:
    """Admitted candidates bind identity, digest, and instance."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    candidate = cast(
        "dict[str, object]",
        cast(
            "list[dict[str, object]]",
            cast(
                "list[dict[str, object]]", aggregate_manifest["batch-bundles"]
            )[0]["observed-candidates"],
        )[0],
    )
    candidate[field] = value

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
        )


@pytest.mark.parametrize(
    "input_name",
    [
        "request",
        "validation-plan",
        "changed-files-snapshot",
        "fact-snapshot",
        "execution-batch-manifest",
    ],
)
def test_aggregate_evidence_manifest_rejects_wrong_input_digests(
    input_name: str,
) -> None:
    """Aggregate inputs are digest-bound to the frozen contract objects."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            input_name
        ],
    )["content-digest"] = "0" * 64
    if input_name == "fact-snapshot":
        with pytest.raises(ContractValidationError):
            validate_ci_validation_aggregate_evidence_manifest(
                aggregate_manifest,
                plan=plan,
                execution_batch_manifest=manifest,
                request=_request_document(),
                changed_files_snapshot=_changed_files_snapshot_document(),
                fact_snapshot=_fact_snapshot_document(),
            )
        return

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
        )


@pytest.mark.parametrize("context_mode", ["with-plan", "planless"])
def test_aggregate_manifest_rejects_valid_request_without_context(
    context_mode: str,
) -> None:
    """Valid request input cannot be authoritative without request context."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    plan_context = plan if context_mode == "with-plan" else None

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan_context,
            execution_batch_manifest=manifest,
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path == "$.input-artifacts.request.admissibility"
        and "requires proven request context" in issue.message
        for issue in exc_info.value.issues
    )


def test_aggregate_manifest_accepts_valid_bound_request_context() -> None:
    """Valid request input remains accepted when bound to request context."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)

    validate_ci_validation_aggregate_evidence_manifest(
        aggregate_manifest,
        plan=plan,
        execution_batch_manifest=manifest,
        request=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )


@pytest.mark.parametrize("context_mode", ["with-plan", "planless"])
def test_aggregate_manifest_rejects_changed_files_without_context(
    context_mode: str,
) -> None:
    """Required valid changed-files input needs snapshot context proof."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    plan_context = plan if context_mode == "with-plan" else None

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan_context,
            execution_batch_manifest=manifest,
            request=_request_document(),
        )

    assert any(
        issue.path == "$.input-artifacts.changed-files-snapshot.admissibility"
        and "requires proven changed_files_snapshot context" in issue.message
        for issue in exc_info.value.issues
    )


def test_aggregate_summary_revalidates_manifest_input_context_proof() -> None:
    """Summary-side manifest revalidation keeps valid input context proof."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)

    with pytest.raises(ContractValidationError) as direct_exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
        )

    with pytest.raises(ContractValidationError) as summary_exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
        )

    direct_context_issues = {
        (issue.path, issue.message)
        for issue in direct_exc_info.value.issues
        if "requires proven" in issue.message
    }
    summary_context_issues = {
        (issue.path, issue.message)
        for issue in summary_exc_info.value.issues
        if "requires proven" in issue.message
    }
    assert (
        "$.input-artifacts.request.admissibility",
        "valid request input requires proven request context",
    ) in direct_context_issues
    assert (
        "$.input-artifacts.changed-files-snapshot.admissibility",
        (
            "valid changed-files-snapshot input requires proven "
            "changed_files_snapshot context"
        ),
    ) in direct_context_issues
    assert direct_context_issues <= summary_context_issues


def test_aggregate_manifest_preserves_not_required_changed_files() -> None:
    """Not-required changed-files rows do not need snapshot context proof."""
    snapshot = _scheduled_full_plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    input_artifacts = _aggregate_input_artifacts(plan, {"placeholder": True})
    input_artifacts["execution-batch-manifest"] = _input_artifact(
        None,
        required=True,
        admissibility="missing",
    )
    aggregate_manifest = freeze_ci_validation_aggregate_evidence_manifest(
        created_at=CREATED_AT,
        repository_owner="hcoona",
        repository_name="three",
        workflow="CI Validation",
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        input_artifacts=input_artifacts,
        batch_bundles=[],
        unexpected_contract_artifacts=[],
        namespace_overflow={
            "detected": False,
            "observed-prefixed-artifact-count-lower-bound": 4,
            "max-prefixed-validation-artifacts": 18,
            "diagnostics": [],
        },
        pre_final_validation_artifacts=4,
        namespace_closed_at=CREATED_AT,
        plan=plan,
        request=cast("dict[str, object]", _scheduled_full_request()),
        fact_snapshot=cast("dict[str, object]", snapshot.fact_snapshot),
    )

    validate_ci_validation_aggregate_evidence_manifest(
        aggregate_manifest,
        plan=plan,
        request=cast("dict[str, object]", _scheduled_full_request()),
        fact_snapshot=cast("dict[str, object]", snapshot.fact_snapshot),
    )


@pytest.mark.parametrize(
    ("context_name", "context"),
    [
        ("request", {"request-digest": "0" * 64}),
        ("changed-files-snapshot", {"changed-files-hash": "0" * 64}),
        ("fact-snapshot", {"fact-snapshot-id": "0" * 64}),
    ],
)
def test_aggregate_manifest_rejects_context_overriding_plan_digest(
    context_name: str,
    context: dict[str, object],
) -> None:
    """Plan-frozen input digests remain authoritative over extra context."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    kwargs = {
        "request": None,
        "changed_files_snapshot": None,
        "fact_snapshot": None,
    }
    if context_name == "request":
        kwargs["request"] = context
    elif context_name == "changed-files-snapshot":
        kwargs["changed_files_snapshot"] = context
    else:
        kwargs["fact_snapshot"] = context

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
            **kwargs,
        )


@pytest.mark.parametrize(
    "input_name", ["changed-files-snapshot", "fact-snapshot"]
)
def test_aggregate_manifest_rejects_plan_required_snapshot_downgrade(
    input_name: str,
) -> None:
    """Plan-required snapshots cannot be downgraded to optional inputs."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    _set_input_absent(
        aggregate_manifest,
        input_name,
        required=False,
        admissibility="not-required",
    )
    if input_name == "changed-files-snapshot":
        _set_input_absent(
            aggregate_manifest,
            "fact-snapshot",
            required=True,
            admissibility="missing",
        )
    _set_input_absent(
        aggregate_manifest,
        "request",
        required=True,
        admissibility="missing",
    )

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
        )


@pytest.mark.parametrize(
    ("input_name", "plan_mutation"),
    [
        ("fact-snapshot", "fact"),
    ],
)
def test_aggregate_manifest_rejects_invalid_plan_not_required_snapshot_state(
    input_name: str,
    plan_mutation: str,
) -> None:
    """Invalid supplied plan snapshot fails before no-authority fallback."""
    plan = _plan()
    if plan_mutation == "changed-files":
        snapshot = _scheduled_full_plan_snapshot()
        plan = cast("dict[str, object]", deepcopy(snapshot.plan))
        affected_range = cast("dict[str, object]", plan["affected-range"])
        affected_range["status"] = "not-applicable"
        affected_range["changed-files-hash"] = None
    else:
        cast("dict[str, object]", plan["fact-snapshot"])["status"] = (
            "unavailable"
        )
    plan["plan-digest"] = ci_validation_plan_digest(plan)
    manifest = _manifest(_plan())
    manifest["plan-digest"] = plan["plan-digest"]
    original_plan = plan if input_name == "changed-files-snapshot" else _plan()
    original_manifest = (
        manifest
        if input_name == "changed-files-snapshot"
        else _manifest(original_plan)
    )
    bundle = _bundle(original_plan, original_manifest)
    aggregate_manifest = _aggregate_evidence_manifest(
        original_plan, original_manifest, bundle
    )
    aggregate_manifest["plan-id"] = plan["plan-id"]
    aggregate_manifest["plan-digest"] = plan["plan-digest"]
    cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            "validation-plan"
        ],
    )["content-digest"] = plan["plan-digest"]
    cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            "execution-batch-manifest"
        ],
    )["content-digest"] = ci_validation_execution_batch_manifest_payload_digest(
        manifest
    )
    _set_input_absent(
        aggregate_manifest,
        input_name,
        required=False,
        admissibility="not-required",
    )
    if input_name == "changed-files-snapshot":
        _set_input_absent(
            aggregate_manifest,
            "fact-snapshot",
            required=True,
            admissibility="missing",
        )
    _set_input_absent(
        aggregate_manifest,
        "request",
        required=True,
        admissibility="missing",
    )
    aggregate_manifest["pre-final-validation-artifacts"] = 5
    slot = cast(
        "dict[str, object]",
        cast("list[dict[str, object]]", aggregate_manifest["batch-bundles"])[0],
    )
    slot["slot-admissibility"] = "missing"
    slot["admitted-candidate-id"] = None
    slot["observed-candidates"] = []
    aggregate_manifest["projection-authority"] = None

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
            changed_files_snapshot=_changed_files_snapshot_document()
            if input_name != "changed-files-snapshot"
            else None,
            fact_snapshot=_fact_snapshot_document()
            if input_name not in {"changed-files-snapshot", "fact-snapshot"}
            else None,
            _require_authoritative_snapshot_inputs=False,
        )

    assert any(
        issue.path == "$.fact-snapshot.status"
        for issue in exc_info.value.issues
    )


def test_aggregate_manifest_rejects_valid_execution_manifest_no_doc() -> None:
    """Valid execution-batch manifest input must have document context."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
        )

    assert any(
        issue.path == "$.input-artifacts.execution-batch-manifest"
        and issue.message
        == "valid admissibility requires execution-batch manifest document"
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    ("input_name", "plan_mutation"),
    [
        ("changed-files-snapshot", "changed-files"),
        ("fact-snapshot", "fact"),
    ],
)
def test_aggregate_manifest_rejects_plan_not_required_snapshot_payload(
    input_name: str,
    plan_mutation: str,
) -> None:
    """Actual not-required snapshots are unexpected, not valid inputs."""
    assert input_name in {"changed-files-snapshot", "fact-snapshot"}
    plan = _plan()
    if plan_mutation == "changed-files":
        cast("dict[str, object]", plan["affected-range"])[
            "changed-files-hash"
        ] = ""
    else:
        cast("dict[str, object]", plan["fact-snapshot"])["status"] = (
            "unavailable"
        )
    plan["plan-digest"] = ci_validation_plan_digest(plan)
    manifest = _manifest(_plan())
    manifest["plan-digest"] = plan["plan-digest"]
    original_plan = _plan()
    original_manifest = _manifest(original_plan)
    bundle = _bundle(original_plan, original_manifest)
    aggregate_manifest = _aggregate_evidence_manifest(
        original_plan, original_manifest, bundle
    )
    aggregate_manifest["plan-id"] = plan["plan-id"]
    aggregate_manifest["plan-digest"] = plan["plan-digest"]
    cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            "validation-plan"
        ],
    )["content-digest"] = plan["plan-digest"]
    cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            "execution-batch-manifest"
        ],
    )["content-digest"] = ci_validation_execution_batch_manifest_payload_digest(
        manifest
    )

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
        )


def test_aggregate_manifest_rejects_inconsistent_prefinal_count() -> None:
    """Pre-final artifact count is derived from input slots and bundles."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    aggregate_manifest["pre-final-validation-artifacts"] = 5

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
        )


def test_aggregate_manifest_rejects_prefinal_count_over_limit() -> None:
    """Pre-final count must leave two final aggregate artifact slots."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    batch_bundles = cast(
        "list[dict[str, object]]", aggregate_manifest["batch-bundles"]
    )
    batch_bundles[:] = [
        _batch_bundle_slot(f"batch-extra-{index:02d}") for index in range(14)
    ]
    aggregate_manifest["pre-final-validation-artifacts"] = 19

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
        )


def test_aggregate_summary_freezes_and_validates() -> None:
    """Aggregate summary binds evidence and reserves final slots."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)

    validate_ci_validation_aggregate_summary(
        summary,
        plan=plan,
        aggregate_evidence_manifest=aggregate_manifest,
        admitted_batch_evidence_bundles=[bundle],
        execution_batch_manifest=manifest,
        request=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )

    assert summary["kind"] == CiValidationKind.AGGREGATE_SUMMARY.value
    assert summary[
        "artifact-ref"
    ] == ci_validation_aggregate_summary_artifact_ref(
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
    )
    assert ci_validation_aggregate_summary_payload_digest(summary) == (
        canonical_json_digest(summary)
    )


@pytest.mark.parametrize(
    ("section", "key", "replacement", "expected_path"),
    [
        ("repository", "owner", "other-owner", "$.repository.owner"),
        ("repository", "name", "other-repo", "$.repository.name"),
        ("run", "workflow", "Other Workflow", "$.run.workflow"),
        ("run", "run-id", "999999", "$.run.run-id"),
        ("run", "run-attempt", "99", "$.run.run-attempt"),
    ],
)
def test_aggregate_summary_rejects_supplied_exec_manifest_envelope_mismatch(
    section: str,
    key: str,
    replacement: str,
    expected_path: str,
) -> None:
    """Supplied execution manifests must share summary provenance."""
    plan = _plan()
    manifest = _manifest(plan)
    supplied_manifest = deepcopy(manifest)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    cast("dict[str, object]", supplied_manifest[section])[key] = replacement
    cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            "execution-batch-manifest"
        ],
    )["content-digest"] = ci_validation_execution_batch_manifest_payload_digest(
        supplied_manifest
    )
    _refresh_summary_manifest_digest(summary, aggregate_manifest)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=supplied_manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(issue.path == expected_path for issue in exc_info.value.issues)


@pytest.mark.parametrize(
    ("section", "key", "replacement", "expected_path"),
    [
        (
            "repository",
            "owner",
            "other-owner",
            "$.aggregate-evidence-manifest.repository.owner",
        ),
        (
            "repository",
            "name",
            "other-repo",
            "$.aggregate-evidence-manifest.repository.name",
        ),
        (
            "run",
            "workflow",
            "Other Workflow",
            "$.aggregate-evidence-manifest.run.workflow",
        ),
        (
            "run",
            "run-id",
            "999999",
            "$.aggregate-evidence-manifest.run.run-id",
        ),
        (
            "run",
            "run-attempt",
            "99",
            "$.aggregate-evidence-manifest.run.run-attempt",
        ),
    ],
)
def test_aggregate_summary_rejects_supplied_manifest_envelope_mismatch(
    section: str,
    key: str,
    replacement: str,
    expected_path: str,
) -> None:
    """Supplied aggregate manifests must share summary provenance."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    cast("dict[str, object]", aggregate_manifest[section])[key] = replacement
    _refresh_summary_manifest_digest(summary, aggregate_manifest)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(issue.path == expected_path for issue in exc_info.value.issues)


def test_aggregate_summary_rejects_extra_satisfied_evidence_row() -> None:
    """Satisfied evidence rows must be exactly payload-derived and planned."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    extra = cast(
        "dict[str, object]",
        deepcopy(
            cast("list[dict[str, object]]", summary["evidence-results"])[0]
        ),
    )
    extra["evidence-expectation-id"] = "evidence-forged-gate"
    extra["work-group-id"] = "wg-forged-gate"
    cast("list[dict[str, object]]", summary["evidence-results"]).append(extra)
    cast("dict[str, object]", summary["work-groups"])["executable-required"] = 2
    cast("dict[str, object]", summary["work-groups"])["required-succeeded"] = 2

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


@pytest.mark.parametrize(
    "diagnostic",
    [
        {"diagnostic-id": "legacy-only"},
        _diagnostic("unknown-code", code="unknown-code"),
        _diagnostic(
            "wrong-detail", code="namespace-closure-failure", detail="build"
        ),
        {**_diagnostic("missing-severity"), "severity": None},
        {
            **_diagnostic("extra-source-key"),
            "source": {"type": "aggregation", "id": None, "extra": "forged"},
        },
    ],
)
def test_aggregate_summary_rejects_malformed_failure_diagnostics(
    diagnostic: dict[str, object],
) -> None:
    """Summary failures require complete registered diagnostic records."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_namespace_failure(summary)
    cast("list[dict[str, object]]", summary["failures"])[0]["diagnostic"] = (
        diagnostic
    )

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


@pytest.mark.parametrize("field", ["source", "severity", "verdict-effect"])
def test_aggregate_summary_rejects_missing_required_failure_diagnostic_fields(
    field: str,
) -> None:
    """Summary failure diagnostics require source, severity, and effect."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_namespace_failure(summary)
    diagnostic = cast(
        "dict[str, object]",
        cast("list[dict[str, object]]", summary["failures"])[0]["diagnostic"],
    )
    del diagnostic[field]

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_aggregate_summary_rejects_failure_diagnostic_code_mismatch() -> None:
    """Failure kind must bind to the diagnostic code family."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _make_summary_batch_evidence_missing(summary, aggregate_manifest)
    diagnostic = cast(
        "dict[str, object]",
        cast("list[dict[str, object]]", summary["failures"])[0]["diagnostic"],
    )
    diagnostic["code"] = "blocking-validation-failure"
    diagnostic["detail"] = "blocking-validation-failure"

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_aggregate_summary_rejects_duplicate_failure_attributions() -> None:
    """Failure attribution rows cannot be duplicated and hidden by sets."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_namespace_failure(summary)
    failures = cast("list[dict[str, object]]", summary["failures"])
    failures.append(cast("dict[str, object]", deepcopy(failures[-1])))

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_aggregate_summary_rejects_noncanonical_failure_order() -> None:
    """Summary failures must be in canonical deterministic order."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_duration_failure(summary)
    cast("list[dict[str, object]]", summary["failures"]).reverse()

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path == "$.failures" and issue.message == "must be sorted"
        for issue in exc_info.value.issues
    )


def test_aggregate_summary_rejects_noncanonical_batch_bundle_order() -> None:
    """Summary batch bundle rows must be in canonical deterministic order."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    rows = cast("list[dict[str, object]]", summary["batch-bundles"])
    extra = cast("dict[str, object]", deepcopy(rows[0]))
    extra["batch-id"] = "batch-a-gate"
    extra["artifact-ref"] = ci_validation_batch_evidence_bundle_artifact_ref(
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        batch_id="batch-a-gate",
    )
    rows.append(extra)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path == "$.batch-bundles" and issue.message == "must be sorted"
        for issue in exc_info.value.issues
    )


def test_aggregate_summary_batch_bundle_json_unsafe_fails_closed() -> None:
    """JSON-unsafe summary bundle rows raise contract errors, not TypeError."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    row = cast(
        "dict[str, object]",
        cast("list[dict[str, object]]", summary["batch-bundles"])[0],
    )
    row["diagnostics"] = [float("nan")]

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(issue.path == "$" for issue in exc_info.value.issues)


def test_aggregate_summary_failure_json_unsafe_fails_closed() -> None:
    """JSON-unsafe summary failures raise contract errors, not TypeError."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_duration_failure(summary)
    failure = cast("list[dict[str, object]]", summary["failures"])[0]
    cast("dict[str, object]", failure["diagnostic"])["message"] = float("nan")

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(issue.path == "$" for issue in exc_info.value.issues)


@pytest.mark.parametrize("terminal_aggregation", [None, "legacy-aggregate"])
def test_aggregate_summary_rejects_invalid_terminal_aggregation(
    terminal_aggregation: str | None,
) -> None:
    """Aggregate summaries must prove terminal aggregation is present."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    work_groups = cast("dict[str, object]", summary["work-groups"])
    if terminal_aggregation is None:
        work_groups.pop("terminal-aggregation")
    else:
        work_groups["terminal-aggregation"] = terminal_aggregation

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path == "$.work-groups.terminal-aggregation"
        for issue in exc_info.value.issues
    )


def test_aggregate_summary_freezer_emits_terminal_aggregation_present() -> None:
    """The aggregate summary freezer records terminal aggregation presence."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)

    summary = _aggregate_summary(plan, aggregate_manifest, bundle)

    assert (
        cast("dict[str, object]", summary["work-groups"])[
            "terminal-aggregation"
        ]
        == "present"
    )


def test_aggregate_summary_json_unsafe_supplied_plan_fails_closed() -> None:
    """JSON-unsafe plan context raises contract errors, not TypeError."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    unsafe_plan = deepcopy(plan)
    unsafe_plan["json-unsafe"] = {"not-json"}

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=unsafe_plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(issue.path == "plan-digest" for issue in exc_info.value.issues)


def test_freezer_json_unsafe_projection_authority_fails_closed() -> None:
    """JSON-unsafe manifest authority raises contract errors, not TypeError."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    authority = cast(
        "dict[str, object]",
        aggregate_manifest["projection-authority"],
    )
    authority["request"] = {"not-json"}

    with pytest.raises(ContractValidationError) as exc_info:
        freeze_ci_validation_aggregate_summary(
            created_at=CREATED_AT,
            repository_owner="hcoona",
            repository_name="three",
            workflow="CI Validation",
            run_id=RUN_ID,
            run_attempt=RUN_ATTEMPT,
            aggregate_evidence_manifest=cast(
                "dict[str, object]", summary["aggregate-evidence-manifest"]
            ),
            final_artifacts=cast(
                "dict[str, object]", summary["final-artifacts"]
            ),
            validation_tree=cast(
                "dict[str, object]", summary["validation-tree"]
            ),
            affected_range=cast("dict[str, object]", summary["affected-range"]),
            request=cast("dict[str, object]", summary["request"]),
            scheduled_full=cast("dict[str, object]", summary["scheduled-full"]),
            verdict=cast("str", summary["verdict"]),
            reason=cast("dict[str, object]", summary["reason"]),
            budgets=cast("dict[str, object]", summary["budgets"]),
            diagnostics=cast("list[dict[str, object]]", summary["diagnostics"]),
            batch_bundles=cast(
                "list[dict[str, object]]", summary["batch-bundles"]
            ),
            evidence_results=cast(
                "list[dict[str, object]]", summary["evidence-results"]
            ),
            failures=cast("list[dict[str, object]]", summary["failures"]),
            work_groups=cast("dict[str, object]", summary["work-groups"]),
            aggregate_evidence_manifest_document=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request_document=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    expected_path = (
        "aggregate_evidence_manifest_document"
        ".projection-authority.projection-digest"
    )
    assert any(issue.path == expected_path for issue in exc_info.value.issues)


def test_aggregate_summary_freezer_emits_canonical_failure_order() -> None:
    """Summary freezing sorts failure rows canonically."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_duration_failure(summary)
    failures = list(
        reversed(cast("list[dict[str, object]]", summary["failures"]))
    )

    frozen = freeze_ci_validation_aggregate_summary(
        created_at=CREATED_AT,
        repository_owner="hcoona",
        repository_name="three",
        workflow="CI Validation",
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        aggregate_evidence_manifest=cast(
            "dict[str, object]", summary["aggregate-evidence-manifest"]
        ),
        final_artifacts=cast("dict[str, object]", summary["final-artifacts"]),
        validation_tree=cast("dict[str, object]", summary["validation-tree"]),
        affected_range=cast("dict[str, object]", summary["affected-range"]),
        request=cast("dict[str, object]", summary["request"]),
        scheduled_full=cast("dict[str, object]", summary["scheduled-full"]),
        verdict=cast("str", summary["verdict"]),
        reason=cast("dict[str, object]", summary["reason"]),
        budgets=cast("dict[str, object]", summary["budgets"]),
        diagnostics=cast("list[dict[str, object]]", summary["diagnostics"]),
        batch_bundles=cast("list[dict[str, object]]", summary["batch-bundles"]),
        evidence_results=cast(
            "list[dict[str, object]]", summary["evidence-results"]
        ),
        failures=failures,
        work_groups=cast("dict[str, object]", summary["work-groups"]),
        plan=plan,
        aggregate_evidence_manifest_document=aggregate_manifest,
        admitted_batch_evidence_bundles=[bundle],
        execution_batch_manifest=manifest,
        request_document=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )

    assert cast("list[dict[str, object]]", frozen["failures"]) == sorted(
        failures,
        key=_summary_failure_sort_key,
    )


def test_aggregate_summary_rejects_forged_work_group_failure_source() -> None:
    """Evidence failure diagnostics must cite their attributed work group."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_failed_evidence(summary)
    failure = cast("list[dict[str, object]]", summary["failures"])[-1]
    diagnostic = cast("dict[str, object]", failure["diagnostic"])
    diagnostic["source"] = {"type": "work-group", "id": "wg-forged"}

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[_failed_bundle(bundle)],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_aggregate_summary_rejects_forged_final_failure_source() -> None:
    """Final evidence failures must come from aggregate provenance."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_missing_aggregate_manifest(summary)
    failure = next(
        item
        for item in cast("list[dict[str, object]]", summary["failures"])
        if item["kind"] == "final-evidence-failure"
        and cast("dict[str, object]", item["diagnostic"])["detail"]
        == "aggregate-summary-without-manifest"
    )
    diagnostic = cast("dict[str, object]", failure["diagnostic"])
    diagnostic["source"] = {"type": "work-group", "id": "wg-python-gate"}

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_aggregate_summary_accepts_canonical_failure_diagnostics() -> None:
    """Registered final and batch failure diagnostics are valid records."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _make_summary_batch_evidence_missing(summary, aggregate_manifest)

    validate_ci_validation_aggregate_summary(
        summary,
        plan=plan,
        aggregate_evidence_manifest=aggregate_manifest,
        execution_batch_manifest=manifest,
        request=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )


def test_aggregate_summary_accepts_canonical_fail_closed_diagnostic() -> None:
    """Canonical fail-closed diagnostics bind to their derived cause."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_missing_aggregate_manifest(summary)

    validate_ci_validation_aggregate_summary(
        summary,
        plan=plan,
        execution_batch_manifest=manifest,
        request=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )


def test_aggregate_summary_rejects_unbound_supplied_plan_projection() -> None:
    """Out-of-band plans cannot project through invalid plan provenance."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle, manifest)
    _set_input_absent(
        aggregate_manifest,
        "validation-plan",
        required=True,
        admissibility="missing",
    )
    aggregate_manifest["projection-authority"] = None
    _make_summary_batch_evidence_missing(summary, aggregate_manifest)
    _mark_summary_required_input_failure(summary)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path == "$.projection-authority"
        for issue in exc_info.value.issues
    )


def test_freeze_aggregate_summary_requires_fail_closed_unbound_plan() -> None:
    """Freezing rejects plan projection without bound plan provenance."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    _set_input_absent(
        aggregate_manifest,
        "validation-plan",
        required=True,
        admissibility="missing",
    )
    aggregate_manifest["projection-authority"] = None

    with pytest.raises(ContractValidationError):
        _aggregate_summary(plan, aggregate_manifest, bundle, manifest)

    fail_closed = _aggregate_summary(
        plan,
        _aggregate_evidence_manifest(plan, manifest, bundle),
        bundle,
        manifest,
    )
    _make_summary_batch_evidence_missing(fail_closed, aggregate_manifest)
    _mark_summary_required_input_failure(fail_closed)
    _set_summary_unknown_projection(fail_closed)
    frozen = freeze_ci_validation_aggregate_summary(
        created_at=CREATED_AT,
        repository_owner="hcoona",
        repository_name="three",
        workflow="CI Validation",
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        aggregate_evidence_manifest=cast(
            "dict[str, object]",
            fail_closed["aggregate-evidence-manifest"],
        ),
        final_artifacts=cast(
            "dict[str, object]", fail_closed["final-artifacts"]
        ),
        validation_tree=cast(
            "dict[str, object]", fail_closed["validation-tree"]
        ),
        affected_range=cast("dict[str, object]", fail_closed["affected-range"]),
        request=cast("dict[str, object]", fail_closed["request"]),
        scheduled_full=cast("dict[str, object]", fail_closed["scheduled-full"]),
        verdict=cast("str", fail_closed["verdict"]),
        reason=cast("dict[str, object]", fail_closed["reason"]),
        budgets=cast("dict[str, object]", fail_closed["budgets"]),
        diagnostics=cast("list[dict[str, object]]", fail_closed["diagnostics"]),
        batch_bundles=cast(
            "list[dict[str, object]]", fail_closed["batch-bundles"]
        ),
        evidence_results=cast(
            "list[dict[str, object]]",
            fail_closed["evidence-results"],
        ),
        failures=cast("list[dict[str, object]]", fail_closed["failures"]),
        work_groups=cast("dict[str, object]", fail_closed["work-groups"]),
        plan=plan,
        aggregate_evidence_manifest_document=aggregate_manifest,
        execution_batch_manifest=manifest,
        request_document=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )

    assert frozen["mode"] == "unknown"


def test_aggregate_summary_accepts_bound_supplied_plan_projection() -> None:
    """A bound validation-plan input authorizes projection."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle, manifest)

    validate_ci_validation_aggregate_summary(
        summary,
        plan=plan,
        aggregate_evidence_manifest=aggregate_manifest,
        admitted_batch_evidence_bundles=[bundle],
        execution_batch_manifest=manifest,
        request=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )


def _multi_cause_fail_closed_fixture() -> tuple[
    dict[str, object],
    dict[str, object],
    dict[str, object],
    dict[str, object],
    dict[str, object],
]:
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _set_input_absent(
        aggregate_manifest,
        "validation-plan",
        required=True,
        admissibility="missing",
    )
    aggregate_manifest["projection-authority"] = None
    _make_summary_batch_evidence_missing(summary, aggregate_manifest)
    cast(
        "list[dict[str, object]]",
        aggregate_manifest["unexpected-contract-artifacts"],
    ).append(
        {
            "physical-artifact-name": "three-ci-validation-" + "9" * 64,
            "artifact-instance-id": "9001",
            "classification": "unexpected",
            "diagnostics": [],
        }
    )
    _sort_unexpected_artifacts(aggregate_manifest)
    cast(
        "dict[str, object]",
        aggregate_manifest["namespace-overflow"],
    )["observed-prefixed-artifact-count-lower-bound"] = 7
    _refresh_summary_manifest_digest(summary, aggregate_manifest)
    _mark_summary_required_input_failure(summary)
    _set_summary_unknown_projection(summary)
    _mark_summary_namespace_failure(summary)
    return plan, manifest, bundle, aggregate_manifest, summary


def test_aggregate_summary_accepts_all_fail_closed_causes() -> None:
    """Every derived fail-closed cause can be attributed independently."""
    plan, manifest, _bundle, aggregate_manifest, summary = (
        _multi_cause_fail_closed_fixture()
    )

    validate_ci_validation_aggregate_summary(
        summary,
        plan=plan,
        aggregate_evidence_manifest=aggregate_manifest,
        execution_batch_manifest=manifest,
        request=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )


def test_aggregate_summary_rejects_missing_fail_closed_cause() -> None:
    """Fail-closed attribution must exactly cover every derived cause."""
    plan, manifest, _bundle, aggregate_manifest, summary = (
        _multi_cause_fail_closed_fixture()
    )
    cast("list[dict[str, object]]", summary["failures"])[:] = [
        failure
        for failure in cast("list[dict[str, object]]", summary["failures"])
        if not (
            failure["kind"] == "fail-closed"
            and cast("dict[str, object]", failure["diagnostic"])["detail"]
            == "unexpected-contract-artifact"
        )
    ]

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )
    assert any("fail-closed" in issue.message for issue in error.value.issues)


def test_aggregate_summary_rejects_extra_fail_closed_cause() -> None:
    """Fail-closed attribution cannot include non-derived causes."""
    plan, manifest, _bundle, aggregate_manifest, summary = (
        _multi_cause_fail_closed_fixture()
    )
    _append_summary_fail_closed_failure(
        summary,
        _diagnostic(
            "fail-closed/forged-extra",
            code="final-evidence-failure",
            detail="aggregate-summary-without-manifest",
            severity="fail-closed",
            verdict_effect="fail-closed",
        ),
        "Forged extra fail-closed cause.",
    )

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )
    assert any("fail-closed" in issue.message for issue in error.value.issues)


def test_aggregate_summary_rejects_wrong_fail_closed_cause_in_set() -> None:
    """A wrong per-cause fail-closed row cannot stand in for a real cause."""
    plan, manifest, _bundle, aggregate_manifest, summary = (
        _multi_cause_fail_closed_fixture()
    )
    failure = next(
        failure
        for failure in cast("list[dict[str, object]]", summary["failures"])
        if failure["kind"] == "fail-closed"
    )
    diagnostic = cast("dict[str, object]", failure["diagnostic"])
    diagnostic["code"] = "final-evidence-failure"
    diagnostic["detail"] = "aggregate-summary-without-manifest"
    _sort_summary_failures(summary)

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )
    assert any("fail-closed" in issue.message for issue in error.value.issues)


def test_aggregate_summary_rejects_duplicate_fail_closed_cause() -> None:
    """Fail-closed attribution cannot duplicate one exact cause."""
    plan, manifest, _bundle, aggregate_manifest, summary = (
        _multi_cause_fail_closed_fixture()
    )
    failure = next(
        failure
        for failure in cast("list[dict[str, object]]", summary["failures"])
        if failure["kind"] == "fail-closed"
    )
    cast("list[dict[str, object]]", summary["failures"]).append(
        cast("dict[str, object]", deepcopy(failure))
    )
    _sort_summary_failures(summary)

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )
    assert any("fail-closed" in issue.message for issue in error.value.issues)


def test_aggregate_summary_rejects_forged_fail_closed_source() -> None:
    """Fail-closed diagnostics must use aggregate-level provenance."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_missing_aggregate_manifest(summary)
    failure = next(
        failure
        for failure in cast("list[dict[str, object]]", summary["failures"])
        if failure["kind"] == "fail-closed"
    )
    diagnostic = cast("dict[str, object]", failure["diagnostic"])
    diagnostic["source"] = {"type": "work-group", "id": "wg-python-gate"}

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


@pytest.mark.parametrize(
    ("code", "detail"),
    [
        ("namespace-closure-failure", "unexpected-contract-artifact"),
        ("final-evidence-failure", "required-input-artifact-failure"),
    ],
)
def test_aggregate_summary_rejects_wrong_fail_closed_cause(
    code: str,
    detail: str,
) -> None:
    """Fail-closed diagnostics must match the derived failure cause."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_missing_aggregate_manifest(summary)
    failure = next(
        failure
        for failure in cast("list[dict[str, object]]", summary["failures"])
        if failure["kind"] == "fail-closed"
    )
    diagnostic = cast("dict[str, object]", failure["diagnostic"])
    diagnostic["code"] = code
    diagnostic["detail"] = detail

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_aggregate_summary_requires_manifest_without_plan_context() -> None:
    """Non-invalid summaries always need aggregate manifest authority."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    cast("list[dict[str, object]]", summary["batch-bundles"]).clear()
    cast("list[dict[str, object]]", summary["evidence-results"]).clear()
    work_groups = cast("dict[str, object]", summary["work-groups"])
    work_groups["executable-required"] = 0
    work_groups["required-succeeded"] = 0

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(summary)


@pytest.mark.parametrize(
    ("field", "forged_value"),
    [
        ("mode", "push"),
        ("validation-tree", {"commit-sha": "9" * 40, "ref": "refs/heads/main"}),
        (
            "affected-range",
            {
                "status": "not-applicable",
                "base-sha": None,
                "base-tip-sha": None,
                "head-sha": None,
                "changed-files-hash": None,
            },
        ),
        ("request", {"artifact-ref": None, "request-digest": "9" * 64}),
        ("scheduled-full", {"enabled": True}),
    ],
)
def test_aggregate_summary_freezer_binds_plan_projection(
    field: str,
    forged_value: object,
) -> None:
    """Plan context is authoritative over caller-supplied summary projection."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    validation_tree = cast("dict[str, object]", plan["validation-tree"])
    affected_range = _summary_affected_range(plan)
    request = cast("dict[str, object]", plan["request"])
    scheduled_full = cast("dict[str, object]", plan["scheduled-full"])
    if field == "validation-tree":
        validation_tree = cast("dict[str, object]", forged_value)
    elif field == "affected-range":
        affected_range = cast("dict[str, object]", forged_value)
    elif field == "request":
        request = cast("dict[str, object]", forged_value)
    elif field == "scheduled-full":
        scheduled_full = cast("dict[str, object]", forged_value)
    manifest_digest = ci_validation_aggregate_evidence_manifest_payload_digest(
        aggregate_manifest,
    )
    summary = freeze_ci_validation_aggregate_summary(
        created_at=CREATED_AT,
        repository_owner="hcoona",
        repository_name="three",
        workflow="CI Validation",
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        aggregate_evidence_manifest={
            "artifact-ref": aggregate_manifest["artifact-ref"],
            "artifact-instance-id": "3001",
            "content-digest": manifest_digest,
        },
        validation_tree=validation_tree,
        affected_range=affected_range,
        request=request,
        scheduled_full=scheduled_full,
        final_artifacts=cast(
            "dict[str, object]",
            _aggregate_summary(plan, aggregate_manifest, bundle)[
                "final-artifacts"
            ],
        ),
        verdict="passed",
        reason={
            "invalid-plan": False,
            "fail-closed": False,
            "required-evidence-missing": False,
            "required-evidence-skipped": False,
            "blocking-validation-failure": False,
            "inadmissible-batch-evidence": False,
            "namespace-closure-failure": False,
            "aggregate-duration-exceeded": False,
            "final-evidence-failure": False,
        },
        budgets={
            "pre-final-validation-artifacts": 6,
            "expected-final-validation-artifacts": 2,
            "expected-actual-validation-artifacts": 8,
            "max-validation-artifacts": 20,
            "actual-execution-batches": 1,
            "actual-total-jobs": 1,
            "actual-windows-jobs": 0,
            "aggregate-duration-seconds": 10,
            "aggregate-target-duration-seconds": 60,
            "aggregate-max-duration-seconds": 120,
        },
        diagnostics=[],
        batch_bundles=cast(
            "list[dict[str, object]]",
            _aggregate_summary(plan, aggregate_manifest, bundle)[
                "batch-bundles"
            ],
        ),
        evidence_results=cast(
            "list[dict[str, object]]",
            _aggregate_summary(plan, aggregate_manifest, bundle)[
                "evidence-results"
            ],
        ),
        failures=[],
        work_groups=cast(
            "dict[str, object]",
            _aggregate_summary(plan, aggregate_manifest, bundle)["work-groups"],
        ),
        plan=plan,
        aggregate_evidence_manifest_document=aggregate_manifest,
        admitted_batch_evidence_bundles=[bundle],
        execution_batch_manifest=manifest,
        request_document=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )

    expected = {
        "mode": plan["mode"],
        "validation-tree": plan["validation-tree"],
        "affected-range": _summary_affected_range(plan),
        "request": plan["request"],
        "scheduled-full": plan["scheduled-full"],
    }
    assert summary[field] == expected[field]


def test_aggregate_summary_freezer_requires_manifest_document() -> None:
    """Authoritative inputs need the manifest payload."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)

    with pytest.raises(ContractValidationError):
        freeze_ci_validation_aggregate_summary(
            created_at=CREATED_AT,
            repository_owner="hcoona",
            repository_name="three",
            workflow="CI Validation",
            run_id=RUN_ID,
            run_attempt=RUN_ATTEMPT,
            aggregate_evidence_manifest=cast(
                "dict[str, object]", summary["aggregate-evidence-manifest"]
            ),
            final_artifacts=cast(
                "dict[str, object]", summary["final-artifacts"]
            ),
            validation_tree=cast(
                "dict[str, object]", summary["validation-tree"]
            ),
            affected_range=cast("dict[str, object]", summary["affected-range"]),
            request=cast("dict[str, object]", summary["request"]),
            scheduled_full=cast("dict[str, object]", summary["scheduled-full"]),
            verdict=cast("str", summary["verdict"]),
            reason=cast("dict[str, object]", summary["reason"]),
            budgets=cast("dict[str, object]", summary["budgets"]),
            diagnostics=cast("list[dict[str, object]]", summary["diagnostics"]),
            batch_bundles=cast(
                "list[dict[str, object]]", summary["batch-bundles"]
            ),
            evidence_results=cast(
                "list[dict[str, object]]", summary["evidence-results"]
            ),
            failures=cast("list[dict[str, object]]", summary["failures"]),
            work_groups=cast("dict[str, object]", summary["work-groups"]),
            plan=plan,
            execution_batch_manifest=manifest,
            request_document=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_aggregate_summary_freezer_accepts_missing_manifest_fail_closed() -> (
    None
):
    """Freezing supports explicit missing-manifest fail-closed summaries."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_missing_aggregate_manifest(summary)

    frozen = freeze_ci_validation_aggregate_summary(
        created_at=CREATED_AT,
        repository_owner="hcoona",
        repository_name="three",
        workflow="CI Validation",
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        aggregate_evidence_manifest=cast(
            "dict[str, object]", summary["aggregate-evidence-manifest"]
        ),
        final_artifacts=cast("dict[str, object]", summary["final-artifacts"]),
        validation_tree=cast("dict[str, object]", summary["validation-tree"]),
        affected_range=cast("dict[str, object]", summary["affected-range"]),
        request=cast("dict[str, object]", summary["request"]),
        scheduled_full=cast("dict[str, object]", summary["scheduled-full"]),
        verdict=cast("str", summary["verdict"]),
        reason=cast("dict[str, object]", summary["reason"]),
        budgets=cast("dict[str, object]", summary["budgets"]),
        diagnostics=cast("list[dict[str, object]]", summary["diagnostics"]),
        batch_bundles=cast("list[dict[str, object]]", summary["batch-bundles"]),
        evidence_results=cast(
            "list[dict[str, object]]", summary["evidence-results"]
        ),
        failures=cast("list[dict[str, object]]", summary["failures"]),
        work_groups=cast("dict[str, object]", summary["work-groups"]),
        plan=plan,
        execution_batch_manifest=manifest,
        request_document=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )

    validate_ci_validation_aggregate_summary(
        frozen,
        plan=plan,
        execution_batch_manifest=manifest,
        request=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )


def test_aggregate_summary_freezer_rejects_no_authority_missing_manifest() -> (
    None
):
    """Missing authority is not accepted unless it is fail-closed explicit."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)

    with pytest.raises(ContractValidationError):
        freeze_ci_validation_aggregate_summary(
            created_at=CREATED_AT,
            repository_owner="hcoona",
            repository_name="three",
            workflow="CI Validation",
            run_id=RUN_ID,
            run_attempt=RUN_ATTEMPT,
            aggregate_evidence_manifest=cast(
                "dict[str, object]", summary["aggregate-evidence-manifest"]
            ),
            final_artifacts=cast(
                "dict[str, object]", summary["final-artifacts"]
            ),
            validation_tree=cast(
                "dict[str, object]", summary["validation-tree"]
            ),
            affected_range=cast("dict[str, object]", summary["affected-range"]),
            request=cast("dict[str, object]", summary["request"]),
            scheduled_full=cast("dict[str, object]", summary["scheduled-full"]),
            verdict=cast("str", summary["verdict"]),
            reason=cast("dict[str, object]", summary["reason"]),
            budgets=cast("dict[str, object]", summary["budgets"]),
            diagnostics=cast("list[dict[str, object]]", summary["diagnostics"]),
            batch_bundles=cast(
                "list[dict[str, object]]", summary["batch-bundles"]
            ),
            evidence_results=cast(
                "list[dict[str, object]]", summary["evidence-results"]
            ),
            failures=cast("list[dict[str, object]]", summary["failures"]),
            work_groups=cast("dict[str, object]", summary["work-groups"]),
            request_document=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_aggregate_summary_freezer_rejects_manifest_document_mismatch() -> None:
    """Manifest document digest and summary claim must match exactly."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    forged_manifest = cast("dict[str, object]", deepcopy(aggregate_manifest))
    forged_manifest["pre-final-validation-artifacts"] = 7

    with pytest.raises(ContractValidationError):
        freeze_ci_validation_aggregate_summary(
            created_at=CREATED_AT,
            repository_owner="hcoona",
            repository_name="three",
            workflow="CI Validation",
            run_id=RUN_ID,
            run_attempt=RUN_ATTEMPT,
            aggregate_evidence_manifest=cast(
                "dict[str, object]", summary["aggregate-evidence-manifest"]
            ),
            final_artifacts=cast(
                "dict[str, object]", summary["final-artifacts"]
            ),
            validation_tree=cast(
                "dict[str, object]", summary["validation-tree"]
            ),
            affected_range=cast("dict[str, object]", summary["affected-range"]),
            request=cast("dict[str, object]", summary["request"]),
            scheduled_full=cast("dict[str, object]", summary["scheduled-full"]),
            verdict=cast("str", summary["verdict"]),
            reason=cast("dict[str, object]", summary["reason"]),
            budgets=cast("dict[str, object]", summary["budgets"]),
            diagnostics=cast("list[dict[str, object]]", summary["diagnostics"]),
            batch_bundles=cast(
                "list[dict[str, object]]", summary["batch-bundles"]
            ),
            evidence_results=cast(
                "list[dict[str, object]]", summary["evidence-results"]
            ),
            failures=cast("list[dict[str, object]]", summary["failures"]),
            work_groups=cast("dict[str, object]", summary["work-groups"]),
            plan=plan,
            aggregate_evidence_manifest_document=forged_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request_document=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_aggregate_summary_rejects_self_content_digest() -> None:
    """The summary cannot contain the digest of its own uploaded artifact."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    final_artifacts = cast("dict[str, Any]", summary["final-artifacts"])
    aggregate_summary = cast(
        "dict[str, object]", final_artifacts["aggregate-summary"]
    )
    aggregate_summary["content-digest"] = "0" * 64

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_aggregate_summary_rejects_self_producer_verification_field() -> None:
    """Summary producer verification is external to the summary payload."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    aggregate_summary = cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-summary"
        ],
    )
    aggregate_summary["producer-verified"] = False

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_aggregate_summary_rejects_execution_manifest_job_budget_mismatch() -> (
    None
):
    """Summary job budget claims are bound to the execution manifest."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    budgets = cast("dict[str, object]", summary["budgets"])
    budgets["actual-total-jobs"] = 18
    budgets["actual-windows-jobs"] = 8

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("actual-execution-batches", 2),
        ("expected-final-validation-artifacts", 3),
        ("max-validation-artifacts", 19),
        ("aggregate-target-duration-seconds", 59),
        ("aggregate-max-duration-seconds", 119),
        ("pre-final-validation-artifacts", 7),
        ("expected-actual-validation-artifacts", 9),
    ],
)
def test_aggregate_summary_rejects_execution_manifest_budget_mismatch(
    field: str,
    value: object,
) -> None:
    """Each manifest-bound summary budget field is independently enforced."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    cast("dict[str, object]", summary["budgets"])[field] = value

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_execution_batch_manifest_rejects_aggregate_duration_over_limit() -> (
    None
):
    """Aggregate job budget is capped at two minutes."""
    plan = _plan()
    budget = _budget(1)
    budget["aggregate-max-duration-seconds"] = 121
    budget["aggregate-target-duration-seconds"] = 121

    with pytest.raises(ContractValidationError):
        freeze_ci_validation_execution_batch_manifest(
            plan=plan,
            batches=[_batch(plan)],
            budget=budget,
            created_at=CREATED_AT,
        )


@pytest.mark.parametrize(
    ("key", "value"),
    [
        ("max-total-jobs", 19),
        ("actual-total-jobs", 19),
        ("max-windows-jobs", 9),
        ("actual-windows-jobs", 9),
        ("actual-validation-artifacts", 21),
        ("expected-final-validation-artifacts", 3),
        ("max-execution-batches", 14),
    ],
)
def test_execution_batch_manifest_rejects_budget_caps(
    key: str,
    value: int,
) -> None:
    """Topology budget caps are contract-enforced."""
    plan = _plan()
    budget = _budget(1)
    budget[key] = value

    with pytest.raises(ContractValidationError):
        freeze_ci_validation_execution_batch_manifest(
            plan=plan,
            batches=[_batch(plan)],
            budget=budget,
            created_at=CREATED_AT,
        )


def test_execution_batch_manifest_rejects_runner_family_mismatch() -> None:
    """Execution batches bind to assigned plan runner families."""
    plan = _plan()
    batch = _batch(plan)
    batch["runner-family"] = "windows"

    with pytest.raises(ContractValidationError):
        freeze_ci_validation_execution_batch_manifest(
            plan=plan,
            batches=[batch],
            budget=_budget(1),
            created_at=CREATED_AT,
        )


def test_execution_batch_manifest_rejects_underreported_windows_jobs() -> None:
    """Windows job totals include Windows execution batches."""
    plan = _plan()
    for group in cast("list[dict[str, object]]", plan["work-groups"]):
        if group["kind"] != "evidence-aggregation":
            group["runner-family"] = "windows"
            break
    plan["plan-digest"] = ci_validation_plan_digest(plan)
    batch = _batch(plan)
    batch["runner-family"] = "windows"
    selector = cast("list[dict[str, object]]", batch["ordered-selectors"])[0]
    slot = cast("dict[str, object]", selector["expected-evidence-slot"])
    slot["runner-family"] = "windows"
    budget = _budget(1)
    budget["actual-windows-jobs"] = 0

    with pytest.raises(ContractValidationError):
        freeze_ci_validation_execution_batch_manifest(
            plan=plan,
            batches=[batch],
            budget=budget,
            created_at=CREATED_AT,
        )


def test_execution_batch_manifest_rejects_overreported_windows_jobs() -> None:
    """Windows job totals cannot claim jobs outside the manifest topology."""
    plan = _plan()
    budget = _budget(1)
    budget["actual-windows-jobs"] = 1

    with pytest.raises(ContractValidationError):
        freeze_ci_validation_execution_batch_manifest(
            plan=plan,
            batches=[_batch(plan)],
            budget=budget,
            created_at=CREATED_AT,
        )


def test_execution_batch_manifest_rejects_underreported_control_windows() -> (
    None
):
    """Windows job totals include relevant control-plane runners."""
    plan = _plan()
    for group in cast("list[dict[str, object]]", plan["work-groups"]):
        if group["kind"] == "evidence-aggregation":
            group["runner-family"] = "windows"
            break
    plan["plan-digest"] = ci_validation_plan_digest(plan)
    budget = _budget(1)
    budget["actual-windows-jobs"] = 0

    with pytest.raises(ContractValidationError):
        freeze_ci_validation_execution_batch_manifest(
            plan=plan,
            batches=[_batch(plan)],
            budget=budget,
            created_at=CREATED_AT,
        )


def test_execution_batch_manifest_rejects_max_batches_above_dynamic_bound() -> (
    None
):
    """Control-plane jobs reduce the dynamic max execution batch allowance."""
    plan = _plan()
    budget = _budget(1)
    budget["non-batch-control-plane-job-count"] = 10
    budget["actual-total-jobs"] = 11
    budget["max-execution-batches"] = 13

    with pytest.raises(ContractValidationError):
        freeze_ci_validation_execution_batch_manifest(
            plan=plan,
            batches=[_batch(plan)],
            budget=budget,
            created_at=CREATED_AT,
        )


def test_execution_batch_manifest_rejects_forged_artifact_headroom_budget() -> (
    None
):
    """Plan-bound validation recomputes budget artifact headroom."""
    plan = _plan()
    budget = _budget(1)
    budget["expected-input-non-bundle-validation-artifacts"] = 17
    budget["pre-final-validation-artifacts"] = 18
    budget["expected-non-bundle-validation-artifacts"] = 19
    budget["actual-validation-artifacts"] = 20
    budget["max-execution-batches"] = 1

    with pytest.raises(ContractValidationError):
        freeze_ci_validation_execution_batch_manifest(
            plan=plan,
            batches=[_batch(plan)],
            budget=budget,
            created_at=CREATED_AT,
        )


def test_execution_batch_manifest_rejects_artifact_headroom_batch_bound() -> (
    None
):
    """Max execution batches must leave room for input and final artifacts."""
    plan = _plan()
    budget = _budget(1)
    budget["expected-input-non-bundle-validation-artifacts"] = 17
    budget["pre-final-validation-artifacts"] = 18
    budget["expected-non-bundle-validation-artifacts"] = 19
    budget["actual-validation-artifacts"] = 20
    budget["max-execution-batches"] = 2

    with pytest.raises(ContractValidationError):
        freeze_ci_validation_execution_batch_manifest(
            plan=plan,
            batches=[_batch(plan)],
            budget=budget,
            created_at=CREATED_AT,
        )


def test_batch_evidence_bundle_rejects_slot_shape_mismatch() -> None:
    """Selector result evidence must match the frozen manifest slot."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    result = cast("list[dict[str, object]]", bundle["selector-results"])[0]
    result["coverage-target"] = {"type": "none", "id": None}

    with pytest.raises(ContractValidationError):
        validate_ci_validation_batch_evidence_bundle(
            bundle,
            plan=plan,
            execution_batch_manifest=manifest,
        )


def test_batch_evidence_bundle_rejects_wrong_evidence_branch() -> None:
    """Selector evidence uses one mutually exclusive branch."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    evidence = cast(
        "dict[str, object]",
        cast("list[dict[str, object]]", bundle["selector-results"])[0][
            "evidence"
        ],
    )
    evidence["category-result"] = {
        "category": "ecosystem-gate",
        "outcome": "success",
        "diagnostics": [],
    }

    with pytest.raises(ContractValidationError):
        validate_ci_validation_batch_evidence_bundle(
            bundle,
            plan=plan,
            execution_batch_manifest=manifest,
        )


@pytest.mark.parametrize(
    ("section", "key", "replacement", "expected_path"),
    [
        ("repository", "owner", "other-owner", "$.repository.owner"),
        ("repository", "name", "other-repo", "$.repository.name"),
        ("run", "workflow", "Other Workflow", "$.run.workflow"),
        ("run", "run-id", "999", "$.run.run-id"),
        ("run", "run-attempt", "99", "$.run.run-attempt"),
    ],
)
def test_batch_bundle_rejects_cross_run_manifest_without_plan(
    section: str,
    key: str,
    replacement: str,
    expected_path: str,
) -> None:
    """Nested execution manifests are envelope-bound without plan context."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    cross_run_manifest = cast("dict[str, object]", deepcopy(manifest))
    cast("dict[str, object]", cross_run_manifest[section])[key] = replacement

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_batch_evidence_bundle(
            bundle,
            execution_batch_manifest=cross_run_manifest,
        )

    assert any(issue.path == expected_path for issue in error.value.issues)


def test_batch_evidence_bundle_rejects_forged_evidence_outcome() -> None:
    """Selector outcome is derived from nested evidence results."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    cast("list[dict[str, object]]", bundle["selector-results"])[0][
        "outcome"
    ] = "success"
    capability_results = cast(
        "list[dict[str, object]]",
        cast(
            "dict[str, object]",
            cast("list[dict[str, object]]", bundle["selector-results"])[0][
                "evidence"
            ],
        )["capability-results"],
    )
    capability_results[0]["outcome"] = "blocking-failure"

    with pytest.raises(ContractValidationError):
        validate_ci_validation_batch_evidence_bundle(
            bundle,
            plan=plan,
            execution_batch_manifest=manifest,
        )


def test_batch_evidence_bundle_rejects_extra_invalid_artifact_ref() -> None:
    """Selector evidence artifact refs are validated and nested-consistent."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    evidence = cast(
        "dict[str, object]",
        cast("list[dict[str, object]]", bundle["selector-results"])[0][
            "evidence"
        ],
    )
    cast("list[object]", evidence["artifact-refs"]).append("bad ref")

    with pytest.raises(ContractValidationError):
        validate_ci_validation_batch_evidence_bundle(
            bundle,
            plan=plan,
            execution_batch_manifest=manifest,
        )


def test_aggregate_summary_rejects_final_manifest_digest_mismatch() -> None:
    """Final manifest slot must match the claim and actual manifest."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    final_manifest = cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )
    final_manifest["content-digest"] = "0" * 64

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_aggregate_summary_rejects_unverified_final_manifest() -> None:
    """The final manifest artifact must be producer verified."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    final_manifest = cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )
    final_manifest["producer-verified"] = False

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_aggregate_summary_rejects_summary_bundle_not_required() -> None:
    """Summary bundle rows use the bundle-only admissibility vocabulary."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    cast("list[dict[str, object]]", summary["batch-bundles"])[0][
        "admissibility"
    ] = "not-required"

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_aggregate_summary_rejects_manifest_admitted_candidate_mismatch() -> (
    None
):
    """Summary bundle candidates must exactly mirror aggregate evidence."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    cast("list[dict[str, object]]", summary["batch-bundles"])[0][
        "admitted-candidate-id"
    ] = "candidate-" + "9" * 64

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_aggregate_summary_rejects_evidence_result_mismatch() -> None:
    """Summary evidence results must be derived from manifest-covered work."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    cast("list[dict[str, object]]", summary["evidence-results"])[0][
        "outcome"
    ] = "failed"

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_aggregate_summary_rejects_empty_manifest_claim_digest() -> None:
    """Summary manifest claim digests are required and non-empty."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    cast("dict[str, object]", summary["aggregate-evidence-manifest"])[
        "content-digest"
    ] = None

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


@pytest.mark.parametrize(
    ("key", "value"),
    [
        ("max-validation-artifacts", 21),
        ("pre-final-validation-artifacts", 19),
        ("expected-actual-validation-artifacts", 21),
        ("actual-execution-batches", 14),
        ("actual-total-jobs", 19),
        ("actual-windows-jobs", 9),
    ],
)
def test_aggregate_summary_rejects_budget_cap_overflow(
    key: str,
    value: int,
) -> None:
    """Summary budgets enforce LLD caps and actual-count relationships."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    cast("dict[str, object]", summary["budgets"])[key] = value

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_aggregate_summary_allows_observed_duration_exceeded_failure() -> None:
    """Observed duration overflow fails final verdict, not schema validation."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    cast("dict[str, object]", summary["budgets"])[
        "aggregate-duration-seconds"
    ] = 121
    reason = cast("dict[str, object]", summary["reason"])
    reason["aggregate-duration-exceeded"] = True
    reason["final-evidence-failure"] = True
    summary["verdict"] = "failed"
    cast("list[dict[str, object]]", summary["failures"]).append(
        {
            "kind": "aggregate-duration-exceeded",
            "batch-id": None,
            "work-group-id": None,
            "evidence-expectation-id": None,
            "bundle-id": None,
            "diagnostic": _diagnostic("aggregate-duration-exceeded"),
            "message": "Aggregate duration exceeded the maximum budget.",
        }
    )
    cast("list[dict[str, object]]", summary["failures"]).append(
        {
            "kind": "final-evidence-failure",
            "batch-id": None,
            "work-group-id": None,
            "evidence-expectation-id": None,
            "bundle-id": None,
            "diagnostic": _diagnostic(
                "final-evidence-failure",
                detail="aggregate-duration-exceeded",
                severity="fail-closed",
                verdict_effect="fail-closed",
            ),
            "message": "Aggregate duration exceeded the maximum budget.",
        }
    )

    validate_ci_validation_aggregate_summary(
        summary,
        plan=plan,
        aggregate_evidence_manifest=aggregate_manifest,
        admitted_batch_evidence_bundles=[bundle],
        execution_batch_manifest=manifest,
        request=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )


@pytest.mark.parametrize(
    ("kind", "diagnostic_id"),
    [
        ("forged-failure-kind", "forged-failure-kind"),
        ("final-evidence-failure", "stale-final-evidence-failure"),
    ],
)
def test_aggregate_summary_rejects_unknown_or_extra_failure_kind(
    kind: str,
    diagnostic_id: str,
) -> None:
    """Failure kinds must be registered and derived from actual failures."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _append_summary_failure(
        summary,
        kind=kind,
        diagnostic_id=diagnostic_id,
        message="Forged failure.",
    )

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_aggregate_summary_rejects_missing_required_evidence_failure() -> None:
    """Missing evidence requires a required-evidence-missing failure entry."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _make_summary_batch_evidence_missing(summary, aggregate_manifest)
    _remove_summary_failure_kind(summary, "required-evidence-missing")

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_aggregate_summary_accepts_skipped_evidence_summary() -> None:
    """Skipped required evidence derives failed skipped summary state."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    skipped_bundle = _skipped_bundle(bundle)
    aggregate_manifest = _aggregate_evidence_manifest(
        plan, manifest, skipped_bundle
    )
    summary = _aggregate_summary(
        plan,
        _aggregate_evidence_manifest(plan, manifest, bundle),
        bundle,
    )
    _refresh_summary_manifest_digest(summary, aggregate_manifest)
    _mark_summary_skipped_evidence(summary)

    validate_ci_validation_aggregate_summary(
        summary,
        plan=plan,
        aggregate_evidence_manifest=aggregate_manifest,
        admitted_batch_evidence_bundles=[skipped_bundle],
        execution_batch_manifest=manifest,
        request=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )

    assert (
        cast("dict[str, object]", summary["reason"])[
            "required-evidence-skipped"
        ]
        is True
    )
    assert (
        cast("dict[str, object]", summary["work-groups"])["required-skipped"]
        == 1
    )


def test_aggregate_summary_rejects_missing_skipped_evidence_failure() -> None:
    """Skipped evidence requires an exactly attributed failure entry."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    skipped_bundle = _skipped_bundle(bundle)
    aggregate_manifest = _aggregate_evidence_manifest(
        plan, manifest, skipped_bundle
    )
    summary = _aggregate_summary(
        plan,
        _aggregate_evidence_manifest(plan, manifest, bundle),
        bundle,
    )
    _refresh_summary_manifest_digest(summary, aggregate_manifest)
    _mark_summary_skipped_evidence(summary)
    _remove_summary_failure_kind(summary, "required-evidence-skipped")

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[skipped_bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_aggregate_summary_rejects_missing_failed_evidence_failure() -> None:
    """Failed evidence requires a blocking-validation-failure entry."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    failed_bundle = _failed_bundle(bundle)
    aggregate_manifest = _aggregate_evidence_manifest(
        plan, manifest, failed_bundle
    )
    _refresh_summary_manifest_digest(summary, aggregate_manifest)
    summary_result = cast(
        "list[dict[str, object]]", summary["evidence-results"]
    )[0]
    summary_result["outcome"] = "failed"
    summary["verdict"] = "failed"
    cast("dict[str, object]", summary["reason"])[
        "blocking-validation-failure"
    ] = True
    work_groups = cast("dict[str, object]", summary["work-groups"])
    work_groups["required-succeeded"] = 0
    work_groups["required-failed"] = 1

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[failed_bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


@pytest.mark.parametrize(
    ("outcome", "kind", "count_key"),
    [
        ("missing", "required-evidence-missing", "required-missing"),
        ("skipped", "required-evidence-skipped", "required-skipped"),
        ("failed", "blocking-validation-failure", "required-failed"),
    ],
)
def test_aggregate_summary_rejects_outcome_failures_without_manifest_authority(
    outcome: str,
    kind: str,
    count_key: str,
) -> None:
    """Non-fail-closed outcome failures still require manifest authority."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _add_extra_work_group(
        plan,
        work_group_id="wg-python-gate-extra",
        evidence_id="evidence-python-gate-extra",
    )
    summary["plan-digest"] = plan["plan-digest"]
    results = cast("list[dict[str, object]]", summary["evidence-results"])
    first = results[0]
    first["outcome"] = outcome
    if outcome == "missing":
        first["batch-id"] = None
        first["bundle-id"] = None
        first["selector-index"] = None
    second = dict(first)
    second["evidence-expectation-id"] = "evidence-python-gate-extra"
    second["work-group-id"] = "wg-python-gate-extra"
    results.append(second)
    summary["verdict"] = "failed"
    reason = cast("dict[str, object]", summary["reason"])
    reason[kind] = True
    cast("list[dict[str, object]]", summary["batch-bundles"])[0][
        "admitted-candidate-id"
    ] = None
    work_groups = cast("dict[str, object]", summary["work-groups"])
    work_groups["executable-required"] = 2
    work_groups["required-succeeded"] = 0
    work_groups[count_key] = 2
    _append_outcome_failure_for_result(summary, first, kind=kind)
    _append_outcome_failure_for_result(summary, second, kind=kind)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            _require_aggregate_evidence_manifest=False,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path == "$.projection-authority"
        or issue.message == "must match no-authority fail-closed projection"
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    ("outcome", "kind", "count_key"),
    [
        ("missing", "required-evidence-missing", "required-missing"),
        ("skipped", "required-evidence-skipped", "required-skipped"),
        ("failed", "blocking-validation-failure", "required-failed"),
    ],
)
def test_aggregate_summary_rejects_wrong_failure_attribution(
    outcome: str,
    kind: str,
    count_key: str,
) -> None:
    """Failure rows cannot cover evidence with wrong exact identifiers."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    result = cast("list[dict[str, object]]", summary["evidence-results"])[0]
    result["outcome"] = outcome
    if outcome == "missing":
        result["batch-id"] = None
        result["bundle-id"] = None
        result["selector-index"] = None
    summary["verdict"] = "failed"
    cast("dict[str, object]", summary["reason"])[kind] = True
    work_groups = cast("dict[str, object]", summary["work-groups"])
    work_groups["required-succeeded"] = 0
    work_groups[count_key] = 1
    _append_outcome_failure_for_result(summary, result, kind=kind)
    cast("list[dict[str, object]]", summary["failures"])[-1][
        "evidence-expectation-id"
    ] = "evidence-wrong"

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_aggregate_summary_rejects_extra_exact_failure_attribution() -> None:
    """Extra same-kind failure rows are rejected."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_failed_evidence(summary)
    extra = dict(cast("list[dict[str, object]]", summary["failures"])[-1])
    extra["evidence-expectation-id"] = "evidence-extra"
    cast("list[dict[str, object]]", summary["failures"]).append(extra)

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_aggregate_summary_rejects_missing_inadmissible_batch_failure() -> None:
    """Inadmissible batch evidence requires its failure entry."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _make_summary_batch_evidence_missing(summary, aggregate_manifest)
    _remove_summary_failure_kind(summary, "inadmissible-batch-evidence")

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_aggregate_summary_rejects_missing_final_evidence_failure() -> None:
    """Final evidence failures require the final-evidence-failure entry."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _set_input_absent(
        aggregate_manifest,
        "execution-batch-manifest",
        required=True,
        admissibility="missing",
    )
    _make_summary_batch_evidence_missing(summary, aggregate_manifest)
    _mark_summary_required_input_failure(summary)
    cast("list[dict[str, object]]", summary["failures"]).clear()

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_aggregate_summary_accepts_all_final_evidence_failure_causes() -> None:
    """Final evidence failures must represent every derived cause once."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _set_input_absent(
        aggregate_manifest,
        "execution-batch-manifest",
        required=True,
        admissibility="missing",
    )
    _make_summary_batch_evidence_missing(summary, aggregate_manifest)
    _mark_summary_required_input_failure(summary)
    _mark_summary_duration_failure(summary)

    validate_ci_validation_aggregate_summary(
        summary,
        plan=plan,
        aggregate_evidence_manifest=aggregate_manifest,
        execution_batch_manifest=manifest,
        request=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )


def test_aggregate_summary_rejects_partial_final_evidence_failure_causes() -> (
    None
):
    """Every derived final evidence failure cause needs its own detail."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _set_input_absent(
        aggregate_manifest,
        "execution-batch-manifest",
        required=True,
        admissibility="missing",
    )
    _make_summary_batch_evidence_missing(summary, aggregate_manifest)
    _mark_summary_required_input_failure(summary)
    _mark_summary_duration_failure(summary)
    failures = cast("list[dict[str, object]]", summary["failures"])
    failures[:] = [
        failure
        for failure in failures
        if not (
            failure["kind"] == "final-evidence-failure"
            and cast("dict[str, object]", failure["diagnostic"])["detail"]
            == "aggregate-duration-exceeded"
        )
    ]

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_aggregate_summary_rejects_forged_passed_duration_overflow() -> None:
    """Duration overflow must derive aggregate-duration-exceeded failure."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    cast("dict[str, object]", summary["budgets"])[
        "aggregate-duration-seconds"
    ] = 121

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(summary, plan=plan)


def test_aggregate_summary_rejects_plan_without_aggregate_manifest() -> None:
    """Plan-scoped validation requires manifest-backed evidence coverage."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    cast("list[dict[str, object]]", summary["evidence-results"]).clear()
    summary["verdict"] = "passed"
    cast("dict[str, object]", summary["work-groups"])["executable-required"] = 0
    cast("dict[str, object]", summary["work-groups"])["required-succeeded"] = 0

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(summary, plan=plan)


def test_aggregate_summary_accepts_invalid_plan_mode() -> None:
    """Invalid-plan summaries fail closed without ordinary evidence rows."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_invalid_plan(summary)

    validate_ci_validation_aggregate_summary(summary)


@pytest.mark.parametrize(
    "mutation",
    [
        "passed",
        "evidence-result",
        "execution-count",
        "work-group-count",
        "failure",
    ],
)
def test_aggregate_summary_rejects_invalid_plan_mode_mismatch(
    mutation: str,
) -> None:
    """Invalid-plan mode has explicit fail-closed zero-count invariants."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_invalid_plan(summary)
    if mutation == "passed":
        summary["verdict"] = "passed"
    elif mutation == "evidence-result":
        cast("list[dict[str, object]]", summary["evidence-results"]).append(
            {
                "evidence-expectation-id": "evidence-python-gate",
                "work-group-id": "wg-python-gate",
                "batch-id": None,
                "bundle-id": None,
                "selector-index": None,
                "outcome": "missing",
                "diagnostics": [],
            }
        )
    elif mutation == "execution-count":
        cast("dict[str, object]", summary["budgets"])[
            "actual-execution-batches"
        ] = 1
    elif mutation == "work-group-count":
        cast("dict[str, object]", summary["work-groups"])[
            "executable-required"
        ] = 1
    else:
        cast("list[dict[str, object]]", summary["failures"]).clear()

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(summary)


@pytest.mark.parametrize(
    "field",
    [
        "mode",
        "plan-id",
        "plan-digest",
        "validation-tree",
        "affected-range",
        "request",
        "scheduled-full",
        "batch-bundles",
    ],
)
def test_aggregate_summary_rejects_stale_invalid_plan_fields(
    field: str,
) -> None:
    """Invalid-plan summaries cannot retain stale plan-derived fields."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    stale_values = {
        "mode": plan["mode"],
        "plan-id": plan["plan-id"],
        "plan-digest": plan["plan-digest"],
        "validation-tree": plan["validation-tree"],
        "affected-range": _summary_affected_range(plan),
        "request": {
            "artifact-ref": ci_validation_request_artifact_ref(
                run_id=RUN_ID,
                run_attempt=RUN_ATTEMPT,
            ),
            "request-digest": cast("dict[str, object]", plan["request"])[
                "request-digest"
            ],
        },
        "scheduled-full": {"enabled": True},
        "batch-bundles": [
            {
                "batch-id": _BATCH_ID,
                "artifact-ref": bundle["artifact-ref"],
                "bundle-id": bundle["bundle-id"],
                "admitted-candidate-id": cast(
                    "list[dict[str, object]]",
                    aggregate_manifest["batch-bundles"],
                )[0]["admitted-candidate-id"],
                "candidate-count": 1,
                "admissibility": "valid",
                "diagnostics": [],
            }
        ],
    }
    _mark_summary_invalid_plan(summary)
    summary[field] = stale_values[field]

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(summary)


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("diagnostic", {"diagnostic-id": "forged-invalid-plan"}),
        ("message", "Forged invalid plan message."),
        ("batch-id", _BATCH_ID),
        ("work-group-id", "wg-python-gate"),
        ("evidence-expectation-id", "evidence-python-gate"),
        ("bundle-id", "bundle-forged"),
    ],
)
def test_aggregate_summary_rejects_forged_invalid_plan_failure_fields(
    field: str,
    value: object,
) -> None:
    """Invalid-plan summaries require the exact canonical failure object."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_invalid_plan(summary)
    cast("list[dict[str, object]]", summary["failures"])[0][field] = value

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(summary)


def test_aggregate_summary_rejects_extra_invalid_plan_failure() -> None:
    """Invalid-plan summaries carry exactly one invalid-plan failure."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_invalid_plan(summary)
    cast("list[dict[str, object]]", summary["failures"]).append(
        {
            "kind": "final-evidence-failure",
            "batch-id": None,
            "work-group-id": None,
            "evidence-expectation-id": None,
            "bundle-id": None,
            "diagnostic": _diagnostic(
                "stale-failure",
                code="final-evidence-failure",
                detail="required-input-artifact-failure",
            ),
            "message": "Stale non-invalid-plan failure.",
        }
    )

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(summary)


@pytest.mark.parametrize(
    ("summary_claim_key", "summary_claim_value", "final_key", "final_value"),
    [
        ("artifact-instance-id", "3001", "artifact-instance-id", "3001"),
        ("content-digest", "0" * 64, "content-digest", "0" * 64),
        ("content-digest", None, "producer-verified", True),
    ],
)
def test_invalid_plan_summary_without_manifest_rejects_authoritative_claims(
    summary_claim_key: str,
    summary_claim_value: object,
    final_key: str,
    final_value: object,
) -> None:
    """Invalid-plan summaries cannot claim manifest authority."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_invalid_plan(summary)
    cast("dict[str, object]", summary["aggregate-evidence-manifest"])[
        summary_claim_key
    ] = summary_claim_value
    cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )[final_key] = final_value

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(summary)


def test_aggregate_summary_freezer_replaces_invalid_plan_failures() -> None:
    """The summary freezer canonicalizes invalid-plan failures."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_ref = ci_validation_aggregate_evidence_manifest_artifact_ref(
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
    )
    reason = {
        "invalid-plan": True,
        "fail-closed": False,
        "required-evidence-missing": False,
        "required-evidence-skipped": False,
        "blocking-validation-failure": False,
        "inadmissible-batch-evidence": False,
        "namespace-closure-failure": False,
        "aggregate-duration-exceeded": False,
        "final-evidence-failure": False,
    }
    summary = freeze_ci_validation_aggregate_summary(
        created_at=CREATED_AT,
        repository_owner="hcoona",
        repository_name="three",
        workflow="CI Validation",
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        aggregate_evidence_manifest={
            "artifact-ref": aggregate_ref,
            "artifact-instance-id": "3001",
            "content-digest": "0" * 64,
        },
        final_artifacts={
            "aggregate-evidence-manifest": {
                "artifact-ref": aggregate_ref,
                "artifact-instance-id": "3001",
                "content-digest": "0" * 64,
                "producer-verified": True,
            },
            "aggregate-summary": {
                "artifact-ref": ci_validation_aggregate_summary_artifact_ref(
                    run_id=RUN_ID,
                    run_attempt=RUN_ATTEMPT,
                ),
            },
        },
        validation_tree=cast("dict[str, object]", plan["validation-tree"]),
        affected_range=_summary_affected_range(plan),
        request={
            "artifact-ref": ci_validation_request_artifact_ref(
                run_id=RUN_ID,
                run_attempt=RUN_ATTEMPT,
            ),
            "request-digest": cast("dict[str, object]", plan["request"])[
                "request-digest"
            ],
        },
        scheduled_full=cast("dict[str, object]", plan["scheduled-full"]),
        verdict="failed",
        reason=reason,
        budgets={
            "pre-final-validation-artifacts": 6,
            "expected-final-validation-artifacts": 2,
            "expected-actual-validation-artifacts": 8,
            "max-validation-artifacts": 20,
            "actual-execution-batches": 1,
            "actual-total-jobs": 1,
            "actual-windows-jobs": 0,
            "aggregate-duration-seconds": 10,
            "aggregate-target-duration-seconds": 60,
            "aggregate-max-duration-seconds": 120,
        },
        diagnostics=[],
        batch_bundles=[
            {
                "batch-id": _BATCH_ID,
                "artifact-ref": bundle["artifact-ref"],
                "bundle-id": bundle["bundle-id"],
                "admitted-candidate-id": "candidate-" + "1" * 64,
                "candidate-count": 1,
                "admissibility": "valid",
                "diagnostics": [],
            },
        ],
        evidence_results=[],
        failures=[
            {
                "kind": "final-evidence-failure",
                "batch-id": None,
                "work-group-id": None,
                "evidence-expectation-id": None,
                "bundle-id": None,
                "diagnostic": _diagnostic(
                    "stale-failure",
                    code="final-evidence-failure",
                    detail="required-input-artifact-failure",
                ),
                "message": "Stale non-invalid-plan failure.",
            }
        ],
        work_groups={
            "executable-required": 1,
            "required-succeeded": 0,
            "required-failed": 0,
            "required-skipped": 0,
            "required-missing": 0,
            "terminal-aggregation": "present",
        },
        request_document=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )

    failures = cast("list[dict[str, object]]", summary["failures"])
    assert len(failures) == 1
    assert failures[0] == {
        "kind": "invalid-plan",
        "batch-id": None,
        "work-group-id": None,
        "evidence-expectation-id": None,
        "bundle-id": None,
        "diagnostic": _diagnostic(
            "invalid-plan",
            detail="plan-missing",
            message="No authoritative validation plan was available.",
            severity="fail-closed",
            verdict_effect="fail-closed",
        ),
        "message": "No authoritative validation plan was available.",
    }
    reason = cast("dict[str, object]", summary["reason"])
    assert reason["invalid-plan"] is True
    assert reason["fail-closed"] is False
    assert all(
        value is False
        for key, value in reason.items()
        if key not in {"invalid-plan"}
    )
    assert (
        cast("dict[str, object]", summary["aggregate-evidence-manifest"])[
            "artifact-instance-id"
        ]
        is None
    )
    final_manifest = cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )
    assert final_manifest["content-digest"] is None
    assert final_manifest["producer-verified"] is False


@pytest.mark.parametrize("field", ["plan-id", "plan-digest"])
def test_aggregate_manifest_rejects_stale_no_authoritative_plan_identity(
    field: str,
) -> None:
    """Missing validation-plan input cannot carry stale plan identity."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    _set_input_absent(
        aggregate_manifest,
        "validation-plan",
        required=True,
        admissibility="missing",
    )
    aggregate_manifest["plan-id"] = None
    aggregate_manifest["plan-digest"] = None
    aggregate_manifest[field] = plan[field]

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            execution_batch_manifest=manifest,
        )


def test_aggregate_manifest_allows_snapshot_absent_input_states() -> None:
    """Snapshot inputs can be explicitly absent without binding."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    _set_input_absent(
        aggregate_manifest,
        "changed-files-snapshot",
        required=True,
        admissibility="missing",
    )
    _set_input_absent(
        aggregate_manifest,
        "fact-snapshot",
        required=True,
        admissibility="missing",
    )
    _set_input_absent(
        aggregate_manifest,
        "fact-snapshot",
        required=True,
        admissibility="missing",
    )
    slot = cast(
        "dict[str, object]",
        cast("list[dict[str, object]]", aggregate_manifest["batch-bundles"])[0],
    )
    slot["slot-admissibility"] = "missing"
    slot["admitted-candidate-id"] = None
    slot["observed-candidates"] = []
    aggregate_manifest["projection-authority"] = None
    validate_ci_validation_aggregate_evidence_manifest(
        aggregate_manifest,
        plan=plan,
        execution_batch_manifest=manifest,
        request=_request_document(),
    )


def test_aggregate_manifest_missing_input_skips_ref_and_digest_binding() -> (
    None
):
    """Missing admissibility is an absent state, not a wrong-ref failure."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    _set_input_absent(
        aggregate_manifest,
        "changed-files-snapshot",
        required=True,
        admissibility="missing",
    )
    _set_input_absent(
        aggregate_manifest,
        "fact-snapshot",
        required=True,
        admissibility="missing",
    )
    slot = cast(
        "dict[str, object]",
        cast("list[dict[str, object]]", aggregate_manifest["batch-bundles"])[0],
    )
    slot["slot-admissibility"] = "missing"
    slot["admitted-candidate-id"] = None
    slot["observed-candidates"] = []
    aggregate_manifest["projection-authority"] = None
    validate_ci_validation_aggregate_evidence_manifest(
        aggregate_manifest,
        plan=plan,
        execution_batch_manifest=manifest,
        request=_request_document(),
    )


def test_aggregate_manifest_supplied_plan_rejects_forged_projection() -> None:
    """Missing plan input cannot authorize supplied-plan projection."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    _set_input_absent(
        aggregate_manifest,
        "validation-plan",
        required=True,
        admissibility="missing",
    )
    authority = _projection_authority(plan)
    authority["mode"] = "push"
    authority["projection-digest"] = canonical_json_digest(
        {
            key: value
            for key, value in authority.items()
            if key != "projection-digest"
        }
    )
    aggregate_manifest["projection-authority"] = authority

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
        )


@pytest.mark.parametrize("admissibility", ["missing", "inadmissible"])
def test_aggregate_manifest_rejects_supplied_plan_contradicting_input_row(
    admissibility: str,
) -> None:
    """Non-valid plan rows cannot authorize supplied-plan projection."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    if admissibility == "missing":
        _set_input_absent(
            aggregate_manifest,
            "validation-plan",
            required=True,
            admissibility="missing",
        )
    else:
        cast(
            "dict[str, object]",
            cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
                "validation-plan"
            ],
        )["admissibility"] = "inadmissible"

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
        )

    assert any(
        issue.path == "$.input-artifacts.validation-plan"
        and "authorize projection" in issue.message
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize("admissibility", ["missing", "inadmissible"])
def test_aggregate_manifest_rejects_supplied_execution_manifest_input_conflict(
    admissibility: str,
) -> None:
    """Non-valid execution rows cannot authorize valid bundle evidence."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    if admissibility == "missing":
        _set_input_absent(
            aggregate_manifest,
            "execution-batch-manifest",
            required=True,
            admissibility="missing",
        )
    else:
        cast(
            "dict[str, object]",
            cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
                "execution-batch-manifest"
            ],
        )["admissibility"] = "inadmissible"

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
        )

    assert any(
        issue.path.endswith(".slot-admissibility")
        and "requires authoritative plan or projection authority"
        in issue.message
        for issue in exc_info.value.issues
    )


def test_aggregate_manifest_without_authoritative_plan_rejects_projection() -> (
    None
):
    """A non-authoritative plan input cannot self-authorize projection."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    _set_input_absent(
        aggregate_manifest,
        "validation-plan",
        required=True,
        admissibility="missing",
    )
    aggregate_manifest["plan-id"] = None
    aggregate_manifest["plan-digest"] = None
    aggregate_manifest["projection-authority"] = _projection_authority(plan)

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            execution_batch_manifest=manifest,
        )


@pytest.mark.parametrize(
    ("input_name", "field", "value"),
    [
        ("request", "required", False),
        ("request", "expected-cardinality", 0),
        ("request", "admissibility", "not-required"),
        ("request", "admissibility", "duplicate"),
        ("validation-plan", "required", False),
        ("changed-files-snapshot", "expected-cardinality", 0),
        ("fact-snapshot", "admissibility", "not-required"),
        ("execution-batch-manifest", "admissibility", "duplicate"),
    ],
)
def test_aggregate_manifest_rejects_invalid_input_artifact_combinations(
    input_name: str,
    field: str,
    value: object,
) -> None:
    """Input artifact slots have per-input required/cardinality rules."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            input_name
        ],
    )[field] = value

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
        )


def test_aggregate_manifest_rejects_not_required_input_with_payload() -> None:
    """Not-required input slots must be absent and unbound."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    artifact = cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            "changed-files-snapshot"
        ],
    )
    artifact["required"] = False
    artifact["expected-cardinality"] = 0
    artifact["admissibility"] = "not-required"

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
        )


def test_aggregate_summary_rejects_forged_passed_bundle_results() -> None:
    """Summary evidence results are derived from admitted bundle payloads."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    bad_bundle = cast("dict[str, object]", deepcopy(bundle))
    result = cast("list[dict[str, object]]", bad_bundle["selector-results"])[0]
    result["outcome"] = "blocking-failure"
    cast(
        "list[dict[str, object]]",
        cast("dict[str, object]", result["evidence"])["capability-results"],
    )[0]["outcome"] = "blocking-failure"
    validate_ci_validation_batch_evidence_bundle(
        bad_bundle,
        plan=plan,
        execution_batch_manifest=manifest,
        **_authorizing_context_kwargs(),
    )
    aggregate_manifest = _aggregate_evidence_manifest(
        plan, manifest, bad_bundle
    )
    _refresh_summary_manifest_digest(summary, aggregate_manifest)

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            execution_batch_manifest=manifest,
            admitted_batch_evidence_bundles=[bad_bundle],
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_aggregate_summary_rejects_wrong_admitted_bundle_digest() -> None:
    """Aggregate admitted candidate digest must match the admitted bundle."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    candidate = cast(
        "dict[str, object]",
        cast(
            "list[dict[str, object]]",
            cast(
                "list[dict[str, object]]", aggregate_manifest["batch-bundles"]
            )[0]["observed-candidates"],
        )[0],
    )
    candidate["content-digest"] = "0" * 64
    _refresh_summary_manifest_digest(summary, aggregate_manifest)

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            execution_batch_manifest=manifest,
            admitted_batch_evidence_bundles=[bundle],
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_aggregate_summary_rejects_missing_admitted_bundle_payload() -> None:
    """Every valid admitted aggregate slot needs its bundle payload."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            execution_batch_manifest=manifest,
            admitted_batch_evidence_bundles=[],
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_aggregate_summary_requires_admitted_bundle_payload_argument() -> None:
    """Valid admitted manifest slots require real admitted bundle payloads."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_aggregate_summary_requires_payload_for_satisfied_rows() -> None:
    """Satisfied evidence rows require bundle payload validation."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_aggregate_summary_rejects_forged_batch_bundle_without_manifest() -> (
    None
):
    """Admitted bundle payloads bind summary batch rows without the manifest."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    cast("list[dict[str, object]]", summary["batch-bundles"])[0][
        "bundle-id"
    ] = "bundle-forged"

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            execution_batch_manifest=manifest,
            admitted_batch_evidence_bundles=[bundle],
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_namespace_overflow_lower_bound_requires_detected() -> None:
    """Observed namespace overflow cannot be hidden by detected=false."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    overflow = cast(
        "dict[str, object]", aggregate_manifest["namespace-overflow"]
    )
    overflow["observed-prefixed-artifact-count-lower-bound"] = 21
    overflow["max-prefixed-validation-artifacts"] = 20
    overflow["detected"] = False

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
        )


def test_namespace_overflow_uses_fixed_prefinal_cap() -> None:
    """Namespace overflow cannot raise the contract cap in its payload."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    overflow = cast(
        "dict[str, object]", aggregate_manifest["namespace-overflow"]
    )
    overflow["max-prefixed-validation-artifacts"] = 19

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
        )


def test_namespace_lower_bound_includes_unexpected_prefixed_artifacts() -> None:
    """Namespace lower-bound must include expected and unexpected artifacts."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    cast(
        "list[dict[str, object]]",
        aggregate_manifest["unexpected-contract-artifacts"],
    ).extend(_unexpected_artifact(index) for index in range(25))
    overflow = cast(
        "dict[str, object]", aggregate_manifest["namespace-overflow"]
    )
    overflow["observed-prefixed-artifact-count-lower-bound"] = 6
    overflow["detected"] = False

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
        )

    assert any(
        issue.path
        == "$.namespace-overflow.observed-prefixed-artifact-count-lower-bound"
        for issue in exc_info.value.issues
    )
    assert any(
        issue.path == "$.namespace-overflow.detected"
        for issue in exc_info.value.issues
    )


def test_namespace_lower_bound_accepts_coherent_non_overflow() -> None:
    """Unexpected prefixed artifacts are accepted below the fixed cap."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    cast(
        "list[dict[str, object]]",
        aggregate_manifest["unexpected-contract-artifacts"],
    ).extend(_unexpected_artifact(index) for index in range(2))
    _sort_unexpected_artifacts(aggregate_manifest)
    cast(
        "dict[str, object]",
        aggregate_manifest["namespace-overflow"],
    )["observed-prefixed-artifact-count-lower-bound"] = 8

    validate_ci_validation_aggregate_evidence_manifest(
        aggregate_manifest,
        plan=plan,
        execution_batch_manifest=manifest,
        request=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )


def test_unexpected_artifacts_reject_non_canonical_order() -> None:
    """Unexpected artifact rows are ordered by their run-bound implicit ids."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    frozen = freeze_ci_validation_aggregate_evidence_manifest(
        created_at=CREATED_AT,
        repository_owner="hcoona",
        repository_name="three",
        workflow="CI Validation",
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        input_artifacts=cast(
            "dict[str, object]",
            aggregate_manifest["input-artifacts"],
        ),
        batch_bundles=cast(
            "list[dict[str, object]]",
            aggregate_manifest["batch-bundles"],
        ),
        unexpected_contract_artifacts=[
            _unexpected_artifact(1),
            _unexpected_artifact(2),
        ],
        namespace_overflow={
            "detected": False,
            "observed-prefixed-artifact-count-lower-bound": 8,
            "max-prefixed-validation-artifacts": 18,
            "diagnostics": [],
        },
        pre_final_validation_artifacts=6,
        namespace_closed_at=CREATED_AT,
        plan=plan,
        execution_batch_manifest=manifest,
        request=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )
    cast(
        "list[dict[str, object]]",
        frozen["unexpected-contract-artifacts"],
    ).reverse()

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            frozen,
            plan=plan,
            execution_batch_manifest=manifest,
        )

    assert any(
        issue.path == "$.unexpected-contract-artifacts"
        and issue.message == "must be sorted"
        for issue in exc_info.value.issues
    )


def test_unexpected_artifacts_reject_duplicate_implicit_ids() -> None:
    """Unexpected artifact implicit ids include run, instance, and class."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    cast(
        "list[dict[str, object]]",
        aggregate_manifest["unexpected-contract-artifacts"],
    ).extend([_unexpected_artifact(9), _unexpected_artifact(9)])
    cast(
        "dict[str, object]",
        aggregate_manifest["namespace-overflow"],
    )["observed-prefixed-artifact-count-lower-bound"] = 8

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
        )

    assert any(
        issue.path == "$.unexpected-contract-artifacts"
        and issue.message == "implicit ids must be unique"
        for issue in exc_info.value.issues
    )


def test_namespace_lower_bound_accepts_coherent_overflow() -> None:
    """Overflow is coherent when detected covers the computed lower-bound."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    cast(
        "list[dict[str, object]]",
        aggregate_manifest["unexpected-contract-artifacts"],
    ).extend(_unexpected_artifact(index) for index in range(13))
    _sort_unexpected_artifacts(aggregate_manifest)
    overflow = cast(
        "dict[str, object]", aggregate_manifest["namespace-overflow"]
    )
    overflow["observed-prefixed-artifact-count-lower-bound"] = 19
    overflow["detected"] = True

    validate_ci_validation_aggregate_evidence_manifest(
        aggregate_manifest,
        plan=plan,
        execution_batch_manifest=manifest,
        request=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )


def test_unexpected_artifact_rejects_non_hex_physical_name() -> None:
    """Unexpected artifact closure uses the canonical physical-name grammar."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    cast(
        "list[dict[str, object]]",
        aggregate_manifest["unexpected-contract-artifacts"],
    ).append(
        {
            "physical-artifact-name": "three-ci-validation-not64hex",
            "artifact-instance-id": "9001",
            "classification": "unexpected",
            "diagnostics": [],
        }
    )

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
        )


@pytest.mark.parametrize("failure_kind", ["overflow", "unexpected"])
def test_aggregate_summary_rejects_passed_namespace_closure_failure(
    failure_kind: str,
) -> None:
    """Namespace closure failures require fail-closed failed summaries."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    if failure_kind == "overflow":
        overflow = cast(
            "dict[str, object]", aggregate_manifest["namespace-overflow"]
        )
        overflow["detected"] = True
        overflow["observed-prefixed-artifact-count-lower-bound"] = 19
    else:
        cast(
            "list[dict[str, object]]",
            aggregate_manifest["unexpected-contract-artifacts"],
        ).append(
            {
                "physical-artifact-name": "three-ci-validation-" + "9" * 64,
                "artifact-instance-id": "9001",
                "classification": "unexpected",
                "diagnostics": [],
            }
        )
        _sort_unexpected_artifacts(aggregate_manifest)
        cast(
            "dict[str, object]",
            aggregate_manifest["namespace-overflow"],
        )["observed-prefixed-artifact-count-lower-bound"] = 7
    _refresh_summary_manifest_digest(summary, aggregate_manifest)

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_aggregate_summary_accepts_failed_namespace_closure_failure() -> None:
    """Fail-closed namespace summaries carry the derived failed verdict."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    cast(
        "list[dict[str, object]]",
        aggregate_manifest["unexpected-contract-artifacts"],
    ).append(
        {
            "physical-artifact-name": "three-ci-validation-" + "9" * 64,
            "artifact-instance-id": "9001",
            "classification": "unexpected",
            "diagnostics": [],
        }
    )
    _sort_unexpected_artifacts(aggregate_manifest)
    cast(
        "dict[str, object]",
        aggregate_manifest["namespace-overflow"],
    )["observed-prefixed-artifact-count-lower-bound"] = 7
    _refresh_summary_manifest_digest(summary, aggregate_manifest)
    _mark_summary_namespace_failure(summary)

    validate_ci_validation_aggregate_summary(
        summary,
        plan=plan,
        aggregate_evidence_manifest=aggregate_manifest,
        admitted_batch_evidence_bundles=[bundle],
        execution_batch_manifest=manifest,
        request=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )


def test_aggregate_summary_uses_fixed_overflow_cap_for_fail_closed() -> None:
    """Summary fail-closed derivation ignores higher manifest-provided caps."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    overflow = cast(
        "dict[str, object]", aggregate_manifest["namespace-overflow"]
    )
    overflow["observed-prefixed-artifact-count-lower-bound"] = 19
    overflow["max-prefixed-validation-artifacts"] = 21
    _refresh_summary_manifest_digest(summary, aggregate_manifest)

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


@pytest.mark.parametrize("mode", ["null", "deleted"])
def test_aggregate_manifest_rejects_missing_valid_admitted_candidate_id(
    mode: str,
) -> None:
    """Valid bundle slots must identify the admitted candidate."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    slot = cast("list[dict[str, object]]", aggregate_manifest["batch-bundles"])[
        0
    ]
    if mode == "null":
        slot["admitted-candidate-id"] = None
    else:
        del slot["admitted-candidate-id"]

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
        )


def test_aggregate_summary_rejects_failed_evidence_passed_no_manifest() -> None:
    """Summary self-consistency does not depend on the aggregate manifest."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    cast("list[dict[str, object]]", summary["evidence-results"])[0][
        "outcome"
    ] = "failed"

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(summary, plan=plan)


def test_aggregate_summary_validates_manifest_batch_coverage() -> None:
    """Summary validation binds aggregate evidence to the execution manifest."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    cast("list[dict[str, object]]", aggregate_manifest["batch-bundles"]).clear()
    digest = ci_validation_aggregate_evidence_manifest_payload_digest(
        aggregate_manifest
    )
    cast("dict[str, object]", summary["aggregate-evidence-manifest"])[
        "content-digest"
    ] = digest
    cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )["content-digest"] = digest

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("batch-id", None),
        ("batch-id", "other-batch"),
        ("bundle-id", None),
        ("bundle-id", "bundle-forged"),
    ],
)
def test_aggregate_summary_rejects_satisfied_evidence_bad_bundle_reference(
    field: str,
    value: object,
) -> None:
    """Satisfied evidence rows must point at the admitted batch and bundle."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    result = cast("list[dict[str, object]]", summary["evidence-results"])[0]
    result[field] = value

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


@pytest.mark.parametrize(
    ("input_name", "admissibility"),
    [
        ("request", "missing"),
        ("request", "inadmissible"),
        ("validation-plan", "missing"),
        ("validation-plan", "inadmissible"),
        ("execution-batch-manifest", "missing"),
        ("execution-batch-manifest", "inadmissible"),
    ],
)
def test_aggregate_summary_fail_closes_required_input_failures(
    input_name: str,
    admissibility: str,
) -> None:
    """Required input failures make an otherwise passed summary invalid."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    if admissibility == "missing":
        _set_input_absent(
            aggregate_manifest,
            input_name,
            required=True,
            admissibility="missing",
        )
    else:
        cast(
            "dict[str, object]",
            cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
                input_name
            ],
        )["admissibility"] = "inadmissible"
    if input_name in {"request", "validation-plan"}:
        aggregate_manifest["projection-authority"] = None
    if input_name in {
        "request",
        "validation-plan",
        "execution-batch-manifest",
    }:
        _make_summary_batch_evidence_missing(summary, aggregate_manifest)
    else:
        _refresh_summary_manifest_digest(summary, aggregate_manifest)

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    _mark_summary_required_input_failure(summary)
    if input_name == "request":
        summary["request"] = {"artifact-ref": None, "request-digest": None}
    if input_name in {"request", "validation-plan"}:
        _set_summary_unknown_projection(summary)
    if input_name in {
        "request",
        "validation-plan",
        "execution-batch-manifest",
    }:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )
    else:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


@pytest.mark.parametrize(
    "input_name", ["validation-plan", "execution-batch-manifest"]
)
def test_aggregate_manifest_rejects_context_required_input_downgrade(
    input_name: str,
) -> None:
    """Authoritative context keeps plan and manifest inputs required."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    _set_input_absent(
        aggregate_manifest,
        input_name,
        required=False,
        admissibility="not-required",
    )

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
        )


def test_aggregate_summary_allows_missing_result_without_admitted_bundle() -> (
    None
):
    """Admitted bundle validation does not reject unsupported missing rows."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    slot = cast("list[dict[str, object]]", aggregate_manifest["batch-bundles"])[
        0
    ]
    slot["slot-admissibility"] = "missing"
    slot["admitted-candidate-id"] = None
    slot["observed-candidates"] = []
    _refresh_summary_manifest_digest(summary, aggregate_manifest)
    summary_bundle = cast("list[dict[str, object]]", summary["batch-bundles"])[
        0
    ]
    summary_bundle["bundle-id"] = None
    summary_bundle["admitted-candidate-id"] = None
    summary_bundle["candidate-count"] = 0
    summary_bundle["admissibility"] = "missing"
    _mark_summary_missing_evidence(summary)

    validate_ci_validation_aggregate_summary(
        summary,
        plan=plan,
        aggregate_evidence_manifest=aggregate_manifest,
        execution_batch_manifest=manifest,
        admitted_batch_evidence_bundles=[],
        request=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )


@pytest.mark.parametrize(
    ("field", "forged_value"),
    [
        ("batch-id", _BATCH_ID),
        ("bundle-id", "bundle-forged"),
        ("selector-index", 0),
    ],
)
def test_aggregate_summary_rejects_missing_evidence_provenance(
    field: str, forged_value: object
) -> None:
    """Missing evidence rows cannot claim batch provenance."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _make_summary_batch_evidence_missing(summary, aggregate_manifest)
    evidence_result = cast(
        "list[dict[str, object]]", summary["evidence-results"]
    )[0]
    evidence_result[field] = forged_value

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            execution_batch_manifest=manifest,
            admitted_batch_evidence_bundles=[],
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


@pytest.mark.parametrize(
    ("field", "forged_value"),
    [("batch-id", _BATCH_ID), ("bundle-id", "bundle-forged")],
)
def test_aggregate_summary_rejects_missing_failure_provenance(
    field: str, forged_value: str
) -> None:
    """Missing evidence failures cannot claim batch provenance."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _make_summary_batch_evidence_missing(summary, aggregate_manifest)
    failure = next(
        item
        for item in cast("list[dict[str, object]]", summary["failures"])
        if item["kind"] == "required-evidence-missing"
    )
    failure[field] = forged_value

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            execution_batch_manifest=manifest,
            admitted_batch_evidence_bundles=[],
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


@pytest.mark.parametrize("field", ["artifact-ref", "request-digest"])
def test_aggregate_summary_binds_request_to_manifest_without_plan_context(
    field: str,
) -> None:
    """Summary request identity is bound to the aggregate manifest input."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    request = cast("dict[str, object]", summary["request"])
    if field == "artifact-ref":
        request["artifact-ref"] = ci_validation_request_artifact_ref(
            run_id="999999", run_attempt=RUN_ATTEMPT
        )
    else:
        request["request-digest"] = "9" * 64

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


@pytest.mark.parametrize("admissibility", ["missing", "inadmissible"])
def test_aggregate_summary_requires_empty_request_for_nonvalid_manifest_request(
    admissibility: str,
) -> None:
    """Non-valid manifest request provenance cannot be self-supplied."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _set_input_absent(
        aggregate_manifest,
        "request",
        required=True,
        admissibility=admissibility,
    )
    _refresh_summary_manifest_digest(summary, aggregate_manifest)

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_aggregate_summary_rejects_planless_projection_authority() -> None:
    """Manifest projection authority cannot drive summary without plan."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    expected_message = (
        "supplied plan is required to authorize planless projection authority"
    )
    assert any(
        issue.path == "$.aggregate-evidence-manifest.projection-authority"
        and expected_message in issue.message
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    ("authority_path", "mutator"),
    [
        (
            "$.projection-authority.request.request-digest",
            lambda authority: cast(
                "dict[str, object]", authority["request"]
            ).__setitem__(
                "request-digest",
                "9" * 64,
            ),
        ),
        (
            "$.projection-authority.affected-range.changed-files-hash",
            lambda authority: cast(
                "dict[str, object]",
                authority["affected-range"],
            ).__setitem__("changed-files-hash", "8" * 64),
        ),
    ],
)
def test_aggregate_manifest_rejects_unbound_projection_authority(
    authority_path: str,
    mutator: Callable[[dict[str, object]], None],
) -> None:
    """Standalone manifest authority must bind to input artifact digests."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    authority = cast(
        "dict[str, object]",
        aggregate_manifest["projection-authority"],
    )
    mutator(authority)
    authority["projection-digest"] = canonical_json_digest(
        {
            key: value
            for key, value in authority.items()
            if key != "projection-digest"
        }
    )

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            execution_batch_manifest=manifest,
            request=_request_document(),
        )

    assert any(issue.path == authority_path for issue in exc_info.value.issues)


def test_aggregate_manifest_rejects_stale_authority_request_ref() -> None:
    """Manifest authority must bind request digest and artifact ref."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    _set_input_absent(
        aggregate_manifest,
        "fact-snapshot",
        required=True,
        admissibility="missing",
    )

    authority = cast(
        "dict[str, object]",
        aggregate_manifest["projection-authority"],
    )
    cast("dict[str, object]", authority["request"])["artifact-ref"] = (
        ci_validation_request_artifact_ref(
            run_id=RUN_ID,
            run_attempt="2",
        )
    )
    authority["projection-digest"] = canonical_json_digest(
        {
            key: value
            for key, value in authority.items()
            if key != "projection-digest"
        }
    )

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path == "$.projection-authority.request.artifact-ref"
        for issue in exc_info.value.issues
    )


def test_aggregate_manifest_rejects_request_context_projection_conflict() -> (
    None
):
    """Supplied request semantics must match manifest projection authority."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    request = _request_document()
    cast("dict[str, object]", request["validation-tree"])["ref"] = (
        "refs/pull/43/merge"
    )
    request["request-digest"] = canonical_json_digest(
        ci_validation_request_projection(request),
    )
    request_digest = cast("str", request["request-digest"])
    authority = cast(
        "dict[str, object]",
        aggregate_manifest["projection-authority"],
    )
    cast("dict[str, object]", authority["request"])["request-digest"] = (
        request_digest
    )
    _refresh_projection_authority_digest(authority)
    cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            "request"
        ],
    )["content-digest"] = request_digest

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            execution_batch_manifest=manifest,
            request=request,
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path == "$.projection-authority.validation-tree"
        and "supplied request projection" in issue.message
        for issue in exc_info.value.issues
    )


def test_plan_projection_rejects_request_context_conflict() -> None:
    """Supplied plan authority must also match supplied request semantics."""
    plan = _plan()
    request = _request_document()
    cast("dict[str, object]", request["validation-tree"])["ref"] = (
        "refs/pull/43/merge"
    )
    request["request-digest"] = canonical_json_digest(
        ci_validation_request_projection(request),
    )
    cast("dict[str, object]", plan["request"])["request-digest"] = request[
        "request-digest"
    ]
    plan["plan-digest"] = ci_validation_plan_digest(plan)
    original_plan = _plan()
    original_manifest = _manifest(original_plan)
    bundle = _bundle(original_plan, original_manifest)
    manifest = freeze_ci_validation_execution_batch_manifest(
        plan=plan,
        request=request,
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
        expected_run_id=RUN_ID,
        expected_run_attempt=RUN_ATTEMPT,
        batches=[_batch(plan)],
        budget=_budget(1),
        created_at=CREATED_AT,
    )
    aggregate_manifest = _aggregate_evidence_manifest(
        original_plan, original_manifest, bundle
    )
    aggregate_manifest["plan-digest"] = plan["plan-digest"]
    inputs = cast("dict[str, object]", aggregate_manifest["input-artifacts"])
    cast("dict[str, object]", inputs["validation-plan"])["content-digest"] = (
        plan["plan-digest"]
    )
    cast("dict[str, object]", inputs["request"])["content-digest"] = request[
        "request-digest"
    ]
    cast("dict[str, object]", inputs["execution-batch-manifest"])[
        "content-digest"
    ] = ci_validation_execution_batch_manifest_payload_digest(manifest)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
            request=request,
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path == "$.input-artifacts.validation-plan"
        and "authorize projection" in issue.message
        for issue in exc_info.value.issues
    )


def test_aggregate_manifest_rejects_matching_request_context_without_plan() -> (
    None
):
    """Canonical request context still needs supplied valid plan context."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    _set_input_absent(
        aggregate_manifest,
        "fact-snapshot",
        required=True,
        admissibility="missing",
    )

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
        )

    assert any(
        issue.path == "$.input-artifacts.validation-plan"
        and "supplied validated current-run plan context" in issue.message
        for issue in exc_info.value.issues
    )


def test_aggregate_manifest_rejects_malformed_request_context() -> None:
    """Malformed request context fails closed as contract validation."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    request = _request_document()
    del request["mode"]

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            execution_batch_manifest=manifest,
            request=request,
        )

    assert any(
        issue.path.startswith("request.") for issue in exc_info.value.issues
    )


def test_aggregate_summary_rejects_malformed_projection_authority() -> None:
    """Malformed manifest projection authority fails as validation issues."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    authority = cast(
        "dict[str, object]",
        aggregate_manifest["projection-authority"],
    )
    del authority["request"]
    _refresh_summary_manifest_digest(summary, aggregate_manifest)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path
        == "$.aggregate-evidence-manifest.projection-authority.request"
        and issue.message == "is required"
        for issue in exc_info.value.issues
    )


def test_execution_batch_manifest_rejects_malformed_plan_context() -> None:
    """Supplemental plan context fails closed without uncaught exceptions."""
    plan = _plan()
    manifest = _manifest(plan)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_execution_batch_manifest(
            manifest,
            plan=cast("Any", []),
        )

    assert any(issue.path == "plan" for issue in exc_info.value.issues)


def test_aggregate_summary_rejects_malformed_execution_manifest_context() -> (
    None
):
    """Supplemental execution manifest context is type-guarded."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=cast("Any", "not-an-object"),
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path == "execution_batch_manifest"
        for issue in exc_info.value.issues
    )


def test_aggregate_summary_rejects_malformed_admitted_bundle_context() -> None:
    """Supplemental admitted bundle context is type-guarded."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=cast(
                "Any",
                [bundle, "not-a-bundle"],
            ),
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path == "admitted_batch_evidence_bundles[1]"
        for issue in exc_info.value.issues
    )


def test_aggregate_summary_prefixes_invalid_admitted_bundle_diagnostics() -> (
    None
):
    """Nested admitted bundle validation issues identify the argument slot."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    invalid_bundle = cast("dict[str, object]", deepcopy(bundle))
    execution_tree = cast(
        "dict[str, object]",
        invalid_bundle["execution-tree"],
    )
    execution_tree["verified"] = False
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[invalid_bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    issue_paths = {issue.path for issue in exc_info.value.issues}
    assert (
        "admitted_batch_evidence_bundles[0].execution-tree.verified"
        in issue_paths
    )
    assert "$.execution-tree.verified" not in issue_paths


def test_aggregate_summary_prefixes_admitted_bundle_writer_diagnostics() -> (
    None
):
    """Aggregate validation reports forged admitted writer fields by slot."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    invalid_bundle = cast("dict[str, object]", deepcopy(bundle))
    writer = cast("dict[str, object]", invalid_bundle["writer"])
    writer["observed-job"] = "forged-writer-job"
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[invalid_bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    issue_paths = {issue.path for issue in exc_info.value.issues}
    assert (
        "admitted_batch_evidence_bundles[0].writer.observed-job" in issue_paths
    )
    assert "$.writer.observed-job" not in issue_paths


def test_aggregate_summary_rejects_forged_standalone_validation_tree() -> None:
    """Manifest projection authority rejects forged standalone projection."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    summary["validation-tree"] = {
        "commit-sha": "9" * 40,
        "ref": "refs/heads/main",
    }

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_aggregate_summary_rejects_missing_projection_authority() -> None:
    """Summary projection cannot self-authorize without manifest authority."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    aggregate_manifest["projection-authority"] = None
    _refresh_summary_manifest_digest(summary, aggregate_manifest)

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_missing_manifest_summary_rejects_supplied_plan_projection() -> None:
    """Missing-manifest summaries cannot project from an unproven plan."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_missing_aggregate_manifest(summary)
    summary["mode"] = plan["mode"]
    summary["validation-tree"] = plan["validation-tree"]
    summary["affected-range"] = _summary_affected_range(plan)
    summary["request"] = plan["request"]
    summary["scheduled-full"] = plan["scheduled-full"]

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path == "$.projection-authority"
        or issue.message == "must match no-authority fail-closed projection"
        for issue in exc_info.value.issues
    )


def test_missing_manifest_summary_rejects_arbitrary_projection() -> None:
    """No plan/manifest authority admits only the no-authority projection."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_missing_aggregate_manifest(summary)
    summary["mode"] = "push"
    summary["validation-tree"] = {
        "commit-sha": "9" * 40,
        "ref": "refs/heads/forged",
    }

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.message == "must match no-authority fail-closed projection"
        for issue in exc_info.value.issues
    )


def test_missing_manifest_summary_accepts_no_authority_projection() -> None:
    """Missing-manifest summaries use unknown projection."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_missing_aggregate_manifest(summary)

    validate_ci_validation_aggregate_summary(
        summary,
        plan=plan,
        execution_batch_manifest=manifest,
        _require_aggregate_evidence_manifest=False,
        request=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )
    assert summary["mode"] == "unknown"
    assert summary["validation-tree"] == {"commit-sha": None, "ref": None}
    assert summary["affected-range"] == {
        "status": "unknown",
        "base-sha": None,
        "base-tip-sha": None,
        "head-sha": None,
        "changed-files-hash": None,
    }
    assert summary["request"] == {"artifact-ref": None, "request-digest": None}
    assert summary["scheduled-full"] == {"enabled": False}


def test_missing_manifest_summary_rejects_malformed_supplied_plan() -> None:
    """Malformed supplied plans cannot fall through to no-authority."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_missing_aggregate_manifest(summary)
    malformed_plan = deepcopy(plan)
    del malformed_plan["validation-tree"]
    malformed_plan["plan-digest"] = ci_validation_plan_digest(malformed_plan)
    summary["plan-id"] = malformed_plan["plan-id"]
    summary["plan-digest"] = malformed_plan["plan-digest"]

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=malformed_plan,
            _require_aggregate_evidence_manifest=False,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path == "$.validation-tree" for issue in exc_info.value.issues
    )


def test_summary_rejects_manifest_projection_authority_without_plan() -> None:
    """Manifest authority is not sufficient without supplied plan."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_freezer_validates_malformed_projection_authority() -> None:
    """Freezing reports malformed manifest authority as contract validation."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    del cast(
        "dict[str, object]",
        aggregate_manifest["projection-authority"],
    )["request"]

    with pytest.raises(ContractValidationError) as exc_info:
        _aggregate_summary(plan, aggregate_manifest, bundle)

    assert any(
        issue.path.endswith("projection-authority.request")
        and issue.message == "is required"
        for issue in exc_info.value.issues
    )


def test_freezer_reports_malformed_plan_projection_as_validation_error() -> (
    None
):
    """Malformed self-consistent plans do not raise uncaught KeyError."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    malformed_plan = deepcopy(plan)
    del malformed_plan["validation-tree"]
    malformed_plan["plan-digest"] = ci_validation_plan_digest(malformed_plan)

    with pytest.raises(ContractValidationError) as exc_info:
        freeze_ci_validation_aggregate_evidence_manifest(
            created_at=CREATED_AT,
            repository_owner="hcoona",
            repository_name="three",
            workflow="CI Validation",
            run_id=RUN_ID,
            run_attempt=RUN_ATTEMPT,
            input_artifacts=cast(
                "dict[str, object]",
                aggregate_manifest["input-artifacts"],
            ),
            batch_bundles=cast(
                "list[dict[str, object]]",
                aggregate_manifest["batch-bundles"],
            ),
            unexpected_contract_artifacts=[],
            namespace_overflow=cast(
                "dict[str, object]",
                aggregate_manifest["namespace-overflow"],
            ),
            pre_final_validation_artifacts=6,
            namespace_closed_at=CREATED_AT,
            plan=malformed_plan,
            execution_batch_manifest=manifest,
            request=_request_document(),
        )

    assert any(
        issue.path == "plan.validation-tree" for issue in exc_info.value.issues
    )


def test_aggregate_summary_rejects_tampered_projection_authority_payload() -> (
    None
):
    """Summary projection binds to the manifest authority payload."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    authority = cast(
        "dict[str, object]",
        aggregate_manifest["projection-authority"],
    )
    authority["mode"] = "push"
    authority["projection-digest"] = canonical_json_digest(
        {
            key: value
            for key, value in authority.items()
            if key != "projection-digest"
        }
    )
    _refresh_summary_manifest_digest(summary, aggregate_manifest)

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


@pytest.mark.parametrize(
    ("field", "forged_value"),
    [
        ("mode", "scheduled_full"),
        ("validation-tree", {"commit-sha": "9" * 40, "ref": "refs/heads/main"}),
        ("scheduled-full", {"enabled": True}),
    ],
)
def test_aggregate_summary_rejects_planless_forged_projection_authority_fields(
    field: str,
    forged_value: object,
) -> None:
    """Planless summary cannot self-authorize forged manifest projection."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    authority = cast(
        "dict[str, object]",
        aggregate_manifest["projection-authority"],
    )
    authority[field] = forged_value
    _refresh_projection_authority_digest(authority)
    summary[field] = forged_value
    _refresh_summary_manifest_digest(summary, aggregate_manifest)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path == f"$.{field}"
        and issue.message == "must match no-authority fail-closed projection"
        for issue in exc_info.value.issues
    )


def test_aggregate_summary_rejects_projection_authority_digest_mismatch() -> (
    None
):
    """Summary validation rejects projection authority digest tamper."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    authority = cast(
        "dict[str, object]",
        aggregate_manifest["projection-authority"],
    )
    authority["projection-digest"] = "0" * 64
    _refresh_summary_manifest_digest(summary, aggregate_manifest)

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


@pytest.mark.parametrize(
    ("field", "forged_value"),
    [
        ("mode", "push"),
        ("validation-tree", {"commit-sha": "9" * 40, "ref": "refs/heads/main"}),
        (
            "affected-range",
            {
                "status": "not-applicable",
                "base-sha": None,
                "base-tip-sha": None,
                "head-sha": None,
                "changed-files-hash": None,
            },
        ),
        ("request", {"artifact-ref": None, "request-digest": "9" * 64}),
        ("scheduled-full", {"enabled": True}),
    ],
)
def test_aggregate_summary_rejects_forged_projection_without_admitted_bundle(
    field: str,
    forged_value: object,
) -> None:
    """Plan-derived projection binds with all bundle evidence missing."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    slot = cast("list[dict[str, object]]", aggregate_manifest["batch-bundles"])[
        0
    ]
    slot["slot-admissibility"] = "missing"
    slot["admitted-candidate-id"] = None
    slot["observed-candidates"] = []
    _refresh_summary_manifest_digest(summary, aggregate_manifest)
    summary_bundle = cast("list[dict[str, object]]", summary["batch-bundles"])[
        0
    ]
    summary_bundle["bundle-id"] = None
    summary_bundle["admitted-candidate-id"] = None
    summary_bundle["candidate-count"] = 0
    summary_bundle["admissibility"] = "missing"
    _mark_summary_missing_evidence(summary)
    summary[field] = forged_value

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            execution_batch_manifest=manifest,
            admitted_batch_evidence_bundles=[],
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_aggregate_summary_rejects_missing_bundles_forged_projection() -> None:
    """Manifest authority binds projection when every bundle is missing."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _make_summary_batch_evidence_missing(summary, aggregate_manifest)
    summary["validation-tree"] = {
        "commit-sha": "9" * 40,
        "ref": "refs/heads/main",
    }

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            aggregate_evidence_manifest=aggregate_manifest,
            execution_batch_manifest=manifest,
            admitted_batch_evidence_bundles=[],
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_aggregate_summary_rejects_forged_fact_snapshot_input_digest() -> None:
    """Summary-side revalidation keeps plan-frozen fact snapshot binding."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    fact_input = cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            "fact-snapshot"
        ],
    )
    fact_input["content-digest"] = "0" * 64
    _refresh_summary_manifest_digest(summary, aggregate_manifest)

    with pytest.raises(ContractValidationError) as direct_exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )
    assert any(
        issue.path == "$.input-artifacts.fact-snapshot.content-digest"
        for issue in direct_exc_info.value.issues
    )

    with pytest.raises(ContractValidationError) as summary_exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
        )
    assert any(
        issue.path == "$.input-artifacts.fact-snapshot.content-digest"
        for issue in summary_exc_info.value.issues
    )


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("content-digest", "0" * 64),
        (
            "artifact-ref",
            "ci-validation/execution-batches/999/1/execution-batch-manifest.json",
        ),
    ],
)
def test_aggregate_summary_rejects_admitted_bundle_manifest_mismatch(
    field: str,
    value: str,
) -> None:
    """Admitted bundles remain bound to the real execution manifest."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    good_aggregate_manifest = _aggregate_evidence_manifest(
        plan, manifest, bundle
    )
    summary = _aggregate_summary(plan, good_aggregate_manifest, bundle)
    bad_bundle = cast("dict[str, object]", deepcopy(bundle))
    cast("dict[str, object]", bad_bundle["execution-batch-manifest"])[field] = (
        value
    )
    aggregate_manifest = _aggregate_evidence_manifest(
        plan, manifest, bad_bundle
    )
    _refresh_summary_manifest_digest(summary, aggregate_manifest)

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            execution_batch_manifest=manifest,
            admitted_batch_evidence_bundles=[bad_bundle],
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_batch_bundle_rejects_execution_manifest_plan_mismatch() -> None:
    """Nested execution manifests are validated against the same plan."""
    plan = _plan()
    other_plan = dict(plan)
    other_plan["plan-id"] = "other-plan"
    other_plan["plan-digest"] = ci_validation_plan_digest(other_plan)
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)

    with pytest.raises(ContractValidationError):
        validate_ci_validation_batch_evidence_bundle(
            bundle,
            plan=other_plan,
            execution_batch_manifest=manifest,
        )


def test_aggregate_summary_rejects_aggregate_manifest_plan_mismatch() -> None:
    """Summary plan binding covers nested manifest digest claims."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    other_plan = dict(plan)
    other_plan["plan-id"] = "other-plan"
    other_plan["plan-digest"] = ci_validation_plan_digest(other_plan)
    other_manifest = deepcopy(manifest)
    other_manifest["plan-id"] = other_plan["plan-id"]
    other_manifest["plan-digest"] = other_plan["plan-digest"]
    other_bundle = cast("dict[str, object]", deepcopy(bundle))
    other_bundle["plan-id"] = other_plan["plan-id"]
    other_bundle["plan-digest"] = other_plan["plan-digest"]
    other_aggregate_manifest = _aggregate_evidence_manifest(
        other_plan,
        other_manifest,
        other_bundle,
    )
    other_digest = ci_validation_aggregate_evidence_manifest_payload_digest(
        other_aggregate_manifest,
    )
    cast("dict[str, object]", summary["aggregate-evidence-manifest"])[
        "content-digest"
    ] = other_digest
    cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )["content-digest"] = other_digest

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=other_aggregate_manifest,
            execution_batch_manifest=other_manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


@pytest.mark.parametrize("field", ["plan-id", "plan-digest"])
def test_batch_bundle_rejects_plan_identity_mismatch_without_plan(
    field: str,
) -> None:
    """Bundle plan identity binds to the execution manifest without plan."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    bundle[field] = "other-plan" if field == "plan-id" else "0" * 64

    with pytest.raises(ContractValidationError):
        validate_ci_validation_batch_evidence_bundle(
            bundle,
            execution_batch_manifest=manifest,
        )


@pytest.mark.parametrize("field", ["plan-id", "plan-digest"])
def test_aggregate_manifest_rejects_plan_identity_mismatch_without_plan(
    field: str,
) -> None:
    """Aggregate evidence plan identity binds to the execution manifest."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    aggregate_manifest[field] = "other-plan" if field == "plan-id" else "0" * 64

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            execution_batch_manifest=manifest,
        )


@pytest.mark.parametrize("field", ["plan-id", "plan-digest"])
def test_aggregate_summary_rejects_plan_identity_mismatch_without_plan(
    field: str,
) -> None:
    """Aggregate summary plan identity binds across provided artifacts."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    summary[field] = "other-plan" if field == "plan-id" else "0" * 64

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("mode", "push"),
        ("validation-tree", {"commit-sha": "9" * 40, "ref": "refs/heads/main"}),
        (
            "affected-range",
            {
                "status": "not-applicable",
                "base-sha": None,
                "base-tip-sha": None,
                "head-sha": None,
                "changed-files-hash": None,
            },
        ),
        ("scheduled-full", {"enabled": True}),
    ],
)
def test_aggregate_summary_rejects_bundle_projection_mismatch_without_plan(
    field: str,
    value: object,
) -> None:
    """Summary plan-derived projections bind to admitted bundles."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    summary[field] = value

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_contract_payload_digests_match_canonical_bytes() -> None:
    """Payload digest helpers use canonical contract bytes."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)

    assert ci_validation_execution_batch_manifest_payload_digest(manifest) == (
        hashlib_digest(manifest)
    )
    assert ci_validation_batch_evidence_bundle_payload_digest(bundle) == (
        hashlib_digest(bundle)
    )
    assert ci_validation_aggregate_evidence_manifest_payload_digest(
        aggregate_manifest,
    ) == hashlib_digest(aggregate_manifest)


def hashlib_digest(value: object) -> str:
    """Compute a direct SHA-256 digest over canonical JSON bytes."""
    return hashlib.sha256(canonical_json_bytes(value)).hexdigest()


def test_execution_batch_manifest_rejects_unknown_expected_evidence_id() -> (
    None
):
    """Authoritative plans require selector evidence ids to resolve."""
    plan = _plan()
    batch = _batch(plan)
    selector = cast("list[dict[str, Any]]", batch["ordered-selectors"])[0]
    selector["expected-evidence-id"] = "evidence-unknown"

    with pytest.raises(ContractValidationError):
        freeze_ci_validation_execution_batch_manifest(
            plan=plan,
            batches=[batch],
            budget=_budget(1),
            created_at=CREATED_AT,
        )


def test_execution_batch_manifest_rejects_cross_group_evidence() -> None:
    """Selector evidence ids must bind to the selected work group."""
    plan = _plan()
    other = dict(
        cast("list[dict[str, object]]", plan["evidence-expectations"])[0]
    )
    other["evidence-expectation-id"] = "evidence-other-gate"
    other["work-group-id"] = "wg-other-gate"
    cast("list[dict[str, object]]", plan["evidence-expectations"]).append(other)
    plan["plan-digest"] = ci_validation_plan_digest(plan)
    batch = _batch(plan)
    selector = cast("list[dict[str, Any]]", batch["ordered-selectors"])[0]
    selector["expected-evidence-id"] = "evidence-other-gate"

    with pytest.raises(ContractValidationError):
        freeze_ci_validation_execution_batch_manifest(
            plan=plan,
            batches=[batch],
            budget=_budget(1),
            created_at=CREATED_AT,
        )


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("expected-job-identity", "github-actions-job:" + "9" * 64),
        ("identity-source", "forged-source"),
        ("expected-boundary", "forged-boundary"),
        ("observed-workflow", "Forged Workflow"),
        ("observed-job", "forged-job"),
        ("observed-matrix", {"batch-id": "batch-forged"}),
    ],
)
def test_batch_evidence_bundle_rejects_writer_mismatch(
    field: str,
    value: object,
) -> None:
    """Bundle writer observations must match the selected manifest context."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    cast("dict[str, object]", bundle["writer"])[field] = value

    with pytest.raises(ContractValidationError):
        validate_ci_validation_batch_evidence_bundle(
            bundle,
            plan=plan,
            execution_batch_manifest=manifest,
        )


@pytest.mark.parametrize("field", ["plan-id", "plan-digest"])
def test_standalone_aggregate_manifest_rejects_null_valid_plan_identity(
    field: str,
) -> None:
    """Valid validation-plan inputs require standalone plan identity."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    aggregate_manifest[field] = None

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_evidence_manifest(aggregate_manifest)


def test_standalone_aggregate_manifest_rejects_plan_digest_input_mismatch() -> (
    None
):
    """Standalone manifests bind plan digest to validation-plan input."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            "validation-plan"
        ],
    )["content-digest"] = "0" * 64

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_evidence_manifest(aggregate_manifest)


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("pre-final-validation-artifacts", 7),
        ("expected-actual-validation-artifacts", 9),
    ],
)
def test_aggregate_summary_rejects_manifest_budget_mismatch(
    field: str,
    value: object,
) -> None:
    """Summary artifact budgets must bind to the aggregate manifest count."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    cast("dict[str, object]", summary["budgets"])[field] = value

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def _mark_summary_missing_aggregate_manifest(
    summary: dict[str, object],
) -> None:
    summary["verdict"] = "failed"
    _set_summary_unknown_projection(summary)
    manifest_claim = cast(
        "dict[str, object]",
        summary["aggregate-evidence-manifest"],
    )
    manifest_claim["artifact-instance-id"] = None
    manifest_claim["content-digest"] = None
    final_manifest = cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )
    final_manifest["artifact-instance-id"] = None
    final_manifest["content-digest"] = None
    final_manifest["producer-verified"] = False
    for row in cast("list[dict[str, object]]", summary["batch-bundles"]):
        row["bundle-id"] = None
        row["admitted-candidate-id"] = None
        row["candidate-count"] = 0
        row["admissibility"] = "missing"
    evidence_result = cast(
        "list[dict[str, object]]", summary["evidence-results"]
    )[0]
    if evidence_result.get("outcome") == "satisfied":
        _mark_summary_missing_evidence(summary)
    reason = cast("dict[str, object]", summary["reason"])
    reason["fail-closed"] = True
    reason["final-evidence-failure"] = True
    _append_summary_fail_closed_failure(
        summary,
        _diagnostic(
            "fail-closed/aggregate-summary-without-manifest",
            code="final-evidence-failure",
            detail="aggregate-summary-without-manifest",
            severity="fail-closed",
            verdict_effect="fail-closed",
        ),
        "Missing aggregate evidence manifest forced fail-closed.",
    )
    cast("list[dict[str, object]]", summary["failures"]).append(
        {
            "kind": "final-evidence-failure",
            "batch-id": None,
            "work-group-id": None,
            "evidence-expectation-id": None,
            "bundle-id": None,
            "diagnostic": _diagnostic(
                "final-evidence-failure",
                detail="aggregate-summary-without-manifest",
                severity="fail-closed",
                verdict_effect="fail-closed",
            ),
            "message": "Aggregate summary was validated without a manifest.",
        }
    )
    _sort_summary_failures(summary)


def test_aggregate_summary_accepts_missing_manifest_failure_detail() -> None:
    """Summary-only validation accepts the missing-manifest cause."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_missing_aggregate_manifest(summary)

    validate_ci_validation_aggregate_summary(
        summary,
        plan=plan,
        execution_batch_manifest=manifest,
        request=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )


def test_aggregate_summary_rejects_passed_empty_summary_without_manifest() -> (
    None
):
    """Absent manifests must be explicit fail-closed evidence."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _set_summary_unknown_projection(summary)
    summary["batch-bundles"] = []
    summary["evidence-results"] = []
    summary["failures"] = []
    summary["work-groups"] = {
        "executable-required": 0,
        "required-succeeded": 0,
        "required-failed": 0,
        "required-skipped": 0,
        "required-missing": 0,
        "terminal-aggregation": "present",
    }

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            _require_aggregate_evidence_manifest=False,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path in {"$.verdict", "$.reason.final-evidence-failure"}
        for issue in exc_info.value.issues
    )


def test_aggregate_summary_requires_fail_closed_failure_row() -> None:
    """Fail-closed reasons require an explicit fail-closed failure row."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_missing_aggregate_manifest(summary)
    _remove_summary_failure_kind(summary, "fail-closed")

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path == "$.failures" and "fail-closed" in issue.message
        for issue in exc_info.value.issues
    )


def test_aggregate_summary_fully_validates_supplied_execution_manifest() -> (
    None
):
    """Summary-only validation rejects forged execution manifest topology."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_missing_aggregate_manifest(summary)
    _make_summary_batch_evidence_missing(summary, aggregate_manifest)
    forged_manifest = cast("dict[str, object]", deepcopy(manifest))
    cast("list[dict[str, object]]", forged_manifest["batches"])[0][
        "expected-batch-evidence-bundle-ref"
    ] = ci_validation_batch_evidence_bundle_artifact_ref(
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        batch_id="batch-forged-gate",
    )

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            execution_batch_manifest=forged_manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )
    assert any(
        issue.path.endswith("expected-batch-evidence-bundle-ref")
        for issue in error.value.issues
    )


@pytest.mark.parametrize(
    ("summary_claim_key", "final_key", "final_value"),
    [
        ("artifact-instance-id", None, None),
        ("content-digest", None, None),
        (None, "artifact-instance-id", "3001"),
        (None, "content-digest", "0" * 64),
        (None, "producer-verified", True),
    ],
)
def test_aggregate_summary_rejects_missing_manifest_authoritative_claims(
    summary_claim_key: str | None,
    final_key: str | None,
    final_value: object,
) -> None:
    """Summary-only validation cannot invent manifest identity."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_missing_aggregate_manifest(summary)
    if summary_claim_key is not None:
        cast("dict[str, object]", summary["aggregate-evidence-manifest"])[
            summary_claim_key
        ] = "0" * 64 if summary_claim_key == "content-digest" else "3001"
    if final_key is not None:
        final_manifest = cast(
            "dict[str, object]",
            cast("dict[str, object]", summary["final-artifacts"])[
                "aggregate-evidence-manifest"
            ],
        )
        final_manifest[final_key] = final_value

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_aggregate_summary_rejects_missing_manifest_candidate_claim() -> None:
    """Summary rows cannot claim admitted candidates without manifest proof."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_missing_aggregate_manifest(summary)
    cast("list[dict[str, object]]", summary["batch-bundles"])[0][
        "admitted-candidate-id"
    ] = cast(
        "list[dict[str, object]]",
        aggregate_manifest["batch-bundles"],
    )[0]["admitted-candidate-id"]

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path == "$.batch-bundles[0].admitted-candidate-id"
        for issue in exc_info.value.issues
    )


def test_aggregate_summary_rejects_missing_manifest_satisfied_row() -> None:
    """Missing-manifest summaries cannot report satisfied evidence."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_missing_aggregate_manifest(summary)
    evidence_result = cast(
        "list[dict[str, object]]", summary["evidence-results"]
    )[0]
    evidence_result["batch-id"] = _BATCH_ID
    evidence_result["bundle-id"] = bundle["bundle-id"]
    evidence_result["selector-index"] = 0
    evidence_result["outcome"] = "satisfied"

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path == "$.evidence-results[0].outcome"
        and issue.message == "requires aggregate evidence manifest"
        for issue in exc_info.value.issues
    )


def test_aggregate_summary_rejects_missing_manifest_fake_batch_id() -> None:
    """Summary-only rows must match supplied execution-manifest batches."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_missing_aggregate_manifest(summary)
    row = cast("list[dict[str, object]]", summary["batch-bundles"])[0]
    row["batch-id"] = "batch-forged-gate"
    row["artifact-ref"] = ci_validation_batch_evidence_bundle_artifact_ref(
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        batch_id="batch-forged-gate",
    )

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path == "$.batch-bundles"
        and issue.message
        == "must match execution-batch manifest batch ids exactly"
        for issue in exc_info.value.issues
    )


def test_aggregate_summary_rejects_missing_manifest_stale_bundle_ref() -> None:
    """Summary bundle refs remain bound without manifest proof."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_missing_aggregate_manifest(summary)
    cast("list[dict[str, object]]", summary["batch-bundles"])[0][
        "artifact-ref"
    ] = ci_validation_batch_evidence_bundle_artifact_ref(
        run_id="999999",
        run_attempt=RUN_ATTEMPT,
        batch_id=_BATCH_ID,
    )

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path == "$.batch-bundles[0].artifact-ref"
        and issue.message == "must match run and batch"
        for issue in exc_info.value.issues
    )


def test_aggregate_summary_rejects_duplicate_missing_manifest_batch_id() -> (
    None
):
    """Summary-only validation rejects duplicate batch identities."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_missing_aggregate_manifest(summary)
    rows = cast("list[dict[str, object]]", summary["batch-bundles"])
    duplicate = cast("dict[str, object]", deepcopy(rows[0]))
    duplicate["diagnostics"] = [
        _diagnostic(
            "duplicate-batch-row",
            code="inadmissible-batch-evidence",
            detail="missing-bundle",
        )
    ]
    rows[:] = sorted(
        [rows[0], duplicate],
        key=lambda item: (
            str(item.get("batch-id") or ""),
            str(item.get("artifact-ref") or ""),
            str(item.get("bundle-id") or ""),
            str(item.get("admitted-candidate-id") or ""),
            canonical_json_digest(item),
        ),
    )

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path == "$.batch-bundles.batch-id"
        and issue.message == "must be unique"
        for issue in exc_info.value.issues
    )


def test_aggregate_summary_rejects_missing_manifest_satisfied_bypass() -> None:
    """The internal manifest flag cannot admit satisfied evidence publicly."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    cast("list[dict[str, object]]", summary["batch-bundles"])[0][
        "admitted-candidate-id"
    ] = None

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            _require_aggregate_evidence_manifest=False,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path == "$.evidence-results[0].outcome"
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    ("outcome", "marker"),
    [
        ("missing", _mark_summary_missing_evidence),
        ("skipped", _mark_summary_skipped_evidence),
        ("failed", _mark_summary_failed_evidence),
    ],
)
def test_aggregate_summary_rejects_plan_projection_without_manifest_bypass(
    outcome: str,
    marker: Callable[[dict[str, object]], None],
) -> None:
    """The internal manifest flag cannot let a plan authorize projection."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    marker(summary)
    cast("list[dict[str, object]]", summary["batch-bundles"])[0][
        "admitted-candidate-id"
    ] = None

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            execution_batch_manifest=manifest,
            _require_aggregate_evidence_manifest=False,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert (
        outcome
        == cast("list[dict[str, object]]", summary["evidence-results"])[0][
            "outcome"
        ]
    )
    assert any(
        issue.path == "$.projection-authority"
        or issue.message == "must match no-authority fail-closed projection"
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    "detail",
    ["aggregate-duration-exceeded", "required-input-artifact-failure"],
)
def test_aggregate_summary_rejects_mismatched_missing_manifest_detail(
    detail: str,
) -> None:
    """Missing-manifest summary failures cannot claim another cause."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_missing_aggregate_manifest(summary)
    failure = cast("list[dict[str, object]]", summary["failures"])[-1]
    cast("dict[str, object]", failure["diagnostic"])["detail"] = detail

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_aggregate_summary_rejects_stale_missing_manifest_detail() -> None:
    """Summary final evidence failures reject the stale aggregate detail."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_missing_aggregate_manifest(summary)
    failure = cast("list[dict[str, object]]", summary["failures"])[-1]
    cast("dict[str, object]", failure["diagnostic"])["detail"] = (
        "aggregate-without-manifest"
    )

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )


def test_aggregate_summary_rejects_mismatched_duration_failure_detail() -> None:
    """Duration overflow final evidence failures must cite duration overflow."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    cast("dict[str, object]", summary["budgets"])[
        "aggregate-duration-seconds"
    ] = 121
    reason = cast("dict[str, object]", summary["reason"])
    reason["aggregate-duration-exceeded"] = True
    reason["final-evidence-failure"] = True
    summary["verdict"] = "failed"
    cast("list[dict[str, object]]", summary["failures"]).extend(
        [
            {
                "kind": "aggregate-duration-exceeded",
                "batch-id": None,
                "work-group-id": None,
                "evidence-expectation-id": None,
                "bundle-id": None,
                "diagnostic": _diagnostic("aggregate-duration-exceeded"),
                "message": "Aggregate duration exceeded the maximum budget.",
            },
            {
                "kind": "final-evidence-failure",
                "batch-id": None,
                "work-group-id": None,
                "evidence-expectation-id": None,
                "bundle-id": None,
                "diagnostic": _diagnostic(
                    "final-evidence-failure",
                    detail="required-input-artifact-failure",
                    severity="fail-closed",
                    verdict_effect="fail-closed",
                ),
                "message": "Aggregate duration exceeded the maximum budget.",
            },
        ]
    )

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )
