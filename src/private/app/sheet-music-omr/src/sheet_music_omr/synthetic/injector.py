"""Seeded musical annotation injection on canonical Text Score 2 trees."""

from __future__ import annotations

import hashlib
import json
import random
import xml.etree.ElementTree as ET
from collections import Counter, defaultdict
from dataclasses import dataclass, field, replace
from fractions import Fraction
from itertools import count
from typing import TYPE_CHECKING, cast

from sheet_music_omr.diagnostics import TextScoreError
from sheet_music_omr.evaluation.alignment import atomic_write_text
from sheet_music_omr.harness.stops import StopConditionError
from sheet_music_omr.model import (
    Arpeggiate,
    ArpeggioRelation,
    Articulation,
    ArticulationKind,
    AssembledScore,
    Beam,
    BeamState,
    DashesEnd,
    DashesStart,
    Dynamic,
    DynamicMark,
    DynamicText,
    EndpointRole,
    ExtendedTrillRelation,
    Fermata,
    HairpinEnd,
    HairpinKind,
    HairpinStart,
    LineEnd,
    LineStart,
    MeasureRest,
    Metronome,
    MetronomeUnit,
    Note,
    OttavaEnd,
    OttavaSign,
    OttavaStart,
    PedalAction,
    PedalLine,
    PedalSign,
    PedalSignKind,
    Pitch,
    Placement,
    Rest,
    SingleTremolo,
    SlurRelation,
    Spacer,
    Tempo,
    TremoloRelation,
    TrillEnd,
    Tuplet,
    Words,
)
from sheet_music_omr.musicxml_forward import ReviewRequiredError, to_musicxml
from sheet_music_omr.parser import parse_document
from sheet_music_omr.relations import analyze_relations, new_order
from sheet_music_omr.review import review_markers
from sheet_music_omr.semantics import analyze, validate_document
from sheet_music_omr.timeline import build_timeline
from sheet_music_omr.writer import write_document, write_event

if TYPE_CHECKING:
    from collections.abc import Collection, Iterable, Iterator
    from pathlib import Path
    from typing import Literal

    from sheet_music_omr.model import Direction, Event, NoteFlag, TupletItem
    from sheet_music_omr.semantics import Analysis
    from sheet_music_omr.timeline import LeafEvent, Placed

type _Site = tuple[int, int, int]
type _PedalStyle = Literal["line", "pedstar"]

_RATES = {
    "N1": (0.3, 1.2),
    "N2": (0.15, 0.6),
    "N3": (0.1, 0.35),
    "N4": (0.05, 0.2),
    "N5": (0.4, 1.4),
    "L2": (0.4, 1.6),
    "L3": (0.5, 2.4),
    "L4": (0.2, 0.75),
    "P3": (0.15, 0.45),
}
_CATEGORIES = ("N6", *_RATES)
_TREBLE = 1
_BASS = 2
_HIGH = 6
_LOW = 2
_MAX_OCTAVE = 9
_MIN_CHORD = 2
_MAX_SPANNERS = 16
_PEDAL_MAX_MEASURES = 2
_MIN_FINGERING = 1
_MAX_FINGERING = 5
_SPARSE_SHARE = 0.25
_DENSE_START = 0.75
_SIDE_FLIP_SHARE = 0.2
_BEAT_BOUNDARY = 0
_DYNAMICS = (DynamicMark.P, DynamicMark.MP, DynamicMark.MF, DynamicMark.F)
_WORDS = ("dolce", "espressivo", "cantabile", "leggiero", "sostenuto")
_TEMPOS = (("Allegro", 120), ("Andante", 80), ("Moderato", 104), ("Adagio", 60))
_ARTICULATIONS = (
    ArticulationKind.STACCATO,
    ArticulationKind.ACCENT,
    ArticulationKind.TENUTO,
)
_LABELLED = (
    ArpeggioRelation,
    DashesStart,
    DashesEnd,
    ExtendedTrillRelation,
    HairpinStart,
    HairpinEnd,
    LineStart,
    LineEnd,
    OttavaStart,
    OttavaEnd,
    PedalLine,
    SlurRelation,
    TremoloRelation,
    TrillEnd,
)


