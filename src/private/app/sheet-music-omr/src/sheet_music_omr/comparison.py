"""Round-trip comparison instrument of the W1 comparison protocol.

The instrument compares a reference MusicXML score ``Y`` with its round trip
``T(Y)`` through musicdiff 6.0, as ``docs/comparison-protocol.md`` specifies.
It retains every raw musicdiff operation, assigns each operation to one of the
27 content categories of Text Score 2 or to ``layout``, applies the pairing
rules and counts the elements of the fidelity measure.

This module scores the musicdiff part of the content pass. The supplementary
checks add their elements through :class:`ElementTally` and the
``cross_staff_owned`` hook of :func:`compare_content`. The layout pass is
exposed as raw operations by :func:`compare_layout`.
"""

from __future__ import annotations

import hashlib
import importlib.metadata
import inspect
import re
import textwrap
from collections import Counter
from dataclasses import dataclass
from pathlib import Path
from typing import TYPE_CHECKING

import converter21
import music21 as m21
from musicdiff import DetailLevel
from musicdiff import comparison as musicdiff_comparison
from musicdiff.annotation import (
    AnnExtra,
    AnnMeasure,
    AnnNote,
    AnnPart,
    AnnScore,
    AnnVoice,
)
from musicdiff.comparison import Comparison

if TYPE_CHECKING:
    from collections.abc import Callable, Iterable, Iterator
    from fractions import Fraction

    from musicdiff.comparison import DiffOperation

CONTENT_CATEGORIES: tuple[str, ...] = (
    "pitch",
    "rhythm_rest",
    "chord_membership",
    "accidental",
    "voice",
    "cross_staff",
    "clef",
    "key_signature",
    "time_signature",
    "beam",
    "tuplet",
    "tie",
    "slur",
    "grace",
    "articulation_fermata",
    "ornament",
    "tremolo",
    "fingering",
    "dynamic",
    "hairpin",
    "ottava",
    "pedal",
    "arpeggio",
    "repeat_ending",
    "text",
    "tempo_rehearsal",
    "heading",
)
"""The 27 content categories of the Text Score 2 format, in format order."""

LAYOUT = "layout"
"""The assignment of a difference that only the layout pass counts."""

DISREGARDED = "disregarded"
"""The assignment of a difference that the content pass does not count."""

CONTENT_DETAIL = (DetailLevel.AllObjects | DetailLevel.Voicing) & ~(
    DetailLevel.Lyrics | DetailLevel.LyricIdentifiers | DetailLevel.ChordSymbols
)
"""The musicdiff detail level of the content pass."""

LAYOUT_DETAIL = CONTENT_DETAIL | DetailLevel.Style
"""The musicdiff detail level of the layout pass."""

MUSICDIFF_VERSION = "6.0"
_COUPLING_SOURCE_SHA256 = (
    "ef654c307ab0e5d6d11947e28d18f2986e05c67efdbcfe8a87118e80abc625c9"
)
_COUPLING_OLD = "compare_to[0] != original[0]"
_COUPLING_NEW = "c != original[0]"
_COUPLING_MARKER = "_sheet_music_omr_coupling_fix"

_PITCH = "pitch"
_RHYTHM = "rhythm_rest"
_CHORD = "chord_membership"
_ACCIDENTAL = "accidental"
_VOICE = "voice"
_CROSS_STAFF = "cross_staff"
_BEAM = "beam"
_TUPLET = "tuplet"
_TIE = "tie"
_GRACE = "grace"
_ARTICULATION = "articulation_fermata"
_ORNAMENT = "ornament"
_TREMOLO = "tremolo"
_ARPEGGIO = "arpeggio"

_NOTE_PAIR_OPERATIONS: dict[str, str] = {
    "pitchtypeedit": _RHYTHM,
    "inspitch": _CHORD,
    "delpitch": _CHORD,
    # musicdiff 6.0 does not encode accidental parentheses, so no accidental
    # operation can differ only in them.
    "accidentins": _ACCIDENTAL,
    "accidentdel": _ACCIDENTAL,
    "accidentedit": _ACCIDENTAL,
    "headedit": _RHYTHM,
    "dotins": _RHYTHM,
    "dotdel": _RHYTHM,
    "insspace": _RHYTHM,
    "delspace": _RHYTHM,
    "editspace": _RHYTHM,
    "graceedit": _GRACE,
    "tieins": _TIE,
    "tiedel": _TIE,
    # With Style off, musicdiff records only a tuplet's type and actual
    # number, so no content-pass tuplet operation is a printed-number or
    # bracket difference.
    "instuplet": _TUPLET,
    "deltuplet": _TUPLET,
    "edittuplet": _TUPLET,
    "editnoteshape": LAYOUT,
    "editnoteheadfill": LAYOUT,
    "editstemdirection": LAYOUT,
    "editstyle": LAYOUT,
    "editnoteheadparenthesis": DISREGARDED,
}

