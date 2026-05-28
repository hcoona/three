# ruff: noqa: D103
"""Shared CI validation batch fixtures for workflow-release tests."""

from __future__ import annotations

from copy import deepcopy
from typing import Any, TypedDict, cast

from three_workflow_release_contracts import (
    API_VERSIONS_BY_KIND,
    CiValidationKind,
    canonical_json_digest,
    ci_validation_plan_digest,
    ci_validation_request_artifact_ref,
    ci_validation_request_projection,
    freeze_ci_validation_plan,
    materialize_ci_validation_execution_batches,
    normalize_ci_validation_request,
)
from three_workflow_release_contracts import (
    freeze_ci_validation_execution_batch_manifest as _freeze_execution_manifest,
)

CREATED_AT = "2026-05-14T21:09:21Z"
RUN_ATTEMPT = "1"
RUN_ID = "25887422010"
TREE_SHA = "b" * 40
PLAN_ID = "plan-25887422010-1"


class _AuthorizingContextKwargs(TypedDict):
    request: dict[str, object]
    changed_files_snapshot: dict[str, object]
    fact_snapshot: dict[str, object]
    expected_run_id: str
    expected_run_attempt: str


def _request() -> dict[str, object]:
    document: dict[str, object] = {
        "api-version": API_VERSIONS_BY_KIND[CiValidationKind.REQUEST.value],
        "kind": CiValidationKind.REQUEST.value,
        "created-at": CREATED_AT,
        "repository": {"owner": "hcoona", "name": "three"},
        "run": {
            "workflow": "CI Validation",
            "run-id": RUN_ID,
            "run-attempt": RUN_ATTEMPT,
        },
        "schema-diagnostics": [],
        "artifact-ref": ci_validation_request_artifact_ref(
            run_id=RUN_ID,
            run_attempt=RUN_ATTEMPT,
        ),
        "request-digest": "0" * 64,
        "mode": "pull_request",
        "validation-tree": {
            "commit-sha": TREE_SHA,
            "ref": "refs/pull/42/merge",
        },
        "event": {
            "name": "pull_request",
            "number": "42",
            "actor": "octocat",
            "run-id": RUN_ID,
            "run-attempt": RUN_ATTEMPT,
        },
        "affected-range": {
            "status": "available",
            "base-sha": "a" * 40,
            "base-tip-sha": "c" * 40,
            "head-sha": TREE_SHA,
            "changed-files": [
                "src/public/lib/example.py",
                "tests/example_test.py",
            ],
            "source": "pull_request",
            "diagnostic": None,
            "diagnostic-detail": None,
        },
    }
    document["request-digest"] = canonical_json_digest(
        ci_validation_request_projection(document),
    )
    return document


def _normalized_request():
    result = normalize_ci_validation_request(
        _request(),
        expected_run_id=RUN_ID,
        expected_run_attempt=RUN_ATTEMPT,
    )
    assert result.request is not None
    return result.request


def _classification() -> dict[str, object]:
    return {
        "impacts": [
            {
                "impact-id": "impact-example",
                "category": "project-scoped",
                "matched-paths": [
                    "src/public/lib/example.py",
                    "tests/example_test.py",
                ],
                "source-rule": "python-workspace-path",
                "rationale": "Changed files belong to the example subject.",
                "coverage-target": {
                    "type": "subject",
                    "id": "python.src-public-lib-example",
                },
                "requires": {
                    "descriptor-validation": False,
                    "downstream-expansion": False,
                    "broad-expansion": False,
                    "diagnostic": None,
                },
            },
        ],
        "subject-selection-provenance": [
            {
                "provenance-id": "prov-example",
                "subject-id": "python.src-public-lib-example",
                "selection-kind": "direct",
                "source-impact-ids": ["impact-example"],
                "direct-subject-id": None,
                "dependency-edge-basis": [],
                "broad-expansion-id": None,
                "scheduled-full-source": False,
            },
        ],
        "lightweight-only": False,
    }


def _fail_closed_classification() -> dict[str, object]:
    classification = deepcopy(_classification())
    impact = cast(
        "dict[str, object]",
        cast("list[dict[str, object]]", classification["impacts"])[0],
    )
    impact["category"] = "unknown"
    impact["source-rule"] = "python-workspace-path-fail-closed"
    impact["rationale"] = (
        "Changed path requires fail-closed planning because supporting facts "
        "were incomplete."
    )
    impact["coverage-target"] = {"type": "none", "id": None}
    cast("dict[str, object]", impact["requires"])["diagnostic"] = (
        "unknown-change"
    )
    classification["subject-selection-provenance"] = []
    return classification


