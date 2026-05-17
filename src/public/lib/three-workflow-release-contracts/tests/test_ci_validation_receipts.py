"""Validation receipt contract helper tests."""

from __future__ import annotations

import json
from copy import deepcopy
from typing import cast

import pytest
from three_workflow_release_contracts import (
    API_VERSIONS_BY_KIND,
    CiValidationKind,
    CiValidationPlanSnapshot,
    ContractValidationError,
    DiagnosticDetail,
    DiagnosticFamily,
    DiagnosticSeverity,
    DiagnosticVerdictEffect,
    canonical_json_digest,
    ci_validation_diagnostic,
    ci_validation_receipt_content_digest,
    ci_validation_receipt_payload_digest,
    ci_validation_request_artifact_ref,
    ci_validation_request_projection,
    ci_validation_selector_assignments_artifact_ref,
    ci_validation_writer_id,
    freeze_ci_validation_plan,
    freeze_ci_validation_receipt,
    freeze_ci_validation_selector_assignments,
    load_ci_validation_receipt_payload,
    normalize_ci_validation_request,
    validate_ci_validation_receipt,
)

RUN_ID = "25887422010"
RUN_ATTEMPT = "1"
SHA256_HEX_LENGTH = 64
CREATED_AT = "2026-05-14T21:09:21Z"
PLAN_ID = "plan-25887422010-1"
TREE_SHA = "b" * 40
WORK_GROUP_ID = "wg-python-gate"
PREFLIGHT_WORK_GROUP_ID = "wg-preflight"
DESCRIPTOR_WORK_GROUP_ID = "wg-descriptor"
ARTIFACT_WORK_GROUP_ID = "wg-artifact"


def _work_group() -> dict[str, object]:
    return {
        "work-group-id": WORK_GROUP_ID,
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
        "work-group-id": WORK_GROUP_ID,
        "expected-evidence-id": "evidence-python-gate",
    }


def _evidence_expectation() -> dict[str, object]:
    return {
        "evidence-expectation-id": "evidence-python-gate",
        "work-group-id": WORK_GROUP_ID,
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


def _context() -> tuple[
    CiValidationPlanSnapshot, dict[str, object], dict[str, object]
]:
    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_classification(),
        subjects=[_subject()],
        validation_obligations=[_validation_obligation()],
        work_groups=[_work_group()],
        evidence_expectations=[_evidence_expectation()],
        fact_snapshot_providers=[_fact_provider()],
    )
    writer_id = ci_validation_writer_id(
        workflow="CI Validation",
        job="ci-validation-selector-python",
        matrix={"selector": WORK_GROUP_ID},
    )
    manifest = freeze_ci_validation_selector_assignments(
        plan=snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
        trusted_writer_ids={WORK_GROUP_ID: writer_id},
        created_at=CREATED_AT,
    )
    assignments = manifest["assignments"]
    assert isinstance(assignments, list)
    assignment = assignments[0]
    assert isinstance(assignment, dict)
    return snapshot, manifest, assignment


def _success_evidence() -> dict[str, object]:
    return {
        "category": "ecosystem-gate",
        "planned-capabilities": ["build", "test", "type-check"],
        "capability-results": [
            {"capability": "build", "outcome": "success", "diagnostics": []},
            {"capability": "test", "outcome": "success", "diagnostics": []},
            {
                "capability": "type-check",
                "outcome": "success",
                "diagnostics": [],
            },
        ],
        "artifact-refs": [],
    }


def _detail_profile_receipt() -> tuple[
    CiValidationPlanSnapshot,
    dict[str, object],
    dict[str, object],
    dict[str, object],
]:
    preflight_work_group_id = PREFLIGHT_WORK_GROUP_ID
    preflight_evidence_id = "evidence-preflight"
    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_classification(),
        subjects=[_subject()],
        validation_obligations=[
            {
                **_validation_obligation(),
                "validation-obligation-id": "validation-preflight",
                "kind": "lightweight-preflight",
                "work-group-id": preflight_work_group_id,
                "expected-evidence-id": preflight_evidence_id,
            },
            _validation_obligation(),
        ],
        work_groups=[
            {
                **_work_group(),
                "work-group-id": preflight_work_group_id,
                "kind": "lightweight-preflight",
                "ecosystem": None,
                "expected-evidence": {
                    "category": "lightweight-preflight",
                    "planned-capabilities": None,
                    "detail-profile": "preflight-profile",
                    "required": True,
                },
            },
            _work_group(),
        ],
        evidence_expectations=[
            {
                **_evidence_expectation(),
                "evidence-expectation-id": preflight_evidence_id,
                "work-group-id": preflight_work_group_id,
                "category": "lightweight-preflight",
                "planned-capabilities": None,
                "detail-profile": "preflight-profile",
            },
            _evidence_expectation(),
        ],
        detail_profiles=[
            {
                "detail-profile-id": "preflight-profile",
                "category": "lightweight-preflight",
                "coverage-target": {
                    "type": "subject",
                    "id": "python.src-public-lib-example",
                },
                "required-subchecks": [
                    {
                        "subcheck-id": "preflight-advisory",
                        "check-kind": "policy",
                        "blocking": False,
                        "description": "Run advisory preflight tooling.",
                    },
                    {
                        "subcheck-id": "preflight-subcheck",
                        "check-kind": "tool-discovery",
                        "blocking": True,
                        "description": "Run preflight tooling.",
                    },
                ],
            },
        ],
        fact_snapshot_providers=[_fact_provider()],
    )
    writer_id = ci_validation_writer_id(
        workflow="CI Validation",
        job="ci-validation-selector-preflight",
        matrix={"selector": preflight_work_group_id},
    )
    manifest = freeze_ci_validation_selector_assignments(
        plan=snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
        trusted_writer_ids={
            WORK_GROUP_ID: ci_validation_writer_id(
                workflow="CI Validation",
                job="ci-validation-selector-python",
                matrix={"selector": WORK_GROUP_ID},
            ),
            preflight_work_group_id: writer_id,
        },
        created_at=CREATED_AT,
    )
    assignments = cast("list[dict[str, object]]", manifest["assignments"])
    assignment = next(
        item
        for item in assignments
        if item["work-group-id"] == preflight_work_group_id
    )
    receipt = freeze_ci_validation_receipt(
        plan=snapshot.plan,
        selector_assignments_manifest=manifest,
        assignment=assignment,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
        receipt_id="receipt-001",
        created_at=CREATED_AT,
        execution_observed_commit_sha=TREE_SHA,
        outcome="success",
        evidence={
            "category": "lightweight-preflight",
            "planned-capabilities": None,
            "category-result": {
                "outcome": "success",
                "diagnostics": [],
                "detail": {
                    "work-group-id": preflight_work_group_id,
                    "detail-profile": "preflight-profile",
                    "coverage-target": {
                        "type": "subject",
                        "id": "python.src-public-lib-example",
                    },
                    "selector-variant": None,
                    "runner-family": "ubuntu",
                    "outcome": "success",
                    "subcheck-results": [
                        {
                            "subcheck-id": "preflight-subcheck",
                            "outcome": "success",
                            "diagnostics": [],
                        },
                        {
                            "subcheck-id": "preflight-advisory",
                            "outcome": "success",
                            "diagnostics": [],
                        },
                    ],
                    "diagnostics": [],
                },
            },
            "artifact-refs": [],
        },
    )
    return snapshot, manifest, assignment, receipt


def _skipped_diagnostic(
    source_id: str = WORK_GROUP_ID,
) -> dict[str, object]:
    return ci_validation_diagnostic(
        diagnostic_id="validation-work-skipped/dependency-blocked",
        code=DiagnosticFamily.VALIDATION_WORK_SKIPPED.value,
        detail=DiagnosticDetail.DEPENDENCY_BLOCKED.value,
        message="blocked by upstream dependency",
        source_type="work-group",
        source_id=source_id,
        severity=DiagnosticSeverity.BLOCKING_FAILURE.value,
        verdict_effect=DiagnosticVerdictEffect.NONE.value,
    )


def _failed_diagnostic(
    source_id: str = WORK_GROUP_ID,
) -> dict[str, object]:
    return ci_validation_diagnostic(
        diagnostic_id="validation-work-failed/tooling",
        code=DiagnosticFamily.VALIDATION_WORK_FAILED.value,
        detail=DiagnosticDetail.TOOLING.value,
        message="validation work failed",
        source_type="work-group",
        source_id=source_id,
        severity=DiagnosticSeverity.BLOCKING_FAILURE.value,
        verdict_effect=DiagnosticVerdictEffect.FAILED.value,
    )


def _artifact_shape_diagnostic(
    source_id: str = WORK_GROUP_ID,
) -> dict[str, object]:
    return ci_validation_diagnostic(
        diagnostic_id="artifact-shape-unconfirmed/digest",
        code=DiagnosticFamily.ARTIFACT_SHAPE_UNCONFIRMED.value,
        detail=None,
        message="artifact digest unavailable because shape is unconfirmed",
        source_type="work-group",
        source_id=source_id,
        severity=DiagnosticSeverity.BLOCKING_FAILURE.value,
        verdict_effect=DiagnosticVerdictEffect.FAILED.value,
    )


def _descriptor_invalid_diagnostic(
    source_id: str = WORK_GROUP_ID,
) -> dict[str, object]:
    return ci_validation_diagnostic(
        diagnostic_id="descriptor-invalid/schema",
        code=DiagnosticFamily.DESCRIPTOR_INVALID.value,
        detail=None,
        message="descriptor is invalid",
        source_type="work-group",
        source_id=source_id,
        severity=DiagnosticSeverity.BLOCKING_FAILURE.value,
        verdict_effect=DiagnosticVerdictEffect.FAILED.value,
    )


