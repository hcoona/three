"""Supplementary checks of the W1 comparison protocol.

musicdiff 6.0 does not establish some content, so the comparison protocol
adds project-owned checks that compare the MusicXML of ``Y`` and ``T(Y)``
directly. Each check reads both scores through
:mod:`sheet_music_omr.musicxml_events` and scores its own elements.
:func:`compare_file` runs the whole content pass of one file: the musicdiff
part, with pairing rule 1 deferring to the cross-staff check, and every check.
:func:`failure_element_count` counts the same elements on one score, for a
file whose conversion or comparison fails.
:func:`text_space_consistent` is the text-space consistency check, which is not
part of the fidelity figure.
"""

from __future__ import annotations

from collections import Counter
from dataclasses import dataclass
from fractions import Fraction
from typing import TYPE_CHECKING

import music21 as m21
from music21.common.numberTools import opFrac
from musicdiff.annotation import AnnExtra, AnnNote

from sheet_music_omr.comparison import (
    AnnotatedScore,
    ContentResult,
    ElementTally,
    NotePair,
    annotate,
    compare_annotated,
    content_element_count,
    extra_sites,
    heading_tally,
    voice_element_count,
    voice_order_annotated,
)
from sheet_music_omr.musicxml_events import (
    DASHES,
    SLUR,
    WAVY_LINE,
    ProtocolDefectError,
    XmlDirection,
    XmlEvent,
    XmlSpan,
    dashes_text,
    voice_staves,
)
from sheet_music_omr.musicxml_reverse import from_musicxml_text

if TYPE_CHECKING:
    import xml.etree.ElementTree as ET
    from collections.abc import Callable, Hashable, Iterable, Iterator
    from pathlib import Path

CHECKS: dict[str, str] = {
    "fingering": "fingering",
    "cross_staff": "cross_staff",
    "voice_order": "voice",
    "heading": "heading",
    "wavy_line": "ornament",
    "dashes": "text",
    "notehead_parentheses": "pitch",
    "accidental_parentheses": "accidental",
    "tremolo_strokes": "tremolo",
    "beam_hooks": "beam",
    "rest_beams": "beam",
    "tuplet_ratio": "tuplet",
    "slur_endpoints": "slur",
    "tie_ends": "tie",
}
"""Each supplementary check and the content category it scores."""

_RULE_1 = "cross_staff"
_TUPLET = "tuplet"
_SLUR = "slur"
_HOOKS = frozenset({"forward hook", "backward hook"})
_TREMOLO_ELEMENTS = frozenset({"single", "start"})
_START = "start"
_STOP = "stop"

type _Key = tuple[object, ...]
type _Place = tuple[int, int]
type _Site = tuple[_Place, int, Fraction | None]


@dataclass(frozen=True, slots=True, eq=False)
class FileComparison:
    """The content pass over one file.

    Attributes:
        content: The musicdiff part.
        checks: The element counts of each supplementary check, by name.
    """

    content: ContentResult
    checks: dict[str, ElementTally]

    @property
    def tally(self) -> ElementTally:
        """The element counts of the whole content pass."""
        return sum(self.checks.values(), self.content.tally)

    def check_differences(self) -> dict[str, tuple[int, int]]:
        """Return the non-identical and extra elements of each check.

        Only checks with at least one of either are included.
        """
        return {
            name: (tally.elements - tally.identical, tally.extras)
            for name, tally in self.checks.items()
            if tally.elements != tally.identical or tally.extras
        }

    def category_counts(self) -> Counter[str]:
        """Return the content differences per category.

        These are the counted musicdiff symbols plus the non-identical and
        extra elements of the checks.
        """
        counts = self.content.category_counts()
        for name, (non_identical, extras) in self.check_differences().items():
            counts[CHECKS[name]] += non_identical + extras
        return +counts


