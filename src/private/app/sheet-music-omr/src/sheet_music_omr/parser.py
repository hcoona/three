"""Scannerless parser for Text Score 2 documents.

The parser implements the lexical rules, the preprocessing and the grammar of
the format. It accepts both system documents and assembled scores and returns
the syntax tree of ``sheet_music_omr.model``. It reports the first violation
as a diagnostic with its rule class and its location in the original text,
and it never repairs input.

The parser does not check semantic rules, such as measure fill, state,
relations or carry state. A semantic validator consumes the tree and reports
its findings with the same ``Diagnostic`` type.
"""

from __future__ import annotations

import math
import re
from fractions import Fraction
from typing import TYPE_CHECKING, NoReturn

from sheet_music_omr.diagnostics import (
    Diagnostic,
    Location,
    RuleClass,
    TextScoreError,
)
from sheet_music_omr.lexical import (
    QuotedTextError,
    SourceLine,
    check_document_characters,
    end_location,
    preprocess,
    read_quoted,
)
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
    CarriedAccidental,
    CarryMode,
    CarryState,
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
    Endpoint,
    EndpointRole,
    EventCount,
    ExtendedTrillRelation,
    Fermata,
    FromOption,
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
    OpenRelation,
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
    RelationKind,
    Rest,
    RightOption,
    SingleTremolo,
    SlurRelation,
    SoundingPitch,
    Spacer,
    Stem,
    StemDirection,
    SystemDocument,
    Tempo,
    TimedReview,
    TimeOption,
    TimeSymbol,
    TimeSymbolOption,
    ToOption,
    TremoloRelation,
    TremoloRole,
    TrillEnd,
    Tuplet,
    UntimedReview,
    VoiceLine,
    Words,
)

if TYPE_CHECKING:
    from collections.abc import Callable, Iterable

    from sheet_music_omr.model import (
        Direction,
        Document,
        Event,
        MeasureOption,
        NoteFlag,
        RestFlag,
        TieIn,
        TupletItem,
    )

_DURATIONS = frozenset({"1", "2", "4", "8", "16", "32", "64", "128"})
_METER_DENOMINATORS = _DURATIONS
_STEPS = "ABCDEFG"
_DIGITS = "0123456789"
_ACCIDENTALS = ("##", "bb", "#", "b", "n")
_PARENTHESIZED_ACCIDENTALS = tuple(f"({a})" for a in _ACCIDENTALS)
_MIN_DENOMINATOR = 2
_FORMAT_LINE = "format: text-score/2"
_FORMAT_VERSION = re.compile(r"format: text-score/[1-9][0-9]*")
_LABEL = r"r([1-9][0-9]*)"


class _ParseError(Exception):
    def __init__(self, diagnostic: Diagnostic) -> None:
        super().__init__(diagnostic.message)
        self.diagnostic = diagnostic


def _fail_at(
    location: Location,
    rule: str,
    message: str,
    rule_class: RuleClass = RuleClass.GRAMMAR,
) -> NoReturn:
    raise _ParseError(Diagnostic(rule_class, rule, message, location))


