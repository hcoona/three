#!/usr/bin/env python3
# ruff: noqa: E501, PLR2004, PLR0913, SIM115, ARG001, ANN401, FBT001
"""Control-plane helpers for Three workflow release GitHub Actions."""

from __future__ import annotations

import argparse
import fnmatch
import hashlib
import http.client
import json
import os
import re
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request
from collections.abc import Mapping, Sequence
from datetime import UTC, datetime
from pathlib import Path, PurePosixPath
from typing import TYPE_CHECKING, Any, cast

import yaml

_REPO_ROOT = Path(__file__).resolve().parents[2]
for _WORKSPACE_SRC in (
    _REPO_ROOT / "src/public/lib/three-workflow-release-contracts/src",
    _REPO_ROOT / "src/public/lib/three-workflow-release-planner/src",
    _REPO_ROOT / "src/public/lib/three-workflow-release-proof/src",
    _REPO_ROOT / "src/public/lib/three-workflow-release-authoring/src",
):
    if _WORKSPACE_SRC.is_dir():
        sys.path.insert(0, str(_WORKSPACE_SRC))

from three_workflow_release_authoring import (  # noqa: E402
    CATALOG_PATH,
    AuthoringValidationError,
    validate_project_descriptor_document,
    validate_target_catalog_document,
)
from three_workflow_release_contracts import (  # noqa: E402
    API_VERSIONS_BY_KIND,
    DETAILS_BY_DIAGNOSTIC_CODE,
    CiValidationKind,
    CiValidationObservedReceiptInput,
    ContractValidationError,
    DiagnosticDetail,
    DiagnosticFamily,
    DiagnosticSeverity,
    DiagnosticVerdictEffect,
    ReceiptOutcome,
    admit_exactly_one_artifact,
    artifact_physical_name,
    canonical_json_digest,
    ci_validation_aggregate_artifact_ref,
    ci_validation_batch_evidence_bundle_payload_digest,
    ci_validation_changed_files_snapshot_artifact_ref,
    ci_validation_diagnostic,
    ci_validation_execution_batch_manifest_payload_digest,
    ci_validation_fact_snapshot_artifact_ref,
    ci_validation_observed_entry_id,
    ci_validation_plan_artifact_ref,
    ci_validation_planner_diagnostics_artifact_ref,
    ci_validation_receipt_content_digest,
    ci_validation_receipt_manifest_artifact_ref,
    ci_validation_request_artifact_ref,
    ci_validation_request_projection,
    ci_validation_selector_assignments_artifact_ref,
    ci_validation_writer_id,
    collect_artifacts_by_name,
    freeze_ci_validation_aggregate,
    freeze_ci_validation_batch_evidence_bundle,
    freeze_ci_validation_invalid_plan_aggregate,
    freeze_ci_validation_receipt,
    freeze_ci_validation_receipt_manifest,
    freeze_ci_validation_selector_assignments,
    freeze_ci_validation_writer_observation,
    load_ci_validation_receipt_payload,
    validate_ci_validation_batch_evidence_bundle,
    validate_ci_validation_execution_batch_manifest,
    validate_ci_validation_plan,
    validate_ci_validation_receipt,
    validate_ci_validation_request,
    validate_ci_validation_selector_assignments,
    validate_ci_validation_writer_observation,
    validate_contract,
)
from three_workflow_release_contracts.artifact_names import (  # noqa: E402
    ArtifactNameInputs,
    artifact_name,
    github_release_asset_binding_json,
    immutable_binding_json,
)

if TYPE_CHECKING:
    from three_workflow_release_contracts.actions_artifacts import (
        GitHubActionsArtifactMetadata,
    )

Json = dict[str, Any]
_PERMISSION_RANK = {
    "none": 0,
    "read": 1,
    "triage": 2,
    "write": 3,
    "maintain": 4,
    "admin": 5,
}
_CI_LOCAL_ID_RE = re.compile(r"^[a-z0-9][a-z0-9._-]{0,127}$")
_TOPOLOGIES = (
    "github-token",
    "external-oidc-entry-workflow",
    "external-oidc-caller-workflow",
    "external-oidc-reusable-workflow",
)
_CI_WORK_GROUP_WORKFLOW_LAYER_CAPACITY = 3
_REGISTRY_JSON_TIMEOUT_SECONDS = 15
_NUGET_IDENTITY_NUMERIC_PARTS = 3
_NUGET_MAX_NUMERIC_PARTS = 4
_NUGET_VERSION_PART_RE = re.compile(r"^[0-9]+$")
_NUGET_PRERELEASE_PART_RE = re.compile(r"^[0-9A-Za-z-]+$")
_NUGET_BUILD_METADATA_PART_RE = re.compile(r"^[0-9A-Za-z-]+$")
_OFFICIAL_NON_PUBLIC_REF_CANARY_PROJECTS = frozenset(
    {
        "hcoona-release-smoke-dotnet-executable",
        "hcoona-release-smoke-github-packages",
        "hcoona-release-smoke-github-release",
        "hcoona-release-smoke-inno",
        "hcoona-release-smoke-npm",
        "hcoona-release-smoke-npm-dual",
        "hcoona-release-smoke-nuget",
        "hcoona-release-smoke-pypi",
        "hcoona-release-smoke-rubygems",
        "hcoona-release-smoke-wxt",
    }
)
_CI_CONTROL_ARTIFACT_PRODUCERS = {
    "ci-validation/requests": ("normalize-input", "normalize-input"),
    "ci-validation/planning": ("plan", "plan"),
    "ci-validation/assignments": (
        "materialize-work-groups",
        "materialize-work-groups",
    ),
    "ci-validation/manifests": ("aggregate-evidence", "aggregate-evidence"),
    "ci-validation/aggregate": ("aggregate-evidence", "aggregate-evidence"),
}


def main() -> int:
    """Run the requested control-plane helper command."""
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)
    _add_normalize_entry(subparsers)
    _add_artifact_name(subparsers)
    _add_write_request(subparsers)
    _add_write_ci_validation_request(subparsers)
    _add_ci_validation_artifact_refs(subparsers)
    _add_verify_ci_validation_artifact_boundaries(subparsers)
    _add_materialize_ci_work_groups(subparsers)
    _add_check_ci_validation_dependencies(subparsers)
    _add_validate_ci_validation_lightweight_policy(subparsers)
    _add_validate_ci_validation_descriptors(subparsers)
    _add_run_ci_validation_commands(subparsers)
    _add_write_ci_validation_batch_evidence_bundle(subparsers)
    _add_write_ci_validation_receipt(subparsers)
    _add_write_ci_validation_writer_observation(subparsers)
    _add_download_ci_validation_observed_artifacts(subparsers)
    _add_aggregate_ci_evidence(subparsers)
    _add_plan_gate(subparsers)
    _add_matrix_outputs(subparsers)
    _add_entry_publish_handoff(subparsers)
    _add_build_request(subparsers)
    _add_download_publish_inputs(subparsers)
    _add_publish_request(subparsers)
    _add_prepare_attestation(subparsers)
    _add_attach_attestation(subparsers)
    _add_generate_proofs(subparsers)
    _add_skip_results(subparsers)
    _add_observe_remote_publications(subparsers)
    _add_ensure_tags(subparsers)
    _add_report(subparsers)
    args = parser.parse_args()
    return int(args.func(args) or 0)


def _add_normalize_entry(
    subparsers: argparse._SubParsersAction[argparse.ArgumentParser],
) -> None:
    parser = subparsers.add_parser("normalize-entry")
    parser.add_argument(
        "--profile", required=True, choices=("buddy", "official")
    )
    parser.add_argument("--repository", required=True)
    parser.add_argument("--actor", required=True)
    parser.add_argument("--ref", required=True)
    parser.add_argument("--ref-name", required=True)
    parser.add_argument("--ref-type", required=True, choices=("branch", "tag"))
    parser.add_argument("--pinned-sha", required=True)
    parser.add_argument("--requested-project-ids", default="")
    parser.add_argument("--dry-run", required=True)
    parser.add_argument("--validation-build", required=True)
    parser.add_argument("--force", default="false")
    parser.add_argument("--canary-override-non-public-ref", default="false")
    parser.add_argument("--metadata-out", required=True)
    parser.add_argument("--diagnostics-out", required=True)
    parser.add_argument("--github-output")
    parser.set_defaults(func=_cmd_normalize_entry)


def _add_artifact_name(
    subparsers: argparse._SubParsersAction[argparse.ArgumentParser],
) -> None:
    parser = subparsers.add_parser("artifact-name")
    parser.add_argument("--kind", required=True)
    parser.add_argument("--run-id", required=True, type=int)
    parser.add_argument("--attempt", required=True, type=int)
    parser.add_argument("--plan-id")
    parser.add_argument("--variant-id")
    parser.add_argument("--publish-node-id")
    parser.add_argument("--binding-json")
    parser.add_argument("--github-output")
    parser.add_argument("--output-name", default="artifact_name")
    parser.set_defaults(func=_cmd_artifact_name)


def _add_write_request(
    subparsers: argparse._SubParsersAction[argparse.ArgumentParser],
) -> None:
    parser = subparsers.add_parser("write-planner-request")
    parser.add_argument(
        "--profile", required=True, choices=("buddy", "official")
    )
    parser.add_argument("--commit-sha", required=True)
    parser.add_argument("--requested-project-ids-json", required=True)
    parser.add_argument("--force", required=True)
    parser.add_argument("--out", required=True)
    parser.set_defaults(func=_cmd_write_planner_request)


def _add_write_ci_validation_request(
    subparsers: argparse._SubParsersAction[argparse.ArgumentParser],
) -> None:
    parser = subparsers.add_parser("write-ci-validation-request")
    parser.add_argument(
        "--mode",
        required=True,
        choices=("pull_request", "push", "scheduled_full"),
    )
    parser.add_argument("--repository", required=True)
    parser.add_argument("--workflow", required=True)
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--run-attempt", required=True)
    parser.add_argument("--event-name", required=True)
    parser.add_argument("--event-number", default="")
    parser.add_argument("--actor", required=True)
    parser.add_argument("--validation-commit-sha", required=True)
    parser.add_argument("--validation-ref", default="")
    parser.add_argument("--base-sha", default="")
    parser.add_argument("--base-tip-sha", default="")
    parser.add_argument("--head-sha", default="")
    parser.add_argument("--changed-files-json", default="")
    parser.add_argument("--range-status", choices=("available", "unavailable"))
    parser.add_argument("--range-diagnostic-detail", default="missing")
    parser.add_argument("--created-at")
    parser.add_argument("--out", required=True)
    parser.add_argument("--github-output")
    parser.set_defaults(func=_cmd_write_ci_validation_request)


def _add_ci_validation_artifact_refs(
    subparsers: argparse._SubParsersAction[argparse.ArgumentParser],
) -> None:
    parser = subparsers.add_parser("ci-validation-artifact-refs")
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--run-attempt", required=True)
    parser.add_argument("--github-output", required=True)
    parser.set_defaults(func=_cmd_ci_validation_artifact_refs)


def _add_verify_ci_validation_artifact_boundaries(
    subparsers: argparse._SubParsersAction[argparse.ArgumentParser],
) -> None:
    parser = subparsers.add_parser("verify-ci-validation-artifact-boundaries")
    parser.add_argument("--repository", required=True)
    parser.add_argument("--workflow", required=True)
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--run-attempt", required=True)
    parser.add_argument(
        "--expected-artifact",
        action="append",
        default=[],
        help=(
            "JSON object with artifact-ref, artifact-instance-id, "
            "producer-boundary, and producer-job"
        ),
    )
    parser.set_defaults(func=_cmd_verify_ci_validation_artifact_boundaries)


def _add_materialize_ci_work_groups(
    subparsers: argparse._SubParsersAction[argparse.ArgumentParser],
) -> None:
    parser = subparsers.add_parser("materialize-ci-work-groups")
    parser.add_argument("--plan", required=True)
    parser.add_argument("--changed-files-snapshot", default="")
    parser.add_argument("--fact-snapshot", default="")
    parser.add_argument("--workflow", required=True)
    parser.add_argument("--writer-job", required=True)
    parser.add_argument("--created-at")
    parser.add_argument("--assignments-out", required=True)
    parser.add_argument("--github-output")
    parser.set_defaults(func=_cmd_materialize_ci_work_groups)


def _add_aggregate_ci_evidence(
    subparsers: argparse._SubParsersAction[argparse.ArgumentParser],
) -> None:
    parser = subparsers.add_parser("aggregate-ci-evidence")
    parser.add_argument("--repository", required=True)
    parser.add_argument("--workflow", required=True)
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--run-attempt", required=True)
    parser.add_argument("--plan", default="")
    parser.add_argument("--changed-files-snapshot", default="")
    parser.add_argument("--fact-snapshot", default="")
    parser.add_argument("--assignments", default="")
    parser.add_argument("--observed-artifacts-dir", default="")
    parser.add_argument("--expected-request-artifact-id", default=None)
    parser.add_argument("--expected-plan-artifact-id", default=None)
    parser.add_argument(
        "--expected-changed-files-snapshot-artifact-id",
        default=None,
    )
    parser.add_argument("--expected-fact-snapshot-artifact-id", default=None)
    parser.add_argument(
        "--expected-selector-assignments-artifact-id",
        default=None,
    )
    parser.add_argument("--created-at")
    parser.add_argument("--receipt-manifest-out", required=True)
    parser.add_argument("--aggregate-out", required=True)
    parser.add_argument("--github-output")
    parser.set_defaults(func=_cmd_aggregate_ci_evidence)


def _add_download_ci_validation_observed_artifacts(
    subparsers: argparse._SubParsersAction[argparse.ArgumentParser],
) -> None:
    parser = subparsers.add_parser("download-ci-validation-observed-artifacts")
    parser.add_argument("--repository", required=True)
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--assignments", required=True)
    parser.add_argument("--observed-artifacts-dir", required=True)
    parser.add_argument("--github-output")
    parser.set_defaults(func=_cmd_download_ci_validation_observed_artifacts)


def _add_write_ci_validation_receipt(
    subparsers: argparse._SubParsersAction[argparse.ArgumentParser],
) -> None:
    parser = subparsers.add_parser("write-ci-validation-receipt")
    _add_ci_validation_receipt_args(parser)
    parser.set_defaults(func=_cmd_write_ci_validation_receipt)


def _add_ci_validation_receipt_args(
    parser: argparse.ArgumentParser,
) -> None:
    parser.add_argument("--plan", required=True)
    parser.add_argument("--changed-files-snapshot", default="")
    parser.add_argument("--fact-snapshot", default="")
    parser.add_argument("--assignments", required=True)
    parser.add_argument("--work-group-id", required=True)
    parser.add_argument("--matrix-work-group-json", required=True)
    parser.add_argument("--workflow", required=True)
    parser.add_argument("--job", required=True)
    parser.add_argument("--observed-artifacts-dir", default="")
    parser.add_argument("--observed-commit-sha", required=True)
    parser.add_argument("--validation-result", default="")
    parser.add_argument(
        "--validation-outcome",
        choices=("success", "blocking-failure"),
        default="blocking-failure",
    )
    parser.add_argument("--created-at")
    parser.add_argument("--receipt-out", required=True)
    parser.add_argument("--github-output")


def _add_run_ci_validation_commands(
    subparsers: argparse._SubParsersAction[argparse.ArgumentParser],
) -> None:
    parser = subparsers.add_parser("run-ci-validation-commands")
    parser.add_argument("--plan", default="")
    parser.add_argument("--changed-files-snapshot", default="")
    parser.add_argument("--fact-snapshot", default="")
    parser.add_argument("--assignments", default="")
    parser.add_argument("--observed-artifacts-dir", default="")
    parser.add_argument("--observed-commit-sha", default="")
    parser.add_argument("--matrix-work-group-json", required=True)
    parser.add_argument("--repo-root", default=".")
    parser.add_argument("--result-out", required=True)
    parser.add_argument("--github-output")
    parser.set_defaults(func=_cmd_run_ci_validation_commands)


def _add_write_ci_validation_batch_evidence_bundle(
    subparsers: argparse._SubParsersAction[argparse.ArgumentParser],
) -> None:
    parser = subparsers.add_parser("write-ci-validation-batch-evidence-bundle")
    parser.add_argument("--plan", required=True)
    parser.add_argument("--request", required=True)
    parser.add_argument("--execution-batch-manifest", required=True)
    parser.add_argument("--changed-files-snapshot", default="")
    parser.add_argument("--fact-snapshot", default="")
    parser.add_argument("--matrix-row-json", required=True)
    parser.add_argument("--expected-run-id", required=True)
    parser.add_argument("--expected-run-attempt", required=True)
    parser.add_argument("--workflow", required=True)
    parser.add_argument("--job", required=True)
    parser.add_argument("--assignments", default="")
    parser.add_argument("--observed-artifacts-dir", default="")
    parser.add_argument("--observed-commit-sha", required=True)
    parser.add_argument("--validation-result", action="append", default=[])
    parser.add_argument("--dependency-results-json", default="")
    parser.add_argument("--dependency-bundle", action="append", default=[])
    parser.add_argument("--started-at")
    parser.add_argument("--completed-at")
    parser.add_argument("--created-at")
    parser.add_argument("--bundle-out", required=True)
    parser.add_argument("--github-output")
    parser.set_defaults(func=_cmd_write_ci_validation_batch_evidence_bundle)


def _add_validate_ci_validation_lightweight_policy(
    subparsers: argparse._SubParsersAction[argparse.ArgumentParser],
) -> None:
    parser = subparsers.add_parser("validate-ci-validation-lightweight-policy")
    parser.add_argument("--plan", required=True)
    parser.add_argument("--assignments", required=True)
    parser.add_argument("--work-group-id", required=True)
    parser.add_argument("--matrix-work-group-json", required=True)
    parser.set_defaults(func=_cmd_validate_ci_validation_lightweight_policy)


def _add_validate_ci_validation_descriptors(
    subparsers: argparse._SubParsersAction[argparse.ArgumentParser],
) -> None:
    parser = subparsers.add_parser("validate-ci-validation-descriptors")
    parser.add_argument("--plan", required=True)
    parser.add_argument("--work-group-id", required=True)
    parser.add_argument("--repo-root", default=".")
    parser.set_defaults(func=_cmd_validate_ci_validation_descriptors)


def _add_check_ci_validation_dependencies(
    subparsers: argparse._SubParsersAction[argparse.ArgumentParser],
) -> None:
    parser = subparsers.add_parser("check-ci-validation-dependencies")
    parser.add_argument("--plan", required=True)
    parser.add_argument("--changed-files-snapshot", default="")
    parser.add_argument("--fact-snapshot", default="")
    parser.add_argument("--assignments", required=True)
    parser.add_argument("--work-group-id", required=True)
    parser.add_argument("--observed-artifacts-dir", default="")
    parser.add_argument("--github-output")
    parser.set_defaults(func=_cmd_check_ci_validation_dependencies)


def _add_write_ci_validation_writer_observation(
    subparsers: argparse._SubParsersAction[argparse.ArgumentParser],
) -> None:
    parser = subparsers.add_parser("write-ci-validation-writer-observation")
    parser.add_argument("--plan", required=True)
    parser.add_argument("--changed-files-snapshot", default="")
    parser.add_argument("--fact-snapshot", default="")
    parser.add_argument("--assignments", required=True)
    parser.add_argument("--work-group-id", required=True)
    parser.add_argument("--matrix-work-group-json", required=True)
    parser.add_argument("--workflow", required=True)
    parser.add_argument("--job", required=True)
    parser.add_argument("--artifact-instance-id", required=True)
    parser.add_argument("--created-at")
    parser.add_argument("--observation-out", required=True)
    parser.add_argument("--metadata-out", default="")
    parser.add_argument("--github-output")
    parser.set_defaults(func=_cmd_write_ci_validation_writer_observation)


def _add_plan_gate(
    subparsers: argparse._SubParsersAction[argparse.ArgumentParser],
) -> None:
    parser = subparsers.add_parser("plan-gate")
    parser.add_argument("--plan", required=True)
    parser.add_argument("--execution-sets", required=True)
    parser.add_argument("--remote-observations")
    parser.add_argument("--enabled-external-oidc-targets", default="")
    parser.add_argument("--diagnostics-out", required=True)
    parser.set_defaults(func=_cmd_plan_gate)


def _add_matrix_outputs(
    subparsers: argparse._SubParsersAction[argparse.ArgumentParser],
) -> None:
    parser = subparsers.add_parser("matrix-outputs")
    parser.add_argument("--plan", required=True)
    parser.add_argument("--execution-sets", required=True)
    parser.add_argument("--run-id", required=True, type=int)
    parser.add_argument("--attempt", required=True, type=int)
    parser.add_argument("--github-output", required=True)
    parser.set_defaults(func=_cmd_matrix_outputs)


def _add_entry_publish_handoff(
    subparsers: argparse._SubParsersAction[argparse.ArgumentParser],
) -> None:
    parser = subparsers.add_parser("entry-publish-handoff")
    parser.add_argument("--plan", required=True)
    parser.add_argument("--execution-sets", required=True)
    parser.add_argument("--run-id", required=True, type=int)
    parser.add_argument("--attempt", required=True, type=int)
    parser.add_argument("--out", required=True)
    parser.set_defaults(func=_cmd_entry_publish_handoff)


def _add_build_request(
    subparsers: argparse._SubParsersAction[argparse.ArgumentParser],
) -> None:
    parser = subparsers.add_parser("build-request")
    parser.add_argument("--plan", required=True)
    parser.add_argument("--variant-id", required=True)
    parser.add_argument("--out", required=True)
    parser.set_defaults(func=_cmd_build_request)


def _add_download_publish_inputs(
    subparsers: argparse._SubParsersAction[argparse.ArgumentParser],
) -> None:
    parser = subparsers.add_parser("download-publish-inputs")
    parser.add_argument("--plan", required=True)
    parser.add_argument("--publish-node-id", required=True)
    parser.add_argument("--run-id", required=True, type=int)
    parser.add_argument("--attempt", required=True, type=int)
    parser.add_argument("--repository", required=True)
    parser.add_argument("--build-results-dir", required=True)
    parser.add_argument("--bundles-dir", required=True)
    parser.add_argument("--handoff", default="")
    parser.set_defaults(func=_cmd_download_publish_inputs)


def _add_publish_request(
    subparsers: argparse._SubParsersAction[argparse.ArgumentParser],
) -> None:
    parser = subparsers.add_parser("publish-request")
    parser.add_argument("--plan", required=True)
    parser.add_argument("--publish-node-id", required=True)
    parser.add_argument("--run-id", required=True, type=int)
    parser.add_argument("--attempt", required=True, type=int)
    parser.add_argument("--build-results-dir", required=True)
    parser.add_argument("--bundles-dir", required=True)
    parser.add_argument("--handoff", default="")
    parser.add_argument("--out", required=True)
    parser.set_defaults(func=_cmd_publish_request)


def _add_prepare_attestation(
    subparsers: argparse._SubParsersAction[argparse.ArgumentParser],
) -> None:
    parser = subparsers.add_parser("prepare-attestation")
    parser.add_argument("--publish-request", required=True)
    parser.add_argument("--checksums-out", required=True)
    parser.add_argument("--artifact-ids-out", required=True)
    parser.add_argument("--github-output", required=True)
    parser.set_defaults(func=_cmd_prepare_attestation)


def _add_attach_attestation(
    subparsers: argparse._SubParsersAction[argparse.ArgumentParser],
) -> None:
    parser = subparsers.add_parser("attach-attestation")
    parser.add_argument("--publish-request", required=True)
    parser.add_argument("--artifact-ids", required=True)
    parser.add_argument("--attestation-id", required=True)
    parser.add_argument("--attestation-url", required=True)
    parser.add_argument("--bundle-path", required=True)
    parser.add_argument("--storage-record-ids", default="")
    parser.add_argument("--out", required=True)
    parser.set_defaults(func=_cmd_attach_attestation)


def _add_generate_proofs(
    subparsers: argparse._SubParsersAction[argparse.ArgumentParser],
) -> None:
    parser = subparsers.add_parser("generate-proofs")
    parser.add_argument("--plan", required=True)
    parser.add_argument("--publish-request", required=True)
    parser.add_argument("--publish-result", required=True)
    parser.add_argument("--publish-node-id", required=True)
    parser.add_argument("--run-id", required=True, type=int)
    parser.add_argument("--attempt", required=True, type=int)
    parser.add_argument("--repository", required=True)
    parser.add_argument("--workflow", required=True)
    parser.add_argument("--head-sha", required=True)
    parser.add_argument("--build-results-dir", required=True)
    parser.add_argument("--out-dir", required=True)
    parser.add_argument("--manifest-out", required=True)
    parser.add_argument("--artifact-ids-json", default="")
    parser.add_argument("--github-output", required=True)
    parser.set_defaults(func=_cmd_generate_proofs)


def _add_skip_results(
    subparsers: argparse._SubParsersAction[argparse.ArgumentParser],
) -> None:
    parser = subparsers.add_parser("skip-results")
    parser.add_argument("--plan", required=True)
    parser.add_argument("--execution-sets", required=True)
    parser.add_argument("--run-id", required=True, type=int)
    parser.add_argument("--attempt", required=True, type=int)
    parser.add_argument("--out-dir", required=True)
    parser.add_argument("--manifest-out", required=True)
    parser.set_defaults(func=_cmd_skip_results)


def _add_ensure_tags(
    subparsers: argparse._SubParsersAction[argparse.ArgumentParser],
) -> None:
    parser = subparsers.add_parser("ensure-tags")
    parser.add_argument("--plan", required=True)
    parser.add_argument("--execution-sets", required=True)
    parser.add_argument("--repository", required=True)
    parser.add_argument("--out", required=True)
    parser.set_defaults(func=_cmd_ensure_tags)


