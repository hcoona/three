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
from dataclasses import field as dataclass_field
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
    "tie_slur",
    "pedal",
    "beam_tuplet_span",
    "octave_shift_span",
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
            "multiple rest, measure repeat/style, segno, coda, out-of-range"
            " type, unpitched or hidden pitched note, non-normal notehead,"
            " fret or string"
        ),
        "ending_number": "an ending without a single number",
        "signature_position": "a key or time after a timed note or forward",
        "time_signature": "a time without one beats/beat-type, whole beats",
        "alter_accidental": (
            "a fractional alter, unsupported accidental or editorial display"
        ),
        "barline": "a barline other than ordinary, final, double or repeat",
        "chord": "a chord note on another staff, or a repeated chord pitch",
        "tremolo": (
            "an unmeasured/zero-stroke tremolo or unsupported ornament"
            " accidental-mark"
        ),
        "fingering": (
            "a fingering other than 1-5, two different digits or substitution"
        ),
        "bracket_dashes": (
            "an unsupported or unpaired bracket, or dashes without words"
        ),
        "metronome": "a metronome without one q, h or e unit and per-minute",
        "tie_slur": "an unsupported/unpaired tie or a slur on a rest",
        "pedal": "a line-less pedal continue/discontinue, resume or open line",
        "beam_tuplet_span": (
            "an invalid beam/tuplet span, missing time-modification, or"
            " out-of-bounds voice/offset timeline"
        ),
        "octave_shift_span": (
            "an unpaired, cross-staff or overlapping octave-shift span"
        ),
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
                "trill-mark",  # `orn=trill` (L498).
                "accidental-mark",  # `orn=trill(acc)` (L284, L498).
                "mordent",  # `orn=mordent` (L499).
                "inverted-mordent",  # `orn=inverted-mordent` (L499).
                "turn",  # `orn=turn` (L500).
                "inverted-turn",  # `orn=inverted-turn` (L500).
                # Extended trill relation (L642); marks and words (L973).
                "wavy-line",
                # `tremolo=single:S` (L501); two-note tremolo (L639).
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
                # Expression text `<"text">` (L705), tempo text (L707).
                "words",
                "dynamics",  # Dynamic and dyn-text directions (L690).
                "wedge",  # Hairpins (L716).
                "octave-shift",  # Ottava lines (L719).
                "pedal",  # Pedal signs (L712) and pedal lines (L723).
                "dashes",  # Dashes lines (L727).
                "bracket",  # Unhooked solid voice-leading lines (L730).
                "metronome",  # Tempo `q=120` / `(q=120)` (L707).
                "rehearsal",  # Rehearsal marks (L711).
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
        lead_staff, lead_voice = "1", "1"
        for note in measure.findall("note"):
            if note.find("chord") is None:
                lead_staff, lead_voice = _staff(note), _voice(note)
                staff, voice = lead_staff, lead_voice
            else:
                staff = _text(note, "staff") or lead_staff
                voice = _text(note, "voice") or lead_voice
            voices.setdefault(staff, set()).add(voice)
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
        "measure-style",
        "segno",
        "coda",
        "unpitched",
        "fret",
        "string",
    }
)
_OUT_OF_RANGE_TYPES = frozenset(
    {"breve", "long", "maxima", "256th", "512th", "1024th"}
)


def _check_unsupported_element(root: ET.Element) -> str | None:
    """Check for unsupported elements, note types, hidden notes, noteheads."""
    for where, element in _located(root):
        if element.tag in _UNSUPPORTED_TAGS:
            return f"{where}: {element.tag}"
        text = (element.text or "").strip()
        if element.tag == "type" and text in _OUT_OF_RANGE_TYPES:
            return f"{where}: type {text}"
        if (
            element.tag == "note"
            and element.find("pitch") is not None
            and _attr(element, "print-object") == "no"
        ):
            return f"{where}: hidden pitched note"
        if element.tag == "notehead" and text != "normal":
            return f"{where}: notehead {text}"
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


_FORMAT_ACCIDENTALS = frozenset(
    {"sharp", "natural", "flat", "double-sharp", "flat-flat"}
)


def _accidental_element_fault(where: str, element: ET.Element) -> str | None:
    text = (element.text or "").strip()
    if text not in _PLAIN_ACCIDENTALS:
        return f"{where}: accidental {text}"
    if (
        _attr(element, "bracket") == "yes"
        or _attr(element, "editorial") == "yes"
    ):
        return f"{where}: bracketed or editorial accidental"
    paren = _attr(element, "parentheses")
    if paren not in {"", "yes", "no"}:
        return f"{where}: accidental parentheses {paren}"
    return None


def _check_alter_accidental(root: ET.Element) -> str | None:
    """Check whole-number alters and supported accidentals."""
    for where, element in _located(root):
        text = (element.text or "").strip()
        if element.tag == "alter":
            value = _decimal(text)
            if value is None or value != value.to_integral_value():
                return f"{where}: alter {text}"
        elif element.tag == "accidental":
            fault = _accidental_element_fault(where, element)
            if fault is not None:
                return fault
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
            elif (_text(note, "staff") or _staff(head)) != _staff(head):
                return _at(measure, f"chord note on staff {_staff(note)}")
            elif pitch in pitches:
                return _at(measure, f"chord repeats {pitch[0]}{pitch[2]}")
            else:
                pitches.add(pitch)
    return None


def _check_tremolo(root: ET.Element) -> str | None:
    """Check tremolos and ornament accidental-mark bounds."""
    for measure in _measures(root):
        for tremolo in measure.iter("tremolo"):
            strokes = _integer(tremolo.text)
            if _attr(tremolo, "type") == "unmeasured" or not strokes:
                kind = _attr(tremolo, "type")
                return _at(measure, f"tremolo type {kind} strokes {strokes}")
        for ornaments in measure.iter("ornaments"):
            marks = ornaments.findall("accidental-mark")
            if not marks:
                continue
            if (
                ornaments.find("trill-mark") is None
                or len(marks) > 1
                or (marks[0].text or "").strip() not in _PLAIN_ACCIDENTALS
            ):
                return _at(measure, "unsupported ornament accidental-mark")
    return None


_FINGER = re.compile(r"[1-5]")
_DASH_SUB_FINGER = re.compile(r"([1-5])[-\u2013]([1-5])")
_SMUFL_SUB_FINGER = re.compile(
    r"fingering([1-5])fingeringSubstitution(?:Above|Below)fingering([1-5])"
)
_SMUFL_SUB_GLYPHS = frozenset(
    {"fingeringSubstitutionAbove", "fingeringSubstitutionBelow"}
)


def _check_fingering(root: ET.Element) -> str | None:
    """Check one digit 1-5, two different digits, or two-finger substitution."""
    for measure in _measures(root):
        for note in measure.findall("note"):
            fault = _fingering_fault(note)
            if fault is not None:
                return _at(measure, fault)
    return None


