"""Shared exact first-slice Release commit-6 fixtures."""

from __future__ import annotations

import hashlib
from dataclasses import dataclass
from pathlib import Path

import pytest
from three_workflow_delivery_v3.adapters import node as node_adapter
from three_workflow_delivery_v3.canonical import canonical_sha256, canonicalize
from three_workflow_delivery_v3.catalogs import catalog_digest
from three_workflow_delivery_v3.records.artifacts import (
    ArtifactTransportIdentity,
)
from three_workflow_delivery_v3.records.release import (
    BuddyExecutionIdentity,
    QualificationDecision,
    QualificationEvidence,
    QualificationSnapshot,
    ReleaseArtifact,
    ReleaseAttemptBinding,
    ReleaseAttemptIdentity,
    ReleaseIntent,
    SimulationBinding,
    release_artifact_transport_name,
)
from three_workflow_delivery_v3.release.finalizer import (
    finalize_qualification,
)
from three_workflow_delivery_v3.release.identity import (
    OFFICIAL_SIMULATION_PRODUCER,
    derive_simulation_binding,
    normalize_buddy_live_intent,
    normalize_official_simulation_intent,
)
from three_workflow_delivery_v3.release.planner import (
    plan_live_qualification,
    plan_official_simulation_qualification,
)
from three_workflow_delivery_v3.release.qualification import (
    MechanicalBuildResult,
    execute_project_test,
    execute_release_build,
    form_uploaded_release_artifact,
    qualify_release_artifact_contents,
    qualify_release_install_import,
)
from three_workflow_delivery_v3.repository.compiler import (
    AdmittedRepositoryModelSnapshot,
    CompilationContext,
    CompiledBuild,
    CompiledOutput,
    CompiledQualitySelection,
    CompiledReleaseUnit,
    RepositoryModelSnapshot,
    admit_repository_model_snapshot,
    compile_release_policy,
)
from three_workflow_delivery_v3.repository.descriptors import (
    FIRST_SLICE_POLICY_PATH,
    ReleasePolicy,
    load_release_policy,
)
from three_workflow_delivery_v3.repository.node_provider import (
    NbgvFacts,
    ProjectNode,
)

REPO_ROOT = Path(__file__).resolve().parents[6]
PROJECT_ROOT = REPO_ROOT / "src/public/lib/hcoona-release-smoke-npm"
TARGET = "e" * 40
RUN_ID = 7301
RUN_ATTEMPT = 3
NPM_VERSION = "1.2.3-beta.42.ge123456"
DIGEST_A = "sha256:" + ("a" * 64)
DIGEST_B = "sha256:" + ("b" * 64)
TARBALL = b"commit-6-qualified-tarball"


@dataclass(frozen=True, slots=True)
class QualifiedSimulation:
    """Complete successful qualification fixture."""

    intent: ReleaseIntent
    admitted_repository_model: AdmittedRepositoryModelSnapshot
    binding: SimulationBinding
    snapshot: QualificationSnapshot
    mechanics: MechanicalBuildResult
    artifact: ReleaseArtifact
    evidence: tuple[QualificationEvidence, ...]
    decision: QualificationDecision
    request: node_adapter.BuildRequest
    build_result: node_adapter.BuildResult
    tarball: bytes
    expectation: node_adapter.ArtifactExpectation


def nbgv_facts() -> NbgvFacts:
    """Return exact canonical and native first-slice facts."""
    return NbgvFacts(
        canonical_version="1.2.3",
        sem_ver1="1.2.3-beta-42",
        sem_ver2=NPM_VERSION,
        version_height=42,
        git_commit_id=TARGET,
        public_release=False,
        npm_package_version=NPM_VERSION,
        node_api_result_digest=DIGEST_A,
    )


@pytest.fixture
def intent() -> ReleaseIntent:
    """Return the normalized Official simulation Intent."""
    return normalize_official_simulation_intent(
        repository="hcoona/three",
        selected_ref="refs/heads/feature/release",
        target=TARGET,
        actor="release-operator",
        workflow_run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
    )