class _Cursor:
    """A position within one normalized line."""

    __slots__ = ("line", "pos", "text")

    def __init__(self, line: SourceLine, pos: int = 0) -> None:
        self.line = line
        self.text = line.text
        self.pos = pos

    def loc(self, pos: int | None = None) -> Location:
        return Location(
            self.line.number, (self.pos if pos is None else pos) + 1
        )

    def found(self) -> str:
        if self.at_end():
            return "end of line"
        return repr(self.text[self.pos])

    def fail(
        self,
        rule: str,
        message: str,
        pos: int | None = None,
        rule_class: RuleClass = RuleClass.GRAMMAR,
    ) -> NoReturn:
        _fail_at(self.loc(pos), rule, message, rule_class)

    def fail_expected(self, rule: str, expected: str) -> NoReturn:
        self.fail(rule, f"expected {expected}, found {self.found()}")

    def at_end(self) -> bool:
        return self.pos >= len(self.text)

    def peek(self) -> str:
        return self.text[self.pos : self.pos + 1]

    def startswith(self, literal: str) -> bool:
        return self.text.startswith(literal, self.pos)

    def take(self, literal: str) -> bool:
        if self.text.startswith(literal, self.pos):
            self.pos += len(literal)
            return True
        return False

    def expect(self, literal: str, rule: str) -> None:
        if self.take(literal):
            return
        matched = 0
        rest = self.text[self.pos :]
        while (
            matched < len(literal)
            and matched < len(rest)
            and rest[matched] == literal[matched]
        ):
            matched += 1
        self.pos += matched
        self.fail_expected(rule, repr(literal))

    def take_one_of(self, options: Iterable[str]) -> str | None:
        for option in options:
            if self.take(option):
                return option
        return None

    def expect_one_of(self, options: tuple[str, ...], rule: str) -> str:
        option = self.take_one_of(options)
        if option is None:
            self.fail_expected(rule, " or ".join(repr(o) for o in options))
        return option

    def expect_end(self, rule: str) -> None:
        if not self.at_end():
            self.fail_expected(rule, "end of line")

    def digit(self, allowed: str, rule: str) -> int:
        char = self.peek()
        if not char or char not in allowed:
            self.fail_expected(rule, f"one of {allowed!r}")
        self.pos += 1
        return int(char)

    def digit_run(self) -> str:
        start = self.pos
        while self.peek() and self.peek() in _DIGITS:
            self.pos += 1
        return self.text[start : self.pos]

    def pos_int(self, rule: str) -> int:
        char = self.peek()
        if not char or char not in "123456789":
            self.fail_expected(rule, "a positive integer")
        return int(self.digit_run())

    def rational(self, rule: str, *, positive: bool = False) -> Fraction:
        start = self.pos
        if not positive and self.take("0"):
            return Fraction(0)
        numerator = self.pos_int(rule)
        if not self.take("/"):
            return Fraction(numerator)
        denominator = self.pos_int(rule)
        if (
            denominator < _MIN_DENOMINATOR
            or math.gcd(numerator, denominator) != 1
        ):
            self.fail(
                "rational",
                f"the rational {self.text[start : self.pos]} is not reduced",
                start,
                RuleClass.LEXICAL,
            )
        return Fraction(numerator, denominator)

    def quoted(self, rule: str, *, nonempty: bool) -> str:
        start = self.pos
        try:
            value, self.pos = read_quoted(self.text, self.pos)
        except QuotedTextError as error:
            failed_rule = error.rule if error.rule != "quoted" else rule
            self.fail(failed_rule, error.message, error.index, error.rule_class)
        if nonempty and not value:
            self.fail(rule, "expected non-empty quoted text", start)
        return value

    def sp(self, rule: str) -> None:
        if not self.take(" "):
            self.fail_expected(rule, "' '")
        while self.take(" "):
            pass

    def separator(self, rule: str, closer: str = "") -> bool:
        """Consume ``sp`` before another item; False at the closer or end."""
        if (closer and self.startswith(closer)) or (
            not closer and self.at_end()
        ):
            return False
        if self.peek() != " ":
            expected = repr(closer) if closer else "end of line"
            self.fail_expected(rule, f"' ' or {expected}")
        self.sp(rule)
        return True


# Shared lexical rules.


def _fifths(c: _Cursor, rule: str) -> int:
    if c.take("0"):
        return 0
    sign = -1 if c.take("-") else 1
    char = c.peek()
    if not char or char not in "1234567":
        c.fail_expected(rule, "a key from -7 to 7")
    c.pos += 1
    return sign * int(char)


def _meter(c: _Cursor, rule: str) -> Meter:
    location = c.loc()
    numerator = c.pos_int(rule)
    c.expect("/", rule)
    start = c.pos
    denominator = c.digit_run()
    if denominator not in _METER_DENOMINATORS:
        c.pos = start
        c.fail_expected(rule, "a meter denominator from 1 to 128")
    return Meter(numerator, int(denominator), location=location)


def _clef(c: _Cursor) -> Clef:
    location = c.loc()
    sign = ClefSign(c.expect_one_of(("G", "F", "C"), "clef"))
    line = c.digit("12345", "clef")
    change = 0
    if c.take("+"):
        c.expect("8", "clef")
        change = 1
    elif c.take("-"):
        c.expect("8", "clef")
        change = -1
    return Clef(sign, line, change, location=location)


def _label(c: _Cursor, rule: str) -> int:
    c.expect("r", rule)
    return c.pos_int(rule)


def _candidates(c: _Cursor) -> tuple[str, ...]:
    if not c.take("["):
        return ()
    candidates = [c.quoted("candidates", nonempty=True)]
    while c.take(","):
        candidates.append(c.quoted("candidates", nonempty=True))
    c.expect("]", "candidates")
    return tuple(candidates)


def _bracket_tokens(c: _Cursor, rule: str) -> list[tuple[str, int]]:
    """Read ``[a,b,...]`` and return each token with its start index."""
    c.expect("[", rule)
    tokens: list[tuple[str, int]] = []
    while True:
        start = c.pos
        while c.peek() and c.peek() not in ",]":
            c.pos += 1
        if c.at_end():
            c.fail_expected(rule, "',' or ']'")
        tokens.append((c.text[start : c.pos], start))
        if c.take("]"):
            return tokens
        c.pos += 1


# Pitches and durations.


def _pitch_name(c: _Cursor) -> tuple[str, Accidental | None, int]:
    step_char = c.peek()
    if not step_char or step_char not in _STEPS:
        c.fail_expected("step", "a step from 'A' to 'G'")
    c.pos += 1
    accidental = None
    accidental_location = c.loc()
    spelling = c.take_one_of(_ACCIDENTALS + _PARENTHESIZED_ACCIDENTALS)
    if spelling is not None:
        parenthesized = spelling.startswith("(")
        kind = AccidentalKind(spelling.strip("()"))
        accidental = Accidental(
            kind,
            parenthesized,
            location=accidental_location,
        )
    octave = c.digit(_DIGITS, "octave")
    return step_char, accidental, octave


