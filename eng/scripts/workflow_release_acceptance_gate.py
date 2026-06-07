"""Run pytest nodeids referenced by the workflow-release acceptance matrix."""

from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path
from typing import Any

REPO_ROOT = Path(__file__).parents[2]
MATRIX_PATHS = (
    REPO_ROOT / "tests/fixtures/workflow-release-acceptance-matrix.json",
    REPO_ROOT
    / "tests/fixtures/workflow-release-ci-validation-acceptance-matrix.json",
)
MANDATORY_TEST_NODEIDS = (
    "tests/test_workflow_release_control.py::"
    "test_acceptance_matrix_fixture_tracks_design_scenarios",
    "tests/test_workflow_release_control.py::"
    "test_acceptance_matrix_rows_are_ci_actionable",
    "tests/test_workflow_release_control.py::"
    "test_acceptance_matrix_test_nodeids_are_collected_by_gate",
    "tests/test_workflow_release_control.py::"
    "test_ci_acceptance_matrix_fixture_tracks_lld_scenarios",
    "tests/test_workflow_release_control.py::"
    "test_ci_acceptance_matrix_rows_are_actionable",
    "tests/test_workflow_release_control.py::"
    "test_ci_acceptance_matrix_pins_group1_r204_regressions",
    "tests/test_workflow_release_control.py::"
    "test_acceptance_gate_pins_group1_r204_gate_only_regressions",
    "tests/test_workflow_release_control.py::"
    "test_ci_acceptance_matrix_preserves_no_publish_boundaries",
    "tests/test_workflow_release_control.py::"
    "test_confirmed_scope_descriptor_matrix_matches_current_descriptors",
    "tests/test_workflow_release_control.py::"
    "test_fail_closed_acceptance_rows_match_phase_artifact_contracts",
    "tests/test_workflow_release_control.py::"
    "test_hk_runs_focused_workflow_release_validation",
    "tests/test_workflow_release_control.py::"
    "test_ci_validate_workflow_passes_actionlint_gate",
    "tests/test_workflow_release_control.py::"
    "test_acceptance_gate_rejects_option_like_nodeids_and_uses_separator",
    "tests/test_workflow_release_control.py::"
    "test_aggregate_cli_accepts_false_producer_verified",
    "tests/test_workflow_release_control.py::"
    "test_aggregate_cli_defaults_omitted_producer_verified_to_false",
    "tests/test_workflow_release_control.py::"
    "test_aggregate_namespace_non_bool_producer_verified_fails_closed",
    "tests/test_workflow_release_control.py::"
    "test_aggregate_cli_rejects_malformed_producer_verified",
    "tests/test_workflow_release_control.py::"
    "test_aggregate_cli_rejects_missing_producer_verified_value",
    "tests/test_workflow_release_control.py::"
    "test_ci_validation_workflow_passes_explicit_producer_verified_value",
    "tests/test_workflow_release_control.py::"
    "test_ci_validation_orchestrator_slots_stop_after_completion",
    "tests/test_workflow_release_control.py::"
    "test_ci_orchestrator_wait_returns_promptly_when_all_batches_uploaded",
    "tests/test_workflow_release_control.py::"
    "test_ci_batch_summary_preserves_unverified_for_unbound_manifest",
    "tests/test_workflow_release_control.py::"
    "test_ci_batch_summary_missing_manifest_artifact_id_clears_authority",
    "tests/test_workflow_release_control.py::"
    "test_ci_batch_summary_manifest_control_failure_clears_authority",
    "tests/test_workflow_release_control.py::"
    "test_final_uploaded_byte_gate_preserves_unbound_invalid_plan_manifest",
    "tests/test_workflow_release_control.py::"
    "test_final_uploaded_byte_gate_requires_downloaded_path",
    "tests/test_workflow_release_control.py::"
    "test_final_uploaded_byte_gate_requires_expected_digest",
    "tests/test_workflow_release_control.py::"
    "test_bound_aggregate_manifest_digest_uses_manifest_bytes",
    "tests/test_workflow_release_control.py::"
    "test_final_uploaded_byte_gate_validates_admitted_batch_summary",
    "tests/test_workflow_release_control.py::"
    "test_final_uploaded_byte_gate_rejects_missing_or_tampered_admitted_bundles",
    "tests/test_workflow_release_control.py::"
    "test_ci_batch_request_authority_invalid_before_identity",
    "tests/test_workflow_release_control.py::"
    "test_ci_batch_manifest_unreadable_detail",
    "tests/test_workflow_release_control.py::"
    "test_ci_batch_manifest_explicit_missing_clears_unbound_authority",
    "tests/test_workflow_release_control.py::"
    "test_ci_batch_aggregation_cli_rejects_missing_required_inputs",
    "tests/test_workflow_release_control.py::"
    "test_ci_batch_aggregation_malformed_local_execution_manifest_not_missing",
    "tests/test_workflow_release_control.py::"
    "test_ci_batch_aggregation_fails_closed_for_fail_closed_plan",
    "tests/test_workflow_release_control.py::"
    "test_official_entry_publish_sets_up_npm_trusted_runtime",
    "tests/test_workflow_release_control.py::"
    "test_entry_publish_sets_up_nuget_trusted_publishing",
    "tests/test_workflow_release_control.py::"
    "test_buddy_entry_publish_is_dry_run_gated",
    "tests/test_workflow_release_control.py::"
    "test_reusable_publish_jobs_are_dry_run_gated",
    "tests/test_workflow_release_control.py::"
    "test_skip_only_tag_verification_is_read_only_without_environment",
    "src/public/lib/three-workflow-release-contracts/tests/"
    "test_ci_validation_batches.py::"
    "test_invalid_plan_unbound_manifest_rejects_self_authority_diagnostics",
    "src/public/lib/three-workflow-release-contracts/tests/"
    "test_ci_validation_batches.py::"
    "test_aggregate_summary_rejects_forged_authority_diagnostic_shape",
    "src/public/lib/three-workflow-release-contracts/tests/"
    "test_ci_validation_batches.py::"
    "test_aggregate_manifest_rejects_request_context_projection_conflict",
    "src/public/lib/three-workflow-release-contracts/tests/"
    "test_ci_validation_batches.py::"
    "test_invalid_plan_rejects_retained_projection_with_bound_"
    "snapshot_companion_fallback",
    "src/public/lib/three-workflow-release-contracts/tests/"
    "test_ci_validation_batches.py::"
    "test_invalid_plan_rejects_retained_projection_with_noncanonical_"
    "snapshot_companion_fallback",
    "src/public/lib/three-workflow-release-contracts/tests/"
    "test_ci_validation_batches.py::"
    "test_invalid_plan_rejects_retained_projection_with_forged_"
    "snapshot_companion_fallback",
    "src/public/lib/three-workflow-release-contracts/tests/"
    "test_ci_validation_batches.py::"
    "test_invalid_plan_rejects_retained_projection_with_mismatched_"
    "snapshot_companion_fallback",
    "src/public/lib/three-workflow-release-contracts/tests/"
    "test_ci_validation_batches.py::"
    "test_invalid_plan_rejects_final_producer_verified_manifest",
    "src/public/lib/three-workflow-release-contracts/tests/"
    "test_ci_validation_batches.py::"
    "test_invalid_plan_rejects_self_authorized_final_producer_evidence",
    "src/public/lib/three-workflow-release-contracts/tests/"
    "test_ci_validation_batches.py::"
    "test_release_batch_rejects_blocking_available_malformed_digest",
    "tests/test_workflow_release_control.py::"
    "test_ci_batch_summary_fails_closed_for_non_bool_producer_verified",
    "tests/test_workflow_release_control.py::"
    "test_ci_batch_missing_plan_summary_clears_unbound_authority_failures",
    "tests/test_workflow_release_control.py::"
    "test_verify_ci_validation_artifact_boundaries_allows_final_total_cap",
    "tests/test_workflow_release_control.py::"
    "test_verify_ci_validation_artifact_boundaries_rejects_extra_under_cap",
    "tests/test_workflow_release_control.py::"
    "test_verify_final_uploaded_bytes_rejects_noncanonical_summary",
    "tests/test_workflow_release_control.py::"
    "test_final_uploaded_byte_gate_recomputes_manifest_digest",
    "tests/test_workflow_release_control.py::"
    "test_ci_batch_aggregate_all_does_not_self_verify_unbound_manifest",
    "tests/test_workflow_release_control.py::"
    "test_ci_batch_aggregate_all_false_producer_requires_external_binding",
    "tests/test_workflow_release_control.py::"
    "test_ci_batch_aggregate_all_clears_caller_verified_valid_manifest",
    "tests/test_workflow_release_control.py::"
    "test_final_uploaded_byte_gate_validates_manifest_with_context",
)