def _single_fingering_valid(text: str) -> bool:
    if _FINGER.fullmatch(text):
        return True
    if (
        len(text) == 2  # noqa: PLR2004
        and _FINGER.fullmatch(text[0])
        and _FINGER.fullmatch(text[1])
        and text[0] != text[1]
    ):
        return True
    matched = _DASH_SUB_FINGER.fullmatch(text) or _SMUFL_SUB_FINGER.fullmatch(
        text
    )
    return bool(matched and matched.group(1) != matched.group(2))


def _fingering_fault(note: ET.Element) -> str | None:
    fingerings = note.findall("notations/technical/fingering")
    if not fingerings:
        return None
    if any(_attr(f, "alternate") == "yes" for f in fingerings):
        return "alternate fingering"
    texts = ["".join((f.text or "").split()) for f in fingerings]
    subs = [_attr(f, "substitution") == "yes" for f in fingerings]
    valid = False
    if len(fingerings) == 1 and not subs[0]:
        if _single_fingering_valid(texts[0]):
            return None
        return f"fingering {fingerings[0].text!r}"
    if len(fingerings) == 2 and not subs[0]:  # noqa: PLR2004
        valid = (
            bool(_FINGER.fullmatch(texts[0]))
            and bool(_FINGER.fullmatch(texts[1]))
            and texts[0] != texts[1]
        )
    elif len(fingerings) == 3 and not any(subs):  # noqa: PLR2004
        valid = (
            bool(_FINGER.fullmatch(texts[0]))
            and bool(_FINGER.fullmatch(texts[1]))
            and texts[0] != texts[1]
            and texts[2] in _SMUFL_SUB_GLYPHS
        )
    return None if valid else f"fingering {texts!r}"


def _pair_bracket_endpoints(
    bracket_ends: list[tuple[Fraction, int, int, str, str, ET.Element]],
) -> str | None:
    open_brackets: dict[str, tuple[Fraction, ET.Element]] = {}
    for score_on, _, _, number, kind, m_el in sorted(
        bracket_ends,
        key=lambda item: (item[0], 0 if item[4] == "stop" else 1, item[2]),
    ):
        if kind == "start":
            if number in open_brackets:
                return _at(m_el, f"overlapping bracket number {number}")
            open_brackets[number] = (score_on, m_el)
        else:
            started = open_brackets.pop(number, None)
            if started is None or score_on < started[0]:
                return _at(
                    m_el, f"bracket stop without start (number {number})"
                )
    if open_brackets:
        num, (_, start_m) = next(iter(open_brackets.items()))
        return _at(start_m, f"unclosed bracket number {num}")
    return None


@dataclass(slots=True)
class _BracketScan:
    divisions: Fraction | None = None
    measure_start: Fraction | None = Fraction(0)
    order: int = 0
    worded: set[tuple[str, Fraction]] = dataclass_field(default_factory=set)
    ends: list[tuple[Fraction, int, int, str, str, ET.Element]] = (
        dataclass_field(default_factory=list)
    )


def _record_bracket_direction(
    measure: ET.Element,
    child: ET.Element,
    m_index: int,
    onset: Fraction | None,
    scan: _BracketScan,
) -> str | None:
    preceded = onset is not None and (_staff(child), onset) in scan.worded
    fault, has_words, brackets = _dashes_and_brackets(child, preceded=preceded)
    if fault is not None:
        return _at(measure, fault)
    if has_words and onset is not None:
        scan.worded.add((_staff(child), onset))
    for number, kind in brackets:
        if onset is None or scan.measure_start is None:
            return _at(measure, "bracket at undecidable onset")
        scan.order += 1
        scan.ends.append(
            (
                scan.measure_start + onset,
                m_index,
                scan.order,
                number,
                kind,
                measure,
            )
        )
    return None


def _scan_bracket_measure(
    measure: ET.Element, m_index: int, scan: _BracketScan
) -> str | None:
    scan.worded.clear()
    position: Fraction | None = Fraction(0)
    m_len: Fraction | None = Fraction(0)
    for child in measure:
        if child.tag == "attributes" and child.find("divisions") is not None:
            scan.divisions = _fraction(child.findtext("divisions"))
            if scan.divisions is not None and scan.divisions <= 0:
                scan.divisions = None
        elif child.tag == "direction":
            onset = _shift(position, child, scan.divisions, "offset")
            fault = _record_bracket_direction(
                measure, child, m_index, onset, scan
            )
            if fault is not None:
                return fault
        elif (
            child.tag in {"note", "forward"}
            and child.find("chord") is None
            and child.find("grace") is None
        ):
            position = _shift(position, child, scan.divisions, "duration")
            m_len = (
                None
                if m_len is None or position is None
                else max(m_len, position)
            )
        elif child.tag == "backup":
            position = _shift(position, child, scan.divisions, "duration", -1)
    scan.measure_start = (
        None
        if scan.measure_start is None or m_len is None
        else scan.measure_start + m_len
    )
    return None


def _check_bracket_dashes(root: ET.Element) -> str | None:
    """Check unhooked solid brackets and that dashes starts follow words."""
    scan = _BracketScan()
    for m_index, measure in enumerate(_measures(root)):
        fault = _scan_bracket_measure(measure, m_index, scan)
        if fault is not None:
            return fault
    return _pair_bracket_endpoints(scan.ends)


def _dashes_and_brackets(
    direction: ET.Element, *, preceded: bool
) -> tuple[str | None, bool, list[tuple[str, str]]]:
    has_words = False
    brackets: list[tuple[str, str]] = []
    for direction_type in direction.findall("direction-type"):
        for mark in direction_type:
            if mark.tag == "bracket":
                kind = _attr(mark, "type")
                if (
                    kind not in {"start", "stop"}
                    or _attr(mark, "line-end") != "none"
                    or _attr(mark, "line-type") not in {"", "solid"}
                ):
                    return "unsupported bracket", has_words, brackets
                brackets.append((_attr(mark, "number", "1"), kind))
            if (
                mark.tag == "dashes"
                and _attr(mark, "type") == "start"
                and not (has_words or preceded)
            ):
                return "dashes start without words", has_words, brackets
            if mark.tag == "words" and (mark.text or "").strip():
                has_words = True
    return None, has_words, brackets


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


def _volta_measure_flags(measure: ET.Element) -> tuple[bool, bool, bool]:
    left_start = right_close = repeat = False
    for bar in measure.findall("barline"):
        loc = _attr(bar, "location", "right")
        if loc == "left":
            left_start = left_start or any(
                _attr(e, "type") == "start" for e in bar.findall("ending")
            )
        elif loc == "right":
            right_close = right_close or any(
                _attr(e, "type") in {"stop", "discontinue"}
                for e in bar.findall("ending")
            )
            repeat = repeat or any(
                _attr(r, "direction") == "backward"
                for r in bar.findall("repeat")
            )
    return left_start, right_close, repeat