@dataclass(frozen=True, slots=True)
class InjectionResult:
    """Canonical output notation and exact native and added category counts."""

    score: AssembledScore
    canonical_v2: str
    musicxml: bytes
    injected_counts: dict[str, int]
    native_counts: dict[str, int]
    pedal_style_used: _PedalStyle | None
    injected_by_measure: dict[int, dict[str, int]]

    def write_log(self, sample_dir: Path) -> None:
        """Write injection-log.json beside the sample's notation artifacts."""
        atomic_write_text(
            sample_dir / "injection-log.json",
            json.dumps(
                {
                    "native_counts": self.native_counts,
                    "injected_counts": self.injected_counts,
                    "injected_by_measure": self.injected_by_measure,
                    "pedal_style_used": self.pedal_style_used,
                },
                indent=2,
                sort_keys=True,
            )
            + "\n",
        )


def _note(placed: Placed) -> Note:
    return cast("Note", placed.event)


def _native_counts(events: Iterable[Placed]) -> Counter[str]:
    totals: Counter[str] = Counter(dict.fromkeys(_CATEGORIES, 0))
    for placed in events:
        event = placed.event
        if isinstance(event, Note):
            totals["N6"] += sum(
                bool(pitch.fingering) for pitch in event.pitches
            )
            totals["L2"] += sum(
                isinstance(flag, SlurRelation)
                and flag.role is EndpointRole.START
                for flag in event.flags
            )
            totals["L4"] += any(
                isinstance(flag, Arpeggiate | ArpeggioRelation)
                for flag in event.flags
            )
        if isinstance(event, Note | Rest):
            totals["L3"] += sum(
                isinstance(flag, Articulation | Fermata) for flag in event.flags
            )
        if isinstance(event, MeasureRest):
            totals["L3"] += event.fermata is not None
        totals["N1"] += isinstance(event, Dynamic | DynamicText)
        totals["N2"] += isinstance(event, HairpinStart)
        totals["N3"] += isinstance(event, Words | DashesStart)
        totals["N4"] += isinstance(event, Tempo)
        totals["N5"] += _pedal_down(event)
        totals["P3"] += isinstance(event, OttavaStart)
    return totals


def _pedal_down(event: object) -> bool:
    return (
        isinstance(event, PedalSign) and event.kind is PedalSignKind.DOWN
    ) or (
        isinstance(event, PedalLine) and event.action is PedalAction.LINE_START
    )


def _rate(rng: random.Random, category: str) -> float:
    if category != "N6":
        return rng.uniform(*_RATES[category])
    draw = rng.random()
    if draw < _SPARSE_SHARE:
        return 0.0
    return (
        rng.uniform(1.5, 3.5) if draw < _DENSE_START else rng.uniform(5.0, 8.5)
    )


@dataclass(frozen=True, slots=True)
class _Span:
    start: Placed
    end: Placed

    def overlaps(self, other: _Span) -> bool:
        return (
            self.start.time < other.end.time
            and other.start.time < self.end.time
        )


def _note_spans(notes: list[Placed]) -> Iterator[_Span]:
    for index, start in enumerate(notes):
        for end in notes[index + 1 : index + 4]:
            if start.time < end.time:
                yield _Span(start, end)


def _safe_start(placed: Placed) -> bool:
    return not any(
        pitch.tie_in or pitch.tie_end is not None
        for pitch in _note(placed).pitches
    )


def _direction_key(direction: Direction) -> str:
    if isinstance(direction, Tempo):
        return "<tempo>"
    if isinstance(direction, Words):
        return '<"'
    return write_event(direction).split("[", 1)[0]


def _pedal_style(events: tuple[Placed, ...], rng: random.Random) -> _PedalStyle:
    if any(isinstance(p.event, PedalLine) for p in events):
        return "line"
    if any(isinstance(p.event, PedalSign) for p in events):
        return "pedstar"
    return rng.choice(("line", "pedstar"))


