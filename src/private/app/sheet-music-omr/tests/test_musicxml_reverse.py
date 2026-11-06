"""Scenario tests for the reverse converter from MusicXML.

Every score and MusicXML fragment here is self-authored for this project and
transcribes no existing score. Round trips check that the reverse converter
inverts the forward converter on canonical text; the fragments follow the
shape of MuseScore 4 exports.
"""

from __future__ import annotations

import io
import zipfile
from pathlib import Path

import pytest

from sheet_music_omr.cli import main
from sheet_music_omr.lexical import decode_source
from sheet_music_omr.model import AssembledScore
from sheet_music_omr.musicxml_forward import to_musicxml
from sheet_music_omr.musicxml_reverse import (
    MusicXmlError,
    from_musicxml,
    from_musicxml_text,
)
from sheet_music_omr.parser import parse_document
from sheet_music_omr.writer import write_document

FIXTURES = Path(__file__).resolve().parent / "fixtures"
HEADER = (
    "format: text-score/2\nkey: 0\ntime: 2/4\nclefs: G2 F4\ncarry: octave\n"
)


def canonical(source: str) -> str:
    """Return the canonical text of an assembled score."""
    document = parse_document(decode_source(source.encode("utf-8")))
    assert isinstance(document, AssembledScore)
    return write_document(document)


def round_trip(source: str) -> str:
    """Convert a score to MusicXML and back to canonical text."""
    document = parse_document(decode_source(source.encode("utf-8")))
    assert isinstance(document, AssembledScore)
    return from_musicxml_text(to_musicxml(document))


def refusal(data: bytes | str) -> MusicXmlError:
    """Return the error that refuses ``data``."""
    with pytest.raises(MusicXmlError) as caught:
        from_musicxml(data)
    return caught.value


@pytest.mark.parametrize(
    "body",
    [
        pytest.param(
            "m 1\n1.1: E5/4[up] F5/4[up]\n1.2: x/4 A4/4[down]\n"
            "2.1: C3/8[beam1=start] D3/8[beam1=end] E3/8[xs1] r/8\n",
            id="voices-and-cross-staff",
        ),
        pytest.param(
            "m 1 len=1/4\n1.1: G4/4\n2.1: G3/4\n"
            "m 2 key=-2 time=2/2 time-symbol=cut\n"
            "1.1: B4/2 B4/2\n2.1: C3/2 <clef G2> C4/2\n"
            "m 3\n1.1: B4/1\n2.1: C4/1\n",
            id="pickup-and-changes",
        ),
        pytest.param(
            "m 1\n1.1: F#4/8 F4/8 F5/8 Bn4/8\n2.1: B(b)2/4 F3+(A3)/4\n",
            id="accidentals",
        ),
        pytest.param(
            "m 1\n"
            "1.1: G4/4[rel=slur@r1:start,rel=slur@r2:start]"
            " C5+E5{tie@r3:start}/4[rel=slur@r1:end,rel=slur@r2:end]\n"
            "2.1: C3/2\n"
            "m 2 right=repeat ending=1:single\n"
            "1.1: C5+E5{tie@r3:end}/4 D5/4\n2.1: C3/2\n"
            "m 3 right=final ending=2:single\n"
            "1.1: E5{tie-in}/4 C5/4\n2.1: C3/2\n",
            id="ties-slurs-and-endings",
        ),
        pytest.param(
            "m 1\n1.1: 3:2{C5/4 D5/4 3:2{E5/8 F5/8 G5/8}}\n2.1: C3/2\n",
            id="nested-tuplets",
        ),
        pytest.param(
            "m 1\n"
            "1.1: C5/8.[beam1=start] D5/16[beam1=end,beam2=hook-backward]"
            " E5/16[beam1=start,beam2=start] x/16[beam1=continue,beam2=end]"
            " G5/8[beam1=end]\n2.1: C3/2\n",
            id="beams-and-beamed-spacer",
        ),
        pytest.param(
            "m 1\n"
            "1.1: A4f13/4[stacc_,orn=trill,"
            "rel=ornament@r1:start:extended-trill]"
            " B4f2/4[ferm_,rel=ornament@r1:end:extended-trill]\n"
            "2.1: C3/2[rel=tremolo@r2:two-start:3]"
            " E3/2[rel=tremolo@r2:two-end:3]\n"
            "m 2\n1.1: C5+E5/2[ten^,ferm,arp,tremolo=single:2]\n2.1: D3/2\n",
            id="marks-ornaments-tremolos-fingering",
        ),
        pytest.param(
            "m 1\n1.1: E4+G4/2[rel=arpeggio@r1:member]\n"
            "2.1: C3+G3/2[rel=arpeggio@r1:member]\n",
            id="arpeggio-across-staves",
        ),
        pytest.param(
            "m 1\n1.1: E4+G4/2[arp]\n2.1: C3+G3/2[arp]\n",
            id="separate-arpeggios-at-one-onset",
        ),
        pytest.param(
            "m 1\n"
            '1.1: <^f> <dim(>[rel=hairpin@r1:start:dim] <tempo "Lento" q.=60>'
            ' C5/4 <"dolce"(>[rel=dashes@r3:start]'
            " <hairpin)>[rel=hairpin@r1:end]"
            " D5/4\n"
            "2.1: <8va(>[rel=ottava@r2:start:8va] <p> C4/4"
            " <8va)>[rel=ottava@r2:end:8va] <pedal-sign down>"
            " <pedal-sign up> C3/4\n"
            'm 2\n1.1: <dashes)>[rel=dashes@r3:end] <rehearsal "A"> E5/2\n'
            "2.1: C3/4 D3/4\n",
            id="directions",
        ),
        pytest.param(
            "m 1\n1.1: E5/2\n"
            "2.1: <ped(>[rel=pedal@r1:line-start] C3/4"
            " <ped)>[rel=pedal@r1:line-release] D3/4\n",
            id="pedal-line",
        ),
        pytest.param(
            "m 1\n1.1: C5/4 D5/8[grace/] <mf> E5/4\n"
            '2.1: <"espr."> <tempo "Adagio"> C3/2\n'
            "m 2 time=3/4 right=final\n1.1: mrest[ferm]\n2.1: G2/2. \n",
            id="graces-words-and-measure-rest",
        ),
        pytest.param(
            "m 1\n1.1: C5/2[rel=tremolo@r1:two-start:2]"
            " E5/2[rel=tremolo@r1:two-end:2]\n"
            "2.1: <15mb(>[rel=ottava@r2:start:15mb] C2/4"
            " <15mb)>[rel=ottava@r2:end:15mb] C3/4\n",
            id="tremolo-pair-and-quindicesima",
        ),
    ],
)
def test_round_trip(body: str) -> None:
    """Return the canonical text of every score that the forward side wrote."""
    source = HEADER + body
    assert round_trip(source) == canonical(source)


