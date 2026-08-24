"""Reusable immutable artifact transport and content identities."""

from __future__ import annotations

import re
from dataclasses import dataclass
from typing import TYPE_CHECKING
from urllib.parse import urlsplit

from three_workflow_delivery_v3.canonical import canonical_sha256

if TYPE_CHECKING:
    from three_workflow_delivery_v3.canonical import JsonValue

_SHA256_PATTERN = re.compile(r"sha256:[0-9a-f]{64}\Z")
_SHA512_PATTERN = re.compile(r"sha512:[0-9a-f]{128}\Z")


def _nonempty(value: object, *, field: str) -> str:
    if type(value) is not str or not value or value != value.strip():
        message = f"{field} must be a nonempty exact string"
        raise TypeError(message)
    return value


def _positive_integer(value: object, *, field: str) -> int:
    if type(value) is not int or value <= 0:
        message = f"{field} must be a positive non-Boolean integer"
        raise ValueError(message)
    return value


def _digest(value: object, *, field: str, sha512: bool = False) -> str:
    pattern = _SHA512_PATTERN if sha512 else _SHA256_PATTERN
    if type(value) is not str or pattern.fullmatch(value) is None:
        algorithm = "SHA-512" if sha512 else "SHA-256"
        message = f"{field} must be a prefixed lowercase {algorithm}"
        raise ValueError(message)
    return value


@dataclass(frozen=True, slots=True)
class ArtifactTransportIdentity:
    """Platform transport identity without context-specific CI semantics."""

    artifact_id: int
    artifact_name: str
    artifact_url: str
    transport_digest: str
    producer: str
    workflow_run_id: int
    run_attempt: int

    def __post_init__(self) -> None:
        """Reject malformed or non-current transport primitives."""
        _positive_integer(self.artifact_id, field="artifact_id")
        _nonempty(self.artifact_name, field="artifact_name")
        url = urlsplit(_nonempty(self.artifact_url, field="artifact_url"))
        if url.scheme != "https" or not url.netloc:
            message = "artifact_url must be an absolute HTTPS URL"
            raise ValueError(message)
        _digest(self.transport_digest, field="transport_digest")
        _nonempty(self.producer, field="producer")
        _positive_integer(self.workflow_run_id, field="workflow_run_id")
        _positive_integer(self.run_attempt, field="run_attempt")

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical transport identity."""
        return {
            "artifact-id": self.artifact_id,
            "artifact-name": self.artifact_name,
            "artifact-url": self.artifact_url,
            "transport-digest": self.transport_digest,
            "producer": self.producer,
            "workflow-run-id": self.workflow_run_id,
            "run-attempt": self.run_attempt,
        }

    @property
    def identity_digest(self) -> str:
        """Return the canonical transport identity digest."""
        return canonical_sha256(self.to_document())


@dataclass(frozen=True, slots=True)
class ArtifactContentIdentity:
    """Immutable byte and logical-output identity reusable across contexts."""

    output_id: str
    logical_role: str
    media_kind: str
    basename: str
    byte_size: int
    content_sha256: str
    content_sha512: str | None

    def __post_init__(self) -> None:
        """Reject malformed content identities."""
        _nonempty(self.output_id, field="output_id")
        _nonempty(self.logical_role, field="logical_role")
        _nonempty(self.media_kind, field="media_kind")
        _nonempty(self.basename, field="basename")
        _positive_integer(self.byte_size, field="byte_size")
        _digest(self.content_sha256, field="content_sha256")
        if self.content_sha512 is not None:
            _digest(
                self.content_sha512,
                field="content_sha512",
                sha512=True,
            )

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical content identity."""
        return {
            "output-id": self.output_id,
            "logical-role": self.logical_role,
            "media-kind": self.media_kind,
            "basename": self.basename,
            "byte-size": self.byte_size,
            "content-sha256": self.content_sha256,
            "content-sha512": self.content_sha512,
        }

    @property
    def identity_digest(self) -> str:
        """Return the canonical content identity digest."""
        return canonical_sha256(self.to_document())


__all__ = [
    "ArtifactContentIdentity",
    "ArtifactTransportIdentity",
]