def _add_observe_remote_publications(
    subparsers: argparse._SubParsersAction[argparse.ArgumentParser],
) -> None:
    parser = subparsers.add_parser("observe-remote-publications")
    parser.add_argument("--plan", required=True)
    parser.add_argument("--execution-sets")
    parser.add_argument("--enabled-external-oidc-targets", default="")
    parser.add_argument("--canary-override-non-public-ref", default="false")
    parser.add_argument("--release-environment", default="")
    parser.add_argument("--repository", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--diagnostics-out", required=True)
    parser.set_defaults(func=_cmd_observe_remote_publications)


def _add_report(
    subparsers: argparse._SubParsersAction[argparse.ArgumentParser],
) -> None:
    parser = subparsers.add_parser("report")
    parser.add_argument("--repository", required=True)
    parser.add_argument("--workflow", required=True)
    parser.add_argument("--run-id", required=True, type=int)
    parser.add_argument("--attempt", required=True, type=int)
    parser.add_argument("--head-sha", required=True)
    parser.add_argument(
        "--profile", required=True, choices=("buddy", "official")
    )
    parser.add_argument("--dry-run", required=True)
    parser.add_argument("--validation-build", required=True)
    parser.add_argument("--canary-override-non-public-ref", default="false")
    parser.add_argument("--out", required=True)
    parser.add_argument("--plan", default="")
    parser.add_argument("--execution-sets", default="")
    parser.add_argument("--diagnostics", default="")
    parser.add_argument("--artifacts-root", default="")
    parser.add_argument("--authorize-conclusion", default="skipped")
    parser.add_argument("--validate-conclusion", default="skipped")
    parser.add_argument("--metadata-conclusion", default="skipped")
    parser.add_argument("--plan-conclusion", default="skipped")
    parser.add_argument("--build-conclusion", default="skipped")
    parser.add_argument("--tag-conclusion", default="skipped")
    parser.add_argument("--publish-conclusion", default="skipped")
    parser.set_defaults(func=_cmd_report)


def _cmd_normalize_entry(args: argparse.Namespace) -> int:
    diagnostics: list[Json] = []
    dry_run = _parse_bool(args.dry_run)
    validation_build = _parse_bool(args.validation_build)
    force = _parse_bool(args.force)
    canary_override = _parse_bool(args.canary_override_non_public_ref)
    if validation_build and not dry_run:
        diagnostics.append(
            _diagnostic(
                "REQ_INVALID_INPUT",
                "validation",
                "request",
                "validation-build is valid only when dry-run is true",
                {"input": "validation-build"},
            )
        )
    if args.profile == "official" and force:
        diagnostics.append(
            _diagnostic(
                "REQ_FORCE_FOR_OFFICIAL",
                "validation",
                "request",
                "force is not valid for official releases",
                {},
            )
        )
    requested = _normalize_project_ids(args.requested_project_ids)
    if args.profile == "official":
        diagnostics.extend(
            _official_public_ref_diagnostics(
                args.ref,
                requested,
                canary_override=canary_override,
            )
        )
    elif canary_override:
        diagnostics.append(
            _diagnostic(
                "REQ_INVALID_INPUT",
                "validation",
                "request",
                "canary non-public-ref override is valid only for official releases",
                {"input": "canary-override-non-public-ref"},
            )
        )
    permission = _actor_permission(args.repository, args.actor)
    required = "maintain" if args.profile == "official" else "write"
    if _PERMISSION_RANK.get(permission, -1) < _PERMISSION_RANK[required]:
        diagnostics.append(
            _diagnostic(
                "REQ_ACTOR_UNAUTHORIZED",
                "validation",
                "request",
                f"actor {args.actor!r} has {permission!r} permission, below required {required!r}",
                {
                    "actor": args.actor,
                    "permission": permission,
                    "required": required,
                },
            )
        )
    resolved_sha, peel_details = _resolve_ref(
        args.repository, args.ref_type, args.ref_name
    )
    if resolved_sha is None:
        diagnostics.append(
            _diagnostic(
                "REQ_UNTRUSTED_WORKFLOW_REF",
                "validation",
                "request",
                "selected ref could not be resolved to a commit",
                {"ref": args.ref, **peel_details},
            )
        )
    elif not _trusted_ref(args.repository, args.ref_type, args.ref_name):
        diagnostics.append(
            _diagnostic(
                "REQ_UNTRUSTED_WORKFLOW_REF",
                "validation",
                "request",
                "selected workflow ref is not a trusted release ref",
                {
                    "ref": args.ref,
                    "ref-type": args.ref_type,
                    "ref-name": args.ref_name,
                },
            )
        )
    if diagnostics:
        document = _diagnostics_document(diagnostics)
        _write_json(Path(args.diagnostics_out), document)
        _write_outputs(args.github_output, {"authorized": "false"})
        return 1
    metadata = {
        "api-version": "three.release.entry-metadata/v1alpha1",
        "kind": "entry-metadata",
        "profile": args.profile,
        "repository": args.repository,
        "actor": args.actor,
        "ref": args.ref,
        "ref-name": args.ref_name,
        "ref-type": args.ref_type,
        "commit-sha": args.pinned_sha,
        "requested-project-ids": requested,
        "dry-run": dry_run,
        "validation-build": validation_build,
        "force": force,
        "canary-override-non-public-ref": canary_override,
    }
    _write_json(Path(args.metadata_out), metadata)
    _write_outputs(
        args.github_output,
        {
            "authorized": "true",
            "commit_sha": args.pinned_sha,
            "requested_project_ids_json": json.dumps(
                requested, separators=(",", ":")
            ),
            "dry_run": _bool_str(dry_run),
            "validation_build": _bool_str(validation_build),
            "force": _bool_str(force),
            "canary_override_non_public_ref": _bool_str(canary_override),
        },
    )
    return 0


def _cmd_artifact_name(args: argparse.Namespace) -> int:
    name = artifact_name(
        args.kind,
        ArtifactNameInputs(
            run_id=args.run_id,
            attempt=args.attempt,
            plan_id=args.plan_id,
            variant_id=args.variant_id,
            publish_node_id=args.publish_node_id,
            binding_json=args.binding_json,
        ),
    )
    _write_outputs(args.github_output, {args.output_name: name})
    if not args.github_output:
        print(name)
    return 0


def _cmd_write_planner_request(args: argparse.Namespace) -> int:
    requested = json.loads(args.requested_project_ids_json)
    document = {
        "api-version": "three.release.planner-request/v1alpha1",
        "kind": "planner-request",
        "profile": args.profile,
        "commit-sha": args.commit_sha,
        "requested-project-ids": requested,
        "request-flags": {"force": _parse_bool(args.force)},
    }
    validate_contract(document)
    _write_json(Path(args.out), document)
    return 0


def _cmd_write_ci_validation_request(args: argparse.Namespace) -> int:
    owner, name = _split_repository(args.repository)
    run_id = str(args.run_id)
    run_attempt = str(args.run_attempt)
    request: Json = {
        "api-version": API_VERSIONS_BY_KIND[CiValidationKind.REQUEST.value],
        "kind": CiValidationKind.REQUEST.value,
        "created-at": args.created_at or _utc_now(),
        "repository": {"owner": owner, "name": name},
        "run": {
            "workflow": args.workflow,
            "run-id": run_id,
            "run-attempt": run_attempt,
        },
        "schema-diagnostics": [],
        "artifact-ref": ci_validation_request_artifact_ref(
            run_id=run_id,
            run_attempt=run_attempt,
        ),
        "request-digest": "0" * 64,
        "mode": args.mode,
        "validation-tree": {
            "commit-sha": args.validation_commit_sha,
            "ref": args.validation_ref or None,
        },
        "event": {
            "name": args.event_name,
            "number": args.event_number or None,
            "actor": args.actor,
            "run-id": run_id,
            "run-attempt": run_attempt,
        },
    }
    if args.mode == "scheduled_full":
        request["scheduled-full"] = {"enabled": True}
    else:
        request["affected-range"] = _ci_affected_range(args)
    request["request-digest"] = canonical_json_digest(
        ci_validation_request_projection(request),
    )
    validate_ci_validation_request(
        request,
        expected_run_id=run_id,
        expected_run_attempt=run_attempt,
    )
    _write_json(Path(args.out), request)
    _write_outputs(
        args.github_output,
        {
            "request_artifact_ref": str(request["artifact-ref"]),
            "request_digest": str(request["request-digest"]),
        },
    )
    return 0


def _cmd_ci_validation_artifact_refs(args: argparse.Namespace) -> int:
    run_id = str(args.run_id)
    run_attempt = str(args.run_attempt)
    _write_outputs(
        args.github_output,
        {
            "request_artifact_name": artifact_physical_name(
                ci_validation_request_artifact_ref(
                    run_id=run_id,
                    run_attempt=run_attempt,
                )
            ),
            "plan_artifact_name": artifact_physical_name(
                ci_validation_plan_artifact_ref(
                    run_id=run_id,
                    run_attempt=run_attempt,
                )
            ),
            "planner_diagnostics_artifact_name": artifact_physical_name(
                ci_validation_planner_diagnostics_artifact_ref(
                    run_id=run_id,
                    run_attempt=run_attempt,
                )
            ),
            "changed_files_snapshot_artifact_name": artifact_physical_name(
                ci_validation_changed_files_snapshot_artifact_ref(
                    run_id=run_id,
                    run_attempt=run_attempt,
                )
            ),
            "fact_snapshot_artifact_name": artifact_physical_name(
                ci_validation_fact_snapshot_artifact_ref(
                    run_id=run_id,
                    run_attempt=run_attempt,
                )
            ),
            "selector_assignments_artifact_name": artifact_physical_name(
                ci_validation_selector_assignments_artifact_ref(
                    run_id=run_id,
                    run_attempt=run_attempt,
                )
            ),
            "receipt_manifest_artifact_name": artifact_physical_name(
                ci_validation_receipt_manifest_artifact_ref(
                    run_id=run_id,
                    run_attempt=run_attempt,
                )
            ),
            "aggregate_artifact_name": artifact_physical_name(
                ci_validation_aggregate_artifact_ref(
                    run_id=run_id,
                    run_attempt=run_attempt,
                )
            ),
        },
    )
    return 0


def _cmd_verify_ci_validation_artifact_boundaries(
    args: argparse.Namespace,
) -> int:
    try:
        expected = [
            _ci_expected_artifact_from_json(value)
            for value in args.expected_artifact
        ]
        artifacts = _github_actions_run_artifacts(
            repository=args.repository,
            run_id=str(args.run_id),
        )
        diagnostics = _ci_verify_expected_artifact_producer_boundaries(
            artifacts=artifacts,
            expected_artifacts=expected,
            workflow=args.workflow,
            run_id=str(args.run_id),
            run_attempt=str(args.run_attempt),
        )
    except (
        ContractValidationError,
        RuntimeError,
        TypeError,
        ValueError,
    ) as exc:
        print(str(exc), file=sys.stderr)
        return 1
    if diagnostics:
        for diagnostic in diagnostics:
            print(str(diagnostic["message"]), file=sys.stderr)
        return 1
    return 0


def _ci_expected_artifact_from_json(value: str) -> Json:
    payload = _read_json_value(value)
    if not isinstance(payload, dict):
        msg = "expected artifact must be a JSON object"
        raise TypeError(msg)
    return payload


def _ci_verify_expected_artifact_producer_boundaries(
    *,
    artifacts: Sequence[GitHubActionsArtifactMetadata | Mapping[str, object]],
    expected_artifacts: Sequence[Mapping[str, object]],
    workflow: str,
    run_id: str,
    run_attempt: str,
) -> list[Mapping[str, object]]:
    groups = collect_artifacts_by_name(artifacts)
    diagnostics: list[Mapping[str, object]] = []
    for index, expected in _ci_expected_artifacts_requiring_boundary_check(
        expected_artifacts
    ):
        artifact_ref = expected.get("artifact-ref")
        artifact_instance_id = expected.get("artifact-instance-id")
        producer_boundary = expected.get("producer-boundary")
        producer_job = expected.get("producer-job")
        source_id = str(artifact_ref) if isinstance(artifact_ref, str) else None
        if not isinstance(artifact_ref, str) or not artifact_ref:
            diagnostics.append(
                _ci_boundary_diagnostic(
                    index=index,
                    detail=DiagnosticDetail.STRUCTURALLY_INVALID.value,
                    message="expected artifact ref is missing or malformed",
                    source_id=source_id,
                )
            )
            continue
        expected_boundary, expected_job = (
            _ci_expected_control_artifact_producer(artifact_ref)
        )
        if expected_boundary is None or expected_job is None:
            diagnostics.append(
                _ci_boundary_diagnostic(
                    index=index,
                    detail=DiagnosticDetail.STRUCTURALLY_INVALID.value,
                    message=(
                        "artifact ref is not a registered CI control-plane "
                        "gating artifact"
                    ),
                    source_id=source_id,
                )
            )
            continue
        if (
            producer_boundary != expected_boundary
            or producer_job != expected_job
            or not isinstance(workflow, str)
            or not workflow
        ):
            diagnostics.append(
                _ci_boundary_diagnostic(
                    index=index,
                    detail=_ci_producer_unverified_detail(artifact_ref),
                    message=(
                        "artifact producer boundary does not match the "
                        "contract boundary identity map"
                    ),
                    source_id=source_id,
                )
            )
            continue
        try:
            admission = admit_exactly_one_artifact(
                groups,
                logical_ref=artifact_ref,
            )
        except ContractValidationError as exc:
            diagnostics.append(
                _ci_boundary_diagnostic(
                    index=index,
                    detail=_ci_count_failure_detail(artifact_ref, exc),
                    message=str(exc),
                    source_id=source_id,
                )
            )
            continue
        if (
            not isinstance(artifact_instance_id, str)
            or not artifact_instance_id
        ):
            diagnostics.append(
                _ci_boundary_diagnostic(
                    index=index,
                    detail=_ci_producer_unverified_detail(artifact_ref),
                    message="expected artifact instance id is missing",
                    source_id=source_id,
                )
            )
            continue
        if str(admission.artifact.artifact_id) != artifact_instance_id:
            diagnostics.append(
                _ci_boundary_diagnostic(
                    index=index,
                    detail=_ci_producer_unverified_detail(artifact_ref),
                    message=(
                        "enumerated artifact instance does not match the "
                        "producer job upload output"
                    ),
                    source_id=source_id,
                )
            )
            continue
        if str(admission.artifact.workflow_run_id) != str(run_id):
            diagnostics.append(
                _ci_boundary_diagnostic(
                    index=index,
                    detail=_ci_producer_unverified_detail(artifact_ref),
                    message="artifact metadata does not bind to this workflow run",
                    source_id=source_id,
                )
            )
            continue
        if f"/{run_id}/{run_attempt}/" not in artifact_ref:
            diagnostics.append(
                _ci_boundary_diagnostic(
                    index=index,
                    detail=_ci_producer_unverified_detail(artifact_ref),
                    message="artifact ref does not bind to this run attempt",
                    source_id=source_id,
                )
            )
    return diagnostics


def _ci_expected_artifacts_requiring_boundary_check(
    expected_artifacts: Sequence[Mapping[str, object]],
) -> list[tuple[int, Mapping[str, object]]]:
    checked: list[tuple[int, Mapping[str, object]]] = []
    for index, expected in enumerate(expected_artifacts):
        artifact_ref = expected.get("artifact-ref")
        artifact_instance_id = expected.get("artifact-instance-id")
        if (
            isinstance(artifact_ref, str)
            and _ci_is_optional_control_artifact_ref(artifact_ref)
            and (
                artifact_instance_id is None or str(artifact_instance_id) == ""
            )
        ):
            continue
        checked.append((index, expected))
    return checked


def _ci_is_optional_control_artifact_ref(artifact_ref: str) -> bool:
    return _ci_control_artifact_kind(artifact_ref) in {
        "changed-files-snapshot",
        "fact-snapshot",
    }


def _ci_expected_control_artifact_producer(
    artifact_ref: str,
) -> tuple[str | None, str | None]:
    for prefix, producer in _CI_CONTROL_ARTIFACT_PRODUCERS.items():
        if artifact_ref.startswith(prefix + "/"):
            return producer
    return None, None


def _ci_boundary_diagnostic(
    *,
    index: int,
    detail: str,
    message: str,
    source_id: str | None,
) -> Mapping[str, object]:
    code = DiagnosticFamily.INVALID_PLAN.value
    if detail.startswith("request-"):
        code = DiagnosticFamily.REQUEST_INVALID.value
    elif detail.startswith("final-") or detail == (
        DiagnosticDetail.AGGREGATE_WITHOUT_MANIFEST.value
    ):
        code = DiagnosticFamily.FINAL_EVIDENCE_FAILURE.value
    return ci_validation_diagnostic(
        diagnostic_id=f"producer-boundary/{index:03d}",
        code=code,
        detail=detail,
        message=message,
        source_type="aggregation",
        source_id=source_id,
        severity=DiagnosticSeverity.FAIL_CLOSED.value,
        verdict_effect=DiagnosticVerdictEffect.FAIL_CLOSED.value,
    )


def _ci_count_failure_detail(
    artifact_ref: str,
    exc: ContractValidationError,
) -> str:
    message = str(exc)
    kind = _ci_control_artifact_kind(artifact_ref)
    if "duplicate candidate" in message:
        return {
            "request": DiagnosticDetail.REQUEST_DUPLICATE.value,
            "plan": DiagnosticDetail.PLAN_DUPLICATE.value,
            "changed-files-snapshot": (
                DiagnosticDetail.CHANGED_FILES_SNAPSHOT_DUPLICATE.value
            ),
            "fact-snapshot": DiagnosticDetail.FACT_SNAPSHOT_DUPLICATE.value,
            "selector-assignment": (
                DiagnosticDetail.SELECTOR_ASSIGNMENT_DUPLICATE.value
            ),
            "final-manifest": DiagnosticDetail.FINAL_MANIFEST_DUPLICATE.value,
            "final-aggregate": DiagnosticDetail.FINAL_AGGREGATE_DUPLICATE.value,
        }.get(kind, DiagnosticDetail.STRUCTURALLY_INVALID.value)
    if "missing candidate" in message:
        return {
            "request": DiagnosticDetail.REQUEST_MISSING.value,
            "plan": DiagnosticDetail.PLAN_MISSING.value,
            "changed-files-snapshot": (
                DiagnosticDetail.CHANGED_FILES_SNAPSHOT_MISSING.value
            ),
            "fact-snapshot": DiagnosticDetail.FACT_SNAPSHOT_MISSING.value,
            "selector-assignment": (
                DiagnosticDetail.SELECTOR_ASSIGNMENT_MISSING.value
            ),
            "final-manifest": DiagnosticDetail.FINAL_MANIFEST_MISSING.value,
            "final-aggregate": DiagnosticDetail.FINAL_AGGREGATE_MISSING.value,
        }.get(kind, DiagnosticDetail.STRUCTURALLY_INVALID.value)
    return _ci_producer_unverified_detail(artifact_ref)


def _ci_producer_unverified_detail(artifact_ref: str) -> str:
    kind = _ci_control_artifact_kind(artifact_ref)
    return {
        "request": DiagnosticDetail.REQUEST_PRODUCER_UNVERIFIED.value,
        "plan": DiagnosticDetail.PLAN_PRODUCER_UNVERIFIED.value,
        "changed-files-snapshot": (
            DiagnosticDetail.CHANGED_FILES_SNAPSHOT_PRODUCER_UNVERIFIED.value
        ),
        "fact-snapshot": DiagnosticDetail.FACT_SNAPSHOT_PRODUCER_UNVERIFIED.value,
        "selector-assignment": (
            DiagnosticDetail.SELECTOR_ASSIGNMENT_PRODUCER_UNVERIFIED.value
        ),
        "final-manifest": DiagnosticDetail.FINAL_PRODUCER_UNVERIFIED.value,
        "final-aggregate": DiagnosticDetail.FINAL_PRODUCER_UNVERIFIED.value,
    }.get(kind, DiagnosticDetail.STRUCTURALLY_INVALID.value)


def _ci_control_artifact_kind(artifact_ref: str) -> str:
    suffixes = {
        "/ci-validation-request.json": "request",
        "/validation-plan.json": "plan",
        "/changed-files-snapshot.json": "changed-files-snapshot",
        "/fact-snapshot.json": "fact-snapshot",
        "/selector-assignments.json": "selector-assignment",
        "/receipt-manifest.json": "final-manifest",
        "/ci-validation-aggregate.json": "final-aggregate",
    }
    return next(
        (
            kind
            for suffix, kind in suffixes.items()
            if artifact_ref.endswith(suffix)
        ),
        "unknown",
    )


def _cmd_materialize_ci_work_groups(args: argparse.Namespace) -> int:
    plan = _read_json(Path(args.plan))
    changed_files_snapshot = _read_optional_json(args.changed_files_snapshot)
    fact_snapshot = _read_optional_json(args.fact_snapshot)
    layer_by_work_group = _ci_work_group_dependency_layers(plan)
    matrix = [
        _ci_work_group_matrix_entry(
            plan,
            group,
            dependency_layer=layer_by_work_group[str(group["work-group-id"])],
            writer_job=(
                f"{args.writer_job}-layer-"
                f"{layer_by_work_group[str(group['work-group-id'])]}"
            ),
        )
        for group in _ci_executable_work_groups(plan)
    ]
    trusted_writer_ids = {
        str(entry["work-group-id"]): ci_validation_writer_id(
            workflow=args.workflow,
            job=str(entry["writer-job"]),
            matrix={"work-group": entry},
        )
        for entry in matrix
    }
    assignments = freeze_ci_validation_selector_assignments(
        plan=plan,
        trusted_writer_ids=trusted_writer_ids,
        created_at=args.created_at or _utc_now(),
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
    )
    _write_json(Path(args.assignments_out), assignments)
    work_group_ids = [str(entry["work-group-id"]) for entry in matrix]
    layers = _ci_work_group_matrix_layers(matrix)
    if len(layers) > _CI_WORK_GROUP_WORKFLOW_LAYER_CAPACITY:
        msg = (
            "CI validation plan requires "
            f"{len(layers)} dependency layers, but workflow materialization "
            f"supports {_CI_WORK_GROUP_WORKFLOW_LAYER_CAPACITY}"
        )
        raise RuntimeError(msg)
    layer_outputs: dict[str, str] = {}
    for index in range(_CI_WORK_GROUP_WORKFLOW_LAYER_CAPACITY):
        layer = layers[index] if index < len(layers) else []
        layer_outputs[f"has_work_group_layer_{index}"] = _bool_str(bool(layer))
        layer_outputs[f"work_group_layer_{index}_matrix"] = json.dumps(
            layer,
            separators=(",", ":"),
        )
    _write_outputs(
        args.github_output,
        {
            "has_work_groups": _bool_str(bool(matrix)),
            "work_group_matrix": json.dumps(matrix, separators=(",", ":")),
            "work_group_layers": json.dumps(layers, separators=(",", ":")),
            "work_group_ids": json.dumps(work_group_ids, separators=(",", ":")),
            **layer_outputs,
        },
    )
    return 0


def _cmd_run_ci_validation_commands(args: argparse.Namespace) -> int:
    matrix_work_group = _read_json_value(args.matrix_work_group_json)
    if not isinstance(matrix_work_group, Mapping):
        msg = "matrix work group must be a JSON object"
        raise TypeError(msg)
    plan = _read_optional_json(getattr(args, "plan", ""))
    assignments = _read_optional_json(getattr(args, "assignments", ""))
    changed_files_snapshot = _read_optional_json(
        getattr(args, "changed_files_snapshot", "")
    )
    fact_snapshot = _read_optional_json(getattr(args, "fact_snapshot", ""))
    commands = matrix_work_group.get("validation-commands")
    if not isinstance(commands, Sequence) or isinstance(commands, str | bytes):
        commands = []
    command_results: list[Json] = []
    outcome: ReceiptOutcome = "success"
    for index, command in enumerate(commands):
        result = _ci_run_validation_command(
            index=index,
            command=command,
            plan=plan,
            assignments=assignments,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            observed_artifacts_dir=getattr(args, "observed_artifacts_dir", ""),
            observed_commit_sha=getattr(args, "observed_commit_sha", ""),
            matrix_work_group=matrix_work_group,
            repo_root=Path(args.repo_root),
        )
        if result["outcome"] != "success":
            outcome = "blocking-failure"
        command_results.append(result)
    if not commands:
        outcome = "blocking-failure"
        command_results.append(
            {
                "index": 0,
                "label": "execution-mapping",
                "argv": [],
                "capability": None,
                "exit-code": None,
                "outcome": "blocking-failure",
                "error": "no no-publish validation command is mapped",
            }
        )
    result = {
        "work-group-id": matrix_work_group.get("work-group-id"),
        "kind": matrix_work_group.get("kind"),
        "runner-family": matrix_work_group.get("runner-family"),
        "coverage-target": matrix_work_group.get("coverage-target"),
        "observed-commit-sha": getattr(args, "observed_commit_sha", "") or None,
        "outcome": outcome,
        "commands": command_results,
    }
    _write_json(Path(args.result_out), result)
    _write_outputs(
        args.github_output,
        {
            "validation_outcome": outcome,
            "validation_command_count": str(len(command_results)),
        },
    )
    return 0


def _cmd_check_ci_validation_dependencies(args: argparse.Namespace) -> int:
    plan = _read_json(Path(args.plan))
    assignments = _read_json(Path(args.assignments))
    changed_files_snapshot = _read_optional_json(args.changed_files_snapshot)
    fact_snapshot = _read_optional_json(args.fact_snapshot)
    dependency_blocked = _ci_dependency_blocked(
        plan=plan,
        assignments=assignments,
        work_group_id=args.work_group_id,
        observed_artifacts_dir=getattr(args, "observed_artifacts_dir", ""),
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
    )
    _write_outputs(
        args.github_output,
        {"dependency_blocked": _bool_str(dependency_blocked)},
    )
    return 0


def _cmd_validate_ci_validation_lightweight_policy(
    args: argparse.Namespace,
) -> int:
    try:
        plan = _read_json(Path(args.plan))
        assignments = _read_json(Path(args.assignments))
        matrix_work_group = _read_json_value(args.matrix_work_group_json)
        _require_mapping(matrix_work_group, "matrix work group")
        _validate_ci_validation_lightweight_policy(
            plan=plan,
            assignments=assignments,
            work_group_id=args.work_group_id,
            matrix_work_group=cast("Mapping[str, object]", matrix_work_group),
        )
    except (KeyError, TypeError, ValueError) as exc:
        print(str(exc), file=sys.stderr)
        return 1
    return 0


def _cmd_validate_ci_validation_descriptors(args: argparse.Namespace) -> int:
    try:
        plan = _read_json(Path(args.plan))
        _validate_scoped_descriptor_obligations(
            plan=plan,
            work_group_id=args.work_group_id,
            repo_root=Path(args.repo_root),
        )
    except (
        AuthoringValidationError,
        KeyError,
        OSError,
        TypeError,
        ValueError,
        yaml.YAMLError,
    ) as exc:
        print(str(exc), file=sys.stderr)
        return 1
    return 0


def _cmd_write_ci_validation_batch_evidence_bundle(
    args: argparse.Namespace,
) -> int:
    plan = _read_json(Path(args.plan))
    request = _read_json(Path(args.request))
    execution_batch_manifest = _read_json(Path(args.execution_batch_manifest))
    changed_files_snapshot = _read_optional_json(args.changed_files_snapshot)
    fact_snapshot = _read_optional_json(args.fact_snapshot)
    assignments = _read_optional_json(getattr(args, "assignments", ""))
    observed_artifacts_dir = getattr(args, "observed_artifacts_dir", "")
    matrix_row = _read_json_value(args.matrix_row_json)
    if not isinstance(matrix_row, Mapping):
        msg = "execution-batch matrix row must be a JSON object"
        raise TypeError(msg)
    validate_ci_validation_execution_batch_manifest(
        execution_batch_manifest,
        plan=plan,
        request=request,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        expected_run_id=args.expected_run_id,
        expected_run_attempt=args.expected_run_attempt,
        authorizing=True,
    )
    batch = _ci_execution_batch_from_matrix_row(
        execution_batch_manifest,
        matrix_row,
    )
    validation_results = _ci_validation_results_by_work_group(
        getattr(args, "validation_result", []),
    )
    dependency_results = _ci_batch_dependency_results_by_work_group(
        getattr(args, "dependency_results_json", ""),
    )
    authoritative_dependency_bundles = _ci_authoritative_dependency_bundles(
        getattr(args, "dependency_bundle", []),
        plan=plan,
        request=request,
        execution_batch_manifest=execution_batch_manifest,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        expected_run_id=args.expected_run_id,
        expected_run_attempt=args.expected_run_attempt,
    )
    authoritative_dependency_results = _ci_authoritative_dependency_results(
        authoritative_dependency_bundles,
    )
    selectors = _ci_batch_ordered_selectors(batch)
    selected_work_group_ids = {
        str(selector["work-group-id"]) for selector in selectors
    }
    _reject_off_batch_result_keys(
        validation_results,
        selected_work_group_ids,
        "validation",
    )
    _reject_off_batch_result_keys(
        dependency_results,
        selected_work_group_ids,
        "dependency",
    )
    now = args.created_at or _utc_now()
    selector_results: list[Json] = []
    prior_selector_outcomes: dict[str, str] = {}
    for selector in selectors:
        selector_result = _ci_batch_selector_result(
            plan=plan,
            execution_batch_manifest=execution_batch_manifest,
            batch=batch,
            selector=selector,
            validation_result=validation_results.get(
                str(selector["work-group-id"])
            ),
            dependency_results=dependency_results.get(
                str(selector["work-group-id"]),
                [],
            ),
            authoritative_dependency_results=authoritative_dependency_results,
            prior_selector_outcomes=prior_selector_outcomes,
            observed_commit_sha=args.observed_commit_sha,
            assignments=assignments
            if isinstance(assignments, Mapping)
            else None,
            observed_artifacts_dir=observed_artifacts_dir,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
        )
        selector_results.append(selector_result)
        prior_selector_outcomes[str(selector["work-group-id"])] = str(
            selector_result["outcome"]
        )
    writer = _ci_batch_bundle_writer(
        execution_batch_manifest=execution_batch_manifest,
        batch=batch,
        matrix_row=matrix_row,
        workflow=args.workflow,
        job=args.job,
    )
    bundle = freeze_ci_validation_batch_evidence_bundle(
        plan=plan,
        execution_batch_manifest=execution_batch_manifest,
        batch_id=str(batch["batch-id"]),
        selector_results=selector_results,
        writer=writer,
        execution_tree={
            "observed-commit-sha": args.observed_commit_sha,
            "source": "execution-batch-boundary",
            "verified": _ci_batch_execution_tree_verified(
                plan,
                args.observed_commit_sha,
            ),
        },
        started_at=args.started_at or now,
        completed_at=args.completed_at or now,
        created_at=now,
        request=request,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        expected_run_id=args.expected_run_id,
        expected_run_attempt=args.expected_run_attempt,
        dependency_evidence_bundles=authoritative_dependency_bundles,
    )
    validate_ci_validation_batch_evidence_bundle(
        bundle,
        plan=plan,
        request=request,
        execution_batch_manifest=execution_batch_manifest,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        expected_run_id=args.expected_run_id,
        expected_run_attempt=args.expected_run_attempt,
        dependency_evidence_bundles=authoritative_dependency_bundles,
    )
    _write_json(Path(args.bundle_out), bundle)
    artifact_ref = str(bundle["artifact-ref"])
    _write_outputs(
        args.github_output,
        {
            "batch_id": str(batch["batch-id"]),
            "batch_evidence_bundle_ref": artifact_ref,
            "batch_evidence_bundle_artifact_name": artifact_physical_name(
                artifact_ref
            ),
            "batch_evidence_bundle_payload_digest": (
                ci_validation_batch_evidence_bundle_payload_digest(bundle)
            ),
            "execution_batch_manifest_payload_digest": (
                ci_validation_execution_batch_manifest_payload_digest(
                    execution_batch_manifest
                )
            ),
            "observed_writer_id": str(writer["expected-job-identity"]),
        },
    )
    return 0


def _ci_execution_batch_from_matrix_row(
    execution_batch_manifest: Mapping[str, object],
    matrix_row: Mapping[str, object],
) -> Mapping[str, object]:
    identity = matrix_row.get("identity-matrix")
    if not isinstance(identity, Mapping):
        msg = "execution-batch matrix row is missing identity-matrix"
        raise TypeError(msg)
    batch_id = identity.get("batch-id")
    if not isinstance(batch_id, str) or not batch_id:
        msg = "execution-batch matrix row is missing batch id"
        raise ValueError(msg)
    for batch in _ci_execution_batches(execution_batch_manifest):
        if batch.get("batch-id") != batch_id:
            continue
        expected_identity = _ci_execution_batch_matrix_identity(batch)
        if dict(identity) != expected_identity:
            msg = "execution-batch matrix row identity does not match manifest"
            raise RuntimeError(msg)
        for key, value in expected_identity.items():
            if matrix_row.get(key) != value:
                msg = "execution-batch matrix row projection does not match identity"
                raise RuntimeError(msg)
        expected_writer = _ci_batch_expected_writer_id(
            execution_batch_manifest,
            batch,
        )
        if matrix_row.get("expected-job-identity") != expected_writer:
            msg = "execution-batch matrix row writer identity does not match manifest"
            raise RuntimeError(msg)
        return batch
    msg = "execution-batch matrix row batch id does not exist in manifest"
    raise RuntimeError(msg)


def _ci_execution_batches(
    execution_batch_manifest: Mapping[str, object],
) -> list[Mapping[str, object]]:
    batches = execution_batch_manifest.get("batches")
    if not isinstance(batches, Sequence) or isinstance(batches, str | bytes):
        msg = "execution-batch manifest batches must be an array"
        raise TypeError(msg)
    return [batch for batch in batches if isinstance(batch, Mapping)]


def _ci_execution_batch_matrix_identity(
    batch: Mapping[str, object],
) -> Json:
    return {
        "batch-id": str(batch["batch-id"]),
        "runner-family": str(batch["runner-family"]),
        "expected-batch-evidence-bundle-ref": str(
            batch["expected-batch-evidence-bundle-ref"]
        ),
    }


def _ci_batch_expected_writer_id(
    execution_batch_manifest: Mapping[str, object],
    batch: Mapping[str, object],
) -> str:
    writer = batch.get("batch-writer")
    if not isinstance(writer, Mapping):
        msg = "execution batch is missing batch-writer"
        raise TypeError(msg)
    expected = writer.get("expected-job-identity")
    if not isinstance(expected, str) or not expected:
        msg = "execution batch is missing expected writer identity"
        raise ValueError(msg)
    return expected


def _ci_validation_results_by_work_group(
    paths: Sequence[str],
) -> dict[str, Json]:
    results: dict[str, Json] = {}
    for value in paths:
        result = _read_json(Path(value))
        work_group_id = result.get("work-group-id")
        if not isinstance(work_group_id, str) or not work_group_id:
            msg = f"validation result {value!r} is missing work-group-id"
            raise ValueError(msg)
        if work_group_id in results:
            msg = (
                f"duplicate validation result for work group {work_group_id!r}"
            )
            raise ValueError(msg)
        results[work_group_id] = result
    return results


def _ci_batch_dependency_results_by_work_group(
    value: str,
) -> dict[str, list[Json]]:
    if not value:
        return {}
    parsed = _read_json_value(value)
    if isinstance(parsed, Mapping):
        result: dict[str, list[Json]] = {}
        for work_group_id, rows in parsed.items():
            if not isinstance(work_group_id, str):
                msg = "dependency result keys must be work group ids"
                raise TypeError(msg)
            result[work_group_id] = _ci_batch_dependency_result_rows(rows)
        return result
    rows = _ci_batch_dependency_result_rows(parsed)
    grouped: dict[str, list[Json]] = {}
    for row in rows:
        dependent = row.get("dependent-work-group-id")
        if not isinstance(dependent, str) or not dependent:
            msg = (
                "flat dependency results require dependent-work-group-id on "
                "each row"
            )
            raise ValueError(msg)
        grouped.setdefault(dependent, []).append(row)
    return grouped


def _ci_batch_dependency_result_rows(value: object) -> list[Json]:
    if not isinstance(value, Sequence) or isinstance(value, str | bytes):
        msg = "dependency results must be an array"
        raise TypeError(msg)
    rows: list[Json] = []
    for item in value:
        if not isinstance(item, Mapping):
            msg = "dependency result rows must be objects"
            raise TypeError(msg)
        rows.append(dict(item))
    return rows


def _reject_off_batch_result_keys(
    results: Mapping[str, object],
    selected_work_group_ids: set[str],
    result_kind: str,
) -> None:
    extra = set(results) - selected_work_group_ids
    if extra:
        msg = (
            f"{result_kind} results include unselected work groups "
            f"{sorted(extra)!r}"
        )
        raise RuntimeError(msg)


def _ci_batch_ordered_selectors(
    batch: Mapping[str, object],
) -> list[Mapping[str, object]]:
    selectors = batch.get("ordered-selectors")
    if not isinstance(selectors, Sequence) or isinstance(
        selectors, str | bytes
    ):
        msg = "execution batch ordered-selectors must be an array"
        raise TypeError(msg)
    return [selector for selector in selectors if isinstance(selector, Mapping)]


def _ci_batch_selector_result(
    *,
    plan: Mapping[str, object],
    execution_batch_manifest: Mapping[str, object],
    batch: Mapping[str, object],
    selector: Mapping[str, object],
    validation_result: Mapping[str, object] | None,
    dependency_results: Sequence[Mapping[str, object]],
    authoritative_dependency_results: Mapping[str, Mapping[str, object]],
    prior_selector_outcomes: Mapping[str, str],
    observed_commit_sha: str,
    assignments: Mapping[str, object] | None = None,
    observed_artifacts_dir: str = "",
    changed_files_snapshot: Mapping[str, object] | None = None,
    fact_snapshot: Mapping[str, object] | None = None,
) -> Json:
    slot = selector.get("expected-evidence-slot")
    if not isinstance(slot, Mapping):
        msg = "execution batch selector is missing expected evidence slot"
        raise TypeError(msg)
    work_group_id = str(selector["work-group-id"])
    normalized_dependencies = _ci_batch_normalized_dependency_results(
        selector=selector,
        execution_batch_manifest=execution_batch_manifest,
        current_batch_id=str(batch["batch-id"]),
        dependency_results=dependency_results,
        authoritative_dependency_results=authoritative_dependency_results,
        prior_selector_outcomes=prior_selector_outcomes,
    )
    dependency_blocked = any(
        item["admitted-for-gating"] is not True
        for item in normalized_dependencies
    )
    outcome = _ci_validation_outcome(
        plan,
        work_group_id,
        dependency_blocked=dependency_blocked,
        validation_result=validation_result,
        assignments=assignments,
        observed_artifacts_dir=observed_artifacts_dir,
        observed_commit_sha=observed_commit_sha,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
    )
    diagnostics = _ci_validation_diagnostics(
        plan,
        work_group_id,
        outcome=outcome,
    )
    return {
        "work-group-id": work_group_id,
        "selector-index": selector["selector-index"],
        "expected-evidence-id": selector["expected-evidence-id"],
        "expected-evidence-slot-digest": canonical_json_digest(slot),
        "mode": plan["mode"],
        "validation-tree": dict(
            cast("Mapping[str, object]", plan["validation-tree"])
        ),
        "affected-range": _ci_batch_summary_affected_range(plan),
        "scheduled-full": dict(
            cast("Mapping[str, object]", plan["scheduled-full"])
        ),
        "coverage-target": slot["coverage-target"],
        "ecosystem": slot["ecosystem"],
        "runner-family": slot["runner-family"],
        "selector-variant": slot["selector-variant"],
        "depends-on": list(cast("Sequence[object]", selector["depends-on"])),
        "dependency-results": normalized_dependencies,
        "outcome": outcome,
        "skip-reason": "dependency-blocked" if dependency_blocked else None,
        "evidence": _ci_validation_evidence(
            plan,
            work_group_id,
            outcome=outcome,
            diagnostics=diagnostics,
            validation_result=validation_result,
            fact_snapshot=fact_snapshot,
            batch_bundle=True,
        ),
        "diagnostics": diagnostics,
        "proof-admissibility": "validation-only",
    }


def _ci_batch_normalized_dependency_results(
    *,
    selector: Mapping[str, object],
    execution_batch_manifest: Mapping[str, object],
    current_batch_id: str,
    dependency_results: Sequence[Mapping[str, object]],
    authoritative_dependency_results: Mapping[str, Mapping[str, object]],
    prior_selector_outcomes: Mapping[str, str],
) -> list[Json]:
    depends_on = [
        str(item)
        for item in cast("Sequence[object]", selector.get("depends-on", []))
    ]
    rows_by_work_group = _ci_batch_dependency_results_by_upstream(
        dependency_results
    )
    normalized: list[Json] = []
    positions = _ci_execution_batch_positions(execution_batch_manifest)
    upstream_dependency_ids: set[str] = set()
    for work_group_id in depends_on:
        source_batch_id = positions.get(work_group_id)
        if source_batch_id == current_batch_id:
            prior_outcome = prior_selector_outcomes.get(work_group_id)
            if prior_outcome is None:
                msg = (
                    f"same-batch dependency result for work group "
                    f"{work_group_id!r} is unavailable from prior selectors"
                )
                raise RuntimeError(msg)
            outcome = _ci_same_batch_dependency_outcome(prior_outcome)
            admitted = _ci_selector_outcome_admitted_for_gating(prior_outcome)
            normalized.append(
                {
                    "work-group-id": work_group_id,
                    "source-batch-id": source_batch_id,
                    "outcome": outcome,
                    "admitted-for-gating": admitted,
                }
            )
            continue
        upstream_dependency_ids.add(work_group_id)
        if not isinstance(source_batch_id, str) or not source_batch_id:
            msg = f"dependency source batch for {work_group_id!r} is required"
            raise RuntimeError(msg)
        row = authoritative_dependency_results.get(work_group_id)
        if row is None:
            normalized.append(
                {
                    "work-group-id": work_group_id,
                    "source-batch-id": source_batch_id,
                    "outcome": "missing",
                    "admitted-for-gating": False,
                }
            )
            continue
        row_source_batch_id = row.get("source-batch-id")
        if row_source_batch_id != source_batch_id:
            msg = (
                f"authoritative dependency source for {work_group_id!r} "
                "does not match execution batch manifest"
            )
            raise RuntimeError(msg)
        outcome = row.get("outcome")
        if outcome not in {"satisfied", "missing", "skipped", "failed"}:
            msg = f"dependency outcome for {work_group_id!r} is not registered"
            raise RuntimeError(msg)
        admitted = row.get("admitted-for-gating")
        if not isinstance(admitted, bool):
            msg = f"dependency admission for {work_group_id!r} must be boolean"
            raise TypeError(msg)
        normalized.append(
            {
                "work-group-id": work_group_id,
                "source-batch-id": source_batch_id,
                "outcome": outcome,
                "admitted-for-gating": admitted,
            }
        )
    extra = set(rows_by_work_group) - upstream_dependency_ids
    if extra:
        msg = f"unexpected dependency results for {sorted(extra)!r}"
        raise RuntimeError(msg)
    return normalized


def _ci_authoritative_dependency_bundles(
    paths: Sequence[str],
    *,
    plan: Mapping[str, object],
    request: Mapping[str, object],
    execution_batch_manifest: Mapping[str, object],
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    expected_run_id: str,
    expected_run_attempt: str,
) -> list[Mapping[str, object]]:
    pending: list[tuple[str, Mapping[str, object]]] = []
    for value in paths:
        try:
            bundle = _read_json(Path(value))
        except (OSError, ValueError, TypeError, RuntimeError) as exc:
            msg = f"invalid dependency bundle {value!r}: {exc}"
            raise RuntimeError(msg) from exc
        pending.append((value, bundle))
    bundles: list[Mapping[str, object]] = []
    last_errors: dict[str, ContractValidationError] = {}
    while pending:
        next_pending: list[tuple[str, Mapping[str, object]]] = []
        progressed = False
        for value, bundle in pending:
            try:
                validate_ci_validation_batch_evidence_bundle(
                    bundle,
                    plan=plan,
                    request=request,
                    execution_batch_manifest=execution_batch_manifest,
                    changed_files_snapshot=changed_files_snapshot,
                    fact_snapshot=fact_snapshot,
                    expected_run_id=expected_run_id,
                    expected_run_attempt=expected_run_attempt,
                    dependency_evidence_bundles=bundles,
                )
            except ContractValidationError as exc:
                last_errors[value] = exc
                next_pending.append((value, bundle))
                continue
            bundles.append(bundle)
            progressed = True
        if not next_pending:
            break
        if not progressed:
            value, _bundle = next_pending[0]
            issue = last_errors[value].issues[0]
            msg = (
                f"invalid dependency bundle {value!r}: "
                f"{issue.path} {issue.message}"
            )
            raise RuntimeError(msg) from last_errors[value]
        pending = next_pending
    return bundles


def _ci_authoritative_dependency_results(
    bundles: Sequence[Mapping[str, object]],
) -> dict[str, Mapping[str, object]]:
    results: dict[str, Mapping[str, object]] = {}
    for bundle in bundles:
        batch = bundle.get("batch")
        if not isinstance(batch, Mapping):
            msg = "dependency bundle is missing batch"
            raise TypeError(msg)
        batch_id = batch.get("batch-id")
        if not isinstance(batch_id, str) or not batch_id:
            msg = "dependency bundle is missing batch id"
            raise ValueError(msg)
        selector_results = bundle.get("selector-results")
        if not isinstance(selector_results, Sequence) or isinstance(
            selector_results, str | bytes
        ):
            msg = "dependency bundle selector results must be an array"
            raise TypeError(msg)
        for selector_result in selector_results:
            if not isinstance(selector_result, Mapping):
                msg = "dependency bundle selector result must be an object"
                raise TypeError(msg)
            work_group_id = selector_result.get("work-group-id")
            if not isinstance(work_group_id, str) or not work_group_id:
                msg = (
                    "dependency bundle selector result is missing work-group-id"
                )
                raise ValueError(msg)
            if work_group_id in results:
                msg = (
                    f"duplicate authoritative dependency for {work_group_id!r}"
                )
                raise RuntimeError(msg)
            outcome = str(selector_result.get("outcome"))
            results[work_group_id] = {
                "work-group-id": work_group_id,
                "source-batch-id": batch_id,
                "outcome": _ci_same_batch_dependency_outcome(outcome),
                "admitted-for-gating": _ci_selector_outcome_admitted_for_gating(
                    outcome
                ),
            }
    return results


def _ci_batch_dependency_results_by_upstream(
    dependency_results: Sequence[Mapping[str, object]],
) -> dict[str, Mapping[str, object]]:
    rows_by_work_group: dict[str, Mapping[str, object]] = {}
    for item in dependency_results:
        work_group_id = item.get("work-group-id")
        if not isinstance(work_group_id, str) or not work_group_id:
            msg = "dependency result rows must include work-group-id"
            raise ValueError(msg)
        if work_group_id in rows_by_work_group:
            msg = (
                f"duplicate dependency result for work group {work_group_id!r}"
            )
            raise RuntimeError(msg)
        rows_by_work_group[work_group_id] = item
    return rows_by_work_group


def _ci_same_batch_dependency_outcome(selector_outcome: str) -> str:
    if selector_outcome == "success":
        return "satisfied"
    if selector_outcome == "skipped":
        return "skipped"
    return "failed"


def _ci_selector_outcome_admitted_for_gating(selector_outcome: str) -> bool:
    return selector_outcome in {"success", "blocking-failure"}


def _ci_execution_batch_positions(
    execution_batch_manifest: Mapping[str, object],
) -> dict[str, str]:
    positions: dict[str, str] = {}
    for batch in _ci_execution_batches(execution_batch_manifest):
        batch_id = batch.get("batch-id")
        if not isinstance(batch_id, str):
            continue
        for selector in _ci_batch_ordered_selectors(batch):
            work_group_id = selector.get("work-group-id")
            if isinstance(work_group_id, str):
                positions[work_group_id] = batch_id
    return positions


def _ci_batch_bundle_writer(
    *,
    execution_batch_manifest: Mapping[str, object],
    batch: Mapping[str, object],
    matrix_row: Mapping[str, object],
    workflow: str,
    job: str,
) -> Json:
    identity = _ci_execution_batch_matrix_identity(batch)
    observed_writer_id = ci_validation_writer_id(
        workflow=workflow,
        job=job,
        matrix=identity,
    )
    expected_writer_id = _ci_batch_expected_writer_id(
        execution_batch_manifest,
        batch,
    )
    if matrix_row.get("expected-job-identity") != expected_writer_id:
        msg = "matrix row expected writer identity does not match manifest"
        raise RuntimeError(msg)
    if observed_writer_id != expected_writer_id:
        msg = (
            "observed workflow/job/matrix writer identity does not match batch"
        )
        raise RuntimeError(msg)
    return {
        "identity-source": "github-actions-job-context",
        "expected-boundary": "execution-batch",
        "expected-job-identity": expected_writer_id,
        "observed-workflow": workflow,
        "observed-job": job,
        "observed-matrix": identity,
    }


def _ci_batch_execution_tree_verified(
    plan: Mapping[str, object],
    observed_commit_sha: str,
) -> bool:
    validation_tree = plan.get("validation-tree")
    return (
        isinstance(validation_tree, Mapping)
        and validation_tree.get("commit-sha") == observed_commit_sha
    )


def _ci_batch_summary_affected_range(
    plan: Mapping[str, object],
) -> Json:
    affected = cast("Mapping[str, object]", plan["affected-range"])
    return {
        "status": affected["status"],
        "base-sha": affected["base-sha"],
        "base-tip-sha": affected["base-tip-sha"],
        "head-sha": affected["head-sha"],
        "changed-files-hash": affected["changed-files-hash"] or None,
    }


def _ci_run_validation_command(
    *,
    index: int,
    command: object,
    plan: Json | None,
    assignments: Json | None,
    changed_files_snapshot: Json | None,
    fact_snapshot: Json | None,
    observed_artifacts_dir: str,
    observed_commit_sha: str,
    matrix_work_group: Mapping[str, object],
    repo_root: Path,
) -> Json:
    if not isinstance(command, Mapping):
        return _ci_validation_command_failure(
            index,
            f"command-{index}",
            "command entry is not an object",
        )
    label = str(command.get("label") or f"command-{index}")
    capability = command.get("capability")
    capability_value = str(capability) if isinstance(capability, str) else None
    builtin = command.get("builtin")
    if isinstance(builtin, str):
        return _ci_run_builtin_validation_command(
            index=index,
            label=label,
            capability=capability_value,
            builtin=builtin,
            command=command,
            plan=plan,
            assignments=assignments,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            observed_artifacts_dir=observed_artifacts_dir,
            observed_commit_sha=observed_commit_sha,
            matrix_work_group=matrix_work_group,
            repo_root=repo_root,
        )
    argv = command.get("argv")
    if not isinstance(argv, Sequence) or isinstance(argv, str | bytes):
        return _ci_validation_command_failure(
            index,
            label,
            "command argv is not an array",
            capability=capability_value,
        )
    argv_list = [str(item) for item in argv]
    try:
        completed = subprocess.run(argv_list, cwd=repo_root, check=False)
        returncode: int | None = completed.returncode
        error = None
    except OSError as exc:
        returncode = None
        error = str(exc)
    command_outcome: ReceiptOutcome = (
        "success" if returncode == 0 else "blocking-failure"
    )
    result = {
        "index": index,
        "label": label,
        "argv": argv_list,
        "capability": capability_value,
        "exit-code": returncode,
        "outcome": command_outcome,
    }
    if error is not None:
        result["error"] = error
    return result


def _ci_validation_command_failure(
    index: int,
    label: str,
    error: str,
    *,
    capability: str | None = None,
) -> Json:
    return {
        "index": index,
        "label": label,
        "argv": [],
        "capability": capability,
        "exit-code": None,
        "outcome": "blocking-failure",
        "error": error,
    }


def _ci_run_builtin_validation_command(
    *,
    index: int,
    label: str,
    capability: str | None,
    builtin: str,
    command: Mapping[str, object],
    plan: Json | None,
    assignments: Json | None,
    changed_files_snapshot: Json | None,
    fact_snapshot: Json | None,
    observed_artifacts_dir: str,
    observed_commit_sha: str,
    matrix_work_group: Mapping[str, object],
    repo_root: Path,
) -> Json:
    try:
        command_outcome, error, extra = _ci_builtin_validation_command_outcome(
            builtin=builtin,
            command=command,
            plan=plan,
            assignments=assignments,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            observed_artifacts_dir=observed_artifacts_dir,
            observed_commit_sha=observed_commit_sha,
            matrix_work_group=matrix_work_group,
            repo_root=repo_root,
        )
    except (KeyError, TypeError, ValueError) as exc:
        command_outcome = "blocking-failure"
        error = str(exc)
        extra = {}
    result = {
        "index": index,
        "label": label,
        "argv": [],
        "capability": capability,
        "builtin": builtin,
        "exit-code": 0 if command_outcome == "success" else 1,
        "outcome": command_outcome,
    }
    result.update(extra)
    if error is not None:
        result["error"] = error
    return result


def _ci_builtin_validation_command_outcome(
    *,
    builtin: str,
    command: Mapping[str, object],
    plan: Json | None,
    assignments: Json | None,
    changed_files_snapshot: Json | None,
    fact_snapshot: Json | None,
    observed_artifacts_dir: str,
    observed_commit_sha: str,
    matrix_work_group: Mapping[str, object],
    repo_root: Path,
) -> tuple[ReceiptOutcome, str | None, Json]:
    if builtin == "release-shaped-artifact":
        return _ci_release_shaped_artifact_builtin_outcome(
            plan,
            assignments,
            changed_files_snapshot,
            fact_snapshot,
            observed_artifacts_dir,
            observed_commit_sha,
            matrix_work_group,
            repo_root,
        )
    target, subcheck_ids = _ci_builtin_validation_context(
        plan, matrix_work_group
    )
    if builtin == "lightweight-preflight":
        outcome, error = _ci_lightweight_builtin_outcome(
            matrix_work_group, target, subcheck_ids
        )
        return outcome, error, {}
    if builtin == "workflow-release-tooling":
        outcome, error = _ci_tooling_builtin_outcome(
            matrix_work_group, target, subcheck_ids
        )
        return outcome, error, {}
    return (
        "blocking-failure",
        f"unknown builtin validation command: {builtin}",
        {},
    )


def _ci_builtin_validation_context(
    plan: Json | None,
    matrix_work_group: Mapping[str, object],
) -> tuple[Mapping[str, object], set[str]]:
    if not isinstance(plan, Mapping):
        msg = "frozen validation plan is required"
        raise TypeError(msg)
    work_group_id = matrix_work_group.get("work-group-id")
    if not isinstance(work_group_id, str) or not work_group_id:
        msg = "matrix work group id is required"
        raise ValueError(msg)
    target = matrix_work_group.get("coverage-target")
    if not isinstance(target, Mapping):
        msg = "matrix coverage target is required"
        raise TypeError(msg)
    expectation = _ci_evidence_expectation(plan, work_group_id)
    profile = _ci_detail_profile(plan, str(expectation["detail-profile"]))
    subchecks = profile.get("required-subchecks")
    subcheck_ids = {
        str(item.get("subcheck-id"))
        for item in subchecks
        if isinstance(item, Mapping)
        and isinstance(item.get("subcheck-id"), str)
    }
    return target, subcheck_ids


def _ci_lightweight_builtin_outcome(
    matrix_work_group: Mapping[str, object],
    target: Mapping[str, object],
    subcheck_ids: set[str],
) -> tuple[ReceiptOutcome, str | None]:
    if matrix_work_group.get("kind") != "lightweight-preflight":
        return "blocking-failure", "work group is not lightweight preflight"
    if target != {"type": "lightweight-policy", "id": "known-non-impacting"}:
        return "blocking-failure", "unexpected lightweight coverage target"
    if "known-non-impacting-policy" not in subcheck_ids:
        return (
            "blocking-failure",
            "lightweight detail profile lacks policy subcheck",
        )
    return "success", None


def _validate_ci_validation_lightweight_policy(
    *,
    plan: Mapping[str, object],
    assignments: Mapping[str, object],
    work_group_id: str,
    matrix_work_group: Mapping[str, object],
) -> None:
    group = _ci_work_group(plan, work_group_id)
    if group.get("kind") != "lightweight-preflight":
        msg = "work group is not lightweight preflight"
        raise ValueError(msg)
    _validate_lightweight_matrix_scope(
        work_group_id=work_group_id,
        group=group,
        matrix_work_group=matrix_work_group,
    )
    _validate_lightweight_plan_policy(plan, work_group_id, group)
    assignment = _ci_assignment_for_work_group(assignments, work_group_id)
    _validate_lightweight_assignment_policy(assignment)
    if _ci_work_group_dependency_layers(plan).get(work_group_id, -1) != 0:
        msg = "lightweight preflight must run in the initial validation layer"
        raise ValueError(msg)


def _validate_lightweight_matrix_scope(
    *,
    work_group_id: str,
    group: Mapping[str, object],
    matrix_work_group: Mapping[str, object],
) -> None:
    if matrix_work_group.get("work-group-id") != work_group_id:
        msg = "matrix work group id does not match scoped work group"
        raise ValueError(msg)
    if matrix_work_group.get("no-publish") is not True:
        msg = "lightweight execution context must be no-publish"
        raise ValueError(msg)
    for field in ("kind", "runner-family", "coverage-target"):
        if matrix_work_group.get(field) != group.get(field):
            msg = f"matrix work group {field} does not match frozen plan"
            raise ValueError(msg)


def _validate_lightweight_plan_policy(
    plan: Mapping[str, object],
    work_group_id: str,
    group: Mapping[str, object],
) -> None:
    target = group.get("coverage-target")
    if target != {"type": "lightweight-policy", "id": "known-non-impacting"}:
        msg = "unexpected lightweight coverage target"
        raise ValueError(msg)
    expectation = _ci_evidence_expectation(plan, work_group_id)
    if expectation.get("expected-outcome", "success") != "success":
        msg = "lightweight preflight must expect a successful policy receipt"
        raise ValueError(msg)
    profile = _ci_detail_profile(plan, str(expectation["detail-profile"]))
    subcheck_ids = _ci_detail_profile_required_subcheck_ids(profile)
    if "known-non-impacting-policy" not in subcheck_ids:
        msg = "lightweight detail profile lacks policy subcheck"
        raise ValueError(msg)


def _validate_lightweight_assignment_policy(
    assignment: Mapping[str, object],
) -> None:
    if assignment.get("expected-outcome", "success") != "success":
        msg = "lightweight assignment must expect a successful receipt"
        raise ValueError(msg)
    for field in ("receipt-artifact-ref", "writer-observation-ref"):
        if not isinstance(assignment.get(field), str) or not assignment[field]:
            msg = f"lightweight assignment is missing {field}"
            raise ValueError(msg)
    trusted_writer = assignment.get("trusted-writer-id")
    if not isinstance(trusted_writer, str) or not trusted_writer:
        msg = "lightweight assignment is missing trusted writer identity"
        raise ValueError(msg)


def _ci_detail_profile_required_subcheck_ids(
    profile: Mapping[str, object],
) -> set[str]:
    subchecks = profile.get("required-subchecks")
    if not isinstance(subchecks, Sequence) or isinstance(
        subchecks, str | bytes
    ):
        return set()
    return {
        str(item.get("subcheck-id"))
        for item in subchecks
        if isinstance(item, Mapping)
        and isinstance(item.get("subcheck-id"), str)
    }


def _validate_scoped_descriptor_obligations(
    *,
    plan: Mapping[str, object],
    work_group_id: str,
    repo_root: Path,
) -> None:
    group = _ci_work_group(plan, work_group_id)
    if group.get("kind") != "descriptor-validation":
        msg = "work group is not descriptor validation"
        raise ValueError(msg)
    obligations = _ci_plan_records_for_work_group(
        plan, "descriptor-obligations", work_group_id
    )
    if not obligations:
        msg = "descriptor validation work group has no scoped obligations"
        raise ValueError(msg)
    descriptor_paths = sorted(
        {
            _ci_descriptor_obligation_path(obligation)
            for obligation in obligations
        }
    )
    validate_target_catalog_document(
        _read_yaml(repo_root / CATALOG_PATH),
        catalog_path=CATALOG_PATH,
    )
    tracked_files = _scoped_authoring_tracked_files(repo_root, descriptor_paths)
    for descriptor_path in descriptor_paths:
        validate_project_descriptor_document(
            descriptor_path,
            _read_yaml(repo_root / descriptor_path),
            tracked_files=tracked_files,
        )


def _read_yaml(path: Path) -> object:
    with path.open("r", encoding="utf-8") as stream:
        return yaml.safe_load(stream)


def _scoped_authoring_tracked_files(
    repo_root: Path,
    descriptor_paths: Sequence[str],
) -> set[str]:
    tracked: set[str] = {CATALOG_PATH}
    for descriptor_path in descriptor_paths:
        tracked.add(descriptor_path)
        descriptor_parent = PurePosixPath(descriptor_path).parent
        root = repo_root / descriptor_parent
        if root.is_dir():
            for path in root.rglob("*"):
                if path.is_file():
                    tracked.add(path.relative_to(repo_root).as_posix())
    return tracked


def _ci_tooling_builtin_outcome(
    matrix_work_group: Mapping[str, object],
    target: Mapping[str, object],
    subcheck_ids: set[str],
) -> tuple[ReceiptOutcome, str | None]:
    if matrix_work_group.get("kind") != "workflow-release-tooling":
        return "blocking-failure", "work group is not tooling validation"
    if target.get("type") != "tooling-surface":
        return "blocking-failure", "unexpected tooling coverage target"
    if "tooling-contract" not in subcheck_ids:
        return (
            "blocking-failure",
            "tooling detail profile lacks contract subcheck",
        )
    return "success", None


def _ci_release_shaped_artifact_builtin_outcome(  # noqa: C901, PLR0911
    plan: Json | None,
    assignments: Json | None,
    changed_files_snapshot: Json | None,
    fact_snapshot: Json | None,
    observed_artifacts_dir: str,
    observed_commit_sha: str,
    matrix_work_group: Mapping[str, object],
    repo_root: Path,
) -> tuple[ReceiptOutcome, str | None, Json]:
    if not isinstance(plan, Mapping):
        return "blocking-failure", "frozen validation plan is required", {}
    work_group_id = matrix_work_group.get("work-group-id")
    if not isinstance(work_group_id, str) or not work_group_id:
        return "blocking-failure", "matrix work group id is required", {}
    try:
        group = _ci_work_group(plan, work_group_id)
        _validate_release_shaped_matrix_scope(
            work_group_id=work_group_id,
            group=group,
            matrix_work_group=matrix_work_group,
        )
        expectation = _ci_evidence_expectation(plan, work_group_id)
        if expectation.get("category") != "release-shaped-artifact":
            return (
                "blocking-failure",
                "release-shaped work group lacks matching evidence category",
                {},
            )
        obligations = _ci_plan_records_for_work_group(
            plan, "artifact-obligations", work_group_id
        )
        if not obligations:
            return (
                "blocking-failure",
                "release-shaped work group has no frozen artifact obligations",
                {},
            )
        if not observed_commit_sha:
            return (
                "blocking-failure",
                "observed commit SHA is required to reuse release-shaped evidence",
                {},
            )
        for obligation in obligations:
            refs = _ci_artifact_expected_refs(obligation)
            if not refs or len(refs) != len(set(refs)):
                return (
                    "blocking-failure",
                    "release-shaped artifact obligation has invalid expected refs",
                    {},
                )
        reuse = (
            _ci_reused_release_shaped_artifact_evidence(
                plan=plan,
                assignments=assignments,
                work_group_id=work_group_id,
                observed_artifacts_dir=observed_artifacts_dir,
                observed_commit_sha=observed_commit_sha,
                changed_files_snapshot=changed_files_snapshot,
                fact_snapshot=fact_snapshot,
            )
            if isinstance(assignments, Mapping) and observed_artifacts_dir
            else None
        )
        if reuse is None:
            if (
                isinstance(assignments, Mapping)
                and observed_artifacts_dir
                and _ci_has_observed_release_shaped_receipt_candidate(
                    plan=plan,
                    assignments=assignments,
                    work_group_id=work_group_id,
                    observed_artifacts_dir=observed_artifacts_dir,
                    changed_files_snapshot=changed_files_snapshot,
                    fact_snapshot=fact_snapshot,
                )
            ):
                return (
                    "blocking-failure",
                    "no admissible no-publish release-shaped artifact evidence was found",
                    {},
                )
            return (
                "success",
                None,
                _ci_no_publish_release_shaped_source_evidence(
                    plan=plan,
                    work_group_id=work_group_id,
                    matrix_work_group=matrix_work_group,
                    obligations=obligations,
                    observed_commit_sha=observed_commit_sha,
                    fact_snapshot=fact_snapshot,
                    repo_root=repo_root,
                ),
            )
    except (KeyError, TypeError, ValueError) as exc:
        return "blocking-failure", str(exc), {}
    return (
        "success",
        None,
        {
            "evidence-source": "reused-validation-receipt",
            "reused-receipt": reuse["reused-receipt"],
            "artifact-obligation-results": reuse["artifact-obligation-results"],
        },
    )


def _ci_has_observed_release_shaped_receipt_candidate(
    *,
    plan: Mapping[str, object],
    assignments: Mapping[str, object],
    work_group_id: str,
    observed_artifacts_dir: str,
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
) -> bool:
    assignment = _ci_assignment_for_work_group(assignments, work_group_id)
    expected_writer_id = assignment.get("trusted-writer-id")
    return any(
        _ci_observed_receipt_manifest_matches_trusted_writer(
            observed.manifest_entry,
            work_group_id=work_group_id,
            expected_writer_id=expected_writer_id,
        )
        for observed in _ci_observed_receipt_inputs(
            plan=plan,
            assignments=assignments,
            observed_artifacts_dir=observed_artifacts_dir,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
        )
    )


def _ci_no_publish_release_shaped_source_evidence(
    *,
    plan: Mapping[str, object],
    work_group_id: str,
    matrix_work_group: Mapping[str, object],
    obligations: Sequence[Mapping[str, object]],
    observed_commit_sha: str,
    fact_snapshot: Mapping[str, object] | None,
    repo_root: Path,
) -> Json:
    """Build source-backed no-publish release-shaped validation evidence."""
    results: list[Json] = []
    for obligation in obligations:
        result = _ci_artifact_obligation_success_result(obligation)
        descriptor_path = str(obligation["descriptor-path"])
        descriptor_sha256 = _ci_release_shaped_descriptor_source_digest(
            repo_root,
            descriptor_path,
        )
        descriptor_fact = (
            _ci_descriptor_fact(fact_snapshot, descriptor_path)
            if fact_snapshot is not None
            else None
        )
        cast("dict[str, object]", result["descriptor"])["identity"] = (
            descriptor_fact.get("descriptor-identity")
            if descriptor_fact is not None
            else None
        )
        observed_artifact = cast(
            "dict[str, object]",
            cast("dict[str, object]", result["artifact"])["observed"],
        )
        observed_artifact["digests"] = [
            {
                "artifact-ref": artifact_ref,
                "algorithm": "sha256",
                "digest": _ci_release_shaped_source_artifact_digest(
                    work_group_id=work_group_id,
                    obligation=obligation,
                    artifact_ref=artifact_ref,
                    descriptor_sha256=descriptor_sha256,
                ),
                "digest-available": True,
                "diagnostics": [],
            }
            for artifact_ref in _ci_artifact_expected_refs(obligation)
        ]
        results.append(result)
    return {
        "evidence-source": "no-publish-validation",
        "source-proof": {
            "kind": "no-publish-validation-result",
            "work-group-id": work_group_id,
            "coverage-target": matrix_work_group["coverage-target"],
            "observed-commit-sha": observed_commit_sha,
            "artifact-digests": _ci_release_shaped_digest_proof_entries_from_results(
                results,
            ),
        },
        "artifact-obligation-results": results,
    }


def _ci_release_shaped_descriptor_source_digest(
    repo_root: Path,
    descriptor_path: str,
) -> str:
    path = PurePosixPath(descriptor_path)
    if path.is_absolute() or ".." in path.parts:
        msg = "release-shaped descriptor source path is outside the repository"
        raise ValueError(msg)
    full_path = repo_root / path
    try:
        data = full_path.read_bytes()
    except OSError as exc:
        msg = (
            "release-shaped descriptor source proof is unavailable for "
            f"{descriptor_path}: {exc}"
        )
        raise ValueError(msg) from exc
    return hashlib.sha256(data).hexdigest()


def _ci_release_shaped_source_artifact_digest(
    *,
    work_group_id: str,
    obligation: Mapping[str, object],
    artifact_ref: str,
    descriptor_sha256: str,
) -> str:
    return canonical_json_digest(
        {
            "kind": "release-shaped-no-publish-source-proof",
            "work-group-id": work_group_id,
            "artifact-obligation-id": obligation["artifact-obligation-id"],
            "artifact-ref": artifact_ref,
            "descriptor-path": obligation["descriptor-path"],
            "descriptor-sha256": descriptor_sha256,
            "artifact": obligation["artifact"],
            "release-receipt": obligation["release-receipt"],
        }
    )


def _validate_release_shaped_matrix_scope(
    *,
    work_group_id: str,
    group: Mapping[str, object],
    matrix_work_group: Mapping[str, object],
) -> None:
    if group.get("kind") != "release-shaped-artifact":
        msg = "work group is not release-shaped artifact validation"
        raise ValueError(msg)
    if matrix_work_group.get("work-group-id") != work_group_id:
        msg = "matrix work group id does not match scoped work group"
        raise ValueError(msg)
    if matrix_work_group.get("no-publish") is not True:
        msg = "release-shaped artifact validation context must be no-publish"
        raise ValueError(msg)
    for field in ("kind", "runner-family", "coverage-target"):
        if matrix_work_group.get(field) != group.get(field):
            msg = f"matrix work group {field} does not match frozen plan"
            raise ValueError(msg)


def _ci_reused_release_shaped_artifact_evidence(
    *,
    plan: Mapping[str, object],
    assignments: Mapping[str, object],
    work_group_id: str,
    observed_artifacts_dir: str,
    observed_commit_sha: str,
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
) -> Json | None:
    """Return admissible release-shaped no-publish evidence from a prior receipt."""
    assignment = _ci_assignment_for_work_group(assignments, work_group_id)
    expected_writer_id = assignment.get("trusted-writer-id")
    observed_receipts = _ci_observed_receipt_inputs(
        plan=plan,
        assignments=assignments,
        observed_artifacts_dir=observed_artifacts_dir,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
    )
    for observed in observed_receipts:
        if not _ci_observed_receipt_manifest_matches_trusted_writer(
            observed.manifest_entry,
            work_group_id=work_group_id,
            expected_writer_id=expected_writer_id,
        ):
            continue
        receipt = observed.receipt
        if receipt is None:
            continue
        try:
            validate_ci_validation_receipt(
                receipt,
                plan=plan,
                selector_assignments_manifest=assignments,
                assignment=assignment,
                changed_files_snapshot=changed_files_snapshot,
                fact_snapshot=fact_snapshot,
            )
        except ContractValidationError:
            continue
        if not _ci_receipt_reusable_for_release_shape(
            receipt,
            plan,
            assignments,
            work_group_id,
            observed_commit_sha,
            observed_receipts=observed_receipts,
            source_validation_result=observed.validation_result,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
        ):
            continue
        results = _ci_release_shaped_results_from_receipt(receipt)
        if results is None:
            continue
        return {
            "reused-receipt": {
                "artifact-ref": observed.manifest_entry.get("artifact-ref"),
                "receipt-id": receipt.get("receipt-id"),
                "receipt-content-digest": observed.manifest_entry.get(
                    "receipt-content-digest"
                ),
                "observed-commit-sha": observed_commit_sha,
            },
            "artifact-obligation-results": [dict(result) for result in results],
        }
    return None


def _ci_receipt_reusable_for_release_shape(
    receipt: Mapping[str, object],
    plan: Mapping[str, object],
    assignments: Mapping[str, object],
    work_group_id: str,
    observed_commit_sha: str,
    *,
    observed_receipts: Sequence[CiValidationObservedReceiptInput],
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    source_validation_result: Mapping[str, object] | None = None,
    visited_receipt_digests: set[str] | None = None,
) -> bool:
    validation_tree = receipt.get("validation-tree")
    execution_tree = receipt.get("execution-tree")
    results = _ci_release_shaped_results_from_receipt(receipt)
    if not (
        receipt.get("outcome") == "success"
        and receipt.get("proof-admissibility") == "validation-only"
        and receipt.get("work-group-id") == work_group_id
        and isinstance(validation_tree, Mapping)
        and validation_tree == plan.get("validation-tree")
        and validation_tree.get("commit-sha") == observed_commit_sha
        and isinstance(execution_tree, Mapping)
        and execution_tree.get("observed-commit-sha") == observed_commit_sha
        and execution_tree.get("verified") is True
        and results is not None
        and _ci_release_shaped_results_match_plan(plan, work_group_id, results)
        and not _ci_release_shaped_results_contain_plan_fabrication(
            plan, work_group_id, results
        )
    ):
        return False
    return _ci_release_shaped_receipt_source_is_admissible(
        receipt=receipt,
        plan=plan,
        assignments=assignments,
        work_group_id=work_group_id,
        observed_commit_sha=observed_commit_sha,
        observed_receipts=observed_receipts,
        source_validation_result=source_validation_result,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        visited_receipt_digests=visited_receipt_digests or set(),
    )


def _ci_release_shaped_results_from_receipt(
    receipt: Mapping[str, object],
) -> list[Mapping[str, object]] | None:
    evidence = receipt.get("evidence")
    if not isinstance(evidence, Mapping):
        return None
    category_result = evidence.get("category-result")
    if not isinstance(category_result, Mapping):
        return None
    detail = category_result.get("detail")
    if not isinstance(detail, Mapping):
        return None
    results = detail.get("artifact-obligation-results")
    if not isinstance(results, Sequence) or isinstance(results, str | bytes):
        return None
    return [item for item in results if isinstance(item, Mapping)]


def _ci_release_shaped_detail_from_receipt(
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


def _ci_release_shaped_receipt_source_is_admissible(  # noqa: PLR0911
    *,
    receipt: Mapping[str, object],
    plan: Mapping[str, object],
    assignments: Mapping[str, object],
    work_group_id: str,
    observed_commit_sha: str,
    observed_receipts: Sequence[CiValidationObservedReceiptInput],
    source_validation_result: Mapping[str, object] | None,
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    visited_receipt_digests: set[str],
) -> bool:
    detail = _ci_release_shaped_detail_from_receipt(receipt)
    if detail is None:
        return False
    evidence_source = detail.get("evidence-source")
    if evidence_source == "no-publish-validation":
        return _ci_no_publish_release_shaped_source_proof_is_admissible(
            receipt,
            detail,
            source_validation_result=source_validation_result,
        )
    if evidence_source != "reused-validation-receipt":
        return False
    reused_receipt = detail.get("reused-receipt")
    if not isinstance(reused_receipt, Mapping):
        return False
    assignment = _ci_assignment_for_work_group(assignments, work_group_id)
    prior = _ci_observed_reused_receipt_input(
        reused_receipt,
        observed_receipts,
        observed_commit_sha=observed_commit_sha,
        work_group_id=work_group_id,
        expected_writer_id=assignment.get("trusted-writer-id"),
    )
    if prior is None or prior.receipt is None:
        return False
    prior_digest = prior.manifest_entry.get("receipt-content-digest")
    if (
        not isinstance(prior_digest, str)
        or prior_digest in visited_receipt_digests
    ):
        return False
    prior_work_group_id = prior.receipt.get("work-group-id")
    if prior_work_group_id != work_group_id:
        return False
    prior_detail = _ci_release_shaped_detail_from_receipt(prior.receipt)
    if (
        prior_detail is None
        or not _ci_release_shaped_reused_results_match_source(
            current_detail=detail,
            source_detail=prior_detail,
        )
    ):
        return False
    try:
        validate_ci_validation_receipt(
            prior.receipt,
            plan=plan,
            selector_assignments_manifest=assignments,
            assignment=assignment,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
        )
    except ContractValidationError:
        return False
    return _ci_receipt_reusable_for_release_shape(
        prior.receipt,
        plan,
        assignments,
        work_group_id,
        observed_commit_sha,
        observed_receipts=observed_receipts,
        source_validation_result=prior.validation_result,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        visited_receipt_digests=visited_receipt_digests | {prior_digest},
    )


def _ci_observed_reused_receipt_input(
    reused_receipt: Mapping[str, object],
    observed_receipts: Sequence[CiValidationObservedReceiptInput],
    *,
    observed_commit_sha: str,
    work_group_id: str,
    expected_writer_id: object,
) -> CiValidationObservedReceiptInput | None:
    if reused_receipt.get("observed-commit-sha") != observed_commit_sha:
        return None
    artifact_ref = reused_receipt.get("artifact-ref")
    receipt_id = reused_receipt.get("receipt-id")
    content_digest = reused_receipt.get("receipt-content-digest")
    if not all(
        isinstance(item, str) and item
        for item in (artifact_ref, receipt_id, content_digest)
    ):
        return None
    for observed in observed_receipts:
        if (
            observed.manifest_entry.get("artifact-ref") == artifact_ref
            and observed.manifest_entry.get("receipt-id") == receipt_id
            and observed.manifest_entry.get("receipt-content-digest")
            == content_digest
            and _ci_observed_receipt_manifest_matches_trusted_writer(
                observed.manifest_entry,
                work_group_id=work_group_id,
                expected_writer_id=expected_writer_id,
            )
        ):
            return observed
    return None


def _ci_release_shaped_reused_results_match_source(
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


def _ci_observed_receipt_manifest_matches_trusted_writer(
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


def _ci_no_publish_release_shaped_source_proof_is_admissible(  # noqa: PLR0911
    receipt: Mapping[str, object],
    detail: Mapping[str, object],
    *,
    source_validation_result: Mapping[str, object] | None,
) -> bool:
    source_proof = detail.get("source-proof")
    if not isinstance(source_proof, Mapping):
        return False
    if (
        source_proof.get("kind") != "no-publish-validation-result"
        or source_proof.get("work-group-id") != receipt.get("work-group-id")
        or source_proof.get("coverage-target") != receipt.get("coverage-target")
    ):
        return False
    execution_tree = receipt.get("execution-tree")
    if not isinstance(execution_tree, Mapping):
        return False
    if source_proof.get("observed-commit-sha") != execution_tree.get(
        "observed-commit-sha"
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
        == execution_tree.get("observed-commit-sha")
    ):
        return False
    source_command = _ci_no_publish_source_command_from_validation_result(
        source_validation_result,
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
    return _ci_release_shaped_digest_proof_entries_from_results(
        cast(
            "Sequence[Mapping[str, object]]",
            source_command["artifact-obligation-results"],
        )
    ) == [
        dict(item)
        for item in cast("Sequence[Mapping[str, object]]", proof_digests)
    ]


def _ci_no_publish_source_command_from_validation_result(
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


def _ci_release_shaped_digest_proof_entries(
    receipt: Mapping[str, object],
) -> list[Json]:
    results = _ci_release_shaped_results_from_receipt(receipt)
    if results is None:
        return []
    return _ci_release_shaped_digest_proof_entries_from_results(results)


def _ci_release_shaped_digest_proof_entries_from_results(
    results: Sequence[Mapping[str, object]],
) -> list[Json]:
    entries: list[Json] = []
    for result in results:
        artifact = result.get("artifact")
        if not isinstance(artifact, Mapping):
            return []
        observed = artifact.get("observed")
        if not isinstance(observed, Mapping):
            return []
        digests = observed.get("digests")
        if not isinstance(digests, Sequence) or isinstance(
            digests, str | bytes
        ):
            return []
        for item in digests:
            if not isinstance(item, Mapping):
                return []
            artifact_ref = item.get("artifact-ref")
            algorithm = item.get("algorithm")
            digest = item.get("digest")
            if not all(
                isinstance(value, str)
                for value in (artifact_ref, algorithm, digest)
            ):
                return []
            entries.append(
                {
                    "artifact-ref": artifact_ref,
                    "algorithm": algorithm,
                    "digest": digest,
                }
            )
    return sorted(entries, key=lambda item: str(item["artifact-ref"]))


def _cmd_write_ci_validation_receipt(args: argparse.Namespace) -> int:
    plan = _read_json(Path(args.plan))
    assignments = _read_json(Path(args.assignments))
    changed_files_snapshot = _read_optional_json(args.changed_files_snapshot)
    fact_snapshot = _read_optional_json(args.fact_snapshot)
    assignment = _ci_assignment_for_work_group(assignments, args.work_group_id)
    matrix_work_group = _read_json_value(args.matrix_work_group_json)
    observed_writer_id = ci_validation_writer_id(
        workflow=args.workflow,
        job=args.job,
        matrix={"work-group": matrix_work_group},
    )
    if observed_writer_id != assignment.get("trusted-writer-id"):
        msg = (
            "observed workflow matrix writer identity does not match assignment"
        )
        raise RuntimeError(msg)
    try:
        validation_result = _read_optional_json(
            getattr(args, "validation_result", "")
        )
    except (OSError, json.JSONDecodeError, TypeError, ValueError):
        validation_result = None
    dependency_blocked = _ci_dependency_blocked(
        plan=plan,
        assignments=assignments,
        work_group_id=args.work_group_id,
        observed_artifacts_dir=getattr(args, "observed_artifacts_dir", ""),
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
    )
    outcome = _ci_validation_outcome(
        plan,
        args.work_group_id,
        dependency_blocked=dependency_blocked,
        validation_result=validation_result,
        assignments=assignments,
        observed_artifacts_dir=getattr(args, "observed_artifacts_dir", ""),
        observed_commit_sha=args.observed_commit_sha,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
    )
    diagnostics = _ci_validation_diagnostics(
        plan,
        args.work_group_id,
        outcome=outcome,
    )
    receipt = freeze_ci_validation_receipt(
        plan=plan,
        selector_assignments_manifest=assignments,
        assignment=assignment,
        receipt_id=str(assignment["assignment-id"]),
        created_at=args.created_at or _utc_now(),
        execution_observed_commit_sha=args.observed_commit_sha,
        outcome=outcome,
        evidence=_ci_validation_evidence(
            plan,
            args.work_group_id,
            outcome=outcome,
            diagnostics=diagnostics,
            validation_result=validation_result,
            fact_snapshot=fact_snapshot,
        ),
        diagnostics=diagnostics,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
    )
    _write_json(Path(args.receipt_out), receipt)
    _write_outputs(
        args.github_output,
        {
            "receipt_artifact_ref": str(assignment["receipt-artifact-ref"]),
            "receipt_artifact_name": artifact_physical_name(
                str(assignment["receipt-artifact-ref"])
            ),
            "writer_observation_artifact_ref": str(
                assignment["writer-observation-ref"]
            ),
            "writer_observation_artifact_name": artifact_physical_name(
                str(assignment["writer-observation-ref"])
            ),
            "observed_writer_id": observed_writer_id,
            "dependency_blocked": _bool_str(dependency_blocked),
        },
    )
    return 0


def _cmd_write_ci_validation_writer_observation(
    args: argparse.Namespace,
) -> int:
    plan = _read_json(Path(args.plan))
    assignments = _read_json(Path(args.assignments))
    changed_files_snapshot = _read_optional_json(args.changed_files_snapshot)
    fact_snapshot = _read_optional_json(args.fact_snapshot)
    assignment = _ci_assignment_for_work_group(assignments, args.work_group_id)
    matrix_work_group = _read_json_value(args.matrix_work_group_json)
    observed_writer_id = ci_validation_writer_id(
        workflow=args.workflow,
        job=args.job,
        matrix={"work-group": matrix_work_group},
    )
    observation = freeze_ci_validation_writer_observation(
        plan=plan,
        selector_assignments_manifest=assignments,
        assignment=assignment,
        artifact_instance_id=str(args.artifact_instance_id),
        observed_writer_id=observed_writer_id,
        created_at=args.created_at or _utc_now(),
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
    )
    validate_ci_validation_writer_observation(
        observation,
        plan=plan,
        selector_assignments_manifest=assignments,
        assignment=assignment,
        expected_artifact_instance_id=str(args.artifact_instance_id),
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
    )
    _write_json(Path(args.observation_out), observation)
    if getattr(args, "metadata_out", ""):
        _write_json(
            Path(args.metadata_out),
            {
                "artifact-ref": assignment["receipt-artifact-ref"],
                "physical-artifact-name": artifact_physical_name(
                    str(assignment["receipt-artifact-ref"])
                ),
                "artifact-instance-id": str(args.artifact_instance_id),
                "source": "actions-upload-artifact-output",
            },
        )
    _write_outputs(
        args.github_output,
        {
            "writer_observation_artifact_ref": str(
                assignment["writer-observation-ref"]
            ),
            "writer_observation_artifact_name": artifact_physical_name(
                str(assignment["writer-observation-ref"])
            ),
            "observed_writer_id": observed_writer_id,
        },
    )
    return 0


def _cmd_download_ci_validation_observed_artifacts(
    args: argparse.Namespace,
) -> int:
    root = Path(args.observed_artifacts_dir)
    root.mkdir(parents=True, exist_ok=True)
    try:
        assignments = _read_optional_json(args.assignments)
    except (OSError, json.JSONDecodeError, TypeError, ValueError) as exc:
        assignments = None
        print(
            f"warning: failed to read selector assignments for observed "
            f"artifact downloads: {exc}",
            file=sys.stderr,
        )
    names = (
        _ci_observed_artifact_download_names(assignments)
        if assignments is not None
        else []
    )
    downloaded: list[str] = []
    failed: list[str] = []
    for artifact_name_value in names:
        try:
            _download_artifact(
                args.repository,
                int(args.run_id),
                artifact_name_value,
                root / artifact_name_value,
            )
            downloaded.append(artifact_name_value)
        except RuntimeError as exc:
            failed.append(artifact_name_value)
            print(f"warning: {exc}", file=sys.stderr)
    _write_outputs(
        args.github_output,
        {
            "attempted_artifact_count": str(len(names)),
            "downloaded_artifact_count": str(len(downloaded)),
            "failed_artifact_count": str(len(failed)),
            "failed_artifact_names": json.dumps(
                failed,
                sort_keys=True,
                separators=(",", ":"),
            ),
        },
    )
    return 0


def _cmd_aggregate_ci_evidence(args: argparse.Namespace) -> int:
    owner, name = _split_repository(args.repository)
    created_at = args.created_at or _utc_now()
    boundary_diagnostics = _ci_aggregate_control_artifact_boundary_diagnostics(
        args,
    )
    if boundary_diagnostics:
        return _write_invalid_ci_aggregate(
            args,
            owner=owner,
            name=name,
            created_at=created_at,
            diagnostic_detail=_ci_invalid_plan_detail_from_boundary(
                str(boundary_diagnostics[0]["detail"]),
            ),
        )
    try:
        plan = _read_optional_json(args.plan)
        changed_files_snapshot = _read_optional_json(
            args.changed_files_snapshot
        )
        fact_snapshot = _read_optional_json(args.fact_snapshot)
    except (json.JSONDecodeError, TypeError, ValueError):
        return _write_invalid_ci_aggregate(
            args,
            owner=owner,
            name=name,
            created_at=created_at,
        )
    if plan is not None:
        try:
            validate_ci_validation_plan(
                plan,
                changed_files_snapshot=changed_files_snapshot,
                fact_snapshot=fact_snapshot,
            )
        except ContractValidationError:
            return _write_invalid_ci_aggregate(
                args,
                owner=owner,
                name=name,
                created_at=created_at,
            )
    manifest = freeze_ci_validation_receipt_manifest(
        plan=plan,
        entries=[],
        created_at=created_at,
        repository_owner=owner,
        repository_name=name,
        workflow=args.workflow,
        run_id=str(args.run_id),
        run_attempt=str(args.run_attempt),
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
    )
    _write_json(Path(args.receipt_manifest_out), manifest)
    if plan is None:
        aggregate = freeze_ci_validation_invalid_plan_aggregate(
            created_at=created_at,
            repository_owner=owner,
            repository_name=name,
            workflow=args.workflow,
            run_id=str(args.run_id),
            run_attempt=str(args.run_attempt),
            diagnostic_detail=DiagnosticDetail.PLAN_MISSING.value,
            receipt_manifest=manifest,
        )
    else:
        try:
            assignments = _read_optional_json(args.assignments)
            if assignments is not None:
                validate_ci_validation_selector_assignments(
                    assignments,
                    plan=plan,
                    changed_files_snapshot=changed_files_snapshot,
                    fact_snapshot=fact_snapshot,
                )
        except (
            ContractValidationError,
            json.JSONDecodeError,
            TypeError,
            ValueError,
        ):
            assignments = None
        if assignments is None:
            aggregate = _invalid_ci_aggregate_for_valid_plan(
                args,
                owner=owner,
                name=name,
                created_at=created_at,
                plan=plan,
                manifest=manifest,
                changed_files_snapshot=changed_files_snapshot,
                fact_snapshot=fact_snapshot,
            )
        else:
            try:
                observed_receipts = _ci_observed_receipt_inputs(
                    plan=plan,
                    assignments=assignments,
                    observed_artifacts_dir=getattr(
                        args, "observed_artifacts_dir", ""
                    ),
                    changed_files_snapshot=changed_files_snapshot,
                    fact_snapshot=fact_snapshot,
                )
                manifest = freeze_ci_validation_receipt_manifest(
                    plan=plan,
                    entries=[item.manifest_entry for item in observed_receipts],
                    created_at=created_at,
                    changed_files_snapshot=changed_files_snapshot,
                    fact_snapshot=fact_snapshot,
                )
                _write_json(Path(args.receipt_manifest_out), manifest)
                aggregate = freeze_ci_validation_aggregate(
                    plan=plan,
                    receipt_manifest=manifest,
                    selector_assignments_manifest=assignments,
                    observed_receipts=observed_receipts,
                    created_at=created_at,
                    changed_files_snapshot=changed_files_snapshot,
                    fact_snapshot=fact_snapshot,
                )
            except ContractValidationError:
                aggregate = _invalid_ci_aggregate_for_valid_plan(
                    args,
                    owner=owner,
                    name=name,
                    created_at=created_at,
                    plan=plan,
                    manifest=manifest,
                    changed_files_snapshot=changed_files_snapshot,
                    fact_snapshot=fact_snapshot,
                )
    _write_json(Path(args.aggregate_out), aggregate)
    _write_outputs(
        args.github_output,
        {
            "verdict": str(aggregate["verdict"]),
            "passed": _bool_str(aggregate["verdict"] == "passed"),
        },
    )
    return 0 if aggregate["verdict"] == "passed" else 1


def _write_invalid_ci_aggregate(
    args: argparse.Namespace,
    *,
    owner: str,
    name: str,
    created_at: str,
    diagnostic_detail: str = DiagnosticDetail.STRUCTURALLY_INVALID.value,
) -> int:
    manifest = freeze_ci_validation_receipt_manifest(
        plan=None,
        entries=[],
        created_at=created_at,
        repository_owner=owner,
        repository_name=name,
        workflow=args.workflow,
        run_id=str(args.run_id),
        run_attempt=str(args.run_attempt),
    )
    _write_json(Path(args.receipt_manifest_out), manifest)
    aggregate = freeze_ci_validation_invalid_plan_aggregate(
        created_at=created_at,
        repository_owner=owner,
        repository_name=name,
        workflow=args.workflow,
        run_id=str(args.run_id),
        run_attempt=str(args.run_attempt),
        diagnostic_detail=diagnostic_detail,
        receipt_manifest=manifest,
    )
    _write_json(Path(args.aggregate_out), aggregate)
    _write_outputs(
        args.github_output,
        {
            "verdict": str(aggregate["verdict"]),
            "passed": "false",
        },
    )
    return 1


def _invalid_ci_aggregate_for_valid_plan(
    args: argparse.Namespace,
    *,
    owner: str,
    name: str,
    created_at: str,
    plan: Mapping[str, object],
    manifest: Mapping[str, object],
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
) -> Json:
    return freeze_ci_validation_invalid_plan_aggregate(
        created_at=created_at,
        repository_owner=owner,
        repository_name=name,
        workflow=args.workflow,
        run_id=str(args.run_id),
        run_attempt=str(args.run_attempt),
        diagnostic_detail=DiagnosticDetail.STRUCTURALLY_INVALID.value,
        plan=plan,
        receipt_manifest=manifest,
        post_plan_contract_invalid=True,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
    )


def _ci_aggregate_control_artifact_boundary_diagnostics(
    args: argparse.Namespace,
) -> list[Mapping[str, object]]:
    expected = _ci_expected_aggregate_input_artifacts(args)
    if not expected:
        return []
    try:
        artifacts = _github_actions_run_artifacts(
            repository=args.repository,
            run_id=str(args.run_id),
        )
        return _ci_verify_expected_artifact_producer_boundaries(
            artifacts=artifacts,
            expected_artifacts=expected,
            workflow=args.workflow,
            run_id=str(args.run_id),
            run_attempt=str(args.run_attempt),
        )
    except (
        ContractValidationError,
        RuntimeError,
        TypeError,
        ValueError,
    ) as exc:
        return [
            _ci_boundary_diagnostic(
                index=0,
                detail=DiagnosticDetail.PLAN_PRODUCER_UNVERIFIED.value,
                message=str(exc),
                source_id=None,
            )
        ]


def _ci_invalid_plan_detail_from_boundary(detail: str) -> str:
    if (
        detail
        in DETAILS_BY_DIAGNOSTIC_CODE[DiagnosticFamily.INVALID_PLAN.value]
    ):
        return detail
    return DiagnosticDetail.PLAN_PRODUCER_UNVERIFIED.value


def _ci_expected_aggregate_input_artifacts(
    args: argparse.Namespace,
) -> list[Mapping[str, object]]:
    run_id = str(args.run_id)
    run_attempt = str(args.run_attempt)
    specs = [
        (
            "expected_request_artifact_id",
            ci_validation_request_artifact_ref(
                run_id=run_id,
                run_attempt=run_attempt,
            ),
            "normalize-input",
            "normalize-input",
        ),
        (
            "expected_plan_artifact_id",
            ci_validation_plan_artifact_ref(
                run_id=run_id,
                run_attempt=run_attempt,
            ),
            "plan",
            "plan",
        ),
        (
            "expected_changed_files_snapshot_artifact_id",
            ci_validation_changed_files_snapshot_artifact_ref(
                run_id=run_id,
                run_attempt=run_attempt,
            ),
            "plan",
            "plan",
        ),
        (
            "expected_fact_snapshot_artifact_id",
            ci_validation_fact_snapshot_artifact_ref(
                run_id=run_id,
                run_attempt=run_attempt,
            ),
            "plan",
            "plan",
        ),
        (
            "expected_selector_assignments_artifact_id",
            ci_validation_selector_assignments_artifact_ref(
                run_id=run_id,
                run_attempt=run_attempt,
            ),
            "materialize-work-groups",
            "materialize-work-groups",
        ),
    ]
    expected: list[Mapping[str, object]] = []
    for attr, artifact_ref, boundary, job in specs:
        artifact_id = getattr(args, attr, None)
        if artifact_id is None or (
            _ci_is_optional_control_artifact_ref(artifact_ref)
            and str(artifact_id) == ""
        ):
            continue
        expected.append(
            {
                "artifact-ref": artifact_ref,
                "artifact-instance-id": str(artifact_id),
                "producer-boundary": boundary,
                "producer-job": job,
            }
        )
    return expected


def _cmd_plan_gate(args: argparse.Namespace) -> int:
    plan = _read_json(Path(args.plan))
    execution_sets = _read_json(Path(args.execution_sets))
    remote_observations = (
        _read_json(Path(args.remote_observations))
        if getattr(args, "remote_observations", None)
        else None
    )
    validate_contract(plan)
    validate_contract(execution_sets)
    try:
        diagnostics = _external_oidc_diagnostics(
            plan,
            execution_sets,
            args.enabled_external_oidc_targets,
            remote_observations,
        )
    except RuntimeError as exc:
        diagnostics_document = _diagnostics_from_error(exc)
        if diagnostics_document is None:
            raise
        _write_json(Path(args.diagnostics_out), diagnostics_document)
        sys.stderr.write(json.dumps(diagnostics_document) + "\n")
        return 1
    if diagnostics:
        _write_json(
            Path(args.diagnostics_out), _diagnostics_document(diagnostics)
        )
        return 1
    return 0


def _cmd_matrix_outputs(args: argparse.Namespace) -> int:
    plan = _read_json(Path(args.plan))
    execution_sets = _read_json(Path(args.execution_sets))
    plan_id = str(plan["envelope"]["plan-id"])
    variant_matrix = [
        {"variant-id": variant_id, "runner": _variant_runner(plan, variant_id)}
        for variant_id in sorted(execution_sets["active-variant-ids"])
    ]
    selectors = execution_sets["active-publish-selectors"]
    reusable_classes = _reusable_publish_classes(plan, selectors)
    reusable = sorted(
        node_id
        for node_ids in reusable_classes.values()
        for node_id in node_ids
    )
    entry = sorted(selectors["external-oidc-entry-workflow"])
    entry_proof_matrix = _entry_proof_upload_matrix(
        plan, entry, args.run_id, args.attempt
    )
    selected_gh = execution_sets["selected-github-release-publish-node-ids"]
    active_gh = execution_sets["active-github-release-publish-node-ids"]
    outputs = {
        "plan_id": plan_id,
        "plan_artifact_name": artifact_name(
            "plan",
            ArtifactNameInputs(args.run_id, args.attempt, plan_id=plan_id),
        ),
        "execution_sets_artifact_name": artifact_name(
            "execution-sets",
            ArtifactNameInputs(args.run_id, args.attempt, plan_id=plan_id),
        ),
        "entry_publish_handoff_artifact_name": artifact_name(
            "entry-publish-handoff",
            ArtifactNameInputs(args.run_id, args.attempt, plan_id=plan_id),
        ),
        "tag_result_artifact_name": artifact_name(
            "tag-result",
            ArtifactNameInputs(args.run_id, args.attempt, plan_id=plan_id),
        ),
        "variant_ids": json.dumps(
            execution_sets["active-variant-ids"], separators=(",", ":")
        ),
        "variant_matrix": json.dumps(variant_matrix, separators=(",", ":")),
        "reusable_publish_node_ids": json.dumps(
            reusable, separators=(",", ":")
        ),
        "reusable_github_release_publish_node_ids": json.dumps(
            reusable_classes["github-release"], separators=(",", ":")
        ),
        "reusable_github_packages_publish_node_ids": json.dumps(
            reusable_classes["github-packages"], separators=(",", ":")
        ),
        "reusable_external_oidc_publish_node_ids": json.dumps(
            reusable_classes["external-oidc"], separators=(",", ":")
        ),
        "entry_publish_node_ids": json.dumps(entry, separators=(",", ":")),
        "entry_proof_matrix": json.dumps(
            entry_proof_matrix, separators=(",", ":")
        ),
        "skip_publish_node_ids": json.dumps(
            execution_sets["skip-satisfied-publish-node-ids"],
            separators=(",", ":"),
        ),
        "selected_github_release_node_ids": json.dumps(
            selected_gh, separators=(",", ":")
        ),
        "active_github_release_node_ids": json.dumps(
            active_gh, separators=(",", ":")
        ),
        "has_variants": _bool_str(bool(execution_sets["active-variant-ids"])),
        "has_reusable_publish": _bool_str(bool(reusable)),
        "has_reusable_github_release_publish": _bool_str(
            bool(reusable_classes["github-release"])
        ),
        "has_reusable_github_packages_publish": _bool_str(
            bool(reusable_classes["github-packages"])
        ),
        "has_reusable_external_oidc_publish": _bool_str(
            bool(reusable_classes["external-oidc"])
        ),
        "has_entry_publish": _bool_str(bool(entry)),
        "has_entry_proofs": _bool_str(bool(entry_proof_matrix)),
        "has_selected_github_release": _bool_str(bool(selected_gh)),
        "has_active_github_release": _bool_str(bool(active_gh)),
        "has_skip_results": _bool_str(
            bool(execution_sets["skip-satisfied-publish-node-ids"])
        ),
        "has_live_side_effects": _bool_str(
            bool(execution_sets["active-publish-node-ids"])
        ),
    }
    _write_outputs(args.github_output, outputs)
    return 0


def _cmd_entry_publish_handoff(args: argparse.Namespace) -> int:
    plan = _read_json(Path(args.plan))
    execution_sets = _read_json(Path(args.execution_sets))
    handoff = _entry_publish_handoff(
        plan, execution_sets, args.run_id, args.attempt
    )
    validate_contract(handoff)
    _write_json(Path(args.out), handoff)
    return 0


def _cmd_build_request(args: argparse.Namespace) -> int:
    plan = _read_json(Path(args.plan))
    envelope = plan["envelope"]
    variant = plan["graph"]["variants"][args.variant_id]
    project_id = variant["project-id"]
    artifacts = _build_request_artifacts(plan, variant)
    request = {
        "api-version": "three.release.build-request/v1alpha1",
        "kind": "build-request",
        "plan-id": envelope["plan-id"],
        "profile": envelope["profile"],
        "commit-sha": envelope["commit-sha"],
        "project": envelope["projects"][project_id],
        "variant": variant,
        "artifacts": artifacts,
    }
    validate_contract(request)
    _write_json(Path(args.out), request)
    return 0


def _build_request_artifacts(
    plan: Mapping[str, Any], variant: Mapping[str, Any]
) -> Json:
    """Materialize build-request artifacts with effective build projections."""
    graph = _mapping(plan["graph"], "graph")
    plan_artifacts = _mapping(graph["artifacts"], "graph.artifacts")
    artifacts = {
        str(artifact_id): dict(
            _mapping(
                plan_artifacts[artifact_id], f"graph.artifacts.{artifact_id}"
            )
        )
        for artifact_id in variant["artifact-ids"]
    }
    _materialize_npm_build_projections(graph, artifacts)
    return artifacts


def _materialize_npm_build_projections(
    graph: Mapping[str, Any], artifacts: Json
) -> None:
    """Apply legacy single-artifact npm target projection to build artifacts."""
    target_snapshots = _mapping(
        graph["target-instance-snapshots"], "graph.target-instance-snapshots"
    )
    effective_names: dict[str, str] = {}
    sources: dict[str, str] = {}
    for node_id, raw_node in _mapping(
        graph["publish-nodes"], "graph.publish-nodes"
    ).items():
        node = _mapping(raw_node, f"graph.publish-nodes.{node_id}")
        snapshot_id = str(node["target-instance-snapshot-id"])
        snapshot = _mapping(
            target_snapshots[snapshot_id],
            f"graph.target-instance-snapshots.{snapshot_id}",
        )
        if snapshot.get("family") != "npm":
            continue
        node_artifact_ids = [
            item
            for item in node.get("artifact-ids", [])
            if isinstance(item, str)
        ]
        artifact_keys = {str(artifact_id) for artifact_id in artifacts}
        relevant = [
            artifact_id
            for artifact_id in node_artifact_ids
            if artifact_id in artifact_keys
        ]
        if not relevant:
            continue
        projection = _mapping(
            node.get("projection", {}), "publish-node.projection"
        )
        package_name = projection.get("package-name")
        if not isinstance(package_name, str):
            continue
        if len(node_artifact_ids) != 1:
            msg = (
                f"npm package-name projection for publish node {node_id!r} "
                "references multiple artifacts; use artifact-level projection"
            )
            raise ValueError(msg)
        artifact_id = relevant[0]
        artifact_projection = _mapping(
            artifacts[artifact_id].get("projection", {}),
            f"artifacts.{artifact_id}.projection",
        )
        if isinstance(artifact_projection.get("package-name"), str):
            continue
        previous = effective_names.get(artifact_id)
        if previous is not None and previous != package_name:
            msg = (
                f"conflicting npm package-name projections for artifact "
                f"{artifact_id!r}: {previous!r} from {sources[artifact_id]!r} "
                f"and {package_name!r} from {node_id!r}; use distinct "
                "artifact-level projections"
            )
            raise ValueError(msg)
        effective_names[artifact_id] = package_name
        sources[artifact_id] = str(node_id)
    for artifact_id, package_name in effective_names.items():
        projection = dict(
            _mapping(
                artifacts[artifact_id].get("projection", {}),
                f"artifacts.{artifact_id}.projection",
            )
        )
        projection["package-name"] = package_name
        artifacts[artifact_id]["projection"] = projection


def _cmd_download_publish_inputs(args: argparse.Namespace) -> int:
    plan = _read_json(Path(args.plan))
    expected = _publish_input_names(
        plan, args.publish_node_id, args.run_id, args.attempt
    )
    if args.handoff:
        _validate_handoff_inputs(
            _read_json(Path(args.handoff)), args.publish_node_id, expected
        )
    for name in expected["build-result-artifact-names"]:
        _download_artifact(
            args.repository,
            args.run_id,
            name,
            Path(args.build_results_dir) / name,
        )
    for name in expected["build-bundle-artifact-names"]:
        _download_artifact(
            args.repository, args.run_id, name, Path(args.bundles_dir) / name
        )
    return 0


def _cmd_publish_request(args: argparse.Namespace) -> int:
    plan = _read_json(Path(args.plan))
    if args.handoff:
        _validate_handoff_inputs(
            _read_json(Path(args.handoff)),
            args.publish_node_id,
            _publish_input_names(
                plan, args.publish_node_id, args.run_id, args.attempt
            ),
        )
    request = _publish_request(
        plan,
        args.publish_node_id,
        args.run_id,
        args.attempt,
        Path(args.build_results_dir),
        Path(args.bundles_dir),
    )
    validate_contract(request)
    _write_json(Path(args.out), request)
    return 0


def _cmd_prepare_attestation(args: argparse.Namespace) -> int:
    request = _read_json(Path(args.publish_request))
    snapshot = request["target-instance-snapshot"]
    needs = snapshot["family"] == "github-release"
    artifact_ids = sorted(request["artifacts"].keys()) if needs else []
    Path(args.artifact_ids_out).write_text(
        json.dumps(artifact_ids, separators=(",", ":")) + "\n", encoding="utf-8"
    )
    lines = []
    asset_names = (
        request["publish-node"]
        .get("projection", {})
        .get("asset-names-by-artifact-id", {})
    )
    for artifact_id in artifact_ids:
        entry = request["artifacts"][artifact_id]
        subject_name = asset_names.get(artifact_id)
        if not isinstance(subject_name, str) or not subject_name:
            msg = f"missing planned GitHub Release asset name for {artifact_id}"
            raise ValueError(msg)
        lines.append(f"{entry['sha256']}  {subject_name}")
    Path(args.checksums_out).write_text(
        "\n".join(lines) + ("\n" if lines else ""), encoding="utf-8"
    )
    _write_outputs(args.github_output, {"needs_attestation": _bool_str(needs)})
    return 0


def _cmd_attach_attestation(args: argparse.Namespace) -> int:
    request = _read_json(Path(args.publish_request))
    artifact_ids = json.loads(
        Path(args.artifact_ids).read_text(encoding="utf-8")
    )
    if artifact_ids:
        payload = {
            artifact_id: {
                "attestation-id": args.attestation_id,
                "attestation-url": args.attestation_url,
                "bundle-path": args.bundle_path,
            }
            for artifact_id in artifact_ids
        }
        if args.storage_record_ids:
            for value in payload.values():
                value["storage-record-ids"] = args.storage_record_ids
        request["github-release-asset-attestations"] = payload
    validate_contract(request)
    _write_json(Path(args.out), request)
    return 0


def _cmd_generate_proofs(args: argparse.Namespace) -> int:
    plan = _read_json(Path(args.plan))
    publish_request = _read_json(Path(args.publish_request))
    publish_result = _read_json(Path(args.publish_result))
    validate_contract(plan)
    validate_contract(publish_request)
    validate_contract(publish_result)
    if publish_request["publish-node-id"] != args.publish_node_id:
        msg = "publish request node does not match requested proof node"
        raise ValueError(msg)
    if publish_result["publish-node-id"] != args.publish_node_id:
        msg = "publish result node does not match requested proof node"
        raise ValueError(msg)

    artifact_ids = (
        _read_json(Path(args.artifact_ids_json))
        if args.artifact_ids_json
        else _workflow_artifact_ids_by_name(args.repository, args.run_id)
    )
    run = {
        "repository": args.repository,
        "workflow": args.workflow,
        "run-id": args.run_id,
        "run-attempt": args.attempt,
        "head-sha": args.head_sha,
        "live": True,
        "dry-run": False,
        "validation-only": False,
    }
    proofs = _proof_documents(
        plan,
        publish_request,
        publish_result,
        args.publish_node_id,
        run,
        artifact_ids,
        Path(args.build_results_dir),
        args.run_id,
        args.attempt,
    )
    out_dir = Path(args.out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    manifest_entries = []
    for name, proof in proofs:
        validate_contract(proof)
        file_name = f"{name}.json"
        _write_json(out_dir / file_name, proof)
        manifest_entries.append({"name": name, "file": file_name})
    manifest = {
        "proofs": manifest_entries,
        "proof_artifact_names": [entry["name"] for entry in manifest_entries],
    }
    _write_json(Path(args.manifest_out), manifest)
    _write_outputs(
        args.github_output,
        {
            "has_proofs": _bool_str(bool(manifest_entries)),
            "proof_matrix": json.dumps(manifest_entries, separators=(",", ":")),
        },
    )
    return 0


def _cmd_skip_results(args: argparse.Namespace) -> int:
    plan = _read_json(Path(args.plan))
    execution_sets = _read_json(Path(args.execution_sets))
    out_dir = Path(args.out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    plan_id = str(plan["envelope"]["plan-id"])
    names: list[str] = []
    for node_id in execution_sets["skip-satisfied-publish-node-ids"]:
        node = plan["graph"]["publish-nodes"][node_id]
        snapshot_id = node["target-instance-snapshot-id"]
        result = {
            "api-version": "three.release.skip-result/v1alpha1",
            "kind": "skip-result",
            "plan-id": plan_id,
            "project-id": node["project-id"],
            "publish-node-id": node_id,
            "target-instance-snapshot-id": snapshot_id,
            "resolved-publish-identity": node["resolved-publish-identity"],
            "outcome": "skip-satisfied",
            "reason-source": "planner",
            "evidence": {"planner-disposition": "skip-satisfied"},
        }
        validate_contract(result)
        name = artifact_name(
            "skip-result",
            ArtifactNameInputs(
                args.run_id,
                args.attempt,
                plan_id=plan_id,
                publish_node_id=node_id,
            ),
        )
        item_dir = out_dir / name
        item_dir.mkdir(parents=True, exist_ok=True)
        _write_json(item_dir / "skip-result.json", result)
        names.append(name)
    _write_json(Path(args.manifest_out), {"skip-result-artifact-names": names})
    return 0


def _cmd_observe_remote_publications(args: argparse.Namespace) -> int:
    plan = _read_json(Path(args.plan))
    execution_sets = (
        _read_json(Path(args.execution_sets))
        if getattr(args, "execution_sets", None)
        else None
    )
    try:
        enabled = _normalize_enablement(
            getattr(args, "enabled_external_oidc_targets", ""), plan
        )
    except RuntimeError as exc:
        diagnostics_document = _diagnostics_from_error(exc)
        if diagnostics_document is None:
            raise
        _write_json(Path(args.diagnostics_out), diagnostics_document)
        sys.stderr.write(json.dumps(diagnostics_document) + "\n")
        return 1
    diagnostics: list[Json] = []
    observations: dict[str, str] = {}
    for node_id, node in sorted(plan["graph"]["publish-nodes"].items()):
        snapshot_id = node["target-instance-snapshot-id"]
        snapshot = plan["graph"]["target-instance-snapshots"][snapshot_id]
        try:
            observation = _maybe_observe_remote_publication(
                args.repository,
                str(plan["envelope"]["commit-sha"]),
                node_id,
                node,
                snapshot_id,
                snapshot,
                execution_sets,
                enabled,
            )
        except (RuntimeError, TypeError, ValueError) as exc:
            diagnostics.append(
                _publish_node_diagnostic(
                    "REMOTE_CLASSIFICATION_FAILED",
                    "remote publication lookup failed",
                    node_id,
                    node,
                    snapshot_id,
                    details={"error": str(exc)},
                )
            )
            continue
        if observation is not None:
            observations[node_id] = observation
    if diagnostics:
        _write_json(
            Path(args.diagnostics_out), _diagnostics_document(diagnostics)
        )
        return 1
    _write_json(Path(args.out), observations)
    return 0


def _maybe_observe_remote_publication(
    repository: str,
    commit_sha: str,
    node_id: str,
    node: Json,
    snapshot_id: str,
    snapshot: Mapping[str, Any],
    execution_sets: Mapping[str, Any] | None,
    enabled: set[str],
) -> str | None:
    if snapshot["family"] == "github-release":
        return _observe_github_release_publication(repository, commit_sha, node)
    if _supports_github_packages_remote_observation(
        snapshot
    ) and _requires_live_github_token_remote_observation(
        node_id, execution_sets
    ):
        return _observe_github_packages_publication(
            repository,
            node,
            snapshot,
        )
    if _supports_public_registry_remote_observation(
        snapshot
    ) and _requires_live_external_remote_observation(
        node_id, node, snapshot_id, snapshot, execution_sets, enabled
    ):
        return _observe_public_registry_publication(node, snapshot)
    return None


def _cmd_ensure_tags(args: argparse.Namespace) -> int:
    plan = _read_json(Path(args.plan))
    execution_sets = _read_json(Path(args.execution_sets))
    plan_id = str(plan["envelope"]["plan-id"])
    commit_sha = str(plan["envelope"]["commit-sha"])
    selected = set(execution_sets["selected-github-release-publish-node-ids"])
    active = set(execution_sets["active-github-release-publish-node-ids"])
    tags: dict[str, dict[str, bool]] = {}
    for node_id in selected:
        node = plan["graph"]["publish-nodes"][node_id]
        tag = str(node["resolved-publish-identity"]["release-tag"])
        requirement = tags.setdefault(
            tag, {"can-create": False, "requires-existing": False}
        )
        requirement["can-create"] = (
            requirement["can-create"] or node_id in active
        )
        requirement["requires-existing"] = (
            requirement["requires-existing"]
            or node.get("publish-disposition") != "publish"
        )
    tag_results = []
    missing: list[str] = []
    for tag, requirement in sorted(tags.items()):
        peeled = _remote_tag_commit(args.repository, tag)
        if peeled is None:
            if requirement["requires-existing"]:
                msg = (
                    f"required tag {tag!r} is missing but at least one "
                    "skip-satisfied node references it"
                )
                raise RuntimeError(msg)
            if requirement["can-create"]:
                missing.append(tag)
            continue
        if peeled != commit_sha:
            msg = f"required tag {tag!r} points to {peeled}, expected {commit_sha}"
            raise RuntimeError(msg)
        tag_results.append(
            {
                "release-tag": tag,
                "outcome": "verified",
                "expected-commit-sha": commit_sha,
                "peeled-commit-sha": peeled,
            }
        )
    for tag in missing:
        _gh_api(
            args.repository,
            f"repos/{args.repository}/git/refs",
            method="POST",
            fields={"ref": f"refs/tags/{tag}", "sha": commit_sha},
        )
        tag_results.append(
            {
                "release-tag": tag,
                "outcome": "created",
                "expected-commit-sha": commit_sha,
                "peeled-commit-sha": commit_sha,
            }
        )
    result = {
        "api-version": "three.release.tag-result/v1alpha1",
        "kind": "tag-result",
        "plan-id": plan_id,
        "commit-sha": commit_sha,
        "tags": sorted(tag_results, key=lambda item: item["release-tag"]),
    }
    validate_contract(result)
    _write_json(Path(args.out), result)
    return 0


def _cmd_report(args: argparse.Namespace) -> int:
    plan = _read_optional_json(args.plan)
    execution_sets = _read_optional_json(args.execution_sets)
    plan_id = plan["envelope"]["plan-id"] if plan else None
    selected_projects = (
        plan["envelope"]["selected-project-ids"] if plan else None
    )
    artifact_names = _collect_artifact_names(
        Path(args.artifacts_root) if args.artifacts_root else None
    )
    artifacts_root = Path(args.artifacts_root) if args.artifacts_root else None
    report = {
        "api-version": "three.release.report/v1alpha1",
        "kind": "release-report",
        "run": {
            "repository": args.repository,
            "workflow": args.workflow,
            "run-id": args.run_id,
            "run-attempt": args.attempt,
            "head-sha": args.head_sha,
            "profile": args.profile,
            "dry-run": _parse_bool(args.dry_run),
            "validation-build": _parse_bool(args.validation_build),
            "canary-override-non-public-ref": _parse_bool(
                args.canary_override_non_public_ref
            ),
            "conclusion": _overall_conclusion(args),
        },
        "plan": {"plan-id": plan_id, "selected-project-ids": selected_projects},
        "artifacts": artifact_names,
        "jobs": {
            "authorize-entry": {"conclusion": args.authorize_conclusion},
            "validate-authoring": {"conclusion": args.validate_conclusion},
            "dotnet-metadata": {"conclusion": args.metadata_conclusion},
            "plan": {"conclusion": args.plan_conclusion},
            "build": {
                "conclusion": args.build_conclusion,
                "failed-variant-ids": _failed_variant_ids(
                    args.build_conclusion,
                    plan,
                    execution_sets,
                    artifacts_root,
                    artifact_names,
                ),
            },
            "ensure-tag": {"conclusion": args.tag_conclusion},
            "publish": {
                "conclusion": args.publish_conclusion,
                "failed-publish-node-ids": _failed_publish_node_ids(
                    args.publish_conclusion,
                    plan,
                    execution_sets,
                    artifacts_root,
                    artifact_names,
                ),
            },
        },
        "counts": _report_counts(plan, execution_sets, artifact_names),
    }
    validate_contract(report)
    _write_json(Path(args.out), report)
    _append_summary(report)
    return 0


def _external_oidc_diagnostics(
    plan: Json,
    execution_sets: Json,
    enablement: str,
    remote_observations: Mapping[str, Any] | None = None,
) -> list[Json]:
    if plan["envelope"]["profile"] != "official":
        return []
    if (
        execution_sets["dry-run"]
        or not execution_sets["active-publish-node-ids"]
    ):
        return []
    enabled = _normalize_enablement(enablement, plan)
    diagnostics: list[Json] = []
    for node_id in execution_sets["active-publish-node-ids"]:
        node = plan["graph"]["publish-nodes"][node_id]
        snapshot_id = node["target-instance-snapshot-id"]
        snapshot = plan["graph"]["target-instance-snapshots"][snapshot_id]
        capabilities = snapshot["capabilities"]
        if capabilities["credential-posture"] != "oidc":
            continue
        topology = capabilities["publish-topology"]
        if topology not in _TOPOLOGIES:
            diagnostics.append(
                _publish_node_diagnostic(
                    "REQ_EXTERNAL_TOPOLOGY_BLOCKED",
                    "selected official external OIDC target uses an unsupported topology",
                    node_id,
                    node,
                    snapshot_id,
                    details={
                        "target-family": snapshot["family"],
                        "publish-topology": topology,
                    },
                )
            )
            continue
        identity = node["resolved-publish-identity"]
        token = f"{snapshot_id}#{node['project-id']}#{identity['package-name']}"
        if token not in enabled:
            diagnostics.append(
                _publish_node_diagnostic(
                    "REQ_EXTERNAL_TARGET_DISABLED",
                    "selected official external OIDC target is not live-enabled",
                    node_id,
                    node,
                    snapshot_id,
                    details={
                        "required-enable-token": token,
                        "target-instance-ref": snapshot_id,
                        "project-id": node["project-id"],
                        "resolved-publish-identity": identity,
                    },
                )
            )
            continue
        if _supports_remote_observation(snapshot):
            if (
                remote_observations is not None
                and node_id in remote_observations
            ):
                continue
            diagnostics.append(
                _publish_node_diagnostic(
                    "REMOTE_CLASSIFICATION_FAILED",
                    "authoritative remote observation is missing for selected "
                    "official external OIDC target",
                    node_id,
                    node,
                    snapshot_id,
                    details={
                        "remote-observation": "missing",
                        "target-family": snapshot["family"],
                        "target-instance-ref": snapshot_id,
                        "publish-topology": topology,
                    },
                )
            )
            continue
        diagnostics.append(
            _publish_node_diagnostic(
                "REMOTE_CLASSIFICATION_FAILED",
                "authoritative remote observation is unsupported for selected "
                "official external OIDC target",
                node_id,
                node,
                snapshot_id,
                details={
                    "remote-observation": "unsupported",
                    "target-family": snapshot["family"],
                    "target-instance-ref": snapshot_id,
                    "publish-topology": topology,
                },
            )
        )
    return diagnostics


def _variant_runner(plan: Json, variant_id: str) -> str:
    variant = plan["graph"]["variants"][variant_id]
    project = plan["envelope"]["projects"][variant["project-id"]]
    if project["ecosystem"] == "dotnet":
        if _dotnet_variant_has_executable_artifact(plan, variant):
            runner = _runner_for_variant_dimensions(variant.get("dimensions"))
            if runner is not None:
                return runner
        return "windows-latest"
    return "ubuntu-latest"


def _dotnet_variant_has_executable_artifact(plan: Json, variant: Json) -> bool:
    artifact_ids = variant.get("artifact-ids")
    if not isinstance(artifact_ids, list):
        return False
    artifacts = plan["graph"]["artifacts"]
    return any(
        isinstance(artifact_id, str)
        and artifacts[artifact_id].get("concrete-kind") == "executable"
        for artifact_id in artifact_ids
    )


def _runner_for_variant_dimensions(dimensions: object) -> str | None:
    if not isinstance(dimensions, dict):
        return None
    os_value = dimensions.get("os")
    if isinstance(os_value, str):
        runner = _runner_for_os_token(os_value)
        if runner is not None:
            return runner
    rid = dimensions.get("rid")
    if isinstance(rid, str):
        return _runner_for_os_token(rid.split("-", 1)[0])
    return None


def _runner_for_os_token(value: str) -> str | None:
    normalized = value.casefold()
    if normalized in {"windows", "win"}:
        return "windows-latest"
    if normalized == "linux":
        return "ubuntu-latest"
    if normalized in {"macos", "osx"}:
        return "macos-latest"
    return None


def _publish_permission_class(plan: Json, publish_node_id: str) -> str:
    publish_node = plan["graph"]["publish-nodes"][publish_node_id]
    target_id = publish_node["target-instance-snapshot-id"]
    target = plan["graph"]["target-instance-snapshots"][target_id]
    topology = target["capabilities"]["publish-topology"]
    if topology.startswith("external-oidc-"):
        return "external-oidc"
    if target.get("family") == "github-release":
        return "github-release"
    destination_host = target.get("destination", {}).get("host", "")
    if target.get(
        "instance-id"
    ) == "github-packages" or destination_host.endswith("pkg.github.com"):
        return "github-packages"
    msg = (
        "unsupported reusable publish permission class for "
        f"{publish_node_id}: target {target_id}"
    )
    raise RuntimeError(msg)


def _reusable_publish_classes(
    plan: Json, selectors: Mapping[str, list[str]]
) -> dict[str, list[str]]:
    classes = {
        "github-release": [],
        "github-packages": [],
        "external-oidc": [],
    }
    reusable = sorted(
        selectors["github-token"]
        + selectors["external-oidc-caller-workflow"]
        + selectors["external-oidc-reusable-workflow"]
    )
    for publish_node_id in reusable:
        classes[_publish_permission_class(plan, publish_node_id)].append(
            publish_node_id
        )
    return classes


def _entry_publish_handoff(
    plan: Json, execution_sets: Json, run_id: int, attempt: int
) -> Json:
    plan_id = str(plan["envelope"]["plan-id"])
    entry_node_ids = sorted(
        execution_sets["active-publish-selectors"][
            "external-oidc-entry-workflow"
        ]
    )
    inputs = {
        node_id: _publish_input_names(plan, node_id, run_id, attempt)
        for node_id in entry_node_ids
    }
    return {
        "api-version": "three.release.entry-publish-handoff/v1alpha1",
        "kind": "entry-publish-handoff",
        "plan-id": plan_id,
        "commit-sha": plan["envelope"]["commit-sha"],
        "plan-artifact-name": artifact_name(
            "plan", ArtifactNameInputs(run_id, attempt, plan_id=plan_id)
        ),
        "execution-sets-artifact-name": artifact_name(
            "execution-sets",
            ArtifactNameInputs(run_id, attempt, plan_id=plan_id),
        ),
        "entry-publish-node-ids": entry_node_ids,
        "publish-inputs-by-node-id": inputs,
    }


def _publish_input_names(
    plan: Json, publish_node_id: str, run_id: int, attempt: int
) -> Json:
    plan_id = str(plan["envelope"]["plan-id"])
    node = plan["graph"]["publish-nodes"][publish_node_id]
    variant_ids = sorted(
        {
            plan["graph"]["artifacts"][artifact_id]["variant-id"]
            for artifact_id in node["artifact-ids"]
        }
    )
    return {
        "target-instance-snapshot-id": node["target-instance-snapshot-id"],
        "build-result-artifact-names": [
            artifact_name(
                "build-result",
                ArtifactNameInputs(
                    run_id, attempt, plan_id=plan_id, variant_id=variant_id
                ),
            )
            for variant_id in variant_ids
        ],
        "build-bundle-artifact-names": [
            artifact_name(
                "variant-bundle",
                ArtifactNameInputs(
                    run_id, attempt, plan_id=plan_id, variant_id=variant_id
                ),
            )
            for variant_id in variant_ids
        ],
    }


def _validate_handoff_inputs(
    handoff: Json, publish_node_id: str, expected: Json
) -> None:
    validate_contract(handoff)
    if publish_node_id not in handoff["entry-publish-node-ids"]:
        msg = f"{publish_node_id} is not present in entry-publish-handoff.json"
        raise ValueError(msg)
    actual = handoff["publish-inputs-by-node-id"][publish_node_id]
    if actual != expected:
        msg = f"{publish_node_id} build input names do not match the handoff"
        raise ValueError(msg)


def _download_artifact(
    repository: str, run_id: int, artifact_name_value: str, destination: Path
) -> None:
    destination.mkdir(parents=True, exist_ok=True)
    env = os.environ.copy()
    if "GH_TOKEN" not in env and "GITHUB_TOKEN" in env:
        env["GH_TOKEN"] = env["GITHUB_TOKEN"]
    result = subprocess.run(
        [
            "gh",
            "run",
            "download",
            str(run_id),
            "--repo",
            repository,
            "--name",
            artifact_name_value,
            "--dir",
            str(destination),
        ],
        cwd=Path.cwd(),
        env=env,
        capture_output=True,
        text=True,
        check=False,
    )
    if result.returncode != 0:
        msg = (
            f"failed to download required artifact {artifact_name_value}: "
            f"{result.stderr.strip()}"
        )
        raise RuntimeError(msg)


def _ci_observed_artifact_download_names(
    assignments: Mapping[str, object],
) -> list[str]:
    items = assignments.get("assignments")
    if not isinstance(items, Sequence) or isinstance(items, str | bytes):
        return []
    names: list[str] = []
    seen: set[str] = set()
    for item in items:
        if not isinstance(item, Mapping):
            continue
        for field in ("receipt-artifact-ref", "writer-observation-ref"):
            artifact_ref = item.get(field)
            if not isinstance(artifact_ref, str) or not artifact_ref:
                continue
            name = artifact_physical_name(artifact_ref)
            if name in seen:
                continue
            seen.add(name)
            names.append(name)
    return names


def _workflow_artifact_ids_by_name(repository: str, run_id: int) -> Json:
    artifacts: Json = {}
    page = 1
    while True:
        payload = _gh_api(
            repository,
            f"repos/{repository}/actions/runs/{run_id}/artifacts?per_page=100&page={page}",
        )
        items = payload.get("artifacts", [])
        if not items:
            break
        for item in items:
            if not item.get("expired", False):
                artifacts[str(item["name"])] = int(item["id"])
        if len(items) < 100:
            break
        page += 1
    return artifacts


def _normalize_enablement(value: str, plan: Json) -> set[str]:
    known_oidc = _known_oidc_target_instance_refs()
    tokens = sorted(
        {
            item.strip()
            for chunk in value.split("\n")
            for item in chunk.split(",")
            if item.strip()
        }
    )
    for token in tokens:
        parts = token.split("#")
        if (
            len(parts) != 3
            or any(part == "" or "*" in part for part in parts)
            or parts[0] not in known_oidc
        ):
            diag = _diagnostic(
                "REQ_INVALID_INPUT",
                "validation",
                "request",
                "invalid external OIDC live-enable token",
                {"token": token},
            )
            raise RuntimeError(json.dumps(_diagnostics_document([diag])))
    return set(tokens)


def _known_oidc_target_instance_refs() -> set[str]:
    catalog = yaml.safe_load(
        (_REPO_ROOT / "eng/release/target-instances.yml").read_text(
            encoding="utf-8"
        )
    )
    if not isinstance(catalog, Mapping):
        return set()
    families = catalog.get("families", {})
    if not isinstance(families, Mapping):
        return set()
    return {
        f"{family_id}/{instance['id']}"
        for family_id, family in families.items()
        if isinstance(family, Mapping)
        for instance in family.get("instances", [])
        if isinstance(instance, Mapping)
        if instance.get("capabilities", {}).get("credential-posture") == "oidc"
    }


def _diagnostics_from_error(error: RuntimeError) -> Json | None:
    text = str(error)
    if not text.startswith("{"):
        return None
    try:
        document = json.loads(text)
    except json.JSONDecodeError:
        return None
    if not isinstance(document, dict):
        return None
    if document.get("kind") != "planner-diagnostics":
        return None
    validate_contract(document)
    return document


def _publish_request(
    plan: Json,
    node_id: str,
    run_id: int,
    attempt: int,
    build_results_dir: Path,
    bundles_dir: Path,
) -> Json:
    envelope = plan["envelope"]
    node = plan["graph"]["publish-nodes"][node_id]
    project_id = node["project-id"]
    snapshot_id = node["target-instance-snapshot-id"]
    artifacts: Json = {}
    for artifact_id in node["artifact-ids"]:
        artifact = plan["graph"]["artifacts"][artifact_id]
        variant_id = artifact["variant-id"]
        build_name = artifact_name(
            "build-result",
            ArtifactNameInputs(
                run_id,
                attempt,
                plan_id=envelope["plan-id"],
                variant_id=variant_id,
            ),
        )
        bundle_name = artifact_name(
            "variant-bundle",
            ArtifactNameInputs(
                run_id,
                attempt,
                plan_id=envelope["plan-id"],
                variant_id=variant_id,
            ),
        )
        build_result = _read_json(
            build_results_dir / build_name / "build-result.json"
        )
        receipt = build_result["artifacts"][artifact_id]
        artifacts[artifact_id] = {
            "artifact": artifact,
            "input-path": (
                bundles_dir / bundle_name / receipt["bundle-relative-path"]
            ).as_posix(),
            "bundle-relative-path": receipt["bundle-relative-path"],
            "sha256": receipt["sha256"],
            "byte-size": receipt["byte-size"],
        }
        if "archive" in receipt:
            artifacts[artifact_id]["archive"] = receipt["archive"]
    return {
        "api-version": "three.release.publish-request/v1alpha1",
        "kind": "publish-request",
        "plan-id": envelope["plan-id"],
        "profile": envelope["profile"],
        "commit-sha": envelope["commit-sha"],
        "publish-node-id": node_id,
        "project": envelope["projects"][project_id],
        "publish-node": {**node, "publish-node-id": node_id},
        "target-instance-snapshot": plan["graph"]["target-instance-snapshots"][
            snapshot_id
        ],
        "artifacts": artifacts,
    }


def _proof_documents(
    plan: Json,
    publish_request: Json,
    publish_result: Json,
    publish_node_id: str,
    run: Json,
    artifact_ids_by_name: Json,
    build_results_dir: Path,
    run_id: int,
    attempt: int,
) -> list[tuple[str, Json]]:
    from three_workflow_release_proof import (  # noqa: PLC0415
        github_release_asset_proofs,
        immutable_proofs,
    )

    node = plan["graph"]["publish-nodes"][publish_node_id]
    snapshot = plan["graph"]["target-instance-snapshots"][
        node["target-instance-snapshot-id"]
    ]
    family = snapshot["family"]
    proofs: list[tuple[str, Json]] = []
    if family in {"nuget", "pypi", "npm", "rubygems"}:
        plan_id = str(plan["envelope"]["plan-id"])
        for variant_id in _publish_node_variant_ids(plan, publish_node_id):
            build_result_name = artifact_name(
                "build-result",
                ArtifactNameInputs(
                    run_id, attempt, plan_id=plan_id, variant_id=variant_id
                ),
            )
            bundle_name = artifact_name(
                "variant-bundle",
                ArtifactNameInputs(
                    run_id, attempt, plan_id=plan_id, variant_id=variant_id
                ),
            )
            build_result = _read_json(
                build_results_dir / build_result_name / "build-result.json"
            )
            build_result_artifact_id = artifact_ids_by_name.get(
                build_result_name
            )
            if not isinstance(build_result_artifact_id, int):
                msg = f"missing Actions artifact id for {build_result_name}"
                raise TypeError(msg)
            for proof in immutable_proofs(
                plan=plan,
                build_result=build_result,
                publish_node_id=publish_node_id,
                run=run,
                build_result_artifact_name=build_result_name,
                build_result_artifact_id=build_result_artifact_id,
                bundle_artifact_name=bundle_name,
            ):
                binding = proof["binding"]
                if not isinstance(binding, Mapping):
                    msg = "immutable proof binding must be an object"
                    raise TypeError(msg)
                name = artifact_name(
                    "immutable-proof",
                    ArtifactNameInputs(
                        run_id,
                        attempt,
                        binding_json=immutable_binding_json(
                            publish_node_id=str(binding["publish-node-id"]),
                            artifact_id=str(binding["artifact-id"]),
                            package_name=str(binding["package-name"]),
                            version=str(binding["version"]),
                        ),
                    ),
                )
                proofs.append((name, proof))
    if family == "github-release":
        for proof in github_release_asset_proofs(
            publish_request=publish_request,
            publish_result=publish_result,
            run=run,
        ):
            binding = proof["binding"]
            if not isinstance(binding, Mapping):
                msg = "GitHub Release asset proof binding must be an object"
                raise TypeError(msg)
            name = artifact_name(
                "github-release-asset-proof",
                ArtifactNameInputs(
                    run_id,
                    attempt,
                    binding_json=github_release_asset_binding_json(
                        publish_node_id=str(binding["publish-node-id"]),
                        artifact_id=str(binding["artifact-id"]),
                        release_tag=str(binding["release-tag"]),
                        asset_name=str(binding["asset-name"]),
                    ),
                ),
            )
            proofs.append((name, proof))
    return proofs


def _entry_proof_upload_matrix(
    plan: Json,
    publish_node_ids: Sequence[str],
    run_id: int,
    attempt: int,
) -> list[Json]:
    plan_id = str(plan["envelope"]["plan-id"])
    matrix: list[Json] = []
    for publish_node_id in publish_node_ids:
        publish_result_name = artifact_name(
            "publish-result",
            ArtifactNameInputs(
                run_id,
                attempt,
                plan_id=plan_id,
                publish_node_id=publish_node_id,
            ),
        )
        staging_artifact_name = f"proof-staging-{publish_result_name}"
        for name in _planned_proof_artifact_names(
            plan, publish_node_id, run_id, attempt
        ):
            matrix.append(
                {
                    "name": name,
                    "file": f"{name}.json",
                    "staging-artifact-name": staging_artifact_name,
                }
            )
    return matrix


def _planned_proof_artifact_names(
    plan: Json,
    publish_node_id: str,
    run_id: int,
    attempt: int,
) -> list[str]:
    node = plan["graph"]["publish-nodes"][publish_node_id]
    snapshot = plan["graph"]["target-instance-snapshots"][
        node["target-instance-snapshot-id"]
    ]
    family = snapshot["family"]
    names: list[str] = []
    if family in {"nuget", "pypi", "npm", "rubygems"}:
        identity = node["resolved-publish-identity"]
        for artifact_id in node["artifact-ids"]:
            names.append(
                artifact_name(
                    "immutable-proof",
                    ArtifactNameInputs(
                        run_id,
                        attempt,
                        binding_json=immutable_binding_json(
                            publish_node_id=publish_node_id,
                            artifact_id=str(artifact_id),
                            package_name=str(identity["package-name"]),
                            version=str(identity["version"]),
                        ),
                    ),
                )
            )
    if family == "github-release":
        identity = node["resolved-publish-identity"]
        asset_names = node["projection"]["asset-names-by-artifact-id"]
        for artifact_id in node["artifact-ids"]:
            names.append(
                artifact_name(
                    "github-release-asset-proof",
                    ArtifactNameInputs(
                        run_id,
                        attempt,
                        binding_json=github_release_asset_binding_json(
                            publish_node_id=publish_node_id,
                            artifact_id=str(artifact_id),
                            release_tag=str(identity["release-tag"]),
                            asset_name=str(asset_names[artifact_id]),
                        ),
                    ),
                )
            )
    return names


def _publish_node_variant_ids(plan: Json, publish_node_id: str) -> list[str]:
    node = plan["graph"]["publish-nodes"][publish_node_id]
    return sorted(
        {
            plan["graph"]["artifacts"][artifact_id]["variant-id"]
            for artifact_id in node["artifact-ids"]
        }
    )


def _collect_artifact_names(root: Path | None) -> Json:
    result: Json = {
        "plan-artifact-name": None,
        "planner-diagnostics-artifact-name": None,
        "dotnet-planner-metadata-input-artifact-name": None,
        "dotnet-planner-metadata-artifact-name": None,
        "execution-sets-artifact-name": None,
        "entry-publish-handoff-artifact-name": None,
        "tag-result-artifact-name": None,
        "build-result-artifact-names": [],
        "publish-result-artifact-names": [],
        "skip-result-artifact-names": [],
    }
    if root is None or not root.exists():
        return result
    names = sorted(path.name for path in root.iterdir() if path.is_dir())
    prefix_fields = {
        "release-plan-v1-": "plan-artifact-name",
        "release-planner-diagnostics-v1-": "planner-diagnostics-artifact-name",
        "release-dotnet-planner-metadata-input-v1-": "dotnet-planner-metadata-input-artifact-name",
        "release-dotnet-planner-metadata-v1-": "dotnet-planner-metadata-artifact-name",
        "release-execution-sets-v1-": "execution-sets-artifact-name",
        "release-entry-publish-handoff-v1-": "entry-publish-handoff-artifact-name",
        "release-tag-result-v1-": "tag-result-artifact-name",
    }
    arrays = {
        "release-build-result-v1-": "build-result-artifact-names",
        "release-publish-result-v1-": "publish-result-artifact-names",
        "release-skip-result-v1-": "skip-result-artifact-names",
    }
    for name in names:
        for prefix, field in prefix_fields.items():
            if name.startswith(prefix):
                result[field] = name
        for prefix, field in arrays.items():
            if name.startswith(prefix):
                result[field].append(name)
    return result


def _failed_variant_ids(
    conclusion: str,
    plan: Json | None,
    execution_sets: Json | None,
    root: Path | None,
    artifacts: Json,
) -> list[str]:
    if conclusion != "failure" or plan is None or execution_sets is None:
        return []
    if root is None or not root.exists():
        return sorted(execution_sets["active-variant-ids"])
    plan_id = plan["envelope"]["plan-id"]
    succeeded = set()
    for name in artifacts["build-result-artifact-names"]:
        receipt = _read_receipt(root / name / "build-result.json")
        if (
            _is_valid_receipt(receipt)
            and receipt.get("kind") == "build-result"
            and receipt.get("api-version")
            == "three.release.build-result/v1alpha1"
            and receipt.get("plan-id") == plan_id
        ):
            variant_id = receipt.get("variant-id")
            if isinstance(variant_id, str):
                succeeded.add(variant_id)
    return sorted(set(execution_sets["active-variant-ids"]) - succeeded)


def _failed_publish_node_ids(
    conclusion: str,
    plan: Json | None,
    execution_sets: Json | None,
    root: Path | None,
    artifacts: Json,
) -> list[str]:
    if conclusion != "failure" or plan is None or execution_sets is None:
        return []
    if root is None or not root.exists():
        return sorted(execution_sets["active-publish-node-ids"])
    plan_id = plan["envelope"]["plan-id"]
    succeeded = set()
    for name in artifacts["publish-result-artifact-names"]:
        receipt = _read_receipt(root / name / "publish-result.json")
        if (
            _is_valid_receipt(receipt)
            and receipt.get("kind") == "publish-result"
            and receipt.get("api-version")
            == "three.release.publish-result/v1alpha1"
            and receipt.get("plan-id") == plan_id
        ):
            publish_node_id = receipt.get("publish-node-id")
            if isinstance(publish_node_id, str):
                succeeded.add(publish_node_id)
    return sorted(set(execution_sets["active-publish-node-ids"]) - succeeded)


def _is_valid_receipt(receipt: Json) -> bool:
    try:
        validate_contract(receipt)
    except ContractValidationError:
        return False
    return True


def _read_receipt(path: Path) -> Json:
    try:
        return _read_json(path)
    except (OSError, TypeError, ValueError):
        return {}


def _report_counts(
    plan: Json | None, execution_sets: Json | None, artifacts: Json
) -> Json:
    if plan is None or execution_sets is None:
        return {
            "selected-projects": 0,
            "active-variants": 0,
            "active-publish-nodes": 0,
            "published-nodes": len(artifacts["publish-result-artifact-names"]),
            "skipped-publish-nodes": len(
                artifacts["skip-result-artifact-names"]
            ),
        }
    return {
        "selected-projects": len(plan["envelope"]["selected-project-ids"]),
        "active-variants": len(execution_sets["active-variant-ids"]),
        "active-publish-nodes": len(execution_sets["active-publish-node-ids"]),
        "published-nodes": len(artifacts["publish-result-artifact-names"]),
        "skipped-publish-nodes": len(artifacts["skip-result-artifact-names"]),
    }


def _overall_conclusion(args: argparse.Namespace) -> str:
    conclusions = [
        args.authorize_conclusion,
        args.validate_conclusion,
        args.metadata_conclusion,
        args.plan_conclusion,
        args.build_conclusion,
        args.tag_conclusion,
        args.publish_conclusion,
    ]
    if "failure" in conclusions:
        return "failure"
    if "cancelled" in conclusions:
        return "cancelled"
    if "success" in conclusions:
        return "success"
    return "skipped"


def _append_summary(report: Json) -> None:
    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if not summary:
        return
    lines = [
        "## Workflow Release Report",
        "",
        f"Conclusion: `{report['run']['conclusion']}`",
        f"Plan: `{report['plan']['plan-id']}`",
        "Canary non-public-ref override: "
        f"`{_bool_str(report['run']['canary-override-non-public-ref'])}`",
        "",
        "| Count | Value |",
        "| --- | ---: |",
    ]
    for key, value in report["counts"].items():
        lines.append(f"| {key} | {value} |")
    Path(summary).open("a", encoding="utf-8").write("\n".join(lines) + "\n")


def _actor_permission(repository: str, actor: str) -> str:
    try:
        return str(
            _gh_api(
                repository,
                f"repos/{repository}/collaborators/{urllib.parse.quote(actor, safe='')}/permission",
            )["permission"]
        )
    except (RuntimeError, KeyError, TypeError):
        return "none"


def _resolve_ref(
    repository: str, ref_type: str, ref_name: str
) -> tuple[str | None, Json]:
    api_ref = (
        f"heads/{ref_name}" if ref_type == "branch" else f"tags/{ref_name}"
    )
    try:
        payload = _gh_api(
            repository,
            f"repos/{repository}/git/ref/{urllib.parse.quote(api_ref, safe='/')}",
        )
        obj = payload["object"]
        obj_type = str(obj["type"])
        sha = str(obj["sha"])
        if obj_type == "commit":
            return sha, {"object-type": obj_type}
        if obj_type == "tag":
            return _peel_tag(repository, sha), {
                "object-type": obj_type,
                "tag-object-sha": sha,
            }
    except (RuntimeError, KeyError, TypeError):
        return None, {"error": "ref lookup failed"}
    return None, {"object-type": obj_type}


def _peel_tag(repository: str, sha: str) -> str | None:
    current = sha
    for _ in range(8):
        payload = _gh_api(repository, f"repos/{repository}/git/tags/{current}")
        obj = payload["object"]
        obj_type = str(obj["type"])
        current = str(obj["sha"])
        if obj_type == "commit":
            return current
        if obj_type != "tag":
            return None
    return None


def _trusted_ref(repository: str, ref_type: str, ref_name: str) -> bool:
    if ref_type == "branch":
        repo = _gh_api(repository, f"repos/{repository}")
        if ref_name == repo.get("default_branch"):
            return True
        try:
            branch = _gh_api(
                repository,
                f"repos/{repository}/branches/{urllib.parse.quote(ref_name, safe='')}",
            )
            if branch.get("protected") is True:
                return True
        except RuntimeError:
            pass
        try:
            return bool(
                _gh_api(
                    repository,
                    f"repos/{repository}/rules/branches/{urllib.parse.quote(ref_name, safe='')}",
                )
                or []
            )
        except RuntimeError:
            return False
    return _tag_has_active_ruleset(repository, ref_name)


def _official_public_ref_diagnostics(
    full_ref: str,
    requested_project_ids: Sequence[str],
    *,
    canary_override: bool,
) -> list[Json]:
    projects = _release_project_public_ref_specs()
    selected_ids = (
        list(requested_project_ids)
        if requested_project_ids
        else sorted(projects)
    )
    diagnostics: list[Json] = []
    unknown_ids = [
        project_id for project_id in selected_ids if project_id not in projects
    ]
    for project_id in unknown_ids:
        diagnostics.append(
            _diagnostic(
                "REQ_PROJECT_NOT_FOUND",
                "validation",
                "project",
                f"requested project {project_id!r} is not an in-scope releasable project",
                {"requested-project-id": project_id},
                project_id=project_id,
            )
        )
    selected_known = [
        project_id for project_id in selected_ids if project_id in projects
    ]
    disallowed = [
        project_id
        for project_id in selected_known
        if not _matches_public_release_ref_spec(full_ref, projects[project_id])
    ]
    if not disallowed:
        if (
            canary_override
            and set(selected_ids) - _OFFICIAL_NON_PUBLIC_REF_CANARY_PROJECTS
        ):
            diagnostics.append(_canary_override_scope_diagnostic(selected_ids))
        return diagnostics
    if canary_override:
        if set(selected_ids) <= _OFFICIAL_NON_PUBLIC_REF_CANARY_PROJECTS:
            return diagnostics
        diagnostics.append(_canary_override_scope_diagnostic(selected_ids))
        return diagnostics
    for project_id in disallowed:
        diagnostics.append(
            _diagnostic(
                "REQ_UNTRUSTED_WORKFLOW_REF",
                "validation",
                "project",
                "official release ref does not match the project's NBGV publicReleaseRefSpec",
                {
                    "ref": full_ref,
                    "publicReleaseRefSpec": projects[project_id],
                    "canary-override-non-public-ref": False,
                },
                project_id=project_id,
            )
        )
    return diagnostics


def _canary_override_scope_diagnostic(project_ids: Sequence[str]) -> Json:
    return _diagnostic(
        "REQ_INVALID_INPUT",
        "validation",
        "request",
        "canary non-public-ref override is allowlisted only for dedicated release-smoke projects",
        {
            "requested-project-ids": sorted(project_ids),
            "allowed-project-ids": sorted(
                _OFFICIAL_NON_PUBLIC_REF_CANARY_PROJECTS
            ),
            "canary-override-non-public-ref": True,
        },
    )


def _release_project_public_ref_specs() -> dict[str, list[str]]:
    projects: dict[str, list[str]] = {}
    for descriptor in sorted(_REPO_ROOT.glob("src/**/three.release.yml")):
        document = yaml.safe_load(descriptor.read_text(encoding="utf-8"))
        if not isinstance(document, Mapping):
            continue
        project = document.get("project")
        if not isinstance(project, Mapping) or not isinstance(
            project.get("id"), str
        ):
            continue
        spec = _nearest_public_release_ref_spec(descriptor.parent)
        projects[str(project["id"])] = spec
    return projects


def _nearest_public_release_ref_spec(project_dir: Path) -> list[str]:
    for current in (project_dir, *_repo_relative_parents(project_dir)):
        version_json = current / "version.json"
        if not version_json.is_file():
            continue
        document = json.loads(version_json.read_text(encoding="utf-8"))
        spec = document.get("publicReleaseRefSpec")
        if isinstance(spec, list) and all(
            isinstance(item, str) for item in spec
        ):
            return list(spec)
    return []


def _repo_relative_parents(path: Path) -> list[Path]:
    parents: list[Path] = []
    current = path
    while current != _REPO_ROOT and _REPO_ROOT in current.parents:
        current = current.parent
        parents.append(current)
    return parents


def _matches_public_release_ref_spec(
    full_ref: str, patterns: Sequence[str]
) -> bool:
    return any(re.fullmatch(pattern, full_ref) for pattern in patterns)


def _tag_has_active_ruleset(repository: str, tag_name: str) -> bool:
    try:
        rulesets = _gh_api(
            repository,
            f"repos/{repository}/rulesets?targets=tag&includes_parents=true&per_page=100",
        )
    except RuntimeError:
        return False
    if not isinstance(rulesets, list):
        return False
    for item in rulesets:
        if item.get("enforcement") not in {"active", "enabled"}:
            continue
        ruleset_id = item.get("id")
        if not isinstance(ruleset_id, int):
            continue
        detail = _gh_api(
            repository, f"repos/{repository}/rulesets/{ruleset_id}"
        )
        if (
            detail.get("target") == "tag"
            and detail.get("enforcement") in {"active", "enabled"}
            and _ruleset_ref_matches(detail, tag_name)
        ):
            return True
    return False


def _ruleset_ref_matches(ruleset: Mapping[str, Any], ref_name: str) -> bool:
    ref = f"refs/tags/{ref_name}"
    conditions = ruleset.get("conditions")
    if not isinstance(conditions, Mapping):
        return True
    ref_condition = conditions.get("ref_name")
    if not isinstance(ref_condition, Mapping):
        return True
    include_value = ref_condition.get("include")
    includes = include_value if isinstance(include_value, list) else ["~ALL"]
    exclude_value = ref_condition.get("exclude")
    excludes = exclude_value if isinstance(exclude_value, list) else []
    return any(
        _ref_pattern_matches(str(pattern), ref_name, ref)
        for pattern in includes
    ) and not any(
        _ref_pattern_matches(str(pattern), ref_name, ref)
        for pattern in excludes
    )


def _ref_pattern_matches(pattern: str, name: str, full_ref: str) -> bool:
    if pattern == "~ALL":
        return True
    return fnmatch.fnmatchcase(name, pattern) or fnmatch.fnmatchcase(
        full_ref, pattern
    )


def _remote_tag_commit(repository: str, tag: str) -> str | None:
    try:
        payload = _gh_api(
            repository,
            f"repos/{repository}/git/ref/{urllib.parse.quote(f'tags/{tag}', safe='/')}",
        )
    except RuntimeError as exc:
        if _is_github_not_found_error(exc):
            return None
        raise
    obj = payload["object"]
    if obj["type"] == "commit":
        return str(obj["sha"])
    if obj["type"] == "tag":
        peeled = _peel_tag(repository, str(obj["sha"]))
        if peeled is not None:
            return peeled
        msg = f"existing tag {tag!r} cannot be peeled to a commit"
        raise RuntimeError(msg)
    msg = f"existing tag {tag!r} points to unsupported object type {obj['type']!r}"
    raise RuntimeError(msg)


def _observe_github_release_publication(
    repository: str,
    commit_sha: str,
    node: Json,
) -> str:
    identity = node["resolved-publish-identity"]
    tag = str(identity["release-tag"])
    tag_commit = _remote_tag_commit(repository, tag)
    if tag_commit is not None and tag_commit != commit_sha:
        return "conflicting"
    release = _github_release_by_tag(repository, tag)
    if release is None:
        return "absent"
    if tag_commit is None:
        return "conflicting"
    return _classify_github_release_payload(release, node)


def _observe_pypi_publication(node: Json) -> str:
    package_name, version = _package_publish_identity(node, "PyPI")
    payload = _pypi_project_json(package_name)
    if payload is None:
        return "absent"
    releases = payload.get("releases")
    if not isinstance(releases, Mapping):
        msg = f"PyPI JSON API payload for {package_name!r} is missing releases"
        raise TypeError(msg)
    if version not in releases:
        return "absent"
    files = releases[version]
    if not isinstance(files, list):
        msg = (
            f"PyPI JSON API payload for {package_name!r} version {version!r} "
            "has malformed release files"
        )
        raise TypeError(msg)
    return "exact-satisfied"


def _observe_nuget_publication(node: Json) -> str:
    package_name, version = _package_publish_identity(node, "NuGet")
    planned_version = _nuget_version_key(version)
    payload = _nuget_versions_json(package_name)
    if payload is None:
        return "absent"
    versions = payload.get("versions")
    if not isinstance(versions, list) or not all(
        isinstance(item, str) and item for item in versions
    ):
        msg = (
            f"NuGet flat-container payload for {package_name!r} "
            "is missing valid versions"
        )
        raise TypeError(msg)
    observed = {_nuget_version_key(item) for item in versions}
    if planned_version in observed:
        return "exact-satisfied"
    return "absent"


def _observe_github_packages_nuget_publication(
    repository: str,
    node: Json,
    snapshot: Mapping[str, Any],
) -> str:
    package_name, version = _package_publish_identity(
        node, "GitHub Packages NuGet"
    )
    planned_version = _nuget_version_key(version)
    owner = _github_packages_owner(repository, snapshot)
    package = _github_packages_nuget_package(
        repository,
        owner,
        package_name,
    )
    if package is None:
        return "absent"
    remote_package_name = str(package["name"])
    versions = _github_packages_nuget_versions(
        repository, owner, remote_package_name
    )
    observed: set[str] = set()
    for item in versions:
        if not isinstance(item, Mapping):
            msg = (
                "GitHub Packages NuGet versions API returned a malformed "
                f"version item for {remote_package_name!r}"
            )
            raise TypeError(msg)
        version_name = item.get("name")
        if not isinstance(version_name, str) or not version_name:
            msg = (
                "GitHub Packages NuGet versions API returned a version item "
                f"without name for {remote_package_name!r}"
            )
            raise TypeError(msg)
        observed.add(_nuget_version_key(version_name))
    if planned_version in observed:
        return "exact-satisfied"
    return "absent"


def _observe_github_packages_publication(
    repository: str,
    node: Json,
    snapshot: Mapping[str, Any],
) -> str:
    family = str(snapshot.get("family"))
    if family == "nuget":
        return _observe_github_packages_nuget_publication(
            repository,
            node,
            snapshot,
        )
    package_name, version = _package_publish_identity(
        node, f"GitHub Packages {family}"
    )
    owner = _github_packages_owner(repository, snapshot)
    package = _github_packages_package(
        repository,
        owner,
        family,
        package_name,
    )
    if package is None:
        return "absent"
    remote_package_name = str(package["name"])
    versions = _github_packages_versions(
        repository, owner, family, remote_package_name
    )
    for item in versions:
        if not isinstance(item, Mapping):
            msg = (
                f"GitHub Packages {family} versions API returned a malformed "
                f"version item for {remote_package_name!r}"
            )
            raise TypeError(msg)
        version_name = item.get("name")
        if not isinstance(version_name, str) or not version_name:
            msg = (
                f"GitHub Packages {family} versions API returned a version item "
                f"without name for {remote_package_name!r}"
            )
            raise TypeError(msg)
        if version_name == version:
            return "exact-satisfied"
    return "absent"


def _observe_npm_publication(node: Json) -> str:
    package_name, version = _package_publish_identity(node, "npm")
    payload = _npm_package_json(package_name)
    if payload is None:
        return "absent"
    versions = payload.get("versions")
    if not isinstance(versions, Mapping):
        msg = f"npm registry payload for {package_name!r} is missing versions"
        raise TypeError(msg)
    if version in versions:
        return "exact-satisfied"
    return "absent"


def _observe_rubygems_publication(node: Json) -> str:
    package_name, version = _package_publish_identity(node, "RubyGems")
    payload = _rubygems_versions_json(package_name)
    if payload is None:
        return "absent"
    if not isinstance(payload, list):
        msg = (
            f"RubyGems versions API payload for {package_name!r} is not a list"
        )
        raise TypeError(msg)
    for item in payload:
        if not isinstance(item, Mapping):
            msg = (
                f"RubyGems versions API payload for {package_name!r} "
                "contains a malformed version item"
            )
            raise TypeError(msg)
        number = item.get("number")
        if not isinstance(number, str) or not number:
            msg = (
                f"RubyGems versions API payload for {package_name!r} "
                "contains a version item without number"
            )
            raise TypeError(msg)
        if number == version:
            return "exact-satisfied"
    return "absent"


def _observe_public_registry_publication(
    node: Json, snapshot: Mapping[str, Any]
) -> str:
    family = snapshot.get("family")
    if family == "pypi":
        return _observe_pypi_publication(node)
    if family == "nuget":
        return _observe_nuget_publication(node)
    if family == "npm":
        return _observe_npm_publication(node)
    if family == "rubygems":
        return _observe_rubygems_publication(node)
    msg = f"unsupported public registry family for observation: {family!r}"
    raise RuntimeError(msg)


def _package_publish_identity(
    node: Json, registry_name: str
) -> tuple[str, str]:
    identity = node.get("resolved-publish-identity")
    if not isinstance(identity, Mapping):
        msg = (
            f"{registry_name} publish node is missing resolved-publish-identity"
        )
        raise TypeError(msg)
    package_name = identity.get("package-name")
    version = identity.get("version")
    if not isinstance(package_name, str) or not package_name:
        msg = f"{registry_name} publish identity is missing package-name"
        raise TypeError(msg)
    if not isinstance(version, str) or not version:
        msg = f"{registry_name} publish identity is missing version"
        raise TypeError(msg)
    return package_name, version


def _github_packages_owner(repository: str, snapshot: Mapping[str, Any]) -> str:
    destination = snapshot.get("destination")
    owner = (
        destination.get("owner") if isinstance(destination, Mapping) else None
    )
    if isinstance(owner, str) and owner:
        return owner
    repo_owner, _, _ = repository.partition("/")
    if repo_owner:
        return repo_owner
    msg = "GitHub Packages NuGet target is missing destination owner"
    raise TypeError(msg)


def _github_packages_owner_endpoint_prefix(repository: str, owner: str) -> str:
    repo_owner, separator, repo_name = repository.partition("/")
    if separator and owner == repo_owner:
        payload = _gh_api(
            repository,
            f"repos/{urllib.parse.quote(repo_owner, safe='')}/"
            f"{urllib.parse.quote(repo_name, safe='')}",
        )
        owner_payload = (
            payload.get("owner") if isinstance(payload, Mapping) else None
        )
        owner_type = (
            owner_payload.get("type")
            if isinstance(owner_payload, Mapping)
            else None
        )
    else:
        payload = _gh_api(
            repository, f"users/{urllib.parse.quote(owner, safe='')}"
        )
        owner_type = (
            payload.get("type") if isinstance(payload, Mapping) else None
        )
    if owner_type == "Organization":
        return f"orgs/{urllib.parse.quote(owner, safe='')}"
    if owner_type == "User":
        return f"users/{urllib.parse.quote(owner, safe='')}"
    msg = (
        f"GitHub Packages owner {owner!r} has unsupported or missing "
        f"GitHub owner type {owner_type!r}"
    )
    raise TypeError(msg)


def _github_packages_nuget_package(
    repository: str,
    owner: str,
    package_name: str,
) -> Mapping[str, Any] | None:
    return _github_packages_package(
        repository,
        owner,
        "nuget",
        package_name,
    )


def _github_packages_package(
    repository: str,
    owner: str,
    package_type: str,
    package_name: str,
) -> Mapping[str, Any] | None:
    prefix = _github_packages_owner_endpoint_prefix(repository, owner)
    endpoint = (
        f"{prefix}/packages/{urllib.parse.quote(package_type, safe='')}/"
        f"{urllib.parse.quote(package_name, safe='')}"
    )
    try:
        payload = _gh_api(repository, endpoint)
    except RuntimeError as exc:
        if _is_github_not_found_error(exc):
            sys.stderr.write(
                "GitHub Packages 404 treated as absent; publish remains the "
                "authority for permissions and conflicts: "
                f"package-name={package_name!r}, owner={owner!r}, "
                f"package-type={package_type!r}\n"
            )
            return None
        raise
    if not isinstance(payload, Mapping):
        msg = (
            "GitHub Packages package API returned a malformed package "
            f"payload for {package_name!r}"
        )
        raise TypeError(msg)
    remote_name = payload.get("name")
    if not isinstance(remote_name, str) or not remote_name:
        msg = (
            "GitHub Packages package API returned a package payload without "
            f"name for {package_name!r}"
        )
        raise TypeError(msg)
    return payload


def _github_packages_nuget_versions(
    repository: str, owner: str, package_name: str
) -> list[Any]:
    prefix = _github_packages_owner_endpoint_prefix(repository, owner)
    endpoint = (
        f"{prefix}/packages/nuget/"
        f"{urllib.parse.quote(package_name, safe='')}/versions?per_page=100"
    )
    return _gh_api_paginated(repository, endpoint)


def _github_packages_versions(
    repository: str, owner: str, package_type: str, package_name: str
) -> list[Any]:
    prefix = _github_packages_owner_endpoint_prefix(repository, owner)
    endpoint = (
        f"{prefix}/packages/{urllib.parse.quote(package_type, safe='')}/"
        f"{urllib.parse.quote(package_name, safe='')}/versions?per_page=100"
    )
    return _gh_api_paginated(repository, endpoint)


def _pypi_project_json(package_name: str) -> Json | None:
    normalized = _pep503_name(package_name)
    url = (
        f"https://pypi.org/pypi/{urllib.parse.quote(normalized, safe='')}/json"
    )
    payload = _registry_json_request(
        url,
        api_name="PyPI JSON API",
        subject=f"package {normalized!r}",
    )
    if payload is None:
        return None
    if not isinstance(payload, dict):
        msg = f"PyPI JSON API returned non-object payload for {normalized!r}"
        raise TypeError(msg)
    return payload


def _nuget_versions_json(package_name: str) -> Json | None:
    normalized = package_name.lower()
    url = (
        "https://api.nuget.org/v3-flatcontainer/"
        f"{urllib.parse.quote(normalized, safe='')}/index.json"
    )
    payload = _registry_json_request(
        url,
        api_name="NuGet flat-container API",
        subject=f"package {normalized!r}",
    )
    if payload is None:
        return None
    if not isinstance(payload, dict):
        msg = (
            f"NuGet flat-container API returned non-object payload for "
            f"{normalized!r}"
        )
        raise TypeError(msg)
    return payload


def _npm_package_json(package_name: str) -> Json | None:
    url = (
        "https://registry.npmjs.org/"
        f"{urllib.parse.quote(package_name, safe='')}"
    )
    payload = _registry_json_request(
        url,
        api_name="npm registry API",
        subject=f"package {package_name!r}",
    )
    if payload is None:
        return None
    if not isinstance(payload, dict):
        msg = (
            f"npm registry API returned non-object payload for {package_name!r}"
        )
        raise TypeError(msg)
    return payload


def _rubygems_versions_json(package_name: str) -> list[Any] | None:
    url = (
        "https://rubygems.org/api/v1/versions/"
        f"{urllib.parse.quote(package_name, safe='')}.json"
    )
    payload = _registry_json_request(
        url,
        api_name="RubyGems versions API",
        subject=f"gem {package_name!r}",
    )
    if payload is None:
        return None
    if not isinstance(payload, list):
        msg = f"RubyGems versions API returned non-list payload for {package_name!r}"
        raise TypeError(msg)
    return payload


def _registry_json_request(
    url: str,
    *,
    api_name: str,
    subject: str,
) -> object | None:
    request = urllib.request.Request(  # noqa: S310
        url,
        headers={
            "Accept": "application/json",
            "User-Agent": "three-workflow-release-control/1.0",
        },
    )
    try:
        with urllib.request.urlopen(  # noqa: S310
            request, timeout=_REGISTRY_JSON_TIMEOUT_SECONDS
        ) as response:
            status = response.getcode()
            if status != 200:
                msg = f"{api_name} request failed for {subject}: HTTP {status}"
                raise RuntimeError(msg)
            raw = response.read()
    except urllib.error.HTTPError as exc:
        if exc.code == 404:
            return None
        msg = f"{api_name} request failed for {subject}: HTTP {exc.code}"
        raise RuntimeError(msg) from exc
    except urllib.error.URLError as exc:
        msg = f"{api_name} request failed for {subject}: {exc.reason}"
        raise RuntimeError(msg) from exc
    except http.client.HTTPException as exc:
        msg = f"{api_name} request failed for {subject}: {exc}"
        raise RuntimeError(msg) from exc
    except OSError as exc:
        msg = f"{api_name} request failed for {subject}: {exc}"
        raise RuntimeError(msg) from exc
    try:
        payload = json.loads(raw.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        msg = f"{api_name} returned invalid JSON for {subject}: {exc}"
        raise RuntimeError(msg) from exc
    return payload


def _supports_remote_observation(snapshot: Mapping[str, Any]) -> bool:
    return snapshot.get("family") == "github-release" or (
        _supports_public_registry_remote_observation(snapshot)
        or _supports_github_packages_remote_observation(snapshot)
    )


def _supports_github_packages_remote_observation(
    snapshot: Mapping[str, Any],
) -> bool:
    destination = snapshot.get("destination")
    github_hosts = {
        "nuget": "nuget.pkg.github.com",
        "npm": "npm.pkg.github.com",
        "rubygems": "rubygems.pkg.github.com",
    }
    family = snapshot.get("family")
    return (
        isinstance(family, str)
        and isinstance(destination, Mapping)
        and destination.get("host") == github_hosts.get(family)
    )


def _supports_public_registry_remote_observation(
    snapshot: Mapping[str, Any],
) -> bool:
    destination = snapshot.get("destination")
    if not isinstance(destination, Mapping):
        return False
    family = snapshot.get("family")
    host = destination.get("host")
    return (
        (family == "pypi" and host == "pypi.org")
        or (family == "nuget" and host == "nuget.org")
        or (family == "npm" and host == "registry.npmjs.org")
        or (family == "rubygems" and host == "rubygems.org")
    )


def _supports_pypi_remote_observation(snapshot: Mapping[str, Any]) -> bool:
    destination = snapshot.get("destination")
    return (
        snapshot.get("family") == "pypi"
        and isinstance(destination, Mapping)
        and destination.get("host") == "pypi.org"
    )


def _requires_live_external_remote_observation(
    node_id: str,
    node: Mapping[str, Any],
    snapshot_id: str,
    snapshot: Mapping[str, Any],
    execution_sets: Mapping[str, Any] | None,
    enabled: set[str],
) -> bool:
    if execution_sets is None:
        return True
    candidates_key = (
        "publish-intent-node-ids"
        if execution_sets.get("dry-run")
        else "active-publish-node-ids"
    )
    candidates = execution_sets.get(candidates_key, [])
    required = node_id in candidates
    capabilities = snapshot.get("capabilities")
    if (
        required
        and isinstance(capabilities, Mapping)
        and capabilities.get("credential-posture") == "oidc"
    ):
        identity = node.get("resolved-publish-identity")
        package_name = (
            identity.get("package-name")
            if isinstance(identity, Mapping)
            else None
        )
        if isinstance(package_name, str) and package_name:
            token = f"{snapshot_id}#{node.get('project-id')}#{package_name}"
            required = token in enabled
    return required


def _requires_live_github_token_remote_observation(
    node_id: str,
    execution_sets: Mapping[str, Any] | None,
) -> bool:
    if execution_sets is None:
        return True
    return node_id in _remote_observation_publish_candidate_ids(execution_sets)


def _remote_observation_publish_candidate_ids(
    execution_sets: Mapping[str, Any],
) -> Sequence[Any]:
    candidates_key = (
        "publish-intent-node-ids"
        if execution_sets.get("dry-run")
        else "active-publish-node-ids"
    )
    candidates = execution_sets.get(candidates_key, [])
    if isinstance(candidates, str):
        return []
    return candidates if isinstance(candidates, Sequence) else []


def _pep503_name(name: str) -> str:
    return re.sub(r"[-_.]+", "-", name).lower()


def _nuget_version_key(version: str) -> str:
    if version != version.strip():
        msg = f"NuGet version has surrounding whitespace: {version!r}"
        raise ValueError(msg)
    public_version, metadata_separator, metadata = version.partition("+")
    if metadata_separator:
        _validate_nuget_identifiers(
            metadata,
            _NUGET_BUILD_METADATA_PART_RE,
            "build metadata",
            version,
        )
    release, prerelease_separator, prerelease = public_version.partition("-")
    release_parts = release.split(".")
    if not 1 <= len(release_parts) <= _NUGET_MAX_NUMERIC_PARTS or not all(
        _NUGET_VERSION_PART_RE.fullmatch(part) for part in release_parts
    ):
        msg = (
            f"NuGet version has invalid numeric release components: {version!r}"
        )
        raise ValueError(msg)
    normalized_numbers = [str(int(part)) for part in release_parts]
    while len(normalized_numbers) < _NUGET_IDENTITY_NUMERIC_PARTS:
        normalized_numbers.append("0")
    if (
        len(normalized_numbers) == _NUGET_MAX_NUMERIC_PARTS
        and normalized_numbers[3] == "0"
    ):
        normalized_numbers.pop()
    normalized = ".".join(normalized_numbers)
    if not prerelease_separator:
        return normalized
    prerelease_parts = _validate_nuget_identifiers(
        prerelease,
        _NUGET_PRERELEASE_PART_RE,
        "prerelease",
        version,
    )
    return f"{normalized}-{'.'.join(part.lower() for part in prerelease_parts)}"


def _validate_nuget_identifiers(
    value: str,
    pattern: re.Pattern[str],
    label: str,
    version: str,
) -> list[str]:
    parts = value.split(".")
    if not value or not all(pattern.fullmatch(part) for part in parts):
        msg = f"NuGet version has invalid {label} identifiers: {version!r}"
        raise ValueError(msg)
    return parts


def _classify_github_release_payload(release: Json, node: Json) -> str:
    desired = node.get("desired-publish-state", {})
    desired_prerelease = (
        isinstance(desired, Mapping)
        and desired.get("release-state") == "prerelease"
    )
    actual_prerelease = release.get("prerelease")
    if actual_prerelease is None:
        actual_prerelease = release.get("isPrerelease")
    if not isinstance(actual_prerelease, bool):
        return "conflicting"
    planned_assets = _planned_github_release_assets(node)
    actual_assets = _observed_github_release_assets(release)
    if planned_assets is None or actual_assets is None:
        return "conflicting"
    if (
        actual_prerelease == desired_prerelease
        and actual_assets == planned_assets
    ):
        return "exact-satisfied"
    return "partial"


def _planned_github_release_assets(node: Json) -> set[str] | None:
    projection = node.get("projection")
    if not isinstance(projection, Mapping):
        return None
    planned_assets_by_id = projection.get("asset-names-by-artifact-id")
    if not isinstance(planned_assets_by_id, Mapping):
        return None
    return {str(value) for value in planned_assets_by_id.values()}


def _observed_github_release_assets(release: Json) -> set[str] | None:
    assets = release.get("assets")
    if not isinstance(assets, list):
        return None
    actual_assets: set[str] = set()
    for asset in assets:
        if not isinstance(asset, Mapping) or not isinstance(
            asset.get("name"), str
        ):
            return None
        actual_assets.add(str(asset["name"]))
    return actual_assets


def _github_release_by_tag(repository: str, tag: str) -> Json | None:
    try:
        payload = _gh_api(
            repository,
            f"repos/{repository}/releases/tags/{urllib.parse.quote(tag, safe='/')}",
        )
    except RuntimeError as exc:
        if _is_github_not_found_error(exc):
            return None
        raise
    if not isinstance(payload, dict):
        msg = f"release lookup for {tag!r} returned non-object payload"
        raise TypeError(msg)
    return payload


def _is_github_not_found_error(exc: RuntimeError) -> bool:
    message = str(exc)
    return "HTTP 404" in message or "404 Not Found" in message


def _gh_api(
    repository: str,
    endpoint: str,
    *,
    method: str = "GET",
    fields: Mapping[str, str] | None = None,
) -> Any:
    env = os.environ.copy()
    if "GH_TOKEN" not in env and "GITHUB_TOKEN" in env:
        env["GH_TOKEN"] = env["GITHUB_TOKEN"]
    command = ["gh", "api", endpoint, "--method", method]
    for key, value in (fields or {}).items():
        command.extend(["-f", f"{key}={value}"])
    result = subprocess.run(
        command,
        cwd=Path.cwd(),
        env=env,
        capture_output=True,
        text=True,
        check=False,
    )
    if result.returncode != 0:
        msg = f"gh api failed for {endpoint}: {result.stderr.strip()}"
        raise RuntimeError(msg)
    if not result.stdout.strip():
        return {}
    try:
        return json.loads(result.stdout)
    except json.JSONDecodeError as exc:
        msg = f"gh api returned invalid JSON for {endpoint}: {exc}"
        raise RuntimeError(msg) from exc


def _gh_api_paginated(repository: str, endpoint: str) -> list[Any]:
    env = os.environ.copy()
    if "GH_TOKEN" not in env and "GITHUB_TOKEN" in env:
        env["GH_TOKEN"] = env["GITHUB_TOKEN"]
    result = subprocess.run(
        ["gh", "api", endpoint, "--paginate", "--slurp"],
        cwd=Path.cwd(),
        env=env,
        capture_output=True,
        text=True,
        check=False,
    )
    if result.returncode != 0:
        msg = f"gh api failed for {endpoint}: {result.stderr.strip()}"
        raise RuntimeError(msg)
    if not result.stdout.strip():
        return []
    try:
        payload = json.loads(result.stdout)
    except json.JSONDecodeError as exc:
        msg = f"gh api returned invalid JSON for {endpoint}: {exc}"
        raise RuntimeError(msg) from exc
    if not isinstance(payload, list):
        msg = f"gh api returned non-list payload for {endpoint}"
        raise TypeError(msg)
    if all(isinstance(page, list) for page in payload):
        return [item for page in payload for item in page]
    return payload


def _github_actions_run_artifacts(
    *,
    repository: str,
    run_id: str,
) -> list[Mapping[str, object]]:
    pages = _gh_api_paginated(
        repository,
        f"repos/{repository}/actions/runs/{run_id}/artifacts?per_page=100",
    )
    artifacts: list[Mapping[str, object]] = []
    seen_artifact_ids: set[str] = set()
    for index, page in enumerate(pages):
        for artifact in _github_actions_run_artifact_page_items(page, index):
            artifact_id = artifact.get("id")
            if artifact_id is not None:
                artifact_id_key = str(artifact_id)
                if artifact_id_key in seen_artifact_ids:
                    continue
                seen_artifact_ids.add(artifact_id_key)
            artifacts.append(artifact)
    return artifacts


def _github_actions_run_artifact_page_items(
    page: object,
    index: int,
) -> Sequence[Mapping[str, object]]:
    if isinstance(page, Mapping):
        page_artifacts = page.get("artifacts")
        if not isinstance(page_artifacts, Sequence) or isinstance(
            page_artifacts,
            str | bytes,
        ):
            msg = f"artifact API page {index} is missing artifacts array"
            raise TypeError(msg)
        artifacts: list[Mapping[str, object]] = []
        for artifact in page_artifacts:
            if not isinstance(artifact, Mapping):
                msg = f"artifact API page {index} contains non-object"
                raise TypeError(msg)
            artifacts.append(artifact)
        return artifacts
    if isinstance(page, Sequence) and not isinstance(page, str | bytes):
        artifacts: list[Mapping[str, object]] = []
        for artifact in page:
            if not isinstance(artifact, Mapping):
                msg = f"artifact API page {index} contains non-object"
                raise TypeError(msg)
            artifacts.append(artifact)
        return artifacts
    msg = f"artifact API page {index} has unsupported shape"
    raise TypeError(msg)


def _publish_node_diagnostic(
    code: str,
    message: str,
    node_id: str,
    node: Json,
    snapshot_id: str,
    *,
    details: Json,
) -> Json:
    return _diagnostic(
        code,
        "validation",
        "publish-node",
        message,
        details,
        project_id=node["project-id"],
        publish_node_id=node_id,
        target_instance_snapshot_id=snapshot_id,
        resolved_publish_identity=node["resolved-publish-identity"],
    )


def _diagnostic(
    code: str,
    phase: str,
    scope_kind: str,
    message: str,
    details: Json,
    **extra: Any,
) -> Json:
    result: Json = {
        "api-version": "three.release.planner-diagnostic/v1alpha1",
        "kind": "planner-diagnostic",
        "code": code,
        "message": message,
        "phase": phase,
        "scope-kind": scope_kind,
        "blocking": True,
        "details": details,
    }
    result.update(
        {
            key.replace("_", "-"): value
            for key, value in extra.items()
            if value is not None
        }
    )
    return result


def _diagnostics_document(diagnostics: Sequence[Json]) -> Json:
    document = {
        "api-version": "three.release.planner-diagnostics/v1alpha1",
        "kind": "planner-diagnostics",
        "diagnostics": list(diagnostics),
    }
    validate_contract(document)
    return document


def _normalize_project_ids(value: str) -> list[str]:
    return sorted(
        {
            item.strip()
            for chunk in value.split("\n")
            for item in chunk.split(",")
            if item.strip()
        }
    )


def _split_repository(repository: str) -> tuple[str, str]:
    parts = repository.split("/", 1)
    if len(parts) != 2 or not parts[0] or not parts[1]:
        msg = "repository must use owner/name form"
        raise ValueError(msg)
    return parts[0], parts[1]


def _utc_now() -> str:
    return (
        datetime.now(UTC)
        .replace(microsecond=0)
        .isoformat()
        .replace(
            "+00:00",
            "Z",
        )
    )


def _ci_affected_range(args: argparse.Namespace) -> Json:
    status = args.range_status or "unavailable"
    if status == "available":
        changed_files = json.loads(args.changed_files_json or "[]")
        if not isinstance(changed_files, list) or not all(
            isinstance(item, str) for item in changed_files
        ):
            msg = "--changed-files-json must be a JSON string array"
            raise TypeError(msg)
        return {
            "status": "available",
            "base-sha": args.base_sha or None,
            "base-tip-sha": args.base_tip_sha or None,
            "head-sha": args.head_sha or None,
            "changed-files": sorted(changed_files),
            "source": args.mode,
            "diagnostic": None,
            "diagnostic-detail": None,
        }
    return {
        "status": "unavailable",
        "base-sha": args.base_sha or None,
        "base-tip-sha": args.base_tip_sha or None,
        "head-sha": args.head_sha or None,
        "changed-files": None,
        "source": args.mode,
        "diagnostic": "range-unconfirmed",
        "diagnostic-detail": args.range_diagnostic_detail,
    }


def _ci_executable_work_groups(
    plan: Mapping[str, object],
) -> list[Mapping[str, Any]]:
    groups = plan.get("work-groups")
    if not isinstance(groups, Sequence) or isinstance(groups, str | bytes):
        return []
    result: list[Mapping[str, Any]] = []
    for group in groups:
        if not isinstance(group, Mapping):
            continue
        if group.get("kind") == "evidence-aggregation":
            continue
        work_group_id = group.get("work-group-id")
        if isinstance(work_group_id, str):
            result.append(_mapping(group, "work-group"))
    return sorted(result, key=lambda item: str(item["work-group-id"]))


def _executable_ci_work_group_ids(plan: Mapping[str, object]) -> list[str]:
    return [
        str(group["work-group-id"])
        for group in _ci_executable_work_groups(plan)
    ]


def _ci_work_group_matrix_entry(
    plan: Mapping[str, object],
    group: Mapping[str, Any],
    *,
    dependency_layer: int,
    writer_job: str,
) -> Json:
    runner_family = str(group["runner-family"])
    return {
        "work-group-id": str(group["work-group-id"]),
        "kind": str(group.get("kind")),
        "runner-family": runner_family,
        "runner": runner_family,
        "ecosystem": group.get("ecosystem"),
        "coverage-target": group.get("coverage-target"),
        "selector-variant": group.get("selector-variant"),
        "depends-on": [
            str(item)
            for item in cast("Sequence[object]", group.get("depends-on", []))
        ],
        "dependency-layer": dependency_layer,
        "writer-job": writer_job,
        "validation-commands": _ci_validation_commands(plan, group),
        "no-publish": True,
    }


def _ci_validation_commands(
    plan: Mapping[str, object],
    group: Mapping[str, Any],
) -> list[Json]:
    kind = str(group.get("kind"))
    if kind == "ecosystem-gate":
        return _ci_ecosystem_validation_commands(plan, group)
    if kind == "descriptor-validation":
        return [
            _ci_command(
                "validate scoped workflow-release descriptors",
                [
                    "uv",
                    "run",
                    "python",
                    "eng/scripts/workflow_release_control.py",
                    "validate-ci-validation-descriptors",
                    "--plan",
                    ".three-ci-validation/plan/validation-plan.json",
                    "--work-group-id",
                    str(group.get("work-group-id")),
                    "--repo-root",
                    ".",
                ],
            )
        ]
    if kind == "lightweight-preflight":
        matrix_work_group = _ci_work_group_matrix_command_context(group)
        return [
            _ci_command(
                "validate lightweight preflight policy",
                [
                    "uv",
                    "run",
                    "python",
                    "eng/scripts/workflow_release_control.py",
                    "validate-ci-validation-lightweight-policy",
                    "--plan",
                    ".three-ci-validation/plan/validation-plan.json",
                    "--assignments",
                    ".three-ci-validation/materialize/selector-assignments.json",
                    "--work-group-id",
                    str(group.get("work-group-id")),
                    "--matrix-work-group-json",
                    json.dumps(
                        matrix_work_group,
                        separators=(",", ":"),
                        sort_keys=True,
                    ),
                ],
            )
        ]
    if kind == "release-shaped-artifact":
        return [
            _ci_builtin_command(
                "validate release-shaped artifact obligations",
                "release-shaped-artifact",
            )
        ]
    if kind == "workflow-release-tooling":
        return _ci_workflow_release_tooling_validation_commands(group)
    return []


def _ci_work_group_matrix_command_context(group: Mapping[str, Any]) -> Json:
    return {
        "work-group-id": str(group.get("work-group-id")),
        "kind": str(group.get("kind")),
        "runner-family": group.get("runner-family"),
        "coverage-target": group.get("coverage-target"),
        "no-publish": True,
    }


def _ci_ecosystem_validation_commands(
    plan: Mapping[str, object],
    group: Mapping[str, Any],
) -> list[Json]:
    ecosystem = str(group.get("ecosystem"))
    root = _ci_work_group_subject_root(plan, group)
    expectation = cast(
        "Mapping[str, object]",
        group.get("expected-evidence", {}),
    )
    capabilities = [
        str(item)
        for item in cast(
            "Sequence[object]",
            expectation.get("planned-capabilities", []),
        )
    ]
    if ecosystem == "dotnet":
        return _ci_dotnet_validation_commands(root, capabilities)
    if ecosystem == "python":
        return _ci_python_validation_commands(root, capabilities)
    if ecosystem in {"javascript", "typescript"}:
        return _ci_javascript_validation_commands(root, capabilities)
    if ecosystem == "ruby":
        return _ci_ruby_validation_commands(root, capabilities)
    return []


def _ci_dotnet_validation_commands(
    root: str,
    capabilities: Sequence[str],
) -> list[Json]:
    target = root if root != "." else "dirs.proj"
    commands: list[Json] = []
    if "build" in capabilities:
        commands.append(
            _ci_command(
                "dotnet build",
                ["dotnet", "build", target],
                capability="build",
            )
        )
    if "test" in capabilities:
        dotnet_test_argv = [
            "dotnet",
            "test",
            target,
        ]
        if "build" in capabilities:
            dotnet_test_argv.extend(["--no-restore", "--no-build"])
        commands.append(
            _ci_command(
                "dotnet test",
                dotnet_test_argv,
                capability="test",
            )
        )
    if "type-check" in capabilities:
        commands.append(
            _ci_command(
                "dotnet type check",
                ["dotnet", "build", target],
                capability="type-check",
            )
        )
    if "format" in capabilities:
        commands.append(
            _ci_command(
                "dotnet format check",
                ["dotnet", "format", target, "--verify-no-changes"],
                capability="format",
            )
        )
    return commands


def _ci_python_validation_commands(
    root: str,
    capabilities: Sequence[str],
) -> list[Json]:
    commands: list[Json] = []
    if "build" in capabilities:
        commands.append(
            _ci_command(
                "python build",
                [
                    "uv",
                    "build",
                    "--out-dir",
                    ".three-ci-validation/work/validation-build",
                    root,
                ],
                capability="build",
            )
        )
    if "test" in capabilities:
        commands.append(
            _ci_command(
                "python tests",
                ["uv", "run", "pytest", root],
                capability="test",
            )
        )
    if "lint" in capabilities:
        commands.append(
            _ci_command(
                "python lint",
                ["uv", "run", "ruff", "check", "--force-exclude", root],
                capability="lint",
            )
        )
    if "format" in capabilities:
        commands.append(
            _ci_command(
                "python format check",
                [
                    "uv",
                    "run",
                    "ruff",
                    "format",
                    "--quiet",
                    "--force-exclude",
                    "--check",
                    root,
                ],
                capability="format",
            )
        )
    if "type-check" in capabilities:
        commands.append(
            _ci_command(
                "python type check",
                ["uv", "run", "pyrefly", "check"],
                capability="type-check",
            )
        )
    return commands


def _ci_javascript_validation_commands(
    root: str,
    capabilities: Sequence[str],
) -> list[Json]:
    script_by_capability = {
        "build": "build",
        "test": "test",
        "lint": "lint",
        "type-check": "typecheck",
    }
    commands = [
        _ci_command(
            "javascript-typescript install",
            ["pnpm", "install", "--frozen-lockfile"],
        )
    ]
    commands.extend(
        _ci_command(
            f"javascript-typescript {script}",
            ["pnpm", "--dir", root, "run", script],
            capability=capability,
        )
        for capability, script in script_by_capability.items()
        if capability in capabilities
    )
    if "format" in capabilities:
        commands.append(
            _ci_command(
                "javascript-typescript format check",
                [
                    "pnpm",
                    "--dir",
                    root,
                    "exec",
                    "biome",
                    "format",
                    "--check",
                    ".",
                ],
                capability="format",
            )
        )
    return commands


def _ci_ruby_validation_commands(
    root: str,
    capabilities: Sequence[str],
) -> list[Json]:
    commands: list[Json] = []
    if "build" in capabilities:
        commands.append(
            _ci_command(
                "ruby versioning tool restore",
                ["dotnet", "tool", "restore"],
                capability="build",
            )
        )
        commands.append(
            _ci_command(
                "ruby gem build",
                [
                    "ruby",
                    "-e",
                    (
                        "root=ARGV.fetch(0); "
                        "out=ARGV.fetch(1); "
                        "gemspec=Dir[File.join(root, '*.gemspec')].sort.first; "
                        'abort("no gemspec found under #{root}") unless gemspec; '
                        "system('gem', 'build', gemspec, '--output', out) "
                        "or exit($?.exitstatus || 1)"
                    ),
                    root,
                    ".three-ci-validation/work/validation-build.gem",
                ],
                capability="build",
            )
        )
    return commands


def _ci_workflow_release_tooling_validation_commands(
    group: Mapping[str, Any],
) -> list[Json]:
    target = group.get("coverage-target")
    surface = ""
    if isinstance(target, Mapping) and target.get("type") == "tooling-surface":
        surface = str(target.get("id") or "")
    path_by_surface = {
        "planner": "src/public/lib/three-workflow-release-planner",
        "classifier": "src/public/lib/three-workflow-release-planner",
        "fact-provider": "src/public/lib/three-workflow-release-planner",
        "descriptor-contract": "src/public/lib/three-workflow-release-contracts",
        "workflow-release-contract": "src/public/lib/three-workflow-release-contracts",
        "authoring-validation": "src/public/lib/three-workflow-release-authoring",
        "target-catalog": "src/public/lib/three-workflow-release-planner",
        "build-execution": "src/public/lib/three-workflow-release-build",
        "publish-execution": "src/public/lib/three-workflow-release-publish",
        "smoke-validation": "tests/test_workflow_release_control.py",
        "descriptor-schema-documentation": (
            "src/public/lib/three-workflow-release-authoring"
        ),
    }
    if surface == "workflow-orchestration":
        return [
            _ci_command(
                "workflow-release orchestration tests",
                [
                    "uv",
                    "run",
                    "python",
                    "-m",
                    "pytest",
                    "tests/test_workflow_release_control.py",
                    "-q",
                ],
            )
        ]
    path = path_by_surface.get(surface)
    if path is None:
        return [
            _ci_builtin_command(
                "validate workflow-release tooling surface",
                "workflow-release-tooling",
            )
        ]
    return [
        _ci_command(
            f"workflow-release {surface} tooling lint",
            ["uv", "run", "ruff", "check", "--force-exclude", path],
        )
    ]


def _ci_work_group_subject_root(
    plan: Mapping[str, object],
    group: Mapping[str, Any],
) -> str:
    target = group.get("coverage-target")
    subject_id = None
    if isinstance(target, Mapping) and target.get("type") == "subject":
        subject_id = target.get("id")
    subjects = plan.get("subjects")
    if isinstance(subjects, Sequence) and not isinstance(subjects, str | bytes):
        for subject in subjects:
            if (
                isinstance(subject, Mapping)
                and subject.get("subject-id") == subject_id
                and isinstance(subject.get("root"), str)
            ):
                return str(subject["root"])
    return "."


def _ci_command(
    label: str,
    argv: Sequence[str],
    *,
    capability: str | None = None,
) -> Json:
    return {
        "label": label,
        "argv": [str(item) for item in argv],
        "capability": capability,
    }


def _ci_builtin_command(label: str, builtin: str) -> Json:
    return {
        "label": label,
        "argv": [],
        "builtin": builtin,
        "capability": None,
    }


def _ci_work_group_dependency_layers(
    plan: Mapping[str, object],
) -> dict[str, int]:
    groups = {
        str(group["work-group-id"]): group
        for group in _ci_executable_work_groups(plan)
    }
    layers: dict[str, int] = {}

    def layer(work_group_id: str, visiting: set[str]) -> int:
        if work_group_id in layers:
            return layers[work_group_id]
        if work_group_id in visiting:
            msg = f"work group dependency cycle includes {work_group_id}"
            raise RuntimeError(msg)
        visiting.add(work_group_id)
        group = groups[work_group_id]
        dependencies = [
            str(item)
            for item in cast("Sequence[object]", group.get("depends-on", []))
            if str(item) in groups
        ]
        value = (
            max(layer(dependency, visiting) for dependency in dependencies) + 1
            if dependencies
            else 0
        )
        visiting.remove(work_group_id)
        layers[work_group_id] = value
        return value

    for work_group_id in sorted(groups):
        layer(work_group_id, set())
    return layers


def _ci_work_group_matrix_layers(
    matrix: Sequence[Mapping[str, object]],
) -> list[list[Json]]:
    layer_count = (
        max(int(item["dependency-layer"]) for item in matrix) + 1
        if matrix
        else 0
    )
    return [
        [
            dict(item)
            for item in matrix
            if int(item["dependency-layer"]) == layer_index
        ]
        for layer_index in range(layer_count)
    ]


def _ci_dependency_blocked(
    *,
    plan: Mapping[str, object],
    assignments: Mapping[str, object],
    work_group_id: str,
    observed_artifacts_dir: str,
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
) -> bool:
    dependencies = [
        str(item)
        for item in cast(
            "Sequence[object]",
            _ci_work_group(plan, work_group_id).get("depends-on", []),
        )
    ]
    if not dependencies:
        return False
    observed_by_work_group = {
        str(item.manifest_entry.get("writer-work-group-id")): item
        for item in _ci_observed_receipt_inputs(
            plan=plan,
            assignments=assignments,
            observed_artifacts_dir=observed_artifacts_dir,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
        )
        if item.manifest_entry.get("writer-work-group-id") in dependencies
    }
    for dependency in dependencies:
        assignment = _ci_assignment_for_work_group(assignments, dependency)
        observed = observed_by_work_group.get(dependency)
        if observed is None or observed.receipt is None:
            return True
        if observed.manifest_entry.get("observed-writer-id") != assignment.get(
            "trusted-writer-id"
        ):
            return True
        try:
            validate_ci_validation_receipt(
                observed.receipt,
                plan=plan,
                selector_assignments_manifest=assignments,
                assignment=assignment,
                changed_files_snapshot=changed_files_snapshot,
                fact_snapshot=fact_snapshot,
            )
        except ContractValidationError:
            return True
        if observed.receipt.get("outcome") == "skipped":
            return True
    return False


def _ci_assignment_for_work_group(
    assignments: Mapping[str, object],
    work_group_id: str,
) -> Mapping[str, object]:
    items = assignments.get("assignments")
    if not isinstance(items, Sequence) or isinstance(items, str | bytes):
        msg = "selector assignments must contain an assignments array"
        raise TypeError(msg)
    for item in items:
        if (
            isinstance(item, Mapping)
            and item.get("work-group-id") == work_group_id
        ):
            return item
    msg = f"work group {work_group_id!r} is not assigned"
    raise KeyError(msg)


def _read_json_value(value: str) -> object:
    return json.loads(value)


def _ci_validation_diagnostics(
    plan: Mapping[str, object],
    work_group_id: str,
    *,
    outcome: str,
) -> list[Json]:
    if outcome == "success":
        return []
    return [_ci_validation_diagnostic(plan, work_group_id, outcome)]


def _ci_validation_diagnostic(
    plan: Mapping[str, object],
    work_group_id: str,
    outcome: str,
) -> Json:
    if outcome == "blocking-failure":
        group = _ci_work_group(plan, work_group_id)
        kind = str(group.get("kind") or "validation-work")
        return ci_validation_diagnostic(
            diagnostic_id=f"validation-work-failed/{work_group_id}/execution",
            code=DiagnosticFamily.VALIDATION_WORK_FAILED.value,
            detail=DiagnosticDetail.TOOLING.value,
            message=(
                f"No-publish CI validation execution for {kind} did not "
                "complete successfully."
            ),
            source_type="work-group",
            source_id=work_group_id,
            severity=DiagnosticSeverity.BLOCKING_FAILURE.value,
            verdict_effect=DiagnosticVerdictEffect.FAILED.value,
        )
    return ci_validation_diagnostic(
        diagnostic_id=f"validation-work-skipped/{work_group_id}/dependency",
        code=DiagnosticFamily.VALIDATION_WORK_SKIPPED.value,
        detail=DiagnosticDetail.DEPENDENCY_BLOCKED.value,
        message=(
            "No-publish CI validation execution was skipped because a planned "
            "prerequisite work group did not produce a successful receipt."
        ),
        source_type="work-group",
        source_id=work_group_id,
        severity=DiagnosticSeverity.WARNING.value,
        verdict_effect=DiagnosticVerdictEffect.NONE.value,
    )


def _ci_capability_failure_diagnostic(
    work_group_id: str,
    capability: str,
) -> Json:
    detail_by_capability = {
        "build": DiagnosticDetail.BUILD.value,
        "test": DiagnosticDetail.TEST.value,
        "lint": DiagnosticDetail.LINT.value,
        "format": DiagnosticDetail.FORMAT.value,
        "type-check": DiagnosticDetail.TYPE_CHECK.value,
    }
    return ci_validation_diagnostic(
        diagnostic_id=(
            f"validation-work-failed/{work_group_id}/capability/{capability}"
        ),
        code=DiagnosticFamily.VALIDATION_WORK_FAILED.value,
        detail=detail_by_capability.get(
            capability, DiagnosticDetail.TOOLING.value
        ),
        message=(
            f"No-publish CI validation command for planned capability "
            f"{capability!r} did not complete successfully."
        ),
        source_type="work-group",
        source_id=work_group_id,
        severity=DiagnosticSeverity.BLOCKING_FAILURE.value,
        verdict_effect=DiagnosticVerdictEffect.FAILED.value,
    )


def _ci_validation_outcome(
    plan: Mapping[str, object],
    work_group_id: str,
    *,
    dependency_blocked: bool,
    validation_result: Mapping[str, object] | None,
    assignments: Mapping[str, object] | None = None,
    observed_artifacts_dir: str = "",
    observed_commit_sha: str = "",
    changed_files_snapshot: Mapping[str, object] | None = None,
    fact_snapshot: Mapping[str, object] | None = None,
) -> ReceiptOutcome:
    if dependency_blocked:
        return "skipped"
    if (
        validation_result is not None
        and _ci_validation_result_has_success_evidence(
            plan,
            work_group_id,
            validation_result,
            assignments=assignments,
            observed_artifacts_dir=observed_artifacts_dir,
            observed_commit_sha=observed_commit_sha,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
        )
    ):
        return "success"
    return "blocking-failure"


def _ci_validation_result_has_success_evidence(
    plan: Mapping[str, object],
    work_group_id: str,
    validation_result: Mapping[str, object],
    *,
    assignments: Mapping[str, object] | None = None,
    observed_artifacts_dir: str = "",
    observed_commit_sha: str = "",
    changed_files_snapshot: Mapping[str, object] | None = None,
    fact_snapshot: Mapping[str, object] | None = None,
) -> bool:
    if validation_result.get("outcome") != "success" or not (
        _ci_validation_result_identity_matches(
            plan,
            work_group_id,
            validation_result,
            observed_commit_sha=observed_commit_sha,
        )
    ):
        return False
    commands = validation_result.get("commands")
    if not isinstance(commands, Sequence) or isinstance(commands, str | bytes):
        return False
    is_release_shaped = (
        _ci_work_group(plan, work_group_id).get("kind")
        == "release-shaped-artifact"
    )
    command_mappings = [
        command for command in commands if isinstance(command, Mapping)
    ]
    if not command_mappings or (
        is_release_shaped and len(command_mappings) != len(commands)
    ):
        return False
    if is_release_shaped:
        release_results = _ci_release_shaped_results_from_validation_result(
            plan,
            work_group_id,
            validation_result,
        )
        return (
            all(
                command.get("outcome") == "success"
                for command in command_mappings
            )
            and _ci_release_shaped_result_has_admissible_source(
                plan=plan,
                work_group_id=work_group_id,
                validation_result=validation_result,
                command_mappings=command_mappings,
                assignments=assignments,
                observed_artifacts_dir=observed_artifacts_dir,
                observed_commit_sha=observed_commit_sha,
                changed_files_snapshot=changed_files_snapshot,
                fact_snapshot=fact_snapshot,
            )
            and release_results is not None
            and not _ci_release_shaped_results_contain_plan_fabrication(
                plan, work_group_id, release_results
            )
        )
    expectation = _ci_evidence_expectation(plan, work_group_id)
    planned_capabilities = expectation.get("planned-capabilities")
    if isinstance(planned_capabilities, Sequence) and not isinstance(
        planned_capabilities, str | bytes
    ):
        commands_by_capability = _ci_command_results_by_capability(
            validation_result,
        )
        return all(
            commands_by_capability.get(str(capability))
            and all(
                command.get("outcome") == "success"
                for command in commands_by_capability[str(capability)]
            )
            for capability in planned_capabilities
        )
    return all(
        command.get("outcome") == "success" for command in command_mappings
    )


def _ci_validation_result_identity_matches(
    plan: Mapping[str, object],
    work_group_id: str,
    validation_result: Mapping[str, object],
    *,
    observed_commit_sha: str = "",
) -> bool:
    planned_work_group = _ci_work_group(plan, work_group_id)
    if not (
        validation_result.get("work-group-id") == work_group_id
        and validation_result.get("kind") == planned_work_group.get("kind")
        and validation_result.get("runner-family")
        == planned_work_group.get("runner-family")
    ):
        return False
    validation_tree = plan.get("validation-tree")
    if not isinstance(validation_tree, Mapping):
        return False
    result_commit = validation_result.get("observed-commit-sha")
    return bool(
        result_commit == validation_tree.get("commit-sha")
        and (not observed_commit_sha or result_commit == observed_commit_sha)
        and validation_result.get("coverage-target")
        == planned_work_group.get("coverage-target")
    )


def _ci_release_shaped_result_has_admissible_source(
    *,
    plan: Mapping[str, object],
    work_group_id: str,
    validation_result: Mapping[str, object],
    command_mappings: Sequence[Mapping[str, object]],
    assignments: Mapping[str, object] | None,
    observed_artifacts_dir: str,
    observed_commit_sha: str,
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
) -> bool:
    if (
        not observed_commit_sha
        or validation_result.get("observed-commit-sha") != observed_commit_sha
    ):
        return False
    source_command = _ci_no_publish_source_command_from_validation_result(
        validation_result,
    )
    if source_command is not None:
        source_detail: Json = {
            "evidence-source": "no-publish-validation",
            "source-proof": dict(
                cast("Mapping[str, object]", source_command["source-proof"]),
            ),
            "artifact-obligation-results": [
                dict(item)
                for item in cast(
                    "Sequence[Mapping[str, object]]",
                    source_command["artifact-obligation-results"],
                )
            ],
        }
        return _ci_no_publish_release_shaped_source_proof_is_admissible(
            {
                "work-group-id": work_group_id,
                "coverage-target": validation_result.get("coverage-target"),
                "execution-tree": {
                    "observed-commit-sha": observed_commit_sha,
                },
            },
            source_detail,
            source_validation_result=validation_result,
        )
    if not isinstance(assignments, Mapping) or not observed_artifacts_dir:
        return False
    reuse = _ci_reused_release_shaped_artifact_evidence(
        plan=plan,
        assignments=assignments,
        work_group_id=work_group_id,
        observed_artifacts_dir=observed_artifacts_dir,
        observed_commit_sha=observed_commit_sha,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
    )
    return reuse is not None and all(
        _ci_release_shaped_command_matches_reused_evidence(command, reuse)
        for command in command_mappings
    )


def _ci_release_shaped_command_matches_reused_evidence(
    command: Mapping[str, object],
    reuse: Mapping[str, object],
) -> bool:
    return (
        command.get("evidence-source") == "reused-validation-receipt"
        and command.get("reused-receipt") == reuse.get("reused-receipt")
        and command.get("artifact-obligation-results")
        == reuse.get("artifact-obligation-results")
    )


def _ci_release_shaped_results_from_validation_result(
    plan: Mapping[str, object],
    work_group_id: str,
    validation_result: Mapping[str, object],
) -> list[Mapping[str, object]] | None:
    commands = validation_result.get("commands")
    if not isinstance(commands, Sequence) or isinstance(commands, str | bytes):
        return None
    results: list[Mapping[str, object]] = []
    for command in commands:
        if not isinstance(command, Mapping):
            continue
        command_results = command.get("artifact-obligation-results")
        if not isinstance(command_results, Sequence) or isinstance(
            command_results, str | bytes
        ):
            continue
        for result in command_results:
            if not isinstance(result, Mapping):
                return None
            results.append(result)
    if not _ci_release_shaped_results_match_plan(plan, work_group_id, results):
        return None
    return results


def _ci_release_shaped_results_match_plan(
    plan: Mapping[str, object],
    work_group_id: str,
    results: Sequence[Mapping[str, object]],
) -> bool:
    obligations = _ci_plan_records_for_work_group(
        plan, "artifact-obligations", work_group_id
    )
    if not obligations:
        return False
    obligations_by_id = {
        str(obligation["artifact-obligation-id"]): obligation
        for obligation in obligations
    }
    result_ids = [
        str(result.get("artifact-obligation-id")) for result in results
    ]
    if len(result_ids) != len(set(result_ids)) or set(result_ids) != set(
        obligations_by_id
    ):
        return False
    return all(
        _ci_release_shaped_result_matches_obligation(
            result,
            obligations_by_id[str(result.get("artifact-obligation-id"))],
        )
        for result in results
    )


def _ci_release_shaped_results_contain_plan_fabrication(
    plan: Mapping[str, object],
    work_group_id: str,
    results: Sequence[Mapping[str, object]],
) -> bool:
    obligations = _ci_plan_records_for_work_group(
        plan, "artifact-obligations", work_group_id
    )
    obligations_by_id = {
        str(obligation["artifact-obligation-id"]): obligation
        for obligation in obligations
    }
    return any(
        _ci_release_result_is_fabricated_from_plan(result, obligations_by_id)
        for result in results
    )


def _ci_release_result_is_fabricated_from_plan(
    result: Mapping[str, object],
    obligations_by_id: Mapping[str, Mapping[str, object]],
) -> bool:
    obligation = obligations_by_id.get(
        str(result.get("artifact-obligation-id"))
    )
    artifact = result.get("artifact")
    if obligation is None or not isinstance(artifact, Mapping):
        return False
    observed = artifact.get("observed")
    if not isinstance(observed, Mapping):
        return False
    digests = observed.get("digests")
    if not isinstance(digests, Sequence) or isinstance(digests, str | bytes):
        return False
    return any(
        isinstance(item, Mapping)
        and isinstance(item.get("artifact-ref"), str)
        and item.get("digest")
        == _ci_fabricated_release_artifact_digest(
            obligation,
            str(item["artifact-ref"]),
        )
        for item in digests
    )


def _ci_fabricated_release_artifact_digest(
    obligation: Mapping[str, object],
    artifact_ref: str,
) -> str:
    return canonical_json_digest(
        {
            "artifact-obligation-id": obligation["artifact-obligation-id"],
            "artifact-ref": artifact_ref,
            "artifact": obligation["artifact"],
            "release-receipt": obligation["release-receipt"],
        }
    )


def _ci_release_shaped_result_matches_obligation(
    result: Mapping[str, object],
    obligation: Mapping[str, object],
) -> bool:
    artifact = result.get("artifact")
    release_receipt = result.get("release-receipt")
    if not isinstance(artifact, Mapping) or not isinstance(
        release_receipt, Mapping
    ):
        return False
    planned_artifact = obligation.get("artifact")
    planned_receipt = obligation.get("release-receipt")
    expected_refs = _ci_artifact_expected_refs(obligation)
    return (
        bool(expected_refs)
        and result.get("outcome") == "success"
        and result.get("diagnostics") == []
        and result.get("profile-coverage") == obligation.get("profile-coverage")
        and artifact.get("planned") == planned_artifact
        and _ci_artifact_observed_refs(artifact) == expected_refs
        and _ci_artifact_observed_digests_match_refs(artifact, expected_refs)
        and artifact.get("outcome") == "success"
        and artifact.get("diagnostics") == []
        and release_receipt.get("planned") == planned_receipt
        and release_receipt.get("expected") is True
        and release_receipt.get("schema-checked") is True
        and release_receipt.get("outcome") == "success"
        and release_receipt.get("diagnostics") == []
    )


def _ci_artifact_expected_refs(
    obligation: Mapping[str, object],
) -> list[str]:
    artifact = obligation.get("artifact")
    if not isinstance(artifact, Mapping):
        return []
    refs = artifact.get("expected-artifact-refs")
    if not isinstance(refs, Sequence) or isinstance(refs, str | bytes):
        return []
    return [str(item) for item in refs if isinstance(item, str)]


def _ci_artifact_observed_refs(artifact: Mapping[str, object]) -> list[str]:
    observed = artifact.get("observed")
    if not isinstance(observed, Mapping):
        return []
    refs = observed.get("refs")
    if not isinstance(refs, Sequence) or isinstance(refs, str | bytes):
        return []
    return [str(item) for item in refs if isinstance(item, str)]


def _ci_artifact_observed_digests_match_refs(
    artifact: Mapping[str, object],
    expected_refs: Sequence[str],
) -> bool:
    observed = artifact.get("observed")
    if not isinstance(observed, Mapping):
        return False
    digests = observed.get("digests")
    if not isinstance(digests, Sequence) or isinstance(digests, str | bytes):
        return False
    seen: set[str] = set()
    for item in digests:
        if not isinstance(item, Mapping):
            return False
        artifact_ref = item.get("artifact-ref")
        digest = item.get("digest")
        if (
            not isinstance(artifact_ref, str)
            or artifact_ref in seen
            or item.get("algorithm") != "sha256"
            or item.get("digest-available") is not True
            or not isinstance(digest, str)
            or re.fullmatch(r"[0-9a-f]{64}", digest) is None
            or item.get("diagnostics") != []
        ):
            return False
        seen.add(artifact_ref)
    return seen == set(expected_refs)


def _ci_release_shaped_observed_refs(
    results: Sequence[Mapping[str, object]],
) -> list[str]:
    refs: list[str] = []
    for result in results:
        artifact = result.get("artifact")
        if isinstance(artifact, Mapping):
            refs.extend(_ci_artifact_observed_refs(artifact))
    return refs


def _ci_validation_evidence(
    plan: Mapping[str, object],
    work_group_id: str,
    *,
    outcome: str,
    diagnostics: Sequence[Mapping[str, object]],
    validation_result: Mapping[str, object] | None = None,
    fact_snapshot: Mapping[str, object] | None = None,
    batch_bundle: bool = False,
) -> Json:
    expectation = _ci_evidence_expectation(plan, work_group_id)
    category = str(expectation["category"])
    if isinstance(expectation.get("planned-capabilities"), Sequence):
        capabilities = [
            str(item)
            for item in cast(
                "Sequence[object]",
                expectation["planned-capabilities"],
            )
        ]
        capability_results = _ci_capability_results(
            work_group_id=work_group_id,
            capabilities=capabilities,
            outcome=outcome,
            diagnostics=diagnostics,
            validation_result=validation_result,
        )
        return {
            "category": category,
            "planned-capabilities": capabilities,
            "capability-results": capability_results,
            "artifact-refs": [],
        }
    category_result: Json = {
        "outcome": outcome,
        "diagnostics": [dict(item) for item in diagnostics],
    }
    if batch_bundle:
        category_result["category"] = category
    else:
        category_result["detail"] = _ci_validation_detail(
            plan,
            work_group_id,
            category,
            diagnostics,
            outcome=outcome,
            fact_snapshot=fact_snapshot,
        )
    artifact_refs: list[str] = []
    if (
        category == "release-shaped-artifact"
        and outcome == "success"
        and validation_result is not None
    ):
        release_results = _ci_release_shaped_results_from_validation_result(
            plan,
            work_group_id,
            validation_result,
        )
        if release_results is not None:
            detail: Json = {
                "artifact-obligation-results": [
                    dict(result) for result in release_results
                ]
            }
            source_proof = (
                _ci_release_shaped_source_proof_from_validation_result(
                    validation_result
                )
            )
            if source_proof is not None:
                detail.update(source_proof)
            artifact_refs = _ci_release_shaped_observed_refs(release_results)
            if batch_bundle:
                category_result["artifact-refs"] = artifact_refs
            else:
                category_result["detail"] = detail
    return {
        "category": category,
        "planned-capabilities": None,
        "category-result": category_result,
        "artifact-refs": artifact_refs,
    }


def _ci_release_shaped_source_proof_from_validation_result(  # noqa: PLR0911
    validation_result: Mapping[str, object],
) -> Json | None:
    commands = validation_result.get("commands")
    if not isinstance(commands, Sequence) or isinstance(commands, str | bytes):
        return None
    if len(commands) != 1:
        return None
    command = commands[0]
    if not (
        isinstance(command, Mapping)
        and command.get("outcome") == "success"
        and isinstance(command.get("artifact-obligation-results"), Sequence)
        and not isinstance(
            command.get("artifact-obligation-results"), str | bytes
        )
    ):
        return None
    evidence_source = command.get("evidence-source")
    if evidence_source == "reused-validation-receipt":
        reused_receipt = command.get("reused-receipt")
        if not isinstance(reused_receipt, Mapping):
            return None
        return {
            "evidence-source": "reused-validation-receipt",
            "reused-receipt": dict(reused_receipt),
        }
    if evidence_source != "no-publish-validation":
        return None
    source_proof = command.get("source-proof")
    if not isinstance(source_proof, Mapping):
        return None
    return {
        "evidence-source": "no-publish-validation",
        "source-proof": dict(source_proof),
    }


def _ci_capability_results(
    *,
    work_group_id: str,
    capabilities: Sequence[str],
    outcome: str,
    diagnostics: Sequence[Mapping[str, object]],
    validation_result: Mapping[str, object] | None,
) -> list[Json]:
    if outcome == "skipped":
        return [
            {
                "capability": capability,
                "outcome": "skipped",
                "diagnostics": [dict(item) for item in diagnostics],
            }
            for capability in capabilities
        ]
    if (
        outcome != "success"
        and validation_result is not None
        and validation_result.get("outcome") == "success"
    ):
        validation_result = None
    if validation_result is None:
        return [
            {
                "capability": capability,
                "outcome": outcome,
                "diagnostics": [dict(item) for item in diagnostics],
            }
            for capability in capabilities
        ]
    commands_by_capability = _ci_command_results_by_capability(
        validation_result,
    )
    results: list[Json] = []
    for capability in capabilities:
        commands = commands_by_capability.get(capability, [])
        capability_outcome = (
            "success"
            if commands
            and all(command.get("outcome") == "success" for command in commands)
            else "blocking-failure"
        )
        capability_diagnostics = (
            []
            if capability_outcome == "success"
            else [_ci_capability_failure_diagnostic(work_group_id, capability)]
        )
        results.append(
            {
                "capability": capability,
                "outcome": capability_outcome,
                "diagnostics": capability_diagnostics,
            }
        )
    return results


def _ci_command_results_by_capability(
    validation_result: Mapping[str, object] | None,
) -> dict[str, list[Mapping[str, object]]]:
    commands = (
        None if validation_result is None else validation_result.get("commands")
    )
    if not isinstance(commands, Sequence) or isinstance(commands, str | bytes):
        return {}
    result: dict[str, list[Mapping[str, object]]] = {}
    for command in commands:
        if not isinstance(command, Mapping):
            continue
        capability = command.get("capability")
        if isinstance(capability, str):
            result.setdefault(capability, []).append(command)
    return result


def _ci_validation_detail(
    plan: Mapping[str, object],
    work_group_id: str,
    category: str,
    diagnostics: Sequence[Mapping[str, object]],
    *,
    outcome: str,
    fact_snapshot: Mapping[str, object] | None,
) -> Json:
    if category in {"lightweight-preflight", "workflow-release-tooling"}:
        return _ci_detail_profile_result(
            plan,
            work_group_id,
            category,
            diagnostics,
            outcome=outcome,
        )
    if category == "descriptor-validation":
        return {
            "descriptor-obligation-results": [
                _ci_descriptor_placeholder_result(
                    obligation,
                    diagnostics,
                    outcome=outcome,
                    fact_snapshot=fact_snapshot,
                )
                for obligation in _ci_plan_records_for_work_group(
                    plan, "descriptor-obligations", work_group_id
                )
            ],
        }
    if category == "release-shaped-artifact":
        return {
            "artifact-obligation-results": [
                _ci_artifact_placeholder_result(
                    obligation,
                    diagnostics,
                    fact_snapshot=fact_snapshot,
                    outcome=outcome,
                )
                for obligation in _ci_plan_records_for_work_group(
                    plan, "artifact-obligations", work_group_id
                )
            ],
        }
    return {}


def _ci_detail_profile_result(
    plan: Mapping[str, object],
    work_group_id: str,
    category: str,
    diagnostics: Sequence[Mapping[str, object]],
    *,
    outcome: str,
) -> Json:
    group = _ci_work_group(plan, work_group_id)
    expectation = _ci_evidence_expectation(plan, work_group_id)
    profile = _ci_detail_profile(plan, str(expectation["detail-profile"]))
    detail: Json = {
        "work-group-id": work_group_id,
        "detail-profile": expectation["detail-profile"],
        "coverage-target": group["coverage-target"],
        "selector-variant": group.get("selector-variant"),
        "runner-family": group["runner-family"],
        "outcome": outcome,
        "subcheck-results": [
            {
                "subcheck-id": item["subcheck-id"],
                "outcome": outcome,
                "diagnostics": [dict(item) for item in diagnostics],
            }
            for item in cast(
                "Sequence[Mapping[str, object]]",
                profile["required-subchecks"],
            )
        ],
        "diagnostics": [dict(item) for item in diagnostics],
    }
    if category == "workflow-release-tooling":
        detail["ecosystem"] = group.get("ecosystem")
    return detail


def _ci_descriptor_placeholder_result(
    obligation: Mapping[str, object],
    diagnostics: Sequence[Mapping[str, object]],
    *,
    outcome: str,
    fact_snapshot: Mapping[str, object] | None,
) -> Json:
    descriptor_path = _ci_descriptor_obligation_path(obligation)
    fact = _ci_descriptor_fact(fact_snapshot, descriptor_path)
    return {
        "descriptor-obligation-id": obligation["descriptor-obligation-id"],
        "descriptor": {
            "path": descriptor_path,
            "identity": fact.get("descriptor-identity") if fact else None,
            "owner-subject-id": fact.get("owner-subject-id") if fact else None,
            "source": fact.get("source") if fact else "ecosystem-provider",
        },
        "descriptor-scope": obligation["descriptor-scope"],
        "outcome": outcome,
        "diagnostics": [dict(item) for item in diagnostics],
    }


def _ci_artifact_placeholder_result(
    obligation: Mapping[str, object],
    diagnostics: Sequence[Mapping[str, object]],
    *,
    fact_snapshot: Mapping[str, object] | None,
    outcome: str,
) -> Json:
    artifact = dict(cast("Mapping[str, object]", obligation["artifact"]))
    receipt = dict(cast("Mapping[str, object]", obligation["release-receipt"]))
    descriptor_path = str(obligation["descriptor-path"])
    descriptor_fact = _ci_descriptor_fact(fact_snapshot, descriptor_path)
    return {
        "artifact-obligation-id": obligation["artifact-obligation-id"],
        "descriptor": {
            "path": descriptor_path,
            "identity": descriptor_fact.get("descriptor-identity")
            if descriptor_fact is not None
            else None,
        },
        "profile-coverage": obligation["profile-coverage"],
        "artifact": {
            "planned": artifact,
            "observed": {"refs": [], "digests": []},
            "outcome": outcome,
            "diagnostics": [dict(item) for item in diagnostics],
        },
        "release-receipt": {
            "planned": receipt,
            "expected": True,
            "schema-checked": False,
            "outcome": outcome,
            "diagnostics": [dict(item) for item in diagnostics],
        },
        "outcome": outcome,
        "diagnostics": [dict(item) for item in diagnostics],
    }


def _ci_artifact_obligation_success_result(
    obligation: Mapping[str, object],
) -> Json:
    artifact = dict(cast("Mapping[str, object]", obligation["artifact"]))
    receipt = dict(cast("Mapping[str, object]", obligation["release-receipt"]))
    expected_refs = _ci_artifact_expected_refs(obligation)
    return {
        "artifact-obligation-id": obligation["artifact-obligation-id"],
        "descriptor": {
            "path": str(obligation["descriptor-path"]),
            "identity": None,
        },
        "profile-coverage": obligation["profile-coverage"],
        "artifact": {
            "planned": artifact,
            "observed": {
                "refs": expected_refs,
                "digests": [
                    {
                        "artifact-ref": artifact_ref,
                        "algorithm": "sha256",
                        "digest": _ci_fabricated_release_artifact_digest(
                            obligation, artifact_ref
                        ),
                        "digest-available": True,
                        "diagnostics": [],
                    }
                    for artifact_ref in expected_refs
                ],
            },
            "outcome": "success",
            "diagnostics": [],
        },
        "release-receipt": {
            "planned": receipt,
            "expected": True,
            "schema-checked": True,
            "outcome": "success",
            "diagnostics": [],
        },
        "outcome": "success",
        "diagnostics": [],
    }


def _ci_evidence_expectation(
    plan: Mapping[str, object],
    work_group_id: str,
) -> Mapping[str, object]:
    for item in _ci_plan_records_for_work_group(
        plan, "evidence-expectations", work_group_id
    ):
        return item
    msg = f"work group {work_group_id!r} has no evidence expectation"
    raise KeyError(msg)


def _ci_work_group(
    plan: Mapping[str, object],
    work_group_id: str,
) -> Mapping[str, object]:
    for item in _ci_plan_records_for_work_group(
        plan, "work-groups", work_group_id
    ):
        return item
    msg = f"work group {work_group_id!r} does not exist"
    raise KeyError(msg)


def _ci_detail_profile(
    plan: Mapping[str, object],
    profile_id: str,
) -> Mapping[str, object]:
    profiles = plan.get("detail-profiles")
    if isinstance(profiles, Sequence) and not isinstance(profiles, str | bytes):
        for item in profiles:
            if (
                isinstance(item, Mapping)
                and item.get("detail-profile-id") == profile_id
            ):
                return item
    msg = f"detail profile {profile_id!r} does not exist"
    raise KeyError(msg)


def _ci_plan_records_for_work_group(
    plan: Mapping[str, object],
    key: str,
    work_group_id: str,
) -> list[Mapping[str, object]]:
    records = plan.get(key)
    if not isinstance(records, Sequence) or isinstance(records, str | bytes):
        return []
    return [
        item
        for item in records
        if isinstance(item, Mapping)
        and item.get("work-group-id") == work_group_id
    ]


def _ci_descriptor_obligation_path(obligation: Mapping[str, object]) -> str:
    target = obligation.get("coverage-target")
    if isinstance(target, Mapping) and isinstance(target.get("id"), str):
        return str(target["id"])
    return str(obligation.get("descriptor-path"))


def _ci_descriptor_fact(
    fact_snapshot: Mapping[str, object] | None,
    descriptor_path: str,
) -> Mapping[str, object] | None:
    if fact_snapshot is None:
        return None
    providers = fact_snapshot.get("providers")
    if not isinstance(providers, Sequence) or isinstance(
        providers, str | bytes
    ):
        return None
    for provider in providers:
        if not isinstance(provider, Mapping):
            continue
        descriptors = provider.get("descriptors")
        if not isinstance(descriptors, Sequence) or isinstance(
            descriptors, str | bytes
        ):
            continue
        for descriptor in descriptors:
            if (
                isinstance(descriptor, Mapping)
                and descriptor.get("descriptor-path") == descriptor_path
            ):
                return descriptor
    return None


def _ci_observed_receipt_inputs(
    *,
    plan: Mapping[str, object],
    assignments: Mapping[str, object],
    observed_artifacts_dir: str,
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
) -> list[CiValidationObservedReceiptInput]:
    if not observed_artifacts_dir:
        return []
    root = Path(observed_artifacts_dir)
    if not root.is_dir():
        return []
    items = assignments.get("assignments")
    if not isinstance(items, Sequence) or isinstance(items, str | bytes):
        return []
    assignment_by_receipt_name = {
        artifact_physical_name(str(item["receipt-artifact-ref"])): item
        for item in items
        if isinstance(item, Mapping)
        and isinstance(item.get("receipt-artifact-ref"), str)
    }
    excluded_names = _ci_excluded_observed_artifact_names(plan, assignments)
    observed: list[CiValidationObservedReceiptInput] = []
    for artifact_dir in sorted(root.iterdir(), key=lambda item: item.name):
        if not artifact_dir.is_dir() or artifact_dir.name in excluded_names:
            continue
        assignment = assignment_by_receipt_name.get(artifact_dir.name)
        if assignment is None and not (artifact_dir / "receipt.json").is_file():
            continue
        observed_input = _ci_observed_receipt_input(
            plan=plan,
            assignments=assignments,
            assignment=assignment,
            artifact_dir=artifact_dir,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
        )
        observed.append(observed_input)
    return observed


def _ci_observed_receipt_input(
    *,
    plan: Mapping[str, object],
    assignments: Mapping[str, object],
    assignment: Mapping[str, object] | None,
    artifact_dir: Path,
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
) -> CiValidationObservedReceiptInput:
    if assignment is None:
        return _ci_unassigned_observed_receipt_input(artifact_dir, plan=plan)
    receipt_ref = str(assignment["receipt-artifact-ref"])
    observation_ref = str(assignment["writer-observation-ref"])
    receipt_path = artifact_dir / "receipt.json"
    observation_path = (
        artifact_dir.parent
        / artifact_physical_name(observation_ref)
        / "writer-observation.json"
    )
    metadata_path = (
        artifact_dir.parent
        / artifact_physical_name(observation_ref)
        / "receipt-artifact-metadata.json"
    )
    receipt: Mapping[str, object] | None = None
    raw_receipt: bytes | None = None
    validation_result = _ci_observed_validation_result(artifact_dir)
    try:
        raw_receipt = receipt_path.read_bytes()
        receipt = load_ci_validation_receipt_payload(raw_receipt)
    except (
        ContractValidationError,
        OSError,
        TypeError,
        ValueError,
        json.JSONDecodeError,
    ):
        receipt = None
    observation = None
    artifact_instance_id = _ci_independent_artifact_instance_id(
        metadata_path,
        receipt_ref=receipt_ref,
    )
    try:
        candidate_observation = _read_json(observation_path)
        if artifact_instance_id is not None:
            validate_ci_validation_writer_observation(
                candidate_observation,
                plan=plan,
                selector_assignments_manifest=assignments,
                assignment=assignment,
                expected_artifact_instance_id=artifact_instance_id,
                changed_files_snapshot=changed_files_snapshot,
                fact_snapshot=fact_snapshot,
            )
            observation = candidate_observation
    except (
        ContractValidationError,
        KeyError,
        OSError,
        TypeError,
        ValueError,
        json.JSONDecodeError,
    ):
        observation = None
    content_digest = (
        ci_validation_receipt_content_digest(raw_receipt)
        if raw_receipt is not None
        else None
    )
    artifact_instance_id = artifact_instance_id or artifact_dir.name
    entry = {
        "observed-entry-id": ci_validation_observed_entry_id(
            run_id=str(cast("Mapping[str, object]", plan["run"])["run-id"]),
            run_attempt=str(
                cast("Mapping[str, object]", plan["run"])["run-attempt"]
            ),
            artifact_ref=receipt_ref,
            artifact_instance_id=artifact_instance_id,
        ),
        "artifact-ref": receipt_ref,
        "physical-artifact-name": artifact_physical_name(receipt_ref),
        "artifact-instance-id": artifact_instance_id,
        "assignment-id": assignment["assignment-id"],
        "writer-work-group-id": assignment["work-group-id"],
        "trusted-writer-id": assignment["trusted-writer-id"],
        "observed-writer-id": observation.get("observed-writer-id")
        if observation is not None
        else None,
        "writer-observation-ref": observation_ref,
        "receipt-id": receipt.get("receipt-id")
        if receipt is not None
        else None,
        "receipt-content-digest": content_digest,
    }
    return CiValidationObservedReceiptInput(
        manifest_entry=entry,
        receipt=receipt,
        raw_receipt_bytes=raw_receipt,
        validation_result=validation_result,
    )


def _ci_unassigned_observed_receipt_input(
    artifact_dir: Path,
    *,
    plan: Mapping[str, object],
) -> CiValidationObservedReceiptInput:
    raw_receipt: bytes | None
    receipt: Mapping[str, object] | None
    validation_result = _ci_observed_validation_result(artifact_dir)
    try:
        raw_receipt = (artifact_dir / "receipt.json").read_bytes()
    except OSError:
        raw_receipt = None
        receipt = None
    else:
        try:
            receipt = load_ci_validation_receipt_payload(raw_receipt)
        except (
            ContractValidationError,
            TypeError,
            ValueError,
            json.JSONDecodeError,
        ):
            receipt = None
    artifact_ref = _ci_verified_unassigned_receipt_ref(
        receipt,
        physical_name=artifact_dir.name,
        plan=plan,
    )
    return CiValidationObservedReceiptInput(
        manifest_entry=_ci_unassigned_receipt_manifest_entry(
            artifact_dir,
            plan=plan,
            artifact_ref=artifact_ref,
            receipt_id=receipt.get("receipt-id")
            if receipt is not None and artifact_ref is not None
            else None,
            raw_receipt=raw_receipt,
        ),
        receipt=receipt,
        raw_receipt_bytes=raw_receipt,
        validation_result=validation_result,
    )


def _ci_observed_validation_result(
    artifact_dir: Path,
) -> Mapping[str, object] | None:
    try:
        candidate = _read_json(artifact_dir / "validation-result.json")
    except (OSError, TypeError, ValueError, json.JSONDecodeError):
        return None
    return candidate if isinstance(candidate, Mapping) else None


def _ci_unassigned_receipt_manifest_entry(
    artifact_dir: Path,
    *,
    plan: Mapping[str, object],
    artifact_ref: str | None,
    receipt_id: object,
    raw_receipt: bytes | None,
) -> Mapping[str, object]:
    run = cast("Mapping[str, object]", plan["run"])
    physical_name = artifact_dir.name
    return {
        "observed-entry-id": ci_validation_observed_entry_id(
            run_id=str(run["run-id"]),
            run_attempt=str(run["run-attempt"]),
            artifact_ref=artifact_ref,
            artifact_instance_id=physical_name,
        ),
        "artifact-ref": artifact_ref,
        "physical-artifact-name": physical_name,
        "artifact-instance-id": physical_name,
        "assignment-id": None,
        "writer-work-group-id": None,
        "trusted-writer-id": None,
        "observed-writer-id": None,
        "writer-observation-ref": None,
        "receipt-id": receipt_id if isinstance(receipt_id, str) else None,
        "receipt-content-digest": ci_validation_receipt_content_digest(
            raw_receipt
        )
        if raw_receipt is not None
        else None,
    }


def _ci_verified_unassigned_receipt_ref(
    receipt: Mapping[str, object] | None,
    *,
    physical_name: str,
    plan: Mapping[str, object],
) -> str | None:
    if receipt is None:
        return None
    artifact_ref = receipt.get("artifact-ref")
    if not isinstance(artifact_ref, str):
        return None
    run = cast("Mapping[str, object]", plan["run"])
    match = re.fullmatch(
        r"ci-validation/receipts/([^/]+)/([^/]+)/([^/]+)/receipt\.json",
        artifact_ref,
    )
    if (
        match is None
        or match.group(1) != str(run["run-id"])
        or match.group(2) != str(run["run-attempt"])
        or _CI_LOCAL_ID_RE.fullmatch(match.group(3)) is None
    ):
        return None
    try:
        expected_physical_name = artifact_physical_name(artifact_ref)
    except ContractValidationError:
        return None
    if expected_physical_name != physical_name:
        return None
    return artifact_ref


def _ci_independent_artifact_instance_id(
    metadata_path: Path,
    *,
    receipt_ref: str,
) -> str | None:
    try:
        metadata = _read_json(metadata_path)
    except (OSError, TypeError, ValueError, json.JSONDecodeError):
        return None
    if metadata.get("artifact-ref") != receipt_ref:
        return None
    if metadata.get("physical-artifact-name") != artifact_physical_name(
        receipt_ref
    ):
        return None
    artifact_instance_id = metadata.get("artifact-instance-id")
    if not isinstance(artifact_instance_id, str) or artifact_instance_id == "":
        return None
    return artifact_instance_id


def _ci_excluded_observed_artifact_names(
    plan: Mapping[str, object],
    assignments: Mapping[str, object],
) -> set[str]:
    run = cast("Mapping[str, object]", plan["run"])
    run_id = str(run["run-id"])
    run_attempt = str(run["run-attempt"])
    refs = {
        ci_validation_request_artifact_ref(
            run_id=run_id,
            run_attempt=run_attempt,
        ),
        ci_validation_plan_artifact_ref(
            run_id=run_id,
            run_attempt=run_attempt,
        ),
        ci_validation_planner_diagnostics_artifact_ref(
            run_id=run_id,
            run_attempt=run_attempt,
        ),
        ci_validation_changed_files_snapshot_artifact_ref(
            run_id=run_id,
            run_attempt=run_attempt,
        ),
        ci_validation_fact_snapshot_artifact_ref(
            run_id=run_id,
            run_attempt=run_attempt,
        ),
        ci_validation_selector_assignments_artifact_ref(
            run_id=run_id,
            run_attempt=run_attempt,
        ),
        ci_validation_receipt_manifest_artifact_ref(
            run_id=run_id,
            run_attempt=run_attempt,
        ),
        ci_validation_aggregate_artifact_ref(
            run_id=run_id,
            run_attempt=run_attempt,
        ),
    }
    items = assignments.get("assignments")
    if isinstance(items, Sequence) and not isinstance(items, str | bytes):
        refs.update(
            str(item["writer-observation-ref"])
            for item in items
            if isinstance(item, Mapping)
            and isinstance(item.get("writer-observation-ref"), str)
        )
    return {artifact_physical_name(ref) for ref in refs}


def _parse_bool(value: str | bool) -> bool:
    if isinstance(value, bool):
        return value
    return value.lower() == "true"


def _bool_str(value: bool) -> str:
    return "true" if value else "false"


def _read_json(path: Path) -> Json:
    payload = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(payload, dict):
        msg = f"{path} must contain a JSON object"
        raise TypeError(msg)
    return payload


def _mapping(value: object, path: str) -> Mapping[str, Any]:
    if not isinstance(value, Mapping):
        msg = f"{path} must be a JSON object"
        raise TypeError(msg)
    return value


def _require_mapping(value: object, label: str) -> None:
    if not isinstance(value, Mapping):
        msg = f"{label} must be a JSON object"
        raise TypeError(msg)


def _read_optional_json(value: str) -> Json | None:
    if not value:
        return None
    path = Path(value)
    if not path.is_file():
        return None
    return _read_json(path)


def _write_json(path: Path, document: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(document, indent=2, sort_keys=True, ensure_ascii=False)
        + "\n",
        encoding="utf-8",
    )


def _write_outputs(path: str | None, values: Mapping[str, str]) -> None:
    if not path:
        return
    with Path(path).open("a", encoding="utf-8") as handle:
        for key, value in values.items():
            if "\n" in value:
                token = hashlib.sha256(f"{key}\n{value}".encode()).hexdigest()
                handle.write(f"{key}<<{token}\n{value}\n{token}\n")
            else:
                handle.write(f"{key}={value}\n")


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except RuntimeError as exc:
        text = str(exc)
        if text.startswith("{"):
            sys.stderr.write(text + "\n")
        else:
            sys.stderr.write(f"{exc}\n")
        raise SystemExit(1) from exc
