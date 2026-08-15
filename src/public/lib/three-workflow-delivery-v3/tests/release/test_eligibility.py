"""Scenarios for fixed-source Governance and live eligibility."""

from __future__ import annotations

import hashlib
import json
import subprocess
from collections.abc import Callable
from dataclasses import replace
from datetime import UTC, datetime, timedelta
from pathlib import Path
from typing import TYPE_CHECKING

import pytest
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.catalogs import catalog_digest
from three_workflow_delivery_v3.release.eligibility import (
    CONSUMER_POLICY_ID,
    ConsumerPolicyResult,
    EligibilityResult,
    GovernanceBlob,
    LiveEligibilityContext,
    SurfaceDigest,
    evaluate_live_eligibility,
    observe_governance_source,
    parse_governance_attestation,
    release_policy_digest,
)
from three_workflow_delivery_v3.repository.compiler import (
    CompilationContext,
    CompiledBuild,
    CompiledOutput,
    CompiledQualitySelection,
    CompiledReleaseUnit,
    FactBundleAdmissionContext,
    RepositoryModelSnapshot,
    admit_node_provider_fact_bundle,
    compile_release_policy,
    compile_repository_model,
    first_slice_provider_manifest,
    provider_binding,
)
from three_workflow_delivery_v3.repository.descriptors import (
    FIRST_SLICE_POLICY_PATH,
    FIRST_SLICE_RELEASE_UNIT,
    GOVERNANCE_PATH,
    GOVERNANCE_REF,
    GOVERNANCE_REPOSITORY,
    GovernanceSource,
    load_release_policy,
)
from three_workflow_delivery_v3.repository.node_provider import (
    AUTHORITATIVE_REMOTE,
    PROVIDER_EXECUTION_CLASS,
    PROVIDER_EXECUTION_MODE,
    PROVIDER_IMPLEMENTATION_ID,
    PROVIDER_LOGICAL_ID,
    TAG_REFSPEC,
    CheckoutEvidence,
    GlobalInput,
    NbgvFacts,
    NodeProviderResult,
    ProjectNode,
    create_node_provider_fact_bundle,
)

if TYPE_CHECKING:
    from three_workflow_delivery_v3.repository.descriptors import ReleasePolicy

REPO_ROOT = Path(__file__).resolve().parents[6]
PRODUCT_PATH = "src/public/lib/hcoona-release-smoke-npm"
FIXTURES = Path(__file__).resolve().parents[1] / "fixtures" / "release"
TARGET = "e" * 40
GOVERNANCE_COMMIT = "f" * 40
GOVERNANCE_BLOB = "b" * 40
NOW = datetime(2026, 8, 6, 12, 0, 0, tzinfo=UTC)
SURFACE_PACKAGE = "src/public/lib/hcoona-release-smoke-npm/package.json"
SURFACE_LOCK = "pnpm-lock.yaml"
WORKFLOW_RUN_ID = 7101
RUN_ATTEMPT = 3
PREFIXED_SHA256_LENGTH = 71
FRESH_SOURCE_CALL_COUNT = 3

type SnapshotMutation = Callable[
    [RepositoryModelSnapshot], RepositoryModelSnapshot
]
type ConsumerPolicyMutation = Callable[
    [ConsumerPolicyResult], ConsumerPolicyResult
]
type LiveContextMutation = Callable[
    [LiveEligibilityContext], LiveEligibilityContext
]
type CompilationContextMutation = Callable[
    [CompilationContext], CompilationContext
]
type GovernanceSourceMutation = Callable[[GovernanceSource], GovernanceSource]


def _policy() -> ReleasePolicy:
    return load_release_policy(
        REPO_ROOT / FIRST_SLICE_POLICY_PATH,
        _target_path=FIRST_SLICE_POLICY_PATH,
    )


def _run(repo: Path, *command: str) -> str:
    return subprocess.run(  # noqa: S603
        command,
        cwd=repo,
        check=True,
        capture_output=True,
        text=True,
    ).stdout.strip()


def _initialize_repository(repo: Path) -> None:
    _run(repo, "git", "init", "--quiet")
    _run(repo, "git", "config", "user.name", "Workflow Delivery Test")
    _run(
        repo,
        "git",
        "config",
        "user.email",
        "workflow-delivery@example.invalid",
    )


def _commit_all(repo: Path) -> str:
    _run(repo, "git", "add", "--all")
    _run(repo, "git", "commit", "--quiet", "--message", "fixture")
    return _run(repo, "git", "rev-parse", "HEAD")


