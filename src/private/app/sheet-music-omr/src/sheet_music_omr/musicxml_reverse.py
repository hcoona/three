"""The reverse converter from MusicXML to a Text Score 2 assembled score.

``from_musicxml`` reads a ``score-partwise`` document, plain or compressed as
``.mxl``, with exactly one part on two staves, and returns the assembled score
that the forward converter would turn back into the same musical content. It
inverts the encoding choices of ``musicxml_forward``: heading credits become
heading lines, the work title and composer become metadata, bold words become
text-only tempo marks and the MusicXML voice ``(staff - 1) * 4 + voice`` gives
the voice line.

The converter never repairs or guesses. MusicXML that the format cannot
represent fails with an ``unsupported`` diagnostic, and the result is always
checked by the semantic validator and against the sounding pitches of the
input, so a conversion either succeeds completely or yields only diagnostics.
Layout, identifiers, ``divisions`` values, part names, page and system breaks,
page-number and rights credits, and playback data are not content and are
ignored.
"""

from __future__ import annotations

import io
import lzma
import re
import xml.etree.ElementTree as ET
import zipfile
import zlib
from dataclasses import dataclass, field, replace
from fractions import Fraction
from typing import TYPE_CHECKING, Final, NoReturn
from xml.parsers import expat

from sheet_music_omr.diagnostics import RuleClass, TextScoreError
from sheet_music_omr.lexical import decode_source
from sheet_music_omr.model import (
    Accidental,
    AccidentalKind,
    Arpeggiate,
    ArpeggioRelation,
    Articulation,
    ArticulationKind,
    AssembledScore,
    Barline,
    Beam,
    BeamState,
    CarryMode,
    Clef,
    ClefChange,
    ClefSign,
    CrossStaff,
    DashesEnd,
    DashesStart,
    Duration,
    Dynamic,
    DynamicMark,
    EndingKind,
    EndingOption,
    EndpointRole,
    ExtendedTrillRelation,
    Fermata,
    Grace,
    HairpinEnd,
    HairpinKind,
    HairpinStart,
    HeadedBlock,
    Heading,
    KeyOption,
    LeftRepeatOption,
    LenOption,
    MeasureBlock,
    MeasureRest,
    Meter,
    Metronome,
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
    Pitch,
    Placement,
    Rehearsal,
    Rest,
    RightOption,
    SingleTremolo,
    SlurRelation,
    Spacer,
    Stem,
    StemDirection,
    Tempo,
    TimedReview,
    TimeOption,
    TimeSymbol,
    TimeSymbolOption,
    TremoloRelation,
    TremoloRole,
    Tuplet,
    VoiceLine,
    Words,
)
from sheet_music_omr.parser import parse_document
from sheet_music_omr.pitches import resolve_pitches
from sheet_music_omr.relations import OTTAVA_SHIFTS, analyze_relations
from sheet_music_omr.semantics import validate_document
from sheet_music_omr.timeline import build_timeline
from sheet_music_omr.writer import write_document, write_event

if TYPE_CHECKING:
    from collections.abc import Callable, Iterable

    from sheet_music_omr.diagnostics import Diagnostic
    from sheet_music_omr.model import (
        Event,
        MeasureOption,
        NoteFlag,
        TupletItem,
    )
    from sheet_music_omr.relations import Relation

MAX_INPUT_BYTES: Final = 64 * 1024 * 1024
_ZIP_MAGIC: Final = b"PK\x03\x04"
_CONTAINER: Final = "META-INF/container.xml"
_VOICES_PER_STAFF: Final = 4
_STAVES: Final = 2
_MAX_STROKES: Final = 8
_MAX_DOTS: Final = 3
_RELABEL_ROUNDS: Final = 4
_STEPS: Final = "CDEFGAB"
_LINE_PREFIX: Final = re.compile(r"([12])\.\d: ")

_TYPES: Final = {
    "whole": 1,
    "half": 2,
    "quarter": 4,
    "eighth": 8,
    "16th": 16,
    "32nd": 32,
    "64th": 64,
    "128th": 128,
}
_ACCIDENTALS: Final = {
    "sharp": AccidentalKind.SHARP,
    "flat": AccidentalKind.FLAT,
    "natural": AccidentalKind.NATURAL,
    "double-sharp": AccidentalKind.DOUBLE_SHARP,
    "flat-flat": AccidentalKind.DOUBLE_FLAT,
}
_ARTICULATIONS: Final = {
    "staccato": ArticulationKind.STACCATO,
    "accent": ArticulationKind.ACCENT,
    "tenuto": ArticulationKind.TENUTO,
    "staccatissimo": ArticulationKind.STACCATISSIMO,
    "strong-accent": ArticulationKind.MARCATO,
    "detached-legato": ArticulationKind.PORTATO,
}
_PLACEMENTS: Final = {"above": Placement.ABOVE, "below": Placement.BELOW}
_ORNAMENTS: Final = {
    "trill-mark": OrnamentKind.TRILL,
    "mordent": OrnamentKind.MORDENT,
    "inverted-mordent": OrnamentKind.INVERTED_MORDENT,
    "turn": OrnamentKind.TURN,
    "inverted-turn": OrnamentKind.INVERTED_TURN,
}
_BEAMS: Final = {
    "begin": BeamState.START,
    "continue": BeamState.CONTINUE,
    "end": BeamState.END,
    "forward hook": BeamState.HOOK_FORWARD,
    "backward hook": BeamState.HOOK_BACKWARD,
}
_OTTAVAS: Final = {
    ("down", "8"): OttavaSign.OTTAVA_ALTA,
    ("up", "8"): OttavaSign.OTTAVA_BASSA,
    ("down", "15"): OttavaSign.QUINDICESIMA_ALTA,
    ("up", "15"): OttavaSign.QUINDICESIMA_BASSA,
}
_BEAT_UNITS: Final = {
    ("quarter", False): MetronomeUnit.QUARTER,
    ("half", False): MetronomeUnit.HALF,
    ("eighth", False): MetronomeUnit.EIGHTH,
    ("quarter", True): MetronomeUnit.DOTTED_QUARTER,
    ("half", True): MetronomeUnit.DOTTED_HALF,
}
_PEDAL_LINES: Final = {
    "start": PedalAction.LINE_START,
    "change": PedalAction.LINE_CHANGE,
    "stop": PedalAction.LINE_RELEASE,
}
_PEDAL_SIGNS: Final = {
    "start": (PedalSignKind.DOWN,),
    "stop": (PedalSignKind.UP,),
    "change": (PedalSignKind.UP, PedalSignKind.DOWN),
}
_CLEF_LINES: Final = {"G": 2, "F": 4, "C": 3}
_SYMBOLS: Final = {
    None: TimeSymbol.NUMERIC,
    "normal": TimeSymbol.NUMERIC,
    "common": TimeSymbol.COMMON,
    "cut": TimeSymbol.CUT,
}
_HEADING_CREDITS: Final = frozenset(
    {"title", "subtitle", "composer", "arranger", "lyricist"},
)
_IGNORED_CREDITS: Final = frozenset({"page number", "rights", "part name"})
_IGNORED_MEASURE: Final = frozenset(
    {"print", "sound", "listening", "bookmark", "link", "grouping"},
)
_IGNORED_ATTRIBUTES: Final = frozenset(
    {"instruments", "staff-details", "part-symbol", "footnote", "level"},
)
_NOTE_CHILDREN: Final = frozenset(
    {
        "instrument",
        "footnote",
        "level",
        "play",
        "listen",
        "notehead",
        "chord",
        "grace",
        "pitch",
        "rest",
        "duration",
        "tie",
        "voice",
        "type",
        "dot",
        "accidental",
        "time-modification",
        "stem",
        "staff",
        "beam",
        "notations",
    },
)
_START: Final = "start"
_STOP: Final = "stop"
_CHANGE: Final = "change"
_CONTINUE: Final = "continue"
_YES: Final = "yes"

_NOTE: Final = "note"
_REST: Final = "rest"
_SPACER: Final = "spacer"
_MREST: Final = "mrest"
_DIRECTION: Final = "direction"


# Diagnostics.


@dataclass(frozen=True, slots=True)
class MusicXmlDiagnostic:
    """One reason why a MusicXML document does not convert.

    Attributes:
        rule_class: ``lexical`` for unreadable input, ``grammar`` for
            inconsistent MusicXML, ``unsupported`` for content that the
            converter cannot convert faithfully, or the class of a validator
            finding.
        rule: A stable rule name within the class.
        message: A human-readable description.
        measure: The MusicXML ``number`` of the measure, if known.
        staff: The staff, if known.
        element: The path of the element within the part, if known.
    """

    rule_class: RuleClass
    rule: str
    message: str
    measure: str | None = None
    staff: int | None = None
    element: str | None = None

    @property
    def code(self) -> str:
        """Return the rule class and rule name as ``class/rule``."""
        return f"{self.rule_class}/{self.rule}"

    def format(self, path: str) -> str:
        """Return the ``path: place: class/rule: message`` spelling."""
        place = []
        if self.measure is not None:
            place.append(f"measure {self.measure}")
        if self.staff is not None:
            place.append(f"staff {self.staff}")
        if self.element is not None:
            place.append(self.element)
        where = f" {', '.join(place)}:" if place else ""
        return f"{path}:{where} {self.code}: {self.message}"


class MusicXmlError(Exception):
    """Raised when a MusicXML document does not convert to an assembled score.

    Attributes:
        diagnostics: The reasons, at least one.
    """

    def __init__(self, diagnostics: tuple[MusicXmlDiagnostic, ...]) -> None:
        """Create an error that carries one or more diagnostics."""
        super().__init__("; ".join(d.format("<musicxml>") for d in diagnostics))
        self.diagnostics = diagnostics


@dataclass(frozen=True, slots=True)
class _At:
    """Where a MusicXML element is."""

    measure: str | None = None
    staff: int | None = None
    element: str | None = None


def _fail(
    rule_class: RuleClass, rule: str, message: str, at: _At | None
) -> NoReturn:
    at = at or _At()
    raise MusicXmlError(
        (
            MusicXmlDiagnostic(
                rule_class, rule, message, at.measure, at.staff, at.element
            ),
        ),
    )


