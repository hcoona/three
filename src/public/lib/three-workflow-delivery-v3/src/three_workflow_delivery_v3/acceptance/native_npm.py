"""Pure WD-OPS-002A / LLD 18.6 native npm state comparisons.

Collectors supply complete active inventories and tags, observed (not
expected) controls, remote SHA-256/SHA-512 and canonical witnesses for active
scenario versions. Expected fixtures remain separate comparator operands.
No IO or content hashing occurs here. Witness/target binding, npm parsing,
completeness, freshness and pre-existing public hcoona admission belong there.

Counters are deliberately RECOMPUTED from inventories. Collectors retain full
responses, raw counters and volatile timestamps/request IDs/URLs outside this
canonical shape. Dangling tags remain; contents require active membership.
Raw evidence and native provenance remain acceptance-only, never Release or
Governance records. Administrative history is outside this active projection.

Comparisons return None only for a shape gate, or reject with ValueError.
Callers separately enforce authoritative NpmProcessOutcome, probe sequencing,
authorization and the pinned profile. No gate certifies native acceptance.
"""

from __future__ import annotations

import re
from dataclasses import dataclass, replace
from typing import TYPE_CHECKING

from three_workflow_delivery_v3.canonical import (
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)

if TYPE_CHECKING:
    from three_workflow_delivery_v3.canonical import JsonValue

ACCEPTANCE_STATE_SCHEMA = "workflow-delivery-v3/native-npm-state/v2"
_MAX_JSON_INTEGER = 2**53 - 1
_TAG_PAIR_SIZE = 2


def _require(condition: bool, message: str) -> None:  # noqa: FBT001
    if not condition:
        raise ValueError(message)


def _name(value: object) -> bool:
    if type(value) is not str or not value or value != value.strip():
        return False
    try:
        value.encode("utf-8")
    except UnicodeError:
        return False
    return True


def _identity(value: object) -> bool:
    return type(value) is int and 0 < value <= _MAX_JSON_INTEGER


def _names(values: tuple[str, ...]) -> bool:
    return (
        type(values) is tuple
        and all(_name(value) for value in values)
        and values == tuple(sorted(set(values)))
    )


@dataclass(frozen=True)
class PackageControl:
    """Stable controls; empty access means not exposed, not no grants."""

    container_id: int
    full_scoped_name: str
    owner: str
    visibility: str
    repository_full_name: str
    exposed_access: tuple[str, ...]

    def __post_init__(self) -> None:
        """Reject missing identities and noncanonical exposed facts."""
        _require(_identity(self.container_id), "invalid package container ID")
        _require(
            _name(self.full_scoped_name)
            and _name(self.owner)
            and _name(self.repository_full_name),
            "package control requires observed names",
        )
        _require(
            type(self.visibility) is str
            and self.visibility in ("public", "private", "internal"),
            "invalid observed visibility",
        )
        _require(
            _names(self.exposed_access), "access facts must be sorted unique"
        )

    def to_document(self) -> dict[str, JsonValue]:
        """Return only stable observed package-control facts."""
        access: list[JsonValue] = list(self.exposed_access)
        return {
            "container_id": self.container_id,
            "full_scoped_name": self.full_scoped_name,
            "owner": self.owner,
            "visibility": self.visibility,
            "repository_full_name": self.repository_full_name,
            "exposed_access": access,
        }


@dataclass(frozen=True)
class ObservedContent:
    """Actual content facts, or an explicitly separate expected fixture operand.

    Witness bytes are immutable canonical JSON; the collector binds the target
    to that remote witness. No fixture-specific protocol is imposed here.
    """

    version: str
    sha256: str
    sha512: str
    witness: bytes
    target: str

    def __post_init__(self) -> None:
        """Require canonical actual digests, witness bytes, and identities."""
        _require(
            _name(self.version) and _name(self.target),
            "missing content identity",
        )
        for value, pattern in (
            (self.sha256, r"sha256:[0-9a-f]{64}"),
            (self.sha512, r"sha512:[0-9a-f]{128}"),
        ):
            _require(
                type(value) is str and re.fullmatch(pattern, value) is not None,
                "content digest must use canonical prefixed lowercase hex",
            )
        _require(type(self.witness) is bytes, "witness must be immutable bytes")
        try:
            parse_canonical_json(self.witness)
        except (TypeError, ValueError) as error:
            message = "witness must be a canonical JSON object"
            raise ValueError(message) from error

    def to_document(self) -> dict[str, JsonValue]:
        """Return content facts without hashing or substituting local bytes."""
        return {
            "version": self.version,
            "sha256": self.sha256,
            "sha512": self.sha512,
            "witness": parse_canonical_json(self.witness),
            "target": self.target,
        }


@dataclass(frozen=True)
class VersionIdentity:
    """Stable service version ID and name for active-inventory provenance."""

    version_id: int
    name: str

    def __post_init__(self) -> None:
        """Reject missing or non-finite service identities."""
        _require(
            _identity(self.version_id) and _name(self.name),
            "invalid version identity",
        )

    def to_document(self) -> dict[str, JsonValue]:
        """Return the stable service identity."""
        return {"version_id": self.version_id, "name": self.name}


@dataclass(frozen=True)
class AcceptanceState:
    """Closed observed shape over the complete active projection."""

    control: PackageControl
    active_versions: tuple[str, ...]
    tags: tuple[tuple[str, str], ...]
    contents: tuple[ObservedContent, ...]

    def __post_init__(self) -> None:
        """Reject duplicate inventories, mutable shapes, and unbound content."""
        _require(
            type(self.control) is PackageControl,
            "missing observed package control",
        )
        _require(
            _names(self.active_versions), "active names must be sorted unique"
        )
        _require(
            type(self.tags) is tuple
            and all(
                type(pair) is tuple
                and len(pair) == _TAG_PAIR_SIZE
                and all(map(_name, pair))
                for pair in self.tags
            )
            and _names(tuple(tag for tag, _ in self.tags)),
            "tags require sorted unique names and string targets",
        )
        _require(
            type(self.contents) is tuple
            and all(type(item) is ObservedContent for item in self.contents)
            and _names(tuple(item.version for item in self.contents))
            and all(
                item.version in self.active_versions for item in self.contents
            ),
            "scenario contents must be sorted unique and observed active",
        )

    def to_document(self) -> dict[str, JsonValue]:
        """Return versioned closed state with explicitly derived counts."""
        active: list[JsonValue] = list(self.active_versions)
        tags: dict[str, JsonValue] = dict(self.tags)
        return {
            "schema": ACCEPTANCE_STATE_SCHEMA,
            "control": self.control.to_document(),
            "active_versions": active,
            "active_version_count": len(self.active_versions),
            "tags": tags,
            "contents": [item.to_document() for item in self.contents],
        }

    def digest(self) -> str:
        """Digest the complete canonical comparison shape, not raw evidence."""
        return canonical_sha256(self.to_document())


def empty_delta(before: AcceptanceState, after: AcceptanceState) -> None:
    """Require the entire canonical shape to match, or raise ValueError."""
    _require(
        type(before) is AcceptanceState and type(after) is AcceptanceState,
        "comparison requires typed acceptance states",
    )
    expected, actual = before.to_document(), after.to_document()
    changed = [
        key
        for key in expected
        if canonicalize(expected[key]) != canonicalize(actual[key])
    ]
    _require(not changed, f"acceptance delta changed: {', '.join(changed)}")


def _scenario_tag(tag: str) -> None:
    _require(_name(tag) and tag != "latest", "scenario tag must not be latest")


def _with_added_content(
    before: AcceptanceState, desired: ObservedContent
) -> AcceptanceState:
    _require(
        type(desired) is ObservedContent, "missing expected scenario content"
    )
    _require(
        desired.version not in before.active_versions,
        "candidate already active",
    )
    return replace(
        before,
        active_versions=tuple(
            sorted((*before.active_versions, desired.version))
        ),
        contents=tuple(
            sorted((*before.contents, desired), key=lambda item: item.version)
        ),
    )


def require_creation_delta(
    before: AcceptanceState,
    after: AcceptanceState,
    desired: ObservedContent,
    tag: str,
) -> None:
    """Require only exact new content and its previously absent declared tag."""
    _scenario_tag(tag)
    _require(tag not in dict(before.tags), "creation tag already present")
    expected = _with_added_content(before, desired)
    expected = replace(
        expected, tags=tuple(sorted((*before.tags, (tag, desired.version))))
    )
    empty_delta(expected, after)


def require_active_duplicate_delta(
    before: AcceptanceState,
    after: AcceptanceState,
    expected_existing: ObservedContent,
) -> None:
    """Require the original scenario content and an entirely empty delta."""
    _require(
        type(expected_existing) is ObservedContent
        and expected_existing in before.contents
        and expected_existing in after.contents,
        "active duplicate requires exact original content in both states",
    )
    empty_delta(before, after)


def require_tag_race_delta(
    before: AcceptanceState,
    after: AcceptanceState,
    desired: ObservedContent,
    known_w: ObservedContent,
    tag: str,
) -> None:
    """Allow unchanged state or exact V with only the declared tag at V or W.

    A no-mutation failure may leave V absent and W intact. Passing this shape
    gate never upgrades a failed command or permits another publish to add V.
    """
    _scenario_tag(tag)
    _require(
        type(known_w) is ObservedContent
        and known_w in before.contents
        and dict(before.tags).get(tag) == known_w.version,
        "race baseline requires exact W with the declared tag at W",
    )
    expected = _with_added_content(before, desired)
    if desired.version not in after.active_versions:
        empty_delta(before, after)
        return
    actual_target = dict(after.tags).get(tag)
    _require(
        actual_target in (desired.version, known_w.version),
        "race tag must resolve to V or W",
    )
    tags = dict(before.tags)
    tags[tag] = dict(after.tags)[tag]
    empty_delta(replace(expected, tags=tuple(sorted(tags.items()))), after)
