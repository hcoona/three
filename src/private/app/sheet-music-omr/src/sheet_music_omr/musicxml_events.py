"""MusicXML events and line spans for the comparison protocol's checks.

The supplementary checks of ``docs/comparison-protocol.md`` read the MusicXML
of both scores directly. This module reads one score into its events,
directions and line spans, with the absolute times, grace positions and
written pitches that the checks key on. It follows the reverse converter's
reading of the same facts: an ottava shifts the written octave of the events
of its staff from its start up to, but not including, its stop; spanner
endpoints pair in time order, a stop before a start on one event; and a wavy
line that starts and stops on one note is not a span.
"""

from __future__ import annotations

from collections import Counter
from dataclasses import dataclass, field
from fractions import Fraction
from pathlib import Path
from typing import TYPE_CHECKING

from sheet_music_omr.musicxml_reverse import _load as _load_musicxml

if TYPE_CHECKING:
    import xml.etree.ElementTree as ET
    from collections.abc import Iterable

type PitchKey = tuple[str, Fraction, int]
type Leaf = XmlEvent | XmlDirection
type VoiceKey = tuple[int, int, str, int | None]
"""A voice: part, measure, MusicXML voice and, for a split voice, staff."""

SLUR = "slur"
WAVY_LINE = "wavy-line"
DASHES = "dashes"

_YES = "yes"
_START = "start"
_STOP = "stop"
_TYPE_DURATIONS: dict[str, Fraction] = {
    "maxima": Fraction(32),
    "long": Fraction(16),
    "breve": Fraction(8),
    "whole": Fraction(4),
    "half": Fraction(2),
    "quarter": Fraction(1),
    "eighth": Fraction(1, 2),
    "16th": Fraction(1, 4),
    "32nd": Fraction(1, 8),
    "64th": Fraction(1, 16),
    "128th": Fraction(1, 32),
    "256th": Fraction(1, 64),
    "512th": Fraction(1, 128),
    "1024th": Fraction(1, 256),
}
_OTTAVA_SHIFTS: dict[tuple[str, str], int] = {
    ("down", "8"): 1,
    ("up", "8"): -1,
    ("down", "15"): 2,
    ("up", "15"): -2,
}


class ProtocolDefectError(RuntimeError):
    """A difference or a score fact that the protocol cannot assign.

    The protocol makes this a stop condition: the run stops, and the
    difference is never counted as agreement.
    """


@dataclass(slots=True, eq=False)
class XmlEvent:
    """One note, rest or chord of a MusicXML score.

    Attributes:
        part: The 0-based part ordinal.
        measure: The 0-based measure ordinal in its part.
        staff: The 0-based staff in its part.
        voice: The MusicXML voice, ``"1"`` when absent.
        onset: The onset in quarter notes from the start of the measure.
        duration: The sounding duration in quarter notes, 0 for a grace note.
        written: The written duration from the note type and dots, else the
            sounding duration.
        grace_position: 0 for a non-grace event; for a grace event its 1-based
            position, in document order, among the grace events of its voice
            at its onset. The voice is the one from ``voice_staves``.
        order: The document order of the event in its score.
        notes: The ``note`` elements: the first and its chord members.
        start: The absolute onset in quarter notes.
        shift: The octave shift of an ottava over the event.
    """

    part: int
    measure: int
    staff: int
    voice: str
    onset: Fraction
    duration: Fraction
    written: Fraction
    grace_position: int
    order: int
    notes: list[ET.Element] = field(default_factory=list)
    start: Fraction = Fraction(0)
    shift: int = 0

    @property
    def place(self) -> tuple[int, int]:
        """The part and staff of the event."""
        return self.part, self.staff

    @property
    def position(self) -> tuple[Fraction, int]:
        """The onset and grace position that the checks key on."""
        return self.onset, self.grace_position

    @property
    def grace(self) -> bool:
        """Whether the event is a grace note or chord."""
        return self.notes[0].find("grace") is not None

    @property
    def rest(self) -> bool:
        """Whether the event is a rest."""
        return self.notes[0].find("rest") is not None

    @property
    def end(self) -> Fraction:
        """The absolute time at which the event ends."""
        return self.start + self.duration

    @property
    def pitches(self) -> tuple[PitchKey, ...]:
        """The sorted written pitches of the members."""
        return tuple(
            sorted(
                pitch
                for pitch in map(self.written_pitch, self.notes)
                if pitch is not None
            ),
        )

    def written_pitch(self, note: ET.Element) -> PitchKey | None:
        """Return the written pitch of one member, if it has a pitch."""
        sounding = musicxml_pitch(note)
        if sounding is None:
            return None
        step, alter, octave = sounding
        return step, alter, octave - self.shift


