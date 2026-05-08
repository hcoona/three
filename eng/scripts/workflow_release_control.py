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
from pathlib import Path
from typing import Any

import yaml

_REPO_ROOT = Path(__file__).resolve().parents[2]
for _WORKSPACE_SRC in (
    _REPO_ROOT / "src/public/lib/three-workflow-release-contracts/src",
    _REPO_ROOT / "src/public/lib/three-workflow-release-proof/src",
):
    if _WORKSPACE_SRC.is_dir():
        sys.path.insert(0, str(_WORKSPACE_SRC))

from three_workflow_release_contracts import (  # noqa: E402
    ContractValidationError,
    validate_contract,
)
from three_workflow_release_contracts.artifact_names import (  # noqa: E402
    ArtifactNameInputs,
    artifact_name,
    github_release_asset_binding_json,
    immutable_binding_json,
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
_TOPOLOGIES = (
    "github-token",
    "external-oidc-entry-workflow",
    "external-oidc-caller-workflow",
    "external-oidc-reusable-workflow",
)
_PYPI_JSON_TIMEOUT_SECONDS = 15
_OFFICIAL_NON_PUBLIC_REF_CANARY_PROJECTS = frozenset({"hcoona-release-smoke"})


def main() -> int:
    """Run the requested control-plane helper command."""
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)
    _add_normalize_entry(subparsers)
    _add_artifact_name(subparsers)
    _add_write_request(subparsers)
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
    artifacts = {
        artifact_id: plan["graph"]["artifacts"][artifact_id]
        for artifact_id in variant["artifact-ids"]
    }
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
        if snapshot["family"] == "github-release":
            try:
                observations[node_id] = _observe_github_release_publication(
                    args.repository,
                    str(plan["envelope"]["commit-sha"]),
                    node,
                )
            except (RuntimeError, TypeError) as exc:
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
        if _supports_pypi_remote_observation(
            snapshot
        ) and _requires_live_external_remote_observation(
            node_id, node, snapshot_id, snapshot, execution_sets, enabled
        ):
            try:
                observations[node_id] = _observe_pypi_publication(node)
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
    if diagnostics:
        _write_json(
            Path(args.diagnostics_out), _diagnostics_document(diagnostics)
        )
        return 1
    _write_json(Path(args.out), observations)
    return 0


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
            if not _supports_pypi_remote_observation(snapshot):
                continue
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
    known_oidc = {
        snapshot_id
        for snapshot_id, snapshot in plan["graph"][
            "target-instance-snapshots"
        ].items()
        if snapshot["capabilities"]["credential-posture"] == "oidc"
    }
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
        "canary non-public-ref override is allowlisted only for hcoona-release-smoke",
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
    includes = (
        ref_condition.get("include")
        if isinstance(ref_condition.get("include"), list)
        else ["~ALL"]
    )
    excludes = (
        ref_condition.get("exclude")
        if isinstance(ref_condition.get("exclude"), list)
        else []
    )
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
    identity = node.get("resolved-publish-identity")
    if not isinstance(identity, Mapping):
        msg = "PyPI publish node is missing resolved-publish-identity"
        raise TypeError(msg)
    package_name = identity.get("package-name")
    version = identity.get("version")
    if not isinstance(package_name, str) or not package_name:
        msg = "PyPI publish identity is missing package-name"
        raise TypeError(msg)
    if not isinstance(version, str) or not version:
        msg = "PyPI publish identity is missing version"
        raise TypeError(msg)
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


def _pypi_project_json(package_name: str) -> Json | None:
    normalized = _pep503_name(package_name)
    url = (
        f"https://pypi.org/pypi/{urllib.parse.quote(normalized, safe='')}/json"
    )
    request = urllib.request.Request(  # noqa: S310
        url,
        headers={
            "Accept": "application/json",
            "User-Agent": "three-workflow-release-control/1.0",
        },
    )
    try:
        with urllib.request.urlopen(  # noqa: S310
            request, timeout=_PYPI_JSON_TIMEOUT_SECONDS
        ) as response:
            status = response.getcode()
            if status != 200:
                msg = (
                    f"PyPI JSON API request failed for package "
                    f"{normalized!r}: HTTP {status}"
                )
                raise RuntimeError(msg)
            raw = response.read()
    except urllib.error.HTTPError as exc:
        if exc.code == 404:
            return None
        msg = (
            f"PyPI JSON API request failed for package {normalized!r}: "
            f"HTTP {exc.code}"
        )
        raise RuntimeError(msg) from exc
    except urllib.error.URLError as exc:
        msg = (
            f"PyPI JSON API request failed for package {normalized!r}: "
            f"{exc.reason}"
        )
        raise RuntimeError(msg) from exc
    except http.client.HTTPException as exc:
        msg = f"PyPI JSON API request failed for package {normalized!r}: {exc}"
        raise RuntimeError(msg) from exc
    except OSError as exc:
        msg = f"PyPI JSON API request failed for package {normalized!r}: {exc}"
        raise RuntimeError(msg) from exc
    try:
        payload = json.loads(raw.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        msg = (
            f"PyPI JSON API returned invalid JSON for package {normalized!r}: "
            f"{exc}"
        )
        raise RuntimeError(msg) from exc
    if not isinstance(payload, dict):
        msg = f"PyPI JSON API returned non-object payload for {normalized!r}"
        raise TypeError(msg)
    return payload


def _supports_remote_observation(snapshot: Mapping[str, Any]) -> bool:
    return snapshot.get("family") == "github-release" or (
        _supports_pypi_remote_observation(snapshot)
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


def _pep503_name(name: str) -> str:
    return re.sub(r"[-_.]+", "-", name).lower()


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
    return json.loads(result.stdout)


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
