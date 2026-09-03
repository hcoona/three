"""Strict GitHub Packages npm observation and publication Adapter."""

from __future__ import annotations

import contextlib
import hashlib
import inspect
import itertools
import os
import re
import stat
import tempfile
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass
from dataclasses import field as dataclass_field
from datetime import datetime
from pathlib import Path
from time import monotonic
from typing import TYPE_CHECKING, Protocol, cast

from three_workflow_delivery_v3.adapters.node import (
    ArtifactExpectation,
    _read_tarball,
    _validate_artifact_expectation,
    qualify_npm_artifact_contents,
)
from three_workflow_delivery_v3.adapters.npmjs import (
    DEFAULT_EXPANDED_TARBALL_LIMIT_BYTES,
    _remote_tarball_observation,
)
from three_workflow_delivery_v3.canonical import (
    canonical_sha256,
    canonicalize,
    parse_json_strict,
)
from three_workflow_delivery_v3.records.release import (
    PUBLISHER_GOVERNANCE_RECHECK_FAILED_BEFORE_RUNNER,
    ActionResult,
    AuthorizationRecord,
    BuddyExecutionIdentity,
    CapabilityAdmissionDecision,
    DestinationProjection,
    ExternalPackageCoordinate,
    ObservationRequestFacts,
    ObservationResponseFacts,
    ObservationValue,
    ProjectionObservation,
    PublicationAction,
    PublicationSnapshot,
    QualificationDecision,
    QualificationSnapshot,
    Receipt,
    ReleaseArtifact,
    ReleaseAttemptIdentity,
)
from three_workflow_delivery_v3.release.eligibility import (
    GovernanceRejectionError,
    GovernanceSourceClient,
    require_fresh_governance_identity,
)
from three_workflow_delivery_v3.release.finalizer import (
    desired_projection_state_digest,
)

if TYPE_CHECKING:
    from collections.abc import Callable

    from three_workflow_delivery_v3.canonical import JsonValue
    from three_workflow_delivery_v3.repository.descriptors import (
        GovernanceSource,
    )

GITHUB_PACKAGES_DESTINATION_ID = "npm/github-packages-hcoona-three-v1"
GITHUB_PACKAGES_REGISTRY = "https://npm.pkg.github.com"
GITHUB_PACKAGES_PACKAGE = "@hcoona/hcoona-release-smoke-npm"
GITHUB_PACKAGES_OBSERVATION_CONTRACT_ID = "npm/github-packages-observation-v1"
GITHUB_PACKAGES_OPERATION = "npm-publish-create-only"
GITHUB_PACKAGES_OWNER = "hcoona"
GITHUB_PACKAGES_REPOSITORY = "hcoona/three"
GITHUB_PACKAGES_OBSERVER_PRODUCER = "observe-github-packages"
GITHUB_PACKAGES_PUBLISHER_PRODUCER = "publish-github-packages"
GITHUB_API_ORIGIN = "https://api.github.com"
ACCEPTANCE_PACKAGE_COORDINATE = (
    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.1"
)
ACCEPTANCE_PACKAGE_NAME = "@hcoona/hcoona-release-smoke-npm"
ACCEPTANCE_REPOSITORY_URL = "git+https://github.com/hcoona/three.git"
ACCEPTANCE_WITNESS_PATH = "package/workflow-delivery/acceptance.json"
ACCEPTANCE_SCENARIO_SPECS = (
    ("absent-create-readback", "0.0.0-wdv3-acceptance.1", "wdv3-acceptance-1"),
    ("exact", "0.0.0-wdv3-acceptance.1", "wdv3-acceptance-1"),
    ("identical-race", "0.0.0-wdv3-acceptance.2", "wdv3-acceptance-2"),
    ("differing-race", "0.0.0-wdv3-acceptance.3", "wdv3-acceptance-3"),
    ("lost-response", "0.0.0-wdv3-acceptance.4", "wdv3-acceptance-4"),
)
RETRY_2_ACCEPTANCE_PACKAGE_COORDINATE = (
    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.5"
)
RETRY_2_ACCEPTANCE_SCENARIO_SPECS = (
    ("absent-create-readback", "0.0.0-wdv3-acceptance.5", "wdv3-acceptance-5"),
    ("exact", "0.0.0-wdv3-acceptance.5", "wdv3-acceptance-5"),
    ("identical-race", "0.0.0-wdv3-acceptance.6", "wdv3-acceptance-6"),
    ("differing-race", "0.0.0-wdv3-acceptance.7", "wdv3-acceptance-7"),
    ("lost-response", "0.0.0-wdv3-acceptance.8", "wdv3-acceptance-8"),
)
RETRY_3_ACCEPTANCE_PACKAGE_COORDINATE = (
    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.9"
)
RETRY_3_ACCEPTANCE_SCENARIO_SPECS = (
    ("absent-create-readback", "0.0.0-wdv3-acceptance.9", "wdv3-acceptance-9"),
    ("exact", "0.0.0-wdv3-acceptance.9", "wdv3-acceptance-9"),
    ("identical-race", "0.0.0-wdv3-acceptance.10", "wdv3-acceptance-10"),
    ("differing-race", "0.0.0-wdv3-acceptance.11", "wdv3-acceptance-11"),
    ("lost-response", "0.0.0-wdv3-acceptance.12", "wdv3-acceptance-12"),
)
RETRY_4_ACCEPTANCE_PACKAGE_COORDINATE = (
    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.13"
)
RETRY_4_ACCEPTANCE_SCENARIO_SPECS = (
    (
        "absent-create-readback",
        "0.0.0-wdv3-acceptance.13",
        "wdv3-acceptance-13",
    ),
    ("exact", "0.0.0-wdv3-acceptance.13", "wdv3-acceptance-13"),
    ("identical-race", "0.0.0-wdv3-acceptance.14", "wdv3-acceptance-14"),
    ("differing-race", "0.0.0-wdv3-acceptance.15", "wdv3-acceptance-15"),
    ("lost-response", "0.0.0-wdv3-acceptance.16", "wdv3-acceptance-16"),
)
RETRY_5_ACCEPTANCE_PACKAGE_COORDINATE = (
    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.17"
)
RETRY_5_ACCEPTANCE_SCENARIO_SPECS = (
    (
        "absent-create-readback",
        "0.0.0-wdv3-acceptance.17",
        "wdv3-acceptance-17",
    ),
    ("exact", "0.0.0-wdv3-acceptance.17", "wdv3-acceptance-17"),
    ("identical-race", "0.0.0-wdv3-acceptance.18", "wdv3-acceptance-18"),
    ("differing-race", "0.0.0-wdv3-acceptance.19", "wdv3-acceptance-19"),
    ("lost-response", "0.0.0-wdv3-acceptance.20", "wdv3-acceptance-20"),
)
_ACCEPTANCE_SUITE_PROFILES = (
    (ACCEPTANCE_PACKAGE_COORDINATE, ACCEPTANCE_SCENARIO_SPECS),
    (
        RETRY_2_ACCEPTANCE_PACKAGE_COORDINATE,
        RETRY_2_ACCEPTANCE_SCENARIO_SPECS,
    ),
    (
        RETRY_3_ACCEPTANCE_PACKAGE_COORDINATE,
        RETRY_3_ACCEPTANCE_SCENARIO_SPECS,
    ),
    (
        RETRY_4_ACCEPTANCE_PACKAGE_COORDINATE,
        RETRY_4_ACCEPTANCE_SCENARIO_SPECS,
    ),
    (
        RETRY_5_ACCEPTANCE_PACKAGE_COORDINATE,
        RETRY_5_ACCEPTANCE_SCENARIO_SPECS,
    ),
)
ACCEPTANCE_COORDINATES = {
    scenario: f"{ACCEPTANCE_PACKAGE_NAME}@{version}"
    for scenario, version, _tag in ACCEPTANCE_SCENARIO_SPECS
}
ACCEPTANCE_TAGS = frozenset(
    tag for _scenario, _version, tag in ACCEPTANCE_SCENARIO_SPECS
)
ACCEPTANCE_SCENARIOS = frozenset(
    {
        "absent-create-readback",
        "exact",
        "identical-race",
        "differing-race",
        "lost-response",
    }
)
_ACCEPTANCE_COORDINATE_TAG_PAIRS = frozenset(
    (f"{ACCEPTANCE_PACKAGE_NAME}@{version}", tag)
    for _base_coordinate, specs in _ACCEPTANCE_SUITE_PROFILES
    for _scenario, version, tag in specs
)


def fixed_acceptance_scenario_specs(
    base_package_coordinate: str,
) -> tuple[tuple[str, str, str], ...]:
    """Return one closed reviewed acceptance suite by exact base coordinate."""
    for fixed_coordinate, specs in _ACCEPTANCE_SUITE_PROFILES:
        if base_package_coordinate == fixed_coordinate:
            return specs
    message = "acceptance package coordinate is not a reviewed fixed suite"
    raise ValueError(message)


def fixed_acceptance_coordinates(
    base_package_coordinate: str,
) -> dict[str, str]:
    """Return exact scenario coordinates for one reviewed acceptance suite."""
    return {
        scenario: f"{ACCEPTANCE_PACKAGE_NAME}@{version}"
        for scenario, version, _tag in fixed_acceptance_scenario_specs(
            base_package_coordinate
        )
    }


def _fixed_acceptance_coordinate(
    *,
    scenario: str,
    tag: str,
) -> str:
    matches = {
        f"{ACCEPTANCE_PACKAGE_NAME}@{version}"
        for _base_coordinate, specs in _ACCEPTANCE_SUITE_PROFILES
        for fixed_scenario, version, fixed_tag in specs
        if scenario == fixed_scenario and tag == fixed_tag
    }
    if len(matches) != 1:
        message = "acceptance scenario and tag are not one reviewed fixed pair"
        raise ValueError(message)
    return matches.pop()


DEFAULT_TIMEOUT_SECONDS = 20
DEFAULT_METADATA_LIMIT_BYTES = 1_000_000
DEFAULT_TARBALL_LIMIT_BYTES = 25_000_000
DEFAULT_MAX_PAGES = 100
GITHUB_PAGE_SIZE = 100
PRIVATE_CONFIG_MODE = 0o600
PAIR_SIZE = 2
MAX_REDIRECTS = 5
HTTP_OK = 200
HTTP_CREATED = 201
_ACCEPTANCE_PUBLISH_SUCCESS_STATUSES = frozenset({HTTP_OK, HTTP_CREATED})
_HTTP_STATUS_MIN = 100
_HTTP_STATUS_MAX = 599
HTTP_MULTIPLE_CHOICES = 300
HTTP_PERMANENT_REDIRECT = 308
HTTP_UNAUTHORIZED = 401
HTTP_FORBIDDEN = 403
HTTP_NOT_FOUND = 404
HTTP_CONFLICT = 409
HTTP_TOO_MANY_REQUESTS = 429
HTTP_SERVER_ERROR = 500
_REDACTED = "******"
_TOKEN_PATTERNS = (
    re.compile(r"(?i)(authorization\s*:\s*)(?:bearer\s+)?\S+"),
    re.compile(r"(?i)(//npm\.pkg\.github\.com/:_authToken=)\S+"),
    re.compile(r"(?i)(npm_token\s*=\s*)\S+"),
)
_SELECTED_HEADERS = frozenset(
    {
        "cache-control",
        "content-length",
        "content-type",
        "etag",
        "last-modified",
        "link",
        "retry-after",
    }
)
_ALLOWED_TARBALL_HOSTS = frozenset(
    {
        "npm.pkg.github.com",
        "objects.githubusercontent.com",
        "github-registry-files.githubusercontent.com",
    }
)
_MISSING = object()
_CREDENTIAL_HOSTS = frozenset({"api.github.com", "npm.pkg.github.com"})


@dataclass(frozen=True, slots=True)
class GitHubPackagesHttpResponse:
    """Bounded response returned by an injectable authenticated GET seam."""

    status: int
    url: str
    headers: tuple[tuple[str, str], ...]
    body: bytes
    redirects: tuple[str, ...] = ()
    truncated: bool = False
    complete: bool = True

    def __post_init__(self) -> None:
        """Reject malformed transport facts before classification."""
        if type(self.status) is not int or self.status < 0:
            message = "HTTP response status must be a nonnegative exact integer"
            raise TypeError(message)
        if type(self.url) is not str or not self.url:
            message = "HTTP response URL must be a nonempty exact string"
            raise TypeError(message)
        if type(self.headers) is not tuple or any(
            type(item) is not tuple
            or len(item) != PAIR_SIZE
            or any(type(value) is not str for value in item)
            for item in self.headers
        ):
            message = "HTTP response headers must be exact string pairs"
            raise TypeError(message)
        if type(self.body) is not bytes:
            message = "HTTP response body must be exact bytes"
            raise TypeError(message)
        if type(self.redirects) is not tuple or any(
            type(value) is not str for value in self.redirects
        ):
            message = "HTTP response redirects must be exact strings"
            raise TypeError(message)
        if type(self.truncated) is not bool or type(self.complete) is not bool:
            message = "HTTP response completion facts must be exact Booleans"
            raise TypeError(message)


class GitHubPackagesTransport(Protocol):
    """Injectable bounded GitHub REST/npm registry GET transport."""

    def get(
        self,
        url: str,
        *,
        headers: tuple[tuple[str, str], ...],
        timeout: float,
        max_bytes: int,
    ) -> GitHubPackagesHttpResponse:
        """Fetch one bounded response without retaining credentials."""


class GitHubPackagesHttpTransport:
    """Concrete bounded HTTPS transport with manual safe redirects."""

    _REDIRECT_STATUSES = frozenset({301, 302, 303, 307, 308})

    def __init__(
        self,
        opener: Callable[
            [urllib.request.Request, float, int],
            GitHubPackagesHttpResponse,
        ]
        | None = None,
        *,
        max_redirects: int = MAX_REDIRECTS,
    ) -> None:
        self._opener = opener or self._open_once
        self._max_redirects = _positive_exact_int(
            max_redirects,
            field="max_redirects",
        )

    def _open_once(
        self,
        request: urllib.request.Request,
        timeout: float,
        max_bytes: int,
    ) -> GitHubPackagesHttpResponse:
        class _NoRedirect(urllib.request.HTTPRedirectHandler):
            def redirect_request(
                self,
                *_args: object,
                **_kwargs: object,
            ) -> None:
                return None

        opener = urllib.request.build_opener(_NoRedirect)
        try:
            with opener.open(request, timeout=timeout) as response:
                body = response.read(max_bytes + 1)
                return GitHubPackagesHttpResponse(
                    status=response.status,
                    url=response.geturl(),
                    headers=tuple(response.headers.items()),
                    body=body[:max_bytes],
                    truncated=len(body) > max_bytes,
                    complete=len(body) <= max_bytes,
                )
        except urllib.error.HTTPError as error:
            body = error.read(max_bytes + 1)
            return GitHubPackagesHttpResponse(
                status=error.code,
                url=error.geturl(),
                headers=tuple(error.headers.items()),
                body=body[:max_bytes],
                truncated=len(body) > max_bytes,
                complete=len(body) <= max_bytes,
            )
        except TimeoutError as error:
            raise GitHubPackagesTimeoutError(str(error)) from error
        except OSError as error:
            raise GitHubPackagesNetworkError(str(error)) from error

    def get(
        self,
        url: str,
        *,
        headers: tuple[tuple[str, str], ...],
        timeout: float,
        max_bytes: int,
    ) -> GitHubPackagesHttpResponse:
        """Fetch one bounded response after validating each credentialed hop."""
        _positive_exact_int(max_bytes, field="max_bytes")
        if type(timeout) not in {float, int} or timeout <= 0:
            message = "timeout must be positive"
            raise ValueError(message)
        if type(headers) is not tuple or any(
            type(pair) is not tuple
            or len(pair) != PAIR_SIZE
            or any(type(value) is not str for value in pair)
            for pair in headers
        ):
            message = "headers must be exact string pairs"
            raise TypeError(message)

        redirects: list[str] = []
        current_url = url
        current_headers = headers
        seen: set[str] = set()
        for _hop in range(self._max_redirects + 1):
            credentialed = any(
                name.lower() == "authorization" for name, _ in current_headers
            )
            _validate_transport_request(current_url, credentialed=credentialed)
            if current_url in seen:
                message = "redirect cycle rejected before credentialed request"
                raise GitHubPackagesPolicyError(message)
            seen.add(current_url)
            request = urllib.request.Request(  # noqa: S310
                current_url,
                headers=dict(current_headers),
                method="GET",
            )
            response = self._opener(request, float(timeout), max_bytes)
            if type(response) is not GitHubPackagesHttpResponse:
                message = "HTTP opener returned a malformed response"
                raise TypeError(message)
            if response.status not in self._REDIRECT_STATUSES:
                return GitHubPackagesHttpResponse(
                    status=response.status,
                    url=response.url,
                    headers=response.headers,
                    body=response.body,
                    redirects=tuple(redirects),
                    truncated=response.truncated,
                    complete=response.complete,
                )
            location = _header(response, "location")
            if location is None:
                message = "redirect location is missing"
                raise GitHubPackagesPolicyError(message)
            target_url = urllib.parse.urljoin(current_url, location)
            if len(redirects) >= self._max_redirects:
                message = "redirect limit exceeded before credentialed request"
                raise GitHubPackagesPolicyError(message)
            target_headers = redirect_headers(
                source_url=current_url,
                target_url=target_url,
                headers=current_headers,
            )
            if any(
                name.lower() == "authorization" for name, _ in target_headers
            ):
                _validate_transport_request(target_url, credentialed=True)
            redirects.append(target_url)
            current_url = target_url
            current_headers = target_headers
        message = "redirect limit exceeded"
        raise GitHubPackagesPolicyError(message)


@dataclass(frozen=True, slots=True)
class PublishCommandResult:
    """Sanitized result of the one permitted npm publish process."""

    outcome: str
    exit_code: int | None
    stdout: str
    stderr: str
    command: tuple[str, ...]


