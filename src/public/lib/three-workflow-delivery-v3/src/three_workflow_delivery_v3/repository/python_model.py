"""Closed Python Repository Model compilation from admitted Provider facts."""

from __future__ import annotations

from dataclasses import dataclass
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3.adapters.python import (
    PythonBuildRequest,
    PythonPackageTargetWitness,
)
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
from three_workflow_delivery_v3.repository.python_provider import (
    PYTHON_BUILD_DEFINITION,
    PYTHON_POLICY,
    PYTHON_RELEASE_UNIT,
    PYTHON_ROOT,
    PYTHON_TOOLCHAIN,
    PythonProviderResult,
    python_digest,
    python_input_candidates,
    python_object,
    python_provider_result_from_document,
)

if TYPE_CHECKING:
    from pathlib import Path


PYTHON_QUALITY_PRESET = "python/hcoona-release-smoke-python-v1"
PYTHON_QUALITY = QUALITY_PRESETS[PYTHON_QUALITY_PRESET].required
PYTHON_DESCRIPTOR = f"{PYTHON_ROOT}/workflow-delivery.release-unit.yml"
PYTHON_QUALITY_PATH = f"{PYTHON_ROOT}/workflow-delivery.quality.yml"


def python_policy_document() -> dict[str, JsonValue]:
    """Return the closed two-destination policy, with independent admissions."""
    return {
        "schema": "workflow-delivery/v3/python-release-policy",
        "release-unit": PYTHON_RELEASE_UNIT,
        "quality": cast("list[JsonValue]", list(PYTHON_QUALITY)),
        "channels": cast(
            "dict[str, JsonValue]",
            {
                channel: {
                    "destination": f"python/{registry}-v1",
                    "governance": (
                        ".github/workflow-delivery/governance/"
                        f"hcoona-release-smoke-python-{registry}.json"
                    ),
                }
                for channel, registry in (
                    ("buddy", "testpypi"),
                    ("official", "pypi"),
                )
            },
        ),
    }


def python_descriptor_document() -> dict[str, JsonValue]:
    """Close one build and both original native outputs in their order."""
    return {
        "schema": "workflow-delivery/v3/release-unit",
        "release-unit": PYTHON_RELEASE_UNIT,
        "builds": [
            {
                "id": "python-distributions",
                "definition": PYTHON_BUILD_DEFINITION,
                "entry-point": "pyproject.toml",
                "outputs": [
                    {
                        "id": "wheel",
                        "role": "primary-package",
                        "kind": "python-wheel",
                    },
                    {
                        "id": "sdist",
                        "role": "source-package",
                        "kind": "python-sdist",
                    },
                ],
            }
        ],
    }


def python_provider_manifest(
    context: CompilationContext,
) -> ProviderRequestManifest:
    """Freeze the Provider's request before unprivileged target evaluation."""
    validate_compilation_context(context)
    if (
        context.purpose == "release-simulation"
        and context.release_unit != PYTHON_RELEASE_UNIT
    ):
        message = "Python simulation requires its own selected unit"
        raise ValueError(message)
    request_digest = canonical_sha256(
        {
            "schema": "workflow-delivery/v3/python-provider-request",
            "context": _context_document(context),
            "entry-point": f"{PYTHON_ROOT}/pyproject.toml",
            "toolchain": [
                [name, version] for name, version in PYTHON_TOOLCHAIN
            ],
            "build-definition": PYTHON_BUILD_DEFINITION,
            "outputs": ["wheel", "sdist"],
        }
    )
    return ProviderRequestManifest(
        context,
        (
            ProviderRequest(
                "python-smoke",
                "python/uv-nbgv-v1",
                "python/uv-nbgv-v1",
                "target-evaluating",
                "discover-python",
                request_digest,
                f"python/uv-nbgv-v1:{context.request_id}",
            ),
        ),
    )


@dataclass(frozen=True, slots=True)
class AdmittedPythonProviderFacts:
    """Current request and immutable transported Python facts."""

    manifest: ProviderRequestManifest
    result: PythonProviderResult
    request_reference: ArtifactReference
    result_reference: ArtifactReference
    result_transport: ArtifactTransportIdentity

    def __post_init__(self) -> None:
        """Require exact request, producer, run and transport bindings."""
        context = self.manifest.context
        if self.manifest != python_provider_manifest(context):
            message = "Python Provider Manifest differs from reviewed control"
            raise ValueError(message)
        transport = self.result_transport
        reference = self.result_reference
        if (
            self.result.binding
            != provider_binding(self.manifest, "python-smoke")
            or self.request_reference.payload_digest
            != self.manifest.manifest_digest
            or reference.payload_digest != self.result.result_digest
            or reference.artifact_id != transport.artifact_id
            or reference.artifact_digest != transport.transport_digest
            or reference.artifact_url != transport.artifact_url
            or transport.producer != "discover-python"
            or transport.workflow_run_id != context.workflow_run_id
            or transport.run_attempt != context.run_attempt
        ):
            message = (
                "Python Provider transport or current-request binding mismatch"
            )
            raise ValueError(message)


