"""Canonical writer for Text Score 2 documents.

``write_document`` writes the canonical spelling of a syntax tree, as the
"Canonical form" section of the format defines it. The writer applies these
rules itself:

- Header lines follow the grammar order and heading lines keep their reading
    order. Voice lines are sorted by staff and then voice. Events and the
    candidates of a review marker keep their tree order.
- Items are separated by single spaces and lines end with LF, with no
    comments, blank lines or trailing spaces.
- Block options follow the order ``len``, ``key``, ``time``, ``time-symbol``,
    ``left``, ``right``, ``from``, ``to``, ``ending``. The writer omits a
    ``len`` equal to the length of the meter in effect, a reprint of ``key``
    or ``time``, ``time-symbol=numeric``, ``from=0`` and a ``to`` equal to the
    effective length. The key and meter in effect follow the header and the
    ``key`` and ``time`` options of earlier blocks, so no onset is needed.
- Event flags are ordered by class, beams by level and relations by kind and
    then by label.
- Rationals are reduced, text uses the canonical escapes and a carry state
    has the grammar spelling.

Block id order, chord member order, relation label numbering and the entry
order of a carry state are validity rules, which the semantic validator checks
rather than the writer applies. Block ids are positional, while chord member
indices, relation labels and carry entries are referenced by the carry state
of other documents, so respelling them would change content. The writer keeps
these orders as the tree has them. The order of directions at one onset is a
transcription rule, which is neither validated nor reordered. A tool validates
a document before it writes canonical form, so the writer assumes a valid tree
and repairs nothing.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import TYPE_CHECKING, Any

from sheet_music_omr.lexical import format_rational, quote_text
from sheet_music_omr.model import (
    Arpeggiate,
    ArpeggioRelation,
    Articulation,
    AssembledScore,
    Beam,
    CarryState,
    ClefChange,
    CrossStaff,
    DashesEnd,
    DashesStart,
    Dynamic,
    EndingKind,
    EndingOption,
    ExtendedTrillRelation,
    Fermata,
    FromOption,
    Grace,
    HairpinEnd,
    HairpinStart,
    KeyOption,
    LeftRepeatOption,
    LenOption,
    MeasureRest,
    Note,
    Ornament,
    OttavaEnd,
    OttavaStart,
    PedalAction,
    PedalLine,
    PedalSign,
    Rehearsal,
    Rest,
    RightOption,
    SingleTremolo,
    SlurRelation,
    Spacer,
    Stem,
    SystemDocument,
    Tempo,
    TimedReview,
    TimeOption,
    TimeSymbol,
    TimeSymbolOption,
    ToOption,
    TremoloRelation,
    TrillEnd,
    Tuplet,
    UntimedReview,
    Words,
)

if TYPE_CHECKING:
    from collections.abc import Callable, Iterable
    from fractions import Fraction

    from sheet_music_omr.model import (
        CarriedAccidental,
        Clef,
        Document,
        Duration,
        Endpoint,
        Event,
        EventCount,
        Heading,
        MeasureBlock,
        MeasureOption,
        Meter,
        NoteFlag,
        OpenRelation,
        Pitch,
        SoundingPitch,
        VoiceLine,
    )


# Shared values.


def _clef(clef: Clef) -> str:
    change = {1: "+8", -1: "-8"}.get(clef.octave_change, "")
    return f"{clef.sign}{clef.line}{change}"


def _meter(meter: Meter) -> str:
    return f"{meter.numerator}/{meter.denominator}"


def _duration(duration: Duration) -> str:
    return f"{duration.base}{'.' * duration.dots}"


def format_candidates(candidates: Iterable[str]) -> str:
    """Return the canonical ``["...",...]`` spelling, or ``""`` for none."""
    quoted = ",".join(quote_text(candidate) for candidate in candidates)
    return f"[{quoted}]" if quoted else ""


# Pitches and flags.


def _pitch(pitch: Pitch) -> str:
    accidental = ""
    if pitch.accidental is not None:
        accidental = pitch.accidental.kind.value
        if pitch.accidental.parenthesized:
            accidental = f"({accidental})"
    name = f"{pitch.step}{accidental}{pitch.octave}"
    if pitch.parenthesized:
        name = f"({name})"
    fingering = "".join(str(digit) for digit in pitch.fingering)
    if fingering:
        name += f"f{fingering}"
    marks: list[str] = []
    if pitch.tie_in:
        marks.append("tie-in")
    if pitch.tie_end is not None:
        marks.append(f"tie@r{pitch.tie_end}:end")
    if pitch.tie_start is not None:
        marks.append(f"tie@r{pitch.tie_start}:start")
    marks.extend(f"arc@r{label}:start" for label in pitch.arc_starts)
    if marks:
        name += "{" + ",".join(marks) + "}"
    return name


type _FlagKey = tuple[int, str, int]

# Each flag class maps to its canonical sort key and spelling.
_FLAG_WRITERS: dict[type, Callable[[Any], tuple[_FlagKey, str]]] = {
    Articulation: lambda f: (
        (0, f.kind.value, 0),
        f"{f.kind}{f.placement or ''}",
    ),
    Fermata: lambda f: ((1, "", 0), "ferm_" if f.inverted else "ferm"),
    Arpeggiate: lambda _: ((2, "", 0), "arp"),
    Stem: lambda f: ((3, "", 0), f.direction.value),
    Grace: lambda f: ((4, "", 0), "grace/" if f.slashed else "grace"),
    CrossStaff: lambda f: ((5, "", 0), f"xs{f.staff}"),
    Beam: lambda f: ((6, "", f.level), f"beam{f.level}={f.state}"),
    Ornament: lambda f: ((7, "", 0), f"orn={f.kind}"),
    SingleTremolo: lambda f: ((8, "", 0), f"tremolo=single:{f.strokes}"),
    SlurRelation: lambda f: (
        (9, "slur", f.label),
        f"rel=slur@r{f.label}:{f.role}",
    ),
    TremoloRelation: lambda f: (
        (9, "tremolo", f.label),
        f"rel=tremolo@r{f.label}:{f.role}:{f.strokes}",
    ),
    ArpeggioRelation: lambda f: (
        (9, "arpeggio", f.label),
        f"rel=arpeggio@r{f.label}:member",
    ),
    ExtendedTrillRelation: lambda f: (
        (9, "ornament", f.label),
        f"rel=ornament@r{f.label}:{f.role}:extended-trill",
    ),
}


def _flags(flags: Iterable[NoteFlag]) -> str:
    keyed = sorted(
        (_FLAG_WRITERS[type(flag)](flag) for flag in flags),
        key=lambda item: item[0],
    )
    if not keyed:
        return ""
    return "[" + ",".join(spelling for _, spelling in keyed) + "]"


# Events.


def _note(note: Note) -> str:
    pitches = "+".join(_pitch(pitch) for pitch in note.pitches)
    return f"{pitches}/{_duration(note.duration)}{_flags(note.flags)}"


def _tempo(tempo: Tempo) -> str:
    metronome = ""
    if tempo.metronome is not None:
        metronome = f" {tempo.metronome.unit}={tempo.metronome.per_minute}"
    return f"<tempo {quote_text(tempo.text)}{metronome}>"


def _pedal_line(pedal: PedalLine) -> str:
    bracket = "ped)" if pedal.action is PedalAction.LINE_RELEASE else "ped("
    return f"<{bracket}>[rel=pedal@r{pedal.label}:{pedal.action}]"


def _tuplet(tuplet: Tuplet) -> str:
    items = " ".join(_event(item) for item in tuplet.items)
    return f"{tuplet.actual}:{tuplet.normal}{{{items}}}"


_EVENT_WRITERS: dict[type, Callable[[Any], str]] = {
    Note: _note,
    Rest: lambda e: f"r/{_duration(e.duration)}{_flags(e.flags)}",
    Spacer: lambda e: f"x/{_duration(e.duration)}{_flags(e.beams)}",
    MeasureRest: lambda e: "mrest" + _flags((e.fermata,) if e.fermata else ()),
    TimedReview: lambda e: (
        f"?({format_rational(e.duration)}){format_candidates(e.candidates)}"
    ),
    Tuplet: _tuplet,
    Dynamic: lambda e: f"<{'^' if e.above else ''}{e.mark}>",
    ClefChange: lambda e: f"<clef {_clef(e.clef)}>",
    Words: lambda e: f"<{quote_text(e.text)}>",
    Tempo: _tempo,
    Rehearsal: lambda e: f"<rehearsal {quote_text(e.text)}>",
    PedalSign: lambda e: f"<pedal-sign {e.kind}>",
    UntimedReview: lambda e: f"<?>{format_candidates(e.candidates)}",
    HairpinStart: lambda e: (
        f"<{e.kind}(>[rel=hairpin@r{e.label}:start:{e.kind}]"
    ),
    HairpinEnd: lambda e: f"<hairpin)>[rel=hairpin@r{e.label}:end]",
    OttavaStart: lambda e: (
        f"<{e.sign}(>[rel=ottava@r{e.label}:start:{e.variant}]"
    ),
    OttavaEnd: lambda e: f"<{e.sign})>[rel=ottava@r{e.label}:end:{e.variant}]",
    PedalLine: _pedal_line,
    DashesStart: lambda e: (
        f"<{quote_text(e.text)}(>[rel=dashes@r{e.label}:start]"
    ),
    DashesEnd: lambda e: f"<dashes)>[rel=dashes@r{e.label}:end]",
    TrillEnd: lambda e: f"<trill)>[rel=ornament@r{e.label}:end:extended-trill]",
}


def _event(event: Event) -> str:
    return _EVENT_WRITERS[type(event)](event)


def _voice_line(voice: VoiceLine) -> str:
    events = " ".join(_event(event) for event in voice.events)
    return f"{voice.staff}.{voice.voice}: {events}"


# Measure blocks.

_OPTION_ORDER: tuple[type, ...] = (
    LenOption,
    KeyOption,
    TimeOption,
    TimeSymbolOption,
    LeftRepeatOption,
    RightOption,
    FromOption,
    ToOption,
    EndingOption,
)


_OPTION_WRITERS: dict[type, Callable[[Any], str]] = {
    LenOption: lambda o: f"len={format_rational(o.value)}",
    KeyOption: lambda o: f"key={o.fifths}",
    TimeOption: lambda o: f"time={_meter(o.meter)}",
    TimeSymbolOption: lambda o: f"time-symbol={o.symbol}",
    LeftRepeatOption: lambda _: "left=repeat",
    RightOption: lambda o: f"right={o.barline}",
    FromOption: lambda o: f"from={format_rational(o.value)}",
    ToOption: lambda o: f"to={format_rational(o.value)}",
    EndingOption: lambda o: (
        f"ending={o.number}:{o.kind}"
        if o.kind in {EndingKind.START, EndingKind.SINGLE}
        else f"ending={o.kind}"
    ),
}


class _State:
    """The key and meter in effect before a block."""

    __slots__ = ("key", "meter")

    def __init__(self, key: int, meter: Meter) -> None:
        self.key = key
        self.meter = meter


@dataclass(frozen=True, slots=True)
class _Defaults:
    """The values that make a block option a canonical default."""

    key: int
    meter: Meter
    meter_length: Fraction
    length: Fraction


_IS_DEFAULT: dict[type, Callable[[Any, _Defaults], bool]] = {
    LenOption: lambda o, d: o.value == d.meter_length,
    KeyOption: lambda o, d: o.fifths == d.key,
    TimeOption: lambda o, d: o.meter == d.meter,
    TimeSymbolOption: lambda o, _: o.symbol is TimeSymbol.NUMERIC,
    FromOption: lambda o, _: o.value == 0,
    ToOption: lambda o, d: o.value == d.length,
}


def _canonical_options(
    block: MeasureBlock,
    state: _State,
) -> list[MeasureOption]:
    """Order the options of a block and omit the canonical defaults.

    Updates ``state`` to the key and meter in effect after the block.
    """
    key, meter = state.key, state.meter
    length = None
    for option in block.options:
        if isinstance(option, KeyOption):
            state.key = option.fifths
        elif isinstance(option, TimeOption):
            state.meter = option.meter
        elif isinstance(option, LenOption):
            length = option.value
    meter_length = state.meter.length
    defaults = _Defaults(
        key,
        meter,
        meter_length,
        meter_length if length is None else length,
    )
    kept = (
        option
        for option in block.options
        if not _IS_DEFAULT.get(type(option), lambda _o, _d: False)(
            option,
            defaults,
        )
    )
    return sorted(kept, key=lambda option: _OPTION_ORDER.index(type(option)))


def _block(block: MeasureBlock, state: _State) -> list[str]:
    options = "".join(
        f" {_OPTION_WRITERS[type(option)](option)}"
        for option in _canonical_options(block, state)
    )
    voices = sorted(block.voices, key=lambda voice: (voice.staff, voice.voice))
    return [f"m {block.number}{options}", *map(_voice_line, voices)]


# Carry state.


def _count_entry(entry: EventCount) -> str:
    return f'"{entry.staff}.{entry.voice}":{entry.count}'


def _acc_entry(entry: CarriedAccidental) -> str:
    octave = "" if entry.octave is None else f',"octave":{entry.octave}'
    return (
        f'{{"staff":{entry.staff},"step":"{entry.step}"{octave},'
        f'"alter":{entry.alter}}}'
    )


def _endpoint(endpoint: Endpoint) -> str:
    member = "null" if endpoint.member_index is None else endpoint.member_index
    return (
        f'{{"measure":{endpoint.measure},'
        f'"offset":"{format_rational(endpoint.offset)}",'
        f'"staff":{endpoint.staff},"voice":{endpoint.voice},'
        f'"event_index":{endpoint.event_index},"member_index":{member}}}'
    )


def _sounding(pitch: SoundingPitch) -> str:
    alter = '"tie-in"' if pitch.alter == "tie-in" else str(pitch.alter)
    return f'{{"step":"{pitch.step}","octave":{pitch.octave},"alter":{alter}}}'


def _open_entry(entry: OpenRelation) -> str:
    fields = ""
    if entry.pitch is not None:
        fields += f',"pitch":{_sounding(entry.pitch)}'
    if entry.variant is not None:
        fields += f',"variant":"{entry.variant}"'
    if entry.written is not None:
        fields += f',"written":"{format_rational(entry.written)}"'
    if entry.scale is not None:
        fields += f',"scale":"{format_rational(entry.scale)}"'
    return (
        f'{{"id":"r{entry.label}","kind":"{entry.kind}",'
        f'"start":{_endpoint(entry.start)}{fields}}}'
    )


def write_carry_state(state: CarryState) -> str:
    """Return the grammar spelling of a carry state."""
    counts = ",".join(map(_count_entry, state.event_counts))
    accidentals = ",".join(map(_acc_entry, state.accidentals))
    open_entries = ",".join(map(_open_entry, state.open))
    return (
        f'{{"measure":{state.measure},'
        f'"offset":"{format_rational(state.offset)}",'
        f'"event_counts":{{{counts}}},'
        f'"accidental_scope":"{state.accidental_scope}",'
        f'"accidentals":[{accidentals}],"open":[{open_entries}]}}'
    )


# Documents.


def _headings(headings: Iterable[Heading]) -> list[str]:
    return [f"heading: {quote_text(heading.text)}" for heading in headings]


def _state_lines(document: Document) -> list[str]:
    first, second = document.clefs
    return [
        f"key: {document.key}",
        f"time: {_meter(document.time)}",
        f"clefs: {_clef(first)} {_clef(second)}",
        f"carry: {document.carry}",
    ]


def write_document(document: Document) -> str:
    """Return the canonical text of a document, ending with LF."""
    lines = ["format: text-score/2"]
    state = _State(document.key, document.time)
    if isinstance(document, SystemDocument):
        lines.extend(_state_lines(document))
        lines.extend(
            (
                f"page: {document.page}",
                f"system: {document.system}",
                f"carry-in: {write_carry_state(document.carry_in)}",
            ),
        )
        lines.extend(_headings(document.headings))
        for block in document.blocks:
            lines.extend(_block(block, state))
    elif isinstance(document, AssembledScore):
        if document.title is not None:
            lines.append(f"title: {quote_text(document.title)}")
        if document.composer is not None:
            lines.append(f"composer: {quote_text(document.composer)}")
        lines.extend(_state_lines(document))
        for headed in document.blocks:
            lines.extend(_headings(headed.headings))
            lines.extend(_block(headed.block, state))
    return "\n".join(lines) + "\n"
