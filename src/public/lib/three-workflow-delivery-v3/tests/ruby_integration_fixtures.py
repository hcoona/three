"""Source-backed Ruby fixtures without registry effects."""

from dataclasses import replace
from datetime import UTC, datetime, timedelta
from pathlib import Path

from three_workflow_delivery_v3._ruby_native import (
    RUBY_PACKAGE_FILES,
    RUBY_RELEASE_UNIT,
    RUBY_ROOT,
    ruby_digest,
    ruby_profile,
)
from three_workflow_delivery_v3.adapters.ruby import build_ruby_package
from three_workflow_delivery_v3.adapters.rubygems import RubyRegistry
from three_workflow_delivery_v3.canonical import canonical_sha256, canonicalize
from three_workflow_delivery_v3.catalogs import catalog_digest
from three_workflow_delivery_v3.records.artifacts import (
    ArtifactReference,
    ArtifactTransportIdentity,
)
from three_workflow_delivery_v3.records.ruby import RubyArtifact
from three_workflow_delivery_v3.release.ruby_governance import (
    RubyGovernance,
    blocked_ruby_governance,
    ruby_publisher_tuple,
)
from three_workflow_delivery_v3.repository.compiler import (
    CompilationContext,
    provider_binding,
)
from three_workflow_delivery_v3.repository.node_provider import (
    AUTHORITATIVE_REMOTE,
    TAG_REFSPEC,
    CheckoutEvidence,
)
from three_workflow_delivery_v3.repository.ruby_model import (
    RUBY_POLICY,
    RubyRepositoryModelSnapshot,
    admit_ruby_provider_facts,
    compile_ruby_repository_model,
    ruby_provider_manifest,
)
from three_workflow_delivery_v3.repository.ruby_provider import (
    RUBY_GLOBALS,
    RubyNbgvFacts,
    RubyProviderResult,
)

from .ruby_fixtures import EPOCH, commit, git

ROOT = Path(__file__).resolve().parents[5]
TARGET = "a" * 40
RUN_ID = 991
NOW = datetime(2026, 9, 30, 12, tzinfo=UTC)
PROFILE = {
    "ruby": "4.0.7",
    "rubygems": "4.0.20",
    "ruby-platform": "x86_64-linux",
    "ruby-revision": "modeled-revision",
    "zlib": "modeled-zlib",
    "zlib-runtime": "modeled-zlib-runtime",
}


def reference(document, artifact_id=101, filename="record.json"):
    """Bind canonical fixture bytes to a synthetic immutable reference."""
    return ArtifactReference(
        artifact_id,
        "sha256:" + "1" * 64,
        f"https://example.invalid/artifacts/{artifact_id}",
        filename,
        canonical_sha256(document),
    )


def context(target=TARGET, purpose="slice-validation", run_id=RUN_ID):
    """Select exact current-request identity and same-revision control."""
    return CompilationContext(
        "release-request:" + "b" * 64,
        purpose,
        run_id,
        None if purpose == "live-release" else 1,
        target,
        "compile-ruby-model",
        f"workflow-delivery-v3:{target}",
        catalog_digest(),
        "buddy" if purpose == "release-simulation" else None,
        RUBY_RELEASE_UNIT if purpose == "release-simulation" else None,
    )


def contents():
    """Use actual maintained tool, product and authoring bytes."""
    paths = [*RUBY_GLOBALS, RUBY_POLICY]
    paths.extend(
        p.relative_to(ROOT).as_posix()
        for p in (ROOT / RUBY_ROOT).rglob("*")
        if p.is_file()
    )
    return {p: (ROOT / p).read_bytes() for p in sorted(paths)}


def admitted(ctx=None, source=None, *, profile=None, epoch=EPOCH):
    """Admit compile-only modeled discovery against meaningful source bytes."""
    ctx = context() if ctx is None else ctx
    source = contents() if source is None else source
    manifest = ruby_provider_manifest(ctx)
    nbgv = RubyNbgvFacts(
        canonicalize(
            {
                "SemVer2": "0.1.0-beta.7",
                "GitCommitId": ctx.target,
                "VersionHeight": 7,
                "PublicRelease": True,
            }
        ),
        "0.1.0.pre.beta.7",
    )
    result = RubyProviderResult(
        provider_binding(manifest, "ruby-smoke"),
        CheckoutEvidence(
            ctx.target,
            ctx.target,
            shallow=False,
            ancestry_complete=True,
            tags_complete=True,
            credentials_persisted=False,
            authoritative_remote=AUTHORITATIVE_REMOTE,
            authoritative_remote_url="file:///ruby-integration-fixture.git",
            tag_refspec=TAG_REFSPEC,
        ),
        nbgv,
        tuple(sorted((p, ruby_digest(b)) for p, b in source.items())),
        epoch,
        canonicalize(PROFILE if profile is None else profile),
        canonicalize(
            {
                "name": RUBY_RELEASE_UNIT,
                "version": nbgv.native_version,
                "platform": "ruby",
                "files": list(RUBY_PACKAGE_FILES),
                "required-ruby": ">= 4.0",
            }
        ),
    )
    request = reference(manifest.to_document(), 101, "request.json")
    ref = reference(result.to_document(), 102, "provider.json")
    transport = ArtifactTransportIdentity(
        ref.artifact_id,
        manifest.requests[0].expected_result_identity,
        ref.artifact_url,
        ref.artifact_digest,
        "discover-ruby",
        ctx.workflow_run_id,
        ctx.run_attempt,
    )
    return admit_ruby_provider_facts(
        canonicalize(result.to_document()),
        manifest=manifest,
        request_reference=request,
        result_reference=ref,
        result_transport=transport,
    )


