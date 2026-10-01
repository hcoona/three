"""Replay Ruby reads from original bytes and explicitly screened evidence."""

from __future__ import annotations

import base64
import re
from http import HTTPStatus
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3._ruby_native import ruby_object
from three_workflow_delivery_v3.adapters.ruby import inspect_ruby_distribution
from three_workflow_delivery_v3.adapters.ruby_registry import (
    RUBY_OBSERVATION_FAILURES,
    RubyHttpResponse,
    RubyRegistryObservation,
    RubyRegistryReader,
    RubyRequestBudget,
    RubyResponseEntry,
    RubyResponseEvidence,
    RubyScreenedResponse,
    ruby_continuation_request,
    ruby_response_document,
    ruby_safe_media,
    ruby_screen_response,
    ruby_storage_path,
)
from three_workflow_delivery_v3.adapters.rubygems import (
    RUBY_GEM_LIMIT,
    RUBY_INDEX_LIMIT,
    RUBY_REGISTRY_REQUEST_LIMIT,
    RubyRegistry,
)
from three_workflow_delivery_v3.canonical import JsonValue, canonicalize

if TYPE_CHECKING:
    from three_workflow_delivery_v3.adapters.ruby import RubyDistribution

_MIN_STATUS = 100
_MAX_STATUS = 599
_STORAGE_PATH_PARTS = 7
_DIGEST = re.compile(r"sha256:[0-9a-f]{64}\Z")


def _text(value: JsonValue) -> str:
    if not isinstance(value, str):
        message = "Ruby response string is invalid"
        raise TypeError(message)
    return value


class _ReplayTransport:
    """Consume closed logical entries; this object cannot perform a send."""

    def __init__(self, entries: list[JsonValue], failure: JsonValue) -> None:
        self.failure = failure
        self.entries = entries
        self.used = 0

    def request(
        self,
        method: str,
        url: str,
        headers: dict[str, str],
        body: bytes | None,
        maximum_bytes: int,
    ) -> RubyHttpResponse:
        del method, url, headers, body, maximum_bytes
        message = "Ruby offline replay cannot issue HTTP requests"
        raise ValueError(message)

    def _failure(self) -> None:
        message = "Ruby readback lacks the next native response"
        if isinstance(self.failure, str):
            raise RUBY_OBSERVATION_FAILURES[self.failure](message)
        raise ValueError(message)

    def read(
        self,
        request: str | dict[str, JsonValue],
        maximum_bytes: int,
    ) -> RubyResponseEvidence:
        if self.used >= len(self.entries):
            self._failure()
        key = "url" if isinstance(request, str) else "request"
        entry = ruby_object(self.entries[self.used], {key, "response"})
        if canonicalize(entry[key]) != canonicalize(request):
            message = "Ruby readback response order or origin mismatch"
            raise ValueError(message)
        if entry["response"] is None:
            if key != "request" or self.used != len(self.entries) - 1:
                message = "Ruby missing response is not a terminal continuation"
                raise ValueError(message)
            self.used += 1
            self._failure()
        response = ruby_response_from_document(entry["response"], maximum_bytes)
        self.used += 1
        return response


class _ReplayReader(RubyRegistryReader):
    """Consume attested failure and continuation joins without signed URLs."""

    interpretation_failure: str | None = None
    response_count: int = 0

    def __init__(
        self, registry: RubyRegistry, transport: _ReplayTransport
    ) -> None:
        super().__init__(
            registry,
            RubyRequestBudget(transport),
            github_read_token="offline-replay"
            if registry.name == "github-packages"
            else None,
        )
        self.replay = transport

    def observe(self, expected: RubyDistribution) -> RubyRegistryObservation:
        self.expected = expected
        return super().observe(expected)

    def _interpretation(self, responses: list[RubyResponseEntry]) -> None:
        self._failure_stage = "interpretation"
        if (
            self.interpretation_failure is not None
            and len(responses) == self.response_count
        ):
            message = "retained Ruby interpretation failure"
            raise RUBY_OBSERVATION_FAILURES[self.interpretation_failure](
                message
            )

    def _get(
        self,
        url: str,
        limit: int,
        responses: list[RubyResponseEntry],
    ) -> RubyResponseEvidence:
        self._failure_stage = "request"
        response = self.replay.read(url, limit)
        if (
            isinstance(response, RubyScreenedResponse)
            and response.safe_origin is not None
        ):
            ruby_storage_path(
                response.safe_origin,
                cast("str", response.safe_path),
                self.expected.witness.nbgv.native_version,
            )
        responses.append((url, ruby_screen_response(response)))
        self._interpretation(responses)
        return response

    def _continue_download(
        self,
        expected: RubyDistribution,
        responses: list[RubyResponseEntry],
        response: RubyResponseEvidence,
    ) -> RubyDistribution | None:
        if (
            not isinstance(response, RubyScreenedResponse)
            or response.reason != "redirect"
            or response.status != HTTPStatus.FOUND
            or response.safe_origin is None
            or response.safe_path is None
            or response.location_digest is None
        ):
            message = "Ruby replay has no admitted screened continuation"
            raise ValueError(message)
        ruby_storage_path(
            response.safe_origin,
            response.safe_path,
            expected.witness.nbgv.native_version,
        )
        logical = ruby_continuation_request(response)
        self._failure_stage = "request"
        responses.append((logical, None))
        storage = self.replay.read(logical, RUBY_GEM_LIMIT)
        responses[-1] = (logical, storage)
        self._interpretation(responses)
        if isinstance(storage, RubyScreenedResponse):
            if (
                storage.reason == "storage-mismatch"
                and storage.status == HTTPStatus.OK
                and (
                    storage.body_digest != expected.digest
                    or storage.body_size != len(expected.content)
                )
            ):
                return None
            message = (
                "Ruby screened storage evidence cannot establish exactness"
            )
            raise ValueError(message)
        if storage.status != HTTPStatus.OK or storage.body != expected.content:
            message = (
                "Ruby original storage bytes differ from the qualified gem"
            )
            raise ValueError(message)
        return inspect_ruby_distribution(
            expected.filename, storage.body, expected.witness
        )