_TIE_MARK_PATTERNS = {
    "tie-in": re.compile(r"tie-in"),
    "tie_end": re.compile(rf"tie@{_LABEL}:end"),
    "tie_start": re.compile(rf"tie@{_LABEL}:start"),
    "arc_start": re.compile(rf"arc@{_LABEL}:start"),
}


def _classify_tie_mark(token: str) -> tuple[str, int | None] | None:
    for kind, pattern in _TIE_MARK_PATTERNS.items():
        match = pattern.fullmatch(token)
        if match:
            return kind, int(match.group(1)) if match.groups() else None
    return None


def _tie_marks(c: _Cursor) -> list[tuple[str, int | None]]:
    c.expect("{", "tie_marks")
    marks: list[tuple[str, int | None, int]] = []
    while True:
        start = c.pos
        while c.peek() and c.peek() not in ",}":
            c.pos += 1
        if c.at_end():
            c.fail_expected("tie_marks", "',' or '}'")
        classified = _classify_tie_mark(c.text[start : c.pos])
        if classified is None:
            c.fail("tie_marks", "invalid tie mark", start)
        marks.append((*classified, start))
        if c.take("}"):
            break
        c.pos += 1
    kinds = [kind for kind, _, _ in marks]
    first = 1 if kinds[0] in {"tie-in", "tie_end"} else 0
    rest = kinds[first:]
    if rest and rest != ["tie_start"] and set(rest) != {"arc_start"}:
        if rest[0] == "arc_start":
            bad = first + next(
                i for i, k in enumerate(rest) if k != "arc_start"
            )
        elif rest[0] == "tie_start":
            bad = first + 1
        else:
            bad = first
        c.fail(
            "tie_marks",
            "tie marks are an optional tie end or tie-in followed by one tie "
            "start or by arc starts",
            marks[bad][2],
        )
    return [(kind, label) for kind, label, _ in marks]


def _pitch(c: _Cursor) -> Pitch:
    location = c.loc()
    parenthesized = c.take("(")
    step, accidental, octave = _pitch_name(c)
    if parenthesized:
        c.expect(")", "pitch")
    fingering: tuple[int, ...] = ()
    if c.take("f"):
        first = c.digit("12345", "fingering")
        fingering = (first,)
        if c.peek() and c.peek() in "12345":
            fingering = (first, c.digit("12345", "fingering"))
    marks = _tie_marks(c) if c.peek() == "{" else []

    def labels(kind: str) -> list[int]:
        return [label for k, label in marks if k == kind and label is not None]

    return Pitch(
        step,
        accidental,
        octave,
        parenthesized=parenthesized,
        fingering=fingering,
        tie_in=any(kind == "tie-in" for kind, _ in marks),
        tie_end=next(iter(labels("tie_end")), None),
        tie_start=next(iter(labels("tie_start")), None),
        arc_starts=tuple(labels("arc_start")),
        location=location,
    )


def _duration(c: _Cursor) -> Duration:
    location = c.loc()
    start = c.pos
    base = c.digit_run()
    if base not in _DURATIONS:
        c.pos = start
        c.fail_expected("duration", "a duration from 1 to 128")
    dots = 0
    while c.take("."):
        dots += 1
    return Duration(int(base), dots, location=location)


# Event flags.

type _FlagBuilder = Callable[[re.Match[str], Location], NoteFlag]

_ARTICULATION = re.compile(r"(stacciss|stacc|acc|ten|marc|port)([\^_]?)")
_FERMATA = re.compile(r"ferm(_?)")
_BEAM = re.compile(
    r"beam([1-5])=(start|continue|end|hook-forward|hook-backward)",
)


def _articulation(match: re.Match[str], location: Location) -> NoteFlag:
    placement = Placement(match.group(2)) if match.group(2) else None
    return Articulation(
        ArticulationKind(match.group(1)),
        placement,
        location=location,
    )


def _fermata(match: re.Match[str], location: Location) -> Fermata:
    return Fermata(inverted=bool(match.group(1)), location=location)


def _beam(match: re.Match[str], location: Location) -> Beam:
    return Beam(
        int(match.group(1)),
        BeamState(match.group(2)),
        location=location,
    )


