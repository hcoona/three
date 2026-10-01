"""Whole-request supervision for the fixed Ruby hosted HTTPS caller."""

from __future__ import annotations

import base64
import multiprocessing
import queue
import threading
import time
from datetime import UTC, datetime
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3._ruby_native import ruby_object
from three_workflow_delivery_v3.adapters.ruby_registry import (
    RubyHttpResponse,
    RubyHttpsTransport,
)
from three_workflow_delivery_v3.adapters.rubygems import (
    RUBY_HTTP_HEADER_LIMIT,
    RUBY_HTTP_TIMEOUT,
    RUBY_INDEX_LIMIT,
    RUBY_LOCATION_LIMIT,
)
from three_workflow_delivery_v3.canonical import canonicalize, parse_json_strict

if TYPE_CHECKING:
    from collections.abc import Callable
    from multiprocessing.connection import Connection


_IPC_LIMIT = 4 * RUBY_INDEX_LIMIT
_REAP_SECONDS = 5.0
_HTTP_STATUS_MIN = 100
_HTTP_STATUS_MAX = 599


def _perform(  # noqa: PLR0913, PLR0917 - one fixed raw request
    output: Connection,
    method: str,
    url: str,
    headers: dict[str, str],
    body: bytes | None,
    maximum_bytes: int,
) -> None:
    try:
        response = RubyHttpsTransport().request(
            method, url, headers, body, maximum_bytes
        )
        # A malicious error page must not persist a reflected job credential.
        # Exchange responses have no Authorization header and stay private in
        # the parent writer; they are never serialized into audit artifacts.
        authorization = headers.get("Authorization")
        credential_parts = []
        if authorization:
            credential_parts.extend(
                [
                    authorization.encode(),
                    authorization.split(" ", 1)[-1].encode(),
                ]
            )
            if authorization.startswith("Basic "):
                decoded = base64.b64decode(
                    authorization.removeprefix("Basic "), validate=True
                )
                credential_parts.extend([decoded, decoded.split(b":", 1)[-1]])
        response_text = (
            response.body
            + response.content_type.encode()
            + (response.location or "").encode()
        )
        if any(part and part in response_text for part in credential_parts):
            output.send_bytes(canonicalize({"failure": "unsafe-response"}))
            return
        output.send_bytes(
            canonicalize(
                {
                    "status": response.status,
                    "content-type": response.content_type,
                    "location": response.location,
                    "location-invalid": response.location_invalid,
                    "body-base64": base64.b64encode(response.body).decode(
                        "ascii"
                    ),
                }
            )
        )
    except Exception:  # noqa: BLE001 - child errors must not expose credentials
        output.send_bytes(canonicalize({"failure": "request-failed"}))
    finally:
        output.close()


def _receive(
    incoming: Connection, results: queue.Queue[bytes | Exception]
) -> None:
    try:
        results.put_nowait(incoming.recv_bytes(_IPC_LIMIT))
    except Exception as error:  # noqa: BLE001 - propagate privately to parent
        results.put_nowait(error)


def _await_response(
    results: queue.Queue[bytes | Exception], end: float
) -> bytes:
    try:
        content = results.get(timeout=max(0.0, end - time.monotonic()))
    except queue.Empty:
        message = "Ruby HTTP whole-request deadline exhausted"
        raise TimeoutError(message) from None
    if isinstance(content, Exception):
        message = "Ruby supervised response transport failed"
        raise OSError(message) from None
    return content


class RubyOperationHttpsTransport:
    """One supervised send; never forks or substitutes the registry writer.

    Native HTTP runs in a child with verified TLS and the pinned profile. Only
    the raw response returns through a private in-memory pipe. The parent
    writer performs exchange validation and retains its credential identity.
    No credential is persisted and no network exception text is propagated.
    """

    def __init__(
        self,
        deadline: datetime,
        *,
        clock: Callable[[], datetime] = lambda: datetime.now(UTC),
    ) -> None:
        """Bind the current job phase's absolute deadline."""
        now = clock()
        if deadline.tzinfo is None or now.tzinfo is None or now >= deadline:
            message = "Ruby HTTP phase deadline is unavailable or expired"
            raise ValueError(message)
        # Refuse unsupported hosts/profile before creating the request child.
        RubyHttpsTransport()
        self.deadline = deadline
        self._clock = clock
        self._context = multiprocessing.get_context("fork")

    def request(
        self,
        method: str,
        url: str,
        headers: dict[str, str],
        body: bytes | None,
        maximum_bytes: int,
    ) -> RubyHttpResponse:
        """Cap DNS, connect, TLS, headers and complete body at 30 seconds."""
        now = self._clock()
        seconds = min(RUBY_HTTP_TIMEOUT, (self.deadline - now).total_seconds())
        if seconds <= 0 or not 0 < maximum_bytes <= RUBY_INDEX_LIMIT:
            message = "Ruby HTTP deadline or response bound is invalid"
            raise ValueError(message)
        end = time.monotonic() + seconds
        incoming, outgoing = self._context.Pipe(duplex=False)
        child = self._context.Process(
            target=_perform,
            args=(outgoing, method, url, headers, body, maximum_bytes),
            daemon=True,
        )
        receiver: threading.Thread | None = None
        try:
            child.start()
            outgoing.close()
            results: queue.Queue[bytes | Exception] = queue.Queue(maxsize=1)
            receiver = threading.Thread(
                target=_receive, args=(incoming, results), daemon=True
            )
            receiver.start()
            content = _await_response(results, end)
            if time.monotonic() >= end or self._clock() >= self.deadline:
                message = (
                    "Ruby HTTP response exceeded its whole-request deadline"
                )
                raise TimeoutError(message)
            doc = ruby_object(parse_json_strict(content))
            if "failure" in doc:
                message = "Ruby supervised HTTP request failed"
                raise OSError(message)
            content_type, encoded = (
                doc.get("content-type"),
                doc.get("body-base64"),
            )
            if (
                set(doc)
                != {
                    "status",
                    "content-type",
                    "body-base64",
                    "location",
                    "location-invalid",
                }
                or not isinstance(content_type, str)
                or not isinstance(encoded, str)
                or len(content_type.encode()) > RUBY_HTTP_HEADER_LIMIT
                or type(doc.get("location-invalid")) is not bool
                or (
                    doc.get("location") is not None
                    and (
                        not isinstance(doc["location"], str)
                        or len(cast("str", doc["location"]).encode())
                        > RUBY_LOCATION_LIMIT
                    )
                )
            ):
                message = "Ruby supervised HTTP response is malformed"
                raise ValueError(message)
            response = RubyHttpResponse(
                cast("int", doc.get("status")),
                base64.b64decode(encoded, validate=True),
                content_type,
                cast("str | None", doc["location"]),
                cast("bool", doc["location-invalid"]),
            )
            if (
                type(response.status) is not int
                or not _HTTP_STATUS_MIN <= response.status <= _HTTP_STATUS_MAX
                or len(response.body) > maximum_bytes
            ):
                message = "Ruby supervised HTTP response is malformed"
                raise ValueError(message)
            return response
        finally:
            outgoing.close()
            if child.pid is not None:
                if child.is_alive():
                    child.kill()
                child.join(_REAP_SECONDS)
                if child.is_alive():
                    message = "Ruby HTTP child could not be reaped"
                    raise RuntimeError(message)
                child.close()
            if receiver is not None:
                receiver.join(_REAP_SECONDS)
            incoming.close()
