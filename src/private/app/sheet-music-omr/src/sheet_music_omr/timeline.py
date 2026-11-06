"""Onsets, durations and event indices of the events of a document.

A timeline places every event of a document, depth first through tuplet
containers, with the onset, duration, tuplet multiplier and 1-based,
measure-global event index that the "Voice lines and durations" and "Event
indexing" sections of the format define. Measures follow one another on one
time axis, so that rules that cross a barline, such as ties, compare times.

The timeline takes the first occurrence of a repeated block option, so that it
can place the events of a document that a block rule rejects.
"""

from __future__ import annotations

from dataclasses import dataclass
from fractions import Fraction
from typing import TYPE_CHECKING

from sheet_music_omr.model import (
    AssembledScore,
    ClefChange,
    CrossStaff,
    DashesEnd,
    DashesStart,
    Dynamic,
    FromOption,
    Grace,
    HairpinEnd,
    HairpinStart,
    KeyOption,
    LenOption,
    MeasureRest,
    Note,
    OttavaEnd,
    OttavaStart,
    PedalLine,
    PedalSign,
    Rehearsal,
    Rest,
    Spacer,
    SystemDocument,
    Tempo,
    TimedReview,
    TimeOption,
    ToOption,
    TremoloRelation,
    TrillEnd,
    Tuplet,
    UntimedReview,
    Words,
)

if TYPE_CHECKING:
    from collections.abc import Iterator, Sequence

    from sheet_music_omr.model import (
        Direction,
        Document,
        Event,
        MeasureBlock,
        MeasureOption,
        Meter,
        TupletItem,
        VoiceLine,
    )

DIRECTION_TYPES = (
    Dynamic,
    ClefChange,
    Words,
    Tempo,
    Rehearsal,
    PedalSign,
    UntimedReview,
    HairpinStart,
    HairpinEnd,
    OttavaStart,
    OttavaEnd,
    PedalLine,
    DashesStart,
    DashesEnd,
    TrillEnd,
)

type LeafEvent = Direction | Note | Rest | Spacer | MeasureRest | TimedReview


def first_option[T](block: MeasureBlock, kind: type[T]) -> T | None:
    """Return the first option of a kind in a block, if any."""
    for option in block.options:
        if isinstance(option, kind):
            return option
    return None


def document_blocks(document: Document) -> tuple[MeasureBlock, ...]:
    """Return the measure blocks of a document in written order."""
    if isinstance(document, AssembledScore):
        return tuple(headed.block for headed in document.blocks)
    return document.blocks


def is_grace(event: LeafEvent) -> bool:
    """Return whether an event is a grace note."""
    return isinstance(event, Note) and any(
        isinstance(flag, Grace) for flag in event.flags
    )


@dataclass(frozen=True, slots=True)
class Frame:
    """The measure context of one block.

    Attributes:
        index: The position of the block in the document, from 0.
        block: The block.
        key: The key signature in effect in the measure.
        meter: The meter in effect in the measure.
        length: The effective length of the measure.
        start: The ``from`` of the block.
        end: The ``to`` of the block.
        origin: The time of the measure start on the document's time axis.
    """

    index: int
    block: MeasureBlock
    key: int
    meter: Meter
    length: Fraction
    start: Fraction
    end: Fraction
    origin: Fraction

    @property
    def measure(self) -> int:
        """Return the measure id of the block."""
        return self.block.number

    @property
    def owns_start(self) -> bool:
        """Return whether the block starts at the start of its measure."""
        return self.start == 0

    @property
    def owns_end(self) -> bool:
        """Return whether the block ends at the end of its measure."""
        return self.end == self.length


@dataclass(frozen=True, slots=True)
class Placed:
    """An event placed in its measure.

    Attributes:
        event: The event; tuplet containers are never placed.
        frame: The measure context of the event's block.
        line_index: The position of the event's line in its block.
        staff: The staff of the event's line.
        voice: The voice of the event's line.
        index: The 1-based, measure-global event index.
        position: The position of the event in its line, from 0.
        onset: The onset in the measure.
        duration: The time that the event takes.
        scale: The product of the tuplet group ratios that hold the event.
    """

    event: LeafEvent
    frame: Frame
    line_index: int
    staff: int
    voice: int
    index: int
    position: int
    onset: Fraction
    duration: Fraction
    scale: Fraction

    @property
    def ident(self) -> tuple[int, int, int]:
        """Return a key that identifies the event within its document."""
        return (self.frame.index, self.line_index, self.position)

    @property
    def time(self) -> Fraction:
        """Return the onset on the document's time axis."""
        return self.frame.origin + self.onset

    @property
    def end_time(self) -> Fraction:
        """Return the time at which the event ends."""
        return self.time + self.duration

    @property
    def displayed_staff(self) -> int:
        """Return the staff that displays the event."""
        if isinstance(self.event, Note):
            for flag in self.event.flags:
                if isinstance(flag, CrossStaff):
                    return flag.staff
        return self.staff

    @property
    def is_direction(self) -> bool:
        """Return whether the event is a direction."""
        return isinstance(self.event, DIRECTION_TYPES)

    @property
    def is_grace(self) -> bool:
        """Return whether the event is a grace note."""
        return is_grace(self.event)

    @property
    def is_main_rhythmic(self) -> bool:
        """Return whether the event is a non-grace rhythmic event."""
        return not self.is_direction and not self.is_grace

    def same_line(self, other: Placed) -> bool:
        """Return whether two events lie in the same line of one block."""
        return (
            self.frame.index == other.frame.index
            and self.line_index == other.line_index
        )

    def precedes(self, other: Placed) -> bool:
        """Return whether the event lies strictly before another one.

        Events at the same time are ordered only within one line.
        """
        if self.time != other.time:
            return self.time < other.time
        return self.same_line(other) and self.position < other.position