@dataclass(slots=True)
class _Edits:
    directions: dict[_Site, list[Direction]] = field(
        default_factory=lambda: defaultdict(list)
    )
    end_directions: dict[tuple[int, int], list[Direction]] = field(
        default_factory=lambda: defaultdict(list)
    )
    flags: dict[_Site, list[NoteFlag]] = field(
        default_factory=lambda: defaultdict(list)
    )
    pitches: dict[_Site, tuple[Pitch, ...]] = field(default_factory=dict)
    counts: dict[int, Counter[str]] = field(
        default_factory=lambda: defaultdict(Counter)
    )

    def record(self, category: str, where: Placed, quantity: int = 1) -> None:
        self.counts[where.frame.measure][category] += quantity

    def span(
        self, category: str, span: _Span, start: Direction, end: Direction
    ) -> None:
        self.directions[span.start.ident].append(start)
        self.directions[span.end.ident].append(end)
        self.record(category, span.start)

    def line(
        self, events: tuple[Event, ...], frame: int, voice: int
    ) -> tuple[Event, ...]:
        return (
            *self.walk(events, frame, voice, count()),
            *sorted(
                self.end_directions.get((frame, voice), ()), key=_direction_key
            ),
        )

    def walk(
        self,
        events: tuple[Event, ...],
        frame: int,
        voice: int,
        positions: Iterator[int],
    ) -> Iterator[Event]:
        for event in events:
            if isinstance(event, Tuplet):
                items = tuple(self.walk(event.items, frame, voice, positions))
                yield replace(
                    event, items=cast("tuple[TupletItem, ...]", items)
                )
                continue
            site = (frame, voice, next(positions))
            yield from sorted(self.directions.get(site, ()), key=_direction_key)
            if isinstance(event, Note):
                yield replace(
                    event,
                    pitches=self.pitches.get(site, event.pitches),
                    flags=(*event.flags, *self.flags.get(site, ())),
                )
            else:
                yield event

    def apply(self, score: AssembledScore) -> AssembledScore:
        return replace(
            score,
            blocks=tuple(
                replace(
                    headed,
                    block=replace(
                        headed.block,
                        voices=tuple(
                            replace(
                                voice,
                                events=self.line(voice.events, frame, index),
                            )
                            for index, voice in enumerate(headed.block.voices)
                        ),
                    ),
                )
                for frame, headed in enumerate(score.blocks)
            ),
        )


