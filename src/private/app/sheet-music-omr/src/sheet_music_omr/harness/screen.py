"""Apply the comparison protocol's structural scope screen to one MusicXML file.

The screen is the protocol's "Structural scope screen" (comparison-protocol.md,
"Structural scope screen" and "Selection before conversion"). It applies to one
candidate MusicXML document, a PDMX root document or a Lieder piano extract,
and fails closed: a check that cannot be decided fails, and an excluded file
is counted by its first failing reason in the protocol's order. :data:`REASONS`
lists the reasons in that order. The last, the musicdiff parse within 300
seconds, is run by the caller with a timeout, so :func:`screen` never returns
it.

:data:`ALLOWLIST` and the attribute rules of :func:`allowlist_violations` are
written from the Text Score 2 format text (text-format.md: event flags, pitch,
relations, directions, marks and words, category encodings and unsupported
content) and the MusicXML 4.0 vocabulary only, independently of the converters
and of the comparator's element mapping. Each allowed entry cites the format
lines it comes from. An element or attribute value that the format does not
enumerate is a violation.

The input is parsed with :mod:`xml.etree.ElementTree`, which fetches no DTD
and resolves no external entity: a document that needs one is unreadable, and
an unreadable document fails the first reason.
"""

from __future__ import annotations

import re
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from decimal import Decimal
from fractions import Fraction
from types import MappingProxyType
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from collections.abc import Callable, Iterator, Mapping

REASONS: tuple[str, ...] = (
    "parts_staves",
    "clef",
    "clef_restated",
    "voices",
    "lyric_harmony_cue",
    "unsupported_element",
    "ending_number",
    "signature_position",
    "time_signature",
    "alter_accidental",
    "barline",
    "chord",
    "tremolo",
    "fingering",
    "bracket_dashes",
    "metronome",
    "beam_tuplet_span",
    "allowlist",
    "musicdiff_parse",
)
"""The exclusion reasons, one per protocol bullet, in the protocol's order.

The last, ``musicdiff_parse``, is the musicdiff 6.0 parse within 300 seconds,
which the caller runs with a timeout. :func:`screen` never returns it.
"""

REASON_TEXT: Mapping[str, str] = MappingProxyType(
    {
        "parts_staves": "not one part with two five-line staves throughout",
        "clef": "a clef other than G, F or C, line 1-5, octave change +-1",
        "clef_restated": "a clef restates the clef of its staff in the measure",
        "voices": "a staff has notes of more than four voices in one measure",
        "lyric_harmony_cue": "lyrics, harmony, figured bass or cue notes",
        "unsupported_element": (
            "multiple rest, measure repeat, segno, coda, breve, long, maxima,"
            " unpitched note, fret or string"
        ),
        "ending_number": "an ending without a single number",
        "signature_position": "a key or time after a timed note or forward",
        "time_signature": "a time without one beats/beat-type, whole beats",
        "alter_accidental": "a fractional alter or a microtonal accidental",
        "barline": "a barline other than ordinary, final, double or repeat",
        "chord": "a chord note on another staff, or a repeated chord pitch",
        "tremolo": "an unmeasured tremolo or a tremolo with no strokes",
        "fingering": "a fingering other than one digit 1-5 (two on a trill)",
        "bracket_dashes": "a bracket direction, or dashes that follow no words",
        "metronome": "a metronome without one q, h or e unit and per-minute",
        "beam_tuplet_span": "a beam or tuplet that stops outside its measure",
        "allowlist": "a notation or direction variant that the format lacks",
        "musicdiff_parse": "musicdiff 6.0 does not parse the file within 300 s",
    }
)
"""A short English description of each reason, for reports."""


@dataclass(frozen=True, slots=True)
class ScreenResult:
    """The outcome of the screen for one document.

    Attributes:
        reason: The first failing reason, one of :data:`REASONS`, or None when
            the document passes every structural check.
        detail: One short line that says what failed, such as the measure and
            the element, or an empty string on a pass. It names no file path.
    """

    reason: str | None
    detail: str

    @property
    def passed(self) -> bool:
        """Whether the document passed every structural check."""
        return self.reason is None


_DYNAMIC_MARKS = frozenset(
    {
        "pppp",
        "ppp",
        "pp",
        "p",
        "mp",
        "mf",
        "f",
        "ff",
        "fff",
        "ffff",
        "sf",
        "sfz",
        "sffz",
        "fp",
        "rf",
        "rfz",
        "fz",
        "sfp",
        "sfpp",
        "pf",
    }
)
"""The twenty dynamics of the format (L671-673)."""