def _volta_tie_in_map(measures: list[ET.Element]) -> dict[int, int]:
    """Return 0-based ``tie-in`` measure ordinals mapped to source measures."""
    flags = [_volta_measure_flags(m) for m in measures]
    in_bracket: list[bool] = []
    active = False
    for left_start, right_close, _ in flags:
        active = active or left_start
        in_bracket.append(active)
        if right_close:
            active = False
    sources: dict[int, int] = {}
    idx = 0
    total = len(measures)
    while idx < total:
        if not in_bracket[idx]:
            idx += 1
            continue
        g_start = idx
        while idx + 1 < total and in_bracket[idx + 1]:
            idx += 1
        src = g_start - 1
        if src >= 0:
            for m_idx in range(g_start + 1, idx + 1):
                if flags[m_idx][0]:
                    sources[m_idx] = src
            if flags[idx][2] and idx + 1 < total and not in_bracket[idx + 1]:
                sources[idx + 1] = src
        idx += 1
    return sources


@dataclass(slots=True)
class _TieNote:
    m_idx: int
    measure: ET.Element
    voice: str
    own: str
    shown: str
    onset: Fraction
    start_q: Fraction
    end_q: Fraction
    pitch: tuple[str, Decimal, int]
    starts: bool
    stops: bool
    order: int = 0
    is_grace: bool = False


def _tie_note_fault(child: ET.Element) -> str | None:
    if child.find("rest") is not None and (
        child.find("notations/slur") is not None
        or child.find("tie") is not None
        or child.find("notations/tied") is not None
    ):
        return "slur or tie on a rest"
    t_marks = [*child.findall("tie"), *child.findall("notations/tied")]
    for mark in t_marks:
        if _attr(mark, "type") not in {"start", "stop"}:
            return f"tie type {_attr(mark, 'type')}"
    if child.find("grace") is not None and any(
        _attr(m, "type") == "stop" for m in t_marks
    ):
        return "tie stop on a grace note"
    return None


@dataclass(slots=True)
class _TieMeasureCursor:
    measure: ET.Element
    m_idx: int
    divisions: Fraction | None
    score_time: Fraction
    pos: Fraction | None = Fraction(0)
    last_on: Fraction | None = Fraction(0)
    m_len: Fraction = Fraction(0)
    order: int = 0
    lead_shown: str = "1"
    lead_voice: str = "1"
    voice_spans: dict[str, list[tuple[str, Fraction, Fraction]]] = (
        dataclass_field(default_factory=dict)
    )
    notes: list[_TieNote] = dataclass_field(default_factory=list)


def _record_grace_tie_note(
    child: ET.Element, cur: _TieMeasureCursor, *, chord: bool
) -> str | None:
    if not chord:
        cur.last_on = cur.pos
        cur.order += 1
    t_marks = [*child.findall("tie"), *child.findall("notations/tied")]
    if not any(_attr(m, "type") == "start" for m in t_marks):
        return None
    pitch = _pitch(child)
    onset = cur.last_on if chord else cur.pos
    if pitch is None or onset is None or cur.divisions is None:
        return _at(cur.measure, "tie at undecidable time")
    shown = (
        (_text(child, "staff") or cur.lead_shown) if chord else cur.lead_shown
    )
    voice = (
        (_text(child, "voice") or cur.lead_voice) if chord else cur.lead_voice
    )
    cur.notes.append(
        _TieNote(
            m_idx=cur.m_idx,
            measure=cur.measure,
            voice=voice,
            own=shown,
            shown=shown,
            onset=onset,
            start_q=cur.score_time + onset,
            end_q=cur.score_time + onset,
            pitch=pitch,
            starts=True,
            stops=False,
            order=cur.order,
            is_grace=True,
        )
    )
    return None


def _handle_tie_note_el(
    child: ET.Element, cur: _TieMeasureCursor
) -> str | None:
    fault = _tie_note_fault(child)
    if fault is not None:
        return _at(cur.measure, fault)
    chord = child.find("chord") is not None
    if not chord:
        cur.lead_shown = _staff(child)
        cur.lead_voice = _voice(child)
    if child.find("grace") is not None:
        return _record_grace_tie_note(child, cur, chord=chord)
    onset = cur.last_on if chord else cur.pos
    if not chord:
        cur.order += 1
        cur.last_on = cur.pos
        cur.pos = _shift(cur.pos, child, cur.divisions, "duration")
        if cur.pos is not None:
            cur.m_len = max(cur.m_len, cur.pos)
            if onset is not None:
                cur.voice_spans.setdefault(cur.lead_voice, []).append(
                    (cur.lead_shown, onset, cur.pos)
                )
    pitch = _pitch(child)
    if pitch is None:
        return None
    dur = _fraction(child.findtext("duration"))
    if onset is None or cur.pos is None or cur.divisions is None or dur is None:
        return _at(cur.measure, "tie at undecidable time")
    shown = (
        (_text(child, "staff") or cur.lead_shown) if chord else cur.lead_shown
    )
    voice = (
        (_text(child, "voice") or cur.lead_voice) if chord else cur.lead_voice
    )
    t_marks = [*child.findall("tie"), *child.findall("notations/tied")]
    cur.notes.append(
        _TieNote(
            m_idx=cur.m_idx,
            measure=cur.measure,
            voice=voice,
            own=shown,
            shown=shown,
            onset=onset,
            start_q=cur.score_time + onset,
            end_q=cur.score_time + onset + dur / cur.divisions,
            pitch=pitch,
            starts=any(_attr(m, "type") == "start" for m in t_marks),
            stops=any(_attr(m, "type") == "stop" for m in t_marks),
            order=cur.order,
        )
    )
    return None


def _assign_tie_measure_voices(cur: _TieMeasureCursor) -> None:
    split_voices: set[str] = set()
    homes: dict[str, str] = {}
    for voice, spans in cur.voice_spans.items():
        if any(
            s1 != s2 and a0 < b1 and b0 < a1
            for idx, (s1, a0, a1) in enumerate(spans)
            for s2, b0, b1 in spans[idx + 1 :]
        ):
            split_voices.add(voice)
        else:
            c1 = sum(1 for st, *_ in spans if st == "1")
            c2 = sum(1 for st, *_ in spans if st == "2")
            homes[voice] = "2" if c2 > c1 else "1"
    for note in cur.notes:
        if note.voice in split_voices:
            note.own = note.shown
            note.voice = f"{note.voice}#{note.shown}"
        else:
            note.own = homes.get(note.voice, note.shown)


def _scan_tie_measure(
    measure: ET.Element,
    m_idx: int,
    divisions: Fraction | None,
    score_time: Fraction,
) -> tuple[str | None, Fraction | None, Fraction, list[_TieNote]]:
    cur = _TieMeasureCursor(measure, m_idx, divisions, score_time)
    for child in measure:
        if child.tag == "attributes" and child.find("divisions") is not None:
            cur.divisions = _fraction(child.findtext("divisions"))
            if cur.divisions is not None and cur.divisions <= 0:
                cur.divisions = None
        elif child.tag in {"forward", "backup"}:
            sign = -1 if child.tag == "backup" else 1
            cur.pos = _shift(cur.pos, child, cur.divisions, "duration", sign)
            if cur.pos is not None and sign > 0:
                cur.m_len = max(cur.m_len, cur.pos)
        elif child.tag == "note":
            err = _handle_tie_note_el(child, cur)
            if err is not None:
                return err, cur.divisions, cur.m_len, cur.notes
    _assign_tie_measure_voices(cur)
    return None, cur.divisions, cur.m_len, cur.notes


