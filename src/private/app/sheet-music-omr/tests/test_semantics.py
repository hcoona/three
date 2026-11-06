"""Scenario tests for the semantic validator and carry-out derivation.

Every document here is self-authored for this project and transcribes no
existing score.
"""

from __future__ import annotations

from fractions import Fraction
from pathlib import Path

import pytest

from sheet_music_omr import (
    TextScoreError,
    carry_out,
    check_document,
    parse_document,
    validate_document,
)
from sheet_music_omr.model import (
    CarriedAccidental,
    EventCount,
    Note,
    RelationKind,
    SystemDocument,
)
from sheet_music_omr.pitches import resolve_pitches
from sheet_music_omr.relations import analyze_relations
from sheet_music_omr.timeline import build_timeline
from sheet_music_omr.writer import write_carry_state

FIXTURES = Path(__file__).resolve().parent / "fixtures"
SPEC = Path(__file__).resolve().parents[1] / "docs" / "text-format.md"

INITIAL = (
    '{"measure":1,"offset":"0","event_counts":{},"accidental_scope":"octave",'
    '"accidentals":[],"open":[]}'
)
SLUR = (
    '[{"id":"r1","kind":"slur","start":{"measure":4,"offset":"0","staff":1,'
    '"voice":1,"event_index":1,"member_index":null}}]'
)
TIE = (
    '[{"id":"r1","kind":"tie","start":{"measure":4,"offset":"1/4","staff":1,'
    '"voice":1,"event_index":2,"member_index":1},'
    '"pitch":{"step":"C","octave":4,"alter":0}}]'
)
ARC = TIE.replace('"tie"', '"arc"')
TREMOLO = (
    '[{"id":"r1","kind":"tremolo","start":{"measure":4,"offset":"1/4",'
    '"staff":1,"voice":1,"event_index":2,"member_index":null},'
    '"variant":"2","written":"3/8","scale":"1"}]'
)
TRILL = (
    '[{"id":"r1","kind":"ornament","start":{"measure":4,"offset":"0",'
    '"staff":1,"voice":1,"event_index":1,"member_index":null},'
    '"variant":"extended-trill"}]'
)


def system(body: str, carry_in: str = INITIAL, scope: str = "octave") -> str:
    """Return a system document in 2/4 whose blocks start on line 9."""
    return (
        "format: text-score/2\nkey: 0\ntime: 2/4\nclefs: G2 F4\n"
        f"carry: {scope}\npage: 1\nsystem: 1\ncarry-in: {carry_in}\n{body}\n"
    )


def score(body: str) -> str:
    """Return an assembled score in 2/4 whose blocks start on line 6."""
    return (
        "format: text-score/2\nkey: 0\ntime: 2/4\nclefs: G2 F4\n"
        f"carry: octave\n{body}\n"
    )


def carry(
    offset: str = "0",
    counts: str = "{}",
    open_relations: str = "[]",
) -> str:
    """Return a carry-in state at measure 5."""
    return (
        f'{{"measure":5,"offset":"{offset}","event_counts":{counts},'
        f'"accidental_scope":"octave","accidentals":[],"open":{open_relations}}}'
    )


def _codes(text: str) -> list[str]:
    diagnostics = validate_document(parse_document(text))
    return [f"{d.code}@{d.location}" for d in diagnostics]


def _spec_examples() -> list[str]:
    text = SPEC.read_text("utf-8")
    blocks = text.split("```text\n")[1:]
    return [
        block.split("```", 1)[0]
        for block in blocks
        if block.startswith("format: text-score/2")
    ]


VALID_FIXTURES = sorted(FIXTURES.glob("*.txt"))

