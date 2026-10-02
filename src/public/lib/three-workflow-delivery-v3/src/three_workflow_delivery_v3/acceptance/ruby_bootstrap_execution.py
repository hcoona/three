"""One bootstrap credential/upload lifetime and bounded original readback."""

from __future__ import annotations

import os
import time
from dataclasses import dataclass
from datetime import datetime, timedelta
from http.client import HTTPException
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3._ruby_native import ruby_text
from three_workflow_delivery_v3.acceptance.ruby_bootstrap_contract import (
    require_bootstrap,
    require_bootstrap_transport,
)
from three_workflow_delivery_v3.acceptance.ruby_bootstrap_publication import (
    RubyBootstrapMarker,
    require_bootstrap_phase,
)
from three_workflow_delivery_v3.adapters.ruby import qualify_ruby_consumer
from three_workflow_delivery_v3.adapters.ruby_registry import (
    RubyRegistryWriter,
    RubyResponseEvidence,
    ruby_response_document,
    ruby_screen_response,
    ruby_upload_accepted,
)
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
)
from three_workflow_delivery_v3.records.ruby import validate_ruby_quality_detail
from three_workflow_delivery_v3.release.ruby_execution import _number
from three_workflow_delivery_v3.release.ruby_governance import (
    ruby_publisher_job,
)
from three_workflow_delivery_v3.release.ruby_publication import _instant
from three_workflow_delivery_v3.release.ruby_readback import (
    replay_ruby_observation,
)
from three_workflow_delivery_v3.repository.ruby_model import RUBY_QUALITY

if TYPE_CHECKING:
    from collections.abc import Callable
    from pathlib import Path

    from three_workflow_delivery_v3.adapters.ruby import RubyDistribution
    from three_workflow_delivery_v3.adapters.ruby_registry import (
        RubyRegistryReader,
    )
    from three_workflow_delivery_v3.records.artifacts import (
        ArtifactReference,
        ArtifactTransportIdentity,
    )

_MAX_VISIBILITY = 6
_SPACING = 10.0
_WINDOW = 60.0


def _retain(path: Path, document: dict[str, JsonValue]) -> None:
    with path.open("xb") as stream:
        stream.write(canonicalize(document))
        stream.flush()
        os.fsync(stream.fileno())
    directory = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY)
    try:
        os.fsync(directory)
    finally:
        os.close(directory)