class PublishRunner(Protocol):
    """Injectable npm publish process seam."""

    def run(
        self,
        argv: tuple[str, ...],
        *,
        env: dict[str, str],
    ) -> object:
        """Execute one exact process and return bounded result facts."""


@dataclass(frozen=True, slots=True)
class PublishClassification:
    """Pure first-slice mutation classification."""

    outcome: str
    mutation_disposition: str
    receipt_digest: str | None


@dataclass(frozen=True, slots=True)
class ProbeClassification:
    """Small pure-classification result used by focused scenario tests."""

    value: ObservationValue

    def to_document(self) -> dict[str, JsonValue]:
        """Return a closed document without credentials."""
        return {
            "schema": "workflow-delivery/v3/github-packages-probe",
            "value": self.value.to_document(),
        }


@dataclass(frozen=True, slots=True, eq=False)
class ValidatedAcceptanceRequestProof:
    """Immutable proof of the exact admitted request and upstream response."""

    request_digest: str
    tarball_sha512: str
    package_coordinate: str
    tag: str
    upstream_status: int
    selected_headers: tuple[tuple[str, str], ...]
    response_body_digest: str
    response_identity_digest: str
    _raw_request: bytes = dataclass_field(repr=False, compare=False)
    _tarball: bytes = dataclass_field(repr=False, compare=False)
    _response_body: bytes = dataclass_field(repr=False, compare=False)

    def __post_init__(self) -> None:
        """Reject any substitution after proof formation."""
        expected_request = (
            "sha256:" + hashlib.sha256(self._raw_request).hexdigest()
        )
        expected_tarball = "sha512:" + hashlib.sha512(self._tarball).hexdigest()
        if self.request_digest != expected_request:
            message = "validated request digest is not exact"
            raise ValueError(message)
        if self.tarball_sha512 != expected_tarball:
            message = "validated tarball digest is not exact"
            raise ValueError(message)
        if (self.package_coordinate, self.tag) not in (
            _ACCEPTANCE_COORDINATE_TAG_PAIRS
        ):
            message = "validated request coordinate or tag is not fixed"
            raise ValueError(message)
        if (
            type(self.upstream_status) is not int
            or self.upstream_status not in _ACCEPTANCE_PUBLISH_SUCCESS_STATUSES
        ):
            message = (
                "validated upstream status is not an accepted npm publish "
                "status"
            )
            raise ValueError(message)
        if (
            type(self.selected_headers) is not tuple
            or tuple(sorted(self.selected_headers)) != self.selected_headers
            or any(
                type(item) is not tuple
                or len(item) != PAIR_SIZE
                or type(item[0]) is not str
                or type(item[1]) is not str
                or item[0] != item[0].lower()
                or item[0] not in {"content-type", "etag", "retry-after"}
                for item in self.selected_headers
            )
        ):
            message = "validated selected headers are not exact"
            raise ValueError(message)
        expected_response_body = self._sha256(self._response_body)
        if self.response_body_digest != expected_response_body:
            message = "validated response body digest is not exact"
            raise ValueError(message)
        if self.response_identity_digest != canonical_sha256(
            cast(
                "JsonValue",
                {
                    "request-digest": self.request_digest,
                    "upstream-status": self.upstream_status,
                    "selected-headers": dict(self.selected_headers),
                    "response-body-digest": self.response_body_digest,
                },
            )
        ):
            message = "validated response identity digest is not exact"
            raise ValueError(message)

    @staticmethod
    def _sha256(value: bytes) -> str:
        return "sha256:" + hashlib.sha256(value).hexdigest()

    @classmethod
    def from_validated_exchange(  # noqa: PLR0913
        cls,
        *,
        raw_request: bytes,
        tarball: bytes,
        package_coordinate: str,
        tag: str,
        upstream_status: int,
        selected_headers: dict[str, str],
        response_body: bytes,
    ) -> ValidatedAcceptanceRequestProof:
        """Form proof only from exact bytes admitted at the proxy boundary."""
        normalized_headers = tuple(
            sorted(
                (name.lower(), value)
                for name, value in selected_headers.items()
            )
        )
        request_digest = cls._sha256(raw_request)
        response_body_digest = cls._sha256(response_body)
        identity_facts = cast(
            "JsonValue",
            {
                "request-digest": request_digest,
                "upstream-status": upstream_status,
                "selected-headers": dict(normalized_headers),
                "response-body-digest": response_body_digest,
            },
        )
        return cls(
            request_digest=request_digest,
            tarball_sha512=("sha512:" + hashlib.sha512(tarball).hexdigest()),
            package_coordinate=package_coordinate,
            tag=tag,
            upstream_status=upstream_status,
            selected_headers=normalized_headers,
            response_body_digest=response_body_digest,
            response_identity_digest=canonical_sha256(identity_facts),
            _raw_request=raw_request,
            _tarball=tarball,
            _response_body=response_body,
        )

    @classmethod
    def from_closed_document(
        cls,
        document: dict[str, JsonValue],
        *,
        package_coordinate: str,
        tag: str,
        response_identity_digest: str,
    ) -> ValidatedAcceptanceRequestProof:
        """Rehydrate a credential-free proof document."""
        if type(document) is not dict or set(document) != {
            "schema",
            "request-digest",
            "tarball-sha512",
            "package-coordinate",
            "tag",
            "upstream-status",
            "selected-headers",
            "response-body-digest",
            "response-identity-digest",
        }:
            message = "validated-request-proof has unknown or missing fields"
            raise ValueError(message)
        if (
            document["schema"]
            != "workflow-delivery/v3/validated-acceptance-request-proof"
        ):
            message = "validated-request-proof schema is not exact"
            raise ValueError(message)
        selected_headers = document["selected-headers"]
        if (
            type(document["request-digest"]) is not str
            or re.fullmatch(r"sha256:[0-9a-f]{64}", document["request-digest"])
            is None
            or type(document["tarball-sha512"]) is not str
            or re.fullmatch(r"sha512:[0-9a-f]{128}", document["tarball-sha512"])
            is None
            or type(document["response-body-digest"]) is not str
            or re.fullmatch(
                r"sha256:[0-9a-f]{64}",
                document["response-body-digest"],
            )
            is None
            or type(document["response-identity-digest"]) is not str
            or re.fullmatch(
                r"sha256:[0-9a-f]{64}",
                document["response-identity-digest"],
            )
            is None
        ):
            message = "validated-request-proof digests are malformed"
            raise ValueError(message)
        if document["package-coordinate"] != package_coordinate:
            message = "validated-request-proof package-coordinate is not exact"
            raise ValueError(message)
        if document["tag"] != tag:
            message = "validated-request-proof tag is not exact"
            raise ValueError(message)
        if (
            type(document["upstream-status"]) is not int
            or document["upstream-status"]
            not in _ACCEPTANCE_PUBLISH_SUCCESS_STATUSES
        ):
            message = "validated-request-proof upstream-status is not exact"
            raise ValueError(message)
        if type(selected_headers) is not dict or any(
            type(name) is not str
            or type(value) is not str
            or name != name.lower()
            or name not in {"content-type", "etag", "retry-after"}
            for name, value in selected_headers.items()
        ):
            message = "validated-request-proof selected-headers are malformed"
            raise ValueError(message)
        expected_identity = canonical_sha256(
            cast(
                "JsonValue",
                {
                    "request-digest": document["request-digest"],
                    "upstream-status": document["upstream-status"],
                    "selected-headers": selected_headers,
                    "response-body-digest": document["response-body-digest"],
                },
            )
        )
        if document["response-identity-digest"] != expected_identity:
            message = (
                "validated-request-proof response-identity-digest is not exact"
            )
            raise ValueError(message)
        if response_identity_digest != expected_identity:
            message = "validated-request-proof does not match response identity"
            raise ValueError(message)
        proof = cls.__new__(cls)
        object.__setattr__(proof, "request_digest", document["request-digest"])
        object.__setattr__(proof, "tarball_sha512", document["tarball-sha512"])
        object.__setattr__(proof, "package_coordinate", package_coordinate)
        object.__setattr__(proof, "tag", tag)
        object.__setattr__(
            proof,
            "upstream_status",
            document["upstream-status"],
        )
        object.__setattr__(
            proof,
            "selected_headers",
            tuple(sorted(cast("dict[str, str]", selected_headers).items())),
        )
        object.__setattr__(
            proof,
            "response_body_digest",
            document["response-body-digest"],
        )
        object.__setattr__(
            proof,
            "response_identity_digest",
            document["response-identity-digest"],
        )
        object.__setattr__(proof, "_raw_request", b"")
        object.__setattr__(proof, "_tarball", b"")
        object.__setattr__(proof, "_response_body", b"")
        return proof

    def to_document(self) -> dict[str, JsonValue]:
        """Return the credential-free closed proof document."""
        return cast(
            "dict[str, JsonValue]",
            {
                "schema": (
                    "workflow-delivery/v3/validated-acceptance-request-proof"
                ),
                "request-digest": self.request_digest,
                "tarball-sha512": self.tarball_sha512,
                "package-coordinate": self.package_coordinate,
                "tag": self.tag,
                "upstream-status": self.upstream_status,
                "selected-headers": dict(self.selected_headers),
                "response-body-digest": self.response_body_digest,
                "response-identity-digest": self.response_identity_digest,
            },
        )

    def __eq__(self, other: object) -> bool:
        """Compare proofs, retaining the legacy diagnostic-dict assertion."""
        if type(other) is ValidatedAcceptanceRequestProof:
            return self.to_document() == other.to_document()
        if type(other) is dict:
            return other == {
                "outcome": "lost-response-processed",
                "request-digest": self.request_digest,
                "upstream-status": self.upstream_status,
                "selected-headers": dict(self.selected_headers),
                "response-body-digest": self.response_body_digest,
                "response-identity-digest": self.response_identity_digest,
            }
        return NotImplemented

    def __hash__(self) -> int:
        """Hash the closed public proof facts."""
        return hash(
            (
                self.request_digest,
                self.tarball_sha512,
                self.package_coordinate,
                self.tag,
                self.upstream_status,
                self.selected_headers,
                self.response_body_digest,
                self.response_identity_digest,
            )
        )


_ACCEPTANCE_RUNNER_EXIT_CLASSIFICATIONS = frozenset(
    {
        "protocol-confirmed",
        "runner-failed-before-mutation",
        "runner-failed-after-action-start",
        "runner-failed-after-mutation-start",
        "runner-malformed-before-mutation",
    }
)
_ACCEPTANCE_LOCAL_RUNNER_EXCEPTION_CATEGORIES = frozenset(
    {"TimeoutError", "OSError", "RuntimeError", "ValueError"}
)
_ACCEPTANCE_UPSTREAM_TRANSPORT_CATEGORIES = frozenset(
    {"TimeoutError", "OSError", "HTTPException"}
)
_ACCEPTANCE_RUNNER_EXCEPTION_CATEGORIES = (
    _ACCEPTANCE_LOCAL_RUNNER_EXCEPTION_CATEGORIES
    | _ACCEPTANCE_UPSTREAM_TRANSPORT_CATEGORIES
)
_ACCEPTANCE_UPSTREAM_DIAGNOSTIC_FIELDS = frozenset(
    {
        "upstream-status",
        "exception-category",
        "request-correlation-digest",
    }
)
_SHA256_DIGEST_PATTERN = re.compile(r"sha256:[0-9a-f]{64}\Z")


def _validate_acceptance_runner_diagnostic_shape(
    *,
    exit_classification: str,
    upstream_status: int | None,
    exception_category: str | None,
    request_correlation_digest: str | None,
) -> None:
    if (
        upstream_status is None
        and exception_category is None
        and request_correlation_digest is None
    ):
        message = (
            "acceptance runner diagnostic does not contain a diagnostic arm"
        )
        raise ValueError(message)
    if exit_classification == "protocol-confirmed":
        if (
            upstream_status not in _ACCEPTANCE_PUBLISH_SUCCESS_STATUSES
            or exception_category is not None
            or request_correlation_digest is None
        ):
            message = "protocol-confirmed runner diagnostic facts contradict"
            raise ValueError(message)
        return
    request_bound = request_correlation_digest is not None
    if upstream_status is not None and exception_category is not None:
        message = "acceptance runner diagnostic mixes status and exception"
        raise ValueError(message)
    if (
        not request_bound
        and upstream_status is not None
        and upstream_status != HTTP_CREATED
    ):
        message = "unbound runner diagnostic status is not historical"
        raise ValueError(message)
    if (
        request_bound
        and upstream_status is None
        and exception_category not in _ACCEPTANCE_UPSTREAM_TRANSPORT_CATEGORIES
    ):
        message = "request-bound runner diagnostic category is not transport"
        raise ValueError(message)
    if request_bound and exit_classification != (
        "runner-failed-after-mutation-start"
    ):
        message = (
            "request-bound runner diagnostic contradicts exit classification"
        )
        raise ValueError(message)
    if not request_bound and exception_category == "HTTPException":
        message = "upstream HTTPException diagnostic requires request binding"
        raise ValueError(message)


@dataclass(frozen=True, slots=True)
class AcceptanceRunnerDiagnostic:
    """Closed credential-free facts about one acceptance runner exit."""

    exit_classification: str
    upstream_status: int | None
    exception_category: str | None
    request_correlation_digest: str | None

    def __post_init__(self) -> None:
        """Reject unbounded or contradictory diagnostic facts."""
        if (
            type(self.exit_classification) is not str
            or self.exit_classification
            not in _ACCEPTANCE_RUNNER_EXIT_CLASSIFICATIONS
        ):
            message = "acceptance runner exit classification is not closed"
            raise ValueError(message)
        if self.upstream_status is not None and (
            type(self.upstream_status) is not int
            or not _HTTP_STATUS_MIN <= self.upstream_status <= _HTTP_STATUS_MAX
        ):
            message = "acceptance runner upstream status is not closed"
            raise ValueError(message)
        if self.exception_category is not None and (
            type(self.exception_category) is not str
            or self.exception_category
            not in _ACCEPTANCE_RUNNER_EXCEPTION_CATEGORIES
        ):
            message = "acceptance runner exception category is not closed"
            raise ValueError(message)
        if self.request_correlation_digest is not None and (
            type(self.request_correlation_digest) is not str
            or _SHA256_DIGEST_PATTERN.fullmatch(self.request_correlation_digest)
            is None
        ):
            message = "acceptance runner request correlation is malformed"
            raise ValueError(message)
        _validate_acceptance_runner_diagnostic_shape(
            exit_classification=self.exit_classification,
            upstream_status=self.upstream_status,
            exception_category=self.exception_category,
            request_correlation_digest=self.request_correlation_digest,
        )

    def to_document(self) -> dict[str, JsonValue]:
        """Return the exact closed diagnostic document."""
        return cast(
            "dict[str, JsonValue]",
            {
                "exit-classification": self.exit_classification,
                "upstream-status": self.upstream_status,
                "exception-category": self.exception_category,
                "request-correlation-digest": (self.request_correlation_digest),
            },
        )


@dataclass(frozen=True, slots=True)
class FixedCoordinateAcceptanceProbeResult:
    """Closed diagnostic facts for one temporary acceptance probe."""

    scenario: str
    package_coordinate: str
    tag: str
    pre_state: str
    post_state: str
    result: str
    mutation_classification: str
    action_executed: bool
    mutation_started: bool
    response_identity_digest: str
    content_sha512: str | None
    diagnostics: tuple[str, ...]
    validated_request_proof: ValidatedAcceptanceRequestProof | None = None
    runner_diagnostic: AcceptanceRunnerDiagnostic | None = None

    def to_document(self) -> dict[str, JsonValue]:
        """Return the immutable acceptance-only probe document."""
        document = cast(
            "dict[str, JsonValue]",
            {
                "schema": (
                    "workflow-delivery/v3/fixed-coordinate-acceptance-probe"
                ),
                "scenario": self.scenario,
                "package-coordinate": self.package_coordinate,
                "tag": self.tag,
                "pre-state": self.pre_state,
                "post-state": self.post_state,
                "result": self.result,
                "mutation-classification": self.mutation_classification,
                "action-executed": self.action_executed,
                "mutation-started": self.mutation_started,
                "response-identity-digest": self.response_identity_digest,
                "content-sha512": self.content_sha512,
                "diagnostics": list(self.diagnostics),
            },
        )
        if self.validated_request_proof is not None:
            document["validated-request-proof"] = (
                self.validated_request_proof.to_document()
            )
        if self.runner_diagnostic is not None:
            document["runner-diagnostic"] = self.runner_diagnostic.to_document()
        return document


