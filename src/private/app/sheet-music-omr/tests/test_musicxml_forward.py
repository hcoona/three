"""Scenario tests for the forward converter to MusicXML 4.0.

Every score here is self-authored for this project and transcribes no existing
score. The tests parse the MusicXML output and check its musical structure
rather than its exact bytes.
"""

from __future__ import annotations

import xml.etree.ElementTree as ET
from collections import defaultdict
from pathlib import Path

import pytest

from sheet_music_omr.cli import main
from sheet_music_omr.diagnostics import RuleClass, TextScoreError
from sheet_music_omr.lexical import decode_source
from sheet_music_omr.model import AssembledScore
from sheet_music_omr.musicxml_forward import (
    ReviewRequiredError,
    to_musicxml,
    to_musicxml_text,
)
from sheet_music_omr.parser import parse_document
from sheet_music_omr.semantics import validate_document

FIXTURES = Path(__file__).resolve().parent / "fixtures"
HEADER = (
    "format: text-score/2\nkey: 0\ntime: 2/4\nclefs: G2 F4\ncarry: octave\n"
)
ALTERS = {"-2": "bb", "-1": "b", "0": "", "1": "#", "2": "##"}

LARGE = (FIXTURES / "musicxml-study.txt").read_bytes()


def xml(data: bytes) -> ET.Element:
    """Parse MusicXML that this package wrote."""
    return ET.fromstring(data)  # noqa: S314


def one(element: ET.Element, path: str) -> ET.Element:
    """Return the first element at ``path``, which shall exist."""
    found = element.find(path)
    assert found is not None, path
    return found


def text(element: ET.Element, path: str, default: str | None = None) -> str:
    """Return the text at ``path``, which shall exist."""
    found = element.findtext(path, default)
    assert found is not None, path
    return found


def parse_bytes(data: bytes) -> AssembledScore:
    """Parse the bytes of an assembled score."""
    document = parse_document(decode_source(data))
    assert isinstance(document, AssembledScore)
    return document


def parse(source: str) -> AssembledScore:
    """Parse the text of an assembled score."""
    return parse_bytes(source.encode("utf-8"))


def convert(body: str, header: str = HEADER) -> ET.Element:
    """Convert a score and return the root of the parsed MusicXML."""
    return xml(to_musicxml(parse(header + body)))


def measures(root: ET.Element) -> list[ET.Element]:
    """Return the measures of the only part."""
    return one(root, "part").findall("measure")


def pitch_name(note: ET.Element) -> str:
    """Return a note's sounding pitch as text, or ``r`` for a rest."""
    if note.find("rest") is not None:
        return "R" if one(note, "rest").get("measure") == "yes" else "r"
    alter = ALTERS[text(note, "pitch/alter", "0")]
    return f"{text(note, 'pitch/step')}{alter}{text(note, 'pitch/octave')}"


def voices(measure: ET.Element) -> dict[str, list[tuple[int, str, int, str]]]:
    """Follow the time cursor of a measure and group its events by voice.

    Each entry is ``(onset, pitches, duration, staff)``. Chord notes join the
    entry of their chord, grace notes have duration 0, hidden rests are
    ``x`` and ``forward`` elements are ``>``.
    """
    cursor = 0
    events: dict[str, list[tuple[int, str, int, str]]] = defaultdict(list)
    for element in measure:
        if element.tag == "backup":
            cursor -= int(text(element, "duration"))
            continue
        if element.tag not in {"note", "forward"}:
            continue
        voice = text(element, "voice")
        staff = text(element, "staff")
        if element.tag == "forward":
            duration = int(text(element, "duration"))
            events[voice].append((cursor, ">", duration, staff))
            cursor += duration
            continue
        name = pitch_name(element)
        if element.get("print-object") == "no":
            name = "x"
        if element.find("chord") is not None:
            onset, names, duration, first_staff = events[voice][-1]
            events[voice][-1] = (
                onset,
                f"{names}+{name}",
                duration,
                first_staff,
            )
            continue
        duration = int(text(element, "duration", "0"))
        events[voice].append((cursor, name, duration, staff))
        cursor += duration
    return dict(events)