@dataclass(frozen=True, slots=True)
class Line:
    """A voice line with its placed events."""

    frame: Frame
    line: VoiceLine
    events: tuple[Placed, ...]

    @property
    def total(self) -> Fraction:
        """Return the sum of the durations of the line's events."""
        return sum((placed.duration for placed in self.events), Fraction(0))


@dataclass(frozen=True, slots=True)
class Timeline:
    """The placed events of a document."""

    frames: tuple[Frame, ...]
    lines: tuple[Line, ...]

    def events(self) -> Iterator[Placed]:
        """Yield every placed event in written order."""
        for line in self.lines:
            yield from line.events

    @property
    def start_time(self) -> Fraction:
        """Return the time at which the document starts."""
        first = self.frames[0]
        return first.origin + first.start

    @property
    def end_time(self) -> Fraction:
        """Return the time at which the document ends."""
        last = self.frames[-1]
        return last.origin + last.end


def _note_duration(note: Note, scale: Fraction) -> Fraction:
    if is_grace(note):
        return Fraction(0)
    value = note.duration.value * scale
    if any(isinstance(flag, TremoloRelation) for flag in note.flags):
        return value / 2
    return value


def event_duration(event: LeafEvent, scale: Fraction, frame: Frame) -> Fraction:
    """Return the time that an event takes.

    Args:
        event: The event.
        scale: The product of the ratios of the tuplet groups that hold it.
        frame: The measure context, which gives a whole-measure rest its span.

    Returns:
        The duration in whole notes.
    """
    if isinstance(event, Note):
        return _note_duration(event, scale)
    if isinstance(event, (Rest, Spacer)):
        return event.duration.value * scale
    if isinstance(event, MeasureRest):
        return frame.end - frame.start
    if isinstance(event, TimedReview):
        return event.duration
    return Fraction(0)


class _LineWalker:
    """Places the events of one voice line."""

    def __init__(self, frame: Frame, line_index: int, line: VoiceLine) -> None:
        self.frame = frame
        self.line_index = line_index
        self.line = line
        self.onset = frame.start
        self.placed: list[Placed] = []

    def walk(
        self,
        items: Sequence[Event | TupletItem],
        scale: Fraction,
        first_index: int,
    ) -> int:
        index = first_index
        for item in items:
            if isinstance(item, Tuplet):
                ratio = Fraction(item.normal, item.actual)
                index = self.walk(item.items, scale * ratio, index)
                continue
            duration = event_duration(item, scale, self.frame)
            self.placed.append(
                Placed(
                    item,
                    self.frame,
                    self.line_index,
                    self.line.staff,
                    self.line.voice,
                    index,
                    len(self.placed),
                    self.onset,
                    duration,
                    scale,
                ),
            )
            index += 1
            self.onset += duration
        return index


def _option_value[T](
    block: MeasureBlock,
    kind: type[MeasureOption],
    default: T,
) -> T:
    option = first_option(block, kind)
    if option is None:
        return default
    return getattr(option, _OPTION_FIELDS[kind])


_OPTION_FIELDS: dict[type, str] = {
    KeyOption: "fifths",
    TimeOption: "meter",
    LenOption: "value",
    FromOption: "value",
    ToOption: "value",
}


def build_timeline(document: Document) -> Timeline:
    """Place every event of a document.

    Args:
        document: A parsed document.

    Returns:
        The timeline of the document.
    """
    counts: dict[tuple[int, int], int] = {}
    if isinstance(document, SystemDocument):
        counts = {
            (entry.staff, entry.voice): entry.count
            for entry in document.carry_in.event_counts
        }
    key, meter = document.key, document.time
    origin = Fraction(0)
    frames: list[Frame] = []
    lines: list[Line] = []
    for index, block in enumerate(document_blocks(document)):
        key = _option_value(block, KeyOption, key)
        meter = _option_value(block, TimeOption, meter)
        length = _option_value(block, LenOption, meter.length)
        frame = Frame(
            index,
            block,
            key,
            meter,
            length,
            _option_value(block, FromOption, Fraction(0)),
            _option_value(block, ToOption, length),
            origin,
        )
        frames.append(frame)
        origin += length
        for line_index, line in enumerate(block.voices):
            walker = _LineWalker(frame, line_index, line)
            first = counts.get((line.staff, line.voice), 0) if index == 0 else 0
            walker.walk(line.events, Fraction(1), first + 1)
            lines.append(Line(frame, line, tuple(walker.placed)))
    return Timeline(tuple(frames), tuple(lines))