_EXTRA_PAIR_OPERATIONS = frozenset(
    {
        "extracontentedit",
        "extrasymboledit",
        "extrainfoedit",
        "extraoffsetedit",
        "extradurationedit",
    },
)

_STAFF_GROUP_OPERATIONS = frozenset(
    {
        "staffgrpins",
        "staffgrpdel",
        "staffgrpnameedit",
        "staffgrpabbreviationedit",
        "staffgrpsymboledit",
        "staffgrpbartogetheredit",
        "staffgrppartindicesedit",
    },
)

_INSERTIONS = frozenset({"noteins", "voiceins", "insbar", "inspart"})
_DELETIONS = frozenset({"notedel", "voicedel", "delbar", "delpart"})

_EXTRA_KINDS: dict[str, str] = {
    "clef": "clef",
    "keysig": "key_signature",
    "timesig": "time_signature",
    "ottava": "ottava",
    "slur": "slur",
    "arpeggio": _ARPEGGIO,
    "tremolo": _TREMOLO,
    "dynamic": "dynamic",
    "crescendo": "hairpin",
    "diminuendo": "hairpin",
    "pedalmark": "pedal",
    "pedalbounce": "pedal",
    "pedalgapstart": "pedal",
    "pedalgapend": "pedal",
    "barline": "repeat_ending",
    "repeat": "repeat_ending",
    "ending": "repeat_ending",
    "direction": "text",
    "tempo": "tempo_rehearsal",
    "rehearsalmark": "tempo_rehearsal",
    "staffinfo": LAYOUT,
    "systembreak": LAYOUT,
    "pagebreak": LAYOUT,
}

_ORNAMENT_NAMES = frozenset(
    {"trill", "mordent", "inverted mordent", "turn", "inverted turn"},
)
_ORNAMENT_ACCIDENTALS = re.compile(r" \([^()]*\)$")
_FINGERING = "fingering"
_SIGNATURE_KINDS = frozenset({"clef", "keysig", "timesig"})
_SYMBOL_TIMES = frozenset({"common", "cut"})

type _PendingChanges = dict[
    tuple[int, int, str],
    list[tuple[str, str | None, str | None]],
]


class ProtocolDefectError(RuntimeError):
    """A difference that the category assignment cannot assign.

    The protocol makes this a stop condition: the run stops, and the
    difference is never counted as agreement.
    """


class InstrumentMismatchError(RuntimeError):
    """The installed musicdiff is not the pinned instrument."""


class ScoreParseError(RuntimeError):
    """musicdiff cannot parse or annotate a score."""


@dataclass(frozen=True, slots=True)
class Location:
    """Where an annotated object is in its score.

    Attributes:
        staff: The 0-based index of the musicdiff part, one per staff.
        measure_index: The 0-based index of the measure in its staff.
        measure_number: The measure number with any suffix.
        voice: The 0-based index of the voice in musicdiff's voice list.
        voice_id: The music21 voice identifier, the MusicXML voice number.
        onset: The offset in quarter lengths from the start of the measure.
    """

    staff: int
    measure_index: int | None = None
    measure_number: str | None = None
    voice: int | None = None
    voice_id: str | None = None
    onset: float | Fraction | None = None


@dataclass(frozen=True, slots=True)
class OperationRecord:
    """One raw musicdiff operation.

    The first object belongs to ``T(Y)`` and the second to ``Y``.

    Attributes:
        name: The musicdiff operation name.
        kind1: The kind of the ``T(Y)`` object, if any.
        kind2: The kind of the ``Y`` object, if any.
        cost: The operation's edit distance.
        location1: The location of the ``T(Y)`` object, if any.
        location2: The location of the ``Y`` object, if any.
    """

    name: str
    kind1: str | None
    kind2: str | None
    cost: int
    location1: Location | None
    location2: Location | None