def _unsupported(rule: str, message: str, at: _At | None) -> NoReturn:
    _fail(RuleClass.UNSUPPORTED, rule, message, at)


def _malformed(rule: str, message: str, at: _At | None) -> NoReturn:
    _fail(RuleClass.GRAMMAR, rule, message, at)


# Public API.


def from_musicxml(data: bytes | str) -> AssembledScore:
    """Convert a MusicXML piano score to a valid assembled score.

    Args:
        data: A ``score-partwise`` document, or the bytes of a compressed
            ``.mxl`` file, with one part on two staves.

    Returns:
        The assembled score, parsed back from its canonical text, so its
        nodes carry locations in that text.

    Raises:
        MusicXmlError: The input is unreadable, inconsistent or holds content
            that it cannot convert faithfully. No partial result is returned.
    """
    root = _load(data)
    score = _Reader(root).read()
    return score.convert()


def from_musicxml_text(data: bytes | str) -> str:
    """Return the canonical text of ``from_musicxml``."""
    return write_document(from_musicxml(data))


# Input.


class _DeclarationError(Exception):
    pass


def _load(data: bytes | str) -> ET.Element:
    if isinstance(data, str):
        try:
            size = len(data.encode("utf-8"))
        except UnicodeEncodeError:
            msg = "the text holds a character that XML cannot encode"
            _fail(RuleClass.LEXICAL, "xml", msg, None)
        _check_size(size)
        raw: bytes | str = data
    else:
        _check_size(len(data))
        raw = _unzip(data) if data.startswith(_ZIP_MAGIC) else data
    root = _parse_xml(raw)
    if root.tag == "score-timewise":
        msg = "a score-timewise document is not supported"
        _unsupported("score_timewise", msg, None)
    if root.tag != "score-partwise":
        msg = f"the root element is <{root.tag}>, not <score-partwise>"
        _malformed("root", msg, None)
    return root


def _check_size(size: int) -> None:
    if size > MAX_INPUT_BYTES:
        msg = f"the input is larger than {MAX_INPUT_BYTES} bytes"
        _fail(RuleClass.LEXICAL, "input_size", msg, None)


def _unzip(raw: bytes) -> bytes:
    try:
        with zipfile.ZipFile(io.BytesIO(raw)) as archive:
            container = _parse_xml(_zip_member(archive, _CONTAINER))
            rootfile = next(
                (e for e in container.iter() if e.tag == "rootfile"),
                None,
            )
            path = None if rootfile is None else rootfile.get("full-path")
            if not path:
                msg = f"{_CONTAINER} names no rootfile"
                _fail(RuleClass.LEXICAL, "mxl", msg, None)
            return _zip_member(archive, path)
    except (
        zipfile.BadZipFile,
        KeyError,
        EOFError,
        OSError,
        RuntimeError,
        zlib.error,
        lzma.LZMAError,
    ) as error:
        msg = f"the compressed MusicXML file is unreadable: {error}"
        _fail(RuleClass.LEXICAL, "mxl", msg, None)


def _zip_member(archive: zipfile.ZipFile, name: str) -> bytes:
    with archive.open(name) as stream:
        data = stream.read(MAX_INPUT_BYTES + 1)
    _check_size(len(data))
    return data


def _parse_xml(raw: bytes | str) -> ET.Element:
    """Parse XML that declares no entities and has no internal DTD subset."""

    def doctype(
        _name: str,
        _system: str | None,
        _public: str | None,
        internal: bool,  # noqa: FBT001
    ) -> None:
        if internal:
            raise _DeclarationError

    def entity(*_args: object) -> None:
        raise _DeclarationError

    scanner = expat.ParserCreate()
    scanner.StartDoctypeDeclHandler = doctype
    scanner.EntityDeclHandler = entity
    try:
        scanner.Parse(raw, True)  # noqa: FBT003
    except expat.ExpatError as error:
        msg = f"the document is not well-formed XML: {error}"
        _fail(RuleClass.LEXICAL, "xml", msg, None)
    except _DeclarationError:
        msg = "the document declares a DTD internal subset or an entity"
        _fail(RuleClass.LEXICAL, "dtd", msg, None)
    # The scan above has refused every entity declaration, and ElementTree
    # never fetches an external DTD. It still fails on a reference to an
    # entity that an external DTD would declare, such as ``&eacute;``.
    try:
        return ET.fromstring(raw)  # noqa: S314
    except ET.ParseError as error:
        msg = f"the document is not well-formed XML: {error}"
        _fail(RuleClass.LEXICAL, "xml", msg, None)


def _int(text: str | None, rule: str, what: str, at: _At | None) -> int:
    try:
        return int((text or "").strip())
    except ValueError:
        msg = f"{what} is not an integer: {text!r}"
        _malformed(rule, msg, at)


def _ticks(element: ET.Element, at: _At) -> int:
    value = _int(element.findtext("duration"), "duration", "duration", at)
    if value < 0:
        _malformed("duration", "a duration is negative", at)
    return value


def _values(duration: Fraction, at: _At) -> list[Duration]:
    """Split a span into spacer values, one dotted value if possible."""
    for base in _TYPES.values():
        for dots in range(_MAX_DOTS + 1):
            candidate = Duration(base, dots)
            if candidate.value == duration:
                return [candidate]
    values: list[Duration] = []
    rest = duration
    for base in _TYPES.values():
        while rest >= Fraction(1, base):
            values.append(Duration(base))
            rest -= Fraction(1, base)
    if rest:
        msg = f"a gap of {duration} whole notes has no spacer spelling"
        _unsupported("spacer", msg, at)
    return values


# Intermediate structure.


@dataclass(eq=False, slots=True)
class _Group:
    """One tuplet group of a voice line."""

    number: str
    actual: int | None
    normal: int | None
    first: _Leaf | None = None

    @property
    def ratio(self) -> Fraction:
        assert self.actual is not None  # noqa: S101
        assert self.normal is not None  # noqa: S101
        return Fraction(self.actual, self.normal)


@dataclass(eq=False, slots=True)
class _Member:
    """One notehead with its sounding pitch."""

    step: str
    octave: int
    alter: int
    accidental: Accidental | None
    parenthesized: bool
    at: _At
    fingering: list[int] = field(default_factory=list)
    tie_start: bool = False
    tie_stop: bool = False
    start_number: str | None = None
    stop_number: str | None = None
    start_label: int | None = None
    end_label: int | None = None
    tie_in: bool = False

    @property
    def sounding(self) -> tuple[str, int, int]:
        return (self.step, self.octave, self.alter)

    @property
    def order(self) -> tuple[int, str]:
        token = ""
        if self.accidental is not None:
            token = self.accidental.kind.value
            if self.accidental.parenthesized:
                token = f"({token})"
        return (self.octave * 7 + _STEPS.index(self.step), token)


@dataclass(eq=False, slots=True)
class _Leaf:
    """One event of a voice line, before it becomes a model node."""

    kind: str
    measure: int
    staff: int
    voice: int
    shown: int
    onset: Fraction
    duration: Fraction
    order: int
    at: _At
    written: Duration | None = None
    grace: Grace | None = None
    members: list[_Member] = field(default_factory=list)
    flags: list[NoteFlag] = field(default_factory=list)
    modification: Fraction = Fraction(1)
    starts: list[tuple[str, int | None, int | None]] = field(
        default_factory=list
    )
    stops: list[str] = field(default_factory=list)
    path: tuple[_Group, ...] = ()
    slurs: list[tuple[str, str]] = field(default_factory=list)
    wavy: list[tuple[str, str]] = field(default_factory=list)
    pair: tuple[str, int] | None = None
    arpeggio: list[str] = field(default_factory=list)
    relations: list[NoteFlag] = field(default_factory=list)
    event: Event | None = None
    endpoint: tuple[str, str, str] | None = None
    own_staff: bool = False
    label: int | None = None

    @property
    def is_main(self) -> bool:
        return self.kind != _DIRECTION and self.grace is None


@dataclass(eq=False, slots=True)
class _Measure:
    """One MusicXML measure and the state that it changes."""

    index: int
    number: str
    meter: Meter
    options: list[MeasureOption] = field(default_factory=list)
    leaves: list[_Leaf] = field(default_factory=list)
    directions: list[_Leaf] = field(default_factory=list)
    lines: dict[tuple[int, int], list[_Leaf]] = field(default_factory=dict)
    length: Fraction = Fraction(0)
    start: Fraction = Fraction(0)

    def at(self, staff: int | None = None) -> _At:
        return _At(self.number, staff)


@dataclass(slots=True)
class _Cursor:
    """The time cursor of one measure, in divisions."""

    position: int = 0
    furthest: int = 0
    last: _Leaf | None = None

    def move(self, ticks: int, at: _At) -> None:
        self.position += ticks
        if self.position < 0:
            _malformed("backup", "a backup moves before the measure", at)
        self.furthest = max(self.furthest, self.position)


# Reading the document.


