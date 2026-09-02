"""Current public-shape contracts for Live Eligibility."""

from __future__ import annotations

import hashlib
import inspect
import json
import subprocess
from collections.abc import Callable
from dataclasses import fields, replace
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
from three_workflow_delivery_v3.release import eligibility
from three_workflow_delivery_v3.release.eligibility import (
    AdmittedLiveEligibilityDecision,
    EligibilityResult,
    GovernanceBlob,
    LiveEligibilityAdmissionMode,
    LiveEligibilityContext,
    LiveEligibilityDecision,
    admit_live_eligibility_decision,
    evaluate_live_eligibility,
    observe_governance_source,
    parse_governance_attestation,
    release_policy_digest,
)
from three_workflow_delivery_v3.release.static_reference_model import (
    STATIC_REFERENCE_ERROR_KINDS,
    STATIC_REFERENCE_POLICY_ID,
    BoundedStaticReferenceResult,
    StaticReferenceFinding,
    parse_bounded_static_reference_result,
)
from three_workflow_delivery_v3.release.static_reference_policy import (
    STATIC_REFERENCE_POLICY_DIGEST,
)
from three_workflow_delivery_v3.repository.compiler import (
    CompilationContext,
    FactBundleAdmissionContext,
    RepositoryModelSnapshot,
    admit_node_provider_fact_bundle,
    compile_repository_model,
    first_slice_provider_manifest,
    provider_binding,
)
from three_workflow_delivery_v3.repository.descriptors import (
    FIRST_SLICE_PACKAGE,
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
    from three_workflow_delivery_v3.records.release import ReleaseIntent
    from three_workflow_delivery_v3.repository.compiler import (
        AdmittedRepositoryModelSnapshot,
    )
    from three_workflow_delivery_v3.repository.descriptors import ReleasePolicy


def test_live_eligibility_api_owns_static_reference_input() -> None:
    """Accept the repository root, not a caller-formed policy Result."""
    parameters = inspect.signature(evaluate_live_eligibility).parameters

    assert tuple(parameters) == (
        "context",
        "snapshot",
        "policy",
        "client",
        "repository_root",
        "now",
    )
    assert parameters["repository_root"].kind is (
        inspect.Parameter.KEYWORD_ONLY
    )
    assert parameters["now"].kind is inspect.Parameter.KEYWORD_ONLY
    assert "consumer_policy" not in parameters


def test_live_eligibility_decision_names_static_reference_evidence() -> None:
    """Make the bounded Result a first-class immutable Decision field."""
    assert tuple(field.name for field in fields(LiveEligibilityDecision)) == (
        "context",
        "static_reference",
        "governance",
        "result",
        "diagnostics",
    )
    assert tuple(
        field.name for field in fields(AdmittedLiveEligibilityDecision)
    ) == (
        "context",
        "static_reference",
        "governance",
        "result",
        "diagnostics",
        "canonical_digest",
        "canonical_bytes",
    )


def test_live_eligibility_runtime_has_no_consumer_policy_symbols() -> None:
    """Do not retain a hidden compatibility shim in the evaluator module."""
    module = inspect.getmodule(evaluate_live_eligibility)

    assert module is not None
    assert not hasattr(module, "ConsumerPolicyResult")
    assert not hasattr(module, "CONSUMER_POLICY_ID")
    assert not hasattr(module, "validate_consumer_policy_result")


REPO_ROOT = Path(__file__).resolve().parents[6]
PRODUCT_PATH = "src/public/lib/hcoona-release-smoke-npm"
FIXTURES = Path(__file__).resolve().parents[1] / "fixtures" / "release"
TARGET = "e" * 40
GOVERNANCE_COMMIT = "f" * 40
GOVERNANCE_BLOB = "b" * 40
NOW = datetime(2026, 8, 6, 12, 0, 0, tzinfo=UTC)
WORKFLOW_RUN_ID = 7101
RUN_ATTEMPT = 3
PREFIXED_SHA256_LENGTH = 71
FRESH_SOURCE_CALL_COUNT = 3
EXPECTED_STATIC_REFERENCE_ERROR_KINDS = (
    "source-acquisition-failed",
    "encoding-rejected",
    "authority-rejected",
    "authority-execution-failed",
    "unsupported-projection",
    "authority-mismatch",
    "cleanup-failed",
)
LIVE_STATIC_REFERENCE_IMPLEMENTATIONS = (
    "@npmcli/package-json@8.0.0",
    "@pnpm/deps.path@1101.0.1",
    "@pnpm/lockfile.fs@1100.2.5",
    "@pnpm/lockfile.utils@1102.1.0",
    "@pnpm/resolving.npm-resolver@1104.1.0",
    "@pnpm/workspace.spec-parser@1100.0.1",
    "@pnpm/workspace.workspace-manifest-reader@1100.1.8",
    "NuGet.Packaging@7.9.0",
    "NuGet.ProjectModel@7.9.0",
    "dotnet-runtime@10.0.8",
    "node@24.19.0",
    "npm-package-arg@14.0.0",
)

type SnapshotMutation = Callable[
    [RepositoryModelSnapshot], RepositoryModelSnapshot
]
type LiveContextMutation = Callable[
    [LiveEligibilityContext], LiveEligibilityContext
]
type CompilationContextMutation = Callable[
    [CompilationContext], CompilationContext
]
type GovernanceSourceMutation = Callable[[GovernanceSource], GovernanceSource]
type DecisionMutation = Callable[[dict[str, JsonValue]], None]
type StaticReferenceDocumentMutation = Callable[[dict[str, JsonValue]], None]


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


def _compiled_snapshot(tmp_path: Path) -> RepositoryModelSnapshot:
    repo, target = _target_authoring_repo(tmp_path)
    context = CompilationContext(
        request_id="release-request-42",
        purpose="live-release",
        workflow_run_id=WORKFLOW_RUN_ID,
        run_attempt=None,
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
        ("git", "show", f"{target}:{PRODUCT_PATH}/package.json"),  # noqa: S607
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
                        ("git", "show", f"{target}:{path}"),  # noqa: S607
                        cwd=repo,
                        check=True,
                        capture_output=True,
                    ).stdout
                ).hexdigest()
            ),
            project_ids=(FIRST_SLICE_PACKAGE,),
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
        toolchain=(("node", "v24.14.0"), ("pnpm", "11.21.0")),
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
                project_id=FIRST_SLICE_PACKAGE,
                package_name=FIRST_SLICE_PACKAGE,
                path=PRODUCT_PATH,
                manifest_path=f"{PRODUCT_PATH}/package.json",
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


def _attestation_document(**updates: JsonValue) -> dict[str, JsonValue]:
    document = parse_canonical_json(
        (FIXTURES / "governance-disabled.json").read_bytes()
    )
    document.update(updates)
    return document


def _attestation_content(**updates: JsonValue) -> bytes:
    return canonicalize(_attestation_document(**updates))


def _static_reference(
    *,
    target: str = TARGET,
    findings: tuple[StaticReferenceFinding, ...] = (),
    error_kind: str | None = None,
    policy_digest: str = STATIC_REFERENCE_POLICY_DIGEST,
    implementation_identities: tuple[str, ...] | None = None,
) -> BoundedStaticReferenceResult:
    return BoundedStaticReferenceResult(
        source_kind="git-target",
        target=target,
        policy_id=STATIC_REFERENCE_POLICY_ID,
        policy_digest=policy_digest,
        implementation_identities=(
            implementation_identities
            if implementation_identities is not None
            else (
                LIVE_STATIC_REFERENCE_IMPLEMENTATIONS
                if error_kind is None
                else ()
            )
        ),
        findings=findings,
        error_kind=error_kind,
    )