@dataclass(frozen=True, slots=True)
class FixedAcceptanceSuiteResult:
    """Canonical fixed-scenario suite evidence emitted by one probe job."""

    suite: str
    scenarios: tuple[FixedCoordinateAcceptanceProbeResult, ...]

    @property
    def scenario_inventory(self) -> tuple[str, ...]:
        """Return the exact ordered scenario inventory."""
        return tuple(result.scenario for result in self.scenarios)

    @property
    def mutation_classification(self) -> str:
        """Aggregate once with unknown > incomplete > complete precedence."""
        classifications = {
            result.mutation_classification for result in self.scenarios
        }
        if "unknown" in classifications:
            return "unknown"
        if "incomplete" in classifications:
            return "incomplete"
        return "complete"

    @property
    def result(self) -> str:
        """Return the workflow-facing terminal result."""
        return (
            "success"
            if self.mutation_classification == "complete"
            else self.mutation_classification
        )

    def to_document(self) -> dict[str, JsonValue]:
        """Return the closed canonical suite record."""
        scenario_documents: list[JsonValue] = []
        for scenario in self.scenarios:
            scenario_documents.append(
                {
                    "scenario": scenario.scenario,
                    "package-coordinate": scenario.package_coordinate,
                    "tag": scenario.tag,
                    "mutation-classification": (
                        scenario.mutation_classification
                    ),
                    "pre": {"state": scenario.pre_state},
                    "action": {
                        "operation": "npm-publish-create-only",
                        "executed": scenario.action_executed,
                        "mutation-started": scenario.mutation_started,
                    },
                    "response": {
                        "result": scenario.result,
                        "identity-digest": scenario.response_identity_digest,
                        "diagnostics": list(scenario.diagnostics),
                    },
                    "post": {
                        "state": scenario.post_state,
                        "content-sha512": scenario.content_sha512,
                    },
                    **(
                        {
                            "validated-request-proof": (
                                scenario.validated_request_proof.to_document()
                            )
                        }
                        if scenario.validated_request_proof is not None
                        else {}
                    ),
                    **(
                        {
                            "runner-diagnostic": (
                                scenario.runner_diagnostic.to_document()
                            )
                        }
                        if scenario.runner_diagnostic is not None
                        else {}
                    ),
                }
            )
        document = cast(
            "dict[str, JsonValue]",
            {
                "schema": "workflow-delivery/v3/fixed-acceptance-suite",
                "suite": self.suite,
                "scenario-inventory": list(self.scenario_inventory),
                "scenarios": scenario_documents,
                "mutation-classification": self.mutation_classification,
                "result": self.result,
            },
        )
        document["record-digest"] = canonical_sha256(document)
        return document


@dataclass(frozen=True, slots=True)
class PublicationExecutionResult:
    """Complete current-Attempt publication result."""

    command: PublishCommandResult
    observation: ProjectionObservation | None
    action_result: ActionResult


@dataclass(frozen=True, slots=True)
class DeferredPublicationExecutionResult:
    """Publication facts awaiting immutable Receipt transport binding."""

    command: PublishCommandResult
    observation: ProjectionObservation | None
    classification: PublishClassification
    response_identity_digest: str | None
    diagnostic_reference: str | None
    receipt: Receipt | None


class PublisherGovernanceRecheckRejectionError(Exception):
    """Typed terminal rejection after marker admission but before npm."""

    def __init__(self, result: DeferredPublicationExecutionResult) -> None:
        """Retain the exact closed terminal publication result."""
        if (
            type(result) is not DeferredPublicationExecutionResult
            or result.classification.outcome != "failed"
            or result.classification.mutation_disposition != "no-side-effect"
            or result.observation is not None
            or result.response_identity_digest is not None
            or result.receipt is not None
            or result.diagnostic_reference
            != PUBLISHER_GOVERNANCE_RECHECK_FAILED_BEFORE_RUNNER
        ):
            message = "Publisher Governance rejection result is malformed"
            raise ValueError(message)
        self.result = result
        super().__init__(PUBLISHER_GOVERNANCE_RECHECK_FAILED_BEFORE_RUNNER)


@dataclass(frozen=True, slots=True)
class GitHubPackagesPublishPreflight:
    """Immutable authority, bytes, and npm-configuration admission."""

    attempt: ReleaseAttemptIdentity
    publication_snapshot_digest: str
    action_digest: str
    lock_group: str
    tarball_sha256: str
    tarball_sha512: str
    npm_configuration_digest: str
    governance_provenance: tuple[tuple[str, str], ...]
    governance_content_sha256: str
    governance_expires_at: str
    governance_live_enabled: bool

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical preflight document."""
        return {
            "schema": "workflow-delivery/v3/github-packages-publish-preflight",
            "attempt": self.attempt.to_document(),
            "publication-snapshot-digest": self.publication_snapshot_digest,
            "action-digest": self.action_digest,
            "lock-group": self.lock_group,
            "tarball-sha256": self.tarball_sha256,
            "tarball-sha512": self.tarball_sha512,
            "npm-configuration-digest": self.npm_configuration_digest,
            "governance-provenance": [
                [name, value] for name, value in self.governance_provenance
            ],
            "governance-content-sha256": self.governance_content_sha256,
            "governance-expires-at": self.governance_expires_at,
            "governance-live-enabled": self.governance_live_enabled,
        }

    @property
    def preflight_digest(self) -> str:
        """Return the canonical preflight digest."""
        return canonical_sha256(self.to_document())


@dataclass(frozen=True, slots=True)
class MutationMayHaveStartedMarker:
    """Durable action-bound boundary immediately before npm invocation."""

    attempt: ReleaseAttemptIdentity
    publication_snapshot_digest: str
    action_digest: str
    lock_group: str
    preflight_digest: str

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical mutation-start marker."""
        return {
            "schema": (
                "workflow-delivery/v3/github-packages-mutation-may-have-started"
            ),
            "attempt": self.attempt.to_document(),
            "publication-snapshot-digest": self.publication_snapshot_digest,
            "action-digest": self.action_digest,
            "lock-group": self.lock_group,
            "preflight-digest": self.preflight_digest,
        }

    @property
    def marker_digest(self) -> str:
        """Return the canonical marker digest."""
        return canonical_sha256(self.to_document())


@dataclass(frozen=True, slots=True)
class _Exchange:
    stage: str
    requested_url: str
    final_url: str | None
    redirects: tuple[str, ...]
    status: int | str
    selected_headers: tuple[tuple[str, str], ...]
    truncated: bool | None
    complete: bool | None
    body_sha256: str | None
    detail: str | None = None

    def to_document(self) -> dict[str, JsonValue]:
        return cast(
            "dict[str, JsonValue]",
            {
                "stage": self.stage,
                "requested-url": self.requested_url,
                "final-url": self.final_url,
                "redirects": list(self.redirects),
                "status": self.status,
                "selected-headers": [
                    [name, value] for name, value in self.selected_headers
                ],
                "truncated": self.truncated,
                "complete": self.complete,
                "body-sha256": self.body_sha256,
                "detail": self.detail,
            },
        )


@dataclass(frozen=True, slots=True)
class _RemoteFacts:
    rest_version: str | None = None
    npm_version: str | None = None
    owner: str | None = None
    repository: str | None = None
    tag_version: str | None = None
    target_tag_present: bool = False
    content_sha512: str | None = None
    witness_digest: str | None = None


class GitHubPackagesNetworkError(RuntimeError):
    """A bounded transport failed before a complete response."""


class GitHubPackagesTimeoutError(GitHubPackagesNetworkError):
    """A bounded transport timed out."""


class GitHubPackagesPolicyError(RuntimeError):
    """A response violated the approved HTTPS origin policy."""


def _acceptance_observation(
    value: object,
    *,
    tag: str,
    desired_sha512: str,
) -> tuple[str, str, str]:
    if type(value) is not dict:
        message = "acceptance observation must be a closed object"
        raise ValueError(message)
    observation = cast("dict[str, object]", value)
    state = observation.get("state")
    response = observation.get("response-identity-digest")
    if state not in {"absent", "exact", "conflicting", "unknown"}:
        message = "acceptance observation state is unsupported"
        raise ValueError(message)
    if (
        type(response) is not str
        or re.fullmatch(r"sha256:[0-9a-f]{64}", response) is None
    ):
        message = "acceptance response identity digest is malformed"
        raise ValueError(message)
    content = observation.get("content-sha512")
    if state == "absent":
        return "absent", response, desired_sha512
    if state == "unknown":
        return "unknown", response, desired_sha512
    if (
        type(content) is not str
        or re.fullmatch(r"sha512:[0-9a-f]{128}", content) is None
    ):
        message = "acceptance observation content SHA-512 is malformed"
        raise ValueError(message)
    observed_tag = observation.get("tag")
    normalized_state = cast("str", state)
    if state == "exact" and (observed_tag != tag or content != desired_sha512):
        normalized_state = "conflicting"
    return normalized_state, response, content


def inspect_fixed_acceptance_tarball(  # noqa: PLR0913
    tarball: bytes,
    *,
    package_coordinate: str,
    tag: str,
    observed_version: str,
    observed_tag_version: str,
    target_sha: str,
) -> dict[str, object]:
    """Hash and strictly inspect exact downloaded acceptance tarball bytes."""
    expected_version = package_coordinate.rsplit("@", 1)[1]
    if (
        observed_version != expected_version
        or observed_tag_version != expected_version
    ):
        message = "acceptance metadata version or tag binding mismatch"
        raise ValueError(message)
    entries = _read_tarball(
        tarball,
        max_payload_bytes=DEFAULT_EXPANDED_TARBALL_LIMIT_BYTES,
    )
    if set(entries) != {
        "package/package.json",
        "package/index.js",
        ACCEPTANCE_WITNESS_PATH,
    }:
        message = "acceptance tarball entry closure mismatch"
        raise ValueError(message)
    manifest = parse_json_strict(entries["package/package.json"])
    if type(manifest) is not dict:
        message = "acceptance package manifest must be an object"
        raise TypeError(message)
    repository = manifest.get("repository")
    if manifest.get("name") != ACCEPTANCE_PACKAGE_NAME:
        message = "acceptance package owner/name mismatch"
        raise ValueError(message)
    if manifest.get("version") != expected_version:
        message = "acceptance package version mismatch"
        raise ValueError(message)
    if repository != {"type": "git", "url": ACCEPTANCE_REPOSITORY_URL}:
        message = "acceptance package repository association mismatch"
        raise ValueError(message)
    witness = parse_json_strict(entries[ACCEPTANCE_WITNESS_PATH])
    if witness != {
        "purpose": "destination-acceptance",
        "target-sha": target_sha,
    }:
        message = "acceptance witness target or purpose mismatch"
        raise ValueError(message)
    return {
        "state": "exact",
        "version": observed_version,
        "tag": tag,
        "content-sha512": (f"sha512:{hashlib.sha512(tarball).hexdigest()}"),
        "repository": GITHUB_PACKAGES_REPOSITORY,
        "owner": GITHUB_PACKAGES_OWNER,
    }


def _acceptance_result(  # noqa: PLR0913
    *,
    scenario: str,
    tag: str,
    pre_state: str,
    post_state: str,
    result: str,
    mutation_classification: str,
    action_executed: bool,
    mutation_started: bool,
    response_identity_digest: str,
    content_sha512: str | None,
    diagnostics: tuple[str, ...] = (),
    validated_request_proof: ValidatedAcceptanceRequestProof | None = None,
    runner_diagnostic: AcceptanceRunnerDiagnostic | None = None,
) -> FixedCoordinateAcceptanceProbeResult:
    return FixedCoordinateAcceptanceProbeResult(
        scenario=scenario,
        package_coordinate=_fixed_acceptance_coordinate(
            scenario=scenario,
            tag=tag,
        ),
        tag=tag,
        pre_state=pre_state,
        post_state=post_state,
        result=result,
        mutation_classification=mutation_classification,
        action_executed=action_executed,
        mutation_started=mutation_started,
        response_identity_digest=response_identity_digest,
        content_sha512=content_sha512,
        diagnostics=diagnostics,
        validated_request_proof=validated_request_proof,
        runner_diagnostic=runner_diagnostic,
    )


def _runner_failure_result(
    *,
    action_executed: bool,
    mutation_started: bool,
    malformed_outcome: bool = False,
) -> str:
    if mutation_started:
        return "runner-failed-after-mutation-start"
    if action_executed:
        return "runner-failed-after-action-start"
    if malformed_outcome:
        return "runner-malformed-before-mutation"
    return "runner-failed-before-mutation"


def _runner_failure_diagnostics(
    *,
    action_executed: bool,
    mutation_started: bool,
) -> tuple[str, ...]:
    if action_executed or mutation_started:
        return ("runner-did-not-prove-controlled-outcome",)
    return ("runner-did-not-prove-mutation-start",)


def _remaining_acceptance_time(deadline: float) -> float:
    remaining = round(deadline - monotonic(), 3)
    if remaining <= 0:
        message = "acceptance operation deadline expired"
        raise TimeoutError(message)
    return remaining


def _call_with_acceptance_deadline(
    function: object,
    *args: object,
    deadline: float,
    **kwargs: object,
) -> object:
    """Pass the shared deadline only to deadline-aware injected seams."""
    callable_function = cast("Callable[..., object]", function)
    if _accepts_acceptance_deadline(callable_function):
        kwargs["deadline"] = deadline
    return callable_function(*args, **kwargs)


def _accepts_acceptance_deadline(function: object) -> bool:
    parameters = inspect.signature(
        cast("Callable[..., object]", function)
    ).parameters.values()
    return any(
        parameter.name == "deadline"
        or parameter.kind is inspect.Parameter.VAR_KEYWORD
        for parameter in parameters
    )


def _valid_lost_response_proof(
    document: dict[str, object],
    *,
    tarball: bytes,
    version: str,
    tag: str,
) -> bool:
    proof = document.get("validated-request-proof")
    return (
        document.get("outcome") == "lost-response-processed"
        and type(proof) is ValidatedAcceptanceRequestProof
        and proof.tarball_sha512
        == "sha512:" + hashlib.sha512(tarball).hexdigest()
        and proof.package_coordinate == f"{ACCEPTANCE_PACKAGE_NAME}@{version}"
        and proof.tag == tag
        and proof.upstream_status in _ACCEPTANCE_PUBLISH_SUCCESS_STATUSES
        and document.get("request-digest") == proof.request_digest
        and document.get("upstream-status") == proof.upstream_status
        and document.get("selected-headers") == dict(proof.selected_headers)
        and document.get("response-body-digest") == proof.response_body_digest
        and document.get("response-identity-digest")
        == proof.response_identity_digest
    )


def _valid_protocol_confirmed_proof(
    document: dict[str, object],
    *,
    tarball: bytes,
    package_coordinate: str,
    tag: str,
) -> bool:
    proof = document.get("validated-request-proof")
    return (
        document.get("outcome") == "protocol-confirmed"
        and type(proof) is ValidatedAcceptanceRequestProof
        and proof.tarball_sha512
        == "sha512:" + hashlib.sha512(tarball).hexdigest()
        and proof.package_coordinate == package_coordinate
        and proof.tag == tag
        and proof.upstream_status in _ACCEPTANCE_PUBLISH_SUCCESS_STATUSES
        and document.get("request-digest") == proof.request_digest
        and document.get("upstream-status") == proof.upstream_status
        and document.get("selected-headers") == dict(proof.selected_headers)
        and document.get("response-body-digest") == proof.response_body_digest
        and document.get("response-identity-digest")
        == proof.response_identity_digest
    )


def _admit_acceptance_upstream_diagnostic(
    value: object,
    *,
    exit_classification: str,
) -> AcceptanceRunnerDiagnostic:
    if type(value) is not dict or set(value) != (
        _ACCEPTANCE_UPSTREAM_DIAGNOSTIC_FIELDS
    ):
        message = "acceptance upstream diagnostic is not exact"
        raise ValueError(message)
    if value["request-correlation-digest"] is None:
        message = "acceptance upstream diagnostic is not request-bound"
        raise ValueError(message)
    return AcceptanceRunnerDiagnostic(
        exit_classification=exit_classification,
        upstream_status=cast("int | None", value["upstream-status"]),
        exception_category=cast("str | None", value["exception-category"]),
        request_correlation_digest=cast(
            "str | None",
            value["request-correlation-digest"],
        ),
    )


def _try_acceptance_upstream_diagnostic(
    value: object,
    *,
    exit_classification: str,
    local_fallback_available: bool,
) -> AcceptanceRunnerDiagnostic | None:
    try:
        return _admit_acceptance_upstream_diagnostic(
            value,
            exit_classification=exit_classification,
        )
    except ValueError:
        if not local_fallback_available:
            raise
        return None


def _local_runner_exception_category(error: Exception) -> str:
    if isinstance(error, TimeoutError):
        return "TimeoutError"
    if isinstance(error, OSError):
        return "OSError"
    if isinstance(error, RuntimeError):
        return "RuntimeError"
    return "ValueError"


def _validate_unadmitted_returned_diagnostic(
    document: dict[str, object],
) -> None:
    upstream_diagnostic = document.get("upstream-diagnostic", _MISSING)
    if upstream_diagnostic is _MISSING:
        return
    _admit_acceptance_upstream_diagnostic(
        upstream_diagnostic,
        exit_classification="runner-malformed-before-mutation",
    )


def _require_runner_diagnostic_proof_binding(
    diagnostic: AcceptanceRunnerDiagnostic | None,
    proof: ValidatedAcceptanceRequestProof,
) -> None:
    if diagnostic is None:
        return
    if (
        diagnostic.exception_category is not None
        or diagnostic.upstream_status != proof.upstream_status
        or diagnostic.request_correlation_digest != proof.request_digest
    ):
        message = (
            "acceptance runner diagnostic does not bind validated request proof"
        )
        raise ValueError(message)


def _acceptance_runner_diagnostic(  # noqa: PLR0913
    document: dict[str, object],
    *,
    runner_error: Exception | None,
    action_executed: bool,
    mutation_started: bool,
    exception_startedness_admitted: bool,
    protocol_confirmed: bool,
) -> AcceptanceRunnerDiagnostic | None:
    if runner_error is not None and not exception_startedness_admitted:
        _validate_unadmitted_returned_diagnostic(document)
        return None
    if protocol_confirmed:
        proof = document.get("validated-request-proof")
        accepted_proof = cast("ValidatedAcceptanceRequestProof", proof)
        upstream_diagnostic = document.get("upstream-diagnostic", _MISSING)
        if upstream_diagnostic is not _MISSING:
            diagnostic = _admit_acceptance_upstream_diagnostic(
                upstream_diagnostic,
                exit_classification="protocol-confirmed",
            )
            _require_runner_diagnostic_proof_binding(
                diagnostic,
                accepted_proof,
            )
            return diagnostic
        return AcceptanceRunnerDiagnostic(
            exit_classification="protocol-confirmed",
            upstream_status=accepted_proof.upstream_status,
            exception_category=None,
            request_correlation_digest=accepted_proof.request_digest,
        )
    exit_classification = _runner_failure_result(
        action_executed=action_executed,
        mutation_started=mutation_started,
        malformed_outcome=(
            isinstance(runner_error, ValueError)
            and not action_executed
            and not mutation_started
        ),
    )
    upstream_diagnostic = (
        getattr(runner_error, "upstream_diagnostic", _MISSING)
        if runner_error is not None
        else _MISSING
    )
    if upstream_diagnostic is _MISSING:
        upstream_diagnostic = document.get("upstream-diagnostic", _MISSING)
    if upstream_diagnostic is not _MISSING:
        diagnostic = _try_acceptance_upstream_diagnostic(
            upstream_diagnostic,
            exit_classification=exit_classification,
            local_fallback_available=runner_error is not None,
        )
        if diagnostic is not None:
            return diagnostic
    if runner_error is not None:
        return AcceptanceRunnerDiagnostic(
            exit_classification=exit_classification,
            upstream_status=None,
            exception_category=_local_runner_exception_category(runner_error),
            request_correlation_digest=None,
        )
    return None