class _Reader:
    """Reads one ``score-partwise`` document into measures of leaves."""

    def __init__(self, root: ET.Element) -> None:
        self.root = root
        self.divisions: int | None = None
        self.key: int | None = None
        self.meter: Meter | None = None
        self.current_key = 0
        self.current_meter = Meter(4, 4)
        self.clefs: dict[int, Clef] = {}
        self.staves = 1
        self.ending: int | None = None
        self.order = 0
        self.measures: list[_Measure] = []

    def read(self) -> _Score:
        part = self._part()
        for index, element in enumerate(part.findall("measure")):
            self.measures.append(self._measure(element, index))
        if not self.measures:
            _malformed("measure", "the part has no measure", None)
        if self.ending is not None:
            at = _At(self.measures[-1].number)
            _unsupported("ending", "a volta bracket stays open", at)
        start = Fraction(0)
        for measure in self.measures:
            measure.start = start
            start += measure.length
        assert self.key is not None  # noqa: S101
        assert self.meter is not None  # noqa: S101
        return _Score(
            title=_title(self.root),
            composer=_composer(self.root),
            headings=_headings(self.root),
            key=self.key,
            meter=self.meter,
            clefs=(self.clefs[1], self.clefs[2]),
            measures=self.measures,
        )

    def _part(self) -> ET.Element:
        parts = self.root.findall("part")
        score_parts = self.root.findall("part-list/score-part")
        if len(parts) != 1 or len(score_parts) != 1:
            msg = (
                f"the score has {max(len(parts), len(score_parts))} parts;"
                " only one piano part on two staves converts"
            )
            _unsupported("part_count", msg, None)
        return parts[0]

    def next_order(self) -> int:
        self.order += 1
        return self.order

    def tick_value(self, ticks: int, at: _At) -> Fraction:
        if self.divisions is None:
            _malformed("divisions", "a duration precedes <divisions>", at)
        return Fraction(ticks, 4 * self.divisions)

    # Measures.

    def _measure(self, element: ET.Element, index: int) -> _Measure:
        number = element.get("number", str(index + 1))
        measure = _Measure(index, number, self.current_meter)
        cursor = _Cursor()
        counts: dict[str, int] = {}
        barlines: dict[str, ET.Element] = {}
        for child in element:
            counts[child.tag] = counts.get(child.tag, 0) + 1
            path = f"measure[{index + 1}]/{child.tag}[{counts[child.tag]}]"
            at = _At(number, None, path)
            if child.tag == "barline":
                location = child.get("location", "right")
                if location not in {"left", "right"} or location in barlines:
                    _unsupported("barline", "a mid-measure barline", at)
                barlines[location] = child
            else:
                self._child(child, measure, cursor, at)
        if index == 0:
            self._check_header(measure)
        self._barlines(measure, barlines)
        if self.divisions is None or not any(
            leaf.is_main for leaf in measure.leaves
        ):
            _unsupported("empty_measure", "the measure has no event", None)
        length = Fraction(cursor.furthest, 4 * self.divisions)
        measure.length = max(
            [length, *(leaf.onset + leaf.duration for leaf in measure.leaves)],
        )
        if measure.length != measure.meter.length:
            measure.options.append(LenOption(measure.length))
        _Lines(self, measure).assemble()
        return measure

    def _child(
        self,
        child: ET.Element,
        measure: _Measure,
        cursor: _Cursor,
        at: _At,
    ) -> None:
        if child.tag == "note":
            _NoteReader(self, measure, cursor, at).read(child)
        elif child.tag == "backup":
            cursor.move(-_ticks(child, at), at)
        elif child.tag == "forward":
            self._forward(child, measure, cursor, at)
        elif child.tag == "attributes":
            self._attributes(child, measure, cursor, at)
        elif child.tag == "direction":
            _DirectionReader(self, measure, cursor, at).read(child)
        elif child.tag not in _IGNORED_MEASURE:
            msg = f"<{child.tag}> is not supported"
            _unsupported(child.tag, msg, at)

    def _check_header(self, measure: _Measure) -> None:
        at = measure.at()
        if self.staves != _STAVES:
            msg = (
                f"the part has {self.staves} staves; only a piano grand staff"
                " of two staves converts"
            )
            _unsupported("staff_count", msg, at)
        if self.key is None:
            self.key = 0
        if self.meter is None:
            _unsupported("time", "the first measure has no time signature", at)
        for staff in (1, 2):
            if staff not in self.clefs:
                msg = f"staff {staff} has no clef in the first measure"
                _unsupported("clef", msg, measure.at(staff))

    def _forward(
        self,
        element: ET.Element,
        measure: _Measure,
        cursor: _Cursor,
        at: _At,
    ) -> None:
        ticks = _ticks(element, at)
        voice_text = element.findtext("voice")
        if voice_text is not None:
            staff, voice = _line_of(voice_text, at)
            onset = self.tick_value(cursor.position, at)
            for value in _values(self.tick_value(ticks, at), at):
                measure.leaves.append(
                    _Leaf(
                        _SPACER,
                        measure.index,
                        staff,
                        voice,
                        staff,
                        onset,
                        value.value,
                        self.next_order(),
                        at,
                        written=value,
                    ),
                )
                onset += value.value
        cursor.move(ticks, at)

    # Attributes.

    def _attributes(
        self,
        element: ET.Element,
        measure: _Measure,
        cursor: _Cursor,
        at: _At,
    ) -> None:
        for child in element:
            tag = child.tag
            if tag == "divisions":
                divisions = _int(child.text, "divisions", tag, at)
                if divisions <= 0:
                    _malformed("divisions", "divisions are not positive", at)
                if self.divisions not in {None, divisions} and cursor.furthest:
                    msg = "the divisions change inside a measure"
                    _unsupported("divisions_change", msg, at)
                self.divisions = divisions
            elif tag == "staves":
                self.staves = _int(child.text, "staves", tag, at)
                if self.staves != _STAVES:
                    msg = f"the part has {self.staves} staves, not two"
                    _unsupported("staff_count", msg, at)
            elif tag not in {"key", "time", "clef"} | _IGNORED_ATTRIBUTES:
                msg = f"<{tag}> in <attributes> is not supported"
                _unsupported(tag, msg, at)
        self._signatures(element, measure, cursor, at)
        for child in element.findall("clef"):
            self._clef(child, measure, cursor, at)

    def _signatures(
        self,
        element: ET.Element,
        measure: _Measure,
        cursor: _Cursor,
        at: _At,
    ) -> None:
        keys, times = _signature_values(element, at)
        if (keys or times) and cursor.position != 0:
            msg = "a key or time signature changes inside a measure"
            _unsupported("signature_change", msg, at)
        for fifths in keys:
            if measure.index == 0 and self.key is None:
                self.key = fifths
            elif fifths != self.current_key:
                measure.options.append(KeyOption(fifths))
            self.current_key = fifths
        for meter, symbol in times:
            if measure.index == 0 and self.meter is None:
                self.meter = meter
            elif meter != measure.meter:
                measure.options.append(TimeOption(meter))
            measure.meter = self.current_meter = meter
            if symbol is not TimeSymbol.NUMERIC:
                measure.options.append(TimeSymbolOption(symbol))

    def _clef(
        self,
        element: ET.Element,
        measure: _Measure,
        cursor: _Cursor,
        at: _At,
    ) -> None:
        staff = _int(element.get("number", "1"), "staff", "a clef number", at)
        if staff not in {1, 2}:
            _malformed("staff", f"clef number {staff} is not 1 or 2", at)
        clef = _clef_of(element, replace(at, staff=staff))
        first = measure.index == 0 and cursor.position == 0
        if first and staff not in self.clefs:
            self.clefs[staff] = clef
            return
        onset = self.tick_value(cursor.position, at)
        measure.directions.append(
            _Leaf(
                _DIRECTION,
                measure.index,
                staff,
                0,
                staff,
                onset,
                Fraction(0),
                self.next_order(),
                replace(at, staff=staff),
                event=ClefChange(clef),
                own_staff=True,
            ),
        )

    # Barlines and endings.

    def _barlines(
        self, measure: _Measure, barlines: dict[str, ET.Element]
    ) -> None:
        left = barlines.get("left")
        right = barlines.get("right")
        start = None if left is None else self._left(left, measure)
        close = None if right is None else self._right(right, measure)
        self._ending(measure, start, close)

    def _ending(
        self, measure: _Measure, start: int | None, close: int | None
    ) -> None:
        at = measure.at()
        if start is not None:
            if self.ending is not None:
                msg = "a volta bracket starts inside another one"
                _unsupported("ending", msg, at)
            if close is None:
                measure.options.append(EndingOption(EndingKind.START, start))
                self.ending = start
            elif close != start:
                _unsupported("ending", "the bracket numbers differ", at)
            else:
                measure.options.append(EndingOption(EndingKind.SINGLE, start))
        elif self.ending is not None:
            if close is None:
                measure.options.append(EndingOption(EndingKind.CONTINUE))
            elif close != self.ending:
                _unsupported("ending", "the bracket numbers differ", at)
            else:
                measure.options.append(EndingOption(EndingKind.END))
                self.ending = None
        elif close is not None:
            _unsupported("ending", "a volta bracket ends without start", at)

    def _left(self, element: ET.Element, measure: _Measure) -> int | None:
        at = measure.at()
        _check_barline_children(element, at)
        style = element.findtext("bar-style")
        repeat = element.find("repeat")
        if repeat is not None:
            if repeat.get("direction") != "forward" or style not in {
                None,
                "heavy-light",
            }:
                _unsupported("barline", "a left barline repeats backward", at)
            measure.options.append(LeftRepeatOption())
        elif style not in {None, "regular"}:
            msg = f"a left barline of style {style!r} is not supported"
            _unsupported("barline", msg, at)
        ending = element.find("ending")
        if ending is None:
            return None
        if ending.get("type") != _START:
            msg = "a volta bracket stops at a left barline"
            _unsupported("ending", msg, at)
        return _ending_number(ending, at)

    def _right(self, element: ET.Element, measure: _Measure) -> int | None:
        at = measure.at()
        _check_barline_children(element, at)
        style = element.findtext("bar-style")
        repeat = element.find("repeat")
        if repeat is not None:
            if repeat.get("direction") != "backward" or style not in {
                None,
                "light-heavy",
            }:
                msg = "a right barline repeats forward or is not light-heavy"
                _unsupported("barline", msg, at)
            measure.options.append(RightOption(Barline.REPEAT))
        elif style == "light-heavy":
            measure.options.append(RightOption(Barline.FINAL))
        elif style == "light-light":
            measure.options.append(RightOption(Barline.DOUBLE))
        elif style not in {None, "regular"}:
            msg = f"a barline of style {style!r} is not supported"
            _unsupported("barline", msg, at)
        ending = element.find("ending")
        if ending is None:
            return None
        if ending.get("type") not in {_STOP, "discontinue"}:
            msg = "a volta bracket starts at a right barline"
            _unsupported("ending", msg, at)
        return _ending_number(ending, at)


