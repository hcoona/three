"""The forward converter from an assembled score to MusicXML 4.0.

``to_musicxml`` turns a valid assembled score into a ``score-partwise``
document with one piano part on two staves, following the "Category
encodings" section of the format. It validates the score first and refuses an
invalid score or one that still holds review markers. It never emits a guess:
content that it cannot convert faithfully, either because MusicXML cannot carry
it as the format means it or by a recorded converter choice, fails with an
unsupported-content diagnostic instead of partial output.

The converter reuses the timeline, relation and pitch analysis of the
validator. MusicXML pitches are sounding pitches, so a note takes the resolved
step, alteration and octave, which include any ottava line, and an
``accidental`` element appears only where the score prints one.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from fractions import Fraction
from math import lcm
from typing import TYPE_CHECKING, Final, NoReturn

from sheet_music_omr.diagnostics import TextScoreError, unsupported
from sheet_music_omr.model import (
    AccidentalKind,
    Arpeggiate,
    ArpeggioRelation,
    Articulation,
    ArticulationKind,
    Barline,
    Beam,
    BeamState,
    ClefChange,
    DashesEnd,
    DashesStart,
    Dynamic,
    EndingKind,
    EndingOption,
    EndpointRole,
    ExtendedTrillRelation,
    Fermata,
    Grace,
    HairpinEnd,
    HairpinKind,
    HairpinStart,
    LeftRepeatOption,
    MeasureRest,
    MetronomeUnit,
    Note,
    Ornament,
    OrnamentKind,
    OttavaEnd,
    OttavaSign,
    OttavaStart,
    PedalAction,
    PedalLine,
    PedalSign,
    PedalSignKind,
    Placement,
    Rehearsal,
    Rest,
    RightOption,
    SingleTremolo,
    SlurRelation,
    Spacer,
    Stem,
    Tempo,
    TimeSymbol,
    TimeSymbolOption,
    TremoloRelation,
    TremoloRole,
    Tuplet,
    Words,
)
from sheet_music_omr.musicxml_tree import Element, serialize
from sheet_music_omr.pitches import resolve_pitches
from sheet_music_omr.relations import END, analyze_relations
from sheet_music_omr.review import review_markers
from sheet_music_omr.semantics import validate_document
from sheet_music_omr.timeline import build_timeline, first_option

if TYPE_CHECKING:
    from collections.abc import Callable, Iterable, Sequence

    from sheet_music_omr.diagnostics import Location
    from sheet_music_omr.model import (
        AssembledScore,
        Clef,
        Event,
        Metronome,
        Pitch,
        TupletItem,
    )
    from sheet_music_omr.pitches import PitchResolver
    from sheet_music_omr.relations import Point, Relation, RelationAnalysis
    from sheet_music_omr.review import ReviewMarker
    from sheet_music_omr.timeline import Frame, Line, Placed, Timeline

DOCTYPE: Final = (
    '<!DOCTYPE score-partwise PUBLIC "-//Recordare//DTD MusicXML 4.0'
    ' Partwise//EN" "http://www.musicxml.org/dtds/partwise.dtd">'
)
PART_ID: Final = "P1"
_MAX_NUMBER: Final = 16
_VOICES_PER_STAFF: Final = 4
_PAGE_HEIGHT: Final = 1697
_PAGE_WIDTH: Final = 1200
_MARGIN: Final = 70
_CREDIT_STEP: Final = 50

_TYPE_NAMES: Final = {
    1: "whole",
    2: "half",
    4: "quarter",
    8: "eighth",
    16: "16th",
    32: "32nd",
    64: "64th",
    128: "128th",
}
_ACCIDENTALS: Final = {
    AccidentalKind.SHARP: "sharp",
    AccidentalKind.FLAT: "flat",
    AccidentalKind.NATURAL: "natural",
    AccidentalKind.DOUBLE_SHARP: "double-sharp",
    AccidentalKind.DOUBLE_FLAT: "flat-flat",
}
_ARTICULATIONS: Final = {
    ArticulationKind.STACCATO: "staccato",
    ArticulationKind.ACCENT: "accent",
    ArticulationKind.TENUTO: "tenuto",
    ArticulationKind.STACCATISSIMO: "staccatissimo",
    ArticulationKind.MARCATO: "strong-accent",
    ArticulationKind.PORTATO: "detached-legato",
}
_PLACEMENTS: Final = {Placement.ABOVE: "above", Placement.BELOW: "below"}
_ORNAMENTS: Final = {
    OrnamentKind.TRILL: "trill-mark",
    OrnamentKind.MORDENT: "mordent",
    OrnamentKind.INVERTED_MORDENT: "inverted-mordent",
    OrnamentKind.TURN: "turn",
    OrnamentKind.INVERTED_TURN: "inverted-turn",
}
_BEAM_VALUES: Final = {
    BeamState.START: "begin",
    BeamState.CONTINUE: "continue",
    BeamState.END: "end",
    BeamState.HOOK_FORWARD: "forward hook",
    BeamState.HOOK_BACKWARD: "backward hook",
}
_BARLINE_STYLES: Final = {
    Barline.FINAL: "light-heavy",
    Barline.DOUBLE: "light-light",
    Barline.REPEAT: "light-heavy",
}
_OTTAVA_SHIFTS: Final = {
    OttavaSign.OTTAVA_ALTA: ("down", "8"),
    OttavaSign.OTTAVA_BASSA: ("up", "8"),
    OttavaSign.QUINDICESIMA_ALTA: ("down", "15"),
    OttavaSign.QUINDICESIMA_BASSA: ("up", "15"),
}
_PEDAL_TYPES: Final = {
    PedalAction.LINE_START: "start",
    PedalAction.LINE_CHANGE: "change",
    PedalAction.LINE_RELEASE: "stop",
}
_LINE_RANK: Final = {
    PedalAction.LINE_START: 0,
    PedalAction.LINE_CHANGE: 1,
    PedalAction.LINE_RELEASE: 2,
}
_BEAT_UNITS: Final = {
    MetronomeUnit.QUARTER: ("quarter", False, Fraction(1)),
    MetronomeUnit.HALF: ("half", False, Fraction(2)),
    MetronomeUnit.EIGHTH: ("eighth", False, Fraction(1, 2)),
    MetronomeUnit.DOTTED_QUARTER: ("quarter", True, Fraction(3, 2)),
    MetronomeUnit.DOTTED_HALF: ("half", True, Fraction(3)),
}
# The MusicXML number family of each numbered relation kind. Two-note
# tremolos carry no number.
_FAMILIES: Final = {
    "tie": "tied",
    "slur": "slur",
    "ornament": "wavy-line",
    "hairpin": "wedge",
    "ottava": "octave-shift",
    "pedal": "pedal",
    "dashes": "dashes",
    "arpeggio": "arpeggiate",
}
_UNNUMBERED: Final = frozenset({"tremolo"})


class ReviewRequiredError(Exception):
    """Raised when an assembled score still needs a person to resolve it.

    Attributes:
        markers: The review markers that the score holds, in written order.
    """

    def __init__(self, markers: tuple[ReviewMarker, ...]) -> None:
        """Create an error for the unresolved review markers of a score."""
        super().__init__(
            f"the score holds {len(markers)} unresolved review marker(s)",
        )
        self.markers = markers


def _fail(rule: str, message: str, location: Location | None) -> NoReturn:
    raise TextScoreError((unsupported(rule, message, location),))


def to_musicxml(score: AssembledScore) -> bytes:
    """Convert a valid assembled score to a MusicXML 4.0 partwise document.

    Args:
        score: A parsed assembled score.

    Returns:
        The UTF-8 document with an XML declaration and the partwise DOCTYPE.
        The same score always gives the same bytes.

    Raises:
        TextScoreError: The score is not valid, or it holds content that
            the converter cannot convert faithfully.
        ReviewRequiredError: The score holds unresolved review markers.
    """
    diagnostics = validate_document(score)
    if diagnostics:
        raise TextScoreError(diagnostics)
    markers = review_markers(score)
    if markers:
        raise ReviewRequiredError(markers)
    timeline = build_timeline(score)
    relations = analyze_relations(score, timeline)
    pitches = resolve_pitches(score, timeline, relations)
    return _Converter(score, timeline, relations, pitches).convert()


def to_musicxml_text(score: AssembledScore) -> str:
    """Return ``to_musicxml`` decoded as text."""
    return to_musicxml(score).decode("utf-8")


# Tuplet groups.


@dataclass(frozen=True, slots=True)
class _Group:
    """One tuplet group with its nesting depth from 1."""

    actual: int
    normal: int
    depth: int


@dataclass(slots=True)
class _LeafTuplets:
    """The tuplet context of one leaf event of a voice line."""

    groups: tuple[_Group, ...] = ()
    starts: list[_Group] = field(default_factory=list)
    stops: list[_Group] = field(default_factory=list)


def _is_rhythmic(item: Event | TupletItem) -> bool:
    if isinstance(item, Note):
        return not any(isinstance(flag, Grace) for flag in item.flags)
    return isinstance(item, (Rest, Spacer))


def _tuplet_leaves(events: Sequence[Event]) -> list[_LeafTuplets]:
    """Return the tuplet context of each leaf, in timeline order."""
    leaves: list[_LeafTuplets] = []
    spans: list[tuple[_Group, list[int]]] = []

    def walk(
        items: Sequence[Event | TupletItem],
        path: tuple[tuple[_Group, list[int]], ...],
    ) -> None:
        for item in items:
            if isinstance(item, Tuplet):
                depth = len(path) + 1
                if depth > _MAX_NUMBER:
                    msg = (
                        f"tuplets nested more than {_MAX_NUMBER} deep, which"
                        " MusicXML numbers cannot tell apart"
                    )
                    _fail("spanner_numbers", msg, item.location)
                span = (_Group(item.actual, item.normal, depth), [])
                spans.append(span)
                walk(item.items, (*path, span))
                continue
            if _is_rhythmic(item):
                for _, members in path:
                    members.append(len(leaves))
            leaves.append(_LeafTuplets(tuple(group for group, _ in path)))

    walk(events, ())
    for group, members in spans:
        if members:
            leaves[members[0]].starts.append(group)
            leaves[members[-1]].stops.append(group)
    for leaf in leaves:
        leaf.starts.sort(key=lambda group: group.depth)
        leaf.stops.sort(key=lambda group: -group.depth)
    return leaves


@dataclass(frozen=True, slots=True)
class _NoteContext:
    """What every chord member of one note event shares."""

    grace: Grace | None
    modification: Element | None
    stem: Stem | None
    beams: list[Beam]
    shared: list[Element]
    arpeggio: Element | None


# Spanner numbers.

# An arpeggio at one onset: its first document-order index, its relation
# label or the ident of its arp event, and its location.
type _ArpeggioItem = tuple[int, int | tuple[int, int, int], Location | None]


def _allocate(
    intervals: Iterable[tuple[int, int, int, Location | None]],
    family: str,
) -> dict[int, int]:
    """Give each interval the smallest number free over its whole span."""
    numbers: dict[int, int] = {}
    active: list[tuple[int, int]] = []
    for first, last, label, location in sorted(intervals):
        active = [(end, number) for end, number in active if end >= first]
        used = {number for _, number in active}
        number = next(n for n in range(1, _MAX_NUMBER + 2) if n not in used)
        if number > _MAX_NUMBER:
            msg = (
                f"more than {_MAX_NUMBER} {family} spanners overlap, which"
                " MusicXML numbers cannot tell apart"
            )
            _fail("spanner_numbers", msg, location)
        numbers[label] = number
        active.append((last, number))
    return numbers


# The converter.


def _quarter_tempo(metronome: Metronome) -> str:
    value = metronome.per_minute * _BEAT_UNITS[metronome.unit][2]
    if value.denominator == 1:
        return str(value.numerator)
    return str(float(value))


def _clef_element(clef: Clef, staff: int) -> Element:
    element = Element("clef", {"number": str(staff)})
    element.add("sign", clef.sign.value)
    element.add("line", str(clef.line))
    if clef.octave_change:
        element.add("clef-octave-change", str(clef.octave_change))
    return element


def _flags[T](event: Note, kind: type[T]) -> list[T]:
    return [flag for flag in event.flags if isinstance(flag, kind)]


def _pedal_rank(placed: Placed) -> int:
    event = placed.event
    return (
        0
        if isinstance(event, PedalSign) and event.kind is PedalSignKind.UP
        else 1
    )


def _line_rank(placed: Placed) -> int:
    event = placed.event
    assert isinstance(event, PedalLine)  # noqa: S101
    return _LINE_RANK[event.action]


def _pedal_order(events: Sequence[Placed]) -> list[Placed]:
    """Order pedal events at one onset.

    A pedal-up sign goes before a pedal-down sign, and one line's start,
    changes and release go in that order. Other events keep their slots.
    """
    ordered = list(events)
    start = 0
    while start < len(ordered):
        end = start
        while end < len(ordered) and ordered[end].onset == ordered[start].onset:
            end += 1
        slots = [
            i
            for i in range(start, end)
            if isinstance(ordered[i].event, PedalSign)
        ]
        signs = sorted((ordered[i] for i in slots), key=_pedal_rank)
        for slot, placed in zip(slots, signs, strict=True):
            ordered[slot] = placed
        lines: dict[int, list[int]] = {}
        for i in range(start, end):
            event = ordered[i].event
            if isinstance(event, PedalLine):
                lines.setdefault(event.label, []).append(i)
        for indices in lines.values():
            actions = sorted((ordered[i] for i in indices), key=_line_rank)
            for slot, placed in zip(indices, actions, strict=True):
                ordered[slot] = placed
        start = max(end, start + 1)
    return ordered


class _Converter:
    """Builds the MusicXML tree of one valid assembled score."""

    def __init__(
        self,
        score: AssembledScore,
        timeline: Timeline,
        relations: RelationAnalysis,
        pitches: PitchResolver,
    ) -> None:
        self.score = score
        self.timeline = timeline
        self.relations = relations
        self.pitches = pitches
        self.lines: dict[int, list[Line]] = {}
        for line in timeline.lines:
            self.lines.setdefault(line.frame.index, []).append(line)
        for lines in self.lines.values():
            lines.sort(key=lambda line: (line.line.staff, line.line.voice))
        self.order: dict[tuple[int, int, int], int] = {}
        self.leaves: dict[tuple[int, int, int], _LeafTuplets] = {}
        for frame in timeline.frames:
            for line in self.lines.get(frame.index, []):
                leaves = _tuplet_leaves(line.line.events)
                for placed in line.events:
                    self.leaves[placed.ident] = leaves[placed.position]
                for placed in _pedal_order(line.events):
                    self.order[placed.ident] = len(self.order)
        self.divisions = self._divisions()
        self.arp_numbers: dict[tuple[int, int, int], int] = {}
        self.numbers = self._numbers()
        self.ending: int | None = None

    def _divisions(self) -> int:
        denominators = {
            (frame.length * 4).denominator for frame in self.timeline.frames
        }
        denominators.update(
            (placed.duration * 4).denominator
            for placed in self.timeline.events()
        )
        return lcm(*denominators)

    def _numbers(self) -> dict[int, int]:
        families: dict[str, list[tuple[int, int, int, Location | None]]] = {}
        arpeggios: dict[tuple[int, Fraction], list[_ArpeggioItem]] = {}
        ties: list[tuple[int, Relation]] = []
        for label, relation in sorted(self.relations.relations.items()):
            kind = relation.kind
            if kind in _UNNUMBERED:
                continue
            family = _FAMILIES.get(kind)
            if family is None:
                msg = f"an assembled score cannot carry a {kind} relation"
                _fail("relation_kind", msg, relation.location)
            indices = [
                self.order[point.placed.ident] for point in relation.points
            ]
            if kind == "tie":
                ties.append((label, relation))
                continue
            if kind == "arpeggio":
                placed = relation.points[0].placed
                arpeggios.setdefault((placed.frame.index, placed.onset), [])
                arpeggios[placed.frame.index, placed.onset].append(
                    (min(indices), label, relation.location),
                )
                continue
            families.setdefault(family, []).append(
                (min(indices), max(indices), label, relation.location),
            )
        families["tied"] = self._tie_intervals(ties)
        self._refuse_pedal_signs(families.get("pedal", []))
        numbers: dict[int, int] = {}
        for family, intervals in sorted(families.items()):
            numbers.update(_allocate(intervals, family))
        self._number_arpeggios(arpeggios, numbers)
        return numbers

    def _tie_intervals(
        self,
        ties: Sequence[tuple[int, Relation]],
    ) -> list[tuple[int, int, int, Location | None]]:
        """Span each tie in written pitch order, with a stop before a start."""

        def key(point: Point) -> tuple[int, int, int]:
            phase = 0 if point.role == END else 1
            return (self.order[point.placed.ident], point.member or 0, phase)

        keys = sorted({key(point) for _, tie in ties for point in tie.points})
        rank = {written: index for index, written in enumerate(keys)}
        intervals: list[tuple[int, int, int, Location | None]] = []
        for label, tie in ties:
            ranks = [rank[key(point)] for point in tie.points]
            intervals.append((min(ranks), max(ranks), label, tie.location))
        return intervals

    def _number_arpeggios(
        self,
        arpeggios: dict[tuple[int, Fraction], list[_ArpeggioItem]],
        numbers: dict[int, int],
    ) -> None:
        """Number the arpeggios at each onset of a measure from 1."""
        for placed in self.timeline.events():
            event = placed.event
            if isinstance(event, Note) and _flags(event, Arpeggiate):
                arpeggios.setdefault((placed.frame.index, placed.onset), [])
                arpeggios[placed.frame.index, placed.onset].append(
                    (self.order[placed.ident], placed.ident, event.location),
                )
        for _, items in sorted(arpeggios.items()):
            items.sort(key=lambda item: item[0])
            if len(items) > _MAX_NUMBER:
                msg = (
                    f"more than {_MAX_NUMBER} arpeggiate spanners overlap,"
                    " which MusicXML numbers cannot tell apart"
                )
                _fail("spanner_numbers", msg, items[_MAX_NUMBER][2])
            for number, (_, key, _) in enumerate(items, 1):
                if isinstance(key, tuple):
                    self.arp_numbers[key] = number
                else:
                    numbers[key] = number

    def _refuse_pedal_signs(
        self,
        lines: Sequence[tuple[int, int, int, Location | None]],
    ) -> None:
        if not lines:
            return
        for placed in self.timeline.events():
            if isinstance(placed.event, PedalSign):
                msg = (
                    "a score with both pedal signs and pedal lines is not"
                    " converted, since their MusicXML pedal numbers could"
                    " collide"
                )
                _fail("pedal", msg, placed.event.location)

    def ticks(self, duration: Fraction) -> str:
        """Return a duration in divisions."""
        value = duration * 4 * self.divisions
        return str(value.numerator)

    def convert(self) -> bytes:
        """Return the serialized document."""
        root = Element("score-partwise", {"version": "4.0"})
        self._header(root)
        part = root.add("part", attributes={"id": PART_ID})
        previous: Frame | None = None
        for frame in self.timeline.frames:
            part.append(self._measure(frame, previous))
            previous = frame
        return serialize(root, DOCTYPE)

    # Header.

    def _header(self, root: Element) -> None:
        score = self.score
        if score.title is not None:
            root.add("work").add("work-title", score.title)
        identification = root.add("identification")
        if score.composer is not None:
            identification.add("creator", score.composer, {"type": "composer"})
        identification.add("encoding").add("software", "sheet-music-omr")
        defaults = root.add("defaults")
        scaling = defaults.add("scaling")
        scaling.add("millimeters", "7")
        scaling.add("tenths", "40")
        layout = defaults.add("page-layout")
        layout.add("page-height", str(_PAGE_HEIGHT))
        layout.add("page-width", str(_PAGE_WIDTH))
        margins = layout.add("page-margins", attributes={"type": "both"})
        for side in ("left", "right", "top", "bottom"):
            margins.add(f"{side}-margin", str(_MARGIN))
        self._credits(root)
        score_part = root.add("part-list").add(
            "score-part",
            attributes={"id": PART_ID},
        )
        score_part.add("part-name", "Piano")
        instrument = score_part.add(
            "score-instrument",
            attributes={"id": f"{PART_ID}-I1"},
        )
        instrument.add("instrument-name", "Piano")
        instrument.add("instrument-sound", "keyboard.piano")

    def _credits(self, root: Element) -> None:
        headings = [
            heading.text
            for headed in self.score.blocks
            for heading in headed.headings
        ]
        for index, text in enumerate(headings):
            credit = root.add("credit", attributes={"page": "1"})
            credit.add("credit-type", "title" if index == 0 else "subtitle")
            y = _PAGE_HEIGHT - _MARGIN - index * _CREDIT_STEP
            credit.add(
                "credit-words",
                text,
                {
                    "default-x": str(_PAGE_WIDTH // 2),
                    "default-y": str(y),
                    "justify": "center",
                    "valign": "top",
                },
            )

    # Measures.

    def _measure(self, frame: Frame, previous: Frame | None) -> Element:
        measure = Element("measure", {"number": str(frame.measure)})
        if frame.index == 0 and frame.length < frame.meter.length:
            measure.attributes["implicit"] = "yes"
        if frame.index > 0 and self.score.blocks[frame.index].headings:
            measure.add("print", attributes={"new-system": "yes"})
        left = self._left_barline(frame)
        if left is not None:
            measure.append(left)
        attributes = self._attributes(frame, previous)
        if attributes.children:
            measure.append(attributes)
        for position, line in enumerate(self.lines.get(frame.index, [])):
            if position > 0:
                measure.add("backup").add("duration", self.ticks(frame.length))
            for placed in _pedal_order(line.events):
                measure.children.extend(self._event(placed))
        right = self._right_barline(frame)
        if right is not None:
            measure.append(right)
        return measure

    def _attributes(self, frame: Frame, previous: Frame | None) -> Element:
        attributes = Element("attributes")
        if previous is None:
            attributes.add("divisions", str(self.divisions))
        if previous is None or frame.key != previous.key:
            attributes.add("key").add("fifths", str(frame.key))
        symbol_option = first_option(frame.block, TimeSymbolOption)
        symbol = (
            TimeSymbol.NUMERIC
            if symbol_option is None
            else symbol_option.symbol
        )
        if (
            previous is None
            or frame.meter != previous.meter
            or symbol is not TimeSymbol.NUMERIC
        ):
            time = attributes.add("time")
            if symbol is not TimeSymbol.NUMERIC:
                time.attributes["symbol"] = symbol.value
            time.add("beats", str(frame.meter.numerator))
            time.add("beat-type", str(frame.meter.denominator))
        if previous is None:
            attributes.add("staves", "2")
            for staff, clef in enumerate(self.score.clefs, 1):
                attributes.append(_clef_element(clef, staff))
        return attributes

    def _ending(self, option: EndingOption) -> int:
        if option.number is not None:
            return option.number
        if self.ending is None:
            msg = "the volta bracket continues no open bracket"
            _fail("ending", msg, option.location)
        return self.ending

    def _left_barline(self, frame: Frame) -> Element | None:
        repeat = first_option(frame.block, LeftRepeatOption)
        ending = first_option(frame.block, EndingOption)
        opens = ending is not None and ending.kind in {
            EndingKind.START,
            EndingKind.SINGLE,
        }
        if repeat is None and not opens:
            return None
        barline = Element("barline", {"location": "left"})
        if repeat is not None:
            barline.add("bar-style", "heavy-light")
        if ending is not None and opens:
            number = str(self._ending(ending))
            barline.add(
                "ending",
                f"{number}.",
                {"number": number, "type": "start"},
            )
            self.ending = ending.number
        if repeat is not None:
            barline.add("repeat", attributes={"direction": "forward"})
        return barline

    def _right_barline(self, frame: Frame) -> Element | None:
        right = first_option(frame.block, RightOption)
        ending = first_option(frame.block, EndingOption)
        closes = ending is not None and ending.kind in {
            EndingKind.SINGLE,
            EndingKind.END,
        }
        if right is None and not closes:
            return None
        barline = Element("barline", {"location": "right"})
        if right is not None:
            barline.add("bar-style", _BARLINE_STYLES[right.barline])
        if ending is not None and closes:
            number = str(self._ending(ending))
            barline.add("ending", attributes={"number": number, "type": "stop"})
            self.ending = None
        if right is not None and right.barline is Barline.REPEAT:
            barline.add("repeat", attributes={"direction": "backward"})
        return barline

    # Events.

    def _event(self, placed: Placed) -> list[Element]:
        event = placed.event
        if isinstance(event, Note):
            return self._note(placed, event)
        if isinstance(event, Rest):
            return [self._rest(placed, event)]
        if isinstance(event, Spacer):
            return [self._spacer(placed, event)]
        if isinstance(event, MeasureRest):
            return [self._measure_rest(placed, event)]
        builder = _DIRECTIONS.get(type(event))
        if builder is None:
            msg = f"the converter cannot emit a {type(event).__name__} event"
            _fail("event", msg, event.location)
        return [builder(self, placed)]

    def voice(self, placed: Placed) -> str:
        """Return the MusicXML voice number of an event's line."""
        return str((placed.staff - 1) * _VOICES_PER_STAFF + placed.voice)

    def _leaf(self, placed: Placed) -> _LeafTuplets:
        return self.leaves[placed.ident]

    def _time_modification(
        self,
        leaf: _LeafTuplets,
        *,
        tremolo: bool = False,
    ) -> Element | None:
        actual = normal = 1
        for group in leaf.groups:
            actual *= group.actual
            normal *= group.normal
        if tremolo:
            actual *= 2
        if actual == normal == 1:
            return None
        element = Element("time-modification")
        element.add("actual-notes", str(actual))
        element.add("normal-notes", str(normal))
        return element

    def _rhythm(
        self,
        note: Element,
        placed: Placed,
        base: int,
        dots: int,
    ) -> None:
        note.add("voice", self.voice(placed))
        note.add("type", _TYPE_NAMES[base])
        for _ in range(dots):
            note.add("dot")

    def _note(self, placed: Placed, event: Note) -> list[Element]:
        leaf = self._leaf(placed)
        grace = _flags(event, Grace)
        tremolo = bool(_flags(event, TremoloRelation))
        context = _NoteContext(
            grace=grace[0] if grace else None,
            modification=(
                None
                if grace
                else self._time_modification(leaf, tremolo=tremolo)
            ),
            stem=next(iter(_flags(event, Stem)), None),
            beams=sorted(_flags(event, Beam), key=lambda beam: beam.level),
            shared=self._shared_notations(event, leaf),
            arpeggio=self._arpeggio(placed, event),
        )
        return [
            self._member(placed, event, member, pitch, context)
            for member, pitch in enumerate(event.pitches, 1)
        ]

    def _member(
        self,
        placed: Placed,
        event: Note,
        member: int,
        pitch: Pitch,
        context: _NoteContext,
    ) -> Element:
        note = self._member_start(placed, member, pitch, context)
        self._rhythm(note, placed, event.duration.base, event.duration.dots)
        if pitch.accidental is not None:
            parentheses = pitch.accidental.parenthesized
            note.add(
                "accidental",
                _ACCIDENTALS[pitch.accidental.kind],
                {"parentheses": "yes"} if parentheses else {},
            )
        if context.modification is not None:
            note.append(context.modification)
        if context.stem is not None:
            note.add("stem", context.stem.direction.value)
        if pitch.parenthesized:
            note.add("notehead", "normal", {"parentheses": "yes"})
        note.add("staff", str(placed.displayed_staff))
        notations = self._pitch_notations(pitch)
        if member == 1:
            _add_beams(note, context.beams)
            notations.extend(context.shared)
        if context.arpeggio is not None:
            notations.append(context.arpeggio)
        if notations:
            note.add("notations").children.extend(_merge_notations(notations))
        return note

    def _member_start(
        self,
        placed: Placed,
        member: int,
        pitch: Pitch,
        context: _NoteContext,
    ) -> Element:
        note = Element("note")
        if context.grace is not None:
            slash = {"slash": "yes"} if context.grace.slashed else {}
            note.add("grace", attributes=slash)
        if member > 1:
            note.add("chord")
        note.append(self._pitch(placed, member, pitch))
        if context.grace is None:
            note.add("duration", self.ticks(placed.duration))
        for kind in _tie_types(pitch):
            note.add("tie", attributes={"type": kind})
        return note

    def _pitch(self, placed: Placed, member: int, pitch: Pitch) -> Element:
        sounding = self.pitches.resolved.get((placed.ident, member))
        if sounding is None or not isinstance(sounding.alter, int):
            msg = "the pitch has no resolved sounding alteration"
            _fail("deferred_alteration", msg, pitch.location)
        element = Element("pitch")
        element.add("step", sounding.step)
        if sounding.alter:
            element.add("alter", str(sounding.alter))
        element.add("octave", str(sounding.octave))
        return element

    def _arpeggio(self, placed: Placed, event: Note) -> Element | None:
        related = _flags(event, ArpeggioRelation)
        rolled = _flags(event, Arpeggiate)
        if len(related) + len(rolled) > 1:
            msg = (
                "an event in more than one arpeggio, from an arp flag or"
                " arpeggio relations, is not converted"
            )
            _fail("arpeggio", msg, placed.event.location)
        if related:
            number = self.numbers[related[0].label]
            return Element("arpeggiate", {"number": str(number)})
        if rolled:
            number = self.arp_numbers[placed.ident]
            return Element("arpeggiate", {"number": str(number)})
        return None

    def _pitch_notations(self, pitch: Pitch) -> list[Element]:
        notations: list[Element] = []
        if pitch.tie_in:
            notations.append(Element("tied", {"type": "stop"}))
        for label, kind in (
            (pitch.tie_end, "stop"),
            (pitch.tie_start, "start"),
        ):
            if label is not None:
                number = str(self.numbers[label])
                notations.append(
                    Element("tied", {"type": kind, "number": number}),
                )
        if pitch.fingering:
            technical = Element("technical")
            for digit in pitch.fingering:
                technical.add("fingering", str(digit))
            notations.append(technical)
        return notations

    def _shared_notations(
        self, event: Note, leaf: _LeafTuplets
    ) -> list[Element]:
        notations: list[Element] = []
        for slur in _flags(event, SlurRelation):
            kind = "start" if slur.role is EndpointRole.START else "stop"
            number = str(self.numbers[slur.label])
            notations.append(Element("slur", {"type": kind, "number": number}))
        notations.extend(_tuplets(leaf))
        ornaments = self._ornaments(event)
        if ornaments.children:
            notations.append(ornaments)
        articulations = Element("articulations")
        for articulation in _flags(event, Articulation):
            attributes = {}
            if articulation.placement is not None:
                attributes["placement"] = _PLACEMENTS[articulation.placement]
            articulations.add(
                _ARTICULATIONS[articulation.kind], attributes=attributes
            )
        if articulations.children:
            notations.append(articulations)
        notations.extend(_fermatas(_flags(event, Fermata)))
        return notations

    def _ornaments(self, event: Note) -> Element:
        ornaments = Element("ornaments")
        for ornament in _flags(event, Ornament):
            ornaments.add(_ORNAMENTS[ornament.kind])
        for trill in _flags(event, ExtendedTrillRelation):
            kind = "start" if trill.role is EndpointRole.START else "stop"
            number = str(self.numbers[trill.label])
            ornaments.add(
                "wavy-line", attributes={"type": kind, "number": number}
            )
        for single in _flags(event, SingleTremolo):
            ornaments.add("tremolo", str(single.strokes), {"type": "single"})
        for pair in _flags(event, TremoloRelation):
            kind = "start" if pair.role is TremoloRole.TWO_START else "stop"
            ornaments.add("tremolo", str(pair.strokes), {"type": kind})
        return ornaments

    def _rest(self, placed: Placed, event: Rest) -> Element:
        note = Element("note")
        note.add("rest")
        note.add("duration", self.ticks(placed.duration))
        self._rest_body(note, placed, event.duration.base, event.duration.dots)
        _add_beams(
            note,
            sorted(
                (flag for flag in event.flags if isinstance(flag, Beam)),
                key=lambda beam: beam.level,
            ),
        )
        leaf = self._leaf(placed)
        notations = _tuplets(leaf)
        notations.extend(
            _fermatas(
                [flag for flag in event.flags if isinstance(flag, Fermata)]
            ),
        )
        if notations:
            note.add("notations").children.extend(notations)
        return note

    def _rest_body(
        self, note: Element, placed: Placed, base: int, dots: int
    ) -> None:
        self._rhythm(note, placed, base, dots)
        modification = self._time_modification(self._leaf(placed))
        if modification is not None:
            note.append(modification)
        note.add("staff", str(placed.staff))

    def _spacer(self, placed: Placed, event: Spacer) -> Element:
        leaf = self._leaf(placed)
        if not event.beams and not leaf.groups:
            forward = Element("forward")
            forward.add("duration", self.ticks(placed.duration))
            forward.add("voice", self.voice(placed))
            forward.add("staff", str(placed.staff))
            return forward
        note = Element("note", {"print-object": "no"})
        note.add("rest")
        note.add("duration", self.ticks(placed.duration))
        self._rest_body(note, placed, event.duration.base, event.duration.dots)
        _add_beams(note, sorted(event.beams, key=lambda beam: beam.level))
        notations = _tuplets(leaf)
        if notations:
            note.add("notations").children.extend(notations)
        return note

    def _measure_rest(self, placed: Placed, event: MeasureRest) -> Element:
        note = Element("note")
        note.add("rest", attributes={"measure": "yes"})
        note.add("duration", self.ticks(placed.duration))
        note.add("voice", self.voice(placed))
        note.add("staff", str(placed.staff))
        if event.fermata is not None:
            note.add("notations").children.extend(_fermatas([event.fermata]))
        return note

    # Directions.

    def direction(
        self,
        placed: Placed,
        types: Sequence[Element],
        *,
        placement: str | None = None,
        sound: Element | None = None,
    ) -> Element:
        """Return a ``direction`` with its types, voice, staff and sound."""
        attributes = {} if placement is None else {"placement": placement}
        element = Element("direction", attributes)
        for kind in types:
            element.add("direction-type").append(kind)
        element.add("voice", self.voice(placed))
        element.add("staff", str(placed.staff))
        if sound is not None:
            element.append(sound)
        return element

    def number(self, label: int) -> str:
        """Return the MusicXML number of a relation label."""
        return str(self.numbers[label])