def test_round_trip_of_the_study_with_headings_first() -> None:
    """Round-trip the multi-category study once its headings lead the score.

    Credits carry no measure position, so a heading before a later block comes
    back before the first measure.
    """
    source = (FIXTURES / "musicxml-study.txt").read_text(encoding="utf-8")
    later = 'heading: "Second part"\n'
    first = 'heading: "for piano"\n'
    moved = source.replace(later, "").replace(first, first + later)
    assert round_trip(source) == canonical(moved)


def test_round_trip_of_the_assembled_fixture_in_transcription_order() -> None:
    """Round-trip the assembled fixture once rewritten in transcription order.

    MusicXML does not record the carry mode, so ``octave`` is preferred when it
    yields the same pitches, and directions follow grace notes and the ASCII
    order of the transcription rules.
    """
    source = (FIXTURES / "assembled-score.txt").read_text(encoding="utf-8")
    expected = canonical(source)
    for old, new in (
        ("carry: letter", "carry: octave"),
        ('heading: "Coda"\n', ""),
        ('heading: "Tiny Canon"\n', 'heading: "Tiny Canon"\nheading: "Coda"\n'),
        ('<rehearsal "A"> <f>', '<f> <rehearsal "A">'),
        (
            "<ped)>[rel=pedal@r4:line-release] C4/4[grace]",
            "C4/4[grace] <ped)>[rel=pedal@r4:line-release]",
        ),
    ):
        assert old in expected
        expected = expected.replace(old, new)
    assert round_trip(source) == expected


def test_letter_carry_is_chosen_when_octave_carry_misreads_pitches() -> None:
    """Keep letter carry when the printed accidentals only work under it."""
    source = (
        HEADER.replace("octave", "letter") + "m 1\n1.1: F#4/4 F5/4\n2.1: C3/2\n"
    )
    assert round_trip(source) == canonical(source)