VALID = {
    "tie-in into a second ending": system(
        "m 1\n1.1: C#4/2\nm 2 right=repeat ending=1:single\n1.1: E4/2\n"
        "m 3 ending=2:single\n1.1: C4{tie-in}/2",
    ),
    "tie-in into an unlabeled last ending": system(
        "m 1\n1.1: C#4/2\nm 2 right=repeat ending=1:single\n1.1: E4/2\n"
        "m 3\n1.1: C#4{tie-in}/2",
    ),
    "deferred tie-in": system(
        "m 5 ending=2:single\n1.1: C4{tie-in}/2",
        carry(),
    ),
    "carried arc ends as a slur": system(
        "m 5\n1.1: D4/2[rel=slur@r1:end]",
        carry(open_relations=ARC),
    ),
    "carried arc ends as a tie": system(
        "m 5\n1.1: C4{tie@r1:end}/2",
        carry(open_relations=ARC),
    ),
    "carried trill ends on a direction": system(
        "m 5\n1.1: C4/4 <trill)>[rel=ornament@r1:end:extended-trill] D4/4",
        carry(open_relations=TRILL),
    ),
    "carried slur keeps its label": system(
        "m 5\n1.1: C4/4[rel=slur@r2:start]"
        " D4/4[rel=slur@r1:end,rel=slur@r2:end]",
        carry(open_relations=SLUR),
    ),
    "two slurs share endpoints": system(
        "m 1\n1.1: C4/4[rel=slur@r1:start,rel=slur@r2:start]"
        " D4/4[rel=slur@r1:end,rel=slur@r2:end]",
    ),
    "beams with a hook and a rest": system(
        "m 1\n1.1: C5/8.[beam1=start] D5/16[beam1=continue,beam2=hook-backward]"
        " r/8[beam1=continue] E5/8[beam1=end]",
    ),
    "grace notes beam apart": system(
        "m 1\n1.1: D5/16[grace,beam1=start,beam2=start]"
        " E5/16[grace,beam1=end,beam2=end] C5/2",
    ),
    "arpeggio across staves": system(
        "m 1\n1.1: E4+G4/2[rel=arpeggio@r1:member]\n"
        "2.1: C3/2[rel=arpeggio@r1:member]",
    ),
    "carried tremolo with eight dots": system(
        "m 5 from=511/4096 to=511/2048\n"
        "1.1: D4/8........[rel=tremolo@r1:two-end:2]",
        carry("511/4096", '{"1.1":1}', TREMOLO.replace('"3/8"', '"511/2048"')),
    ),
    "directions at one onset in any order": system(
        "m 1\n1.1: C4/4 <hairpin)>[rel=hairpin@r1:end]"
        " <cresc(>[rel=hairpin@r1:start:cresc]"
        " <ped(>[rel=pedal@r2:line-start] D4/4"
        " <ped)>[rel=pedal@r2:line-release] <ped(>[rel=pedal@r2:line-change]",
    ),
}

