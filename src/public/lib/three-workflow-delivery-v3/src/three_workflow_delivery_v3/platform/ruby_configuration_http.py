"""Fixed Ruby configuration HTTPS operations with POSIX deadline supervision."""

from __future__ import annotations

import base64
import http.client
import multiprocessing
import re
import ssl
import time
from dataclasses import dataclass, field
from datetime import UTC, datetime
from pathlib import Path
from tempfile import TemporaryDirectory
from typing import TYPE_CHECKING, Protocol, cast

from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonicalize,
    parse_json_strict,
)

if TYPE_CHECKING:
    from collections.abc import Callable
    from typing import BinaryIO

BODY_LIMIT = 2 * 1024 * 1024
HEADER_LIMIT = 64 * 1024
REQUEST_SECONDS = 30
_MIN_STATUS = 100
_MAX_STATUS = 599
_API = "https://api.github.com"
_REPO = "/repos/hcoona/three"
_MARKER = "WDV3_APPROVAL_ENVIRONMENT_MARKER"
_DESTINATIONS = {
    "github-packages": "workflow-delivery-v3-ruby-github-packages",
    "rubygems": "workflow-delivery-v3-ruby-rubygems",
}
_SIMPLE = {
    "principal": "/user",
    "repository": _REPO,
    "main": _REPO + "/branches/main",
    "protection": _REPO + "/branches/main/protection",
    "selected-package": "/user/packages/rubygems/hcoona-release-smoke-ruby",
    "rubygems-project-owners": (
        "/api/v1/gems/hcoona-release-smoke-ruby/owners.json"
    ),
}
_PAGES = {
    "effective-rules": (_REPO + "/rules/branches/main", "", 5),
    "rulesets": (_REPO + "/rulesets", "includes_parents=true&", 5),
    "collaborators": (_REPO + "/collaborators", "affiliation=all&", 5),
    "owner-packages": ("/user/packages", "package_type=rubygems&", 10),
    "secrets": ("/secrets", "", 2),
    "variables": ("/variables", "", 2),
    "branch-policies": ("/deployment-branch-policies", "", 2),
}
_ENV_ROLES = {
    "environment",
    "custom-protections",
    "environment-put",
    "branch-policy-create",
    "marker-create",
    "marker-update",
}
_HEADERS = {
    "content-type",
    "content-length",
    "link",
    "date",
    "x-oauth-scopes",
    "x-github-request-id",
}
_FAILURES = {"deadline", "transport", "response-limit", "unsafe-response"}


def _reject(message: str) -> None:
    raise ValueError(message)


