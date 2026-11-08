"""Tests for the structural scope screen of the round-trip harness."""

from __future__ import annotations

import dataclasses
import importlib.util
import sys
from pathlib import Path
from typing import TYPE_CHECKING

import pytest

from sheet_music_omr.harness.screen import (
    ScreenResult,
    allowlist_violations,
    screen,
)

if TYPE_CHECKING:
    from types import ModuleType

TESTS = Path(__file__).resolve().parent
FIXTURES = TESTS / "fixtures" / "comparison"
BASE = (FIXTURES / "base.musicxml").read_text(encoding="utf-8")
SINGLE = (FIXTURES / "single-staff.musicxml").read_text(encoding="utf-8")


def _variant(*edits: tuple[str, str]) -> str:
    """Return the base fixture with exact text edits, each made once."""
    text = BASE
    for old, new in edits:
        assert text.count(old) == 1, old
        text = text.replace(old, new)
    return text


def _edit(note_id: str, old: str, new: str) -> tuple[str, str]:
    """Return an edit that replaces text once inside one note of the base."""
    start = BASE.index(f'<note id="{note_id}">')
    block = BASE[start : BASE.index("</note>", start) + len("</note>")]
    assert block.count(old) == 1, old
    return block, block.replace(old, new)


def _inside(note_id: str, xml: str) -> tuple[str, str]:
    """Return an edit that appends XML at the end of one note of the base."""
    return _edit(note_id, "</note>", f"  {xml}\n      </note>")


def _before(note_id: str, xml: str) -> tuple[str, str]:
    """Return an edit that inserts XML before one note of the base."""
    anchor = f'<note id="{note_id}">'
    return anchor, f"{xml}\n      {anchor}"


def _direction(*types: str, staff: int = 1) -> str:
    kinds = "".join(
        f"<direction-type>{kind}</direction-type>" for kind in types
    )
    return f"<direction>{kinds}<staff>{staff}</staff></direction>"


def _attributes(xml: str) -> str:
    return f"<attributes>{xml}</attributes>"


def _grace(voice: int) -> str:
    return (
        "<note><grace/><pitch><step>C</step><octave>3</octave></pitch>"
        f"<voice>{voice}</voice><type>eighth</type><staff>2</staff></note>"
    )


_FINAL_BARLINE = "<bar-style>light-heavy</bar-style>"
_TRILL = "<ornaments><trill-mark/></ornaments>"


def _fingers(*digits: str, trill: bool = False, attributes: str = "") -> str:
    marks = "".join(
        f"<fingering{attributes}>{digit}</fingering>" for digit in digits
    )
    ornaments = _TRILL if trill else ""
    return f"<notations>{ornaments}<technical>{marks}</technical></notations>"