def _collect_test_nodeids(document: dict[str, Any]) -> list[str]:
    """Return acceptance evidence pytest nodeids in deterministic order."""
    nodeids = set(MANDATORY_TEST_NODEIDS)
    for row in document["rows"]:
        for references in row["evidence"].values():
            for reference in references:
                if reference["type"] == "test":
                    nodeids.add(reference["value"])
    _validate_nodeids(nodeids)
    return sorted(nodeids)


def _validate_nodeids(nodeids: set[str]) -> None:
    """Reject pytest nodeids that could be parsed as options."""
    invalid = sorted(nodeid for nodeid in nodeids if nodeid.startswith("-"))
    if invalid:
        message = (
            f"acceptance matrix contains option-like test nodeids: {invalid}"
        )
        raise ValueError(message)


def _pytest_command(nodeids: list[str]) -> list[str]:
    """Build the pytest command for acceptance evidence tests."""
    return [
        sys.executable,
        "-m",
        "pytest",
        "--import-mode=importlib",
        "--",
        *nodeids,
    ]


def main() -> int:
    """Run the acceptance evidence pytest nodeids."""
    documents = [
        json.loads(matrix_path.read_text(encoding="utf-8"))
        for matrix_path in MATRIX_PATHS
    ]
    merged_document = {
        "rows": [row for document in documents for row in document["rows"]],
    }
    try:
        nodeids = _collect_test_nodeids(merged_document)
    except ValueError as exc:
        print(str(exc), file=sys.stderr)
        return 2
    return subprocess.run(
        _pytest_command(nodeids),
        cwd=REPO_ROOT,
        check=False,
    ).returncode


if __name__ == "__main__":
    raise SystemExit(main())
