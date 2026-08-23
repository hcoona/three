"""Command-line entry point for approved Workflow Delivery v3 work."""

# ruff: noqa: EM101, EM102, I001, TRY003, TRY301

from __future__ import annotations

import argparse
import base64
import hashlib
import http.client
import http.server
import io
import json
import os
import subprocess
import sys
import tarfile
import tempfile
import threading
from datetime import UTC, datetime
from pathlib import Path
from time import monotonic, time
from typing import TYPE_CHECKING, Protocol, Self, cast
from urllib.error import URLError
from urllib.parse import urlsplit
from urllib.request import Request, urlopen

from three_workflow_delivery_v3.adapters.github_packages import (
    ACCEPTANCE_COORDINATES,
    ACCEPTANCE_PACKAGE_COORDINATE,
    ACCEPTANCE_PACKAGE_NAME,
    ACCEPTANCE_REPOSITORY_URL,
    DeferredPublicationExecutionResult,
    GitHubPackagesHttpResponse,
    GitHubPackagesHttpTransport,
    GitHubPackagesPublishPreflight,
    MutationMayHaveStartedMarker,
    PublisherGovernanceRecheckRejectionError,
    PublishRunner,
    ValidatedAcceptanceRequestProof,
    form_mutation_may_have_started_marker,
    inspect_fixed_acceptance_tarball,
    observe_github_packages_projection,
    preflight_github_packages_action,
    publish_github_packages_action,
    run_fixed_acceptance_suite,
)
from three_workflow_delivery_v3.adapters.node import (
    BuildRequest,
    PackageTargetWitness,
    RuntimeRequest,
    build_node_package,
    run_node_project_build,
    run_node_project_tests,
)
from three_workflow_delivery_v3.records.governance import (
    admit_governance_acceptance_evidence,
)
from three_workflow_delivery_v3.adapters.npmjs import observe_npmjs_projection
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
    CiBootstrapProjectionRequest,
    derive_ci_supersession_state,
    finalize_ci_slice,
    qualifies_precoexistence_bootstrap_projection,
    render_ci_slice_summary,
)
from three_workflow_delivery_v3.governance.inspection import (
    inspect_acceptance_reviewer,
)
from three_workflow_delivery_v3.ci.planner import (
    ROOT_HK_DEFINITION,
    _definition_digest,
    form_pull_request_candidate,
    form_slice_validation_candidate,
    plan_ci_qualification,
)
from three_workflow_delivery_v3.records.artifacts import (
    ArtifactTransportIdentity,
)
from three_workflow_delivery_v3.records.ci import (
    CI_LANE_IDS,
    CI_WORKFLOW_PATH,
    CiArtifact,
    CiCandidate,
    CiLaneResult,
    CiQualificationSnapshot,
    _ci_candidate_from_document,
    _ci_slice_decision_from_document,
    admit_ci_lane_result_json,
    admit_ci_qualification_snapshot_json,
    admit_ci_slice_decision_json,
    ci_qualification_snapshot_digest,
)
from three_workflow_delivery_v3.records.release import (
    HYPOTHETICAL_ACTIONS_REPORT_PRODUCER,
    NPMJS_OBSERVER_PRODUCER,
    PUBLISHER_GOVERNANCE_RECHECK_FAILED_BEFORE_RUNNER,
    ActionResult,
    AttemptOutcome,
    AuthorizationRecord,
    BuddyExecutionIdentity,
    CapabilityAdmissionDecision,
    CapabilityGroupResultBundle,
    ExecutionHistoryAdmissionSnapshot,
    HypotheticalAction,
    ProjectionObservation,
    PublicationSnapshot,
    QualificationDecision,
    QualificationEvidence,
    QualificationSnapshot,
    Receipt,
    ReceiptTransportReference,
    ReleaseArtifact,
    ReleaseAttemptBinding,
    ReleaseAttemptIdentity,
    ReleaseIntent,
    SimulationBinding,
    SimulationOutcome,
    admit_release_record,
    release_artifact_transport_name,
)
from three_workflow_delivery_v3.records.release_transport import (
    ReleaseAdmissionBindings,
)
from three_workflow_delivery_v3.release import (
    AdmittedLiveEligibilityDecision,
    LiveEligibilityAdmissionMode,
    admit_live_eligibility_decision,
    admit_live_capability,
    bind_reviewer_artifact,
    derive_buddy_execution_identity,
    derive_release_attempt_binding,
    derive_simulation_binding,
    discover_execution_history,
    execute_project_test,
    execute_release_build,
    finalize_attempt_outcome,
    finalize_qualification,
    finalize_simulation,
    form_authorization_record,
    form_incomplete_evidence,
    form_uploaded_release_artifact,
    governance_observation_provenance,
    materialize_hypothetical_actions,
    materialize_publication_snapshot,
    materialize_reviewer_artifact,
    materialize_reviewer_payload,
    normalize_buddy_live_intent,
    normalize_official_simulation_intent,
    parse_governance_attestation,
    plan_live_qualification,
    plan_official_simulation_qualification,
    observe_governance_source,
    ReviewerArtifact,
    ReviewerPayload,
    qualify_release_artifact_contents,
    qualify_release_install_import,
    require_fresh_governance_identity,
)
from three_workflow_delivery_v3.platform.github import GitHubRestClient
from three_workflow_delivery_v3.release.consumer_policy import (
    CONSUMER_POLICY_ID,
    ConsumerPolicyResult,
    SurfaceDigest,
    validate_consumer_policy_result,
)
from three_workflow_delivery_v3.release.eligibility import (
    LiveEligibilityContext,
    evaluate_live_eligibility,
    release_policy_digest,
)
from three_workflow_delivery_v3.release.simulation import (
    HypotheticalActionsReport,
    ReleaseAdapterContext,
    SimulationObservationSet,
    hypothetical_actions_report_from_bytes,
    release_adapter_context_from_bytes,
    render_simulation_summary,
    simulation_observation_set_from_bytes,
)
from three_workflow_delivery_v3.release.workflow import (
    artifact_expectation,
    form_release_adapter_context,
    mechanical_build_document,
    mechanical_build_from_bytes,
    node_build_request,
    runtime_request,
)
from three_workflow_delivery_v3.repository import (
    AdmittedRepositoryModelSnapshot,
    CheckoutMaterialization,
    CompilationContext,
    FactBundleAdmissionContext,
    NodeProviderResult,
    ProviderRequestManifest,
    admit_node_provider_fact_bundle,
    admit_repository_model_snapshot,
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
    from three_workflow_delivery_v3.repository.descriptors import ReleasePolicy

_PROJECT_PATH = "src/public/lib/hcoona-release-smoke-npm"
_CI_REQUEST_SCHEMA = "workflow-delivery/v3/ci-request"
_CI_ADAPTER_CONTEXT_SCHEMA = "workflow-delivery/v3/ci-node-adapter-context"
_CI_ADAPTER_RESULT_SCHEMA = "workflow-delivery/v3/ci-node-adapter-result"
_SHA256_HEX_LENGTH = 64
_PAIR_FIELD_COUNT = 2
_ACCEPTANCE_LOOPBACK_DUMMY_TOKEN = "wdv3-loopback-dummy-token"  # noqa: S105
_SYSTEM_POPEN = subprocess.Popen
_TARGET_SHA_LENGTH = 40
_ACCEPTANCE_VERSIONS_PER_PAGE = 100
_LOWER_HEX = frozenset("0123456789abcdef")


class _AcceptanceSuiteAction(argparse.Action):
    def __call__(
        self,
        parser: argparse.ArgumentParser,
        namespace: argparse.Namespace,
        values: str | Sequence[str] | None,
        option_string: str | None = None,
    ) -> None:
        del parser, option_string
        if type(values) is not str:
            raise TypeError("acceptance suite must be an exact string")
        setattr(namespace, self.dest, values)
        if getattr(namespace, "timeout_seconds", None) is None:
            timeout = 120.0 if values == "absent-create-readback" else 300.0
            namespace.timeout_seconds = timeout


class _AcceptanceProbeArguments(Protocol):
    package_coordinate: str
    suite: str
    target_sha: str
    timeout_seconds: float
    max_response_bytes: int
    max_output_bytes: int
    output: str
    github_output: str | None


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
LIVE_OUTCOME_EXIT_STATUS = {
    "success": 0,
    "failure": 1,
    "incomplete": 1,
    "replayable-no-side-effect": 1,
    "incomplete-possibly-mutated": 1,
    "unknown-replayable-approval-contract": 1,
}


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


def _governance_inspect_acceptance_reviewer_command(
    arguments: argparse.Namespace,
) -> int:
    inspection = inspect_acceptance_reviewer(
        repository=arguments.repository,
        workflow_run_id=arguments.workflow_run_id,
        environment=arguments.environment,
        deployment=arguments.deployment,
        job=arguments.job,
        artifact_id=arguments.artifact_id,
        timeout_seconds=arguments.timeout_seconds,
        max_output_bytes=arguments.max_output_bytes,
    )
    document = inspection.to_document()
    if arguments.output:
        _write_output(arguments.output, document)
    else:
        _write_document(document)
    return 0


def _governance_admit_acceptance_evidence_command(
    arguments: argparse.Namespace,
) -> int:
    evidence = admit_governance_acceptance_evidence(
        Path(arguments.document).read_bytes()
    )
    _write_document(evidence.to_document())
    return 0


def _governance_run_fixed_acceptance_probe_command(
    arguments: _AcceptanceProbeArguments,
) -> int:
    if arguments.package_coordinate != ACCEPTANCE_PACKAGE_COORDINATE:
        raise ValueError("acceptance package coordinate is not fixed")
    token = os.environ.pop("WDV3_ACCEPTANCE_GITHUB_TOKEN", None)
    if not token:
        raise ValueError(
            "WDV3_ACCEPTANCE_GITHUB_TOKEN must contain the acceptance token"
        )
    with tempfile.TemporaryDirectory(prefix="wdv3-acceptance-") as temporary:
        root = Path(temporary)
        npm_config = root / ".npmrc"
        npm_config.write_text(
            (
                "@hcoona:registry=https://npm.pkg.github.com\n"
                "ignore-scripts=true\n"
            ),
            encoding="utf-8",
        )
        npm_config.chmod(0o600)
        suite_inventory = (
            ("absent-create-readback",)
            if arguments.suite == "absent-create-readback"
            else ("exact", "identical-race", "differing-race", "lost-response")
        )
        tarballs: dict[str, Path] = {}
        contenders: dict[str, Path] = {}
        for scenario in suite_inventory:
            version = ACCEPTANCE_COORDINATES[scenario].rsplit("@", 1)[1]
            tarballs[scenario] = _build_acceptance_tarball(
                root,
                scenario=scenario,
                version=version,
                target_sha=arguments.target_sha,
                timeout_seconds=arguments.timeout_seconds,
            )
            if scenario == "differing-race":
                contenders[scenario] = _build_acceptance_tarball(
                    root,
                    scenario=f"{scenario}-contender",
                    version=version,
                    target_sha=arguments.target_sha,
                    timeout_seconds=arguments.timeout_seconds,
                )
        result = run_fixed_acceptance_suite(
            suite=arguments.suite,
            tarballs=tarballs,
            transport=_AcceptanceNpmTransport(
                npm_config,
                token=token,
                target_sha=arguments.target_sha,
            ),
            runner=_AcceptanceNpmRunner(
                npm_config,
                contender_tarballs=contenders,
                token=token,
            ),
            timeout_seconds=arguments.timeout_seconds,
            max_response_bytes=arguments.max_response_bytes,
            max_output_bytes=arguments.max_output_bytes,
            deadline=monotonic() + arguments.timeout_seconds,
        )
        token = ""
    document = result.to_document()
    _write_output(arguments.output, document)
    _append_outputs(
        arguments.github_output,
        (
            ("result", result.result),
            ("scenario-inventory", json.dumps(result.scenario_inventory)),
            ("record-digest", document["record-digest"]),
            ("mutation-classification", result.mutation_classification),
            (
                "record-json",
                json.dumps(document, separators=(",", ":"), sort_keys=True),
            ),
        ),
    )
    return 0


def _acceptance_subprocess_environment(
    *,
    npm_config: Path | None = None,
) -> dict[str, str]:
    environment = {
        name: os.environ[name]
        for name in ("HOME", "LANG", "LC_ALL", "PATH", "TMPDIR")
        if name in os.environ
    }
    if npm_config is not None:
        environment["NPM_CONFIG_USERCONFIG"] = str(npm_config)
    return environment


def _build_acceptance_tarball(
    root: Path,
    *,
    scenario: str,
    version: str,
    target_sha: str,
    timeout_seconds: float,
) -> Path:
    package_root = root / f"package-{scenario}"
    package_root.mkdir()
    package_document = {
        "name": ACCEPTANCE_PACKAGE_NAME,
        "version": version,
        "private": False,
        "files": ["index.js", "workflow-delivery/acceptance.json"],
        "repository": {
            "type": "git",
            "url": ACCEPTANCE_REPOSITORY_URL,
        },
    }
    (package_root / "package.json").write_bytes(
        canonicalize(cast("JsonValue", package_document))
    )
    payload_scenario = (
        "absent-create-readback" if scenario == "exact" else scenario
    )
    (package_root / "index.js").write_text(
        f"export const workflowDeliveryAcceptance = {payload_scenario!r};\n",
        encoding="utf-8",
    )
    witness = package_root / "workflow-delivery/acceptance.json"
    witness.parent.mkdir()
    witness.write_bytes(
        canonicalize(
            {
                "purpose": "destination-acceptance",
                "target-sha": target_sha,
            }
        )
    )
    pack_root = root / f"packed-{scenario}"
    pack_root.mkdir()
    pack = subprocess.run(  # noqa: S603
        (
            "npm",
            "pack",
            "--ignore-scripts",
            "--pack-destination",
            str(pack_root),
        ),
        cwd=package_root,
        check=True,
        capture_output=True,
        text=True,
        timeout=timeout_seconds,
        env=_acceptance_subprocess_environment(),
    )
    return pack_root / pack.stdout.strip().splitlines()[-1]


def _append_outputs(
    path: str | None,
    values: tuple[tuple[str, object], ...],
) -> None:
    if path is None:
        return
    with Path(path).open("a", encoding="utf-8") as output:
        for name, value in values:
            print(f"{name}={value}", file=output)


def _record_outputs(
    path: str | None,
    *,
    role: str,
    digest: str,
    extra: tuple[tuple[str, object], ...] = (),
) -> None:
    _append_outputs(
        path,
        (
            (f"{role}-digest", digest),
            (f"{role}-digest-hex", digest.removeprefix("sha256:")),
            *extra,
        ),
    )


class _SubprocessPublishRunner(PublishRunner):
    """Exact process seam for the one permitted npm publish command."""

    def run(
        self,
        argv: tuple[str, ...],
        *,
        env: dict[str, str],
    ) -> dict[str, object]:
        completed = subprocess.run(  # noqa: S603
            argv,
            check=False,
            capture_output=True,
            text=True,
            env={**os.environ, **env},
        )
        return {
            "exit_code": completed.returncode,
            "stdout": completed.stdout,
            "stderr": completed.stderr,
        }


def _remaining_seconds(deadline: float) -> float:
    remaining = round(deadline - monotonic(), 3)
    if remaining <= 0:
        raise TimeoutError("acceptance operation deadline expired")
    return remaining


def _acceptance_publish_body(
    tarball: bytes,
    *,
    version: str,
    tag: str,
) -> bytes:
    attachment_name = f"hcoona-release-smoke-npm-{version}.tgz"
    return canonicalize(
        {
            "_id": ACCEPTANCE_PACKAGE_NAME,
            "name": ACCEPTANCE_PACKAGE_NAME,
            "dist-tags": {tag: version},
            "versions": {
                version: {
                    "name": ACCEPTANCE_PACKAGE_NAME,
                    "version": version,
                    "dist": {
                        "integrity": (
                            "sha512-"
                            + base64.b64encode(
                                hashlib.sha512(tarball).digest()
                            ).decode("ascii")
                        ),
                        "shasum": hashlib.sha1(tarball).hexdigest(),  # noqa: S324
                    },
                }
            },
            "_attachments": {
                attachment_name: {
                    "content_type": "application/octet-stream",
                    "data": base64.b64encode(tarball).decode("ascii"),
                    "length": len(tarball),
                }
            },
        }
    )


def _validate_acceptance_publish_body(  # noqa: C901, PLR0912
    body: bytes,
    *,
    expected_version: str,
    expected_tag: str,
    expected_tarball: bytes | None,
    expected_target_sha: str | None,
) -> bytes:
    try:
        document = json.loads(body)
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise ValueError("publish body is not JSON") from error
    if type(document) is not dict:
        raise ValueError("publish body must be an object")
    if (
        document.get("_id") != ACCEPTANCE_PACKAGE_NAME
        or document.get("name") != ACCEPTANCE_PACKAGE_NAME
        or document.get("dist-tags") != {expected_tag: expected_version}
    ):
        raise ValueError("publish identity or dist-tag is not exact")
    versions = document.get("versions")
    if type(versions) is not dict or set(versions) != {expected_version}:
        raise ValueError("publish versions closure is not exact")
    version_document = versions[expected_version]
    if type(version_document) is not dict or (
        version_document.get("name") != ACCEPTANCE_PACKAGE_NAME
        or version_document.get("version") != expected_version
    ):
        raise ValueError("publish version identity is not exact")
    attachments = document.get("_attachments")
    expected_attachment_names = {
        f"hcoona-release-smoke-npm-{expected_version}.tgz",
        f"hcoona-hcoona-release-smoke-npm-{expected_version}.tgz",
        (f"@hcoona/hcoona-release-smoke-npm-{expected_version}.tgz"),
    }
    if (
        type(attachments) is not dict
        or len(attachments) != 1
        or not set(attachments).issubset(expected_attachment_names)
    ):
        raise ValueError("publish attachment closure is not exact")
    expected_attachment = next(iter(attachments))
    attachment = attachments[expected_attachment]
    if (
        type(attachment) is not dict
        or attachment.get("content_type") != "application/octet-stream"
        or type(attachment.get("data")) is not str
        or type(attachment.get("length")) is not int
    ):
        raise ValueError("publish attachment is malformed")
    try:
        tarball = base64.b64decode(attachment["data"], validate=True)
    except (ValueError, TypeError) as error:
        raise ValueError("publish attachment is not valid base64") from error
    if attachment["length"] != len(tarball):
        raise ValueError("publish attachment length is not exact")
    if expected_tarball is not None and tarball != expected_tarball:
        raise ValueError("publish attachment bytes are not expected")
    dist = version_document.get("dist")
    expected_integrity = "sha512-" + base64.b64encode(
        hashlib.sha512(tarball).digest()
    ).decode("ascii")
    if type(dist) is not dict or (
        dist.get("integrity") != expected_integrity
        or dist.get("shasum") != hashlib.sha1(tarball).hexdigest()  # noqa: S324
    ):
        raise ValueError("publish tarball hashes are not exact")
    try:
        with tarfile.open(fileobj=io.BytesIO(tarball), mode="r:gz") as archive:
            witness_file = archive.extractfile(
                "package/workflow-delivery/acceptance.json"
            )
            if witness_file is None:
                raise ValueError("acceptance witness is missing")
            witness_bytes = witness_file.read()
    except (tarfile.TarError, OSError, KeyError) as error:
        raise ValueError("acceptance tarball is malformed") from error
    try:
        witness = json.loads(witness_bytes)
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise ValueError("acceptance witness is malformed") from error
    target_sha = witness.get("target-sha") if type(witness) is dict else None
    if (
        type(witness) is not dict
        or set(witness) != {"purpose", "target-sha"}
        or witness.get("purpose") != "destination-acceptance"
        or type(target_sha) is not str
        or len(target_sha) != _TARGET_SHA_LENGTH
        or any(character not in "0123456789abcdef" for character in target_sha)
        or (
            expected_target_sha is not None
            and target_sha != expected_target_sha
        )
    ):
        raise ValueError("acceptance witness is not exact")
    return tarball


class AcceptanceMutationProxy:
    """Bounded loopback mutation boundary with strict request qualification."""

    _MAX_REQUEST_BYTES = 30_000_000
    _MAX_RESPONSE_BYTES = 1_000_000

    def __init__(  # noqa: C901, PLR0913
        self,
        *,
        timeout_seconds: float,
        token: str,
        incoming_dummy_token: str | None = None,
        expected_method: str,
        expected_path: str,
        expected_version: str = "0.0.0-wdv3-acceptance.4",
        expected_tag: str = "wdv3-acceptance-4",
        expected_tarballs: tuple[bytes, ...] = (),
        expected_target_sha: str | None = None,
        expected_requests: int = 1,
        drop_accepted_response: bool = True,
        deadline: float | None = None,
    ) -> None:
        """Create one fixed-host bounded loopback proxy."""
        self.observed = threading.Event()
        self.processed = threading.Event()
        self.proof: ValidatedAcceptanceRequestProof | None = None
        self.validation_error: str | None = None
        self.request_facts: list[dict[str, object]] = []
        self._legacy_timeout_seconds = timeout_seconds
        self._shared_deadline = deadline is not None
        self._deadline = (
            monotonic() + timeout_seconds if deadline is None else deadline
        )
        owner = self
        max_request_bytes = self._MAX_REQUEST_BYTES
        max_response_bytes = self._MAX_RESPONSE_BYTES
        if not token:
            raise ValueError("lost-response proxy token must be nonempty")
        if incoming_dummy_token == "":
            raise ValueError("loopback dummy token must be nonempty")
        if expected_method != "PUT":
            raise ValueError("lost-response proxy method must be PUT")
        if not expected_path.startswith("/@hcoona%2f") or "?" in expected_path:
            raise ValueError("lost-response proxy path is not fixed")
        if expected_requests not in {1, 2}:
            raise ValueError("acceptance proxy request count is unsupported")
        barrier = (
            threading.Barrier(expected_requests)
            if expected_requests == _PAIR_FIELD_COUNT
            else None
        )

        class Handler(http.server.BaseHTTPRequestHandler):
            protocol_version = "HTTP/1.1"

            def log_message(
                self,
                format: str,  # noqa: A002
                *args: object,
            ) -> None:
                del format, args

            def do_POST(self) -> None:
                self._forward()

            def do_PUT(self) -> None:
                self._forward()

            def do_GET(self) -> None:
                body = b'{"error":"not_found"}'
                self.send_response(http.HTTPStatus.NOT_FOUND)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(body)))
                self.send_header("Connection", "close")
                self.end_headers()
                self.wfile.write(body)
                self.close_connection = True

            def _reject(self, status: int) -> None:
                self.send_response(status)
                self.send_header("Content-Length", "0")
                self.end_headers()

            def _forward(  # noqa: C901, PLR0911, PLR0912, PLR0915
                self,
            ) -> None:
                if (
                    self.command != expected_method
                    or self.path != expected_path
                    or self.headers.get("Transfer-Encoding") is not None
                ):
                    self._reject(400)
                    return
                if (
                    incoming_dummy_token is not None
                    and self.headers.get("Authorization")
                    != f"Bearer {incoming_dummy_token}"
                ):
                    self._reject(401)
                    return
                length_text = self.headers.get("Content-Length")
                if length_text is None:
                    self._reject(411)
                    return
                try:
                    length = int(length_text)
                except ValueError:
                    self._reject(400)
                    return
                if length <= 0 or length > max_request_bytes:
                    self._reject(413)
                    return
                body = self.rfile.read(length)
                if len(body) != length:
                    self.close_connection = True
                    return
                content_type = self.headers.get("Content-Type", "")
                if content_type.lower().split(";", 1)[0].strip() != (
                    "application/json"
                ):
                    self._reject(415)
                    return
                try:
                    tarball = _validate_acceptance_publish_body(
                        body,
                        expected_version=expected_version,
                        expected_tag=expected_tag,
                        expected_tarball=None,
                        expected_target_sha=expected_target_sha,
                    )
                except ValueError as error:
                    owner.validation_error = str(error)
                    self._reject(422)
                    return
                if expected_tarballs and (
                    tarball not in expected_tarballs
                    or sum(
                        fact["tarball-sha512"]
                        == "sha512:" + hashlib.sha512(tarball).hexdigest()
                        for fact in owner.request_facts
                    )
                    >= expected_tarballs.count(tarball)
                ):
                    self._reject(409)
                    return
                request_digest = "sha256:" + hashlib.sha256(body).hexdigest()
                request_fact: dict[str, object] = {
                    "request-digest": request_digest,
                    "tarball-sha512": (
                        "sha512:" + hashlib.sha512(tarball).hexdigest()
                    ),
                }
                owner.request_facts.append(request_fact)
                owner.observed.set()
                if barrier is not None:
                    try:
                        barrier.wait(
                            timeout=owner._proxy_timeout()  # noqa: SLF001
                        )
                    except threading.BrokenBarrierError:
                        self._reject(504)
                        return
                headers = {
                    name: value
                    for name, value in self.headers.items()
                    if name.lower()
                    not in {
                        "authorization",
                        "connection",
                        "content-length",
                        "host",
                        "proxy-authorization",
                        "transfer-encoding",
                    }
                }
                headers["Authorization"] = "Bearer " + token
                headers["Content-Length"] = str(len(body))
                connection = http.client.HTTPSConnection(
                    "npm.pkg.github.com",
                    timeout=owner._proxy_timeout(),  # noqa: SLF001
                )
                try:
                    connection.request(
                        expected_method,
                        expected_path,
                        body=body,
                        headers=headers,
                    )
                    response = connection.getresponse()
                    response_body = response.read(max_response_bytes + 1)
                    if len(response_body) > max_response_bytes:
                        self._reject(502)
                        return
                    response_headers: list[tuple[str, str]] = []
                    for original_name, original_value in response.getheaders():
                        sanitized_name = original_name.replace("\n", "")
                        sanitized_value = original_value.replace("\n", "")
                        if (
                            sanitized_name != original_name
                            or sanitized_value != original_value
                            or "\r" in sanitized_name
                            or "\r" in sanitized_value
                        ):
                            self._reject(502)
                            return
                        response_headers.append(
                            (sanitized_name, sanitized_value)
                        )
                    selected_headers = {
                        name.lower(): value
                        for name, value in response_headers
                        if name.lower()
                        in {
                            "content-type",
                            "etag",
                            "retry-after",
                        }
                    }
                    if response.status == http.HTTPStatus.CREATED:
                        proof = ValidatedAcceptanceRequestProof.from_validated_exchange(  # noqa: E501
                            raw_request=body,
                            tarball=tarball,
                            package_coordinate=(
                                f"{ACCEPTANCE_PACKAGE_NAME}@{expected_version}"
                            ),
                            tag=expected_tag,
                            upstream_status=response.status,
                            selected_headers=selected_headers,
                            response_body=response_body,
                        )
                        owner.proof = proof
                        request_fact.update(
                            {
                                "upstream-result": "created",
                                "proof": proof.to_document(),
                            }
                        )
                        owner.processed.set()
                        if drop_accepted_response:
                            self.close_connection = True
                            return
                    else:
                        request_fact["upstream-result"] = (
                            "create-conflict"
                            if response.status == http.HTTPStatus.CONFLICT
                            else "failed"
                        )
                    self.send_response(response.status)
                    for name, value in response_headers:
                        if name.lower() not in {
                            "connection",
                            "content-length",
                            "transfer-encoding",
                        }:
                            self.send_header(name, value)
                    self.send_header("Content-Length", str(len(response_body)))
                    self.end_headers()
                    self.wfile.write(response_body)
                except (OSError, TimeoutError, http.client.HTTPException):
                    self._reject(502)
                finally:
                    connection.close()

        self._server = http.server.ThreadingHTTPServer(
            ("127.0.0.1", 0),
            Handler,
        )
        self._server.timeout = self._proxy_timeout()
        self._thread = threading.Thread(
            target=self._server.serve_forever,
            kwargs={
                "poll_interval": min(
                    0.05,
                    self._proxy_timeout(),
                )
            },
            daemon=True,
        )

    def _proxy_timeout(self) -> float:
        return (
            _remaining_seconds(self._deadline)
            if self._shared_deadline
            else self._legacy_timeout_seconds
        )

    @property
    def registry(self) -> str:
        """Return the loopback registry origin."""
        host = self._server.server_address[0]
        port = self._server.server_address[1]
        return f"http://{host}:{port}"

    def __enter__(self) -> Self:
        """Start the local server."""
        self._thread.start()
        return self

    def __exit__(
        self,
        _exc_type: object,
        _exc_value: object,
        _traceback: object,
    ) -> None:
        """Stop the local server and release its socket."""
        self._server.shutdown()
        self._server.server_close()
        self._thread.join(
            timeout=max(0.0, round(self._deadline - monotonic(), 3))
        )