def _tie_types(pitch: Pitch) -> list[str]:
    """Return the tie types of a pitch, the stop before the start."""
    types: list[str] = []
    if pitch.tie_end is not None or pitch.tie_in:
        types.append("stop")
    if pitch.tie_start is not None:
        types.append("start")
    return types


def _add_beams(note: Element, beams: Iterable[Beam]) -> None:
    for beam in beams:
        note.add("beam", _BEAM_VALUES[beam.state], {"number": str(beam.level)})


def _tuplets(leaf: _LeafTuplets) -> list[Element]:
    elements: list[Element] = []
    for group in leaf.starts:
        tuplet = Element(
            "tuplet", {"type": "start", "number": str(group.depth)}
        )
        tuplet.add("tuplet-actual").add("tuplet-number", str(group.actual))
        tuplet.add("tuplet-normal").add("tuplet-number", str(group.normal))
        elements.append(tuplet)
    elements.extend(
        Element("tuplet", {"type": "stop", "number": str(group.depth)})
        for group in leaf.stops
    )
    return elements


def _fermatas(fermatas: Iterable[Fermata]) -> list[Element]:
    return [
        Element(
            "fermata",
            {"type": "inverted" if fermata.inverted else "upright"},
        )
        for fermata in fermatas
    ]


_NOTATION_ORDER: Final = {
    "tied": 0,
    "slur": 1,
    "tuplet": 2,
    "ornaments": 3,
    "technical": 4,
    "articulations": 5,
    "fermata": 6,
    "arpeggiate": 7,
}