@dataclass(frozen=True, slots=True, eq=False)
class Difference:
    """One assigned difference for element counting.

    Attributes:
        symbols: The assignment of each symbol: a content category, ``layout``
            or ``disregarded``. An operation on a part of an object has one
            symbol; a decomposed whole object has one per symbol.
        y_object: The touched annotated object of ``Y``, if any.
        t_object: The annotated object of ``T(Y)``, if any.
        operations: The raw operations that make up the difference.
    """

    symbols: tuple[str, ...]
    y_object: AnnNote | AnnExtra | None
    t_object: AnnNote | AnnExtra | None
    operations: tuple[OperationRecord, ...]

    @property
    def counted(self) -> bool:
        """Whether the content pass counts this difference."""
        return bool(self.categories)

    @property
    def categories(self) -> frozenset[str]:
        """The content categories among the symbols."""
        return frozenset(s for s in self.symbols if s in CONTENT_CATEGORIES)

    @property
    def extra(self) -> bool:
        """Whether the difference holds an object only in ``T(Y)``."""
        return self.y_object is None


@dataclass(frozen=True, slots=True)
class ElementTally:
    """Element counts of the fidelity measure.

    Attributes:
        elements: The number of in-scope reference elements.
        identical: The number of identical reference elements.
        extras: The number of extra elements.
    """

    elements: int
    identical: int
    extras: int

    def __add__(self, other: ElementTally) -> ElementTally:
        """Return the sum of two tallies, for pooling and for checks."""
        return ElementTally(
            self.elements + other.elements,
            self.identical + other.identical,
            self.extras + other.extras,
        )

    @property
    def fidelity(self) -> float | None:
        """Identical elements over elements plus extras, if any."""
        denominator = self.elements + self.extras
        return self.identical / denominator if denominator else None


@dataclass(frozen=True, slots=True, eq=False)
class PassRecord:
    """The retained figures of one musicdiff pass over one file.

    Attributes:
        operations: Every raw operation, in musicdiff's order.
        omr_edit_distance: musicdiff's total edit distance.
        symbols_t: The musicdiff symbol count of ``T(Y)``.
        symbols_y: The musicdiff symbol count of ``Y``.
    """

    operations: tuple[OperationRecord, ...]
    omr_edit_distance: int
    symbols_t: int
    symbols_y: int

    @property
    def normalized_distance(self) -> float:
        """The edit distance over the symbol counts of both scores."""
        both = self.symbols_t + self.symbols_y
        return self.omr_edit_distance / both if both else 0.0


@dataclass(frozen=True, slots=True, eq=False)
class AnnotatedScore:
    """A parsed score and its annotation, with reprints removed.

    The music21 score is kept because annotated objects refer to it weakly.

    Attributes:
        score: The music21 score.
        annotated: The musicdiff annotated score.
    """

    score: m21.stream.Score
    annotated: AnnScore


@dataclass(frozen=True, slots=True, eq=False)
class ContentResult:
    """The musicdiff part of the content pass over one file.

    Attributes:
        record: The retained raw figures.
        differences: Every assigned difference, counted or not.
        elements: The in-scope annotated elements of ``Y``.
        t_score: The annotated ``T(Y)``.
        y_score: The annotated ``Y``.
    """

    record: PassRecord
    differences: tuple[Difference, ...]
    elements: tuple[AnnNote | AnnExtra, ...]
    t_score: AnnotatedScore
    y_score: AnnotatedScore

    @property
    def counted(self) -> tuple[Difference, ...]:
        """The counted differences."""
        return tuple(d for d in self.differences if d.counted)

    @property
    def non_identical(self) -> tuple[AnnNote | AnnExtra, ...]:
        """The elements that a counted difference touches."""
        touched = {id(d.y_object) for d in self.counted}
        return tuple(e for e in self.elements if id(e) in touched)

    @property
    def tally(self) -> ElementTally:
        """The element counts of the musicdiff part."""
        elements = len(self.elements)
        return ElementTally(
            elements,
            elements - len(self.non_identical),
            sum(1 for d in self.counted if d.extra),
        )

    def category_counts(self) -> Counter[str]:
        """Return the counted symbols per content category."""
        return Counter(
            s
            for d in self.counted
            for s in d.symbols
            if s in CONTENT_CATEGORIES
        )


