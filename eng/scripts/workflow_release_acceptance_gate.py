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
    "test_ci_acceptance_matrix_pins_targeted_invalid_claim_evidence",
    "tests/test_workflow_release_control.py::"
    "test_acceptance_gate_pins_group1_r204_gate_only_regressions",
    "tests/test_workflow_release_control.py::"
    "test_acceptance_gate_pins_r22_release_reconciliation_regressions",
    "tests/test_workflow_release_control.py::"
    "test_acceptance_gate_pins_r23_release_reconciliation_regression",
    "tests/test_workflow_release_control.py::"
    "test_acceptance_gate_pins_r26_r27_release_reconciliation_regressions",
    "tests/test_workflow_release_control.py::"
    "test_acceptance_gate_pins_r32_release_reconciliation_regressions",
    "tests/test_workflow_release_control.py::"
    "test_acceptance_gate_pins_r35_release_planner_regression",
    "tests/test_workflow_release_control.py::"
    "test_acceptance_gate_pins_r36_dotnet_dist_boundary_regression",
    "tests/test_workflow_release_control.py::"
    "test_acceptance_gate_pins_r37_github_release_skip_satisfied_regressions",
    "tests/test_workflow_release_control.py::"
    "test_acceptance_gate_pins_r38_workflow_helper_regression",
    "tests/test_workflow_release_control.py::"
    "test_workflow_helper_invocations_use_uv_workspace_python",
    "tests/test_workflow_release_control.py::"
    "test_acceptance_gate_pins_r39_github_release_coverage_regression",
    "tests/test_workflow_release_control.py::"
    "test_acceptance_gate_pins_r40_observer_regression",
    "tests/test_workflow_release_control.py::"
    "test_acceptance_gate_pins_r41_release_completion_and_buddy_regressions",
    "tests/test_workflow_release_control.py::"
    "test_acceptance_gate_pins_r42_all_skip_completion_receipt_regressions",
    "tests/test_workflow_release_control.py::"
    "test_acceptance_gate_pins_r43_ensure_tag_and_pypi_dependency_regressions",
    "tests/test_workflow_release_control.py::"
    "test_acceptance_gate_pins_r45_final_coverage_only_sidecar_regression",
    "tests/test_workflow_release_control.py::"
    "test_acceptance_gate_pins_r46_r48_release_report_plan_identity_regression",
    "tests/test_workflow_release_control.py::"
    "test_acceptance_gate_pins_r49_release_no_side_effect_regressions",
    "tests/test_workflow_release_control.py::"
    "test_acceptance_gate_pins_r50_executor_boundary_ensure_tag_doc_regression",
    "tests/test_workflow_release_control.py::"
    "test_acceptance_gate_pins_r51_doc_boundary_regressions",
    "tests/test_workflow_release_control.py::"
    "test_acceptance_gate_pins_r52_tag_permission_boundary_regression",
    "tests/test_workflow_release_control.py::"
    "test_acceptance_gate_pins_r53_tag_permission_acceptance_regression",
    "tests/test_workflow_release_control.py::"
    "test_acceptance_gate_pins_r54_hook_trigger_surface_traceability",
    "tests/test_workflow_release_control.py::"
    "test_acceptance_gate_pins_r8_ci_fail_closed_mapping_regressions",
    "tests/test_workflow_release_control.py::"
    "test_ci_validation_release_shaped_object_mapping_ref_mismatch_blocks",
    "tests/test_workflow_release_control.py::"
    "test_ci_validation_release_shaped_unrelated_legacy_mapping_blocks",
    "tests/test_workflow_release_control.py::"
    "test_ci_validation_release_shaped_unrelated_malformed_generated_mapping_blocks",
    "tests/test_workflow_release_control.py::"
    "test_ci_validation_release_shaped_unrelated_malformed_generated_path_blocks",
    "tests/test_workflow_release_control.py::"
    "test_ci_validation_release_shaped_unrelated_generated_path_mismatched_bundle_blocks",
    "tests/test_workflow_release_control.py::"
    "test_ci_validation_release_shaped_expected_generated_source_missing_scope_blocks",
    "tests/test_workflow_release_control.py::"
    "test_ci_validation_release_shaped_identity_only_generated_mapping_blocks",
    "tests/test_workflow_release_control.py::"
    "test_ci_validation_release_shaped_generated_path_outside_scoped_bundle_blocks",
    "tests/test_workflow_release_control.py::"
    "test_ci_validation_release_shaped_generated_source_invalid_identity_blocks",
    "tests/test_workflow_release_control.py::"
    "test_ci_validation_release_shaped_generated_source_invalid_schema_blocks",
    "tests/test_workflow_release_control.py::"
    "test_ci_validation_release_shaped_stale_same_scope_request_mapping_rebuilds",
    "tests/test_workflow_release_control.py::"
    "test_ci_validation_release_shaped_legacy_mapping_to_materialized_path_blocks",
    "tests/test_workflow_release_control.py::"
    "test_ci_validation_release_shaped_empty_explicit_mapping_blocks",
    "tests/test_workflow_release_control.py::"
    "test_ci_validation_release_shaped_rejects_escaping_materialized_receipt_path",
    "tests/test_workflow_release_control.py::"
    "test_ci_validation_release_shaped_materialized_paths_are_scope_isolated",
    "tests/test_workflow_release_control.py::"
    "test_ci_validation_release_shaped_same_runner_work_groups_are_scope_isolated",
    "tests/test_workflow_release_control.py::"
    "test_ci_release_shaped_materialization_groups_keep_runner_scope",
    "tests/test_workflow_release_control.py::"
    "test_ci_validation_release_shaped_materializes_same_runner_plan_mapping",
    "tests/test_workflow_release_control.py::"
    "test_ci_validation_release_shaped_does_not_reuse_cross_runner_mapping",
    "tests/test_workflow_release_control.py::"
    "test_ci_validation_release_shaped_does_not_reuse_cross_work_group_mapping",
    "tests/test_workflow_release_control.py::"
    "test_ci_validation_release_shaped_preserves_satisfied_supplemental_group",
    "tests/test_workflow_release_control.py::"
    "test_ci_batch_aggregation_propagates_malformed_release_mapping_failure",
    "src/public/lib/three-workflow-release-planner/tests/test_planner.py::"
    "test_buddy_mixed_github_release_fails_closed_when_deactivated",
    "src/public/lib/three-workflow-release-planner/tests/test_planner.py::"
    "test_ci_validation_plans_workflow_governance_markdown_as_tooling",
    "src/public/lib/three-workflow-release-contracts/tests/test_contracts.py::"
    "test_successful_release_report_requires_plan_identity",
    "src/public/lib/three-workflow-release-contracts/tests/test_contracts.py::"
    "test_successful_release_report_requires_plan_tuple_when_plan_skipped",
    "src/public/lib/three-workflow-release-contracts/tests/test_contracts.py::"
    "test_publish_request_rejects_skip_satisfied_node",
    "tests/test_workflow_release_control.py::"
    "test_low_level_design_skips_ensure_tag_without_active_github_release",
    "tests/test_workflow_release_control.py::"
    "test_executor_boundary_docs_skip_ensure_tag_for_skip_only_github_release",
    "tests/test_workflow_release_control.py::"
    "test_low_level_design_documents_github_release_attestation_boundaries",
    "tests/test_workflow_release_control.py::"
    "test_low_level_design_scopes_tag_permission_row_to_ensure_tag",
    "tests/test_workflow_release_control.py::"
    "test_publish_request_command_rejects_skip_satisfied_node",
    "tests/test_workflow_release_control.py::"
    "test_github_release_publish_request_rejects_skip_satisfied_asset_"
    "receipt_path",
    "tests/test_workflow_release_control.py::"
    "test_github_release_completion_requires_skip_satisfied_receipt",
    "tests/test_workflow_release_control.py::"
    "test_release_completed_accepts_finalized_all_skip_execution_sets",
    "tests/test_workflow_release_control.py::"
    "test_release_completed_rejects_finalized_all_skip_without_skip_receipts",
    "tests/test_workflow_release_control.py::"
    "test_release_completed_rejects_finalized_all_skip_stale_or_misbound_skip_receipts",
    "tests/test_workflow_release_control.py::"
    "test_ensure_tags_fails_missing_skip_satisfied_tags",
    "tests/test_workflow_release_control.py::"
    "test_ensure_tags_fails_mixed_missing_skip_satisfied_tag",
    "tests/test_workflow_release_control.py::"
    "test_publish_python_allows_skipped_ensure_tag_when_no_active_github_release",
    "tests/test_workflow_release_control.py::"
    "test_release_create_coverage_preflight_rejects_missing_skip_satisfied_sibling",
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
    "test_acceptance_gate_rejects_duplicate_same_column_evidence",
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
    "test_orchestrator_npmjs_publish_uses_trusted_publisher_runtime",
    "tests/test_workflow_release_control.py::"
    "test_active_split_topology_does_not_model_nuget_registry_targets",
    "tests/test_workflow_release_control.py::"
    "test_ci_release_pipeline_architecture_detects_active_split_topology",
    "tests/test_workflow_release_control.py::"
    "test_buddy_entry_is_not_restricted_by_public_release_ref",
    "tests/test_workflow_release_control.py::"
    "test_entry_authorization_requires_profile_scoped_permissions",
    "tests/test_workflow_release_control.py::"
    "test_entry_workflows_authorize_before_write_capable_orchestration",
    "tests/test_workflow_release_control.py::"
    "test_buddy_entry_is_not_restricted_by_public_release_ref",
    "tests/test_workflow_release_control.py::"
    "test_entry_workflows_concurrency_on_canonical_release_identity",
    "tests/test_workflow_release_control.py::"
    "test_official_manual_dispatch_allows_display_name_alias",
    "tests/test_workflow_release_control.py::"
    "test_resolve_project_canonicalizes_display_name_alias",
    "tests/test_workflow_release_control.py::"
    "test_release_resolve_workflow_canonicalizes_project_alias_before_detection",
    "tests/test_workflow_release_control.py::"
    "test_official_tag_push_allows_branch_only_project_spec",
    "tests/test_workflow_release_control.py::"
    "test_official_default_rejects_non_public_release_ref",
    "tests/test_workflow_release_control.py::"
    "test_release_workflows_generate_final_release_reports",
    "tests/test_workflow_release_control.py::"
    "test_release_create_finalization_requires_complete_sidecar_coverage",
    "tests/test_workflow_release_control.py::"
    "test_release_create_finalization_requires_live_coverage_only_sidecars",
    "tests/test_workflow_release_control.py::"
    "test_release_orchestrate_prepares_plan_artifacts_before_ensure_tag",
    "tests/test_workflow_release_control.py::"
    "test_release_orchestrate_ensures_tags_before_publish_fanout",
    "tests/test_workflow_release_control.py::"
    "test_buddy_github_release_deactivation_blocks_publish_handoff",
    "tests/test_workflow_release_control.py::"
    "test_github_release_mixed_same_release_requires_union",
    "tests/test_workflow_release_control.py::"
    "test_observe_remote_publications_mixed_same_release_exact_and_partial",
    "tests/test_workflow_release_control.py::"
    "test_matrix_outputs_filter_disabled_all_node_registry_targets",
    "tests/test_workflow_release_control.py::"
    "test_matrix_outputs_filter_disabled_all_ruby_registry_targets",
    "tests/test_workflow_release_control.py::"
    "test_matrix_outputs_filter_disabled_target_only_variant",
    "tests/test_workflow_release_control.py::"
    "test_release_create_authorization_allows_skip_satisfied_same_release_coverage",
    "tests/test_workflow_release_control.py::"
    "test_release_create_authorization_accepts_mixed_publish_and_skip_satisfied_nodes",
    "tests/test_workflow_release_control.py::"
    "test_github_release_receipt_accepts_skip_satisfied_same_release_sibling",
    "tests/test_workflow_release_control.py::"
    "test_release_create_normalize_allows_skip_satisfied_coverage_without_current_receipt",
    "src/public/lib/three-workflow-release-planner/tests/test_planner.py::"
    "test_github_release_mixed_siblings_keep_skip_satisfied",
    "tests/test_workflow_release_control.py::"
    "test_validate_dotnet_dist_ignores_skip_satisfied_inactive_variant_sibling",
    "tests/test_workflow_release_control.py::"
    "test_release_create_authorization_accepts_non_empty_asset_labels",
    "tests/test_workflow_release_control.py::"
    "test_release_create_authorization_rejects_mismatched_asset_labels",
    "tests/test_workflow_release_control.py::"
    "test_release_create_authorization_rejects_skip_only_node_ids",
    "tests/test_workflow_release_control.py::"
    "test_release_create_workflow_uses_actual_publish_assets_for_mutation",
    "tests/test_workflow_release_control.py::"
    "test_report_ignores_disabled_package_target_for_completion_check",
    "tests/test_workflow_release_control.py::"
    "test_report_counts_skip_results_after_disabled_target_filter",
    "tests/test_workflow_release_control.py::"
    "test_report_accepts_all_registry_disabled_github_release_only",
    "tests/test_workflow_release_control.py::"
    "test_release_completed_accepts_all_registry_disabled_github_release_only",
    "src/public/lib/three-workflow-release-proof/tests/test_proof.py::"
    "test_github_release_asset_proof_accepts_replace_authoritative_release",
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
    "src/public/lib/three-workflow-release-proof/tests/test_proof.py::"
    "test_github_release_exact_requires_build_receipt_evidence",
    "src/public/lib/three-workflow-release-proof/tests/test_proof.py::"
    "test_github_release_asset_proof_requires_publish_request_evidence",
)