def run_fixed_coordinate_acceptance_probe(  # noqa: C901, PLR0911, PLR0912, PLR0913, PLR0915
    *,
    scenario: str,
    package_coordinate: str,
    tag: str,
    tarball: Path,
    tarball_sha512: str,
    transport: object,
    runner: object,
    timeout_seconds: float,
    max_response_bytes: int,
    max_output_bytes: int,
    deadline: float | None = None,
) -> FixedCoordinateAcceptanceProbeResult:
    """Run one bounded injected acceptance-only create/readback scenario."""
    expected_coordinate = _fixed_acceptance_coordinate(
        scenario=scenario,
        tag=tag,
    )
    if package_coordinate != expected_coordinate:
        message = "acceptance package coordinate is not the fixed coordinate"
        raise ValueError(message)
    if scenario not in ACCEPTANCE_SCENARIOS:
        message = "acceptance mutation scenario is unsupported"
        raise ValueError(message)
    if not isinstance(tarball, Path) or not tarball.is_file():
        message = "acceptance tarball must be an existing Path"
        raise ValueError(message)
    actual_sha512 = f"sha512:{hashlib.sha512(tarball.read_bytes()).hexdigest()}"
    if tarball_sha512 != actual_sha512:
        message = "acceptance tarball SHA-512 digest mismatch"
        raise ValueError(message)
    if type(timeout_seconds) not in {float, int} or timeout_seconds <= 0:
        message = "acceptance timeout must be positive"
        raise ValueError(message)
    _positive_exact_int(max_response_bytes, field="max_response_bytes")
    _positive_exact_int(max_output_bytes, field="max_output_bytes")
    observe = getattr(transport, "observe", None)
    run = getattr(runner, "run", None)
    if not callable(observe) or not callable(run):
        message = "acceptance transport and runner must be injected"
        raise TypeError(message)

    operation_deadline = (
        monotonic() + float(timeout_seconds) if deadline is None else deadline
    )
    pre_value = _call_with_acceptance_deadline(
        observe,
        package_coordinate,
        tag,
        timeout_seconds=_remaining_acceptance_time(operation_deadline),
        max_response_bytes=max_response_bytes,
        deadline=operation_deadline,
    )
    pre_state, pre_response, pre_content = _acceptance_observation(
        pre_value,
        tag=tag,
        desired_sha512=actual_sha512,
    )
    if scenario == "exact":
        if pre_state == "exact" and pre_content == actual_sha512:
            return _acceptance_result(
                scenario=scenario,
                tag=tag,
                pre_state="exact",
                post_state="exact",
                result="exact-no-mutation",
                mutation_classification="complete",
                action_executed=False,
                mutation_started=False,
                response_identity_digest=pre_response,
                content_sha512=pre_content,
            )
        result = (
            "exact-state-absent"
            if pre_state == "absent"
            else "preexisting-tag-conflict"
        )
        diagnostics = (
            (
                "exact-state-not-observed",
                "human-reconciliation-required",
            )
            if pre_state == "absent"
            else (
                "conflicting-remote-bytes-or-tag",
                "human-reconciliation-required",
            )
        )
        return _acceptance_result(
            scenario=scenario,
            tag=tag,
            pre_state=pre_state,
            post_state=pre_state,
            result=result,
            mutation_classification="incomplete",
            action_executed=False,
            mutation_started=False,
            response_identity_digest=pre_response,
            content_sha512=pre_content,
            diagnostics=diagnostics,
        )
    if pre_state != "absent":
        return _acceptance_result(
            scenario=scenario,
            tag=tag,
            pre_state=pre_state,
            post_state=pre_state,
            result="fixed-coordinate-already-exists",
            mutation_classification="incomplete",
            action_executed=False,
            mutation_started=False,
            response_identity_digest=pre_response,
            content_sha512=pre_content,
            diagnostics=(
                "absent-state-not-observed",
                "new-fixed-coordinate-required",
            ),
        )

    command = (
        "npm",
        "publish",
        str(tarball),
        "--tag",
        tag,
        "--registry",
        GITHUB_PACKAGES_REGISTRY,
        "--ignore-scripts",
    )
    runner_result: object | None = None
    runner_error: Exception | None = None
    runner_timed_out = False
    action_executed = False
    mutation_started = False
    exception_startedness_admitted = False
    returned_facts_malformed = False
    try:
        run_scenario = getattr(runner, "run_scenario", None)
        if callable(run_scenario):
            runner_result = _call_with_acceptance_deadline(
                run_scenario,
                scenario,
                command,
                env={"NPM_CONFIG_IGNORE_SCRIPTS": "true"},
                timeout_seconds=_remaining_acceptance_time(operation_deadline),
                max_output_bytes=max_output_bytes,
                deadline=operation_deadline,
            )
        else:
            runner_result = _call_with_acceptance_deadline(
                run,
                command,
                env={"NPM_CONFIG_IGNORE_SCRIPTS": "true"},
                timeout_seconds=_remaining_acceptance_time(operation_deadline),
                max_output_bytes=max_output_bytes,
                deadline=operation_deadline,
            )
    except TimeoutError as error:
        runner_error = error
        runner_timed_out = True
        executed_fact = getattr(error, "action_executed", None)
        started_fact = getattr(error, "mutation_started", None)
        if (
            type(executed_fact) is bool
            and type(started_fact) is bool
            and (executed_fact or not started_fact)
        ):
            action_executed = executed_fact
            mutation_started = started_fact
            exception_startedness_admitted = True
    except (OSError, RuntimeError, ValueError) as error:
        runner_error = error
        executed_fact = getattr(error, "action_executed", None)
        started_fact = getattr(error, "mutation_started", None)
        if (
            type(executed_fact) is bool
            and type(started_fact) is bool
            and (executed_fact or not started_fact)
        ):
            action_executed = executed_fact
            mutation_started = started_fact
            exception_startedness_admitted = True

    if type(runner_result) is dict:
        executed_fact = runner_result.get("action-executed")
        started_fact = runner_result.get("mutation-started")
        if (
            type(executed_fact) is not bool
            or type(started_fact) is not bool
            or (started_fact and not executed_fact)
        ):
            returned_facts_malformed = True
            runner_error = ValueError(
                "acceptance runner action facts are malformed"
            )
            action_executed = False
            mutation_started = False
        else:
            action_executed = executed_fact
            mutation_started = started_fact

    post_value = _call_with_acceptance_deadline(
        observe,
        package_coordinate,
        tag,
        timeout_seconds=_remaining_acceptance_time(operation_deadline),
        max_response_bytes=max_response_bytes,
        deadline=operation_deadline,
    )
    post_state, post_response, post_content = _acceptance_observation(
        post_value,
        tag=tag,
        desired_sha512=actual_sha512,
    )
    runner_document = (
        cast("dict[str, object]", runner_result)
        if type(runner_result) is dict
        else {}
    )
    protocol_confirmed = (
        scenario == "absent-create-readback"
        and action_executed
        and mutation_started
        and _valid_protocol_confirmed_proof(
            runner_document,
            tarball=tarball.read_bytes(),
            package_coordinate=package_coordinate,
            tag=tag,
        )
    )
    runner_diagnostic = _acceptance_runner_diagnostic(
        runner_document,
        runner_error=runner_error,
        action_executed=action_executed,
        mutation_started=mutation_started,
        exception_startedness_admitted=exception_startedness_admitted,
        protocol_confirmed=protocol_confirmed,
    )
    if runner_error is not None:
        malformed_facts = returned_facts_malformed
        if runner_timed_out:
            if exception_startedness_admitted and not mutation_started:
                return _acceptance_result(
                    scenario=scenario,
                    tag=tag,
                    pre_state="absent",
                    post_state=post_state,
                    result=_runner_failure_result(
                        action_executed=action_executed,
                        mutation_started=mutation_started,
                    ),
                    mutation_classification="incomplete",
                    action_executed=action_executed,
                    mutation_started=mutation_started,
                    response_identity_digest=post_response,
                    content_sha512=post_content,
                    diagnostics=_runner_failure_diagnostics(
                        action_executed=action_executed,
                        mutation_started=mutation_started,
                    ),
                    runner_diagnostic=runner_diagnostic,
                )
            return _acceptance_result(
                scenario=scenario,
                tag=tag,
                pre_state="absent",
                post_state=post_state,
                result="timeout",
                mutation_classification="unknown",
                action_executed=action_executed,
                mutation_started=mutation_started,
                response_identity_digest=post_response,
                content_sha512=post_content,
                diagnostics=(
                    "mutation-may-have-started",
                    "human-reconciliation-required",
                ),
                runner_diagnostic=runner_diagnostic,
            )
        if malformed_facts:
            return _acceptance_result(
                scenario=scenario,
                tag=tag,
                pre_state="absent",
                post_state=post_state,
                result="runner-malformed-before-mutation",
                mutation_classification="incomplete",
                action_executed=action_executed,
                mutation_started=mutation_started,
                response_identity_digest=post_response,
                content_sha512=post_content,
                diagnostics=("runner-action-facts-not-fully-admitted",),
                runner_diagnostic=runner_diagnostic,
            )
        if scenario == "lost-response":
            if exception_startedness_admitted and not mutation_started:
                return _acceptance_result(
                    scenario=scenario,
                    tag=tag,
                    pre_state="absent",
                    post_state=post_state,
                    result=_runner_failure_result(
                        action_executed=action_executed,
                        mutation_started=mutation_started,
                    ),
                    mutation_classification="incomplete",
                    action_executed=action_executed,
                    mutation_started=mutation_started,
                    response_identity_digest=post_response,
                    content_sha512=post_content,
                    diagnostics=_runner_failure_diagnostics(
                        action_executed=action_executed,
                        mutation_started=mutation_started,
                    ),
                    runner_diagnostic=runner_diagnostic,
                )
            return _acceptance_result(
                scenario=scenario,
                tag=tag,
                pre_state="absent",
                post_state=post_state,
                result="lost-response",
                mutation_classification="unknown",
                action_executed=action_executed,
                mutation_started=mutation_started,
                response_identity_digest=post_response,
                content_sha512=post_content,
                diagnostics=(
                    "mutation-may-have-started",
                    "human-reconciliation-required",
                ),
                runner_diagnostic=runner_diagnostic,
            )
        return _acceptance_result(
            scenario=scenario,
            tag=tag,
            pre_state="absent",
            post_state=post_state,
            result=_runner_failure_result(
                action_executed=action_executed,
                mutation_started=mutation_started,
                malformed_outcome=malformed_facts,
            ),
            mutation_classification="incomplete",
            action_executed=action_executed,
            mutation_started=mutation_started,
            response_identity_digest=post_response,
            content_sha512=post_content,
            diagnostics=(
                ("runner-action-facts-not-fully-admitted",)
                if malformed_facts
                else _runner_failure_diagnostics(
                    action_executed=action_executed,
                    mutation_started=mutation_started,
                )
            ),
            runner_diagnostic=runner_diagnostic,
        )

    outcome = runner_document.get("outcome")
    if scenario == "lost-response":
        if (
            _valid_lost_response_proof(
                runner_document,
                tarball=tarball.read_bytes(),
                version=package_coordinate.rsplit("@", 1)[1],
                tag=tag,
            )
            and action_executed
            and mutation_started
            and post_state == "exact"
            and post_content == actual_sha512
        ):
            proof = cast(
                "ValidatedAcceptanceRequestProof",
                runner_document["validated-request-proof"],
            )
            _require_runner_diagnostic_proof_binding(
                runner_diagnostic,
                proof,
            )
            return _acceptance_result(
                scenario=scenario,
                tag=tag,
                pre_state="absent",
                post_state="exact",
                result="lost-response-exact-after-start",
                mutation_classification="complete",
                action_executed=action_executed,
                mutation_started=mutation_started,
                response_identity_digest=proof.response_identity_digest,
                content_sha512=post_content,
                diagnostics=("mutation-started-and-readback-exact",),
                validated_request_proof=proof,
                runner_diagnostic=runner_diagnostic,
            )
        if not mutation_started:
            return _acceptance_result(
                scenario=scenario,
                tag=tag,
                pre_state="absent",
                post_state=post_state,
                result=_runner_failure_result(
                    action_executed=action_executed,
                    mutation_started=mutation_started,
                    malformed_outcome=type(outcome) is not str,
                ),
                mutation_classification="incomplete",
                action_executed=action_executed,
                mutation_started=mutation_started,
                response_identity_digest=post_response,
                content_sha512=post_content,
                diagnostics=_runner_failure_diagnostics(
                    action_executed=action_executed,
                    mutation_started=mutation_started,
                ),
                runner_diagnostic=runner_diagnostic,
            )
        return _acceptance_result(
            scenario=scenario,
            tag=tag,
            pre_state="absent",
            post_state=post_state,
            result="lost-response",
            mutation_classification="unknown",
            action_executed=action_executed,
            mutation_started=mutation_started,
            response_identity_digest=post_response,
            content_sha512=post_content,
            diagnostics=(
                "mutation-may-have-started",
                "human-reconciliation-required",
            ),
            runner_diagnostic=runner_diagnostic,
        )
    if protocol_confirmed:
        proof = cast(
            "ValidatedAcceptanceRequestProof",
            runner_document["validated-request-proof"],
        )
        if (
            action_executed
            and mutation_started
            and post_state == "exact"
            and post_content == actual_sha512
        ):
            return _acceptance_result(
                scenario=scenario,
                tag=tag,
                pre_state="absent",
                post_state="exact",
                result="protocol-confirmed",
                mutation_classification="complete",
                action_executed=True,
                mutation_started=True,
                response_identity_digest=proof.response_identity_digest,
                content_sha512=post_content,
                validated_request_proof=proof,
                runner_diagnostic=runner_diagnostic,
            )
        return _acceptance_result(
            scenario=scenario,
            tag=tag,
            pre_state="absent",
            post_state=post_state,
            result="protocol-confirmed-readback-incomplete",
            mutation_classification="incomplete",
            action_executed=action_executed,
            mutation_started=mutation_started,
            response_identity_digest=proof.response_identity_digest,
            content_sha512=post_content,
            diagnostics=("exact-readback-not-observed",),
            validated_request_proof=proof,
            runner_diagnostic=runner_diagnostic,
        )
    if type(outcome) is not str or outcome not in {
        "created",
        "create-conflict",
    }:
        return _acceptance_result(
            scenario=scenario,
            tag=tag,
            pre_state="absent",
            post_state=post_state,
            result=_runner_failure_result(
                action_executed=action_executed,
                mutation_started=mutation_started,
                malformed_outcome=type(outcome) is not str,
            ),
            mutation_classification="incomplete",
            action_executed=action_executed,
            mutation_started=mutation_started,
            response_identity_digest=post_response,
            content_sha512=post_content,
            diagnostics=("runner-did-not-prove-controlled-outcome",),
            runner_diagnostic=runner_diagnostic,
        )
    if not action_executed or not mutation_started:
        return _acceptance_result(
            scenario=scenario,
            tag=tag,
            pre_state="absent",
            post_state=post_state,
            result="runner-malformed-before-mutation",
            mutation_classification="incomplete",
            action_executed=action_executed,
            mutation_started=mutation_started,
            response_identity_digest=post_response,
            content_sha512=post_content,
            diagnostics=("runner-action-facts-not-fully-admitted",),
            runner_diagnostic=runner_diagnostic,
        )
    if scenario == "absent-create-readback":
        return _acceptance_result(
            scenario=scenario,
            tag=tag,
            pre_state="absent",
            post_state=post_state,
            result="created-without-request-proof",
            mutation_classification="incomplete",
            action_executed=action_executed,
            mutation_started=mutation_started,
            response_identity_digest=post_response,
            content_sha512=post_content,
            diagnostics=("request-bound-created-proof-required",),
            runner_diagnostic=runner_diagnostic,
        )
    if scenario == "differing-race":
        contender_outcomes = runner_document.get("contender-outcomes")
        winner_sha512 = runner_document.get("winner-content-sha512")
        contender_sha512 = runner_document.get("contender-content-sha512")
        valid_outcomes = (
            type(contender_outcomes) is list
            and len(contender_outcomes) == PAIR_SIZE
            and contender_outcomes.count("created") == 1
            and contender_outcomes.count("create-conflict") == 1
        )
        race_overlap_proven = runner_document.get("race-overlap-proven") is True
        valid_winner = (
            type(winner_sha512) is str
            and winner_sha512 == post_content
            and post_state in {"exact", "conflicting"}
        )
        if contender_sha512 is not None:
            valid_winner = (
                valid_winner
                and type(contender_sha512) is str
                and winner_sha512 in {actual_sha512, contender_sha512}
            )
        if not valid_outcomes or not valid_winner or not race_overlap_proven:
            return _acceptance_result(
                scenario=scenario,
                tag=tag,
                pre_state="absent",
                post_state=post_state,
                result="differing-race-winner-not-proven",
                mutation_classification="unknown",
                action_executed=action_executed,
                mutation_started=mutation_started,
                response_identity_digest=post_response,
                content_sha512=post_content,
                diagnostics=(
                    "exclusive-created-and-conflict-outcomes-required",
                    "winner-readback-identity-required",
                    "race-overlap-not-proven",
                    "human-reconciliation-required",
                ),
                runner_diagnostic=runner_diagnostic,
            )
        return _acceptance_result(
            scenario=scenario,
            tag=tag,
            pre_state="absent",
            post_state=post_state,
            result="differing-race-conflict",
            mutation_classification="complete",
            action_executed=action_executed,
            mutation_started=mutation_started,
            response_identity_digest=post_response,
            content_sha512=post_content,
            diagnostics=("conflicting-remote-bytes-or-tag",),
            runner_diagnostic=runner_diagnostic,
        )
    if post_state == "exact" and post_content == actual_sha512:
        if outcome == "create-conflict":
            if scenario == "differing-race":
                return _acceptance_result(
                    scenario=scenario,
                    tag=tag,
                    pre_state="absent",
                    post_state="exact",
                    result="differing-race-conflicting-readback-missing",
                    mutation_classification="incomplete",
                    action_executed=action_executed,
                    mutation_started=mutation_started,
                    response_identity_digest=post_response,
                    content_sha512=post_content,
                    diagnostics=(
                        "required-conflicting-readback-not-observed",
                        "human-reconciliation-required",
                    ),
                    runner_diagnostic=runner_diagnostic,
                )
            result = "identical-race-exact"
            diagnostics = ("identical-race-exact",)
        else:
            result = "created"
            diagnostics = ()
        return _acceptance_result(
            scenario=scenario,
            tag=tag,
            pre_state="absent",
            post_state="exact",
            result=result,
            mutation_classification="complete",
            action_executed=action_executed,
            mutation_started=mutation_started,
            response_identity_digest=post_response,
            content_sha512=post_content,
            diagnostics=diagnostics,
            runner_diagnostic=runner_diagnostic,
        )
    if post_state == "conflicting":
        if outcome == "create-conflict" and scenario == "identical-race":
            return _acceptance_result(
                scenario=scenario,
                tag=tag,
                pre_state="absent",
                post_state="unknown",
                result="conflict-race-tag-unknown",
                mutation_classification="unknown",
                action_executed=action_executed,
                mutation_started=mutation_started,
                response_identity_digest=post_response,
                content_sha512=post_content,
                diagnostics=(
                    "conflicting-remote-bytes-or-tag",
                    "human-reconciliation-required",
                ),
                runner_diagnostic=runner_diagnostic,
            )
        if outcome == "create-conflict":
            return _acceptance_result(
                scenario=scenario,
                tag=tag,
                pre_state="absent",
                post_state="conflicting",
                result="differing-race-conflict",
                mutation_classification="complete",
                action_executed=action_executed,
                mutation_started=mutation_started,
                response_identity_digest=post_response,
                content_sha512=post_content,
                diagnostics=("conflicting-remote-bytes-or-tag",),
                runner_diagnostic=runner_diagnostic,
            )
        return _acceptance_result(
            scenario=scenario,
            tag=tag,
            pre_state="absent",
            post_state="conflicting",
            result="readback-tag-conflict",
            mutation_classification="incomplete",
            action_executed=action_executed,
            mutation_started=mutation_started,
            response_identity_digest=post_response,
            content_sha512=post_content,
            diagnostics=(
                "conflicting-remote-bytes-or-tag",
                "human-reconciliation-required",
            ),
            runner_diagnostic=runner_diagnostic,
        )
    return _acceptance_result(
        scenario=scenario,
        tag=tag,
        pre_state="absent",
        post_state="unknown",
        result="lost-response",
        mutation_classification="unknown",
        action_executed=action_executed,
        mutation_started=mutation_started,
        response_identity_digest=post_response,
        content_sha512=post_content,
        diagnostics=(
            "mutation-may-have-started",
            "human-reconciliation-required",
        ),
        runner_diagnostic=runner_diagnostic,
    )