ALLOWLIST: Mapping[str, frozenset[str]] = MappingProxyType(
    {
        # The children of `notations`.
        "notations": frozenset(
            {
                "tied",  # Relations, ties (L579); category `tie` (L1099).
                "slur",  # Relations, slurs (L619); category `slur` (L1101).
                "tuplet",  # Category `tuplet` `N:M{...}` (L1098).
                "ornaments",  # Event flags `orn=` (L491); see below.
                "technical",  # Container for fingering, see below.
                "articulations",  # Event flags, articulations (L481).
                # No `dynamics`: a dynamic is only a direction (L665, L671)
                # and the category `dynamic` is dynamic directions (L1116).
                "fermata",  # Event flags `ferm` and `ferm_` (L484).
                # Event flags `arp` (L486); arpeggio relation (L631).
                "arpeggiate",
            }
        ),
        # Event flags (L481): `stacc`, `acc`, `ten`, `stacciss`, `marc`, `port`.
        "articulations": frozenset(
            {
                "staccato",  # `stacc` (L481).
                "accent",  # `acc` (L481).
                "tenuto",  # `ten` (L481).
                "staccatissimo",  # `stacciss` (L481).
                "strong-accent",  # `marc` (L481).
                "detached-legato",  # `port`, "detached legato" (L481-482).
            }
        ),
        "ornaments": frozenset(
            {
                "trill-mark",  # `orn=trill` (L491).
                "mordent",  # `orn=mordent` (L491).
                "inverted-mordent",  # `orn=inverted-mordent` (L491).
                "turn",  # `orn=turn` (L492).
                "inverted-turn",  # `orn=inverted-turn` (L492).
                # Extended trill relation (L625); marks and words (L944).
                "wavy-line",
                # `tremolo=single:S` (L493); two-note tremolo (L621).
                "tremolo",
            }
        ),
        "technical": frozenset(
            {
                # Pitch suffix `f1` to `f5` (L548); category (L1114).
                "fingering",
            }
        ),
        # Directions (L671): the twenty enumerated dynamics. The format names
        # each by its printed text (L671-673, L315-316), not by a MusicXML
        # element, so `other-dynamics` with exactly that text is one of them.
        "dynamics": _DYNAMIC_MARKS | {"other-dynamics"},
        # Measure options (L235-240, L423-427): the closing barline style, the
        # repeat barlines and the volta brackets. A barline encodes nothing
        # else; a fermata, for one, is only an event flag (L484).
        "barline": frozenset({"bar-style", "repeat", "ending"}),
        "direction-type": frozenset(
            {
                # Expression text `<"text">` (L678), tempo text (L680).
                "words",
                "dynamics",  # Dynamic directions (L671).
                "wedge",  # Hairpins (L689).
                "octave-shift",  # Ottava lines (L692).
                "pedal",  # Pedal signs (L685) and pedal lines (L700).
                "dashes",  # Dashes lines (L703).
                "metronome",  # Tempo `q=120` (L680).
                "rehearsal",  # Rehearsal marks (L684).
            }
        ),
    }
)
"""The MusicXML element names that the format enumerates, per container.

Each key is a container element: ``notations``, ``articulations``,
``ornaments``, ``technical``, ``dynamics`` (wherever it appears), ``barline``
and ``direction-type``. Each value holds the child element names that the Text
Score 2 format enumerates. The allowlist is written from the format text, not
from the converters.
"""

_UNREADABLE = "unreadable"
_DECIMAL = re.compile(r"[+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)")
_INTEGER = re.compile(r"[+-]?[0-9]+")


def screen(data: bytes | str) -> ScreenResult:
    """Apply the structural scope screen to one MusicXML document.

    Args:
        data: The text of a partwise MusicXML document.

    Returns:
        The first failing reason and its detail, or a pass. An unreadable
        document, or one that is not ``score-partwise``, fails the first
        reason. The ``musicdiff_parse`` reason is never returned.
    """
    root = _parse(data)
    if root is None:
        return ScreenResult(REASONS[0], _UNREADABLE)
    if root.tag != "score-partwise":
        return ScreenResult(REASONS[0], "not score-partwise")
    for reason, check in _CHECKS:
        detail = check(root)
        if detail is not None:
            return ScreenResult(reason, detail)
    return ScreenResult(None, "")