def _finding(
    *,
    path: str = "src/public/app/consumer/package.json",
    prohibited_form: str = "D",
) -> StaticReferenceFinding:
    return StaticReferenceFinding(
        path=path,
        family="npm-manifest",
        context="dependencies",
        prohibited_form=prohibited_form,
        matched_identity=FIRST_SLICE_PACKAGE,
        location=f"dependencies.{FIRST_SLICE_PACKAGE}",
    )


def _context(
    snapshot: RepositoryModelSnapshot,
    policy: ReleasePolicy,
    *,
    selected_ref: str = "refs/heads/feature/workflow-delivery-v3",
) -> LiveEligibilityContext:
    return LiveEligibilityContext(
        purpose="live-release",
        request_id=snapshot.context.request_id,
        workflow_run_id=snapshot.context.workflow_run_id,
        run_attempt=RUN_ATTEMPT,
        selected_ref=selected_ref,
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
        self.scan_calls: list[tuple[Path, str, str]] = []

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
    monkeypatch: pytest.MonkeyPatch,
    client: RecordingGovernanceClient,
    *,
    snapshot: RepositoryModelSnapshot,
    policy: ReleasePolicy,
    static_reference: BoundedStaticReferenceResult | None = None,
    context: LiveEligibilityContext | None = None,
    context_mutation: LiveContextMutation | None = None,
    now: datetime = NOW,
) -> LiveEligibilityDecision:
    selected_context = context or _context(snapshot, policy)
    if context_mutation is not None:
        selected_context = context_mutation(selected_context)
    selected_result = static_reference or _static_reference(
        target=selected_context.target
    )

    def scan(
        repository_root: Path,
        *,
        source_kind: str,
        target: str,
    ) -> BoundedStaticReferenceResult:
        client.scan_calls.append((repository_root, source_kind, target))
        return selected_result

    monkeypatch.setattr(
        eligibility,
        "scan_bounded_static_references",
        scan,
    )
    return evaluate_live_eligibility(
        selected_context,
        snapshot,
        policy,
        client,
        repository_root=REPO_ROOT,
        now=now,
    )


def _transport_decision(
    live_intent: ReleaseIntent,
    live_admitted_repository_model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
    *,
    issuer: str | None = None,
) -> LiveEligibilityDecision:
    model = live_admitted_repository_model.snapshot
    context = LiveEligibilityContext(
        purpose=live_intent.purpose,
        request_id=live_intent.request_id,
        workflow_run_id=live_intent.workflow_run_id,
        run_attempt=live_intent.run_attempt,
        selected_ref=live_intent.selected_ref,
        target=live_intent.target,
        repository_model_digest=(
            live_admitted_repository_model.canonical_digest
        ),
        producer="evaluate-live-eligibility",
        control=model.context.control,
        release_policy_digest=release_policy_digest(policy),
        catalog_digest=catalog_digest(),
    )
    updates: dict[str, JsonValue] = {"live_enabled": True}
    if issuer is not None:
        updates["issuer"] = issuer
    client = RecordingGovernanceClient(_attestation_content(**updates))
    with pytest.MonkeyPatch.context() as monkeypatch:
        return _evaluate(
            monkeypatch,
            client,
            snapshot=model,
            policy=policy,
            static_reference=_static_reference(target=live_intent.target),
            context=context,
        )


def _set_decision_path(
    document: dict[str, JsonValue],
    path: tuple[str, ...],
    value: JsonValue,
) -> None:
    selected = document
    for name in path[:-1]:
        child = selected[name]
        if not isinstance(child, dict):
            message = f"{'.'.join(path[:-1])} is not an object"
            raise TypeError(message)
        selected = child
    selected[path[-1]] = value


def _make_static_reference_finding(
    document: dict[str, JsonValue],
) -> None:
    context = _object_member(document, "context")
    target = context["target"]
    assert isinstance(target, str)
    document["static-reference"] = _static_reference(
        target=target,
        findings=(_finding(),),
    ).to_document()


def _make_blocked_with_diagnostic(
    document: dict[str, JsonValue],
) -> None:
    document["result"] = "blocked"
    document["diagnostics"] = ["governance-live-disabled"]


def _admit_mutated_decision(  # noqa: PLR0913
    document: dict[str, JsonValue],
    *,
    live_intent: ReleaseIntent,
    live_admitted_repository_model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
    admission_mode: LiveEligibilityAdmissionMode = (
        LiveEligibilityAdmissionMode.CURRENT_FRESHNESS
    ),
    now: datetime = NOW,
) -> AdmittedLiveEligibilityDecision:
    content = canonicalize(document)
    return admit_live_eligibility_decision(
        content,
        intent=live_intent,
        repository_model=live_admitted_repository_model,
        policy=policy,
        expected_digest=canonical_sha256(document),
        admission_mode=admission_mode,
        now=now,
    )


def test_evaluator_output_round_trips_through_strict_live_admission(
    live_intent: ReleaseIntent,
    live_admitted_repository_model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
) -> None:
    """Admit the evaluator's complete canonical current-attempt output."""
    decision = _transport_decision(
        live_intent,
        live_admitted_repository_model,
        policy,
    )
    content = canonicalize(decision.to_document())

    admitted = admit_live_eligibility_decision(
        content,
        intent=live_intent,
        repository_model=live_admitted_repository_model,
        policy=policy,
        expected_digest=decision.decision_digest,
        admission_mode=LiveEligibilityAdmissionMode.CURRENT_FRESHNESS,
        now=NOW,
    )

    assert admitted.to_document() == decision.to_document()
    assert admitted.canonical_bytes == content
    assert admitted.decision_digest == decision.decision_digest
    assert admitted.static_reference.result_digest == (
        decision.static_reference.result_digest
    )
    assert admitted.static_reference.source_kind == "git-target"
    assert admitted.static_reference.target == live_intent.target
    assert admitted.governance.provenance == (
        ("blob-oid", GOVERNANCE_BLOB),
        (
            "content-sha256",
            decision.governance.content_sha256,
        ),
        ("path", GOVERNANCE_PATH),
        ("ref", GOVERNANCE_REF),
        ("repository", GOVERNANCE_REPOSITORY),
        ("resolved-commit", GOVERNANCE_COMMIT),
    )


@pytest.mark.parametrize(
    "admission_mode",
    [
        LiveEligibilityAdmissionMode.CURRENT_FRESHNESS,
        LiveEligibilityAdmissionMode.CAPABILITY_REPLAY,
    ],
)
def test_whitespace_bearing_issuer_round_trips_through_admission(
    admission_mode: LiveEligibilityAdmissionMode,
    live_intent: ReleaseIntent,
    live_admitted_repository_model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
) -> None:
    """Preserve valid nonblank human issuer evidence in every mode."""
    issuer = "  reviewed issuer  "
    decision = _transport_decision(
        live_intent,
        live_admitted_repository_model,
        policy,
        issuer=issuer,
    )

    admitted = admit_live_eligibility_decision(
        canonicalize(decision.to_document()),
        intent=live_intent,
        repository_model=live_admitted_repository_model,
        policy=policy,
        expected_digest=decision.decision_digest,
        admission_mode=admission_mode,
        now=NOW,
    )

    assert admitted.to_document() == decision.to_document()
    assert admitted.governance.issuer == issuer


@pytest.mark.parametrize(
    "admitted_at",
    [
        pytest.param(
            datetime(2026, 10, 1, tzinfo=UTC),
            id="exact-expiry",
        ),
        pytest.param(
            datetime(2026, 10, 1, 0, 0, 1, tzinfo=UTC),
            id="after-expiry",
        ),
    ],
)
def test_pre_attempt_admission_rejects_at_and_after_current_expiry(
    admitted_at: datetime,
    live_intent: ReleaseIntent,
    live_admitted_repository_model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
) -> None:
    """Keep initial and bind-time admission strict at the expiry boundary."""
    decision = _transport_decision(
        live_intent,
        live_admitted_repository_model,
        policy,
    )

    with pytest.raises(ValueError, match="not fresh and enabled"):
        admit_live_eligibility_decision(
            canonicalize(decision.to_document()),
            intent=live_intent,
            repository_model=live_admitted_repository_model,
            policy=policy,
            expected_digest=decision.decision_digest,
            admission_mode=(LiveEligibilityAdmissionMode.CURRENT_FRESHNESS),
            now=admitted_at,
        )


