"""Bounded actual-adapter capture and byte-preserving local replay."""

from __future__ import annotations

import base64
import threading
import time
from dataclasses import dataclass, replace
from http import HTTPStatus
from typing import TYPE_CHECKING, cast
from urllib.parse import quote, urlsplit

from three_workflow_delivery_v3.acceptance.python_native_contract import require
from three_workflow_delivery_v3.adapters.pypi import (
    MAX_FILE_BYTES,
    MAX_INDEX_BYTES,
    MAX_RESPONSE_BYTES,
    PythonHttpResponse,
    PythonHttpTransport,
    PythonRegistry,
    read_python_index,
)
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonicalize,
    parse_canonical_json,
    parse_json_strict,
)

if TYPE_CHECKING:
    from collections.abc import Callable

    from three_workflow_delivery_v3.adapters.python import (
        PythonDistribution,
        PythonPackageTargetWitness,
    )


class NativeTransport:
    """Enforce the suite's finite registry budget before each real request."""

    def __init__(
        self,
        registry: PythonRegistry,
        transport: PythonHttpTransport,
        *,
        secrets: tuple[str, ...] = (),
        clock: Callable[[], float] = time.monotonic,
        deadline: float | None = None,
    ) -> None:
        """Hold capabilities only in memory and share one locked counter."""
        self.registry = registry
        self.transport = transport
        self.clock = clock
        self.deadline = clock() + 600 if deadline is None else deadline
        self.counts = {"upload": 0, "index": 0, "file": 0}
        self.calls: list[dict[str, JsonValue]] = []
        self._lock = threading.Lock()
        self.local = threading.local()
        self._forbidden = tuple(
            part
            for secret in secrets
            for part in (
                secret.encode(),
                base64.b64encode(secret.encode()),
                base64.b64encode(f"__token__:{secret}".encode()),
                quote(secret, safe="").encode(),
            )
            if part
        )

    def request(
        self,
        method: str,
        url: str,
        headers: dict[str, str],
        body: bytes | None,
        maximum_bytes: int,
    ) -> PythonHttpResponse:
        """Admit one bounded request, without retries or retaining headers."""
        if method == "POST" and url == self.registry.upload_url:
            kind, limit, maximum = "upload", 10, MAX_RESPONSE_BYTES
        elif method == "GET" and url == self.registry.index_url:
            kind, limit, maximum = "index", 29, MAX_INDEX_BYTES
        else:
            parsed = urlsplit(url)
            require(
                method == "GET"
                and parsed.scheme == "https"
                and parsed.netloc == self.registry.file_host
                and parsed.path.startswith("/packages/")
                and not parsed.query
                and not parsed.fragment,
                "foreign acceptance request",
            )
            kind, limit, maximum = "file", 18, MAX_FILE_BYTES
        require(maximum_bytes == maximum, "acceptance response bound differs")
        with self._lock:
            require(
                self.clock() < self.deadline and self.counts[kind] < limit,
                "acceptance budget exhausted",
            )
            self.counts[kind] += 1
            record: dict[str, JsonValue] = {
                "kind": kind,
                "start": self.clock(),
                "finish": None,
            }
            self.calls.append(record)
            self.local.call = record
        try:
            response = self.transport.request(
                method, url, headers, body, maximum_bytes
            )
        finally:
            record["finish"] = self.clock()
        require(
            len(response.body) <= maximum_bytes,
            "acceptance response exceeds budget",
        )
        require(
            not any(
                secret in response.body
                or secret in response.content_type.encode()
                for secret in self._forbidden
            ),
            "unsafe acceptance response",
        )
        return replace(
            response,
            started=cast("float", record["start"]),
            finished=cast("float", record["finish"]),
        )


@dataclass(frozen=True)
class Capture:
    """One complete original index and its actual scenario downloads."""

    index: PythonHttpResponse
    downloads: tuple[tuple[str, PythonHttpResponse], ...]
    inventory: dict[str, JsonValue]
    distributions: tuple[PythonDistribution, ...]

    def files(self, ordinal: int) -> dict[str, bytes]:
        """Retain raw bodies and independently replayable response metadata."""
        prefix = f"capture/c{ordinal}"
        files = {
            prefix + "/index.body": self.index.body,
            prefix + "/index.response.json": canonicalize(
                {
                    "status": self.index.status,
                    "content-type": self.index.content_type,
                }
            ),
        }
        responses: list[JsonValue] = []
        for number, (url, response) in enumerate(self.downloads):
            path = f"{prefix}/file-{number}.body"
            files[path] = response.body
            files[f"{prefix}/file-{number}.response.json"] = canonicalize(
                {
                    "url": url,
                    "status": response.status,
                    "content-type": response.content_type,
                }
            )
            responses.append(
                {
                    "url": url,
                    "status": response.status,
                    "content-type": response.content_type,
                    "body": path,
                }
            )
        files[prefix + ".json"] = canonicalize(
            {
                "index-status": self.index.status,
                "index-content-type": self.index.content_type,
                "inventory": self.inventory,
                "downloads": responses,
            }
        )
        return files


class _IndexReplay:
    def __init__(
        self,
        registry: PythonRegistry,
        response: PythonHttpResponse,
        transport: PythonHttpTransport,
        retain: Callable[[str, bytes], None] | None = None,
        ordinal: int = 0,
    ) -> None:
        self.retain = retain
        self.ordinal = ordinal
        self.registry = registry
        self.response = response
        self.transport = transport
        self.downloads: list[tuple[str, PythonHttpResponse]] = []

    def request(
        self,
        method: str,
        url: str,
        headers: dict[str, str],
        body: bytes | None,
        maximum_bytes: int,
    ) -> PythonHttpResponse:
        if method == "GET" and url == self.registry.index_url:
            return self.response
        response = self.transport.request(
            method, url, headers, body, maximum_bytes
        )
        if self.retain is not None:
            prefix = f"capture/c{self.ordinal}/file-{len(self.downloads)}"
            self.retain(prefix + ".body", response.body)
            self.retain(
                prefix + ".response.json",
                canonicalize(
                    {
                        "url": url,
                        "status": response.status,
                        "content-type": response.content_type,
                    }
                ),
            )
        self.downloads.append((url, response))
        return response


def collect_capture(  # noqa: PLR0913 - raw response reuse and evidence retention
    registry: PythonRegistry,
    witnesses: tuple[PythonPackageTargetWitness, PythonPackageTargetWitness],
    transport: PythonHttpTransport,
    *,
    retain: Callable[[str, bytes], None] | None = None,
    ordinal: int = 0,
    response: PythonHttpResponse | None = None,
) -> Capture:
    """Fetch the index once, then reuse both existing version readers."""
    if response is None:
        response = transport.request(
            "GET",
            registry.index_url,
            {"Accept": "application/vnd.pypi.simple.v1+json"},
            None,
            MAX_INDEX_BYTES,
        )
    require(len(response.body) <= MAX_INDEX_BYTES, "oversized acceptance index")
    if retain is not None:
        retain(f"capture/c{ordinal}/index.body", response.body)
        retain(
            f"capture/c{ordinal}/index.response.json",
            canonicalize(
                {
                    "status": response.status,
                    "content-type": response.content_type,
                }
            ),
        )
    require(
        response.status == HTTPStatus.OK,
        "native acceptance requires a complete HTTP 200 index",
    )
    replay = _IndexReplay(registry, response, transport, retain, ordinal)
    distributions = tuple(
        file
        for witness in witnesses
        for file in read_python_index(registry, witness, replay).files
    )
    inventory: dict[str, JsonValue] = {}
    doc = cast("dict[str, JsonValue]", parse_json_strict(response.body))
    for item in cast("list[dict[str, JsonValue]]", doc["files"]):
        inventory[cast("str", item["filename"])] = item
    return Capture(response, tuple(replay.downloads), inventory, distributions)


def replay_capture(
    files: dict[str, bytes],
    ordinal: int,
    registry: PythonRegistry,
    witnesses: tuple[PythonPackageTargetWitness, PythonPackageTargetWitness],
) -> Capture:
    """Re-run actual inspection solely from supplied raw service bytes."""
    prefix = f"capture/c{ordinal}"
    metadata = parse_canonical_json(files[prefix + ".json"])
    queue = [
        (
            registry.index_url,
            PythonHttpResponse(
                cast("int", metadata["index-status"]),
                files[prefix + "/index.body"],
                cast("str", metadata["index-content-type"]),
            ),
        )
    ]
    for item in cast("list[dict[str, JsonValue]]", metadata["downloads"]):
        queue.append(
            (
                cast("str", item["url"]),
                PythonHttpResponse(
                    cast("int", item["status"]),
                    files[cast("str", item["body"])],
                    cast("str", item["content-type"]),
                ),
            )
        )

    class Replay:
        def request(
            self,
            method: str,
            url: str,
            headers: dict[str, str],  # noqa: ARG002 - transport protocol
            body: bytes | None,  # noqa: ARG002 - transport protocol
            maximum_bytes: int,
        ) -> PythonHttpResponse:
            require(
                bool(queue) and method == "GET",
                "unexpected capture replay request",
            )
            expected_url, response = queue.pop(0)
            require(
                url == expected_url and len(response.body) <= maximum_bytes,
                "capture response binding differs",
            )
            return response

    capture = collect_capture(registry, witnesses, Replay())
    require(
        not queue
        and capture.inventory == metadata["inventory"]
        and capture.files(ordinal)
        == {key: files[key] for key in capture.files(ordinal)},
        "capture metadata or inventory differs",
    )
    return capture