def allowlist_violations(data: bytes | str) -> tuple[str, ...]:
    """Return every notation or direction variant outside :data:`ALLOWLIST`.

    This serves the allowlist review: it reports every violation, not only the
    first, whatever the other checks of the screen decide.

    Args:
        data: The text of a MusicXML document.

    Returns:
        The distinct violations, container by container in document order,
        each such as ``ornaments/shake`` or ``wedge@type=continue``, or
        ``("unreadable",)`` when the document cannot be parsed.
    """
    root = _parse(data)
    if root is None:
        return (_UNREADABLE,)
    return tuple(dict.fromkeys(fault for _, fault in _allowlist_faults(root)))


def _parse(data: bytes | str) -> ET.Element | None:
    try:
        # Expat fetches no DTD and resolves no external entity, and it limits
        # entity expansion.
        return ET.fromstring(data)  # noqa: S314 - see the comment above
    except ET.ParseError, ValueError, LookupError:
        return None


def _measures(root: ET.Element) -> list[ET.Element]:
    return root.findall("part/measure")


def _where(measure: ET.Element) -> str:
    return f"measure {measure.get('number', '?')}"


def _at(measure: ET.Element, what: str) -> str:
    return f"{_where(measure)}: {what}"


def _located(root: ET.Element) -> Iterator[tuple[str, ET.Element]]:
    """Yield every element of a document with the measure that holds it."""
    for child in root:
        if child.tag == "part":
            for measure in child:
                where = _where(measure)
                for element in measure.iter():
                    yield where, element
        else:
            for element in child.iter():
                yield "header", element


def _text(element: ET.Element, path: str) -> str | None:
    text = element.findtext(path)
    return None if text is None else text.strip()


def _attr(element: ET.Element, name: str, default: str = "") -> str:
    value = element.get(name)
    return default if value is None else value.strip()


def _staff(element: ET.Element) -> str:
    return _text(element, "staff") or "1"


def _voice(note: ET.Element) -> str:
    return _text(note, "voice") or "1"


def _decimal(text: str | None) -> Decimal | None:
    if text is None or not _DECIMAL.fullmatch(text.strip()):
        return None
    return Decimal(text.strip())


def _integer(text: str | None) -> int | None:
    if text is None or not _INTEGER.fullmatch(text.strip()):
        return None
    return int(text)


def _fraction(text: str | None) -> Fraction | None:
    value = _decimal(text)
    return None if value is None else Fraction(value)


_STAFF_NUMBERS = frozenset({"1", "2"})
_CLEF_SIGNS = frozenset({"G", "F", "C"})
_CLEF_LINES = frozenset(range(1, 6))
_CLEF_OCTAVE_CHANGES = frozenset({-1, 1})
_MAX_VOICES = 4


def _check_parts_staves(root: ET.Element) -> str | None:
    """Check one part with two five-line staves throughout, without TAB."""
    parts = root.findall("part")
    score_parts = root.findall("part-list/score-part")
    if len(parts) != 1 or len(score_parts) != 1:
        return f"{len(parts)} parts, {len(score_parts)} score-parts"
    first = parts[0].find("measure/attributes")
    if first is None or _text(first, "staves") != "2":
        return "the first attributes do not give two staves"
    for measure in _measures(root):
        fault = _staff_fault(measure)
        if fault is not None:
            return _at(measure, fault)
    return None


def _staff_fault(measure: ET.Element) -> str | None:
    for staves in measure.iter("staves"):
        if (staves.text or "").strip() != "2":
            return f"staves {staves.text}"
    for staff in measure.iter("staff"):
        if (staff.text or "").strip() not in _STAFF_NUMBERS:
            return f"staff {staff.text}"
    for details in measure.iter("staff-details"):
        if (
            _attr(details, "number", "1") not in _STAFF_NUMBERS
            or _text(details, "staff-lines") not in {None, "5"}
            or _text(details, "staff-type") not in {None, "regular"}
            or details.find("staff-tuning") is not None
        ):
            return "staff-details other than five regular lines"
    for clef in measure.iter("clef"):
        if _text(clef, "sign") in {"percussion", "TAB"}:
            return f"clef {_text(clef, 'sign')}"
        if _attr(clef, "number", "1") not in _STAFF_NUMBERS:
            return f"clef of staff {_attr(clef, 'number')}"
    return None


def _clef_key(clef: ET.Element) -> tuple[str, int, int] | None:
    """Return a clef's sign, line and octave change, or None if unsupported."""
    sign = _text(clef, "sign")
    line = _integer(clef.findtext("line"))
    if (
        sign is None
        or line is None
        or sign not in _CLEF_SIGNS
        or line not in _CLEF_LINES
    ):
        return None
    change = clef.findtext("clef-octave-change")
    if change is None:
        return sign, line, 0
    octave = _integer(change)
    if octave is None or octave not in _CLEF_OCTAVE_CHANGES:
        return None
    return sign, line, octave


