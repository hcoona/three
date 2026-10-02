"""Bounded Ruby registry reads and one-shot binary publication mechanisms."""

from __future__ import annotations

import base64
import http.client
import math
import platform
import re
import ssl
import subprocess
import time
from dataclasses import dataclass, field
from datetime import datetime, timedelta
from http import HTTPStatus
from typing import TYPE_CHECKING, Protocol, cast
from urllib.parse import urlsplit

from three_workflow_delivery_v3._ruby_native import (
    RUBY_RELEASE_UNIT,
    ruby_digest,
    ruby_object,
    ruby_text,
)
from three_workflow_delivery_v3._ruby_registry import (
    RUBY_INDEX_ENTRY_LIMIT,
    ruby_registry_candidates,
)
from three_workflow_delivery_v3.adapters.ruby import (
    RubyDistribution,
    inspect_ruby_distribution,
)
from three_workflow_delivery_v3.adapters.rubygems import (
    RUBY_GEM_LIMIT,
    RUBY_GITHUB_STORAGE_ORIGIN,
    RUBY_HTTP_HEADER_LIMIT,
    RUBY_HTTP_RESPONSE_LIMIT,
    RUBY_HTTP_TIMEOUT,
    RUBY_INDEX_LIMIT,
    RUBY_LOCATION_LIMIT,
    RUBY_REGISTRY_REQUEST_LIMIT,
    RubyRegistry,
)
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
    parse_json_strict,
)

if TYPE_CHECKING:
    from collections.abc import Callable

_MIN_HTTP_STATUS = 100
_MAX_HTTP_STATUS = 599
_ASCII_SPACE = 0x20
_ASCII_DELETE = 0x7F

# Order matters: RemoteDisconnected belongs to both HTTPException and OSError.
RUBY_OBSERVATION_FAILURES = {
    "HTTPException": http.client.HTTPException,
    "OSError": OSError,
    "SubprocessError": subprocess.SubprocessError,
    "ValueError": ValueError,
    "TypeError": TypeError,
}


def _failure_kind(error: Exception) -> str:
    for name, family in RUBY_OBSERVATION_FAILURES.items():
        if isinstance(error, family):
            return name
    message = "unsupported Ruby observation failure family"
    raise TypeError(message)


@dataclass(frozen=True, slots=True)
class RubyHttpResponse:
    """One bounded response; credential responses must never be retained."""

    status: int
    body: bytes = field(repr=False)
    content_type: str = field(default="", repr=False)
    location: str | None = field(default=None, repr=False, compare=False)
    location_invalid: bool = field(default=False, repr=False, compare=False)


class RubyHttpTransport(Protocol):
    """One request per invocation, without redirect, proxy or retry."""

    def request(
        self,
        method: str,
        url: str,
        headers: dict[str, str],
        body: bytes | None,
        maximum_bytes: int,
    ) -> RubyHttpResponse:
        """Return the complete bounded response or fail without resending."""
        ...