class _Planner:
    def __init__(self, base: Analysis, *, seed: str) -> None:
        self.base = base
        self.rng = random.Random(hashlib.sha256(seed.encode("utf-8")).digest())  # noqa: S311 - reproducible sampling
        self.events = tuple(base.timeline.events())
        self.notes = [
            p
            for p in self.events
            if isinstance(p.event, Note) and not p.is_grace
        ]
        self.native = _native_counts(self.events)
        self.style = _pedal_style(self.events, self.rng)
        self.labels = count(max(base.relations.relations, default=0) + 1)
        self.edits = _Edits()
        self.spans = [
            span
            for line in base.timeline.lines
            for span in _note_spans(
                [
                    p
                    for p in line.events
                    if isinstance(p.event, Note) and not p.is_grace
                ]
            )
        ]

    def choose[T](self, candidates: Iterable[T], limit: int) -> list[T]:
        options = list(candidates)
        return self.rng.sample(options, min(limit, len(options)))

    def plan(self, enabled: Collection[str]) -> None:
        strategies = {
            "N6": self.add_fingering,
            "N1": self.add_dynamics,
            "N2": self.add_hairpins,
            "N3": self.add_words,
            "N4": self.add_tempo,
            "N5": self.add_pedal,
            "L2": self.add_slurs,
            "L3": self.add_articulations,
            "L4": self.add_arpeggios,
            "P3": self.add_ottavas,
        }
        for category, strategy in strategies.items():
            rate = _rate(self.rng, category)
            gap = (
                round(rate * len(self.base.timeline.frames))
                - self.native[category]
            )
            if category in enabled and gap > 0:
                strategy(gap)

    def add_fingering(self, limit: int) -> None:
        for placed in self.choose(self.notes, len(self.notes)):
            digits = tuple(
                pitch
                if pitch.fingering or pitch.tie_in or pitch.tie_end is not None
                else replace(
                    pitch,
                    fingering=(
                        self.rng.randint(_MIN_FINGERING, _MAX_FINGERING),
                    ),
                )
                for pitch in _note(placed).pitches
            )
            added = sum(
                a != b
                for a, b in zip(digits, _note(placed).pitches, strict=True)
            )
            if not 0 < added <= limit:
                continue
            self.edits.pitches[placed.ident] = digits
            self.edits.record("N6", placed, added)
            limit -= added
            if not limit:
                break

    def _anchors(self, kind: type, *, boundary: bool) -> Iterator[Placed]:
        blocked = {
            (p.staff, p.time) for p in self.events if isinstance(p.event, kind)
        }
        for placed in self.notes:
            site = (placed.staff, placed.time)
            if (
                site not in blocked
                and placed.staff == _TREBLE
                and (not boundary or placed.onset == _BEAT_BOUNDARY)
                and _safe_start(placed)
            ):
                blocked.add(site)
                yield placed

    def _direction(
        self, category: str, where: Placed, direction: Direction
    ) -> None:
        self.edits.directions[where.ident].append(direction)
        self.edits.record(category, where)

    def add_dynamics(self, limit: int) -> None:
        for placed in self.choose(
            self._anchors(Dynamic | DynamicText, boundary=False), limit
        ):
            self._direction("N1", placed, Dynamic(self.rng.choice(_DYNAMICS)))

    def add_words(self, limit: int) -> None:
        for placed in self.choose(
            self._anchors(Words | DashesStart, boundary=True), limit
        ):
            self._direction("N3", placed, Words(self.rng.choice(_WORDS)))

    def add_tempo(self, limit: int) -> None:
        for placed in self.choose(self._anchors(Tempo, boundary=True), limit):
            text, bpm = self.rng.choice(_TEMPOS)
            metronome = (
                Metronome(MetronomeUnit.QUARTER, bpm)
                if self.rng.choice((True, False))
                else None
            )
            self._direction("N4", placed, Tempo(text, metronome))

    def _relation_spans(self, kind: str) -> list[_Span]:
        return [
            _Span(relation.start.placed, relation.end.placed)
            for relation in self.base.relations.relations.values()
            if relation.kind == kind
            and relation.start is not None
            and relation.end is not None
        ]

    def add_hairpins(self, limit: int) -> None:
        occupied = self._relation_spans("hairpin")
        dynamics = {
            (p.staff, p.time)
            for p in self.events
            if isinstance(p.event, Dynamic)
            or any(
                isinstance(mark, Dynamic)
                for mark in self.edits.directions.get(p.ident, ())
            )
        }
        spans = self.choose(self.spans, len(self.spans))
        spans.sort(
            key=lambda span: (span.start.staff, span.start.time) not in dynamics
        )
        for span in spans:
            if any(
                span.start.staff == other.start.staff and span.overlaps(other)
                for other in occupied
            ):
                continue
            if (
                span.start.displayed_staff != span.start.staff
                or span.end.displayed_staff != span.start.staff
                or span.start.voice != span.end.voice
                or span.end.onset <= Fraction(0)
            ):
                continue
            label = next(self.labels)
            kind = self.rng.choice(tuple(HairpinKind))
            self.edits.span(
                "N2", span, HairpinStart(kind, label), HairpinEnd(label)
            )
            occupied.append(span)
            limit -= 1
            if not limit:
                break

    def _native_pedal_spans(self) -> list[_Span]:
        occupied = self._relation_spans("pedal")
        signs = sorted(
            (p for p in self.events if isinstance(p.event, PedalSign)),
            key=lambda p: (p.time, _pedal_down(p.event)),
        )
        start = None
        for mark in signs:
            if _pedal_down(mark.event):
                start = start or mark
            else:
                occupied.append(_Span(start or mark, mark))
                start = None
        if start is not None:
            last = max(self.events, key=lambda p: p.time)
            occupied.append(_Span(start, last))
        return occupied

    def add_pedal(self, limit: int) -> None:
        bass = {}
        bass_line_by_frame: dict[int, int] = {}
        for ev in self.events:
            if ev.staff == _BASS and ev.voice == 1 and ev.ident is not None:
                bass_line_by_frame.setdefault(ev.frame.index, ev.ident[1])
        for note in self.notes:
            if (
                note.staff == _BASS
                and note.voice == 1
                and note.displayed_staff == _BASS
                and _safe_start(note)
            ):
                bass.setdefault(note.time, note)
        notes = [bass[time] for time in sorted(bass)]
        spans = [
            span
            for span in _note_spans(notes)
            if span.end.frame.index
            < span.start.frame.index + _PEDAL_MAX_MEASURES
            and (
                span.end.onset > Fraction(0)
                or (
                    span.end.frame.index > 0
                    and (span.end.frame.index - 1) in bass_line_by_frame
                )
            )
            and span.end.time - span.start.time
            >= Fraction(1, span.start.frame.meter.denominator)
        ]
        occupied = self._native_pedal_spans()
        for span in self.choose(spans, len(spans)):
            if any(span.overlaps(other) for other in occupied):
                continue
            if self.style == "line":
                label = next(self.labels)
                start: Direction = PedalLine(label, PedalAction.LINE_START)
                end: Direction = PedalLine(label, PedalAction.LINE_RELEASE)
            else:
                start = PedalSign(PedalSignKind.DOWN)
                end = PedalSign(PedalSignKind.UP)
            if span.end.onset == Fraction(0):
                prev_frame = span.end.frame.index - 1
                self.edits.directions[span.start.ident].append(start)
                self.edits.end_directions[
                    (prev_frame, bass_line_by_frame[prev_frame])
                ].append(end)
                self.edits.record("N5", span.start)
            else:
                self.edits.span("N5", span, start, end)
            occupied.append(span)
            limit -= 1
            if not limit:
                break

    def add_slurs(self, limit: int) -> None:
        occupied = self._relation_spans("slur")
        spans = self.choose(self.spans, len(self.spans))
        spans.sort(
            key=lambda span: (
                span.start.displayed_staff == span.end.displayed_staff
            )
        )
        for span in spans:
            overlapping = [
                other
                for other in occupied
                if span.overlaps(other)
                or span.start.time == other.end.time
                or span.end.time == other.start.time
            ]
            if len(overlapping) >= _MAX_SPANNERS or any(
                (other.start.ident, other.end.ident)
                == (span.start.ident, span.end.ident)
                for other in overlapping
            ):
                continue
            label = next(self.labels)
            self.edits.flags[span.start.ident].append(
                SlurRelation(label, EndpointRole.START)
            )
            self.edits.flags[span.end.ident].append(
                SlurRelation(label, EndpointRole.END)
            )
            self.edits.record("L2", span.start)
            occupied.append(span)
            limit -= 1
            if not limit:
                break

    def _flag_candidates(self, flags: type) -> Iterator[Placed]:
        return (
            p
            for p in self.notes
            if _safe_start(p)
            and not any(isinstance(flag, flags) for flag in _note(p).flags)
        )

    def add_articulations(self, limit: int) -> None:
        for placed in self.choose(
            self._flag_candidates(Articulation | Fermata), limit
        ):
            side = self.rng.choice((None, Placement.ABOVE, Placement.BELOW))
            flag = (
                Fermata(inverted=side is Placement.BELOW)
                if self.rng.randrange(4) == 0
                else Articulation(self.rng.choice(_ARTICULATIONS), side)
            )
            self.edits.flags[placed.ident].append(flag)
            self.edits.record("L3", placed)

    def add_arpeggios(self, limit: int) -> None:
        candidates = (
            p
            for p in self._flag_candidates(Arpeggiate | ArpeggioRelation)
            if len(_note(p).pitches) >= _MIN_CHORD
        )
        for placed in self.choose(candidates, limit):
            self.edits.flags[placed.ident].append(Arpeggiate())
            self.edits.record("L4", placed)

    def _ottava_candidates(self) -> Iterator[tuple[_Span, list[Placed]]]:
        relations = self._relation_spans("tie") + self._relation_spans("ottava")
        unsafe = {
            index
            for span in relations
            for index in range(span.start.frame.index, span.end.frame.index + 1)
        }
        mrest_frames: set[int] = set()
        notes_by_frame: dict[tuple[int, int], list[Placed]] = defaultdict(list)
        max_real_end: dict[int, Fraction] = defaultdict(lambda: Fraction(0))
        for placed in self.events:
            if isinstance(placed.event, MeasureRest):
                mrest_frames.add(placed.frame.index)
            if isinstance(placed.event, Note | Rest | MeasureRest):
                f_idx = placed.frame.index
                max_real_end[f_idx] = max(
                    max_real_end[f_idx], placed.onset + placed.duration
                )
            if placed.voice != 1 or placed.displayed_staff != placed.staff:
                unsafe.add(placed.frame.index)
            if isinstance(placed.event, Note):
                notes_by_frame[
                    placed.frame.index, placed.displayed_staff
                ].append(placed)
                if any(pitch.has_tie_marks for pitch in placed.event.pitches):
                    unsafe.add(placed.frame.index)
        drift_from = next(
            (
                fr.index
                for fr in self.base.timeline.frames
                if max_real_end[fr.index] < fr.end
                or (
                    fr.index in mrest_frames
                    and fr.end - fr.start != fr.meter.length
                )
            ),
            len(self.base.timeline.frames),
        )
        for span in self.spans:
            frame, staff = span.start.frame.index, span.start.staff
            if (
                frame >= drift_from
                or frame in unsafe
                or span.end.frame.index != frame
                or span.start.voice != 1
                or span.end.voice != 1
                or span.start.displayed_staff != staff
                or span.end.displayed_staff != staff
            ):
                continue
            notes = [
                p
                for p in notes_by_frame[frame, staff]
                if span.start.time <= p.time < span.end.time
            ]
            if (
                notes
                and any(p.time == span.start.time for p in notes)
                and any(p.end_time == span.end.time for p in notes)
                and all(p.end_time <= span.end.time for p in notes)
                and all(_eligible_ottava(p, staff) for p in notes)
            ):
                yield span, notes

    def add_ottavas(self, limit: int) -> None:
        candidates = list(self._ottava_candidates())
        occupied: set[int] = set()
        accidentals = {
            p.frame.index
            for p in self.events
            if isinstance(p.event, Note)
            and any(pitch.accidental is not None for pitch in p.event.pitches)
        }
        for span, notes in self.choose(candidates, len(candidates)):
            frame = span.start.frame.index
            if frame in occupied:
                continue
            alta = span.start.staff == _TREBLE
            sign = OttavaSign.OTTAVA_ALTA if alta else OttavaSign.OTTAVA_BASSA
            step = -1 if alta else 1
            label = next(self.labels)
            before = {
                p.ident: self.edits.pitches.get(p.ident, _note(p).pitches)
                for p in notes
            }
            for site, pitches in before.items():
                self.edits.pitches[site] = tuple(
                    replace(pitch, octave=pitch.octave + step)
                    for pitch in pitches
                )
            self.edits.span(
                "P3",
                span,
                OttavaStart(sign, label, sign),
                OttavaEnd(sign, label, sign),
            )
            # Octave-scoped accidentals can change a neighboring note's sound.
            if frame in accidentals and not _same_sounding(
                self.base,
                self.edits.apply(
                    cast("AssembledScore", self.base.relations.document)
                ),
            ):
                self.edits.pitches.update(before)
                self.edits.directions[span.start.ident].pop()
                self.edits.directions[span.end.ident].pop()
                self.edits.counts[span.start.frame.measure]["P3"] -= 1
                continue
            occupied.add(frame)
            if len(occupied) >= limit:
                break


