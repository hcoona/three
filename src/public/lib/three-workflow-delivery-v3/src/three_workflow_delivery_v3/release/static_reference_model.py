"""Canonical records for the bounded static-reference policy."""

from __future__ import annotations

import ntpath
import re
from dataclasses import dataclass
from pathlib import PurePath, PurePosixPath, PureWindowsPath
from typing import TYPE_CHECKING, Literal, cast

from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    parse_canonical_json,
)

if TYPE_CHECKING:
    from collections.abc import Mapping, Sequence

type StaticReferenceSourceKind = Literal["git-target", "index", "worktree"]
type StaticReferenceErrorKind = Literal[
    "source-acquisition-failed",
    "encoding-rejected",
    "authority-rejected",
    "authority-execution-failed",
    "unsupported-projection",
    "authority-mismatch",
    "cleanup-failed",
]
type StaticReferenceFamily = Literal[
    "npm-manifest",
    "pnpm-lock",
    "pnpm-workspace",
    "nuget-lock",
    "nuget-packages-config",
]

STATIC_REFERENCE_POLICY_SCHEMA = (
    "workflow-delivery/v3/bounded-static-reference-policy"
)
STATIC_REFERENCE_RESULT_SCHEMA = (
    "workflow-delivery/v3/bounded-static-reference-result"
)
STATIC_REFERENCE_POLICY_ID = (
    "release/hcoona-release-smoke-npm-bounded-static-reference-v1"
)
PRODUCER_PACKAGE = "@hcoona/hcoona-release-smoke-npm"
PRODUCER_ROOT = "src/public/lib/hcoona-release-smoke-npm"
PRODUCER_MANIFEST = f"{PRODUCER_ROOT}/package.json"
STATIC_REFERENCE_SOURCE_KINDS: tuple[StaticReferenceSourceKind, ...] = (
    "git-target",
    "index",
    "worktree",
)
STATIC_REFERENCE_ERROR_KINDS: tuple[StaticReferenceErrorKind, ...] = (
    "source-acquisition-failed",
    "encoding-rejected",
    "authority-rejected",
    "authority-execution-failed",
    "unsupported-projection",
    "authority-mismatch",
    "cleanup-failed",
)
STATIC_REFERENCE_FAMILIES: tuple[StaticReferenceFamily, ...] = (
    "npm-manifest",
    "pnpm-lock",
    "pnpm-workspace",
    "nuget-lock",
    "nuget-packages-config",
)
STATIC_REFERENCE_PROHIBITED_FORMS = (
    "A",
    "D",
    "L",
    "V",
    "W",
    "dependency-key",
)

_DIGEST_PATTERN = re.compile(r"sha256:[0-9a-f]{64}\Z")
_SHA_PATTERN = re.compile(r"[0-9a-f]{40}\Z")
_MATCHED_IDENTITIES = frozenset({PRODUCER_PACKAGE, PRODUCER_ROOT})


def utf8_sort_key(value: str) -> bytes:
    """Return the exact UTF-8 byte ordering key."""
    return value.encode("utf-8")


def normalized_repository_path(value: object, *, field: str) -> str:
    """Validate one normalized repository-relative POSIX path."""
    if type(value) is not str or not value or "\0" in value:
        message = f"{field} must be a normalized repository-relative path"
        raise ValueError(message)
    candidate = PurePosixPath(value)
    if (
        value == "."
        or candidate.is_absolute()
        or candidate.as_posix() != value
        or any(part in {"", ".", ".."} for part in candidate.parts)
    ):
        message = f"{field} must be a normalized repository-relative path"
        raise ValueError(message)
    return value


def native_repository_path[PathT: PurePath](
    root: PathT,
    value: object,
    *,
    field: str,
) -> PathT:
    """Map a POSIX repository path without host path reinterpretation."""
    normalized = normalized_repository_path(value, field=field)
    logical_parts = PurePosixPath(normalized).parts
    if isinstance(root, PureWindowsPath) and any(
        ntpath.isreserved(part) for part in logical_parts
    ):
        message = f"{field} cannot be represented below its native root"
        raise ValueError(message)
    candidate = root.joinpath(*logical_parts)
    try:
        relative = candidate.relative_to(root)
    except ValueError as error:
        message = f"{field} cannot be represented below its native root"
        raise ValueError(message) from error
    if relative.parts != logical_parts:
        message = f"{field} cannot be represented below its native root"
        raise ValueError(message)
    return candidate


def _exact_string(value: object, *, field: str) -> str:
    if type(value) is not str or not value or value != value.strip():
        message = f"{field} must be a nonempty exact string"
        raise TypeError(message)
    return value


def _object(value: object, *, field: str) -> Mapping[str, object]:
    if not isinstance(value, dict) or any(
        type(key) is not str for key in value
    ):
        message = f"{field} must be an object with exact string keys"
        raise TypeError(message)
    return value


def _array(value: object, *, field: str) -> Sequence[object]:
    if not isinstance(value, list):
        message = f"{field} must be an array"
        raise TypeError(message)
    return value


def _validate_source_binding(
    source_kind: StaticReferenceSourceKind,
    target: str | None,
) -> None:
    if source_kind not in STATIC_REFERENCE_SOURCE_KINDS:
        message = "static-reference source kind is invalid"
        raise ValueError(message)
    if source_kind == "git-target":
        if type(target) is not str or _SHA_PATTERN.fullmatch(target) is None:
            message = "git-target Result requires a full lowercase target"
            raise ValueError(message)
    elif target is not None:
        message = "index/worktree Result must not bind a target"
        raise ValueError(message)


def _validate_implementation_identities(
    identities: tuple[str, ...],
) -> None:
    if type(identities) is not tuple or any(
        type(identity) is not str or not identity for identity in identities
    ):
        message = "implementation identities must be exact strings"
        raise TypeError(message)
    expected = tuple(sorted(set(identities), key=utf8_sort_key))
    if identities != expected:
        message = "implementation identities must be sorted and unique"
        raise ValueError(message)


def _validate_findings(
    findings: tuple[StaticReferenceFinding, ...],
) -> None:
    if type(findings) is not tuple or any(
        type(finding) is not StaticReferenceFinding for finding in findings
    ):
        message = "findings must contain exact StaticReferenceFinding values"
        raise TypeError(message)
    expected = tuple(sorted(set(findings), key=StaticReferenceFinding.sort_key))
    if findings != expected:
        message = "findings must be sorted and unique"
        raise ValueError(message)


@dataclass(frozen=True, slots=True)
class StaticReferenceFinding:
    """One prohibited fact projected from an admitted authority graph."""

    path: str
    family: StaticReferenceFamily
    context: str
    prohibited_form: str
    matched_identity: str
    location: str | None = None

    def __post_init__(self) -> None:
        """Reject noncanonical or unbounded finding fields."""
        normalized_repository_path(self.path, field="finding.path")
        if self.family not in STATIC_REFERENCE_FAMILIES:
            message = "finding.family is not a retained static-reference family"
            raise ValueError(message)
        _exact_string(self.context, field="finding.context")
        if self.prohibited_form not in STATIC_REFERENCE_PROHIBITED_FORMS:
            message = "finding.prohibited_form is not canonical"
            raise ValueError(message)
        if self.matched_identity not in _MATCHED_IDENTITIES:
            message = (
                "finding.matched_identity is not a sanitized policy identity"
            )
            raise ValueError(message)
        if self.location is not None:
            _exact_string(self.location, field="finding.location")

    def sort_key(self) -> tuple[bytes, bytes, bytes, bytes, bytes, bytes]:
        """Return the canonical UTF-8 finding order."""
        return (
            utf8_sort_key(self.path),
            utf8_sort_key(self.family),
            utf8_sort_key(self.context),
            utf8_sort_key(self.prohibited_form),
            utf8_sort_key(self.matched_identity),
            utf8_sort_key(self.location or ""),
        )

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical finding representation."""
        document: dict[str, JsonValue] = {
            "path": self.path,
            "family": self.family,
            "semantic-context": self.context,
            "prohibited-form": self.prohibited_form,
            "matched-identity": self.matched_identity,
        }
        if self.location is not None:
            document["location"] = self.location
        return document


@dataclass(frozen=True, slots=True)
class BoundedStaticReferenceResult:
    """One source-bound canonical bounded static-reference Result."""

    source_kind: StaticReferenceSourceKind
    target: str | None
    policy_id: str
    policy_digest: str
    implementation_identities: tuple[str, ...]
    findings: tuple[StaticReferenceFinding, ...]
    error_kind: StaticReferenceErrorKind | None = None

    def __post_init__(self) -> None:
        """Reject an invalid or noncanonical Result."""
        _validate_source_binding(self.source_kind, self.target)
        if self.policy_id != STATIC_REFERENCE_POLICY_ID:
            message = "static-reference policy ID is not current"
            raise ValueError(message)
        if _DIGEST_PATTERN.fullmatch(self.policy_digest) is None:
            message = "static-reference policy digest must be SHA-256"
            raise ValueError(message)
        _validate_implementation_identities(self.implementation_identities)
        _validate_findings(self.findings)
        if self.error_kind is not None:
            if self.error_kind not in STATIC_REFERENCE_ERROR_KINDS:
                message = "static-reference error kind is invalid"
                raise ValueError(message)
            if self.findings:
                message = "error Result must not retain partial findings"
                raise ValueError(message)

    @property
    def result(self) -> Literal["clean", "findings", "error"]:
        """Return the sole canonical Result discriminator."""
        if self.error_kind is not None:
            return "error"
        if self.findings:
            return "findings"
        return "clean"

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical Result representation."""
        identities: list[JsonValue] = list(self.implementation_identities)
        findings: list[JsonValue] = [
            finding.to_document() for finding in self.findings
        ]
        document: dict[str, JsonValue] = {
            "schema": STATIC_REFERENCE_RESULT_SCHEMA,
            "result": self.result,
            "source-kind": self.source_kind,
            "policy-id": self.policy_id,
            "policy-digest": self.policy_digest,
            "implementation-identities": identities,
            "findings": findings,
        }
        if self.target is not None:
            document["target"] = self.target
        if self.error_kind is not None:
            document["error-kind"] = self.error_kind
        return document

    @property
    def result_digest(self) -> str:
        """Return the canonical Result digest."""
        return canonical_sha256(self.to_document())


def _parse_implementation_identities(
    parsed: Mapping[str, object],
) -> tuple[str, ...]:
    values = _array(
        parsed["implementation-identities"],
        field="implementation-identities",
    )
    return tuple(
        _exact_string(value, field="implementation identity")
        for value in values
    )


def _parse_finding(value: object) -> StaticReferenceFinding:
    item = _object(value, field="finding")
    expected_fields = {
        "path",
        "family",
        "semantic-context",
        "prohibited-form",
        "matched-identity",
    }
    if "location" in item:
        expected_fields.add("location")
    if set(item) != expected_fields:
        message = "static-reference finding fields are not exact"
        raise ValueError(message)
    family_value = _exact_string(item["family"], field="finding.family")
    if family_value not in STATIC_REFERENCE_FAMILIES:
        message = "finding.family is not retained"
        raise ValueError(message)
    return StaticReferenceFinding(
        path=normalized_repository_path(item["path"], field="finding.path"),
        family=cast("StaticReferenceFamily", family_value),
        context=_exact_string(
            item["semantic-context"],
            field="finding.semantic-context",
        ),
        prohibited_form=_exact_string(
            item["prohibited-form"],
            field="finding.prohibited-form",
        ),
        matched_identity=_exact_string(
            item["matched-identity"],
            field="finding.matched-identity",
        ),
        location=(
            _exact_string(item["location"], field="finding.location")
            if "location" in item
            else None
        ),
    )


def _parse_findings(
    parsed: Mapping[str, object],
) -> tuple[StaticReferenceFinding, ...]:
    values = _array(parsed["findings"], field="findings")
    return tuple(_parse_finding(value) for value in values)


def _parse_error_kind(
    parsed: Mapping[str, object],
) -> StaticReferenceErrorKind | None:
    if "error-kind" not in parsed:
        return None
    value = _exact_string(parsed["error-kind"], field="error-kind")
    if value not in STATIC_REFERENCE_ERROR_KINDS:
        message = "static-reference error kind is invalid"
        raise ValueError(message)
    return cast("StaticReferenceErrorKind", value)


def _expected_result_fields(
    source_kind: StaticReferenceSourceKind,
    result: str,
) -> set[str]:
    fields = {
        "schema",
        "result",
        "source-kind",
        "policy-id",
        "policy-digest",
        "implementation-identities",
        "findings",
    }
    if source_kind == "git-target":
        fields.add("target")
    if result == "error":
        fields.add("error-kind")
    return fields


def parse_bounded_static_reference_result(
    document: bytes | bytearray,
) -> BoundedStaticReferenceResult:
    """Strictly parse and validate one canonical Result."""
    parsed = parse_canonical_json(document)
    source_kind_value = _exact_string(
        parsed.get("source-kind"),
        field="source-kind",
    )
    if source_kind_value not in STATIC_REFERENCE_SOURCE_KINDS:
        message = "static-reference source kind is invalid"
        raise ValueError(message)
    source_kind = cast("StaticReferenceSourceKind", source_kind_value)
    result_value = _exact_string(parsed.get("result"), field="result")
    if result_value not in {"clean", "findings", "error"}:
        message = "static-reference result discriminator is invalid"
        raise ValueError(message)

    expected_fields = _expected_result_fields(source_kind, result_value)
    if set(parsed) != expected_fields:
        message = "static-reference Result fields are not exact"
        raise ValueError(message)
    if parsed["schema"] != STATIC_REFERENCE_RESULT_SCHEMA:
        message = "static-reference Result schema is invalid"
        raise ValueError(message)

    result = BoundedStaticReferenceResult(
        source_kind=source_kind,
        target=(
            _exact_string(parsed["target"], field="target")
            if "target" in parsed
            else None
        ),
        policy_id=_exact_string(parsed["policy-id"], field="policy-id"),
        policy_digest=_exact_string(
            parsed["policy-digest"],
            field="policy-digest",
        ),
        implementation_identities=_parse_implementation_identities(parsed),
        findings=_parse_findings(parsed),
        error_kind=_parse_error_kind(parsed),
    )
    if result.result != result_value:
        message = "static-reference Result discriminator is inconsistent"
        raise ValueError(message)
    if result.to_document() != parsed:
        message = "static-reference Result did not round-trip exactly"
        raise ValueError(message)
    return result


__all__ = [
    "PRODUCER_MANIFEST",
    "PRODUCER_PACKAGE",
    "PRODUCER_ROOT",
    "STATIC_REFERENCE_ERROR_KINDS",
    "STATIC_REFERENCE_FAMILIES",
    "STATIC_REFERENCE_POLICY_ID",
    "STATIC_REFERENCE_POLICY_SCHEMA",
    "STATIC_REFERENCE_PROHIBITED_FORMS",
    "STATIC_REFERENCE_RESULT_SCHEMA",
    "STATIC_REFERENCE_SOURCE_KINDS",
    "BoundedStaticReferenceResult",
    "StaticReferenceErrorKind",
    "StaticReferenceFamily",
    "StaticReferenceFinding",
    "StaticReferenceSourceKind",
    "native_repository_path",
    "normalized_repository_path",
    "parse_bounded_static_reference_result",
    "utf8_sort_key",
]
