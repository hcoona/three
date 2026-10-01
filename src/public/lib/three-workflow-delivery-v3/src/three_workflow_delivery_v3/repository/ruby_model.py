"""Closed Ruby Repository Model compilation from admitted Provider facts."""

from __future__ import annotations

from dataclasses import dataclass
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3._ruby_native import (
    RUBY_BUILD_DEFINITION,
    RUBY_MANIFEST,
    RUBY_RELEASE_UNIT,
    RUBY_ROOT,
    RUBY_VERSION,
    RUBYGEMS_VERSION,
    ruby_digest,
    ruby_object,
)
from three_workflow_delivery_v3.adapters.ruby import (
    RubyBuildRequest,
    RubyPackageTargetWitness,
)
from three_workflow_delivery_v3.adapters.rubygems import RubyRegistry
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.catalogs import QUALITY_PRESETS
from three_workflow_delivery_v3.records.artifacts import (
    ArtifactReference,
    ArtifactTransportIdentity,
    artifact_reference_from_document,
)
from three_workflow_delivery_v3.repository.compiler import (
    CompilationContext,
    ProviderRequest,
    ProviderRequestManifest,
    _compilation_context_from_document,
    _context_document,
    _git_target_file_bytes,
    _git_target_paths,
    provider_binding,
    validate_compilation_context,
)
from three_workflow_delivery_v3.repository.descriptors import _parse_yaml
from three_workflow_delivery_v3.repository.node_provider import _run_command
from three_workflow_delivery_v3.repository.ruby_provider import (
    RubyProviderResult,
    ruby_input_candidates,
    ruby_provider_result_from_document,
)

if TYPE_CHECKING:
    from pathlib import Path


RUBY_POLICY = "eng/workflow-delivery/v3/policies/hcoona-release-smoke-ruby.yml"
RUBY_TOOLCHAIN = (
    ("ruby", RUBY_VERSION),
    ("rubygems", RUBYGEMS_VERSION),
    ("nbgv", "3.10.94"),
)
RUBY_QUALITY_PRESET = "ruby/hcoona-release-smoke-ruby-v1"
RUBY_QUALITY = QUALITY_PRESETS[RUBY_QUALITY_PRESET].required
RUBY_DESCRIPTOR = f"{RUBY_ROOT}/workflow-delivery.release-unit.yml"
RUBY_QUALITY_PATH = f"{RUBY_ROOT}/workflow-delivery.quality.yml"


def ruby_policy_document() -> dict[str, JsonValue]:
    """Return the closed two-destination policy, with independent admissions."""
    return {
        "schema": "workflow-delivery/v3/ruby-release-policy",
        "release-unit": RUBY_RELEASE_UNIT,
        "quality": cast("list[JsonValue]", list(RUBY_QUALITY)),
        "channels": cast(
            "dict[str, JsonValue]",
            {
                channel: {
                    "destination": RubyRegistry(registry).profile[
                        "destination"
                    ],
                    "governance": (
                        ".github/workflow-delivery/governance/"
                        f"hcoona-release-smoke-ruby-{registry}.json"
                    ),
                }
                for channel, registry in (
                    ("buddy", "github-packages"),
                    ("official", "rubygems"),
                )
            },
        ),
    }


def ruby_descriptor_document() -> dict[str, JsonValue]:
    """Close one build and the original native output."""
    return {
        "schema": "workflow-delivery/v3/release-unit",
        "release-unit": RUBY_RELEASE_UNIT,
        "builds": [
            {
                "id": "ruby-gem",
                "definition": RUBY_BUILD_DEFINITION,
                "entry-point": RUBY_MANIFEST,
                "outputs": [
                    {
                        "id": "gem",
                        "role": "primary-package",
                        "kind": "ruby-gem",
                    },
                ],
            }
        ],
    }


