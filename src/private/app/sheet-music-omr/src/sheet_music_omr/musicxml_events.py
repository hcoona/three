"""MusicXML events and line spans for the comparison protocol's checks.

The supplementary checks of ``docs/comparison-protocol.md`` read the MusicXML
of both scores directly. This module reads one score into its events,
directions and line spans, with the absolute times, grace positions and
written pitches that the checks key on. An ottava shifts the written octave of
the events of its staff from its start up to, but not including, its stop. A
spanner's ``number`` tells it apart only from spanners that overlap it in
document order, as MusicXML 4.0 defines the number level, so endpoints of one
number pair in document order, a stop before a start on one note, and a stop
may precede its start. Only the endpoints that two spans of one number share
in document order, which MusicXML 4.0 does not allow, pair in time order, the
reading this module used before, and only where each pair in time stays
within them; every other pair keeps its document order. A span that document
order pairs backward in time, or an ottava that it pairs across staves, stops
the run. A wavy line that starts and stops on one note is not a span.
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
    from collections.abc import Callable, Iterable

type PitchKey = tuple[str, Fraction, int]
type Leaf = XmlEvent | XmlDirection
type VoiceKey = tuple[int, int, str, int | None]
"""A voice: part, measure, MusicXML voice and, for a split voice, staff."""

SLUR = "slur"
WAVY_LINE = "wavy-line"
DASHES = "dashes"

_OTTAVA = "MusicXML octave shift"
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


def dashes_text(
    start: XmlDirection,
    directions: tuple[XmlDirection, ...],
) -> str | None:
    """Return the text of the ``words`` direction that a dashes start follows.

    The words may be in the start's own direction, or else in the nearest
    preceding direction of its staff and onset.
    """
    if start.words is not None:
        return start.words
    preceding = [
        direction.words
        for direction in directions
        if direction.place == start.place
        and direction.measure == start.measure
        and direction.onset == start.onset
        and direction.order < start.order
        and direction.words is not None
    ]
    return preceding[-1] if preceding else None


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


@dataclass(frozen=True, slots=True)
class XmlOttava:
    """An octave shift from its start to its stop.

    Attributes:
        place: The part and staff of the shift.
        start: The absolute start of the direction that starts it.
        end: The absolute start of the direction that stops it.
        shift: The octaves that it shifts the written pitch by.
    """

    place: tuple[int, int]
    start: Fraction
    end: Fraction
    shift: int


@dataclass(frozen=True, slots=True, eq=False)
class XmlScore:
    """The events, directions, line spans and ottavas of one MusicXML score.

    Attributes:
        events: Every event, in document order.
        directions: Every direction, in document order.
        spans: Every slur, wavy-line and dashes span.
        ottavas: Every octave shift.
        system_starts: The 0-based ordinals of the measures that start a
            system: the first measure and each new system or page.
        measure_starts: The absolute start of each measure ordinal.
    """

    events: tuple[XmlEvent, ...]
    directions: tuple[XmlDirection, ...]
    spans: tuple[XmlSpan, ...]
    ottavas: tuple[XmlOttava, ...]
    system_starts: frozenset[int]
    measure_starts: tuple[Fraction, ...]


def read_score(source: Path | str) -> XmlScore:
    """Read the events, directions, spans and ottavas of one MusicXML score.

    Args:
        source: A MusicXML file path, or MusicXML text.

    Returns:
        The score's events, directions, spans, ottavas, system starts and
        measure starts.

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
        ottavas = _apply_ottavas(self.events, self.directions)
        return XmlScore(
            tuple(self.events),
            tuple(self.directions),
            _spans(self.events, self.directions),
            ottavas,
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
) -> tuple[XmlOttava, ...]:
    ottavas: list[XmlOttava] = []
    for span in _pair_numbers(list(directions), _ottava_endpoints):
        if span.start.place != span.stop.place:
            message = "a MusicXML octave shift stops on another staff"
            raise ProtocolDefectError(message)
        if span.start_note is None:
            message = "a MusicXML octave shift has no start element"
            raise ProtocolDefectError(message)
        ottavas.append(
            XmlOttava(
                span.start.place,
                span.start.start,
                span.stop.start,
                _ottava_size(span.start_note),
            ),
        )
    for event in events:
        event.shift = next(
            (
                ottava.shift
                for ottava in ottavas
                if ottava.place == event.place
                and ottava.start <= event.start < ottava.end
            ),
            0,
        )
    return tuple(ottavas)