@dataclass(frozen=True, slots=True)
class _Side:
    """One score of a file, as the checks read it.

    Attributes:
        score: The annotated score and its MusicXML.
        touched: The ``id`` of each event that a counted musicdiff difference
            touches.
        places: For ``T(Y)``, the part and staff that each event of a
            pairing-rule-1 pair takes from its ``Y`` note.
    """

    score: AnnotatedScore
    touched: frozenset[int]
    places: dict[int, _Place]

    def place(self, event: XmlEvent) -> _Place:
        return self.places.get(id(event), event.place)

    def note_key(self, event: XmlEvent, note: ET.Element) -> _Key:
        """Key one member by measure, staff, onset, written pitch, duration."""
        return (
            event.measure,
            self.place(event),
            *event.position,
            event.written_pitch(note),
            event.written,
        )

    def event_key(self, event: XmlEvent) -> _Key:
        """Key one event by measure, staff, onset, written pitches, duration."""
        return (
            event.measure,
            self.place(event),
            *event.position,
            event.pitches,
            event.written,
        )


def compare_file(t_source: Path | str, y_source: Path | str) -> FileComparison:
    """Run the content pass of one file: musicdiff and every check.

    Args:
        t_source: The round trip ``T(Y)``.
        y_source: The reference ``Y``.

    Returns:
        The musicdiff part and the element counts of every check.

    Raises:
        ProtocolDefectError: If a difference or score fact cannot be assigned.
    """
    t_score = annotate(t_source)
    y_score = annotate(y_source)
    t_cross = _cross_staff_events(t_score)
    y_cross = _cross_staff_events(y_score)
    owned_t = {id(event) for event in t_cross}
    owned_y = {id(event) for event in y_cross}

    def cross_staff_owned(pair: NotePair) -> bool:
        return (
            id(y_score.events[id(pair.y_note)]) in owned_y
            or id(t_score.events[id(pair.t_note)]) in owned_t
        )

    content = compare_annotated(
        t_score,
        y_score,
        cross_staff_owned=cross_staff_owned,
    )
    y_side, t_side = _sides(content)
    checks = {
        "fingering": _multiset(_fingerings(y_side), _fingerings(t_side)),
        "cross_staff": _multiset(
            map(_cross_staff_key, y_cross),
            map(_cross_staff_key, t_cross),
        ),
        "voice_order": voice_order_annotated(t_score, y_score),
        "heading": heading_tally(t_source, y_source),
        **_line_checks(y_side, t_side),
        **_mark_checks(y_side, t_side),
        "tuplet_ratio": _paired_values(
            _ratios(y_side, _touched(content, _TUPLET)[0]),
            _ratios(t_side, frozenset()),
        ),
        "slur_endpoints": _slur_tally(y_side, t_side, _slur_touches(content)),
        "tie_ends": _exempt_multiset(_tie_stops(y_side), _tie_stops(t_side)),
    }
    return FileComparison(
        content,
        {name: checks[name] for name in CHECKS},
    )


def failure_element_count(source: Path | str) -> int:
    """Count a failed file's content elements on one score alone.

    These are the in-scope elements of the musicdiff part and every element
    that a supplementary check defines on the score. An element that a check
    counts only when ``T(Y)`` has a counterpart counts without one.

    Args:
        source: The score, ``Y`` or ``X``.

    Returns:
        The number of content elements, each non-identical for a failed file.

    Raises:
        ProtocolDefectError: If a score fact cannot be assigned.
    """
    score = annotate(source)
    side = _Side(score, frozenset(), {})
    # A failed file's voices need no counterpart, so they are counted without
    # their positions; its heading lines match themselves, one element each.
    elements: dict[str, Iterable[object]] = {
        "fingering": _fingerings(side),
        "cross_staff": _cross_staff_events(score),
        "wavy_line": _wavy_lines(side),
        "dashes": _dashes_lines(side),
        "notehead_parentheses": _parentheses(side, "notehead"),
        "accidental_parentheses": _parentheses(side, "accidental"),
        "tremolo_strokes": _tremolos(side),
        "beam_hooks": _hooks(side),
        "rest_beams": _rest_beams(side),
        "tuplet_ratio": _ratios(side, frozenset()),
        "slur_endpoints": _slurs(side),
        "tie_ends": _tie_stops(side),
    }
    counts = {name: sum(1 for _ in items) for name, items in elements.items()}
    counts["voice_order"] = voice_element_count(score)
    counts["heading"] = heading_tally(source, source).elements
    return content_element_count(score) + sum(counts.values())