def run_fixed_acceptance_suite(  # noqa: PLR0913
    *,
    suite: str,
    tarballs: dict[str, Path],
    transport: object,
    runner: object,
    timeout_seconds: float,
    max_response_bytes: int,
    max_output_bytes: int,
    base_package_coordinate: str = ACCEPTANCE_PACKAGE_COORDINATE,
    deadline: float | None = None,
) -> FixedAcceptanceSuiteResult:
    """Run one reviewed fixed suite with no caller-selected coordinates."""
    scenario_specs = fixed_acceptance_scenario_specs(base_package_coordinate)
    coordinates = fixed_acceptance_coordinates(base_package_coordinate)
    inventories = {
        "absent-create-readback": ("absent-create-readback",),
        "exact-and-conflict": (
            "exact",
            "identical-race",
            "differing-race",
            "lost-response",
        ),
    }
    inventory = inventories.get(suite)
    if inventory is None:
        message = "acceptance suite is not reviewed"
        raise ValueError(message)
    if set(tarballs) != set(inventory):
        message = "acceptance suite tarball inventory is not exact"
        raise ValueError(message)
    operation_deadline = (
        monotonic() + float(timeout_seconds) if deadline is None else deadline
    )
    results: list[FixedCoordinateAcceptanceProbeResult] = []
    for scenario in inventory:
        coordinate = coordinates[scenario]
        tag = next(
            fixed_tag
            for fixed_scenario, _version, fixed_tag in scenario_specs
            if fixed_scenario == scenario
        )
        tarball = tarballs[scenario]
        results.append(
            run_fixed_coordinate_acceptance_probe(
                scenario=scenario,
                package_coordinate=coordinate,
                tag=tag,
                tarball=tarball,
                tarball_sha512=(
                    f"sha512:{hashlib.sha512(tarball.read_bytes()).hexdigest()}"
                ),
                transport=transport,
                runner=runner,
                timeout_seconds=timeout_seconds,
                max_response_bytes=max_response_bytes,
                max_output_bytes=max_output_bytes,
                deadline=operation_deadline,
            )
        )
    return FixedAcceptanceSuiteResult(suite=suite, scenarios=tuple(results))


def _positive_exact_int(value: object, *, field: str) -> int:
    if type(value) is not int:
        message = f"{field} must be a positive exact integer"
        raise TypeError(message)
    accepted = cast("int", value)
    if accepted <= 0:
        message = f"{field} must be positive"
        raise ValueError(message)
    return accepted


def validate_observation_bounds(
    *,
    timeout: int,
    max_bytes: int,
    max_pages: int,
) -> None:
    """Validate exact positive first-slice observation limits."""
    _positive_exact_int(timeout, field="timeout")
    _positive_exact_int(max_bytes, field="max_bytes")
    pages = _positive_exact_int(max_pages, field="max_pages")
    if pages > DEFAULT_MAX_PAGES:
        message = f"max_pages must be at most {DEFAULT_MAX_PAGES}"
        raise ValueError(message)


def github_package_versions_url(
    *,
    owner: str,
    package_name: str,
    page: int,
    per_page: int,
) -> str:
    """Return the exact escaped user-owned npm package versions URL."""
    if (
        owner != GITHUB_PACKAGES_OWNER
        or package_name != GITHUB_PACKAGES_PACKAGE
    ):
        message = (
            "GitHub Packages owner/package identity is outside first slice"
        )
        raise ValueError(message)
    _positive_exact_int(page, field="page")
    size = _positive_exact_int(per_page, field="per_page")
    if size != GITHUB_PAGE_SIZE:
        message = f"per_page must be exactly {GITHUB_PAGE_SIZE}"
        raise ValueError(message)
    resource_name = package_name.removeprefix(f"@{owner}/")
    encoded = urllib.parse.quote(resource_name, safe="")
    return (
        f"{GITHUB_API_ORIGIN}/users/{owner}/packages/npm/{encoded}/versions"
        f"?per_page={size}&page={page}"
    )


def _npm_package_path(package_name: str) -> str:
    if package_name != GITHUB_PACKAGES_PACKAGE:
        message = "npm package identity is outside first slice"
        raise ValueError(message)
    return urllib.parse.quote(package_name, safe="@")


def npm_exact_metadata_url(package_name: str, version: str) -> str:
    """Return the exact escaped GitHub Packages npm version metadata URL."""
    encoded_package = _npm_package_path(package_name)
    encoded_version = urllib.parse.quote(version, safe="")
    return f"{GITHUB_PACKAGES_REGISTRY}/{encoded_package}/{encoded_version}"


def _npm_package_metadata_url(package_name: str) -> str:
    return f"{GITHUB_PACKAGES_REGISTRY}/{_npm_package_path(package_name)}"


def github_api_headers(token: str) -> tuple[tuple[str, str], ...]:
    """Return exact credential-bearing GitHub REST transport headers."""
    _token(token)
    return (
        ("Accept", "application/vnd.github+json"),
        ("Authorization", f"Bearer {token}"),
        ("X-GitHub-Api-Version", "2022-11-28"),
    )


def _github_transport_headers(token: str) -> tuple[tuple[str, str], ...]:
    return github_api_headers(token)


def _retained_github_headers() -> tuple[tuple[str, str], ...]:
    return (
        ("Accept", "application/vnd.github+json"),
        ("Authorization", _REDACTED),
        ("X-GitHub-Api-Version", "2022-11-28"),
    )


def _npm_transport_headers(
    token: str,
    *,
    tarball: bool = False,
) -> tuple[tuple[str, str], ...]:
    _token(token)
    return (
        (
            "Accept",
            "application/octet-stream"
            if tarball
            else "application/vnd.npm.install-v1+json, application/json",
        ),
        ("Accept-Encoding", "identity"),
        ("Authorization", f"Bearer {token}"),
        ("Cache-Control", "no-cache"),
    )


def _token(token: object) -> str:
    if type(token) is not str or not token:
        message = "GitHub Packages token must be a nonempty exact string"
        raise TypeError(message)
    return cast("str", token)


def redact_diagnostic(value: str, *, secrets: tuple[str, ...] = ()) -> str:
    """Redact credentials from retained command, response, and diagnostics."""
    redacted = value
    for secret in secrets:
        if secret:
            redacted = redacted.replace(secret, _REDACTED)
    for pattern in _TOKEN_PATTERNS:
        redacted = pattern.sub(rf"\1{_REDACTED}", redacted)
    return redacted


def _origin(url: str) -> tuple[str, str, int]:
    parsed = urllib.parse.urlparse(url)
    try:
        port = parsed.port
    except ValueError as error:
        message = "URL has an invalid port"
        raise GitHubPackagesPolicyError(message) from error
    if (
        parsed.scheme != "https"
        or parsed.hostname is None
        or parsed.username is not None
        or parsed.password is not None
        or port not in {None, 443}
        or parsed.fragment
    ):
        message = "URL is outside the approved HTTPS origin policy"
        raise GitHubPackagesPolicyError(message)
    return ("https", parsed.hostname.lower(), 443)


def _validate_transport_request(url: str, *, credentialed: bool) -> None:
    _scheme, host, _port = _origin(url)
    if credentialed and host not in _CREDENTIAL_HOSTS:
        message = "credentialed request host is outside approved origins"
        raise GitHubPackagesPolicyError(message)


def redirect_headers(
    *,
    source_url: str,
    target_url: str,
    headers: tuple[tuple[str, str], ...],
) -> tuple[tuple[str, str], ...]:
    """Strip credentials whenever a redirect crosses an HTTPS origin."""
    if _origin(source_url) == _origin(target_url):
        return headers
    return tuple(
        (name, value)
        for name, value in headers
        if name.lower() not in {"authorization", "cookie", "npm-token"}
    )


def _allowed_url(url: str, *, stage: str) -> bool:
    try:
        _scheme, host, _port = _origin(url)
    except GitHubPackagesPolicyError:
        return False
    if stage == "rest":
        return host == "api.github.com"
    if stage in {"npm-metadata", "npm-tags"}:
        return host == "npm.pkg.github.com"
    return host in _ALLOWED_TARBALL_HOSTS


def _response_policy_ok(
    response: GitHubPackagesHttpResponse,
    *,
    requested_url: str,
    stage: str,
    credentialed: bool,
) -> bool:
    chain = (requested_url, *response.redirects, response.url)
    if not all(_allowed_url(url, stage=stage) for url in chain):
        return False
    if credentialed:
        return all(
            _origin(source) == _origin(target)
            for source, target in itertools.pairwise(chain)
        )
    return True


def _header(
    response: GitHubPackagesHttpResponse,
    name: str,
) -> str | None:
    lowered = name.lower()
    for key, value in response.headers:
        if key.lower() == lowered:
            return value
    return None


def _github_link_next_url(
    response: GitHubPackagesHttpResponse,
    *,
    requested_url: str,
) -> str | None:
    value = _header(response, "link")
    if value is None:
        return None
    next_url: str | None = None
    for member in value.split(","):
        section, *parameters = member.strip().split(";")
        if not section.startswith("<") or not section.endswith(">"):
            message = "GitHub REST Link is malformed"
            raise GitHubPackagesPolicyError(message)
        rels = {
            parameter.strip()[5:-1]
            for parameter in parameters
            if parameter.strip().startswith('rel="')
            and parameter.strip().endswith('"')
        }
        if "next" not in rels:
            continue
        candidate = urllib.parse.urljoin(requested_url, section[1:-1])
        if next_url is not None:
            message = "GitHub REST Link has duplicate next relations"
            raise GitHubPackagesPolicyError(message)
        if not _allowed_url(candidate, stage="rest") or _origin(
            candidate
        ) != _origin(requested_url):
            message = (
                "GitHub REST Link next is outside the authoritative origin"
            )
            raise GitHubPackagesPolicyError(message)
        next_url = candidate
    return next_url


def _identity_encoding(response: GitHubPackagesHttpResponse) -> bool:
    encoding = _header(response, "content-encoding")
    return encoding is None or encoding.lower() == "identity"


def _selected_headers(
    response: GitHubPackagesHttpResponse,
) -> tuple[tuple[str, str], ...]:
    return tuple(
        (name.lower(), redact_diagnostic(value))
        for name, value in sorted(
            response.headers,
            key=lambda item: item[0].lower(),
        )
        if name.lower() in _SELECTED_HEADERS
    )


def _body_digest(body: bytes) -> str:
    return f"sha256:{hashlib.sha256(body).hexdigest()}"


def _exchange(
    stage: str,
    requested_url: str,
    response: GitHubPackagesHttpResponse,
    *,
    detail: str | None = None,
) -> _Exchange:
    return _Exchange(
        stage=stage,
        requested_url=requested_url,
        final_url=response.url,
        redirects=response.redirects,
        status=response.status,
        selected_headers=_selected_headers(response),
        truncated=response.truncated,
        complete=response.complete,
        body_sha256=_body_digest(response.body),
        detail=detail,
    )


def _synthetic_exchange(
    stage: str,
    requested_url: str,
    status: str,
) -> _Exchange:
    return _Exchange(
        stage=stage,
        requested_url=requested_url,
        final_url=None,
        redirects=(),
        status=status,
        selected_headers=(),
        truncated=None,
        complete=None,
        body_sha256=None,
    )


def _json(response: GitHubPackagesHttpResponse, *, field: str) -> JsonValue:
    try:
        return parse_json_strict(response.body)
    except (TypeError, ValueError) as error:
        message = f"{field} is malformed"
        raise ValueError(message) from error


def _status_classification(response: GitHubPackagesHttpResponse) -> str | None:
    if response.truncated or not response.complete:
        return "unknown"
    if response.status == HTTP_TOO_MANY_REQUESTS or (
        response.status >= HTTP_SERVER_ERROR
    ):
        return "unknown"
    if response.status in {HTTP_UNAUTHORIZED, HTTP_FORBIDDEN}:
        return "unprovable"
    return None


def _target_tag(target: str) -> str:
    if type(target) is not str or re.fullmatch(r"[0-9a-f]{40}", target) is None:
        message = "target must be a 40-character lowercase SHA"
        raise ValueError(message)
    return f"buddy-sha-{target}"


def _probe_value(  # noqa: PLR0913
    *,
    classification: str,
    coordinate: ExternalPackageCoordinate,
    owner: str | None = None,
    content_sha512: str | None = None,
    witness_digest: str | None = None,
    tag_version: str | None = None,
    target: str,
) -> ObservationValue:
    if classification == "absent":
        return ObservationValue(
            classification="absent",
            owner=None,
            coordinate=None,
            content_sha512=None,
            witness_digest=None,
            routing=(),
        )
    routing = (
        ((_target_tag(target), tag_version),) if tag_version is not None else ()
    )
    if classification == "exact-satisfied":
        return ObservationValue(
            classification=classification,
            owner=owner or f"org:{GITHUB_PACKAGES_OWNER}",
            coordinate=coordinate,
            content_sha512=content_sha512,
            witness_digest=witness_digest,
            routing=routing,
        )
    retain_remote = classification in {"partial", "conflicting"}
    return ObservationValue(
        classification=classification,
        owner=(owner if retain_remote else None),
        coordinate=(coordinate if retain_remote else None),
        content_sha512=(content_sha512 if retain_remote else None),
        witness_digest=(witness_digest if retain_remote else None),
        routing=(routing if retain_remote else ()),
    )


def classify_rest_npm_consistency(
    *,
    rest_version: str | None,
    npm_version: str | None,
    tag_version: str | None,
) -> str:
    """Classify exact cross-surface version/tag facts."""
    present = [
        value for value in (rest_version, npm_version) if value is not None
    ]
    if len(set(present)) > 1:
        return "conflicting"
    if bool(rest_version) != bool(npm_version):
        return "unknown"
    if present and tag_version is None:
        return "partial"
    if present and tag_version != present[0]:
        return "conflicting"
    return "exact-satisfied" if present else "absent"


