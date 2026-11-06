"""Semantic rules of headers, measure blocks, voice lines and event marks.

These rules need no relation endpoints and no accidental state. Each check
yields its diagnostics, so that a validator can report every violation.
"""

from __future__ import annotations

from itertools import pairwise
from typing import TYPE_CHECKING, Final, Literal

from sheet_music_omr.diagnostics import semantic, unsupported
from sheet_music_omr.model import (
    Arpeggiate,
    Articulation,
    AssembledScore,
    Beam,
    ClefChange,
    CrossStaff,
    EndingKind,
    EndingOption,
    Fermata,
    FromOption,
    Grace,
    KeyOption,
    LeftRepeatOption,
    LenOption,
    MeasureRest,
    Note,
    Ornament,
    OrnamentKind,
    Rest,
    RightOption,
    SingleTremolo,
    Spacer,
    Stem,
    SystemDocument,
    TimedReview,
    TimeOption,
    TimeSymbol,
    TimeSymbolOption,
    ToOption,
    Tuplet,
    UntimedReview,
)
from sheet_music_omr.timeline import first_option, is_grace

if TYPE_CHECKING:
    from collections.abc import Iterable, Iterator, Sequence
    from fractions import Fraction

    from sheet_music_omr.diagnostics import Diagnostic, Location
    from sheet_music_omr.model import (
        Document,
        Event,
        MeasureBlock,
        Pitch,
        TupletItem,
    )
    from sheet_music_omr.timeline import Frame, Line, Placed, Timeline

type Findings = Iterator[Diagnostic]

UNKNOWN: Final = "unknown"
type Known[T] = T | Literal["unknown"]

STEPS: Final = "CDEFGAB"
_TRILL_DIGITS: Final = 2
_COMMON: Final = (4, 4)
_CUT: Final = (2, 2)
_OPENING: Final = frozenset({EndingKind.START, EndingKind.CONTINUE})
_CLOSING: Final = frozenset({EndingKind.CONTINUE, EndingKind.END})

_OPTION_NAMES: dict[type, str] = {
    LenOption: "len",
    KeyOption: "key",
    TimeOption: "time",
    TimeSymbolOption: "time-symbol",
    LeftRepeatOption: "left",
    RightOption: "right",
    FromOption: "from",
    ToOption: "to",
    EndingOption: "ending",
}
_START_OWNED = (
    KeyOption,
    TimeOption,
    TimeSymbolOption,
    LeftRepeatOption,
    EndingOption,
)


def is_initial(document: Document) -> bool:
    """Return whether the document starts at the start of the piece."""
    if isinstance(document, AssembledScore):
        return True
    carry_in = document.carry_in
    return carry_in.measure == 1 and carry_in.offset == 0


def ending_of(frame: Frame) -> Known[EndingOption | None]:
    """Return the ``ending`` of a block's measure, if the block owns it."""
    if not frame.owns_start:
        return UNKNOWN
    return first_option(frame.block, EndingOption)


def lines_of(timeline: Timeline, frame: Frame) -> list[Line]:
    """Return the lines of one block."""
    return [line for line in timeline.lines if line.frame.index == frame.index]


# Headers and state.


def check_headers(document: Document, timeline: Timeline) -> Findings:
    """Check the header and heading rules and the initial state."""
    if isinstance(document, SystemDocument):
        carry_in = document.carry_in
        if document.carry is not carry_in.accidental_scope:
            yield semantic(
                "carry_scope",
                f"carry is {document.carry} but carry-in.accidental_scope"
                f" is {carry_in.accidental_scope}",
                carry_in.location,
            )
        if carry_in.offset != 0:
            for heading in document.headings:
                yield unsupported(
                    "heading_in_measure",
                    "heading text above a system that starts inside a measure"
                    " is unsupported",
                    heading.location,
                )
    if is_initial(document):
        yield from _check_initial_block(document, timeline)