def text_space_consistent(
    t_source: Path | str,
    y_source: Path | str,
    *,
    reverse: Callable[[bytes | str], str] = from_musicxml_text,
) -> bool:
    """Check that ``Y`` and ``T(Y)`` reverse to the same canonical text.

    Args:
        t_source: The round trip ``T(Y)``.
        y_source: The reference ``Y``.
        reverse: The reverse converter to canonical text.

    Returns:
        Whether the canonical texts are equal. A mismatch is a converter
        defect, not a fidelity difference.
    """
    return reverse(_data(y_source)) == reverse(_data(t_source))


def _data(source: Path | str) -> bytes | str:
    return source if isinstance(source, str) else source.read_bytes()


def _sides(content: ContentResult) -> tuple[_Side, _Side]:
    touched_y, touched_t = _touched(content, None)
    y_events = content.y_score.events
    t_events = content.t_score.events
    places = {
        id(t_events[id(pair.t_note)]): y_events[id(pair.y_note)].place
        for pair in content.pairings
        if pair.rule == _RULE_1
    }
    return (
        _Side(content.y_score, touched_y, {}),
        _Side(content.t_score, touched_t, places),
    )


def _touched(
    content: ContentResult,
    category: str | None,
) -> tuple[frozenset[int], frozenset[int]]:
    """Return the events of each score that counted differences touch."""
    touched_y: set[int] = set()
    touched_t: set[int] = set()
    for difference in content.counted:
        if category is not None and category not in difference.categories:
            continue
        for note, events, touched in (
            (difference.y_object, content.y_score.events, touched_y),
            (difference.t_object, content.t_score.events, touched_t),
        ):
            if isinstance(note, AnnNote) and id(note) in events:
                touched.add(id(events[id(note)]))
    return frozenset(touched_y), frozenset(touched_t)


def _multiset(y_keys: Iterable[_Key], t_keys: Iterable[_Key]) -> ElementTally:
    """Match keys one to one: unmatched ``Y`` keys and ``T(Y)`` keys differ."""
    y_counts = Counter(y_keys)
    t_counts = Counter(t_keys)
    identical = (y_counts & t_counts).total()
    return ElementTally(
        y_counts.total(),
        identical,
        t_counts.total() - identical,
    )


def _exempt_multiset(
    y_elements: Iterable[tuple[_Key, bool]],
    t_elements: Iterable[tuple[_Key, bool]],
) -> ElementTally:
    """Match keys one to one, exempting unmatched touched elements.

    An exempt unmatched element is neither an element nor extra.
    """
    y_all, y_exempt = _counts(y_elements)
    t_all, t_exempt = _counts(t_elements)
    elements = identical = extras = 0
    for key in y_all.keys() | t_all.keys():
        matched = min(y_all[key], t_all[key])
        y_free = min(y_exempt[key], y_all[key] - matched)
        t_free = min(t_exempt[key], t_all[key] - matched)
        elements += y_all[key] - y_free
        identical += matched
        extras += t_all[key] - matched - t_free
    return ElementTally(elements, identical, extras)


def _counts(
    elements: Iterable[tuple[_Key, bool]],
) -> tuple[Counter[_Key], Counter[_Key]]:
    every: Counter[_Key] = Counter()
    exempt: Counter[_Key] = Counter()
    for key, is_exempt in elements:
        every[key] += 1
        if is_exempt:
            exempt[key] += 1
    return every, exempt