def _signature_values(
    element: ET.Element, at: _At
) -> tuple[set[int], set[tuple[Meter, TimeSymbol]]]:
    for tag in ("key", "time"):
        _check_staves(element.findall(tag), at)
    keys = {_key(child, at) for child in element.findall("key")}
    times = {_time(child, at) for child in element.findall("time")}
    if len(keys) > 1 or len(times) > 1:
        msg = "the staves have different key or time signatures"
        _unsupported("signature", msg, at)
    return keys, times


def _check_staves(declarations: list[ET.Element], at: _At) -> None:
    covered = {
        _int(number, "staff", "a signature number", at)
        for number in (child.get("number") for child in declarations)
        if number is not None
    }
    if any(child.get("number") is None for child in declarations):
        covered |= {1, 2}
    if declarations and covered != {1, 2}:
        msg = "a key or time signature for one staff only"
        _unsupported("signature", msg, at)


def _check_barline_children(element: ET.Element, at: _At) -> None:
    for child in element:
        if child.tag not in {"bar-style", "repeat", "ending", "footnote"}:
            msg = f"<{child.tag}> in a barline is not supported"
            _unsupported(child.tag, msg, at)


def _ending_number(element: ET.Element, at: _At) -> int:
    text = element.get("number", "").strip()
    if not text.isdigit() or int(text) <= 0:
        msg = f"the volta number {text!r} is not one positive integer"
        _unsupported("ending_number", msg, at)
    return int(text)


def _key(element: ET.Element, at: _At) -> int:
    if element.get("print-object") == "no":
        _unsupported("key", "a hidden key signature", at)
    if element.find("fifths") is None:
        _unsupported("key", "a non-traditional key signature", at)
    return _int(element.findtext("fifths"), "key", "fifths", at)


def _time(element: ET.Element, at: _At) -> tuple[Meter, TimeSymbol]:
    if element.get("print-object") == "no":
        _unsupported("time", "a hidden time signature", at)
    symbol = element.get("symbol")
    if symbol not in _SYMBOLS:
        msg = f"the time symbol {symbol!r} is not supported"
        _unsupported("time_symbol", msg, at)
    beats = element.findall("beats")
    types = element.findall("beat-type")
    if (
        len(beats) != 1
        or len(types) != 1
        or element.find("senza-misura") is not None
    ):
        _unsupported("time", "a composite or free time signature", at)
    numerator = (beats[0].text or "").strip()
    denominator = (types[0].text or "").strip()
    if not numerator.isdigit() or not denominator.isdigit():
        msg = f"the time signature {numerator}/{denominator} is not numeric"
        _unsupported("time", msg, at)
    if int(denominator) == 0:
        _malformed("time", "the time signature has beat type 0", at)
    return Meter(int(numerator), int(denominator)), _SYMBOLS[symbol]


def _clef_of(element: ET.Element, at: _At) -> Clef:
    if element.get("print-object") == "no":
        _unsupported("clef", "a hidden clef", at)
    sign = (element.findtext("sign") or "").strip()
    if sign not in _CLEF_LINES:
        _unsupported("clef", f"the clef sign {sign!r} is not supported", at)
    line_text = element.findtext("line")
    line = (
        _CLEF_LINES[sign]
        if line_text is None
        else _int(line_text, "clef", "a clef line", at)
    )
    change = _int(
        element.findtext("clef-octave-change", "0"),
        "clef",
        "a clef octave change",
        at,
    )
    if change not in {-1, 0, 1} or not 1 <= line <= 5:  # noqa: PLR2004
        _unsupported("clef", "the clef is not supported", at)
    return Clef(ClefSign(sign), line, change)


def _line_of(voice_text: str, at: _At) -> tuple[int, int]:
    number = _int(voice_text, "voice", "a voice", at)
    if not 1 <= number <= _STAVES * _VOICES_PER_STAFF:
        msg = f"MusicXML voice {number} lies outside 1 to 8"
        _unsupported("voice", msg, at)
    staff, voice = divmod(number - 1, _VOICES_PER_STAFF)
    return staff + 1, voice + 1


def _staff_of(element: ET.Element, at: _At) -> int:
    staff = _int(element.findtext("staff", "1"), "staff", "a staff", at)
    if staff not in {1, 2}:
        _malformed("staff", f"staff {staff} is not 1 or 2", at)
    return staff


def _clean(text: str | None) -> str | None:
    stripped = (text or "").strip()
    return stripped or None


def _title(root: ET.Element) -> str | None:
    return _clean(root.findtext("work/work-title")) or _clean(
        root.findtext("movement-title"),
    )


def _composer(root: ET.Element) -> str | None:
    creators = root.findall("identification/creator[@type='composer']")
    if len(creators) > 1:
        _unsupported("creator", "the score names several composers", None)
    return _clean(creators[0].text) if creators else None


def _headings(root: ET.Element) -> list[str]:
    found: list[tuple[ET.Element, int]] = []
    for position, credit in enumerate(root.findall("credit"), 1):
        at = _At(element=f"credit[{position}]")
        types = {(t.text or "").strip() for t in credit.findall("credit-type")}
        if not types:
            msg = "a credit has no credit-type, so its role is unknown"
            _unsupported("credit_type", msg, at)
        if types <= _IGNORED_CREDITS:
            continue
        if not types <= _HEADING_CREDITS:
            msg = f"credit types {sorted(types)} are not supported"
            _unsupported("credit_type", msg, at)
        if (
            credit.find("credit-image") is not None
            or credit.find("credit-symbol") is not None
        ):
            _unsupported("credit", "an image or symbol credit", at)
        found.append((credit, position))
    if all(
        credit.find("credit-words[@default-y]") is not None
        for credit, _ in found
    ):
        found.sort(key=_credit_place)
    lines: list[str] = []
    for credit, _ in found:
        text = "".join(w.text or "" for w in credit.findall("credit-words"))
        lines.extend(line.strip() for line in text.splitlines() if line.strip())
    return lines


def _credit_place(item: tuple[ET.Element, int]) -> tuple[float, ...]:
    credit, position = item
    words = credit.find("credit-words[@default-y]")
    assert words is not None  # noqa: S101
    try:
        return (
            float(credit.get("page", "1")),
            -float(words.get("default-y", "0")),
            float(words.get("default-x", "0")),
            position,
        )
    except ValueError:
        at = _At(element=f"credit[{position}]")
        _malformed("credit", "a credit position is not a number", at)


# Notes.


class _NoteReader:
    """Reads one ``note`` element into a leaf or a chord member."""

    def __init__(
        self,
        reader: _Reader,
        measure: _Measure,
        cursor: _Cursor,
        at: _At,
    ) -> None:
        self.reader = reader
        self.measure = measure
        self.cursor = cursor
        self.at = at

    def read(self, element: ET.Element) -> None:
        shown = _staff_of(element, self.at)
        self.at = replace(self.at, staff=shown)
        for child in element:
            if child.tag not in _NOTE_CHILDREN:
                msg = f"<{child.tag}> in a note is not supported"
                _unsupported(child.tag, msg, self.at)
        voice_text = element.findtext("voice")
        if voice_text is None:
            staff, voice = shown, 1
        else:
            staff, voice = _line_of(voice_text, self.at)
        grace_element = element.find("grace")
        grace = (
            None
            if grace_element is None
            else Grace(grace_element.get("slash") == _YES)
        )
        ticks = 0 if grace is not None else _ticks(element, self.at)
        duration = self.reader.tick_value(ticks, self.at)
        if element.find("chord") is not None:
            self._chord(element, (staff, voice, shown), grace, duration)
            return
        onset = self.reader.tick_value(self.cursor.position, self.at)
        self.cursor.move(ticks, self.at)
        leaf = _Leaf(
            _NOTE,
            self.measure.index,
            staff,
            voice,
            shown,
            onset,
            duration,
            self.reader.next_order(),
            self.at,
            grace=grace,
        )
        rest = element.find("rest")
        if rest is not None:
            self._rest(element, rest, leaf)
            return
        self.cursor.last = leaf
        leaf.written = self._written(element, required=True)
        leaf.modification = _modification(element, self.at)
        self._stem(element, leaf)
        self._beams(element, leaf)
        leaf.members.append(self._member(element, leaf))
        self.measure.leaves.append(leaf)

    def _chord(
        self,
        element: ET.Element,
        place: tuple[int, int, int],
        grace: Grace | None,
        duration: Fraction,
    ) -> None:
        head = self.cursor.last
        if head is None or element.find("rest") is not None:
            _malformed("chord", "a chord note follows no note", self.at)
        if place[2] != head.shown:
            msg = "a chord with noteheads on two staves"
            _unsupported("mixed_staff_chord", msg, self.at)
        if (
            place[:2] != (head.staff, head.voice)
            or grace != head.grace
            or duration != head.duration
            or self._written(element, required=True) != head.written
        ):
            msg = "a chord note differs from its chord in voice or duration"
            _malformed("chord", msg, self.at)
        self._stem(element, head)
        self._beams(element, head)
        head.members.append(self._member(element, head))

    def _rest(self, element: ET.Element, rest: ET.Element, leaf: _Leaf) -> None:
        self.cursor.last = None
        if leaf.grace is not None:
            _malformed("rest", "a grace rest", self.at)
        if leaf.shown != leaf.staff:
            msg = "a rest displayed on the other staff"
            _unsupported("cross_staff_rest", msg, self.at)
        hidden = element.get("print-object") == "no"
        written = self._written(element, required=False)
        leaf.modification = _modification(element, self.at)
        if hidden and written is None:
            if (
                element.find("notations") is not None
                or element.find("beam") is not None
            ):
                _unsupported("spacer", "a hidden rest without type", self.at)
            onset = leaf.onset
            for value in _values(leaf.duration, self.at):
                self.measure.leaves.append(
                    replace(
                        leaf,
                        kind=_SPACER,
                        onset=onset,
                        duration=value.value,
                        written=value,
                    ),
                )
                onset += value.value
            return
        if rest.get("measure") == _YES and not hidden:
            leaf.kind = _MREST
        elif written is None:
            _malformed("type", "a rest has no <type>", self.at)
        else:
            leaf.kind = _SPACER if hidden else _REST
            leaf.written = written
        self._beams(element, leaf)
        allowed = {"tuplet"} if hidden else {"tuplet", "fermata"}
        _Notations(leaf, None, self.at, allowed).read(element)
        self.measure.leaves.append(leaf)

    def _written(
        self, element: ET.Element, *, required: bool
    ) -> Duration | None:
        type_text = element.findtext("type")
        if type_text is None:
            if required:
                _malformed("type", "a note has no <type>", self.at)
            return None
        base = _TYPES.get(type_text.strip())
        if base is None:
            msg = f"the note value {type_text!r} is not supported"
            _unsupported("note_type", msg, self.at)
        return Duration(base, len(element.findall("dot")))

    def _stem(self, element: ET.Element, leaf: _Leaf) -> None:
        text = (element.findtext("stem") or "none").strip()
        if text in {"up", "down"}:
            _add(leaf.flags, Stem(StemDirection(text)))
        elif text != "none":
            _unsupported("stem", f"a {text!r} stem is not supported", self.at)

    def _beams(self, element: ET.Element, leaf: _Leaf) -> None:
        for beam in element.findall("beam"):
            state = _BEAMS.get((beam.text or "").strip())
            if state is None:
                msg = f"the beam value {beam.text!r} is not supported"
                _unsupported("beam", msg, self.at)
            level = _int(
                beam.get("number", "1"), "beam", "a beam level", self.at
            )
            _add(leaf.flags, Beam(level, state))

    def _member(self, element: ET.Element, leaf: _Leaf) -> _Member:
        if element.get("print-object") == "no":
            _unsupported("hidden_note", "a hidden pitched note", self.at)
        pitch = element.find("pitch")
        if pitch is None:
            _unsupported("unpitched", "a note without pitch", self.at)
        step = (pitch.findtext("step") or "").strip()
        if len(step) != 1 or step not in _STEPS:
            _malformed("pitch", f"the step {step!r} is not valid", self.at)
        try:
            alter = Fraction((pitch.findtext("alter") or "0").strip())
        except ValueError:
            alter = Fraction(1, 2)
        if alter.denominator != 1 or abs(alter) > 2:  # noqa: PLR2004
            msg = "an alteration other than -2 to 2 semitones"
            _unsupported("alter", msg, self.at)
        octave = _int(pitch.findtext("octave"), "pitch", "an octave", self.at)
        member = _Member(
            step,
            octave,
            int(alter),
            self._accidental(element),
            self._parenthesized(element),
            self.at,
        )
        _Notations(leaf, member, self.at, None).read(element)
        return member

    def _accidental(self, element: ET.Element) -> Accidental | None:
        accidental = element.find("accidental")
        if accidental is None:
            return None
        kind = _ACCIDENTALS.get((accidental.text or "").strip())
        if kind is None or accidental.get("bracket") == _YES:
            msg = f"the accidental {accidental.text!r} is not supported"
            _unsupported("accidental", msg, self.at)
        return Accidental(kind, accidental.get("parentheses") == _YES)

    def _parenthesized(self, element: ET.Element) -> bool:
        notehead = element.find("notehead")
        if notehead is None:
            return False
        if (notehead.text or "").strip() != "normal":
            msg = f"the notehead {notehead.text!r} is not supported"
            _unsupported("notehead", msg, self.at)
        return notehead.get("parentheses") == _YES