def _merge_notations(elements: list[Element]) -> list[Element]:
    """Order the notations of one note by kind, keeping order within a kind."""
    return sorted(elements, key=lambda element: _NOTATION_ORDER[element.tag])


def _dynamic(converter: _Converter, placed: Placed) -> Element:
    event = placed.event
    assert isinstance(event, Dynamic)  # noqa: S101
    dynamics = Element("dynamics")
    dynamics.add(event.mark.value)
    placement = "above" if event.above else "below"
    return converter.direction(placed, [dynamics], placement=placement)


def _clef_change(_converter: _Converter, placed: Placed) -> Element:
    event = placed.event
    assert isinstance(event, ClefChange)  # noqa: S101
    attributes = Element("attributes")
    attributes.append(_clef_element(event.clef, placed.staff))
    return attributes


def _words(converter: _Converter, placed: Placed) -> Element:
    event = placed.event
    assert isinstance(event, Words)  # noqa: S101
    return converter.direction(placed, [Element("words", text=event.text)])


def _tempo(converter: _Converter, placed: Placed) -> Element:
    event = placed.event
    assert isinstance(event, Tempo)  # noqa: S101
    types: list[Element] = []
    if event.text:
        types.append(Element("words", {"font-weight": "bold"}, event.text))
    sound = None
    if event.metronome is not None:
        unit, dotted, _ = _BEAT_UNITS[event.metronome.unit]
        metronome = Element("metronome")
        metronome.add("beat-unit", unit)
        if dotted:
            metronome.add("beat-unit-dot")
        metronome.add("per-minute", str(event.metronome.per_minute))
        types.append(metronome)
        sound = Element("sound", {"tempo": _quarter_tempo(event.metronome)})
    return converter.direction(placed, types, sound=sound)


