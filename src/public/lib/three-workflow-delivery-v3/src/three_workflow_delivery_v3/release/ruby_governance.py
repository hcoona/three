"""Independent fail-closed Ruby registry admission and protected bindings."""

from __future__ import annotations

import re
from dataclasses import dataclass
from datetime import datetime, timedelta
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3._ruby_native import (
    RUBY_RELEASE_UNIT,
    ruby_object,
    ruby_text,
)
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)

if TYPE_CHECKING:
    from three_workflow_delivery_v3.adapters.rubygems import RubyRegistry


RUBY_WORKFLOW = ".github/workflows/workflow-delivery-v3-ruby-smoke.yml"
_MAX_GOVERNANCE_AGE = timedelta(days=90)
_DIGEST = re.compile(r"sha256:[0-9a-f]{64}\Z")


def ruby_publisher_job(
    registry: RubyRegistry, *, bootstrap: bool = False
) -> str:
    """Name the actual destination-specific least-privilege publisher job."""
    return (
        "publish-ruby"
        + ("-bootstrap" if bootstrap else "")
        + "-"
        + registry.name
    )


def ruby_governance_path(registry: RubyRegistry) -> str:
    """Select an independent protected file, never target-selected authority."""
    return (
        ".github/workflow-delivery/governance/"
        f"hcoona-release-smoke-ruby-{registry.name}.json"
    )


def ruby_publisher_tuple(registry: RubyRegistry) -> dict[str, JsonValue]:
    """Close the exact future OIDC registration without provisioning it."""
    return {
        "repository": "hcoona/three",
        "owner-id": 712433,
        "project": RUBY_RELEASE_UNIT,
        "registry": registry.origin,
        "authentication": (
            "github-job-token"
            if registry.name == "github-packages"
            else "ruby-oidc"
        ),
        "audience": None
        if registry.name == "github-packages"
        else "rubygems.org",
        "workflow": RUBY_WORKFLOW.rsplit("/", 1)[1],
        "environment": registry.environment,
        "selected-ref": "refs/heads/main",
    }


def blocked_ruby_governance(registry: RubyRegistry) -> dict[str, JsonValue]:
    """Represent missing configuration and source-evidence admission."""
    return {
        "schema": "workflow-delivery/v3/ruby-governance-v1",
        "publisher": ruby_publisher_tuple(registry),
        "accepted-operator": "hcoona",
        "live_enabled": False,
        "state": "blocked",
        "operation-profile-digest": registry.profile_digest,
        "inspected-at": None,
        "expires-at": None,
        "configuration": None,
        "source-evidence-revision": None,
    }


def _time(value: JsonValue) -> datetime:
    text = ruby_text(value)
    value_time = datetime.fromisoformat(text)
    if not text.endswith("Z") or value_time.utcoffset() != timedelta():
        message = "Ruby Governance timestamps must be UTC"
        raise ValueError(message)
    return value_time


def _validate_package_access(value: JsonValue, registry: RubyRegistry) -> None:
    """Bind effective visibility and read/write access to this destination."""
    access = ruby_object(
        value,
        {"visibility", "repository", "read", "publish", "authentication"},
    )
    if (
        access["visibility"] not in {"private", "public"}
        or (registry.name == "rubygems" and access["visibility"] != "public")
        or access["repository"] != "hcoona/three"
        or access["read"] is not True
        or access["publish"] is not True
        or access["authentication"]
        != ruby_publisher_tuple(registry)["authentication"]
    ):
        message = "Ruby package access lacks the admitted destination binding"
        raise ValueError(message)