FAILING_CASES = [
    pytest.param(
        "parts_staves",
        (("<staves>2</staves>", "<staves>3</staves>"),),
        id="three-staves",
    ),
    pytest.param(
        "parts_staves",
        (
            (
                "</score-part>\n  </part-list>",
                '</score-part>\n    <score-part id="P2"/>\n  </part-list>',
            ),
            (
                "</part>\n</score-partwise>",
                '</part>\n  <part id="P2"/>\n</score-partwise>',
            ),
        ),
        id="two-parts",
    ),
    pytest.param(
        "parts_staves",
        (
            _before(
                "m1s1n1",
                _attributes(
                    '<staff-details number="2">'
                    "<staff-lines>4</staff-lines></staff-details>"
                ),
            ),
        ),
        id="four-line-staff",
    ),
    pytest.param(
        "parts_staves",
        (
            _before(
                "m3s2n1",
                _attributes('<clef number="2"><sign>percussion</sign></clef>'),
            ),
        ),
        id="percussion-clef",
    ),
    pytest.param(
        "parts_staves",
        (
            (
                '<score-partwise version="4.0">',
                '<score-timewise version="4.0">',
            ),
            ("</score-partwise>", "</score-timewise>"),
        ),
        id="timewise",
    ),
    pytest.param(
        "clef",
        (
            _before(
                "m3s1n1",
                _attributes(
                    '<clef number="1"><sign>G</sign><line>6</line></clef>'
                ),
            ),
        ),
        id="clef-line-6",
    ),
    pytest.param(
        "clef",
        (
            _before(
                "m3s2n1",
                _attributes('<clef number="2"><sign>F</sign></clef>'),
            ),
        ),
        id="clef-without-line",
    ),
    pytest.param(
        "clef",
        (
            _before(
                "m3s1n1",
                _attributes(
                    '<clef number="1"><sign>G</sign><line>2</line>'
                    "<clef-octave-change>2</clef-octave-change></clef>"
                ),
            ),
        ),
        id="clef-two-octaves",
    ),
    pytest.param(
        "clef_restated",
        (
            _before(
                "m5s1n1",
                _attributes(
                    '<clef number="1"><sign>G</sign><line>2</line></clef>'
                ),
            ),
        ),
        id="clef-restated",
    ),
    pytest.param(
        "voices",
        (_before("m3s2n1", "".join(_grace(voice) for voice in (1, 2, 3, 4))),),
        id="five-voices",
    ),
    pytest.param(
        "lyric_harmony_cue",
        (
            _before(
                "m1s1n1",
                "<harmony><root><root-step>C</root-step></root>"
                "<kind>major</kind></harmony>",
            ),
        ),
        id="harmony",
    ),
    pytest.param(
        "lyric_harmony_cue",
        (_inside("m1s1n3", "<lyric><text>la</text></lyric>"),),
        id="lyric",
    ),
    pytest.param(
        "lyric_harmony_cue",
        (
            _edit(
                "m2s1n1",
                "<type>eighth</type>",
                '<type size="cue">eighth</type>',
            ),
        ),
        id="cue-size",
    ),
    pytest.param(
        "unsupported_element",
        ((_FINAL_BARLINE, f"{_FINAL_BARLINE}<segno/>"),),
        id="barline-segno",
    ),
    pytest.param(
        "unsupported_element",
        (_edit("m2s1n1", "<type>eighth</type>", "<type>breve</type>"),),
        id="breve",
    ),
    pytest.param(
        "ending_number",
        (
            (
                _FINAL_BARLINE,
                f'{_FINAL_BARLINE}<ending number="1, 2" type="stop"/>',
            ),
        ),
        id="two-numbers",
    ),
    pytest.param(
        "signature_position",
        (
            _before(
                "m6s1n2",
                _attributes(
                    "<time><beats>3</beats><beat-type>4</beat-type></time>"
                ),
            ),
        ),
        id="time-after-note",
    ),
    pytest.param(
        "time_signature",
        (
            _before(
                "m6s1n1",
                _attributes(
                    "<time><beats>3+2</beats><beat-type>8</beat-type></time>"
                ),
            ),
        ),
        id="composite-beats",
    ),
    pytest.param(
        "time_signature",
        (_before("m6s1n1", _attributes("<time><senza-misura/></time>")),),
        id="senza-misura",
    ),
    pytest.param(
        "alter_accidental",
        (
            _edit(
                "m4s1n5", "<step>A</step>", "<step>A</step><alter>0.5</alter>"
            ),
        ),
        id="half-alter",
    ),
    pytest.param(
        "alter_accidental",
        (_inside("m1s1n3", "<accidental>quarter-sharp</accidental>"),),
        id="quarter-sharp",
    ),
    pytest.param(
        "barline",
        ((_FINAL_BARLINE, "<bar-style>heavy-light</bar-style>"),),
        id="heavy-light-without-repeat",
    ),
    pytest.param(
        "chord",
        (_edit("m2s2n3", "<step>G</step>", "<step>E</step>"),),
        id="repeated-pitch",
    ),
    pytest.param(
        "chord",
        (_edit("m2s2n3", "<staff>2</staff>", "<staff>1</staff>"),),
        id="chord-staff",
    ),
    pytest.param(
        "tremolo",
        (
            _inside(
                "m7s1n1",
                "<notations><ornaments>"
                '<tremolo type="unmeasured">3</tremolo>'
                "</ornaments></notations>",
            ),
        ),
        id="unmeasured",
    ),
    pytest.param(
        "tremolo",
        (
            _inside(
                "m7s1n1",
                "<notations><ornaments><tremolo>0</tremolo></ornaments></notations>",
            ),
        ),
        id="no-strokes",
    ),
    pytest.param(
        "fingering",
        (_inside("m1s1n3", _fingers("1", "3")),),
        id="two-without-trill",
    ),
    pytest.param(
        "fingering",
        (_inside("m1s1n3", _fingers("2", "2", trill=True)),),
        id="same-two-on-trill",
    ),
    pytest.param(
        "fingering",
        (_inside("m1s1n3", _fingers("6")),),
        id="digit-6",
    ),
    pytest.param(
        "fingering",
        (_inside("m1s1n3", _fingers("2", attributes=' substitution="yes"')),),
        id="substitution",
    ),
    pytest.param(
        "bracket_dashes",
        (
            _before(
                "m1s1n1", _direction('<bracket type="start" line-end="none"/>')
            ),
        ),
        id="bracket",
    ),
    pytest.param(
        "bracket_dashes",
        (_before("m1s1n1", _direction('<dashes type="start"/>')),),
        id="dashes-alone",
    ),
    pytest.param(
        "bracket_dashes",
        (
            _before(
                "m1s1n1",
                _direction('<dashes type="start"/>', "<words>cresc.</words>"),
            ),
        ),
        id="words-after-dashes",
    ),
    pytest.param(
        "bracket_dashes",
        (
            _before("m1s1n1", _direction("<words>cresc.</words>")),
            _before("m1s1n2", _direction('<dashes type="start"/>')),
        ),
        id="words-at-another-onset",
    ),
    pytest.param(
        "metronome",
        (
            _before(
                "m1s1n1",
                _direction(
                    "<metronome><beat-unit>quarter</beat-unit>"
                    "<beat-unit>half</beat-unit></metronome>"
                ),
            ),
        ),
        id="metric-modulation",
    ),
    pytest.param(
        "metronome",
        (
            _before(
                "m1s1n1",
                _direction(
                    "<metronome><beat-unit>eighth</beat-unit><beat-unit-dot/>"
                    "<per-minute>60</per-minute></metronome>"
                ),
            ),
        ),
        id="dotted-eighth",
    ),
    pytest.param(
        "metronome",
        (
            _before(
                "m1s1n1",
                _direction(
                    "<metronome><beat-unit>quarter</beat-unit>"
                    "<per-minute>c. 60</per-minute></metronome>"
                ),
            ),
        ),
        id="per-minute-text",
    ),
    pytest.param(
        "metronome",
        (
            _before(
                "m1s1n1",
                _direction(
                    "<metronome><beat-unit>quarter</beat-unit>"
                    "<per-minute>52.5</per-minute></metronome>"
                ),
            ),
        ),
        id="per-minute-decimal",
    ),
    pytest.param(
        "beam_tuplet_span",
        (_edit("m2s1n2", '<beam number="1">end</beam>', ""),),
        id="open-beam",
    ),
    pytest.param(
        "beam_tuplet_span",
        (('<tuplet type="stop"/>', ""),),
        id="open-tuplet",
    ),
    pytest.param(
        "allowlist",
        (
            _inside(
                "m1s1n3", '<notations><glissando type="start"/></notations>'
            ),
        ),
        id="glissando",
    ),
    pytest.param(
        "allowlist",
        (_before("m1s1n1", _direction('<wedge type="continue"/>')),),
        id="wedge-continue",
    ),
    pytest.param(
        "allowlist",
        ((_FINAL_BARLINE, f"{_FINAL_BARLINE}<fermata/>"),),
        id="barline-fermata",
    ),
    pytest.param(
        "allowlist",
        (
            _before(
                "m1s1n1",
                _direction(
                    '<metronome parentheses="yes">'
                    "<beat-unit>quarter</beat-unit>"
                    "<per-minute>120</per-minute></metronome>"
                ),
            ),
        ),
        id="metronome-parentheses",
    ),
    pytest.param(
        "allowlist",
        (
            _before(
                "m1s1n1",
                _direction('<wedge type="crescendo" niente="yes"/>'),
            ),
        ),
        id="wedge-niente",
    ),
]


