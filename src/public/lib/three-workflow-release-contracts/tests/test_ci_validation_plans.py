"""CI validation planner core and snapshot helper tests."""

from __future__ import annotations

from copy import deepcopy
from typing import TYPE_CHECKING, Any, cast

import pytest
from three_workflow_release_contracts import (
    API_VERSIONS_BY_KIND,
    PLANNED_CAPABILITY_ORDER,
    CiValidationKind,
    ContractValidationError,
    DiagnosticDetail,
    DiagnosticFamily,
    DiagnosticSeverity,
    DiagnosticVerdictEffect,
    canonical_json_digest,
    ci_validation_changed_files_hash,
    ci_validation_changed_files_snapshot_artifact_ref,
    ci_validation_diagnostic,
    ci_validation_fact_snapshot_artifact_ref,
    ci_validation_fact_snapshot_id,
    ci_validation_plan_digest,
    ci_validation_request_artifact_ref,
    ci_validation_request_projection,
    ci_validation_subject_universe_id,
    freeze_ci_validation_plan,
    normalize_ci_validation_request,
    plan_from_request_normalization,
    validate_ci_validation_plan,
)

if TYPE_CHECKING:
    from collections.abc import Callable

RUN_ID = "25887422010"
RUN_ATTEMPT = "1"
CREATED_AT = "2026-05-14T21:09:21Z"
TREE_SHA = "b" * 40
PLAN_ID = "plan-25887422010-1"
TOOLING_SURFACE_IDS = (
    "authoring-validation",
    "build-execution",
    "classifier",
    "descriptor-contract",
    "descriptor-schema-documentation",
    "fact-provider",
    "planner",
    "publish-execution",
    "smoke-validation",
    "target-catalog",
    "workflow-orchestration",
    "workflow-release-contract",
)


def _request(*, changed_files: list[str] | None = None) -> dict[str, object]:
    if changed_files is None:
        changed_files = ["src/public/lib/example.py", "tests/example_test.py"]
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


def _normalized_non_head_pull_request():
    document = _request()
    validation_tree = cast("dict[str, object]", document["validation-tree"])
    validation_tree["commit-sha"] = "d" * 40
    document["request-digest"] = canonical_json_digest(
        ci_validation_request_projection(document),
    )
    result = normalize_ci_validation_request(
        document,
        expected_run_id=RUN_ID,
        expected_run_attempt=RUN_ATTEMPT,
    )
    assert result.request is not None
    return result.request


def _normalized_unavailable_pull_request():
    document = _request()
    document["affected-range"] = {
        "status": "unavailable",
        "base-sha": "a" * 40,
        "base-tip-sha": "c" * 40,
        "head-sha": TREE_SHA,
        "changed-files": None,
        "source": "pull_request",
        "diagnostic": DiagnosticFamily.RANGE_UNCONFIRMED.value,
        "diagnostic-detail": DiagnosticDetail.MISSING.value,
    }
    document["request-digest"] = canonical_json_digest(
        ci_validation_request_projection(document),
    )
    result = normalize_ci_validation_request(
        document,
        expected_run_id=RUN_ID,
        expected_run_attempt=RUN_ATTEMPT,
    )
    assert result.request is not None
    return result.request


def _pull_request_merge_commit_verification(  # noqa: PLR0913
    *,
    commit_sha: str = "d" * 40,
    base_tip_sha: str = "c" * 40,
    head_sha: str = TREE_SHA,
    ref: str = "refs/pull/42/merge",
    verified: bool = True,
    verification_source: str = "github-control-plane",
) -> dict[str, object]:
    return {
        "commit-sha": commit_sha,
        "base-tip-sha": base_tip_sha,
        "head-sha": head_sha,
        "ref": ref,
        "verified": verified,
        "verification-source": verification_source,
    }


def _push_request(
    *,
    changed_files: list[str] | None = None,
) -> dict[str, object]:
    document = _request(changed_files=changed_files)
    document["mode"] = "push"
    document["validation-tree"] = {
        "commit-sha": TREE_SHA,
        "ref": "refs/heads/main",
    }
    document["event"] = {
        "name": "push",
        "number": None,
        "actor": "octocat",
        "run-id": RUN_ID,
        "run-attempt": RUN_ATTEMPT,
    }
    affected_range = cast("dict[str, object]", document["affected-range"])
    affected_range["base-tip-sha"] = None
    affected_range["source"] = "push"
    document["request-digest"] = canonical_json_digest(
        ci_validation_request_projection(document),
    )
    return document


def _normalized_push_request(*, changed_files: list[str] | None = None):
    result = normalize_ci_validation_request(
        _push_request(changed_files=changed_files),
        expected_run_id=RUN_ID,
        expected_run_attempt=RUN_ATTEMPT,
    )
    assert result.request is not None
    return result.request


def _scheduled_full_request() -> dict[str, object]:
    document = _request()
    document["mode"] = "scheduled_full"
    document["event"] = {
        "name": "schedule",
        "number": None,
        "actor": "github-actions[bot]",
        "run-id": RUN_ID,
        "run-attempt": RUN_ATTEMPT,
    }
    document.pop("affected-range")
    document["scheduled-full"] = {"enabled": True}
    document["request-digest"] = canonical_json_digest(
        ci_validation_request_projection(document),
    )
    return document