def admit_python_provider_facts(
    payload: bytes,
    *,
    manifest: ProviderRequestManifest,
    request_reference: ArtifactReference,
    result_reference: ArtifactReference,
    result_transport: ArtifactTransportIdentity,
) -> AdmittedPythonProviderFacts:
    """Admit canonical facts through their immutable reference."""
    if python_digest(payload) != result_reference.payload_digest:
        message = "Python Provider payload digest mismatch"
        raise ValueError(message)
    return AdmittedPythonProviderFacts(
        manifest,
        python_provider_result_from_document(parse_canonical_json(payload)),
        request_reference,
        result_reference,
        result_transport,
    )


@dataclass(frozen=True, slots=True)
class PythonRepositoryModelSnapshot:
    """Strict Python Model variant; legacy slice consumers reject its schema."""

    context: CompilationContext
    provider: PythonProviderResult
    request_reference: ArtifactReference
    provider_reference: ArtifactReference

    def __post_init__(self) -> None:
        """Close the canonical request-local model and source-selected pair."""
        manifest = python_provider_manifest(self.context)
        if (
            self.provider.binding != provider_binding(manifest, "python-smoke")
            or self.request_reference.payload_digest != manifest.manifest_digest
            or self.provider_reference.payload_digest
            != self.provider.result_digest
            or self.provider.nbgv.target != self.context.target
            or not {
                PYTHON_DESCRIPTOR,
                PYTHON_QUALITY_PATH,
                PYTHON_POLICY,
            }.issubset(dict(self.provider.source_input_manifest))
        ):
            message = "Python Repository Model current-source binding mismatch"
            raise ValueError(message)

    def to_document(self) -> dict[str, JsonValue]:
        """Serialize the purpose and separate destination sources."""
        return {
            "schema": "workflow-delivery/v3/python-repository-model-snapshot",
            "context": _context_document(self.context),
            "provider": self.provider.to_document(),
            "request-reference": self.request_reference.to_document(),
            "provider-reference": self.provider_reference.to_document(),
            "release-unit": python_descriptor_document(),
            "release-policy": python_policy_document(),
            "quality": cast("list[JsonValue]", list(PYTHON_QUALITY)),
            "reverse-index": cast(
                "dict[str, JsonValue]",
                {PYTHON_RELEASE_UNIT: ["python-distributions"]},
            ),
            "ready": True,
        }

    @property
    def snapshot_digest(self) -> str:
        """Return the immutable Model identity."""
        return canonical_sha256(self.to_document())

    def build_request(self) -> PythonBuildRequest:
        """Freeze the original pair for the owning CI or Release context."""
        return PythonBuildRequest(
            PythonPackageTargetWitness(
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
            self.provider.build_constraints,
        )


def python_repository_model_from_document(
    value: JsonValue,
) -> PythonRepositoryModelSnapshot:
    """Reject other variants, extra fields or altered contracts."""
    doc = python_object(
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
    model = PythonRepositoryModelSnapshot(
        _compilation_context_from_document(doc["context"]),
        python_provider_result_from_document(doc["provider"]),
        artifact_reference_from_document(doc["request-reference"]),
        artifact_reference_from_document(doc["provider-reference"]),
    )
    if canonicalize(model.to_document()) != canonicalize(doc):
        message = "Python Repository Model is not the closed normalized variant"
        raise ValueError(message)
    return model


def compile_python_repository_model(
    repo_root: Path,
    facts: AdmittedPythonProviderFacts,
) -> PythonRepositoryModelSnapshot:
    """Compile facts and target authoring without executing target code."""
    if type(facts) is not AdmittedPythonProviderFacts:
        message = "Python compilation requires admitted Provider facts"
        raise TypeError(message)
    context = facts.manifest.context
    target_paths = _git_target_paths(repo_root, context.target)
    expected = python_input_candidates(tuple(target_paths))
    inputs = facts.result.source_input_manifest
    if tuple(path for path, _ in inputs) != expected:
        message = (
            "Python Provider source manifest omits or substitutes target inputs"
        )
        raise ValueError(message)
    for path, digest in inputs:
        if (
            python_digest(
                _git_target_file_bytes(repo_root, context.target, path)
            )
            != digest
        ):
            message = "Python Provider source bytes differ from exact target"
            raise ValueError(message)
    expected_quality: dict[str, JsonValue] = {
        "schema": "workflow-delivery/v3/quality-selection",
        "ecosystems": {"python": {"preset": PYTHON_QUALITY_PRESET}},
    }
    for path, document in (
        (PYTHON_DESCRIPTOR, python_descriptor_document()),
        (PYTHON_QUALITY_PATH, expected_quality),
        (PYTHON_POLICY, python_policy_document()),
    ):
        actual = _parse_yaml(
            _git_target_file_bytes(repo_root, context.target, path).decode(
                "utf-8"
            ),
            source=path,
        )
        if actual != document:
            message = "Python authoring differs from the two-format contract"
            raise ValueError(message)
    model = PythonRepositoryModelSnapshot(
        context,
        facts.result,
        facts.request_reference,
        facts.result_reference,
    )
    python_repository_model_from_document(
        parse_canonical_json(canonicalize(model.to_document()))
    )
    return model