@dataclass(frozen=True, slots=True)
class RubyConfigurationRequest:
    """Select one reviewed endpoint and body, never arbitrary URLs or JSON."""

    destination: str
    role: str
    page: int = 1
    resource_id: int | None = None

    def __post_init__(self) -> None:  # noqa: C901 - closed endpoint role admission
        """Reject inputs outside fixed endpoint and pagination roles."""
        if self.destination not in _DESTINATIONS or type(self.page) is not int:
            _reject("Invalid Ruby configuration destination or page")
        if (
            self.role in {"owner-packages", "selected-package"}
            and self.destination != "github-packages"
        ):
            _reject("RubyGems configuration cannot use GitHub package roles")
        if (
            self.role == "rubygems-project-owners"
            and self.destination != "rubygems"
        ):
            _reject("GitHub configuration cannot select RubyGems owners")
        if self.role in _PAGES:
            if not 1 <= self.page <= _PAGES[self.role][2]:
                _reject("Ruby configuration page exceeds its fixed bound")
        elif self.page != 1:
            _reject("Ruby configuration role is not paginated")
        if self.role == "ruleset-detail":
            if type(self.resource_id) is not int or self.resource_id <= 0:
                _reject("Ruby configuration ruleset ID is invalid")
        elif self.resource_id is not None:
            _reject("Ruby configuration role cannot select a resource ID")
        if self.role not in {*_SIMPLE, *_PAGES, *_ENV_ROLES, "ruleset-detail"}:
            _reject("Unsupported Ruby configuration endpoint role")

    @property
    def origin(self) -> str:
        """Select the fixed origin; public owners receive no token."""
        return (
            "https://rubygems.org"
            if self.role == "rubygems-project-owners"
            else _API
        )

    @property
    def environment(self) -> str:
        """Return the fixed destination Environment."""
        return _DESTINATIONS[self.destination]

    @property
    def path(self) -> str:
        """Return the sole supported path/query for this selection."""
        env = f"{_REPO}/environments/{self.environment}"
        if self.role in _SIMPLE:
            return _SIMPLE[self.role]
        if self.role in _PAGES:
            path, query, _ = _PAGES[self.role]
            if self.role in {"secrets", "variables", "branch-policies"}:
                path = env + path
            return f"{path}?{query}per_page=100&page={self.page}"
        if self.role == "ruleset-detail":
            return f"{_REPO}/rulesets/{self.resource_id}?includes_parents=true"
        return (
            env
            + {
                "environment": "",
                "environment-put": "",
                "custom-protections": "/deployment_protection_rules",
                "branch-policy-create": "/deployment-branch-policies",
                "marker-create": "/variables",
                "marker-update": f"/variables/{_MARKER}",
            }[self.role]
        )

    @property
    def method(self) -> str:
        """Return GET or the exact admitted configuration write method."""
        return {
            "environment-put": "PUT",
            "branch-policy-create": "POST",
            "marker-create": "POST",
            "marker-update": "PATCH",
        }.get(self.role, "GET")

    @property
    def mutation_role(self) -> str | None:
        """Unify marker create/update for durable single-spend accounting."""
        if self.role in {"marker-create", "marker-update"}:
            return "marker"
        return self.role if self.method != "GET" else None

    @property
    def body(self) -> bytes | None:
        """Generate only documented write fields; never change admin bypass."""
        if self.role == "environment-put":
            return canonicalize(
                {
                    "wait_timer": 0,
                    "prevent_self_review": False,
                    "reviewers": [{"type": "User", "id": 712433}],
                    "deployment_branch_policy": {
                        "protected_branches": False,
                        "custom_branch_policies": True,
                    },
                }
            )
        if self.role == "branch-policy-create":
            return canonicalize({"name": "main", "type": "branch"})
        if self.role in {"marker-create", "marker-update"}:
            value: dict[str, JsonValue] = {"value": self.environment + "/v1"}
            if self.role == "marker-create":
                value["name"] = _MARKER
            return canonicalize(value)
        return None


@dataclass(frozen=True, slots=True)
class RubyConfigurationResponse:
    """Private original response or spent failed send, with safe diagnostics."""

    request: RubyConfigurationRequest
    receipt: str
    started_at: datetime
    completed_at: datetime
    status: int | None
    headers: tuple[tuple[str, str], ...] = field(default=(), repr=False)
    body: bytes = field(default=b"", repr=False)
    failure: str | None = None

    def __post_init__(self) -> None:
        """Keep the evidence shape closed and internally consistent."""
        if (
            not self.receipt
            or self.started_at.tzinfo is None
            or self.completed_at.tzinfo is None
            or self.completed_at < self.started_at
            or type(self.body) is not bytes
            or len(self.body) > BODY_LIMIT
        ):
            _reject("Invalid Ruby configuration response envelope")
        if self.failure is not None:
            if (
                self.failure not in _FAILURES
                or self.status is not None
                or self.body
                or self.headers
            ):
                _reject("Contradictory Ruby configuration failure evidence")
        elif (
            type(self.status) is not int
            or not _MIN_STATUS <= self.status <= _MAX_STATUS
        ):
            _reject("Invalid Ruby configuration response status")
        names = [key for key, _ in self.headers]
        if (
            len(set(names)) != len(names)
            or any(key not in _HEADERS for key in names)
            or sum(
                len(k.encode()) + len(v.encode()) + 4 for k, v in self.headers
            )
            > HEADER_LIMIT
        ):
            _reject("Invalid Ruby configuration retained headers")

    def header(self, name: str) -> str | None:
        """Return one explicitly retained normalized response header."""
        return dict(self.headers).get(name)

    def document(self) -> dict[str, JsonValue]:
        """Serialize private evidence for later owner-data screening."""
        return {
            "schema": "workflow-delivery/v3/ruby-configuration-response-v1",
            "destination": self.request.destination,
            "role": self.request.role,
            "method": self.request.method,
            "url": self.request.origin + self.request.path,
            "receipt": self.receipt,
            "started-at": self.started_at.isoformat(),
            "completed-at": self.completed_at.isoformat(),
            "status": self.status,
            "headers": cast("JsonValue", dict(self.headers)),
            "body-base64": base64.b64encode(self.body).decode("ascii"),
            "failure": self.failure,
        }