def _eligible_ottava(placed: Placed, staff: int) -> bool:
    if (
        placed.is_grace
        or placed.staff != staff
        or placed.displayed_staff != staff
    ):
        return False
    return all(
        _HIGH <= pitch.octave <= _MAX_OCTAVE
        if staff == _TREBLE
        else 0 <= pitch.octave <= _LOW
        for pitch in _note(placed).pitches
    )


def _same_sounding(before: Analysis, score: AssembledScore) -> bool:
    after = analyze(score)
    return list(before.pitches.resolved.values()) == list(
        after.pitches.resolved.values()
    )


def _rename_event(event: Event, labels: dict[int, int]) -> Event:
    if isinstance(event, _LABELLED):
        return replace(event, label=labels[event.label])
    if isinstance(event, Tuplet):
        items = tuple(_rename_event(item, labels) for item in event.items)
        return replace(event, items=cast("tuple[TupletItem, ...]", items))
    if not isinstance(event, Note):
        return event
    pitches = tuple(
        replace(
            pitch,
            tie_end=None if pitch.tie_end is None else labels[pitch.tie_end],
            tie_start=None
            if pitch.tie_start is None
            else labels[pitch.tie_start],
            arc_starts=tuple(labels[label] for label in pitch.arc_starts),
        )
        for pitch in event.pitches
    )
    flags = tuple(
        replace(flag, label=labels[flag.label])
        if isinstance(flag, _LABELLED)
        else flag
        for flag in event.flags
    )
    return replace(event, pitches=pitches, flags=flags)