def ruby_provider_manifest(
    context: CompilationContext,
) -> ProviderRequestManifest:
    """Freeze the Provider's request before unprivileged target evaluation."""
    validate_compilation_context(context)
    if (
        context.purpose == "release-simulation"
        and context.release_unit != RUBY_RELEASE_UNIT
    ):
        message = "Ruby simulation requires its own selected unit"
        raise ValueError(message)
    request_digest = canonical_sha256(
        {
            "schema": "workflow-delivery/v3/ruby-provider-request",
            "context": _context_document(context),
            "entry-point": f"{RUBY_ROOT}/{RUBY_MANIFEST}",
            "toolchain": [[name, version] for name, version in RUBY_TOOLCHAIN],
            "build-definition": RUBY_BUILD_DEFINITION,
            "outputs": ["gem"],
        }
    )
    return ProviderRequestManifest(
        context,
        (
            ProviderRequest(
                "ruby-smoke",
                "ruby/nbgv-v1",
                "ruby/nbgv-v1",
                "target-evaluating",
                "discover-ruby",
                request_digest,
                f"ruby/nbgv-v1:{context.request_id}",
            ),
        ),
    )


@dataclass(frozen=True, slots=True)
class AdmittedRubyProviderFacts:
    """Current request and immutable transported Ruby facts."""

    manifest: ProviderRequestManifest
    result: RubyProviderResult
    request_reference: ArtifactReference
    result_reference: ArtifactReference
    result_transport: ArtifactTransportIdentity

    def __post_init__(self) -> None:
        """Require exact request, producer, run and transport bindings."""
        context = self.manifest.context
        if self.manifest != ruby_provider_manifest(context):
            message = "Ruby Provider Manifest differs from reviewed control"
            raise ValueError(message)
        transport = self.result_transport
        reference = self.result_reference
        if (
            self.result.binding != provider_binding(self.manifest, "ruby-smoke")
            or self.request_reference.payload_digest
            != self.manifest.manifest_digest
            or reference.payload_digest != self.result.result_digest
            or reference.artifact_id != transport.artifact_id
            or reference.artifact_digest != transport.transport_digest
            or reference.artifact_url != transport.artifact_url
            or transport.producer != "discover-ruby"
            or transport.workflow_run_id != context.workflow_run_id
            or transport.run_attempt != context.run_attempt
        ):
            message = (
                "Ruby Provider transport or current-request binding mismatch"
            )
            raise ValueError(message)


def admit_ruby_provider_facts(
    payload: bytes,
    *,
    manifest: ProviderRequestManifest,
    request_reference: ArtifactReference,
    result_reference: ArtifactReference,
    result_transport: ArtifactTransportIdentity,
) -> AdmittedRubyProviderFacts:
    """Admit canonical facts through their immutable reference."""
    if ruby_digest(payload) != result_reference.payload_digest:
        message = "Ruby Provider payload digest mismatch"
        raise ValueError(message)
    return AdmittedRubyProviderFacts(
        manifest,
        ruby_provider_result_from_document(parse_canonical_json(payload)),
        request_reference,
        result_reference,
        result_transport,
    )


@dataclass(frozen=True, slots=True)
class RubyRepositoryModelSnapshot:
    """Strict Ruby Model variant; legacy slice consumers reject its schema."""

    context: CompilationContext
    provider: RubyProviderResult
    request_reference: ArtifactReference
    provider_reference: ArtifactReference

    def __post_init__(self) -> None:
        """Close the canonical request-local model and source-selected gem."""
        manifest = ruby_provider_manifest(self.context)
        if (
            self.provider.binding != provider_binding(manifest, "ruby-smoke")
            or self.request_reference.payload_digest != manifest.manifest_digest
            or self.provider_reference.payload_digest
            != self.provider.result_digest
            or self.provider.nbgv.target != self.context.target
            or not {
                RUBY_DESCRIPTOR,
                RUBY_QUALITY_PATH,
                RUBY_POLICY,
            }.issubset(dict(self.provider.source_input_manifest))
        ):
            message = "Ruby Repository Model current-source binding mismatch"
            raise ValueError(message)

    def to_document(self) -> dict[str, JsonValue]:
        """Serialize the purpose and separate destination sources."""
        return {
            "schema": "workflow-delivery/v3/ruby-repository-model-snapshot",
            "context": _context_document(self.context),
            "provider": self.provider.to_document(),
            "request-reference": self.request_reference.to_document(),
            "provider-reference": self.provider_reference.to_document(),
            "release-unit": ruby_descriptor_document(),
            "release-policy": ruby_policy_document(),
            "quality": cast("list[JsonValue]", list(RUBY_QUALITY)),
            "reverse-index": cast(
                "dict[str, JsonValue]",
                {RUBY_RELEASE_UNIT: ["ruby-gem"]},
            ),
            "ready": True,
        }

    @property
    def snapshot_digest(self) -> str:
        """Return the immutable Model identity."""
        return canonical_sha256(self.to_document())

    def build_request(self) -> RubyBuildRequest:
        """Freeze the original gem for the owning CI or Release context."""
        return RubyBuildRequest(
            RubyPackageTargetWitness(
                self.context.target,
                self.provider.nbgv,
                self.context.catalog_digest,
                canonical_sha256(
                    {
                        "schema": "workflow-delivery/v3/control-identity",
                        "identity": self.context.control,
                    }
                ),
                self.context.purpose,
            ),
            self.provider.source_input_manifest,
            self.provider.source_date_epoch,
            self.provider.native_profile,
        )