def _normalized_scheduled_full_request():
    result = normalize_ci_validation_request(
        _scheduled_full_request(),
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


def _classification_with_second_subject() -> dict[str, object]:
    classification = _classification()
    impacts = cast("list[dict[str, object]]", classification["impacts"])
    impacts[0]["matched-paths"] = ["src/public/lib/example.py"]
    impacts.append(
        {
            "impact-id": "impact-example-tools",
            "category": "project-scoped",
            "matched-paths": [
                "tests/example_test.py",
            ],
            "source-rule": "python-workspace-path",
            "rationale": "Changed files belong to the example tools subject.",
            "coverage-target": {
                "type": "subject",
                "id": "python.src-public-lib-example-tools",
            },
            "requires": {
                "descriptor-validation": False,
                "downstream-expansion": False,
                "broad-expansion": False,
                "diagnostic": None,
            },
        },
    )
    provenance = cast(
        "list[dict[str, object]]",
        classification["subject-selection-provenance"],
    )
    provenance.append(
        {
            "provenance-id": "prov-example-tools",
            "subject-id": "python.src-public-lib-example-tools",
            "selection-kind": "direct",
            "source-impact-ids": ["impact-example-tools"],
            "direct-subject-id": None,
            "dependency-edge-basis": [],
            "broad-expansion-id": None,
            "scheduled-full-source": False,
        },
    )
    return classification


def _classification_with_second_impact_for_same_subject() -> dict[str, object]:
    classification = _classification()
    impacts = cast("list[dict[str, object]]", classification["impacts"])
    impacts[0]["matched-paths"] = ["src/public/lib/example.py"]
    impacts.append(
        {
            "impact-id": "impact-example-tests",
            "category": "project-scoped",
            "matched-paths": [
                "tests/example_test.py",
            ],
            "source-rule": "python-workspace-path",
            "rationale": "Changed tests belong to the example subject.",
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
    )
    provenance = cast(
        "list[dict[str, object]]",
        classification["subject-selection-provenance"],
    )
    provenance[0]["source-impact-ids"] = [
        "impact-example",
        "impact-example-tests",
    ]
    return classification


def _scheduled_full_classification() -> dict[str, object]:
    classification = _classification()
    classification["impacts"] = []
    classification["subject-selection-provenance"] = [
        {
            "provenance-id": "prov-example",
            "subject-id": "python.src-public-lib-example",
            "selection-kind": "scheduled-full",
            "source-impact-ids": [],
            "direct-subject-id": None,
            "dependency-edge-basis": [],
            "broad-expansion-id": None,
            "scheduled-full-source": True,
        },
    ]
    return classification


def _lightweight_classification() -> dict[str, object]:
    classification = _classification()
    impacts = cast("list[dict[str, object]]", classification["impacts"])
    impacts[0]["category"] = "known-non-impacting"
    impacts[0]["coverage-target"] = {"type": "none", "id": None}
    classification["subject-selection-provenance"] = []
    classification["lightweight-only"] = True
    return classification


def _workflow_release_infrastructure_classification(
    *,
    surface: str = "authoring-validation",
    descriptors: str = "none",
) -> dict[str, object]:
    classification = _classification()
    impacts = cast("list[dict[str, object]]", classification["impacts"])
    impacts[0]["category"] = "workflow-release-infrastructure"
    impacts[0]["coverage-target"] = _tooling_coverage_target(surface)
    impacts[0]["requires"] = {
        "descriptor-validation": False,
        "downstream-expansion": False,
        "broad-expansion": True,
        "diagnostic": None,
    }
    classification["broad-expansions"] = [
        {
            "expansion-id": f"expansion-{surface}",
            "source-impact-id": "impact-example",
            "category": "workflow-release-infrastructure",
            "reason": f"Expand affected {surface} workflow-release surface.",
            "resulting-scope": {
                "ecosystems": ["python"],
                "subjects": ["python.src-public-lib-example"],
                "descriptors": descriptors,
            },
        },
    ]
    return classification


def _zero_file_lightweight_classification() -> dict[str, object]:
    classification = _lightweight_classification()
    classification["impacts"] = []
    return classification


def _empty_fact_provider() -> dict[str, object]:
    provider = _fact_provider()
    provider["roots"] = []
    provider["subjects"] = []
    return provider


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


def _fact_provider_with_second_subject() -> dict[str, object]:
    provider = _fact_provider()
    roots = cast("list[str]", provider["roots"])
    subjects = cast("list[str]", provider["subjects"])
    roots.append("src/public/lib/example-tools")
    subjects.append("python.src-public-lib-example-tools")
    return provider


def _descriptor_fact_provider() -> dict[str, object]:
    provider = _fact_provider()
    provider["descriptors"] = [
        {
            "descriptor-path": "src/public/lib/example/three-release.json",
            "descriptor-identity": "example",
            "owner-subject-id": "python.src-public-lib-example",
            "source": "ecosystem-provider",
        },
    ]
    provider["target-catalog"] = {
        "catalog-id": "catalog-python-example",
        "descriptor-paths": ["src/public/lib/example/three-release.json"],
        "entries": [
            {
                "descriptor-path": "src/public/lib/example/three-release.json",
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


def _multi_profile_descriptor_fact_provider() -> dict[str, object]:
    provider = _descriptor_fact_provider()
    catalog = cast("dict[str, object]", provider["target-catalog"])
    entries = cast("list[dict[str, object]]", catalog["entries"])
    zip_entry = deepcopy(entries[0])
    zip_entry["profile"] = "zip"
    entries.append(zip_entry)
    return provider


def _workflow_release_descriptor_provider() -> dict[str, object]:
    return {
        "provider": "workflow-release",
        "provider-version": "workflow-release/v1",
        "status": "available",
        "roots": [],
        "subjects": [],
        "dependency-edges": [],
        "tooling-surfaces": list(TOOLING_SURFACE_IDS),
        "descriptors": [
            {
                "descriptor-path": "docs/workflow-release/three-release.json",
                "descriptor-identity": "workflow-release",
                "owner-subject-id": None,
                "source": "workflow-release-provider",
            },
        ],
        "target-catalog": {
            "catalog-id": None,
            "descriptor-paths": [],
            "entries": [],
        },
        "diagnostics": [],
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


def _unsupported_subject(
    *,
    subject_id: str = "ruby.unsupported-example",
    ecosystem: str = "ruby",
) -> dict[str, object]:
    subject = _subject()
    subject["subject-id"] = subject_id
    subject["ecosystem"] = ecosystem
    subject["root"] = "src/unsupported/example"
    subject["activity-status"] = "inactive"
    subject["selection-status"] = "not-selected"
    subject["capability-class"] = "validation-only"
    subject["descriptor"] = {"path": None, "identity": None}
    subject["capabilities"] = {
        "build": False,
        "test": False,
        "lint": False,
        "format": False,
        "type-check": False,
        "release-shaped-artifacts": False,
    }
    subject["inclusion"] = {
        "source": "workspace",
        "reason": "unsupported workspace",
    }
    subject["exclusion"] = {"reason": "unsupported-ecosystem"}
    return subject


def _second_subject() -> dict[str, object]:
    subject = _subject()
    subject["subject-id"] = "python.src-public-lib-example-tools"
    subject["root"] = "src/public/lib/example-tools"
    return subject


def _second_ecosystem_gate_work_group() -> dict[str, object]:
    group = _ecosystem_gate_work_group()
    group["work-group-id"] = "wg-python-tools-gate"
    group["coverage-target"] = {
        "type": "subject",
        "id": "python.src-public-lib-example-tools",
    }
    return group


def _second_validation_obligation() -> dict[str, object]:
    obligation = _validation_obligation()
    obligation["validation-obligation-id"] = "validation-python-tools-gate"
    obligation["coverage-target"] = {
        "type": "subject",
        "id": "python.src-public-lib-example-tools",
    }
    obligation["work-group-id"] = "wg-python-tools-gate"
    obligation["expected-evidence-id"] = "evidence-python-tools-gate"
    return obligation


def _second_evidence_expectation() -> dict[str, object]:
    evidence = _evidence_expectation()
    evidence["evidence-expectation-id"] = "evidence-python-tools-gate"
    evidence["work-group-id"] = "wg-python-tools-gate"
    evidence["coverage-target"] = {
        "type": "subject",
        "id": "python.src-public-lib-example-tools",
    }
    return evidence


def _third_subject() -> dict[str, object]:
    subject = _subject()
    subject["subject-id"] = "python.src-public-lib-example-tools-extra"
    subject["root"] = "src/public/lib/example-tools-extra"
    return subject


def _third_ecosystem_gate_work_group() -> dict[str, object]:
    group = _ecosystem_gate_work_group()
    group["work-group-id"] = "wg-python-tools-extra-gate"
    group["coverage-target"] = {
        "type": "subject",
        "id": "python.src-public-lib-example-tools-extra",
    }
    return group


def _third_validation_obligation() -> dict[str, object]:
    obligation = _validation_obligation()
    obligation["validation-obligation-id"] = (
        "validation-python-tools-extra-gate"
    )
    obligation["coverage-target"] = {
        "type": "subject",
        "id": "python.src-public-lib-example-tools-extra",
    }
    obligation["work-group-id"] = "wg-python-tools-extra-gate"
    obligation["expected-evidence-id"] = "evidence-python-tools-extra-gate"
    return obligation


def _third_evidence_expectation() -> dict[str, object]:
    evidence = _evidence_expectation()
    evidence["evidence-expectation-id"] = "evidence-python-tools-extra-gate"
    evidence["work-group-id"] = "wg-python-tools-extra-gate"
    evidence["coverage-target"] = {
        "type": "subject",
        "id": "python.src-public-lib-example-tools-extra",
    }
    return evidence


def _descriptor_backed_subject() -> dict[str, object]:
    subject = _subject()
    subject["capability-class"] = "descriptor-backed"
    subject["descriptor"] = {
        "path": "src/public/lib/example/three-release.json",
        "identity": "python.src-public-lib-example",
    }
    capabilities = cast("dict[str, bool]", subject["capabilities"])
    capabilities["release-shaped-artifacts"] = True
    return subject


def _make_subject_inactive(subject: dict[str, object]) -> None:
    subject["activity-status"] = "inactive"
    subject["selection-status"] = "not-selected"


def _make_subject_not_selected(subject: dict[str, object]) -> None:
    subject["selection-status"] = "not-selected"


def _make_subject_validation_only(subject: dict[str, object]) -> None:
    subject["capability-class"] = "validation-only"
    capabilities = cast("dict[str, bool]", subject["capabilities"])
    capabilities["release-shaped-artifacts"] = False


def _make_subject_descriptor_path_mismatch(
    subject: dict[str, object],
) -> None:
    descriptor = cast("dict[str, object]", subject["descriptor"])
    descriptor["path"] = "src/public/lib/example/other-release.json"


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


def _scheduled_full_validation_obligation() -> dict[str, object]:
    obligation = _validation_obligation()
    obligation["source-impact-ids"] = []
    return obligation


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


def _descriptor_work_group() -> dict[str, object]:
    descriptor_path = "src/public/lib/example/three-release.json"
    return {
        "work-group-id": "wg-descriptor",
        "kind": "descriptor-validation",
        "coverage-target": {"type": "descriptor", "id": descriptor_path},
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
    descriptor_path = "src/public/lib/example/three-release.json"
    return {
        "evidence-expectation-id": "evidence-descriptor",
        "work-group-id": "wg-descriptor",
        "coverage-target": {"type": "descriptor", "id": descriptor_path},
        "category": "descriptor-validation",
        "planned-capabilities": None,
        "detail-profile": None,
        "required": True,
        "blocking-if-missing": True,
    }


def _descriptor_obligation() -> dict[str, object]:
    descriptor_path = "src/public/lib/example/three-release.json"
    return {
        "descriptor-obligation-id": "descriptor-example",
        "source-impact-ids": ["impact-example"],
        "descriptor-scope": "selected",
        "coverage-target": {"type": "descriptor", "id": descriptor_path},
        "required": True,
        "blocking": True,
        "work-group-id": "wg-descriptor",
        "expected-evidence-id": "evidence-descriptor",
    }


def _workflow_release_descriptor_path() -> str:
    return "docs/workflow-release/three-release.json"


def _workflow_release_descriptor_work_group() -> dict[str, object]:
    descriptor_path = _workflow_release_descriptor_path()
    return {
        **_descriptor_work_group(),
        "work-group-id": "wg-descriptor-workflow-release",
        "coverage-target": {"type": "descriptor", "id": descriptor_path},
    }


def _workflow_release_descriptor_evidence_expectation() -> dict[str, object]:
    descriptor_path = _workflow_release_descriptor_path()
    return {
        **_descriptor_evidence_expectation(),
        "evidence-expectation-id": "evidence-descriptor-workflow-release",
        "work-group-id": "wg-descriptor-workflow-release",
        "coverage-target": {"type": "descriptor", "id": descriptor_path},
    }


def _workflow_release_descriptor_obligation(
    *,
    source_impact_ids: list[str] | None = None,
) -> dict[str, object]:
    descriptor_path = _workflow_release_descriptor_path()
    return {
        **_descriptor_obligation(),
        "descriptor-obligation-id": "descriptor-workflow-release",
        "source-impact-ids": source_impact_ids if source_impact_ids else [],
        "descriptor-scope": "all-discovered",
        "coverage-target": {"type": "descriptor", "id": descriptor_path},
        "work-group-id": "wg-descriptor-workflow-release",
        "expected-evidence-id": "evidence-descriptor-workflow-release",
    }


def _artifact_work_group() -> dict[str, object]:
    return {
        "work-group-id": "wg-artifact",
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


def _artifact_evidence_expectation() -> dict[str, object]:
    return {
        "evidence-expectation-id": "evidence-artifact",
        "work-group-id": "wg-artifact",
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
        "work-group-id": "wg-artifact",
        "expected-evidence-id": "evidence-artifact",
    }


def _artifact_obligation() -> dict[str, object]:
    return {
        "artifact-obligation-id": "artifact-example",
        "source-impact-ids": ["impact-example"],
        "subject-id": "python.src-public-lib-example",
        "descriptor-path": "src/public/lib/example/three-release.json",
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
        "work-group-id": "wg-artifact",
        "expected-evidence-id": "evidence-artifact",
    }


def _tooling_coverage_target(
    surface: str = "authoring-validation",
) -> dict[str, object]:
    return {"type": "tooling-surface", "id": surface}


def _tooling_detail_profile(
    *,
    profile_id: str = "profile-tooling",
    category: str = "workflow-release-tooling",
    coverage_target: dict[str, object] | None = None,
    surface: str = "authoring-validation",
) -> dict[str, object]:
    return {
        "detail-profile-id": profile_id,
        "category": category,
        "coverage-target": coverage_target or _tooling_coverage_target(surface),
        "required-subchecks": [
            {
                "subcheck-id": "contract",
                "check-kind": "contract",
                "blocking": True,
                "description": "Verify the workflow-release tooling contract.",
            },
        ],
    }


def _tooling_work_group(
    *,
    surface: str = "authoring-validation",
    work_group_id: str = "wg-tooling",
    profile_id: str | None = "profile-tooling",
) -> dict[str, object]:
    return {
        "work-group-id": work_group_id,
        "kind": "workflow-release-tooling",
        "coverage-target": _tooling_coverage_target(surface),
        "ecosystem": None,
        "runner-family": "ubuntu",
        "selector-variant": None,
        "depends-on": [],
        "expected-evidence": {
            "category": "workflow-release-tooling",
            "planned-capabilities": None,
            "detail-profile": profile_id,
            "required": True,
        },
    }


def _tooling_evidence_expectation(
    *,
    surface: str = "authoring-validation",
    evidence_id: str = "evidence-tooling",
    work_group_id: str = "wg-tooling",
    profile_id: str | None = "profile-tooling",
) -> dict[str, object]:
    return {
        "evidence-expectation-id": evidence_id,
        "work-group-id": work_group_id,
        "coverage-target": _tooling_coverage_target(surface),
        "category": "workflow-release-tooling",
        "planned-capabilities": None,
        "detail-profile": profile_id,
        "required": True,
        "blocking-if-missing": True,
    }


def _tooling_validation_obligation(
    *,
    surface: str = "authoring-validation",
    obligation_id: str = "validation-tooling",
    work_group_id: str = "wg-tooling",
    evidence_id: str = "evidence-tooling",
    source_impact_ids: list[str] | None = None,
) -> dict[str, object]:
    return {
        "validation-obligation-id": obligation_id,
        "source-impact-ids": (
            ["impact-example"]
            if source_impact_ids is None
            else source_impact_ids
        ),
        "kind": "workflow-release-tooling",
        "coverage-target": _tooling_coverage_target(surface),
        "required": True,
        "blocking": True,
        "work-group-id": work_group_id,
        "expected-evidence-id": evidence_id,
    }


def _lightweight_coverage_target(
    policy: str = "known-non-impacting",
) -> dict[str, object]:
    return {"type": "lightweight-policy", "id": policy}


def _lightweight_work_group(
    *,
    policy: str = "known-non-impacting",
    profile_id: str | None = "profile-lightweight",
) -> dict[str, object]:
    return {
        "work-group-id": "wg-lightweight",
        "kind": "lightweight-preflight",
        "coverage-target": _lightweight_coverage_target(policy),
        "ecosystem": None,
        "runner-family": "ubuntu",
        "selector-variant": None,
        "depends-on": [],
        "expected-evidence": {
            "category": "lightweight-preflight",
            "planned-capabilities": None,
            "detail-profile": profile_id,
            "required": True,
        },
    }


def _lightweight_evidence_expectation(
    *,
    policy: str = "known-non-impacting",
    profile_id: str | None = "profile-lightweight",
) -> dict[str, object]:
    return {
        "evidence-expectation-id": "evidence-lightweight",
        "work-group-id": "wg-lightweight",
        "coverage-target": _lightweight_coverage_target(policy),
        "category": "lightweight-preflight",
        "planned-capabilities": None,
        "detail-profile": profile_id,
        "required": True,
        "blocking-if-missing": True,
    }


def _lightweight_validation_obligation(
    *,
    policy: str = "known-non-impacting",
) -> dict[str, object]:
    return {
        "validation-obligation-id": "validation-lightweight",
        "source-impact-ids": ["impact-example"],
        "kind": "lightweight-preflight",
        "coverage-target": _lightweight_coverage_target(policy),
        "required": True,
        "blocking": True,
        "work-group-id": "wg-lightweight",
        "expected-evidence-id": "evidence-lightweight",
    }


def _scheduled_full_tooling_records() -> tuple[
    list[dict[str, object]],
    list[dict[str, object]],
    list[dict[str, object]],
    list[dict[str, object]],
]:
    work_groups: list[dict[str, object]] = []
    evidence: list[dict[str, object]] = []
    obligations: list[dict[str, object]] = []
    profiles: list[dict[str, object]] = []
    for surface in TOOLING_SURFACE_IDS:
        suffix = surface.replace("-", "_")
        work_group_id = f"wg-tooling-{surface}"
        evidence_id = f"evidence-tooling-{surface}"
        profile_id = f"profile-tooling-{surface}"
        work_groups.append(
            _tooling_work_group(
                surface=surface,
                work_group_id=work_group_id,
                profile_id=profile_id,
            ),
        )
        evidence.append(
            _tooling_evidence_expectation(
                surface=surface,
                evidence_id=evidence_id,
                work_group_id=work_group_id,
                profile_id=profile_id,
            ),
        )
        obligations.append(
            _tooling_validation_obligation(
                surface=surface,
                obligation_id=f"validation-tooling-{suffix}",
                work_group_id=work_group_id,
                evidence_id=evidence_id,
                source_impact_ids=[],
            ),
        )
        profiles.append(
            _tooling_detail_profile(
                profile_id=profile_id,
                surface=surface,
            ),
        )
    return work_groups, evidence, obligations, profiles


def _plan_snapshot():
    return freeze_ci_validation_plan(
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


def _push_plan_snapshot():
    return freeze_ci_validation_plan(
        request=_normalized_push_request(),
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


def _scheduled_full_plan_snapshot():
    (
        tooling_work_groups,
        tooling_evidence,
        tooling_obligations,
        tooling_profiles,
    ) = _scheduled_full_tooling_records()
    return freeze_ci_validation_plan(
        request=_normalized_scheduled_full_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_scheduled_full_classification(),
        subjects=[_subject()],
        validation_obligations=[
            _scheduled_full_validation_obligation(),
            *tooling_obligations,
        ],
        descriptor_obligations=[_workflow_release_descriptor_obligation()],
        work_groups=[
            _workflow_release_descriptor_work_group(),
            _ecosystem_gate_work_group(),
            *tooling_work_groups,
        ],
        evidence_expectations=[
            _workflow_release_descriptor_evidence_expectation(),
            _evidence_expectation(),
            *tooling_evidence,
        ],
        detail_profiles=tooling_profiles,
        fact_snapshot_providers=[
            _fact_provider(),
            _workflow_release_descriptor_provider(),
        ],
    )


def _global_full_scope_plan_snapshot():
    (
        tooling_work_groups,
        tooling_evidence,
        tooling_obligations,
        tooling_profiles,
    ) = _scheduled_full_tooling_records()
    for obligation in tooling_obligations:
        obligation["source-impact-ids"] = ["impact-example"]
    classification = _classification()
    impacts = cast("list[dict[str, object]]", classification["impacts"])
    impacts[0]["category"] = "global"
    impacts[0]["coverage-target"] = {"type": "global", "id": None}
    return freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=classification,
        subjects=[_subject()],
        validation_obligations=[
            _validation_obligation(),
            *tooling_obligations,
        ],
        descriptor_obligations=[
            _workflow_release_descriptor_obligation(
                source_impact_ids=["impact-example"],
            ),
        ],
        work_groups=[
            _workflow_release_descriptor_work_group(),
            _ecosystem_gate_work_group(),
            *tooling_work_groups,
        ],
        evidence_expectations=[
            _workflow_release_descriptor_evidence_expectation(),
            _evidence_expectation(),
            *tooling_evidence,
        ],
        detail_profiles=tooling_profiles,
        fact_snapshot_providers=[
            _fact_provider(),
            _workflow_release_descriptor_provider(),
        ],
    )


def _redigest_plan(plan: dict[str, object]) -> dict[str, object]:
    plan["plan-digest"] = ci_validation_plan_digest(plan)
    return plan


def _set_bad_impact_diagnostic(impact: dict[str, object]) -> None:
    requires = cast("dict[str, object]", impact["requires"])
    requires["diagnostic"] = "not-a-diagnostic-code"


def _set_invalid_mode(plan: dict[str, object]) -> None:
    plan["mode"] = "workflow_dispatch"


def _set_bad_scheduled_full(plan: dict[str, object]) -> None:
    scheduled_full = cast("dict[str, object]", plan["scheduled-full"])
    scheduled_full["enabled"] = True


def _set_bad_affected_range(plan: dict[str, object]) -> None:
    affected_range = cast("dict[str, object]", plan["affected-range"])
    affected_range["changed-files-hash"] = None


def _set_bad_execution_tree(plan: dict[str, object]) -> None:
    planner = cast("dict[str, object]", plan["planner"])
    execution_tree = cast("dict[str, object]", planner["execution-tree"])
    execution_tree["observed-commit-sha"] = "c" * 40


def _set_bad_subject_universe(plan: dict[str, object]) -> None:
    subject_universe = cast("dict[str, object]", plan["subject-universe"])
    subject_universe["id"] = "0" * 64


def _set_bad_fact_snapshot(plan: dict[str, object]) -> None:
    fact_snapshot = cast("dict[str, object]", plan["fact-snapshot"])
    fact_snapshot["status"] = "unavailable"


def _remove_diagnostic_message(diagnostic: dict[str, object]) -> None:
    diagnostic.pop("message")


def _empty_diagnostic_message(diagnostic: dict[str, object]) -> None:
    diagnostic["message"] = ""


def _remove_diagnostic_source(diagnostic: dict[str, object]) -> None:
    diagnostic.pop("source")


def _set_bad_diagnostic_source_type(diagnostic: dict[str, object]) -> None:
    diagnostic["source"] = {"type": "bad", "id": None}


def _valid_fail_closed_diagnostic() -> dict[str, object]:
    return ci_validation_diagnostic(
        diagnostic_id="range-unconfirmed/001",
        code=DiagnosticFamily.RANGE_UNCONFIRMED.value,
        detail=DiagnosticDetail.MISSING.value,
        message="affected range was not confirmed",
        source_type="request",
        source_id=None,
        severity=DiagnosticSeverity.FAIL_CLOSED.value,
        verdict_effect=DiagnosticVerdictEffect.FAIL_CLOSED.value,
    )


def _unrelated_fail_closed_diagnostic() -> dict[str, object]:
    return ci_validation_diagnostic(
        diagnostic_id="invalid-plan/001",
        code=DiagnosticFamily.INVALID_PLAN.value,
        detail=DiagnosticDetail.PLAN_MISSING.value,
        message="plan was not available",
        source_type="request",
        source_id=None,
        severity=DiagnosticSeverity.FAIL_CLOSED.value,
        verdict_effect=DiagnosticVerdictEffect.FAIL_CLOSED.value,
    )


def test_plan_digest_is_deterministic_and_excludes_plan_digest() -> None:
    """Freeze equivalent inputs to a stable Group 1 canonical digest."""
    first = _plan_snapshot()
    second = _plan_snapshot()

    assert first.plan["plan-digest"] == second.plan["plan-digest"]
    assert first.plan["plan-digest"] == ci_validation_plan_digest(first.plan)
    changed = dict(first.plan)
    changed["plan-digest"] = "f" * 64
    assert ci_validation_plan_digest(changed) == first.plan["plan-digest"]


@pytest.mark.parametrize(
    "mutation",
    [
        lambda plan: plan.__setitem__(1, "non-string-key"),
        lambda plan: cast("dict[str, object]", plan["planner"]).__setitem__(
            "version",
            1.5,
        ),
    ],
)
def test_validate_plan_reports_non_ijson_digest_content(
    mutation: Callable[[dict[object, object]], None],
) -> None:
    """Digest recomputation reports non-I-JSON plan content as invalid."""
    snapshot = _plan_snapshot()
    plan = cast("dict[object, object]", deepcopy(snapshot.plan))
    mutation(plan)

    with pytest.raises(
        ContractValidationError,
        match="cannot canonicalize plan",
    ):
        validate_ci_validation_plan(
            cast("dict[str, object]", plan),
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_validate_plan_reports_non_ijson_subject_universe_content() -> None:
    """Subject-universe digest recomputation fails closed on bad subjects."""
    snapshot = _plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    subjects = cast("list[dict[str, object]]", plan["subjects"])
    subjects[0]["unexpected-float"] = 1.5

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )

    messages = [
        f"{issue.path}: {issue.message}" for issue in error.value.issues
    ]
    assert any("cannot canonicalize plan" in message for message in messages)
    assert any(
        message == "subjects[0]: must only contain registered subject members"
        for message in messages
    )


def test_validate_plan_rejects_unknown_plan_root_member() -> None:
    """Plan root schema is closed to unknown members."""
    snapshot = _plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    plan["unknown-root-member"] = True
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )

    assert any(
        issue.path == "$.unknown-root-member"
        and issue.message == "is not allowed"
        for issue in error.value.issues
    )


def test_plan_contains_required_fields_and_valid_common_envelope() -> None:
    """Expose the LLD plan envelope and all top-level plan sections."""
    snapshot = _plan_snapshot()
    plan = snapshot.plan

    validate_ci_validation_plan(
        plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
        expected_run_id=RUN_ID,
        expected_run_attempt=RUN_ATTEMPT,
    )
    assert (
        plan["api-version"]
        == (API_VERSIONS_BY_KIND[CiValidationKind.PLAN.value])
    )
    assert plan["kind"] == CiValidationKind.PLAN.value
    assert plan["request"] == {
        "artifact-ref": ci_validation_request_artifact_ref(
            run_id=RUN_ID,
            run_attempt=RUN_ATTEMPT,
        ),
        "request-digest": _normalized_request().request_digest,
    }
    for key in (
        "classification",
        "subjects",
        "descriptor-obligations",
        "validation-obligations",
        "artifact-obligations",
        "work-groups",
        "evidence-expectations",
        "detail-profiles",
        "diagnostics",
    ):
        assert key in plan


def test_push_plan_contains_mode_specific_affected_range() -> None:
    """Push plans bind to the pushed head and do not carry PR base tips."""
    snapshot = _push_plan_snapshot()

    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
        expected_run_id=RUN_ID,
        expected_run_attempt=RUN_ATTEMPT,
    )
    assert snapshot.plan["mode"] == "push"
    assert snapshot.plan["validation-tree"] == {
        "commit-sha": TREE_SHA,
        "ref": "refs/heads/main",
    }
    affected_range = cast("dict[str, object]", snapshot.plan["affected-range"])
    assert affected_range["base-tip-sha"] is None
    assert affected_range["head-sha"] == TREE_SHA


def test_pull_request_plan_contains_mode_specific_affected_range() -> None:
    """PR plans bind validation to the PR head and carry the base tip."""
    snapshot = _plan_snapshot()

    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
        expected_run_id=RUN_ID,
        expected_run_attempt=RUN_ATTEMPT,
    )
    affected_range = cast("dict[str, object]", snapshot.plan["affected-range"])
    validation_tree = cast(
        "dict[str, object]",
        snapshot.plan["validation-tree"],
    )
    assert affected_range["base-tip-sha"] == "c" * 40
    assert validation_tree["commit-sha"] == affected_range["head-sha"]


def test_scheduled_full_plan_requires_null_affected_endpoints() -> None:
    """Scheduled-full plans remain the null-endpoint affected-range case."""
    snapshot = _scheduled_full_plan_snapshot()
    assert snapshot.plan["affected-range"] == {
        "status": "not-applicable",
        "base-sha": None,
        "base-tip-sha": None,
        "head-sha": None,
        "changed-files-hash": None,
    }
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    affected_range = cast("dict[str, object]", plan["affected-range"])
    affected_range["head-sha"] = TREE_SHA
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="must be null"):
        validate_ci_validation_plan(
            plan,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_validate_plan_rejects_pull_request_missing_base_tip_sha() -> None:
    """Available PR plans require an explicit base tip boundary."""
    snapshot = _plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    affected_range = cast("dict[str, object]", plan["affected-range"])
    affected_range["base-tip-sha"] = None
    _redigest_plan(plan)

    with pytest.raises(
        ContractValidationError,
        match="is required for pull_request",
    ):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_validate_plan_accepts_pull_request_verified_merge_commit() -> None:
    """PR validation trees may use a verified merge commit for the PR pair."""
    snapshot = _plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    validation_tree = cast("dict[str, object]", plan["validation-tree"])
    validation_tree["commit-sha"] = "d" * 40
    affected_range = cast("dict[str, object]", plan["affected-range"])
    validation_tree["merge-commit"] = {
        "commit-sha": "d" * 40,
        "base-tip-sha": affected_range["base-tip-sha"],
        "head-sha": affected_range["head-sha"],
        "ref": validation_tree["ref"],
        "verified": True,
        "verification-source": "github-control-plane",
    }
    planner = cast("dict[str, object]", plan["planner"])
    execution_tree = cast("dict[str, object]", planner["execution-tree"])
    execution_tree["observed-commit-sha"] = "d" * 40
    _redigest_plan(plan)

    validate_ci_validation_plan(
        plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
        pull_request_merge_commit_verification=(
            _pull_request_merge_commit_verification()
        ),
    )


def test_validate_plan_rejects_pull_request_unverified_merge_commit() -> None:
    """PR merge refs must carry explicit verified pair provenance."""
    snapshot = _plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    validation_tree = cast("dict[str, object]", plan["validation-tree"])
    validation_tree["commit-sha"] = "d" * 40
    planner = cast("dict[str, object]", plan["planner"])
    execution_tree = cast("dict[str, object]", planner["execution-tree"])
    execution_tree["observed-commit-sha"] = "d" * 40
    _redigest_plan(plan)

    with pytest.raises(
        ContractValidationError,
        match="verified merge commit for pull_request",
    ):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_validate_plan_rejects_pr_self_attested_merge_commit() -> None:
    """PR merge commits require trusted provenance."""
    snapshot = _plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    validation_tree = cast("dict[str, object]", plan["validation-tree"])
    validation_tree["commit-sha"] = "d" * 40
    affected_range = cast("dict[str, object]", plan["affected-range"])
    validation_tree["merge-commit"] = {
        "commit-sha": "d" * 40,
        "base-tip-sha": affected_range["base-tip-sha"],
        "head-sha": affected_range["head-sha"],
        "ref": validation_tree["ref"],
        "verified": True,
        "verification-source": "github-control-plane",
    }
    planner = cast("dict[str, object]", plan["planner"])
    execution_tree = cast("dict[str, object]", planner["execution-tree"])
    execution_tree["observed-commit-sha"] = "d" * 40
    _redigest_plan(plan)

    with pytest.raises(
        ContractValidationError,
        match="requires trusted pull-request merge commit verification",
    ):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_validate_plan_rejects_head_bound_pr_merge_commit() -> None:
    """Head-bound PR validation trees must not carry merge-commit claims."""
    snapshot = _plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    validation_tree = cast("dict[str, object]", plan["validation-tree"])
    affected_range = cast("dict[str, object]", plan["affected-range"])
    validation_tree["merge-commit"] = {
        "commit-sha": validation_tree["commit-sha"],
        "base-tip-sha": affected_range["base-tip-sha"],
        "head-sha": affected_range["head-sha"],
        "ref": "refs/pull/42/merge",
        "verified": True,
        "verification-source": "github-control-plane",
    }
    _redigest_plan(plan)

    with pytest.raises(
        ContractValidationError,
        match="must be absent when pull_request validation tree is head-sha",
    ):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_validate_plan_rejects_push_merge_commit() -> None:
    """Push validation trees must not carry pull request merge claims."""
    plan = cast("dict[str, object]", deepcopy(_push_plan_snapshot().plan))
    validation_tree = cast("dict[str, object]", plan["validation-tree"])
    validation_tree["merge-commit"] = _pull_request_merge_commit_verification(
        commit_sha=TREE_SHA,
        ref="refs/heads/main",
    )
    _redigest_plan(plan)

    with pytest.raises(
        ContractValidationError,
        match=r"validation-tree\.merge-commit.*must be absent for push",
    ):
        validate_ci_validation_plan(plan)


def test_validate_plan_rejects_scheduled_full_merge_commit() -> None:
    """Scheduled-full validation trees must not carry PR merge claims."""
    plan = cast(
        "dict[str, object]",
        deepcopy(_scheduled_full_plan_snapshot().plan),
    )
    validation_tree = cast("dict[str, object]", plan["validation-tree"])
    validation_tree["merge-commit"] = _pull_request_merge_commit_verification(
        commit_sha=TREE_SHA,
    )
    _redigest_plan(plan)

    with pytest.raises(
        ContractValidationError,
        match=(
            r"validation-tree\.merge-commit.*must be absent for scheduled_full"
        ),
    ):
        validate_ci_validation_plan(plan)


def test_validate_plan_rejects_unavailable_pr_merge_commit() -> None:
    """PR merge claims are only valid for available non-head ranges."""
    snapshot = freeze_ci_validation_plan(
        request=_normalized_unavailable_pull_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="fail-closed",
        diagnostics=[_valid_fail_closed_diagnostic()],
        fact_snapshot_providers=None,
    )
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    validation_tree = cast("dict[str, object]", plan["validation-tree"])
    validation_tree["merge-commit"] = _pull_request_merge_commit_verification(
        commit_sha=TREE_SHA,
    )
    _redigest_plan(plan)

    with pytest.raises(
        ContractValidationError,
        match=(
            r"validation-tree\.merge-commit.*must be absent unless "
            r"pull_request affected range is available"
        ),
    ):
        validate_ci_validation_plan(plan)


def test_freeze_plan_rejects_self_attested_pull_request_merge_commit() -> None:
    """Planner construction requires trusted control-plane merge evidence."""
    with pytest.raises(
        ContractValidationError,
        match="pull-request-merge-commit-verification",
    ):
        freeze_ci_validation_plan(
            request=_normalized_non_head_pull_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha="d" * 40,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[_subject()],
            validation_obligations=[_validation_obligation()],
            work_groups=[_ecosystem_gate_work_group()],
            evidence_expectations=[_evidence_expectation()],
            fact_snapshot_providers=[_fact_provider()],
        )


def test_freeze_plan_accepts_trusted_pull_request_merge_commit() -> None:
    """Trusted merge verification is copied from the constructor boundary."""
    snapshot = freeze_ci_validation_plan(
        request=_normalized_non_head_pull_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha="d" * 40,
        verdict_intent="executable",
        classification=_classification(),
        subjects=[_subject()],
        validation_obligations=[_validation_obligation()],
        work_groups=[_ecosystem_gate_work_group()],
        evidence_expectations=[_evidence_expectation()],
        fact_snapshot_providers=[_fact_provider()],
        pull_request_merge_commit_verification=(
            _pull_request_merge_commit_verification()
        ),
    )

    validation_tree = cast(
        "dict[str, object]",
        snapshot.plan["validation-tree"],
    )
    assert validation_tree["merge-commit"] == (
        _pull_request_merge_commit_verification()
    )


@pytest.mark.parametrize(
    ("field", "value", "expected_match"),
    [
        (
            "commit-sha",
            "e" * 40,
            r"pull-request-merge-commit-verification\.commit-sha",
        ),
        (
            "base-tip-sha",
            "e" * 40,
            r"pull-request-merge-commit-verification\.base-tip-sha",
        ),
        (
            "head-sha",
            "e" * 40,
            r"pull-request-merge-commit-verification\.head-sha",
        ),
        (
            "ref",
            "refs/pull/42/head",
            r"pull-request-merge-commit-verification\.ref",
        ),
    ],
)
def test_freeze_plan_rejects_mismatched_merge_verification(
    field: str,
    value: object,
    expected_match: str,
) -> None:
    """Trusted merge evidence must bind the recorded base/head/ref/commit."""
    verification = _pull_request_merge_commit_verification()
    verification[field] = value

    with pytest.raises(ContractValidationError, match=expected_match):
        freeze_ci_validation_plan(
            request=_normalized_non_head_pull_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha="d" * 40,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[_subject()],
            validation_obligations=[_validation_obligation()],
            work_groups=[_ecosystem_gate_work_group()],
            evidence_expectations=[_evidence_expectation()],
            fact_snapshot_providers=[_fact_provider()],
            pull_request_merge_commit_verification=verification,
        )


def test_validate_plan_rejects_pull_request_mismatched_merge_pair() -> None:
    """PR merge provenance must bind the exact recorded base/head pair."""
    snapshot = _plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    validation_tree = cast("dict[str, object]", plan["validation-tree"])
    validation_tree["commit-sha"] = "d" * 40
    affected_range = cast("dict[str, object]", plan["affected-range"])
    validation_tree["merge-commit"] = {
        "commit-sha": "d" * 40,
        "base-tip-sha": "e" * 40,
        "head-sha": affected_range["head-sha"],
        "ref": validation_tree["ref"],
        "verified": True,
        "verification-source": "github-control-plane",
    }
    planner = cast("dict[str, object]", plan["planner"])
    execution_tree = cast("dict[str, object]", planner["execution-tree"])
    execution_tree["observed-commit-sha"] = "d" * 40
    _redigest_plan(plan)

    with pytest.raises(
        ContractValidationError,
        match=(
            r"merge-commit\.base-tip-sha: "
            r"must match affected-range\.base-tip-sha"
        ),
    ):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
            pull_request_merge_commit_verification={
                "commit-sha": "d" * 40,
                "base-tip-sha": "e" * 40,
                "head-sha": affected_range["head-sha"],
                "ref": validation_tree["ref"],
                "verified": True,
                "verification-source": "github-control-plane",
            },
        )


def test_validate_plan_rejects_pull_request_mismatched_merge_commit() -> None:
    """PR merge provenance must bind the validation tree commit itself."""
    snapshot = _plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    validation_tree = cast("dict[str, object]", plan["validation-tree"])
    validation_tree["commit-sha"] = "d" * 40
    affected_range = cast("dict[str, object]", plan["affected-range"])
    validation_tree["merge-commit"] = {
        "commit-sha": "e" * 40,
        "base-tip-sha": affected_range["base-tip-sha"],
        "head-sha": affected_range["head-sha"],
        "ref": validation_tree["ref"],
        "verified": True,
        "verification-source": "github-control-plane",
    }
    planner = cast("dict[str, object]", plan["planner"])
    execution_tree = cast("dict[str, object]", planner["execution-tree"])
    execution_tree["observed-commit-sha"] = "d" * 40
    _redigest_plan(plan)

    with pytest.raises(
        ContractValidationError,
        match=(
            r"merge-commit\.commit-sha: "
            r"must match validation-tree\.commit-sha"
        ),
    ):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
            pull_request_merge_commit_verification={
                "commit-sha": "e" * 40,
                "base-tip-sha": affected_range["base-tip-sha"],
                "head-sha": affected_range["head-sha"],
                "ref": validation_tree["ref"],
                "verified": True,
                "verification-source": "github-control-plane",
            },
        )


def test_validate_plan_rejects_push_base_tip_sha() -> None:
    """Push plans must not retain pull request base-tip semantics."""
    plan = cast("dict[str, object]", deepcopy(_push_plan_snapshot().plan))
    affected_range = cast("dict[str, object]", plan["affected-range"])
    affected_range["base-tip-sha"] = "c" * 40
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="must be null for push"):
        validate_ci_validation_plan(plan)


def test_validate_plan_rejects_push_tree_head_mismatch() -> None:
    """Push validation-tree commits must match the affected-range head."""
    plan = cast("dict[str, object]", deepcopy(_push_plan_snapshot().plan))
    validation_tree = cast("dict[str, object]", plan["validation-tree"])
    validation_tree["commit-sha"] = "d" * 40
    planner = cast("dict[str, object]", plan["planner"])
    execution_tree = cast("dict[str, object]", planner["execution-tree"])
    execution_tree["observed-commit-sha"] = "d" * 40
    _redigest_plan(plan)

    with pytest.raises(
        ContractValidationError,
        match=r"must match affected-range\.head-sha for push",
    ):
        validate_ci_validation_plan(plan)


@pytest.mark.parametrize(
    "mutation",
    [
        lambda impact: impact.__setitem__("category", "not-a-category"),
        lambda impact: impact.__setitem__(
            "coverage-target",
            {"type": "tooling-surface", "id": "unknown-surface"},
        ),
        _set_bad_impact_diagnostic,
        lambda impact: impact.__setitem__("source-rule", ""),
    ],
)
def test_validate_plan_rejects_invalid_classification_records(
    mutation: Callable[[dict[str, object]], None],
) -> None:
    """Classification impact records use closed schema and vocabularies."""
    plan = cast("dict[str, object]", deepcopy(_plan_snapshot().plan))
    classification = cast("dict[str, object]", plan["classification"])
    impacts = cast("list[dict[str, object]]", classification["impacts"])
    mutation(impacts[0])
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError):
        validate_ci_validation_plan(plan)


def test_freeze_rejects_classification_without_changed_file_coverage() -> None:
    """Impact matched paths must exactly cover the changed-files snapshot."""
    classification = _classification()
    impacts = cast("list[dict[str, object]]", classification["impacts"])
    impacts[0]["matched-paths"] = ["src/public/lib/example.py"]

    with pytest.raises(ContractValidationError, match="changed files"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=classification,
            subjects=[_subject()],
            validation_obligations=[_validation_obligation()],
            work_groups=[_ecosystem_gate_work_group()],
            evidence_expectations=[_evidence_expectation()],
            fact_snapshot_providers=[_fact_provider()],
        )


def test_changed_files_snapshot_projection_is_stable() -> None:
    """Freeze the changed-files hash payload required by later consumers."""
    snapshot = _plan_snapshot()
    changed_files_snapshot = snapshot.changed_files_snapshot
    assert changed_files_snapshot is not None

    expected_hash = ci_validation_changed_files_hash(
        ["src/public/lib/example.py", "tests/example_test.py"],
    )
    assert changed_files_snapshot["artifact-ref"] == (
        ci_validation_changed_files_snapshot_artifact_ref(
            run_id=RUN_ID,
            run_attempt=RUN_ATTEMPT,
        )
    )
    assert changed_files_snapshot["changed-files-hash"] == expected_hash
    assert snapshot.plan["affected-range"] == {
        "status": "available",
        "base-sha": "a" * 40,
        "base-tip-sha": "c" * 40,
        "head-sha": TREE_SHA,
        "changed-files-hash": expected_hash,
    }


def test_validate_plan_requires_changed_files_snapshot_companion() -> None:
    """Available changed-files hashes require the authoritative sidecar."""
    snapshot = _plan_snapshot()

    with pytest.raises(ContractValidationError, match="changed-files-snapshot"):
        validate_ci_validation_plan(
            snapshot.plan,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_validate_plan_rejects_changed_files_snapshot_binding_drift() -> None:
    """Changed-files snapshot sidecars bind exactly to plan run identity."""
    snapshot = _plan_snapshot()
    changed_files_snapshot = cast(
        "dict[str, object]",
        deepcopy(snapshot.changed_files_snapshot),
    )
    changed_files_snapshot["artifact-ref"] = (
        ci_validation_changed_files_snapshot_artifact_ref(
            run_id="999",
            run_attempt=RUN_ATTEMPT,
        )
    )

    with pytest.raises(ContractValidationError, match="plan run identity"):
        validate_ci_validation_plan(
            snapshot.plan,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_validate_plan_rejects_unknown_changed_files_snapshot_root_member() -> (
    None
):
    """Changed-files snapshot root schema is closed to unknown members."""
    snapshot = _plan_snapshot()
    changed_files_snapshot = cast(
        "dict[str, object]",
        deepcopy(snapshot.changed_files_snapshot),
    )
    changed_files_snapshot["unknown-root-member"] = True

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_plan(
            snapshot.plan,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )

    assert any(
        issue.path == "$.changed-files-snapshot.unknown-root-member"
        and issue.message == "is not allowed"
        for issue in error.value.issues
    )


@pytest.mark.parametrize(
    "mutation",
    [
        lambda _snapshot, plan, _changed, _facts: cast(
            "list[dict[str, object]]",
            plan["subjects"],
        )[0].__setitem__("root", "/absolute"),
        lambda _snapshot, _plan, _changed, facts: cast(
            "list[object]",
            cast("list[dict[str, object]]", facts["providers"])[0]["roots"],
        ).__setitem__(0, "./relative"),
        lambda _snapshot, _plan, changed, _facts: cast(
            "list[object]",
            cast("dict[str, object]", changed["hash-payload"])["changed-files"],
        ).__setitem__(0, "src\\bad.py"),
        lambda _snapshot, _plan, changed, _facts: cast(
            "list[object]",
            cast("dict[str, object]", changed["hash-payload"])["changed-files"],
        ).__setitem__(0, "."),
        lambda _snapshot, plan, _changed, _facts: cast(
            "list[dict[str, object]]",
            cast("dict[str, object]", plan["classification"])["impacts"],
        )[0].__setitem__("matched-paths", ["src//bad.py"]),
    ],
)
def test_validate_plan_rejects_noncanonical_repo_relative_paths(
    mutation: Callable[
        [
            Any,
            dict[str, object],
            dict[str, object],
            dict[str, object],
        ],
        None,
    ],
) -> None:
    """Git path fields are canonical repository-relative paths."""
    snapshot = _plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    changed_files_snapshot = cast(
        "dict[str, object]",
        deepcopy(snapshot.changed_files_snapshot),
    )
    fact_snapshot = cast("dict[str, object]", deepcopy(snapshot.fact_snapshot))
    mutation(snapshot, plan, changed_files_snapshot, fact_snapshot)
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="repo-relative"):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
        )


def test_validate_plan_accepts_repository_root_directory_roots() -> None:
    """Directory root fields may use exact dot for the repository root."""
    subject = _subject()
    subject["root"] = "."
    provider = _fact_provider()
    provider["roots"] = ["."]
    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_classification(),
        subjects=[subject],
        validation_obligations=[_validation_obligation()],
        work_groups=[_ecosystem_gate_work_group()],
        evidence_expectations=[_evidence_expectation()],
        fact_snapshot_providers=[provider],
    )

    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )


def test_validate_plan_rejects_noncanonical_descriptor_catalog_paths() -> None:
    """Descriptor and target catalog paths use canonical Git path spelling."""
    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_classification(),
        subjects=[_descriptor_backed_subject()],
        validation_obligations=[
            _artifact_validation_obligation(),
            _validation_obligation(),
        ],
        descriptor_obligations=[_descriptor_obligation()],
        artifact_obligations=[_artifact_obligation()],
        work_groups=[
            _artifact_work_group(),
            _descriptor_work_group(),
            _ecosystem_gate_work_group(),
        ],
        evidence_expectations=[
            _artifact_evidence_expectation(),
            _descriptor_evidence_expectation(),
            _evidence_expectation(),
        ],
        fact_snapshot_providers=[_descriptor_fact_provider()],
    )
    fact_snapshot = cast("dict[str, object]", deepcopy(snapshot.fact_snapshot))
    provider = cast("list[dict[str, object]]", fact_snapshot["providers"])[0]
    descriptors = cast("list[dict[str, object]]", provider["descriptors"])
    descriptors[0]["descriptor-path"] = "src/public/lib/example/"

    with pytest.raises(ContractValidationError, match="repo-relative"):
        validate_ci_validation_plan(
            snapshot.plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=fact_snapshot,
        )


def test_validate_plan_rejects_noncanonical_target_catalog_paths() -> None:
    """Target catalog descriptor paths use canonical Git path spelling."""
    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_classification(),
        subjects=[_descriptor_backed_subject()],
        validation_obligations=[
            _artifact_validation_obligation(),
            _validation_obligation(),
        ],
        descriptor_obligations=[_descriptor_obligation()],
        artifact_obligations=[_artifact_obligation()],
        work_groups=[
            _artifact_work_group(),
            _descriptor_work_group(),
            _ecosystem_gate_work_group(),
        ],
        evidence_expectations=[
            _artifact_evidence_expectation(),
            _descriptor_evidence_expectation(),
            _evidence_expectation(),
        ],
        fact_snapshot_providers=[_descriptor_fact_provider()],
    )
    fact_snapshot = cast("dict[str, object]", deepcopy(snapshot.fact_snapshot))
    provider = cast("list[dict[str, object]]", fact_snapshot["providers"])[0]
    catalog = cast("dict[str, object]", provider["target-catalog"])
    descriptor_paths = cast("list[str]", catalog["descriptor-paths"])
    descriptor_paths[0] = "src/../three-release.json"

    with pytest.raises(ContractValidationError, match="repo-relative"):
        validate_ci_validation_plan(
            snapshot.plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=fact_snapshot,
        )


def test_fact_and_subject_snapshot_id_projections_are_stable() -> None:
    """Bind plan IDs to deterministic fact and subject projections."""
    snapshot = _plan_snapshot()
    fact_snapshot = snapshot.fact_snapshot
    assert fact_snapshot is not None

    assert fact_snapshot["artifact-ref"] == (
        ci_validation_fact_snapshot_artifact_ref(
            run_id=RUN_ID,
            run_attempt=RUN_ATTEMPT,
        )
    )
    assert fact_snapshot["fact-snapshot-id"] == ci_validation_fact_snapshot_id(
        [_fact_provider()],
    )
    assert snapshot.plan["fact-snapshot"] == {
        "status": "available",
        "id": fact_snapshot["fact-snapshot-id"],
    }
    assert snapshot.plan["subject-universe"] == {
        "status": "available",
        "id": ci_validation_subject_universe_id([_subject()]),
    }


def test_validate_plan_rejects_embedded_provider_facts() -> None:
    """Plan envelopes only carry sidecar IDs, never provider fact payloads."""
    snapshot = _plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    subject_universe = cast("dict[str, object]", plan["subject-universe"])
    fact_snapshot = cast("dict[str, object]", plan["fact-snapshot"])
    subject_universe["provider-subjects"] = [
        {
            "subject-id": "python.src-public-lib-example",
            "provider": "python",
            "ecosystem": "python",
        },
    ]
    fact_snapshot["providers"] = [_fact_provider()]
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="status and id"):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_validate_plan_requires_companion_fact_snapshot_when_available() -> (
    None
):
    """Available fact-snapshot envelopes require the authoritative sidecar."""
    snapshot = _plan_snapshot()

    with pytest.raises(ContractValidationError, match="companion"):
        validate_ci_validation_plan(snapshot.plan)


def test_validate_plan_rejects_unknown_fact_snapshot_root_member() -> None:
    """Fact snapshot root schema is closed to unknown members."""
    snapshot = _plan_snapshot()
    fact_snapshot = cast("dict[str, object]", deepcopy(snapshot.fact_snapshot))
    fact_snapshot["unknown-root-member"] = True

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_plan(
            snapshot.plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=fact_snapshot,
        )

    assert any(
        issue.path == "$.fact-snapshot.unknown-root-member"
        and issue.message == "is not allowed"
        for issue in error.value.issues
    )


def test_validate_plan_rejects_invalid_plan_id_even_when_sidecar_matches() -> (
    None
):
    """Plan IDs must be valid independently of fact snapshot equality."""
    snapshot = _plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    sidecar = cast("dict[str, object]", deepcopy(snapshot.fact_snapshot))
    plan["plan-id"] = None
    sidecar["plan-id"] = None
    _redigest_plan(plan)

    with pytest.raises(
        ContractValidationError,
        match="stable plan identifier",
    ):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=sidecar,
        )


@pytest.mark.parametrize(
    "mutation",
    [
        lambda sidecar: cast("dict[str, object]", sidecar["run"]).__setitem__(
            "run-id",
            "999",
        ),
        lambda sidecar: cast("dict[str, object]", sidecar["run"]).__setitem__(
            "run-attempt",
            "2",
        ),
        lambda sidecar: sidecar.__setitem__(
            "artifact-ref",
            ci_validation_fact_snapshot_artifact_ref(
                run_id="999",
                run_attempt=RUN_ATTEMPT,
            ),
        ),
        lambda sidecar: sidecar.__setitem__("plan-id", "different-plan"),
    ],
)
def test_validate_plan_rejects_fact_snapshot_sidecar_binding_mismatch(
    mutation: Callable[[dict[str, object]], None],
) -> None:
    """Companion fact snapshot binding must exactly match the plan."""
    snapshot = _plan_snapshot()
    sidecar = cast("dict[str, object]", deepcopy(snapshot.fact_snapshot))
    mutation(sidecar)

    with pytest.raises(ContractValidationError, match="must match"):
        validate_ci_validation_plan(
            snapshot.plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=sidecar,
        )


def test_validate_plan_rejects_invalid_fact_snapshot_plan_id() -> None:
    """Companion fact snapshots must carry a valid plan-id string."""
    snapshot = _plan_snapshot()
    sidecar = cast("dict[str, object]", deepcopy(snapshot.fact_snapshot))
    sidecar["plan-id"] = None

    with pytest.raises(
        ContractValidationError,
        match="stable plan identifier",
    ):
        validate_ci_validation_plan(
            snapshot.plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=sidecar,
        )


def test_validate_plan_rejects_invalid_raw_fact_snapshot_provider_entry() -> (
    None
):
    """Provider arrays are validated raw instead of filtering bad entries."""
    snapshot = _plan_snapshot()
    sidecar = cast("dict[str, object]", deepcopy(snapshot.fact_snapshot))
    providers = cast("list[object]", sidecar["providers"])
    providers.append("not-a-provider")

    with pytest.raises(ContractValidationError, match="must be object"):
        validate_ci_validation_plan(
            snapshot.plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=sidecar,
        )


def test_validate_plan_rejects_request_artifact_ref_run_mismatch() -> None:
    """Request artifact refs bind exactly to the plan run identity."""
    snapshot = _plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    request = cast("dict[str, object]", plan["request"])
    request["artifact-ref"] = ci_validation_request_artifact_ref(
        run_id="999",
        run_attempt=RUN_ATTEMPT,
    )
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="plan run identity"):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_rejects_invalid_untrusted_request_inputs() -> None:
    """Planner helpers require Group 3 normalized request objects."""
    with pytest.raises(ContractValidationError, match="normalized CI request"):
        freeze_ci_validation_plan(
            request=cast("Any", _request()),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            fact_snapshot_providers=[_fact_provider()],
        )


def test_no_forged_plan_from_invalid_normalization() -> None:
    """Invalid request normalization stays on the no-plan path."""
    document = _request()
    document["request-digest"] = "1" * 64
    normalization = normalize_ci_validation_request(
        document,
        expected_run_id=RUN_ID,
        expected_run_attempt=RUN_ATTEMPT,
    )

    snapshot = plan_from_request_normalization(
        normalization,
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="fail-closed",
        diagnostics=normalization.diagnostics,
        fact_snapshot_providers=[_fact_provider()],
    )

    assert normalization.request is None
    assert snapshot is None


def test_fail_closed_plan_requires_fail_closed_diagnostic() -> None:
    """Do not emit an empty successful-looking fail-closed plan."""
    with pytest.raises(ContractValidationError, match="fail-closed diagnostic"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="fail-closed",
            fact_snapshot_providers=None,
        )


def test_fail_closed_plan_freezes_terminal_aggregation_only() -> None:
    """Fail-closed plans carry diagnostics but no executable selectors."""
    diagnostic = _valid_fail_closed_diagnostic()

    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(changed_files=[]),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="fail-closed",
        diagnostics=[diagnostic],
        fact_snapshot_providers=None,
    )

    assert snapshot.plan["verdict-intent"] == "fail-closed"
    assert snapshot.fact_snapshot is None
    assert snapshot.plan["work-groups"] == [
        {
            "work-group-id": "evidence-aggregation",
            "kind": "evidence-aggregation",
            "coverage-target": {
                "type": "aggregation",
                "id": "ci-validation-aggregate",
            },
            "runner-family": "ubuntu",
            "depends-on": [],
            "aggregate-output": CiValidationKind.AGGREGATE.value,
        },
    ]


def test_unavailable_affected_range_retains_confirmed_endpoints() -> None:
    """Unavailable ranges may keep endpoint SHAs while omitting file hashes."""
    snapshot = freeze_ci_validation_plan(
        request=_normalized_unavailable_pull_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="fail-closed",
        diagnostics=[_valid_fail_closed_diagnostic()],
        fact_snapshot_providers=None,
    )

    assert snapshot.changed_files_snapshot is None
    assert snapshot.plan["verdict-intent"] == "fail-closed"
    assert snapshot.plan["affected-range"] == {
        "status": "unavailable",
        "base-sha": "a" * 40,
        "base-tip-sha": "c" * 40,
        "head-sha": TREE_SHA,
        "changed-files-hash": None,
    }
    validate_ci_validation_plan(snapshot.plan)


def test_unavailable_affected_range_requires_range_unconfirmed_diagnostic() -> (
    None
):
    """Unavailable ranges must carry the request's range-unconfirmed reason."""
    with pytest.raises(ContractValidationError, match="range-unconfirmed"):
        freeze_ci_validation_plan(
            request=_normalized_unavailable_pull_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="fail-closed",
            fact_snapshot_providers=None,
        )


def test_unavailable_range_rejects_unrelated_fail_closed_diagnostic() -> None:
    """Fail-closed diagnostics do not substitute for affected-range evidence."""
    with pytest.raises(ContractValidationError, match="range-unconfirmed"):
        freeze_ci_validation_plan(
            request=_normalized_unavailable_pull_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="fail-closed",
            diagnostics=[_unrelated_fail_closed_diagnostic()],
            fact_snapshot_providers=None,
        )


def test_unavailable_affected_range_rejects_mismatched_diagnostic_detail() -> (
    None
):
    """Planner diagnostics must preserve the normalized range detail."""
    diagnostic = _valid_fail_closed_diagnostic()
    diagnostic["detail"] = DiagnosticDetail.INCOMPLETE.value

    with pytest.raises(ContractValidationError, match="diagnostic detail"):
        freeze_ci_validation_plan(
            request=_normalized_unavailable_pull_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="fail-closed",
            diagnostics=[diagnostic],
            fact_snapshot_providers=None,
        )


def test_unavailable_affected_range_accepts_matching_diagnostic_detail() -> (
    None
):
    """The propagated range-unconfirmed detail is accepted."""
    snapshot = freeze_ci_validation_plan(
        request=_normalized_unavailable_pull_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="fail-closed",
        diagnostics=[_valid_fail_closed_diagnostic()],
        fact_snapshot_providers=None,
    )

    assert snapshot.plan["diagnostics"] == [_valid_fail_closed_diagnostic()]


def test_unavailable_push_rejects_base_tip_sha() -> None:
    """Unavailable push ranges still must not carry PR base-tip semantics."""
    snapshot = freeze_ci_validation_plan(
        request=_normalized_push_request(changed_files=[]),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="fail-closed",
        diagnostics=[_valid_fail_closed_diagnostic()],
        fact_snapshot_providers=None,
    )
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    affected_range = cast("dict[str, object]", plan["affected-range"])
    affected_range["status"] = "unavailable"
    affected_range["base-tip-sha"] = "c" * 40
    affected_range["changed-files-hash"] = None
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="must be null for push"):
        validate_ci_validation_plan(plan)


def test_freezes_representative_derived_runner_and_capabilities() -> None:
    """Later selector execution consumes frozen runner/capability fields."""
    snapshot = _plan_snapshot()
    work_groups = cast("list[dict[str, object]]", snapshot.plan["work-groups"])
    python_group = next(
        group
        for group in work_groups
        if group["work-group-id"] == "wg-python-gate"
    )
    expected_evidence = cast(
        "dict[str, object]",
        python_group["expected-evidence"],
    )

    assert PLANNED_CAPABILITY_ORDER == (
        "build",
        "test",
        "lint",
        "format",
        "type-check",
    )
    assert python_group["ecosystem"] == "python"
    assert python_group["runner-family"] == "ubuntu"
    assert expected_evidence["planned-capabilities"] == [
        "build",
        "test",
        "type-check",
    ]


def test_rejects_noncanonical_planned_capabilities() -> None:
    """Planner-time derived capabilities must be declared-order sets."""
    work_group = _ecosystem_gate_work_group()
    expected_evidence = cast(
        "dict[str, object]",
        work_group["expected-evidence"],
    )
    expected_evidence["planned-capabilities"] = ["test", "build"]

    with pytest.raises(ContractValidationError, match="declared unique order"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[_subject()],
            work_groups=[work_group],
            fact_snapshot_providers=[_fact_provider()],
        )


def test_rejects_empty_planned_capability_branch() -> None:
    """Non-null planned-capabilities is the non-empty capability branch."""
    work_group = _ecosystem_gate_work_group()
    expected_evidence = cast(
        "dict[str, object]",
        work_group["expected-evidence"],
    )
    expected_evidence["planned-capabilities"] = []

    with pytest.raises(ContractValidationError, match="non-empty capability"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[_subject()],
            work_groups=[work_group],
            fact_snapshot_providers=[_fact_provider()],
        )


@pytest.mark.parametrize(
    "kind",
    ["lightweight-preflight", "workflow-release-tooling"],
)
def test_category_result_evidence_rejects_planned_capability_branch(
    kind: str,
) -> None:
    """Category-result evidence kinds must use null planned-capabilities."""
    if kind == "lightweight-preflight":
        work_group = _lightweight_work_group()
        expected_evidence = cast(
            "dict[str, object]",
            work_group["expected-evidence"],
        )
        expected_evidence["planned-capabilities"] = ["build"]
        evidence = _lightweight_evidence_expectation()
        evidence["planned-capabilities"] = ["build"]

        with pytest.raises(ContractValidationError, match="category result"):
            freeze_ci_validation_plan(
                request=_normalized_request(),
                plan_id=PLAN_ID,
                created_at=CREATED_AT,
                observed_commit_sha=TREE_SHA,
                verdict_intent="executable",
                classification=_lightweight_classification(),
                subjects=[],
                validation_obligations=[_lightweight_validation_obligation()],
                work_groups=[work_group],
                evidence_expectations=[evidence],
                detail_profiles=[
                    _tooling_detail_profile(
                        profile_id="profile-lightweight",
                        category="lightweight-preflight",
                        coverage_target=_lightweight_coverage_target(),
                    ),
                ],
                fact_snapshot_providers=[_empty_fact_provider()],
            )
        return

    work_group = _tooling_work_group()
    expected_evidence = cast(
        "dict[str, object]",
        work_group["expected-evidence"],
    )
    expected_evidence["planned-capabilities"] = ["build"]
    evidence = _tooling_evidence_expectation()
    evidence["planned-capabilities"] = ["build"]

    with pytest.raises(ContractValidationError, match="category result"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_workflow_release_infrastructure_classification(),
            subjects=[_subject()],
            validation_obligations=[
                _validation_obligation(),
                _tooling_validation_obligation(),
            ],
            work_groups=[_ecosystem_gate_work_group(), work_group],
            evidence_expectations=[_evidence_expectation(), evidence],
            detail_profiles=[_tooling_detail_profile()],
            fact_snapshot_providers=[_fact_provider()],
        )


def test_valid_executable_plan_has_required_bindings() -> None:
    """Executable work, evidence, and obligation bind one-to-one."""
    snapshot = _plan_snapshot()

    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )


def test_executable_plan_rejects_empty_evidence_and_obligations() -> None:
    """Executable work groups cannot be orphan validation work."""
    with pytest.raises(ContractValidationError, match="evidence expectation"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[_subject()],
            work_groups=[_ecosystem_gate_work_group()],
            fact_snapshot_providers=[_fact_provider()],
        )


def test_executable_plan_rejects_orphan_evidence() -> None:
    """Evidence expectations must bind to one executable work group."""
    evidence = _evidence_expectation()
    evidence["work-group-id"] = "wg-orphan"

    with pytest.raises(ContractValidationError):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[_subject()],
            validation_obligations=[_validation_obligation()],
            work_groups=[_ecosystem_gate_work_group()],
            evidence_expectations=[evidence],
            fact_snapshot_providers=[_fact_provider()],
        )


def test_executable_plan_rejects_orphan_obligation() -> None:
    """Validation obligations must bind to one executable work group."""
    obligation = _validation_obligation()
    obligation["work-group-id"] = "wg-orphan"

    with pytest.raises(ContractValidationError):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[_subject()],
            validation_obligations=[obligation],
            work_groups=[_ecosystem_gate_work_group()],
            evidence_expectations=[_evidence_expectation()],
            fact_snapshot_providers=[_fact_provider()],
        )


def test_executable_plan_rejects_mismatched_binding_fields() -> None:
    """Duplicated binding contract fields must match exactly."""
    plan = cast("dict[str, object]", deepcopy(_plan_snapshot().plan))
    evidence_expectations = cast(
        "list[dict[str, object]]",
        plan["evidence-expectations"],
    )
    evidence_expectations[0]["planned-capabilities"] = ["build"]
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="does not match binding"):
        validate_ci_validation_plan(plan)


def test_descriptor_validation_chain_accepts_valid_plan() -> None:
    """Descriptor-validation work binds to a descriptor obligation."""
    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_classification(),
        subjects=[_descriptor_backed_subject()],
        validation_obligations=[
            _artifact_validation_obligation(),
            _validation_obligation(),
        ],
        descriptor_obligations=[_descriptor_obligation()],
        artifact_obligations=[_artifact_obligation()],
        work_groups=[
            _artifact_work_group(),
            _descriptor_work_group(),
            _ecosystem_gate_work_group(),
        ],
        evidence_expectations=[
            _artifact_evidence_expectation(),
            _descriptor_evidence_expectation(),
            _evidence_expectation(),
        ],
        fact_snapshot_providers=[_descriptor_fact_provider()],
    )

    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )


def test_descriptor_validation_null_ecosystem_uses_ubuntu_runner() -> None:
    """Descriptor-validation work without an ecosystem is Ubuntu-bound."""
    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_classification(),
        subjects=[_descriptor_backed_subject()],
        validation_obligations=[
            _artifact_validation_obligation(),
            _validation_obligation(),
        ],
        descriptor_obligations=[_descriptor_obligation()],
        artifact_obligations=[_artifact_obligation()],
        work_groups=[
            _artifact_work_group(),
            _descriptor_work_group(),
            _ecosystem_gate_work_group(),
        ],
        evidence_expectations=[
            _artifact_evidence_expectation(),
            _descriptor_evidence_expectation(),
            _evidence_expectation(),
        ],
        fact_snapshot_providers=[_descriptor_fact_provider()],
    )
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    work_groups = cast("list[dict[str, object]]", plan["work-groups"])
    descriptor_group = next(
        group
        for group in work_groups
        if group["kind"] == "descriptor-validation"
    )
    descriptor_group["runner-family"] = "windows"
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="runner-family"):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_descriptor_obligation_rejects_validation_only_subject() -> None:
    """Descriptor obligations require selected descriptor-backed subjects."""
    with pytest.raises(
        ContractValidationError,
        match="selected descriptor-backed subject",
    ):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[_subject()],
            validation_obligations=[_validation_obligation()],
            descriptor_obligations=[_descriptor_obligation()],
            work_groups=[
                _descriptor_work_group(),
                _ecosystem_gate_work_group(),
            ],
            evidence_expectations=[
                _descriptor_evidence_expectation(),
                _evidence_expectation(),
            ],
            fact_snapshot_providers=[_descriptor_fact_provider()],
        )


def test_descriptor_validation_requires_category_result_branch() -> None:
    """Descriptor-result evidence cannot masquerade as capability results."""
    group = _descriptor_work_group()
    expected_evidence = cast("dict[str, object]", group["expected-evidence"])
    expected_evidence["planned-capabilities"] = ["build"]

    with pytest.raises(ContractValidationError, match="category result"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[_descriptor_backed_subject()],
            descriptor_obligations=[_descriptor_obligation()],
            work_groups=[group],
            evidence_expectations=[_descriptor_evidence_expectation()],
            fact_snapshot_providers=[_descriptor_fact_provider()],
        )


def test_artifact_receipt_requires_category_result_branch() -> None:
    """Release-shaped artifact receipts use category-result evidence detail."""
    group = _artifact_work_group()
    expected_evidence = cast("dict[str, object]", group["expected-evidence"])
    expected_evidence["planned-capabilities"] = ["build"]

    with pytest.raises(ContractValidationError, match="category result"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[_descriptor_backed_subject()],
            validation_obligations=[_artifact_validation_obligation()],
            artifact_obligations=[_artifact_obligation()],
            work_groups=[group],
            evidence_expectations=[_artifact_evidence_expectation()],
            fact_snapshot_providers=[_descriptor_fact_provider()],
        )


@pytest.mark.parametrize("field", ["work-group-id", "expected-evidence-id"])
def test_required_descriptor_obligation_requires_binding_ids(
    field: str,
) -> None:
    """Required descriptor obligations cannot omit executable binding IDs."""
    obligation = _descriptor_obligation()
    obligation[field] = None

    with pytest.raises(ContractValidationError, match="required obligations"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[_descriptor_backed_subject()],
            descriptor_obligations=[obligation],
            work_groups=[_descriptor_work_group()],
            evidence_expectations=[_descriptor_evidence_expectation()],
            fact_snapshot_providers=[_descriptor_fact_provider()],
        )


def test_descriptor_validation_rejects_missing_obligation() -> None:
    """Descriptor-validation work requires one descriptor obligation."""
    with pytest.raises(ContractValidationError, match="descriptor obligation"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[_descriptor_backed_subject()],
            work_groups=[_descriptor_work_group()],
            evidence_expectations=[_descriptor_evidence_expectation()],
            fact_snapshot_providers=[_descriptor_fact_provider()],
        )