_NOTE_FLAGS: tuple[tuple[re.Pattern[str], _FlagBuilder], ...] = (
    (_ARTICULATION, _articulation),
    (_FERMATA, _fermata),
    (_BEAM, _beam),
    (re.compile("arp"), lambda _, loc: Arpeggiate(location=loc)),
    (
        re.compile("up|down"),
        lambda m, loc: Stem(StemDirection(m.group(0)), location=loc),
    ),
    (
        re.compile("grace(/?)"),
        lambda m, loc: Grace(slashed=bool(m.group(1)), location=loc),
    ),
    (
        re.compile("xs([12])"),
        lambda m, loc: CrossStaff(int(m.group(1)), location=loc),
    ),
    (
        re.compile(
            "orn=(trill|mordent|inverted-mordent|turn|inverted-turn)",
        ),
        lambda m, loc: Ornament(OrnamentKind(m.group(1)), location=loc),
    ),
    (
        re.compile("tremolo=single:([1-8])"),
        lambda m, loc: SingleTremolo(int(m.group(1)), location=loc),
    ),
    (
        re.compile(rf"rel=slur@{_LABEL}:(start|end)"),
        lambda m, loc: SlurRelation(
            int(m.group(1)),
            EndpointRole(m.group(2)),
            location=loc,
        ),
    ),
    (
        re.compile(rf"rel=tremolo@{_LABEL}:(two-start|two-end):([1-8])"),
        lambda m, loc: TremoloRelation(
            int(m.group(1)),
            TremoloRole(m.group(2)),
            int(m.group(3)),
            location=loc,
        ),
    ),
    (
        re.compile(rf"rel=arpeggio@{_LABEL}:member"),
        lambda m, loc: ArpeggioRelation(int(m.group(1)), location=loc),
    ),
    (
        re.compile(rf"rel=ornament@{_LABEL}:(start|end):extended-trill"),
        lambda m, loc: ExtendedTrillRelation(
            int(m.group(1)),
            EndpointRole(m.group(2)),
            location=loc,
        ),
    ),
)
_REST_FLAGS = _NOTE_FLAGS[1:3]
_BEAM_FLAGS = _NOTE_FLAGS[2:3]


def _flags(
    c: _Cursor,
    rule: str,
    builders: tuple[tuple[re.Pattern[str], _FlagBuilder], ...],
    description: str,
) -> list[NoteFlag]:
    flags: list[NoteFlag] = []
    for token, start in _bracket_tokens(c, rule):
        for pattern, build in builders:
            match = pattern.fullmatch(token)
            if match:
                flags.append(build(match, c.loc(start)))
                break
        else:
            c.fail(rule, f"invalid {description} {token!r}", start)
    return flags


# Events.


def _note(c: _Cursor) -> Note:
    location = c.loc()
    pitches = [_pitch(c)]
    while c.take("+"):
        pitches.append(_pitch(c))
    c.expect("/", "note")
    duration = _duration(c)
    flags: list[NoteFlag] = []
    if c.peek() == "[":
        flags = _flags(c, "note_flag", _NOTE_FLAGS, "note flag")
    return Note(tuple(pitches), duration, tuple(flags), location=location)


def _rest(c: _Cursor) -> Rest:
    location = c.loc()
    c.expect("r/", "rest")
    duration = _duration(c)
    flags: list[RestFlag] = []
    if c.peek() == "[":
        flags = [
            flag
            for flag in _flags(c, "rest_flag", _REST_FLAGS, "rest flag")
            if isinstance(flag, (Fermata, Beam))
        ]
    return Rest(duration, tuple(flags), location=location)


def _spacer(c: _Cursor) -> Spacer:
    location = c.loc()
    c.expect("x/", "rest")
    duration = _duration(c)
    beams: list[Beam] = []
    if c.peek() == "[":
        beams = [
            flag
            for flag in _flags(c, "beam_flags", _BEAM_FLAGS, "spacer flag")
            if isinstance(flag, Beam)
        ]
    return Spacer(duration, tuple(beams), location=location)


def _measure_rest(c: _Cursor) -> MeasureRest:
    location = c.loc()
    c.expect("mrest", "mrest")
    fermata = None
    if c.take("["):
        fermata_location = c.loc()
        inverted = c.expect_one_of(("ferm_", "ferm"), "fermata") == "ferm_"
        fermata = Fermata(inverted=inverted, location=fermata_location)
        c.expect("]", "mrest")
    return MeasureRest(fermata, location=location)


def _timed_review(c: _Cursor) -> TimedReview:
    location = c.loc()
    c.expect("?(", "review_span")
    duration = c.rational("review_span", positive=True)
    c.expect(")", "review_span")
    return TimedReview(duration, _candidates(c), location=location)


def _tuplet(c: _Cursor) -> Tuplet:
    location = c.loc()
    actual = c.pos_int("tuplet")
    c.expect(":", "tuplet")
    normal = c.pos_int("tuplet")
    c.expect("{", "tuplet")
    items = [_tuplet_item(c)]
    while c.separator("tuplet", "}"):
        items.append(_tuplet_item(c))
    c.expect("}", "tuplet")
    return Tuplet(actual, normal, tuple(items), location=location)


# Directions.


def _relation_suffix(c: _Cursor, head: str, tail: str) -> int:
    c.expect(head, "direction")
    label = _label(c, "label")
    c.expect(tail, "direction")
    return label


def _untimed_review(c: _Cursor, location: Location) -> Direction:
    return UntimedReview(_candidates(c), location=location)


def _hairpin_start(kind: HairpinKind) -> _DirectionBuilder:
    def build(c: _Cursor, location: Location) -> Direction:
        label = _relation_suffix(c, "[rel=hairpin@", f":start:{kind}]")
        return HairpinStart(kind, label, location=location)

    return build


def _hairpin_end(c: _Cursor, location: Location) -> Direction:
    return HairpinEnd(
        _relation_suffix(c, "[rel=hairpin@", ":end]"),
        location=location,
    )