def _paired_values(
    y_elements: Iterable[tuple[_Key, Hashable]],
    t_elements: Iterable[tuple[_Key, Hashable]],
) -> ElementTally:
    """Count ``Y`` elements with a ``T(Y)`` counterpart, identical by value.

    These checks add no extra elements.
    """
    y_values = _values(y_elements)
    t_values = _values(t_elements)
    elements = identical = 0
    for key, values in y_values.items():
        counterparts = t_values.get(key)
        if counterparts is None:
            continue
        matched = min(values.total(), counterparts.total())
        elements += matched
        identical += min(matched, (values & counterparts).total())
    return ElementTally(elements, identical, 0)


def _values(
    elements: Iterable[tuple[_Key, Hashable]],
) -> dict[_Key, Counter[Hashable]]:
    values: dict[_Key, Counter[Hashable]] = {}
    for key, value in elements:
        values.setdefault(key, Counter())[value] += 1
    return values


def _fingerings(side: _Side) -> Iterator[_Key]:
    for event in side.score.xml.events:
        for note in event.notes:
            for mark in note.findall("notations/technical/fingering"):
                for digit in "".join((mark.text or "").split()):
                    yield (*side.note_key(event, note), digit)


def _cross_staff_events(score: AnnotatedScore) -> list[XmlEvent]:
    """Return the events that are not on their voice's staff."""
    staves = voice_staves(score.xml.events)
    return [
        event
        for event in score.xml.events
        if event.staff != staves[id(event)][1]
    ]


def _cross_staff_key(event: XmlEvent) -> _Key:
    return (event.measure, event.place, *event.position, event.pitches)


def _line_checks(y_side: _Side, t_side: _Side) -> dict[str, ElementTally]:
    return {
        "wavy_line": _multiset(_wavy_lines(y_side), _wavy_lines(t_side)),
        "dashes": _multiset(_dashes_lines(y_side), _dashes_lines(t_side)),
    }


def _wavy_lines(side: _Side) -> Iterator[_Key]:
    for span in side.score.xml.spans:
        if span.family == WAVY_LINE:
            start, start_note, stop, stop_note = _note_span(span)
            yield (
                side.note_key(start, start_note),
                side.note_key(stop, stop_note),
            )


def _note_span(
    span: XmlSpan,
) -> tuple[XmlEvent, ET.Element, XmlEvent, ET.Element]:
    start, stop = span.start, span.stop
    if (
        not isinstance(start, XmlEvent)
        or not isinstance(stop, XmlEvent)
        or span.start_note is None
        or span.stop_note is None
    ):
        message = f"a {span.family} span does not join two notes"
        raise ProtocolDefectError(message)
    return start, span.start_note, stop, span.stop_note


def _dashes_lines(side: _Side) -> Iterator[_Key]:
    directions = side.score.xml.directions
    for span in side.score.xml.spans:
        if span.family != DASHES:
            continue
        start, stop = span.start, span.stop
        if not isinstance(start, XmlDirection) or not isinstance(
            stop,
            XmlDirection,
        ):
            message = "a dashes span does not join two directions"
            raise ProtocolDefectError(message)
        yield (
            (start.measure, start.place, start.onset),
            (stop.measure, stop.place, stop.onset),
            dashes_text(start, directions),
        )


def _mark_checks(y_side: _Side, t_side: _Side) -> dict[str, ElementTally]:
    return {
        "notehead_parentheses": _multiset(
            _parentheses(y_side, "notehead"),
            _parentheses(t_side, "notehead"),
        ),
        "accidental_parentheses": _multiset(
            _parentheses(y_side, "accidental"),
            _parentheses(t_side, "accidental"),
        ),
        "tremolo_strokes": _paired_values(
            _tremolos(y_side),
            _tremolos(t_side),
        ),
        "beam_hooks": _paired_values(_hooks(y_side), _hooks(t_side)),
        "rest_beams": _exempt_multiset(
            _rest_beams(y_side),
            _rest_beams(t_side),
        ),
    }


def _parentheses(side: _Side, tag: str) -> Iterator[_Key]:
    for event in side.score.xml.events:
        for note in event.notes:
            mark = note.find(tag)
            if mark is not None and mark.get("parentheses") == "yes":
                yield side.note_key(event, note)