def _check_initial_block(document: Document, timeline: Timeline) -> Findings:
    block = timeline.frames[0].block
    for option in block.options:
        if isinstance(option, KeyOption) and option.fifths != document.key:
            yield semantic(
                "initial_state",
                "the first block of a piece may only restate the key header",
                option.location,
            )
        if isinstance(option, TimeOption) and option.meter != document.time:
            yield semantic(
                "initial_state",
                "the first block of a piece may only restate the time header",
                option.location,
            )
    for placed in timeline.events():
        if (
            placed.frame.index == 0
            and placed.onset == 0
            and isinstance(placed.event, ClefChange)
        ):
            yield semantic(
                "initial_state",
                "a clef direction at onset 0 of the first block of a piece"
                " is invalid; the clefs header gives the initial clefs",
                placed.event.location,
            )


# Measure blocks and options.


def check_blocks(document: Document, timeline: Timeline) -> Findings:
    """Check measure ids, block options, fragments and volta brackets."""
    first_id = 1
    if isinstance(document, SystemDocument):
        first_id = document.carry_in.measure
    last = len(timeline.frames) - 1
    for frame in timeline.frames:
        block = frame.block
        expected = first_id + frame.index
        if block.number != expected:
            yield semantic(
                "measure_id",
                f"measure id {block.number} shall be {expected}",
                block.location,
            )
        yield from _repeated_options(block)
        yield from _check_range(frame)
        yield from _check_fragment(document, frame, last)
        yield from _check_ownership(frame)
        yield from _check_time_symbol(frame)
        if not frame.owns_end:
            for line in lines_of(timeline, frame):
                yield from _check_fragment_grace(line)
    yield from _check_endings(document, timeline)


def _repeated_options(block: MeasureBlock) -> Findings:
    seen: set[type] = set()
    for option in block.options:
        kind = type(option)
        if kind in seen:
            yield semantic(
                "option_repeated",
                f"the option {_OPTION_NAMES[kind]} occurs more than once",
                option.location,
            )
        seen.add(kind)


def _check_range(frame: Frame) -> Findings:
    if not 0 <= frame.start < frame.end <= frame.length:
        yield semantic(
            "measure_range",
            f"the block covers {frame.start} to {frame.end}, which is not"
            f" within the effective length {frame.length}",
            frame.block.location,
        )


def _option_location(
    frame: Frame, kind: type[FromOption | ToOption]
) -> Location | None:
    option = first_option(frame.block, kind)
    return frame.block.location if option is None else option.location


def _check_fragment(document: Document, frame: Frame, last: int) -> Findings:
    block = frame.block
    if isinstance(document, AssembledScore):
        if not (frame.owns_start and frame.owns_end):
            yield semantic(
                "fragment",
                "every block of an assembled score covers its whole measure",
                block.location,
            )
        return
    start_location = _option_location(frame, FromOption)
    if frame.index == 0 and frame.start != document.carry_in.offset:
        yield semantic(
            "fragment",
            f"the first block starts at {frame.start} but carry-in.offset is"
            f" {document.carry_in.offset}",
            start_location,
        )
    if frame.index != 0 and not frame.owns_start:
        yield semantic(
            "fragment",
            "only the first block of a system document may start inside its"
            " measure",
            start_location,
        )
    if frame.index != last and not frame.owns_end:
        yield semantic(
            "fragment",
            "only the last block of a system document may end inside its"
            " measure",
            _option_location(frame, ToOption),
        )


def _check_ownership(frame: Frame) -> Findings:
    for option in frame.block.options:
        name = _OPTION_NAMES[type(option)]
        if isinstance(option, _START_OWNED) and not frame.owns_start:
            yield semantic(
                "option_owner",
                f"only a block with from=0 may carry {name}",
                option.location,
            )
        if isinstance(option, RightOption) and not frame.owns_end:
            yield semantic(
                "option_owner",
                "only a block that ends its measure may carry right",
                option.location,
            )


def _check_time_symbol(frame: Frame) -> Findings:
    option = first_option(frame.block, TimeSymbolOption)
    if option is None:
        return
    meter = (frame.meter.numerator, frame.meter.denominator)
    required = {TimeSymbol.COMMON: _COMMON, TimeSymbol.CUT: _CUT}.get(
        option.symbol,
    )
    if required is not None and meter != required:
        yield semantic(
            "time_symbol",
            f"time-symbol={option.symbol} requires the meter"
            f" {required[0]}/{required[1]}",
            option.location,
        )


