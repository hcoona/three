"""Bounded original-byte GitHub IO for the local NuGet acceptance operator.

This boundary does not discover credentials or authorize operations. The caller
must admit the concrete request, existing capability and redirect policy.
Dispatch is never replayed; artifact retrieval permits one unauthenticated hop.
"""

from __future__ import annotations

import http.client
import math
import os
import re
import ssl
import time
from dataclasses import dataclass
from http import HTTPStatus
from typing import TYPE_CHECKING, cast
from urllib.parse import urlsplit

from three_workflow_delivery_v3.acceptance import nuget_operator as process
from three_workflow_delivery_v3.acceptance.nuget_evidence import (
    _object,
    _read,
    _require,
    _sha,
)
from three_workflow_delivery_v3.adapters.nuget_github_packages import (
    read_location_secrets,
    read_redirect_origin,
)
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonicalize,
    parse_canonical_json,
    parse_json_strict,
)

if TYPE_CHECKING:
    from multiprocessing.connection import Connection
    from pathlib import Path

_API = "https://api.github.com"
_REPOSITORY = "/repos/hcoona/three"
_REQUEST_LIMIT = 65536
_ARTIFACT_REDIRECT_POLICY = "github-api-location-v1"


@dataclass(frozen=True)
class NuGetGitHubLimits:
    """Cumulative HTTP bounds, separate from native capture/consumer budgets."""

    requests: int
    response_body_bytes: int
    metadata_bytes: int
    artifact_bytes: int
    call_timeout_seconds: float
    polls_per_probe: int
    poll_interval_seconds: float
    artifact_redirect_policy: str

    def __post_init__(self) -> None:
        """Reject incomplete or nonfinite bounds before any network effect."""
        for value in (
            self.requests,
            self.response_body_bytes,
            self.metadata_bytes,
            self.artifact_bytes,
            self.polls_per_probe,
        ):
            _require(
                type(value) is int and value > 0, "invalid GitHub allowance"
            )
        for value in (self.call_timeout_seconds, self.poll_interval_seconds):
            _require(
                type(value) in (int, float)
                and math.isfinite(value)
                and value > 0,
                "invalid GitHub deadline",
            )
        _require(
            self.artifact_redirect_policy == _ARTIFACT_REDIRECT_POLICY,
            "invalid GitHub artifact redirect policy",
        )

    def to_document(self) -> dict[str, JsonValue]:
        """State what is bounded, including overflow and redirect requests."""
        return {
            "requests": self.requests,
            "responseBodyBytes": self.response_body_bytes,
            "metadataBytesPerResponse": self.metadata_bytes,
            "artifactBytesPerResponse": self.artifact_bytes,
            "callTimeoutSeconds": self.call_timeout_seconds,
            "pollsPerProbe": self.polls_per_probe,
            "pollIntervalSeconds": self.poll_interval_seconds,
            "artifactRedirectPolicy": self.artifact_redirect_policy,
            "dispatchRedirects": 0,
            "artifactRedirects": 1,
            "retries": 0,
            "overflowBytesPerResponse": 1,
            "terminationGraceSeconds": 5,
        }


@dataclass(frozen=True)
class _Request:
    method: str
    route: str
    body: bytes | None
    maximum_bytes: int
    download: bool
    artifact_id: int | None
    call: int