class _LostResponseProxy(AcceptanceMutationProxy):
    """Compatibility name for the reusable acceptance mutation proxy."""


class _AcceptanceNpmRunner:
    """Bounded npm runner for the temporary fixed acceptance command."""

    def __init__(
        self,
        npm_config: Path,
        *,
        contender_tarballs: dict[str, Path],
        token: str = "",
    ) -> None:
        self._npm_config = npm_config
        self._contender_tarballs = dict(contender_tarballs)
        self._token = token

    def _run_process(
        self,
        argv: tuple[str, ...],
        *,
        env: dict[str, str],
        timeout_seconds: float,
        max_output_bytes: int,
        deadline: float | None = None,
    ) -> dict[str, object]:
        operation_deadline = (
            monotonic() + timeout_seconds if deadline is None else deadline
        )
        try:
            completed = subprocess.run(  # noqa: S603
                argv,
                check=False,
                capture_output=True,
                text=True,
                timeout=_remaining_seconds(operation_deadline),
                env={
                    **_acceptance_subprocess_environment(
                        npm_config=self._npm_config
                    ),
                    **env,
                },
            )
        except subprocess.TimeoutExpired:
            raise TimeoutError("acceptance npm scenario timed out") from None
        result = self._classify(
            completed,
            max_output_bytes=max_output_bytes,
        )
        result["action-executed"] = True
        result["mutation-started"] = True
        return result

    def run(
        self,
        argv: tuple[str, ...],
        *,
        env: dict[str, str],
        timeout_seconds: float,
        max_output_bytes: int,
        deadline: float | None = None,
    ) -> dict[str, object]:
        return self._run_process(
            argv,
            env=env,
            timeout_seconds=timeout_seconds,
            max_output_bytes=max_output_bytes,
            deadline=deadline,
        )

    def run_scenario(  # noqa: C901, PLR0913, PLR0915
        self,
        scenario: str,
        argv: tuple[str, ...],
        *,
        env: dict[str, str],
        timeout_seconds: float,
        max_output_bytes: int,
        deadline: float | None = None,
    ) -> dict[str, object]:
        """Execute deterministic competing/lost-response orchestration."""
        shared_deadline = deadline is not None
        operation_deadline = (
            monotonic() + timeout_seconds if deadline is None else deadline
        )
        commands = [argv]
        if scenario == "identical-race":
            commands.append(argv)
        elif scenario == "differing-race":
            contender = self._contender_tarballs[scenario]
            commands.append(
                tuple(
                    str(contender) if value == argv[2] else value
                    for value in argv
                )
            )
        version = ACCEPTANCE_COORDINATES[scenario].rsplit("@", 1)[1]
        tag = (
            argv[argv.index("--tag") + 1]
            if "--tag" in argv
            else f"wdv3-acceptance-{version.rsplit('.', 1)[1]}"
        )
        tarballs = tuple(
            Path(command[2]).read_bytes() if Path(command[2]).is_file() else b""
            for command in commands
        )
        package_path = "/@hcoona%2fhcoona-release-smoke-npm"
        processes: list[subprocess.Popen[str]] = []
        completed: list[subprocess.CompletedProcess[str]] = []
        system_process_boundary = subprocess.Popen is _SYSTEM_POPEN
        with (
            _LostResponseProxy(
                timeout_seconds=_remaining_seconds(operation_deadline),
                token=self._token or "unavailable-test-token",
                incoming_dummy_token=(
                    _ACCEPTANCE_LOOPBACK_DUMMY_TOKEN
                    if system_process_boundary
                    else None
                ),
                expected_method="PUT",
                expected_path=package_path,
                expected_version=version,
                expected_tag=tag,
                expected_tarballs=tarballs,
                expected_requests=len(commands),
                drop_accepted_response=scenario == "lost-response",
                deadline=operation_deadline,
            ) as proxy,
            tempfile.TemporaryDirectory(
                prefix="wdv3-acceptance-proxy-"
            ) as temporary,
        ):
            local_config = Path(temporary) / ".npmrc"
            dummy_auth = (
                f"//{proxy.registry.removeprefix('http://')}/:"
                f"_authToken={_ACCEPTANCE_LOOPBACK_DUMMY_TOKEN}\n"
                if system_process_boundary
                else ""
            )
            local_config.write_text(
                (
                    f"@hcoona:registry={proxy.registry}\n"
                    f"{dummy_auth}"
                    "ignore-scripts=true\n"
                ),
                encoding="utf-8",
            )
            local_config.chmod(0o600)
            local_commands: list[tuple[str, ...]] = []
            try:
                for command in commands:
                    local_command = list(command)
                    if "--registry" in local_command:
                        registry_index = local_command.index("--registry") + 1
                        local_command[registry_index] = proxy.registry
                    else:
                        local_command.extend(("--registry", proxy.registry))
                    local_commands.append(tuple(local_command))
                    processes.append(
                        subprocess.Popen(  # noqa: S603
                            tuple(local_command),
                            stdout=subprocess.PIPE,
                            stderr=subprocess.PIPE,
                            text=True,
                            env={
                                **_acceptance_subprocess_environment(
                                    npm_config=local_config
                                ),
                                **env,
                            },
                        )
                    )
                for process, command in zip(
                    processes, local_commands, strict=True
                ):
                    # process.communicate(timeout=timeout_seconds) would reset
                    # the budget; every wait uses the shared deadline instead.
                    stdout, stderr = process.communicate(
                        timeout=_remaining_seconds(operation_deadline)
                    )
                    if process.poll() is None:
                        process.kill()
                        process.communicate()
                    completed.append(
                        subprocess.CompletedProcess(
                            command,
                            process.returncode,
                            stdout,
                            stderr,
                        )
                    )
            except subprocess.TimeoutExpired:
                self._cleanup_processes(
                    processes,
                    deadline=operation_deadline if shared_deadline else None,
                )
                # The Adapter maps this to result="timeout" and
                # mutation_classification="unknown".
                error = TimeoutError("acceptance npm scenario timed out")
                error.action_executed = bool(processes)  # type: ignore[attr-defined]
                error.mutation_started = getattr(  # type: ignore[attr-defined]
                    proxy, "observed", threading.Event()
                ).is_set()
                raise error from None
            except OSError as error:
                self._cleanup_processes(
                    processes,
                    deadline=operation_deadline if shared_deadline else None,
                )
                error.action_executed = bool(processes)  # type: ignore[attr-defined]
                error.mutation_started = getattr(  # type: ignore[attr-defined]
                    proxy, "observed", threading.Event()
                ).is_set()
                raise
            if scenario == "lost-response" and proxy.proof is not None:
                return {
                    "outcome": "lost-response-processed",
                    "validated-request-proof": proxy.proof,
                    "request-digest": proxy.proof.request_digest,
                    "upstream-status": proxy.proof.upstream_status,
                    "selected-headers": dict(proxy.proof.selected_headers),
                    "response-body-digest": proxy.proof.response_body_digest,
                    "response-identity-digest": (
                        proxy.proof.response_identity_digest
                    ),
                    "action-executed": True,
                    "mutation-started": getattr(
                        proxy, "observed", proxy.processed
                    ).is_set(),
                }
        results = [
            self._classify(result, max_output_bytes=max_output_bytes)
            for result in completed
        ]
        outcomes = [result["outcome"] for result in results]
        if (
            len(outcomes) == _PAIR_FIELD_COUNT
            and outcomes.count("created") == 1
            and outcomes.count("create-conflict") == 1
        ):
            winner_index = outcomes.index("created")
            content_hashes = [
                "sha512:"
                + hashlib.sha512(Path(command[2]).read_bytes()).hexdigest()
                for command in commands
            ]
            contenders = [
                {
                    "contender-id": f"contender-{index + 1}",
                    "request-digest": (
                        "sha256:"
                        + hashlib.sha256(
                            _acceptance_publish_body(
                                tarballs[index],
                                version=version,
                                tag=tag,
                            )
                        ).hexdigest()
                    ),
                    "tarball-sha512": content_hashes[index],
                    "upstream-result": outcomes[index],
                }
                for index in range(len(commands))
            ]
            return {
                "outcome": "create-conflict",
                "action-executed": True,
                "mutation-started": getattr(
                    proxy, "observed", proxy.processed
                ).is_set(),
                "contender-outcomes": outcomes,
                "winner-content-sha512": content_hashes[winner_index],
                "contender-content-sha512": content_hashes[1],
                "race-overlap-proven": len(processes) == _PAIR_FIELD_COUNT,
                "barrier-arrivals": [
                    f"contender-{index + 1}" for index in range(len(processes))
                ],
                "barrier-release": "simultaneous",
                "contenders": contenders,
                "response-identity-digest": canonical_sha256(
                    cast("JsonValue", results)
                ),
            }
        return {
            "outcome": "failed",
            "action-executed": bool(processes),
            "mutation-started": getattr(
                proxy, "observed", proxy.processed
            ).is_set(),
            "contender-outcomes": outcomes,
            "response-identity-digest": canonical_sha256(
                cast("JsonValue", results)
            ),
        }

    @staticmethod
    def _cleanup_processes(
        processes: list[subprocess.Popen[str]],
        *,
        deadline: float | None = None,
    ) -> None:
        for process in processes:
            if process.poll() is None:
                process.kill()

        for process in processes:
            if deadline is None:
                process.communicate()
                continue
            try:
                process.communicate(timeout=_remaining_seconds(deadline))
            except (subprocess.TimeoutExpired, TimeoutError):
                continue

    @staticmethod
    def _classify(
        completed: subprocess.CompletedProcess[str],
        *,
        max_output_bytes: int,
    ) -> dict[str, object]:
        output = (completed.stdout + "\n" + completed.stderr).encode()
        if len(output) > max_output_bytes:
            raise ValueError("acceptance npm output exceeded the bounded limit")
        outcome = "created" if completed.returncode == 0 else "failed"
        lowered = output.decode(errors="replace").lower()
        if completed.returncode != 0 and any(
            token in lowered
            for token in ("e409", "conflict", "previously published")
        ):
            outcome = "create-conflict"
        return {
            "outcome": outcome,
            "response-identity-digest": (
                f"sha256:{hashlib.sha256(output).hexdigest()}"
            ),
        }