def _ottava(sign: OttavaSign, *, start: bool) -> _DirectionBuilder:
    role = "start" if start else "end"

    def build(c: _Cursor, location: Location) -> Direction:
        label = _relation_suffix(c, "[rel=ottava@", f":{role}:")
        variant = OttavaSign(
            c.expect_one_of(tuple(s.value for s in OttavaSign), "ottava_sign"),
        )
        c.expect("]", "direction")
        if start:
            return OttavaStart(sign, label, variant, location=location)
        return OttavaEnd(sign, label, variant, location=location)

    return build


def _pedal_open(c: _Cursor, location: Location) -> Direction:
    c.expect("[rel=pedal@", "direction")
    label = _label(c, "label")
    c.expect(":", "direction")
    action = c.expect_one_of(("line-start", "line-change"), "direction")
    c.expect("]", "direction")
    return PedalLine(label, PedalAction(action), location=location)


def _pedal_release(c: _Cursor, location: Location) -> Direction:
    label = _relation_suffix(c, "[rel=pedal@", ":line-release]")
    return PedalLine(label, PedalAction.LINE_RELEASE, location=location)


def _dashes_end(c: _Cursor, location: Location) -> Direction:
    return DashesEnd(
        _relation_suffix(c, "[rel=dashes@", ":end]"),
        location=location,
    )


def _trill_end(c: _Cursor, location: Location) -> Direction:
    return TrillEnd(
        _relation_suffix(c, "[rel=ornament@", ":end:extended-trill]"),
        location=location,
    )


def _text_direction(c: _Cursor, location: Location) -> Direction:
    text = c.quoted("direction", nonempty=True)
    if c.take(">"):
        return Words(text, location=location)
    c.expect("(>", "direction")
    label = _relation_suffix(c, "[rel=dashes@", ":start]")
    return DashesStart(text, label, location=location)


def _clef_change(c: _Cursor, location: Location) -> Direction:
    clef = _clef(c)
    c.expect(">", "direction")
    return ClefChange(clef, location=location)


def _metronome(c: _Cursor) -> Metronome:
    location = c.loc()
    unit = c.expect_one_of(
        tuple(u.value for u in MetronomeUnit),
        "metronome",
    )
    c.expect("=", "metronome")
    value = c.pos_int("metronome")
    return Metronome(MetronomeUnit(unit), value, location=location)


def _tempo(c: _Cursor, location: Location) -> Direction:
    text_start = c.pos
    text = c.quoted("tempo_args", nonempty=False)
    metronome = None
    if c.peek() == " ":
        c.sp("tempo_args")
        metronome = _metronome(c)
    elif not text:
        c.fail(
            "tempo_args",
            "empty tempo text requires a metronome mark",
            text_start,
        )
    c.expect(">", "direction")
    return Tempo(text, metronome, location=location)


def _rehearsal(c: _Cursor, location: Location) -> Direction:
    text = c.quoted("direction", nonempty=True)
    c.expect(">", "direction")
    return Rehearsal(text, location=location)


def _pedal_sign(c: _Cursor, location: Location) -> Direction:
    kind = PedalSignKind(c.expect_one_of(("down", "up"), "direction"))
    c.expect(">", "direction")
    return PedalSign(kind, location=location)


def _dynamic(c: _Cursor, location: Location) -> Direction:
    above = c.take("^")
    start = c.pos
    while c.peek() and c.peek().isascii() and c.peek().isalpha():
        c.pos += 1
    name = c.text[start : c.pos]
    if name not in DynamicMark._value2member_map_:
        c.pos = start
        c.fail_expected("direction", "a direction")
    c.expect(">", "direction")
    return Dynamic(DynamicMark(name), above, location=location)


type _DirectionBuilder = Callable[[_Cursor, Location], Direction]

_DIRECTIONS: tuple[tuple[str, _DirectionBuilder], ...] = (
    ("<?>", _untimed_review),
    ("<cresc(>", _hairpin_start(HairpinKind.CRESC)),
    ("<dim(>", _hairpin_start(HairpinKind.DIM)),
    ("<hairpin)>", _hairpin_end),
    *((f"<{s}(>", _ottava(s, start=True)) for s in OttavaSign),
    *((f"<{s})>", _ottava(s, start=False)) for s in OttavaSign),
    ("<ped(>", _pedal_open),
    ("<ped)>", _pedal_release),
    ("<dashes)>", _dashes_end),
    ("<trill)>", _trill_end),
    ('<"', _text_direction),
    ("<clef ", _clef_change),
    ("<tempo ", _tempo),
    ("<rehearsal ", _rehearsal),
    ("<pedal-sign ", _pedal_sign),
    ("<", _dynamic),
)


def _direction(c: _Cursor) -> Direction:
    location = c.loc()
    for prefix, build in _DIRECTIONS:
        if c.startswith(prefix):
            # A quoted direction keeps its opening quote for the text reader.
            c.pos += len(prefix) - (1 if prefix == '<"' else 0)
            return build(c, location)
    return c.fail_expected("direction", "'<'")


type _EventParser = Callable[[_Cursor], Event]