def _tie_stop_matches(row: _TieNote, stop: _TieNote) -> bool:
    if stop.is_grace or stop.start_q != row.end_q or stop.pitch != row.pitch:
        return False
    if row.is_grace and stop.m_idx == row.m_idx:
        return stop.voice == row.voice and row.order < stop.order
    return stop.own == row.own or stop.shown == row.shown


def _is_valid_tie_in_stop(
    stop_row: _TieNote,
    pitched_notes: list[_TieNote],
    first_voice_onset: dict[tuple[int, str], Fraction],
    tie_in_sources: dict[int, int],
    measure_bounds: list[tuple[Fraction, Fraction]],
) -> bool:
    src_m = tie_in_sources.get(stop_row.m_idx)
    if (
        src_m is None
        or stop_row.onset != 0
        or first_voice_onset.get((stop_row.m_idx, stop_row.voice)) != 0
    ):
        return False
    src_end = measure_bounds[src_m][1]
    return any(
        not r.is_grace
        and r.m_idx == src_m
        and r.end_q == src_end
        and r.pitch == stop_row.pitch
        and (r.own == stop_row.own or r.shown == stop_row.shown)
        for r in pitched_notes
    )


def _augment_bipartite(
    node: int,
    adj: dict[int, list[int]],
    partner: dict[int, int],
    seen: set[int],
) -> bool:
    for nxt in adj.get(node, ()):
        if nxt in seen:
            continue
        seen.add(nxt)
        prev = partner.get(nxt)
        if prev is None or _augment_bipartite(prev, adj, partner, seen):
            partner[nxt] = node
            return True
    return False


def _build_tie_edges(
    pitched_notes: list[_TieNote],
) -> tuple[list[int], list[int], dict[int, list[int]], dict[int, list[int]]]:
    starts = [idx for idx, row in enumerate(pitched_notes) if row.starts]
    stops = [
        idx
        for idx, row in enumerate(pitched_notes)
        if row.stops and not row.is_grace
    ]
    start_adj: dict[int, list[int]] = {}
    stop_adj: dict[int, list[int]] = {}
    for u_idx in starts:
        u_row = pitched_notes[u_idx]
        cands = [
            s_idx
            for s_idx in stops
            if _tie_stop_matches(u_row, pitched_notes[s_idx])
        ]
        cands.sort(key=lambda s_idx: pitched_notes[s_idx].voice != u_row.voice)
        start_adj[u_idx] = cands
        for s_idx in cands:
            stop_adj.setdefault(s_idx, []).append(u_idx)
    return starts, stops, start_adj, stop_adj


def _verify_tie_pairing(
    pitched_notes: list[_TieNote],
    tie_in_sources: dict[int, int],
    measure_bounds: list[tuple[Fraction, Fraction]],
) -> str | None:
    first_voice_onset: dict[tuple[int, str], Fraction] = {}
    for row in pitched_notes:
        if row.is_grace:
            continue
        key = (row.m_idx, row.voice)
        if key not in first_voice_onset or row.onset < first_voice_onset[key]:
            first_voice_onset[key] = row.onset
    starts, stops, start_adj, stop_adj = _build_tie_edges(pitched_notes)
    start_partner_of_start: dict[int, int] = {}
    for s_idx in stops:
        if _is_valid_tie_in_stop(
            pitched_notes[s_idx],
            pitched_notes,
            first_voice_onset,
            tie_in_sources,
            measure_bounds,
        ):
            continue
        if not _augment_bipartite(
            s_idx, stop_adj, start_partner_of_start, set()
        ):
            return _at(
                pitched_notes[s_idx].measure, "tie stop without matching start"
            )
    matched_start_of_stop = {
        s_idx: u_idx for u_idx, s_idx in start_partner_of_start.items()
    }
    for u_idx in starts:
        if u_idx not in start_partner_of_start and not _augment_bipartite(
            u_idx, start_adj, matched_start_of_stop, set()
        ):
            return _at(
                pitched_notes[u_idx].measure,
                "tie start reaches no matching stop",
            )
    return None


def _check_tie_slur(root: ET.Element) -> str | None:
    """Check tie/tied types and pairing, and reject slurs on rests."""
    measures = _measures(root)
    tie_in_sources = _volta_tie_in_map(measures)
    divisions: Fraction | None = None
    score_time = Fraction(0)
    measure_bounds: list[tuple[Fraction, Fraction]] = []
    pitched_notes: list[_TieNote] = []
    for m_idx, measure in enumerate(measures):
        fault, divisions, m_len, m_notes = _scan_tie_measure(
            measure, m_idx, divisions, score_time
        )
        if fault is not None:
            return fault
        pitched_notes.extend(m_notes)
        measure_bounds.append((score_time, score_time + m_len))
        score_time += m_len
    return _verify_tie_pairing(pitched_notes, tie_in_sources, measure_bounds)


type _PedalEndpoint = tuple[Fraction, int, int, str, str, ET.Element]


@dataclass(slots=True)
class _PedalScan:
    divisions: Fraction | None = None
    measure_start: Fraction | None = Fraction(0)
    order: int = 0
    ends: list[_PedalEndpoint] = dataclass_field(default_factory=list)


def _record_pedal_dir(
    measure: ET.Element,
    direction: ET.Element,
    onset: Fraction | None,
    scan: _PedalScan,
) -> str | None:
    for pedal in direction.findall("direction-type/pedal"):
        kind = _attr(pedal, "type")
        line = _attr(pedal, "line") == "yes"
        if kind == "resume":
            return _at(measure, "pedal resume")
        if kind in {"continue", "discontinue"} and not line:
            return _at(measure, f"pedal {kind} without line=yes")
        if not line:
            continue
        if onset is None or scan.measure_start is None:
            return _at(measure, "pedal at undecidable onset")
        pri = (
            0
            if kind in {"stop", "discontinue"}
            else (1 if kind == "change" else 2)
        )
        scan.order += 1
        scan.ends.append(
            (
                scan.measure_start + onset,
                pri,
                scan.order,
                _attr(pedal, "number", "1"),
                kind,
                measure,
            )
        )
    return None


