"""Scenario tests for the system-assembly test of the harness.

Every score here is self-authored for this project and transcribes no
existing score.
"""

from __future__ import annotations

from dataclasses import replace
from pathlib import Path
from typing import TYPE_CHECKING

import pytest

from sheet_music_omr.harness.systems import (
    AssemblyTestStatus,
    SplitError,
    SystemStart,
    assembly_test,
    compare_assembly,
    split_score,
    system_starts,
)
from sheet_music_omr.model import AssembledScore
from sheet_music_omr.musicxml_forward import to_musicxml
from sheet_music_omr.parser import parse_document
from sheet_music_omr.writer import write_document

if TYPE_CHECKING:
    from sheet_music_omr.model import SystemDocument

FIXTURES = Path(__file__).resolve().parent / "fixtures"
SPLITS = FIXTURES / "harness" / "systems"
STUDY = FIXTURES / "assembly" / "study.expected.txt"
BASE = FIXTURES / "comparison" / "base.musicxml"
BREAK = '<print new-system="yes"/>'

# A tie and a slur cross the break before m 3; system 1 holds a key change
# and a clef change, and system 2 starts with another key change.
CROSSING = """format: text-score/2
title: "Split Study"
composer: "Test Author"
key: 0
time: 4/4
clefs: G2 F4
carry: octave
heading: "Split Study"
m 1
1.1: C5/4[rel=slur@r1:start] D5/4 E5/4 G5/4[rel=slur@r1:end]
2.1: C3/2 <clef G2> C4/2
m 2 key=2
1.1: G5/4 A5/4 E5/4 D5{tie@r3:start}/4
2.1: D4/2 A4/2[rel=slur@r2:start]
m 3 key=-1
1.1: D5{tie@r3:end}/2 A5/2
2.1: A4/2[rel=slur@r2:end] <clef F4> D3/2
m 4 right=final
1.1: D5/1
2.1: D3/1
"""


def _score(text: str) -> AssembledScore:
    document = parse_document(text)
    assert isinstance(document, AssembledScore)
    return document


def _canonical(path: Path) -> str:
    return write_document(parse_document(path.read_text("utf-8")))


def _musicxml(*measures: str) -> str:
    """Return a one-part score whose measures hold the given children."""
    body = "".join(
        f'<measure number="{number}">{children}</measure>'
        for number, children in enumerate(measures, 1)
    )
    return (
        '<?xml version="1.0" encoding="UTF-8"?><score-partwise version="4.0">'
        '<part-list><score-part id="P1"><part-name>Piano</part-name>'
        f'</score-part></part-list><part id="P1">{body}</part>'
        "</score-partwise>"
    )


def _crossing_x() -> bytes:
    """Return the MusicXML of ``CROSSING`` with a system break at m 3."""
    x = to_musicxml(_score(CROSSING))
    start = b'<measure number="3">'
    assert x.count(start) == 1
    assert BREAK.encode() not in x
    return x.replace(start, start + BREAK.encode())


def _split_crossing() -> tuple[SystemDocument, ...]:
    return split_score(
        _score(CROSSING), (SystemStart(0, 1, 1), SystemStart(2, 1, 2))
    )


# Breaks.


def test_system_starts_number_pages_and_systems() -> None:
    """Number systems per page and ignore a break in the first measure."""
    layout = system_starts(
        _musicxml(
            '<print new-page="yes"/>',
            "",
            BREAK,
            '<print new-page="yes"/>',
            '<print new-system="yes" new-page="yes"/>',
            '<print new-system="no"/>',
        )
    )

    assert (layout.measures, layout.has_breaks) == (6, True)
    assert layout.starts == (
        SystemStart(0, 1, 1),
        SystemStart(2, 1, 2),
        SystemStart(3, 2, 1),
        SystemStart(4, 3, 1),
    )


def test_system_starts_without_breaks() -> None:
    """Report one system when only the first measure has a break."""
    layout = system_starts(_musicxml('<print new-page="yes"/>', ""))

    assert (layout.measures, layout.has_breaks) == (2, False)
    assert layout.starts == (SystemStart(0, 1, 1),)


def test_break_after_a_note_fails_closed() -> None:
    """Fail the split when a break starts a system inside a measure."""
    x = _musicxml("", f"<attributes/><note/>{BREAK}")

    with pytest.raises(SplitError, match="break_inside_measure"):
        system_starts(x)
    result = assembly_test(x)
    assert result.status is AssemblyTestStatus.SPLIT_FAILED
    assert result.detail == "musicxml/break_inside_measure (measure 2)"
    assert not result.passed