def _modification(element: ET.Element, at: _At) -> Fraction:
    modification = element.find("time-modification")
    if modification is None:
        return Fraction(1)
    actual = _int(modification.findtext("actual-notes"), "tuplet", "", at)
    normal = _int(modification.findtext("normal-notes"), "tuplet", "", at)
    if actual <= 0 or normal <= 0:
        _malformed("tuplet", "a time modification is not positive", at)
    return Fraction(actual, normal)


def _single_wavy(leaf: _Leaf, family: str, number: str) -> bool:
    """Tell whether a wavy line starts and stops on this one note."""
    if family != "wavy-line" or (_START, number) not in leaf.wavy:
        return False
    if Ornament(OrnamentKind.TRILL) not in leaf.flags:
        msg = "a wavy line that no trill sign starts"
        _unsupported("wavy_line", msg, leaf.at)
    return True


def _set_tie(member: _Member, kind: str | None, at: _At) -> None:
    if kind == _START:
        member.tie_start = True
    elif kind == _STOP:
        member.tie_stop = True
    else:
        _unsupported("tie", f"a tie of type {kind!r} is not supported", at)


def _add[T](items: list[T], item: T) -> None:
    if item not in items:
        items.append(item)


class _Notations:
    """Reads the ``notations`` of one note or rest into its leaf."""

    def __init__(
        self,
        leaf: _Leaf,
        member: _Member | None,
        at: _At,
        allowed: set[str] | None,
    ) -> None:
        self.leaf = leaf
        self.member = member
        self.at = at
        self.allowed = allowed

    def read(self, element: ET.Element) -> None:
        for notations in element.findall("notations"):
            for child in notations:
                if child.tag in {"footnote", "level"}:
                    continue
                if self.allowed is not None and child.tag not in self.allowed:
                    msg = f"<{child.tag}> on a rest is not supported"
                    _unsupported(child.tag, msg, self.at)
                handler = _NOTATIONS.get(child.tag)
                if handler is None:
                    msg = f"the notation <{child.tag}> is not supported"
                    _unsupported(child.tag, msg, self.at)
                handler(self, child)

    def tied(self, element: ET.Element) -> None:
        assert self.member is not None  # noqa: S101
        kind = element.get("type")
        _set_tie(self.member, kind, self.at)
        if kind == _START:
            self.member.start_number = element.get("number")
        else:
            self.member.stop_number = element.get("number")

    def slur(self, element: ET.Element) -> None:
        kind = element.get("type")
        if kind in {_START, _STOP}:
            _add(self.leaf.slurs, (kind, element.get("number", "1")))

    def tuplet(self, element: ET.Element) -> None:
        number = element.get("number", "1")
        if element.get("type") == _STOP:
            _add(self.leaf.stops, number)
            return
        if any(start[0] == number for start in self.leaf.starts):
            return
        actual = element.findtext("tuplet-actual/tuplet-number")
        normal = element.findtext("tuplet-normal/tuplet-number")
        if actual is None or normal is None:
            self.leaf.starts.append((number, None, None))
            return
        ratio = (
            _int(actual, "tuplet", "a tuplet number", self.at),
            _int(normal, "tuplet", "a tuplet number", self.at),
        )
        if min(ratio) <= 0:
            _malformed("tuplet", "a tuplet number is not positive", self.at)
        self.leaf.starts.append((number, *ratio))

    def ornaments(self, element: ET.Element) -> None:
        for child in element:
            kind = _ORNAMENTS.get(child.tag)
            if kind is not None:
                if child.get("long") == _YES or child.get("slash") == _YES:
                    msg = f"a long or slashed <{child.tag}> is not supported"
                    _unsupported(child.tag, msg, self.at)
                _add(self.leaf.flags, Ornament(kind))
            elif child.tag == "wavy-line":
                kind_text = child.get("type")
                if kind_text in {_START, _STOP}:
                    number = child.get("number", "1")
                    _add(self.leaf.wavy, (kind_text, number))
            elif child.tag == "tremolo":
                self._tremolo(child)
            else:
                msg = f"the ornament <{child.tag}> is not supported"
                _unsupported(child.tag, msg, self.at)

    def _tremolo(self, element: ET.Element) -> None:
        kind = element.get("type", "single")
        strokes = _int(element.text, "tremolo", "a stroke count", self.at)
        if kind not in {"single", _START, _STOP} or not (
            1 <= strokes <= _MAX_STROKES
        ):
            msg = "an unmeasured tremolo or one with 0 strokes"
            _unsupported("tremolo", msg, self.at)
        if kind == "single":
            _add(self.leaf.flags, SingleTremolo(strokes))
        elif self.leaf.pair not in {None, (kind, strokes)}:
            _unsupported("tremolo", "conflicting tremolo marks", self.at)
        else:
            self.leaf.pair = (kind, strokes)

    def technical(self, element: ET.Element) -> None:
        for child in element:
            if (
                child.tag != "fingering"
                or child.get("substitution") == _YES
                or child.get("alternate") == _YES
            ):
                msg = f"the technical mark <{child.tag}> is not supported"
                _unsupported(child.tag, msg, self.at)
            text = (child.text or "").strip()
            if not text or any(digit not in "12345" for digit in text):
                msg = f"the fingering {child.text!r} is not supported"
                _unsupported("fingering", msg, self.at)
            assert self.member is not None  # noqa: S101
            self.member.fingering.extend(int(digit) for digit in text)

    def articulations(self, element: ET.Element) -> None:
        for child in element:
            kind = _ARTICULATIONS.get(child.tag)
            if kind is None:
                msg = f"the articulation <{child.tag}> is not supported"
                _unsupported(child.tag, msg, self.at)
            placement = _PLACEMENTS.get(child.get("placement", ""))
            _add(self.leaf.flags, Articulation(kind, placement))

    def fermata(self, element: ET.Element) -> None:
        if (element.text or "").strip() not in {"", "normal"}:
            msg = f"the fermata shape {element.text!r} is not supported"
            _unsupported("fermata", msg, self.at)
        _add(self.leaf.flags, Fermata(element.get("type") == "inverted"))

    def arpeggiate(self, element: ET.Element) -> None:
        if element.get("direction") is not None:
            msg = "an arpeggio with an arrow is not supported"
            _unsupported("arpeggiate", msg, self.at)
        _add(self.leaf.arpeggio, element.get("number", ""))


_NOTATIONS: Final[dict[str, Callable[[_Notations, ET.Element], None]]] = {
    "tied": _Notations.tied,
    "slur": _Notations.slur,
    "tuplet": _Notations.tuplet,
    "ornaments": _Notations.ornaments,
    "technical": _Notations.technical,
    "articulations": _Notations.articulations,
    "fermata": _Notations.fermata,
    "arpeggiate": _Notations.arpeggiate,
}


# Directions.