def test_capability_replay_accepts_originally_valid_decision_after_expiry(
    live_intent: ReleaseIntent,
    live_admitted_repository_model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
) -> None:
    """Replay expired immutable authority so capability can reobserve it."""
    decision = _transport_decision(
        live_intent,
        live_admitted_repository_model,
        policy,
    )
    admitted_at = datetime(2026, 10, 1, 0, 0, 1, tzinfo=UTC)

    admitted = admit_live_eligibility_decision(
        canonicalize(decision.to_document()),
        intent=live_intent,
        repository_model=live_admitted_repository_model,
        policy=policy,
        expected_digest=decision.decision_digest,
        admission_mode=LiveEligibilityAdmissionMode.CAPABILITY_REPLAY,
        now=admitted_at,
    )

    assert admitted.to_document() == decision.to_document()
    assert (
        admitted.governance.inspected_at
        <= admitted.governance.observed_at
        < admitted.governance.expires_at
        < admitted_at
    )


@pytest.mark.parametrize(
    "admission_mode",
    [
        LiveEligibilityAdmissionMode.CURRENT_FRESHNESS,
        LiveEligibilityAdmissionMode.CAPABILITY_REPLAY,
    ],
)
def test_admission_rejects_observation_at_original_expiry_in_every_mode(
    admission_mode: LiveEligibilityAdmissionMode,
    live_intent: ReleaseIntent,
    live_admitted_repository_model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
) -> None:
    """Reject a Decision that was already expired when originally observed."""
    document = _transport_decision(
        live_intent,
        live_admitted_repository_model,
        policy,
    ).to_document()
    _set_decision_path(
        document,
        ("governance", "observed-at"),
        "2026-10-01T00:00:00Z",
    )

    with pytest.raises(ValueError, match="not fresh and enabled"):
        _admit_mutated_decision(
            document,
            live_intent=live_intent,
            live_admitted_repository_model=live_admitted_repository_model,
            policy=policy,
            admission_mode=admission_mode,
            now=datetime(2026, 10, 1, 0, 0, 1, tzinfo=UTC),
        )


@pytest.mark.parametrize(
    ("path", "value", "message"),
    [
        pytest.param(
            ("context", "target"),
            "d" * 40,
            "current lineage mismatch",
            id="lineage",
        ),
        pytest.param(
            ("static-reference", "policy-id"),
            "other-policy",
            "policy ID is not current",
            id="static-reference-policy",
        ),
        pytest.param(
            ("governance", "repository"),
            "other/repository",
            "exact fixed contract",
            id="governance-source",
        ),
    ],
)
def test_capability_replay_rejects_representative_semantic_substitutions(  # noqa: PLR0913, PLR0917
    path: tuple[str, ...],
    value: JsonValue,
    message: str,
    live_intent: ReleaseIntent,
    live_admitted_repository_model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
) -> None:
    """Keep all immutable semantic validation active during replay."""
    document = _transport_decision(
        live_intent,
        live_admitted_repository_model,
        policy,
    ).to_document()
    _set_decision_path(document, path, value)

    with pytest.raises((TypeError, ValueError), match=message):
        _admit_mutated_decision(
            document,
            live_intent=live_intent,
            live_admitted_repository_model=live_admitted_repository_model,
            policy=policy,
            admission_mode=LiveEligibilityAdmissionMode.CAPABILITY_REPLAY,
            now=datetime(2026, 10, 1, 0, 0, 1, tzinfo=UTC),
        )


@pytest.mark.parametrize(
    "admission_mode",
    [
        LiveEligibilityAdmissionMode.CURRENT_FRESHNESS,
        LiveEligibilityAdmissionMode.CAPABILITY_REPLAY,
    ],
)
def test_artifact_cannot_select_live_eligibility_admission_mode(
    admission_mode: LiveEligibilityAdmissionMode,
    live_intent: ReleaseIntent,
    live_admitted_repository_model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
) -> None:
    """Keep lifecycle admission mode outside the closed immutable artifact."""
    document = _transport_decision(
        live_intent,
        live_admitted_repository_model,
        policy,
    ).to_document()
    document["admission-mode"] = "capability-replay"

    with pytest.raises(ValueError, match="unknown field: admission-mode"):
        _admit_mutated_decision(
            document,
            live_intent=live_intent,
            live_admitted_repository_model=live_admitted_repository_model,
            policy=policy,
            admission_mode=admission_mode,
        )


@pytest.mark.parametrize(
    ("path", "value"),
    [
        pytest.param(
            ("context", "purpose"),
            "release-simulation",
            id="cross-purpose",
        ),
        pytest.param(
            ("context", "request-id"),
            "release-request:" + ("1" * 64),
            id="cross-request",
        ),
        pytest.param(
            ("context", "workflow-run-id"),
            7300,
            id="prior-run",
        ),
        pytest.param(
            ("context", "run-attempt"),
            2,
            id="prior-attempt",
        ),
        pytest.param(
            ("context", "selected-ref"),
            "refs/heads/other",
            id="cross-ref",
        ),
        pytest.param(
            ("context", "target"),
            "d" * 40,
            id="cross-target",
        ),
        pytest.param(
            ("context", "repository-model-digest"),
            "sha256:" + ("9" * 64),
            id="cross-model",
        ),
        pytest.param(
            ("context", "producer"),
            "other-producer",
            id="producer",
        ),
        pytest.param(
            ("context", "control"),
            "workflow-delivery-v3:" + ("d" * 40),
            id="control",
        ),
        pytest.param(
            ("context", "release-policy-digest"),
            "sha256:" + ("8" * 64),
            id="release-policy",
        ),
        pytest.param(
            ("context", "catalog-digest"),
            "sha256:" + ("7" * 64),
            id="catalog",
        ),
    ],
)
def test_live_admission_rejects_each_current_lineage_mutation(
    path: tuple[str, ...],
    value: JsonValue,
    live_intent: ReleaseIntent,
    live_admitted_repository_model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
) -> None:
    """Reject prior, cross-request, cross-target, and substituted lineage."""
    document = _transport_decision(
        live_intent,
        live_admitted_repository_model,
        policy,
    ).to_document()
    _set_decision_path(document, path, value)

    with pytest.raises(ValueError, match="current lineage mismatch"):
        _admit_mutated_decision(
            document,
            live_intent=live_intent,
            live_admitted_repository_model=live_admitted_repository_model,
            policy=policy,
        )


