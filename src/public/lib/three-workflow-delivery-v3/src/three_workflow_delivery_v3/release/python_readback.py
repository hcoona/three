"""Original public response evidence for offline Python Result verification."""

from __future__ import annotations

import base64
from dataclasses import replace
from typing import TYPE_CHECKING, cast
from urllib.parse import quote

from three_workflow_delivery_v3.adapters.pypi import (
    HTTP_TIMEOUT_SECONDS,
    MAX_FILE_BYTES,
    PythonHttpResponse,
    read_python_index,
)
from three_workflow_delivery_v3.adapters.python_observation import (
    number,
    response_document,
    response_from_document,
)
from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.repository.python_provider import python_object

if TYPE_CHECKING:
    from collections.abc import Callable

    from three_workflow_delivery_v3.adapters.pypi import (
        PythonHttpTransport,
        PythonIndexObservation,
        PythonRegistry,
    )
    from three_workflow_delivery_v3.adapters.python import (
        PythonPackageTargetWitness,
    )
    from three_workflow_delivery_v3.canonical import JsonValue


_MAX_PHASE_READS = 6
_DISTRIBUTION_COUNT = 2


class _InvalidDownloadEvidenceError(ValueError):
    """Distinguish an impossible trace from a replayed download failure."""


class PublicationTransport:
    """Time one actual send and screen credentials before public retention."""

    def __init__(
        self,
        transport: PythonHttpTransport,
        token: str,
        clock: Callable[[], float],
        *,
        deadline: float,
        admit: Callable[[], None],
    ) -> None:
        """Keep the token only in the in-memory reflection screen."""
        self.transport = transport
        self.clock = clock
        self.deadline = deadline
        self.admit = admit
        self.upload_started: float | None = None
        self.forbidden = tuple(
            part
            for part in (
                token.encode(),
                base64.b64encode(token.encode()),
                base64.b64encode(f"__token__:{token}".encode()),
                quote(token, safe="").encode(),
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
        """Admit one request and return safe, timed response bytes."""
        self.admit()
        started = number(self.clock())
        if started >= self.deadline:
            message = "Python publication authority expired"
            raise ValueError(message)
        if method == "POST":
            self.upload_started = started
        response = self.transport.request(
            method, url, headers, body, maximum_bytes
        )
        finished = number(self.clock())
        if len(response.body) > maximum_bytes or any(
            secret in response.body or secret in response.content_type.encode()
            for secret in self.forbidden
        ):
            message = "unsafe Python publication response"
            raise ValueError(message)
        if not started <= finished <= started + HTTP_TIMEOUT_SECONDS:
            message = "Python publication response timing differs"
            raise ValueError(message)
        return replace(response, started=started, finished=finished)


class ReadbackTransport:
    """Reuse the final index once and retain each reached public download."""

    def __init__(
        self,
        registry: PythonRegistry,
        index: PythonHttpResponse,
        transport: PythonHttpTransport,
        downloads: list[JsonValue],
        *,
        retain: Callable[[str, bytes], None] | None = None,
    ) -> None:
        """Bind the phase terminal index and a bounded download accumulator."""
        self.registry = registry
        self.index = index
        self.transport = transport
        self.downloads = downloads
        self.retain = retain
        self.used = False

    def request(
        self,
        method: str,
        url: str,
        headers: dict[str, str],
        body: bytes | None,
        maximum_bytes: int,
    ) -> PythonHttpResponse:
        """Read each file once after the complete phase has finished."""
        if not self.used and method == "GET" and url == self.registry.index_url:
            self.used = True
            return self.index
        entry: dict[str, JsonValue] = {
            "url": url,
            "response": None,
            "start": None,
            "finish": None,
        }
        self.downloads.append(entry)
        try:
            response = self.transport.request(
                method, url, headers, body, maximum_bytes
            )
            entry.update(
                {
                    "response": response_document(response),
                    "start": response.started,
                    "finish": response.finished,
                }
            )
        finally:
            if self.retain is not None:
                self.retain(
                    f"download-{len(self.downloads) - 1}.json",
                    canonicalize(entry),
                )

        return response


def replay_readback(  # noqa: C901, PLR0913 - validate evidence before replaying terminal service failures
    registry: PythonRegistry,
    witness: PythonPackageTargetWitness,
    index: PythonHttpResponse,
    downloads: JsonValue,
    *,
    after: float,
    deadline: float,
) -> PythonIndexObservation | None:
    """Inspect the exact original downloaded bytes without destination IO."""
    if not isinstance(downloads, list) or len(downloads) > len(
        ("wheel", "sdist")
    ):
        message = "Python Result download inventory differs"
        raise ValueError(message)
    queue = [
        python_object(item, {"url", "response", "start", "finish"})
        for item in downloads
    ]
    last = number(after)
    for ordinal, entry in enumerate(queue):
        if entry["response"] is None:
            if (
                ordinal != len(queue) - 1
                or entry["start"] is not None
                or entry["finish"] is not None
            ):
                message = "Python Result failed download evidence differs"
                raise ValueError(message)
            continue
        start, finish = number(entry["start"]), number(entry["finish"])
        if (
            not last <= start <= finish <= start + HTTP_TIMEOUT_SECONDS
            or start >= deadline
        ):
            message = "Python Result download timing differs"
            raise ValueError(message)
        response_from_document(entry["response"], maximum=MAX_FILE_BYTES)
        last = finish

    class Replay:
        def request(
            self,
            method: str,
            url: str,
            headers: dict[str, str],
            body: bytes | None,
            maximum_bytes: int,
        ) -> PythonHttpResponse:
            del headers
            if (
                not queue
                or method != "GET"
                or body is not None
                or maximum_bytes != MAX_FILE_BYTES
            ):
                message = "Python Result download missing or unadmitted"
                raise _InvalidDownloadEvidenceError(message)
            entry = queue.pop(0)
            if entry["url"] != url:
                message = "Python Result download URL differs"
                raise _InvalidDownloadEvidenceError(message)
            if entry["response"] is None:
                message = "Retained Python download failed without a response"
                raise OSError(message)
            return replace(
                response_from_document(
                    entry["response"], maximum=MAX_FILE_BYTES
                ),
                started=cast("float", entry["start"]),
                finished=cast("float", entry["finish"]),
            )

    try:
        observation = read_python_index(
            registry, witness, ReadbackTransport(registry, index, Replay(), [])
        )
    except _InvalidDownloadEvidenceError:
        raise
    except (OSError, TypeError, ValueError):
        # The original response/transport failure is terminal only after every
        # reached request has matched its independently reconstructed binding.
        observation = None
    if queue:
        message = "Python Result has unconsumed downloads"
        raise ValueError(message)
    return observation


def validate_observation_document(value: JsonValue) -> None:
    """Close the nested Result wire shape before predecessor-bound replay."""
    evidence = python_object(
        value, {"phase", "downloads", "upload-started", "authority"}
    )
    number(evidence["upload-started"])
    authority = python_object(
        evidence["authority"], {"utc", "monotonic", "deadline"}
    )
    number(authority["monotonic"])
    number(authority["deadline"])
    phase = python_object(
        evidence["phase"],
        {
            "schema",
            "profile-digest",
            "policy",
            "phase",
            "clock",
            "upload-response-digest",
            "upload-finished",
            "deadline",
            "outer-deadline",
            "addition",
            "previous",
            "reads",
            "terminal",
            "stopped-at",
        },
    )
    python_object(phase["addition"], {"filename", "digest"})
    response_from_document(phase["previous"])
    reads = phase["reads"]
    downloads = evidence["downloads"]
    if (
        not isinstance(reads, list)
        or len(reads) > _MAX_PHASE_READS
        or not isinstance(downloads, list)
        or len(downloads) > _DISTRIBUTION_COUNT
    ):
        message = "Python Result observation evidence exceeds its bounds"
        raise ValueError(message)
    for raw in reads:
        entry = python_object(
            raw,
            {
                "ordinal",
                "url",
                "admitted-at",
                "start",
                "finish",
                "response",
                "classification",
            },
        )
        if entry["response"] is not None:
            response_from_document(entry["response"])
    for raw in downloads:
        entry = python_object(raw, {"url", "start", "finish", "response"})
        if entry["response"] is not None:
            response_from_document(entry["response"], maximum=MAX_FILE_BYTES)