INVALID = [
    ("m 1\n1.1: <clef F4> C4/2", INITIAL, "semantic/initial_state@10:6"),
    ("m 1 key=1\n1.1: C4/2", INITIAL, "semantic/initial_state@9:5"),
    ("m 2\n1.1: C4/2", INITIAL, "semantic/measure_id@9:1"),
    (
        "m 1 right=double right=final\n1.1: C4/2",
        INITIAL,
        "semantic/option_repeated@9:18",
    ),
    ("m 1 to=1\n1.1: C4/1", INITIAL, "semantic/measure_range@9:1"),
    ("m 1 to=1/4\n1.1: C4/4\nm 2\n1.1: C4/2", INITIAL, "semantic/fragment@9:5"),
    (
        "m 5 from=1/4\n1.1: C4/2",
        carry("1/2", '{"1.1":1}'),
        "semantic/fragment@9:5",
    ),
    (
        "m 5 from=1/4 key=2\n1.1: x/4",
        carry("1/4", '{"1.1":1}'),
        "semantic/option_owner@9:14",
    ),
    ("m 1 time-symbol=cut\n1.1: C4/2", INITIAL, "semantic/time_symbol@9:5"),
    (
        "m 1 to=1/4\n1.1: C4/4 D4/8[grace]",
        INITIAL,
        "semantic/fragment_grace@10:11",
    ),
    ("m 1 ending=continue\n1.1: C4/2", INITIAL, "semantic/ending@9:5"),
    (
        "m 1 ending=1:start\n1.1: C4/2\nm 2\n1.1: C4/2",
        INITIAL,
        "semantic/ending@11:1",
    ),
    ("m 1\n1.1: C4/2\n1.1: D4/2", INITIAL, "semantic/voice_line_repeated@11:1"),
    ("m 1\n1.1: C4/4", INITIAL, "semantic/measure_fill@10:1"),
    ("m 1\n1.1: 3:2{D4/8[grace]} C4/2", INITIAL, "semantic/tuplet@10:6"),
    ("m 1\n1.1: mrest C4/4", INITIAL, "semantic/measure_rest@10:6"),
    ("m 1 to=1/4\n1.1: mrest", INITIAL, "unsupported/split_measure_rest@10:6"),
    ("m 1\n1.1: C4/2[stacc,stacc^]", INITIAL, "semantic/repeated_mark@10:17"),
    ("m 1\n1.1: C4/2[xs1]", INITIAL, "semantic/cross_staff@10:11"),
    ("m 1\n1.1: E4+C4/2", INITIAL, "semantic/chord_order@10:9"),
    ("m 1\n1.1: C4+C4/2", INITIAL, "unsupported/duplicate_chord_member@10:9"),
    ("m 1\n1.1: C4f12/2", INITIAL, "unsupported/fingering@10:6"),
    ("m 1\n1.1: (C(#)4)/2", INITIAL, "semantic/parenthesized_accidental@10:8"),
    (
        "m 1\n1.1: <15ma(>[rel=ottava@r1:start:15ma] C9/2",
        INITIAL,
        "semantic/sounding_octave@10:40",
    ),
    ("m 1\n1.1: C4/8[beam1=start] D4/8 C4/4", INITIAL, "semantic/beam@10:11"),
    (
        "m 1\n1.1: C4/4 D4/8 E4/8[beam1=start]",
        INITIAL,
        "unsupported/beam_boundary@10:21",
    ),
    (
        "m 1\n1.1: C4/8[beam1=end] D4/8 C4/4",
        INITIAL,
        "unsupported/beam_boundary@10:11",
    ),
    (
        "m 1\n1.1: C4/16[beam1=start] D4/16[beam1=end] E4/8 F4/4",
        INITIAL,
        "semantic/beam@10:12",
    ),
    (
        "m 1\n1.1: C4/4 <clef F4> <clef C3> D4/4",
        INITIAL,
        "semantic/clef_change@10:21",
    ),
    ('m 1\n1.1: ?(1/2)["a","a"]', INITIAL, "semantic/review_candidates@10:6"),
    (
        "m 1\n1.1: C4{tie@r1:start}/8 D4/8 C4{tie@r1:end}/4",
        INITIAL,
        "semantic/tie@10:30",
    ),
    (
        "m 1\n1.1: C4{tie@r1:start}/4 D4{tie@r1:end}/4",
        INITIAL,
        "semantic/tie@10:25",
    ),
    (
        "m 1\n1.1: C#4{tie@r1:start}/4 Cn4{tie@r1:end}/4",
        INITIAL,
        "semantic/tie@10:26",
    ),
    ("m 1\n1.1: C4{tie@r1:start}/4 D4/4", INITIAL, "semantic/tie@10:6"),
    ("m 1\n1.1: C4{tie-in}/2", INITIAL, "semantic/tie_in@10:6"),
    (
        (
            "m 1\n1.1: C4/2\nm 2 right=repeat ending=1:single\n1.1: E4/2\n"
            "m 3 ending=2:single\n1.1: D4{tie-in}/2"
        ),
        INITIAL,
        "semantic/tie_in@14:6",
    ),
    (
        (
            "m 1\n1.1: C#4/2\nm 2 right=repeat ending=1:single\n1.1: E4/2\n"
            "m 3 ending=2:single\n1.1: Cn4{tie-in}/2"
        ),
        INITIAL,
        "semantic/tie_in@14:6",
    ),
    (
        "m 1\n1.1: C4/4[rel=slur@r1:end] D4/4[rel=slur@r1:start]",
        INITIAL,
        "semantic/slur@10:11",
    ),
    (
        (
            "m 1\n1.1: C4/4[rel=tremolo@r1:two-start:2] D4/8"
            " E4/4[rel=tremolo@r1:two-end:2] F4/8"
        ),
        INITIAL,
        "semantic/tremolo@10:49",
    ),
    (
        (
            "m 1\n1.1: C4/4[rel=ornament@r1:start:extended-trill]"
            " D4/4[rel=ornament@r1:end:extended-trill]"
        ),
        INITIAL,
        "semantic/ornament@10:11",
    ),
    (
        (
            "m 1\n1.1: C4/4[orn=trill,rel=ornament@r1:start:extended-trill]"
            " <trill)>[rel=ornament@r1:end:extended-trill] D4/4"
        ),
        INITIAL,
        "semantic/ornament@10:59",
    ),
    (
        "m 1\n1.1: C4/2[rel=arpeggio@r1:member]",
        INITIAL,
        "semantic/arpeggio@10:11",
    ),
    (
        (
            "m 1\n1.1: <hairpin)>[rel=hairpin@r1:end] C4/4"
            " <cresc(>[rel=hairpin@r1:start:cresc] D4/4"
        ),
        INITIAL,
        "semantic/hairpin@10:6",
    ),
    (
        (
            "m 1\n1.1: <ped)>[rel=pedal@r1:line-release] C4/4"
            " <ped(>[rel=pedal@r1:line-start] D4/4"
        ),
        INITIAL,
        "semantic/pedal@10:6",
    ),
    (
        (
            "m 1\n1.1: <ped(>[rel=pedal@r1:line-start] C4/4"
            " <ped)>[rel=pedal@r1:line-release] D4/4"
            " <ped(>[rel=pedal@r1:line-change]"
        ),
        INITIAL,
        "semantic/pedal@10:82",
    ),
    (
        (
            "m 5\n1.1: <ped)>[rel=pedal@r1:line-release] C4/4"
            " <ped(>[rel=pedal@r1:line-change] D4/4"
        ),
        carry(open_relations=SLUR.replace('"slur"', '"pedal"')),
        "semantic/pedal@10:45",
    ),
    (
        "m 1\n1.1: C4/8[grace,rel=tremolo@r1:two-start:2] D4/2",
        INITIAL,
        "semantic/tremolo@10:17",
    ),
    (
        (
            "m 1\n1.1: <dashes)>[rel=dashes@r1:end] C4/4"
            ' <"dim."(>[rel=dashes@r1:start] D4/4'
        ),
        INITIAL,
        "semantic/dashes@10:6",
    ),
    (
        "m 1\n1.1: <8va(>[rel=ottava@r1:start:8vb] C4/2",
        INITIAL,
        "semantic/ottava@10:6",
    ),
    (
        (
            "m 1\n1.1: <8va(>[rel=ottava@r1:start:8va] C4/4"
            " <8va(>[rel=ottava@r2:start:8va] C4/4"
        ),
        INITIAL,
        "semantic/ottava@10:43",
    ),
    (
        (
            "m 1\n1.1: <8va(>[rel=ottava@r1:start:8va] C4/2\n"
            "2.1: C3/4 <8va)>[rel=ottava@r1:end:8va] C3/4"
        ),
        INITIAL,
        "semantic/ottava@11:11",
    ),
    (
        (
            "m 1\n1.1: <ped(>[rel=pedal@r1:line-start] C4/4"
            " <ped)>[rel=pedal@r1:line-release] D4/4"
            " <ped)>[rel=pedal@r1:line-release]"
        ),
        INITIAL,
        "semantic/relation_reuse@10:82",
    ),
    (
        "m 1\n1.1: <cresc(>[rel=hairpin@r1:start:cresc] C4/2[rel=slur@r1:end]",
        INITIAL,
        "semantic/relation_kind@10:48",
    ),
    (
        "m 5\n1.1: <8va)>[rel=ottava@r1:end:8va] C4/2",
        carry(open_relations=ARC),
        "semantic/relation_kind@10:6",
    ),
    (
        (
            "m 1\n1.1: C4/2[rel=tremolo@r1:two-start:2]"
            " D4/2[rel=tremolo@r1:two-end:3]"
        ),
        INITIAL,
        "semantic/relation_variant@10:44",
    ),
    (
        "m 1\n1.1: C4/2[rel=slur@r1:end]",
        INITIAL,
        "semantic/relation_start@10:11",
    ),
    (
        "m 1\n1.1: C4/4[rel=slur@r2:start] D4/4[rel=slur@r2:end]",
        INITIAL,
        "semantic/relation_label@10:11",
    ),
    (
        (
            "m 1\n1.1:"
            " C4/4[rel=slur@r1:start,rel=slur@r2:start,rel=slur@r3:start]"
            " D4/4[rel=slur@r1:end,rel=slur@r2:end,rel=slur@r3:end]"
        ),
        INITIAL,
        "semantic/relation_duplicate@10:29",
    ),
    (
        "m 5\n1.1: C4/4[rel=slur@r1:start] D4/4",
        carry(open_relations=SLUR),
        "semantic/relation_reuse@10:11",
    ),
    (
        'heading: "x"\nm 5 from=1/4\n1.1: C4/4',
        carry("1/4", '{"1.1":1}'),
        "unsupported/heading_in_measure@9:1",
    ),
    (
        "m 5 from=1/4\n1.1: C4/4",
        carry("1/4", '{"2.1":1,"1.1":1}'),
        "semantic/carry_order@8:63",
    ),
    ("m 5\n1.1: C4/2", carry(counts='{"1.1":1}'), "semantic/carry_in@8:53"),
    ("m 5\n1.1: C4/2", carry(open_relations=TIE), "semantic/tie@8:108"),
    (
        "m 5\n1.1: C4/2[rel=tremolo@r1:two-end:2] D4/4",
        carry(open_relations=TREMOLO),
        "semantic/tremolo@10:11",
    ),
]