def repository_model(
    intent: ReleaseIntent,
    policy: ReleasePolicy,
    *,
    context: CompilationContext | None = None,
) -> RepositoryModelSnapshot:
    """Return the exact ready first-slice Repository Model."""
    if context is None:
        context = CompilationContext(
            request_id=intent.request_id,
            purpose="release-simulation",
            workflow_run_id=RUN_ID,
            run_attempt=RUN_ATTEMPT,
            target=TARGET,
            producer=OFFICIAL_SIMULATION_PRODUCER,
            control=f"workflow-delivery-v3:{TARGET}",
            catalog_digest=catalog_digest(),
            channel="official",
            release_unit="hcoona-release-smoke-npm",
        )
    project = ProjectNode(
        project_id="@hcoona/hcoona-release-smoke-npm",
        package_name="@hcoona/hcoona-release-smoke-npm",
        path="src/public/lib/hcoona-release-smoke-npm",
        manifest_path=("src/public/lib/hcoona-release-smoke-npm/package.json"),
        private=False,
        workspace_dependencies=(),
    )
    build = CompiledBuild(
        build_id="npm-package",
        definition="node/npm-package-v1",
        project_id=project.project_id,
        entry_point=project.manifest_path,
        outputs=(
            CompiledOutput(
                output_id="npm-tarball",
                role="primary-package",
                kind="npm-tarball",
            ),
        ),
        required_native_projections=("npmPackageVersion",),
    )
    release_unit = CompiledReleaseUnit(
        release_unit="hcoona-release-smoke-npm",
        descriptor_path=(
            "src/public/lib/hcoona-release-smoke-npm/"
            "workflow-delivery.release-unit.yml"
        ),
        builds=(build,),
    )
    quality = CompiledQualitySelection(
        path=(
            "src/public/lib/hcoona-release-smoke-npm/"
            "workflow-delivery.quality.yml"
        ),
        ecosystem="node",
        preset="node/hcoona-release-smoke-npm-v1",
        required=("node/project-build-v1", "node/project-test-v1"),
        advisory=(),
    )
    return RepositoryModelSnapshot(
        context=context,
        manifest_digest=DIGEST_A,
        provider_result_digests=(DIGEST_B,),
        project_nodes=(project,),
        release_units=(release_unit,),
        quality=(quality,),
        release_policy_path=FIRST_SLICE_POLICY_PATH,
        release_policy=compile_release_policy(policy),
        nbgv=nbgv_facts(),
        reverse_index=(
            (
                project.project_id,
                ("hcoona-release-smoke-npm/npm-package",),
            ),
        ),
        unresolved=(),
        ready=True,
    )


@pytest.fixture
def admitted_repository_model(
    intent: ReleaseIntent,
    policy: ReleasePolicy,
) -> AdmittedRepositoryModelSnapshot:
    """Serialize and admit the exact current simulation model."""
    snapshot = repository_model(intent, policy)
    canonical_bytes = canonicalize(snapshot.to_document())
    return admit_repository_model_snapshot(
        canonical_bytes,
        expected_context=snapshot.context,
        expected_digest=snapshot.snapshot_digest,
    )


@pytest.fixture
def policy() -> ReleasePolicy:
    """Load the normalized first-slice Release policy."""
    path = REPO_ROOT / FIRST_SLICE_POLICY_PATH
    return load_release_policy(
        path,
        _target_content=path.read_text(encoding="utf-8"),
        _target_path=FIRST_SLICE_POLICY_PATH,
    )


@pytest.fixture
def binding(
    intent: ReleaseIntent,
    admitted_repository_model: AdmittedRepositoryModelSnapshot,
) -> SimulationBinding:
    """Derive the simulation binding from admitted current inputs."""
    return derive_simulation_binding(intent, admitted_repository_model)


@pytest.fixture
def qualification_snapshot(
    intent: ReleaseIntent,
    binding: SimulationBinding,
    admitted_repository_model: AdmittedRepositoryModelSnapshot,
) -> QualificationSnapshot:
    """Plan the complete Official simulation qualification Snapshot."""
    return plan_official_simulation_qualification(
        intent,
        binding,
        admitted_repository_model,
    )


@pytest.fixture
def live_intent() -> ReleaseIntent:
    """Return the normalized Buddy live Intent."""
    return normalize_buddy_live_intent(
        repository="hcoona/three",
        selected_ref="refs/heads/feature/release",
        target=TARGET,
        actor="release-operator",
        workflow_run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
    )


