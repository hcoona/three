"""Separate first-project configuration; never normal ownership authority."""

from __future__ import annotations

import re
from dataclasses import dataclass
from datetime import datetime, timedelta
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3._ruby_native import (
    ruby_digest,
    ruby_object,
    ruby_text,
)
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.release.ruby_governance import (
    ruby_publisher_tuple,
)
from three_workflow_delivery_v3.release.ruby_operation import (
    RUBY_SUCCESSOR_VERSION,
    RubyOperationRequest,
    ruby_operation_binding,
    validate_ruby_operation_admission,
)

if TYPE_CHECKING:
    from three_workflow_delivery_v3.adapters.rubygems import RubyRegistry

_SCOPE = (
    "Reviewed first-project configuration and service registration; "
    "no existing package ownership, name reservation or normal Live admission."
)


def _time(value: JsonValue) -> datetime:
    text = ruby_text(value)
    result = datetime.fromisoformat(text)
    if not text.endswith("Z") or result.utcoffset() != timedelta():
        message = "Ruby configuration requires UTC timestamps"
        raise ValueError(message)
    return result


def _digest(value: JsonValue) -> str:
    text = ruby_text(value)
    if not re.fullmatch(r"sha256:[0-9a-f]{64}", text):
        message = "Ruby configuration requires a SHA-256 identity"
        raise ValueError(message)
    return text


def _same(actual: JsonValue, expected: JsonValue) -> None:
    if canonicalize(actual) != canonicalize(expected):
        message = "Ruby first-project binding differs from fixed authority"
        raise ValueError(message)


def ruby_bootstrap_registration(registry: RubyRegistry) -> dict[str, JsonValue]:
    """Select first-project intent without claiming existing ownership."""
    return {
        **ruby_publisher_tuple(registry),
        "workflow": "workflow-delivery-v3-ruby-bootstrap.yml",
        "account": "hcoona"
        if registry.name == "github-packages"
        else "Raffle2282",
        "state": "associated-creation"
        if registry.name == "github-packages"
        else "pending-publisher",
    }


def blocked_ruby_bootstrap_configuration(
    registry: RubyRegistry,
) -> dict[str, JsonValue]:
    """Keep unavailable first-project configuration wholly disabled."""
    return {
        "schema": "workflow-delivery/v3/ruby-bootstrap-configuration-v1",
        "state": "blocked",
        "binding": ruby_operation_binding(registry.name + "-bootstrap"),
        "source-evidence-revision": None,
        "operation-profile-digest": registry.profile_digest,
        "protocol-digest": None,
        "inspected-at": None,
        "expires-at": None,
        "github-controls": None,
        "first-project-registration": None,
        "attestation-digest": None,
        "scope-limitation": _SCOPE,
    }


def _controls(value: JsonValue, registry: RubyRegistry) -> dict[str, JsonValue]:
    expected: dict[str, JsonValue] = {
        "reviewer-id": 712433,
        "prevent-self-review": False,
        "wait-timer": 0,
        "protected-main-only": True,
        "sentinel": registry.environment + "/v1",
        "secret-count": 0,
        "accepted-writers": ["hcoona"],
    }
    native_ids = {
        "environment-id",
        "reviewer-rule-id",
        "deployment-branch-rule-id",
    }
    controls = ruby_object(
        value,
        {
            *expected,
            *native_ids,
            "can-admins-bypass",
            "control-baseline-digest",
        },
    )
    for key, fixed in expected.items():
        _same(controls[key], fixed)
    for key in native_ids:
        if type(controls[key]) is not int or cast("int", controls[key]) <= 0:
            message = "Ruby first-project controls lack native resource IDs"
            raise ValueError(message)
    if type(controls["can-admins-bypass"]) is not bool:
        message = "Ruby first-project controls require actual bypass state"
        raise ValueError(message)
    _digest(controls["control-baseline-digest"])
    return controls


