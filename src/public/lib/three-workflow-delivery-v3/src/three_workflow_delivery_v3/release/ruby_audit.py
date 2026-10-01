"""Strict Ruby publication imports and terminal evidence replay."""

from __future__ import annotations

from datetime import datetime
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3._ruby_native import ruby_object, ruby_text
from three_workflow_delivery_v3.adapters.rubygems import (
    RUBY_HTTP_RESPONSE_LIMIT,
)
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.records.artifacts import (
    artifact_reference_from_document,
)
from three_workflow_delivery_v3.release.ruby_execution import (
    RubyPublicationResult,
    _number,
)
from three_workflow_delivery_v3.release.ruby_publication import (
    RubyApprovalBundle,
    RubyMutationMarker,
    RubyPublicationAuthorization,
    RubyPublicationSnapshot,
    RubyRemoteObservation,
    exact_ruby_observation,
)
from three_workflow_delivery_v3.release.ruby_readback import (
    replay_ruby_observation,
    ruby_response_from_document,
)

if TYPE_CHECKING:
    from three_workflow_delivery_v3.release.ruby_governance import (
        RubyGovernance,
    )
    from three_workflow_delivery_v3.release.ruby_qualification import (
        RubyQualificationDecision,
    )

_MAX_READS = 6
_SPACING = 10.0
_WINDOW = 60.0


def _same(value: JsonValue, expected: dict[str, JsonValue]) -> None:
    if canonicalize(value) != canonicalize(expected):
        message = (
            "Ruby publication record differs from its current predecessors"
        )
        raise ValueError(message)


def _time(value: JsonValue) -> datetime:
    instant = datetime.fromisoformat(ruby_text(value))
    if instant.tzinfo is None:
        message = "Ruby publication evidence lacks an aware timestamp"
        raise ValueError(message)
    return instant


def ruby_remote_observation_from_document(
    value: JsonValue, decision: RubyQualificationDecision, payload: bytes
) -> RubyRemoteObservation:
    """Replay native evidence against this independently qualified original."""
    doc = ruby_object(value)
    native = replay_ruby_observation(
        doc.get("native"),
        decision.artifact.inspect(payload),
        decision.snapshot.governance.registry,
    )
    result = RubyRemoteObservation(
        decision,
        artifact_reference_from_document(doc.get("decision-reference")),
        native,
        _time(doc.get("observed-at")),
    )
    _same(value, result.to_document())
    return result


def ruby_publication_snapshot_from_document(
    value: JsonValue, observation: RubyRemoteObservation
) -> RubyPublicationSnapshot:
    """Materialize zero/one action again from its exact observation."""
    doc = ruby_object(value)
    result = RubyPublicationSnapshot(
        observation,
        artifact_reference_from_document(doc.get("observation-reference")),
    )
    _same(value, result.to_document())
    return result


def ruby_approval_bundle_from_document(
    value: JsonValue, snapshot: RubyPublicationSnapshot
) -> RubyApprovalBundle:
    """Reconstruct the summary binding; reject approval for zero actions."""
    doc = ruby_object(value)
    result = RubyApprovalBundle(
        snapshot,
        artifact_reference_from_document(doc.get("snapshot-reference")),
        artifact_reference_from_document(doc.get("summary-reference")),
    )
    _same(value, result.to_document())
    return result


def ruby_authorization_from_document(
    value: JsonValue, bundle: RubyApprovalBundle
) -> RubyPublicationAuthorization:
    """Require this Bundle and the native current-run Environment proof."""
    doc = ruby_object(value)
    result = RubyPublicationAuthorization(
        bundle,
        artifact_reference_from_document(doc.get("bundle-reference")),
        canonicalize(doc.get("approval-evidence")),
        _time(doc.get("completed-at")),
    )
    _same(value, result.to_document())
    return result


def ruby_marker_from_document(
    value: JsonValue,
    authorization: RubyPublicationAuthorization,
    fresh_governance: RubyGovernance,
    payload: bytes,
) -> RubyMutationMarker:
    """Replay the final missing proof before admitting durable authority."""
    doc = ruby_object(value)
    decision = authorization.bundle.snapshot.observation.decision
    native = replay_ruby_observation(
        doc.get("pre-state"),
        decision.artifact.inspect(payload),
        fresh_governance.registry,
    )
    result = RubyMutationMarker(
        authorization,
        artifact_reference_from_document(doc.get("authorization-reference")),
        fresh_governance,
        native,
        _time(doc.get("observed-at")),
    )
    _same(value, result.to_document())
    return result