def _tremolos(side: _Side) -> Iterator[tuple[_Key, Hashable]]:
    for event in side.score.xml.events:
        for note in event.notes:
            for mark in note.findall("notations/ornaments/tremolo"):
                kind = mark.get("type", "single")
                if kind in _TREMOLO_ELEMENTS:
                    yield (
                        (*side.event_key(event), kind),
                        (mark.text or "").strip(),
                    )


def _hooks(side: _Side) -> Iterator[tuple[_Key, Hashable]]:
    for event in side.score.xml.events:
        if event.rest:
            continue
        for note in event.notes:
            for beam in note.findall("beam"):
                value = (beam.text or "").strip()
                if value in _HOOKS:
                    yield (
                        (*side.event_key(event), beam.get("number", "1")),
                        value,
                    )


def _rest_beams(side: _Side) -> Iterator[tuple[_Key, bool]]:
    for event in side.score.xml.events:
        if not event.rest:
            continue
        for note in event.notes:
            for beam in note.findall("beam"):
                yield (
                    (
                        event.measure,
                        side.place(event),
                        *event.position,
                        event.written,
                        beam.get("number", "1"),
                        (beam.text or "").strip(),
                    ),
                    id(event) in side.touched,
                )


def _ratios(
    side: _Side,
    excluded: frozenset[int],
) -> Iterator[tuple[_Key, Hashable]]:
    for event in side.score.xml.events:
        modification = event.notes[0].find("time-modification")
        if modification is None or id(event) in excluded:
            continue
        yield (
            (event.measure, side.place(event), *event.position, event.pitches),
            (
                (modification.findtext("actual-notes") or "").strip(),
                (modification.findtext("normal-notes") or "").strip(),
            ),
        )


@dataclass(frozen=True, slots=True)
class _Slur:
    start: XmlEvent
    stop: XmlEvent


def _slurs(side: _Side) -> list[_Slur]:
    slurs: list[_Slur] = []
    for span in side.score.xml.spans:
        if span.family == SLUR:
            start, _, stop, _ = _note_span(span)
            slurs.append(_Slur(start, stop))
    return slurs


def _slur_touches(
    content: ContentResult,
) -> dict[_Site, frozenset[Fraction | None]]:
    """Return each touched ``Y`` slur's start site and musicdiff duration.

    musicdiff identifies a slur by its start site and its duration, so a
    counted ``slur`` difference touches each ``Y`` slur that has both.
    """
    sites = extra_sites(content.y_score)
    touched: dict[_Site, set[Fraction | None]] = {}
    for difference in content.counted:
        extra = difference.y_object
        if (
            _SLUR in difference.categories
            and isinstance(extra, AnnExtra)
            and id(extra) in sites
        ):
            duration = (
                None if extra.duration is None else Fraction(extra.duration)
            )
            touched.setdefault(sites[id(extra)], set()).add(duration)
    return {site: frozenset(values) for site, values in touched.items()}


def _slur_site(slur: _Slur) -> _Site:
    return slur.start.place, slur.start.measure, slur.start.onset


def _music21_notes(
    score: AnnotatedScore,
) -> dict[int, m21.base.Music21Object]:
    """Return the music21 note of each annotated event, by its event's id."""
    notes: dict[int, m21.base.Music21Object] = {}
    for part in score.annotated.part_list:
        for bar in part.bar_list:
            for voice in bar.voices_list:
                for note in voice.annot_notes:
                    event = score.events.get(id(note))
                    general_note = note.get_object()
                    if event is not None and general_note is not None:
                        notes[id(event)] = general_note
    return notes


def _slur_duration(
    slur: _Slur,
    notes: dict[int, m21.base.Music21Object],
    score: m21.stream.Score,
) -> Fraction | None:
    """The duration that musicdiff gives the slur, if it can be computed.

    musicdiff measures from the first note's offset in the music21 score to
    the last note's end, as :class:`musicdiff.annotation.AnnExtra` does. A
    MusicXML measure's trailing ``forward`` can lengthen the measure in
    MusicXML time but not in music21 offsets, so MusicXML times do not give
    this duration.
    """
    first = notes.get(id(slur.start))
    last = notes.get(id(slur.stop))
    if first is None or last is None:
        return None
    try:
        start = first.getOffsetInHierarchy(score)
    except m21.sites.SitesException:
        return None
    try:
        end = opFrac(
            last.getOffsetInHierarchy(score) + last.duration.quarterLength,
        )
    except m21.sites.SitesException:
        end = start
    return Fraction(opFrac(end - start))


