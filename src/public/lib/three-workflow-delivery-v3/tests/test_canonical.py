"""Tests for strict JSON and RFC 8785 canonical primitives."""

from __future__ import annotations

import json

import pytest
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
    parse_json_strict,
)


@pytest.mark.parametrize(
    ("document", "expected"),
    [
        (
            (
                '{"numbers":[333333333.33333329,1E30,4.50,2e-3,1e-27],'
                '"literals":[null,true,false]}'
            ),
            (
                b'{"literals":[null,true,false],"numbers":'
                b"[333333333.3333333,1e+30,4.5,0.002,1e-27]}"
            ),
        ),
        (
            (
                '{"\\u20ac":"Euro Sign","\\r":"Carriage Return",'
                '"\\ufb33":"Hebrew Letter Dalet With Dagesh","1":"One"}'
            ),
            (
                '{"\\r":"Carriage Return","1":"One","€":"Euro Sign",'
                '"דּ":"Hebrew Letter Dalet With Dagesh"}'
            ).encode(),
        ),
    ],
)
def test_canonicalize_matches_rfc8785_golden_vector(
    document: str,
    expected: bytes,
) -> None:
    """Match RFC 8785 number serialization and UTF-16 member ordering."""
    value = parse_json_strict(document)

    assert canonicalize(value) == expected
    assert canonicalize(value).decode("utf-8") == expected.decode("utf-8")


def test_canonicalize_returns_utf8_json_bytes() -> None:
    """Return exact UTF-8 JSON bytes without ASCII escaping."""
    result = canonicalize({"currency": "€", "message": "München"})

    assert result == '{"currency":"€","message":"München"}'.encode()
    assert isinstance(result, bytes)


def test_canonical_sha256_matches_golden_digest() -> None:
    """Hash the canonical representation rather than insertion order."""
    first: dict[str, JsonValue] = {"z": [3, 2, 1], "a": "value"}
    second: dict[str, JsonValue] = {"a": "value", "z": [3, 2, 1]}

    assert canonical_sha256(first) == (
        "sha256:666291de8b61a11d3a139f9df95e7476"
        "a3accf1ac96d401abf8fb35b43e294ba"
    )
    assert canonical_sha256(second) == canonical_sha256(first)


@pytest.mark.parametrize(
    "document",
    ["", '{"missing":}', "[1,]"],
)
def test_parse_json_strict_rejects_malformed_json(
    document: str,
) -> None:
    """Reject invalid syntax through the real JSON parser."""
    with pytest.raises(json.JSONDecodeError):
        parse_json_strict(document)


@pytest.mark.parametrize("constant", ["NaN", "Infinity"])
def test_parse_json_strict_rejects_non_json_constants(constant: str) -> None:
    """Reject non-JSON constants admitted by the underlying parser."""
    with pytest.raises(ValueError, match=f"invalid JSON constant: {constant}"):
        parse_json_strict(constant)


@pytest.mark.parametrize(
    ("document", "duplicate_name"),
    [
        ('{"id":1,"id":2}', "id"),
        ('{"outer":{"value":1,"value":2}}', "value"),
        ('[{"nested":{"key":1,"key":2}}]', "key"),
    ],
)
def test_parse_json_strict_rejects_duplicate_members(
    document: str,
    duplicate_name: str,
) -> None:
    """Reject duplicate object members at every nesting depth."""
    with pytest.raises(
        ValueError,
        match=rf"duplicate JSON object member: '{duplicate_name}'",
    ):
        parse_json_strict(document)


def test_parse_json_strict_accepts_strict_utf8_bytes() -> None:
    """Parse non-ASCII JSON only when its bytes are strict UTF-8."""
    document = '{"currency":"€","city":"München"}'.encode()

    assert parse_json_strict(document) == {
        "currency": "€",
        "city": "München",
    }


@pytest.mark.parametrize(
    "document",
    [
        '{"value":"€"}'.encode("utf-16"),
        '{"value":"€"}'.encode("utf-16-le"),
        '{"value":"€"}'.encode("utf-16-be"),
        '{"value":"€"}'.encode("utf-32"),
        '{"value":"€"}'.encode("utf-32-le"),
        '{"value":"€"}'.encode("utf-32-be"),
        b'{"value":"\xff"}',
    ],
    ids=[
        "utf-16-bom",
        "utf-16-little-endian",
        "utf-16-big-endian",
        "utf-32-bom",
        "utf-32-little-endian",
        "utf-32-big-endian",
        "invalid-utf-8",
    ],
)
def test_parse_json_strict_rejects_non_utf8_bytes(document: bytes) -> None:
    """Reject byte input encoded as UTF-16, UTF-32, or invalid UTF-8."""
    with pytest.raises(UnicodeDecodeError, match="utf-8"):
        parse_json_strict(document)