class RubyHttpsTransport:
    """Pinned standard-library TLS; no native gem push or ambient config."""

    def __init__(self) -> None:
        """Bind the actual HTTP runtime to the frozen profile."""
        if (
            platform.python_version() != "3.14.3"
            or ssl.OPENSSL_VERSION != "OpenSSL 3.5.5 27 Jan 2026"
        ):
            message = "Ruby HTTP runtime differs from the admitted profile"
            raise ValueError(message)

    def request(
        self,
        method: str,
        url: str,
        headers: dict[str, str],
        body: bytes | None,
        maximum_bytes: int,
    ) -> RubyHttpResponse:
        """Read at most the selected budget, with verified TLS, exactly once."""
        parsed = urlsplit(url)
        if (
            method not in {"GET", "POST"}
            or parsed.scheme != "https"
            or not parsed.hostname
            or parsed.username is not None
            or parsed.password is not None
            or parsed.port not in {None, 443}
            or parsed.fragment
            or any(c in url for c in "\r\n\\")
            or type(maximum_bytes) is not int
            or not 0 < maximum_bytes <= RUBY_INDEX_LIMIT
        ):
            message = "invalid bounded Ruby HTTPS request"
            raise ValueError(message)
        context = ssl.create_default_context()
        context.minimum_version = ssl.TLSVersion.TLSv1_2
        connection = http.client.HTTPSConnection(
            parsed.hostname, 443, timeout=RUBY_HTTP_TIMEOUT, context=context
        )
        try:
            path = parsed.path + ("?" + parsed.query if parsed.query else "")
            connection.request(method, path, body=body, headers=headers)
            response = connection.getresponse()
            received_headers = response.getheaders()
            if (
                sum(
                    len(name.encode("latin-1"))
                    + len(value.encode("latin-1"))
                    + 4
                    for name, value in received_headers
                )
                > RUBY_HTTP_HEADER_LIMIT
            ):
                message = "Ruby HTTP headers exceed their accepted byte budget"
                raise ValueError(message)
            locations = [
                value
                for name, value in received_headers
                if name.lower() == "location"
            ]
            invalid_location = len(locations) > 1 or any(
                not value or len(value.encode("latin-1")) > RUBY_LOCATION_LIMIT
                for value in locations
            )
            location = (
                locations[0]
                if len(locations) == 1 and not invalid_location
                else None
            )
            content = response.read(maximum_bytes + 1)
            if len(content) > maximum_bytes:
                message = "Ruby HTTP response exceeds its byte budget"
                raise ValueError(message)
            return RubyHttpResponse(
                response.status,
                content,
                response.getheader("Content-Type", ""),
                location,
                invalid_location,
            )
        finally:
            connection.close()


class RubyRequestBudget:
    """One operation's cumulative budget, including failed requests."""

    def __init__(self, transport: RubyHttpTransport) -> None:
        """Wrap the caller's single transport for the entire operation."""
        self.transport = transport
        self.used = 0

    def request(
        self,
        method: str,
        url: str,
        headers: dict[str, str],
        body: bytes | None,
        maximum_bytes: int,
    ) -> RubyHttpResponse:
        """Consume before sending; failures never refund a request."""
        if self.used >= RUBY_REGISTRY_REQUEST_LIMIT:
            message = "Ruby registry request budget exhausted"
            raise ValueError(message)
        self.used += 1
        response = self.transport.request(
            method, url, headers, body, maximum_bytes
        )
        if (
            type(response.status) is not int
            or not _MIN_HTTP_STATUS <= response.status <= _MAX_HTTP_STATUS
            or type(response.body) is not bytes
            or len(response.body) > maximum_bytes
            or not isinstance(response.content_type, str)
            or (
                response.location is not None
                and (
                    not isinstance(response.location, str)
                    or len(response.location.encode()) > RUBY_LOCATION_LIMIT
                )
            )
            or type(response.location_invalid) is not bool
        ):
            message = "invalid or excessive Ruby HTTP response"
            raise ValueError(message)
        return response


def _secret(value: str) -> str:
    if (
        not isinstance(value, str)
        or not value
        or any(c.isspace() for c in value)
    ):
        message = "Ruby credential is absent or malformed"
        raise ValueError(message)
    return value


def _json(response: RubyResponseEvidence) -> JsonValue:
    if (
        not isinstance(response, RubyHttpResponse)
        or response.location is not None
        or response.location_invalid
        or response.status != HTTPStatus.OK
        or response.content_type.split(";", 1)[0].strip() != "application/json"
    ):
        message = (
            "Ruby registry metadata response is unavailable or unsupported"
        )
        raise ValueError(message)
    return parse_json_strict(response.body)


_SAFE_MEDIA = frozenset(
    {
        "",
        "application/json",
        "application/octet-stream",
        "application/gzip",
        "application/x-gzip",
        "text/plain",
        "text/html",
        "other",
    }
)


def ruby_safe_media(value: str) -> str:
    """Keep a closed classification, never arbitrary reflected header text."""
    media = value.split(";", 1)[0].strip().lower()
    return media if media in _SAFE_MEDIA else "other"


