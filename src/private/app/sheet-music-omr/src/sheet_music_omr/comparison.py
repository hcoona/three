"""Round-trip comparison instrument of the W1 comparison protocol.

The instrument compares a reference MusicXML score ``Y`` with its round trip
``T(Y)`` through musicdiff 6.0, as ``docs/comparison-protocol.md`` specifies.
Same-ordinal measure comparisons and a global-only comparison assign
differences and count fidelity elements; the ordinary whole-score comparison
is retained as a separate diagnostic.

This module scores the musicdiff part of the content pass and removes the
reprints that the format does not encode. :func:`annotate` maps each annotated
note to its MusicXML event, :func:`compare_annotated` compares two annotated
scores, and rule 1 uses its ``cross_staff_owned`` hook.
``sheet_music_omr.supplementary`` runs the supplementary checks and combines
them with this content pass per file. :func:`compare_layout` runs the layout
pass and counts the layout elements of the layout-fidelity score.

Every function that takes a score takes a ``Path`` to a MusicXML file or the
MusicXML text as a ``str``; a ``str`` is never read as a path.
"""

from __future__ import annotations

import hashlib
import importlib.metadata
import inspect
import math
import re
import textwrap
from bisect import bisect_right
from collections import Counter
from copy import copy
from dataclasses import dataclass, field
from fractions import Fraction
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
    AnnStaffGroup,
    AnnVoice,
)
from musicdiff.comparison import Comparison

from sheet_music_omr.musicxml_events import (
    DASHES,
    WAVY_LINE,
    PitchKey,
    ProtocolDefectError,
    VoiceKey,
    XmlDirection,
    XmlEvent,
    XmlScore,
    XmlSpan,
    dashes_text,
    read_score,
    voice_staves,
)
from sheet_music_omr.musicxml_reverse import _load as _load_musicxml

if TYPE_CHECKING:
    import xml.etree.ElementTree as ET
    from collections.abc import Callable, Iterable, Iterator

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
_OTTAVA_OCTAVES = {8: 1, 15: 2, 22: 3}
"""The octaves of each music21 ottava shift magnitude."""

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

_TUPLET_OPERATIONS = frozenset({"instuplet", "deltuplet", "edittuplet"})

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
# A trill expression, with its accidental marks and, in the layout pass, its
# placement, as musicdiff spells them.
_TRILL_EXPRESSION = re.compile(r"trill(?: \([^()]*\))?(?:\([^()]*\))?")
# With Style, musicdiff appends a placement to an articulation's name and to
# an ornament's name.
_PLACEMENT = re.compile(r"\((?:above|below)\)$")
_FERMATA = "fermata"
_INVERTED_FERMATA = "inverted fermata"
# MusicXML defines a fermata without a type as upright, and music21 10.5.2
# gives it the type "inverted".
_UNTYPED_FERMATA = re.compile(r"<fermata(?=[\s/>])(?![^>]*\btype\s*=)")
_UNTYPED_FERMATA_BYTES = re.compile(_UNTYPED_FERMATA.pattern.encode())
_UPRIGHT_FERMATA = '<fermata type="upright"'
_ZIP_MAGIC = b"PK"
_FINGERING = "fingering"
_ARTICULATIONS = "articulations"
_EXPRESSIONS = "expressions"
_TUPLETS = "tuplets"
_TUPLET_INFO = "tuplet_info"
_HEADING_ROLES = frozenset(
    {"title", "subtitle", "composer", "arranger", "lyricist"},
)
_SIGNATURE_KINDS = frozenset({"clef", "keysig", "timesig"})
_SYMBOL_TIMES = frozenset({"common", "cut"})
_SINGLE_VOICE_POSITION = 1
_TWO_VOICE_COUNT = 2
_VOICE_POSITION_STEP = 2
_MAX_CANONICAL_VOICES = 4
_MAX_VOICES_PER_STEM = 2
_VALID_STEM_COUNTS = frozenset({(1, 1), (2, 1), (1, 2), (2, 2)})

type _AnnObject = AnnNote | AnnExtra | AnnStaffGroup
type _AttachedItem = tuple[str, int]


@dataclass(frozen=True, slots=True)
class _ItemChange:
    """One change of an annotated note's attached-item list.

    Attributes:
        name: The musicdiff operation name.
        source: The annotated note's list that holds the items.
        t_item: The item of ``T(Y)``, if any.
        y_item: The item of ``Y``, if any.
        t_index: The index of ``t_item`` in its list, if any.
        y_index: The index of ``y_item`` in its list, if any.
    """

    name: str
    source: str
    t_item: str | None
    y_item: str | None
    t_index: int | None
    y_index: int | None


type _PendingChanges = dict[tuple[int, int, str], list[_ItemChange]]


class InstrumentMismatchError(RuntimeError):
    """The installed musicdiff is not the pinned instrument."""


class ScoreParseError(RuntimeError):
    """musicdiff cannot parse or annotate a score."""


@dataclass(frozen=True, slots=True)
class Location:
    """Where an annotated object is in its score.

    Attributes:
        staff: The 0-based index of the musicdiff part, one per staff.
        part_index: The 0-based source MusicXML part ordinal.
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
    voice_position: int | None = None
    part_index: int | None = None
    grace_position: int = 0


@dataclass(frozen=True, slots=True)
class _CrossStaffBeamGroup:
    home_staff: int
    measure_index: int
    voice_id: str | tuple[str, int]
    events: tuple[tuple[int, AnnNote], ...]
    event_onsets: tuple[tuple[int, Fraction], ...]
    home_note: AnnNote
    home_direction: str | None


@dataclass(frozen=True, slots=True)
class _ScoreVoiceEvent:
    onset: Fraction
    order: tuple[Fraction, bool, int, int]
    staff: int
    source_staff: tuple[int, int]
    note: AnnNote
    voice_id: str | None


@dataclass(slots=True)
class _ActiveCrossStaffBeam:
    events: list[_ScoreVoiceEvent]
    voice_id: str | tuple[str, int]
    is_grace: bool
    complete: bool


_BeamStreamKey = tuple[tuple[int, int], str, str | int]
_BeamKey = tuple[_BeamStreamKey, bool]


@dataclass(frozen=True, slots=True)
class _MeasureEventContext:
    measure: m21.stream.Measure
    bar: AnnMeasure
    event_onsets: dict[int, Fraction] | None


@dataclass(frozen=True, slots=True)
class _StaffVoiceContext:
    staff: int
    measure_index: int
    groups_by_note: dict[int, _CrossStaffBeamGroup]
    measure_events: _MeasureEventContext


@dataclass(slots=True)
class _CrossStaffBeamTracker:
    measure_index: int
    groups: list[_CrossStaffBeamGroup] = field(default_factory=list)
    active: dict[_BeamKey, _ActiveCrossStaffBeam] = field(default_factory=dict)
    aliases: dict[_BeamKey, _BeamKey] = field(default_factory=dict)

    def add(self, event: _ScoreVoiceEvent) -> None:
        stream_key: _BeamStreamKey = (
            event.source_staff,
            "voice" if event.voice_id is not None else "staff",
            event.voice_id if event.voice_id is not None else event.staff,
        )
        is_grace = bool(event.note.graceness)
        event_key = (stream_key, is_grace)
        active_key = self.aliases.get(event_key, event_key)
        beam = self.active.get(active_key)
        beam_type = _source_primary_beam_type(event.note)
        if beam_type is None:
            self._reject_interruption(beam)
            return
        if beam_type == "start":
            self._start(event, active_key, beam, is_grace=is_grace)
            return
        if beam_type not in {"continue", "stop", "partial"}:
            return
        active_key, beam = self._find_owner(
            event,
            event_key,
            active_key,
            beam,
            is_grace=is_grace,
        )
        if beam_type == "partial":
            if beam is not None:
                beam.events.append(event)
            return
        self._advance(
            event,
            active_key,
            beam,
            beam_type,
            is_grace=is_grace,
        )

    def _find_owner(
        self,
        event: _ScoreVoiceEvent,
        event_key: _BeamKey,
        active_key: _BeamKey,
        beam: _ActiveCrossStaffBeam | None,
        *,
        is_grace: bool,
    ) -> tuple[
        _BeamKey,
        _ActiveCrossStaffBeam | None,
    ]:
        if beam is not None:
            return active_key, beam
        candidates = [
            (candidate_key, candidate)
            for candidate_key, candidate in self.active.items()
            if candidate.is_grace == is_grace
            and candidate_key != active_key
            and candidate.events[-1].source_staff != event.source_staff
            and (
                event.voice_id is None
                or candidate.events[-1].voice_id == event.voice_id
            )
        ]
        if len(candidates) > 1:
            message = "a flattened beam event has ambiguous voice ownership"
            raise ProtocolDefectError(message)
        if not candidates:
            return active_key, None
        active_key, beam = candidates[0]
        self.aliases[event_key] = active_key
        if event.voice_id is not None and isinstance(beam.voice_id, tuple):
            beam.voice_id = event.voice_id
        return active_key, beam

    def _reject_interruption(self, beam: _ActiveCrossStaffBeam | None) -> None:
        if beam is not None:
            message = "a primary beam group is interrupted before its stop"
            raise ProtocolDefectError(message)

    def _start(
        self,
        event: _ScoreVoiceEvent,
        active_key: _BeamKey,
        beam: _ActiveCrossStaffBeam | None,
        *,
        is_grace: bool,
    ) -> None:
        if beam is not None:
            message = "a voice has nested primary beam groups"
            raise ProtocolDefectError(message)
        self.active[active_key] = _ActiveCrossStaffBeam(
            [event],
            _cross_staff_voice_id(event),
            is_grace=is_grace,
            complete=True,
        )

    def _advance(
        self,
        event: _ScoreVoiceEvent,
        active_key: _BeamKey,
        beam: _ActiveCrossStaffBeam | None,
        beam_type: str,
        *,
        is_grace: bool,
    ) -> None:
        if beam is None:
            beam = _ActiveCrossStaffBeam(
                [event],
                _cross_staff_voice_id(event),
                is_grace=is_grace,
                complete=False,
            )
            if beam_type != "stop":
                self.active[active_key] = beam
                return
        else:
            beam.events.append(event)
        if beam_type == "stop":
            self._complete(active_key, beam)

    def _complete(
        self,
        active_key: _BeamKey,
        beam: _ActiveCrossStaffBeam,
    ) -> None:
        self.groups.extend(
            _completed_cross_staff_beam(
                beam.events,
                self.measure_index,
                beam.voice_id,
                complete=beam.complete,
            ),
        )
        self.active.pop(active_key, None)
        self.aliases = {
            alias: target
            for alias, target in self.aliases.items()
            if target != active_key
        }


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
        y_item: The list and index of the changed attached item of
            ``y_object``, if any.
        t_item: The list and index of the changed attached item of
            ``t_object``, if any.
    """

    symbols: tuple[str, ...]
    y_object: _AnnObject | None
    t_object: _AnnObject | None
    operations: tuple[OperationRecord, ...]
    y_item: _AttachedItem | None = None
    t_item: _AttachedItem | None = None

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


@dataclass(frozen=True, slots=True, eq=False)
class NotePair:
    """A note pair matched by one of the protocol's pairing rules."""

    rule: str
    y_note: AnnNote
    t_note: AnnNote
    y_location: Location
    t_location: Location


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
class PassDiagnostic:
    """The ordinary whole-score figures of one musicdiff pass over one file.

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
class PassRecord:
    """The authoritative anchored comparisons and whole-score diagnostic.

    Attributes:
        measure_operations: Operations from same-ordinal measure pairs.
        measure_edit_cost: Sum of edit costs returned for those pairs.
        global_operations: Operations from the once-per-score global-only pass.
        global_edit_cost: Edit cost returned for the global-only pass.
        whole_score: Ordinary whole-score musicdiff diagnostic; it does not
            assign or count content differences.
    """

    measure_operations: tuple[OperationRecord, ...]
    measure_edit_cost: int
    global_operations: tuple[OperationRecord, ...]
    global_edit_cost: int
    whole_score: PassDiagnostic

    @property
    def operations(self) -> tuple[OperationRecord, ...]:
        """Return the authoritative measure-pair and global operations."""
        return self.measure_operations + self.global_operations

    @property
    def omr_edit_distance(self) -> int:
        """Return the ordinary whole-score diagnostic distance."""
        return self.whole_score.omr_edit_distance

    @property
    def symbols_t(self) -> int:
        """Return the whole-score symbol count of ``T(Y)``."""
        return self.whole_score.symbols_t

    @property
    def symbols_y(self) -> int:
        """Return the whole-score symbol count of ``Y``."""
        return self.whole_score.symbols_y

    @property
    def normalized_distance(self) -> float:
        """Return the whole-score diagnostic's normalized distance."""
        return self.whole_score.normalized_distance


