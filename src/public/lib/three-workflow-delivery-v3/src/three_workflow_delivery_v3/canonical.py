"""Strict JSON parsing and RFC 8785 canonicalization."""

from __future__ import annotations

import hashlib
import json
from typing import TYPE_CHECKING, NoReturn

import rfc8785

if TYPE_CHECKING:
    from collections.abc import Iterable

type JsonPrimitive = None | bool | int | float | str
type JsonValue = JsonPrimitive | list[JsonValue] | dict[str, JsonValue]


def _reject_duplicate_members(
    members: Iterable[tuple[str, JsonValue]],
) -> dict[str, JsonValue]:
    result: dict[str, JsonValue] = {}
    for name, value in members:
        if name in result:
            message = f"duplicate JSON object member: {name!r}"
            raise ValueError(message)
        result[name] = value
    return result


def _reject_non_json_constant(constant: str) -> NoReturn:
    message = f"invalid JSON constant: {constant}"
    raise ValueError(message)


def parse_json_strict(document: str | bytes | bytearray) -> JsonValue:
    """Parse strict JSON, rejecting malformed input and duplicate members."""
    if not isinstance(document, str):
        document = document.decode("utf-8")

    parsed: JsonValue = json.loads(
        document,
        object_pairs_hook=_reject_duplicate_members,
        parse_constant=_reject_non_json_constant,
    )
    return parsed


def parse_canonical_json(
    document: bytes | bytearray,
) -> dict[str, JsonValue]:
    """Parse a transported canonical JSON object without coercion."""
    parsed = parse_json_strict(document)
    if not isinstance(parsed, dict):
        message = "canonical JSON record must be an object"
        raise TypeError(message)
    if canonicalize(parsed) != bytes(document):
        message = "JSON record is not canonical"
        raise ValueError(message)
    return parsed


def canonicalize(value: JsonValue) -> bytes:
    """Serialize a JSON value to RFC 8785 canonical UTF-8 bytes."""
    return rfc8785.dumps(value)


def canonical_sha256(value: JsonValue) -> str:
    """Return the prefixed SHA-256 digest of a canonical JSON value."""
    digest = hashlib.sha256(canonicalize(value)).hexdigest()
    return f"sha256:{digest}"