@dataclass(frozen=True, slots=True)
class RubyGovernance:
    """Protected source bytes plus their immutable current-main provenance."""

    registry: RubyRegistry
    content: bytes
    source_commit: str
    observed_at: datetime

    def __post_init__(self) -> None:  # noqa: C901
        """Admit closed disabled or evidenced ready state, never a hybrid."""
        doc = ruby_object(
            parse_canonical_json(self.content),
            set(blocked_ruby_governance(self.registry)),
        )
        fixed = blocked_ruby_governance(self.registry)
        for key in (
            "schema",
            "publisher",
            "accepted-operator",
            "operation-profile-digest",
        ):
            if canonicalize(doc[key]) != canonicalize(fixed[key]):
                message = "Ruby Governance authority or profile mismatch"
                raise ValueError(message)
        if (
            not re.fullmatch(r"[0-9a-f]{40}", self.source_commit)
            or self.observed_at.tzinfo is None
        ):
            message = "Ruby Governance lacks protected source provenance"
            raise ValueError(message)
        if doc["live_enabled"] is False:
            if canonicalize(doc) != canonicalize(fixed):
                message = "disabled Ruby Governance must retain blocked state"
                raise ValueError(message)
            return
        if doc["live_enabled"] is not True or doc["state"] != "ready":
            message = "Ruby Governance state is not an explicit admission"
            raise ValueError(message)
        inspected = _time(doc["inspected-at"])
        expires = _time(doc["expires-at"])
        if not inspected < expires <= inspected + _MAX_GOVERNANCE_AGE:
            message = "Ruby Governance freshness window is invalid"
            raise ValueError(message)
        configuration = ruby_object(
            doc["configuration"],
            {
                "publisher-registration",
                "package-ownership",
                "package-access",
                "environment-id",
                "reviewer-id",
                "prevent-self-review",
                "can-admins-bypass",
                "wait-timer",
                "protected-main-only",
                "sentinel",
                "secret-count",
                "accepted-writers",
                "attestation-digest",
                "scope-limitation",
            },
        )
        expected = {
            "publisher-registration": ruby_publisher_tuple(self.registry),
            "package-ownership": {
                "package": RUBY_RELEASE_UNIT,
                "account": "hcoona"
                if self.registry.name == "github-packages"
                else "Raffle2282",
                "repository": "hcoona/three",
                "confirmed": True,
            },
            "reviewer-id": 712433,
            "prevent-self-review": False,
            "wait-timer": 0,
            "protected-main-only": True,
            "sentinel": self.registry.environment + "/v1",
            "secret-count": 0,
            "accepted-writers": ["hcoona"],
            "scope-limitation": (
                "Reviewed service registration attestation; "
                "no runtime registration inventory or "
                "token-decoding scope proof."
            ),
        }
        for key, value in expected.items():
            if canonicalize(configuration[key]) != canonicalize(
                cast("JsonValue", value)
            ):
                message = "Ruby configuration differs from admitted protections"
                raise ValueError(message)
        bypass = configuration["can-admins-bypass"]
        if type(bypass) is not bool:
            message = (
                "Ruby administrator bypass requires its actual Boolean value"
            )
            raise ValueError(message)
        _validate_package_access(configuration["package-access"], self.registry)
        if (
            type(configuration["environment-id"]) is not int
            or cast("int", configuration["environment-id"]) <= 0
            or not _DIGEST.fullmatch(
                ruby_text(configuration["attestation-digest"])
            )
        ):
            message = "Ruby configuration lacks native identity or attestation"
            raise ValueError(message)
        if not re.fullmatch(
            r"[0-9a-f]{40}", ruby_text(doc["source-evidence-revision"])
        ):
            message = "Ruby Governance lacks pinned source-evidence revision"
            raise ValueError(message)

    @property
    def document(self) -> dict[str, JsonValue]:
        """Return strictly admitted source content."""
        return cast("dict[str, JsonValue]", parse_canonical_json(self.content))

    @property
    def digest(self) -> str:
        """Return source content identity independent of observation time."""
        return canonical_sha256(self.document)

    def require_live(self, now: datetime) -> None:
        """Reject disabled, expired or future admission."""
        doc = self.document
        if doc["live_enabled"] is not True:
            message = (
                "Ruby registry remains disabled pending separate admission"
            )
            raise ValueError(message)
        if now.tzinfo is None or not _time(
            doc["inspected-at"]
        ) <= self.observed_at <= now < _time(doc["expires-at"]):
            message = "Ruby Governance is stale or not current"
            raise ValueError(message)

    def require_same_live(self, fresh: RubyGovernance, now: datetime) -> None:
        """Reject changed admission; flag-off is not rollback after mutation."""
        fresh.require_live(now)
        if (
            fresh.registry != self.registry
            or fresh.digest != self.digest
            or fresh.observed_at < self.observed_at
        ):
            message = "Ruby protected Governance changed within the Attempt"
            raise ValueError(message)