def _subject() -> dict[str, object]:
    return {
        "subject-id": "python.src-public-lib-example",
        "ecosystem": "python",
        "root": "src/public/lib/example",
        "activity-status": "active",
        "selection-status": "selected",
        "capability-class": "validation-only",
        "descriptor": {"path": None, "identity": None},
        "capabilities": {
            "build": True,
            "test": True,
            "lint": False,
            "format": False,
            "type-check": True,
            "release-shaped-artifacts": False,
        },
        "inclusion": {"source": "workspace", "reason": "uv workspace"},
        "exclusion": {"reason": None},
    }


def _fact_provider() -> dict[str, object]:
    return {
        "provider": "python",
        "provider-version": "uv-workspace/v1",
        "status": "available",
        "roots": ["src/public/lib/example"],
        "subjects": ["python.src-public-lib-example"],
        "dependency-edges": [],
        "tooling-surfaces": [],
        "descriptors": [],
        "target-catalog": {
            "catalog-id": None,
            "descriptor-paths": [],
            "entries": [],
        },
        "diagnostics": [],
    }


def _ecosystem_gate_work_group() -> dict[str, object]:
    return {
        "work-group-id": "wg-python-gate",
        "kind": "ecosystem-gate",
        "coverage-target": {
            "type": "subject",
            "id": "python.src-public-lib-example",
        },
        "ecosystem": "python",
        "runner-family": "ubuntu",
        "selector-variant": None,
        "depends-on": [],
        "expected-evidence": {
            "category": "ecosystem-gate",
            "planned-capabilities": ["build", "test", "type-check"],
            "detail-profile": None,
            "required": True,
        },
    }


def _validation_obligation() -> dict[str, object]:
    return {
        "validation-obligation-id": "validation-python-gate",
        "source-impact-ids": ["impact-example"],
        "kind": "ecosystem-gate",
        "coverage-target": {
            "type": "subject",
            "id": "python.src-public-lib-example",
        },
        "required": True,
        "blocking": True,
        "work-group-id": "wg-python-gate",
        "expected-evidence-id": "evidence-python-gate",
    }


def _evidence_expectation() -> dict[str, object]:
    return {
        "evidence-expectation-id": "evidence-python-gate",
        "work-group-id": "wg-python-gate",
        "coverage-target": {
            "type": "subject",
            "id": "python.src-public-lib-example",
        },
        "category": "ecosystem-gate",
        "planned-capabilities": ["build", "test", "type-check"],
        "detail-profile": None,
        "required": True,
        "blocking-if-missing": True,
    }


def plan() -> dict[str, object]:
    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_classification(),
        subjects=[_subject()],
        validation_obligations=[_validation_obligation()],
        work_groups=[_ecosystem_gate_work_group()],
        evidence_expectations=[_evidence_expectation()],
        fact_snapshot_providers=[_fact_provider()],
    )
    return cast("dict[str, object]", snapshot.plan)


def fail_closed_plan() -> dict[str, object]:
    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="fail-closed",
        classification=_fail_closed_classification(),
        diagnostics=[
            {
                "diagnostic-id": "fail-closed/unknown-change",
                "code": "unknown-change",
                "detail": "incomplete",
                "message": "Changed files could not be classified.",
                "source": {"type": "aggregation", "id": None},
                "severity": "fail-closed",
                "verdict-effect": "fail-closed",
            }
        ],
        fact_snapshot_providers=None,
    )
    return cast("dict[str, object]", snapshot.plan)


def request_document() -> dict[str, object]:
    return _request()


def changed_files_snapshot_document() -> dict[str, object]:
    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_classification(),
        subjects=[_subject()],
        validation_obligations=[_validation_obligation()],
        work_groups=[_ecosystem_gate_work_group()],
        evidence_expectations=[_evidence_expectation()],
        fact_snapshot_providers=[_fact_provider()],
    )
    assert snapshot.changed_files_snapshot is not None
    return cast("dict[str, object]", deepcopy(snapshot.changed_files_snapshot))