def _ottava_endpoints(leaf: Leaf) -> list[_Endpoint]:
    endpoints: list[_Endpoint] = []
    if not isinstance(leaf, XmlDirection):
        return endpoints
    for shift in leaf.element.findall("direction-type/octave-shift"):
        kind = shift.get("type")
        if kind == "continue":
            continue
        if kind != _STOP:
            _ottava_size(shift)
        role = _STOP if kind == _STOP else _START
        endpoints.append((_OTTAVA, role, shift.get("number", "1"), shift))
    return endpoints


def _ottava_size(shift: ET.Element) -> int:
    size = _OTTAVA_SHIFTS.get((shift.get("type") or "", shift.get("size", "8")))
    if size is None:
        message = "MusicXML octave shift is not supported"
        raise ProtocolDefectError(message)
    return size


type _Endpoint = tuple[str, str, str, ET.Element | None]
type _NumberKey = tuple[int, str, str]
"""A spanner number: part, family and MusicXML ``number``."""
type _Pending = tuple[str, Leaf, ET.Element | None]
"""An endpoint of a number: role, leaf and element."""


class _NumberConflictError(ProtocolDefectError):
    """Two spans of one number overlap in document order."""

    def __init__(self, key: _NumberKey, message: str) -> None:
        super().__init__(message)
        self.key = key


@dataclass(slots=True, eq=False)
class _Run:
    """The endpoints of one number from one empty pending state to the next.

    A run of two endpoints is a span. A longer run nests spans of its number,
    which MusicXML 4.0 does not allow, and has a ``conflict``.
    """

    key: _NumberKey
    family: str
    ends: list[_Pending] = field(default_factory=list)
    waiting: list[_Pending] = field(default_factory=list)
    conflict: _NumberConflictError | None = None


def _spans(
    events: list[XmlEvent],
    directions: list[XmlDirection],
) -> tuple[XmlSpan, ...]:
    return tuple(_pair_numbers([*events, *directions], _endpoints))


def _pair_numbers(
    leaves: list[Leaf],
    endpoints: Callable[[Leaf], list[_Endpoint]],
) -> list[XmlSpan]:
    """Pair numbered endpoints in document order where it can.

    The endpoints of a run that nests spans of one number pair in time
    order, provided that each pair in time keeps to one run; every other
    run is a span, and any other defect stops.
    """
    by_order = sorted(leaves, key=lambda leaf: leaf.order)
    runs = _runs(by_order, endpoints, lambda _: True)
    spans = [_run_span(run) for run in runs if run.conflict is None]
    nested = dict.fromkeys(run.key for run in runs if run.conflict is not None)
    by_time = sorted(leaves, key=_leaf_key)
    for key in nested:
        spans += _retimed(
            [run for run in runs if run.key == key], by_time, endpoints
        )
    spans.sort(key=lambda span: _leaf_key(span.stop))
    return spans


def _retimed(
    runs: list[_Run],
    by_time: list[Leaf],
    endpoints: Callable[[Leaf], list[_Endpoint]],
) -> list[XmlSpan]:
    """Pair the nested runs of one number in time order.

    Each pair in time must keep to one run of the document order, so that
    no pair that the document order decodes on its own is replaced.
    """
    conflict = next(run.conflict for run in runs if run.conflict is not None)
    members = {
        _end_key(end): index
        for index, run in enumerate(runs)
        for end in run.ends
    }
    spans: list[XmlSpan] = []
    try:
        for timed in _runs(by_time, endpoints, {conflict.key}.__contains__):
            if timed.conflict is not None:
                raise conflict
            first, second = (members.get(_end_key(end)) for end in timed.ends)
            if first is None or first != second:
                raise conflict
            if runs[first].conflict is not None:
                spans.append(_run_span(timed))
    except ProtocolDefectError:
        raise conflict from None
    return spans


def _end_key(end: _Pending) -> tuple[str, int, int]:
    role, leaf, element = end
    return role, id(leaf), id(element)