def _renumber(score: AssembledScore) -> tuple[AssembledScore, dict[int, int]]:
    relations = analyze_relations(score, build_timeline(score)).relations
    labels = {
        relation.label: number
        for number, relation in enumerate(
            sorted(relations.values(), key=new_order), 1
        )
    }
    numbered = replace(
        score,
        blocks=tuple(
            replace(
                item,
                block=replace(
                    item.block,
                    voices=tuple(
                        replace(
                            line,
                            events=tuple(
                                _rename_event(e, labels) for e in line.events
                            ),
                        )
                        for line in item.block.voices
                    ),
                ),
            )
            for item in score.blocks
        ),
    )
    return numbered, labels


def _placed_notes(events: Iterable[Placed]) -> Iterator[Placed]:
    return iter(
        sorted(
            (p for p in events if isinstance(p.event, Note)),
            key=lambda p: (p.frame.index, p.staff, p.voice, p.position),
        )
    )


def _with_engraving_hints(
    musicxml: bytes,
    score: AssembledScore,
    planner: _Planner,
    labels: dict[int, int],
    seed: str,
) -> bytes:
    # Text Score 2 does not encode the side of these marks.
    # Apply placement only to the engraving input, not to the label.
    rng = random.Random(  # noqa: S311 - reproducible engraving placement
        hashlib.sha256(f"{seed}:engraving".encode()).digest()
    )
    root = ET.fromstring(musicxml)  # noqa: S314 - generated by this module
    written = iter(root.findall("./part/measure/note[pitch]"))
    pairs = zip(
        _placed_notes(planner.events),
        _placed_notes(build_timeline(score).events()),
        strict=True,
    )
    changed = False
    for source, output in pairs:
        attached = [next(written) for _ in _note(output).pitches]
        changed |= _place_digits(source, output, attached, rng)
        slur_labels = {
            labels[added.label]
            for added in planner.edits.flags.get(source.ident, ())
            if isinstance(added, SlurRelation)
            and added.role is EndpointRole.START
        }
        changed |= _place_slurs(output, attached[0], slur_labels, rng)
    if not changed:
        return musicxml
    declaration, doctype, _ = musicxml.split(b"\n", 2)
    return b"\n".join(
        (declaration, doctype, ET.tostring(root, encoding="utf-8"), b"")
    )