def _valid_receipt() -> tuple[
    dict[str, object],
    CiValidationPlanSnapshot,
    dict[str, object],
    dict[str, object],
]:
    snapshot, manifest, assignment = _context()
    receipt = freeze_ci_validation_receipt(
        plan=snapshot.plan,
        selector_assignments_manifest=manifest,
        assignment=assignment,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
        receipt_id="receipt-001",
        created_at=CREATED_AT,
        execution_observed_commit_sha=TREE_SHA,
        outcome="success",
        evidence=_success_evidence(),
    )
    return receipt, snapshot, manifest, assignment


def test_freeze_and_validate_capability_receipt() -> None:
    """Freeze a receipt bound to the plan and assignment."""
    receipt, snapshot, manifest, assignment = _valid_receipt()

    validate_ci_validation_receipt(
        receipt,
        plan=snapshot.plan,
        selector_assignments_manifest=manifest,
        assignment=assignment,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    assert receipt["artifact-ref"] == assignment["receipt-artifact-ref"]
    assert receipt["proof-admissibility"] == "validation-only"


def test_receipt_rejects_extra_self_attested_writer_identity() -> None:
    """Reject payload writer identity claims outside the schema."""
    receipt, snapshot, manifest, assignment = _valid_receipt()
    receipt["trusted-writer-id"] = assignment["trusted-writer-id"]

    with pytest.raises(ContractValidationError, match="not allowed"):
        validate_ci_validation_receipt(
            receipt,
            plan=snapshot.plan,
            selector_assignments_manifest=manifest,
            assignment=assignment,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_receipt_rejects_mismatched_execution_tree() -> None:
    """Require trusted execution tree evidence to match the plan."""
    receipt, snapshot, manifest, assignment = _valid_receipt()
    execution_tree = cast("dict[str, object]", receipt["execution-tree"])
    execution_tree["observed-commit-sha"] = "d" * 40

    with pytest.raises(ContractValidationError, match="validation-tree"):
        validate_ci_validation_receipt(
            receipt,
            plan=snapshot.plan,
            selector_assignments_manifest=manifest,
            assignment=assignment,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_receipt_rejects_capability_outcome_mismatch() -> None:
    """Derive top-level outcome from capability results."""
    receipt, snapshot, manifest, assignment = _valid_receipt()
    receipt["outcome"] = "success"
    evidence = cast("dict[str, object]", receipt["evidence"])
    results = cast("list[dict[str, object]]", evidence["capability-results"])
    results[1]["outcome"] = "blocking-failure"
    results[1]["diagnostics"] = [
        ci_validation_diagnostic(
            diagnostic_id="validation-work-failed/test",
            code=DiagnosticFamily.VALIDATION_WORK_FAILED.value,
            detail=DiagnosticDetail.TEST.value,
            message="tests failed",
            source_type="work-group",
            source_id=WORK_GROUP_ID,
            severity=DiagnosticSeverity.BLOCKING_FAILURE.value,
            verdict_effect=DiagnosticVerdictEffect.FAILED.value,
        ),
    ]

    with pytest.raises(ContractValidationError, match="capability results"):
        validate_ci_validation_receipt(
            receipt,
            plan=snapshot.plan,
            selector_assignments_manifest=manifest,
            assignment=assignment,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_receipt_rejects_top_level_non_success_without_diagnostics() -> None:
    """Require receipt-level diagnostics when the receipt is not successful."""
    receipt, snapshot, manifest, assignment = _valid_receipt()
    receipt["outcome"] = "skipped"

    with pytest.raises(ContractValidationError, match="non-success outcome"):
        validate_ci_validation_receipt(
            receipt,
            plan=snapshot.plan,
            selector_assignments_manifest=manifest,
            assignment=assignment,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_receipt_rejects_capability_non_success_without_diagnostics() -> None:
    """Require diagnostics on failed or skipped capability results."""
    receipt, snapshot, manifest, assignment = _valid_receipt()
    receipt["outcome"] = "blocking-failure"
    receipt["diagnostics"] = [_failed_diagnostic()]
    evidence = cast("dict[str, object]", receipt["evidence"])
    results = cast("list[dict[str, object]]", evidence["capability-results"])
    results[1]["outcome"] = "blocking-failure"

    with pytest.raises(ContractValidationError, match="non-success outcome"):
        validate_ci_validation_receipt(
            receipt,
            plan=snapshot.plan,
            selector_assignments_manifest=manifest,
            assignment=assignment,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_receipt_rejects_non_release_artifact_refs() -> None:
    """Forbid top-level artifact refs for non-artifact receipts."""
    receipt, snapshot, manifest, assignment = _valid_receipt()
    evidence = cast("dict[str, object]", receipt["evidence"])
    evidence["artifact-refs"] = [
        ci_validation_selector_assignments_artifact_ref(
            run_id=RUN_ID,
            run_attempt=RUN_ATTEMPT,
        ),
    ]

    with pytest.raises(ContractValidationError, match="non-artifact"):
        validate_ci_validation_receipt(
            receipt,
            plan=snapshot.plan,
            selector_assignments_manifest=manifest,
            assignment=assignment,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_receipt_rejects_assignment_outside_selector_manifest() -> None:
    """Bind receipts to exact selector-manifest assignments."""
    receipt, snapshot, manifest, assignment = _valid_receipt()
    forged_assignment = deepcopy(assignment)
    forged_assignment["trusted-writer-id"] = ci_validation_writer_id(
        workflow="CI Validation",
        job="forged",
        matrix={"selector": WORK_GROUP_ID},
    )

    with pytest.raises(ContractValidationError, match="exactly match"):
        validate_ci_validation_receipt(
            receipt,
            plan=snapshot.plan,
            selector_assignments_manifest=manifest,
            assignment=forged_assignment,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_receipt_payload_loader_rejects_non_ijson_numbers() -> None:
    """Convert non-I-JSON receipt content into validation errors."""
    with pytest.raises(ContractValidationError, match="floats"):
        load_ci_validation_receipt_payload(b'{"value":1.5}')


def test_receipt_payload_loader_rejects_duplicate_members() -> None:
    """Reject duplicate object member names before canonicalization."""
    with pytest.raises(ContractValidationError, match="duplicate"):
        load_ci_validation_receipt_payload(b'{"value":1,"value":2}')


def test_receipt_payload_loader_rejects_nested_duplicate_members() -> None:
    """Reject duplicate object member names in nested objects."""
    with pytest.raises(ContractValidationError, match="duplicate"):
        load_ci_validation_receipt_payload(b'{"outer":{"value":1,"value":2}}')


def test_receipt_digest_helpers_are_canonical_and_raw() -> None:
    """Distinguish observed raw content digest from canonical payload digest."""
    receipt, _, _, _ = _valid_receipt()
    raw = json.dumps(receipt, sort_keys=True).encode("utf-8")

    assert ci_validation_receipt_content_digest(raw) != (
        ci_validation_receipt_payload_digest(receipt)
    )
    assert len(ci_validation_receipt_content_digest(raw)) == SHA256_HEX_LENGTH


def _descriptor_path() -> str:
    return "src/public/lib/example/three-release.json"


def _descriptor_fact_provider() -> dict[str, object]:
    provider = _fact_provider()
    provider["descriptors"] = [
        {
            "descriptor-path": _descriptor_path(),
            "descriptor-identity": "example",
            "owner-subject-id": "python.src-public-lib-example",
            "source": "ecosystem-provider",
        },
    ]
    provider["target-catalog"] = {
        "catalog-id": "catalog-python-example",
        "descriptor-paths": [_descriptor_path()],
        "entries": [
            {
                "descriptor-path": _descriptor_path(),
                "profile": "wheel",
                "artifact": {
                    "kind-family": "python",
                    "concrete-kind": "wheel",
                    "logical-artifact-role": "package",
                    "variant-dimensions": {},
                    "expected-artifact-refs": [
                        "ci-validation/artifacts/python/example/wheel.whl",
                    ],
                },
                "release-receipt": {
                    "expected-family": "python",
                    "logical-receipt-role": "build",
                    "variant-dimensions": {},
                },
            },
        ],
    }
    return provider


def _descriptor_backed_subject() -> dict[str, object]:
    subject = _subject()
    subject["capability-class"] = "descriptor-backed"
    subject["descriptor"] = {
        "path": _descriptor_path(),
        "identity": "python.src-public-lib-example",
    }
    capabilities = cast("dict[str, bool]", subject["capabilities"])
    capabilities["release-shaped-artifacts"] = True
    return subject


def _descriptor_work_group() -> dict[str, object]:
    return {
        "work-group-id": DESCRIPTOR_WORK_GROUP_ID,
        "kind": "descriptor-validation",
        "coverage-target": {"type": "descriptor", "id": _descriptor_path()},
        "ecosystem": None,
        "runner-family": "ubuntu",
        "selector-variant": None,
        "depends-on": [],
        "expected-evidence": {
            "category": "descriptor-validation",
            "planned-capabilities": None,
            "detail-profile": None,
            "required": True,
        },
    }


def _descriptor_evidence_expectation() -> dict[str, object]:
    return {
        "evidence-expectation-id": "evidence-descriptor",
        "work-group-id": DESCRIPTOR_WORK_GROUP_ID,
        "coverage-target": {"type": "descriptor", "id": _descriptor_path()},
        "category": "descriptor-validation",
        "planned-capabilities": None,
        "detail-profile": None,
        "required": True,
        "blocking-if-missing": True,
    }


def _descriptor_obligation() -> dict[str, object]:
    return {
        "descriptor-obligation-id": "descriptor-example",
        "source-impact-ids": ["impact-example"],
        "descriptor-scope": "selected",
        "coverage-target": {"type": "descriptor", "id": _descriptor_path()},
        "required": True,
        "blocking": True,
        "work-group-id": DESCRIPTOR_WORK_GROUP_ID,
        "expected-evidence-id": "evidence-descriptor",
    }


def _artifact_work_group() -> dict[str, object]:
    return {
        "work-group-id": ARTIFACT_WORK_GROUP_ID,
        "kind": "release-shaped-artifact",
        "coverage-target": {
            "type": "artifact-obligation",
            "id": "artifact-example",
        },
        "ecosystem": "python",
        "runner-family": "ubuntu",
        "selector-variant": None,
        "depends-on": [],
        "expected-evidence": {
            "category": "release-shaped-artifact",
            "planned-capabilities": None,
            "detail-profile": None,
            "required": True,
        },
    }


def _dependency_blocked_artifact_work_group() -> dict[str, object]:
    work_group = _artifact_work_group()
    work_group["depends-on"] = [WORK_GROUP_ID]
    return work_group


def _artifact_evidence_expectation() -> dict[str, object]:
    return {
        "evidence-expectation-id": "evidence-artifact",
        "work-group-id": ARTIFACT_WORK_GROUP_ID,
        "coverage-target": {
            "type": "artifact-obligation",
            "id": "artifact-example",
        },
        "category": "release-shaped-artifact",
        "planned-capabilities": None,
        "detail-profile": None,
        "required": True,
        "blocking-if-missing": True,
    }


def _artifact_validation_obligation() -> dict[str, object]:
    return {
        "validation-obligation-id": "validation-artifact",
        "source-impact-ids": ["impact-example"],
        "kind": "release-shaped-artifact",
        "coverage-target": {
            "type": "artifact-obligation",
            "id": "artifact-example",
        },
        "required": True,
        "blocking": True,
        "work-group-id": ARTIFACT_WORK_GROUP_ID,
        "expected-evidence-id": "evidence-artifact",
    }


def _artifact_obligation() -> dict[str, object]:
    return {
        "artifact-obligation-id": "artifact-example",
        "source-impact-ids": ["impact-example"],
        "subject-id": "python.src-public-lib-example",
        "descriptor-path": _descriptor_path(),
        "profile-coverage": ["wheel"],
        "artifact": {
            "kind-family": "python",
            "concrete-kind": "wheel",
            "logical-artifact-role": "package",
            "variant-dimensions": {},
            "expected-artifact-refs": [
                "ci-validation/artifacts/python/example/wheel.whl",
            ],
        },
        "release-receipt": {
            "expected-family": "python",
            "logical-receipt-role": "build",
            "variant-dimensions": {},
        },
        "credential-posture": "credential-free",
        "expected-evidence-category": "release-shaped-artifact",
        "required": True,
        "blocking": True,
        "validation-obligation-id": "validation-artifact",
        "work-group-id": ARTIFACT_WORK_GROUP_ID,
        "expected-evidence-id": "evidence-artifact",
    }


def _specialized_context(
    *,
    group: dict[str, object],
    evidence_expectation: dict[str, object],
    descriptor_obligations: list[dict[str, object]] | None = None,
    validation_obligations: list[dict[str, object]] | None = None,
    artifact_obligations: list[dict[str, object]] | None = None,
):
    del evidence_expectation
    del descriptor_obligations
    del validation_obligations
    del artifact_obligations
    default_work_groups = {
        ARTIFACT_WORK_GROUP_ID: _artifact_work_group(),
        DESCRIPTOR_WORK_GROUP_ID: _descriptor_work_group(),
        WORK_GROUP_ID: _work_group(),
    }
    default_work_groups[cast("str", group["work-group-id"])] = group
    work_groups = list(default_work_groups.values())
    evidence_expectations = [
        _artifact_evidence_expectation(),
        _descriptor_evidence_expectation(),
        _evidence_expectation(),
    ]
    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_classification(),
        subjects=[_descriptor_backed_subject()],
        descriptor_obligations=[_descriptor_obligation()],
        validation_obligations=[
            _artifact_validation_obligation(),
            _validation_obligation(),
        ],
        artifact_obligations=[_artifact_obligation()],
        work_groups=work_groups,
        evidence_expectations=evidence_expectations,
        fact_snapshot_providers=[_descriptor_fact_provider()],
    )
    wanted_work_group_id = cast("str", group["work-group-id"])
    trusted_writer_ids = {
        cast("str", item["work-group-id"]): ci_validation_writer_id(
            workflow="CI Validation",
            job=f"ci-validation-selector-{item['work-group-id']}",
            matrix={"selector": item["work-group-id"]},
        )
        for item in work_groups
    }
    manifest = freeze_ci_validation_selector_assignments(
        plan=snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
        trusted_writer_ids=trusted_writer_ids,
        created_at=CREATED_AT,
    )
    assignments = cast("list[dict[str, object]]", manifest["assignments"])
    assignment = next(
        item
        for item in assignments
        if item["work-group-id"] == wanted_work_group_id
    )
    return snapshot, manifest, assignment


def _descriptor_receipt_evidence() -> dict[str, object]:
    return {
        "category": "descriptor-validation",
        "planned-capabilities": None,
        "category-result": {
            "outcome": "success",
            "diagnostics": [],
            "detail": {
                "descriptor-obligation-results": [
                    {
                        "descriptor-obligation-id": "descriptor-example",
                        "descriptor": {
                            "path": _descriptor_path(),
                            "identity": "example",
                            "owner-subject-id": "python.src-public-lib-example",
                            "source": "ecosystem-provider",
                        },
                        "descriptor-scope": "selected",
                        "outcome": "success",
                        "diagnostics": [],
                    },
                ],
            },
        },
        "artifact-refs": [],
    }


def _artifact_ref() -> str:
    return "ci-validation/artifacts/python/example/wheel.whl"


def _release_receipt_evidence() -> dict[str, object]:
    return {
        "category": "release-shaped-artifact",
        "planned-capabilities": None,
        "category-result": {
            "outcome": "success",
            "diagnostics": [],
            "detail": {
                "artifact-obligation-results": [
                    {
                        "artifact-obligation-id": "artifact-example",
                        "descriptor": {
                            "path": _descriptor_path(),
                            "identity": "example",
                        },
                        "profile-coverage": ["wheel"],
                        "artifact": {
                            "planned": {
                                "kind-family": "python",
                                "concrete-kind": "wheel",
                                "logical-artifact-role": "package",
                                "variant-dimensions": {},
                                "expected-artifact-refs": [_artifact_ref()],
                            },
                            "observed": {
                                "refs": [_artifact_ref()],
                                "digests": [
                                    {
                                        "artifact-ref": _artifact_ref(),
                                        "algorithm": "sha256",
                                        "digest": "1" * SHA256_HEX_LENGTH,
                                        "digest-available": True,
                                        "diagnostics": [],
                                    },
                                ],
                            },
                            "outcome": "success",
                            "diagnostics": [],
                        },
                        "release-receipt": {
                            "planned": {
                                "expected-family": "python",
                                "logical-receipt-role": "build",
                                "variant-dimensions": {},
                            },
                            "expected": True,
                            "schema-checked": True,
                            "outcome": "success",
                            "diagnostics": [],
                        },
                        "outcome": "success",
                        "diagnostics": [],
                    },
                ],
            },
        },
        "artifact-refs": [_artifact_ref()],
    }


def _receipt_for_context(
    snapshot: CiValidationPlanSnapshot,
    manifest: dict[str, object],
    assignment: dict[str, object],
    evidence: dict[str, object],
) -> dict[str, object]:
    return freeze_ci_validation_receipt(
        plan=snapshot.plan,
        selector_assignments_manifest=manifest,
        assignment=assignment,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
        receipt_id="receipt-001",
        created_at=CREATED_AT,
        execution_observed_commit_sha=TREE_SHA,
        outcome="success",
        evidence=evidence,
    )


def _validate_specialized_receipt(
    receipt: dict[str, object],
    snapshot: CiValidationPlanSnapshot,
    manifest: dict[str, object],
    assignment: dict[str, object],
) -> None:
    validate_ci_validation_receipt(
        receipt,
        plan=snapshot.plan,
        selector_assignments_manifest=manifest,
        assignment=assignment,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )


def test_descriptor_receipt_binds_descriptor_fields_to_facts() -> None:
    """Accept descriptor evidence only when copied from the fact snapshot."""
    snapshot, manifest, assignment = _specialized_context(
        group=_descriptor_work_group(),
        evidence_expectation=_descriptor_evidence_expectation(),
        descriptor_obligations=[_descriptor_obligation()],
    )
    receipt = _receipt_for_context(
        snapshot, manifest, assignment, _descriptor_receipt_evidence()
    )

    _validate_specialized_receipt(receipt, snapshot, manifest, assignment)


def test_descriptor_receipt_rejects_fact_field_mismatch() -> None:
    """Reject descriptor receipts with fields not copied from frozen facts."""
    snapshot, manifest, assignment = _specialized_context(
        group=_descriptor_work_group(),
        evidence_expectation=_descriptor_evidence_expectation(),
        descriptor_obligations=[_descriptor_obligation()],
    )
    receipt = _receipt_for_context(
        snapshot, manifest, assignment, _descriptor_receipt_evidence()
    )
    evidence = cast("dict[str, object]", receipt["evidence"])
    category = cast("dict[str, object]", evidence["category-result"])
    detail = cast("dict[str, object]", category["detail"])
    results = cast(
        "list[dict[str, object]]", detail["descriptor-obligation-results"]
    )
    descriptor = cast("dict[str, object]", results[0]["descriptor"])
    descriptor["identity"] = "forged"

    with pytest.raises(ContractValidationError, match="must match plan"):
        _validate_specialized_receipt(receipt, snapshot, manifest, assignment)


def test_release_receipt_binds_artifact_obligation_fields() -> None:
    """Accept release-shaped evidence matching frozen artifact obligation."""
    snapshot, manifest, assignment = _specialized_context(
        group=_artifact_work_group(),
        evidence_expectation=_artifact_evidence_expectation(),
        validation_obligations=[_artifact_validation_obligation()],
        artifact_obligations=[_artifact_obligation()],
    )
    receipt = _receipt_for_context(
        snapshot, manifest, assignment, _release_receipt_evidence()
    )

    _validate_specialized_receipt(receipt, snapshot, manifest, assignment)


def test_release_receipt_rejects_planned_artifact_mismatch() -> None:
    """Reject release-shaped planned fields not copied from the obligation."""
    snapshot, manifest, assignment = _specialized_context(
        group=_artifact_work_group(),
        evidence_expectation=_artifact_evidence_expectation(),
        validation_obligations=[_artifact_validation_obligation()],
        artifact_obligations=[_artifact_obligation()],
    )
    receipt = _receipt_for_context(
        snapshot, manifest, assignment, _release_receipt_evidence()
    )
    result = _release_result(receipt)
    artifact = cast("dict[str, object]", result["artifact"])
    planned = cast("dict[str, object]", artifact["planned"])
    planned["concrete-kind"] = "sdist"

    with pytest.raises(ContractValidationError, match="must match plan"):
        _validate_specialized_receipt(receipt, snapshot, manifest, assignment)


def test_release_receipt_rejects_success_observed_ref_mismatch() -> None:
    """Reject successful release-shaped observed ref mismatches."""
    snapshot, manifest, assignment = _specialized_context(
        group=_artifact_work_group(),
        evidence_expectation=_artifact_evidence_expectation(),
        validation_obligations=[_artifact_validation_obligation()],
        artifact_obligations=[_artifact_obligation()],
    )
    receipt = _receipt_for_context(
        snapshot, manifest, assignment, _release_receipt_evidence()
    )
    result = _release_result(receipt)
    artifact = cast("dict[str, object]", result["artifact"])
    observed = cast("dict[str, object]", artifact["observed"])
    other_ref = "ci-validation/artifacts/python/example/other.whl"
    observed["refs"] = [other_ref]
    digests = cast("list[dict[str, object]]", observed["digests"])
    digests[0]["artifact-ref"] = other_ref
    evidence = cast("dict[str, object]", receipt["evidence"])
    evidence["artifact-refs"] = observed["refs"]

    with pytest.raises(ContractValidationError, match="blocking-failure"):
        _validate_specialized_receipt(receipt, snapshot, manifest, assignment)


def test_release_receipt_allows_failed_observed_ref_mismatch() -> None:
    """Allow observed ref mismatches as blocking-failure evidence."""
    snapshot, manifest, assignment = _specialized_context(
        group=_artifact_work_group(),
        evidence_expectation=_artifact_evidence_expectation(),
        validation_obligations=[_artifact_validation_obligation()],
        artifact_obligations=[_artifact_obligation()],
    )
    receipt = _receipt_for_context(
        snapshot, manifest, assignment, _release_receipt_evidence()
    )
    result = _release_result(receipt)
    artifact = cast("dict[str, object]", result["artifact"])
    observed = cast("dict[str, object]", artifact["observed"])
    other_ref = "ci-validation/artifacts/python/example/other.whl"
    observed["refs"] = [other_ref]
    digests = cast("list[dict[str, object]]", observed["digests"])
    digests[0]["artifact-ref"] = other_ref
    artifact["outcome"] = "blocking-failure"
    artifact["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    result["outcome"] = "blocking-failure"
    result["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    evidence = cast("dict[str, object]", receipt["evidence"])
    category = cast("dict[str, object]", evidence["category-result"])
    category["outcome"] = "blocking-failure"
    category["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    evidence["artifact-refs"] = observed["refs"]
    receipt["outcome"] = "blocking-failure"
    receipt["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]

    _validate_specialized_receipt(receipt, snapshot, manifest, assignment)


def test_top_level_failed_diagnostic_requires_blocking_failure() -> None:
    """Top-level failed diagnostics affect the whole receipt outcome."""
    receipt, snapshot, manifest, assignment = _valid_receipt()
    receipt["diagnostics"] = [
        ci_validation_diagnostic(
            diagnostic_id="validation-work-failed/tooling",
            code=DiagnosticFamily.VALIDATION_WORK_FAILED.value,
            detail=DiagnosticDetail.TOOLING.value,
            message="validation wrapper failed",
            source_type="work-group",
            source_id=WORK_GROUP_ID,
            severity=DiagnosticSeverity.BLOCKING_FAILURE.value,
            verdict_effect=DiagnosticVerdictEffect.FAILED.value,
        ),
    ]

    with pytest.raises(ContractValidationError, match="blocking-failure"):
        validate_ci_validation_receipt(
            receipt,
            plan=snapshot.plan,
            selector_assignments_manifest=manifest,
            assignment=assignment,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def _release_result(receipt: dict[str, object]) -> dict[str, object]:
    evidence = cast("dict[str, object]", receipt["evidence"])
    category = cast("dict[str, object]", evidence["category-result"])
    detail = cast("dict[str, object]", category["detail"])
    results = cast(
        "list[dict[str, object]]", detail["artifact-obligation-results"]
    )
    return results[0]


def test_top_level_failed_diagnostic_allows_blocking_failure() -> None:
    """Allow top-level failed diagnostics to determine receipt outcome."""
    receipt, snapshot, manifest, assignment = _valid_receipt()
    receipt["outcome"] = "blocking-failure"
    receipt["diagnostics"] = [
        ci_validation_diagnostic(
            diagnostic_id="validation-work-failed/tooling",
            code=DiagnosticFamily.VALIDATION_WORK_FAILED.value,
            detail=DiagnosticDetail.TOOLING.value,
            message="validation wrapper failed",
            source_type="work-group",
            source_id=WORK_GROUP_ID,
            severity=DiagnosticSeverity.BLOCKING_FAILURE.value,
            verdict_effect=DiagnosticVerdictEffect.FAILED.value,
        ),
    ]

    validate_ci_validation_receipt(
        receipt,
        plan=snapshot.plan,
        selector_assignments_manifest=manifest,
        assignment=assignment,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )


def test_failed_diagnostic_code_requires_failed_verdict_effect() -> None:
    """Diagnostic code semantics must agree with verdict-effect."""
    receipt, snapshot, manifest, assignment = _valid_receipt()
    receipt["diagnostics"] = [_failed_diagnostic()]
    diagnostics = cast("list[dict[str, object]]", receipt["diagnostics"])
    diagnostics[0]["verdict-effect"] = DiagnosticVerdictEffect.NONE.value

    with pytest.raises(ContractValidationError, match="failed verdict-effect"):
        validate_ci_validation_receipt(
            receipt,
            plan=snapshot.plan,
            selector_assignments_manifest=manifest,
            assignment=assignment,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_artifact_shape_diagnostic_requires_failed_verdict_effect() -> None:
    """Unconfirmed artifact shape diagnostics are blocking failures."""
    receipt, snapshot, manifest, assignment = _valid_receipt()
    receipt["diagnostics"] = [_artifact_shape_diagnostic()]
    diagnostics = cast("list[dict[str, object]]", receipt["diagnostics"])
    diagnostics[0]["verdict-effect"] = DiagnosticVerdictEffect.NONE.value

    with pytest.raises(ContractValidationError, match="failed verdict-effect"):
        validate_ci_validation_receipt(
            receipt,
            plan=snapshot.plan,
            selector_assignments_manifest=manifest,
            assignment=assignment,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_known_non_impacting_is_not_receipt_diagnostic_code() -> None:
    """Planner-only known-non-impacting diagnostics are not receipt evidence."""
    receipt, snapshot, manifest, assignment = _valid_receipt()
    receipt["diagnostics"] = [
        ci_validation_diagnostic(
            diagnostic_id="known-non-impacting/planner",
            code=DiagnosticFamily.KNOWN_NON_IMPACTING.value,
            detail=None,
            message="planner classified this change as non-impacting",
            source_type="work-group",
            source_id=WORK_GROUP_ID,
            severity=DiagnosticSeverity.INFO.value,
            verdict_effect=DiagnosticVerdictEffect.NONE.value,
        ),
    ]

    with pytest.raises(ContractValidationError, match="valid for receipts"):
        validate_ci_validation_receipt(
            receipt,
            plan=snapshot.plan,
            selector_assignments_manifest=manifest,
            assignment=assignment,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_receipt_rejects_foreign_diagnostic_source_id() -> None:
    """Bind receipt diagnostics to the validated work group."""
    receipt, snapshot, manifest, assignment = _valid_receipt()
    receipt["outcome"] = "blocking-failure"
    receipt["diagnostics"] = [_failed_diagnostic("wg-foreign")]

    with pytest.raises(ContractValidationError, match="receipt work group"):
        validate_ci_validation_receipt(
            receipt,
            plan=snapshot.plan,
            selector_assignments_manifest=manifest,
            assignment=assignment,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_receipt_rejects_null_diagnostic_source_id() -> None:
    """Require diagnostics to name the validated work group explicitly."""
    receipt, snapshot, manifest, assignment = _valid_receipt()
    receipt["outcome"] = "blocking-failure"
    receipt["diagnostics"] = [_failed_diagnostic()]
    diagnostics = cast("list[dict[str, object]]", receipt["diagnostics"])
    source = cast("dict[str, object]", diagnostics[0]["source"])
    source["id"] = None

    with pytest.raises(ContractValidationError, match="receipt work group"):
        validate_ci_validation_receipt(
            receipt,
            plan=snapshot.plan,
            selector_assignments_manifest=manifest,
            assignment=assignment,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_descriptor_invalid_diagnostic_requires_failed_verdict_effect() -> None:
    """Invalid descriptor diagnostics are blocking failures."""
    receipt, snapshot, manifest, assignment = _valid_receipt()
    receipt["diagnostics"] = [_descriptor_invalid_diagnostic()]
    diagnostics = cast("list[dict[str, object]]", receipt["diagnostics"])
    diagnostics[0]["verdict-effect"] = DiagnosticVerdictEffect.NONE.value

    with pytest.raises(ContractValidationError, match="failed verdict-effect"):
        validate_ci_validation_receipt(
            receipt,
            plan=snapshot.plan,
            selector_assignments_manifest=manifest,
            assignment=assignment,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_descriptor_invalid_diagnostic_allows_blocking_failure() -> None:
    """Allow descriptor-invalid diagnostics in descriptor validation results."""
    snapshot, manifest, assignment = _specialized_context(
        group=_descriptor_work_group(),
        evidence_expectation=_descriptor_evidence_expectation(),
        descriptor_obligations=[_descriptor_obligation()],
    )
    receipt = _receipt_for_context(
        snapshot, manifest, assignment, _descriptor_receipt_evidence()
    )
    receipt["outcome"] = "blocking-failure"
    receipt["diagnostics"] = [_failed_diagnostic(DESCRIPTOR_WORK_GROUP_ID)]
    evidence = cast("dict[str, object]", receipt["evidence"])
    category = cast("dict[str, object]", evidence["category-result"])
    category["outcome"] = "blocking-failure"
    category["diagnostics"] = [_failed_diagnostic(DESCRIPTOR_WORK_GROUP_ID)]
    detail = cast("dict[str, object]", category["detail"])
    results = cast(
        "list[dict[str, object]]", detail["descriptor-obligation-results"]
    )
    results[0]["outcome"] = "blocking-failure"
    results[0]["diagnostics"] = [
        _descriptor_invalid_diagnostic(DESCRIPTOR_WORK_GROUP_ID)
    ]

    _validate_specialized_receipt(receipt, snapshot, manifest, assignment)


def test_descriptor_invalid_diagnostic_rejected_for_ecosystem_gate() -> None:
    """Reject descriptor-invalid diagnostics in unrelated receipt categories."""
    receipt, snapshot, manifest, assignment = _valid_receipt()
    receipt["outcome"] = "blocking-failure"
    receipt["diagnostics"] = [_descriptor_invalid_diagnostic()]

    with pytest.raises(
        ContractValidationError, match="receipt category or location"
    ):
        validate_ci_validation_receipt(
            receipt,
            plan=snapshot.plan,
            selector_assignments_manifest=manifest,
            assignment=assignment,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_artifact_shape_diagnostic_rejected_outside_release_context() -> None:
    """Reject artifact-shape-unconfirmed outside release artifact evidence."""
    receipt, snapshot, manifest, assignment = _valid_receipt()
    receipt["outcome"] = "blocking-failure"
    receipt["diagnostics"] = [_artifact_shape_diagnostic()]

    with pytest.raises(
        ContractValidationError, match="receipt category or location"
    ):
        validate_ci_validation_receipt(
            receipt,
            plan=snapshot.plan,
            selector_assignments_manifest=manifest,
            assignment=assignment,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_release_receipt_rejects_top_level_artifact_shape_diagnostic() -> None:
    """Only artifact/digest fields may carry artifact-shape diagnostics."""
    snapshot, manifest, assignment = _specialized_context(
        group=_artifact_work_group(),
        evidence_expectation=_artifact_evidence_expectation(),
        validation_obligations=[_artifact_validation_obligation()],
        artifact_obligations=[_artifact_obligation()],
    )
    receipt = _receipt_for_context(
        snapshot, manifest, assignment, _release_receipt_evidence()
    )
    receipt["outcome"] = "blocking-failure"
    receipt["diagnostics"] = [
        _artifact_shape_diagnostic(ARTIFACT_WORK_GROUP_ID)
    ]

    with pytest.raises(
        ContractValidationError, match="receipt category or location"
    ):
        _validate_specialized_receipt(receipt, snapshot, manifest, assignment)


def test_release_receipt_rejects_category_artifact_shape_diagnostic() -> None:
    """Reject artifact-shape diagnostics on release category results."""
    snapshot, manifest, assignment = _specialized_context(
        group=_artifact_work_group(),
        evidence_expectation=_artifact_evidence_expectation(),
        validation_obligations=[_artifact_validation_obligation()],
        artifact_obligations=[_artifact_obligation()],
    )
    receipt = _receipt_for_context(
        snapshot, manifest, assignment, _release_receipt_evidence()
    )
    receipt["outcome"] = "blocking-failure"
    receipt["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    evidence = cast("dict[str, object]", receipt["evidence"])
    category = cast("dict[str, object]", evidence["category-result"])
    category["outcome"] = "blocking-failure"
    category["diagnostics"] = [
        _artifact_shape_diagnostic(ARTIFACT_WORK_GROUP_ID)
    ]

    with pytest.raises(
        ContractValidationError, match="receipt category or location"
    ):
        _validate_specialized_receipt(receipt, snapshot, manifest, assignment)


def test_release_receipt_rejects_result_artifact_shape_diagnostic() -> None:
    """Reject artifact-shape diagnostics on release obligation results."""
    snapshot, manifest, assignment = _specialized_context(
        group=_artifact_work_group(),
        evidence_expectation=_artifact_evidence_expectation(),
        validation_obligations=[_artifact_validation_obligation()],
        artifact_obligations=[_artifact_obligation()],
    )
    receipt = _receipt_for_context(
        snapshot, manifest, assignment, _release_receipt_evidence()
    )
    receipt["outcome"] = "blocking-failure"
    receipt["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    evidence = cast("dict[str, object]", receipt["evidence"])
    category = cast("dict[str, object]", evidence["category-result"])
    category["outcome"] = "blocking-failure"
    category["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    result = _release_result(receipt)
    result["outcome"] = "blocking-failure"
    result["diagnostics"] = [_artifact_shape_diagnostic(ARTIFACT_WORK_GROUP_ID)]

    with pytest.raises(
        ContractValidationError, match="receipt category or location"
    ):
        _validate_specialized_receipt(receipt, snapshot, manifest, assignment)


def test_skipped_diagnostic_is_not_success_evidence() -> None:
    """Skipped diagnostics only support skipped outcomes."""
    receipt, snapshot, manifest, assignment = _valid_receipt()
    receipt["diagnostics"] = [_skipped_diagnostic()]

    with pytest.raises(ContractValidationError, match="must be skipped"):
        validate_ci_validation_receipt(
            receipt,
            plan=snapshot.plan,
            selector_assignments_manifest=manifest,
            assignment=assignment,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_category_failed_diagnostic_folds_into_detail_outcome() -> None:
    """Allow category diagnostics to derive failure from successful detail."""
    snapshot, manifest, assignment = _specialized_context(
        group=_descriptor_work_group(),
        evidence_expectation=_descriptor_evidence_expectation(),
        descriptor_obligations=[_descriptor_obligation()],
    )
    receipt = _receipt_for_context(
        snapshot, manifest, assignment, _descriptor_receipt_evidence()
    )
    receipt["outcome"] = "blocking-failure"
    receipt["diagnostics"] = [_failed_diagnostic(DESCRIPTOR_WORK_GROUP_ID)]
    evidence = cast("dict[str, object]", receipt["evidence"])
    category = cast("dict[str, object]", evidence["category-result"])
    category["outcome"] = "blocking-failure"
    category["diagnostics"] = [_failed_diagnostic(DESCRIPTOR_WORK_GROUP_ID)]

    _validate_specialized_receipt(receipt, snapshot, manifest, assignment)


def test_detail_profile_category_diagnostic_keeps_detail_success() -> None:
    """Allow category diagnostics above a successful detail profile."""
    snapshot, manifest, assignment, receipt = _detail_profile_receipt()
    receipt["outcome"] = "blocking-failure"
    receipt["diagnostics"] = [_failed_diagnostic(PREFLIGHT_WORK_GROUP_ID)]
    evidence = cast("dict[str, object]", receipt["evidence"])
    category = cast("dict[str, object]", evidence["category-result"])
    category["outcome"] = "blocking-failure"
    category["diagnostics"] = [_failed_diagnostic(PREFLIGHT_WORK_GROUP_ID)]

    _validate_specialized_receipt(receipt, snapshot, manifest, assignment)


def test_detail_failed_diagnostic_folds_into_subcheck_outcome() -> None:
    """Allow detail diagnostics to derive failure from successful subchecks."""
    snapshot, manifest, assignment, receipt = _detail_profile_receipt()
    receipt["outcome"] = "blocking-failure"
    receipt["diagnostics"] = [_failed_diagnostic(PREFLIGHT_WORK_GROUP_ID)]
    evidence = cast("dict[str, object]", receipt["evidence"])
    category = cast("dict[str, object]", evidence["category-result"])
    category["outcome"] = "blocking-failure"
    category["diagnostics"] = [_failed_diagnostic(PREFLIGHT_WORK_GROUP_ID)]
    detail = cast("dict[str, object]", category["detail"])
    detail["outcome"] = "blocking-failure"
    detail["diagnostics"] = [_failed_diagnostic(PREFLIGHT_WORK_GROUP_ID)]

    _validate_specialized_receipt(receipt, snapshot, manifest, assignment)


def test_detail_profile_ignores_non_blocking_subcheck_failure() -> None:
    """Only blocking subchecks derive detail-profile outcomes."""
    snapshot, manifest, assignment, receipt = _detail_profile_receipt()
    evidence = cast("dict[str, object]", receipt["evidence"])
    category = cast("dict[str, object]", evidence["category-result"])
    detail = cast("dict[str, object]", category["detail"])
    results = cast("list[dict[str, object]]", detail["subcheck-results"])
    results[1]["outcome"] = "blocking-failure"
    results[1]["diagnostics"] = [_failed_diagnostic(PREFLIGHT_WORK_GROUP_ID)]

    _validate_specialized_receipt(receipt, snapshot, manifest, assignment)


def test_detail_profile_rejects_blocking_subcheck_failure_mismatch() -> None:
    """Blocking subchecks still derive detail-profile failures."""
    snapshot, manifest, assignment, receipt = _detail_profile_receipt()
    evidence = cast("dict[str, object]", receipt["evidence"])
    category = cast("dict[str, object]", evidence["category-result"])
    detail = cast("dict[str, object]", category["detail"])
    results = cast("list[dict[str, object]]", detail["subcheck-results"])
    results[0]["outcome"] = "blocking-failure"
    results[0]["diagnostics"] = [_failed_diagnostic(PREFLIGHT_WORK_GROUP_ID)]

    with pytest.raises(ContractValidationError, match="subchecks"):
        _validate_specialized_receipt(receipt, snapshot, manifest, assignment)


def test_detail_profile_ignores_non_blocking_subcheck_skip() -> None:
    """Non-blocking skips do not make the detail profile skipped."""
    snapshot, manifest, assignment, receipt = _detail_profile_receipt()
    evidence = cast("dict[str, object]", receipt["evidence"])
    category = cast("dict[str, object]", evidence["category-result"])
    detail = cast("dict[str, object]", category["detail"])
    results = cast("list[dict[str, object]]", detail["subcheck-results"])
    results[1]["outcome"] = "skipped"
    results[1]["diagnostics"] = [_skipped_diagnostic(PREFLIGHT_WORK_GROUP_ID)]

    _validate_specialized_receipt(receipt, snapshot, manifest, assignment)


def test_release_result_failed_diagnostic_folds_into_nested_outcome() -> None:
    """Allow release diagnostics to derive failure from nested success."""
    snapshot, manifest, assignment = _specialized_context(
        group=_artifact_work_group(),
        evidence_expectation=_artifact_evidence_expectation(),
        validation_obligations=[_artifact_validation_obligation()],
        artifact_obligations=[_artifact_obligation()],
    )
    receipt = _receipt_for_context(
        snapshot, manifest, assignment, _release_receipt_evidence()
    )
    receipt["outcome"] = "blocking-failure"
    receipt["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    evidence = cast("dict[str, object]", receipt["evidence"])
    category = cast("dict[str, object]", evidence["category-result"])
    category["outcome"] = "blocking-failure"
    category["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    result = _release_result(receipt)
    result["outcome"] = "blocking-failure"
    result["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]

    _validate_specialized_receipt(receipt, snapshot, manifest, assignment)


def test_descriptor_receipt_rejects_nested_outcome_mismatch() -> None:
    """Derive descriptor category outcomes from nested obligation results."""
    snapshot, manifest, assignment = _specialized_context(
        group=_descriptor_work_group(),
        evidence_expectation=_descriptor_evidence_expectation(),
        descriptor_obligations=[_descriptor_obligation()],
    )
    receipt = _receipt_for_context(
        snapshot, manifest, assignment, _descriptor_receipt_evidence()
    )
    evidence = cast("dict[str, object]", receipt["evidence"])
    category = cast("dict[str, object]", evidence["category-result"])
    detail = cast("dict[str, object]", category["detail"])
    results = cast(
        "list[dict[str, object]]", detail["descriptor-obligation-results"]
    )
    results[0]["outcome"] = "blocking-failure"
    results[0]["diagnostics"] = [
        ci_validation_diagnostic(
            diagnostic_id="validation-work-failed/descriptor",
            code=DiagnosticFamily.VALIDATION_WORK_FAILED.value,
            detail=DiagnosticDetail.TOOLING.value,
            message="descriptor invalid",
            source_type="work-group",
            source_id=DESCRIPTOR_WORK_GROUP_ID,
            severity=DiagnosticSeverity.BLOCKING_FAILURE.value,
            verdict_effect=DiagnosticVerdictEffect.FAILED.value,
        ),
    ]

    with pytest.raises(ContractValidationError, match="detail results"):
        _validate_specialized_receipt(receipt, snapshot, manifest, assignment)


def test_descriptor_receipt_rejects_category_non_success_without_diag() -> None:
    """Require diagnostics when a category result is not successful."""
    snapshot, manifest, assignment = _specialized_context(
        group=_descriptor_work_group(),
        evidence_expectation=_descriptor_evidence_expectation(),
        descriptor_obligations=[_descriptor_obligation()],
    )
    receipt = _receipt_for_context(
        snapshot, manifest, assignment, _descriptor_receipt_evidence()
    )
    receipt["outcome"] = "blocking-failure"
    receipt["diagnostics"] = [_failed_diagnostic()]
    evidence = cast("dict[str, object]", receipt["evidence"])
    category = cast("dict[str, object]", evidence["category-result"])
    category["outcome"] = "blocking-failure"
    detail = cast("dict[str, object]", category["detail"])
    results = cast(
        "list[dict[str, object]]", detail["descriptor-obligation-results"]
    )
    results[0]["outcome"] = "blocking-failure"
    results[0]["diagnostics"] = [_failed_diagnostic()]

    with pytest.raises(ContractValidationError, match="non-success outcome"):
        _validate_specialized_receipt(receipt, snapshot, manifest, assignment)


def test_release_receipt_rejects_nested_outcome_mismatch() -> None:
    """Derive release-shaped outcomes from artifact and receipt results."""
    snapshot, manifest, assignment = _specialized_context(
        group=_artifact_work_group(),
        evidence_expectation=_artifact_evidence_expectation(),
        validation_obligations=[_artifact_validation_obligation()],
        artifact_obligations=[_artifact_obligation()],
    )
    receipt = _receipt_for_context(
        snapshot, manifest, assignment, _release_receipt_evidence()
    )
    result = _release_result(receipt)
    artifact = cast("dict[str, object]", result["artifact"])
    artifact["outcome"] = "blocking-failure"

    with pytest.raises(ContractValidationError, match="nested results"):
        _validate_specialized_receipt(receipt, snapshot, manifest, assignment)


def test_release_receipt_rejects_artifact_non_success_without_diagnostics() -> (
    None
):
    """Require diagnostics when nested release artifact results fail."""
    snapshot, manifest, assignment = _specialized_context(
        group=_artifact_work_group(),
        evidence_expectation=_artifact_evidence_expectation(),
        validation_obligations=[_artifact_validation_obligation()],
        artifact_obligations=[_artifact_obligation()],
    )
    receipt = _receipt_for_context(
        snapshot, manifest, assignment, _release_receipt_evidence()
    )
    receipt["outcome"] = "blocking-failure"
    receipt["diagnostics"] = [_failed_diagnostic()]
    evidence = cast("dict[str, object]", receipt["evidence"])
    category = cast("dict[str, object]", evidence["category-result"])
    category["outcome"] = "blocking-failure"
    category["diagnostics"] = [_failed_diagnostic()]
    result = _release_result(receipt)
    result["outcome"] = "blocking-failure"
    result["diagnostics"] = [_failed_diagnostic()]
    artifact = cast("dict[str, object]", result["artifact"])
    artifact["outcome"] = "blocking-failure"

    with pytest.raises(ContractValidationError, match="non-success outcome"):
        _validate_specialized_receipt(receipt, snapshot, manifest, assignment)


def test_release_receipt_rejects_success_without_schema_check() -> None:
    """Require release receipt schema validation for successful releases."""
    snapshot, manifest, assignment = _specialized_context(
        group=_artifact_work_group(),
        evidence_expectation=_artifact_evidence_expectation(),
        validation_obligations=[_artifact_validation_obligation()],
        artifact_obligations=[_artifact_obligation()],
    )
    receipt = _receipt_for_context(
        snapshot, manifest, assignment, _release_receipt_evidence()
    )
    result = _release_result(receipt)
    release_receipt = cast("dict[str, object]", result["release-receipt"])
    release_receipt["schema-checked"] = False

    with pytest.raises(ContractValidationError, match="schema-checked"):
        _validate_specialized_receipt(receipt, snapshot, manifest, assignment)


def test_release_receipt_rejects_unavailable_digest_without_diagnostic() -> (
    None
):
    """Require artifact-shape diagnostics for unavailable release digests."""
    snapshot, manifest, assignment = _specialized_context(
        group=_artifact_work_group(),
        evidence_expectation=_artifact_evidence_expectation(),
        validation_obligations=[_artifact_validation_obligation()],
        artifact_obligations=[_artifact_obligation()],
    )
    receipt = _receipt_for_context(
        snapshot, manifest, assignment, _release_receipt_evidence()
    )
    result = _release_result(receipt)
    artifact = cast("dict[str, object]", result["artifact"])
    observed = cast("dict[str, object]", artifact["observed"])
    digests = cast("list[dict[str, object]]", observed["digests"])
    digests[0]["digest-available"] = False
    digests[0]["digest"] = ""

    with pytest.raises(
        ContractValidationError, match="artifact-shape-unconfirmed"
    ):
        _validate_specialized_receipt(receipt, snapshot, manifest, assignment)


def test_release_receipt_rejects_success_with_unavailable_digest() -> None:
    """Block release-shaped success when a planned artifact digest is absent."""
    snapshot, manifest, assignment = _specialized_context(
        group=_artifact_work_group(),
        evidence_expectation=_artifact_evidence_expectation(),
        validation_obligations=[_artifact_validation_obligation()],
        artifact_obligations=[_artifact_obligation()],
    )
    receipt = _receipt_for_context(
        snapshot, manifest, assignment, _release_receipt_evidence()
    )
    result = _release_result(receipt)
    artifact = cast("dict[str, object]", result["artifact"])
    observed = cast("dict[str, object]", artifact["observed"])
    digests = cast("list[dict[str, object]]", observed["digests"])
    digests[0]["digest-available"] = False
    digests[0]["digest"] = ""
    digests[0]["diagnostics"] = [
        _artifact_shape_diagnostic(ARTIFACT_WORK_GROUP_ID)
    ]

    with pytest.raises(ContractValidationError, match="available for success"):
        _validate_specialized_receipt(receipt, snapshot, manifest, assignment)


def test_release_receipt_allows_artifact_shape_blocking_failure() -> None:
    """Allow unavailable digests when artifact shape evidence blocks release."""
    snapshot, manifest, assignment = _specialized_context(
        group=_artifact_work_group(),
        evidence_expectation=_artifact_evidence_expectation(),
        validation_obligations=[_artifact_validation_obligation()],
        artifact_obligations=[_artifact_obligation()],
    )
    receipt = _receipt_for_context(
        snapshot, manifest, assignment, _release_receipt_evidence()
    )
    result = _release_result(receipt)
    artifact = cast("dict[str, object]", result["artifact"])
    observed = cast("dict[str, object]", artifact["observed"])
    digests = cast("list[dict[str, object]]", observed["digests"])
    digests[0]["digest-available"] = False
    digests[0]["digest"] = ""
    digests[0]["diagnostics"] = [
        _artifact_shape_diagnostic(ARTIFACT_WORK_GROUP_ID)
    ]
    artifact["outcome"] = "blocking-failure"
    artifact["diagnostics"] = [
        _artifact_shape_diagnostic(ARTIFACT_WORK_GROUP_ID)
    ]
    result["outcome"] = "blocking-failure"
    result["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    evidence = cast("dict[str, object]", receipt["evidence"])
    category = cast("dict[str, object]", evidence["category-result"])
    category["outcome"] = "blocking-failure"
    category["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    receipt["outcome"] = "blocking-failure"
    receipt["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]

    _validate_specialized_receipt(receipt, snapshot, manifest, assignment)


def test_release_digest_artifact_shape_diagnostic_requires_failed_effect() -> (
    None
):
    """Digest diagnostics inherit receipt diagnostic code semantics."""
    snapshot, manifest, assignment = _specialized_context(
        group=_artifact_work_group(),
        evidence_expectation=_artifact_evidence_expectation(),
        validation_obligations=[_artifact_validation_obligation()],
        artifact_obligations=[_artifact_obligation()],
    )
    receipt = _receipt_for_context(
        snapshot, manifest, assignment, _release_receipt_evidence()
    )
    receipt["outcome"] = "blocking-failure"
    receipt["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    evidence = cast("dict[str, object]", receipt["evidence"])
    category = cast("dict[str, object]", evidence["category-result"])
    category["outcome"] = "blocking-failure"
    category["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    result = _release_result(receipt)
    result["outcome"] = "blocking-failure"
    result["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    artifact = cast("dict[str, object]", result["artifact"])
    artifact["outcome"] = "blocking-failure"
    artifact["diagnostics"] = [
        _artifact_shape_diagnostic(ARTIFACT_WORK_GROUP_ID)
    ]
    observed = cast("dict[str, object]", artifact["observed"])
    digests = cast("list[dict[str, object]]", observed["digests"])
    digests[0]["digest-available"] = False
    digests[0]["digest"] = ""
    digests[0]["diagnostics"] = [
        _artifact_shape_diagnostic(ARTIFACT_WORK_GROUP_ID)
    ]
    diagnostics = cast("list[dict[str, object]]", digests[0]["diagnostics"])
    diagnostics[0]["verdict-effect"] = DiagnosticVerdictEffect.NONE.value

    with pytest.raises(ContractValidationError, match="failed verdict-effect"):
        _validate_specialized_receipt(receipt, snapshot, manifest, assignment)


def test_release_receipt_rejects_success_with_failed_digest_diagnostic() -> (
    None
):
    """Failed digest diagnostics make artifact success inadmissible."""
    snapshot, manifest, assignment = _specialized_context(
        group=_artifact_work_group(),
        evidence_expectation=_artifact_evidence_expectation(),
        validation_obligations=[_artifact_validation_obligation()],
        artifact_obligations=[_artifact_obligation()],
    )
    receipt = _receipt_for_context(
        snapshot, manifest, assignment, _release_receipt_evidence()
    )
    result = _release_result(receipt)
    artifact = cast("dict[str, object]", result["artifact"])
    observed = cast("dict[str, object]", artifact["observed"])
    digests = cast("list[dict[str, object]]", observed["digests"])
    digests[0]["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]

    with pytest.raises(
        ContractValidationError, match="failed digest diagnostics"
    ):
        _validate_specialized_receipt(receipt, snapshot, manifest, assignment)


def test_release_receipt_rejects_nested_success_artifact_digest() -> None:
    """Do not let a sibling failure mask artifact success invariants."""
    snapshot, manifest, assignment = _specialized_context(
        group=_artifact_work_group(),
        evidence_expectation=_artifact_evidence_expectation(),
        validation_obligations=[_artifact_validation_obligation()],
        artifact_obligations=[_artifact_obligation()],
    )
    receipt = _receipt_for_context(
        snapshot, manifest, assignment, _release_receipt_evidence()
    )
    receipt["outcome"] = "blocking-failure"
    receipt["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    evidence = cast("dict[str, object]", receipt["evidence"])
    category = cast("dict[str, object]", evidence["category-result"])
    category["outcome"] = "blocking-failure"
    category["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    result = _release_result(receipt)
    result["outcome"] = "blocking-failure"
    result["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    artifact = cast("dict[str, object]", result["artifact"])
    observed = cast("dict[str, object]", artifact["observed"])
    digests = cast("list[dict[str, object]]", observed["digests"])
    digests[0]["digest-available"] = False
    digests[0]["digest"] = ""
    digests[0]["diagnostics"] = [
        _artifact_shape_diagnostic(ARTIFACT_WORK_GROUP_ID)
    ]
    release_receipt = cast("dict[str, object]", result["release-receipt"])
    release_receipt["outcome"] = "blocking-failure"
    release_receipt["diagnostics"] = [
        _failed_diagnostic(ARTIFACT_WORK_GROUP_ID)
    ]

    with pytest.raises(ContractValidationError, match="available for success"):
        _validate_specialized_receipt(receipt, snapshot, manifest, assignment)


def test_release_receipt_rejects_masked_failed_digest_diagnostic() -> None:
    """Do not let a sibling failure mask failed digest diagnostics."""
    snapshot, manifest, assignment = _specialized_context(
        group=_artifact_work_group(),
        evidence_expectation=_artifact_evidence_expectation(),
        validation_obligations=[_artifact_validation_obligation()],
        artifact_obligations=[_artifact_obligation()],
    )
    receipt = _receipt_for_context(
        snapshot, manifest, assignment, _release_receipt_evidence()
    )
    receipt["outcome"] = "blocking-failure"
    receipt["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    evidence = cast("dict[str, object]", receipt["evidence"])
    category = cast("dict[str, object]", evidence["category-result"])
    category["outcome"] = "blocking-failure"
    category["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    result = _release_result(receipt)
    result["outcome"] = "blocking-failure"
    result["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    artifact = cast("dict[str, object]", result["artifact"])
    observed = cast("dict[str, object]", artifact["observed"])
    digests = cast("list[dict[str, object]]", observed["digests"])
    digests[0]["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    release_receipt = cast("dict[str, object]", result["release-receipt"])
    release_receipt["outcome"] = "blocking-failure"
    release_receipt["diagnostics"] = [
        _failed_diagnostic(ARTIFACT_WORK_GROUP_ID)
    ]

    with pytest.raises(
        ContractValidationError, match="failed digest diagnostics"
    ):
        _validate_specialized_receipt(receipt, snapshot, manifest, assignment)


def test_release_receipt_accepts_failed_digest_with_blocking_artifact() -> None:
    """Allow failed digest diagnostics when artifact failure propagates."""
    snapshot, manifest, assignment = _specialized_context(
        group=_artifact_work_group(),
        evidence_expectation=_artifact_evidence_expectation(),
        validation_obligations=[_artifact_validation_obligation()],
        artifact_obligations=[_artifact_obligation()],
    )
    receipt = _receipt_for_context(
        snapshot, manifest, assignment, _release_receipt_evidence()
    )
    receipt["outcome"] = "blocking-failure"
    receipt["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    evidence = cast("dict[str, object]", receipt["evidence"])
    category = cast("dict[str, object]", evidence["category-result"])
    category["outcome"] = "blocking-failure"
    category["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    result = _release_result(receipt)
    result["outcome"] = "blocking-failure"
    result["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    artifact = cast("dict[str, object]", result["artifact"])
    artifact["outcome"] = "blocking-failure"
    artifact["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    observed = cast("dict[str, object]", artifact["observed"])
    digests = cast("list[dict[str, object]]", observed["digests"])
    digests[0]["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]

    _validate_specialized_receipt(receipt, snapshot, manifest, assignment)


def test_release_receipt_rejects_nested_success_receipt_schema() -> None:
    """Do not let a sibling failure mask release receipt success invariants."""
    snapshot, manifest, assignment = _specialized_context(
        group=_artifact_work_group(),
        evidence_expectation=_artifact_evidence_expectation(),
        validation_obligations=[_artifact_validation_obligation()],
        artifact_obligations=[_artifact_obligation()],
    )
    receipt = _receipt_for_context(
        snapshot, manifest, assignment, _release_receipt_evidence()
    )
    receipt["outcome"] = "blocking-failure"
    receipt["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    evidence = cast("dict[str, object]", receipt["evidence"])
    category = cast("dict[str, object]", evidence["category-result"])
    category["outcome"] = "blocking-failure"
    category["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    result = _release_result(receipt)
    result["outcome"] = "blocking-failure"
    result["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    artifact = cast("dict[str, object]", result["artifact"])
    artifact["outcome"] = "blocking-failure"
    artifact["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    release_receipt = cast("dict[str, object]", result["release-receipt"])
    release_receipt["schema-checked"] = False

    with pytest.raises(ContractValidationError, match="schema-checked"):
        _validate_specialized_receipt(receipt, snapshot, manifest, assignment)


def test_release_receipt_rejects_masked_skipped_artifact() -> None:
    """Do not let sibling failures mask skipped artifact production."""
    snapshot, manifest, assignment = _specialized_context(
        group=_artifact_work_group(),
        evidence_expectation=_artifact_evidence_expectation(),
        validation_obligations=[_artifact_validation_obligation()],
        artifact_obligations=[_artifact_obligation()],
    )
    receipt = _receipt_for_context(
        snapshot, manifest, assignment, _release_receipt_evidence()
    )
    receipt["outcome"] = "blocking-failure"
    receipt["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    evidence = cast("dict[str, object]", receipt["evidence"])
    evidence["artifact-refs"] = []
    category = cast("dict[str, object]", evidence["category-result"])
    category["outcome"] = "blocking-failure"
    category["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    result = _release_result(receipt)
    result["outcome"] = "blocking-failure"
    result["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    artifact = cast("dict[str, object]", result["artifact"])
    artifact["outcome"] = "skipped"
    artifact["diagnostics"] = [_skipped_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    observed = cast("dict[str, object]", artifact["observed"])
    observed["refs"] = []
    observed["digests"] = []
    release_receipt = cast("dict[str, object]", result["release-receipt"])
    release_receipt["outcome"] = "blocking-failure"
    release_receipt["diagnostics"] = [
        _failed_diagnostic(ARTIFACT_WORK_GROUP_ID)
    ]

    with pytest.raises(ContractValidationError, match="nested skipped"):
        _validate_specialized_receipt(receipt, snapshot, manifest, assignment)


def test_release_receipt_rejects_masked_skipped_release_receipt() -> None:
    """Do not let sibling failures mask skipped release receipt validation."""
    snapshot, manifest, assignment = _specialized_context(
        group=_artifact_work_group(),
        evidence_expectation=_artifact_evidence_expectation(),
        validation_obligations=[_artifact_validation_obligation()],
        artifact_obligations=[_artifact_obligation()],
    )
    receipt = _receipt_for_context(
        snapshot, manifest, assignment, _release_receipt_evidence()
    )
    receipt["outcome"] = "blocking-failure"
    receipt["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    evidence = cast("dict[str, object]", receipt["evidence"])
    category = cast("dict[str, object]", evidence["category-result"])
    category["outcome"] = "blocking-failure"
    category["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    result = _release_result(receipt)
    result["outcome"] = "blocking-failure"
    result["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    artifact = cast("dict[str, object]", result["artifact"])
    artifact["outcome"] = "blocking-failure"
    artifact["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    release_receipt = cast("dict[str, object]", result["release-receipt"])
    release_receipt["outcome"] = "skipped"
    release_receipt["schema-checked"] = False
    release_receipt["diagnostics"] = [
        _skipped_diagnostic(ARTIFACT_WORK_GROUP_ID)
    ]

    with pytest.raises(ContractValidationError, match="nested skipped"):
        _validate_specialized_receipt(receipt, snapshot, manifest, assignment)


def test_release_receipt_accepts_dependency_blocked_skip() -> None:
    """Allow dependency-blocked release work to skip artifact production."""
    snapshot, manifest, assignment = _specialized_context(
        group=_dependency_blocked_artifact_work_group(),
        evidence_expectation=_artifact_evidence_expectation(),
        validation_obligations=[_artifact_validation_obligation()],
        artifact_obligations=[_artifact_obligation()],
    )
    evidence = _release_receipt_evidence()
    evidence["artifact-refs"] = []
    category = cast("dict[str, object]", evidence["category-result"])
    category["outcome"] = "skipped"
    category["diagnostics"] = [_skipped_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    result = cast(
        "dict[str, object]",
        cast("dict[str, list[dict[str, object]]]", category["detail"])[
            "artifact-obligation-results"
        ][0],
    )
    result["outcome"] = "skipped"
    result["diagnostics"] = [_skipped_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    artifact = cast("dict[str, object]", result["artifact"])
    artifact["outcome"] = "skipped"
    artifact["diagnostics"] = [_skipped_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    observed = cast("dict[str, object]", artifact["observed"])
    observed["refs"] = []
    observed["digests"] = []
    release_receipt = cast("dict[str, object]", result["release-receipt"])
    release_receipt["outcome"] = "skipped"
    release_receipt["schema-checked"] = False
    release_receipt["diagnostics"] = [
        _skipped_diagnostic(ARTIFACT_WORK_GROUP_ID)
    ]
    receipt = freeze_ci_validation_receipt(
        plan=snapshot.plan,
        selector_assignments_manifest=manifest,
        assignment=assignment,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
        receipt_id="receipt-001",
        created_at=CREATED_AT,
        execution_observed_commit_sha=TREE_SHA,
        outcome="skipped",
        evidence=evidence,
        diagnostics=[_skipped_diagnostic(ARTIFACT_WORK_GROUP_ID)],
    )

    _validate_specialized_receipt(receipt, snapshot, manifest, assignment)


def test_release_receipt_rejects_dependency_skip_without_depends_on() -> None:
    """Reject dependency-blocked release skips without frozen depends-on."""
    snapshot, manifest, assignment = _specialized_context(
        group=_artifact_work_group(),
        evidence_expectation=_artifact_evidence_expectation(),
        validation_obligations=[_artifact_validation_obligation()],
        artifact_obligations=[_artifact_obligation()],
    )
    evidence = _release_receipt_evidence()
    evidence["artifact-refs"] = []
    category = cast("dict[str, object]", evidence["category-result"])
    category["outcome"] = "skipped"
    category["diagnostics"] = [_skipped_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    result = cast(
        "dict[str, object]",
        cast("dict[str, list[dict[str, object]]]", category["detail"])[
            "artifact-obligation-results"
        ][0],
    )
    result["outcome"] = "skipped"
    result["diagnostics"] = [_skipped_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    artifact = cast("dict[str, object]", result["artifact"])
    artifact["outcome"] = "skipped"
    artifact["diagnostics"] = [_skipped_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    observed = cast("dict[str, object]", artifact["observed"])
    observed["refs"] = []
    observed["digests"] = []
    release_receipt = cast("dict[str, object]", result["release-receipt"])
    release_receipt["outcome"] = "skipped"
    release_receipt["schema-checked"] = False
    release_receipt["diagnostics"] = [
        _skipped_diagnostic(ARTIFACT_WORK_GROUP_ID)
    ]

    with pytest.raises(ContractValidationError, match="depends-on"):
        freeze_ci_validation_receipt(
            plan=snapshot.plan,
            selector_assignments_manifest=manifest,
            assignment=assignment,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
            receipt_id="receipt-001",
            created_at=CREATED_AT,
            execution_observed_commit_sha=TREE_SHA,
            outcome="skipped",
            evidence=evidence,
            diagnostics=[_skipped_diagnostic(ARTIFACT_WORK_GROUP_ID)],
        )


def test_release_receipt_rejects_dependency_blocked_skip_when_unexpected() -> (
    None
):
    """Bind dependency-blocked release skips to planned receipt expectation."""
    snapshot, manifest, assignment = _specialized_context(
        group=_dependency_blocked_artifact_work_group(),
        evidence_expectation=_artifact_evidence_expectation(),
        validation_obligations=[_artifact_validation_obligation()],
        artifact_obligations=[_artifact_obligation()],
    )
    evidence = _release_receipt_evidence()
    evidence["artifact-refs"] = []
    category = cast("dict[str, object]", evidence["category-result"])
    category["outcome"] = "skipped"
    category["diagnostics"] = [_skipped_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    result = cast(
        "dict[str, object]",
        cast("dict[str, list[dict[str, object]]]", category["detail"])[
            "artifact-obligation-results"
        ][0],
    )
    result["outcome"] = "skipped"
    result["diagnostics"] = [_skipped_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    artifact = cast("dict[str, object]", result["artifact"])
    artifact["outcome"] = "skipped"
    artifact["diagnostics"] = [_skipped_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    observed = cast("dict[str, object]", artifact["observed"])
    observed["refs"] = []
    observed["digests"] = []
    release_receipt = cast("dict[str, object]", result["release-receipt"])
    release_receipt["outcome"] = "skipped"
    release_receipt["schema-checked"] = False
    release_receipt["diagnostics"] = [
        _skipped_diagnostic(ARTIFACT_WORK_GROUP_ID)
    ]
    receipt = freeze_ci_validation_receipt(
        plan=snapshot.plan,
        selector_assignments_manifest=manifest,
        assignment=assignment,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
        receipt_id="receipt-001",
        created_at=CREATED_AT,
        execution_observed_commit_sha=TREE_SHA,
        outcome="skipped",
        evidence=evidence,
        diagnostics=[_skipped_diagnostic(ARTIFACT_WORK_GROUP_ID)],
    )
    frozen_release_receipt = cast(
        "dict[str, object]", _release_result(receipt)["release-receipt"]
    )
    frozen_release_receipt["expected"] = False

    with pytest.raises(ContractValidationError, match="expected"):
        _validate_specialized_receipt(receipt, snapshot, manifest, assignment)


def test_detail_profile_requires_diagnostics_for_skipped_subcheck() -> None:
    """Require skipped detail-profile subchecks to carry diagnostics."""
    snapshot, manifest, assignment, receipt = _detail_profile_receipt()
    evidence = cast("dict[str, object]", receipt["evidence"])
    category = cast("dict[str, object]", evidence["category-result"])
    category["outcome"] = "skipped"
    detail = cast("dict[str, object]", category["detail"])
    detail["outcome"] = "skipped"
    subchecks = cast("list[dict[str, object]]", detail["subcheck-results"])
    subchecks[0]["outcome"] = "skipped"

    with pytest.raises(ContractValidationError, match="must explain skipped"):
        validate_ci_validation_receipt(
            receipt,
            plan=snapshot.plan,
            selector_assignments_manifest=manifest,
            assignment=assignment,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_detail_profile_rejects_success_subcheck_with_failed_diagnostic() -> (
    None
):
    """Bind subcheck outcomes to their diagnostic verdict effects."""
    snapshot, manifest, assignment, receipt = _detail_profile_receipt()
    evidence = cast("dict[str, object]", receipt["evidence"])
    category = cast("dict[str, object]", evidence["category-result"])
    detail = cast("dict[str, object]", category["detail"])
    subchecks = cast("list[dict[str, object]]", detail["subcheck-results"])
    subchecks[0]["diagnostics"] = [_failed_diagnostic()]

    with pytest.raises(ContractValidationError, match="blocking-failure"):
        validate_ci_validation_receipt(
            receipt,
            plan=snapshot.plan,
            selector_assignments_manifest=manifest,
            assignment=assignment,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )
