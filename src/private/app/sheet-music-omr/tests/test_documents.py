"""Scenario tests for parsing, canonical writing and grammar diagnostics."""

from __future__ import annotations

import re
from pathlib import Path

import pytest

from sheet_music_omr import (
    TextScoreError,
    decode_source,
    parse_document,
    review_markers,
    write_document,
)
from sheet_music_omr.model import AssembledScore, SystemDocument

PROJECT = Path(__file__).resolve().parents[1]
FIXTURES = PROJECT / "tests" / "fixtures"
SPEC = PROJECT / "docs" / "text-format.md"

HEADER = (
    "format: text-score/2\n"
    "key: 0\n"
    "time: 4/4\n"
    "clefs: G2 F4\n"
    "carry: octave\n"
    "page: 1\n"
    "system: 1\n"
    'carry-in: {"measure":1,"offset":"0","event_counts":{},'
    '"accidental_scope":"octave","accidentals":[],"open":[]}\n'
)


def _fixture_body(path: Path) -> str:
    """Return a fixture without its provenance comment line."""
    text = path.read_text(encoding="utf-8")
    assert text.startswith("// Self-authored for this project")
    return text.split("\n", 1)[1]


def _canonical_fixtures() -> list[Path]:
    return [
        path
        for path in sorted(FIXTURES.glob("*.txt"))
        if not path.name.startswith("noncanonical-system.txt")
    ]


def _spec_section(title: str) -> str:
    text = SPEC.read_text(encoding="utf-8")
    start = text.index(f"\n## {title}\n")
    end = text.index("\n## ", start + 1)
    return text[start:end]


def _spec_examples() -> list[str]:
    text = SPEC.read_text(encoding="utf-8")
    blocks = re.findall(r"```text\n(.*?)```", text, re.DOTALL)
    return [block for block in blocks if block.startswith("format:")]


def _witness_document(witness: str) -> str:
    """Wrap a category witness into the smallest document that holds it."""
    if witness.startswith("m "):
        return f"{HEADER}{witness}\n1.1: mrest\n"
    if witness.startswith("heading: "):
        return f"{HEADER}{witness}\nm 1\n1.1: mrest\n"
    if re.match(r"\d\.\d: ", witness):
        return f"{HEADER}m 1\n{witness}\n"
    return f"{HEADER}m 1\n1.1: {witness}\n"


def _witnesses() -> list[str]:
    section = _spec_section("Category encodings")
    bullets = re.split(r"\n- ", section)[1:]
    assert len(bullets) == 27  # noqa: PLR2004 - the spec's category count
    witnesses: list[str] = []
    for bullet in bullets:
        tail = bullet[bullet.index("Witness") :]
        witnesses.extend(re.findall(r"`([^`]+)`", tail))
    return witnesses


@pytest.mark.parametrize("path", _canonical_fixtures(), ids=lambda p: p.name)
def test_canonical_fixtures_round_trip(path: Path) -> None:
    """Canonical fixtures survive parsing and writing unchanged."""
    text = _fixture_body(path)
    document = parse_document(decode_source(path.read_bytes()))
    assert write_document(document) == text
    assert parse_document(text) == document


def test_noncanonical_document_canonicalizes() -> None:
    """A non-canonical document writes its canonical spelling."""
    source = FIXTURES / "noncanonical-system.txt"
    expected = _fixture_body(FIXTURES / "noncanonical-system.canonical.txt")
    written = write_document(parse_document(source.read_text("utf-8")))
    assert written == expected
    assert write_document(parse_document(written)) == written


def test_document_kinds() -> None:
    """System documents and assembled scores keep their headers."""
    first = parse_document(_fixture_body(FIXTURES / "system-first.txt"))
    score = parse_document(_fixture_body(FIXTURES / "assembled-score.txt"))
    assert isinstance(first, SystemDocument)
    assert [h.text for h in first.headings] == [
        "Little Waltz",
        'for "Ana" \\ op. 1',
    ]
    assert isinstance(score, AssembledScore)
    assert score.title == "Tiny Canon"
    assert [[h.text for h in b.headings] for b in score.blocks] == [
        ["Tiny Canon"],
        [],
        [],
        [],
        ["Coda"],
    ]


@pytest.mark.parametrize("text", _spec_examples())
def test_spec_examples_round_trip(text: str) -> None:
    """Every complete example in the format record round-trips."""
    document = parse_document(text)
    assert write_document(document) == text
    assert parse_document(write_document(document)) == document


@pytest.mark.parametrize("witness", _witnesses())
def test_category_witnesses_round_trip(witness: str) -> None:
    """Every category witness of the format record round-trips."""
    text = _witness_document(witness)
    document = parse_document(text)
    assert write_document(document) == text
    assert parse_document(write_document(document)) == document