def install_coupling_fix() -> None:
    """Install the protocol's fix of musicdiff's voice coupling.

    Replaces exactly ``compare_to[0] != original[0]`` with
    ``c != original[0]`` in ``Comparison._voices_coupling_recursive`` and
    changes nothing else. The installed function's source must match the
    pinned musicdiff 6.0 source. The fix is installed once per process.

    Raises:
        InstrumentMismatchError: If musicdiff is not the pinned version or the
            function's source does not match.
    """
    current = Comparison.__dict__["_voices_coupling_recursive"]
    if getattr(current.__func__, _COUPLING_MARKER, False):
        return
    version = importlib.metadata.version("musicdiff")
    if version != MUSICDIFF_VERSION:
        message = f"musicdiff {version} is installed, not {MUSICDIFF_VERSION}"
        raise InstrumentMismatchError(message)
    source = textwrap.dedent(inspect.getsource(current.__func__))
    digest = hashlib.sha256(source.encode()).hexdigest()
    if digest != _COUPLING_SOURCE_SHA256 or source.count(_COUPLING_OLD) != 1:
        message = "musicdiff voice-coupling source does not match 6.0"
        raise InstrumentMismatchError(message)
    namespace = dict(vars(musicdiff_comparison))
    code = compile(
        source.replace(_COUPLING_OLD, _COUPLING_NEW),
        inspect.getsourcefile(current.__func__) or "<musicdiff>",
        "exec",
    )
    exec(code, namespace)  # noqa: S102 - the pinned, checked musicdiff source
    patched = namespace["_voices_coupling_recursive"]
    setattr(patched.__func__, _COUPLING_MARKER, True)
    Comparison._voices_coupling_recursive = patched  # noqa: SLF001


def annotate(
    source: Path | str,
    detail: DetailLevel | int = CONTENT_DETAIL,
) -> AnnotatedScore:
    """Parse and annotate one score as musicdiff does, removing reprints.

    Args:
        source: A MusicXML file path, or MusicXML text.
        detail: The musicdiff detail level.

    Returns:
        The parsed and annotated score.

    Raises:
        ScoreParseError: If music21 or musicdiff cannot parse the score.
    """
    converter21.register()
    try:
        if isinstance(source, Path):
            parsed = m21.converter.parse(source, forceSource=True)
        else:
            parsed = m21.converter.parse(
                source,
                format="musicxml",
                forceSource=True,
            )
        if not isinstance(parsed, m21.stream.Score):
            message = "the source is not a single score"
            raise ScoreParseError(message)  # noqa: TRY301
        annotated = AnnScore(parsed, detail)
    except ScoreParseError:
        raise
    # Any music21 or musicdiff failure on the score is a parse-failure status.
    except Exception as error:
        message = f"musicdiff cannot parse the score: {error}"
        raise ScoreParseError(message) from error
    _remove_reprints(annotated, parsed)
    return AnnotatedScore(parsed, annotated)


def compare_layout(t_source: Path | str, y_source: Path | str) -> PassRecord:
    """Run the layout pass and retain its raw figures.

    Args:
        t_source: The round trip ``T(Y)``.
        y_source: The reference ``Y``.

    Returns:
        The raw operations and figures of the layout pass.
    """
    return _run_pass(
        annotate(t_source, LAYOUT_DETAIL),
        annotate(y_source, LAYOUT_DETAIL),
    )[0]


def compare_content(
    t_source: Path | str,
    y_source: Path | str,
    *,
    cross_staff_owned: Callable[[AnnNote, AnnNote], bool] | None = None,
) -> ContentResult:
    """Run and score the musicdiff part of the content pass.

    Args:
        t_source: The round trip ``T(Y)``.
        y_source: The reference ``Y``.
        cross_staff_owned: Whether the cross-staff check has an element for
            the ``Y`` note or the ``T(Y)`` note of a pairing-rule-1 pair, in
            which case the pair is disregarded.

    Returns:
        The retained figures, assigned differences and elements.

    Raises:
        ProtocolDefectError: If a difference cannot be assigned.
    """
    t_score = annotate(t_source)
    y_score = annotate(y_source)
    record, operations = _run_pass(t_score, y_score)
    locations = _locate(t_score.annotated) | _locate(y_score.annotated)
    differences: list[Difference] = []
    pending: _PendingChanges = {}
    for operation in operations:
        differences.extend(
            _assign(operation, record_of(operation, locations), pending),
        )
    differences = _apply_pairing(differences, locations, cross_staff_owned)
    elements = tuple(_elements(y_score.annotated))
    element_ids = {id(e) for e in elements}
    for difference in differences:
        if (
            difference.counted
            and not difference.extra
            and id(difference.y_object) not in element_ids
        ):
            message = f"difference outside the elements: {difference}"
            raise ProtocolDefectError(message)
    return ContentResult(
        record,
        tuple(differences),
        elements,
        t_score,
        y_score,
    )