def test_descriptor_validation_rejects_mismatched_obligation() -> None:
    """Descriptor obligations must bind matching descriptor-validation work."""
    obligation = _descriptor_obligation()
    obligation["coverage-target"] = {"type": "descriptor", "id": "other.json"}

    with pytest.raises(ContractValidationError):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[_descriptor_backed_subject()],
            descriptor_obligations=[obligation],
            work_groups=[_descriptor_work_group()],
            evidence_expectations=[_descriptor_evidence_expectation()],
            fact_snapshot_providers=[_descriptor_fact_provider()],
        )


def test_descriptor_validation_rejects_evidence_field_mismatch() -> None:
    """Descriptor work still validates expected-evidence duplication."""
    work_group = _descriptor_work_group()
    expected = cast("dict[str, object]", work_group["expected-evidence"])
    expected["required"] = False

    with pytest.raises(ContractValidationError, match="required"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[_descriptor_backed_subject()],
            validation_obligations=[
                _artifact_validation_obligation(),
                _validation_obligation(),
            ],
            descriptor_obligations=[_descriptor_obligation()],
            artifact_obligations=[_artifact_obligation()],
            work_groups=[
                _artifact_work_group(),
                work_group,
                _ecosystem_gate_work_group(),
            ],
            evidence_expectations=[
                _artifact_evidence_expectation(),
                _descriptor_evidence_expectation(),
                _evidence_expectation(),
            ],
            fact_snapshot_providers=[_descriptor_fact_provider()],
        )


def test_release_artifact_chain_accepts_valid_plan() -> None:
    """Release-shaped work binds artifact and validation obligations."""
    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_classification(),
        subjects=[_descriptor_backed_subject()],
        validation_obligations=[
            _artifact_validation_obligation(),
            _validation_obligation(),
        ],
        descriptor_obligations=[_descriptor_obligation()],
        artifact_obligations=[_artifact_obligation()],
        work_groups=[
            _artifact_work_group(),
            _descriptor_work_group(),
            _ecosystem_gate_work_group(),
        ],
        evidence_expectations=[
            _artifact_evidence_expectation(),
            _descriptor_evidence_expectation(),
            _evidence_expectation(),
        ],
        fact_snapshot_providers=[_descriptor_fact_provider()],
    )

    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )


def test_artifact_obligations_cover_all_target_catalog_profiles() -> None:
    """Artifact obligations exactly cover descriptor target catalog profiles."""
    obligation = _artifact_obligation()
    obligation["profile-coverage"] = ["wheel", "zip"]

    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_classification(),
        subjects=[_descriptor_backed_subject()],
        validation_obligations=[
            _artifact_validation_obligation(),
            _validation_obligation(),
        ],
        descriptor_obligations=[_descriptor_obligation()],
        artifact_obligations=[obligation],
        work_groups=[
            _artifact_work_group(),
            _descriptor_work_group(),
            _ecosystem_gate_work_group(),
        ],
        evidence_expectations=[
            _artifact_evidence_expectation(),
            _descriptor_evidence_expectation(),
            _evidence_expectation(),
        ],
        fact_snapshot_providers=[_multi_profile_descriptor_fact_provider()],
    )

    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )


def test_artifact_obligations_cover_same_profile_artifact_shapes() -> None:
    """Catalog entries may require multiple artifact shapes for one profile."""
    provider = _descriptor_fact_provider()
    catalog = cast("dict[str, object]", provider["target-catalog"])
    wheel_entry = deepcopy(
        cast("list[dict[str, object]]", catalog["entries"])[0],
    )
    sdist_entry = deepcopy(wheel_entry)
    sdist_artifact = cast("dict[str, object]", sdist_entry["artifact"])
    sdist_artifact["concrete-kind"] = "sdist"
    sdist_artifact["expected-artifact-refs"] = [
        "ci-validation/artifacts/python/example/sdist.tar.gz",
    ]
    catalog["entries"] = [sdist_entry, wheel_entry]

    wheel_obligation = _artifact_obligation()
    sdist_obligation = deepcopy(wheel_obligation)
    sdist_obligation["artifact-obligation-id"] = "artifact-example-sdist"
    sdist_obligation["validation-obligation-id"] = "validation-artifact-sdist"
    sdist_obligation["work-group-id"] = "wg-artifact-sdist"
    sdist_obligation["expected-evidence-id"] = "evidence-artifact-sdist"
    sdist_artifact_obligation = cast(
        "dict[str, object]", sdist_obligation["artifact"]
    )
    sdist_artifact_obligation["concrete-kind"] = "sdist"
    sdist_artifact_obligation["expected-artifact-refs"] = [
        "ci-validation/artifacts/python/example/sdist.tar.gz",
    ]

    sdist_work_group = _artifact_work_group()
    sdist_work_group["work-group-id"] = "wg-artifact-sdist"
    sdist_work_group["coverage-target"] = {
        "type": "artifact-obligation",
        "id": "artifact-example-sdist",
    }
    sdist_evidence = _artifact_evidence_expectation()
    sdist_evidence["evidence-expectation-id"] = "evidence-artifact-sdist"
    sdist_evidence["work-group-id"] = "wg-artifact-sdist"
    sdist_evidence["coverage-target"] = {
        "type": "artifact-obligation",
        "id": "artifact-example-sdist",
    }
    sdist_validation = _artifact_validation_obligation()
    sdist_validation["validation-obligation-id"] = "validation-artifact-sdist"
    sdist_validation["coverage-target"] = {
        "type": "artifact-obligation",
        "id": "artifact-example-sdist",
    }
    sdist_validation["work-group-id"] = "wg-artifact-sdist"
    sdist_validation["expected-evidence-id"] = "evidence-artifact-sdist"

    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_classification(),
        subjects=[_descriptor_backed_subject()],
        validation_obligations=[
            _artifact_validation_obligation(),
            sdist_validation,
            _validation_obligation(),
        ],
        descriptor_obligations=[_descriptor_obligation()],
        artifact_obligations=[wheel_obligation, sdist_obligation],
        work_groups=[
            _artifact_work_group(),
            sdist_work_group,
            _descriptor_work_group(),
            _ecosystem_gate_work_group(),
        ],
        evidence_expectations=[
            _artifact_evidence_expectation(),
            sdist_evidence,
            _descriptor_evidence_expectation(),
            _evidence_expectation(),
        ],
        fact_snapshot_providers=[provider],
    )

    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )


@pytest.mark.parametrize(
    ("profiles", "provider"),
    [
        (["wheel"], _multi_profile_descriptor_fact_provider),
        ([], _descriptor_fact_provider),
        (["wheel", "zip"], _descriptor_fact_provider),
    ],
)
def test_artifact_obligations_reject_inexact_target_catalog_profiles(
    profiles: list[str],
    provider: Callable[[], dict[str, object]],
) -> None:
    """Artifact obligations cannot miss or add descriptor catalog profiles."""
    obligation = _artifact_obligation()
    obligation["profile-coverage"] = profiles

    with pytest.raises(
        ContractValidationError,
        match="target-catalog profiles",
    ):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[_descriptor_backed_subject()],
            validation_obligations=[
                _artifact_validation_obligation(),
                _validation_obligation(),
            ],
            descriptor_obligations=[_descriptor_obligation()],
            artifact_obligations=[obligation],
            work_groups=[
                _artifact_work_group(),
                _descriptor_work_group(),
                _ecosystem_gate_work_group(),
            ],
            evidence_expectations=[
                _artifact_evidence_expectation(),
                _descriptor_evidence_expectation(),
                _evidence_expectation(),
            ],
            fact_snapshot_providers=[provider()],
        )


def test_artifact_obligations_reject_unbacked_target_catalog() -> None:
    """Artifact obligations cannot execute without target-catalog backing."""
    provider = _descriptor_fact_provider()
    catalog = cast("dict[str, object]", provider["target-catalog"])
    catalog["descriptor-paths"] = []
    catalog["entries"] = []

    with pytest.raises(
        ContractValidationError,
        match=r"target-catalog profiles|unbacked",
    ):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[_descriptor_backed_subject()],
            validation_obligations=[
                _artifact_validation_obligation(),
                _validation_obligation(),
            ],
            descriptor_obligations=[_descriptor_obligation()],
            artifact_obligations=[_artifact_obligation()],
            work_groups=[
                _artifact_work_group(),
                _descriptor_work_group(),
                _ecosystem_gate_work_group(),
            ],
            evidence_expectations=[
                _artifact_evidence_expectation(),
                _descriptor_evidence_expectation(),
                _evidence_expectation(),
            ],
            fact_snapshot_providers=[provider],
        )


def test_selected_descriptor_backed_subject_rejects_missing_descriptor_chain():
    """Selected descriptor-backed subjects require descriptor validation."""
    with pytest.raises(ContractValidationError, match="descriptor chain"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[_descriptor_backed_subject()],
            validation_obligations=[_artifact_validation_obligation()],
            artifact_obligations=[_artifact_obligation()],
            work_groups=[_artifact_work_group()],
            evidence_expectations=[_artifact_evidence_expectation()],
            fact_snapshot_providers=[_descriptor_fact_provider()],
        )


def test_selected_descriptor_backed_subject_rejects_missing_artifact_chain():
    """Selected descriptor-backed subjects require release artifact evidence."""
    with pytest.raises(ContractValidationError, match="artifact chain"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[_descriptor_backed_subject()],
            descriptor_obligations=[_descriptor_obligation()],
            work_groups=[_descriptor_work_group()],
            evidence_expectations=[_descriptor_evidence_expectation()],
            fact_snapshot_providers=[_descriptor_fact_provider()],
        )


def test_selected_descriptor_backed_subject_rejects_catalog_capability_gap():
    """Target-catalog entries require artifact coverage."""
    subject = _descriptor_backed_subject()
    capabilities = cast("dict[str, bool]", subject["capabilities"])
    capabilities["release-shaped-artifacts"] = False

    with pytest.raises(ContractValidationError, match="artifact chain"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[subject],
            validation_obligations=[_validation_obligation()],
            descriptor_obligations=[_descriptor_obligation()],
            work_groups=[
                _descriptor_work_group(),
                _ecosystem_gate_work_group(),
            ],
            evidence_expectations=[
                _descriptor_evidence_expectation(),
                _evidence_expectation(),
            ],
            fact_snapshot_providers=[_descriptor_fact_provider()],
        )


def test_descriptor_backed_subject_without_release_artifacts_needs_no_chain():
    """Zero-target descriptor-backed subjects need no artifact chain."""
    subject = _descriptor_backed_subject()
    capabilities = cast("dict[str, bool]", subject["capabilities"])
    capabilities["release-shaped-artifacts"] = False
    provider = _descriptor_fact_provider()
    catalog = cast("dict[str, object]", provider["target-catalog"])
    catalog["entries"] = []

    freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_classification(),
        subjects=[subject],
        validation_obligations=[_validation_obligation()],
        descriptor_obligations=[_descriptor_obligation()],
        work_groups=[_descriptor_work_group(), _ecosystem_gate_work_group()],
        evidence_expectations=[
            _descriptor_evidence_expectation(),
            _evidence_expectation(),
        ],
        fact_snapshot_providers=[provider],
    )


def test_descriptor_backed_subject_requires_ecosystem_gate_chain() -> None:
    """Descriptor-backed subjects still require enabled validation gates."""
    with pytest.raises(ContractValidationError, match="ecosystem-gate chain"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[_descriptor_backed_subject()],
            validation_obligations=[_artifact_validation_obligation()],
            descriptor_obligations=[_descriptor_obligation()],
            artifact_obligations=[_artifact_obligation()],
            work_groups=[_artifact_work_group(), _descriptor_work_group()],
            evidence_expectations=[
                _artifact_evidence_expectation(),
                _descriptor_evidence_expectation(),
            ],
            fact_snapshot_providers=[_descriptor_fact_provider()],
        )


@pytest.mark.parametrize("field", ["work-group-id", "expected-evidence-id"])
def test_required_artifact_obligation_requires_binding_ids(field: str) -> None:
    """Required artifact obligations cannot omit executable binding IDs."""
    obligation = _artifact_obligation()
    obligation[field] = None

    with pytest.raises(ContractValidationError, match="required obligations"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[_descriptor_backed_subject()],
            validation_obligations=[_artifact_validation_obligation()],
            artifact_obligations=[obligation],
            work_groups=[_artifact_work_group()],
            evidence_expectations=[_artifact_evidence_expectation()],
            fact_snapshot_providers=[_descriptor_fact_provider()],
        )


def test_release_artifact_rejects_missing_artifact_obligation() -> None:
    """Release-shaped artifact work requires one artifact obligation."""
    with pytest.raises(ContractValidationError, match="artifact obligation"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[_descriptor_backed_subject()],
            validation_obligations=[_artifact_validation_obligation()],
            work_groups=[_artifact_work_group()],
            evidence_expectations=[_artifact_evidence_expectation()],
            fact_snapshot_providers=[_descriptor_fact_provider()],
        )


def test_release_artifact_rejects_orphan_or_mismatched_obligation() -> None:
    """Artifact obligations must bind matching release-shaped work."""
    obligation = _artifact_obligation()
    obligation["validation-obligation-id"] = "other-validation"

    with pytest.raises(ContractValidationError):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[_descriptor_backed_subject()],
            validation_obligations=[_artifact_validation_obligation()],
            artifact_obligations=[obligation],
            work_groups=[_artifact_work_group()],
            evidence_expectations=[_artifact_evidence_expectation()],
            fact_snapshot_providers=[_descriptor_fact_provider()],
        )


@pytest.mark.parametrize(
    ("mutation", "error"),
    [
        (_make_subject_inactive, "inactive"),
        (_make_subject_not_selected, "unselected"),
        (_make_subject_validation_only, "descriptor-backed"),
        (_make_subject_descriptor_path_mismatch, "descriptor path"),
    ],
)
def test_release_artifact_requires_selected_descriptor_backed_subject(
    mutation: Callable[[dict[str, object]], None],
    error: str,
) -> None:
    """Artifact obligations bind only to selected descriptor-backed subjects."""
    subject = _descriptor_backed_subject()
    mutation(subject)

    with pytest.raises(ContractValidationError, match=error):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[subject],
            validation_obligations=[_artifact_validation_obligation()],
            artifact_obligations=[_artifact_obligation()],
            work_groups=[_artifact_work_group()],
            evidence_expectations=[_artifact_evidence_expectation()],
            fact_snapshot_providers=[_descriptor_fact_provider()],
        )


def test_subject_schema_rejects_unsupported_ecosystem_capabilities() -> None:
    """Unsupported ecosystems must be inactive and capability-free."""
    subject = _subject()
    subject["ecosystem"] = "ruby"
    subject["activity-status"] = "active"

    with pytest.raises(ContractValidationError, match="inactive"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[subject],
            validation_obligations=[_validation_obligation()],
            work_groups=[_ecosystem_gate_work_group()],
            evidence_expectations=[_evidence_expectation()],
            fact_snapshot_providers=[_fact_provider()],
        )


def test_duplicate_selector_targets_require_unique_variants() -> None:
    """Duplicate targets must be disambiguated by selector variant."""
    work_groups = [_ecosystem_gate_work_group()]
    evidence = [_evidence_expectation()]
    obligations = [_validation_obligation()]
    duplicate_group = deepcopy(work_groups[0])
    duplicate_group["work-group-id"] = "wg-python-gate-duplicate"
    duplicate_evidence = deepcopy(evidence[0])
    duplicate_evidence["evidence-expectation-id"] = "evidence-python-gate-2"
    duplicate_evidence["work-group-id"] = "wg-python-gate-duplicate"
    duplicate_obligation = deepcopy(obligations[0])
    duplicate_obligation["validation-obligation-id"] = (
        "validation-python-gate-2"
    )
    duplicate_obligation["work-group-id"] = "wg-python-gate-duplicate"
    duplicate_obligation["expected-evidence-id"] = "evidence-python-gate-2"
    work_groups.append(duplicate_group)
    evidence.append(duplicate_evidence)
    obligations.append(duplicate_obligation)

    with pytest.raises(ContractValidationError, match="selector-variant"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[_subject()],
            validation_obligations=obligations,
            work_groups=work_groups,
            evidence_expectations=evidence,
            fact_snapshot_providers=[_fact_provider()],
        )


def test_validate_plan_reports_non_ijson_selector_coverage_target() -> None:
    """Selector variant validation reports non-I-JSON coverage targets."""
    snapshot = _plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    work_groups = cast("list[dict[str, object]]", plan["work-groups"])
    work_group = next(
        group
        for group in work_groups
        if group.get("kind") != "evidence-aggregation"
    )
    target = cast("dict[str, object]", work_group["coverage-target"])
    target["weight"] = 1.5
    plan["plan-digest"] = "not-a-digest"

    with pytest.raises(
        ContractValidationError,
        match="cannot canonicalize coverage target",
    ):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_detail_profile_references_must_resolve() -> None:
    """Work groups and evidence cannot reference undefined detail profiles."""
    work_group = _ecosystem_gate_work_group()
    evidence = _evidence_expectation()
    expected = cast("dict[str, object]", work_group["expected-evidence"])
    expected["detail-profile"] = "missing-profile"
    evidence["detail-profile"] = "missing-profile"

    with pytest.raises(ContractValidationError, match="detail-profile"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[_subject()],
            validation_obligations=[_validation_obligation()],
            work_groups=[work_group],
            evidence_expectations=[evidence],
            fact_snapshot_providers=[_fact_provider()],
        )


def test_tooling_detail_profile_reference_accepts_matching_definition() -> None:
    """Tooling detail profiles match the referencing category and target."""
    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_classification(),
        subjects=[_subject()],
        validation_obligations=[
            _validation_obligation(),
            _tooling_validation_obligation(),
        ],
        work_groups=[_ecosystem_gate_work_group(), _tooling_work_group()],
        evidence_expectations=[
            _evidence_expectation(),
            _tooling_evidence_expectation(),
        ],
        detail_profiles=[_tooling_detail_profile()],
        fact_snapshot_providers=[_fact_provider()],
    )

    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )


def test_tooling_detail_profile_reference_is_required() -> None:
    """Tooling and lightweight category-result work must name a profile."""
    with pytest.raises(ContractValidationError, match="detail-profile"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[_subject()],
            validation_obligations=[
                _validation_obligation(),
                _tooling_validation_obligation(),
            ],
            work_groups=[
                _ecosystem_gate_work_group(),
                _tooling_work_group(profile_id=None),
            ],
            evidence_expectations=[
                _evidence_expectation(),
                _tooling_evidence_expectation(profile_id=None),
            ],
            fact_snapshot_providers=[_fact_provider()],
        )


@pytest.mark.parametrize(
    "profile",
    [
        _tooling_detail_profile(category="lightweight-preflight"),
        _tooling_detail_profile(
            coverage_target=_tooling_coverage_target("target-catalog"),
        ),
    ],
)
def test_detail_profile_definition_must_match_reference(
    profile: dict[str, object],
) -> None:
    """Detail profile category and coverage target duplicate the referrer."""
    with pytest.raises(
        ContractValidationError,
        match=r"category|coverage-target",
    ):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[_subject()],
            validation_obligations=[_tooling_validation_obligation()],
            work_groups=[_tooling_work_group()],
            evidence_expectations=[_tooling_evidence_expectation()],
            detail_profiles=[profile],
            fact_snapshot_providers=[_fact_provider()],
        )


def test_tooling_coverage_target_ids_are_closed() -> None:
    """Tooling-surface target IDs must be registered workflow surfaces."""
    with pytest.raises(ContractValidationError, match="registered"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[_subject()],
            validation_obligations=[
                _tooling_validation_obligation(surface="unknown-surface"),
            ],
            work_groups=[_tooling_work_group(surface="unknown-surface")],
            evidence_expectations=[
                _tooling_evidence_expectation(surface="unknown-surface"),
            ],
            detail_profiles=[
                _tooling_detail_profile(
                    coverage_target=_tooling_coverage_target("unknown-surface"),
                ),
            ],
            fact_snapshot_providers=[_fact_provider()],
        )


@pytest.mark.parametrize(
    "surface",
    [
        "planner",
        "classifier",
        "fact-provider",
        "descriptor-schema-documentation",
    ],
)
def test_tooling_coverage_target_accepts_planner_surfaces(
    surface: str,
) -> None:
    """Planner-owned tooling-surface target IDs are registered."""
    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_classification(),
        subjects=[_subject()],
        validation_obligations=[
            _validation_obligation(),
            _tooling_validation_obligation(surface=surface),
        ],
        work_groups=[
            _ecosystem_gate_work_group(),
            _tooling_work_group(surface=surface),
        ],
        evidence_expectations=[
            _evidence_expectation(),
            _tooling_evidence_expectation(surface=surface),
        ],
        detail_profiles=[
            _tooling_detail_profile(
                coverage_target=_tooling_coverage_target(surface),
            ),
        ],
        fact_snapshot_providers=[_fact_provider()],
    )

    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )


@pytest.mark.parametrize(
    ("field", "value", "error"),
    [
        ("blocking", None, "blocking"),
        ("blocking", "yes", "blocking"),
        ("description", "", "description"),
        ("description", None, "description"),
    ],
)
def test_detail_profile_subchecks_require_blocking_and_description(
    field: str,
    value: object,
    error: str,
) -> None:
    """Required subchecks expose execution gating and inspectable intent."""
    profile = _tooling_detail_profile()
    subchecks = cast("list[dict[str, object]]", profile["required-subchecks"])
    subchecks[0][field] = value

    with pytest.raises(ContractValidationError, match=error):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[_subject()],
            validation_obligations=[_tooling_validation_obligation()],
            work_groups=[_tooling_work_group()],
            evidence_expectations=[_tooling_evidence_expectation()],
            detail_profiles=[profile],
            fact_snapshot_providers=[_fact_provider()],
        )


def test_detail_profile_subcheck_ids_unique_after_nfc_normalization() -> None:
    """Required subcheck IDs are unique by Unicode NFC normalized identity."""
    profile = _tooling_detail_profile()
    subchecks = cast("list[dict[str, object]]", profile["required-subchecks"])
    subchecks[0]["subcheck-id"] = "cafe\u0301"
    subchecks.append(
        {
            "subcheck-id": "café",
            "check-kind": "contract",
            "blocking": True,
            "description": "Verify the equivalent composed subcheck.",
        }
    )

    with pytest.raises(ContractValidationError, match="duplicate"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[_subject()],
            validation_obligations=[_tooling_validation_obligation()],
            work_groups=[_tooling_work_group()],
            evidence_expectations=[_tooling_evidence_expectation()],
            detail_profiles=[profile],
            fact_snapshot_providers=[_fact_provider()],
        )


def test_lightweight_policy_coverage_target_is_known_non_impacting() -> None:
    """Lightweight-policy target IDs use the closed known-non-impacting ID."""
    profile = _tooling_detail_profile(
        profile_id="profile-lightweight",
        category="lightweight-preflight",
        coverage_target=_lightweight_coverage_target(),
    )
    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_lightweight_classification(),
        subjects=[],
        validation_obligations=[_lightweight_validation_obligation()],
        work_groups=[_lightweight_work_group()],
        evidence_expectations=[_lightweight_evidence_expectation()],
        detail_profiles=[profile],
        fact_snapshot_providers=[_empty_fact_provider()],
    )

    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    with pytest.raises(ContractValidationError, match="registered"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_lightweight_classification(),
            subjects=[],
            validation_obligations=[
                _lightweight_validation_obligation(policy="other-policy"),
            ],
            work_groups=[_lightweight_work_group(policy="other-policy")],
            evidence_expectations=[
                _lightweight_evidence_expectation(policy="other-policy"),
            ],
            detail_profiles=[
                _tooling_detail_profile(
                    profile_id="profile-lightweight",
                    category="lightweight-preflight",
                    coverage_target=_lightweight_coverage_target(
                        "other-policy"
                    ),
                ),
            ],
            fact_snapshot_providers=[_empty_fact_provider()],
        )


def test_lightweight_preflight_accepts_registered_tooling_surface() -> None:
    """Lightweight preflight may cover workflow-release tooling surfaces."""
    coverage_target = _tooling_coverage_target("workflow-release-contract")
    profile = _tooling_detail_profile(
        profile_id="profile-lightweight",
        category="lightweight-preflight",
        coverage_target=coverage_target,
    )
    validation = _lightweight_validation_obligation()
    validation["coverage-target"] = coverage_target
    work_group = _lightweight_work_group()
    work_group["coverage-target"] = coverage_target
    evidence = _lightweight_evidence_expectation()
    evidence["coverage-target"] = coverage_target

    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_lightweight_classification(),
        subjects=[],
        validation_obligations=[validation],
        work_groups=[work_group],
        evidence_expectations=[evidence],
        detail_profiles=[profile],
        fact_snapshot_providers=[_empty_fact_provider()],
    )

    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )


def test_lightweight_preflight_rejects_non_ubuntu_runner() -> None:
    """Lightweight preflight is always hosted on Ubuntu runners."""
    profile = _tooling_detail_profile(
        profile_id="profile-lightweight",
        category="lightweight-preflight",
        coverage_target=_lightweight_coverage_target(),
    )
    work_group = _lightweight_work_group()
    work_group["runner-family"] = "windows"

    with pytest.raises(ContractValidationError, match="runner-family"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_lightweight_classification(),
            subjects=[],
            validation_obligations=[_lightweight_validation_obligation()],
            work_groups=[work_group],
            evidence_expectations=[_lightweight_evidence_expectation()],
            detail_profiles=[profile],
            fact_snapshot_providers=[_empty_fact_provider()],
        )