@dataclass(frozen=True, slots=True)
class RubyBootstrapResult:
    """Definitive send response and replayable bounded visibility evidence."""

    marker: RubyBootstrapMarker
    marker_reference: ArtifactReference
    marker_transport: ArtifactTransportIdentity
    status: str
    response: RubyResponseEvidence | None
    visibility: tuple[dict[str, JsonValue], ...]
    started_at: datetime
    started_tick: float
    deadline: datetime
    completed_at: datetime | None = None
    completed_tick: float | None = None
    error_kind: str | None = None

    def __post_init__(self) -> None:
        """Require timely exact originals after a definitive HTTP 200."""
        publication = self.marker.publication
        require_bootstrap_transport(
            publication.run,
            self.marker_reference,
            self.marker_transport,
            canonicalize(self.marker.to_document()),
            producer=ruby_publisher_job(
                publication.run.inputs.configuration.registry, bootstrap=True
            ),
            payload_path="mutation-marker.json",
        )
        require_bootstrap(
            self.status in {"not-attempted", "unknown", "failed", "succeeded"}
            and (self.status in {"failed", "succeeded"})
            == (self.response is not None)
            and (self.response is not None) == (self.completed_at is not None)
            and (self.response is not None) == (self.completed_tick is not None)
            and len(self.visibility) <= _MAX_VISIBILITY
            and (not self.visibility or self.status == "succeeded")
            and self.started_at.tzinfo is not None
            and self.deadline.tzinfo is not None
            and self.marker.absence.observed_at
            <= self.started_at
            < self.deadline
            and self.error_kind
            in {
                None,
                "OSError",
                "ValueError",
                "TypeError",
                "HTTPException",
                "RuntimeError",
            },
            "Ruby bootstrap Result contradicts its one upload lifetime",
        )
        if self.response is not None:
            require_bootstrap(
                ruby_upload_accepted(self.response)
                == (self.status == "succeeded")
                and self.completed_at is not None
                and self.completed_at.tzinfo is not None
                and self.completed_at >= self.started_at
                and _number(cast("float", self.completed_tick))
                >= _number(self.started_tick),
                "Ruby bootstrap Result contradicts its HTTP response",
            )
        publication.run.require_current(self.started_at)
        expires = datetime.fromisoformat(
            ruby_text(publication.run.request.document["expires-at"])
        )
        require_bootstrap(
            self.deadline
            <= min(expires, self.started_at + timedelta(minutes=30)),
            "Ruby bootstrap Result exceeds operation authority",
        )
        deadline_tick = (
            _number(self.started_tick)
            + (self.deadline - self.started_at).total_seconds()
        )
        previous_tick = None
        previous_end = self.completed_at
        for ordinal, entry in enumerate(self.visibility):
            native = replay_ruby_observation(
                entry["native"],
                publication.original,
                publication.run.inputs.configuration.registry,
            )
            started = datetime.fromisoformat(ruby_text(entry["started-at"]))
            ended = datetime.fromisoformat(ruby_text(entry["completed-at"]))
            tick = _number(cast("float", entry["started-monotonic"]))
            end_tick = _number(cast("float", entry["completed-monotonic"]))
            completed = cast("datetime", self.completed_at)
            completed_tick = _number(cast("float", self.completed_tick))
            timely = (
                completed <= started <= ended < self.deadline
                and end_tick < deadline_tick
                and (ended - completed).total_seconds() < _WINDOW
                and completed_tick
                <= tick
                <= end_tick
                < completed_tick + _WINDOW
            )
            exact = timely and native.classification == "exact"
            require_bootstrap(
                type(entry["ordinal"]) is int
                and entry["ordinal"] == ordinal
                and (previous_tick is None or tick - previous_tick >= _SPACING)
                and (previous_end is None or started >= previous_end)
                and type(entry["exact"]) is bool
                and entry["exact"] == exact
                and (
                    ordinal == len(self.visibility) - 1
                    or (timely and native.classification == "missing")
                ),
                "Ruby bootstrap visibility contradicts native bytes or order",
            )
            previous_tick, previous_end = tick, ended

    @property
    def result(self) -> str:
        """Report bootstrap publication separately from normal admission."""
        return (
            "published"
            if (
                self.status == "succeeded"
                and self.error_kind is None
                and self.visibility
                and self.visibility[-1]["exact"] is True
            )
            else "failed"
        )

    def to_document(self) -> dict[str, JsonValue]:
        """Preserve failed and ambiguous effects without recovery."""
        return {
            "schema": (
                "workflow-delivery/v3/ruby-bootstrap-publication-result-v1"
            ),
            "run-binding-digest": self.marker.publication.run.binding_digest,
            "marker-reference": self.marker_reference.to_document(),
            "status": self.status,
            "response": None
            if self.response is None
            else ruby_response_document(self.response),
            "visibility": cast("JsonValue", list(self.visibility)),
            "started-at": _instant(self.started_at),
            "started-monotonic": self.started_tick,
            "deadline": _instant(self.deadline),
            "upload-completed-at": None
            if self.completed_at is None
            else _instant(self.completed_at),
            "upload-completed-monotonic": self.completed_tick,
            "error-kind": self.error_kind,
            "result": self.result,
            "mutation-classification": "not-mutated"
            if self.status == "not-attempted"
            else "mutated"
            if self.status == "succeeded"
            else "possibly-mutated",
            "normal-live-completion": False,
            "producer": ruby_publisher_job(
                self.marker.publication.run.inputs.configuration.registry,
                bootstrap=True,
            ),
        }