@pytest.mark.parametrize(
    ("path", "value", "message"),
    [
        pytest.param(
            ("static-reference", "target"),
            "d" * 40,
            "static-reference target mismatch",
            id="static-reference-target",
        ),
        pytest.param(
            ("static-reference", "policy-id"),
            "other-policy",
            "policy ID is not current",
            id="static-reference-policy-id",
        ),
        pytest.param(
            ("static-reference", "policy-digest"),
            "sha256:" + ("0" * 64),
            "policy is not current",
            id="static-reference-policy-digest",
        ),
        pytest.param(
            ("governance", "repository"),
            "other/repository",
            "exact fixed contract",
            id="governance-repository",
        ),
        pytest.param(
            ("governance", "ref"),
            "refs/heads/other",
            "exact fixed contract",
            id="governance-ref",
        ),
        pytest.param(
            ("governance", "path"),
            ".github/other.json",
            "exact fixed contract",
            id="governance-path",
        ),
        pytest.param(
            ("governance", "max-age-days"),
            89,
            "exact fixed contract",
            id="governance-max-age",
        ),
        pytest.param(
            ("governance", "resolved-commit"),
            "f" * 39,
            "full commit SHA",
            id="governance-commit",
        ),
        pytest.param(
            ("governance", "blob-oid"),
            "b" * 39,
            "blob OID is malformed",
            id="governance-blob",
        ),
        pytest.param(
            ("governance", "content-sha256"),
            "sha256:" + ("9" * 64),
            "attestation identity mismatch",
            id="governance-content-identity",
        ),
        pytest.param(
            ("governance", "attestation-content-digest"),
            "sha256:" + ("8" * 64),
            "attestation identity mismatch",
            id="governance-attestation-identity",
        ),
        pytest.param(
            ("governance", "live-enabled"),
            False,
            "not fresh and enabled",
            id="governance-disabled",
        ),
        pytest.param(
            ("governance", "inspected-at"),
            "2026-08-07T00:00:00Z",
            "not fresh and enabled",
            id="governance-not-yet-valid",
        ),
        pytest.param(
            ("governance", "observed-at"),
            "2026-08-07T00:00:00Z",
            "not fresh and enabled",
            id="governance-future-observation",
        ),
        pytest.param(
            ("governance", "expires-at"),
            "2026-08-06T12:00:00Z",
            "not fresh and enabled",
            id="governance-expired",
        ),
    ],
)
def test_live_admission_rejects_static_reference_and_governance_mutations(  # noqa: PLR0913, PLR0917
    path: tuple[str, ...],
    value: JsonValue,
    message: str,
    live_intent: ReleaseIntent,
    live_admitted_repository_model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
) -> None:
    """Reject bounded static-reference and Governance substitutions."""
    document = _transport_decision(
        live_intent,
        live_admitted_repository_model,
        policy,
    ).to_document()
    _set_decision_path(document, path, value)

    with pytest.raises((TypeError, ValueError), match=message):
        _admit_mutated_decision(
            document,
            live_intent=live_intent,
            live_admitted_repository_model=live_admitted_repository_model,
            policy=policy,
        )


@pytest.mark.parametrize(
    ("mutation", "message"),
    [
        pytest.param(
            _make_static_reference_finding,
            "not a closed passing decision",
            id="passing-with-static-reference-finding",
        ),
        pytest.param(
            _make_blocked_with_diagnostic,
            "not a closed passing decision",
            id="blocked-with-diagnostic",
        ),
    ],
)
def test_live_admission_requires_a_diagnostic_free_static_reference_clean_pass(
    mutation: DecisionMutation,
    message: str,
    live_intent: ReleaseIntent,
    live_admitted_repository_model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
) -> None:
    """Require pass, no diagnostics, and clean exact-target evidence."""
    document = _transport_decision(
        live_intent,
        live_admitted_repository_model,
        policy,
    ).to_document()
    mutation(document)

    with pytest.raises(ValueError, match=message):
        _admit_mutated_decision(
            document,
            live_intent=live_intent,
            live_admitted_repository_model=live_admitted_repository_model,
            policy=policy,
        )


def test_live_admission_rejects_hash_consistent_wrong_policy_digest(
    live_intent: ReleaseIntent,
    live_admitted_repository_model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
) -> None:
    """Reject an obsolete static-reference policy through every hash layer."""
    document = _transport_decision(
        live_intent,
        live_admitted_repository_model,
        policy,
    ).to_document()
    static_reference = _object_member(document, "static-reference")
    static_reference["policy-digest"] = "sha256:" + ("0" * 64)

    with pytest.raises(ValueError, match="policy is not current"):
        _admit_mutated_decision(
            document,
            live_intent=live_intent,
            live_admitted_repository_model=live_admitted_repository_model,
            policy=policy,
        )


@pytest.mark.parametrize(
    "implementation_identities",
    [
        pytest.param((), id="empty"),
        pytest.param(
            (
                "NuGet.Packaging@7.9.0",
                "NuGet.ProjectModel@7.9.0",
                "dotnet-runtime@10.0.8",
            ),
            id="nuget-only",
        ),
        pytest.param(
            (
                "@npmcli/package-json@8.0.0",
                "@pnpm/deps.path@1101.0.1",
                "@pnpm/lockfile.fs@1100.2.5",
                "@pnpm/lockfile.utils@1102.1.0",
                "@pnpm/resolving.npm-resolver@1104.1.0",
                "@pnpm/workspace.spec-parser@1100.0.1",
                "@pnpm/workspace.workspace-manifest-reader@1100.1.8",
                "node@24.19.0",
                "npm-package-arg@14.0.0",
            ),
            id="node-only",
        ),
    ],
)
def test_live_admission_requires_mandatory_authority_implementations(
    implementation_identities: tuple[str, ...],
    live_intent: ReleaseIntent,
    live_admitted_repository_model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
) -> None:
    """Reject a hash-consistent pass without every mandatory Live graph."""
    document = _transport_decision(
        live_intent,
        live_admitted_repository_model,
        policy,
    ).to_document()
    static_reference = _object_member(document, "static-reference")
    static_reference["implementation-identities"] = list(
        implementation_identities
    )

    with pytest.raises(ValueError, match="implementations are incomplete"):
        _admit_mutated_decision(
            document,
            live_intent=live_intent,
            live_admitted_repository_model=live_admitted_repository_model,
            policy=policy,
        )


@pytest.mark.parametrize(
    ("path", "value", "message"),
    [
        pytest.param(
            ("context", "workflow-run-id"),
            True,
            "positive integer",
            id="boolean-run",
        ),
        pytest.param(
            ("context", "target"),
            1,
            "nonempty exact string",
            id="numeric-target",
        ),
        pytest.param(
            ("static-reference", "findings"),
            FIRST_SLICE_PACKAGE,
            "must be an array",
            id="scalar-findings",
        ),
        pytest.param(
            ("governance", "max-age-days"),
            "90",
            "positive integer",
            id="string-max-age",
        ),
        pytest.param(
            ("governance", "live-enabled"),
            "true",
            "must be Boolean",
            id="string-live-enabled",
        ),
        pytest.param(
            ("diagnostics",),
            [1],
            "nonempty exact string",
            id="numeric-diagnostic",
        ),
    ],
)
def test_live_admission_rejects_malformed_primitives(  # noqa: PLR0913, PLR0917
    path: tuple[str, ...],
    value: JsonValue,
    message: str,
    live_intent: ReleaseIntent,
    live_admitted_repository_model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
) -> None:
    """Reject Boolean, numeric, scalar, and string coercion substitutes."""
    document = _transport_decision(
        live_intent,
        live_admitted_repository_model,
        policy,
    ).to_document()
    _set_decision_path(document, path, value)

    with pytest.raises((TypeError, ValueError), match=message):
        _admit_mutated_decision(
            document,
            live_intent=live_intent,
            live_admitted_repository_model=live_admitted_repository_model,
            policy=policy,
        )