@dataclass(slots=True, eq=False)
class XmlDirection:
    """One ``direction`` element of a MusicXML score.

    Attributes:
        part: The 0-based part ordinal.
        measure: The 0-based measure ordinal in its part.
        staff: The 0-based staff in its part.
        onset: The onset in quarter notes from the start of the measure,
            including any offset.
        order: The document order of the direction in its score.
        element: The ``direction`` element.
        start: The absolute onset in quarter notes.
    """

    part: int
    measure: int
    staff: int
    onset: Fraction
    order: int
    element: ET.Element
    start: Fraction = Fraction(0)

    @property
    def place(self) -> tuple[int, int]:
        """The part and staff of the direction."""
        return self.part, self.staff

    @property
    def words(self) -> str | None:
        """The joined text of the direction's ``words``, if any."""
        words = self.element.findall("direction-type/words")
        return "".join(w.text or "" for w in words) if words else None


@dataclass(frozen=True, slots=True, eq=False)
class XmlSpan:
    """A slur, wavy line or dashes line from its start to its stop.

    Attributes:
        family: ``slur``, ``wavy-line`` or ``dashes``.
        start: The event or direction that carries the start.
        stop: The event or direction that carries the stop.
        start_note: The ``note`` element of a start on an event.
        stop_note: The ``note`` element of a stop on an event.
    """

    family: str
    start: Leaf
    stop: Leaf
    start_note: ET.Element | None
    stop_note: ET.Element | None


@dataclass(frozen=True, slots=True, eq=False)
class XmlScore:
    """The events, directions and line spans of one MusicXML score.

    Attributes:
        events: Every event, in document order.
        directions: Every direction, in document order.
        spans: Every slur, wavy-line and dashes span.
        system_starts: The 0-based ordinals of the measures that start a
            system: the first measure and each new system or page.
        measure_starts: The absolute start of each measure ordinal.
    """

    events: tuple[XmlEvent, ...]
    directions: tuple[XmlDirection, ...]
    spans: tuple[XmlSpan, ...]
    system_starts: frozenset[int]
    measure_starts: tuple[Fraction, ...]


def read_score(source: Path | str) -> XmlScore:
    """Read the events, directions and line spans of one MusicXML score.

    Args:
        source: A MusicXML file path, or MusicXML text.

    Returns:
        The score's events, directions, spans, system starts and measure
        starts.

    Raises:
        ProtocolDefectError: If the score has a fact the checks cannot read.
    """
    data = source.read_bytes() if isinstance(source, Path) else source
    root = _load_musicxml(data)
    reader = _ScoreReader()
    for part_index, part in enumerate(root.findall("part")):
        reader.read_part(part_index, part)
    return reader.finish()


def voice_staves(events: Iterable[XmlEvent]) -> dict[int, tuple[VoiceKey, int]]:
    """Return the voice of each event and the staff of that voice.

    A voice is the events of one MusicXML voice of a part in a measure. When
    its events on two staves overlap in time, the staves reuse the voice
    number for separate voices, one on each staff. A voice's staff is the
    staff that holds most of its events, the lower number on a tie.

    Args:
        events: The events of one score.

    Returns:
        By ``id`` of each event, its voice and the 0-based staff of that
        voice.
    """
    voices: dict[tuple[int, int, str], list[XmlEvent]] = {}
    for event in events:
        voices.setdefault(
            (event.part, event.measure, event.voice),
            [],
        ).append(event)
    result: dict[int, tuple[VoiceKey, int]] = {}
    for (part, measure, voice), members in voices.items():
        for split_staff, split in _split_voice(members):
            counts = Counter(event.staff for event in split)
            home = min(counts, key=lambda staff: (-counts[staff], staff))
            key = (part, measure, voice, split_staff)
            for event in split:
                result[id(event)] = (key, home)
    return result