class _AcceptanceHttpTransport(Protocol):
    def get(
        self,
        url: str,
        *,
        headers: tuple[tuple[str, str], ...],
        timeout: float,
        max_bytes: int,
    ) -> GitHubPackagesHttpResponse: ...


class _AcceptanceNpmTransport:
    """Bounded metadata plus exact downloaded-tarball observer."""

    def __init__(
        self,
        npm_config: Path,
        *,
        token: str,
        target_sha: str,
    ) -> None:
        self._npm_config = npm_config
        self._token = token
        self._target_sha = target_sha
        self._transport: _AcceptanceHttpTransport = (
            GitHubPackagesHttpTransport()
        )

    def _authenticated_get(
        self,
        url: str,
        *,
        headers: tuple[tuple[str, str], ...],
        timeout: float,
        max_bytes: int,
    ) -> GitHubPackagesHttpResponse:
        authenticated_headers = tuple(
            (
                name,
                "Bearer " + self._token
                if name.lower() == "authorization"
                else value,
            )
            for name, value in headers
        )
        return self._transport.get(
            url,
            headers=authenticated_headers,
            timeout=timeout,
            max_bytes=max_bytes,
        )

    def observe(  # noqa: C901, PLR0911, PLR0912, PLR0915
        self,
        package_coordinate: str,
        tag: str,
        *,
        timeout_seconds: float,
        max_response_bytes: int,
        deadline: float | None = None,
    ) -> dict[str, object]:
        operation_deadline = (
            monotonic() + timeout_seconds if deadline is None else deadline
        )
        version = package_coordinate.rsplit("@", 1)[-1]
        api_headers = (
            ("Accept", "application/vnd.github+json"),
            ("Authorization", "******"),
            ("X-GitHub-Api-Version", "2022-11-28"),
            ("User-Agent", "three-workflow-delivery-v3"),
        )
        package_url = (
            "https://api.github.com/users/hcoona/packages/npm/"
            "hcoona-release-smoke-npm"
        )
        package_response = self._authenticated_get(
            package_url,
            headers=api_headers,
            timeout=_remaining_seconds(operation_deadline),
            max_bytes=max_response_bytes,
        )
        api_bodies = [package_response.body]

        def api_digest() -> str:
            return "sha256:" + hashlib.sha256(b"".join(api_bodies)).hexdigest()

        if (
            package_response.status == 404  # noqa: PLR2004
            and not package_response.truncated
            and package_response.complete
        ):
            return {
                "state": "absent",
                "response-identity-digest": api_digest(),
            }
        if (
            package_response.status != 200  # noqa: PLR2004
            or package_response.truncated
            or not package_response.complete
        ):
            return {
                "state": "unknown",
                "response-identity-digest": api_digest(),
            }
        try:
            package_metadata = json.loads(package_response.body)
        except (UnicodeDecodeError, json.JSONDecodeError):
            package_metadata = None
        repository = (
            package_metadata.get("repository")
            if type(package_metadata) is dict
            else None
        )
        owner = (
            package_metadata.get("owner")
            if type(package_metadata) is dict
            else None
        )
        owner_login = owner.get("login") if type(owner) is dict else None
        if (
            type(package_metadata) is not dict
            or package_metadata.get("package_type") != "npm"
            or package_metadata.get("name") != "hcoona-release-smoke-npm"
            or type(repository) is not dict
            or repository.get("full_name") != "hcoona/three"
            or owner_login != "hcoona"
        ):
            return {
                "state": "unknown",
                "response-identity-digest": api_digest(),
            }

        version_present = False
        for page in range(1, 101):
            try:
                remaining = _remaining_seconds(operation_deadline)
            except TimeoutError:
                return {
                    "state": "unknown",
                    "response-identity-digest": api_digest(),
                }
            versions_response = self._authenticated_get(
                f"{package_url}/versions?"
                f"per_page={_ACCEPTANCE_VERSIONS_PER_PAGE}&page={page}",
                headers=api_headers,
                timeout=remaining,
                max_bytes=max_response_bytes,
            )
            api_bodies.append(versions_response.body)
            if (
                versions_response.status != 200  # noqa: PLR2004
                or versions_response.truncated
                or not versions_response.complete
            ):
                return {
                    "state": "unknown",
                    "response-identity-digest": api_digest(),
                }
            try:
                versions = json.loads(versions_response.body)
            except (UnicodeDecodeError, json.JSONDecodeError):
                versions = None
            if type(versions) is not list or any(
                type(item) is not dict for item in versions
            ):
                return {
                    "state": "unknown",
                    "response-identity-digest": api_digest(),
                }
            for item in versions:
                if item.get("name") != version:
                    continue
                metadata = item.get("metadata")
                if type(metadata) is dict and metadata.get(
                    "package_type"
                ) not in {None, "npm"}:
                    return {
                        "state": "unknown",
                        "response-identity-digest": api_digest(),
                    }
                version_present = True
            if version_present or len(versions) < _ACCEPTANCE_VERSIONS_PER_PAGE:
                break
        if (
            not version_present
            and len(versions) == _ACCEPTANCE_VERSIONS_PER_PAGE
        ):
            return {
                "state": "unknown",
                "response-identity-digest": api_digest(),
            }
        if not version_present:
            return {
                "state": "absent",
                "response-identity-digest": api_digest(),
            }

        with tempfile.TemporaryDirectory(
            prefix="wdv3-acceptance-readback-"
        ) as temporary:
            readback_config = Path(temporary) / ".npmrc"
            readback_config.write_text(
                (
                    "@hcoona:registry=https://npm.pkg.github.com\n"
                    f"//npm.pkg.github.com/:_authToken={self._token}\n"
                    "ignore-scripts=true\n"
                ),
                encoding="utf-8",
            )
            readback_config.chmod(0o600)
            completed = subprocess.run(  # noqa: S603
                (
                    "npm",
                    "view",
                    package_coordinate,
                    "version",
                    "dist.tarball",
                    "dist-tags",
                    "--json",
                    "--registry",
                    "https://npm.pkg.github.com",
                ),
                check=False,
                capture_output=True,
                text=True,
                timeout=_remaining_seconds(operation_deadline),
                env=_acceptance_subprocess_environment(
                    npm_config=readback_config
                ),
            )
        response = (completed.stdout + "\n" + completed.stderr).encode()
        if len(response) > max_response_bytes:
            raise ValueError(
                "acceptance npm observation exceeded the bounded limit"
            )
        response_digest = f"sha256:{hashlib.sha256(response).hexdigest()}"
        sanitized_stdout = completed.stdout.replace(self._token, "******")
        sanitized_stderr = (
            completed.stderr.replace(self._token, "******")
            .replace(str(readback_config), "<temporary-npm-config>")
            .replace(
                "//npm.pkg.github.com/:_authToken=******",
                "//npm.pkg.github.com/:_authToken=<redacted>",
            )
        )
        readback_result = {
            "outcome": "success" if completed.returncode == 0 else "failed",
            "stdout": sanitized_stdout,
            "stderr": sanitized_stderr,
        }
        if completed.returncode != 0:
            return {
                "state": "unknown",
                "response-identity-digest": response_digest,
                "readback-result": readback_result,
                "diagnostics": ("npm-view-readback-failed",),
            }
        value = json.loads(completed.stdout)
        if type(value) is not dict:
            raise ValueError("acceptance npm observation was malformed")
        observed_version = value.get("version")
        tarball_url = value.get("dist.tarball")
        if tarball_url is None and type(value.get("dist")) is dict:
            tarball_url = value["dist"].get("tarball")
        tags = value.get("dist-tags", {})
        observed_tag_version = tags.get(tag) if type(tags) is dict else None
        if (
            type(observed_version) is not str
            or type(observed_tag_version) is not str
            or type(tarball_url) is not str
        ):
            return {
                "state": "unknown",
                "response-identity-digest": response_digest,
                "readback-result": readback_result,
                "diagnostics": ("npm-view-readback-malformed",),
            }
        tarball_response = self._authenticated_get(
            tarball_url,
            headers=(
                ("Accept", "application/octet-stream"),
                ("Authorization", f"Bearer {self._token}"),
                ("User-Agent", "three-workflow-delivery-v3"),
            ),
            timeout=_remaining_seconds(operation_deadline),
            max_bytes=25_000_000,
        )
        if (
            tarball_response.status < 200  # noqa: PLR2004
            or tarball_response.status >= 300  # noqa: PLR2004
            or tarball_response.truncated
            or not tarball_response.complete
        ):
            return {
                "state": "unknown",
                "response-identity-digest": response_digest,
                "readback-result": readback_result,
                "diagnostics": ("npm-tarball-readback-failed",),
            }
        observation = inspect_fixed_acceptance_tarball(
            tarball_response.body,
            package_coordinate=package_coordinate,
            tag=tag,
            observed_version=observed_version,
            observed_tag_version=observed_tag_version,
            target_sha=self._target_sha,
        )
        observation["response-identity-digest"] = canonical_sha256(
            {
                "github-api": api_digest(),
                "metadata": response_digest,
                "tarball": (
                    f"sha256:{hashlib.sha256(tarball_response.body).hexdigest()}"
                ),
            }
        )
        observation["readback-result"] = readback_result
        observation["diagnostics"] = (
            *cast(
                "tuple[str, ...]",
                observation.get("diagnostics", ()),
            ),
            "npm-view-readback-succeeded",
        )
        return observation


def _verify_uploaded_payload(
    path: str,
    *,
    artifact_id: int,
    artifact_digest: str,
) -> bytes:
    if artifact_id <= 0:
        raise ValueError("downloaded artifact ID must be positive")
    content = Path(path).read_bytes()
    expected = _normalized_digest(artifact_digest)
    actual = f"sha256:{hashlib.sha256(content).hexdigest()}"
    if actual != expected:
        raise ValueError(
            "downloaded payload digest does not match upload output"
        )
    return content


def _release_bindings(
    arguments: argparse.Namespace,
    *,
    producer: str | None = None,
    purpose: str = "release-simulation",
    request_id: str | None = None,
    execution: BuddyExecutionIdentity | None = None,
) -> ReleaseAdmissionBindings:
    return ReleaseAdmissionBindings(
        purpose=purpose,
        workflow_run_id=arguments.workflow_run_id,
        run_attempt=arguments.run_attempt,
        target=arguments.target,
        producer=producer,
        request_id=request_id,
        execution=execution,
    )


def _qualification_purpose(arguments: argparse.Namespace) -> str:
    purpose = getattr(arguments, "purpose", "release-simulation")
    if purpose not in {"release-simulation", "live-release"}:
        raise ValueError("Release qualification purpose is unsupported")
    return cast("str", purpose)


def _validate_optional_uploaded_record_transport(
    group: str,
    *,
    path: str | None,
    record_digest: str | None,
    artifact_id: int | None,
    artifact_digest: str | None,
) -> None:
    if (
        path is None
        and record_digest is None
        and artifact_id is None
        and artifact_digest is None
    ):
        return
    if (
        path is None
        or record_digest is None
        or artifact_id is None
        or artifact_digest is None
    ):
        missing = ", ".join(
            name
            for name, value in (
                ("path", path),
                ("record digest", record_digest),
                ("artifact ID", artifact_id),
                ("artifact digest", artifact_digest),
            )
            if value is None
        )
        raise ValueError(
            f"{group} uploaded record transport is partial: missing {missing}; "
            "path, record digest, artifact ID, and artifact digest must be all "
            "present or all absent"
        )


def _load_release_record(  # noqa: PLR0913
    path: str,
    *,
    record_type: type[
        ReleaseIntent
        | SimulationBinding
        | ReleaseAttemptBinding
        | QualificationSnapshot
        | ReleaseArtifact
        | QualificationEvidence
        | QualificationDecision
        | ProjectionObservation
        | HypotheticalAction
        | PublicationSnapshot
        | AuthorizationRecord
        | CapabilityAdmissionDecision
        | ActionResult
        | Receipt
        | CapabilityGroupResultBundle
        | AttemptOutcome
        | SimulationOutcome
    ],
    expected_digest: str,
    artifact_id: int,
    artifact_digest: str,
    bindings: ReleaseAdmissionBindings,
) -> (
    ReleaseIntent
    | SimulationBinding
    | ReleaseAttemptBinding
    | QualificationSnapshot
    | ReleaseArtifact
    | QualificationEvidence
    | QualificationDecision
    | ProjectionObservation
    | HypotheticalAction
    | PublicationSnapshot
    | AuthorizationRecord
    | CapabilityAdmissionDecision
    | ActionResult
    | Receipt
    | CapabilityGroupResultBundle
    | AttemptOutcome
    | SimulationOutcome
):
    content = _verify_uploaded_payload(
        path,
        artifact_id=artifact_id,
        artifact_digest=artifact_digest,
    )
    admitted = admit_release_record(
        content,
        expected_type=record_type,
        expected_digest=expected_digest,
        expected_bindings=bindings,
    )
    if type(admitted) is not record_type:
        raise TypeError("Release record admission returned the wrong type")
    return admitted


def _release_model_context(intent: ReleaseIntent) -> CompilationContext:
    return CompilationContext(
        request_id=intent.request_id,
        purpose="release-simulation",
        workflow_run_id=intent.workflow_run_id,
        run_attempt=intent.run_attempt,
        target=intent.target,
        producer="compile-simulation-model",
        control=f"workflow-delivery-v3:{intent.target}",
        catalog_digest=catalog_digest(),
        channel="official",
        release_unit=intent.release_unit,
    )


def _live_model_context(intent: ReleaseIntent) -> CompilationContext:
    return CompilationContext(
        request_id=intent.request_id,
        purpose="live-release",
        workflow_run_id=intent.workflow_run_id,
        run_attempt=intent.run_attempt,
        target=intent.target,
        producer="compile-live-model",
        control=f"workflow-delivery-v3:{intent.target}",
        catalog_digest=catalog_digest(),
    )


def _load_release_intent(
    arguments: argparse.Namespace,
    *,
    purpose: str = "release-simulation",
) -> ReleaseIntent:
    record = _load_release_record(
        arguments.intent,
        record_type=ReleaseIntent,
        expected_digest=arguments.intent_digest,
        artifact_id=arguments.intent_artifact_id,
        artifact_digest=arguments.intent_artifact_digest,
        bindings=_release_bindings(arguments, purpose=purpose),
    )
    return cast("ReleaseIntent", record)


def _load_live_intent(
    arguments: argparse.Namespace,
) -> ReleaseIntent:
    record = _load_release_record(
        arguments.intent,
        record_type=ReleaseIntent,
        expected_digest=arguments.intent_digest,
        artifact_id=arguments.intent_artifact_id,
        artifact_digest=arguments.intent_artifact_digest,
        bindings=_release_bindings(arguments, purpose="live-release"),
    )
    return cast("ReleaseIntent", record)


def _load_release_model(
    arguments: argparse.Namespace,
    intent: ReleaseIntent,
) -> AdmittedRepositoryModelSnapshot:
    content = _verify_uploaded_payload(
        arguments.repository_model,
        artifact_id=arguments.repository_model_artifact_id,
        artifact_digest=arguments.repository_model_artifact_digest,
    )
    return admit_repository_model_snapshot(
        content,
        expected_context=_release_model_context(intent),
        expected_digest=arguments.repository_model_digest,
    )


def _load_live_model(
    arguments: argparse.Namespace,
    intent: ReleaseIntent,
) -> AdmittedRepositoryModelSnapshot:
    content = _verify_uploaded_payload(
        arguments.repository_model,
        artifact_id=arguments.repository_model_artifact_id,
        artifact_digest=arguments.repository_model_artifact_digest,
    )
    return admit_repository_model_snapshot(
        content,
        expected_context=_live_model_context(intent),
        expected_digest=arguments.repository_model_digest,
    )


def _load_attempt_binding(
    arguments: argparse.Namespace,
) -> ReleaseAttemptBinding:
    record = _load_release_record(
        arguments.attempt_binding,
        record_type=ReleaseAttemptBinding,
        expected_digest=arguments.attempt_binding_digest,
        artifact_id=arguments.attempt_binding_artifact_id,
        artifact_digest=arguments.attempt_binding_artifact_digest,
        bindings=_release_bindings(arguments, purpose="live-release"),
    )
    return cast("ReleaseAttemptBinding", record)


def _load_simulation_binding(
    arguments: argparse.Namespace,
) -> SimulationBinding:
    record = _load_release_record(
        arguments.simulation_binding,
        record_type=SimulationBinding,
        expected_digest=arguments.simulation_binding_digest,
        artifact_id=arguments.simulation_binding_artifact_id,
        artifact_digest=arguments.simulation_binding_artifact_digest,
        bindings=_release_bindings(arguments),
    )
    return cast("SimulationBinding", record)


def _load_qualification_snapshot(
    arguments: argparse.Namespace,
) -> QualificationSnapshot:
    record = _load_release_record(
        arguments.qualification_snapshot,
        record_type=QualificationSnapshot,
        expected_digest=arguments.qualification_snapshot_digest,
        artifact_id=arguments.qualification_snapshot_artifact_id,
        artifact_digest=arguments.qualification_snapshot_artifact_digest,
        bindings=_release_bindings(
            arguments,
            purpose=_qualification_purpose(arguments),
        ),
    )
    return cast("QualificationSnapshot", record)


def _load_live_qualification_snapshot(
    arguments: argparse.Namespace,
) -> QualificationSnapshot:
    record = _load_release_record(
        arguments.qualification_snapshot,
        record_type=QualificationSnapshot,
        expected_digest=arguments.qualification_snapshot_digest,
        artifact_id=arguments.qualification_snapshot_artifact_id,
        artifact_digest=arguments.qualification_snapshot_artifact_digest,
        bindings=_release_bindings(arguments, purpose="live-release"),
    )
    return cast("QualificationSnapshot", record)


def _load_release_adapter_context(
    arguments: argparse.Namespace,
    snapshot: QualificationSnapshot,
) -> ReleaseAdapterContext:
    content = _verify_uploaded_payload(
        arguments.adapter_context,
        artifact_id=arguments.adapter_context_artifact_id,
        artifact_digest=arguments.adapter_context_artifact_digest,
    )
    return release_adapter_context_from_bytes(
        content,
        snapshot=snapshot,
        expected_digest=arguments.adapter_context_digest,
    )


def _load_release_artifact_record(
    arguments: argparse.Namespace,
    *,
    path: str | None = None,
    expected_digest: str | None = None,
    artifact_id: int | None = None,
    artifact_digest: str | None = None,
) -> ReleaseArtifact:
    record = _load_release_record(
        path or arguments.release_artifact,
        record_type=ReleaseArtifact,
        expected_digest=expected_digest or arguments.release_artifact_digest,
        artifact_id=artifact_id or arguments.release_artifact_artifact_id,
        artifact_digest=(
            artifact_digest or arguments.release_artifact_artifact_digest
        ),
        bindings=_release_bindings(
            arguments,
            producer="build-tarball",
            purpose=_qualification_purpose(arguments),
        ),
    )
    return cast("ReleaseArtifact", record)


def _load_live_release_artifact_record(
    arguments: argparse.Namespace,
) -> ReleaseArtifact:
    record = _load_release_record(
        arguments.release_artifact,
        record_type=ReleaseArtifact,
        expected_digest=arguments.release_artifact_digest,
        artifact_id=arguments.release_artifact_artifact_id,
        artifact_digest=arguments.release_artifact_artifact_digest,
        bindings=_release_bindings(
            arguments,
            producer="build-tarball",
            purpose="live-release",
        ),
    )
    return cast("ReleaseArtifact", record)


