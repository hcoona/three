"""Command-line entry point for approved Workflow Delivery v3 commit 3."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import TYPE_CHECKING

from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.catalogs import (
    catalog_digest,
    catalog_document,
)
from three_workflow_delivery_v3.release import (
    parse_governance_attestation,
)
from three_workflow_delivery_v3.repository import (
    CheckoutMaterialization,
    CompilationContext,
    FactBundleAdmissionContext,
    NodeProviderResult,
    ProviderRequestManifest,
    admit_node_provider_fact_bundle,
    compile_repository_model,
    create_node_provider_fact_bundle,
    first_slice_provider_manifest,
    load_first_slice_authoring,
    provide_node_repository_facts,
    provider_binding,
)

if TYPE_CHECKING:
    from collections.abc import Sequence

    from three_workflow_delivery_v3.canonical import JsonValue

_PROJECT_PATH = "src/public/lib/hcoona-release-smoke-npm"


def _write_document(document: dict[str, JsonValue]) -> None:
    sys.stdout.buffer.write(canonicalize(document) + b"\n")


def _catalog_command() -> int:
    document = catalog_document()
    document["catalog-digest"] = catalog_digest()
    _write_document(document)
    return 0


def _validate_authoring_command(arguments: argparse.Namespace) -> int:
    repo_root = Path(arguments.repo_root).resolve()
    descriptor, quality, policy = load_first_slice_authoring(
        repo_root,
        arguments.target,
    )
    build_definitions: list[JsonValue] = [
        build.definition for build in descriptor.builds
    ]
    quality_presets: dict[str, JsonValue] = dict(quality.ecosystems)
    governance: dict[str, JsonValue] = {
        "repository": policy.governance.repository,
        "ref": policy.governance.ref,
        "path": policy.governance.path,
        "max-age-days": policy.governance.max_age_days,
    }
    document: dict[str, JsonValue] = {
        "schema": "workflow-delivery/v3/authoring-validation",
        "target": arguments.target,
        "release-unit": descriptor.release_unit,
        "descriptor-path": _repo_relative(repo_root, descriptor.path),
        "build-definitions": build_definitions,
        "quality-presets": quality_presets,
        "release-policy-path": _repo_relative(repo_root, policy.path),
        "governance": governance,
        "catalog-digest": catalog_digest(),
        "result": "valid",
    }
    _write_document(document)
    return 0


def _repo_relative(repo_root: Path, path: str) -> str:
    candidate = Path(path)
    if not candidate.is_absolute():
        return candidate.as_posix()
    return candidate.relative_to(repo_root).as_posix()


def _compilation_context(
    arguments: argparse.Namespace,
) -> CompilationContext:
    simulation = arguments.purpose == "release-simulation"
    return CompilationContext(
        request_id=arguments.request_id,
        purpose=arguments.purpose,
        workflow_run_id=arguments.workflow_run_id,
        run_attempt=arguments.run_attempt,
        target=arguments.target,
        producer=arguments.compiler_producer,
        control=arguments.control,
        catalog_digest=catalog_digest(),
        channel=arguments.channel if simulation else None,
        release_unit=arguments.release_unit if simulation else None,
    )


def _provider_result(
    arguments: argparse.Namespace,
) -> tuple[
    Path,
    CompilationContext,
    ProviderRequestManifest,
    NodeProviderResult,
]:
    repo_root = Path(arguments.repo_root).resolve()
    context = _compilation_context(arguments)
    manifest = first_slice_provider_manifest(
        context,
        provider_producer=arguments.provider_producer,
    )
    result = provide_node_repository_facts(
        repo_root,
        arguments.project_path,
        provider_binding(manifest, "node-first-slice"),
        CheckoutMaterialization(
            fetch_depth=arguments.fetch_depth,
            credentials_persisted=not arguments.no_persist_credentials,
        ),
    )
    return repo_root, context, manifest, result


def _provide_node_command(arguments: argparse.Namespace) -> int:
    _, _, manifest, result = _provider_result(arguments)
    document = result.to_document()
    document["provider-request-manifest-digest"] = manifest.manifest_digest
    document["result-digest"] = result.result_digest
    _write_document(document)
    return 0


def _compile_command(arguments: argparse.Namespace) -> int:
    repo_root, context, manifest, result = _provider_result(arguments)
    bundle = create_node_provider_fact_bundle(
        result,
        manifest_digest=manifest.manifest_digest,
        manifest_entry_id=manifest.requests[0].entry_id,
        request_artifact_id=arguments.request_artifact_id,
        request_artifact_digest=arguments.request_artifact_digest,
        transport_id=arguments.transport_id,
        transport_digest=arguments.transport_digest,
    )
    admitted = admit_node_provider_fact_bundle(
        bundle,
        context=context,
        manifest=manifest,
        admission=FactBundleAdmissionContext(
            request_artifact_id=arguments.request_artifact_id,
            request_artifact_digest=arguments.request_artifact_digest,
            transport_id=arguments.transport_id,
            transport_digest=arguments.transport_digest,
            bundle_digest=bundle.bundle_digest,
        ),
    )
    snapshot = compile_repository_model(
        repo_root,
        context,
        manifest,
        [admitted],
    )
    document = snapshot.to_document()
    document["snapshot-digest"] = snapshot.snapshot_digest
    _write_document(document)
    return 0


def _validate_attestation_command(arguments: argparse.Namespace) -> int:
    attestation = parse_governance_attestation(
        Path(arguments.document).read_bytes()
    )
    document = attestation.to_document()
    document["content-digest"] = attestation.content_digest
    _write_document(document)
    return 0


def _add_provider_arguments(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--repo-root", default=".")
    parser.add_argument("--project-path", default=_PROJECT_PATH)
    parser.add_argument("--request-id", required=True)
    parser.add_argument(
        "--purpose",
        required=True,
        choices=(
            "ci-pr-slice-shadow",
            "slice-validation",
            "live-release",
            "release-simulation",
        ),
    )
    parser.add_argument("--workflow-run-id", required=True, type=int)
    parser.add_argument("--run-attempt", required=True, type=int)
    parser.add_argument("--target", required=True)
    parser.add_argument("--compiler-producer", required=True)
    parser.add_argument("--provider-producer", required=True)
    parser.add_argument("--control", required=True)
    parser.add_argument("--channel", choices=("buddy", "official"))
    parser.add_argument("--release-unit")
    parser.add_argument("--fetch-depth", required=True, type=int)
    parser.add_argument(
        "--no-persist-credentials",
        action="store_true",
        required=True,
    )


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="context", required=True)

    catalog = commands.add_parser("catalog")
    catalog.set_defaults(handler=_catalog_command)

    repository = commands.add_parser("repository")
    repository_commands = repository.add_subparsers(
        dest="repository_command",
        required=True,
    )
    validate = repository_commands.add_parser("validate-authoring")
    validate.add_argument("--repo-root", default=".")
    validate.add_argument("--target", required=True)
    validate.set_defaults(handler=_validate_authoring_command)

    provide = repository_commands.add_parser("provide-node")
    _add_provider_arguments(provide)
    provide.set_defaults(handler=_provide_node_command)

    compile_parser = repository_commands.add_parser("compile")
    _add_provider_arguments(compile_parser)
    compile_parser.add_argument(
        "--request-artifact-id", required=True, type=int
    )
    compile_parser.add_argument("--request-artifact-digest", required=True)
    compile_parser.add_argument("--transport-id", required=True, type=int)
    compile_parser.add_argument("--transport-digest", required=True)
    compile_parser.set_defaults(handler=_compile_command)

    release = commands.add_parser("release")
    release_commands = release.add_subparsers(
        dest="release_command",
        required=True,
    )
    attestation = release_commands.add_parser("validate-attestation")
    attestation.add_argument("--document", required=True)
    attestation.set_defaults(handler=_validate_attestation_command)
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    """Execute one approved commit-3 context-owned command."""
    parser = _parser()
    arguments = parser.parse_args(argv)
    try:
        handler = arguments.handler
        if arguments.context == "catalog":
            return handler()
        return handler(arguments)
    except (OSError, TypeError, ValueError, json.JSONDecodeError) as error:
        sys.stderr.write(f"{error}\n")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