@dataclass(frozen=True, slots=True, eq=False)
class AnnotatedScore:
    """A parsed score and its annotation, with reprints removed.

    The music21 score is kept because annotated objects refer to it weakly.

    Attributes:
        score: The music21 score.
        annotated: The musicdiff annotated score.
        xml: The MusicXML events, directions and line spans of the score.
        events: The MusicXML event of each annotated note, by ``id``.
    """

    score: m21.stream.Score
    annotated: AnnScore
    xml: XmlScore
    events: dict[int, XmlEvent]


@dataclass(frozen=True, slots=True, eq=False)
class ContentResult:
    """The musicdiff part of the content pass over one file.

    Attributes:
        record: The retained raw figures.
        differences: Every assigned difference, counted or not.
        elements: The in-scope annotated elements of ``Y``.
        t_score: The annotated ``T(Y)``.
        y_score: The annotated ``Y``.
        pairings: Note pairs matched by pairing rules 1 and 2.
    """

    record: PassRecord
    differences: tuple[Difference, ...]
    elements: tuple[AnnNote | AnnExtra, ...]
    t_score: AnnotatedScore
    y_score: AnnotatedScore
    pairings: tuple[NotePair, ...]

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


@dataclass(frozen=True, slots=True, eq=False)
class LayoutElement:
    """One layout element, or one extra layout element.

    Attributes:
        obj: The annotated object, or the note that carries the item.
        item: The list and index of the attached item, if the element is an
            articulation, expression or tuplet group.
    """

    obj: _AnnObject
    item: _AttachedItem | None = None

    @property
    def key(self) -> tuple[int, _AttachedItem | None]:
        """The identity of the element within one annotated score."""
        return id(self.obj), self.item