def _load_qualification_decision(
    arguments: argparse.Namespace,
) -> QualificationDecision:
    record = _load_release_record(
        arguments.qualification_decision,
        record_type=QualificationDecision,
        expected_digest=arguments.qualification_decision_digest,
        artifact_id=arguments.qualification_decision_artifact_id,
        artifact_digest=arguments.qualification_decision_artifact_digest,
        bindings=_release_bindings(
            arguments,
            purpose=_qualification_purpose(arguments),
        ),
    )
    return cast("QualificationDecision", record)


def _load_live_qualification_decision(
    arguments: argparse.Namespace,
) -> QualificationDecision:
    record = _load_release_record(
        arguments.qualification_decision,
        record_type=QualificationDecision,
        expected_digest=arguments.qualification_decision_digest,
        artifact_id=arguments.qualification_decision_artifact_id,
        artifact_digest=arguments.qualification_decision_artifact_digest,
        bindings=_release_bindings(arguments, purpose="live-release"),
    )
    return cast("QualificationDecision", record)


def _release_normalize_request_command(arguments: argparse.Namespace) -> int:
    intent = normalize_official_simulation_intent(
        repository=arguments.repository,
        selected_ref=arguments.selected_ref,
        target=arguments.target,
        actor=arguments.actor,
        workflow_run_id=arguments.workflow_run_id,
        run_attempt=arguments.run_attempt,
    )
    _write_output(arguments.output, intent.to_document())
    _record_outputs(
        arguments.github_output,
        role="intent",
        digest=intent.intent_digest,
        extra=(("request-id", intent.request_id),),
    )
    return 0


def _release_admit_intent_command(arguments: argparse.Namespace) -> int:
    _load_release_intent(
        arguments,
        purpose=_qualification_purpose(arguments),
    )
    return 0


def _release_compile_model_command(arguments: argparse.Namespace) -> int:
    intent = _load_release_intent(arguments)
    repo_root = Path(arguments.repo_root).resolve()
    context = _release_model_context(intent)
    manifest = first_slice_provider_manifest(
        context,
        provider_producer="discover-node",
    )
    _verify_uploaded_payload(
        arguments.provider_result,
        artifact_id=arguments.provider_artifact_id,
        artifact_digest=arguments.provider_artifact_digest,
    )
    result = _load_node_provider_result(
        arguments.provider_result,
        expected_binding=provider_binding(manifest, "node-first-slice"),
        expected_manifest_digest=manifest.manifest_digest,
    )
    request_transport_digest = _normalized_digest(
        arguments.intent_artifact_digest
    )
    provider_transport_digest = _normalized_digest(
        arguments.provider_artifact_digest
    )
    bundle = create_node_provider_fact_bundle(
        result,
        manifest_digest=manifest.manifest_digest,
        manifest_entry_id=manifest.requests[0].entry_id,
        request_artifact_id=arguments.intent_artifact_id,
        request_artifact_digest=request_transport_digest,
        transport_id=arguments.provider_artifact_id,
        transport_digest=provider_transport_digest,
    )
    admitted = admit_node_provider_fact_bundle(
        bundle,
        context=context,
        manifest=manifest,
        admission=FactBundleAdmissionContext(
            request_artifact_id=arguments.intent_artifact_id,
            request_artifact_digest=request_transport_digest,
            transport_id=arguments.provider_artifact_id,
            transport_digest=provider_transport_digest,
            bundle_digest=bundle.bundle_digest,
        ),
    )
    snapshot = compile_repository_model(
        repo_root,
        context,
        manifest,
        [admitted],
    )
    canonical_bytes = canonicalize(snapshot.to_document())
    admitted_model = admit_repository_model_snapshot(
        canonical_bytes,
        expected_context=context,
        expected_digest=snapshot.snapshot_digest,
    )
    Path(arguments.output).parent.mkdir(parents=True, exist_ok=True)
    Path(arguments.output).write_bytes(admitted_model.canonical_bytes)
    _record_outputs(
        arguments.github_output,
        role="repository-model",
        digest=admitted_model.canonical_digest,
    )
    return 0


def _release_create_identity_command(arguments: argparse.Namespace) -> int:
    intent = _load_release_intent(arguments)
    model = _load_release_model(arguments, intent)
    binding = derive_simulation_binding(intent, model)
    _write_output(arguments.output, binding.to_document())
    _record_outputs(
        arguments.github_output,
        role="simulation-binding",
        digest=binding.binding_digest,
        extra=(("simulation-id", binding.simulation.identity),),
    )
    return 0


def _release_plan_qualification_command(arguments: argparse.Namespace) -> int:
    if hasattr(arguments, "attempt_binding"):
        intent = _load_live_intent(arguments)
        model = _load_live_model(arguments, intent)
        binding = _load_attempt_binding(arguments)
        snapshot = plan_live_qualification(intent, binding, model)
        purpose = "live-release"
    else:
        intent = _load_release_intent(arguments)
        model = _load_release_model(arguments, intent)
        simulation_binding = _load_simulation_binding(arguments)
        snapshot = plan_official_simulation_qualification(
            intent,
            simulation_binding,
            model,
        )
        purpose = "release-simulation"
    source_date_epoch = int(
        _command_stdout(
            ("git", "show", "-s", "--format=%ct", snapshot.target),
            Path(arguments.repo_root).resolve(),
        )
    )
    context = form_release_adapter_context(
        snapshot,
        model,
        source_date_epoch=source_date_epoch,
        node_version=_command_stdout(
            ("node", "--version"),
            Path(arguments.repo_root).resolve(),
        ),
        pnpm_version=_command_stdout(
            ("pnpm", "--version"),
            Path(arguments.repo_root).resolve(),
        ),
        npm_version=_command_stdout(
            ("npm", "--version"),
            Path(arguments.repo_root).resolve(),
        ),
    )
    _write_output(arguments.output, snapshot.to_document())
    _write_output(arguments.adapter_context_output, context.to_document())
    _record_outputs(
        arguments.github_output,
        role="qualification-snapshot",
        digest=snapshot.snapshot_digest,
        extra=(
            ("adapter-context-digest", context.context_digest),
            (
                "adapter-context-digest-hex",
                context.context_digest.removeprefix("sha256:"),
            ),
            (
                "tarball-artifact-name",
                release_artifact_transport_name(
                    repository=snapshot.repository,
                    purpose=purpose,
                    output=snapshot.outputs[0],
                    qualification_snapshot_digest=snapshot.snapshot_digest,
                    workflow_run_id=arguments.workflow_run_id,
                    run_attempt=arguments.run_attempt,
                    producer="build-tarball",
                ),
            ),
        ),
    )
    return 0


def _release_run_build_command(arguments: argparse.Namespace) -> int:
    snapshot = _load_qualification_snapshot(arguments)
    context = _load_release_adapter_context(arguments, snapshot)
    request = node_build_request(
        Path(arguments.repo_root).resolve(),
        snapshot,
        context,
    )
    mechanics, failure = execute_release_build(snapshot, request)
    if failure is not None:
        _write_output(arguments.failure_evidence_output, failure.to_document())
        _record_outputs(
            arguments.github_output,
            role="build-evidence",
            digest=failure.evidence_digest,
            extra=(("build-status", "failed"),),
        )
        return 0
    if mechanics is None:
        raise ValueError(
            "Release build returned neither mechanics nor Evidence"
        )
    Path(arguments.tarball_output).parent.mkdir(parents=True, exist_ok=True)
    Path(arguments.tarball_output).write_bytes(mechanics.tarball)
    _write_output(
        arguments.mechanical_output,
        mechanical_build_document(mechanics),
    )
    _append_outputs(
        arguments.github_output,
        (
            ("build-status", "satisfied"),
            (
                "tarball-content-digest",
                mechanics.content.content_sha256,
            ),
            (
                "tarball-content-digest-hex",
                mechanics.content.content_sha256.removeprefix("sha256:"),
            ),
        ),
    )
    return 0


def _release_form_artifact_command(arguments: argparse.Namespace) -> int:
    snapshot = _load_qualification_snapshot(arguments)
    context = _load_release_adapter_context(arguments, snapshot)
    tarball = Path(arguments.tarball).read_bytes()
    mechanics = mechanical_build_from_bytes(
        Path(arguments.mechanical_result).read_bytes(),
        snapshot=snapshot,
        tarball=tarball,
    )
    if (
        canonical_sha256(context.witness.to_document())
        != mechanics.witness_digest
    ):
        raise ValueError("Mechanical result witness does not match context")
    transport = ArtifactTransportIdentity(
        artifact_id=arguments.tarball_artifact_id,
        artifact_name=arguments.tarball_artifact_name,
        artifact_url=arguments.tarball_artifact_url,
        transport_digest=_normalized_digest(arguments.tarball_artifact_digest),
        producer="build-tarball",
        workflow_run_id=arguments.workflow_run_id,
        run_attempt=arguments.run_attempt,
    )
    artifact, evidence = form_uploaded_release_artifact(
        snapshot,
        mechanics,
        transport,
    )
    _write_output(arguments.artifact_output, artifact.to_document())
    _write_output(arguments.evidence_output, evidence.to_document())
    _record_outputs(
        arguments.github_output,
        role="release-artifact",
        digest=artifact.artifact_digest,
        extra=(
            ("build-evidence-digest", evidence.evidence_digest),
            (
                "build-evidence-digest-hex",
                evidence.evidence_digest.removeprefix("sha256:"),
            ),
        ),
    )
    return 0


def _release_project_test_command(arguments: argparse.Namespace) -> int:
    snapshot = _load_qualification_snapshot(arguments)
    context = _load_release_adapter_context(arguments, snapshot)
    evidence = execute_project_test(
        snapshot,
        Path(arguments.repo_root).resolve() / context.project_path,
        runtime_request(snapshot, context),
    )
    _write_output(arguments.output, evidence.to_document())
    _record_outputs(
        arguments.github_output,
        role="project-test-evidence",
        digest=evidence.evidence_digest,
    )
    return 0


def _release_tarball_qualification_command(
    arguments: argparse.Namespace,
    *,
    operation: str,
) -> int:
    snapshot = _load_qualification_snapshot(arguments)
    context = _load_release_adapter_context(arguments, snapshot)
    artifact = _load_release_artifact_record(arguments)
    tarball = Path(arguments.tarball).read_bytes()
    expectation = artifact_expectation(snapshot, context, artifact)
    if operation == "artifact-contents":
        evidence = qualify_release_artifact_contents(
            snapshot,
            artifact,
            tarball,
            expectation,
        )
        role = "artifact-contents-evidence"
    elif operation == "install-import":
        evidence = qualify_release_install_import(
            snapshot,
            artifact,
            tarball,
            expectation,
            runtime_request(snapshot, context),
        )
        role = "install-import-evidence"
    else:
        raise ValueError(f"unsupported Release qualification: {operation}")
    _write_output(arguments.output, evidence.to_document())
    _record_outputs(
        arguments.github_output,
        role=role,
        digest=evidence.evidence_digest,
    )
    return 0


def _release_artifact_contents_command(arguments: argparse.Namespace) -> int:
    return _release_tarball_qualification_command(
        arguments,
        operation="artifact-contents",
    )


def _release_install_import_command(arguments: argparse.Namespace) -> int:
    return _release_tarball_qualification_command(
        arguments,
        operation="install-import",
    )


def _release_incomplete_evidence_command(arguments: argparse.Namespace) -> int:
    snapshot = _load_qualification_snapshot(arguments)
    evidence = form_incomplete_evidence(
        snapshot,
        arguments.obligation_id,
        reason="blocked-by-prerequisite",
    )
    _write_output(arguments.output, evidence.to_document())
    _record_outputs(
        arguments.github_output,
        role=arguments.output_role,
        digest=evidence.evidence_digest,
    )
    return 0


def _optional_evidence(
    arguments: argparse.Namespace,
    *,
    prefix: str,
    producer: str,
    purpose: str | None = None,
) -> QualificationEvidence | None:
    path = getattr(arguments, prefix.replace("-", "_"))
    if path is None:
        return None
    record = _load_release_record(
        path,
        record_type=QualificationEvidence,
        expected_digest=getattr(
            arguments,
            f"{prefix.replace('-', '_')}_digest",
        ),
        artifact_id=getattr(
            arguments,
            f"{prefix.replace('-', '_')}_artifact_id",
        ),
        artifact_digest=getattr(
            arguments,
            f"{prefix.replace('-', '_')}_artifact_digest",
        ),
        bindings=_release_bindings(
            arguments,
            producer=producer,
            purpose=(
                _qualification_purpose(arguments)
                if purpose is None
                else purpose
            ),
        ),
    )
    return cast("QualificationEvidence", record)


def _release_finalize_qualification_command(
    arguments: argparse.Namespace,
) -> int:
    snapshot = _load_qualification_snapshot(arguments)
    evidence = tuple(
        item
        for item in (
            _optional_evidence(
                arguments,
                prefix="build-evidence",
                producer="build-tarball",
            ),
            _optional_evidence(
                arguments,
                prefix="project-test-evidence",
                producer="project-test",
            ),
            _optional_evidence(
                arguments,
                prefix="artifact-contents-evidence",
                producer="npm-artifact-qualification",
            ),
            _optional_evidence(
                arguments,
                prefix="install-import-evidence",
                producer="npm-artifact-qualification",
            ),
        )
        if item is not None
    )
    artifacts: tuple[ReleaseArtifact, ...] = ()
    if arguments.release_artifact is not None:
        artifacts = (_load_release_artifact_record(arguments),)
    decision = finalize_qualification(snapshot, evidence, artifacts)
    _write_output(arguments.output, decision.to_document())
    _record_outputs(
        arguments.github_output,
        role="qualification-decision",
        digest=decision.decision_digest,
        extra=(
            ("qualification-result", decision.terminal_result),
            ("qualification-failure-class", decision.failure_class),
        ),
    )
    return 0


def _release_observe_npmjs_command(
    arguments: argparse.Namespace,
) -> int:
    snapshot = _load_qualification_snapshot(arguments)
    decision = _load_qualification_decision(arguments)
    if not isinstance(snapshot.subject, SimulationBinding):
        raise TypeError("npmjs observation requires simulation Snapshot")
    observations: tuple[ProjectionObservation, ...] = ()
    if decision.terminal_result == "success":
        context = _load_release_adapter_context(arguments, snapshot)
        artifact = _load_release_artifact_record(arguments)
        expectation = artifact_expectation(snapshot, context, artifact)
        observations = (
            observe_npmjs_projection(
                snapshot,
                decision,
                artifact,
                expectation,
            ),
        )
    bundle = SimulationObservationSet(
        simulation=snapshot.subject.simulation,
        purpose=snapshot.subject.purpose,
        target=snapshot.target,
        producer=NPMJS_OBSERVER_PRODUCER,
        workflow_run_id=snapshot.subject.simulation.workflow_run_id,
        run_attempt=snapshot.subject.simulation.run_attempt,
        qualification_snapshot_digest=snapshot.snapshot_digest,
        qualification_decision_digest=decision.decision_digest,
        observations=observations,
    )
    _write_output(arguments.output, bundle.to_document())
    _record_outputs(
        arguments.github_output,
        role="observation-set",
        digest=bundle.set_digest,
    )
    return 0


def _load_observation_set(
    arguments: argparse.Namespace,
    snapshot: QualificationSnapshot,
    decision: QualificationDecision,
) -> SimulationObservationSet:
    content = _verify_uploaded_payload(
        arguments.observation_set,
        artifact_id=arguments.observation_set_artifact_id,
        artifact_digest=arguments.observation_set_artifact_digest,
    )
    return simulation_observation_set_from_bytes(
        content,
        snapshot=snapshot,
        decision=decision,
        expected_digest=arguments.observation_set_digest,
    )


def _release_materialize_actions_command(
    arguments: argparse.Namespace,
) -> int:
    snapshot = _load_qualification_snapshot(arguments)
    decision = _load_qualification_decision(arguments)
    observations = _load_observation_set(
        arguments,
        snapshot,
        decision,
    )
    if not isinstance(snapshot.subject, SimulationBinding):
        raise TypeError("Hypothetical actions require simulation Snapshot")
    actions: tuple[HypotheticalAction, ...] = ()
    if decision.terminal_result == "success" and observations.observations:
        classifications = {
            observation.value.classification
            for observation in observations.observations
        }
        if classifications <= {"absent", "exact-satisfied"}:
            artifact = _load_release_artifact_record(arguments)
            actions = materialize_hypothetical_actions(
                snapshot,
                decision,
                observations.observations,
                (artifact,),
            )
    report = HypotheticalActionsReport(
        simulation=snapshot.subject.simulation,
        purpose=snapshot.subject.purpose,
        target=snapshot.target,
        producer=HYPOTHETICAL_ACTIONS_REPORT_PRODUCER,
        workflow_run_id=snapshot.subject.simulation.workflow_run_id,
        run_attempt=snapshot.subject.simulation.run_attempt,
        qualification_snapshot_digest=snapshot.snapshot_digest,
        qualification_decision_digest=decision.decision_digest,
        observation_set_digest=observations.set_digest,
        observation_digests=observations.observation_digests,
        actions=actions,
        publication_snapshot_emitted=False,
    )
    _write_output(arguments.output, report.to_document())
    _record_outputs(
        arguments.github_output,
        role="hypothetical-actions-report",
        digest=report.report_digest,
    )
    return 0


def _release_finalize_simulation_command(
    arguments: argparse.Namespace,
) -> int:
    snapshot = _load_qualification_snapshot(arguments)
    decision = _load_qualification_decision(arguments)
    observations = _load_observation_set(
        arguments,
        snapshot,
        decision,
    )
    actions_content = _verify_uploaded_payload(
        arguments.actions_report,
        artifact_id=arguments.actions_report_artifact_id,
        artifact_digest=arguments.actions_report_artifact_digest,
    )
    actions_report = hypothetical_actions_report_from_bytes(
        actions_content,
        snapshot=snapshot,
        decision=decision,
        observations=observations,
        expected_digest=arguments.actions_report_digest,
    )
    artifacts: tuple[ReleaseArtifact, ...] = ()
    if arguments.release_artifact is not None:
        artifacts = (_load_release_artifact_record(arguments),)
    outcome = finalize_simulation(
        snapshot,
        decision,
        observations=observations.observations,
        artifacts=artifacts,
    )
    if actions_report.actions != outcome.hypothetical_actions:
        raise ValueError("Hypothetical actions report substitution mismatch")
    _write_output(arguments.output, outcome.to_document())
    summary = render_simulation_summary(
        snapshot,
        decision,
        outcome.to_document(),
    )
    summary_path = Path(arguments.summary_output)
    summary_path.parent.mkdir(parents=True, exist_ok=True)
    summary_path.write_text(summary, encoding="utf-8", newline="\n")
    if arguments.github_step_summary is not None:
        with Path(arguments.github_step_summary).open(
            "a",
            encoding="utf-8",
            newline="\n",
        ) as github_summary:
            github_summary.write(summary)
    _record_outputs(
        arguments.github_output,
        role="simulation-outcome",
        digest=outcome.outcome_digest,
        extra=(
            ("terminal-result", outcome.terminal_result),
            ("failure-class", outcome.failure_class),
            ("next-action", outcome.next_action),
        ),
    )
    return 0 if outcome.terminal_result == "success" else 1


def _release_normalize_live_request_command(
    arguments: argparse.Namespace,
) -> int:
    intent = normalize_buddy_live_intent(
        repository=arguments.repository,
        selected_ref=arguments.selected_ref,
        target=arguments.target,
        actor=arguments.actor,
        workflow_run_id=arguments.workflow_run_id,
        run_attempt=arguments.run_attempt,
    )
    _write_output(arguments.output, intent.to_document())
    _record_outputs(
        arguments.github_output,
        role="intent",
        digest=intent.intent_digest,
        extra=(("request-id", intent.request_id),),
    )
    return 0