@pytest.mark.parametrize(
    ("container", "member", "operation", "message"),
    [
        pytest.param(
            (),
            "context",
            "missing",
            "missing required field",
            id="missing-top-level",
        ),
        pytest.param(
            (),
            "unexpected",
            "unknown",
            "unknown field",
            id="unknown-top-level",
        ),
        pytest.param(
            ("context",),
            "request-id",
            "missing",
            "missing required field",
            id="missing-context",
        ),
        pytest.param(
            ("context",),
            "unexpected",
            "unknown",
            "unknown field",
            id="unknown-context",
        ),
        pytest.param(
            ("static-reference",),
            "policy-digest",
            "missing",
            "Result fields are not exact",
            id="missing-static-reference",
        ),
        pytest.param(
            ("static-reference",),
            "unexpected",
            "unknown",
            "Result fields are not exact",
            id="unknown-static-reference",
        ),
        pytest.param(
            ("governance",),
            "attestation-content-digest",
            "missing",
            "missing required field",
            id="missing-governance",
        ),
        pytest.param(
            ("governance",),
            "unexpected",
            "unknown",
            "unknown field",
            id="unknown-governance",
        ),
    ],
)
def test_live_admission_closes_every_nested_schema(  # noqa: PLR0913, PLR0917
    container: tuple[str, ...],
    member: str,
    operation: str,
    message: str,
    live_intent: ReleaseIntent,
    live_admitted_repository_model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
) -> None:
    """Reject missing and unknown members at every Decision object layer."""
    document = _transport_decision(
        live_intent,
        live_admitted_repository_model,
        policy,
    ).to_document()
    selected = document
    for name in container:
        child = selected[name]
        if not isinstance(child, dict):
            error = f"{name} is not an object"
            raise TypeError(error)
        selected = child
    if operation == "missing":
        del selected[member]
    else:
        selected[member] = "unexpected"

    with pytest.raises(ValueError, match=message):
        _admit_mutated_decision(
            document,
            live_intent=live_intent,
            live_admitted_repository_model=live_admitted_repository_model,
            policy=policy,
        )


def test_live_admission_rejects_minimal_pass_payload(
    live_intent: ReleaseIntent,
    live_admitted_repository_model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
) -> None:
    """Reject the former minimal result-only payload."""
    document: dict[str, JsonValue] = {"result": "pass"}

    with pytest.raises(ValueError, match="missing required field"):
        _admit_mutated_decision(
            document,
            live_intent=live_intent,
            live_admitted_repository_model=live_admitted_repository_model,
            policy=policy,
        )


def test_live_eligibility_passes_with_fresh_exact_target_inputs(
    monkeypatch: pytest.MonkeyPatch,
    live_admitted_repository_model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
) -> None:
    """Pass only current exact-target inputs and a fresh enabled attestation."""
    snapshot = live_admitted_repository_model.snapshot
    client = RecordingGovernanceClient(_attestation_content(live_enabled=True))

    decision = _evaluate(
        monkeypatch,
        client,
        snapshot=snapshot,
        policy=policy,
    )

    assert decision.result is EligibilityResult.PASS
    assert decision.diagnostics == ()
    assert decision.context.purpose == "live-release"
    assert decision.context.request_id == snapshot.context.request_id
    assert decision.context.workflow_run_id == snapshot.context.workflow_run_id
    assert decision.context.run_attempt == RUN_ATTEMPT
    assert decision.context.target == TARGET
    assert decision.static_reference.source_kind == "git-target"
    assert decision.static_reference.target == TARGET
    assert decision.static_reference.result == "clean"
    assert decision.governance.attestation.live_enabled is True
    assert decision.decision_digest.startswith("sha256:")
    assert client.scan_calls == [(REPO_ROOT, "git-target", TARGET)]
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
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    """Bind the Decision to the compiler's complete first-slice Snapshot."""
    snapshot = _compiled_snapshot(tmp_path)
    policy = _policy()
    client = RecordingGovernanceClient(_attestation_content(live_enabled=True))

    decision = _evaluate(
        monkeypatch,
        client,
        snapshot=snapshot,
        policy=policy,
    )

    assert decision.result is EligibilityResult.PASS
    assert decision.context.repository_model_digest == snapshot.snapshot_digest
    assert decision.static_reference.target == snapshot.context.target
    assert client.scan_calls == [
        (REPO_ROOT, "git-target", snapshot.context.target)
    ]
    assert snapshot.release_units[0].release_unit == (
        "hcoona-release-smoke-npm"
    )
    assert snapshot.release_units[0].builds[0].outputs[0].output_id == (
        "npm-tarball"
    )
    context = _object_member(decision.to_document(), "context")
    assert context["repository-model-digest"] == snapshot.snapshot_digest


@pytest.mark.parametrize(
    ("context_mutation", "field_name"),
    [
        (_with_snapshot_channel, "channel"),
        (_with_snapshot_release_unit, "release_unit"),
    ],
    ids=["channel-set", "release-unit-set"],
)
def test_live_eligibility_rejects_live_snapshot_with_selection(
    monkeypatch: pytest.MonkeyPatch,
    live_admitted_repository_model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
    context_mutation: CompilationContextMutation,
    field_name: str,
) -> None:
    """Reject live Repository Model contexts carrying simulation selection."""
    base_snapshot = live_admitted_repository_model.snapshot
    snapshot = replace(
        base_snapshot,
        context=context_mutation(base_snapshot.context),
    )
    client = RecordingGovernanceClient(_attestation_content(live_enabled=True))

    with pytest.raises(ValueError, match="simulation selection"):
        _evaluate(
            monkeypatch,
            client,
            snapshot=snapshot,
            policy=policy,
        )

    assert getattr(snapshot.context, field_name) is not None
    assert snapshot.context.purpose == "live-release"
    assert client.scan_calls == []
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
    monkeypatch: pytest.MonkeyPatch,
    live_admitted_repository_model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
    mutate: SnapshotMutation,
    message: str,
) -> None:
    """Reject self-consistent Snapshots without first-slice closure."""
    snapshot = mutate(live_admitted_repository_model.snapshot)
    client = RecordingGovernanceClient(_attestation_content(live_enabled=True))

    with pytest.raises(ValueError, match=message):
        _evaluate(
            monkeypatch,
            client,
            snapshot=snapshot,
            policy=policy,
        )

    assert client.scan_calls == []
    assert client.calls == []


def test_decision_binds_attestation_provenance_and_content_digest(
    monkeypatch: pytest.MonkeyPatch,
    live_admitted_repository_model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
) -> None:
    """Bind source, static evidence, content, and current observation."""
    snapshot = live_admitted_repository_model.snapshot
    content = _attestation_content(live_enabled=True)
    client = RecordingGovernanceClient(content)

    decision = _evaluate(
        monkeypatch,
        client,
        snapshot=snapshot,
        policy=policy,
    )
    document = decision.to_document()
    governance = _object_member(document, "governance")
    static_reference = _object_member(document, "static-reference")
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
    assert static_reference == decision.static_reference.to_document()
    assert static_reference["policy-digest"] == (STATIC_REFERENCE_POLICY_DIGEST)
    assert static_reference["target"] == snapshot.context.target
    assert decision.static_reference.result_digest.startswith("sha256:")
    assert context["repository-model-digest"] == (
        decision.context.repository_model_digest
    )


def test_each_evaluation_performs_a_fresh_protected_ref_read(
    monkeypatch: pytest.MonkeyPatch,
    live_admitted_repository_model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
) -> None:
    """Do not reuse an earlier enabled observation after source disablement."""
    snapshot = live_admitted_repository_model.snapshot
    client = RecordingGovernanceClient(_attestation_content(live_enabled=True))

    first = _evaluate(
        monkeypatch,
        client,
        snapshot=snapshot,
        policy=policy,
    )
    client.content = _attestation_content(live_enabled=False)
    client.blob_oid = "c" * 40
    second = _evaluate(
        monkeypatch,
        client,
        snapshot=snapshot,
        policy=policy,
    )

    assert first.result is EligibilityResult.PASS
    assert second.result is EligibilityResult.BLOCKED
    assert second.diagnostics == ("governance-live-disabled",)
    assert first.governance.content_sha256 != second.governance.content_sha256
    assert first.governance.blob_oid == GOVERNANCE_BLOB
    assert second.governance.blob_oid == "c" * 40
    assert client.scan_calls == [
        (REPO_ROOT, "git-target", TARGET),
        (REPO_ROOT, "git-target", TARGET),
    ]
    assert [call[0] for call in client.calls] == [
        "protected",
        "resolve",
        "read",
        "protected",
        "resolve",
        "read",
    ]