def _place_digits(
    source: Placed,
    output: Placed,
    attached: list[ET.Element],
    rng: random.Random,
) -> bool:
    side, flip = (
        ("above", "below")
        if source.displayed_staff == _TREBLE
        else ("below", "above")
    )
    placement = flip if rng.random() < _SIDE_FLIP_SHARE else side
    changed = False
    for initial, final, xml in zip(
        _note(source).pitches, _note(output).pitches, attached, strict=True
    ):
        if not initial.fingering and final.fingering:
            for digit in xml.findall("notations/technical/fingering"):
                digit.set("placement", placement)
                changed = True
    return changed


def _place_slurs(
    output: Placed, written: ET.Element, labels: set[int], rng: random.Random
) -> bool:
    changed = False
    slurs = (
        flag for flag in _note(output).flags if isinstance(flag, SlurRelation)
    )
    for flag, xml in zip(slurs, written.findall("notations/slur"), strict=True):
        if flag.label in labels and flag.role is EndpointRole.START:
            xml.set("placement", rng.choice(("above", "below")))
            changed = True
    return changed


def inject_annotations(
    score: AssembledScore,
    *,
    seed: str,
    enabled_categories: Collection[str] | None = None,
) -> InjectionResult:
    """Fill per-piece category gaps without replacing native notation."""
    base = analyze(score)
    if base.diagnostics:
        raise TextScoreError(base.diagnostics)
    if markers := review_markers(score):
        raise ReviewRequiredError(markers)
    enabled = frozenset(
        _CATEGORIES if enabled_categories is None else enabled_categories
    )
    if enabled.difference(_CATEGORIES):
        message = "unknown synthetic injection category"
        raise ValueError(message)
    planner = _Planner(base, seed=seed)
    planner.plan(enabled)
    injected, labels = _renumber(planner.edits.apply(score))
    if diagnostics := validate_document(injected):
        raise TextScoreError(diagnostics)
    canonical = write_document(injected)
    parsed = parse_document(canonical)
    if not isinstance(parsed, AssembledScore):
        message = "injected Text Score 2 is not an assembled score"
        raise StopConditionError(message)
    musicxml = to_musicxml(parsed)
    if planner.edits.counts:
        musicxml = _with_engraving_hints(
            musicxml, parsed, planner, labels, seed
        )
    totals: Counter[str] = Counter(dict.fromkeys(_CATEGORIES, 0))
    for per_measure in planner.edits.counts.values():
        totals.update(per_measure)
    style = planner.style if planner.native["N5"] or totals["N5"] else None
    return InjectionResult(
        parsed,
        canonical,
        musicxml,
        dict(totals),
        dict(planner.native),
        style,
        {
            measure: {k: v for k, v in counts.items() if v}
            for measure, counts in planner.edits.counts.items()
            if any(counts.values())
        },
    )