INVALID_SCORES = [
    ("m 1\n1.1: C4/2[rel=slur@r1:start]", "semantic/relation_open@7:11"),
    ("m 1\n1.1: C4{arc@r1:start}/2", "semantic/arc@7:6"),
    ("m 1 to=1/4\n1.1: C4/4", "semantic/fragment@6:1"),
]


@pytest.mark.parametrize("path", VALID_FIXTURES, ids=lambda p: p.name)
def test_fixtures_are_valid(path: Path) -> None:
    """Every fixture outside ``invalid`` is semantically valid."""
    assert _codes(path.read_text("utf-8")) == []


@pytest.mark.parametrize("text", _spec_examples())
def test_spec_examples_are_valid(text: str) -> None:
    """Every complete example of the format record is valid."""
    assert _codes(text) == []


@pytest.mark.parametrize("text", VALID.values(), ids=list(VALID))
def test_valid_documents(text: str) -> None:
    """Documents that exercise permissive rules are valid."""
    assert _codes(text) == []


@pytest.mark.parametrize(
    ("body", "carry_in", "expected"),
    INVALID,
    ids=[case[2] for case in INVALID],
)
def test_invalid_system_documents(
    body: str, carry_in: str, expected: str
) -> None:
    """Each invalid document reports its rule class and location."""
    assert expected in _codes(system(body, carry_in))