def test_disabled_attestation_blocks_before_attempt_creation(
    monkeypatch: pytest.MonkeyPatch,
    live_admitted_repository_model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
) -> None:
    """Return a blocking pre-Attempt Decision for live_enabled false."""
    client = RecordingGovernanceClient(
        (FIXTURES / "governance-disabled.json").read_bytes()
    )

    decision = _evaluate(
        monkeypatch,
        client,
        snapshot=live_admitted_repository_model.snapshot,
        policy=policy,
    )
    document = decision.to_document()

    assert decision.result is EligibilityResult.BLOCKED
    assert decision.diagnostics == ("governance-live-disabled",)
    assert document["result"] == "blocked"
    governance = _object_member(document, "governance")
    assert governance["live-enabled"] is False
    assert document["static-reference"] == (
        decision.static_reference.to_document()
    )
    assert "attempt" not in document
    assert "release-execution" not in document
    assert "authorization" not in document


def test_expired_attestation_blocks_before_attempt_creation(
    monkeypatch: pytest.MonkeyPatch,
    live_admitted_repository_model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
) -> None:
    """Block a structurally valid attestation after its current expiry."""
    client = RecordingGovernanceClient(
        _attestation_content(
            live_enabled=True,
            inspected_at="2026-06-01T00:00:00Z",
            expires_at="2026-08-05T00:00:00Z",
        )
    )

    decision = _evaluate(
        monkeypatch,
        client,
        snapshot=live_admitted_repository_model.snapshot,
        policy=policy,
    )

    assert decision.result is EligibilityResult.BLOCKED
    assert decision.diagnostics == ("governance-attestation-expired",)
    assert decision.governance.attestation.live_enabled is True
    assert decision.governance.attestation.expires_at < NOW
    assert client.scan_calls == [(REPO_ROOT, "git-target", TARGET)]


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
def test_attestation_time_boundaries_block_live_eligibility(  # noqa: PLR0913, PLR0917
    monkeypatch: pytest.MonkeyPatch,
    live_admitted_repository_model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
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

    decision = _evaluate(
        monkeypatch,
        client,
        snapshot=live_admitted_repository_model.snapshot,
        policy=policy,
    )

    assert decision.result is EligibilityResult.BLOCKED
    assert decision.diagnostics == (diagnostic,)
    assert decision.static_reference.result == "clean"


@pytest.mark.parametrize(
    ("static_reference", "diagnostic"),
    [
        pytest.param(
            _static_reference(findings=(_finding(),)),
            "static-reference-findings",
            id="findings",
        ),
        *[
            pytest.param(
                _static_reference(error_kind=error_kind),
                f"static-reference-{error_kind}",
                id=error_kind,
            )
            for error_kind in EXPECTED_STATIC_REFERENCE_ERROR_KINDS
        ],
    ],
)
def test_static_reference_findings_and_errors_block_before_attempt_creation(
    monkeypatch: pytest.MonkeyPatch,
    live_admitted_repository_model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
    static_reference: BoundedStaticReferenceResult,
    diagnostic: str,
) -> None:
    """Block every non-clean internally scanned static-reference Result."""
    client = RecordingGovernanceClient(_attestation_content(live_enabled=True))

    decision = _evaluate(
        monkeypatch,
        client,
        snapshot=live_admitted_repository_model.snapshot,
        policy=policy,
        static_reference=static_reference,
    )
    document = decision.to_document()
    evidence = _object_member(document, "static-reference")

    assert STATIC_REFERENCE_ERROR_KINDS == (
        EXPECTED_STATIC_REFERENCE_ERROR_KINDS
    )
    assert decision.result is EligibilityResult.BLOCKED
    assert decision.diagnostics == (diagnostic,)
    assert evidence == static_reference.to_document()
    assert evidence["target"] == TARGET
    assert document["diagnostics"] == [diagnostic]
    assert document["result"] == "blocked"
    assert decision.governance.attestation.live_enabled is True
    assert client.scan_calls == [(REPO_ROOT, "git-target", TARGET)]
    assert len(client.calls) == FRESH_SOURCE_CALL_COUNT


def _replace_static_reference_target(
    document: dict[str, JsonValue],
) -> None:
    static_reference = _object_member(document, "static-reference")
    static_reference["target"] = "d" * 40


def _replace_static_reference_policy_digest(
    document: dict[str, JsonValue],
) -> None:
    static_reference = _object_member(document, "static-reference")
    static_reference["policy-digest"] = "sha256:" + ("0" * 64)


def _replace_static_reference_with_index(
    document: dict[str, JsonValue],
) -> None:
    static_reference = _object_member(document, "static-reference")
    static_reference["source-kind"] = "index"
    del static_reference["target"]


@pytest.mark.parametrize(
    ("mutate", "message"),
    [
        pytest.param(
            _replace_static_reference_target,
            "static-reference target mismatch",
            id="target",
        ),
        pytest.param(
            _replace_static_reference_policy_digest,
            "policy is not current",
            id="policy-digest",
        ),
        pytest.param(
            _replace_static_reference_with_index,
            "static-reference target mismatch",
            id="source-kind",
        ),
    ],
)
def test_static_reference_requires_exact_target_and_digest_bound_evidence(
    mutate: DecisionMutation,
    message: str,
    live_intent: ReleaseIntent,
    live_admitted_repository_model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
) -> None:
    """Reject wrong-source, wrong-target, and obsolete-policy evidence."""
    decision = _transport_decision(
        live_intent,
        live_admitted_repository_model,
        policy,
    )
    document = decision.to_document()
    original_result_digest = decision.static_reference.result_digest
    mutate(document)

    with pytest.raises(ValueError, match=message):
        _admit_mutated_decision(
            document,
            live_intent=live_intent,
            live_admitted_repository_model=live_admitted_repository_model,
            policy=policy,
        )

    assert original_result_digest.startswith("sha256:")
    assert len(original_result_digest) == PREFIXED_SHA256_LENGTH


def _add_static_reference_unknown(
    document: dict[str, JsonValue],
) -> None:
    document["unexpected"] = "value"


def _shorten_static_reference_target(
    document: dict[str, JsonValue],
) -> None:
    document["target"] = "e" * 39


def _malform_static_reference_policy_digest(
    document: dict[str, JsonValue],
) -> None:
    document["policy-digest"] = "0" * 64


def _scalar_static_reference_findings(
    document: dict[str, JsonValue],
) -> None:
    document["findings"] = FIRST_SLICE_PACKAGE


def _duplicate_static_reference_findings(
    document: dict[str, JsonValue],
) -> None:
    finding = _finding().to_document()
    document["result"] = "findings"
    document["findings"] = [finding, finding]


def _drop_static_reference_error_kind(
    document: dict[str, JsonValue],
) -> None:
    document["result"] = "error"


@pytest.mark.parametrize(
    ("mutate", "message"),
    [
        pytest.param(
            _add_static_reference_unknown,
            "Result fields are not exact",
            id="unknown-field",
        ),
        pytest.param(
            _shorten_static_reference_target,
            "full lowercase target",
            id="target-shape",
        ),
        pytest.param(
            _malform_static_reference_policy_digest,
            "digest must be SHA-256",
            id="policy-digest-shape",
        ),
        pytest.param(
            _scalar_static_reference_findings,
            "must be an array",
            id="scalar-findings",
        ),
        pytest.param(
            _duplicate_static_reference_findings,
            "sorted and unique",
            id="duplicate-findings",
        ),
        pytest.param(
            _drop_static_reference_error_kind,
            "Result fields are not exact",
            id="error-without-kind",
        ),
    ],
)
def test_static_reference_result_is_closed_and_deterministic(
    mutate: StaticReferenceDocumentMutation,
    message: str,
) -> None:
    """Reject ambiguous or nondeterministic bounded scanner Results."""
    result = _static_reference()
    canonical_bytes = canonicalize(result.to_document())
    parsed = parse_bounded_static_reference_result(canonical_bytes)
    document = result.to_document()
    mutate(document)

    assert parsed == result
    assert parsed.to_document() == result.to_document()
    assert parsed.result_digest == result.result_digest
    with pytest.raises((TypeError, ValueError), match=message):
        parse_bounded_static_reference_result(canonicalize(document))


def test_eligibility_rejects_live_repository_model_with_run_attempt(
    monkeypatch: pytest.MonkeyPatch,
    live_admitted_repository_model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
) -> None:
    """Reject a retired Live Snapshot run-attempt before external reads."""
    current_snapshot = live_admitted_repository_model.snapshot
    prior_snapshot = replace(
        current_snapshot,
        context=replace(current_snapshot.context, run_attempt=2),
    )
    current_context = _context(current_snapshot, policy)
    client = RecordingGovernanceClient(_attestation_content(live_enabled=True))

    with pytest.raises(
        ValueError,
        match="live compilation cannot bind run_attempt",
    ):
        _evaluate(
            monkeypatch,
            client,
            snapshot=prior_snapshot,
            policy=policy,
            context=current_context,
        )

    assert client.scan_calls == []
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
        "target",
        "control",
        "catalog",
        "policy",
    ],
)
def test_eligibility_rejects_wrong_current_binding(
    monkeypatch: pytest.MonkeyPatch,
    live_admitted_repository_model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
    context_mutation: LiveContextMutation,
    message: str,
) -> None:
    """Reject each wrong current authority before any fresh external read."""
    client = RecordingGovernanceClient(_attestation_content(live_enabled=True))

    with pytest.raises(ValueError, match=message):
        _evaluate(
            monkeypatch,
            client,
            snapshot=live_admitted_repository_model.snapshot,
            policy=policy,
            context_mutation=context_mutation,
        )

    assert client.scan_calls == []
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
    monkeypatch: pytest.MonkeyPatch,
    live_admitted_repository_model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
    source_mutation: GovernanceSourceMutation,
) -> None:
    """Reject any source variation before scanning or invoking the client."""
    mutated_policy = replace(
        policy,
        governance=source_mutation(policy.governance),
    )
    client = RecordingGovernanceClient(_attestation_content(live_enabled=True))

    with pytest.raises(
        ValueError,
        match="Governance source is not the exact fixed contract",
    ):
        _evaluate(
            monkeypatch,
            client,
            snapshot=live_admitted_repository_model.snapshot,
            policy=mutated_policy,
        )

    assert client.scan_calls == []
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
            b'{"schema":"first","schema":"second","live_enabled":false}',
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
    monkeypatch: pytest.MonkeyPatch,
    live_admitted_repository_model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
    context_mutation: LiveContextMutation,
    message: str,
) -> None:
    """Reject malformed current authority before scanning or Governance."""
    client = RecordingGovernanceClient(_attestation_content(live_enabled=True))

    with pytest.raises((TypeError, ValueError), match=message):
        _evaluate(
            monkeypatch,
            client,
            snapshot=live_admitted_repository_model.snapshot,
            policy=policy,
            context_mutation=context_mutation,
        )

    assert client.scan_calls == []
    assert client.calls == []


