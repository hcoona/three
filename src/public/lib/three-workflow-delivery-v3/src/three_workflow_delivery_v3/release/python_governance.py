"""Independent fail-closed Python registry admission and protected bindings."""

from __future__ import annotations

import re
from dataclasses import dataclass
from datetime import datetime, timedelta
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.repository.python_provider import (
    PYTHON_RELEASE_UNIT,
    python_object,
    python_text,
)

if TYPE_CHECKING:
    from three_workflow_delivery_v3.adapters.pypi import PythonRegistry


PYTHON_WORKFLOW = ".github/workflows/workflow-delivery-v3-python-smoke.yml"
PYTHON_PUBLISHER = "publish-python"
_MAX_GOVERNANCE_AGE = timedelta(days=90)
_DIGEST = re.compile(r"sha256:[0-9a-f]{64}\Z")


def python_governance_path(registry: PythonRegistry) -> str:
    """Select an independent protected file, never target-selected authority."""
    return (
        ".github/workflow-delivery/governance/"
        f"hcoona-release-smoke-python-{registry.name}.json"
    )


def python_publisher_tuple(registry: PythonRegistry) -> dict[str, JsonValue]:
    """Close the exact future OIDC registration without provisioning it."""
    return {
        "repository": "hcoona/three",
        "owner-id": 712433,
        "project": PYTHON_RELEASE_UNIT,
        "registry": registry.origin,
        "audience": registry.name,
        "workflow": PYTHON_WORKFLOW.rsplit("/", 1)[1],
        "environment": registry.environment,
        "selected-ref": "refs/heads/main",
    }


def blocked_python_governance(registry: PythonRegistry) -> dict[str, JsonValue]:
    """Represent missing native and configuration evidence."""
    return {
        "schema": "workflow-delivery/v3/python-governance-v1",
        "publisher": python_publisher_tuple(registry),
        "accepted-operator": "hcoona",
        "live_enabled": False,
        "state": "blocked",
        "operation-profile-digest": registry.profile_digest,
        "inspected-at": None,
        "expires-at": None,
        "native-acceptance": None,
        "configuration": None,
        "source-evidence-revision": None,
    }


def _time(value: JsonValue) -> datetime:
    text = python_text(value)
    value_time = datetime.fromisoformat(text)
    if not text.endswith("Z") or value_time.utcoffset() != timedelta():
        message = "Python Governance timestamps must be UTC"
        raise ValueError(message)
    return value_time


@dataclass(frozen=True, slots=True)
class PythonGovernance:
    """Protected source bytes plus their immutable current-main provenance."""

    registry: PythonRegistry
    content: bytes
    source_commit: str
    observed_at: datetime

    def __post_init__(self) -> None:  # noqa: C901
        """Admit closed disabled or evidenced ready state, never a hybrid."""
        doc = python_object(
            parse_canonical_json(self.content),
            set(blocked_python_governance(self.registry)),
        )
        fixed = blocked_python_governance(self.registry)
        for key in (
            "schema",
            "publisher",
            "accepted-operator",
            "operation-profile-digest",
        ):
            if canonicalize(doc[key]) != canonicalize(fixed[key]):
                message = "Python Governance authority or profile mismatch"
                raise ValueError(message)
        if (
            not re.fullmatch(r"[0-9a-f]{40}", self.source_commit)
            or self.observed_at.tzinfo is None
        ):
            message = "Python Governance lacks protected source provenance"
            raise ValueError(message)
        if doc["live_enabled"] is False:
            if canonicalize(doc) != canonicalize(fixed):
                message = "disabled Python Governance must retain blocked state"
                raise ValueError(message)
            return
        if doc["live_enabled"] is not True or doc["state"] != "ready":
            message = "Python Governance state is not an explicit admission"
            raise ValueError(message)
        inspected = _time(doc["inspected-at"])
        expires = _time(doc["expires-at"])
        if not inspected < expires <= inspected + _MAX_GOVERNANCE_AGE:
            message = "Python Governance freshness window is invalid"
            raise ValueError(message)
        native = python_object(
            doc["native-acceptance"],
            {
                "suite",
                "registry",
                "project",
                "profile-digest",
                "evidence-digest",
                "generation",
                "review",
                "passed",
            },
        )
        if (
            native["suite"] != "workflow-delivery/v3/python-native-suite-v1"
            or native["registry"] != self.registry.name
            or native["project"] != PYTHON_RELEASE_UNIT
            or native["profile-digest"] != self.registry.profile_digest
            or native["passed"] is not True
            or not _DIGEST.fullmatch(python_text(native["evidence-digest"]))
            or not re.fullmatch(
                r"[0-9a-f]{32}", python_text(native["generation"])
            )
            or not python_text(native["review"]).startswith(
                "https://github.com/hcoona/three/"
            )
        ):
            message = (
                "Python native acceptance does not admit this exact destination"
            )
            raise ValueError(message)
        configuration = python_object(
            doc["configuration"],
            {
                "publisher-registration",
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
            "publisher-registration": python_publisher_tuple(self.registry),
            "reviewer-id": 712433,
            "prevent-self-review": False,
            "can-admins-bypass": False,
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
                message = (
                    "Python configuration differs from admitted protections"
                )
                raise ValueError(message)
        if (
            type(configuration["environment-id"]) is not int
            or cast("int", configuration["environment-id"]) <= 0
            or not _DIGEST.fullmatch(
                python_text(configuration["attestation-digest"])
            )
        ):
            message = (
                "Python configuration lacks native identity or attestation"
            )
            raise ValueError(message)
        if not re.fullmatch(
            r"[0-9a-f]{40}", python_text(doc["source-evidence-revision"])
        ):
            message = "Python Governance lacks pinned source-evidence revision"
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
                "Python registry remains disabled pending separate admission"
            )
            raise ValueError(message)
        if now.tzinfo is None or not _time(
            doc["inspected-at"]
        ) <= self.observed_at <= now < _time(doc["expires-at"]):
            message = "Python Governance is stale or not current"
            raise ValueError(message)

    def require_same_live(self, fresh: PythonGovernance, now: datetime) -> None:
        """Reject changed admission; flag-off is not rollback after mutation."""
        fresh.require_live(now)
        if (
            fresh.registry != self.registry
            or fresh.digest != self.digest
            or fresh.observed_at < self.observed_at
        ):
            message = "Python protected Governance changed within the Attempt"
            raise ValueError(message)