@pytest.mark.parametrize(
    ("coverage_target", "match"),
    [
        ({"type": "tooling-surface", "id": "unknown-surface"}, "registered"),
        ({"type": "subject", "id": "python.src-public-lib-example"}, "tooling"),
    ],
)
def test_lightweight_preflight_rejects_invalid_no_scope_targets(
    coverage_target: dict[str, object],
    match: str,
) -> None:
    """Lightweight-only preflight targets stay scoped to policy or tooling."""
    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_lightweight_classification(),
        subjects=[],
        validation_obligations=[_lightweight_validation_obligation()],
        work_groups=[_lightweight_work_group()],
        evidence_expectations=[_lightweight_evidence_expectation()],
        detail_profiles=[
            _tooling_detail_profile(
                profile_id="profile-lightweight",
                category="lightweight-preflight",
                coverage_target=_lightweight_coverage_target(),
            ),
        ],
        fact_snapshot_providers=[_empty_fact_provider()],
    )
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    for section_name in (
        "validation-obligations",
        "work-groups",
        "evidence-expectations",
        "detail-profiles",
    ):
        records = cast("list[dict[str, object]]", plan[section_name])
        if section_name == "work-groups":
            record = next(
                group
                for group in records
                if group["kind"] != "evidence-aggregation"
            )
        else:
            record = records[0]
        record["coverage-target"] = coverage_target
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match=match):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_zero_file_executable_plan_freezes_no_scope_shape() -> None:
    """No-file executable plans carry only terminal aggregation work."""
    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(changed_files=[]),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_zero_file_lightweight_classification(),
        subjects=[],
        fact_snapshot_providers=[_empty_fact_provider()],
    )
    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    classification = cast("dict[str, object]", snapshot.plan["classification"])
    assert classification["impacts"] == []
    assert snapshot.plan["validation-obligations"] == []
    assert snapshot.plan["evidence-expectations"] == []
    work_groups = cast("list[dict[str, object]]", snapshot.plan["work-groups"])
    assert [group["kind"] for group in work_groups] == ["evidence-aggregation"]

    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    plan_classification = cast("dict[str, object]", plan["classification"])
    plan_classification["lightweight-only"] = False
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="zero-file"):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


@pytest.mark.parametrize(
    ("section", "record_factory", "match"),
    [
        (
            "validation-obligations",
            _lightweight_validation_obligation,
            "validation-obligations",
        ),
        (
            "work-groups",
            _lightweight_work_group,
            "work-groups",
        ),
        (
            "evidence-expectations",
            _lightweight_evidence_expectation,
            "evidence-expectations",
        ),
    ],
)
def test_zero_file_executable_plan_rejects_lightweight_sections(
    section: str,
    record_factory: Callable[[], dict[str, object]],
    match: str,
) -> None:
    """Zero-file executable plans reject lightweight scoped work."""
    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(changed_files=[]),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_zero_file_lightweight_classification(),
        subjects=[],
        fact_snapshot_providers=[_empty_fact_provider()],
    )
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    records = cast("list[dict[str, object]]", plan[section])
    records.append(record_factory())
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match=match):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_zero_file_executable_plan_rejects_empty_impact_record() -> None:
    """Zero-file executable plans encode no impacts at all."""
    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(changed_files=[]),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_zero_file_lightweight_classification(),
        subjects=[],
        fact_snapshot_providers=[_empty_fact_provider()],
    )
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    classification = cast("dict[str, object]", plan["classification"])
    classification["impacts"] = [
        {
            "impact-id": "impact-empty",
            "category": "known-non-impacting",
            "matched-paths": [],
            "source-rule": "no-changed-files",
            "rationale": "No files changed.",
            "coverage-target": {"type": "none", "id": None},
            "requires": {
                "descriptor-validation": False,
                "downstream-expansion": False,
                "broad-expansion": False,
                "diagnostic": None,
            },
        },
    ]
    _redigest_plan(plan)

    with pytest.raises(
        ContractValidationError,
        match=r"classification\.impacts",
    ):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


@pytest.mark.parametrize("target_type", ["aggregation", "global", "none"])
@pytest.mark.parametrize(
    "section",
    ["work-groups", "evidence-expectations", "validation-obligations"],
)
def test_executable_coverage_targets_reject_non_executable_types(
    section: str,
    target_type: str,
) -> None:
    """Executable targets exclude terminal and impact-only types."""
    snapshot = _plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    target = {
        "type": target_type,
        "id": (
            "ci-validation-aggregate" if target_type == "aggregation" else None
        ),
    }
    records = cast("list[dict[str, object]]", plan[section])
    if section == "work-groups":
        record = next(
            group
            for group in records
            if group["kind"] != "evidence-aggregation"
        )
    else:
        record = records[0]
    record["coverage-target"] = target
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="registered"):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_detail_profiles_reject_artifact_obligation_targets() -> None:
    """Detail profiles cannot target artifact-obligation execution bindings."""
    with pytest.raises(ContractValidationError, match="registered"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[_subject()],
            validation_obligations=[_tooling_validation_obligation()],
            work_groups=[_tooling_work_group()],
            evidence_expectations=[_tooling_evidence_expectation()],
            detail_profiles=[
                _tooling_detail_profile(
                    coverage_target={
                        "type": "artifact-obligation",
                        "id": "artifact-example",
                    },
                ),
            ],
            fact_snapshot_providers=[_fact_provider()],
        )


def test_impact_records_keep_impact_only_coverage_targets() -> None:
    """Impact records retain global/none targets outside executable bindings."""
    snapshot = _global_full_scope_plan_snapshot()

    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    lightweight_snapshot = freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_lightweight_classification(),
        subjects=[],
        validation_obligations=[_lightweight_validation_obligation()],
        work_groups=[_lightweight_work_group()],
        evidence_expectations=[_lightweight_evidence_expectation()],
        detail_profiles=[
            _tooling_detail_profile(
                profile_id="profile-lightweight",
                category="lightweight-preflight",
                coverage_target=_lightweight_coverage_target(),
            ),
        ],
        fact_snapshot_providers=[_empty_fact_provider()],
    )
    validate_ci_validation_plan(
        lightweight_snapshot.plan,
        changed_files_snapshot=lightweight_snapshot.changed_files_snapshot,
        fact_snapshot=lightweight_snapshot.fact_snapshot,
    )


def test_lightweight_only_rejects_selected_subjects_and_provenance() -> None:
    """Lightweight-only plans cannot smuggle subject selection state."""
    with pytest.raises(ContractValidationError, match="select subjects"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_lightweight_classification(),
            subjects=[_subject()],
            validation_obligations=[],
            work_groups=[_lightweight_work_group()],
            evidence_expectations=[_lightweight_evidence_expectation()],
            detail_profiles=[
                _tooling_detail_profile(
                    profile_id="profile-lightweight",
                    category="lightweight-preflight",
                    coverage_target=_lightweight_coverage_target(),
                ),
            ],
            fact_snapshot_providers=[_fact_provider()],
        )

    classification = _lightweight_classification()
    classification["subject-selection-provenance"] = cast(
        "list[dict[str, object]]",
        _classification()["subject-selection-provenance"],
    )

    with pytest.raises(ContractValidationError, match="provenance"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=classification,
            subjects=[],
            validation_obligations=[],
            work_groups=[_lightweight_work_group()],
            evidence_expectations=[_lightweight_evidence_expectation()],
            detail_profiles=[
                _tooling_detail_profile(
                    profile_id="profile-lightweight",
                    category="lightweight-preflight",
                    coverage_target=_lightweight_coverage_target(),
                ),
            ],
            fact_snapshot_providers=[_empty_fact_provider()],
        )


def test_lightweight_only_rejects_impacting_classification() -> None:
    """Lightweight-only impact records are limited to known non-impacting."""
    classification = _lightweight_classification()
    impacts = cast("list[dict[str, object]]", classification["impacts"])
    impacts[0]["category"] = "project-scoped"

    with pytest.raises(ContractValidationError, match="known-non-impacting"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=classification,
            subjects=[],
            validation_obligations=[],
            work_groups=[_lightweight_work_group()],
            evidence_expectations=[_lightweight_evidence_expectation()],
            detail_profiles=[
                _tooling_detail_profile(
                    profile_id="profile-lightweight",
                    category="lightweight-preflight",
                    coverage_target=_lightweight_coverage_target(),
                ),
            ],
            fact_snapshot_providers=[_empty_fact_provider()],
        )


def test_lightweight_only_rejects_subject_validation_obligations() -> None:
    """Lightweight-only validation obligations cannot target subjects."""
    with pytest.raises(ContractValidationError, match="lightweight preflight"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_lightweight_classification(),
            subjects=[],
            validation_obligations=[_validation_obligation()],
            work_groups=[_lightweight_work_group()],
            evidence_expectations=[_lightweight_evidence_expectation()],
            detail_profiles=[
                _tooling_detail_profile(
                    profile_id="profile-lightweight",
                    category="lightweight-preflight",
                    coverage_target=_lightweight_coverage_target(),
                ),
            ],
            fact_snapshot_providers=[_empty_fact_provider()],
        )


def test_descriptor_obligation_must_resolve_to_fact_snapshot() -> None:
    """Descriptor obligations are fact-backed, not plan-only declarations."""
    descriptor_path = "src/public/lib/example/missing-release.json"
    group = _descriptor_work_group()
    group["coverage-target"] = {"type": "descriptor", "id": descriptor_path}
    evidence = _descriptor_evidence_expectation()
    evidence["coverage-target"] = {"type": "descriptor", "id": descriptor_path}
    obligation = _descriptor_obligation()
    obligation["coverage-target"] = {
        "type": "descriptor",
        "id": descriptor_path,
    }

    with pytest.raises(ContractValidationError, match="descriptor fact"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[_subject()],
            descriptor_obligations=[obligation],
            work_groups=[group],
            evidence_expectations=[evidence],
            fact_snapshot_providers=[_descriptor_fact_provider()],
        )


def test_descriptor_detail_target_must_resolve_to_fact_snapshot() -> None:
    """Descriptor coverage targets resolve from fact descriptors only."""
    snapshot = _plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    detail_profiles = cast("list[dict[str, object]]", plan["detail-profiles"])
    detail_profiles.append(
        _tooling_detail_profile(
            coverage_target={
                "type": "descriptor",
                "id": "src/public/lib/example/three-release.json",
            },
        ),
    )
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="descriptor target"):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_descriptor_detail_target_accepts_fact_backing() -> None:
    """Authoritative descriptor facts back descriptor-scoped detail targets."""
    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_classification(),
        subjects=[_descriptor_backed_subject()],
        validation_obligations=[
            _artifact_validation_obligation(),
            _validation_obligation(),
        ],
        descriptor_obligations=[_descriptor_obligation()],
        artifact_obligations=[_artifact_obligation()],
        work_groups=[
            _artifact_work_group(),
            _descriptor_work_group(),
            _ecosystem_gate_work_group(),
        ],
        evidence_expectations=[
            _artifact_evidence_expectation(),
            _descriptor_evidence_expectation(),
            _evidence_expectation(),
        ],
        fact_snapshot_providers=[_descriptor_fact_provider()],
    )
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    detail_profiles = cast("list[dict[str, object]]", plan["detail-profiles"])
    detail_profiles.append(
        _tooling_detail_profile(
            coverage_target={
                "type": "descriptor",
                "id": "src/public/lib/example/three-release.json",
            },
        ),
    )
    _redigest_plan(plan)

    validate_ci_validation_plan(
        plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )


def test_artifact_obligation_payload_must_be_catalog_backed() -> None:
    """Artifact obligations must match catalog-backed payloads."""
    obligation = _artifact_obligation()
    artifact = cast("dict[str, object]", obligation["artifact"])
    artifact["expected-artifact-refs"] = [
        "ci-validation/artifacts/python/example/other.whl",
    ]

    with pytest.raises(ContractValidationError, match="unbacked"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[_descriptor_backed_subject()],
            validation_obligations=[_artifact_validation_obligation()],
            artifact_obligations=[obligation],
            work_groups=[_artifact_work_group()],
            evidence_expectations=[_artifact_evidence_expectation()],
            fact_snapshot_providers=[_descriptor_fact_provider()],
        )


def test_dependency_graph_rejects_unknown_dependency() -> None:
    """Work-group dependencies must resolve to frozen work-group IDs."""
    plan = cast("dict[str, object]", deepcopy(_plan_snapshot().plan))
    work_groups = cast("list[dict[str, object]]", plan["work-groups"])
    work_groups[0]["depends-on"] = ["wg-missing"]
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="known work groups"):
        validate_ci_validation_plan(plan)


def test_dependency_graph_rejects_cycle() -> None:
    """Work-group dependencies must be acyclic."""
    plan = cast("dict[str, object]", deepcopy(_plan_snapshot().plan))
    work_groups = cast("list[dict[str, object]]", plan["work-groups"])
    executable = next(
        group
        for group in work_groups
        if group["kind"] != "evidence-aggregation"
    )
    terminal = next(
        group
        for group in work_groups
        if group["kind"] == "evidence-aggregation"
    )
    executable["depends-on"] = [terminal["work-group-id"]]
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match=r"aggregation|acyclic"):
        validate_ci_validation_plan(plan)


def test_dependency_graph_rejects_executable_dependency_on_terminal() -> None:
    """Executable work must not depend on terminal aggregation."""
    plan = cast("dict[str, object]", deepcopy(_plan_snapshot().plan))
    work_groups = cast("list[dict[str, object]]", plan["work-groups"])
    executable = next(
        group
        for group in work_groups
        if group["kind"] != "evidence-aggregation"
    )
    executable["depends-on"] = ["evidence-aggregation"]
    terminal = next(
        group
        for group in work_groups
        if group["kind"] == "evidence-aggregation"
    )
    terminal["depends-on"] = []
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="aggregation"):
        validate_ci_validation_plan(plan)


def test_dependency_graph_rejects_missing_terminal_coverage() -> None:
    """Terminal aggregation must be downstream of every executable group."""
    plan = cast("dict[str, object]", deepcopy(_plan_snapshot().plan))
    work_groups = cast("list[dict[str, object]]", plan["work-groups"])
    terminal = next(
        group
        for group in work_groups
        if group["kind"] == "evidence-aggregation"
    )
    terminal["depends-on"] = []
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="downstream"):
        validate_ci_validation_plan(plan)


def test_dependency_graph_accepts_valid_plan() -> None:
    """Frozen terminal aggregation depends on executable validation work."""
    snapshot = _plan_snapshot()
    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )


def test_executable_plan_requires_exact_provider_subject_coverage() -> None:
    """Selected subject universes must be backed by available providers."""
    provider = _fact_provider()
    provider["subjects"] = []

    with pytest.raises(ContractValidationError, match="exactly match"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[_subject()],
            work_groups=[_ecosystem_gate_work_group()],
            fact_snapshot_providers=[provider],
        )


def test_executable_plan_rejects_unrepresented_provider_subjects() -> None:
    """Provider-bound subjects cannot be omitted from the subject universe."""
    provider = _fact_provider()
    provider["subjects"] = [
        "python.src-public-lib-example",
        "python.src-public-lib-extra",
    ]

    with pytest.raises(ContractValidationError, match="exactly match"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[_subject()],
            work_groups=[_ecosystem_gate_work_group()],
            fact_snapshot_providers=[provider],
        )


def test_provider_subject_projection_accepts_exact_canonical_coverage() -> None:
    """Frozen plans keep provider subject facts in the companion snapshot."""
    provider = _fact_provider()
    provider["subjects"] = [
        "python.src-public-lib-example",
        "python.src-public-lib-example-tools",
    ]
    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_classification_with_second_subject(),
        subjects=[_subject(), _second_subject()],
        validation_obligations=[
            _validation_obligation(),
            _second_validation_obligation(),
        ],
        work_groups=[
            _ecosystem_gate_work_group(),
            _second_ecosystem_gate_work_group(),
        ],
        evidence_expectations=[
            _evidence_expectation(),
            _second_evidence_expectation(),
        ],
        fact_snapshot_providers=[provider],
    )

    assert snapshot.plan["subject-universe"] == {
        "status": "available",
        "id": ci_validation_subject_universe_id(
            [_subject(), _second_subject()],
        ),
    }
    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )


def test_selected_validation_only_subjects_require_gate_chains() -> None:
    """Every selected validation-only subject gets ecosystem-gate validation."""
    provider = _fact_provider()
    provider["subjects"] = [
        "python.src-public-lib-example",
        "python.src-public-lib-example-tools",
    ]

    with pytest.raises(ContractValidationError, match="validation-only"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification_with_second_subject(),
            subjects=[_subject(), _second_subject()],
            validation_obligations=[_validation_obligation()],
            work_groups=[_ecosystem_gate_work_group()],
            evidence_expectations=[_evidence_expectation()],
            fact_snapshot_providers=[provider],
        )


def test_validate_plan_rejects_matched_paths_missing_changed_file() -> None:
    """Impact matched paths must exactly cover the changed-files sidecar."""
    snapshot = _plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    classification = cast("dict[str, object]", plan["classification"])
    impacts = cast("list[dict[str, object]]", classification["impacts"])
    impacts[0]["matched-paths"] = ["src/public/lib/example.py"]
    _redigest_plan(plan)

    with pytest.raises(
        ContractValidationError,
        match="companion changed files",
    ):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_validate_plan_rejects_matched_paths_extra_changed_file() -> None:
    """Impact matched paths cannot add files absent from the sidecar."""
    snapshot = _plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    classification = cast("dict[str, object]", plan["classification"])
    impacts = cast("list[dict[str, object]]", classification["impacts"])
    impacts[0]["matched-paths"] = [
        "docs/extra.md",
        "src/public/lib/example.py",
        "tests/example_test.py",
    ]
    _redigest_plan(plan)

    with pytest.raises(
        ContractValidationError,
        match="companion changed files",
    ):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_validate_plan_rejects_unknown_impact_in_executable_plan() -> None:
    """Executable plans require resolved, non-unknown impact categories."""
    snapshot = _plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    classification = cast("dict[str, object]", plan["classification"])
    impacts = cast("list[dict[str, object]]", classification["impacts"])
    impacts[0]["category"] = "unknown"
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="unknown impacts"):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_validate_plan_rejects_empty_validation_only_capabilities() -> None:
    """Validation-only ecosystem gates must carry derived capabilities."""
    snapshot = _plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    work_groups = cast("list[dict[str, object]]", plan["work-groups"])
    gate = next(
        group for group in work_groups if group["kind"] == "ecosystem-gate"
    )
    expected_evidence = cast("dict[str, object]", gate["expected-evidence"])
    expected_evidence["planned-capabilities"] = []
    evidence = cast("list[dict[str, object]]", plan["evidence-expectations"])[0]
    evidence["planned-capabilities"] = []
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="non-empty capability"):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_validate_plan_rejects_mismatched_gate_capabilities() -> None:
    """Validation-only gate capabilities are derived from subject booleans."""
    snapshot = _plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    work_groups = cast("list[dict[str, object]]", plan["work-groups"])
    gate = next(
        group for group in work_groups if group["kind"] == "ecosystem-gate"
    )
    expected_evidence = cast("dict[str, object]", gate["expected-evidence"])
    expected_evidence["planned-capabilities"] = ["build"]
    evidence = cast("list[dict[str, object]]", plan["evidence-expectations"])[0]
    evidence["planned-capabilities"] = ["build"]
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="ecosystem-gate chain"):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_ecosystem_gate_capabilities_union_all_selected_subject_classes() -> (
    None
):
    """Ecosystem gates derive capabilities across all selected subjects."""
    descriptor_subject = _descriptor_backed_subject()
    descriptor_capabilities = cast(
        "dict[str, bool]",
        descriptor_subject["capabilities"],
    )
    descriptor_capabilities["lint"] = True
    validation_subject = _second_subject()
    provider = _descriptor_fact_provider()
    provider["subjects"] = [
        "python.src-public-lib-example",
        "python.src-public-lib-example-tools",
    ]
    expected_capabilities = [
        capability
        for capability in PLANNED_CAPABILITY_ORDER
        if capability in {"build", "test", "lint", "type-check"}
    ]
    coverage_target = {"type": "ecosystem", "id": "python"}
    work_group = _ecosystem_gate_work_group()
    work_group["coverage-target"] = coverage_target
    expected_evidence = cast(
        "dict[str, object]",
        work_group["expected-evidence"],
    )
    expected_evidence["planned-capabilities"] = expected_capabilities
    validation_obligation = _validation_obligation()
    validation_obligation["coverage-target"] = coverage_target
    evidence = _evidence_expectation()
    evidence["coverage-target"] = coverage_target
    evidence["planned-capabilities"] = expected_capabilities

    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_classification_with_second_subject(),
        subjects=[descriptor_subject, validation_subject],
        validation_obligations=[
            _artifact_validation_obligation(),
            validation_obligation,
        ],
        descriptor_obligations=[_descriptor_obligation()],
        artifact_obligations=[_artifact_obligation()],
        work_groups=[
            _artifact_work_group(),
            _descriptor_work_group(),
            work_group,
        ],
        evidence_expectations=[
            _artifact_evidence_expectation(),
            _descriptor_evidence_expectation(),
            evidence,
        ],
        fact_snapshot_providers=[provider],
    )

    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )


def test_validate_plan_rejects_validation_only_no_capabilities() -> None:
    """Selected validation-only subjects need non-empty derived coverage."""
    snapshot = _plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    subjects = cast("list[object]", plan["subjects"])
    subject = cast("dict[str, object]", subjects[0])
    capabilities = cast("dict[str, bool]", subject["capabilities"])
    for capability in PLANNED_CAPABILITY_ORDER:
        capabilities[capability] = False
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="capability"):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_validate_plan_rejects_duplicate_subject_ids() -> None:
    """Digest-consistent plan documents still reject duplicate subjects."""
    provider = _fact_provider()
    provider["subjects"] = [
        "python.src-public-lib-example",
        "python.src-public-lib-example-tools",
    ]
    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_classification_with_second_subject(),
        subjects=[_subject(), _second_subject()],
        validation_obligations=[
            _validation_obligation(),
            _second_validation_obligation(),
        ],
        work_groups=[
            _ecosystem_gate_work_group(),
            _second_ecosystem_gate_work_group(),
        ],
        evidence_expectations=[
            _evidence_expectation(),
            _second_evidence_expectation(),
        ],
        fact_snapshot_providers=[provider],
    )
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    subjects = cast("list[dict[str, object]]", plan["subjects"])
    subjects[1]["subject-id"] = subjects[0]["subject-id"]
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="ordered uniquely"):
        validate_ci_validation_plan(plan)


def test_validate_plan_rejects_noncanonical_subject_order() -> None:
    """Plan validation fails before accepting noncanonical subject digests."""
    provider = _fact_provider()
    provider["subjects"] = [
        "python.src-public-lib-example",
        "python.src-public-lib-example-tools",
    ]
    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_classification_with_second_subject(),
        subjects=[_subject(), _second_subject()],
        validation_obligations=[
            _validation_obligation(),
            _second_validation_obligation(),
        ],
        work_groups=[
            _ecosystem_gate_work_group(),
            _second_ecosystem_gate_work_group(),
        ],
        evidence_expectations=[
            _evidence_expectation(),
            _second_evidence_expectation(),
        ],
        fact_snapshot_providers=[provider],
    )
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    subjects = cast("list[dict[str, object]]", plan["subjects"])
    subjects.reverse()
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="ordered uniquely"):
        validate_ci_validation_plan(plan)


@pytest.mark.parametrize(
    "mutation",
    [
        _set_invalid_mode,
        _set_bad_scheduled_full,
        _set_bad_affected_range,
        _set_bad_execution_tree,
        _set_bad_subject_universe,
        _set_bad_fact_snapshot,
    ],
)
def test_validate_plan_rejects_digest_consistent_invalid_envelope_fields(
    mutation,
) -> None:
    """Reject digest-consistent invalid LLD envelope/runtime fields."""
    plan = cast("dict[str, object]", deepcopy(_plan_snapshot().plan))
    mutation(plan)
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError):
        validate_ci_validation_plan(plan)


def test_fact_snapshot_rejects_duplicate_descriptor_paths() -> None:
    """Descriptor paths are globally unique across the fact snapshot."""
    provider = _descriptor_fact_provider()
    descriptors = cast("list[dict[str, object]]", provider["descriptors"])
    descriptors.append(dict(descriptors[0]))

    with pytest.raises(ContractValidationError, match="globally unique"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[_subject()],
            work_groups=[_ecosystem_gate_work_group()],
            fact_snapshot_providers=[provider],
        )

    with pytest.raises(ContractValidationError, match="globally unique"):
        ci_validation_fact_snapshot_id([provider])


def test_fact_snapshot_rejects_noncanonical_nested_ordering() -> None:
    """Nested fact arrays must be pre-canonical before snapshot digesting."""
    provider = _fact_provider()
    provider["dependency-edges"] = [
        {
            "from-subject-id": "python.z",
            "to-subject-id": "python.a",
            "relation": "workspace",
        },
        {
            "from-subject-id": "python.a",
            "to-subject-id": "python.z",
            "relation": "workspace",
        },
    ]

    with pytest.raises(ContractValidationError, match="canonical"):
        ci_validation_fact_snapshot_id([provider])


def test_validate_plan_reports_non_ijson_fact_snapshot_dimensions() -> None:
    """Companion fact snapshots report non-I-JSON catalog dimensions."""
    snapshot = _plan_snapshot()
    sidecar = cast("dict[str, object]", deepcopy(snapshot.fact_snapshot))
    providers = cast("list[dict[str, object]]", sidecar["providers"])
    provider = providers[0]
    descriptor_provider = _descriptor_fact_provider()
    provider["descriptors"] = descriptor_provider["descriptors"]
    provider["target-catalog"] = descriptor_provider["target-catalog"]
    catalog = cast("dict[str, object]", provider["target-catalog"])
    entries = cast("list[dict[str, object]]", catalog["entries"])
    artifact = cast("dict[str, object]", entries[0]["artifact"])
    artifact["variant-dimensions"] = {"python": 3.14}

    with pytest.raises(
        ContractValidationError,
        match="cannot canonicalize fact snapshot",
    ):
        validate_ci_validation_plan(
            snapshot.plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=sidecar,
        )


