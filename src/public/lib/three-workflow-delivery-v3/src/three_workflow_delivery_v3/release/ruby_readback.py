"""Replay Ruby registry observations from bounded credential-free envelopes."""

from __future__ import annotations

import base64
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3._ruby_native import ruby_object
from three_workflow_delivery_v3.adapters.ruby_registry import (
    RUBY_OBSERVATION_FAILURES,
    RubyHttpResponse,
    RubyRegistryObservation,
    RubyRegistryReader,
    RubyRequestBudget,
    ruby_response_document,
)
from three_workflow_delivery_v3.adapters.rubygems import (
    RUBY_INDEX_LIMIT,
    RUBY_REGISTRY_REQUEST_LIMIT,
    RubyRegistry,
)
from three_workflow_delivery_v3.canonical import JsonValue, canonicalize

if TYPE_CHECKING:
    from three_workflow_delivery_v3.adapters.ruby import RubyDistribution


_MIN_STATUS = 100
_MAX_STATUS = 599


def _text(value: JsonValue) -> str:
    if not isinstance(value, str):
        message = "Ruby response string is invalid"
        raise TypeError(message)
    return value


class _ReplayTransport:
    def __init__(self, entries: list[JsonValue], failure: JsonValue) -> None:
        self.failure = failure
        self.entries = entries
        self.used = 0

    def request(
        self,
        method: str,
        url: str,
        headers: dict[str, str],  # noqa: ARG002 - transport protocol
        body: bytes | None,
        maximum_bytes: int,
    ) -> RubyHttpResponse:
        if self.used >= len(self.entries):
            # Only a validated finite family can reproduce an attested failure.
            message = "Ruby readback lacks the next native response"
            if isinstance(self.failure, str):
                raise RUBY_OBSERVATION_FAILURES[self.failure](message)
            raise ValueError(message)
        entry = ruby_object(self.entries[self.used], {"url", "response"})
        if method != "GET" or body is not None or entry["url"] != url:
            message = "Ruby readback response order or origin mismatch"
            raise ValueError(message)
        response = ruby_response_from_document(entry["response"], maximum_bytes)
        self.used += 1
        return response


class _ReplayReader(RubyRegistryReader):
    """Preserve attested interpretation failure after its validated prefix."""

    interpretation_failure: str | None = None
    response_count: int = 0

    def _get(
        self,
        url: str,
        limit: int,
        responses: list[tuple[str, RubyHttpResponse]],
    ) -> RubyHttpResponse:
        response = super()._get(url, limit, responses)
        if (
            self.interpretation_failure is not None
            and len(responses) == self.response_count
        ):
            # Native timeout/filesystem failure need not recur offline. This
            # attests only unknown state, never the underlying failure cause.
            message = "retained Ruby interpretation failure"
            raise RUBY_OBSERVATION_FAILURES[self.interpretation_failure](
                message
            )
        return response


def ruby_response_from_document(
    value: JsonValue, maximum_bytes: int = RUBY_INDEX_LIMIT
) -> RubyHttpResponse:
    """Decode the exact bounded response; reject digest or shape drift."""
    doc = ruby_object(
        value, {"status", "content-type", "body-base64", "body-digest"}
    )
    encoded = _text(doc["body-base64"])
    if len(encoded) > 4 * ((maximum_bytes + 2) // 3):
        message = "Ruby retained response exceeds its byte budget"
        raise ValueError(message)
    response = RubyHttpResponse(
        cast("int", doc["status"]),
        base64.b64decode(encoded, validate=True),
        _text(doc["content-type"]),
    )
    if (
        type(response.status) is not int
        or not _MIN_STATUS <= response.status <= _MAX_STATUS
        or len(response.body) > maximum_bytes
        or canonicalize(ruby_response_document(response)) != canonicalize(doc)
    ):
        message = "Ruby retained response digest or status is invalid"
        raise ValueError(message)
    return response


def replay_ruby_observation(
    value: JsonValue, expected: RubyDistribution, registry: RubyRegistry
) -> RubyRegistryObservation:
    """Recompute exact/missing/conflicting state without network capability."""
    doc = ruby_object(
        value,
        {
            "schema",
            "registry",
            "version",
            "classification",
            "failure-kind",
            "failure-stage",
            "responses",
            "artifact-digest",
        },
    )
    entries = doc["responses"]
    if (
        not isinstance(entries, list)
        or len(entries) > RUBY_REGISTRY_REQUEST_LIMIT
    ):
        message = "Ruby readback response inventory is invalid"
        raise ValueError(message)
    failure = doc["failure-kind"]
    stage = doc["failure-stage"]
    if failure is None:
        valid_failure = stage is None and doc["classification"] != "unknown"
    else:
        valid_failure = (
            isinstance(failure, str)
            and failure in RUBY_OBSERVATION_FAILURES
            and stage in ("request", "interpretation")
            and doc["classification"] == "unknown"
            and doc["artifact-digest"] is None
            and (stage != "interpretation" or bool(entries))
        )
    if not valid_failure:
        message = "Ruby readback failure family or stage is invalid"
        raise ValueError(message)
    transport = _ReplayTransport(
        entries, failure if stage == "request" else None
    )
    reader = _ReplayReader(
        registry,
        RubyRequestBudget(transport),
        github_read_token="offline-replay"
        if registry.name == "github-packages"
        else None,
    )
    reader.response_count = len(entries)
    if stage == "interpretation":
        reader.interpretation_failure = cast("str", failure)
    result = reader.observe(expected)
    if transport.used != len(entries) or canonicalize(
        result.to_document()
    ) != canonicalize(doc):
        message = "Ruby registry observation differs from original responses"
        raise ValueError(message)
    return result
