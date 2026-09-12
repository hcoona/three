"""NuGet capture evidence, without an executable operator or acceptance verdict.

The caller admits tooling and its prebuilt helper, supplies existing read
capability, and owns process supervision and generation-wide budgets. This
component never builds tooling, acquires credentials, dispatches or publishes.
Its deadline rejects late completion; it does not terminate a blocked process.
Only complete, credential-free bodies returned by the transport are retained,
with selected response headers. A transport failure or credential reflection
preserves earlier exchanges, never fabricated or redacted package bytes.
"""

from __future__ import annotations

import base64
import hashlib
import math
import platform
import re
import ssl
import sys
import time
from dataclasses import dataclass
from datetime import UTC, datetime
from http import HTTPStatus
from pathlib import Path
from typing import TYPE_CHECKING
from urllib.parse import quote, unquote, urlsplit

from three_workflow_delivery_v3.adapters import nuget_github_packages as native
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
    parse_json_strict,
)

if TYPE_CHECKING:
    from collections.abc import Callable


_RESPONSE_HEADERS = frozenset(
    {
        "cache-control",
        "content-encoding",
        "content-length",
        "content-type",
        "date",
        "etag",
        "last-modified",
        "link",
        "retry-after",
        "x-github-request-id",
    }
)


def _require(condition: bool, message: str) -> None:  # noqa: FBT001
    if not condition:
        raise ValueError(message)


@dataclass(frozen=True, slots=True)
class NuGetCaptureLimits:
    """One capture's finite allowances, not a reusable execution grant."""

    requests: int
    version_pages: int
    response_bytes: int
    socket_timeout_seconds: float
    completion_timeout_seconds: float

    def __post_init__(self) -> None:
        """Reject ambiguous types and nonfinite limits before any read."""
        for value in (self.requests, self.version_pages, self.response_bytes):
            _require(type(value) is int and value > 0, "invalid capture limit")
        for value in (
            self.socket_timeout_seconds,
            self.completion_timeout_seconds,
        ):
            _require(
                type(value) in (int, float)
                and math.isfinite(value)
                and value > 0,
                "invalid capture timeout",
            )

    def to_document(self) -> dict[str, JsonValue]:
        """Describe accounting and timeout semantics explicitly."""
        return {
            "requests": self.requests,
            "versionPages": self.version_pages,
            "responseBytes": self.response_bytes,
            "socketTimeoutSeconds": self.socket_timeout_seconds,
            "completionTimeoutSeconds": self.completion_timeout_seconds,
            "deadlineSemantics": "admission-only; caller supervises process",
            "failedReadBytes": "reserved maximum including overflow sentinel",
        }


@dataclass(frozen=True, slots=True)
class NuGetCaptureRequest:
    """Caller-closed subject; stored values do not authorize execution."""

    generation: str
    label: str
    tooling_sha: str
    helper_runtime_sha256: str
    container_id: int
    version: str
    limits: NuGetCaptureLimits

    def __post_init__(self) -> None:
        """Close request shape; official NuGet owns native version grammar."""
        for value in (self.generation, self.label):
            _require(
                type(value) is str
                and re.fullmatch(r"[a-z0-9][a-z0-9-]{0,63}", value) is not None,
                "invalid capture generation or label",
            )
        for value, size in (
            (self.tooling_sha, 40),
            (self.helper_runtime_sha256, 64),
        ):
            _require(
                type(value) is str
                and re.fullmatch(rf"[0-9a-f]{{{size}}}", value) is not None,
                "capture requires exact caller tooling and helper bindings",
            )
        _require(
            type(self.container_id) is int and self.container_id > 0,
            "invalid capture container",
        )
        _require(
            type(self.version) is str and bool(self.version), "missing version"
        )
        _require(
            type(self.limits) is NuGetCaptureLimits, "missing capture limits"
        )

    def to_document(self) -> dict[str, JsonValue]:
        """Retain selected scope separately from caller admission."""
        return {
            "schema": "workflow-delivery/v3/nuget-capture-request",
            "generation": self.generation,
            "label": self.label,
            "callerToolingSha": self.tooling_sha,
            "callerHelperRuntimeSha256": self.helper_runtime_sha256,
            "containerId": self.container_id,
            "packageId": native.NUGET_PACKAGE_ID,
            "version": self.version,
            "serviceIndex": native.NUGET_SERVICE_INDEX,
            "scope": (
                "all active coordinates and object IDs; scenario archive only"
            ),
            "limits": self.limits.to_document(),
        }