def fact_snapshot_document() -> dict[str, object]:
    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_classification(),
        subjects=[_subject()],
        validation_obligations=[_validation_obligation()],
        work_groups=[_ecosystem_gate_work_group()],
        evidence_expectations=[_evidence_expectation()],
        fact_snapshot_providers=[_fact_provider()],
    )
    assert snapshot.fact_snapshot is not None
    return cast("dict[str, object]", deepcopy(snapshot.fact_snapshot))


def authorizing_context_kwargs() -> _AuthorizingContextKwargs:
    return {
        "request": request_document(),
        "changed_files_snapshot": changed_files_snapshot_document(),
        "fact_snapshot": fact_snapshot_document(),
        "expected_run_id": RUN_ID,
        "expected_run_attempt": RUN_ATTEMPT,
    }


def manifest(plan: dict[str, object]) -> dict[str, object]:
    fact_snapshot = (
        None
        if cast("dict[str, object]", plan["fact-snapshot"])["status"]
        == "unavailable"
        else fact_snapshot_document()
    )
    materialization = materialize_ci_validation_execution_batches(
        plan=plan,
        request=request_document(),
        changed_files_snapshot=changed_files_snapshot_document(),
        fact_snapshot=fact_snapshot,
        expected_run_id=RUN_ID,
        expected_run_attempt=RUN_ATTEMPT,
        created_at=CREATED_AT,
        execution_workflow="CI Validation",
    )
    return cast("dict[str, object]", materialization.manifest)


def freeze_ci_validation_execution_batch_manifest(
    **kwargs: Any,
) -> dict[str, object]:
    """Freeze an execution-batch manifest using public contract APIs."""
    return _freeze_execution_manifest(**kwargs)


def budget(batch_count: int, *, input_count: int = 5) -> dict[str, object]:
    physical_job_count = int(batch_count > 0)
    return {
        "min-total-jobs": physical_job_count,
        "max-total-jobs": 18,
        "min-windows-jobs": 0,
        "max-windows-jobs": 8,
        "non-batch-control-plane-job-count": 0,
        "actual-total-jobs": physical_job_count,
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


def add_dependent_work_group(plan: dict[str, object]) -> None:
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


def add_transitive_work_group(plan: dict[str, object]) -> None:
    add_dependent_work_group(plan)
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


def dependent_batches(plan: dict[str, object]) -> list[dict[str, object]]:
    return _execution_batches(plan)


def _execution_batches(plan: dict[str, object]) -> list[dict[str, object]]:
    materialization = materialize_ci_validation_execution_batches(
        plan=plan,
        **authorizing_context_kwargs(),
        created_at=CREATED_AT,
        execution_workflow="CI Validation",
    )
    execution_manifest = cast("dict[str, object]", materialization.manifest)
    batches = cast(
        "list[dict[str, object]]",
        deepcopy(execution_manifest["batches"]),
    )
    batch_ids = {batch["batch-id"] for batch in batches}
    batches.sort(
        key=lambda batch: (
            len(
                [
                    batch_id
                    for batch_id in cast(
                        "list[object]", batch["depends-on-batches"]
                    )
                    if batch_id in batch_ids
                ]
            ),
            str(batch["batch-id"]),
        )
    )
    return batches


def zero_batch_execution_manifest(
    execution_manifest: dict[str, object],
) -> dict[str, object]:
    result = deepcopy(execution_manifest)
    result["batches"] = []
    manifest_budget = cast("dict[str, object]", execution_manifest["budget"])
    input_count = cast(
        "int",
        manifest_budget["expected-input-non-bundle-validation-artifacts"],
    )
    result["budget"] = budget(0, input_count=input_count)
    return result


def tooling_detail_profile(
    *,
    profile_id: str = "profile-tooling",
    category: str = "workflow-release-tooling",
    coverage_target: dict[str, object] | None = None,
) -> dict[str, object]:
    return {
        "detail-profile-id": profile_id,
        "category": category,
        "coverage-target": coverage_target
        or {"type": "tooling-surface", "id": "authoring-validation"},
        "required-subchecks": [
            {
                "subcheck-id": "contract",
                "check-kind": "contract",
                "blocking": True,
                "description": "Verify the workflow-release tooling contract.",
            },
        ],
    }