@pytest.mark.parametrize(
    "failure",
    ["unprotected", "protection-read", "resolve", "blob"],
    ids=["unprotected", "protection-read", "missing-ref", "missing-blob"],
)
def test_missing_unreadable_or_unprotected_source_fails_closed(
    monkeypatch: pytest.MonkeyPatch,
    live_admitted_repository_model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
    failure: str,
) -> None:
    """Fail without a Decision when the fixed source cannot be freshly read."""
    client = RecordingGovernanceClient(_attestation_content(live_enabled=True))
    if failure == "unprotected":
        client.protected = False
    else:
        client.failure = failure

    with pytest.raises(ValueError, match=r"protected|unavailable"):
        _evaluate(
            monkeypatch,
            client,
            snapshot=live_admitted_repository_model.snapshot,
            policy=policy,
        )

    assert client.scan_calls == [(REPO_ROOT, "git-target", TARGET)]
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
def test_resolved_commit_blob_and_content_provenance_are_strict(  # noqa: PLR0913, PLR0917
    monkeypatch: pytest.MonkeyPatch,
    live_admitted_repository_model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
    field: str,
    value: str,
    message: str,
) -> None:
    """Reject malformed resolved provenance before eligibility."""
    client = RecordingGovernanceClient(_attestation_content(live_enabled=True))
    setattr(client, field, value)

    with pytest.raises(ValueError, match=message):
        _evaluate(
            monkeypatch,
            client,
            snapshot=live_admitted_repository_model.snapshot,
            policy=policy,
        )

    assert client.scan_calls == [(REPO_ROOT, "git-target", TARGET)]


def test_prior_facts_cannot_substitute_for_fresh_input(
    monkeypatch: pytest.MonkeyPatch,
    live_admitted_repository_model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
) -> None:
    """Require the live evaluator's client even when prior facts exist."""
    snapshot = live_admitted_repository_model.snapshot
    client = RecordingGovernanceClient(_attestation_content(live_enabled=True))
    prior = _evaluate(
        monkeypatch,
        client,
        snapshot=snapshot,
        policy=policy,
    )
    blocking_client = RecordingGovernanceClient(
        _attestation_content(live_enabled=True)
    )
    blocking_client.failure = "blob"

    with pytest.raises(ValueError, match="blob unavailable"):
        _evaluate(
            monkeypatch,
            blocking_client,
            snapshot=snapshot,
            policy=policy,
        )

    assert prior.result is EligibilityResult.PASS
    assert prior.decision_digest.startswith("sha256:")
    assert blocking_client.scan_calls == [(REPO_ROOT, "git-target", TARGET)]
    assert blocking_client.calls[-1][0] == "read"
    assert len(blocking_client.calls) == FRESH_SOURCE_CALL_COUNT