class RubyConfigurationSender(Protocol):
    """Inject the reviewed durable transport into fixed collectors."""

    def send(
        self, request: RubyConfigurationRequest
    ) -> RubyConfigurationResponse:
        """Reserve, perform and retain exactly one bounded send."""
        ...


class _HeaderReader:
    def __init__(self, source: BinaryIO) -> None:
        self.source = source
        self.used = 0

    def readline(self, limit: int = -1) -> bytes:
        remaining = HEADER_LIMIT - self.used
        content = self.source.readline(
            min(limit, remaining + 1) if limit >= 0 else remaining + 1
        )
        self.used += len(content)
        if self.used > HEADER_LIMIT:
            _reject("Ruby configuration response headers exceed limit")
        return content


class _Response(http.client.HTTPResponse):
    def begin(self) -> None:
        """Limit all status/header bytes before stdlib parses them."""
        if getattr(self, "headers", None) is not None:
            return
        original = self.fp
        reader = _HeaderReader(cast("BinaryIO", original))
        setattr(self, "fp", reader)  # noqa: B010 - adapt stdlib internal file protocol
        try:
            super().begin()
        finally:
            self._header_used = reader.used
            setattr(self, "fp", original)  # noqa: B010 - restore stdlib internal file protocol

    def _read_and_discard_trailer(self) -> None:
        """Include chunk trailers in the same complete header byte ceiling."""
        original = self.fp
        reader = _HeaderReader(cast("BinaryIO", original))
        reader.used = self._header_used
        setattr(self, "fp", reader)  # noqa: B010 - adapt stdlib internal file protocol
        try:
            while True:
                line = reader.readline(HEADER_LIMIT + 1)
                if line in {b"\r\n", b"\n"}:
                    break
                if not line:
                    _reject("Ruby configuration chunk trailer is incomplete")
        finally:
            self._header_used = reader.used
            setattr(self, "fp", original)  # noqa: B010 - restore stdlib internal file protocol


def _perform(  # noqa: C901, PLR0912 - one TLS send and sanitized response boundary
    request: RubyConfigurationRequest, token: str, output: Path
) -> None:
    """Execute only in the supervised child; never retain exception strings."""
    result: dict[str, JsonValue] = {"failure": "transport"}
    connection: http.client.HTTPSConnection | None = None
    try:
        context = ssl.create_default_context()
        context.minimum_version = ssl.TLSVersion.TLSv1_2
        connection = http.client.HTTPSConnection(
            request.origin.removeprefix("https://"),
            443,
            timeout=REQUEST_SECONDS,
            context=context,
        )
        connection.response_class = _Response
        headers = {
            "Authorization": f"Bearer {token}",
            "Accept": "application/vnd.github+json",
            "X-GitHub-Api-Version": "2022-11-28",
            "User-Agent": "three-workflow-delivery-v3",
        }
        if request.role == "rubygems-project-owners":
            headers = {
                "Accept": "application/json",
                "User-Agent": "three-workflow-delivery-v3",
            }
        if request.body is not None:
            headers["Content-Type"] = "application/json"
        connection.request(
            request.method, request.path, body=request.body, headers=headers
        )
        response = connection.getresponse()
        content = response.read(BODY_LIMIT + 1)
        length = response.getheader("Content-Length")
        if length is not None and (
            not length.isdigit() or int(length) != len(content)
        ):
            _reject("Ruby configuration response was truncated or malformed")
        selected: dict[str, str] = {}
        for original_key, value in response.getheaders():
            key = original_key.lower()
            if request.role == "rubygems-project-owners" and key == "location":
                _reject("RubyGems owners response cannot redirect")
            if key in _HEADERS:
                if key in selected:
                    _reject("Repeated retained Ruby configuration header")
                selected[key] = value
        if len(content) > BODY_LIMIT:
            result = {"failure": "response-limit"}
        elif token.encode() in content or any(
            token in value for value in selected.values()
        ):
            result = {"failure": "unsafe-response"}
        else:
            result = {
                "status": response.status,
                "headers": cast("JsonValue", selected),
                "body": base64.b64encode(content).decode("ascii"),
            }
    except Exception:  # noqa: BLE001 - sanitize all child failure diagnostics
        result = {"failure": "transport"}
    finally:
        if connection is not None:
            connection.close()
    output.write_bytes(canonicalize(result))