def _write(path: Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")


def _target_authoring_repo(tmp_path: Path) -> tuple[Path, str]:
    repo = tmp_path / "repo"
    repo.mkdir()
    _initialize_repository(repo)
    source_product = REPO_ROOT / PRODUCT_PATH
    target_product = repo / PRODUCT_PATH
    for name in (
        "package.json",
        "version.json",
        "workflow-delivery.release-unit.yml",
        "workflow-delivery.quality.yml",
    ):
        _write(
            target_product / name,
            (source_product / name).read_text(encoding="utf-8"),
        )
    _write(
        repo / FIRST_SLICE_POLICY_PATH,
        (REPO_ROOT / FIRST_SLICE_POLICY_PATH).read_text(encoding="utf-8"),
    )
    for name in (
        "package.json",
        "pnpm-lock.yaml",
        "pnpm-workspace.yaml",
        "version.json",
    ):
        _write(
            repo / name,
            (REPO_ROOT / name).read_text(encoding="utf-8"),
        )
    return repo, _commit_all(repo)


def _attestation_document(**updates: JsonValue) -> dict[str, JsonValue]:
    document = parse_canonical_json(
        (FIXTURES / "governance-disabled.json").read_bytes()
    )
    document.update(updates)
    return document


def _attestation_content(**updates: JsonValue) -> bytes:
    return canonicalize(_attestation_document(**updates))


def _snapshot(  # noqa: PLR0913
    *,
    purpose: str = "live-release",
    request_id: str = "release-request-42",
    workflow_run_id: int = WORKFLOW_RUN_ID,
    run_attempt: int = RUN_ATTEMPT,
    target: str = TARGET,
    control: str | None = None,
) -> RepositoryModelSnapshot:
    context = CompilationContext(
        request_id=request_id,
        purpose=purpose,
        workflow_run_id=workflow_run_id,
        run_attempt=run_attempt,
        target=target,
        producer="compile-model",
        control=control or f"workflow-delivery-v3:{target}",
        catalog_digest=catalog_digest(),
        channel="official" if purpose == "release-simulation" else None,
        release_unit=(
            "hcoona-release-smoke-npm"
            if purpose == "release-simulation"
            else None
        ),
    )
    return RepositoryModelSnapshot(
        context=context,
        manifest_digest="sha256:" + ("1" * 64),
        provider_result_digests=("sha256:" + ("2" * 64),),
        project_nodes=(
            ProjectNode(
                project_id="@hcoona/hcoona-release-smoke-npm",
                package_name="@hcoona/hcoona-release-smoke-npm",
                path=PRODUCT_PATH,
                manifest_path=f"{PRODUCT_PATH}/package.json",
                private=False,
                workspace_dependencies=(),
            ),
        ),
        release_units=(
            CompiledReleaseUnit(
                release_unit=FIRST_SLICE_RELEASE_UNIT,
                descriptor_path=(
                    f"{PRODUCT_PATH}/workflow-delivery.release-unit.yml"
                ),
                builds=(
                    CompiledBuild(
                        build_id="npm-package",
                        definition="node/npm-package-v1",
                        project_id="@hcoona/hcoona-release-smoke-npm",
                        entry_point=f"{PRODUCT_PATH}/package.json",
                        outputs=(
                            CompiledOutput(
                                output_id="npm-tarball",
                                role="primary-package",
                                kind="npm-tarball",
                            ),
                        ),
                        required_native_projections=("npmPackageVersion",),
                    ),
                ),
            ),
        ),
        quality=(
            CompiledQualitySelection(
                path=f"{PRODUCT_PATH}/workflow-delivery.quality.yml",
                ecosystem="node",
                preset="node/hcoona-release-smoke-npm-v1",
                required=("node/project-build-v1", "node/project-test-v1"),
                advisory=(),
            ),
        ),
        release_policy_path=FIRST_SLICE_POLICY_PATH,
        release_policy=compile_release_policy(_policy()),
        nbgv=NbgvFacts(
            canonical_version="1.2.3",
            sem_ver1="1.2.3-beta-0042-e123456",
            sem_ver2="1.2.3-beta.42.ge123456",
            version_height=42,
            git_commit_id=target,
            public_release=False,
            npm_package_version="1.2.3-beta.42.ge123456",
            node_api_result_digest="sha256:" + ("3" * 64),
        ),
        reverse_index=(
            (
                "@hcoona/hcoona-release-smoke-npm",
                (f"{FIRST_SLICE_RELEASE_UNIT}/npm-package",),
            ),
        ),
        unresolved=(),
        ready=True,
    )


def _compiled_snapshot(tmp_path: Path) -> RepositoryModelSnapshot:
    repo, target = _target_authoring_repo(tmp_path)
    context = CompilationContext(
        request_id="release-request-42",
        purpose="live-release",
        workflow_run_id=WORKFLOW_RUN_ID,
        run_attempt=RUN_ATTEMPT,
        target=target,
        producer="compile-model",
        control=f"workflow-delivery-v3:{target}",
        catalog_digest=catalog_digest(),
    )
    manifest = first_slice_provider_manifest(
        context,
        provider_producer="discover-node",
    )
    manifest_content = subprocess.run(  # noqa: S603
        ("git", "show", f"{target}:{PRODUCT_PATH}/package.json"),
        cwd=repo,
        check=True,
        capture_output=True,
    ).stdout
    global_inputs = tuple(
        GlobalInput(
            path=path,
            content_digest=(
                "sha256:"
                + hashlib.sha256(
                    subprocess.run(  # noqa: S603
                        ("git", "show", f"{target}:{path}"),
                        cwd=repo,
                        check=True,
                        capture_output=True,
                    ).stdout
                ).hexdigest()
            ),
            project_ids=("@hcoona/hcoona-release-smoke-npm",),
        )
        for path in (
            "package.json",
            "pnpm-lock.yaml",
            "pnpm-workspace.yaml",
            f"{PRODUCT_PATH}/version.json",
            "version.json",
        )
    )
    result = NodeProviderResult(
        binding=provider_binding(manifest, "node-first-slice"),
        provider_logical_id=PROVIDER_LOGICAL_ID,
        provider_implementation_id=PROVIDER_IMPLEMENTATION_ID,
        execution_mode=PROVIDER_EXECUTION_MODE,
        execution_class=PROVIDER_EXECUTION_CLASS,
        toolchain=(("node", "v24.14.0"), ("pnpm", "11.17.0")),
        manifest_digest=(
            f"sha256:{hashlib.sha256(manifest_content).hexdigest()}"
        ),
        configuration_digest=canonical_sha256(
            {
                "schema": "workflow-delivery/v3/node-provider-configuration",
                "global-inputs": [
                    global_input.to_document() for global_input in global_inputs
                ],
            }
        ),
        checkout=CheckoutEvidence(
            target=target,
            head=target,
            shallow=False,
            ancestry_complete=True,
            tags_complete=True,
            credentials_persisted=False,
            authoritative_remote=AUTHORITATIVE_REMOTE,
            authoritative_remote_url="file:///authoritative-remote.git",
            tag_refspec=TAG_REFSPEC,
        ),
        project_nodes=(
            ProjectNode(
                project_id="@hcoona/hcoona-release-smoke-npm",
                package_name="@hcoona/hcoona-release-smoke-npm",
                path="src/public/lib/hcoona-release-smoke-npm",
                manifest_path=(
                    "src/public/lib/hcoona-release-smoke-npm/package.json"
                ),
                private=False,
                workspace_dependencies=(),
            ),
        ),
        global_inputs=global_inputs,
        build_capabilities=("node/npm-package-v1",),
        nbgv=NbgvFacts(
            canonical_version="1.2.3",
            sem_ver1="1.2.3-beta-0042-e123456",
            sem_ver2="1.2.3-beta.42.ge123456",
            version_height=42,
            git_commit_id=target,
            public_release=False,
            npm_package_version="1.2.3-beta.42.ge123456",
            node_api_result_digest="sha256:" + ("3" * 64),
        ),
        unresolved=(),
        conflicts=(),
        outcome="success",
        diagnostic_reference=None,
    )
    bundle = create_node_provider_fact_bundle(
        result,
        manifest_digest=manifest.manifest_digest,
        manifest_entry_id=manifest.requests[0].entry_id,
        request_artifact_id=101,
        request_artifact_digest="sha256:" + ("7" * 64),
        transport_id=202,
        transport_digest="sha256:" + ("8" * 64),
    )
    admitted = admit_node_provider_fact_bundle(
        bundle,
        context=context,
        manifest=manifest,
        admission=FactBundleAdmissionContext(
            request_artifact_id=101,
            request_artifact_digest="sha256:" + ("7" * 64),
            transport_id=202,
            transport_digest="sha256:" + ("8" * 64),
            bundle_digest=bundle.bundle_digest,
        ),
    )
    return compile_repository_model(repo, context, manifest, [admitted])


def _consumer_policy(
    *,
    target: str = TARGET,
    consumers: tuple[str, ...] = (),
    admitted_exceptions: tuple[SurfaceDigest, ...] | None = None,
) -> ConsumerPolicyResult:
    surfaces = (
        SurfaceDigest(SURFACE_LOCK, "sha256:" + ("c" * 64)),
        SurfaceDigest(SURFACE_PACKAGE, "sha256:" + ("a" * 64)),
    )
    surfaces = tuple(sorted(surfaces, key=lambda item: item.path))
    exceptions = admitted_exceptions
    if exceptions is None:
        exceptions = (SurfaceDigest(SURFACE_PACKAGE, "sha256:" + ("a" * 64)),)
    return ConsumerPolicyResult(
        policy_id=CONSUMER_POLICY_ID,
        policy_digest="sha256:" + ("d" * 64),
        target=target,
        scanned_surfaces=surfaces,
        admitted_exceptions=exceptions,
        consumers=consumers,
    )


def _context(
    snapshot: RepositoryModelSnapshot,
    policy: ReleasePolicy,
) -> LiveEligibilityContext:
    return LiveEligibilityContext(
        purpose="live-release",
        request_id=snapshot.context.request_id,
        workflow_run_id=snapshot.context.workflow_run_id,
        run_attempt=snapshot.context.run_attempt,
        selected_ref="refs/heads/feature/workflow-delivery-v3",
        target=snapshot.context.target,
        repository_model_digest=snapshot.snapshot_digest,
        producer="evaluate-live-eligibility",
        control=snapshot.context.control,
        release_policy_digest=release_policy_digest(policy),
        catalog_digest=catalog_digest(),
    )


def _object_member(
    document: dict[str, JsonValue],
    name: str,
) -> dict[str, JsonValue]:
    value = document[name]
    if not isinstance(value, dict):
        message = f"{name} is not an object"
        raise TypeError(message)
    return value


def _with_snapshot_channel(
    context: CompilationContext,
) -> CompilationContext:
    return replace(context, channel="official")


def _with_snapshot_release_unit(
    context: CompilationContext,
) -> CompilationContext:
    return replace(context, release_unit=FIRST_SLICE_RELEASE_UNIT)


def _with_simulation_purpose(
    context: LiveEligibilityContext,
) -> LiveEligibilityContext:
    return replace(context, purpose="release-simulation")


def _with_other_request(
    context: LiveEligibilityContext,
) -> LiveEligibilityContext:
    return replace(context, request_id="other-request")


def _with_other_workflow_run(
    context: LiveEligibilityContext,
) -> LiveEligibilityContext:
    return replace(context, workflow_run_id=7102)


def _with_other_attempt(
    context: LiveEligibilityContext,
) -> LiveEligibilityContext:
    return replace(context, run_attempt=4)


def _with_other_target(
    context: LiveEligibilityContext,
) -> LiveEligibilityContext:
    return replace(context, target="d" * 40)


def _with_other_control(
    context: LiveEligibilityContext,
) -> LiveEligibilityContext:
    return replace(context, control="other-control")


def _with_other_catalog(
    context: LiveEligibilityContext,
) -> LiveEligibilityContext:
    return replace(context, catalog_digest="sha256:" + ("9" * 64))


def _with_other_release_policy(
    context: LiveEligibilityContext,
) -> LiveEligibilityContext:
    return replace(
        context,
        release_policy_digest="sha256:" + ("8" * 64),
    )


def _with_empty_request(
    context: LiveEligibilityContext,
) -> LiveEligibilityContext:
    return replace(context, request_id="")


def _with_empty_selected_ref(
    context: LiveEligibilityContext,
) -> LiveEligibilityContext:
    return replace(context, selected_ref="")


def _with_zero_workflow_run(
    context: LiveEligibilityContext,
) -> LiveEligibilityContext:
    return replace(context, workflow_run_id=0)


def _with_boolean_attempt(
    context: LiveEligibilityContext,
) -> LiveEligibilityContext:
    return replace(context, run_attempt=True)


def _with_empty_producer(
    context: LiveEligibilityContext,
) -> LiveEligibilityContext:
    return replace(context, producer="")


def _with_empty_control(
    context: LiveEligibilityContext,
) -> LiveEligibilityContext:
    return replace(context, control="")


def _with_malformed_snapshot_digest(
    context: LiveEligibilityContext,
) -> LiveEligibilityContext:
    return replace(context, repository_model_digest="1" * 64)


def _with_other_governance_repository(
    source: GovernanceSource,
) -> GovernanceSource:
    return replace(source, repository="other/repository")


def _with_short_governance_ref(
    source: GovernanceSource,
) -> GovernanceSource:
    return replace(source, ref="main")


def _with_other_governance_path(
    source: GovernanceSource,
) -> GovernanceSource:
    return replace(source, path=".github/other.json")


def _with_lower_governance_age(
    source: GovernanceSource,
) -> GovernanceSource:
    return replace(source, max_age_days=89)


def _with_higher_governance_age(
    source: GovernanceSource,
) -> GovernanceSource:
    return replace(source, max_age_days=91)


class RecordingGovernanceClient:
    """Deterministic contents-read-compatible Governance source."""

    def __init__(self, content: bytes) -> None:
        """Initialize one protected ref and fixed-path blob."""
        self.content = content
        self.protected = True
        self.commit = GOVERNANCE_COMMIT
        self.blob_oid = GOVERNANCE_BLOB
        self.failure: str | None = None
        self.calls: list[tuple[str, ...]] = []

    def is_ref_protected(self, repository: str, ref: str) -> bool:
        """Record and answer the fresh protection query."""
        self.calls.append(("protected", repository, ref))
        if self.failure == "protection-read":
            message = "protection unavailable"
            raise ValueError(message)
        return self.protected

    def resolve_ref(self, repository: str, ref: str) -> str:
        """Record and answer the fresh ref resolution."""
        self.calls.append(("resolve", repository, ref))
        if self.failure == "resolve":
            message = "ref unavailable"
            raise ValueError(message)
        return self.commit

    def read_blob(
        self,
        repository: str,
        commit: str,
        path: str,
    ) -> GovernanceBlob:
        """Record and answer the exact commit/path blob read."""
        self.calls.append(("read", repository, commit, path))
        if self.failure == "blob":
            message = "blob unavailable"
            raise ValueError(message)
        return GovernanceBlob(blob_oid=self.blob_oid, content=self.content)


def _evaluate(  # noqa: PLR0913
    client: RecordingGovernanceClient,
    *,
    snapshot: RepositoryModelSnapshot | None = None,
    consumer_policy: ConsumerPolicyResult | None = None,
    context_mutation: LiveContextMutation | None = None,
    policy: ReleasePolicy | None = None,
    now: datetime = NOW,
):
    selected_snapshot = snapshot or _snapshot()
    selected_policy = policy or _policy()
    context = _context(selected_snapshot, selected_policy)
    if context_mutation is not None:
        context = context_mutation(context)
    return evaluate_live_eligibility(
        context,
        selected_snapshot,
        consumer_policy or _consumer_policy(),
        selected_policy,
        client,
        now=now,
    )


def test_live_eligibility_passes_with_fresh_exact_target_inputs() -> None:
    """Pass only current exact-target inputs and a fresh enabled attestation."""
    client = RecordingGovernanceClient(_attestation_content(live_enabled=True))

    decision = _evaluate(client)

    assert decision.result is EligibilityResult.PASS
    assert decision.diagnostics == ()
    assert decision.context.purpose == "live-release"
    assert decision.context.request_id == "release-request-42"
    assert decision.context.workflow_run_id == WORKFLOW_RUN_ID
    assert decision.context.run_attempt == RUN_ATTEMPT
    assert decision.context.target == TARGET
    assert decision.consumer_policy.target == TARGET
    assert decision.governance.attestation.live_enabled is True
    assert decision.decision_digest.startswith("sha256:")
    assert client.calls == [
        ("protected", GOVERNANCE_REPOSITORY, GOVERNANCE_REF),
        ("resolve", GOVERNANCE_REPOSITORY, GOVERNANCE_REF),
        (
            "read",
            GOVERNANCE_REPOSITORY,
            GOVERNANCE_COMMIT,
            GOVERNANCE_PATH,
        ),
    ]


def test_live_eligibility_accepts_an_actual_compiled_repository_model(
    tmp_path: Path,
) -> None:
    """Bind the Decision to the compiler's complete first-slice Snapshot."""
    snapshot = _compiled_snapshot(tmp_path)
    client = RecordingGovernanceClient(_attestation_content(live_enabled=True))

    decision = _evaluate(
        client,
        snapshot=snapshot,
        consumer_policy=_consumer_policy(target=snapshot.context.target),
    )

    assert decision.result is EligibilityResult.PASS
    assert decision.context.repository_model_digest == snapshot.snapshot_digest
    assert snapshot.release_units[0].release_unit == (
        "hcoona-release-smoke-npm"
    )
    assert snapshot.release_units[0].builds[0].outputs[0].output_id == (
        "npm-tarball"
    )
    context = _object_member(decision.to_document(), "context")
    assert context["repository-model-digest"] == (snapshot.snapshot_digest)


@pytest.mark.parametrize(
    ("context_mutation", "field_name"),
    [
        (_with_snapshot_channel, "channel"),
        (_with_snapshot_release_unit, "release_unit"),
    ],
    ids=["channel-set", "release-unit-set"],
)
def test_live_eligibility_rejects_live_snapshot_with_selection(
    context_mutation: CompilationContextMutation,
    field_name: str,
) -> None:
    """Reject live Repository Model contexts carrying simulation selection."""
    base_snapshot = _snapshot()
    snapshot = replace(
        base_snapshot,
        context=context_mutation(base_snapshot.context),
    )
    policy = _policy()
    client = RecordingGovernanceClient(_attestation_content(live_enabled=True))

    with pytest.raises(
        ValueError,
        match="simulation selection",
    ):
        evaluate_live_eligibility(
            _context(snapshot, policy),
            snapshot,
            _consumer_policy(),
            policy,
            client,
            now=NOW,
        )

    assert getattr(snapshot.context, field_name) is not None
    assert snapshot.context.purpose == "live-release"
    assert client.calls == []


@pytest.mark.parametrize(
    ("mutate", "message"),
    [
        (
            lambda snapshot: replace(snapshot, release_units=()),
            "must contain one Release Unit",
        ),
        (
            lambda snapshot: replace(snapshot, quality=()),
            "Quality closure mismatch",
        ),
        (
            lambda snapshot: replace(
                snapshot,
                project_nodes=(
                    replace(snapshot.project_nodes[0], path="src/substitute"),
                ),
            ),
            "Project Node closure mismatch",
        ),
        (
            lambda snapshot: replace(
                snapshot,
                release_units=(
                    replace(
                        snapshot.release_units[0],
                        builds=(
                            replace(
                                snapshot.release_units[0].builds[0],
                                outputs=(
                                    replace(
                                        snapshot.release_units[0]
                                        .builds[0]
                                        .outputs[0],
                                        output_id="substituted-tarball",
                                    ),
                                ),
                            ),
                        ),
                    ),
                ),
            ),
            "output closure mismatch",
        ),
        (
            lambda snapshot: replace(
                snapshot,
                nbgv=replace(snapshot.nbgv, git_commit_id="d" * 40),
            ),
            "NBGV facts are incomplete",
        ),
    ],
    ids=[
        "missing-release-unit",
        "missing-quality",
        "substituted-project",
        "substituted-output",
        "target-unbound-nbgv",
    ],
)
def test_eligibility_rejects_incomplete_or_substituted_repository_model_closure(
    mutate: SnapshotMutation,
    message: str,
) -> None:
    """Reject self-consistent Snapshots without first-slice closure."""
    snapshot = mutate(_snapshot())
    policy = _policy()
    client = RecordingGovernanceClient(_attestation_content(live_enabled=True))

    with pytest.raises(ValueError, match=message):
        evaluate_live_eligibility(
            _context(snapshot, policy),
            snapshot,
            _consumer_policy(),
            policy,
            client,
            now=NOW,
        )

    assert client.calls == []


def test_decision_binds_attestation_provenance_and_content_digest() -> None:
    """Bind fixed source, commit, blob, canonical content, and observation."""
    content = _attestation_content(live_enabled=True)
    client = RecordingGovernanceClient(content)

    decision = _evaluate(client)
    document = decision.to_document()
    governance = _object_member(document, "governance")
    consumer_policy = _object_member(document, "consumer-policy")
    context = _object_member(document, "context")

    assert governance == {
        "repository": GOVERNANCE_REPOSITORY,
        "ref": GOVERNANCE_REF,
        "resolved-commit": GOVERNANCE_COMMIT,
        "path": GOVERNANCE_PATH,
        "blob-oid": GOVERNANCE_BLOB,
        "content-sha256": decision.governance.content_sha256,
        "observed-at": "2026-08-06T12:00:00Z",
        "max-age-days": 90,
        "live-enabled": True,
        "issuer": "hcoona",
        "inspected-at": "2026-08-01T00:00:00Z",
        "expires-at": "2026-10-01T00:00:00Z",
        "attestation-content-digest": (
            decision.governance.attestation.content_digest
        ),
    }
    assert decision.governance.content_sha256 == (
        decision.governance.attestation.content_digest
    )
    assert decision.governance.content_sha256.startswith("sha256:")
    assert len(decision.governance.content_sha256) == PREFIXED_SHA256_LENGTH
    assert consumer_policy["result-digest"] == (
        decision.consumer_policy.result_digest
    )
    assert context["repository-model-digest"] == (
        decision.context.repository_model_digest
    )


def test_each_evaluation_performs_a_fresh_protected_ref_read() -> None:
    """Do not reuse an earlier enabled observation after source disablement."""
    client = RecordingGovernanceClient(_attestation_content(live_enabled=True))

    first = _evaluate(client)
    client.content = _attestation_content(live_enabled=False)
    client.blob_oid = "c" * 40
    second = _evaluate(client)

    assert first.result is EligibilityResult.PASS
    assert second.result is EligibilityResult.BLOCKED
    assert second.diagnostics == ("governance-live-disabled",)
    assert first.governance.content_sha256 != second.governance.content_sha256
    assert first.governance.blob_oid == GOVERNANCE_BLOB
    assert second.governance.blob_oid == "c" * 40
    assert [call[0] for call in client.calls] == [
        "protected",
        "resolve",
        "read",
        "protected",
        "resolve",
        "read",
    ]


def test_disabled_attestation_blocks_before_attempt_creation() -> None:
    """Return a blocking pre-Attempt Decision for live_enabled false."""
    client = RecordingGovernanceClient(
        (FIXTURES / "governance-disabled.json").read_bytes()
    )

    decision = _evaluate(client)
    document = decision.to_document()

    assert decision.result is EligibilityResult.BLOCKED
    assert decision.diagnostics == ("governance-live-disabled",)
    assert document["result"] == "blocked"
    governance = _object_member(document, "governance")
    assert governance["live-enabled"] is False
    assert "attempt" not in document
    assert "release-execution" not in document
    assert "authorization" not in document


def test_expired_attestation_blocks_before_attempt_creation() -> None:
    """Block a structurally valid attestation after its current expiry."""
    client = RecordingGovernanceClient(
        _attestation_content(
            live_enabled=True,
            inspected_at="2026-06-01T00:00:00Z",
            expires_at="2026-08-05T00:00:00Z",
        )
    )

    decision = _evaluate(client)

    assert decision.result is EligibilityResult.BLOCKED
    assert decision.diagnostics == ("governance-attestation-expired",)
    assert decision.governance.attestation.live_enabled is True
    assert decision.governance.attestation.expires_at < NOW


@pytest.mark.parametrize(
    ("inspected_at", "expires_at", "diagnostic"),
    [
        (
            "2026-08-07T00:00:00Z",
            "2026-10-01T00:00:00Z",
            "governance-attestation-not-yet-valid",
        ),
        (
            "2026-06-01T00:00:00Z",
            "2026-08-06T12:00:00Z",
            "governance-attestation-expired",
        ),
    ],
    ids=["future-inspection", "exact-expiry"],
)
def test_attestation_time_boundaries_block_live_eligibility(
    inspected_at: str,
    expires_at: str,
    diagnostic: str,
) -> None:
    """Block before inspection and at the exact expiration instant."""
    client = RecordingGovernanceClient(
        _attestation_content(
            live_enabled=True,
            inspected_at=inspected_at,
            expires_at=expires_at,
        )
    )

    decision = _evaluate(client)

    assert decision.result is EligibilityResult.BLOCKED
    assert decision.diagnostics == (diagnostic,)


def test_consumer_positive_result_blocks_before_attempt_creation() -> None:
    """Block any normal consumer reported by the target-bound policy."""
    consumer_path = "src/public/app/consumer/package.json"
    policy_result = _consumer_policy(consumers=(consumer_path,))
    policy_result = replace(
        policy_result,
        scanned_surfaces=tuple(
            sorted(
                (
                    *policy_result.scanned_surfaces,
                    SurfaceDigest(
                        consumer_path,
                        "sha256:" + ("9" * 64),
                    ),
                ),
                key=lambda item: item.path,
            )
        ),
    )
    client = RecordingGovernanceClient(_attestation_content(live_enabled=True))

    decision = _evaluate(client, consumer_policy=policy_result)

    assert decision.result is EligibilityResult.BLOCKED
    assert decision.diagnostics == ("consumer-policy-found-consumers",)
    assert decision.consumer_policy.consumers == (consumer_path,)
    consumer_policy = _object_member(
        decision.to_document(),
        "consumer-policy",
    )
    assert consumer_policy["consumers"] == [consumer_path]
    assert decision.governance.attestation.live_enabled is True


@pytest.mark.parametrize(
    ("mutate", "message"),
    [
        (
            lambda result: replace(result, target="d" * 40),
            "target does not match",
        ),
        (
            lambda result: replace(
                result,
                admitted_exceptions=(
                    SurfaceDigest(
                        SURFACE_PACKAGE,
                        "sha256:" + ("f" * 64),
                    ),
                ),
            ),
            "not digest-bound/allowlisted",
        ),
        (
            lambda result: replace(
                result,
                admitted_exceptions=(
                    SurfaceDigest(
                        "src/public/app/unreviewed/package.json",
                        "sha256:" + ("a" * 64),
                    ),
                ),
            ),
            "not digest-bound/allowlisted",
        ),
        (
            lambda result: replace(
                result,
                scanned_surfaces=tuple(reversed(result.scanned_surfaces)),
            ),
            "sorted by path",
        ),
    ],
    ids=["target", "exception-digest", "exception-path", "surface-order"],
)
def test_consumer_policy_requires_exact_target_and_digest_bound_surfaces(
    mutate: ConsumerPolicyMutation,
    message: str,
) -> None:
    """Reject target mismatch and unreviewed or non-digest exceptions."""
    client = RecordingGovernanceClient(_attestation_content(live_enabled=True))
    policy_result = mutate(_consumer_policy())

    with pytest.raises(ValueError, match=message):
        _evaluate(client, consumer_policy=policy_result)


@pytest.mark.parametrize(
    ("mutate", "message"),
    [
        (
            lambda result: replace(result, policy_id="target/policy"),
            "ID is not the static",
        ),
        (
            lambda result: replace(result, policy_digest="d" * 64),
            "digest must be SHA-256",
        ),
        (
            lambda result: replace(result, target="e" * 39),
            "target must be a full commit SHA",
        ),
        (
            lambda result: replace(result, scanned_surfaces=()),
            "scanned_surfaces must be nonempty",
        ),
        (
            lambda result: replace(
                result,
                scanned_surfaces=(
                    result.scanned_surfaces[0],
                    result.scanned_surfaces[0],
                ),
            ),
            "duplicate path",
        ),
        (
            lambda result: replace(
                result,
                consumers=(SURFACE_PACKAGE, SURFACE_LOCK),
            ),
            "consumers must be sorted",
        ),
        (
            lambda result: replace(
                result,
                consumers=(SURFACE_PACKAGE, SURFACE_PACKAGE),
            ),
            "consumers contain duplicates",
        ),
        (
            lambda result: replace(
                result,
                consumers=("src/public/lib/unscanned/package.json",),
            ),
            "was not in scanned surfaces",
        ),
    ],
    ids=[
        "policy-id",
        "policy-digest",
        "target-shape",
        "empty-surfaces",
        "duplicate-surface",
        "consumer-order",
        "duplicate-consumer",
        "unscanned-consumer",
    ],
)
def test_consumer_policy_input_is_closed_and_deterministic(
    mutate: ConsumerPolicyMutation,
    message: str,
) -> None:
    """Reject unbound, ambiguous, or nondeterministic scanner results."""
    client = RecordingGovernanceClient(_attestation_content(live_enabled=True))
    policy_result = mutate(_consumer_policy())

    with pytest.raises(ValueError, match=message):
        _evaluate(client, consumer_policy=policy_result)

    assert client.calls == []


def test_eligibility_rejects_prior_attempt_repository_model() -> None:
    """Reject a prior-attempt Snapshot before reading Governance."""
    current_snapshot = _snapshot(run_attempt=4)
    prior_snapshot = _snapshot(run_attempt=3)
    policy = _policy()
    context = _context(current_snapshot, policy)
    client = RecordingGovernanceClient(_attestation_content(live_enabled=True))

    with pytest.raises(
        ValueError,
        match="Repository Model is not exact and ready",
    ):
        evaluate_live_eligibility(
            context,
            prior_snapshot,
            _consumer_policy(),
            policy,
            client,
            now=NOW,
        )

    assert client.calls == []


@pytest.mark.parametrize(
    ("context_mutation", "message"),
    [
        (
            _with_simulation_purpose,
            "requires live-release",
        ),
        (
            _with_other_request,
            "Repository Model binding mismatch",
        ),
        (
            _with_other_workflow_run,
            "Repository Model binding mismatch",
        ),
        (
            _with_other_attempt,
            "Repository Model binding mismatch",
        ),
        (
            _with_other_target,
            "Repository Model binding mismatch",
        ),
        (
            _with_other_control,
            "Repository Model binding mismatch",
        ),
        (
            _with_other_catalog,
            "catalog digest mismatch",
        ),
        (
            _with_other_release_policy,
            "Release policy digest mismatch",
        ),
    ],
    ids=[
        "purpose",
        "request",
        "run",
        "attempt",
        "target",
        "control",
        "catalog",
        "policy",
    ],
)
def test_eligibility_rejects_wrong_current_binding(
    context_mutation: LiveContextMutation,
    message: str,
) -> None:
    """Reject each wrong current authority before the fresh source read."""
    client = RecordingGovernanceClient(_attestation_content(live_enabled=True))

    with pytest.raises(ValueError, match=message):
        _evaluate(
            client,
            context_mutation=context_mutation,
        )

    assert client.calls == []


@pytest.mark.parametrize(
    "source_mutation",
    [
        _with_other_governance_repository,
        _with_short_governance_ref,
        _with_other_governance_path,
        _with_lower_governance_age,
        _with_higher_governance_age,
    ],
    ids=["repository", "ref", "path", "age-lower", "age-upper"],
)
def test_fixed_governance_source_rejects_repository_ref_path_or_age_change(
    source_mutation: GovernanceSourceMutation,
) -> None:
    """Reject any source variation before invoking the client."""
    policy = _policy()
    policy = replace(
        policy,
        governance=source_mutation(policy.governance),
    )
    client = RecordingGovernanceClient(_attestation_content(live_enabled=True))

    with pytest.raises(
        ValueError,
        match="Governance source is not the exact fixed contract",
    ):
        _evaluate(client, policy=policy)

    assert client.calls == []


@pytest.mark.parametrize(
    ("content", "message"),
    [
        (
            canonicalize(
                {
                    **_attestation_document(),
                    "command": "npm publish",
                }
            ),
            "unknown field: command",
        ),
        (
            json.dumps(_attestation_document(), indent=2).encode(),
            "record is not canonical",
        ),
        (
            (b'{"schema":"first","schema":"second","live_enabled":false}'),
            "duplicate JSON object member: 'schema'",
        ),
    ],
    ids=["executable-field", "noncanonical", "duplicate"],
)
def test_attestation_rejects_unknown_duplicate_noncanonical_and_executable_fields(  # noqa: E501
    content: bytes,
    message: str,
) -> None:
    """Keep the protected document canonical, strict, and non-executable."""
    with pytest.raises(ValueError, match=message):
        parse_governance_attestation(content)


@pytest.mark.parametrize(
    "value",
    [0, 1, "false", None],
    ids=["zero", "one", "string", "null"],
)
def test_attestation_requires_boolean_live_enabled(value: JsonValue) -> None:
    """Reject truthy/falsy substitutes for the required Boolean."""
    content = _attestation_content(live_enabled=value)

    with pytest.raises(
        TypeError,
        match="live_enabled must be Boolean",
    ):
        parse_governance_attestation(content)


@pytest.mark.parametrize(
    "writers",
    [
        [],
        [{"login": "hcoona", "role": "Read"}],
        [
            {"login": "hcoona", "role": "Admin"},
            {"login": "hcoona", "role": "Write"},
        ],
    ],
    ids=["empty", "wrong-role", "duplicate-login"],
)
def test_attestation_requires_accepted_writer_inventory(
    writers: list[JsonValue],
) -> None:
    """Require nonempty unique Write/Maintain/Admin writer facts."""
    content = _attestation_content(accepted_writers=writers)

    with pytest.raises(ValueError, match="accepted_writers"):
        parse_governance_attestation(content)


def test_attestation_accepts_human_access_evidence_digest_alternative() -> None:
    """Accept evidence identity instead of a structured access inventory."""
    document = _attestation_document(live_enabled=True)
    del document["access_inventory"]
    document["access_evidence_digest"] = "sha256:" + ("7" * 64)

    attestation = parse_governance_attestation(canonicalize(document))

    assert attestation.access_inventory is None
    assert attestation.access_evidence_digest == "sha256:" + ("7" * 64)
    assert attestation.live_enabled is True
    assert attestation.content_digest.startswith("sha256:")


@pytest.mark.parametrize(
    "configuration",
    ["neither", "both", "malformed-digest", "empty-inventory"],
)
def test_attestation_requires_exactly_one_access_inventory_or_evidence_digest(
    configuration: str,
) -> None:
    """Require one complete human-inspection evidence representation."""
    document = _attestation_document()
    if configuration in {"neither", "malformed-digest"}:
        del document["access_inventory"]
    if configuration == "both":
        document["access_evidence_digest"] = "sha256:" + ("7" * 64)
    elif configuration == "malformed-digest":
        document["access_evidence_digest"] = "7" * 64
    elif configuration == "empty-inventory":
        access_inventory = _object_member(document, "access_inventory")
        access_inventory["package"] = []

    with pytest.raises(ValueError, match=r"access|SHA-256"):
        parse_governance_attestation(canonicalize(document))


@pytest.mark.parametrize(
    ("updates", "message"),
    [
        ({"release_policy": "other-policy"}, "policy/package binding"),
        ({"package": "@hcoona/other"}, "policy/package binding"),
        ({"issuer": ""}, "issuer must be a nonempty string"),
        ({"inspected_at": "2026-08-01"}, "UTC second-precision"),
        (
            {
                "inspected_at": "2026-08-01T00:00:00Z",
                "expires_at": "2026-10-31T00:00:01Z",
            },
            "expiry must be within 90 days",
        ),
        ({"limitations": []}, "limitations must be nonempty"),
    ],
    ids=[
        "policy",
        "package",
        "issuer",
        "inspection-time",
        "expiry-bound",
        "limitations",
    ],
)
def test_attestation_requires_policy_package_issuer_times_and_limitations(
    updates: dict[str, JsonValue],
    message: str,
) -> None:
    """Validate every fixed human-attestation semantic field."""
    with pytest.raises((TypeError, ValueError), match=message):
        parse_governance_attestation(_attestation_content(**updates))


def test_attestation_expiry_accepts_exactly_ninety_days() -> None:
    """Keep the approved maximum inclusive rather than shortening it."""
    inspected_at = datetime(2026, 8, 1, tzinfo=UTC)
    expires_at = inspected_at + timedelta(days=90)
    content = _attestation_content(
        inspected_at=inspected_at.strftime("%Y-%m-%dT%H:%M:%SZ"),
        expires_at=expires_at.strftime("%Y-%m-%dT%H:%M:%SZ"),
    )

    attestation = parse_governance_attestation(content)

    assert attestation.expires_at - attestation.inspected_at == timedelta(
        days=90
    )
    assert attestation.to_document()["expires_at"] == "2026-10-30T00:00:00Z"


@pytest.mark.parametrize(
    "expires_at",
    ["2026-08-01T00:00:00Z", "2026-07-31T23:59:59Z"],
    ids=["zero-lifetime", "negative-lifetime"],
)
def test_attestation_expiry_requires_positive_lifetime(
    expires_at: str,
) -> None:
    """Reject zero-length and reversed attestation validity windows."""
    content = _attestation_content(
        inspected_at="2026-08-01T00:00:00Z",
        expires_at=expires_at,
    )

    with pytest.raises(ValueError, match="expiry must be within 90 days"):
        parse_governance_attestation(content)


@pytest.mark.parametrize(
    ("context_mutation", "message"),
    [
        (_with_empty_request, "request_id"),
        (_with_empty_selected_ref, "selected_ref"),
        (_with_zero_workflow_run, "must be a positive integer"),
        (_with_boolean_attempt, "must be a positive integer"),
        (_with_empty_producer, "producer"),
        (_with_empty_control, "control"),
        (_with_malformed_snapshot_digest, "must be SHA-256"),
    ],
    ids=[
        "request",
        "selected-ref",
        "run",
        "attempt",
        "producer",
        "control",
        "snapshot-digest",
    ],
)
def test_live_context_rejects_invalid_primitives_before_source_read(
    context_mutation: LiveContextMutation,
    message: str,
) -> None:
    """Reject malformed current authority before reading Governance."""
    client = RecordingGovernanceClient(_attestation_content(live_enabled=True))

    with pytest.raises((TypeError, ValueError), match=message):
        _evaluate(client, context_mutation=context_mutation)

    assert client.calls == []


@pytest.mark.parametrize(
    "failure",
    ["unprotected", "protection-read", "resolve", "blob"],
    ids=["unprotected", "protection-read", "missing-ref", "missing-blob"],
)
def test_missing_unreadable_or_unprotected_source_fails_closed(
    failure: str,
) -> None:
    """Fail without a Decision when the fixed source cannot be freshly read."""
    client = RecordingGovernanceClient(_attestation_content(live_enabled=True))
    if failure == "unprotected":
        client.protected = False
    else:
        client.failure = failure

    with pytest.raises(ValueError, match=r"protected|unavailable"):
        _evaluate(client)

    assert any(call[0] == "protected" for call in client.calls)
    assert not (
        failure in {"unprotected", "protection-read"}
        and any(call[0] == "resolve" for call in client.calls)
    )


@pytest.mark.parametrize(
    ("field", "value", "message"),
    [
        ("commit", "f" * 39, "full commit SHA"),
        ("commit", "F" * 40, "full commit SHA"),
        ("blob_oid", "b" * 39, "blob OID is malformed"),
        ("blob_oid", "B" * 40, "blob OID is malformed"),
    ],
    ids=[
        "short-commit",
        "uppercase-commit",
        "short-blob",
        "uppercase-blob",
    ],
)
def test_resolved_commit_blob_and_content_provenance_are_strict(
    field: str,
    value: str,
    message: str,
) -> None:
    """Reject malformed resolved provenance before eligibility."""
    client = RecordingGovernanceClient(_attestation_content(live_enabled=True))
    setattr(client, field, value)

    with pytest.raises(ValueError, match=message):
        _evaluate(client)


def test_prior_facts_cannot_substitute_for_fresh_input() -> None:
    """Require the live evaluator's client even when prior facts exist."""
    client = RecordingGovernanceClient(_attestation_content(live_enabled=True))
    prior = _evaluate(client)
    blocking_client = RecordingGovernanceClient(
        _attestation_content(live_enabled=True)
    )
    blocking_client.failure = "blob"

    with pytest.raises(ValueError, match="blob unavailable"):
        _evaluate(blocking_client)

    assert prior.result is EligibilityResult.PASS
    assert prior.decision_digest.startswith("sha256:")
    assert blocking_client.calls[-1][0] == "read"
    assert len(blocking_client.calls) == FRESH_SOURCE_CALL_COUNT


def test_observation_rejects_non_utc_time_before_source_read() -> None:
    """Require a trusted UTC observation instant."""
    client = RecordingGovernanceClient(_attestation_content(live_enabled=True))

    with pytest.raises(ValueError, match="UTC-aware"):
        observe_governance_source(
            _policy().governance,
            client,
            now=datetime(2026, 8, 6, 12, 0, 0),  # noqa: DTZ001
        )

    assert client.calls == []


_WHITESPACE_ONLY_VALUES = (" ", "\t", "\n", "\r\n", "\u00a0")
_HUMAN_EVIDENCE_PATHS: dict[str, tuple[str | int, ...]] = {
    "issuer": ("issuer",),
    "accepted_writers[0].login": ("accepted_writers", 0, "login"),
    "accepted_writers[0].role": ("accepted_writers", 0, "role"),
    "access_inventory.repository[0].subject": (
        "access_inventory",
        "repository",
        0,
        "subject",
    ),
    "access_inventory.repository[0].access": (
        "access_inventory",
        "repository",
        0,
        "access",
    ),
    "access_inventory.package[0].subject": (
        "access_inventory",
        "package",
        0,
        "subject",
    ),
    "access_inventory.package[0].access": (
        "access_inventory",
        "package",
        0,
        "access",
    ),
    "access_inventory.manage_actions[0].subject": (
        "access_inventory",
        "manage_actions",
        0,
        "subject",
    ),
    "access_inventory.manage_actions[0].access": (
        "access_inventory",
        "manage_actions",
        0,
        "access",
    ),
    "limitations[0]": ("limitations", 0),
}
_HUMAN_EVIDENCE_MESSAGES = {
    **{
        position: (
            position.replace(".", r"\.").replace("[", r"\[").replace("]", r"\]")
            + " must be a nonempty string"
        )
        for position in _HUMAN_EVIDENCE_PATHS
    },
    "limitations[0]": "limitations must be a nonempty string",
    "access_evidence_digest": (
        "access_evidence_digest must be a nonempty string"
    ),
}
_PRESERVED_HUMAN_EVIDENCE_POSITIONS = (
    "issuer",
    "accepted_writers[0].login",
    "access_inventory.repository[0].subject",
    "access_inventory.repository[0].access",
    "access_inventory.package[0].subject",
    "access_inventory.package[0].access",
    "access_inventory.manage_actions[0].subject",
    "access_inventory.manage_actions[0].access",
    "limitations[0]",
    "access_evidence_digest",
    "accepted_writers[0].role",
)
_FIXED_CANONICAL_VALUES = {
    "schema": "workflow-delivery/v3/governance-attestation",
    "release_policy": "hcoona-release-smoke-npm",
    "package": "@hcoona/hcoona-release-smoke-npm",
    "accepted_writers[0].role": "Admin",
}
_FIXED_CANONICAL_PATHS: dict[str, tuple[str | int, ...]] = {
    "schema": ("schema",),
    "release_policy": ("release_policy",),
    "package": ("package",),
    "accepted_writers[0].role": ("accepted_writers", 0, "role"),
}


def _replace_attestation_member(
    document: dict[str, JsonValue],
    path: tuple[str | int, ...],
    value: str,
) -> None:
    current: JsonValue = document
    for member in path[:-1]:
        if isinstance(member, str):
            assert isinstance(current, dict)
            current = current[member]
        else:
            assert isinstance(current, list)
            current = current[member]
    leaf = path[-1]
    if isinstance(leaf, str):
        assert isinstance(current, dict)
        current[leaf] = value
    else:
        assert isinstance(current, list)
        current[leaf] = value


def _attestation_document_with_human_evidence(
    position: str,
    value: str,
) -> dict[str, JsonValue]:
    document = _attestation_document()
    if position == "access_evidence_digest":
        del document["access_inventory"]
        document["access_evidence_digest"] = value
    else:
        _replace_attestation_member(
            document,
            _HUMAN_EVIDENCE_PATHS[position],
            value,
        )
    return document


@pytest.mark.parametrize(
    "position",
    [*_HUMAN_EVIDENCE_PATHS, "access_evidence_digest"],
    ids=[
        "issuer",
        "writer-login",
        "writer-role",
        "repository-subject",
        "repository-access",
        "package-subject",
        "package-access",
        "manage-actions-subject",
        "manage-actions-access",
        "limitation",
        "digest-alternative",
    ],
)
@pytest.mark.parametrize(
    "whitespace",
    _WHITESPACE_ONLY_VALUES,
    ids=["space", "tab", "newline", "crlf", "unicode-nbsp"],
)
def test_attestation_rejects_whitespace_only_human_evidence_strings(
    position: str,
    whitespace: str,
) -> None:
    """Reject blank human evidence without a canonical-JSON false positive."""
    document = _attestation_document_with_human_evidence(
        position,
        whitespace,
    )

    with pytest.raises(
        TypeError,
        match=_HUMAN_EVIDENCE_MESSAGES[position],
    ):
        parse_governance_attestation(canonicalize(document))


@pytest.mark.parametrize(
    "category",
    ["repository", "package", "manage_actions"],
    ids=["repository", "package", "manage-actions"],
)
@pytest.mark.parametrize(
    "whitespace",
    _WHITESPACE_ONLY_VALUES,
    ids=["space", "tab", "newline", "crlf", "unicode-nbsp"],
)
def test_attestation_rejects_unknown_access_inventory_value_member(
    category: str,
    whitespace: str,
) -> None:
    """Reject adjudicated value wording as an unknown schema member."""
    document = _attestation_document()
    access_inventory = document["access_inventory"]
    assert isinstance(access_inventory, dict)
    grants = access_inventory[category]
    assert isinstance(grants, list)
    grant = grants[0]
    assert isinstance(grant, dict)
    grant["value"] = whitespace

    with pytest.raises(
        ValueError,
        match=(rf"access_inventory\.{category}\[0\] unknown field: value"),
    ):
        parse_governance_attestation(canonicalize(document))


@pytest.mark.parametrize(
    "position",
    _PRESERVED_HUMAN_EVIDENCE_POSITIONS,
    ids=[
        "issuer",
        "writer-login",
        "repository-subject",
        "repository-access",
        "package-subject",
        "package-access",
        "manage-actions-subject",
        "manage-actions-access",
        "limitation",
        "digest-alternative",
        "writer-role",
    ],
)
def test_attestation_preserves_exact_human_evidence_strings(
    position: str,
) -> None:
    """Validate open strings without trimming or otherwise normalizing them."""
    if position == "access_evidence_digest":
        value = "sha256:" + ("7" * 64)
    elif position == "accepted_writers[0].role":
        value = "Admin"
    else:
        value = " \tAudited human evidence\u00a0 \n"
    document = _attestation_document_with_human_evidence(position, value)

    attestation = parse_governance_attestation(canonicalize(document))

    if position == "issuer":
        assert attestation.issuer == value
    elif position == "accepted_writers[0].login":
        assert attestation.accepted_writers[0].login == value
    elif position == "accepted_writers[0].role":
        assert attestation.accepted_writers[0].role == value
    elif position == "limitations[0]":
        assert attestation.limitations[0] == value
    elif position == "access_evidence_digest":
        assert attestation.access_evidence_digest == value
        assert attestation.access_inventory is None
    else:
        inventory = attestation.access_inventory
        assert inventory is not None
        category = position.split(".")[1].removesuffix("[0]")
        member = position.rsplit(".", maxsplit=1)[1]
        assert getattr(getattr(inventory, category)[0], member) == value
    assert attestation.to_document() == document
    assert attestation.live_enabled is False


@pytest.mark.parametrize(
    "field",
    _FIXED_CANONICAL_VALUES,
    ids=["schema", "release-policy", "package", "writer-role"],
)
@pytest.mark.parametrize(
    "variation",
    ["leading-space", "trailing-space", "case-changed"],
)
def test_attestation_keeps_fixed_canonical_values_exact(
    field: str,
    variation: str,
) -> None:
    """Accept only exact fixed values without trimming or case folding."""
    canonical_value = _FIXED_CANONICAL_VALUES[field]
    accepted_document = _attestation_document()
    accepted = parse_governance_attestation(canonicalize(accepted_document))
    if field == "accepted_writers[0].role":
        assert accepted.accepted_writers[0].role == canonical_value
    else:
        assert accepted.to_document()[field] == canonical_value
    assert (
        accepted.to_document()["inspected_at"]
        == (accepted_document["inspected_at"])
    )
    assert (
        accepted.to_document()["expires_at"]
        == (accepted_document["expires_at"])
    )

    if variation == "leading-space":
        rejected_value = f" {canonical_value}"
    elif variation == "trailing-space":
        rejected_value = f"{canonical_value} "
    else:
        rejected_value = canonical_value.swapcase()
    rejected_document = _attestation_document()
    _replace_attestation_member(
        rejected_document,
        _FIXED_CANONICAL_PATHS[field],
        rejected_value,
    )
    message = (
        "role is not accepted"
        if field == "accepted_writers[0].role"
        else (
            "wrong schema"
            if field == "schema"
            else "policy/package binding mismatch"
        )
    )

    with pytest.raises(ValueError, match=message):
        parse_governance_attestation(canonicalize(rejected_document))