def failure_element_count(source: Path | str) -> int:
    """Count the content-pass in-scope elements of one score alone.

    This is the musicdiff part of a failed file's element count.

    Args:
        source: The score, ``Y`` or ``X``.

    Returns:
        The number of in-scope annotated elements.
    """
    return sum(1 for _ in _elements(annotate(source).annotated))


def pooled_fidelity(tallies: Iterable[ElementTally]) -> float | None:
    """Pool element tallies over the files of an arm.

    Args:
        tallies: One tally per file.

    Returns:
        Identical elements over elements plus extras, or None when both are
        zero.
    """
    return sum(tallies, ElementTally(0, 0, 0)).fidelity


def record_of(
    operation: DiffOperation,
    locations: dict[int, Location],
) -> OperationRecord:
    """Return the retained record of one raw musicdiff operation.

    Args:
        operation: The musicdiff operation.
        locations: The locations of annotated objects by ``id``.

    Returns:
        The operation's record.
    """
    return OperationRecord(
        operation.name,
        _kind(operation.obj1),
        _kind(operation.obj2),
        operation.edit_distance,
        locations.get(id(operation.obj1)) if operation.obj1 else None,
        locations.get(id(operation.obj2)) if operation.obj2 else None,
    )


def _run_pass(
    t_score: AnnotatedScore,
    y_score: AnnotatedScore,
) -> tuple[PassRecord, list[DiffOperation]]:
    install_coupling_fix()
    operations, cost = Comparison.annotated_scores_diff(
        t_score.annotated,
        y_score.annotated,
    )
    locations = _locate(t_score.annotated) | _locate(y_score.annotated)
    record = PassRecord(
        tuple(record_of(op, locations) for op in operations),
        cost,
        t_score.annotated.notation_size(),
        y_score.annotated.notation_size(),
    )
    return record, operations


def _kind(obj: object) -> str | None:
    if obj is None:
        return None
    if isinstance(obj, AnnExtra):
        return f"{type(obj).__name__}:{obj.kind}"
    return type(obj).__name__


def _measure_indexes(part: object) -> dict[int, int]:
    if not isinstance(part, m21.stream.Stream):
        return {}
    return {
        id(measure): index
        for index, measure in enumerate(
            part.getElementsByClass(m21.stream.Measure),
        )
    }


def _locate(score: AnnScore) -> dict[int, Location]:
    locations: dict[int, Location] = {}
    for staff, part in enumerate(score.part_list):
        locations[id(part)] = Location(staff)
        indexes = _measure_indexes(part.get_object())
        for bar in part.bar_list:
            measure = bar.get_object()
            if not isinstance(measure, m21.stream.Measure):
                message = f"annotated bar without a music21 measure: {bar}"
                raise ProtocolDefectError(message)
            index = indexes.get(id(measure))
            locations[id(bar)] = Location(staff, index, bar.measureNumber)
            for voice_index, voice in enumerate(bar.voices_list):
                m21_voice = voice.get_object()
                voice_id = (
                    str(m21_voice.id)
                    if isinstance(m21_voice, m21.stream.Voice)
                    else None
                )
                locations[id(voice)] = Location(
                    staff,
                    index,
                    bar.measureNumber,
                    voice_index,
                    voice_id,
                )
                for note in voice.annot_notes:
                    locations[id(note)] = Location(
                        staff,
                        index,
                        bar.measureNumber,
                        voice_index,
                        voice_id,
                        _general_note(note).getOffsetInHierarchy(measure),
                    )
            for extra in bar.extras_list:
                locations[id(extra)] = Location(
                    staff,
                    index,
                    bar.measureNumber,
                    onset=extra.offset,
                )
    return locations


def _elements(score: AnnScore) -> Iterator[AnnNote | AnnExtra]:
    for part in score.part_list:
        for bar in part.bar_list:
            for voice in bar.voices_list:
                yield from voice.annot_notes
            for extra in bar.extras_list:
                if _extra_category(extra.kind) in CONTENT_CATEGORIES:
                    yield extra


def _system_starts(score: m21.stream.Score) -> set[int]:
    starts = {0}
    layouts = (m21.layout.SystemLayout, m21.layout.PageLayout)
    for part in score.parts:
        measures = part.getElementsByClass(m21.stream.Measure)
        for index, measure in enumerate(measures):
            if any(x.isNew for x in measure.getElementsByClass(layouts)):
                starts.add(index)
    return starts