@dataclass(frozen=True, slots=True)
class RubyBootstrapConfiguration:
    """Protected configuration bytes with a bounded reviewed lifetime."""

    registry: RubyRegistry
    content: bytes

    def __post_init__(self) -> None:
        """Reject normal ownership claims and partly enabled configuration."""
        fixed = blocked_ruby_bootstrap_configuration(self.registry)
        doc = ruby_object(parse_canonical_json(self.content), set(fixed))
        for key in (
            "schema",
            "binding",
            "operation-profile-digest",
            "scope-limitation",
        ):
            _same(doc[key], fixed[key])
        if doc["state"] == "blocked":
            _same(doc, fixed)
            return
        if doc["state"] != "first-project-ready":
            message = "Ruby configuration is not first-project admission"
            raise ValueError(message)
        inspected, expires = (
            _time(doc["inspected-at"]),
            _time(doc["expires-at"]),
        )
        if not inspected < expires <= inspected + timedelta(days=7):
            message = "Ruby bootstrap configuration exceeds its finite lifetime"
            raise ValueError(message)
        if not re.fullmatch(
            r"[0-9a-f]{40}", ruby_text(doc["source-evidence-revision"])
        ):
            message = "Ruby configuration requires pinned source evidence"
            raise ValueError(message)
        _digest(doc["protocol-digest"])
        _digest(doc["attestation-digest"])
        _controls(doc["github-controls"], self.registry)
        _same(
            doc["first-project-registration"],
            ruby_bootstrap_registration(self.registry),
        )

    @property
    def document(self) -> dict[str, JsonValue]:
        """Return a fresh parsed view of the immutable source bytes."""
        return parse_canonical_json(self.content)

    @property
    def digest(self) -> str:
        """Bind exact canonical configuration, not a refreshed timestamp."""
        return ruby_digest(self.content)

    def require_current(self, now: datetime) -> None:
        """Reject blocked, not-yet-inspected and expired configuration."""
        doc = self.document
        if (
            doc["state"] != "first-project-ready"
            or now.tzinfo is None
            or not (
                _time(doc["inspected-at"]) <= now < _time(doc["expires-at"])
            )
        ):
            message = "Ruby first-project configuration is unavailable or stale"
            raise ValueError(message)


