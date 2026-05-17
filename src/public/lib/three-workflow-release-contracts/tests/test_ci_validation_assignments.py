"""Selector-assignment and writer-observation helper tests."""

from __future__ import annotations

from copy import deepcopy
from typing import cast

import pytest
from three_workflow_release_contracts import (
    API_VERSIONS_BY_KIND,
    CiValidationArtifactProducerAuthority,
    CiValidationKind,
    ContractValidationError,
    DiagnosticDetail,
    DiagnosticFamily,
    DiagnosticSeverity,
    DiagnosticVerdictEffect,
    GitHubActionsArtifactMetadata,
    admit_ci_validation_selector_assignments_artifact,
    admit_ci_validation_writer_observation_artifact,
    artifact_physical_name,
    canonical_json_digest,
    ci_validation_assignment_id,
    ci_validation_diagnostic,
    ci_validation_plan_digest,
    ci_validation_receipt_artifact_ref,
    ci_validation_request_artifact_ref,
    ci_validation_request_projection,
    ci_validation_selector_assignments_artifact_ref,
    ci_validation_writer_id,
    ci_validation_writer_observation_artifact_ref,
    collect_artifacts_by_name,
    freeze_ci_validation_plan,
    freeze_ci_validation_selector_assignments,
    freeze_ci_validation_writer_observation,
    normalize_ci_validation_request,
    validate_ci_validation_selector_assignments,
    validate_ci_validation_writer_observation,
)

RUN_ID = "25887422010"
RUN_ATTEMPT = "1"
CREATED_AT = "2026-05-14T21:09:21Z"
PLAN_ID = "plan-25887422010-1"
PLAN_DIGEST_PLACEHOLDER = "0" * 64
TREE_SHA = "b" * 40
WORK_GROUP_ID = "wg-python-gate"
SECOND_WORK_GROUP_ID = "lightweight-preflight"


def _work_group(
    work_group_id: str = WORK_GROUP_ID,
    *,
    kind: str = "ecosystem-gate",
) -> dict[str, object]:
    return {
        "work-group-id": work_group_id,
        "kind": kind,
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


def _terminal_group(
    *, depends_on: list[str] | None = None
) -> dict[str, object]:
    return {
        "work-group-id": "evidence-aggregation",
        "kind": "evidence-aggregation",
        "coverage-target": {
            "type": "aggregation",
            "id": "ci-validation-aggregate",
        },
        "runner-family": "ubuntu",
        "depends-on": depends_on or [WORK_GROUP_ID],
        "aggregate-output": CiValidationKind.AGGREGATE.value,
    }


def _incomplete_plan(
    *, work_groups: list[dict[str, object]] | None = None
) -> dict[str, object]:
    document: dict[str, object] = {
        "api-version": API_VERSIONS_BY_KIND[CiValidationKind.PLAN.value],
        "kind": CiValidationKind.PLAN.value,
        "created-at": CREATED_AT,
        "repository": {"owner": "hcoona", "name": "three"},
        "run": {
            "workflow": "CI Validation",
            "run-id": RUN_ID,
            "run-attempt": RUN_ATTEMPT,
        },
        "schema-diagnostics": [],
        "plan-id": PLAN_ID,
        "plan-digest": PLAN_DIGEST_PLACEHOLDER,
        "work-groups": work_groups or [_work_group(), _terminal_group()],
    }
    document["plan-digest"] = ci_validation_plan_digest(document)
    return document


def _request(*, changed_files: list[str] | None = None) -> dict[str, object]:
    if changed_files is None:
        changed_files = [
            "src/public/lib/example.py",
            "tests/example_test.py",
        ]
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
            "changed-files": changed_files,
            "source": "pull_request",
            "diagnostic": None,
            "diagnostic-detail": None,
        },
    }
    document["request-digest"] = canonical_json_digest(
        ci_validation_request_projection(document),
    )
    return document


