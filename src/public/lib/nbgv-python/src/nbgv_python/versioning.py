"""Utilities for normalizing NBGV versions to PEP 440."""

from __future__ import annotations

import re

from packaging.version import InvalidVersion, Version

_LABEL_MAP = {
    "a": "a",
    "alpha": "a",
    "b": "b",
    "beta": "b",
    "rc": "rc",
    "pre": "rc",
    "preview": "rc",
    "prerelease": "rc",
    "dev": ".dev",
}

_SEMVER_PATTERN = re.compile(
    r"^(?P<core>\d+(?:\.\d+)*)(?:-(?P<pre>[^+]+))?(?:\+(?P<local>.+))?$"
)

_SPLIT_PATTERN = re.compile(r"[.\-]")
_NON_ALNUM_PATTERN = re.compile(r"[^0-9a-zA-Z]")


def normalize_version_field(value: object, *, field: str) -> str:
    """Return a PEP 440 compliant version string for *value*."""
    candidate = str(value)
    try:
        return str(Version(candidate))
    except InvalidVersion:
        converted = _convert_semver_to_pep440(candidate)
        if converted is not None:
            try:
                return str(Version(converted))
            except InvalidVersion as exc:  # pragma: no cover - defensive branch
                msg = (
                    f"Field '{field}' produced '{candidate}' which could not "
                    "be normalized to a PEP 440 version."
                )
                raise RuntimeError(msg) from exc
    msg = (
        f"Field '{field}' produced '{candidate}' which is not a valid "
        "PEP 440 version."
    )
    raise RuntimeError(msg)


def _convert_semver_to_pep440(candidate: str) -> str | None:
    match = _SEMVER_PATTERN.match(candidate)
    if not match:
        return None
    core = match.group("core")
    prerelease = match.group("pre")
    local = match.group("local")
    result = core
    local_parts: list[str] = []
    if local:
        local_parts.extend(_normalize_local_parts(local))
    if prerelease:
        pre_result = _convert_prerelease(prerelease)
        if pre_result is None:
            local_parts[:0] = _normalize_local_parts(prerelease)
        else:
            suffix, extras = pre_result
            result += suffix
            if extras:
                local_parts.extend(_normalize_local_parts(".".join(extras)))
    if local_parts:
        result = f"{result}+{'.'.join(local_parts)}"
    return result


def _convert_prerelease(text: str) -> tuple[str, list[str]] | None:
    tokens = [token for token in _SPLIT_PATTERN.split(text) if token]
    if not tokens:
        return None
    label_token = tokens[0]
    match = re.match(r"(?P<label>[A-Za-z]+)(?P<number>\d*)$", label_token)
    if match:
        label = match.group("label").lower()
        number = match.group("number")
        remainder_tokens: list[str] = []
    else:
        label = "".join(ch for ch in label_token if ch.isalpha()).lower()
        number = "".join(ch for ch in label_token if ch.isdigit())
        remainder_tokens = [
            chunk
            for chunk in _SPLIT_PATTERN.split(
                label_token[len(label) + len(number) :]
            )
            if chunk
        ]
    remainder_tokens.extend(tokens[1:])
    if label not in _LABEL_MAP:
        return None
    if not number:
        number_index = _first_numeric_index(remainder_tokens)
        if number_index is not None:
            number = str(int(remainder_tokens.pop(number_index)))
    if not number:
        number = "0"
    mapped = _LABEL_MAP[label]
    suffix = f"{mapped}{number}"
    return suffix, remainder_tokens


def _first_numeric_index(parts: list[str]) -> int | None:
    for index, part in enumerate(parts):
        if part.isdigit():
            return index
    return None


def _normalize_local_parts(text: str) -> list[str]:
    parts = [segment for segment in _SPLIT_PATTERN.split(text) if segment]
    normalized: list[str] = []
    for part in parts:
        clean = _NON_ALNUM_PATTERN.sub("-", part)
        clean = re.sub(r"-+", "-", clean).strip("-")
        if clean:
            normalized.append(clean.lower())
    return normalized


__all__ = ["normalize_version_field"]