# Split.


def test_split_reproduces_the_study_systems() -> None:
    """Split the study at m 3 and m 6 into documents that assemble back."""
    score = _score(STUDY.read_text("utf-8"))
    starts = (SystemStart(0, 1, 1), SystemStart(2, 1, 2), SystemStart(5, 2, 1))

    documents = split_score(score, starts)

    assert [write_document(document) for document in documents] == [
        _canonical(SPLITS / "study-split-1.txt"),
        _canonical(SPLITS / "study-split-2.txt"),
        _canonical(FIXTURES / "assembly" / "study-3.txt"),
    ]
    result = compare_assembly(score, documents)
    assert (result.status, result.systems) == (AssemblyTestStatus.EQUAL, 3)


def test_split_carries_a_tie_and_a_slur_across_a_break() -> None:
    """Carry a tie as a tie, and take the headers from the state before."""
    first, second = _split_crossing()

    assert [heading.text for heading in first.headings] == ["Split Study"]
    assert (second.key, second.time) == (2, first.time)
    assert [clef.sign for clef in second.clefs] == ["G", "G"]
    assert [
        (entry.label, entry.kind.value) for entry in second.carry_in.open
    ] == [(2, "slur"), (3, "tie")]
    assert "D5{tie@r3:end}/2" in write_document(second)


def test_heading_inside_a_system_fails_the_split() -> None:
    """Fail the split when a heading precedes a block inside a system."""
    score = _score(STUDY.read_text("utf-8"))

    with pytest.raises(SplitError, match=r"heading_inside_system .*measure 6"):
        split_score(score, (SystemStart(0, 1, 1), SystemStart(2, 1, 2)))


# The whole test.


def test_piece_with_crossing_relations_assembles_equal() -> None:
    """Pass a piece whose tie, slur and clef state cross its break."""
    result = assembly_test(_crossing_x())

    assert (result.status, result.systems) == (AssemblyTestStatus.EQUAL, 2)
    assert result.texts_differ is False
    assert result.passed


def test_calibration_fixture_assembles_equal_at_its_break() -> None:
    """Pass the comparison calibration fixture at its system break."""
    result = assembly_test(BASE.read_bytes())

    assert (result.status, result.systems) == (AssemblyTestStatus.EQUAL, 2)


def test_piece_without_breaks_is_not_evidence() -> None:
    """Return no-breaks, which does not pass, for a piece without breaks."""
    x = BASE.read_bytes()
    assert x.count(BREAK.encode()) == 1

    result = assembly_test(x.replace(BREAK.encode(), b""))

    assert (result.status, result.systems) == (AssemblyTestStatus.NO_BREAKS, 1)
    assert result.texts_differ is None
    assert not result.passed


def test_piece_that_does_not_reverse_fails() -> None:
    """Report the reverse converter's rule codes for an unusable piece."""
    one_staff = _musicxml(
        "<attributes><divisions>1</divisions></attributes>",
        BREAK,
    )

    results = [assembly_test(x) for x in (b"not xml", one_staff)]

    assert [(r.status, r.detail, r.passed) for r in results] == [
        (AssemblyTestStatus.REVERSE_FAILED, "lexical/xml", False),
        (
            AssemblyTestStatus.REVERSE_FAILED,
            "unsupported/staff_count (measure 1)",
            False,
        ),
    ]


def test_differing_texts_without_counted_differences_pass() -> None:
    """Pass differing texts whose conversions compare without difference."""
    whole = _score(CROSSING.replace("D5/1", "D5/1[up]", 1))

    result = compare_assembly(whole, _split_crossing())

    assert result.status is AssemblyTestStatus.COMPARED_PASS
    assert (result.texts_differ, result.differences) == (True, 0)
    assert result.passed


def test_differing_texts_with_counted_differences_fail() -> None:
    """Fail differing texts whose conversions differ in content."""
    whole = _score(CROSSING.replace("D5/1", "E5/1", 1))

    result = compare_assembly(whole, _split_crossing())

    assert result.status is AssemblyTestStatus.COMPARED_FAIL
    assert result.differences == 1
    assert result.category_counts == (("pitch", 1),)
    assert not result.passed


def test_assembly_failure_names_rule_codes() -> None:
    """Report assembly failures by rule code and system, without content."""
    first, second = _split_crossing()

    result = compare_assembly(_score(CROSSING), (first, replace(second, key=0)))

    assert result.status is AssemblyTestStatus.ASSEMBLY_FAILED
    assert result.detail == "assembly/state (system 2)"
    assert result.texts_differ is None