def execute_ruby_bootstrap_publication(  # noqa: C901, PLR0912, PLR0913, PLR0915 - ordered one-shot boundary
    marker: RubyBootstrapMarker,
    marker_reference: ArtifactReference,
    marker_transport: ArtifactTransportIdentity,
    *,
    reader: RubyRegistryReader,
    claim_path: Path,
    deadline: datetime,
    clock: Callable[[], datetime],
    check_authority: Callable[[], dict[str, JsonValue]],
    assertion: Callable[[], str] | None = None,
    github_token: str | None = None,
    monotonic: Callable[[], float] = time.monotonic,
    wait: Callable[[float], None] = time.sleep,
) -> RubyBootstrapResult:
    """Persist a spent lifetime before one assertion/exchange/upload.

    The reviewed hosted caller owns durable cross-job claims, actual authority
    checks and the single native OIDC call behind assertion. A claim path is
    never recreated after failure. Neither callback may retry. Credential
    responses remain solely in this process's writer and are never retained.
    """
    publication = marker.publication
    run = publication.run
    budget = require_bootstrap_phase(publication, reader, "execute")
    require_bootstrap_transport(
        run,
        marker_reference,
        marker_transport,
        canonicalize(marker.to_document()),
        producer=ruby_publisher_job(
            run.inputs.configuration.registry, bootstrap=True
        ),
        payload_path="mutation-marker.json",
    )
    registry = run.inputs.configuration.registry
    require_bootstrap(
        (
            registry.name == "rubygems"
            and assertion is not None
            and github_token is None
        )
        or (
            registry.name == "github-packages"
            and assertion is None
            and github_token is not None
        ),
        "Ruby bootstrap selected the wrong credential path",
    )
    started, start_tick = clock(), _number(monotonic())
    run.require_current(started)
    expires = datetime.fromisoformat(
        ruby_text(run.request.document["expires-at"])
    )
    require_bootstrap(
        deadline.tzinfo is not None
        and marker.absence.observed_at
        <= started
        < deadline
        <= min(expires, budget.deadline, started + timedelta(minutes=30)),
        "Ruby bootstrap execution exceeds its admitted lifetime",
    )
    deadline_tick = start_tick + (deadline - started).total_seconds()
    _retain(claim_path, marker.to_document())
    evidence = claim_path.with_name(claim_path.name + "-observations")
    evidence.mkdir(exist_ok=False)
    writer = RubyRegistryWriter(registry, budget)
    original = publication.original
    previous_utc, previous_tick = started, start_tick

    def current() -> tuple[datetime, float]:
        nonlocal previous_utc, previous_tick
        utc, tick = clock(), _number(monotonic())
        require_bootstrap(
            previous_utc <= utc < deadline
            and previous_tick <= tick < deadline_tick,
            "Ruby bootstrap authority expired or clock regressed",
        )
        run.require_current(utc)
        previous_utc, previous_tick = utc, tick
        return utc, tick

    def finish(  # noqa: PLR0913, PLR0917 - retained terminal fields
        status: str,
        response: RubyResponseEvidence | None = None,
        observations: tuple[dict[str, JsonValue], ...] = (),
        completed: datetime | None = None,
        tick: float | None = None,
        error: str | None = None,
    ) -> RubyBootstrapResult:
        result = RubyBootstrapResult(
            marker,
            marker_reference,
            marker_transport,
            status,
            response,
            observations,
            started,
            start_tick,
            deadline,
            completed,
            tick,
            error,
        )
        _retain(evidence / "result.json", result.to_document())
        return result

    try:
        _retain(evidence / "credential-authority.json", check_authority())
        now, _ = current()
        if assertion is not None:
            jwt = assertion()
            now, _ = current()
            try:
                credential = writer.exchange(
                    jwt, now=now, bootstrap=True, monotonic=monotonic
                )
            finally:
                del jwt
                if writer.exchange_receipt is not None:
                    _retain(
                        evidence / "exchange-receipt.json",
                        writer.exchange_receipt,
                    )
        else:
            credential = cast("str", github_token)
        _retain(evidence / "upload-authority.json", check_authority())
        now, _ = current()
    except (
        OSError,
        ValueError,
        TypeError,
        HTTPException,
        RuntimeError,
    ) as error:
        return finish("not-attempted", error=_error(error))
    try:
        response = ruby_screen_response(
            writer.upload(original, credential, now=now)
        )
    except (
        OSError,
        ValueError,
        TypeError,
        HTTPException,
        RuntimeError,
    ) as error:
        return finish("unknown", error=_error(error))
    completed, completed_tick = clock(), _number(monotonic())
    _retain(evidence / "upload.json", ruby_response_document(response))
    if not ruby_upload_accepted(response):
        return finish(
            "failed", response, completed=completed, tick=completed_tick
        )
    observations: list[dict[str, JsonValue]] = []
    error_kind = None
    last_start = None
    for ordinal in range(_MAX_VISIBILITY):
        try:
            if last_start is not None:
                wait(max(0.0, _SPACING - (_number(monotonic()) - last_start)))
            observed, tick = current()
            if (
                observed < completed
                or tick < completed_tick
                or tick >= completed_tick + _WINDOW
                or (observed - completed).total_seconds() >= _WINDOW
            ):
                break
            require_bootstrap(
                last_start is None or tick - last_start >= _SPACING,
                "Ruby bootstrap visibility spacing failed",
            )
            native = reader.observe(original)
            ended, end_tick = clock(), _number(monotonic())
        except (OSError, ValueError, TypeError, RuntimeError) as error:
            error_kind = _error(error)
            break
        timely = (
            observed <= ended < deadline
            and tick <= end_tick < deadline_tick
            and 0 <= (ended - completed).total_seconds() < _WINDOW
            and completed_tick <= end_tick < completed_tick + _WINDOW
        )
        entry: dict[str, JsonValue] = {
            "ordinal": ordinal,
            "started-at": _instant(observed),
            "started-monotonic": tick,
            "completed-at": _instant(ended),
            "completed-monotonic": end_tick,
            "native": native.to_document(),
            "exact": timely and native.classification == "exact",
        }
        _retain(evidence / f"visibility-{ordinal}.json", entry)
        observations.append(entry)
        if not timely:
            error_kind = "ValueError"
            break
        previous_utc, previous_tick = ended, end_tick
        if native.classification != "missing":
            break
        last_start = tick
    return finish(
        "succeeded",
        response,
        tuple(observations),
        completed,
        completed_tick,
        error_kind,
    )