def test_fact_snapshot_id_reports_non_ijson_target_catalog_dimensions() -> None:
    """Fact snapshot digesting reports non-I-JSON catalog dimensions."""
    provider = _descriptor_fact_provider()
    catalog = cast("dict[str, object]", provider["target-catalog"])
    entries = cast("list[dict[str, object]]", catalog["entries"])
    artifact = cast("dict[str, object]", entries[0]["artifact"])
    artifact["variant-dimensions"] = {"python": 3.14}

    with pytest.raises(
        ContractValidationError,
        match="target catalog variant dimensions",
    ):
        ci_validation_fact_snapshot_id([provider])


def test_freeze_plan_reports_non_ijson_target_catalog_dimensions() -> None:
    """Fact snapshot production reports non-I-JSON catalog dimensions."""
    provider = _descriptor_fact_provider()
    catalog = cast("dict[str, object]", provider["target-catalog"])
    entries = cast("list[dict[str, object]]", catalog["entries"])
    artifact = cast("dict[str, object]", entries[0]["artifact"])
    artifact["variant-dimensions"] = {"python": 3.14}

    with pytest.raises(
        ContractValidationError,
        match="target catalog variant dimensions",
    ):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[_subject()],
            work_groups=[_ecosystem_gate_work_group()],
            fact_snapshot_providers=[provider],
        )


def test_fact_snapshot_rejects_unknown_provider_tooling_surface() -> None:
    """Provider tooling facts resolve to the closed surface vocabulary."""
    provider = _workflow_release_descriptor_provider()
    provider["tooling-surfaces"] = [
        *TOOLING_SURFACE_IDS[:-2],
        "unknown-surface",
        *TOOLING_SURFACE_IDS[-2:],
    ]

    with pytest.raises(ContractValidationError, match="closed tooling surface"):
        ci_validation_fact_snapshot_id([provider])


def test_fact_snapshot_rejects_dependency_edge_unknown_endpoint() -> None:
    """Provider dependency edges only connect provider-bound subjects."""
    provider = _fact_provider()
    provider["dependency-edges"] = [
        {
            "from-subject-id": "python.src-public-lib-example",
            "to-subject-id": "python.unknown",
            "relation": "workspace",
        },
    ]

    with pytest.raises(ContractValidationError, match="provider subject"):
        ci_validation_fact_snapshot_id([provider])


def test_fact_snapshot_rejects_invalid_nested_entries() -> None:
    """Target-catalog entries must include required nested artifact shapes."""
    provider = _descriptor_fact_provider()
    target_catalog = cast("dict[str, object]", provider["target-catalog"])
    entries = cast("list[dict[str, object]]", target_catalog["entries"])
    entries[0]["artifact"] = {"kind-family": "python"}

    with pytest.raises(ContractValidationError):
        freeze_ci_validation_plan(
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
            fact_snapshot_providers=[provider],
        )


@pytest.mark.parametrize(
    "mutation",
    [
        _remove_diagnostic_message,
        _empty_diagnostic_message,
        _remove_diagnostic_source,
        _set_bad_diagnostic_source_type,
    ],
)
def test_planner_diagnostics_require_message_and_planner_source(
    mutation,
) -> None:
    """Planner diagnostics require message and planner source provenance."""
    diagnostic = _valid_fail_closed_diagnostic()
    mutation(diagnostic)

    with pytest.raises(ContractValidationError):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="fail-closed",
            diagnostics=[diagnostic],
            fact_snapshot_providers=None,
        )


def test_planner_diagnostic_accepts_valid_message_and_source() -> None:
    """Valid planner diagnostic provenance remains accepted."""
    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(changed_files=[]),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="fail-closed",
        diagnostics=[_valid_fail_closed_diagnostic()],
        fact_snapshot_providers=None,
    )

    assert snapshot.plan["diagnostics"] == [_valid_fail_closed_diagnostic()]


def test_planner_diagnostic_rejects_work_group_source() -> None:
    """Planner diagnostics exclude work-group from the source vocabulary."""
    diagnostic = _valid_fail_closed_diagnostic()
    diagnostic["source"] = {"type": "work-group", "id": "wg-python-gate"}

    with pytest.raises(ContractValidationError):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="fail-closed",
            diagnostics=[diagnostic],
            fact_snapshot_providers=None,
        )


def test_fact_provider_diagnostic_accepts_work_group_source() -> None:
    """Generic fact-provider diagnostics may cite work-group provenance."""
    diagnostic = _valid_fail_closed_diagnostic()
    diagnostic["source"] = {"type": "work-group", "id": "wg-python-gate"}
    provider = _empty_fact_provider()
    provider["status"] = "unavailable"
    provider["diagnostics"] = [diagnostic]

    assert ci_validation_fact_snapshot_id([provider])


def test_fact_provider_diagnostic_accepts_null_message() -> None:
    """Provider diagnostics may omit human text with a null message."""
    diagnostic = _valid_fail_closed_diagnostic()
    diagnostic["message"] = None
    provider = _empty_fact_provider()
    provider["status"] = "unavailable"
    provider["diagnostics"] = [diagnostic]

    assert ci_validation_fact_snapshot_id([provider])


def test_planner_diagnostic_rejects_null_message() -> None:
    """Planner diagnostics still require human-readable messages."""
    diagnostic = _valid_fail_closed_diagnostic()
    diagnostic["message"] = None

    with pytest.raises(ContractValidationError, match="message"):
        freeze_ci_validation_plan(
            request=_normalized_request(changed_files=[]),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="fail-closed",
            diagnostics=[diagnostic],
            fact_snapshot_providers=None,
        )


def test_executable_plan_rejects_default_classification_changed_file_gap() -> (
    None
):
    """Defaulted classification cannot bypass changed-file coverage."""
    with pytest.raises(ContractValidationError, match="changed files"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            subjects=[_subject()],
            validation_obligations=[_validation_obligation()],
            work_groups=[_ecosystem_gate_work_group()],
            evidence_expectations=[_evidence_expectation()],
            fact_snapshot_providers=[_fact_provider()],
        )


def test_provider_subject_projection_rejects_wrong_provider_identity() -> None:
    """Provider-bound subject coverage includes provider/ecosystem identity."""
    subject = _subject()
    subject["subject-id"] = "dotnet.src-public-lib-example"
    subject["ecosystem"] = "dotnet"
    provider = _fact_provider()
    provider["subjects"] = ["dotnet.src-public-lib-example"]
    classification = _classification()
    impact = cast("list[dict[str, object]]", classification["impacts"])[0]
    target = cast("dict[str, object]", impact["coverage-target"])
    target["id"] = "dotnet.src-public-lib-example"
    provenance = cast(
        "list[dict[str, object]]",
        classification["subject-selection-provenance"],
    )
    provenance[0]["subject-id"] = "dotnet.src-public-lib-example"

    with pytest.raises(ContractValidationError, match="provider"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=classification,
            subjects=[subject],
            validation_obligations=[_validation_obligation()],
            work_groups=[_ecosystem_gate_work_group()],
            evidence_expectations=[_evidence_expectation()],
            fact_snapshot_providers=[provider],
        )


def test_validate_plan_rejects_unbacked_descriptor_fact_snapshot() -> None:
    """Descriptor obligations resolve from fact providers, not themselves."""
    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_classification(),
        subjects=[_descriptor_backed_subject()],
        validation_obligations=[
            _artifact_validation_obligation(),
            _validation_obligation(),
        ],
        descriptor_obligations=[_descriptor_obligation()],
        artifact_obligations=[_artifact_obligation()],
        work_groups=[
            _artifact_work_group(),
            _descriptor_work_group(),
            _ecosystem_gate_work_group(),
        ],
        evidence_expectations=[
            _artifact_evidence_expectation(),
            _descriptor_evidence_expectation(),
            _evidence_expectation(),
        ],
        fact_snapshot_providers=[_descriptor_fact_provider()],
    )
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    fact_snapshot = cast("dict[str, object]", deepcopy(snapshot.fact_snapshot))
    fact_snapshot["providers"] = [_fact_provider()]
    fact_snapshot["fact-snapshot-id"] = ci_validation_fact_snapshot_id(
        [_fact_provider()],
    )
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="descriptor fact"):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=fact_snapshot,
        )


def test_validate_plan_rejects_companion_provider_subject_mismatch() -> None:
    """Standalone validation rechecks companion provider/subject equality."""
    snapshot = _plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    fact_snapshot = cast("dict[str, object]", deepcopy(snapshot.fact_snapshot))
    providers = cast("list[dict[str, object]]", fact_snapshot["providers"])
    providers[0]["subjects"] = []
    fact_snapshot["fact-snapshot-id"] = ci_validation_fact_snapshot_id(
        providers,
    )
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="provider"):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=fact_snapshot,
        )


def test_validate_plan_rejects_descriptor_owner_subject_mismatch() -> None:
    """Ecosystem-owned descriptor facts belong to the selected subject."""
    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_classification(),
        subjects=[_descriptor_backed_subject()],
        validation_obligations=[
            _artifact_validation_obligation(),
            _validation_obligation(),
        ],
        descriptor_obligations=[_descriptor_obligation()],
        artifact_obligations=[_artifact_obligation()],
        work_groups=[
            _artifact_work_group(),
            _descriptor_work_group(),
            _ecosystem_gate_work_group(),
        ],
        evidence_expectations=[
            _artifact_evidence_expectation(),
            _descriptor_evidence_expectation(),
            _evidence_expectation(),
        ],
        fact_snapshot_providers=[_descriptor_fact_provider()],
    )
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    fact_snapshot = cast("dict[str, object]", deepcopy(snapshot.fact_snapshot))
    providers = cast("list[dict[str, object]]", fact_snapshot["providers"])
    descriptors = cast("list[dict[str, object]]", providers[0]["descriptors"])
    descriptors[0]["owner-subject-id"] = "python.other"
    fact_snapshot["fact-snapshot-id"] = ci_validation_fact_snapshot_id(
        providers,
    )
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="owner subject"):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=fact_snapshot,
        )


def test_validate_plan_accepts_workflow_release_owned_descriptor() -> None:
    """Workflow-release descriptor facts are tooling-owned."""
    classification = _workflow_release_infrastructure_classification(
        descriptors="all-discovered",
    )
    descriptor_path = "docs/workflow-release/three-release.json"

    freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=classification,
        subjects=[_subject()],
        validation_obligations=[
            _validation_obligation(),
            _tooling_validation_obligation(),
        ],
        descriptor_obligations=[
            {
                **_descriptor_obligation(),
                "descriptor-scope": "all-discovered",
                "coverage-target": {
                    "type": "descriptor",
                    "id": descriptor_path,
                },
            },
        ],
        work_groups=[
            {
                **_descriptor_work_group(),
                "coverage-target": {
                    "type": "descriptor",
                    "id": descriptor_path,
                },
            },
            _ecosystem_gate_work_group(),
            _tooling_work_group(),
        ],
        evidence_expectations=[
            {
                **_descriptor_evidence_expectation(),
                "coverage-target": {
                    "type": "descriptor",
                    "id": descriptor_path,
                },
            },
            _evidence_expectation(),
            _tooling_evidence_expectation(),
        ],
        detail_profiles=[_tooling_detail_profile()],
        fact_snapshot_providers=[
            _fact_provider(),
            _workflow_release_descriptor_provider(),
        ],
    )


def test_workflow_release_provider_rejects_ecosystem_owned_subjects() -> None:
    """The workflow-release provider cannot claim ecosystem subjects."""
    provider = _workflow_release_descriptor_provider()
    provider["subjects"] = ["python.src-public-lib-example"]

    with pytest.raises(
        ContractValidationError,
        match="workflow-release provider subjects",
    ):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_lightweight_classification(),
            subjects=[],
            validation_obligations=[_lightweight_validation_obligation()],
            work_groups=[_lightweight_work_group()],
            evidence_expectations=[_lightweight_evidence_expectation()],
            detail_profiles=[
                _tooling_detail_profile(
                    profile_id="profile-lightweight",
                    category="lightweight-preflight",
                    coverage_target=_lightweight_coverage_target(),
                ),
            ],
            fact_snapshot_providers=[provider],
        )


def test_validate_plan_rejects_missing_selected_subject_provenance() -> None:
    """Every active selected subject needs selection provenance."""
    plan = cast("dict[str, object]", deepcopy(_plan_snapshot().plan))
    classification = cast("dict[str, object]", plan["classification"])
    classification["subject-selection-provenance"] = []
    _redigest_plan(plan)

    with pytest.raises(
        ContractValidationError,
        match="active selected subject",
    ):
        validate_ci_validation_plan(plan)


@pytest.mark.parametrize(
    ("activity_status", "selection_status"),
    [("inactive", "not-selected"), ("active", "not-selected")],
)
def test_validate_plan_rejects_provenance_for_non_active_selected_subject(
    activity_status: str,
    selection_status: str,
) -> None:
    """Selection provenance records can target only active selected subjects."""
    plan = cast("dict[str, object]", deepcopy(_plan_snapshot().plan))
    subjects = cast("list[dict[str, object]]", plan["subjects"])
    second_subject = _second_subject()
    second_subject["activity-status"] = activity_status
    second_subject["selection-status"] = selection_status
    subjects.append(second_subject)
    plan["subject-universe"] = {
        "status": "available",
        "id": ci_validation_subject_universe_id(subjects),
    }
    classification = cast("dict[str, object]", plan["classification"])
    provenance = cast(
        "list[dict[str, object]]",
        classification["subject-selection-provenance"],
    )
    provenance[0]["subject-id"] = "python.src-public-lib-example-tools"
    _redigest_plan(plan)

    with pytest.raises(
        ContractValidationError,
        match="active selected subject",
    ):
        validate_ci_validation_plan(plan)


def test_validate_plan_rejects_invalid_direct_provenance_semantics() -> None:
    """Direct selections cannot carry downstream direct-subject links."""
    plan = cast("dict[str, object]", deepcopy(_plan_snapshot().plan))
    classification = cast("dict[str, object]", plan["classification"])
    provenance = cast(
        "list[dict[str, object]]",
        classification["subject-selection-provenance"],
    )
    provenance[0]["direct-subject-id"] = "python.src-public-lib-example"
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="direct-subject-id"):
        validate_ci_validation_plan(plan)


def test_validate_plan_accepts_matching_direct_project_scoped_provenance() -> (
    None
):
    """Direct project-scoped provenance must cite impacts for that subject."""
    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_classification_with_second_subject(),
        subjects=[_subject(), _second_subject()],
        validation_obligations=[
            _validation_obligation(),
            _second_validation_obligation(),
        ],
        work_groups=[
            _ecosystem_gate_work_group(),
            _second_ecosystem_gate_work_group(),
        ],
        evidence_expectations=[
            _evidence_expectation(),
            _second_evidence_expectation(),
        ],
        fact_snapshot_providers=[_fact_provider_with_second_subject()],
    )

    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )


def test_freeze_rejects_project_scoped_impact_without_direct_cause() -> None:
    """Every project-scoped impact cause needs direct provenance coverage."""
    classification = _classification_with_second_impact_for_same_subject()
    provenance = cast(
        "list[dict[str, object]]",
        classification["subject-selection-provenance"],
    )
    provenance[0]["source-impact-ids"] = ["impact-example"]

    with pytest.raises(ContractValidationError, match="direct provenance"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=classification,
            subjects=[_subject()],
            validation_obligations=[_validation_obligation()],
            work_groups=[_ecosystem_gate_work_group()],
            evidence_expectations=[_evidence_expectation()],
            fact_snapshot_providers=[_fact_provider()],
        )


def test_freeze_accepts_project_scoped_provenance_for_every_direct_cause() -> (
    None
):
    """Project-scoped impact causes can share one covered subject."""
    freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_classification_with_second_impact_for_same_subject(),
        subjects=[_subject()],
        validation_obligations=[_validation_obligation()],
        work_groups=[_ecosystem_gate_work_group()],
        evidence_expectations=[_evidence_expectation()],
        fact_snapshot_providers=[_fact_provider()],
    )


def test_freeze_rejects_targeted_project_without_direct_provenance() -> None:
    """Directly targeted project-scoped subjects require direct provenance."""
    edge = {
        "from-subject-id": "python.src-public-lib-example-tools",
        "to-subject-id": "python.src-public-lib-example",
        "relation": "workspace",
    }
    provider = _fact_provider_with_second_subject()
    provider["dependency-edges"] = [edge]
    classification = _classification_with_second_subject()
    provenance = cast(
        "list[dict[str, object]]",
        classification["subject-selection-provenance"],
    )
    provenance[1]["selection-kind"] = "downstream"
    provenance[1]["source-impact-ids"] = ["impact-example"]
    provenance[1]["direct-subject-id"] = "python.src-public-lib-example"
    provenance[1]["dependency-edge-basis"] = [edge]

    with pytest.raises(ContractValidationError, match="direct provenance"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=classification,
            subjects=[_subject(), _second_subject()],
            validation_obligations=[
                _validation_obligation(),
                _second_validation_obligation(),
            ],
            work_groups=[
                _ecosystem_gate_work_group(),
                _second_ecosystem_gate_work_group(),
            ],
            evidence_expectations=[
                _evidence_expectation(),
                _second_evidence_expectation(),
            ],
            fact_snapshot_providers=[provider],
        )


def test_freeze_accepts_targeted_project_with_direct_and_downstream() -> None:
    """A targeted subject can keep direct provenance plus downstream cause."""
    edge = {
        "from-subject-id": "python.src-public-lib-example-tools",
        "to-subject-id": "python.src-public-lib-example",
        "relation": "workspace",
    }
    provider = _fact_provider_with_second_subject()
    provider["dependency-edges"] = [edge]
    classification = _classification_with_second_subject()
    provenance = cast(
        "list[dict[str, object]]",
        classification["subject-selection-provenance"],
    )
    provenance.append(
        {
            "provenance-id": "prov-example-tools-downstream",
            "subject-id": "python.src-public-lib-example-tools",
            "selection-kind": "downstream",
            "source-impact-ids": ["impact-example"],
            "direct-subject-id": "python.src-public-lib-example",
            "dependency-edge-basis": [edge],
            "broad-expansion-id": None,
            "scheduled-full-source": False,
        },
    )

    freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=classification,
        subjects=[_subject(), _second_subject()],
        validation_obligations=[
            _validation_obligation(),
            _second_validation_obligation(),
        ],
        work_groups=[
            _ecosystem_gate_work_group(),
            _second_ecosystem_gate_work_group(),
        ],
        evidence_expectations=[
            _evidence_expectation(),
            _second_evidence_expectation(),
        ],
        fact_snapshot_providers=[provider],
    )


def test_validate_plan_rejects_mismatched_direct_project_provenance() -> None:
    """Direct project-scoped provenance cannot cite another subject's impact."""
    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_classification_with_second_subject(),
        subjects=[_subject(), _second_subject()],
        validation_obligations=[
            _validation_obligation(),
            _second_validation_obligation(),
        ],
        work_groups=[
            _ecosystem_gate_work_group(),
            _second_ecosystem_gate_work_group(),
        ],
        evidence_expectations=[
            _evidence_expectation(),
            _second_evidence_expectation(),
        ],
        fact_snapshot_providers=[_fact_provider_with_second_subject()],
    )
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    classification = cast("dict[str, object]", plan["classification"])
    provenance = cast(
        "list[dict[str, object]]",
        classification["subject-selection-provenance"],
    )
    provenance[1]["source-impact-ids"] = ["impact-example"]
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="project-scoped impacts"):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_validate_plan_accepts_valid_broad_expansion_scope() -> None:
    """Broad-expansion audit scope names known ecosystems and subjects."""
    snapshot = _plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    classification = cast("dict[str, object]", plan["classification"])
    classification["broad-expansions"] = [
        {
            "expansion-id": "expansion-python",
            "source-impact-id": "impact-example",
            "category": "ecosystem",
            "reason": "Expand to the Python ecosystem.",
            "resulting-scope": {
                "ecosystems": ["python"],
                "subjects": ["python.src-public-lib-example"],
                "descriptors": "none",
            },
        },
    ]
    _redigest_plan(plan)

    validate_ci_validation_plan(
        plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )


def test_validate_plan_rejects_invalid_broad_expansion_scope() -> None:
    """Broad-expansion scope arrays are canonical resolving references."""
    snapshot = _plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    classification = cast("dict[str, object]", plan["classification"])
    classification["broad-expansions"] = [
        {
            "expansion-id": "expansion-python",
            "source-impact-id": "impact-example",
            "category": "ecosystem",
            "reason": "Expand to the Python ecosystem.",
            "resulting-scope": {
                "ecosystems": ["python", "python"],
                "subjects": ["python.unknown"],
                "descriptors": "unknown",
            },
        },
    ]
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="resulting-scope"):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_validate_plan_rejects_subject_subsumption_kind() -> None:
    """Subsumption retained IDs resolve only in registered frozen namespaces."""
    snapshot = _plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    classification = cast("dict[str, object]", plan["classification"])
    classification["subsumptions"] = [
        {
            "subsumption-id": "subsumption-subject",
            "source-impact-ids": ["impact-example"],
            "source-expansion-ids": [],
            "subsumed-kind": "subject",
            "subsumed-candidate-ids": ["subject:candidate"],
            "retained-id": "python.src-public-lib-example",
            "reason": "Subject subsumption is not a frozen namespace.",
        },
    ]
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="subsumed-kind"):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


@pytest.mark.parametrize(
    ("subsumed_kind", "retained_id"),
    [
        ("detail-profile", "profile-tooling"),
        ("subject-selection-provenance", "prov-example"),
    ],
)
def test_validate_plan_accepts_supported_subsumption_kinds(
    subsumed_kind: str,
    retained_id: str,
) -> None:
    """Detail-profile and selection provenance are valid subsumption targets."""
    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_classification(),
        subjects=[_subject()],
        validation_obligations=[
            _validation_obligation(),
            _tooling_validation_obligation(),
        ],
        work_groups=[_ecosystem_gate_work_group(), _tooling_work_group()],
        evidence_expectations=[
            _evidence_expectation(),
            _tooling_evidence_expectation(),
        ],
        detail_profiles=[_tooling_detail_profile()],
        fact_snapshot_providers=[_fact_provider()],
    )
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    classification = cast("dict[str, object]", plan["classification"])
    classification["subsumptions"] = [
        {
            "subsumption-id": f"subsumption-{subsumed_kind}",
            "source-impact-ids": ["impact-example"],
            "source-expansion-ids": [],
            "subsumed-kind": subsumed_kind,
            "subsumed-candidate-ids": [f"{subsumed_kind}:candidate"],
            "retained-id": retained_id,
            "reason": "Fold duplicate planner candidates.",
        },
    ]
    _redigest_plan(plan)

    validate_ci_validation_plan(
        plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )


@pytest.mark.parametrize("reason", [None, ""])
def test_validate_plan_rejects_subsumption_without_reason(
    reason: object,
) -> None:
    """Subsumption records must explain why planner candidates were folded."""
    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_classification(),
        subjects=[_subject()],
        validation_obligations=[
            _validation_obligation(),
            _tooling_validation_obligation(),
        ],
        work_groups=[_ecosystem_gate_work_group(), _tooling_work_group()],
        evidence_expectations=[
            _evidence_expectation(),
            _tooling_evidence_expectation(),
        ],
        detail_profiles=[_tooling_detail_profile()],
        fact_snapshot_providers=[_fact_provider()],
    )
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    classification = cast("dict[str, object]", plan["classification"])
    record: dict[str, object] = {
        "subsumption-id": "subsumption-detail-profile",
        "source-impact-ids": ["impact-example"],
        "source-expansion-ids": [],
        "subsumed-kind": "detail-profile",
        "subsumed-candidate-ids": ["detail-profile:candidate"],
        "retained-id": "profile-tooling",
    }
    if reason is not None:
        record["reason"] = reason
    classification["subsumptions"] = [record]
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="reason"):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_freeze_rejects_wrong_runner_for_descriptor_ecosystem_work() -> None:
    """Executable work with an ecosystem uses that ecosystem runner family."""
    work_group = _descriptor_work_group()
    work_group["ecosystem"] = "dotnet"
    work_group["runner-family"] = "ubuntu"
    obligation = _descriptor_obligation()
    obligation["descriptor-scope"] = "ecosystem"

    with pytest.raises(ContractValidationError, match="runner-family"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[_descriptor_backed_subject()],
            validation_obligations=[_validation_obligation()],
            descriptor_obligations=[obligation],
            work_groups=sorted(
                [work_group, _ecosystem_gate_work_group()],
                key=lambda group: str(group["work-group-id"]),
            ),
            evidence_expectations=[
                _descriptor_evidence_expectation(),
                _evidence_expectation(),
            ],
            fact_snapshot_providers=[_descriptor_fact_provider()],
        )