def classify_github_packages_probe(  # noqa: PLR0913
    *,
    coordinate: ExternalPackageCoordinate,
    target: str,
    rest_state: str,
    npm_state: str,
    local_sha512: str,
    remote_sha512: str | None,
    local_witness: str,
    remote_witness: str | None,
    tag_version: str | None,
) -> ProbeClassification:
    """Pure projection-wide classification helper."""
    target_tag = _target_tag(target)
    del target_tag
    if rest_state == "denied" or npm_state in {"denied", "unknown"}:
        classification = "unprovable" if npm_state == "denied" else "unknown"
    elif rest_state == npm_state == "absent" and tag_version is None:
        classification = "absent"
    elif rest_state != npm_state:
        classification = "unknown"
    elif (
        rest_state != "present"
        or remote_sha512 is None
        or remote_witness is None
    ):
        classification = "unprovable"
    elif remote_sha512 != local_sha512 or remote_witness != local_witness:
        classification = "conflicting"
    elif tag_version is None:
        classification = "partial"
    elif tag_version != coordinate.native_version:
        classification = "conflicting"
    else:
        classification = "exact-satisfied"
    return ProbeClassification(
        _probe_value(
            classification=classification,
            coordinate=coordinate,
            owner=f"org:{GITHUB_PACKAGES_OWNER}",
            content_sha512=remote_sha512,
            witness_digest=remote_witness,
            tag_version=tag_version,
            target=target,
        )
    )


def _validate_first_slice_basis(
    snapshot: QualificationSnapshot,
    decision: QualificationDecision,
    artifact: ReleaseArtifact,
    expectation: ArtifactExpectation,
) -> tuple[ReleaseAttemptIdentity, DestinationProjection]:
    if type(snapshot) is not QualificationSnapshot:
        message = "GitHub Packages observation basis Snapshot is not exact"
        raise ValueError(message)
    if type(snapshot.subject) is not ReleaseAttemptIdentity:
        message = "GitHub Packages observation basis is not live"
        raise TypeError(message)
    attempt = snapshot.subject
    if (
        type(decision) is not QualificationDecision
        or type(artifact) is not ReleaseArtifact
        or decision.terminal_result != "success"
        or decision.subject != attempt
        or decision.qualification_snapshot_digest != snapshot.snapshot_digest
        or artifact.subject != attempt
        or artifact.purpose != "live-release"
        or artifact.target != snapshot.target
        or artifact.qualification_snapshot_digest != snapshot.snapshot_digest
        or artifact.artifact_digest not in decision.admitted_artifact_digests
    ):
        message = "GitHub Packages observation basis is not current"
        raise ValueError(message)
    if len(snapshot.destination_projections) != 1:
        message = "GitHub Packages observation basis projection is not exact"
        raise ValueError(message)
    projection = snapshot.destination_projections[0]
    coordinate = projection.coordinate
    if (
        projection.destination_id != GITHUB_PACKAGES_DESTINATION_ID
        or projection.registry != GITHUB_PACKAGES_REGISTRY
        or projection.observation_contract_id
        != GITHUB_PACKAGES_OBSERVATION_CONTRACT_ID
        or projection.operation != GITHUB_PACKAGES_OPERATION
        or coordinate.channel != "buddy"
        or coordinate.package_name != GITHUB_PACKAGES_PACKAGE
        or coordinate.native_version != snapshot.nbgv.npm_package_version
        or expectation.package_name != GITHUB_PACKAGES_PACKAGE
        or expectation.npm_package_version != coordinate.native_version
        or snapshot.target != attempt.execution.target
        or type(attempt.execution) is not BuddyExecutionIdentity
    ):
        message = "GitHub Packages observation basis is outside first slice"
        raise ValueError(message)
    _validate_artifact_expectation(expectation)
    return attempt, projection


def _repository_from_metadata(document: dict[str, JsonValue]) -> str | None:
    repository = document.get("repository")
    candidate: str | None = None
    if type(repository) is str:
        candidate = repository
    elif isinstance(repository, dict) and type(repository.get("url")) is str:
        candidate = cast("str", repository["url"])
    if candidate is None:
        return None
    normalized = candidate.removesuffix(".git")
    for prefix in (
        "git+https://github.com/",
        "https://github.com/",
        "git://github.com/",
        "github:",
    ):
        if normalized.startswith(prefix):
            return normalized.removeprefix(prefix)
    return "ambiguous"


def _rest_owner(version_document: dict[str, JsonValue]) -> str:
    value = version_document.get("url")
    if type(value) is not str:
        return "ambiguous"
    try:
        parsed = urllib.parse.urlparse(value)
        origin = _origin(value)
        (
            root,
            owner_kind,
            owner,
            packages_segment,
            package_type,
            package_resource,
            versions_segment,
            version_id,
        ) = parsed.path.split("/")
        version_number = int(version_id)
    except (GitHubPackagesPolicyError, ValueError):
        return "ambiguous"
    resource_name = GITHUB_PACKAGES_PACKAGE.removeprefix(
        f"@{GITHUB_PACKAGES_OWNER}/"
    )
    if (
        origin != ("https", "api.github.com", 443)
        or parsed.query
        or parsed.params
        or root
        or owner_kind not in {"users", "orgs"}
        or not owner
        or packages_segment != "packages"
        or package_type != "npm"
        or package_resource != resource_name
        or versions_segment != "versions"
        or not version_id.isdigit()
        or version_number <= 0
    ):
        return "ambiguous"
    return owner.lower()


def _aggregate_response_facts(
    exchanges: tuple[_Exchange, ...],
    facts: _RemoteFacts,
) -> ObservationResponseFacts:
    encoded = tuple(
        (
            f"exchange-{index:03d}",
            canonicalize(exchange.to_document()).decode("utf-8"),
        )
        for index, exchange in enumerate(exchanges)
    )
    status = exchanges[-1].status if exchanges else "not-run"
    return ObservationResponseFacts(
        stage="synthetic",
        requested_url=exchanges[0].requested_url if exchanges else "not-run",
        final_url=exchanges[-1].final_url if exchanges else None,
        redirects=tuple(
            redirect
            for exchange in exchanges
            for redirect in exchange.redirects
        ),
        status=status,
        selected_headers=encoded,
        truncated=any(exchange.truncated is True for exchange in exchanges),
        body_sha256=canonical_sha256(
            {
                "schema": "workflow-delivery/v3/github-packages-exchanges",
                "exchanges": [exchange.to_document() for exchange in exchanges],
            }
        ),
        status_detail="ordered-redacted-canonical-exchanges",
        metadata_package=(
            GITHUB_PACKAGES_PACKAGE if facts.npm_version is not None else None
        ),
        metadata_version=facts.npm_version,
        tarball_content_sha512=facts.content_sha512,
        remote_witness_digest=facts.witness_digest,
    )


def _projection_observation(  # noqa: PLR0913
    *,
    snapshot: QualificationSnapshot,
    artifact: ReleaseArtifact,
    projection: DestinationProjection,
    request_facts: ObservationRequestFacts,
    exchanges: tuple[_Exchange, ...],
    facts: _RemoteFacts,
    classification: str,
) -> ProjectionObservation:
    del artifact
    response_facts = _aggregate_response_facts(exchanges, facts)
    value = _probe_value(
        classification=classification,
        coordinate=projection.coordinate,
        owner=f"org:{GITHUB_PACKAGES_OWNER}",
        content_sha512=facts.content_sha512,
        witness_digest=facts.witness_digest,
        tag_version=facts.tag_version if facts.target_tag_present else None,
        target=snapshot.target,
    )
    request_digest = request_facts.request_digest
    return ProjectionObservation(
        subject=cast("ReleaseAttemptIdentity", snapshot.subject),
        purpose="live-release",
        target=snapshot.target,
        producer=GITHUB_PACKAGES_OBSERVER_PRODUCER,
        qualification_snapshot_digest=snapshot.snapshot_digest,
        projection=projection,
        desired_state_digest=request_facts.desired_state_digest,
        observation_contract_id=GITHUB_PACKAGES_OBSERVATION_CONTRACT_ID,
        request_facts=request_facts,
        request_digest=request_digest,
        response_facts=response_facts,
        response_digest=canonical_sha256(
            {
                "schema": "workflow-delivery/v3/observation-response",
                "request-digest": request_digest,
                "facts": response_facts.to_document(),
                "value": value.to_document(),
            }
        ),
        value=value,
    )


def observe_github_packages_projection(  # noqa: C901, PLR0911, PLR0912, PLR0913, PLR0915
    snapshot: QualificationSnapshot,
    decision: QualificationDecision,
    artifact: ReleaseArtifact,
    expectation: ArtifactExpectation,
    *,
    token: str,
    transport: GitHubPackagesTransport,
    timeout: int = DEFAULT_TIMEOUT_SECONDS,
    metadata_limit_bytes: int = DEFAULT_METADATA_LIMIT_BYTES,
    tarball_limit_bytes: int = DEFAULT_TARBALL_LIMIT_BYTES,
    expanded_tarball_limit_bytes: int = DEFAULT_EXPANDED_TARBALL_LIMIT_BYTES,
    max_pages: int = DEFAULT_MAX_PAGES,
) -> ProjectionObservation:
    """Observe exact GitHub Packages state through bounded injectable reads."""
    validate_observation_bounds(
        timeout=timeout,
        max_bytes=metadata_limit_bytes,
        max_pages=max_pages,
    )
    _positive_exact_int(tarball_limit_bytes, field="tarball_limit_bytes")
    _positive_exact_int(
        expanded_tarball_limit_bytes,
        field="expanded_tarball_limit_bytes",
    )
    _token(token)
    _attempt, projection = _validate_first_slice_basis(
        snapshot,
        decision,
        artifact,
        expectation,
    )
    first_url = github_package_versions_url(
        owner=GITHUB_PACKAGES_OWNER,
        package_name=GITHUB_PACKAGES_PACKAGE,
        page=1,
        per_page=GITHUB_PAGE_SIZE,
    )
    request_facts = ObservationRequestFacts(
        qualification_snapshot_digest=snapshot.snapshot_digest,
        projection_digest=projection.projection_digest,
        desired_state_digest=desired_projection_state_digest(
            snapshot,
            projection.projection_id,
            artifact,
        ),
        method="GET",
        url=first_url,
        headers=_retained_github_headers(),
    )
    exchanges: list[_Exchange] = []
    rest_version: str | None = None
    rest_owner: str | None = None
    rest_package_absent = False

    url = first_url
    for _page_index in range(1, max_pages + 1):
        try:
            response = cast(
                "GitHubPackagesHttpResponse",
                transport.get(
                    url,
                    headers=_github_transport_headers(token),
                    timeout=float(timeout),
                    max_bytes=metadata_limit_bytes,
                ),
            )
        except GitHubPackagesPolicyError:
            exchanges.append(_synthetic_exchange("rest", url, "off-policy"))
            return _projection_observation(
                snapshot=snapshot,
                artifact=artifact,
                projection=projection,
                request_facts=request_facts,
                exchanges=tuple(exchanges),
                facts=_RemoteFacts(),
                classification="unprovable",
            )
        except (
            GitHubPackagesTimeoutError,
            GitHubPackagesNetworkError,
            OSError,
        ):
            exchanges.append(_synthetic_exchange("rest", url, "network-error"))
            return _projection_observation(
                snapshot=snapshot,
                artifact=artifact,
                projection=projection,
                request_facts=request_facts,
                exchanges=tuple(exchanges),
                facts=_RemoteFacts(),
                classification="unknown",
            )
        if type(response) is not GitHubPackagesHttpResponse:
            message = "GitHub Packages transport returned a malformed response"
            raise TypeError(message)
        exchanges.append(_exchange("rest", url, response))
        status_class = _status_classification(response)
        if status_class is not None:
            return _projection_observation(
                snapshot=snapshot,
                artifact=artifact,
                projection=projection,
                request_facts=request_facts,
                exchanges=tuple(exchanges),
                facts=_RemoteFacts(),
                classification=status_class,
            )
        if not _response_policy_ok(
            response,
            requested_url=url,
            stage="rest",
            credentialed=True,
        ):
            return _projection_observation(
                snapshot=snapshot,
                artifact=artifact,
                projection=projection,
                request_facts=request_facts,
                exchanges=tuple(exchanges),
                facts=_RemoteFacts(),
                classification="unprovable",
            )
        if response.status == HTTP_NOT_FOUND:
            rest_package_absent = True
            break
        if response.status != HTTP_OK or not _identity_encoding(response):
            return _projection_observation(
                snapshot=snapshot,
                artifact=artifact,
                projection=projection,
                request_facts=request_facts,
                exchanges=tuple(exchanges),
                facts=_RemoteFacts(),
                classification="unprovable",
            )
        try:
            document = _json(response, field="GitHub versions response")
        except ValueError:
            return _projection_observation(
                snapshot=snapshot,
                artifact=artifact,
                projection=projection,
                request_facts=request_facts,
                exchanges=tuple(exchanges),
                facts=_RemoteFacts(),
                classification="unprovable",
            )
        if not isinstance(document, list):
            return _projection_observation(
                snapshot=snapshot,
                artifact=artifact,
                projection=projection,
                request_facts=request_facts,
                exchanges=tuple(exchanges),
                facts=_RemoteFacts(),
                classification="unprovable",
            )
        page_versions = document
        try:
            next_url = _github_link_next_url(response, requested_url=url)
        except GitHubPackagesPolicyError:
            return _projection_observation(
                snapshot=snapshot,
                artifact=artifact,
                projection=projection,
                request_facts=request_facts,
                exchanges=tuple(exchanges),
                facts=_RemoteFacts(),
                classification="unprovable",
            )
        for item in page_versions:
            if not isinstance(item, dict) or type(item.get("name")) is not str:
                return _projection_observation(
                    snapshot=snapshot,
                    artifact=artifact,
                    projection=projection,
                    request_facts=request_facts,
                    exchanges=tuple(exchanges),
                    facts=_RemoteFacts(),
                    classification="unprovable",
                )
            if item["name"] == projection.coordinate.native_version:
                if rest_version is not None:
                    return _projection_observation(
                        snapshot=snapshot,
                        artifact=artifact,
                        projection=projection,
                        request_facts=request_facts,
                        exchanges=tuple(exchanges),
                        facts=_RemoteFacts(),
                        classification="unprovable",
                    )
                rest_version = cast("str", item["name"])
                rest_owner = _rest_owner(item)
        if next_url is None:
            break
        url = next_url
    else:
        exchanges.append(
            _synthetic_exchange("rest", first_url, "pagination-incomplete")
        )
        return _projection_observation(
            snapshot=snapshot,
            artifact=artifact,
            projection=projection,
            request_facts=request_facts,
            exchanges=tuple(exchanges),
            facts=_RemoteFacts(rest_version=rest_version),
            classification="unknown",
        )

    package_url = _npm_package_metadata_url(GITHUB_PACKAGES_PACKAGE)
    exact_url = npm_exact_metadata_url(
        GITHUB_PACKAGES_PACKAGE,
        projection.coordinate.native_version,
    )
    npm_documents: dict[str, dict[str, JsonValue] | None] = {}
    for stage, url in (("npm-tags", package_url), ("npm-metadata", exact_url)):
        try:
            response = cast(
                "GitHubPackagesHttpResponse",
                transport.get(
                    url,
                    headers=_npm_transport_headers(token),
                    timeout=float(timeout),
                    max_bytes=metadata_limit_bytes,
                ),
            )
        except GitHubPackagesPolicyError:
            exchanges.append(_synthetic_exchange(stage, url, "off-policy"))
            classification = "unprovable"
            return _projection_observation(
                snapshot=snapshot,
                artifact=artifact,
                projection=projection,
                request_facts=request_facts,
                exchanges=tuple(exchanges),
                facts=_RemoteFacts(rest_version=rest_version),
                classification=classification,
            )
        except (
            GitHubPackagesTimeoutError,
            GitHubPackagesNetworkError,
            OSError,
        ):
            exchanges.append(_synthetic_exchange(stage, url, "network-error"))
            return _projection_observation(
                snapshot=snapshot,
                artifact=artifact,
                projection=projection,
                request_facts=request_facts,
                exchanges=tuple(exchanges),
                facts=_RemoteFacts(rest_version=rest_version),
                classification="unknown",
            )
        if type(response) is not GitHubPackagesHttpResponse:
            message = "GitHub Packages transport returned a malformed response"
            raise TypeError(message)
        exchanges.append(_exchange(stage, url, response))
        status_class = _status_classification(response)
        if status_class is not None:
            return _projection_observation(
                snapshot=snapshot,
                artifact=artifact,
                projection=projection,
                request_facts=request_facts,
                exchanges=tuple(exchanges),
                facts=_RemoteFacts(rest_version=rest_version),
                classification=status_class,
            )
        if not _response_policy_ok(
            response,
            requested_url=url,
            stage=stage,
            credentialed=True,
        ):
            return _projection_observation(
                snapshot=snapshot,
                artifact=artifact,
                projection=projection,
                request_facts=request_facts,
                exchanges=tuple(exchanges),
                facts=_RemoteFacts(rest_version=rest_version),
                classification="unprovable",
            )
        if response.status == HTTP_NOT_FOUND:
            npm_documents[stage] = None
            continue
        if response.status != HTTP_OK or not _identity_encoding(response):
            return _projection_observation(
                snapshot=snapshot,
                artifact=artifact,
                projection=projection,
                request_facts=request_facts,
                exchanges=tuple(exchanges),
                facts=_RemoteFacts(rest_version=rest_version),
                classification="unprovable",
            )
        try:
            document = _json(response, field=stage)
        except ValueError:
            return _projection_observation(
                snapshot=snapshot,
                artifact=artifact,
                projection=projection,
                request_facts=request_facts,
                exchanges=tuple(exchanges),
                facts=_RemoteFacts(rest_version=rest_version),
                classification="unprovable",
            )
        if not isinstance(document, dict):
            return _projection_observation(
                snapshot=snapshot,
                artifact=artifact,
                projection=projection,
                request_facts=request_facts,
                exchanges=tuple(exchanges),
                facts=_RemoteFacts(rest_version=rest_version),
                classification="unprovable",
            )
        npm_documents[stage] = document

    package_document = npm_documents["npm-tags"]
    exact_document = npm_documents["npm-metadata"]
    tag_version: str | None = None
    target_tag_present = False
    if package_document is not None:
        if package_document.get("name") != GITHUB_PACKAGES_PACKAGE:
            return _projection_observation(
                snapshot=snapshot,
                artifact=artifact,
                projection=projection,
                request_facts=request_facts,
                exchanges=tuple(exchanges),
                facts=_RemoteFacts(rest_version=rest_version),
                classification="conflicting",
            )
        tags = package_document.get("dist-tags")
        if not isinstance(tags, dict):
            return _projection_observation(
                snapshot=snapshot,
                artifact=artifact,
                projection=projection,
                request_facts=request_facts,
                exchanges=tuple(exchanges),
                facts=_RemoteFacts(rest_version=rest_version),
                classification="unprovable",
            )
        tag = tags.get(_target_tag(snapshot.target))
        if tag is not None and type(tag) is not str:
            return _projection_observation(
                snapshot=snapshot,
                artifact=artifact,
                projection=projection,
                request_facts=request_facts,
                exchanges=tuple(exchanges),
                facts=_RemoteFacts(rest_version=rest_version),
                classification="unprovable",
            )
        tag_version = cast("str | None", tag)
        target_tag_present = tag is not None

    npm_version: str | None = None
    repository: str | None = None
    if exact_document is not None:
        name = exact_document.get("name")
        version = exact_document.get("version")
        if name != GITHUB_PACKAGES_PACKAGE or type(version) is not str:
            return _projection_observation(
                snapshot=snapshot,
                artifact=artifact,
                projection=projection,
                request_facts=request_facts,
                exchanges=tuple(exchanges),
                facts=_RemoteFacts(
                    rest_version=rest_version,
                    tag_version=tag_version,
                    target_tag_present=target_tag_present,
                ),
                classification="conflicting",
            )
        npm_version = cast("str", version)
        if npm_version != projection.coordinate.native_version:
            return _projection_observation(
                snapshot=snapshot,
                artifact=artifact,
                projection=projection,
                request_facts=request_facts,
                exchanges=tuple(exchanges),
                facts=_RemoteFacts(
                    rest_version=rest_version,
                    npm_version=npm_version,
                    tag_version=tag_version,
                    target_tag_present=target_tag_present,
                ),
                classification="conflicting",
            )
        repository = _repository_from_metadata(exact_document)

    facts = _RemoteFacts(
        rest_version=rest_version,
        npm_version=npm_version,
        owner=rest_owner,
        repository=repository,
        tag_version=tag_version,
        target_tag_present=target_tag_present,
    )
    if rest_owner == "ambiguous" or repository == "ambiguous":
        return _projection_observation(
            snapshot=snapshot,
            artifact=artifact,
            projection=projection,
            request_facts=request_facts,
            exchanges=tuple(exchanges),
            facts=facts,
            classification="unprovable",
        )
    if rest_owner not in {None, GITHUB_PACKAGES_OWNER} or repository not in {
        None,
        GITHUB_PACKAGES_REPOSITORY,
    }:
        return _projection_observation(
            snapshot=snapshot,
            artifact=artifact,
            projection=projection,
            request_facts=request_facts,
            exchanges=tuple(exchanges),
            facts=facts,
            classification="conflicting",
        )
    if bool(rest_version) != bool(npm_version):
        return _projection_observation(
            snapshot=snapshot,
            artifact=artifact,
            projection=projection,
            request_facts=request_facts,
            exchanges=tuple(exchanges),
            facts=facts,
            classification="unknown",
        )
    if rest_version is None:
        classification = "conflicting" if target_tag_present else "absent"
        if not rest_package_absent and exact_document is None:
            classification = "conflicting" if target_tag_present else "absent"
        return _projection_observation(
            snapshot=snapshot,
            artifact=artifact,
            projection=projection,
            request_facts=request_facts,
            exchanges=tuple(exchanges),
            facts=facts,
            classification=classification,
        )
    if exact_document is None:
        return _projection_observation(
            snapshot=snapshot,
            artifact=artifact,
            projection=projection,
            request_facts=request_facts,
            exchanges=tuple(exchanges),
            facts=facts,
            classification="unknown",
        )
    dist = exact_document.get("dist")
    if not isinstance(dist, dict) or type(dist.get("tarball")) is not str:
        return _projection_observation(
            snapshot=snapshot,
            artifact=artifact,
            projection=projection,
            request_facts=request_facts,
            exchanges=tuple(exchanges),
            facts=facts,
            classification="unprovable",
        )
    tarball_url = cast("str", dist["tarball"])
    if not _allowed_url(tarball_url, stage="tarball"):
        return _projection_observation(
            snapshot=snapshot,
            artifact=artifact,
            projection=projection,
            request_facts=request_facts,
            exchanges=tuple(exchanges),
            facts=facts,
            classification="unprovable",
        )
    tarball_headers = redirect_headers(
        source_url=exact_url,
        target_url=tarball_url,
        headers=_npm_transport_headers(token, tarball=True),
    )
    credentialed_tarball = any(
        name.lower() == "authorization" for name, _value in tarball_headers
    )
    try:
        tarball_response = cast(
            "GitHubPackagesHttpResponse",
            transport.get(
                tarball_url,
                headers=tarball_headers,
                timeout=float(timeout),
                max_bytes=tarball_limit_bytes,
            ),
        )
    except GitHubPackagesPolicyError:
        exchanges.append(
            _synthetic_exchange("tarball", tarball_url, "off-policy")
        )
        return _projection_observation(
            snapshot=snapshot,
            artifact=artifact,
            projection=projection,
            request_facts=request_facts,
            exchanges=tuple(exchanges),
            facts=facts,
            classification="unprovable",
        )
    except (GitHubPackagesTimeoutError, GitHubPackagesNetworkError, OSError):
        exchanges.append(
            _synthetic_exchange("tarball", tarball_url, "network-error")
        )
        return _projection_observation(
            snapshot=snapshot,
            artifact=artifact,
            projection=projection,
            request_facts=request_facts,
            exchanges=tuple(exchanges),
            facts=facts,
            classification="unknown",
        )
    if type(tarball_response) is not GitHubPackagesHttpResponse:
        message = "GitHub Packages transport returned a malformed response"
        raise TypeError(message)
    exchanges.append(_exchange("tarball", tarball_url, tarball_response))
    status_class = _status_classification(tarball_response)
    if status_class is not None:
        return _projection_observation(
            snapshot=snapshot,
            artifact=artifact,
            projection=projection,
            request_facts=request_facts,
            exchanges=tuple(exchanges),
            facts=facts,
            classification=status_class,
        )
    if not _response_policy_ok(
        tarball_response,
        requested_url=tarball_url,
        stage="tarball",
        credentialed=credentialed_tarball,
    ):
        return _projection_observation(
            snapshot=snapshot,
            artifact=artifact,
            projection=projection,
            request_facts=request_facts,
            exchanges=tuple(exchanges),
            facts=facts,
            classification="unprovable",
        )
    if tarball_response.status != HTTP_OK or not _identity_encoding(
        tarball_response
    ):
        return _projection_observation(
            snapshot=snapshot,
            artifact=artifact,
            projection=projection,
            request_facts=request_facts,
            exchanges=tuple(exchanges),
            facts=facts,
            classification="unprovable",
        )
    remote = _remote_tarball_observation(
        tarball_response.body,
        artifact=artifact,
        expectation=expectation,
        expanded_limit_bytes=expanded_tarball_limit_bytes,
    )
    facts = _RemoteFacts(
        rest_version=rest_version,
        npm_version=npm_version,
        owner=rest_owner,
        repository=repository,
        tag_version=tag_version,
        target_tag_present=target_tag_present,
        content_sha512=remote.content_sha512,
        witness_digest=remote.witness_digest,
    )
    if remote.classification != "exact-satisfied":
        classification = remote.classification
    elif not target_tag_present:
        classification = "partial"
    elif tag_version != projection.coordinate.native_version:
        classification = "conflicting"
    else:
        classification = "exact-satisfied"
    return _projection_observation(
        snapshot=snapshot,
        artifact=artifact,
        projection=projection,
        request_facts=request_facts,
        exchanges=tuple(exchanges),
        facts=facts,
        classification=classification,
    )