def _error(error: Exception) -> str:
    # Never retain arbitrary exceptions or credential-bearing bodies.
    if isinstance(error, HTTPException):
        return "HTTPException"
    if isinstance(error, OSError):
        return "OSError"
    if isinstance(error, TypeError):
        return "TypeError"
    if isinstance(error, ValueError):
        return "ValueError"
    return "RuntimeError"


def verify_ruby_bootstrap_remote(
    result: RubyBootstrapResult,
    reader: RubyRegistryReader,
    *,
    clock: Callable[[], datetime],
    check_authority: Callable[[], dict[str, JsonValue]],
    consumer: Callable[
        [RubyDistribution], dict[str, JsonValue]
    ] = qualify_ruby_consumer,
) -> dict[str, JsonValue]:
    """Download exact original and install it in a separate unprivileged job.

    This evidence does not independently audit its author, inspect postcreation
    ownership/registration or admit normal Live. Those remain external gates.
    """
    publication = result.marker.publication
    require_bootstrap(
        result.result == "published",
        "A failed bootstrap cannot become successful through later readback",
    )
    require_bootstrap_phase(publication, reader, "remote-consumer")
    publication.run.require_current(clock())
    authority = check_authority()
    publication.run.require_current(clock())
    native = reader.observe(publication.original)
    replayed = replay_ruby_observation(
        native.to_document(), publication.original, reader.registry
    )
    require_bootstrap(
        replayed.classification == "exact"
        and replayed.distribution is not None,
        "Ruby bootstrap final original is not exact",
    )
    detail = consumer(cast("RubyDistribution", replayed.distribution))
    validate_ruby_quality_detail(
        publication.artifact, RUBY_QUALITY[1], "passed", canonicalize(detail)
    )
    completed = clock()
    publication.run.require_current(completed)
    return {
        "schema": "workflow-delivery/v3/ruby-bootstrap-remote-consumer-v1",
        "result-digest": canonical_sha256(result.to_document()),
        "native": replayed.to_document(),
        "consumer": detail,
        "authority": authority,
        "completed-at": _instant(completed),
        "normal-live-completion": False,
        "producer": "verify-ruby-bootstrap-remote",
    }