def _exchange(  # noqa: PLR0913
    url: str,
    method: str,
    body: bytes | None,
    *,
    token: str | None,
    timeout: float,
    maximum_bytes: int,
    evidence: process._Evidence,
    label: str,
) -> tuple[int, bytes, str | None, int]:
    """Retain safe evidence and account for the original response bytes."""
    parsed = urlsplit(url)
    actor = url == _API + "/user" and method == "GET"
    headers = {
        "Accept": "application/vnd.github+json"
        if token
        else "application/octet-stream",
        "Accept-Encoding": "identity",
        "User-Agent": "workflow-delivery-v3-nuget-acceptance",
    }
    if token is not None:
        headers["Authorization"] = "Bearer " + token
        headers["X-GitHub-Api-Version"] = "2022-11-28"
    if body is not None:
        headers["Content-Type"] = "application/json"
    connection = http.client.HTTPSConnection(
        cast("str", parsed.hostname),
        timeout=timeout,
        context=ssl.create_default_context(),
    )
    connection.set_debuglevel(0)
    connection.auto_open = 0
    try:
        connection.connect()
        target = parsed.path or "/"
        if "?" in url:
            target += "?" + parsed.query
        connection.request(
            method, target, body=body, headers=headers, encode_chunked=False
        )
        with connection.getresponse() as response:
            locations = response.headers.get_all("Location", [])
            _require(len(locations) <= 1, "ambiguous artifact redirect")
            location = locations[0] if locations else None
            if location:
                # A redirect is a temporary capability. Never retain its text,
                # including reflection in a response body.
                evidence.check(location.encode())
                evidence.forbidden += (location.encode(),)
            content = response.read(maximum_bytes + 1)
            read_bytes = len(content)
            omit_body = bool(locations) or actor
            if not omit_body:
                evidence.write(label + ".body", content[:maximum_bytes])
            evidence.write(
                label + ".json",
                canonicalize(
                    {
                        "status": response.status,
                        "bodyBytesRead": len(content),
                        "bodySha256": _sha(content),
                        "bodyComplete": len(content) <= maximum_bytes,
                        "bodyRetention": "omitted" if omit_body else "original",
                        "bodyOmissionReason": "location"
                        if locations
                        else "authenticated-actor"
                        if actor
                        else None,
                        "contentLength": response.getheader("Content-Length"),
                        "contentEncoding": response.getheader(
                            "Content-Encoding"
                        ),
                        "locationSha256": _sha(location.encode())
                        if location
                        else None,
                    }
                ),
            )
            _require(
                len(content) <= maximum_bytes,
                "GitHub response byte limit exceeded",
            )
            _require(
                response.getheader("Content-Encoding") in (None, "identity"),
                "unexpected GitHub response encoding",
            )
            length = response.getheader("Content-Length")
            _require(
                length is None or length == str(len(content)),
                "incomplete GitHub response body",
            )
            if actor:
                _require(
                    response.status == HTTPStatus.OK and not locations,
                    "invalid actor response",
                )
                user = _object(parse_json_strict(content))
                _require(
                    type(user.get("id")) is int
                    and user["id"] > 0
                    and isinstance(user.get("login"), str)
                    and bool(user["login"]),
                    "invalid actor identity",
                )
                content = canonicalize(
                    {"id": user["id"], "login": user["login"]}
                )
                evidence.write(label + ".body", content)
                evidence.write(
                    "actor-identity.json",
                    canonicalize(
                        {
                            "evidenceKind": "derived-identity",
                            "endpoint": "/user",
                            "fields": ["id", "login"],
                            "bodyFile": label + ".body",
                            "bodySha256": _sha(content),
                        }
                    ),
                )
            return response.status, content, location, read_bytes
    finally:
        connection.close()


def _worker(  # noqa: PLR0913, PLR0917
    control: Connection,
    request: _Request,
    limits: NuGetGitHubLimits,
    directory: Path,
    token: str,
    timeout: float,
) -> None:
    os.setsid()
    control.send("ready")
    _require(control.recv() == "start", "invalid GitHub worker start")
    control.close()
    evidence = process._Evidence(directory, token)  # noqa: SLF001
    deadline = time.monotonic() + timeout
    try:
        status, content, location, read_bytes = _exchange(
            _API + request.route,
            request.method,
            request.body,
            token=token,
            timeout=timeout,
            maximum_bytes=limits.metadata_bytes
            if request.download
            else request.maximum_bytes,
            evidence=evidence,
            label="api",
        )
        count = 1
        if request.download:
            _require(
                status == HTTPStatus.FOUND and bool(location),
                "missing artifact redirect",
            )
            location = cast("str", location)
            origin = read_redirect_origin(location)
            # The raw Location was checked before it became forbidden. Parse
            # only after validation, then guard signed target fragments too.
            evidence.forbidden += read_location_secrets(location)
            evidence.write(
                "artifact-origin.json",
                canonicalize(
                    {
                        "evidenceKind": "derived-artifact-origin",
                        "artifactRedirectPolicy": (
                            limits.artifact_redirect_policy
                        ),
                        "artifactId": request.artifact_id,
                        "route": request.route,
                        "call": request.call,
                        "origin": origin,
                        "apiResponseFile": "api.json",
                        "apiResponseSha256": _sha(
                            _read(directory, "api.json", 4096)
                        ),
                        "locationSha256": _sha(location.encode()),
                    }
                ),
            )
            remaining = deadline - time.monotonic()
            _require(remaining > 0, "artifact retrieval deadline expired")
            status, content, further, artifact_bytes = _exchange(
                location,
                "GET",
                None,
                token=None,
                timeout=remaining,
                maximum_bytes=request.maximum_bytes,
                evidence=evidence,
                label="artifact",
            )
            _require(
                further is None, "additional artifact redirect is forbidden"
            )
            count += 1
            read_bytes += artifact_bytes
        else:
            _require(location is None, "API redirects are forbidden")
        _require(status == HTTPStatus.OK, "unexpected GitHub response status")
        evidence.write(
            "completed.json",
            canonicalize(
                {
                    "requests": count,
                    "responseBodyBytes": read_bytes,
                    "bodySha256": _sha(content),
                    "bodyFile": "artifact.body"
                    if request.download
                    else "api.body",
                }
            ),
        )
    except BaseException as error:  # noqa: BLE001 - suppress secret diagnostics
        evidence.write(
            "failed.json", canonicalize({"errorType": type(error).__name__})
        )
        # Do not let a signed URL, credential or remote diagnostic reach stderr.
        raise SystemExit(1) from None