def test_observation_rejects_non_utc_time_before_source_read(
    policy: ReleasePolicy,
) -> None:
    """Require a trusted UTC observation instant."""
    client = RecordingGovernanceClient(_attestation_content(live_enabled=True))

    with pytest.raises(ValueError, match="UTC-aware"):
        observe_governance_source(
            policy.governance,
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
    "package": FIRST_SLICE_PACKAGE,
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
        == accepted_document["inspected_at"]
    )
    assert (
        accepted.to_document()["expires_at"] == accepted_document["expires_at"]
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


@pytest.mark.parametrize(
    "admission_mode",
    [
        pytest.param(
            LiveEligibilityAdmissionMode.CURRENT_FRESHNESS,
            id="current-freshness",
        ),
        pytest.param(
            LiveEligibilityAdmissionMode.CAPABILITY_REPLAY,
            id="capability-replay",
        ),
    ],
)
@pytest.mark.parametrize(
    ("result", "diagnostics"),
    [
        pytest.param("blocked", [], id="blocked-result-only"),
        pytest.param(
            "pass",
            ["governance-live-disabled"],
            id="diagnostic-only",
        ),
    ],
)
def test_live_admission_independently_enforces_result_and_diagnostics_guards(  # noqa: PLR0913, PLR0917
    admission_mode: LiveEligibilityAdmissionMode,
    result: str,
    diagnostics: list[str],
    live_intent: ReleaseIntent,
    live_admitted_repository_model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
) -> None:
    """Reject each non-passing closure property without help from another."""
    document = _transport_decision(
        live_intent,
        live_admitted_repository_model,
        policy,
    ).to_document()
    document["result"] = result
    document["diagnostics"] = diagnostics
    static_reference = _object_member(document, "static-reference")

    assert static_reference["result"] == "clean"
    assert document["result"] == result
    assert document["diagnostics"] == diagnostics
    with pytest.raises(
        ValueError,
        match="not a closed passing decision",
    ):
        _admit_mutated_decision(
            document,
            live_intent=live_intent,
            live_admitted_repository_model=live_admitted_repository_model,
            policy=policy,
            admission_mode=admission_mode,
        )


@pytest.mark.parametrize(
    "admission_mode",
    [
        pytest.param(
            LiveEligibilityAdmissionMode.CURRENT_FRESHNESS,
            id="current-freshness",
        ),
        pytest.param(
            LiveEligibilityAdmissionMode.CAPABILITY_REPLAY,
            id="capability-replay",
        ),
    ],
)
def test_enabled_governance_exact_validity_boundaries_evaluate_and_admit(
    admission_mode: LiveEligibilityAdmissionMode,
    monkeypatch: pytest.MonkeyPatch,
    live_intent: ReleaseIntent,
    live_admitted_repository_model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
) -> None:
    """Admit an enabled observation at inspection with a 90-day lifetime."""
    inspected_at = NOW
    expires_at = inspected_at + timedelta(days=90)
    content = _attestation_content(
        live_enabled=True,
        inspected_at=inspected_at.strftime("%Y-%m-%dT%H:%M:%SZ"),
        expires_at=expires_at.strftime("%Y-%m-%dT%H:%M:%SZ"),
    )
    snapshot = live_admitted_repository_model.snapshot
    context = _context(
        snapshot,
        policy,
        selected_ref=live_intent.selected_ref,
    )
    client = RecordingGovernanceClient(content)

    decision = _evaluate(
        monkeypatch,
        client,
        snapshot=snapshot,
        policy=policy,
        context=context,
        now=inspected_at,
    )
    document = decision.to_document()
    static_reference = _object_member(document, "static-reference")
    governance = _object_member(document, "governance")
    canonical_bytes = canonicalize(document)
    admitted = admit_live_eligibility_decision(
        canonical_bytes,
        intent=live_intent,
        repository_model=live_admitted_repository_model,
        policy=policy,
        expected_digest=decision.decision_digest,
        admission_mode=admission_mode,
        now=inspected_at,
    )

    assert decision.result is EligibilityResult.PASS
    assert decision.diagnostics == ()
    assert static_reference["source-kind"] == "git-target"
    assert static_reference["target"] == live_intent.target
    assert static_reference["result"] == "clean"
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
        "inspected-at": "2026-08-06T12:00:00Z",
        "expires-at": "2026-11-04T12:00:00Z",
        "attestation-content-digest": (
            decision.governance.attestation.content_digest
        ),
    }
    assert decision.governance.observed_at == inspected_at
    assert decision.governance.attestation.inspected_at == inspected_at
    assert (
        decision.governance.attestation.expires_at
        - decision.governance.attestation.inspected_at
        == timedelta(days=90)
    )
    assert admitted.to_document() == document
    assert admitted.canonical_bytes == canonical_bytes
    assert admitted.decision_digest == decision.decision_digest
    assert admitted.result is EligibilityResult.PASS
    assert admitted.diagnostics == ()
    assert admitted.static_reference.result == "clean"
    assert admitted.static_reference.target == live_intent.target
    assert admitted.governance.live_enabled is True
    assert admitted.governance.observed_at == admitted.governance.inspected_at
    assert (
        admitted.governance.expires_at - admitted.governance.inspected_at
        == timedelta(days=90)
    )
    assert client.scan_calls == [(REPO_ROOT, "git-target", TARGET)]
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


@pytest.mark.parametrize(
    "instant",
    [
        pytest.param(
            datetime.fromisoformat("2026-08-06T12:00:00+01:00"),
            id="positive-one-hour",
        ),
        pytest.param(
            datetime.fromisoformat("2026-08-06T12:00:00-01:00"),
            id="negative-one-hour",
        ),
    ],
)
def test_observation_rejects_nonzero_utc_offsets_before_source_read(
    instant: datetime,
    policy: ReleasePolicy,
) -> None:
    """Reject aware non-UTC instants before invoking the source client."""
    client = RecordingGovernanceClient(_attestation_content(live_enabled=True))

    assert instant.tzinfo is not None
    assert instant.utcoffset() in {
        timedelta(hours=-1),
        timedelta(hours=1),
    }
    with pytest.raises(ValueError, match="UTC-aware"):
        observe_governance_source(
            policy.governance,
            client,
            now=instant,
        )

    assert client.scan_calls == []
    assert client.calls == []


@pytest.mark.parametrize(
    ("selected_ref", "message"),
    [
        pytest.param(
            "refs/remotes/origin/main",
            "unsupported namespace",
            id="unsupported-namespace",
        ),
        pytest.param(
            "refs/heads/",
            "not a valid Git ref",
            id="empty-suffix",
        ),
        pytest.param(
            "refs/heads/topic/",
            "not a valid Git ref",
            id="trailing-slash",
        ),
        pytest.param(
            "refs/tags/release.",
            "not a valid Git ref",
            id="trailing-dot",
        ),
        pytest.param(
            "refs/heads/topic..branch",
            "not a valid Git ref",
            id="double-dot",
        ),
        pytest.param(
            "refs/heads/topic@{1",
            "not a valid Git ref",
            id="reflog-sequence",
        ),
        pytest.param(
            "refs/heads/topic branch",
            "not a valid Git ref",
            id="forbidden-space",
        ),
        pytest.param(
            "refs/heads/topic~branch",
            "not a valid Git ref",
            id="forbidden-tilde",
        ),
        pytest.param(
            "refs/heads/topic^branch",
            "not a valid Git ref",
            id="forbidden-caret",
        ),
        pytest.param(
            "refs/heads/topic:branch",
            "not a valid Git ref",
            id="forbidden-colon",
        ),
        pytest.param(
            "refs/heads/topic?branch",
            "not a valid Git ref",
            id="forbidden-question-mark",
        ),
        pytest.param(
            "refs/heads/topic*branch",
            "not a valid Git ref",
            id="forbidden-asterisk",
        ),
        pytest.param(
            "refs/heads/topic[branch",
            "not a valid Git ref",
            id="forbidden-open-bracket",
        ),
        pytest.param(
            "refs/heads/topic\\branch",
            "not a valid Git ref",
            id="forbidden-backslash",
        ),
        pytest.param(
            "refs/heads/topic/.hidden",
            "not a valid Git ref",
            id="hidden-component",
        ),
        pytest.param(
            "refs/heads/topic.lock",
            "not a valid Git ref",
            id="lock-suffix",
        ),
        pytest.param(
            "refs/heads/topic//branch",
            "not a valid Git ref",
            id="empty-component",
        ),
        pytest.param(
            "refs/heads/topic\x00branch",
            "not a valid Git ref",
            id="nul-control",
        ),
        pytest.param(
            "refs/heads/topic\x1fbranch",
            "not a valid Git ref",
            id="unit-separator-control",
        ),
        pytest.param(
            "refs/heads/topic\x7fbranch",
            "not a valid Git ref",
            id="delete-control",
        ),
    ],
)
def test_selected_ref_grammar_rejects_invalid_refs_before_any_read(
    selected_ref: str,
    message: str,
    monkeypatch: pytest.MonkeyPatch,
    live_admitted_repository_model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
) -> None:
    """Reject invalid supported-ref grammar before either external read."""
    snapshot = live_admitted_repository_model.snapshot
    context = _context(snapshot, policy, selected_ref=selected_ref)
    client = RecordingGovernanceClient(_attestation_content(live_enabled=True))

    with pytest.raises(ValueError, match=message):
        _evaluate(
            monkeypatch,
            client,
            snapshot=snapshot,
            policy=policy,
            context=context,
        )

    assert context.selected_ref == selected_ref
    assert client.scan_calls == []
    assert client.calls == []