def ruby_repository_model_from_document(
    value: JsonValue,
) -> RubyRepositoryModelSnapshot:
    """Reject other variants, extra fields or altered contracts."""
    doc = ruby_object(
        value,
        {
            "schema",
            "context",
            "provider",
            "request-reference",
            "provider-reference",
            "release-unit",
            "release-policy",
            "quality",
            "reverse-index",
            "ready",
        },
    )
    model = RubyRepositoryModelSnapshot(
        _compilation_context_from_document(doc["context"]),
        ruby_provider_result_from_document(doc["provider"]),
        artifact_reference_from_document(doc["request-reference"]),
        artifact_reference_from_document(doc["provider-reference"]),
    )
    if canonicalize(model.to_document()) != canonicalize(doc):
        message = "Ruby Repository Model is not the closed normalized variant"
        raise ValueError(message)
    return model


def compile_ruby_repository_model(
    repo_root: Path,
    facts: AdmittedRubyProviderFacts,
) -> RubyRepositoryModelSnapshot:
    """Compile facts and target authoring without executing target code."""
    if type(facts) is not AdmittedRubyProviderFacts:
        message = "Ruby compilation requires admitted Provider facts"
        raise TypeError(message)
    context = facts.manifest.context
    epoch = int(
        _run_command(
            ("git", "show", "-s", "--format=%ct", context.target), repo_root
        )
    )
    if epoch != facts.result.source_date_epoch:
        message = "Ruby Provider epoch differs from the immutable target"
        raise ValueError(message)
    target_paths = _git_target_paths(repo_root, context.target)
    expected = ruby_input_candidates(tuple(target_paths))
    inputs = facts.result.source_input_manifest
    if tuple(path for path, _ in inputs) != expected:
        message = (
            "Ruby Provider source manifest omits or substitutes target inputs"
        )
        raise ValueError(message)
    for path, digest in inputs:
        if (
            ruby_digest(_git_target_file_bytes(repo_root, context.target, path))
            != digest
        ):
            message = "Ruby Provider source bytes differ from exact target"
            raise ValueError(message)
    expected_quality: dict[str, JsonValue] = {
        "schema": "workflow-delivery/v3/quality-selection",
        "ecosystems": {"ruby": {"preset": RUBY_QUALITY_PRESET}},
    }
    for path, document in (
        (RUBY_DESCRIPTOR, ruby_descriptor_document()),
        (RUBY_QUALITY_PATH, expected_quality),
        (RUBY_POLICY, ruby_policy_document()),
    ):
        actual = _parse_yaml(
            _git_target_file_bytes(repo_root, context.target, path).decode(
                "utf-8"
            ),
            source=path,
        )
        if actual != document:
            message = "Ruby authoring differs from the one-gem contract"
            raise ValueError(message)
    model = RubyRepositoryModelSnapshot(
        context,
        facts.result,
        facts.request_reference,
        facts.result_reference,
    )
    ruby_repository_model_from_document(
        parse_canonical_json(canonicalize(model.to_document()))
    )
    return model