def _check_clef(root: ET.Element) -> str | None:
    """Check that every clef is G, F or C on a line, octave change -1 or 1."""
    for measure in _measures(root):
        for clef in measure.iter("clef"):
            if _clef_key(clef) is None:
                sign = _text(clef, "sign")
                line = _text(clef, "line")
                change = _text(clef, "clef-octave-change")
                return _at(measure, f"clef {sign} line {line} change {change}")
    return None


def _check_clef_restated(root: ET.Element) -> str | None:
    """Check that no clef restates the clef of its staff in its measure."""
    for measure in _measures(root):
        last: dict[str, tuple[str, int, int] | None] = {}
        for clef in measure.iter("clef"):
            staff = _attr(clef, "number", "1")
            key = _clef_key(clef)
            if staff in last and last[staff] == key:
                return _at(measure, f"clef of staff {staff} restated")
            last[staff] = key
    return None


def _check_voices(root: ET.Element) -> str | None:
    """Check that no staff has notes of more than four voices in a measure."""
    for measure in _measures(root):
        voices: dict[str, set[str]] = {}
        for note in measure.findall("note"):
            voices.setdefault(_staff(note), set()).add(_voice(note))
        for staff, values in voices.items():
            if len(values) > _MAX_VOICES:
                return _at(measure, f"{len(values)} voices on staff {staff}")
    return None


def _check_lyric_harmony_cue(root: ET.Element) -> str | None:
    """Check for lyrics, harmony, figured bass and cue notes."""
    for where, element in _located(root):
        if element.tag in {"lyric", "harmony", "figured-bass", "cue"}:
            return f"{where}: {element.tag}"
        if element.tag == "type" and _attr(element, "size") == "cue":
            return f"{where}: type size=cue"
    return None


_UNSUPPORTED_TAGS = frozenset(
    {
        "multiple-rest",
        "measure-repeat",
        "segno",
        "coda",
        "unpitched",
        "fret",
        "string",
    }
)
_LONG_TYPES = frozenset({"breve", "long", "maxima"})


def _check_unsupported_element(root: ET.Element) -> str | None:
    """Check for the unsupported elements and note types that the list names."""
    for where, element in _located(root):
        if element.tag in _UNSUPPORTED_TAGS:
            return f"{where}: {element.tag}"
        note_type = (element.text or "").strip()
        if element.tag == "type" and note_type in _LONG_TYPES:
            return f"{where}: type {note_type}"
    return None


_ENDING_NUMBER = re.compile(r"[1-9][0-9]*")


def _check_ending_number(root: ET.Element) -> str | None:
    """Check that every ending has a single number."""
    for measure in _measures(root):
        for ending in measure.iter("ending"):
            number = ending.get("number")
            if number is None or not _ENDING_NUMBER.fullmatch(number.strip()):
                return _at(measure, f"ending number {number!r}")
    return None


def _check_signature_position(root: ET.Element) -> str | None:
    """Check that keys and times precede the first timed note or forward."""
    for measure in _measures(root):
        timed = False
        for child in measure:
            if (
                timed
                and child.tag == "attributes"
                and (
                    child.find("key") is not None
                    or child.find("time") is not None
                )
            ):
                return _at(measure, "key or time after a timed note or forward")
            if (
                child.tag in {"note", "forward"}
                and child.find("duration") is not None
            ):
                timed = True
    return None


_POSITIVE_INTEGER = re.compile(r"0*[1-9][0-9]*")


def _check_time_signature(root: ET.Element) -> str | None:
    """Check that every time has one beats/beat-type pair with whole beats."""
    for measure in _measures(root):
        for time in measure.iter("time"):
            beats = list(time.iter("beats"))
            beat_types = list(time.iter("beat-type"))
            if (
                time.find("senza-misura") is not None
                or len(beats) != 1
                or len(beat_types) != 1
                or not _POSITIVE_INTEGER.fullmatch(
                    (beats[0].text or "").strip()
                )
            ):
                texts = [beat.text for beat in beats]
                return _at(measure, f"time with beats {texts}")
    return None