def score(
    measures: str, *, part_list: str | None = None, extra: str = ""
) -> str:
    """Wrap measures in a one-part MuseScore-style document."""
    parts = part_list or (
        '<score-part id="P1"><part-name>Piano</part-name></score-part>'
    )
    return (
        '<?xml version="1.0" encoding="UTF-8" standalone="no"?>\n'
        '<!DOCTYPE score-partwise PUBLIC "-//Recordare//DTD MusicXML 4.0'
        ' Partwise//EN" "http://www.musicxml.org/dtds/partwise.dtd">\n'
        '<score-partwise version="4.0">'
        f"{extra}<part-list>{parts}</part-list>"
        f'<part id="P1">{measures}</part></score-partwise>'
    )


ATTRIBUTES = (
    "<attributes><divisions>2</divisions><key><fifths>0</fifths></key>"
    "<time><beats>2</beats><beat-type>4</beat-type></time>"
    "<staves>2</staves>"
    '<clef number="1"><sign>G</sign><line>2</line></clef>'
    '<clef number="2"><sign>F</sign><line>4</line></clef></attributes>'
)


def note(  # noqa: PLR0913
    pitch: str,
    duration: int,
    kind: str,
    voice: int,
    staff: int,
    *,
    chord: bool = False,
    tie: str | None = None,
    after_type: str = "",
    notations: str = "",
) -> str:
    """Return a MuseScore-style note element; ``pitch`` is ``r`` for a rest.

    ``tie`` is the type of both the ``tie`` and the ``tied`` element.
    """
    ties = ""
    if tie is not None:
        ties = f'<tie type="{tie}"/>'
        notations = f'<notations><tied type="{tie}"/></notations>{notations}'
    head = (
        "<rest/>"
        if pitch == "r"
        else f"<pitch><step>{pitch[0]}</step><octave>{pitch[1]}</octave>"
        "</pitch>"
    )
    return (
        f'<note default-x="10">{"<chord/>" if chord else ""}{head}'
        f"<duration>{duration}</duration>{ties}<voice>{voice}</voice>"
        f"<type>{kind}</type>{after_type}<staff>{staff}</staff>"
        f"{notations}</note>"
    )


def test_musescore_style_measures() -> None:
    """Read backup voices, chords, credits, ties and an ignored system break."""
    up, down = "<stem>up</stem>", "<stem>down</stem>"
    measure = (
        '<measure number="1" width="300"><print><system-layout/></print>'
        f"{ATTRIBUTES}"
        + note("G4", 2, "quarter", 1, 1, after_type=up)
        + note("C5", 2, "quarter", 1, 1, after_type=up, tie="start")
        + note("E5", 2, "quarter", 1, 1, chord=True, after_type=up, tie="start")
        + "<backup><duration>4</duration></backup>"
        "<forward><duration>2</duration><voice>2</voice>"
        "<staff>1</staff></forward>"
        + note("E4", 2, "quarter", 2, 1, after_type=down)
        + "<backup><duration>4</duration></backup>"
        + note("C3", 4, "half", 5, 2)
        + '</measure><measure number="2"><print new-system="yes"/>'
        + note("C5", 2, "quarter", 1, 1, tie="stop")
        + note("E5", 2, "quarter", 1, 1, chord=True, tie="stop")
        + note("r", 2, "quarter", 1, 1)
        + "<backup><duration>4</duration></backup>"
        + note("C3", 4, "half", 5, 2)
        + '<barline location="right"><bar-style>light-heavy</bar-style>'
        "</barline></measure>"
    )
    header = (
        "<work><work-title>Piece</work-title></work>"
        '<identification><creator type="composer">Someone</creator>'
        "</identification>"
        '<credit page="1"><credit-type>subtitle</credit-type>'
        '<credit-words default-x="100" default-y="1500">Op. 2</credit-words>'
        "</credit>"
        '<credit page="1"><credit-type>title</credit-type>'
        '<credit-words default-x="100" default-y="1600">Piece</credit-words>'
        "</credit>"
        '<credit page="1"><credit-type>page number</credit-type>'
        '<credit-words default-x="9" default-y="9">1</credit-words></credit>'
    )
    expected = (
        'format: text-score/2\ntitle: "Piece"\ncomposer: "Someone"\n'
        "key: 0\ntime: 2/4\nclefs: G2 F4\ncarry: octave\n"
        'heading: "Piece"\nheading: "Op. 2"\n'
        "m 1\n1.1: G4/4[up] C5{tie@r1:start}+E5{tie@r2:start}/4[up]\n"
        "1.2: x/4 E4/4[down]\n2.1: C3/2\n"
        "m 2 right=final\n"
        "1.1: C5{tie@r1:end}+E5{tie@r2:end}/4 r/4\n2.1: C3/2\n"
    )
    assert from_musicxml_text(score(measure, extra=header)) == canonical(
        expected
    )