def _runner_result(
    raw: object,
    *,
    command: tuple[str, ...],
    token: str,
) -> PublishCommandResult:
    if isinstance(raw, PublishCommandResult):
        exit_code = raw.exit_code
        stdout = raw.stdout
        stderr = raw.stderr
    elif isinstance(raw, dict):
        exit_code = raw.get("exit_code")
        stdout = raw.get("stdout", "")
        stderr = raw.get("stderr", "")
    else:
        exit_code = getattr(raw, "exit_code", getattr(raw, "returncode", None))
        stdout = getattr(raw, "stdout", "")
        stderr = getattr(raw, "stderr", "")
    if type(exit_code) is not int:
        message = "npm publish runner omitted an exact exit code"
        raise TypeError(message)
    if type(stdout) is not str or type(stderr) is not str:
        message = "npm publish runner output must be exact strings"
        raise TypeError(message)
    text = f"{stdout}\n{stderr}".lower()
    if exit_code == 0:
        outcome = "success"
    elif any(
        marker in text
        for marker in ("e409", "epublishconflict", "cannot publish over")
    ):
        outcome = "create-conflict"
    else:
        outcome = "failed"
    return PublishCommandResult(
        outcome=outcome,
        exit_code=exit_code,
        stdout=redact_diagnostic(stdout, secrets=(token,)),
        stderr=redact_diagnostic(stderr, secrets=(token,)),
        command=tuple(_REDACTED if item == token else item for item in command),
    )


def _write_private_npm_config(path: Path, token: str) -> None:
    flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL
    descriptor = os.open(path, flags, stat.S_IRUSR | stat.S_IWUSR)
    try:
        with os.fdopen(
            descriptor,
            "w",
            encoding="utf-8",
            newline="\n",
        ) as stream:
            stream.write(
                f"//npm.pkg.github.com/:_authToken={token}\n"
                "always-auth=true\n"
                "registry=https://npm.pkg.github.com\n"
                "ignore-scripts=true\n"
            )
    except BaseException:
        path.unlink(missing_ok=True)
        raise
    if stat.S_IMODE(path.stat().st_mode) != PRIVATE_CONFIG_MODE:
        path.unlink(missing_ok=True)
        message = "temporary npm config mode is not 0600"
        raise PermissionError(message)


def _validate_publish_preconditions(  # noqa: PLR0913
    *,
    publication_snapshot: PublicationSnapshot,
    authorization: AuthorizationRecord,
    capability_decision: CapabilityAdmissionDecision,
    action: PublicationAction,
    qualification_snapshot: QualificationSnapshot,
    qualification_decision: QualificationDecision,
    artifact: ReleaseArtifact,
    expectation: ArtifactExpectation,
) -> None:
    if (
        type(publication_snapshot) is not PublicationSnapshot
        or type(authorization) is not AuthorizationRecord
        or type(capability_decision) is not CapabilityAdmissionDecision
        or type(action) is not PublicationAction
        or type(qualification_snapshot) is not QualificationSnapshot
        or type(qualification_decision) is not QualificationDecision
        or type(artifact) is not ReleaseArtifact
        or type(expectation) is not ArtifactExpectation
    ):
        message = "publication precondition record has the wrong type"
        raise TypeError(message)
    attempt = publication_snapshot.attempt
    expected_control = f"workflow-delivery-v3:{attempt.execution.target}"
    _validate_artifact_expectation(expectation)
    actions = publication_snapshot.materialized_actions
    expected_action_digests = tuple(
        sorted(candidate.action_digest for candidate in actions)
    )
    expected_artifact_digests = tuple(
        sorted(candidate.artifact_digest for candidate in actions)
    )
    expected_resource_key_sets = tuple(
        sorted(
            (candidate.action_id, candidate.mutable_resource_keys)
            for candidate in actions
        )
    )
    expected_lock_groups = tuple(
        sorted(
            (candidate.action_id, candidate.lock_group) for candidate in actions
        )
    )
    if (
        authorization.attempt != attempt
        or authorization.control != expected_control
        or authorization.publication_snapshot_digest
        != publication_snapshot.snapshot_digest
        or capability_decision.attempt != attempt
        or capability_decision.control != expected_control
        or capability_decision.publication_snapshot_digest
        != publication_snapshot.snapshot_digest
        or capability_decision.authorization_digest
        != authorization.authorization_digest
        or not capability_decision.authorizing
        or action not in actions
        or capability_decision.action_digests != expected_action_digests
        or capability_decision.artifact_digests != expected_artifact_digests
        or capability_decision.resource_key_sets != expected_resource_key_sets
        or capability_decision.lock_groups != expected_lock_groups
        or action.projection.destination_id != GITHUB_PACKAGES_DESTINATION_ID
        or action.operation != GITHUB_PACKAGES_OPERATION
        or qualification_snapshot.subject != attempt
        or qualification_snapshot.snapshot_digest
        != publication_snapshot.qualification_snapshot_digest
        or qualification_decision.subject != attempt
        or qualification_decision.qualification_snapshot_digest
        != qualification_snapshot.snapshot_digest
        or qualification_decision.decision_digest
        != publication_snapshot.qualification_decision_digest
        or qualification_decision.terminal_result != "success"
        or qualification_decision.admitted_artifact_digests
        != tuple(
            candidate.artifact_digest
            for candidate in publication_snapshot.materialized_actions
        )
        or artifact != action.artifact
        or artifact.qualification_snapshot_digest
        != qualification_snapshot.snapshot_digest
        or artifact.artifact_digest != action.artifact_digest
        or artifact.output != action.artifact_output
        or action.projection
        not in qualification_snapshot.destination_projections
        or action.projection.output not in qualification_snapshot.outputs
        or expectation.package_name != action.projection.coordinate.package_name
        or expectation.npm_package_version
        != action.projection.coordinate.native_version
        or expectation.lifecycle_scripts != artifact.lifecycle_scripts
        or expectation.entry_allowlist != artifact.entries
        or f"sha256:{hashlib.sha256(expectation.witness_bytes).hexdigest()}"
        != artifact.witness_digest
    ):
        message = "publication precondition binding mismatch"
        raise ValueError(message)


def _validate_local_tarball_preconditions(
    *,
    tarball: Path,
    artifact: ReleaseArtifact,
    expectation: ArtifactExpectation,
    expanded_tarball_limit_bytes: int,
) -> None:
    _positive_exact_int(
        expanded_tarball_limit_bytes,
        field="expanded_tarball_limit_bytes",
    )
    if tarball.name != artifact.content.basename:
        message = "publication tarball basename binding mismatch"
        raise ValueError(message)
    try:
        status = tarball.lstat()
    except OSError as error:
        message = "publication tarball cannot be statted"
        raise ValueError(message) from error
    if not stat.S_ISREG(status.st_mode):
        message = "publication tarball must be a safe ordinary file"
        raise ValueError(message)
    content = tarball.read_bytes()
    if (
        len(content) != artifact.content.byte_size
        or status.st_size != artifact.content.byte_size
    ):
        message = "publication tarball size binding mismatch"
        raise ValueError(message)
    if f"sha256:{hashlib.sha256(content).hexdigest()}" != (
        artifact.content.content_sha256
    ):
        message = "publication tarball SHA-256 binding mismatch"
        raise ValueError(message)
    if f"sha512:{hashlib.sha512(content).hexdigest()}" != (
        artifact.content.content_sha512
    ):
        message = "publication tarball SHA-512 binding mismatch"
        raise ValueError(message)
    manifest = qualify_npm_artifact_contents(content, expectation)
    if (
        manifest.basename != artifact.content.basename
        or manifest.byte_size != artifact.content.byte_size
        or manifest.sha256 != artifact.content.content_sha256
        or manifest.sha512 != artifact.content.content_sha512
        or f"sha256:{hashlib.sha256(expectation.witness_bytes).hexdigest()}"
        != artifact.witness_digest
    ):
        message = "publication tarball packed identity binding mismatch"
        raise ValueError(message)


def classify_publish_result(
    *,
    command_outcome: str,
    post_observation: str,
    receipt: Receipt | None,
) -> PublishClassification:
    """Apply pure create-only race and uncertainty semantics."""
    if command_outcome == "created":
        if post_observation == "exact-satisfied" and receipt is not None:
            return PublishClassification(
                "success",
                "created",
                receipt.receipt_digest,
            )
        return PublishClassification("incomplete", "possibly-mutated", None)
    if command_outcome == "create-conflict":
        return PublishClassification("failed", "no-side-effect", None)
    if command_outcome == "lost-response":
        return PublishClassification("incomplete", "possibly-mutated", None)
    return PublishClassification("incomplete", "possibly-mutated", None)


def _action_result(  # noqa: PLR0913
    *,
    publication_snapshot: PublicationSnapshot,
    action: PublicationAction,
    classification: PublishClassification,
    response_identity_digest: str | None,
    receipt: Receipt | None,
    diagnostic_reference: str | None,
    control: str,
) -> ActionResult:
    attempt = publication_snapshot.attempt
    return ActionResult(
        attempt=attempt,
        publication_snapshot_digest=publication_snapshot.snapshot_digest,
        action_id=action.action_id,
        action_digest=action.action_digest,
        lock_group=action.lock_group,
        outcome=classification.outcome,
        mutation_disposition=classification.mutation_disposition,
        response_identity_digest=response_identity_digest,
        receipt=receipt,
        diagnostic_reference=diagnostic_reference,
        producer=GITHUB_PACKAGES_PUBLISHER_PRODUCER,
        control=control,
        workflow_run_id=attempt.workflow_run_id,
    )