class RubyConfigurationTransport:
    """One-shot POSIX child bounds DNS, TLS, headers and the complete body."""

    def __init__(
        self,
        token: str,
        *,
        deadline: datetime,
        reserve: Callable[[RubyConfigurationRequest], str],
        retain: Callable[[RubyConfigurationResponse], None],
    ) -> None:
        """Bind in-memory capability and mandatory durable callbacks."""
        if (
            not isinstance(token, str)
            or not re.fullmatch(r"[A-Za-z0-9_]+", token)
            or deadline.tzinfo is None
            or not callable(reserve)
            or not callable(retain)
        ):
            _reject("Invalid Ruby configuration transport admission")
        self._token = token
        self._deadline = deadline
        self._reserve = reserve
        self._retain = retain

    def send(  # noqa: C901 - one supervised lifetime and cleanup boundary
        self, request: RubyConfigurationRequest
    ) -> RubyConfigurationResponse:
        """Spend before starting; kill the child when its deadline expires."""
        if type(request) is not RubyConfigurationRequest:
            _reject("Ruby configuration requires a fixed request")
        remaining = (self._deadline - datetime.now(UTC)).total_seconds()
        if remaining <= 0:
            _reject("Ruby configuration phase has expired")
        receipt = self._reserve(request)
        if (
            not isinstance(receipt, str)
            or not receipt
            or self._token in receipt
        ):
            _reject("Ruby configuration reservation receipt is invalid")
        started = datetime.now(UTC)
        allowance = min(
            REQUEST_SECONDS, (self._deadline - started).total_seconds()
        )
        end = time.monotonic() + max(0, allowance)
        result: dict[str, JsonValue] = {"failure": "deadline"}
        with TemporaryDirectory(prefix="wdv3-ruby-configuration-") as temporary:
            output = Path(temporary) / "response.json"
            child = multiprocessing.get_context("fork").Process(
                target=_perform, args=(request, self._token, output)
            )
            try:
                if allowance > 0:
                    child.start()
                    child.join(max(0, end - time.monotonic()))
                    if child.is_alive():
                        child.kill()
                        child.join(5)
                    elif time.monotonic() <= end and child.exitcode == 0:
                        if output.stat().st_size <= BODY_LIMIT * 2:
                            raw = parse_json_strict(output.read_bytes())
                            if isinstance(raw, dict):
                                result = raw
            except (OSError, ValueError):
                result = {"failure": "transport"}
            finally:
                if child.pid is not None and child.is_alive():
                    child.kill()
                    child.join(5)
                if child.pid is not None and not child.is_alive():
                    child.close()
        response = _decode_response(request, receipt, started, result)
        self._retain(response)
        return response


def _decode_response(
    request: RubyConfigurationRequest,
    receipt: str,
    started: datetime,
    result: dict[str, JsonValue],
) -> RubyConfigurationResponse:
    if "failure" in result:
        return RubyConfigurationResponse(
            request,
            receipt,
            started,
            datetime.now(UTC),
            None,
            failure=str(result["failure"]),
        )
    return RubyConfigurationResponse(
        request,
        receipt,
        started,
        datetime.now(UTC),
        cast("int", result["status"]),
        tuple(cast("dict[str, str]", result["headers"]).items()),
        base64.b64decode(cast("str", result["body"]), validate=True),
    )