@pytest.mark.parametrize(("reason", "edits"), FAILING_CASES)
def test_screen_fails_on_its_first_failing_reason(
    reason: str,
    edits: tuple[tuple[str, str], ...],
) -> None:
    """Each check excludes a small edit of the base fixture by its reason."""
    result = screen(_variant(*edits))

    assert result.reason == reason
    assert not result.passed
    assert result.detail


def test_screen_counts_the_first_reason_in_protocol_order() -> None:
    """A fault in an earlier measure loses to an earlier reason."""
    text = _variant(
        _inside("m1s1n3", _fingers("6")),
        (_FINAL_BARLINE, "<bar-style>heavy-light</bar-style>"),
    )

    assert screen(text) == ScreenResult(
        "barline",
        "measure 7: bar-style heavy-light",
    )


@pytest.mark.parametrize(
    "text",
    [
        pytest.param("<score-partwise>", id="malformed"),
        pytest.param(
            '<!DOCTYPE score-partwise [<!ENTITY e SYSTEM "file:///nowhere">]>'
            "<score-partwise>&e;</score-partwise>",
            id="external-entity",
        ),
    ],
)
def test_unreadable_document_fails_the_first_reason(text: str) -> None:
    """A document that cannot be parsed, or needs an entity, fails closed."""
    assert screen(text) == ScreenResult("parts_staves", "unreadable")
    assert allowlist_violations(text) == ("unreadable",)