def _split_voice(
    events: list[XmlEvent],
) -> list[tuple[int | None, list[XmlEvent]]]:
    """Split a MusicXML voice whose events on different staves overlap.

    Two staves that reuse one voice number hold two voices, not one voice
    that crosses staves.
    """
    if any(
        left.staff != right.staff
        and left.start < right.end
        and right.start < left.end
        for left in events
        for right in events
    ):
        staves = sorted({event.staff for event in events})
        return [
            (staff, [event for event in events if event.staff == staff])
            for staff in staves
        ]
    return [(None, events)]


def musicxml_pitch(note: ET.Element) -> PitchKey | None:
    """Return the sounding pitch of a ``note`` element, if it has one.

    Raises:
        ProtocolDefectError: If the pitch is incomplete or invalid.
    """
    pitch = note.find("pitch")
    if pitch is None:
        return None
    step = pitch.findtext("step")
    octave_text = pitch.findtext("octave")
    if step is None or octave_text is None:
        message = "MusicXML pitch has no step or octave"
        raise ProtocolDefectError(message)
    alter_text = pitch.findtext("alter", "0")
    try:
        return step, Fraction(alter_text), int(octave_text)
    except (ValueError, ZeroDivisionError) as error:
        message = "MusicXML pitch has an invalid alter or octave"
        raise ProtocolDefectError(message) from error


def xml_duration(element: ET.Element, divisions: int) -> Fraction:
    """Return the ``duration`` of an element in quarter notes.

    Raises:
        ProtocolDefectError: If the duration is missing or invalid.
    """
    value = element.findtext("duration")
    if value is None:
        message = f"MusicXML {element.tag} has no duration"
        raise ProtocolDefectError(message)
    try:
        duration = Fraction(int(value), divisions)
    except (ValueError, ZeroDivisionError) as error:
        message = f"MusicXML {element.tag} has an invalid duration"
        raise ProtocolDefectError(message) from error
    if duration < 0:
        message = f"MusicXML {element.tag} has a negative duration"
        raise ProtocolDefectError(message)
    return duration


def positive_int(value: str, name: str) -> int:
    """Return a positive integer field of a MusicXML element.

    Raises:
        ProtocolDefectError: If the value is not a positive integer.
    """
    try:
        result = int(value)
    except ValueError as error:
        message = f"MusicXML {name} is not an integer: {value!r}"
        raise ProtocolDefectError(message) from error
    if result <= 0:
        message = f"MusicXML {name} is not positive: {result}"
        raise ProtocolDefectError(message)
    return result


def _written_duration(note: ET.Element, sounding: Fraction) -> Fraction:
    note_type = note.findtext("type")
    if note_type is None:
        return sounding
    base = _TYPE_DURATIONS.get(note_type.strip())
    if base is None:
        message = f"MusicXML note type is not supported: {note_type!r}"
        raise ProtocolDefectError(message)
    written = base
    dot = base
    for _ in note.findall("dot"):
        dot /= 2
        written += dot
    return written


def _staff(element: ET.Element) -> int:
    return positive_int(element.findtext("staff", "1"), "staff") - 1


@dataclass(slots=True)
class _MeasureState:
    cursor: Fraction = Fraction(0)
    length: Fraction = Fraction(0)
    event: XmlEvent | None = None