def _response_identity(
    command: PublishCommandResult,
    observation: ProjectionObservation,
) -> str:
    return canonical_sha256(
        {
            "schema": "workflow-delivery/v3/github-packages-publish-response",
            "command": {
                "outcome": command.outcome,
                "exit-code": command.exit_code,
                "stdout": command.stdout,
                "stderr": command.stderr,
                "argv": list(command.command),
            },
            "observation-digest": observation.observation_digest,
        }
    )


def _npm_configuration_digest(
    *,
    action: PublicationAction,
    target: str,
) -> str:
    return canonical_sha256(
        {
            "schema": "workflow-delivery/v3/github-packages-npm-config",
            "registry": GITHUB_PACKAGES_REGISTRY,
            "tag": _target_tag(target),
            "ignore-scripts": True,
            "operation": action.operation,
            "coordinate": action.projection.coordinate.to_document(),
        }
    )


def preflight_github_packages_action(  # noqa: PLR0913
    *,
    tarball: Path,
    target: str,
    publication_snapshot: PublicationSnapshot,
    authorization: AuthorizationRecord,
    capability_decision: CapabilityAdmissionDecision,
    action: PublicationAction,
    qualification_snapshot: QualificationSnapshot,
    qualification_decision: QualificationDecision,
    artifact: ReleaseArtifact,
    expectation: ArtifactExpectation,
    governance_source: GovernanceSource,
    governance_client: GovernanceSourceClient,
    governance_observed_at: datetime,
    expanded_tarball_limit_bytes: int = DEFAULT_EXPANDED_TARBALL_LIMIT_BYTES,
) -> GitHubPackagesPublishPreflight:
    """Validate all authority, bytes, and npm configuration without mutation."""
    _validate_publish_preconditions(
        publication_snapshot=publication_snapshot,
        authorization=authorization,
        capability_decision=capability_decision,
        action=action,
        qualification_snapshot=qualification_snapshot,
        qualification_decision=qualification_decision,
        artifact=artifact,
        expectation=expectation,
    )
    if (
        action.artifact != artifact
        or action.projection.coordinate.channel != "buddy"
        or publication_snapshot.attempt.execution.target != target
    ):
        message = "publication precondition artifact/action target mismatch"
        raise ValueError(message)
    _validate_local_tarball_preconditions(
        tarball=tarball,
        artifact=artifact,
        expectation=expectation,
        expanded_tarball_limit_bytes=expanded_tarball_limit_bytes,
    )
    require_fresh_governance_identity(
        governance_source,
        governance_client,
        now=governance_observed_at,
        expected_provenance=capability_decision.governance_provenance,
        expected_content_sha256=(capability_decision.governance_content_sha256),
        expected_expires_at=capability_decision.governance_expires_at,
        expected_live_enabled=capability_decision.governance_live_enabled,
    )
    content = tarball.read_bytes()
    return GitHubPackagesPublishPreflight(
        attempt=publication_snapshot.attempt,
        publication_snapshot_digest=publication_snapshot.snapshot_digest,
        action_digest=action.action_digest,
        lock_group=action.lock_group,
        tarball_sha256=f"sha256:{hashlib.sha256(content).hexdigest()}",
        tarball_sha512=f"sha512:{hashlib.sha512(content).hexdigest()}",
        npm_configuration_digest=_npm_configuration_digest(
            action=action,
            target=target,
        ),
        governance_provenance=capability_decision.governance_provenance,
        governance_content_sha256=(
            capability_decision.governance_content_sha256
        ),
        governance_expires_at=capability_decision.governance_expires_at,
        governance_live_enabled=capability_decision.governance_live_enabled,
    )


def form_mutation_may_have_started_marker(
    *,
    preflight: GitHubPackagesPublishPreflight,
) -> MutationMayHaveStartedMarker:
    """Form the immutable marker persisted before npm may be invoked."""
    if type(preflight) is not GitHubPackagesPublishPreflight:
        message = "mutation marker requires an exact preflight"
        raise TypeError(message)
    return MutationMayHaveStartedMarker(
        attempt=preflight.attempt,
        publication_snapshot_digest=preflight.publication_snapshot_digest,
        action_digest=preflight.action_digest,
        lock_group=preflight.lock_group,
        preflight_digest=preflight.preflight_digest,
    )


def _admit_mutation_marker(  # noqa: PLR0913
    *,
    tarball: Path,
    target: str,
    publication_snapshot: PublicationSnapshot,
    action: PublicationAction,
    preflight: GitHubPackagesPublishPreflight,
    mutation_marker: MutationMayHaveStartedMarker,
) -> None:
    if (
        type(preflight) is not GitHubPackagesPublishPreflight
        or type(mutation_marker) is not MutationMayHaveStartedMarker
        or preflight.attempt != publication_snapshot.attempt
        or preflight.publication_snapshot_digest
        != publication_snapshot.snapshot_digest
        or preflight.action_digest != action.action_digest
        or preflight.lock_group != action.lock_group
        or preflight.npm_configuration_digest
        != _npm_configuration_digest(action=action, target=target)
        or mutation_marker.attempt != preflight.attempt
        or mutation_marker.publication_snapshot_digest
        != preflight.publication_snapshot_digest
        or mutation_marker.action_digest != preflight.action_digest
        or mutation_marker.lock_group != preflight.lock_group
        or mutation_marker.preflight_digest != preflight.preflight_digest
    ):
        message = "mutation-start marker admission failed"
        raise ValueError(message)
    content = tarball.read_bytes()
    if (
        f"sha256:{hashlib.sha256(content).hexdigest()}"
        != preflight.tarball_sha256
        or f"sha512:{hashlib.sha512(content).hexdigest()}"
        != preflight.tarball_sha512
    ):
        message = "mutation-start tarball bytes changed after preflight"
        raise ValueError(message)


def publish_github_packages_action(  # noqa: C901, PLR0912, PLR0913, PLR0915
    *,
    tarball: Path,
    target: str,
    token: str,
    runner: PublishRunner,
    temp_root: Path,
    transport: GitHubPackagesTransport | object = _MISSING,
    publication_snapshot: PublicationSnapshot | object = _MISSING,
    authorization: AuthorizationRecord | object = _MISSING,
    capability_decision: CapabilityAdmissionDecision | object = _MISSING,
    action: PublicationAction | object = _MISSING,
    qualification_snapshot: QualificationSnapshot | object = _MISSING,
    qualification_decision: QualificationDecision | object = _MISSING,
    artifact: ReleaseArtifact | object = _MISSING,
    expectation: ArtifactExpectation | object = _MISSING,
    preflight: GitHubPackagesPublishPreflight | object = _MISSING,
    mutation_marker: MutationMayHaveStartedMarker | object = _MISSING,
    governance_source: GovernanceSource | object = _MISSING,
    governance_client: GovernanceSourceClient | object = _MISSING,
    governance_observed_at: datetime | Callable[[], datetime] | object = (
        _MISSING
    ),
    defer_receipt_binding: bool = False,
    checkout_root: Path | None = None,
) -> (
    PublishCommandResult
    | PublicationExecutionResult
    | DeferredPublicationExecutionResult
):
    """Execute exactly one create-only publish and optional exact readback."""
    tag = _target_tag(target)
    if not isinstance(tarball, Path) or not isinstance(temp_root, Path):
        message = "tarball and temp_root must be Paths"
        raise TypeError(message)
    full_mode_requested = any(
        value is not _MISSING
        for value in (
            transport,
            publication_snapshot,
            authorization,
            capability_decision,
            action,
            qualification_snapshot,
            qualification_decision,
            artifact,
            expectation,
            preflight,
            mutation_marker,
            governance_source,
            governance_client,
            governance_observed_at,
        )
    )
    if not full_mode_requested or any(
        value is _MISSING or value is None
        for value in (
            transport,
            publication_snapshot,
            authorization,
            capability_decision,
            action,
            qualification_snapshot,
            qualification_decision,
            artifact,
            expectation,
            preflight,
            mutation_marker,
            governance_source,
            governance_client,
            governance_observed_at,
        )
    ):
        message = "standalone GitHub Packages mutation mode is not public"
        raise ValueError(message)
    publication_action = cast("PublicationAction", action)
    admitted_artifact = cast("ReleaseArtifact", artifact)
    _admit_mutation_marker(
        tarball=tarball,
        target=target,
        publication_snapshot=cast(
            "PublicationSnapshot",
            publication_snapshot,
        ),
        action=publication_action,
        preflight=cast("GitHubPackagesPublishPreflight", preflight),
        mutation_marker=cast(
            "MutationMayHaveStartedMarker",
            mutation_marker,
        ),
    )
    _token(token)
    temp_root.mkdir(parents=True, exist_ok=True)
    if checkout_root is not None:
        resolved_temp = temp_root.resolve()
        resolved_checkout = checkout_root.resolve()
        if resolved_temp == resolved_checkout or resolved_checkout in (
            resolved_temp.parents
        ):
            message = "temporary npm config must be outside checkout"
            raise ValueError(message)
    config_dir = Path(
        tempfile.mkdtemp(prefix="wdv3-github-packages-", dir=temp_root)
    )
    config_path = config_dir / "npmrc"
    _write_private_npm_config(config_path, token)
    argv = (
        "npm",
        "publish",
        str(tarball),
        "--registry",
        GITHUB_PACKAGES_REGISTRY,
        "--tag",
        tag,
        "--ignore-scripts",
        "--userconfig",
        str(config_path),
    )
    env = {
        "HOME": str(config_dir),
        "NPM_CONFIG_USERCONFIG": str(config_path),
        "NPM_CONFIG_IGNORE_SCRIPTS": "true",
    }
    try:
        capability = cast("CapabilityAdmissionDecision", capability_decision)
        admitted_preflight = cast(
            "GitHubPackagesPublishPreflight",
            preflight,
        )
        if (
            admitted_preflight.governance_provenance
            != capability.governance_provenance
            or admitted_preflight.governance_content_sha256
            != capability.governance_content_sha256
            or admitted_preflight.governance_expires_at
            != capability.governance_expires_at
            or admitted_preflight.governance_live_enabled
            is not capability.governance_live_enabled
        ):
            message = "Publisher Governance preflight identity mismatch"
            raise ValueError(message)
        observed_at = (
            governance_observed_at()
            if callable(governance_observed_at)
            else governance_observed_at
        )
        if type(observed_at) is not datetime:
            message = "Publisher Governance observation time is malformed"
            raise TypeError(message)
        try:
            require_fresh_governance_identity(
                cast("GovernanceSource", governance_source),
                cast("GovernanceSourceClient", governance_client),
                now=observed_at,
                expected_provenance=admitted_preflight.governance_provenance,
                expected_content_sha256=(
                    admitted_preflight.governance_content_sha256
                ),
                expected_expires_at=admitted_preflight.governance_expires_at,
                expected_live_enabled=(
                    admitted_preflight.governance_live_enabled
                ),
            )
        except GovernanceRejectionError as error:
            command = PublishCommandResult(
                outcome="governance-rejected",
                exit_code=None,
                stdout="",
                stderr="",
                command=argv,
            )
            result = DeferredPublicationExecutionResult(
                command=command,
                observation=None,
                classification=PublishClassification(
                    "failed",
                    "no-side-effect",
                    None,
                ),
                response_identity_digest=None,
                diagnostic_reference=(
                    PUBLISHER_GOVERNANCE_RECHECK_FAILED_BEFORE_RUNNER
                ),
                receipt=None,
            )
            raise PublisherGovernanceRecheckRejectionError(result) from error
        try:
            raw = runner.run(argv, env=env)
        except Exception as error:
            if not full_mode_requested:
                raise
            command = PublishCommandResult(
                outcome="lost-response",
                exit_code=None,
                stdout="",
                stderr=redact_diagnostic(str(error), secrets=(token,)),
                command=argv,
            )
        else:
            command = _runner_result(raw, command=argv, token=token)
    finally:
        config_path.unlink(missing_ok=True)
        with contextlib.suppress(OSError):
            config_dir.rmdir()
    publication = cast("PublicationSnapshot", publication_snapshot)
    if command.outcome == "success":
        observation = observe_github_packages_projection(
            cast("QualificationSnapshot", qualification_snapshot),
            cast("QualificationDecision", qualification_decision),
            cast("ReleaseArtifact", artifact),
            cast("ArtifactExpectation", expectation),
            token=token,
            transport=cast("GitHubPackagesTransport", transport),
        )
        response_identity = _response_identity(command, observation)
        receipt: Receipt | None = None
        if observation.value.classification == "exact-satisfied":
            admitted_artifact = cast("ReleaseArtifact", artifact)
            receipt = Receipt(
                attempt=publication.attempt,
                publication_snapshot_digest=publication.snapshot_digest,
                action_id=publication_action.action_id,
                action_digest=publication_action.action_digest,
                coordinate=publication_action.projection.coordinate,
                mutable_resource_keys=publication_action.mutable_resource_keys,
                lock_group=publication_action.lock_group,
                artifact_transport=admitted_artifact.transport,
                artifact_content_sha256=(
                    admitted_artifact.content.content_sha256
                ),
                artifact_content_sha512=cast(
                    "str",
                    admitted_artifact.content.content_sha512,
                ),
                witness_digest=admitted_artifact.witness_digest,
                creation_result="created",
                tag_mapping=(
                    (
                        _target_tag(publication.attempt.execution.target),
                        publication_action.projection.coordinate.native_version,
                    ),
                ),
                response_identity_digest=response_identity,
                producer=GITHUB_PACKAGES_PUBLISHER_PRODUCER,
                control=cast(
                    "CapabilityAdmissionDecision",
                    capability_decision,
                ).control,
                workflow_run_id=publication.attempt.workflow_run_id,
            )
            if defer_receipt_binding:
                classification = classify_publish_result(
                    command_outcome="created",
                    post_observation=observation.value.classification,
                    receipt=receipt,
                )
                return DeferredPublicationExecutionResult(
                    command=command,
                    observation=observation,
                    classification=classification,
                    response_identity_digest=response_identity,
                    diagnostic_reference=None,
                    receipt=receipt,
                )
        classification = classify_publish_result(
            command_outcome="created",
            post_observation=observation.value.classification,
            receipt=receipt,
        )
        if defer_receipt_binding:
            return DeferredPublicationExecutionResult(
                command=command,
                observation=observation,
                classification=classification,
                response_identity_digest=(
                    response_identity if receipt is not None else None
                ),
                diagnostic_reference=(
                    None if receipt is not None else "post-publish-not-exact"
                ),
                receipt=receipt,
            )
        return PublicationExecutionResult(
            command=command,
            observation=observation,
            action_result=_action_result(
                publication_snapshot=publication,
                action=publication_action,
                classification=classification,
                response_identity_digest=(
                    response_identity if receipt is not None else None
                ),
                receipt=receipt,
                diagnostic_reference=(
                    None if receipt is not None else "post-publish-not-exact"
                ),
                control=cast(
                    "CapabilityAdmissionDecision",
                    capability_decision,
                ).control,
            ),
        )

    command_outcome = command.outcome
    observation = observe_github_packages_projection(
        cast("QualificationSnapshot", qualification_snapshot),
        cast("QualificationDecision", qualification_decision),
        cast("ReleaseArtifact", artifact),
        cast("ArtifactExpectation", expectation),
        token=token,
        transport=cast("GitHubPackagesTransport", transport),
    )
    classification = classify_publish_result(
        command_outcome=command_outcome,
        post_observation=observation.value.classification,
        receipt=None,
    )
    if defer_receipt_binding:
        return DeferredPublicationExecutionResult(
            command=command,
            observation=observation,
            classification=classification,
            response_identity_digest=None,
            diagnostic_reference=command.outcome,
            receipt=None,
        )
    return PublicationExecutionResult(
        command=command,
        observation=observation,
        action_result=_action_result(
            publication_snapshot=publication,
            action=publication_action,
            classification=classification,
            response_identity_digest=None,
            receipt=None,
            diagnostic_reference=command.outcome,
            control=cast(
                "CapabilityAdmissionDecision",
                capability_decision,
            ).control,
        ),
    )


def validate_receipt_response_bindings(
    *,
    receipt: Receipt,
    expected_receipt: Receipt,
    expected_response_identity_digest: str,
) -> None:
    """Reject any Receipt/action/artifact/response substitution."""
    if receipt != expected_receipt:
        message = "Receipt binding mismatch"
        raise ValueError(message)
    if receipt.response_identity_digest != expected_response_identity_digest:
        message = "Receipt response identity binding mismatch"
        raise ValueError(message)


__all__ = [  # noqa: RUF022
    "DEFAULT_MAX_PAGES",
    "DEFAULT_METADATA_LIMIT_BYTES",
    "DEFAULT_TARBALL_LIMIT_BYTES",
    "DEFAULT_TIMEOUT_SECONDS",
    "DeferredPublicationExecutionResult",
    "GITHUB_PACKAGES_DESTINATION_ID",
    "GITHUB_PACKAGES_OBSERVATION_CONTRACT_ID",
    "GITHUB_PACKAGES_OPERATION",
    "GITHUB_PACKAGES_PACKAGE",
    "GITHUB_PACKAGES_REGISTRY",
    "GitHubPackagesHttpResponse",
    "GitHubPackagesNetworkError",
    "GitHubPackagesPolicyError",
    "GitHubPackagesTimeoutError",
    "GitHubPackagesTransport",
    "GitHubPackagesPublishPreflight",
    "MutationMayHaveStartedMarker",
    "ProbeClassification",
    "PublicationExecutionResult",
    "PublisherGovernanceRecheckRejectionError",
    "PublishClassification",
    "PublishCommandResult",
    "PublishRunner",
    "classify_github_packages_probe",
    "classify_publish_result",
    "classify_rest_npm_consistency",
    "github_api_headers",
    "github_package_versions_url",
    "npm_exact_metadata_url",
    "observe_github_packages_projection",
    "form_mutation_may_have_started_marker",
    "preflight_github_packages_action",
    "publish_github_packages_action",
    "redact_diagnostic",
    "redirect_headers",
    "validate_observation_bounds",
    "validate_receipt_response_bindings",
]