_PLAIN_ACCIDENTALS = frozenset(
    {
        "sharp",
        "natural",
        "flat",
        "double-sharp",
        "sharp-sharp",
        "flat-flat",
        "natural-sharp",
        "natural-flat",
        "triple-sharp",
        "triple-flat",
    }
)
"""The MusicXML 4.0 accidental values that are neither microtonal nor other.

Every other value of the accidental-value enumeration is a quarter-tone,
arrow, slash, numbered, Persian or ``other`` value, which fails.
"""
_ACCIDENTAL_TAGS = frozenset(
    {
        "accidental",
        "accidental-mark",
        "key-accidental",
        "accidental-text",
    }
)


def _check_alter_accidental(root: ET.Element) -> str | None:
    """Check whole-number alters and accidentals that are not microtonal."""
    for where, element in _located(root):
        text = (element.text or "").strip()
        if element.tag == "alter":
            value = _decimal(text)
            if value is None or value != value.to_integral_value():
                return f"{where}: alter {text}"
        elif element.tag in _ACCIDENTAL_TAGS and text not in _PLAIN_ACCIDENTALS:
            return f"{where}: {element.tag} {text}"
    return None


_PLAIN_BAR_STYLES = frozenset({None, "regular", "light-light", "light-heavy"})


def _check_barline(root: ET.Element) -> str | None:
    """Check ordinary, final, double and repeat barlines."""
    for measure in _measures(root):
        for barline in measure.iter("barline"):
            style = _text(barline, "bar-style")
            repeat = barline.find("repeat")
            if style in _PLAIN_BAR_STYLES or (
                style == "heavy-light"
                and repeat is not None
                and _attr(repeat, "direction") in {"backward", "forward"}
            ):
                continue
            return _at(measure, f"bar-style {style}")
    return None


def _pitch(note: ET.Element) -> tuple[str, Decimal, int] | None:
    pitch = note.find("pitch")
    if pitch is None:
        return None
    step = _text(pitch, "step")
    octave = _integer(pitch.findtext("octave"))
    alter_text = pitch.findtext("alter")
    alter = Decimal(0) if alter_text is None else _decimal(alter_text)
    if step is None or octave is None or alter is None:
        return None
    return step, alter, octave


def _check_chord(root: ET.Element) -> str | None:
    """Check chord notes against the staff and pitches of their chord."""
    for measure in _measures(root):
        head: ET.Element | None = None
        pitches: set[tuple[str, Decimal, int] | None] = set()
        for note in measure.findall("note"):
            pitch = _pitch(note)
            if note.find("chord") is None:
                head, pitches = note, {pitch}
            elif head is None or pitch is None or None in pitches:
                return _at(measure, "a chord note without pitched chord head")
            elif _staff(note) != _staff(head):
                return _at(measure, f"chord note on staff {_staff(note)}")
            elif pitch in pitches:
                return _at(measure, f"chord repeats {pitch[0]}{pitch[2]}")
            else:
                pitches.add(pitch)
    return None


def _check_tremolo(root: ET.Element) -> str | None:
    """Check for unmeasured tremolos and tremolos without strokes."""
    for measure in _measures(root):
        for tremolo in measure.iter("tremolo"):
            strokes = _integer(tremolo.text)
            if _attr(tremolo, "type") == "unmeasured" or not strokes:
                kind = _attr(tremolo, "type")
                return _at(measure, f"tremolo type {kind} strokes {strokes}")
    return None


_FINGER = re.compile(r"[1-5]")
_TRILL_FINGERINGS = 2


def _check_fingering(root: ET.Element) -> str | None:
    """Check one digit 1-5 per note, or two different digits on a trill."""
    for measure in _measures(root):
        for note in measure.findall("note"):
            fault = _fingering_fault(note)
            if fault is not None:
                return _at(measure, fault)
    return None


def _fingering_fault(note: ET.Element) -> str | None:
    fingerings = note.findall("notations/technical/fingering")
    for fingering in fingerings:
        if "yes" in {
            _attr(fingering, "substitution"),
            _attr(fingering, "alternate"),
        }:
            return "substitution or alternate fingering"
        if not _FINGER.fullmatch(fingering.text or ""):
            return f"fingering {fingering.text!r}"
    if len(fingerings) <= 1:
        return None
    if (
        len(fingerings) == _TRILL_FINGERINGS
        and fingerings[0].text != fingerings[1].text
        and note.find("notations/ornaments/trill-mark") is not None
    ):
        return None
    return f"{len(fingerings)} fingerings"