@dataclass(slots=True)
class _ScoreReader:
    events: list[XmlEvent] = field(default_factory=list)
    directions: list[XmlDirection] = field(default_factory=list)
    system_starts: set[int] = field(default_factory=lambda: {0})
    lengths: list[Fraction] = field(default_factory=list)
    order: int = 0
    divisions: int = 1

    def read_part(self, part_index: int, part: ET.Element) -> None:
        self.divisions = 1
        for measure_index, measure in enumerate(part.findall("measure")):
            state = _MeasureState()
            for child in measure:
                self._read(child, (part_index, measure_index), state)
            if measure_index < len(self.lengths):
                self.lengths[measure_index] = max(
                    self.lengths[measure_index],
                    state.length,
                )
            else:
                self.lengths.append(state.length)

    def _read(
        self,
        child: ET.Element,
        where: tuple[int, int],
        state: _MeasureState,
    ) -> None:
        tag = child.tag
        if tag == "attributes":
            value = child.findtext("divisions")
            if value is not None:
                self.divisions = positive_int(value, "divisions")
        elif tag == "print":
            if _YES in {child.get("new-system"), child.get("new-page")}:
                self.system_starts.add(where[1])
        elif tag in {"backup", "forward"}:
            state.event = None
            duration = xml_duration(child, self.divisions)
            # As music21 does, a backup stops at the start of the measure.
            state.cursor = max(
                Fraction(0),
                state.cursor + (-duration if tag == "backup" else duration),
            )
            state.length = max(state.length, state.cursor)
        elif tag == "direction":
            self._direction(child, where, state)
        elif tag == "note":
            self._note(child, where, state)

    def _direction(
        self,
        element: ET.Element,
        where: tuple[int, int],
        state: _MeasureState,
    ) -> None:
        offset = element.findtext("offset")
        onset = state.cursor
        if offset is not None:
            try:
                onset += Fraction(int(offset), self.divisions)
            except ValueError as error:
                message = f"MusicXML offset is not an integer: {offset!r}"
                raise ProtocolDefectError(message) from error
        self.directions.append(
            XmlDirection(
                where[0],
                where[1],
                _staff(element),
                onset,
                self._next_order(),
                element,
            ),
        )

    def _note(
        self,
        note: ET.Element,
        where: tuple[int, int],
        state: _MeasureState,
    ) -> None:
        grace = note.find("grace") is not None
        duration = Fraction(0) if grace else xml_duration(note, self.divisions)
        voice = (note.findtext("voice") or "1").strip()
        if note.find("chord") is not None:
            event = state.event
            if event is None or (event.voice, event.staff) != (
                voice,
                _staff(note),
            ):
                message = "MusicXML chord member has no matching first note"
                raise ProtocolDefectError(message)
            event.notes.append(note)
            return
        onset = state.cursor
        event = XmlEvent(
            where[0],
            where[1],
            _staff(note),
            voice,
            onset,
            duration,
            _written_duration(note, duration),
            0,
            self._next_order(),
            [note],
        )
        self.events.append(event)
        state.event = event
        state.cursor += duration
        state.length = max(state.length, state.cursor)

    def _next_order(self) -> int:
        self.order += 1
        return self.order

    def finish(self) -> XmlScore:
        starts: list[Fraction] = []
        total = Fraction(0)
        for length in self.lengths:
            starts.append(total)
            total += length
        for leaf in (*self.events, *self.directions):
            leaf.start = starts[leaf.measure] + leaf.onset
        _number_graces(self.events)
        _apply_ottavas(self.events, self.directions)
        return XmlScore(
            tuple(self.events),
            tuple(self.directions),
            _spans(self.events, self.directions),
            frozenset(self.system_starts),
            tuple(starts),
        )


def _number_graces(events: list[XmlEvent]) -> None:
    # A voice can be split only once every event of its measure has its time.
    voices = voice_staves(events)
    graces: Counter[tuple[VoiceKey, Fraction]] = Counter()
    for event in events:
        if event.grace:
            graces[voices[id(event)][0], event.onset] += 1
            event.grace_position = graces[voices[id(event)][0], event.onset]