@pytest.mark.parametrize(
    "kind",
    ["lightweight-preflight", "workflow-release-tooling"],
)
def test_freeze_rejects_non_null_ecosystem_for_non_ecosystem_work(
    kind: str,
) -> None:
    """Lightweight and ordinary tooling work never carry an ecosystem."""
    if kind == "lightweight-preflight":
        work_group = _lightweight_work_group()
        work_group["ecosystem"] = "python"
        classification = _lightweight_classification()
        subjects: list[dict[str, object]] = []
        validation = [_lightweight_validation_obligation()]
        evidence = [_lightweight_evidence_expectation()]
        detail_profiles = [
            _tooling_detail_profile(category="lightweight-preflight"),
        ]
        providers = [_empty_fact_provider()]
    else:
        work_group = _tooling_work_group()
        work_group["ecosystem"] = "python"
        classification = _classification()
        subjects = [_subject()]
        validation = [
            _validation_obligation(),
            _tooling_validation_obligation(),
        ]
        evidence = [_evidence_expectation(), _tooling_evidence_expectation()]
        detail_profiles = [_tooling_detail_profile()]
        providers = [_fact_provider()]

    with pytest.raises(ContractValidationError, match="ecosystem"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=classification,
            subjects=subjects,
            validation_obligations=validation,
            work_groups=sorted(
                [work_group, _ecosystem_gate_work_group()]
                if kind == "workflow-release-tooling"
                else [work_group],
                key=lambda group: str(group["work-group-id"]),
            ),
            evidence_expectations=evidence,
            detail_profiles=detail_profiles,
            fact_snapshot_providers=providers,
        )


def test_freeze_accepts_descriptor_ecosystem_when_scope_requires_it() -> None:
    """Ecosystem-scoped descriptor work carries the owning subject ecosystem."""
    work_group = _descriptor_work_group()
    work_group["ecosystem"] = "python"
    obligation = _descriptor_obligation()
    obligation["descriptor-scope"] = "ecosystem"

    freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_classification(),
        subjects=[_descriptor_backed_subject()],
        validation_obligations=[
            _artifact_validation_obligation(),
            _validation_obligation(),
        ],
        descriptor_obligations=[obligation],
        artifact_obligations=[_artifact_obligation()],
        work_groups=sorted(
            [_artifact_work_group(), work_group, _ecosystem_gate_work_group()],
            key=lambda group: str(group["work-group-id"]),
        ),
        evidence_expectations=[
            _artifact_evidence_expectation(),
            _descriptor_evidence_expectation(),
            _evidence_expectation(),
        ],
        fact_snapshot_providers=[_descriptor_fact_provider()],
    )


def test_freeze_rejects_descriptor_ecosystem_for_selected_scope() -> None:
    """Descriptor work without ecosystem-specific scope keeps ecosystem null."""
    work_group = _descriptor_work_group()
    work_group["ecosystem"] = "python"

    with pytest.raises(ContractValidationError, match="descriptor-scope"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[_descriptor_backed_subject()],
            validation_obligations=[_validation_obligation()],
            descriptor_obligations=[_descriptor_obligation()],
            work_groups=sorted(
                [work_group, _ecosystem_gate_work_group()],
                key=lambda group: str(group["work-group-id"]),
            ),
            evidence_expectations=[
                _descriptor_evidence_expectation(),
                _evidence_expectation(),
            ],
            fact_snapshot_providers=[_descriptor_fact_provider()],
        )


def test_freeze_rejects_descriptor_ecosystem_mismatched_owner() -> None:
    """Descriptor ecosystem work must match the owning subject ecosystem."""
    work_group = _descriptor_work_group()
    work_group["ecosystem"] = "dotnet"
    work_group["runner-family"] = "windows"
    obligation = _descriptor_obligation()
    obligation["descriptor-scope"] = "ecosystem"

    with pytest.raises(
        ContractValidationError,
        match="owning subject ecosystem",
    ):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[_descriptor_backed_subject()],
            validation_obligations=[
                _artifact_validation_obligation(),
                _validation_obligation(),
            ],
            descriptor_obligations=[obligation],
            artifact_obligations=[_artifact_obligation()],
            work_groups=sorted(
                [
                    _artifact_work_group(),
                    work_group,
                    _ecosystem_gate_work_group(),
                ],
                key=lambda group: str(group["work-group-id"]),
            ),
            evidence_expectations=[
                _artifact_evidence_expectation(),
                _descriptor_evidence_expectation(),
                _evidence_expectation(),
            ],
            fact_snapshot_providers=[_descriptor_fact_provider()],
        )


def _rich_plan_for_canonical_section_tests() -> dict[str, object]:
    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_classification(),
        subjects=[_descriptor_backed_subject()],
        validation_obligations=[
            _artifact_validation_obligation(),
            _validation_obligation(),
        ],
        descriptor_obligations=[_descriptor_obligation()],
        artifact_obligations=[_artifact_obligation()],
        work_groups=[
            _artifact_work_group(),
            _descriptor_work_group(),
            _ecosystem_gate_work_group(),
        ],
        evidence_expectations=[
            _artifact_evidence_expectation(),
            _descriptor_evidence_expectation(),
            _evidence_expectation(),
        ],
        detail_profiles=[
            _tooling_detail_profile(profile_id="profile-a"),
            _tooling_detail_profile(profile_id="profile-b"),
        ],
        fact_snapshot_providers=[_descriptor_fact_provider()],
    )
    return cast("dict[str, object]", deepcopy(snapshot.plan))


@pytest.mark.parametrize(
    ("section", "id_key"),
    [
        ("descriptor-obligations", "descriptor-obligation-id"),
        ("validation-obligations", "validation-obligation-id"),
        ("artifact-obligations", "artifact-obligation-id"),
        ("evidence-expectations", "evidence-expectation-id"),
        ("detail-profiles", "detail-profile-id"),
    ],
)
def test_validate_plan_rejects_duplicate_identifier_bearing_arrays(
    section: str,
    id_key: str,
) -> None:
    """Identifier-bearing plan arrays must remain unique after digesting."""
    plan = _rich_plan_for_canonical_section_tests()
    records = cast("list[dict[str, object]]", plan[section])
    duplicate = deepcopy(records[0])
    records.append(duplicate)
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match=id_key):
        validate_ci_validation_plan(plan)


@pytest.mark.parametrize(
    ("section", "error"),
    [
        ("descriptor-obligations", "descriptor-obligation-id"),
        ("validation-obligations", "validation-obligation-id"),
        ("artifact-obligations", "artifact-obligation-id"),
        ("evidence-expectations", "evidence-expectation-id"),
        ("detail-profiles", "canonical"),
    ],
)
def test_validate_plan_rejects_noncanonical_identifier_bearing_arrays(
    section: str,
    error: str,
) -> None:
    """Identifier-bearing plan arrays must remain in canonical order."""
    plan = _rich_plan_for_canonical_section_tests()
    records = cast("list[dict[str, object]]", plan[section])
    if len(records) == 1:
        extra = deepcopy(records[0])
        id_key = error
        extra[id_key] = f"{records[0][id_key]}-z"
        records.append(extra)
    assert records[1]
    records.reverse()
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match=error):
        validate_ci_validation_plan(plan)


@pytest.mark.parametrize(
    ("section", "id_key"),
    [
        ("descriptor-obligations", "descriptor-obligation-id"),
        ("validation-obligations", "validation-obligation-id"),
        ("artifact-obligations", "artifact-obligation-id"),
    ],
)
def test_validate_plan_rejects_missing_obligation_ids(
    section: str,
    id_key: str,
) -> None:
    """Obligation ids are required non-empty strings, not nullable fields."""
    plan = _rich_plan_for_canonical_section_tests()
    records = cast("list[dict[str, object]]", plan[section])
    records[0].pop(id_key)
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match=id_key):
        validate_ci_validation_plan(plan)


@pytest.mark.parametrize("value", [None, "", "validation-does-not-exist"])
def test_validate_plan_rejects_unresolved_artifact_validation_obligation_id(
    value: object,
) -> None:
    """Artifact obligation validation references are required and resolved."""
    plan = _rich_plan_for_canonical_section_tests()
    artifacts = cast("list[dict[str, object]]", plan["artifact-obligations"])
    artifacts[0]["validation-obligation-id"] = value
    _redigest_plan(plan)

    with pytest.raises(
        ContractValidationError,
        match="validation-obligation-id",
    ):
        validate_ci_validation_plan(plan)


def test_validate_plan_rejects_missing_downstream_edges_fact_snapshot() -> None:
    """Downstream selection edge bases resolve to fact snapshot edges."""
    edge = {
        "from-subject-id": "python.src-public-lib-example-tools",
        "to-subject-id": "python.src-public-lib-example",
        "relation": "workspace",
    }
    provider = _fact_provider()
    provider["subjects"] = [
        "python.src-public-lib-example",
        "python.src-public-lib-example-tools",
    ]
    provider["dependency-edges"] = [edge]
    classification = _classification_with_second_subject()
    provenance = cast(
        "list[dict[str, object]]",
        classification["subject-selection-provenance"],
    )
    provenance.append(
        {
            "provenance-id": "prov-example-tools-downstream",
            "subject-id": "python.src-public-lib-example-tools",
            "selection-kind": "downstream",
            "source-impact-ids": ["impact-example"],
            "direct-subject-id": "python.src-public-lib-example",
            "dependency-edge-basis": [edge],
            "broad-expansion-id": None,
            "scheduled-full-source": False,
        },
    )
    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=classification,
        subjects=[_subject(), _second_subject()],
        validation_obligations=[
            _validation_obligation(),
            _second_validation_obligation(),
        ],
        work_groups=[
            _ecosystem_gate_work_group(),
            _second_ecosystem_gate_work_group(),
        ],
        evidence_expectations=[
            _evidence_expectation(),
            _second_evidence_expectation(),
        ],
        fact_snapshot_providers=[provider],
    )
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    fact_snapshot = cast("dict[str, object]", deepcopy(snapshot.fact_snapshot))
    providers = cast("list[dict[str, object]]", fact_snapshot["providers"])
    providers[0]["dependency-edges"] = []
    fact_snapshot["fact-snapshot-id"] = ci_validation_fact_snapshot_id(
        providers,
    )
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="fact snapshot"):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=fact_snapshot,
        )


def test_freeze_plan_rejects_unselected_project_downstream_subject() -> None:
    """Project-scoped executable impacts select active downstream subjects."""
    edge = {
        "from-subject-id": "python.src-public-lib-example-tools",
        "to-subject-id": "python.src-public-lib-example",
        "relation": "workspace",
    }
    provider = _fact_provider_with_second_subject()
    provider["dependency-edges"] = [edge]
    second_subject = _second_subject()
    second_subject["selection-status"] = "not-selected"

    with pytest.raises(ContractValidationError, match="downstream subject"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[_subject(), second_subject],
            validation_obligations=[_validation_obligation()],
            work_groups=[_ecosystem_gate_work_group()],
            evidence_expectations=[_evidence_expectation()],
            fact_snapshot_providers=[provider],
        )


def test_freeze_plan_does_not_treat_dependencies_as_downstream() -> None:
    """Dependency edges select dependents, not dependencies, as downstream."""
    edge = {
        "from-subject-id": "python.src-public-lib-example-tools",
        "to-subject-id": "python.src-public-lib-example",
        "relation": "workspace",
    }
    provider = _fact_provider_with_second_subject()
    provider["dependency-edges"] = [edge]
    first_subject = _subject()
    first_subject["selection-status"] = "not-selected"
    classification = _classification()
    impacts = cast("list[dict[str, object]]", classification["impacts"])
    coverage_target = cast("dict[str, object]", impacts[0]["coverage-target"])
    coverage_target["id"] = "python.src-public-lib-example-tools"
    provenance = cast(
        "list[dict[str, object]]",
        classification["subject-selection-provenance"],
    )
    provenance[0]["subject-id"] = "python.src-public-lib-example-tools"

    freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=classification,
        subjects=[first_subject, _second_subject()],
        validation_obligations=[_second_validation_obligation()],
        work_groups=[_second_ecosystem_gate_work_group()],
        evidence_expectations=[_second_evidence_expectation()],
        fact_snapshot_providers=[provider],
    )


def test_freeze_plan_rejects_direct_project_downstream_provenance() -> None:
    """Downstream project closure requires downstream provenance."""
    edge = {
        "from-subject-id": "python.src-public-lib-example-tools",
        "to-subject-id": "python.src-public-lib-example",
        "relation": "workspace",
    }
    provider = _fact_provider_with_second_subject()
    provider["dependency-edges"] = [edge]

    with pytest.raises(ContractValidationError, match="downstream provenance"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification_with_second_subject(),
            subjects=[_subject(), _second_subject()],
            validation_obligations=[
                _validation_obligation(),
                _second_validation_obligation(),
            ],
            work_groups=[
                _ecosystem_gate_work_group(),
                _second_ecosystem_gate_work_group(),
            ],
            evidence_expectations=[
                _evidence_expectation(),
                _second_evidence_expectation(),
            ],
            fact_snapshot_providers=[provider],
        )


def test_freeze_rejects_downstream_project_impact_without_cause() -> None:
    """Downstream project-scoped impact causes all need provenance coverage."""
    edge = {
        "from-subject-id": "python.src-public-lib-example-tools",
        "to-subject-id": "python.src-public-lib-example",
        "relation": "workspace",
    }
    provider = _fact_provider_with_second_subject()
    provider["dependency-edges"] = [edge]
    classification = _classification_with_second_impact_for_same_subject()
    provenance = cast(
        "list[dict[str, object]]",
        classification["subject-selection-provenance"],
    )
    provenance.append(
        {
            "provenance-id": "prov-example-tools-downstream",
            "subject-id": "python.src-public-lib-example-tools",
            "selection-kind": "downstream",
            "source-impact-ids": ["impact-example"],
            "direct-subject-id": "python.src-public-lib-example",
            "dependency-edge-basis": [edge],
            "broad-expansion-id": None,
            "scheduled-full-source": False,
        },
    )

    with pytest.raises(ContractValidationError, match="downstream provenance"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=classification,
            subjects=[_subject(), _second_subject()],
            validation_obligations=[
                _validation_obligation(),
                _second_validation_obligation(),
            ],
            work_groups=[
                _ecosystem_gate_work_group(),
                _second_ecosystem_gate_work_group(),
            ],
            evidence_expectations=[
                _evidence_expectation(),
                _second_evidence_expectation(),
            ],
            fact_snapshot_providers=[provider],
        )


def test_freeze_plan_accepts_transitive_project_downstream_closure() -> None:
    """Project-scoped downstream closure follows transitive fact edges."""
    first_edge = {
        "from-subject-id": "python.src-public-lib-example-tools",
        "to-subject-id": "python.src-public-lib-example",
        "relation": "workspace",
    }
    second_edge = {
        "from-subject-id": "python.src-public-lib-example-tools-extra",
        "to-subject-id": "python.src-public-lib-example-tools",
        "relation": "workspace",
    }
    provider = _fact_provider_with_second_subject()
    roots = cast("list[str]", provider["roots"])
    subjects = cast("list[str]", provider["subjects"])
    roots.append("src/public/lib/example-tools-extra")
    subjects.append("python.src-public-lib-example-tools-extra")
    provider["dependency-edges"] = [first_edge, second_edge]
    classification = _classification_with_second_subject()
    provenance = cast(
        "list[dict[str, object]]",
        classification["subject-selection-provenance"],
    )
    provenance.append(
        {
            "provenance-id": "prov-example-tools-downstream",
            "subject-id": "python.src-public-lib-example-tools",
            "selection-kind": "downstream",
            "source-impact-ids": ["impact-example"],
            "direct-subject-id": "python.src-public-lib-example",
            "dependency-edge-basis": [first_edge],
            "broad-expansion-id": None,
            "scheduled-full-source": False,
        },
    )
    provenance.append(
        {
            "provenance-id": "prov-example-tools-extra",
            "subject-id": "python.src-public-lib-example-tools-extra",
            "selection-kind": "downstream",
            "source-impact-ids": ["impact-example"],
            "direct-subject-id": "python.src-public-lib-example",
            "dependency-edge-basis": [first_edge, second_edge],
            "broad-expansion-id": None,
            "scheduled-full-source": False,
        },
    )
    provenance.append(
        {
            "provenance-id": "prov-example-tools-extra-from-tools",
            "subject-id": "python.src-public-lib-example-tools-extra",
            "selection-kind": "downstream",
            "source-impact-ids": ["impact-example-tools"],
            "direct-subject-id": "python.src-public-lib-example-tools",
            "dependency-edge-basis": [second_edge],
            "broad-expansion-id": None,
            "scheduled-full-source": False,
        },
    )

    freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=classification,
        subjects=[_subject(), _second_subject(), _third_subject()],
        validation_obligations=[
            _validation_obligation(),
            _third_validation_obligation(),
            _second_validation_obligation(),
        ],
        work_groups=[
            _ecosystem_gate_work_group(),
            _third_ecosystem_gate_work_group(),
            _second_ecosystem_gate_work_group(),
        ],
        evidence_expectations=[
            _evidence_expectation(),
            _third_evidence_expectation(),
            _second_evidence_expectation(),
        ],
        fact_snapshot_providers=[provider],
    )


def test_freeze_rejects_missing_transitive_project_downstream() -> None:
    """Project-scoped downstream closure cannot omit transitive dependents."""
    first_edge = {
        "from-subject-id": "python.src-public-lib-example-tools",
        "to-subject-id": "python.src-public-lib-example",
        "relation": "workspace",
    }
    second_edge = {
        "from-subject-id": "python.src-public-lib-example-tools-extra",
        "to-subject-id": "python.src-public-lib-example-tools",
        "relation": "workspace",
    }
    provider = _fact_provider_with_second_subject()
    roots = cast("list[str]", provider["roots"])
    subjects = cast("list[str]", provider["subjects"])
    roots.append("src/public/lib/example-tools-extra")
    subjects.append("python.src-public-lib-example-tools-extra")
    provider["dependency-edges"] = [first_edge, second_edge]
    classification = _classification_with_second_subject()
    provenance = cast(
        "list[dict[str, object]]",
        classification["subject-selection-provenance"],
    )
    provenance.append(
        {
            "provenance-id": "prov-example-tools-downstream",
            "subject-id": "python.src-public-lib-example-tools",
            "selection-kind": "downstream",
            "source-impact-ids": ["impact-example"],
            "direct-subject-id": "python.src-public-lib-example",
            "dependency-edge-basis": [first_edge],
            "broad-expansion-id": None,
            "scheduled-full-source": False,
        },
    )
    third_subject = _third_subject()
    third_subject["selection-status"] = "not-selected"

    with pytest.raises(ContractValidationError, match="downstream subject"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=classification,
            subjects=[_subject(), _second_subject(), third_subject],
            validation_obligations=[
                _validation_obligation(),
                _second_validation_obligation(),
            ],
            work_groups=[
                _ecosystem_gate_work_group(),
                _second_ecosystem_gate_work_group(),
            ],
            evidence_expectations=[
                _evidence_expectation(),
                _second_evidence_expectation(),
            ],
            fact_snapshot_providers=[provider],
        )


def test_validate_plan_rejects_terminal_aggregation_target_drift() -> None:
    """Terminal aggregation coverage target is fixed and exact."""
    plan = cast("dict[str, object]", deepcopy(_plan_snapshot().plan))
    work_groups = cast("list[dict[str, object]]", plan["work-groups"])
    terminal = next(
        group
        for group in work_groups
        if group["kind"] == "evidence-aggregation"
    )
    terminal["coverage-target"] = {"type": "global", "id": None}
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="aggregate"):
        validate_ci_validation_plan(plan)


def test_unavailable_provider_rejects_authoritative_facts() -> None:
    """Unavailable providers cannot carry authoritative fact records."""
    provider = _fact_provider()
    provider["status"] = "unavailable"
    provider["diagnostics"] = [_valid_fail_closed_diagnostic()]

    with pytest.raises(ContractValidationError, match="must be empty"):
        ci_validation_fact_snapshot_id([provider])


def test_unavailable_provider_requires_diagnostics() -> None:
    """Unavailable providers must explain why facts are absent."""
    provider = _fact_provider()
    provider["status"] = "unavailable"
    provider["roots"] = []
    provider["subjects"] = []

    with pytest.raises(ContractValidationError, match="non-empty"):
        ci_validation_fact_snapshot_id([provider])


def test_executable_plan_rejects_unavailable_provider() -> None:
    """Executable plans must not bind to partially unavailable facts."""
    provider = _empty_fact_provider()
    provider["status"] = "unavailable"
    provider["diagnostics"] = [_valid_fail_closed_diagnostic()]

    with pytest.raises(ContractValidationError, match="unavailable providers"):
        freeze_ci_validation_plan(
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
            fact_snapshot_providers=[provider],
        )


def test_validate_executable_plan_rejects_unavailable_provider() -> None:
    """Standalone validation also rejects unavailable companion providers."""
    snapshot = _plan_snapshot()
    fact_snapshot = cast(
        "dict[str, object]",
        deepcopy(snapshot.fact_snapshot),
    )
    provider = _empty_fact_provider()
    provider["status"] = "unavailable"
    provider["diagnostics"] = [_valid_fail_closed_diagnostic()]
    providers = [provider]
    fact_snapshot["providers"] = providers
    fact_snapshot["fact-snapshot-id"] = ci_validation_fact_snapshot_id(
        providers,
    )
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    fact_envelope = cast("dict[str, object]", plan["fact-snapshot"])
    fact_envelope["id"] = fact_snapshot["fact-snapshot-id"]
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="unavailable providers"):
        validate_ci_validation_plan(plan, fact_snapshot=fact_snapshot)


def test_fail_closed_plan_allows_unavailable_provider() -> None:
    """Fail-closed plans may carry unavailable-provider diagnostics."""
    provider = _empty_fact_provider()
    provider["status"] = "unavailable"
    provider["diagnostics"] = [_valid_fail_closed_diagnostic()]

    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(changed_files=[]),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="fail-closed",
        diagnostics=[_valid_fail_closed_diagnostic()],
        fact_snapshot_providers=[provider],
    )

    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )


def test_fail_closed_plan_rejects_changed_file_coverage_gap() -> None:
    """Fail-closed plans still cover available changed-file snapshots."""
    empty = freeze_ci_validation_plan(
        request=_normalized_request(changed_files=[]),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="fail-closed",
        diagnostics=[_valid_fail_closed_diagnostic()],
        fact_snapshot_providers=None,
    )
    changed_files_snapshot = _plan_snapshot().changed_files_snapshot
    assert changed_files_snapshot is not None
    plan = cast("dict[str, object]", deepcopy(empty.plan))
    affected_range = cast("dict[str, object]", plan["affected-range"])
    affected_range["changed-files-hash"] = changed_files_snapshot[
        "changed-files-hash"
    ]
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="changed files"):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=empty.fact_snapshot,
        )


@pytest.mark.parametrize(
    ("section", "record_index"),
    [
        ("validation-obligations", 0),
        ("descriptor-obligations", 0),
        ("artifact-obligations", 0),
    ],
)
def test_obligation_source_impacts_must_resolve_to_classification(
    section: str,
    record_index: int,
) -> None:
    """Plan obligations cite canonical classification impact IDs."""
    plan = _rich_plan_for_canonical_section_tests()
    records = cast("list[dict[str, object]]", plan[section])
    records[record_index]["source-impact-ids"] = ["missing-impact"]
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="must resolve to impact"):
        validate_ci_validation_plan(plan)


def test_obligation_source_impacts_are_empty_only_for_scheduled_full() -> None:
    """Affected plans cannot use empty obligation impact provenance."""
    plan = cast("dict[str, object]", deepcopy(_plan_snapshot().plan))
    obligations = cast(
        "list[dict[str, object]]",
        plan["validation-obligations"],
    )
    obligations[0]["source-impact-ids"] = []
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="ordered uniquely"):
        validate_ci_validation_plan(plan)


def test_scheduled_full_plan_uses_scheduled_selection_provenance() -> None:
    """Scheduled plans carry scheduled provenance, not impact provenance."""
    snapshot = _scheduled_full_plan_snapshot()

    assert snapshot.changed_files_snapshot is None
    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )


def test_scheduled_full_plan_rejects_classification_impacts() -> None:
    """Scheduled-full plans cannot carry changed-file impact records."""
    plan = cast(
        "dict[str, object]",
        deepcopy(_scheduled_full_plan_snapshot().plan),
    )
    classification = cast("dict[str, object]", plan["classification"])
    classification["impacts"] = _classification()["impacts"]
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="scheduled-full plans"):
        validate_ci_validation_plan(plan)


def test_scheduled_full_plan_rejects_impact_selection_kind() -> None:
    """Scheduled-full subject provenance must use scheduled-full selection."""
    plan = cast(
        "dict[str, object]",
        deepcopy(_scheduled_full_plan_snapshot().plan),
    )
    classification = cast("dict[str, object]", plan["classification"])
    provenance = cast(
        "list[dict[str, object]]",
        classification["subject-selection-provenance"],
    )
    provenance[0]["selection-kind"] = "direct"
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="scheduled mode"):
        validate_ci_validation_plan(plan)


@pytest.mark.parametrize(
    ("field", "value", "expected"),
    [
        ("source-impact-ids", ["impact-example"], "source-impact-ids"),
        (
            "direct-subject-id",
            "python.src-public-lib-example",
            "direct-subject-id",
        ),
        ("broad-expansion-id", "expansion-example", "broad-expansion-id"),
        (
            "dependency-edge-basis",
            [
                {
                    "from-subject-id": "python.src-public-lib-example",
                    "to-subject-id": "python.src-public-lib-example",
                    "relation": "workspace",
                },
            ],
            "dependency-edge-basis",
        ),
        ("scheduled-full-source", False, "scheduled-full-source"),
    ],
)
def test_scheduled_full_plan_rejects_impact_provenance_fields(
    field: str,
    value: object,
    expected: str,
) -> None:
    """Scheduled-full provenance is isolated from changed-file derivation."""
    plan = cast(
        "dict[str, object]",
        deepcopy(_scheduled_full_plan_snapshot().plan),
    )
    classification = cast("dict[str, object]", plan["classification"])
    provenance = cast(
        "list[dict[str, object]]",
        classification["subject-selection-provenance"],
    )
    provenance[0][field] = value
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match=expected):
        validate_ci_validation_plan(plan)