def _check_bracket_dashes(root: ET.Element) -> str | None:
    """Check for brackets, and that every dashes start follows words.

    A dashes line is the dashed extension of expression text, and its start
    records that text (format L703-705). The words are earlier in the start's
    own direction, or in a preceding direction of the same measure, staff and
    onset.
    """
    worded: set[tuple[str, Fraction]] = set()
    current: ET.Element | None = None
    for measure, direction, onset in _directions(root):
        if measure is not current:
            current = measure
            worded.clear()
        preceded = onset is not None and (_staff(direction), onset) in worded
        fault, has_words = _dashes_fault(direction, preceded=preceded)
        if fault is not None:
            return _at(measure, fault)
        if has_words and onset is not None:
            worded.add((_staff(direction), onset))
    return None


def _dashes_fault(
    direction: ET.Element, *, preceded: bool
) -> tuple[str | None, bool]:
    """Return a direction's bracket or dashes fault and whether it has words."""
    has_words = False
    for direction_type in direction.findall("direction-type"):
        for mark in direction_type:
            if mark.tag == "bracket":
                return "bracket", has_words
            if (
                mark.tag == "dashes"
                and _attr(mark, "type") == "start"
                and not (has_words or preceded)
            ):
                return "dashes start without words", has_words
            if mark.tag == "words" and (mark.text or "").strip():
                has_words = True
    return None, has_words


def _directions(
    root: ET.Element,
) -> Iterator[tuple[ET.Element, ET.Element, Fraction | None]]:
    """Yield each direction with its measure and onset, None if undecidable.

    The onset is in quarter notes from the start of the measure, with the
    direction's offset, following the measure's notes, backups and forwards.
    """
    divisions: Fraction | None = None
    for measure in _measures(root):
        position: Fraction | None = Fraction(0)
        for child in measure:
            if (
                child.tag == "attributes"
                and child.find("divisions") is not None
            ):
                divisions = _fraction(child.findtext("divisions"))
                if divisions is not None and divisions <= 0:
                    divisions = None
            elif child.tag == "direction":
                yield (
                    measure,
                    child,
                    _shift(position, child, divisions, "offset"),
                )
            elif (
                child.tag in {"note", "forward"} and child.find("chord") is None
            ):
                position = _shift(position, child, divisions, "duration")
            elif child.tag == "backup":
                position = _shift(position, child, divisions, "duration", -1)


def _shift(
    position: Fraction | None,
    element: ET.Element,
    divisions: Fraction | None,
    path: str,
    sign: int = 1,
) -> Fraction | None:
    """Return a position moved by an element's duration or offset."""
    text = element.findtext(path)
    if text is None:
        return position
    amount = _fraction(text)
    if position is None or divisions is None or amount is None:
        return None
    return position + sign * amount / divisions


_BEAT_UNITS = frozenset({"quarter", "half", "eighth"})
_DOTTED_BEAT_UNITS = frozenset({"quarter", "half"})
_POS_INT = re.compile(r"[1-9][0-9]*")


def _check_metronome(root: ET.Element) -> str | None:
    """Check one quarter, half or eighth beat unit and a per-minute value."""
    for measure in _measures(root):
        for metronome in measure.iter("metronome"):
            fault = _metronome_fault(metronome)
            if fault is not None:
                return _at(measure, fault)
    return None


def _metronome_fault(metronome: ET.Element) -> str | None:
    units = metronome.findall("beat-unit")
    if (
        len(units) != 1
        or metronome.find("beat-unit-tied") is not None
        or metronome.find("metronome-note") is not None
    ):
        return f"metronome with {len(units)} beat units"
    unit = (units[0].text or "").strip()
    dots = len(metronome.findall("beat-unit-dot"))
    if (
        unit not in _BEAT_UNITS
        or dots > 1
        or (dots and unit not in _DOTTED_BEAT_UNITS)
    ):
        return f"metronome beat unit {unit} with {dots} dots"
    # The value is a positive integer, `pos_int` (L314, L357, L682-683).
    rates = metronome.findall("per-minute")
    if len(rates) != 1 or not _POS_INT.fullmatch((rates[0].text or "").strip()):
        return "metronome without a positive integer per-minute value"
    return None