def test_base_fixture_passes_with_no_allowlist_violation() -> None:
    """The base calibration fixture is inside the screen."""
    assert screen(BASE.encode()) == ScreenResult(None, "")
    assert allowlist_violations(BASE) == ()


def test_single_staff_fixture_fails_staves_inside_the_allowlist() -> None:
    """The single-staff fixture has one staff but only supported marks."""
    assert screen(SINGLE).reason == "parts_staves"
    assert allowlist_violations(SINGLE) == ()


@pytest.mark.parametrize(
    "edits",
    [
        pytest.param(
            (_inside("m1s1n3", _fingers("1", "3", trill=True)),),
            id="two-fingerings-on-trill",
        ),
        pytest.param(
            (
                _before(
                    "m1s1n1",
                    _direction("<words>cresc.</words>")
                    + _direction('<dashes type="start"/>'),
                ),
                _before("m1s1n4", _direction('<dashes type="stop"/>')),
            ),
            id="dashes-after-words-direction",
        ),
        pytest.param(
            (
                _before(
                    "m7s1n1",
                    '<barline location="left">'
                    "<bar-style>heavy-light</bar-style>"
                    '<repeat direction="forward"/></barline>',
                ),
            ),
            id="forward-repeat",
        ),
        pytest.param(
            (
                _before(
                    "m1s1n1", _direction('<pedal type="start" line="yes"/>')
                ),
                _before(
                    "m1s1n3", _direction('<pedal type="change" line="yes"/>')
                ),
                _before(
                    "m1s1n4", _direction('<pedal type="stop" line="yes"/>')
                ),
            ),
            id="pedal-line",
        ),
        pytest.param(
            (
                _before(
                    "m1s1n3",
                    _direction('<pedal type="change" line="no" sign="yes"/>'),
                ),
            ),
            id="pedal-sign-change",
        ),
        pytest.param(
            (
                _before(
                    "m1s1n1",
                    _direction(
                        "<dynamics><other-dynamics>pf</other-dynamics>"
                        "</dynamics>"
                    ),
                ),
            ),
            id="other-dynamics-pf",
        ),
        pytest.param(
            (
                _edit(
                    "m4s1n5",
                    "<step>A</step>",
                    "<step>A</step><alter>-1.0</alter>",
                ),
            ),
            id="integral-decimal-alter",
        ),
    ],
)
def test_screen_passes_supported_variants(
    edits: tuple[tuple[str, str], ...],
) -> None:
    """Supported variants of checked constructs pass the screen."""
    text = _variant(*edits)

    assert screen(text) == ScreenResult(None, "")
    assert allowlist_violations(text) == ()


@pytest.mark.parametrize(
    ("xml", "expected"),
    [
        pytest.param(
            '<notations><glissando type="start"/>'
            "<articulations><spiccato/></articulations></notations>",
            ("notations/glissando", "articulations/spiccato"),
            id="every-violation",
        ),
        pytest.param(
            "<notations><ornaments><shake/></ornaments>"
            "<technical><up-bow/></technical></notations>",
            ("ornaments/shake", "technical/up-bow"),
            id="ornament-and-technical",
        ),
        pytest.param(
            '<notations><ornaments><wavy-line type="start"/></ornaments>'
            '<fermata type="upright">square</fermata></notations>',
            ("fermata square", "wavy-line start without trill-mark"),
            id="wavy-line-and-fermata-shape",
        ),
        pytest.param(
            '<notations><ornaments><turn slash="yes"/>'
            '<mordent long="yes"/></ornaments></notations>',
            ("turn@slash=yes", "mordent@long=yes"),
            id="turn-and-mordent-variants",
        ),
        pytest.param(
            "<notations><dynamics><f/></dynamics></notations>",
            ("notations/dynamics",),
            id="dynamics-on-a-note",
        ),
    ],
)
def test_allowlist_reports_note_variants(
    xml: str,
    expected: tuple[str, ...],
) -> None:
    """Every unsupported note mark is reported, container by container."""
    assert allowlist_violations(_variant(_inside("m1s1n3", xml))) == expected


