"""Finite bootstrap effects, sanitized raw journals and deterministic replay."""

from __future__ import annotations

import base64
import time
from dataclasses import replace
from http import HTTPStatus
from typing import TYPE_CHECKING, cast
from urllib.parse import parse_qsl, quote, urlsplit

from three_workflow_delivery_v3.acceptance.python_bootstrap_contract import (
    WINDOW_SECONDS,
    authority_window,
    utc_number,
)
from three_workflow_delivery_v3.acceptance.python_native_contract import require
from three_workflow_delivery_v3.adapters.pypi import (
    HTTP_TIMEOUT_SECONDS,
    MAX_FILE_BYTES,
    MAX_INDEX_BYTES,
    MAX_RESPONSE_BYTES,
    PythonHttpResponse,
    PythonRegistry,
)
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonicalize,
    parse_json_strict,
)
from three_workflow_delivery_v3.repository.python_provider import (
    python_digest,
    python_object,
)

if TYPE_CHECKING:
    from collections.abc import Callable

    from three_workflow_delivery_v3.adapters.pypi import PythonHttpTransport

REGISTRY = PythonRegistry("testpypi")
PHASE_LIMITS = {
    "authorize": {"proof": 8, "index": 1},
    "execute": {
        "proof": 8,
        "index": 13,
        "file": 3,
        "upload": 2,
        "oidc": 1,
        "mint": 1,
    },
    "audit": {"index": 1, "file": 2},
}
RECORD_FIELDS = {
    "kind",
    "method",
    "url",
    "maximum-bytes",
    "start",
    "finish",
    "status",
    "content-type",
    "body-digest",
    "request-body-digest",
    "redacted",
    "monotonic-start",
    "monotonic-finish",
    "monotonic-deadline",
}


def request_kind(method: str, url: str, maximum: int) -> str:
    """Classify only admitted HTTPS effects with exact response ceilings."""
    parsed = urlsplit(url)
    require(
        parsed.scheme == "https"
        and parsed.username is None
        and parsed.password is None
        and not parsed.fragment,
        "foreign bootstrap URL",
    )
    if method == "POST" and url == REGISTRY.upload_url:
        kind, bound = "upload", MAX_RESPONSE_BYTES
    elif method == "GET" and url == REGISTRY.index_url:
        kind, bound = "index", MAX_INDEX_BYTES
    elif (
        method == "GET"
        and parsed.netloc == REGISTRY.file_host
        and parsed.path.startswith("/packages/")
        and not parsed.query
    ):
        kind, bound = "file", MAX_FILE_BYTES
    elif (
        method == "GET"
        and parsed.netloc == "api.github.com"
        and parsed.path.startswith("/repos/hcoona/three/")
    ):
        kind, bound = "proof", 8 * 1024 * 1024
    elif method == "POST" and url == f"{REGISTRY.origin}/_/oidc/mint-token":
        kind, bound = "mint", MAX_RESPONSE_BYTES
    else:
        require(
            method == "GET"
            and parsed.hostname is not None
            and parsed.hostname.endswith(".actions.githubusercontent.com")
            and parsed.port in {None, 443}
            and [
                value
                for key, value in parse_qsl(parsed.query)
                if key == "audience"
            ]
            == ["testpypi"],
            "unadmitted bootstrap effect",
        )
        kind, bound = "oidc", MAX_RESPONSE_BYTES
    require(maximum == bound, "bootstrap response bound differs")
    return kind


class JournalTransport:
    """Enforce one phase budget before each send and retain reached facts."""

    def __init__(  # noqa: PLR0913 - fixed phase transport boundary
        self,
        transport: PythonHttpTransport,
        phase: str,
        retain: Callable[[str, bytes], None],
        *,
        window: JsonValue = None,
        clock: Callable[[], float] = time.time,
        monotonic: Callable[[], float] = time.monotonic,
        secrets: tuple[str, ...] = (),
    ) -> None:
        """Hold the fixed authority and credentials only within this process."""
        self.transport = transport
        self.limits = PHASE_LIMITS[phase]
        self.counts = dict.fromkeys(self.limits, 0)
        self.retain = retain
        self.window = window
        self.clock = clock
        self.monotonic = monotonic
        self.monotonic_deadline = float("inf")
        if window is not None:
            _, deadline = authority_window(window, clock())
            self.monotonic_deadline = self.monotonic() + deadline - clock()
        self.records: list[JsonValue] = []
        self.forbidden: list[bytes] = []
        for secret in secrets:
            self.add_secret(secret)

    def add_secret(self, secret: str) -> None:
        """Screen common reflected encodings before any raw retention."""
        require(bool(secret), "missing bootstrap credential")
        self.forbidden.extend(
            (
                secret.encode(),
                base64.b64encode(secret.encode()),
                base64.b64encode(f"__token__:{secret}".encode()),
                quote(secret, safe="").encode(),
            )
        )

    def request(
        self,
        method: str,
        url: str,
        headers: dict[str, str],
        body: bytes | None,
        maximum_bytes: int,
    ) -> PythonHttpResponse:
        """Send once; expiry does not discard an already admitted response."""
        kind = request_kind(method, url, maximum_bytes)
        require(
            kind in self.limits and self.counts[kind] < self.limits[kind],
            "bootstrap effect budget exhausted",
        )
        monotonic_start = self.monotonic()
        begin = utc_number(self.clock())
        if self.window is not None:
            authority_window(self.window, begin)
            require(
                self.monotonic() < self.monotonic_deadline,
                "bootstrap monotonic deadline expired",
            )
        require(
            not self.records
            or begin >= cast("dict", self.records[-1])["finish"],
            "bootstrap clock moved backwards",
        )
        require(
            not any(secret in url.encode() for secret in self.forbidden),
            "unsafe bootstrap URL",
        )
        self.counts[kind] += 1
        ordinal = len(self.records)
        redacted = kind in {"oidc", "mint"}
        record: dict[str, JsonValue] = {
            "monotonic-start": monotonic_start,
            "monotonic-finish": None,
            "monotonic-deadline": self.monotonic_deadline
            if self.window is not None
            else None,
            "kind": kind,
            "method": method,
            "url": url,
            "maximum-bytes": maximum_bytes,
            "start": begin,
            "finish": None,
            "status": None,
            "content-type": "",
            "body-digest": None,
            "redacted": redacted,
            "request-body-digest": python_digest(body)
            if kind == "upload" and body is not None
            else None,
        }
        try:
            response = self.transport.request(
                method, url, headers, body, maximum_bytes
            )
            record["monotonic-finish"] = self.monotonic()
            record["finish"] = self.clock()
            require(
                len(response.body) <= maximum_bytes,
                "bootstrap response too large",
            )
            require(
                not any(
                    secret in response.content_type.encode()
                    or (not redacted and secret in response.body)
                    for secret in self.forbidden
                ),
                "unsafe bootstrap response",
            )
            record["status"] = response.status
            if not redacted:
                record["content-type"] = response.content_type
                record["body-digest"] = python_digest(response.body)
                self.retain(f"http/{ordinal}.body", response.body)
            require(
                begin
                <= utc_number(self.clock())
                <= begin + HTTP_TIMEOUT_SECONDS,
                "bootstrap request exceeded its completion bound",
            )
            return replace(
                response,
                started=monotonic_start,
                finished=cast("float", record["monotonic-finish"]),
            )
        finally:
            if record["finish"] is None:
                record["finish"] = self.clock()
            if record["monotonic-finish"] is None:
                record["monotonic-finish"] = self.monotonic()
            self.records.append(record)
            self.retain(f"http/{ordinal}.json", canonicalize(record))

    def close(self) -> None:
        """Persist all reached requests, including unsuccessful calls."""
        self.retain("requests.json", canonicalize(self.records))


class ReplayTransport:
    """Consume original noncredential responses in recorded order."""

    def __init__(
        self, files: dict[str, bytes], phase: str, *, window: JsonValue = None
    ) -> None:
        """Validate raw journals and finite timing without network access."""
        records = parse_json_strict(files["requests.json"])
        require(isinstance(records, list), "missing bootstrap request journal")
        self.records = [
            python_object(record, RECORD_FIELDS)
            for record in cast("list[JsonValue]", records)
        ]
        self.files = files
        self.position = 0
        self.not_before = 0.0
        limits = PHASE_LIMITS[phase]
        counts = dict.fromkeys(limits, 0)
        expected = {"requests.json"}
        last = 0.0
        last_monotonic = 0.0
        self.monotonic_deadline: float | None = None
        for ordinal, record in enumerate(self.records):
            require(
                files[f"http/{ordinal}.json"] == canonicalize(record),
                "bootstrap raw request record differs",
            )
            expected.add(f"http/{ordinal}.json")
            kind = request_kind(
                cast("str", record["method"]),
                cast("str", record["url"]),
                cast("int", record["maximum-bytes"]),
            )
            require(
                kind == record["kind"] and kind in limits,
                "bootstrap replay effect differs",
            )
            counts[kind] += 1
            require(
                counts[kind] <= limits[kind], "bootstrap replay budget exceeded"
            )
            begin, finish = (
                utc_number(record["start"]),
                utc_number(record["finish"]),
            )
            require(
                last <= begin <= finish <= begin + HTTP_TIMEOUT_SECONDS,
                "bootstrap request timing differs",
            )
            last = finish
            monotonic_start = utc_number(record["monotonic-start"])
            monotonic_finish = utc_number(record["monotonic-finish"])
            require(
                last_monotonic
                <= monotonic_start
                <= monotonic_finish
                <= monotonic_start + HTTP_TIMEOUT_SECONDS,
                "bootstrap monotonic request timing differs",
            )
            last_monotonic = monotonic_finish
            if window is None:
                require(
                    record["monotonic-deadline"] is None,
                    "foreign bootstrap monotonic deadline",
                )
            else:
                bound = utc_number(record["monotonic-deadline"])
                require(
                    monotonic_start < bound
                    and bound - monotonic_start <= WINDOW_SECONDS,
                    "bootstrap monotonic admission expired",
                )
                if self.monotonic_deadline is None:
                    self.monotonic_deadline = bound
                require(
                    self.monotonic_deadline == bound,
                    "bootstrap monotonic deadline renewed",
                )
            if window is not None:
                authority_window(window, begin)
            require(
                type(record["status"]) is int
                and record["redacted"] is (kind in {"oidc", "mint"}),
                "bootstrap response unavailable",
            )
            require(
                kind == "upload" or record["request-body-digest"] is None,
                "unexpected bootstrap request payload evidence",
            )
            if kind in {"oidc", "mint"}:
                require(
                    record["content-type"] == ""
                    and record["body-digest"] is None,
                    "credential response retained",
                )
            else:
                name = f"http/{ordinal}.body"
                expected.add(name)
                require(
                    python_digest(files[name]) == record["body-digest"]
                    and len(files[name]) <= cast("int", record["maximum-bytes"])
                    and isinstance(record["content-type"], str),
                    "bootstrap raw response differs",
                )
        require(
            {
                name
                for name in files
                if name == "requests.json" or name.startswith("http/")
            }
            == expected,
            "bootstrap raw journal inventory differs",
        )
        self.counts = counts

    def request(
        self,
        method: str,
        url: str,
        headers: dict[str, str],
        body: bytes | None,
        maximum_bytes: int,
    ) -> PythonHttpResponse:
        """Read the next raw response, never a stored summary verdict."""
        del headers
        require(
            self.position < len(self.records), "bootstrap raw response missing"
        )
        record = self.records[self.position]
        require(
            utc_number(record["monotonic-start"]) >= self.not_before,
            "bootstrap effect precedes observation termination",
        )
        require(
            (record["method"], record["url"], record["maximum-bytes"])
            == (method, url, maximum_bytes)
            and record["redacted"] is False,
            "bootstrap response order differs",
        )
        require(
            record["request-body-digest"]
            == (
                python_digest(body)
                if record["kind"] == "upload" and body is not None
                else None
            ),
            "bootstrap original upload payload differs",
        )
        content = self.files[f"http/{self.position}.body"]
        self.position += 1
        return PythonHttpResponse(
            cast("int", record["status"]),
            content,
            cast("str", record["content-type"]),
            cast("float", record["monotonic-start"]),
            cast("float", record["monotonic-finish"]),
        )

    def credential(self, kind: str) -> None:
        """Check admission and status without retaining credentials."""
        require(
            self.position < len(self.records),
            "bootstrap credential fact missing",
        )
        record = self.records[self.position]
        require(
            record["kind"] == kind
            and record["redacted"] is True
            and record["status"] == HTTPStatus.OK,
            "bootstrap credential acquisition failed",
        )
        self.position += 1

    def finished(self) -> None:
        """Require every original response to have an explained scenario use."""
        require(
            self.position == len(self.records), "unconsumed bootstrap responses"
        )