_TUPLET_ITEM_PARSERS: dict[str, _EventParser] = {
    "<": _direction,
    "r": _rest,
    "x": _spacer,
    "(": _note,
    **dict.fromkeys(_STEPS, _note),
    **dict.fromkeys("123456789", _tuplet),
}
_EVENT_PARSERS: dict[str, _EventParser] = {
    **_TUPLET_ITEM_PARSERS,
    "m": _measure_rest,
    "?": _timed_review,
}


def _tuplet_item(c: _Cursor) -> TupletItem:
    parse = _TUPLET_ITEM_PARSERS.get(c.peek())
    if parse is None:
        c.fail_expected(
            "tuplet_item",
            "a direction, tuplet, note, rest or spacer",
        )
    item = parse(c)
    if isinstance(item, (MeasureRest, TimedReview)):  # pragma: no cover
        c.fail("tuplet_item", "unexpected event in a tuplet")
    return item


def _event(c: _Cursor) -> Event:
    parse = _EVENT_PARSERS.get(c.peek())
    if parse is None:
        c.fail_expected("event", "an event")
    return parse(c)


# Measure blocks.


def _ending(c: _Cursor, location: Location) -> EndingOption:
    if c.take("continue"):
        return EndingOption(EndingKind.CONTINUE, location=location)
    if c.take("end"):
        return EndingOption(EndingKind.END, location=location)
    number = c.pos_int("ending")
    c.expect(":", "ending")
    kind = EndingKind(c.expect_one_of(("start", "single"), "ending"))
    return EndingOption(kind, number, location=location)


type _OptionParser = Callable[[_Cursor, Location], MeasureOption]

_MEASURE_OPTIONS: tuple[tuple[str, _OptionParser], ...] = (
    (
        "len=",
        lambda c, loc: LenOption(c.rational("measure_option"), location=loc),
    ),
    (
        "key=",
        lambda c, loc: KeyOption(_fifths(c, "fifths"), location=loc),
    ),
    (
        "time-symbol=",
        lambda c, loc: TimeSymbolOption(
            TimeSymbol(
                c.expect_one_of(
                    tuple(s.value for s in TimeSymbol),
                    "measure_option",
                ),
            ),
            location=loc,
        ),
    ),
    (
        "time=",
        lambda c, loc: TimeOption(_meter(c, "meter"), location=loc),
    ),
    ("left=repeat", lambda _, loc: LeftRepeatOption(location=loc)),
    (
        "right=",
        lambda c, loc: RightOption(
            Barline(
                c.expect_one_of(
                    tuple(b.value for b in Barline),
                    "measure_option",
                ),
            ),
            location=loc,
        ),
    ),
    (
        "from=",
        lambda c, loc: FromOption(c.rational("measure_option"), location=loc),
    ),
    (
        "to=",
        lambda c, loc: ToOption(c.rational("measure_option"), location=loc),
    ),
    ("ending=", _ending),
)


def _measure_option(c: _Cursor) -> MeasureOption:
    location = c.loc()
    for prefix, parse in _MEASURE_OPTIONS:
        if c.take(prefix):
            return parse(c, location)
    return c.fail_expected("measure_option", "a measure option")


def _measure_header(line: SourceLine) -> tuple[int, list[MeasureOption]]:
    c = _Cursor(line)
    c.expect("m ", "measure_block")
    number = c.pos_int("measure_block")
    options: list[MeasureOption] = []
    while c.separator("measure_block"):
        options.append(_measure_option(c))
    return number, options


def _voice_line(line: SourceLine) -> VoiceLine:
    c = _Cursor(line)
    if c.peek() not in {"1", "2"}:
        c.fail_expected("voice_line", "a voice line 'S.V: ' or a block 'm N'")
    staff = c.digit("12", "voice_line")
    c.expect(".", "voice_line")
    voice = c.digit("1234", "voice_line")
    c.expect(":", "voice_line")
    c.sp("voice_line")
    events = [_event(c)]
    while c.separator("voice_line"):
        events.append(_event(c))
    return VoiceLine(staff, voice, tuple(events), location=c.loc(0))


# Carry state.


def _count_entry(c: _Cursor) -> EventCount:
    location = c.loc()
    c.expect('"', "count_entry")
    staff = c.digit("12", "count_entry")
    c.expect(".", "count_entry")
    voice = c.digit("1234", "count_entry")
    c.expect('":', "count_entry")
    count = c.pos_int("count_entry")
    return EventCount(staff, voice, count, location=location)


_ALTERS = ("-2", "-1", "0", "1", "2")


def _acc_entry(c: _Cursor) -> CarriedAccidental:
    location = c.loc()
    c.expect('{"staff":', "acc_entry")
    staff = c.digit("12", "acc_entry")
    c.expect(',"step":"', "acc_entry")
    step = c.expect_one_of(tuple(_STEPS), "acc_entry")
    c.expect('"', "acc_entry")
    octave = c.digit(_DIGITS, "acc_entry") if c.take(',"octave":') else None
    c.expect(',"alter":', "acc_entry")
    alter = int(c.expect_one_of(_ALTERS, "alter"))
    c.expect("}", "acc_entry")
    return CarriedAccidental(staff, step, octave, alter, location=location)


