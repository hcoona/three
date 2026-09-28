"""Tests for strict JSON and RFC 8785 canonical primitives."""

from __future__ import annotations

import json

import pytest
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
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


def test_parse_json_strict_rejects_malformed_json() -> None:
    """Propagate invalid syntax from the real JSON parser."""
    with pytest.raises(json.JSONDecodeError):
        parse_json_strict('{"missing":}')


def test_parse_json_strict_rejects_non_json_constants() -> None:
    """Install the callback that rejects non-JSON numeric constants."""
    with pytest.raises(ValueError, match="invalid JSON constant: NaN"):
        parse_json_strict("NaN")


def test_parse_json_strict_rejects_duplicate_members() -> None:
    """Reject a duplicate object member nested within an array."""
    with pytest.raises(ValueError, match="duplicate JSON object member: 'key'"):
        parse_json_strict('[{"nested":{"key":1,"key":2}}]')


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
        '{"value":"€"}'.encode("utf-16-le"),
        b'{"value":"\xff"}',
    ],
    ids=["utf-16-little-endian", "invalid-utf-8"],
)
def test_parse_json_strict_rejects_non_utf8_bytes(document: bytes) -> None:
    """Reject alternative encoding and invalid bytes at the UTF-8 decoder."""
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


def test_parse_canonical_json_accepts_canonical_utf8_object() -> None:
    """Return the exact object encoded by canonical bytes."""
    document = b'{"active":true,"items":[1,"two"],"name":"record"}'

    result = parse_canonical_json(document)

    assert result == {
        "active": True,
        "items": [1, "two"],
        "name": "record",
    }


def test_parse_canonical_json_rejects_non_object_json() -> None:
    """Require an object even when the supplied array is canonical JSON."""
    with pytest.raises(TypeError, match="must be an object"):
        parse_canonical_json(b"[1,2]")


def test_parse_canonical_json_rejects_duplicate_members() -> None:
    """Reject duplicates before the wrapper compares canonical bytes."""
    with pytest.raises(
        ValueError, match="duplicate JSON object member: 'value'"
    ):
        parse_canonical_json(b'{"outer":{"value":1,"value":2}}')


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
