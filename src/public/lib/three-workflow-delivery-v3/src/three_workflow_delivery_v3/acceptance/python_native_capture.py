"""Byte-preserving reconstruction of historical Python capture evidence."""

from __future__ import annotations

from dataclasses import dataclass
from http import HTTPStatus
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3.acceptance.python_native_contract import require
from three_workflow_delivery_v3.adapters.pypi import (
    MAX_INDEX_BYTES,
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