def _normalized_request(*, changed_files: list[str] | None = None):
    result = normalize_ci_validation_request(
        _request(changed_files=changed_files),
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


def _valid_snapshot():
    return freeze_ci_validation_plan(
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


def _fail_closed_snapshot():
    return freeze_ci_validation_plan(
        request=_normalized_request(changed_files=[]),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="fail-closed",
        diagnostics=[
            ci_validation_diagnostic(
                diagnostic_id="invalid-plan/001",
                code=DiagnosticFamily.INVALID_PLAN.value,
                detail=DiagnosticDetail.PLAN_MISSING.value,
                message="plan was not available",
                source_type="request",
                source_id=None,
                severity=DiagnosticSeverity.FAIL_CLOSED.value,
                verdict_effect=DiagnosticVerdictEffect.FAIL_CLOSED.value,
            ),
        ],
        fact_snapshot_providers=None,
    )


def _writer_id(*, job: str = "ci-validation-selector-python") -> str:
    return ci_validation_writer_id(
        workflow="CI Validation",
        job=job,
        matrix={"selector": WORK_GROUP_ID},
    )


def _manifest() -> dict[str, object]:
    snapshot = _valid_snapshot()
    return freeze_ci_validation_selector_assignments(
        plan=snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
        trusted_writer_ids={WORK_GROUP_ID: _writer_id()},
        created_at=CREATED_AT,
    )


def _validate_manifest(manifest: object, snapshot) -> None:
    validate_ci_validation_selector_assignments(
        manifest,
        plan=snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )


def _writer_context():
    snapshot = _valid_snapshot()
    manifest = freeze_ci_validation_selector_assignments(
        plan=snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
        trusted_writer_ids={WORK_GROUP_ID: _writer_id()},
        created_at=CREATED_AT,
    )
    assignments = manifest["assignments"]
    assert isinstance(assignments, list)
    assignment = assignments[0]
    assert isinstance(assignment, dict)
    return snapshot, manifest, assignment


def test_assignment_and_observation_refs_are_contract_owned() -> None:
    """Derive path-safe IDs and logical refs without payload identity claims."""
    writer_id = _writer_id()

    assert ci_validation_assignment_id(work_group_id=WORK_GROUP_ID) == (
        WORK_GROUP_ID
    )
    assert writer_id.startswith("github-actions-job:")
    assert writer_id == _writer_id()
    assert ci_validation_selector_assignments_artifact_ref(
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
    ) == ("ci-validation/assignments/25887422010/1/selector-assignments.json")
    assert ci_validation_receipt_artifact_ref(
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        work_group_id=WORK_GROUP_ID,
    ) == ("ci-validation/receipts/25887422010/1/wg-python-gate/receipt.json")
    assert ci_validation_writer_observation_artifact_ref(
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        assignment_id=WORK_GROUP_ID,
    ) == ("ci-validation/writer-observations/25887422010/1/wg-python-gate.json")


def test_writer_id_canonicalizes_matrix_order() -> None:
    """Use stable canonical digesting for trusted and observed writer IDs."""
    first = ci_validation_writer_id(
        workflow="CI Validation",
        job="selector",
        matrix={"b": 2, "a": "one"},
    )
    second = ci_validation_writer_id(
        workflow="CI Validation",
        job="selector",
        matrix={"a": "one", "b": 2},
    )

    assert first == second


@pytest.mark.parametrize(
    "work_group_id",
    [
        "../escape",
        "Uppercase",
        "with/slash",
        "",
    ],
)
def test_refs_reject_non_path_safe_ids(work_group_id: str) -> None:
    """Assignment-bearing path segments fail closed before ref construction."""
    with pytest.raises(ContractValidationError):
        ci_validation_assignment_id(work_group_id=work_group_id)
    with pytest.raises(ContractValidationError):
        ci_validation_receipt_artifact_ref(
            run_id=RUN_ID,
            run_attempt=RUN_ATTEMPT,
            work_group_id=work_group_id,
        )
    with pytest.raises(ContractValidationError):
        ci_validation_writer_observation_artifact_ref(
            run_id=RUN_ID,
            run_attempt=RUN_ATTEMPT,
            assignment_id=work_group_id,
        )


def test_freeze_selector_assignments_binds_executable_work_groups() -> None:
    """Materialize exactly one assignment per executable selector."""
    snapshot = _valid_snapshot()
    manifest = freeze_ci_validation_selector_assignments(
        plan=snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
        trusted_writer_ids={WORK_GROUP_ID: _writer_id()},
        created_at=CREATED_AT,
    )

    _validate_manifest(manifest, snapshot)

    assert manifest["plan-id"] == PLAN_ID
    assert manifest["plan-digest"] == snapshot.plan["plan-digest"]
    assert manifest["assignments"] == [
        {
            "assignment-id": WORK_GROUP_ID,
            "work-group-id": WORK_GROUP_ID,
            "trusted-writer-id": _writer_id(),
            "writer-identity-source": "github-actions-job-context",
            "receipt-artifact-ref": ci_validation_receipt_artifact_ref(
                run_id=RUN_ID,
                run_attempt=RUN_ATTEMPT,
                work_group_id=WORK_GROUP_ID,
            ),
            "writer-observation-ref": (
                ci_validation_writer_observation_artifact_ref(
                    run_id=RUN_ID,
                    run_attempt=RUN_ATTEMPT,
                    assignment_id=WORK_GROUP_ID,
                )
            ),
        },
    ]


def test_empty_selector_assignments_are_valid_for_no_executable_work() -> None:
    """Fail-closed or no-work plans still get one empty manifest."""
    snapshot = _fail_closed_snapshot()

    manifest = freeze_ci_validation_selector_assignments(
        plan=snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
        trusted_writer_ids={},
        created_at=CREATED_AT,
    )

    _validate_manifest(manifest, snapshot)
    assert manifest["assignments"] == []


def test_evidence_aggregation_does_not_receive_writer_assignment() -> None:
    """Terminal aggregation is closed-kind but not receipt-executable."""
    snapshot = _fail_closed_snapshot()

    with pytest.raises(ContractValidationError, match="executable work group"):
        freeze_ci_validation_selector_assignments(
            plan=snapshot.plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
            trusted_writer_ids={"evidence-aggregation": _writer_id()},
            created_at=CREATED_AT,
        )


def test_incomplete_plan_rejected_before_assignment_materialization() -> None:
    """Partial plan payloads cannot authorize selector receipt writers."""
    with pytest.raises(ContractValidationError, match="is required"):
        freeze_ci_validation_selector_assignments(
            plan=_incomplete_plan(),
            trusted_writer_ids={WORK_GROUP_ID: _writer_id()},
            created_at=CREATED_AT,
        )


def test_unknown_work_group_kind_rejected_before_assignment() -> None:
    """Unknown plan work-group kinds cannot authorize receipt writers."""
    snapshot = _valid_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    work_groups = plan["work-groups"]
    assert isinstance(work_groups, list)
    assert isinstance(work_groups[0], dict)
    work_groups[0]["kind"] = "unknown-validation-kind"
    plan["plan-digest"] = ci_validation_plan_digest(plan)

    with pytest.raises(ContractValidationError, match="not registered"):
        freeze_ci_validation_selector_assignments(
            plan=plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
            trusted_writer_ids={WORK_GROUP_ID: _writer_id()},
            created_at=CREATED_AT,
        )


def test_selector_assignments_reject_missing_executable() -> None:
    """Do not admit receipts when the assignment manifest is incomplete."""
    manifest = _manifest()
    manifest["assignments"] = []

    with pytest.raises(ContractValidationError, match="exactly one assignment"):
        _validate_manifest(manifest, _valid_snapshot())


def test_selector_assignment_validation_rejects_wrong_receipt_ref() -> None:
    """Receipt refs bind to the plan work-group ID, not payload claims."""
    manifest = _manifest()
    assignments = manifest["assignments"]
    assert isinstance(assignments, list)
    assignments[0]["receipt-artifact-ref"] = ci_validation_receipt_artifact_ref(
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        work_group_id=SECOND_WORK_GROUP_ID,
    )

    with pytest.raises(ContractValidationError, match="receipt ref"):
        _validate_manifest(manifest, _valid_snapshot())


def test_selector_assignments_reject_mismatched_assignment_id() -> None:
    """Assignment IDs must be derived from the matched work-group ID."""
    manifest = _manifest()
    assignments = manifest["assignments"]
    assert isinstance(assignments, list)
    assignments[0]["assignment-id"] = "arbitrary-assignment"
    assignments[0]["writer-observation-ref"] = (
        ci_validation_writer_observation_artifact_ref(
            run_id=RUN_ID,
            run_attempt=RUN_ATTEMPT,
            assignment_id="arbitrary-assignment",
        )
    )

    with pytest.raises(ContractValidationError, match="derived work-group"):
        _validate_manifest(manifest, _valid_snapshot())


def test_selector_assignments_reject_duplicate_observation_refs() -> None:
    """Duplicate writer-observation refs are not ambiguous authority."""
    manifest = _manifest()
    assignments = manifest["assignments"]
    assert isinstance(assignments, list)
    assignments.append(deepcopy(assignments[0]))

    with pytest.raises(ContractValidationError, match="unique"):
        _validate_manifest(manifest, _valid_snapshot())


def test_writer_observation_binds_assignment_and_artifact_instance() -> None:
    """Validate observed writer identity from boundary data."""
    snapshot, manifest, assignment = _writer_context()
    observation = freeze_ci_validation_writer_observation(
        plan=snapshot.plan,
        selector_assignments_manifest=manifest,
        assignment=assignment,
        artifact_instance_id="7005183651",
        observed_writer_id=assignment["trusted-writer-id"],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    validate_ci_validation_writer_observation(
        observation,
        plan=snapshot.plan,
        selector_assignments_manifest=manifest,
        assignment=assignment,
        expected_artifact_instance_id="7005183651",
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    assert observation["assignment-id"] == assignment["assignment-id"]
    assert observation["observed-writer-id"] == assignment["trusted-writer-id"]


def test_writer_observation_freeze_rejects_assignment_outside_manifest() -> (
    None
):
    """Writer observations must use selector-manifest-backed assignments."""
    snapshot, manifest, assignment = _writer_context()
    forged_assignment = dict(assignment)
    forged_assignment["work-group-id"] = "forged-work-group"
    forged_assignment["assignment-id"] = "forged-work-group"
    forged_assignment["receipt-artifact-ref"] = (
        ci_validation_receipt_artifact_ref(
            run_id=RUN_ID,
            run_attempt=RUN_ATTEMPT,
            work_group_id="forged-work-group",
        )
    )
    forged_assignment["writer-observation-ref"] = (
        ci_validation_writer_observation_artifact_ref(
            run_id=RUN_ID,
            run_attempt=RUN_ATTEMPT,
            assignment_id="forged-work-group",
        )
    )

    with pytest.raises(ContractValidationError, match="manifest entry"):
        freeze_ci_validation_writer_observation(
            plan=snapshot.plan,
            selector_assignments_manifest=manifest,
            assignment=forged_assignment,
            artifact_instance_id="7005183651",
            observed_writer_id=forged_assignment["trusted-writer-id"],
            created_at=CREATED_AT,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_writer_observation_validate_rejects_forged_assignment_mapping() -> (
    None
):
    """An internally consistent assignment mapping is not enough authority."""
    snapshot, manifest, assignment = _writer_context()
    observation = freeze_ci_validation_writer_observation(
        plan=snapshot.plan,
        selector_assignments_manifest=manifest,
        assignment=assignment,
        artifact_instance_id="7005183651",
        observed_writer_id=assignment["trusted-writer-id"],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    forged_assignment = dict(assignment)
    forged_assignment["trusted-writer-id"] = ci_validation_writer_id(
        workflow="CI Validation",
        job="forged-job",
        matrix={"selector": WORK_GROUP_ID},
    )

    with pytest.raises(ContractValidationError, match="exactly match"):
        validate_ci_validation_writer_observation(
            observation,
            plan=snapshot.plan,
            selector_assignments_manifest=manifest,
            assignment=forged_assignment,
            expected_artifact_instance_id="7005183651",
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_writer_observation_rejects_self_attested_mismatched_writer() -> None:
    """A payload writer claim cannot override the trusted assignment writer."""
    snapshot, manifest, assignment = _writer_context()
    observation = freeze_ci_validation_writer_observation(
        plan=snapshot.plan,
        selector_assignments_manifest=manifest,
        assignment=assignment,
        artifact_instance_id="7005183651",
        observed_writer_id=assignment["trusted-writer-id"],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    observation["observed-writer-id"] = ci_validation_writer_id(
        workflow="CI Validation",
        job="different-job",
        matrix={"selector": WORK_GROUP_ID},
    )

    with pytest.raises(ContractValidationError, match="trusted writer"):
        validate_ci_validation_writer_observation(
            observation,
            plan=snapshot.plan,
            selector_assignments_manifest=manifest,
            assignment=assignment,
            expected_artifact_instance_id="7005183651",
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_writer_observation_rejects_artifact_instance_mismatch() -> None:
    """Observation must bind the exact already-uploaded receipt artifact."""
    snapshot, manifest, assignment = _writer_context()
    observation = freeze_ci_validation_writer_observation(
        plan=snapshot.plan,
        selector_assignments_manifest=manifest,
        assignment=assignment,
        artifact_instance_id="7005183651",
        observed_writer_id=assignment["trusted-writer-id"],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    with pytest.raises(ContractValidationError, match="artifact instance"):
        validate_ci_validation_writer_observation(
            observation,
            plan=snapshot.plan,
            selector_assignments_manifest=manifest,
            assignment=assignment,
            expected_artifact_instance_id="7005183492",
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_writer_observation_rejects_missing_expected_artifact_instance() -> (
    None
):
    """Payload-only writer-observation validation is not admission-safe."""
    snapshot, manifest, assignment = _writer_context()
    observation = freeze_ci_validation_writer_observation(
        plan=snapshot.plan,
        selector_assignments_manifest=manifest,
        assignment=assignment,
        artifact_instance_id="7005183651",
        observed_writer_id=assignment["trusted-writer-id"],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    with pytest.raises(
        ContractValidationError,
        match="expected-artifact-instance-id",
    ):
        validate_ci_validation_writer_observation(
            observation,
            plan=snapshot.plan,
            selector_assignments_manifest=manifest,
            assignment=assignment,
            expected_artifact_instance_id=cast("str", None),
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_writer_observation_rejects_extra_payload_fields() -> None:
    """Keep the writer-observation schema closed."""
    snapshot, manifest, assignment = _writer_context()
    observation = freeze_ci_validation_writer_observation(
        plan=snapshot.plan,
        selector_assignments_manifest=manifest,
        assignment=assignment,
        artifact_instance_id="7005183651",
        observed_writer_id=assignment["trusted-writer-id"],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    observation["payload-writer-id"] = assignment["trusted-writer-id"]

    with pytest.raises(ContractValidationError, match="not allowed"):
        validate_ci_validation_writer_observation(
            observation,
            plan=snapshot.plan,
            selector_assignments_manifest=manifest,
            assignment=assignment,
            expected_artifact_instance_id="7005183651",
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_selector_manifest_exact_one_admission_uses_contract_ref() -> None:
    """Existing artifact admission enforces exact-one manifest instances."""
    logical_ref = ci_validation_selector_assignments_artifact_ref(
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
    )
    groups = collect_artifacts_by_name(
        [
            GitHubActionsArtifactMetadata(
                artifact_id=7001,
                name=artifact_physical_name(logical_ref),
                created_at=CREATED_AT,
                expired=False,
            ),
        ],
    )

    admission = admit_ci_validation_selector_assignments_artifact(
        groups,
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        producer_authority=CiValidationArtifactProducerAuthority(
            artifact_id=7001,
            boundary="materialize-work-groups",
            verified=True,
        ),
    )

    assert admission.logical_ref == logical_ref


def test_selector_manifest_admission_rejects_unverified_producer() -> None:
    """Selector-assignment authority requires non-payload boundary proof."""
    logical_ref = ci_validation_selector_assignments_artifact_ref(
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
    )
    groups = collect_artifacts_by_name(
        [
            GitHubActionsArtifactMetadata(
                artifact_id=7001,
                name=artifact_physical_name(logical_ref),
                created_at=CREATED_AT,
                expired=False,
            ),
        ],
    )

    with pytest.raises(ContractValidationError, match="non-payload"):
        admit_ci_validation_selector_assignments_artifact(
            groups,
            run_id=RUN_ID,
            run_attempt=RUN_ATTEMPT,
            producer_authority=CiValidationArtifactProducerAuthority(
                artifact_id=7001,
                boundary="materialize-work-groups",
                verified=False,
            ),
        )


def test_writer_observation_exact_one_admission_uses_assignment_ref() -> None:
    """Observation artifacts are admitted only at assignment-derived refs."""
    manifest = _manifest()
    assignments = manifest["assignments"]
    assert isinstance(assignments, list)
    assignment = assignments[0]
    logical_ref = assignment["writer-observation-ref"]
    assert isinstance(logical_ref, str)
    groups = collect_artifacts_by_name(
        [
            GitHubActionsArtifactMetadata(
                artifact_id=7002,
                name=artifact_physical_name(logical_ref),
                created_at=CREATED_AT,
                expired=False,
            ),
        ],
    )

    admission = admit_ci_validation_writer_observation_artifact(
        groups,
        assignment=assignment,
        producer_authority=CiValidationArtifactProducerAuthority(
            artifact_id=7002,
            boundary="trusted-observation-boundary",
            verified=True,
        ),
    )

    assert admission.logical_ref == logical_ref


def test_writer_observation_admission_rejects_unverified_producer() -> None:
    """Writer-observation authority requires observation-boundary proof."""
    manifest = _manifest()
    assignments = manifest["assignments"]
    assert isinstance(assignments, list)
    assignment = assignments[0]
    logical_ref = assignment["writer-observation-ref"]
    assert isinstance(logical_ref, str)
    groups = collect_artifacts_by_name(
        [
            GitHubActionsArtifactMetadata(
                artifact_id=7002,
                name=artifact_physical_name(logical_ref),
                created_at=CREATED_AT,
                expired=False,
            ),
        ],
    )

    with pytest.raises(ContractValidationError, match="non-payload"):
        admit_ci_validation_writer_observation_artifact(
            groups,
            assignment=assignment,
            producer_authority=CiValidationArtifactProducerAuthority(
                artifact_id=7002,
                boundary="trusted-observation-boundary",
                verified=False,
            ),
        )


def test_admission_rejects_authority_for_different_artifact_instance() -> None:
    """Producer verification is bound to the admitted artifact instance."""
    logical_ref = ci_validation_selector_assignments_artifact_ref(
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
    )
    groups = collect_artifacts_by_name(
        [
            GitHubActionsArtifactMetadata(
                artifact_id=7001,
                name=artifact_physical_name(logical_ref),
                created_at=CREATED_AT,
                expired=False,
            ),
        ],
    )

    with pytest.raises(ContractValidationError, match="artifact instance"):
        admit_ci_validation_selector_assignments_artifact(
            groups,
            run_id=RUN_ID,
            run_attempt=RUN_ATTEMPT,
            producer_authority=CiValidationArtifactProducerAuthority(
                artifact_id=7002,
                boundary="materialize-work-groups",
                verified=True,
            ),
        )


def test_admission_rejects_wrong_producer_boundary() -> None:
    """Verified producer authority must match the required logical boundary."""
    logical_ref = ci_validation_selector_assignments_artifact_ref(
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
    )
    groups = collect_artifacts_by_name(
        [
            GitHubActionsArtifactMetadata(
                artifact_id=7001,
                name=artifact_physical_name(logical_ref),
                created_at=CREATED_AT,
                expired=False,
            ),
        ],
    )

    with pytest.raises(
        ContractValidationError, match="materialize-work-groups"
    ):
        admit_ci_validation_selector_assignments_artifact(
            groups,
            run_id=RUN_ID,
            run_attempt=RUN_ATTEMPT,
            producer_authority=CiValidationArtifactProducerAuthority(
                artifact_id=7001,
                boundary="trusted-observation-boundary",
                verified=True,
            ),
        )


def test_validator_rejects_plan_digest_mismatch() -> None:
    """Selector materialization cannot bind to a mutable plan projection."""
    snapshot = _valid_snapshot()
    plan = snapshot.plan
    manifest = _manifest()
    mutated_plan = cast("dict[str, object]", deepcopy(plan))
    mutated_plan["plan-digest"] = "1" * 64

    with pytest.raises(ContractValidationError, match="does not match plan"):
        validate_ci_validation_selector_assignments(
            manifest,
            plan=mutated_plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )
