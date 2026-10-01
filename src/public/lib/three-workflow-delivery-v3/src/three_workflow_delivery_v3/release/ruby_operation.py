"""Closed Ruby operation envelopes and separately admitted exact requests.

These records do not dispatch, configure a service, acquire credentials or
authorize an upload. The reviewed sole operator owns independent admission and
durable slot reservation; hashes establish byte identity, not reviewer trust.
"""

from __future__ import annotations

import re
from dataclasses import dataclass
from datetime import datetime, timedelta
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3._ruby_native import (
    RUBY_RELEASE_UNIT,
    ruby_digest,
    ruby_object,
    ruby_text,
)
from three_workflow_delivery_v3.adapters.rubygems import RubyRegistry
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.repository.ruby_controls import (
    RUBY_CONFIGURATION_PATHS,
    RUBY_ENVELOPE_PATH,
    RUBY_GOVERNANCE_PATHS,
)
from three_workflow_delivery_v3.repository.ruby_provider import (
    ruby_nbgv_facts_from_document,
)

if TYPE_CHECKING:
    from collections.abc import Mapping

RUBY_OPERATION_SLOTS = tuple(
    f"{destination}-{kind}"
    for destination in ("github-packages", "rubygems")
    for kind in ("bootstrap", "normal01", "normal02", "normal03", "normal04")
)
RUBY_REGISTRY_PARTITIONS = {
    "eligibility": 3,
    "pre-marker": 3,
    "execute": 20,
    "zero-action": 3,
    "remote-consumer": 3,
}
_SHA = re.compile(r"[0-9a-f]{40}\Z")
_DIGEST = re.compile(r"sha256:[0-9a-f]{64}\Z")
_GENERATION = re.compile(r"[0-9a-f]{32}\Z")
_MAX_LIFETIME = timedelta(hours=4)


def _time(value: JsonValue) -> datetime:
    text = ruby_text(value)
    instant = datetime.fromisoformat(text)
    if not text.endswith("Z") or instant.utcoffset() != timedelta():
        message = "Ruby operation timestamps must be UTC"
        raise ValueError(message)
    return instant


def _digest(value: JsonValue) -> str:
    text = ruby_text(value)
    if not _DIGEST.fullmatch(text):
        message = "Ruby operation requires a SHA-256 identity"
        raise ValueError(message)
    return text


def _sha(value: JsonValue) -> str:
    text = ruby_text(value)
    if not _SHA.fullmatch(text):
        message = "Ruby operation requires a full Git identity"
        raise ValueError(message)
    return text


def _same(actual: JsonValue, expected: JsonValue) -> None:
    if canonicalize(actual) != canonicalize(expected):
        message = "Ruby operation differs from its closed authority"
        raise ValueError(message)


def ruby_operation_binding(slot: str) -> dict[str, JsonValue]:
    """Resolve a finite slot to its fixed service and workflow tuple."""
    if slot not in RUBY_OPERATION_SLOTS:
        message = "Ruby operation slot is outside the finite campaign"
        raise ValueError(message)
    destination, kind = slot.rsplit("-", 1)
    registry = RubyRegistry(destination)
    bootstrap = kind == "bootstrap"
    index = 0 if destination == "github-packages" else 1
    return {
        "repository": "hcoona/three",
        "ref": "refs/heads/main",
        "actor": "hcoona",
        "actor-id": 712433,
        "reviewer": "hcoona",
        "reviewer-id": 712433,
        "package": RUBY_RELEASE_UNIT,
        "destination": destination,
        "registry": registry.origin,
        "environment": registry.environment,
        "workflow": "workflow-delivery-v3-ruby-"
        + ("bootstrap" if bootstrap else "smoke")
        + ".yml",
        "kind": "bootstrap" if bootstrap else "normal",
        "configuration-path": (
            RUBY_CONFIGURATION_PATHS[index]
            if bootstrap
            else RUBY_GOVERNANCE_PATHS[index]
        ),
        "operation-profile-digest": registry.profile_digest,
    }


def disabled_ruby_operation_envelope() -> dict[str, JsonValue]:
    """Describe protected constraints with no executable slot enabled."""
    return {
        "schema": "workflow-delivery/v3/ruby-operation-envelope-v1",
        "repository": "hcoona/three",
        "ref": "refs/heads/main",
        "path": RUBY_ENVELOPE_PATH,
        "maximum-request-lifetime-seconds": 14400,
        "registry-partitions": cast(
            "JsonValue", dict(RUBY_REGISTRY_PARTITIONS)
        ),
        "maximum-uploads": 1,
        "maximum-oidc-assertions": 1,
        "maximum-token-exchanges": 1,
        "obligations": [
            "exact-target-nbgv",
            "fresh-current-run-build",
            "native-content-and-clean-consumer-qualification",
            "independent-request-admission",
            "durable-predispatch-reservation",
            "current-run-approval-for-action",
            "exact-remote-original-and-independent-terminal-audit",
        ],
        "slots": dict.fromkeys(RUBY_OPERATION_SLOTS),
    }


@dataclass(frozen=True, slots=True)
class RubyOperationEnvelope:
    """Protected constraints; enabling a slot requires a protected change."""

    content: bytes

    def __post_init__(self) -> None:
        """Reject unknown fields, modified ceilings and partial slot state."""
        fixed = disabled_ruby_operation_envelope()
        doc = ruby_object(parse_canonical_json(self.content), set(fixed))
        for key in fixed.keys() - {"slots"}:
            _same(doc[key], fixed[key])
        slots = ruby_object(doc["slots"], set(RUBY_OPERATION_SLOTS))
        generations: set[str] = set()
        for slot, value in slots.items():
            if value is None:
                continue
            enabled = ruby_object(
                value,
                {
                    "binding",
                    "generation",
                    "not-before",
                    "expires-at",
                    "configuration-digest",
                    "caller-digest",
                    "protocol-digest",
                    "github-budget-digest",
                },
            )
            _same(enabled["binding"], ruby_operation_binding(slot))
            generation = ruby_text(enabled["generation"])
            if (
                not _GENERATION.fullmatch(generation)
                or generation in generations
            ):
                message = "Ruby operation generation is invalid or duplicated"
                raise ValueError(message)
            generations.add(generation)
            start, end = (
                _time(enabled["not-before"]),
                _time(enabled["expires-at"]),
            )
            if not start < end <= start + timedelta(days=7):
                message = (
                    "Ruby slot admission must have a finite seven-day window"
                )
                raise ValueError(message)
            for key in (
                "configuration-digest",
                "caller-digest",
                "protocol-digest",
                "github-budget-digest",
            ):
                _digest(enabled[key])

    @property
    def document(self) -> dict[str, JsonValue]:
        """Return admitted canonical constraints."""
        return parse_canonical_json(self.content)

    def enabled_slot(self, slot: str, now: datetime) -> dict[str, JsonValue]:
        """Require a currently enabled finite slot without consuming it."""
        ruby_operation_binding(slot)
        value = ruby_object(self.document["slots"])[slot]
        if value is None:
            message = "Ruby operation slot is disabled"
            raise ValueError(message)
        enabled = ruby_object(value)
        if now.tzinfo is None or not (
            _time(enabled["not-before"]) <= now < _time(enabled["expires-at"])
        ):
            message = "Ruby operation slot is outside its admission window"
            raise ValueError(message)
        return enabled


@dataclass(frozen=True, slots=True)
class RubyOperationRequest:
    """Post-merge exact bytes; never a self-referential Git input."""

    content: bytes
    envelope: RubyOperationEnvelope

    def __post_init__(self) -> None:
        """Bind a closed request to one protected source/control target."""
        doc = ruby_object(
            parse_canonical_json(self.content),
            {
                "schema",
                "slot",
                "generation",
                "binding",
                "target",
                "control",
                "tree",
                "nbgv",
                "source-manifest-digest",
                "envelope-digest",
                "configuration-digest",
                "caller-digest",
                "protocol-digest",
                "github-budget-digest",
                "issued-at",
                "expires-at",
            },
        )
        _same(doc["schema"], "workflow-delivery/v3/ruby-operation-request-v1")
        slot = ruby_text(doc["slot"])
        issued, expires = _time(doc["issued-at"]), _time(doc["expires-at"])
        enabled = self.envelope.enabled_slot(slot, issued)
        if (
            not issued
            < expires
            <= min(issued + _MAX_LIFETIME, _time(enabled["expires-at"]))
        ):
            message = "Ruby request lifetime exceeds its protected envelope"
            raise ValueError(message)
        for key in (
            "generation",
            "binding",
            "configuration-digest",
            "caller-digest",
            "protocol-digest",
            "github-budget-digest",
        ):
            _same(doc[key], enabled[key])
        _same(doc["envelope-digest"], ruby_digest(self.envelope.content))
        target = _sha(doc["target"])
        _same(doc["control"], target)
        _sha(doc["tree"])
        _digest(doc["source-manifest-digest"])
        facts = ruby_nbgv_facts_from_document(doc["nbgv"])
        if (
            facts.target != target
            or parse_canonical_json(facts.raw_bytes)["PublicRelease"]
            is not True
        ):
            message = (
                "Ruby operation requires exact protected-main version facts"
            )
            raise ValueError(message)

    @property
    def document(self) -> dict[str, JsonValue]:
        """Return the sealed execution binding."""
        return parse_canonical_json(self.content)

    @property
    def digest(self) -> str:
        """Identify the exact bytes independently reviewed before dispatch."""
        return ruby_digest(self.content)

    def require_current(
        self, environment: Mapping[str, str], now: datetime
    ) -> None:
        """Check native current-run identity before any registry access."""
        doc = self.document
        self.envelope.enabled_slot(ruby_text(doc["slot"]), now)
        binding = ruby_object(doc["binding"])
        expected = {
            "GITHUB_REPOSITORY": "hcoona/three",
            "GITHUB_REF": "refs/heads/main",
            "GITHUB_EVENT_NAME": "workflow_dispatch",
            "GITHUB_ACTOR": "hcoona",
            "GITHUB_ACTOR_ID": "712433",
            "GITHUB_TRIGGERING_ACTOR": "hcoona",
            "GITHUB_RUN_ATTEMPT": "1",
            "GITHUB_SHA": ruby_text(doc["target"]),
            "GITHUB_WORKFLOW_SHA": ruby_text(doc["target"]),
            "GITHUB_WORKFLOW_REF": "hcoona/three/.github/workflows/"
            + ruby_text(binding["workflow"])
            + "@refs/heads/main",
        }
        if any(
            environment.get(key) != value for key, value in expected.items()
        ) or not (_time(doc["issued-at"]) <= now < _time(doc["expires-at"])):
            message = (
                "Ruby execution differs from its admitted request or lifetime"
            )
            raise ValueError(message)
        run = environment.get("GITHUB_RUN_ID", "")
        if not re.fullmatch(r"[1-9][0-9]*", run):
            message = "Ruby operation requires one native current run"
            raise ValueError(message)


def validate_ruby_operation_admission(
    content: bytes, request: RubyOperationRequest, *, review: bytes
) -> dict[str, JsonValue]:
    """Bind a separately retained independent verdict; do not infer trust."""
    doc = ruby_object(
        parse_canonical_json(content),
        {
            "schema",
            "request-digest",
            "caller-digest",
            "review-digest",
            "reviewer",
            "author",
            "verdict",
            "carrier",
            "reviewed-at",
            "ledger-digest",
        },
    )
    _same(doc["schema"], "workflow-delivery/v3/ruby-operation-admission-v1")
    _same(doc["request-digest"], request.digest)
    _same(doc["caller-digest"], request.document["caller-digest"])
    _same(doc["review-digest"], ruby_digest(review))
    _digest(doc["ledger-digest"])
    _same(doc["verdict"], "admitted")
    reviewer, author = ruby_text(doc["reviewer"]), ruby_text(doc["author"])
    if (
        reviewer == author
        or not review.strip()
        or not re.fullmatch(
            r"https://github\.com/hcoona/three/issues/954#issuecomment-[1-9][0-9]*",
            ruby_text(doc["carrier"]),
        )
        or not (
            _time(request.document["issued-at"])
            <= _time(doc["reviewed-at"])
            < _time(request.document["expires-at"])
        )
    ):
        message = "Ruby request lacks separately retained independent admission"
        raise ValueError(message)
    return doc


def validate_ruby_operation_reservation(
    content: bytes,
    request: RubyOperationRequest,
    admission: bytes,
    *,
    review: bytes,
) -> dict[str, JsonValue]:
    """Join reserved bytes to the exact independent admission before runtime."""
    admitted = validate_ruby_operation_admission(
        admission, request, review=review
    )
    doc = ruby_object(
        parse_canonical_json(content),
        {
            "schema",
            "ledger-digest",
            "slot",
            "generation",
            "request-digest",
            "admission-digest",
            "reserved-at",
        },
    )
    _same(doc["schema"], "workflow-delivery/v3/ruby-operation-reservation-v1")
    _same(doc["ledger-digest"], admitted["ledger-digest"])
    _same(doc["request-digest"], request.digest)
    _same(doc["admission-digest"], ruby_digest(admission))
    for key in ("slot", "generation"):
        _same(doc[key], request.document[key])
    if not (
        _time(admitted["reviewed-at"])
        <= _time(doc["reserved-at"])
        < _time(request.document["expires-at"])
    ):
        message = (
            "Ruby reservation is outside its independently admitted lifetime"
        )
        raise ValueError(message)
    return doc