def _run_span(run: _Run) -> XmlSpan:
    (role, leaf, element), (_, other, other_element) = run.ends
    if role == _START:
        return _span(run.family, (leaf, element), (other, other_element))
    return _span(run.family, (other, other_element), (leaf, element))


def _runs(
    leaves: list[Leaf],
    endpoints_of: Callable[[Leaf], list[_Endpoint]],
    include: Callable[[_NumberKey], bool],
) -> list[_Run]:
    """Split the endpoints of each included number into runs, in leaf order."""
    runs: list[_Run] = []
    open_runs: dict[_NumberKey, _Run] = {}

    def waiting(key: _NumberKey) -> str | None:
        run = open_runs.get(key)
        return None if run is None else run.waiting[0][0]

    for leaf in leaves:
        endpoints = [
            endpoint
            for endpoint in _leaf_endpoints(leaf, waiting, endpoints_of)
            if include((leaf.part, endpoint[0], endpoint[2]))
        ]
        single = {
            number
            for family, role, number, _ in endpoints
            if family == WAVY_LINE
            and role == _STOP
            and (leaf.part, family, number) not in open_runs
            and (WAVY_LINE, _START, number) in {e[:3] for e in endpoints}
        }
        for family, role, number, note in endpoints:
            if family == WAVY_LINE and number in single:
                continue
            closed = _add_end(
                open_runs,
                (leaf.part, family, number),
                family,
                (role, leaf, note),
            )
            if closed is not None:
                runs.append(closed)
    for run in open_runs.values():
        if run.conflict is not None:
            raise run.conflict
        role = run.waiting[0][0]
        raise ProtocolDefectError(_unpaired(run.family, role, run.key[2]))
    return runs


def _add_end(
    open_runs: dict[_NumberKey, _Run],
    key: _NumberKey,
    family: str,
    end: _Pending,
) -> _Run | None:
    """Add an endpoint to the open run of its number; return a closed run."""
    run = open_runs.get(key)
    if run is None:
        run = open_runs[key] = _Run(key, family)
    run.ends.append(end)
    role, leaf, _ = end
    other = run.waiting[-1] if run.waiting else None
    if other is not None and (other[0] == role or other[1] is leaf):
        run.conflict = run.conflict or _NumberConflictError(
            key,
            _overlap(family, role, key[2]),
        )
    if other is None or other[0] == role:
        run.waiting.append(end)
        return None
    run.waiting.pop()
    return None if run.waiting else open_runs.pop(key)


def _overlap(family: str, role: str, number: str) -> str:
    if role == _START:
        return f"two {family} spans share number {number}"
    return f"a {family} stop with number {number} has no start"


def _unpaired(family: str, role: str, number: str) -> str:
    if role == _START:
        return f"a {family} with number {number} never stops"
    return f"a {family} stop with number {number} has no start"


def _leaf_endpoints(
    leaf: Leaf,
    waiting: Callable[[_NumberKey], str | None],
    endpoints: Callable[[Leaf], list[_Endpoint]],
) -> list[_Endpoint]:
    # A stop on a leaf closes the span that is open before a start on the leaf
    # opens the next; a start on a leaf first takes a stop that came earlier.
    def first(endpoint: _Endpoint) -> bool:
        role = waiting((leaf.part, endpoint[0], endpoint[2]))
        return endpoint[1] != (_START if role == _STOP else _STOP)

    return sorted(endpoints(leaf), key=first)


def _span(
    family: str,
    start: tuple[Leaf, ET.Element | None],
    stop: tuple[Leaf, ET.Element | None],
) -> XmlSpan:
    if start[0] is stop[0] or _written(stop[0]) < _written(start[0]):
        message = f"a {family} stops before it starts"
        raise ProtocolDefectError(message)
    return XmlSpan(family, start[0], stop[0], start[1], stop[1])


def _written(leaf: Leaf) -> tuple[Fraction, int, int, int]:
    """Order leaves in written time: graces of one onset by position."""
    if isinstance(leaf, XmlDirection):
        return leaf.start, leaf.measure, 1, 0
    if leaf.grace:
        return leaf.start, leaf.measure, 0, leaf.grace_position
    return leaf.start, leaf.measure, 2, 0


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