@dataclass(frozen=True, slots=True, eq=False)
class LayoutResult:
    """The layout pass over one file.

    Attributes:
        record: The retained raw figures.
        differences: The ``layout`` differences.
        elements: The layout elements of ``Y``.
        non_identical: The layout elements that a ``layout`` difference
            touches.
        extras: The extra layout elements of ``T(Y)``.
    """

    record: PassRecord
    differences: tuple[Difference, ...]
    elements: tuple[LayoutElement, ...]
    non_identical: tuple[LayoutElement, ...]
    extras: tuple[LayoutElement, ...]

    @property
    def operations(self) -> tuple[OperationRecord, ...]:
        """Return the authoritative raw operations of the layout pass."""
        return self.record.operations

    @property
    def tally(self) -> ElementTally:
        """The element counts of the layout-fidelity score."""
        elements = len(self.elements)
        return ElementTally(
            elements,
            elements - len(self.non_identical),
            len(self.extras),
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

    Before annotation, ``words`` directions are read as the protocol's words
    reading specifies: repeat words stay text, and tempo words become tempo
    text. With ``Style``, an inverted fermata is read by its shape, which
    musicdiff 6.0 does not name. A fermata without a type is upright, as
    MusicXML defines it.

    Args:
        source: A ``Path`` to a MusicXML file, or MusicXML text as a
            ``str``. A ``str`` is always read as text, never as a path.
        detail: The musicdiff detail level.

    Returns:
        The parsed and annotated score.

    Raises:
        ScoreParseError: If music21 or musicdiff cannot parse the score.
        ProtocolDefectError: If the MusicXML structure has no protocol
            reading, an annotated note has no unique MusicXML event, or
            music21 reads an octave shift other than MusicXML does.
    """
    converter21.register()
    parsed, xml = _parse(source)
    words = _read_words(parsed, xml)
    if DetailLevel.includesStyle(detail):
        _read_fermata_shapes(parsed)
    try:
        annotated = AnnScore(parsed, detail)
    except ProtocolDefectError:
        raise
    # Any musicdiff failure on the score is a parse-failure status.
    except Exception as error:
        message = f"musicdiff cannot parse the score: {error}"
        raise ScoreParseError(message) from error
    _remove_reprints(annotated, parsed)
    events, notes = _bridge(annotated, xml)
    _check_octave_shifts(annotated, parsed, xml, events)
    reading = _WordsReading(words, parsed, detail)
    _remove_line_reprints(annotated, xml, notes, reading)
    return AnnotatedScore(parsed, annotated, xml, events)


def _parse(source: Path | str) -> tuple[m21.stream.Score, XmlScore]:
    try:
        data = source.read_bytes() if isinstance(source, Path) else source
        if isinstance(data, bytes) and data.startswith(_ZIP_MAGIC):
            # No protocol input is a container, and the fermata reading
            # below needs the root document's bytes.
            message = "a compressed MusicXML container is not a protocol input"
            raise ProtocolDefectError(message)  # noqa: TRY301
        typed = _upright_untyped_fermatas(data)
        if typed is None and isinstance(source, Path):
            parsed = m21.converter.parse(source, forceSource=True)
        else:
            parsed = m21.converter.parse(
                data if typed is None else typed,
                format="musicxml",
                forceSource=True,
            )
        if not isinstance(parsed, m21.stream.Score):
            message = "the source is not a single score"
            raise ScoreParseError(message)  # noqa: TRY301
        xml = read_score(source)
    except ScoreParseError, ProtocolDefectError:
        raise
    # Any music21 failure on the score is a parse-failure status.
    except Exception as error:
        message = f"musicdiff cannot parse the score: {error}"
        raise ScoreParseError(message) from error
    return parsed, xml


def _upright_untyped_fermatas(data: bytes | str) -> bytes | str | None:
    """Give each fermata without a type the type ``upright``.

    Returns:
        The MusicXML with the types added, or ``None`` when every fermata
        has a type.

    Raises:
        ProtocolDefectError: If a fermata without a type remains.
    """
    if not _has_untyped_fermata(data):
        return None
    if isinstance(data, bytes):
        typed = _UNTYPED_FERMATA_BYTES.sub(_UPRIGHT_FERMATA.encode(), data)
    else:
        typed = _UNTYPED_FERMATA.sub(_UPRIGHT_FERMATA, data)
    if _has_untyped_fermata(typed):
        message = "a fermata without a type cannot be read as upright"
        raise ProtocolDefectError(message)
    return typed


def _has_untyped_fermata(data: bytes | str) -> bool:
    if isinstance(data, bytes):
        if b"<fermata" not in data:
            return False
    elif "<fermata" not in data:
        return False
    root = _load_musicxml(data)
    return any(f.get("type") is None for f in root.iter("fermata"))


def _read_words(
    score: m21.stream.Score,
    xml: XmlScore,
) -> dict[int, tuple[m21.base.Music21Object, ...]]:
    """Read each ``words`` direction as the protocol's words reading does.

    music21 reads words such as "D.C." or "Fine" as repeat expressions,
    which musicdiff does not compare; each becomes the text expression it
    carries. The words of a tempo direction become one tempo text.

    Returns:
        The music21 objects that carry each direction's words, by the
        direction's document order.
    """
    read: dict[int, tuple[m21.base.Music21Object, ...]] = {}
    streams = list(score.parts)
    parts = {
        source: streams[index]
        for index, source in _source_staff_indices(
            [(stream, stream) for stream in streams],
        ).items()
    }
    # Each claimed object stays referenced, so its id stays unique.
    claimed: dict[int, m21.base.Music21Object] = {}
    for direction in xml.directions:
        words = direction.element.findall("direction-type/words")
        if not words:
            continue
        part = parts.get(direction.place)
        measures = (
            list(part.getElementsByClass(m21.stream.Measure))
            if part is not None
            else []
        )
        if direction.measure >= len(measures):
            message = (
                f"no music21 measure for a direction at {_site(direction)}"
            )
            raise ProtocolDefectError(message)
        measure = measures[direction.measure]
        expressions = [
            _claim_words(measure, direction, (word.text or "").strip(), claimed)
            for word in words
        ]
        text = "".join(word.text or "" for word in words).strip()
        if text and _is_tempo(direction.element, words):
            mark = _set_tempo(measure, direction, expressions, text, claimed)
            read[direction.order] = (mark,)
        else:
            read[direction.order] = tuple(expressions)
    return read


def _claim_words(
    measure: m21.stream.Measure,
    direction: XmlDirection,
    text: str,
    claimed: dict[int, m21.base.Music21Object],
) -> m21.expressions.TextExpression:
    candidates = list(
        measure.getElementsByClass(
            (m21.expressions.TextExpression, m21.repeat.RepeatExpression),
        ),
    )
    for candidate in candidates:
        if id(candidate) in claimed:
            continue
        if _fraction(candidate.offset) != direction.onset:
            continue
        if isinstance(candidate, m21.expressions.TextExpression):
            if candidate.content != text:
                continue
            expression = candidate
        else:
            expression = candidate.getTextExpression()
            if expression is None or expression.content != text:
                continue
            measure.replace(candidate, expression)
        claimed[id(expression)] = expression
        return expression
    message = f"no music21 text for {text!r} at {_site(direction)}"
    raise ProtocolDefectError(message)


def _site(direction: XmlDirection) -> str:
    return (
        f"part {direction.part + 1}, staff {direction.staff + 1}, "
        f"measure ordinal {direction.measure}, onset {direction.onset}"
    )


def _is_tempo(element: ET.Element, words: list[ET.Element]) -> bool:
    if element.find("direction-type/dashes") is not None:
        return False
    if element.find("direction-type/metronome") is not None:
        return True
    sound = element.find("sound")
    if sound is not None and sound.get("tempo") is not None:
        return True
    return all(word.get("font-weight") == "bold" for word in words)


def _set_tempo(
    measure: m21.stream.Measure,
    direction: XmlDirection,
    expressions: list[m21.expressions.TextExpression],
    text: str,
    claimed: dict[int, m21.base.Music21Object],
) -> m21.tempo.MetronomeMark:
    """Make the direction's words one tempo text of a metronome mark.

    music21 links a metronome mark to no direction, so the mark of a
    direction with a metronome is the one music21 metronome mark at its
    onset that no other direction claims.

    Returns:
        The metronome mark that carries the tempo text.

    Raises:
        ProtocolDefectError: If the direction's metronome has no music21
            metronome mark, or more than one mark could be its own.
    """
    carrier, *others = expressions
    for other in others:
        measure.remove(other)
    carrier.content = text
    if not carrier.hasStyleInformation:
        carrier.style = m21.style.TextStyle()
    # The carrier has a style, so the tempo text links it unchanged.
    tempo = m21.tempo.TempoText()
    tempo.setTextExpression(carrier)
    if direction.element.find("direction-type/metronome") is None:
        # musicdiff reads a text-only mark's text as content; music21's
        # implicit number from the text is not compared.
        mark = m21.tempo.MetronomeMark()
        mark.text = tempo
        mark.style = carrier.style
        mark.placement = carrier.placement
        measure.replace(carrier, mark)
        claimed[id(mark)] = mark
        return mark
    marks = [
        mark
        for mark in measure.getElementsByClass(m21.tempo.MetronomeMark)
        if id(mark) not in claimed and _fraction(mark.offset) == direction.onset
    ]
    if len(marks) != 1:
        count = "more than one" if marks else "no"
        message = f"{count} music21 metronome mark at {_site(direction)}"
        raise ProtocolDefectError(message)
    (mark,) = marks
    claimed[id(mark)] = mark
    mark.text = tempo
    measure.remove(carrier)
    return mark


class _InvertedFermata(m21.expressions.Fermata):
    """A music21 fermata that musicdiff names by its inverted shape."""

    @property
    def name(self) -> str:
        """The name that musicdiff records for the expression."""
        return _INVERTED_FERMATA


def _read_fermata_shapes(score: m21.stream.Score) -> None:
    """Name each inverted note fermata as the fermata reading specifies.

    musicdiff 6.0 records only a fermata's name, which music21 derives from
    its class, so an upright and an inverted fermata would compare equal.
    """
    for general_note in score.recurse().getElementsByClass(
        m21.note.GeneralNote,
    ):
        for expression in general_note.expressions:
            if (
                type(expression) is m21.expressions.Fermata
                and expression.type == "inverted"
            ):
                expression.__class__ = _InvertedFermata


def compare_layout(
    t_source: Path | str,
    y_source: Path | str,
) -> LayoutResult:
    """Run and score the layout pass.

    Args:
        t_source: The round trip ``T(Y)``.
        y_source: The reference ``Y``.

    Returns:
        The retained figures, ``layout`` differences and layout elements.

    Raises:
        ProtocolDefectError: If a difference cannot be assigned.
    """
    return compare_layout_annotated(
        annotate(t_source, LAYOUT_DETAIL),
        annotate(y_source, LAYOUT_DETAIL),
    )


def compare_layout_annotated(
    t_score: AnnotatedScore,
    y_score: AnnotatedScore,
) -> LayoutResult:
    """Run and score the layout pass on scores annotated with ``Style``.

    The layout pass assigns operations and pairs notes as the content pass
    does. A layout element is identical unless a difference whose only symbol
    is ``layout`` touches it, so layout carried by an inserted or deleted
    content object is touched by no ``layout`` difference.

    Args:
        t_score: The round trip ``T(Y)``, annotated with ``LAYOUT_DETAIL``.
        y_score: The reference ``Y``, annotated with ``LAYOUT_DETAIL``.

    Returns:
        The retained figures, ``layout`` differences and layout elements.

    Raises:
        ProtocolDefectError: If a difference cannot be assigned.
    """
    record, operations = _run_pass(t_score, y_score)
    locations = _locate(t_score) | _locate(y_score)
    differences: list[Difference] = []
    pending: _PendingChanges = {}
    for operation in operations:
        differences.extend(
            _assign(operation, record_of(operation, locations), pending),
        )
    differences, _ = _apply_pairing(differences, locations, None)
    layout = tuple(d for d in differences if d.symbols == (LAYOUT,))
    touched = {
        (id(d.y_object), d.y_item) for d in layout if d.y_object is not None
    }
    elements = tuple(_layout_elements(y_score.annotated))
    return LayoutResult(
        record,
        layout,
        elements,
        tuple(e for e in elements if e.key in touched),
        _layout_extras(differences),
    )


def compare_content(
    t_source: Path | str,
    y_source: Path | str,
    *,
    cross_staff_owned: Callable[[NotePair], bool] | None = None,
) -> ContentResult:
    """Run and score the musicdiff part of the content pass.

    Args:
        t_source: The round trip ``T(Y)``.
        y_source: The reference ``Y``.
        cross_staff_owned: Whether the cross-staff check has an element for
            the ``Y`` note or the ``T(Y)`` note of a pairing-rule-1 pair, in
            which case the pair's difference is omitted. The pairing remains
            available in the result for supplementary checks.

    Returns:
        The retained figures, assigned differences, elements and note pairs.

    Raises:
        ProtocolDefectError: If a difference cannot be assigned.
    """
    return compare_annotated(
        annotate(t_source),
        annotate(y_source),
        cross_staff_owned=cross_staff_owned,
    )


def compare_annotated(
    t_score: AnnotatedScore,
    y_score: AnnotatedScore,
    *,
    cross_staff_owned: Callable[[NotePair], bool] | None = None,
) -> ContentResult:
    """Run and score the musicdiff part of the content pass on annotations.

    Args:
        t_score: The annotated round trip ``T(Y)``.
        y_score: The annotated reference ``Y``.
        cross_staff_owned: As for :func:`compare_content`.

    Returns:
        The retained figures, assigned differences, elements and note pairs.

    Raises:
        ProtocolDefectError: If a difference cannot be assigned.
    """
    record, operations = _run_pass(t_score, y_score)
    locations = _locate(t_score) | _locate(y_score)
    differences: list[Difference] = []
    pending: _PendingChanges = {}
    for operation in operations:
        differences.extend(
            _assign(operation, record_of(operation, locations), pending),
        )
    differences, pairings = _apply_pairing(
        differences,
        locations,
        cross_staff_owned,
    )
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
        pairings,
    )


def heading_tally(
    t_source: Path | str,
    y_source: Path | str,
) -> ElementTally:
    """Score heading lines by their MusicXML reading order."""
    t_lines = _heading_lines(t_source)
    y_lines = _heading_lines(y_source)
    identical = _lcs_length(t_lines, y_lines)
    return ElementTally(len(y_lines), identical, len(t_lines) - identical)


def voice_order_tally(
    t_source: Path | str,
    y_source: Path | str,
) -> ElementTally:
    """Score canonical voice positions for content-matched voices.

    The check adds no extras. Voices whose content has no match in the other
    score remain the responsibility of musicdiff and the pairing rules.
    """
    return voice_order_annotated(annotate(t_source), annotate(y_source))


def voice_order_annotated(
    t_score: AnnotatedScore,
    y_score: AnnotatedScore,
) -> ElementTally:
    """Score canonical voice positions of two annotated scores.

    See :func:`voice_order_tally`.
    """
    return sum(
        voice_order_by_measure(t_score, y_score).values(),
        ElementTally(0, 0, 0),
    )


def voice_order_by_measure(
    t_score: AnnotatedScore,
    y_score: AnnotatedScore,
) -> dict[int, ElementTally]:
    """Score canonical voice positions per measure.

    Voices of equal content on one staff in one measure are matched one to
    one, matching as many equal position sequences as possible.

    Args:
        t_score: The annotated round trip ``T(Y)``.
        y_score: The annotated reference ``Y``.

    Returns:
        By 0-based measure ordinal, the voice-order elements of that measure.
    """
    t_groups = _voice_content_groups(t_score)
    y_groups = _voice_content_groups(y_score)
    tallies: dict[int, ElementTally] = {}
    for key, y_positions in y_groups.items():
        t_positions = t_groups.get(key)
        if t_positions is None:
            continue
        matched = min(sum(y_positions.values()), sum(t_positions.values()))
        identical = sum(
            min(y_positions[position], t_positions[position])
            for position in y_positions.keys() | t_positions.keys()
        )
        _, measure_index, _ = key
        tallies[measure_index] = tallies.get(
            measure_index,
            ElementTally(0, 0, 0),
        ) + ElementTally(matched, identical, 0)
    return tallies


def content_element_count(score: AnnotatedScore) -> int:
    """Count the in-scope elements of one score's musicdiff part.

    A failed file adds the supplementary elements of the same score; see
    :func:`sheet_music_omr.supplementary.failure_element_count`.

    Args:
        score: The annotated score, ``Y`` or ``X``.

    Returns:
        The number of in-scope annotated elements.
    """
    return sum(1 for _ in _elements(score.annotated))


def failure_layout_element_count(source: Path | str) -> int:
    """Count the layout elements of one score alone.

    A failed file counts every one of them as non-identical.

    Args:
        source: The score, ``Y`` or ``X``.

    Returns:
        The number of layout elements.
    """
    annotated = annotate(source, LAYOUT_DETAIL).annotated
    return sum(1 for _ in _layout_elements(annotated))


def extra_sites(
    score: AnnotatedScore,
) -> dict[int, tuple[tuple[int, int], int, Fraction | None]]:
    """Locate the annotated extras of a score in its MusicXML.

    Args:
        score: The annotated score.

    Returns:
        By ``id`` of each extra, its MusicXML part and 0-based staff, its
        measure ordinal and its onset in the measure, if it has one.

    Raises:
        ProtocolDefectError: If a measure has no source ordinal.
    """
    staff_keys = _musicxml_part_staff_indices(score.annotated)
    sites: dict[int, tuple[tuple[int, int], int, Fraction | None]] = {}
    for staff, part in enumerate(score.annotated.part_list):
        indexes = _measure_indexes(part.get_object())
        for bar in part.bar_list:
            measure_index = indexes.get(id(bar.get_object()))
            if measure_index is None:
                message = f"measure {bar.measureNumber} has no source index"
                raise ProtocolDefectError(message)
            for extra in bar.extras_list:
                offset = (
                    None if extra.offset is None else _fraction(extra.offset)
                )
                sites[id(extra)] = (staff_keys[staff], measure_index, offset)
    return sites


def _heading_lines(source: Path | str) -> tuple[str, ...]:
    data = source.read_bytes() if isinstance(source, Path) else source
    root = _load_musicxml(data)
    by_page: dict[
        int,
        list[tuple[int, tuple[float, float] | None, tuple[str, ...]]],
    ] = {}
    for order, credit in enumerate(root.findall("credit")):
        heading = _heading_credit(credit)
        if heading is not None:
            page, position, lines = heading
            by_page.setdefault(page, []).append((order, position, lines))

    result: list[str] = []
    for page in sorted(by_page):
        page_credits = by_page[page]
        if all(position is not None for _, position, _ in page_credits):
            page_credits.sort(key=_heading_position)
        else:
            page_credits.sort(key=lambda credit: credit[0])
        result.extend(line for _, _, lines in page_credits for line in lines)
    return tuple(result)


def _music21_pitch_key(pitch: m21.pitch.Pitch) -> PitchKey:
    if pitch.octave is None:
        message = f"paired note pitch has no octave: {pitch}"
        raise ProtocolDefectError(message)
    alter = pitch.accidental.alter if pitch.accidental else 0.0
    return str(pitch.step), _fraction(alter), pitch.octave


def _fraction(value: float | Fraction) -> Fraction:
    return value if isinstance(value, Fraction) else Fraction(str(value))


def _heading_credit(
    credit: ET.Element,
) -> tuple[int, tuple[float, float] | None, tuple[str, ...]] | None:
    if not _HEADING_ROLES.intersection(
        (element.text or "").strip()
        for element in credit.findall("credit-type")
    ):
        return None
    words = credit.findall("credit-words")
    if not words:
        return None
    try:
        page = int(credit.get("page", "1"))
    except ValueError as error:
        message = f"heading credit has an invalid page: {credit.get('page')!r}"
        raise ProtocolDefectError(message) from error
    if page < 1:
        message = f"heading credit has an invalid page: {page}"
        raise ProtocolDefectError(message)
    position = _credit_position(words[0])
    lines = tuple(line for word in words for line in _credit_words_lines(word))
    return page, position, lines


def _credit_words_lines(words: ET.Element) -> tuple[str, ...]:
    text = "".join(words.itertext())
    return tuple(
        " ".join(line.split()) for line in (text.splitlines() or [text])
    )


def _credit_position(words: ET.Element) -> tuple[float, float] | None:
    x_text = words.get("default-x")
    y_text = words.get("default-y")
    if x_text is None or y_text is None:
        return None
    try:
        x, y = float(x_text), float(y_text)
    except ValueError as error:
        message = "heading credit has a nonnumeric position"
        raise ProtocolDefectError(message) from error
    if not math.isfinite(x) or not math.isfinite(y):
        message = "heading credit has a non-finite position"
        raise ProtocolDefectError(message)
    return x, y


def _heading_position(
    credit: tuple[int, tuple[float, float] | None, tuple[str, ...]],
) -> tuple[float, float, int]:
    position = credit[1]
    if position is None:
        message = "a positioned heading credit has no coordinates"
        raise ProtocolDefectError(message)
    return -position[1], position[0], credit[0]


def _lcs_length(left: tuple[str, ...], right: tuple[str, ...]) -> int:
    row = [0] * (len(right) + 1)
    for left_item in left:
        next_row = [0]
        for index, right_item in enumerate(right):
            next_row.append(
                row[index] + 1
                if left_item == right_item
                else max(row[index + 1], next_row[-1])
            )
        row = next_row
    return row[-1]


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
    t_bars = tuple(_indexed_bars(part) for part in t_score.annotated.part_list)
    y_bars = tuple(_indexed_bars(part) for part in y_score.annotated.part_list)
    t_staff_keys = _musicxml_part_staff_indices(t_score.annotated)
    y_staff_keys = _musicxml_part_staff_indices(y_score.annotated)
    t_staff_indexes = {key: staff for staff, key in t_staff_keys.items()}
    y_staff_indexes = {key: staff for staff, key in y_staff_keys.items()}
    matched_staff_keys = t_staff_indexes.keys() & y_staff_indexes.keys()
    measure_operations: list[DiffOperation] = []
    measure_edit_cost = 0

    # Anchor each measure comparison so matching cannot shift repeated bars.
    for part_key in sorted(matched_staff_keys):
        t_part_index = t_staff_indexes[part_key]
        y_part_index = y_staff_indexes[part_key]
        for measure_index in sorted(
            t_bars[t_part_index].keys() | y_bars[y_part_index].keys(),
        ):
            t_bar = t_bars[t_part_index].get(measure_index)
            y_bar = y_bars[y_part_index].get(measure_index)
            t_fragment = _single_part_score(
                t_score.annotated,
                _part_slice(
                    t_score.annotated.part_list[t_part_index],
                    [] if t_bar is None else [t_bar],
                ),
            )
            y_fragment = _single_part_score(
                y_score.annotated,
                _part_slice(
                    y_score.annotated.part_list[y_part_index],
                    [] if y_bar is None else [y_bar],
                ),
            )
            pair_operations, pair_cost = Comparison.annotated_scores_diff(
                t_fragment,
                y_fragment,
            )
            measure_operations.extend(pair_operations)
            measure_edit_cost += pair_cost

    t_global, y_global = _global_score_slices(
        t_score.annotated,
        y_score.annotated,
        t_staff_keys,
        y_staff_keys,
        matched_staff_keys,
    )
    global_operations, global_cost = Comparison.annotated_scores_diff(
        t_global,
        y_global,
    )
    whole_operations, whole_cost = Comparison.annotated_scores_diff(
        t_score.annotated,
        y_score.annotated,
    )

    locations = _locate(t_score) | _locate(y_score)
    symbols_t = t_score.annotated.notation_size()
    symbols_y = y_score.annotated.notation_size()
    whole_record = PassDiagnostic(
        tuple(record_of(op, locations) for op in whole_operations),
        whole_cost,
        symbols_t,
        symbols_y,
    )
    record = PassRecord(
        tuple(record_of(op, locations) for op in measure_operations),
        measure_edit_cost,
        tuple(record_of(op, locations) for op in global_operations),
        global_cost,
        whole_record,
    )
    return record, measure_operations + global_operations


def _indexed_bars(part: AnnPart) -> dict[int, AnnMeasure]:
    indexes = _measure_indexes(part.get_object())
    bars: dict[int, AnnMeasure] = {}
    for bar in part.bar_list:
        measure = bar.get_object()
        if not isinstance(measure, m21.stream.Measure):
            message = f"annotated bar without a music21 measure: {bar}"
            raise ProtocolDefectError(message)
        index = indexes.get(id(measure))
        if index is None or index in bars:
            message = f"cannot assign a unique source measure index: {bar}"
            raise ProtocolDefectError(message)
        bars[index] = bar
    return bars


def _part_slice(part: AnnPart, bars: list[AnnMeasure]) -> AnnPart:
    sliced: AnnPart = copy(part)
    sliced.bar_list = bars
    sliced.n_of_bars = len(bars)
    sliced.precomputed_str = hash(str(sliced))
    _reset_size(sliced)
    return sliced


def _single_part_score(score: AnnScore, part: AnnPart) -> AnnScore:
    sliced: AnnScore = copy(score)
    sliced.part_list = [part]
    sliced.n_of_parts = 1
    sliced.staff_group_list = []
    sliced.metadata_items_list = []
    sliced.num_syntax_errors_fixed = 0
    _reset_size(sliced)
    return sliced


def _global_score_slices(
    t_score: AnnScore,
    y_score: AnnScore,
    t_staff_keys: dict[int, tuple[int, int]],
    y_staff_keys: dict[int, tuple[int, int]],
    matched_staff_keys: set[tuple[int, int]],
) -> tuple[AnnScore, AnnScore]:
    t_staff_indexes = {key: staff for staff, key in t_staff_keys.items()}
    y_staff_indexes = {key: staff for staff, key in y_staff_keys.items()}
    staff_keys = sorted(t_staff_indexes.keys() | y_staff_indexes.keys())

    def global_parts(
        score: AnnScore,
        own_indexes: dict[tuple[int, int], int],
        other_score: AnnScore,
        other_indexes: dict[tuple[int, int], int],
    ) -> list[AnnPart]:
        parts: list[AnnPart] = []
        for key in staff_keys:
            own_index = own_indexes.get(key)
            if own_index is not None:
                part = score.part_list[own_index]
                bars = [] if key in matched_staff_keys else part.bar_list
            else:
                part = other_score.part_list[other_indexes[key]]
                bars = []
            parts.append(_part_slice(part, bars))
        return parts

    def global_score(score: AnnScore, parts: list[AnnPart]) -> AnnScore:
        sliced: AnnScore = copy(score)
        sliced.part_list = parts
        sliced.n_of_parts = len(parts)
        sliced.num_syntax_errors_fixed = 0
        _reset_size(sliced)
        return sliced

    return (
        global_score(
            t_score,
            global_parts(t_score, t_staff_indexes, y_score, y_staff_indexes),
        ),
        global_score(
            y_score,
            global_parts(y_score, y_staff_indexes, t_score, t_staff_indexes),
        ),
    )


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


def _canonical_voice_positions(
    bar: AnnMeasure,
    *,
    voices: list[AnnVoice] | None = None,
    direction_overrides: dict[int, str | None] | None = None,
    event_onsets: dict[int, Fraction] | None = None,
) -> dict[int, int]:
    voices = bar.voices_list if voices is None else voices
    _reject_double_stem_events(voices, bar)
    if len(voices) <= _SINGLE_VOICE_POSITION:
        return _single_voice_event_positions(voices)

    measure = bar.get_object()
    if not isinstance(measure, m21.stream.Measure):
        message = f"annotated bar without a music21 measure: {bar}"
        raise ProtocolDefectError(message)
    if not _has_overlapping_voices(voices, measure, event_onsets):
        return _single_voice_event_positions(voices)
    if len(voices) > _MAX_CANONICAL_VOICES:
        message = f"measure {bar.measureNumber} has more than four voices"
        raise ProtocolDefectError(message)
    position_context = _MeasureEventContext(measure, bar, event_onsets)
    if len(voices) == _TWO_VOICE_COUNT:
        return _two_voice_event_positions(
            voices,
            position_context,
            direction_overrides,
        )

    by_direction = _events_by_stem_direction(
        voices,
        position_context,
        direction_overrides,
    )
    stem_counts = (len(by_direction["up"]), len(by_direction["down"]))
    if stem_counts not in _VALID_STEM_COUNTS:
        message = (
            f"measure {bar.measureNumber} has stem directions that do not "
            "establish two to four voices"
        )
        raise ProtocolDefectError(message)
    return _rank_stem_voices(by_direction, bar.measureNumber)


def _reject_double_stem_events(
    voices: list[AnnVoice],
    bar: AnnMeasure,
) -> None:
    if any(
        _stem_direction(note) == "double"
        for voice in voices
        for note in voice.annot_notes
    ):
        message = (
            f"measure {bar.measureNumber} has a double-stem event "
            "whose two voice positions cannot be represented"
        )
        raise ProtocolDefectError(message)


def _single_voice_event_positions(voices: list[AnnVoice]) -> dict[int, int]:
    return {
        id(note): _SINGLE_VOICE_POSITION
        for voice in voices
        for note in voice.annot_notes
    }


def _two_voice_event_positions(
    voices: list[AnnVoice],
    context: _MeasureEventContext,
    direction_overrides: dict[int, str | None] | None = None,
) -> dict[int, int]:
    positions: dict[int, int] = {}
    seen_directions: set[str] = set()
    voice_directions = [
        _voice_event_directions(voice, context.bar, direction_overrides)
        for voice in voices
    ]
    for index, (voice, directions) in enumerate(
        zip(voices, voice_directions, strict=True),
    ):
        stemless_direction = _stemless_voice_direction(
            directions,
            voice_directions[1 - index],
        )
        for note in voice.annot_notes:
            direction = directions[id(note)] or stemless_direction
            if direction is None:
                direction = _infer_voice_direction(
                    note,
                    voice,
                    directions,
                    context,
                )
            seen_directions.add(direction)
            positions[id(note)] = 1 if direction == "up" else 2
    if seen_directions != {"up", "down"}:
        message = (
            f"measure {context.bar.measureNumber} has overlapping voices that "
            "stem direction cannot distinguish"
        )
        raise ProtocolDefectError(message)
    return positions


def _stemless_voice_direction(
    directions: dict[int, str | None],
    other_directions: dict[int, str | None],
) -> str | None:
    """Return the direction of a voice without a stemmed event.

    Of two overlapping voices, one is voice 1 and the other voice 2, so a
    voice whose stemless events complete its own rhythm takes the position
    opposite to the other voice when that voice has one stem direction.
    """
    if any(direction is not None for direction in directions.values()):
        return None
    other = {
        direction
        for direction in other_directions.values()
        if direction is not None
    }
    if len(other) != 1:
        return None
    return "down" if other == {"up"} else "up"


def _events_by_stem_direction(
    voices: list[AnnVoice],
    context: _MeasureEventContext,
    direction_overrides: dict[int, str | None] | None = None,
) -> dict[str, list[tuple[list[AnnNote], int]]]:
    by_direction: dict[str, list[tuple[list[AnnNote], int]]] = {
        "up": [],
        "down": [],
    }
    for voice in voices:
        directions = _voice_event_directions(
            voice,
            context.bar,
            direction_overrides,
        )
        explicit_directions = {
            direction
            for direction in directions.values()
            if direction is not None
        }
        if len(explicit_directions) > 1:
            message = (
                f"measure {context.bar.measureNumber} has a mixed-stem voice "
                "whose position among three or four voices is ambiguous"
            )
            raise ProtocolDefectError(message)
        notes_by_direction: dict[str, list[AnnNote]] = {"up": [], "down": []}
        for note in voice.annot_notes:
            direction = directions[id(note)]
            if direction is None:
                direction = _infer_voice_direction(
                    note,
                    voice,
                    directions,
                    context,
                )
            notes_by_direction[direction].append(note)
        for direction, notes in notes_by_direction.items():
            if notes:
                by_direction[direction].append(
                    (notes, _voice_height(notes, context.bar)),
                )
    return by_direction


def _voice_event_directions(
    voice: AnnVoice,
    bar: AnnMeasure,
    direction_overrides: dict[int, str | None] | None = None,
) -> dict[int, str | None]:
    directions: dict[int, str | None] = {}
    for note in voice.annot_notes:
        direction = (
            direction_overrides.get(id(note))
            if direction_overrides is not None
            else None
        ) or _stem_direction(note)
        directions[id(note)] = (
            direction if direction in {"up", "down"} else None
        )

    for notes, complete in _beam_groups(voice, bar, direction_overrides):
        beam_directions = [
            directions[id(note)]
            for note in notes
            if directions[id(note)] is not None
        ]
        if len(set(beam_directions)) > 1:
            if not complete:
                message = (
                    f"measure {bar.measureNumber} has a mixed-stem beam "
                    "group whose first event is outside the measure"
                )
                raise ProtocolDefectError(message)
            direction = beam_directions[0]
            for note in notes:
                directions[id(note)] = direction
    return directions


def _beam_groups(
    voice: AnnVoice,
    bar: AnnMeasure,
    direction_overrides: dict[int, str | None] | None = None,
) -> list[tuple[list[AnnNote], bool]]:
    groups: list[tuple[list[AnnNote], bool]] = []
    active: list[AnnNote] | None = None
    complete = False
    for note in voice.annot_notes:
        if note.graceness or (
            direction_overrides is not None and id(note) in direction_overrides
        ):
            continue
        beam_type = _primary_beam_type(note)
        if beam_type == "start":
            if active is not None:
                message = (
                    f"measure {bar.measureNumber} has nested "
                    "primary beam groups"
                )
                raise ProtocolDefectError(message)
            active = [note]
            complete = True
            continue
        if beam_type in {"continue", "stop"}:
            if active is None:
                active = [note]
                complete = False
            else:
                active.append(note)
            if beam_type == "stop":
                groups.append((active, complete))
                active = None
            continue
        if active is not None:
            message = (
                f"measure {bar.measureNumber} has a primary beam group "
                "interrupted before its stop"
            )
            raise ProtocolDefectError(message)
    if active is not None:
        groups.append((active, False))
    return groups


def _primary_beam_type(note: AnnNote) -> str | None:
    if not note.beamings:
        return None
    return note.beamings[0]


def _musicxml_part_key(part: AnnPart, staff: int) -> tuple[str, str | int]:
    return _stream_part_key(part.get_object(), staff, part)


def _stream_part_key(
    musicxml_part: object,
    staff: int,
    label: object,
) -> tuple[str, str | int]:
    if not isinstance(musicxml_part, m21.stream.Stream):
        message = f"annotated part without a music21 stream: {label}"
        raise ProtocolDefectError(message)
    if not isinstance(musicxml_part, m21.stream.PartStaff):
        return ("staff", staff)
    if musicxml_part.id is None:
        message = f"music21 part staff has no source id: {label}"
        raise ProtocolDefectError(message)
    match = re.fullmatch(r"(.+)-Staff\d+", str(musicxml_part.id))
    if match is None:
        message = f"music21 part staff has no source part id: {label}"
        raise ProtocolDefectError(message)
    return ("part", match.group(1))


def _musicxml_part_indices(score: AnnScore) -> dict[int, int]:
    return {
        staff: part_index
        for staff, (part_index, _) in _musicxml_part_staff_indices(
            score
        ).items()
    }


def _musicxml_part_staff_indices(
    score: AnnScore,
) -> dict[int, tuple[int, int]]:
    return _source_staff_indices(
        [(part.get_object(), part) for part in score.part_list],
    )


def _source_staff_indices(
    parts: list[tuple[object, object]],
) -> dict[int, tuple[int, int]]:
    indexes: dict[int, tuple[int, int]] = {}
    source_parts: dict[tuple[str, str | int], int] = {}
    source_staves: set[tuple[int, int]] = set()
    for staff, (musicxml_part, label) in enumerate(parts):
        part_key = _stream_part_key(musicxml_part, staff, label)
        if part_key not in source_parts:
            source_parts[part_key] = len(source_parts)
        if isinstance(musicxml_part, m21.stream.PartStaff):
            match = re.fullmatch(r".+-Staff(\d+)", str(musicxml_part.id))
            if match is None:
                message = (
                    f"music21 part staff has no source staff index: {label}"
                )
                raise ProtocolDefectError(message)
            source_staff = int(match.group(1)) - 1
        else:
            source_staff = 0
        source_key = (source_parts[part_key], source_staff)
        if source_key in source_staves:
            message = f"duplicate MusicXML source staff identity: {source_key}"
            raise ProtocolDefectError(message)
        source_staves.add(source_key)
        indexes[staff] = source_key
    return indexes


def _source_primary_beam_type(note: AnnNote) -> str | None:
    beams = getattr(_general_note(note), "beams", None)
    if beams is None:
        return note.beamings[0] if note.beamings else None
    return beams.getByNumber(1).type if 1 in beams.getNumbers() else None


def _score_voice_events(
    annotated: AnnotatedScore,
) -> dict[tuple[tuple[str, str | int], int], list[_ScoreVoiceEvent]]:
    events_by_measure: dict[
        tuple[tuple[str, str | int], int],
        list[_ScoreVoiceEvent],
    ] = {}
    score = annotated.annotated
    source_staff_indexes = _musicxml_part_staff_indices(score)
    for staff, part in enumerate(score.part_list):
        part_key = _musicxml_part_key(part, staff)
        measure_indexes = _measure_indexes(part.get_object())
        for bar in part.bar_list:
            measure = bar.get_object()
            if not isinstance(measure, m21.stream.Measure):
                message = f"annotated bar without a music21 measure: {bar}"
                raise ProtocolDefectError(message)
            measure_index = measure_indexes.get(id(measure))
            if measure_index is None:
                message = f"measure {bar.measureNumber} has no source index"
                raise ProtocolDefectError(message)
            for voice in bar.voices_list:
                m21_voice = voice.get_object()
                if isinstance(m21_voice, m21.stream.Voice):
                    parent_voice_id = (
                        str(m21_voice.id) if m21_voice.id is not None else None
                    )
                elif isinstance(m21_voice, m21.stream.Measure):
                    parent_voice_id = None
                else:
                    continue
                events = events_by_measure.setdefault(
                    (part_key, measure_index),
                    [],
                )
                for note in voice.annot_notes:
                    onset = _fraction(
                        _general_note(note).getOffsetInHierarchy(measure),
                    )
                    voice_id = parent_voice_id or _source_voice_id(note)
                    events.append(
                        _ScoreVoiceEvent(
                            onset,
                            _event_order(annotated.events[id(note)]),
                            staff,
                            source_staff_indexes[staff],
                            note,
                            voice_id,
                        ),
                    )
    return events_by_measure


def _source_voice_id(note: AnnNote) -> str | None:
    voice_ids = {
        str(site.id)
        for site in _general_note(note).sites.get()
        if isinstance(site, m21.stream.Voice) and site.id is not None
    }
    if len(voice_ids) > 1:
        message = f"note has multiple music21 voice sites: {note}"
        raise ProtocolDefectError(message)
    return next(iter(voice_ids), None)


def _cross_staff_beam_groups(
    annotated: AnnotatedScore,
) -> list[_CrossStaffBeamGroup]:
    return [
        group
        for (_, measure_index), events in _score_voice_events(
            annotated,
        ).items()
        for group in _cross_staff_beams_in_measure(
            events,
            measure_index,
        )
    ]


def _cross_staff_beams_in_measure(
    events: list[_ScoreVoiceEvent],
    measure_index: int,
) -> list[_CrossStaffBeamGroup]:
    events.sort(key=lambda event: (event.onset, event.order))
    tracker = _CrossStaffBeamTracker(measure_index)
    for event in events:
        tracker.add(event)
    return tracker.groups


def _cross_staff_voice_id(event: _ScoreVoiceEvent) -> str | tuple[str, int]:
    return (
        event.voice_id if event.voice_id is not None else ("staff", event.staff)
    )


def _completed_cross_staff_beam(
    events: list[_ScoreVoiceEvent] | None,
    measure_index: int,
    voice_id: str | tuple[str, int],
    *,
    complete: bool,
) -> list[_CrossStaffBeamGroup]:
    if events is None or not complete:
        return []
    group = _make_cross_staff_beam_group(events, measure_index, voice_id)
    return [group] if group is not None else []


def _make_cross_staff_beam_group(
    events: list[_ScoreVoiceEvent],
    measure_index: int,
    voice_id: str | tuple[str, int],
) -> _CrossStaffBeamGroup | None:
    group_events = tuple((event.staff, event.note) for event in events)
    if len({staff for staff, _ in group_events}) <= 1:
        return None
    non_grace = [
        (staff, note) for staff, note in group_events if not note.graceness
    ]
    staff_counts = Counter(staff for staff, _ in non_grace)
    if staff_counts:
        max_count = max(staff_counts.values())
        home_staff = next(
            staff for staff, _ in non_grace if staff_counts[staff] == max_count
        )
        home_note = next(
            note for staff, note in non_grace if staff == home_staff
        )
    else:
        home_staff, home_note = group_events[0]
    direction = _stem_direction(home_note)
    return _CrossStaffBeamGroup(
        home_staff,
        measure_index,
        voice_id,
        group_events,
        tuple((id(event.note), event.onset) for event in events),
        home_note,
        direction if direction in {"up", "down"} else None,
    )


def _filtered_voice(
    voice: AnnVoice,
    context: _StaffVoiceContext,
) -> AnnVoice | None:
    retained = [
        note
        for note in voice.annot_notes
        if (
            (group := context.groups_by_note.get(id(note))) is None
            or group.home_staff == context.staff
        )
    ]
    voice_note_ids = {id(note) for note in voice.annot_notes}
    matching_groups: dict[int, _CrossStaffBeamGroup] = {}
    for note in voice.annot_notes:
        group = context.groups_by_note.get(id(note))
        if (
            group is not None
            and group.home_staff == context.staff
            and group.measure_index == context.measure_index
        ):
            matching_groups[id(group)] = group
    retained_ids = {id(note) for note in retained}
    added_foreign_event = False
    for group in matching_groups.values():
        for _, note in group.events:
            if id(note) not in voice_note_ids and id(note) not in retained_ids:
                retained.append(note)
                retained_ids.add(id(note))
                added_foreign_event = True
    if not retained:
        return None
    if added_foreign_event:
        retained.sort(
            key=lambda note: _event_onset(
                note,
                context.measure_events.measure,
                context.measure_events.event_onsets,
            ),
        )
    if [id(note) for note in retained] == [
        id(note) for note in voice.annot_notes
    ]:
        return voice
    filtered_voice = copy(voice)
    filtered_voice.annot_notes = retained
    return filtered_voice


def _beam_direction_overrides(
    voice: AnnVoice,
    context: _StaffVoiceContext,
) -> dict[int, str | None]:
    return {
        id(note): group.home_direction
        for note in voice.annot_notes
        if (group := context.groups_by_note.get(id(note))) is not None
        and group.home_staff == context.staff
        and group.measure_index == context.measure_index
    }


def _score_voice_positions(
    annotated: AnnotatedScore,
) -> tuple[dict[int, int], dict[int, _CrossStaffBeamGroup]]:
    groups = _cross_staff_beam_groups(annotated)
    groups_by_note = {
        id(note): group for group in groups for _, note in group.events
    }
    event_onsets = {
        note_id: onset
        for group in groups
        for note_id, onset in group.event_onsets
    }
    positions: dict[int, int] = {}
    for staff, part in enumerate(annotated.annotated.part_list):
        measure_indexes = _measure_indexes(part.get_object())
        for bar in part.bar_list:
            measure = bar.get_object()
            if not isinstance(measure, m21.stream.Measure):
                message = f"annotated bar without a music21 measure: {bar}"
                raise ProtocolDefectError(message)
            measure_index = measure_indexes.get(id(measure))
            if measure_index is None:
                message = f"measure {bar.measureNumber} has no source index"
                raise ProtocolDefectError(message)
            voice_context = _StaffVoiceContext(
                staff,
                measure_index,
                groups_by_note,
                _MeasureEventContext(measure, bar, event_onsets),
            )
            voices: list[AnnVoice] = []
            direction_overrides: dict[int, str | None] = {}
            for voice in bar.voices_list:
                filtered = _filtered_voice(voice, voice_context)
                if filtered is not None:
                    voices.append(filtered)
                    direction_overrides.update(
                        _beam_direction_overrides(filtered, voice_context),
                    )
            positions.update(
                _canonical_voice_positions(
                    bar,
                    voices=voices,
                    direction_overrides=direction_overrides,
                    event_onsets=event_onsets,
                ),
            )

    for group in groups:
        anchor_position = positions.get(id(group.home_note))
        if anchor_position is None:
            message = (
                f"cross-staff beam in measure index {group.measure_index} "
                "has no canonical position on its home staff"
            )
            raise ProtocolDefectError(message)
        for _, note in group.events:
            positions[id(note)] = anchor_position
    return positions, groups_by_note


def _infer_voice_direction(
    note: AnnNote,
    voice: AnnVoice,
    directions: dict[int, str | None],
    context: _MeasureEventContext,
) -> str:
    onset = _event_onset(note, context.measure, context.event_onsets)
    candidates = [
        (
            abs(
                _event_onset(
                    other,
                    context.measure,
                    context.event_onsets,
                )
                - onset
            ),
            direction,
        )
        for other in voice.annot_notes
        if (direction := directions[id(other)]) is not None
    ]
    if not candidates:
        message = (
            f"overlapping voice in measure {context.bar.measureNumber} has an "
            "unstemmed event with no stemmed event to establish its position"
        )
        raise ProtocolDefectError(message)
    distance = min(candidate[0] for candidate in candidates)
    nearest_directions = {
        direction
        for candidate_distance, direction in candidates
        if candidate_distance == distance
    }
    if len(nearest_directions) != 1:
        message = (
            f"overlapping voice in measure {context.bar.measureNumber} has an "
            "unstemmed event with ambiguous voice position"
        )
        raise ProtocolDefectError(message)
    return nearest_directions.pop()


def _has_overlapping_voices(
    voices: list[AnnVoice],
    measure: m21.stream.Measure,
    event_onsets: dict[int, Fraction] | None = None,
) -> bool:
    intervals: list[tuple[int, Fraction, Fraction]] = []
    for voice_index, voice in enumerate(voices):
        for note in voice.annot_notes:
            if note.graceness:
                continue
            general_note = _general_note(note)
            duration = _fraction(general_note.duration.quarterLength)
            if duration <= 0:
                continue
            onset = _event_onset(note, measure, event_onsets)
            intervals.append((voice_index, onset, onset + duration))
    return any(
        left_voice != right_voice
        and max(left_start, right_start) < min(left_end, right_end)
        for index, (left_voice, left_start, left_end) in enumerate(intervals)
        for right_voice, right_start, right_end in intervals[index + 1 :]
    )


def _event_onset(
    note: AnnNote,
    measure: m21.stream.Measure,
    event_onsets: dict[int, Fraction] | None,
) -> Fraction:
    if event_onsets is not None and id(note) in event_onsets:
        return event_onsets[id(note)]
    return _fraction(_general_note(note).getOffsetInHierarchy(measure))


def _voice_height(
    notes: list[AnnNote],
    bar: AnnMeasure,
) -> int:
    pitches: list[int] = []
    for note in notes:
        general_note = _general_note(note)
        if isinstance(general_note, m21.note.Note):
            note_pitches = (general_note.pitch,)
        elif isinstance(general_note, m21.chord.Chord):
            note_pitches = general_note.pitches
        else:
            continue
        pitches.extend(
            "CDEFGAB".index(pitch.step) + 7 * pitch.octave
            for pitch in note_pitches
            if pitch.step is not None and pitch.octave is not None
        )
    if not pitches:
        message = (
            f"overlapping voice in measure {bar.measureNumber} has no pitch "
            "to establish its vertical order"
        )
        raise ProtocolDefectError(message)
    return max(pitches)


def _stem_direction(note: AnnNote) -> str:
    general_note = _general_note(note)
    if isinstance(general_note, m21.chord.Chord):
        return _chord_stem_direction(general_note)
    if isinstance(general_note, m21.note.Note):
        return general_note.stemDirection
    return "unspecified"


def _chord_stem_direction(chord: m21.chord.Chord) -> str:
    """Return a chord's stem, which music21 imports onto its notes."""
    if chord.stemDirection != "unspecified":
        return chord.stemDirection
    directions = {component.stemDirection for component in chord.notes} - {
        "unspecified"
    }
    if len(directions) > 1:
        message = (
            f"chord in measure {chord.measureNumber} has notes with different "
            "stem directions"
        )
        raise ProtocolDefectError(message)
    return directions.pop() if directions else "unspecified"


def _rank_stem_voices(
    by_direction: dict[str, list[tuple[list[AnnNote], int]]],
    measure_number: str,
) -> dict[int, int]:
    positions: dict[int, int] = {}
    for direction, first_position in (("up", 1), ("down", 2)):
        ranked = by_direction[direction]
        if len(ranked) > _MAX_VOICES_PER_STEM:
            message = (
                f"measure {measure_number} has more than two "
                f"{direction}-stem voices"
            )
            raise ProtocolDefectError(message)
        ranked.sort(key=lambda pair: pair[1], reverse=True)
        if len(ranked) == _MAX_VOICES_PER_STEM and ranked[0][1] == ranked[1][1]:
            message = (
                f"measure {measure_number} has {direction}-stem voices "
                "with an ambiguous vertical order"
            )
            raise ProtocolDefectError(message)
        for index, (notes, _height) in enumerate(ranked):
            position = first_position + _VOICE_POSITION_STEP * index
            for note in notes:
                positions[id(note)] = position
    return positions


def _voice_content_groups(
    annotated: AnnotatedScore,
) -> dict[
    tuple[tuple[int, int], int, tuple[object, ...]], Counter[tuple[int, ...]]
]:
    groups: dict[
        tuple[tuple[int, int], int, tuple[object, ...]],
        Counter[tuple[int, ...]],
    ] = {}
    positions, _ = _score_voice_positions(annotated)
    source_staff_indexes = _musicxml_part_staff_indices(annotated.annotated)
    flattened_staff_indexes = {
        source_staff: staff
        for staff, source_staff in source_staff_indexes.items()
    }
    for (source_staff, measure_index, _identity), events in _measure_voices(
        annotated,
    ).items():
        staff = flattened_staff_indexes[source_staff]
        signature = tuple(
            _pairing_key(
                note,
                Location(
                    staff,
                    measure_index,
                    measure_number,
                    onset=onset,
                ),
            )
            for onset, _, note, _, measure_number in events
        )
        key = (source_staff, measure_index, signature)
        position_sequence = tuple(
            positions[id(note)] for _, _, note, _, _ in events
        )
        groups.setdefault(key, Counter())[position_sequence] += 1
    return groups


def voice_element_count(annotated: AnnotatedScore) -> int:
    """Count the voices of each measure without their canonical positions.

    A failed file counts each voice of each measure as an element, which
    needs no counterpart and so no position to agree on.

    Args:
        annotated: The annotated score, ``Y`` or ``X``.

    Returns:
        The number of voices, by staff and measure, that hold an event.
    """
    return len(_measure_voices(annotated))


def _measure_voices(
    annotated: AnnotatedScore,
) -> dict[
    tuple[tuple[int, int], int, VoiceKey],
    list[tuple[Fraction, XmlEvent, AnnNote, m21.stream.Measure, str]],
]:
    """Return each voice's events by MusicXML staff and measure ordinal."""
    source_staff_indexes = _musicxml_part_staff_indices(annotated.annotated)
    voice_staves_by_event = voice_staves(annotated.xml.events)
    voices: dict[
        tuple[tuple[int, int], int, VoiceKey],
        list[tuple[Fraction, XmlEvent, AnnNote, m21.stream.Measure, str]],
    ] = {}
    for staff, part in enumerate(annotated.annotated.part_list):
        indexes = _measure_indexes(part.get_object())
        source_part, _ = source_staff_indexes[staff]
        for bar in part.bar_list:
            measure = bar.get_object()
            if not isinstance(measure, m21.stream.Measure):
                message = f"annotated bar without a music21 measure: {bar}"
                raise ProtocolDefectError(message)
            measure_index = indexes.get(id(measure))
            if measure_index is None:
                message = f"measure {bar.measureNumber} has no source index"
                raise ProtocolDefectError(message)
            for voice in bar.voices_list:
                for note in voice.annot_notes:
                    event = annotated.events[id(note)]
                    voice_key, voice_staff = voice_staves_by_event[id(event)]
                    onset = _fraction(
                        _general_note(note).getOffsetInHierarchy(measure),
                    )
                    voices.setdefault(
                        ((source_part, voice_staff), measure_index, voice_key),
                        [],
                    ).append(
                        (
                            onset,
                            event,
                            note,
                            measure,
                            bar.measureNumber,
                        ),
                    )
    for events in voices.values():
        # A voice's events can lie on two staves, so they follow the written
        # order, which does not depend on the staff that displays them.
        events.sort(key=lambda event: _event_order(event[1]))
    return voices


def _locate(annotated: AnnotatedScore) -> dict[int, Location]:
    locations: dict[int, Location] = {}
    note_positions, _ = _score_voice_positions(annotated)
    score = annotated.annotated
    part_indexes = _musicxml_part_indices(score)
    for staff, part in enumerate(score.part_list):
        part_index = part_indexes[staff]
        locations[id(part)] = Location(staff, part_index=part_index)
        indexes = _measure_indexes(part.get_object())
        for bar in part.bar_list:
            measure = bar.get_object()
            if not isinstance(measure, m21.stream.Measure):
                message = f"annotated bar without a music21 measure: {bar}"
                raise ProtocolDefectError(message)
            index = indexes.get(id(measure))
            locations[id(bar)] = Location(
                staff,
                index,
                bar.measureNumber,
                part_index=part_index,
            )
            for voice_index, voice in enumerate(bar.voices_list):
                m21_voice = voice.get_object()
                voice_id = (
                    str(m21_voice.id)
                    if isinstance(m21_voice, m21.stream.Voice)
                    else None
                )
                positions = {
                    note_positions[id(note)] for note in voice.annot_notes
                }
                locations[id(voice)] = Location(
                    staff,
                    index,
                    bar.measureNumber,
                    voice_index,
                    voice_id,
                    voice_position=(
                        next(iter(positions)) if len(positions) == 1 else None
                    ),
                    part_index=part_index,
                )
                for note in voice.annot_notes:
                    onset = _general_note(note).getOffsetInHierarchy(measure)
                    grace_position = annotated.events[id(note)].grace_position
                    locations[id(note)] = Location(
                        staff,
                        index,
                        bar.measureNumber,
                        voice_index,
                        voice_id,
                        onset,
                        note_positions[id(note)],
                        part_index,
                        grace_position,
                    )
            for extra in bar.extras_list:
                locations[id(extra)] = Location(
                    staff,
                    index,
                    bar.measureNumber,
                    onset=extra.offset,
                    part_index=part_index,
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


def _layout_elements(score: AnnScore) -> Iterator[LayoutElement]:
    for part in score.part_list:
        for bar in part.bar_list:
            for voice in bar.voices_list:
                for note in voice.annot_notes:
                    yield from _object_layout_elements(note)
            for extra in bar.extras_list:
                yield from _object_layout_elements(extra)
    for group in score.staff_group_list:
        yield from _object_layout_elements(group)


def _object_layout_elements(obj: _AnnObject) -> Iterator[LayoutElement]:
    """Yield an object's own layout element and its attached ones."""
    if _carries_layout(obj):
        yield LayoutElement(obj)
    if isinstance(obj, AnnNote):
        for source in (_ARTICULATIONS, _EXPRESSIONS, _TUPLET_INFO):
            for index, item in enumerate(getattr(obj, source)):
                if _item_carries_layout(source, item):
                    yield LayoutElement(obj, (source, index))


def _carries_layout(obj: _AnnObject) -> bool:
    """Whether an object itself carries a layout value."""
    if isinstance(obj, AnnStaffGroup):
        return True
    if isinstance(obj, AnnExtra):
        return bool(obj.styledict) or _extra_category(obj.kind) == LAYOUT
    return (
        obj.noteshape != "normal"
        or obj.noteheadFill is not None
        or obj.stemDirection != "unspecified"
        or bool(obj.styledict)
        or (_is_rest(obj) and obj.pitches[0].name != "R")
    )


def _item_carries_layout(source: str, item: str) -> bool:
    """Whether an attached item carries a layout value."""
    if source == _ARTICULATIONS:
        return (
            bool(_PLACEMENT.search(item)) and _layout_base(item) != _FINGERING
        )
    if source == _EXPRESSIONS:
        return bool(_PLACEMENT.search(item)) or (
            _PLACEMENT.sub("", item) == _INVERTED_FERMATA
        )
    # A tuplet group's printed number, bracket and placement are on its start
    # note, as one entry that musicdiff leaves empty when none is printed.
    return source == _TUPLET_INFO and bool(item)


def _layout_extras(
    differences: Iterable[Difference],
) -> tuple[LayoutElement, ...]:
    """Return the layout elements of ``T(Y)`` without a counterpart's.

    A ``T(Y)`` object is an extra when it carries layout and its counterpart
    is not itself a layout element; an attached item is an extra when it
    carries layout and its aligned item of ``Y`` does not.
    """
    extras: list[LayoutElement] = []
    deleted: set[int] = set()
    pairs: dict[tuple[int, int], tuple[_AnnObject, _AnnObject]] = {}
    for difference in differences:
        t_obj, y_obj = difference.t_object, difference.y_object
        if t_obj is None:
            continue
        if y_obj is None:
            if id(t_obj) not in deleted:
                deleted.add(id(t_obj))
                extras.extend(_object_layout_elements(t_obj))
            continue
        pairs.setdefault((id(t_obj), id(y_obj)), (t_obj, y_obj))
        if difference.t_item is None:
            continue
        source, index = difference.t_item
        if not _item_carries_layout(source, getattr(t_obj, source)[index]):
            continue
        if difference.y_item is not None:
            y_source, y_index = difference.y_item
            if _item_carries_layout(
                y_source,
                getattr(y_obj, y_source)[y_index],
            ):
                continue
        extras.append(LayoutElement(t_obj, difference.t_item))
    extras.extend(
        LayoutElement(t_obj)
        for t_obj, y_obj in pairs.values()
        if _carries_layout(t_obj) and not _carries_layout(y_obj)
    )
    return tuple(extras)


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
    info = extra.infodict
    if extra.kind == "timesig":
        symbol = info.get("symbol")
        if symbol == "common":
            info = {"numerator": "4", "denominator": "4"}
        elif symbol == "cut":
            info = {"numerator": "2", "denominator": "2"}
    return (extra.content, extra.symbolic, sorted(info.items()))


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


def _reset_size(
    obj: AnnNote | AnnVoice | AnnMeasure | AnnPart | AnnScore,
) -> None:
    obj._cached_notation_size = None  # noqa: SLF001 - musicdiff's size cache


@dataclass(frozen=True, slots=True, eq=False)
class _WordsReading:
    """A score's words reading, which its reprint removal uses."""

    words: dict[int, tuple[m21.base.Music21Object, ...]]
    score: m21.stream.Score
    detail: DetailLevel | int


@dataclass(frozen=True, slots=True)
class _BarSite:
    staff: tuple[int, int]
    measure_index: int
    candidates: dict[tuple[int, int, int, Fraction, bool], list[XmlEvent]]
    notes: dict[int, AnnNote]


def _bridge(
    score: AnnScore,
    xml: XmlScore,
) -> tuple[dict[int, XmlEvent], dict[int, AnnNote]]:
    """Map each annotated note to its MusicXML event, and each event back."""
    staff_keys = _musicxml_part_staff_indices(score)
    candidates: dict[tuple[int, int, int, Fraction, bool], list[XmlEvent]] = {}
    for event in xml.events:
        key = (*event.place, event.measure, event.onset, event.grace)
        candidates.setdefault(key, []).append(event)
    events: dict[int, XmlEvent] = {}
    notes: dict[int, AnnNote] = {}
    for staff, part in enumerate(score.part_list):
        indexes = _measure_indexes(part.get_object())
        for bar in part.bar_list:
            measure_index = indexes.get(id(bar.get_object()))
            if measure_index is None:
                message = f"measure {bar.measureNumber} has no source index"
                raise ProtocolDefectError(message)
            site = _BarSite(
                staff_keys[staff],
                measure_index,
                candidates,
                notes,
            )
            for voice in bar.voices_list:
                events.update(_bridge_voice(voice, bar, site))
    return events, notes


def _bridge_voice(
    voice: AnnVoice,
    bar: AnnMeasure,
    site: _BarSite,
) -> dict[int, XmlEvent]:
    voice_id = _annotated_voice_id(voice)
    measure = bar.get_object()
    if not isinstance(measure, m21.stream.Measure):
        message = f"bar {bar.measureNumber} has no music21 measure"
        raise ProtocolDefectError(message)
    events: dict[int, XmlEvent] = {}
    for note in voice.annot_notes:
        general_note = _general_note(note)
        onset = _fraction(general_note.getOffsetInHierarchy(measure))
        grace = general_note.duration.isGrace
        key = (*site.staff, site.measure_index, onset, grace)
        candidates = [
            event
            for event in site.candidates.get(key, ())
            if id(event) not in site.notes
            and (voice_id is None or event.voice == voice_id)
        ]
        if len(candidates) > 1:
            rest = _is_rest(note)
            pitches = _annotated_pitches(note)
            candidates = [
                event
                for event in candidates
                if event.rest == rest and event.pitches == pitches
            ]
        # music21 keeps a staff's grace notes in document order, while a grace
        # position counts the grace notes of a voice on every staff.
        if grace and candidates:
            candidates = [min(candidates, key=lambda event: event.order)]
        if len(candidates) != 1:
            message = f"annotated note has no unique MusicXML event: {note}"
            raise ProtocolDefectError(message)
        events[id(note)] = candidates[0]
        site.notes[id(candidates[0])] = note
    return events


def _annotated_voice_id(voice: AnnVoice) -> str | None:
    m21_voice = voice.get_object()
    return (
        str(m21_voice.id) if isinstance(m21_voice, m21.stream.Voice) else None
    )


def _check_octave_shifts(
    score: AnnScore,
    parsed: m21.stream.Score,
    xml: XmlScore,
    events: dict[int, XmlEvent],
) -> None:
    """Stop where music21 reads an octave shift other than MusicXML does.

    music21 starts an ottava on the next note in the document, whatever its
    staff or offset, and ends it on the note that precedes its stop in the
    measure, if any, so it can drop or misplace the notes of an ottava.
    musicdiff then compares written pitches that the score does not write,
    and an ottava, from the start of its first element to the end of its
    last, that the score does not give. So each music21 ottava must start
    and end on the staff of a MusicXML octave shift, at its start and after
    its length; a note under either reading of an ottava must keep the
    written pitch that the MusicXML octave shifts give it; and each note and
    rest must start in a music21 ottava of its staff exactly when, and by as
    many octaves as, MusicXML shifts it.
    """
    extents = _ottava_extents(score, parsed, xml, events)
    for part in score.part_list:
        for bar in part.bar_list:
            for voice in bar.voices_list:
                for note in voice.annot_notes:
                    if _misreads_octave_shift(
                        note,
                        events[id(note)],
                        parsed,
                        extents,
                    ):
                        message = (
                            "music21 reads an octave shift in measure "
                            f"{bar.measureNumber} other than MusicXML does"
                        )
                        raise ProtocolDefectError(message)


type _OttavaExtents = dict[
    tuple[int, int],
    list[tuple[Fraction, Fraction, int]],
]
"""The start, end and octaves of each music21 ottava, by staff."""


def _ottava_extents(
    score: AnnScore,
    parsed: m21.stream.Score,
    xml: XmlScore,
    events: dict[int, XmlEvent],
) -> _OttavaExtents:
    """Return each music21 ottava's extent, which must be a MusicXML one.

    musicdiff compares an ottava's offset in the measure of its first
    element, and its length to the end of its last element.
    """
    bridged = {
        id(_general_note(note)): events[id(note)]
        for part in score.part_list
        for bar in part.bar_list
        for voice in bar.voices_list
        for note in voice.annot_notes
    }
    shifts = {
        (ottava.place, ottava.start, ottava.end - ottava.start, ottava.shift)
        for ottava in xml.ottavas
    }
    extents: _OttavaExtents = {}
    for ottava in parsed.spannerBundle:
        if not isinstance(ottava, m21.spanner.Ottava):
            continue
        first, last = ottava.getFirst(), ottava.getLast()
        if first is None or last is None:
            continue
        first_event, last_event = bridged.get(id(first)), bridged.get(id(last))
        if first_event is None or last_event is None:
            message = (
                "music21 places an ottava on a note that musicdiff does not "
                "annotate"
            )
            raise ProtocolDefectError(message)
        start = _fraction(first.getOffsetInHierarchy(parsed))
        end = _fraction(last.getOffsetInHierarchy(parsed)) + _fraction(
            last.duration.quarterLength,
        )
        octaves = _OTTAVA_OCTAVES[ottava.shiftMagnitude()]
        shift = octaves if ottava.shiftDirection() == "up" else -octaves
        if (
            last_event.place != first_event.place
            or (
                first_event.place,
                first_event.start,
                end - start,
                shift,
            )
            not in shifts
        ):
            message = (
                "music21 reads an octave shift in measure "
                f"{first.measureNumber} other than MusicXML does"
            )
            raise ProtocolDefectError(message)
        extents.setdefault(first_event.place, []).append((start, end, shift))
    return extents


def _misreads_octave_shift(
    note: AnnNote,
    event: XmlEvent,
    parsed: m21.stream.Score,
    extents: _OttavaExtents,
) -> bool:
    general_note = _general_note(note)
    if (
        event.shift or general_note.getSpannerSites(["Ottava"])
    ) and _annotated_pitches(note) != event.pitches:
        return True
    staff_extents = extents.get(event.place, [])
    if not (event.shift or staff_extents):
        return False
    onset = _fraction(general_note.getOffsetInHierarchy(parsed))
    shift = next(
        (
            octaves
            for start, end, octaves in staff_extents
            if start <= onset < end
        ),
        0,
    )
    return shift != event.shift


def _annotated_pitches(note: AnnNote) -> tuple[PitchKey, ...]:
    general_note = _general_note(note)
    if isinstance(general_note, m21.chord.Chord):
        pitches = general_note.pitches
    elif isinstance(general_note, m21.note.Note):
        pitches = (general_note.pitch,)
    else:
        pitches = ()
    return tuple(sorted(_music21_pitch_key(pitch) for pitch in pitches))


def _remove_line_reprints(
    annotated: AnnScore,
    xml: XmlScore,
    notes: dict[int, AnnNote],
    words: _WordsReading,
) -> None:
    """Remove trill marks and words that restate an open line's start.

    Each is on the first event of a system on the line's staff, in a later
    system than the line's start.
    """
    staff_indexes = {
        key: staff
        for staff, key in _musicxml_part_staff_indices(annotated).items()
    }
    firsts = _system_first_events(xml)
    changed: set[int] = set()
    for span in xml.spans:
        staff = staff_indexes.get(span.start.place)
        if staff is None:
            continue
        for system, first in firsts.get(span.start.place, ()):
            if span.start.measure < system and _drop_line_reprint(
                span,
                first,
                annotated.part_list[staff],
                (xml, notes, words),
            ):
                changed.add(staff)
    for staff in changed:
        part = annotated.part_list[staff]
        part.precomputed_str = hash(str(part))
        _reset_size(part)
    if changed:
        _reset_size(annotated)


def _system_first_events(
    xml: XmlScore,
) -> dict[tuple[int, int], list[tuple[int, list[XmlEvent]]]]:
    """The first events of each system on each staff.

    A staff's first event in a system can be in a later measure than the
    system's first, or after the start of a measure. Simultaneous events
    of several voices are all first.

    Returns:
        By staff, the first measure of each system that has events on it,
        in order, with the events at the earliest written position.
    """
    starts = sorted(xml.system_starts)
    systems: dict[tuple[tuple[int, int], int], list[XmlEvent]] = {}
    for event in xml.events:
        system = starts[bisect_right(starts, event.measure) - 1]
        systems.setdefault((event.place, system), []).append(event)
    firsts: dict[tuple[int, int], list[tuple[int, list[XmlEvent]]]] = {}
    for (place, system), events in sorted(
        systems.items(),
        key=lambda item: item[0],
    ):
        first = min(map(_written_order, events))
        firsts.setdefault(place, []).append(
            (
                system,
                [event for event in events if _written_order(event) == first],
            ),
        )
    return firsts


def _drop_line_reprint(
    span: XmlSpan,
    first: list[XmlEvent],
    part: AnnPart,
    source: tuple[
        XmlScore,
        dict[int, AnnNote],
        _WordsReading,
    ],
) -> bool:
    xml, notes, words = source
    if span.family == WAVY_LINE:
        dropped = [
            _drop_trill(note, part)
            for event in _trill_reprints(span, first)
            if (note := notes.get(id(event))) is not None
        ]
        return any(dropped)
    return span.family == DASHES and _drop_words_reprint(
        span,
        first[0],
        part,
        (xml, words),
    )


def _trill_reprints(
    span: XmlSpan,
    first: list[XmlEvent],
) -> list[XmlEvent]:
    """The first events of a system that repeat the line's trill mark."""
    signature = (
        None if span.start_note is None else _trill_signature(span.start_note)
    )
    if signature is None:
        return []
    return [
        event
        for event in first
        if event.start <= span.stop.start
        and any(_trill_signature(note) == signature for note in event.notes)
    ]


def _written_order(event: XmlEvent) -> tuple[Fraction, bool, int]:
    return event.start, not event.grace, event.grace_position


def _event_order(event: XmlEvent) -> tuple[Fraction, bool, int, int]:
    """The written order of an event, then its document order."""
    return (*_written_order(event), event.order)


def _trill_signature(note: ET.Element) -> tuple[str, ...] | None:
    ornaments = note.find("notations/ornaments")
    if ornaments is None or ornaments.find("trill-mark") is None:
        return None
    return tuple(
        sorted(
            (mark.text or "").strip()
            for mark in ornaments.findall("accidental-mark")
        ),
    )


def _drop_trill(note: AnnNote, part: AnnPart) -> bool:
    for index, name in enumerate(note.expressions):
        if _TRILL_EXPRESSION.fullmatch(name):
            del note.expressions[index]
            break
    else:
        return False
    note.precomputed_str = str(note)
    _reset_size(note)
    for bar in part.bar_list:
        for voice in bar.voices_list:
            if any(n is note for n in voice.annot_notes):
                voice.precomputed_str = str(voice)
                _reset_size(voice)
                bar.precomputed_str = hash(str(bar))
                bar.precomputed_repr = hash(repr(bar))
                _reset_size(bar)
    return True


def _drop_words_reprint(
    span: XmlSpan,
    first: XmlEvent,
    part: AnnPart,
    source: tuple[XmlScore, _WordsReading],
) -> bool:
    """Drop the first words direction on ``first`` that restates the line.

    Only a direction whose joined words restate the line counts, so new
    words of the same text after the line's stop stay. Each of its words,
    which music21 may read as several text expressions, is dropped. The
    words are the reprint, so the metronome of a direction with tempo words
    stays, compared as a mark without text.
    """
    xml, reading = source
    words = reading.words
    start = span.start
    stop = span.stop
    if not isinstance(start, XmlDirection) or (stop.measure, stop.onset) < (
        first.measure,
        first.onset,
    ):
        return False
    text = dashes_text(start, xml.directions)
    if text is None:
        return False
    directions = sorted(
        _reprint_directions(span, first, text, xml),
        key=lambda direction: direction.order,
    )
    if not directions:
        return False
    indexes = _measure_indexes(part.get_object())
    for bar in part.bar_list:
        if indexes.get(id(bar.get_object())) != first.measure:
            continue
        extras = {
            id(extra.get_object()): extra
            for extra in bar.extras_list
            if extra.kind in {"direction", "tempo"}
            and extra.offset == first.onset
        }
        for direction in directions:
            reprints = [
                extras[id(item)]
                for item in words.get(direction.order, ())
                if id(item) in extras
            ]
            if reprints:
                metronome = (
                    direction.element.find("direction-type/metronome")
                    is not None
                )
                _drop_words(bar, reprints, reading if metronome else None)
                return True
    return False


def _drop_words(
    bar: AnnMeasure,
    reprints: list[AnnExtra],
    reading: _WordsReading | None,
) -> None:
    """Drop the reprinted words, keeping a metronome mark without its text.

    With ``reading``, the direction has a ``metronome``, and its mark is
    annotated again without its text, as musicdiff annotates a mark that
    a ``metronome`` alone gives. A mark that is then empty is dropped, as
    musicdiff ignores it.
    """
    kept: dict[int, AnnExtra] = {}
    measure = bar.get_object()
    if not isinstance(measure, m21.stream.Measure):
        message = f"bar {bar.measureNumber} has no music21 measure"
        raise ProtocolDefectError(message)
    for extra in reprints:
        mark = extra.get_object()
        if reading is not None and isinstance(mark, m21.tempo.MetronomeMark):
            bare = _without_text(mark, measure, reading)
            if bare is not None:
                kept[id(extra)] = bare
    dropped = {id(extra) for extra in reprints}
    bar.extras_list = [
        kept.get(id(extra), extra)
        for extra in bar.extras_list
        if id(extra) not in dropped or id(extra) in kept
    ]
    bar.n_of_elements -= len(dropped) - len(kept)
    bar.precomputed_str = hash(str(bar))
    bar.precomputed_repr = hash(repr(bar))
    _reset_size(bar)


def _without_text(
    mark: m21.tempo.MetronomeMark,
    measure: m21.stream.Measure,
    reading: _WordsReading,
) -> AnnExtra | None:
    """Annotate ``mark`` as if it had no text, then restore its text.

    Returns:
        The annotated mark, or ``None`` when it shows nothing to compare.
    """
    # musicdiff reads the tempo text object, not the ``text`` property.
    text = mark._tempoText  # noqa: SLF001
    implicit = mark.textImplicit
    mark._tempoText = None  # noqa: SLF001
    mark.textImplicit = True
    try:
        bare = AnnExtra(mark, measure, reading.score, reading.detail)
    finally:
        mark._tempoText = text  # noqa: SLF001
        mark.textImplicit = implicit
    if bare.content or bare.symbolic or bare.infodict:
        return bare
    return None


def _reprint_directions(
    span: XmlSpan,
    first: XmlEvent,
    text: str,
    xml: XmlScore,
) -> list[XmlDirection]:
    """The words of the line's text on ``first`` while the line is open.

    The line does not stop before ``first``. A line that stops at the
    position of ``first`` is open only at words that precede its stop in
    document order.
    """
    stop = span.stop
    position = (first.measure, first.onset)
    stop_position = (stop.measure, stop.onset)
    return [
        direction
        for direction in xml.directions
        if direction.place == span.start.place
        and (direction.measure, direction.onset) == position
        and direction.words == text
        and (stop_position > position or direction.order <= stop.order)
    ]


def _extra_category(kind: str) -> str:
    category = _EXTRA_KINDS.get(kind)
    if category is None:
        message = f"extra kind {kind!r} has no category"
        raise ProtocolDefectError(message)
    return category


def _expression_category(name: str) -> str:
    name = _PLACEMENT.sub("", name)
    if name in {_FERMATA, _INVERTED_FERMATA}:
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
    return DISREGARDED if _layout_base(name) == _FINGERING else _ARTICULATION


def _layout_base(item: str) -> str:
    """Return an attached item without its placement or fermata shape."""
    base = _PLACEMENT.sub("", item)
    return _FERMATA if base == _INVERTED_FERMATA else base


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
        return [
            Difference(
                (LAYOUT,),
                y_obj if isinstance(y_obj, AnnStaffGroup) else None,
                t_obj if isinstance(t_obj, AnnStaffGroup) else None,
                (record,),
            ),
        ]
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
    differences: list[Difference] = []
    for symbol, change in _note_pair_symbols(name, t_obj, y_obj, pending):
        if change is None:
            differences.append(Difference((symbol,), y_obj, t_obj, (record,)))
            continue
        differences.append(
            Difference(
                (symbol,),
                y_obj,
                t_obj,
                (record,),
                None
                if change.y_index is None
                else (change.source, change.y_index),
                None
                if change.t_index is None
                else (change.source, change.t_index),
            ),
        )
    return differences


def _note_pair_symbols(
    name: str,
    t_note: AnnNote,
    y_note: AnnNote,
    pending: _PendingChanges,
) -> list[tuple[str, _ItemChange | None]]:
    if name in _NOTE_PAIR_OPERATIONS:
        return [(_NOTE_PAIR_OPERATIONS[name], None)]
    if name == "pitchnameedit":
        both_rests = _is_rest(t_note) and _is_rest(y_note)
        return [(LAYOUT if both_rests else _PITCH, None)]
    if name in {"insbeam", "delbeam", "editbeam"}:
        if _duration_type(t_note) != _duration_type(y_note):
            return [(_RHYTHM, None)]
        both_rests = _is_rest(t_note) and _is_rest(y_note)
        return [(DISREGARDED if both_rests else _BEAM, None)]
    for which, categorize in (
        ("articulation", _articulation_category),
        ("expression", _expression_category),
    ):
        if name in {"ins" + which, "del" + which, "edit" + which}:
            change = _aligned_change(name, (t_note, y_note, which), pending)
            return [(_attached_symbol(change, categorize), change)]
    if name in _TUPLET_OPERATIONS:
        change = _aligned_change(name, (t_note, y_note, "tuplet"), pending)
        return [(_tuplet_symbol(change, t_note, y_note), change)]
    message = f"operation {name!r} has no assignment"
    raise ProtocolDefectError(message)


def _attached_symbol(
    change: _ItemChange,
    categorize: Callable[[str], str],
) -> str:
    """Assign an articulation or expression change.

    The change is ``layout`` when only the placement or fermata shape
    differs, unless an item is disregarded.
    """
    items = (change.t_item, change.y_item)
    if (
        change.t_item is not None
        and change.y_item is not None
        and DISREGARDED not in {categorize(i) for i in items if i is not None}
        and _layout_base(change.t_item) == _layout_base(change.y_item)
    ):
        return LAYOUT
    return _change_category(items, categorize)


def _tuplet_symbol(
    change: _ItemChange,
    t_note: AnnNote,
    y_note: AnnNote,
) -> str:
    """Assign a tuplet change, ``layout`` if only what is printed differs.

    musicdiff aligns a note's tuplet types, then its tuplet infos. With Style,
    an info is the printed number, bracket and placement of a group that
    starts on the note; without it, the actual number. When the two notes'
    tuplet types and music21 tuplets agree in type and ratio, an info change
    is only in the printed number, bracket or placement, which never happens
    without Style.
    """
    if (
        change.source == _TUPLET_INFO
        and t_note.tuplets == y_note.tuplets
        and _tuplet_ratios(t_note) == _tuplet_ratios(y_note)
    ):
        return LAYOUT
    return _TUPLET


def _tuplet_ratios(note: AnnNote) -> tuple[tuple[object, int, int], ...]:
    return tuple(
        (tuplet.type, tuplet.numberNotesActual, tuplet.numberNotesNormal)
        for tuplet in _general_note(note).duration.tuplets
    )


def _aligned_change(
    name: str,
    pair: tuple[AnnNote, AnnNote, str],
    pending: _PendingChanges,
) -> _ItemChange:
    """Return the item change that one attached-item operation makes.

    musicdiff's operation carries no index, so its alignment is recomputed,
    and the changes of one note pair are consumed in musicdiff's order: for a
    tuplet, the changes of the tuplet types before those of the tuplet infos.
    """
    t_note, y_note, which = pair
    key = (id(t_note), id(y_note), which)
    if key not in pending:
        sources = (
            (_TUPLETS, _TUPLET_INFO) if which == "tuplet" else (which + "s",)
        )
        pending[key] = [
            change
            for source in sources
            for change in _alignment_changes(
                getattr(t_note, source),
                getattr(y_note, source),
                which,
                source,
            )
        ]
    changes = pending[key]
    if not changes or changes[0].name != name:
        message = f"{name} does not match musicdiff's alignment"
        raise ProtocolDefectError(message)
    return changes.pop(0)


def _alignment_changes(
    t_items: list[str],
    y_items: list[str],
    which: str,
    source: str,
) -> list[_ItemChange]:
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
    changes: list[_ItemChange] = []
    for kind, i, j in reversed(steps):
        if kind == "del":
            changes.append(
                _ItemChange("del" + which, source, t_items[i], None, i, None),
            )
        elif kind == "ins":
            changes.append(
                _ItemChange("ins" + which, source, None, y_items[j], None, j),
            )
        elif t_items[i] != y_items[j]:
            changes.append(
                _ItemChange(
                    "edit" + which,
                    source,
                    t_items[i],
                    y_items[j],
                    i,
                    j,
                ),
            )
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
        () if _is_rest(note) else _source_beamings(note),
    )


def _source_beamings(note: AnnNote) -> tuple[tuple[int, str], ...]:
    beams = getattr(_general_note(note), "beams", None)
    if beams is None:
        return tuple(enumerate(note.beamings, start=1))
    return tuple(
        (number, beams.getByNumber(number).type)
        for number in beams.getNumbers()
    )


def _pairing_rule(t_location: Location, y_location: Location) -> str | None:
    """Return the category of a matching pair under rule 1 or rule 2."""
    if t_location.part_index != y_location.part_index:
        return None
    if t_location.staff != y_location.staff:
        return _CROSS_STAFF
    if t_location.voice_position != y_location.voice_position:
        return _VOICE
    return None


def _apply_pairing(
    differences: list[Difference],
    locations: dict[int, Location],
    cross_staff_owned: Callable[[NotePair], bool] | None,
) -> tuple[list[Difference], tuple[NotePair, ...]]:
    """Apply pairing rules 1 and 2, in that order, one to one."""
    inserted = [d for d in differences if _whole_note(d, y_side=True)]
    deleted = [d for d in differences if _whole_note(d, y_side=False)]
    removed: set[int] = set()
    added: list[Difference] = []
    pairings: list[NotePair] = []
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
            pair = NotePair(
                rule,
                y_note,
                t_note,
                locations[id(y_note)],
                locations[id(t_note)],
            )
            pairings.append(pair)
            if (
                rule == _CROSS_STAFF
                and cross_staff_owned is not None
                and cross_staff_owned(pair)
            ):
                continue
            operations = y_diff.operations + t_diff.operations
            added.append(Difference((rule,), y_note, t_note, operations))
    return [d for d in differences if id(d) not in removed] + added, tuple(
        pairings,
    )


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