class _DirectionReader:
    """Reads one ``direction`` element into direction leaves."""

    def __init__(
        self,
        reader: _Reader,
        measure: _Measure,
        cursor: _Cursor,
        at: _At,
    ) -> None:
        self.reader = reader
        self.measure = measure
        self.cursor = cursor
        self.at = at

    def read(self, element: ET.Element) -> None:
        offset = element.findtext("offset")
        if offset is not None and _int(offset, "offset", "offset", self.at):
            msg = "a direction with an offset from its position"
            _unsupported("direction_offset", msg, self.at)
        staff = _staff_of(element, self.at)
        self.at = replace(self.at, staff=staff)
        kinds = [
            kind
            for direction_type in element.findall("direction-type")
            for kind in direction_type
        ]
        words = [kind for kind in kinds if kind.tag == "words"]
        others = [kind for kind in kinds if kind.tag != "words"]
        text = "".join(word.text or "" for word in words)
        if not text.strip():
            text = ""
        tags = {kind.tag for kind in others}
        if tags & {"metronome", "dashes"}:
            if len(others) != 1:
                msg = "a tempo or dashes line combined with another mark"
                _unsupported("direction", msg, self.at)
            if others[0].tag == "metronome":
                metronome = self._metronome(others[0])
                self._add(staff, Tempo(text, metronome))
            else:
                self._dashes(staff, others[0], text)
            return
        if text:
            sound = element.find("sound")
            bold = all(word.get("font-weight") == "bold" for word in words)
            if bold or (sound is not None and sound.get("tempo") is not None):
                self._add(staff, Tempo(text))
            else:
                self._add(staff, Words(text))
        placement = element.get("placement")
        for kind in others:
            self._other(staff, kind, placement)

    def _add(
        self,
        staff: int,
        event: Event,
        endpoint: tuple[str, str, str] | None = None,
        *,
        own_staff: bool = False,
    ) -> None:
        onset = self.reader.tick_value(self.cursor.position, self.at)
        self.measure.directions.append(
            _Leaf(
                _DIRECTION,
                self.measure.index,
                staff,
                0,
                staff,
                onset,
                Fraction(0),
                self.reader.next_order(),
                self.at,
                event=event,
                endpoint=endpoint,
                own_staff=own_staff,
            ),
        )

    def _metronome(self, element: ET.Element) -> Metronome:
        if (
            element.get("parentheses") == _YES
            or element.find("metronome-relation") is not None
            or len(element.findall("beat-unit")) != 1
        ):
            msg = "a metronome mark other than unit = number"
            _unsupported("metronome", msg, self.at)
        unit = (element.findtext("beat-unit") or "").strip()
        dotted = len(element.findall("beat-unit-dot"))
        kind = _BEAT_UNITS.get((unit, bool(dotted)))
        per_minute = (element.findtext("per-minute") or "").strip()
        if kind is None or dotted > 1 or not per_minute.isdigit():
            msg = "the metronome unit or value is not supported"
            _unsupported("metronome", msg, self.at)
        return Metronome(kind, int(per_minute))

    def _dashes(self, staff: int, element: ET.Element, text: str) -> None:
        kind = element.get("type")
        number = element.get("number", "1")
        if kind == _START:
            if not text:
                _unsupported("dashes", "a dashes line without text", self.at)
            self._add(staff, DashesStart(text, 0), ("dashes", _START, number))
        elif kind == _STOP and not text:
            self._add(staff, DashesEnd(0), ("dashes", _STOP, number))
        elif kind != _CONTINUE or text:
            _unsupported("dashes", "a dashes mark is not supported", self.at)

    def _other(
        self, staff: int, element: ET.Element, placement: str | None
    ) -> None:
        tag = element.tag
        if tag == "dynamics":
            marks = list(element)
            values = {mark.value for mark in DynamicMark}
            if len(marks) != 1 or marks[0].tag not in values:
                msg = "the dynamic mark is not supported"
                _unsupported("dynamics", msg, self.at)
            above = (element.get("placement") or placement) == "above"
            self._add(staff, Dynamic(DynamicMark(marks[0].tag), above=above))
        elif tag == "rehearsal":
            text = element.text or ""
            if not text.strip():
                _unsupported("rehearsal", "an empty rehearsal mark", self.at)
            self._add(staff, Rehearsal(text))
        elif tag == "pedal":
            self._pedal(staff, element)
        elif tag == "wedge":
            self._wedge(staff, element)
        elif tag == "octave-shift":
            self._octave_shift(staff, element)
        else:
            msg = f"the direction <{tag}> is not supported"
            _unsupported(tag, msg, self.at)

    def _pedal(self, staff: int, element: ET.Element) -> None:
        kind = element.get("type", "")
        line = element.get("line") == _YES
        sign = element.get("sign", "no" if line else _YES) == _YES
        if kind == _CONTINUE:
            return
        if line and kind in _PEDAL_LINES:
            number = element.get("number", "1")
            role = _STOP if kind == _STOP else kind
            self._add(
                staff,
                PedalLine(0, _PEDAL_LINES[kind]),
                ("pedal", role, number),
            )
        elif not line and sign and kind in _PEDAL_SIGNS:
            for sign_kind in _PEDAL_SIGNS[kind]:
                self._add(staff, PedalSign(sign_kind))
        else:
            msg = f"a pedal mark of type {kind!r} is not supported"
            _unsupported("pedal", msg, self.at)

    def _wedge(self, staff: int, element: ET.Element) -> None:
        kind = element.get("type")
        number = element.get("number", "1")
        if element.get("niente") == _YES:
            _unsupported("wedge", "a niente hairpin", self.at)
        if kind in {"crescendo", "diminuendo"}:
            hairpin = (
                HairpinKind.CRESC if kind == "crescendo" else HairpinKind.DIM
            )
            self._add(
                staff,
                HairpinStart(hairpin, 0),
                ("wedge", _START, number),
            )
        elif kind == _STOP:
            self._add(staff, HairpinEnd(0), ("wedge", _STOP, number))
        elif kind != _CONTINUE:
            _unsupported("wedge", f"a wedge of type {kind!r}", self.at)

    def _octave_shift(self, staff: int, element: ET.Element) -> None:
        kind = element.get("type")
        number = element.get("number", "1")
        size = element.get("size", "8")
        if kind == _CONTINUE:
            return
        if kind == _STOP:
            sign = OttavaSign.OTTAVA_ALTA
            event: Event = OttavaEnd(sign, 0, sign)
            self._add(
                staff, event, ("octave-shift", _STOP, number), own_staff=True
            )
            return
        sign = _OTTAVAS.get((kind or "", size))
        if sign is None:
            msg = f"an octave shift {kind} {size} is not supported"
            _unsupported("octave_shift", msg, self.at)
        self._add(
            staff,
            OttavaStart(sign, 0, sign),
            ("octave-shift", _START, number),
            own_staff=True,
        )


# Voice lines.


class _Lines:
    """Arranges the leaves of one measure into voice lines."""

    def __init__(self, reader: _Reader, measure: _Measure) -> None:
        self.reader = reader
        self.measure = measure

    def assemble(self) -> None:
        lines = self.measure.lines
        for leaf in self.measure.leaves:
            lines.setdefault((leaf.staff, leaf.voice), []).append(leaf)
        for key, leaves in lines.items():
            leaves.sort(key=lambda leaf: (leaf.onset, leaf.is_main, leaf.order))
            mains = [leaf for leaf in leaves if leaf.is_main]
            _group_tuplets(mains)
            for leaf in mains:
                _check_duration(leaf)
            lines[key] = [
                *(leaf for leaf in leaves if not leaf.is_main),
                *self._fill(key, mains),
            ]
        for direction in self.measure.directions:
            self._place(direction)

    def _spacers(
        self, key: tuple[int, int], start: Fraction, end: Fraction
    ) -> list[_Leaf]:
        at = self.measure.at(key[0])
        spacers: list[_Leaf] = []
        for value in _values(end - start, at) if end > start else []:
            spacers.append(
                _Leaf(
                    _SPACER,
                    self.measure.index,
                    key[0],
                    key[1],
                    key[0],
                    start,
                    value.value,
                    self.reader.next_order(),
                    at,
                    written=value,
                ),
            )
            start += value.value
        return spacers

    def _fill(self, key: tuple[int, int], mains: list[_Leaf]) -> list[_Leaf]:
        """Fill the gaps of a line with spacers, refusing overlaps."""
        filled: list[_Leaf] = []
        cursor = Fraction(0)
        previous: _Leaf | None = None
        for leaf in mains:
            if leaf.onset < cursor:
                msg = "two events of one voice overlap"
                _unsupported("voice_overlap", msg, leaf.at)
            if (
                previous is not None
                and leaf.onset > cursor
                and set(previous.path) & set(leaf.path)
            ):
                msg = "a gap inside a tuplet group"
                _unsupported("tuplet_gap", msg, leaf.at)
            filled.extend(self._spacers(key, cursor, leaf.onset))
            filled.append(leaf)
            cursor = leaf.onset + leaf.duration
            previous = leaf
        filled.extend(self._spacers(key, cursor, self.measure.length))
        return filled

    def _boundaries(self, key: tuple[int, int]) -> set[Fraction]:
        bounds = {Fraction(0)}
        for leaf in self.measure.lines[key]:
            if leaf.is_main:
                bounds.update((leaf.onset, leaf.onset + leaf.duration))
        return bounds

    def _first_line(
        self, staff: int, onset: Fraction
    ) -> tuple[int, int] | None:
        return next(
            (
                key
                for key in sorted(self.measure.lines)
                if key[0] == staff and onset in self._boundaries(key)
            ),
            None,
        )

    def _place(self, direction: _Leaf) -> None:
        """Put a direction into the line that the transcription rules give."""
        lines = self.measure.lines
        key = self._first_line(direction.staff, direction.onset)
        if key is None and direction.own_staff:
            used = {voice for staff, voice in lines if staff == direction.staff}
            voice = next(
                (v for v in range(1, _VOICES_PER_STAFF + 1) if v not in used),
                None,
            )
            if voice is None:
                msg = "a fifth voice line on one staff"
                _unsupported("voice_count", msg, direction.at)
            key = (direction.staff, voice)
            lines[key] = [
                *self._spacers(key, Fraction(0), direction.onset),
                *self._spacers(key, direction.onset, self.measure.length),
            ]
        if key is None:
            key = self._first_line(3 - direction.staff, direction.onset)
        if key is None:
            msg = "a direction lies at no event boundary of any voice line"
            _unsupported("direction_position", msg, direction.at)
        direction.staff, direction.voice = key
        lines[key].append(direction)