def _release_compile_live_model_command(arguments: argparse.Namespace) -> int:
    intent = _load_live_intent(arguments)
    repo_root = Path(arguments.repo_root).resolve()
    context = _live_model_context(intent)
    manifest = first_slice_provider_manifest(
        context,
        provider_producer="discover-node",
    )
    _verify_uploaded_payload(
        arguments.provider_result,
        artifact_id=arguments.provider_artifact_id,
        artifact_digest=arguments.provider_artifact_digest,
    )
    result = _load_node_provider_result(
        arguments.provider_result,
        expected_binding=provider_binding(manifest, "node-first-slice"),
        expected_manifest_digest=manifest.manifest_digest,
    )
    request_transport_digest = _normalized_digest(
        arguments.intent_artifact_digest
    )
    provider_transport_digest = _normalized_digest(
        arguments.provider_artifact_digest
    )
    bundle = create_node_provider_fact_bundle(
        result,
        manifest_digest=manifest.manifest_digest,
        manifest_entry_id=manifest.requests[0].entry_id,
        request_artifact_id=arguments.intent_artifact_id,
        request_artifact_digest=request_transport_digest,
        transport_id=arguments.provider_artifact_id,
        transport_digest=provider_transport_digest,
    )
    admitted = admit_node_provider_fact_bundle(
        bundle,
        context=context,
        manifest=manifest,
        admission=FactBundleAdmissionContext(
            request_artifact_id=arguments.intent_artifact_id,
            request_artifact_digest=request_transport_digest,
            transport_id=arguments.provider_artifact_id,
            transport_digest=provider_transport_digest,
            bundle_digest=bundle.bundle_digest,
        ),
    )
    snapshot = compile_repository_model(
        repo_root,
        context,
        manifest,
        [admitted],
    )
    admitted_model = admit_repository_model_snapshot(
        canonicalize(snapshot.to_document()),
        expected_context=context,
        expected_digest=snapshot.snapshot_digest,
    )
    _write_output(arguments.output, admitted_model.snapshot.to_document())
    _record_outputs(
        arguments.github_output,
        role="repository-model",
        digest=admitted_model.canonical_digest,
        extra=(
            (
                "execution-concurrency-key",
                canonical_sha256(
                    derive_buddy_execution_identity(intent).to_document()
                ).removeprefix("sha256:"),
            ),
        ),
    )
    return 0


def _consumer_policy_from_file(  # noqa: C901
    path: str,
) -> ConsumerPolicyResult:
    try:
        document = json.loads(Path(path).read_bytes())
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as error:
        raise ValueError("Consumer policy result is unreadable") from error
    if type(document) is not dict:
        raise TypeError("Consumer policy result must be an object")
    result_marker = document.pop("result", None)
    if result_marker not in {None, "clean", "consumer"}:
        raise ValueError("Consumer policy result marker is invalid")
    if document.get("schema") != "workflow-delivery/v3/consumer-policy-result":
        raise ValueError("Consumer policy result has the wrong schema")

    def surfaces(name: str) -> tuple[SurfaceDigest, ...]:
        value = document.get(name)
        if type(value) is not list:
            raise TypeError(f"Consumer policy {name} must be an array")
        parsed: list[SurfaceDigest] = []
        for item in value:
            if (
                type(item) is not dict
                or type(item.get("path")) is not str
                or type(item.get("content-digest")) is not str
            ):
                raise TypeError(f"Consumer policy {name} entry is malformed")
            parsed.append(
                SurfaceDigest(
                    path=cast("str", item["path"]),
                    content_digest=cast("str", item["content-digest"]),
                )
            )
        return tuple(parsed)

    consumers = document.get("consumers")
    if type(consumers) is not list or any(
        type(item) is not str for item in consumers
    ):
        raise TypeError("Consumer policy consumers must be strings")
    result = ConsumerPolicyResult(
        policy_id=cast("str", document.get("policy-id")),
        policy_digest=cast("str", document.get("policy-digest")),
        target=cast("str", document.get("target")),
        scanned_surfaces=surfaces("scanned-surfaces"),
        admitted_exceptions=surfaces("admitted-exceptions"),
        consumers=tuple(cast("list[str]", consumers)),
    )
    if result.policy_id != CONSUMER_POLICY_ID:
        raise ValueError("Consumer policy result is not the permanent policy")
    validate_consumer_policy_result(result)
    return result


def _release_evaluate_live_eligibility_command(
    arguments: argparse.Namespace,
) -> int:
    intent = _load_live_intent(arguments)
    model = _load_live_model(arguments, intent)
    _descriptor, _quality, policy = load_first_slice_authoring(
        Path(arguments.repo_root).resolve(),
        arguments.target,
    )
    consumer_policy = _consumer_policy_from_file(arguments.consumer_policy)
    client = GitHubRestClient(
        repository=policy.governance.repository,
        token=arguments.github_token,
    )
    context = LiveEligibilityContext(
        purpose="live-release",
        request_id=intent.request_id,
        workflow_run_id=arguments.workflow_run_id,
        run_attempt=arguments.run_attempt,
        selected_ref=intent.selected_ref,
        target=arguments.target,
        repository_model_digest=model.canonical_digest,
        producer="evaluate-live-eligibility",
        control=model.snapshot.context.control,
        release_policy_digest=release_policy_digest(policy),
        catalog_digest=catalog_digest(),
    )
    decision = evaluate_live_eligibility(
        context,
        model.snapshot,
        consumer_policy,
        policy,
        client,
        now=datetime.now(UTC),
    )
    _write_output(arguments.output, decision.to_document())
    _record_outputs(
        arguments.github_output,
        role="live-eligibility",
        digest=decision.decision_digest,
        extra=(
            (
                "live-result",
                "admitted" if decision.result == "pass" else "blocked",
            ),
        ),
    )
    return 0 if decision.result == "pass" else 1


def _release_discover_history_command(arguments: argparse.Namespace) -> int:
    intent = _load_live_intent(arguments)
    execution = derive_buddy_execution_identity(intent)
    client = GitHubRestClient(
        repository=arguments.repository,
        token=arguments.github_token,
        workflow_path=arguments.workflow_path,
    )
    snapshot = discover_execution_history(
        client=client,
        execution=execution,
        request_id=intent.request_id,
        current_workflow_run_id=arguments.workflow_run_id,
        current_run_attempt=arguments.run_attempt,
    )
    _write_output(arguments.output, snapshot.to_document())
    _record_outputs(
        arguments.github_output,
        role="history-snapshot",
        digest=snapshot.snapshot_digest,
    )
    return 0


def _history_snapshot_from_file(
    arguments: argparse.Namespace,
    intent: ReleaseIntent,
) -> ExecutionHistoryAdmissionSnapshot:
    content = _verify_uploaded_payload(
        arguments.history_snapshot,
        artifact_id=arguments.history_snapshot_artifact_id,
        artifact_digest=arguments.history_snapshot_artifact_digest,
    )
    admitted = admit_release_record(
        content,
        expected_type=ExecutionHistoryAdmissionSnapshot,
        expected_digest=arguments.history_snapshot_digest,
        expected_bindings=_release_bindings(
            arguments,
            purpose="live-release",
            request_id=intent.request_id,
            execution=derive_buddy_execution_identity(intent),
        ),
    )
    if type(admitted) is not ExecutionHistoryAdmissionSnapshot:
        raise TypeError("History Snapshot admission returned the wrong type")
    return admitted


def _admitted_live_eligibility_decision(
    arguments: argparse.Namespace,
    intent: ReleaseIntent,
    model: AdmittedRepositoryModelSnapshot,
    *,
    admission_mode: LiveEligibilityAdmissionMode,
) -> tuple[AdmittedLiveEligibilityDecision, ReleasePolicy]:
    content = _verify_uploaded_payload(
        arguments.live_eligibility_decision,
        artifact_id=arguments.live_eligibility_artifact_id,
        artifact_digest=arguments.live_eligibility_artifact_digest,
    )
    _descriptor, _quality, policy = load_first_slice_authoring(
        Path(arguments.repo_root).resolve(),
        arguments.target,
    )
    admitted = admit_live_eligibility_decision(
        content,
        intent=intent,
        repository_model=model,
        policy=policy,
        expected_digest=_normalized_digest(
            arguments.live_eligibility_payload_digest
        ),
        admission_mode=admission_mode,
        now=datetime.now(UTC),
    )
    return admitted, policy


def _release_bind_live_attempt_command(arguments: argparse.Namespace) -> int:
    intent = _load_live_intent(arguments)
    model = _load_live_model(arguments, intent)
    eligibility, _policy = _admitted_live_eligibility_decision(
        arguments,
        intent,
        model,
        admission_mode=LiveEligibilityAdmissionMode.CURRENT_FRESHNESS,
    )
    history_snapshot = _history_snapshot_from_file(arguments, intent)
    binding = derive_release_attempt_binding(
        intent=intent,
        execution=derive_buddy_execution_identity(intent),
        repository_model_digest=arguments.repository_model_digest,
        live_eligibility_artifact_id=arguments.live_eligibility_artifact_id,
        live_eligibility_artifact_digest=_normalized_digest(
            arguments.live_eligibility_artifact_digest
        ),
        live_eligibility_payload_digest=_normalized_digest(
            arguments.live_eligibility_payload_digest
        ),
        attestation_provenance=eligibility.governance.provenance,
        history_snapshot=history_snapshot,
        history_snapshot_artifact_id=arguments.history_snapshot_artifact_id,
        history_snapshot_artifact_digest=_normalized_digest(
            arguments.history_snapshot_artifact_digest
        ),
    )
    _write_output(arguments.output, binding.to_document())
    _record_outputs(
        arguments.github_output,
        role="attempt-binding",
        digest=binding.binding_digest,
        extra=(("attempt-run-attempt", binding.attempt.run_attempt),),
    )
    return 0


def _release_admit_history_command(arguments: argparse.Namespace) -> int:
    intent = _load_live_intent(arguments)
    snapshot = _history_snapshot_from_file(arguments, intent)
    _write_output(arguments.output, snapshot.to_document())
    _record_outputs(
        arguments.github_output,
        role="history-snapshot",
        digest=snapshot.snapshot_digest,
    )
    return 0


def _release_admit_live_eligibility_command(
    arguments: argparse.Namespace,
) -> int:
    intent = _load_live_intent(arguments)
    model = _load_live_model(arguments, intent)
    decision, _policy = _admitted_live_eligibility_decision(
        arguments,
        intent,
        model,
        admission_mode=LiveEligibilityAdmissionMode.CURRENT_FRESHNESS,
    )
    _write_output(arguments.output, decision.to_document())
    _record_outputs(
        arguments.github_output,
        role="live-eligibility-decision",
        digest=decision.decision_digest,
    )
    return 0


def _release_admit_live_attempt_command(arguments: argparse.Namespace) -> int:
    _load_attempt_binding(arguments)
    return 0


def _release_materialize_publication_command(
    arguments: argparse.Namespace,
) -> int:
    snapshot = _load_live_qualification_snapshot(arguments)
    decision = _load_live_qualification_decision(arguments)
    artifact = _load_live_release_artifact_record(arguments)
    observation = cast(
        "ProjectionObservation",
        _load_release_record(
            arguments.observation,
            record_type=ProjectionObservation,
            expected_digest=arguments.observation_digest,
            artifact_id=arguments.observation_artifact_id,
            artifact_digest=arguments.observation_artifact_digest,
            bindings=_release_bindings(
                arguments,
                producer="observe-github-packages",
                purpose="live-release",
            ),
        ),
    )
    publication = materialize_publication_snapshot(
        snapshot,
        decision,
        (observation,),
        (artifact,),
    )
    snapshot_bytes = canonicalize(publication.to_document())
    snapshot_digest = publication.snapshot_digest
    markdown = (
        "# Workflow Delivery v3 Buddy publication review\n\n"
        f"- Publication Snapshot digest: `{snapshot_digest}`\n"
        f"- Target: `{publication.attempt.execution.target}`\n"
        f"- Workflow run: `{publication.attempt.workflow_run_id}`\n"
        f"- Run attempt: `{publication.attempt.run_attempt}`\n"
        f"- Materialized actions: `{len(publication.materialized_actions)}`\n"
    ).encode()
    reviewer = materialize_reviewer_payload(
        snapshot_bytes=snapshot_bytes,
        summary_bytes=markdown,
    )
    Path(arguments.output).write_bytes(snapshot_bytes)
    Path(arguments.summary_output).write_bytes(reviewer.summary_bytes)
    _write_output(arguments.formatter_input_output, reviewer.to_document())
    _record_outputs(
        arguments.github_output,
        role="publication-snapshot",
        digest=snapshot_digest,
        extra=(
            (
                "publication-snapshot-payload-digest",
                reviewer.snapshot_payload_digest,
            ),
            ("reviewer-digest", reviewer.summary_payload_digest),
            (
                "publish-required",
                str(bool(publication.materialized_actions)).lower(),
            ),
            (
                "resource-concurrency-key",
                publication.materialized_actions[0].lock_group
                if publication.materialized_actions
                else "no-op",
            ),
        ),
    )
    return 0


def _reviewer_payload_from_value(
    document: dict[str, JsonValue],
) -> ReviewerPayload:
    if (
        set(document)
        != {
            "schema",
            "snapshot-base64",
            "summary-base64",
            "snapshot-payload-digest",
            "summary-payload-digest",
        }
        or document["schema"] != "workflow-delivery/v3/reviewer-formatter-input"
    ):
        raise ValueError("reviewer formatter input has the wrong schema")
    try:
        snapshot_bytes = base64.b64decode(
            _string(document["snapshot-base64"], context="snapshot-base64"),
            validate=True,
        )
        summary_bytes = base64.b64decode(
            _string(document["summary-base64"], context="summary-base64"),
            validate=True,
        )
    except ValueError as error:
        raise ValueError(
            "reviewer formatter input base64 is malformed"
        ) from error
    payload = materialize_reviewer_payload(
        snapshot_bytes=snapshot_bytes,
        summary_bytes=summary_bytes,
    )
    if (
        payload.snapshot_payload_digest != document["snapshot-payload-digest"]
        or payload.summary_payload_digest != document["summary-payload-digest"]
    ):
        raise ValueError("reviewer formatter input digest mismatch")
    return payload


def _reviewer_payload_from_document(path: str) -> ReviewerPayload:
    return _reviewer_payload_from_value(
        _object(
            parse_canonical_json(Path(path).read_bytes()),
            context="reviewer formatter input",
        )
    )


def _release_bind_reviewer_artifact_command(
    arguments: argparse.Namespace,
) -> int:
    payload = _reviewer_payload_from_document(arguments.formatter_input)
    if (
        Path(arguments.publication_snapshot).read_bytes()
        != payload.snapshot_bytes
    ):
        raise ValueError("reviewer Publication Snapshot bytes mismatch")
    if Path(arguments.reviewer_summary).read_bytes() != payload.summary_bytes:
        raise ValueError("reviewer Markdown bytes mismatch")
    reviewer = bind_reviewer_artifact(
        payload=payload,
        artifact_id=arguments.reviewer_artifact_id,
        upload_digest=_normalized_digest(arguments.reviewer_artifact_digest),
        snapshot_payload_digest=arguments.snapshot_payload_digest,
        summary_payload_digest=arguments.summary_payload_digest,
    )
    document = payload.to_document()
    document.update(
        {
            "schema": "workflow-delivery/v3/bound-reviewer-formatter-input",
            "reviewer-artifact-id": reviewer.artifact_id,
            "reviewer-artifact-digest": reviewer.upload_digest,
        }
    )
    _write_output(arguments.output, cast("dict[str, JsonValue]", document))
    _record_outputs(
        arguments.github_output,
        role="bound-reviewer",
        digest=canonical_sha256(cast("dict[str, JsonValue]", document)),
    )
    return 0


def _bound_reviewer_artifact(
    path: str,
) -> tuple[ReviewerArtifact, ReviewerPayload]:
    document = _object(
        parse_canonical_json(Path(path).read_bytes()),
        context="bound reviewer formatter input",
    )
    if (
        document.get("schema")
        != "workflow-delivery/v3/bound-reviewer-formatter-input"
    ):
        raise ValueError("bound reviewer formatter input has the wrong schema")
    base = dict(document)
    artifact_id = _integer(
        base.pop("reviewer-artifact-id"),
        context="reviewer-artifact-id",
    )
    artifact_digest = _string(
        base.pop("reviewer-artifact-digest"),
        context="reviewer-artifact-digest",
    )
    base["schema"] = "workflow-delivery/v3/reviewer-formatter-input"
    payload = _reviewer_payload_from_value(cast("dict[str, JsonValue]", base))
    return bind_reviewer_artifact(
        payload=payload,
        artifact_id=artifact_id,
        upload_digest=_normalized_digest(artifact_digest),
        snapshot_payload_digest=payload.snapshot_payload_digest,
        summary_payload_digest=payload.summary_payload_digest,
    ), payload


def _load_publication_snapshot(
    arguments: argparse.Namespace,
) -> PublicationSnapshot:
    return cast(
        "PublicationSnapshot",
        _load_release_record(
            arguments.publication_snapshot,
            record_type=PublicationSnapshot,
            expected_digest=arguments.publication_snapshot_digest,
            artifact_id=arguments.publication_snapshot_artifact_id,
            artifact_digest=arguments.publication_snapshot_artifact_digest,
            bindings=_release_bindings(arguments, purpose="live-release"),
        ),
    )


def _load_authorization(arguments: argparse.Namespace) -> AuthorizationRecord:
    return cast(
        "AuthorizationRecord",
        _load_release_record(
            arguments.authorization,
            record_type=AuthorizationRecord,
            expected_digest=arguments.authorization_digest,
            artifact_id=arguments.authorization_artifact_id,
            artifact_digest=arguments.authorization_artifact_digest,
            bindings=_release_bindings(
                arguments,
                producer="approval",
                purpose="live-release",
            ),
        ),
    )


def _release_form_authorization_command(arguments: argparse.Namespace) -> int:
    reviewer, payload = _bound_reviewer_artifact(arguments.formatter_input)
    publication = cast(
        "PublicationSnapshot",
        admit_release_record(
            payload.snapshot_bytes,
            expected_type=PublicationSnapshot,
            expected_digest=payload.snapshot_payload_digest,
            expected_bindings=_release_bindings(
                arguments,
                purpose="live-release",
            ),
        ),
    )
    authorization = form_authorization_record(
        approval_result=arguments.approval_result,
        attempt=publication.attempt,
        publication_snapshot=publication,
        reviewer_artifact=reviewer,
        approval_job_id=arguments.approval_job_id,
        completed_at=arguments.completed_at,
        control=arguments.control,
    )
    _write_output(arguments.output, authorization.to_document())
    _record_outputs(
        arguments.github_output,
        role="authorization",
        digest=authorization.authorization_digest,
    )
    return 0