def _screened_response(
    doc: dict[str, JsonValue], maximum_bytes: int
) -> RubyScreenedResponse:
    ruby_object(
        doc,
        {
            "kind",
            "status",
            "content-type",
            "body-size",
            "body-digest",
            "location-digest",
            "reason",
            "safe-origin",
            "safe-path",
        },
    )
    response = RubyScreenedResponse(
        cast("int", doc["status"]),
        _text(doc["content-type"]),
        cast("int", doc["body-size"]),
        _text(doc["body-digest"]),
        None
        if doc["location-digest"] is None
        else _text(doc["location-digest"]),
        _text(doc["reason"]),
        None if doc["safe-origin"] is None else _text(doc["safe-origin"]),
        None if doc["safe-path"] is None else _text(doc["safe-path"]),
    )
    route = response.safe_origin is not None or response.safe_path is not None
    if (
        type(response.status) is not int
        or not _MIN_STATUS <= response.status <= _MAX_STATUS
        or type(response.body_size) is not int
        or not 0 <= response.body_size <= maximum_bytes
        or not _DIGEST.fullmatch(response.body_digest)
        or (
            response.location_digest is not None
            and not _DIGEST.fullmatch(response.location_digest)
        )
        or ruby_safe_media(response.content_type) != response.content_type
        or response.reason
        not in {
            "redirect",
            "unexpected-location",
            "invalid-location",
            "storage-error",
            "storage-mismatch",
            "storage-unverified",
        }
        or (
            response.reason == "redirect"
            and not HTTPStatus.MULTIPLE_CHOICES
            <= response.status
            < HTTPStatus.BAD_REQUEST
        )
        or (
            response.reason == "unexpected-location"
            and response.location_digest is None
        )
        or (
            response.reason in {"storage-mismatch", "storage-unverified"}
            and response.status != HTTPStatus.OK
        )
        or (
            route
            and (
                response.reason != "redirect"
                or response.status != HTTPStatus.FOUND
                or response.location_digest is None
                or response.safe_origin is None
                or response.safe_path is None
            )
        )
    ):
        message = "Ruby screened response shape or bounds are invalid"
        raise ValueError(message)
    if route:
        path = cast("str", response.safe_path)
        parts = path.split("/")
        if len(parts) != _STORAGE_PATH_PARTS:
            message = "Ruby screened storage path is invalid"
            raise ValueError(message)
        ruby_storage_path(cast("str", response.safe_origin), path, parts[-2])
    return response


def ruby_response_from_document(
    value: JsonValue,
    maximum_bytes: int = RUBY_INDEX_LIMIT,
) -> RubyResponseEvidence:
    """Decode exactly one evidence arm; screened bytes are never fabricated."""
    doc = ruby_object(value)
    if doc.get("kind") == "screened":
        return _screened_response(doc, maximum_bytes)
    ruby_object(
        doc, {"kind", "status", "content-type", "body-base64", "body-digest"}
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
    value: JsonValue,
    expected: RubyDistribution,
    registry: RubyRegistry,
) -> RubyRegistryObservation:
    """Recompute state from ordered safe evidence without network access."""
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
    failure, stage = doc["failure-kind"], doc["failure-stage"]
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
    reader = _ReplayReader(registry, transport)
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