def _group_tuplets(mains: list[_Leaf]) -> None:
    """Give each rhythmic leaf of a line the tuplet groups that hold it."""
    stack: list[_Group] = []
    for leaf in mains:
        for number, actual, normal in leaf.starts:
            stack.append(_Group(number, actual, normal, leaf))
        leaf.path = tuple(stack)
        _derive_ratios(leaf)
        if not leaf.stops:
            continue
        closing: list[int] = []
        for number in leaf.stops:
            index = next(
                (
                    i
                    for i in range(len(stack) - 1, -1, -1)
                    if stack[i].number == number and i not in closing
                ),
                None,
            )
            if index is None:
                msg = f"tuplet {number} stops without a start"
                _malformed("tuplet", msg, leaf.at)
            closing.append(index)
        if sorted(closing) != list(
            range(len(stack) - len(closing), len(stack))
        ):
            msg = "tuplet groups that cross each other"
            _unsupported("tuplet", msg, leaf.at)
        del stack[len(stack) - len(closing) :]
    if stack:
        at = mains[-1].at if mains else None
        msg = "a tuplet group stays open at the barline"
        _unsupported("tuplet_open", msg, at)


def _derive_ratios(leaf: _Leaf) -> None:
    """Give a group without printed numbers the ratio of its first leaf."""
    unknown = [group for group in leaf.path if group.actual is None]
    if not unknown:
        return
    if len(unknown) > 1:
        msg = "nested tuplet groups start without their numbers"
        _unsupported("tuplet_ratio", msg, leaf.at)
    ratio = leaf.modification / (2 if leaf.pair else 1)
    for group in leaf.path:
        if group.actual is not None:
            ratio /= group.ratio
    if ratio == 1:
        msg = "a tuplet group without a time modification"
        _malformed("tuplet", msg, leaf.at)
    unknown[0].actual, unknown[0].normal = ratio.numerator, ratio.denominator


def _check_duration(leaf: _Leaf) -> None:
    if leaf.written is None:
        return
    value = leaf.written.value
    for group in leaf.path:
        value /= group.ratio
    if leaf.pair is not None:
        value /= 2
    if value != leaf.duration:
        msg = "the duration does not match the note value and tuplet ratio"
        _malformed("duration", msg, leaf.at)


# Relations.


class _Matcher:
    """Pairs relation endpoints and gives each relation a temporary label."""

    def __init__(self, measures: list[_Measure]) -> None:
        self.measures = measures
        self.leaves = [
            leaf
            for measure in measures
            for leaves in measure.lines.values()
            for leaf in leaves
        ]
        self.leaves.sort(key=self.key)
        self.labels = 0
        self.spans: list[tuple[int, Fraction, Fraction, int]] = []

    def time(self, leaf: _Leaf) -> Fraction:
        return self.measures[leaf.measure].start + leaf.onset

    def key(self, leaf: _Leaf) -> tuple[Fraction, int, int, int]:
        rank = 0 if leaf.grace is not None else 1
        rank = 1 if leaf.kind == _DIRECTION else rank * 2
        return (self.time(leaf), leaf.measure, rank, leaf.order)

    def label(self) -> int:
        self.labels += 1
        return self.labels

    def match(self) -> None:
        self._numbered()
        self._ties()
        self._tremolos()
        self._arpeggios()

    def _endpoints(self) -> list[tuple[int, int, _Leaf, str, str, str]]:
        endpoints: list[tuple[int, int, _Leaf, str, str, str]] = []
        for position, leaf in enumerate(self.leaves):
            marks = [("slur", kind, number) for kind, number in leaf.slurs]
            marks.extend(
                ("wavy-line", kind, number) for kind, number in leaf.wavy
            )
            if leaf.endpoint is not None:
                marks.append(leaf.endpoint)
            for family, role, number in marks:
                endpoints.append(
                    (
                        position,
                        1 if role == _START else 0,
                        leaf,
                        family,
                        role,
                        number,
                    )
                )
        endpoints.sort(key=lambda item: (item[0], item[1]))
        return endpoints

    def _numbered(self) -> None:
        open_starts: dict[tuple[str, str], tuple[_Leaf, int]] = {}
        # A wavy line over its own trilled note alone adds nothing.
        single: set[tuple[int, str, str]] = set()
        for position, _, leaf, family, role, number in self._endpoints():
            key = (family, number)
            if role == _START:
                if (position, family, number) in single:
                    continue
                if key in open_starts:
                    msg = f"two {family} spanners share number {number}"
                    _unsupported("spanner_number", msg, leaf.at)
                label = self.label()
                open_starts[key] = (leaf, label)
                self._start(leaf, family, label)
                continue
            if key not in open_starts and _single_wavy(leaf, family, number):
                single.add((position, family, number))
                continue
            if key not in open_starts:
                msg = f"a {family} {role} has no start"
                _unsupported(family, msg, leaf.at)
            start, label = open_starts[key]
            if role != _CHANGE:
                del open_starts[key]
            self._end(start, leaf, family, label)
        for (family, _), (leaf, _) in open_starts.items():
            msg = f"a {family} spanner never stops"
            _unsupported(family, msg, leaf.at)

    def _start(self, leaf: _Leaf, family: str, label: int) -> None:
        if family == "slur":
            if leaf.kind != _NOTE:
                _unsupported("slur", "a slur on a rest", leaf.at)
            leaf.relations.append(SlurRelation(label, EndpointRole.START))
        elif family == "wavy-line":
            if Ornament(OrnamentKind.TRILL) not in leaf.flags:
                msg = "a wavy line that no trill sign starts"
                _unsupported("wavy_line", msg, leaf.at)
            leaf.relations.append(
                ExtendedTrillRelation(label, EndpointRole.START),
            )
        else:
            leaf.label = label

    def _end(self, start: _Leaf, leaf: _Leaf, family: str, label: int) -> None:
        if family == "slur":
            if leaf.kind != _NOTE:
                _unsupported("slur", "a slur on a rest", leaf.at)
            leaf.relations.append(SlurRelation(label, EndpointRole.END))
        elif family == "wavy-line":
            leaf.relations.append(
                ExtendedTrillRelation(label, EndpointRole.END),
            )
        else:
            leaf.label = label
            if family == "octave-shift":
                self._ottava(start, leaf)

    def _ottava(self, start: _Leaf, end: _Leaf) -> None:
        assert isinstance(start.event, OttavaStart)  # noqa: S101
        if start.staff != end.staff:
            msg = "an octave shift stops on the other staff"
            _unsupported("octave_shift", msg, end.at)
        sign = start.event.sign
        end.event = OttavaEnd(sign, 0, sign)
        self.spans.append(
            (
                start.staff,
                self.time(start),
                self.time(end),
                OTTAVA_SHIFTS[sign],
            ),
        )

    def _ties(self) -> None:
        stops = [
            (leaf, member)
            for leaf in self.leaves
            if leaf.kind == _NOTE
            for member in leaf.members
            if member.tie_stop
        ]
        for leaf in self.leaves:
            if leaf.kind != _NOTE:
                continue
            for member in leaf.members:
                if member.tie_start:
                    self._tie(leaf, member, stops)
        for _, member in stops:
            member.tie_in = True

    def _tie(
        self,
        leaf: _Leaf,
        member: _Member,
        stops: list[tuple[_Leaf, _Member]],
    ) -> None:
        end = self.time(leaf) + leaf.duration
        candidates = [
            (other, target)
            for other, target in stops
            if other.grace is None
            and other.staff == leaf.staff
            and self.time(other) == end
            and target.sounding == member.sounding
            and (
                None in {member.start_number, target.stop_number}
                or target.stop_number == member.start_number
            )
        ]
        if not candidates:
            msg = "a tie that reaches no note of the same pitch"
            _unsupported("tie", msg, member.at)
        candidates.sort(
            key=lambda item: (
                item[1].stop_number != member.start_number,
                (item[0].staff, item[0].voice) != (leaf.staff, leaf.voice),
                item[0].order,
            ),
        )
        other, target = candidates[0]
        stops.remove((other, target))
        label = self.label()
        member.start_label = label
        target.end_label = label

    def _tremolos(self) -> None:
        pending: dict[tuple[int, int], _Leaf] = {}
        for leaf in self.leaves:
            if leaf.pair is None:
                continue
            kind, strokes = leaf.pair
            line = (leaf.staff, leaf.voice)
            if kind == _START and line not in pending:
                pending[line] = leaf
                continue
            start = pending.pop(line, None)
            if (
                kind != _STOP
                or start is None
                or start.pair != (_START, strokes)
            ):
                msg = "the two notes of a tremolo do not pair"
                _unsupported("tremolo", msg, leaf.at)
            label = self.label()
            start.relations.append(
                TremoloRelation(label, TremoloRole.TWO_START, strokes),
            )
            leaf.relations.append(
                TremoloRelation(label, TremoloRole.TWO_END, strokes),
            )
        for leaf in pending.values():
            _unsupported(
                "tremolo", "a tremolo without its second note", leaf.at
            )

    def _arpeggios(self) -> None:
        groups: dict[tuple[str, Fraction], list[_Leaf]] = {}
        for leaf in self.leaves:
            for number in leaf.arpeggio:
                if number:
                    groups.setdefault((number, self.time(leaf)), []).append(
                        leaf
                    )
                else:
                    _add(leaf.flags, Arpeggiate())
        for members in groups.values():
            if len(members) == 1:
                _add(members[0].flags, Arpeggiate())
                continue
            label = self.label()
            for leaf in members:
                leaf.relations.append(ArpeggioRelation(label))

    def shift(self, leaf: _Leaf) -> int:
        time = self.time(leaf)
        return next(
            (
                shift
                for staff, start, end, shift in self.spans
                if staff == leaf.shown and start <= time < end
            ),
            0,
        )