def _is_reprint(
    extra: AnnExtra,
    in_effect: tuple[object, ...] | None,
    *,
    system_start: bool,
) -> bool:
    if in_effect is None or _signature(extra) != in_effect:
        return False
    if extra.kind == "timesig":
        return extra.infodict.get("symbol") not in _SYMBOL_TIMES
    if extra.kind == "clef":
        return system_start and extra.offset == 0
    return True


def _signature(extra: AnnExtra) -> tuple[object, ...]:
    return (extra.content, extra.symbolic, sorted(extra.infodict.items()))


def _remove_reprints(annotated: AnnScore, score: m21.stream.Score) -> None:
    starts = _system_starts(score)
    for part in annotated.part_list:
        indexes = _measure_indexes(part.get_object())
        in_effect: dict[str, tuple[object, ...]] = {}
        changed = False
        for bar in part.bar_list:
            system_start = indexes.get(id(bar.get_object())) in starts
            reprints: set[int] = set()
            for extra in bar.extras_list:
                if extra.kind not in _SIGNATURE_KINDS:
                    continue
                if _is_reprint(
                    extra,
                    in_effect.get(extra.kind),
                    system_start=system_start,
                ):
                    reprints.add(id(extra))
                in_effect[extra.kind] = _signature(extra)
            if reprints:
                _drop_extras(bar, reprints)
                changed = True
        if changed:
            part.bar_list = [b for b in part.bar_list if b.n_of_elements > 0]
            part.n_of_bars = len(part.bar_list)
            part.precomputed_str = hash(str(part))
            _reset_size(part)
            _reset_size(annotated)


def _drop_extras(bar: AnnMeasure, reprints: set[int]) -> None:
    bar.extras_list = [e for e in bar.extras_list if id(e) not in reprints]
    bar.n_of_elements -= len(reprints)
    bar.precomputed_str = hash(str(bar))
    bar.precomputed_repr = hash(repr(bar))
    _reset_size(bar)


def _reset_size(obj: AnnMeasure | AnnPart | AnnScore) -> None:
    obj._cached_notation_size = None  # noqa: SLF001 - musicdiff's size cache


def _extra_category(kind: str) -> str:
    category = _EXTRA_KINDS.get(kind)
    if category is None:
        message = f"extra kind {kind!r} has no category"
        raise ProtocolDefectError(message)
    return category


def _expression_category(name: str) -> str:
    if name == "fermata":
        return _ARTICULATION
    if name == "bTrem":
        return _TREMOLO
    if name.startswith("arpeggio"):
        return _ARPEGGIO
    if _ORNAMENT_ACCIDENTALS.sub("", name) in _ORNAMENT_NAMES:
        return _ORNAMENT
    message = f"expression {name!r} has no category"
    raise ProtocolDefectError(message)


def _articulation_category(name: str) -> str:
    return DISREGARDED if name == _FINGERING else _ARTICULATION


def _general_note(note: AnnNote) -> m21.note.GeneralNote:
    general_note = note.get_object()
    if not isinstance(general_note, m21.note.GeneralNote):
        message = f"annotated note without a music21 note: {note}"
        raise ProtocolDefectError(message)
    return general_note


def _is_rest(note: AnnNote) -> bool:
    return _general_note(note).isRest


def _duration_type(note: AnnNote) -> str:
    return _general_note(note).duration.type


def _note_symbols(note: AnnNote) -> tuple[str, ...]:
    symbols: list[str] = []
    rest = _is_rest(note)
    for index, pitch in enumerate(note.pitches):
        if rest:
            symbols.append(_RHYTHM)
        else:
            symbols.append(_PITCH if index == 0 else _CHORD)
        if pitch.accidental != "None":
            symbols.append(_ACCIDENTAL)
        if pitch.tied:
            symbols.append(_TIE)
    symbols.append(_RHYTHM)
    symbols.extend([_RHYTHM] * note.dots)
    # A rest's beams are rebuilt by musicdiff and owned by the rest-beam check.
    symbols.extend([DISREGARDED if rest else _BEAM] * len(note.beamings))
    symbols.extend([_TUPLET] * (len(note.tuplets) + len(note.tuplet_info)))
    symbols.extend(_articulation_category(a) for a in note.articulations)
    symbols.extend(_expression_category(e) for e in note.expressions)
    if note.graceness:
        symbols.append(_GRACE)
    if note.gap_dur != 0:
        symbols.append(_RHYTHM)
    layout = (
        note.noteshape != "normal",
        note.noteheadFill is not None,
        note.stemDirection != "unspecified",
        bool(note.styledict),
    )
    symbols.extend(LAYOUT for present in layout if present)
    if note.noteheadParenthesis:
        symbols.append(DISREGARDED)
    return tuple(symbols)


