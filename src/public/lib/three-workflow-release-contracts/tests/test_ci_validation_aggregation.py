"""Receipt manifest and aggregate contract helper tests."""

from __future__ import annotations

import importlib.util
from copy import deepcopy
from pathlib import Path
from types import ModuleType
from typing import Any, cast

import pytest
from three_workflow_release_contracts import (
    ContractValidationError,
    DiagnosticDetail,
    DiagnosticFamily,
    DiagnosticSeverity,
    DiagnosticVerdictEffect,
    artifact_physical_name,
    canonical_json_bytes,
    ci_validation_aggregate_payload_digest,
    ci_validation_diagnostic,
    ci_validation_observed_entry_id,
    ci_validation_receipt_artifact_ref,
    ci_validation_receipt_content_digest,
    ci_validation_receipt_manifest_payload_digest,
    ci_validation_writer_id,
    ci_validation_writer_observation_artifact_ref,
    freeze_ci_validation_aggregate,
    freeze_ci_validation_invalid_plan_aggregate,
    freeze_ci_validation_receipt_manifest,
    validate_ci_validation_aggregate,
    validate_ci_validation_receipt_manifest,
)

_RECEIPTS_TEST_PATH = Path(__file__).with_name("test_ci_validation_receipts.py")
_RECEIPTS_SPEC = importlib.util.spec_from_file_location(
    "test_ci_validation_receipts",
    _RECEIPTS_TEST_PATH,
)
assert _RECEIPTS_SPEC is not None
assert _RECEIPTS_SPEC.loader is not None
_RECEIPTS_MODULE = importlib.util.module_from_spec(_RECEIPTS_SPEC)
assert isinstance(_RECEIPTS_MODULE, ModuleType)
_RECEIPTS_SPEC.loader.exec_module(_RECEIPTS_MODULE)

_CREATED_AT_NAME = "CREATED_AT"
_RUN_ATTEMPT_NAME = "RUN_ATTEMPT"
_RUN_ID_NAME = "RUN_ID"
_WORK_GROUP_ID_NAME = "WORK_GROUP_ID"
_FAILED_DIAGNOSTIC_NAME = "_failed_diagnostic"
_SKIPPED_DIAGNOSTIC_NAME = "_skipped_diagnostic"
_VALID_RECEIPT_NAME = "_valid_receipt"

CREATED_AT = cast("str", getattr(_RECEIPTS_MODULE, _CREATED_AT_NAME))
RUN_ATTEMPT = cast("str", getattr(_RECEIPTS_MODULE, _RUN_ATTEMPT_NAME))
RUN_ID = cast("str", getattr(_RECEIPTS_MODULE, _RUN_ID_NAME))
WORK_GROUP_ID = cast("str", getattr(_RECEIPTS_MODULE, _WORK_GROUP_ID_NAME))
_failed_diagnostic = getattr(_RECEIPTS_MODULE, _FAILED_DIAGNOSTIC_NAME)
_skipped_diagnostic = getattr(_RECEIPTS_MODULE, _SKIPPED_DIAGNOSTIC_NAME)
_valid_receipt = getattr(_RECEIPTS_MODULE, _VALID_RECEIPT_NAME)
_artifact_shape_diagnostic = _RECEIPTS_MODULE.__dict__[
    "_artifact_shape_diagnostic"
]
_descriptor_invalid_diagnostic = _RECEIPTS_MODULE.__dict__[
    "_descriptor_invalid_diagnostic"
]
_specialized_context = _RECEIPTS_MODULE.__dict__["_specialized_context"]
_descriptor_work_group = _RECEIPTS_MODULE.__dict__["_descriptor_work_group"]
_descriptor_evidence_expectation = _RECEIPTS_MODULE.__dict__[
    "_descriptor_evidence_expectation"
]
_descriptor_obligation = _RECEIPTS_MODULE.__dict__["_descriptor_obligation"]
_descriptor_receipt_evidence = _RECEIPTS_MODULE.__dict__[
    "_descriptor_receipt_evidence"
]
_artifact_work_group = _RECEIPTS_MODULE.__dict__["_artifact_work_group"]
_artifact_evidence_expectation = _RECEIPTS_MODULE.__dict__[
    "_artifact_evidence_expectation"
]
_artifact_validation_obligation = _RECEIPTS_MODULE.__dict__[
    "_artifact_validation_obligation"
]
_artifact_obligation = _RECEIPTS_MODULE.__dict__["_artifact_obligation"]
_release_receipt_evidence = _RECEIPTS_MODULE.__dict__[
    "_release_receipt_evidence"
]
_receipt_for_context = _RECEIPTS_MODULE.__dict__["_receipt_for_context"]
_release_result = _RECEIPTS_MODULE.__dict__["_release_result"]
DESCRIPTOR_WORK_GROUP_ID = cast(
    "str", _RECEIPTS_MODULE.__dict__["DESCRIPTOR_WORK_GROUP_ID"]
)
ARTIFACT_WORK_GROUP_ID = cast(
    "str", _RECEIPTS_MODULE.__dict__["ARTIFACT_WORK_GROUP_ID"]
)
_REF_SENTINEL = object()


def _writer_id(work_group_id: str) -> str:
    return ci_validation_writer_id(
        workflow="CI Validation",
        job="ci-validation-selector-python",
        matrix={"selector": work_group_id},
    )


def _writer_observation_ref(work_group_id: str) -> str:
    return ci_validation_writer_observation_artifact_ref(
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        assignment_id=work_group_id,
    )


def _entry(
    receipt: dict[str, object] | None,
    *,
    artifact_ref: str | None | object = _REF_SENTINEL,
    instance_id: str = "1001",
    work_group_id: str | None = WORK_GROUP_ID,
    receipt_id: str | None = "receipt-001",
) -> dict[str, object]:
    ref = (
        cast("str | None", artifact_ref)
        if artifact_ref is not _REF_SENTINEL
        else (
            cast("str", receipt["artifact-ref"])
            if receipt is not None
            else None
        )
    )
    entry_id = ci_validation_observed_entry_id(
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        artifact_ref=ref,
        artifact_instance_id=instance_id,
    )
    trusted_writer_id = _writer_id(work_group_id) if work_group_id else None
    return {
        "observed-entry-id": entry_id,
        "artifact-ref": ref,
        "physical-artifact-name": artifact_physical_name(ref)
        if ref is not None
        else artifact_physical_name(
            f"ci-validation/receipts/{RUN_ID}/{RUN_ATTEMPT}/unknown/receipt.json"
        ),
        "artifact-instance-id": instance_id,
        "assignment-id": work_group_id,
        "writer-work-group-id": work_group_id,
        "trusted-writer-id": trusted_writer_id,
        "observed-writer-id": trusted_writer_id,
        "writer-observation-ref": _writer_observation_ref(work_group_id)
        if work_group_id
        else None,
        "receipt-id": receipt_id,
        "receipt-content-digest": ci_validation_receipt_content_digest(
            canonical_json_bytes(receipt)
        )
        if receipt is not None
        else "0" * 64,
    }


def _entry_for_assignment(
    receipt: dict[str, object], assignment: dict[str, object]
) -> dict[str, object]:
    artifact_ref = cast("str", assignment["receipt-artifact-ref"])
    instance_id = "1001"
    entry_id = ci_validation_observed_entry_id(
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        artifact_ref=artifact_ref,
        artifact_instance_id=instance_id,
    )
    return {
        "observed-entry-id": entry_id,
        "artifact-ref": artifact_ref,
        "physical-artifact-name": artifact_physical_name(artifact_ref),
        "artifact-instance-id": instance_id,
        "assignment-id": assignment["work-group-id"],
        "writer-work-group-id": assignment["work-group-id"],
        "trusted-writer-id": assignment["trusted-writer-id"],
        "observed-writer-id": assignment["trusted-writer-id"],
        "writer-observation-ref": assignment["writer-observation-ref"],
        "receipt-id": receipt["receipt-id"],
        "receipt-content-digest": ci_validation_receipt_content_digest(
            canonical_json_bytes(receipt)
        ),
    }


def _observed_input(
    entry: dict[str, object],
    receipt: dict[str, object] | None,
) -> dict[str, object]:
    raw = canonical_json_bytes(receipt) if receipt is not None else None
    result: dict[str, object] = {"manifest-entry": entry, "receipt": receipt}
    if raw is not None:
        result["raw-receipt-bytes"] = raw
    return result


def _release_observed_input_with_no_publish_result(
    *,
    extra_commands: list[object] | None = None,
) -> tuple[Any, dict[str, object], dict[str, object], dict[str, object]]:
    snapshot, selector_manifest, assignment = _specialized_context(
        group=_artifact_work_group(),
        evidence_expectation=_artifact_evidence_expectation(),
        validation_obligations=[_artifact_validation_obligation()],
        artifact_obligations=[_artifact_obligation()],
    )
    receipt = _receipt_for_context(
        snapshot, selector_manifest, assignment, _release_receipt_evidence()
    )
    evidence = cast("dict[str, object]", receipt["evidence"])
    category = cast("dict[str, object]", evidence["category-result"])
    detail = cast("dict[str, object]", category["detail"])
    artifact_results = cast(
        "list[dict[str, object]]", detail["artifact-obligation-results"]
    )
    proof_digests: list[dict[str, object]] = []
    for result in artifact_results:
        artifact = cast("dict[str, object]", result["artifact"])
        observed = cast("dict[str, object]", artifact["observed"])
        for digest in cast("list[dict[str, object]]", observed["digests"]):
            proof_digests.append(
                {
                    "artifact-ref": digest["artifact-ref"],
                    "algorithm": digest["algorithm"],
                    "digest": digest["digest"],
                }
            )
    source_proof = {
        "kind": "no-publish-validation-result",
        "work-group-id": receipt["work-group-id"],
        "coverage-target": receipt["coverage-target"],
        "observed-commit-sha": cast(
            "dict[str, object]", receipt["execution-tree"]
        )["observed-commit-sha"],
        "artifact-digests": sorted(
            proof_digests, key=lambda item: str(item["artifact-ref"])
        ),
    }
    detail["evidence-source"] = "no-publish-validation"
    detail["source-proof"] = source_proof
    source_command = {
        "outcome": "success",
        "evidence-source": "no-publish-validation",
        "source-proof": source_proof,
        "artifact-obligation-results": artifact_results,
    }
    validation_result: dict[str, object] = {
        "outcome": "success",
        "work-group-id": receipt["work-group-id"],
        "kind": "release-shaped-artifact",
        "coverage-target": receipt["coverage-target"],
        "observed-commit-sha": cast(
            "dict[str, object]", receipt["execution-tree"]
        )["observed-commit-sha"],
        "commands": [source_command, *(extra_commands or [])],
    }
    entry = _entry_for_assignment(receipt, assignment)
    observed_input = _observed_input(entry, receipt)
    observed_input["validation-result"] = validation_result
    return snapshot, selector_manifest, entry, observed_input


def _release_reused_receipt_chain_inputs() -> tuple[
    Any,
    dict[str, object],
    dict[str, object],
    dict[str, object],
    dict[str, object],
    dict[str, object],
]:
    snapshot, selector_manifest, prior_entry, prior_input = (
        _release_observed_input_with_no_publish_result()
    )
    prior_receipt = cast("dict[str, object]", prior_input["receipt"])
    current_receipt = deepcopy(prior_receipt)
    current_receipt["receipt-id"] = "receipt-reused-001"
    detail = cast(
        "dict[str, object]",
        cast(
            "dict[str, object]",
            cast("dict[str, object]", current_receipt["evidence"])[
                "category-result"
            ],
        )["detail"],
    )
    detail["evidence-source"] = "reused-validation-receipt"
    detail.pop("source-proof", None)
    detail["reused-receipt"] = {
        "artifact-ref": prior_entry["artifact-ref"],
        "receipt-id": prior_entry["receipt-id"],
        "receipt-content-digest": prior_entry["receipt-content-digest"],
        "observed-commit-sha": cast(
            "dict[str, object]", prior_receipt["execution-tree"]
        )["observed-commit-sha"],
    }
    current_entry = _entry_for_assignment(
        current_receipt,
        cast(
            "dict[str, object]",
            cast("list[dict[str, object]]", selector_manifest["assignments"])[
                0
            ],
        ),
    )
    current_entry["artifact-instance-id"] = "1002"
    current_entry["observed-entry-id"] = ci_validation_observed_entry_id(
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        artifact_ref=cast("str", current_entry["artifact-ref"]),
        artifact_instance_id="1002",
    )
    current_input = _observed_input(current_entry, current_receipt)
    return (
        snapshot,
        selector_manifest,
        prior_entry,
        prior_input,
        current_entry,
        current_input,
    )


def _manifest(
    entries: list[dict[str, object]],
) -> tuple[dict[str, object], object, dict[str, object]]:
    _receipt, snapshot, selector_manifest, _assignment = _valid_receipt()
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=entries,
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    return manifest, snapshot, selector_manifest


def _valid_aggregate() -> tuple[Any, dict[str, object]]:
    receipt, snapshot, selector_manifest, _assignment = _valid_receipt()
    entry = _entry(receipt)
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[_observed_input(entry, receipt)],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    return snapshot, aggregate


def _remove_inadmissible_receipt_accounting(
    aggregate: dict[str, object],
) -> None:
    aggregate["failures"] = [
        failure
        for failure in cast("list[dict[str, object]]", aggregate["failures"])
        if failure["kind"] != "inadmissible-receipt"
    ]
    aggregate["diagnostics"] = sorted(
        [
            cast("dict[str, object]", failure["diagnostic"])
            for failure in cast(
                "list[dict[str, object]]", aggregate["failures"]
            )
        ],
        key=lambda item: str(item["diagnostic-id"]),
    )
    cast("dict[str, object]", aggregate["reason"])["inadmissible-receipt"] = (
        False
    )
    if not cast("list[dict[str, object]]", aggregate["failures"]):
        aggregate["verdict"] = "passed"


def _set_diagnostic_verdict_effect(
    value: Any, diagnostic_id: str, verdict_effect: str
) -> None:
    if isinstance(value, dict):
        if value.get("diagnostic-id") == diagnostic_id:
            value["verdict-effect"] = verdict_effect
        for child in value.values():
            _set_diagnostic_verdict_effect(child, diagnostic_id, verdict_effect)
    elif isinstance(value, list):
        for child in value:
            _set_diagnostic_verdict_effect(child, diagnostic_id, verdict_effect)