def _scan_pedal_measure(measure: ET.Element, scan: _PedalScan) -> str | None:
    position: Fraction | None = Fraction(0)
    m_len: Fraction | None = Fraction(0)
    for child in measure:
        if child.tag == "attributes" and child.find("divisions") is not None:
            scan.divisions = _fraction(child.findtext("divisions"))
            if scan.divisions is not None and scan.divisions <= 0:
                scan.divisions = None
        elif child.tag == "direction":
            onset = _shift(position, child, scan.divisions, "offset")
            fault = _record_pedal_dir(measure, child, onset, scan)
            if fault is not None:
                return fault
        elif (
            child.tag in {"note", "forward"}
            and child.find("chord") is None
            and child.find("grace") is None
        ):
            position = _shift(position, child, scan.divisions, "duration")
            m_len = (
                None
                if m_len is None or position is None
                else max(m_len, position)
            )
        elif child.tag == "backup":
            position = _shift(position, child, scan.divisions, "duration", -1)
    scan.measure_start = (
        None
        if scan.measure_start is None or m_len is None
        else scan.measure_start + m_len
    )
    return None


def _pair_pedal_endpoints(ends: list[_PedalEndpoint]) -> str | None:
    opened: dict[str, tuple[Fraction, ET.Element]] = {}
    for score_on, _, _, num, kind, m_el in sorted(
        ends, key=lambda it: (it[0], it[1], it[2])
    ):
        if kind == "start":
            if num in opened:
                return _at(m_el, f"overlapping pedal line {num}")
            opened[num] = (score_on, m_el)
        elif kind in {"stop", "discontinue"}:
            started = opened.pop(num, None)
            if started is None or score_on < started[0]:
                return _at(m_el, f"pedal {kind} without start")
        elif kind == "change":
            started = opened.get(num)
            if started is None or score_on < started[0]:
                return _at(m_el, "pedal change without start")
    if opened:
        num, (_, start_m) = next(iter(opened.items()))
        return _at(start_m, f"unclosed pedal line {num}")
    return None


def _check_pedal(root: ET.Element) -> str | None:
    """Check pedal line attributes, absence of resume, and closed lines."""
    scan = _PedalScan()
    for measure in _measures(root):
        fault = _scan_pedal_measure(measure, scan)
        if fault is not None:
            return fault
    return _pair_pedal_endpoints(scan.ends)


_FORMAT_TYPE_QUARTERS: Mapping[str, Fraction] = MappingProxyType(
    {
        "whole": Fraction(4),
        "half": Fraction(2),
        "quarter": Fraction(1),
        "eighth": Fraction(1, 2),
        "16th": Fraction(1, 4),
        "32nd": Fraction(1, 8),
        "64th": Fraction(1, 16),
        "128th": Fraction(1, 32),
    }
)


def _spacer_writable(quarters: Fraction) -> bool:
    return quarters >= 0 and (quarters * 32).denominator == 1


def _note_tuplet_scale(child: ET.Element) -> Fraction | None:
    tm = child.find("time-modification")
    if tm is None:
        return Fraction(1)
    actual = _integer(tm.findtext("actual-notes"))
    normal = _integer(tm.findtext("normal-notes"))
    if not actual or not normal or actual <= 0 or normal <= 0:
        return None
    return Fraction(normal, actual)


def _written_duration_ok(
    child: ET.Element, step: Fraction
) -> tuple[bool, Fraction]:
    scale = _note_tuplet_scale(child)
    if scale is None:
        return False, Fraction(1)
    rest_el = child.find("rest")
    if rest_el is not None:
        if (
            _attr(rest_el, "measure") == "yes"
            and _attr(child, "print-object") != "no"
        ):
            return step > 0, scale
        if _attr(child, "print-object") == "no" and child.find("type") is None:
            return _spacer_writable(step / scale), scale
    base = _FORMAT_TYPE_QUARTERS.get(_text(child, "type") or "")
    if base is None:
        return False, scale
    dots = len(child.findall("dot"))
    written = base * Fraction((1 << (dots + 1)) - 1, 1 << dots) * scale
    if any(
        _attr(t, "type") in {"start", "stop"}
        for t in child.findall("notations/ornaments/tremolo")
    ):
        if child.find("time-modification") is None:
            written /= 2
        else:
            scale *= 2
    return written == step, scale


_BEAM_STATES = frozenset(
    {"begin", "continue", "end", "forward hook", "backward hook"}
)
type _VoiceMeasureRecord = tuple[
    int,
    ET.Element,
    bool,
    bool,
    Fraction,
    list[tuple[Fraction, Fraction, str, dict[int, str]]],
]


@dataclass(slots=True)
class _SpanLeadEvent:
    staff: str
    is_grace: bool
    onset: Fraction
    end_on: Fraction
    stem: str
    beams: dict[int, str]
    note_type: str
    dots: int
    scale: Fraction = Fraction(1)
    tuplets: list[tuple[str, str]] = dataclass_field(default_factory=list)


def _extract_note_beams(child: ET.Element) -> tuple[dict[int, str] | None, str]:
    beams_map: dict[int, str] = {}
    for beam in child.findall("beam"):
        lvl = _integer(beam.get("number", "1"))
        st = (beam.text or "").strip()
        if lvl is None or not 1 <= lvl <= 5 or st not in _BEAM_STATES:  # noqa: PLR2004
            return None, f"invalid beam {st!r}"
        beams_map[lvl] = st
    return beams_map, ""


def _add_note_tuplets(child: ET.Element, target: list[tuple[str, str]]) -> None:
    for tuplet in child.findall("notations/tuplet"):
        pair = (_attr(tuplet, "number", "1"), _attr(tuplet, "type"))
        if pair not in target:
            target.append(pair)


@dataclass(slots=True)
class _SpanMeasureState:
    measure: ET.Element
    divisions: Fraction | None
    pos: Fraction | None = Fraction(0)
    m_len: Fraction = Fraction(0)
    cur_lead: _SpanLeadEvent | None = None
    dirs_in_m: list[Fraction | None] = dataclass_field(default_factory=list)
    voice_events: dict[str, list[_SpanLeadEvent]] = dataclass_field(
        default_factory=dict
    )


def _record_main_span_note(
    child: ET.Element,
    voice: str,
    staff: str,
    beams_map: dict[int, str],
    st: _SpanMeasureState,
) -> str | None:
    onset = st.pos
    step = _shift(Fraction(0), child, st.divisions, "duration")
    st.pos = _shift(st.pos, child, st.divisions, "duration")
    if onset is None or step is None or st.pos is None:
        return "note at undecidable time"
    st.m_len = max(st.m_len, st.pos)
    ok, scale = _written_duration_ok(child, step)
    if not ok:
        return "unwritable note or rest duration"
    stem_txt = (_text(child, "stem") or "").strip()
    stem = stem_txt if stem_txt in {"up", "down"} else ""
    ev = _SpanLeadEvent(
        staff=staff,
        is_grace=False,
        onset=onset,
        end_on=onset + step,
        stem=stem,
        beams=beams_map,
        note_type=_text(child, "type") or "",
        dots=len(child.findall("dot")),
        scale=scale,
    )
    _add_note_tuplets(child, ev.tuplets)
    st.cur_lead = ev
    st.voice_events.setdefault(voice, []).append(ev)
    return None


