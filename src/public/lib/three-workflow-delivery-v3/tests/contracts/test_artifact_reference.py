"""Contract tests for immutable artifact references and their documents."""

from __future__ import annotations

from dataclasses import FrozenInstanceError
from typing import TYPE_CHECKING, TypedDict

import pytest
from three_workflow_delivery_v3.records.artifacts import (
    ArtifactReference,
    artifact_reference_from_document,
)

if TYPE_CHECKING:
    from typing import Literal

    from three_workflow_delivery_v3.canonical import JsonValue

_ARTIFACT_ID = 17
_ARTIFACT_DIGEST = "sha256:" + ("a" * 64)
_ARTIFACT_URL = "https://example.test/artifacts/17"
_PAYLOAD_PATH = "payload/release.tar.zst"
_PAYLOAD_DIGEST = "sha256:" + ("b" * 64)


class _ArtifactReferenceArguments(TypedDict):
    """Typed constructor arguments for the canonical reference."""

    artifact_id: int
    artifact_digest: str
    artifact_url: str
    payload_path: str
    payload_digest: str


def _valid_reference_kwargs() -> _ArtifactReferenceArguments:
    """Return fresh normalized constructor arguments."""
    return {
        "artifact_id": _ARTIFACT_ID,
        "artifact_digest": _ARTIFACT_DIGEST,
        "artifact_url": _ARTIFACT_URL,
        "payload_path": _PAYLOAD_PATH,
        "payload_digest": _PAYLOAD_DIGEST,
    }


def _valid_document() -> dict[str, JsonValue]:
    """Return a fresh normalized Artifact Reference document."""
    return {
        "artifact-id": _ARTIFACT_ID,
        "artifact-digest": _ARTIFACT_DIGEST,
        "artifact-url": _ARTIFACT_URL,
        "payload-path": _PAYLOAD_PATH,
        "payload-digest": _PAYLOAD_DIGEST,
    }


def test_artifact_reference_is_frozen_slotted_and_serializes_exact_contract_fields() -> (  # noqa: E501
    None
):
    """Preserve values, closed shape, fresh serialization, and immutability."""
    reference = ArtifactReference(**_valid_reference_kwargs())

    assert reference.artifact_id == _ARTIFACT_ID
    assert reference.artifact_digest == _ARTIFACT_DIGEST
    assert reference.artifact_url == _ARTIFACT_URL
    assert reference.payload_path == _PAYLOAD_PATH
    assert reference.payload_digest == _PAYLOAD_DIGEST

    first_document = reference.to_document()
    second_document = reference.to_document()
    assert type(first_document) is dict
    assert first_document == _valid_document()
    assert second_document == first_document
    assert second_document is not first_document
    assert "schema" not in first_document

    assert hasattr(ArtifactReference, "__slots__")
    assert not hasattr(reference, "__dict__")
    field_name = "artifact_id"
    with pytest.raises(FrozenInstanceError):
        setattr(reference, field_name, 18)


@pytest.mark.parametrize(
    "artifact_id",
    [
        pytest.param(0, id="zero"),
        pytest.param(-1, id="negative"),
        pytest.param(True, id="true"),
        pytest.param(False, id="false"),
    ],
)
def test_artifact_reference_rejects_nonpositive_or_boolean_artifact_id(
    artifact_id: int,
) -> None:
    """Reject the complete nonpositive and Boolean ID boundary."""
    arguments = _valid_reference_kwargs()
    arguments["artifact_id"] = artifact_id

    with pytest.raises(
        ValueError,
        match="positive non-Boolean integer",
    ):
        ArtifactReference(**arguments)