def test_freeze_manifest_sorts_entries_and_closes_namespace() -> None:
    """Manifest entries and closure IDs use observed-entry-id ordering."""
    receipt, snapshot, _selector_manifest, _assignment = _valid_receipt()
    first = _entry(receipt, instance_id="1001")
    second = _entry(receipt, instance_id="1002")

    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[second, first],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    ids = [
        item["observed-entry-id"]
        for item in cast("list[dict[str, object]]", manifest["entries"])
    ]
    assert ids == sorted(ids)
    closure = cast("dict[str, object]", manifest["receipt-namespace-closure"])
    expected_entry_count = 2
    assert closure["closed-receipt-count"] == expected_entry_count
    assert closure["observed-entry-ids"] == ids
    validate_ci_validation_receipt_manifest(
        manifest,
        plan=snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )


def test_aggregate_rejects_readable_receipt_without_content_digest() -> None:
    """Readable receipts without observed raw-byte digests are inadmissible."""
    receipt, snapshot, selector_manifest, _assignment = _valid_receipt()
    entry = _entry(receipt)
    entry["receipt-content-digest"] = None

    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[_observed_input(entry, receipt)],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    observed = cast("list[dict[str, object]]", aggregate["observed-receipts"])
    reason = cast("dict[str, object]", aggregate["reason"])
    assert observed[0]["admissibility"] == "inadmissible"
    assert reason["inadmissible-receipt"] is True


def test_aggregate_rejects_parsed_receipt_without_raw_bytes() -> None:
    """Parsed receipts are inadmissible without observed raw artifact bytes."""
    receipt, snapshot, selector_manifest, _assignment = _valid_receipt()
    entry = _entry(receipt)
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[{"manifest-entry": entry, "receipt": receipt}],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    observed = cast("list[dict[str, object]]", aggregate["observed-receipts"])
    assert observed[0]["admissibility"] == "inadmissible"


def test_manifest_allows_classified_ref_without_receipt_id_or_digest() -> None:
    """Classified unreadable receipt entries can omit payload-only fields."""
    receipt, snapshot, _selector_manifest, _assignment = _valid_receipt()
    entry = _entry(receipt, receipt_id=None)
    entry["receipt-content-digest"] = None

    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    assert manifest["entries"] == [entry]


def test_freeze_manifest_rejects_missing_observed_entry_id() -> None:
    """Malformed entries are reported as contract validation failures."""
    receipt, snapshot, _selector_manifest, _assignment = _valid_receipt()
    entry = _entry(receipt)
    del entry["observed-entry-id"]

    with pytest.raises(ContractValidationError, match="observed-entry-id"):
        freeze_ci_validation_receipt_manifest(
            plan=snapshot.plan,
            entries=[entry],
            created_at=CREATED_AT,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_manifest_rejects_malformed_writer_ids() -> None:
    """Manifest writer identity fields use the registered writer-id shape."""
    receipt, snapshot, _selector_manifest, _assignment = _valid_receipt()
    entry = _entry(receipt)
    entry["trusted-writer-id"] = "github-actions-job:not-a-digest"

    with pytest.raises(ContractValidationError, match="github-actions-job"):
        freeze_ci_validation_receipt_manifest(
            plan=snapshot.plan,
            entries=[entry],
            created_at=CREATED_AT,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_manifest_allows_unclassified_null_ref_without_content_digest() -> None:
    """Unclassified null-ref manifest entries may remain without a digest."""
    receipt, snapshot, _selector_manifest, _assignment = _valid_receipt()
    entry = _entry(
        receipt, artifact_ref=None, work_group_id=None, receipt_id=None
    )
    entry["writer-work-group-id"] = None
    entry["receipt-content-digest"] = None

    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    assert (
        cast("list[dict[str, object]]", manifest["entries"])[0][
            "receipt-content-digest"
        ]
        is None
    )


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("assignment-id", "other-assignment"),
        ("trusted-writer-id", "github-actions-job:" + "2" * 64),
        ("observed-writer-id", "github-actions-job:" + "3" * 64),
        ("writer-observation-ref", None),
    ],
)
def test_manifest_writer_identity_mismatch_is_inadmissible(
    field: str, value: object
) -> None:
    """Readable receipts are bound to selector assignment writer identity."""
    receipt, snapshot, selector_manifest, _assignment = _valid_receipt()
    entry = _entry(receipt)
    entry[field] = value
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[_observed_input(entry, receipt)],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    observed = cast("list[dict[str, object]]", aggregate["observed-receipts"])
    diagnostics = cast("list[dict[str, object]]", observed[0]["diagnostics"])
    assert observed[0]["admissibility"] == "inadmissible"
    assert diagnostics[0]["detail"] == (
        DiagnosticDetail.MISMATCHED_WRITER_IDENTITY.value
    )
    assert (
        cast("dict[str, object]", aggregate["reason"])["inadmissible-receipt"]
        is True
    )


def test_aggregate_rejects_valid_observed_receipt_without_digest() -> None:
    """Valid readable observed receipts must carry a content digest."""
    receipt, snapshot, selector_manifest, _assignment = _valid_receipt()
    entry = _entry(receipt)
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[_observed_input(entry, receipt)],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    cast("list[dict[str, object]]", aggregate["observed-receipts"])[0][
        "receipt-content-digest"
    ] = None

    with pytest.raises(ContractValidationError, match="content-digest"):
        validate_ci_validation_aggregate(aggregate)