@pytest.mark.parametrize(("body", "expected"), INVALID_SCORES)
def test_invalid_assembled_scores(body: str, expected: str) -> None:
    """Assembled-score rules report their rule class and location."""
    assert expected in _codes(score(body))


def test_carry_scope_must_match_carry() -> None:
    """The ``carry`` line and ``accidental_scope`` agree."""
    text = system("m 1\n1.1: C4/2", scope="letter")
    assert "semantic/carry_scope@8:11" in _codes(text)


def test_check_document_raises_with_diagnostics() -> None:
    """``check_document`` raises with every diagnostic of the document."""
    document = parse_document(system("m 1\n1.1: C4/4"))
    with pytest.raises(TextScoreError) as error:
        check_document(document)
    assert [d.code for d in error.value.diagnostics] == [
        "semantic/measure_fill"
    ]


def test_spec_carry_out_matches_next_carry_in() -> None:
    """The carry-out of the first example is the carry-in of the second."""
    first, second = (parse_document(text) for text in _spec_examples()[:2])
    assert isinstance(first, SystemDocument)
    assert isinstance(second, SystemDocument)
    assert carry_out(first) == second.carry_in


def test_fixture_carry_out_matches_next_carry_in() -> None:
    """Every carried kind passes from the first fixture to its continuation."""
    first = parse_document((FIXTURES / "system-first.txt").read_text("utf-8"))
    second = parse_document(
        (FIXTURES / "system-continuation.txt").read_text("utf-8"),
    )
    assert isinstance(first, SystemDocument)
    assert isinstance(second, SystemDocument)
    derived = carry_out(first)
    assert derived == second.carry_in
    assert {entry.kind for entry in derived.open} == {
        RelationKind.ARC,
        RelationKind.DASHES,
        RelationKind.HAIRPIN,
        RelationKind.ORNAMENT,
        RelationKind.OTTAVA,
        RelationKind.PEDAL,
        RelationKind.TIE,
        RelationKind.TREMOLO,
    }