def _whole_objects(obj: object) -> Iterator[AnnNote | AnnExtra]:
    if isinstance(obj, (AnnNote, AnnExtra)):
        yield obj
    elif isinstance(obj, AnnVoice):
        yield from obj.annot_notes
    elif isinstance(obj, AnnMeasure):
        for voice in obj.voices_list:
            yield from voice.annot_notes
        yield from obj.extras_list
    elif isinstance(obj, AnnPart):
        for bar in obj.bar_list:
            yield from _whole_objects(bar)
    else:
        message = f"cannot decompose {type(obj).__name__}"
        raise ProtocolDefectError(message)


def _whole_symbols(obj: AnnNote | AnnExtra) -> tuple[str, ...]:
    if isinstance(obj, AnnNote):
        return _note_symbols(obj)
    return (_extra_category(obj.kind),)


def _assign(
    operation: DiffOperation,
    record: OperationRecord,
    pending: _PendingChanges,
) -> list[Difference]:
    name = operation.name
    t_obj, y_obj = operation.obj1, operation.obj2
    if name in _INSERTIONS or name == "extrains":
        return [
            Difference(_whole_symbols(o), o, None, (record,))
            for o in _whole_objects(y_obj)
        ]
    if name in _DELETIONS or name == "extradel":
        return [
            Difference(_whole_symbols(o), None, o, (record,))
            for o in _whole_objects(t_obj)
        ]
    if name in _STAFF_GROUP_OPERATIONS:
        return [Difference((LAYOUT,), None, None, (record,))]
    if name in _EXTRA_PAIR_OPERATIONS or name == "extrastyleedit":
        if not isinstance(y_obj, AnnExtra) or not isinstance(t_obj, AnnExtra):
            message = f"{name} without two extras"
            raise ProtocolDefectError(message)
        category = (
            LAYOUT if name == "extrastyleedit" else _extra_category(y_obj.kind)
        )
        return [Difference((category,), y_obj, t_obj, (record,))]
    if not isinstance(y_obj, AnnNote) or not isinstance(t_obj, AnnNote):
        message = f"operation {name!r} has no assignment"
        raise ProtocolDefectError(message)
    return [
        Difference((symbol,), y_obj, t_obj, (record,))
        for symbol in _note_pair_symbols(name, t_obj, y_obj, pending)
    ]


def _note_pair_symbols(
    name: str,
    t_note: AnnNote,
    y_note: AnnNote,
    pending: _PendingChanges,
) -> list[str]:
    if name in _NOTE_PAIR_OPERATIONS:
        return [_NOTE_PAIR_OPERATIONS[name]]
    if name == "pitchnameedit":
        both_rests = _is_rest(t_note) and _is_rest(y_note)
        return [LAYOUT if both_rests else _PITCH]
    if name in {"insbeam", "delbeam", "editbeam"}:
        if _duration_type(t_note) != _duration_type(y_note):
            return [_RHYTHM]
        both_rests = _is_rest(t_note) and _is_rest(y_note)
        return [DISREGARDED if both_rests else _BEAM]
    if name in {"insarticulation", "delarticulation", "editarticulation"}:
        change = _aligned_change(
            name,
            (t_note, y_note, "articulation"),
            pending,
        )
        return [_change_category(change, _articulation_category)]
    if name in {"insexpression", "delexpression", "editexpression"}:
        change = _aligned_change(
            name,
            (t_note, y_note, "expression"),
            pending,
        )
        return [_change_category(change, _expression_category)]
    message = f"operation {name!r} has no assignment"
    raise ProtocolDefectError(message)


def _aligned_change(
    name: str,
    pair: tuple[AnnNote, AnnNote, str],
    pending: _PendingChanges,
) -> tuple[str | None, str | None]:
    """Return the items that one articulation or expression operation changes.

    musicdiff's operation carries no index, so its alignment is recomputed,
    and the changes of one note pair are consumed in musicdiff's order.
    """
    t_note, y_note, which = pair
    key = (id(t_note), id(y_note), which)
    if key not in pending:
        t_items = getattr(t_note, which + "s")
        y_items = getattr(y_note, which + "s")
        pending[key] = _alignment_changes(t_items, y_items, which)
    changes = pending[key]
    if not changes or changes[0][0] != name:
        message = f"{name} does not match musicdiff's alignment"
        raise ProtocolDefectError(message)
    _, t_item, y_item = changes.pop(0)
    return t_item, y_item