@pytest.mark.parametrize(
    ("field_name", "digest"),
    [
        pytest.param(
            "artifact_digest",
            "a" * 64,
            id="artifact-digest-missing-prefix",
        ),
        pytest.param(
            "artifact_digest",
            "sha256:" + ("a" * 63),
            id="artifact-digest-too-short",
        ),
        pytest.param(
            "artifact_digest",
            "sha256:" + ("a" * 65),
            id="artifact-digest-too-long",
        ),
        pytest.param(
            "artifact_digest",
            "sha256:" + ("a" * 63) + "g",
            id="artifact-digest-non-hex",
        ),
        pytest.param(
            "artifact_digest",
            "sha256:" + ("A" * 64),
            id="artifact-digest-uppercase",
        ),
        pytest.param(
            "payload_digest",
            "b" * 64,
            id="payload-digest-missing-prefix",
        ),
        pytest.param(
            "payload_digest",
            "sha256:" + ("b" * 63),
            id="payload-digest-too-short",
        ),
        pytest.param(
            "payload_digest",
            "sha256:" + ("b" * 65),
            id="payload-digest-too-long",
        ),
        pytest.param(
            "payload_digest",
            "sha256:" + ("b" * 63) + "g",
            id="payload-digest-non-hex",
        ),
        pytest.param(
            "payload_digest",
            "sha256:" + ("B" * 64),
            id="payload-digest-uppercase",
        ),
    ],
)
def test_artifact_reference_rejects_malformed_digest(
    field_name: Literal["artifact_digest", "payload_digest"],
    digest: str,
) -> None:
    """Reject each malformed digest independently and identify its field."""
    arguments = _valid_reference_kwargs()
    arguments[field_name] = digest

    with pytest.raises(ValueError, match=field_name) as exc_info:
        ArtifactReference(**arguments)

    assert "SHA-256" in str(exc_info.value)


@pytest.mark.parametrize(
    "artifact_url",
    [
        pytest.param(
            "http://example.test/artifacts/17",
            id="http-url",
        ),
        pytest.param(
            "artifacts/17",
            id="relative-url-without-scheme",
        ),
        pytest.param(
            "https:artifacts/17",
            id="relative-url-with-https-scheme",
        ),
    ],
)
def test_artifact_reference_rejects_non_https_or_relative_artifact_url(
    artifact_url: str,
) -> None:
    """Reject non-HTTPS and non-absolute artifact locations."""
    arguments = _valid_reference_kwargs()
    arguments["artifact_url"] = artifact_url

    with pytest.raises(ValueError, match="absolute HTTPS URL"):
        ArtifactReference(**arguments)


@pytest.mark.parametrize(
    "payload_path",
    [
        pytest.param(
            "payload//release.tar.zst",
            id="duplicate-separator",
        ),
        pytest.param(
            "/payload/release.tar.zst",
            id="absolute-path",
        ),
        pytest.param(
            "payload/../release.tar.zst",
            id="parent-traversal",
        ),
        pytest.param(
            r"payload\release.tar.zst",
            id="backslash-separator",
        ),
    ],
)
def test_artifact_reference_rejects_non_normalized_absolute_or_traversing_payload_path(  # noqa: E501
    payload_path: str,
) -> None:
    """Reject non-normalized, absolute, and parent-traversing payload paths."""
    arguments = _valid_reference_kwargs()
    arguments["payload_path"] = payload_path

    with pytest.raises(
        ValueError,
        match="normalized relative POSIX path",
    ):
        ArtifactReference(**arguments)


def test_artifact_reference_from_document_round_trips_normalized_document() -> (
    None
):
    """Parse a normalized closed document without changing its value."""
    document = _valid_document()

    reference = artifact_reference_from_document(document)

    assert isinstance(reference, ArtifactReference)
    assert reference == ArtifactReference(**_valid_reference_kwargs())
    assert reference.to_document() == document


def test_artifact_reference_from_document_rejects_non_object() -> None:
    """Reject a representative JSON value that is not an object."""
    value: JsonValue = ["not", "an", "object"]

    with pytest.raises(TypeError, match="must be an object"):
        artifact_reference_from_document(value)