def _endpoint(c: _Cursor) -> Endpoint:
    location = c.loc()
    c.expect('{"measure":', "endpoint")
    measure = c.pos_int("endpoint")
    c.expect(',"offset":"', "endpoint")
    offset = c.rational("endpoint")
    c.expect('","staff":', "endpoint")
    staff = c.digit("12", "endpoint")
    c.expect(',"voice":', "endpoint")
    voice = c.digit("1234", "endpoint")
    c.expect(',"event_index":', "endpoint")
    event_index = c.pos_int("endpoint")
    c.expect(',"member_index":', "endpoint")
    member_index = None if c.take("null") else c.pos_int("endpoint")
    c.expect("}", "endpoint")
    return Endpoint(
        measure,
        offset,
        staff,
        voice,
        event_index,
        member_index,
        location=location,
    )


def _sounding(c: _Cursor) -> SoundingPitch:
    location = c.loc()
    c.expect('{"step":"', "sounding")
    step = c.expect_one_of(tuple(_STEPS), "sounding")
    c.expect('","octave":', "sounding")
    octave = c.digit(_DIGITS, "sounding")
    c.expect(',"alter":', "sounding")
    alter: int | TieIn = "tie-in"
    if not c.take('"tie-in"'):
        alter = int(c.expect_one_of(_ALTERS, "alter"))
    c.expect("}", "sounding")
    return SoundingPitch(step, octave, alter, location=location)


def _quoted_variant(c: _Cursor, options: tuple[str, ...]) -> str:
    c.expect(',"variant":"', "open_entry")
    variant = c.expect_one_of(options, "open_entry")
    c.expect('"', "open_entry")
    return variant


def _open_entry(c: _Cursor) -> OpenRelation:
    location = c.loc()
    c.expect('{"id":"', "open_entry")
    label = _label(c, "label")
    c.expect('","kind":"', "open_entry")
    kind = RelationKind(
        c.expect_one_of(
            tuple(k.value for k in RelationKind),
            "open_entry",
        ),
    )
    c.expect('","start":', "open_entry")
    start = _endpoint(c)
    pitch = variant = written = scale = None
    if kind in {RelationKind.TIE, RelationKind.ARC}:
        c.expect(',"pitch":', "open_entry")
        pitch = _sounding(c)
    elif kind is RelationKind.TREMOLO:
        variant = _quoted_variant(c, tuple("12345678"))
        c.expect(',"written":"', "open_entry")
        written = c.rational("open_entry")
        c.expect('","scale":"', "open_entry")
        scale = c.rational("open_entry")
        c.expect('"', "open_entry")
    elif kind is RelationKind.ORNAMENT:
        variant = _quoted_variant(c, ("extended-trill",))
    elif kind is RelationKind.HAIRPIN:
        variant = _quoted_variant(c, tuple(h.value for h in HairpinKind))
    elif kind is RelationKind.OTTAVA:
        variant = _quoted_variant(c, tuple(s.value for s in OttavaSign))
    c.expect("}", "open_entry")
    return OpenRelation(
        label,
        kind,
        start,
        pitch,
        variant,
        written,
        scale,
        location=location,
    )


def _entries[T](
    c: _Cursor,
    closer: str,
    parse: Callable[[_Cursor], T],
) -> tuple[T, ...]:
    if c.startswith(closer):
        return ()
    entries = [parse(c)]
    while c.take(","):
        entries.append(parse(c))
    return tuple(entries)


def _carry_state(c: _Cursor) -> CarryState:
    location = c.loc()
    c.expect('{"measure":', "carry_state")
    measure = c.pos_int("carry_state")
    c.expect(',"offset":"', "carry_state")
    offset = c.rational("carry_state")
    c.expect('","event_counts":{', "carry_state")
    counts = _entries(c, "}", _count_entry)
    c.expect('},"accidental_scope":"', "carry_state")
    scope = CarryMode(c.expect_one_of(("octave", "letter"), "carry_mode"))
    c.expect('","accidentals":[', "carry_state")
    accidentals = _entries(c, "]", _acc_entry)
    c.expect('],"open":[', "carry_state")
    open_entries = _entries(c, "]", _open_entry)
    c.expect("]}", "carry_state")
    return CarryState(
        measure,
        offset,
        counts,
        scope,
        accidentals,
        open_entries,
        location=location,
    )


# Documents.