@dataclass(frozen=True, slots=True)
class RubyFirstProjectInspection:
    """Screened current absence facts joined by independent admission review."""

    content: bytes
    configuration: RubyBootstrapConfiguration

    def __post_init__(self) -> None:
        """Close the exact target/slot and finite dispatch/action intervals."""
        doc = ruby_object(
            parse_canonical_json(self.content),
            {
                "schema",
                "destination",
                "slot",
                "generation",
                "target",
                "configuration-digest",
                "control-baseline-digest",
                "operator-principal",
                "absence-kind",
                "absence-verdict",
                "evidence-manifest-digest",
                "observed-at",
                "dispatch-by",
                "expires-at",
            },
        )
        registry = self.configuration.registry
        github = registry.name == "github-packages"
        successor = (
            doc["schema"]
            == "workflow-delivery/v3/ruby-first-project-inspection-v2"
        )
        if successor and not github:
            message = "Ruby successor inspection is only for GitHub Packages"
            raise ValueError(message)
        observed = _time(doc["observed-at"])
        self.configuration.require_current(observed)
        controls = _controls(
            self.configuration.document["github-controls"], registry
        )
        expected: dict[str, JsonValue] = {
            "schema": "workflow-delivery/v3/ruby-first-project-inspection-v"
            + ("2" if successor else "1"),
            "destination": registry.name,
            "slot": registry.name
            + ("-bootstrap02" if successor else "-bootstrap"),
            "configuration-digest": self.configuration.digest,
            "control-baseline-digest": controls["control-baseline-digest"],
            "operator-principal": {"login": "hcoona", "id": 712433}
            if github
            else {"kind": "unauthenticated-public-endpoint"},
            "absence-kind": "github-owner-package-inventory"
            if github
            else "rubygems-project-owners",
            "absence-verdict": "absent",
        }
        for key, fixed in expected.items():
            _same(doc[key], fixed)
        if not re.fullmatch(
            r"[0-9a-f]{40}", ruby_text(doc["target"])
        ) or not re.fullmatch(r"[0-9a-f]{32}", ruby_text(doc["generation"])):
            message = "Ruby first-project inspection lacks an exact operation"
            raise ValueError(message)
        _digest(doc["evidence-manifest-digest"])
        if _time(doc["dispatch-by"]) != observed + timedelta(
            minutes=10
        ) or not (
            observed
            < _time(doc["expires-at"])
            <= min(
                observed + timedelta(hours=2),
                _time(self.configuration.document["expires-at"]),
            )
        ):
            message = "Ruby first-project inspection has invalid deadlines"
            raise ValueError(message)

    @property
    def document(self) -> dict[str, JsonValue]:
        """Expose screened facts without credentials or owner inventory."""
        return parse_canonical_json(self.content)

    def require_request(
        self,
        request: RubyOperationRequest,
        now: datetime,
        *,
        before_dispatch: bool = False,
    ) -> None:
        """Join sealed request without a circular request/inspection hash."""
        doc, selected = self.document, request.document
        if (
            doc["schema"]
            == "workflow-delivery/v3/ruby-first-project-inspection-v2"
            and request.envelope.version != RUBY_SUCCESSOR_VERSION
        ):
            message = "Ruby successor inspection requires its v2 envelope"
            raise ValueError(message)
        request.envelope.enabled_slot(ruby_text(selected["slot"]), now)
        self.configuration.require_current(now)
        for key in ("target", "slot", "generation", "configuration-digest"):
            _same(selected[key], doc[key])
        _same(
            selected["protocol-digest"],
            self.configuration.document["protocol-digest"],
        )
        if (
            now.tzinfo is None
            or not (
                _time(doc["observed-at"])
                <= _time(selected["issued-at"])
                <= now
                < _time(selected["expires-at"])
                <= _time(doc["expires-at"])
            )
            or (before_dispatch and now >= _time(doc["dispatch-by"]))
        ):
            message = (
                "Ruby first-project request exceeds its inspection lifetime"
            )
            raise ValueError(message)


def validate_ruby_bootstrap_review(  # noqa: PLR0913 - explicit joined authorities
    content: bytes,
    request: RubyOperationRequest,
    configuration: RubyBootstrapConfiguration,
    inspection: RubyFirstProjectInspection,
    admission: bytes,
    *,
    now: datetime,
) -> dict[str, JsonValue]:
    """Join review bytes; the operator establishes reviewer provenance."""
    doc = ruby_object(
        parse_canonical_json(content),
        {
            "schema",
            "request-digest",
            "configuration-digest",
            "first-project-inspection-digest",
            "caller-digest",
            "protocol-digest",
            "reviewer",
            "author",
            "reviewed-at",
            "verdict",
            "carrier",
        },
    )
    if inspection.configuration != configuration:
        message = "Ruby bootstrap review selected a different configuration"
        raise ValueError(message)
    inspection.require_request(request, now)
    admitted = validate_ruby_operation_admission(
        admission, request, review=content
    )
    expected: dict[str, JsonValue] = {
        "schema": "workflow-delivery/v3/ruby-operation-admission-review-v1",
        "request-digest": request.digest,
        "configuration-digest": configuration.digest,
        "first-project-inspection-digest": ruby_digest(inspection.content),
        "caller-digest": request.document["caller-digest"],
        "protocol-digest": request.document["protocol-digest"],
        **{
            key: admitted[key]
            for key in (
                "reviewer",
                "author",
                "reviewed-at",
                "verdict",
                "carrier",
            )
        },
    }
    _same(doc, expected)
    if _time(doc["reviewed-at"]) > now:
        message = "Ruby bootstrap review is not yet current"
        raise ValueError(message)
    return doc
