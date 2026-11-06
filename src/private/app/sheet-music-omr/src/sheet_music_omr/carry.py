"""Carry-in validity and carry-out derivation of the "Carry state" section."""

from __future__ import annotations

from fractions import Fraction
from itertools import pairwise
from typing import TYPE_CHECKING, Final

from sheet_music_omr.diagnostics import semantic
from sheet_music_omr.model import (
    CarriedAccidental,
    CarryMode,
    CarryState,
    Endpoint,
    EventCount,
    Note,
    OpenRelation,
    OttavaEnd,
    OttavaStart,
    RelationKind,
    SoundingPitch,
)
from sheet_music_omr.relations import ARC, TIE, TREMOLO

if TYPE_CHECKING:
    from collections.abc import Iterator, Sequence

    from sheet_music_omr.diagnostics import Diagnostic, Location
    from sheet_music_omr.model import SystemDocument
    from sheet_music_omr.pitches import PitchResolver
    from sheet_music_omr.relations import Point, Relation, RelationAnalysis
    from sheet_music_omr.timeline import Timeline

_BASES: Final = (1, 2, 4, 8, 16, 32, 64, 128)
_PITCHED = frozenset({RelationKind.TIE, RelationKind.ARC})


def is_written_value(value: Fraction) -> bool:
    """Return whether a rational is the value of a written duration.

    A base with ``d`` dots has the value ``(2**(d + 1) - 1) / (base * 2**d)``
    in lowest terms, and the format allows any number of dots.
    """
    numerator = value.numerator
    if numerator <= 0 or numerator & (numerator + 1):
        return False
    dots = numerator.bit_length() - 1
    base, remainder = divmod(value.denominator, 2**dots)
    return remainder == 0 and base in _BASES


def _accidental_key(entry: CarriedAccidental) -> tuple[int, str, int]:
    return (
        entry.staff,
        entry.step,
        -1 if entry.octave is None else entry.octave,
    )


def _check_order(carry: CarryState) -> Iterator[Diagnostic]:
    yield from _check_ascending(
        [((e.staff, e.voice), e.location) for e in carry.event_counts],
        "event_counts",
    )
    yield from _check_ascending(
        [(_accidental_key(e), e.location) for e in carry.accidentals],
        "accidentals",
    )
    yield from _check_ascending(
        [((e.label,), e.location) for e in carry.open],
        "open",
    )


def _check_ascending(
    keys: Sequence[tuple[tuple[int | str, ...], Location | None]], name: str
) -> Iterator[Diagnostic]:
    for (previous, _), (current, location) in pairwise(keys):
        if previous >= current:
            yield semantic(
                "carry_order",
                f"the entries of {name} shall strictly ascend",
                location,
            )


def _check_empty(carry: CarryState) -> Iterator[Diagnostic]:
    if carry.offset == 0:
        for entry in (*carry.event_counts, *carry.accidentals):
            yield semantic(
                "carry_in",
                "event_counts and accidentals are empty at offset 0",
                entry.location,
            )
        if carry.measure == 1:
            for relation in carry.open:
                yield semantic(
                    "carry_in",
                    "nothing is open at the start of a piece",
                    relation.location,
                )
    for count in carry.event_counts:
        if count.count <= 0:
            yield semantic(
                "carry_in",
                "an event count shall be positive",
                count.location,
            )


def _check_accidentals(carry: CarryState) -> Iterator[Diagnostic]:
    by_octave = carry.accidental_scope is CarryMode.OCTAVE
    for entry in carry.accidentals:
        if (entry.octave is not None) != by_octave:
            yield semantic(
                "carry_in",
                "an accidental entry has an octave exactly when the scope is"
                " octave",
                entry.location,
            )


def _check_open(carry: CarryState) -> Iterator[Diagnostic]:
    counts = {(c.staff, c.voice): c.count for c in carry.event_counts}
    for relation in carry.open:
        start = relation.start
        if (start.member_index is not None) != (relation.kind in _PITCHED):
            yield semantic(
                "carry_in",
                "member_index is set exactly for a tie or an arc",
                start.location,
            )
        position = (start.measure, start.offset)
        if position > (carry.measure, carry.offset) or (
            start.measure == carry.measure
            and start.event_index > counts.get((start.staff, start.voice), 0)
        ):
            yield semantic(
                "carry_in",
                "an open relation shall start before the carry position",
                start.location,
            )
        if relation.kind is RelationKind.TREMOLO and not (
            relation.written is not None
            and is_written_value(relation.written)
            and relation.scale is not None
            and relation.scale > 0
        ):
            yield semantic(
                "carry_in",
                "a tremolo entry needs a written duration value and a positive"
                " scale",
                relation.location,
            )


def check_carry_in(document: SystemDocument) -> Iterator[Diagnostic]:
    """Check the order and the consistency of a ``carry-in`` state."""
    carry = document.carry_in
    yield from _check_order(carry)
    yield from _check_empty(carry)
    yield from _check_accidentals(carry)
    yield from _check_open(carry)


# Carry-out.


def _start_endpoint(point: Point) -> Endpoint:
    placed = point.placed
    return Endpoint(
        placed.frame.measure,
        placed.onset,
        placed.staff,
        placed.voice,
        placed.index,
        point.member,
    )


def _open_entry(relation: Relation, resolver: PitchResolver) -> OpenRelation:
    if relation.carried is not None:
        return relation.carried
    start = relation.start
    if start is None:
        message = f"r{relation.label} has no start"
        raise ValueError(message)
    kind = RelationKind(relation.kind)
    pitch = variant = written = scale = None
    if relation.kind in {TIE, ARC} and start.member is not None:
        sounding = resolver.resolved[(start.placed.ident, start.member)]
        alter = sounding.alter
        pitch = SoundingPitch(
            sounding.step,
            sounding.octave,
            alter if alter is not None else 0,
        )
    elif relation.kind == TREMOLO:
        event = start.placed.event
        if isinstance(event, Note):
            written = event.duration.value
        scale = start.placed.scale
        variant = start.variant
    else:
        variant = start.variant
        event = start.placed.event
        if isinstance(event, (OttavaStart, OttavaEnd)):
            variant = event.sign.value
    return OpenRelation(
        relation.label,
        kind,
        _start_endpoint(start),
        pitch,
        variant,
        written,
        scale,
    )


def _counts(
    document: SystemDocument, timeline: Timeline
) -> tuple[EventCount, ...]:
    last = timeline.frames[-1]
    counts: dict[tuple[int, int], int] = {}
    if last.index == 0:
        for entry in document.carry_in.event_counts:
            counts[(entry.staff, entry.voice)] = entry.count
    for line in timeline.lines:
        if line.frame.index == last.index and line.events:
            key = (line.line.staff, line.line.voice)
            counts[key] = counts.get(key, 0) + len(line.events)
    return tuple(
        EventCount(staff, voice, count)
        for (staff, voice), count in sorted(counts.items())
        if count
    )


def derive_carry_out(
    document: SystemDocument,
    timeline: Timeline,
    relations: RelationAnalysis,
    resolver: PitchResolver,
) -> CarryState:
    """Derive the carry-out of a valid system document."""
    last = timeline.frames[-1]
    open_entries = tuple(
        _open_entry(relation, resolver)
        for relation in sorted(
            relations.relations.values(), key=lambda r: r.label
        )
        if relation.is_open
    )
    scope = document.carry
    if last.owns_end:
        return CarryState(
            last.measure + 1, Fraction(0), (), scope, (), open_entries
        )
    accidentals = tuple(
        CarriedAccidental(staff, step, octave, alter)
        for (staff, step, octave), alter in sorted(
            resolver.state.items(),
            key=lambda item: (item[0][0], item[0][1], item[0][2] or 0),
        )
    )
    return CarryState(
        last.measure,
        last.end,
        _counts(document, timeline),
        scope,
        accidentals,
        open_entries,
    )