@dataclass(frozen=True, slots=True)
class RubyScreenedResponse:
    """Retained digest evidence whose suppressed body is not an original."""

    status: int
    content_type: str
    body_size: int
    body_digest: str
    location_digest: str | None
    reason: str
    safe_origin: str | None = None
    safe_path: str | None = None


RubyResponseEvidence = RubyHttpResponse | RubyScreenedResponse
RubyResponseEntry = tuple[
    str | dict[str, JsonValue], RubyResponseEvidence | None
]


def ruby_storage_path(origin: str, path: str, version: str) -> None:
    """Validate the same exact safe routing facts in live and offline reads."""
    prefix = "/rubygemsregistryv2prod/blobs/712433/" + RUBY_RELEASE_UNIT
    pattern = re.escape(prefix + "/" + version + "/") + (
        r"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}"
    )
    if (
        not re.fullmatch(r"[0-9]+(?:\.[0-9A-Za-z]+)*", version)
        or origin != RUBY_GITHUB_STORAGE_ORIGIN
        or not re.fullmatch(pattern, path)
    ):
        message = "Ruby storage route differs from the exact coordinate"
        raise ValueError(message)


def ruby_storage_location(
    location: str | None, version: str
) -> tuple[str, str]:
    """Admit one opaque signed URI without normalization or capability echo."""
    message = "Ruby storage Location is unsupported"
    if (
        not isinstance(location, str)
        or not location
        or len(location) > RUBY_LOCATION_LIMIT
        or not location.isascii()
        or any(
            ord(c) <= _ASCII_SPACE or ord(c) == _ASCII_DELETE for c in location
        )
        or "\\" in location
        or "#" in location
    ):
        raise ValueError(message)
    try:
        parsed = urlsplit(location)
        host = RUBY_GITHUB_STORAGE_ORIGIN.removeprefix("https://")
        valid = (
            parsed.scheme == "https"
            and parsed.netloc in {host, host + ":443"}
            and bool(parsed.query)
            and "%" not in parsed.path
        )
    except ValueError:
        raise ValueError(message) from None
    if not valid:
        raise ValueError(message)
    ruby_storage_path(RUBY_GITHUB_STORAGE_ORIGIN, parsed.path, version)
    return RUBY_GITHUB_STORAGE_ORIGIN, parsed.path


def ruby_screen_response(
    response: RubyResponseEvidence,
    *,
    reason: str | None = None,
    route: tuple[str, str] | None = None,
) -> RubyResponseEvidence:
    """Remove capability-bearing material before any evidence append."""
    if isinstance(response, RubyScreenedResponse):
        return response
    if reason is None:
        if response.location_invalid:
            reason = "invalid-location"
        elif (
            HTTPStatus.MULTIPLE_CHOICES
            <= response.status
            < HTTPStatus.BAD_REQUEST
        ):
            reason = "redirect"
        elif response.location is not None:
            reason = "unexpected-location"
        else:
            return RubyHttpResponse(
                response.status,
                response.body,
                ruby_safe_media(response.content_type),
            )
    return RubyScreenedResponse(
        response.status,
        ruby_safe_media(response.content_type),
        len(response.body),
        ruby_digest(response.body),
        None
        if response.location is None
        else ruby_digest(response.location.encode()),
        reason,
        None if route is None else route[0],
        None if route is None else route[1],
    )


def ruby_upload_accepted(response: RubyResponseEvidence) -> bool:
    """Only an original HTTP 200 without Location admits visibility reads."""
    return (
        isinstance(response, RubyHttpResponse)
        and response.status == HTTPStatus.OK
        and response.location is None
        and not response.location_invalid
    )