_DIRECTION_FACETS: dict[type, str] = {
    DashesStart: "dashes",
    LineStart: "voice_leading_line",
    DynamicText: "dynamic_text",
}


def _unchecked_event(event: LeafEvent, facets: set[str]) -> None:
    if facet := _DIRECTION_FACETS.get(type(event)):
        facets.add(facet)
    if isinstance(event, PedalLine) and event.action is PedalAction.LINE_OPEN:
        facets.add("pedal_discontinue")
    if (
        isinstance(event, Tempo)
        and event.metronome
        and event.metronome.parenthesized
    ):
        facets.add("parenthesized_metronome")
    flags = (
        event.beams
        if isinstance(event, Spacer)
        else (event.flags if isinstance(event, Note | Rest) else ())
    )
    facets.update(facet for flag in flags if (facet := _flag_facet(flag)))
    if isinstance(event, Note):
        if any(pitch.parenthesized for pitch in event.pitches):
            facets.add("parenthesized_notehead")
        if any(
            pitch.accidental and pitch.accidental.parenthesized
            for pitch in event.pitches
        ):
            facets.add("parenthesized_accidental")


def _flag_facet(flag: object) -> str | None:
    if isinstance(flag, ExtendedTrillRelation):
        return "extended_trill"
    if isinstance(flag, SingleTremolo | TremoloRelation):
        return "tremolo"
    if isinstance(flag, Beam) and flag.state in (
        BeamState.HOOK_FORWARD,
        BeamState.HOOK_BACKWARD,
    ):
        return "beam_hook"
    return None


def _has_restated_midscore_clef(root: ET.Element) -> bool:
    seen_staves: set[str] = set()
    for m_idx, measure in enumerate(root.findall("./part/measure"), 1):
        seen_motion = False
        for child in measure:
            if child.tag in {"note", "backup", "forward"}:
                seen_motion = True
            elif child.tag == "attributes":
                for clef in child.findall("clef"):
                    st_num = clef.get("number", "1")
                    if (
                        m_idx == 1
                        and not seen_motion
                        and st_num not in seen_staves
                    ):
                        seen_staves.add(st_num)
                    else:
                        return True
    return False


def has_unchecked_verovio_supplementary_facet(
    score: AssembledScore, musicxml_text: str | bytes
) -> list[str]:
    """List applicable W1 facets not established by the Verovio MEI gate."""
    facets: set[str] = set()
    for placed in build_timeline(score).events():
        _unchecked_event(placed.event, facets)
    try:
        root = ET.fromstring(musicxml_text)  # noqa: S314 - stdlib does not load external entities
    except ET.ParseError as error:
        message = "cannot establish supplementary facets of malformed MusicXML"
        raise StopConditionError(message) from error
    xml_facets = {
        ".//wavy-line": "extended_trill",
        ".//direction-type/dashes": "dashes",
        ".//direction-type/bracket": "voice_leading_line",
        ".//ending": "volta_ending",
        ".//notehead[@parentheses='yes']": "parenthesized_notehead",
        ".//accidental[@parentheses='yes']": "parenthesized_accidental",
        ".//metronome[@parentheses='yes']": "parenthesized_metronome",
        ".//tremolo": "tremolo",
        ".//pedal[@type='discontinue']": "pedal_discontinue",
        ".//beam[.='forward hook']": "beam_hook",
        ".//beam[.='backward hook']": "beam_hook",
    }
    facets.update(
        facet
        for query, facet in xml_facets.items()
        if root.find(query) is not None
    )
    if _has_restated_midscore_clef(root):
        facets.add("restated_clef")
    if any(
        (mark.text or "").strip() not in DynamicMark
        for mark in root.iter("other-dynamics")
    ):
        facets.add("dynamic_text")
    return sorted(facets)