def _check_fragment_grace(line: Line) -> Findings:
    main = [p.position for p in line.events if p.is_main_rhythmic]
    last = main[-1] if main else -1
    for placed in line.events[last + 1 :]:
        if placed.is_grace:
            yield semantic(
                "fragment_grace",
                "a block that ends inside its measure shall not hold a grace"
                " note after its last non-grace rhythmic event",
                placed.event.location,
            )


def _check_endings(document: Document, timeline: Timeline) -> Findings:
    frames = timeline.frames
    first = ending_of(frames[0])
    if (
        is_initial(document)
        and isinstance(first, EndingOption)
        and first.kind in _CLOSING
    ):
        yield semantic(
            "ending",
            f"the first measure of a piece cannot carry ending={first.kind}",
            first.location,
        )
    for previous, current in pairwise(frames):
        before, after = ending_of(previous), ending_of(current)
        if isinstance(before, str) or isinstance(after, str):
            continue
        opens = before is not None and before.kind in _OPENING
        closes = after is not None and after.kind in _CLOSING
        if opens != closes:
            message = (
                f"measure {current.measure} shall carry ending=continue or"
                " ending=end after an open volta bracket"
                if opens
                else f"ending={after.kind if after else ''} is valid only"
                " after ending=N:start or ending=continue"
            )
            location = current.block.location
            yield semantic(
                "ending",
                message,
                location if after is None else after.location,
            )


# Voice lines and durations.


def check_lines(timeline: Timeline) -> Findings:
    """Check voice line uniqueness, fill, tuplet groups and ``mrest``."""
    seen: set[tuple[int, int, int]] = set()
    for line in timeline.lines:
        frame, voice_line = line.frame, line.line
        key = (frame.index, voice_line.staff, voice_line.voice)
        name = f"{voice_line.staff}.{voice_line.voice}"
        if key in seen:
            yield semantic(
                "voice_line_repeated",
                f"the block has more than one line {name}",
                voice_line.location,
            )
        seen.add(key)
        span = frame.end - frame.start
        if frame.start < frame.end and line.total != span:
            yield semantic(
                "measure_fill",
                f"the line {name} lasts {line.total} but its block covers"
                f" {span}",
                voice_line.location,
            )
        yield from _check_measure_rest(line)
        yield from _check_tuplet_groups(voice_line.events)


def _check_measure_rest(line: Line) -> Findings:
    rhythmic = [p for p in line.events if not p.is_direction]
    for placed in rhythmic:
        if not isinstance(placed.event, MeasureRest):
            continue
        if not (line.frame.owns_start and line.frame.owns_end):
            yield unsupported(
                "split_measure_rest",
                "a whole-measure rest in a split measure is unsupported",
                placed.event.location,
            )
        elif len(rhythmic) > 1:
            yield semantic(
                "measure_rest",
                "mrest shall be the only rhythmic event of its line",
                placed.event.location,
            )


def _has_main_member(tuplet: Tuplet) -> bool:
    for item in tuplet.items:
        if isinstance(item, Tuplet):
            if _has_main_member(item):
                return True
        elif isinstance(item, (Note, Rest, Spacer)) and not is_grace(item):
            return True
    return False


def _check_tuplet_groups(items: Sequence[Event | TupletItem]) -> Findings:
    for item in items:
        if isinstance(item, Tuplet):
            if not _has_main_member(item):
                yield semantic(
                    "tuplet",
                    "a tuplet shall contain a rhythmic member that is not a"
                    " grace note",
                    item.location,
                )
            yield from _check_tuplet_groups(item.items)


# Event flags, pitches, directions and review markers.


_MARK_KEYS: Final[dict[type, str]] = {
    Fermata: "fermata",
    Arpeggiate: "arp",
    Stem: "stem",
    Grace: "grace",
    CrossStaff: "xs",
    Ornament: "orn",
    SingleTremolo: "tremolo",
}


def _mark_key(flag: object) -> object:
    if isinstance(flag, Articulation):
        return ("articulation", flag.kind)
    if isinstance(flag, Beam):
        return ("beam", flag.level)
    return _MARK_KEYS.get(type(flag))


def event_flags(event: object) -> tuple[object, ...]:
    """Return the flags of a note, rest or spacer, or no flags."""
    if isinstance(event, (Note, Rest)):
        return event.flags
    if isinstance(event, Spacer):
        return event.beams
    return ()