def test_manifest_rejects_tampered_observed_entry_id() -> None:
    """Observed entry IDs are recomputed from run, ref, and instance ID."""
    receipt, snapshot, _selector_manifest, _assignment = _valid_receipt()
    entry = _entry(receipt)
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    tampered_entry = cast("list[dict[str, object]]", manifest["entries"])[0]
    tampered_entry["observed-entry-id"] = "receipt-" + "0" * 64
    closure = cast("dict[str, object]", manifest["receipt-namespace-closure"])
    closure["observed-entry-ids"] = [tampered_entry["observed-entry-id"]]

    with pytest.raises(ContractValidationError, match="canonical derivation"):
        validate_ci_validation_receipt_manifest(
            manifest,
            plan=snapshot.plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_manifest_rejects_boolean_closed_receipt_count() -> None:
    """Namespace closure count is an integer, not a bool."""
    receipt, snapshot, _selector_manifest, _assignment = _valid_receipt()
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[_entry(receipt)],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    closure = cast("dict[str, object]", manifest["receipt-namespace-closure"])
    closure["closed-receipt-count"] = True

    with pytest.raises(ContractValidationError, match="integer"):
        validate_ci_validation_receipt_manifest(
            manifest,
            plan=snapshot.plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


@pytest.mark.parametrize(
    "artifact_ref",
    [
        f"ci-validation/receipts/other-run/{RUN_ATTEMPT}/{WORK_GROUP_ID}/receipt.json",
        f"ci-validation/receipts/{RUN_ID}/2/{WORK_GROUP_ID}/receipt.json",
        f"ci-validation/receipts/{RUN_ID}/{RUN_ATTEMPT}/{WORK_GROUP_ID}/other.json",
        f"ci-validation/planning/{RUN_ID}/{RUN_ATTEMPT}/validation-plan.json",
        f"ci-validation/receipts/{RUN_ID}/{RUN_ATTEMPT}/BadGroup/receipt.json",
    ],
)
def test_manifest_rejects_non_current_receipt_artifact_refs(
    artifact_ref: str,
) -> None:
    """Non-null manifest refs must be current run-attempt receipt refs."""
    receipt, snapshot, _selector_manifest, _assignment = _valid_receipt()
    entry = _entry(receipt)
    entry["artifact-ref"] = artifact_ref
    entry["physical-artifact-name"] = artifact_physical_name(artifact_ref)
    entry["observed-entry-id"] = ci_validation_observed_entry_id(
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        artifact_ref=artifact_ref,
        artifact_instance_id=cast("str", entry["artifact-instance-id"]),
    )
    manifest = freeze_ci_validation_receipt_manifest(
        plan=None,
        entries=[],
        created_at=CREATED_AT,
        repository_owner="hcoona",
        repository_name="three",
        workflow="CI Validation",
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
    )
    manifest["plan-id"] = snapshot.plan["plan-id"]
    manifest["plan-digest"] = snapshot.plan["plan-digest"]
    manifest["entries"] = [entry]
    closure = cast("dict[str, object]", manifest["receipt-namespace-closure"])
    closure["closed-receipt-count"] = 1
    closure["observed-entry-ids"] = [entry["observed-entry-id"]]

    with pytest.raises(
        ContractValidationError, match=r"receipt ref|manifest|path-safe"
    ):
        validate_ci_validation_receipt_manifest(
            manifest,
            plan=snapshot.plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_freeze_aggregate_passes_with_matching_manifest_and_receipt() -> None:
    """A valid required receipt satisfies its evidence expectation."""
    receipt, snapshot, selector_manifest, _assignment = _valid_receipt()
    entry = _entry(receipt)
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[_observed_input(entry, receipt)],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    assert aggregate["verdict"] == "passed"
    reason = cast("dict[str, object]", aggregate["reason"])
    assert reason["invalid-plan"] is False
    assert (
        cast("dict[str, object]", aggregate["work-groups"])[
            "required-succeeded"
        ]
        == 1
    )
    assert ci_validation_aggregate_payload_digest(aggregate)
    assert cast("dict[str, object]", aggregate["receipt-manifest"])[
        "content-digest"
    ] == ci_validation_receipt_manifest_payload_digest(manifest)


def test_aggregate_rejects_unjustified_fail_closed_failure() -> None:
    """Fail-closed failures must come from a fail-closed validated plan."""
    receipt, snapshot, selector_manifest, _assignment = _valid_receipt()
    entry = _entry(receipt)
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[_observed_input(entry, receipt)],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    diagnostic = ci_validation_diagnostic(
        diagnostic_id="final-evidence-failure/final-manifest-missing",
        code=DiagnosticFamily.FINAL_EVIDENCE_FAILURE.value,
        detail=DiagnosticDetail.FINAL_MANIFEST_MISSING.value,
        message="manifest missing",
        source_type="aggregation",
        source_id=None,
        severity=DiagnosticSeverity.BLOCKING_FAILURE.value,
        verdict_effect=DiagnosticVerdictEffect.FAIL_CLOSED.value,
    )
    aggregate["verdict"] = "failed"
    cast("dict[str, object]", aggregate["reason"])["fail-closed"] = True
    cast("list[dict[str, object]]", aggregate["failures"]).append(
        {
            "kind": "fail-closed",
            "work-group-id": None,
            "evidence-expectation-id": None,
            "receipt-id": None,
            "observed-entry-id": None,
            "receipt-artifact-ref": None,
            "receipt-content-digest": None,
            "message": "unjustified fail closed",
            "diagnostic": diagnostic,
        }
    )
    aggregate["diagnostics"] = [diagnostic]

    with pytest.raises(ContractValidationError, match="justified"):
        validate_ci_validation_aggregate(
            aggregate,
            plan=snapshot.plan,
            receipt_manifest=manifest,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_aggregate_rejects_unjustified_evidence_failure() -> None:
    """Evidence failures require a matching non-success evidence result."""
    receipt, snapshot, selector_manifest, _assignment = _valid_receipt()
    entry = _entry(receipt)
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[_observed_input(entry, receipt)],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    result = cast("list[dict[str, object]]", aggregate["evidence-results"])[0]
    diagnostic = _failed_diagnostic()
    aggregate["verdict"] = "failed"
    cast("dict[str, object]", aggregate["reason"])[
        "blocking-validation-failure"
    ] = True
    cast("list[dict[str, object]]", aggregate["failures"]).append(
        {
            "kind": "blocking-validation-failure",
            "work-group-id": result["work-group-id"],
            "evidence-expectation-id": result["evidence-expectation-id"],
            "receipt-id": result["receipt-id"],
            "observed-entry-id": result["observed-entry-id"],
            "receipt-artifact-ref": result["receipt-artifact-ref"],
            "receipt-content-digest": result["receipt-content-digest"],
            "message": "unjustified evidence failure",
            "diagnostic": diagnostic,
        }
    )
    aggregate["diagnostics"] = [diagnostic]

    with pytest.raises(ContractValidationError, match="justified"):
        validate_ci_validation_aggregate(
            aggregate,
            plan=snapshot.plan,
            receipt_manifest=manifest,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_aggregate_observed_receipts_must_mirror_manifest_entries() -> None:
    """Aggregate observed receipts cannot omit, add, or alter closed entries."""
    receipt, snapshot, selector_manifest, _assignment = _valid_receipt()
    first = _entry(receipt, instance_id="1001")
    second = _entry(receipt, instance_id="1002")
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[first, second],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    with pytest.raises(ContractValidationError, match="mirror"):
        freeze_ci_validation_aggregate(
            plan=snapshot.plan,
            receipt_manifest=manifest,
            selector_assignments_manifest=selector_manifest,
            observed_receipts=[_observed_input(first, receipt)],
            created_at=CREATED_AT,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )

    aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[
            _observed_input(first, receipt),
            _observed_input(second, receipt),
        ],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    cast("list[dict[str, object]]", aggregate["observed-receipts"]).append(
        deepcopy(
            cast("list[dict[str, object]]", aggregate["observed-receipts"])[0]
        )
    )
    with pytest.raises(ContractValidationError, match="mirror"):
        validate_ci_validation_aggregate(
            aggregate,
            plan=snapshot.plan,
            receipt_manifest=manifest,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )

    aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[
            _observed_input(first, receipt),
            _observed_input(second, receipt),
        ],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    observed = cast("list[dict[str, object]]", aggregate["observed-receipts"])
    observed[0]["receipt-content-digest"] = "0" * 64
    with pytest.raises(ContractValidationError, match="mirror"):
        validate_ci_validation_aggregate(
            aggregate,
            plan=snapshot.plan,
            receipt_manifest=manifest,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_aggregate_reports_required_evidence_failures() -> None:
    """Required evidence states map to reason booleans and failures."""
    receipt, snapshot, selector_manifest, _assignment = _valid_receipt()
    missing_manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    missing = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=missing_manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    assert (
        cast("dict[str, object]", missing["reason"])[
            "required-evidence-missing"
        ]
        is True
    )

    skipped_receipt = deepcopy(receipt)
    skipped_receipt["outcome"] = "skipped"
    skipped_receipt["diagnostics"] = [_skipped_diagnostic()]
    skipped_evidence = cast("dict[str, object]", skipped_receipt["evidence"])
    skipped_results = cast(
        "list[dict[str, object]]", skipped_evidence["capability-results"]
    )
    skipped_results[1]["outcome"] = "skipped"
    skipped_results[1]["diagnostics"] = [_skipped_diagnostic()]
    skipped_entry = _entry(skipped_receipt)
    skipped_manifest, _, _ = _manifest([skipped_entry])
    skipped = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=skipped_manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[_observed_input(skipped_entry, skipped_receipt)],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    assert (
        cast("dict[str, object]", skipped["reason"])[
            "required-evidence-skipped"
        ]
        is True
    )

    failed_receipt = deepcopy(receipt)
    failed_receipt["outcome"] = "blocking-failure"
    failed_receipt["diagnostics"] = [_failed_diagnostic()]
    failed_evidence = cast("dict[str, object]", failed_receipt["evidence"])
    failed_results = cast(
        "list[dict[str, object]]", failed_evidence["capability-results"]
    )
    failed_results[1]["outcome"] = "blocking-failure"
    failed_results[1]["diagnostics"] = [_failed_diagnostic()]
    failed_entry = _entry(failed_receipt)
    failed_manifest, _, _ = _manifest([failed_entry])
    failed = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=failed_manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[_observed_input(failed_entry, failed_receipt)],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    assert (
        cast("dict[str, object]", failed["reason"])[
            "blocking-validation-failure"
        ]
        is True
    )

    wrong_plan_receipt = deepcopy(receipt)
    wrong_plan_receipt["plan-id"] = "other-plan"
    bad_entry = _entry(wrong_plan_receipt)
    bad_manifest, _, _ = _manifest([bad_entry])
    inadmissible = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=bad_manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[_observed_input(bad_entry, wrong_plan_receipt)],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    reason = cast("dict[str, object]", inadmissible["reason"])
    assert reason["inadmissible-receipt"] is True
    assert reason["required-evidence-missing"] is True


def test_release_success_accepts_single_no_publish_source_command() -> None:
    """Release success may be backed by one observed no-publish command."""
    snapshot, selector_manifest, entry, observed_input = (
        _release_observed_input_with_no_publish_result()
    )
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[observed_input],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    artifact_result = next(
        item
        for item in cast(
            "list[dict[str, object]]", aggregate["evidence-results"]
        )
        if item["work-group-id"] == ARTIFACT_WORK_GROUP_ID
    )
    assert artifact_result["outcome"] == "satisfied"
    assert not any(
        failure["work-group-id"] == ARTIFACT_WORK_GROUP_ID
        for failure in cast("list[dict[str, object]]", aggregate["failures"])
    )
    validate_ci_validation_aggregate(
        aggregate,
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[observed_input],
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )


def test_release_success_accepts_same_work_group_reused_receipt_chain() -> None:
    """A reused receipt may rely on an observed same-work-group prior."""
    (
        snapshot,
        selector_manifest,
        prior_entry,
        prior_input,
        current_entry,
        current_input,
    ) = _release_reused_receipt_chain_inputs()
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[prior_entry, current_entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[prior_input, current_input],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    artifact_result = next(
        item
        for item in cast(
            "list[dict[str, object]]", aggregate["evidence-results"]
        )
        if item["work-group-id"] == ARTIFACT_WORK_GROUP_ID
    )
    assert artifact_result["outcome"] == "satisfied"
    assert (
        artifact_result["observed-entry-id"]
        == current_entry["observed-entry-id"]
    )
    assert not any(
        failure["kind"] == "blocking-validation-failure"
        and failure["work-group-id"] == ARTIFACT_WORK_GROUP_ID
        for failure in cast("list[dict[str, object]]", aggregate["failures"])
    )
    assert {
        item["admissibility"]
        for item in cast(
            "list[dict[str, object]]", aggregate["observed-receipts"]
        )
    } == {"valid"}
    validate_ci_validation_aggregate(
        aggregate,
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[prior_input, current_input],
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )


def test_release_success_rejects_reused_receipt_result_mismatch() -> None:
    """A reused receipt cannot replace the source artifact results it reuses."""
    (
        snapshot,
        selector_manifest,
        prior_entry,
        prior_input,
        current_entry,
        current_input,
    ) = _release_reused_receipt_chain_inputs()
    current_receipt = cast("dict[str, object]", current_input["receipt"])
    detail = cast(
        "dict[str, object]",
        cast(
            "dict[str, object]",
            cast("dict[str, object]", current_receipt["evidence"])[
                "category-result"
            ],
        )["detail"],
    )
    results = cast(
        "list[dict[str, object]]", detail["artifact-obligation-results"]
    )
    artifact = cast("dict[str, object]", results[0]["artifact"])
    observed = cast("dict[str, object]", artifact["observed"])
    digest = cast("list[dict[str, object]]", observed["digests"])[0]
    digest["digest"] = "e" * 64
    raw_current = canonical_json_bytes(current_receipt)
    current_input["raw-receipt-bytes"] = raw_current
    current_entry["receipt-content-digest"] = (
        ci_validation_receipt_content_digest(raw_current)
    )
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[prior_entry, current_entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[prior_input, current_input],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    assert aggregate["verdict"] == "failed"
    assert (
        cast("dict[str, object]", aggregate["reason"])[
            "blocking-validation-failure"
        ]
        is True
    )


def test_validation_accepts_duplicate_release_chain_bound_to_raw_bytes() -> (
    None
):
    """Duplicate release summaries are accepted when raw receipt bytes bind."""
    (
        snapshot,
        selector_manifest,
        prior_entry,
        prior_input,
        current_entry,
        current_input,
    ) = _release_reused_receipt_chain_inputs()
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[prior_entry, current_entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[prior_input, current_input],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    validate_ci_validation_aggregate(
        aggregate,
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[prior_input, current_input],
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )


@pytest.mark.parametrize(
    "bad_raw_bytes",
    [b'{"not":"the receipt"}', b'{"not":'],
)
def test_validation_rejects_duplicate_release_chain_bad_raw_bytes(
    bad_raw_bytes: bytes,
) -> None:
    """Duplicate release chain proof must bind to observed raw bytes."""
    (
        snapshot,
        selector_manifest,
        prior_entry,
        prior_input,
        current_entry,
        current_input,
    ) = _release_reused_receipt_chain_inputs()
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[prior_entry, current_entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[prior_input, current_input],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    tampered_current_input = deepcopy(current_input)
    tampered_current_input["raw-receipt-bytes"] = bad_raw_bytes

    with pytest.raises(ContractValidationError, match="duplicate valid"):
        validate_ci_validation_aggregate(
            aggregate,
            plan=snapshot.plan,
            receipt_manifest=manifest,
            selector_assignments_manifest=selector_manifest,
            observed_receipts=[prior_input, tampered_current_input],
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_release_success_accepts_multi_hop_same_work_group_reused_chain() -> (
    None
):
    """A reused receipt may rely on another admissible reused receipt."""
    (
        snapshot,
        selector_manifest,
        prior_entry,
        prior_input,
        current_entry,
        current_input,
    ) = _release_reused_receipt_chain_inputs()
    current_receipt = cast("dict[str, object]", current_input["receipt"])
    latest_receipt = deepcopy(current_receipt)
    latest_receipt["receipt-id"] = "receipt-reused-002"
    detail = cast(
        "dict[str, object]",
        cast(
            "dict[str, object]",
            cast("dict[str, object]", latest_receipt["evidence"])[
                "category-result"
            ],
        )["detail"],
    )
    detail["reused-receipt"] = {
        "artifact-ref": current_entry["artifact-ref"],
        "receipt-id": current_entry["receipt-id"],
        "receipt-content-digest": current_entry["receipt-content-digest"],
        "observed-commit-sha": cast(
            "dict[str, object]", current_receipt["execution-tree"]
        )["observed-commit-sha"],
    }
    latest_entry = _entry_for_assignment(
        latest_receipt,
        cast(
            "dict[str, object]",
            cast("list[dict[str, object]]", selector_manifest["assignments"])[
                0
            ],
        ),
    )
    latest_entry["artifact-instance-id"] = "1003"
    latest_entry["observed-entry-id"] = ci_validation_observed_entry_id(
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        artifact_ref=cast("str", latest_entry["artifact-ref"]),
        artifact_instance_id="1003",
    )
    latest_input = _observed_input(latest_entry, latest_receipt)
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[prior_entry, current_entry, latest_entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[prior_input, current_input, latest_input],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    artifact_result = next(
        item
        for item in cast(
            "list[dict[str, object]]", aggregate["evidence-results"]
        )
        if item["work-group-id"] == ARTIFACT_WORK_GROUP_ID
    )
    assert artifact_result["outcome"] == "satisfied"
    assert (
        artifact_result["observed-entry-id"]
        == latest_entry["observed-entry-id"]
    )
    assert {
        item["admissibility"]
        for item in cast(
            "list[dict[str, object]]", aggregate["observed-receipts"]
        )
    } == {"valid"}
    validate_ci_validation_aggregate(
        aggregate,
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[prior_input, current_input, latest_input],
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )


def test_aggregate_rejects_forged_current_reused_release_receipt() -> None:
    """Satisfied reused release evidence must validate the current receipt."""
    (
        snapshot,
        selector_manifest,
        prior_entry,
        prior_input,
        current_entry,
        current_input,
    ) = _release_reused_receipt_chain_inputs()
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[prior_entry, current_entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[prior_input, current_input],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    forged_input = deepcopy(current_input)
    forged_receipt = cast("dict[str, object]", forged_input["receipt"])
    forged_validation_tree = cast(
        "dict[str, object]", forged_receipt["validation-tree"]
    )
    forged_validation_tree["commit-sha"] = "f" * 40
    forged_raw = canonical_json_bytes(forged_receipt)
    forged_digest = ci_validation_receipt_content_digest(forged_raw)
    forged_input["raw-receipt-bytes"] = forged_raw
    forged_entry = cast("dict[str, object]", forged_input["manifest-entry"])
    forged_entry["receipt-content-digest"] = forged_digest

    forged_manifest = deepcopy(manifest)
    for entry in cast("list[dict[str, object]]", forged_manifest["entries"]):
        if entry["observed-entry-id"] == current_entry["observed-entry-id"]:
            entry["receipt-content-digest"] = forged_digest
    forged_aggregate = deepcopy(aggregate)
    for observed in cast(
        "list[dict[str, object]]", forged_aggregate["observed-receipts"]
    ):
        if observed["observed-entry-id"] == current_entry["observed-entry-id"]:
            observed["receipt-content-digest"] = forged_digest
    for result in cast(
        "list[dict[str, object]]", forged_aggregate["evidence-results"]
    ):
        if result["observed-entry-id"] == current_entry["observed-entry-id"]:
            result["receipt-content-digest"] = forged_digest
    cast("dict[str, object]", forged_aggregate["receipt-manifest"])[
        "content-digest"
    ] = ci_validation_receipt_manifest_payload_digest(forged_manifest)

    with pytest.raises(ContractValidationError, match="observed source proof"):
        validate_ci_validation_aggregate(
            forged_aggregate,
            plan=snapshot.plan,
            receipt_manifest=forged_manifest,
            selector_assignments_manifest=selector_manifest,
            observed_receipts=[prior_input, forged_input],
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_aggregate_rejects_duplicate_release_summaries_without_proof() -> None:
    """Duplicate release summaries cannot self-assert chain linkage."""
    (
        snapshot,
        selector_manifest,
        prior_entry,
        prior_input,
        current_entry,
        current_input,
    ) = _release_reused_receipt_chain_inputs()
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[prior_entry, current_entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[prior_input, current_input],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    with pytest.raises(ContractValidationError, match="duplicate valid"):
        validate_ci_validation_aggregate(
            aggregate,
            plan=snapshot.plan,
            receipt_manifest=manifest,
            selector_assignments_manifest=selector_manifest,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_aggregate_rejects_forged_single_reused_release_summary() -> None:
    """Reused release evidence cannot be satisfied without its source."""
    (
        snapshot,
        selector_manifest,
        prior_entry,
        prior_input,
        current_entry,
        current_input,
    ) = _release_reused_receipt_chain_inputs()
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[prior_entry, current_entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[prior_input, current_input],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    current_entry_id = current_entry["observed-entry-id"]
    aggregate["observed-receipts"] = [
        item
        for item in cast(
            "list[dict[str, object]]", aggregate["observed-receipts"]
        )
        if item["observed-entry-id"] == current_entry_id
    ]

    with pytest.raises(ContractValidationError, match="observed source proof"):
        validate_ci_validation_aggregate(
            aggregate,
            plan=snapshot.plan,
            receipt_manifest=manifest,
            selector_assignments_manifest=selector_manifest,
            observed_receipts=[current_input],
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_release_success_rejects_same_work_group_duplicate_current_reuse() -> (
    None
):
    """An extra current receipt for the same work group remains ambiguous."""
    (
        snapshot,
        selector_manifest,
        prior_entry,
        prior_input,
        current_entry,
        current_input,
    ) = _release_reused_receipt_chain_inputs()
    duplicate_receipt = deepcopy(
        cast("dict[str, object]", current_input["receipt"])
    )
    duplicate_receipt["receipt-id"] = "receipt-reused-002"
    duplicate_entry = deepcopy(current_entry)
    duplicate_entry["receipt-id"] = duplicate_receipt["receipt-id"]
    duplicate_entry["artifact-instance-id"] = "1003"
    duplicate_entry["observed-entry-id"] = ci_validation_observed_entry_id(
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        artifact_ref=cast("str", duplicate_entry["artifact-ref"]),
        artifact_instance_id="1003",
    )
    duplicate_entry["receipt-content-digest"] = (
        ci_validation_receipt_content_digest(
            canonical_json_bytes(duplicate_receipt)
        )
    )
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[prior_entry, current_entry, duplicate_entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[
            prior_input,
            current_input,
            _observed_input(duplicate_entry, duplicate_receipt),
        ],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    assert aggregate["verdict"] == "failed"
    assert (
        cast("dict[str, object]", aggregate["reason"])["inadmissible-receipt"]
        is True
    )
    assert any(
        cast("dict[str, object]", failure["diagnostic"])["detail"]
        == DiagnosticDetail.DUPLICATE_RECEIPT.value
        for failure in cast("list[dict[str, object]]", aggregate["failures"])
    )


def test_release_success_rejects_self_asserted_reused_receipt_chain() -> None:
    """A reused receipt link cannot prove itself."""
    (
        snapshot,
        selector_manifest,
        _prior_entry,
        _prior_input,
        current_entry,
        current_input,
    ) = _release_reused_receipt_chain_inputs()
    current_receipt = cast("dict[str, object]", current_input["receipt"])
    detail = cast(
        "dict[str, object]",
        cast(
            "dict[str, object]",
            cast("dict[str, object]", current_receipt["evidence"])[
                "category-result"
            ],
        )["detail"],
    )
    reused = cast("dict[str, object]", detail["reused-receipt"])
    reused["receipt-id"] = current_entry["receipt-id"]
    reused["receipt-content-digest"] = current_entry["receipt-content-digest"]
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[current_entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[current_input],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    assert aggregate["verdict"] == "failed"
    reason = cast("dict[str, object]", aggregate["reason"])
    assert (
        reason["blocking-validation-failure"] is True
        or reason["inadmissible-receipt"] is True
    )


@pytest.mark.parametrize(
    "extra_command",
    [
        {
            "outcome": "blocking-failure",
            "evidence-source": "no-publish-validation",
            "diagnostics": [],
        },
        {
            "outcome": "success",
            "evidence-source": "unsupported-sidecar-command",
            "diagnostics": [],
        },
        {"outcome": "success"},
        "malformed-command",
    ],
)
def test_release_success_rejects_extra_no_publish_sidecar_commands(
    extra_command: object,
) -> None:
    """Extra failed, unsupported, or malformed sidecar commands fail closed."""
    snapshot, selector_manifest, entry, observed_input = (
        _release_observed_input_with_no_publish_result(
            extra_commands=[extra_command]
        )
    )
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[observed_input],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    reason = cast("dict[str, object]", aggregate["reason"])
    assert aggregate["verdict"] == "failed"
    assert reason["blocking-validation-failure"] is True
    assert any(
        failure["kind"] == "blocking-validation-failure"
        and failure["work-group-id"] == ARTIFACT_WORK_GROUP_ID
        for failure in cast("list[dict[str, object]]", aggregate["failures"])
    )
    validate_ci_validation_aggregate(
        aggregate,
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )


def test_evidence_failure_kinds_reject_wrong_diagnostic_families() -> None:
    """Failure kind and mirrored evidence diagnostics must align."""
    receipt, snapshot, selector_manifest, _assignment = _valid_receipt()
    wrong_diagnostic = ci_validation_diagnostic(
        diagnostic_id="inadmissible-receipt/tampered",
        code=DiagnosticFamily.INADMISSIBLE_RECEIPT.value,
        detail=None,
        message="tampered diagnostic family",
        source_type="aggregation",
        source_id=None,
        severity=DiagnosticSeverity.BLOCKING_FAILURE.value,
        verdict_effect=DiagnosticVerdictEffect.FAILED.value,
    )
    aggregates: list[tuple[dict[str, object], dict[str, object]]] = []

    missing_manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    missing = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=missing_manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    aggregates.append((missing, missing_manifest))

    for outcome, helper in (
        ("skipped", _skipped_diagnostic),
        ("blocking-failure", _failed_diagnostic),
    ):
        non_success = deepcopy(receipt)
        non_success["outcome"] = outcome
        non_success["diagnostics"] = [helper()]
        evidence = cast("dict[str, object]", non_success["evidence"])
        capability_results = cast(
            "list[dict[str, object]]", evidence["capability-results"]
        )
        capability_results[1]["outcome"] = outcome
        capability_results[1]["diagnostics"] = [helper()]
        entry = _entry(non_success)
        manifest = freeze_ci_validation_receipt_manifest(
            plan=snapshot.plan,
            entries=[entry],
            created_at=CREATED_AT,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )
        aggregate = freeze_ci_validation_aggregate(
            plan=snapshot.plan,
            receipt_manifest=manifest,
            selector_assignments_manifest=selector_manifest,
            observed_receipts=[_observed_input(entry, non_success)],
            created_at=CREATED_AT,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )
        aggregates.append((aggregate, manifest))

    for aggregate, manifest in aggregates:
        tampered = deepcopy(aggregate)
        failure = cast("list[dict[str, object]]", tampered["failures"])[0]
        diagnostic = deepcopy(wrong_diagnostic)
        failure["diagnostic"] = diagnostic
        result = cast("list[dict[str, object]]", tampered["evidence-results"])[
            0
        ]
        result["diagnostics"] = [diagnostic]
        tampered["diagnostics"] = sorted(
            [
                cast("dict[str, object]", item["diagnostic"])
                for item in cast(
                    "list[dict[str, object]]", tampered["failures"]
                )
            ],
            key=lambda item: str(item["diagnostic-id"]),
        )

        with pytest.raises(ContractValidationError, match="failure kind"):
            validate_ci_validation_aggregate(
                tampered,
                plan=snapshot.plan,
                receipt_manifest=manifest,
                changed_files_snapshot=snapshot.changed_files_snapshot,
                fact_snapshot=snapshot.fact_snapshot,
            )


@pytest.mark.parametrize(
    "failure_kind",
    [
        "invalid-plan",
        "final-evidence-failure",
        "inadmissible-receipt",
        "required-evidence-missing",
        "required-evidence-skipped",
    ],
)
def test_failed_aggregate_failures_require_failed_verdict_effect(
    failure_kind: str,
) -> None:
    """Failed aggregate failures cannot carry non-effect diagnostics."""
    receipt, snapshot, selector_manifest, _assignment = _valid_receipt()

    if failure_kind == "invalid-plan":
        aggregate = freeze_ci_validation_invalid_plan_aggregate(
            created_at=CREATED_AT,
            repository_owner="hcoona",
            repository_name="three",
            workflow="CI Validation",
            run_id=RUN_ID,
            run_attempt=RUN_ATTEMPT,
        )
        manifest = None
    elif failure_kind == "final-evidence-failure":
        entry = _entry(receipt)
        manifest = freeze_ci_validation_receipt_manifest(
            plan=snapshot.plan,
            entries=[entry],
            created_at=CREATED_AT,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )
        aggregate = freeze_ci_validation_aggregate(
            plan=snapshot.plan,
            receipt_manifest=manifest,
            selector_assignments_manifest=selector_manifest,
            observed_receipts=[_observed_input(entry, receipt)],
            final_evidence_diagnostics=[
                ci_validation_diagnostic(
                    diagnostic_id=(
                        "final-evidence-failure/final-manifest-missing"
                    ),
                    code=DiagnosticFamily.FINAL_EVIDENCE_FAILURE.value,
                    detail=DiagnosticDetail.FINAL_MANIFEST_MISSING.value,
                    message="manifest missing",
                    source_type="aggregation",
                    source_id=None,
                    severity=DiagnosticSeverity.BLOCKING_FAILURE.value,
                    verdict_effect=DiagnosticVerdictEffect.FAILED.value,
                )
            ],
            created_at=CREATED_AT,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )
    elif failure_kind == "inadmissible-receipt":
        wrong_plan_receipt = deepcopy(receipt)
        wrong_plan_receipt["plan-id"] = "other-plan"
        entry = _entry(wrong_plan_receipt)
        manifest = freeze_ci_validation_receipt_manifest(
            plan=snapshot.plan,
            entries=[entry],
            created_at=CREATED_AT,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )
        aggregate = freeze_ci_validation_aggregate(
            plan=snapshot.plan,
            receipt_manifest=manifest,
            selector_assignments_manifest=selector_manifest,
            observed_receipts=[_observed_input(entry, wrong_plan_receipt)],
            created_at=CREATED_AT,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )
    elif failure_kind == "required-evidence-skipped":
        skipped_receipt = deepcopy(receipt)
        skipped_receipt["outcome"] = "skipped"
        skipped_receipt["diagnostics"] = [_skipped_diagnostic()]
        skipped_evidence = cast(
            "dict[str, object]", skipped_receipt["evidence"]
        )
        skipped_results = cast(
            "list[dict[str, object]]",
            skipped_evidence["capability-results"],
        )
        skipped_results[1]["outcome"] = "skipped"
        skipped_results[1]["diagnostics"] = [_skipped_diagnostic()]
        entry = _entry(skipped_receipt)
        manifest = freeze_ci_validation_receipt_manifest(
            plan=snapshot.plan,
            entries=[entry],
            created_at=CREATED_AT,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )
        aggregate = freeze_ci_validation_aggregate(
            plan=snapshot.plan,
            receipt_manifest=manifest,
            selector_assignments_manifest=selector_manifest,
            observed_receipts=[_observed_input(entry, skipped_receipt)],
            created_at=CREATED_AT,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )
    else:
        manifest = freeze_ci_validation_receipt_manifest(
            plan=snapshot.plan,
            entries=[],
            created_at=CREATED_AT,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )
        aggregate = freeze_ci_validation_aggregate(
            plan=snapshot.plan,
            receipt_manifest=manifest,
            selector_assignments_manifest=selector_manifest,
            observed_receipts=[],
            created_at=CREATED_AT,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )

    failure = next(
        item
        for item in cast("list[dict[str, object]]", aggregate["failures"])
        if item["kind"] == failure_kind
    )
    diagnostic_id = cast(
        "str",
        cast("dict[str, object]", failure["diagnostic"])["diagnostic-id"],
    )
    _set_diagnostic_verdict_effect(
        aggregate, diagnostic_id, DiagnosticVerdictEffect.NONE.value
    )

    if failure_kind == "invalid-plan":
        with pytest.raises(ContractValidationError, match="verdict-effect"):
            validate_ci_validation_aggregate(aggregate)
    else:
        assert manifest is not None
        with pytest.raises(ContractValidationError, match="verdict-effect"):
            validate_ci_validation_aggregate(
                aggregate,
                plan=snapshot.plan,
                receipt_manifest=manifest,
                selector_assignments_manifest=selector_manifest,
                changed_files_snapshot=snapshot.changed_files_snapshot,
                fact_snapshot=snapshot.fact_snapshot,
            )


def test_aggregate_evidence_results_drive_failures_counts_and_verdict() -> None:
    """Non-success evidence requires failures and failed verdict."""
    receipt, snapshot, selector_manifest, _assignment = _valid_receipt()
    entry = _entry(receipt)
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[_observed_input(entry, receipt)],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    result = cast("list[dict[str, object]]", aggregate["evidence-results"])[0]
    result["outcome"] = "failed"
    counts = cast("dict[str, object]", aggregate["work-groups"])
    counts["required-succeeded"] = 0
    counts["required-failed"] = 1

    with pytest.raises(ContractValidationError, match="non-success"):
        validate_ci_validation_aggregate(
            aggregate,
            plan=snapshot.plan,
            receipt_manifest=manifest,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )

    result["outcome"] = "satisfied"
    counts["required-succeeded"] = 0
    counts["required-failed"] = 0
    with pytest.raises(ContractValidationError, match="evidence-result"):
        validate_ci_validation_aggregate(
            aggregate,
            plan=snapshot.plan,
            receipt_manifest=manifest,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_missing_evidence_result_rejects_existing_valid_observed_receipt() -> (
    None
):
    """Missing evidence cannot ignore a valid receipt for the work group."""
    receipt, snapshot, selector_manifest, _assignment = _valid_receipt()
    entry = _entry(receipt)
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[_observed_input(entry, receipt)],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    result = cast("list[dict[str, object]]", aggregate["evidence-results"])[0]
    diagnostic = ci_validation_diagnostic(
        diagnostic_id="required-evidence-missing/evidence-python-gate",
        code=DiagnosticFamily.REQUIRED_EVIDENCE_MISSING.value,
        detail=None,
        message="Required evidence receipt is missing",
        source_type="aggregation",
        source_id=None,
        severity=DiagnosticSeverity.BLOCKING_FAILURE.value,
        verdict_effect=DiagnosticVerdictEffect.FAILED.value,
    )
    result.update(
        {
            "receipt-id": None,
            "observed-entry-id": None,
            "receipt-artifact-ref": None,
            "receipt-content-digest": None,
            "outcome": "missing",
            "diagnostics": [diagnostic],
        }
    )
    cast("list[dict[str, object]]", aggregate["failures"]).append(
        {
            "kind": "required-evidence-missing",
            "work-group-id": result["work-group-id"],
            "evidence-expectation-id": result["evidence-expectation-id"],
            "receipt-id": None,
            "observed-entry-id": None,
            "receipt-artifact-ref": None,
            "receipt-content-digest": None,
            "diagnostic": diagnostic,
            "message": "Required evidence receipt is missing",
        }
    )
    aggregate["verdict"] = "failed"
    reason = cast("dict[str, object]", aggregate["reason"])
    reason["required-evidence-missing"] = True
    counts = cast("dict[str, object]", aggregate["work-groups"])
    counts["required-succeeded"] = 0
    counts["required-missing"] = 1
    aggregate["diagnostics"] = [diagnostic]

    with pytest.raises(ContractValidationError, match="zero valid"):
        validate_ci_validation_aggregate(
            aggregate,
            plan=snapshot.plan,
            receipt_manifest=manifest,
            selector_assignments_manifest=selector_manifest,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_aggregate_evidence_results_must_cover_plan_expectations() -> None:
    """Evidence result IDs and work groups must exactly match the plan."""
    receipt, snapshot, selector_manifest, _assignment = _valid_receipt()
    entry = _entry(receipt)
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    def valid_aggregate() -> dict[str, object]:
        return freeze_ci_validation_aggregate(
            plan=snapshot.plan,
            receipt_manifest=manifest,
            selector_assignments_manifest=selector_manifest,
            observed_receipts=[_observed_input(entry, receipt)],
            created_at=CREATED_AT,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )

    omitted = valid_aggregate()
    omitted["evidence-results"] = []
    counts = cast("dict[str, object]", omitted["work-groups"])
    counts["executable-required"] = 0
    counts["required-succeeded"] = 0
    with pytest.raises(ContractValidationError, match="plan evidence"):
        validate_ci_validation_aggregate(
            omitted,
            plan=snapshot.plan,
            receipt_manifest=manifest,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )

    extra = valid_aggregate()
    extra_result = deepcopy(
        cast("list[dict[str, object]]", extra["evidence-results"])[0]
    )
    extra_result["evidence-expectation-id"] = "evidence-extra"
    cast("list[dict[str, object]]", extra["evidence-results"]).append(
        extra_result
    )
    counts = cast("dict[str, object]", extra["work-groups"])
    counts["executable-required"] = 2
    counts["required-succeeded"] = 2
    with pytest.raises(ContractValidationError, match="plan evidence"):
        validate_ci_validation_aggregate(
            extra,
            plan=snapshot.plan,
            receipt_manifest=manifest,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )

    duplicate = valid_aggregate()
    duplicate_result = deepcopy(
        cast("list[dict[str, object]]", duplicate["evidence-results"])[0]
    )
    cast("list[dict[str, object]]", duplicate["evidence-results"]).append(
        duplicate_result
    )
    counts = cast("dict[str, object]", duplicate["work-groups"])
    counts["executable-required"] = 2
    counts["required-succeeded"] = 2
    with pytest.raises(ContractValidationError, match="unique"):
        validate_ci_validation_aggregate(
            duplicate,
            plan=snapshot.plan,
            receipt_manifest=manifest,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )

    renamed = valid_aggregate()
    cast("list[dict[str, object]]", renamed["evidence-results"])[0][
        "evidence-expectation-id"
    ] = "evidence-renamed"
    with pytest.raises(ContractValidationError, match="plan evidence"):
        validate_ci_validation_aggregate(
            renamed,
            plan=snapshot.plan,
            receipt_manifest=manifest,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )

    mismatched = valid_aggregate()
    cast("list[dict[str, object]]", mismatched["evidence-results"])[0][
        "work-group-id"
    ] = "wg-other"
    with pytest.raises(ContractValidationError, match="plan evidence"):
        validate_ci_validation_aggregate(
            mismatched,
            plan=snapshot.plan,
            receipt_manifest=manifest,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_inadmissible_observed_receipts_require_failures() -> None:
    """Every inadmissible observed receipt is verdict-affecting."""
    receipt, snapshot, selector_manifest, _assignment = _valid_receipt()
    wrong_plan_receipt = deepcopy(receipt)
    wrong_plan_receipt["plan-id"] = "other-plan"
    entry = _entry(wrong_plan_receipt)
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[_observed_input(entry, wrong_plan_receipt)],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    aggregate["failures"] = [
        item
        for item in cast("list[dict[str, object]]", aggregate["failures"])
        if item["kind"] != "inadmissible-receipt"
    ]
    cast("dict[str, object]", aggregate["reason"])["inadmissible-receipt"] = (
        False
    )

    with pytest.raises(ContractValidationError, match="inadmissible receipts"):
        validate_ci_validation_aggregate(
            aggregate,
            plan=snapshot.plan,
            receipt_manifest=manifest,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_null_or_malformed_ref_entries_cannot_derive_work_group() -> None:
    """Unclassified receipt-like entries cannot satisfy evidence by payload."""
    receipt, snapshot, selector_manifest, _assignment = _valid_receipt()
    null_entry = _entry(
        receipt,
        artifact_ref=None,
        work_group_id=None,
        instance_id="null-ref",
        receipt_id=None,
    )
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[null_entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[null_entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    observed = cast("list[dict[str, object]]", aggregate["observed-receipts"])
    assert observed[0]["admissibility"] == "inadmissible"
    assert observed[0]["work-group-id"] is None
    assert cast("dict[str, object]", aggregate["reason"])[
        "required-evidence-missing"
    ]

    for key, value in {
        "assignment-id": WORK_GROUP_ID,
        "writer-work-group-id": WORK_GROUP_ID,
        "trusted-writer-id": _writer_id(WORK_GROUP_ID),
        "observed-writer-id": _writer_id(WORK_GROUP_ID),
        "writer-observation-ref": _writer_observation_ref(WORK_GROUP_ID),
        "receipt-id": "receipt-001",
    }.items():
        invalid_null_entry = deepcopy(null_entry)
        invalid_null_entry[key] = value
        with pytest.raises(ContractValidationError, match="established"):
            freeze_ci_validation_receipt_manifest(
                plan=snapshot.plan,
                entries=[invalid_null_entry],
                created_at=CREATED_AT,
                changed_files_snapshot=snapshot.changed_files_snapshot,
                fact_snapshot=snapshot.fact_snapshot,
            )

    malformed_ref = f"ci-validation/planning/{RUN_ID}/{RUN_ATTEMPT}/x.json"
    malformed_entry = deepcopy(null_entry)
    malformed_entry["artifact-ref"] = malformed_ref
    malformed_entry["physical-artifact-name"] = artifact_physical_name(
        malformed_ref
    )
    malformed_entry["observed-entry-id"] = ci_validation_observed_entry_id(
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        artifact_ref=malformed_ref,
        artifact_instance_id=cast(
            "str", malformed_entry["artifact-instance-id"]
        ),
    )
    malformed_entry["writer-work-group-id"] = WORK_GROUP_ID
    with pytest.raises(
        ContractValidationError, match=r"established|receipt ref"
    ):
        freeze_ci_validation_receipt_manifest(
            plan=snapshot.plan,
            entries=[malformed_entry],
            created_at=CREATED_AT,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_aggregate_receipt_manifest_ref_binding_is_unconditional() -> None:
    """Standalone validation enforces manifest ref invariants."""
    receipt, snapshot, selector_manifest, _assignment = _valid_receipt()
    entry = _entry(receipt)
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[_observed_input(entry, receipt)],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    wrong_ref = deepcopy(aggregate)
    cast("dict[str, object]", wrong_ref["receipt-manifest"])["artifact-ref"] = (
        "ci-validation/manifests/other/1/receipt-manifest.json"
    )
    with pytest.raises(ContractValidationError, match="contract-owned"):
        validate_ci_validation_aggregate(
            wrong_ref,
            plan=snapshot.plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )

    null_ref = deepcopy(aggregate)
    binding = cast("dict[str, object]", null_ref["receipt-manifest"])
    binding["artifact-ref"] = None
    binding["content-digest"] = None
    with pytest.raises(ContractValidationError, match="authoritative"):
        validate_ci_validation_aggregate(
            null_ref,
            plan=snapshot.plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )

    null_digest = deepcopy(aggregate)
    cast("dict[str, object]", null_digest["receipt-manifest"])[
        "content-digest"
    ] = None
    with pytest.raises(ContractValidationError, match="both"):
        validate_ci_validation_aggregate(
            null_digest,
            plan=snapshot.plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )

    digest_without_ref = deepcopy(aggregate)
    cast("dict[str, object]", digest_without_ref["receipt-manifest"])[
        "artifact-ref"
    ] = None
    with pytest.raises(ContractValidationError, match="both"):
        validate_ci_validation_aggregate(
            digest_without_ref,
            plan=snapshot.plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_standalone_aggregate_binds_observed_receipts_to_envelope() -> None:
    """Standalone validation binds observed receipt refs to envelope."""
    snapshot, aggregate = _valid_aggregate()
    validate_ci_validation_aggregate(
        aggregate,
        plan=snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    receipt = cast(
        "dict[str, object]",
        cast("list[dict[str, object]]", aggregate["observed-receipts"])[0],
    )
    instance_id = cast("str", receipt["artifact-instance-id"])

    for ref, match in (
        (
            ci_validation_receipt_artifact_ref(
                run_id="other-run",
                run_attempt=RUN_ATTEMPT,
                work_group_id=WORK_GROUP_ID,
            ),
            "aggregate run",
        ),
        (
            ci_validation_receipt_artifact_ref(
                run_id=RUN_ID,
                run_attempt="2",
                work_group_id=WORK_GROUP_ID,
            ),
            "aggregate run attempt",
        ),
        (
            f"ci-validation/not-receipts/{RUN_ID}/{RUN_ATTEMPT}/"
            f"{WORK_GROUP_ID}/receipt.json",
            "current run-attempt receipt ref",
        ),
    ):
        tampered = deepcopy(aggregate)
        observed = cast(
            "dict[str, object]",
            cast("list[dict[str, object]]", tampered["observed-receipts"])[0],
        )
        observed["artifact-ref"] = ref
        observed["physical-artifact-name"] = artifact_physical_name(ref)
        observed["observed-entry-id"] = ci_validation_observed_entry_id(
            run_id=RUN_ID,
            run_attempt=RUN_ATTEMPT,
            artifact_ref=ref,
            artifact_instance_id=instance_id,
        )
        with pytest.raises(ContractValidationError, match=match):
            validate_ci_validation_aggregate(
                tampered,
                plan=snapshot.plan,
                changed_files_snapshot=snapshot.changed_files_snapshot,
                fact_snapshot=snapshot.fact_snapshot,
            )


def test_standalone_aggregate_rejects_tampered_observed_entry_id() -> None:
    """Standalone aggregate validation checks observed-entry-id derivation."""
    snapshot, aggregate = _valid_aggregate()
    observed = cast(
        "dict[str, object]",
        cast("list[dict[str, object]]", aggregate["observed-receipts"])[0],
    )
    observed["observed-entry-id"] = "receipt-" + "0" * 64

    with pytest.raises(ContractValidationError, match="canonical derivation"):
        validate_ci_validation_aggregate(
            aggregate,
            plan=snapshot.plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_standalone_aggregate_rejects_duplicate_observed_entry_id() -> None:
    """Standalone aggregate validation enforces observed-entry-id uniqueness."""
    receipt, snapshot, selector_manifest, _assignment = _valid_receipt()
    null_entry = _entry(
        receipt,
        artifact_ref=None,
        work_group_id=None,
        instance_id="unclassified-receipt",
        receipt_id=None,
    )
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[null_entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[null_entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    validate_ci_validation_aggregate(
        aggregate,
        plan=snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    observed = cast("list[dict[str, object]]", aggregate["observed-receipts"])
    observed.append(deepcopy(observed[0]))

    with pytest.raises(
        ContractValidationError, match=r"unique|duplicate|observed-entry-id"
    ):
        validate_ci_validation_aggregate(
            aggregate,
            plan=snapshot.plan,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_standalone_non_invalid_aggregate_requires_verified_plan() -> None:
    """Standalone non-invalid aggregate cannot validate without a plan."""
    _snapshot, aggregate = _valid_aggregate()
    aggregate["plan-id"] = None
    aggregate["plan-digest"] = None
    aggregate["mode"] = "unknown"

    with pytest.raises(ContractValidationError, match="validated plan"):
        validate_ci_validation_aggregate(aggregate)


def test_standalone_final_evidence_failure_requires_verified_plan() -> None:
    """Standalone final-only failure aggregate still requires a plan."""
    snapshot, aggregate = _valid_aggregate()
    diagnostic = ci_validation_diagnostic(
        diagnostic_id="final-evidence-failure/final-manifest-missing",
        code=DiagnosticFamily.FINAL_EVIDENCE_FAILURE.value,
        detail=DiagnosticDetail.FINAL_MANIFEST_MISSING.value,
        message="manifest missing",
        source_type="aggregation",
        source_id=None,
        severity=DiagnosticSeverity.BLOCKING_FAILURE.value,
        verdict_effect=DiagnosticVerdictEffect.FAILED.value,
    )
    aggregate["verdict"] = "failed"
    aggregate["plan-id"] = None
    aggregate["plan-digest"] = None
    aggregate["mode"] = "unknown"
    cast("dict[str, object]", aggregate["reason"])["final-evidence-failure"] = (
        True
    )
    cast("list[dict[str, object]]", aggregate["failures"]).append(
        {
            "kind": "final-evidence-failure",
            "work-group-id": None,
            "evidence-expectation-id": None,
            "receipt-id": None,
            "observed-entry-id": None,
            "receipt-artifact-ref": None,
            "receipt-content-digest": None,
            "message": "manifest missing",
            "diagnostic": diagnostic,
        }
    )
    aggregate["diagnostics"] = sorted(
        [
            *cast("list[dict[str, object]]", aggregate["diagnostics"]),
            diagnostic,
        ],
        key=lambda item: str(item["diagnostic-id"]),
    )
    aggregate["failure-count"] = len(
        cast("list[dict[str, object]]", aggregate["failures"])
    )
    assert snapshot.plan["plan-id"]

    with pytest.raises(ContractValidationError, match="validated plan"):
        validate_ci_validation_aggregate(aggregate)


def test_invalid_plan_mode_does_not_create_inadmissible_receipt_failures() -> (
    None
):
    """Invalid-plan aggregates keep observed receipts inspection-only."""
    entry = _entry(None, artifact_ref=None, work_group_id=None, receipt_id=None)

    aggregate = freeze_ci_validation_invalid_plan_aggregate(
        created_at=CREATED_AT,
        repository_owner="hcoona",
        repository_name="three",
        workflow="CI Validation",
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        diagnostic_detail=DiagnosticDetail.STRUCTURALLY_INVALID.value,
        observed_receipts=[entry],
    )

    reason = cast("dict[str, object]", aggregate["reason"])
    assert reason["invalid-plan"] is True
    assert reason["inadmissible-receipt"] is False
    assert [
        item["kind"]
        for item in cast("list[dict[str, object]]", aggregate["failures"])
    ] == ["invalid-plan"]
    assert (
        cast("dict[str, object]", aggregate["work-groups"])[
            "executable-required"
        ]
        == 0
    )


def test_invalid_plan_aggregate_rejects_extra_failure_modes() -> None:
    """Invalid-plan aggregates are isolated from other failure reasons."""
    aggregate = freeze_ci_validation_invalid_plan_aggregate(
        created_at=CREATED_AT,
        repository_owner="hcoona",
        repository_name="three",
        workflow="CI Validation",
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
    )
    extra = {
        **cast("list[dict[str, object]]", aggregate["failures"])[0],
        "kind": "final-evidence-failure",
        "diagnostic": ci_validation_diagnostic(
            diagnostic_id="final-evidence-failure/final-manifest-missing",
            code=DiagnosticFamily.FINAL_EVIDENCE_FAILURE.value,
            detail=DiagnosticDetail.FINAL_MANIFEST_MISSING.value,
            message="manifest missing",
            source_type="aggregation",
            source_id=None,
            severity=DiagnosticSeverity.BLOCKING_FAILURE.value,
            verdict_effect=DiagnosticVerdictEffect.FAILED.value,
        ),
    }
    cast("list[dict[str, object]]", aggregate["failures"]).append(extra)
    cast("dict[str, object]", aggregate["reason"])["final-evidence-failure"] = (
        True
    )
    aggregate["diagnostics"] = sorted(
        [
            cast("dict[str, object]", item["diagnostic"])
            for item in cast("list[dict[str, object]]", aggregate["failures"])
        ],
        key=lambda item: str(item["diagnostic-id"]),
    )

    with pytest.raises(ContractValidationError, match="invalid-plan"):
        validate_ci_validation_aggregate(aggregate)


def test_invalid_plan_aggregate_rejects_duplicate_invalid_plan_failures() -> (
    None
):
    """Invalid-plan aggregates carry exactly one applicable failure."""
    aggregate = freeze_ci_validation_invalid_plan_aggregate(
        created_at=CREATED_AT,
        repository_owner="hcoona",
        repository_name="three",
        workflow="CI Validation",
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
    )
    failure = deepcopy(
        cast("list[dict[str, object]]", aggregate["failures"])[0]
    )
    cast("list[dict[str, object]]", aggregate["failures"]).append(failure)

    with pytest.raises(ContractValidationError, match="exactly one"):
        validate_ci_validation_aggregate(aggregate)


def test_invalid_plan_failure_diagnostic_must_match_family() -> None:
    """Invalid-plan failures carry only invalid-plan diagnostics."""
    aggregate = freeze_ci_validation_invalid_plan_aggregate(
        created_at=CREATED_AT,
        repository_owner="hcoona",
        repository_name="three",
        workflow="CI Validation",
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
    )
    failure = cast("list[dict[str, object]]", aggregate["failures"])[0]
    failure["diagnostic"] = ci_validation_diagnostic(
        diagnostic_id="final-evidence-failure/final-manifest-missing",
        code=DiagnosticFamily.FINAL_EVIDENCE_FAILURE.value,
        detail=DiagnosticDetail.FINAL_MANIFEST_MISSING.value,
        message="manifest missing",
        source_type="aggregation",
        source_id=None,
        severity=DiagnosticSeverity.BLOCKING_FAILURE.value,
        verdict_effect=DiagnosticVerdictEffect.FAILED.value,
    )

    with pytest.raises(ContractValidationError, match="invalid-plan"):
        validate_ci_validation_aggregate(aggregate)


def test_invalid_plan_failure_diagnostic_requires_detail() -> None:
    """Invalid-plan failures cannot omit the registered detail."""
    aggregate = freeze_ci_validation_invalid_plan_aggregate(
        created_at=CREATED_AT,
        repository_owner="hcoona",
        repository_name="three",
        workflow="CI Validation",
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
    )
    failure = cast("list[dict[str, object]]", aggregate["failures"])[0]
    diagnostic = cast("dict[str, object]", failure["diagnostic"])
    diagnostic["detail"] = None
    aggregate["diagnostics"] = [diagnostic]

    with pytest.raises(ContractValidationError, match="detail"):
        validate_ci_validation_aggregate(aggregate)


def test_invalid_plan_aggregate_rejects_valid_observed_receipts() -> None:
    """Invalid-plan aggregates only carry inspection-only observed receipts."""
    receipt, snapshot, selector_manifest, _assignment = _valid_receipt()
    entry = _entry(receipt)
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    valid_aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[_observed_input(entry, receipt)],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    invalid_aggregate = freeze_ci_validation_invalid_plan_aggregate(
        created_at=CREATED_AT,
        repository_owner="hcoona",
        repository_name="three",
        workflow="CI Validation",
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
    )
    invalid_aggregate["observed-receipts"] = valid_aggregate[
        "observed-receipts"
    ]

    with pytest.raises(ContractValidationError, match="inadmissible"):
        validate_ci_validation_aggregate(invalid_aggregate)


def test_invalid_plan_helper_validates_manifest_mirroring() -> None:
    """Invalid-plan helper rejects manifest/aggregate observed mismatches."""
    entry = _entry(None, artifact_ref=None, work_group_id=None, receipt_id=None)
    manifest = freeze_ci_validation_receipt_manifest(
        plan=None,
        entries=[entry],
        created_at=CREATED_AT,
        repository_owner="hcoona",
        repository_name="three",
        workflow="CI Validation",
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
    )

    with pytest.raises(ContractValidationError, match="mirror"):
        freeze_ci_validation_invalid_plan_aggregate(
            created_at=CREATED_AT,
            repository_owner="hcoona",
            repository_name="three",
            workflow="CI Validation",
            run_id=RUN_ID,
            run_attempt=RUN_ATTEMPT,
            receipt_manifest=manifest,
            observed_receipts=[],
        )


def test_post_plan_invalid_preserves_only_verified_plan_fields() -> None:
    """Post-plan invalid copies only verified plan fields."""
    _receipt, snapshot, _selector_manifest, _assignment = _valid_receipt()
    unverified = freeze_ci_validation_invalid_plan_aggregate(
        created_at=CREATED_AT,
        repository_owner="hcoona",
        repository_name="three",
        workflow="CI Validation",
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        plan=snapshot.plan,
        post_plan_contract_invalid=True,
    )
    assert unverified["plan-id"] is None
    assert unverified["mode"] == "unknown"

    verified = freeze_ci_validation_invalid_plan_aggregate(
        created_at=CREATED_AT,
        repository_owner="hcoona",
        repository_name="three",
        workflow="CI Validation",
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        plan=snapshot.plan,
        post_plan_contract_invalid=True,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    assert verified["plan-id"] == snapshot.plan["plan-id"]
    validate_ci_validation_aggregate(
        verified,
        plan=snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    with pytest.raises(ContractValidationError, match="verified plan"):
        validate_ci_validation_aggregate(verified)


def test_invalid_plan_observed_receipts_ignore_unclassified_writer() -> None:
    """Inspection-only receipts derive work group only from receipt refs."""
    entry = _entry(
        None,
        artifact_ref=None,
        work_group_id=WORK_GROUP_ID,
        receipt_id=None,
    )
    aggregate = freeze_ci_validation_invalid_plan_aggregate(
        created_at=CREATED_AT,
        repository_owner="hcoona",
        repository_name="three",
        workflow="CI Validation",
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        observed_receipts=[entry],
    )

    observed = cast("list[dict[str, object]]", aggregate["observed-receipts"])
    assert observed[0]["work-group-id"] is None
    validate_ci_validation_aggregate(aggregate)


def test_observed_receipt_work_group_requires_receipt_ref() -> None:
    """Observed receipt work group identity is bound to valid receipt refs."""
    null_entry = _entry(
        None,
        artifact_ref=None,
        work_group_id=None,
        receipt_id=None,
    )
    aggregate = freeze_ci_validation_invalid_plan_aggregate(
        created_at=CREATED_AT,
        repository_owner="hcoona",
        repository_name="three",
        workflow="CI Validation",
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        observed_receipts=[null_entry],
    )
    cast("list[dict[str, object]]", aggregate["observed-receipts"])[0][
        "work-group-id"
    ] = WORK_GROUP_ID
    with pytest.raises(ContractValidationError, match="receipt artifact-ref"):
        validate_ci_validation_aggregate(aggregate)

    malformed_ref = f"ci-validation/planning/{RUN_ID}/{RUN_ATTEMPT}/x.json"
    malformed_entry = deepcopy(null_entry)
    malformed_entry["artifact-ref"] = malformed_ref
    malformed_entry["physical-artifact-name"] = artifact_physical_name(
        malformed_ref
    )
    malformed_entry["observed-entry-id"] = ci_validation_observed_entry_id(
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        artifact_ref=malformed_ref,
        artifact_instance_id=cast(
            "str", malformed_entry["artifact-instance-id"]
        ),
    )
    with pytest.raises(ContractValidationError, match="receipt ref"):
        freeze_ci_validation_invalid_plan_aggregate(
            created_at=CREATED_AT,
            repository_owner="hcoona",
            repository_name="three",
            workflow="CI Validation",
            run_id=RUN_ID,
            run_attempt=RUN_ATTEMPT,
            observed_receipts=[malformed_entry],
        )


def test_satisfied_evidence_result_requires_observed_receipt_binding() -> None:
    """Satisfied evidence must point at its matching valid observed receipt."""
    receipt, snapshot, selector_manifest, _assignment = _valid_receipt()
    entry = _entry(receipt)
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    def valid_aggregate() -> dict[str, object]:
        return freeze_ci_validation_aggregate(
            plan=snapshot.plan,
            receipt_manifest=manifest,
            selector_assignments_manifest=selector_manifest,
            observed_receipts=[_observed_input(entry, receipt)],
            created_at=CREATED_AT,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )

    null_binding = valid_aggregate()
    result = cast("list[dict[str, object]]", null_binding["evidence-results"])[
        0
    ]
    result["receipt-content-digest"] = None
    with pytest.raises(ContractValidationError, match="satisfied evidence"):
        validate_ci_validation_aggregate(
            null_binding,
            plan=snapshot.plan,
            receipt_manifest=manifest,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )

    missing_observed = valid_aggregate()
    result = cast(
        "list[dict[str, object]]", missing_observed["evidence-results"]
    )[0]
    result["observed-entry-id"] = "missing-entry"
    with pytest.raises(ContractValidationError, match="observed receipt"):
        validate_ci_validation_aggregate(
            missing_observed,
            plan=snapshot.plan,
            receipt_manifest=manifest,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )

    mismatched_digest = valid_aggregate()
    result = cast(
        "list[dict[str, object]]", mismatched_digest["evidence-results"]
    )[0]
    result["receipt-content-digest"] = "0" * 64
    with pytest.raises(ContractValidationError, match="observed receipt"):
        validate_ci_validation_aggregate(
            mismatched_digest,
            plan=snapshot.plan,
            receipt_manifest=manifest,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )

    inadmissible_observed = valid_aggregate()
    cast("list[dict[str, object]]", inadmissible_observed["observed-receipts"])[
        0
    ]["admissibility"] = "inadmissible"
    with pytest.raises(ContractValidationError, match="valid observed receipt"):
        validate_ci_validation_aggregate(
            inadmissible_observed,
            plan=snapshot.plan,
            receipt_manifest=manifest,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_non_success_evidence_results_require_observed_receipt_binding() -> (
    None
):
    """Skipped and failed evidence must bind the matching valid receipt."""
    for receipt_outcome, result_outcome, diagnostic in (
        ("skipped", "skipped", _skipped_diagnostic()),
        ("blocking-failure", "failed", _failed_diagnostic()),
    ):
        receipt, snapshot, selector_manifest, _assignment = _valid_receipt()
        receipt = deepcopy(receipt)
        receipt["outcome"] = receipt_outcome
        receipt["diagnostics"] = [diagnostic]
        evidence = cast("dict[str, object]", receipt["evidence"])
        capability_results = cast(
            "list[dict[str, object]]", evidence["capability-results"]
        )
        capability_results[1]["outcome"] = receipt_outcome
        capability_results[1]["diagnostics"] = [diagnostic]
        entry = _entry(receipt)
        manifest = freeze_ci_validation_receipt_manifest(
            plan=snapshot.plan,
            entries=[entry],
            created_at=CREATED_AT,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )

        for field, value in (
            ("observed-entry-id", "missing-entry"),
            ("receipt-id", "wrong-receipt"),
            (
                "receipt-artifact-ref",
                f"ci-validation/receipts/{RUN_ID}/{RUN_ATTEMPT}/wrong/receipt.json",
            ),
            ("receipt-content-digest", "0" * 64),
        ):
            tampered = freeze_ci_validation_aggregate(
                plan=snapshot.plan,
                receipt_manifest=manifest,
                selector_assignments_manifest=selector_manifest,
                observed_receipts=[_observed_input(entry, receipt)],
                created_at=CREATED_AT,
                changed_files_snapshot=snapshot.changed_files_snapshot,
                fact_snapshot=snapshot.fact_snapshot,
            )
            result = next(
                item
                for item in cast(
                    "list[dict[str, object]]",
                    tampered["evidence-results"],
                )
                if item["outcome"] == result_outcome
            )
            failure = next(
                item
                for item in cast(
                    "list[dict[str, object]]", tampered["failures"]
                )
                if item["evidence-expectation-id"]
                == result["evidence-expectation-id"]
            )
            result[field] = value
            failure[field] = value
            with pytest.raises(
                ContractValidationError, match="observed receipt"
            ):
                validate_ci_validation_aggregate(
                    tampered,
                    plan=snapshot.plan,
                    receipt_manifest=manifest,
                    changed_files_snapshot=snapshot.changed_files_snapshot,
                    fact_snapshot=snapshot.fact_snapshot,
                )


@pytest.mark.parametrize(
    "verdict_effect",
    [
        DiagnosticVerdictEffect.FAILED.value,
        DiagnosticVerdictEffect.FAIL_CLOSED.value,
    ],
)
def test_satisfied_evidence_result_rejects_verdict_affecting_diagnostics(
    verdict_effect: str,
) -> None:
    """Satisfied evidence diagnostics cannot affect the aggregate verdict."""
    _snapshot, aggregate = _valid_aggregate()
    diagnostic = ci_validation_diagnostic(
        diagnostic_id=f"satisfied/verdict-affecting-{verdict_effect}",
        code=DiagnosticFamily.FINAL_EVIDENCE_FAILURE.value,
        detail=DiagnosticDetail.FINAL_AGGREGATE_MALFORMED.value,
        message="Satisfied evidence carried a verdict-affecting diagnostic",
        source_type="aggregation",
        source_id=None,
        severity=DiagnosticSeverity.BLOCKING_FAILURE.value,
        verdict_effect=verdict_effect,
    )
    result = cast("list[dict[str, object]]", aggregate["evidence-results"])[0]
    result["diagnostics"] = [diagnostic]
    cast("list[dict[str, object]]", aggregate["diagnostics"]).append(diagnostic)
    aggregate["diagnostics"] = sorted(
        cast("list[dict[str, object]]", aggregate["diagnostics"]),
        key=lambda item: str(item["diagnostic-id"]),
    )

    with pytest.raises(ContractValidationError, match="satisfied evidence"):
        validate_ci_validation_aggregate(aggregate)


def test_aggregate_root_diagnostics_cover_referenced_diagnostics() -> None:
    """Root diagnostics are the canonical set referenced by nested records."""
    aggregate = freeze_ci_validation_invalid_plan_aggregate(
        created_at=CREATED_AT,
        repository_owner="hcoona",
        repository_name="three",
        workflow="CI Validation",
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
    )

    omitted = deepcopy(aggregate)
    omitted["diagnostics"] = []
    with pytest.raises(ContractValidationError, match="referenced diagnostics"):
        validate_ci_validation_aggregate(omitted)

    mismatched = deepcopy(aggregate)
    cast("list[dict[str, object]]", mismatched["diagnostics"])[0]["message"] = (
        "tampered diagnostic"
    )
    with pytest.raises(ContractValidationError, match="referenced diagnostics"):
        validate_ci_validation_aggregate(mismatched)

    extra = deepcopy(aggregate)
    extra_diagnostic = deepcopy(
        cast("list[dict[str, object]]", extra["diagnostics"])[0]
    )
    extra_diagnostic["diagnostic-id"] = "invalid-plan/extra"
    cast("list[dict[str, object]]", extra["diagnostics"]).append(
        extra_diagnostic
    )
    with pytest.raises(ContractValidationError, match="referenced diagnostics"):
        validate_ci_validation_aggregate(extra)


def test_failed_descriptor_receipt_preserves_descriptor_diagnostic() -> None:
    """Aggregate failures preserve descriptor-invalid receipt diagnostics."""
    snapshot, selector_manifest, assignment = _specialized_context(
        group=_descriptor_work_group(),
        evidence_expectation=_descriptor_evidence_expectation(),
        descriptor_obligations=[_descriptor_obligation()],
    )
    receipt = _receipt_for_context(
        snapshot, selector_manifest, assignment, _descriptor_receipt_evidence()
    )
    diagnostic = _descriptor_invalid_diagnostic(DESCRIPTOR_WORK_GROUP_ID)
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
    results[0]["diagnostics"] = [diagnostic]
    entry = _entry_for_assignment(receipt, assignment)
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[_observed_input(entry, receipt)],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    failure = next(
        item
        for item in cast("list[dict[str, object]]", aggregate["failures"])
        if item["kind"] == "blocking-validation-failure"
        and item["work-group-id"] == DESCRIPTOR_WORK_GROUP_ID
    )
    assert failure["diagnostic"] == diagnostic
    validate_ci_validation_aggregate(
        aggregate,
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )


def test_failed_artifact_receipt_preserves_artifact_shape_diagnostic() -> None:
    """Aggregate failures preserve artifact-shape-unconfirmed diagnostics."""
    snapshot, selector_manifest, assignment = _specialized_context(
        group=_artifact_work_group(),
        evidence_expectation=_artifact_evidence_expectation(),
        validation_obligations=[_artifact_validation_obligation()],
        artifact_obligations=[_artifact_obligation()],
    )
    receipt = _receipt_for_context(
        snapshot, selector_manifest, assignment, _release_receipt_evidence()
    )
    diagnostic = _artifact_shape_diagnostic(ARTIFACT_WORK_GROUP_ID)
    result = _release_result(receipt)
    artifact = cast("dict[str, object]", result["artifact"])
    observed = cast("dict[str, object]", artifact["observed"])
    digests = cast("list[dict[str, object]]", observed["digests"])
    digests[0]["digest-available"] = False
    digests[0]["digest"] = ""
    digests[0]["diagnostics"] = [diagnostic]
    artifact["outcome"] = "blocking-failure"
    artifact["diagnostics"] = [diagnostic]
    result["outcome"] = "blocking-failure"
    result["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    evidence = cast("dict[str, object]", receipt["evidence"])
    category = cast("dict[str, object]", evidence["category-result"])
    category["outcome"] = "blocking-failure"
    category["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    receipt["outcome"] = "blocking-failure"
    receipt["diagnostics"] = [_failed_diagnostic(ARTIFACT_WORK_GROUP_ID)]
    entry = _entry_for_assignment(receipt, assignment)
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[_observed_input(entry, receipt)],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    failure = next(
        item
        for item in cast("list[dict[str, object]]", aggregate["failures"])
        if item["kind"] == "blocking-validation-failure"
        and item["work-group-id"] == ARTIFACT_WORK_GROUP_ID
    )
    assert failure["diagnostic"] == diagnostic
    validate_ci_validation_aggregate(
        aggregate,
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )


def test_evidence_failures_mirror_receipt_fields_and_diagnostic() -> None:
    """Non-success failures bind receipt fields and diagnostics exactly."""
    receipt, snapshot, selector_manifest, _assignment = _valid_receipt()
    failed_receipt = deepcopy(receipt)
    failed_receipt["outcome"] = "blocking-failure"
    failed_receipt["diagnostics"] = [_failed_diagnostic()]
    failed_evidence = cast("dict[str, object]", failed_receipt["evidence"])
    failed_results = cast(
        "list[dict[str, object]]", failed_evidence["capability-results"]
    )
    failed_results[1]["outcome"] = "blocking-failure"
    failed_results[1]["diagnostics"] = [_failed_diagnostic()]
    entry = _entry(failed_receipt)
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    def aggregate() -> dict[str, object]:
        return freeze_ci_validation_aggregate(
            plan=snapshot.plan,
            receipt_manifest=manifest,
            selector_assignments_manifest=selector_manifest,
            observed_receipts=[_observed_input(entry, failed_receipt)],
            created_at=CREATED_AT,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )

    for field, value in (
        ("receipt-id", "wrong-receipt"),
        (
            "receipt-artifact-ref",
            f"ci-validation/receipts/{RUN_ID}/{RUN_ATTEMPT}/wrong/receipt.json",
        ),
        ("receipt-content-digest", "0" * 64),
    ):
        tampered = aggregate()
        failure = next(
            item
            for item in cast("list[dict[str, object]]", tampered["failures"])
            if item["kind"] == "blocking-validation-failure"
        )
        failure[field] = value
        with pytest.raises(ContractValidationError, match="non-success"):
            validate_ci_validation_aggregate(
                tampered,
                plan=snapshot.plan,
                receipt_manifest=manifest,
                changed_files_snapshot=snapshot.changed_files_snapshot,
                fact_snapshot=snapshot.fact_snapshot,
            )

    tampered = aggregate()
    failure = next(
        item
        for item in cast("list[dict[str, object]]", tampered["failures"])
        if item["kind"] == "blocking-validation-failure"
    )
    diagnostic = deepcopy(cast("dict[str, object]", failure["diagnostic"]))
    diagnostic["diagnostic-id"] = "validation-work-failed/tampered"
    failure["diagnostic"] = diagnostic
    cast("list[dict[str, object]]", tampered["diagnostics"]).append(diagnostic)
    tampered["diagnostics"] = sorted(
        cast("list[dict[str, object]]", tampered["diagnostics"]),
        key=lambda item: str(item["diagnostic-id"]),
    )
    with pytest.raises(ContractValidationError, match="non-success"):
        validate_ci_validation_aggregate(
            tampered,
            plan=snapshot.plan,
            receipt_manifest=manifest,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_inadmissible_failures_mirror_receipt_fields_and_diagnostic() -> None:
    """Inadmissible failures bind receipt fields and diagnostics exactly."""
    receipt, snapshot, selector_manifest, _assignment = _valid_receipt()
    wrong_plan_receipt = deepcopy(receipt)
    wrong_plan_receipt["plan-id"] = "other-plan"
    entry = _entry(wrong_plan_receipt)
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    def aggregate() -> dict[str, object]:
        return freeze_ci_validation_aggregate(
            plan=snapshot.plan,
            receipt_manifest=manifest,
            selector_assignments_manifest=selector_manifest,
            observed_receipts=[_observed_input(entry, wrong_plan_receipt)],
            created_at=CREATED_AT,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )

    for field, value in (
        ("receipt-id", "wrong-receipt"),
        (
            "receipt-artifact-ref",
            f"ci-validation/receipts/{RUN_ID}/{RUN_ATTEMPT}/wrong/receipt.json",
        ),
        ("receipt-content-digest", "0" * 64),
        ("work-group-id", "wrong"),
    ):
        tampered = aggregate()
        failure = next(
            item
            for item in cast("list[dict[str, object]]", tampered["failures"])
            if item["kind"] == "inadmissible-receipt"
        )
        failure[field] = value
        with pytest.raises(
            ContractValidationError, match="inadmissible receipts"
        ):
            validate_ci_validation_aggregate(
                tampered,
                plan=snapshot.plan,
                receipt_manifest=manifest,
                changed_files_snapshot=snapshot.changed_files_snapshot,
                fact_snapshot=snapshot.fact_snapshot,
            )

    tampered = aggregate()
    failure = next(
        item
        for item in cast("list[dict[str, object]]", tampered["failures"])
        if item["kind"] == "inadmissible-receipt"
    )
    diagnostic = deepcopy(cast("dict[str, object]", failure["diagnostic"]))
    diagnostic["diagnostic-id"] = "inadmissible-receipt/tampered/wrong-plan"
    failure["diagnostic"] = diagnostic
    cast("list[dict[str, object]]", tampered["diagnostics"]).append(diagnostic)
    tampered["diagnostics"] = sorted(
        cast("list[dict[str, object]]", tampered["diagnostics"]),
        key=lambda item: str(item["diagnostic-id"]),
    )
    with pytest.raises(ContractValidationError, match="inadmissible receipts"):
        validate_ci_validation_aggregate(
            tampered,
            plan=snapshot.plan,
            receipt_manifest=manifest,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )

    wrong_family = aggregate()
    observed = next(
        item
        for item in cast(
            "list[dict[str, object]]", wrong_family["observed-receipts"]
        )
        if item["admissibility"] == "inadmissible"
    )
    failure = next(
        item
        for item in cast("list[dict[str, object]]", wrong_family["failures"])
        if item["kind"] == "inadmissible-receipt"
    )
    diagnostic = ci_validation_diagnostic(
        diagnostic_id="required-evidence-missing/tampered",
        code=DiagnosticFamily.REQUIRED_EVIDENCE_MISSING.value,
        detail=None,
        message="tampered diagnostic family",
        source_type="aggregation",
        source_id=None,
        severity=DiagnosticSeverity.BLOCKING_FAILURE.value,
        verdict_effect=DiagnosticVerdictEffect.FAILED.value,
    )
    observed["diagnostics"] = [diagnostic]
    failure["diagnostic"] = diagnostic
    wrong_family["diagnostics"] = sorted(
        [
            cast("dict[str, object]", item["diagnostic"])
            for item in cast(
                "list[dict[str, object]]", wrong_family["failures"]
            )
        ],
        key=lambda item: str(item["diagnostic-id"]),
    )
    with pytest.raises(ContractValidationError, match="failure kind"):
        validate_ci_validation_aggregate(
            wrong_family,
            plan=snapshot.plan,
            receipt_manifest=manifest,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_aggregate_rejects_manifest_digest_mismatch() -> None:
    """Aggregate final evidence binds the exact receipt manifest digest."""
    receipt, snapshot, selector_manifest, _assignment = _valid_receipt()
    entry = _entry(receipt)
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[_observed_input(entry, receipt)],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    cast("dict[str, object]", aggregate["receipt-manifest"])[
        "content-digest"
    ] = "0" * 64

    with pytest.raises(ContractValidationError, match="manifest"):
        validate_ci_validation_aggregate(
            aggregate,
            plan=snapshot.plan,
            receipt_manifest=manifest,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_aggregate_validates_supplied_manifest_before_binding() -> None:
    """Validation does not trust a digest-matching invalid manifest."""
    receipt, snapshot, selector_manifest, _assignment = _valid_receipt()
    entry = _entry(receipt)
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[_observed_input(entry, receipt)],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    bad_manifest = deepcopy(manifest)
    bad_manifest["kind"] = "ci-validation-aggregate"
    cast("dict[str, object]", aggregate["receipt-manifest"])[
        "content-digest"
    ] = ci_validation_receipt_manifest_payload_digest(bad_manifest)

    with pytest.raises(ContractValidationError, match="receipt-manifest"):
        validate_ci_validation_aggregate(
            aggregate,
            plan=snapshot.plan,
            receipt_manifest=bad_manifest,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_aggregate_rejects_manifest_with_wrong_run_attempt() -> None:
    """Supplied manifest envelope must match the aggregate run attempt."""
    receipt, snapshot, selector_manifest, _assignment = _valid_receipt()
    entry = _entry(receipt)
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[_observed_input(entry, receipt)],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    bad_manifest = deepcopy(manifest)
    cast("dict[str, object]", bad_manifest["run"])["run-attempt"] = "2"
    cast("dict[str, object]", aggregate["receipt-manifest"])[
        "content-digest"
    ] = ci_validation_receipt_manifest_payload_digest(bad_manifest)

    with pytest.raises(ContractValidationError, match="run attempt"):
        validate_ci_validation_aggregate(
            aggregate,
            plan=snapshot.plan,
            receipt_manifest=bad_manifest,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_aggregate_rejects_manifest_bound_to_wrong_plan() -> None:
    """Supplied manifest plan fields must match the validated plan."""
    receipt, snapshot, selector_manifest, _assignment = _valid_receipt()
    entry = _entry(receipt)
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[_observed_input(entry, receipt)],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    bad_manifest = deepcopy(manifest)
    bad_manifest["plan-id"] = "other-plan"
    cast("dict[str, object]", aggregate["receipt-manifest"])[
        "content-digest"
    ] = ci_validation_receipt_manifest_payload_digest(bad_manifest)

    with pytest.raises(ContractValidationError, match="plan"):
        validate_ci_validation_aggregate(
            aggregate,
            plan=snapshot.plan,
            receipt_manifest=bad_manifest,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_duplicate_unreadable_and_unclassified_entries_fail_closed() -> None:
    """Duplicate and unclassified entries are represented and fail."""
    receipt, snapshot, selector_manifest, _assignment = _valid_receipt()
    duplicate_a = _entry(receipt, instance_id="1001")
    duplicate_b = _entry(receipt, instance_id="1002")
    unclassified = _entry(
        None,
        artifact_ref=None,
        work_group_id=None,
        receipt_id=None,
        instance_id="9999",
    )
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[unclassified, duplicate_b, duplicate_a],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[
            _observed_input(duplicate_b, receipt),
            _observed_input(unclassified, None),
            _observed_input(duplicate_a, receipt),
        ],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    assert (
        cast("dict[str, object]", aggregate["reason"])["inadmissible-receipt"]
        is True
    )
    observed = cast("list[dict[str, object]]", aggregate["observed-receipts"])
    assert [item["observed-entry-id"] for item in observed] == sorted(
        item["observed-entry-id"] for item in observed
    )
    assert any(item["artifact-ref"] is None for item in observed)


def test_terminal_aggregation_receipt_is_inadmissible() -> None:
    """The terminal evidence-aggregation selector never emits receipts."""
    receipt, snapshot, selector_manifest, _assignment = _valid_receipt()
    terminal_ref = ci_validation_receipt_artifact_ref(
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        work_group_id="evidence-aggregation",
    )
    terminal_entry = _entry(
        receipt,
        artifact_ref=terminal_ref,
        work_group_id="evidence-aggregation",
        instance_id="2001",
    )
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[terminal_entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[_observed_input(terminal_entry, receipt)],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    assert (
        cast("dict[str, object]", aggregate["reason"])["inadmissible-receipt"]
        is True
    )
    details = [
        cast("dict[str, object]", item["diagnostic"])["detail"]
        for item in cast("list[dict[str, object]]", aggregate["failures"])
        if item["kind"] == "inadmissible-receipt"
    ]
    assert DiagnosticDetail.UNEXPECTED_RECEIPT.value in details


def test_validate_recomputes_null_ref_receipt_admissibility() -> None:
    """Validation rejects success-shaped null-ref observations."""
    receipt, snapshot, selector_manifest, _assignment = _valid_receipt()
    null_entry = _entry(
        receipt, artifact_ref=None, work_group_id=None, receipt_id=None
    )
    null_entry["receipt-content-digest"] = None
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[null_entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[_observed_input(null_entry, None)],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    observed = cast("list[dict[str, object]]", aggregate["observed-receipts"])
    observed[0]["admissibility"] = "valid"
    observed[0]["diagnostics"] = []
    _remove_inadmissible_receipt_accounting(aggregate)

    with pytest.raises(
        ContractValidationError, match="valid observed receipts"
    ):
        validate_ci_validation_aggregate(
            aggregate,
            plan=snapshot.plan,
            receipt_manifest=manifest,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_validate_recomputes_duplicate_receipt_admissibility() -> None:
    """Validation rejects tampered duplicate valid observations."""
    receipt, snapshot, selector_manifest, _assignment = _valid_receipt()
    duplicate_a = _entry(receipt, instance_id="1001")
    duplicate_b = _entry(receipt, instance_id="1002")
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[duplicate_b, duplicate_a],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[
            _observed_input(duplicate_b, receipt),
            _observed_input(duplicate_a, receipt),
        ],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    for observed in cast(
        "list[dict[str, object]]", aggregate["observed-receipts"]
    ):
        observed["admissibility"] = "valid"
        observed["diagnostics"] = []
    _remove_inadmissible_receipt_accounting(aggregate)

    with pytest.raises(ContractValidationError, match="duplicate valid"):
        validate_ci_validation_aggregate(
            aggregate,
            plan=snapshot.plan,
            receipt_manifest=manifest,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_validate_recomputes_terminal_receipt_admissibility() -> None:
    """Validation rejects terminal observations tampered into valid receipts."""
    receipt, snapshot, selector_manifest, _assignment = _valid_receipt()
    terminal_ref = ci_validation_receipt_artifact_ref(
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        work_group_id="evidence-aggregation",
    )
    terminal_entry = _entry(
        receipt,
        artifact_ref=terminal_ref,
        work_group_id="evidence-aggregation",
        instance_id="2001",
    )
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[terminal_entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[_observed_input(terminal_entry, receipt)],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    observed = cast("list[dict[str, object]]", aggregate["observed-receipts"])[
        0
    ]
    observed["admissibility"] = "valid"
    observed["diagnostics"] = []
    _remove_inadmissible_receipt_accounting(aggregate)

    with pytest.raises(ContractValidationError, match="terminal aggregation"):
        validate_ci_validation_aggregate(
            aggregate,
            plan=snapshot.plan,
            receipt_manifest=manifest,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_validate_rejects_valid_receipt_with_inadmissible_diagnostics() -> None:
    """Validation does not trust valid admissibility over diagnostics."""
    receipt, snapshot, selector_manifest, _assignment = _valid_receipt()
    entry = _entry(receipt)
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[_observed_input(entry, receipt)],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    observed = cast("list[dict[str, object]]", aggregate["observed-receipts"])[
        0
    ]
    observed["diagnostics"] = [
        ci_validation_diagnostic(
            diagnostic_id="inadmissible-receipt/tampered/wrong-plan",
            code=DiagnosticFamily.INADMISSIBLE_RECEIPT.value,
            detail=DiagnosticDetail.WRONG_PLAN.value,
            message="tampered diagnostic",
            source_type="aggregation",
            source_id=None,
            severity=DiagnosticSeverity.BLOCKING_FAILURE.value,
            verdict_effect=DiagnosticVerdictEffect.FAILED.value,
        )
    ]

    with pytest.raises(
        ContractValidationError, match="valid observed receipts"
    ):
        validate_ci_validation_aggregate(
            aggregate,
            plan=snapshot.plan,
            receipt_manifest=manifest,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_aggregate_validation_requires_canonical_failure_order() -> None:
    """Failures are validated with the LLD tuple ordering."""
    aggregate = freeze_ci_validation_invalid_plan_aggregate(
        created_at=CREATED_AT,
        repository_owner="hcoona",
        repository_name="three",
        workflow="CI Validation",
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
    )
    extra = {
        **cast("list[dict[str, object]]", aggregate["failures"])[0],
        "kind": "final-evidence-failure",
        "diagnostic": ci_validation_diagnostic(
            diagnostic_id="final-evidence-failure/final-manifest-missing",
            code=DiagnosticFamily.FINAL_EVIDENCE_FAILURE.value,
            detail=DiagnosticDetail.FINAL_MANIFEST_MISSING.value,
            message="manifest missing",
            source_type="aggregation",
            source_id=None,
            severity=DiagnosticSeverity.BLOCKING_FAILURE.value,
            verdict_effect=DiagnosticVerdictEffect.FAILED.value,
        ),
    }
    cast("list[dict[str, object]]", aggregate["failures"]).append(extra)
    cast("dict[str, object]", aggregate["reason"])["final-evidence-failure"] = (
        True
    )
    aggregate["diagnostics"] = sorted(
        [
            cast("dict[str, object]", item["diagnostic"])
            for item in cast("list[dict[str, object]]", aggregate["failures"])
        ],
        key=lambda item: str(item["diagnostic-id"]),
    )

    with pytest.raises(ContractValidationError, match="sorted"):
        validate_ci_validation_aggregate(aggregate)


def test_sorted_diagnostics_validation_handles_malformed_ids() -> None:
    """Malformed diagnostic ids produce validation errors, not TypeError."""
    aggregate = freeze_ci_validation_invalid_plan_aggregate(
        created_at=CREATED_AT,
        repository_owner="hcoona",
        repository_name="three",
        workflow="CI Validation",
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
    )
    malformed = deepcopy(
        cast("list[dict[str, object]]", aggregate["diagnostics"])[0]
    )
    malformed["diagnostic-id"] = None
    cast("list[dict[str, object]]", aggregate["diagnostics"]).append(malformed)

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate(aggregate)


def test_sorted_evidence_results_validation_handles_malformed_ids() -> None:
    """Malformed evidence-result ids do not cause TypeError."""
    _snapshot, aggregate = _valid_aggregate()
    malformed = deepcopy(
        cast("list[dict[str, object]]", aggregate["evidence-results"])[0]
    )
    malformed["evidence-expectation-id"] = None
    cast("list[dict[str, object]]", aggregate["evidence-results"]).append(
        malformed
    )

    with pytest.raises(ContractValidationError):
        validate_ci_validation_aggregate(aggregate)


@pytest.mark.parametrize(
    ("field", "value", "message"),
    [
        (
            "observed-writer-id",
            ci_validation_writer_id(
                workflow="CI Validation",
                job="ci-validation-selector-tampered",
                matrix={"selector": WORK_GROUP_ID},
            ),
            "observed-writer-id",
        ),
        (
            "trusted-writer-id",
            ci_validation_writer_id(
                workflow="CI Validation",
                job="ci-validation-selector-tampered",
                matrix={"selector": WORK_GROUP_ID},
            ),
            "observed-writer-id",
        ),
        ("assignment-id", "tampered-assignment", "assignment-id"),
        (
            "writer-observation-ref",
            ci_validation_writer_observation_artifact_ref(
                run_id=RUN_ID,
                run_attempt=RUN_ATTEMPT,
                assignment_id="tampered-assignment",
            ),
            "writer-observation-ref",
        ),
    ],
)
def test_aggregate_supplied_manifest_rejects_rebound_writer_binding(
    field: str,
    value: str,
    message: str,
) -> None:
    """Supplied manifests cannot rebind valid receipts to writer identity."""
    receipt, snapshot, selector_manifest, _assignment = _valid_receipt()
    entry = _entry(receipt)
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[_observed_input(entry, receipt)],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    cast("list[dict[str, object]]", manifest["entries"])[0][field] = value
    cast("dict[str, object]", aggregate["receipt-manifest"])[
        "content-digest"
    ] = ci_validation_receipt_manifest_payload_digest(manifest)

    with pytest.raises(ContractValidationError, match=message):
        validate_ci_validation_aggregate(
            aggregate,
            plan=snapshot.plan,
            receipt_manifest=manifest,
            selector_assignments_manifest=selector_manifest,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_aggregate_supplied_manifest_rejects_coherent_rebound_writer_ids() -> (
    None
):
    """Supplied manifests cannot rebind valid receipts to another writer."""
    receipt, snapshot, selector_manifest, _assignment = _valid_receipt()
    entry = _entry(receipt)
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    aggregate = freeze_ci_validation_aggregate(
        plan=snapshot.plan,
        receipt_manifest=manifest,
        selector_assignments_manifest=selector_manifest,
        observed_receipts=[_observed_input(entry, receipt)],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    rebound_writer_id = ci_validation_writer_id(
        workflow="CI Validation",
        job="ci-validation-selector-python",
        matrix={"selector": "rebound-work-group"},
    )
    cast("list[dict[str, object]]", manifest["entries"])[0][
        "trusted-writer-id"
    ] = rebound_writer_id
    cast("list[dict[str, object]]", manifest["entries"])[0][
        "observed-writer-id"
    ] = rebound_writer_id
    cast("dict[str, object]", aggregate["receipt-manifest"])[
        "content-digest"
    ] = ci_validation_receipt_manifest_payload_digest(manifest)

    with pytest.raises(ContractValidationError, match="selector assignment"):
        validate_ci_validation_aggregate(
            aggregate,
            plan=snapshot.plan,
            receipt_manifest=manifest,
            selector_assignments_manifest=selector_manifest,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
        )


def test_freeze_aggregate_rejects_non_final_evidence_diagnostics() -> None:
    """Final evidence failures require final evidence diagnostics."""
    receipt, snapshot, selector_manifest, _assignment = _valid_receipt()
    entry = _entry(receipt)
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    with pytest.raises(ContractValidationError, match="final-evidence"):
        freeze_ci_validation_aggregate(
            plan=snapshot.plan,
            receipt_manifest=manifest,
            selector_assignments_manifest=selector_manifest,
            observed_receipts=[_observed_input(entry, receipt)],
            created_at=CREATED_AT,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
            final_evidence_diagnostics=[
                ci_validation_diagnostic(
                    diagnostic_id="required-evidence-missing/tampered",
                    code=DiagnosticFamily.REQUIRED_EVIDENCE_MISSING.value,
                    detail=None,
                    message="required evidence missing",
                    source_type="aggregation",
                    source_id=None,
                    severity=DiagnosticSeverity.BLOCKING_FAILURE.value,
                    verdict_effect=DiagnosticVerdictEffect.FAILED.value,
                )
            ],
        )


def test_aggregate_rejects_final_evidence_failure_diagnostic_family() -> None:
    """Tampered final-evidence failures cannot cite other families."""
    aggregate = freeze_ci_validation_invalid_plan_aggregate(
        created_at=CREATED_AT,
        repository_owner="hcoona",
        repository_name="three",
        workflow="CI Validation",
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
    )
    failure = cast("list[dict[str, object]]", aggregate["failures"])[0]
    failure["kind"] = "final-evidence-failure"

    with pytest.raises(ContractValidationError, match="final-evidence"):
        validate_ci_validation_aggregate(aggregate)


def test_final_evidence_failure_diagnostic_requires_detail() -> None:
    """Final-evidence failures cannot omit the registered detail."""
    receipt, snapshot, selector_manifest, _assignment = _valid_receipt()
    entry = _entry(receipt)
    manifest = freeze_ci_validation_receipt_manifest(
        plan=snapshot.plan,
        entries=[entry],
        created_at=CREATED_AT,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )

    with pytest.raises(ContractValidationError, match="final-evidence"):
        freeze_ci_validation_aggregate(
            plan=snapshot.plan,
            receipt_manifest=manifest,
            selector_assignments_manifest=selector_manifest,
            observed_receipts=[_observed_input(entry, receipt)],
            created_at=CREATED_AT,
            changed_files_snapshot=snapshot.changed_files_snapshot,
            fact_snapshot=snapshot.fact_snapshot,
            final_evidence_diagnostics=[
                ci_validation_diagnostic(
                    diagnostic_id="final-evidence-failure/tampered",
                    code=DiagnosticFamily.FINAL_EVIDENCE_FAILURE.value,
                    detail=None,
                    message="final evidence failed without detail",
                    source_type="aggregation",
                    source_id=None,
                    severity=DiagnosticSeverity.BLOCKING_FAILURE.value,
                    verdict_effect=DiagnosticVerdictEffect.FAILED.value,
                )
            ],
        )
