"""Closed first-project request and fixed runner-UTC authority window."""

from __future__ import annotations

import math
import re
from dataclasses import dataclass
from typing import TYPE_CHECKING, cast
from urllib.parse import urlsplit

from packaging.version import Version

from three_workflow_delivery_v3.acceptance.python_native_contract import (
    commit,
    digest,
    positive,
    require,
)
from three_workflow_delivery_v3.adapters.pypi import PythonRegistry
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonicalize,
    parse_canonical_json,
    parse_json_strict,
)
from three_workflow_delivery_v3.repository.python_provider import (
    PYTHON_RELEASE_UNIT,
    python_digest,
    python_object,
    python_text,
)

if TYPE_CHECKING:
    from pathlib import Path

WORKFLOW = ".github/workflows/workflow-delivery-v3-bootstrap-python.yml"
SLOT_PATHS = {
    "testpypi": ".github/workflow-delivery/bootstrap/python-request.json",
    "pypi": ".github/workflow-delivery/bootstrap/python-pypi-request.json",
}
ACCOUNTS = {"testpypi": "Backspace7980", "pypi": "Sherry7290"}
WINDOW_SECONDS = 600


@dataclass(frozen=True)
class BootstrapRequest:
    """One prospective bootstrap generation, never native admission."""

    content: bytes

    def __post_init__(self) -> None:
        """Reject foreign or incomplete first-project authority."""
        doc = python_object(
            parse_canonical_json(self.content),
            {
                "schema",
                "generation",
                "registry",
                "account",
                "project",
                "workflow",
                "profile-digest",
                "source",
                "fixture-digests",
                "environment",
                "authorization",
                "configuration",
            },
        )
        require(
            doc["schema"] == "workflow-delivery/v3/python-bootstrap-request"
            and doc["account"] == ACCOUNTS[self.registry.name]
            and doc["project"] == PYTHON_RELEASE_UNIT
            and doc["workflow"] == WORKFLOW
            and doc["profile-digest"] == self.registry.profile_digest,
            "foreign bootstrap request",
        )
        require(
            re.fullmatch(r"[0-9a-f]{32}", python_text(doc["generation"]))
            is not None,
            "invalid bootstrap generation",
        )
        source = python_object(doc["source"], {"commit", "version"})
        commit(source["commit"])
        version = python_text(source["version"])
        parsed = Version(version)
        require(
            str(parsed) == version
            and parsed.local is None
            and parsed.is_prerelease,
            "bootstrap requires a public prerelease",
        )
        for value in python_object(
            doc["fixture-digests"], {"wheel", "sdist"}
        ).values():
            digest(value)
        environment = python_object(
            doc["environment"], {"id", "name", "sentinel"}
        )
        positive(environment["id"])
        require(
            environment["name"] == self.registry.environment
            and environment["sentinel"] == self.sentinel,
            "bootstrap Environment differs",
        )
        for name in ("authorization", "configuration"):
            reference = python_object(doc[name], {"url", "digest"})
            url = urlsplit(python_text(reference["url"]))
            require(
                url.scheme == "https"
                and bool(url.netloc)
                and url.username is None
                and url.password is None,
                "invalid reviewed bootstrap evidence reference",
            )
            digest(reference["digest"])

    @property
    def document(self) -> dict[str, JsonValue]:
        """Return independent parsed immutable request facts."""
        return parse_canonical_json(self.content)

    @property
    def registry(self) -> PythonRegistry:
        """Return the closed destination selected by the retained request."""
        return PythonRegistry(python_text(self.document["registry"]))

    @property
    def sentinel(self) -> str:
        """Bind approval proof to the selected destination Environment."""
        return f"{self.registry.environment}/v1"

    @property
    def digest(self) -> str:
        """Bind exact request bytes."""
        return python_digest(self.content)

    @property
    def source(self) -> dict[str, JsonValue]:
        """Return the one source and public prerelease."""
        return cast("dict[str, JsonValue]", self.document["source"])


def load_bootstrap_request(
    root: Path, expected_digest: str, registry: PythonRegistry
) -> BootstrapRequest:
    """Read the protected slot without overrides or placeholder fallback."""
    value = parse_json_strict((root / SLOT_PATHS[registry.name]).read_bytes())
    require(value is not None, "bootstrap request slot is disabled")
    request = BootstrapRequest(canonicalize(value))
    require(
        request.registry == registry and request.digest == expected_digest,
        "bootstrap request selection or digest differs",
    )
    return request


def utc_number(value: JsonValue) -> float:
    """Admit finite non-Boolean epoch seconds from trusted runner UTC."""
    require(
        type(value) in {int, float}
        and math.isfinite(cast("float", value))
        and cast("float", value) > 0,
        "invalid bootstrap UTC instant",
    )
    return float(cast("float", value))


def authority_window(
    value: JsonValue, now: float | None = None
) -> tuple[float, float]:
    """Validate the fixed half-open window without renewing it on readback."""
    doc = python_object(value, {"created-at", "deadline"})
    created = utc_number(doc["created-at"])
    deadline = utc_number(doc["deadline"])
    require(deadline == created + WINDOW_SECONDS, "bootstrap deadline differs")
    if now is not None:
        require(
            created <= utc_number(now) < deadline,
            "bootstrap authority future or expired",
        )
    return created, deadline


def platform_facts(run_id: int, tooling_sha: str) -> dict[str, JsonValue]:
    """Close exactly the existing retained platform projection."""
    positive(run_id)
    commit(tooling_sha)
    return {
        "GITHUB_REPOSITORY": "hcoona/three",
        "GITHUB_REPOSITORY_ID": "1102295886",
        "GITHUB_ACTOR": "hcoona",
        "GITHUB_ACTOR_ID": "712433",
        "GITHUB_REF": "refs/heads/main",
        "GITHUB_REF_PROTECTED": "true",
        "GITHUB_RUN_ID": str(run_id),
        "GITHUB_RUN_ATTEMPT": "1",
        "GITHUB_SHA": tooling_sha,
        "GITHUB_WORKFLOW_SHA": tooling_sha,
        "GITHUB_WORKFLOW_REF": f"hcoona/three/{WORKFLOW}@refs/heads/main",
        "RUNNER_OS": "Linux",
    }


def phase_binding(
    request: BootstrapRequest, run_id: int, tooling_sha: str, phase: str
) -> dict[str, JsonValue]:
    """Bind request, run, tooling and transport profile."""
    positive(run_id)
    commit(tooling_sha)
    return {
        "request-digest": request.digest,
        "run-id": run_id,
        "run-attempt": 1,
        "tooling-sha": tooling_sha,
        "producer": f"{phase}-python-bootstrap",
        "profile": request.registry.profile,
    }