def _check_beam_tuplet_span(root: ET.Element) -> str | None:
    """Check that every beam level and tuplet that starts stops in its measure.

    Beams are matched by voice, grace and level, and tuplets by voice and
    number. A cross-staff beam changes staff, so staff is not a key. A chord
    has one beam, so the beams of its other notes are not counted again.
    """
    for measure in _measures(root):
        spans: dict[tuple[str, ...], int] = {}
        for note in measure.findall("note"):
            voice = _voice(note)
            grace = "grace" if note.find("grace") is not None else ""
            beams = (
                [] if note.find("chord") is not None else note.findall("beam")
            )
            for beam in beams:
                level = _attr(beam, "number", "1")
                state = (beam.text or "").strip()
                _count(
                    spans, ("beam", voice, grace, level), state, "begin", "end"
                )
            for tuplet in note.findall("notations/tuplet"):
                number = _attr(tuplet, "number", "1")
                state = _attr(tuplet, "type")
                _count(spans, ("tuplet", voice, number), state, "start", "stop")
        unclosed = [key for key, count in spans.items() if count > 0]
        if unclosed:
            kind, voice, *_, number = unclosed[0]
            return _at(
                measure, f"{kind} {number} of voice {voice} does not stop"
            )
    return None


def _count(
    spans: dict[tuple[str, ...], int],
    key: tuple[str, ...],
    state: str,
    start: str,
    stop: str,
) -> None:
    if state == start:
        spans[key] = spans.get(key, 0) + 1
    elif state == stop and spans.get(key, 0) > 0:
        spans[key] -= 1


def _check_allowlist(root: ET.Element) -> str | None:
    """Check that every notation and direction variant is in the allowlist."""
    first = next(_allowlist_faults(root), None)
    return None if first is None else f"{first[0]}: {first[1]}"


def _allowlist_faults(root: ET.Element) -> Iterator[tuple[str, str]]:
    """Yield each measure and allowlist fault, container by container."""
    for measure in root.iter("measure"):
        where = _where(measure)
        for child in measure:
            note = child if child.tag == "note" else None
            for container in child.iter():
                allowed = ALLOWLIST.get(container.tag)
                if allowed is None:
                    continue
                for mark in container:
                    if mark.tag not in allowed:
                        yield where, f"{container.tag}/{mark.tag}"
                        continue
                    rule = _VARIANT_RULES.get(mark.tag)
                    fault = None if rule is None else rule(mark, note)
                    if fault is not None:
                        yield where, fault


def _one_of(
    mark: ET.Element,
    name: str,
    allowed: frozenset[str],
    default: str = "",
) -> str | None:
    """Return a fault unless an attribute takes an allowed value."""
    value = _attr(mark, name, default)
    return None if value in allowed else f"{mark.tag}@{name}={value}"


def _typed(
    *allowed: str,
) -> Callable[[ET.Element, ET.Element | None], str | None]:
    """Return a rule that allows only the given ``type`` values."""
    values = frozenset(allowed)

    def rule(mark: ET.Element, _note: ET.Element | None) -> str | None:
        return _one_of(mark, "type", values)

    return rule


def _octave_shift(mark: ET.Element, _note: ET.Element | None) -> str | None:
    # Ottava lines 8va, 8vb, 15ma and 15mb, a start and an end (L692-699).
    return _one_of(mark, "type", frozenset({"up", "down", "stop"})) or _one_of(
        mark, "size", frozenset({"8", "15"}), "8"
    )


def _pedal(mark: ET.Element, _note: ET.Element | None) -> str | None:
    # A pedal line has a start, changes and a release (L700-702); a `Ped.`
    # glyph that begins a line is its start (marks and words). Without a line,
    # `<pedal-sign down>` and `<pedal-sign up>` are the glyphs, and the two at
    # one onset are a pedal change (L685-688).
    if _attr(mark, "line") != "yes" and _attr(mark, "sign") == "no":
        return "pedal@sign=no"
    return _one_of(mark, "type", frozenset({"start", "change", "stop"}))


def _wedge(mark: ET.Element, _note: ET.Element | None) -> str | None:
    # Hairpins start as cresc or dim and end (L689-691, L295-297); the format
    # has no niente hairpin.
    if _attr(mark, "niente") == "yes":
        return "wedge@niente=yes"
    return _one_of(mark, "type", frozenset({"crescendo", "diminuendo", "stop"}))


def _metronome(mark: ET.Element, _note: ET.Element | None) -> str | None:
    # Tempo is `<tempo "text" q=120>` (L680-683, L313-314), with no
    # parenthesised form.
    if _attr(mark, "parentheses") == "yes":
        return "metronome@parentheses=yes"
    return None


def _fermata(mark: ET.Element, _note: ET.Element | None) -> str | None:
    # `ferm` and `ferm_`, upright and inverted (L484). The format enumerates
    # no fermata shape, so a shape other than the default is outside it.
    shape = (mark.text or "").strip()
    fault = _one_of(mark, "type", frozenset({"", "upright", "inverted"}))
    if fault is None and shape not in {"", "normal"}:
        return f"fermata {shape}"
    return fault