def _record_grace_span_note(
    child: ET.Element,
    voice: str,
    staff: str,
    beams_map: dict[int, str],
    st: _SpanMeasureState,
) -> str | None:
    ntype = _text(child, "type") or ""
    if (
        st.pos is None
        or child.find("rest") is not None
        or ntype not in _FORMAT_TYPE_QUARTERS
        or child.find("time-modification") is not None
    ):
        return "invalid grace note type or duration"
    ev = _SpanLeadEvent(
        staff=staff,
        is_grace=True,
        onset=st.pos,
        end_on=st.pos,
        stem="",
        beams=beams_map,
        note_type=ntype,
        dots=len(child.findall("dot")),
    )
    _add_note_tuplets(child, ev.tuplets)
    st.cur_lead = ev
    st.voice_events.setdefault(voice, []).append(ev)
    return None


def _check_chord_span_member(
    child: ET.Element, st: _SpanMeasureState
) -> str | None:
    lead = st.cur_lead
    if lead is None or (child.find("grace") is not None) != lead.is_grace:
        return "chord member without matching lead"
    if (_text(child, "type") or "") != lead.note_type or len(
        child.findall("dot")
    ) != lead.dots:
        return "chord member type or dots differ from lead"
    if not lead.is_grace:
        step = _shift(Fraction(0), child, st.divisions, "duration")
        if step != lead.end_on - lead.onset:
            return "chord member duration differs from lead"
    _add_note_tuplets(child, lead.tuplets)
    return None


def _handle_span_note(child: ET.Element, st: _SpanMeasureState) -> str | None:
    if (
        child.findall("notations/tuplet")
        and child.find("time-modification") is None
    ):
        return "tuplet without time-modification"
    if child.find("chord") is not None:
        return _check_chord_span_member(child, st)
    voice, staff = _voice(child), _staff(child)
    beams_map, err = _extract_note_beams(child)
    if beams_map is None:
        return err
    if child.find("grace") is not None:
        return _record_grace_span_note(child, voice, staff, beams_map, st)
    return _record_main_span_note(child, voice, staff, beams_map, st)


def _step_span_cursor(child: ET.Element, st: _SpanMeasureState) -> str | None:
    st.cur_lead = None
    if child.tag == "backup":
        st.pos = _shift(st.pos, child, st.divisions, "duration", -1)
        if st.pos is None or st.pos < 0:
            return "backup moves before measure start"
    elif child.tag == "forward":
        step = _shift(Fraction(0), child, st.divisions, "duration")
        st.pos = _shift(st.pos, child, st.divisions, "duration")
        if step is None or st.pos is None or step < 0:
            return "invalid forward duration"
        st.m_len = max(st.m_len, st.pos)
    return None


def _scan_span_measure(
    measure: ET.Element, divisions: Fraction | None
) -> tuple[str | None, _SpanMeasureState]:
    st = _SpanMeasureState(measure=measure, divisions=divisions)
    for child in measure:
        if child.tag == "attributes" and child.find("divisions") is not None:
            st.divisions = _fraction(child.findtext("divisions"))
            if st.divisions is not None and st.divisions <= 0:
                st.divisions = None
        elif child.tag == "direction":
            st.dirs_in_m.append(_shift(st.pos, child, st.divisions, "offset"))
        elif child.tag in {"backup", "forward"}:
            fault = _step_span_cursor(child, st)
            if fault is not None:
                return _at(measure, fault), st
        elif child.tag == "note":
            fault = _handle_span_note(child, st)
            if fault is not None:
                return _at(measure, fault), st
    return None, st


def _split_screen_voice(
    events: list[_SpanLeadEvent],
) -> list[tuple[str | None, str, list[_SpanLeadEvent]]]:
    mains = [ev for ev in events if not ev.is_grace]
    overlaps = any(
        a.staff != b.staff and a.onset < b.end_on and b.onset < a.end_on
        for idx, a in enumerate(mains)
        for b in mains[idx + 1 :]
    )
    if overlaps:
        by_st: dict[str, list[_SpanLeadEvent]] = {}
        for ev in events:
            by_st.setdefault(ev.staff, []).append(ev)
        return [(st, st, by_st[st]) for st in sorted(by_st)]
    c1 = sum(1 for ev in mains if ev.staff == "1")
    c2 = sum(1 for ev in mains if ev.staff == "2")
    home = "2" if c2 > c1 else "1"
    return [(None, home, events)]


def _check_slice_graces_and_tuplets(
    voice: str, events: list[_SpanLeadEvent]
) -> tuple[str | None, bool, list[_SpanLeadEvent]]:
    grace_run: list[dict[int, str]] = []
    mains: list[_SpanLeadEvent] = []
    leading_grace = False
    tuplet_spans: dict[tuple[str, ...], int] = {}
    for ev in events:
        for num, kind in ev.tuplets:
            _count(tuplet_spans, (voice, num), kind, "start", "stop")
        if ev.is_grace:
            grace_run.append(dict(ev.beams))
            if not mains and ev.onset == 0:
                leading_grace = True
        else:
            if grace_run and _beam_seq_fault(grace_run) is not None:
                return "invalid grace beam span", False, []
            grace_run = []
            mains.append(ev)
    if grace_run and _beam_seq_fault(grace_run) is not None:
        return "invalid grace beam span", False, []
    unclosed = [k for k, c in tuplet_spans.items() if c > 0]
    if unclosed:
        return (
            f"tuplet {unclosed[0][1]} of voice {voice} does not stop",
            False,
            [],
        )
    return None, leading_grace, mains


def _verify_voice_timeline(
    voice: str,
    mains: list[_SpanLeadEvent],
    m_len: Fraction,
    boundaries: set[Fraction],
) -> tuple[str | None, list[tuple[Fraction, Fraction, str, dict[int, str]]]]:
    ordered = sorted(mains, key=lambda ev: ev.onset)
    cur_v = Fraction(0)
    scales: list[Fraction] = []
    for ev in ordered:
        if ev.onset < cur_v:
            return f"overlapping events in voice {voice}", []
        cur_scale = scales[-1] if scales else Fraction(1)
        if ev.onset > cur_v and not _spacer_writable(
            (ev.onset - cur_v) / cur_scale
        ):
            return f"unwritable gap in voice {voice}", []
        for _, kind in ev.tuplets:
            if kind == "start":
                scales.append(ev.scale)
            elif kind == "stop" and scales:
                scales.pop()
        boundaries.update((ev.onset, ev.end_on))
        cur_v = ev.end_on
    if cur_v < m_len and not _spacer_writable(m_len - cur_v):
        return f"unwritable trailing gap in voice {voice}", []
    return None, [(ev.onset, ev.end_on, ev.stem, ev.beams) for ev in ordered]


type _MeasureSliceMap = dict[
    str, dict[int, list[tuple[str | None, str, _VoiceMeasureRecord]]]
]