def test_review_markers_are_listed_in_text_order() -> None:
    """Timed and untimed markers are listed with candidates."""
    path = FIXTURES / "system-continuation.txt"
    markers = review_markers(parse_document(path.read_text("utf-8")))
    listed = [
        (
            type(m.marker).__name__,
            m.measure,
            f"{m.staff}.{m.voice}",
            str(m.location),
            m.marker.candidates,
        )
        for m in markers
    ]
    assert listed == [
        ("UntimedReview", 3, "2.1", "12:37", ("sfz", "sf")),
        ("TimedReview", 3, "2.1", "12:53", ("B2/4", "Bb2/4")),
        ("TimedReview", 4, "1.1", "14:66", ()),
        ("UntimedReview", 4, "2.1", "15:40", ()),
    ]


VALID_BLOCK = "m 1\n1.1: C4/1\n"


@pytest.mark.parametrize(
    ("text", "code", "location"),
    [
        pytest.param(
            HEADER.replace("\n", "\r\n", 1) + VALID_BLOCK,
            "lexical/line_end",
            "1:21",
            id="carriage-return",
        ),
        pytest.param(
            HEADER + "m 1\n1.1:\tC4/1\n",
            "lexical/tab",
            "10:5",
            id="tab",
        ),
        pytest.param(
            HEADER + VALID_BLOCK[:-1],
            "lexical/final_lf",
            "10:10",
            id="missing-final-lf",
        ),
        pytest.param(
            "\ufeff" + HEADER + VALID_BLOCK,
            "lexical/encoding",
            "1:1",
            id="byte-order-mark",
        ),
        pytest.param(
            HEADER.replace("/2", "/3", 1) + VALID_BLOCK,
            "unsupported/format_version",
            "1:20",
            id="format-version",
        ),
        pytest.param(
            HEADER + "m 1 len=4/2\n1.1: C4/1\n",
            "lexical/rational",
            "9:9",
            id="unreduced-rational",
        ),
        pytest.param(
            HEADER + "m 1\n1.1: C4/3\n",
            "grammar/duration",
            "10:9",
            id="duration",
        ),
        pytest.param(
            HEADER + "m 1\n1.1: C4/1[stacc,bogus]\n",
            "grammar/note_flag",
            "10:17",
            id="note-flag",
        ),
        pytest.param(
            HEADER + "m 1\n1.1: <?>[] C4/1\n",
            "grammar/candidates",
            "10:10",
            id="empty-candidates",
        ),
        pytest.param(
            HEADER + 'heading: ""\n' + VALID_BLOCK,
            "grammar/heading_line",
            "9:10",
            id="empty-heading",
        ),
        pytest.param(
            HEADER,
            "grammar/measure_block",
            "9:1",
            id="no-measure-block",
        ),
        pytest.param(
            HEADER.replace('"measure":1', '"measure": 1') + VALID_BLOCK,
            "grammar/carry_state",
            "8:22",
            id="carry-state-space",
        ),
        pytest.param(
            HEADER + 'm 1\n1.1: <"\\x"> C4/1\n',
            "grammar/text_char",
            "10:8",
            id="unknown-escape",
        ),
        pytest.param(
            HEADER + 'm 1\n1.1: <"\\b"> C4/1\n',
            "lexical/text_char",
            "10:8",
            id="backspace-escape",
        ),
        pytest.param(
            HEADER + 'm 1\n1.1: <"\\ud800"> C4/1\n',
            "lexical/surrogate",
            "10:8",
            id="lone-surrogate",
        ),
        pytest.param(
            HEADER + "m 1\n1.1: 3:2{?(1/4) C4/4 C4/4} C4/2\n",
            "grammar/tuplet_item",
            "10:10",
            id="review-span-in-tuplet",
        ),
        pytest.param(
            HEADER + "m 1 bogus=1\n1.1: C4/1\n",
            "grammar/measure_option",
            "9:5",
            id="measure-option",
        ),
        pytest.param(
            HEADER + "m 1\n1.5: C4/1\n",
            "grammar/voice_line",
            "10:3",
            id="voice-number",
        ),
        pytest.param(
            HEADER + VALID_BLOCK + 'heading: "Late"\n' + VALID_BLOCK,
            "grammar/measure_block",
            "11:1",
            id="system-heading-after-block",
        ),
        pytest.param(
            HEADER + "m 1\n1.1: C4{tie@r1:start,tie@r2:end}/1\n",
            "grammar/tie_marks",
            "10:22",
            id="tie-mark-order",
        ),
    ],
)
def test_invalid_documents_report_rule_and_location(
    text: str,
    code: str,
    location: str,
) -> None:
    """An invalid document reports its rule class and location."""
    with pytest.raises(TextScoreError) as caught:
        parse_document(text)
    (diagnostic,) = caught.value.diagnostics
    assert (diagnostic.code, str(diagnostic.location)) == (code, location)


def test_invalid_utf8_reports_location() -> None:
    """Invalid UTF-8 reports the location of the first bad byte."""
    with pytest.raises(TextScoreError) as caught:
        decode_source(b"format: text-score/2\nkey: \xff\n")
    (diagnostic,) = caught.value.diagnostics
    assert (diagnostic.code, str(diagnostic.location)) == (
        "lexical/encoding",
        "2:6",
    )