@pytest.fixture
def live_admitted_repository_model(
    live_intent: ReleaseIntent,
    policy: ReleasePolicy,
) -> AdmittedRepositoryModelSnapshot:
    """Return the admitted live-purpose request-local Repository Model."""
    context = CompilationContext(
        request_id=live_intent.request_id,
        purpose="live-release",
        workflow_run_id=RUN_ID,
        run_attempt=None,
        target=TARGET,
        producer="compile-live-model",
        control=f"workflow-delivery-v3:{TARGET}",
        catalog_digest=catalog_digest(),
    )
    snapshot = repository_model(live_intent, policy, context=context)
    canonical_bytes = canonicalize(snapshot.to_document())
    return admit_repository_model_snapshot(
        canonical_bytes,
        expected_context=context,
        expected_digest=snapshot.snapshot_digest,
    )


@pytest.fixture
def live_attempt_binding(
    live_intent: ReleaseIntent,
    live_admitted_repository_model: AdmittedRepositoryModelSnapshot,
) -> ReleaseAttemptBinding:
    """Return an exact Buddy Attempt binding for live qualification."""
    execution = BuddyExecutionIdentity(
        channel="buddy",
        release_unit=live_intent.release_unit,
        target=live_intent.target,
    )
    attempt = ReleaseAttemptIdentity(
        execution=execution,
        workflow_run_id=live_intent.workflow_run_id,
        run_attempt=live_intent.run_attempt,
    )
    return ReleaseAttemptBinding(
        intent_digest=live_intent.intent_digest,
        request_id=live_intent.request_id,
        execution=execution,
        attempt=attempt,
        repository_model_digest=(
            live_admitted_repository_model.canonical_digest
        ),
        live_eligibility_artifact_id=7001,
        live_eligibility_artifact_digest=DIGEST_A,
        live_eligibility_payload_digest=DIGEST_B,
        attestation_provenance=(
            ("blob-oid", "governance-blob"),
            ("content-sha256", DIGEST_A),
            (
                "path",
                (
                    ".github/workflow-delivery/governance/"
                    "hcoona-release-smoke-npm.json"
                ),
            ),
            ("ref", "refs/heads/main"),
            ("repository", "hcoona/three"),
            ("resolved-commit", TARGET),
        ),
        history_snapshot_artifact_id=7002,
        history_snapshot_artifact_digest=DIGEST_B,
    )


@pytest.fixture
def live_qualification_snapshot(
    live_intent: ReleaseIntent,
    live_attempt_binding: ReleaseAttemptBinding,
    live_admitted_repository_model: AdmittedRepositoryModelSnapshot,
) -> QualificationSnapshot:
    """Plan the complete Buddy live qualification Snapshot."""
    return plan_live_qualification(
        live_intent,
        live_attempt_binding,
        live_admitted_repository_model,
    )


def node_build_request(
    snapshot: QualificationSnapshot,
) -> node_adapter.BuildRequest:
    """Create the exact runtime Build Request selected by the Snapshot."""
    assert isinstance(snapshot.subject, SimulationBinding)
    control_digest = canonical_sha256(
        {
            "schema": "workflow-delivery/v3/control-identity",
            "identity": snapshot.subject.control,
        }
    )
    witness = node_adapter.PackageTargetWitness(
        target=snapshot.target,
        release_unit=snapshot.release_unit,
        nbgv=snapshot.nbgv,
        build_definition="node/npm-package-v1",
        catalog_digest=catalog_digest(),
        control_digest=control_digest,
        purpose="release-simulation",
    )
    return node_adapter.BuildRequest(
        source_root=PROJECT_ROOT,
        declared_inputs=snapshot.build_requests[0].declared_inputs,
        npm_package_version=NPM_VERSION,
        witness=witness,
        source_date_epoch=1_700_000_000,
        node_version="24.14.0",
        pnpm_version="11.21.0",
        npm_version="11.9.0",
    )