class _Audit:
    def __init__(self, directory: Path, token: str) -> None:
        _require(
            type(token) is str and bool(token), "missing capture credential"
        )
        # Cover the actual API Bearer and NuGet Basic credential forms. This
        # does not claim to recognize arbitrary secrets or encodings.
        self.secrets = (
            token.encode(),
            base64.b64encode(("hcoona:" + token).encode()),
        )
        directory.mkdir(mode=0o700, parents=False, exist_ok=False)
        self.directory = directory
        self.files: dict[str, JsonValue] = {}

    def check(self, content: bytes) -> None:
        _require(
            not any(secret in content for secret in self.secrets),
            "capture evidence contains a request credential",
        )

    def write(self, name: str, content: bytes) -> None:
        self.check(content)
        with (self.directory / name).open("xb") as stream:
            stream.write(content)
        self.files[name] = {
            "sha256": hashlib.sha256(content).hexdigest(),
            "bytes": len(content),
        }

    def event(self, document: dict[str, JsonValue]) -> None:
        # Persist reservation before calling the transport. A stopped process
        # leaves an outstanding allowance, never a free retry.
        content = canonicalize(document) + b"\n"
        self.check(content)
        with (self.directory / "requests.jsonl").open("ab") as stream:
            stream.write(content)


class _CaptureTransport:
    def __init__(
        self,
        transport: native.NuGetReadTransport,
        limits: NuGetCaptureLimits,
        audit: _Audit,
        monotonic: Callable[[], float],
        container_id: int,
    ) -> None:
        self.transport = transport
        self.limits = limits
        self.audit = audit
        self.monotonic = monotonic
        self.container_id = container_id
        self.deadline = monotonic() + limits.completion_timeout_seconds
        self.requests = 0
        self.pages = 0
        self.charged_bytes = 0
        self.returned_bytes = 0
        self.responses: list[dict[str, JsonValue]] = []

    def remaining(self) -> float:
        remaining = self.deadline - self.monotonic()
        _require(
            math.isfinite(remaining) and remaining > 0,
            "capture deadline expired",
        )
        return remaining

    def counts(self) -> dict[str, JsonValue]:
        return {
            "requests": self.requests,
            "versionPages": self.pages,
            "chargedResponseBytes": self.charged_bytes,
            "returnedResponseBytes": self.returned_bytes,
        }

    def get(
        self,
        url: str,
        *,
        headers: tuple[tuple[str, str], ...],
        timeout: float,
        max_bytes: int,
    ) -> native.NuGetHttpResponse:
        remaining = self.remaining()
        self.audit.check(unquote(url).encode())
        page = urlsplit(url).path.endswith("/versions")
        _require(
            self.requests < self.limits.requests,
            "capture request limit reached",
        )
        _require(
            not page or self.pages < self.limits.version_pages,
            "capture page limit reached",
        )
        available = self.limits.response_bytes - self.charged_bytes
        # The existing transport reads max_bytes + 1 to detect overflow. Reserve
        # that byte too, so a failed read cannot exceed the declared allowance.
        bound = min(max_bytes, available - 1)
        _require(bound > 0, "capture byte limit reached")
        effective_timeout = min(
            timeout, self.limits.socket_timeout_seconds, remaining
        )
        self.requests += 1
        self.pages += int(page)
        self.charged_bytes += bound + 1
        self.audit.event(
            {
                "event": "reserved",
                "request": self.requests,
                "method": "GET",
                "url": url,
                "maximumBodyBytes": bound,
                "socketTimeoutSeconds": effective_timeout,
                "counts": self.counts(),
            }
        )
        response = self.transport.get(
            url, headers=headers, timeout=effective_timeout, max_bytes=bound
        )
        # Transport contracts are trusted for resource bounds. Returned bytes
        # still require admission, and a bad response never completes capture.
        _require(
            len(response.body) <= bound + 1, "transport exceeded reserved bytes"
        )
        self.charged_bytes -= bound + 1 - len(response.body)
        self.returned_bytes += len(response.body)
        self.audit.check(unquote(response.url).encode())
        for key, value in response.headers:
            if key.lower() == "link":
                self.audit.check(unquote(value).encode())
        name = f"response-{self.requests:03d}.body"
        record: dict[str, JsonValue] = {
            "request": self.requests,
            "requestedUrl": url,
            "responseUrl": response.url,
            "status": response.status,
            "headers": [
                [key.lower(), value]
                for key, value in response.headers
                if key.lower() in _RESPONSE_HEADERS
            ],
            "body": name,
        }
        encoded_record = canonicalize(record)
        # Check URL/header metadata before writing even a safe body. Unsafe
        # bodies fail in write(), leaving no artifact for this response.
        self.audit.check(encoded_record)
        self.audit.write(name, response.body)
        self.audit.write(f"response-{self.requests:03d}.json", encoded_record)
        self.responses.append(record)
        self.audit.event(
            {
                "event": "returned",
                "request": self.requests,
                "counts": self.counts(),
            }
        )
        self.remaining()
        _require(
            len(response.body) <= bound, "response exceeds capture byte bound"
        )
        control_url = (
            "https://api.github.com/users/hcoona/packages/nuget/"
            + quote(native.NUGET_PACKAGE_ID, safe="")
        )
        if url == control_url and response.status == HTTPStatus.OK:
            control = parse_json_strict(response.body)
            _require(
                isinstance(control, dict)
                and control.get("id") == self.container_id,
                "capture container mismatch",
            )
        return response


