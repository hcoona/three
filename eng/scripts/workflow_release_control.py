#!/usr/bin/env python3
# ruff: noqa: E501, PLR2004, PLR0913, SIM115, ARG001, ANN401, FBT001
"""Control-plane helpers for Three workflow release GitHub Actions."""

from __future__ import annotations

import argparse
import errno
import fnmatch
import hashlib
import http.client
import io
import json
import math
import os
import re
import shutil
import stat
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request
import zipfile
from collections.abc import Collection, Iterable, Mapping, Sequence
from contextlib import suppress
from datetime import UTC, datetime
from pathlib import Path, PurePosixPath
from typing import TYPE_CHECKING, Any, Literal, cast

import yaml

_REPO_ROOT = Path(__file__).resolve().parents[2]
for _WORKSPACE_SRC in (
    _REPO_ROOT / "src/public/lib/three-workflow-release-contracts/src",
    _REPO_ROOT / "src/public/lib/three-workflow-release-planner/src",
    _REPO_ROOT / "src/public/lib/three-workflow-release-proof/src",
    _REPO_ROOT / "src/public/lib/three-workflow-release-authoring/src",
    _REPO_ROOT / "src/public/lib/three-workflow-release-build/src",
    _REPO_ROOT / "src/public/lib/three-workflow-release-metadata/src",
):
    if _WORKSPACE_SRC.is_dir():
        sys.path.insert(0, str(_WORKSPACE_SRC))

from three_workflow_release_authoring import (  # noqa: E402
    CATALOG_PATH,
    AuthoringValidationError,
    validate_authoring,
    validate_project_descriptor_document,
    validate_target_catalog_document,
)
from three_workflow_release_build import (  # noqa: E402
    BuildExecutorError,
    execute_build,
)
from three_workflow_release_contracts import (  # noqa: E402
    API_VERSIONS_BY_KIND,
    DETAILS_BY_DIAGNOSTIC_CODE,
    CiValidationKind,
    ContractValidationError,
    DiagnosticDetail,
    DiagnosticFamily,
    DiagnosticSeverity,
    DiagnosticVerdictEffect,
    ValidationIssue,
    admit_exactly_one_artifact,
    artifact_physical_name,
    canonical_json_bytes,
    canonical_json_digest,
    ci_validation_aggregate_evidence_manifest_artifact_ref,
    ci_validation_aggregate_evidence_manifest_payload_digest,
    ci_validation_aggregate_summary_artifact_ref,
    ci_validation_aggregate_summary_payload_digest,
    ci_validation_batch_evidence_bundle_payload_digest,
    ci_validation_batch_evidence_candidate_id,
    ci_validation_changed_files_snapshot_artifact_ref,
    ci_validation_diagnostic,
    ci_validation_execution_batch_manifest_artifact_ref,
    ci_validation_execution_batch_manifest_payload_digest,
    ci_validation_fact_snapshot_artifact_ref,
    ci_validation_plan_artifact_ref,
    ci_validation_plan_digest,
    ci_validation_planner_diagnostics_artifact_ref,
    ci_validation_request_artifact_ref,
    ci_validation_request_projection,
    collect_artifacts_by_name,
    freeze_ci_validation_aggregate_summary,
    freeze_ci_validation_batch_evidence_bundle,
    materialize_ci_validation_execution_batches,
    validate_ci_validation_aggregate_evidence_manifest,
    validate_ci_validation_aggregate_summary,
    validate_ci_validation_batch_evidence_bundle,
    validate_ci_validation_execution_batch_manifest,
    validate_ci_validation_plan,
    validate_ci_validation_request,
    validate_contract,
)
from three_workflow_release_contracts.artifact_names import (  # noqa: E402
    ArtifactNameInputs,
    artifact_name,
    github_release_asset_binding_json,
    immutable_binding_json,
)
from three_workflow_release_contracts.ci_validation_assignments import (  # noqa: E402
    ci_validation_writer_id,
)
from three_workflow_release_contracts.ci_validation_batches import (  # noqa: E402
    _freeze_ci_validation_aggregate_evidence_manifest,
)
from three_workflow_release_metadata import (  # noqa: E402
    collect_dotnet_metadata,
)
from three_workflow_release_planner import (  # noqa: E402
    PlannerInputs,
    plan_release,
)

if TYPE_CHECKING:
    from three_workflow_release_contracts.actions_artifacts import (
        GitHubActionsArtifactMetadata,
    )

Json = dict[str, Any]
_CiValidationOutcome = Literal["success", "blocking-failure", "skipped"]


class _TrustedDependencyBundle(dict[str, object]):
    def __init__(
        self,
        bundle: Mapping[str, object],
        *,
        artifact_instance_id: str,
        admitted_candidate_id: str,
    ) -> None:
        super().__init__(bundle)
        self.artifact_instance_id = artifact_instance_id
        self.admitted_candidate_id = admitted_candidate_id


_FINAL_NAMESPACE_CLOSURE_MISMATCH_DETAIL = "final-namespace-closure-mismatch"
_STRICT_CI_PHYSICAL_ARTIFACT_NAME_RE = re.compile(
    r"^three-ci-validation-[1-9][0-9]*-[1-9][0-9]*-[0-9a-f]{64}$",
)
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
_CI_VALIDATION_PRE_FINAL_NAMESPACE_ARTIFACT_CAP = 18
_CI_VALIDATION_TOTAL_NAMESPACE_ARTIFACT_CAP = 20
_CI_VALIDATION_LIVE_NAMESPACE_ARTIFACT_CAP = (
    _CI_VALIDATION_PRE_FINAL_NAMESPACE_ARTIFACT_CAP
)
_CI_INVALID_AGGREGATE_DURATION_SECONDS = 121
_CI_VALIDATION_LIVE_NAMESPACE_ENUMERATION_PAGE_CAP = 2
_CI_VALIDATION_LIVE_NAMESPACE_ENUMERATION_ITEM_CAP = 200
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
    "ci-validation/execution-batches": (
        "materialize-execution-batches",
        "materialize-execution-batches",
    ),
    "ci-validation/aggregate": ("aggregate-evidence", "aggregate-evidence"),
}
_CI_DOWNLOADER_OBSERVATION_FILE = "downloader-observation.json"
_CI_DOWNLOADER_ADMITTED_BATCH_ARTIFACTS_KEY = "admitted-batch-artifacts"
_CI_ORCHESTRATOR_STATE_ADMISSION_SOURCE = "orchestrator-artifact-id-state"
_CI_ORCHESTRATOR_LIVE_CROSS_FAMILY_ADMISSION_SOURCE = (
    "orchestrator-live-cross-family"
)


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
    _add_materialize_ci_validation_execution_batches(subparsers)
    _add_validate_ci_validation_descriptors(subparsers)
    _add_run_ci_validation_batch_commands(subparsers)
    _add_write_ci_validation_batch_evidence_bundle(subparsers)
    _add_run_ci_validation_runner_family_orchestrator_step(subparsers)
    _add_record_ci_validation_runner_family_orchestrator_upload(subparsers)
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
    parser.add_argument("--request", default="")
    parser.add_argument("--plan", default="")
    parser.add_argument("--changed-files-snapshot", default="")
    parser.add_argument("--fact-snapshot", default="")
    parser.add_argument("--execution-batch-manifest", default="")
    parser.add_argument(
        "--expected-artifact",
        action="append",
        default=[],
        help=(
            "JSON object with artifact-ref, artifact-instance-id, "
            "producer-boundary, and producer-job"
        ),
    )
    parser.add_argument(
        "--max-prefixed-validation-artifacts",
        type=int,
        default=_CI_VALIDATION_PRE_FINAL_NAMESPACE_ARTIFACT_CAP,
        help=(
            "Maximum live three-ci-validation-* artifacts allowed for this "
            "boundary check. Use 18 for pre-final closure checks and 20 after "
            "the aggregate summary upload."
        ),
    )
    parser.add_argument(
        "--expected-prefixed-validation-artifacts",
        type=int,
        default=None,
        help=(
            "Exact non-expired three-ci-validation-* artifact count expected "
            "after final publication."
        ),
    )
    parser.set_defaults(func=_cmd_verify_ci_validation_artifact_boundaries)


def _add_materialize_ci_validation_execution_batches(
    subparsers: argparse._SubParsersAction[argparse.ArgumentParser],
) -> None:
    parser = subparsers.add_parser(
        "materialize-ci-validation-execution-batches"
    )
    parser.add_argument("--plan", required=True)
    parser.add_argument("--request", required=True)
    parser.add_argument("--changed-files-snapshot", default="")
    parser.add_argument("--fact-snapshot", default="")
    parser.add_argument("--workflow", required=True)
    parser.add_argument("--execution-job", default="execution-batch")
    parser.add_argument(
        "--non-batch-control-plane-job-count",
        type=int,
        default=0,
    )
    parser.add_argument("--expected-run-id", required=True)
    parser.add_argument("--expected-run-attempt", required=True)
    parser.add_argument("--created-at")
    parser.add_argument("--execution-batch-manifest-out", required=True)
    parser.add_argument("--github-output")
    parser.set_defaults(func=_cmd_materialize_ci_validation_execution_batches)


def _add_aggregate_ci_evidence(
    subparsers: argparse._SubParsersAction[argparse.ArgumentParser],
) -> None:
    parser = subparsers.add_parser("aggregate-ci-evidence")
    parser.add_argument("--repository", required=True)
    parser.add_argument("--workflow", required=True)
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--run-attempt", required=True)
    parser.add_argument("--plan", default="")
    parser.add_argument("--request", default="")
    parser.add_argument("--execution-batch-manifest", default="")
    parser.add_argument("--changed-files-snapshot", default="")
    parser.add_argument("--fact-snapshot", default="")
    parser.add_argument("--observed-artifacts-dir", required=True)
    parser.add_argument("--expected-request-artifact-id", default=None)
    parser.add_argument("--expected-plan-artifact-id", default=None)
    parser.add_argument(
        "--expected-changed-files-snapshot-artifact-id",
        default=None,
    )
    parser.add_argument("--expected-fact-snapshot-artifact-id", default=None)
    parser.add_argument(
        "--expected-execution-batch-manifest-artifact-id",
        default=None,
    )
    parser.add_argument(
        "--aggregate-evidence-manifest-artifact-id", default=None
    )
    parser.add_argument(
        "--aggregate-evidence-manifest-producer-verified",
        action="store_true",
        help=(
            "Assert that the uploaded aggregate evidence manifest artifact "
            "passed producer-boundary verification before summary generation."
        ),
    )
    parser.add_argument(
        "--aggregate-phase",
        choices=("all", "evidence", "summary"),
        default="all",
    )
    parser.add_argument("--batch-materialization-failed", action="store_true")
    parser.add_argument("--created-at")
    parser.add_argument("--started-at")
    parser.add_argument("--completed-at")
    parser.add_argument("--aggregate-evidence-manifest-out", required=True)
    parser.add_argument("--aggregate-summary-out", required=True)
    parser.add_argument("--github-output")
    parser.set_defaults(func=_cmd_aggregate_ci_evidence)


def _add_download_ci_validation_observed_artifacts(
    subparsers: argparse._SubParsersAction[argparse.ArgumentParser],
) -> None:
    parser = subparsers.add_parser(
        "download-ci-validation-observed-artifacts",
        description=(
            "Enumerate and download current-run GitHub Actions artifacts, "
            "recording downloader-observed metadata for aggregate validation."
        ),
        help=(
            "download artifacts and record downloader-observed metadata for "
            "aggregate validation"
        ),
    )
    parser.add_argument("--repository", required=True)
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--run-attempt", default="")
    parser.add_argument("--plan", default="")
    parser.add_argument("--execution-batch-manifest", default="")
    parser.add_argument("--observed-artifacts-dir", required=True)
    parser.add_argument("--github-output")
    parser.set_defaults(func=_cmd_download_ci_validation_observed_artifacts)


def _add_run_ci_validation_batch_commands(
    subparsers: argparse._SubParsersAction[argparse.ArgumentParser],
) -> None:
    parser = subparsers.add_parser("run-ci-validation-batch-commands")
    parser.add_argument("--plan", required=True)
    parser.add_argument("--request", default="")
    parser.add_argument("--execution-batch-manifest", required=True)
    parser.add_argument("--changed-files-snapshot", default="")
    parser.add_argument("--fact-snapshot", default="")
    parser.add_argument("--observed-artifacts-dir", default="")
    parser.add_argument("--expected-run-id", default="")
    parser.add_argument("--expected-run-attempt", default="")
    parser.add_argument("--dependency-bundle", action="append", default=[])
    parser.add_argument("--observed-commit-sha", required=True)
    parser.add_argument("--matrix-row-json", required=True)
    parser.add_argument("--repo-root", default=".")
    parser.add_argument("--result-out-dir", required=True)
    parser.add_argument("--github-output")
    parser.set_defaults(func=_cmd_run_ci_validation_batch_commands)


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
    parser.add_argument("--observed-artifacts-dir", default="")
    parser.add_argument("--observed-commit-sha", required=True)
    parser.add_argument("--validation-result", action="append", default=[])
    parser.add_argument("--dependency-results-json", default="")
    parser.add_argument("--dependency-bundle", action="append", default=[])
    parser.add_argument(
        "--orchestrator-slot-index",
        default=None,
        help="Physical runner-family orchestrator slot index for writer evidence.",
    )
    parser.add_argument("--started-at")
    parser.add_argument("--completed-at")
    parser.add_argument("--created-at")
    parser.add_argument("--bundle-out", required=True)
    parser.add_argument("--github-output")
    parser.set_defaults(func=_cmd_write_ci_validation_batch_evidence_bundle)


def _add_run_ci_validation_runner_family_orchestrator_step(
    subparsers: argparse._SubParsersAction[argparse.ArgumentParser],
) -> None:
    parser = subparsers.add_parser(
        "run-ci-validation-runner-family-orchestrator-step",
        description=(
            "Run the next dependency-ready CI validation execution batch for "
            "one runner family."
        ),
    )
    parser.add_argument("--plan", required=True)
    parser.add_argument("--request", required=True)
    parser.add_argument("--execution-batch-manifest", required=True)
    parser.add_argument("--changed-files-snapshot", default="")
    parser.add_argument("--fact-snapshot", default="")
    parser.add_argument("--runner-family", required=True)
    parser.add_argument("--repository", default="")
    parser.add_argument("--workflow", required=True)
    parser.add_argument("--job", required=True)
    parser.add_argument("--expected-run-id", required=True)
    parser.add_argument("--expected-run-attempt", required=True)
    parser.add_argument("--observed-artifacts-dir", required=True)
    parser.add_argument("--state-dir", required=True)
    parser.add_argument("--work-dir", required=True)
    parser.add_argument("--slot-index", required=True)
    parser.add_argument("--observed-commit-sha", required=True)
    parser.add_argument("--repo-root", default=".")
    parser.add_argument("--github-output")
    parser.set_defaults(
        func=_cmd_run_ci_validation_runner_family_orchestrator_step
    )


def _add_record_ci_validation_runner_family_orchestrator_upload(
    subparsers: argparse._SubParsersAction[argparse.ArgumentParser],
) -> None:
    parser = subparsers.add_parser(
        "record-ci-validation-runner-family-orchestrator-upload",
        description=(
            "Record the artifact id assigned to a runner-family orchestrator "
            "batch upload."
        ),
    )
    parser.add_argument("--execution-batch-manifest", required=True)
    parser.add_argument("--batch-id", required=True)
    parser.add_argument("--artifact-id", required=True)
    parser.add_argument("--artifact-name", required=True)
    parser.add_argument("--expected-run-id", required=True)
    parser.add_argument("--expected-run-attempt", required=True)
    parser.add_argument("--orchestrator-slot-index", default=None)
    parser.add_argument("--observed-artifacts-dir", required=True)
    parser.add_argument("--state-dir", required=True)
    parser.add_argument("--github-output")
    parser.set_defaults(
        func=_cmd_record_ci_validation_runner_family_orchestrator_upload
    )