def test_scheduled_full_plan_rejects_unselected_active_subject() -> None:
    """Scheduled-full runs must select every active validation subject."""
    plan = cast(
        "dict[str, object]",
        deepcopy(_scheduled_full_plan_snapshot().plan),
    )
    subjects = cast("list[dict[str, object]]", plan["subjects"])
    subjects[0]["selection-status"] = "not-selected"
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="select every active"):
        validate_ci_validation_plan(plan)


def test_scheduled_full_plan_rejects_missing_active_subject_provenance() -> (
    None
):
    """Scheduled-full provenance covers the active subject set."""
    plan = cast(
        "dict[str, object]",
        deepcopy(_scheduled_full_plan_snapshot().plan),
    )
    classification = cast("dict[str, object]", plan["classification"])
    classification["subject-selection-provenance"] = []
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="every active selected"):
        validate_ci_validation_plan(plan)


def test_scheduled_full_plan_requires_every_closed_tooling_surface() -> None:
    """Scheduled-full plans cover the closed workflow-release tooling scope."""
    plan = cast(
        "dict[str, object]",
        deepcopy(_scheduled_full_plan_snapshot().plan),
    )
    surface = "planner"
    work_group_id = f"wg-tooling-{surface}"
    for section_name in (
        "work-groups",
        "evidence-expectations",
        "validation-obligations",
        "detail-profiles",
    ):
        records = cast("list[dict[str, object]]", plan[section_name])
        records[:] = [
            record
            for record in records
            if record.get("work-group-id") != work_group_id
            and record.get("detail-profile-id") != f"profile-tooling-{surface}"
        ]
    work_groups = cast("list[dict[str, object]]", plan["work-groups"])
    for group in work_groups:
        if group.get("kind") == "evidence-aggregation":
            depends_on = cast("list[str]", group["depends-on"])
            depends_on[:] = [
                dependency
                for dependency in depends_on
                if dependency != work_group_id
            ]
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="closed tooling surface"):
        validate_ci_validation_plan(plan)


def test_scheduled_full_plan_requires_every_discovered_descriptor() -> None:
    """Scheduled-full plans validate all descriptor facts, including tooling."""
    snapshot = _scheduled_full_plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    descriptor_obligations = cast(
        "list[dict[str, object]]",
        plan["descriptor-obligations"],
    )
    descriptor_obligations.clear()
    _redigest_plan(plan)

    with pytest.raises(
        ContractValidationError,
        match="every discovered descriptor",
    ):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_scheduled_full_descriptor_obligation_requires_work_and_evidence() -> (
    None
):
    """Full-scope descriptor obligations bind one-to-one to executable work."""
    snapshot = _scheduled_full_plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    work_group_id = "wg-descriptor-workflow-release"
    for section_name in ("work-groups", "evidence-expectations"):
        records = cast("list[dict[str, object]]", plan[section_name])
        records[:] = [
            record
            for record in records
            if record.get("work-group-id") != work_group_id
        ]
    work_groups = cast("list[dict[str, object]]", plan["work-groups"])
    for group in work_groups:
        if group.get("kind") == "evidence-aggregation":
            depends_on = cast("list[str]", group["depends-on"])
            depends_on[:] = [
                dependency
                for dependency in depends_on
                if dependency != work_group_id
            ]
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="one-to-one"):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_global_impact_affected_plan_requires_full_tooling_scope() -> None:
    """Global affected plans cover the scheduled-full-equivalent tool scope."""
    plan = cast(
        "dict[str, object]",
        deepcopy(_global_full_scope_plan_snapshot().plan),
    )
    surface = "planner"
    work_group_id = f"wg-tooling-{surface}"
    for section_name in (
        "work-groups",
        "evidence-expectations",
        "validation-obligations",
        "detail-profiles",
    ):
        records = cast("list[dict[str, object]]", plan[section_name])
        records[:] = [
            record
            for record in records
            if record.get("work-group-id") != work_group_id
            and record.get("detail-profile-id") != f"profile-tooling-{surface}"
        ]
    work_groups = cast("list[dict[str, object]]", plan["work-groups"])
    for group in work_groups:
        if group.get("kind") == "evidence-aggregation":
            depends_on = cast("list[str]", group["depends-on"])
            depends_on[:] = [
                dependency
                for dependency in depends_on
                if dependency != work_group_id
            ]
    _redigest_plan(plan)

    with pytest.raises(
        ContractValidationError,
        match="scheduled-full-equivalent",
    ):
        validate_ci_validation_plan(plan)


def test_global_impact_affected_plan_requires_workflow_descriptor() -> None:
    """Global affected plans include null-owner workflow-release descriptors."""
    snapshot = _global_full_scope_plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    descriptor_obligations = cast(
        "list[dict[str, object]]",
        plan["descriptor-obligations"],
    )
    descriptor_obligations.clear()
    _redigest_plan(plan)

    with pytest.raises(
        ContractValidationError,
        match="every discovered descriptor",
    ):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_global_impact_affected_plan_accepts_full_scope_provenance() -> None:
    """Global affected plans keep impact provenance with full scope."""
    snapshot = _global_full_scope_plan_snapshot()

    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    classification = cast("dict[str, object]", snapshot.plan["classification"])
    provenance = cast(
        "list[dict[str, object]]",
        classification["subject-selection-provenance"],
    )
    assert provenance[0]["selection-kind"] == "direct"
    assert provenance[0]["scheduled-full-source"] is False


def test_global_full_scope_workflow_descriptor_accepts_global_impact() -> None:
    """Full-scope workflow descriptors may cite the global triggering impact."""
    snapshot = _global_full_scope_plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    obligations = cast(
        "list[dict[str, object]]",
        plan["descriptor-obligations"],
    )
    obligations[0]["source-impact-ids"] = ["impact-example"]
    _redigest_plan(plan)

    validate_ci_validation_plan(
        plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )


def test_tooling_specific_workflow_descriptor_rejects_global_impact() -> None:
    """Tooling-specific workflow descriptors still cite tooling impacts only."""
    snapshot = _global_full_scope_plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    obligations = cast(
        "list[dict[str, object]]",
        plan["descriptor-obligations"],
    )
    obligations[0]["descriptor-scope"] = "selected"
    obligations[0]["source-impact-ids"] = ["impact-example"]
    _redigest_plan(plan)

    with pytest.raises(
        ContractValidationError,
        match="tooling-surface impacts",
    ):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_global_impact_affected_plan_selects_every_active_subject() -> None:
    """Global affected plans use full active-subject scope."""
    plan = cast(
        "dict[str, object]",
        deepcopy(_global_full_scope_plan_snapshot().plan),
    )
    subjects = cast("list[dict[str, object]]", plan["subjects"])
    subjects[0]["selection-status"] = "not-selected"
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="select every active"):
        validate_ci_validation_plan(plan)


def test_workflow_release_infrastructure_requires_tooling_chain() -> None:
    """Infrastructure impacts require validation for the affected surface."""
    snapshot = _plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    plan["classification"] = _workflow_release_infrastructure_classification()
    _redigest_plan(plan)

    with pytest.raises(
        ContractValidationError,
        match="workflow-release-tooling validation",
    ):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_workflow_release_infrastructure_requires_surface_expansion() -> None:
    """Infrastructure impact expansions identify deterministic surface scope."""
    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_workflow_release_infrastructure_classification(),
        subjects=[_subject()],
        validation_obligations=[
            _validation_obligation(),
            _tooling_validation_obligation(),
        ],
        work_groups=[_ecosystem_gate_work_group(), _tooling_work_group()],
        evidence_expectations=[
            _evidence_expectation(),
            _tooling_evidence_expectation(),
        ],
        detail_profiles=[_tooling_detail_profile()],
        fact_snapshot_providers=[_fact_provider()],
    )
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    classification = cast("dict[str, object]", plan["classification"])
    classification["broad-expansions"] = []
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="surface-specific"):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


@pytest.mark.parametrize(
    "surface",
    [
        "planner",
        "classifier",
        "workflow-release-contract",
        "workflow-orchestration",
    ],
)
def test_scheduled_full_equivalent_infrastructure_impact_requires_full_scope(
    surface: str,
) -> None:
    """LLD 10.3 surfaces use scheduled-full-equivalent validation scope."""
    snapshot = _global_full_scope_plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    classification = cast("dict[str, object]", plan["classification"])
    classification.update(
        _workflow_release_infrastructure_classification(
            surface=surface,
            descriptors="all-discovered",
        ),
    )
    subjects = cast("list[dict[str, object]]", plan["subjects"])
    subjects[0]["selection-status"] = "not-selected"
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="select every active"):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


@pytest.mark.parametrize(
    "surface",
    [
        "planner",
        "classifier",
        "workflow-release-contract",
        "workflow-orchestration",
    ],
)
def test_scheduled_full_equivalent_infrastructure_requires_full_tooling_scope(
    surface: str,
) -> None:
    """LLD 10.3 infrastructure changes cover every closed tooling surface."""
    snapshot = _global_full_scope_plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    classification = cast("dict[str, object]", plan["classification"])
    classification.update(
        _workflow_release_infrastructure_classification(
            surface=surface,
            descriptors="all-discovered",
        ),
    )
    omitted_surface = "planner"
    work_group_id = f"wg-tooling-{omitted_surface}"
    for section_name in (
        "work-groups",
        "evidence-expectations",
        "validation-obligations",
        "detail-profiles",
    ):
        records = cast("list[dict[str, object]]", plan[section_name])
        records[:] = [
            record
            for record in records
            if record.get("work-group-id") != work_group_id
            and record.get("detail-profile-id")
            != f"profile-tooling-{omitted_surface}"
        ]
    work_groups = cast("list[dict[str, object]]", plan["work-groups"])
    for group in work_groups:
        if group.get("kind") == "evidence-aggregation":
            depends_on = cast("list[str]", group["depends-on"])
            depends_on[:] = [
                dependency
                for dependency in depends_on
                if dependency != work_group_id
            ]
    _redigest_plan(plan)

    with pytest.raises(
        ContractValidationError,
        match="scheduled-full-equivalent",
    ):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


@pytest.mark.parametrize(
    "surface",
    [
        "planner",
        "classifier",
        "workflow-release-contract",
        "workflow-orchestration",
    ],
)
def test_scheduled_full_equivalent_infrastructure_accepts_full_scope(
    surface: str,
) -> None:
    """LLD 10.3 surfaces accept scheduled-full-equivalent validation scope."""
    snapshot = _global_full_scope_plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    classification = cast("dict[str, object]", plan["classification"])
    classification.update(
        _workflow_release_infrastructure_classification(
            surface=surface,
            descriptors="all-discovered",
        ),
    )
    _redigest_plan(plan)

    validate_ci_validation_plan(
        plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )


def test_authoring_validation_infra_requires_descriptor_scope() -> None:
    """LLD 10.3 authoring changes validate all discovered descriptors."""
    snapshot = _global_full_scope_plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    classification = cast("dict[str, object]", plan["classification"])
    classification.update(
        _workflow_release_infrastructure_classification(
            surface="authoring-validation",
            descriptors="all-discovered",
        ),
    )
    descriptor_obligations = cast(
        "list[dict[str, object]]",
        plan["descriptor-obligations"],
    )
    descriptor_obligations.clear()
    _redigest_plan(plan)

    with pytest.raises(
        ContractValidationError,
        match="workflow-release infrastructure scope",
    ):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_authoring_validation_infrastructure_accepts_descriptor_scope() -> None:
    """LLD 10.3 descriptor-scoped infrastructure impact passes."""
    snapshot = _global_full_scope_plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    classification = cast("dict[str, object]", plan["classification"])
    classification.update(
        _workflow_release_infrastructure_classification(
            surface="authoring-validation",
            descriptors="all-discovered",
        ),
    )
    _redigest_plan(plan)

    validate_ci_validation_plan(
        plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )


def test_fact_provider_infra_selects_active_provider_subjects() -> None:
    """Fact-provider scope covers all active provider subjects."""
    second_subject = _second_subject()
    capabilities = cast("dict[str, bool]", second_subject["capabilities"])
    capabilities["build"] = False
    capabilities["type-check"] = False
    classification = _workflow_release_infrastructure_classification(
        surface="fact-provider",
    )
    provenance = cast(
        "list[dict[str, object]]",
        classification["subject-selection-provenance"],
    )
    provenance.append(
        {
            "provenance-id": "prov-example-tools",
            "subject-id": "python.src-public-lib-example-tools",
            "selection-kind": "broad-expansion",
            "source-impact-ids": ["impact-example"],
            "direct-subject-id": None,
            "dependency-edge-basis": [],
            "broad-expansion-id": "expansion-fact-provider",
            "scheduled-full-source": False,
        },
    )
    expansions = cast(
        "list[dict[str, object]]",
        classification["broad-expansions"],
    )
    resulting_scope = cast(
        "dict[str, object]",
        expansions[0]["resulting-scope"],
    )
    resulting_scope["subjects"] = [
        "python.src-public-lib-example",
        "python.src-public-lib-example-tools",
    ]
    second_group = _second_ecosystem_gate_work_group()
    second_expected = cast(
        "dict[str, object]",
        second_group["expected-evidence"],
    )
    second_expected["planned-capabilities"] = ["test"]
    second_evidence = _second_evidence_expectation()
    second_evidence["planned-capabilities"] = ["test"]
    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=classification,
        subjects=[_subject(), second_subject],
        validation_obligations=[
            _validation_obligation(),
            _second_validation_obligation(),
            _tooling_validation_obligation(surface="fact-provider"),
        ],
        work_groups=[
            _ecosystem_gate_work_group(),
            second_group,
            _tooling_work_group(surface="fact-provider"),
        ],
        evidence_expectations=[
            _evidence_expectation(),
            second_evidence,
            _tooling_evidence_expectation(surface="fact-provider"),
        ],
        detail_profiles=[_tooling_detail_profile(surface="fact-provider")],
        fact_snapshot_providers=[_fact_provider_with_second_subject()],
    )
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    subjects = cast("list[dict[str, object]]", plan["subjects"])
    for subject in subjects:
        if subject.get("subject-id") == "python.src-public-lib-example-tools":
            subject["selection-status"] = "not-selected"
    plan["subject-universe"] = {
        "status": "available",
        "id": ci_validation_subject_universe_id(subjects),
    }
    mutated_classification = cast("dict[str, object]", plan["classification"])
    mutated_provenance = cast(
        "list[dict[str, object]]",
        mutated_classification["subject-selection-provenance"],
    )
    mutated_provenance[:] = [
        record
        for record in mutated_provenance
        if record.get("subject-id") != "python.src-public-lib-example-tools"
    ]
    _redigest_plan(plan)

    with pytest.raises(
        ContractValidationError,
        match="active provider-bound",
    ):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_fact_provider_infra_rejects_undetermined_ecosystem_scope() -> None:
    """Fact-provider impacts must determine affected provider ecosystems."""
    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_workflow_release_infrastructure_classification(
            surface="fact-provider",
        ),
        subjects=[_subject()],
        validation_obligations=[
            _validation_obligation(),
            _tooling_validation_obligation(surface="fact-provider"),
        ],
        work_groups=[
            _ecosystem_gate_work_group(),
            _tooling_work_group(surface="fact-provider"),
        ],
        evidence_expectations=[
            _evidence_expectation(),
            _tooling_evidence_expectation(surface="fact-provider"),
        ],
        detail_profiles=[_tooling_detail_profile(surface="fact-provider")],
        fact_snapshot_providers=[_fact_provider()],
    )
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    classification = cast("dict[str, object]", plan["classification"])
    expansions = cast(
        "list[dict[str, object]]",
        classification["broad-expansions"],
    )
    resulting_scope = cast(
        "dict[str, object]",
        expansions[0]["resulting-scope"],
    )
    resulting_scope["ecosystems"] = []
    _redigest_plan(plan)

    with pytest.raises(
        ContractValidationError,
        match="determine affected ecosystems",
    ):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_target_catalog_infrastructure_requires_artifact_scope() -> None:
    """LLD 10.3 target-catalog changes cover descriptor-backed artifacts."""
    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=_workflow_release_infrastructure_classification(
            surface="target-catalog",
            descriptors="selected",
        ),
        subjects=[_descriptor_backed_subject()],
        validation_obligations=[
            _artifact_validation_obligation(),
            _validation_obligation(),
            _tooling_validation_obligation(surface="target-catalog"),
        ],
        descriptor_obligations=[_descriptor_obligation()],
        artifact_obligations=[_artifact_obligation()],
        work_groups=[
            _artifact_work_group(),
            _descriptor_work_group(),
            _ecosystem_gate_work_group(),
            _tooling_work_group(surface="target-catalog"),
        ],
        evidence_expectations=[
            _artifact_evidence_expectation(),
            _descriptor_evidence_expectation(),
            _evidence_expectation(),
            _tooling_evidence_expectation(surface="target-catalog"),
        ],
        detail_profiles=[_tooling_detail_profile(surface="target-catalog")],
        fact_snapshot_providers=[_descriptor_fact_provider()],
    )
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    artifact_obligations = cast(
        "list[dict[str, object]]",
        plan["artifact-obligations"],
    )
    artifact_obligations.clear()
    _redigest_plan(plan)

    with pytest.raises(
        ContractValidationError,
        match="artifact scope",
    ):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_known_non_impacting_impact_set_requires_lightweight_only() -> None:
    """All known-non-impacting impact sets cannot carry normal validation."""
    snapshot = _plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    classification = cast("dict[str, object]", plan["classification"])
    impacts = cast("list[dict[str, object]]", classification["impacts"])
    impacts[0]["category"] = "known-non-impacting"
    impacts[0]["coverage-target"] = {"type": "none", "id": None}
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="lightweight-only"):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


@pytest.mark.parametrize(
    ("section", "field"),
    [
        ("validation-obligations", "required"),
        ("validation-obligations", "blocking"),
        ("descriptor-obligations", "required"),
        ("descriptor-obligations", "blocking"),
        ("artifact-obligations", "required"),
        ("artifact-obligations", "blocking"),
    ],
)
def test_executable_obligations_are_verdict_relevant(
    section: str,
    field: str,
) -> None:
    """Executable obligations are all required and blocking."""
    plan = _rich_plan_for_canonical_section_tests()
    records = cast("list[dict[str, object]]", plan[section])
    records[0][field] = False
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="must be true"):
        validate_ci_validation_plan(plan)


@pytest.mark.parametrize(
    "section",
    ["descriptor-obligations", "artifact-obligations"],
)
def test_special_obligations_require_binding_even_when_not_required(
    section: str,
) -> None:
    """Descriptor/artifact obligations cannot opt out of bindings."""
    plan = _rich_plan_for_canonical_section_tests()
    records = cast("list[dict[str, object]]", plan[section])
    records[0]["required"] = False
    records[0]["work-group-id"] = None
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="work-group-id"):
        validate_ci_validation_plan(plan)


def test_unsupported_subject_rejected_from_provider_facts() -> None:
    """Unsupported ecosystems are isolated from provider fact subjects."""
    provider = _fact_provider()
    provider["subjects"] = [
        "python.src-public-lib-example",
        "ruby.unsupported-example",
    ]

    with pytest.raises(ContractValidationError, match="provider"):
        freeze_ci_validation_plan(
            request=_normalized_request(),
            plan_id=PLAN_ID,
            created_at=CREATED_AT,
            observed_commit_sha=TREE_SHA,
            verdict_intent="executable",
            classification=_classification(),
            subjects=[_subject(), _unsupported_subject()],
            validation_obligations=[_validation_obligation()],
            work_groups=[_ecosystem_gate_work_group()],
            evidence_expectations=[_evidence_expectation()],
            fact_snapshot_providers=[provider],
        )


def test_unsupported_subject_rejected_from_coverage_targets() -> None:
    """Work, evidence, and obligations cannot target unsupported subjects."""
    plan = cast("dict[str, object]", deepcopy(_plan_snapshot().plan))
    subjects = cast("list[dict[str, object]]", plan["subjects"])
    subjects.append(_unsupported_subject())
    plan["subject-universe"] = {
        "status": "available",
        "id": ci_validation_subject_universe_id(subjects),
    }
    target = {"type": "subject", "id": "ruby.unsupported-example"}
    for section in (
        "work-groups",
        "evidence-expectations",
        "validation-obligations",
    ):
        records = cast("list[dict[str, object]]", plan[section])
        records[0]["coverage-target"] = target
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="unsupported subject"):
        validate_ci_validation_plan(plan)


@pytest.mark.parametrize(
    "status_updates",
    [
        {"activity-status": "inactive", "selection-status": "not-selected"},
        {"selection-status": "not-selected"},
    ],
)
def test_coverage_targets_must_resolve_to_active_selected_subjects(
    status_updates: dict[str, object],
) -> None:
    """Subject targets resolve only against active selected subjects."""
    snapshot = _plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    subjects = cast("list[dict[str, object]]", plan["subjects"])
    subjects[0].update(status_updates)
    plan["subject-universe"] = {
        "status": "available",
        "id": ci_validation_subject_universe_id(subjects),
    }
    _redigest_plan(plan)

    with pytest.raises(
        ContractValidationError,
        match="subject target is unresolved",
    ):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_ecosystem_targets_must_have_active_selected_subject() -> None:
    """Executable ecosystem targets require an active selected subject."""
    snapshot = _plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    subjects = cast("list[dict[str, object]]", plan["subjects"])
    subjects[0]["selection-status"] = "not-selected"
    plan["subject-universe"] = {
        "status": "available",
        "id": ci_validation_subject_universe_id(subjects),
    }
    target = {"type": "ecosystem", "id": "python"}
    original_target = {"type": "subject", "id": "python.src-public-lib-example"}
    for section in (
        "work-groups",
        "evidence-expectations",
        "validation-obligations",
    ):
        records = cast("list[dict[str, object]]", plan[section])
        for record in records:
            if record.get("coverage-target") == original_target:
                record["coverage-target"] = target
    _redigest_plan(plan)

    with pytest.raises(
        ContractValidationError,
        match="ecosystem target is unresolved",
    ):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


@pytest.mark.parametrize(
    "target",
    [
        {"type": "subject", "id": "python.missing"},
        {"type": "ecosystem", "id": "javascript"},
    ],
)
def test_impact_coverage_targets_must_resolve(
    target: dict[str, object],
) -> None:
    """Impact targets must resolve to active selected subjects."""
    snapshot = _plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    classification = cast("dict[str, object]", plan["classification"])
    impacts = cast("list[dict[str, object]]", classification["impacts"])
    impacts[0]["coverage-target"] = target
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="target is unresolved"):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_active_selected_ecosystem_targets_resolve() -> None:
    """Active selected subjects make ecosystem targets valid."""
    snapshot = _plan_snapshot()
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    target = {"type": "ecosystem", "id": "python"}
    original_target = {"type": "subject", "id": "python.src-public-lib-example"}
    classification = cast("dict[str, object]", plan["classification"])
    impacts = cast("list[dict[str, object]]", classification["impacts"])
    impacts[0]["coverage-target"] = target
    for section in (
        "work-groups",
        "evidence-expectations",
        "validation-obligations",
    ):
        records = cast("list[dict[str, object]]", plan[section])
        for record in records:
            if record.get("coverage-target") == original_target:
                record["coverage-target"] = target
    _redigest_plan(plan)

    validate_ci_validation_plan(
        plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )


def test_ecosystem_impacts_select_every_active_subject() -> None:
    """Ecosystem-scoped impacts cannot omit active target subjects."""
    classification = _classification_with_second_subject()
    impacts = cast("list[dict[str, object]]", classification["impacts"])
    impacts[0]["category"] = "ecosystem-scoped"
    impacts[0]["coverage-target"] = {"type": "ecosystem", "id": "python"}
    snapshot = freeze_ci_validation_plan(
        request=_normalized_request(),
        plan_id=PLAN_ID,
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="executable",
        classification=classification,
        subjects=[_subject(), _second_subject()],
        validation_obligations=[
            _validation_obligation(),
            _second_validation_obligation(),
        ],
        work_groups=[
            _ecosystem_gate_work_group(),
            _second_ecosystem_gate_work_group(),
        ],
        evidence_expectations=[
            _evidence_expectation(),
            _second_evidence_expectation(),
        ],
        fact_snapshot_providers=[_fact_provider_with_second_subject()],
    )
    plan = cast("dict[str, object]", deepcopy(snapshot.plan))
    subjects = cast("list[dict[str, object]]", plan["subjects"])
    subjects[1]["selection-status"] = "not-selected"
    plan["subject-universe"] = {
        "status": "available",
        "id": ci_validation_subject_universe_id(subjects),
    }
    mutated_classification = cast("dict[str, object]", plan["classification"])
    provenance = cast(
        "list[dict[str, object]]",
        mutated_classification["subject-selection-provenance"],
    )
    provenance[:] = [
        record
        for record in provenance
        if record.get("subject-id") != "python.src-public-lib-example-tools"
    ]
    _redigest_plan(plan)

    with pytest.raises(
        ContractValidationError,
        match="targeted ecosystem",
    ):
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_unsupported_subject_rejected_from_selection_provenance() -> None:
    """Selection provenance cannot select unsupported subjects."""
    plan = cast("dict[str, object]", deepcopy(_plan_snapshot().plan))
    subjects = cast("list[dict[str, object]]", plan["subjects"])
    subjects.append(_unsupported_subject())
    plan["subject-universe"] = {
        "status": "available",
        "id": ci_validation_subject_universe_id(subjects),
    }
    classification = cast("dict[str, object]", plan["classification"])
    provenance = cast(
        "list[dict[str, object]]",
        classification["subject-selection-provenance"],
    )
    provenance[0]["subject-id"] = "ruby.unsupported-example"
    _redigest_plan(plan)

    with pytest.raises(ContractValidationError, match="unsupported subject"):
        validate_ci_validation_plan(plan)