def _finalize_span_measure(
    m_idx: int, st: _SpanMeasureState, voice_slices: _MeasureSliceMap
) -> str | None:
    measure = st.measure
    if not st.voice_events and not _spacer_writable(st.m_len):
        return _at(measure, "unwritable forward duration")
    has_break = m_idx > 0 and any(
        _attr(p, "new-system") == "yes" or _attr(p, "new-page") == "yes"
        for p in measure.findall("print")
    )
    boundaries: set[Fraction] = {Fraction(0), st.m_len}
    for voice, events in st.voice_events.items():
        for split_st, home, slice_evs in _split_screen_voice(events):
            g_fault, lead_grace, mains = _check_slice_graces_and_tuplets(
                voice, slice_evs
            )
            if g_fault is not None:
                return _at(measure, g_fault)
            if not mains:
                continue
            v_fault, cleaned = _verify_voice_timeline(
                voice, mains, st.m_len, boundaries
            )
            if v_fault is not None:
                return _at(measure, v_fault)
            rec: _VoiceMeasureRecord = (
                m_idx,
                measure,
                has_break,
                lead_grace,
                st.m_len,
                cleaned,
            )
            voice_slices.setdefault(voice, {}).setdefault(m_idx, []).append(
                (split_st, home, rec)
            )
    for d_on in st.dirs_in_m:
        if (
            d_on is None
            or not (0 <= d_on <= st.m_len)
            or d_on not in boundaries
        ):
            return _at(measure, f"direction offset {d_on} off boundary")
    return None


def _link_screen_slices(
    per_m: dict[int, list[tuple[str | None, str, _VoiceMeasureRecord]]],
) -> list[list[_VoiceMeasureRecord]]:
    chains: list[list[_VoiceMeasureRecord]] = []
    active: list[tuple[str | None, str, list[_VoiceMeasureRecord]]] = []
    prev_m = -2
    for m_idx in sorted(per_m):
        slices = per_m[m_idx]
        if m_idx != prev_m + 1:
            active = []
        unsplit = (
            len(active) == 1
            and len(slices) == 1
            and active[0][0] is None
            and slices[0][0] is None
        )
        next_active: list[
            tuple[str | None, str, list[_VoiceMeasureRecord]]
        ] = []
        for split_st, home, rec in slices:
            matched = (
                active[0]
                if unsplit
                else next((a for a in active if a[1] == home), None)
            )
            if matched is not None:
                active.remove(matched)
                matched[2].append(rec)
                next_active.append((split_st, home, matched[2]))
            else:
                chain = [rec]
                chains.append(chain)
                next_active.append((split_st, home, chain))
        active = next_active
        prev_m = m_idx
    return chains


def _check_beam_tuplet_span(root: ET.Element) -> str | None:
    """Check tuplets, cross-barline beams, direction offsets and timelines."""
    divisions: Fraction | None = None
    voice_slices: _MeasureSliceMap = {}
    for m_idx, measure in enumerate(_measures(root)):
        fault, st = _scan_span_measure(measure, divisions)
        if fault is not None:
            return fault
        divisions = st.divisions
        fin_fault = _finalize_span_measure(m_idx, st, voice_slices)
        if fin_fault is not None:
            return fin_fault
    for voice, per_m in voice_slices.items():
        for chain in _link_screen_slices(per_m):
            v_fault = _check_voice_beams(voice, chain)
            if v_fault is not None:
                return v_fault
    return None


def _check_mixed_stem_cross_barline(
    run: list[tuple[ET.Element, int, str, dict[int, str]]],
) -> str | None:
    g_measures: set[int] = set()
    g_stems: set[str] = set()
    for measure, m_idx, stem, bm in run:
        b1 = bm.get(1)
        if b1 == "begin":
            g_measures = {m_idx}
            g_stems = {stem} if stem else set()
        elif b1 in {"continue", "end"}:
            g_measures.add(m_idx)
            if stem:
                g_stems.add(stem)
            if b1 == "end":
                if len(g_measures) > 1 and len(g_stems) > 1:
                    return _at(measure, "cross-barline mixed-stem beam")
                g_measures, g_stems = set(), set()
    return None


def _check_voice_beams(
    voice: str, entries: list[_VoiceMeasureRecord]
) -> str | None:
    runs: list[list[tuple[ET.Element, int, str, dict[int, str]]]] = []
    prev_m = -2
    prev_end = Fraction(-1)
    prev_len = Fraction(0)
    for m_idx, measure, has_break, lead_grace, m_len, notes in entries:
        if not notes:
            prev_m = -2
            continue
        tagged = [(measure, m_idx, st, dict(bm)) for _, _, st, bm in notes]
        if (
            runs
            and prev_m + 1 == m_idx
            and not has_break
            and not lead_grace
            and prev_end == prev_len
            and notes[0][0] == 0
        ):
            runs[-1].extend(tagged)
        else:
            runs.append(tagged)
        prev_m, prev_end, prev_len = m_idx, notes[-1][1], m_len
    for run in runs:
        fault_idx = _beam_seq_fault([bm for *_, bm in run])
        if fault_idx is not None:
            return _at(
                run[fault_idx][0],
                f"beam of voice {voice} does not pair cleanly",
            )
        mixed_fault = _check_mixed_stem_cross_barline(run)
        if mixed_fault is not None:
            return mixed_fault
    return None


def _normalize_screen_beam(
    level: int,
    state: str,
    beams: dict[int, str],
    opened: set[int],
    nxt: dict[int, str],
) -> str:
    if state != "begin":
        return state
    if level in opened and level in nxt:
        return "continue"
    if level not in opened and level > 1 and level not in nxt:
        lower = beams.get(level - 1)
        if lower in {"begin", "continue", "end"}:
            return "backward hook" if lower == "end" else "forward hook"
    return state


def _step_screen_beam(
    level: int, state: str, beams: dict[int, str], opened: set[int]
) -> bool:
    if level > 1 and level - 1 not in beams:
        return False
    if state in {"forward hook", "backward hook"}:
        need = (
            {"begin", "continue"}
            if state == "forward hook"
            else {"continue", "end"}
        )
        return (
            level > 1 and level not in opened and beams.get(level - 1) in need
        )
    if state == "begin":
        if level in opened:
            return False
        opened.add(level)
    elif state in {"continue", "end"}:
        if level not in opened:
            return False
        if state == "end":
            opened.discard(level)
    return not (
        level > 1
        and state in {"begin", "continue"}
        and beams.get(level - 1) not in {"begin", "continue"}
    )


def _beam_seq_fault(seq: list[dict[int, str]]) -> int | None:
    """Normalize MuseScore 4 beam quirks in place and return first bad index."""
    opened: set[int] = set()
    for idx, beams in enumerate(seq):
        nxt = seq[idx + 1] if idx + 1 < len(seq) else {}
        if not opened.issubset(beams):
            return idx
        for level in sorted(beams):
            state = _normalize_screen_beam(
                level, beams[level], beams, opened, nxt
            )
            beams[level] = state
            if not _step_screen_beam(level, state, beams, opened):
                return idx
    return (len(seq) - 1) if opened else None


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


_OCTAVE_SHIFT_ENDS = frozenset({"up", "down", "stop"})