def _alignment_changes(
    t_items: list[str],
    y_items: list[str],
    which: str,
) -> list[tuple[str, str | None, str | None]]:
    def sub_cost(i: int, j: int) -> int:
        return 0 if t_items[i] == y_items[j] else 1

    align = musicdiff_comparison._align_suffixes  # noqa: SLF001 - musicdiff's own alignment
    _, steps = align(
        len(t_items),
        len(y_items),
        [1] * len(t_items),
        [1] * len(y_items),
        sub_cost,
    )
    changes: list[tuple[str, str | None, str | None]] = []
    for kind, i, j in reversed(steps):
        if kind == "del":
            changes.append(("del" + which, t_items[i], None))
        elif kind == "ins":
            changes.append(("ins" + which, None, y_items[j]))
        elif t_items[i] != y_items[j]:
            changes.append(("edit" + which, t_items[i], y_items[j]))
    return changes


def _change_category(
    change: tuple[str | None, str | None],
    categorize: Callable[[str], str],
) -> str:
    categories = {categorize(item) for item in change if item is not None}
    content = categories - {DISREGARDED}
    if len(content) > 1:
        message = f"edit {change!r} spans categories {sorted(content)}"
        raise ProtocolDefectError(message)
    return content.pop() if content else DISREGARDED


def _pairing_key(
    note: AnnNote,
    location: Location,
) -> tuple[object, ...]:
    m21_note = _general_note(note)
    return (
        location.measure_index,
        location.onset,
        tuple((p.name, p.accidental, p.tied) for p in note.pitches),
        m21_note.duration.quarterLength,
        m21_note.duration.type,
        note.dots,
        note.graceness,
        tuple(note.tuplets),
        tuple(note.tuplet_info),
        tuple(note.articulations),
        tuple(note.expressions),
        () if _is_rest(note) else tuple(note.beamings),
    )


def _pairing_rule(t_location: Location, y_location: Location) -> str | None:
    """Return the category of a matching pair under rule 1 or rule 2."""
    if t_location.staff != y_location.staff:
        return _CROSS_STAFF
    if t_location.voice_id != y_location.voice_id:
        return _VOICE
    return None


def _apply_pairing(
    differences: list[Difference],
    locations: dict[int, Location],
    cross_staff_owned: Callable[[AnnNote, AnnNote], bool] | None,
) -> list[Difference]:
    """Apply pairing rules 1 and 2, in that order, one to one."""
    inserted = [d for d in differences if _whole_note(d, y_side=True)]
    deleted = [d for d in differences if _whole_note(d, y_side=False)]
    removed: set[int] = set()
    added: list[Difference] = []
    for rule in (_CROSS_STAFF, _VOICE):
        for y_diff in inserted:
            t_diff = _pair_for(y_diff, deleted, removed, locations, rule)
            if t_diff is None:
                continue
            removed.update((id(y_diff), id(t_diff)))
            y_note, t_note = y_diff.y_object, t_diff.t_object
            if not isinstance(y_note, AnnNote) or not isinstance(
                t_note,
                AnnNote,
            ):
                continue
            if (
                rule == _CROSS_STAFF
                and cross_staff_owned is not None
                and cross_staff_owned(y_note, t_note)
            ):
                continue
            operations = y_diff.operations + t_diff.operations
            added.append(Difference((rule,), y_note, t_note, operations))
    return [d for d in differences if id(d) not in removed] + added


def _whole_note(difference: Difference, *, y_side: bool) -> bool:
    names = _INSERTIONS if y_side else _DELETIONS
    note = difference.y_object if y_side else difference.t_object
    return (
        isinstance(note, AnnNote)
        and len(difference.operations) == 1
        and difference.operations[0].name in names
    )


def _pair_for(
    y_diff: Difference,
    deleted: list[Difference],
    removed: set[int],
    locations: dict[int, Location],
    rule: str,
) -> Difference | None:
    if id(y_diff) in removed or not isinstance(y_diff.y_object, AnnNote):
        return None
    y_location = locations[id(y_diff.y_object)]
    y_key = _pairing_key(y_diff.y_object, y_location)
    for t_diff in deleted:
        if id(t_diff) in removed or not isinstance(t_diff.t_object, AnnNote):
            continue
        t_location = locations[id(t_diff.t_object)]
        if (
            _pairing_rule(t_location, y_location) == rule
            and _pairing_key(t_diff.t_object, t_location) == y_key
        ):
            return t_diff
    return None
