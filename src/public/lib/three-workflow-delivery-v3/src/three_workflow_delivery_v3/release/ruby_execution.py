"""One durable Ruby upload lifetime and bounded post-success visibility."""

from __future__ import annotations

import math
import time
from dataclasses import dataclass
from datetime import datetime
from http import HTTPStatus
from http.client import HTTPException
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3._ruby_native import ruby_text
from three_workflow_delivery_v3.adapters.ruby_registry import (
    RubyHttpResponse,
    RubyRegistryReader,
    RubyRegistryWriter,
    ruby_response_document,
)
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.records.release import PUBLICATION_RESULT_SCHEMA
from three_workflow_delivery_v3.release.ruby_governance import (
    ruby_publisher_job,
)
from three_workflow_delivery_v3.release.ruby_publication import (
    RubyMutationMarker,
    _instant,
    _reference,
    exact_ruby_observation,
)
from three_workflow_delivery_v3.release.ruby_readback import (
    replay_ruby_observation,
)

if TYPE_CHECKING:
    from collections.abc import Callable
    from pathlib import Path

    from three_workflow_delivery_v3.adapters.ruby_registry import (
        RubyPublishingCredential,
    )
    from three_workflow_delivery_v3.records.artifacts import ArtifactReference
    from three_workflow_delivery_v3.records.release import (
        ReleaseAttemptIdentity,
    )

_MAX_READS = 6
_SPACING = 10.0
_WINDOW = 60.0


def _number(value: float) -> float:
    if type(value) not in {int, float} or not math.isfinite(value) or value < 0:
        message = "Ruby publication requires a finite monotonic clock"
        raise ValueError(message)
    return value


@dataclass(frozen=True, slots=True)
class RubyPublicationResult:
    """Retained singular-action facts, replayed by the terminal audit."""

    marker: RubyMutationMarker
    marker_reference: ArtifactReference
    status: str
    upload_response: RubyHttpResponse | None
    visibility: tuple[dict[str, JsonValue], ...]
    authority: bytes
    upload_completed_at: datetime | None = None
    upload_completed_monotonic: float | None = None
    error_kind: str | None = None

    def __post_init__(self) -> None:
        """Close response outcomes before allowing a scalar published result."""
        _reference(self.marker_reference, self.marker.to_document())
        response = self.upload_response
        if (
            self.status
            not in {"not-attempted", "unknown", "failed", "succeeded"}
            or len(self.visibility) > _MAX_READS
            or (self.status in {"failed", "succeeded"})
            != (response is not None)
            or (
                self.status == "succeeded"
                and response is not None
                and response.status != HTTPStatus.OK
            )
            or (
                self.status == "failed"
                and response is not None
                and response.status == HTTPStatus.OK
            )
            or (self.visibility and self.status != "succeeded")
            or (response is not None) != (self.upload_completed_at is not None)
            or (response is not None)
            != (self.upload_completed_monotonic is not None)
        ):
            message = "Ruby Result contradicts the one upload response"
            raise ValueError(message)

    @property
    def attempt(self) -> ReleaseAttemptIdentity:
        """Expose the common V3 terminal Attempt binding."""
        return self.marker.attempt

    @property
    def mutation_marker_reference(self) -> ArtifactReference:
        """Expose the common direct predecessor reference."""
        return self.marker_reference

    @property
    def result(self) -> str:
        """A definitive upload still requires verified final exact bytes."""
        if (
            self.status == "succeeded"
            and self.visibility
            and self.visibility[-1]["exact"] is True
        ):
            return "published"
        return "failed"

    @property
    def mutation_classification(self) -> str:
        """Preserve uncertainty even when rejected or interrupted."""
        if self.status == "not-attempted":
            return "not-mutated"
        return "mutated" if self.status == "succeeded" else "possibly-mutated"

    def to_document(self) -> dict[str, JsonValue]:
        """Retain responses, order and timing without credential material."""
        return {
            "schema": PUBLICATION_RESULT_SCHEMA,
            "variant": "ruby-gem",
            "attempt": self.marker.attempt.to_document(),
            "mutation-marker-reference": self.marker_reference.to_document(),
            "status": self.status,
            "upload-response": None
            if self.upload_response is None
            else ruby_response_document(self.upload_response),
            "upload-completed-at": None
            if self.upload_completed_at is None
            else _instant(self.upload_completed_at),
            "upload-completed-monotonic": self.upload_completed_monotonic,
            "visibility": cast("list[JsonValue]", list(self.visibility)),
            "authority": parse_canonical_json(self.authority),
            "error-kind": self.error_kind,
            "result": self.result,
            "mutation-classification": self.mutation_classification,
            "producer": ruby_publisher_job(self.marker.pre_state.registry),
        }

    @property
    def result_digest(self) -> str:
        """Return the immutable scalar Result identity."""
        return canonical_sha256(self.to_document())