def ruby_response_document(
    response: RubyResponseEvidence,
) -> dict[str, JsonValue]:
    """Serialize the explicit safe evidence arm, never an ephemeral Location."""
    response = ruby_screen_response(response)
    if isinstance(response, RubyScreenedResponse):
        return {
            "kind": "screened",
            "status": response.status,
            "content-type": response.content_type,
            "body-size": response.body_size,
            "body-digest": response.body_digest,
            "location-digest": response.location_digest,
            "reason": response.reason,
            "safe-origin": response.safe_origin,
            "safe-path": response.safe_path,
        }
    return {
        "kind": "original",
        "status": response.status,
        "content-type": response.content_type,
        "body-base64": base64.b64encode(response.body).decode("ascii"),
        "body-digest": ruby_digest(response.body),
    }


def ruby_continuation_request(
    response: RubyScreenedResponse,
) -> dict[str, JsonValue]:
    """Join one storage request to its screened, attested redirect response."""
    return {
        "method": "GET",
        "origin": response.safe_origin,
        "path": response.safe_path,
        "credentials": "none",
        "location-digest": response.location_digest,
        "redirect-response-digest": canonical_sha256(
            ruby_response_document(response)
        ),
    }


@dataclass(frozen=True, slots=True)
class RubyRegistryObservation:
    """Exact coordinate state with original bounded responses and bytes."""

    registry: RubyRegistry
    version: str
    classification: str
    responses: tuple[RubyResponseEntry, ...]
    distribution: RubyDistribution | None = None
    failure_kind: str | None = None
    failure_stage: str | None = None

    def to_document(self) -> dict[str, JsonValue]:
        """Retain replayable public/index evidence, excluding credentials."""
        return {
            "schema": "workflow-delivery/v3/ruby-registry-observation-v2",
            "registry": self.registry.name,
            "version": self.version,
            "classification": self.classification,
            "failure-kind": self.failure_kind,
            "failure-stage": self.failure_stage,
            "responses": [
                {
                    **(
                        {"url": request}
                        if isinstance(request, str)
                        else {"request": request}
                    ),
                    "response": None
                    if response is None
                    else ruby_response_document(response),
                }
                for request, response in self.responses
            ],
            "artifact-digest": None
            if self.distribution is None
            else self.distribution.digest,
        }


