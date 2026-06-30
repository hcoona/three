"""Execution-batch CI validation contract tests."""

from __future__ import annotations

import hashlib
import importlib.util
from collections.abc import Mapping, Sequence
from copy import deepcopy
from pathlib import Path
from types import ModuleType
from typing import TYPE_CHECKING, Any, NotRequired, TypedDict, cast

import pytest
import three_workflow_release_contracts as three_workflow_release_contracts_pkg
from three_workflow_release_contracts import (
    API_VERSIONS_BY_KIND,
    CI_VALIDATION_AGGREGATE_MANIFEST_AUTHORITY_MESSAGES,
    CI_VALIDATION_INVALID_PLAN_MISSING_MESSAGE,
    CI_VALIDATION_INVALID_PLAN_NON_AUTHORITATIVE_MESSAGE,
    DETAILS_BY_DIAGNOSTIC_CODE,
    REGISTERED_CI_VALIDATION_DIAGNOSTIC_CODES,
    REGISTERED_CI_VALIDATION_DIAGNOSTIC_DETAILS,
    CiValidationKind,
    ContractValidationError,
    ValidationIssue,
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
    ci_validation_batches,
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
    _materializer_batch_specs,
    _materializer_budget,
    _materializer_compatibility_key,
    _materializer_compatibility_profile,
    _projection_authority_from_plan,
    _validate_admitted_bundles_topologically,
    _validate_budget,
    _validate_ci_validation_aggregate_evidence_manifest,
    _validate_ci_validation_execution_batch_manifest,
    _validate_summary_manifest_projection_authority,
    _validate_supplied_summary_execution_manifest,
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
EXPECTED_MAX_EXECUTION_BATCHES = 13
EXPECTED_COALESCED_RELEASE_EXECUTOR_BATCHES = 1
EXPECTED_RELEASE_EXECUTION_SPLIT_BATCHES = 2
OLD_PER_BATCH_WINDOWS_FLOOR = 4
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
_global_full_scope_plan_snapshot = _PLANS_MODULE.__dict__[
    "_global_full_scope_plan_snapshot"
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
_AGGREGATE_SUMMARY_SELF_ARTIFACT_DIAGNOSTICS = (
    "aggregate-summary-missing",
    "aggregate-summary-duplicate",
    "aggregate-summary-unreadable",
    "aggregate-summary-malformed",
    "aggregate-summary-non-canonical",
    "aggregate-summary-digest-mismatch",
    "aggregate-summary-producer-unverified",
)


def test_aggregate_summary_self_artifact_diagnostics_are_not_public() -> None:
    """Aggregate summary upload-byte gates do not define public diagnostics."""
    assert (
        "aggregate-summary-artifact-failure"
        not in REGISTERED_CI_VALIDATION_DIAGNOSTIC_CODES
    )
    for detail in _AGGREGATE_SUMMARY_SELF_ARTIFACT_DIAGNOSTICS:
        assert detail not in REGISTERED_CI_VALIDATION_DIAGNOSTIC_DETAILS
    assert (
        "aggregate-summary-artifact-failure" not in DETAILS_BY_DIAGNOSTIC_CODE
    )


class _AuthorizingContextKwargs(TypedDict):
    request: dict[str, object]
    changed_files_snapshot: dict[str, object]
    fact_snapshot: dict[str, object]
    expected_run_id: str
    expected_run_attempt: str


class _AggregateSummaryValidationKwargs(TypedDict):
    plan: NotRequired[Mapping[str, object] | None]
    aggregate_evidence_manifest: NotRequired[Mapping[str, object] | None]
    admitted_batch_evidence_bundles: NotRequired[
        Sequence[Mapping[str, object]] | None
    ]
    execution_batch_manifest: NotRequired[Mapping[str, object] | None]
    request: NotRequired[Mapping[str, object] | None]
    changed_files_snapshot: NotRequired[Mapping[str, object] | None]
    fact_snapshot: NotRequired[Mapping[str, object] | None]


def _diagnostic(  # noqa: PLR0913
    diagnostic_id: str,
    *,
    code: str | None = None,
    detail: str | None = None,
    message: str | None = None,
    severity: str = "blocking-failure",
    verdict_effect: str = "failed",
    source_id: str | None = None,
) -> dict[str, object]:
    diagnostic_code = code or diagnostic_id
    diagnostic_message = message
    if (
        diagnostic_message is None
        and diagnostic_code == "final-evidence-failure"
        and detail in CI_VALIDATION_AGGREGATE_MANIFEST_AUTHORITY_MESSAGES
    ):
        diagnostic_message = (
            CI_VALIDATION_AGGREGATE_MANIFEST_AUTHORITY_MESSAGES[detail]
        )
    return {
        "diagnostic-id": diagnostic_id,
        "code": diagnostic_code,
        "detail": detail or diagnostic_code,
        "message": diagnostic_id
        if diagnostic_message is None
        else diagnostic_message,
        "source": {"type": "aggregation", "id": source_id},
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


def _global_full_scope_plan() -> dict[str, object]:
    snapshot = _global_full_scope_plan_snapshot()
    return cast("dict[str, object]", deepcopy(snapshot.plan))


def _global_full_scope_context_kwargs() -> _AuthorizingContextKwargs:
    snapshot = _global_full_scope_plan_snapshot()
    return {
        "request": _request_document(),
        "changed_files_snapshot": cast(
            "dict[str, object]",
            deepcopy(snapshot.changed_files_snapshot),
        ),
        "fact_snapshot": cast(
            "dict[str, object]",
            deepcopy(snapshot.fact_snapshot),
        ),
        "expected_run_id": RUN_ID,
        "expected_run_attempt": RUN_ATTEMPT,
    }


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


def _fail_closed_plan_context() -> tuple[
    dict[str, object],
    dict[str, object],
    dict[str, object],
]:
    classification = deepcopy(_PLANS_MODULE.__dict__["_classification"]())
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
    snapshot = _PLANS_MODULE.__dict__["freeze_ci_validation_plan"](
        request=_PLANS_MODULE.__dict__["_normalized_request"](),
        plan_id=cast("str", _PLANS_MODULE.__dict__["PLAN_ID"]),
        created_at=CREATED_AT,
        observed_commit_sha=TREE_SHA,
        verdict_intent="fail-closed",
        classification=classification,
        diagnostics=[
            _diagnostic(
                "fail-closed/unknown-change",
                code="unknown-change",
                detail="incomplete",
                message="Changed files could not be classified.",
                severity="fail-closed",
                verdict_effect="fail-closed",
            )
        ],
        fact_snapshot_providers=None,
    )
    plan = cast("dict[str, object]", snapshot.plan)
    changed_files_snapshot = cast(
        "dict[str, object]",
        deepcopy(snapshot.changed_files_snapshot),
    )
    materialization = materialize_ci_validation_execution_batches(
        plan=plan,
        request=_request_document(),
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=None,
        expected_run_id=RUN_ID,
        expected_run_attempt=RUN_ATTEMPT,
        created_at=CREATED_AT,
        execution_workflow="CI Validation",
    )
    return (
        plan,
        changed_files_snapshot,
        cast("dict[str, object]", materialization.manifest),
    )


def _int_mapping_value(mapping: Mapping[str, object], key: str) -> int:
    value = mapping[key]
    assert isinstance(value, int)
    assert not isinstance(value, bool)
    return value


def _validate_manifest_with_authorizing_case(
    manifest: Mapping[str, object],
    authorizing_case: str,
) -> None:
    if authorizing_case == "omitted":
        validate_ci_validation_execution_batch_manifest(manifest)
        return
    validate_ci_validation_execution_batch_manifest(
        manifest,
        authorizing=True,
    )


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


def _release_authorizing_context() -> dict[str, dict[str, object]]:
    snapshot = _release_plan_snapshot()
    return {
        "request": _request_document(),
        "changed_files_snapshot": cast(
            "dict[str, object]",
            snapshot.changed_files_snapshot,
        ),
        "fact_snapshot": cast("dict[str, object]", snapshot.fact_snapshot),
    }


def _release_batch_bundle() -> dict[str, object]:
    plan, manifest = _release_plan_and_manifest()
    batch = next(
        batch
        for batch in cast("list[dict[str, object]]", manifest["batches"])
        if cast("dict[str, object]", batch["compatibility-profile"])[
            "release-shaped-profile"
        ]
        is not None
    )
    selector = cast("list[dict[str, object]]", batch["ordered-selectors"])[0]
    obligation = cast("list[dict[str, object]]", plan["artifact-obligations"])[
        0
    ]
    artifact_ref = cast(
        "list[str]",
        cast("dict[str, object]", obligation["artifact"])[
            "expected-artifact-refs"
        ],
    )[0]
    digest = "a" * 64
    request_digest = "0123456789abcdef" * 4
    bundle_id = request_digest[:24]
    result = {
        "artifact-obligation-id": obligation["artifact-obligation-id"],
        "descriptor": {
            "path": obligation["descriptor-path"],
            "identity": "example",
        },
        "profile-coverage": obligation["profile-coverage"],
        "artifact": {
            "planned": obligation["artifact"],
            "observed": {
                "refs": [artifact_ref],
                "digests": [
                    {
                        "artifact-ref": artifact_ref,
                        "algorithm": "sha256",
                        "digest": digest,
                        "digest-available": True,
                        "diagnostics": [],
                    }
                ],
            },
            "outcome": "success",
            "diagnostics": [],
        },
        "release-receipt": {
            "planned": obligation["release-receipt"],
            "expected": True,
            "schema-checked": True,
            "outcome": "success",
            "diagnostics": [],
        },
        "outcome": "success",
        "diagnostics": [],
    }
    slot = cast("dict[str, object]", selector["expected-evidence-slot"])
    detail = {
        "evidence-source": "no-publish-validation",
        "source-proof": {
            "kind": "no-publish-validation-result",
            "work-group-id": selector["work-group-id"],
            "coverage-target": slot["coverage-target"],
            "observed-commit-sha": TREE_SHA,
            "generated-builds": [
                {
                    "request-digest": request_digest,
                    "bundle-id": bundle_id,
                }
            ],
            "artifact-digests": [
                {
                    "artifact-ref": artifact_ref,
                    "algorithm": "sha256",
                    "digest": digest,
                    "byte-source": {
                        "kind": "validation-build-output",
                        "path": (
                            ".three-ci-validation/work/validation-build/"
                            f"release-shaped/{bundle_id}/dist/pkg.whl"
                        ),
                        "size": 1,
                    },
                }
            ],
        },
        "artifact-obligation-results": [result],
    }
    selector_result = {
        "work-group-id": selector["work-group-id"],
        "selector-index": selector["selector-index"],
        "expected-evidence-id": selector["expected-evidence-id"],
        "expected-evidence-slot-digest": canonical_json_digest(slot),
        "mode": plan["mode"],
        "validation-tree": plan["validation-tree"],
        "affected-range": _summary_affected_range(plan),
        "scheduled-full": plan["scheduled-full"],
        "coverage-target": slot["coverage-target"],
        "ecosystem": "python",
        "runner-family": "ubuntu",
        "selector-variant": None,
        "depends-on": [],
        "dependency-results": [],
        "outcome": "success",
        "skip-reason": None,
        "evidence": {
            "category": "release-shaped-artifact",
            "planned-capabilities": None,
            "artifact-refs": [artifact_ref],
            "category-result": {
                "category": "release-shaped-artifact",
                "outcome": "success",
                "diagnostics": [],
                "artifact-refs": [artifact_ref],
                "detail": detail,
            },
        },
        "diagnostics": [],
        "proof-admissibility": "validation-only",
    }
    writer = cast("dict[str, object]", batch["batch-writer"])
    context = _release_authorizing_context()
    return freeze_ci_validation_batch_evidence_bundle(
        plan=plan,
        execution_batch_manifest=manifest,
        batch_id=cast("str", batch["batch-id"]),
        selector_results=[selector_result],
        writer={
            "identity-source": writer["identity-source"],
            "expected-boundary": writer["expected-boundary"],
            "expected-job-identity": writer["expected-job-identity"],
            "observed-workflow": "CI Validation",
            "observed-job": "execution-batch",
            "observed-matrix": _execution_batch_matrix_identity(batch),
        },
        execution_tree={
            "observed-commit-sha": TREE_SHA,
            "source": "execution-batch-boundary",
            "verified": True,
        },
        started_at=CREATED_AT,
        completed_at=CREATED_AT,
        created_at=CREATED_AT,
        request=context["request"],
        changed_files_snapshot=context["changed_files_snapshot"],
        fact_snapshot=context["fact_snapshot"],
        expected_run_id=RUN_ID,
        expected_run_attempt=RUN_ATTEMPT,
    )


def _release_bundle_detail(bundle: dict[str, object]) -> dict[str, object]:
    selector = cast("list[dict[str, object]]", bundle["selector-results"])[0]
    evidence = cast("dict[str, object]", selector["evidence"])
    category_result = cast("dict[str, object]", evidence["category-result"])
    return cast("dict[str, object]", category_result["detail"])


def _validate_release_bundle(bundle: dict[str, object]) -> None:
    plan, manifest = _release_plan_and_manifest()
    context = _release_authorizing_context()
    validate_ci_validation_batch_evidence_bundle(
        bundle,
        plan=plan,
        execution_batch_manifest=manifest,
        request=context["request"],
        changed_files_snapshot=context["changed_files_snapshot"],
        fact_snapshot=context["fact_snapshot"],
        expected_run_id=RUN_ID,
        expected_run_attempt=RUN_ATTEMPT,
    )


def _valid_release_profile_telemetry() -> dict[str, object]:
    request_digest = "0123456789abcdef" * 4
    bundle_id = request_digest[:24]
    bundle_dir = (
        f".three-ci-validation/work/validation-build/release-shaped/{bundle_id}"
    )
    return {
        "kind": "release-shaped-validation-profile-telemetry",
        "schema-version": 1,
        "phases": [
            {
                "phase": "release-build-execute-build",
                "outcome": "success",
                "started-at": "2026-06-26T05:00:00.000Z",
                "completed-at": "2026-06-26T05:00:01.000Z",
                "duration-ms": 1000,
                "cwd": bundle_dir,
                "descriptor-path": "src/public/lib/example/three.release.yml",
                "project-id": "python.example",
                "profile": "wheel",
                "ecosystem": "python",
                "request-digest": request_digest,
                "bundle-id": bundle_id,
                "work-group-id": "wg-artifact",
                "runner-family": "ubuntu",
                "cache-hit": False,
                "cache-path": (
                    ".three-ci-validation/work/release-shaped-plans/plan.json"
                ),
                "project-count": 1,
                "target-count": 1,
                "obligation-count": 1,
                "artifact-count": 1,
                "build-id-count": 1,
                "output-path": bundle_dir,
            },
            {
                "phase": "artifact-digest-observation",
                "outcome": "success",
                "started-at": "2026-06-26T05:00:01.000Z",
                "completed-at": "2026-06-26T05:00:02.000Z",
                "duration-ms": 1000,
                "artifact-ref": (
                    "ci-validation/artifacts/python/example/wheel.whl"
                ),
                "output-path": f"{bundle_dir}/dist/pkg.whl",
            },
        ],
        "release-build": {
            "bundle-dir": bundle_dir,
            "request-digest": request_digest,
            "bundle-id": bundle_id,
            "work-group-id": "wg-artifact",
            "runner-family": "ubuntu",
            "profile-root": (f"{bundle_dir}/_profile/runs/run-1"),
            "executor": {
                "kind": "release-build-profile-telemetry",
                "schema-version": 1,
                "profile-root": (f"{bundle_dir}/_profile/runs/run-1"),
                "path": (f"{bundle_dir}/release-build-profile-telemetry.json"),
                "phases": [
                    {
                        "phase": "dotnet-pack",
                        "outcome": "success",
                        "started-at": "2026-06-26T05:00:00.000Z",
                        "completed-at": "2026-06-26T05:00:01.000Z",
                        "duration-ms": 1000,
                        "argv": [
                            "dotnet",
                            "pack",
                            (
                                "/bl:.three-ci-validation/work/"
                                "validation-build/_profile/runs/run-1/"
                                "binlogs/0001-dotnet-pack.binlog"
                            ),
                        ],
                        "uploaded-evidence-argv": [
                            "dotnet",
                            "pack",
                            (
                                "/bl:validation-result-profile-evidence/"
                                "_profile/runs/run-1/binlogs/"
                                "0001-dotnet-pack.binlog"
                            ),
                        ],
                        "cwd": bundle_dir,
                        "exit-code": 0,
                        "output-paths": [f"{bundle_dir}/dist/pkg.whl"],
                        "binlog-path": (
                            f"{bundle_dir}/_profile/runs/run-1/binlogs/"
                            "0001-dotnet-pack.binlog"
                        ),
                        "binlog-exists": True,
                        "binlog-uploaded-evidence-path": (
                            "validation-result-profile-evidence/"
                            "_profile/runs/run-1/binlogs/"
                            "0001-dotnet-pack.binlog"
                        ),
                    }
                ],
            },
        },
        "uploaded-evidence-path": "validation-result-profile-evidence",
        "uploaded-evidence-files": [
            "validation-result-profile-evidence/release-build-profile-telemetry.json",
            (
                "validation-result-profile-evidence/_profile/runs/run-1/"
                "binlogs/0001-dotnet-pack.binlog"
            ),
            (
                "validation-result-profile-evidence/_profile/runs/run-1/"
                "binlogs/0002-dotnet-pack.binlog"
            ),
            (
                "validation-result-profile-evidence/_profile/runs/run-1/"
                "binlogs/0003-dotnet-pack.binlog"
            ),
            (
                "validation-result-profile-evidence/_profile/runs/run-1/"
                "binlogs/0004-dotnet-pack.binlog"
            ),
        ],
    }


def _add_valid_powershell_profile_telemetry(
    telemetry: dict[str, object],
) -> None:
    release_build = cast("dict[str, object]", telemetry["release-build"])
    release_build["powershell"] = [
        {
            "kind": "powershell-release-build-profile-telemetry",
            "schema-version": 1,
            "script": (
                "src/public/app/ImageOcclusionEditor/script/"
                "Publish-ImageOcclusionEditor.ps1"
            ),
            "path": (
                ".three-ci-validation/work/validation-build/"
                "powershell-publish-profile-telemetry.json"
            ),
            "phases": [
                {
                    "phase": "publish",
                    "outcome": "success",
                    "started-at": "2026-06-26T05:00:00.000Z",
                    "completed-at": "2026-06-26T05:00:01.000Z",
                    "duration-ms": 1000,
                    "argv": [
                        "pwsh",
                        "-File",
                        (
                            "src/public/app/ImageOcclusionEditor/script/"
                            "Publish-ImageOcclusionEditor.ps1"
                        ),
                        "-OutputRoot",
                        ".three-ci-validation/work/validation-build/out",
                        (
                            "-TelemetryOutputPath=.three-ci-validation/work/"
                            "validation-build/_profile/publish.json"
                        ),
                        (
                            "-MsBuildBinlogDirectory:.three-ci-validation/"
                            "work/validation-build/_profile/binlogs"
                        ),
                    ],
                    "uploaded-evidence-argv": [
                        "pwsh",
                        "-File",
                        (
                            "src/public/app/ImageOcclusionEditor/script/"
                            "Publish-ImageOcclusionEditor.ps1"
                        ),
                        "-OutputRoot",
                        "validation-result-profile-evidence/out",
                        (
                            "-TelemetryOutputPath=validation-result-"
                            "profile-evidence/publish.json"
                        ),
                    ],
                }
            ],
        }
    ]


def _valid_release_profile_telemetry_with_powershell() -> dict[str, object]:
    telemetry = _valid_release_profile_telemetry()
    _add_valid_powershell_profile_telemetry(telemetry)
    return telemetry


def _replace_nested_profile_telemetry_value(
    telemetry: dict[str, object],
    keys: Sequence[str | int],
    value: object,
    *,
    with_powershell: bool = False,
) -> dict[str, object]:
    mutated = deepcopy(telemetry)
    if with_powershell:
        _add_valid_powershell_profile_telemetry(mutated)
    container: object = mutated
    for key in keys[:-1]:
        if isinstance(key, int):
            container = cast("list[object]", container)[key]
        else:
            container = cast("dict[str, object]", container)[key]
    final_key = keys[-1]
    if isinstance(final_key, int):
        cast("list[object]", container)[final_key] = value
    else:
        cast("dict[str, object]", container)[final_key] = value
    return mutated


_INVALID_PROFILE_PATH_FORMS = (
    pytest.param(
        "posix-absolute",
        "/home/runner/work/repo/profile-path",
        id="posix-absolute",
    ),
    pytest.param(
        "windows-drive",
        "C:/work/repo/profile-path",
        id="windows-drive",
    ),
    pytest.param(
        "unc",
        r"\\server\share\profile-path",
        id="unc",
    ),
    pytest.param(
        "parent-traversal",
        "../profile-path",
        id="parent-traversal",
    ),
)

_INVALID_PROFILE_PATH_VALUES = (
    pytest.param("/home/runner/work/repo/profile-path", id="posix-absolute"),
    pytest.param("C:/work/repo/profile-path", id="windows-drive"),
    pytest.param(r"\\server\share\profile-path", id="unc"),
    pytest.param("../profile-path", id="parent-traversal"),
)


def _release_profile_phase(
    telemetry: dict[str, object],
    owner: str,
) -> dict[str, object]:
    release_build = cast("dict[str, object]", telemetry["release-build"])
    if owner == "executor":
        executor = cast("dict[str, object]", release_build["executor"])
        phases = cast("list[dict[str, object]]", executor["phases"])
        return phases[0]
    if owner == "powershell":
        powershell = cast(
            "list[dict[str, object]]",
            release_build["powershell"],
        )
        phases = cast("list[dict[str, object]]", powershell[0]["phases"])
        return phases[0]
    phases = cast("list[dict[str, object]]", telemetry["phases"])
    return phases[0]


_NESTED_RELEASE_PHASE_PATH_CASES = (
    pytest.param(
        ("release-build", "executor", "phases", 0, "cwd"),
        ".profile-telemetry.release-build.executor.phases[0].cwd",
        "base",
        "scalar",
        id="executor-cwd",
    ),
    pytest.param(
        ("release-build", "executor", "phases", 0, "output-path"),
        ".profile-telemetry.release-build.executor.phases[0].output-path",
        "base",
        "scalar",
        id="executor-output-path",
    ),
    pytest.param(
        ("release-build", "executor", "phases", 0, "binlog-path"),
        ".profile-telemetry.release-build.executor.phases[0].binlog-path",
        "base",
        "scalar",
        id="executor-binlog-path",
    ),
    pytest.param(
        ("release-build", "executor", "phases", 0, "binlog-directory"),
        ".profile-telemetry.release-build.executor.phases[0].binlog-directory",
        "base",
        "scalar",
        id="executor-binlog-directory",
    ),
    pytest.param(
        ("release-build", "executor", "phases", 0, "output-paths"),
        ".profile-telemetry.release-build.executor.phases[0].output-paths[0]",
        "base",
        "sequence",
        id="executor-output-paths",
    ),
    pytest.param(
        ("release-build", "executor", "phases", 0, "binlog-paths"),
        ".profile-telemetry.release-build.executor.phases[0].binlog-paths[0]",
        "base",
        "sequence",
        id="executor-binlog-paths",
    ),
    pytest.param(
        (
            "release-build",
            "executor",
            "phases",
            0,
            "binlog-uploaded-evidence-path",
        ),
        (
            ".profile-telemetry.release-build.executor.phases[0]"
            ".binlog-uploaded-evidence-path"
        ),
        "base",
        "scalar",
        id="executor-binlog-uploaded-evidence-path",
    ),
    pytest.param(
        (
            "release-build",
            "executor",
            "phases",
            0,
            "binlog-uploaded-evidence-paths",
        ),
        (
            ".profile-telemetry.release-build.executor.phases[0]"
            ".binlog-uploaded-evidence-paths[0]"
        ),
        "base",
        "sequence",
        id="executor-binlog-uploaded-evidence-paths",
    ),
    pytest.param(
        ("release-build", "powershell", 0, "phases", 0, "cwd"),
        ".profile-telemetry.release-build.powershell[0].phases[0].cwd",
        "powershell",
        "scalar",
        id="powershell-cwd",
    ),
    pytest.param(
        ("release-build", "powershell", 0, "phases", 0, "output-path"),
        ".profile-telemetry.release-build.powershell[0].phases[0].output-path",
        "powershell",
        "scalar",
        id="powershell-output-path",
    ),
    pytest.param(
        ("release-build", "powershell", 0, "phases", 0, "binlog-path"),
        ".profile-telemetry.release-build.powershell[0].phases[0].binlog-path",
        "powershell",
        "scalar",
        id="powershell-binlog-path",
    ),
    pytest.param(
        ("release-build", "powershell", 0, "phases", 0, "binlog-directory"),
        (
            ".profile-telemetry.release-build.powershell[0].phases[0]"
            ".binlog-directory"
        ),
        "powershell",
        "scalar",
        id="powershell-binlog-directory",
    ),
    pytest.param(
        ("release-build", "powershell", 0, "phases", 0, "output-paths"),
        ".profile-telemetry.release-build.powershell[0].phases[0].output-paths[0]",
        "powershell",
        "sequence",
        id="powershell-output-paths",
    ),
    pytest.param(
        ("release-build", "powershell", 0, "phases", 0, "binlog-paths"),
        ".profile-telemetry.release-build.powershell[0].phases[0].binlog-paths[0]",
        "powershell",
        "sequence",
        id="powershell-binlog-paths",
    ),
    pytest.param(
        (
            "release-build",
            "powershell",
            0,
            "phases",
            0,
            "binlog-uploaded-evidence-path",
        ),
        (
            ".profile-telemetry.release-build.powershell[0].phases[0]"
            ".binlog-uploaded-evidence-path"
        ),
        "powershell",
        "scalar",
        id="powershell-binlog-uploaded-evidence-path",
    ),
    pytest.param(
        (
            "release-build",
            "powershell",
            0,
            "phases",
            0,
            "binlog-uploaded-evidence-paths",
        ),
        (
            ".profile-telemetry.release-build.powershell[0].phases[0]"
            ".binlog-uploaded-evidence-paths[0]"
        ),
        "powershell",
        "sequence",
        id="powershell-binlog-uploaded-evidence-paths",
    ),
)


def test_release_shaped_batch_accepts_bound_source_proof() -> None:
    """Positive release-shaped batch evidence binds proof to bytes."""
    _validate_release_bundle(_release_batch_bundle())


def test_release_shaped_batch_accepts_profile_telemetry() -> None:
    """Release-shaped detail validates optional hosted profile telemetry."""
    bundle = _release_batch_bundle()
    _release_bundle_detail(bundle)["profile-telemetry"] = (
        _valid_release_profile_telemetry()
    )

    _validate_release_bundle(bundle)


def test_release_shaped_batch_rejects_mismatched_release_build_plural() -> None:
    """Singular release-build metadata must mirror release-builds[0]."""
    bundle = _release_batch_bundle()
    telemetry = _valid_release_profile_telemetry()
    plural_build = deepcopy(telemetry["release-build"])
    cast("dict[str, object]", plural_build)["profile-root"] = (
        ".three-ci-validation/work/validation-build/_profile/runs/other-run"
    )
    telemetry["release-builds"] = [plural_build]
    _release_bundle_detail(bundle)["profile-telemetry"] = telemetry

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        ".profile-telemetry.release-build" in issue.path
        and "release-builds[0]" in issue.message
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    ("mutator", "expected_path"),
    [
        (
            lambda telemetry: cast(
                "dict[str, object]",
                _release_profile_phase(telemetry, "base"),
            ).pop("request-digest"),
            ".profile-telemetry.phases[0].request-digest",
        ),
        (
            lambda telemetry: cast(
                "dict[str, object]",
                telemetry["release-build"],
            ).pop("work-group-id"),
            ".profile-telemetry.release-build.work-group-id",
        ),
        (
            lambda telemetry: cast(
                "dict[str, object]",
                telemetry["release-build"],
            ).__setitem__(
                "bundle-dir",
                ".three-ci-validation/work/validation-build/stale",
            ),
            ".profile-telemetry.release-build.bundle-dir",
        ),
        (
            lambda telemetry: cast(
                "dict[str, object]",
                telemetry["release-build"],
            ).__setitem__("runner-family", "windows"),
            ".profile-telemetry.release-build.runner-family",
        ),
        (
            lambda telemetry: cast(
                "dict[str, object]",
                telemetry["release-build"],
            ).__setitem__("bundle-id", "f" * 24),
            ".profile-telemetry.release-build.bundle-id",
        ),
    ],
)
def test_release_shaped_batch_rejects_unbound_release_build_profile(
    mutator: Callable[[dict[str, object]], object],
    expected_path: str,
) -> None:
    """Captured release-build profiles must bind to execute-build identity."""
    bundle = _release_batch_bundle()
    telemetry = _valid_release_profile_telemetry()
    mutator(telemetry)
    _release_bundle_detail(bundle)["profile-telemetry"] = telemetry

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(expected_path in issue.path for issue in exc_info.value.issues)


def test_release_shaped_batch_rejects_foreign_profile_selector() -> None:
    """Internally consistent profile telemetry must still match selector."""
    bundle = _release_batch_bundle()
    telemetry = _valid_release_profile_telemetry()
    _release_profile_phase(telemetry, "base")["work-group-id"] = "wg-foreign"
    release_build = cast("dict[str, object]", telemetry["release-build"])
    release_build["work-group-id"] = "wg-foreign"
    _release_bundle_detail(bundle)["profile-telemetry"] = telemetry

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        ".profile-telemetry.phases[0].work-group-id" in issue.path
        and "selector" in issue.message
        for issue in exc_info.value.issues
    )
    assert any(
        ".profile-telemetry.release-build.work-group-id" in issue.path
        and "selector" in issue.message
        for issue in exc_info.value.issues
    )


def test_release_shaped_batch_rejects_foreign_selector_without_build() -> None:
    """Execute-build phases bind to selector even without release-build data."""
    bundle = _release_batch_bundle()
    telemetry = _valid_release_profile_telemetry()
    _release_profile_phase(telemetry, "base")["work-group-id"] = "wg-foreign"
    telemetry.pop("release-build")
    _release_bundle_detail(bundle)["profile-telemetry"] = telemetry

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        ".profile-telemetry.phases[0].work-group-id" in issue.path
        and "selector" in issue.message
        for issue in exc_info.value.issues
    )


def test_release_shaped_batch_rejects_foreign_scoped_profile_phase() -> None:
    """Scoped non-execute profile phases must match the validated selector."""
    bundle = _release_batch_bundle()
    telemetry = _valid_release_profile_telemetry()
    foreign_phase: dict[str, object] = {
        "phase": "validation-build-artifact-mapping-record",
        "work-group-id": "wg-foreign",
        "runner-family": "linux",
        "outcome": "success",
        "started-at": "2025-01-01T00:00:00.000Z",
        "completed-at": "2025-01-01T00:00:01.000Z",
        "duration-ms": 1000,
    }
    phases = cast("list[dict[str, object]]", telemetry["phases"])
    phases.append(foreign_phase)
    _release_bundle_detail(bundle)["profile-telemetry"] = telemetry

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        ".profile-telemetry.phases[2].work-group-id" in issue.path
        and "selector" in issue.message
        for issue in exc_info.value.issues
    )
    assert any(
        ".profile-telemetry.phases[2].runner-family" in issue.path
        and "selector" in issue.message
        for issue in exc_info.value.issues
    )


def test_release_shaped_batch_rejects_foreign_supplemental_primary_group_phase() -> (  # noqa: E501
    None
):
    """Primary-group phases cannot use supplemental scope mismatch allowance."""
    bundle = _release_batch_bundle()
    telemetry = _valid_release_profile_telemetry()
    primary_phase = deepcopy(_release_profile_phase(telemetry, "base"))
    primary_phase["phase"] = "release-build-materialization-primary-group"
    primary_phase["work-group-id"] = "wg-foreign"
    primary_phase["supplemental"] = True
    phases = cast("list[dict[str, object]]", telemetry["phases"])
    phases.append(primary_phase)
    _release_bundle_detail(bundle)["profile-telemetry"] = telemetry

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        ".profile-telemetry.phases[2].work-group-id" in issue.path
        and "selector" in issue.message
        for issue in exc_info.value.issues
    )


def test_release_shaped_batch_accepts_supplemental_group_phase() -> None:
    """Actual supplemental materialization may keep same-runner evidence."""
    bundle = _release_batch_bundle()
    telemetry = _valid_release_profile_telemetry()
    supplemental_phase = deepcopy(_release_profile_phase(telemetry, "base"))
    supplemental_phase["phase"] = (
        "release-build-materialization-supplemental-group"
    )
    supplemental_phase["work-group-id"] = "wg-supplemental"
    supplemental_phase["supplemental"] = True
    supplemental_phase["artifact-count"] = 2
    phases = cast("list[dict[str, object]]", telemetry["phases"])
    phases.append(supplemental_phase)
    _release_bundle_detail(bundle)["profile-telemetry"] = telemetry

    _validate_release_bundle(bundle)


def test_release_shaped_batch_rejects_standalone_supplemental_build_mismatch() -> (  # noqa: E501
    None
):
    """Supplemental execute/release-build mismatch needs group evidence."""
    bundle = _release_batch_bundle()
    telemetry = _valid_release_profile_telemetry()
    foreign_digest = "f" * 64
    foreign_bundle_id = foreign_digest[:24]
    foreign_bundle_dir = (
        ".three-ci-validation/work/validation-build/release-shaped/"
        f"{foreign_bundle_id}"
    )
    foreign_execute = deepcopy(_release_profile_phase(telemetry, "base"))
    foreign_execute["request-digest"] = foreign_digest
    foreign_execute["bundle-id"] = foreign_bundle_id
    foreign_execute["output-path"] = foreign_bundle_dir
    foreign_execute["work-group-id"] = "wg-supplemental"
    foreign_execute["supplemental"] = True
    phases = cast("list[dict[str, object]]", telemetry["phases"])
    phases.append(foreign_execute)
    release_build = cast("dict[str, object]", telemetry["release-build"])
    release_build["request-digest"] = foreign_digest
    release_build["bundle-id"] = foreign_bundle_id
    release_build["bundle-dir"] = foreign_bundle_dir
    release_build["work-group-id"] = "wg-supplemental"
    release_build["supplemental"] = True
    release_build["profile-root"] = f"{foreign_bundle_dir}/_profile/runs/run-1"
    executor = cast("dict[str, object]", release_build["executor"])
    executor["profile-root"] = f"{foreign_bundle_dir}/_profile/runs/run-1"
    _release_bundle_detail(bundle)["profile-telemetry"] = telemetry

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        ".profile-telemetry.phases[2].work-group-id" in issue.path
        and "selector" in issue.message
        for issue in exc_info.value.issues
    )
    assert any(
        ".profile-telemetry.release-build.work-group-id" in issue.path
        and "selector" in issue.message
        for issue in exc_info.value.issues
    )


def test_release_shaped_batch_accepts_group_backed_supplemental_build() -> None:
    """Supplemental execute/release-build evidence binds to group record."""
    bundle = _release_batch_bundle()
    telemetry = _valid_release_profile_telemetry()
    foreign_digest = "f" * 64
    foreign_bundle_id = foreign_digest[:24]
    foreign_bundle_dir = (
        ".three-ci-validation/work/validation-build/release-shaped/"
        f"{foreign_bundle_id}"
    )
    foreign_execute = deepcopy(_release_profile_phase(telemetry, "base"))
    foreign_execute["request-digest"] = foreign_digest
    foreign_execute["bundle-id"] = foreign_bundle_id
    foreign_execute["output-path"] = foreign_bundle_dir
    foreign_execute["work-group-id"] = "wg-supplemental"
    foreign_execute["supplemental"] = True
    supplemental_group = deepcopy(foreign_execute)
    supplemental_group["phase"] = (
        "release-build-materialization-supplemental-group"
    )
    supplemental_group["artifact-count"] = 2
    phases = cast("list[dict[str, object]]", telemetry["phases"])
    phases.extend([supplemental_group, foreign_execute])
    release_build = cast("dict[str, object]", telemetry["release-build"])
    release_build["request-digest"] = foreign_digest
    release_build["bundle-id"] = foreign_bundle_id
    release_build["bundle-dir"] = foreign_bundle_dir
    release_build["work-group-id"] = "wg-supplemental"
    release_build["supplemental"] = True
    release_build["profile-root"] = f"{foreign_bundle_dir}/_profile/runs/run-1"
    executor = cast("dict[str, object]", release_build["executor"])
    executor["profile-root"] = f"{foreign_bundle_dir}/_profile/runs/run-1"
    _release_bundle_detail(bundle)["profile-telemetry"] = telemetry

    _validate_release_bundle(bundle)


def test_release_shaped_batch_rejects_group_mismatched_supplemental_build() -> (
    None
):
    """Supplemental group proof must match execute/release-build work-group."""
    bundle = _release_batch_bundle()
    telemetry = _valid_release_profile_telemetry()
    foreign_digest = "f" * 64
    foreign_bundle_id = foreign_digest[:24]
    foreign_bundle_dir = (
        ".three-ci-validation/work/validation-build/release-shaped/"
        f"{foreign_bundle_id}"
    )
    foreign_execute = deepcopy(_release_profile_phase(telemetry, "base"))
    foreign_execute["request-digest"] = foreign_digest
    foreign_execute["bundle-id"] = foreign_bundle_id
    foreign_execute["output-path"] = foreign_bundle_dir
    foreign_execute["work-group-id"] = "wg-supplemental"
    foreign_execute["supplemental"] = True
    supplemental_group = deepcopy(foreign_execute)
    supplemental_group["phase"] = (
        "release-build-materialization-supplemental-group"
    )
    supplemental_group["work-group-id"] = "wg-other-supplemental"
    supplemental_group["artifact-count"] = 2
    phases = cast("list[dict[str, object]]", telemetry["phases"])
    phases.extend([supplemental_group, foreign_execute])
    release_build = cast("dict[str, object]", telemetry["release-build"])
    release_build["request-digest"] = foreign_digest
    release_build["bundle-id"] = foreign_bundle_id
    release_build["bundle-dir"] = foreign_bundle_dir
    release_build["work-group-id"] = "wg-supplemental"
    release_build["supplemental"] = True
    release_build["profile-root"] = f"{foreign_bundle_dir}/_profile/runs/run-1"
    executor = cast("dict[str, object]", release_build["executor"])
    executor["profile-root"] = f"{foreign_bundle_dir}/_profile/runs/run-1"
    _release_bundle_detail(bundle)["profile-telemetry"] = telemetry

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        ".profile-telemetry.phases[3].work-group-id" in issue.path
        and "selector" in issue.message
        for issue in exc_info.value.issues
    )
    assert any(
        ".profile-telemetry.release-build.work-group-id" in issue.path
        and "selector" in issue.message
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize("group_outcome", ["failure", "skipped"])
def test_release_shaped_batch_rejects_non_success_supplemental_group_build(
    group_outcome: str,
) -> None:
    """Only successful supplemental groups authorize mismatched builds."""
    bundle = _release_batch_bundle()
    telemetry = _valid_release_profile_telemetry()
    foreign_digest = "f" * 64
    foreign_bundle_id = foreign_digest[:24]
    foreign_bundle_dir = (
        ".three-ci-validation/work/validation-build/release-shaped/"
        f"{foreign_bundle_id}"
    )
    foreign_execute = deepcopy(_release_profile_phase(telemetry, "base"))
    foreign_execute["request-digest"] = foreign_digest
    foreign_execute["bundle-id"] = foreign_bundle_id
    foreign_execute["output-path"] = foreign_bundle_dir
    foreign_execute["work-group-id"] = "wg-supplemental"
    foreign_execute["supplemental"] = True
    supplemental_group = deepcopy(foreign_execute)
    supplemental_group["phase"] = (
        "release-build-materialization-supplemental-group"
    )
    supplemental_group["outcome"] = group_outcome
    supplemental_group["artifact-count"] = 2
    phases = cast("list[dict[str, object]]", telemetry["phases"])
    phases.extend([supplemental_group, foreign_execute])
    release_build = cast("dict[str, object]", telemetry["release-build"])
    release_build["request-digest"] = foreign_digest
    release_build["bundle-id"] = foreign_bundle_id
    release_build["bundle-dir"] = foreign_bundle_dir
    release_build["work-group-id"] = "wg-supplemental"
    release_build["supplemental"] = True
    release_build["profile-root"] = f"{foreign_bundle_dir}/_profile/runs/run-1"
    executor = cast("dict[str, object]", release_build["executor"])
    executor["profile-root"] = f"{foreign_bundle_dir}/_profile/runs/run-1"
    _release_bundle_detail(bundle)["profile-telemetry"] = telemetry

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        ".profile-telemetry.phases[3].work-group-id" in issue.path
        and "selector" in issue.message
        for issue in exc_info.value.issues
    )
    assert any(
        ".profile-telemetry.release-build.work-group-id" in issue.path
        and "selector" in issue.message
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    ("owner", "expected_path"),
    [
        ("release-build", ".profile-telemetry.release-build.supplemental"),
        ("phase", ".profile-telemetry.phases[0].supplemental"),
    ],
)
def test_release_shaped_batch_rejects_non_boolean_supplemental(
    owner: str,
    expected_path: str,
) -> None:
    """Supplemental markers are explicit booleans where admitted."""
    bundle = _release_batch_bundle()
    telemetry = _valid_release_profile_telemetry()
    if owner == "release-build":
        cast("dict[str, object]", telemetry["release-build"])[
            "supplemental"
        ] = "true"
    else:
        _release_profile_phase(telemetry, "base")["supplemental"] = "true"
    _release_bundle_detail(bundle)["profile-telemetry"] = telemetry

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        expected_path in issue.path and issue.message == "must be a boolean"
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    "phase_name",
    ["release-build-request", "unexpected-unscoped-phase"],
)
def test_release_shaped_batch_rejects_unscoped_profile_phase(
    phase_name: str,
) -> None:
    """Scope-sensitive profile phases must declare selector identity."""
    bundle = _release_batch_bundle()
    telemetry = _valid_release_profile_telemetry()
    phases = cast("list[dict[str, object]]", telemetry["phases"])
    phases.append(
        {
            "phase": phase_name,
            "outcome": "success",
            "started-at": "2025-01-01T00:00:00.000Z",
            "completed-at": "2025-01-01T00:00:01.000Z",
            "duration-ms": 1000,
        }
    )
    _release_bundle_detail(bundle)["profile-telemetry"] = telemetry

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        ".profile-telemetry.phases[2].work-group-id" in issue.path
        and issue.message == "is required"
        for issue in exc_info.value.issues
    )
    assert any(
        ".profile-telemetry.phases[2].runner-family" in issue.path
        and issue.message == "is required"
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    "phase_name",
    sorted(
        ci_validation_batches._UNSCOPED_RELEASE_SHAPED_PROFILE_PHASES,  # noqa: SLF001
    ),
)
def test_release_shaped_batch_accepts_unscoped_allowlist_profile_phase(
    phase_name: str,
) -> None:
    """Explicitly allowlisted profile phases may omit selector scope."""
    bundle = _release_batch_bundle()
    telemetry = _valid_release_profile_telemetry()
    phases = cast("list[dict[str, object]]", telemetry["phases"])
    if phase_name != "artifact-digest-observation":
        phases.append(
            {
                "phase": phase_name,
                "outcome": "success",
                "started-at": "2025-01-01T00:00:00.000Z",
                "completed-at": "2025-01-01T00:00:01.000Z",
                "duration-ms": 1000,
            }
        )
    assert all(
        "work-group-id" not in phase and "runner-family" not in phase
        for phase in phases
        if phase.get("phase") == phase_name
    )
    _release_bundle_detail(bundle)["profile-telemetry"] = telemetry

    _validate_release_bundle(bundle)


def test_release_shaped_batch_rejects_duplicate_foreign_execute_phase() -> None:
    """Duplicate execute output paths cannot hide foreign selector identity."""
    bundle = _release_batch_bundle()
    telemetry = _valid_release_profile_telemetry()
    foreign_phase = deepcopy(_release_profile_phase(telemetry, "base"))
    foreign_phase["work-group-id"] = "wg-foreign"
    telemetry["phases"] = [
        foreign_phase,
        _release_profile_phase(telemetry, "base"),
    ]
    _release_bundle_detail(bundle)["profile-telemetry"] = telemetry

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        ".profile-telemetry.phases[0].work-group-id" in issue.path
        and "selector" in issue.message
        for issue in exc_info.value.issues
    )
    assert any(
        ".profile-telemetry.phases[1].output-path" in issue.path
        and "unique" in issue.message
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    ("key", "value", "expected_message"),
    [
        ("work-group-id", "../foreign", "must be path-safe"),
        ("runner-family", "solaris", "is not registered"),
    ],
)
def test_release_shaped_batch_rejects_invalid_profile_identity_values(
    key: str,
    value: str,
    expected_message: str,
) -> None:
    """Execute and release-build identity values are syntactically bounded."""
    bundle = _release_batch_bundle()
    telemetry = _valid_release_profile_telemetry()
    _release_profile_phase(telemetry, "base")[key] = value
    cast("dict[str, object]", telemetry["release-build"])[key] = value
    _release_bundle_detail(bundle)["profile-telemetry"] = telemetry

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        ".profile-telemetry.phases[0]." + key in issue.path
        and issue.message == expected_message
        for issue in exc_info.value.issues
    )
    assert any(
        ".profile-telemetry.release-build." + key in issue.path
        and issue.message == expected_message
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    "root_owner",
    ["release-build", "executor"],
)
def test_release_shaped_batch_rejects_foreign_profile_root(
    root_owner: str,
) -> None:
    """Release-build profile roots must stay under their matched bundle."""
    bundle = _release_batch_bundle()
    telemetry = _valid_release_profile_telemetry()
    release_build = cast("dict[str, object]", telemetry["release-build"])
    foreign_root = (
        ".three-ci-validation/work/validation-build/release-shaped/"
        f"{'f' * 24}/_profile/runs/run-1"
    )
    if root_owner == "executor":
        executor = cast("dict[str, object]", release_build["executor"])
        executor["profile-root"] = foreign_root
        expected_path = ".profile-telemetry.release-build.executor.profile-root"
    else:
        release_build["profile-root"] = foreign_root
        expected_path = ".profile-telemetry.release-build.profile-root"
    _release_bundle_detail(bundle)["profile-telemetry"] = telemetry

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        expected_path in issue.path and "bundle-dir" in issue.message
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    "root_owner",
    ["release-build", "executor"],
)
def test_release_shaped_batch_rejects_bundle_profile_root(
    root_owner: str,
) -> None:
    """Release-build profile roots must be below their matched bundle."""
    bundle = _release_batch_bundle()
    telemetry = _valid_release_profile_telemetry()
    release_build = cast("dict[str, object]", telemetry["release-build"])
    bundle_dir = cast("str", release_build["bundle-dir"])
    if root_owner == "executor":
        executor = cast("dict[str, object]", release_build["executor"])
        executor["profile-root"] = bundle_dir
        expected_path = ".profile-telemetry.release-build.executor.profile-root"
    else:
        release_build["profile-root"] = bundle_dir
        expected_path = ".profile-telemetry.release-build.profile-root"
    _release_bundle_detail(bundle)["profile-telemetry"] = telemetry

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        expected_path in issue.path and "bundle-dir/_profile" in issue.message
        for issue in exc_info.value.issues
    )


def test_release_shaped_batch_rejects_bare_foreign_profile_roots() -> None:
    """Bare repo-relative profile roots are not accepted as bundle roots."""
    bundle = _release_batch_bundle()
    telemetry = _valid_release_profile_telemetry()
    release_build = cast("dict[str, object]", telemetry["release-build"])
    release_build["profile-root"] = "foreign-profile-root"
    executor = cast("dict[str, object]", release_build["executor"])
    executor["profile-root"] = "foreign-profile-root"
    _release_bundle_detail(bundle)["profile-telemetry"] = telemetry

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        ".profile-telemetry.release-build.profile-root" in issue.path
        and "bundle-dir" in issue.message
        for issue in exc_info.value.issues
    )
    assert any(
        ".profile-telemetry.release-build.executor.profile-root" in issue.path
        and "bundle-dir" in issue.message
        for issue in exc_info.value.issues
    )


def test_release_shaped_batch_rejects_bare_profile_roots() -> None:
    """Bare _profile roots are not accepted as bundle-root metadata."""
    bundle = _release_batch_bundle()
    telemetry = _valid_release_profile_telemetry()
    release_build = cast("dict[str, object]", telemetry["release-build"])
    release_build["profile-root"] = "_profile/runs/run-1"
    executor = cast("dict[str, object]", release_build["executor"])
    executor["profile-root"] = "_profile/runs/run-1"
    _release_bundle_detail(bundle)["profile-telemetry"] = telemetry

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        ".profile-telemetry.release-build.profile-root" in issue.path
        and "bundle-dir" in issue.message
        for issue in exc_info.value.issues
    )
    assert any(
        ".profile-telemetry.release-build.executor.profile-root" in issue.path
        and "bundle-dir" in issue.message
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    "root_owner",
    ["release-build", "executor"],
)
def test_release_shaped_batch_rejects_current_directory_profile_root(
    root_owner: str,
) -> None:
    """Current-directory profile roots are not accepted as bundle roots."""
    bundle = _release_batch_bundle()
    telemetry = _valid_release_profile_telemetry()
    release_build = cast("dict[str, object]", telemetry["release-build"])
    if root_owner == "executor":
        executor = cast("dict[str, object]", release_build["executor"])
        executor["profile-root"] = "."
        expected_path = ".profile-telemetry.release-build.executor.profile-root"
    else:
        release_build["profile-root"] = "."
        expected_path = ".profile-telemetry.release-build.profile-root"
    _release_bundle_detail(bundle)["profile-telemetry"] = telemetry

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        expected_path in issue.path and "bundle-dir" in issue.message
        for issue in exc_info.value.issues
    )


def test_release_shaped_batch_rejects_unidentified_execute_no_build() -> None:
    """Execute-build phases require identity even without sidecar evidence."""
    bundle = _release_batch_bundle()
    telemetry = _valid_release_profile_telemetry()
    phase = _release_profile_phase(telemetry, "base")
    phase.pop("request-digest")
    phase.pop("bundle-id")
    telemetry.pop("release-build")
    _release_bundle_detail(bundle)["profile-telemetry"] = telemetry

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        ".profile-telemetry.phases[0].request-digest" in issue.path
        and issue.message == "is required"
        for issue in exc_info.value.issues
    )
    assert any(
        ".profile-telemetry.phases[0].bundle-id" in issue.path
        and issue.message == "is required"
        for issue in exc_info.value.issues
    )


def test_release_shaped_batch_rejects_unidentified_outputless_execute() -> None:
    """Execute-build identity is required even before output-path binding."""
    bundle = _release_batch_bundle()
    telemetry = _valid_release_profile_telemetry()
    phase = _release_profile_phase(telemetry, "base")
    phase.pop("request-digest")
    phase.pop("bundle-id")
    phase.pop("output-path")
    telemetry.pop("release-build")
    _release_bundle_detail(bundle)["profile-telemetry"] = telemetry

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        ".profile-telemetry.phases[0].request-digest" in issue.path
        and issue.message == "is required"
        for issue in exc_info.value.issues
    )
    assert any(
        ".profile-telemetry.phases[0].bundle-id" in issue.path
        and issue.message == "is required"
        for issue in exc_info.value.issues
    )


def test_release_shaped_batch_rejects_outputless_identified_execute() -> None:
    """Execute-build phases require output-path."""
    bundle = _release_batch_bundle()
    telemetry = _valid_release_profile_telemetry()
    phase = _release_profile_phase(telemetry, "base")
    phase.pop("output-path")
    telemetry.pop("release-build")
    _release_bundle_detail(bundle)["profile-telemetry"] = telemetry

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        ".profile-telemetry.phases[0].output-path" in issue.path
        and issue.message == "is required"
        for issue in exc_info.value.issues
    )


def test_release_shaped_batch_rejects_bundle_dir_basename_mismatch() -> None:
    """Generated bundle paths must be bound to the validated bundle id."""
    bundle = _release_batch_bundle()
    telemetry = _valid_release_profile_telemetry()
    wrong_bundle_dir = (
        f".three-ci-validation/work/validation-build/release-shaped/{'f' * 24}"
    )
    _release_profile_phase(telemetry, "base")["output-path"] = wrong_bundle_dir
    release_build = cast("dict[str, object]", telemetry["release-build"])
    release_build["bundle-dir"] = wrong_bundle_dir
    release_build["profile-root"] = f"{wrong_bundle_dir}/_profile/runs/run-1"
    executor = cast("dict[str, object]", release_build["executor"])
    executor["profile-root"] = f"{wrong_bundle_dir}/_profile/runs/run-1"
    _release_bundle_detail(bundle)["profile-telemetry"] = telemetry

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        ".profile-telemetry.phases[0].output-path" in issue.path
        and "bundle-id" in issue.message
        for issue in exc_info.value.issues
    )
    assert any(
        ".profile-telemetry.release-build.bundle-dir" in issue.path
        and "bundle-id" in issue.message
        for issue in exc_info.value.issues
    )


def test_release_shaped_batch_rejects_forged_bundle_path_shape() -> None:
    """Generated bundle paths must use the controlled release-shaped root."""
    bundle = _release_batch_bundle()
    telemetry = _valid_release_profile_telemetry()
    bundle_id = cast(
        "str",
        _release_profile_phase(telemetry, "base")["bundle-id"],
    )
    wrong_bundle_dir = f"tmp/release-shaped/{bundle_id}"
    _release_profile_phase(telemetry, "base")["output-path"] = wrong_bundle_dir
    release_build = cast("dict[str, object]", telemetry["release-build"])
    release_build["bundle-dir"] = wrong_bundle_dir
    release_build["profile-root"] = f"{wrong_bundle_dir}/_profile/runs/run-1"
    executor = cast("dict[str, object]", release_build["executor"])
    executor["profile-root"] = f"{wrong_bundle_dir}/_profile/runs/run-1"
    _release_bundle_detail(bundle)["profile-telemetry"] = telemetry

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        ".profile-telemetry.phases[0].output-path" in issue.path
        and "release-shaped bundle path" in issue.message
        for issue in exc_info.value.issues
    )
    assert any(
        ".profile-telemetry.release-build.bundle-dir" in issue.path
        and "release-shaped bundle path" in issue.message
        for issue in exc_info.value.issues
    )


def test_release_shaped_batch_rejects_foreign_build_identity() -> None:
    """Profile identities must match the current source-proof build identity."""
    bundle = _release_batch_bundle()
    telemetry = _valid_release_profile_telemetry()
    foreign_digest = "f" * 64
    foreign_bundle_id = foreign_digest[:24]
    foreign_bundle_dir = (
        ".three-ci-validation/work/validation-build/release-shaped/"
        f"{foreign_bundle_id}"
    )
    phase = _release_profile_phase(telemetry, "base")
    phase["request-digest"] = foreign_digest
    phase["bundle-id"] = foreign_bundle_id
    phase["output-path"] = foreign_bundle_dir
    release_build = cast("dict[str, object]", telemetry["release-build"])
    release_build["request-digest"] = foreign_digest
    release_build["bundle-id"] = foreign_bundle_id
    release_build["bundle-dir"] = foreign_bundle_dir
    release_build["profile-root"] = f"{foreign_bundle_dir}/_profile/runs/run-1"
    executor = cast("dict[str, object]", release_build["executor"])
    executor["profile-root"] = f"{foreign_bundle_dir}/_profile/runs/run-1"
    _release_bundle_detail(bundle)["profile-telemetry"] = telemetry

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        ".profile-telemetry.phases[0].request-digest" in issue.path
        and "source-proof generated build identity" in issue.message
        for issue in exc_info.value.issues
    )
    assert any(
        ".profile-telemetry.release-build.request-digest" in issue.path
        and "source-proof generated build identity" in issue.message
        for issue in exc_info.value.issues
    )


def test_release_shaped_batch_rejects_missing_generated_builds() -> None:
    """Profile identities require source-proof generated-builds."""
    bundle = _release_batch_bundle()
    telemetry = _valid_release_profile_telemetry()
    detail = _release_bundle_detail(bundle)
    source_proof = cast("dict[str, object]", detail["source-proof"])
    source_proof.pop("generated-builds")
    detail["profile-telemetry"] = telemetry

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        ".detail.source-proof.generated-builds" in issue.path
        and "generated validation-build identities" in issue.message
        for issue in exc_info.value.issues
    )


def test_release_shaped_batch_rejects_generated_proof_without_builds() -> None:
    """Generated byte-source paths require generated-build identities."""
    bundle = _release_batch_bundle()
    detail = _release_bundle_detail(bundle)
    source_proof = cast("dict[str, object]", detail["source-proof"])
    source_proof.pop("generated-builds")
    detail.pop("profile-telemetry", None)

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        ".detail.source-proof.generated-builds" in issue.path
        and "generated validation-build identities" in issue.message
        for issue in exc_info.value.issues
    )


def test_release_shaped_batch_accepts_declared_proof_without_builds() -> None:
    """Declared byte-source paths do not require generated-build identities."""
    bundle = _release_batch_bundle()
    detail = _release_bundle_detail(bundle)
    source_proof = cast("dict[str, object]", detail["source-proof"])
    source_proof.pop("generated-builds")
    digests = cast("list[dict[str, object]]", source_proof["artifact-digests"])
    byte_source = cast("dict[str, object]", digests[0]["byte-source"])
    byte_source["path"] = ".three-ci-validation/work/validation-build/pkg.whl"
    detail.pop("profile-telemetry", None)

    _validate_release_bundle(bundle)


def test_release_shaped_batch_accepts_gem_validation_build_source() -> None:
    """Legacy validation-build.gem output root remains accepted."""
    bundle = _release_batch_bundle()
    detail = _release_bundle_detail(bundle)
    source_proof = cast("dict[str, object]", detail["source-proof"])
    source_proof.pop("generated-builds")
    digests = cast("list[dict[str, object]]", source_proof["artifact-digests"])
    byte_source = cast("dict[str, object]", digests[0]["byte-source"])
    byte_source["path"] = ".three-ci-validation/work/validation-build.gem"
    detail.pop("profile-telemetry", None)

    _validate_release_bundle(bundle)


def test_release_shaped_batch_rejects_arbitrary_byte_source_path() -> None:
    """Byte-source paths must come from validation-build output roots."""
    bundle = _release_batch_bundle()
    detail = _release_bundle_detail(bundle)
    source_proof = cast("dict[str, object]", detail["source-proof"])
    source_proof.pop("generated-builds")
    digests = cast("list[dict[str, object]]", source_proof["artifact-digests"])
    byte_source = cast("dict[str, object]", digests[0]["byte-source"])
    byte_source["path"] = "README.md"
    detail.pop("profile-telemetry", None)

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        ".detail.source-proof.artifact-digests[0].byte-source.path"
        in issue.path
        and "validation-build output root" in issue.message
        for issue in exc_info.value.issues
    )


def test_release_shaped_batch_rejects_unobserved_byte_source_path() -> None:
    """Source-proof byte-sources must match observed validation-build output."""
    bundle = _release_batch_bundle()
    detail = _release_bundle_detail(bundle)
    source_proof = cast("dict[str, object]", detail["source-proof"])
    digests = cast("list[dict[str, object]]", source_proof["artifact-digests"])
    artifact_ref = cast("str", digests[0]["artifact-ref"])
    byte_source = cast("dict[str, object]", digests[0]["byte-source"])
    byte_source["path"] = (
        ".three-ci-validation/work/validation-build/unrelated/pkg.whl"
    )
    telemetry = _valid_release_profile_telemetry()
    phases = cast("list[dict[str, object]]", telemetry["phases"])
    phases.append(
        {
            "phase": "artifact-digest-observation",
            "outcome": "success",
            "started-at": "2025-01-01T00:00:00Z",
            "completed-at": "2025-01-01T00:00:01Z",
            "duration-ms": 1000,
            "artifact-ref": artifact_ref,
            "output-path": (
                ".three-ci-validation/work/validation-build/release-shaped/"
                "0123456789abcdef01234567/dist/pkg.whl"
            ),
        }
    )
    detail["profile-telemetry"] = telemetry

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        ".detail.source-proof.artifact-digests[0].byte-source.path"
        in issue.path
        and "observed validation-build output path" in issue.message
        for issue in exc_info.value.issues
    )


def test_release_shaped_batch_rejects_missing_byte_source_observation() -> None:
    """Profile telemetry must observe every source-proof byte-source."""
    bundle = _release_batch_bundle()
    detail = _release_bundle_detail(bundle)
    telemetry = _valid_release_profile_telemetry()
    phases = cast("list[dict[str, object]]", telemetry["phases"])
    telemetry["phases"] = [
        phase
        for phase in phases
        if phase.get("phase") != "artifact-digest-observation"
    ]
    detail["profile-telemetry"] = telemetry

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        ".detail.source-proof.artifact-digests[0].byte-source.path"
        in issue.path
        and "observed validation-build output path" in issue.message
        for issue in exc_info.value.issues
    )


def test_release_shaped_batch_rejects_conflicting_byte_source_observation() -> (
    None
):
    """Duplicate artifact observations cannot bypass byte-source binding."""
    bundle = _release_batch_bundle()
    detail = _release_bundle_detail(bundle)
    source_proof = cast("dict[str, object]", detail["source-proof"])
    digests = cast("list[dict[str, object]]", source_proof["artifact-digests"])
    artifact_ref = cast("str", digests[0]["artifact-ref"])
    byte_source = cast("dict[str, object]", digests[0]["byte-source"])
    byte_source["path"] = (
        ".three-ci-validation/work/validation-build/unrelated/pkg.whl"
    )
    telemetry = _valid_release_profile_telemetry()
    phases = cast("list[dict[str, object]]", telemetry["phases"])
    phases.append(
        {
            "phase": "artifact-digest-observation",
            "outcome": "success",
            "started-at": "2025-01-01T00:00:00Z",
            "completed-at": "2025-01-01T00:00:01Z",
            "duration-ms": 1000,
            "artifact-ref": artifact_ref,
            "output-path": (
                ".three-ci-validation/work/validation-build/other/pkg.whl"
            ),
        }
    )
    detail["profile-telemetry"] = telemetry

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        ".profile-telemetry.phases[2].artifact-ref" in issue.path
        and "exactly one artifact digest observation" in issue.message
        for issue in exc_info.value.issues
    )
    assert any(
        ".detail.source-proof.artifact-digests[0].byte-source.path"
        in issue.path
        and "observed validation-build output path" in issue.message
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize("outcome", ["failure", "skipped"])
def test_release_shaped_batch_rejects_non_success_byte_source_observation(
    outcome: str,
) -> None:
    """Only successful artifact observations bind source-proof byte-sources."""
    bundle = _release_batch_bundle()
    detail = _release_bundle_detail(bundle)
    telemetry = _valid_release_profile_telemetry()
    observation = cast("list[dict[str, object]]", telemetry["phases"])[1]
    assert observation["phase"] == "artifact-digest-observation"
    observation["outcome"] = outcome
    detail["profile-telemetry"] = telemetry

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        ".detail.source-proof.artifact-digests[0].byte-source.path"
        in issue.path
        and "observed validation-build output path" in issue.message
        for issue in exc_info.value.issues
    )


def test_release_shaped_batch_rejects_partial_duplicate_byte_source_observation() -> (  # noqa: E501
    None
):
    """Malformed duplicate observations cannot bypass duplicate checks."""
    bundle = _release_batch_bundle()
    detail = _release_bundle_detail(bundle)
    telemetry = _valid_release_profile_telemetry()
    phases = cast("list[dict[str, object]]", telemetry["phases"])
    phases.append(
        {
            "phase": "artifact-digest-observation",
            "outcome": "success",
            "started-at": "2025-01-01T00:00:00.000Z",
            "completed-at": "2025-01-01T00:00:01.000Z",
            "duration-ms": 1000,
            "artifact-ref": phases[1]["artifact-ref"],
        }
    )
    detail["profile-telemetry"] = telemetry

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        ".profile-telemetry.phases[2].artifact-ref" in issue.path
        and "exactly one artifact digest observation" in issue.message
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize("outcome", ["success", "failure", "skipped"])
def test_release_shaped_batch_rejects_foreign_byte_source_observation(
    outcome: str,
) -> None:
    """Observed artifact refs must exactly match current source-proof refs."""
    bundle = _release_batch_bundle()
    detail = _release_bundle_detail(bundle)
    telemetry = _valid_release_profile_telemetry()
    phases = cast("list[dict[str, object]]", telemetry["phases"])
    phases.append(
        {
            "phase": "artifact-digest-observation",
            "outcome": outcome,
            "started-at": "2025-01-01T00:00:00.000Z",
            "completed-at": "2025-01-01T00:00:01.000Z",
            "duration-ms": 1000,
            "artifact-ref": (
                "ci-validation/artifacts/python/example/foreign.whl"
            ),
            "output-path": (
                ".three-ci-validation/work/validation-build/foreign.whl"
            ),
        }
    )
    detail["profile-telemetry"] = telemetry

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        ".detail.source-proof.artifact-digests" in issue.path
        and "observed artifact digest refs exactly" in issue.message
        for issue in exc_info.value.issues
    )


def test_release_shaped_batch_rejects_generated_bundle_root_byte_source() -> (
    None
):
    """Generated byte-sources must identify files below the bundle root."""
    bundle = _release_batch_bundle()
    detail = _release_bundle_detail(bundle)
    source_proof = cast("dict[str, object]", detail["source-proof"])
    source_proof.pop("generated-builds")
    digests = cast("list[dict[str, object]]", source_proof["artifact-digests"])
    byte_source = cast("dict[str, object]", digests[0]["byte-source"])
    byte_source["path"] = (
        ".three-ci-validation/work/validation-build/release-shaped/"
        "0123456789abcdef01234567"
    )
    detail.pop("profile-telemetry", None)

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        ".detail.source-proof.artifact-digests[0].byte-source.path"
        in issue.path
        and "below generated release-shaped bundle path" in issue.message
        for issue in exc_info.value.issues
    )
    assert any(
        ".detail.source-proof.generated-builds" in issue.path
        and "generated validation-build identities" in issue.message
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    ("path_value", "expected_message"),
    [
        (
            ".three-ci-validation/work/validation-build/release-shaped",
            "generated release-shaped bundle path",
        ),
        (
            ".three-ci-validation/work/validation-build/release-shaped/"
            "not-a-valid-bundle/dist/pkg.whl",
            "valid generated release-shaped bundle id",
        ),
    ],
)
def test_release_shaped_batch_rejects_invalid_release_shaped_byte_source_root(
    path_value: str,
    expected_message: str,
) -> None:
    """Generated byte-source paths must identify a valid bundle file."""
    bundle = _release_batch_bundle()
    detail = _release_bundle_detail(bundle)
    source_proof = cast("dict[str, object]", detail["source-proof"])
    source_proof.pop("generated-builds")
    digests = cast("list[dict[str, object]]", source_proof["artifact-digests"])
    byte_source = cast("dict[str, object]", digests[0]["byte-source"])
    byte_source["path"] = path_value
    detail.pop("profile-telemetry", None)

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        ".detail.source-proof.artifact-digests[0].byte-source.path"
        in issue.path
        and expected_message in issue.message
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    "path_value",
    [
        "./.three-ci-validation/work/validation-build/release-shaped/"
        "0123456789abcdef01234567/dist/pkg.whl",
        "/abs/.three-ci-validation/work/validation-build/release-shaped/"
        "0123456789abcdef01234567/dist/pkg.whl",
        ".three-ci-validation\\work\\validation-build\\release-shaped\\"
        "0123456789abcdef01234567\\dist\\pkg.whl",
        ".three-ci-validation/work/validation-build/release-shaped/"
        "0123456789abcdef01234567/../0123456789abcdef01234567/dist/pkg.whl",
    ],
)
def test_release_shaped_batch_rejects_noncanonical_byte_source_path(
    path_value: str,
) -> None:
    """Byte-source paths must be normalized relative paths."""
    bundle = _release_batch_bundle()
    detail = _release_bundle_detail(bundle)
    source_proof = cast("dict[str, object]", detail["source-proof"])
    source_proof.pop("generated-builds")
    digests = cast("list[dict[str, object]]", source_proof["artifact-digests"])
    byte_source = cast("dict[str, object]", digests[0]["byte-source"])
    byte_source["path"] = path_value
    detail.pop("profile-telemetry", None)

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        ".detail.source-proof.artifact-digests[0].byte-source.path"
        in issue.path
        and "normalized relative path" in issue.message
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize("path_value", [None, "", 123])
def test_release_shaped_batch_rejects_invalid_byte_source_path(
    path_value: object,
) -> None:
    """Byte-source path is required and must be a string."""
    bundle = _release_batch_bundle()
    detail = _release_bundle_detail(bundle)
    source_proof = cast("dict[str, object]", detail["source-proof"])
    digests = cast("list[dict[str, object]]", source_proof["artifact-digests"])
    byte_source = cast("dict[str, object]", digests[0]["byte-source"])
    if path_value is None:
        byte_source.pop("path")
    else:
        byte_source["path"] = path_value
    detail.pop("profile-telemetry", None)

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        ".detail.source-proof.artifact-digests[0].byte-source.path"
        in issue.path
        and issue.message == "must be a string"
        for issue in exc_info.value.issues
    )


def test_release_shaped_batch_rejects_extra_generated_build_identity() -> None:
    """Source-proof generated-build identities must not self-authorize."""
    bundle = _release_batch_bundle()
    telemetry = _valid_release_profile_telemetry()
    foreign_digest = "f" * 64
    foreign_bundle_id = foreign_digest[:24]
    foreign_bundle_dir = (
        ".three-ci-validation/work/validation-build/release-shaped/"
        f"{foreign_bundle_id}"
    )
    phase = _release_profile_phase(telemetry, "base")
    phase["request-digest"] = foreign_digest
    phase["bundle-id"] = foreign_bundle_id
    phase["output-path"] = foreign_bundle_dir
    release_build = cast("dict[str, object]", telemetry["release-build"])
    release_build["request-digest"] = foreign_digest
    release_build["bundle-id"] = foreign_bundle_id
    release_build["bundle-dir"] = foreign_bundle_dir
    release_build["profile-root"] = f"{foreign_bundle_dir}/_profile/runs/run-1"
    executor = cast("dict[str, object]", release_build["executor"])
    executor["profile-root"] = f"{foreign_bundle_dir}/_profile/runs/run-1"
    detail = _release_bundle_detail(bundle)
    source_proof = cast("dict[str, object]", detail["source-proof"])
    generated_builds = cast(
        "list[dict[str, object]]",
        source_proof["generated-builds"],
    )
    generated_builds.append(
        {
            "request-digest": foreign_digest,
            "bundle-id": foreign_bundle_id,
        }
    )
    detail["profile-telemetry"] = telemetry

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        ".detail.source-proof.generated-builds" in issue.path
        and "byte-source bundles" in issue.message
        for issue in exc_info.value.issues
    )
    assert any(
        ".detail.source-proof.generated-builds" in issue.path
        and "execute-build identities" in issue.message
        for issue in exc_info.value.issues
    )


def test_release_shaped_batch_rejects_duplicate_bundle_generated_identity() -> (
    None
):
    """Generated-build identities are one-to-one with bundle ids."""
    bundle = _release_batch_bundle()
    detail = _release_bundle_detail(bundle)
    source_proof = cast("dict[str, object]", detail["source-proof"])
    generated_builds = cast(
        "list[dict[str, object]]",
        source_proof["generated-builds"],
    )
    generated_builds.append(
        {
            "request-digest": "0123456789abcdef01234567" + ("f" * 40),
            "bundle-id": "0123456789abcdef01234567",
        }
    )

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        ".detail.source-proof.generated-builds" in issue.path
        and "one identity per bundle-id" in issue.message
        for issue in exc_info.value.issues
    )


def test_release_shaped_batch_rejects_declared_stray_generated_builds() -> None:
    """Declared output evidence rejects stray generated-build identities."""
    bundle = _release_batch_bundle()
    detail = _release_bundle_detail(bundle)
    source_proof = cast("dict[str, object]", detail["source-proof"])
    digests = cast("list[dict[str, object]]", source_proof["artifact-digests"])
    byte_source = cast("dict[str, object]", digests[0]["byte-source"])
    byte_source["path"] = ".three-ci-validation/work/validation-build/pkg.whl"
    detail.pop("profile-telemetry", None)

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        ".detail.source-proof.generated-builds" in issue.path
        and "byte-source bundles or profile telemetry" in issue.message
        for issue in exc_info.value.issues
    )


def test_release_shaped_batch_accepts_profile_identity_metadata() -> None:
    """Generated validation-build identity metadata is valid."""
    request_digest = "0123456789abcdef" * 4
    bundle = _release_batch_bundle()
    telemetry = _valid_release_profile_telemetry()
    phase = _release_profile_phase(telemetry, "base")
    phase["request-digest"] = request_digest
    phase["bundle-id"] = request_digest[:24]
    _release_bundle_detail(bundle)["profile-telemetry"] = telemetry

    _validate_release_bundle(bundle)


def test_release_shaped_batch_accepts_node_profile_telemetry() -> None:
    """Release-shaped profile phases accept node ecosystem telemetry."""
    bundle = _release_batch_bundle()
    telemetry = _valid_release_profile_telemetry()
    phase = _release_profile_phase(telemetry, "base")
    phase["project-id"] = "node.example"
    phase["profile"] = "npm-pack"
    phase["ecosystem"] = "node"
    _release_bundle_detail(bundle)["profile-telemetry"] = telemetry

    _validate_release_bundle(bundle)


def test_release_shaped_batch_accepts_profile_telemetry_path_argv_forms() -> (
    None
):
    """Normalized path-bearing profile argv forms are accepted."""
    bundle = _release_batch_bundle()
    telemetry = _valid_release_profile_telemetry_with_powershell()
    executor_phase = _release_profile_phase(telemetry, "executor")
    executor_phase["argv"] = [
        *cast("list[str]", executor_phase["argv"]),
        "--output=.three-ci-validation/work/validation-build/out",
        "-o:.three-ci-validation/work/validation-build/out",
        "@src/public/lib/three-workflow-release-contracts/pyproject.toml",
        (
            "/bl:LogFile=.three-ci-validation/work/validation-build/"
            "_profile/runs/run-1/binlogs/0002-dotnet-pack.binlog;"
            "ProjectImports=None"
        ),
        (
            '/bl:LogFile=".three-ci-validation/work/validation-build/'
            '_profile/runs/run-1/binlogs/0003-dotnet-pack.binlog";'
            "ProjectImports=None"
        ),
        (
            "/bl:ProjectImports=None;.three-ci-validation/work/"
            "validation-build/_profile/runs/run-1/binlogs/"
            "0004-dotnet-pack.binlog"
        ),
        "/binaryLogger:ProjectImports=None",
        "src/public/lib/three-workflow-release-contracts/pyproject.toml",
        "Configuration=Release",
        "/p:Configuration=Release",
    ]
    executor_phase["uploaded-evidence-argv"] = [
        *cast("list[str]", executor_phase["uploaded-evidence-argv"]),
        (
            "-binaryLogger:LogFile=validation-result-profile-evidence/"
            "_profile/runs/run-1/binlogs/0002-dotnet-pack.binlog"
        ),
        (
            '-binaryLogger:"validation-result-profile-evidence/'
            '_profile/runs/run-1/binlogs/0003-dotnet-pack.binlog"'
        ),
        (
            "-binaryLogger:ProjectImports=None;validation-result-"
            "profile-evidence/_profile/runs/run-1/binlogs/"
            "0004-dotnet-pack.binlog"
        ),
        "src/public/lib/three-workflow-release-contracts/pyproject.toml",
    ]
    powershell_phase = _release_profile_phase(telemetry, "powershell")
    powershell_phase["argv"] = [
        *cast("list[str]", powershell_phase["argv"]),
        "/O.three-ci-validation/work/validation-build/installer-out",
        "/DPublishDir=.three-ci-validation/work/validation-build/publish",
        "src/public/app/ImageOcclusionEditor/ImageOcclusionEditor.csproj",
    ]
    powershell_phase["uploaded-evidence-argv"] = [
        *cast("list[str]", powershell_phase["uploaded-evidence-argv"]),
        "src/public/app/ImageOcclusionEditor/ImageOcclusionEditor.csproj",
    ]
    _release_bundle_detail(bundle)["profile-telemetry"] = telemetry

    _validate_release_bundle(bundle)


@pytest.mark.parametrize(
    ("mutator", "expected_path"),
    [
        (
            lambda _telemetry: "not-an-object",
            ".profile-telemetry",
        ),
        (
            lambda telemetry: {
                **telemetry,
                "phases": ["not-an-object"],
            },
            ".profile-telemetry.phases[0]",
        ),
        (
            lambda telemetry: {
                **telemetry,
                "phases": [
                    {
                        "phase": "release-build-execute-build",
                        "outcome": "blocked",
                        "started-at": "2026-06-26T05:00:00Z",
                        "completed-at": "2026-06-26T05:00:01.000Z",
                        "duration-ms": -1,
                    }
                ],
            },
            ".profile-telemetry.phases[0].outcome",
        ),
        (
            lambda telemetry: {
                **telemetry,
                "release-build": "not-an-object",
            },
            ".profile-telemetry.release-build",
        ),
        (
            lambda telemetry: {
                **telemetry,
                "phases": [
                    {
                        **cast("list[dict[str, object]]", telemetry["phases"])[
                            0
                        ],
                        "cache-hit": "yes",
                    }
                ],
            },
            ".profile-telemetry.phases[0].cache-hit",
        ),
        (
            lambda telemetry: {
                **telemetry,
                "phases": [
                    {
                        **cast("list[dict[str, object]]", telemetry["phases"])[
                            0
                        ],
                        "ecosystem": "unregistered-ecosystem",
                    }
                ],
            },
            ".profile-telemetry.phases[0].ecosystem",
        ),
        (
            lambda telemetry: {
                **telemetry,
                "phases": [
                    {
                        **cast("list[dict[str, object]]", telemetry["phases"])[
                            0
                        ],
                        "artifact-count": -1,
                    }
                ],
            },
            ".profile-telemetry.phases[0].artifact-count",
        ),
        (
            lambda telemetry: {
                **telemetry,
                "phases": [
                    {
                        **cast("list[dict[str, object]]", telemetry["phases"])[
                            0
                        ],
                        "descriptor-count": -1,
                    }
                ],
            },
            ".profile-telemetry.phases[0].descriptor-count",
        ),
        (
            lambda telemetry: {
                **telemetry,
                "phases": [
                    {
                        **cast("list[dict[str, object]]", telemetry["phases"])[
                            0
                        ],
                        "descriptor-count": "1",
                    }
                ],
            },
            ".profile-telemetry.phases[0].descriptor-count",
        ),
        (
            lambda telemetry: {
                **telemetry,
                "phases": [
                    {
                        **cast("list[dict[str, object]]", telemetry["phases"])[
                            0
                        ],
                        "tracked-file-count": -1,
                    }
                ],
            },
            ".profile-telemetry.phases[0].tracked-file-count",
        ),
        (
            lambda telemetry: {
                **telemetry,
                "phases": [
                    {
                        **cast("list[dict[str, object]]", telemetry["phases"])[
                            0
                        ],
                        "tracked-file-count": "2",
                    }
                ],
            },
            ".profile-telemetry.phases[0].tracked-file-count",
        ),
        (
            lambda telemetry: {
                **telemetry,
                "release-build": {
                    "bundle-dir": ".three-ci-validation/work/validation-build",
                    "executor": {
                        "kind": "forged-profile-telemetry",
                        "schema-version": 1,
                        "profile-root": (
                            ".three-ci-validation/work/validation-build/"
                            "_profile/runs/run-1"
                        ),
                        "path": (
                            ".three-ci-validation/work/validation-build/"
                            "release-build-profile-telemetry.json"
                        ),
                        "phases": [],
                    },
                },
            },
            ".profile-telemetry.release-build.executor.kind",
        ),
        (
            lambda telemetry: {
                **telemetry,
                "release-build": {
                    "bundle-dir": ".three-ci-validation/work/validation-build",
                    "executor": {
                        "kind": "release-build-profile-telemetry",
                        "schema-version": 1,
                        "profile-root": (
                            ".three-ci-validation/work/validation-build/"
                            "_profile/runs/run-1"
                        ),
                        "path": (
                            ".three-ci-validation/work/validation-build/"
                            "release-build-profile-telemetry.json"
                        ),
                        "phases": [
                            {
                                "phase": "dotnet-pack",
                                "outcome": "success",
                                "started-at": "2026-06-26T05:00:00.000Z",
                                "completed-at": "2026-06-26T05:00:01.000Z",
                                "duration-ms": 1000,
                                "argv": ["dotnet", 123],
                            }
                        ],
                    },
                },
            },
            ".profile-telemetry.release-build.executor.phases[0].argv[1]",
        ),
    ],
)
def test_release_shaped_batch_rejects_malformed_profile_telemetry(
    mutator: Callable[[dict[str, object]], object],
    expected_path: str,
) -> None:
    """Malformed optional release-shaped profile telemetry is rejected."""
    bundle = _release_batch_bundle()
    _release_bundle_detail(bundle)["profile-telemetry"] = mutator(
        _valid_release_profile_telemetry()
    )

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(expected_path in issue.path for issue in exc_info.value.issues)


@pytest.mark.parametrize(
    ("metadata", "expected_path"),
    [
        (
            {"request-digest": "A" * 64, "bundle-id": "a" * 24},
            ".profile-telemetry.phases[0].request-digest",
        ),
        (
            {"request-digest": "a" * 64, "bundle-id": "not-a-bundle-id"},
            ".profile-telemetry.phases[0].bundle-id",
        ),
        (
            {"request-digest": "a" * 64, "bundle-id": "b" * 24},
            ".profile-telemetry.phases[0].bundle-id",
        ),
        (
            {"bundle-id": "a" * 24},
            ".profile-telemetry.phases[0].request-digest",
        ),
        (
            {"request-digest": "a" * 64},
            ".profile-telemetry.phases[0].bundle-id",
        ),
    ],
)
def test_release_shaped_batch_rejects_malformed_profile_identity_metadata(
    metadata: Mapping[str, object],
    expected_path: str,
) -> None:
    """Generated validation-build profile identity metadata is bounded."""
    bundle = _release_batch_bundle()
    telemetry = _valid_release_profile_telemetry()
    _release_profile_phase(telemetry, "base").update(metadata)
    _release_bundle_detail(bundle)["profile-telemetry"] = telemetry

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(expected_path in issue.path for issue in exc_info.value.issues)


@pytest.mark.parametrize(
    ("shape", "invalid_path"),
    _INVALID_PROFILE_PATH_FORMS,
)
@pytest.mark.parametrize(
    ("keys", "expected_path", "telemetry_shape"),
    [
        (
            ("release-build", "bundle-dir"),
            ".profile-telemetry.release-build.bundle-dir",
            "base",
        ),
        (
            ("release-build", "profile-root"),
            ".profile-telemetry.release-build.profile-root",
            "base",
        ),
        (
            ("release-build", "executor", "profile-root"),
            ".profile-telemetry.release-build.executor.profile-root",
            "base",
        ),
        (
            ("release-build", "executor", "path"),
            ".profile-telemetry.release-build.executor.path",
            "base",
        ),
        (
            ("release-build", "powershell", 0, "script"),
            ".profile-telemetry.release-build.powershell[0].script",
            "powershell",
        ),
        (
            ("release-build", "powershell", 0, "path"),
            ".profile-telemetry.release-build.powershell[0].path",
            "powershell",
        ),
        (
            ("phases", 0, "descriptor-path"),
            ".profile-telemetry.phases[0].descriptor-path",
            "base",
        ),
        (
            ("phases", 0, "cache-path"),
            ".profile-telemetry.phases[0].cache-path",
            "base",
        ),
    ],
)
def test_release_shaped_batch_rejects_invalid_profile_metadata_paths(
    shape: str,
    invalid_path: str,
    keys: Sequence[str | int],
    expected_path: str,
    telemetry_shape: str,
) -> None:
    """Profile metadata path fields reject every non-portable path shape."""
    del shape
    bundle = _release_batch_bundle()
    _release_bundle_detail(bundle)["profile-telemetry"] = (
        _replace_nested_profile_telemetry_value(
            _valid_release_profile_telemetry(),
            keys,
            invalid_path,
            with_powershell=telemetry_shape == "powershell",
        )
    )

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(expected_path in issue.path for issue in exc_info.value.issues)


@pytest.mark.parametrize(
    ("shape", "invalid_path"),
    _INVALID_PROFILE_PATH_FORMS,
)
@pytest.mark.parametrize(
    ("keys", "expected_path", "value_shape"),
    [
        (
            ("phases", 0, "cwd"),
            ".profile-telemetry.phases[0].cwd",
            "scalar",
        ),
        (
            ("phases", 0, "output-path"),
            ".profile-telemetry.phases[0].output-path",
            "scalar",
        ),
        (
            ("phases", 0, "binlog-path"),
            ".profile-telemetry.phases[0].binlog-path",
            "scalar",
        ),
        (
            ("phases", 0, "binlog-directory"),
            ".profile-telemetry.phases[0].binlog-directory",
            "scalar",
        ),
        (
            ("phases", 0, "binlog-uploaded-evidence-path"),
            (".profile-telemetry.phases[0].binlog-uploaded-evidence-path"),
            "scalar",
        ),
        (
            ("uploaded-evidence-path",),
            ".profile-telemetry.uploaded-evidence-path",
            "scalar",
        ),
        (
            ("phases", 0, "output-paths"),
            ".profile-telemetry.phases[0].output-paths[0]",
            "sequence",
        ),
        (
            ("phases", 0, "binlog-paths"),
            ".profile-telemetry.phases[0].binlog-paths[0]",
            "sequence",
        ),
        (
            ("phases", 0, "binlog-uploaded-evidence-paths"),
            (".profile-telemetry.phases[0].binlog-uploaded-evidence-paths[0]"),
            "sequence",
        ),
        (
            ("uploaded-evidence-files",),
            ".profile-telemetry.uploaded-evidence-files[0]",
            "sequence",
        ),
    ],
)
def test_release_shaped_batch_rejects_invalid_profile_telemetry_paths(
    shape: str,
    invalid_path: str,
    keys: Sequence[str | int],
    expected_path: str,
    value_shape: str,
) -> None:
    """Profile telemetry path fields reject every non-portable path shape."""
    del shape
    bundle = _release_batch_bundle()
    value: object = (
        [invalid_path] if value_shape == "sequence" else invalid_path
    )
    _release_bundle_detail(bundle)["profile-telemetry"] = (
        _replace_nested_profile_telemetry_value(
            _valid_release_profile_telemetry(),
            keys,
            value,
        )
    )

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(expected_path in issue.path for issue in exc_info.value.issues)


@pytest.mark.parametrize("invalid_path", _INVALID_PROFILE_PATH_VALUES)
@pytest.mark.parametrize(
    ("keys", "expected_path", "telemetry_shape", "value_shape"),
    _NESTED_RELEASE_PHASE_PATH_CASES,
)
def test_release_shaped_batch_rejects_invalid_nested_release_phase_paths(
    invalid_path: str,
    keys: Sequence[str | int],
    expected_path: str,
    telemetry_shape: str,
    value_shape: str,
) -> None:
    """Nested release-build phase path fields reject non-portable paths."""
    bundle = _release_batch_bundle()
    value: object = (
        [invalid_path] if value_shape == "sequence" else invalid_path
    )
    _release_bundle_detail(bundle)["profile-telemetry"] = (
        _replace_nested_profile_telemetry_value(
            _valid_release_profile_telemetry(),
            keys,
            value,
            with_powershell=telemetry_shape == "powershell",
        )
    )

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(expected_path in issue.path for issue in exc_info.value.issues)


@pytest.mark.parametrize(
    ("owner", "phase_path"),
    [
        (
            "executor",
            ".profile-telemetry.release-build.executor.phases[0]",
        ),
        (
            "powershell",
            ".profile-telemetry.release-build.powershell[0].phases[0]",
        ),
    ],
)
@pytest.mark.parametrize(
    "argv_key",
    ["argv", "uploaded-evidence-argv"],
)
@pytest.mark.parametrize(
    ("argv", "invalid_index"),
    [
        (
            ["dotnet", "pack", "/bl:/home/runner/work/repo/build.binlog"],
            2,
        ),
        (
            ["dotnet", "pack", "/bl:../escape.binlog"],
            2,
        ),
        (
            ["dotnet", "pack", "/bl:LogFile=/home/build.binlog"],
            2,
        ),
        (
            ["dotnet", "pack", "/bl:LogFile=C:/work/build.binlog"],
            2,
        ),
        (
            ["dotnet", "pack", "/bl:LogFile=../build.binlog"],
            2,
        ),
        (
            ["dotnet", "pack", '/bl:LogFile="/home/build.binlog"'],
            2,
        ),
        (
            ["dotnet", "pack", '/bl:LogFile="../build.binlog"'],
            2,
        ),
        (
            ["dotnet", "pack", '/bl:"/home/build.binlog"'],
            2,
        ),
        (
            ["dotnet", "pack", '/bl:"../build.binlog"'],
            2,
        ),
        (
            ["dotnet", "pack", "/bl:ProjectImports=None;../escape.binlog"],
            2,
        ),
        (
            ["dotnet", "pack", "/bl:ProjectImports=None;/tmp"],
            2,
        ),
        (
            [
                "dotnet",
                "pack",
                '-binaryLogger:ProjectImports=None;"/home/build.binlog"',
            ],
            2,
        ),
        (
            ["dotnet", "pack", "../src/project.csproj"],
            2,
        ),
        (
            ["dotnet", "pack", "/home/runner/work/repo/project.csproj"],
            2,
        ),
        (
            ["dotnet", "pack", "/" + "tmp"],
            2,
        ),
        (
            ["dotnet", "pack", "--output=/home/build/out"],
            2,
        ),
        (
            ["dotnet", "pack", "--output=../escape/out"],
            2,
        ),
        (
            ["dotnet", "pack", "-o:/home/build/out"],
            2,
        ),
        (
            ["dotnet", "pack", "--output", "/home/build/out"],
            3,
        ),
        (
            ["dotnet", "pack", "-o", "../escape/out"],
            3,
        ),
        (
            ["dotnet", "pack", "@/home/build/args.rsp"],
            2,
        ),
        (
            ["dotnet", "pack", "@../args.rsp"],
            2,
        ),
        (
            ["pwsh", "-TelemetryOutputPath=C:/work/profile.json"],
            1,
        ),
        (
            ["pwsh", "-MsBuildBinlogDirectory=../profile/binlogs"],
            1,
        ),
        (
            ["dotnet", "pack", r"\\server\share\evidence.csproj"],
            2,
        ),
        (
            ["pwsh", "-OutputRoot", r"\\server\share\evidence"],
            2,
        ),
        (
            ["pwsh", "-File", "../script/Publish.ps1"],
            2,
        ),
        (
            ["ISCC.exe", "/O/home/build/installer"],
            1,
        ),
        (
            ["ISCC.exe", "/OC:/work/installer"],
            1,
        ),
        (
            ["ISCC.exe", r"/O\\server\share\installer"],
            1,
        ),
        (
            ["ISCC.exe", "/O../escape/installer"],
            1,
        ),
        (
            ["ISCC.exe", "/DPublishDir=/home/build/publish"],
            1,
        ),
        (
            ["ISCC.exe", "/DPublishDir=../publish"],
            1,
        ),
    ],
)
def test_release_shaped_batch_rejects_invalid_profile_argv_paths(
    owner: str,
    phase_path: str,
    argv_key: str,
    argv: list[str],
    invalid_index: int,
) -> None:
    """Executor and PowerShell profile argv path entries must stay relative."""
    bundle = _release_batch_bundle()
    telemetry = _valid_release_profile_telemetry()
    if owner == "powershell":
        _add_valid_powershell_profile_telemetry(telemetry)
    phase = _release_profile_phase(telemetry, owner)
    phase[argv_key] = argv
    _release_bundle_detail(bundle)["profile-telemetry"] = telemetry

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    expected_path = f"{phase_path}.{argv_key}[{invalid_index}]"
    assert any(expected_path in issue.path for issue in exc_info.value.issues)


@pytest.mark.parametrize(
    "argv_key",
    ["argv", "uploaded-evidence-argv"],
)
@pytest.mark.parametrize(
    "executable",
    [
        "/usr/bin/dotnet",
        "C:/Program Files/Inno Setup 6/ISCC.exe",
    ],
)
def test_release_shaped_batch_accepts_absolute_profile_argv_executable(
    argv_key: str,
    executable: str,
) -> None:
    """External executable paths in argv[0] are opaque producer metadata."""
    bundle = _release_batch_bundle()
    telemetry = _valid_release_profile_telemetry()
    phase = _release_profile_phase(telemetry, "executor")
    binlog_arg = (
        "/bl:validation-result-profile-evidence/_profile/runs/run-1/"
        "binlogs/0001-dotnet-pack.binlog"
        if argv_key == "uploaded-evidence-argv"
        else (
            "/bl:.three-ci-validation/work/validation-build/_profile/"
            "runs/run-1/binlogs/0001-dotnet-pack.binlog"
        )
    )
    phase[argv_key] = [
        executable,
        "pack",
        "src/public/lib/three-workflow-release-contracts/pyproject.toml",
        binlog_arg,
    ]
    _release_bundle_detail(bundle)["profile-telemetry"] = telemetry

    _validate_release_bundle(bundle)


@pytest.mark.parametrize(
    "argv_key",
    ["argv", "uploaded-evidence-argv"],
)
@pytest.mark.parametrize(
    ("argv", "invalid_index"),
    [
        (
            ["/usr/bin/dotnet", "pack", "/home/build/project.csproj"],
            2,
        ),
        (
            ["C:/Program Files/Inno Setup 6/ISCC.exe", "/home/build/setup.iss"],
            1,
        ),
        (
            ["/usr/bin/dotnet", "pack", "/bl:/home/build.binlog"],
            2,
        ),
        (
            ["/usr/bin/pwsh", "-TelemetryOutputPath", "/home/profile.json"],
            2,
        ),
    ],
)
def test_release_shaped_batch_rejects_later_absolute_profile_argv_paths(
    argv_key: str,
    argv: list[str],
    invalid_index: int,
) -> None:
    """Opaque argv[0] does not permit later absolute path arguments."""
    bundle = _release_batch_bundle()
    telemetry = _valid_release_profile_telemetry()
    phase = _release_profile_phase(telemetry, "executor")
    phase[argv_key] = argv
    _release_bundle_detail(bundle)["profile-telemetry"] = telemetry

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    expected_path = (
        ".profile-telemetry.release-build.executor.phases[0]"
        f".{argv_key}[{invalid_index}]"
    )
    assert any(expected_path in issue.path for issue in exc_info.value.issues)


@pytest.mark.parametrize(
    ("mutator", "expected_path"),
    [
        (
            lambda telemetry: {
                **telemetry,
                "uploaded-evidence-path": "C:/profile-evidence",
            },
            ".profile-telemetry.uploaded-evidence-path",
        ),
        (
            lambda telemetry: {
                **telemetry,
                "uploaded-evidence-files": [
                    "validation-result-profile-evidence/../escape.binlog",
                ],
            },
            ".profile-telemetry.uploaded-evidence-files[0]",
        ),
        (
            lambda telemetry: {
                **telemetry,
                "uploaded-evidence-files": [
                    "other-profile-evidence/0001-dotnet-pack.binlog",
                ],
            },
            ".profile-telemetry.uploaded-evidence-files[0]",
        ),
        (
            lambda telemetry: _profile_telemetry_with_missing_uploaded_binlog(
                telemetry,
            ),
            ".profile-telemetry.release-build.executor.phases[0].binlog-uploaded-evidence-path",
        ),
        (
            (
                lambda telemetry: (
                    _profile_telemetry_with_local_uploaded_argv_binlog(
                        telemetry,
                    )
                )
            ),
            ".profile-telemetry.release-build.executor.phases[0].uploaded-evidence-argv[2]",
        ),
    ],
)
def test_release_shaped_batch_rejects_invalid_uploaded_evidence_contract(
    mutator: Callable[[dict[str, object]], object],
    expected_path: str,
) -> None:
    """Uploaded profile evidence paths must be relative and self-contained."""
    bundle = _release_batch_bundle()
    _release_bundle_detail(bundle)["profile-telemetry"] = mutator(
        _valid_release_profile_telemetry()
    )

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(expected_path in issue.path for issue in exc_info.value.issues)


def test_release_shaped_batch_rejects_cross_wired_uploaded_binlog() -> None:
    """Multi-build binlog evidence must stay under the owning build subtree."""
    bundle = _release_batch_bundle()
    telemetry = _multi_release_profile_with_uploaded_binlogs()
    release_builds = cast(
        "list[dict[str, object]]",
        telemetry["release-builds"],
    )
    first_phase = cast(
        "list[dict[str, object]]",
        cast("dict[str, object]", release_builds[0]["executor"])["phases"],
    )[0]
    second_phase = cast(
        "list[dict[str, object]]",
        cast("dict[str, object]", release_builds[1]["executor"])["phases"],
    )[0]
    first_phase["binlog-uploaded-evidence-path"] = second_phase[
        "binlog-uploaded-evidence-path"
    ]
    first_phase["binlog-uploaded-evidence-paths"] = [
        second_phase["binlog-uploaded-evidence-path"],
    ]
    first_phase["uploaded-evidence-argv"] = [
        "dotnet",
        "pack",
        f"/bl:{second_phase['binlog-uploaded-evidence-path']}",
    ]
    telemetry["release-build"] = deepcopy(release_builds[0])
    _release_bundle_detail(bundle)["profile-telemetry"] = telemetry

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        ".profile-telemetry.release-builds[0].executor.phases[0]."
        "binlog-uploaded-evidence-path"
        in issue.path
        and "owning release-build" in issue.message
        for issue in exc_info.value.issues
    )
    assert any(
        ".profile-telemetry.release-builds[0].executor.phases[0]."
        "binlog-uploaded-evidence-paths[0]"
        in issue.path
        and "owning release-build" in issue.message
        for issue in exc_info.value.issues
    )
    assert any(
        ".profile-telemetry.release-builds[0].executor.phases[0]."
        "uploaded-evidence-argv[2]"
        in issue.path
        and "owning release-build" in issue.message
        for issue in exc_info.value.issues
    )
    assert any(
        ".profile-telemetry.release-build.executor.phases[0]."
        "binlog-uploaded-evidence-path"
        in issue.path
        and "owning release-build" in issue.message
        for issue in exc_info.value.issues
    )
    assert any(
        ".profile-telemetry.release-build.executor.phases[0]."
        "binlog-uploaded-evidence-paths[0]"
        in issue.path
        and "owning release-build" in issue.message
        for issue in exc_info.value.issues
    )
    assert any(
        ".profile-telemetry.release-build.executor.phases[0]."
        "uploaded-evidence-argv[2]"
        in issue.path
        and "owning release-build" in issue.message
        for issue in exc_info.value.issues
    )


def test_release_shaped_batch_rejects_single_builds_cross_wire() -> None:
    """Single-entry release-builds still scope nested uploaded binlogs."""
    bundle = _release_batch_bundle()
    telemetry = _multi_release_profile_with_uploaded_binlogs()
    release_builds = cast(
        "list[dict[str, object]]",
        telemetry["release-builds"],
    )
    first_build = deepcopy(release_builds[0])
    second_phase = cast(
        "list[dict[str, object]]",
        cast("dict[str, object]", release_builds[1]["executor"])["phases"],
    )[0]
    first_phase = cast(
        "list[dict[str, object]]",
        cast("dict[str, object]", first_build["executor"])["phases"],
    )[0]
    first_phase["binlog-uploaded-evidence-path"] = second_phase[
        "binlog-uploaded-evidence-path"
    ]
    first_phase["uploaded-evidence-argv"] = [
        "dotnet",
        "pack",
        f"/bl:{second_phase['binlog-uploaded-evidence-path']}",
    ]
    telemetry["release-build"] = deepcopy(first_build)
    telemetry["release-builds"] = [first_build]
    phases = cast("list[dict[str, object]]", telemetry["phases"])
    telemetry["phases"] = [phases[0]]
    _set_source_proof_generated_builds_from_profile(bundle, telemetry)
    _release_bundle_detail(bundle)["profile-telemetry"] = telemetry

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        ".profile-telemetry.release-builds[0].executor.phases[0]."
        "binlog-uploaded-evidence-path"
        in issue.path
        and "owning release-build" in issue.message
        for issue in exc_info.value.issues
    )
    assert any(
        ".profile-telemetry.release-builds[0].executor.phases[0]."
        "uploaded-evidence-argv[2]"
        in issue.path
        and "owning release-build" in issue.message
        for issue in exc_info.value.issues
    )


def test_release_shaped_batch_rejects_singular_build_cross_wire() -> None:
    """Singular release-build rejects foreign release-builds subtree claims."""
    bundle = _release_batch_bundle()
    telemetry = _valid_release_profile_telemetry()
    foreign_path = _release_profile_uploaded_binlog_path("f" * 24)
    release_build = cast("dict[str, object]", telemetry["release-build"])
    executor = cast("dict[str, object]", release_build["executor"])
    phase = cast("list[dict[str, object]]", executor["phases"])[0]
    phase["binlog-uploaded-evidence-path"] = foreign_path
    phase["binlog-uploaded-evidence-paths"] = [foreign_path]
    phase["uploaded-evidence-argv"] = ["dotnet", "pack", f"/bl:{foreign_path}"]
    uploaded_files = cast("list[str]", telemetry["uploaded-evidence-files"])
    uploaded_files.append(foreign_path)
    _release_bundle_detail(bundle)["profile-telemetry"] = telemetry

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        ".profile-telemetry.release-build.executor.phases[0]."
        "binlog-uploaded-evidence-path"
        in issue.path
        and "must not use release-builds" in issue.message
        for issue in exc_info.value.issues
    )
    assert any(
        ".profile-telemetry.release-build.executor.phases[0]."
        "binlog-uploaded-evidence-paths[0]"
        in issue.path
        and "must not use release-builds" in issue.message
        for issue in exc_info.value.issues
    )
    assert any(
        ".profile-telemetry.release-build.executor.phases[0]."
        "uploaded-evidence-argv[2]"
        in issue.path
        and "must not use release-builds" in issue.message
        for issue in exc_info.value.issues
    )


def test_release_shaped_batch_rejects_singular_build_plural_scope() -> None:
    """Singular release-build cannot claim release-builds subtree evidence."""
    bundle = _release_batch_bundle()
    telemetry = _valid_release_profile_telemetry()
    release_build = cast("dict[str, object]", telemetry["release-build"])
    bundle_id = cast("str", release_build["bundle-id"])
    plural_path = _release_profile_uploaded_binlog_path(bundle_id)
    executor = cast("dict[str, object]", release_build["executor"])
    phase = cast("list[dict[str, object]]", executor["phases"])[0]
    phase["binlog-uploaded-evidence-path"] = plural_path
    phase["binlog-uploaded-evidence-paths"] = [plural_path]
    phase["uploaded-evidence-argv"] = ["dotnet", "pack", f"/bl:{plural_path}"]
    uploaded_files = cast("list[str]", telemetry["uploaded-evidence-files"])
    uploaded_files.append(plural_path)
    _release_bundle_detail(bundle)["profile-telemetry"] = telemetry

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        ".profile-telemetry.release-build.executor.phases[0]."
        "binlog-uploaded-evidence-path"
        in issue.path
        and "must not use release-builds" in issue.message
        for issue in exc_info.value.issues
    )
    assert any(
        ".profile-telemetry.release-build.executor.phases[0]."
        "binlog-uploaded-evidence-paths[0]"
        in issue.path
        and "must not use release-builds" in issue.message
        for issue in exc_info.value.issues
    )
    assert any(
        ".profile-telemetry.release-build.executor.phases[0]."
        "uploaded-evidence-argv[2]"
        in issue.path
        and "must not use release-builds" in issue.message
        for issue in exc_info.value.issues
    )


def test_release_shaped_batch_rejects_cross_wired_top_level_binlog() -> None:
    """Top-level execute-build binlog evidence is scoped to its own build."""
    bundle = _release_batch_bundle()
    telemetry = _multi_release_profile_with_uploaded_binlogs()
    phases = cast("list[dict[str, object]]", telemetry["phases"])
    second_path = _release_profile_uploaded_binlog_path("f" * 24)
    phases[0]["binlog-uploaded-evidence-path"] = second_path
    phases[0]["binlog-uploaded-evidence-paths"] = [second_path]
    phases[0]["uploaded-evidence-argv"] = [
        "dotnet",
        "pack",
        f"/bl:{second_path}",
    ]
    _set_source_proof_generated_builds_from_profile(bundle, telemetry)
    _release_bundle_detail(bundle)["profile-telemetry"] = telemetry

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        ".profile-telemetry.phases[0].binlog-uploaded-evidence-path"
        in issue.path
        and "owning release-build" in issue.message
        for issue in exc_info.value.issues
    )
    assert any(
        ".profile-telemetry.phases[0].binlog-uploaded-evidence-paths[0]"
        in issue.path
        and "owning release-build" in issue.message
        for issue in exc_info.value.issues
    )
    assert any(
        ".profile-telemetry.phases[0].uploaded-evidence-argv[2]" in issue.path
        and "owning release-build" in issue.message
        for issue in exc_info.value.issues
    )


def test_release_shaped_batch_rejects_unowned_top_level_binlog() -> None:
    """Sidecar-less execute-build binlog evidence still uses owning scope."""
    bundle = _release_batch_bundle()
    telemetry = _multi_release_profile_with_uploaded_binlogs()
    phases = cast("list[dict[str, object]]", telemetry["phases"])
    second_path = _release_profile_uploaded_binlog_path("f" * 24)
    phases[0]["binlog-uploaded-evidence-path"] = second_path
    phases[0]["binlog-uploaded-evidence-paths"] = [second_path]
    phases[0]["uploaded-evidence-argv"] = [
        "dotnet",
        "pack",
        f"/bl:{second_path}",
    ]
    _set_source_proof_generated_builds_from_profile(bundle, telemetry)
    telemetry.pop("release-build", None)
    telemetry.pop("release-builds", None)
    _release_bundle_detail(bundle)["profile-telemetry"] = telemetry

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        ".profile-telemetry.phases[0].binlog-uploaded-evidence-path"
        in issue.path
        and "owning release-build" in issue.message
        for issue in exc_info.value.issues
    )
    assert any(
        ".profile-telemetry.phases[0].uploaded-evidence-argv[2]" in issue.path
        and "owning release-build" in issue.message
        for issue in exc_info.value.issues
    )


def _set_source_proof_generated_builds_from_profile(
    bundle: dict[str, object],
    telemetry: Mapping[str, object],
) -> None:
    detail = _release_bundle_detail(bundle)
    source_proof = cast("dict[str, object]", detail["source-proof"])
    source_proof["generated-builds"] = [
        {
            "request-digest": phase["request-digest"],
            "bundle-id": phase["bundle-id"],
        }
        for phase in cast("list[dict[str, object]]", telemetry["phases"])
        if phase.get("phase") == "release-build-execute-build"
    ]


def _multi_release_profile_with_uploaded_binlogs() -> dict[str, object]:
    telemetry = _valid_release_profile_telemetry()
    first_digest = cast(
        "str", _release_profile_phase(telemetry, "base")["request-digest"]
    )
    second_digest = "f" * 64
    first_build = _release_profile_uploaded_build(
        telemetry,
        request_digest=first_digest,
        bundle_id=first_digest[:24],
    )
    second_build = _release_profile_uploaded_build(
        telemetry,
        request_digest=second_digest,
        bundle_id=second_digest[:24],
    )
    telemetry["phases"] = [
        _release_profile_execute_phase_for_build(telemetry, first_build),
        _release_profile_execute_phase_for_build(telemetry, second_build),
    ]
    telemetry["release-build"] = deepcopy(first_build)
    telemetry["release-builds"] = [first_build, second_build]
    telemetry["uploaded-evidence-files"] = [
        _release_profile_uploaded_binlog_path(first_digest[:24]),
        _release_profile_uploaded_binlog_path(second_digest[:24]),
        (
            "validation-result-profile-evidence/release-builds/"
            f"{first_digest[:24]}/release-build-profile-telemetry.json"
        ),
        (
            "validation-result-profile-evidence/release-builds/"
            f"{second_digest[:24]}/release-build-profile-telemetry.json"
        ),
    ]
    return telemetry


def _release_profile_uploaded_build(
    telemetry: Mapping[str, object],
    *,
    request_digest: str,
    bundle_id: str,
) -> dict[str, object]:
    release_build = deepcopy(
        cast("dict[str, object]", telemetry["release-build"])
    )
    bundle_dir = (
        f".three-ci-validation/work/validation-build/release-shaped/{bundle_id}"
    )
    binlog_path = _release_profile_uploaded_binlog_path(bundle_id)
    release_build.update(
        {
            "bundle-dir": bundle_dir,
            "request-digest": request_digest,
            "bundle-id": bundle_id,
            "profile-root": f"{bundle_dir}/_profile/runs/run-1",
        }
    )
    executor = cast("dict[str, object]", release_build["executor"])
    executor["profile-root"] = f"{bundle_dir}/_profile/runs/run-1"
    phases = cast("list[dict[str, object]]", executor["phases"])
    phases[0]["binlog-uploaded-evidence-path"] = binlog_path
    phases[0]["binlog-uploaded-evidence-paths"] = [binlog_path]
    phases[0]["uploaded-evidence-argv"] = [
        "dotnet",
        "pack",
        f"/bl:{binlog_path}",
    ]
    return release_build


def _release_profile_execute_phase_for_build(
    telemetry: dict[str, object],
    release_build: Mapping[str, object],
) -> dict[str, object]:
    phase = deepcopy(_release_profile_phase(telemetry, "base"))
    for key in (
        "request-digest",
        "bundle-id",
        "work-group-id",
        "runner-family",
    ):
        phase[key] = release_build[key]
    phase["output-path"] = release_build["bundle-dir"]
    return phase


def _release_profile_uploaded_binlog_path(bundle_id: str) -> str:
    return (
        "validation-result-profile-evidence/release-builds/"
        f"{bundle_id}/_profile/runs/run-1/binlogs/build.binlog"
    )


def _profile_telemetry_with_missing_uploaded_binlog(
    telemetry: dict[str, object],
) -> dict[str, object]:
    mutated = deepcopy(telemetry)
    release_build = cast("dict[str, object]", mutated["release-build"])
    executor = cast("dict[str, object]", release_build["executor"])
    phases = cast("list[dict[str, object]]", executor["phases"])
    phases[0]["binlog-uploaded-evidence-path"] = (
        "validation-result-profile-evidence/_profile/runs/run-1/binlogs/"
        "missing.binlog"
    )
    return mutated


def _profile_telemetry_with_local_uploaded_argv_binlog(
    telemetry: dict[str, object],
) -> dict[str, object]:
    mutated = deepcopy(telemetry)
    release_build = cast("dict[str, object]", mutated["release-build"])
    executor = cast("dict[str, object]", release_build["executor"])
    phases = cast("list[dict[str, object]]", executor["phases"])
    phases[0]["uploaded-evidence-argv"] = [
        "dotnet",
        "pack",
        (
            "/bl:.three-ci-validation/work/validation-build/_profile/"
            "runs/run-1/binlogs/0001-dotnet-pack.binlog"
        ),
    ]
    return mutated


def test_release_shaped_batch_rejects_empty_powershell_telemetry_array() -> (
    None
):
    """Empty PowerShell telemetry cannot satisfy release-build telemetry."""
    bundle = _release_batch_bundle()
    telemetry = _valid_release_profile_telemetry()
    telemetry["release-build"] = {
        "bundle-dir": ".three-ci-validation/work/validation-build",
        "powershell": [],
    }
    _release_bundle_detail(bundle)["profile-telemetry"] = telemetry

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        ".profile-telemetry.release-build.powershell" in issue.path
        for issue in exc_info.value.issues
    )


def test_batch_evidence_bundle_accepts_selector_timing() -> None:
    """Selector timing is optional evidence metadata with stable shape."""
    bundle = _release_batch_bundle()
    selector = cast("list[dict[str, object]]", bundle["selector-results"])[0]
    selector["timing"] = {
        "started-at": "2026-06-26T05:00:00.123Z",
        "completed-at": "2026-06-26T05:00:01.456Z",
        "duration-ms": 1333,
    }

    _validate_release_bundle(bundle)


def test_batch_evidence_bundle_accepts_command_timing_projection() -> None:
    """Selector command timings are visible in batch evidence bundles."""
    bundle = _release_batch_bundle()
    selector = cast("list[dict[str, object]]", bundle["selector-results"])[0]
    selector["command-timings"] = [
        {
            "index": 0,
            "label": "python tests",
            "capability": "test",
            "outcome": "success",
            "timing": {
                "started-at": "2026-06-26T05:00:00.123Z",
                "completed-at": "2026-06-26T05:00:01.456Z",
                "duration-ms": 1333,
            },
        }
    ]

    _validate_release_bundle(bundle)


def test_release_shaped_batch_accepts_profile_telemetry_with_timing() -> None:
    """Release-shaped detail validates profile telemetry beside timings."""
    bundle = _release_batch_bundle()
    detail = _release_bundle_detail(bundle)
    profile_telemetry = _valid_release_profile_telemetry()
    detail["profile-telemetry"] = profile_telemetry
    selector = cast("list[dict[str, object]]", bundle["selector-results"])[0]
    selector["timing"] = {
        "started-at": "2026-06-26T05:00:00.123Z",
        "completed-at": "2026-06-26T05:00:02.456Z",
        "duration-ms": 2333,
    }
    selector["command-timings"] = [
        {
            "index": 0,
            "label": "release-shaped-artifact",
            "capability": None,
            "outcome": "success",
            "timing": {
                "started-at": "2026-06-26T05:00:00.500Z",
                "completed-at": "2026-06-26T05:00:01.500Z",
                "duration-ms": 1000,
            },
        }
    ]

    _validate_release_bundle(bundle)

    assert detail["profile-telemetry"] == profile_telemetry
    assert "timing" in selector
    assert "command-timings" in selector


def test_orchestrator_release_shaped_batch_accepts_profile_and_timing() -> None:
    """Orchestrator release-shaped bundles validate profile plus timing."""
    _, manifest = _release_plan_and_manifest()
    batch = next(
        batch
        for batch in cast("list[dict[str, object]]", manifest["batches"])
        if cast("dict[str, object]", batch["compatibility-profile"])[
            "release-shaped-profile"
        ]
        is not None
    )
    bundle = _release_batch_bundle()
    bundle["writer"] = _writer_with_observed_identity(
        manifest,
        cast("str", batch["batch-id"]),
        writer_context="orchestrator",
    )
    bundle["orchestrator-step"] = _orchestrator_step_for_batch(batch)
    detail = _release_bundle_detail(bundle)
    detail["profile-telemetry"] = _valid_release_profile_telemetry()
    selector = cast("list[dict[str, object]]", bundle["selector-results"])[0]
    selector["timing"] = {
        "started-at": "2026-06-26T05:00:00.123Z",
        "completed-at": "2026-06-26T05:00:02.456Z",
        "duration-ms": 2333,
    }
    selector["command-timings"] = [
        {
            "index": 0,
            "label": "release-shaped-artifact",
            "capability": None,
            "outcome": "success",
            "timing": {
                "started-at": "2026-06-26T05:00:00.500Z",
                "completed-at": "2026-06-26T05:00:01.500Z",
                "duration-ms": 1000,
            },
        }
    ]

    _validate_release_bundle(bundle)

    assert bundle["orchestrator-step"]
    assert detail["profile-telemetry"]
    assert selector["timing"]
    assert selector["command-timings"]


def test_batch_evidence_bundle_rejects_malformed_selector_timing() -> None:
    """Selector timing rejects non-stable timestamp and duration shapes."""
    bundle = _release_batch_bundle()
    selector = cast("list[dict[str, object]]", bundle["selector-results"])[0]
    selector["timing"] = {
        "started-at": "2026-06-26T05:00:00Z",
        "completed-at": "2026-06-26T05:00:01.456Z",
        "duration-ms": -1,
    }

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    messages = "\n".join(str(issue) for issue in exc_info.value.issues)
    assert ".selector-results[0].timing.started-at" in messages
    assert ".selector-results[0].timing.duration-ms" in messages


def test_batch_evidence_bundle_rejects_explicit_null_selector_timing() -> None:
    """Missing timing is optional, but explicit null timing is invalid."""
    bundle = _release_batch_bundle()
    selector = cast("list[dict[str, object]]", bundle["selector-results"])[0]
    selector["timing"] = None

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    messages = "\n".join(str(issue) for issue in exc_info.value.issues)
    assert "$.selector-results[0].timing" in messages
    assert "must be an object" in messages


def test_batch_evidence_bundle_rejects_impossible_selector_timing() -> None:
    """Timing duration must be monotonic and match wall-clock elapsed time."""
    bundle = _release_batch_bundle()
    selector = cast("list[dict[str, object]]", bundle["selector-results"])[0]
    selector["timing"] = {
        "started-at": "2026-06-26T05:00:02.000Z",
        "completed-at": "2026-06-26T05:00:01.000Z",
        "duration-ms": 1000,
    }

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    messages = "\n".join(str(issue) for issue in exc_info.value.issues)
    assert ".selector-results[0].timing.completed-at" in messages


def test_batch_evidence_bundle_rejects_inconsistent_selector_timing() -> None:
    """Timing duration has an explicit tolerance against elapsed timestamps."""
    bundle = _release_batch_bundle()
    selector = cast("list[dict[str, object]]", bundle["selector-results"])[0]
    selector["timing"] = {
        "started-at": "2026-06-26T05:00:00.000Z",
        "completed-at": "2026-06-26T05:00:10.000Z",
        "duration-ms": 1000,
    }

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    messages = "\n".join(str(issue) for issue in exc_info.value.issues)
    assert "must match completed-at minus started-at within 1000ms" in messages


def test_release_shaped_batch_rejects_missing_obligation_results() -> None:
    """Successful release-shaped evidence must carry obligation results."""
    bundle = _release_batch_bundle()
    del _release_bundle_detail(bundle)["artifact-obligation-results"]

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        "artifact-obligation-results" in issue.path
        for issue in cast("ContractValidationError", exc_info.value).issues
    )


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("work-group-id", "wrong-work-group"),
        ("coverage-target", {"type": "artifact-obligation", "id": "wrong"}),
        ("observed-commit-sha", "1" * 40),
    ],
)
def test_release_shaped_batch_rejects_unbound_source_proof(
    field: str,
    value: object,
) -> None:
    """No-publish source proof must bind to selector identity and commit."""
    bundle = _release_batch_bundle()
    source_proof = cast(
        "dict[str, object]",
        _release_bundle_detail(bundle)["source-proof"],
    )
    source_proof[field] = value

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(field in issue.path for issue in exc_info.value.issues)


def test_release_shaped_batch_rejects_source_proof_digest_mismatch() -> None:
    """No-publish source proof digests must equal observed artifact digests."""
    bundle = _release_batch_bundle()
    source_proof = cast(
        "dict[str, object]",
        _release_bundle_detail(bundle)["source-proof"],
    )
    cast("list[dict[str, object]]", source_proof["artifact-digests"])[0][
        "digest"
    ] = "b" * 64

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any("digest" in issue.path for issue in exc_info.value.issues)


def test_release_shaped_batch_rejects_reused_receipt_public_evidence() -> None:
    """Public batch bundles cannot self-attest reused receipt evidence."""
    bundle = _release_batch_bundle()
    detail = _release_bundle_detail(bundle)
    detail["evidence-source"] = "reused-validation-receipt"
    detail["reused-receipt"] = {
        "artifact-ref": (
            "ci-validation/receipts/25887422010/1/wg-release/receipt.json"
        ),
        "receipt-id": "receipt-release",
        "receipt-content-digest": "a" * 64,
        "observed-commit-sha": TREE_SHA,
    }
    detail.pop("source-proof", None)

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        issue.path.endswith(".evidence-source")
        for issue in exc_info.value.issues
    )


def test_release_shaped_batch_rejects_obligation_detail_plan_mismatch() -> None:
    """Release-shaped obligation detail must equal the frozen plan and facts."""
    bundle = _release_batch_bundle()
    result = cast(
        "dict[str, object]",
        cast(
            "list[object]",
            _release_bundle_detail(bundle)["artifact-obligation-results"],
        )[0],
    )
    result["profile-coverage"] = ["forged-profile"]

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        "profile-coverage" in issue.path for issue in exc_info.value.issues
    )


def test_release_batch_allows_blocking_unavailable_shape() -> None:
    """Release-shaped blocking rows carry per-obligation unavailable detail."""
    bundle = _release_batch_bundle()
    selector = cast(
        "dict[str, object]",
        cast("list[object]", bundle["selector-results"])[0],
    )
    evidence = cast("dict[str, object]", selector["evidence"])
    category_result = cast("dict[str, object]", evidence["category-result"])
    detail = cast("dict[str, object]", category_result["detail"])
    result = cast(
        "dict[str, object]",
        cast("list[object]", detail["artifact-obligation-results"])[0],
    )
    diagnostic = _diagnostic(
        "release-shaped/artifact-shape-unconfirmed",
        code="artifact-shape-unconfirmed",
        detail="unconfirmed-provenance",
        severity="fail-closed",
        verdict_effect="fail-closed",
    )
    selector["outcome"] = "blocking-failure"
    selector["diagnostics"] = [diagnostic]
    evidence["artifact-refs"] = []
    category_result["outcome"] = "blocking-failure"
    category_result["diagnostics"] = [diagnostic]
    category_result["artifact-refs"] = []
    detail.pop("evidence-source", None)
    detail.pop("source-proof", None)
    result["outcome"] = "blocking-failure"
    result["diagnostics"] = [diagnostic]
    artifact = cast("dict[str, object]", result["artifact"])
    artifact["outcome"] = "blocking-failure"
    artifact["diagnostics"] = [diagnostic]
    observed = cast("dict[str, object]", artifact["observed"])
    for item in cast("list[dict[str, object]]", observed["digests"]):
        item["digest"] = ""
        item["digest-available"] = False
        item["diagnostics"] = [diagnostic]
    receipt = cast("dict[str, object]", result["release-receipt"])
    receipt["schema-checked"] = False
    receipt["outcome"] = "blocking-failure"
    receipt["diagnostics"] = [diagnostic]

    _validate_release_bundle(bundle)


def test_release_batch_rejects_blocking_available_malformed_digest() -> None:
    """Available non-success release-shaped digests must still be valid."""
    bundle = _release_batch_bundle()
    selector = cast(
        "dict[str, object]",
        cast("list[object]", bundle["selector-results"])[0],
    )
    evidence = cast("dict[str, object]", selector["evidence"])
    category_result = cast("dict[str, object]", evidence["category-result"])
    detail = cast("dict[str, object]", category_result["detail"])
    result = cast(
        "dict[str, object]",
        cast("list[object]", detail["artifact-obligation-results"])[0],
    )
    diagnostic = _diagnostic(
        "release-shaped/artifact-shape-unconfirmed",
        code="artifact-shape-unconfirmed",
        detail="unconfirmed-provenance",
        severity="fail-closed",
        verdict_effect="fail-closed",
    )
    selector["outcome"] = "blocking-failure"
    selector["diagnostics"] = [diagnostic]
    evidence["artifact-refs"] = []
    category_result["outcome"] = "blocking-failure"
    category_result["diagnostics"] = [diagnostic]
    category_result["artifact-refs"] = []
    detail.pop("evidence-source", None)
    detail.pop("source-proof", None)
    result["outcome"] = "blocking-failure"
    result["diagnostics"] = [diagnostic]
    artifact = cast("dict[str, object]", result["artifact"])
    artifact["outcome"] = "blocking-failure"
    artifact["diagnostics"] = [diagnostic]
    observed = cast("dict[str, object]", artifact["observed"])
    for item in cast("list[dict[str, object]]", observed["digests"]):
        item["digest"] = "not-a-digest"
        item["digest-available"] = True
        item["diagnostics"] = [diagnostic]
    receipt = cast("dict[str, object]", result["release-receipt"])
    receipt["schema-checked"] = False
    receipt["outcome"] = "blocking-failure"
    receipt["diagnostics"] = [diagnostic]

    with pytest.raises(ContractValidationError) as exc_info:
        _validate_release_bundle(bundle)

    assert any(
        issue.path.endswith(".digest") and issue.message == "must be a digest"
        for issue in exc_info.value.issues
    )


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


def test_materializer_splits_compatible_ancestor_descendant_batches() -> None:
    """Compatible non-contiguous ancestors must not create a batch DAG cycle."""
    plan = _plan()
    _add_transitive_work_group(plan)
    transitive_group = next(
        group
        for group in cast("list[dict[str, object]]", plan["work-groups"])
        if group["work-group-id"] == "wg-transitive-gate"
    )
    transitive_group["ecosystem"] = "python"
    plan["plan-digest"] = ci_validation_plan_digest(plan)

    materialization = materialize_ci_validation_execution_batches(
        plan=plan,
        **_authorizing_context_kwargs(),
        created_at=CREATED_AT,
        execution_workflow="CI Validation",
    )
    manifest = cast("dict[str, object]", materialization.manifest)

    validate_ci_validation_execution_batch_manifest(
        manifest,
        plan=plan,
        **_authorizing_context_kwargs(),
    )
    batch_selector_ids = [
        {
            selector["work-group-id"]
            for selector in cast(
                "list[dict[str, object]]", batch["ordered-selectors"]
            )
        }
        for batch in cast("list[dict[str, object]]", manifest["batches"])
    ]
    assert not any(
        {"wg-python-gate", "wg-transitive-gate"}.issubset(work_group_ids)
        for work_group_ids in batch_selector_ids
    )


def test_materializer_allows_cross_family_batch_dependencies() -> None:
    """Batch manifests may preserve DAG edges across runner families."""
    plan = _plan()
    _add_dependent_work_group(plan)
    dependent_group = next(
        group
        for group in cast("list[dict[str, object]]", plan["work-groups"])
        if group["work-group-id"] == "wg-dependent-gate"
    )
    dependent_group["runner-family"] = "windows"
    dependent_group["ecosystem"] = "dotnet"
    plan["plan-digest"] = ci_validation_plan_digest(plan)

    materialization = materialize_ci_validation_execution_batches(
        plan=plan,
        **_authorizing_context_kwargs(),
        created_at=CREATED_AT,
        execution_workflow="CI Validation",
    )
    batches = cast(
        "list[dict[str, object]]", materialization.manifest["batches"]
    )
    dependent_batch = next(
        batch
        for batch in batches
        if any(
            selector["work-group-id"] == "wg-dependent-gate"
            for selector in cast(
                "list[dict[str, object]]", batch["ordered-selectors"]
            )
        )
    )
    dependency_id = cast("list[str]", dependent_batch["depends-on-batches"])[0]
    upstream_batch = next(
        batch for batch in batches if batch["batch-id"] == dependency_id
    )

    assert dependent_batch["runner-family"] == "windows"
    assert upstream_batch["runner-family"] != dependent_batch["runner-family"]


def test_manifest_validation_allows_cross_family_batch_dependencies() -> None:
    """Manifest validation preserves cross-family batch DAG semantics."""
    plan = _plan()
    _add_dependent_work_group(plan)
    dependent_group = next(
        group
        for group in cast("list[dict[str, object]]", plan["work-groups"])
        if group["work-group-id"] == "wg-dependent-gate"
    )
    dependent_group["runner-family"] = "windows"
    dependent_group["ecosystem"] = "dotnet"
    plan["plan-digest"] = ci_validation_plan_digest(plan)
    materialization = materialize_ci_validation_execution_batches(
        plan=plan,
        **_authorizing_context_kwargs(),
        created_at=CREATED_AT,
        execution_workflow="CI Validation",
    )
    manifest = cast("dict[str, object]", deepcopy(materialization.manifest))

    validate_ci_validation_execution_batch_manifest(
        manifest,
        plan=plan,
        **_authorizing_context_kwargs(),
    )


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


def _writer_with_observed_identity(
    manifest: dict[str, object],
    batch_id: str,
    *,
    writer_context: str = "direct",
) -> dict[str, object]:
    writer = _writer_for_batch(manifest, batch_id)
    observed_matrix = cast("dict[str, object]", writer["observed-matrix"])
    if writer_context == "orchestrator":
        batch = next(
            item
            for item in cast("list[dict[str, object]]", manifest["batches"])
            if item["batch-id"] == batch_id
        )
        observed_matrix = {}
        writer["identity-source"] = "github-actions-orchestrator-job-context"
        writer["observed-job"] = (
            f"execution-batch-{batch['runner-family']}-orchestrator"
        )
        writer["observed-matrix"] = observed_matrix
        writer["logical-batch-identity"] = _execution_batch_matrix_identity(
            batch,
        )
        writer["observed-orchestrator-slot-index"] = "0"
    writer["observed-writer-identity"] = ci_validation_writer_id(
        workflow=cast("str", writer["observed-workflow"]),
        job=cast("str", writer["observed-job"]),
        matrix=observed_matrix,
    )
    return writer


def _orchestrator_step_for_batch(
    batch: Mapping[str, object],
    *,
    slot_index: str = "0",
) -> dict[str, object]:
    return {
        "runner-family": batch["runner-family"],
        "slot-index": slot_index,
        "dependency-selection": {
            "timing": {
                "started-at": "2026-06-26T05:00:00.000Z",
                "completed-at": "2026-06-26T05:00:00.250Z",
                "duration-ms": 250,
            },
            "selected-batch-id": batch["batch-id"],
            "waiting-batch-ids": [],
        },
    }


def _orchestrator_writer_bundle(
    plan: dict[str, object],
    manifest: dict[str, object],
) -> dict[str, object]:
    batch = cast("list[dict[str, object]]", manifest["batches"])[0]
    bundle = _bundle(plan, manifest)
    bundle["writer"] = _writer_with_observed_identity(
        manifest,
        cast("str", batch["batch-id"]),
        writer_context="orchestrator",
    )
    bundle["orchestrator-step"] = _orchestrator_step_for_batch(batch)
    return bundle


@pytest.mark.parametrize(
    "writer_context", ["direct", "orchestrator"], ids=["direct", "orchestrator"]
)
def test_batch_bundle_rejects_forged_observed_writer_identity(
    writer_context: str,
) -> None:
    """Observed writer identity is recomputed from observed job context."""
    plan = _plan()
    manifest = _manifest(plan)
    batch = cast("list[dict[str, object]]", manifest["batches"])[0]
    bundle = _bundle(plan, manifest)
    writer = _writer_with_observed_identity(
        manifest,
        cast("str", batch["batch-id"]),
        writer_context=writer_context,
    )
    writer["observed-writer-identity"] = ci_validation_writer_id(
        workflow="Forged CI",
        job="forged-job",
        matrix={},
    )
    bundle["writer"] = writer

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_batch_evidence_bundle(
            bundle,
            plan=plan,
            execution_batch_manifest=manifest,
            **_authorizing_context_kwargs(),
        )

    assert any(
        issue.path == "$.writer.observed-writer-identity"
        for issue in error.value.issues
    )


def test_batch_bundle_rejects_orchestrator_writer_without_slot_index() -> None:
    """Physical orchestrator writer context must include its slot identity."""
    plan = _plan()
    manifest = _manifest(plan)
    batch = cast("list[dict[str, object]]", manifest["batches"])[0]
    bundle = _bundle(plan, manifest)
    writer = _writer_with_observed_identity(
        manifest,
        cast("str", batch["batch-id"]),
        writer_context="orchestrator",
    )
    del writer["observed-orchestrator-slot-index"]
    bundle["writer"] = writer

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_batch_evidence_bundle(
            bundle,
            plan=plan,
            execution_batch_manifest=manifest,
            **_authorizing_context_kwargs(),
        )

    assert any(
        issue.path == "$.writer.observed-orchestrator-slot-index"
        for issue in error.value.issues
    )


def test_batch_bundle_direct_writer_does_not_require_slot_index() -> None:
    """Direct execution batch writer context has no orchestrator slot."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    writer = cast("dict[str, object]", bundle["writer"])
    writer.pop("observed-orchestrator-slot-index", None)

    validate_ci_validation_batch_evidence_bundle(
        bundle,
        plan=plan,
        execution_batch_manifest=manifest,
        **_authorizing_context_kwargs(),
    )


def test_batch_bundle_rejects_direct_writer_with_slot_index() -> None:
    """Direct execution batch writer must not claim an orchestrator slot."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    writer = cast("dict[str, object]", bundle["writer"])
    writer["observed-orchestrator-slot-index"] = "0"

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_batch_evidence_bundle(
            bundle,
            plan=plan,
            execution_batch_manifest=manifest,
            **_authorizing_context_kwargs(),
        )

    assert any(
        issue.path == "$.writer.observed-orchestrator-slot-index"
        for issue in error.value.issues
    )


def test_batch_bundle_rejects_direct_writer_with_orchestrator_step() -> None:
    """Direct execution batch writer bundles must omit orchestrator step."""
    plan = _plan()
    manifest = _manifest(plan)
    batch = cast("list[dict[str, object]]", manifest["batches"])[0]
    bundle = _bundle(plan, manifest)
    bundle["orchestrator-step"] = _orchestrator_step_for_batch(batch)

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_batch_evidence_bundle(
            bundle,
            plan=plan,
            execution_batch_manifest=manifest,
            **_authorizing_context_kwargs(),
        )

    assert any(
        issue.path == "$.orchestrator-step" for issue in error.value.issues
    )


def test_batch_bundle_orchestrator_writer_requires_orchestrator_step() -> None:
    """Physical orchestrator writer bundles must expose step evidence."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _orchestrator_writer_bundle(plan, manifest)
    del bundle["orchestrator-step"]

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_batch_evidence_bundle(
            bundle,
            plan=plan,
            execution_batch_manifest=manifest,
            **_authorizing_context_kwargs(),
        )

    assert any(
        issue.path == "$.orchestrator-step" for issue in error.value.issues
    )


@pytest.mark.parametrize(
    ("field", "value", "expected_path"),
    [
        (
            "runner-family",
            "windows",
            "$.orchestrator-step.runner-family",
        ),
        ("slot-index", "1", "$.orchestrator-step.slot-index"),
        (
            "selected-batch-id",
            "forged-batch",
            "$.orchestrator-step.dependency-selection.selected-batch-id",
        ),
    ],
    ids=["runner-family", "slot-index", "selected-batch-id"],
)
def test_batch_bundle_orchestrator_step_must_match_writer_and_batch(
    field: str,
    value: str,
    expected_path: str,
) -> None:
    """Orchestrator step metadata is bound to the selected batch and slot."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _orchestrator_writer_bundle(plan, manifest)
    orchestrator_step = cast("dict[str, object]", bundle["orchestrator-step"])
    if field == "selected-batch-id":
        dependency_selection = cast(
            "dict[str, object]",
            orchestrator_step["dependency-selection"],
        )
        dependency_selection[field] = value
    else:
        orchestrator_step[field] = value

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_batch_evidence_bundle(
            bundle,
            plan=plan,
            execution_batch_manifest=manifest,
            **_authorizing_context_kwargs(),
        )

    assert any(issue.path == expected_path for issue in error.value.issues)


@pytest.mark.parametrize(
    "writer_context", ["direct", "orchestrator"], ids=["direct", "orchestrator"]
)
def test_batch_bundle_freeze_rejects_forged_observed_writer_identity(
    writer_context: str,
) -> None:
    """Freeze path applies the same observed writer identity check."""
    plan = _plan()
    manifest = _manifest(plan)
    batch = cast("list[dict[str, object]]", manifest["batches"])[0]
    writer = _writer_with_observed_identity(
        manifest,
        cast("str", batch["batch-id"]),
        writer_context=writer_context,
    )
    writer["observed-writer-identity"] = ci_validation_writer_id(
        workflow="Forged CI",
        job="forged-job",
        matrix={},
    )

    with pytest.raises(ContractValidationError) as error:
        freeze_ci_validation_batch_evidence_bundle(
            plan=plan,
            execution_batch_manifest=manifest,
            batch_id=cast("str", batch["batch-id"]),
            selector_results=[_selector_result(plan, manifest)],
            writer=writer,
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
        issue.path == "$.writer.observed-writer-identity"
        for issue in error.value.issues
    )


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


def _summary_affected_range(plan: Mapping[str, object]) -> dict[str, object]:
    affected = cast("Mapping[str, object]", plan["affected-range"])
    return {
        "status": affected["status"],
        "base-sha": affected["base-sha"],
        "base-tip-sha": affected["base-tip-sha"],
        "head-sha": affected["head-sha"],
        "changed-files-hash": affected["changed-files-hash"] or None,
    }


def _summary_failure(
    summary: Mapping[str, object],
    index: int = 0,
) -> dict[str, object]:
    failures = cast("Sequence[dict[str, object]]", summary["failures"])
    return failures[index]


def _record_diagnostic(
    record: Mapping[str, object],
    index: int = 0,
) -> dict[str, object]:
    diagnostics = cast("Sequence[dict[str, object]]", record["diagnostics"])
    return diagnostics[index]


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


def _without_final_producer_diagnostics(
    summary: Mapping[str, object],
) -> list[dict[str, object]]:
    diagnostics = cast("list[dict[str, object]]", summary["diagnostics"])
    return [
        diagnostic
        for diagnostic in diagnostics
        if diagnostic["code"] != "final-producer-unverified"
    ]


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
                "artifact-ref": (
                    ci_validation_aggregate_summary_artifact_ref(
                        run_id=RUN_ID,
                        run_attempt=RUN_ATTEMPT,
                    )
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


def _fail_closed_aggregate_manifest(
    plan: dict[str, object],
    manifest: dict[str, object],
    changed_files_snapshot: dict[str, object],
) -> dict[str, object]:
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
            None,
            required=False,
            admissibility="not-required",
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
    cast("dict[str, object]", input_artifacts["changed-files-snapshot"])[
        "content-digest"
    ] = cast("dict[str, object]", plan["affected-range"])["changed-files-hash"]
    cast("dict[str, object]", input_artifacts["execution-batch-manifest"])[
        "content-digest"
    ] = ci_validation_execution_batch_manifest_payload_digest(manifest)
    budget = cast("dict[str, object]", manifest["budget"])
    return freeze_ci_validation_aggregate_evidence_manifest(
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
            "observed-prefixed-artifact-count-lower-bound": (
                budget["pre-final-validation-artifacts"]
            ),
            "max-prefixed-validation-artifacts": 18,
            "diagnostics": [],
        },
        pre_final_validation_artifacts=cast(
            "int",
            budget["pre-final-validation-artifacts"],
        ),
        namespace_closed_at=CREATED_AT,
        plan=plan,
        execution_batch_manifest=manifest,
        request=_request_document(),
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=None,
    )


def _fail_closed_aggregate_summary(
    plan: dict[str, object],
    manifest: dict[str, object],
    aggregate_manifest: dict[str, object],
    changed_files_snapshot: dict[str, object],
) -> dict[str, object]:
    manifest_ref = cast("str", aggregate_manifest["artifact-ref"])
    manifest_digest = ci_validation_aggregate_evidence_manifest_payload_digest(
        aggregate_manifest,
    )
    budget = cast("dict[str, object]", manifest["budget"])
    failure_diagnostic = _diagnostic(
        "fail-closed/unknown-change",
        code="unknown-change",
        detail="incomplete",
        message="Changed files could not be classified.",
        severity="fail-closed",
        verdict_effect="fail-closed",
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
                "authority-diagnostics": [],
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
        reason={
            "invalid-plan": False,
            "fail-closed": True,
            "required-evidence-missing": False,
            "required-evidence-skipped": False,
            "blocking-validation-failure": False,
            "inadmissible-batch-evidence": False,
            "namespace-closure-failure": False,
            "required-input-artifact-failure": False,
            "aggregate-summary-without-manifest": False,
            "final-producer-unverified": False,
            "final-evidence-failure": False,
        },
        budgets={
            "pre-final-validation-artifacts": budget[
                "pre-final-validation-artifacts"
            ],
            "expected-final-validation-artifacts": budget[
                "expected-final-validation-artifacts"
            ],
            "expected-actual-validation-artifacts": budget[
                "actual-validation-artifacts"
            ],
            "max-validation-artifacts": budget["max-validation-artifacts"],
            "actual-execution-batches": 0,
            "actual-total-jobs": 0,
            "actual-windows-jobs": 0,
            "aggregate-duration-seconds": 10,
            "aggregate-target-duration-seconds": 60,
            "aggregate-max-duration-seconds": 120,
        },
        diagnostics=[failure_diagnostic],
        batch_bundles=[],
        evidence_results=[],
        failures=[
            {
                "kind": "fail-closed",
                "diagnostic": failure_diagnostic,
                "message": "Changed files could not be classified.",
                "evidence-expectation-id": None,
                "work-group-id": None,
                "batch-id": None,
                "bundle-id": None,
            }
        ],
        work_groups={
            "executable-required": 0,
            "required-succeeded": 0,
            "required-failed": 0,
            "required-skipped": 0,
            "required-missing": 0,
            "terminal-aggregation": "present",
        },
        plan=plan,
        aggregate_evidence_manifest_document=aggregate_manifest,
        admitted_batch_evidence_bundles=[],
        execution_batch_manifest=manifest,
        request_document=_request_document(),
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=None,
    )


def _invalid_planning_input_manifest(
    input_name: str,
    detail: str,
) -> dict[str, object]:
    request = _request_document()
    request_artifact = _input_artifact(
        ci_validation_request_artifact_ref(
            run_id=RUN_ID,
            run_attempt=RUN_ATTEMPT,
        ),
        required=True,
        admissibility="valid",
    )
    request_artifact["content-digest"] = request["request-digest"]
    input_artifacts = {
        "request": request_artifact,
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
            None,
            required=True,
            admissibility="missing",
        ),
    }
    invalid_artifact = cast("dict[str, object]", input_artifacts[input_name])
    invalid_artifact["admissibility"] = "inadmissible"
    invalid_artifact["diagnostics"] = [
        _diagnostic(
            "invalid-plan"
            if detail == "plan-missing"
            else f"invalid-plan/{detail}",
            code="invalid-plan",
            detail=detail,
            message=(
                CI_VALIDATION_INVALID_PLAN_MISSING_MESSAGE
                if detail == "plan-missing"
                else CI_VALIDATION_INVALID_PLAN_NON_AUTHORITATIVE_MESSAGE
            ),
            severity="fail-closed",
            verdict_effect="fail-closed",
            source_id=cast("str | None", invalid_artifact["artifact-ref"]),
        )
    ]
    return {
        "api-version": API_VERSIONS_BY_KIND[
            CiValidationKind.AGGREGATE_EVIDENCE_MANIFEST.value
        ],
        "kind": CiValidationKind.AGGREGATE_EVIDENCE_MANIFEST.value,
        "created-at": CREATED_AT,
        "repository": {"owner": "hcoona", "name": "three"},
        "run": {
            "workflow": "CI Validation",
            "run-id": RUN_ID,
            "run-attempt": RUN_ATTEMPT,
        },
        "schema-diagnostics": [],
        "artifact-ref": ci_validation_aggregate_evidence_manifest_artifact_ref(
            run_id=RUN_ID,
            run_attempt=RUN_ATTEMPT,
        ),
        "plan-id": None,
        "plan-digest": None,
        "input-artifacts": input_artifacts,
        "batch-bundles": [],
        "unexpected-contract-artifacts": [],
        "namespace-overflow": {
            "detected": False,
            "observed-prefixed-artifact-count-lower-bound": 5,
            "max-prefixed-validation-artifacts": 18,
            "diagnostics": [],
        },
        "projection-authority": None,
        "pre-final-validation-artifacts": 5,
        "namespace-closed-at": CREATED_AT,
        "proof-admissibility": "validation-only",
    }


def _authoritative_invalid_planning_input_manifest(
    plan: dict[str, object],
    input_name: str,
    detail: str,
) -> dict[str, object]:
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    _mark_aggregate_manifest_retained_authority(aggregate_manifest)
    invalid_artifact = cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            input_name
        ],
    )
    invalid_artifact["admissibility"] = "inadmissible"
    invalid_artifact["diagnostics"] = [
        _diagnostic(
            "invalid-plan"
            if detail == "plan-missing"
            else f"invalid-plan/{detail}",
            code="invalid-plan",
            detail=detail,
            message=(
                CI_VALIDATION_INVALID_PLAN_MISSING_MESSAGE
                if detail == "plan-missing"
                else CI_VALIDATION_INVALID_PLAN_NON_AUTHORITATIVE_MESSAGE
            ),
            severity="fail-closed",
            verdict_effect="fail-closed",
            source_id=cast("str | None", invalid_artifact["artifact-ref"]),
        )
    ]
    return aggregate_manifest


def _freeze_invalid_planning_input_summary(
    aggregate_manifest: dict[str, object],
    *,
    plan: dict[str, object] | None = None,
    final_manifest_producer_verified: bool = True,
) -> dict[str, object]:
    manifest_digest = ci_validation_aggregate_evidence_manifest_payload_digest(
        aggregate_manifest,
    )
    manifest_ref = cast("str", aggregate_manifest["artifact-ref"])
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
                "producer-verified": final_manifest_producer_verified,
            },
            "aggregate-summary": {
                "artifact-ref": ci_validation_aggregate_summary_artifact_ref(
                    run_id=RUN_ID,
                    run_attempt=RUN_ATTEMPT,
                ),
            },
        },
        validation_tree={"commit-sha": "9" * 40, "ref": "refs/heads/main"},
        affected_range={
            "status": "available",
            "base-sha": "1" * 40,
            "base-tip-sha": "2" * 40,
            "head-sha": "3" * 40,
            "changed-files-hash": "4" * 64,
        },
        request={"artifact-ref": None, "request-digest": None},
        scheduled_full={"enabled": True},
        verdict="failed",
        reason={
            "invalid-plan": False,
            "fail-closed": False,
            "required-evidence-missing": False,
            "required-evidence-skipped": False,
            "blocking-validation-failure": False,
            "inadmissible-batch-evidence": False,
            "namespace-closure-failure": False,
            "final-evidence-failure": True,
        },
        budgets={
            "pre-final-validation-artifacts": 5,
            "expected-final-validation-artifacts": 2,
            "expected-actual-validation-artifacts": 7,
            "max-validation-artifacts": 20,
            "actual-execution-batches": 1,
            "actual-total-jobs": 1,
            "actual-windows-jobs": 0,
            "aggregate-duration-seconds": 10,
            "aggregate-target-duration-seconds": 60,
            "aggregate-max-duration-seconds": 120,
        },
        diagnostics=[],
        batch_bundles=[],
        evidence_results=[],
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
    _mark_aggregate_manifest_retained_authority(aggregate_manifest)
    aggregate_manifest["projection-authority"] = None


def _mark_aggregate_manifest_retained_authority(
    aggregate_manifest: dict[str, object],
) -> None:
    aggregate_manifest["batch-bundles"] = []
    aggregate_manifest["pre-final-validation-artifacts"] = 5
    cast("dict[str, object]", aggregate_manifest["namespace-overflow"])[
        "observed-prefixed-artifact-count-lower-bound"
    ] = 5
    _set_input_absent(
        aggregate_manifest,
        "execution-batch-manifest",
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
    context = _authorizing_context_kwargs()

    with pytest.raises(ContractValidationError):
        validate_ci_validation_batch_evidence_bundle(
            bundle,
            plan=plan,
            execution_batch_manifest=manifest,
            request=(None if missing_key == "request" else context["request"]),
            changed_files_snapshot=(
                None
                if missing_key == "changed_files_snapshot"
                else context["changed_files_snapshot"]
            ),
            fact_snapshot=(
                None
                if missing_key == "fact_snapshot"
                else context["fact_snapshot"]
            ),
            expected_run_id=(
                None
                if missing_key == "expected_run_id"
                else context["expected_run_id"]
            ),
            expected_run_attempt=(
                None
                if missing_key == "expected_run_attempt"
                else context["expected_run_attempt"]
            ),
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
    summary["plan-id"] = plan["plan-id"]
    summary["plan-digest"] = plan["plan-digest"]

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
        "physical-artifact-name": (
            f"three-ci-validation-{RUN_ID}-{RUN_ATTEMPT}-{index:064x}"
        ),
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


def _append_summary_namespace_failure(
    summary: dict[str, object],
    *,
    diagnostic_id: str,
    detail: str,
    message: str,
) -> None:
    _append_summary_kind_failure(
        summary,
        "namespace-closure-failure",
        _diagnostic(
            diagnostic_id,
            code="namespace-closure-failure",
            detail=detail,
            severity="fail-closed",
            verdict_effect="fail-closed",
        ),
        message,
    )


def _mark_summary_namespace_failure(
    summary: dict[str, object],
    *,
    include_overflow: bool = False,
) -> None:
    summary["verdict"] = "failed"
    reason = cast("dict[str, object]", summary["reason"])
    reason["fail-closed"] = True
    reason["namespace-closure-failure"] = True
    _append_summary_namespace_failure(
        summary,
        diagnostic_id="namespace-closure-failure",
        detail="unexpected-contract-artifact",
        message="Validation artifact namespace was not closed.",
    )
    if include_overflow:
        _append_summary_namespace_failure(
            summary,
            diagnostic_id="namespace-closure-failure/namespace-overflow",
            detail="namespace-overflow",
            message="Validation artifact namespace overflowed.",
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
    reason["required-input-artifact-failure"] = True
    reason["final-evidence-failure"] = False
    _append_summary_kind_failure(
        summary,
        "required-input-artifact-failure",
        _diagnostic(
            "required-input-artifact-failure",
            code="required-input-artifact-failure",
            detail="required-input-artifact-failure",
            severity="fail-closed",
            verdict_effect="fail-closed",
        ),
        "Required input artifact forced fail-closed.",
    )
    _sort_summary_failures(summary)


def _mark_summary_duration_overrun(summary: dict[str, object]) -> None:
    cast("dict[str, object]", summary["budgets"])[
        "aggregate-duration-seconds"
    ] = 121


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
                message=CI_VALIDATION_INVALID_PLAN_MISSING_MESSAGE,
                severity="fail-closed",
                verdict_effect="fail-closed",
            ),
            "message": CI_VALIDATION_INVALID_PLAN_MISSING_MESSAGE,
        }
    )
    cast("list[dict[str, object]]", summary["diagnostics"]).sort(
        key=lambda item: str(item.get("diagnostic-id")),
    )
    _sort_summary_failures(summary)


def _mark_summary_bound_final_producer_unverified(
    summary: dict[str, object],
    *,
    bind_final_manifest: bool = True,
    include_derived_final_evidence: bool = True,
) -> None:
    cast("dict[str, object]", summary["reason"])[
        "final-producer-unverified"
    ] = True
    manifest_claim = cast(
        "dict[str, object]", summary["aggregate-evidence-manifest"]
    )
    final_manifest = cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )
    if bind_final_manifest:
        manifest_claim["artifact-instance-id"] = "3001"
        manifest_claim["content-digest"] = "0" * 64
        final_manifest["artifact-instance-id"] = "3001"
        final_manifest["content-digest"] = "0" * 64
    final_manifest["producer-verified"] = False
    final_producer_diagnostic = _diagnostic(
        "final-producer-unverified",
        code="final-producer-unverified",
        detail="final-producer-unverified",
        message=(
            "Aggregate evidence manifest producer boundary was not verified "
            "before summary generation."
        ),
        severity="fail-closed",
        verdict_effect="fail-closed",
    )
    cast("list[dict[str, object]]", summary["diagnostics"]).append(
        final_producer_diagnostic
    )
    cast("list[dict[str, object]]", summary["failures"]).append(
        {
            "kind": "final-producer-unverified",
            "batch-id": None,
            "work-group-id": None,
            "evidence-expectation-id": None,
            "bundle-id": None,
            "diagnostic": final_producer_diagnostic,
            "message": final_producer_diagnostic["message"],
        }
    )
    if include_derived_final_evidence:
        final_evidence_diagnostic = _diagnostic(
            "final-evidence-failure/final-producer-unverified",
            code="final-evidence-failure",
            detail="final-producer-unverified",
            message="Aggregate evidence manifest producer was unverified.",
            severity="fail-closed",
            verdict_effect="fail-closed",
        )
        cast("dict[str, object]", summary["reason"])[
            "final-evidence-failure"
        ] = True
        cast("list[dict[str, object]]", summary["diagnostics"]).append(
            final_evidence_diagnostic
        )
        cast("list[dict[str, object]]", summary["failures"]).append(
            {
                "kind": "final-evidence-failure",
                "batch-id": None,
                "work-group-id": None,
                "evidence-expectation-id": None,
                "bundle-id": None,
                "diagnostic": final_evidence_diagnostic,
                "message": final_evidence_diagnostic["message"],
            }
        )
    cast("list[dict[str, object]]", summary["diagnostics"]).sort(
        key=lambda item: str(item.get("diagnostic-id")),
    )
    _sort_summary_failures(summary)


def _set_invalid_plan_partial_projection(
    summary: dict[str, object],
    plan: Mapping[str, object],
    field: str,
) -> None:
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
    if field in {
        "plan-id",
        "plan-digest",
        "mode",
        "validation-tree",
        "request",
        "scheduled-full",
    }:
        summary[field] = deepcopy(plan[field])
    elif field == "affected-range":
        summary[field] = _summary_affected_range(plan)
    elif field == "request.artifact-ref":
        summary["request"] = {
            "artifact-ref": cast("Mapping[str, object]", plan["request"])[
                "artifact-ref"
            ],
            "request-digest": None,
        }
    elif field == "request-digest":
        summary["request"] = {
            "artifact-ref": None,
            "request-digest": cast("Mapping[str, object]", plan["request"])[
                "request-digest"
            ],
        }
    else:
        raise AssertionError(field)


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
    _append_summary_kind_failure(summary, "fail-closed", diagnostic, message)


def _append_summary_kind_failure(
    summary: dict[str, object],
    kind: str,
    diagnostic: dict[str, object],
    message: str,
) -> None:
    if kind in {
        "aggregate-summary-without-manifest",
        "final-evidence-failure",
        "final-producer-unverified",
        "required-input-artifact-failure",
    }:
        reason = summary.get("reason")
        if isinstance(reason, dict):
            reason["fail-closed"] = True
    cast("list[dict[str, object]]", summary["failures"]).append(
        {
            "kind": kind,
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


class _TrustedTestDependencyBundle(dict[str, object]):
    def __init__(self, bundle: Mapping[str, object]) -> None:
        super().__init__(bundle)
        batch = cast("Mapping[str, object]", bundle["batch"])
        artifact_ref = cast("str", bundle["artifact-ref"])
        self.artifact_instance_id = f"{batch['batch-id']}-artifact"
        self.admitted_candidate_id = ci_validation_batch_evidence_candidate_id(
            run_id=RUN_ID,
            run_attempt=RUN_ATTEMPT,
            batch_id=cast("str", batch["batch-id"]),
            artifact_ref=artifact_ref,
            artifact_instance_id=self.artifact_instance_id,
            physical_artifact_name=artifact_physical_name(artifact_ref),
        )


def _trusted_dependency_bundle(
    bundle: Mapping[str, object],
) -> Mapping[str, object]:
    return _TrustedTestDependencyBundle(bundle)


def _dependency_identity_fields(
    bundle: Mapping[str, object],
) -> dict[str, object]:
    trusted = _trusted_dependency_bundle(bundle)
    return {
        "upstream-artifact-instance-id": cast(
            "_TrustedTestDependencyBundle",
            trusted,
        ).artifact_instance_id,
        "upstream-admitted-candidate-id": cast(
            "_TrustedTestDependencyBundle",
            trusted,
        ).admitted_candidate_id,
    }


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
            "upstream-artifact-ref": base_bundle["artifact-ref"],
            "upstream-bundle-id": base_bundle["bundle-id"],
            **_dependency_identity_fields(base_bundle),
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
        dependency_evidence_bundles=[_trusted_dependency_bundle(base_bundle)],
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
            "upstream-artifact-ref": base_bundle["artifact-ref"],
            "upstream-bundle-id": base_bundle["bundle-id"],
            **_dependency_identity_fields(base_bundle),
            "outcome": "satisfied",
            "admitted-for-gating": True,
        }
    ]
    dependent_bundle = _bundle_for_batch(
        plan,
        manifest,
        dependent_batch_id,
        result,
        dependency_evidence_bundles=[_trusted_dependency_bundle(base_bundle)],
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
            _aggregate_slot_for_bundle(
                bundle,
                cast(
                    "_TrustedTestDependencyBundle",
                    _trusted_dependency_bundle(bundle),
                ).artifact_instance_id,
            )
            for bundle in bundles
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
            "final-evidence-failure": False,
        },
        budgets={
            "pre-final-validation-artifacts": 5 + len(bundles),
            "expected-final-validation-artifacts": 2,
            "expected-actual-validation-artifacts": 7 + len(bundles),
            "max-validation-artifacts": 20,
            "actual-execution-batches": len(bundles),
            "actual-total-jobs": int(bool(bundles)),
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
        admitted_batch_evidence_bundles=[
            _trusted_dependency_bundle(bundle) for bundle in bundles
        ],
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
        == "three.ci.validation.batch-evidence-bundle/v1alpha2"
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
        "aggregate-summary-without-manifest",
        "required-input-artifact-failure",
        "namespace-overflow",
        "aggregate-without-manifest",
        "final-manifest-missing",
        "final-aggregate-missing",
    ],
)
def test_shared_final_evidence_registry_rejects_non_authority_details(
    detail: str,
) -> None:
    """Final-evidence details are limited to current authority diagnostics."""
    with pytest.raises(ContractValidationError):
        validate_ci_validation_diagnostic_record(
            _diagnostic(
                "final-evidence",
                code="final-evidence-failure",
                detail=detail,
                severity="fail-closed",
                verdict_effect="fail-closed",
            )
        )


def test_shared_final_evidence_registry_accepts_final_producer_unverified() -> (
    None
):
    """Final-evidence failures may derive final producer boundary failures."""
    validate_ci_validation_diagnostic_record(
        _diagnostic(
            "final-evidence",
            code="final-evidence-failure",
            detail="final-producer-unverified",
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
        ("inadmissible-batch-evidence", "duplicate-bundle-candidates"),
        ("inadmissible-batch-evidence", "bundle-producer-unverified"),
        (
            "inadmissible-batch-evidence",
            "bundle-metadata-authority-invalid",
        ),
        ("inadmissible-batch-evidence", "execution-batch-manifest-malformed"),
        ("namespace-closure-failure", "unexpected-contract-artifact"),
        ("namespace-closure-failure", "namespace-enumeration-unavailable"),
        ("inadmissible-batch-evidence", "execution-batch-manifest-missing"),
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


def test_namespace_closure_rejects_final_upload_gate_detail() -> None:
    """Final upload byte mismatches stay out of summary JSON causes."""
    with pytest.raises(ContractValidationError):
        validate_ci_validation_diagnostic_record(
            _diagnostic(
                "final-upload-byte-gate",
                code="namespace-closure-failure",
                detail="final-namespace-closure-mismatch",
                severity="fail-closed",
                verdict_effect="fail-closed",
            )
        )


def test_inadmissible_batch_evidence_rejects_generic_self_detail() -> None:
    """The public inadmissible-batch family requires a specific cause detail."""
    assert (
        "inadmissible-batch-evidence"
        not in REGISTERED_CI_VALIDATION_DIAGNOSTIC_DETAILS
    )
    assert (
        "inadmissible-batch-evidence"
        not in DETAILS_BY_DIAGNOSTIC_CODE["inadmissible-batch-evidence"]
    )
    with pytest.raises(ContractValidationError):
        validate_ci_validation_diagnostic_record(
            _diagnostic(
                "generic-inadmissible-batch-detail",
                code="inadmissible-batch-evidence",
                detail="inadmissible-batch-evidence",
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


def test_materializer_counts_non_batch_control_plane_jobs() -> None:
    """Execution topology budget includes live non-batch control-plane jobs."""
    snapshot = _PLANS_MODULE.__dict__["_plan_snapshot"]()
    control_plane_count = 4

    materialization = materialize_ci_validation_execution_batches(
        plan=cast("dict[str, object]", snapshot.plan),
        request=_request_document(),
        changed_files_snapshot=cast(
            "dict[str, object]", snapshot.changed_files_snapshot
        ),
        fact_snapshot=cast("dict[str, object]", snapshot.fact_snapshot),
        created_at=CREATED_AT,
        execution_workflow="CI Validation",
        expected_run_id=RUN_ID,
        expected_run_attempt=RUN_ATTEMPT,
        non_batch_control_plane_job_count=control_plane_count,
    )

    manifest = cast("dict[str, object]", materialization.manifest)
    budget = cast("dict[str, object]", manifest["budget"])
    batches = cast("list[dict[str, object]]", manifest["batches"])
    active_runner_families = {
        batch["runner-family"]
        for batch in batches
        if isinstance(batch.get("runner-family"), str)
    }
    assert budget["non-batch-control-plane-job-count"] == control_plane_count
    assert budget["actual-total-jobs"] == (
        control_plane_count + len(active_runner_families)
    )
    assert (
        _int_mapping_value(budget, "max-execution-batches")
        <= EXPECTED_MAX_EXECUTION_BATCHES
    )


def test_broad_global_materializer_counts_physical_orchestrator_jobs() -> None:
    """Broad/global executable manifests count physical orchestrator jobs."""
    plan = _global_full_scope_plan()
    batches: list[dict[str, object]] = [
        {"runner-family": "windows"} for _ in range(4)
    ] + [{"runner-family": "ubuntu"} for _ in range(8)]

    budget = _materializer_budget(
        plan=plan,
        batches=batches,
        expected_input_non_bundle_validation_artifacts=5,
        max_execution_batches=13,
        non_batch_control_plane_job_count=0,
        aggregate_target_duration_seconds=60,
        aggregate_max_duration_seconds=120,
    )

    expected_physical_jobs = len({"ubuntu", "windows"})
    assert budget["min-total-jobs"] == expected_physical_jobs
    assert budget["actual-total-jobs"] == expected_physical_jobs
    assert budget["min-windows-jobs"] == 1
    assert budget["actual-windows-jobs"] == 1


def test_broad_global_materializer_allows_physical_windows_orchestrator() -> (
    None
):
    """Broad/global manifests do not require per-batch Windows jobs."""
    plan = _global_full_scope_plan()

    materialization = materialize_ci_validation_execution_batches(
        plan=plan,
        **_global_full_scope_context_kwargs(),
        created_at=CREATED_AT,
        execution_workflow="CI Validation",
    )
    budget = cast("Mapping[str, object]", materialization.manifest["budget"])

    assert (
        cast("int", budget["actual-windows-jobs"]) < OLD_PER_BATCH_WINDOWS_FLOOR
    )


def test_broad_global_materializer_fits_execution_batch_budget() -> None:
    """Full-scope materialization covers each work group within budget."""
    plan = _global_full_scope_plan()

    materialization = materialize_ci_validation_execution_batches(
        plan=plan,
        **_global_full_scope_context_kwargs(),
        created_at=CREATED_AT,
        execution_workflow="CI Validation",
    )
    manifest = cast("dict[str, object]", materialization.manifest)
    batches = cast("list[dict[str, object]]", manifest["batches"])
    selectors = [
        selector
        for batch in batches
        for selector in cast(
            "list[dict[str, object]]",
            batch["ordered-selectors"],
        )
    ]
    selected_work_group_ids = {
        cast("str", group["work-group-id"])
        for group in cast("list[dict[str, object]]", plan["work-groups"])
        if group["kind"] != "evidence-aggregation"
    }
    selector_work_group_ids = [
        cast("str", selector["work-group-id"]) for selector in selectors
    ]
    budget = cast("Mapping[str, object]", manifest["budget"])
    actual_execution_batches = cast("int", budget["actual-execution-batches"])
    max_execution_batches = cast("int", budget["max-execution-batches"])
    pre_final_validation_artifacts = cast(
        "int",
        budget["pre-final-validation-artifacts"],
    )
    expected_input_non_bundle_validation_artifacts = cast(
        "int",
        budget["expected-input-non-bundle-validation-artifacts"],
    )
    actual_validation_artifacts = cast(
        "int",
        budget["actual-validation-artifacts"],
    )
    expected_final_validation_artifacts = cast(
        "int",
        budget["expected-final-validation-artifacts"],
    )

    assert len(batches) <= EXPECTED_MAX_EXECUTION_BATCHES
    assert len(selector_work_group_ids) == len(set(selector_work_group_ids))
    assert set(selector_work_group_ids) == selected_work_group_ids
    assert actual_execution_batches == len(batches)
    assert actual_execution_batches <= max_execution_batches
    assert pre_final_validation_artifacts == (
        expected_input_non_bundle_validation_artifacts + len(batches)
    )
    assert actual_validation_artifacts == (
        pre_final_validation_artifacts + expected_final_validation_artifacts
    )


def test_broad_global_materializer_coalesces_repository_validation() -> None:
    """Repository-wide descriptor and tooling checks share one generic batch."""
    plan = _global_full_scope_plan()

    materialization = materialize_ci_validation_execution_batches(
        plan=plan,
        **_global_full_scope_context_kwargs(),
        created_at=CREATED_AT,
        execution_workflow="CI Validation",
    )
    groups = {
        cast("str", group["work-group-id"]): group
        for group in cast("Sequence[dict[str, object]]", plan["work-groups"])
    }
    repository_batches = [
        batch
        for batch in cast(
            "Sequence[dict[str, object]]",
            materialization.manifest["batches"],
        )
        if cast("Mapping[str, object]", batch["compatibility-profile"])[
            "execution-profile"
        ]
        == "exec-repository-validation-generic"
    ]

    assert len(repository_batches) == 1
    repository_batch = repository_batches[0]
    selector_kinds = {
        groups[cast("str", selector["work-group-id"])]["kind"]
        for selector in cast(
            "Sequence[dict[str, object]]",
            repository_batch["ordered-selectors"],
        )
    }
    assert selector_kinds == {
        "descriptor-validation",
        "workflow-release-tooling",
    }


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


def _release_materialization_with_alt_obligation(
    *,
    extra_ecosystem: str = "python",
    extra_runner_family: str = "ubuntu",
) -> dict[str, object]:
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
    receipt_work_group["ecosystem"] = extra_ecosystem
    receipt_work_group["runner-family"] = extra_runner_family
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
    return cast(
        "dict[str, object]",
        materialize_ci_validation_execution_batches(
            plan=cast("dict[str, object]", snapshot.plan),
            request=_request_document(),
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
            created_at=CREATED_AT,
            execution_workflow="CI Validation",
            expected_run_id=RUN_ID,
            expected_run_attempt=RUN_ATTEMPT,
        ).manifest,
    )


def test_materializer_coalesces_release_batches_by_execution_shape() -> None:
    """Artifact and receipt payload differences stay on selectors."""
    manifest = _release_materialization_with_alt_obligation()

    release_batches = [
        batch
        for batch in cast(
            "list[dict[str, object]]",
            manifest["batches"],
        )
        if cast("dict[str, object]", batch["compatibility-profile"])[
            "release-shaped-profile"
        ]
        is not None
    ]
    assert len(release_batches) == 1
    selectors = cast(
        "list[dict[str, object]]",
        release_batches[0]["ordered-selectors"],
    )
    assert {selector["work-group-id"] for selector in selectors} == {
        "wg-artifact",
        "wg-artifact-alt-receipt",
    }


@pytest.mark.parametrize(
    ("extra_ecosystem", "extra_runner_family", "expected_values"),
    [
        ("ruby", "ubuntu", {"python", "ruby"}),
        ("python", "windows", {"ubuntu", "windows"}),
        ("python", "macos", {"ubuntu", "macos"}),
    ],
)
def test_materializer_keeps_release_execution_dimensions_split(
    extra_ecosystem: str,
    extra_runner_family: str,
    expected_values: set[str],
) -> None:
    """Release-shaped batches still split by ecosystem and runner family."""
    base_obligation = _PLANS_MODULE.__dict__["_artifact_obligation"]()
    receipt_obligation = deepcopy(base_obligation)
    receipt_obligation["artifact-obligation-id"] = (
        "artifact-example-alt-receipt"
    )
    receipt_obligation["work-group-id"] = "wg-artifact-alt-receipt"
    base_group = _PLANS_MODULE.__dict__["_artifact_work_group"]()
    receipt_group = _PLANS_MODULE.__dict__["_artifact_work_group"]()
    receipt_group["work-group-id"] = "wg-artifact-alt-receipt"
    receipt_group["ecosystem"] = extra_ecosystem
    receipt_group["runner-family"] = extra_runner_family
    receipt_group["coverage-target"] = {
        "type": "artifact-obligation",
        "id": "artifact-example-alt-receipt",
    }
    groups = {
        "wg-artifact": base_group,
        "wg-artifact-alt-receipt": receipt_group,
    }

    specs = _materializer_batch_specs(
        {
            "artifact-obligations": [
                base_obligation,
                receipt_obligation,
            ]
        },
        groups,
        ["wg-artifact", "wg-artifact-alt-receipt"],
    )

    assert len(specs) == EXPECTED_RELEASE_EXECUTION_SPLIT_BATCHES
    if extra_ecosystem != "python":
        values = {
            cast("dict[str, object]", spec["compatibility-profile"])[
                "ecosystem"
            ]
            for spec in specs
        }
    else:
        values = {
            cast("dict[str, object]", spec["key-payload"])["runner-family"]
            for spec in specs
        }
    assert values == expected_values


def test_materializer_keeps_release_artifact_shapes_on_selectors() -> None:
    """Artifact shape differences do not split one release executor profile."""
    base_obligation = _PLANS_MODULE.__dict__["_artifact_obligation"]()
    zip_obligation = deepcopy(base_obligation)
    zip_obligation["artifact-obligation-id"] = "artifact-example-zip"
    zip_obligation["work-group-id"] = "wg-artifact-zip"
    cast("dict[str, object]", zip_obligation["artifact"])["concrete-kind"] = (
        "zip"
    )
    base_group = _PLANS_MODULE.__dict__["_artifact_work_group"]()
    zip_group = _PLANS_MODULE.__dict__["_artifact_work_group"]()
    zip_group["work-group-id"] = "wg-artifact-zip"
    zip_group["coverage-target"] = {
        "type": "artifact-obligation",
        "id": "artifact-example-zip",
    }
    groups = {
        "wg-artifact": base_group,
        "wg-artifact-zip": zip_group,
    }

    specs = _materializer_batch_specs(
        {"artifact-obligations": [base_obligation, zip_obligation]},
        groups,
        ["wg-artifact", "wg-artifact-zip"],
    )
    selectors = cast("list[str]", specs[0]["work-group-ids"])

    assert len(specs) == EXPECTED_COALESCED_RELEASE_EXECUTOR_BATCHES
    assert set(selectors) == {"wg-artifact", "wg-artifact-zip"}


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


@pytest.mark.parametrize("key", ["min-total-jobs", "min-windows-jobs"])
def test_execution_batch_manifest_rejects_empty_batches_with_lower_bounds(
    key: str,
) -> None:
    """Empty manifests cannot declare non-waived topology lower bounds."""
    plan = _plan()
    manifest = _manifest(plan)
    manifest["batches"] = []
    manifest["budget"] = _budget(0)
    manifest["plan-id"] = None
    manifest["plan-digest"] = None
    cast("dict[str, object]", manifest["budget"])[key] = 1

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_execution_batch_manifest(
            manifest,
            authorizing=False,
        )

    assert any(issue.path == f"$.budget.{key}" for issue in error.value.issues)


@pytest.mark.parametrize("authorizing_case", ["omitted", "true"])
@pytest.mark.parametrize("plan_identity", ["omitted", "none"])
def test_planless_zero_batch_manifest_requires_non_authorizing_mode(
    authorizing_case: str,
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
        _validate_manifest_with_authorizing_case(manifest, authorizing_case)

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
        pytest.param({"diagnostic-id": "legacy-only"}, id="legacy-only"),
        pytest.param(
            _diagnostic("bad-code", code="unregistered-code"),
            id="unregistered-code",
        ),
        pytest.param(
            _diagnostic("bad-detail", code="invalid-plan", detail="build"),
            id="invalid-detail",
        ),
        pytest.param(
            {
                **_diagnostic("missing-source"),
                "source": {"type": "aggregation"},
            },
            id="missing-source-id",
        ),
        pytest.param(
            {
                **_diagnostic("extra-source-key"),
                "source": {
                    "type": "aggregation",
                    "id": None,
                    "extra": "forged",
                },
            },
            id="extra-source-key",
        ),
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


@pytest.mark.parametrize(
    "field",
    ["source", "severity", "verdict-effect"],
    ids=["source", "severity", "verdict-effect"],
)
def test_batch_evidence_bundle_rejects_missing_required_diagnostic_fields(
    field: str,
) -> None:
    """Batch diagnostics independently require source, severity, and effect."""
    plan = _plan()
    manifest = _manifest(plan)
    diagnostic = _diagnostic(
        "inadmissible-batch-evidence",
        code="inadmissible-batch-evidence",
        detail="malformed-bundle",
    )
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
            dependency_evidence_bundles=[
                _trusted_dependency_bundle(base_bundle)
            ],
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
    dependency["outcome"] = "missing"
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
    """Cross-batch failed rows must be admitted for dependency gating."""
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
    """Failed dependencies are admissible for downstream gating."""
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
        dependency_evidence_bundles=[
            _trusted_dependency_bundle(failed_base_bundle)
        ],
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
        admitted_batch_evidence_bundles=[
            _trusted_dependency_bundle(base_bundle),
            _trusted_dependency_bundle(dependent_bundle),
        ],
        execution_batch_manifest=manifest,
        request=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )


def test_aggregate_summary_manifest_validation_uses_fact_snapshot_context() -> (
    None
):
    """Descriptor-backed plans keep descriptor context during summary freeze."""
    plan, manifest = _release_plan_and_manifest()
    context = _release_authorizing_context()
    issues: list[Any] = []

    assert _validate_supplied_summary_execution_manifest(
        manifest,
        plan,
        _envelope(plan, CiValidationKind.PLAN),
        request=context["request"],
        changed_files_snapshot=context["changed_files_snapshot"],
        fact_snapshot=context["fact_snapshot"],
        issues=issues,
    )
    assert issues == []


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


def test_admitted_bundle_topology_passes_only_transitive_dependencies(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Independent later bundles do not revalidate prior unrelated bundles."""
    bundles = [
        {"batch": {"batch-id": "base", "depends-on-batches": []}},
        {
            "batch": {
                "batch-id": "dependent",
                "depends-on-batches": ["base"],
            },
        },
        {"batch": {"batch-id": "independent", "depends-on-batches": []}},
    ]
    dependency_ids_by_batch: dict[str, list[str]] = {}

    def fake_validate(
        bundle: Mapping[str, object],
        **kwargs: object,
    ) -> None:
        batch = cast("Mapping[str, object]", bundle["batch"])
        dependency_bundles = cast(
            "Sequence[Mapping[str, object]]",
            kwargs["dependency_evidence_bundles"],
        )
        dependency_ids_by_batch[str(batch["batch-id"])] = [
            str(cast("Mapping[str, object]", dependency["batch"])["batch-id"])
            for dependency in dependency_bundles
        ]

    monkeypatch.setattr(
        ci_validation_batches,
        "validate_ci_validation_batch_evidence_bundle",
        fake_validate,
    )
    monkeypatch.setattr(
        ci_validation_batches,
        "_trusted_dependency_bundle_from_manifest",
        lambda bundle, _manifest_rows: bundle,
    )

    issues: list[Any] = []
    _validate_admitted_bundles_topologically(
        bundles,
        plan=None,
        request=None,
        execution_batch_manifest=None,
        changed_files_snapshot=None,
        fact_snapshot=None,
        envelope=None,
        issues=issues,
    )

    assert issues == []
    assert dependency_ids_by_batch == {
        "base": [],
        "dependent": ["base"],
        "independent": [],
    }


def test_admitted_bundle_topology_reports_dependency_cycles(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """No-progress topology validation reports cycles instead of crashing."""
    bundles = [
        {"batch": {"batch-id": "first", "depends-on-batches": ["second"]}},
        {"batch": {"batch-id": "second", "depends-on-batches": ["first"]}},
    ]

    def fake_validate(*_args: object, **_kwargs: object) -> None:
        raise AssertionError

    monkeypatch.setattr(
        ci_validation_batches,
        "validate_ci_validation_batch_evidence_bundle",
        fake_validate,
    )

    issues: list[Any] = []
    _validate_admitted_bundles_topologically(
        bundles,
        plan=None,
        request=None,
        execution_batch_manifest=None,
        changed_files_snapshot=None,
        fact_snapshot=None,
        envelope=None,
        issues=issues,
    )

    assert [issue.path for issue in issues] == [
        "admitted_batch_evidence_bundles[0].batch.depends-on-batches",
        "admitted_batch_evidence_bundles[1].batch.depends-on-batches",
    ]


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
            dependency_evidence_bundles=[
                _trusted_dependency_bundle(failed_base_bundle)
            ],
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
            dependency_evidence_bundles=[
                _trusted_dependency_bundle(failed_base_bundle),
                _trusted_dependency_bundle(base_bundle),
            ],
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
            "upstream-artifact-ref": base_bundle["artifact-ref"],
            "upstream-bundle-id": base_bundle["bundle-id"],
            **_dependency_identity_fields(base_bundle),
            "outcome": "satisfied",
            "admitted-for-gating": True,
        }
    ]
    dependent_bundle = _bundle_for_batch(
        plan,
        manifest,
        batch_by_group["wg-dependent-gate"],
        dependent_result,
        dependency_evidence_bundles=[_trusted_dependency_bundle(base_bundle)],
    )
    transitive_result = _selector_result(
        plan, manifest, batch_by_group["wg-transitive-gate"]
    )
    transitive_result["dependency-results"] = [
        {
            "work-group-id": "wg-dependent-gate",
            "source-batch-id": batch_by_group["wg-dependent-gate"],
            "upstream-artifact-ref": dependent_bundle["artifact-ref"],
            "upstream-bundle-id": dependent_bundle["bundle-id"],
            **_dependency_identity_fields(dependent_bundle),
            "outcome": "satisfied",
            "admitted-for-gating": True,
        }
    ]
    transitive_bundle = _bundle_for_batch(
        plan,
        manifest,
        batch_by_group["wg-transitive-gate"],
        transitive_result,
        dependency_evidence_bundles=[
            _trusted_dependency_bundle(base_bundle),
            _trusted_dependency_bundle(dependent_bundle),
        ],
    )

    with pytest.raises(ContractValidationError) as error:
        validate_ci_validation_batch_evidence_bundle(
            transitive_bundle,
            plan=plan,
            execution_batch_manifest=manifest,
            dependency_evidence_bundles=[
                _trusted_dependency_bundle(dependent_bundle)
            ],
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
        dependency_evidence_bundles=[
            _trusted_dependency_bundle(base_bundle),
            _trusted_dependency_bundle(dependent_bundle),
        ],
        **_authorizing_context_kwargs(),
    )


def test_batch_evidence_allows_transitive_unresolved_dependency_block() -> None:
    """C may consume B's dependency-blocked skip without A success evidence."""
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

    dependent_result = _selector_result(
        plan, manifest, batch_by_group["wg-dependent-gate"]
    )
    dependent_result["dependency-results"] = [
        {
            "work-group-id": "wg-python-gate",
            "source-batch-id": batch_by_group["wg-python-gate"],
            "outcome": "missing",
            "admitted-for-gating": False,
        }
    ]
    dependent_result["outcome"] = "skipped"
    dependent_result["skip-reason"] = "dependency-blocked"
    dependent_result["diagnostics"] = [
        _diagnostic(
            "dependency-blocked",
            code="validation-work-skipped",
            detail="dependency-blocked",
        )
    ]
    for capability_result in cast(
        "list[dict[str, object]]",
        cast("dict[str, object]", dependent_result["evidence"])[
            "capability-results"
        ],
    ):
        capability_result["outcome"] = "skipped"
    dependent_bundle = _bundle_for_batch(
        plan,
        manifest,
        batch_by_group["wg-dependent-gate"],
        dependent_result,
    )

    transitive_result = _selector_result(
        plan, manifest, batch_by_group["wg-transitive-gate"]
    )
    transitive_result["dependency-results"] = [
        {
            "work-group-id": "wg-dependent-gate",
            "source-batch-id": batch_by_group["wg-dependent-gate"],
            "upstream-artifact-ref": dependent_bundle["artifact-ref"],
            "upstream-bundle-id": dependent_bundle["bundle-id"],
            **_dependency_identity_fields(dependent_bundle),
            "outcome": "skipped",
            "admitted-for-gating": False,
        }
    ]
    transitive_result["outcome"] = "skipped"
    transitive_result["skip-reason"] = "dependency-blocked"
    transitive_result["diagnostics"] = [
        _diagnostic(
            "dependency-blocked",
            code="validation-work-skipped",
            detail="dependency-blocked",
        )
    ]
    for capability_result in cast(
        "list[dict[str, object]]",
        cast("dict[str, object]", transitive_result["evidence"])[
            "capability-results"
        ],
    ):
        capability_result["outcome"] = "skipped"

    _bundle_for_batch(
        plan,
        manifest,
        batch_by_group["wg-transitive-gate"],
        transitive_result,
        dependency_evidence_bundles=[
            _trusted_dependency_bundle(dependent_bundle)
        ],
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
            dependency_evidence_bundles=[
                _trusted_dependency_bundle(base_bundle)
            ],
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


def test_public_validator_admits_failed_dependency_for_gating() -> None:
    """Blocking-failure upstream evidence is admitted for dependency gating."""
    (
        plan,
        manifest,
        base_bundle,
        dependent_bundle,
        _aggregate_manifest,
        _summary,
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
    validate_ci_validation_batch_evidence_bundle(
        dependent_bundle,
        plan=plan,
        execution_batch_manifest=manifest,
        dependency_evidence_bundles=[
            _trusted_dependency_bundle(failed_base_bundle)
        ],
        **_authorizing_context_kwargs(),
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
    cast("dict[str, object]", request["event"])["actor"] = "mona"

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
        issue.path == "request.ci-validation-request"
        and issue.message == "request-digest-mismatch"
        for issue in exc_info.value.issues
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
        "validation-tree" in issue.path for issue in exc_info.value.issues
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

    if input_name == "request":
        assert not any(
            issue.path.startswith("request.") for issue in exc_info.value.issues
        )
    else:
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
    """Invalid planning inputs cannot retain plan/projection authority."""
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
        issue.path == "$.projection-authority"
        and "without an authoritative plan" in issue.message
        for issue in exc_info.value.issues
    )
    assert all(
        issue.path not in {"$.plan-id", "$.plan-digest"}
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
            "execution-batch-manifest",
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
        ("changed-files-snapshot", "changed-files"),
        ("fact-snapshot", "fact"),
    ],
)
def test_aggregate_manifest_rejects_invalid_plan_not_required_snapshot_state(
    input_name: str,
    plan_mutation: str,
) -> None:
    """Invalid supplied plan snapshot fails before no-authority fallback."""
    original_plan = _plan()
    if plan_mutation == "changed-files":
        plan = cast("dict[str, object]", deepcopy(original_plan))
        affected_range = cast("dict[str, object]", plan["affected-range"])
        affected_range["status"] = "not-applicable"
        affected_range["changed-files-hash"] = None
    else:
        plan = cast("dict[str, object]", deepcopy(original_plan))
        cast("dict[str, object]", plan["fact-snapshot"])["status"] = (
            "unavailable"
        )
    plan["plan-digest"] = ci_validation_plan_digest(plan)
    original_manifest = _manifest(original_plan)
    manifest = cast("dict[str, object]", deepcopy(original_manifest))
    manifest["plan-digest"] = plan["plan-digest"]
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

    expected_issue_path = (
        "$.affected-range.status"
        if input_name == "changed-files-snapshot"
        else "$.fact-snapshot.status"
    )
    assert any(
        issue.path == expected_issue_path for issue in exc_info.value.issues
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
    "container_path",
    [
        ("schema-diagnostics",),
        ("diagnostics",),
        (
            "final-artifacts",
            "aggregate-evidence-manifest",
            "authority-diagnostics",
        ),
        ("batch-bundles", 0, "diagnostics"),
        ("evidence-results", 0, "diagnostics"),
    ],
    ids=[
        "schema-diagnostics",
        "diagnostics",
        "authority-diagnostics",
        "batch-bundles",
        "evidence-results",
    ],
)
def test_aggregate_summary_rejects_forged_neutral_invalid_plan_diagnostic(
    container_path: tuple[str | int, ...],
) -> None:
    """Neutral invalid-plan diagnostics still require canonical binding."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    container: object = summary
    for key in container_path:
        if isinstance(key, int):
            container = cast("list[object]", container)[key]
        else:
            mapping = cast("dict[str, object]", container)
            if key == "authority-diagnostics" and key not in mapping:
                mapping[key] = []
            container = mapping[key]
    cast("list[dict[str, object]]", container).append(
        {
            "diagnostic-id": "invalid-plan/forged",
            "code": "invalid-plan",
            "detail": "malformed-plan",
            "message": CI_VALIDATION_INVALID_PLAN_NON_AUTHORITATIVE_MESSAGE,
            "source": {"type": "aggregation", "id": None},
            "severity": "warning",
            "verdict-effect": "none",
        }
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


@pytest.mark.parametrize(
    "container_path",
    [
        ("schema-diagnostics",),
        ("diagnostics",),
        ("batch-bundles", 0, "diagnostics"),
    ],
    ids=[
        "schema-diagnostics",
        "diagnostics",
        "batch-bundles",
    ],
)
def test_aggregate_summary_rejects_unbound_neutral_invalid_plan_diagnostic(
    container_path: tuple[str | int, ...],
) -> None:
    """Neutral invalid-plan diagnostics still require invalid-plan binding."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    container: object = summary
    for key in container_path:
        if isinstance(key, int):
            container = cast("list[object]", container)[key]
        else:
            mapping = cast("dict[str, object]", container)
            if key == "authority-diagnostics" and key not in mapping:
                mapping[key] = []
            container = mapping[key]
    cast("list[dict[str, object]]", container).append(
        {
            "diagnostic-id": "invalid-plan/malformed-plan",
            "code": "invalid-plan",
            "detail": "malformed-plan",
            "message": CI_VALIDATION_INVALID_PLAN_NON_AUTHORITATIVE_MESSAGE,
            "source": {"type": "aggregation", "id": "forged"},
            "severity": "warning",
            "verdict-effect": "none",
        }
    )

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
        "canonical bound invalid-plan diagnostic" in issue.message
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    "container_path",
    [
        ("schema-diagnostics",),
        ("diagnostics",),
        ("batch-bundles", 0, "diagnostics"),
    ],
    ids=[
        "schema-diagnostics",
        "diagnostics",
        "batch-bundles",
    ],
)
def test_aggregate_summary_accepts_bound_neutral_invalid_plan_diagnostic(
    container_path: tuple[str | int, ...],
) -> None:
    """Non-invalid summaries may carry bound neutral plan diagnostics."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    container: object = summary
    for key in container_path:
        if isinstance(key, int):
            container = cast("list[object]", container)[key]
        else:
            mapping = cast("dict[str, object]", container)
            if key == "authority-diagnostics" and key not in mapping:
                mapping[key] = []
            container = mapping[key]
    cast("list[dict[str, object]]", container).append(
        {
            "diagnostic-id": "invalid-plan/malformed-plan",
            "code": "invalid-plan",
            "detail": "malformed-plan",
            "message": "Human-readable invalid-plan text may vary.",
            "source": {"type": "aggregation", "id": None},
            "severity": "warning",
            "verdict-effect": "none",
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


def test_aggregate_summary_rejects_fail_closed_invalid_plan_diagnostic() -> (
    None
):
    """Non-invalid summaries cannot carry effectful invalid-plan diagnostics."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    cast("list[dict[str, object]]", summary["diagnostics"]).append(
        {
            "diagnostic-id": "fail-closed/invalid-plan/plan-missing",
            "code": "invalid-plan",
            "detail": "plan-missing",
            "message": "Validation planning failed closed.",
            "source": {"type": "aggregation", "id": None},
            "severity": "fail-closed",
            "verdict-effect": "fail-closed",
        }
    )

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
        issue.path == "$.diagnostics[0]"
        and "canonical bound invalid-plan diagnostic" in issue.message
        for issue in exc_info.value.issues
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
        pytest.param({"diagnostic-id": "legacy-only"}, id="legacy-only"),
        pytest.param(
            _diagnostic("unknown-code", code="unknown-code"),
            id="unknown-code",
        ),
        pytest.param(
            _diagnostic(
                "wrong-detail",
                code="namespace-closure-failure",
                detail="build",
            ),
            id="wrong-detail",
        ),
        pytest.param(
            {**_diagnostic("missing-severity"), "severity": None},
            id="missing-severity",
        ),
        pytest.param(
            {
                **_diagnostic("extra-source-key"),
                "source": {
                    "type": "aggregation",
                    "id": None,
                    "extra": "forged",
                },
            },
            id="extra-source-key",
        ),
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


@pytest.mark.parametrize(
    "field",
    ["source", "severity", "verdict-effect"],
    ids=["source", "severity", "verdict-effect"],
)
def test_aggregate_summary_rejects_missing_required_failure_diagnostic_fields(
    field: str,
) -> None:
    """Summary failure diagnostics require source, severity, and effect."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_namespace_failure(summary, include_overflow=True)
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
    _mark_summary_namespace_failure(summary, include_overflow=True)
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
    plan, manifest, bundle, aggregate_manifest, summary = (
        _multi_cause_fail_closed_fixture()
    )
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
    _mark_summary_required_input_failure(summary)
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


@pytest.mark.parametrize(
    "terminal_aggregation",
    [None, "legacy-aggregate"],
    ids=["missing-terminal-aggregation", "legacy-terminal-aggregation"],
)
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
        ci_validation_batches.freeze_ci_validation_aggregate_summary(
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
    _set_input_absent(
        aggregate_manifest,
        "execution-batch-manifest",
        required=True,
        admissibility="missing",
    )
    _make_summary_batch_evidence_missing(summary, aggregate_manifest)
    _mark_summary_required_input_failure(summary)
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
        final_artifacts=cast(
            "dict[str, object]",
            summary["final-artifacts"],
        ),
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


def test_aggregate_summary_rejects_forged_summary_without_manifest_source() -> (
    None
):
    """Summary-without-manifest failures must come from aggregate provenance."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_missing_aggregate_manifest(summary)
    failure = next(
        item
        for item in cast("list[dict[str, object]]", summary["failures"])
        if item["kind"] == "aggregate-summary-without-manifest"
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


def test_aggregate_summary_fails_closed_for_fail_closed_plan() -> None:
    """Planner fail-closed diagnostics force a failed aggregate summary."""
    plan, changed_files_snapshot, manifest = _fail_closed_plan_context()
    aggregate_manifest = _fail_closed_aggregate_manifest(
        plan,
        manifest,
        changed_files_snapshot,
    )
    summary = _fail_closed_aggregate_summary(
        plan,
        manifest,
        aggregate_manifest,
        changed_files_snapshot,
    )

    validate_ci_validation_aggregate_summary(
        summary,
        plan=plan,
        aggregate_evidence_manifest=aggregate_manifest,
        admitted_batch_evidence_bundles=[],
        execution_batch_manifest=manifest,
        request=_request_document(),
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=None,
    )


def test_aggregate_summary_rejects_passed_fail_closed_plan() -> None:
    """Fail-closed plans cannot be summarized as passing no-op plans."""
    plan, changed_files_snapshot, manifest = _fail_closed_plan_context()
    aggregate_manifest = _fail_closed_aggregate_manifest(
        plan,
        manifest,
        changed_files_snapshot,
    )
    summary = _fail_closed_aggregate_summary(
        plan,
        manifest,
        aggregate_manifest,
        changed_files_snapshot,
    )
    summary["verdict"] = "passed"
    cast("dict[str, object]", summary["reason"])["fail-closed"] = False
    summary["failures"] = []

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=None,
        )

    issue_paths = {issue.path for issue in exc_info.value.issues}
    assert "$.verdict" in issue_paths
    assert "$.reason.fail-closed" in issue_paths
    assert "$.failures" in issue_paths


def test_aggregate_summary_rejects_wrong_fail_closed_plan_cause() -> None:
    """Fail-closed failures must exactly cover planner diagnostic causes."""
    plan, changed_files_snapshot, manifest = _fail_closed_plan_context()
    aggregate_manifest = _fail_closed_aggregate_manifest(
        plan,
        manifest,
        changed_files_snapshot,
    )
    summary = _fail_closed_aggregate_summary(
        plan,
        manifest,
        aggregate_manifest,
        changed_files_snapshot,
    )
    failure = cast("list[dict[str, object]]", summary["failures"])[0]
    cast("dict[str, object]", failure["diagnostic"])["detail"] = "inconsistent"

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[],
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=None,
        )

    assert any(issue.path == "$.failures" for issue in exc_info.value.issues)


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

    assert {
        "$.mode",
        "$.validation-tree",
        "$.affected-range",
        "$.request",
    } <= {issue.path for issue in exc_info.value.issues}


def test_freeze_aggregate_summary_requires_fail_closed_unbound_plan() -> None:
    """Freezing forces zero-state projection without bound plan provenance."""
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
    with pytest.raises(ContractValidationError):
        ci_validation_batches.freeze_ci_validation_aggregate_summary(
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
            affected_range=cast(
                "dict[str, object]", fail_closed["affected-range"]
            ),
            request=cast("dict[str, object]", fail_closed["request"]),
            scheduled_full=cast(
                "dict[str, object]", fail_closed["scheduled-full"]
            ),
            verdict=cast("str", fail_closed["verdict"]),
            reason=cast("dict[str, object]", fail_closed["reason"]),
            budgets=cast("dict[str, object]", fail_closed["budgets"]),
            diagnostics=cast(
                "list[dict[str, object]]", fail_closed["diagnostics"]
            ),
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
        "execution-batch-manifest",
        required=True,
        admissibility="missing",
    )
    _make_summary_batch_evidence_missing(summary, aggregate_manifest)
    cast(
        "list[dict[str, object]]",
        aggregate_manifest["unexpected-contract-artifacts"],
    ).append(
        {
            "physical-artifact-name": (
                f"three-ci-validation-{RUN_ID}-{RUN_ATTEMPT}-" + "9" * 64
            ),
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
    _mark_summary_duration_overrun(summary)
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


def test_aggregate_summary_rejects_missing_namespace_cause() -> None:
    """Namespace attribution must exactly cover every derived cause."""
    plan, manifest, _bundle, aggregate_manifest, summary = (
        _multi_cause_fail_closed_fixture()
    )
    cast("list[dict[str, object]]", summary["failures"])[:] = [
        failure
        for failure in cast("list[dict[str, object]]", summary["failures"])
        if not (
            failure["kind"] == "namespace-closure-failure"
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
    assert any(
        "namespace closure" in issue.message for issue in error.value.issues
    )


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


def test_aggregate_summary_rejects_wrong_namespace_cause_in_set() -> None:
    """A wrong per-cause namespace row cannot stand in for a real cause."""
    plan, manifest, _bundle, aggregate_manifest, summary = (
        _multi_cause_fail_closed_fixture()
    )
    failure = next(
        failure
        for failure in cast("list[dict[str, object]]", summary["failures"])
        if failure["kind"] == "namespace-closure-failure"
    )
    diagnostic = cast("dict[str, object]", failure["diagnostic"])
    diagnostic["detail"] = "namespace-enumeration-unavailable"
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
    assert any(
        "namespace closure" in issue.message for issue in error.value.issues
    )


def test_aggregate_summary_rejects_duplicate_namespace_cause() -> None:
    """Namespace attribution cannot duplicate one exact cause."""
    plan, manifest, _bundle, aggregate_manifest, summary = (
        _multi_cause_fail_closed_fixture()
    )
    failure = next(
        failure
        for failure in cast("list[dict[str, object]]", summary["failures"])
        if failure["kind"] == "namespace-closure-failure"
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
    assert any(
        "namespace closure" in issue.message for issue in error.value.issues
    )


def test_aggregate_summary_rejects_forged_missing_manifest_source() -> None:
    """Missing-manifest diagnostics must use aggregate-level provenance."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_missing_aggregate_manifest(summary)
    failure = next(
        failure
        for failure in cast("list[dict[str, object]]", summary["failures"])
        if failure["kind"] == "aggregate-summary-without-manifest"
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
    """Missing-manifest diagnostics must match the derived failure cause."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_missing_aggregate_manifest(summary)
    failure = next(
        failure
        for failure in cast("list[dict[str, object]]", summary["failures"])
        if failure["kind"] == "aggregate-summary-without-manifest"
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
        validate_ci_validation_aggregate_summary(summary, plan=plan)


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
        ci_validation_batches.freeze_ci_validation_aggregate_summary(
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
        final_artifacts=cast(
            "dict[str, object]",
            summary["final-artifacts"],
        ),
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
        ci_validation_batches.freeze_ci_validation_aggregate_summary(
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


@pytest.mark.parametrize(
    ("key", "value"),
    [
        ("min-total-jobs", 11),
        ("min-windows-jobs", 3),
        ("actual-total-jobs", 11),
        ("actual-windows-jobs", 3),
    ],
)
def test_execution_batch_manifest_rejects_broad_global_physical_budget_mismatch(
    key: str,
    value: int,
) -> None:
    """Broad/global executable manifests validate physical topology counts."""
    plan = _global_full_scope_plan()
    batches: list[dict[str, object]] = [
        {"runner-family": "windows"} for _ in range(4)
    ] + [{"runner-family": "ubuntu"} for _ in range(8)]
    budget = _materializer_budget(
        plan=plan,
        batches=batches,
        expected_input_non_bundle_validation_artifacts=5,
        max_execution_batches=13,
        non_batch_control_plane_job_count=0,
        aggregate_target_duration_seconds=60,
        aggregate_max_duration_seconds=120,
    )
    budget[key] = value
    issues: list[Any] = []

    _validate_budget(
        budget,
        len(batches),
        batches,
        {},
        plan,
        issues,
    )

    assert any(str(issue.path) == f"$.budget.{key}" for issue in issues)


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


def test_manifest_allows_batches_independent_of_job_headroom() -> None:
    """Physical job headroom does not cap logical execution batches."""
    plan = _plan()
    budget = _budget(1)
    budget["non-batch-control-plane-job-count"] = 10
    budget["min-total-jobs"] = 11
    budget["actual-total-jobs"] = 11
    budget["max-execution-batches"] = 13
    issues: list[Any] = []

    _validate_budget(
        budget,
        1,
        [_batch(plan)],
        {},
        plan,
        issues,
    )
    assert not issues


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


def test_aggregate_summary_allows_observed_duration_overrun_telemetry() -> None:
    """Observed duration overflow stays non-blocking telemetry."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_duration_overrun(summary)

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
    assert summary["verdict"] == "passed"
    assert "aggregate-duration-exceeded" not in cast(
        "dict[str, object]", summary["reason"]
    )


@pytest.mark.parametrize(
    ("kind", "diagnostic_id"),
    [
        ("forged-failure-kind", "forged-failure-kind"),
        ("final-evidence-failure", "stale-final-evidence-failure"),
    ],
    ids=[
        "forged-failure-kind-forged-failure-kind",
        "final-evidence-failure-stale-final-evidence-failure",
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
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
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
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
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
    _mark_summary_duration_overrun(summary)

    failures = cast("list[dict[str, object]]", summary["failures"])
    assert not any(
        failure["kind"] == "aggregate-duration-exceeded" for failure in failures
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


def test_aggregate_summary_rejects_partial_specific_failure_causes() -> None:
    """Every derived specific summary cause needs its own failure row."""
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
    _mark_summary_duration_overrun(summary)
    failures = cast("list[dict[str, object]]", summary["failures"])
    failures[:] = [
        failure
        for failure in failures
        if failure["kind"] != "required-input-artifact-failure"
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


def test_aggregate_summary_rejects_duration_overrun_failure_row() -> None:
    """Duration overflow must not add correctness failure rows."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_duration_overrun(summary)
    failures = cast("list[dict[str, object]]", summary["failures"])
    failures.append(
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

    with pytest.raises(ContractValidationError) as error:
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
        issue.path == "$.failures[0].kind"
        and issue.message == "is not registered"
        for issue in error.value.issues
    )


def test_aggregate_summary_accepts_passed_duration_overflow() -> None:
    """Duration overflow is telemetry and does not change the verdict."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    cast("dict[str, object]", summary["budgets"])[
        "aggregate-duration-seconds"
    ] = 121

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

    validate_ci_validation_aggregate_summary(
        summary,
        request=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )


def test_invalid_plan_rejects_unbound_retained_root_diagnostic() -> None:
    """No-authority invalid-plan summaries cannot add retained details."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_invalid_plan(summary)
    cast("list[dict[str, object]]", summary["diagnostics"]).append(
        _diagnostic(
            "invalid-plan/schema-invalid",
            code="invalid-plan",
            detail="schema-invalid",
            message=CI_VALIDATION_INVALID_PLAN_NON_AUTHORITATIVE_MESSAGE,
            severity="warning",
            verdict_effect="none",
        )
    )

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            _require_aggregate_evidence_manifest=False,
        )

    assert any(
        issue.path == "$.diagnostics[0]"
        and "canonical bound invalid-plan diagnostic" in issue.message
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    "mutation",
    [
        "passed",
        "evidence-result",
        "execution-count",
        "work-group-count",
        "failure",
    ],
    ids=[
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
        validate_ci_validation_aggregate_summary(
            summary,
            aggregate_evidence_manifest=aggregate_manifest,
        )


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
        validate_ci_validation_aggregate_summary(
            summary,
            aggregate_evidence_manifest=aggregate_manifest,
        )


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("diagnostic", {"diagnostic-id": "forged-invalid-plan"}),
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
    invalid_plan_failure = next(
        failure
        for failure in cast("list[dict[str, object]]", summary["failures"])
        if failure["kind"] == "invalid-plan"
    )
    invalid_plan_failure[field] = value

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(
            summary,
            aggregate_evidence_manifest=aggregate_manifest,
        )


@pytest.mark.parametrize(
    ("failure_kind", "message"),
    [
        pytest.param(
            "invalid-plan",
            "Alternate invalid-plan text.",
            id="invalid-plan",
        ),
        pytest.param(
            "fail-closed",
            "Alternate fail-closed text.",
            id="fail-closed",
        ),
    ],
)
def test_invalid_plan_summary_accepts_message_only_variations(
    failure_kind: str,
    message: str,
) -> None:
    """Invalid-plan failure identity excludes human-readable message text."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_invalid_plan(summary)
    failure = next(
        failure
        for failure in cast("list[dict[str, object]]", summary["failures"])
        if failure["kind"] == "invalid-plan"
    )
    if failure_kind == "fail-closed":
        failure["kind"] = "fail-closed"
        diagnostic = cast("dict[str, object]", failure["diagnostic"])
        diagnostic.update(
            {
                "diagnostic-id": "fail-closed/invalid-plan/plan-missing",
                "severity": "fail-closed",
                "verdict-effect": "fail-closed",
            }
        )
        reason = cast("dict[str, object]", summary["reason"])
        reason["invalid-plan"] = False
        reason["fail-closed"] = True
    failure["message"] = message
    cast("dict[str, object]", failure["diagnostic"])["message"] = message
    _sort_summary_failures(summary)

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


def test_invalid_plan_rejects_verified_no_authority_final_manifest() -> None:
    """No-authority invalid-plan summaries cannot self-verify final manifest."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_invalid_plan(summary)
    cast("dict[str, object]", summary["aggregate-evidence-manifest"])[
        "content-digest"
    ] = None
    final_manifest = cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )
    final_manifest["producer-verified"] = True

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(summary)

    assert any(
        issue.path
        == "$.final-artifacts.aggregate-evidence-manifest.producer-verified"
        and "must be false without bound aggregate evidence manifest"
        in issue.message
        for issue in exc_info.value.issues
    )


def test_invalid_plan_bound_manifest_rejects_null_identity() -> None:
    """Bound no-authority invalid-plan manifests require final identity."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_invalid_plan(summary)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            aggregate_evidence_manifest=aggregate_manifest,
        )

    assert any(
        issue.path
        in {
            "$.aggregate-evidence-manifest.artifact-instance-id",
            "$.aggregate-evidence-manifest.content-digest",
            "$.final-artifacts.aggregate-evidence-manifest.artifact-instance-id",
            "$.final-artifacts.aggregate-evidence-manifest.content-digest",
        }
        for issue in exc_info.value.issues
    )


def test_invalid_plan_unbound_manifest_allows_unverified_producer() -> None:
    """Unbound no-authority invalid-plan manifests stay unverified."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_invalid_plan(summary)
    final_manifest = cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )
    final_manifest["producer-verified"] = False

    validate_ci_validation_aggregate_summary(summary)


def test_invalid_plan_unbound_manifest_rejects_forged_preserved_identity() -> (
    None
):
    """Self-matching summary manifest claims do not prove bound evidence."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_invalid_plan(summary)
    manifest_claim = cast(
        "dict[str, object]", summary["aggregate-evidence-manifest"]
    )
    final_manifest = cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )
    manifest_claim["artifact-instance-id"] = "forged-aggregate-manifest"
    manifest_claim["content-digest"] = "0123456789abcdef" * 4
    final_manifest["artifact-instance-id"] = "forged-aggregate-manifest"
    final_manifest["content-digest"] = "0123456789abcdef" * 4

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(summary)

    assert any(
        issue.path
        in {
            "$.aggregate-evidence-manifest.artifact-instance-id",
            "$.aggregate-evidence-manifest.content-digest",
            "$.final-artifacts.aggregate-evidence-manifest.artifact-instance-id",
            "$.final-artifacts.aggregate-evidence-manifest.content-digest",
        }
        for issue in exc_info.value.issues
    )


def test_invalid_plan_unbound_manifest_rejects_self_authority_diagnostics() -> (
    None
):
    """Summary-local authority diagnostics do not prove manifest authority."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_invalid_plan(summary)
    invalid_plan_failure = _summary_failure(summary)
    invalid_plan_diagnostic = cast(
        "dict[str, object]", invalid_plan_failure["diagnostic"]
    )
    invalid_plan_diagnostic["diagnostic-id"] = "invalid-plan/malformed-plan"
    invalid_plan_diagnostic["detail"] = "malformed-plan"
    invalid_plan_diagnostic["message"] = (
        CI_VALIDATION_INVALID_PLAN_NON_AUTHORITATIVE_MESSAGE
    )
    invalid_plan_failure["message"] = invalid_plan_diagnostic["message"]
    manifest_claim = cast(
        "dict[str, object]", summary["aggregate-evidence-manifest"]
    )
    final_manifest = cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )
    manifest_claim["artifact-instance-id"] = "forged-aggregate-manifest"
    manifest_claim["content-digest"] = "0123456789abcdef" * 4
    final_manifest["artifact-instance-id"] = "forged-aggregate-manifest"
    final_manifest["content-digest"] = "0123456789abcdef" * 4
    authority_diagnostic = _diagnostic(
        "final-evidence-failure/aggregate-evidence-manifest-malformed",
        code="final-evidence-failure",
        detail="aggregate-evidence-manifest-malformed",
        severity="fail-closed",
        verdict_effect="fail-closed",
    )
    final_manifest["authority-diagnostics"] = [authority_diagnostic]
    cast("dict[str, object]", summary["reason"])["final-evidence-failure"] = (
        True
    )
    cast("list[dict[str, object]]", summary["failures"]).append(
        {
            "kind": "final-evidence-failure",
            "batch-id": None,
            "work-group-id": None,
            "evidence-expectation-id": None,
            "bundle-id": None,
            "diagnostic": _diagnostic(
                "final-evidence-failure/aggregate-evidence-manifest-malformed",
                code="final-evidence-failure",
                detail="aggregate-evidence-manifest-malformed",
                severity="fail-closed",
                verdict_effect="fail-closed",
            ),
            "message": "Self-authorized aggregate evidence manifest failure.",
        }
    )
    _sort_summary_failures(summary)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            _require_aggregate_evidence_manifest=False,
        )

    assert any(
        issue.path
        in {
            "$.aggregate-evidence-manifest.artifact-instance-id",
            "$.aggregate-evidence-manifest.content-digest",
            "$.final-artifacts.aggregate-evidence-manifest.artifact-instance-id",
            "$.final-artifacts.aggregate-evidence-manifest.content-digest",
            "$.failures",
        }
        or "final evidence failure causes" in issue.message
        for issue in exc_info.value.issues
    )


def test_invalid_plan_rejects_self_authorized_final_producer_evidence() -> None:
    """Final producer evidence failures require bound manifest authority."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_invalid_plan(summary)
    final_evidence_diagnostic = _diagnostic(
        "final-evidence-failure/final-producer-unverified",
        code="final-evidence-failure",
        detail="final-producer-unverified",
        message="Aggregate evidence manifest producer was unverified.",
        severity="fail-closed",
        verdict_effect="fail-closed",
    )
    cast("dict[str, object]", summary["reason"])["final-evidence-failure"] = (
        True
    )
    cast("list[dict[str, object]]", summary["diagnostics"]).append(
        final_evidence_diagnostic
    )
    cast("list[dict[str, object]]", summary["failures"]).append(
        {
            "kind": "final-evidence-failure",
            "batch-id": None,
            "work-group-id": None,
            "evidence-expectation-id": None,
            "bundle-id": None,
            "diagnostic": final_evidence_diagnostic,
            "message": final_evidence_diagnostic["message"],
        }
    )
    cast("list[dict[str, object]]", summary["diagnostics"]).sort(
        key=lambda item: str(item.get("diagnostic-id")),
    )
    _sort_summary_failures(summary)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            _require_aggregate_evidence_manifest=False,
        )

    assert any(
        issue.path in {"$.reason.final-evidence-failure", "$.failures"}
        or "final evidence failure causes" in issue.message
        for issue in exc_info.value.issues
    )


def test_retained_invalid_plan_summary_requires_bound_manifest_identity() -> (
    None
):
    """Retained invalid-plan projection cannot self-claim manifest identity."""
    plan = _plan()
    aggregate_manifest = _authoritative_invalid_planning_input_manifest(
        plan,
        "changed-files-snapshot",
        "changed-files-snapshot-schema-invalid",
    )
    summary = _freeze_invalid_planning_input_summary(
        aggregate_manifest,
        plan=plan,
    )
    manifest_claim = cast(
        "dict[str, object]", summary["aggregate-evidence-manifest"]
    )
    final_manifest = cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )
    manifest_claim["artifact-instance-id"] = "forged-aggregate-manifest"
    manifest_claim["content-digest"] = "f" * 64
    final_manifest["artifact-instance-id"] = "forged-aggregate-manifest"
    final_manifest["content-digest"] = "f" * 64

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(summary)

    assert any(
        issue.path
        in {
            "$.aggregate-evidence-manifest.artifact-instance-id",
            "$.aggregate-evidence-manifest.content-digest",
            "$.projection-authority",
        }
        or "no-authority fail-closed projection" in issue.message
        for issue in exc_info.value.issues
    )


def test_retained_invalid_plan_external_binding_blocks_projection() -> None:
    """Externally bound manifest bytes do not authorize retained projection."""
    plan = _plan()
    aggregate_ref = ci_validation_aggregate_evidence_manifest_artifact_ref(
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
    )
    detail = "changed-files-snapshot-schema-invalid"
    invalid_plan_diagnostic = _diagnostic(
        f"invalid-plan/{detail}",
        code="invalid-plan",
        detail=detail,
        message=CI_VALIDATION_INVALID_PLAN_NON_AUTHORITATIVE_MESSAGE,
        severity="fail-closed",
        verdict_effect="fail-closed",
    )
    with pytest.raises(ContractValidationError) as exc_info:
        freeze_ci_validation_aggregate_summary(
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
                    "authority-diagnostics": [],
                },
                "aggregate-summary": {
                    "artifact-ref": ci_validation_aggregate_summary_artifact_ref(  # noqa: E501
                        run_id=RUN_ID,
                        run_attempt=RUN_ATTEMPT,
                    ),
                },
            },
            validation_tree=cast("dict[str, object]", plan["validation-tree"]),
            affected_range=_summary_affected_range(plan),
            request=cast("dict[str, object]", plan["request"]),
            scheduled_full=cast("dict[str, object]", plan["scheduled-full"]),
            verdict="failed",
            reason={"invalid-plan": True},
            budgets={
                "pre-final-validation-artifacts": 5,
                "expected-final-validation-artifacts": 2,
                "expected-actual-validation-artifacts": 7,
                "max-validation-artifacts": 20,
                "actual-execution-batches": 0,
                "actual-total-jobs": 0,
                "actual-windows-jobs": 0,
                "aggregate-duration-seconds": 10,
                "aggregate-target-duration-seconds": 60,
                "aggregate-max-duration-seconds": 120,
            },
            diagnostics=[invalid_plan_diagnostic],
            batch_bundles=[],
            evidence_results=[],
            failures=[
                {
                    "kind": "invalid-plan",
                    "batch-id": None,
                    "work-group-id": None,
                    "evidence-expectation-id": None,
                    "bundle-id": None,
                    "diagnostic": invalid_plan_diagnostic,
                    "message": invalid_plan_diagnostic["message"],
                }
            ],
            work_groups={
                "executable-required": 0,
                "required-succeeded": 0,
                "required-failed": 0,
                "required-skipped": 0,
                "required-missing": 0,
                "terminal-aggregation": "present",
            },
            plan=plan,
            aggregate_evidence_manifest_bound=True,
            aggregate_evidence_manifest_external_binding_verified=True,
        )

    assert any(
        (
            issue.path == "$.projection-authority"
            and "aggregate manifest input authority" in issue.message
        )
        or issue.message == "must match invalid-plan context"
        for issue in exc_info.value.issues
    )


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
    assert {
        "kind": "invalid-plan",
        "batch-id": None,
        "work-group-id": None,
        "evidence-expectation-id": None,
        "bundle-id": None,
        "diagnostic": _diagnostic(
            "invalid-plan",
            detail="plan-missing",
            message=CI_VALIDATION_INVALID_PLAN_MISSING_MESSAGE,
            severity="fail-closed",
            verdict_effect="fail-closed",
        ),
        "message": CI_VALIDATION_INVALID_PLAN_MISSING_MESSAGE,
    } in failures
    assert all(failure["kind"] == "invalid-plan" for failure in failures)
    reason = cast("dict[str, object]", summary["reason"])
    assert reason["invalid-plan"] is True
    assert reason["fail-closed"] is False
    assert reason["final-producer-unverified"] is False
    assert all(
        value is False for key, value in reason.items() if key != "invalid-plan"
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


def test_freezer_drops_invalid_plan_authority_fail_closed() -> None:
    """The freezer drops unbound summary-local final authority rows."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_invalid_plan(summary)
    final_manifest = cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )
    authority_diagnostic = _diagnostic(
        "authority/aggregate-evidence-manifest-malformed",
        code="final-evidence-failure",
        detail="aggregate-evidence-manifest-malformed",
        severity="fail-closed",
        verdict_effect="fail-closed",
    )
    final_manifest["authority-diagnostics"] = [authority_diagnostic]
    reason = cast("dict[str, object]", summary["reason"])
    reason["final-evidence-failure"] = True
    final_failure = {
        "kind": "final-evidence-failure",
        "batch-id": None,
        "work-group-id": None,
        "evidence-expectation-id": None,
        "bundle-id": None,
        "diagnostic": _diagnostic(
            "final-evidence-failure/aggregate-evidence-manifest-malformed",
            code="final-evidence-failure",
            detail="aggregate-evidence-manifest-malformed",
            severity="fail-closed",
            verdict_effect="fail-closed",
        ),
        "message": "Malformed aggregate evidence manifest authority.",
    }
    cast("list[dict[str, object]]", summary["failures"]).append(final_failure)

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
        verdict="failed",
        reason=reason,
        budgets=cast("dict[str, object]", summary["budgets"]),
        diagnostics=[],
        batch_bundles=[],
        evidence_results=[],
        failures=cast("list[dict[str, object]]", summary["failures"]),
        work_groups=cast("dict[str, object]", summary["work-groups"]),
    )

    frozen_reason = cast("dict[str, object]", frozen["reason"])
    frozen_failures = cast("list[dict[str, object]]", frozen["failures"])
    assert frozen_reason["fail-closed"] is False
    assert frozen_reason["final-evidence-failure"] is False
    assert not any(
        failure["kind"] == "final-evidence-failure"
        for failure in frozen_failures
    )
    assert not any(
        failure["kind"] == "fail-closed" for failure in frozen_failures
    )


def test_invalid_plan_summary_rejects_missing_authority_final_failure() -> None:
    """Authority diagnostics require matching invalid-plan final rows."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_invalid_plan(summary)
    final_manifest = cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )
    final_manifest["authority-diagnostics"] = [
        _diagnostic(
            "final-evidence-failure",
            detail="aggregate-evidence-manifest-missing",
            severity="fail-closed",
            verdict_effect="fail-closed",
        )
    ]
    reason = cast("dict[str, object]", summary["reason"])
    reason["final-evidence-failure"] = True

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
        issue.path == "$.final-artifacts.aggregate-evidence-manifest."
        "authority-diagnostics"
        for issue in exc_info.value.issues
    )


def test_invalid_plan_rejects_unbound_authority_final_without_fail_closed() -> (
    None
):
    """Authority final rows require independently bound manifest evidence."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_invalid_plan(summary)
    final_manifest = cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )
    final_manifest["authority-diagnostics"] = [
        _diagnostic(
            "final-evidence-failure/aggregate-evidence-manifest-missing",
            code="final-evidence-failure",
            detail="aggregate-evidence-manifest-missing",
            message="Preserved aggregate evidence manifest was missing.",
            severity="fail-closed",
            verdict_effect="fail-closed",
        )
    ]
    reason = cast("dict[str, object]", summary["reason"])
    reason["final-evidence-failure"] = True
    failure_diagnostic = _diagnostic(
        "final-evidence-failure/aggregate-evidence-manifest-missing",
        code="final-evidence-failure",
        detail="aggregate-evidence-manifest-missing",
        severity="fail-closed",
        verdict_effect="fail-closed",
    )
    cast("list[dict[str, object]]", summary["diagnostics"]).append(
        failure_diagnostic
    )
    cast("list[dict[str, object]]", summary["failures"]).append(
        {
            "kind": "final-evidence-failure",
            "batch-id": None,
            "work-group-id": None,
            "evidence-expectation-id": None,
            "bundle-id": None,
            "diagnostic": failure_diagnostic,
            "message": "Missing aggregate evidence manifest authority.",
        }
    )
    cast("list[dict[str, object]]", summary["diagnostics"]).sort(
        key=lambda item: str(item.get("diagnostic-id")),
    )
    _sort_summary_failures(summary)
    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(summary)

    assert any(
        issue.path == "$.final-artifacts.aggregate-evidence-manifest."
        "authority-diagnostics"
        for issue in exc_info.value.issues
    )


def test_invalid_plan_rejects_bool_only_authority_final_failure() -> None:
    """Caller bools alone do not authorize final manifest authority rows."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_invalid_plan(summary)
    final_manifest = cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )
    final_manifest["authority-diagnostics"] = [
        _diagnostic(
            "authority/aggregate-evidence-manifest-missing",
            code="final-evidence-failure",
            detail="aggregate-evidence-manifest-missing",
            severity="fail-closed",
            verdict_effect="fail-closed",
        )
    ]
    reason = cast("dict[str, object]", summary["reason"])
    reason["final-evidence-failure"] = True
    cast("list[dict[str, object]]", summary["failures"]).append(
        {
            "kind": "final-evidence-failure",
            "batch-id": None,
            "work-group-id": None,
            "evidence-expectation-id": None,
            "bundle-id": None,
            "diagnostic": _diagnostic(
                "final-evidence-failure/aggregate-evidence-manifest-missing",
                code="final-evidence-failure",
                detail="aggregate-evidence-manifest-missing",
                severity="fail-closed",
                verdict_effect="fail-closed",
            ),
            "message": "Missing aggregate evidence manifest authority.",
        }
    )
    _sort_summary_failures(summary)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            _aggregate_evidence_manifest_bound=True,
        )

    assert any(
        issue.path
        == "$.final-artifacts.aggregate-evidence-manifest.authority-diagnostics"
        for issue in exc_info.value.issues
    )


def test_invalid_plan_rejects_bool_details_authority_final_failure() -> None:
    """Caller bool plus details do not authorize final authority rows."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_invalid_plan(summary)
    manifest_claim = cast(
        "dict[str, object]", summary["aggregate-evidence-manifest"]
    )
    final_manifest = cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )
    manifest_claim["artifact-instance-id"] = "forged-aggregate-manifest"
    manifest_claim["content-digest"] = "0123456789abcdef" * 4
    final_manifest["artifact-instance-id"] = manifest_claim[
        "artifact-instance-id"
    ]
    final_manifest["content-digest"] = manifest_claim["content-digest"]
    authority_diagnostic = _diagnostic(
        "authority/aggregate-evidence-manifest-malformed",
        code="final-evidence-failure",
        detail="aggregate-evidence-manifest-malformed",
        severity="fail-closed",
        verdict_effect="fail-closed",
    )
    final_manifest["authority-diagnostics"] = [authority_diagnostic]
    reason = cast("dict[str, object]", summary["reason"])
    reason["final-evidence-failure"] = True
    cast("list[dict[str, object]]", summary["failures"]).append(
        {
            "kind": "final-evidence-failure",
            "batch-id": None,
            "work-group-id": None,
            "evidence-expectation-id": None,
            "bundle-id": None,
            "diagnostic": authority_diagnostic,
            "message": "Forged aggregate evidence manifest authority.",
        }
    )
    _sort_summary_failures(summary)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            _aggregate_evidence_manifest_bound=True,
            _aggregate_manifest_authority_failure_details={
                "aggregate-evidence-manifest-malformed"
            },
        )

    assert any(
        issue.path
        == "$.final-artifacts.aggregate-evidence-manifest.authority-diagnostics"
        for issue in exc_info.value.issues
    )


def test_invalid_plan_rejects_forged_authority_with_valid_manifest() -> None:
    """Valid manifest input does not authorize caller-forged details."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_invalid_plan(summary)
    authority_diagnostic = _diagnostic(
        "authority/aggregate-evidence-manifest-malformed",
        code="final-evidence-failure",
        detail="aggregate-evidence-manifest-malformed",
        severity="fail-closed",
        verdict_effect="fail-closed",
    )
    final_manifest = cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )
    final_manifest["authority-diagnostics"] = [authority_diagnostic]
    reason = cast("dict[str, object]", summary["reason"])
    reason["final-evidence-failure"] = True
    cast("list[dict[str, object]]", summary["failures"]).append(
        {
            "kind": "final-evidence-failure",
            "batch-id": None,
            "work-group-id": None,
            "evidence-expectation-id": None,
            "bundle-id": None,
            "diagnostic": authority_diagnostic,
            "message": "Forged aggregate evidence manifest authority.",
        }
    )
    _sort_summary_failures(summary)

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
            _aggregate_manifest_authority_failure_details={
                "aggregate-evidence-manifest-malformed"
            },
        )

    assert any(
        issue.path
        == "$.final-artifacts.aggregate-evidence-manifest.authority-diagnostics"
        for issue in exc_info.value.issues
    )


def test_invalid_plan_rejects_summary_local_authority_by_default() -> None:
    """Default validation does not let summaries bind final authority."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_invalid_plan(summary)
    summary["plan-id"] = plan["plan-id"]
    summary["plan-digest"] = plan["plan-digest"]
    summary["mode"] = plan["mode"]
    summary["validation-tree"] = plan["validation-tree"]
    summary["affected-range"] = _summary_affected_range(plan)
    summary["request"] = plan["request"]
    summary["scheduled-full"] = plan["scheduled-full"]
    failure = _summary_failure(summary)
    diagnostic = cast("dict[str, object]", failure["diagnostic"])
    diagnostic["diagnostic-id"] = (
        "invalid-plan/changed-files-snapshot-schema-invalid"
    )
    diagnostic["detail"] = "changed-files-snapshot-schema-invalid"
    diagnostic["message"] = CI_VALIDATION_INVALID_PLAN_NON_AUTHORITATIVE_MESSAGE
    failure["message"] = diagnostic["message"]
    summary["diagnostics"] = [diagnostic]
    manifest_claim = cast(
        "dict[str, object]", summary["aggregate-evidence-manifest"]
    )
    final_manifest = cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )
    manifest_claim["artifact-instance-id"] = "forged-aggregate-manifest"
    manifest_claim["content-digest"] = "0123456789abcdef" * 4
    final_manifest["artifact-instance-id"] = manifest_claim[
        "artifact-instance-id"
    ]
    final_manifest["content-digest"] = manifest_claim["content-digest"]
    final_manifest["producer-verified"] = False
    authority_diagnostic = _diagnostic(
        "final-evidence-failure/aggregate-evidence-manifest-malformed",
        code="final-evidence-failure",
        detail="aggregate-evidence-manifest-malformed",
        severity="fail-closed",
        verdict_effect="fail-closed",
    )
    final_manifest["authority-diagnostics"] = [authority_diagnostic]
    reason = cast("dict[str, object]", summary["reason"])
    reason["final-evidence-failure"] = True
    reason["final-producer-unverified"] = True
    cast("list[dict[str, object]]", summary["failures"]).extend(
        [
            {
                "kind": "final-evidence-failure",
                "batch-id": None,
                "work-group-id": None,
                "evidence-expectation-id": None,
                "bundle-id": None,
                "diagnostic": authority_diagnostic,
                "message": "Forged final manifest authority.",
            },
            {
                "kind": "final-producer-unverified",
                "batch-id": None,
                "work-group-id": None,
                "evidence-expectation-id": None,
                "bundle-id": None,
                "diagnostic": _diagnostic(
                    "final-producer-unverified",
                    code="final-producer-unverified",
                    detail="final-producer-unverified",
                    severity="fail-closed",
                    verdict_effect="fail-closed",
                ),
                "message": (
                    "Aggregate evidence manifest producer was unverified."
                ),
            },
        ]
    )
    _sort_summary_failures(summary)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(summary, plan=plan)

    assert any(
        issue.path
        == "$.final-artifacts.aggregate-evidence-manifest.authority-diagnostics"
        or "retained invalid-plan projection requires" in issue.message
        for issue in exc_info.value.issues
    )


def test_retained_invalid_plan_rejects_unbound_manifest_authority() -> None:
    """Retained projection requires independently bound manifest authority."""
    plan = _plan()
    aggregate_manifest = _authoritative_invalid_planning_input_manifest(
        plan,
        "changed-files-snapshot",
        "changed-files-snapshot-schema-invalid",
    )
    summary = _freeze_invalid_planning_input_summary(
        aggregate_manifest,
        plan=plan,
    )

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
            _aggregate_evidence_manifest_bound=False,
        )

    assert any(
        issue.path == "$.projection-authority"
        or issue.path in {"$.plan-id", "$.plan-digest"}
        for issue in exc_info.value.issues
    )


def test_invalid_plan_freezer_ignores_bool_only_authority_final_failure() -> (
    None
):
    """Freezing does not preserve final authority from a caller bool alone."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_invalid_plan(summary)
    final_manifest = cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )
    final_manifest["artifact-instance-id"] = "forged-aggregate-manifest"
    final_manifest["content-digest"] = "0123456789abcdef" * 4
    final_manifest["producer-verified"] = False
    final_manifest["authority-diagnostics"] = [
        _diagnostic(
            "authority/aggregate-evidence-manifest-malformed",
            code="final-evidence-failure",
            detail="aggregate-evidence-manifest-malformed",
            severity="fail-closed",
            verdict_effect="fail-closed",
        )
    ]

    frozen = freeze_ci_validation_aggregate_summary(
        created_at=CREATED_AT,
        repository_owner="hcoona",
        repository_name="three",
        workflow="CI Validation",
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        aggregate_evidence_manifest=cast(
            "dict[str, object]",
            summary["aggregate-evidence-manifest"],
        ),
        final_artifacts=cast(
            "dict[str, object]",
            summary["final-artifacts"],
        ),
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
            "list[dict[str, object]]",
            summary["evidence-results"],
        ),
        failures=cast("list[dict[str, object]]", summary["failures"]),
        work_groups=cast("dict[str, object]", summary["work-groups"]),
        aggregate_evidence_manifest_bound=True,
    )

    frozen_final_manifest = cast(
        "dict[str, object]",
        cast("dict[str, object]", frozen["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )
    assert frozen_final_manifest["producer-verified"] is False
    assert frozen_final_manifest["authority-diagnostics"] == []
    assert (
        cast("dict[str, object]", frozen["reason"])["final-evidence-failure"]
        is False
    )


def test_freezer_ignores_matching_summary_local_authority_failure() -> None:
    """Matching summary claims do not prove final manifest authority."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_invalid_plan(summary)
    raw_digest = "0123456789abcdef" * 4
    manifest_claim = cast(
        "dict[str, object]", summary["aggregate-evidence-manifest"]
    )
    final_manifest = cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )
    manifest_claim["artifact-instance-id"] = "bound-aggregate-manifest"
    manifest_claim["content-digest"] = raw_digest
    final_manifest["artifact-instance-id"] = "bound-aggregate-manifest"
    final_manifest["content-digest"] = raw_digest
    final_manifest["producer-verified"] = False
    authority_diagnostic = _diagnostic(
        "final-evidence-failure/aggregate-evidence-manifest-malformed",
        code="final-evidence-failure",
        detail="aggregate-evidence-manifest-malformed",
        severity="fail-closed",
        verdict_effect="fail-closed",
    )
    final_manifest["authority-diagnostics"] = [authority_diagnostic]
    reason = cast("dict[str, object]", summary["reason"])
    reason["final-evidence-failure"] = True
    reason["final-producer-unverified"] = True
    cast("list[dict[str, object]]", summary["failures"]).extend(
        [
            {
                "kind": "final-evidence-failure",
                "batch-id": None,
                "work-group-id": None,
                "evidence-expectation-id": None,
                "bundle-id": None,
                "diagnostic": authority_diagnostic,
                "message": "Bound aggregate evidence manifest is malformed.",
            },
            {
                "kind": "final-producer-unverified",
                "batch-id": None,
                "work-group-id": None,
                "evidence-expectation-id": None,
                "bundle-id": None,
                "diagnostic": _diagnostic(
                    "final-producer-unverified",
                    code="final-producer-unverified",
                    detail="final-producer-unverified",
                    severity="fail-closed",
                    verdict_effect="fail-closed",
                ),
                "message": (
                    "Aggregate evidence manifest producer was unverified."
                ),
            },
        ]
    )
    _sort_summary_failures(summary)

    frozen = freeze_ci_validation_aggregate_summary(
        created_at=CREATED_AT,
        repository_owner="hcoona",
        repository_name="three",
        workflow="CI Validation",
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        aggregate_evidence_manifest=manifest_claim,
        final_artifacts=cast("dict[str, object]", summary["final-artifacts"]),
        validation_tree=cast("dict[str, object]", summary["validation-tree"]),
        affected_range=cast("dict[str, object]", summary["affected-range"]),
        request=cast("dict[str, object]", summary["request"]),
        scheduled_full=cast("dict[str, object]", summary["scheduled-full"]),
        verdict=cast("str", summary["verdict"]),
        reason=reason,
        budgets=cast("dict[str, object]", summary["budgets"]),
        diagnostics=cast("list[dict[str, object]]", summary["diagnostics"]),
        batch_bundles=cast("list[dict[str, object]]", summary["batch-bundles"]),
        evidence_results=cast(
            "list[dict[str, object]]",
            summary["evidence-results"],
        ),
        failures=cast("list[dict[str, object]]", summary["failures"]),
        work_groups=cast("dict[str, object]", summary["work-groups"]),
        aggregate_evidence_manifest_bound=True,
    )

    frozen_final_manifest = cast(
        "dict[str, object]",
        cast("dict[str, object]", frozen["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )
    frozen_manifest_claim = cast(
        "dict[str, object]", frozen["aggregate-evidence-manifest"]
    )
    assert frozen_manifest_claim["artifact-instance-id"] is None
    assert frozen_manifest_claim["content-digest"] is None
    assert frozen_final_manifest["artifact-instance-id"] is None
    assert frozen_final_manifest["content-digest"] is None
    assert frozen_final_manifest["authority-diagnostics"] == []
    assert frozen_final_manifest["producer-verified"] is False
    assert (
        cast("dict[str, object]", frozen["reason"])["final-evidence-failure"]
        is False
    )


def test_invalid_plan_freezer_rejects_external_authority_without_context() -> (
    None
):
    """External failure details still require manifest authority context."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_invalid_plan(summary)
    raw_digest = "0123456789abcdef" * 4
    manifest_claim = cast(
        "dict[str, object]", summary["aggregate-evidence-manifest"]
    )
    final_manifest = cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )
    manifest_claim["artifact-instance-id"] = "bound-aggregate-manifest"
    manifest_claim["content-digest"] = raw_digest
    final_manifest["artifact-instance-id"] = "bound-aggregate-manifest"
    final_manifest["content-digest"] = raw_digest
    final_manifest["producer-verified"] = False
    authority_diagnostic = _diagnostic(
        "final-evidence-failure/aggregate-evidence-manifest-malformed",
        code="final-evidence-failure",
        detail="aggregate-evidence-manifest-malformed",
        severity="fail-closed",
        verdict_effect="fail-closed",
    )
    final_manifest["authority-diagnostics"] = [authority_diagnostic]
    reason = cast("dict[str, object]", summary["reason"])
    reason["final-evidence-failure"] = True
    reason["final-producer-unverified"] = True
    cast("list[dict[str, object]]", summary["failures"]).extend(
        [
            {
                "kind": "final-evidence-failure",
                "batch-id": None,
                "work-group-id": None,
                "evidence-expectation-id": None,
                "bundle-id": None,
                "diagnostic": authority_diagnostic,
                "message": "Bound aggregate evidence manifest is malformed.",
            },
            {
                "kind": "final-producer-unverified",
                "batch-id": None,
                "work-group-id": None,
                "evidence-expectation-id": None,
                "bundle-id": None,
                "diagnostic": _diagnostic(
                    "final-producer-unverified",
                    code="final-producer-unverified",
                    detail="final-producer-unverified",
                    severity="fail-closed",
                    verdict_effect="fail-closed",
                ),
                "message": (
                    "Aggregate evidence manifest producer was unverified."
                ),
            },
        ]
    )
    _sort_summary_failures(summary)

    with pytest.raises(ContractValidationError) as exc_info:
        freeze_ci_validation_aggregate_summary(
            created_at=CREATED_AT,
            repository_owner="hcoona",
            repository_name="three",
            workflow="CI Validation",
            run_id=RUN_ID,
            run_attempt=RUN_ATTEMPT,
            aggregate_evidence_manifest=manifest_claim,
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
            reason=reason,
            budgets=cast("dict[str, object]", summary["budgets"]),
            diagnostics=cast("list[dict[str, object]]", summary["diagnostics"]),
            batch_bundles=cast(
                "list[dict[str, object]]", summary["batch-bundles"]
            ),
            evidence_results=cast(
                "list[dict[str, object]]",
                summary["evidence-results"],
            ),
            failures=cast("list[dict[str, object]]", summary["failures"]),
            work_groups=cast("dict[str, object]", summary["work-groups"]),
            aggregate_evidence_manifest_document=aggregate_manifest,
            aggregate_evidence_manifest_bound=True,
            aggregate_evidence_manifest_external_binding_verified=True,
            aggregate_manifest_authority_failure_details=[
                "aggregate-evidence-manifest-malformed"
            ],
        )

    assert any(
        "requires" in issue.message or "must match" in issue.message
        for issue in exc_info.value.issues
    )


def test_invalid_plan_freezer_requires_document_for_external_binding() -> None:
    """External binding alone does not authorize the invalid-plan summary."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_invalid_plan(summary)
    raw_digest = "0123456789abcdef" * 4
    manifest_claim = cast(
        "dict[str, object]", summary["aggregate-evidence-manifest"]
    )
    final_manifest = cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )
    manifest_claim["artifact-instance-id"] = "bound-aggregate-manifest"
    manifest_claim["content-digest"] = raw_digest
    final_manifest["artifact-instance-id"] = "bound-aggregate-manifest"
    final_manifest["content-digest"] = raw_digest
    final_manifest["producer-verified"] = False
    final_manifest["authority-diagnostics"] = []

    frozen = ci_validation_batches.freeze_ci_validation_aggregate_summary(
        created_at=CREATED_AT,
        repository_owner="hcoona",
        repository_name="three",
        workflow="CI Validation",
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        aggregate_evidence_manifest=manifest_claim,
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
            "list[dict[str, object]]",
            summary["evidence-results"],
        ),
        failures=cast("list[dict[str, object]]", summary["failures"]),
        work_groups=cast("dict[str, object]", summary["work-groups"]),
        aggregate_evidence_manifest_bound=True,
        aggregate_evidence_manifest_external_binding_verified=True,
    )

    frozen_final_manifest = cast(
        "dict[str, object]",
        cast("dict[str, object]", frozen["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )
    assert frozen_final_manifest["artifact-instance-id"] is None
    assert frozen_final_manifest["content-digest"] is None
    assert frozen_final_manifest["producer-verified"] is False
    assert not any(
        failure.get("kind") == "final-producer-unverified"
        for failure in cast("list[dict[str, object]]", frozen["failures"])
    )


@pytest.mark.parametrize(
    "target",
    ["validator", "freezer"],
    ids=["validator", "freezer"],
)
def test_invalid_plan_bound_external_authority_rejects_null_manifest_identity(
    target: str,
) -> None:
    """Verified external authority requires preserved manifest identity."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_invalid_plan(summary)
    manifest_claim = cast(
        "dict[str, object]", summary["aggregate-evidence-manifest"]
    )
    final_manifest = cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )
    manifest_claim["artifact-instance-id"] = None
    manifest_claim["content-digest"] = None
    final_manifest["artifact-instance-id"] = None
    final_manifest["content-digest"] = None
    final_manifest["producer-verified"] = False
    authority_diagnostic = _diagnostic(
        "final-evidence-failure/aggregate-evidence-manifest-malformed",
        code="final-evidence-failure",
        detail="aggregate-evidence-manifest-malformed",
        severity="fail-closed",
        verdict_effect="fail-closed",
    )
    final_manifest["authority-diagnostics"] = [authority_diagnostic]
    reason = cast("dict[str, object]", summary["reason"])
    reason["final-evidence-failure"] = True
    reason["final-producer-unverified"] = True
    cast("list[dict[str, object]]", summary["failures"]).extend(
        [
            {
                "kind": "final-evidence-failure",
                "batch-id": None,
                "work-group-id": None,
                "evidence-expectation-id": None,
                "bundle-id": None,
                "diagnostic": authority_diagnostic,
                "message": "Bound aggregate evidence manifest is malformed.",
            },
            {
                "kind": "final-producer-unverified",
                "batch-id": None,
                "work-group-id": None,
                "evidence-expectation-id": None,
                "bundle-id": None,
                "diagnostic": _diagnostic(
                    "final-producer-unverified",
                    code="final-producer-unverified",
                    detail="final-producer-unverified",
                    severity="fail-closed",
                    verdict_effect="fail-closed",
                ),
                "message": (
                    "Aggregate evidence manifest producer was unverified."
                ),
            },
        ]
    )
    _sort_summary_failures(summary)

    if target == "validator":
        with pytest.raises(ContractValidationError) as exc_info:
            validate_ci_validation_aggregate_summary(
                summary,
                aggregate_evidence_manifest=aggregate_manifest,
                _aggregate_evidence_manifest_bound=True,
                _aggregate_evidence_manifest_external_binding_verified=True,
                _aggregate_manifest_authority_failure_details={
                    "aggregate-evidence-manifest-malformed"
                },
            )
    else:
        with pytest.raises(ContractValidationError) as exc_info:
            freeze_ci_validation_aggregate_summary(
                created_at=CREATED_AT,
                repository_owner="hcoona",
                repository_name="three",
                workflow="CI Validation",
                run_id=RUN_ID,
                run_attempt=RUN_ATTEMPT,
                aggregate_evidence_manifest=manifest_claim,
                final_artifacts=cast(
                    "dict[str, object]", summary["final-artifacts"]
                ),
                validation_tree=cast(
                    "dict[str, object]", summary["validation-tree"]
                ),
                affected_range=cast(
                    "dict[str, object]", summary["affected-range"]
                ),
                request=cast("dict[str, object]", summary["request"]),
                scheduled_full=cast(
                    "dict[str, object]", summary["scheduled-full"]
                ),
                verdict=cast("str", summary["verdict"]),
                reason=reason,
                budgets=cast("dict[str, object]", summary["budgets"]),
                diagnostics=cast(
                    "list[dict[str, object]]", summary["diagnostics"]
                ),
                batch_bundles=cast(
                    "list[dict[str, object]]", summary["batch-bundles"]
                ),
                evidence_results=cast(
                    "list[dict[str, object]]",
                    summary["evidence-results"],
                ),
                failures=cast("list[dict[str, object]]", summary["failures"]),
                work_groups=cast("dict[str, object]", summary["work-groups"]),
                aggregate_evidence_manifest_document=aggregate_manifest,
                aggregate_evidence_manifest_bound=True,
                aggregate_evidence_manifest_external_binding_verified=True,
                aggregate_manifest_authority_failure_details=[
                    "aggregate-evidence-manifest-malformed"
                ],
            )

    assert any(
        issue.path
        in {
            "$.aggregate-evidence-manifest.artifact-instance-id",
            "$.aggregate-evidence-manifest.content-digest",
            "$.final-artifacts.aggregate-evidence-manifest.artifact-instance-id",
            "$.final-artifacts.aggregate-evidence-manifest.content-digest",
        }
        for issue in exc_info.value.issues
    )


def test_invalid_plan_freezer_marks_bound_manifest_external_binding_verified(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Bound aggregate manifests must stay externally verified in validation."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_invalid_plan(summary)
    cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )["producer-verified"] = True
    captured_kwargs: dict[str, object] = {}

    def fake_validate(_summary: object, **kwargs: object) -> None:
        captured_kwargs.update(kwargs)

    monkeypatch.setattr(
        ci_validation_batches,
        "validate_ci_validation_aggregate_summary",
        fake_validate,
    )
    monkeypatch.setattr(
        three_workflow_release_contracts_pkg,
        "validate_ci_validation_aggregate_summary",
        fake_validate,
    )

    ci_validation_batches.freeze_ci_validation_aggregate_summary(
        created_at=CREATED_AT,
        repository_owner="hcoona",
        repository_name="three",
        workflow="CI Validation",
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        aggregate_evidence_manifest=cast(
            "dict[str, object]",
            summary["aggregate-evidence-manifest"],
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
        aggregate_evidence_manifest_document=aggregate_manifest,
        aggregate_evidence_manifest_bound=True,
        aggregate_evidence_manifest_external_binding_verified=True,
        aggregate_manifest_authority_failure_details=[
            "aggregate-evidence-manifest-malformed",
        ],
    )

    assert captured_kwargs["_aggregate_evidence_manifest_bound"] is True
    assert (
        captured_kwargs[
            "_aggregate_evidence_manifest_external_binding_verified"
        ]
        is True
    )


def test_invalid_plan_freezer_ignores_bool_details_authority_failure() -> None:
    """Freezing ignores final authority from bool plus details alone."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_invalid_plan(summary)
    raw_digest = "0123456789abcdef" * 4
    manifest_claim = cast(
        "dict[str, object]", summary["aggregate-evidence-manifest"]
    )
    final_manifest = cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )
    manifest_claim["artifact-instance-id"] = "forged-aggregate-manifest"
    manifest_claim["content-digest"] = raw_digest
    final_manifest["artifact-instance-id"] = "forged-aggregate-manifest"
    final_manifest["content-digest"] = raw_digest
    final_manifest["producer-verified"] = False
    final_manifest["authority-diagnostics"] = [
        _diagnostic(
            "authority/aggregate-evidence-manifest-malformed",
            code="final-evidence-failure",
            detail="aggregate-evidence-manifest-malformed",
            severity="fail-closed",
            verdict_effect="fail-closed",
        )
    ]
    reason = cast("dict[str, object]", summary["reason"])
    reason["final-evidence-failure"] = True
    reason["final-producer-unverified"] = True

    frozen = freeze_ci_validation_aggregate_summary(
        created_at=CREATED_AT,
        repository_owner="hcoona",
        repository_name="three",
        workflow="CI Validation",
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        aggregate_evidence_manifest=manifest_claim,
        final_artifacts=cast("dict[str, object]", summary["final-artifacts"]),
        validation_tree=cast("dict[str, object]", summary["validation-tree"]),
        affected_range=cast("dict[str, object]", summary["affected-range"]),
        request=cast("dict[str, object]", summary["request"]),
        scheduled_full=cast("dict[str, object]", summary["scheduled-full"]),
        verdict=cast("str", summary["verdict"]),
        reason=reason,
        budgets=cast("dict[str, object]", summary["budgets"]),
        diagnostics=cast("list[dict[str, object]]", summary["diagnostics"]),
        batch_bundles=cast("list[dict[str, object]]", summary["batch-bundles"]),
        evidence_results=cast(
            "list[dict[str, object]]",
            summary["evidence-results"],
        ),
        failures=cast("list[dict[str, object]]", summary["failures"]),
        work_groups=cast("dict[str, object]", summary["work-groups"]),
        aggregate_evidence_manifest_bound=True,
        aggregate_manifest_authority_failure_details=[
            "aggregate-evidence-manifest-malformed"
        ],
    )

    frozen_final_manifest = cast(
        "dict[str, object]",
        cast("dict[str, object]", frozen["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )
    frozen_manifest_claim = cast(
        "dict[str, object]", frozen["aggregate-evidence-manifest"]
    )
    assert frozen_manifest_claim["artifact-instance-id"] is None
    assert frozen_manifest_claim["content-digest"] is None
    assert frozen_final_manifest["artifact-instance-id"] is None
    assert frozen_final_manifest["content-digest"] is None
    assert frozen_final_manifest["authority-diagnostics"] == []
    assert frozen_final_manifest["producer-verified"] is False
    assert (
        cast("dict[str, object]", frozen["reason"])["final-evidence-failure"]
        is False
    )


def test_invalid_plan_rejects_uncovered_final_evidence_failure() -> None:
    """Invalid-plan final evidence rows must be covered by root diagnostics."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_invalid_plan(summary)
    final_manifest = cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )
    final_manifest["authority-diagnostics"] = [
        _diagnostic(
            "authority/aggregate-evidence-manifest-missing",
            code="final-evidence-failure",
            detail="aggregate-evidence-manifest-missing",
            severity="fail-closed",
            verdict_effect="fail-closed",
        )
    ]
    reason = cast("dict[str, object]", summary["reason"])
    reason["final-evidence-failure"] = True
    cast("list[dict[str, object]]", summary["failures"]).append(
        {
            "kind": "final-evidence-failure",
            "batch-id": None,
            "work-group-id": None,
            "evidence-expectation-id": None,
            "bundle-id": None,
            "diagnostic": _diagnostic(
                "final-evidence-failure/aggregate-evidence-manifest-missing",
                code="final-evidence-failure",
                detail="aggregate-evidence-manifest-missing",
                severity="fail-closed",
                verdict_effect="fail-closed",
            ),
            "message": "Missing aggregate evidence manifest authority.",
        }
    )
    _sort_summary_failures(summary)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path.endswith(".diagnostic")
        and "covered by root diagnostics" in issue.message
        for issue in exc_info.value.issues
    )


def test_invalid_plan_rejects_unverified_manifest_final_failure() -> None:
    """Invalid-plan final evidence failures require authority diagnostics."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_invalid_plan(summary)
    manifest_claim = cast(
        "dict[str, object]", summary["aggregate-evidence-manifest"]
    )
    manifest_claim["artifact-instance-id"] = "3001"
    manifest_claim["content-digest"] = "0" * 64
    final_manifest = cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )
    final_manifest["artifact-instance-id"] = "3001"
    final_manifest["content-digest"] = "0" * 64
    final_manifest["producer-verified"] = False
    reason = cast("dict[str, object]", summary["reason"])
    reason["final-evidence-failure"] = True
    cast("list[dict[str, object]]", summary["failures"]).append(
        {
            "kind": "final-evidence-failure",
            "batch-id": None,
            "work-group-id": None,
            "evidence-expectation-id": None,
            "bundle-id": None,
            "diagnostic": _diagnostic(
                "final-evidence-failure/final-producer-unverified",
                code="final-evidence-failure",
                detail="final-producer-unverified",
                severity="fail-closed",
                verdict_effect="fail-closed",
            ),
            "message": "Aggregate evidence manifest producer was unverified.",
        }
    )
    _sort_summary_failures(summary)

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(summary)


def test_invalid_plan_rejects_summary_local_manifest_authority() -> None:
    """Summary-local manifest identity cannot bind final authority failures."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_invalid_plan(summary)
    manifest_claim = cast(
        "dict[str, object]", summary["aggregate-evidence-manifest"]
    )
    final_manifest = cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )
    manifest_claim["artifact-instance-id"] = "forged-upload"
    manifest_claim["content-digest"] = "0123456789abcdef" * 4
    final_manifest["artifact-instance-id"] = "forged-upload"
    final_manifest["content-digest"] = "0123456789abcdef" * 4
    final_manifest["producer-verified"] = False
    authority_diagnostic = _diagnostic(
        "final-evidence-failure/aggregate-evidence-manifest-malformed",
        code="final-evidence-failure",
        detail="aggregate-evidence-manifest-malformed",
        message="Preserved aggregate evidence manifest was malformed.",
        severity="fail-closed",
        verdict_effect="fail-closed",
    )
    final_manifest["authority-diagnostics"] = [authority_diagnostic]
    reason = cast("dict[str, object]", summary["reason"])
    reason["final-producer-unverified"] = True
    reason["final-evidence-failure"] = True
    final_producer_diagnostic = _diagnostic(
        "final-producer-unverified",
        code="final-producer-unverified",
        detail="final-producer-unverified",
        message=(
            "Aggregate evidence manifest producer boundary was not verified "
            "before summary generation."
        ),
        severity="fail-closed",
        verdict_effect="fail-closed",
    )
    final_producer_evidence_diagnostic = _diagnostic(
        "final-evidence-failure/final-producer-unverified",
        code="final-evidence-failure",
        detail="final-producer-unverified",
        message="Aggregate evidence manifest producer was unverified.",
        severity="fail-closed",
        verdict_effect="fail-closed",
    )
    cast("list[dict[str, object]]", summary["diagnostics"]).extend(
        [
            authority_diagnostic,
            final_producer_diagnostic,
            final_producer_evidence_diagnostic,
        ]
    )
    cast("list[dict[str, object]]", summary["failures"]).extend(
        [
            {
                "kind": "final-evidence-failure",
                "batch-id": None,
                "work-group-id": None,
                "evidence-expectation-id": None,
                "bundle-id": None,
                "diagnostic": authority_diagnostic,
                "message": authority_diagnostic["message"],
            },
            {
                "kind": "final-evidence-failure",
                "batch-id": None,
                "work-group-id": None,
                "evidence-expectation-id": None,
                "bundle-id": None,
                "diagnostic": final_producer_evidence_diagnostic,
                "message": final_producer_evidence_diagnostic["message"],
            },
            {
                "kind": "final-producer-unverified",
                "batch-id": None,
                "work-group-id": None,
                "evidence-expectation-id": None,
                "bundle-id": None,
                "diagnostic": final_producer_diagnostic,
                "message": final_producer_diagnostic["message"],
            },
        ]
    )
    cast("list[dict[str, object]]", summary["diagnostics"]).sort(
        key=lambda item: str(item.get("diagnostic-id")),
    )
    _sort_summary_failures(summary)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            _require_aggregate_evidence_manifest=False,
        )

    assert any(
        issue.path.startswith("$.final-artifacts.aggregate-evidence-manifest")
        or issue.path == "$.failures"
        for issue in exc_info.value.issues
    )


def test_invalid_plan_rejects_summary_local_missing_manifest_authority() -> (
    None
):
    """Summary-local missing-manifest diagnostics cannot bind authority."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_invalid_plan(summary)
    manifest_claim = cast(
        "dict[str, object]", summary["aggregate-evidence-manifest"]
    )
    final_manifest = cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )
    manifest_claim["artifact-instance-id"] = "forged-upload"
    manifest_claim["content-digest"] = "0123456789abcdef" * 4
    final_manifest["artifact-instance-id"] = "forged-upload"
    final_manifest["content-digest"] = "0123456789abcdef" * 4
    final_manifest["producer-verified"] = False
    authority_diagnostic = _diagnostic(
        "final-evidence-failure/aggregate-evidence-manifest-missing",
        code="final-evidence-failure",
        detail="aggregate-evidence-manifest-missing",
        message="Missing aggregate evidence manifest authority.",
        severity="fail-closed",
        verdict_effect="fail-closed",
    )
    final_manifest["authority-diagnostics"] = [authority_diagnostic]
    reason = cast("dict[str, object]", summary["reason"])
    reason["final-producer-unverified"] = True
    reason["final-evidence-failure"] = True
    final_producer_diagnostic = _diagnostic(
        "final-producer-unverified",
        code="final-producer-unverified",
        detail="final-producer-unverified",
        message=(
            "Aggregate evidence manifest producer boundary was not verified "
            "before summary generation."
        ),
        severity="fail-closed",
        verdict_effect="fail-closed",
    )
    final_producer_evidence_diagnostic = _diagnostic(
        "final-evidence-failure/final-producer-unverified",
        code="final-evidence-failure",
        detail="final-producer-unverified",
        message="Aggregate evidence manifest producer was unverified.",
        severity="fail-closed",
        verdict_effect="fail-closed",
    )
    cast("list[dict[str, object]]", summary["diagnostics"]).extend(
        [
            authority_diagnostic,
            final_producer_diagnostic,
            final_producer_evidence_diagnostic,
        ]
    )
    cast("list[dict[str, object]]", summary["failures"]).extend(
        [
            {
                "kind": "final-evidence-failure",
                "batch-id": None,
                "work-group-id": None,
                "evidence-expectation-id": None,
                "bundle-id": None,
                "diagnostic": authority_diagnostic,
                "message": authority_diagnostic["message"],
            },
            {
                "kind": "final-evidence-failure",
                "batch-id": None,
                "work-group-id": None,
                "evidence-expectation-id": None,
                "bundle-id": None,
                "diagnostic": final_producer_evidence_diagnostic,
                "message": final_producer_evidence_diagnostic["message"],
            },
            {
                "kind": "final-producer-unverified",
                "batch-id": None,
                "work-group-id": None,
                "evidence-expectation-id": None,
                "bundle-id": None,
                "diagnostic": final_producer_diagnostic,
                "message": final_producer_diagnostic["message"],
            },
        ]
    )
    cast("list[dict[str, object]]", summary["diagnostics"]).sort(
        key=lambda item: str(item.get("diagnostic-id")),
    )
    _sort_summary_failures(summary)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            _require_aggregate_evidence_manifest=False,
        )

    assert any(
        issue.path.startswith("$.final-artifacts.aggregate-evidence-manifest")
        or issue.path == "$.failures"
        for issue in exc_info.value.issues
    )


def test_invalid_plan_rejects_unbound_final_producer() -> None:
    """Final producer failures require external manifest binding."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_invalid_plan(summary)
    _mark_summary_bound_final_producer_unverified(
        summary,
        bind_final_manifest=False,
        include_derived_final_evidence=False,
    )

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(summary)

    assert any(
        issue.path == "$.failures"
        and "bound unverified final manifest producer" in issue.message
        for issue in exc_info.value.issues
    )


def test_invalid_plan_rejects_uncovered_final_producer_failure() -> None:
    """Invalid-plan final producer rows must be covered by root diagnostics."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_invalid_plan(summary)
    _mark_summary_bound_final_producer_unverified(
        summary,
        bind_final_manifest=False,
        include_derived_final_evidence=False,
    )
    summary["diagnostics"] = _without_final_producer_diagnostics(summary)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(summary)

    assert any(
        issue.path.endswith(".diagnostic")
        and "covered by root diagnostics" in issue.message
        for issue in exc_info.value.issues
    )


def test_invalid_plan_accepts_canonical_fail_closed_invalid_plan() -> None:
    """Canonical fail-closed invalid-plan rows preserve fail-closed reason."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_invalid_plan(summary)
    failure = next(
        failure
        for failure in cast("list[dict[str, object]]", summary["failures"])
        if failure["kind"] == "invalid-plan"
    )
    failure["kind"] = "fail-closed"
    failure["message"] = "Validation planning failed closed."
    diagnostic = cast("dict[str, object]", failure["diagnostic"])
    diagnostic.update(
        {
            "diagnostic-id": "fail-closed/invalid-plan/plan-missing",
            "code": "invalid-plan",
            "detail": "plan-missing",
            "message": "Validation planning failed closed.",
            "source": {"type": "aggregation", "id": None},
            "severity": "fail-closed",
            "verdict-effect": "fail-closed",
        }
    )
    reason = cast("dict[str, object]", summary["reason"])
    reason["invalid-plan"] = False
    reason["fail-closed"] = True
    _sort_summary_failures(summary)

    validate_ci_validation_aggregate_summary(summary)


def test_invalid_plan_accepts_nonzero_actual_total_jobs() -> None:
    """Invalid-plan summaries may preserve physical job counts."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_invalid_plan(summary)
    cast("dict[str, object]", summary["budgets"])["actual-total-jobs"] = 1

    validate_ci_validation_aggregate_summary(summary)


def test_invalid_plan_rejects_unverified_final_producer_without_failure() -> (
    None
):
    """producer-verified false requires matching failure state."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )["producer-verified"] = False
    cast("dict[str, object]", summary["reason"])[
        "final-producer-unverified"
    ] = False
    summary["failures"] = [
        failure
        for failure in cast("list[dict[str, object]]", summary["failures"])
        if failure["kind"] != "final-producer-unverified"
    ]
    summary["diagnostics"] = _without_final_producer_diagnostics(summary)

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
        (
            issue.path == "$.failures"
            and "final-producer-unverified" in issue.message
            and ("failure" in issue.message or "must include" in issue.message)
        )
        or (
            issue.path == "$.final-artifacts.aggregate-evidence-manifest"
            and (
                "unverified producer requires bound artifact instance "
                "and digest"
            )
            in issue.message
        )
        for issue in exc_info.value.issues
    )


def test_retained_invalid_plan_rejects_unbound_final_producer_unverified() -> (
    None
):
    """Retained invalid-plan summaries require bound final producer failures."""
    plan = _plan()
    aggregate_manifest = _authoritative_invalid_planning_input_manifest(
        plan,
        "changed-files-snapshot",
        "changed-files-snapshot-schema-invalid",
    )
    with pytest.raises(ContractValidationError) as exc_info:
        _freeze_invalid_planning_input_summary(
            aggregate_manifest,
            plan=plan,
            final_manifest_producer_verified=False,
        )

    assert any(
        issue.path
        == "$.final-artifacts.aggregate-evidence-manifest.producer-verified"
        and "final-producer-unverified" in issue.message
        for issue in exc_info.value.issues
    )


def test_mixed_retained_invalid_plan_details_keep_authority() -> None:
    """Mixed retained invalid-plan details cannot downgrade to no authority."""
    detail_from_set = vars(ci_validation_batches)[
        "_invalid_plan_failure_detail_from_detail_set"
    ]
    selected_detail = detail_from_set(
        {"plan-duplicate", "plan-producer-unverified"}
    )

    assert selected_detail == "plan-duplicate"
    assert selected_detail != "malformed-plan"


@pytest.mark.parametrize(
    ("detail", "message"),
    [
        pytest.param(
            "plan-missing",
            CI_VALIDATION_INVALID_PLAN_MISSING_MESSAGE,
            id="plan-missing",
        ),
        pytest.param(
            "malformed-plan",
            CI_VALIDATION_INVALID_PLAN_NON_AUTHORITATIVE_MESSAGE,
            id="malformed-plan",
        ),
        pytest.param(
            "plan-unreadable",
            CI_VALIDATION_INVALID_PLAN_NON_AUTHORITATIVE_MESSAGE,
            id="plan-unreadable",
        ),
    ],
)
def test_invalid_plan_rejects_no_authority_preserved_projection(
    detail: str,
    message: str,
) -> None:
    """No-authority details reject preserved projection context."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_invalid_plan(summary)
    failure = _summary_failure(summary)
    failure["message"] = message
    diagnostic = cast("dict[str, object]", failure["diagnostic"])
    diagnostic["diagnostic-id"] = (
        "invalid-plan" if detail == "plan-missing" else f"invalid-plan/{detail}"
    )
    diagnostic["detail"] = detail
    diagnostic["message"] = message
    summary["diagnostics"] = [diagnostic]
    summary["plan-id"] = plan["plan-id"]
    summary["plan-digest"] = plan["plan-digest"]
    summary["mode"] = plan["mode"]
    summary["validation-tree"] = plan["validation-tree"]
    summary["affected-range"] = _summary_affected_range(plan)
    summary["request"] = plan["request"]
    summary["scheduled-full"] = plan["scheduled-full"]

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(summary, plan=plan)

    assert any(
        (
            issue.path == "$.projection-authority"
            and "no-authority projection context" in issue.message
        )
        or "must match invalid-plan context" in issue.message
        for issue in exc_info.value.issues
    )
    assert all(
        issue.path not in {"$.plan-id", "$.plan-digest"}
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    ("input_name", "detail"),
    [
        pytest.param(
            "changed-files-snapshot",
            "changed-files-snapshot-malformed",
            id="changed-files-snapshot-malformed",
        ),
        pytest.param(
            "fact-snapshot",
            "fact-snapshot-malformed",
            id="fact-snapshot-malformed",
        ),
    ],
)
def test_invalid_plan_rejects_malformed_snapshot_without_projection_authority(
    input_name: str,
    detail: str,
) -> None:
    """Retained malformed snapshots require projection authority."""
    aggregate_manifest = _invalid_planning_input_manifest(
        input_name,
        detail,
    )

    with pytest.raises(ContractValidationError) as exc_info:
        _freeze_invalid_planning_input_summary(aggregate_manifest)

    assert any(
        issue.path == "$.projection-authority"
        and "retained invalid-plan details" in issue.message
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    ("input_name", "detail"),
    [
        pytest.param(
            "changed-files-snapshot",
            "changed-files-snapshot-malformed",
            id="changed-files-snapshot-malformed",
        ),
        pytest.param(
            "fact-snapshot",
            "fact-snapshot-malformed",
            id="fact-snapshot-malformed",
        ),
    ],
)
def test_standalone_aggregate_manifest_rejects_malformed_snapshot_no_authority(
    input_name: str,
    detail: str,
) -> None:
    """Retained malformed snapshots require manifest projection authority."""
    aggregate_manifest = _invalid_planning_input_manifest(
        input_name,
        detail,
    )

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(aggregate_manifest)

    assert any(
        issue.path == "$.projection-authority"
        and "retained invalid-plan details" in issue.message
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    ("input_name", "detail"),
    [
        pytest.param(
            "changed-files-snapshot",
            "changed-files-snapshot-malformed",
            id="changed-files-snapshot-malformed",
        ),
        pytest.param(
            "fact-snapshot",
            "fact-snapshot-malformed",
            id="fact-snapshot-malformed",
        ),
    ],
)
def test_standalone_malformed_snapshot_rejects_unbound_projection(
    input_name: str,
    detail: str,
) -> None:
    """Retained malformed snapshots reject unproven projection authority."""
    aggregate_manifest = _invalid_planning_input_manifest(
        input_name,
        detail,
    )
    aggregate_manifest["projection-authority"] = (
        _projection_authority_from_plan(_plan())
    )

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(aggregate_manifest)

    assert any(
        issue.path == "$.projection-authority"
        for issue in exc_info.value.issues
    )


def test_standalone_malformed_snapshot_does_not_hide_retained_detail() -> None:
    """Malformed snapshot collapse cannot mask retained invalid-plan detail."""
    aggregate_manifest = _invalid_planning_input_manifest(
        "changed-files-snapshot",
        "changed-files-snapshot-malformed",
    )
    validation_plan = cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            "validation-plan"
        ],
    )
    validation_plan["admissibility"] = "inadmissible"
    validation_plan["diagnostics"] = [
        _diagnostic(
            "invalid-plan/schema-invalid",
            code="invalid-plan",
            detail="schema-invalid",
            message=CI_VALIDATION_INVALID_PLAN_NON_AUTHORITATIVE_MESSAGE,
            severity="fail-closed",
            verdict_effect="fail-closed",
            source_id=cast("str | None", validation_plan["artifact-ref"]),
        )
    ]

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(aggregate_manifest)

    assert any(
        issue.path == "$.projection-authority"
        and "retained invalid-plan details" in issue.message
        for issue in exc_info.value.issues
    )


def test_standalone_invalid_plan_rejects_ambiguous_retained_diagnostics() -> (
    None
):
    """Multiple invalid-plan diagnostics cannot collapse to no-authority."""
    aggregate_manifest = _invalid_planning_input_manifest(
        "validation-plan",
        "malformed-plan",
    )
    validation_plan = cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            "validation-plan"
        ],
    )
    diagnostics = cast(
        "list[dict[str, object]]",
        validation_plan["diagnostics"],
    )
    diagnostics.append(
        _diagnostic(
            "invalid-plan/plan-duplicate",
            code="invalid-plan",
            detail="plan-duplicate",
            message=CI_VALIDATION_INVALID_PLAN_NON_AUTHORITATIVE_MESSAGE,
            severity="fail-closed",
            verdict_effect="fail-closed",
            source_id=cast("str | None", validation_plan["artifact-ref"]),
        )
    )

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(aggregate_manifest)

    assert any(
        issue.path == "$.projection-authority"
        and "retained invalid-plan details" in issue.message
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    ("input_name", "detail"),
    [
        pytest.param(
            "changed-files-snapshot",
            "changed-files-snapshot-malformed",
            id="changed-files-snapshot-malformed",
        ),
        pytest.param(
            "fact-snapshot",
            "fact-snapshot-malformed",
            id="fact-snapshot-malformed",
        ),
    ],
)
def test_malformed_snapshot_invalid_plan_rejects_retained_projection(
    input_name: str,
    detail: str,
) -> None:
    """Malformed snapshot details reject missing retained authority."""
    plan = _plan()
    aggregate_manifest = _invalid_planning_input_manifest(
        input_name,
        detail,
    )

    with pytest.raises(ContractValidationError) as exc_info:
        _freeze_invalid_planning_input_summary(
            aggregate_manifest,
            plan=plan,
        )

    assert any(
        issue.path == "$.projection-authority"
        and "retained invalid-plan details" in issue.message
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    ("input_name", "detail"),
    [
        pytest.param(
            "changed-files-snapshot",
            "changed-files-snapshot-malformed",
            id="changed-files-snapshot-malformed",
        ),
        pytest.param(
            "fact-snapshot",
            "fact-snapshot-malformed",
            id="fact-snapshot-malformed",
        ),
    ],
)
def test_malformed_snapshot_invalid_plan_rejects_no_authority_projection(
    input_name: str,
    detail: str,
) -> None:
    """Retained malformed snapshot details cannot use no authority."""
    aggregate_manifest = _invalid_planning_input_manifest(
        input_name,
        detail,
    )

    with pytest.raises(ContractValidationError) as exc_info:
        _freeze_invalid_planning_input_summary(aggregate_manifest)

    assert any(
        issue.path == "$.projection-authority"
        and "retained invalid-plan details" in issue.message
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    ("input_name", "detail"),
    [
        pytest.param(
            "changed-files-snapshot",
            "changed-files-snapshot-malformed",
            id="changed-files-snapshot-malformed",
        ),
        pytest.param(
            "fact-snapshot",
            "fact-snapshot-malformed",
            id="fact-snapshot-malformed",
        ),
    ],
)
def test_malformed_snapshot_invalid_plan_rejects_ambiguous_retained_diagnostics(
    input_name: str,
    detail: str,
) -> None:
    """Retained malformed snapshot diagnostics must be unambiguous."""
    plan = _plan()
    aggregate_manifest = _authoritative_invalid_planning_input_manifest(
        plan,
        input_name,
        detail,
    )
    input_artifact = cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            input_name
        ],
    )
    diagnostics = cast("list[dict[str, object]]", input_artifact["diagnostics"])
    diagnostics.append(dict(diagnostics[0]))

    with pytest.raises(ContractValidationError):
        _freeze_invalid_planning_input_summary(
            aggregate_manifest,
            plan=plan,
        )


@pytest.mark.parametrize(
    ("input_name", "detail"),
    [
        pytest.param(
            "changed-files-snapshot",
            "changed-files-snapshot-malformed",
            id="changed-files-snapshot-malformed",
        ),
        pytest.param(
            "fact-snapshot",
            "fact-snapshot-malformed",
            id="fact-snapshot-malformed",
        ),
    ],
)
def test_malformed_snapshot_freezer_rejects_no_authority_projection(
    input_name: str,
    detail: str,
) -> None:
    """Snapshot-malformed details require retained projection authority."""
    aggregate_manifest = _invalid_planning_input_manifest(
        input_name,
        detail,
    )

    with pytest.raises(ContractValidationError) as exc_info:
        _freeze_invalid_planning_input_summary(aggregate_manifest)

    assert any(
        issue.path == "$.projection-authority"
        and "retained invalid-plan details" in issue.message
        for issue in exc_info.value.issues
    )


def test_no_authority_invalid_plan_freezer_clears_manifest_claim() -> None:
    """No-authority invalid-plan summaries do not publish manifest identity."""
    aggregate_manifest = _invalid_planning_input_manifest(
        "validation-plan",
        "plan-missing",
    )

    summary = _freeze_invalid_planning_input_summary(aggregate_manifest)

    manifest_claim = cast(
        "dict[str, object]", summary["aggregate-evidence-manifest"]
    )
    final_manifest = cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )
    assert manifest_claim["artifact-instance-id"] is None
    assert manifest_claim["content-digest"] is None
    assert final_manifest["artifact-instance-id"] is None
    assert final_manifest["content-digest"] is None
    assert final_manifest["producer-verified"] is False
    validate_ci_validation_aggregate_summary(summary)


def test_no_authority_invalid_plan_strips_unbound_producer_unverified() -> None:
    """No-authority invalid plans strip unbound producer-unverified state."""
    aggregate_manifest = _invalid_planning_input_manifest(
        "validation-plan",
        "plan-missing",
    )

    summary = _freeze_invalid_planning_input_summary(
        aggregate_manifest,
        final_manifest_producer_verified=False,
    )

    final_manifest = cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )
    assert final_manifest["artifact-instance-id"] is None
    assert final_manifest["content-digest"] is None
    assert final_manifest["producer-verified"] is False
    assert not any(
        failure.get("kind") == "final-producer-unverified"
        for failure in cast("list[dict[str, object]]", summary["failures"])
    )


def test_no_authority_invalid_plan_rejects_summary_only_authority() -> None:
    """Summary-local final authority cannot bind no-authority invalid plans."""
    aggregate_manifest = _invalid_planning_input_manifest(
        "validation-plan",
        "plan-missing",
    )
    summary = _freeze_invalid_planning_input_summary(aggregate_manifest)
    forged_digest = "0123456789abcdef" * 4
    manifest_claim = cast(
        "dict[str, object]", summary["aggregate-evidence-manifest"]
    )
    final_manifest = cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )
    manifest_claim["artifact-instance-id"] = "forged-aggregate-manifest"
    manifest_claim["content-digest"] = forged_digest
    final_manifest["artifact-instance-id"] = "forged-aggregate-manifest"
    final_manifest["content-digest"] = forged_digest
    final_manifest["producer-verified"] = False
    authority_diagnostic = _diagnostic(
        "final-evidence-failure/aggregate-evidence-manifest-malformed",
        code="final-evidence-failure",
        detail="aggregate-evidence-manifest-malformed",
        severity="fail-closed",
        verdict_effect="fail-closed",
    )
    final_manifest["authority-diagnostics"] = [authority_diagnostic]
    producer_diagnostic = _diagnostic(
        "final-producer-unverified",
        code="final-producer-unverified",
        detail="final-producer-unverified",
        severity="fail-closed",
        verdict_effect="fail-closed",
    )
    reason = cast("dict[str, object]", summary["reason"])
    reason["final-evidence-failure"] = True
    reason["final-producer-unverified"] = True
    failures = cast("list[dict[str, object]]", summary["failures"])
    failures.extend(
        [
            {
                "kind": "final-evidence-failure",
                "batch-id": None,
                "work-group-id": None,
                "evidence-expectation-id": None,
                "bundle-id": None,
                "diagnostic": authority_diagnostic,
                "message": authority_diagnostic["message"],
            },
            {
                "kind": "final-producer-unverified",
                "batch-id": None,
                "work-group-id": None,
                "evidence-expectation-id": None,
                "bundle-id": None,
                "diagnostic": producer_diagnostic,
                "message": producer_diagnostic["message"],
            },
        ]
    )
    cast("list[dict[str, object]]", summary["diagnostics"]).extend(
        [authority_diagnostic, producer_diagnostic]
    )
    _sort_summary_failures(summary)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(summary)

    issue_paths = {issue.path for issue in exc_info.value.issues}
    assert "$.aggregate-evidence-manifest.content-digest" in issue_paths
    assert (
        "$.final-artifacts.aggregate-evidence-manifest.content-digest"
        in issue_paths
    )


@pytest.mark.parametrize(
    "detail", ["plan-missing", "malformed-plan", "plan-unreadable"]
)
def test_no_authority_invalid_plan_manifest_accepts_original_plan_context(
    detail: str,
) -> None:
    """Original context cannot make null no-authority identity stale."""
    aggregate_manifest = _invalid_planning_input_manifest(
        "validation-plan",
        detail,
    )

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=_plan(),
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
            expected_run_id=RUN_ID,
            expected_run_attempt=RUN_ATTEMPT,
        )

    issue_paths = {issue.path for issue in exc_info.value.issues}
    assert "$.plan-id" not in issue_paths
    assert "$.plan-digest" not in issue_paths


@pytest.mark.parametrize(
    "detail",
    ["plan-missing", "malformed-plan", "plan-unreadable"],
    ids=["plan-missing", "malformed-plan", "plan-unreadable"],
)
def test_no_authority_invalid_plan_fact_snapshot_mismatch_requires_proof(
    detail: str,
) -> None:
    """No-authority details do not compare facts to supplied plan context."""
    aggregate_manifest = _invalid_planning_input_manifest(
        "validation-plan",
        detail,
    )
    fact_snapshot = _fact_snapshot_document()
    fact_snapshot["plan-id"] = "other-plan"

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=_plan(),
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=fact_snapshot,
            expected_run_id=RUN_ID,
            expected_run_attempt=RUN_ATTEMPT,
        )

    issue_pairs = {
        (issue.path, issue.message) for issue in exc_info.value.issues
    }
    assert (
        "fact_snapshot.plan-id",
        "requires proven plan identity",
    ) in issue_pairs
    assert (
        "fact_snapshot.plan-id",
        "must match plan",
    ) not in issue_pairs


def test_summary_rejects_null_manifest_identity_with_bound_authority() -> None:
    """Authority diagnostics skip digest equality, not bound field presence."""
    aggregate_manifest = _invalid_planning_input_manifest(
        "validation-plan",
        "plan-missing",
    )
    summary = _freeze_invalid_planning_input_summary(aggregate_manifest)
    authority_diagnostic = _diagnostic(
        "final-evidence/aggregate-evidence-manifest-malformed",
        code="final-evidence",
        detail="aggregate-evidence-manifest-malformed",
        message="Preserved aggregate evidence manifest is malformed.",
        severity="fail-closed",
        verdict_effect="fail-closed",
    )
    final_manifest = cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )
    final_manifest["authority-diagnostics"] = [authority_diagnostic]
    final_manifest["producer-verified"] = False
    for claim in (
        cast("dict[str, object]", summary["aggregate-evidence-manifest"]),
        final_manifest,
    ):
        claim["artifact-instance-id"] = None
        claim["content-digest"] = None

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            aggregate_evidence_manifest=aggregate_manifest,
        )

    issue_paths = {issue.path for issue in exc_info.value.issues}
    assert any(
        path.startswith(
            "$.final-artifacts.aggregate-evidence-manifest.authority-diagnostics"
        )
        for path in issue_paths
    )


def test_invalid_plan_freezer_downgrades_malformed_supplied_plan() -> None:
    """Malformed supplied plans are downgraded before projection reads."""
    aggregate_manifest = _invalid_planning_input_manifest(
        "validation-plan",
        "malformed-plan",
    )

    summary = _freeze_invalid_planning_input_summary(
        aggregate_manifest,
        plan={"plan-id": "malformed-plan"},
    )

    failure = _summary_failure(summary)
    diagnostic = cast("dict[str, object]", failure["diagnostic"])
    assert diagnostic["detail"] == "malformed-plan"
    assert summary["plan-id"] is None
    validate_ci_validation_aggregate_summary(
        summary,
        aggregate_evidence_manifest=aggregate_manifest,
    )


@pytest.mark.parametrize(
    ("input_name", "detail"),
    [
        ("validation-plan", "schema-invalid"),
        ("fact-snapshot", "fact-snapshot-producer-unverified"),
    ],
)
def test_invalid_plan_freezer_downgrades_unauthorized_complete_supplied_plan(
    input_name: str,
    detail: str,
) -> None:
    """A complete supplied plan cannot downgrade retained projection details."""
    plan = _plan()
    aggregate_manifest = _invalid_planning_input_manifest(input_name, detail)

    with pytest.raises(ContractValidationError) as exc_info:
        _freeze_invalid_planning_input_summary(
            aggregate_manifest,
            plan=plan,
        )

    assert any(
        issue.path == "$.projection-authority"
        and "retained invalid-plan details" in issue.message
        for issue in exc_info.value.issues
    )


def test_invalid_plan_rejects_schema_invalid_no_authority_projection() -> None:
    """Schema-invalid plan details must preserve producer projection context."""
    aggregate_manifest = _invalid_planning_input_manifest(
        "validation-plan",
        "plan-unreadable",
    )
    summary = _freeze_invalid_planning_input_summary(aggregate_manifest)
    failure = _summary_failure(summary)
    diagnostic = cast("dict[str, object]", failure["diagnostic"])
    diagnostic["diagnostic-id"] = "invalid-plan/schema-invalid"
    diagnostic["detail"] = "schema-invalid"
    failure["message"] = CI_VALIDATION_INVALID_PLAN_NON_AUTHORITATIVE_MESSAGE

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(summary)

    assert any(
        issue.path == "$.projection-authority"
        and "complete producer-compatible projection context" in issue.message
        for issue in exc_info.value.issues
    )


def test_invalid_plan_rejects_retained_projection_without_authority() -> None:
    """Retained details require authoritative producer context."""
    plan = _plan()
    aggregate_manifest = _invalid_planning_input_manifest(
        "validation-plan",
        "plan-unreadable",
    )
    summary = _freeze_invalid_planning_input_summary(
        aggregate_manifest,
    )
    input_artifact = cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            "validation-plan"
        ],
    )
    diagnostic = _record_diagnostic(input_artifact)
    diagnostic["diagnostic-id"] = "invalid-plan/schema-invalid"
    diagnostic["detail"] = "schema-invalid"
    cast("dict[str, object]", summary["aggregate-evidence-manifest"])[
        "artifact-instance-id"
    ] = None
    cast("dict[str, object]", summary["aggregate-evidence-manifest"])[
        "content-digest"
    ] = None
    final_manifest = cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )
    final_manifest["artifact-instance-id"] = None
    final_manifest["content-digest"] = None
    failure = _summary_failure(summary)
    failure_diagnostic = cast("dict[str, object]", failure["diagnostic"])
    failure_diagnostic["diagnostic-id"] = "invalid-plan/schema-invalid"
    failure_diagnostic["detail"] = "schema-invalid"
    summary["diagnostics"] = [failure_diagnostic]
    summary["plan-id"] = plan["plan-id"]
    summary["plan-digest"] = plan["plan-digest"]
    summary["mode"] = plan["mode"]
    summary["validation-tree"] = plan["validation-tree"]
    summary["affected-range"] = _summary_affected_range(plan)
    summary["request"] = plan["request"]
    summary["scheduled-full"] = plan["scheduled-full"]

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            aggregate_evidence_manifest=aggregate_manifest,
        )

    assert any(
        issue.path == "$.projection-authority"
        and "producer-compatible projection context" in issue.message
        for issue in exc_info.value.issues
    )


def test_invalid_plan_rejects_corrupted_retained_plan_digest_authority() -> (
    None
):
    """Retained projection authority uses producer-bound digest context."""
    plan = _plan()
    corrupted_plan = deepcopy(plan)
    corrupted_plan["plan-digest"] = "0" * 64
    aggregate_manifest = _authoritative_invalid_planning_input_manifest(
        plan,
        "changed-files-snapshot",
        "changed-files-snapshot-schema-invalid",
    )
    summary = _freeze_invalid_planning_input_summary(
        aggregate_manifest,
        plan=plan,
    )
    validation_plan_artifact = cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            "validation-plan"
        ],
    )
    validation_plan_artifact["content-digest"] = corrupted_plan["plan-digest"]
    _refresh_summary_manifest_digest(summary, aggregate_manifest)
    summary["plan-id"] = corrupted_plan["plan-id"]
    summary["plan-digest"] = corrupted_plan["plan-digest"]
    summary["mode"] = corrupted_plan["mode"]
    summary["validation-tree"] = corrupted_plan["validation-tree"]
    summary["affected-range"] = _summary_affected_range(corrupted_plan)
    summary["request"] = corrupted_plan["request"]
    summary["scheduled-full"] = corrupted_plan["scheduled-full"]

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=corrupted_plan,
            aggregate_evidence_manifest=aggregate_manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
        )

    assert any(
        issue.path == "$.projection-authority"
        and "producer-compatible projection context" in issue.message
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    ("_input_name", "detail"),
    [
        pytest.param(
            "validation-plan",
            "schema-invalid",
            id="validation-plan-schema-invalid",
        ),
        pytest.param(
            "fact-snapshot",
            "fact-snapshot-producer-unverified",
            id="fact-snapshot-fact-snapshot-producer-unverified",
        ),
    ],
)
@pytest.mark.parametrize(
    "partial_field",
    [
        "plan-id",
        "plan-digest",
        "mode",
        "validation-tree",
        "affected-range",
        "request.artifact-ref",
        "request-digest",
        "scheduled-full",
    ],
    ids=[
        "plan-id",
        "plan-digest",
        "mode",
        "validation-tree",
        "affected-range",
        "request.artifact-ref",
        "request-digest",
        "scheduled-full",
    ],
)
def test_invalid_plan_rejects_partial_retained_projection(
    _input_name: str,
    detail: str,
    partial_field: str,
) -> None:
    """A single forged projection field is not retained authority."""
    plan = _plan()
    aggregate_manifest = _invalid_planning_input_manifest(
        "validation-plan",
        "plan-unreadable",
    )
    summary = _freeze_invalid_planning_input_summary(aggregate_manifest)
    failure = _summary_failure(summary)
    diagnostic = cast("dict[str, object]", failure["diagnostic"])
    diagnostic["diagnostic-id"] = f"invalid-plan/{detail}"
    diagnostic["detail"] = detail
    failure["message"] = CI_VALIDATION_INVALID_PLAN_NON_AUTHORITATIVE_MESSAGE
    _set_invalid_plan_partial_projection(summary, plan, partial_field)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(summary, plan=plan)

    assert any(
        issue.path == "$.projection-authority"
        and "complete producer-compatible projection context" in issue.message
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    ("input_name", "detail"),
    [
        (
            "validation-plan",
            "schema-invalid",
        ),
        (
            "changed-files-snapshot",
            "changed-files-snapshot-schema-invalid",
        ),
        ("fact-snapshot", "fact-snapshot-producer-unverified"),
    ],
    ids=[
        "validation-plan-schema-invalid",
        "changed-files-snapshot-changed-files-snapshot-schema-invalid",
        "fact-snapshot-fact-snapshot-producer-unverified",
    ],
)
def test_invalid_plan_accepts_retained_plan_projection(
    input_name: str,
    detail: str,
) -> None:
    """Retained details preserve producer-compatible projection context."""
    plan = _plan()
    aggregate_manifest = _authoritative_invalid_planning_input_manifest(
        plan,
        input_name,
        detail,
    )
    summary = _freeze_invalid_planning_input_summary(
        aggregate_manifest,
        plan=plan,
    )
    summary["plan-id"] = plan["plan-id"]
    summary["plan-digest"] = plan["plan-digest"]
    summary["mode"] = plan["mode"]
    summary["validation-tree"] = plan["validation-tree"]
    summary["affected-range"] = _summary_affected_range(plan)
    summary["request"] = plan["request"]
    summary["scheduled-full"] = plan["scheduled-full"]

    validate_ci_validation_aggregate_summary(
        summary,
        plan=plan,
        aggregate_evidence_manifest=aggregate_manifest,
        request=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )


def test_invalid_plan_rejects_retained_projection_with_inadmissible_request_summary(  # noqa: E501
) -> None:
    """Retained plan request summaries require valid request artifacts."""
    plan = _plan()
    aggregate_manifest = _authoritative_invalid_planning_input_manifest(
        plan,
        "changed-files-snapshot",
        "changed-files-snapshot-schema-invalid",
    )
    summary = _freeze_invalid_planning_input_summary(
        aggregate_manifest,
        plan=plan,
    )
    summary["plan-id"] = plan["plan-id"]
    summary["plan-digest"] = plan["plan-digest"]
    summary["mode"] = plan["mode"]
    summary["validation-tree"] = plan["validation-tree"]
    summary["affected-range"] = _summary_affected_range(plan)
    summary["request"] = plan["request"]
    summary["scheduled-full"] = plan["scheduled-full"]
    request_artifact = cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            "request"
        ],
    )
    request_artifact["admissibility"] = "inadmissible"
    request_artifact["diagnostics"] = [
        _diagnostic(
            "required-input-artifact-failure/snapshot-companion-unproven",
            code="required-input-artifact-failure",
            detail="snapshot-companion-unproven",
            message="Snapshot companion input artifact was not proven.",
            severity="fail-closed",
            verdict_effect="fail-closed",
            source_id=cast("str | None", request_artifact["artifact-ref"]),
        )
    ]
    _refresh_summary_manifest_digest(summary, aggregate_manifest)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path == "$.projection-authority"
        and "producer-compatible projection context" in issue.message
        for issue in exc_info.value.issues
    )


def test_invalid_plan_rejects_retained_projection_without_request_context() -> (
    None
):
    """Retained plan request summaries do not replace request documents."""
    plan = _plan()
    aggregate_manifest = _authoritative_invalid_planning_input_manifest(
        plan,
        "changed-files-snapshot",
        "changed-files-snapshot-schema-invalid",
    )
    summary = _freeze_invalid_planning_input_summary(
        aggregate_manifest,
        plan=plan,
    )
    summary["plan-id"] = plan["plan-id"]
    summary["plan-digest"] = plan["plan-digest"]
    summary["mode"] = plan["mode"]
    summary["validation-tree"] = plan["validation-tree"]
    summary["affected-range"] = _summary_affected_range(plan)
    summary["request"] = plan["request"]
    summary["scheduled-full"] = plan["scheduled-full"]

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path == "$.projection-authority"
        for issue in exc_info.value.issues
    )


def test_invalid_plan_manifest_requires_request_context() -> None:
    """Retained aggregate projection requires supplied request context."""
    plan = _plan()
    manifest = _manifest(plan)
    aggregate_manifest = _authoritative_invalid_planning_input_manifest(
        plan,
        "changed-files-snapshot",
        "changed-files-snapshot-schema-invalid",
    )
    aggregate_manifest["projection-authority"] = (
        _projection_authority_from_plan(plan)
    )

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path == "$.projection-authority"
        and (
            "retained invalid-plan details require aggregate manifest input "
            "authority"
        )
        in issue.message
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    ("input_name", "detail", "companion_input_name"),
    [
        pytest.param(
            "changed-files-snapshot",
            "changed-files-snapshot-schema-invalid",
            "fact-snapshot",
            id="changed-files-with-fact-companion",
        ),
        pytest.param(
            "fact-snapshot",
            "fact-snapshot-producer-unverified",
            "changed-files-snapshot",
            id="fact-with-changed-files-companion",
        ),
    ],
)
def test_invalid_plan_accepts_retained_projection_with_snapshot_companion_fallback(  # noqa: E501
    input_name: str,
    detail: str,
    companion_input_name: str,
) -> None:
    """Snapshot companion fallback preserves retained projection authority."""
    plan = _plan()
    aggregate_manifest = _authoritative_invalid_planning_input_manifest(
        plan,
        input_name,
        detail,
    )
    summary = _freeze_invalid_planning_input_summary(
        aggregate_manifest,
        plan=plan,
    )
    summary["plan-id"] = plan["plan-id"]
    summary["plan-digest"] = plan["plan-digest"]
    summary["mode"] = plan["mode"]
    summary["validation-tree"] = plan["validation-tree"]
    summary["affected-range"] = _summary_affected_range(plan)
    summary["request"] = plan["request"]
    summary["scheduled-full"] = plan["scheduled-full"]
    companion_artifact = cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            companion_input_name
        ],
    )
    companion_artifact["artifact-ref"] = None
    companion_artifact["artifact-instance-id"] = None
    companion_artifact["content-digest"] = None
    companion_artifact["admissibility"] = "missing"
    companion_artifact["diagnostics"] = [
        _diagnostic(
            "required-input-artifact-failure/snapshot-companion-unproven",
            code="required-input-artifact-failure",
            detail="snapshot-companion-unproven",
            message="Snapshot companion input artifact was not proven.",
            severity="fail-closed",
            verdict_effect="fail-closed",
            source_id=cast("str | None", companion_artifact["artifact-ref"]),
        )
    ]
    _refresh_summary_manifest_digest(summary, aggregate_manifest)

    validate_ci_validation_aggregate_summary(
        summary,
        plan=plan,
        aggregate_evidence_manifest=aggregate_manifest,
        request=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )
    assert [
        diagnostic["diagnostic-id"]
        for diagnostic in cast(
            "list[dict[str, object]]",
            companion_artifact["diagnostics"],
        )
    ] == [
        "required-input-artifact-failure/snapshot-companion-unproven",
    ]


@pytest.mark.parametrize(
    ("input_name", "detail", "companion_input_name"),
    [
        pytest.param(
            "changed-files-snapshot",
            "changed-files-snapshot-schema-invalid",
            "fact-snapshot",
            id="changed-files-with-fact-companion",
        ),
        pytest.param(
            "fact-snapshot",
            "fact-snapshot-producer-unverified",
            "changed-files-snapshot",
            id="fact-with-changed-files-companion",
        ),
    ],
)
def test_invalid_plan_accepts_retained_projection_with_inadmissible_snapshot_companion_fallback(  # noqa: E501
    input_name: str,
    detail: str,
    companion_input_name: str,
) -> None:
    """Inadmissible no-binding fallback preserves retained authority."""
    plan = _plan()
    aggregate_manifest = _authoritative_invalid_planning_input_manifest(
        plan,
        input_name,
        detail,
    )
    summary = _freeze_invalid_planning_input_summary(
        aggregate_manifest,
        plan=plan,
    )
    summary["plan-id"] = plan["plan-id"]
    summary["plan-digest"] = plan["plan-digest"]
    summary["mode"] = plan["mode"]
    summary["validation-tree"] = plan["validation-tree"]
    summary["affected-range"] = _summary_affected_range(plan)
    summary["request"] = plan["request"]
    summary["scheduled-full"] = plan["scheduled-full"]
    companion_artifact = cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            companion_input_name
        ],
    )
    companion_artifact["artifact-ref"] = None
    companion_artifact["artifact-instance-id"] = None
    companion_artifact["content-digest"] = None
    companion_artifact["admissibility"] = "inadmissible"
    companion_artifact["diagnostics"] = [
        _diagnostic(
            "required-input-artifact-failure/snapshot-companion-unproven",
            code="required-input-artifact-failure",
            detail="snapshot-companion-unproven",
            message="Snapshot companion input artifact was not proven.",
            severity="fail-closed",
            verdict_effect="fail-closed",
            source_id=None,
        )
    ]
    _refresh_summary_manifest_digest(summary, aggregate_manifest)

    validate_ci_validation_aggregate_summary(
        summary,
        plan=plan,
        aggregate_evidence_manifest=aggregate_manifest,
        request=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )
    assert [
        diagnostic["diagnostic-id"]
        for diagnostic in cast(
            "list[dict[str, object]]",
            companion_artifact["diagnostics"],
        )
    ] == [
        "required-input-artifact-failure/snapshot-companion-unproven",
    ]


@pytest.mark.parametrize(
    ("input_name", "detail", "companion_input_name"),
    [
        pytest.param(
            "changed-files-snapshot",
            "changed-files-snapshot-schema-invalid",
            "fact-snapshot",
            id="changed-files-with-fact-companion",
        ),
        pytest.param(
            "fact-snapshot",
            "fact-snapshot-producer-unverified",
            "changed-files-snapshot",
            id="fact-with-changed-files-companion",
        ),
    ],
)
def test_invalid_plan_rejects_retained_projection_with_bound_snapshot_companion_fallback(  # noqa: E501
    input_name: str,
    detail: str,
    companion_input_name: str,
) -> None:
    """Snapshot unavailable fallback must not carry bound artifact fields."""
    plan = _plan()
    aggregate_manifest = _authoritative_invalid_planning_input_manifest(
        plan,
        input_name,
        detail,
    )
    summary = _freeze_invalid_planning_input_summary(
        aggregate_manifest,
        plan=plan,
    )
    summary["plan-id"] = plan["plan-id"]
    summary["plan-digest"] = plan["plan-digest"]
    summary["mode"] = plan["mode"]
    summary["validation-tree"] = plan["validation-tree"]
    summary["affected-range"] = _summary_affected_range(plan)
    summary["request"] = plan["request"]
    summary["scheduled-full"] = plan["scheduled-full"]
    companion_artifact = cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            companion_input_name
        ],
    )
    companion_artifact["admissibility"] = "inadmissible"
    companion_artifact["diagnostics"] = [
        _diagnostic(
            "required-input-artifact-failure/snapshot-companion-unproven",
            code="required-input-artifact-failure",
            detail="snapshot-companion-unproven",
            message="Snapshot companion input artifact was not proven.",
            severity="fail-closed",
            verdict_effect="fail-closed",
            source_id=cast("str | None", companion_artifact["artifact-ref"]),
        )
    ]
    _refresh_summary_manifest_digest(summary, aggregate_manifest)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path == "$.projection-authority"
        and "producer-compatible projection context" in issue.message
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    ("input_name", "detail", "companion_input_name"),
    [
        pytest.param(
            "changed-files-snapshot",
            "changed-files-snapshot-schema-invalid",
            "fact-snapshot",
            id="changed-files-with-fact-companion",
        ),
        pytest.param(
            "fact-snapshot",
            "fact-snapshot-producer-unverified",
            "changed-files-snapshot",
            id="fact-with-changed-files-companion",
        ),
    ],
)
@pytest.mark.parametrize(
    "diagnostic_case",
    [
        pytest.param("other-id", id="other-id"),
        pytest.param("wrong-detail", id="wrong-detail"),
        pytest.param(
            "with-unrelated-diagnostic", id="with-unrelated-diagnostic"
        ),
    ],
)
def test_invalid_plan_rejects_retained_projection_with_noncanonical_snapshot_companion_fallback(  # noqa: E501
    input_name: str,
    detail: str,
    companion_input_name: str,
    diagnostic_case: str,
) -> None:
    """Snapshot fallback requires the canonical diagnostic identity."""
    plan = _plan()
    aggregate_manifest = _authoritative_invalid_planning_input_manifest(
        plan,
        input_name,
        detail,
    )
    summary = _freeze_invalid_planning_input_summary(
        aggregate_manifest,
        plan=plan,
    )
    summary["plan-id"] = plan["plan-id"]
    summary["plan-digest"] = plan["plan-digest"]
    summary["mode"] = plan["mode"]
    summary["validation-tree"] = plan["validation-tree"]
    summary["affected-range"] = _summary_affected_range(plan)
    summary["request"] = plan["request"]
    summary["scheduled-full"] = plan["scheduled-full"]
    companion_artifact = cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            companion_input_name
        ],
    )
    companion_artifact["artifact-ref"] = None
    companion_artifact["artifact-instance-id"] = None
    companion_artifact["content-digest"] = None
    companion_artifact["admissibility"] = "missing"
    companion_artifact["diagnostics"] = [
        _diagnostic(
            "required-input-artifact-failure/snapshot-companion-unproven",
            code="required-input-artifact-failure",
            detail="snapshot-companion-unproven",
            message="Snapshot companion input artifact was not proven.",
            severity="fail-closed",
            verdict_effect="fail-closed",
            source_id=None,
        )
    ]
    if diagnostic_case == "other-id":
        companion_artifact["diagnostics"] = [
            _diagnostic(
                "required-input-artifact-failure/other",
                code="required-input-artifact-failure",
                detail="required-input-artifact-failure",
                message="Snapshot companion input artifact was not proven.",
                severity="fail-closed",
                verdict_effect="fail-closed",
                source_id=None,
            )
        ]
    elif diagnostic_case == "wrong-detail":
        companion_artifact["diagnostics"] = [
            _diagnostic(
                "required-input-artifact-failure/snapshot-companion-unproven",
                code="required-input-artifact-failure",
                detail="required-input-artifact-failure",
                message="Snapshot companion input artifact was not proven.",
                severity="fail-closed",
                verdict_effect="fail-closed",
                source_id=None,
            )
        ]
    elif diagnostic_case == "with-unrelated-diagnostic":
        diagnostics = cast(
            "list[dict[str, object]]",
            companion_artifact["diagnostics"],
        )
        diagnostics.append(
            _diagnostic(
                "required-input-artifact-failure/zz-unrelated",
                code="required-input-artifact-failure",
                detail="required-input-artifact-failure",
                message="A different required input artifact was not proven.",
                severity="fail-closed",
                verdict_effect="fail-closed",
                source_id=None,
            )
        )
    _refresh_summary_manifest_digest(summary, aggregate_manifest)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path == "$.projection-authority"
        and "producer-compatible projection context" in issue.message
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    ("input_name", "detail", "companion_input_name"),
    [
        pytest.param(
            "changed-files-snapshot",
            "changed-files-snapshot-schema-invalid",
            "fact-snapshot",
            id="changed-files-with-fact-companion",
        ),
        pytest.param(
            "fact-snapshot",
            "fact-snapshot-producer-unverified",
            "changed-files-snapshot",
            id="fact-with-changed-files-companion",
        ),
    ],
)
@pytest.mark.parametrize(
    "diagnostic_case",
    [
        pytest.param("other-id", id="other-id"),
        pytest.param("wrong-detail", id="wrong-detail"),
        pytest.param(
            "with-unrelated-diagnostic", id="with-unrelated-diagnostic"
        ),
    ],
)
def test_invalid_plan_rejects_retained_projection_with_noncanonical_inadmissible_snapshot_companion_fallback(  # noqa: E501
    input_name: str,
    detail: str,
    companion_input_name: str,
    diagnostic_case: str,
) -> None:
    """Inadmissible no-binding fallback rejects noncanonical identity."""
    plan = _plan()
    aggregate_manifest = _authoritative_invalid_planning_input_manifest(
        plan,
        input_name,
        detail,
    )
    summary = _freeze_invalid_planning_input_summary(
        aggregate_manifest,
        plan=plan,
    )
    summary["plan-id"] = plan["plan-id"]
    summary["plan-digest"] = plan["plan-digest"]
    summary["mode"] = plan["mode"]
    summary["validation-tree"] = plan["validation-tree"]
    summary["affected-range"] = _summary_affected_range(plan)
    summary["request"] = plan["request"]
    summary["scheduled-full"] = plan["scheduled-full"]
    companion_artifact = cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            companion_input_name
        ],
    )
    companion_artifact["artifact-ref"] = None
    companion_artifact["artifact-instance-id"] = None
    companion_artifact["content-digest"] = None
    companion_artifact["admissibility"] = "inadmissible"
    companion_artifact["diagnostics"] = [
        _diagnostic(
            "required-input-artifact-failure/snapshot-companion-unproven",
            code="required-input-artifact-failure",
            detail="snapshot-companion-unproven",
            message="Snapshot companion input artifact was not proven.",
            severity="fail-closed",
            verdict_effect="fail-closed",
            source_id=None,
        )
    ]
    if diagnostic_case == "other-id":
        companion_artifact["diagnostics"] = [
            _diagnostic(
                "required-input-artifact-failure/other",
                code="required-input-artifact-failure",
                detail="required-input-artifact-failure",
                message="Snapshot companion input artifact was not proven.",
                severity="fail-closed",
                verdict_effect="fail-closed",
                source_id=None,
            )
        ]
    elif diagnostic_case == "wrong-detail":
        companion_artifact["diagnostics"] = [
            _diagnostic(
                "required-input-artifact-failure/snapshot-companion-unproven",
                code="required-input-artifact-failure",
                detail="required-input-artifact-failure",
                message="Snapshot companion input artifact was not proven.",
                severity="fail-closed",
                verdict_effect="fail-closed",
                source_id=None,
            )
        ]
    elif diagnostic_case == "with-unrelated-diagnostic":
        diagnostics = cast(
            "list[dict[str, object]]",
            companion_artifact["diagnostics"],
        )
        diagnostics.append(
            _diagnostic(
                "required-input-artifact-failure/zz-unrelated",
                code="required-input-artifact-failure",
                detail="required-input-artifact-failure",
                message="A different required input artifact was not proven.",
                severity="fail-closed",
                verdict_effect="fail-closed",
                source_id=None,
            )
        )
    _refresh_summary_manifest_digest(summary, aggregate_manifest)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path == "$.projection-authority"
        and "producer-compatible projection context" in issue.message
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    ("input_name", "detail", "companion_input_name"),
    [
        pytest.param(
            "changed-files-snapshot",
            "changed-files-snapshot-schema-invalid",
            "fact-snapshot",
            id="changed-files-with-fact-companion",
        ),
        pytest.param(
            "fact-snapshot",
            "fact-snapshot-producer-unverified",
            "changed-files-snapshot",
            id="fact-with-changed-files-companion",
        ),
    ],
)
@pytest.mark.parametrize(
    "diagnostic_mutation",
    [
        pytest.param("source", id="source"),
        pytest.param("severity", id="severity"),
        pytest.param("verdict-effect", id="verdict-effect"),
        pytest.param("extra-field", id="extra-field"),
    ],
)
def test_invalid_plan_rejects_retained_projection_with_forged_snapshot_companion_fallback(  # noqa: E501
    input_name: str,
    detail: str,
    companion_input_name: str,
    diagnostic_mutation: str,
) -> None:
    """Snapshot companion fallback requires exact canonical diagnostics."""
    plan = _plan()
    aggregate_manifest = _authoritative_invalid_planning_input_manifest(
        plan,
        input_name,
        detail,
    )
    summary = _freeze_invalid_planning_input_summary(
        aggregate_manifest,
        plan=plan,
    )
    summary["plan-id"] = plan["plan-id"]
    summary["plan-digest"] = plan["plan-digest"]
    summary["mode"] = plan["mode"]
    summary["validation-tree"] = plan["validation-tree"]
    summary["affected-range"] = _summary_affected_range(plan)
    summary["request"] = plan["request"]
    summary["scheduled-full"] = plan["scheduled-full"]
    companion_artifact = cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            companion_input_name
        ],
    )
    companion_artifact["artifact-ref"] = None
    companion_artifact["artifact-instance-id"] = None
    companion_artifact["content-digest"] = None
    companion_artifact["admissibility"] = "missing"
    diagnostic = _diagnostic(
        "required-input-artifact-failure/snapshot-companion-unproven",
        code="required-input-artifact-failure",
        detail="snapshot-companion-unproven",
        message="Snapshot companion input artifact was not proven.",
        severity="fail-closed",
        verdict_effect="fail-closed",
        source_id=None,
    )
    if diagnostic_mutation == "source":
        diagnostic["source"] = {
            "type": "aggregation",
            "id": "ci-validation/forged/input.json",
        }
    elif diagnostic_mutation == "severity":
        diagnostic["severity"] = "warning"
    elif diagnostic_mutation == "verdict-effect":
        diagnostic["verdict-effect"] = "none"
    else:
        diagnostic["extra"] = "forged"
    companion_artifact["diagnostics"] = [diagnostic]
    _refresh_summary_manifest_digest(summary, aggregate_manifest)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path == "$.projection-authority"
        and "producer-compatible projection context" in issue.message
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    ("input_name", "detail", "companion_input_name"),
    [
        pytest.param(
            "changed-files-snapshot",
            "changed-files-snapshot-schema-invalid",
            "fact-snapshot",
            id="changed-files-with-fact-companion",
        ),
        pytest.param(
            "fact-snapshot",
            "fact-snapshot-producer-unverified",
            "changed-files-snapshot",
            id="fact-with-changed-files-companion",
        ),
    ],
)
@pytest.mark.parametrize(
    "diagnostic_mutation",
    [
        pytest.param("source", id="source"),
        pytest.param("severity", id="severity"),
        pytest.param("verdict-effect", id="verdict-effect"),
        pytest.param("extra-field", id="extra-field"),
    ],
)
def test_invalid_plan_rejects_retained_projection_with_forged_inadmissible_snapshot_companion_fallback(  # noqa: E501
    input_name: str,
    detail: str,
    companion_input_name: str,
    diagnostic_mutation: str,
) -> None:
    """Inadmissible no-binding fallback rejects forged diagnostics."""
    plan = _plan()
    aggregate_manifest = _authoritative_invalid_planning_input_manifest(
        plan,
        input_name,
        detail,
    )
    summary = _freeze_invalid_planning_input_summary(
        aggregate_manifest,
        plan=plan,
    )
    summary["plan-id"] = plan["plan-id"]
    summary["plan-digest"] = plan["plan-digest"]
    summary["mode"] = plan["mode"]
    summary["validation-tree"] = plan["validation-tree"]
    summary["affected-range"] = _summary_affected_range(plan)
    summary["request"] = plan["request"]
    summary["scheduled-full"] = plan["scheduled-full"]
    companion_artifact = cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            companion_input_name
        ],
    )
    companion_artifact["artifact-ref"] = None
    companion_artifact["artifact-instance-id"] = None
    companion_artifact["content-digest"] = None
    companion_artifact["admissibility"] = "inadmissible"
    diagnostic = _diagnostic(
        "required-input-artifact-failure/snapshot-companion-unproven",
        code="required-input-artifact-failure",
        detail="snapshot-companion-unproven",
        message="Snapshot companion input artifact was not proven.",
        severity="fail-closed",
        verdict_effect="fail-closed",
        source_id=None,
    )
    if diagnostic_mutation == "source":
        diagnostic["source"] = {
            "type": "aggregation",
            "id": "ci-validation/forged/input.json",
        }
    elif diagnostic_mutation == "severity":
        diagnostic["severity"] = "warning"
    elif diagnostic_mutation == "verdict-effect":
        diagnostic["verdict-effect"] = "none"
    else:
        diagnostic["extra"] = "forged"
    companion_artifact["diagnostics"] = [diagnostic]
    _refresh_summary_manifest_digest(summary, aggregate_manifest)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path == "$.projection-authority"
        and "producer-compatible projection context" in issue.message
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    ("input_name", "detail", "companion_input_name"),
    [
        pytest.param(
            "changed-files-snapshot",
            "changed-files-snapshot-schema-invalid",
            "fact-snapshot",
            id="changed-files-with-fact-companion",
        ),
        pytest.param(
            "fact-snapshot",
            "fact-snapshot-producer-unverified",
            "changed-files-snapshot",
            id="fact-with-changed-files-companion",
        ),
    ],
)
@pytest.mark.parametrize(
    "companion_admissibility",
    [
        pytest.param("missing", id="missing"),
        pytest.param("inadmissible", id="inadmissible"),
    ],
)
def test_invalid_plan_accepts_retained_projection_with_snapshot_companion_message_change(  # noqa: E501
    input_name: str,
    detail: str,
    companion_input_name: str,
    companion_admissibility: str,
) -> None:
    """Snapshot companion fallback identity ignores human-readable wording."""
    plan = _plan()
    aggregate_manifest = _authoritative_invalid_planning_input_manifest(
        plan,
        input_name,
        detail,
    )
    summary = _freeze_invalid_planning_input_summary(
        aggregate_manifest,
        plan=plan,
    )
    summary["plan-id"] = plan["plan-id"]
    summary["plan-digest"] = plan["plan-digest"]
    summary["mode"] = plan["mode"]
    summary["validation-tree"] = plan["validation-tree"]
    summary["affected-range"] = _summary_affected_range(plan)
    summary["request"] = plan["request"]
    summary["scheduled-full"] = plan["scheduled-full"]
    companion_artifact = cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            companion_input_name
        ],
    )
    companion_artifact["artifact-ref"] = None
    companion_artifact["artifact-instance-id"] = None
    companion_artifact["content-digest"] = None
    companion_artifact["admissibility"] = companion_admissibility
    companion_artifact["diagnostics"] = [
        _diagnostic(
            "required-input-artifact-failure/snapshot-companion-unproven",
            code="required-input-artifact-failure",
            detail="snapshot-companion-unproven",
            message="Updated snapshot companion fallback wording.",
            severity="fail-closed",
            verdict_effect="fail-closed",
            source_id=None,
        )
    ]
    _refresh_summary_manifest_digest(summary, aggregate_manifest)

    validate_ci_validation_aggregate_summary(
        summary,
        plan=plan,
        aggregate_evidence_manifest=aggregate_manifest,
        request=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )


@pytest.mark.parametrize(
    ("input_name", "detail", "companion_input_name"),
    [
        pytest.param(
            "changed-files-snapshot",
            "changed-files-snapshot-schema-invalid",
            "fact-snapshot",
            id="changed-files-with-fact-companion",
        ),
        pytest.param(
            "fact-snapshot",
            "fact-snapshot-producer-unverified",
            "changed-files-snapshot",
            id="fact-with-changed-files-companion",
        ),
    ],
)
@pytest.mark.parametrize(
    "mutated_field",
    [
        pytest.param("artifact-ref", id="artifact-ref"),
        pytest.param("artifact-instance-id", id="artifact-instance-id"),
        pytest.param("content-digest", id="content-digest"),
    ],
)
def test_invalid_plan_rejects_retained_projection_with_mismatched_snapshot_companion_fallback(  # noqa: E501
    input_name: str,
    detail: str,
    companion_input_name: str,
    mutated_field: str,
) -> None:
    """Snapshot fallback does not bypass retained companion binding."""
    plan = _plan()
    aggregate_manifest = _authoritative_invalid_planning_input_manifest(
        plan,
        input_name,
        detail,
    )
    summary = _freeze_invalid_planning_input_summary(
        aggregate_manifest,
        plan=plan,
    )
    summary["plan-id"] = plan["plan-id"]
    summary["plan-digest"] = plan["plan-digest"]
    summary["mode"] = plan["mode"]
    summary["validation-tree"] = plan["validation-tree"]
    summary["affected-range"] = _summary_affected_range(plan)
    summary["request"] = plan["request"]
    summary["scheduled-full"] = plan["scheduled-full"]
    companion_artifact = cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            companion_input_name
        ],
    )
    companion_artifact["admissibility"] = "inadmissible"
    companion_artifact["diagnostics"] = [
        _diagnostic(
            "required-input-artifact-failure/snapshot-companion-unproven",
            code="required-input-artifact-failure",
            detail="snapshot-companion-unproven",
            message="Snapshot companion input artifact was not proven.",
            severity="fail-closed",
            verdict_effect="fail-closed",
            source_id=cast("str | None", companion_artifact["artifact-ref"]),
        )
    ]
    companion_artifact[mutated_field] = (
        "0" * 64
        if mutated_field == "content-digest"
        else (
            None
            if mutated_field == "artifact-instance-id"
            else "ci-validation/forged/input.json"
        )
    )
    _refresh_summary_manifest_digest(summary, aggregate_manifest)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path == "$.projection-authority"
        and "producer-compatible projection context" in issue.message
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    ("input_name", "detail"),
    [
        (
            "changed-files-snapshot",
            "changed-files-snapshot-schema-invalid",
        ),
        ("fact-snapshot", "fact-snapshot-producer-unverified"),
    ],
    ids=[
        "changed-files-snapshot-changed-files-snapshot-schema-invalid",
        "fact-snapshot-fact-snapshot-producer-unverified",
    ],
)
@pytest.mark.parametrize(
    "mutated_field",
    ["artifact-ref", "artifact-instance-id", "content-digest"],
    ids=["artifact-ref", "artifact-instance-id", "content-digest"],
)
def test_invalid_plan_rejects_retained_snapshot_projection_mismatched_input_artifact(  # noqa: E501
    input_name: str,
    detail: str,
    mutated_field: str,
) -> None:
    """Retained snapshot projection requires exact producer input binding."""
    plan = _plan()
    aggregate_manifest = _authoritative_invalid_planning_input_manifest(
        plan,
        input_name,
        detail,
    )
    summary = _freeze_invalid_planning_input_summary(
        aggregate_manifest,
        plan=plan,
    )
    summary["plan-id"] = plan["plan-id"]
    summary["plan-digest"] = plan["plan-digest"]
    summary["mode"] = plan["mode"]
    summary["validation-tree"] = plan["validation-tree"]
    summary["affected-range"] = _summary_affected_range(plan)
    summary["request"] = plan["request"]
    summary["scheduled-full"] = plan["scheduled-full"]
    input_artifact = cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            input_name
        ],
    )
    input_artifact[mutated_field] = (
        "0" * 64
        if mutated_field == "content-digest"
        else (
            None
            if mutated_field == "artifact-instance-id"
            else "ci-validation/forged/input.json"
        )
    )
    _refresh_summary_manifest_digest(summary, aggregate_manifest)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path == "$.projection-authority"
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    "identity_field", ["plan-id", "plan-digest"], ids=["plan-id", "plan-digest"]
)
def test_invalid_plan_rejects_retained_projection_forged_plan_identity(
    identity_field: str,
) -> None:
    """Retained projection identity must match authoritative plan context."""
    plan = _plan()
    aggregate_manifest = _authoritative_invalid_planning_input_manifest(
        plan,
        "changed-files-snapshot",
        "changed-files-snapshot-schema-invalid",
    )
    summary = _freeze_invalid_planning_input_summary(
        aggregate_manifest,
        plan=plan,
    )
    summary["plan-id"] = plan["plan-id"]
    summary["plan-digest"] = plan["plan-digest"]
    summary["mode"] = plan["mode"]
    summary["validation-tree"] = plan["validation-tree"]
    summary["affected-range"] = _summary_affected_range(plan)
    summary["request"] = plan["request"]
    summary["scheduled-full"] = plan["scheduled-full"]
    summary[identity_field] = (
        "forged-plan-id" if identity_field == "plan-id" else "0" * 64
    )

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path == f"$.{identity_field}"
        and "must match plan" in issue.message
        for issue in exc_info.value.issues
    )


def test_invalid_plan_accepts_fact_snapshot_producer_unverified_projection() -> (  # noqa: E501
    None
):
    """Fact snapshot producer-unverified details retain plan context."""
    plan = _plan()
    aggregate_manifest = _authoritative_invalid_planning_input_manifest(
        plan,
        "fact-snapshot",
        "fact-snapshot-producer-unverified",
    )
    summary = _freeze_invalid_planning_input_summary(
        aggregate_manifest,
        plan=plan,
    )
    summary["plan-id"] = plan["plan-id"]
    summary["plan-digest"] = plan["plan-digest"]
    summary["mode"] = plan["mode"]
    summary["validation-tree"] = plan["validation-tree"]
    summary["affected-range"] = _summary_affected_range(plan)
    summary["request"] = plan["request"]
    summary["scheduled-full"] = plan["scheduled-full"]

    validate_ci_validation_aggregate_summary(
        summary,
        plan=plan,
        aggregate_evidence_manifest=aggregate_manifest,
        request=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )


def test_invalid_plan_rejects_retained_projection_without_manifest_context() -> (  # noqa: E501
    None
):
    """Retained projection requires supplied aggregate manifest authority."""
    plan = _plan()
    aggregate_manifest = _authoritative_invalid_planning_input_manifest(
        plan,
        "fact-snapshot",
        "fact-snapshot-producer-unverified",
    )
    summary = _freeze_invalid_planning_input_summary(
        aggregate_manifest,
        plan=plan,
    )
    summary["plan-id"] = plan["plan-id"]
    summary["plan-digest"] = plan["plan-digest"]
    summary["mode"] = plan["mode"]
    summary["validation-tree"] = plan["validation-tree"]
    summary["affected-range"] = _summary_affected_range(plan)
    summary["request"] = plan["request"]
    summary["scheduled-full"] = plan["scheduled-full"]

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path == "$.projection-authority"
        and "require aggregate manifest input authority" in issue.message
        for issue in exc_info.value.issues
    )


def test_invalid_plan_freezer_rejects_retained_projection_without_manifest_context() -> (  # noqa: E501
    None
):
    """The freezer rejects retained details without manifest authority."""
    plan = _plan()
    aggregate_manifest = _authoritative_invalid_planning_input_manifest(
        plan,
        "fact-snapshot",
        "fact-snapshot-producer-unverified",
    )
    summary = _freeze_invalid_planning_input_summary(
        aggregate_manifest,
        plan=plan,
    )

    with pytest.raises(ContractValidationError) as exc_info:
        freeze_ci_validation_aggregate_summary(
            created_at=CREATED_AT,
            repository_owner="hcoona",
            repository_name="three",
            workflow="CI Validation",
            run_id=RUN_ID,
            run_attempt=RUN_ATTEMPT,
            aggregate_evidence_manifest=cast(
                "dict[str, object]",
                summary["aggregate-evidence-manifest"],
            ),
            final_artifacts=cast(
                "dict[str, object]",
                summary["final-artifacts"],
            ),
            validation_tree=cast("dict[str, object]", plan["validation-tree"]),
            affected_range=_summary_affected_range(plan),
            request=cast("dict[str, object]", plan["request"]),
            scheduled_full=cast("dict[str, object]", plan["scheduled-full"]),
            verdict=cast("str", summary["verdict"]),
            reason=cast("dict[str, object]", summary["reason"]),
            budgets=cast("dict[str, object]", summary["budgets"]),
            diagnostics=cast("list[dict[str, object]]", summary["diagnostics"]),
            batch_bundles=cast(
                "list[dict[str, object]]",
                summary["batch-bundles"],
            ),
            evidence_results=cast(
                "list[dict[str, object]]",
                summary["evidence-results"],
            ),
            failures=cast("list[dict[str, object]]", summary["failures"]),
            work_groups=cast("dict[str, object]", summary["work-groups"]),
            plan=plan,
            request_document=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
            aggregate_evidence_manifest_document=None,
        )

    assert any(
        issue.path == "$.projection-authority"
        and "require aggregate manifest input authority" in issue.message
        for issue in exc_info.value.issues
    )


def test_invalid_plan_retained_projection_validates_aggregate_manifest_authority() -> (  # noqa: E501
    None
):
    """Retained invalid-plan summaries validate manifest input authority."""
    plan = _plan()
    aggregate_manifest = _authoritative_invalid_planning_input_manifest(
        plan,
        "fact-snapshot",
        "fact-snapshot-producer-unverified",
    )
    summary = _freeze_invalid_planning_input_summary(
        aggregate_manifest,
        plan=plan,
    )
    summary["plan-id"] = plan["plan-id"]
    summary["plan-digest"] = plan["plan-digest"]
    summary["mode"] = plan["mode"]
    summary["validation-tree"] = plan["validation-tree"]
    summary["affected-range"] = _summary_affected_range(plan)
    summary["request"] = plan["request"]
    summary["scheduled-full"] = plan["scheduled-full"]
    request_artifact = cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            "request"
        ],
    )
    request_artifact["content-digest"] = "0" * 64

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path.startswith("$.input-artifacts.request")
        or issue.path.startswith("$.aggregate-evidence-manifest")
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    ("failure_kind", "mutation"),
    [
        ("final-evidence-failure", "source"),
        ("final-evidence-failure", "severity"),
        ("final-evidence-failure", "verdict-effect"),
        ("final-producer-unverified", "source"),
        ("final-producer-unverified", "severity"),
        ("final-producer-unverified", "verdict-effect"),
    ],
    ids=[
        "final-evidence-failure-source",
        "final-evidence-failure-severity",
        "final-evidence-failure-verdict-effect",
        "final-producer-unverified-source",
        "final-producer-unverified-severity",
        "final-producer-unverified-verdict-effect",
    ],
)
def test_invalid_plan_rejects_final_failure_diagnostic_semantics(
    failure_kind: str,
    mutation: str,
) -> None:
    """Invalid-plan final failures retain fail-closed diagnostic semantics."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_invalid_plan(summary)
    final_manifest = cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )
    final_manifest["producer-verified"] = False
    final_manifest.setdefault("authority-diagnostics", [])
    if failure_kind == "final-evidence-failure":
        diagnostic = _diagnostic(
            "final-evidence-failure/aggregate-evidence-manifest-unreadable",
            code="final-evidence-failure",
            detail="aggregate-evidence-manifest-unreadable",
            severity="fail-closed",
            verdict_effect="fail-closed",
        )
        cast(
            "list[dict[str, object]]",
            final_manifest["authority-diagnostics"],
        ).append(diagnostic)
        cast("dict[str, object]", summary["reason"])[
            "final-evidence-failure"
        ] = True
    else:
        diagnostic = _diagnostic(
            "final-producer-unverified",
            code="final-producer-unverified",
            detail="final-producer-unverified",
            message=(
                "Aggregate evidence manifest producer boundary was not "
                "verified before summary generation."
            ),
            severity="fail-closed",
            verdict_effect="fail-closed",
        )
        cast("dict[str, object]", summary["reason"])[
            "final-producer-unverified"
        ] = True
    if mutation == "source":
        diagnostic["source"] = {"type": "work-group", "id": "wg-forged"}
    elif mutation == "severity":
        diagnostic["severity"] = "blocking-failure"
    else:
        diagnostic["verdict-effect"] = "failed"
    cast("list[dict[str, object]]", summary["diagnostics"]).append(diagnostic)
    cast("list[dict[str, object]]", summary["failures"]).append(
        {
            "kind": failure_kind,
            "batch-id": None,
            "work-group-id": None,
            "evidence-expectation-id": None,
            "bundle-id": None,
            "diagnostic": diagnostic,
            "message": diagnostic["message"],
        }
    )
    _sort_summary_failures(summary)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            aggregate_evidence_manifest=aggregate_manifest,
        )

    expected_issue_suffix = {
        "source": ".diagnostic.source",
        "severity": ".diagnostic.severity",
        "verdict-effect": ".diagnostic.verdict-effect",
    }[mutation]
    expected_message = (
        "must be aggregate"
        if mutation == "source"
        else "must be fail-closed for fail-closed failures"
    )
    assert any(
        issue.path.startswith("$.failures[")
        and issue.path.endswith(expected_issue_suffix)
        and expected_message in issue.message
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    "mutation",
    [
        "diagnostic-id",
        "code",
        "message",
        "source",
        "severity",
        "verdict-effect",
        "missing-message",
        "null-diagnostic",
    ],
    ids=[
        "diagnostic-id",
        "code",
        "message",
        "source",
        "severity",
        "verdict-effect",
        "missing-message",
        "null-diagnostic",
    ],
)
def test_aggregate_summary_rejects_forged_authority_diagnostic_shape(
    mutation: str,
) -> None:
    """Authority diagnostics must match canonical aggregate evidence shape."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    summary["verdict"] = "failed"
    final_manifest = cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )
    final_manifest["producer-verified"] = False
    canonical_diagnostic = _diagnostic(
        "final-evidence-failure/aggregate-evidence-manifest-unreadable",
        code="final-evidence-failure",
        detail="aggregate-evidence-manifest-unreadable",
        severity="fail-closed",
        verdict_effect="fail-closed",
    )
    forged_diagnostic = dict(canonical_diagnostic)
    if mutation == "diagnostic-id":
        forged_diagnostic["diagnostic-id"] = "authority/forged"
    elif mutation == "code":
        forged_diagnostic["code"] = "blocking-validation-failure"
    elif mutation == "message":
        forged_diagnostic["message"] = "Forged matching detail."
    elif mutation == "source":
        forged_diagnostic["source"] = {"type": "work-group", "id": "wg"}
    elif mutation == "severity":
        forged_diagnostic["severity"] = "blocking-failure"
    elif mutation == "verdict-effect":
        forged_diagnostic["verdict-effect"] = "failed"
    elif mutation == "missing-message":
        del forged_diagnostic["message"]
    else:
        final_manifest["authority-diagnostics"] = [None]
    if mutation != "null-diagnostic":
        final_manifest["authority-diagnostics"] = [forged_diagnostic]
    cast("dict[str, object]", summary["reason"])["final-evidence-failure"] = (
        True
    )
    cast("list[dict[str, object]]", summary["diagnostics"]).append(
        canonical_diagnostic
    )
    cast("list[dict[str, object]]", summary["failures"]).append(
        {
            "kind": "final-evidence-failure",
            "batch-id": None,
            "work-group-id": None,
            "evidence-expectation-id": None,
            "bundle-id": None,
            "diagnostic": canonical_diagnostic,
            "message": canonical_diagnostic["message"],
        }
    )
    _sort_summary_failures(summary)

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
            _aggregate_manifest_authority_failure_details={
                "aggregate-evidence-manifest-unreadable",
            },
        )

    assert any(
        issue.path.startswith(
            "$.final-artifacts.aggregate-evidence-manifest."
            "authority-diagnostics",
        )
        for issue in exc_info.value.issues
    )


def test_invalid_plan_rejects_final_producer_verified_manifest() -> None:
    """Final producer-boundary rows require an unverified final manifest."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_invalid_plan(summary)
    diagnostic = _diagnostic(
        "final-producer-unverified",
        code="final-producer-unverified",
        detail="final-producer-unverified",
        message=(
            "Aggregate evidence manifest producer boundary was not verified "
            "before summary generation."
        ),
        severity="fail-closed",
        verdict_effect="fail-closed",
    )
    cast("list[dict[str, object]]", summary["diagnostics"]).append(diagnostic)
    cast("dict[str, object]", summary["reason"])[
        "final-producer-unverified"
    ] = True
    cast("list[dict[str, object]]", summary["failures"]).append(
        {
            "kind": "final-producer-unverified",
            "batch-id": None,
            "work-group-id": None,
            "evidence-expectation-id": None,
            "bundle-id": None,
            "diagnostic": diagnostic,
            "message": diagnostic["message"],
        }
    )
    final_manifest = cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )
    final_manifest["producer-verified"] = True
    _sort_summary_failures(summary)

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate_summary(summary)


def test_aggregate_summary_accepts_bound_final_producer_unverified() -> None:
    """Bound normal summaries derive final-producer-unverified from manifest."""
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
    summary["verdict"] = "failed"
    cast("dict[str, object]", summary["reason"])[
        "final-producer-unverified"
    ] = True
    cast("dict[str, object]", summary["reason"])["final-evidence-failure"] = (
        True
    )
    diagnostic = _diagnostic(
        "final-producer-unverified",
        code="final-producer-unverified",
        detail="final-producer-unverified",
        message=(
            "Aggregate evidence manifest producer boundary was not verified "
            "before summary generation."
        ),
        severity="fail-closed",
        verdict_effect="fail-closed",
    )
    cast("list[dict[str, object]]", summary["diagnostics"]).append(diagnostic)
    _append_summary_kind_failure(
        summary,
        "final-producer-unverified",
        diagnostic,
        cast("str", diagnostic["message"]),
    )
    final_evidence_diagnostic = _diagnostic(
        "final-evidence-failure/final-producer-unverified",
        code="final-evidence-failure",
        detail="final-producer-unverified",
        message="Aggregate evidence manifest producer was unverified.",
        severity="fail-closed",
        verdict_effect="fail-closed",
    )
    cast("list[dict[str, object]]", summary["diagnostics"]).append(
        final_evidence_diagnostic
    )
    _append_summary_kind_failure(
        summary,
        "final-evidence-failure",
        final_evidence_diagnostic,
        cast("str", final_evidence_diagnostic["message"]),
    )
    cast("list[dict[str, object]]", summary["diagnostics"]).sort(
        key=lambda item: str(item.get("diagnostic-id")),
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
    "failure_kind",
    ["fail-closed", "final-evidence-failure", "final-producer-unverified"],
    ids=["fail-closed", "final-evidence-failure", "final-producer-unverified"],
)
@pytest.mark.parametrize(
    "attribution_key",
    ["batch-id", "work-group-id", "evidence-expectation-id", "bundle-id"],
    ids=["batch-id", "work-group-id", "evidence-expectation-id", "bundle-id"],
)
def test_invalid_plan_rejects_final_failure_attribution(
    failure_kind: str,
    attribution_key: str,
) -> None:
    """Invalid-plan final failure rows must not retain execution attribution."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_invalid_plan(summary)
    if failure_kind == "fail-closed":
        diagnostic = _diagnostic(
            "fail-closed/incomplete",
            code="unknown-change",
            detail="incomplete",
            severity="fail-closed",
            verdict_effect="fail-closed",
        )
        cast("dict[str, object]", summary["reason"])["fail-closed"] = True
    elif failure_kind == "final-evidence-failure":
        diagnostic = _diagnostic(
            "final-evidence-failure/aggregate-evidence-manifest-missing",
            code="final-evidence-failure",
            detail="aggregate-evidence-manifest-missing",
            severity="fail-closed",
            verdict_effect="fail-closed",
        )
        cast("dict[str, object]", summary["reason"])[
            "final-evidence-failure"
        ] = True
    else:
        diagnostic = _diagnostic(
            "final-producer-unverified",
            code="final-producer-unverified",
            detail="final-producer-unverified",
            message=(
                "Aggregate evidence manifest producer boundary was not "
                "verified before summary generation."
            ),
            severity="fail-closed",
            verdict_effect="fail-closed",
        )
        cast("list[dict[str, object]]", summary["diagnostics"]).append(
            diagnostic
        )
        cast("dict[str, object]", summary["reason"])[
            "final-producer-unverified"
        ] = True
    failure = {
        "kind": failure_kind,
        "batch-id": None,
        "work-group-id": None,
        "evidence-expectation-id": None,
        "bundle-id": None,
        "diagnostic": diagnostic,
        "message": diagnostic["message"],
    }
    failure[attribution_key] = f"stale-{attribution_key}"
    cast("list[dict[str, object]]", summary["failures"]).append(failure)
    _sort_summary_failures(summary)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(summary)

    assert any(
        issue.path.endswith(f".{attribution_key}")
        and "must be null for protected fail-closed failure rows"
        in issue.message
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    "failure_kind",
    ["fail-closed", "final-evidence-failure", "final-producer-unverified"],
    ids=[
        "fail-closed",
        "final-evidence-failure",
        "final-producer-unverified",
    ],
)
@pytest.mark.parametrize(
    "attribution_key",
    ["batch-id", "work-group-id", "evidence-expectation-id", "bundle-id"],
    ids=["batch-id", "work-group-id", "evidence-expectation-id", "bundle-id"],
)
def test_aggregate_summary_rejects_non_invalid_protected_attribution(
    failure_kind: str,
    attribution_key: str,
) -> None:
    """Non-invalid final failure rows must not retain execution attribution."""
    if failure_kind == "fail-closed":
        plan, changed_files_snapshot, manifest = _fail_closed_plan_context()
        aggregate_manifest = _fail_closed_aggregate_manifest(
            plan,
            manifest,
            changed_files_snapshot,
        )
        summary = _fail_closed_aggregate_summary(
            plan,
            manifest,
            aggregate_manifest,
            changed_files_snapshot,
        )
        validation_context: _AggregateSummaryValidationKwargs = {
            "plan": plan,
            "aggregate_evidence_manifest": aggregate_manifest,
            "admitted_batch_evidence_bundles": [],
            "execution_batch_manifest": manifest,
            "request": _request_document(),
            "changed_files_snapshot": changed_files_snapshot,
            "fact_snapshot": None,
        }
    else:
        plan = _plan()
        manifest = _manifest(plan)
        bundle = _bundle(plan, manifest)
        aggregate_manifest = _aggregate_evidence_manifest(
            plan, manifest, bundle
        )
        summary = _aggregate_summary(plan, aggregate_manifest, bundle)
        summary["verdict"] = "failed"
        final_manifest = cast(
            "dict[str, object]",
            cast("dict[str, object]", summary["final-artifacts"])[
                "aggregate-evidence-manifest"
            ],
        )
        final_manifest["producer-verified"] = False
        final_manifest.setdefault("authority-diagnostics", [])
        validation_context = {
            "plan": plan,
            "admitted_batch_evidence_bundles": [bundle],
            "execution_batch_manifest": manifest,
            "request": _request_document(),
            "changed_files_snapshot": _changed_files_snapshot_document(),
            "fact_snapshot": _fact_snapshot_document(),
        }
        if failure_kind == "final-evidence-failure":
            diagnostic = _diagnostic(
                "final-evidence-failure/aggregate-evidence-manifest-unreadable",
                code="final-evidence-failure",
                detail="aggregate-evidence-manifest-unreadable",
                severity="fail-closed",
                verdict_effect="fail-closed",
            )
            cast(
                "list[dict[str, object]]",
                final_manifest["authority-diagnostics"],
            ).append(diagnostic)
            cast("dict[str, object]", summary["reason"])[
                "final-evidence-failure"
            ] = True
        else:
            diagnostic = _diagnostic(
                "final-producer-unverified",
                code="final-producer-unverified",
                detail="final-producer-unverified",
                message=(
                    "Aggregate evidence manifest producer boundary was not "
                    "verified before summary generation."
                ),
                severity="fail-closed",
                verdict_effect="fail-closed",
            )
            cast("dict[str, object]", summary["reason"])[
                "final-producer-unverified"
            ] = True
        cast("list[dict[str, object]]", summary["diagnostics"]).append(
            diagnostic
        )
        _append_summary_kind_failure(
            summary,
            failure_kind,
            diagnostic,
            cast("str", diagnostic["message"]),
        )
    failure = next(
        item
        for item in cast("list[dict[str, object]]", summary["failures"])
        if item["kind"] == failure_kind
    )
    failure[attribution_key] = f"stale-{attribution_key}"

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(summary, **validation_context)

    assert any(
        issue.path.endswith(f".{attribution_key}")
        and "must be null for protected fail-closed failure rows"
        in issue.message
        for issue in exc_info.value.issues
    )


@pytest.mark.parametrize(
    "detail",
    [
        pytest.param("plan-duplicate", id="plan-duplicate"),
        pytest.param(
            "plan-producer-unverified",
            id="plan-producer-unverified",
        ),
    ],
)
def test_aggregate_summary_classifies_invalid_validation_plan_input(
    detail: str,
) -> None:
    """Non-authoritative validation-plan input forces invalid-plan summary."""
    plan = _plan()
    aggregate_manifest = _authoritative_invalid_planning_input_manifest(
        plan,
        "validation-plan",
        detail,
    )

    summary = _freeze_invalid_planning_input_summary(
        aggregate_manifest,
        plan=plan,
    )

    reason = cast("dict[str, object]", summary["reason"])
    failures = cast("list[dict[str, object]]", summary["failures"])
    assert reason["invalid-plan"] is True
    assert reason["final-evidence-failure"] is False
    assert [failure["kind"] for failure in failures] == ["invalid-plan"]
    assert cast("dict[str, object]", failures[0]["diagnostic"])["detail"] == (
        detail
    )


def test_aggregate_summary_rejects_mismatched_input_detail() -> None:
    """Manifest-derived invalid-plan detail remains authoritative."""
    plan = _plan()
    aggregate_manifest = _authoritative_invalid_planning_input_manifest(
        plan,
        "validation-plan",
        "plan-duplicate",
    )
    summary = _freeze_invalid_planning_input_summary(
        aggregate_manifest,
        plan=plan,
    )
    replacement = _diagnostic(
        "invalid-plan/plan-producer-unverified",
        code="invalid-plan",
        detail="plan-producer-unverified",
        message=CI_VALIDATION_INVALID_PLAN_NON_AUTHORITATIVE_MESSAGE,
        severity="fail-closed",
        verdict_effect="fail-closed",
        source_id=None,
    )
    cast("list[dict[str, object]]", summary["diagnostics"])[0] = replacement
    failures = cast("list[dict[str, object]]", summary["failures"])
    failures[0]["diagnostic"] = replacement

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path == "$.failures"
        and "canonical invalid-plan failure" in issue.message
        for issue in exc_info.value.issues
    )


def test_plan_duplicate_retains_bound_plan_projection() -> None:
    """Bound plan-duplicate evidence preserves the retained plan projection."""
    plan = _plan()
    aggregate_manifest = _authoritative_invalid_planning_input_manifest(
        plan,
        "validation-plan",
        "plan-duplicate",
    )

    summary = _freeze_invalid_planning_input_summary(
        aggregate_manifest,
        plan=plan,
    )

    assert summary["plan-id"] == plan["plan-id"]
    assert summary["plan-digest"] == plan["plan-digest"]
    assert summary["mode"] == plan["mode"]
    assert summary["validation-tree"] == plan["validation-tree"]
    assert summary["affected-range"] == _summary_affected_range(plan)
    assert summary["request"] == plan["request"]
    assert summary["scheduled-full"] == plan["scheduled-full"]
    validate_ci_validation_aggregate_summary(
        summary,
        plan=plan,
        aggregate_evidence_manifest=aggregate_manifest,
        request=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )


@pytest.mark.parametrize(
    "authority_case",
    [
        pytest.param("null", id="null"),
        pytest.param("missing", id="missing"),
        pytest.param("incomplete-payload", id="incomplete-payload"),
        pytest.param("digest-mismatch", id="digest-mismatch"),
        pytest.param("mismatched-field", id="mismatched-field"),
    ],
)
def test_retained_invalid_plan_rejects_missing_projection_authority(
    authority_case: str,
) -> None:
    """Retained invalid-plan evidence requires retained projection authority."""
    plan = _plan()
    aggregate_manifest = _authoritative_invalid_planning_input_manifest(
        plan,
        "validation-plan",
        "plan-duplicate",
    )
    if authority_case == "null":
        aggregate_manifest["projection-authority"] = None
    elif authority_case == "missing":
        aggregate_manifest.pop("projection-authority")
    else:
        authority = dict(
            cast(
                "dict[str, object]",
                aggregate_manifest["projection-authority"],
            )
        )
        if authority_case == "incomplete-payload":
            authority.pop("request")
        elif authority_case == "digest-mismatch":
            authority["projection-digest"] = "0" * 64
        elif authority_case == "mismatched-field":
            authority["mode"] = "push"
            _refresh_projection_authority_digest(authority)
        else:
            raise AssertionError(authority_case)
        aggregate_manifest["projection-authority"] = authority

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path == "$.projection-authority"
        and (
            "retained invalid-plan details require" in issue.message
            or "must match plan projection authority" in issue.message
            or "must match canonical digest" in issue.message
        )
        for issue in exc_info.value.issues
    )


def test_invalid_plan_summary_derives_standalone_detail() -> None:
    """Standalone validation derives canonical invalid-plan detail."""
    aggregate_manifest = _invalid_planning_input_manifest(
        "validation-plan",
        "plan-unreadable",
    )
    summary = _freeze_invalid_planning_input_summary(aggregate_manifest)

    validate_ci_validation_aggregate_summary(
        summary,
        aggregate_evidence_manifest=aggregate_manifest,
    )


def test_invalid_plan_summary_enforces_context_detail() -> None:
    """Manifest context fixes the expected canonical invalid-plan detail."""
    aggregate_manifest = _invalid_planning_input_manifest(
        "validation-plan",
        "malformed-plan",
    )
    summary = _freeze_invalid_planning_input_summary(aggregate_manifest)
    failures = cast("list[dict[str, object]]", summary["failures"])
    failure = failures[0]
    diagnostic = cast("dict[str, object]", failure["diagnostic"])
    diagnostic["diagnostic-id"] = "invalid-plan/plan-unreadable"
    diagnostic["detail"] = "plan-unreadable"

    with pytest.raises(ContractValidationError, match="invalid-plan"):
        validate_ci_validation_aggregate_summary(
            summary,
            aggregate_evidence_manifest=aggregate_manifest,
        )


@pytest.mark.parametrize(
    ("input_name", "detail"),
    [
        ("changed-files-snapshot", "changed-files-snapshot-digest-mismatch"),
        ("fact-snapshot", "fact-snapshot-producer-unverified"),
    ],
    ids=[
        "changed-files-snapshot-changed-files-snapshot-digest-mismatch",
        "fact-snapshot-fact-snapshot-producer-unverified",
    ],
)
def test_aggregate_summary_classifies_invalid_companion_snapshot_input(
    input_name: str,
    detail: str,
) -> None:
    """Non-authoritative companion snapshots force invalid-plan summary."""
    plan = _plan()
    aggregate_manifest = _authoritative_invalid_planning_input_manifest(
        plan,
        input_name,
        detail,
    )

    summary = _freeze_invalid_planning_input_summary(
        aggregate_manifest,
        plan=plan,
    )

    reason = cast("dict[str, object]", summary["reason"])
    failures = cast("list[dict[str, object]]", summary["failures"])
    assert reason["invalid-plan"] is True
    assert reason["final-evidence-failure"] is False
    assert [failure["kind"] for failure in failures] == ["invalid-plan"]
    assert cast("dict[str, object]", failures[0]["diagnostic"])["detail"] == (
        detail
    )


def test_aggregate_summary_rejects_forged_invalid_plan_input_diagnostic() -> (
    None
):
    """Input invalid-plan diagnostics must be canonical and input-bound."""
    plan = _plan()
    aggregate_manifest = _authoritative_invalid_planning_input_manifest(
        plan,
        "fact-snapshot",
        "fact-snapshot-schema-invalid",
    )
    summary = _freeze_invalid_planning_input_summary(
        aggregate_manifest,
        plan=plan,
    )
    artifact = cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            "fact-snapshot"
        ],
    )
    diagnostic = cast(
        "dict[str, object]",
        cast("list[dict[str, object]]", artifact["diagnostics"])[0],
    )
    diagnostic["source"] = {"type": "work-group", "id": "wg-forged"}
    _refresh_summary_manifest_digest(summary, aggregate_manifest)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path == "$.input-artifacts.fact-snapshot.diagnostics[0]"
        and "canonical bound invalid-plan input diagnostic" in issue.message
        for issue in exc_info.value.issues
    )


def test_invalid_plan_input_diagnostic_message_edit_accepted() -> None:
    """Input invalid-plan diagnostic identity excludes display message text."""
    plan = _plan()
    aggregate_manifest = _authoritative_invalid_planning_input_manifest(
        plan,
        "fact-snapshot",
        "fact-snapshot-schema-invalid",
    )
    summary = _freeze_invalid_planning_input_summary(
        aggregate_manifest,
        plan=plan,
    )
    artifact = cast(
        "dict[str, object]",
        cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
            "fact-snapshot"
        ],
    )
    diagnostic = cast(
        "dict[str, object]",
        cast("list[dict[str, object]]", artifact["diagnostics"])[0],
    )
    diagnostic["message"] = "Human-readable invalid-plan text may vary."
    _refresh_summary_manifest_digest(summary, aggregate_manifest)

    validate_ci_validation_aggregate_summary(
        summary,
        plan=plan,
        aggregate_evidence_manifest=aggregate_manifest,
        request=_request_document(),
        changed_files_snapshot=_changed_files_snapshot_document(),
        fact_snapshot=_fact_snapshot_document(),
    )


@pytest.mark.parametrize(
    "detail",
    ["required-input-artifact-failure", "aggregate-duration-exceeded"],
)
def test_invalid_plan_summary_rejects_unrelated_authority_details(
    detail: str,
) -> None:
    """Unrelated authority diagnostics cannot authorize failures."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_invalid_plan(summary)
    final_manifest = cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )
    final_manifest["authority-diagnostics"] = [
        _diagnostic(
            f"authority/{detail}",
            code="final-evidence-failure",
            detail=detail,
            severity="fail-closed",
            verdict_effect="fail-closed",
        )
    ]
    reason = cast("dict[str, object]", summary["reason"])
    reason["fail-closed"] = True
    reason["final-evidence-failure"] = True
    _append_summary_fail_closed_failure(
        summary,
        _diagnostic(
            f"fail-closed/{detail}",
            code="final-evidence-failure",
            detail=detail,
            severity="fail-closed",
            verdict_effect="fail-closed",
        ),
        "Unrelated authority detail forced fail-closed.",
    )
    cast("list[dict[str, object]]", summary["failures"]).append(
        {
            "kind": "final-evidence-failure",
            "batch-id": None,
            "work-group-id": None,
            "evidence-expectation-id": None,
            "bundle-id": None,
            "diagnostic": _diagnostic(
                f"final-evidence-failure/{detail}",
                code="final-evidence-failure",
                detail=detail,
                severity="fail-closed",
                verdict_effect="fail-closed",
            ),
            "message": "Unrelated authority detail claimed final failure.",
        }
    )
    _sort_summary_failures(summary)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(summary)

    assert any(
        issue.path == "$.final-artifacts.aggregate-evidence-manifest."
        "authority-diagnostics[0].detail"
        for issue in exc_info.value.issues
    )
    assert any(
        issue.path == "$.failures"
        and "final evidence failure causes" in issue.message
        for issue in exc_info.value.issues
    )


def test_invalid_plan_summary_rejects_forged_final_failure() -> None:
    """Invalid-plan final rows cannot be authorized by summary failures."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_invalid_plan(summary)
    reason = cast("dict[str, object]", summary["reason"])
    reason["final-evidence-failure"] = True
    cast("list[dict[str, object]]", summary["failures"]).append(
        {
            "kind": "final-evidence-failure",
            "batch-id": None,
            "work-group-id": None,
            "evidence-expectation-id": None,
            "bundle-id": None,
            "diagnostic": _diagnostic(
                "forged-final-failure",
                code="final-evidence-failure",
                detail="aggregate-summary-without-manifest",
                severity="fail-closed",
                verdict_effect="fail-closed",
            ),
            "message": "Forged invalid-plan final evidence failure.",
        }
    )
    _sort_summary_failures(summary)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(summary)

    assert any(
        issue.path == "$.failures"
        and "final evidence failure causes" in issue.message
        for issue in exc_info.value.issues
    )


def test_contextual_summary_rejects_self_authorized_final_failure() -> None:
    """Supplied manifest context, not summary payload, authorizes final rows."""
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
    final_manifest["authority-diagnostics"] = [
        _diagnostic(
            "self-authorized/aggregate-evidence-manifest-malformed",
            code="final-evidence-failure",
            detail="aggregate-evidence-manifest-malformed",
            severity="fail-closed",
            verdict_effect="fail-closed",
        )
    ]
    summary["verdict"] = "failed"
    reason = cast("dict[str, object]", summary["reason"])
    reason["final-evidence-failure"] = True
    cast("list[dict[str, object]]", summary["failures"]).append(
        {
            "kind": "final-evidence-failure",
            "batch-id": None,
            "work-group-id": None,
            "evidence-expectation-id": None,
            "bundle-id": None,
            "diagnostic": _diagnostic(
                "final-evidence-failure/aggregate-evidence-manifest-malformed",
                code="final-evidence-failure",
                detail="aggregate-evidence-manifest-malformed",
                severity="fail-closed",
                verdict_effect="fail-closed",
            ),
            "message": "Self-authorized aggregate evidence manifest failure.",
        }
    )
    _sort_summary_failures(summary)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            **_authorizing_context_kwargs(),
        )

    assert any(
        issue.path == "$.final-artifacts.aggregate-evidence-manifest."
        "authority-diagnostics"
        for issue in exc_info.value.issues
    )
    assert any(
        issue.path == "$.failures"
        and "final evidence failure causes" in issue.message
        for issue in exc_info.value.issues
    )


def test_contextual_summary_rejects_unrelated_manifest_authority_failure() -> (
    None
):
    """External manifest authority must correspond to accepted final details."""
    plan = _plan()
    aggregate_manifest = _authoritative_invalid_planning_input_manifest(
        plan,
        "fact-snapshot",
        "fact-snapshot-producer-unverified",
    )
    summary = _freeze_invalid_planning_input_summary(
        aggregate_manifest,
        plan=plan,
    )
    forged_digest = "0123456789abcdef" * 4
    manifest_claim = cast(
        "dict[str, object]", summary["aggregate-evidence-manifest"]
    )
    final_manifest = cast(
        "dict[str, object]",
        cast("dict[str, object]", summary["final-artifacts"])[
            "aggregate-evidence-manifest"
        ],
    )
    for claim in (manifest_claim, final_manifest):
        claim["artifact-instance-id"] = "forged-aggregate-manifest"
        claim["content-digest"] = forged_digest
    final_manifest["producer-verified"] = False
    authority_diagnostic = _diagnostic(
        "final-evidence-failure/aggregate-evidence-manifest-malformed",
        code="final-evidence-failure",
        detail="aggregate-evidence-manifest-malformed",
        message="Preserved aggregate evidence manifest was malformed.",
        severity="fail-closed",
        verdict_effect="fail-closed",
    )
    final_manifest["authority-diagnostics"] = [authority_diagnostic]
    reason = cast("dict[str, object]", summary["reason"])
    reason["final-evidence-failure"] = True
    reason["final-producer-unverified"] = True
    failures = cast("list[dict[str, object]]", summary["failures"])
    failures.extend(
        [
            {
                "kind": "final-evidence-failure",
                "batch-id": None,
                "work-group-id": None,
                "evidence-expectation-id": None,
                "bundle-id": None,
                "diagnostic": authority_diagnostic,
                "message": authority_diagnostic["message"],
            },
            {
                "kind": "final-producer-unverified",
                "batch-id": None,
                "work-group-id": None,
                "evidence-expectation-id": None,
                "bundle-id": None,
                "diagnostic": _diagnostic(
                    "final-producer-unverified",
                    code="final-producer-unverified",
                    detail="final-producer-unverified",
                    severity="fail-closed",
                    verdict_effect="fail-closed",
                ),
                "message": (
                    "Aggregate evidence manifest producer boundary was not "
                    "verified before summary generation."
                ),
            },
            {
                "kind": "final-evidence-failure",
                "batch-id": None,
                "work-group-id": None,
                "evidence-expectation-id": None,
                "bundle-id": None,
                "diagnostic": _diagnostic(
                    "final-evidence-failure/final-producer-unverified",
                    code="final-evidence-failure",
                    detail="final-producer-unverified",
                    severity="fail-closed",
                    verdict_effect="fail-closed",
                ),
                "message": (
                    "Aggregate evidence manifest producer was unverified."
                ),
            },
        ]
    )
    _sort_summary_failures(summary)

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    assert any(
        issue.path
        in {
            "$.aggregate-evidence-manifest.content-digest",
            "$.final-artifacts.aggregate-evidence-manifest.content-digest",
            "$.final-artifacts.aggregate-evidence-manifest."
            "authority-diagnostics",
        }
        for issue in exc_info.value.issues
    )


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


def test_aggregate_manifest_rejects_supplied_plan_missing_input_row() -> None:
    """Missing plan rows use no-authority projection diagnostics."""
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

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
        )

    assert any(
        issue.path in {"$.plan-id", "$.plan-digest"}
        and "without an authoritative plan" in issue.message
        for issue in exc_info.value.issues
    )
    assert any(
        issue.path == "$.projection-authority"
        and "without an authoritative plan" in issue.message
        for issue in exc_info.value.issues
    )


def test_aggregate_manifest_rejects_supplied_plan_inadmissible_input_row() -> (
    None
):
    """Inadmissible present plan rows cannot authorize supplied projection."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
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
                "physical-artifact-name": (
                    f"three-ci-validation-{RUN_ID}-{RUN_ATTEMPT}-" + "9" * 64
                ),
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
            "physical-artifact-name": (
                f"three-ci-validation-{RUN_ID}-{RUN_ATTEMPT}-" + "9" * 64
            ),
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
        execution_manifest_context = (
            None if input_name == "execution-batch-manifest" else manifest
        )
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            execution_batch_manifest=execution_manifest_context,
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
    """Invalid planning inputs close authority before request checks."""
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
        issue.path == "$.projection-authority"
        and "without an authoritative plan" in issue.message
        for issue in exc_info.value.issues
    )
    assert all(
        issue.path not in {"$.plan-id", "$.plan-digest"}
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
    """Invalid planning inputs cannot retain plan/projection authority."""
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
        issue.path == "$.projection-authority"
        and "without an authoritative plan" in issue.message
        for issue in exc_info.value.issues
    )
    assert all(
        issue.path not in {"$.plan-id", "$.plan-digest"}
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
        "validation-tree" in issue.path for issue in exc_info.value.issues
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


def test_summary_accepts_manifest_projection_authority_when_plan_unbound() -> (
    None
):
    """Manifest authority may authorize a matching summary projection."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    issues: list[ValidationIssue] = []

    assert _validate_summary_manifest_projection_authority(
        summary,
        aggregate_manifest,
        issues,
    )
    assert issues == []


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
        pytest.param(
            "content-digest",
            "0" * 64,
            id=(
                "content-digest-"
                "0000000000000000000000000000000000000000000000000000000000000000"
            ),
        ),
        pytest.param(
            "artifact-ref",
            "ci-validation/execution-batches/999/1/execution-batch-manifest.json",
            id=(
                "artifact-ref-ci-validation/execution-batches/999/1/"
                "execution-batch-manifest.json"
            ),
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
        pytest.param(
            "expected-job-identity",
            "github-actions-job:" + "9" * 64,
            id="expected-job-identity-mismatch",
        ),
        pytest.param(
            "identity-source",
            "forged-source",
            id="identity-source-mismatch",
        ),
        pytest.param(
            "expected-boundary",
            "forged-boundary",
            id="expected-boundary-mismatch",
        ),
        pytest.param(
            "observed-workflow",
            "Forged Workflow",
            id="observed-workflow-mismatch",
        ),
        pytest.param(
            "observed-job",
            "forged-job",
            id="observed-job-mismatch",
        ),
        pytest.param(
            "observed-matrix",
            {"batch-id": "batch-forged"},
            id="observed-matrix-mismatch",
        ),
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
    summary["plan-id"] = None
    summary["plan-digest"] = None
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
    reason["aggregate-summary-without-manifest"] = True
    reason["final-producer-unverified"] = False
    reason["final-evidence-failure"] = False
    _append_summary_kind_failure(
        summary,
        "aggregate-summary-without-manifest",
        _diagnostic(
            "aggregate-summary-without-manifest",
            code="aggregate-summary-without-manifest",
            detail="aggregate-summary-without-manifest",
            severity="fail-closed",
            verdict_effect="fail-closed",
        ),
        "Missing aggregate evidence manifest forced fail-closed.",
    )
    _sort_summary_failures(summary)


def test_aggregate_summary_accepts_missing_manifest_failure_detail() -> None:
    """Summary-only missing-manifest validation requires unbound identity."""
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


def test_aggregate_summary_rejects_missing_manifest_supplied_identity() -> None:
    """Supplied plan context cannot authorize summary identity by itself."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_missing_aggregate_manifest(summary)
    summary["plan-id"] = plan["plan-id"]
    summary["plan-digest"] = plan["plan-digest"]

    with pytest.raises(ContractValidationError) as exc_info:
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            execution_batch_manifest=manifest,
            request=_request_document(),
            changed_files_snapshot=_changed_files_snapshot_document(),
            fact_snapshot=_fact_snapshot_document(),
        )

    issue_pairs = {
        (issue.path, issue.message) for issue in exc_info.value.issues
    }
    assert (
        "$.plan-id",
        "must be null without an authoritative plan",
    ) in issue_pairs
    assert (
        "$.plan-digest",
        "must be null without an authoritative plan",
    ) in issue_pairs


def test_aggregate_summary_accepts_missing_manifest_unverified_marker() -> None:
    """Missing-manifest markers do not self-authorize producer failure."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_missing_aggregate_manifest(summary)
    cast("dict[str, object]", summary["reason"])[
        "final-producer-unverified"
    ] = False
    _remove_summary_failure_kind(summary, "final-producer-unverified")
    summary["diagnostics"] = _without_final_producer_diagnostics(summary)

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
        issue.path
        in {
            "$.verdict",
            "$.reason.final-evidence-failure",
            "$.final-artifacts.aggregate-evidence-manifest.producer-verified",
        }
        for issue in exc_info.value.issues
    )


def test_aggregate_summary_requires_missing_manifest_failure_row() -> None:
    """Missing-manifest reasons require an explicit failure row."""
    plan = _plan()
    manifest = _manifest(plan)
    bundle = _bundle(plan, manifest)
    aggregate_manifest = _aggregate_evidence_manifest(plan, manifest, bundle)
    summary = _aggregate_summary(plan, aggregate_manifest, bundle)
    _mark_summary_missing_aggregate_manifest(summary)
    _remove_summary_failure_kind(summary, "aggregate-summary-without-manifest")

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
        issue.path == "$.failures"
        and "aggregate-summary-without-manifest" in issue.message
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
        pytest.param(
            "artifact-instance-id",
            None,
            None,
            id="summary-artifact-instance-id-claim",
        ),
        pytest.param(
            "content-digest",
            None,
            None,
            id="summary-content-digest-claim",
        ),
        pytest.param(
            None,
            "artifact-instance-id",
            "3001",
            id="final-artifact-instance-id-claim",
        ),
        pytest.param(
            None,
            "content-digest",
            "0" * 64,
            id="final-content-digest-claim",
        ),
        pytest.param(
            None,
            "producer-verified",
            True,
            id="final-producer-verified-claim",
        ),
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
    """Summary-only rows cannot forge missing-manifest batch failures."""
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
        issue.path == "$.failures" and "batch-forged-gate" in issue.message
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


def test_aggregate_summary_rejects_duration_overrun_reason() -> None:
    """Duration overflow must not be reported as a correctness reason."""
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
    summary["verdict"] = "failed"

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