_TREMOLO_STROKES = frozenset(range(1, 9))


def _tremolo(mark: ET.Element, _note: ET.Element | None) -> str | None:
    # `tremolo=single:S` (L493) and the two-note tremolo (L621), `S` 1 to 8.
    fault = _one_of(
        mark, "type", frozenset({"single", "start", "stop"}), "single"
    )
    if fault is None and _integer(mark.text) not in _TREMOLO_STROKES:
        return f"tremolo strokes {(mark.text or '').strip()}"
    return fault


def _wavy_line(mark: ET.Element, note: ET.Element | None) -> str | None:
    # The wavy line of an extended trill starts on a note with `orn=trill`
    # (L625-626).
    fault = _one_of(mark, "type", frozenset({"start", "stop"}))
    if (
        fault is None
        and _attr(mark, "type") == "start"
        and (
            note is None or note.find("notations/ornaments/trill-mark") is None
        )
    ):
        return "wavy-line start without trill-mark"
    return fault


def _mordent(mark: ET.Element, _note: ET.Element | None) -> str | None:
    # `orn=mordent` and `orn=inverted-mordent` have no long, approach or
    # departure variant (L491).
    if _attr(mark, "long") == "yes":
        return f"{mark.tag}@long=yes"
    for name in ("approach", "departure"):
        if mark.get(name) is not None:
            return f"{mark.tag}@{name}"
    return None


def _turn(mark: ET.Element, _note: ET.Element | None) -> str | None:
    # `orn=turn` and `orn=inverted-turn` have no slashed variant (L491-492).
    return f"{mark.tag}@slash=yes" if _attr(mark, "slash") == "yes" else None


def _arpeggiate(mark: ET.Element, _note: ET.Element | None) -> str | None:
    # `arp` and the arpeggio relation have no arrow (L486, L631).
    direction = mark.get("direction")
    return None if direction is None else f"arpeggiate@direction={direction}"


def _dynamics(mark: ET.Element, _note: ET.Element | None) -> str | None:
    # A dynamic direction is one of the enumerated marks (L671).
    return None if len(mark) == 1 else f"dynamics with {len(mark)} marks"


def _other_dynamics(mark: ET.Element, _note: ET.Element | None) -> str | None:
    # Only the exact printed text of an enumerated dynamic (L671-673).
    text = mark.text or ""
    return None if text in _DYNAMIC_MARKS else f"other-dynamics {text!r}"


_VARIANT_RULES: Mapping[
    str, Callable[[ET.Element, ET.Element | None], str | None]
] = MappingProxyType(
    {
        "tied": _typed("start", "stop"),  # Tie start and end (L579).
        "slur": _typed("start", "stop"),  # Slur start and end (L619).
        "wedge": _wedge,
        "dashes": _typed("start", "stop"),  # Dashes lines (L703).
        "metronome": _metronome,
        "octave-shift": _octave_shift,
        "pedal": _pedal,
        "fermata": _fermata,
        "tremolo": _tremolo,
        "wavy-line": _wavy_line,
        "mordent": _mordent,
        "inverted-mordent": _mordent,
        "turn": _turn,
        "inverted-turn": _turn,
        "arpeggiate": _arpeggiate,
        "dynamics": _dynamics,
        "other-dynamics": _other_dynamics,
    }
)
"""The attribute rules for the variants that the format distinguishes."""

_CHECKS: tuple[tuple[str, Callable[[ET.Element], str | None]], ...] = (
    ("parts_staves", _check_parts_staves),
    ("clef", _check_clef),
    ("clef_restated", _check_clef_restated),
    ("voices", _check_voices),
    ("lyric_harmony_cue", _check_lyric_harmony_cue),
    ("unsupported_element", _check_unsupported_element),
    ("ending_number", _check_ending_number),
    ("signature_position", _check_signature_position),
    ("time_signature", _check_time_signature),
    ("alter_accidental", _check_alter_accidental),
    ("barline", _check_barline),
    ("chord", _check_chord),
    ("tremolo", _check_tremolo),
    ("fingering", _check_fingering),
    ("bracket_dashes", _check_bracket_dashes),
    ("metronome", _check_metronome),
    ("beam_tuplet_span", _check_beam_tuplet_span),
    ("allowlist", _check_allowlist),
)
"""The structural checks in the order of :data:`REASONS`, without musicdiff."""