class RubyRegistryReader:
    """Destination-specific reads sharing one operation's request budget."""

    def __init__(
        self,
        registry: RubyRegistry,
        budget: RubyRequestBudget,
        *,
        github_read_token: str | None = None,
    ) -> None:
        """Close read authentication without putting secrets in URLs."""
        self.registry = registry
        self.budget = budget
        self._headers: dict[str, str] = {}
        if registry.name == "github-packages":
            if github_read_token is None:
                message = (
                    "GitHub Ruby inventory requires package-read capability"
                )
                raise ValueError(message)
            credential = base64.b64encode(
                ("hcoona:" + _secret(github_read_token)).encode("utf-8")
            ).decode("ascii")
            self._headers["Authorization"] = "Basic " + credential
        elif github_read_token is not None:
            message = "GitHub credentials cannot cross into RubyGems.org"
            raise ValueError(message)

    def observe(self, expected: RubyDistribution) -> RubyRegistryObservation:
        """Resolve native equality and compare the downloaded original gem."""
        responses: list[RubyResponseEntry] = []
        self._failure_stage = "interpretation"
        try:
            return self._observe(expected, responses)
        except (
            OSError,
            ValueError,
            TypeError,
            subprocess.SubprocessError,
            http.client.HTTPException,
        ) as error:
            return RubyRegistryObservation(
                self.registry,
                expected.witness.nbgv.native_version,
                "unknown",
                tuple(responses),
                failure_kind=_failure_kind(error),
                failure_stage=self._failure_stage,
            )

    def _observe(
        self,
        expected: RubyDistribution,
        responses: list[RubyResponseEntry],
    ) -> RubyRegistryObservation:
        version = expected.witness.nbgv.native_version
        if self.registry.name == "github-packages":
            candidates = self._github_candidates(version, responses)
        else:
            candidates = self._rubygems_candidates(version, responses)
        classification = "missing"
        distribution = None
        if candidates:
            if len(candidates) != 1 or candidates[0] != {
                "name": RUBY_RELEASE_UNIT,
                "version": version,
                "platform": "ruby",
            }:
                classification = "conflicting"
            else:
                distribution = self._download(expected, responses)
                classification = (
                    "exact" if distribution is not None else "conflicting"
                )
        return RubyRegistryObservation(
            self.registry,
            version,
            classification,
            tuple(responses),
            distribution,
        )

    def _get(
        self,
        url: str,
        limit: int,
        responses: list[RubyResponseEntry],
    ) -> RubyResponseEvidence:
        self._failure_stage = "request"
        response = self.budget.request(
            "GET", url, dict(self._headers), None, limit
        )
        responses.append((url, ruby_screen_response(response)))
        self._failure_stage = "interpretation"
        return response

    def _github_candidates(
        self, version: str, responses: list[RubyResponseEntry]
    ) -> tuple[dict[str, JsonValue], ...]:
        candidates: list[dict[str, JsonValue]] = []
        for index in ("specs.4.8.gz", "prerelease_specs.4.8.gz"):
            response = self._get(
                self.registry.origin + "/" + index, RUBY_INDEX_LIMIT, responses
            )
            if (
                not isinstance(response, RubyHttpResponse)
                or response.location is not None
                or response.location_invalid
                or response.status != HTTPStatus.OK
            ):
                message = "GitHub Ruby native inventory is unavailable"
                raise ValueError(message)
            candidates.extend(
                ruby_registry_candidates(version, marshal=response.body)
            )
        return tuple(candidates)

    def _rubygems_candidates(
        self, version: str, responses: list[RubyResponseEntry]
    ) -> tuple[dict[str, JsonValue], ...]:
        response = self._get(
            f"{self.registry.origin}/api/v1/versions/{RUBY_RELEASE_UNIT}.json",
            RUBY_INDEX_LIMIT,
            responses,
        )
        if isinstance(response, RubyScreenedResponse) or (
            response.location is not None or response.location_invalid
        ):
            message = "RubyGems inventory cannot redirect"
            raise ValueError(message)
        if response.status == HTTPStatus.NOT_FOUND:
            return ()
        inventory = _json(response)
        if (
            not isinstance(inventory, list)
            or len(inventory) > RUBY_INDEX_ENTRY_LIMIT
        ):
            message = "RubyGems version inventory is invalid or excessive"
            raise ValueError(message)
        entries: list[JsonValue] = []
        for item in inventory:
            row = ruby_object(item)
            entries.append(
                [
                    RUBY_RELEASE_UNIT,
                    ruby_text(row.get("number")),
                    ruby_text(row.get("platform")),
                ]
            )
        return ruby_registry_candidates(version, entries=entries)

    def _download(
        self,
        expected: RubyDistribution,
        responses: list[RubyResponseEntry],
    ) -> RubyDistribution | None:
        url = f"{self.registry.origin}/gems/{expected.filename}"
        metadata_sha = None
        if self.registry.name == "rubygems":
            metadata_url = (
                f"{self.registry.origin}/api/v2/rubygems/{RUBY_RELEASE_UNIT}/versions/"
                f"{expected.witness.nbgv.native_version}.json?platform=ruby"
            )
            metadata = ruby_object(
                _json(
                    self._get(metadata_url, RUBY_HTTP_RESPONSE_LIMIT, responses)
                )
            )
            metadata_sha = ruby_text(metadata.get("sha"))
            if (
                metadata.get("name") != RUBY_RELEASE_UNIT
                or metadata.get("version")
                != expected.witness.nbgv.native_version
                or metadata.get("platform") != "ruby"
                or metadata.get("yanked") is not False
                or metadata.get("gem_uri") != url
                or not re.fullmatch(r"[0-9a-f]{64}", metadata_sha)
            ):
                message = "RubyGems metadata has conflicting identity or state"
                raise ValueError(message)
        response = self._get(url, RUBY_GEM_LIMIT, responses)
        if (
            self.registry.name == "github-packages"
            and response.status == HTTPStatus.FOUND
        ):
            return self._continue_download(expected, responses, response)
        if (
            not isinstance(response, RubyHttpResponse)
            or response.location is not None
            or response.location_invalid
            or response.status != HTTPStatus.OK
        ):
            message = "Ruby registry original gem download failed"
            raise ValueError(message)
        if (
            metadata_sha is not None
            and ruby_digest(response.body) != "sha256:" + metadata_sha
        ):
            message = "RubyGems original gem contradicts its metadata digest"
            raise ValueError(message)
        if response.body != expected.content:
            return None
        return inspect_ruby_distribution(
            expected.filename, response.body, expected.witness
        )

    def _continue_download(
        self,
        expected: RubyDistribution,
        responses: list[RubyResponseEntry],
        response: RubyResponseEvidence,
    ) -> RubyDistribution | None:
        if (
            not isinstance(response, RubyHttpResponse)
            or response.location_invalid
        ):
            message = "Ruby storage redirect is unavailable"
            raise ValueError(message)
        route = ruby_storage_location(
            response.location, expected.witness.nbgv.native_version
        )
        screened = cast(
            "RubyScreenedResponse", ruby_screen_response(response, route=route)
        )
        responses[-1] = (responses[-1][0], screened)
        logical = ruby_continuation_request(screened)
        self._failure_stage = "request"
        responses.append((logical, None))
        storage = self.budget.request(
            "GET", cast("str", response.location), {}, None, RUBY_GEM_LIMIT
        )
        self._failure_stage = "interpretation"
        # Retain a safe failure before parsing; replace only verified bytes.
        responses[-1] = (
            logical,
            ruby_screen_response(storage, reason="storage-unverified"),
        )
        if (
            storage.status != HTTPStatus.OK
            or storage.location is not None
            or storage.location_invalid
        ):
            responses[-1] = (
                logical,
                ruby_screen_response(storage, reason="storage-error"),
            )
            message = "Ruby storage original gem download failed"
            raise ValueError(message)
        if storage.body != expected.content:
            responses[-1] = (
                logical,
                ruby_screen_response(storage, reason="storage-mismatch"),
            )
            return None
        distribution = inspect_ruby_distribution(
            expected.filename, storage.body, expected.witness
        )
        responses[-1] = (logical, ruby_screen_response(storage))
        return distribution


