"""Command-line entry point for approved Workflow Delivery v3 work."""

# ruff: noqa: EM101, EM102, TRY003, TRY301

from __future__ import annotations

import argparse
import hashlib
import json
import subprocess
import sys
from pathlib import Path
from time import time
from typing import TYPE_CHECKING, cast
from urllib.error import URLError
from urllib.parse import urlsplit
from urllib.request import Request, urlopen

from three_workflow_delivery_v3.adapters.node import (
    BuildRequest,
    PackageTargetWitness,
    RuntimeRequest,
    build_node_package,
    run_node_project_build,
    run_node_project_tests,
)
from three_workflow_delivery_v3.canonical import (
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.catalogs import (
    catalog_digest,
    catalog_document,
)
from three_workflow_delivery_v3.ci.evidence import (
    form_ci_evidence,
    form_empty_lane_result,
    form_evidence_lane_result,
)
from three_workflow_delivery_v3.ci.finalizer import (
    derive_ci_supersession_state,
    finalize_ci_slice,
    render_ci_slice_summary,
)
from three_workflow_delivery_v3.ci.planner import (
    ROOT_HK_DEFINITION,
    _definition_digest,
    form_pull_request_candidate,
    form_slice_validation_candidate,
    plan_ci_qualification,
)
from three_workflow_delivery_v3.records.ci import (
    CI_LANE_IDS,
    CiArtifact,
    CiCandidate,
    CiLaneResult,
    CiQualificationSnapshot,
    _ci_candidate_from_document,
    admit_ci_lane_result_json,
    admit_ci_qualification_snapshot_json,
    ci_qualification_snapshot_digest,
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
from three_workflow_delivery_v3.repository.node_provider import (
    CheckoutEvidence,
    GlobalInput,
    NbgvFacts,
    ProjectNode,
    ProviderBinding,
    validate_nbgv_facts,
    validate_node_provider_result,
)

if TYPE_CHECKING:
    from collections.abc import Sequence

    from three_workflow_delivery_v3.canonical import JsonValue

_PROJECT_PATH = "src/public/lib/hcoona-release-smoke-npm"
_CI_REQUEST_SCHEMA = "workflow-delivery/v3/ci-request"
_CI_ADAPTER_CONTEXT_SCHEMA = "workflow-delivery/v3/ci-node-adapter-context"
_CI_ADAPTER_RESULT_SCHEMA = "workflow-delivery/v3/ci-node-adapter-result"
_SHA256_HEX_LENGTH = 64
_PAIR_FIELD_COUNT = 2
_LOWER_HEX = frozenset("0123456789abcdef")
_NODE_BUILD_INPUTS = (
    "README.md",
    "package.json",
    "scripts/build.mjs",
    "src/index.js",
)
_NODE_TEST_INPUTS = (
    "package.json",
    "src/index.js",
    "test/index.test.js",
)
_GITHUB_PUBLIC_API = "https://api.github.com"


class _GitHubPullRequestLookupError(RuntimeError):
    """One unavailable or malformed public GitHub PR lookup."""


def _write_document(document: dict[str, JsonValue]) -> None:
    sys.stdout.buffer.write(canonicalize(document) + b"\n")


def _write_output(path: str, document: dict[str, JsonValue]) -> None:
    destination = Path(path)
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_bytes(canonicalize(document))


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
    if arguments.output is None:
        _write_document(document)
    else:
        _write_output(arguments.output, document)
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


def _object(value: JsonValue, *, context: str) -> dict[str, JsonValue]:
    if not isinstance(value, dict):
        raise TypeError(f"{context} must be an object")
    return value


def _array(value: JsonValue, *, context: str) -> list[JsonValue]:
    if not isinstance(value, list):
        raise TypeError(f"{context} must be an array")
    return value


def _string(value: JsonValue, *, context: str) -> str:
    if type(value) is not str:
        raise TypeError(f"{context} must be a string")
    return value


def _integer(value: JsonValue, *, context: str) -> int:
    if type(value) is not int:
        raise TypeError(f"{context} must be an integer")
    return value


def _boolean(value: JsonValue, *, context: str) -> bool:
    if type(value) is not bool:
        raise TypeError(f"{context} must be a Boolean")
    return value


def _strings(value: JsonValue, *, context: str) -> tuple[str, ...]:
    return tuple(
        _string(item, context=f"{context}[{index}]")
        for index, item in enumerate(_array(value, context=context))
    )


def _string_pairs(
    value: JsonValue,
    *,
    context: str,
) -> tuple[tuple[str, str], ...]:
    pairs: list[tuple[str, str]] = []
    for index, item in enumerate(_array(value, context=context)):
        pair = _array(item, context=f"{context}[{index}]")
        if len(pair) != _PAIR_FIELD_COUNT:
            raise ValueError(f"{context}[{index}] must contain two strings")
        pairs.append(
            (
                _string(pair[0], context=f"{context}[{index}][0]"),
                _string(pair[1], context=f"{context}[{index}][1]"),
            )
        )
    return tuple(pairs)


def _nullable_string(value: JsonValue, *, context: str) -> str | None:
    return None if value is None else _string(value, context=context)


def _read_object(
    path: str, *, context: str
) -> tuple[bytes, dict[str, JsonValue]]:
    content = Path(path).read_bytes()
    return content, _object(parse_canonical_json(content), context=context)


def _normalized_digest(value: str) -> str:
    raw = value.removeprefix("sha256:")
    if len(raw) != _SHA256_HEX_LENGTH or any(c not in _LOWER_HEX for c in raw):
        raise ValueError("artifact digest must be lowercase SHA-256")
    return f"sha256:{raw}"


def _ci_candidate_command(arguments: argparse.Namespace) -> int:
    if arguments.event_kind == "pull_request":
        if None in (arguments.base_sha, arguments.head_sha, arguments.target):
            raise ValueError("pull_request requires base, head, and target")
        base_sha = cast("str", arguments.base_sha)
        head_sha = cast("str", arguments.head_sha)
        target = cast("str", arguments.target)
        candidate = form_pull_request_candidate(
            repository=arguments.repository,
            request_id=arguments.request_id,
            workflow_run_id=arguments.workflow_run_id,
            run_attempt=arguments.run_attempt,
            selected_ref=arguments.selected_ref,
            base_sha=base_sha,
            head_sha=head_sha,
            tested_merge_sha=target,
            comparison_identity=(base_sha, head_sha),
        )
        helper = (
            Path(arguments.repo_root) / "eng/scripts/workflow_delivery_v3_hk.py"
        )
        result = subprocess.run(  # noqa: S603
            (
                sys.executable,
                str(helper),
                "--repository",
                arguments.repo_root,
                "--from-ref",
                base_sha,
                "--to-ref",
                head_sha,
            ),
            check=True,
            capture_output=True,
            text=True,
        )
        changed_value = json.loads(result.stdout)
        if not isinstance(changed_value, list):
            raise TypeError("changed-path helper did not return a JSON array")
        changed_paths = tuple(
            _string(path, context="changed path")
            for path in cast("list[JsonValue]", changed_value)
        )
    else:
        if arguments.target is None:
            raise ValueError("slice-validation candidate requires target")
        candidate = form_slice_validation_candidate(
            repository=arguments.repository,
            request_id=arguments.request_id,
            workflow_run_id=arguments.workflow_run_id,
            run_attempt=arguments.run_attempt,
            selected_ref=arguments.selected_ref,
            target=arguments.target,
        )
        changed_paths = ()
    _write_output(
        arguments.output,
        {
            "schema": _CI_REQUEST_SCHEMA,
            "candidate": candidate.to_document(),
            "changed-paths": cast("list[JsonValue]", list(changed_paths)),
        },
    )
    return 0


def _ci_admit_payload_command(arguments: argparse.Namespace) -> int:
    content = Path(arguments.input).read_bytes()
    expected = _normalized_digest(arguments.expected_digest)
    actual = f"sha256:{hashlib.sha256(content).hexdigest()}"
    if actual != expected:
        raise ValueError(
            "downloaded payload digest does not match upload output"
        )
    parse_canonical_json(content)
    return 0


def _nbgv_from_document(value: JsonValue) -> NbgvFacts:
    document = _object(value, context="NBGV")
    canonical = _object(document["canonical"], context="NBGV canonical")
    native = _object(document["native"], context="NBGV native")
    return NbgvFacts(
        canonical_version=_string(canonical["version"], context="version"),
        sem_ver1=_string(canonical["semVer1"], context="NBGV semVer1"),
        sem_ver2=_string(canonical["semVer2"], context="NBGV semVer2"),
        version_height=_integer(canonical["versionHeight"], context="height"),
        git_commit_id=_string(canonical["gitCommitId"], context="commit"),
        public_release=_boolean(canonical["publicRelease"], context="public"),
        npm_package_version=_string(native["npmPackageVersion"], context="npm"),
        node_api_result_digest=_string(
            document["node-api-result-digest"], context="NBGV digest"
        ),
    )


def _project_from_document(value: JsonValue, *, context: str) -> ProjectNode:
    document = _object(value, context=context)
    return ProjectNode(
        project_id=_string(document["project-id"], context=f"{context}.id"),
        package_name=_string(document["package-name"], context="package"),
        path=_string(document["path"], context=f"{context}.path"),
        manifest_path=_string(document["manifest-path"], context="manifest"),
        private=_boolean(document["private"], context=f"{context}.private"),
        workspace_dependencies=_strings(
            document["workspace-dependencies"], context="workspace dependencies"
        ),
    )


def _load_node_provider_result(
    path: str,
    *,
    expected_binding: ProviderBinding,
    expected_manifest_digest: str,
) -> NodeProviderResult:
    _, wrapper = _read_object(path, context="Node Provider Result")
    document = dict(wrapper)
    manifest_digest = _string(
        document.pop("provider-request-manifest-digest"), context="manifest"
    )
    result_digest = _string(document.pop("result-digest"), context="digest")
    binding_document = _object(document["binding"], context="binding")
    binding = ProviderBinding(
        request_id=_string(
            binding_document["request-id"], context="request-id"
        ),
        purpose=_string(binding_document["purpose"], context="purpose"),
        workflow_run_id=_integer(
            binding_document["workflow-run-id"], context="run"
        ),
        run_attempt=_integer(
            binding_document["run-attempt"], context="attempt"
        ),
        target=_string(binding_document["target"], context="target"),
        producer=_string(binding_document["producer"], context="producer"),
        control=_string(binding_document["control"], context="control"),
        catalog_digest=_string(
            binding_document["catalog-digest"], context="catalog"
        ),
        request_digest=_string(
            binding_document["request-digest"], context="request"
        ),
    )
    provider = _object(document["provider"], context="provider")
    toolchain_document = _object(provider["toolchain"], context="toolchain")
    toolchain = tuple(
        sorted(
            (
                _string(name, context="toolchain name"),
                _string(version, context=f"toolchain.{name}"),
            )
            for name, version in toolchain_document.items()
        )
    )
    input_digests = _object(document["input-digests"], context="digests")
    checkout_document = _object(document["checkout"], context="checkout")
    checkout = CheckoutEvidence(
        target=_string(checkout_document["target"], context="checkout.target"),
        head=_string(checkout_document["head"], context="checkout.head"),
        shallow=_boolean(checkout_document["shallow"], context="shallow"),
        ancestry_complete=_boolean(
            checkout_document["ancestry-complete"], context="ancestry"
        ),
        tags_complete=_boolean(
            checkout_document["tags-complete"], context="tags"
        ),
        credentials_persisted=_boolean(
            checkout_document["credentials-persisted"], context="credentials"
        ),
        authoritative_remote=_string(
            checkout_document["authoritative-remote"], context="remote"
        ),
        authoritative_remote_url=_string(
            checkout_document["authoritative-remote-url"], context="remote URL"
        ),
        tag_refspec=_string(
            checkout_document["tag-refspec"], context="tag refspec"
        ),
    )
    projects = tuple(
        _project_from_document(item, context=f"project-nodes[{index}]")
        for index, item in enumerate(
            _array(document["project-nodes"], context="projects")
        )
    )
    global_inputs: list[GlobalInput] = []
    for index, item in enumerate(
        _array(document["global-inputs"], context="globals")
    ):
        item_document = _object(item, context=f"global[{index}]")
        global_inputs.append(
            GlobalInput(
                path=_string(item_document["path"], context="global path"),
                content_digest=_string(
                    item_document["content-digest"], context="global digest"
                ),
                project_ids=_strings(
                    item_document["project-ids"], context="global projects"
                ),
            )
        )
    result = NodeProviderResult(
        binding=binding,
        provider_logical_id=_string(
            provider["logical-id"], context="logical-id"
        ),
        provider_implementation_id=_string(
            provider["implementation-id"], context="implementation-id"
        ),
        execution_mode=_string(
            provider["execution-mode"], context="execution-mode"
        ),
        execution_class=_string(
            provider["execution-class"], context="execution-class"
        ),
        toolchain=toolchain,
        manifest_digest=_string(
            input_digests["manifest"], context="manifest digest"
        ),
        configuration_digest=_string(
            input_digests["configuration"], context="configuration digest"
        ),
        checkout=checkout,
        project_nodes=projects,
        global_inputs=tuple(global_inputs),
        build_capabilities=_strings(
            document["build-capabilities"], context="capabilities"
        ),
        nbgv=_nbgv_from_document(document["nbgv"]),
        unresolved=_strings(document["unresolved"], context="unresolved"),
        conflicts=_strings(document["conflicts"], context="conflicts"),
        outcome=_string(document["outcome"], context="outcome"),
        diagnostic_reference=_nullable_string(
            document["diagnostic-reference"], context="diagnostic"
        ),
    )
    validate_node_provider_result(result)
    if result.to_document() != document:
        raise ValueError("Node Provider Result is not normalized")
    if manifest_digest != expected_manifest_digest:
        raise ValueError("Node Provider Result manifest mismatch")
    if result.binding != expected_binding:
        raise ValueError("Node Provider Result binding mismatch")
    if result.result_digest != result_digest:
        raise ValueError("Node Provider Result digest mismatch")
    return result


def _load_ci_request(path: str) -> tuple[CiCandidate, tuple[str, ...]]:
    _, document = _read_object(path, context="CI request")
    if (
        document.keys() != {"schema", "candidate", "changed-paths"}
        or document["schema"] != _CI_REQUEST_SCHEMA
    ):
        raise ValueError("CI request has the wrong schema")
    return (
        _ci_candidate_from_document(
            document["candidate"], context="CI Candidate"
        ),
        _strings(document["changed-paths"], context="changed-paths"),
    )


def _command_stdout(command: tuple[str, ...], cwd: Path) -> str:
    return subprocess.run(  # noqa: S603
        command,
        cwd=cwd,
        check=True,
        capture_output=True,
        text=True,
    ).stdout.strip()


def _ci_plan_command(arguments: argparse.Namespace) -> int:
    repo_root = Path(arguments.repo_root).resolve()
    candidate, changed_paths = _load_ci_request(arguments.request)
    context = CompilationContext(
        request_id=candidate.request_id,
        purpose=candidate.purpose,
        workflow_run_id=candidate.workflow_run_id,
        run_attempt=candidate.run_attempt,
        target=candidate.target,
        producer="plan",
        control=f"workflow-delivery-v3:{candidate.workflow_sha}",
        catalog_digest=catalog_digest(),
    )
    manifest = first_slice_provider_manifest(
        context,
        provider_producer="discover-node",
    )
    result = _load_node_provider_result(
        arguments.provider_result,
        expected_binding=provider_binding(manifest, "node-first-slice"),
        expected_manifest_digest=manifest.manifest_digest,
    )
    request_digest = _normalized_digest(arguments.request_artifact_digest)
    provider_digest = _normalized_digest(arguments.provider_artifact_digest)
    bundle = create_node_provider_fact_bundle(
        result,
        manifest_digest=manifest.manifest_digest,
        manifest_entry_id=manifest.requests[0].entry_id,
        request_artifact_id=arguments.request_artifact_id,
        request_artifact_digest=request_digest,
        transport_id=arguments.provider_artifact_id,
        transport_digest=provider_digest,
    )
    admitted = admit_node_provider_fact_bundle(
        bundle,
        context=context,
        manifest=manifest,
        admission=FactBundleAdmissionContext(
            request_artifact_id=arguments.request_artifact_id,
            request_artifact_digest=request_digest,
            transport_id=arguments.provider_artifact_id,
            transport_digest=provider_digest,
            bundle_digest=bundle.bundle_digest,
        ),
    )
    repository_model = compile_repository_model(
        repo_root,
        context,
        manifest,
        [admitted],
    )
    if candidate.event_kind == "pull_request":
        plan = plan_ci_qualification(
            candidate,
            repository_model,
            repository_model_digest=repository_model.snapshot_digest,
            changed_paths=changed_paths,
            comparison_identity=(
                cast("str", candidate.base_sha),
                cast("str", candidate.head_sha),
            ),
        )
    else:
        if changed_paths:
            raise ValueError("slice-validation rejects changed paths")
        plan = plan_ci_qualification(
            candidate,
            repository_model,
            repository_model_digest=repository_model.snapshot_digest,
        )
    plan_digest = ci_qualification_snapshot_digest(plan)
    _write_output(arguments.output, plan.to_document())
    adapter_context_path = Path(arguments.adapter_context_output)
    if plan.ready:
        source_date_epoch = int(
            _command_stdout(
                ("git", "show", "-s", "--format=%ct", candidate.target),
                repo_root,
            )
        )
        toolchain = dict(result.toolchain)
        toolchain["npm"] = _command_stdout(("npm", "--version"), repo_root)
        adapter_context: dict[str, JsonValue] = {
            "schema": _CI_ADAPTER_CONTEXT_SCHEMA,
            "plan-digest": plan_digest,
            "repository-model-digest": repository_model.snapshot_digest,
            "project-id": repository_model.project_nodes[0].project_id,
            "project-path": repository_model.project_nodes[0].path,
            "release-unit": repository_model.release_units[0].release_unit,
            "build-id": repository_model.release_units[0].builds[0].build_id,
            "build-definition": (
                repository_model.release_units[0].builds[0].definition
            ),
            "catalog-digest": repository_model.context.catalog_digest,
            "control": repository_model.context.control,
            "source-date-epoch": source_date_epoch,
            "toolchain": cast("dict[str, JsonValue]", toolchain),
            "nbgv": repository_model.nbgv.to_document(),
        }
        _write_output(arguments.adapter_context_output, adapter_context)
    elif adapter_context_path.exists():
        adapter_context_path.unlink()
    if arguments.github_output is not None:
        with Path(arguments.github_output).open(
            "a", encoding="utf-8"
        ) as output:
            print(f"plan-digest={plan_digest}", file=output)
            print(f"plan-ready={str(plan.ready).lower()}", file=output)
            for obligation in plan.obligations:
                selected = str(obligation.selected).lower()
                print(f"{obligation.lane_id}-selected={selected}", file=output)
    return 0


def _load_ci_plan(path: str, expected_digest: str) -> CiQualificationSnapshot:
    content, document = _read_object(path, context="CI Plan")
    candidate = _ci_candidate_from_document(
        document["candidate"], context="CI Candidate"
    )
    return admit_ci_qualification_snapshot_json(
        content,
        expected_candidate=candidate,
        expected_repository_model_digest=_string(
            document["repository-model-digest"],
            context="CI Plan.repository-model-digest",
        ),
        expected_root_hk_definition=ROOT_HK_DEFINITION,
        expected_root_hk_definition_digest=_definition_digest(
            ROOT_HK_DEFINITION
        ),
        expected_plan_digest=expected_digest,
    )


def _load_adapter_context(
    path: str,
    *,
    plan: CiQualificationSnapshot,
) -> tuple[dict[str, JsonValue], NbgvFacts]:
    _, document = _read_object(path, context="CI Adapter context")
    if (
        document["schema"] != _CI_ADAPTER_CONTEXT_SCHEMA
        or document["plan-digest"] != ci_qualification_snapshot_digest(plan)
        or document["repository-model-digest"] != plan.repository_model_digest
        or document["project-id"] not in plan.selected_project_nodes
        or document["release-unit"] not in plan.selected_release_units
        or document["build-id"] not in plan.selected_variants
        or document["project-path"] != _PROJECT_PATH
        or document["control"]
        != f"workflow-delivery-v3:{plan.candidate.workflow_sha}"
    ):
        raise ValueError("CI Adapter context does not match the Plan")
    nbgv = _nbgv_from_document(document["nbgv"])
    validate_nbgv_facts(nbgv, target=plan.candidate.target)
    return document, nbgv


def _ci_node_adapter_command(arguments: argparse.Namespace) -> int:
    plan = _load_ci_plan(arguments.plan, arguments.plan_digest)
    obligation = next(
        (
            item
            for item in plan.obligations
            if item.lane_id == arguments.lane_id
        ),
        None,
    )
    if obligation is None or not obligation.selected:
        raise ValueError("Node Adapter lane is not selected")
    context, nbgv = _load_adapter_context(
        arguments.adapter_context,
        plan=plan,
    )
    toolchain = _object(context["toolchain"], context="toolchain")
    if toolchain.keys() != {"node", "pnpm", "npm"}:
        raise ValueError("CI Adapter toolchain is not closed")
    repository_root = Path(arguments.repository_root).resolve(strict=True)
    project_root = (repository_root / _PROJECT_PATH).resolve(strict=True)
    witness = PackageTargetWitness(
        target=plan.candidate.target,
        release_unit=_string(
            context["release-unit"],
            context="release-unit",
        ),
        nbgv=nbgv,
        build_definition=_string(
            context["build-definition"],
            context="build-definition",
        ),
        catalog_digest=_string(
            context["catalog-digest"],
            context="catalog-digest",
        ),
        control_digest=canonical_sha256(
            {"control": _string(context["control"], context="control")}
        ),
        purpose=plan.candidate.purpose,
    )
    runtime = RuntimeRequest(
        node_version=_string(toolchain["node"], context="toolchain.node"),
        npm_version=_string(toolchain["npm"], context="toolchain.npm"),
    )
    source_date_epoch = _integer(
        context["source-date-epoch"],
        context="source-date-epoch",
    )
    invocation_digest = canonical_sha256(
        {
            "schema": "workflow-delivery/v3/ci-node-adapter-invocation",
            "lane-id": arguments.lane_id,
            "plan-digest": arguments.plan_digest,
            "repository-model-digest": plan.repository_model_digest,
            "obligation-request-digest": obligation.request_digest,
            "project-id": context["project-id"],
            "project-path": context["project-path"],
            "release-unit": context["release-unit"],
            "build-id": context["build-id"],
            "build-definition": context["build-definition"],
            "source-date-epoch": source_date_epoch,
            "toolchain": toolchain,
            "nbgv": nbgv.to_document(),
        }
    )
    result: dict[str, JsonValue] = {
        "schema": _CI_ADAPTER_RESULT_SCHEMA,
        "lane-id": arguments.lane_id,
        "plan-digest": arguments.plan_digest,
        "repository-model-digest": plan.repository_model_digest,
        "outcome": "success",
        "output-digests": [invocation_digest],
        "artifact": None,
        "diagnostics": [],
    }
    try:
        if arguments.lane_id == "project-test":
            run_node_project_tests(project_root, runtime)
        else:
            request = BuildRequest(
                source_root=project_root,
                declared_inputs=_NODE_BUILD_INPUTS,
                npm_package_version=nbgv.npm_package_version,
                witness=witness,
                source_date_epoch=source_date_epoch,
                node_version=runtime.node_version.removeprefix("v"),
                pnpm_version=_string(
                    toolchain["pnpm"],
                    context="toolchain.pnpm",
                ),
                npm_version=runtime.npm_version,
            )
            if arguments.lane_id == "project-build":
                run_node_project_build(request)
                cast("list[JsonValue]", result["output-digests"]).append(
                    "sha256:"
                    + hashlib.sha256(witness.canonical_bytes).hexdigest()
                )
            elif arguments.lane_id == "npm-artifact-build":
                if arguments.tarball_output is None:
                    raise ValueError("npm Adapter requires --tarball-output")
                build = build_node_package(request)
                tarball = Path(arguments.tarball_output)
                tarball.parent.mkdir(parents=True, exist_ok=True)
                tarball.write_bytes(build.tarball)
                result["output-digests"] = [
                    invocation_digest,
                    *(digest for _, digest in build.source_input_manifest),
                ]
                result["artifact"] = {
                    "tarball-basename": build.manifest.basename,
                    "content-sha256": build.manifest.sha256,
                    "content-sha512": build.manifest.sha512,
                    "byte-size": build.manifest.byte_size,
                    "provenance-digest": (
                        "sha256:" + hashlib.sha256(build.witness).hexdigest()
                    ),
                    "entries": list(build.manifest.entries),
                    "lifecycle-scripts": [
                        [name, command]
                        for name, command in build.manifest.lifecycle_scripts
                    ],
                }
                result["diagnostics"] = [
                    f"built {build.manifest.basename}",
                ]
            else:
                raise ValueError("unsupported Node Adapter lane")
    except (subprocess.CalledProcessError, ValueError) as error:
        result["outcome"] = "failure"
        result["output-digests"] = [invocation_digest]
        result["artifact"] = None
        result["diagnostics"] = [
            f"{arguments.lane_id} Adapter failed: {type(error).__name__}"
        ]
        _write_output(arguments.output, result)
        return 1
    _write_output(arguments.output, result)
    return 0


def _mechanical_result(
    path: str,
    *,
    plan: CiQualificationSnapshot,
    lane_id: str,
) -> tuple[
    str,
    tuple[str, ...],
    dict[str, JsonValue] | None,
    tuple[str, ...],
]:
    _, document = _read_object(path, context="CI Adapter result")
    if document.keys() != {
        "schema",
        "lane-id",
        "plan-digest",
        "repository-model-digest",
        "outcome",
        "output-digests",
        "artifact",
        "diagnostics",
    }:
        raise ValueError("CI Adapter result schema is not closed")
    if (
        document["schema"] != _CI_ADAPTER_RESULT_SCHEMA
        or document["lane-id"] != lane_id
        or document["plan-digest"] != ci_qualification_snapshot_digest(plan)
        or document["repository-model-digest"] != plan.repository_model_digest
    ):
        raise ValueError("CI Adapter result does not match the Plan lane")
    artifact_value = document["artifact"]
    artifact = (
        None
        if artifact_value is None
        else _object(artifact_value, context="Adapter artifact")
    )
    if artifact is not None and artifact.keys() != {
        "tarball-basename",
        "content-sha256",
        "content-sha512",
        "byte-size",
        "provenance-digest",
        "entries",
        "lifecycle-scripts",
    }:
        raise ValueError("CI Adapter artifact content schema is not closed")
    return (
        _string(document["outcome"], context="Adapter outcome"),
        _strings(document["output-digests"], context="output-digests"),
        artifact,
        _strings(document["diagnostics"], context="diagnostics"),
    )


def _artifact_from_mechanics(
    arguments: argparse.Namespace,
    *,
    plan: CiQualificationSnapshot,
    producer: str,
    facts: dict[str, JsonValue],
) -> CiArtifact:
    if (
        arguments.artifact_id is None
        or arguments.artifact_name is None
        or arguments.artifact_url is None
        or arguments.artifact_digest is None
    ):
        raise ValueError(
            "successful npm lane requires uploaded artifact metadata"
        )
    if len(plan.selected_outputs) != 1:
        raise ValueError(
            "successful npm lane requires exactly one planned output"
        )
    output_id, logical_role, media_kind = plan.selected_outputs[0]
    return CiArtifact(
        candidate=plan.candidate,
        producer=producer,
        workflow_run_id=plan.workflow_run_id,
        run_attempt=plan.run_attempt,
        output_id=output_id,
        logical_role=logical_role,
        media_kind=media_kind,
        artifact_id=arguments.artifact_id,
        artifact_name=arguments.artifact_name,
        artifact_url=arguments.artifact_url,
        transport_digest=_normalized_digest(arguments.artifact_digest),
        tarball_basename=_string(
            facts["tarball-basename"],
            context="artifact.tarball-basename",
        ),
        content_sha256=_string(
            facts["content-sha256"],
            context="artifact.content-sha256",
        ),
        content_sha512=_string(
            facts["content-sha512"],
            context="artifact.content-sha512",
        ),
        byte_size=_integer(
            facts["byte-size"],
            context="artifact.byte-size",
        ),
        provenance_digest=_string(
            facts["provenance-digest"],
            context="artifact.provenance-digest",
        ),
        entries=_strings(facts["entries"], context="artifact.entries"),
        lifecycle_scripts=_string_pairs(
            facts["lifecycle-scripts"],
            context="artifact.lifecycle-scripts",
        ),
    )


def _ci_lane_result_command(  # noqa: C901, PLR0912
    arguments: argparse.Namespace,
) -> int:
    plan = _load_ci_plan(arguments.plan, arguments.plan_digest)
    obligation = next(
        (
            item
            for item in plan.obligations
            if item.lane_id == arguments.lane_id
        ),
        None,
    )
    if obligation is None:
        raise ValueError("lane result names an unknown lane")
    supplied_artifact_metadata = any(
        value is not None
        for value in (
            arguments.artifact_id,
            arguments.artifact_name,
            arguments.artifact_url,
            arguments.artifact_digest,
        )
    )
    if not obligation.selected:
        if (
            arguments.outcome is not None
            or arguments.mechanical_result is not None
            or supplied_artifact_metadata
        ):
            raise ValueError("unselected lane cannot supply Evidence")
        lane_result = form_empty_lane_result(
            plan,
            lane_id=arguments.lane_id,
        )
    else:
        if arguments.mechanical_result is not None:
            outcome, output_digests, artifact_facts, diagnostics = (
                _mechanical_result(
                    arguments.mechanical_result,
                    plan=plan,
                    lane_id=arguments.lane_id,
                )
            )
            if artifact_facts is None:
                if any(
                    value is not None
                    for value in (
                        arguments.artifact_id,
                        arguments.artifact_name,
                        arguments.artifact_url,
                        arguments.artifact_digest,
                    )
                ):
                    raise ValueError(
                        "platform artifact metadata requires Adapter facts"
                    )
                artifacts: tuple[CiArtifact, ...] = ()
            else:
                if arguments.lane_id != "npm-artifact-build":
                    raise ValueError(
                        "non-artifact lane emitted Adapter artifact facts"
                    )
                artifacts = (
                    _artifact_from_mechanics(
                        arguments,
                        plan=plan,
                        producer=arguments.lane_id,
                        facts=artifact_facts,
                    ),
                )
        elif arguments.outcome is not None:
            if arguments.lane_id != "root-hk":
                raise ValueError("--outcome is root-hk only")
            if supplied_artifact_metadata:
                raise ValueError("root-hk cannot supply artifact metadata")
            outcome = arguments.outcome
            output_digests = (
                canonical_sha256(
                    {
                        "schema": "workflow-delivery/v3/ci-lane-receipt",
                        "lane-id": arguments.lane_id,
                        "outcome": outcome,
                    }
                ),
            )
            artifacts = ()
            diagnostics = ("closed root-HK mechanical result",)
        else:
            raise ValueError("selected lane requires a mechanical result")
        evidence = form_ci_evidence(
            plan,
            obligation=obligation,
            producer=arguments.lane_id,
            workflow_run_id=plan.workflow_run_id,
            run_attempt=plan.run_attempt,
            runner="ubuntu-24.04",
            raw_outcome=outcome,
            output_digests=output_digests,
            artifacts=artifacts,
            diagnostics=diagnostics,
        )
        lane_result = form_evidence_lane_result(plan, evidence)
    _write_output(arguments.output, lane_result.to_document())
    return 0


def _load_lane_result(
    path: str,
    *,
    plan: CiQualificationSnapshot,
) -> CiLaneResult:
    content, document = _read_object(path, context="CI Lane Result")
    lane_id = _string(document["lane-id"], context="CI Lane Result.lane-id")
    return admit_ci_lane_result_json(
        content,
        expected_candidate=plan.candidate,
        expected_plan_digest=ci_qualification_snapshot_digest(plan),
        expected_lane_id=lane_id,
    )


def _validate_github_public_api_url(api_url: str) -> None:
    base = urlsplit(api_url)
    if (
        base.scheme != "https"
        or base.netloc != "api.github.com"
        or base.path not in {"", "/"}
        or base.query
        or base.fragment
    ):
        raise ValueError("GitHub public API URL is not exact")


def _fetch_current_pull_request(
    *,
    api_url: str,
    repository: str,
    pull_request_number: int,
) -> dict[str, JsonValue]:
    url = (
        f"{api_url.rstrip('/')}/repos/{repository}/pulls/{pull_request_number}"
    )
    request = Request(  # noqa: S310
        url,
        headers={
            "Accept": "application/vnd.github+json",
            "User-Agent": "three-workflow-delivery-v3",
            "X-GitHub-Api-Version": "2022-11-28",
        },
    )
    try:
        with urlopen(request, timeout=10) as response:  # noqa: S310
            payload = response.read()
        return _object(
            cast("JsonValue", json.loads(payload)),
            context="GitHub pull request",
        )
    except (OSError, URLError, json.JSONDecodeError, TypeError) as error:
        raise _GitHubPullRequestLookupError from error


def _ci_supersession_state(
    arguments: argparse.Namespace,
    *,
    plan: CiQualificationSnapshot,
) -> str:
    if plan.candidate.event_kind != "pull_request":
        if arguments.pull_request_number is not None:
            raise ValueError("manual finalization rejects a PR number")
        return "not-applicable"
    if arguments.pull_request_number is None:
        raise ValueError("pull-request finalization requires its PR number")
    if (
        type(arguments.pull_request_number) is not int
        or arguments.pull_request_number <= 0
        or plan.candidate.request_id != f"pr-{arguments.pull_request_number}"
    ):
        raise ValueError("pull-request number does not match the Plan")
    _validate_github_public_api_url(arguments.github_api_url)
    try:
        document = _fetch_current_pull_request(
            api_url=arguments.github_api_url,
            repository=plan.candidate.repository,
            pull_request_number=arguments.pull_request_number,
        )
        base = _object(document["base"], context="GitHub pull request.base")
        head = _object(document["head"], context="GitHub pull request.head")
        return derive_ci_supersession_state(
            plan,
            current_base_sha=_string(
                base["sha"],
                context="GitHub pull request.base.sha",
            ),
            current_head_sha=_string(
                head["sha"],
                context="GitHub pull request.head.sha",
            ),
            current_tested_merge_sha=_string(
                document["merge_commit_sha"],
                context="GitHub pull request.merge_commit_sha",
            ),
        )
    except (
        KeyError,
        TypeError,
        ValueError,
        _GitHubPullRequestLookupError,
    ):
        return "unsupported"


def _ci_finalize_command(arguments: argparse.Namespace) -> int:
    plan = _load_ci_plan(arguments.plan, arguments.plan_digest)
    lane_results = tuple(
        _load_lane_result(path, plan=plan) for path in arguments.lane_result
    )
    supersession_state = _ci_supersession_state(arguments, plan=plan)
    if type(arguments.started_at) is not int or arguments.started_at < 0:
        raise TypeError("--started-at must be an exact nonnegative integer")
    finished_at = _current_epoch_seconds()
    if finished_at < arguments.started_at:
        raise ValueError("--started-at cannot be later than finalization")
    decision = finalize_ci_slice(
        plan,
        lane_results,
        elapsed_seconds=finished_at - arguments.started_at,
        supersession_state=supersession_state,
    )
    _write_output(arguments.decision_output, decision.to_document())
    _write_output(arguments.summary_output, decision.summary.to_document())
    if arguments.github_step_summary is not None:
        summary = render_ci_slice_summary(decision)
        with Path(arguments.github_step_summary).open(
            "a",
            encoding="utf-8",
        ) as output:
            print("## Workflow Delivery v3 CI slice", file=output)
            print("", file=output)
            print(summary, file=output)
    return 0 if decision.terminal_result == "success" else 1


def _current_epoch_seconds() -> int:
    return int(time())


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


def _parser() -> argparse.ArgumentParser:  # noqa: PLR0915
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
    provide.add_argument("--output")
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

    ci = commands.add_parser("ci")
    ci_commands = ci.add_subparsers(dest="ci_command", required=True)

    candidate = ci_commands.add_parser("candidate")
    candidate.add_argument(
        "--event-kind",
        required=True,
        choices=("pull_request", "workflow_dispatch"),
    )
    candidate.add_argument("--repo-root", default=".")
    candidate.add_argument("--repository", required=True)
    candidate.add_argument("--request-id", required=True)
    candidate.add_argument("--workflow-run-id", required=True, type=int)
    candidate.add_argument("--run-attempt", required=True, type=int)
    candidate.add_argument("--selected-ref", required=True)
    candidate.add_argument("--base-sha")
    candidate.add_argument("--head-sha")
    candidate.add_argument("--target")
    candidate.add_argument("--output", required=True)
    candidate.set_defaults(handler=_ci_candidate_command)

    admit_payload = ci_commands.add_parser("admit-payload")
    admit_payload.add_argument("--input", required=True)
    admit_payload.add_argument("--expected-digest", required=True)
    admit_payload.set_defaults(handler=_ci_admit_payload_command)

    plan = ci_commands.add_parser("plan")
    plan.add_argument("--repo-root", default=".")
    plan.add_argument("--request", required=True)
    plan.add_argument("--provider-result", required=True)
    plan.add_argument("--request-artifact-id", required=True, type=int)
    plan.add_argument("--request-artifact-digest", required=True)
    plan.add_argument("--provider-artifact-id", required=True, type=int)
    plan.add_argument("--provider-artifact-digest", required=True)
    plan.add_argument("--output", required=True)
    plan.add_argument("--adapter-context-output", required=True)
    plan.add_argument("--github-output")
    plan.set_defaults(handler=_ci_plan_command)

    adapter = ci_commands.add_parser("node-adapter")
    adapter.add_argument(
        "--lane-id",
        required=True,
        choices=("project-build", "project-test", "npm-artifact-build"),
    )
    adapter.add_argument("--plan", required=True)
    adapter.add_argument("--plan-digest", required=True)
    adapter.add_argument("--adapter-context", required=True)
    adapter.add_argument("--repository-root", default=".")
    adapter.add_argument("--tarball-output")
    adapter.add_argument("--output", required=True)
    adapter.set_defaults(handler=_ci_node_adapter_command)

    lane_result = ci_commands.add_parser("lane-result")
    lane_result.add_argument("--plan", required=True)
    lane_result.add_argument("--plan-digest", required=True)
    lane_result.add_argument("--lane-id", required=True, choices=CI_LANE_IDS)
    lane_result.add_argument(
        "--outcome",
        choices=("success", "failure", "skipped", "timed-out", "unknown"),
    )
    lane_result.add_argument("--mechanical-result")
    lane_result.add_argument("--artifact-id", type=int)
    lane_result.add_argument("--artifact-name")
    lane_result.add_argument("--artifact-url")
    lane_result.add_argument("--artifact-digest")
    lane_result.add_argument("--output", required=True)
    lane_result.set_defaults(handler=_ci_lane_result_command)

    finalize = ci_commands.add_parser("finalize")
    finalize.add_argument("--plan", required=True)
    finalize.add_argument("--plan-digest", required=True)
    finalize.add_argument("--lane-result", action="append", default=[])
    finalize.add_argument("--started-at", required=True, type=int)
    finalize.add_argument("--pull-request-number", type=int)
    finalize.add_argument("--github-api-url", default=_GITHUB_PUBLIC_API)
    finalize.add_argument("--decision-output", required=True)
    finalize.add_argument("--summary-output", required=True)
    finalize.add_argument("--github-step-summary")
    finalize.set_defaults(handler=_ci_finalize_command)
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    """Execute one approved context-owned command."""
    parser = _parser()
    arguments = parser.parse_args(argv)
    try:
        handler = arguments.handler
        if arguments.context == "catalog":
            return handler()
        return handler(arguments)
    except (
        OSError,
        subprocess.CalledProcessError,
        TypeError,
        ValueError,
        json.JSONDecodeError,
    ) as error:
        sys.stderr.write(f"{error}\n")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