def test_time_modification_without_tuplet_numbers() -> None:
    """Derive a tuplet ratio from the time modification of its notes."""
    modification = (
        "<time-modification><actual-notes>3</actual-notes>"
        "<normal-notes>2</normal-notes></time-modification>"
    )
    start = '<notations><tuplet type="start"/></notations>'
    stop = '<notations><tuplet type="stop"/></notations>'
    measure = (
        '<measure number="1">'
        + ATTRIBUTES.replace(">2</divisions>", ">3</divisions>")
        + note("C5", 3, "quarter", 1, 1)
        + note(
            "D5", 1, "eighth", 1, 1, after_type=modification, notations=start
        )
        + note("E5", 1, "eighth", 1, 1, after_type=modification)
        + note("F5", 1, "eighth", 1, 1, after_type=modification, notations=stop)
        + "<backup><duration>6</duration></backup>"
        + note("C3", 6, "half", 5, 2)
        + "</measure>"
    )
    expected = HEADER + "m 1\n1.1: C5/4 3:2{D5/8 E5/8 F5/8}\n2.1: C3/2\n"
    assert from_musicxml_text(score(measure)) == canonical(expected)
    wrong = measure.replace(
        "<duration>1</duration>", "<duration>2</duration>", 1
    )
    assert refusal(score(wrong)).diagnostics[0].code == "grammar/duration"


def test_compressed_file() -> None:
    """Read the root file that the container of an ``.mxl`` names."""
    source = HEADER + "m 1\n1.1: C5/2\n2.1: C3/2\n"
    document = parse_document(decode_source(source.encode("utf-8")))
    assert isinstance(document, AssembledScore)
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, "w") as archive:
        archive.writestr("mimetype", "application/vnd.recordare.musicxml")
        archive.writestr(
            "META-INF/container.xml",
            '<container><rootfiles><rootfile full-path="score/a.musicxml"/>'
            "</rootfiles></container>",
        )
        archive.writestr("score/a.musicxml", to_musicxml(document))
    assert from_musicxml_text(buffer.getvalue()) == canonical(source)


def test_two_parts_are_refused() -> None:
    """Refuse a score with more than one part."""
    parts = (
        '<score-part id="P1"><part-name>A</part-name></score-part>'
        '<score-part id="P2"><part-name>B</part-name></score-part>'
    )
    error = refusal(
        score(f'<measure number="1">{ATTRIBUTES}</measure>', part_list=parts)
    )
    assert error.diagnostics[0].code == "unsupported/part_count"


def test_one_staff_is_refused() -> None:
    """Refuse a part that is not a grand staff."""
    measure = (
        '<measure number="1"><attributes><divisions>1</divisions>'
        "<time><beats>2</beats><beat-type>4</beat-type></time>"
        "<clef><sign>G</sign><line>2</line></clef></attributes>"
        + note("C5", 2, "half", 1, 1)
        + "</measure>"
    )
    error = refusal(score(measure))
    assert error.diagnostics[0].code == "unsupported/staff_count"


@pytest.mark.parametrize(
    ("extra", "rule"),
    [
        ("<lyric><text>la</text></lyric>", "unsupported/lyric"),
        (
            '<notations><glissando type="start"/></notations>',
            "unsupported/glissando",
        ),
        (
            "<notations><articulations><doit/></articulations></notations>",
            "unsupported/doit",
        ),
    ],
)
def test_unsupported_content_is_refused(extra: str, rule: str) -> None:
    """Refuse note content that the format cannot represent."""
    measure = (
        f'<measure number="3">{ATTRIBUTES}'
        + note("C5", 4, "half", 1, 1, notations=extra)
        + "<backup><duration>4</duration></backup>"
        + note("C3", 4, "half", 5, 2)
        + "</measure>"
    )
    diagnostic = refusal(score(measure)).diagnostics[0]
    assert diagnostic.code == rule
    assert (diagnostic.measure, diagnostic.staff) == ("3", 1)