def _apply_ottavas(
    events: list[XmlEvent],
    directions: list[XmlDirection],
) -> None:
    spans: list[tuple[tuple[int, int], Fraction, Fraction, int]] = []
    open_shifts: dict[tuple[int, str], tuple[XmlDirection, int]] = {}
    for direction in sorted(directions, key=lambda d: (d.start, d.order)):
        for shift in direction.element.findall("direction-type/octave-shift"):
            kind = shift.get("type")
            key = (direction.part, shift.get("number", "1"))
            if kind == _STOP:
                opened = open_shifts.pop(key, None)
                if opened is None or opened[0].place != direction.place:
                    message = "MusicXML octave shift stop has no start"
                    raise ProtocolDefectError(message)
                spans.append(
                    (
                        direction.place,
                        opened[0].start,
                        direction.start,
                        opened[1],
                    ),
                )
            elif kind != "continue":
                size = _OTTAVA_SHIFTS.get((kind or "", shift.get("size", "8")))
                if size is None or key in open_shifts:
                    message = "MusicXML octave shift is not supported"
                    raise ProtocolDefectError(message)
                open_shifts[key] = (direction, size)
    if open_shifts:
        message = "MusicXML octave shift never stops"
        raise ProtocolDefectError(message)
    for event in events:
        event.shift = next(
            (
                size
                for place, start, end, size in spans
                if place == event.place and start <= event.start < end
            ),
            0,
        )


type _Endpoint = tuple[str, str, str, ET.Element | None]


def _spans(
    events: list[XmlEvent],
    directions: list[XmlDirection],
) -> tuple[XmlSpan, ...]:
    leaves: list[Leaf] = [*events, *directions]
    leaves.sort(key=_leaf_key)
    spans: list[XmlSpan] = []
    open_starts: dict[tuple[int, str, str], tuple[Leaf, ET.Element | None]] = {}
    for leaf in leaves:
        endpoints = sorted(_endpoints(leaf), key=lambda e: e[1] == _START)
        single = {
            number
            for family, role, number, _ in endpoints
            if family == WAVY_LINE
            and role == _STOP
            and (leaf.part, family, number) not in open_starts
            and (WAVY_LINE, _START, number) in {e[:3] for e in endpoints}
        }
        for family, role, number, note in endpoints:
            if family == WAVY_LINE and number in single:
                continue
            key = (leaf.part, family, number)
            if role == _START:
                if key in open_starts:
                    message = f"two {family} spans share number {number}"
                    raise ProtocolDefectError(message)
                open_starts[key] = (leaf, note)
                continue
            opened = open_starts.pop(key, None)
            if opened is None:
                message = f"a {family} stop has no start"
                raise ProtocolDefectError(message)
            spans.append(XmlSpan(family, opened[0], leaf, opened[1], note))
    if open_starts:
        message = "a slur, wavy line or dashes line never stops"
        raise ProtocolDefectError(message)
    return tuple(spans)


def _leaf_key(leaf: Leaf) -> tuple[Fraction, int, int, int]:
    rank = 1 if isinstance(leaf, XmlDirection) else 0 if leaf.grace else 2
    return leaf.start, leaf.measure, rank, leaf.order


def _endpoints(leaf: Leaf) -> list[_Endpoint]:
    endpoints: list[_Endpoint] = []
    if isinstance(leaf, XmlDirection):
        for dashes in leaf.element.findall("direction-type/dashes"):
            role = dashes.get("type")
            if role in {_START, _STOP}:
                number = dashes.get("number", "1")
                endpoints.append((DASHES, role, number, None))
        return endpoints
    for note in leaf.notes:
        marks = [(SLUR, mark) for mark in note.findall("notations/slur")] + [
            (WAVY_LINE, mark)
            for mark in note.findall("notations/ornaments/wavy-line")
        ]
        for family, mark in marks:
            role = mark.get("type")
            if role in {_START, _STOP}:
                endpoints.append(
                    (family, role, mark.get("number", "1"), note),
                )
    return endpoints