# Building and checking the score.


type _Expected = dict[tuple[int, int, int, int], list[tuple[str, int, int]]]


def _relabeled[T](item: T, labels: dict[int, int]) -> T:
    label = getattr(item, "label", None)
    if isinstance(label, int) and label in labels:
        return replace(item, label=labels[label])  # type: ignore[arg-type]
    return item


@dataclass(eq=False, slots=True)
class _Open:
    """A tuplet group whose items are being collected."""

    group: _Group
    items: list[TupletItem | _Open] = field(default_factory=list)

    def freeze(self) -> Tuplet:
        assert self.group.actual is not None  # noqa: S101
        assert self.group.normal is not None  # noqa: S101
        return Tuplet(
            self.group.actual,
            self.group.normal,
            tuple(
                item.freeze() if isinstance(item, _Open) else item
                for item in self.items
            ),
        )


@dataclass(eq=False, slots=True)
class _Score:
    """The read document, ready to become an assembled score."""

    title: str | None
    composer: str | None
    headings: list[str]
    key: int
    meter: Meter
    clefs: tuple[Clef, Clef]
    measures: list[_Measure]
    matcher: _Matcher | None = None

    def convert(self) -> AssembledScore:
        self.matcher = _Matcher(self.measures)
        self.matcher.match()
        failure: tuple[MusicXmlDiagnostic, ...] | None = None
        for carry in (CarryMode.OCTAVE, CarryMode.LETTER):
            result = self._attempt(carry)
            if isinstance(result, AssembledScore):
                return result
            failure = failure or result
        assert failure is not None  # noqa: S101
        raise MusicXmlError(failure)

    def _attempt(
        self, carry: CarryMode
    ) -> AssembledScore | tuple[MusicXmlDiagnostic, ...]:
        labels = {label: label for label in range(1, self._labels() + 1)}
        for _ in range(_RELABEL_ROUNDS):
            document, expected = self.build(carry, labels)
            order = _label_order(document)
            if all(order.get(label, label) == label for label in order):
                break
            labels = {
                temporary: order.get(label, label)
                for temporary, label in labels.items()
            }
        text = write_document(document)
        try:
            parsed = parse_document(decode_source(text.encode("utf-8")))
        except TextScoreError as error:
            return self._located(error.diagnostics, text)
        assert isinstance(parsed, AssembledScore)  # noqa: S101
        diagnostics = validate_document(parsed)
        if diagnostics:
            return self._located(diagnostics, text)
        mismatch = _pitch_mismatch(parsed, expected)
        if mismatch is not None:
            return (mismatch,)
        return parsed

    def _labels(self) -> int:
        assert self.matcher is not None  # noqa: S101
        return self.matcher.labels

    def build(
        self, carry: CarryMode, labels: dict[int, int]
    ) -> tuple[AssembledScore, _Expected]:
        expected: _Expected = {}
        blocks = []
        for measure in self.measures:
            voices = []
            for key in sorted(measure.lines):
                events = self._line(measure, key, labels, expected)
                voices.append(VoiceLine(key[0], key[1], events))
            block = MeasureBlock(
                measure.index + 1, tuple(measure.options), tuple(voices)
            )
            headings = self.headings if measure.index == 0 else []
            blocks.append(
                HeadedBlock(tuple(Heading(text) for text in headings), block),
            )
        document = AssembledScore(
            self.title,
            self.composer,
            self.key,
            self.meter,
            self.clefs,
            carry,
            tuple(blocks),
        )
        return document, expected

    def _line(
        self,
        measure: _Measure,
        key: tuple[int, int],
        labels: dict[int, int],
        expected: _Expected,
    ) -> tuple[Event, ...]:
        built = [
            (leaf, self._event(leaf, labels)) for leaf in measure.lines[key]
        ]
        built.sort(key=lambda item: _slot(item[0], item[1]))
        paths: list[tuple[_Group, ...]] = []
        following: _Leaf | None = None
        for leaf, _ in reversed(built):
            if leaf.is_main:
                following = leaf
                paths.append(leaf.path)
            elif following is None:
                paths.append(())
            else:
                paths.append(
                    tuple(
                        g for g in following.path if g.first is not following
                    ),
                )
        paths.reverse()
        root: list[Event | _Open] = []
        stack: list[_Open] = []
        for position, ((leaf, event), path) in enumerate(
            zip(built, paths, strict=True),
        ):
            depth = 0
            while (
                depth < len(path)
                and depth < len(stack)
                and stack[depth].group is path[depth]
            ):
                depth += 1
            del stack[depth:]
            for group in path[depth:]:
                opened = _Open(group)
                (stack[-1].items if stack else root).append(opened)
                stack.append(opened)
            if stack:
                assert not isinstance(event, MeasureRest | TimedReview)  # noqa: S101
                stack[-1].items.append(event)
            else:
                root.append(event)
            if leaf.kind == _NOTE:
                expected[(measure.index, key[0], key[1], position)] = [
                    member.sounding for member in _ordered(leaf)
                ]
        return tuple(
            item.freeze() if isinstance(item, _Open) else item for item in root
        )

    def _event(self, leaf: _Leaf, labels: dict[int, int]) -> Event:
        if leaf.kind == _DIRECTION:
            assert leaf.event is not None  # noqa: S101
            if leaf.label is None:
                return leaf.event
            return replace(leaf.event, label=labels[leaf.label])  # type: ignore[arg-type]
        assert leaf.written is not None or leaf.kind == _MREST  # noqa: S101
        fermata = next(
            (flag for flag in leaf.flags if isinstance(flag, Fermata)), None
        )
        beams = tuple(flag for flag in leaf.flags if isinstance(flag, Beam))
        if leaf.kind == _MREST:
            return MeasureRest(fermata)
        assert leaf.written is not None  # noqa: S101
        if leaf.kind == _SPACER:
            return Spacer(leaf.written, beams)
        if leaf.kind == _REST:
            rest_flags = (*beams, *(() if fermata is None else (fermata,)))
            return Rest(leaf.written, rest_flags)
        flags = list(leaf.flags)
        if leaf.grace is not None:
            flags.append(leaf.grace)
        if leaf.shown != leaf.staff:
            flags.append(CrossStaff(leaf.shown))
        flags.extend(_relabeled(flag, labels) for flag in leaf.relations)
        assert self.matcher is not None  # noqa: S101
        shift = self.matcher.shift(leaf)
        pitches = tuple(
            Pitch(
                member.step,
                member.accidental,
                member.octave - shift,
                member.parenthesized,
                tuple(member.fingering),
                member.tie_in,
                None if member.end_label is None else labels[member.end_label],
                None
                if member.start_label is None
                else labels[member.start_label],
            )
            for member in _ordered(leaf)
        )
        return Note(pitches, leaf.written, tuple(flags))

    def _located(
        self, diagnostics: Iterable[Diagnostic], text: str
    ) -> tuple[MusicXmlDiagnostic, ...]:
        places: list[tuple[int, int | None]] = []
        block, staff = -1, None
        for row in text.split("\n"):
            if row.startswith("m "):
                block, staff = block + 1, None
            else:
                prefix = _LINE_PREFIX.match(row)
                if prefix is not None:
                    staff = int(prefix.group(1))
            places.append((block, staff))
        located = []
        for diagnostic in diagnostics:
            block, staff = places[diagnostic.location.line - 1]
            number = self.measures[block].number if block >= 0 else None
            located.append(
                MusicXmlDiagnostic(
                    diagnostic.rule_class,
                    diagnostic.rule,
                    diagnostic.message,
                    number,
                    staff,
                ),
            )
        return tuple(located)


def _ordered(leaf: _Leaf) -> list[_Member]:
    return sorted(leaf.members, key=lambda member: member.order)


def _slot(leaf: _Leaf, event: Event) -> tuple[Fraction, int, str, int]:
    """Order a line: grace notes, then directions, then the main event."""
    if leaf.kind != _DIRECTION:
        return (leaf.onset, 0 if leaf.grace is not None else 2, "", leaf.order)
    label = getattr(event, "label", 0)
    spelling = write_event(_relabeled(event, {label: 0}))
    return (leaf.onset, 1, spelling, label)


def _label_order(document: AssembledScore) -> dict[int, int]:
    """Return the labels that the relation numbering rule gives."""
    analysis = analyze_relations(document, build_timeline(document))
    ordered = sorted(
        analysis.relations.values(),
        key=lambda relation: (_new_order(relation), relation.label),
    )
    return {relation.label: n for n, relation in enumerate(ordered, 1)}


def _new_order(relation: Relation) -> tuple[object, ...]:
    """Return the numbering key of the format's relation label rule."""
    ending: tuple[object, ...] = (
        (1,) if relation.is_open else (0, relation.last_key)
    )
    variant = next((p.variant for p in relation.points if p.variant), "")
    return (relation.first_key, relation.kind, ending, variant)


def _pitch_mismatch(
    document: AssembledScore, expected: _Expected
) -> MusicXmlDiagnostic | None:
    """Check that every written pitch sounds as the MusicXML pitch."""
    timeline = build_timeline(document)
    relations = analyze_relations(document, timeline)
    resolved = resolve_pitches(document, timeline, relations).resolved
    for placed in timeline.events():
        if not isinstance(placed.event, Note):
            continue
        place = (
            placed.frame.index,
            placed.staff,
            placed.voice,
            placed.position,
        )
        for member, sounding in enumerate(expected[place], 1):
            found = resolved.get((placed.ident, member))
            if found is None or (found.step, found.octave, found.alter) != (
                sounding
            ):
                msg = (
                    "the printed accidentals do not imply the MusicXML"
                    f" pitch {sounding[0]}{sounding[2]:+d} octave {sounding[1]}"
                    " under either accidental carry rule"
                )
                return MusicXmlDiagnostic(
                    RuleClass.UNSUPPORTED,
                    "pitch_alteration",
                    msg,
                    None,
                    placed.staff,
                )
    return None
