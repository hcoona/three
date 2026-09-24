"""Shared Python byte provenance; domain decisions remain in CI and Release."""

from __future__ import annotations

from dataclasses import dataclass
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3.adapters.python import (
    PythonDistribution,
    PythonPackageTargetWitness,
    inspect_python_distribution,
    python_package_target_witness_from_document,
)
from three_workflow_delivery_v3.canonical import JsonValue, canonical_sha256
from three_workflow_delivery_v3.records.artifacts import (
    ArtifactReference,
    ArtifactTransportIdentity,
    artifact_reference_from_document,
)
from three_workflow_delivery_v3.repository.python_provider import (
    python_object,
    python_text,
)

if TYPE_CHECKING:
    from three_workflow_delivery_v3.repository.compiler import (
        CompilationContext,
    )


@dataclass(frozen=True, slots=True)
class PythonArtifact:
    """One original distribution and its immutable current-build transport."""

    variant: str
    filename: str
    byte_size: int
    witness: PythonPackageTargetWitness
    reference: ArtifactReference
    transport: ArtifactTransportIdentity

    def __post_init__(self) -> None:
        """Close the native format, source witness and transport identity."""
        if (
            self.variant not in {"wheel", "sdist"}
            or type(self.byte_size) is not int
            or self.byte_size <= 0
        ):
            message = "invalid Python artifact variant or size"
            raise ValueError(message)
        expected = (
            f"hcoona_release_smoke_python-{self.witness.nbgv.pep440_version}"
            + ("-py3-none-any.whl" if self.variant == "wheel" else ".tar.gz")
        )
        if (
            self.filename != expected
            or self.reference.payload_path != self.filename
            or self.reference.artifact_id != self.transport.artifact_id
            or self.reference.artifact_digest != self.transport.transport_digest
            or self.reference.artifact_url != self.transport.artifact_url
            or self.transport.producer != "build-python"
        ):
            message = "Python artifact native or transport identity mismatch"
            raise ValueError(message)

    @property
    def artifact_digest(self) -> str:
        """Return the complete transported artifact identity."""
        return canonical_sha256(self.to_document())

    def to_document(self) -> dict[str, JsonValue]:
        """Serialize exact provenance without carrying archive bytes."""
        return {
            "schema": "workflow-delivery/v3/python-artifact",
            "variant": self.variant,
            "filename": self.filename,
            "byte-size": self.byte_size,
            "witness": self.witness.to_document(),
            "reference": self.reference.to_document(),
            "transport": self.transport.to_document(),
        }

    def inspect(self, content: bytes) -> PythonDistribution:
        """Admit downloaded original bytes, never a rebuilt consumer wheel."""
        distribution = inspect_python_distribution(
            self.filename, content, self.variant, self.witness
        )
        if (
            distribution.digest != self.reference.payload_digest
            or len(content) != self.byte_size
        ):
            message = (
                "Python artifact payload differs from its immutable reference"
            )
            raise ValueError(message)
        return distribution


def python_artifact_from_document(value: JsonValue) -> PythonArtifact:
    """Read the strict native artifact and shared transport primitives."""
    doc = python_object(
        value,
        {
            "schema",
            "variant",
            "filename",
            "byte-size",
            "witness",
            "reference",
            "transport",
        },
    )
    if doc["schema"] != "workflow-delivery/v3/python-artifact":
        message = "foreign Python artifact schema"
        raise ValueError(message)
    transport = doc["transport"]
    if not isinstance(transport, dict):
        message = "invalid Python artifact transport"
        raise TypeError(message)
    keys = {
        "artifact-id",
        "artifact-name",
        "artifact-url",
        "transport-digest",
        "producer",
        "workflow-run-id",
    }
    python_object(
        transport,
        keys | ({"run-attempt"} if "run-attempt" in transport else set()),
    )
    if "run-attempt" in transport and type(transport["run-attempt"]) is not int:
        message = "Python transport run-attempt must be absent or an integer"
        raise TypeError(message)
    return PythonArtifact(
        python_text(doc["variant"]),
        python_text(doc["filename"]),
        cast("int", doc["byte-size"]),
        python_package_target_witness_from_document(doc["witness"]),
        artifact_reference_from_document(doc["reference"]),
        ArtifactTransportIdentity(
            cast("int", transport["artifact-id"]),
            python_text(transport["artifact-name"]),
            python_text(transport["artifact-url"]),
            python_text(transport["transport-digest"]),
            python_text(transport["producer"]),
            cast("int", transport["workflow-run-id"]),
            cast("int | None", transport.get("run-attempt")),
        ),
    )


def validate_python_artifact_pair(
    artifacts: tuple[PythonArtifact, ...],
    *,
    witness: PythonPackageTargetWitness,
    context: CompilationContext,
) -> None:
    """Require exactly both original formats from this purpose and build run."""
    if type(artifacts) is not tuple or tuple(a.variant for a in artifacts) != (
        "wheel",
        "sdist",
    ):
        message = "Python artifact set must contain wheel then sdist"
        raise ValueError(message)
    if len({a.reference.artifact_id for a in artifacts}) != len(artifacts):
        message = "Python originals require independent immutable transports"
        raise ValueError(message)
    for artifact in artifacts:
        if (
            artifact.witness != witness
            or artifact.transport.workflow_run_id != context.workflow_run_id
            or artifact.transport.run_attempt != context.run_attempt
            or artifact.witness.purpose != context.purpose
            or artifact.witness.target != context.target
        ):
            message = (
                "Python artifact purpose or current-build binding mismatch"
            )
            raise ValueError(message)