def _add_validate_ci_validation_descriptors(
    subparsers: argparse._SubParsersAction[argparse.ArgumentParser],
) -> None:
    parser = subparsers.add_parser("validate-ci-validation-descriptors")
    parser.add_argument("--plan", required=True)
    parser.add_argument("--work-group-id", required=True)
    parser.add_argument("--repo-root", default=".")
    parser.set_defaults(func=_cmd_validate_ci_validation_descriptors)


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
            "execution_batch_manifest_artifact_name": artifact_physical_name(
                ci_validation_execution_batch_manifest_artifact_ref(
                    run_id=run_id,
                    run_attempt=run_attempt,
                )
            ),
            "aggregate_evidence_manifest_artifact_name": artifact_physical_name(
                ci_validation_aggregate_evidence_manifest_artifact_ref(
                    run_id=run_id,
                    run_attempt=run_attempt,
                )
            ),
            "aggregate_summary_artifact_name": artifact_physical_name(
                ci_validation_aggregate_summary_artifact_ref(
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
        max_prefixed_validation_artifacts = int(
            getattr(
                args,
                "max_prefixed_validation_artifacts",
                _CI_VALIDATION_PRE_FINAL_NAMESPACE_ARTIFACT_CAP,
            )
        )
        expected_prefixed_validation_artifacts = getattr(
            args,
            "expected_prefixed_validation_artifacts",
            None,
        )
        prior_attempt_artifact_names = (
            _ci_known_prior_attempt_artifact_names_from_expected(
                expected,
                run_id=str(args.run_id),
                run_attempt=str(args.run_attempt),
            )
        )
        excluded_prefixed_artifact_names = prior_attempt_artifact_names
        if (
            expected_prefixed_validation_artifacts is None
            and max_prefixed_validation_artifacts
            < _CI_VALIDATION_TOTAL_NAMESPACE_ARTIFACT_CAP
        ):
            excluded_prefixed_artifact_names = (
                _ci_current_final_aggregate_artifact_names_for_boundary(
                    expected,
                    run_id=str(args.run_id),
                    run_attempt=str(args.run_attempt),
                )
                | prior_attempt_artifact_names
            )
        artifacts = _github_actions_run_artifacts_for_boundary_check(
            repository=args.repository,
            run_id=str(args.run_id),
            run_attempt=str(args.run_attempt),
            max_prefixed_validation_artifacts=max_prefixed_validation_artifacts,
            excluded_prefixed_artifact_names=excluded_prefixed_artifact_names,
        )
        diagnostics = _ci_verify_expected_artifact_producer_boundaries(
            artifacts=artifacts,
            expected_artifacts=expected,
            workflow=args.workflow,
            run_id=str(args.run_id),
            run_attempt=str(args.run_attempt),
            expected_prefixed_validation_artifacts=(
                expected_prefixed_validation_artifacts
            ),
        )
        if not diagnostics:
            diagnostics = _ci_verify_expected_final_artifact_uploaded_bytes(
                expected,
                run_id=str(args.run_id),
                run_attempt=str(args.run_attempt),
                request=_read_optional_json(getattr(args, "request", "")),
                plan=_read_optional_json(getattr(args, "plan", "")),
                changed_files_snapshot=_read_optional_json(
                    getattr(args, "changed_files_snapshot", "")
                ),
                fact_snapshot=_read_optional_json(
                    getattr(args, "fact_snapshot", "")
                ),
                execution_batch_manifest=_read_optional_json(
                    getattr(args, "execution_batch_manifest", "")
                ),
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


def _ci_verify_expected_artifact_producer_boundaries(  # noqa: C901
    *,
    artifacts: Sequence[GitHubActionsArtifactMetadata | Mapping[str, object]],
    expected_artifacts: Sequence[Mapping[str, object]],
    workflow: str,
    run_id: str,
    run_attempt: str,
    expected_prefixed_validation_artifacts: int | None = None,
) -> list[Mapping[str, object]]:
    prior_attempt_artifact_names = (
        _ci_known_prior_attempt_artifact_names_from_expected(
            expected_artifacts,
            run_id=run_id,
            run_attempt=run_attempt,
        )
    )
    active_artifacts = _ci_artifacts_excluding_names(
        _ci_artifacts_excluding_expired(artifacts),
        prior_attempt_artifact_names,
    )
    groups = collect_artifacts_by_name(active_artifacts)
    diagnostics: list[Mapping[str, object]] = []
    if expected_prefixed_validation_artifacts is not None:
        if (
            not isinstance(expected_prefixed_validation_artifacts, int)
            or isinstance(expected_prefixed_validation_artifacts, bool)
            or expected_prefixed_validation_artifacts < 0
        ):
            diagnostics.append(
                _ci_boundary_diagnostic(
                    index=-1,
                    detail=DiagnosticDetail.STRUCTURALLY_INVALID.value,
                    message=(
                        "expected prefixed validation artifact count is "
                        "malformed"
                    ),
                    source_id=None,
                )
            )
        elif len(active_artifacts) != expected_prefixed_validation_artifacts:
            diagnostics.append(
                _ci_boundary_diagnostic(
                    index=-1,
                    detail=_FINAL_NAMESPACE_CLOSURE_MISMATCH_DETAIL,
                    message=(
                        "observed CI validation artifact namespace total does "
                        "not match the expected final count"
                    ),
                    source_id=None,
                )
            )
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


def _ci_workflow_gate_diagnostic(
    *,
    index: int,
    detail: str,
    message: str,
    source_id: str | None,
) -> Mapping[str, object]:
    return {
        "diagnostic-id": f"workflow-gate/{index:03d}",
        "code": "workflow-gate-failure",
        "detail": detail,
        "message": message,
        "source": {"type": "aggregation", "id": source_id},
        "severity": DiagnosticSeverity.FAIL_CLOSED.value,
        "verdict-effect": DiagnosticVerdictEffect.FAIL_CLOSED.value,
    }


def _ci_verify_expected_final_artifact_uploaded_bytes(
    expected_artifacts: Sequence[Mapping[str, object]],
    *,
    run_id: str,
    run_attempt: str,
    request: Mapping[str, object] | None = None,
    plan: Mapping[str, object] | None = None,
    changed_files_snapshot: Mapping[str, object] | None = None,
    fact_snapshot: Mapping[str, object] | None = None,
    execution_batch_manifest: Mapping[str, object] | None = None,
) -> list[Mapping[str, object]]:
    diagnostics: list[Mapping[str, object]] = []
    uploaded: dict[str, Mapping[str, object]] = {}
    for index, expected in enumerate(expected_artifacts):
        artifact_ref = expected.get("artifact-ref")
        downloaded_path = expected.get("downloaded-path")
        if not (
            isinstance(artifact_ref, str)
            and isinstance(downloaded_path, str)
            and downloaded_path
        ):
            continue
        kind = _ci_control_artifact_kind(artifact_ref)
        if kind not in {"aggregate-evidence-manifest", "aggregate-summary"}:
            continue
        try:
            document = _ci_load_uploaded_canonical_json_object(
                Path(downloaded_path)
            )
            if kind == "aggregate-evidence-manifest":
                validate_ci_validation_aggregate_evidence_manifest(
                    document,
                    plan=plan,
                    request=request,
                    changed_files_snapshot=changed_files_snapshot,
                    fact_snapshot=fact_snapshot,
                    execution_batch_manifest=execution_batch_manifest,
                    expected_run_id=run_id,
                    expected_run_attempt=run_attempt,
                )
            expected_digest = expected.get("content-digest")
            if isinstance(expected_digest, str) and expected_digest:
                _ci_validate_uploaded_artifact_digest(
                    document,
                    expected_digest,
                )
            uploaded[kind] = document
        except (
            ContractValidationError,
            OSError,
            TypeError,
            UnicodeDecodeError,
            ValueError,
        ) as exc:
            diagnostics.append(
                _ci_workflow_gate_diagnostic(
                    index=index,
                    detail=_ci_uploaded_final_artifact_failure_detail(kind),
                    message=(
                        "uploaded final artifact bytes are not authoritative: "
                        f"{exc}"
                    ),
                    source_id=artifact_ref,
                )
            )
    diagnostics.extend(
        _ci_verify_uploaded_final_artifact_digest_claims(
            uploaded,
            expected_artifacts,
            run_id=run_id,
            run_attempt=run_attempt,
        )
    )
    return diagnostics


def _ci_load_uploaded_canonical_json_object(path: Path) -> Mapping[str, object]:
    raw = path.read_bytes()
    document = json.loads(raw.decode("utf-8"))
    if not isinstance(document, Mapping):
        msg = "uploaded artifact JSON must be an object"
        raise TypeError(msg)
    canonical = canonical_json_bytes(document)
    if raw != canonical:
        msg = "uploaded artifact is not canonical JSON"
        raise ValueError(msg)
    return document


def _ci_uploaded_final_artifact_failure_detail(kind: str) -> str:
    if kind == "aggregate-evidence-manifest":
        return DiagnosticDetail.AGGREGATE_EVIDENCE_MANIFEST_MALFORMED.value
    return _FINAL_NAMESPACE_CLOSURE_MISMATCH_DETAIL


def _ci_validate_uploaded_artifact_digest(
    document: Mapping[str, object],
    expected_digest: str,
) -> None:
    if canonical_json_digest(document) != expected_digest:
        msg = "uploaded artifact digest does not match expected digest"
        raise ValueError(msg)


def _ci_verify_uploaded_final_artifact_digest_claims(
    uploaded: Mapping[str, Mapping[str, object]],
    expected_artifacts: Sequence[Mapping[str, object]],
    *,
    run_id: str,
    run_attempt: str,
) -> list[Mapping[str, object]]:
    manifest = uploaded.get("aggregate-evidence-manifest")
    summary = uploaded.get("aggregate-summary")
    if summary is None:
        return []
    summary_index = _ci_expected_artifact_index(
        expected_artifacts,
        "aggregate-summary",
    )
    try:
        _ci_validate_uploaded_aggregate_summary_final_claims(
            summary,
            manifest,
            run_id=run_id,
            run_attempt=run_attempt,
        )
        if manifest is not None:
            _ci_validate_uploaded_manifest_digest_claim(summary, manifest)
    except (ContractValidationError, TypeError, ValueError) as exc:
        return [
            _ci_workflow_gate_diagnostic(
                index=summary_index,
                detail=_FINAL_NAMESPACE_CLOSURE_MISMATCH_DETAIL,
                message=(
                    "uploaded final aggregate artifacts do not match summary "
                    f"digest claims: {exc}"
                ),
                source_id=ci_validation_aggregate_summary_artifact_ref(
                    run_id=run_id,
                    run_attempt=run_attempt,
                ),
            )
        ]
    return []


def _ci_validate_uploaded_aggregate_summary_final_claims(
    summary: Mapping[str, object],
    manifest: Mapping[str, object] | None,
    *,
    run_id: str,
    run_attempt: str,
) -> None:
    expected_summary_ref = ci_validation_aggregate_summary_artifact_ref(
        run_id=run_id,
        run_attempt=run_attempt,
    )
    expected_manifest_ref = (
        ci_validation_aggregate_evidence_manifest_artifact_ref(
            run_id=run_id,
            run_attempt=run_attempt,
        )
    )
    final_artifacts = summary.get("final-artifacts")
    if not isinstance(final_artifacts, Mapping):
        msg = "final-artifacts claim is missing"
        raise TypeError(msg)
    final_summary = final_artifacts.get("aggregate-summary")
    if not isinstance(final_summary, Mapping):
        msg = "final-artifacts.aggregate-summary claim is missing"
        raise TypeError(msg)
    if final_summary.get("artifact-ref") != expected_summary_ref:
        msg = (
            "final-artifacts.aggregate-summary.artifact-ref does not match run"
        )
        raise ValueError(msg)
    for path, claim in (
        (
            "aggregate-evidence-manifest",
            summary.get("aggregate-evidence-manifest"),
        ),
        (
            "final-artifacts.aggregate-evidence-manifest",
            final_artifacts.get("aggregate-evidence-manifest"),
        ),
    ):
        if not isinstance(claim, Mapping):
            msg = f"{path} claim is missing"
            raise TypeError(msg)
        if claim.get("artifact-ref") != expected_manifest_ref:
            msg = f"{path}.artifact-ref does not match run"
            raise ValueError(msg)
        if manifest is not None and claim.get("artifact-ref") != manifest.get(
            "artifact-ref"
        ):
            msg = f"{path}.artifact-ref does not match uploaded manifest"
            raise ValueError(msg)


def _ci_validate_uploaded_manifest_digest_claim(
    summary: Mapping[str, object],
    manifest: Mapping[str, object],
) -> None:
    uploaded_manifest_digest = canonical_json_digest(manifest)
    final_artifacts = summary.get("final-artifacts")
    final_manifest_claim = (
        final_artifacts.get("aggregate-evidence-manifest")
        if isinstance(final_artifacts, Mapping)
        else None
    )
    for path, claim in (
        (
            "aggregate-evidence-manifest",
            summary.get("aggregate-evidence-manifest"),
        ),
        (
            "final-artifacts.aggregate-evidence-manifest",
            final_manifest_claim,
        ),
    ):
        if not isinstance(claim, Mapping):
            msg = f"{path} claim is missing"
            raise TypeError(msg)
        if claim.get("content-digest") != uploaded_manifest_digest:
            msg = f"{path}.content-digest does not match uploaded bytes"
            raise ValueError(msg)


def _ci_expected_artifact_index(
    expected_artifacts: Sequence[Mapping[str, object]],
    kind: str,
) -> int:
    for index, expected in enumerate(expected_artifacts):
        artifact_ref = expected.get("artifact-ref")
        if (
            isinstance(artifact_ref, str)
            and _ci_control_artifact_kind(artifact_ref) == kind
        ):
            return index
    return -1


def _github_actions_run_artifacts_for_boundary_check(
    *,
    repository: str,
    run_id: str,
    run_attempt: str,
    max_prefixed_validation_artifacts: int,
    excluded_prefixed_artifact_names: Collection[str] = (),
) -> list[Mapping[str, object]]:
    if not (
        1
        <= max_prefixed_validation_artifacts
        <= _CI_VALIDATION_TOTAL_NAMESPACE_ARTIFACT_CAP
    ):
        msg = (
            "max prefixed validation artifacts must be between 1 and "
            f"{_CI_VALIDATION_TOTAL_NAMESPACE_ARTIFACT_CAP}"
        )
        raise ValueError(msg)
    artifacts = _github_actions_run_artifacts(
        repository=repository,
        run_id=run_id,
        run_attempt=run_attempt,
        prefixed_artifact_cap=max_prefixed_validation_artifacts,
        excluded_prefixed_artifact_names=excluded_prefixed_artifact_names,
    )
    if (
        _ci_prefixed_artifact_count(
            artifacts,
            run_id=run_id,
            run_attempt=run_attempt,
            excluded_prefixed_artifact_names=excluded_prefixed_artifact_names,
        )
        > max_prefixed_validation_artifacts
    ):
        msg = (
            "CI validation artifact namespace overflowed during bounded "
            "producer-boundary verification"
        )
        raise RuntimeError(msg)
    return artifacts


def _ci_artifacts_excluding_expired(
    artifacts: Sequence[GitHubActionsArtifactMetadata | Mapping[str, object]],
) -> list[GitHubActionsArtifactMetadata | Mapping[str, object]]:
    return [
        artifact
        for artifact in artifacts
        if not (
            not isinstance(artifact, Mapping)
            and getattr(artifact, "expired", None) is True
        )
        and not (
            isinstance(artifact, Mapping) and artifact.get("expired") is True
        )
    ]


def _ci_artifacts_excluding_names(
    artifacts: Sequence[GitHubActionsArtifactMetadata | Mapping[str, object]],
    artifact_names: Collection[str],
) -> list[GitHubActionsArtifactMetadata | Mapping[str, object]]:
    return [
        artifact
        for artifact in artifacts
        if (name := _ci_artifact_metadata_name(artifact)) is None
        or name not in artifact_names
    ]


def _ci_artifact_metadata_name(
    artifact: GitHubActionsArtifactMetadata | Mapping[str, object],
) -> str | None:
    name = (
        artifact.get("name")
        if isinstance(artifact, Mapping)
        else getattr(artifact, "name", None)
    )
    return name if isinstance(name, str) else None


def _ci_known_prior_attempt_artifact_names_from_expected(
    expected_artifacts: Sequence[Mapping[str, object]],
    *,
    run_id: str,
    run_attempt: str,
) -> set[str]:
    return _ci_known_prior_attempt_artifact_names(
        (
            artifact_ref
            for expected in expected_artifacts
            if isinstance(
                artifact_ref := expected.get("artifact-ref"),
                str,
            )
        ),
        run_id=run_id,
        run_attempt=run_attempt,
    )


def _ci_known_prior_attempt_artifact_names(
    artifact_refs: Iterable[str],
    *,
    run_id: str,
    run_attempt: str,
) -> set[str]:
    try:
        current_attempt = int(run_attempt)
    except ValueError:
        return set()
    if current_attempt <= 1:
        return set()
    current_attempt_segment = f"/{run_id}/{run_attempt}/"
    artifact_names: set[str] = set()
    for artifact_ref in artifact_refs:
        if current_attempt_segment not in artifact_ref:
            continue
        for prior_attempt in range(1, current_attempt):
            prior_ref = artifact_ref.replace(
                current_attempt_segment,
                f"/{run_id}/{prior_attempt}/",
                1,
            )
            try:
                artifact_names.add(artifact_physical_name(prior_ref))
            except ContractValidationError:
                continue
    return artifact_names


def _ci_prefixed_artifact_count(
    artifacts: Sequence[GitHubActionsArtifactMetadata | Mapping[str, object]],
    *,
    run_id: str,
    run_attempt: str,
    excluded_prefixed_artifact_names: Collection[str] = (),
) -> int:
    current_attempt_prefix = _ci_attempt_physical_artifact_name_prefix(
        run_id=run_id,
        run_attempt=run_attempt,
    )
    return sum(
        1
        for artifact in artifacts
        if (name := _ci_artifact_metadata_name(artifact)) is not None
        and name.startswith(current_attempt_prefix)
        and name not in excluded_prefixed_artifact_names
    )


def _ci_attempt_physical_artifact_name_prefix(
    *,
    run_id: str,
    run_attempt: str,
) -> str:
    return f"three-ci-validation-{run_id}-{run_attempt}-"


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
    if detail == _FINAL_NAMESPACE_CLOSURE_MISMATCH_DETAIL:
        return _ci_workflow_gate_diagnostic(
            index=index,
            detail=detail,
            message=message,
            source_id=source_id,
        )
    code = DiagnosticFamily.INVALID_PLAN.value
    if detail.startswith("request-"):
        code = DiagnosticFamily.REQUEST_INVALID.value
    elif detail == DiagnosticDetail.REQUIRED_INPUT_ARTIFACT_FAILURE.value:
        code = DiagnosticFamily.REQUIRED_INPUT_ARTIFACT_FAILURE.value
    elif detail.startswith("execution-batch-manifest"):
        code = DiagnosticFamily.INADMISSIBLE_BATCH_EVIDENCE.value
    elif detail == DiagnosticDetail.AGGREGATE_SUMMARY_WITHOUT_MANIFEST.value:
        code = DiagnosticFamily.AGGREGATE_SUMMARY_WITHOUT_MANIFEST.value
    elif detail == DiagnosticDetail.FINAL_PRODUCER_UNVERIFIED.value:
        code = DiagnosticFamily.FINAL_PRODUCER_UNVERIFIED.value
    elif detail in {
        DiagnosticDetail.NAMESPACE_OVERFLOW.value,
        DiagnosticDetail.UNEXPECTED_CONTRACT_ARTIFACT.value,
        DiagnosticDetail.NAMESPACE_ENUMERATION_UNAVAILABLE.value,
    }:
        code = DiagnosticFamily.NAMESPACE_CLOSURE_FAILURE.value
    elif detail.startswith(
        (
            "final-",
            "aggregate-evidence-manifest",
        )
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
            "aggregate-evidence-manifest": (
                DiagnosticDetail.AGGREGATE_EVIDENCE_MANIFEST_DUPLICATE.value
            ),
            "aggregate-summary": _FINAL_NAMESPACE_CLOSURE_MISMATCH_DETAIL,
            "execution-batch-manifest": (
                DiagnosticDetail.EXECUTION_BATCH_MANIFEST_DUPLICATE.value
            ),
        }.get(kind, DiagnosticDetail.STRUCTURALLY_INVALID.value)
    if "missing candidate" in message:
        return {
            "request": DiagnosticDetail.REQUEST_MISSING.value,
            "plan": DiagnosticDetail.PLAN_MISSING.value,
            "changed-files-snapshot": (
                DiagnosticDetail.CHANGED_FILES_SNAPSHOT_MISSING.value
            ),
            "fact-snapshot": DiagnosticDetail.FACT_SNAPSHOT_MISSING.value,
            "aggregate-evidence-manifest": (
                DiagnosticDetail.AGGREGATE_EVIDENCE_MANIFEST_MISSING.value
            ),
            "aggregate-summary": _FINAL_NAMESPACE_CLOSURE_MISMATCH_DETAIL,
            "execution-batch-manifest": (
                DiagnosticDetail.EXECUTION_BATCH_MANIFEST_MISSING.value
            ),
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
        "aggregate-evidence-manifest": (
            DiagnosticDetail.FINAL_PRODUCER_UNVERIFIED.value
        ),
        "aggregate-summary": _FINAL_NAMESPACE_CLOSURE_MISMATCH_DETAIL,
        "execution-batch-manifest": (
            DiagnosticDetail.EXECUTION_BATCH_MANIFEST_MALFORMED.value
        ),
    }.get(kind, DiagnosticDetail.STRUCTURALLY_INVALID.value)


def _ci_control_artifact_kind(artifact_ref: str) -> str:
    suffixes = {
        "/ci-validation-request.json": "request",
        "/validation-plan.json": "plan",
        "/changed-files-snapshot.json": "changed-files-snapshot",
        "/fact-snapshot.json": "fact-snapshot",
        "/execution-batch-manifest.json": "execution-batch-manifest",
        "/aggregate-evidence-manifest.json": "aggregate-evidence-manifest",
        "/aggregate-summary.json": "aggregate-summary",
    }
    return next(
        (
            kind
            for suffix, kind in suffixes.items()
            if artifact_ref.endswith(suffix)
        ),
        "unknown",
    )


def _cmd_materialize_ci_validation_execution_batches(
    args: argparse.Namespace,
) -> int:
    plan = _read_json(Path(args.plan))
    request = _read_json(Path(args.request))
    changed_files_snapshot = _read_optional_json(args.changed_files_snapshot)
    fact_snapshot = _read_optional_json(args.fact_snapshot)
    materialization = materialize_ci_validation_execution_batches(
        plan=plan,
        request=request,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        created_at=args.created_at or _utc_now(),
        execution_workflow=args.workflow,
        execution_job=args.execution_job,
        non_batch_control_plane_job_count=(
            args.non_batch_control_plane_job_count
        ),
        expected_run_id=str(args.expected_run_id),
        expected_run_attempt=str(args.expected_run_attempt),
    )
    manifest = cast("Mapping[str, object]", materialization.manifest)
    _write_json(Path(args.execution_batch_manifest_out), manifest)
    run_id = str(args.expected_run_id)
    run_attempt = str(args.expected_run_attempt)
    artifact_ref = ci_validation_execution_batch_manifest_artifact_ref(
        run_id=run_id,
        run_attempt=run_attempt,
    )
    _write_outputs(
        args.github_output,
        {
            "execution_batch_manifest_ref": artifact_ref,
            "execution_batch_manifest_name": artifact_physical_name(
                artifact_ref
            ),
            "execution_batch_manifest_payload_digest": (
                ci_validation_execution_batch_manifest_payload_digest(manifest)
            ),
            "execution_batch_matrix": json.dumps(
                materialization.matrix,
                separators=(",", ":"),
            ),
            "has_execution_batches": _bool_str(
                bool(
                    cast(
                        "Sequence[object]",
                        cast(
                            "Mapping[str, object]",
                            materialization.matrix,
                        ).get("include", []),
                    )
                )
            ),
            **_ci_execution_batch_runner_family_outputs(
                manifest, materialization.matrix
            ),
        },
    )
    return 0


def _ci_execution_batch_runner_family_outputs(
    execution_batch_manifest: Mapping[str, object],
    execution_batch_matrix: Mapping[str, object],
) -> dict[str, str]:
    rows_by_family = _ci_execution_batch_runner_family_rows(
        execution_batch_manifest,
        execution_batch_matrix,
    )
    outputs: dict[str, str] = {}
    for runner_family in ("ubuntu", "windows", "macos"):
        rows = rows_by_family.get(runner_family, [])
        output_prefix = runner_family.replace("-", "_")
        outputs[f"has_{output_prefix}_execution_batches"] = _bool_str(
            bool(rows)
        )
        outputs[f"{output_prefix}_execution_batch_matrix"] = json.dumps(
            {"include": rows},
            separators=(",", ":"),
        )
    return outputs


def _ci_execution_batch_runner_family_rows(
    execution_batch_manifest: Mapping[str, object],
    execution_batch_matrix: Mapping[str, object],
) -> dict[str, list[Json]]:
    rows = execution_batch_matrix.get("include")
    if not isinstance(rows, Sequence) or isinstance(rows, str | bytes):
        rows = []
    row_by_batch_id = {
        str(cast("Mapping[str, object]", row)["batch-id"]): row
        for row in rows
        if isinstance(row, Mapping) and isinstance(row.get("batch-id"), str)
    }
    rows_by_family: dict[str, list[Json]] = {}
    for batch in _ci_execution_batches_in_dependency_order(
        execution_batch_manifest
    ):
        batch_id = str(batch["batch-id"])
        row = row_by_batch_id.get(batch_id)
        if row is not None:
            compatibility = batch.get("compatibility-profile")
            augmented = dict(cast("Mapping[str, object]", row))
            augmented["expected-dependency-bundles"] = (
                _ci_execution_batch_expected_dependency_bundles(
                    execution_batch_manifest,
                    batch,
                )
            )
            if isinstance(compatibility, Mapping):
                augmented["ecosystem"] = compatibility.get("ecosystem")
                augmented["setup-profile"] = compatibility.get("setup-profile")
                augmented["execution-profile"] = compatibility.get(
                    "execution-profile"
                )
            runner_family = str(batch["runner-family"])
            rows_by_family.setdefault(runner_family, []).append(augmented)
    return rows_by_family


def _ci_execution_batch_expected_dependency_bundles(
    execution_batch_manifest: Mapping[str, object],
    batch: Mapping[str, object],
) -> list[Json]:
    batches_by_id = {
        str(item["batch-id"]): item
        for item in _ci_execution_batches(execution_batch_manifest)
    }
    dependency_batch_ids = _ci_execution_batch_transitive_dependencies(
        str(batch["batch-id"]),
        batches_by_id,
    )
    bindings: list[Json] = []
    for dependency_batch_id in dependency_batch_ids:
        dependency_batch = batches_by_id[dependency_batch_id]
        artifact_ref = str(
            dependency_batch["expected-batch-evidence-bundle-ref"]
        )
        artifact_name = artifact_physical_name(artifact_ref)
        bindings.append(
            {
                "batch-id": dependency_batch_id,
                "artifact-ref": artifact_ref,
                "artifact-name": artifact_name,
                "artifact-path": (
                    f".three-ci-validation/observed-artifacts/{artifact_name}"
                ),
                "artifact-metadata-path": (
                    ".three-ci-validation/observed-artifacts/"
                    f"{artifact_name}/artifact-metadata.json"
                ),
            }
        )
    return bindings


def _ci_execution_batch_transitive_dependencies(
    batch_id: str,
    batches_by_id: Mapping[str, Mapping[str, object]],
) -> list[str]:
    ordered: list[str] = []
    seen: set[str] = set()
    visiting: set[str] = set()

    def visit(dependency_batch_id: str) -> None:
        if dependency_batch_id in seen:
            return
        if dependency_batch_id in visiting:
            msg = (
                "execution batch dependency cycle includes "
                f"{dependency_batch_id!r}"
            )
            raise RuntimeError(msg)
        dependency_batch = batches_by_id.get(dependency_batch_id)
        if dependency_batch is None:
            msg = f"unknown execution batch dependency {dependency_batch_id!r}"
            raise RuntimeError(msg)
        visiting.add(dependency_batch_id)
        for transitive_id in cast(
            "Sequence[object]",
            dependency_batch.get("depends-on-batches", []),
        ):
            visit(str(transitive_id))
        visiting.remove(dependency_batch_id)
        seen.add(dependency_batch_id)
        ordered.append(dependency_batch_id)

    current_batch = batches_by_id.get(batch_id)
    if current_batch is None:
        msg = f"unknown execution batch {batch_id!r}"
        raise RuntimeError(msg)
    for dependency_id in cast(
        "Sequence[object]",
        current_batch.get("depends-on-batches", []),
    ):
        visit(str(dependency_id))
    return ordered


def _ci_execution_batch_dependency_layers(
    execution_batch_manifest: Mapping[str, object],
) -> dict[str, int]:
    batches = _ci_execution_batches(execution_batch_manifest)
    batch_ids = [str(batch["batch-id"]) for batch in batches]
    dependencies = {
        str(batch["batch-id"]): [
            str(item)
            for item in cast(
                "Sequence[object]", batch.get("depends-on-batches", [])
            )
        ]
        for batch in batches
    }
    layers: dict[str, int] = {}
    visiting: set[str] = set()

    def visit(batch_id: str) -> int:
        if batch_id in layers:
            return layers[batch_id]
        if batch_id in visiting:
            msg = f"execution batch dependency cycle includes {batch_id!r}"
            raise RuntimeError(msg)
        if batch_id not in dependencies:
            msg = f"unknown execution batch dependency {batch_id!r}"
            raise RuntimeError(msg)
        visiting.add(batch_id)
        layer = 0
        for dependency_id in dependencies[batch_id]:
            layer = max(layer, visit(dependency_id) + 1)
        visiting.remove(batch_id)
        layers[batch_id] = layer
        return layer

    for batch_id in batch_ids:
        visit(batch_id)
    return layers


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
    outcome: _CiValidationOutcome = "success"
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


def _cmd_run_ci_validation_batch_commands(args: argparse.Namespace) -> int:
    plan = _read_json(Path(args.plan))
    execution_batch_manifest = _read_json(Path(args.execution_batch_manifest))
    request = _read_optional_json(getattr(args, "request", ""))
    changed_files_snapshot = _read_optional_json(args.changed_files_snapshot)
    fact_snapshot = _read_optional_json(args.fact_snapshot)
    matrix_row = _read_json_value(args.matrix_row_json)
    if not isinstance(matrix_row, Mapping):
        msg = "execution-batch matrix row must be a JSON object"
        raise TypeError(msg)
    batch = _ci_execution_batch_from_matrix_row(
        execution_batch_manifest,
        matrix_row,
    )
    layer_by_work_group = _ci_work_group_dependency_layers(plan)
    result_dir = Path(args.result_out_dir)
    result_dir.mkdir(parents=True, exist_ok=True)
    result_paths: list[str] = []
    outcome: _CiValidationOutcome = "success"
    dependency_bundle_paths = getattr(args, "dependency_bundle", [])
    if dependency_bundle_paths and request is None:
        msg = "--request is required when dependency bundles are supplied"
        raise RuntimeError(msg)
    authoritative_dependency_bundles = (
        _ci_authoritative_dependency_bundles(
            dependency_bundle_paths,
            plan=plan,
            request=cast("Mapping[str, object]", request),
            execution_batch_manifest=execution_batch_manifest,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            observed_artifacts_dir=getattr(args, "observed_artifacts_dir", ""),
            expected_run_id=getattr(args, "expected_run_id", "")
            or str(
                cast("Mapping[str, object]", execution_batch_manifest["run"])[
                    "run-id"
                ]
            ),
            expected_run_attempt=getattr(
                args,
                "expected_run_attempt",
                "",
            )
            or str(
                cast("Mapping[str, object]", execution_batch_manifest["run"])[
                    "run-attempt"
                ]
            ),
            dependency_artifact_admissions=(
                _ci_internal_dependency_artifact_admissions(args)
            ),
        )
        if dependency_bundle_paths
        else []
    )
    authoritative_dependency_results = _ci_authoritative_dependency_results(
        authoritative_dependency_bundles,
    )
    prior_selector_outcomes: dict[str, str] = {}
    for index, selector in enumerate(_ci_batch_ordered_selectors(batch)):
        work_group_id = str(selector["work-group-id"])
        group = _ci_work_group_by_id(plan, work_group_id)
        matrix_work_group = _ci_work_group_matrix_entry(
            plan,
            group,
            dependency_layer=layer_by_work_group[work_group_id],
            writer_job=str(matrix_row["expected-job-identity"]),
        )
        result_path = result_dir / f"validation-result-{index:03d}.json"
        normalized_dependencies = _ci_batch_normalized_dependency_results(
            selector=selector,
            execution_batch_manifest=execution_batch_manifest,
            current_batch_id=str(batch["batch-id"]),
            dependency_results=[],
            authoritative_dependency_results=authoritative_dependency_results,
            prior_selector_outcomes=prior_selector_outcomes,
        )
        dependency_blocked = any(
            item["admitted-for-gating"] is not True
            for item in normalized_dependencies
        )
        if dependency_blocked:
            result = _ci_dependency_blocked_validation_result(
                matrix_work_group,
                observed_commit_sha=args.observed_commit_sha,
            )
            _write_json(result_path, result)
            outcome = "blocking-failure"
            prior_selector_outcomes[work_group_id] = "skipped"
            result_paths.append(str(result_path))
            continue
        command_args = argparse.Namespace(
            plan=args.plan,
            changed_files_snapshot=args.changed_files_snapshot,
            fact_snapshot=args.fact_snapshot,
            assignments="",
            observed_artifacts_dir=args.observed_artifacts_dir,
            observed_commit_sha=args.observed_commit_sha,
            matrix_work_group_json=json.dumps(
                matrix_work_group,
                separators=(",", ":"),
            ),
            repo_root=args.repo_root,
            result_out=str(result_path),
            github_output=None,
        )
        _cmd_run_ci_validation_commands(command_args)
        result = _read_json(result_path)
        if result.get("outcome") != "success":
            outcome = "blocking-failure"
        prior_selector_outcomes[work_group_id] = str(result.get("outcome"))
        result_paths.append(str(result_path))
    _write_outputs(
        args.github_output,
        {
            "validation_outcome": outcome,
            "validation_result_count": str(len(result_paths)),
            "validation_result_paths": json.dumps(
                result_paths,
                separators=(",", ":"),
            ),
        },
    )
    return 0


def _ci_dependency_blocked_validation_result(
    matrix_work_group: Mapping[str, object],
    *,
    observed_commit_sha: str,
) -> Json:
    return {
        "work-group-id": matrix_work_group.get("work-group-id"),
        "kind": matrix_work_group.get("kind"),
        "runner-family": matrix_work_group.get("runner-family"),
        "coverage-target": matrix_work_group.get("coverage-target"),
        "observed-commit-sha": observed_commit_sha or None,
        "outcome": "skipped",
        "commands": [
            {
                "index": 0,
                "label": "dependency-gate",
                "argv": [],
                "capability": None,
                "exit-code": None,
                "outcome": "skipped",
                "error": "dependency-blocked",
            }
        ],
    }


def _ci_work_group_by_id(
    plan: Mapping[str, object],
    work_group_id: str,
) -> Mapping[str, Any]:
    for group in _ci_executable_work_groups(plan):
        if group.get("work-group-id") == work_group_id:
            return group
    msg = f"unknown CI validation work group {work_group_id!r}"
    raise RuntimeError(msg)


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
        observed_artifacts_dir=observed_artifacts_dir,
        expected_run_id=args.expected_run_id,
        expected_run_attempt=args.expected_run_attempt,
        dependency_artifact_admissions=(
            _ci_internal_dependency_artifact_admissions(args)
        ),
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
        orchestrator_slot_index=getattr(args, "orchestrator_slot_index", None),
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


def _cmd_run_ci_validation_runner_family_orchestrator_step(
    args: argparse.Namespace,
) -> int:
    execution_batch_manifest = _read_json(Path(args.execution_batch_manifest))
    state_dir = Path(args.state_dir)
    observed_root = Path(args.observed_artifacts_dir)
    work_dir = Path(args.work_dir) / f"slot-{int(args.slot_index):02d}"
    state_dir.mkdir(parents=True, exist_ok=True)
    observed_root.mkdir(parents=True, exist_ok=True)
    work_dir.mkdir(parents=True, exist_ok=True)
    family = str(args.runner_family)
    batches_by_id = {
        str(batch["batch-id"]): batch
        for batch in _ci_execution_batches(execution_batch_manifest)
    }
    family_batches = [
        batch
        for batch in _ci_execution_batches_in_dependency_order(
            execution_batch_manifest
        )
        if str(batch.get("runner-family")) == family
    ]
    dependency_admissions: dict[str, Json] = {}
    ready, waiting = _ci_orchestrator_select_ready_batch(
        family_batches=family_batches,
        batches_by_id=batches_by_id,
        repository=str(args.repository),
        run_id=str(args.expected_run_id),
        run_attempt=str(args.expected_run_attempt),
        state_dir=state_dir,
        observed_root=observed_root,
        dependency_admissions=dependency_admissions,
    )
    if ready is None:
        if waiting:
            msg = (
                "runner-family orchestrator could not select a dependency-ready "
                "batch; waiting batch ids: "
                f"{', '.join(sorted(waiting))}"
            )
            raise RuntimeError(msg)
        _write_outputs(
            args.github_output,
            {
                "batch_selected": "false",
                "orchestrator_complete": "true",
                "waiting_batch_ids": json.dumps(
                    [],
                    separators=(",", ":"),
                ),
            },
        )
        return 0

    batch = ready
    batch_id = str(batch["batch-id"])
    artifact_ref = str(batch["expected-batch-evidence-bundle-ref"])
    artifact_name = artifact_physical_name(artifact_ref)
    upload_dir = work_dir / "upload"
    result_dir = work_dir / "validation-results"
    bundle_path = observed_root / artifact_name / "batch-evidence-bundle.json"
    upload_dir.mkdir(parents=True, exist_ok=True)
    result_dir.mkdir(parents=True, exist_ok=True)
    dependency_bundle_paths = _ci_orchestrator_dependency_bundle_paths(
        batch,
        batches_by_id=batches_by_id,
        observed_root=observed_root,
    )
    matrix_row = _ci_orchestrator_matrix_row(
        execution_batch_manifest,
        batch,
    )
    common = {
        "plan": args.plan,
        "request": args.request,
        "execution_batch_manifest": args.execution_batch_manifest,
        "changed_files_snapshot": args.changed_files_snapshot,
        "fact_snapshot": args.fact_snapshot,
        "observed_artifacts_dir": args.observed_artifacts_dir,
        "expected_run_id": args.expected_run_id,
        "expected_run_attempt": args.expected_run_attempt,
        "dependency_bundle": dependency_bundle_paths,
        "_dependency_artifact_admissions": list(dependency_admissions.values()),
        "observed_commit_sha": args.observed_commit_sha,
        "matrix_row_json": json.dumps(matrix_row, separators=(",", ":")),
        "repo_root": args.repo_root,
        "github_output": None,
        "orchestrator_slot_index": str(args.slot_index),
    }
    _cmd_run_ci_validation_batch_commands(
        argparse.Namespace(
            **common,
            result_out_dir=str(result_dir),
        )
    )
    validation_result_paths = sorted(
        str(path) for path in result_dir.glob("*.json")
    )
    _cmd_write_ci_validation_batch_evidence_bundle(
        argparse.Namespace(
            **common,
            workflow=args.workflow,
            job=args.job,
            validation_result=validation_result_paths,
            dependency_results_json="",
            started_at=None,
            completed_at=None,
            created_at=None,
            bundle_out=str(bundle_path),
        )
    )
    shutil.copy2(bundle_path, upload_dir / "batch-evidence-bundle.json")
    for result_path in validation_result_paths:
        shutil.copy2(result_path, upload_dir / Path(result_path).name)
    _write_json(
        _ci_orchestrator_ran_state_path(state_dir, batch_id),
        {
            "batch-id": batch_id,
            "artifact-name": artifact_name,
            "artifact-ref": artifact_ref,
            "upload-path": str(upload_dir),
        },
    )
    _write_outputs(
        args.github_output,
        {
            "batch_selected": "true",
            "batch_id": batch_id,
            "batch_evidence_bundle_artifact_name": artifact_name,
            "batch_evidence_upload_path": str(upload_dir),
            "orchestrator_complete": "false",
        },
    )
    return 0


def _cmd_record_ci_validation_runner_family_orchestrator_upload(
    args: argparse.Namespace,
) -> int:
    manifest = _read_json(Path(args.execution_batch_manifest))
    batches_by_id = {
        str(batch["batch-id"]): batch
        for batch in _ci_execution_batches(manifest)
    }
    batch = batches_by_id.get(str(args.batch_id))
    if batch is None:
        msg = f"unknown execution batch {args.batch_id!r}"
        raise RuntimeError(msg)
    artifact_ref = str(batch["expected-batch-evidence-bundle-ref"])
    expected_name = artifact_physical_name(artifact_ref)
    if args.artifact_name != expected_name:
        msg = "uploaded artifact name does not match expected batch bundle name"
        raise RuntimeError(msg)
    artifact_dir = Path(args.observed_artifacts_dir) / expected_name
    artifact_dir.mkdir(parents=True, exist_ok=True)
    _write_json(
        artifact_dir / "artifact-metadata.json",
        {
            "artifact-instance-id": str(args.artifact_id),
            "artifact-ref": artifact_ref,
            "physical-artifact-name": expected_name,
            "run-id": str(args.expected_run_id),
            "run-attempt": str(args.expected_run_attempt),
            "producer-boundary": "execution-batch",
            "admission-source": _CI_ORCHESTRATOR_STATE_ADMISSION_SOURCE,
        },
    )
    uploaded_state = {
        "batch-id": str(args.batch_id),
        "artifact-name": expected_name,
        "artifact-ref": artifact_ref,
        "artifact-instance-id": str(args.artifact_id),
        "run-id": str(args.expected_run_id),
        "run-attempt": str(args.expected_run_attempt),
        "producer-boundary": "execution-batch",
        "admission-source": _CI_ORCHESTRATOR_STATE_ADMISSION_SOURCE,
    }
    slot_index = getattr(args, "orchestrator_slot_index", None)
    if slot_index is not None and str(slot_index) != "":
        uploaded_state["orchestrator-slot-index"] = str(slot_index)
    state_dir = Path(args.state_dir)
    _write_json(
        _ci_orchestrator_uploaded_state_path(
            state_dir,
            str(args.batch_id),
        ),
        uploaded_state,
    )
    _write_outputs(
        args.github_output,
        {
            "recorded_batch_id": str(args.batch_id),
            "recorded_artifact_id": str(args.artifact_id),
        },
    )
    return 0


def _ci_orchestrator_state_key(batch_id: str) -> str:
    return re.sub(r"[^A-Za-z0-9_.-]", "_", batch_id)


def _ci_orchestrator_ran_state_path(state_dir: Path, batch_id: str) -> Path:
    return state_dir / "ran" / f"{_ci_orchestrator_state_key(batch_id)}.json"


def _ci_orchestrator_uploaded_state_path(
    state_dir: Path, batch_id: str
) -> Path:
    return (
        state_dir / "uploaded" / f"{_ci_orchestrator_state_key(batch_id)}.json"
    )


def _ci_orchestrator_matrix_row(
    execution_batch_manifest: Mapping[str, object],
    batch: Mapping[str, object],
) -> Json:
    compatibility = batch.get("compatibility-profile")
    row = _ci_execution_batch_matrix_identity(batch)
    row["identity-matrix"] = dict(row)
    row["expected-job-identity"] = _ci_batch_expected_writer_id(
        execution_batch_manifest,
        batch,
    )
    row["expected-dependency-bundles"] = (
        _ci_execution_batch_expected_dependency_bundles(
            execution_batch_manifest,
            batch,
        )
    )
    if isinstance(compatibility, Mapping):
        row["ecosystem"] = compatibility.get("ecosystem")
        row["setup-profile"] = compatibility.get("setup-profile")
        row["execution-profile"] = compatibility.get("execution-profile")
    return row


def _ci_orchestrator_select_ready_batch(
    family_batches: Sequence[Mapping[str, object]],
    *,
    batches_by_id: Mapping[str, Mapping[str, object]],
    repository: str,
    run_id: str,
    run_attempt: str,
    state_dir: Path,
    observed_root: Path,
    dependency_admissions: dict[str, Json],
) -> tuple[Mapping[str, object] | None, list[str]]:
    waiting: list[str] = []
    for batch in family_batches:
        batch_id = str(batch["batch-id"])
        if _ci_orchestrator_uploaded_state_path(state_dir, batch_id).exists():
            continue
        if _ci_orchestrator_ran_state_path(state_dir, batch_id).exists():
            waiting.append(batch_id)
            continue
        if _ci_orchestrator_dependencies_ready(
            batch,
            batches_by_id=batches_by_id,
            repository=repository,
            run_id=run_id,
            run_attempt=run_attempt,
            state_dir=state_dir,
            observed_root=observed_root,
            dependency_admissions=dependency_admissions,
        ):
            return batch, waiting
        waiting.append(batch_id)
    return None, waiting


def _ci_orchestrator_dependencies_ready(
    batch: Mapping[str, object],
    *,
    batches_by_id: Mapping[str, Mapping[str, object]],
    repository: str,
    run_id: str,
    run_attempt: str,
    state_dir: Path,
    observed_root: Path,
    dependency_admissions: dict[str, Json] | None = None,
) -> bool:
    current_family = str(batch["runner-family"])
    for dependency_id in _ci_execution_batch_transitive_dependencies(
        str(batch["batch-id"]),
        batches_by_id,
    ):
        dependency = batches_by_id[dependency_id]
        if str(dependency["runner-family"]) == current_family:
            admission = _ci_orchestrator_same_family_dependency_admission(
                dependency,
                repository=repository,
                run_id=run_id,
                run_attempt=run_attempt,
                state_dir=state_dir,
                observed_root=observed_root,
            )
            if admission is None:
                return False
            if dependency_admissions is not None:
                dependency_admissions[
                    str(admission["physical-artifact-name"])
                ] = admission
            continue
        admission = _ci_orchestrator_cross_family_dependency_admission(
            dependency,
            repository=repository,
            run_id=run_id,
            run_attempt=run_attempt,
            observed_root=observed_root,
        )
        if admission is None:
            return False
        if dependency_admissions is not None:
            dependency_admissions[str(admission["physical-artifact-name"])] = (
                admission
            )
    return True


def _ci_orchestrator_same_family_dependency_admission(
    dependency: Mapping[str, object],
    *,
    repository: str,
    run_id: str,
    run_attempt: str,
    state_dir: Path,
    observed_root: Path,
) -> Json | None:
    if not repository:
        return None
    dependency_id = str(dependency["batch-id"])
    uploaded_path = _ci_orchestrator_uploaded_state_path(
        state_dir,
        dependency_id,
    )
    if not uploaded_path.exists():
        return None
    try:
        uploaded = _read_json(uploaded_path)
    except (OSError, TypeError, ValueError, json.JSONDecodeError):
        return None
    return _ci_orchestrator_download_recorded_dependency(
        dependency,
        recorded_upload=uploaded,
        repository=repository,
        run_id=run_id,
        run_attempt=run_attempt,
        observed_root=observed_root,
    )


def _ci_orchestrator_cross_family_dependency_admission(
    dependency: Mapping[str, object],
    *,
    repository: str,
    run_id: str,
    run_attempt: str,
    observed_root: Path,
) -> Json | None:
    if not repository:
        return None
    artifact_ref = str(dependency["expected-batch-evidence-bundle-ref"])
    artifact_name_value = artifact_physical_name(artifact_ref)
    try:
        artifact_api_by_name = _ci_observed_artifact_api_multimap(
            repository=repository,
            run_id=run_id,
            run_attempt=run_attempt,
            prefixed_artifact_cap=_CI_VALIDATION_LIVE_NAMESPACE_ARTIFACT_CAP,
        )
    except (RuntimeError, TypeError, ValueError):
        return None
    matches = [
        artifact_api
        for artifact_api in artifact_api_by_name.get(artifact_name_value, [])
        if _ci_live_artifact_matches_expected(
            artifact_api,
            artifact_id=str(artifact_api.get("id", "")),
            artifact_name_value=artifact_name_value,
            run_id=run_id,
            run_attempt=run_attempt,
        )
    ]
    if len(matches) != 1:
        return None
    return _ci_orchestrator_download_admitted_dependency(
        dependency,
        repository=repository,
        run_id=run_id,
        run_attempt=run_attempt,
        observed_root=observed_root,
        artifact_api=matches[0],
        source=_CI_ORCHESTRATOR_LIVE_CROSS_FAMILY_ADMISSION_SOURCE,
    )


def _ci_orchestrator_download_recorded_dependency(
    dependency: Mapping[str, object],
    *,
    recorded_upload: Mapping[str, object],
    repository: str,
    run_id: str,
    run_attempt: str,
    observed_root: Path,
) -> Json | None:
    artifact_ref = str(dependency["expected-batch-evidence-bundle-ref"])
    artifact_name_value = artifact_physical_name(artifact_ref)
    artifact_instance_id = recorded_upload.get("artifact-instance-id")
    if not _ci_orchestrator_recorded_upload_matches_dependency(
        dependency,
        recorded_upload,
        run_id=run_id,
        run_attempt=run_attempt,
    ):
        return None
    artifact_instance_id = cast("str", artifact_instance_id)
    artifact_api = _ci_live_artifact_api_instance_by_id(
        repository=repository,
        artifact_id=artifact_instance_id,
    )
    if not _ci_live_artifact_matches_expected(
        artifact_api,
        artifact_id=artifact_instance_id,
        artifact_name_value=artifact_name_value,
        run_id=run_id,
        run_attempt=run_attempt,
    ):
        return None
    return _ci_orchestrator_download_admitted_dependency(
        dependency,
        repository=repository,
        run_id=run_id,
        run_attempt=run_attempt,
        observed_root=observed_root,
        artifact_api=cast("Mapping[str, object]", artifact_api),
        source="orchestrator-artifact-id-state",
    )


def _ci_orchestrator_recorded_upload_matches_dependency(
    dependency: Mapping[str, object],
    recorded_upload: Mapping[str, object],
    *,
    run_id: str,
    run_attempt: str,
) -> bool:
    artifact_ref = str(dependency["expected-batch-evidence-bundle-ref"])
    artifact_name_value = artifact_physical_name(artifact_ref)
    artifact_instance_id = recorded_upload.get("artifact-instance-id")
    expected = {
        "artifact-name": artifact_name_value,
        "artifact-ref": artifact_ref,
        "run-id": str(run_id),
        "run-attempt": str(run_attempt),
        "producer-boundary": "execution-batch",
        "admission-source": _CI_ORCHESTRATOR_STATE_ADMISSION_SOURCE,
    }
    return (
        all(
            recorded_upload.get(key) == value for key, value in expected.items()
        )
        and isinstance(artifact_instance_id, str)
        and artifact_instance_id != ""
    )


def _ci_orchestrator_dependency_bundle_paths(
    batch: Mapping[str, object],
    *,
    batches_by_id: Mapping[str, Mapping[str, object]],
    observed_root: Path,
) -> list[str]:
    paths: list[str] = []
    for dependency_id in _ci_execution_batch_transitive_dependencies(
        str(batch["batch-id"]),
        batches_by_id,
    ):
        dependency = batches_by_id[dependency_id]
        artifact_ref = str(dependency["expected-batch-evidence-bundle-ref"])
        paths.append(
            str(
                observed_root
                / artifact_physical_name(artifact_ref)
                / "batch-evidence-bundle.json"
            )
        )
    return paths


def _ci_orchestrator_download_admitted_dependency(
    dependency: Mapping[str, object],
    *,
    repository: str,
    run_id: str,
    run_attempt: str,
    observed_root: Path,
    artifact_api: Mapping[str, object],
    source: str,
) -> Json | None:
    artifact_ref = str(dependency["expected-batch-evidence-bundle-ref"])
    artifact_name_value = artifact_physical_name(artifact_ref)
    destination = observed_root / artifact_name_value
    admission = _ci_dependency_artifact_admission(
        artifact_ref=artifact_ref,
        artifact_name_value=artifact_name_value,
        artifact_api=artifact_api,
        run_id=run_id,
        run_attempt=run_attempt,
        source=source,
    )
    try:
        if destination.exists():
            shutil.rmtree(destination)
        _download_artifact_by_id(
            repository,
            artifact_api,
            artifact_name_value,
            destination,
        )
        _materialize_ci_observed_artifact_metadata(
            destination,
            artifact_name_value=artifact_name_value,
            artifact_api=artifact_api,
            run_id=run_id,
            expected_artifact_ref=artifact_ref,
            expected_run_attempt=run_attempt,
            admission_source=source,
        )
    except (OSError, RuntimeError, TypeError, ValueError):
        return None
    if not _ci_dependency_artifact_metadata_matches_admission(
        destination / "artifact-metadata.json",
        admission,
    ):
        return None
    return admission


def _ci_dependency_artifact_admission(
    *,
    artifact_ref: str,
    artifact_name_value: str,
    artifact_api: Mapping[str, object],
    run_id: str,
    run_attempt: str,
    source: str,
) -> Json:
    artifact_id = artifact_api.get("id")
    if artifact_id is None or not str(artifact_id):
        msg = f"artifact {artifact_name_value} live admission is missing id"
        raise RuntimeError(msg)
    return {
        "artifact-ref": artifact_ref,
        "physical-artifact-name": artifact_name_value,
        "artifact-instance-id": str(artifact_id),
        "run-id": str(run_id),
        "run-attempt": str(run_attempt),
        "producer-boundary": "execution-batch",
        "admission-source": source,
    }


def _ci_dependency_artifact_metadata_matches_admission(
    metadata_path: Path,
    admission: Mapping[str, object],
) -> bool:
    try:
        metadata = _read_json(metadata_path)
    except (OSError, TypeError, ValueError, json.JSONDecodeError):
        return False
    if not isinstance(metadata, Mapping):
        return False
    for key in (
        "artifact-ref",
        "physical-artifact-name",
        "artifact-instance-id",
        "run-id",
        "run-attempt",
        "producer-boundary",
        "admission-source",
    ):
        if metadata.get(key) != admission.get(key):
            return False
    return True


def _ci_live_artifact_api_instance_by_id(
    *,
    repository: str,
    artifact_id: str,
) -> Mapping[str, object] | None:
    endpoint = f"repos/{repository}/actions/artifacts/{artifact_id}"
    try:
        payload = _gh_api(repository, endpoint)
    except (RuntimeError, TypeError, ValueError):
        return None
    return payload if isinstance(payload, Mapping) else None


def _ci_live_artifact_matches_expected(
    artifact_api: Mapping[str, object] | None,
    *,
    artifact_id: str,
    artifact_name_value: str,
    run_id: str,
    run_attempt: str,
    require_run_attempt: bool = False,
) -> bool:
    if not artifact_name_value.startswith(
        f"three-ci-validation-{run_id}-{run_attempt}-"
    ):
        return False
    if artifact_api is None or artifact_api.get("expired") is True:
        return False
    identity_matches = (
        str(artifact_api.get("id", "")) == artifact_id
        and artifact_api.get("name") == artifact_name_value
    )
    observed_run_ids, observed_run_attempts = (
        _ci_live_artifact_run_observations(
            artifact_api,
        )
    )
    if not observed_run_ids or any(
        value != run_id for value in observed_run_ids
    ):
        return False
    if not observed_run_attempts and require_run_attempt:
        return False
    if any(value != run_attempt for value in observed_run_attempts):
        return False
    return identity_matches


def _ci_live_artifact_run_observations(
    artifact_api: Mapping[str, object],
) -> tuple[list[str], list[str]]:
    observed_run_ids: list[str] = []
    observed_run_attempts: list[str] = []
    workflow_run = artifact_api.get("workflow_run")
    if isinstance(workflow_run, Mapping):
        live_run_id = workflow_run.get("id")
        if live_run_id is not None:
            observed_run_ids.append(str(live_run_id))
        live_attempt = workflow_run.get("run_attempt")
        if live_attempt is not None:
            observed_run_attempts.append(str(live_attempt))
    for key in ("run-id", "run_id"):
        live_run_id = artifact_api.get(key)
        if live_run_id is not None:
            observed_run_ids.append(str(live_run_id))
    for key in ("run-attempt", "run_attempt"):
        live_attempt = artifact_api.get(key)
        if live_attempt is not None:
            observed_run_attempts.append(str(live_attempt))
    return observed_run_ids, observed_run_attempts


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
                _ci_missing_cross_batch_dependency_result(
                    work_group_id,
                    source_batch_id,
                    rows_by_work_group,
                )
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
        normalized_row = {
            "work-group-id": work_group_id,
            "source-batch-id": source_batch_id,
            "outcome": outcome,
            "admitted-for-gating": admitted,
        }
        normalized_row.update(_ci_dependency_identity_fields(row))
        normalized.append(normalized_row)
    extra = set(rows_by_work_group) - upstream_dependency_ids
    if extra:
        msg = f"unexpected dependency results for {sorted(extra)!r}"
        raise RuntimeError(msg)
    return normalized


def _ci_missing_cross_batch_dependency_result(
    work_group_id: str,
    source_batch_id: str,
    rows_by_work_group: Mapping[str, Mapping[str, object]],
) -> Json:
    supplied_row = rows_by_work_group.get(work_group_id)
    if supplied_row is not None and not _ci_dependency_result_unresolved(
        supplied_row,
        source_batch_id=source_batch_id,
    ):
        raise ContractValidationError(
            [
                ValidationIssue(
                    "$.selector-results[].dependency-results[]",
                    "requires authoritative upstream bundle evidence",
                )
            ]
        )
    return {
        "work-group-id": work_group_id,
        "source-batch-id": source_batch_id,
        "outcome": "missing",
        "admitted-for-gating": False,
    }


def _ci_dependency_result_unresolved(
    row: Mapping[str, object],
    *,
    source_batch_id: str,
) -> bool:
    if row.get("source-batch-id") != source_batch_id:
        return False
    if row.get("outcome") not in {"missing", "skipped"}:
        return False
    if row.get("admitted-for-gating") is not False:
        return False
    return not any(
        row.get(key) is not None
        for key in (
            "upstream-artifact-ref",
            "upstream-bundle-id",
            "upstream-artifact-instance-id",
            "upstream-admitted-candidate-id",
        )
    )


def _ci_dependency_identity_fields(row: Mapping[str, object]) -> Json:
    result: Json = {}
    for key in (
        "upstream-artifact-ref",
        "upstream-bundle-id",
        "upstream-artifact-instance-id",
        "upstream-admitted-candidate-id",
    ):
        value = row.get(key)
        if value is not None:
            result[key] = value
    return result


def _ci_authoritative_dependency_bundles(
    paths: Sequence[str],
    *,
    plan: Mapping[str, object],
    request: Mapping[str, object],
    execution_batch_manifest: Mapping[str, object],
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    observed_artifacts_dir: str,
    expected_run_id: str,
    expected_run_attempt: str,
    dependency_artifact_admissions: Sequence[Mapping[str, object]] = (),
) -> list[Mapping[str, object]]:
    expected_refs = _ci_expected_batch_bundle_refs(execution_batch_manifest)
    admissions_by_name = _ci_dependency_artifact_admissions_by_name(
        dependency_artifact_admissions,
    )
    pending: list[tuple[str, Mapping[str, object]]] = []
    for value in paths:
        expected_artifact_ref = (
            _ci_dependency_bundle_path_expected_artifact_ref(
                value,
                expected_refs=expected_refs,
                observed_artifacts_dir=observed_artifacts_dir,
            )
        )
        if expected_artifact_ref is None:
            continue
        try:
            bundle = _read_json(Path(value))
        except (OSError, ValueError, TypeError, RuntimeError) as exc:
            msg = f"invalid dependency bundle {value!r}: {exc}"
            raise RuntimeError(msg) from exc
        _ci_verify_dependency_bundle_path_identity(
            value,
            bundle,
            expected_artifact_ref=expected_artifact_ref,
        )
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
            bundles.append(
                _ci_trusted_dependency_bundle(
                    value,
                    bundle,
                    expected_run_id=expected_run_id,
                    expected_run_attempt=expected_run_attempt,
                    admission=admissions_by_name.get(
                        artifact_physical_name(str(bundle["artifact-ref"]))
                    ),
                )
            )
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


def _ci_trusted_dependency_bundle(
    path: str,
    bundle: Mapping[str, object],
    *,
    expected_run_id: str,
    expected_run_attempt: str,
    admission: Mapping[str, object] | None = None,
) -> Mapping[str, object]:
    artifact_ref = bundle.get("artifact-ref")
    batch = bundle.get("batch")
    batch_id = batch.get("batch-id") if isinstance(batch, Mapping) else None
    if not isinstance(artifact_ref, str) or not isinstance(batch_id, str):
        msg = "dependency bundle is missing trusted artifact identity inputs"
        raise TypeError(msg)
    physical_name = artifact_physical_name(artifact_ref)
    metadata_path = Path(path).parent / "artifact-metadata.json"
    try:
        metadata = _read_json(metadata_path)
    except (OSError, TypeError, ValueError, json.JSONDecodeError) as exc:
        msg = f"dependency artifact metadata is unavailable for {path!r}"
        raise RuntimeError(msg) from exc
    if not isinstance(metadata, Mapping):
        msg = f"dependency artifact metadata for {path!r} must be an object"
        raise TypeError(msg)
    if admission is None:
        msg = (
            f"dependency artifact metadata for {path!r} has no live artifact "
            "admission record"
        )
        raise RuntimeError(msg)
    artifact_instance_id = metadata.get("artifact-instance-id")
    if not isinstance(artifact_instance_id, str) or not artifact_instance_id:
        msg = (
            f"dependency artifact metadata for {path!r} is missing artifact "
            "instance id"
        )
        raise RuntimeError(msg)
    expected_metadata = {
        "artifact-ref": artifact_ref,
        "physical-artifact-name": physical_name,
        "run-id": expected_run_id,
        "run-attempt": expected_run_attempt,
        "producer-boundary": "execution-batch",
    }
    _ci_verify_dependency_admission_source(path, metadata)
    expected_metadata["admission-source"] = str(metadata["admission-source"])
    for key, expected_value in expected_metadata.items():
        if metadata.get(key) != expected_value:
            msg = f"dependency artifact metadata for {path!r} does not match {key}"
            raise RuntimeError(msg)
    expected_admission = {
        **expected_metadata,
        "artifact-instance-id": artifact_instance_id,
    }
    for key, expected_value in expected_admission.items():
        if admission.get(key) != expected_value:
            msg = f"dependency artifact admission for {path!r} does not match {key}"
            raise RuntimeError(msg)
    candidate_id = ci_validation_batch_evidence_candidate_id(
        run_id=expected_run_id,
        run_attempt=expected_run_attempt,
        batch_id=batch_id,
        artifact_ref=artifact_ref,
        artifact_instance_id=artifact_instance_id,
        physical_artifact_name=physical_name,
    )
    return _TrustedDependencyBundle(
        bundle,
        artifact_instance_id=artifact_instance_id,
        admitted_candidate_id=candidate_id,
    )


def _ci_verify_dependency_admission_source(
    path: str,
    admission: Mapping[str, object],
) -> None:
    admission_source = admission.get("admission-source")
    if admission_source in {
        "github-actions-live-api",
        _CI_ORCHESTRATOR_STATE_ADMISSION_SOURCE,
        _CI_ORCHESTRATOR_LIVE_CROSS_FAMILY_ADMISSION_SOURCE,
    }:
        return
    msg = (
        f"dependency artifact admission for {path!r} has untrusted or missing "
        "source"
    )
    raise RuntimeError(msg)


def _ci_dependency_artifact_admissions_by_name(
    admissions: Sequence[Mapping[str, object]],
) -> dict[str, Mapping[str, object]]:
    by_name: dict[str, Mapping[str, object]] = {}
    for admission in admissions:
        name = admission.get("physical-artifact-name")
        if isinstance(name, str) and name:
            if name in by_name and by_name[name] != admission:
                msg = f"ambiguous dependency artifact admission for {name!r}"
                raise RuntimeError(msg)
            by_name[name] = admission
    return by_name


def _ci_internal_dependency_artifact_admissions(
    args: argparse.Namespace,
) -> list[Mapping[str, object]]:
    admissions = getattr(args, "_dependency_artifact_admissions", ())
    parsed: list[Mapping[str, object]] = []
    for admission in admissions:
        if not isinstance(admission, Mapping):
            msg = "internal dependency artifact admission must be an object"
            raise TypeError(msg)
        parsed.append(admission)
    return parsed


def _ci_dependency_bundle_path_is_expected(
    path: str,
    *,
    expected_refs: set[str],
    observed_artifacts_dir: str,
) -> bool:
    return (
        _ci_dependency_bundle_path_expected_artifact_ref(
            path,
            expected_refs=expected_refs,
            observed_artifacts_dir=observed_artifacts_dir,
        )
        is not None
    )


def _ci_dependency_bundle_path_expected_artifact_ref(
    path: str,
    *,
    expected_refs: set[str],
    observed_artifacts_dir: str,
) -> str | None:
    return next(
        (
            artifact_ref
            for artifact_ref in sorted(expected_refs)
            if _ci_dependency_bundle_path_matches_artifact_ref(
                path,
                artifact_ref=artifact_ref,
                observed_artifacts_dir=observed_artifacts_dir,
            )
        ),
        None,
    )


def _ci_verify_dependency_bundle_path_identity(
    path: str,
    bundle: object,
    *,
    expected_artifact_ref: str,
) -> None:
    if not isinstance(bundle, Mapping):
        return
    artifact_ref = bundle.get("artifact-ref")
    if not isinstance(artifact_ref, str):
        return
    if artifact_ref != expected_artifact_ref:
        msg = (
            f"invalid dependency bundle {path!r}: artifact-ref does not match "
            "expected dependency artifact path"
        )
        raise RuntimeError(msg)


def _ci_dependency_bundle_path_matches_artifact_ref(
    path: str,
    *,
    artifact_ref: str,
    observed_artifacts_dir: str,
) -> bool:
    expected_name = artifact_physical_name(artifact_ref)
    bundle_path = Path(path)
    if bundle_path.name != "batch-evidence-bundle.json":
        return False
    if bundle_path.parent.name != expected_name:
        return False
    if observed_artifacts_dir:
        try:
            relative_path = bundle_path.resolve().relative_to(
                Path(observed_artifacts_dir).resolve()
            )
        except ValueError:
            return False
        return relative_path.parts[:2] == (
            expected_name,
            "batch-evidence-bundle.json",
        )
    return True


def _ci_authoritative_dependency_results(  # noqa: C901
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
            artifact_ref = bundle.get("artifact-ref")
            bundle_id = bundle.get("bundle-id")
            result = {
                "work-group-id": work_group_id,
                "source-batch-id": batch_id,
                "outcome": _ci_same_batch_dependency_outcome(outcome),
                "admitted-for-gating": _ci_selector_outcome_admitted_for_gating(
                    outcome
                ),
            }
            if isinstance(artifact_ref, str):
                result["upstream-artifact-ref"] = artifact_ref
            if isinstance(bundle_id, str):
                result["upstream-bundle-id"] = bundle_id
            artifact_instance_id = getattr(
                bundle,
                "artifact_instance_id",
                None,
            )
            admitted_candidate_id = getattr(
                bundle,
                "admitted_candidate_id",
                None,
            )
            if isinstance(artifact_instance_id, str):
                result["upstream-artifact-instance-id"] = artifact_instance_id
            if isinstance(admitted_candidate_id, str):
                result["upstream-admitted-candidate-id"] = admitted_candidate_id
            results[work_group_id] = result
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
    orchestrator_slot_index: str | None = None,
) -> Json:
    identity = _ci_execution_batch_matrix_identity(batch)
    expected_writer_id = _ci_batch_expected_writer_id(
        execution_batch_manifest,
        batch,
    )
    if matrix_row.get("expected-job-identity") != expected_writer_id:
        msg = "matrix row expected writer identity does not match manifest"
        raise RuntimeError(msg)
    actual_matrix: Mapping[str, object] = identity
    identity_source = "github-actions-job-context"
    observed_writer_id = ci_validation_writer_id(
        workflow=workflow,
        job=job,
        matrix=actual_matrix,
    )
    orchestrator_job = (
        f"execution-batch-{identity['runner-family']}-orchestrator"
    )
    if observed_writer_id != expected_writer_id and job == orchestrator_job:
        if (
            orchestrator_slot_index is None
            or str(orchestrator_slot_index) == ""
        ):
            msg = (
                "physical runner-family orchestrator writer context requires "
                "orchestrator_slot_index"
            )
            raise RuntimeError(msg)
        actual_matrix = {}
        identity_source = "github-actions-orchestrator-job-context"
        observed_writer_id = ci_validation_writer_id(
            workflow=workflow,
            job=job,
            matrix=actual_matrix,
        )
    if observed_writer_id != expected_writer_id and job != orchestrator_job:
        msg = (
            "observed workflow/job/matrix writer identity does not match batch"
        )
        raise RuntimeError(msg)
    writer = {
        "identity-source": identity_source,
        "expected-boundary": "execution-batch",
        "expected-job-identity": expected_writer_id,
        "observed-writer-identity": observed_writer_id,
        "observed-workflow": workflow,
        "observed-job": job,
        "observed-matrix": dict(actual_matrix),
        "logical-batch-identity": identity,
    }
    if (
        identity_source == "github-actions-orchestrator-job-context"
        and orchestrator_slot_index is not None
    ):
        writer["observed-orchestrator-slot-index"] = str(
            orchestrator_slot_index
        )
    return writer


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
    command_outcome: _CiValidationOutcome = (
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
) -> tuple[_CiValidationOutcome, str | None, Json]:
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
    if not isinstance(subchecks, Sequence) or isinstance(subchecks, str):
        subchecks = ()
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
) -> tuple[_CiValidationOutcome, str | None]:
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
) -> tuple[_CiValidationOutcome, str | None]:
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


def _ci_release_shaped_artifact_builtin_outcome(  # noqa: PLR0911
    plan: Json | None,
    assignments: Json | None,
    changed_files_snapshot: Json | None,
    fact_snapshot: Json | None,
    observed_artifacts_dir: str,
    observed_commit_sha: str,
    matrix_work_group: Mapping[str, object],
    repo_root: Path,
) -> tuple[_CiValidationOutcome, str | None, Json]:
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
        no_publish = _ci_no_publish_release_shaped_artifact_evidence(
            plan=plan,
            work_group_id=work_group_id,
            obligations=obligations,
            observed_commit_sha=observed_commit_sha,
            matrix_work_group=matrix_work_group,
            fact_snapshot=fact_snapshot,
            repo_root=repo_root,
        )
        if no_publish is not None:
            return "success", None, no_publish
    except (KeyError, TypeError, ValueError) as exc:
        return "blocking-failure", str(exc), {}
    return (
        "blocking-failure",
        "artifact-shape-unconfirmed: byte-bound no-publish release-shaped artifact validation evidence is unavailable",
        {},
    )


def _ci_no_publish_release_shaped_artifact_evidence(
    *,
    plan: Mapping[str, object],
    work_group_id: str,
    obligations: Sequence[Mapping[str, object]],
    observed_commit_sha: str,
    matrix_work_group: Mapping[str, object],
    fact_snapshot: Mapping[str, object] | None,
    repo_root: Path,
) -> Json | None:
    expected_refs = [
        ref
        for obligation in obligations
        for ref in _ci_artifact_expected_refs(obligation)
    ]
    mapping_present, output_by_ref = (
        _ci_declared_validation_build_output_mapping(
            repo_root=repo_root,
            expected_refs=expected_refs,
        )
    )
    if output_by_ref is None or not set(expected_refs).issubset(output_by_ref):
        if mapping_present and output_by_ref is None:
            return None
        _ci_materialize_no_publish_release_shaped_artifacts(
            plan=plan,
            obligations=obligations,
            observed_commit_sha=observed_commit_sha,
            repo_root=repo_root,
        )
        output_by_ref = _ci_validation_build_outputs_by_artifact_ref(
            repo_root=repo_root,
            expected_refs=expected_refs,
        )
        if output_by_ref is None:
            return None
    digest_entries: list[Json] = []
    results: list[Json] = []
    for obligation in obligations:
        refs = _ci_artifact_expected_refs(obligation)
        descriptor_path = str(obligation.get("descriptor-path") or "")
        descriptor_fact = _ci_descriptor_fact(fact_snapshot, descriptor_path)
        descriptor_identity = (
            descriptor_fact.get("descriptor-identity")
            if descriptor_fact is not None
            else None
        )
        if not isinstance(descriptor_identity, str) or not descriptor_identity:
            return None
        observed_digests: list[Json] = []
        for artifact_ref in refs:
            output = output_by_ref[artifact_ref]
            digest_entry = _ci_validation_build_digest_entry(
                artifact_ref=artifact_ref,
                path=output,
                repo_root=repo_root,
            )
            observed_digests.append(
                {
                    "artifact-ref": artifact_ref,
                    "algorithm": "sha256",
                    "digest": digest_entry["digest"],
                    "digest-available": True,
                    "diagnostics": [],
                }
            )
            digest_entries.append(digest_entry)
        results.append(
            {
                "artifact-obligation-id": obligation["artifact-obligation-id"],
                "descriptor": {
                    "path": descriptor_path,
                    "identity": descriptor_identity,
                },
                "profile-coverage": obligation.get("profile-coverage"),
                "artifact": {
                    "planned": obligation.get("artifact"),
                    "observed": {
                        "refs": refs,
                        "digests": observed_digests,
                    },
                    "outcome": "success",
                    "diagnostics": [],
                },
                "release-receipt": {
                    "planned": obligation.get("release-receipt"),
                    "expected": True,
                    "schema-checked": True,
                    "outcome": "success",
                    "diagnostics": [],
                },
                "outcome": "success",
                "diagnostics": [],
            }
        )
    return {
        "evidence-source": "no-publish-validation",
        "source-proof": {
            "kind": "no-publish-validation-result",
            "work-group-id": work_group_id,
            "coverage-target": matrix_work_group.get("coverage-target"),
            "observed-commit-sha": observed_commit_sha,
            "artifact-digests": sorted(
                digest_entries,
                key=lambda item: str(item["artifact-ref"]),
            ),
        },
        "artifact-obligation-results": results,
    }


def _ci_validation_build_outputs_by_artifact_ref(
    *,
    repo_root: Path,
    expected_refs: Sequence[str],
) -> dict[str, Path] | None:
    if not expected_refs or len(expected_refs) != len(set(expected_refs)):
        return None
    mapping_present, declared = _ci_declared_validation_build_output_mapping(
        repo_root=repo_root,
        expected_refs=expected_refs,
    )
    if not mapping_present or declared is None:
        return None
    return declared if set(expected_refs).issubset(declared) else None


def _ci_materialize_no_publish_release_shaped_artifacts(
    *,
    plan: Mapping[str, object],
    obligations: Sequence[Mapping[str, object]],
    observed_commit_sha: str,
    repo_root: Path,
) -> None:
    build = _ci_no_publish_release_shaped_build_request(
        plan=plan,
        obligations=obligations,
        observed_commit_sha=observed_commit_sha,
        repo_root=repo_root,
    )
    if build is None:
        return
    request, artifact_refs_by_build_id = build
    request_digest = hashlib.sha256(
        json.dumps(request, sort_keys=True, separators=(",", ":")).encode()
    ).hexdigest()[:24]
    bundle_dir = (
        repo_root
        / ".three-ci-validation"
        / "work"
        / "validation-build"
        / "release-shaped"
        / request_digest
    )
    result = _ci_execute_no_publish_release_shaped_build(
        request=request,
        repo_root=repo_root,
        bundle_dir=bundle_dir,
    )
    _ci_record_validation_build_result_artifacts(
        repo_root=repo_root,
        bundle_dir=bundle_dir,
        result=result,
        artifact_refs_by_build_id=artifact_refs_by_build_id,
    )


def _ci_no_publish_release_shaped_build_request(  # noqa: PLR0911
    *,
    plan: Mapping[str, object],
    obligations: Sequence[Mapping[str, object]],
    observed_commit_sha: str,
    repo_root: Path,
) -> tuple[Json, dict[str, list[str]]] | None:
    if not obligations:
        return None
    profile = _ci_single_release_profile(obligations)
    descriptor_path = str(obligations[0].get("descriptor-path") or "")
    if not profile or not descriptor_path:
        return None
    if any(
        str(item.get("descriptor-path") or "") != descriptor_path
        for item in obligations
    ):
        return None
    descriptor_file = repo_root / descriptor_path
    if not descriptor_file.is_file():
        return None
    project_id = _ci_release_descriptor_project_id(repo_root, descriptor_path)
    if not project_id:
        return None
    release_plan = _ci_validation_release_plan(
        repo_root=repo_root,
        observed_commit_sha=observed_commit_sha,
        profile=profile,
        project_id=project_id,
    )
    variant, artifact_refs_by_build_id = (
        _ci_release_plan_variant_for_obligations(
            release_plan=release_plan,
            project_id=project_id,
            obligations=_ci_variant_release_shaped_obligations(
                plan=plan,
                descriptor_path=descriptor_path,
                profile=profile,
                seed_obligations=obligations,
            ),
        )
    )
    if variant is None or not artifact_refs_by_build_id:
        return None
    envelope = _mapping(release_plan["envelope"], "envelope")
    request = {
        "api-version": "three.release.build-request/v1alpha1",
        "kind": "build-request",
        "plan-id": envelope["plan-id"],
        "profile": envelope["profile"],
        "commit-sha": envelope["commit-sha"],
        "project": _mapping(envelope["projects"], "envelope.projects")[
            project_id
        ],
        "variant": variant,
        "artifacts": _build_request_artifacts(release_plan, variant),
    }
    validate_contract(request)
    return request, artifact_refs_by_build_id


def _ci_single_release_profile(
    obligations: Sequence[Mapping[str, object]],
) -> str | None:
    profiles = {
        str(profile)
        for obligation in obligations
        for profile in cast(
            "Sequence[object]",
            obligation.get("profile-coverage", []),
        )
        if isinstance(profile, str) and profile
    }
    return next(iter(profiles)) if len(profiles) == 1 else None


def _ci_release_descriptor_project_id(
    repo_root: Path,
    descriptor_path: str,
) -> str | None:
    descriptor = _read_yaml(repo_root / descriptor_path)
    if not isinstance(descriptor, Mapping):
        return None
    project = descriptor.get("project")
    if not isinstance(project, Mapping):
        return None
    project_id = project.get("id")
    return project_id if isinstance(project_id, str) and project_id else None


def _ci_validation_release_plan(
    *,
    repo_root: Path,
    observed_commit_sha: str,
    profile: str,
    project_id: str,
) -> Json:
    cache_dir = (
        repo_root / ".three-ci-validation" / "work" / "release-shaped-plans"
    )
    cache_dir.mkdir(parents=True, exist_ok=True)
    cache_key = hashlib.sha256(
        json.dumps(
            {
                "commit-sha": observed_commit_sha,
                "profile": profile,
                "project-id": project_id,
            },
            sort_keys=True,
            separators=(",", ":"),
        ).encode()
    ).hexdigest()[:24]
    cache_path = cache_dir / f"{cache_key}.json"
    if cache_path.is_file():
        cached = _read_json(cache_path)
        validate_contract(cached)
        return cached
    try:
        snapshot = validate_authoring(repo_root)
        project = snapshot.projects[project_id]
        request = {
            "api-version": "three.release.planner-request/v1alpha1",
            "kind": "planner-request",
            "commit-sha": observed_commit_sha,
            "profile": profile,
            "request-flags": {"force": False},
            "requested-project-ids": [project_id],
        }
        dotnet_metadata = None
        if project.ecosystem == "dotnet":
            metadata_input = snapshot.dotnet_metadata_input(observed_commit_sha)
            dotnet_metadata = collect_dotnet_metadata(metadata_input, repo_root)
        result = plan_release(
            snapshot,
            PlannerInputs(
                request=request,
                repo_root=repo_root,
                dry_run=True,
                validation_build=True,
                dotnet_metadata=dotnet_metadata,
            ),
        )
    except (KeyError, TypeError, ValueError) as exc:
        msg = f"release-shaped validation build planning failed: {exc}"
        raise ValueError(msg) from exc
    validate_contract(result.plan)
    _write_json(cache_path, result.plan)
    return result.plan


def _ci_variant_release_shaped_obligations(
    *,
    plan: Mapping[str, object],
    descriptor_path: str,
    profile: str,
    seed_obligations: Sequence[Mapping[str, object]],
) -> list[Mapping[str, object]]:
    dimensions = _ci_obligation_variant_dimensions(seed_obligations[0])
    if dimensions is None:
        return list(seed_obligations)
    grouped: list[Mapping[str, object]] = []
    artifact_obligations = plan.get("artifact-obligations", [])
    if not isinstance(artifact_obligations, Sequence) or isinstance(
        artifact_obligations, str | bytes
    ):
        return list(seed_obligations)
    for obligation in artifact_obligations:
        if not isinstance(obligation, Mapping):
            continue
        if str(obligation.get("descriptor-path") or "") != descriptor_path:
            continue
        profiles = obligation.get("profile-coverage", [])
        if not isinstance(profiles, Sequence) or isinstance(
            profiles, str | bytes
        ):
            continue
        if profile not in profiles:
            continue
        if _ci_obligation_variant_dimensions(obligation) == dimensions:
            grouped.append(obligation)
    return grouped or list(seed_obligations)


def _ci_obligation_variant_dimensions(
    obligation: Mapping[str, object],
) -> dict[str, object] | None:
    artifact = obligation.get("artifact")
    if not isinstance(artifact, Mapping):
        return None
    dimensions = artifact.get("variant-dimensions", {})
    return dict(dimensions) if isinstance(dimensions, Mapping) else None


def _ci_release_plan_variant_for_obligations(
    *,
    release_plan: Mapping[str, object],
    project_id: str,
    obligations: Sequence[Mapping[str, object]],
) -> tuple[Json | None, dict[str, list[str]]]:
    if not obligations:
        return None, {}
    expected_dimensions = (
        _ci_obligation_variant_dimensions(obligations[0]) or {}
    )
    graph = _mapping(release_plan["graph"], "graph")
    variants = _mapping(graph["variants"], "graph.variants")
    artifacts = _mapping(graph["artifacts"], "graph.artifacts")
    for variant in variants.values():
        candidate = dict(_mapping(variant, "graph.variants[]"))
        if candidate.get("project-id") != project_id:
            continue
        if (
            dict(
                _mapping(candidate.get("dimensions", {}), "variant.dimensions")
            )
            != expected_dimensions
        ):
            continue
        artifact_refs = _ci_release_plan_artifact_refs_for_obligations(
            variant=candidate,
            artifacts=artifacts,
            obligations=obligations,
        )
        if artifact_refs:
            return candidate, artifact_refs
    return None, {}


def _ci_release_plan_artifact_refs_for_obligations(
    *,
    variant: Mapping[str, object],
    artifacts: Mapping[str, object],
    obligations: Sequence[Mapping[str, object]],
) -> dict[str, list[str]]:
    available_ids = [
        str(item)
        for item in cast("Sequence[object]", variant.get("artifact-ids", []))
        if isinstance(item, str)
    ]
    unused = set(available_ids)
    refs_by_build_id: dict[str, list[str]] = {}
    for obligation in obligations:
        artifact_id = _ci_matching_release_plan_artifact_id(
            obligation=obligation,
            artifacts=artifacts,
            candidate_ids=unused,
        )
        if artifact_id is None:
            return {}
        refs = _ci_artifact_expected_refs(obligation)
        if not refs:
            return {}
        refs_by_build_id.setdefault(artifact_id, []).extend(refs)
        unused.remove(artifact_id)
    return refs_by_build_id


def _ci_matching_release_plan_artifact_id(
    *,
    obligation: Mapping[str, object],
    artifacts: Mapping[str, object],
    candidate_ids: set[str],
) -> str | None:
    expected = obligation.get("artifact")
    if not isinstance(expected, Mapping):
        return None
    expected_handles = _ci_artifact_expected_descriptor_handles(expected)
    matches: list[str] = []
    for artifact_id in sorted(candidate_ids):
        artifact = artifacts.get(artifact_id)
        if not isinstance(artifact, Mapping):
            continue
        if (
            artifact.get("role") == expected.get("logical-artifact-role")
            and artifact.get("kind-family") == expected.get("kind-family")
            and artifact.get("concrete-kind") == expected.get("concrete-kind")
        ):
            descriptor_handle = artifact.get("descriptor-handle")
            if expected_handles and descriptor_handle not in expected_handles:
                continue
            matches.append(artifact_id)
    return matches[0] if len(matches) == 1 else None


def _ci_artifact_expected_descriptor_handles(
    artifact: Mapping[str, object],
) -> set[str]:
    handles: set[str] = set()
    for artifact_ref in _ci_artifact_expected_refs({"artifact": artifact}):
        path = PurePosixPath(artifact_ref)
        if path.suffix != ".artifact":
            continue
        stem = path.stem
        if stem:
            handles.add(stem)
    return handles


def _ci_execute_no_publish_release_shaped_build(
    *,
    request: Mapping[str, object],
    repo_root: Path,
    bundle_dir: Path,
) -> Json:
    try:
        return execute_build(request, repo_root, bundle_dir)
    except BuildExecutorError as exc:
        msg = f"release-shaped validation build failed: {exc}"
        raise ValueError(msg) from exc


def _ci_record_validation_build_result_artifacts(
    *,
    repo_root: Path,
    bundle_dir: Path,
    result: Mapping[str, object],
    artifact_refs_by_build_id: Mapping[str, Sequence[str]],
) -> None:
    artifacts = result.get("artifacts")
    if not isinstance(artifacts, Mapping):
        msg = "release-shaped validation build result has no artifacts"
        raise TypeError(msg)
    additions: dict[str, str] = {}
    for artifact_id, refs in artifact_refs_by_build_id.items():
        receipt = artifacts.get(artifact_id)
        if not isinstance(receipt, Mapping):
            msg = f"release-shaped validation build omitted {artifact_id!r}"
            raise TypeError(msg)
        relative_path = receipt.get("bundle-relative-path")
        if not isinstance(relative_path, str) or not relative_path:
            msg = f"release-shaped validation build receipt for {artifact_id!r} has no bundle path"
            raise ValueError(msg)
        output = (bundle_dir / relative_path).resolve()
        if not output.is_file() or not _ci_validation_build_output_is_allowed(
            repo_root=repo_root,
            output=output,
        ):
            msg = f"release-shaped validation build output is not allowed for {artifact_id!r}"
            raise ValueError(msg)
        output_path = output.relative_to(repo_root.resolve()).as_posix()
        for artifact_ref in refs:
            additions[str(artifact_ref)] = output_path
    _ci_update_validation_build_output_mapping(repo_root, additions)


def _ci_update_validation_build_output_mapping(
    repo_root: Path,
    additions: Mapping[str, str],
) -> None:
    if not additions:
        return
    mapping_path = (
        repo_root
        / ".three-ci-validation"
        / "work"
        / "validation-build-artifacts.json"
    )
    mapping_path.parent.mkdir(parents=True, exist_ok=True)
    existing: dict[str, str] = {}
    mapping_present, items = _ci_validation_build_output_mapping_items(
        mapping_path
    )
    if mapping_present and items is not None:
        for item in items:
            if not isinstance(item, Mapping):
                continue
            artifact_ref = item.get("artifact-ref")
            path_value = item.get("path")
            if isinstance(artifact_ref, str) and isinstance(path_value, str):
                existing[artifact_ref] = path_value
    existing.update(additions)
    _write_json(mapping_path, {"artifacts": dict(sorted(existing.items()))})


def _ci_declared_validation_build_output_mapping(
    *,
    repo_root: Path,
    expected_refs: Sequence[str],
) -> tuple[bool, dict[str, Path] | None]:
    mapping_path = (
        repo_root
        / ".three-ci-validation"
        / "work"
        / "validation-build-artifacts.json"
    )
    mapping_present, declared_items = _ci_validation_build_output_mapping_items(
        mapping_path,
    )
    if not mapping_present:
        return False, None
    if declared_items is None:
        return True, None
    expected_set = set(expected_refs)
    output_by_ref: dict[str, Path] = {}
    for item in declared_items:
        if (
            isinstance(item, Mapping)
            and isinstance(item.get("artifact-ref"), str)
            and item.get("artifact-ref") not in expected_set
        ):
            continue
        parsed_item = _ci_declared_validation_build_output_mapping_item(
            repo_root=repo_root,
            expected_refs=expected_set,
            output_by_ref=output_by_ref,
            item=item,
        )
        if parsed_item is None:
            return True, None
        artifact_ref, output = parsed_item
        output_by_ref[artifact_ref] = output
    return True, output_by_ref


def _ci_declared_validation_build_output_mapping_item(
    *,
    repo_root: Path,
    expected_refs: set[str],
    output_by_ref: Mapping[str, Path],
    item: object,
) -> tuple[str, Path] | None:
    if not isinstance(item, Mapping):
        return None
    artifact_ref = item.get("artifact-ref")
    path_value = item.get("path")
    if (
        not isinstance(artifact_ref, str)
        or artifact_ref not in expected_refs
        or artifact_ref in output_by_ref
        or not isinstance(path_value, str)
        or not path_value
    ):
        return None
    output = (repo_root / path_value).resolve()
    try:
        output.relative_to(repo_root.resolve())
    except ValueError:
        return None
    if not output.is_file() or not _ci_validation_build_output_is_allowed(
        repo_root=repo_root,
        output=output,
    ):
        return None
    return artifact_ref, output


def _ci_validation_build_output_is_allowed(
    *,
    repo_root: Path,
    output: Path,
) -> bool:
    validation_build_root = (
        repo_root / ".three-ci-validation" / "work" / "validation-build"
    ).resolve()
    validation_build_gem = (
        repo_root / ".three-ci-validation" / "work" / "validation-build.gem"
    ).resolve()
    if output == validation_build_gem:
        return True
    if output == validation_build_root:
        return True
    with suppress(ValueError):
        output.relative_to(validation_build_root)
        return True
    return False


def _ci_validation_build_output_mapping_items(
    mapping_path: Path,
) -> tuple[bool, list[object] | None]:
    try:
        mapping_stat = mapping_path.lstat()
    except FileNotFoundError:
        return False, None
    except OSError:
        mapping_stat = None
    if mapping_stat is None or not stat.S_ISREG(mapping_stat.st_mode):
        return True, None
    try:
        raw = json.loads(mapping_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return True, None
    items = raw.get("artifacts", raw) if isinstance(raw, Mapping) else raw
    if isinstance(items, Mapping):
        return True, [
            {"artifact-ref": artifact_ref, "path": path}
            for artifact_ref, path in items.items()
        ]
    if isinstance(items, Sequence) and not isinstance(items, str | bytes):
        return True, list(items)
    return True, None


def _ci_validation_build_digest_entry(
    *,
    artifact_ref: str,
    path: Path,
    repo_root: Path,
) -> Json:
    data = path.read_bytes()
    try:
        relative_path = (
            path.resolve().relative_to(repo_root.resolve()).as_posix()
        )
    except ValueError:
        relative_path = path.as_posix()
    return {
        "artifact-ref": artifact_ref,
        "algorithm": "sha256",
        "digest": hashlib.sha256(data).hexdigest(),
        "byte-source": {
            "kind": "validation-build-output",
            "path": relative_path,
            "size": len(data),
        },
    }


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


def _ci_no_publish_release_shaped_source_proof_is_admissible(  # noqa: C901, PLR0911
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
    if not _ci_source_proof_digests_are_byte_bound(
        cast("Sequence[Mapping[str, object]]", proof_digests)
    ):
        return False
    expected_entries = _ci_release_shaped_digest_proof_entries_from_results(
        cast(
            "Sequence[Mapping[str, object]]",
            source_command["artifact-obligation-results"],
        )
    )
    proof_entries = [
        {
            "artifact-ref": item.get("artifact-ref"),
            "algorithm": item.get("algorithm"),
            "digest": item.get("digest"),
        }
        for item in cast("Sequence[Mapping[str, object]]", proof_digests)
    ]
    return expected_entries == sorted(
        proof_entries,
        key=lambda item: str(item["artifact-ref"]),
    )


def _ci_source_proof_digests_are_byte_bound(
    proof_digests: Sequence[Mapping[str, object]],
) -> bool:
    seen: set[str] = set()
    for item in proof_digests:
        artifact_ref = item.get("artifact-ref")
        digest = item.get("digest")
        byte_source = item.get("byte-source")
        if (
            not isinstance(artifact_ref, str)
            or artifact_ref in seen
            or item.get("algorithm") != "sha256"
            or not isinstance(digest, str)
            or re.fullmatch(r"[0-9a-f]{64}", digest) is None
            or not isinstance(byte_source, Mapping)
            or byte_source.get("kind") != "validation-build-output"
            or not isinstance(byte_source.get("path"), str)
            or not isinstance(byte_source.get("size"), int)
            or isinstance(byte_source.get("size"), bool)
            or cast("int", byte_source.get("size")) < 0
        ):
            return False
        seen.add(artifact_ref)
    return bool(seen)


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


def _cmd_download_ci_validation_observed_artifacts(
    args: argparse.Namespace,
) -> int:
    root = Path(args.observed_artifacts_dir)
    if root.exists():
        shutil.rmtree(root)
    root.mkdir(parents=True, exist_ok=True)
    plan = _ci_read_download_context_mapping(args, "plan", "plan")
    execution_batch_manifest = _ci_read_download_context_mapping(
        args,
        "execution_batch_manifest",
        "execution-batch manifest",
    )
    names: list[str] = []
    batch_downloads = (
        _ci_batch_observed_artifact_downloads(execution_batch_manifest)
        if execution_batch_manifest is not None
        else {}
    )
    observed_run_attempt = (
        _ci_execution_manifest_run_attempt(execution_batch_manifest)
        or _ci_execution_manifest_run_attempt(plan)
        or str(getattr(args, "run_attempt", ""))
    )
    known_prior_attempt_names = _ci_known_prior_attempt_artifact_names(
        _ci_live_namespace_allowed_artifact_refs(
            execution_batch_manifest=execution_batch_manifest,
            plan=plan,
            run_id=str(args.run_id),
            run_attempt=observed_run_attempt,
        ),
        run_id=str(args.run_id),
        run_attempt=observed_run_attempt,
    )
    excluded_namespace_names = (
        _ci_current_final_aggregate_artifact_names(
            run_id=str(args.run_id),
            run_attempt=observed_run_attempt,
        )
        | known_prior_attempt_names
    )
    for artifact_name_value in batch_downloads:
        if artifact_name_value not in names:
            names.append(artifact_name_value)
    downloaded: list[str] = []
    failed: list[str] = []
    admitted_batch_artifacts: list[Mapping[str, object]] = []
    artifact_api_by_name: dict[str, list[Mapping[str, object]]] = {}
    artifact_api_singletons_by_name: dict[str, Mapping[str, object]] = {}
    artifact_api_failed = False
    try:
        artifact_api_by_name = _ci_observed_artifact_api_multimap(
            repository=args.repository,
            run_id=str(args.run_id),
            run_attempt=observed_run_attempt,
            prefixed_artifact_cap=_CI_VALIDATION_LIVE_NAMESPACE_ARTIFACT_CAP,
            excluded_prefixed_artifact_names=excluded_namespace_names,
        )
        artifact_api_singletons_by_name = _ci_observed_artifact_api_singletons(
            artifact_api_by_name,
        )
        failed.extend(
            _materialize_ci_live_unexpected_contract_artifacts(
                root,
                artifact_api_by_name=artifact_api_by_name,
                execution_batch_manifest=execution_batch_manifest,
                plan=plan,
                run_id=str(args.run_id),
                run_attempt=observed_run_attempt,
            )
        )
    except (
        ContractValidationError,
        RuntimeError,
        TypeError,
        ValueError,
    ) as exc:
        artifact_api_failed = True
        print(
            f"warning: failed to enumerate GitHub Actions artifacts for "
            f"metadata materialization: {exc}",
            file=sys.stderr,
        )
    _materialize_ci_downloader_observation(
        root,
        run_id=str(args.run_id),
        run_attempt=observed_run_attempt,
        artifact_api_metadata_available=not artifact_api_failed,
        namespace_overflow=_ci_live_namespace_overflow_detected(
            artifact_api_by_name,
            run_id=str(args.run_id),
            run_attempt=observed_run_attempt,
            excluded_prefixed_artifact_names=excluded_namespace_names,
        ),
    )
    for artifact_name_value in names:
        try:
            expected_artifact_ref = batch_downloads.get(artifact_name_value)
            artifact_api = None
            admission_source = "github-actions-live-api"
            if expected_artifact_ref is not None:
                artifact_api = _admit_expected_ci_batch_artifact_api_instance(
                    artifact_name_value,
                    artifact_api_by_name=artifact_api_by_name,
                )
                _raise_if_expected_batch_artifact_wrong_attempt(
                    artifact_api,
                    artifact_name_value=artifact_name_value,
                    run_id=str(args.run_id),
                    run_attempt=observed_run_attempt,
                )
            else:
                artifact_api = artifact_api_singletons_by_name.get(
                    artifact_name_value
                )
            destination = root / artifact_name_value
            if expected_artifact_ref is not None:
                _download_artifact_by_id(
                    args.repository,
                    artifact_api,
                    artifact_name_value,
                    destination,
                )
            else:
                _download_artifact(
                    args.repository,
                    int(args.run_id),
                    artifact_name_value,
                    destination,
                )
            _materialize_ci_observed_artifact_metadata(
                destination,
                artifact_name_value=artifact_name_value,
                artifact_api=artifact_api,
                run_id=str(args.run_id),
                expected_artifact_ref=expected_artifact_ref,
                expected_run_attempt=observed_run_attempt,
                admission_source=admission_source,
            )
            if expected_artifact_ref is not None:
                admitted_batch_artifacts.append(
                    _ci_downloader_batch_admission_record(
                        artifact_name_value=artifact_name_value,
                        artifact_api=cast(
                            "Mapping[str, object]",
                            artifact_api,
                        ),
                        artifact_ref=expected_artifact_ref,
                        execution_batch_manifest=execution_batch_manifest,
                        run_id=str(args.run_id),
                        run_attempt=observed_run_attempt,
                        admission_source=admission_source,
                    )
                )
            downloaded.append(artifact_name_value)
        except (RuntimeError, TypeError, ValueError, OSError) as exc:
            failed.append(artifact_name_value)
            print(f"warning: {exc}", file=sys.stderr)
    _materialize_ci_downloader_observation(
        root,
        run_id=str(args.run_id),
        run_attempt=observed_run_attempt,
        artifact_api_metadata_available=not artifact_api_failed,
        namespace_overflow=_ci_live_namespace_overflow_detected(
            artifact_api_by_name,
            run_id=str(args.run_id),
            run_attempt=observed_run_attempt,
            excluded_prefixed_artifact_names=excluded_namespace_names,
        ),
        admitted_batch_artifacts=admitted_batch_artifacts,
    )
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
            "artifact_api_metadata_available": _bool_str(
                not artifact_api_failed
            ),
            "namespace_overflow": _bool_str(
                _ci_live_namespace_overflow_detected(
                    artifact_api_by_name,
                    run_id=str(args.run_id),
                    run_attempt=observed_run_attempt,
                    excluded_prefixed_artifact_names=excluded_namespace_names,
                )
            ),
        },
    )
    return 0


def _materialize_ci_downloader_observation(
    root: Path,
    *,
    run_id: str,
    run_attempt: str,
    artifact_api_metadata_available: bool,
    namespace_overflow: bool,
    admitted_batch_artifacts: Sequence[Mapping[str, object]] | None = None,
) -> None:
    _write_json(
        root / _CI_DOWNLOADER_OBSERVATION_FILE,
        {
            _CI_DOWNLOADER_ADMITTED_BATCH_ARTIFACTS_KEY: sorted(
                [dict(item) for item in (admitted_batch_artifacts or [])],
                key=lambda item: str(item.get("candidate-id")),
            ),
            "artifact-api-metadata-available": artifact_api_metadata_available,
            "namespace-enumeration": (
                "available"
                if artifact_api_metadata_available
                else "unavailable"
            ),
            "namespace-overflow": namespace_overflow,
            "run-id": run_id,
            "run-attempt": run_attempt,
        },
    )


def _ci_downloader_batch_admission_record(
    *,
    artifact_name_value: str,
    artifact_api: Mapping[str, object],
    artifact_ref: str,
    execution_batch_manifest: Mapping[str, object] | None,
    run_id: str,
    run_attempt: str,
    admission_source: str,
) -> Mapping[str, object]:
    artifact_instance_id = str(artifact_api["id"])
    batch_id = _ci_batch_id_for_expected_artifact_ref(
        execution_batch_manifest,
        artifact_ref,
    )
    return {
        "admission-source": admission_source,
        "artifact-instance-id": artifact_instance_id,
        "artifact-ref": artifact_ref,
        "batch-id": batch_id,
        "candidate-id": ci_validation_batch_evidence_candidate_id(
            run_id=run_id,
            run_attempt=run_attempt,
            batch_id=batch_id,
            artifact_ref=artifact_ref,
            artifact_instance_id=artifact_instance_id,
            physical_artifact_name=artifact_name_value,
        ),
        "physical-artifact-name": artifact_name_value,
        "producer-boundary": "execution-batch",
        "run-attempt": run_attempt,
        "run-id": run_id,
    }


def _ci_batch_id_for_expected_artifact_ref(
    execution_batch_manifest: Mapping[str, object] | None,
    artifact_ref: str,
) -> str:
    if execution_batch_manifest is not None:
        for batch in cast(
            "Sequence[Mapping[str, object]]",
            execution_batch_manifest.get("batches", []),
        ):
            if batch.get("expected-batch-evidence-bundle-ref") == artifact_ref:
                batch_id = batch.get("batch-id")
                if isinstance(batch_id, str) and batch_id:
                    return batch_id
    msg = f"no execution batch found for expected artifact ref {artifact_ref}"
    raise ValueError(msg)


def _ci_read_download_context_mapping(
    args: argparse.Namespace,
    attr: str,
    label: str,
) -> Mapping[str, object] | None:
    try:
        value = _read_optional_json(getattr(args, attr, ""))
        if value is None:
            return None
        if isinstance(value, Mapping):
            return value
        print(
            f"warning: {label} must be a JSON object",
            file=sys.stderr,
        )
    except (OSError, json.JSONDecodeError, TypeError, ValueError) as exc:
        print(
            f"warning: failed to read {label} for observed artifact "
            f"downloads: {exc}",
            file=sys.stderr,
        )
    return None


def _ci_execution_manifest_run_attempt(
    manifest: Mapping[str, object] | None,
) -> str | None:
    if manifest is None:
        return None
    run = manifest.get("run")
    if not isinstance(run, Mapping):
        return None
    run_attempt = run.get("run-attempt")
    return run_attempt if isinstance(run_attempt, str) else None


def _ci_observed_artifact_api_multimap(
    *,
    repository: str,
    run_id: str,
    run_attempt: str,
    prefixed_artifact_cap: int | None = None,
    excluded_prefixed_artifact_names: Collection[str] = (),
) -> dict[str, list[Mapping[str, object]]]:
    artifact_api_by_name: dict[str, list[Mapping[str, object]]] = {}
    for artifact in _github_actions_run_artifacts(
        repository=repository,
        run_id=run_id,
        run_attempt=run_attempt,
        prefixed_artifact_cap=prefixed_artifact_cap,
        excluded_prefixed_artifact_names=excluded_prefixed_artifact_names,
    ):
        if artifact.get("expired") is True:
            continue
        artifact_name = artifact.get("name")
        if isinstance(artifact_name, str):
            artifact_api_by_name.setdefault(artifact_name, []).append(artifact)
    return artifact_api_by_name


def _ci_live_namespace_overflow_detected(
    artifact_api_by_name: Mapping[str, Sequence[Mapping[str, object]]],
    *,
    run_id: str,
    run_attempt: str,
    excluded_prefixed_artifact_names: Collection[str] = (),
) -> bool:
    current_attempt_prefix = _ci_attempt_physical_artifact_name_prefix(
        run_id=run_id,
        run_attempt=run_attempt,
    )
    return (
        sum(
            len(artifacts)
            for artifact_name_value, artifacts in artifact_api_by_name.items()
            if artifact_name_value.startswith(current_attempt_prefix)
            and artifact_name_value not in excluded_prefixed_artifact_names
        )
        > _CI_VALIDATION_LIVE_NAMESPACE_ARTIFACT_CAP
    )


def _ci_observed_artifact_api_singletons(
    artifact_api_by_name: Mapping[str, Sequence[Mapping[str, object]]],
) -> dict[str, Mapping[str, object]]:
    return {
        artifact_name: artifacts[0]
        for artifact_name, artifacts in artifact_api_by_name.items()
        if len(artifacts) == 1
    }


def _materialize_ci_live_unexpected_contract_artifacts(
    root: Path,
    *,
    artifact_api_by_name: Mapping[str, Sequence[Mapping[str, object]]],
    execution_batch_manifest: Mapping[str, object] | None,
    plan: Mapping[str, object] | None,
    run_id: str,
    run_attempt: str,
) -> list[str]:
    names = _ci_live_unexpected_contract_artifact_names(
        artifact_api_by_name,
        execution_batch_manifest=execution_batch_manifest,
        plan=plan,
        run_id=run_id,
        run_attempt=run_attempt,
    )
    for artifact_name_value in names:
        _materialize_ci_live_unexpected_contract_artifact(
            root,
            artifact_name_value=artifact_name_value,
            artifacts=artifact_api_by_name[artifact_name_value],
            run_id=run_id,
        )
    return names


def _ci_live_unexpected_contract_artifact_names(
    artifact_api_by_name: Mapping[str, Sequence[Mapping[str, object]]],
    *,
    execution_batch_manifest: Mapping[str, object] | None,
    plan: Mapping[str, object] | None = None,
    run_id: str,
    run_attempt: str,
) -> list[str]:
    allowed_names = _ci_live_namespace_allowed_artifact_names(
        execution_batch_manifest=execution_batch_manifest,
        plan=plan,
        run_id=run_id,
        run_attempt=run_attempt,
    )
    current_attempt_prefix = _ci_attempt_physical_artifact_name_prefix(
        run_id=run_id,
        run_attempt=run_attempt,
    )
    return [
        artifact_name_value
        for artifact_name_value in sorted(artifact_api_by_name)
        if artifact_name_value.startswith(current_attempt_prefix)
        and artifact_name_value not in allowed_names
    ]


def _ci_live_namespace_allowed_artifact_names(
    *,
    execution_batch_manifest: Mapping[str, object] | None,
    plan: Mapping[str, object] | None = None,
    run_id: str | None = None,
    run_attempt: str | None = None,
) -> set[str]:
    return {
        artifact_physical_name(ref)
        for ref in _ci_live_namespace_allowed_artifact_refs(
            execution_batch_manifest=execution_batch_manifest,
            plan=plan,
            run_id=run_id,
            run_attempt=run_attempt,
        )
        if ref
    }


def _ci_live_namespace_allowed_artifact_refs(
    *,
    execution_batch_manifest: Mapping[str, object] | None,
    plan: Mapping[str, object] | None = None,
    run_id: str | None = None,
    run_attempt: str | None = None,
) -> set[str]:
    refs: set[str] = set()
    if execution_batch_manifest is not None:
        refs.update(
            _ci_batch_observed_artifact_downloads(
                execution_batch_manifest,
            ).values()
        )
    if execution_batch_manifest is not None:
        run = execution_batch_manifest.get("run")
        if isinstance(run, Mapping):
            manifest_run_id = run.get("run-id")
            manifest_run_attempt = run.get("run-attempt")
            if isinstance(manifest_run_id, str):
                run_id = manifest_run_id
            if isinstance(manifest_run_attempt, str):
                run_attempt = manifest_run_attempt
    if (
        isinstance(run_id, str)
        and isinstance(run_attempt, str)
        and run_id
        and run_attempt
    ):
        refs.update(
            {
                ci_validation_request_artifact_ref(
                    run_id=run_id,
                    run_attempt=run_attempt,
                ),
                ci_validation_plan_artifact_ref(
                    run_id=run_id,
                    run_attempt=run_attempt,
                ),
                ci_validation_execution_batch_manifest_artifact_ref(
                    run_id=run_id,
                    run_attempt=run_attempt,
                ),
                ci_validation_aggregate_evidence_manifest_artifact_ref(
                    run_id=run_id,
                    run_attempt=run_attempt,
                ),
                ci_validation_aggregate_summary_artifact_ref(
                    run_id=run_id,
                    run_attempt=run_attempt,
                ),
            }
        )
        refs.update(
            _ci_live_required_snapshot_artifact_refs(
                plan,
                run_id=run_id,
                run_attempt=run_attempt,
            )
        )
    return {ref for ref in refs if ref}


def _ci_live_required_snapshot_artifact_refs(
    plan: Mapping[str, object] | None,
    *,
    run_id: str,
    run_attempt: str,
) -> set[str]:
    if plan is None:
        return set()
    refs: set[str] = set()
    affected_range = plan.get("affected-range")
    if isinstance(affected_range, Mapping) and isinstance(
        affected_range.get("changed-files-hash"),
        str,
    ):
        refs.add(
            ci_validation_changed_files_snapshot_artifact_ref(
                run_id=run_id,
                run_attempt=run_attempt,
            )
        )
    fact_snapshot = plan.get("fact-snapshot")
    if isinstance(fact_snapshot, Mapping) and isinstance(
        fact_snapshot.get("id"),
        str,
    ):
        refs.add(
            ci_validation_fact_snapshot_artifact_ref(
                run_id=run_id,
                run_attempt=run_attempt,
            )
        )
    return refs


def _ci_current_final_aggregate_artifact_names(
    *,
    run_id: str,
    run_attempt: str,
) -> set[str]:
    return {
        artifact_physical_name(
            ci_validation_aggregate_evidence_manifest_artifact_ref(
                run_id=run_id,
                run_attempt=run_attempt,
            )
        ),
        artifact_physical_name(
            ci_validation_aggregate_summary_artifact_ref(
                run_id=run_id,
                run_attempt=run_attempt,
            )
        ),
    }


def _ci_current_aggregate_evidence_manifest_artifact_name(
    *,
    run_id: str,
    run_attempt: str,
) -> str:
    return artifact_physical_name(
        ci_validation_aggregate_evidence_manifest_artifact_ref(
            run_id=run_id,
            run_attempt=run_attempt,
        )
    )


def _ci_current_aggregate_summary_artifact_name(
    *,
    run_id: str,
    run_attempt: str,
) -> str:
    return artifact_physical_name(
        ci_validation_aggregate_summary_artifact_ref(
            run_id=run_id,
            run_attempt=run_attempt,
        )
    )


def _ci_current_final_aggregate_artifact_names_for_boundary(
    expected_artifacts: Sequence[Mapping[str, object]],
    *,
    run_id: str,
    run_attempt: str,
) -> set[str]:
    aggregate_manifest_ref = (
        ci_validation_aggregate_evidence_manifest_artifact_ref(
            run_id=run_id,
            run_attempt=run_attempt,
        )
    )
    aggregate_summary_ref = ci_validation_aggregate_summary_artifact_ref(
        run_id=run_id,
        run_attempt=run_attempt,
    )
    expected_refs = {
        artifact_ref
        for artifact in expected_artifacts
        if isinstance((artifact_ref := artifact.get("artifact-ref")), str)
    }
    if aggregate_summary_ref in expected_refs:
        return set()
    if aggregate_manifest_ref in expected_refs:
        return {
            _ci_current_aggregate_evidence_manifest_artifact_name(
                run_id=run_id,
                run_attempt=run_attempt,
            )
        }
    return _ci_current_final_aggregate_artifact_names(
        run_id=run_id,
        run_attempt=run_attempt,
    )


def _ci_current_final_aggregate_artifact_names_for_phase(
    *,
    aggregate_phase: str,
    run_id: str,
    run_attempt: str,
) -> set[str]:
    if aggregate_phase == "summary":
        return {
            _ci_current_aggregate_evidence_manifest_artifact_name(
                run_id=run_id,
                run_attempt=run_attempt,
            )
        }
    return _ci_current_final_aggregate_artifact_names(
        run_id=run_id,
        run_attempt=run_attempt,
    )


def _materialize_ci_live_unexpected_contract_artifact(
    root: Path,
    *,
    artifact_name_value: str,
    artifacts: Sequence[Mapping[str, object]],
    run_id: str,
) -> None:
    destination = root / artifact_name_value
    destination.mkdir(parents=True, exist_ok=True)
    _write_json(
        destination / "artifact-metadata.json",
        {
            "physical-artifact-name": artifact_name_value,
            "run-id": run_id,
            "artifact-instance-ids": [
                str(artifact["id"])
                for artifact in artifacts
                if artifact.get("id") is not None
            ],
            "producer-boundary": "unexpected-live-contract-artifact",
        },
    )


def _admit_expected_ci_batch_artifact_api_instance(
    artifact_name_value: str,
    *,
    artifact_api_by_name: Mapping[str, Sequence[Mapping[str, object]]],
) -> Mapping[str, object]:
    live_artifacts = artifact_api_by_name.get(artifact_name_value, [])
    if len(live_artifacts) != 1:
        msg = (
            f"expected batch artifact {artifact_name_value} to have exactly "
            f"one live GitHub Actions artifact instance, found "
            f"{len(live_artifacts)}"
        )
        raise RuntimeError(msg)
    artifact = live_artifacts[0]
    artifact_id = artifact.get("id")
    if artifact_id is None or not str(artifact_id):
        msg = f"expected batch artifact {artifact_name_value} is missing artifact id"
        raise RuntimeError(msg)
    if artifact.get("expired") is True:
        msg = f"expected batch artifact {artifact_name_value} is expired"
        raise RuntimeError(msg)
    return artifact


def _raise_if_expected_batch_artifact_wrong_attempt(
    artifact_api: Mapping[str, object],
    *,
    artifact_name_value: str,
    run_id: str,
    run_attempt: str,
) -> None:
    if _ci_live_artifact_matches_expected(
        artifact_api,
        artifact_id=str(artifact_api.get("id", "")),
        artifact_name_value=artifact_name_value,
        run_id=run_id,
        run_attempt=run_attempt,
    ):
        return
    msg = (
        f"expected batch artifact {artifact_name_value} does not bind to the "
        "current run attempt"
    )
    raise RuntimeError(msg)


def _ci_batch_observed_artifact_downloads(
    execution_batch_manifest: Mapping[str, object],
) -> dict[str, str]:
    downloads: dict[str, str] = {}
    for batch in cast(
        "Sequence[Mapping[str, object]]",
        execution_batch_manifest.get("batches", []),
    ):
        artifact_ref = batch.get("expected-batch-evidence-bundle-ref")
        if not isinstance(artifact_ref, str) or not artifact_ref:
            continue
        downloads[artifact_physical_name(artifact_ref)] = artifact_ref
    return downloads


def _materialize_ci_observed_artifact_metadata(
    artifact_dir: Path,
    *,
    artifact_name_value: str,
    artifact_api: Mapping[str, object] | None,
    run_id: str,
    expected_artifact_ref: str | None = None,
    expected_run_attempt: str | None = None,
    admission_source: str = "github-actions-live-api",
) -> None:
    bundle_path = artifact_dir / "batch-evidence-bundle.json"
    if not bundle_path.is_file():
        return
    if artifact_api is None:
        msg = (
            f"downloaded artifact {artifact_name_value} was not present in the "
            "GitHub Actions artifacts API response"
        )
        raise RuntimeError(msg)
    artifact_id = artifact_api.get("id")
    api_name = artifact_api.get("name")
    if artifact_id is None or api_name != artifact_name_value:
        msg = (
            f"downloaded artifact {artifact_name_value} did not have trusted "
            "GitHub Actions artifact identity metadata"
        )
        raise RuntimeError(msg)
    if expected_artifact_ref is None or expected_run_attempt is None:
        bundle = _read_json(bundle_path)
        if not isinstance(bundle, Mapping):
            msg = (
                f"{bundle_path} did not contain a batch evidence bundle object"
            )
            raise TypeError(msg)
        artifact_ref = bundle.get("artifact-ref")
        if not isinstance(artifact_ref, str):
            msg = f"{bundle_path} did not contain batch artifact ref metadata"
            raise TypeError(msg)
        run_attempt = cast("Mapping[str, object]", bundle.get("run", {})).get(
            "run-attempt"
        )
        if not isinstance(run_attempt, str) or not run_attempt:
            msg = f"{bundle_path} did not identify its run attempt"
            raise TypeError(msg)
    else:
        artifact_ref = expected_artifact_ref
        run_attempt = expected_run_attempt
    _write_json(
        artifact_dir / "artifact-metadata.json",
        {
            "artifact-instance-id": str(artifact_id),
            "artifact-ref": artifact_ref,
            "physical-artifact-name": artifact_name_value,
            "run-id": run_id,
            "run-attempt": run_attempt,
            "producer-boundary": "execution-batch",
            "admission-source": admission_source,
        },
    )


def _cmd_aggregate_ci_evidence(args: argparse.Namespace) -> int:
    owner, name = _split_repository(args.repository)
    created_at = args.created_at or _utc_now()
    completed_at = getattr(args, "completed_at", None) or created_at
    is_batch_aggregation = bool(
        getattr(args, "execution_batch_manifest", "")
    ) or bool(getattr(args, "batch_materialization_failed", False))
    if not is_batch_aggregation:
        print(
            "error: aggregate-ci-evidence requires explicit G5 batch mode "
            "(--execution-batch-manifest or --batch-materialization-failed)",
            file=sys.stderr,
        )
        return 2
    if not str(getattr(args, "observed_artifacts_dir", "")).strip():
        print(
            "error: aggregate-ci-evidence batch mode requires "
            "--observed-artifacts-dir from the downloader step",
            file=sys.stderr,
        )
        return 2
    boundary_diagnostics = _ci_aggregate_control_artifact_boundary_diagnostics(
        args,
    )
    malformed_control_diagnostics: list[Mapping[str, object]] = []
    try:
        plan = _read_optional_json(args.plan)
    except OSError:
        return _cmd_aggregate_ci_batch_evidence(
            args,
            owner=owner,
            name=name,
            created_at=created_at,
            completed_at=completed_at,
            plan=None,
            changed_files_snapshot=None,
            fact_snapshot=None,
            boundary_diagnostics=[
                *boundary_diagnostics,
                _ci_invalid_plan_control_diagnostic(
                    args,
                    detail=DiagnosticDetail.PLAN_UNREADABLE.value,
                    message="Validation plan control input is unreadable.",
                ),
            ],
        )
    except (json.JSONDecodeError, TypeError, ValueError):
        return _cmd_aggregate_ci_batch_evidence(
            args,
            owner=owner,
            name=name,
            created_at=created_at,
            completed_at=completed_at,
            plan=None,
            changed_files_snapshot=None,
            fact_snapshot=None,
            boundary_diagnostics=[
                *boundary_diagnostics,
                _ci_invalid_plan_control_diagnostic(
                    args,
                    detail=DiagnosticDetail.MALFORMED_PLAN.value,
                    message="Validation plan control input is malformed.",
                ),
            ],
        )
    try:
        changed_files_snapshot = _read_optional_json(
            args.changed_files_snapshot
        )
    except (json.JSONDecodeError, TypeError, ValueError):
        changed_files_snapshot = None
        malformed_control_diagnostics.append(
            _ci_malformed_control_input_diagnostic(
                ci_validation_changed_files_snapshot_artifact_ref(
                    run_id=str(args.run_id),
                    run_attempt=str(args.run_attempt),
                ),
                code=DiagnosticFamily.INVALID_PLAN.value,
                detail=DiagnosticDetail.CHANGED_FILES_SNAPSHOT_MALFORMED.value,
                message="Changed-files snapshot control input is malformed.",
            )
        )
    try:
        fact_snapshot = _read_optional_json(args.fact_snapshot)
    except (json.JSONDecodeError, TypeError, ValueError):
        fact_snapshot = None
        malformed_control_diagnostics.append(
            _ci_malformed_control_input_diagnostic(
                ci_validation_fact_snapshot_artifact_ref(
                    run_id=str(args.run_id),
                    run_attempt=str(args.run_attempt),
                ),
                code=DiagnosticFamily.INVALID_PLAN.value,
                detail=DiagnosticDetail.FACT_SNAPSHOT_MALFORMED.value,
                message="Fact snapshot control input is malformed.",
            )
        )
    if plan is not None:
        try:
            validate_ci_validation_plan(
                plan,
                changed_files_snapshot=changed_files_snapshot,
                fact_snapshot=fact_snapshot,
            )
        except ContractValidationError:
            return _cmd_aggregate_ci_batch_evidence(
                args,
                owner=owner,
                name=name,
                created_at=created_at,
                completed_at=completed_at,
                plan=None,
                changed_files_snapshot=None,
                fact_snapshot=None,
                boundary_diagnostics=[
                    *boundary_diagnostics,
                    _ci_invalid_plan_control_diagnostic(
                        args,
                        detail=_ci_invalid_plan_detail_for_validation_error(
                            plan
                        ),
                        message=("Validation plan control input is invalid."),
                    ),
                    *malformed_control_diagnostics,
                ],
                invalid_plan_context=plan,
            )
    return _cmd_aggregate_ci_batch_evidence(
        args,
        owner=owner,
        name=name,
        created_at=created_at,
        completed_at=completed_at,
        plan=plan,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        boundary_diagnostics=[
            *boundary_diagnostics,
            *malformed_control_diagnostics,
        ],
    )


def _cmd_aggregate_ci_batch_evidence(  # noqa: C901, PLR0911, PLR0912, PLR0915
    args: argparse.Namespace,
    *,
    owner: str,
    name: str,
    created_at: str,
    completed_at: str,
    summary_created_at: str | None = None,
    plan: Mapping[str, object] | None,
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    boundary_diagnostics: Sequence[Mapping[str, object]] = (),
    invalid_plan_context: Mapping[str, object] | None = None,
) -> int:
    """Aggregate execution-batch evidence bundles into final G5 artifacts."""
    manifest_created_at = _ci_aggregate_manifest_created_at(args, created_at)
    if plan is None:
        aggregation = _ci_missing_plan_batch_payloads(
            args,
            owner=owner,
            name=name,
            created_at=manifest_created_at,
            completed_at=completed_at,
            summary_created_at=created_at,
            input_artifacts=(
                _ci_invalid_plan_input_artifacts(
                    args,
                    invalid_plan_context=invalid_plan_context,
                    boundary_diagnostics=boundary_diagnostics,
                )
                if boundary_diagnostics
                else None
            ),
            invalid_plan_context=(
                invalid_plan_context
                if _ci_has_malformed_snapshot_control_diagnostic(
                    boundary_diagnostics
                )
                else None
            ),
        )
        aggregate_manifest = aggregation["aggregate_manifest"]
        if getattr(args, "aggregate_phase", "all") == "evidence":
            _write_final_ci_json(
                Path(_ci_aggregate_manifest_out(args)),
                aggregate_manifest,
            )
            _write_outputs(
                args.github_output,
                {
                    "verdict": "failed",
                    "passed": "false",
                    "aggregate_evidence_manifest_ref": str(
                        aggregate_manifest["artifact-ref"]
                    ),
                    "aggregate_evidence_manifest_payload_digest": (
                        _ci_bound_aggregate_manifest_digest(aggregate_manifest)
                    ),
                    "aggregate_created_at": created_at,
                },
            )
            return 0
        if (
            getattr(args, "aggregate_phase", "all") != "summary"
            and not Path(_ci_aggregate_manifest_out(args)).is_file()
        ):
            _write_final_ci_json(
                Path(_ci_aggregate_manifest_out(args)),
                aggregate_manifest,
            )
        aggregate_summary = aggregation["aggregate_summary"]
        _write_final_ci_json(
            Path(_ci_aggregate_summary_out(args)), aggregate_summary
        )
        _write_outputs(
            args.github_output,
            {
                "verdict": "failed",
                "passed": "false",
                "aggregate_evidence_manifest_ref": str(
                    aggregate_manifest["artifact-ref"]
                ),
                "aggregate_evidence_manifest_payload_digest": (
                    _ci_bound_aggregate_manifest_digest(
                        aggregate_manifest,
                        aggregate_summary,
                    )
                ),
                "aggregate_summary_ref": str(aggregate_summary["artifact-ref"]),
                "aggregate_summary_payload_digest": (
                    ci_validation_aggregate_summary_payload_digest(
                        aggregate_summary
                    )
                ),
                "aggregate_created_at": created_at,
            },
        )
        return 1
    request_detail = None
    try:
        request = _read_optional_json(getattr(args, "request", ""))
    except OSError:
        request = None
        request_detail = DiagnosticDetail.REQUEST_UNREADABLE.value
    except (TypeError, ValueError, json.JSONDecodeError):
        request = None
        request_detail = DiagnosticDetail.REQUEST_MALFORMED.value
    if request is None:
        request_detail = (
            request_detail or DiagnosticDetail.REQUEST_MISSING.value
        )
        aggregation = _ci_invalid_request_batch_payloads(
            args,
            owner=owner,
            name=name,
            created_at=manifest_created_at,
            completed_at=completed_at,
            summary_created_at=created_at,
            plan=plan,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            boundary_diagnostics=boundary_diagnostics,
            request_detail=request_detail,
        )
        return _write_ci_failed_batch_aggregation_outputs(
            args,
            aggregation=aggregation,
            created_at=created_at,
        )
    try:
        validate_ci_validation_request(
            request,
            expected_run_id=str(args.run_id),
            expected_run_attempt=str(args.run_attempt),
            expected_artifact_ref=ci_validation_request_artifact_ref(
                run_id=str(args.run_id),
                run_attempt=str(args.run_attempt),
            ),
        )
    except ContractValidationError:
        aggregation = _ci_invalid_request_batch_payloads(
            args,
            owner=owner,
            name=name,
            created_at=manifest_created_at,
            completed_at=completed_at,
            summary_created_at=created_at,
            plan=plan,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            boundary_diagnostics=boundary_diagnostics,
            request_detail=DiagnosticDetail.REQUEST_SCHEMA_INVALID.value,
        )
        return _write_ci_failed_batch_aggregation_outputs(
            args,
            aggregation=aggregation,
            created_at=created_at,
        )
    malformed_manifest_diagnostics: list[Mapping[str, object]] = []
    try:
        execution_batch_manifest = (
            None
            if getattr(args, "batch_materialization_failed", False)
            else _read_json(Path(str(args.execution_batch_manifest)))
        )
    except OSError as exc:
        detail = (
            DiagnosticDetail.EXECUTION_BATCH_MANIFEST_MISSING.value
            if exc.errno == errno.ENOENT
            else DiagnosticDetail.EXECUTION_BATCH_MANIFEST_UNREADABLE.value
        )
        aggregation = _ci_missing_execution_batch_manifest_payloads(
            args,
            owner=owner,
            name=name,
            created_at=manifest_created_at,
            completed_at=completed_at,
            summary_created_at=created_at,
            plan=plan,
            request=request,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            boundary_diagnostics=[
                *boundary_diagnostics,
                _ci_malformed_control_input_diagnostic(
                    ci_validation_execution_batch_manifest_artifact_ref(
                        run_id=str(args.run_id),
                        run_attempt=str(args.run_attempt),
                    ),
                    detail=detail,
                    message=(
                        "Execution-batch manifest control input is unavailable."
                    ),
                ),
            ],
        )
        return _write_ci_failed_batch_aggregation_outputs(
            args,
            aggregation=aggregation,
            created_at=created_at,
        )
    except (TypeError, ValueError, json.JSONDecodeError):
        aggregation = _ci_missing_execution_batch_manifest_payloads(
            args,
            owner=owner,
            name=name,
            created_at=manifest_created_at,
            completed_at=completed_at,
            summary_created_at=created_at,
            plan=plan,
            request=request,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            boundary_diagnostics=[
                *boundary_diagnostics,
                _ci_malformed_control_input_diagnostic(
                    ci_validation_execution_batch_manifest_artifact_ref(
                        run_id=str(args.run_id),
                        run_attempt=str(args.run_attempt),
                    ),
                    detail=(
                        DiagnosticDetail.EXECUTION_BATCH_MANIFEST_MALFORMED.value
                    ),
                    message=(
                        "Execution-batch manifest control input is malformed."
                    ),
                ),
            ],
        )
        return _write_ci_failed_batch_aggregation_outputs(
            args,
            aggregation=aggregation,
            created_at=created_at,
        )
    if _ci_has_malformed_snapshot_control_diagnostic(boundary_diagnostics):
        execution_batch_manifest = None
        malformed_manifest_diagnostics.append(
            _ci_malformed_control_input_diagnostic(
                ci_validation_execution_batch_manifest_artifact_ref(
                    run_id=str(args.run_id),
                    run_attempt=str(args.run_attempt),
                ),
                detail=DiagnosticDetail.EXECUTION_BATCH_MANIFEST_MALFORMED.value,
                message=(
                    "Execution-batch manifest input is not authoritative "
                    "without valid snapshot controls."
                ),
            )
        )
    if execution_batch_manifest is None:
        try:
            aggregation = _ci_missing_execution_batch_manifest_payloads(
                args,
                owner=owner,
                name=name,
                created_at=manifest_created_at,
                completed_at=completed_at,
                summary_created_at=created_at,
                plan=plan,
                request=request,
                changed_files_snapshot=changed_files_snapshot,
                fact_snapshot=fact_snapshot,
                boundary_diagnostics=[
                    *boundary_diagnostics,
                    *malformed_manifest_diagnostics,
                ],
            )
        except (ContractValidationError, TypeError, ValueError) as exc:
            print(f"error: {exc}", file=sys.stderr)
            return 1
        aggregate_manifest = aggregation["aggregate_manifest"]
        if getattr(args, "aggregate_phase", "all") == "evidence":
            _write_final_ci_json(
                Path(_ci_aggregate_manifest_out(args)),
                aggregate_manifest,
            )
            _write_outputs(
                args.github_output,
                {
                    "verdict": "failed",
                    "passed": "false",
                    "aggregate_evidence_manifest_ref": str(
                        aggregate_manifest["artifact-ref"]
                    ),
                    "aggregate_evidence_manifest_payload_digest": (
                        _ci_bound_aggregate_manifest_digest(aggregate_manifest)
                    ),
                    "aggregate_created_at": created_at,
                },
            )
            return 0
        if (
            getattr(args, "aggregate_phase", "all") != "summary"
            and not Path(_ci_aggregate_manifest_out(args)).is_file()
        ):
            _write_final_ci_json(
                Path(_ci_aggregate_manifest_out(args)),
                aggregate_manifest,
            )
        aggregate_summary = aggregation["aggregate_summary"]
        _write_final_ci_json(
            Path(_ci_aggregate_summary_out(args)), aggregate_summary
        )
        _write_outputs(
            args.github_output,
            {
                "verdict": "failed",
                "passed": "false",
                "aggregate_evidence_manifest_ref": str(
                    aggregate_manifest["artifact-ref"]
                ),
                "aggregate_evidence_manifest_payload_digest": (
                    _ci_bound_aggregate_manifest_digest(
                        aggregate_manifest,
                        aggregate_summary,
                    )
                ),
                "aggregate_summary_ref": str(aggregate_summary["artifact-ref"]),
                "aggregate_summary_payload_digest": (
                    ci_validation_aggregate_summary_payload_digest(
                        aggregate_summary
                    )
                ),
                "aggregate_created_at": created_at,
            },
        )
        return 1
    try:
        validate_ci_validation_execution_batch_manifest(
            execution_batch_manifest,
            plan=plan,
            request=request,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            expected_run_id=str(args.run_id),
            expected_run_attempt=str(args.run_attempt),
        )
        aggregation = _ci_batch_aggregation_payloads(
            args,
            owner=owner,
            name=name,
            created_at=manifest_created_at,
            completed_at=completed_at,
            summary_created_at=created_at,
            plan=plan,
            request=request,
            execution_batch_manifest=execution_batch_manifest,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            boundary_diagnostics=boundary_diagnostics,
        )
    except ContractValidationError as exc:
        manifest_detail = _ci_execution_batch_manifest_validation_detail(
            exc,
            execution_batch_manifest,
        )
        try:
            aggregation = _ci_missing_execution_batch_manifest_payloads(
                args,
                owner=owner,
                name=name,
                created_at=manifest_created_at,
                completed_at=completed_at,
                summary_created_at=created_at,
                plan=plan,
                request=request,
                changed_files_snapshot=changed_files_snapshot,
                fact_snapshot=fact_snapshot,
                boundary_diagnostics=[
                    *boundary_diagnostics,
                    _ci_execution_batch_manifest_validation_diagnostic(
                        ci_validation_execution_batch_manifest_artifact_ref(
                            run_id=str(args.run_id),
                            run_attempt=str(args.run_attempt),
                        ),
                        detail=manifest_detail,
                        message=(
                            "Execution-batch manifest control input is "
                            "contract-invalid."
                        ),
                    ),
                ],
            )
        except (ContractValidationError, TypeError, ValueError) as exc:
            print(f"error: {exc}", file=sys.stderr)
            return 1
    except (RuntimeError, TypeError, ValueError, json.JSONDecodeError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1
    aggregate_manifest = aggregation["aggregate_manifest"]
    if getattr(args, "aggregate_phase", "all") == "evidence":
        _write_final_ci_json(
            Path(_ci_aggregate_manifest_out(args)),
            aggregate_manifest,
        )
        _write_outputs(
            args.github_output,
            {
                "verdict": "failed",
                "passed": "false",
                "aggregate_evidence_manifest_ref": str(
                    aggregate_manifest["artifact-ref"]
                ),
                "aggregate_evidence_manifest_payload_digest": (
                    _ci_bound_aggregate_manifest_digest(aggregate_manifest)
                ),
                "aggregate_created_at": created_at,
            },
        )
        return 0
    if (
        getattr(args, "aggregate_phase", "all") != "summary"
        and not Path(_ci_aggregate_manifest_out(args)).is_file()
    ):
        _write_final_ci_json(
            Path(_ci_aggregate_manifest_out(args)),
            aggregate_manifest,
        )
    aggregate_summary = aggregation["aggregate_summary"]
    _write_final_ci_json(
        Path(_ci_aggregate_summary_out(args)), aggregate_summary
    )
    _write_outputs(
        args.github_output,
        {
            "verdict": str(aggregate_summary["verdict"]),
            "passed": _bool_str(aggregate_summary["verdict"] == "passed"),
            "aggregate_evidence_manifest_ref": str(
                aggregate_manifest["artifact-ref"]
            ),
            "aggregate_evidence_manifest_payload_digest": (
                _ci_bound_aggregate_manifest_digest(
                    aggregate_manifest,
                    aggregate_summary,
                )
            ),
            "aggregate_summary_ref": str(aggregate_summary["artifact-ref"]),
            "aggregate_summary_payload_digest": (
                ci_validation_aggregate_summary_payload_digest(
                    aggregate_summary
                )
            ),
            "aggregate_created_at": created_at,
        },
    )
    return 0 if aggregate_summary["verdict"] == "passed" else 1


def _ci_aggregate_manifest_out(args: argparse.Namespace) -> str:
    return str(getattr(args, "aggregate_evidence_manifest_out", ""))


def _write_final_ci_json(path: Path, document: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(canonical_json_bytes(document))


def _ci_aggregate_summary_out(args: argparse.Namespace) -> str:
    return str(args.aggregate_summary_out)


def _ci_aggregate_manifest_created_at(
    args: argparse.Namespace,
    fallback_created_at: str,
) -> str:
    if getattr(args, "aggregate_phase", "all") != "summary":
        return fallback_created_at
    try:
        preserved = json.loads(
            Path(_ci_aggregate_manifest_out(args)).read_text(encoding="utf-8")
        )
    except (OSError, TypeError, ValueError, json.JSONDecodeError):
        return fallback_created_at
    if not isinstance(preserved, Mapping):
        return fallback_created_at
    created_at = preserved.get("created-at")
    if not isinstance(created_at, str):
        return fallback_created_at
    if not _ci_is_contract_valid_rfc3339_timestamp(created_at):
        return fallback_created_at
    return created_at


_CI_CONTRACT_RFC3339_TIMESTAMP_RE = re.compile(
    r"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}"
    r"(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$",
)


def _ci_is_contract_valid_rfc3339_timestamp(value: str) -> bool:
    if _CI_CONTRACT_RFC3339_TIMESTAMP_RE.fullmatch(value) is None:
        return False
    try:
        datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        return False
    return True


def _ci_bound_aggregate_manifest_digest(
    aggregate_manifest: Mapping[str, object],
    aggregate_summary: Mapping[str, object] | None = None,
) -> str:
    if aggregate_summary is not None:
        manifest_claim = aggregate_summary.get("aggregate-evidence-manifest")
        if isinstance(manifest_claim, Mapping):
            digest = manifest_claim.get("content-digest")
            if isinstance(digest, str) and digest:
                return digest
            return ""
    return ci_validation_aggregate_evidence_manifest_payload_digest(
        aggregate_manifest
    )


def _ci_summary_aggregate_manifest_authority(
    args: argparse.Namespace,
    *,
    recomputed_manifest: Mapping[str, object],
    plan: Mapping[str, object] | None,
    execution_batch_manifest: Mapping[str, object] | None,
    request: Mapping[str, object] | None,
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
) -> dict[str, object]:
    path = Path(_ci_aggregate_manifest_out(args))
    diagnostics: list[Mapping[str, object]] = []
    raw_bytes: bytes | None = None
    try:
        raw_bytes = path.read_bytes()
        raw_digest = hashlib.sha256(raw_bytes).hexdigest()
        preserved = json.loads(raw_bytes.decode("utf-8"))
        if not isinstance(preserved, dict):
            diagnostics.append(
                _ci_aggregate_manifest_authority_diagnostic(
                    DiagnosticDetail.AGGREGATE_EVIDENCE_MANIFEST_MALFORMED.value,
                    "Preserved aggregate evidence manifest is malformed.",
                )
            )
            manifest_claim = _ci_aggregate_manifest_claim(recomputed_manifest)
            return {
                "manifest": manifest_claim,
                "content_digest": raw_digest,
                "manifest_document": None,
                "diagnostics": diagnostics,
            }
        preserved_manifest_claim = _ci_aggregate_manifest_claim(
            preserved,
            fallback_manifest=recomputed_manifest,
        )
        recomputed_manifest_claim = _ci_aggregate_manifest_claim(
            recomputed_manifest
        )
        preserved_is_canonical = raw_bytes == canonical_json_bytes(preserved)
        if not preserved_is_canonical:
            diagnostics.append(
                _ci_aggregate_manifest_authority_diagnostic(
                    DiagnosticDetail.AGGREGATE_EVIDENCE_MANIFEST_NON_CANONICAL.value,
                    (
                        "Preserved aggregate evidence manifest bytes are "
                        "not canonical."
                    ),
                )
            )
        if preserved != recomputed_manifest:
            diagnostics.append(
                _ci_aggregate_manifest_authority_diagnostic(
                    DiagnosticDetail.AGGREGATE_EVIDENCE_MANIFEST_DIGEST_MISMATCH.value,
                    (
                        "Preserved aggregate evidence manifest differs "
                        "from the recomputed validation view."
                    ),
                )
            )
        preserved_is_valid = _ci_preserved_aggregate_manifest_is_valid(
            args,
            preserved,
            plan=plan,
            execution_batch_manifest=execution_batch_manifest,
            request=request,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
        )
        preserved_has_structural_authority = (
            _ci_preserved_aggregate_manifest_has_structural_authority(
                args,
                preserved,
            )
        )
        if not preserved_is_valid:
            diagnostics.append(
                _ci_aggregate_manifest_authority_diagnostic(
                    DiagnosticDetail.AGGREGATE_EVIDENCE_MANIFEST_MALFORMED.value,
                    (
                        "Preserved aggregate evidence manifest is "
                        "contract-invalid."
                    ),
                )
            )
        if (
            preserved_is_canonical
            and preserved_has_structural_authority
            and preserved_is_valid
            and preserved == recomputed_manifest
        ):
            return {
                "manifest": preserved,
                "content_digest": raw_digest,
                "manifest_document": preserved,
                "diagnostics": diagnostics,
            }
        manifest_claim = (
            preserved_manifest_claim
            if preserved_is_valid and preserved_has_structural_authority
            else recomputed_manifest_claim
        )
        return {  # noqa: TRY300
            "manifest": manifest_claim,
            "content_digest": raw_digest,
            "manifest_document": None,
            "diagnostics": diagnostics,
        }
    except FileNotFoundError:
        diagnostics.append(
            _ci_aggregate_manifest_authority_diagnostic(
                DiagnosticDetail.AGGREGATE_EVIDENCE_MANIFEST_MISSING.value,
                "Preserved aggregate evidence manifest is missing.",
            )
        )
    except OSError:
        diagnostics.append(
            _ci_aggregate_manifest_authority_diagnostic(
                DiagnosticDetail.AGGREGATE_EVIDENCE_MANIFEST_UNREADABLE.value,
                "Preserved aggregate evidence manifest is unreadable.",
            )
        )
    except (UnicodeDecodeError, TypeError, ValueError, json.JSONDecodeError):
        content_digest = (
            hashlib.sha256(raw_bytes).hexdigest() if raw_bytes else None
        )
        diagnostics.append(
            _ci_aggregate_manifest_authority_diagnostic(
                DiagnosticDetail.AGGREGATE_EVIDENCE_MANIFEST_MALFORMED.value,
                "Preserved aggregate evidence manifest is malformed.",
            )
        )
        return {
            "manifest": _ci_aggregate_manifest_claim(recomputed_manifest),
            "content_digest": content_digest,
            "manifest_document": None,
            "diagnostics": diagnostics,
        }
    return {
        "manifest": _ci_aggregate_manifest_claim(recomputed_manifest),
        "content_digest": None,
        "manifest_document": None,
        "diagnostics": diagnostics,
    }


def _ci_aggregate_manifest_claim(
    manifest: Mapping[str, object],
    *,
    fallback_manifest: Mapping[str, object] | None = None,
) -> dict[str, object]:
    artifact_ref = manifest.get("artifact-ref")
    if isinstance(artifact_ref, str):
        return {"artifact-ref": artifact_ref}
    if fallback_manifest is not None:
        return _ci_aggregate_manifest_claim(fallback_manifest)
    return {"artifact-ref": None}


def _ci_preserved_aggregate_manifest_is_valid(
    args: argparse.Namespace,
    preserved: Mapping[str, object],
    *,
    plan: Mapping[str, object] | None,
    execution_batch_manifest: Mapping[str, object] | None,
    request: Mapping[str, object] | None,
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
) -> bool:
    try:
        validate_ci_validation_aggregate_evidence_manifest(
            preserved,
            plan=plan,
            execution_batch_manifest=execution_batch_manifest,
            request=request,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            expected_run_id=str(args.run_id),
            expected_run_attempt=str(args.run_attempt),
        )
    except ContractValidationError:
        return False
    return True


def _ci_preserved_aggregate_manifest_has_structural_authority(
    args: argparse.Namespace,
    preserved: Mapping[str, object],
) -> bool:
    artifact_ref = preserved.get("artifact-ref")
    if not isinstance(artifact_ref, str):
        return False
    expected_ref = ci_validation_aggregate_evidence_manifest_artifact_ref(
        run_id=str(args.run_id),
        run_attempt=str(args.run_attempt),
    )
    if artifact_ref != expected_ref:
        return False
    try:
        artifact_physical_name(artifact_ref)
    except ContractValidationError:
        return False
    return True


def _ci_aggregate_summary_without_manifest_diagnostic() -> Mapping[str, object]:
    return _ci_aggregate_diagnostic(
        "aggregate-summary-without-manifest",
        code=DiagnosticFamily.AGGREGATE_SUMMARY_WITHOUT_MANIFEST.value,
        detail=DiagnosticDetail.AGGREGATE_SUMMARY_WITHOUT_MANIFEST.value,
        message="Aggregate summary was generated without final manifest bytes.",
        source_id=None,
        severity=DiagnosticSeverity.FAIL_CLOSED.value,
        verdict_effect=DiagnosticVerdictEffect.FAIL_CLOSED.value,
    )


def _ci_aggregate_manifest_authority_diagnostic(
    detail: str,
    message: str,
) -> Mapping[str, object]:
    return _ci_aggregate_diagnostic(
        f"final-evidence-failure/{detail}",
        code=DiagnosticFamily.FINAL_EVIDENCE_FAILURE.value,
        detail=detail,
        message=message,
        source_id=None,
        severity=DiagnosticSeverity.FAIL_CLOSED.value,
        verdict_effect=DiagnosticVerdictEffect.FAIL_CLOSED.value,
    )


def _write_ci_failed_batch_aggregation_outputs(
    args: argparse.Namespace,
    *,
    aggregation: Mapping[str, Mapping[str, object]],
    created_at: str,
) -> int:
    aggregate_manifest = aggregation["aggregate_manifest"]
    if getattr(args, "aggregate_phase", "all") == "evidence":
        _write_final_ci_json(
            Path(_ci_aggregate_manifest_out(args)),
            aggregate_manifest,
        )
        _write_outputs(
            args.github_output,
            {
                "verdict": "failed",
                "passed": "false",
                "aggregate_evidence_manifest_ref": str(
                    aggregate_manifest["artifact-ref"]
                ),
                "aggregate_evidence_manifest_payload_digest": (
                    _ci_bound_aggregate_manifest_digest(aggregate_manifest)
                ),
                "aggregate_created_at": created_at,
            },
        )
        return 0
    if (
        getattr(args, "aggregate_phase", "all") != "summary"
        and not Path(_ci_aggregate_manifest_out(args)).is_file()
    ):
        _write_final_ci_json(
            Path(_ci_aggregate_manifest_out(args)),
            aggregate_manifest,
        )
    aggregate_summary = aggregation["aggregate_summary"]
    _write_final_ci_json(
        Path(_ci_aggregate_summary_out(args)), aggregate_summary
    )
    _write_outputs(
        args.github_output,
        {
            "verdict": "failed",
            "passed": "false",
            "aggregate_evidence_manifest_ref": str(
                aggregate_manifest["artifact-ref"]
            ),
            "aggregate_evidence_manifest_payload_digest": (
                _ci_bound_aggregate_manifest_digest(
                    aggregate_manifest,
                    aggregate_summary,
                )
            ),
            "aggregate_summary_ref": str(aggregate_summary["artifact-ref"]),
            "aggregate_summary_payload_digest": (
                ci_validation_aggregate_summary_payload_digest(
                    aggregate_summary
                )
            ),
            "aggregate_created_at": created_at,
        },
    )
    return 1


def _ci_required_aggregate_manifest_artifact_id(
    args: argparse.Namespace,
) -> str:
    artifact_id = getattr(args, "aggregate_evidence_manifest_artifact_id", None)
    if not isinstance(artifact_id, str) or not artifact_id:
        msg = (
            "--aggregate-evidence-manifest-artifact-id is required before "
            "generating the aggregate summary"
        )
        raise RuntimeError(msg)
    return artifact_id


def _ci_aggregate_manifest_producer_verified(args: argparse.Namespace) -> bool:
    return bool(
        getattr(args, "aggregate_evidence_manifest_producer_verified", True)
    )


def _ci_batch_aggregation_payloads(
    args: argparse.Namespace,
    *,
    owner: str,
    name: str,
    created_at: str,
    completed_at: str | None = None,
    summary_created_at: str | None = None,
    plan: Mapping[str, object],
    request: Mapping[str, object],
    execution_batch_manifest: Mapping[str, object],
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    boundary_diagnostics: Sequence[Mapping[str, object]] = (),
) -> dict[str, Mapping[str, object]]:
    summary_created_at = summary_created_at or created_at
    completed_at = completed_at or summary_created_at
    observed_dir = str(getattr(args, "observed_artifacts_dir", ""))
    input_artifacts = _ci_aggregate_input_artifacts(
        args,
        plan=plan,
        request=request,
        execution_batch_manifest=execution_batch_manifest,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        boundary_diagnostics=boundary_diagnostics,
    )
    _ci_close_snapshot_input_authority(input_artifacts)
    required_input_failure = _ci_aggregate_required_input_failure(
        input_artifacts,
    )
    authoritative_snapshots = _ci_aggregate_input_is_valid(
        input_artifacts,
        "changed-files-snapshot",
    ) and _ci_aggregate_input_is_valid(input_artifacts, "fact-snapshot")
    authoritative_request = (
        request
        if authoritative_snapshots
        and _ci_aggregate_input_is_valid(input_artifacts, "request")
        else None
    )
    authoritative_changed_files_snapshot = (
        changed_files_snapshot if authoritative_snapshots else None
    )
    authoritative_fact_snapshot = (
        fact_snapshot if authoritative_snapshots else None
    )
    bundle_slots, admitted_bundles, unexpected = _ci_aggregate_batch_slots(
        plan=plan,
        request=authoritative_request or request,
        execution_batch_manifest=execution_batch_manifest,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        input_artifacts=input_artifacts,
        observed_artifacts_dir=observed_dir,
        run_id=str(args.run_id),
        run_attempt=str(args.run_attempt),
        admit_valid_bundles=not required_input_failure,
    )
    pre_final_count = _ci_aggregate_pre_final_input_count(
        input_artifacts
    ) + len(bundle_slots)
    namespace_enumeration_unavailable, downloader_namespace_overflow = (
        _ci_aggregate_downloader_namespace_observation(
            Path(observed_dir) if observed_dir else None,
            expected_run_id=str(args.run_id),
            expected_run_attempt=str(args.run_attempt),
        )
    )
    namespace_overflow = _ci_aggregate_namespace_overflow(
        pre_final_count + len(unexpected),
        enumeration_unavailable=namespace_enumeration_unavailable,
        downloader_observed_overflow=downloader_namespace_overflow,
    )
    aggregate_duration_seconds = _ci_aggregate_duration_seconds(
        str(getattr(args, "started_at", "") or created_at),
        completed_at,
    )
    budgets = _ci_aggregate_summary_budgets(
        execution_batch_manifest,
        pre_final_validation_artifacts=pre_final_count,
        aggregate_duration_seconds=aggregate_duration_seconds,
    )
    aggregate_manifest_producer_verified = (
        _ci_aggregate_manifest_producer_verified(args)
    )
    aggregate_manifest_authority_diagnostics = ()
    aggregate_manifest = _freeze_ci_validation_aggregate_evidence_manifest(
        created_at=created_at,
        repository_owner=owner,
        repository_name=name,
        workflow=str(args.workflow),
        run_id=str(args.run_id),
        run_attempt=str(args.run_attempt),
        input_artifacts=input_artifacts,
        batch_bundles=bundle_slots,
        unexpected_contract_artifacts=unexpected,
        namespace_overflow=namespace_overflow,
        pre_final_validation_artifacts=pre_final_count,
        namespace_closed_at=created_at,
        plan=plan,
        execution_batch_manifest=execution_batch_manifest,
        request=request,
        changed_files_snapshot=authoritative_changed_files_snapshot,
        fact_snapshot=authoritative_fact_snapshot,
        _require_authoritative_snapshot_inputs=not required_input_failure,
    )
    aggregate_manifest_document: Mapping[str, object] | None = (
        aggregate_manifest
    )
    if getattr(args, "aggregate_phase", "all") == "evidence":
        if not required_input_failure:
            validate_ci_validation_aggregate_evidence_manifest(
                aggregate_manifest,
                plan=plan,
                execution_batch_manifest=execution_batch_manifest,
                request=authoritative_request,
                changed_files_snapshot=authoritative_changed_files_snapshot,
                fact_snapshot=authoritative_fact_snapshot,
                expected_run_id=str(args.run_id),
                expected_run_attempt=str(args.run_attempt),
            )
        return {"aggregate_manifest": aggregate_manifest}
    aggregate_manifest_digest = (
        ci_validation_aggregate_evidence_manifest_payload_digest(
            aggregate_manifest
        )
    )
    if getattr(args, "aggregate_phase", "all") == "summary":
        manifest_authority = _ci_summary_aggregate_manifest_authority(
            args,
            recomputed_manifest=aggregate_manifest,
            plan=plan,
            execution_batch_manifest=execution_batch_manifest,
            request=authoritative_request,
            changed_files_snapshot=authoritative_changed_files_snapshot,
            fact_snapshot=authoritative_fact_snapshot,
        )
        aggregate_manifest = cast(
            "Mapping[str, object]",
            manifest_authority["manifest"],
        )
        aggregate_manifest_digest = cast(
            "str | None",
            manifest_authority["content_digest"],
        )
        aggregate_manifest_document = cast(
            "Mapping[str, object] | None",
            manifest_authority["manifest_document"],
        )
        aggregate_manifest_authority_diagnostics = cast(
            "Sequence[Mapping[str, object]]",
            manifest_authority["diagnostics"],
        )
        aggregate_manifest_authority_diagnostics = tuple(
            sorted(
                aggregate_manifest_authority_diagnostics,
                key=lambda item: str(item.get("diagnostic-id")),
            )
        )
        if aggregate_manifest_authority_diagnostics:
            aggregate_manifest_producer_verified = False
    evidence_results = _ci_aggregate_evidence_results(
        plan=plan,
        admitted_bundles=admitted_bundles,
    )
    summary_bundle_rows = _ci_aggregate_summary_bundle_rows(
        bundle_slots,
        admitted_bundles=admitted_bundles,
    )
    manifest_binding_producer_verified = (
        aggregate_manifest_producer_verified
        or aggregate_manifest_digest is None
    )
    aggregate_summary_without_manifest = (
        aggregate_manifest_document is None
        and (
            aggregate_manifest_digest is None
            or not aggregate_manifest_authority_diagnostics
        )
    )
    summary_required_input_failure = (
        required_input_failure and aggregate_manifest_document is not None
    )
    summary_namespace_overflow: Mapping[str, object] = (
        namespace_overflow if aggregate_manifest_document is not None else {}
    )
    summary_unexpected = (
        unexpected if aggregate_manifest_document is not None else []
    )
    failures = _ci_aggregate_summary_failures(
        summary_bundle_rows=summary_bundle_rows,
        evidence_results=evidence_results,
        namespace_overflow=summary_namespace_overflow,
        unexpected_artifacts=summary_unexpected,
        required_input_failure=summary_required_input_failure,
        aggregate_manifest_producer_verified=(
            manifest_binding_producer_verified
        ),
        aggregate_manifest_authority_diagnostics=(
            aggregate_manifest_authority_diagnostics
        ),
        aggregate_summary_without_manifest=aggregate_summary_without_manifest,
    )
    diagnostics = sorted(
        [
            failure["diagnostic"]
            for failure in failures
            if isinstance(failure.get("diagnostic"), Mapping)
        ],
        key=lambda item: str(item.get("diagnostic-id")),
    )
    reason = _ci_aggregate_summary_reason(
        summary_bundle_rows=summary_bundle_rows,
        evidence_results=evidence_results,
        namespace_overflow=summary_namespace_overflow,
        unexpected_artifacts=summary_unexpected,
        required_input_failure=summary_required_input_failure,
        aggregate_manifest_producer_verified=(
            manifest_binding_producer_verified
        ),
        aggregate_manifest_authority_failure=bool(
            aggregate_manifest_authority_diagnostics
        ),
        aggregate_summary_without_manifest=aggregate_summary_without_manifest,
    )
    aggregate_summary = freeze_ci_validation_aggregate_summary(
        created_at=summary_created_at,
        repository_owner=owner,
        repository_name=name,
        workflow=str(args.workflow),
        run_id=str(args.run_id),
        run_attempt=str(args.run_attempt),
        aggregate_evidence_manifest={
            "artifact-ref": aggregate_manifest["artifact-ref"],
            "artifact-instance-id": (
                _ci_required_aggregate_manifest_artifact_id(args)
                if aggregate_manifest_digest is not None
                else None
            ),
            "content-digest": aggregate_manifest_digest,
        },
        final_artifacts={
            "aggregate-evidence-manifest": {
                "artifact-ref": aggregate_manifest["artifact-ref"],
                "artifact-instance-id": (
                    _ci_required_aggregate_manifest_artifact_id(args)
                    if aggregate_manifest_digest is not None
                    else None
                ),
                "content-digest": aggregate_manifest_digest,
                "producer-verified": aggregate_manifest_producer_verified,
                "authority-diagnostics": list(
                    aggregate_manifest_authority_diagnostics
                ),
            },
            "aggregate-summary": {
                "artifact-ref": ci_validation_aggregate_summary_artifact_ref(
                    run_id=str(args.run_id),
                    run_attempt=str(args.run_attempt),
                ),
            },
        },
        validation_tree=cast("Mapping[str, object]", plan["validation-tree"]),
        affected_range=_ci_summary_affected_range(plan),
        request=_ci_aggregate_manifest_request_summary(input_artifacts),
        scheduled_full=cast("Mapping[str, object]", plan["scheduled-full"]),
        verdict="failed" if any(reason.values()) else "passed",
        reason=reason,
        budgets=budgets,
        diagnostics=diagnostics,
        batch_bundles=summary_bundle_rows,
        evidence_results=evidence_results,
        failures=failures,
        work_groups=_ci_aggregate_work_group_counts(evidence_results),
        plan=plan,
        aggregate_evidence_manifest_document=aggregate_manifest_document,
        admitted_batch_evidence_bundles=(
            admitted_bundles
            if aggregate_manifest_document is not None
            else None
        ),
        execution_batch_manifest=execution_batch_manifest,
        request_document=authoritative_request,
        changed_files_snapshot=authoritative_changed_files_snapshot,
        fact_snapshot=authoritative_fact_snapshot,
    )
    if not required_input_failure and aggregate_manifest_document is not None:
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest_document,
            plan=plan,
            execution_batch_manifest=execution_batch_manifest,
            request=authoritative_request,
            changed_files_snapshot=authoritative_changed_files_snapshot,
            fact_snapshot=authoritative_fact_snapshot,
            expected_run_id=str(args.run_id),
            expected_run_attempt=str(args.run_attempt),
        )
    if not required_input_failure:
        validate_ci_validation_aggregate_summary(
            aggregate_summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest_document,
            admitted_batch_evidence_bundles=(
                admitted_bundles
                if aggregate_manifest_document is not None
                else None
            ),
            execution_batch_manifest=execution_batch_manifest,
            request=authoritative_request,
            changed_files_snapshot=authoritative_changed_files_snapshot,
            fact_snapshot=authoritative_fact_snapshot,
            expected_run_id=str(args.run_id),
            expected_run_attempt=str(args.run_attempt),
        )
    return {
        "aggregate_manifest": aggregate_manifest,
        "aggregate_summary": aggregate_summary,
    }


def _ci_missing_plan_batch_payloads(
    args: argparse.Namespace,
    *,
    owner: str,
    name: str,
    created_at: str,
    completed_at: str | None = None,
    summary_created_at: str | None = None,
    input_artifacts: Mapping[str, object] | None = None,
    invalid_plan_context: Mapping[str, object] | None = None,
) -> dict[str, Mapping[str, object]]:
    summary_created_at = summary_created_at or created_at
    completed_at = completed_at or summary_created_at
    run_id = str(args.run_id)
    run_attempt = str(args.run_attempt)
    aggregate_input_artifacts: dict[str, object] = (
        dict(input_artifacts)
        if input_artifacts is not None
        else {
            "request": _ci_aggregate_input_artifact(
                ci_validation_request_artifact_ref(
                    run_id=run_id,
                    run_attempt=run_attempt,
                ),
                content_digest=None,
                required=True,
                artifact_instance_id=None,
                require_artifact_instance_id=True,
            ),
            "validation-plan": _ci_aggregate_input_artifact(
                ci_validation_plan_artifact_ref(
                    run_id=run_id,
                    run_attempt=run_attempt,
                ),
                content_digest=None,
                required=True,
                artifact_instance_id=None,
                require_artifact_instance_id=True,
            ),
            "changed-files-snapshot": _ci_aggregate_input_artifact(
                None,
                content_digest=None,
                required=False,
                artifact_instance_id=None,
            ),
            "fact-snapshot": _ci_aggregate_input_artifact(
                None,
                content_digest=None,
                required=False,
                artifact_instance_id=None,
            ),
            "execution-batch-manifest": _ci_aggregate_input_artifact(
                ci_validation_execution_batch_manifest_artifact_ref(
                    run_id=run_id,
                    run_attempt=run_attempt,
                ),
                content_digest=None,
                required=True,
                artifact_instance_id=None,
                require_artifact_instance_id=True,
            ),
        }
    )
    pre_final_count = _ci_aggregate_pre_final_input_count(
        aggregate_input_artifacts
    )
    observed_dir = str(getattr(args, "observed_artifacts_dir", ""))
    root = Path(observed_dir) if observed_dir else None
    unexpected = _ci_aggregate_unexpected_artifacts(
        root,
        run_id=run_id,
        run_attempt=run_attempt,
        expected_refs=_ci_aggregate_allowed_observed_refs(
            input_artifacts=aggregate_input_artifacts,
            expected_batch_refs=set(),
            run_id=run_id,
            run_attempt=run_attempt,
        ),
        expected_batch_refs=set(),
        max_unexpected_artifacts=_ci_aggregate_unexpected_artifact_sentinel_count(
            pre_final_count,
        ),
    )
    namespace_enumeration_unavailable, downloader_namespace_overflow = (
        _ci_aggregate_downloader_namespace_observation(
            root,
            expected_run_id=run_id,
            expected_run_attempt=run_attempt,
        )
    )
    namespace_overflow = _ci_aggregate_namespace_overflow(
        pre_final_count + len(unexpected),
        enumeration_unavailable=namespace_enumeration_unavailable,
        downloader_observed_overflow=downloader_namespace_overflow,
    )
    aggregate_manifest = _freeze_ci_validation_aggregate_evidence_manifest(
        created_at=created_at,
        repository_owner=owner,
        repository_name=name,
        workflow=str(args.workflow),
        run_id=run_id,
        run_attempt=run_attempt,
        input_artifacts=aggregate_input_artifacts,
        batch_bundles=[],
        unexpected_contract_artifacts=unexpected,
        namespace_overflow=namespace_overflow,
        pre_final_validation_artifacts=pre_final_count,
        namespace_closed_at=created_at,
        plan=None,
        execution_batch_manifest=None,
        request=None,
        changed_files_snapshot=None,
        fact_snapshot=None,
        _require_authoritative_snapshot_inputs=False,
    )
    if getattr(args, "aggregate_phase", "all") == "evidence":
        return {"aggregate_manifest": aggregate_manifest}
    aggregate_manifest_document: Mapping[str, object] | None = (
        aggregate_manifest
    )
    aggregate_manifest_digest: str | None = (
        ci_validation_aggregate_evidence_manifest_payload_digest(
            aggregate_manifest
        )
    )
    aggregate_manifest_producer_verified = (
        _ci_aggregate_manifest_producer_verified(args)
    )
    aggregate_manifest_authority_diagnostics: Sequence[
        Mapping[str, object]
    ] = ()
    if getattr(args, "aggregate_phase", "all") == "summary":
        manifest_authority = _ci_summary_aggregate_manifest_authority(
            args,
            recomputed_manifest=aggregate_manifest,
            plan=None,
            execution_batch_manifest=None,
            request=None,
            changed_files_snapshot=None,
            fact_snapshot=None,
        )
        aggregate_manifest = cast(
            "Mapping[str, object]",
            manifest_authority["manifest"],
        )
        aggregate_manifest_digest = cast(
            "str | None",
            manifest_authority["content_digest"],
        )
        aggregate_manifest_document = cast(
            "Mapping[str, object] | None",
            manifest_authority["manifest_document"],
        )
        aggregate_manifest_authority_diagnostics = cast(
            "Sequence[Mapping[str, object]]",
            manifest_authority["diagnostics"],
        )
        aggregate_manifest_authority_diagnostics = tuple(
            sorted(
                aggregate_manifest_authority_diagnostics,
                key=lambda item: str(item.get("diagnostic-id")),
            )
        )
        if aggregate_manifest_authority_diagnostics:
            aggregate_manifest_producer_verified = False
    budgets = _ci_missing_execution_manifest_budgets(
        pre_final_validation_artifacts=pre_final_count,
        aggregate_duration_seconds=_ci_aggregate_duration_seconds(
            str(getattr(args, "started_at", "") or created_at),
            completed_at,
        ),
    )
    manifest_binding_producer_verified = (
        aggregate_manifest_producer_verified
        or aggregate_manifest_digest is None
    )
    aggregate_summary_without_manifest = (
        aggregate_manifest_document is None
        and (
            aggregate_manifest_digest is None
            or not aggregate_manifest_authority_diagnostics
        )
    )
    summary_namespace_overflow: Mapping[str, object] = (
        namespace_overflow if aggregate_manifest_document is not None else {}
    )
    summary_unexpected = (
        unexpected if aggregate_manifest_document is not None else []
    )
    final_failures = _ci_aggregate_summary_failures(
        summary_bundle_rows=[],
        evidence_results=[],
        namespace_overflow=summary_namespace_overflow,
        unexpected_artifacts=summary_unexpected,
        required_input_failure=False,
        aggregate_manifest_producer_verified=(
            manifest_binding_producer_verified
        ),
        aggregate_manifest_authority_diagnostics=(
            aggregate_manifest_authority_diagnostics
        ),
        aggregate_summary_without_manifest=aggregate_summary_without_manifest,
    )
    reason = {
        "invalid-plan": True,
        "fail-closed": any(
            failure.get("kind") == "fail-closed" for failure in final_failures
        ),
        "required-evidence-missing": False,
        "required-evidence-skipped": False,
        "blocking-validation-failure": False,
        "inadmissible-batch-evidence": False,
        "namespace-closure-failure": False,
        "required-input-artifact-failure": False,
        "aggregate-summary-without-manifest": aggregate_summary_without_manifest,
        "final-producer-unverified": not manifest_binding_producer_verified,
        "final-evidence-failure": any(
            failure.get("kind") == "final-evidence-failure"
            for failure in final_failures
        ),
    }
    diagnostics = sorted(
        [
            failure["diagnostic"]
            for failure in final_failures
            if isinstance(failure.get("diagnostic"), Mapping)
        ],
        key=lambda item: str(item.get("diagnostic-id")),
    )
    aggregate_summary = freeze_ci_validation_aggregate_summary(
        created_at=summary_created_at,
        repository_owner=owner,
        repository_name=name,
        workflow=str(args.workflow),
        run_id=run_id,
        run_attempt=run_attempt,
        aggregate_evidence_manifest={
            "artifact-ref": aggregate_manifest["artifact-ref"],
            "artifact-instance-id": (
                _ci_required_aggregate_manifest_artifact_id(args)
                if aggregate_manifest_digest is not None
                else None
            ),
            "content-digest": aggregate_manifest_digest,
        },
        final_artifacts={
            "aggregate-evidence-manifest": {
                "artifact-ref": aggregate_manifest["artifact-ref"],
                "artifact-instance-id": (
                    _ci_required_aggregate_manifest_artifact_id(args)
                    if aggregate_manifest_digest is not None
                    else None
                ),
                "content-digest": aggregate_manifest_digest,
                "producer-verified": aggregate_manifest_producer_verified,
                "authority-diagnostics": list(
                    aggregate_manifest_authority_diagnostics
                ),
            },
            "aggregate-summary": {
                "artifact-ref": ci_validation_aggregate_summary_artifact_ref(
                    run_id=run_id,
                    run_attempt=run_attempt,
                ),
            },
        },
        validation_tree=(
            cast(
                "Mapping[str, object]", invalid_plan_context["validation-tree"]
            )
            if invalid_plan_context is not None
            else {}
        ),
        affected_range=(
            _ci_summary_affected_range(invalid_plan_context)
            if invalid_plan_context is not None
            else {}
        ),
        request=(
            cast("Mapping[str, object]", invalid_plan_context["request"])
            if invalid_plan_context is not None
            else {}
        ),
        scheduled_full=(
            cast("Mapping[str, object]", invalid_plan_context["scheduled-full"])
            if invalid_plan_context is not None
            else {}
        ),
        verdict="failed",
        reason=reason,
        budgets=budgets,
        diagnostics=diagnostics,
        batch_bundles=[],
        evidence_results=[],
        failures=final_failures,
        work_groups=_ci_aggregate_work_group_counts([]),
        plan=invalid_plan_context,
        aggregate_evidence_manifest_document=aggregate_manifest_document,
        admitted_batch_evidence_bundles=None,
        execution_batch_manifest=None,
        request_document=None,
        changed_files_snapshot=None,
        fact_snapshot=None,
    )
    return {
        "aggregate_manifest": aggregate_manifest,
        "aggregate_summary": aggregate_summary,
    }


def _ci_missing_execution_batch_manifest_payloads(
    args: argparse.Namespace,
    *,
    owner: str,
    name: str,
    created_at: str,
    completed_at: str | None = None,
    summary_created_at: str | None = None,
    plan: Mapping[str, object],
    request: Mapping[str, object],
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    boundary_diagnostics: Sequence[Mapping[str, object]] = (),
) -> dict[str, Mapping[str, object]]:
    summary_created_at = summary_created_at or created_at
    completed_at = completed_at or summary_created_at
    run_id = str(args.run_id)
    run_attempt = str(args.run_attempt)
    affected_range = cast("Mapping[str, object]", plan["affected-range"])
    changed_files_hash = affected_range.get("changed-files-hash")
    fact_snapshot_projection = cast(
        "Mapping[str, object]", plan["fact-snapshot"]
    )
    fact_snapshot_id = fact_snapshot_projection.get("id")
    input_artifacts: dict[str, object] = {
        "request": _ci_aggregate_input_artifact(
            ci_validation_request_artifact_ref(
                run_id=run_id,
                run_attempt=run_attempt,
            ),
            content_digest=cast("Mapping[str, object]", plan["request"])[
                "request-digest"
            ],
            required=True,
            artifact_instance_id=getattr(
                args, "expected_request_artifact_id", None
            ),
            require_artifact_instance_id=True,
        ),
        "validation-plan": _ci_aggregate_input_artifact(
            ci_validation_plan_artifact_ref(
                run_id=run_id,
                run_attempt=run_attempt,
            ),
            content_digest=str(plan["plan-digest"]),
            required=True,
            artifact_instance_id=getattr(
                args, "expected_plan_artifact_id", None
            ),
            require_artifact_instance_id=False,
        ),
        "changed-files-snapshot": _ci_aggregate_input_artifact(
            ci_validation_changed_files_snapshot_artifact_ref(
                run_id=run_id,
                run_attempt=run_attempt,
            )
            if isinstance(changed_files_hash, str)
            else None,
            content_digest=changed_files_hash
            if isinstance(changed_files_hash, str)
            else None,
            required=isinstance(changed_files_hash, str),
            artifact_instance_id=getattr(
                args,
                "expected_changed_files_snapshot_artifact_id",
                None,
            ),
            require_artifact_instance_id=False,
        ),
        "fact-snapshot": _ci_aggregate_input_artifact(
            ci_validation_fact_snapshot_artifact_ref(
                run_id=run_id,
                run_attempt=run_attempt,
            )
            if isinstance(fact_snapshot_id, str)
            else None,
            content_digest=fact_snapshot_id
            if isinstance(fact_snapshot_id, str)
            else None,
            required=isinstance(fact_snapshot_id, str),
            artifact_instance_id=getattr(
                args, "expected_fact_snapshot_artifact_id", None
            ),
            require_artifact_instance_id=False,
        ),
        "execution-batch-manifest": _ci_aggregate_input_artifact(
            ci_validation_execution_batch_manifest_artifact_ref(
                run_id=run_id,
                run_attempt=run_attempt,
            ),
            content_digest=None,
            required=True,
            artifact_instance_id=getattr(
                args,
                "expected_execution_batch_manifest_artifact_id",
                None,
            ),
            require_artifact_instance_id=True,
        ),
    }
    _ci_apply_boundary_diagnostics_to_input_artifacts(
        input_artifacts,
        boundary_diagnostics,
    )
    _ci_close_snapshot_input_authority(input_artifacts)
    required_input_failure = _ci_aggregate_required_input_failure(
        input_artifacts,
    )
    authoritative_request = (
        request
        if _ci_aggregate_input_is_valid(input_artifacts, "request")
        else None
    )
    authoritative_changed_files_snapshot = (
        changed_files_snapshot
        if _ci_aggregate_input_is_valid(
            input_artifacts, "changed-files-snapshot"
        )
        else None
    )
    authoritative_fact_snapshot = (
        fact_snapshot
        if _ci_aggregate_input_is_valid(input_artifacts, "fact-snapshot")
        else None
    )
    pre_final_count = _ci_aggregate_pre_final_input_count(input_artifacts)
    observed_dir = str(getattr(args, "observed_artifacts_dir", ""))
    root = Path(observed_dir) if observed_dir else None
    unexpected = _ci_aggregate_unexpected_artifacts(
        root,
        run_id=run_id,
        run_attempt=run_attempt,
        expected_refs=_ci_aggregate_allowed_observed_refs(
            input_artifacts=input_artifacts,
            expected_batch_refs=set(),
            run_id=run_id,
            run_attempt=run_attempt,
        ),
        expected_batch_refs=set(),
        max_unexpected_artifacts=_ci_aggregate_unexpected_artifact_sentinel_count(
            pre_final_count,
        ),
    )
    namespace_enumeration_unavailable, downloader_namespace_overflow = (
        _ci_aggregate_downloader_namespace_observation(
            root,
            expected_run_id=run_id,
            expected_run_attempt=run_attempt,
        )
    )
    namespace_overflow = _ci_aggregate_namespace_overflow(
        pre_final_count + len(unexpected),
        enumeration_unavailable=namespace_enumeration_unavailable,
        downloader_observed_overflow=downloader_namespace_overflow,
    )
    aggregate_manifest = _freeze_ci_validation_aggregate_evidence_manifest(
        created_at=created_at,
        repository_owner=owner,
        repository_name=name,
        workflow=str(args.workflow),
        run_id=run_id,
        run_attempt=run_attempt,
        input_artifacts=input_artifacts,
        batch_bundles=[],
        unexpected_contract_artifacts=unexpected,
        namespace_overflow=namespace_overflow,
        pre_final_validation_artifacts=pre_final_count,
        namespace_closed_at=created_at,
        plan=plan,
        execution_batch_manifest=None,
        request=authoritative_request,
        changed_files_snapshot=authoritative_changed_files_snapshot,
        fact_snapshot=authoritative_fact_snapshot,
        _require_authoritative_snapshot_inputs=not required_input_failure,
    )
    if getattr(args, "aggregate_phase", "all") == "evidence":
        return {"aggregate_manifest": aggregate_manifest}
    aggregate_manifest_document: Mapping[str, object] | None = (
        aggregate_manifest
    )
    aggregate_manifest_digest = (
        ci_validation_aggregate_evidence_manifest_payload_digest(
            aggregate_manifest
        )
    )
    aggregate_manifest_producer_verified = (
        _ci_aggregate_manifest_producer_verified(args)
    )
    aggregate_manifest_authority_diagnostics: Sequence[
        Mapping[str, object]
    ] = ()
    if getattr(args, "aggregate_phase", "all") == "summary":
        manifest_authority = _ci_summary_aggregate_manifest_authority(
            args,
            recomputed_manifest=aggregate_manifest,
            plan=plan,
            execution_batch_manifest=None,
            request=authoritative_request,
            changed_files_snapshot=authoritative_changed_files_snapshot,
            fact_snapshot=authoritative_fact_snapshot,
        )
        aggregate_manifest = cast(
            "Mapping[str, object]",
            manifest_authority["manifest"],
        )
        aggregate_manifest_digest = cast(
            "str | None",
            manifest_authority["content_digest"],
        )
        aggregate_manifest_document = cast(
            "Mapping[str, object] | None",
            manifest_authority["manifest_document"],
        )
        aggregate_manifest_authority_diagnostics = cast(
            "Sequence[Mapping[str, object]]",
            manifest_authority["diagnostics"],
        )
        aggregate_manifest_authority_diagnostics = tuple(
            sorted(
                aggregate_manifest_authority_diagnostics,
                key=lambda item: str(item.get("diagnostic-id")),
            )
        )
        if aggregate_manifest_authority_diagnostics:
            aggregate_manifest_producer_verified = False
    evidence_results = _ci_aggregate_evidence_results(
        plan=plan,
        admitted_bundles=[],
    )
    budgets = _ci_missing_execution_manifest_budgets(
        pre_final_validation_artifacts=pre_final_count,
        aggregate_duration_seconds=_ci_aggregate_duration_seconds(
            str(getattr(args, "started_at", "") or created_at),
            completed_at,
        ),
    )
    manifest_binding_producer_verified = (
        aggregate_manifest_producer_verified
        or aggregate_manifest_digest is None
    )
    aggregate_summary_without_manifest = (
        aggregate_manifest_document is None
        and (
            aggregate_manifest_digest is None
            or not aggregate_manifest_authority_diagnostics
        )
    )
    summary_required_input_failure = (
        required_input_failure and aggregate_manifest_document is not None
    )
    summary_namespace_overflow: Mapping[str, object] = (
        namespace_overflow if aggregate_manifest_document is not None else {}
    )
    summary_unexpected = (
        unexpected if aggregate_manifest_document is not None else []
    )
    failures = _ci_aggregate_summary_failures(
        summary_bundle_rows=[],
        evidence_results=evidence_results,
        namespace_overflow=summary_namespace_overflow,
        unexpected_artifacts=summary_unexpected,
        required_input_failure=summary_required_input_failure,
        aggregate_manifest_producer_verified=(
            manifest_binding_producer_verified
        ),
        aggregate_manifest_authority_diagnostics=(
            aggregate_manifest_authority_diagnostics
        ),
        aggregate_summary_without_manifest=aggregate_summary_without_manifest,
    )
    diagnostics = sorted(
        [
            failure["diagnostic"]
            for failure in failures
            if isinstance(failure.get("diagnostic"), Mapping)
        ],
        key=lambda item: str(item.get("diagnostic-id")),
    )
    reason = _ci_aggregate_summary_reason(
        summary_bundle_rows=[],
        evidence_results=evidence_results,
        namespace_overflow=summary_namespace_overflow,
        unexpected_artifacts=summary_unexpected,
        required_input_failure=summary_required_input_failure,
        aggregate_manifest_producer_verified=(
            manifest_binding_producer_verified
        ),
        aggregate_manifest_authority_failure=bool(
            aggregate_manifest_authority_diagnostics
        ),
        aggregate_summary_without_manifest=aggregate_summary_without_manifest,
    )
    aggregate_summary = freeze_ci_validation_aggregate_summary(
        created_at=summary_created_at,
        repository_owner=owner,
        repository_name=name,
        workflow=str(args.workflow),
        run_id=run_id,
        run_attempt=run_attempt,
        aggregate_evidence_manifest={
            "artifact-ref": aggregate_manifest["artifact-ref"],
            "artifact-instance-id": (
                _ci_required_aggregate_manifest_artifact_id(args)
                if aggregate_manifest_digest is not None
                else None
            ),
            "content-digest": aggregate_manifest_digest,
        },
        final_artifacts={
            "aggregate-evidence-manifest": {
                "artifact-ref": aggregate_manifest["artifact-ref"],
                "artifact-instance-id": (
                    _ci_required_aggregate_manifest_artifact_id(args)
                    if aggregate_manifest_digest is not None
                    else None
                ),
                "content-digest": aggregate_manifest_digest,
                "producer-verified": aggregate_manifest_producer_verified,
                "authority-diagnostics": list(
                    aggregate_manifest_authority_diagnostics
                ),
            },
            "aggregate-summary": {
                "artifact-ref": ci_validation_aggregate_summary_artifact_ref(
                    run_id=run_id,
                    run_attempt=run_attempt,
                ),
            },
        },
        validation_tree=cast("Mapping[str, object]", plan["validation-tree"]),
        affected_range=_ci_summary_affected_range(plan),
        request=_ci_aggregate_manifest_request_summary(input_artifacts),
        scheduled_full=cast("Mapping[str, object]", plan["scheduled-full"]),
        verdict="failed",
        reason=reason,
        budgets=budgets,
        diagnostics=diagnostics,
        batch_bundles=[],
        evidence_results=evidence_results,
        failures=failures,
        work_groups=_ci_aggregate_work_group_counts(evidence_results),
        plan=plan,
        aggregate_evidence_manifest_document=aggregate_manifest_document,
        admitted_batch_evidence_bundles=None,
        execution_batch_manifest=None,
        request_document=authoritative_request,
        changed_files_snapshot=authoritative_changed_files_snapshot,
        fact_snapshot=authoritative_fact_snapshot,
    )
    return {
        "aggregate_manifest": aggregate_manifest,
        "aggregate_summary": aggregate_summary,
    }


def _ci_invalid_request_batch_payloads(
    args: argparse.Namespace,
    *,
    owner: str,
    name: str,
    created_at: str,
    completed_at: str | None = None,
    plan: Mapping[str, object],
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    boundary_diagnostics: Sequence[Mapping[str, object]],
    request_detail: str,
    summary_created_at: str | None = None,
) -> dict[str, Mapping[str, object]]:
    request_ref = ci_validation_request_artifact_ref(
        run_id=str(args.run_id),
        run_attempt=str(args.run_attempt),
    )
    manifest_ref = ci_validation_execution_batch_manifest_artifact_ref(
        run_id=str(args.run_id),
        run_attempt=str(args.run_attempt),
    )
    return _ci_missing_execution_batch_manifest_payloads(
        args,
        owner=owner,
        name=name,
        created_at=created_at,
        completed_at=completed_at,
        summary_created_at=summary_created_at,
        plan=plan,
        request={},
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
        boundary_diagnostics=[
            *boundary_diagnostics,
            _ci_aggregate_diagnostic(
                f"request-invalid/{request_detail}",
                code=DiagnosticFamily.REQUEST_INVALID.value,
                detail=request_detail,
                message="CI validation request control input is not admissible.",
                source_id=request_ref,
                severity=DiagnosticSeverity.FAIL_CLOSED.value,
                verdict_effect=DiagnosticVerdictEffect.FAIL_CLOSED.value,
            ),
            _ci_malformed_control_input_diagnostic(
                manifest_ref,
                detail=DiagnosticDetail.EXECUTION_BATCH_MANIFEST_MISSING.value,
                message=(
                    "Execution-batch manifest input is not authoritative "
                    "without a valid request."
                ),
            ),
        ],
    )


def _ci_missing_execution_manifest_budgets(
    *,
    pre_final_validation_artifacts: int,
    aggregate_duration_seconds: int,
) -> dict[str, object]:
    return {
        "pre-final-validation-artifacts": pre_final_validation_artifacts,
        "expected-final-validation-artifacts": 2,
        "expected-actual-validation-artifacts": pre_final_validation_artifacts
        + 2,
        "max-validation-artifacts": 20,
        "actual-execution-batches": 0,
        "actual-total-jobs": 0,
        "actual-windows-jobs": 0,
        "aggregate-duration-seconds": aggregate_duration_seconds,
        "aggregate-target-duration-seconds": 60,
        "aggregate-max-duration-seconds": 120,
    }


def _ci_aggregate_manifest_request_summary(
    input_artifacts: Mapping[str, object],
) -> dict[str, object]:
    request = input_artifacts.get("request")
    if isinstance(request, Mapping) and request.get("admissibility") == "valid":
        return {
            "artifact-ref": request.get("artifact-ref"),
            "request-digest": request.get("content-digest"),
        }
    return {"artifact-ref": None, "request-digest": None}


def _ci_aggregate_input_is_valid(
    input_artifacts: Mapping[str, object],
    key: str,
) -> bool:
    artifact = input_artifacts.get(key)
    return isinstance(artifact, Mapping) and artifact.get("admissibility") in {
        "valid",
        "not-required",
    }


def _ci_close_snapshot_input_authority(
    input_artifacts: Mapping[str, object],
) -> None:
    snapshots = [
        input_artifacts.get("changed-files-snapshot"),
        input_artifacts.get("fact-snapshot"),
    ]
    if not all(isinstance(artifact, dict) for artifact in snapshots):
        return
    snapshot_maps = cast("list[dict[str, object]]", snapshots)
    required = [item for item in snapshot_maps if item.get("required") is True]
    request_input = input_artifacts.get("request")
    execution_input = input_artifacts.get("execution-batch-manifest")
    request_closed = (
        isinstance(request_input, Mapping)
        and request_input.get("required") is True
        and request_input.get("admissibility") != "valid"
    )
    execution_closed = (
        isinstance(execution_input, Mapping)
        and execution_input.get("required") is True
        and execution_input.get("admissibility") != "valid"
    )
    context_closed = request_closed or execution_closed
    snapshots_closed = any(
        item.get("admissibility") != "valid" for item in required
    )
    if not required or not (context_closed or snapshots_closed):
        return
    if context_closed and not snapshots_closed:
        items = [
            *snapshot_maps,
            *(
                [request_input]
                if execution_closed
                and isinstance(request_input, dict)
                and request_input.get("admissibility") == "valid"
                else []
            ),
        ]
    else:
        items = list(input_artifacts.values())
    for item in items:
        if not isinstance(item, dict) or item.get("required") is not True:
            continue
        if item.get("admissibility") != "valid":
            continue
        item["admissibility"] = "inadmissible"
        item["diagnostics"] = [
            *cast(
                "Sequence[Mapping[str, object]]", item.get("diagnostics", [])
            ),
            _ci_aggregate_diagnostic(
                "required-input-artifact-failure/snapshot-companion-unproven",
                code=DiagnosticFamily.REQUIRED_INPUT_ARTIFACT_FAILURE.value,
                detail=DiagnosticDetail.REQUIRED_INPUT_ARTIFACT_FAILURE.value,
                message=(
                    "Required snapshot input artifact evidence was not "
                    "authoritative as a closed companion set."
                ),
                source_id=item.get("artifact-ref")
                if isinstance(item.get("artifact-ref"), str)
                else None,
                severity=DiagnosticSeverity.FAIL_CLOSED.value,
                verdict_effect=DiagnosticVerdictEffect.FAIL_CLOSED.value,
            ),
        ]


def _ci_aggregate_batch_slots(
    *,
    plan: Mapping[str, object],
    request: Mapping[str, object],
    execution_batch_manifest: Mapping[str, object],
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    input_artifacts: Mapping[str, object],
    observed_artifacts_dir: str,
    run_id: str,
    run_attempt: str,
    admit_valid_bundles: bool = True,
) -> tuple[list[Json], list[Mapping[str, object]], list[Json]]:
    root = Path(observed_artifacts_dir) if observed_artifacts_dir else None
    downloader_admissions = _ci_aggregate_downloader_batch_admissions(
        root,
        expected_run_id=run_id,
        expected_run_attempt=run_attempt,
    )
    expected_refs = _ci_expected_batch_bundle_refs(execution_batch_manifest)
    bundle_slots: list[Json] = []
    admitted_bundles: list[Mapping[str, object]] = []
    admitted_bundles_by_batch_id: dict[str, Mapping[str, object]] = {}
    batches_by_id = {
        str(batch["batch-id"]): batch
        for batch in _ci_execution_batches(execution_batch_manifest)
    }
    for batch in _ci_execution_batches_in_dependency_order(
        execution_batch_manifest,
    ):
        batch_id = str(batch["batch-id"])
        artifact_ref = str(batch["expected-batch-evidence-bundle-ref"])
        dependency_bundles = _ci_admitted_dependency_bundles_for_batch(
            batch_id,
            batches_by_id=batches_by_id,
            admitted_bundles_by_batch_id=admitted_bundles_by_batch_id,
        )
        candidates = _ci_aggregate_bundle_candidates(
            root,
            batch_id=batch_id,
            artifact_ref=artifact_ref,
            run_id=run_id,
            run_attempt=run_attempt,
            plan=plan,
            request=request,
            execution_batch_manifest=execution_batch_manifest,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            dependency_evidence_bundles=dependency_bundles,
            downloader_admissions=downloader_admissions,
        )
        if not admit_valid_bundles:
            for candidate in candidates:
                candidate_record = candidate.get("candidate")
                if (
                    isinstance(candidate_record, dict)
                    and candidate_record.get("admissibility") == "valid"
                ):
                    candidate_record["admissibility"] = "inadmissible"
                    candidate_record["diagnostics"] = [
                        *cast(
                            "Sequence[Mapping[str, object]]",
                            candidate_record.get("diagnostics", []),
                        ),
                        _ci_aggregate_diagnostic(
                            f"inadmissible-batch-evidence/{batch_id}/input-authority",
                            code=(
                                DiagnosticFamily.INADMISSIBLE_BATCH_EVIDENCE.value
                            ),
                            detail=(
                                DiagnosticDetail.BUNDLE_PRODUCER_UNVERIFIED.value
                            ),
                            message=(
                                "Batch evidence bundle cannot be admitted "
                                "without authoritative required inputs."
                            ),
                            source_id=batch_id,
                            severity=(
                                DiagnosticSeverity.BLOCKING_FAILURE.value
                            ),
                            verdict_effect=(
                                DiagnosticVerdictEffect.FAILED.value
                            ),
                        ),
                    ]
        valid_candidates = [
            candidate
            for candidate in candidates
            if candidate["candidate"]["admissibility"] == "valid"
        ]
        slot_diagnostics: list[Mapping[str, object]] = []
        admitted_candidate_id: str | None = None
        if not candidates:
            slot_admissibility = "missing"
            slot_diagnostics.append(
                _ci_aggregate_diagnostic(
                    f"inadmissible-batch-evidence/{batch_id}/missing",
                    code=DiagnosticFamily.INADMISSIBLE_BATCH_EVIDENCE.value,
                    detail=DiagnosticDetail.MISSING_BUNDLE.value,
                    message="Required batch evidence bundle is missing.",
                    source_id=batch_id,
                    severity=DiagnosticSeverity.BLOCKING_FAILURE.value,
                    verdict_effect=DiagnosticVerdictEffect.FAILED.value,
                )
            )
        elif len(candidates) > 1:
            slot_admissibility = "duplicate"
            slot_diagnostics.append(
                _ci_aggregate_diagnostic(
                    f"inadmissible-batch-evidence/{batch_id}/duplicate",
                    code=DiagnosticFamily.INADMISSIBLE_BATCH_EVIDENCE.value,
                    detail=DiagnosticDetail.DUPLICATE_BUNDLE_CANDIDATES.value,
                    message="Multiple batch evidence bundle candidates were observed.",
                    source_id=batch_id,
                    severity=DiagnosticSeverity.BLOCKING_FAILURE.value,
                    verdict_effect=DiagnosticVerdictEffect.FAILED.value,
                )
            )
        elif len(valid_candidates) == 1 and admit_valid_bundles:
            slot_admissibility = "valid"
            valid_candidate = valid_candidates[0]
            candidate_record = cast(
                "Mapping[str, object]",
                valid_candidate["candidate"],
            )
            admitted_candidate_id = str(candidate_record["candidate-id"])
            artifact_instance_id = candidate_record.get("artifact-instance-id")
            if not isinstance(artifact_instance_id, str):
                msg = "valid batch bundle candidate is missing artifact instance id"
                raise RuntimeError(msg)
            admitted_bundles.append(
                _TrustedDependencyBundle(
                    cast("Mapping[str, object]", valid_candidate["bundle"]),
                    artifact_instance_id=artifact_instance_id,
                    admitted_candidate_id=admitted_candidate_id,
                )
            )
            admitted_bundles_by_batch_id[batch_id] = admitted_bundles[-1]
        elif len(valid_candidates) == 1:
            slot_admissibility = "inadmissible"
            slot_diagnostics.append(
                _ci_aggregate_diagnostic(
                    f"inadmissible-batch-evidence/{batch_id}/input-authority",
                    code=DiagnosticFamily.INADMISSIBLE_BATCH_EVIDENCE.value,
                    detail=DiagnosticDetail.BUNDLE_PRODUCER_UNVERIFIED.value,
                    message=(
                        "Batch evidence bundle cannot be admitted without "
                        "authoritative required inputs."
                    ),
                    source_id=batch_id,
                    severity=DiagnosticSeverity.BLOCKING_FAILURE.value,
                    verdict_effect=DiagnosticVerdictEffect.FAILED.value,
                )
            )
        else:
            slot_admissibility = "inadmissible"
            slot_diagnostics.append(
                _ci_aggregate_diagnostic(
                    f"inadmissible-batch-evidence/{batch_id}/malformed",
                    code=DiagnosticFamily.INADMISSIBLE_BATCH_EVIDENCE.value,
                    detail=_ci_inadmissible_candidate_detail(candidates),
                    message="Batch evidence bundle is not authoritative.",
                    source_id=batch_id,
                    severity=DiagnosticSeverity.BLOCKING_FAILURE.value,
                    verdict_effect=DiagnosticVerdictEffect.FAILED.value,
                )
            )
        observed_candidates = [
            cast("Json", item["candidate"]) for item in candidates
        ]
        observed_candidates.sort(key=lambda item: str(item["candidate-id"]))
        bundle_slots.append(
            {
                "batch-id": batch_id,
                "artifact-ref": artifact_ref,
                "expected-cardinality": 1,
                "slot-admissibility": slot_admissibility,
                "admitted-candidate-id": admitted_candidate_id,
                "observed-candidates": observed_candidates,
                "diagnostics": sorted(
                    [dict(item) for item in slot_diagnostics],
                    key=lambda item: str(item.get("diagnostic-id")),
                ),
            }
        )
    allowed_refs = _ci_aggregate_allowed_observed_refs(
        input_artifacts=input_artifacts,
        expected_batch_refs=expected_refs,
        run_id=run_id,
        run_attempt=run_attempt,
    )
    unexpected = _ci_aggregate_unexpected_artifacts(
        root,
        run_id=run_id,
        run_attempt=run_attempt,
        expected_refs=allowed_refs,
        expected_batch_refs=expected_refs,
        max_unexpected_artifacts=_ci_aggregate_unexpected_artifact_sentinel_count(
            _ci_aggregate_pre_final_input_count(input_artifacts)
            + len(bundle_slots),
        ),
    )
    bundle_slots.sort(key=lambda item: str(item["batch-id"]))
    return bundle_slots, admitted_bundles, unexpected


def _ci_admitted_dependency_bundles_for_batch(
    batch_id: str,
    *,
    batches_by_id: Mapping[str, Mapping[str, object]],
    admitted_bundles_by_batch_id: Mapping[str, Mapping[str, object]],
) -> list[Mapping[str, object]]:
    dependency_batch_ids = _ci_execution_batch_transitive_dependencies(
        batch_id,
        batches_by_id,
    )
    return [
        admitted_bundles_by_batch_id[dependency_batch_id]
        for dependency_batch_id in dependency_batch_ids
        if dependency_batch_id in admitted_bundles_by_batch_id
    ]


def _ci_execution_batches_in_dependency_order(
    execution_batch_manifest: Mapping[str, object],
) -> list[Mapping[str, object]]:
    batches = [
        cast("Mapping[str, object]", batch)
        for batch in cast(
            "Sequence[Mapping[str, object]]",
            execution_batch_manifest.get("batches", []),
        )
    ]
    pending = {str(batch["batch-id"]): batch for batch in batches}
    ordered: list[Mapping[str, object]] = []
    while pending:
        ready = [
            batch_id
            for batch_id, batch in pending.items()
            if all(
                str(dependency_id) not in pending
                for dependency_id in cast(
                    "Sequence[object]",
                    batch.get("depends-on-batches", []),
                )
            )
        ]
        if not ready:
            ordered.extend(pending[batch_id] for batch_id in sorted(pending))
            break
        for batch_id in sorted(ready):
            ordered.append(pending.pop(batch_id))
    return ordered


def _ci_expected_batch_bundle_refs(
    execution_batch_manifest: Mapping[str, object],
) -> set[str]:
    return {
        str(batch["expected-batch-evidence-bundle-ref"])
        for batch in cast(
            "Sequence[Mapping[str, object]]",
            execution_batch_manifest.get("batches", []),
        )
    }


def _ci_aggregate_bundle_candidates(
    root: Path | None,
    *,
    batch_id: str,
    artifact_ref: str,
    run_id: str,
    run_attempt: str,
    plan: Mapping[str, object],
    request: Mapping[str, object],
    execution_batch_manifest: Mapping[str, object],
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    dependency_evidence_bundles: Sequence[Mapping[str, object]],
    downloader_admissions: Mapping[str, object],
) -> list[Json]:
    if root is None or not root.is_dir():
        return []
    physical_name = artifact_physical_name(artifact_ref)
    candidate_dirs = [
        item
        for item in sorted(root.iterdir(), key=lambda path: path.name)
        if item.is_dir()
        and (
            item.name == physical_name
            or item.name.startswith(physical_name + "#")
        )
    ]
    candidates: list[Json] = []
    for artifact_dir in candidate_dirs:
        bundle, diagnostics = _ci_read_aggregate_bundle_candidate(
            artifact_dir / "batch-evidence-bundle.json",
            plan=plan,
            request=request,
            execution_batch_manifest=execution_batch_manifest,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            expected_run_id=run_id,
            expected_run_attempt=run_attempt,
            dependency_evidence_bundles=dependency_evidence_bundles,
        )
        artifact_instance_id, producer_verification, authority_diagnostics = (
            _ci_aggregate_bundle_producer_authority(
                artifact_dir,
                physical_name=physical_name,
                artifact_ref=artifact_ref,
                run_id=run_id,
                run_attempt=run_attempt,
                batch_id=batch_id,
                downloader_admissions=downloader_admissions,
            )
        )
        diagnostics = [*diagnostics, *authority_diagnostics]
        if bundle is not None and bundle.get("artifact-ref") != artifact_ref:
            diagnostics.append(
                _ci_aggregate_diagnostic(
                    f"inadmissible-batch-evidence/{batch_id}/ref-mismatch",
                    code=DiagnosticFamily.INADMISSIBLE_BATCH_EVIDENCE.value,
                    detail=(
                        DiagnosticDetail.EXECUTION_BATCH_MANIFEST_BUNDLE_REF_MISMATCH.value
                    ),
                    message=(
                        "Batch evidence bundle ref does not match its "
                        "manifest slot."
                    ),
                    source_id=batch_id,
                    severity=DiagnosticSeverity.BLOCKING_FAILURE.value,
                    verdict_effect=DiagnosticVerdictEffect.FAILED.value,
                )
            )
        payload_readable = bundle is not None and not diagnostics
        content_digest = (
            ci_validation_batch_evidence_bundle_payload_digest(bundle)
            if bundle is not None and payload_readable
            else None
        )
        admissibility = (
            "valid"
            if payload_readable
            and bundle is not None
            and bundle.get("artifact-ref") == artifact_ref
            and producer_verification == "verified"
            else "inadmissible"
        )
        if admissibility != "valid" and not diagnostics:
            diagnostics = [
                _ci_aggregate_diagnostic(
                    f"inadmissible-batch-evidence/{batch_id}/ref-mismatch",
                    code=DiagnosticFamily.INADMISSIBLE_BATCH_EVIDENCE.value,
                    detail=(
                        DiagnosticDetail.EXECUTION_BATCH_MANIFEST_BUNDLE_REF_MISMATCH.value
                    ),
                    message="Batch evidence bundle ref does not match its manifest slot.",
                    source_id=batch_id,
                    severity=DiagnosticSeverity.BLOCKING_FAILURE.value,
                    verdict_effect=DiagnosticVerdictEffect.FAILED.value,
                )
            ]
        candidates.append(
            {
                "candidate": {
                    "candidate-id": ci_validation_batch_evidence_candidate_id(
                        run_id=run_id,
                        run_attempt=run_attempt,
                        batch_id=batch_id,
                        artifact_ref=artifact_ref,
                        artifact_instance_id=artifact_instance_id,
                        physical_artifact_name=physical_name,
                    ),
                    "artifact-instance-id": artifact_instance_id,
                    "content-digest": content_digest,
                    "producer-verification": producer_verification,
                    "payload-readable": payload_readable,
                    "admissibility": admissibility,
                    "diagnostics": sorted(
                        [dict(item) for item in diagnostics],
                        key=lambda item: str(item.get("diagnostic-id")),
                    ),
                },
                "bundle": bundle,
            }
        )
    return candidates


def _ci_read_aggregate_bundle_candidate(
    path: Path,
    *,
    plan: Mapping[str, object],
    request: Mapping[str, object],
    execution_batch_manifest: Mapping[str, object],
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    expected_run_id: str,
    expected_run_attempt: str,
    dependency_evidence_bundles: Sequence[Mapping[str, object]],
) -> tuple[Mapping[str, object] | None, list[Mapping[str, object]]]:
    try:
        bundle = _read_json(path)
        validate_ci_validation_batch_evidence_bundle(
            bundle,
            plan=plan,
            request=request,
            execution_batch_manifest=execution_batch_manifest,
            changed_files_snapshot=changed_files_snapshot,
            fact_snapshot=fact_snapshot,
            expected_run_id=expected_run_id,
            expected_run_attempt=expected_run_attempt,
            dependency_evidence_bundles=dependency_evidence_bundles,
        )
    except (
        ContractValidationError,
        OSError,
        TypeError,
        ValueError,
        json.JSONDecodeError,
    ) as exc:
        return None, [
            _ci_aggregate_diagnostic(
                f"inadmissible-batch-evidence/{canonical_json_digest(str(exc))}",
                code=DiagnosticFamily.INADMISSIBLE_BATCH_EVIDENCE.value,
                detail=DiagnosticDetail.MALFORMED_BUNDLE.value,
                message="Batch evidence bundle could not be validated.",
                source_id=None,
                severity=DiagnosticSeverity.BLOCKING_FAILURE.value,
                verdict_effect=DiagnosticVerdictEffect.FAILED.value,
            )
        ]
    else:
        return bundle, []


def _ci_inadmissible_candidate_detail(
    candidates: Sequence[Mapping[str, object]],
) -> str:
    for candidate in candidates:
        candidate_record = candidate.get("candidate")
        diagnostics = (
            candidate_record.get("diagnostics")
            if isinstance(candidate_record, Mapping)
            else None
        )
        if not isinstance(diagnostics, Sequence) or isinstance(
            diagnostics, str | bytes
        ):
            continue
        for diagnostic in diagnostics:
            if not isinstance(diagnostic, Mapping):
                continue
            detail = diagnostic.get("detail")
            if isinstance(detail, str) and detail:
                return detail
    return DiagnosticDetail.MALFORMED_BUNDLE.value


def _ci_aggregate_bundle_producer_authority(
    artifact_dir: Path,
    *,
    physical_name: str,
    artifact_ref: str,
    run_id: str,
    run_attempt: str,
    batch_id: str,
    downloader_admissions: Mapping[str, object],
) -> tuple[str | None, str, list[Mapping[str, object]]]:
    artifact_instance_id, diagnostics = (
        _ci_aggregate_bundle_internal_metadata_authority(
            artifact_dir,
            physical_name=physical_name,
            artifact_ref=artifact_ref,
            run_id=run_id,
            run_attempt=run_attempt,
        )
    )
    diagnostics = [
        *diagnostics,
        *_ci_aggregate_bundle_downloader_admission_diagnostics(
            artifact_dir,
            batch_id=batch_id,
            artifact_ref=artifact_ref,
            physical_name=physical_name,
            artifact_instance_id=artifact_instance_id,
            run_id=run_id,
            run_attempt=run_attempt,
            downloader_admissions=downloader_admissions,
        ),
    ]
    if diagnostics:
        return artifact_instance_id, "producer-unverified", diagnostics
    return artifact_instance_id, "verified", []


def _ci_aggregate_downloader_batch_admissions(  # noqa: C901, PLR0911
    root: Path | None,
    *,
    expected_run_id: str,
    expected_run_attempt: str,
) -> Json:
    if root is None:
        return {"admissions": {}, "status": "missing"}
    observation_path = root / _CI_DOWNLOADER_OBSERVATION_FILE
    try:
        observation = _read_json(observation_path)
    except (OSError, TypeError, ValueError, json.JSONDecodeError):
        return {"admissions": {}, "status": "missing"}
    if not isinstance(observation, Mapping):
        return {"admissions": {}, "status": "malformed"}
    if (
        observation.get("run-id") != expected_run_id
        or observation.get("run-attempt") != expected_run_attempt
    ):
        return {"admissions": {}, "status": "mismatched"}
    raw_admissions = observation.get(
        _CI_DOWNLOADER_ADMITTED_BATCH_ARTIFACTS_KEY
    )
    if not isinstance(raw_admissions, Sequence) or isinstance(
        raw_admissions,
        str | bytes,
    ):
        return {"admissions": {}, "status": "malformed"}
    admissions: dict[str, Mapping[str, object]] = {}
    required_fields = {
        "admission-source",
        "artifact-instance-id",
        "artifact-ref",
        "batch-id",
        "candidate-id",
        "physical-artifact-name",
        "producer-boundary",
        "run-attempt",
        "run-id",
    }
    for raw_admission in raw_admissions:
        if not isinstance(raw_admission, Mapping):
            return {"admissions": {}, "status": "malformed"}
        if not all(
            isinstance(raw_admission.get(field), str)
            and raw_admission.get(field)
            for field in required_fields
        ):
            return {"admissions": {}, "status": "malformed"}
        if (
            raw_admission.get("run-id") != expected_run_id
            or raw_admission.get("run-attempt") != expected_run_attempt
            or raw_admission.get("producer-boundary") != "execution-batch"
            or raw_admission.get("admission-source")
            != "github-actions-live-api"
        ):
            return {"admissions": {}, "status": "mismatched"}
        candidate_id = str(raw_admission["candidate-id"])
        if candidate_id in admissions:
            return {"admissions": {}, "status": "duplicate"}
        admissions[candidate_id] = dict(raw_admission)
    return {"admissions": admissions, "status": "valid"}


def _ci_aggregate_bundle_downloader_admission_diagnostics(  # noqa: C901, PLR0911
    artifact_dir: Path,
    *,
    batch_id: str,
    artifact_ref: str,
    physical_name: str,
    artifact_instance_id: str | None,
    run_id: str,
    run_attempt: str,
    downloader_admissions: Mapping[str, object],
) -> list[Mapping[str, object]]:
    status = downloader_admissions.get("status")
    if status != "valid":
        return [
            _ci_aggregate_bundle_metadata_diagnostic(
                artifact_dir,
                "downloader-admission",
                (
                    "Downloader-produced batch admission manifest is "
                    f"{status or 'malformed'}."
                ),
            )
        ]
    if not isinstance(artifact_instance_id, str) or not artifact_instance_id:
        return []
    candidate_id = ci_validation_batch_evidence_candidate_id(
        run_id=run_id,
        run_attempt=run_attempt,
        batch_id=batch_id,
        artifact_ref=artifact_ref,
        artifact_instance_id=artifact_instance_id,
        physical_artifact_name=physical_name,
    )
    admissions = downloader_admissions.get("admissions")
    if not isinstance(admissions, Mapping):
        return [
            _ci_aggregate_bundle_metadata_diagnostic(
                artifact_dir,
                "downloader-admission",
                "Downloader-produced batch admission manifest is malformed.",
            )
        ]
    admission = admissions.get(candidate_id)
    if not isinstance(admission, Mapping):
        return [
            _ci_aggregate_bundle_metadata_diagnostic(
                artifact_dir,
                "downloader-admission",
                "Batch evidence candidate was not admitted by the downloader.",
            )
        ]
    expected = {
        "admission-source": "github-actions-live-api",
        "artifact-instance-id": artifact_instance_id,
        "artifact-ref": artifact_ref,
        "batch-id": batch_id,
        "candidate-id": candidate_id,
        "physical-artifact-name": physical_name,
        "producer-boundary": "execution-batch",
        "run-attempt": run_attempt,
        "run-id": run_id,
    }
    diagnostics: list[Mapping[str, object]] = []
    for key, expected_value in expected.items():
        if admission.get(key) != expected_value:
            diagnostics.append(
                _ci_aggregate_bundle_metadata_diagnostic(
                    artifact_dir,
                    f"downloader-admission-{key}",
                    "Downloader-produced batch admission does not match the candidate.",
                )
            )
    try:
        metadata = _read_json(artifact_dir / "artifact-metadata.json")
    except (OSError, TypeError, ValueError, json.JSONDecodeError):
        return diagnostics
    if not isinstance(metadata, Mapping):
        return diagnostics
    for key in (
        "admission-source",
        "artifact-instance-id",
        "artifact-ref",
        "physical-artifact-name",
        "producer-boundary",
        "run-attempt",
        "run-id",
    ):
        if metadata.get(key) != admission.get(key):
            diagnostics.append(
                _ci_aggregate_bundle_metadata_diagnostic(
                    artifact_dir,
                    f"downloader-admission-{key}",
                    "Local batch artifact metadata does not match downloader admission.",
                )
            )
    return diagnostics


def _ci_aggregate_bundle_internal_metadata_authority(
    artifact_dir: Path,
    *,
    physical_name: str,
    artifact_ref: str,
    run_id: str,
    run_attempt: str,
) -> tuple[str | None, list[Mapping[str, object]]]:
    diagnostics: list[Mapping[str, object]] = []
    artifact_instance_id: str | None = None
    try:
        metadata = _read_json(artifact_dir / "artifact-metadata.json")
    except (OSError, TypeError, ValueError, json.JSONDecodeError):
        return (
            None,
            [
                _ci_aggregate_bundle_metadata_diagnostic(
                    artifact_dir,
                    "artifact-metadata",
                    "Downloaded batch artifact metadata is missing or unreadable.",
                )
            ],
        )
    if not isinstance(metadata, Mapping):
        return (
            None,
            [
                _ci_aggregate_bundle_metadata_diagnostic(
                    artifact_dir,
                    "artifact-metadata",
                    "Downloaded batch artifact metadata must be a JSON object.",
                )
            ],
        )
    metadata_id = metadata.get("artifact-instance-id")
    if isinstance(metadata_id, str) and metadata_id:
        artifact_instance_id = metadata_id
    else:
        diagnostics.append(
            _ci_aggregate_bundle_metadata_diagnostic(
                artifact_dir,
                "artifact-instance-id",
                "Downloaded batch artifact metadata does not identify an artifact instance.",
            )
        )
    expected = {
        "artifact-ref": artifact_ref,
        "physical-artifact-name": physical_name,
        "run-id": run_id,
        "run-attempt": run_attempt,
        "producer-boundary": "execution-batch",
    }
    for key, expected_value in expected.items():
        if metadata.get(key) != expected_value:
            diagnostics.append(
                _ci_aggregate_bundle_metadata_diagnostic(
                    artifact_dir,
                    key,
                    "Downloaded batch artifact metadata does not match expected batch artifact identity.",
                )
            )
    if metadata.get("admission-source") not in {
        "github-actions-live-api",
        _CI_ORCHESTRATOR_STATE_ADMISSION_SOURCE,
        _CI_ORCHESTRATOR_LIVE_CROSS_FAMILY_ADMISSION_SOURCE,
    }:
        diagnostics.append(
            _ci_aggregate_bundle_metadata_diagnostic(
                artifact_dir,
                "admission-source",
                "Downloaded batch artifact metadata has an untrusted admission source.",
            )
        )
    return artifact_instance_id, diagnostics


def _ci_aggregate_bundle_metadata_diagnostic(
    artifact_dir: Path,
    field: str,
    message: str,
) -> Mapping[str, object]:
    return _ci_aggregate_diagnostic(
        f"inadmissible-batch-evidence/{artifact_dir.name}/metadata-{field}",
        code=DiagnosticFamily.INADMISSIBLE_BATCH_EVIDENCE.value,
        detail=DiagnosticDetail.BUNDLE_METADATA_AUTHORITY_INVALID.value,
        message=message,
        source_id=artifact_dir.name,
        severity=DiagnosticSeverity.BLOCKING_FAILURE.value,
        verdict_effect=DiagnosticVerdictEffect.FAILED.value,
    )


def _ci_aggregate_artifact_instance_id(
    artifact_dir: Path,
    physical_name: str,
) -> str:
    try:
        metadata = _read_json(artifact_dir / "artifact-metadata.json")
        value = metadata.get("artifact-instance-id")
        metadata_name = metadata.get("physical-artifact-name")
        if isinstance(value, str) and value and metadata_name == physical_name:
            return value
    except (OSError, TypeError, ValueError, json.JSONDecodeError):
        pass
    return artifact_dir.name


def _ci_aggregate_unexpected_artifacts(
    root: Path | None,
    *,
    run_id: str,
    run_attempt: str,
    expected_refs: set[str],
    expected_batch_refs: set[str] | None = None,
    max_unexpected_artifacts: int | None = None,
) -> list[Json]:
    if root is None or not root.is_dir():
        return []
    expected_names = {artifact_physical_name(ref) for ref in expected_refs}
    expected_batch_names = {
        artifact_physical_name(ref) for ref in (expected_batch_refs or set())
    }
    current_attempt_prefix = _ci_attempt_physical_artifact_name_prefix(
        run_id=run_id,
        run_attempt=run_attempt,
    )
    unexpected: list[Json] = []
    for artifact_dir in sorted(root.iterdir(), key=lambda path: path.name):
        if not artifact_dir.is_dir() or any(
            artifact_dir.name == expected_name
            for expected_name in expected_names
        ):
            continue
        if any(
            artifact_dir.name.startswith(expected_name + "#")
            for expected_name in expected_batch_names
        ):
            continue
        if not artifact_dir.name.startswith(current_attempt_prefix):
            continue
        if (
            max_unexpected_artifacts is not None
            and len(unexpected) >= max_unexpected_artifacts
        ):
            break
        physical_artifact_name = _ci_canonical_observed_physical_name(
            artifact_dir.name,
            run_id=run_id,
            run_attempt=run_attempt,
        )
        observed_physical_artifact_name = (
            artifact_dir.name
            if artifact_dir.name != physical_artifact_name
            else None
        )
        source_id = observed_physical_artifact_name or physical_artifact_name
        unexpected_artifact: Json = {
            "physical-artifact-name": physical_artifact_name,
            "artifact-instance-id": artifact_dir.name,
            "classification": "unexpected",
            "diagnostics": [
                _ci_aggregate_diagnostic(
                    f"namespace-closure-failure/{source_id}",
                    code=DiagnosticFamily.NAMESPACE_CLOSURE_FAILURE.value,
                    detail=DiagnosticDetail.UNEXPECTED_CONTRACT_ARTIFACT.value,
                    message="Unexpected CI validation contract artifact.",
                    source_id=source_id,
                    severity=DiagnosticSeverity.FAIL_CLOSED.value,
                    verdict_effect=DiagnosticVerdictEffect.FAIL_CLOSED.value,
                )
            ],
        }
        if observed_physical_artifact_name is not None:
            unexpected_artifact["observed-physical-artifact-name"] = (
                observed_physical_artifact_name
            )
        unexpected.append(unexpected_artifact)
    return unexpected


def _ci_canonical_observed_physical_name(
    artifact_dir_name: str,
    *,
    run_id: str,
    run_attempt: str,
) -> str:
    candidate = artifact_dir_name.split("#", 1)[0]
    if _STRICT_CI_PHYSICAL_ARTIFACT_NAME_RE.fullmatch(candidate):
        return candidate
    digest = canonical_json_digest(
        {
            "observed-physical-artifact-name": artifact_dir_name,
            "representation": "synthetic-schema-valid-physical-name",
            "run-id": run_id,
            "run-attempt": run_attempt,
        },
    )
    return f"three-ci-validation-{run_id}-{run_attempt}-{digest}"


def _ci_aggregate_allowed_observed_refs(
    *,
    input_artifacts: Mapping[str, object],
    expected_batch_refs: set[str],
    run_id: str,
    run_attempt: str,
) -> set[str]:
    refs = set(expected_batch_refs)
    for artifact in input_artifacts.values():
        if not isinstance(artifact, Mapping):
            continue
        artifact_ref = artifact.get("artifact-ref")
        if (
            isinstance(artifact_ref, str)
            and artifact_ref
            and artifact.get("admissibility") != "not-required"
        ):
            refs.add(artifact_ref)
    refs.add(
        ci_validation_aggregate_evidence_manifest_artifact_ref(
            run_id=run_id,
            run_attempt=run_attempt,
        )
    )
    refs.add(
        ci_validation_aggregate_summary_artifact_ref(
            run_id=run_id,
            run_attempt=run_attempt,
        )
    )
    return refs


def _ci_aggregate_input_artifacts(
    args: argparse.Namespace,
    *,
    plan: Mapping[str, object],
    request: Mapping[str, object],
    execution_batch_manifest: Mapping[str, object],
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
    boundary_diagnostics: Sequence[Mapping[str, object]] = (),
) -> dict[str, object]:
    run_id = str(args.run_id)
    run_attempt = str(args.run_attempt)
    affected_range = cast("Mapping[str, object]", plan["affected-range"])
    changed_files_hash = affected_range.get("changed-files-hash")
    fact_snapshot_projection = cast(
        "Mapping[str, object]", plan["fact-snapshot"]
    )
    fact_snapshot_id = fact_snapshot_projection.get("id")
    request_digest = cast("Mapping[str, object]", plan["request"])[
        "request-digest"
    ]
    _ = request
    artifacts: dict[str, object] = {
        "request": _ci_aggregate_input_artifact(
            ci_validation_request_artifact_ref(
                run_id=run_id, run_attempt=run_attempt
            ),
            content_digest=str(request_digest),
            required=True,
            artifact_instance_id=getattr(
                args, "expected_request_artifact_id", None
            ),
            require_artifact_instance_id=True,
        ),
        "validation-plan": _ci_aggregate_input_artifact(
            ci_validation_plan_artifact_ref(
                run_id=run_id, run_attempt=run_attempt
            ),
            content_digest=str(plan["plan-digest"]),
            required=True,
            artifact_instance_id=getattr(
                args, "expected_plan_artifact_id", None
            ),
            require_artifact_instance_id=True,
        ),
        "changed-files-snapshot": _ci_aggregate_input_artifact(
            ci_validation_changed_files_snapshot_artifact_ref(
                run_id=run_id, run_attempt=run_attempt
            )
            if isinstance(changed_files_hash, str)
            else None,
            content_digest=changed_files_hash
            if isinstance(changed_files_hash, str)
            else None,
            required=isinstance(changed_files_hash, str),
            artifact_instance_id=getattr(
                args, "expected_changed_files_snapshot_artifact_id", None
            ),
            require_artifact_instance_id=isinstance(changed_files_hash, str),
        ),
        "fact-snapshot": _ci_aggregate_input_artifact(
            ci_validation_fact_snapshot_artifact_ref(
                run_id=run_id, run_attempt=run_attempt
            )
            if isinstance(fact_snapshot_id, str)
            else None,
            content_digest=fact_snapshot_id
            if isinstance(fact_snapshot_id, str)
            else None,
            required=isinstance(fact_snapshot_id, str),
            artifact_instance_id=getattr(
                args, "expected_fact_snapshot_artifact_id", None
            ),
            require_artifact_instance_id=isinstance(fact_snapshot_id, str),
        ),
        "execution-batch-manifest": _ci_aggregate_input_artifact(
            ci_validation_execution_batch_manifest_artifact_ref(
                run_id=run_id, run_attempt=run_attempt
            ),
            content_digest=ci_validation_execution_batch_manifest_payload_digest(
                execution_batch_manifest
            ),
            required=True,
            artifact_instance_id=getattr(
                args,
                "expected_execution_batch_manifest_artifact_id",
                None,
            ),
            require_artifact_instance_id=True,
        ),
    }
    _ci_apply_boundary_diagnostics_to_input_artifacts(
        artifacts,
        boundary_diagnostics,
    )
    return artifacts


def _ci_aggregate_input_artifact(
    artifact_ref: str | None,
    *,
    content_digest: object,
    required: bool,
    artifact_instance_id: object,
    require_artifact_instance_id: bool = False,
) -> Json:
    if not required:
        return {
            "artifact-ref": None,
            "artifact-instance-id": None,
            "content-digest": None,
            "required": False,
            "expected-cardinality": 0,
            "admissibility": "not-required",
            "diagnostics": [],
        }
    if require_artifact_instance_id and not (
        isinstance(artifact_instance_id, str) and artifact_instance_id
    ):
        detail = _ci_required_input_missing_detail(artifact_ref)
        return {
            "artifact-ref": None,
            "artifact-instance-id": None,
            "content-digest": None,
            "required": True,
            "expected-cardinality": 1,
            "admissibility": "missing",
            "diagnostics": [
                _ci_boundary_diagnostic(
                    index=0,
                    detail=detail,
                    message="Expected required input artifact id is missing.",
                    source_id=None,
                )
            ],
        }
    instance_id = (
        str(artifact_instance_id)
        if isinstance(artifact_instance_id, str) and artifact_instance_id
        else artifact_physical_name(str(artifact_ref))
    )
    return {
        "artifact-ref": artifact_ref,
        "artifact-instance-id": instance_id,
        "content-digest": content_digest,
        "required": True,
        "expected-cardinality": 1,
        "admissibility": "valid",
        "diagnostics": [],
    }


def _ci_required_input_missing_detail(artifact_ref: str | None) -> str:
    if not isinstance(artifact_ref, str):
        return DiagnosticDetail.REQUIRED_INPUT_ARTIFACT_FAILURE.value
    return {
        "execution-batch-manifest": (
            DiagnosticDetail.EXECUTION_BATCH_MANIFEST_MISSING.value
        ),
    }.get(
        _ci_control_artifact_kind(artifact_ref),
        DiagnosticDetail.REQUIRED_INPUT_ARTIFACT_FAILURE.value,
    )


def _ci_apply_boundary_diagnostics_to_input_artifacts(
    artifacts: dict[str, object],
    boundary_diagnostics: Sequence[Mapping[str, object]],
) -> None:
    artifacts_by_ref = {
        artifact.get("artifact-ref"): artifact
        for artifact in artifacts.values()
        if isinstance(artifact, dict)
    }
    for diagnostic in boundary_diagnostics:
        source = diagnostic.get("source")
        source_id = (
            source.get("id")
            if isinstance(source, Mapping)
            else diagnostic.get("source-id")
        )
        artifact = artifacts_by_ref.get(source_id)
        if not isinstance(artifact, dict):
            if source_id is None:
                for generic_artifact in artifacts.values():
                    if (
                        isinstance(generic_artifact, dict)
                        and generic_artifact.get("required") is True
                    ):
                        generic_artifact["admissibility"] = "inadmissible"
                        generic_artifact["diagnostics"] = [
                            *cast(
                                "Sequence[Mapping[str, object]]",
                                generic_artifact.get("diagnostics", []),
                            ),
                            dict(diagnostic),
                        ]
            continue
        detail = str(diagnostic.get("detail"))
        artifact["admissibility"] = (
            "missing" if detail.endswith("-missing") else "inadmissible"
        )
        if artifact["admissibility"] == "missing":
            artifact["artifact-ref"] = None
            artifact["artifact-instance-id"] = None
            artifact["content-digest"] = None
        artifact["diagnostics"] = [dict(diagnostic)]


def _ci_aggregate_namespace_enumeration_unavailable(
    root: Path | None,
    *,
    expected_run_id: str,
    expected_run_attempt: str,
) -> bool:
    return _ci_aggregate_downloader_namespace_observation(
        root,
        expected_run_id=expected_run_id,
        expected_run_attempt=expected_run_attempt,
    )[0]


def _ci_aggregate_downloader_namespace_observation(
    root: Path | None,
    *,
    expected_run_id: str,
    expected_run_attempt: str,
) -> tuple[bool, bool]:
    if root is None:
        return (True, False)
    observation_path = root / _CI_DOWNLOADER_OBSERVATION_FILE
    if not observation_path.is_file():
        return (True, False)
    try:
        observation = _read_json(observation_path)
    except (OSError, TypeError, ValueError, json.JSONDecodeError):
        return (True, False)
    if not isinstance(observation, dict):
        return (True, False)
    metadata_available = observation.get("artifact-api-metadata-available")
    namespace_enumeration = observation.get("namespace-enumeration")
    namespace_overflow = observation.get("namespace-overflow")
    run_id = observation.get("run-id")
    run_attempt = observation.get("run-attempt")
    if (
        not isinstance(metadata_available, bool)
        or namespace_enumeration not in {"available", "unavailable"}
        or not isinstance(namespace_overflow, bool)
        or run_id != expected_run_id
        or run_attempt != expected_run_attempt
    ):
        return (True, False)
    return (
        metadata_available is False or namespace_enumeration == "unavailable",
        namespace_overflow,
    )


def _ci_aggregate_namespace_overflow(
    pre_final_count: int,
    *,
    enumeration_unavailable: bool = False,
    downloader_observed_overflow: bool = False,
) -> Json:
    lower_bound_overflow = (
        pre_final_count > _CI_VALIDATION_LIVE_NAMESPACE_ARTIFACT_CAP
    )
    detected = lower_bound_overflow or downloader_observed_overflow
    diagnostics: list[Mapping[str, object]] = []
    if enumeration_unavailable:
        diagnostics.append(
            _ci_aggregate_diagnostic(
                "namespace-closure-failure/enumeration-unavailable",
                code=DiagnosticFamily.NAMESPACE_CLOSURE_FAILURE.value,
                detail=DiagnosticDetail.NAMESPACE_ENUMERATION_UNAVAILABLE.value,
                message=(
                    "CI validation artifact namespace enumeration was "
                    "unavailable."
                ),
                source_id=None,
                severity=DiagnosticSeverity.FAIL_CLOSED.value,
                verdict_effect=DiagnosticVerdictEffect.FAIL_CLOSED.value,
            )
        )
    if detected:
        diagnostics.append(
            _ci_aggregate_diagnostic(
                "namespace-closure-failure/namespace-overflow",
                code=DiagnosticFamily.NAMESPACE_CLOSURE_FAILURE.value,
                detail=DiagnosticDetail.NAMESPACE_OVERFLOW.value,
                message="CI validation pre-final artifact namespace overflowed.",
                source_id=None,
                severity=DiagnosticSeverity.FAIL_CLOSED.value,
                verdict_effect=DiagnosticVerdictEffect.FAIL_CLOSED.value,
            )
        )
    return {
        "detected": detected,
        "observed-prefixed-artifact-count-lower-bound": pre_final_count,
        "max-prefixed-validation-artifacts": (
            _CI_VALIDATION_LIVE_NAMESPACE_ARTIFACT_CAP
        ),
        "diagnostics": sorted(
            [dict(item) for item in diagnostics],
            key=lambda item: str(item.get("diagnostic-id")),
        ),
    }


def _ci_aggregate_unexpected_artifact_sentinel_count(
    pre_final_count: int,
) -> int:
    return max(
        0,
        _CI_VALIDATION_LIVE_NAMESPACE_ARTIFACT_CAP + 1 - pre_final_count,
    )


def _ci_aggregate_pre_final_input_count(
    input_artifacts: Mapping[str, object],
) -> int:
    return sum(
        1
        for artifact in input_artifacts.values()
        if isinstance(artifact, Mapping)
        and (
            artifact.get("required") is True
            or artifact.get("expected-cardinality") == 1
            or artifact.get("artifact-ref") is not None
        )
    )


def _ci_aggregate_required_input_failure(
    input_artifacts: Mapping[str, object],
) -> bool:
    return any(
        isinstance(artifact, Mapping)
        and artifact.get("required") is True
        and artifact.get("admissibility") != "valid"
        for artifact in input_artifacts.values()
    )


def _ci_aggregate_evidence_results(
    *,
    plan: Mapping[str, object],
    admitted_bundles: Sequence[Mapping[str, object]],
) -> list[Json]:
    rows_by_evidence_id: dict[str, Json] = {}
    for bundle in admitted_bundles:
        batch = cast("Mapping[str, object]", bundle["batch"])
        for selector in cast(
            "Sequence[Mapping[str, object]]",
            bundle.get("selector-results", []),
        ):
            outcome = _ci_selector_outcome_to_summary(
                str(selector.get("outcome"))
            )
            rows_by_evidence_id[str(selector["expected-evidence-id"])] = {
                "evidence-expectation-id": selector["expected-evidence-id"],
                "work-group-id": selector["work-group-id"],
                "batch-id": batch["batch-id"],
                "bundle-id": bundle["bundle-id"],
                "selector-index": selector["selector-index"],
                "outcome": outcome,
                "diagnostics": list(
                    cast(
                        "Sequence[Mapping[str, object]]",
                        selector["diagnostics"],
                    )
                ),
            }
    results: list[Json] = []
    for expectation in cast(
        "Sequence[Mapping[str, object]]",
        plan.get("evidence-expectations", []),
    ):
        evidence_id = str(expectation["evidence-expectation-id"])
        if evidence_id in rows_by_evidence_id:
            results.append(rows_by_evidence_id[evidence_id])
        else:
            results.append(
                {
                    "evidence-expectation-id": evidence_id,
                    "work-group-id": expectation["work-group-id"],
                    "batch-id": None,
                    "bundle-id": None,
                    "selector-index": None,
                    "outcome": "missing",
                    "diagnostics": [
                        _ci_aggregate_diagnostic(
                            f"required-evidence-missing/{evidence_id}",
                            code=DiagnosticFamily.REQUIRED_EVIDENCE_MISSING.value,
                            detail=DiagnosticDetail.MISSING_BUNDLE.value,
                            message="Required evidence was not admitted.",
                            source_id=evidence_id,
                            severity=DiagnosticSeverity.BLOCKING_FAILURE.value,
                            verdict_effect=DiagnosticVerdictEffect.FAILED.value,
                        )
                    ],
                }
            )
    return sorted(
        results, key=lambda item: str(item["evidence-expectation-id"])
    )


def _ci_selector_outcome_to_summary(outcome: str) -> str:
    if outcome == "success":
        return "satisfied"
    if outcome == "skipped":
        return "skipped"
    return "failed"


def _ci_aggregate_summary_bundle_rows(
    bundle_slots: Sequence[Mapping[str, object]],
    *,
    admitted_bundles: Sequence[Mapping[str, object]],
) -> list[Json]:
    bundle_by_batch_id = {
        cast(
            "str", cast("Mapping[str, object]", bundle["batch"])["batch-id"]
        ): bundle
        for bundle in admitted_bundles
    }
    rows: list[Json] = []
    for slot in bundle_slots:
        batch_id = str(slot["batch-id"])
        candidates = cast(
            "Sequence[Mapping[str, object]]",
            slot.get("observed-candidates", []),
        )
        admitted_bundle = bundle_by_batch_id.get(batch_id)
        rows.append(
            {
                "batch-id": batch_id,
                "artifact-ref": slot["artifact-ref"],
                "bundle-id": admitted_bundle.get("bundle-id")
                if admitted_bundle is not None
                else None,
                "admitted-candidate-id": slot.get("admitted-candidate-id"),
                "candidate-count": len(candidates),
                "admissibility": slot["slot-admissibility"],
                "diagnostics": list(
                    cast("Sequence[Mapping[str, object]]", slot["diagnostics"])
                ),
            }
        )
    return sorted(rows, key=lambda item: str(item["batch-id"]))


def _ci_aggregate_summary_failures(  # noqa: C901, PLR0912
    *,
    summary_bundle_rows: Sequence[Mapping[str, object]],
    evidence_results: Sequence[Mapping[str, object]],
    namespace_overflow: Mapping[str, object],
    unexpected_artifacts: Sequence[Mapping[str, object]],
    required_input_failure: bool,
    aggregate_manifest_producer_verified: bool,
    aggregate_manifest_authority_diagnostics: Sequence[
        Mapping[str, object]
    ] = (),
    aggregate_summary_without_manifest: bool = False,
) -> list[Json]:
    failures: list[Json] = []
    for row in evidence_results:
        outcome = row.get("outcome")
        if outcome == "missing":
            failures.append(
                _ci_aggregate_failure(
                    kind="required-evidence-missing",
                    diagnostic=_ci_aggregate_diagnostic(
                        f"required-evidence-missing/{row['evidence-expectation-id']}",
                        code=DiagnosticFamily.REQUIRED_EVIDENCE_MISSING.value,
                        detail=DiagnosticDetail.MISSING_BUNDLE.value,
                        message="Required evidence was missing.",
                        source_id=cast("str", row.get("work-group-id")),
                        source_type="work-group",
                        severity=DiagnosticSeverity.BLOCKING_FAILURE.value,
                        verdict_effect=DiagnosticVerdictEffect.FAILED.value,
                    ),
                    message="Required evidence was missing.",
                    evidence_expectation_id=row.get("evidence-expectation-id"),
                    work_group_id=row.get("work-group-id"),
                    batch_id=row.get("batch-id"),
                    bundle_id=row.get("bundle-id"),
                )
            )
        elif outcome == "skipped":
            failures.append(
                _ci_aggregate_failure(
                    kind="required-evidence-skipped",
                    diagnostic=_ci_aggregate_diagnostic(
                        f"required-evidence-skipped/{row['evidence-expectation-id']}",
                        code=DiagnosticFamily.REQUIRED_EVIDENCE_SKIPPED.value,
                        detail=DiagnosticDetail.DEPENDENCY_BLOCKED.value,
                        message="Required evidence was skipped.",
                        source_id=cast("str", row.get("work-group-id")),
                        source_type="work-group",
                        severity=DiagnosticSeverity.BLOCKING_FAILURE.value,
                        verdict_effect=DiagnosticVerdictEffect.FAILED.value,
                    ),
                    message="Required evidence was skipped.",
                    evidence_expectation_id=row.get("evidence-expectation-id"),
                    work_group_id=row.get("work-group-id"),
                    batch_id=row.get("batch-id"),
                    bundle_id=row.get("bundle-id"),
                )
            )
        elif outcome == "failed":
            failures.append(
                _ci_aggregate_failure(
                    kind="blocking-validation-failure",
                    diagnostic=_ci_aggregate_diagnostic(
                        f"blocking-validation-failure/{row['evidence-expectation-id']}",
                        code=DiagnosticFamily.BLOCKING_VALIDATION_FAILURE.value,
                        detail=DiagnosticDetail.BLOCKING_VALIDATION_FAILURE.value,
                        message="Required validation evidence failed.",
                        source_id=cast("str", row.get("work-group-id")),
                        source_type="work-group",
                        severity=DiagnosticSeverity.BLOCKING_FAILURE.value,
                        verdict_effect=DiagnosticVerdictEffect.FAILED.value,
                    ),
                    message="Required validation evidence failed.",
                    evidence_expectation_id=row.get("evidence-expectation-id"),
                    work_group_id=row.get("work-group-id"),
                    batch_id=row.get("batch-id"),
                    bundle_id=row.get("bundle-id"),
                )
            )
    for row in summary_bundle_rows:
        if row.get("admissibility") == "valid":
            continue
        diagnostic = _ci_aggregate_diagnostic(
            f"inadmissible-batch-evidence/{row['batch-id']}",
            code=DiagnosticFamily.INADMISSIBLE_BATCH_EVIDENCE.value,
            detail=_ci_summary_bundle_inadmissibility_detail(row),
            message="Required batch evidence was not admissible.",
            source_id=None,
            severity=DiagnosticSeverity.BLOCKING_FAILURE.value,
            verdict_effect=DiagnosticVerdictEffect.FAILED.value,
        )
        failures.append(
            _ci_aggregate_failure(
                kind="inadmissible-batch-evidence",
                diagnostic=diagnostic,
                message="Required batch evidence was not admissible.",
                batch_id=row.get("batch-id"),
            )
        )
    if required_input_failure:
        diagnostic = _ci_aggregate_diagnostic(
            "required-input-artifact-failure",
            code=DiagnosticFamily.REQUIRED_INPUT_ARTIFACT_FAILURE.value,
            detail=DiagnosticDetail.REQUIRED_INPUT_ARTIFACT_FAILURE.value,
            message="Required input artifact evidence was not admissible.",
            source_id=None,
            severity=DiagnosticSeverity.FAIL_CLOSED.value,
            verdict_effect=DiagnosticVerdictEffect.FAIL_CLOSED.value,
        )
        failures.append(
            _ci_aggregate_failure(
                kind="required-input-artifact-failure",
                diagnostic=diagnostic,
                message="Required input artifact evidence was not admissible.",
            )
        )
    if not aggregate_manifest_producer_verified:
        diagnostic = _ci_aggregate_diagnostic(
            "final-producer-unverified",
            code=DiagnosticFamily.FINAL_PRODUCER_UNVERIFIED.value,
            detail=DiagnosticDetail.FINAL_PRODUCER_UNVERIFIED.value,
            message=(
                "Aggregate evidence manifest producer boundary was not "
                "verified before summary generation."
            ),
            source_id=None,
            severity=DiagnosticSeverity.FAIL_CLOSED.value,
            verdict_effect=DiagnosticVerdictEffect.FAIL_CLOSED.value,
        )
        failures.append(
            _ci_aggregate_failure(
                kind="final-producer-unverified",
                diagnostic=diagnostic,
                message=(
                    "Aggregate evidence manifest producer boundary was not "
                    "verified before summary generation."
                ),
            )
        )
    for diagnostic in aggregate_manifest_authority_diagnostics:
        message = str(diagnostic.get("message") or "Final manifest failed.")
        failures.append(
            _ci_aggregate_failure(
                kind="final-evidence-failure",
                diagnostic=diagnostic,
                message=message,
            )
        )
    if aggregate_summary_without_manifest:
        diagnostic = _ci_aggregate_summary_without_manifest_diagnostic()
        failures.append(
            _ci_aggregate_failure(
                kind="aggregate-summary-without-manifest",
                diagnostic=diagnostic,
                message="Aggregate summary was generated without final manifest bytes.",
            )
        )
    if unexpected_artifacts:
        diagnostic = _ci_aggregate_diagnostic(
            "namespace-closure-failure/unexpected-contract-artifact",
            code=DiagnosticFamily.NAMESPACE_CLOSURE_FAILURE.value,
            detail=DiagnosticDetail.UNEXPECTED_CONTRACT_ARTIFACT.value,
            message="Validation artifact namespace was not closed.",
            source_id=None,
            severity=DiagnosticSeverity.FAIL_CLOSED.value,
            verdict_effect=DiagnosticVerdictEffect.FAIL_CLOSED.value,
        )
        failures.append(
            _ci_aggregate_failure(
                kind="namespace-closure-failure",
                diagnostic=diagnostic,
                message="Validation artifact namespace was not closed.",
            )
        )
    if namespace_overflow.get("detected") is True:
        diagnostic = _ci_aggregate_diagnostic(
            "namespace-closure-failure/namespace-overflow",
            code=DiagnosticFamily.NAMESPACE_CLOSURE_FAILURE.value,
            detail=DiagnosticDetail.NAMESPACE_OVERFLOW.value,
            message="Validation artifact namespace overflowed.",
            source_id=None,
            severity=DiagnosticSeverity.FAIL_CLOSED.value,
            verdict_effect=DiagnosticVerdictEffect.FAIL_CLOSED.value,
        )
        failures.append(
            _ci_aggregate_failure(
                kind="namespace-closure-failure",
                diagnostic=diagnostic,
                message="Validation artifact namespace overflowed.",
            )
        )
    if _ci_aggregate_namespace_enumeration_failed(namespace_overflow):
        diagnostic = _ci_aggregate_diagnostic(
            "namespace-closure-failure/enumeration-unavailable",
            code=DiagnosticFamily.NAMESPACE_CLOSURE_FAILURE.value,
            detail=DiagnosticDetail.NAMESPACE_ENUMERATION_UNAVAILABLE.value,
            message="Validation artifact namespace enumeration was unavailable.",
            source_id=None,
            severity=DiagnosticSeverity.FAIL_CLOSED.value,
            verdict_effect=DiagnosticVerdictEffect.FAIL_CLOSED.value,
        )
        failures.append(
            _ci_aggregate_failure(
                kind="namespace-closure-failure",
                diagnostic=diagnostic,
                message=(
                    "Validation artifact namespace enumeration was unavailable."
                ),
            )
        )
    return sorted(failures, key=_ci_aggregate_failure_sort_key)


def _ci_summary_bundle_inadmissibility_detail(
    row: Mapping[str, object],
) -> str:
    diagnostics = row.get("diagnostics")
    if isinstance(diagnostics, Sequence) and not isinstance(
        diagnostics, str | bytes
    ):
        for diagnostic in diagnostics:
            if not isinstance(diagnostic, Mapping):
                continue
            detail = diagnostic.get("detail")
            if isinstance(detail, str) and detail:
                return detail
    if row.get("admissibility") == "missing":
        return DiagnosticDetail.MISSING_BUNDLE.value
    if row.get("admissibility") == "duplicate":
        return DiagnosticDetail.DUPLICATE_BUNDLE_CANDIDATES.value
    return DiagnosticDetail.MALFORMED_BUNDLE.value


def _ci_aggregate_summary_reason(
    *,
    summary_bundle_rows: Sequence[Mapping[str, object]],
    evidence_results: Sequence[Mapping[str, object]],
    namespace_overflow: Mapping[str, object],
    unexpected_artifacts: Sequence[Mapping[str, object]],
    required_input_failure: bool,
    aggregate_manifest_producer_verified: bool,
    aggregate_manifest_authority_failure: bool = False,
    aggregate_summary_without_manifest: bool = False,
) -> dict[str, bool]:
    outcomes = [row.get("outcome") for row in evidence_results]
    namespace_failure = (
        bool(unexpected_artifacts)
        or namespace_overflow.get("detected") is True
        or _ci_aggregate_namespace_enumeration_failed(namespace_overflow)
    )
    final_evidence_failure = aggregate_manifest_authority_failure
    return {
        "invalid-plan": False,
        "fail-closed": namespace_failure,
        "required-evidence-missing": "missing" in outcomes,
        "required-evidence-skipped": "skipped" in outcomes,
        "blocking-validation-failure": "failed" in outcomes,
        "inadmissible-batch-evidence": any(
            row.get("admissibility") != "valid" for row in summary_bundle_rows
        ),
        "namespace-closure-failure": namespace_failure,
        "required-input-artifact-failure": required_input_failure,
        "aggregate-summary-without-manifest": aggregate_summary_without_manifest,
        "final-producer-unverified": not aggregate_manifest_producer_verified,
        "final-evidence-failure": final_evidence_failure,
    }


def _ci_aggregate_namespace_enumeration_failed(
    namespace_overflow: Mapping[str, object],
) -> bool:
    diagnostics = namespace_overflow.get("diagnostics")
    if not isinstance(diagnostics, Sequence) or isinstance(
        diagnostics,
        str | bytes,
    ):
        return False
    return any(
        isinstance(diagnostic, Mapping)
        and diagnostic.get("code")
        == DiagnosticFamily.NAMESPACE_CLOSURE_FAILURE.value
        and diagnostic.get("detail")
        == DiagnosticDetail.NAMESPACE_ENUMERATION_UNAVAILABLE.value
        for diagnostic in diagnostics
    )


def _ci_aggregate_work_group_counts(
    evidence_results: Sequence[Mapping[str, object]],
) -> dict[str, object]:
    outcomes = [row.get("outcome") for row in evidence_results]
    return {
        "executable-required": len(evidence_results),
        "required-succeeded": outcomes.count("satisfied"),
        "required-failed": outcomes.count("failed"),
        "required-skipped": outcomes.count("skipped"),
        "required-missing": outcomes.count("missing"),
        "terminal-aggregation": "present",
    }


def _ci_aggregate_summary_budgets(
    execution_batch_manifest: Mapping[str, object],
    *,
    pre_final_validation_artifacts: int,
    aggregate_duration_seconds: int,
) -> dict[str, object]:
    budget = cast("Mapping[str, object]", execution_batch_manifest["budget"])
    return {
        "pre-final-validation-artifacts": pre_final_validation_artifacts,
        "expected-final-validation-artifacts": 2,
        "expected-actual-validation-artifacts": pre_final_validation_artifacts
        + 2,
        "max-validation-artifacts": budget["max-validation-artifacts"],
        "actual-execution-batches": budget["actual-execution-batches"],
        "actual-total-jobs": budget["actual-total-jobs"],
        "actual-windows-jobs": budget["actual-windows-jobs"],
        "aggregate-duration-seconds": aggregate_duration_seconds,
        "aggregate-target-duration-seconds": budget[
            "aggregate-target-duration-seconds"
        ],
        "aggregate-max-duration-seconds": budget[
            "aggregate-max-duration-seconds"
        ],
    }


def _ci_aggregate_duration_seconds(started_at: str, completed_at: str) -> int:
    if not _ci_is_contract_valid_rfc3339_timestamp(
        started_at
    ) or not _ci_is_contract_valid_rfc3339_timestamp(completed_at):
        return _CI_INVALID_AGGREGATE_DURATION_SECONDS
    try:
        started = datetime.fromisoformat(started_at.replace("Z", "+00:00"))
        completed = datetime.fromisoformat(completed_at.replace("Z", "+00:00"))
        duration_seconds = (completed - started).total_seconds()
    except (TypeError, ValueError):
        return _CI_INVALID_AGGREGATE_DURATION_SECONDS
    if (
        started.tzinfo is None
        or completed.tzinfo is None
        or started.utcoffset() is None
        or completed.utcoffset() is None
        or duration_seconds < 0
    ):
        return _CI_INVALID_AGGREGATE_DURATION_SECONDS
    return math.ceil(duration_seconds) if duration_seconds > 0 else 0


def _ci_summary_affected_range(plan: Mapping[str, object]) -> dict[str, object]:
    affected = cast("Mapping[str, object]", plan["affected-range"])
    return {
        "status": affected["status"],
        "base-sha": affected["base-sha"],
        "base-tip-sha": affected["base-tip-sha"],
        "head-sha": affected["head-sha"],
        "changed-files-hash": affected["changed-files-hash"] or None,
    }


def _ci_aggregate_failure(
    *,
    kind: str,
    diagnostic: Mapping[str, object],
    message: str,
    evidence_expectation_id: object = None,
    work_group_id: object = None,
    batch_id: object = None,
    bundle_id: object = None,
) -> Json:
    return {
        "kind": kind,
        "batch-id": batch_id,
        "work-group-id": work_group_id,
        "evidence-expectation-id": evidence_expectation_id,
        "bundle-id": bundle_id,
        "diagnostic": dict(diagnostic),
        "message": message,
    }


def _ci_aggregate_failure_sort_key(
    item: Mapping[str, object],
) -> tuple[str, str, str, str, str, str]:
    return (
        str(item.get("kind") or ""),
        str(item.get("evidence-expectation-id") or ""),
        str(item.get("work-group-id") or ""),
        str(item.get("batch-id") or ""),
        str(item.get("bundle-id") or ""),
        canonical_json_digest(item),
    )


def _ci_aggregate_diagnostic(
    diagnostic_id: str,
    *,
    code: str,
    detail: str,
    message: str,
    source_id: str | None,
    severity: str,
    verdict_effect: str,
    source_type: str = "aggregation",
) -> dict[str, object]:
    return ci_validation_diagnostic(
        diagnostic_id=diagnostic_id,
        code=code,
        detail=detail,
        message=message,
        source_type=source_type,
        source_id=source_id,
        severity=severity,
        verdict_effect=verdict_effect,
    )


def _ci_malformed_control_input_diagnostic(
    artifact_ref: str,
    *,
    code: str = DiagnosticFamily.INADMISSIBLE_BATCH_EVIDENCE.value,
    detail: str,
    message: str,
) -> Mapping[str, object]:
    artifact_kind = _ci_control_artifact_kind(artifact_ref)
    return _ci_aggregate_diagnostic(
        f"{code}/{artifact_kind}-malformed",
        code=code,
        detail=detail,
        message=message,
        source_id=artifact_ref,
        severity=DiagnosticSeverity.FAIL_CLOSED.value,
        verdict_effect=DiagnosticVerdictEffect.FAIL_CLOSED.value,
    )


def _ci_execution_batch_manifest_validation_detail(
    exc: ContractValidationError,
    manifest: Mapping[str, object],
) -> str:
    for issue in exc.issues:
        if (
            issue.path == "$.plan-id"
            and issue.message == "must match plan"
            and isinstance(manifest.get("plan-id"), str)
        ):
            return DiagnosticDetail.EXECUTION_BATCH_MANIFEST_PLAN_MISMATCH.value
        if (
            issue.path.endswith(".expected-batch-evidence-bundle-ref")
            and issue.message == "must match batch id"
        ):
            return DiagnosticDetail.EXECUTION_BATCH_MANIFEST_BUNDLE_REF_MISMATCH.value
        if (
            issue.path == "$.plan-digest"
            and issue.message == "must match plan"
            and isinstance(manifest.get("plan-digest"), str)
        ):
            return (
                DiagnosticDetail.EXECUTION_BATCH_MANIFEST_DIGEST_MISMATCH.value
            )
    return DiagnosticDetail.EXECUTION_BATCH_MANIFEST_MALFORMED.value


def _ci_execution_batch_manifest_validation_diagnostic(
    artifact_ref: str,
    *,
    detail: str,
    message: str,
) -> Mapping[str, object]:
    return _ci_aggregate_diagnostic(
        f"{DiagnosticFamily.INADMISSIBLE_BATCH_EVIDENCE.value}/{detail}",
        code=DiagnosticFamily.INADMISSIBLE_BATCH_EVIDENCE.value,
        detail=detail,
        message=message,
        source_id=artifact_ref,
        severity=DiagnosticSeverity.FAIL_CLOSED.value,
        verdict_effect=DiagnosticVerdictEffect.FAIL_CLOSED.value,
    )


def _ci_invalid_plan_detail_for_validation_error(
    plan: Mapping[str, object],
) -> str:
    plan_digest = plan.get("plan-digest")
    if not isinstance(plan_digest, str):
        return DiagnosticDetail.SCHEMA_INVALID.value
    recomputed_plan_digest = ci_validation_plan_digest(plan)
    if plan_digest == recomputed_plan_digest:
        return DiagnosticDetail.STRUCTURALLY_INVALID.value
    if len(plan_digest) == 64 and all(
        character in "0123456789abcdef" for character in plan_digest
    ):
        return DiagnosticDetail.PLAN_DIGEST_MISMATCH.value
    return DiagnosticDetail.SCHEMA_INVALID.value


def _ci_control_input_raw_digest(value: str) -> str | None:
    if not value:
        return None
    path = Path(value)
    if not path.is_file():
        return None
    try:
        return hashlib.sha256(path.read_bytes()).hexdigest()
    except OSError:
        return None


def _ci_invalid_plan_control_diagnostic(
    args: argparse.Namespace,
    *,
    detail: str,
    message: str,
) -> Mapping[str, object]:
    return _ci_malformed_control_input_diagnostic(
        ci_validation_plan_artifact_ref(
            run_id=str(args.run_id),
            run_attempt=str(args.run_attempt),
        ),
        code=DiagnosticFamily.INVALID_PLAN.value,
        detail=detail,
        message=message,
    )


def _ci_invalid_plan_input_artifacts(
    args: argparse.Namespace,
    *,
    invalid_plan_context: Mapping[str, object] | None,
    boundary_diagnostics: Sequence[Mapping[str, object]],
) -> dict[str, object]:
    run_id = str(args.run_id)
    run_attempt = str(args.run_attempt)
    plan_ref = ci_validation_plan_artifact_ref(
        run_id=run_id,
        run_attempt=run_attempt,
    )
    changed_files_hash: object = None
    fact_snapshot_id: object = None
    plan_digest: object = None
    if invalid_plan_context is not None:
        plan_digest = invalid_plan_context.get("plan-digest")
        affected_range = invalid_plan_context.get("affected-range")
        if isinstance(affected_range, Mapping):
            changed_files_hash = affected_range.get("changed-files-hash")
        fact_projection = invalid_plan_context.get("fact-snapshot")
        if isinstance(fact_projection, Mapping):
            fact_snapshot_id = fact_projection.get("id")
    for diagnostic in boundary_diagnostics:
        source = diagnostic.get("source")
        source_id = (
            source.get("id")
            if isinstance(source, Mapping)
            else diagnostic.get("source-id")
        )
        if source_id == plan_ref and not isinstance(plan_digest, str):
            plan_digest = _ci_control_input_raw_digest(
                str(getattr(args, "plan", ""))
            )
    artifacts: dict[str, object] = {
        "request": _ci_aggregate_input_artifact(
            ci_validation_request_artifact_ref(
                run_id=run_id,
                run_attempt=run_attempt,
            ),
            content_digest=None,
            required=True,
            artifact_instance_id=None,
            require_artifact_instance_id=True,
        ),
        "validation-plan": _ci_aggregate_input_artifact(
            plan_ref,
            content_digest=plan_digest
            if isinstance(plan_digest, str)
            else None,
            required=True,
            artifact_instance_id=(
                getattr(args, "expected_plan_artifact_id", None)
                or artifact_physical_name(plan_ref)
            ),
        ),
        "changed-files-snapshot": _ci_aggregate_input_artifact(
            ci_validation_changed_files_snapshot_artifact_ref(
                run_id=run_id,
                run_attempt=run_attempt,
            )
            if isinstance(changed_files_hash, str)
            else None,
            content_digest=changed_files_hash
            if isinstance(changed_files_hash, str)
            else None,
            required=isinstance(changed_files_hash, str),
            artifact_instance_id=getattr(
                args,
                "expected_changed_files_snapshot_artifact_id",
                None,
            ),
            require_artifact_instance_id=isinstance(changed_files_hash, str),
        ),
        "fact-snapshot": _ci_aggregate_input_artifact(
            ci_validation_fact_snapshot_artifact_ref(
                run_id=run_id,
                run_attempt=run_attempt,
            )
            if isinstance(fact_snapshot_id, str)
            else None,
            content_digest=fact_snapshot_id
            if isinstance(fact_snapshot_id, str)
            else None,
            required=isinstance(fact_snapshot_id, str),
            artifact_instance_id=getattr(
                args, "expected_fact_snapshot_artifact_id", None
            ),
            require_artifact_instance_id=isinstance(fact_snapshot_id, str),
        ),
        "execution-batch-manifest": _ci_aggregate_input_artifact(
            ci_validation_execution_batch_manifest_artifact_ref(
                run_id=run_id,
                run_attempt=run_attempt,
            ),
            content_digest=None,
            required=True,
            artifact_instance_id=None,
            require_artifact_instance_id=True,
        ),
    }
    _ci_apply_boundary_diagnostics_to_input_artifacts(
        artifacts,
        boundary_diagnostics,
    )
    if invalid_plan_context is not None:
        for key in ("changed-files-snapshot", "fact-snapshot"):
            artifact = artifacts.get(key)
            if (
                isinstance(artifact, dict)
                and artifact.get("admissibility") == "valid"
            ):
                artifact["admissibility"] = "inadmissible"
    return artifacts


def _ci_has_malformed_snapshot_control_diagnostic(
    diagnostics: Sequence[Mapping[str, object]],
) -> bool:
    return any(
        diagnostic.get("detail")
        in {
            DiagnosticDetail.CHANGED_FILES_SNAPSHOT_MALFORMED.value,
            DiagnosticDetail.FACT_SNAPSHOT_MALFORMED.value,
        }
        for diagnostic in diagnostics
    )


def _ci_aggregate_control_artifact_boundary_diagnostics(
    args: argparse.Namespace,
) -> list[Mapping[str, object]]:
    expected = _ci_expected_aggregate_input_artifacts(args)
    if not expected:
        return []
    max_prefixed_validation_artifacts = (
        _CI_VALIDATION_PRE_FINAL_NAMESPACE_ARTIFACT_CAP
    )
    excluded_prefixed_artifact_names = (
        _ci_current_final_aggregate_artifact_names_for_phase(
            aggregate_phase=str(getattr(args, "aggregate_phase", "all")),
            run_id=str(args.run_id),
            run_attempt=str(args.run_attempt),
        )
        | _ci_known_prior_attempt_artifact_names_from_expected(
            expected,
            run_id=str(args.run_id),
            run_attempt=str(args.run_attempt),
        )
    )
    try:
        artifacts = _github_actions_run_artifacts_for_boundary_check(
            repository=args.repository,
            run_id=str(args.run_id),
            run_attempt=str(args.run_attempt),
            max_prefixed_validation_artifacts=max_prefixed_validation_artifacts,
            excluded_prefixed_artifact_names=excluded_prefixed_artifact_names,
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
                detail=DiagnosticDetail.FINAL_PRODUCER_UNVERIFIED.value,
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
            "expected_execution_batch_manifest_artifact_id",
            ci_validation_execution_batch_manifest_artifact_ref(
                run_id=run_id,
                run_attempt=run_attempt,
            ),
            "materialize-execution-batches",
            "materialize-execution-batches",
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


def _download_artifact_by_id(
    repository: str,
    artifact_api: Mapping[str, object] | None,
    artifact_name_value: str,
    destination: Path,
) -> None:
    if artifact_api is None:
        msg = f"expected batch artifact {artifact_name_value} has no admitted artifact API identity"
        raise RuntimeError(msg)
    artifact_id = artifact_api.get("id")
    if artifact_id is None or not str(artifact_id):
        msg = f"expected batch artifact {artifact_name_value} has no admitted artifact id"
        raise RuntimeError(msg)
    if artifact_api.get("expired") is True:
        msg = f"expected batch artifact {artifact_name_value} is expired"
        raise RuntimeError(msg)
    destination.mkdir(parents=True, exist_ok=True)
    env = os.environ.copy()
    if "GH_TOKEN" not in env and "GITHUB_TOKEN" in env:
        env["GH_TOKEN"] = env["GITHUB_TOKEN"]
    endpoint = f"repos/{repository}/actions/artifacts/{artifact_id}/zip"
    result = subprocess.run(
        ["gh", "api", endpoint],
        cwd=Path.cwd(),
        env=env,
        capture_output=True,
        check=False,
    )
    if result.returncode != 0:
        msg = (
            f"failed to download admitted artifact id {artifact_id} "
            f"for {artifact_name_value}: {result.stderr.decode('utf-8', 'replace').strip()}"
        )
        raise RuntimeError(msg)
    try:
        with zipfile.ZipFile(io.BytesIO(result.stdout)) as archive:
            archive.extractall(destination)
    except zipfile.BadZipFile as exc:
        msg = (
            f"GitHub artifact id {artifact_id} for {artifact_name_value} "
            "did not return a readable zip archive"
        )
        raise RuntimeError(msg) from exc


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
    run_attempt: str | None = None,
    prefixed_artifact_cap: int | None = None,
    excluded_prefixed_artifact_names: Collection[str] = (),
) -> list[Mapping[str, object]]:
    if prefixed_artifact_cap is not None:
        if run_attempt is None:
            msg = "run_attempt is required for bounded CI artifact enumeration"
            raise ValueError(msg)
        return _github_actions_run_prefixed_artifacts_bounded(
            repository=repository,
            run_id=run_id,
            run_attempt=run_attempt,
            prefixed_artifact_cap=prefixed_artifact_cap,
            excluded_prefixed_artifact_names=excluded_prefixed_artifact_names,
        )
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


def _github_actions_run_prefixed_artifacts_bounded(
    *,
    repository: str,
    run_id: str,
    run_attempt: str,
    prefixed_artifact_cap: int,
    excluded_prefixed_artifact_names: Collection[str] = (),
) -> list[Mapping[str, object]]:
    artifacts: list[Mapping[str, object]] = []
    seen_artifact_ids: set[str] = set()
    counted_prefixed_artifacts = 0
    current_attempt_prefix = _ci_attempt_physical_artifact_name_prefix(
        run_id=run_id,
        run_attempt=run_attempt,
    )
    page_number = 1
    scanned_item_count = 0
    while True:
        page = _gh_api(
            repository,
            (
                f"repos/{repository}/actions/runs/{run_id}/artifacts"
                f"?per_page=100&page={page_number}"
            ),
        )
        page_items = _github_actions_run_artifact_page_items(
            page,
            page_number - 1,
        )
        scanned_item_count += len(page_items)
        _raise_for_ci_live_namespace_item_bound(scanned_item_count)
        if not page_items:
            break
        for artifact in page_items:
            artifact_id = artifact.get("id")
            if artifact_id is not None:
                artifact_id_key = str(artifact_id)
                if artifact_id_key in seen_artifact_ids:
                    continue
                seen_artifact_ids.add(artifact_id_key)
            artifact_name = artifact.get("name")
            if (
                artifact.get("expired") is True
                or not isinstance(artifact_name, str)
                or not artifact_name.startswith(current_attempt_prefix)
            ):
                continue
            artifacts.append(artifact)
            if artifact_name not in excluded_prefixed_artifact_names:
                counted_prefixed_artifacts += 1
            if counted_prefixed_artifacts > prefixed_artifact_cap:
                return artifacts
        if len(page_items) < 100:
            break
        _raise_for_ci_live_namespace_probe_bound(
            page_number=page_number,
            scanned_item_count=scanned_item_count,
        )
        page_number += 1
    return artifacts


def _raise_for_ci_live_namespace_item_bound(scanned_item_count: int) -> None:
    if scanned_item_count <= _CI_VALIDATION_LIVE_NAMESPACE_ENUMERATION_ITEM_CAP:
        return
    msg = (
        "CI validation artifact namespace enumeration unavailable during "
        "bounded probe: total artifact item bound exceeded"
    )
    raise RuntimeError(msg)


def _raise_for_ci_live_namespace_probe_bound(
    *,
    page_number: int,
    scanned_item_count: int,
) -> None:
    if (
        page_number < _CI_VALIDATION_LIVE_NAMESPACE_ENUMERATION_PAGE_CAP
        and scanned_item_count
        < _CI_VALIDATION_LIVE_NAMESPACE_ENUMERATION_ITEM_CAP
    ):
        return
    msg = (
        "CI validation artifact namespace enumeration unavailable during "
        "bounded probe: page or total artifact item bound reached before "
        "namespace closure"
    )
    raise RuntimeError(msg)


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
        return [
            _ci_builtin_command(
                "validate lightweight preflight policy",
                "lightweight-preflight",
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
                [
                    "uv",
                    "run",
                    "--project",
                    root,
                    "--python",
                    "3.13",
                    "--with",
                    "pyrefly",
                    "pyrefly",
                    "check",
                    root,
                    "--summary=none",
                    "--output-format=min-text",
                ],
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
        max(_ci_dependency_layer_index(item) for item in matrix) + 1
        if matrix
        else 0
    )
    return [
        [
            dict(item)
            for item in matrix
            if _ci_dependency_layer_index(item) == layer_index
        ]
        for layer_index in range(layer_count)
    ]


def _ci_dependency_layer_index(item: Mapping[str, object]) -> int:
    layer = item.get("dependency-layer")
    if isinstance(layer, int | str):
        return int(layer)
    msg = "dependency-layer must be an integer"
    raise TypeError(msg)


def _ci_dependency_blocked(
    *,
    plan: Mapping[str, object],
    assignments: Mapping[str, object],
    work_group_id: str,
    observed_artifacts_dir: str,
    changed_files_snapshot: Mapping[str, object] | None,
    fact_snapshot: Mapping[str, object] | None,
) -> bool:
    _ = (
        assignments,
        observed_artifacts_dir,
        changed_files_snapshot,
        fact_snapshot,
    )
    dependencies = [
        str(item)
        for item in cast(
            "Sequence[object]",
            _ci_work_group(plan, work_group_id).get("depends-on", []),
        )
    ]
    return bool(dependencies)


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
        if kind == "release-shaped-artifact":
            return ci_validation_diagnostic(
                diagnostic_id=(
                    f"artifact-shape-unconfirmed/{work_group_id}/bytes"
                ),
                code=DiagnosticFamily.ARTIFACT_SHAPE_UNCONFIRMED.value,
                detail=DiagnosticDetail.INCOMPLETE.value,
                message=(
                    "Release-shaped artifact validation did not produce "
                    "byte-bound no-publish artifact evidence."
                ),
                source_type="work-group",
                source_id=work_group_id,
                severity=DiagnosticSeverity.BLOCKING_FAILURE.value,
                verdict_effect=DiagnosticVerdictEffect.FAILED.value,
            )
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
            "prerequisite work group did not produce a dependency result "
            "admitted for gating from same-batch results or admitted upstream "
            "batch evidence."
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
) -> _CiValidationOutcome:
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
            fact_snapshot=fact_snapshot,
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
    return False


def _ci_release_shaped_results_from_validation_result(
    plan: Mapping[str, object],
    work_group_id: str,
    validation_result: Mapping[str, object],
    *,
    fact_snapshot: Mapping[str, object] | None,
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
    if not _ci_release_shaped_results_match_plan(
        plan,
        work_group_id,
        results,
        fact_snapshot=fact_snapshot,
    ):
        return None
    return results


def _ci_release_shaped_results_match_plan(
    plan: Mapping[str, object],
    work_group_id: str,
    results: Sequence[Mapping[str, object]],
    *,
    fact_snapshot: Mapping[str, object] | None,
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
            fact_snapshot=fact_snapshot,
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
    *,
    fact_snapshot: Mapping[str, object] | None,
) -> bool:
    artifact = result.get("artifact")
    release_receipt = result.get("release-receipt")
    descriptor = result.get("descriptor")
    if not isinstance(artifact, Mapping) or not isinstance(
        release_receipt, Mapping
    ):
        return False
    if not isinstance(descriptor, Mapping):
        return False
    descriptor_path = str(obligation.get("descriptor-path"))
    descriptor_fact = _ci_descriptor_fact(fact_snapshot, descriptor_path)
    expected_descriptor_identity = (
        descriptor_fact.get("descriptor-identity")
        if descriptor_fact is not None
        else None
    )
    if (
        not isinstance(expected_descriptor_identity, str)
        or not expected_descriptor_identity
    ):
        return False
    planned_artifact = obligation.get("artifact")
    planned_receipt = obligation.get("release-receipt")
    expected_refs = _ci_artifact_expected_refs(obligation)
    return (
        bool(expected_refs)
        and descriptor.get("path") == descriptor_path
        and descriptor.get("identity") == expected_descriptor_identity
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
        if category == "release-shaped-artifact" and outcome != "success":
            category_result["detail"] = _ci_validation_detail(
                plan,
                work_group_id,
                category,
                diagnostics,
                outcome=outcome,
                fact_snapshot=fact_snapshot,
            )
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
            fact_snapshot=fact_snapshot,
        )
        if release_results is not None:
            detail: Json = {
                "artifact-obligation-results": [
                    dict(result) for result in release_results
                ]
            }
            source_proof = (
                _ci_release_shaped_source_proof_from_validation_result(
                    validation_result,
                )
            )
            if source_proof is not None:
                detail.update(source_proof)
            artifact_refs = _ci_release_shaped_observed_refs(release_results)
            if batch_bundle:
                category_result["artifact-refs"] = artifact_refs
                category_result["detail"] = detail
            else:
                category_result["detail"] = detail
    return {
        "category": category,
        "planned-capabilities": None,
        "category-result": category_result,
        "artifact-refs": artifact_refs,
    }


def _ci_release_shaped_source_proof_from_validation_result(
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
    expected_refs = _ci_artifact_expected_refs(obligation)
    observed_refs: list[str] = []
    observed_digests: list[Json] = []
    if outcome == "blocking-failure" and any(
        diagnostic.get("code")
        == DiagnosticFamily.ARTIFACT_SHAPE_UNCONFIRMED.value
        for diagnostic in diagnostics
        if isinstance(diagnostic, Mapping)
    ):
        observed_refs = expected_refs
        observed_digests = [
            {
                "artifact-ref": artifact_ref,
                "algorithm": "sha256",
                "digest": "",
                "digest-available": False,
                "diagnostics": [dict(item) for item in diagnostics],
            }
            for artifact_ref in expected_refs
        ]
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
            "observed": {"refs": observed_refs, "digests": observed_digests},
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