def _release_admit_capability_command(arguments: argparse.Namespace) -> int:
    intent = _load_live_intent(arguments)
    model = _load_live_model(arguments, intent)
    initial_eligibility, policy = _admitted_live_eligibility_decision(
        arguments,
        intent,
        model,
        admission_mode=LiveEligibilityAdmissionMode.CAPABILITY_REPLAY,
    )
    publication = _load_publication_snapshot(arguments)
    authorization = _load_authorization(arguments)
    reviewer = materialize_reviewer_artifact(
        snapshot_bytes=Path(arguments.publication_snapshot).read_bytes(),
        summary_bytes=Path(arguments.reviewer_summary).read_bytes(),
        artifact_id=arguments.reviewer_summary_artifact_id,
        upload_digest=_normalized_digest(
            arguments.reviewer_summary_artifact_digest
        ),
    )
    observed_at = datetime.now(UTC)
    fresh = observe_governance_source(
        policy.governance,
        GitHubRestClient(
            repository=policy.governance.repository,
            token=arguments.github_token,
        ),
        now=observed_at,
    )
    fresh_provenance = governance_observation_provenance(fresh)
    initial_provenance = initial_eligibility.governance.provenance
    initial_content_sha256 = initial_eligibility.governance.content_sha256
    decision = admit_live_capability(
        attempt=publication.attempt,
        authorization=authorization,
        publication_snapshot=publication,
        reviewer_artifact=reviewer,
        live_eligibility_artifact_id=arguments.live_eligibility_artifact_id,
        live_eligibility_artifact_digest=_normalized_digest(
            arguments.live_eligibility_artifact_digest
        ),
        governance_provenance=fresh_provenance,
        governance_content_sha256=fresh.content_sha256,
        governance_expires_at=fresh.attestation.expires_at.strftime(
            "%Y-%m-%dT%H:%M:%SZ"
        ),
        governance_live_enabled=fresh.attestation.live_enabled,
        governance_observed_at=observed_at,
        expected_governance_provenance=initial_provenance,
        expected_governance_content_sha256=initial_content_sha256,
        expected_governance_expires_at=(
            initial_eligibility.governance.expires_at.strftime(
                "%Y-%m-%dT%H:%M:%SZ"
            )
        ),
        expected_governance_live_enabled=(
            initial_eligibility.governance.live_enabled
        ),
        control=arguments.control,
    )
    if not isinstance(decision, CapabilityAdmissionDecision):
        raise TypeError("Capability admission returned scenario test result")
    _write_output(arguments.output, decision.to_document())
    _record_outputs(
        arguments.github_output,
        role="capability-decision",
        digest=decision.decision_digest,
        extra=(("capability-result", decision.result),),
    )
    return 0 if decision.authorizing else 1


def _load_capability_decision(
    arguments: argparse.Namespace,
) -> CapabilityAdmissionDecision:
    return cast(
        "CapabilityAdmissionDecision",
        _load_release_record(
            arguments.capability_decision,
            record_type=CapabilityAdmissionDecision,
            expected_digest=arguments.capability_decision_digest,
            artifact_id=arguments.capability_decision_artifact_id,
            artifact_digest=arguments.capability_decision_artifact_digest,
            bindings=_release_bindings(
                arguments,
                producer="approval-finalizer",
                purpose="live-release",
            ),
        ),
    )


def _release_observe_github_packages_command(
    arguments: argparse.Namespace,
) -> int:
    snapshot = _load_live_qualification_snapshot(arguments)
    decision = _load_live_qualification_decision(arguments)
    context = _load_release_adapter_context(arguments, snapshot)
    artifact = _load_live_release_artifact_record(arguments)
    observation = observe_github_packages_projection(
        snapshot,
        decision,
        artifact,
        artifact_expectation(snapshot, context, artifact),
        token=arguments.github_token,
        transport=GitHubPackagesHttpTransport(),
    )
    _write_output(arguments.output, observation.to_document())
    _record_outputs(
        arguments.github_output,
        role="observation",
        digest=observation.observation_digest,
    )
    if observation.value.classification in {"absent", "exact-satisfied"}:
        return 0
    return 1


def _release_publish_github_packages_command(
    arguments: argparse.Namespace,
) -> int:
    publication = _load_publication_snapshot(arguments)
    capability = _load_capability_decision(arguments)
    if not capability.authorizing:
        raise ValueError("Capability Admission Decision is not authorizing")
    preflight = _load_github_packages_preflight(
        arguments.preflight,
        expected_digest=arguments.preflight_digest,
        publication=publication,
    )
    marker = _load_mutation_marker(
        arguments.mutation_marker,
        expected_digest=arguments.mutation_marker_digest,
        artifact_id=arguments.mutation_marker_artifact_id,
        artifact_digest=arguments.mutation_marker_artifact_digest,
        publication=publication,
        preflight=preflight,
    )
    snapshot = _load_live_qualification_snapshot(arguments)
    decision = _load_live_qualification_decision(arguments)
    context = _load_release_adapter_context(arguments, snapshot)
    artifact = _load_live_release_artifact_record(arguments)
    authorization = _load_authorization(arguments)
    _descriptor, _quality, policy = load_first_slice_authoring(
        Path(arguments.repo_root).resolve(),
        arguments.target,
    )
    try:
        result = publish_github_packages_action(
            tarball=Path(arguments.tarball),
            target=publication.attempt.execution.target,
            token=arguments.github_token,
            runner=_SubprocessPublishRunner(),
            temp_root=Path(arguments.temp_root),
            transport=GitHubPackagesHttpTransport(),
            publication_snapshot=publication,
            authorization=authorization,
            capability_decision=capability,
            action=publication.materialized_actions[0],
            qualification_snapshot=snapshot,
            qualification_decision=decision,
            artifact=artifact,
            expectation=artifact_expectation(snapshot, context, artifact),
            preflight=preflight,
            mutation_marker=marker,
            governance_source=policy.governance,
            governance_client=GitHubRestClient(
                repository=policy.governance.repository,
                token=arguments.github_token,
            ),
            governance_observed_at=lambda: datetime.now(UTC),
            defer_receipt_binding=True,
            checkout_root=Path(arguments.repo_root).resolve(),
        )
    except PublisherGovernanceRecheckRejectionError as error:
        result = error.result
    if not isinstance(result, DeferredPublicationExecutionResult):
        message = "GitHub Packages publisher returned a bound result"
        raise TypeError(message)
    if result.receipt is not None:
        _write_output(arguments.receipt_output, result.receipt.to_document())
    state: dict[str, JsonValue] = {
        "schema": "workflow-delivery/v3/deferred-publication-result",
        "action-id": publication.materialized_actions[0].action_id,
        "action-digest": publication.materialized_actions[0].action_digest,
        "lock-group": publication.materialized_actions[0].lock_group,
        "outcome": result.classification.outcome,
        "mutation-disposition": result.classification.mutation_disposition,
        "response-identity-digest": result.response_identity_digest,
        "receipt-digest": (
            None if result.receipt is None else result.receipt.receipt_digest
        ),
        "diagnostic-reference": result.diagnostic_reference,
        "control": capability.control,
    }
    _write_output(arguments.execution_state_output, state)
    _record_outputs(
        arguments.github_output,
        role="publication-execution",
        digest=canonical_sha256(state),
        extra=(
            (
                "receipt-digest",
                "" if result.receipt is None else result.receipt.receipt_digest,
            ),
        ),
    )
    return 0 if result.classification.outcome == "success" else 1


def _release_preflight_github_packages_command(
    arguments: argparse.Namespace,
) -> int:
    publication = _load_publication_snapshot(arguments)
    capability = _load_capability_decision(arguments)
    if not capability.authorizing:
        raise ValueError("Capability Admission Decision is not authorizing")
    _descriptor, _quality, policy = load_first_slice_authoring(
        Path(arguments.repo_root).resolve(),
        arguments.target,
    )
    observed_at = datetime.now(UTC)
    require_fresh_governance_identity(
        policy.governance,
        GitHubRestClient(
            repository=policy.governance.repository,
            token=arguments.github_token,
        ),
        now=observed_at,
        expected_provenance=capability.governance_provenance,
        expected_content_sha256=capability.governance_content_sha256,
        expected_expires_at=capability.governance_expires_at,
        expected_live_enabled=capability.governance_live_enabled,
    )
    if len(publication.materialized_actions) != 1:
        raise ValueError("GitHub Packages publisher requires one action")
    snapshot = _load_live_qualification_snapshot(arguments)
    decision = _load_live_qualification_decision(arguments)
    context = _load_release_adapter_context(arguments, snapshot)
    artifact = _load_live_release_artifact_record(arguments)
    authorization = _load_authorization(arguments)
    preflight = preflight_github_packages_action(
        tarball=Path(arguments.tarball),
        target=publication.attempt.execution.target,
        publication_snapshot=publication,
        authorization=authorization,
        capability_decision=capability,
        action=publication.materialized_actions[0],
        qualification_snapshot=snapshot,
        qualification_decision=decision,
        artifact=artifact,
        expectation=artifact_expectation(snapshot, context, artifact),
        governance_source=policy.governance,
        governance_client=GitHubRestClient(
            repository=policy.governance.repository,
            token=arguments.github_token,
        ),
        governance_observed_at=observed_at,
    )
    _write_output(arguments.preflight_output, preflight.to_document())
    _record_outputs(
        arguments.github_output,
        role="publication-preflight",
        digest=preflight.preflight_digest,
    )
    return 0


def _load_github_packages_preflight(
    path: str,
    *,
    expected_digest: str,
    publication: PublicationSnapshot,
) -> GitHubPackagesPublishPreflight:
    value = json.loads(Path(path).read_bytes())
    action = publication.materialized_actions[0]
    if (
        type(value) is not dict
        or value.get("schema")
        != "workflow-delivery/v3/github-packages-publish-preflight"
        or canonical_sha256(value) != expected_digest
        or value.get("attempt") != publication.attempt.to_document()
        or value.get("publication-snapshot-digest")
        != publication.snapshot_digest
        or value.get("action-digest") != action.action_digest
        or value.get("lock-group") != action.lock_group
    ):
        raise ValueError(
            "GitHub Packages preflight is malformed or substituted"
        )
    governance_value = value.get("governance-provenance")
    if type(governance_value) is not list or any(
        type(pair) is not list
        or len(pair) != _PAIR_FIELD_COUNT
        or any(type(item) is not str for item in pair)
        for pair in governance_value
    ):
        raise ValueError(
            "GitHub Packages preflight is malformed or substituted"
        )
    governance_provenance = tuple(
        (cast("str", pair[0]), cast("str", pair[1]))
        for pair in governance_value
    )
    preflight = GitHubPackagesPublishPreflight(
        attempt=publication.attempt,
        publication_snapshot_digest=publication.snapshot_digest,
        action_digest=action.action_digest,
        lock_group=action.lock_group,
        tarball_sha256=cast("str", value.get("tarball-sha256")),
        tarball_sha512=cast("str", value.get("tarball-sha512")),
        npm_configuration_digest=cast(
            "str",
            value.get("npm-configuration-digest"),
        ),
        governance_provenance=governance_provenance,
        governance_content_sha256=cast(
            "str",
            value.get("governance-content-sha256"),
        ),
        governance_expires_at=cast(
            "str",
            value.get("governance-expires-at"),
        ),
        governance_live_enabled=cast(
            "bool",
            value.get("governance-live-enabled"),
        ),
    )
    if preflight.to_document() != value:
        raise ValueError("GitHub Packages preflight is not canonical")
    return preflight


def _release_mark_github_packages_mutation_command(
    arguments: argparse.Namespace,
) -> int:
    publication = _load_publication_snapshot(arguments)
    preflight = _load_github_packages_preflight(
        arguments.preflight,
        expected_digest=arguments.preflight_digest,
        publication=publication,
    )
    marker = form_mutation_may_have_started_marker(preflight=preflight)
    _write_output(arguments.marker_output, marker.to_document())
    _record_outputs(
        arguments.github_output,
        role="mutation-may-have-started",
        digest=marker.marker_digest,
    )
    return 0


def _load_mutation_marker(  # noqa: PLR0913
    path: str,
    *,
    expected_digest: str,
    artifact_id: int,
    artifact_digest: str,
    publication: PublicationSnapshot,
    preflight: GitHubPackagesPublishPreflight,
) -> MutationMayHaveStartedMarker:
    if artifact_id <= 0:
        raise ValueError("mutation-start marker transport is malformed")
    try:
        _normalized_digest(artifact_digest)
    except ValueError as error:
        raise ValueError(
            "mutation-start marker transport is malformed"
        ) from error
    value = json.loads(Path(path).read_bytes())
    action = publication.materialized_actions[0]
    if (
        type(value) is not dict
        or value.get("schema")
        != ("workflow-delivery/v3/github-packages-mutation-may-have-started")
        or canonical_sha256(value) != expected_digest
        or value.get("attempt") != publication.attempt.to_document()
        or value.get("publication-snapshot-digest")
        != publication.snapshot_digest
        or value.get("action-digest") != action.action_digest
        or value.get("lock-group") != action.lock_group
        or value.get("preflight-digest") != preflight.preflight_digest
    ):
        raise ValueError("mutation-start marker is malformed or substituted")
    return MutationMayHaveStartedMarker(
        attempt=publication.attempt,
        publication_snapshot_digest=publication.snapshot_digest,
        action_digest=action.action_digest,
        lock_group=action.lock_group,
        preflight_digest=preflight.preflight_digest,
    )


def _release_form_github_packages_result_command(
    arguments: argparse.Namespace,
) -> int:
    publication = _load_publication_snapshot(arguments)
    action = publication.materialized_actions[0]
    marker_present = arguments.mutation_marker_artifact_id is not None
    state_value: dict[str, JsonValue] | None = None
    if arguments.execution_state is not None:
        try:
            loaded_state = json.loads(
                Path(arguments.execution_state).read_bytes()
            )
        except (OSError, UnicodeDecodeError, json.JSONDecodeError):
            loaded_state = None
        if (
            type(loaded_state) is dict
            and loaded_state.get("schema")
            == "workflow-delivery/v3/deferred-publication-result"
            and loaded_state.get("action-id") == action.action_id
            and loaded_state.get("action-digest") == action.action_digest
            and loaded_state.get("lock-group") == action.lock_group
        ):
            state_value = cast("dict[str, JsonValue]", loaded_state)
    if state_value is not None and (
        state_value.get("outcome") not in {"success", "failed", "incomplete"}
        or state_value.get("mutation-disposition")
        not in {
            "created",
            "exact-race-accepted",
            "no-side-effect",
            "possibly-mutated",
        }
        or type(state_value.get("control")) is not str
        or (
            state_value.get("response-identity-digest") is not None
            and type(state_value.get("response-identity-digest")) is not str
        )
        or (
            state_value.get("receipt-digest") is not None
            and type(state_value.get("receipt-digest")) is not str
        )
        or (
            marker_present
            and (
                (
                    state_value.get("mutation-disposition") == "no-side-effect"
                    and (
                        state_value.get("outcome") != "failed"
                        or state_value.get("diagnostic-reference")
                        not in {
                            "create-conflict",
                            (PUBLISHER_GOVERNANCE_RECHECK_FAILED_BEFORE_RUNNER),
                        }
                    )
                )
                or (
                    state_value.get("mutation-disposition")
                    in {"created", "exact-race-accepted"}
                    and state_value.get("outcome") != "success"
                )
                or (
                    state_value.get("mutation-disposition")
                    == "possibly-mutated"
                    and state_value.get("outcome") != "incomplete"
                )
            )
        )
    ):
        state_value = None
    if state_value is None:
        state_value = {
            "schema": "workflow-delivery/v3/deferred-publication-result",
            "action-id": action.action_id,
            "action-digest": action.action_digest,
            "lock-group": action.lock_group,
            "outcome": "incomplete" if marker_present else "failed",
            "mutation-disposition": (
                "possibly-mutated" if marker_present else "no-side-effect"
            ),
            "response-identity-digest": None,
            "receipt-digest": None,
            "diagnostic-reference": (
                "terminal-state-missing-or-malformed-after-start"
                if marker_present
                else "preflight-failed-before-mutation-start"
            ),
            "control": f"workflow-delivery-v3:{arguments.target}",
        }
    elif arguments.publish_step_outcome == "success" and (
        state_value.get("outcome") != "success"
    ):
        raise ValueError("Deferred publication step outcome mismatch")
    receipt = None
    if arguments.receipt is not None:
        receipt = cast(
            "Receipt",
            _load_release_record(
                arguments.receipt,
                record_type=Receipt,
                expected_digest=arguments.receipt_digest,
                artifact_id=arguments.receipt_artifact_id,
                artifact_digest=arguments.receipt_artifact_digest,
                bindings=_release_bindings(
                    arguments,
                    producer="publish-github-packages",
                    purpose="live-release",
                ),
            ),
        )
    state_receipt_digest = cast("str | None", state_value["receipt-digest"])
    if receipt is None and state_receipt_digest is not None and marker_present:
        state_value = {
            **state_value,
            "outcome": "incomplete",
            "mutation-disposition": "possibly-mutated",
            "response-identity-digest": None,
            "receipt-digest": None,
            "diagnostic-reference": "receipt-persistence-failed-after-start",
        }
        state_receipt_digest = None
    if (receipt is None) != (state_receipt_digest is None) or (
        receipt is not None
        and (
            receipt.receipt_digest != state_receipt_digest
            or receipt.action_id != action.action_id
        )
    ):
        raise ValueError("Deferred publication Receipt binding mismatch")
    result = ActionResult(
        attempt=publication.attempt,
        publication_snapshot_digest=publication.snapshot_digest,
        action_id=action.action_id,
        action_digest=action.action_digest,
        lock_group=action.lock_group,
        outcome=cast("str", state_value["outcome"]),
        mutation_disposition=cast(
            "str",
            state_value["mutation-disposition"],
        ),
        response_identity_digest=cast(
            "str | None",
            state_value["response-identity-digest"],
        ),
        receipt_artifact_id=(
            None if receipt is None else arguments.receipt_artifact_id
        ),
        receipt_artifact_name=(
            None
            if receipt is None
            else Path(_string(arguments.receipt, context="receipt")).name
        ),
        receipt_artifact_digest=(
            None if receipt is None else arguments.receipt_artifact_digest
        ),
        receipt_payload_digest=(
            None if receipt is None else receipt.receipt_digest
        ),
        receipt_digest=(None if receipt is None else receipt.receipt_digest),
        diagnostic_reference=cast(
            "str | None",
            state_value["diagnostic-reference"],
        ),
        producer="publish-github-packages",
        control=cast("str", state_value["control"]),
        workflow_run_id=publication.attempt.workflow_run_id,
        run_attempt=publication.attempt.run_attempt,
    )
    bundle = CapabilityGroupResultBundle(
        attempt=publication.attempt,
        publication_snapshot_digest=publication.snapshot_digest,
        capability_group=action.capability_group,
        planned_action_ids=(action.action_id,),
        action_results=(result,),
        completion_state=(
            "complete" if result.outcome == "success" else result.outcome
        ),
        producer="publish-github-packages",
        control=result.control,
        workflow_run_id=publication.attempt.workflow_run_id,
        run_attempt=publication.attempt.run_attempt,
    )
    _write_output(arguments.result_output, result.to_document())
    _write_output(arguments.bundle_output, bundle.to_document())
    _record_outputs(
        arguments.github_output,
        role="capability-group-bundle",
        digest=bundle.bundle_digest,
        extra=(
            ("action-result-digest", result.result_digest),
            (
                "receipt-digest",
                "" if receipt is None else receipt.receipt_digest,
            ),
        ),
    )
    return 0 if result.outcome == "success" else 1