def test_carry_out_keeps_fragment_state() -> None:
    """A system that ends inside a measure carries counts and accidentals."""
    document = parse_document(
        (FIXTURES / "system-continuation.txt").read_text("utf-8"),
    )
    assert isinstance(document, SystemDocument)
    derived = carry_out(document)
    assert (derived.measure, derived.offset) == (5, Fraction(1, 4))
    assert derived.event_counts == (EventCount(1, 1, 1), EventCount(2, 1, 2))
    assert derived.accidentals == (
        CarriedAccidental(2, "B", 3, -1),
        CarriedAccidental(2, "G", 4, 2),
    )
    assert derived.open == ()


def test_carry_out_defers_tie_in_alteration() -> None:
    """A tie that leaves a deferred tie-in pitch carries ``tie-in``."""
    document = parse_document(
        system("m 5 ending=2:single\n1.1: C4{tie-in,tie@r1:start}/2", carry()),
    )
    assert isinstance(document, SystemDocument)
    assert write_carry_state(carry_out(document)) == (
        '{"measure":6,"offset":"0","event_counts":{},"accidental_scope":'
        '"octave","accidentals":[],"open":[{"id":"r1","kind":"tie","start":'
        '{"measure":5,"offset":"0","staff":1,"voice":1,"event_index":1,'
        '"member_index":1},"pitch":{"step":"C","octave":4,"alter":"tie-in"}}]}'
    )


def test_carry_out_rejects_invalid_documents() -> None:
    """``carry_out`` derives state only from a valid document."""
    document = parse_document(system("m 1\n1.1: C4/4"))
    assert isinstance(document, SystemDocument)
    with pytest.raises(TextScoreError):
        carry_out(document)


# Focused checks of the core algorithms.


def test_nested_tuplet_groups_fill_and_index() -> None:
    """Nested ratios multiply, and indices count directions, not containers."""
    text = system(
        "m 1\n1.1: 3:2{C5/8 <p> 5:4{D5/32 E5/32 F5/32 G5/32 A5/32} C6/8} B5/4"
    )
    timeline = build_timeline(parse_document(text))
    (line,) = timeline.lines
    assert line.total == Fraction(1, 2)
    assert [p.index for p in line.events] == [1, 2, 3, 4, 5, 6, 7, 8, 9]
    assert [p.onset for p in line.events][:4] == [
        0,
        Fraction(1, 12),
        Fraction(1, 12),
        Fraction(1, 12) + Fraction(1, 60),
    ]


@pytest.mark.parametrize(
    ("scope", "alters"),
    [("octave", [1, 1, 0, 0]), ("letter", [1, 1, 1, 0])],
)
def test_accidentals_follow_the_carry_scope(
    scope: str, alters: list[int]
) -> None:
    """The format's example resolves per octave or per letter."""
    carry_in = INITIAL.replace("octave", scope)
    body = "m 1 time=1/1\n1.1: F#4/4 F4/4 F5/4 Fn4/4"
    text = system(body, carry_in, scope).replace("time: 2/4", "time: 1/1")
    text = text.replace("m 1 time=1/1", "m 1")
    document = parse_document(text)
    assert validate_document(document) == ()
    events = [p.event for p in build_timeline(document).events()]
    assert all(isinstance(event, Note) for event in events)
    resolved = _resolved_alters(text)
    assert resolved == alters


def _resolved_alters(text: str) -> list[int | str | None]:
    document = parse_document(text)
    timeline = build_timeline(document)
    resolver = resolve_pitches(
        document,
        timeline,
        analyze_relations(document, timeline),
    )
    return [
        resolver.resolved[(p.ident, 1)].alter
        for p in timeline.events()
        if isinstance(p.event, Note)
    ]


def test_carried_accidentals_seed_the_measure() -> None:
    """A continuation fragment starts with the carried accidental state."""
    carry_in = (
        '{"measure":5,"offset":"1/4","event_counts":{"1.1":1},'
        '"accidental_scope":"octave","accidentals":[{"staff":1,"step":"F",'
        '"octave":4,"alter":1}],"open":[]}'
    )
    text = system("m 5 from=1/4\n1.1: F4/8 F5/8", carry_in)
    assert validate_document(parse_document(text)) == ()
    assert _resolved_alters(text) == [1, 0]