def test_chord_symbol_is_refused() -> None:
    """Refuse a chord symbol, which the format does not encode."""
    measure = (
        f'<measure number="1">{ATTRIBUTES}'
        "<harmony><root><root-step>C</root-step></root>"
        "<kind>major</kind></harmony>"
        + note("C5", 4, "half", 1, 1)
        + "<backup><duration>4</duration></backup>"
        + note("C3", 4, "half", 5, 2)
        + "</measure>"
    )
    assert refusal(score(measure)).diagnostics[0].code == "unsupported/harmony"


@pytest.mark.parametrize(
    ("data", "rule"),
    [
        ("<score-partwise><part>", "lexical/xml"),
        (
            '<!DOCTYPE s [<!ENTITY a "aaaa">]><score-partwise/>',
            "lexical/dtd",
        ),
        (b"PK\x03\x04broken", "lexical/mxl"),
        (
            score(
                '<measure number="1"/>',
                extra="<work><work-title>&eacute;</work-title></work>",
            ),
            "lexical/xml",
        ),
    ],
)
def test_unreadable_input_is_refused(data: str | bytes, rule: str) -> None:
    """Refuse malformed XML, entity declarations and broken archives."""
    assert refusal(data).diagnostics[0].code == rule


def test_corrupt_compressed_member_is_refused() -> None:
    """Refuse an ``.mxl`` whose deflated root file does not inflate."""
    name = "score.musicxml"
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, "w", zipfile.ZIP_DEFLATED) as archive:
        archive.writestr(
            "META-INF/container.xml",
            f'<container><rootfiles><rootfile full-path="{name}"/>'
            "</rootfiles></container>",
        )
        archive.writestr(name, score('<measure number="1"/>'))
    data = bytearray(buffer.getvalue())
    start = data.index(name.encode()) + len(name)
    data[start : start + 16] = b"\xff" * 16
    assert refusal(bytes(data)).diagnostics[0].code == "lexical/mxl"


def credit(attributes: str, content: str) -> str:
    """Return a title credit."""
    return (
        f"<credit {attributes}><credit-type>title</credit-type>{content}"
        "</credit>"
    )


HIDDEN_BEAMED_REST = (
    '<note print-object="no"><rest/><duration>4</duration><voice>1</voice>'
    '<staff>1</staff><beam number="1">begin</beam></note>'
)


@pytest.mark.parametrize(
    ("staff_one", "extra", "rule"),
    [
        (HIDDEN_BEAMED_REST, "", "unsupported/spacer"),
        (
            note("C5", 4, "half", 1, 1),
            credit('page="1"', "<credit-symbol>segno</credit-symbol>"),
            "unsupported/credit",
        ),
        (
            note("C5", 4, "half", 1, 1),
            credit(
                'page="x"',
                '<credit-words default-x="1" default-y="1">T</credit-words>',
            ),
            "grammar/credit",
        ),
    ],
)
def test_childless_and_malformed_elements_are_refused(
    staff_one: str, extra: str, rule: str
) -> None:
    """Refuse a beamed spacer, a symbol credit and a non-numeric position."""
    measure = (
        f'<measure number="1">{ATTRIBUTES}{staff_one}'
        "<backup><duration>4</duration></backup>"
        + note("C3", 4, "half", 5, 2)
        + "</measure>"
    )
    assert refusal(score(measure, extra=extra)).diagnostics[0].code == rule


def test_cli_converts_and_refuses(
    tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    """Write canonical text, or diagnostics and no output on failure."""
    source = HEADER + "m 1\n1.1: C5/2\n2.1: C3/2\n"
    document = parse_document(decode_source(source.encode("utf-8")))
    assert isinstance(document, AssembledScore)
    good = tmp_path / "good.musicxml"
    good.write_bytes(to_musicxml(document))
    out = tmp_path / "out.txt"
    assert main(["from-musicxml", str(good), "-o", str(out)]) == 0
    assert out.read_text(encoding="utf-8") == canonical(source)
    bad = tmp_path / "bad.musicxml"
    bad.write_text("<score-partwise>", encoding="utf-8")
    missing = tmp_path / "missing.txt"
    assert main(["from-musicxml", str(bad), "-o", str(missing)]) == 1
    assert not missing.exists()
    assert "lexical/xml" in capsys.readouterr().err
