"""Shared Ruby byte provenance; domain decisions remain in CI and Release."""

from __future__ import annotations

from dataclasses import dataclass
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3._ruby_native import (
    RUBY_BYTE_LIMIT,
    RUBY_RELEASE_UNIT,
    ruby_object,
    ruby_text,
)
from three_workflow_delivery_v3.adapters.ruby import (
    RubyDistribution,
    RubyPackageTargetWitness,
    inspect_ruby_distribution,
    ruby_package_target_witness_from_document,
)
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    parse_canonical_json,
)
from three_workflow_delivery_v3.records.artifacts import (
    ArtifactReference,
    ArtifactTransportIdentity,
    artifact_reference_from_document,
)

if TYPE_CHECKING:
    from three_workflow_delivery_v3.repository.compiler import (
        CompilationContext,
    )


@dataclass(frozen=True, slots=True)
class RubyArtifact:
    """One original gem and its immutable current-build transport."""

    filename: str
    byte_size: int
    witness: RubyPackageTargetWitness
    reference: ArtifactReference
    transport: ArtifactTransportIdentity

    def __post_init__(self) -> None:
        """Close the native name, source witness and transport identity."""
        if (
            type(self.byte_size) is not int
            or not 0 < self.byte_size <= RUBY_BYTE_LIMIT
        ):
            message = "invalid Ruby artifact size"
            raise ValueError(message)
        expected = f"{RUBY_RELEASE_UNIT}-{self.witness.nbgv.native_version}.gem"
        if (
            self.filename != expected
            or self.reference.payload_path != self.filename
            or self.reference.artifact_id != self.transport.artifact_id
            or self.reference.artifact_digest != self.transport.transport_digest
            or self.reference.artifact_url != self.transport.artifact_url
            or self.transport.producer != "build-ruby"
        ):
            message = "Ruby artifact native or transport identity mismatch"
            raise ValueError(message)

    @property
    def artifact_digest(self) -> str:
        """Return the complete transported artifact identity."""
        return canonical_sha256(self.to_document())

    def to_document(self) -> dict[str, JsonValue]:
        """Serialize exact provenance without carrying archive bytes."""
        return {
            "schema": "workflow-delivery/v3/ruby-artifact",
            "filename": self.filename,
            "byte-size": self.byte_size,
            "witness": self.witness.to_document(),
            "reference": self.reference.to_document(),
            "transport": self.transport.to_document(),
        }

    def inspect(self, content: bytes) -> RubyDistribution:
        """Admit downloaded original bytes without repacking."""
        distribution = inspect_ruby_distribution(
            self.filename, content, self.witness
        )
        if (
            distribution.digest != self.reference.payload_digest
            or len(content) != self.byte_size
        ):
            message = (
                "Ruby artifact payload differs from its immutable reference"
            )
            raise ValueError(message)
        return distribution


def ruby_artifact_from_document(value: JsonValue) -> RubyArtifact:
    """Read the strict native artifact and shared transport primitives."""
    doc = ruby_object(
        value,
        {
            "schema",
            "filename",
            "byte-size",
            "witness",
            "reference",
            "transport",
        },
    )
    if doc["schema"] != "workflow-delivery/v3/ruby-artifact":
        message = "foreign Ruby artifact schema"
        raise ValueError(message)
    transport = doc["transport"]
    if not isinstance(transport, dict):
        message = "invalid Ruby artifact transport"
        raise TypeError(message)
    keys = {
        "artifact-id",
        "artifact-name",
        "artifact-url",
        "transport-digest",
        "producer",
        "workflow-run-id",
    }
    ruby_object(
        transport,
        keys | ({"run-attempt"} if "run-attempt" in transport else set()),
    )
    if "run-attempt" in transport and type(transport["run-attempt"]) is not int:
        message = "Ruby transport run-attempt must be absent or an integer"
        raise TypeError(message)
    return RubyArtifact(
        ruby_text(doc["filename"]),
        cast("int", doc["byte-size"]),
        ruby_package_target_witness_from_document(doc["witness"]),
        artifact_reference_from_document(doc["reference"]),
        ArtifactTransportIdentity(
            cast("int", transport["artifact-id"]),
            ruby_text(transport["artifact-name"]),
            ruby_text(transport["artifact-url"]),
            ruby_text(transport["transport-digest"]),
            ruby_text(transport["producer"]),
            cast("int", transport["workflow-run-id"]),
            cast("int | None", transport.get("run-attempt")),
        ),
    )


def validate_ruby_artifact(
    artifact: RubyArtifact,
    *,
    witness: RubyPackageTargetWitness,
    context: CompilationContext,
) -> None:
    """Require this purpose's original gem from the current Build run."""
    if type(artifact) is not RubyArtifact:
        message = "Ruby qualification requires one original gem artifact"
        raise TypeError(message)
    if (
        artifact.witness != witness
        or artifact.transport.workflow_run_id != context.workflow_run_id
        or artifact.transport.run_attempt != context.run_attempt
        or artifact.witness.purpose != context.purpose
        or artifact.witness.target != context.target
    ):
        message = "Ruby artifact purpose or current-build binding mismatch"
        raise ValueError(message)


def validate_ruby_quality_detail(
    artifact: RubyArtifact, definition: str, result: str, detail: bytes
) -> None:
    """Admit intrinsic original-byte and consumer facts for either owner."""
    document = parse_canonical_json(detail)
    if result == "failed":
        error = ruby_object(document, {"error-kind"})
        ruby_text(error["error-kind"])
        return
    if result != "passed":
        message = "invalid Ruby quality detail result"
        raise ValueError(message)
    if definition == "ruby/gem-contents-v1":
        expected: dict[str, JsonValue] = {
            "digest": artifact.reference.payload_digest
        }
        ruby_object(document, set(expected))
    elif definition == "ruby/gem-install-require-v1":
        expected = {
            "schema": "workflow-delivery/v3/ruby-consumer-evidence",
            "artifact-digest": artifact.reference.payload_digest,
            "witness-digest": canonical_sha256(artifact.witness.to_document()),
            "project-id": RUBY_RELEASE_UNIT,
            "version": artifact.witness.nbgv.native_version,
        }
        consumer = ruby_object(document, set(expected) | {"install-output"})
        if not isinstance(consumer["install-output"], str):
            message = "Ruby consumer install output must be text"
            raise TypeError(message)
    else:
        message = "unknown Ruby quality detail definition"
        raise ValueError(message)
    if not isinstance(document, dict) or any(
        document[key] != value for key, value in expected.items()
    ):
        message = "Ruby quality detail differs from its original artifact"
        raise ValueError(message)