def _rehearsal(converter: _Converter, placed: Placed) -> Element:
    event = placed.event
    assert isinstance(event, Rehearsal)  # noqa: S101
    return converter.direction(placed, [Element("rehearsal", text=event.text)])


def _pedal_sign(converter: _Converter, placed: Placed) -> Element:
    event = placed.event
    assert isinstance(event, PedalSign)  # noqa: S101
    kind = "start" if event.kind is PedalSignKind.DOWN else "stop"
    pedal = Element("pedal", {"type": kind, "line": "no", "sign": "yes"})
    return converter.direction(placed, [pedal])


def _pedal_line(converter: _Converter, placed: Placed) -> Element:
    event = placed.event
    assert isinstance(event, PedalLine)  # noqa: S101
    pedal = Element(
        "pedal",
        {
            "type": _PEDAL_TYPES[event.action],
            "number": converter.number(event.label),
            "line": "yes",
        },
    )
    return converter.direction(placed, [pedal])


def _hairpin_start(converter: _Converter, placed: Placed) -> Element:
    event = placed.event
    assert isinstance(event, HairpinStart)  # noqa: S101
    kind = "crescendo" if event.kind is HairpinKind.CRESC else "diminuendo"
    wedge = Element(
        "wedge", {"type": kind, "number": converter.number(event.label)}
    )
    return converter.direction(placed, [wedge])