@pytest.mark.parametrize(
    "field_name",
    [
        pytest.param("artifact-id", id="artifact-id"),
        pytest.param("artifact-digest", id="artifact-digest"),
        pytest.param("artifact-url", id="artifact-url"),
        pytest.param("payload-path", id="payload-path"),
        pytest.param("payload-digest", id="payload-digest"),
    ],
)
def test_artifact_reference_from_document_rejects_each_missing_field(
    field_name: str,
) -> None:
    """Reject each omission without an unknown-field confounder."""
    document = _valid_document()
    del document[field_name]

    with pytest.raises(
        ValueError,
        match="missing required field",
    ) as exc_info:
        artifact_reference_from_document(document)

    assert field_name in str(exc_info.value)


def test_artifact_reference_from_document_rejects_unknown_field() -> None:
    """Reject an otherwise valid document opened with a schema member."""
    document = _valid_document()
    document["schema"] = "workflow-delivery/v3/artifact-reference"

    with pytest.raises(ValueError, match="unknown field") as exc_info:
        artifact_reference_from_document(document)

    assert "schema" in str(exc_info.value)


@pytest.mark.parametrize(
    ("field_name", "replacement", "error_type", "message_fragments"),
    [
        pytest.param(
            "artifact-id",
            "17",
            ValueError,
            ("positive non-Boolean integer",),
            id="artifact-id-string",
        ),
        pytest.param(
            "artifact-digest",
            17,
            ValueError,
            ("artifact-digest", "SHA-256"),
            id="artifact-digest-integer",
        ),
        pytest.param(
            "artifact-url",
            17,
            TypeError,
            ("artifact-url", "nonempty exact string"),
            id="artifact-url-integer",
        ),
        pytest.param(
            "payload-path",
            17,
            TypeError,
            ("payload-path", "nonempty exact string"),
            id="payload-path-integer",
        ),
        pytest.param(
            "payload-digest",
            17,
            ValueError,
            ("payload-digest", "SHA-256"),
            id="payload-digest-integer",
        ),
    ],
)
def test_artifact_reference_from_document_rejects_each_wrong_typed_field(
    field_name: str,
    replacement: JsonValue,
    error_type: type[ValueError | TypeError],
    message_fragments: tuple[str, ...],
) -> None:
    """Reject every wrong-typed field without coercing its value."""
    document = _valid_document()
    document[field_name] = replacement

    with pytest.raises(error_type) as exc_info:
        artifact_reference_from_document(document)

    message = str(exc_info.value)
    for fragment in message_fragments:
        assert fragment in message


@pytest.mark.parametrize(
    ("field_name", "replacement", "error_type", "message_fragments"),
    [
        pytest.param(
            "artifact-digest",
            "sha256:" + ("A" * 64),
            ValueError,
            ("artifact-digest", "SHA-256"),
            id="artifact-digest-uppercase",
        ),
        pytest.param(
            "artifact-url",
            " https://example.test/artifacts/17",
            TypeError,
            ("artifact-url", "nonempty exact string"),
            id="artifact-url-leading-whitespace",
        ),
        pytest.param(
            "payload-path",
            "payload//release.tar.zst",
            ValueError,
            ("payload-path", "normalized relative POSIX path"),
            id="payload-path-duplicate-separator",
        ),
        pytest.param(
            "payload-digest",
            "sha256:" + ("B" * 64),
            ValueError,
            ("payload-digest", "SHA-256"),
            id="payload-digest-uppercase",
        ),
    ],
)
def test_artifact_reference_from_document_rejects_non_normalized_field(
    field_name: str,
    replacement: JsonValue,
    error_type: type[ValueError | TypeError],
    message_fragments: tuple[str, ...],
) -> None:
    """Reject each unambiguously non-normalized document field."""
    document = _valid_document()
    document[field_name] = replacement

    with pytest.raises(error_type) as exc_info:
        artifact_reference_from_document(document)

    message = str(exc_info.value)
    for fragment in message_fragments:
        assert fragment in message