def _first_overlapping_span(
    spans: list[tuple[str, Fraction, Fraction, ET.Element]],
) -> str | None:
    """Return the first same-staff octave-shift overlap, if any."""
    ordered = sorted(spans, key=lambda item: (item[0], item[1], item[2]))
    for index, (staff, start, end, _) in enumerate(ordered):
        for other_staff, other_start, other_end, other_measure in ordered[
            index + 1 :
        ]:
            if other_staff != staff or other_start >= end:
                break
            if start < other_end:
                return _at(
                    other_measure,
                    f"overlapping octave-shift spans on staff {staff}",
                )
    return None


def _check_octave_shift_span(root: ET.Element) -> str | None:
    """Check that octave-shift spans pair in order on one staff without overlap.

    An ottava line starts with ``<S(>`` and ends with ``<S)>`` not before its
    start on the same staff without reusing an open ``number``, and ottava
    lines on one staff shall not overlap (format L615-617, L692-699).
    """
    endpoints: list[tuple[Fraction, int, int, str, str, str, ET.Element]] = []
    for index, (
        measure_index,
        measure,
        onset,
        number,
        staff,
        kind,
    ) in enumerate(_octave_shift_endpoints(root)):
        if onset is None:
            return _at(measure, "octave-shift at undecidable onset")
        endpoints.append(
            (onset, measure_index, index, number, staff, kind, measure)
        )

    open_starts: dict[str, tuple[ET.Element, str, Fraction]] = {}
    spans: list[tuple[str, Fraction, Fraction, ET.Element]] = []
    for onset, _, _, number, staff, kind, measure in sorted(endpoints):
        if kind == "stop":
            started = open_starts.pop(number, None)
            if started is None or staff != started[1]:
                reason = (
                    f"octave-shift stop without start (number {number})"
                    if started is None
                    else "octave-shift stops on another staff"
                )
                return _at(measure, reason)
            if onset <= started[2]:
                return _at(measure, "octave-shift has non-positive span")
            spans.append((staff, started[2], onset, started[0]))
        elif number in open_starts:
            return _at(measure, f"overlapping octave-shift number {number}")
        else:
            open_starts[number] = (measure, staff, onset)

    if open_starts:
        number, (start_measure, _, _) = next(iter(open_starts.items()))
        return _at(start_measure, f"unclosed octave-shift number {number}")
    return _first_overlapping_span(spans)


def _direction_octave_shifts(
    measure_index: int,
    measure: ET.Element,
    direction: ET.Element,
    onset: Fraction | None,
) -> Iterator[tuple[int, ET.Element, Fraction | None, str, str, str]]:
    """Yield non-continue octave-shift marks inside one ``direction``."""
    staff = _staff(direction)
    for direction_type in direction.findall("direction-type"):
        for mark in direction_type:
            kind = _attr(mark, "type")
            if mark.tag == "octave-shift" and kind in _OCTAVE_SHIFT_ENDS:
                yield (
                    measure_index,
                    measure,
                    onset,
                    _attr(mark, "number", "1"),
                    staff,
                    kind,
                )


def _octave_shift_endpoints(
    root: ET.Element,
) -> Iterator[tuple[int, ET.Element, Fraction | None, str, str, str]]:
    """Yield non-continue octave-shift marks with score onset in quarters."""
    divisions: Fraction | None = None
    measure_start: Fraction | None = Fraction(0)
    for measure_index, measure in enumerate(_measures(root)):
        position: Fraction | None = Fraction(0)
        measure_length: Fraction | None = Fraction(0)
        for child in measure:
            if (
                child.tag == "attributes"
                and child.find("divisions") is not None
            ):
                divisions = _fraction(child.findtext("divisions"))
                if divisions is not None and divisions <= 0:
                    divisions = None
            elif child.tag == "direction":
                local = _shift(position, child, divisions, "offset")
                onset = (
                    None
                    if measure_start is None or local is None
                    else measure_start + local
                )
                yield from _direction_octave_shifts(
                    measure_index, measure, child, onset
                )
            elif (
                child.tag in {"note", "forward"}
                and child.find("chord") is None
                and child.find("grace") is None
            ):
                position = _shift(position, child, divisions, "duration")
                measure_length = (
                    None
                    if measure_length is None or position is None
                    else max(measure_length, position)
                )
            elif child.tag == "backup":
                position = _shift(position, child, divisions, "duration", -1)
                if position is not None and position < 0:
                    position = Fraction(0)
        measure_start = (
            None
            if measure_start is None or measure_length is None
            else measure_start + measure_length
        )


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
    line = _attr(mark, "line") == "yes"
    if not line and _attr(mark, "sign") == "no":
        return "pedal@sign=no"
    allowed = (
        frozenset({"start", "change", "continue", "stop", "discontinue"})
        if line
        else frozenset({"start", "change", "stop"})
    )
    return _one_of(mark, "type", allowed)


def _wedge(mark: ET.Element, _note: ET.Element | None) -> str | None:
    if _attr(mark, "niente") == "yes":
        return "wedge@niente=yes"
    return _one_of(mark, "type", frozenset({"crescendo", "diminuendo", "stop"}))


def _metronome(mark: ET.Element, _note: ET.Element | None) -> str | None:
    return _one_of(mark, "parentheses", frozenset({"", "yes", "no"}))


def _bracket(mark: ET.Element, _note: ET.Element | None) -> str | None:
    return (
        _one_of(mark, "type", frozenset({"start", "stop"}))
        or _one_of(mark, "line-end", frozenset({"none"}))
        or _one_of(mark, "line-type", frozenset({"", "solid"}))
    )


def _accidental_mark(mark: ET.Element, note: ET.Element | None) -> str | None:
    text = (mark.text or "").strip()
    if text not in _FORMAT_ACCIDENTALS:
        return f"accidental-mark {text}"
    if note is None or note.find("notations/ornaments/trill-mark") is None:
        return "accidental-mark without trill-mark"
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
    return None if len(mark) >= 1 else "dynamics with 0 marks"


def _other_dynamics(mark: ET.Element, _note: ET.Element | None) -> str | None:
    text = (mark.text or "").strip()
    return None if text else "other-dynamics ''"


_VARIANT_RULES: Mapping[
    str, Callable[[ET.Element, ET.Element | None], str | None]
] = MappingProxyType(
    {
        "tied": _typed("start", "stop"),
        "slur": _typed("start", "stop"),
        "wedge": _wedge,
        "dashes": _typed("start", "stop"),
        "bracket": _bracket,
        "metronome": _metronome,
        "octave-shift": _octave_shift,
        "pedal": _pedal,
        "fermata": _fermata,
        "tremolo": _tremolo,
        "accidental-mark": _accidental_mark,
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
    ("tie_slur", _check_tie_slur),
    ("pedal", _check_pedal),
    ("beam_tuplet_span", _check_beam_tuplet_span),
    ("octave_shift_span", _check_octave_shift_span),
    ("allowlist", _check_allowlist),
)
"""The structural checks in the order of :data:`REASONS`, without musicdiff."""