def _reader_runtime() -> dict[str, JsonValue]:
    return {
        "kind": "local-capture-runtime; not publication-profile admission",
        "pythonImplementation": sys.implementation.name,
        "pythonVersion": platform.python_version(),
        "pythonBuild": sys.version,
        "pythonExecutableSha256": hashlib.sha256(
            Path(sys.executable).read_bytes()
        ).hexdigest(),
        "platform": platform.system(),
        "tlsLibrary": ssl.OPENSSL_VERSION,
        "adapterSourceSha256": hashlib.sha256(
            Path(native.__file__).read_bytes()
        ).hexdigest(),
        "captureSourceSha256": hashlib.sha256(
            Path(__file__).read_bytes()
        ).hexdigest(),
    }


def capture_nuget_state(  # noqa: PLR0913
    request: NuGetCaptureRequest,
    *,
    authority: native.NuGetAuthority,
    transport: native.NuGetReadTransport,
    token: str,
    audit_directory: Path,
    monotonic: Callable[[], float] = time.monotonic,
    clock: Callable[[], datetime] = lambda: datetime.now(UTC),
) -> Path:
    """Retain one complete active capture or fail with partial private evidence.

    The caller owns actual source/helper admission and process termination. No
    source SHA or helper digest supplied here independently proves its lineage.
    The returned file reports sequential observations, never an atomic snapshot,
    an access audit, destination absence beyond that read, or suite acceptance.
    """
    document = request.to_document()
    encoded_request = canonicalize(document)
    audit = _Audit(audit_directory, token)
    audit.write("request.json", encoded_request)
    bounded = _CaptureTransport(
        transport, request.limits, audit, monotonic, request.container_id
    )
    try:
        started = clock()
        _require(
            started.utcoffset() is not None, "capture requires an aware clock"
        )
        audit.write("reader-runtime.json", canonicalize(_reader_runtime()))
        bounded.remaining()
        state = native.read_nuget_active_state(
            transport=bounded,
            authority=authority,
            token=token,
            package_id=native.NUGET_PACKAGE_ID,
            version=request.version,
        )
        _require(
            state.package_control["id"] == request.container_id,
            "capture container mismatch",
        )
        bounded.remaining()
        ended = clock()
        _require(
            ended.utcoffset() is not None and ended >= started,
            "invalid capture clock interval",
        )
        package: JsonValue = None
        if state.package is not None:
            audit.write("scenario-witness.json", state.package.witness)
            package = {
                "body": bounded.responses[-1]["body"],
                "sha256": state.package.sha256,
                "sha512": state.package.sha512,
                "witness": "scenario-witness.json",
                "witnessSha256": state.package.witness_sha256,
                "nativeFacts": state.package.official_facts,
            }
        journal = (audit_directory / "requests.jsonl").read_bytes()
        audit.files["requests.jsonl"] = {
            "sha256": hashlib.sha256(journal).hexdigest(),
            "bytes": len(journal),
        }
        capture: dict[str, JsonValue] = {
            "schema": "workflow-delivery/v3/nuget-active-capture",
            "requestDigest": canonical_sha256(document),
            "startedAt": started.isoformat(),
            "completedAt": ended.isoformat(),
            "coordinate": state.identity.coordinate,
            "resources": {
                "serviceIndexSha256": state.resources.index_sha256,
                "packageBaseAddress": state.resources.package_base_address,
                "packagePublish": state.resources.package_publish,
            },
            "packageControl": state.package_control,
            "activeCoordinates": sorted(
                identity.coordinate for identity in state.active_versions
            ),
            "githubVersions": list(state.github_versions),
            "scenarioPackage": package,
            "responses": bounded.responses,
            "counts": bounded.counts(),
            "files": audit.files,
            "evidenceLevel": (
                "sequential supplied-dependency observation; "
                "independent native audit required"
            ),
        }
        # A partial write is never exposed under the completed capture name.
        encoded_capture = canonicalize(capture)
        audit.check(encoded_capture)
        pending = audit_directory / "capture.pending"
        with pending.open("xb") as stream:
            stream.write(encoded_capture)
        bounded.remaining()
        completed = audit_directory / "capture.json"
        pending.rename(completed)
    except Exception as error:
        # Exception messages can contain request credentials or diagnostics.
        # Preserve only the class and previously retained safe responses.
        audit.write(
            "failure.json",
            canonicalize(
                {
                    "completedCapture": False,
                    "errorType": type(error).__name__,
                    "counts": bounded.counts(),
                    "returnedResponses": bounded.responses,
                }
            ),
        )
        raise
    return completed