class NuGetGitHubClient:
    """A non-resumable HTTP budget over caller-admitted existing credentials."""

    def __init__(
        self, directory: Path, limits: NuGetGitHubLimits, token: str
    ) -> None:
        """Create fresh evidence without authentication or network IO."""
        _require(
            os.name == "posix",
            "NuGet operator requires POSIX or configured WSL",
        )
        _require(
            bool(token) and not any(c.isspace() for c in token),
            "invalid GitHub credential",
        )
        self.directory = directory
        directory.mkdir(mode=0o700, parents=False, exist_ok=False)
        self.evidence = process._Evidence(directory, token)  # noqa: SLF001
        self.evidence.write("limits.json", canonicalize(limits.to_document()))
        self.limits, self.token = limits, token
        self.requests = 0
        self.response_bytes = 0
        self.calls = 0
        self.failed = False

    def request(
        self,
        route: str,
        *,
        deadline: float,
        body: bytes | None = None,
        download: bool = False,
    ) -> bytes:
        """Reserve before effects; ambiguous calls spend the reservation."""
        _require(not self.failed, "GitHub client already failed")
        _require(
            (route == "/user" or route.startswith(_REPOSITORY + "/"))
            and not any(c.isspace() for c in route)
            and "#" not in route
            and "\\" not in route,
            "unselected GitHub route",
        )
        _require(
            body is None or (not download and len(body) <= _REQUEST_LIMIT),
            "invalid dispatch body",
        )
        artifact_id = None
        if download:
            match = re.fullmatch(
                _REPOSITORY + r"/actions/artifacts/([1-9][0-9]*)/zip", route
            )
            _require(match is not None, "unselected GitHub artifact route")
            artifact_id = int(cast("re.Match[str]", match)[1])
        maximum = (
            self.limits.artifact_bytes
            if download
            else self.limits.metadata_bytes
        )
        reserve_requests = 2 if download else 1
        reserve_bytes = (
            maximum + 1 + (self.limits.metadata_bytes + 1 if download else 0)
        )
        remaining = deadline - time.monotonic()
        _require(
            self.requests + reserve_requests <= self.limits.requests
            and self.response_bytes + reserve_bytes
            <= self.limits.response_body_bytes
            and remaining > 0,
            "GitHub cumulative allowance exhausted",
        )
        self.calls += 1
        self.requests += reserve_requests
        self.response_bytes += reserve_bytes
        directory = self.directory / f"call-{self.calls:04d}"
        directory.mkdir(mode=0o700)
        evidence = process._Evidence(directory, self.token)  # noqa: SLF001
        request = _Request(
            "POST" if body is not None else "GET",
            route,
            body,
            maximum,
            download,
            artifact_id,
            self.calls,
        )
        evidence.write(
            "reserved.json",
            canonicalize(
                {
                    "method": request.method,
                    "route": route,
                    "call": self.calls,
                    "artifactId": artifact_id,
                    "artifactRedirectPolicy": (
                        self.limits.artifact_redirect_policy
                        if download
                        else None
                    ),
                    "requests": reserve_requests,
                    "responseBodyBytes": reserve_bytes,
                }
            ),
        )
        if body is not None:
            evidence.write("request.body", body)
        try:
            timeout = min(remaining, self.limits.call_timeout_seconds)
            code = process._supervise(  # noqa: SLF001
                _worker,
                (request, self.limits, directory, self.token, timeout),
                timeout,
            )
            _require(
                code == 0 and time.monotonic() < deadline,
                "GitHub call failed or completed late",
            )
            result = _object(
                parse_canonical_json(_read(directory, "completed.json", 4096))
            )
            content = _read(
                directory, "artifact.body" if download else "api.body", maximum
            )
            _require(
                result.get("requests") == reserve_requests
                and type(result.get("responseBodyBytes")) is int
                and len(content) <= result["responseBodyBytes"] <= reserve_bytes
                and result.get("bodySha256") == _sha(content),
                "GitHub response accounting mismatch",
            )
            self.response_bytes -= reserve_bytes - cast(
                "int", result["responseBodyBytes"]
            )
        except BaseException as error:
            self.failed = True
            evidence.write(
                "call-failed.json",
                canonicalize(
                    {
                        "errorType": type(error).__name__,
                        "reservationSpent": True,
                        "remoteCompletionUnknown": body is not None,
                    }
                ),
            )
            raise
        else:
            return content