def assert_voices_fill(measure: ET.Element, length: int) -> None:
    """Check that every voice runs without gaps from 0 to ``length``."""
    for voice, entries in voices(measure).items():
        cursor = 0
        for onset, _, duration, _ in entries:
            assert onset == cursor, (voice, entries)
            cursor += duration
        assert cursor == length, (voice, entries)


def notes_of(measure: ET.Element) -> list[ET.Element]:
    """Return the notes of a measure."""
    return measure.findall("note")


def note_by_pitch(measure: ET.Element, name: str) -> ET.Element:
    """Return the only note of a measure with a sounding pitch."""
    found = [note for note in notes_of(measure) if pitch_name(note) == name]
    assert len(found) == 1, name
    return found[0]


def test_document_header_and_initial_attributes() -> None:
    """Write the declaration, DOCTYPE, metadata, credits and one piano part."""
    document = to_musicxml_text(
        parse(
            'format: text-score/2\ntitle: "Little Piece"\n'
            'composer: "Someone"\nkey: 3\ntime: 3/8\n'
            "clefs: G2-8 F4\ncarry: letter\n"
            'heading: "Little Piece"\nheading: "Op. 1"\n'
            "m 1\n1.1: C4/4.\n2.1: C3/4.\n",
        ),
    )
    assert document.startswith(
        '<?xml version="1.0" encoding="UTF-8" standalone="no"?>\n'
        "<!DOCTYPE score-partwise PUBLIC"
        ' "-//Recordare//DTD MusicXML 4.0 Partwise//EN"'
        ' "http://www.musicxml.org/dtds/partwise.dtd">\n',
    )
    root = xml(document.encode("utf-8"))
    assert root.tag == "score-partwise"
    assert root.get("version") == "4.0"
    assert text(root, "work/work-title") == "Little Piece"
    assert text(root, "identification/creator[@type='composer']") == "Someone"
    headings = [
        (text(credit, "credit-type"), text(credit, "credit-words"))
        for credit in root.findall("credit")
    ]
    assert headings == [("title", "Little Piece"), ("subtitle", "Op. 1")]
    assert [
        part.get("id") for part in root.findall("part-list/score-part")
    ] == [
        "P1",
    ]
    assert len(root.findall("part")) == 1
    attributes = one(measures(root)[0], "attributes")
    assert text(attributes, "key/fifths") == "3"
    assert text(attributes, "time/beats") == "3"
    assert text(attributes, "time/beat-type") == "8"
    assert text(attributes, "staves") == "2"
    clefs = [
        (
            clef.get("number"),
            text(clef, "sign"),
            text(clef, "line"),
            clef.findtext("clef-octave-change"),
        )
        for clef in attributes.findall("clef")
    ]
    assert clefs == [("1", "G", "2", "-1"), ("2", "F", "4", None)]
    divisions = int(text(attributes, "divisions"))
    assert_voices_fill(measures(root)[0], divisions * 3 // 2)


def test_voices_staves_backup_and_forward() -> None:
    """Give each line its own voice and fill each voice with backup/forward."""
    root = convert(
        "m 1\n"
        "1.1: E5/4[up] F5/4[up]\n"
        "1.2: x/4 A4/4[down]\n"
        "2.1: C3/8[beam1=start] D3/8[beam1=end] E3/8[xs1] r/8\n",
    )
    measure = measures(root)[0]
    divisions = int(text(measure, "attributes/divisions"))
    assert_voices_fill(measure, 2 * divisions)
    found = voices(measure)
    quarter, eighth = divisions, divisions // 2
    assert found == {
        "1": [(0, "E5", quarter, "1"), (quarter, "F5", quarter, "1")],
        "2": [(0, ">", quarter, "1"), (quarter, "A4", quarter, "1")],
        "5": [
            (0, "C3", eighth, "2"),
            (eighth, "D3", eighth, "2"),
            (2 * eighth, "E3", eighth, "1"),
            (3 * eighth, "r", eighth, "2"),
        ],
    }
    backups = [int(text(b, "duration")) for b in measure.findall("backup")]
    assert backups == [2 * divisions, 2 * divisions]
    stems = [note.findtext("stem") for note in notes_of(measure)]
    assert stems[:3] == ["up", "up", "down"]


def test_pickup_and_mid_score_changes() -> None:
    """Mark a pickup implicit and emit key, time and clef changes in place."""
    root = convert(
        "m 1 len=1/4\n1.1: G4/4\n2.1: G3/4\n"
        "m 2 key=-2 time=2/2 time-symbol=cut\n"
        "1.1: B4/2 B4/2\n2.1: C3/2 <clef G2> C4/2\n"
        "m 3 key=-2\n1.1: B4/1\n2.1: C4/1\n",
    )
    first, second, third = measures(root)
    assert first.get("implicit") == "yes"
    assert second.get("implicit") is None
    attributes = one(second, "attributes")
    assert text(attributes, "key/fifths") == "-2"
    time = one(attributes, "time")
    assert time.get("symbol") == "cut"
    assert (text(time, "beats"), text(time, "beat-type")) == ("2", "2")
    assert attributes.find("clef") is None
    children = list(second)
    clef_change = next(
        element
        for element in children
        if element.tag == "attributes" and element.find("clef") is not None
    )
    clef = one(clef_change, "clef")
    assert (clef.get("number"), text(clef, "sign")) == ("2", "G")
    following = children[children.index(clef_change) + 1]
    assert pitch_name(following) == "C4"
    # A reprinted key carries no content and is not emitted again.
    assert third.find("attributes") is None
    assert [pitch_name(n) for n in notes_of(second)[:2]] == ["Bb4", "Bb4"]


def test_accidentals_printed_and_sounding() -> None:
    """Emit sounding alterations and only the accidentals that are printed."""
    root = convert(
        "m 1\n1.1: F#4/8 F4/8 F5/8 Bn4/8\n2.1: B(b)2/4 F3+(A3)/4\n",
        header=HEADER.replace("key: 0", "key: -1"),
    )
    measure = measures(root)[0]
    rows = [
        (
            pitch_name(note),
            note.findtext("accidental"),
            one(note, "accidental").get("parentheses")
            if note.find("accidental") is not None
            else None,
            one(note, "notehead").get("parentheses")
            if note.find("notehead") is not None
            else None,
        )
        for note in notes_of(measure)
    ]
    assert rows == [
        ("F#4", "sharp", None, None),
        ("F#4", None, None, None),
        ("F5", None, None, None),
        ("B4", "natural", None, None),
        ("Bb2", "flat", "yes", None),
        ("F3", None, None, None),
        ("A3", None, None, "yes"),
    ]


def test_ties_slurs_and_tie_in_pairing() -> None:
    """Pair tie and slur starts with their stops across barlines."""
    root = convert(
        "m 1\n"
        "1.1: G4/4[rel=slur@r1:start,rel=slur@r2:start]"
        " C5+E5{tie@r3:start}/4[rel=slur@r1:end,rel=slur@r2:end]\n"
        "2.1: C3/2\n"
        "m 2 right=repeat ending=1:single\n"
        "1.1: C5+E5{tie@r3:end}/4 D5/4\n2.1: C3/2\n"
        "m 3 right=final ending=2:single\n"
        "1.1: E5{tie-in}/4 C5/4\n2.1: C3/2\n",
    )
    first, second, third = measures(root)
    g4, c5, e5 = notes_of(first)[:3]
    starts = [
        s.get("number", "") for s in g4.findall("notations/slur[@type='start']")
    ]
    stops = [
        s.get("number", "") for s in c5.findall("notations/slur[@type='stop']")
    ]
    assert sorted(starts) == sorted(stops) == ["1", "2"]
    assert e5.find("notations/slur") is None
    assert [t.get("type") for t in c5.findall("tie")] == []
    assert [t.get("type") for t in e5.findall("tie")] == ["start"]
    assert [t.get("type") for t in e5.findall("notations/tied")] == ["start"]
    c5_end, e5_end = notes_of(second)[:2]
    assert c5_end.find("tie") is None
    assert [t.get("type") for t in e5_end.findall("tie")] == ["stop"]
    assert [t.get("type") for t in e5_end.findall("notations/tied")] == ["stop"]
    tie_in = notes_of(third)[0]
    assert pitch_name(tie_in) == "E5"
    assert [t.get("type") for t in tie_in.findall("tie")] == ["stop"]


def test_nested_tuplets() -> None:
    """Write time modifications and nested tuplet starts and stops."""
    root = convert("m 1\n1.1: 3:2{C5/4 D5/4 3:2{E5/8 F5/8 G5/8}}\n2.1: C3/2\n")
    measure = measures(root)[0]
    divisions = int(text(measure, "attributes/divisions"))
    assert_voices_fill(measure, 2 * divisions)
    notes = notes_of(measure)[:5]
    ratios = [
        (
            text(note, "time-modification/actual-notes"),
            text(note, "time-modification/normal-notes"),
        )
        for note in notes
    ]
    assert ratios == [
        ("3", "2"),
        ("3", "2"),
        ("9", "4"),
        ("9", "4"),
        ("9", "4"),
    ]
    marks = [
        [
            (t.get("type"), t.get("number"))
            for t in note.findall("notations/tuplet")
        ]
        for note in notes
    ]
    assert marks == [
        [("start", "1")],
        [],
        [("start", "2")],
        [],
        [("stop", "2"), ("stop", "1")],
    ]
    inner = one(notes[2], "notations/tuplet")
    assert text(inner, "tuplet-actual/tuplet-number") == "3"
    assert text(inner, "tuplet-normal/tuplet-number") == "2"


def test_beams_hooks_and_beamed_spacer() -> None:
    """Write every beam level and hook, and a beamed spacer as a hidden rest."""
    root = convert(
        "m 1\n"
        "1.1: C5/8.[beam1=start] D5/16[beam1=end,beam2=hook-backward]"
        " E5/16[beam1=start,beam2=start] x/16[beam1=continue,beam2=end]"
        " G5/8[beam1=end]\n2.1: C3/2\n",
    )
    measure = measures(root)[0]
    rows = [
        [(beam.get("number"), beam.text) for beam in note.findall("beam")]
        for note in notes_of(measure)[:5]
    ]
    assert rows == [
        [("1", "begin")],
        [("1", "end"), ("2", "backward hook")],
        [("1", "begin"), ("2", "begin")],
        [("1", "continue"), ("2", "end")],
        [("1", "end")],
    ]
    hidden = notes_of(measure)[3]
    assert hidden.get("print-object") == "no"
    assert hidden.find("rest") is not None
    assert measure.find("forward") is None


def test_marks_ornaments_tremolos_and_fingering() -> None:
    """Write articulations, fermatas, ornaments, tremolos and fingering."""
    root = convert(
        "m 1\n"
        "1.1: A4f13/4[orn=trill,rel=ornament@r1:start:extended-trill,stacc_]"
        " B4f2/4[rel=ornament@r1:end:extended-trill,ferm_]\n"
        "2.1: C3/2[rel=tremolo@r2:two-start:3] E3/2[rel=tremolo@r2:two-end:3]\n"
        "m 2\n1.1: C5+E5/2[tremolo=single:2,arp,ten^,ferm]\n2.1: D3/2\n",
    )
    first, second = measures(root)
    a4, b4 = notes_of(first)[:2]
    assert a4.find("notations/ornaments/trill-mark") is not None
    start = one(a4, "notations/ornaments/wavy-line")
    stop = one(b4, "notations/ornaments/wavy-line")
    assert (start.get("type"), stop.get("type")) == ("start", "stop")
    assert start.get("number") == stop.get("number")
    assert [f.text for f in a4.findall("notations/technical/fingering")] == [
        "1",
        "3",
    ]
    assert (
        one(a4, "notations/articulations/staccato").get("placement") == "below"
    )
    assert one(b4, "notations/fermata").get("type") == "inverted"
    divisions = int(text(first, "attributes/divisions"))
    c3, e3 = notes_of(first)[2:4]
    for note, kind in ((c3, "start"), (e3, "stop")):
        assert text(note, "type") == "half"
        assert int(text(note, "duration")) == divisions
        assert text(note, "time-modification/actual-notes") == "2"
        tremolo = one(note, "notations/ornaments/tremolo")
        assert (tremolo.get("type"), tremolo.text) == (kind, "3")
    assert_voices_fill(first, 2 * divisions)
    c5, e5 = notes_of(second)[:2]
    single = one(c5, "notations/ornaments/tremolo")
    assert (single.get("type"), single.text) == ("single", "2")
    assert one(c5, "notations/articulations/tenuto").get("placement") == "above"
    assert one(c5, "notations/fermata").get("type") == "upright"
    assert e5.find("notations/fermata") is None
    assert c5.find("notations/arpeggiate") is not None
    assert e5.find("notations/arpeggiate") is not None


def test_arpeggio_relation_across_staves() -> None:
    """Give the members of a cross-staff arpeggio one number."""
    root = convert(
        "m 1\n1.1: E4+G4/2[rel=arpeggio@r1:member]\n"
        "2.1: C3+G3/2[rel=arpeggio@r1:member]\n",
    )
    numbers = {
        one(note, "notations/arpeggiate").get("number")
        for note in notes_of(measures(root)[0])
    }
    assert numbers == {"1"}


def test_simultaneous_arpeggios_have_distinct_numbers() -> None:
    """Number separate arpeggios and an arp chord at one onset apart."""
    score = parse(
        HEADER + "m 1\n"
        "1.1: E5+G5/2[rel=arpeggio@r1:member]\n"
        "1.2: C4+E4/2[rel=arpeggio@r1:member]\n"
        "2.1: C3+G3/2[rel=arpeggio@r2:member]\n"
        "2.2: E3+B3/2[rel=arpeggio@r2:member]\n"
        "2.3: C2+G2/2[arp]\n",
    )
    assert not validate_document(score)
    notes = notes_of(measures(xml(to_musicxml(score)))[0])
    groups: dict[str, set[str | None]] = defaultdict(set)
    for note, group in zip(notes, "aaaabbbbcc", strict=True):
        groups[group].add(one(note, "notations/arpeggiate").get("number"))
    assert all(len(numbers) == 1 for numbers in groups.values())
    assert len(set.union(*groups.values())) == len(groups)


def test_directions_and_placement() -> None:
    """Write dynamics, wedges, ottavas, pedals, words, dashes and tempo."""
    root = convert(
        "m 1\n"
        '1.1: <^f> <tempo "Lento" q.=60> <dim(>[rel=hairpin@r1:start:dim]'
        ' C5/4 <hairpin)>[rel=hairpin@r1:end] <"dolce"(>[rel=dashes@r3:start]'
        " D5/4\n"
        "2.1: <8va(>[rel=ottava@r2:start:8va] <p> C4/4"
        " <8va)>[rel=ottava@r2:end:8va] <pedal-sign down> <pedal-sign up>"
        " C3/4\n"
        'm 2\n1.1: <dashes)>[rel=dashes@r3:end] <rehearsal "A"> E5/2\n'
        "2.1: C3/4 D3/4\n",
    )
    first, second = measures(root)
    directions = first.findall("direction")
    dynamics = [
        (
            d.get("placement"),
            text(d, "staff"),
            one(d, "direction-type/dynamics")[0].tag,
        )
        for d in directions
        if d.find("direction-type/dynamics") is not None
    ]
    assert dynamics == [("above", "1", "f"), ("below", "2", "p")]
    tempo = next(d for d in directions if d.find("sound") is not None)
    assert text(tempo, "direction-type/words") == "Lento"
    assert text(tempo, "direction-type/metronome/beat-unit") == "quarter"
    assert tempo.find("direction-type/metronome/beat-unit-dot") is not None
    assert one(tempo, "sound").get("tempo") == "90"
    wedges = [w.attrib for w in first.iter("wedge")]
    assert wedges == [
        {"type": "diminuendo", "number": "1"},
        {"type": "stop", "number": "1"},
    ]
    dashes_start = next(
        d for d in directions if d.find(".//dashes") is not None
    )
    assert text(dashes_start, "direction-type/words") == "dolce"
    stop = one(second, ".//dashes")
    assert stop.get("type") == "stop"
    assert stop.get("number") == one(dashes_start, ".//dashes").get("number")
    shifts = [s.attrib for s in first.iter("octave-shift")]
    assert shifts == [
        {"type": "down", "number": "1", "size": "8"},
        {"type": "stop", "number": "1", "size": "8"},
    ]
    # The ottava sounds an octave above the written C4.
    assert voices(first)["5"][0][1] == "C5"
    assert voices(first)["5"][1][1] == "C3"
    signs = [p.get("type") for p in first.iter("pedal")]
    assert signs == ["stop", "start"]
    assert {p.get("sign") for p in first.iter("pedal")} == {"yes"}
    assert text(second, "direction/direction-type/rehearsal") == "A"


def test_repeats_and_endings() -> None:
    """Write repeat barlines and volta brackets with their numbers."""
    root = convert(
        "m 1 left=repeat\n1.1: C5/2\n2.1: C3/2\n"
        "m 2 right=repeat ending=1:start\n1.1: D5/2\n2.1: C3/2\n"
        "m 3 right=repeat ending=end\n1.1: E5/2\n2.1: C3/2\n"
        "m 4 right=double ending=2:single\n1.1: F5/2\n2.1: C3/2\n"
        "m 5 right=final\n1.1: G5/2\n2.1: C3/2\n",
    )

    def barline(
        measure: ET.Element, location: str
    ) -> tuple[object, ...] | None:
        element = measure.find(f"barline[@location='{location}']")
        if element is None:
            return None
        ending = element.find("ending")
        return (
            element.findtext("bar-style"),
            None
            if ending is None
            else (ending.get("number"), ending.get("type")),
            one(element, "repeat").get("direction")
            if element.find("repeat") is not None
            else None,
        )

    rows = [(barline(m, "left"), barline(m, "right")) for m in measures(root)]
    assert rows == [
        (("heavy-light", None, "forward"), None),
        ((None, ("1", "start"), None), ("light-heavy", None, "backward")),
        (None, ("light-heavy", ("1", "stop"), "backward")),
        ((None, ("2", "start"), None), ("light-light", ("2", "stop"), None)),
        (None, ("light-heavy", None, None)),
    ]
    assert text(measures(root)[1], "barline/ending") == "1."


def test_large_multi_category_score() -> None:
    """Convert a score that combines most categories at once."""
    root = xml(to_musicxml(parse_bytes(LARGE)))
    all_measures = measures(root)
    assert [m.get("number") for m in all_measures] == [
        "1",
        "2",
        "3",
        "4",
        "5",
        "6",
    ]
    divisions = int(text(all_measures[0], "attributes/divisions"))
    lengths = [1, 4, 4, 4, 4, 3]
    for measure, quarters in zip(all_measures, lengths, strict=True):
        assert_voices_fill(measure, quarters * divisions)
    assert [text(c, "credit-words") for c in root.findall("credit")] == [
        "Study in F",
        "for piano",
        "Second part",
    ]
    assert one(all_measures[4], "print").get("new-system") == "yes"
    assert all_measures[0].get("implicit") == "yes"
    assert text(all_measures[5], "attributes/time/beats") == "3"
    first, second, third, fourth, fifth, sixth = all_measures
    assert [f.text for f in first.iter("fingering")] == ["1", "2"]
    assert one(first, "direction/sound").get("tempo") == "96"
    assert pitch_name(note_by_pitch(second, "B4")) == "B4"
    assert text(note_by_pitch(second, "B4"), "accidental") == "natural"
    assert [v[1] for v in voices(second)["5"]] == ["C3+G3", "E3", "F3"]
    assert [v[1] for v in voices(third)["5"]] == ["F1", "A4", "C3"]
    assert [v[1] for v in voices(third)["2"]] == [">", "F4", "E4"]
    assert [f.text for f in notes_of(third)[1].iter("fingering")] == [
        "1",
        "3",
    ]
    assert [p.get("type") for p in fourth.iter("pedal")] == [
        "start",
        "change",
        "stop",
    ]
    assert {p.get("line") for p in fourth.iter("pedal")} == {"yes"}
    assert [
        t.get("type") for t in note_by_pitch(fifth, "G4").findall("tie")
    ] == [
        "stop",
    ]
    assert (
        one(note_by_pitch(fifth, "Bb4"), "notehead").get("parentheses") == "yes"
    )
    cross = note_by_pitch(sixth, "G3")
    assert (text(cross, "staff"), text(cross, "voice")) == ("1", "5")
    grace = note_by_pitch(sixth, "D5")
    assert one(grace, "grace").get("slash") == "yes"
    assert grace.find("duration") is None
    e5 = note_by_pitch(sixth, "E5")
    assert [a.tag for a in one(e5, "notations/articulations")] == [
        "strong-accent",
        "staccatissimo",
    ]
    assert e5.find("notations/ornaments/mordent") is not None
    assert one(sixth, "direction").get("placement") == "above"
    assert one(sixth, "barline/ending").attrib == {
        "number": "2",
        "type": "stop",
    }


def test_conversion_is_deterministic() -> None:
    """Give byte-identical output for the same score."""
    assert to_musicxml(parse_bytes(LARGE)) == to_musicxml(parse_bytes(LARGE))


@pytest.mark.parametrize(
    ("body", "error", "rule_class"),
    [
        pytest.param(
            "m 1\n1.1: C5/4\n2.1: C3/2\n",
            TextScoreError,
            RuleClass.SEMANTIC,
            id="invalid-document",
        ),
        pytest.param(
            "m 1\n1.1: C5/4 <?> D5/4\n2.1: C3/2\n",
            ReviewRequiredError,
            None,
            id="untimed-review-marker",
        ),
        pytest.param(
            'm 1\n1.1: ?(1/2)["C5/2"]\n2.1: C3/2\n',
            ReviewRequiredError,
            None,
            id="timed-review-marker",
        ),
        pytest.param(
            "m 1\n1.1: r/2\n2.1: <ped(>[rel=pedal@r1:line-start] C3/8"
            " <pedal-sign down> D3/8 E3/8 <ped)>[rel=pedal@r1:line-release]"
            " F3/8\n",
            TextScoreError,
            RuleClass.UNSUPPORTED,
            id="pedal-sign-inside-pedal-line",
        ),
        pytest.param(
            "m 1\n1.1: <pedal-sign down> C5/2\n"
            "2.1: <ped(>[rel=pedal@r1:line-start] C3/4"
            " <ped)>[rel=pedal@r1:line-release] E3/4\n"
            "m 2\n1.1: <pedal-sign up> C5/2\n2.1: C3/2\n",
            TextScoreError,
            RuleClass.UNSUPPORTED,
            id="pedal-signs-and-pedal-lines",
        ),
        pytest.param(
            "m 1\n1.1: E4+G4/2[arp,rel=arpeggio@r1:member]\n"
            "2.1: C3+G3/2[rel=arpeggio@r1:member]\n",
            TextScoreError,
            RuleClass.UNSUPPORTED,
            id="arp-flag-and-arpeggio-relation",
        ),
    ],
)
def test_refusals(
    body: str,
    error: type[Exception],
    rule_class: RuleClass | None,
) -> None:
    """Refuse an invalid score or one that needs a person, without output."""
    with pytest.raises(error) as raised:
        to_musicxml(parse(HEADER + body))
    if isinstance(raised.value, TextScoreError):
        assert {d.rule_class for d in raised.value.diagnostics} == {rule_class}
    else:
        assert isinstance(raised.value, ReviewRequiredError)
        assert len(raised.value.markers) == 1


def test_cli_writes_musicxml(tmp_path: Path) -> None:
    """Write the MusicXML of an assembled score to the output file."""
    output = tmp_path / "score.musicxml"
    source = str(FIXTURES / "assembled-score.txt")
    assert main(["to-musicxml", source, "-o", str(output)]) == 0
    root = xml(output.read_bytes())
    assert [m.get("number") for m in measures(root)] == [
        "1",
        "2",
        "3",
        "4",
        "5",
    ]


def test_cli_refuses_review_markers_and_system_documents(
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Fail with review lines or an assembly diagnostic and write nothing."""
    marked = tmp_path / "marked.txt"
    marked.write_bytes((HEADER + "m 1\n1.1: C5/2 <?>\n2.1: C3/2\n").encode())
    assert main(["to-musicxml", str(marked)]) == 1
    captured = capsys.readouterr()
    assert captured.out == ""
    assert "review: untimed review marker <?> in measure 1, 1.1" in captured.err
    system = str(FIXTURES / "system-first.txt")
    assert main(["to-musicxml", system]) == 1
    captured = capsys.readouterr()
    assert captured.out == ""
    assert "assembly/not_assembled" in captured.err