@pytest.fixture
def qualified_simulation(
    monkeypatch: pytest.MonkeyPatch,
    intent: ReleaseIntent,
    admitted_repository_model: AdmittedRepositoryModelSnapshot,
    binding: SimulationBinding,
    qualification_snapshot: QualificationSnapshot,
) -> QualifiedSimulation:
    """Return a complete successful four-Evidence qualification."""
    request = node_build_request(qualification_snapshot)
    tarball_sha256 = f"sha256:{hashlib.sha256(TARBALL).hexdigest()}"
    tarball_sha512 = f"sha512:{hashlib.sha512(TARBALL).hexdigest()}"
    lifecycle_scripts = (
        ("build", "node scripts/build.mjs"),
        ("test", "node --test"),
        ("version", "node scripts/nbgv-version.mjs"),
    )
    entries = (
        "package/README.md",
        "package/dist/index.js",
        "package/package.json",
        "package/workflow-delivery/provenance.json",
    )
    manifest = node_adapter.ArtifactManifest(
        basename=f"hcoona-hcoona-release-smoke-npm-{NPM_VERSION}.tgz",
        entries=entries,
        lifecycle_scripts=lifecycle_scripts,
        sha256=tarball_sha256,
        sha512=tarball_sha512,
        byte_size=len(TARBALL),
    )
    expectation = node_adapter.ArtifactExpectation(
        package_name="@hcoona/hcoona-release-smoke-npm",
        npm_package_version=NPM_VERSION,
        files_allowlist=(
            "dist",
            "README.md",
            "workflow-delivery/provenance.json",
        ),
        lifecycle_scripts=lifecycle_scripts,
        entry_allowlist=entries,
        witness_bytes=request.witness.canonical_bytes,
    )
    build_result = node_adapter.BuildResult(
        tarball=TARBALL,
        manifest=manifest,
        expectation=expectation,
        witness=request.witness.canonical_bytes,
        source_input_manifest=tuple(
            (
                path,
                f"sha256:{hashlib.sha256(path.encode()).hexdigest()}",
            )
            for path in request.declared_inputs
        ),
        toolchain=(
            ("node", request.node_version),
            ("pnpm", request.pnpm_version),
            ("npm", request.npm_version),
            ("adapter", "node/npm-package-v1"),
        ),
    )
    monkeypatch.setattr(
        node_adapter,
        "build_node_package",
        lambda _supplied: build_result,
    )
    monkeypatch.setattr(
        node_adapter,
        "run_node_project_tests",
        lambda _project_root, _runtime: None,
    )
    monkeypatch.setattr(
        node_adapter,
        "qualify_npm_artifact_contents",
        lambda _tarball, _supplied: manifest,
    )
    monkeypatch.setattr(
        node_adapter,
        "qualify_npm_install_import",
        lambda _tarball, _supplied, _runtime: node_adapter.InstallImportResult(
            smoke_message="hcoona-release-smoke-npm",
            witness_sha256=canonical_sha256(request.witness.to_document()),
        ),
    )
    transport = ArtifactTransportIdentity(
        artifact_id=801,
        artifact_name=release_artifact_transport_name(
            repository=qualification_snapshot.repository,
            purpose="release-simulation",
            output=qualification_snapshot.outputs[0],
            qualification_snapshot_digest=(
                qualification_snapshot.snapshot_digest
            ),
            workflow_run_id=RUN_ID,
            run_attempt=RUN_ATTEMPT,
            producer="build-tarball",
        ),
        artifact_url=(
            "https://github.com/hcoona/three/actions/runs/7301/artifacts/801"
        ),
        transport_digest="sha256:" + ("d" * 64),
        producer="build-tarball",
        workflow_run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
    )
    mechanics, failed_build_evidence = execute_release_build(
        qualification_snapshot,
        request,
    )
    assert mechanics is not None
    assert failed_build_evidence is None
    artifact, build_evidence = form_uploaded_release_artifact(
        qualification_snapshot,
        mechanics,
        transport,
    )
    runtime = node_adapter.RuntimeRequest(
        node_version="v24.14.0",
        npm_version="11.9.0",
    )
    project_evidence = execute_project_test(
        qualification_snapshot,
        PROJECT_ROOT,
        runtime,
    )
    contents_evidence = qualify_release_artifact_contents(
        qualification_snapshot,
        artifact,
        TARBALL,
        expectation,
    )
    install_evidence = qualify_release_install_import(
        qualification_snapshot,
        artifact,
        TARBALL,
        expectation,
        runtime,
    )
    evidence = (
        build_evidence,
        project_evidence,
        contents_evidence,
        install_evidence,
    )
    decision = finalize_qualification(
        qualification_snapshot,
        evidence,
        (artifact,),
    )
    return QualifiedSimulation(
        intent=intent,
        admitted_repository_model=admitted_repository_model,
        binding=binding,
        snapshot=qualification_snapshot,
        mechanics=mechanics,
        artifact=artifact,
        evidence=evidence,
        decision=decision,
        request=request,
        build_result=build_result,
        tarball=TARBALL,
        expectation=expectation,
    )