def _slur_tally(
    y_side: _Side,
    t_side: _Side,
    touches: dict[_Site, frozenset[Fraction | None]],
) -> ElementTally:
    """Score slur endpoints; musicdiff scores what this check excludes."""
    candidates = _slurs(t_side)
    notes = _music21_notes(y_side.score)
    y_slurs = [
        (slur, _slur_duration(slur, notes, y_side.score.score))
        for slur in _slurs(y_side)
    ]
    durations: dict[_Site, set[Fraction | None]] = {}
    for slur, duration in y_slurs:
        durations.setdefault(_slur_site(slur), set()).add(duration)
    used: set[int] = set()
    elements = identical = 0
    for slur, duration in y_slurs:
        start, stop = slur.start, slur.stop
        touched = touches.get(_slur_site(slur))
        if (
            (
                touched is not None
                # A touched slur whose duration is unknown, or that no ``Y``
                # slur of its site matches, leaves the site to musicdiff.
                and (
                    duration is None
                    or duration in touched
                    or touched.isdisjoint(durations[_slur_site(slur)])
                )
            )
            or id(start) in y_side.touched
            or id(stop) in y_side.touched
        ):
            continue
        matches = [
            index
            for index, other in enumerate(candidates)
            if index not in used
            and (other.start.measure, other.start.onset)
            == (start.measure, start.onset)
            and other.stop.end == stop.end
        ]
        if not matches:
            continue
        sides = (y_side, t_side)
        best = max(
            matches,
            key=lambda index: _slur_preference(slur, candidates[index], sides),
        )
        used.add(best)
        elements += 1
        if _slur_identical(slur, candidates[best], sides):
            identical += 1
    return ElementTally(elements, identical, 0)


def _slur_preference(
    slur: _Slur,
    other: _Slur,
    sides: tuple[_Side, _Side],
) -> tuple[bool, ...]:
    y_side, t_side = sides
    return (
        _stop_onset(slur) == _stop_onset(other),
        (y_side.place(slur.start), y_side.place(slur.stop))
        == (t_side.place(other.start), t_side.place(other.stop)),
        (slur.start.pitches, slur.stop.pitches)
        == (other.start.pitches, other.stop.pitches),
        (slur.start.written, slur.stop.written)
        == (other.start.written, other.stop.written),
    )


def _slur_identical(
    slur: _Slur,
    other: _Slur,
    sides: tuple[_Side, _Side],
) -> bool:
    return slur.start.grace_position == other.start.grace_position and all(
        _slur_preference(slur, other, sides),
    )


def _stop_onset(slur: _Slur) -> tuple[int, Fraction, int]:
    return slur.stop.measure, *slur.stop.position


def _tie_stops(side: _Side) -> Iterator[tuple[_Key, bool]]:
    """Yield each tie stop, exempt when musicdiff scores its tie."""
    touched_starts = {
        (event.place, event.end, event.written_pitch(note))
        for event in side.score.xml.events
        if id(event) in side.touched
        for note in event.notes
        if _tied(note, _START)
    }
    for event in side.score.xml.events:
        for note in event.notes:
            if not _tied(note, _STOP):
                continue
            pitch = event.written_pitch(note)
            yield (
                side.note_key(event, note),
                id(event) in side.touched
                or (event.place, event.start, pitch) in touched_starts,
            )


def _tied(note: ET.Element, kind: str) -> bool:
    """Whether a member has a ``tie`` of one type.

    A ``notations/tied`` only displays a tie; music21 reads ties from ``tie``.
    """
    return any(mark.get("type") == kind for mark in note.findall("tie"))
