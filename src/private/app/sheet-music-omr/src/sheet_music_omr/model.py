"""Typed, immutable syntax tree of Text Score 2 documents.

The tree records every distinction that the grammar spells, including layout
hints such as stem directions, placements, inverted fermatas and dynamics
printed above the staff, so that a writer can re-emit a document and later
tools can validate, assemble and convert it.

The tree is syntactic. It keeps items in their written order, including
repeated or misordered items that the semantic rules reject, so that a
validator can report them. Rationals are kept as reduced ``Fraction`` values
because the lexical rules already reject unreduced spellings.

Every node has an optional ``location`` of its first character in the
original text. Locations do not take part in equality, so two trees compare
equal when they spell the same document.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from enum import StrEnum
from fractions import Fraction
from typing import TYPE_CHECKING, Literal

if TYPE_CHECKING:
    from sheet_music_omr.diagnostics import Location


# Enumerations. Each value is the spelling in the format.


class CarryMode(StrEnum):
    """How a printed accidental stays in force within a measure."""

    OCTAVE = "octave"
    LETTER = "letter"


class ClefSign(StrEnum):
    """The sign of a clef."""

    G = "G"
    F = "F"
    C = "C"


class TimeSymbol(StrEnum):
    """The printed form of a time signature."""

    NUMERIC = "numeric"
    COMMON = "common"
    CUT = "cut"


class Barline(StrEnum):
    """A closing barline written with ``right=``."""

    FINAL = "final"
    DOUBLE = "double"
    REPEAT = "repeat"


class EndingKind(StrEnum):
    """The part of a volta bracket that a measure carries."""

    START = "start"
    SINGLE = "single"
    CONTINUE = "continue"
    END = "end"


class AccidentalKind(StrEnum):
    """A printed accidental sign."""

    DOUBLE_SHARP = "##"
    DOUBLE_FLAT = "bb"
    SHARP = "#"
    FLAT = "b"
    NATURAL = "n"

    @property
    def alter(self) -> int:
        """Return the chromatic alteration in semitones."""
        return _ALTERS[self]


_ALTERS = {
    AccidentalKind.DOUBLE_SHARP: 2,
    AccidentalKind.DOUBLE_FLAT: -2,
    AccidentalKind.SHARP: 1,
    AccidentalKind.FLAT: -1,
    AccidentalKind.NATURAL: 0,
}


class ArticulationKind(StrEnum):
    """An articulation mark."""

    STACCATO = "stacc"
    ACCENT = "acc"
    TENUTO = "ten"
    STACCATISSIMO = "stacciss"
    MARCATO = "marc"
    PORTATO = "port"


class Placement(StrEnum):
    """A placement suffix, which is a layout hint."""

    ABOVE = "^"
    BELOW = "_"


class StemDirection(StrEnum):
    """A stem direction flag, which is a layout hint."""

    UP = "up"
    DOWN = "down"


class BeamState(StrEnum):
    """The state of one beam level on one event."""

    START = "start"
    CONTINUE = "continue"
    END = "end"
    HOOK_FORWARD = "hook-forward"
    HOOK_BACKWARD = "hook-backward"


class OrnamentKind(StrEnum):
    """An ornament written with ``orn=``."""

    TRILL = "trill"
    MORDENT = "mordent"
    INVERTED_MORDENT = "inverted-mordent"
    TURN = "turn"
    INVERTED_TURN = "inverted-turn"


class EndpointRole(StrEnum):
    """Whether a relation endpoint starts or ends its relation."""

    START = "start"
    END = "end"


class TremoloRole(StrEnum):
    """The endpoint of a two-note tremolo."""

    TWO_START = "two-start"
    TWO_END = "two-end"


class DynamicMark(StrEnum):
    """A dynamic direction."""

    PPPP = "pppp"
    PPP = "ppp"
    PP = "pp"
    P = "p"
    MP = "mp"
    MF = "mf"
    F = "f"
    FF = "ff"
    FFF = "fff"
    FFFF = "ffff"
    SF = "sf"
    SFZ = "sfz"
    SFFZ = "sffz"
    FP = "fp"
    RF = "rf"
    RFZ = "rfz"
    FZ = "fz"
    SFP = "sfp"
    SFPP = "sfpp"
    PF = "pf"


class HairpinKind(StrEnum):
    """The variant of a hairpin."""

    CRESC = "cresc"
    DIM = "dim"


class OttavaSign(StrEnum):
    """The sign of an ottava line."""

    OTTAVA_ALTA = "8va"
    OTTAVA_BASSA = "8vb"
    QUINDICESIMA_ALTA = "15ma"
    QUINDICESIMA_BASSA = "15mb"


class PedalAction(StrEnum):
    """An endpoint of a pedal line."""

    LINE_START = "line-start"
    LINE_CHANGE = "line-change"
    LINE_RELEASE = "line-release"


class PedalSignKind(StrEnum):
    """A pedal glyph that is not joined to a line."""

    DOWN = "down"
    UP = "up"


class MetronomeUnit(StrEnum):
    """The beat unit of a metronome mark."""

    DOTTED_QUARTER = "q."
    DOTTED_HALF = "h."
    QUARTER = "q"
    HALF = "h"
    EIGHTH = "e"


class RelationKind(StrEnum):
    """The kind of a relation listed in a carry state."""

    ARC = "arc"
    DASHES = "dashes"
    HAIRPIN = "hairpin"
    ORNAMENT = "ornament"
    OTTAVA = "ottava"
    PEDAL = "pedal"
    SLUR = "slur"
    TIE = "tie"
    TREMOLO = "tremolo"


# Shared values.


@dataclass(frozen=True, slots=True)
class Clef:
    """A clef such as ``G2`` or ``G2-8``.

    Attributes:
        sign: The clef sign.
        line: The staff line, 1 to 5.
        octave_change: ``1`` for ``+8``, ``-1`` for ``-8``, otherwise ``0``.
    """

    sign: ClefSign
    line: int
    octave_change: int = 0
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class Meter:
    """A meter ``N/D``, which is not reduced."""

    numerator: int
    denominator: int
    location: Location | None = field(default=None, compare=False, kw_only=True)

    @property
    def length(self) -> Fraction:
        """Return the measure length of the meter in whole notes."""
        return Fraction(self.numerator, self.denominator)


@dataclass(frozen=True, slots=True)
class Duration:
    """A written duration ``N`` followed by ``dots`` dots."""

    base: int
    dots: int = 0
    location: Location | None = field(default=None, compare=False, kw_only=True)

    @property
    def value(self) -> Fraction:
        """Return the written value in whole notes, before tuplet ratios."""
        return Fraction(1, self.base) * (2 - Fraction(1, 2**self.dots))


# Pitches.


@dataclass(frozen=True, slots=True)
class Accidental:
    """A printed accidental, possibly in parentheses."""

    kind: AccidentalKind
    parenthesized: bool = False
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class Pitch:
    """A written pitch with its fingering and tie marks.

    Attributes:
        step: The step letter, ``A`` to ``G``.
        accidental: The printed accidental, if any.
        octave: The written octave, 0 to 9.
        parenthesized: Whether the notehead is in parentheses.
        fingering: The fingering digits, none, one or two.
        tie_in: Whether the pitch carries ``tie-in``.
        tie_end: The label number of a tie that ends on the pitch.
        tie_start: The label number of a tie that starts on the pitch.
        arc_starts: The label numbers of arcs that start on the pitch.
    """

    step: str
    accidental: Accidental | None
    octave: int
    parenthesized: bool = False
    fingering: tuple[int, ...] = ()
    tie_in: bool = False
    tie_end: int | None = None
    tie_start: int | None = None
    arc_starts: tuple[int, ...] = ()
    location: Location | None = field(default=None, compare=False, kw_only=True)

    @property
    def has_tie_marks(self) -> bool:
        """Return whether the pitch writes a ``{...}`` tie mark group."""
        return (
            self.tie_in
            or self.tie_end is not None
            or self.tie_start is not None
            or bool(self.arc_starts)
        )


# Event flags.


@dataclass(frozen=True, slots=True)
class Articulation:
    """An articulation with an optional placement hint."""

    kind: ArticulationKind
    placement: Placement | None = None
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class Fermata:
    """A fermata, ``ferm``, or an inverted fermata, ``ferm_``."""

    inverted: bool = False
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class Arpeggiate:
    """The ``arp`` flag for a rolled chord within one event."""

    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class Stem:
    """A stem direction flag."""

    direction: StemDirection
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class Grace:
    """A grace flag, ``grace`` or the slashed ``grace/``."""

    slashed: bool = False
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class CrossStaff:
    """The ``xsN`` flag that displays the event on staff ``N``."""

    staff: int
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class Beam:
    """A beam state at one level."""

    level: int
    state: BeamState
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class Ornament:
    """An ornament flag."""

    kind: OrnamentKind
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class SingleTremolo:
    """A single-note tremolo with a stroke count."""

    strokes: int
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class SlurRelation:
    """A slur endpoint, ``rel=slur@rN:start|end``."""

    label: int
    role: EndpointRole
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class TremoloRelation:
    """A two-note tremolo endpoint."""

    label: int
    role: TremoloRole
    strokes: int
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class ArpeggioRelation:
    """An arpeggio member, ``rel=arpeggio@rN:member``."""

    label: int
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class ExtendedTrillRelation:
    """An extended-trill endpoint on a note."""

    label: int
    role: EndpointRole
    location: Location | None = field(default=None, compare=False, kw_only=True)


type EventRelation = (
    SlurRelation | TremoloRelation | ArpeggioRelation | ExtendedTrillRelation
)
type NoteFlag = (
    Articulation
    | Fermata
    | Arpeggiate
    | Stem
    | Grace
    | CrossStaff
    | Beam
    | Ornament
    | SingleTremolo
    | EventRelation
)
type RestFlag = Fermata | Beam


# Rhythmic events.


@dataclass(frozen=True, slots=True)
class Note:
    """A note or chord, with its flags in written order."""

    pitches: tuple[Pitch, ...]
    duration: Duration
    flags: tuple[NoteFlag, ...] = ()
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class Rest:
    """A printed rest ``r/``."""

    duration: Duration
    flags: tuple[RestFlag, ...] = ()
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class Spacer:
    """An unprinted timed spacer ``x/``."""

    duration: Duration
    beams: tuple[Beam, ...] = ()
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class MeasureRest:
    """A whole-measure rest ``mrest``."""

    fermata: Fermata | None = None
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class TimedReview:
    """A timed review marker ``?(R)`` with its candidates."""

    duration: Fraction
    candidates: tuple[str, ...] = ()
    location: Location | None = field(default=None, compare=False, kw_only=True)


# Directions.


@dataclass(frozen=True, slots=True)
class Dynamic:
    """A dynamic, with ``above`` for the ``^`` layout hint."""

    mark: DynamicMark
    above: bool = False
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class ClefChange:
    """A ``<clef X>`` direction."""

    clef: Clef
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class Words:
    """Expression text ``<"text">``."""

    text: str
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class Metronome:
    """A metronome mark such as ``q=120``."""

    unit: MetronomeUnit
    per_minute: int
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class Tempo:
    """A tempo direction; the text may be empty only with a metronome."""

    text: str
    metronome: Metronome | None = None
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class Rehearsal:
    """A rehearsal mark."""

    text: str
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class PedalSign:
    """A pedal glyph that is not joined to a line."""

    kind: PedalSignKind
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class UntimedReview:
    """An untimed review marker ``<?>`` with its candidates."""

    candidates: tuple[str, ...] = ()
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class HairpinStart:
    """The start of a hairpin."""

    kind: HairpinKind
    label: int
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class HairpinEnd:
    """The end of a hairpin."""

    label: int
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class OttavaStart:
    """The start of an ottava line; ``variant`` is the relation variant."""

    sign: OttavaSign
    label: int
    variant: OttavaSign
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class OttavaEnd:
    """The end of an ottava line; ``variant`` is the relation variant."""

    sign: OttavaSign
    label: int
    variant: OttavaSign
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class PedalLine:
    """A start, change or release of a pedal line."""

    label: int
    action: PedalAction
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class DashesStart:
    """The start of a dashes line, which records its text."""

    text: str
    label: int
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class DashesEnd:
    """The end of a dashes line."""

    label: int
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class TrillEnd:
    """``<trill)>``, which ends a carried extended trill without a note."""

    label: int
    location: Location | None = field(default=None, compare=False, kw_only=True)


type Direction = (
    Dynamic
    | ClefChange
    | Words
    | Tempo
    | Rehearsal
    | PedalSign
    | UntimedReview
    | HairpinStart
    | HairpinEnd
    | OttavaStart
    | OttavaEnd
    | PedalLine
    | DashesStart
    | DashesEnd
    | TrillEnd
)


@dataclass(frozen=True, slots=True)
class Tuplet:
    """A tuplet ``actual:normal{...}``: ``actual`` notes in ``normal``."""

    actual: int
    normal: int
    items: tuple[TupletItem, ...]
    location: Location | None = field(default=None, compare=False, kw_only=True)


type TupletItem = Direction | Tuplet | Note | Rest | Spacer
type Event = (
    Direction | Tuplet | Note | Rest | Spacer | MeasureRest | TimedReview
)


# Measure blocks.


@dataclass(frozen=True, slots=True)
class LenOption:
    """``len=R``."""

    value: Fraction
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class KeyOption:
    """``key=K``."""

    fifths: int
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class TimeOption:
    """``time=N/D``."""

    meter: Meter
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class TimeSymbolOption:
    """``time-symbol=...``."""

    symbol: TimeSymbol
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class LeftRepeatOption:
    """``left=repeat``."""

    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class RightOption:
    """``right=...``."""

    barline: Barline
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class FromOption:
    """``from=R``."""

    value: Fraction
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class ToOption:
    """``to=R``."""

    value: Fraction
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class EndingOption:
    """``ending=...``; ``number`` is set for ``start`` and ``single``."""

    kind: EndingKind
    number: int | None = None
    location: Location | None = field(default=None, compare=False, kw_only=True)


type MeasureOption = (
    LenOption
    | KeyOption
    | TimeOption
    | TimeSymbolOption
    | LeftRepeatOption
    | RightOption
    | FromOption
    | ToOption
    | EndingOption
)


@dataclass(frozen=True, slots=True)
class VoiceLine:
    """The events of staff ``staff`` and voice ``voice`` in one block."""

    staff: int
    voice: int
    events: tuple[Event, ...]
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class MeasureBlock:
    """A measure block with its options and voice lines in written order."""

    number: int
    options: tuple[MeasureOption, ...]
    voices: tuple[VoiceLine, ...]
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class Heading:
    """One ``heading`` line."""

    text: str
    location: Location | None = field(default=None, compare=False, kw_only=True)


# Carry state.


@dataclass(frozen=True, slots=True)
class EventCount:
    """An ``event_counts`` entry."""

    staff: int
    voice: int
    count: int
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class CarriedAccidental:
    """An ``accidentals`` entry; ``octave`` is absent in letter scope."""

    staff: int
    step: str
    octave: int | None
    alter: int
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class Endpoint:
    """The ``start`` endpoint of a carried relation."""

    measure: int
    offset: Fraction
    staff: int
    voice: int
    event_index: int
    member_index: int | None
    location: Location | None = field(default=None, compare=False, kw_only=True)


type TieIn = Literal["tie-in"]


@dataclass(frozen=True, slots=True)
class SoundingPitch:
    """The sounding pitch of a carried tie or arc.

    ``alter`` is ``"tie-in"`` when the alteration is deferred.
    """

    step: str
    octave: int
    alter: int | TieIn
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class OpenRelation:
    """An ``open`` entry of a carry state.

    Attributes:
        label: The label number.
        kind: The relation kind.
        start: The start endpoint.
        pitch: The sounding pitch, for ties and arcs only.
        variant: The variant spelling, for tremolos (stroke count),
            ornaments, hairpins and ottavas only.
        written: The written duration of a tremolo's first note.
        scale: The tuplet multiplier of a tremolo.
    """

    label: int
    kind: RelationKind
    start: Endpoint
    pitch: SoundingPitch | None = None
    variant: str | None = None
    written: Fraction | None = None
    scale: Fraction | None = None
    location: Location | None = field(default=None, compare=False, kw_only=True)


@dataclass(frozen=True, slots=True)
class CarryState:
    """A ``carry-in`` state, with entries in written order."""

    measure: int
    offset: Fraction
    event_counts: tuple[EventCount, ...]
    accidental_scope: CarryMode
    accidentals: tuple[CarriedAccidental, ...]
    open: tuple[OpenRelation, ...]
    location: Location | None = field(default=None, compare=False, kw_only=True)


# Documents.


@dataclass(frozen=True, slots=True)
class SystemDocument:
    """A system document: one printed grand-staff system."""

    key: int
    time: Meter
    clefs: tuple[Clef, Clef]
    carry: CarryMode
    page: int
    system: int
    carry_in: CarryState
    headings: tuple[Heading, ...]
    blocks: tuple[MeasureBlock, ...]
    location: Location | None = field(default=None, compare=False, kw_only=True)
    key_location: Location | None = field(
        default=None, compare=False, kw_only=True
    )
    page_location: Location | None = field(
        default=None, compare=False, kw_only=True
    )


@dataclass(frozen=True, slots=True)
class HeadedBlock:
    """A measure block of an assembled score with the headings before it."""

    headings: tuple[Heading, ...]
    block: MeasureBlock


@dataclass(frozen=True, slots=True)
class AssembledScore:
    """An assembled score: a whole piece of complete measures."""

    title: str | None
    composer: str | None
    key: int
    time: Meter
    clefs: tuple[Clef, Clef]
    carry: CarryMode
    blocks: tuple[HeadedBlock, ...]
    location: Location | None = field(default=None, compare=False, kw_only=True)


type Document = SystemDocument | AssembledScore