def test_parse_then_canonicalize_preserves_jcs_semantics() -> None:
    """Strictly parse before sorting members and normalizing numbers."""
    document = '{"z":-0.0,"a":{"é":"text","a":true}}'

    parsed = parse_json_strict(document)

    assert canonicalize(parsed) == (
        '{"a":{"a":true,"é":"text"},"z":0}'.encode()
    )
    assert parsed == {"z": -0.0, "a": {"é": "text", "a": True}}


from pathlib import Path  # noqa: E402

from three_workflow_delivery_v3.canonical import (  # noqa: E402
    parse_canonical_json,
)

_BINDING_FIXTURE_DIRECTORY = Path(__file__).parent / "fixtures" / "bindings"
_FIXTURE_GOLDEN_DIGESTS = {
    "current-authority": (
        "sha256:bfadf748e203e4d005d6fe29ea3b06ddccad3b22aea13b136"
        "4af0b65dc6d3d16"
    ),
}


def test_parse_canonical_json_accepts_canonical_utf8_object() -> None:
    """Return the exact object encoded by canonical bytes."""
    document = b'{"active":true,"items":[1,"two"],"name":"record"}'

    result = parse_canonical_json(document)

    assert result == {
        "active": True,
        "items": [1, "two"],
        "name": "record",
    }


@pytest.mark.parametrize(
    "document",
    [b"null", b"true", b"42", b'"record"', b"[1,2]"],
    ids=["null", "boolean", "number", "string", "array"],
)
def test_parse_canonical_json_rejects_non_object_json(document: bytes) -> None:
    """Reject every canonical JSON kind that is not a transported object."""
    with pytest.raises(TypeError, match="must be an object"):
        parse_canonical_json(document)


def test_parse_canonical_json_rejects_malformed_json() -> None:
    """Preserve strict parser failure for malformed transported bytes."""
    with pytest.raises(json.JSONDecodeError):
        parse_canonical_json(b'{"missing":}')


@pytest.mark.parametrize(
    ("document", "duplicate_name"),
    [
        (b'{"id":1,"id":2}', "id"),
        (b'{"outer":{"value":1,"value":2}}', "value"),
    ],
    ids=["top-level", "nested"],
)
def test_parse_canonical_json_rejects_duplicate_members(
    document: bytes,
    duplicate_name: str,
) -> None:
    """Reject duplicate members before checking canonical record bytes."""
    with pytest.raises(
        ValueError,
        match=rf"duplicate JSON object member: '{duplicate_name}'",
    ):
        parse_canonical_json(document)


def test_parse_canonical_json_rejects_non_utf8_bytes() -> None:
    """Preserve strict UTF-8 decoding at the transported record boundary."""
    with pytest.raises(UnicodeDecodeError, match="utf-8"):
        parse_canonical_json(b'{"value":"\xff"}')


@pytest.mark.parametrize(
    "document",
    [
        b'{ "a":1}',
        b'{"z":1,"a":2}',
        b'{"value":1.0}',
        b'{"value":"\\u0061"}',
    ],
    ids=["whitespace", "key-order", "number-encoding", "string-encoding"],
)
def test_parse_canonical_json_rejects_noncanonical_bytes(
    document: bytes,
) -> None:
    """Reject semantically valid bytes that differ from RFC 8785 output."""
    with pytest.raises(ValueError, match="record is not canonical"):
        parse_canonical_json(document)


def test_parse_canonical_json_checks_encoding_before_digest_use() -> None:
    """Reject noncanonical bytes even when they carry a digest-shaped claim."""
    document = (
        b'{ "payload_digest":'
        b'"sha256:6668e506afbfa6628a50dfca85ec6c8e6c8b07aa6e4c9640'
        b'592ea6844683ffa7"}'
    )

    with pytest.raises(ValueError, match="record is not canonical"):
        parse_canonical_json(document)


@pytest.mark.parametrize("fixture_name", sorted(_FIXTURE_GOLDEN_DIGESTS))
def test_binding_fixtures_have_stable_golden_payload_digests(
    fixture_name: str,
) -> None:
    """Pin fixture payload identities to literal reviewed sidecar values."""
    document = (
        _BINDING_FIXTURE_DIRECTORY / f"{fixture_name}.json"
    ).read_bytes()
    sidecar = (
        (_BINDING_FIXTURE_DIRECTORY / f"{fixture_name}.sha256")
        .read_text(encoding="ascii")
        .strip()
    )

    assert sidecar == _FIXTURE_GOLDEN_DIGESTS[fixture_name]
    assert canonical_sha256(parse_canonical_json(document)) == sidecar