class _DocumentParser:
    """Parses the line structure of a document."""

    def __init__(self, text: str, lines: tuple[SourceLine, ...]) -> None:
        self._end = end_location(text)
        self._lines = lines
        self._index = 0

    def _peek(self) -> SourceLine | None:
        if self._index < len(self._lines):
            return self._lines[self._index]
        return None

    def _next(self, rule: str, expected: str) -> SourceLine:
        line = self._peek()
        if line is None:
            _fail_at(self._end, rule, f"expected {expected}, found end of text")
        self._index += 1
        return line

    def _header(self, prefix: str, rule: str) -> _Cursor:
        line = self._next(rule, f"{prefix.strip()!r}")
        c = _Cursor(line)
        c.expect(prefix, rule)
        return c

    def _quoted_header(self, prefix: str, rule: str) -> tuple[str, Location]:
        c = self._header(prefix, rule)
        location = c.loc(0)
        text = c.quoted(rule, nonempty=True)
        c.expect_end(rule)
        return text, location

    def _format(self) -> None:
        line = self._next("format_line", repr(_FORMAT_LINE))
        if line.text != _FORMAT_LINE and _FORMAT_VERSION.fullmatch(line.text):
            _fail_at(
                Location(line.number, len("format: text-score/") + 1),
                "format_version",
                f"unsupported format version {line.text[8:]!r}",
                RuleClass.UNSUPPORTED,
            )
        c = _Cursor(line)
        c.expect(_FORMAT_LINE, "format_line")
        c.expect_end("format_line")

    def _optional_quoted(self, prefix: str, rule: str) -> str | None:
        line = self._peek()
        if line is None or not line.text.startswith(prefix):
            return None
        return self._quoted_header(prefix, rule)[0]

    def _state_headers(
        self,
    ) -> tuple[int, Meter, tuple[Clef, Clef], CarryMode]:
        c = self._header("key: ", "key_line")
        key = _fifths(c, "fifths")
        c.expect_end("key_line")
        c = self._header("time: ", "time_line")
        time = _meter(c, "meter")
        c.expect_end("time_line")
        c = self._header("clefs: ", "clefs_line")
        first = _clef(c)
        c.sp("clefs_line")
        second = _clef(c)
        c.expect_end("clefs_line")
        c = self._header("carry: ", "carry_line")
        carry = CarryMode(c.expect_one_of(("octave", "letter"), "carry_mode"))
        c.expect_end("carry_line")
        return key, time, (first, second), carry

    def _pos_int_header(self, prefix: str, rule: str) -> int:
        c = self._header(prefix, rule)
        value = c.pos_int(rule)
        c.expect_end(rule)
        return value

    def _headings(self) -> list[Heading]:
        headings: list[Heading] = []
        while (line := self._peek()) is not None and line.text.startswith(
            "heading:",
        ):
            text, location = self._quoted_header("heading: ", "heading_line")
            headings.append(Heading(text, location=location))
        return headings

    def _block(self) -> MeasureBlock:
        header = self._next("measure_block", "a measure block 'm N'")
        if not header.text.startswith("m"):
            _fail_at(
                Location(header.number, 1),
                "measure_block",
                "expected a measure block 'm N'",
            )
        number, options = _measure_header(header)
        voices: list[VoiceLine] = []
        while (line := self._peek()) is not None and not (
            line.text.startswith(("m", "heading:"))
        ):
            voices.append(_voice_line(line))
            self._index += 1
        if not voices:
            location = self._end if line is None else Location(line.number, 1)
            _fail_at(location, "voice_line", "expected a voice line 'S.V: '")
        return MeasureBlock(
            number,
            tuple(options),
            tuple(voices),
            location=Location(header.number, 1),
        )

    def parse(self) -> Document:
        self._format()
        title = self._optional_quoted("title: ", "title_line")
        composer = self._optional_quoted("composer: ", "composer_line")
        key, time, clefs, carry = self._state_headers()
        line = self._peek()
        assembled = (
            title is not None
            or composer is not None
            or line is None
            or not line.text.startswith("page:")
        )
        if assembled:
            return self._assembled(title, composer, key, time, clefs, carry)
        page = self._pos_int_header("page: ", "page_line")
        system = self._pos_int_header("system: ", "system_line")
        c = self._header("carry-in: ", "carry_in_line")
        carry_in = _carry_state(c)
        c.expect_end("carry_in_line")
        headings = self._headings()
        blocks = [self._block()]
        while self._peek() is not None:
            blocks.append(self._block())
        return SystemDocument(
            key,
            time,
            clefs,
            carry,
            page,
            system,
            carry_in,
            tuple(headings),
            tuple(blocks),
            location=Location(1, 1),
        )

    def _assembled(  # noqa: PLR0913, PLR0917
        self,
        title: str | None,
        composer: str | None,
        key: int,
        time: Meter,
        clefs: tuple[Clef, Clef],
        carry: CarryMode,
    ) -> AssembledScore:
        blocks = [HeadedBlock(tuple(self._headings()), self._block())]
        while self._peek() is not None:
            blocks.append(HeadedBlock(tuple(self._headings()), self._block()))
        return AssembledScore(
            title,
            composer,
            key,
            time,
            clefs,
            carry,
            tuple(blocks),
            location=Location(1, 1),
        )


def parse_document(text: str) -> Document:
    """Parse a system document or an assembled score.

    Args:
        text: The decoded document text.

    Returns:
        The syntax tree of the document.

    Raises:
        TextScoreError: When the text breaks a lexical or grammar rule. The
            error carries one diagnostic for the first violation.
    """
    check_document_characters(text)
    try:
        return _DocumentParser(text, preprocess(text)).parse()
    except _ParseError as error:
        raise TextScoreError((error.diagnostic,)) from None