def execute_ruby_publication(  # noqa: C901, PLR0913, PLR0915 - bounded ordered effect
    marker: RubyMutationMarker,
    marker_reference: ArtifactReference,
    payload: bytes,
    *,
    credential: RubyPublishingCredential | str,
    reader: RubyRegistryReader,
    writer: RubyRegistryWriter,
    claim_path: Path,
    deadline: datetime,
    clock: Callable[[], datetime],
    monotonic: Callable[[], float] = time.monotonic,
    wait: Callable[[float], None] = time.sleep,
) -> RubyPublicationResult:
    """Consume one claim; preserve rejection and ambiguity without recovery."""
    _reference(marker_reference, marker.to_document())
    snapshot = marker.authorization.bundle.snapshot
    decision = snapshot.observation.decision
    original = decision.artifact.inspect(payload)
    replay_ruby_observation(
        marker.pre_state.to_document(), original, snapshot.registry
    )
    if (
        reader.registry != snapshot.registry
        or writer.registry != snapshot.registry
        or reader.budget is not writer.budget
    ):
        message = (
            "Ruby publisher requires one shared destination request budget"
        )
        raise ValueError(message)
    admitted = clock()
    tick = _number(monotonic())
    marker.fresh_governance.require_live(admitted)
    expires = datetime.fromisoformat(
        ruby_text(marker.fresh_governance.document["expires-at"])
    )
    if (
        deadline.tzinfo is None
        or not marker.observed_at <= admitted < deadline <= expires
    ):
        message = "Ruby publication authority deadline is invalid"
        raise ValueError(message)
    deadline_tick = tick + (deadline - admitted).total_seconds()
    authority = canonicalize(
        {
            "admitted-at": _instant(admitted),
            "admitted-monotonic": tick,
            "deadline": _instant(deadline),
            "deadline-monotonic": deadline_tick,
        }
    )
    with claim_path.open("xb") as stream:
        stream.write(canonicalize(marker.to_document()))
    evidence = claim_path.with_name(claim_path.name + "-observations")
    evidence.mkdir(exist_ok=False)

    def retain(name: str, document: dict[str, JsonValue]) -> None:
        # A retention failure propagates: the durable marker has no Result.
        with (evidence / name).open("xb") as stream:
            stream.write(canonicalize(document))

    previous_utc = admitted
    previous_tick = tick

    def current() -> tuple[datetime, float]:
        nonlocal previous_utc, previous_tick
        utc, mono = clock(), _number(monotonic())
        if (
            utc < previous_utc
            or mono < previous_tick
            or utc >= deadline
            or mono >= deadline_tick
        ):
            message = "Ruby publication authority expired or clock regressed"
            raise ValueError(message)
        marker.fresh_governance.require_live(utc)
        previous_utc, previous_tick = utc, mono
        return utc, mono

    def result(  # noqa: PLR0913, PLR0917 - terminal evidence fields
        status: str,
        response: RubyHttpResponse | None = None,
        observations: tuple[dict[str, JsonValue], ...] = (),
        completed: datetime | None = None,
        completed_tick: float | None = None,
        error: str | None = None,
    ) -> RubyPublicationResult:
        return RubyPublicationResult(
            marker,
            marker_reference,
            status,
            response,
            observations,
            authority,
            completed,
            completed_tick,
            error,
        )

    try:
        now, _ = current()
    except ValueError as error:
        return result("not-attempted", error=type(error).__name__)
    try:
        response = writer.upload(original, credential, now=now)
    except (OSError, ValueError, TypeError, HTTPException) as error:
        return result("unknown", error=type(error).__name__)
    # Retain the definitive response even if completion crossed a deadline.
    completed, completed_tick = clock(), _number(monotonic())
    retain("upload.json", ruby_response_document(response))
    if response.status != HTTPStatus.OK:
        return result(
            "failed",
            response,
            completed=completed,
            completed_tick=completed_tick,
        )
    observations: list[dict[str, JsonValue]] = []
    terminal_error = None
    last_start = None
    for ordinal in range(_MAX_READS):
        try:
            if last_start is not None:
                wait(max(0.0, _SPACING - (_number(monotonic()) - last_start)))
            started, start_tick = current()
            if (
                started < completed
                or start_tick < completed_tick
                or start_tick >= completed_tick + _WINDOW
                or (started - completed).total_seconds() >= _WINDOW
            ):
                break
            if last_start is not None and start_tick - last_start < _SPACING:
                message = "Ruby visibility spacing was not satisfied"
                raise ValueError(message)  # noqa: TRY301 - terminal timing failure
            native = reader.observe(original)
            ended, end_tick = clock(), _number(monotonic())
        except (OSError, ValueError, TypeError) as error:
            terminal_error = type(error).__name__
            break
        timely = (
            started <= ended < deadline
            and start_tick <= end_tick < deadline_tick
            and 0 <= (ended - completed).total_seconds() < _WINDOW
            and completed_tick <= end_tick < completed_tick + _WINDOW
        )
        exact = timely and exact_ruby_observation(native, decision)
        observation: dict[str, JsonValue] = {
            "ordinal": ordinal,
            "started-at": _instant(started),
            "started-monotonic": start_tick,
            "completed-at": _instant(ended),
            "completed-monotonic": end_tick,
            "native": native.to_document(),
            "exact": exact,
        }
        retain(f"visibility-{ordinal}.json", observation)
        observations.append(observation)
        if not timely:
            terminal_error = "ValueError"
            break
        previous_utc, previous_tick = ended, end_tick
        if exact or native.classification != "missing":
            break
        last_start = start_tick
    return result(
        "succeeded",
        response,
        tuple(observations),
        completed,
        completed_tick,
        terminal_error,
    )