def _release_finalize_live_command(arguments: argparse.Namespace) -> int:
    _validate_optional_uploaded_record_transport(
        "publication_snapshot",
        path=arguments.publication_snapshot,
        record_digest=arguments.publication_snapshot_digest,
        artifact_id=arguments.publication_snapshot_artifact_id,
        artifact_digest=arguments.publication_snapshot_artifact_digest,
    )
    _validate_optional_uploaded_record_transport(
        "authorization",
        path=arguments.authorization,
        record_digest=arguments.authorization_digest,
        artifact_id=arguments.authorization_artifact_id,
        artifact_digest=arguments.authorization_artifact_digest,
    )
    _validate_optional_uploaded_record_transport(
        "capability_decision",
        path=arguments.capability_decision,
        record_digest=arguments.capability_decision_digest,
        artifact_id=arguments.capability_decision_artifact_id,
        artifact_digest=arguments.capability_decision_artifact_digest,
    )
    _validate_optional_uploaded_record_transport(
        "capability_group_bundle",
        path=arguments.capability_group_bundle,
        record_digest=arguments.capability_group_bundle_digest,
        artifact_id=arguments.capability_group_bundle_artifact_id,
        artifact_digest=arguments.capability_group_bundle_artifact_digest,
    )
    _validate_optional_uploaded_record_transport(
        "receipt",
        path=arguments.receipt,
        record_digest=arguments.receipt_digest,
        artifact_id=arguments.receipt_artifact_id,
        artifact_digest=arguments.receipt_artifact_digest,
    )
    binding = _load_attempt_binding(arguments)
    snapshot = _load_live_qualification_snapshot(arguments)
    decision = _load_live_qualification_decision(arguments)
    attempt = binding.attempt
    if type(decision.subject) is not ReleaseAttemptIdentity:
        message = "Live finalization Decision has the wrong subject"
        raise TypeError(message)
    if (
        snapshot.subject != attempt
        or decision.subject != attempt
        or decision.qualification_snapshot_digest != snapshot.snapshot_digest
        or snapshot.repository_model_digest != binding.repository_model_digest
    ):
        message = "Live finalization Attempt authority binding mismatch"
        raise ValueError(message)
    qualification_evidence = tuple(
        item
        for item in (
            _optional_evidence(
                arguments,
                prefix="build-evidence",
                producer="build-tarball",
                purpose="live-release",
            ),
            _optional_evidence(
                arguments,
                prefix="project-test-evidence",
                producer="project-test",
                purpose="live-release",
            ),
            _optional_evidence(
                arguments,
                prefix="artifact-contents-evidence",
                producer="npm-artifact-qualification",
                purpose="live-release",
            ),
            _optional_evidence(
                arguments,
                prefix="install-import-evidence",
                producer="npm-artifact-qualification",
                purpose="live-release",
            ),
        )
        if item is not None
    )
    qualification_artifacts = (
        ()
        if arguments.release_artifact is None
        else (_load_live_release_artifact_record(arguments),)
    )
    if (
        finalize_qualification(
            snapshot,
            qualification_evidence,
            qualification_artifacts,
        )
        != decision
    ):
        message = "Live finalization Qualification Decision is not exact"
        raise ValueError(message)
    publication = (
        None
        if arguments.publication_snapshot is None
        else _load_publication_snapshot(arguments)
    )
    authorization = (
        None
        if arguments.authorization is None
        else _load_authorization(arguments)
    )
    capability_decision = None
    if arguments.capability_decision is not None:
        capability_decision = _load_capability_decision(arguments)
    group_bundle = None
    if arguments.capability_group_bundle is not None:
        group_bundle = cast(
            "CapabilityGroupResultBundle",
            _load_release_record(
                arguments.capability_group_bundle,
                record_type=CapabilityGroupResultBundle,
                expected_digest=arguments.capability_group_bundle_digest,
                artifact_id=arguments.capability_group_bundle_artifact_id,
                artifact_digest=arguments.capability_group_bundle_artifact_digest,
                bindings=_release_bindings(
                    arguments,
                    producer="publish-github-packages",
                    purpose="live-release",
                ),
            ),
        )
    receipt = None
    if arguments.receipt is not None:
        receipt = cast(
            "Receipt",
            _load_release_record(
                arguments.receipt,
                record_type=Receipt,
                expected_digest=arguments.receipt_digest,
                artifact_id=arguments.receipt_artifact_id,
                artifact_digest=arguments.receipt_artifact_digest,
                bindings=_release_bindings(
                    arguments,
                    producer="publish-github-packages",
                    purpose="live-release",
                ),
            ),
        )
    outcome = finalize_attempt_outcome(
        attempt=attempt,
        qualification_decision=decision,
        publication_snapshot=publication,
        authorization=authorization,
        capability_decisions=()
        if capability_decision is None
        else (capability_decision,),
        group_bundles=() if group_bundle is None else (group_bundle,),
        receipts=() if receipt is None else (receipt,),
        receipt_transport_references=()
        if receipt is None
        else (
            ReceiptTransportReference(
                action_id=receipt.action_id,
                artifact_id=arguments.receipt_artifact_id,
                artifact_name=Path(
                    _string(arguments.receipt, context="receipt")
                ).name,
                upload_digest=_normalized_digest(
                    arguments.receipt_artifact_digest
                ),
                payload_digest=receipt.receipt_digest,
            ),
        ),
        publication_preparation_interrupted=(
            arguments.publication_preparation_interrupted
        ),
        platform_terminated=arguments.platform_terminated,
        capability_may_have_started=arguments.capability_may_have_started,
    )
    _write_output(arguments.outcome_output, outcome.to_document())
    summary = (
        "# Workflow Delivery v3 live finalization\n\n"
        f"- Result: `{outcome.result}`\n"
        f"- Terminal phase: `{outcome.terminal_phase}`\n"
        f"- Next action: `{outcome.next_action}`\n"
    )
    Path(arguments.summary_output).write_text(
        summary,
        encoding="utf-8",
        newline="\n",
    )
    if arguments.github_step_summary is not None:
        with Path(arguments.github_step_summary).open(
            "a",
            encoding="utf-8",
            newline="\n",
        ) as github_summary:
            github_summary.write(summary)
    _record_outputs(
        arguments.github_output,
        role="attempt-outcome",
        digest=outcome.outcome_digest,
        extra=(("terminal-result", outcome.result),),
    )
    return LIVE_OUTCOME_EXIT_STATUS[outcome.result]


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


def _git_commit_contains_path(
    repository_root: Path,
    commit: str,
    path: str,
) -> bool:
    if not isinstance(repository_root, Path) or not repository_root.is_dir():
        raise ValueError("bootstrap repository root is not a directory")
    if (
        type(commit) is not str
        or len(commit) != _TARGET_SHA_LENGTH
        or any(character not in _LOWER_HEX for character in commit)
    ):
        raise ValueError("bootstrap base commit must be lowercase 40-hex")
    if type(path) is not str or path != CI_WORKFLOW_PATH:
        raise ValueError("bootstrap marker path is not canonical")
    git = ("git", "-C", os.fspath(repository_root))
    subprocess.run(  # noqa: S603
        (*git, "cat-file", "-e", f"{commit}^{{commit}}"),
        check=True,
        capture_output=True,
        text=True,
    )
    result = subprocess.run(  # noqa: S603
        (
            *git,
            "ls-tree",
            "--full-tree",
            "--name-only",
            commit,
            "--",
            path,
        ),
        check=True,
        capture_output=True,
        text=True,
    )
    entries = tuple(line for line in result.stdout.splitlines() if line)
    if entries not in ((), (path,)):
        raise ValueError("bootstrap marker probe returned an inexact path")
    return entries == (path,)


def _ci_project_bootstrap_shadow_command(
    arguments: argparse.Namespace,
) -> int:
    plan = _load_ci_plan(arguments.plan, arguments.plan_digest)
    content, document = _read_object(
        arguments.decision,
        context="CI Slice Decision",
    )
    parsed = _ci_slice_decision_from_document(
        document,
        context="CI Slice Decision",
    )
    decision = admit_ci_slice_decision_json(
        content,
        expected_plan=plan,
        expected_evidence=(),
        expected_elapsed_seconds=parsed.elapsed_seconds,
        expected_supersession_state=parsed.supersession_state,
    )
    _, summary = _read_object(
        arguments.summary,
        context="CI Slice Summary",
    )
    if summary != decision.summary.to_document():
        raise ValueError("CI Slice Summary does not match the Decision")
    repository_root = Path(arguments.repo_root)
    base_contains_ci_workflow = _git_commit_contains_path(
        repository_root,
        arguments.base_sha,
        CI_WORKFLOW_PATH,
    )
    if not qualifies_precoexistence_bootstrap_projection(
        decision,
        request=CiBootstrapProjectionRequest(
            pull_request_number=arguments.pull_request_number,
            base_sha=arguments.base_sha,
            head_sha=arguments.head_sha,
            tested_merge_sha=arguments.tested_merge_sha,
        ),
        base_contains_ci_workflow=base_contains_ci_workflow,
    ):
        raise ValueError(
            "CI Decision is not eligible for the pre-coexistence bootstrap"
        )
    with Path(arguments.github_step_summary).open(
        "a",
        encoding="utf-8",
    ) as output:
        print("## Pre-coexistence bootstrap projection", file=output)
        print("", file=output)
        print(
            "The canonical Decision remains failure "
            "(`incomplete-model-plan`). Only this non-authoritative check "
            "conclusion is projected as success because the exact base commit "
            f"does not contain `{CI_WORKFLOW_PATH}`.",
            file=output,
        )
    return 0


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