def with_result(facts, result):
    """Rebind modified fixture bytes while retaining transport."""
    return replace(
        facts,
        result=result,
        result_reference=replace(
            facts.result_reference, payload_digest=result.result_digest
        ),
    )


def model(purpose="slice-validation", run_id=RUN_ID):
    """Build a modeled record for application contracts, not native proof."""
    facts = admitted(context(purpose=purpose, run_id=run_id))
    return RubyRepositoryModelSnapshot(
        facts.manifest.context,
        facts.result,
        facts.request_reference,
        facts.result_reference,
    )


def committed_source(root, source=None):
    """Create actual immutable Git input bytes with a fixed committer epoch."""
    source = contents() if source is None else source
    root.mkdir(parents=True)
    for path, content in source.items():
        destination = root / path
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(content)
    git(root, "init", "--quiet", "--initial-branch=main")
    commit(root)
    git(
        root,
        "-c",
        "user.name=Ruby Fixture",
        "-c",
        "user.email=ruby-fixture@example.invalid",
        "commit",
        "--amend",
        "--quiet",
        "--no-edit",
        f"--date=@{EPOCH - 100} +0000",
    )
    return root, git(root, "rev-parse", "HEAD")


def native_original(root, purpose="slice-validation"):
    """Build one original using the actual pinned native profile."""
    source = contents()
    repository, target = committed_source(root / "source", source)
    profile = ruby_profile(cwd=repository, home=root / "profile-home")
    facts = admitted(context(target, purpose), source, profile=profile)
    snapshot = compile_ruby_repository_model(repository, facts)
    distribution = build_ruby_package(repository, snapshot.build_request())
    ref = ArtifactReference(
        801,
        "sha256:" + "8" * 64,
        "https://example.invalid/artifacts/801",
        distribution.filename,
        distribution.digest,
    )
    transport = ArtifactTransportIdentity(
        ref.artifact_id,
        "ruby-original",
        ref.artifact_url,
        ref.artifact_digest,
        "build-ruby",
        snapshot.context.workflow_run_id,
        snapshot.context.run_attempt,
    )
    artifact = RubyArtifact(
        distribution.filename,
        len(distribution.content),
        distribution.witness,
        ref,
        transport,
    )
    return snapshot, artifact, distribution


def consumer(distribution):
    """Substitute clean-consumer output for CI orchestration tests only."""
    return {
        "schema": "workflow-delivery/v3/ruby-consumer-evidence",
        "artifact-digest": distribution.digest,
        "witness-digest": canonical_sha256(distribution.witness.to_document()),
        "project-id": RUBY_RELEASE_UNIT,
        "version": distribution.witness.nbgv.native_version,
        "install-output": "Successfully installed the isolated Ruby smoke gem",
    }


def ready_document(registry):
    """Model configuration without claiming service admission."""
    doc = blocked_ruby_governance(registry)
    doc.update(
        {
            "live_enabled": True,
            "state": "ready",
            "inspected-at": (NOW - timedelta(hours=1))
            .isoformat()
            .replace("+00:00", "Z"),
            "expires-at": (NOW + timedelta(days=1))
            .isoformat()
            .replace("+00:00", "Z"),
            "source-evidence-revision": TARGET,
            "configuration": {
                "publisher-registration": ruby_publisher_tuple(registry),
                "package-ownership": {
                    "package": RUBY_RELEASE_UNIT,
                    "account": "hcoona"
                    if registry.name == "github-packages"
                    else "Raffle2282",
                    "repository": "hcoona/three",
                    "confirmed": True,
                },
                "package-access": {
                    "visibility": "private"
                    if registry.name == "github-packages"
                    else "public",
                    "repository": "hcoona/three",
                    "read": True,
                    "publish": True,
                    "authentication": "github-job-token"
                    if registry.name == "github-packages"
                    else "ruby-oidc",
                },
                "environment-id": 1901,
                "reviewer-id": 712433,
                "prevent-self-review": False,
                "can-admins-bypass": False,
                "wait-timer": 0,
                "protected-main-only": True,
                "sentinel": registry.environment + "/v1",
                "secret-count": 0,
                "accepted-writers": ["hcoona"],
                "attestation-digest": "sha256:" + "f" * 64,
                "scope-limitation": (
                    "Reviewed service registration attestation; no runtime "
                    "registration inventory or token-decoding scope proof."
                ),
            },
        }
    )
    return doc


def governance(name="github-packages", observed_at=NOW):
    """Return modeled, independently bound admission only."""
    registry = RubyRegistry(name)
    return RubyGovernance(
        registry, canonicalize(ready_document(registry)), TARGET, observed_at
    )