@pytest.mark.parametrize(
    ("kind", "expected"),
    [
        pytest.param(
            '<pedal type="sostenuto"/>', "pedal@type=sostenuto", id="sost"
        ),
        pytest.param(
            '<pedal type="change" sign="no"/>', "pedal@sign=no", id="signless"
        ),
        pytest.param(
            '<octave-shift type="up" size="22"/>',
            "octave-shift@size=22",
            id="ottava-22",
        ),
        pytest.param(
            "<dynamics><other-dynamics>sfffz</other-dynamics></dynamics>",
            "other-dynamics 'sfffz'",
            id="other-dynamics",
        ),
        pytest.param(
            "<dynamics><p/><f/></dynamics>", "dynamics with 2 marks", id="pf"
        ),
        pytest.param(
            '<dashes type="continue"/>', "dashes@type=continue", id="dashes"
        ),
        pytest.param(
            "<eyeglasses/>", "direction-type/eyeglasses", id="eyeglasses"
        ),
    ],
)
def test_allowlist_reports_direction_variants(kind: str, expected: str) -> None:
    """Direction types outside the format's variants are reported."""
    text = _variant(_before("m1s1n1", _direction(kind)))

    assert allowlist_violations(text) == (expected,)


def _calibration() -> ModuleType:
    """Load the comparison tests, which build the calibration pairs.

    The tests directory is not a package and pytest imports test modules by
    path, so the module is loaded by path under a name of its own.
    """
    name = "_screen_calibration_test_comparison"
    module = sys.modules.get(name)
    if module is None:
        spec = importlib.util.spec_from_file_location(
            name,
            TESTS / "test_comparison.py",
        )
        assert spec is not None
        assert spec.loader is not None
        module = importlib.util.module_from_spec(spec)
        sys.modules[name] = module
        spec.loader.exec_module(module)
    return module


_PAIR_TABLES = (
    "VALUE_PAIRS",
    "CATEGORY_PAIRS",
    "CHECK_PAIRS",
    "NO_DIFFERENCE_PAIRS",
    "WORDS_PAIRS",
    "LAYOUT_PAIRS",
    "GRACE_ON_STAFF_2_PAIRS",
    "MEASURE_PAIRS",
)


def _calibration_texts() -> dict[str, str]:
    """Return every MusicXML text of every calibration pair table."""
    calibration = _calibration()
    texts: dict[str, str] = {}
    for table in _PAIR_TABLES:
        for name, pair in getattr(calibration, table).items():
            values = (
                dataclasses.astuple(pair)
                if dataclasses.is_dataclass(pair)
                else pair
            )
            documents = [
                value
                for value in values
                if isinstance(value, str) and "<score-partwise" in value
            ]
            assert len(documents) == 2, (table, name)  # noqa: PLR2004
            for index, document in enumerate(documents):
                texts[f"{table} {name} {index}"] = document
    return texts


def test_calibration_pairs_are_inside_the_allowlist() -> None:
    """Review the allowlist against every calibration Y and T(Y) text."""
    texts = _calibration_texts()

    violations = {
        label: found
        for label, text in texts.items()
        if (found := allowlist_violations(text))
    }

    assert texts
    assert violations == {}


def test_beams_repeated_on_chord_notes_count_once() -> None:
    """A chord whose every note carries the beam has one beam span."""
    texts = _calibration_texts()
    arpeggio = texts["VALUE_PAIRS arpeggio-member 0"]

    start = arpeggio.index('<note id="m2s1n1b">')
    member = arpeggio[start : arpeggio.index("</note>", start)]
    assert "<chord/>" in member
    assert '<beam number="1">begin</beam>' in member
    assert screen(arpeggio) == ScreenResult(None, "")