def _add_current_release_arguments(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--workflow-run-id", required=True, type=int)
    parser.add_argument("--run-attempt", required=True, type=int)
    parser.add_argument("--target", required=True)


def _add_qualification_purpose_argument(
    parser: argparse.ArgumentParser,
) -> None:
    parser.add_argument(
        "--purpose",
        choices=("release-simulation", "live-release"),
        default="release-simulation",
    )


def _add_uploaded_record_arguments(
    parser: argparse.ArgumentParser,
    *,
    name: str,
    required: bool = True,
) -> None:
    option = name.replace("_", "-")
    parser.add_argument(f"--{option}", required=required)
    parser.add_argument(
        f"--{option}-digest",
        required=required,
    )
    parser.add_argument(
        f"--{option}-artifact-id",
        required=required,
        type=int,
    )
    parser.add_argument(
        f"--{option}-artifact-digest",
        required=required,
    )


def _add_snapshot_arguments(parser: argparse.ArgumentParser) -> None:
    _add_uploaded_record_arguments(
        parser,
        name="qualification_snapshot",
    )


def _add_adapter_context_arguments(parser: argparse.ArgumentParser) -> None:
    _add_uploaded_record_arguments(parser, name="adapter_context")


def _add_release_artifact_arguments(
    parser: argparse.ArgumentParser,
    *,
    required: bool = True,
) -> None:
    _add_uploaded_record_arguments(
        parser,
        name="release_artifact",
        required=required,
    )


def _add_decision_arguments(parser: argparse.ArgumentParser) -> None:
    _add_uploaded_record_arguments(
        parser,
        name="qualification_decision",
    )


def _add_observation_set_arguments(
    parser: argparse.ArgumentParser,
) -> None:
    _add_uploaded_record_arguments(
        parser,
        name="observation_set",
    )


def _add_optional_evidence_arguments(
    parser: argparse.ArgumentParser,
    *,
    name: str,
) -> None:
    _add_uploaded_record_arguments(parser, name=name, required=False)


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

    governance = commands.add_parser("governance")
    governance_commands = governance.add_subparsers(
        dest="governance_command",
        required=True,
    )
    reviewer_inspection = governance_commands.add_parser(
        "inspect-acceptance-reviewer",
        description=(
            "Optional on-demand read-only diagnostic inspection for one "
            "Governance acceptance reviewer recovery coordinate."
        ),
        help="optional on-demand read-only reviewer inspection",
    )
    reviewer_inspection.add_argument("--repository", required=True)
    reviewer_inspection.add_argument(
        "--workflow-run-id",
        required=True,
        type=int,
    )
    reviewer_inspection.add_argument("--environment", required=True)
    reviewer_inspection.add_argument("--deployment", required=True)
    reviewer_inspection.add_argument("--job", required=True)
    reviewer_inspection.add_argument("--artifact-id", required=True, type=int)
    reviewer_inspection.add_argument(
        "--timeout-seconds",
        default=10.0,
        type=float,
    )
    reviewer_inspection.add_argument(
        "--max-output-bytes",
        default=8192,
        type=int,
    )
    reviewer_inspection.add_argument("--output")
    reviewer_inspection.set_defaults(
        handler=_governance_inspect_acceptance_reviewer_command
    )
    admit_acceptance = governance_commands.add_parser(
        "admit-acceptance-evidence",
        description=(
            "Strictly admit canonical Governance destination-acceptance "
            "Evidence without creating Release lineage."
        ),
    )
    admit_acceptance.add_argument("--document", required=True)
    admit_acceptance.set_defaults(
        handler=_governance_admit_acceptance_evidence_command
    )
    run_acceptance_probe = governance_commands.add_parser(
        "run-fixed-acceptance-probe",
        description=(
            "Run one reviewed fixed-coordinate acceptance suite with bounded "
            "npm transport and output."
        ),
    )
    run_acceptance_probe.add_argument(
        "--suite",
        required=True,
        choices=(
            "absent-create-readback",
            "exact-and-conflict",
        ),
        action=_AcceptanceSuiteAction,
    )
    run_acceptance_probe.add_argument(
        "--package-coordinate",
        required=True,
    )
    run_acceptance_probe.add_argument("--target-sha", required=True)
    run_acceptance_probe.add_argument(
        "--timeout-seconds",
        default=None,
        type=float,
    )
    run_acceptance_probe.add_argument(
        "--max-response-bytes",
        default=8192,
        type=int,
    )
    run_acceptance_probe.add_argument(
        "--max-output-bytes",
        default=4096,
        type=int,
    )
    run_acceptance_probe.add_argument("--output", required=True)
    run_acceptance_probe.add_argument("--github-output")
    run_acceptance_probe.set_defaults(
        handler=_governance_run_fixed_acceptance_probe_command
    )

    release = commands.add_parser("release")
    release_commands = release.add_subparsers(
        dest="release_command",
        required=True,
    )
    attestation = release_commands.add_parser("validate-attestation")
    attestation.add_argument("--document", required=True)
    attestation.set_defaults(handler=_validate_attestation_command)

    normalize_request = release_commands.add_parser(
        "normalize-simulation-request"
    )
    normalize_request.add_argument("--repository", required=True)
    normalize_request.add_argument("--selected-ref", required=True)
    normalize_request.add_argument("--actor", required=True)
    _add_current_release_arguments(normalize_request)
    normalize_request.add_argument("--output", required=True)
    normalize_request.add_argument("--github-output")
    normalize_request.set_defaults(handler=_release_normalize_request_command)

    admit_intent = release_commands.add_parser("admit-intent")
    admit_intent.add_argument(
        "--purpose",
        choices=("release-simulation", "live-release"),
        default="release-simulation",
    )
    _add_current_release_arguments(admit_intent)
    _add_uploaded_record_arguments(admit_intent, name="intent")
    admit_intent.set_defaults(handler=_release_admit_intent_command)

    compile_model = release_commands.add_parser("compile-simulation-model")
    compile_model.add_argument("--repo-root", default=".")
    _add_current_release_arguments(compile_model)
    _add_uploaded_record_arguments(compile_model, name="intent")
    compile_model.add_argument("--provider-result", required=True)
    compile_model.add_argument(
        "--provider-artifact-id",
        required=True,
        type=int,
    )
    compile_model.add_argument(
        "--provider-artifact-digest",
        required=True,
    )
    compile_model.add_argument("--output", required=True)
    compile_model.add_argument("--github-output")
    compile_model.set_defaults(handler=_release_compile_model_command)

    create_identity = release_commands.add_parser("create-simulation-identity")
    _add_current_release_arguments(create_identity)
    _add_uploaded_record_arguments(create_identity, name="intent")
    _add_uploaded_record_arguments(
        create_identity,
        name="repository_model",
    )
    create_identity.add_argument("--output", required=True)
    create_identity.add_argument("--github-output")
    create_identity.set_defaults(handler=_release_create_identity_command)

    plan_qualification = release_commands.add_parser("plan-qualification")
    plan_qualification.add_argument("--repo-root", default=".")
    _add_current_release_arguments(plan_qualification)
    _add_uploaded_record_arguments(plan_qualification, name="intent")
    _add_uploaded_record_arguments(
        plan_qualification,
        name="repository_model",
    )
    _add_uploaded_record_arguments(
        plan_qualification,
        name="simulation_binding",
    )
    plan_qualification.add_argument("--output", required=True)
    plan_qualification.add_argument(
        "--adapter-context-output",
        required=True,
    )
    plan_qualification.add_argument("--github-output")
    plan_qualification.set_defaults(handler=_release_plan_qualification_command)

    run_build = release_commands.add_parser("run-build")
    run_build.add_argument("--repo-root", default=".")
    _add_current_release_arguments(run_build)
    _add_qualification_purpose_argument(run_build)
    _add_snapshot_arguments(run_build)
    _add_adapter_context_arguments(run_build)
    run_build.add_argument("--tarball-output", required=True)
    run_build.add_argument("--mechanical-output", required=True)
    run_build.add_argument("--failure-evidence-output", required=True)
    run_build.add_argument("--github-output")
    run_build.set_defaults(handler=_release_run_build_command)

    form_artifact = release_commands.add_parser("form-uploaded-artifact")
    _add_current_release_arguments(form_artifact)
    _add_qualification_purpose_argument(form_artifact)
    _add_snapshot_arguments(form_artifact)
    _add_adapter_context_arguments(form_artifact)
    form_artifact.add_argument("--mechanical-result", required=True)
    form_artifact.add_argument("--tarball", required=True)
    form_artifact.add_argument(
        "--tarball-artifact-id",
        required=True,
        type=int,
    )
    form_artifact.add_argument("--tarball-artifact-name", required=True)
    form_artifact.add_argument("--tarball-artifact-url", required=True)
    form_artifact.add_argument("--tarball-artifact-digest", required=True)
    form_artifact.add_argument("--artifact-output", required=True)
    form_artifact.add_argument("--evidence-output", required=True)
    form_artifact.add_argument("--github-output")
    form_artifact.set_defaults(handler=_release_form_artifact_command)

    project_test = release_commands.add_parser("run-project-test")
    project_test.add_argument("--repo-root", default=".")
    _add_current_release_arguments(project_test)
    _add_qualification_purpose_argument(project_test)
    _add_snapshot_arguments(project_test)
    _add_adapter_context_arguments(project_test)
    project_test.add_argument("--output", required=True)
    project_test.add_argument("--github-output")
    project_test.set_defaults(handler=_release_project_test_command)

    artifact_contents = release_commands.add_parser("run-artifact-contents")
    _add_current_release_arguments(artifact_contents)
    _add_qualification_purpose_argument(artifact_contents)
    _add_snapshot_arguments(artifact_contents)
    _add_adapter_context_arguments(artifact_contents)
    _add_release_artifact_arguments(artifact_contents)
    artifact_contents.add_argument("--tarball", required=True)
    artifact_contents.add_argument("--output", required=True)
    artifact_contents.add_argument("--github-output")
    artifact_contents.set_defaults(handler=_release_artifact_contents_command)

    install_import = release_commands.add_parser("run-install-import")
    _add_current_release_arguments(install_import)
    _add_qualification_purpose_argument(install_import)
    _add_snapshot_arguments(install_import)
    _add_adapter_context_arguments(install_import)
    _add_release_artifact_arguments(install_import)
    install_import.add_argument("--tarball", required=True)
    install_import.add_argument("--output", required=True)
    install_import.add_argument("--github-output")
    install_import.set_defaults(handler=_release_install_import_command)

    incomplete_evidence = release_commands.add_parser(
        "form-incomplete-evidence"
    )
    _add_current_release_arguments(incomplete_evidence)
    _add_qualification_purpose_argument(incomplete_evidence)
    _add_snapshot_arguments(incomplete_evidence)
    incomplete_evidence.add_argument(
        "--obligation-id",
        required=True,
        choices=(
            "release:quality:npm-artifact-contents",
            "release:quality:npm-install-import",
        ),
    )
    incomplete_evidence.add_argument(
        "--output-role",
        required=True,
        choices=(
            "artifact-contents-evidence",
            "install-import-evidence",
        ),
    )
    incomplete_evidence.add_argument("--output", required=True)
    incomplete_evidence.add_argument("--github-output")
    incomplete_evidence.set_defaults(
        handler=_release_incomplete_evidence_command
    )

    finalize_qualification = release_commands.add_parser(
        "finalize-qualification"
    )
    _add_current_release_arguments(finalize_qualification)
    _add_qualification_purpose_argument(finalize_qualification)
    _add_snapshot_arguments(finalize_qualification)
    _add_optional_evidence_arguments(
        finalize_qualification,
        name="build_evidence",
    )
    _add_optional_evidence_arguments(
        finalize_qualification,
        name="project_test_evidence",
    )
    _add_optional_evidence_arguments(
        finalize_qualification,
        name="artifact_contents_evidence",
    )
    _add_optional_evidence_arguments(
        finalize_qualification,
        name="install_import_evidence",
    )
    _add_release_artifact_arguments(
        finalize_qualification,
        required=False,
    )
    finalize_qualification.add_argument("--output", required=True)
    finalize_qualification.add_argument("--github-output")
    finalize_qualification.set_defaults(
        handler=_release_finalize_qualification_command
    )

    observe_npmjs = release_commands.add_parser("observe-npmjs")
    _add_current_release_arguments(observe_npmjs)
    _add_snapshot_arguments(observe_npmjs)
    _add_decision_arguments(observe_npmjs)
    _add_adapter_context_arguments(observe_npmjs)
    _add_release_artifact_arguments(observe_npmjs, required=False)
    observe_npmjs.add_argument("--output", required=True)
    observe_npmjs.add_argument("--github-output")
    observe_npmjs.set_defaults(handler=_release_observe_npmjs_command)

    materialize_actions = release_commands.add_parser(
        "materialize-hypothetical-actions"
    )
    _add_current_release_arguments(materialize_actions)
    _add_snapshot_arguments(materialize_actions)
    _add_decision_arguments(materialize_actions)
    _add_observation_set_arguments(materialize_actions)
    _add_release_artifact_arguments(
        materialize_actions,
        required=False,
    )
    materialize_actions.add_argument("--output", required=True)
    materialize_actions.add_argument("--github-output")
    materialize_actions.set_defaults(
        handler=_release_materialize_actions_command
    )

    finalize_simulation = release_commands.add_parser("finalize-simulation")
    _add_current_release_arguments(finalize_simulation)
    _add_snapshot_arguments(finalize_simulation)
    _add_decision_arguments(finalize_simulation)
    _add_observation_set_arguments(finalize_simulation)
    _add_uploaded_record_arguments(
        finalize_simulation,
        name="actions_report",
    )
    _add_release_artifact_arguments(
        finalize_simulation,
        required=False,
    )
    finalize_simulation.add_argument("--output", required=True)
    finalize_simulation.add_argument("--summary-output", required=True)
    finalize_simulation.add_argument("--github-step-summary")
    finalize_simulation.add_argument("--github-output")
    finalize_simulation.set_defaults(
        handler=_release_finalize_simulation_command
    )

    normalize_live = release_commands.add_parser("normalize-live-request")
    normalize_live.add_argument("--repository", required=True)
    normalize_live.add_argument("--selected-ref", required=True)
    normalize_live.add_argument("--actor", required=True)
    _add_current_release_arguments(normalize_live)
    normalize_live.add_argument("--output", required=True)
    normalize_live.add_argument("--github-output")
    normalize_live.set_defaults(handler=_release_normalize_live_request_command)

    compile_live = release_commands.add_parser("compile-live-model")
    compile_live.add_argument("--repo-root", default=".")
    _add_current_release_arguments(compile_live)
    _add_uploaded_record_arguments(compile_live, name="intent")
    compile_live.add_argument("--provider-result", required=True)
    compile_live.add_argument("--provider-artifact-id", required=True, type=int)
    compile_live.add_argument("--provider-artifact-digest", required=True)
    compile_live.add_argument("--output", required=True)
    compile_live.add_argument("--github-output")
    compile_live.set_defaults(handler=_release_compile_live_model_command)

    evaluate_live = release_commands.add_parser("evaluate-live-eligibility")
    evaluate_live.add_argument("--repo-root", default=".")
    evaluate_live.add_argument("--github-token", required=True)
    evaluate_live.add_argument("--consumer-policy", required=True)
    _add_current_release_arguments(evaluate_live)
    _add_uploaded_record_arguments(evaluate_live, name="intent")
    _add_uploaded_record_arguments(evaluate_live, name="repository_model")
    evaluate_live.add_argument("--output", required=True)
    evaluate_live.add_argument("--github-output")
    evaluate_live.set_defaults(
        handler=_release_evaluate_live_eligibility_command
    )

    live_eligibility = release_commands.add_parser("admit-live-eligibility")
    live_eligibility.add_argument("--repo-root", default=".")
    _add_current_release_arguments(live_eligibility)
    _add_uploaded_record_arguments(live_eligibility, name="intent")
    _add_uploaded_record_arguments(
        live_eligibility,
        name="repository_model",
    )
    live_eligibility.add_argument(
        "--live-eligibility-decision",
        required=True,
    )
    live_eligibility.add_argument(
        "--live-eligibility-artifact-id",
        required=True,
        type=int,
    )
    live_eligibility.add_argument(
        "--live-eligibility-artifact-digest",
        required=True,
    )
    live_eligibility.add_argument(
        "--live-eligibility-payload-digest",
        required=True,
    )
    live_eligibility.add_argument("--output", required=True)
    live_eligibility.add_argument("--github-output")
    live_eligibility.set_defaults(
        handler=_release_admit_live_eligibility_command
    )

    discover_history = release_commands.add_parser("discover-execution-history")
    discover_history.add_argument("--repository", required=True)
    discover_history.add_argument("--workflow-path", required=True)
    discover_history.add_argument("--github-token", required=True)
    _add_current_release_arguments(discover_history)
    _add_uploaded_record_arguments(discover_history, name="intent")
    discover_history.add_argument("--output", required=True)
    discover_history.add_argument("--github-output")
    discover_history.set_defaults(handler=_release_discover_history_command)

    admit_history = release_commands.add_parser("admit-history")
    _add_current_release_arguments(admit_history)
    _add_uploaded_record_arguments(admit_history, name="intent")
    admit_history.add_argument("--history-snapshot", required=True)
    admit_history.add_argument("--history-snapshot-digest", required=True)
    admit_history.add_argument(
        "--history-snapshot-artifact-id",
        required=True,
        type=int,
    )
    admit_history.add_argument(
        "--history-snapshot-artifact-digest",
        required=True,
    )
    admit_history.add_argument("--output", required=True)
    admit_history.add_argument("--github-output")
    admit_history.set_defaults(handler=_release_admit_history_command)

    bind_attempt = release_commands.add_parser("bind-live-attempt")
    bind_attempt.add_argument("--repo-root", default=".")
    _add_current_release_arguments(bind_attempt)
    _add_uploaded_record_arguments(bind_attempt, name="intent")
    _add_uploaded_record_arguments(bind_attempt, name="repository_model")
    bind_attempt.add_argument(
        "--live-eligibility-decision",
        required=True,
    )
    bind_attempt.add_argument(
        "--live-eligibility-artifact-id",
        required=True,
        type=int,
    )
    bind_attempt.add_argument(
        "--live-eligibility-artifact-digest",
        required=True,
    )
    bind_attempt.add_argument(
        "--live-eligibility-payload-digest",
        required=True,
    )
    bind_attempt.add_argument("--history-snapshot", required=True)
    bind_attempt.add_argument("--history-snapshot-digest", required=True)
    bind_attempt.add_argument(
        "--history-snapshot-artifact-id",
        required=True,
        type=int,
    )
    bind_attempt.add_argument(
        "--history-snapshot-artifact-digest",
        required=True,
    )
    bind_attempt.add_argument("--output", required=True)
    bind_attempt.add_argument("--github-output")
    bind_attempt.set_defaults(handler=_release_bind_live_attempt_command)

    admit_attempt = release_commands.add_parser("admit-live-attempt")
    _add_current_release_arguments(admit_attempt)
    _add_uploaded_record_arguments(admit_attempt, name="attempt_binding")
    admit_attempt.set_defaults(handler=_release_admit_live_attempt_command)

    plan_live = release_commands.add_parser("plan-live-qualification")
    plan_live.add_argument("--repo-root", default=".")
    _add_current_release_arguments(plan_live)
    _add_uploaded_record_arguments(plan_live, name="intent")
    _add_uploaded_record_arguments(plan_live, name="repository_model")
    _add_uploaded_record_arguments(plan_live, name="attempt_binding")
    plan_live.add_argument("--output", required=True)
    plan_live.add_argument("--adapter-context-output", required=True)
    plan_live.add_argument("--github-output")
    plan_live.set_defaults(handler=_release_plan_qualification_command)

    observe_github = release_commands.add_parser("observe-github-packages")
    _add_current_release_arguments(observe_github)
    _add_snapshot_arguments(observe_github)
    _add_decision_arguments(observe_github)
    _add_adapter_context_arguments(observe_github)
    _add_release_artifact_arguments(observe_github)
    observe_github.add_argument("--github-token", required=True)
    observe_github.add_argument("--output", required=True)
    observe_github.add_argument("--github-output")
    observe_github.set_defaults(
        handler=_release_observe_github_packages_command
    )

    materialize_publication = release_commands.add_parser(
        "materialize-publication"
    )
    _add_current_release_arguments(materialize_publication)
    _add_snapshot_arguments(materialize_publication)
    _add_decision_arguments(materialize_publication)
    _add_release_artifact_arguments(materialize_publication)
    _add_uploaded_record_arguments(materialize_publication, name="observation")
    materialize_publication.add_argument("--output", required=True)
    materialize_publication.add_argument("--summary-output", required=True)
    materialize_publication.add_argument(
        "--formatter-input-output",
        required=True,
    )
    materialize_publication.add_argument("--github-output")
    materialize_publication.set_defaults(
        handler=_release_materialize_publication_command
    )

    bind_reviewer = release_commands.add_parser("bind-reviewer-artifact")
    bind_reviewer.add_argument("--formatter-input", required=True)
    bind_reviewer.add_argument("--publication-snapshot", required=True)
    bind_reviewer.add_argument("--reviewer-summary", required=True)
    bind_reviewer.add_argument(
        "--reviewer-artifact-id", required=True, type=int
    )
    bind_reviewer.add_argument("--reviewer-artifact-digest", required=True)
    bind_reviewer.add_argument("--snapshot-payload-digest", required=True)
    bind_reviewer.add_argument("--summary-payload-digest", required=True)
    bind_reviewer.add_argument("--output", required=True)
    bind_reviewer.add_argument("--github-output")
    bind_reviewer.set_defaults(handler=_release_bind_reviewer_artifact_command)

    authorization = release_commands.add_parser("form-authorization")
    _add_current_release_arguments(authorization)
    authorization.add_argument("--formatter-input", required=True)
    authorization.add_argument("--reviewer-summary-artifact-id", type=int)
    authorization.add_argument("--reviewer-summary-artifact-digest")
    authorization.add_argument(
        "--approval-result",
        default="success",
        choices=("success", "deployment-review-denied"),
    )
    authorization.add_argument("--approval-job-id", required=True, type=int)
    authorization.add_argument("--completed-at", required=True)
    authorization.add_argument("--control", required=True)
    authorization.add_argument("--output", required=True)
    authorization.add_argument("--github-output")
    authorization.set_defaults(handler=_release_form_authorization_command)

    capability = release_commands.add_parser("admit-capability")
    capability.add_argument("--repo-root", default=".")
    capability.add_argument("--github-token", required=True)
    _add_current_release_arguments(capability)
    _add_uploaded_record_arguments(capability, name="intent")
    _add_uploaded_record_arguments(capability, name="repository_model")
    _add_uploaded_record_arguments(capability, name="authorization")
    _add_uploaded_record_arguments(capability, name="publication_snapshot")
    capability.add_argument("--reviewer-summary", required=True)
    capability.add_argument(
        "--reviewer-summary-artifact-id",
        required=True,
        type=int,
    )
    capability.add_argument(
        "--reviewer-summary-artifact-digest",
        required=True,
    )
    capability.add_argument("--live-eligibility-decision", required=True)
    capability.add_argument(
        "--live-eligibility-artifact-id",
        required=True,
        type=int,
    )
    capability.add_argument(
        "--live-eligibility-artifact-digest",
        required=True,
    )
    capability.add_argument(
        "--live-eligibility-payload-digest",
        required=True,
    )
    capability.add_argument("--control", required=True)
    capability.add_argument("--output", required=True)
    capability.add_argument("--github-output")
    capability.set_defaults(handler=_release_admit_capability_command)

    publish_github = release_commands.add_parser("publish-github-packages")
    _add_current_release_arguments(publish_github)
    publish_github.add_argument("--repo-root", default=".")
    _add_snapshot_arguments(publish_github)
    _add_decision_arguments(publish_github)
    _add_adapter_context_arguments(publish_github)
    _add_release_artifact_arguments(publish_github)
    _add_uploaded_record_arguments(publish_github, name="publication_snapshot")
    _add_uploaded_record_arguments(publish_github, name="authorization")
    _add_uploaded_record_arguments(publish_github, name="capability_decision")
    publish_github.add_argument("--tarball", required=True)
    publish_github.add_argument("--github-token", required=True)
    publish_github.add_argument("--preflight", required=True)
    publish_github.add_argument("--preflight-digest", required=True)
    _add_uploaded_record_arguments(
        publish_github,
        name="mutation_marker",
    )
    publish_github.add_argument("--temp-root", required=True)
    publish_github.add_argument("--receipt-output", required=True)
    publish_github.add_argument("--execution-state-output", required=True)
    publish_github.add_argument("--github-output")
    publish_github.set_defaults(
        handler=_release_publish_github_packages_command
    )

    preflight_github = release_commands.add_parser("preflight-github-packages")
    _add_current_release_arguments(preflight_github)
    preflight_github.add_argument("--repo-root", default=".")
    _add_snapshot_arguments(preflight_github)
    _add_decision_arguments(preflight_github)
    _add_adapter_context_arguments(preflight_github)
    _add_release_artifact_arguments(preflight_github)
    _add_uploaded_record_arguments(
        preflight_github,
        name="publication_snapshot",
    )
    _add_uploaded_record_arguments(preflight_github, name="authorization")
    _add_uploaded_record_arguments(
        preflight_github,
        name="capability_decision",
    )
    preflight_github.add_argument("--tarball", required=True)
    preflight_github.add_argument("--github-token", required=True)
    preflight_github.add_argument("--preflight-output", required=True)
    preflight_github.add_argument("--github-output")
    preflight_github.set_defaults(
        handler=_release_preflight_github_packages_command
    )

    mark_mutation = release_commands.add_parser(
        "mark-github-packages-mutation-start"
    )
    _add_current_release_arguments(mark_mutation)
    _add_uploaded_record_arguments(
        mark_mutation,
        name="publication_snapshot",
    )
    mark_mutation.add_argument("--preflight", required=True)
    mark_mutation.add_argument("--preflight-digest", required=True)
    mark_mutation.add_argument("--marker-output", required=True)
    mark_mutation.add_argument("--github-output")
    mark_mutation.set_defaults(
        handler=_release_mark_github_packages_mutation_command
    )

    form_github_result = release_commands.add_parser(
        "form-github-packages-result"
    )
    _add_current_release_arguments(form_github_result)
    _add_uploaded_record_arguments(
        form_github_result,
        name="publication_snapshot",
    )
    _add_uploaded_record_arguments(
        form_github_result,
        name="receipt",
        required=False,
    )
    form_github_result.add_argument("--execution-state")
    form_github_result.add_argument("--mutation-marker")
    form_github_result.add_argument(
        "--mutation-marker-artifact-id",
        type=int,
    )
    form_github_result.add_argument("--publish-step-outcome", required=True)
    form_github_result.add_argument("--result-output", required=True)
    form_github_result.add_argument("--bundle-output", required=True)
    form_github_result.add_argument("--github-output")
    form_github_result.set_defaults(
        handler=_release_form_github_packages_result_command
    )

    finalize_live = release_commands.add_parser("finalize-live")
    _add_current_release_arguments(finalize_live)
    _add_uploaded_record_arguments(finalize_live, name="attempt_binding")
    _add_snapshot_arguments(finalize_live)
    _add_decision_arguments(finalize_live)
    _add_optional_evidence_arguments(
        finalize_live,
        name="build_evidence",
    )
    _add_optional_evidence_arguments(
        finalize_live,
        name="project_test_evidence",
    )
    _add_optional_evidence_arguments(
        finalize_live,
        name="artifact_contents_evidence",
    )
    _add_optional_evidence_arguments(
        finalize_live,
        name="install_import_evidence",
    )
    _add_release_artifact_arguments(finalize_live, required=False)
    _add_uploaded_record_arguments(
        finalize_live,
        name="publication_snapshot",
        required=False,
    )
    _add_uploaded_record_arguments(
        finalize_live,
        name="authorization",
        required=False,
    )
    _add_uploaded_record_arguments(
        finalize_live,
        name="capability_decision",
        required=False,
    )
    _add_uploaded_record_arguments(
        finalize_live,
        name="capability_group_bundle",
        required=False,
    )
    _add_uploaded_record_arguments(
        finalize_live,
        name="receipt",
        required=False,
    )
    finalize_live.add_argument(
        "--publication-preparation-interrupted",
        action="store_true",
    )
    finalize_live.add_argument(
        "--platform-terminated",
        action="store_true",
    )
    finalize_live.add_argument(
        "--capability-may-have-started",
        action="store_true",
    )
    finalize_live.add_argument("--outcome-output", required=True)
    finalize_live.add_argument("--summary-output", required=True)
    finalize_live.add_argument("--github-step-summary")
    finalize_live.add_argument("--github-output")
    finalize_live.set_defaults(handler=_release_finalize_live_command)

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

    project_bootstrap = ci_commands.add_parser("project-bootstrap-shadow")
    project_bootstrap.add_argument("--repo-root", default=".")
    project_bootstrap.add_argument("--plan", required=True)
    project_bootstrap.add_argument("--plan-digest", required=True)
    project_bootstrap.add_argument("--decision", required=True)
    project_bootstrap.add_argument("--summary", required=True)
    project_bootstrap.add_argument(
        "--pull-request-number",
        required=True,
        type=int,
    )
    project_bootstrap.add_argument("--base-sha", required=True)
    project_bootstrap.add_argument("--head-sha", required=True)
    project_bootstrap.add_argument("--tested-merge-sha", required=True)
    project_bootstrap.add_argument("--github-step-summary", required=True)
    project_bootstrap.set_defaults(handler=_ci_project_bootstrap_shadow_command)
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