def _collect_test_nodeids(document: dict[str, Any]) -> list[str]:
    """Return acceptance evidence pytest nodeids in deterministic order."""
    _validate_evidence_references(document)
    nodeids = set(MANDATORY_TEST_NODEIDS)
    for row in document["rows"]:
        for references in row["evidence"].values():
            for reference in references:
                if reference["type"] == "test":
                    nodeids.add(reference["value"])
    _validate_nodeids(nodeids)
    return sorted(nodeids)


def _validate_evidence_references(document: dict[str, Any]) -> None:
    """Reject duplicate evidence references within one matrix row column."""
    if document.get("kind") not in {
        "workflow-release-acceptance-matrix",
        "workflow-release-ci-validation-acceptance-matrix",
    }:
        return
    for row in document["rows"]:
        row_id = row.get("id", "<unknown>")
        for column, references in row["evidence"].items():
            seen: set[tuple[str, str]] = set()
            duplicates: set[tuple[str, str]] = set()
            for reference in references:
                key = (reference["type"], reference["value"])
                if key in seen:
                    duplicates.add(key)
                seen.add(key)
            if duplicates:
                duplicate_values = sorted(
                    f"{ref_type}:{value}" for ref_type, value in duplicates
                )
                message = (
                    "acceptance matrix contains duplicate evidence "
                    f"references in {row_id!r} {column!r}: "
                    f"{duplicate_values}"
                )
                raise ValueError(message)


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
        for document in documents:
            _validate_evidence_references(document)
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