@dataclass(frozen=True, slots=True)
class RubyPublishingCredential:
    """Ephemeral exchange result, never an artifact or scope attestation."""

    value: str = field(repr=False)
    expires_at: datetime


class RubyRegistryWriter:
    """At most one exchange and upload for the caller's authorized lifetime."""

    def __init__(
        self, registry: RubyRegistry, budget: RubyRequestBudget
    ) -> None:
        """Retain spent state even when a request raises or is rejected."""
        self.registry = registry
        self.budget = budget
        self._exchange_spent = False
        self._upload_spent = False
        self._issued_credential: RubyPublishingCredential | None = None
        self._exchange_receipt: dict[str, JsonValue] | None = None

    @property
    def exchange_receipt(self) -> dict[str, JsonValue] | None:
        """Return a credential-free snapshot of the first exchange boundary."""
        return (
            None
            if self._exchange_receipt is None
            else dict(self._exchange_receipt)
        )

    def exchange(  # noqa: PLR0915 - ordered one-shot exchange and screened receipt
        self,
        assertion: str,
        *,
        now: datetime,
        bootstrap: bool = False,  # noqa: ARG002 - shared response contract
        monotonic: Callable[[], float] = time.monotonic,
    ) -> RubyPublishingCredential:
        """Exchange once without retaining credential-bearing bodies."""
        if self.registry.name != "rubygems" or self._exchange_spent:
            message = "Ruby token exchange is unsupported or already spent"
            raise ValueError(message)
        if now.tzinfo is None:
            message = "Ruby token validation requires an aware current time"
            raise ValueError(message)
        assertion = _secret(assertion)
        started = monotonic()
        if (
            type(started) not in {int, float}
            or not math.isfinite(started)
            or started < 0
        ):
            message = "Ruby token exchange requires a finite monotonic clock"
            raise ValueError(message)
        self._exchange_spent = True
        receipt: dict[str, JsonValue] = {
            "schema": "workflow-delivery/v3/ruby-exchange-receipt-v1",
            "started-at": now.isoformat(),
            "elapsed-ms": None,
            "response-received": False,
            "http-status": None,
            "result": "request-failed",
        }
        self._exchange_receipt = receipt
        response = self.budget.request(
            "POST",
            self.registry.origin
            + "/api/v1/oidc/trusted_publisher/exchange_token",
            {"Content-Type": "application/json", "Accept": "application/json"},
            canonicalize({"jwt": assertion}),
            RUBY_HTTP_RESPONSE_LIMIT,
        )
        # The budget validates status before returning a complete response.
        receipt["response-received"] = True
        receipt["http-status"] = response.status
        receipt["result"] = "clock-invalid"
        completed = monotonic()
        if (
            type(completed) not in {int, float}
            or not math.isfinite(completed)
            or completed < started
        ):
            message = "Ruby token exchange clock regressed or is not finite"
            raise ValueError(message)
        elapsed = completed - started
        if elapsed <= RUBY_HTTP_TIMEOUT:
            receipt["elapsed-ms"] = int(elapsed * 1000)
        received_at = now + timedelta(seconds=completed - started)
        receipt["result"] = "http-invalid"
        if (
            response.location is not None
            or response.location_invalid
            or response.status != HTTPStatus.CREATED
            or response.content_type.split(";", 1)[0].strip()
            != "application/json"
        ):
            message = "RubyGems token exchange failed"
            raise ValueError(message)
        receipt["result"] = "json-invalid"
        document = ruby_object(parse_json_strict(response.body))
        receipt["result"] = "name-invalid"
        ruby_text(document.get("name"))
        receipt["result"] = "expiry-invalid"
        expires = datetime.fromisoformat(ruby_text(document.get("expires_at")))
        if (
            expires.tzinfo is None
            or not received_at < expires <= received_at + timedelta(minutes=15)
        ):
            message = (
                "RubyGems token response differs from the bounded publisher"
            )
            raise ValueError(message)
        receipt["result"] = "scope-invalid"
        if document.get("scopes") != ["push_rubygem"]:
            message = (
                "RubyGems token response differs from the bounded publisher"
            )
            raise ValueError(message)
        receipt["result"] = "gem-invalid"
        if (
            "gem" in document
            and ruby_object(document["gem"]).get("name") != RUBY_RELEASE_UNIT
        ):
            message = (
                "RubyGems token response differs from the bounded publisher"
            )
            raise ValueError(message)
        receipt["result"] = "credential-invalid"
        credential = RubyPublishingCredential(
            _secret(ruby_text(document.get("rubygems_api_key"))), expires
        )
        self._issued_credential = credential
        receipt["result"] = "accepted"
        return credential

    def upload(
        self,
        distribution: RubyDistribution,
        credential: RubyPublishingCredential | str,
        *,
        now: datetime,
    ) -> RubyHttpResponse:
        """Send original bytes once; Release interprets the status."""
        if self._upload_spent:
            message = "Ruby upload lifetime is already spent"
            raise ValueError(message)
        if self.registry.name == "rubygems":
            if (
                type(credential) is not RubyPublishingCredential
                or credential is not self._issued_credential
                or now.tzinfo is None
                or now >= credential.expires_at
            ):
                message = "Ruby upload requires a current exchanged credential"
                raise ValueError(message)
            token = credential.value
        else:
            if type(credential) is not str:
                message = "GitHub Ruby upload requires its job token"
                raise ValueError(message)
            token = credential
        token = _secret(token)
        if not 0 < len(distribution.content) <= RUBY_GEM_LIMIT:
            message = "Ruby upload original exceeds its byte budget"
            raise ValueError(message)
        self._upload_spent = True
        return self.budget.request(
            "POST",
            self.registry.upload_url,
            {
                "Authorization": token,
                "Content-Type": "application/octet-stream",
            },
            distribution.content,
            RUBY_HTTP_RESPONSE_LIMIT,
        )