def _hairpin_end(converter: _Converter, placed: Placed) -> Element:
    event = placed.event
    assert isinstance(event, HairpinEnd)  # noqa: S101
    wedge = Element(
        "wedge", {"type": "stop", "number": converter.number(event.label)}
    )
    return converter.direction(placed, [wedge])


def _ottava(converter: _Converter, placed: Placed) -> Element:
    event = placed.event
    assert isinstance(event, (OttavaStart, OttavaEnd))  # noqa: S101
    kind, size = _OTTAVA_SHIFTS[event.sign]
    if isinstance(event, OttavaEnd):
        kind = "stop"
    shift = Element(
        "octave-shift",
        {"type": kind, "number": converter.number(event.label), "size": size},
    )
    return converter.direction(placed, [shift])


def _dashes_start(converter: _Converter, placed: Placed) -> Element:
    event = placed.event
    assert isinstance(event, DashesStart)  # noqa: S101
    dashes = Element(
        "dashes",
        {"type": "start", "number": converter.number(event.label)},
    )
    return converter.direction(
        placed, [Element("words", text=event.text), dashes]
    )


def _dashes_end(converter: _Converter, placed: Placed) -> Element:
    event = placed.event
    assert isinstance(event, DashesEnd)  # noqa: S101
    dashes = Element(
        "dashes",
        {"type": "stop", "number": converter.number(event.label)},
    )
    return converter.direction(placed, [dashes])


_DIRECTIONS: Final[dict[type, Callable[[_Converter, Placed], Element]]] = {
    Dynamic: _dynamic,
    ClefChange: _clef_change,
    Words: _words,
    Tempo: _tempo,
    Rehearsal: _rehearsal,
    PedalSign: _pedal_sign,
    PedalLine: _pedal_line,
    HairpinStart: _hairpin_start,
    HairpinEnd: _hairpin_end,
    OttavaStart: _ottava,
    OttavaEnd: _ottava,
    DashesStart: _dashes_start,
    DashesEnd: _dashes_end,
}