def _check_repeated_marks(event: object) -> Findings:
    seen: set[object] = set()
    for flag in event_flags(event):
        key = _mark_key(flag)
        if key is None:
            continue
        if key in seen:
            yield semantic(
                "repeated_mark",
                "an event shall not repeat a mark",
                getattr(flag, "location", None),
            )
        seen.add(key)


def accidental_token(pitch: Pitch) -> str:
    """Return the written accidental token of a pitch, or ``""``."""
    accidental = pitch.accidental
    if accidental is None:
        return ""
    if accidental.parenthesized:
        return f"({accidental.kind.value})"
    return accidental.kind.value


def chord_key(pitch: Pitch) -> tuple[int, str]:
    """Return the key that orders chord members."""
    return (pitch.octave * 7 + STEPS.index(pitch.step), accidental_token(pitch))


def _check_chord(note: Note) -> Findings:
    seen: set[tuple[str, object, int]] = set()
    for previous, pitch in pairwise(note.pitches):
        if chord_key(previous) > chord_key(pitch):
            yield semantic(
                "chord_order",
                "chord members shall ascend by diatonic position, then by"
                " accidental token",
                pitch.location,
            )
    for pitch in note.pitches:
        kind = None if pitch.accidental is None else pitch.accidental.kind
        identity = (pitch.step, kind, pitch.octave)
        if identity in seen:
            yield unsupported(
                "duplicate_chord_member",
                "a chord shall not repeat a step, accidental and octave",
                pitch.location,
            )
        seen.add(identity)


def _check_pitch(pitch: Pitch, *, trill: bool) -> Findings:
    digits = pitch.fingering
    if len(digits) == _TRILL_DIGITS and not (trill and digits[0] != digits[1]):
        yield unsupported(
            "fingering",
            "two fingering digits are valid only as two different digits on"
            " a pitch of a trill",
            pitch.location,
        )
    if (
        pitch.parenthesized
        and pitch.accidental is not None
        and pitch.accidental.parenthesized
    ):
        yield semantic(
            "parenthesized_accidental",
            "the accidental of a parenthesized pitch shall not be"
            " parenthesized again",
            pitch.accidental.location,
        )
    if pitch.arc_starts and pitch.tie_start is not None:
        yield semantic(
            "arc",
            "a pitch shall not start both an arc and a tie",
            pitch.location,
        )
    if list(pitch.arc_starts) != sorted(set(pitch.arc_starts)):
        yield semantic(
            "arc",
            "the arcs of a pitch shall be in ascending label order",
            pitch.location,
        )


def _check_note(placed: Placed, note: Note) -> Findings:
    for flag in note.flags:
        if isinstance(flag, CrossStaff) and flag.staff == placed.staff:
            yield semantic(
                "cross_staff",
                f"xs{flag.staff} shall name a staff other than the line's",
                flag.location,
            )
    yield from _check_chord(note)
    trill = any(
        isinstance(flag, Ornament) and flag.kind is OrnamentKind.TRILL
        for flag in note.flags
    )
    for pitch in note.pitches:
        yield from _check_pitch(pitch, trill=trill)


def _check_candidates(
    candidates: Iterable[str], location: Location | None
) -> Findings:
    listed = list(candidates)
    if len(set(listed)) != len(listed):
        yield semantic(
            "review_candidates",
            "no two candidates of one review marker shall be equal",
            location,
        )


def check_events(timeline: Timeline) -> Findings:
    """Check event marks, pitches, clef directions and review markers."""
    clefs: set[tuple[int, Fraction]] = set()
    for placed in timeline.events():
        event = placed.event
        yield from _check_repeated_marks(event)
        if isinstance(event, Note):
            yield from _check_note(placed, event)
        elif isinstance(event, (TimedReview, UntimedReview)):
            yield from _check_candidates(event.candidates, event.location)
        elif isinstance(event, ClefChange):
            key = (placed.staff, placed.time)
            if key in clefs:
                yield semantic(
                    "clef_change",
                    "two clef directions for one staff at one onset are"
                    " invalid",
                    event.location,
                )
            clefs.add(key)