def ruby_publication_result_from_document(
    value: JsonValue, marker: RubyMutationMarker, payload: bytes
) -> RubyPublicationResult:
    """Strictly import then replay every retained upload/visibility claim."""
    doc = ruby_object(value)
    response = doc.get("upload-response")
    visibility = doc.get("visibility")
    if not isinstance(visibility, list):
        message = "Ruby Result visibility must be an ordered list"
        raise TypeError(message)
    result = RubyPublicationResult(
        marker,
        artifact_reference_from_document(doc.get("mutation-marker-reference")),
        ruby_text(doc.get("status")),
        None
        if response is None
        else ruby_response_from_document(response, RUBY_HTTP_RESPONSE_LIMIT),
        tuple(ruby_object(item) for item in visibility),
        canonicalize(doc.get("authority")),
        None
        if doc.get("upload-completed-at") is None
        else _time(doc["upload-completed-at"]),
        None
        if doc.get("upload-completed-monotonic") is None
        else _number(cast("float", doc["upload-completed-monotonic"])),
        None if doc.get("error-kind") is None else ruby_text(doc["error-kind"]),
    )
    _same(value, result.to_document())
    audit_ruby_publication_result(result, payload)
    return result


def audit_ruby_publication_result(
    result: RubyPublicationResult, payload: bytes
) -> None:
    """Recompute success from original bytes and ordered bounded evidence."""
    marker = result.marker
    decision = marker.authorization.bundle.snapshot.observation.decision
    original = decision.artifact.inspect(payload)
    replay_ruby_observation(
        marker.pre_state.to_document(), original, marker.pre_state.registry
    )
    authority = ruby_object(
        parse_canonical_json(result.authority),
        {"admitted-at", "admitted-monotonic", "deadline", "deadline-monotonic"},
    )
    admitted = _time(authority["admitted-at"])
    tick = _number(cast("float", authority["admitted-monotonic"]))
    deadline = _time(authority["deadline"])
    deadline_tick = _number(cast("float", authority["deadline-monotonic"]))
    expires = _time(marker.fresh_governance.document["expires-at"])
    marker.fresh_governance.require_live(admitted)
    if (
        not marker.observed_at <= admitted < deadline <= expires
        or deadline_tick != tick + (deadline - admitted).total_seconds()
    ):
        message = "Ruby Result authority window differs from its marker"
        raise ValueError(message)
    if result.upload_response is None:
        return
    completed = result.upload_completed_at
    completed_tick = result.upload_completed_monotonic
    if (
        completed is None
        or completed_tick is None
        or completed < admitted
        or completed_tick < tick
    ):
        message = "Ruby upload completion predates its admitted effect"
        raise ValueError(message)
    previous_end, previous_tick = completed, completed_tick
    last_start = None
    terminal = False
    if len(result.visibility) > _MAX_READS:
        message = "Ruby visibility count exceeds the operation profile"
        raise ValueError(message)
    for ordinal, item in enumerate(result.visibility):
        doc = ruby_object(
            item,
            {
                "ordinal",
                "started-at",
                "started-monotonic",
                "completed-at",
                "completed-monotonic",
                "native",
                "exact",
            },
        )
        started, ended = _time(doc["started-at"]), _time(doc["completed-at"])
        start_tick = _number(cast("float", doc["started-monotonic"]))
        end_tick = _number(cast("float", doc["completed-monotonic"]))
        if (
            terminal
            or type(doc["ordinal"]) is not int
            or doc["ordinal"] != ordinal
            or not previous_end <= started < deadline
            or not previous_tick <= start_tick < deadline_tick
            or not 0 <= (started - completed).total_seconds() < _WINDOW
            or not completed_tick <= start_tick < completed_tick + _WINDOW
            or (last_start is not None and start_tick - last_start < _SPACING)
            or type(doc["exact"]) is not bool
        ):
            message = "Ruby visibility order, spacing or authority is invalid"
            raise ValueError(message)
        native = replay_ruby_observation(
            doc["native"], original, marker.pre_state.registry
        )
        timely = (
            started <= ended < deadline
            and start_tick <= end_tick < deadline_tick
            and 0 <= (ended - completed).total_seconds() < _WINDOW
            and completed_tick <= end_tick < completed_tick + _WINDOW
        )
        exact = timely and exact_ruby_observation(native, decision)
        if doc["exact"] is not exact:
            message = "Ruby Result exactness differs from original native bytes"
            raise ValueError(message)
        terminal = not timely or exact or native.classification != "missing"
        previous_end, previous_tick, last_start = ended, end_tick, start_tick
    if result.result == "published" and result.error_kind is not None:
        message = "Ruby successful Result cannot retain a terminal failure"
        raise ValueError(message)
