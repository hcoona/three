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

Every function that takes a score takes a ``Path`` to a MusicXML file or the
MusicXML text as a ``str``; a ``str`` is never read as a path.
"""

from __future__ import annotations

import re
from collections import Counter
from dataclasses import dataclass
from fractions import Fraction
from itertools import pairwise
from typing import TYPE_CHECKING

import music21 as m21
from music21.common.numberTools import opFrac
from musicdiff.annotation import AnnExtra, AnnNote

from sheet_music_omr.comparison import (
    AnnotatedScore,
    ContentResult,
    ElementTally,
    NotePair,
    UndeterminedReason,
    annotate,
    compare_annotated,
    content_element_count,
    extra_sites,
    heading_tally,
    voice_element_count,
    voice_order_result,
)
from sheet_music_omr.model import DynamicMark
from sheet_music_omr.musicxml_events import (
    BRACKET,
    DASHES,
    SLUR,
    WAVY_LINE,
    ProtocolDefectError,
    XmlDirection,
    XmlEvent,
    XmlSpan,
    _link_cross_barline_slices,
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
    "voice_line": "voice",
    "notehead_parentheses": "pitch",
    "accidental_parentheses": "accidental",
    "metronome_parentheses": "tempo_rehearsal",
    "other_dynamics": "dynamic",
    "pedal_ends": "pedal",
    "tremolo_strokes": "tremolo",
    "beam_hooks": "beam",
    "rest_beams": "beam",
    "cross_barline_beams": "beam",
    "tuplet_ratio": "tuplet",
    "slur_endpoints": "slur",
    "tie_ends": "tie",
}
"""Each supplementary check and the content category it scores."""

_RULE_1 = "cross_staff"
_TUPLET = "tuplet"
_SLUR = "slur"
_HOOKS = frozenset({"forward hook", "backward hook"})
_FINGERS = frozenset("12345")
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
        excluded_voices: The excluded reference voices of the voice-order
            check, by reason: voices of ``Y`` with a content partner whose
            canonical positions the format's rules do not determine. They are
            neither elements nor agreement, and are reported beside the
            voice-order elements.
    """

    content: ContentResult
    checks: dict[str, ElementTally]
    excluded_voices: Counter[UndeterminedReason]

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
    voice_order = voice_order_result(t_score, y_score)
    checks = {
        "fingering": _multiset(
            _compared_fingerings(y_side),
            _compared_fingerings(t_side),
        ),
        "cross_staff": _multiset(
            map(_cross_staff_key, y_cross),
            map(_cross_staff_key, t_cross),
        ),
        "voice_order": voice_order.tally,
        "heading": heading_tally(t_source, y_source),
        **_line_checks(y_side, t_side),
        **_mark_checks(y_side, t_side),
        "pedal_ends": _paired_values(
            _pedal_ends(y_side, _pedal_touches(content)),
            _pedal_ends(t_side, frozenset()),
        ),
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
        voice_order.excluded,
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
    # their positions, undetermined ones included; its heading lines match
    # themselves, one element each.
    elements: dict[str, Iterable[object]] = {
        "fingering": _fingerings(side),
        "cross_staff": _cross_staff_events(score),
        "wavy_line": _wavy_lines(side),
        "dashes": _dashes_lines(side),
        "voice_line": _voice_lines(side),
        "notehead_parentheses": _parentheses(side, "notehead"),
        "accidental_parentheses": _parentheses(side, "accidental"),
        "metronome_parentheses": _metronome_parentheses(side),
        "other_dynamics": _other_dynamics(side),
        "pedal_ends": _pedal_ends(side, frozenset()),
        "tremolo_strokes": _tremolos(side),
        "beam_hooks": _hooks(side),
        "rest_beams": _rest_beams(side),
        "cross_barline_beams": _cross_barline_beams(side),
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


_SMUFL_SUB = re.compile(
    r"fingering([1-5])fingeringSubstitution(?:Above|Below)fingering([1-5])"
)
_DASH_SUB = re.compile(r"([1-5])[-\u2013]([1-5])")
_SMUFL_SUB_MARKS = frozenset(
    {"fingeringSubstitutionAbove", "fingeringSubstitutionBelow"}
)
_STANDARD_DYNAMICS = frozenset(mark.value for mark in DynamicMark)


def _fingerings(side: _Side) -> Iterator[_Key]:
    """Yield fingering elements of one score, falling back to raw tokens."""
    for event in side.score.xml.events:
        for note in event.notes:
            marks = note.findall("notations/technical/fingering")
            if not marks:
                continue
            base_key = side.note_key(event, note)
            parsed = _try_note_fingerings(base_key, marks)
            if parsed is not None:
                yield from parsed
            else:
                for mark in marks:
                    for ch in "".join((mark.text or "").split()):
                        yield (*base_key, 1, ch)


def _compared_fingerings(side: _Side) -> Iterator[_Key]:
    """Yield the fingering elements of one side of a comparison.

    Raises:
        ProtocolDefectError: If a fingering writes an unsupported form.
    """
    for event in side.score.xml.events:
        for note in event.notes:
            marks = note.findall("notations/technical/fingering")
            if not marks:
                continue
            base_key = side.note_key(event, note)
            parsed = _try_note_fingerings(base_key, marks)
            if parsed is None:
                texts = ["".join((m.text or "").split()) for m in marks]
                message = f"a fingering writes {texts!r}, not a finger"
                raise ProtocolDefectError(message)
            yield from parsed


def _single_mark_fingering(
    base_key: _Key, text: str
) -> tuple[_Key, ...] | None:
    if len(text) == 1 and text in _FINGERS:
        return ((*base_key, 1, text),)
    if (
        len(text) == 2  # noqa: PLR2004
        and text[0] in _FINGERS
        and text[1] in _FINGERS
        and text[0] != text[1]
    ):
        return ((*base_key, 1, text[0]), (*base_key, 2, text[1]))
    matched = _DASH_SUB.fullmatch(text) or _SMUFL_SUB.fullmatch(text)
    if matched and matched.group(1) != matched.group(2):
        return ((*base_key, "sub", matched.group(1), matched.group(2)),)
    return None


def _try_note_fingerings(
    base_key: _Key, marks: list[ET.Element]
) -> tuple[_Key, ...] | None:
    if any(mark.get("alternate") == "yes" for mark in marks):
        return None
    texts = ["".join((mark.text or "").split()) for mark in marks]
    subs = [mark.get("substitution") == "yes" for mark in marks]
    result: tuple[_Key, ...] | None = None
    if len(marks) == 1 and not subs[0]:
        result = _single_mark_fingering(base_key, texts[0])
    elif (
        len(marks) == 2  # noqa: PLR2004
        and not subs[0]
        and all(len(t) == 1 and t in _FINGERS for t in texts)
        and texts[0] != texts[1]
    ):
        result = (
            ((*base_key, "sub", texts[0], texts[1]),)
            if subs[1]
            else ((*base_key, 1, texts[0]), (*base_key, 2, texts[1]))
        )
    elif (
        len(marks) == 3  # noqa: PLR2004
        and not any(subs)
        and len(texts[0]) == 1
        and len(texts[1]) == 1
        and texts[0] in _FINGERS
        and texts[1] in _FINGERS
        and texts[0] != texts[1]
        and texts[2] in _SMUFL_SUB_MARKS
    ):
        result = ((*base_key, "sub", texts[0], texts[1]),)
    return result


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
        "voice_line": _multiset(_voice_lines(y_side), _voice_lines(t_side)),
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


def _voice_lines(side: _Side) -> Iterator[_Key]:
    for span in side.score.xml.spans:
        if span.family != BRACKET:
            continue
        start, stop = span.start, span.stop
        if not isinstance(start, XmlDirection) or not isinstance(
            stop,
            XmlDirection,
        ):
            message = "a bracket span does not join two directions"
            raise ProtocolDefectError(message)
        yield (
            (start.measure, start.place, start.onset),
            (stop.measure, stop.place, stop.onset),
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
        "metronome_parentheses": _multiset(
            _metronome_parentheses(y_side),
            _metronome_parentheses(t_side),
        ),
        "other_dynamics": _paired_values(
            _other_dynamics(y_side),
            _other_dynamics(t_side),
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
        "cross_barline_beams": _exempt_multiset(
            _cross_barline_beams(y_side),
            _cross_barline_beams(t_side),
        ),
    }


def _metronome_parentheses(side: _Side) -> Iterator[_Key]:
    for direction in side.score.xml.directions:
        for metronome in direction.element.findall("direction-type/metronome"):
            if metronome.get("parentheses") == "yes":
                yield (
                    direction.measure,
                    direction.place,
                    direction.onset,
                    (metronome.findtext("beat-unit") or "").strip(),
                    len(metronome.findall("beat-unit-dot")),
                    (metronome.findtext("per-minute") or "").strip(),
                )


def _other_dynamics(side: _Side) -> Iterator[tuple[_Key, Hashable]]:
    for direction in side.score.xml.directions:
        for other in direction.element.findall(
            "direction-type/dynamics/other-dynamics"
        ):
            trimmed = (other.text or "").strip()
            if trimmed and trimmed not in _STANDARD_DYNAMICS:
                yield (
                    (direction.measure, direction.place, direction.onset),
                    trimmed,
                )


type _PedalTouch = tuple[int, Fraction, Fraction]


def _extra_pedal_interval(
    obj: object,
    score: AnnotatedScore,
    sites: dict[int, _Site],
) -> _PedalTouch | None:
    if not isinstance(obj, AnnExtra) or id(obj) not in sites:
        return None
    (part_idx, _), m_idx, onset = sites[id(obj)]
    if m_idx >= len(score.xml.measure_starts):
        return None
    m_start = score.xml.measure_starts[m_idx]
    if onset is None:
        m_end = m_start + score.xml.measure_lengths[m_idx]
        return (part_idx, m_start, m_end)
    start_q = m_start + onset
    dur_q = Fraction(0) if obj.duration is None else Fraction(str(obj.duration))
    return (part_idx, start_q, start_q + max(Fraction(0), dur_q))


def _pedal_touches(content: ContentResult) -> tuple[_PedalTouch, ...]:
    y_sites = extra_sites(content.y_score)
    t_sites = extra_sites(content.t_score)
    touches: set[_PedalTouch] = set()
    for diff in content.counted:
        if "pedal" not in diff.categories:
            continue
        for obj, score, sites in (
            (diff.y_object, content.y_score, y_sites),
            (diff.t_object, content.t_score, t_sites),
        ):
            interval = _extra_pedal_interval(obj, score, sites)
            if interval is not None:
                touches.add(interval)
    return tuple(touches)


def _is_terminal_discontinue(
    items: list[tuple[XmlDirection, ET.Element]], idx: int, num: str
) -> bool:
    later = next(
        (
            other.get("type")
            for _, other in items[idx + 1 :]
            if (other.get("number") or "1") == num
            and other.get("type") in {"resume", _START}
        ),
        None,
    )
    return later != "resume"


def _pedal_span_key(start_dir: XmlDirection, end_dir: XmlDirection) -> _Key:
    return (
        (start_dir.measure, start_dir.part, start_dir.onset),
        (end_dir.measure, end_dir.part, end_dir.onset),
    )


def _pedal_item_rank(
    item: tuple[XmlDirection, ET.Element],
) -> tuple[Fraction, int, int]:
    direction, pedal = item
    closing = (pedal.get("type") or "") in {_STOP, "change", "discontinue"}
    return (direction.start, 0 if closing else 1, direction.order)


def _pedal_ends(
    side: _Side,
    touched_intervals: Iterable[_PedalTouch],
) -> Iterator[tuple[_Key, Hashable]]:
    touches = tuple(touched_intervals)
    by_part: dict[int, list[tuple[XmlDirection, ET.Element]]] = {}
    for direction in side.score.xml.directions:
        for pedal in direction.element.findall("direction-type/pedal"):
            if pedal.get("line") == "yes":
                by_part.setdefault(direction.part, []).append(
                    (direction, pedal)
                )
    for part_idx, raw_items in by_part.items():
        items = sorted(raw_items, key=_pedal_item_rank)
        opened: dict[str, XmlDirection] = {}
        for idx, (direction, pedal) in enumerate(items):
            kind = pedal.get("type") or ""
            num = pedal.get("number") or "1"
            if kind == _START:
                opened[num] = direction
                continue
            closes = kind == _STOP or (
                kind == "discontinue"
                and _is_terminal_discontinue(items, idx, num)
            )
            if not closes:
                continue
            start_dir = opened.pop(num, None)
            if start_dir is None:
                continue
            overlaps_touch = any(
                t_part == part_idx
                and t0 <= direction.start
                and start_dir.start <= t1
                for t_part, t0, t1 in touches
            )
            if not overlaps_touch:
                yield (_pedal_span_key(start_dir, direction), kind)


def _cross_barline_beams(side: _Side) -> Iterator[tuple[_Key, bool]]:
    lengths = side.score.xml.measure_lengths
    for chain in _link_cross_barline_slices(list(side.score.xml.events)):
        for (m_idx, events_m), (next_idx, events_next) in pairwise(chain):
            if next_idx != m_idx + 1 or m_idx >= len(lengths):
                continue
            mains_m = sorted(
                (ev for ev in events_m if not ev.grace),
                key=lambda item: (item.onset, item.order),
            )
            ordered_next = sorted(
                events_next,
                key=lambda item: (item.onset, not item.grace, item.order),
            )
            mains_next = [ev for ev in ordered_next if not ev.grace]
            if not mains_m or not mains_next:
                continue
            last_m = mains_m[-1]
            first_next = mains_next[0]
            has_leading_grace = any(
                ev.grace and ev.onset == 0 and ev.order < first_next.order
                for ev in ordered_next
            )
            if (
                last_m.rest
                or first_next.rest
                or last_m.onset + last_m.duration != lengths[m_idx]
                or first_next.onset != 0
                or has_leading_grace
            ):
                continue
            next_beams: dict[str, str] = {
                (b.get("number") or "1"): (b.text or "").strip()
                for b in first_next.notes[0].findall("beam")
            }
            for beam in last_m.notes[0].findall("beam"):
                num = beam.get("number") or "1"
                if (beam.text or "").strip() in {
                    "begin",
                    "continue",
                } and next_beams.get(num) in {"continue", "end"}:
                    yield (
                        (
                            m_idx,
                            side.place(last_m),
                            side.place(first_next),
                            num,
                            last_m.pitches,
                            last_m.written,
                            first_next.pitches,
                            first_next.written,
                        ),
                        id(last_m) in side.touched
                        or id(first_next) in side.touched,
                    )


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
    scored = [
        slur
        for slur, duration in y_slurs
        if not (
            (
                (touched := touches.get(_slur_site(slur))) is not None
                # A touched slur whose duration is unknown, or that no ``Y``
                # slur of its site matches, leaves the site to musicdiff.
                and (
                    duration is None
                    or duration in touched
                    or touched.isdisjoint(durations[_slur_site(slur)])
                )
            )
            or id(slur.start) in y_side.touched
            or id(slur.stop) in y_side.touched
        )
    ]
    sides = (y_side, t_side)
    used: set[int] = set()
    unpaired: list[_Slur] = []
    # Identity is the equality of one key, so pairing identical slurs first
    # gives every identical pair whatever the order of either score's slurs.
    for slur in scored:
        partner = next(
            (
                index
                for index, other in enumerate(candidates)
                if index not in used
                and _slur_matches(slur, other)
                and _slur_identical(slur, other, sides)
            ),
            None,
        )
        if partner is None:
            unpaired.append(slur)
        else:
            used.add(partner)
    identical = len(scored) - len(unpaired)
    elements = identical
    for slur in unpaired:
        matches = [
            index
            for index, other in enumerate(candidates)
            if index not in used and _slur_matches(slur, other)
        ]
        if not matches:
            continue
        used.add(
            max(
                matches,
                key=lambda index: _slur_preference(
                    slur, candidates[index], sides
                ),
            ),
        )
        elements += 1
    return ElementTally(elements, identical, 0)


def _slur_matches(slur: _Slur, other: _Slur) -> bool:
    """Whether ``other`` starts at the onset and ends when ``slur`` does."""
    return (other.start.measure, other.start.onset) == (
        slur.start.measure,
        slur.start.onset,
    ) and other.stop.end == slur.stop.end


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
    xml = side.score.xml
    staves = voice_staves(xml.events)
    first_in_voice: set[int] = set()
    seen_voices: set[tuple[int, int, str, int | None]] = set()
    for event in sorted(
        xml.events, key=lambda item: (item.measure, item.onset, item.order)
    ):
        if event.grace:
            continue
        vkey = staves[id(event)][0]
        if vkey not in seen_voices:
            seen_voices.add(vkey)
            if event.onset == 0:
                first_in_voice.add(id(event))

    touched_starts = {
        (
            side.place(event),
            (event.part, staves[id(event)][1]),
            event.end,
            event.written_pitch(note),
        )
        for event in xml.events
        if id(event) in side.touched
        for note in event.notes
        if _tied(note, _START)
    }

    for event in xml.events:
        tie_in_src = (
            xml.tie_in_sources.get(event.measure)
            if id(event) in first_in_voice
            else None
        )
        for note in event.notes:
            has_playback = _tied(note, _STOP)
            has_notated = tie_in_src is not None and _notated_tied(note, _STOP)
            if not (has_playback or has_notated):
                continue
            pitch = event.written_pitch(note)
            own = (event.part, staves[id(event)][1])
            shown = side.place(event)
            exempt = id(event) in side.touched or any(
                end == event.start
                and p == pitch
                and (s_shown == shown or s_own == own)
                for s_shown, s_own, end, p in touched_starts
            )
            if not exempt and tie_in_src is not None:
                exempt = _tie_in_source_touched(
                    side, staves, tie_in_src, (own, shown, pitch)
                )
            yield (side.note_key(event, note), exempt)


def _tie_in_source_touched(
    side: _Side,
    staves: dict[int, tuple[tuple[int, int, str, int | None], int]],
    source_measure: int,
    target: tuple[_Place, _Place, object],
) -> bool:
    own, shown, pitch = target
    xml = side.score.xml
    if source_measure >= len(xml.measure_starts):
        return False
    source_end = (
        xml.measure_starts[source_measure] + xml.measure_lengths[source_measure]
    )
    own_sources = [
        ev
        for ev in xml.events
        if ev.measure == source_measure
        and not ev.grace
        and ev.end == source_end
        and (ev.part, staves[id(ev)][1]) == own
        and any(ev.written_pitch(n) == pitch for n in ev.notes)
    ]
    candidates = own_sources or [
        ev
        for ev in xml.events
        if ev.measure == source_measure
        and not ev.grace
        and ev.end == source_end
        and side.place(ev) == shown
        and any(ev.written_pitch(n) == pitch for n in ev.notes)
    ]
    return any(id(ev) in side.touched for ev in candidates)


def _tied(note: ET.Element, kind: str) -> bool:
    """Whether a member has a ``tie`` of one type."""
    return any(mark.get("type") == kind for mark in note.findall("tie"))


def _notated_tied(note: ET.Element, kind: str) -> bool:
    """Whether a member has a ``notations/tied`` of one type."""
    return any(
        mark.get("type") == kind for mark in note.findall("notations/tied")
    )
