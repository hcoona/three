"""Calibration scenarios of the round-trip comparison instrument.

Each pair compares a self-authored reference ``Y`` from
``fixtures/comparison`` with a round trip ``T(Y)`` derived from it by exact
text edits, and asserts what the calibration section of
``docs/comparison-protocol.md`` expects of it.
"""

from __future__ import annotations

import re
from collections import Counter
from dataclasses import dataclass
from pathlib import Path
from typing import TYPE_CHECKING

import pytest
from musicdiff.comparison import Comparison

from sheet_music_omr import comparison
from sheet_music_omr.comparison import (
    ContentResult,
    ElementTally,
    InstrumentMismatchError,
    OperationRecord,
    ProtocolDefectError,
    ScoreParseError,
    annotate,
    compare_content,
    compare_layout,
    failure_layout_element_count,
    heading_tally,
    pooled_fidelity,
    voice_order_by_measure,
    voice_order_tally,
)
from sheet_music_omr.musicxml_reverse import from_musicxml_text
from sheet_music_omr.supplementary import (
    CHECKS,
    FileComparison,
    compare_file,
    failure_element_count,
    text_space_consistent,
)

if TYPE_CHECKING:
    from collections.abc import Callable

FIXTURES = Path(__file__).resolve().parent / "fixtures" / "comparison"
BASE = (FIXTURES / "base.musicxml").read_text(encoding="utf-8")
SINGLE = (FIXTURES / "single-staff.musicxml").read_text(encoding="utf-8")
CONTENT_CATEGORY_COUNT = 27
VOICE_POSITION_SWAP_COUNT = 2
VOICE_ORDER_ELEMENT_COUNT = 19
VOICE_ORDER_SINGLE_CHANGE_COUNT = 1
# 52 musicdiff elements, 19 voices, 1 slur, 1 tie stop, 1 beam hook, 2 rest
# beams and 3 events with a time modification.
BASE_FILE_ELEMENT_COUNT = 79


def _note(note_id: str, text: str = BASE) -> str:
    start = text.index(f'      <note id="{note_id}">')
    end = text.index("</note>\n", start) + len("</note>\n")
    return text[start:end]


def _measure(number: str, text: str = BASE) -> str:
    start = text.index(f'    <measure number="{number}">')
    end = text.index("    </measure>", start) + len("    </measure>")
    return text[start:end]


def _edit(
    note_id: str,
    *changes: str,
    base: str = BASE,
) -> tuple[str, str]:
    """Replace each old text of ``changes`` once in one note."""
    note = edited = _note(note_id, base)
    for old, new in zip(changes[::2], changes[1::2], strict=True):
        assert edited.count(old) == 1
        edited = edited.replace(old, new)
    return note, edited


def _before(note_id: str, xml: str) -> tuple[str, str]:
    tag = f'      <note id="{note_id}">\n'
    return tag, xml + tag


def _notations(
    note_id: str,
    inner: str,
    *,
    base: str = BASE,
) -> tuple[str, str]:
    notations = f"        <notations>\n{inner}        </notations>\n"
    return _edit(
        note_id,
        "      </note>\n",
        notations + "      </note>\n",
        base=base,
    )


def _direction(inner: str, staff: int = 1) -> str:
    return (
        "      <direction>\n"
        "        <direction-type>\n"
        f"{inner}"
        "        </direction-type>\n"
        f"        <staff>{staff}</staff>\n"
        "      </direction>\n"
    )


def _attributes(inner: str) -> str:
    return f"      <attributes>\n{inner}      </attributes>\n"


def _credit(
    role: str,
    text: str,
    *,
    page: int = 1,
    x: int | None = 10,
    y: int | None = 100,
) -> str:
    position = (
        f' default-x="{x}" default-y="{y}"'
        if x is not None and y is not None
        else ""
    )
    return (
        f'  <credit page="{page}">\n'
        f"    <credit-type>{role}</credit-type>\n"
        f"    <credit-words{position}>{text}"
        "</credit-words>\n"
        "  </credit>\n"
    )


def _with_credits(*credit_xml: str, base: str = BASE) -> str:
    marker = "  <part-list>\n"
    assert base.count(marker) == 1
    return base.replace(marker, "".join(credit_xml) + marker, 1)


def _variant(*edits: tuple[str, str], base: str = BASE) -> str:
    text = base
    for old, new in edits:
        assert text.count(old) == 1
        text = text.replace(old, new)
    return text


def _move_note_to_second_part_score() -> str:
    note = _note("s1n1", SINGLE)
    second_measure = _measure("1", SINGLE).replace(
        _note("s1n2", SINGLE),
        "",
    )
    second_part = f'  <part id="P2">\n{second_measure}\n  </part>\n'
    second_score_part = (
        '    <score-part id="P2">\n'
        "      <part-name>Melody</part-name>\n"
        "    </score-part>\n"
    )
    return _variant(
        (note, ""),
        ("  </part-list>", second_score_part + "  </part-list>"),
        ("</score-partwise>", second_part + "</score-partwise>"),
        base=SINGLE,
    )


def _single_measure_score(
    parts: tuple[tuple[str, str, str], ...],
) -> str:
    score_parts = "\n".join(
        f'    <score-part id="{part_id}">\n'
        f"      <part-name>{name}</part-name>\n"
        "    </score-part>"
        for part_id, name, _ in parts
    )
    score = "\n".join(
        f'  <part id="{part_id}">\n{measure}\n  </part>'
        for part_id, _, measure in parts
    )
    return (
        '<?xml version="1.0" encoding="UTF-8"?>\n'
        '<score-partwise version="4.0">\n'
        f"  <part-list>\n{score_parts}\n  </part-list>\n"
        f"{score}\n"
        "</score-partwise>\n"
    )


def _single_staff_measure(staff: int, notes: tuple[str, ...]) -> str:
    source_measure = _measure("1")
    attributes_start = source_measure.index("      <attributes>")
    attributes_end = source_measure.index("      </attributes>") + len(
        "      </attributes>\n",
    )
    attributes = source_measure[attributes_start:attributes_end]
    clefs = re.findall(
        r'        <clef number="[12]">.*?        </clef>\n',
        attributes,
        flags=re.DOTALL,
    )
    for index, clef in enumerate(clefs, start=1):
        if index == staff:
            attributes = attributes.replace(
                clef,
                clef.replace(f' number="{staff}"', ""),
            )
        else:
            attributes = attributes.replace(clef, "")
    attributes = attributes.replace("        <staves>2</staves>\n", "")
    staff_notes = "".join(
        note.replace(
            "<staff>2</staff>",
            "<staff>1</staff>",
        )
        for note in notes
    )
    return f'    <measure number="1">\n{attributes}{staff_notes}    </measure>'


@dataclass(frozen=True, slots=True)
class _MusicXmlNote:
    note_id: str
    step: str
    octave: int
    duration: int
    voice: int
    staff: int
    stem: str
    note_type: str
    beam: str | None = None


@dataclass(frozen=True, slots=True)
class _TwoStaffScore:
    beats: int
    beat_type: int
    voice_one_duration: int
    voice_one: tuple[str, ...]
    voice_two: tuple[str, ...]
    voice_two_start: int
    voice_two_end: int


def _musicxml_note(note: _MusicXmlNote) -> str:
    beam_xml = (
        ""
        if note.beam is None
        else f'        <beam number="1">{note.beam}</beam>\n'
    )
    return (
        f'      <note id="{note.note_id}">\n'
        "        <pitch>\n"
        f"          <step>{note.step}</step>\n"
        f"          <octave>{note.octave}</octave>\n"
        "        </pitch>\n"
        f"        <duration>{note.duration}</duration>\n"
        f"        <voice>{note.voice}</voice>\n"
        f"        <type>{note.note_type}</type>\n"
        f"        <stem>{note.stem}</stem>\n"
        f"{beam_xml}"
        f"        <staff>{note.staff}</staff>\n"
        "      </note>\n"
    )


def _two_staff_score(score: _TwoStaffScore) -> str:
    voice_two_start_xml = (
        ""
        if score.voice_two_start == 0
        else (
            f"      <forward><duration>{score.voice_two_start}</duration>"
            "<voice>2</voice><staff>1</staff></forward>\n"
        )
    )
    voice_two_end_xml = (
        ""
        if score.voice_two_end == 0
        else (
            f"      <forward><duration>{score.voice_two_end}</duration>"
            "<voice>2</voice><staff>1</staff></forward>\n"
        )
    )
    return (
        '<?xml version="1.0" encoding="UTF-8"?>\n'
        '<score-partwise version="4.0">\n'
        "  <part-list>\n"
        '    <score-part id="P1"><part-name>Piano</part-name></score-part>\n'
        "  </part-list>\n"
        '  <part id="P1">\n'
        '    <measure number="1">\n'
        "      <attributes>\n"
        "        <divisions>2</divisions>\n"
        "        <time>\n"
        f"          <beats>{score.beats}</beats>\n"
        f"          <beat-type>{score.beat_type}</beat-type>\n"
        "        </time>\n"
        "        <staves>2</staves>\n"
        '        <clef number="1"><sign>G</sign><line>2</line></clef>\n'
        '        <clef number="2"><sign>F</sign><line>4</line></clef>\n'
        "      </attributes>\n"
        f"{''.join(score.voice_one)}"
        f"      <backup><duration>{score.voice_one_duration}</duration>"
        "</backup>\n"
        f"{voice_two_start_xml}"
        f"{''.join(score.voice_two)}"
        f"{voice_two_end_xml}"
        "    </measure>\n"
        "  </part>\n"
        "</score-partwise>\n"
    )


def _four_voice_beam_score(lower_voice_ids: tuple[int, int]) -> str:
    streams = (
        (1, 1, "up", ("C", "D", "E"), 5),
        (2, 1, "down", ("A", "B", "C"), 4),
        (lower_voice_ids[0], 2, "up", ("C", "D", "E"), 3),
        (lower_voice_ids[1], 2, "down", ("A", "B", "C"), 2),
    )
    events: list[str] = []
    for stream_index, (voice, staff, stem, steps, octave) in enumerate(streams):
        if stream_index:
            events.append("      <backup><duration>3</duration></backup>\n")
        for note_index, step in enumerate(steps):
            beam = ("begin", "continue", "end")[note_index]
            events.append(
                _musicxml_note(
                    _MusicXmlNote(
                        f"v{voice}s{staff}n{note_index}",
                        step,
                        octave,
                        1,
                        voice,
                        staff,
                        stem,
                        "eighth",
                        beam,
                    ),
                ),
            )
    return (
        '<?xml version="1.0" encoding="UTF-8"?>\n'
        '<score-partwise version="4.0">\n'
        "  <part-list>\n"
        '    <score-part id="P1"><part-name>Piano</part-name></score-part>\n'
        "  </part-list>\n"
        '  <part id="P1">\n'
        '    <measure number="1">\n'
        "      <attributes>\n"
        "        <divisions>2</divisions>\n"
        "        <time><beats>3</beats><beat-type>8</beat-type></time>\n"
        "        <staves>2</staves>\n"
        '        <clef number="1"><sign>G</sign><line>2</line></clef>\n'
        '        <clef number="2"><sign>F</sign><line>4</line></clef>\n'
        "      </attributes>\n"
        f"{''.join(events)}"
        "    </measure>\n"
        "  </part>\n"
        "</score-partwise>\n"
    )


def _insert_measure(
    text: str,
    source_number: str,
    before_number: str,
    inserted_number: str,
) -> str:
    copied = _measure(source_number, text)
    copied = copied.replace(
        f'number="{source_number}"',
        f'number="{inserted_number}"',
        1,
    ).replace(' id="', ' id="inserted-')
    position = text.index(f'    <measure number="{before_number}">')
    return text[:position] + copied + "\n" + text[position:]


def _delete_measure(text: str, number: str) -> str:
    measure = _measure(number, text)
    return text.replace(measure, "", 1)


def _operation_measure(
    operation: OperationRecord,
    *,
    t_side: bool,
) -> int | None:
    location = operation.location1 if t_side else operation.location2
    return None if location is None else location.measure_index


def _assert_only(result: ContentResult, category: str) -> None:
    assert len(result.differences) == 1
    assert result.counted[0].categories == {category}


def _assert_file(
    result: FileComparison,
    content: dict[str, int] | None = None,
    /,
    **checks: tuple[int, int],
) -> None:
    """Assert a file's counted musicdiff symbols and check differences.

    ``content`` maps each content category to its counted musicdiff symbols,
    and each keyword of ``checks`` names a check with its non-identical and
    extra elements; every other category and check must have none.
    """
    assert result.content.category_counts() == Counter(content or {})
    assert result.check_differences() == checks


_TRIPLET = (
    "<actual-notes>3</actual-notes>\n          <normal-notes>2</normal-notes>"
)
_SEXTUPLET = (
    "<actual-notes>6</actual-notes>\n          <normal-notes>4</normal-notes>"
)
_M5_REPRINTS = _attributes(
    "        <key>\n"
    "          <fifths>0</fifths>\n"
    "        </key>\n"
    "        <time>\n"
    "          <beats>4</beats>\n"
    "          <beat-type>4</beat-type>\n"
    "        </time>\n"
    '        <clef number="1">\n'
    "          <sign>G</sign>\n"
    "          <line>2</line>\n"
    "        </clef>\n"
    '        <clef number="2">\n'
    "          <sign>F</sign>\n"
    "          <line>4</line>\n"
    "        </clef>\n",
)
_M5_CUT_REPRINTS = _M5_REPRINTS.replace(
    "<beats>4</beats>",
    "<beats>2</beats>",
).replace(
    "<beat-type>4</beat-type>",
    "<beat-type>2</beat-type>",
)
_COMMON_TIME = BASE.replace("<time>", '<time symbol="common">', 1)
_CUT_TIME = (
    BASE.replace("<beats>4</beats>", "<beats>2</beats>")
    .replace("<beat-type>4</beat-type>", "<beat-type>2</beat-type>")
    .replace("<time>", '<time symbol="cut">', 1)
)


def _rule_2_pair() -> str:
    """Move the last E5 to a lower canonical voice with a lower raw ID."""
    note = _note("m4s1n4")
    forward = (
        "      <forward>\n"
        "        <duration>{}</duration>\n"
        "        <voice>{}</voice>\n"
        "        <staff>1</staff>\n"
        "      </forward>\n"
    )
    moved = note.replace("<voice>7</voice>", "<voice>2</voice>").replace(
        "<stem>up</stem>",
        "<stem>down</stem>",
    )
    return _variant(
        (note, forward.format(12, 7)),
        (forward.format(24, 2), forward.format(12, 2) + moved),
    )


def _rule_1_pair(base: str = BASE) -> str:
    """Move the voice-2 C5 of measure 5 to staff 2, keeping its voice."""
    note = _note("m5s1n2", base)
    backup = (
        "      <backup>\n        <duration>48</duration>\n      </backup>\n"
    )
    moved = note.replace("<staff>1</staff>", "<staff>2</staff>")
    return _variant(
        (note + backup, ""),
        (
            _note("m5s2n1", base),
            moved + backup + _note("m5s2n1", base),
        ),
        base=base,
    )


def _stem_swap_pair(base: str = BASE) -> str:
    """Swap the stem directions of two distinct-content voices."""
    return _variant(
        _edit(
            "m3s1n1",
            "<stem>up</stem>",
            "<stem>down</stem>",
            base=base,
        ),
        _edit(
            "m3s1n2",
            "<stem>down</stem>",
            "<stem>up</stem>",
            base=base,
        ),
        base=base,
    )


def _mid_voice_stem_change_pair() -> str:
    """Change one event's stem without changing its raw MusicXML voice."""
    return _variant(
        _edit(
            "m4s1n4",
            "<stem>up</stem>",
            "<stem>down</stem>",
        ),
    )


def _stem_position_swap_within_voice_pair() -> tuple[str, str]:
    """Swap two stem-derived positions without changing their histogram."""
    y_text = _variant(
        _edit(
            "m4s1n2",
            "<stem>up</stem>",
            "<stem>down</stem>",
        ),
    )
    t_text = _variant(
        _edit(
            "m4s1n1",
            "<stem>up</stem>",
            "<stem>down</stem>",
            base=y_text,
        ),
        _edit(
            "m4s1n2",
            "<stem>down</stem>",
            "<stem>up</stem>",
            base=y_text,
        ),
        base=y_text,
    )
    return t_text, y_text


def _mixed_stem_beam_pair() -> tuple[str, str]:
    """Change a later beam event's stem while keeping its beam voice."""
    beamed = _variant(
        _edit(
            "m4s1n1",
            "        <staff>1</staff>\n",
            '        <beam number="1">begin</beam>\n        <staff>1</staff>\n',
        ),
        _edit(
            "m4s1n2",
            "        <staff>1</staff>\n",
            '        <beam number="1">end</beam>\n        <staff>1</staff>\n',
        ),
    )
    t_text = _variant(
        _edit(
            "m4s1n2",
            "<stem>up</stem>",
            "<stem>down</stem>",
            base=beamed,
        ),
        base=beamed,
    )
    return t_text, beamed


def _beamed_rest_score() -> str:
    """Add a rest inside a primary beam in an overlapping-voice measure."""
    beamed = _variant(
        _edit(
            "m4s1n1",
            "<duration>12</duration>",
            "<duration>6</duration>",
            "<type>quarter</type>",
            "<type>eighth</type>",
            "        <staff>1</staff>\n",
            '        <beam number="1">begin</beam>\n        <staff>1</staff>\n',
        ),
        _edit(
            "m4s1n2",
            "<duration>12</duration>",
            "<duration>6</duration>",
            "<type>quarter</type>",
            "<type>eighth</type>",
            "        <staff>1</staff>\n",
            '        <beam number="1">end</beam>\n        <staff>1</staff>\n',
        ),
    )
    first_note = _note("m4s1n1", beamed)
    rest = (
        '      <note id="m4s1rest">\n'
        "        <rest/>\n"
        "        <duration>6</duration>\n"
        "        <voice>7</voice>\n"
        "        <type>eighth</type>\n"
        '        <beam number="1">continue</beam>\n'
        "        <staff>1</staff>\n"
        "      </note>\n"
    )
    with_rest = _variant((first_note, first_note + rest), base=beamed)
    last_note = _note("m4s1n4", with_rest)
    forward = (
        "      <forward>\n"
        "        <duration>6</duration>\n"
        "        <voice>7</voice>\n"
        "        <staff>1</staff>\n"
        "      </forward>\n"
    )
    return _variant((last_note, last_note + forward), base=with_rest)


def _distinctive_stem_swap_base() -> str:
    return _variant(
        _notations(
            "m3s1n1",
            "          <ornaments>\n"
            "            <trill-mark/>\n"
            "          </ornaments>\n"
            "          <articulations>\n"
            "            <staccato/>\n"
            "          </articulations>\n"
            "          <fermata/>\n",
        ),
        _notations(
            "m3s1n2",
            "          <ornaments>\n"
            '            <tremolo type="single">3</tremolo>\n'
            "          </ornaments>\n"
            "          <articulations>\n"
            "            <tenuto/>\n"
            "          </articulations>\n",
        ),
    )


def _distinct_pitch_stem_swap_pair() -> tuple[str, str]:
    """Keep distinct-pitch voices content-matched across a stem swap."""
    base = _distinctive_stem_swap_base()
    return _stem_swap_pair(base), base


def _unison_stem_swap_pair() -> tuple[str, str]:
    """Swap unison voice stems while retaining distinctive attached content."""
    base = _distinctive_stem_swap_base()
    base = _variant(
        _edit("m3s1n2", "<step>E</step>", "<step>C</step>", base=base),
        base=base,
    )
    return _stem_swap_pair(base), base


def _highest_voice_pair() -> str:
    """Move the one-note voice 2 of measure 6 to staff 2 as voice 6."""
    note = _note("m6s1n2")
    backup = (
        "      <backup>\n        <duration>48</duration>\n      </backup>\n"
    )
    moved = note.replace("<voice>2</voice>", "<voice>6</voice>").replace(
        "<staff>1</staff>",
        "<staff>2</staff>",
    )
    staff_2 = _note("m6s2n1")
    return _variant(
        (note + backup, ""),
        (staff_2, staff_2 + backup + moved),
    )


def _ornaments(inner: str) -> str:
    return f"          <ornaments>\n{inner}          </ornaments>\n"


def _fingering(digits: str) -> str:
    return (
        "          <technical>\n"
        f"            <fingering>{digits}</fingering>\n"
        "          </technical>\n"
    )


_TRILL_START = _ornaments(
    "            <trill-mark/>\n"
    '            <wavy-line type="start" number="1"/>\n',
)
_TRILL_STOP = _ornaments('            <wavy-line type="stop" number="1"/>\n')


def _line_direction(
    words: str | None,
    dashes: str | None,
    staff: int = 1,
) -> str:
    """Return a direction with optional words and an optional dashes end."""
    types = []
    if words is not None:
        types.append(f"          <words>{words}</words>\n")
    if dashes is not None:
        types.append(f'          <dashes type="{dashes}" number="1"/>\n')
    inner = "".join(
        f"        <direction-type>\n{kind}        </direction-type>\n"
        for kind in types
    )
    return (
        "      <direction>\n"
        f"{inner}"
        f"        <staff>{staff}</staff>\n"
        "      </direction>\n"
    )


def _unison_base(base: str = BASE) -> str:
    """Make measure 3 a C4 whole in voice 1 and a C4 half in voice 2."""
    note = _note("m3s1n2", base)
    half = (
        note.replace("<step>E</step>", "<step>C</step>")
        .replace("<duration>48</duration>", "<duration>24</duration>")
        .replace("<type>whole</type>", "<type>half</type>")
    )
    forward = (
        "      <forward>\n"
        "        <duration>24</duration>\n"
        "        <voice>2</voice>\n"
        "        <staff>1</staff>\n"
        "      </forward>\n"
    )
    return _variant((note, half + forward), base=base)


def _double_grace_base() -> str:
    """Precede the G5 of measure 2 by two grace D5s."""
    grace = _note("m2s1n3")
    second = grace.replace('id="m2s1n3"', 'id="m2s1n3b"')
    return _variant((grace, grace + second))


def _single_tuplet_base() -> str:
    """Replace the staff 2 triplet of measure 4 by a one-event 3:2 group."""
    first = _note("m4s2n1")
    single = (
        first.replace("<duration>8</duration>", "<duration>24</duration>")
        .replace("<type>quarter</type>", "<type>half</type>\n        <dot/>")
        .replace(
            '          <tuplet type="start" bracket="yes"/>\n',
            '          <tuplet type="start" bracket="yes"/>\n'
            '          <tuplet type="stop"/>\n',
        )
    )
    return _variant(
        (first + _note("m4s2n2") + _note("m4s2n3"), single),
    )


def _rest_hook_base() -> str:
    """Give the sixteenth rest of measure 2 a level 2 backward hook."""
    return _variant(
        _edit(
            "m2s2n7",
            '<beam number="2">continue</beam>',
            '<beam number="2">end</beam>',
        ),
        _edit(
            "m2s2n8",
            '<beam number="2">end</beam>',
            '<beam number="2">backward hook</beam>',
        ),
    )


def _volta_score(*, tie_in: bool) -> str:
    """Tie an E5 into a first ending and, if ``tie_in``, a second ending."""
    tie_stop = '        <tie type="stop"/>\n'
    tied_stop = (
        "        <notations>\n"
        '          <tied type="stop"/>\n'
        "        </notations>\n"
    )

    def ending(measure: int, *, tied: bool, last: str) -> str:
        return (
            f'    <measure number="{measure}">\n'
            '      <barline location="left">\n'
            f'        <ending number="{measure - 1}" type="start"/>\n'
            "      </barline>\n"
            f'      <note id="e{measure - 1}">\n'
            "        <pitch>\n"
            "          <step>E</step>\n"
            "          <octave>5</octave>\n"
            "        </pitch>\n"
            "        <duration>16</duration>\n"
            + (tie_stop if tied else "")
            + "        <voice>1</voice>\n"
            "        <type>whole</type>\n"
            + (tied_stop if tied else "")
            + "      </note>\n"
            f"{last}"
            "    </measure>\n"
        )

    source = _edit(
        "s1n2",
        "        <voice>1</voice>\n",
        '        <tie type="start"/>\n        <voice>1</voice>\n',
        "      </note>\n",
        "        <notations>\n"
        '          <tied type="start"/>\n'
        "        </notations>\n"
        "      </note>\n",
        base=SINGLE,
    )
    first = ending(
        2,
        tied=True,
        last='      <barline location="right">\n'
        "        <bar-style>light-heavy</bar-style>\n"
        '        <ending number="1" type="stop"/>\n'
        '        <repeat direction="backward"/>\n'
        "      </barline>\n",
    )
    second = ending(
        3,
        tied=tie_in,
        last='      <barline location="right">\n'
        "        <bar-style>light-heavy</bar-style>\n"
        '        <ending number="2" type="stop"/>\n'
        "      </barline>\n",
    )
    return _variant(
        source,
        (_measure("2", SINGLE) + "\n", first + second),
        base=SINGLE,
    )


_TRILL_MARK = _ornaments("            <trill-mark/>\n")


def _marked(base: str, *marks: tuple[str, str]) -> str:
    """Add each ``(note id, notations content)`` of ``marks`` to ``base``."""
    return _variant(
        *(_notations(note_id, inner, base=base) for note_id, inner in marks),
        base=base,
    )


def _trill_score(stop: str) -> str:
    """Extend a trill from the E5 of measure 1 to ``stop``."""
    return _marked(BASE, ("m1s1n3", _TRILL_START), (stop, _TRILL_STOP))


def _unison_trill_score(start: str) -> str:
    """Trill both unison C4s of measure 3, extending the one at ``start``."""
    other = "m3s1n2" if start == "m3s1n1" else "m3s1n1"
    return _marked(
        _unison_base(),
        (start, _TRILL_START),
        (other, _TRILL_MARK),
        ("m4s1n1", _TRILL_STOP),
    )


def _dashes_score(stop: str, *, on_dolce: bool = False) -> str:
    """Run a dashes line from measure 1 to ``stop``.

    Two ``words`` directions share the onset of the line's start; the line
    continues the ``cresc.`` words, or the ``dolce`` words if ``on_dolce``.
    """
    start = (
        _line_direction("cresc.", None) + _line_direction("dolce", "start")
        if on_dolce
        else _line_direction("cresc.", "start") + _line_direction("dolce", None)
    )
    return _variant(
        _before("m1s1n1", start),
        _before(stop, _line_direction(None, "stop")),
    )


def _line_reprint_pair(*, bold: bool = False) -> tuple[str, str]:
    """Return ``T(Y)`` and a ``Y`` that reprints two continued lines.

    A trill line and a ``cresc.`` dashes line run from measure 4 to measure
    6; ``Y`` reprints the trill mark and the words at the system start in
    measure 5. Bold words of the reprint read as tempo words.
    """
    t_text = _variant(
        _before("m4s1n1", _line_direction("cresc.", "start")),
        _notations("m4s1n4", _TRILL_START),
        _before("m6s1n1", _line_direction(None, "stop")),
        _notations("m6s1n1", _TRILL_STOP),
    )
    y_text = _variant(
        _notations("m5s1n1", _TRILL_MARK, base=t_text),
        _before("m5s1n1", _line_direction("cresc.", None)),
        base=t_text,
    )
    if bold:
        words = "<words>cresc.</words>"
        bold_words = '<words font-weight="bold">cresc.</words>'
        return (
            t_text.replace(words, bold_words),
            y_text.replace(words, bold_words),
        )
    return t_text, y_text


def _parenthesized(note_id: str) -> str:
    """Parenthesize the notehead of one unison C4 of measure 3."""
    base = _unison_base()
    return _variant(
        _edit(
            note_id,
            "</stem>\n",
            '</stem>\n        <notehead parentheses="yes">normal</notehead>\n',
            base=base,
        ),
        base=base,
    )


def _tremolo(kind: str, strokes: int) -> str:
    return _ornaments(
        f'            <tremolo type="{kind}">{strokes}</tremolo>\n',
    )


def _slur_score(start: str, stop: str, base: str = BASE) -> str:
    return _marked(
        base,
        (start, '          <slur number="2" type="start"/>\n'),
        (stop, '          <slur number="2" type="stop"/>\n'),
    )


def _tie_end_score(stop: str) -> str:
    """Tie the C5 whole of measure 3 into one of two C5s of measure 4.

    The C5 quarter ``m4s1n1`` and the C5 half ``m4s1n5`` start together in
    two voices of staff 1.
    """
    base = _variant(
        _edit(
            "m3s1n1",
            "<octave>4</octave>",
            "<octave>5</octave>",
            "        <voice>",
            '        <tie type="start"/>\n        <voice>',
        ),
        _edit(
            "m4s1n5",
            "<step>A</step>",
            "<step>C</step>",
            "<octave>4</octave>",
            "<octave>5</octave>",
        ),
    )
    base = _marked(base, ("m3s1n1", '          <tied type="start"/>\n'))
    return _variant(
        _edit(
            stop,
            "        <voice>",
            '        <tie type="stop"/>\n        <voice>',
            "      </note>\n",
            "        <notations>\n"
            '          <tied type="stop"/>\n'
            "        </notations>\n"
            "      </note>\n",
            base=base,
        ),
        base=base,
    )


def _cross_staff_beam_score() -> str:
    """Beam the first two notes of measure 4, the second on staff 2."""
    beam_group = _variant(
        _edit(
            "m4s1n1",
            "<duration>12</duration>",
            "<duration>6</duration>",
            "<type>quarter</type>",
            "<type>eighth</type>",
            "        <staff>1</staff>\n",
            '        <beam number="1">begin</beam>\n        <staff>1</staff>\n',
        ),
        _edit(
            "m4s1n2",
            "<stem>up</stem>",
            "<stem>down</stem>",
            "<duration>12</duration>",
            "<duration>6</duration>",
            "<type>quarter</type>",
            "<type>eighth</type>",
            "        <staff>1</staff>\n",
            '        <beam number="1">end</beam>\n        <staff>2</staff>\n',
        ),
    )
    return _variant(
        *(
            _edit(
                f"m4s2n{index}",
                "        <staff>2</staff>\n",
                "        <stem>down</stem>\n        <staff>2</staff>\n",
                base=beam_group,
            )
            for index in range(1, 5)
        ),
        base=beam_group,
    )


CATEGORY_PAIRS: dict[str, tuple[str, str, str]] = {
    "pitch": (
        "pitch",
        _variant(_edit("m1s1n2", "<step>D</step>", "<step>F</step>")),
        BASE,
    ),
    "note-to-rest": (
        "rhythm_rest",
        _variant(
            _edit(
                "m2s1n4",
                "        <pitch>\n"
                "          <step>G</step>\n"
                "          <octave>5</octave>\n"
                "        </pitch>\n",
                "        <rest/>\n",
            ),
        ),
        BASE,
    ),
    "unbeamed-eighth-to-sixteenth": (
        "rhythm_rest",
        _variant(
            _edit(
                "m2s1n7",
                "<duration>6</duration>\n"
                "        <voice>1</voice>\n"
                "        <type>eighth</type>",
                "<duration>3</duration>\n"
                "        <voice>1</voice>\n"
                "        <type>16th</type>",
            ),
        ),
        BASE,
    ),
    "unbeamed-eighth-rest-to-sixteenth": (
        "rhythm_rest",
        _variant(
            _edit(
                "m3s2n4",
                "<duration>6</duration>\n"
                "        <voice>5</voice>\n"
                "        <type>eighth</type>",
                "<duration>3</duration>\n"
                "        <voice>5</voice>\n"
                "        <type>16th</type>",
            ),
        ),
        BASE,
    ),
    "beamed-sixteenth-rest-to-eighth": (
        "rhythm_rest",
        _variant(
            _edit(
                "m2s2n8",
                "<duration>3</duration>\n"
                "        <voice>5</voice>\n"
                "        <type>16th</type>\n"
                "        <staff>2</staff>\n"
                '        <beam number="1">end</beam>\n'
                '        <beam number="2">end</beam>\n',
                "<duration>6</duration>\n"
                "        <voice>5</voice>\n"
                "        <type>eighth</type>\n"
                "        <staff>2</staff>\n"
                '        <beam number="1">end</beam>\n',
            ),
        ),
        BASE,
    ),
    "chord_membership": (
        "chord_membership",
        _variant((_note("m2s2n2"), "")),
        BASE,
    ),
    "accidental": (
        "accidental",
        _variant(
            _edit(
                "m4s1n1",
                "<type>quarter</type>\n",
                "<type>quarter</type>\n"
                "        <accidental>natural</accidental>\n",
            ),
        ),
        BASE,
    ),
    "voice": ("voice", _rule_2_pair(), BASE),
    "cross_staff": ("cross_staff", _rule_1_pair(), BASE),
    "clef": (
        "clef",
        _variant(
            _before(
                "m6s2n1",
                _attributes(
                    '        <clef number="2">\n'
                    "          <sign>C</sign>\n"
                    "          <line>4</line>\n"
                    "        </clef>\n",
                ),
            ),
        ),
        BASE,
    ),
    "key_signature": (
        "key_signature",
        _variant(
            _before(
                "m6s1n1",
                _attributes(
                    '        <key number="1">\n'
                    "          <fifths>1</fifths>\n"
                    "        </key>\n",
                ),
            ),
        ),
        BASE,
    ),
    "time_signature": (
        "time_signature",
        _variant(
            _before(
                "m6s1n1",
                _attributes(
                    '        <time number="1">\n'
                    "          <beats>2</beats>\n"
                    "          <beat-type>2</beat-type>\n"
                    "        </time>\n",
                ),
            ),
        ),
        BASE,
    ),
    "beam": (
        "beam",
        _variant(
            _edit(
                "m2s1n2", '        <beam number="2">backward hook</beam>\n', ""
            )
        ),
        BASE,
    ),
    # Only the start note's time-modification changes: musicdiff records the
    # actual number of a group on its start note alone.
    "tuplet": (
        "tuplet",
        _variant(_edit("m4s2n1", _TRIPLET, _SEXTUPLET)),
        BASE,
    ),
    "tie": (
        "tie",
        _variant(
            _edit(
                "m1s2n1",
                '        <tie type="start"/>\n',
                "",
                "        <notations>\n"
                '          <tied type="start"/>\n'
                "        </notations>\n",
                "",
            ),
            _edit(
                "m1s2n2",
                '        <tie type="stop"/>\n',
                "",
                "        <notations>\n"
                '          <tied type="stop"/>\n'
                "        </notations>\n",
                "",
            ),
        ),
        BASE,
    ),
    "slur": (
        "slur",
        _variant(
            _edit(
                "m1s1n1",
                "        <notations>\n"
                '          <slur number="1" type="start"/>\n'
                "        </notations>\n",
                "",
            ),
            _edit(
                "m1s1n2",
                "        <notations>\n"
                '          <slur number="1" type="stop"/>\n'
                "        </notations>\n",
                "",
            ),
        ),
        BASE,
    ),
    "grace": (
        "grace",
        _variant(_edit("m2s1n3", 'slash="yes"', 'slash="no"')),
        BASE,
    ),
    "articulation_fermata": (
        "articulation_fermata",
        _variant(
            _notations(
                "m1s1n3",
                "          <articulations>\n"
                "            <staccato/>\n"
                "          </articulations>\n",
            ),
        ),
        BASE,
    ),
    "fermata": (
        "articulation_fermata",
        _variant(_notations("m1s1n3", "          <fermata/>\n")),
        BASE,
    ),
    "ornament": (
        "ornament",
        _variant(
            _notations(
                "m1s1n3",
                "          <ornaments>\n"
                "            <trill-mark/>\n"
                "          </ornaments>\n",
            ),
        ),
        BASE,
    ),
    "tremolo": (
        "tremolo",
        _variant(
            _notations(
                "m1s1n3",
                "          <ornaments>\n"
                '            <tremolo type="single">3</tremolo>\n'
                "          </ornaments>\n",
            ),
        ),
        BASE,
    ),
    "dynamic": (
        "dynamic",
        _variant(
            _before(
                "m1s1n3",
                _direction(
                    "          <dynamics>\n"
                    "            <f/>\n"
                    "          </dynamics>\n",
                ),
            ),
        ),
        BASE,
    ),
    "hairpin": (
        "hairpin",
        _variant(
            _before(
                "m1s1n3", _direction('          <wedge type="crescendo"/>\n')
            ),
            _before("m1s1n4", _direction('          <wedge type="stop"/>\n')),
        ),
        BASE,
    ),
    "ottava": (
        "ottava",
        _variant(
            _before(
                "m1s1n3",
                _direction('          <octave-shift type="down" size="8"/>\n'),
            ),
            _before(
                "m1s1n4",
                _direction('          <octave-shift type="stop" size="8"/>\n'),
            ),
            _edit("m1s1n3", "<octave>5</octave>", "<octave>6</octave>"),
        ),
        BASE,
    ),
    "pedal": (
        "pedal",
        _variant(
            _before(
                "m1s2n1",
                _direction('          <pedal type="start" line="yes"/>\n', 2),
            ),
            _before(
                "m1s2n2",
                _direction('          <pedal type="stop" line="yes"/>\n', 2),
            ),
        ),
        BASE,
    ),
    "arpeggio": (
        "arpeggio",
        _variant(
            *(
                _notations(note_id, "          <arpeggiate/>\n")
                for note_id in ("m2s2n1", "m2s2n2", "m2s2n3")
            ),
        ),
        BASE,
    ),
    "repeat_ending": (
        "repeat_ending",
        _variant(
            (
                '      </note>\n    </measure>\n    <measure number="2">',
                (
                    "      </note>\n"
                    '      <barline location="right">\n'
                    "        <bar-style>light-light</bar-style>\n"
                    "      </barline>\n"
                    "    </measure>\n"
                    '    <measure number="2">'
                ),
            ),
            base=SINGLE,
        ),
        SINGLE,
    ),
    "text": (
        "text",
        _variant(
            _before("m1s1n3", _direction("          <words>dolce</words>\n"))
        ),
        BASE,
    ),
    "tempo_rehearsal": (
        "tempo_rehearsal",
        _variant(
            _before(
                "m1s1n1",
                _direction(
                    "          <metronome>\n"
                    "            <beat-unit>quarter</beat-unit>\n"
                    "            <per-minute>96</per-minute>\n"
                    "          </metronome>\n",
                ),
            ),
        ),
        BASE,
    ),
    "fingering": (
        "fingering",
        BASE,
        _marked(BASE, ("m1s1n2", _fingering("3"))),
    ),
    "heading": (
        "heading",
        _with_credits(_credit("title", "Title")),
        _with_credits(
            _credit("title", "Title"),
            _credit("composer", "Composer", y=80),
        ),
    ),
}


@pytest.mark.parametrize("pair", CATEGORY_PAIRS, ids=str)
def test_category_pair_gives_one_difference_in_its_category(pair: str) -> None:
    """Report exactly one difference, in the target category."""
    category, t_text, y_text = CATEGORY_PAIRS[pair]
    result = compare_file(t_text, y_text)
    check_elements = sum(map(sum, result.check_differences().values()))

    # A musicdiff operation on a facet that a check owns is disregarded.
    assert len(result.content.counted) + check_elements == 1
    assert all(d.categories == {category} for d in result.content.counted)
    assert result.category_counts() == Counter({category: 1})


def test_category_pairs_cover_every_content_category() -> None:
    """Cover each of the 27 content categories with a category pair."""
    covered = {category for category, _, _ in CATEGORY_PAIRS.values()}

    assert covered == set(comparison.CONTENT_CATEGORIES)
    assert len(covered) == CONTENT_CATEGORY_COUNT


def _words_at(inner: str, note_id: str = "m1s1n1", staff: int = 1) -> str:
    return _variant(_before(note_id, _direction(inner, staff)))


_BOLD = ' font-weight="bold"'
_METRONOME = (
    "          <metronome>\n"
    "            <beat-unit>quarter</beat-unit>\n"
    "            <per-minute>96</per-minute>\n"
    "          </metronome>\n"
)
_TEMPO = _words_at(f"          <words{_BOLD}>Allegro con brio</words>\n")
# A second direction type of the same direction.
_AND_FORTE = (
    "        </direction-type>\n"
    "        <direction-type>\n"
    "          <dynamics><f/></dynamics>\n"
)

# Each pair is (T, Y, expected category counts) of the words reading.
WORDS_PAIRS: dict[str, tuple[str, str, dict[str, int]]] = {
    "tempo-read-as-text": (
        _words_at("          <words>Allegro con brio</words>\n"),
        _TEMPO,
        {"text": 1, "tempo_rehearsal": 1},
    ),
    "tempo-split-into-words": (
        _words_at(
            f"          <words{_BOLD}>Allegro </words>\n"
            f"          <words{_BOLD}>con brio</words>\n",
        ),
        _TEMPO,
        {},
    ),
    "tempo-split-inside-a-word": (
        _words_at(
            f"          <words{_BOLD}>{'Allegro'[:4]}</words>\n"
            f"          <words{_BOLD}>gro con brio</words>\n",
        ),
        _TEMPO,
        {},
    ),
    "tempo-with-dynamics-read-as-text": (
        _words_at("          <words>Allegro con brio</words>\n" + _AND_FORTE),
        _words_at(
            f"          <words{_BOLD}>Allegro con brio</words>\n" + _AND_FORTE,
        ),
        {"text": 1, "tempo_rehearsal": 1},
    ),
    "sound-tempo-words": (
        _words_at("          <words>Allegro con brio</words>\n"),
        _words_at("          <words>Allegro con brio</words>\n").replace(
            "        <staff>1</staff>\n      </direction>",
            '        <staff>1</staff>\n        <sound tempo="132"/>\n'
            "      </direction>",
            1,
        ),
        {"text": 1, "tempo_rehearsal": 1},
    ),
    "metronome-text-dropped": (
        _words_at(_METRONOME),
        _words_at("          <words>Allegro</words>\n" + _METRONOME),
        {"tempo_rehearsal": 1},
    ),
    "repeat-words-dropped": (
        BASE,
        _words_at("          <words>D.C. al Fine</words>\n"),
        {"text": 1},
    ),
    "repeat-words-dropped-on-staff-2": (
        BASE,
        _words_at("          <words>Fine</words>\n", "m1s2n1", 2),
        {"text": 1},
    ),
}


@pytest.mark.parametrize("pair", WORDS_PAIRS, ids=str)
def test_words_reading_pair_gives_its_category_counts(pair: str) -> None:
    """Read tempo words as tempo text and repeat words as text."""
    t_text, y_text, expected = WORDS_PAIRS[pair]
    result = compare_file(t_text, y_text)

    _assert_file(result, expected)


def test_heading_order_swap_is_one_nonidentical_and_one_extra_element() -> None:
    """Compare heading text in positioned reading order, not source order."""
    y_text = _with_credits(
        _credit("title", "Title", y=100),
        _credit("composer", "Composer", y=80),
    )
    t_text = _with_credits(
        _credit("title", "Title", y=80),
        _credit("composer", "Composer", y=100),
    )
    result = compare_file(t_text, y_text)

    assert result.checks["heading"] == ElementTally(2, 1, 1)
    _assert_file(result, heading=(1, 1))


def test_heading_page_move_preserves_reading_order() -> None:
    """Ignore page changes when the ordered heading lines stay the same."""
    y_text = _with_credits(
        _credit("title", "Title", y=100),
        _credit("composer", "Composer", page=2, y=80),
    )
    t_text = _with_credits(
        _credit("title", "Title", y=100),
        _credit("composer", "Composer", y=80),
    )
    result = compare_file(t_text, y_text)

    assert result.checks["heading"] == ElementTally(2, 2, 0)
    _assert_file(result)


def test_heading_order_falls_back_to_document_order_without_positions() -> None:
    """Use source order for a page if any counted credit lacks coordinates."""
    y_text = _with_credits(
        _credit("title", "Title", x=None, y=None),
        _credit("composer", "Composer", y=100),
        _credit("arranger", "Arranger", y=80),
    )
    t_text = _with_credits(
        _credit("title", "Title", x=None, y=None),
        _credit("composer", "Composer", y=80),
        _credit("arranger", "Arranger", y=100),
    )

    assert heading_tally(t_text, y_text) == ElementTally(3, 3, 0)


def test_heading_credit_lines_are_separate_elements() -> None:
    """Count each line of a credit-words element independently."""
    score = _with_credits(_credit("title", "Title\nSubtitle"))

    assert heading_tally(score, score) == ElementTally(2, 2, 0)


def test_heading_tally_excludes_nonheading_credit_types() -> None:
    """Ignore page-number, rights and part-name credits."""
    y_text = _with_credits(
        _credit("page number", "1"),
        _credit("rights", "Copyright"),
        _credit("part name", "Piano"),
    )

    assert heading_tally(BASE, y_text) == ElementTally(0, 0, 0)


@pytest.mark.parametrize(
    "pair",
    [_rule_1_pair, _highest_voice_pair],
    ids=["rule-1", "voice-to-other-staff"],
)
def test_note_moved_to_other_staff_is_one_cross_staff_difference(
    pair: object,
) -> None:
    """Pair a note moved to the other staff as one cross_staff difference."""
    assert callable(pair)
    result = compare_file(pair(), BASE)

    _assert_only(result.content, "cross_staff")
    _assert_file(result, {"cross_staff": 1})
    assert len(result.content.non_identical) == 1
    assert result.tally.extras == 0


def test_cross_staff_check_element_disregards_rule_1_pair() -> None:
    """Leave a rule-1 pair to the cross-staff check that owns it."""
    result = compare_content(
        _rule_1_pair(),
        BASE,
        cross_staff_owned=lambda _pair: True,
    )

    assert result.differences == ()
    assert [pair.rule for pair in result.pairings] == ["cross_staff"]


def test_rule_1_pair_with_fingering_and_slur_end() -> None:
    """Keep note-attached fingering and slur-end marks with a moved note."""
    marked_base = _variant(
        _notations(
            "m5s1n1",
            '          <slur type="start" number="2"/>\n',
        ),
        _notations(
            "m5s1n2",
            "          <technical>\n"
            "            <fingering>3</fingering>\n"
            "          </technical>\n"
            '          <slur type="stop" number="2"/>\n',
        ),
    )
    moved = _rule_1_pair(marked_base)
    result = compare_file(moved, marked_base)

    _assert_only(result.content, "cross_staff")
    _assert_file(result, {"cross_staff": 1})
    assert len(result.content.non_identical) == 1
    assert result.tally.extras == 0
    assert result.checks["fingering"] == ElementTally(1, 1, 0)


def test_fingering_tally_counts_digit_changes() -> None:
    """Compare fingering digits as their own supplementary elements."""
    y_text = _variant(
        _notations(
            "m1s1n2",
            "          <technical>\n"
            "            <fingering>3</fingering>\n"
            "          </technical>\n",
        ),
    )
    t_text = _variant(
        ("<fingering>3</fingering>", "<fingering>4</fingering>"),
        base=y_text,
    )

    assert compare_file(t_text, y_text).checks["fingering"] == ElementTally(
        1, 0, 1
    )


def test_fingering_tally_preserves_chord_member_pitch() -> None:
    """Retain a fingering on its MusicXML chord member."""
    # The chord would hold both ends of the base slur, which no converter
    # reads, so the chord fixture moves the slur end to the next note.
    chord_base = _variant(
        _edit(
            "m1s1n2",
            '      <note id="m1s1n2">\n',
            '      <note id="m1s1n2">\n        <chord/>\n',
            '          <slur number="1" type="stop"/>\n',
            "",
        ),
        _notations("m1s1n3", '          <slur number="1" type="stop"/>\n'),
    )
    y_chord = _variant(
        _notations(
            "m1s1n2",
            "          <technical>\n"
            "            <fingering>3</fingering>\n"
            "          </technical>\n",
            base=chord_base,
        ),
        base=chord_base,
    )
    t_chord = _variant(
        _notations(
            "m1s1n1",
            "          <technical>\n"
            "            <fingering>3</fingering>\n"
            "          </technical>\n",
            base=chord_base,
        ),
        base=chord_base,
    )

    assert compare_file(t_chord, y_chord).checks["fingering"] == ElementTally(
        1, 0, 1
    )


def test_identical_adjacent_rule_1_pair_is_one_cross_staff_difference() -> None:
    """Keep the identical adjacent-measure calibration at one difference."""
    base = _variant(_edit("m5s1n1", "<step>G</step>", "<step>E</step>"))
    result = compare_file(_rule_1_pair(base), base)

    _assert_only(result.content, "cross_staff")
    _assert_file(result, {"cross_staff": 1})
    assert len(result.content.non_identical) == 1
    assert result.tally.extras == 0


@pytest.mark.parametrize(
    ("t_text", "y_text", "operation_name", "ordinal", "t_side"),
    [
        (_insert_measure(BASE, "6", "7", "6-copy"), BASE, "delbar", 7, True),
        (_delete_measure(BASE, "6"), BASE, "insbar", 6, False),
    ],
    ids=["insert-before-later-measures", "delete-before-later-measures"],
)
def test_measure_structure_does_not_realign_later_measures(
    t_text: str,
    y_text: str,
    operation_name: str,
    ordinal: int,
    *,
    t_side: bool,
) -> None:
    """Keep insertions and deletions at their source ordinals."""
    result = compare_content(t_text, y_text)

    assert result.tally.fidelity is not None
    assert result.tally.fidelity < 1
    assert all(
        operation.location1 is None
        or operation.location2 is None
        or operation.location1.measure_index
        == operation.location2.measure_index
        for operation in result.record.measure_operations
    )
    assert any(
        operation.name == operation_name
        and _operation_measure(operation, t_side=t_side) == ordinal
        for operation in result.record.measure_operations
    )


_NOTE = ("pitch", "rhythm_rest")
_BARLINE = ("repeat_ending",)
_VOICE = ("voice",)

type _OrdinalKey = tuple[int, tuple[str, ...], str]

# Measure 6 holds E5 and C5 on staff 1 and C3 on staff 2; measure 7 holds C5
# on staff 1, G3 and C3 on staff 2 and the final barline, one symbol on each
# staff. Each measure ordinal is compared with the same ordinal, and a note
# without a counterpart is one note and rhythm difference. The C5 and C3
# voices that measures 6 and 7 share have other canonical positions in each,
# so they are two non-identical voice elements at the ordinal that compares
# the two measures.
MEASURE_PAIRS: dict[str, tuple[str, str, dict[_OrdinalKey, int]]] = {
    # T(Y) ordinal 7 is the copy of measure 6 and ordinal 8 is measure 7.
    "insert": (
        _insert_measure(BASE, "6", "7", "6-copy"),
        BASE,
        {
            (7, _NOTE, "extra"): 1,
            (7, _NOTE, "non-identical"): 1,
            (7, _BARLINE, "non-identical"): 2,
            (7, _VOICE, "non-identical"): 2,
            (8, _NOTE, "extra"): 3,
            (8, _BARLINE, "extra"): 2,
        },
    ),
    # T(Y) ordinal 6 is measure 7 and ordinal 7 is missing.
    "delete": (
        _delete_measure(BASE, "6"),
        BASE,
        {
            (6, _NOTE, "non-identical"): 1,
            (6, _NOTE, "extra"): 1,
            (6, _BARLINE, "extra"): 2,
            (6, _VOICE, "non-identical"): 2,
            (7, _NOTE, "non-identical"): 3,
            (7, _BARLINE, "non-identical"): 2,
        },
    ),
}


@pytest.mark.parametrize("pair", MEASURE_PAIRS, ids=str)
def test_measure_pair_gives_predeclared_per_ordinal_differences(
    pair: str,
) -> None:
    """Assign each difference of a measure pair to its predeclared ordinal."""
    t_text, y_text, expected = MEASURE_PAIRS[pair]
    result = compare_file(t_text, y_text)
    differences: Counter[_OrdinalKey] = Counter()
    for difference in result.content.counted:
        # A difference spanning two ordinals would pair content across them.
        (index,) = {
            location.measure_index
            for operation in difference.operations
            for location in (operation.location1, operation.location2)
            if location is not None and location.measure_index is not None
        }
        kind = "extra" if difference.extra else "non-identical"
        differences[index + 1, tuple(sorted(difference.categories)), kind] += 1
    voice_order = voice_order_by_measure(annotate(t_text), annotate(y_text))
    for index, tally in voice_order.items():
        if tally.identical != tally.elements:
            differences[index + 1, _VOICE, "non-identical"] += (
                tally.elements - tally.identical
            )

    assert differences == Counter(expected)
    assert result.check_differences().keys() == {"voice_order"}


def test_note_moved_to_other_voice_is_one_non_identical_voice_element() -> None:
    """Pair a note moved to another voice as one voice element."""
    result = compare_file(_rule_2_pair(), BASE)

    _assert_only(result.content, "voice")
    _assert_file(result, {"voice": 1})
    assert len(result.content.non_identical) == 1
    assert result.tally.extras == 0
    assert [pair.rule for pair in result.content.pairings] == ["voice"]


def test_voices_of_equal_content_are_matched_one_to_one() -> None:
    """Leave a dropped voice that duplicates another to the content pass."""
    y_text = _variant(_edit("m3s1n2", "<step>E</step>", "<step>C</step>"))
    backup = (
        "      <backup>\n        <duration>48</duration>\n      </backup>\n"
    )
    t_text = _variant((backup + _note("m3s1n2", y_text), ""), base=y_text)
    result = compare_file(t_text, y_text)

    # Two C4 voices of Y have one partner in T(Y); the other is not a
    # voice-order element, so the loss counts once.
    _assert_file(result, {"pitch": 1, "rhythm_rest": 1})
    assert result.checks["voice_order"] == ElementTally(
        VOICE_ORDER_ELEMENT_COUNT - 1,
        VOICE_ORDER_ELEMENT_COUNT - 1,
        0,
    )
    assert result.tally == ElementTally(
        BASE_FILE_ELEMENT_COUNT - 1,
        BASE_FILE_ELEMENT_COUNT - 2,
        0,
    )


@pytest.mark.parametrize(
    ("t_text", "y_text"),
    [
        (_distinct_pitch_stem_swap_pair()),
        (_unison_stem_swap_pair()),
    ],
    ids=["different-pitches-with-distinct-marks", "unison-with-distinct-marks"],
)
def test_stem_swap_changes_two_canonical_voice_positions(
    t_text: str,
    y_text: str,
) -> None:
    """Count content-preserving voices whose stem-derived positions swap."""
    result = compare_file(t_text, y_text)
    layout = compare_layout(t_text, y_text)

    _assert_file(result, voice_order=(VOICE_POSITION_SWAP_COUNT, 0))
    assert [d.symbols for d in layout.differences] == [("layout",)] * 2
    assert [operation.name for operation in layout.operations] == [
        "editstemdirection",
        "editstemdirection",
    ]


def test_mid_voice_stem_change_uses_each_events_canonical_position() -> None:
    """Assign a changed stem by its event instead of its voice's first stem."""
    t_text = _mid_voice_stem_change_pair()
    result = compare_file(t_text, BASE)
    layout = compare_layout(t_text, BASE)

    _assert_file(result, voice_order=(VOICE_ORDER_SINGLE_CHANGE_COUNT, 0))
    assert result.checks["voice_order"].elements == VOICE_ORDER_ELEMENT_COUNT
    assert [operation.name for operation in layout.operations] == [
        "editstemdirection",
    ]


def test_stem_position_swap_within_voice_is_not_hidden_by_its_histogram() -> (
    None
):
    """Retain event order when comparing a voice's canonical positions."""
    t_text, y_text = _stem_position_swap_within_voice_pair()
    result = compare_file(t_text, y_text)

    _assert_file(result, voice_order=(VOICE_ORDER_SINGLE_CHANGE_COUNT, 0))
    assert result.checks["voice_order"].elements == VOICE_ORDER_ELEMENT_COUNT


def test_mixed_stem_beam_group_uses_its_first_events_position() -> None:
    """Keep mixed-stem beam events in the position of the first event."""
    t_text, y_text = _mixed_stem_beam_pair()
    result = compare_file(t_text, y_text)
    layout = compare_layout(t_text, y_text)

    _assert_file(result)
    assert result.checks["voice_order"].elements == VOICE_ORDER_ELEMENT_COUNT
    assert [operation.name for operation in layout.operations] == [
        "editstemdirection",
    ]


def test_cross_staff_beam_group_is_one_voice_for_its_home_staff() -> None:
    """Keep a cross-staff beam together when another voice is on staff 2."""
    score = _cross_staff_beam_score()

    content = compare_content(score, score)
    layout = compare_layout(score, score)
    tally = voice_order_tally(score, score)

    # Parser event reconstruction does not give a stable contract for the
    # total tally size here, so pin self-identity and absence of extras.
    assert content.counted == ()
    assert layout.operations == ()
    assert tally.elements > 0
    assert tally.identical == tally.elements
    assert tally.extras == 0


def test_unmatched_parts_use_global_only_scoring() -> None:
    """Keep distinct source parts out of ordinary staff comparisons."""
    original = _single_measure_score(
        (("P1", "Piano", _measure("1")),),
    )
    split_staff_one = _single_staff_measure(
        1,
        tuple(_note(f"m1s1n{index}") for index in range(1, 5)),
    )
    changed_staff_two_note = _edit(
        "m1s2n1",
        "<step>C</step>",
        "<step>D</step>",
    )[1]
    split_staff_two = _single_staff_measure(
        2,
        (
            changed_staff_two_note,
            _note("m1s2n2"),
        ),
    )
    split = _single_measure_score(
        (
            ("P1", "Piano upper staff", split_staff_one),
            ("P2", "Piano lower staff", split_staff_two),
        ),
    )

    result = compare_content(split, original)

    assert result.record.measure_operations == ()
    assert result.record.global_operations
    assert result.tally == ElementTally(13, 8, 5)
    assert result.tally.fidelity == 8 / 18
    assert all(
        "cross_staff" not in difference.categories
        for difference in result.differences
    )


def test_unmatched_part_does_not_add_voice_order_reference() -> None:
    """Do not match a source staff to an unmatched part's flattened staff."""
    original = _single_measure_score(
        (("P1", "Piano", _measure("1")),),
    )
    split = _single_measure_score(
        (
            (
                "P1",
                "Piano upper staff",
                _single_staff_measure(
                    1,
                    tuple(_note(f"m1s1n{index}") for index in range(1, 5)),
                ),
            ),
            (
                "P2",
                "Piano lower staff",
                _single_staff_measure(
                    2,
                    (_note("m1s2n1"), _note("m1s2n2")),
                ),
            ),
        ),
    )

    tally = voice_order_tally(split, original)

    assert tally == ElementTally(1, 1, 0)


def test_cross_staff_beam_fragment_in_sole_staff_voice_preserves_identity() -> (
    None
):
    """Keep a beam fragment joined to the rest of its flattened voice."""
    score = _two_staff_score(
        _TwoStaffScore(
            2,
            4,
            4,
            (
                _musicxml_note(
                    _MusicXmlNote(
                        "start", "C", 5, 1, 1, 1, "up", "eighth", "begin"
                    )
                ),
                _musicxml_note(
                    _MusicXmlNote(
                        "stop", "D", 3, 1, 1, 2, "up", "eighth", "end"
                    )
                ),
                _musicxml_note(
                    _MusicXmlNote("after", "E", 5, 2, 1, 1, "up", "quarter")
                ),
            ),
            (),
            0,
            0,
        ),
    )
    changed = _variant(
        _edit(
            "after",
            "<step>E</step>",
            "<step>F</step>",
            base=score,
        ),
        base=score,
    )

    content = compare_content(score, score)
    voice_order = voice_order_tally(score, score)
    changed_voice_order = voice_order_tally(changed, score)

    assert content.differences == ()
    assert content.tally.fidelity == 1.0
    assert voice_order == ElementTally(1, 1, 0)
    assert changed_voice_order == ElementTally(0, 0, 0)


def test_cross_staff_move_pairs_using_source_beamings() -> None:
    """Pair a moved beam member by its source beam relation."""
    score = _two_staff_score(
        _TwoStaffScore(
            3,
            8,
            3,
            (
                _musicxml_note(
                    _MusicXmlNote(
                        "start", "C", 5, 1, 1, 1, "up", "eighth", "begin"
                    )
                ),
                _musicxml_note(
                    _MusicXmlNote(
                        "middle", "D", 5, 1, 1, 1, "up", "eighth", "continue"
                    )
                ),
                _musicxml_note(
                    _MusicXmlNote(
                        "stop", "E", 5, 1, 1, 1, "up", "eighth", "end"
                    )
                ),
            ),
            (),
            0,
            0,
        ),
    )
    moved = _variant(
        _edit(
            "middle",
            "<staff>1</staff>",
            "<staff>2</staff>",
            base=score,
        ),
        base=score,
    )
    ownership_calls: list[bool] = []
    content = compare_content(
        moved,
        score,
        cross_staff_owned=lambda *_args: ownership_calls.append(True) or True,
    )

    assert ownership_calls == [True]
    assert content.category_counts()["pitch"] == 0
    assert content.category_counts()["beam"] == 0
    assert content.category_counts()["rhythm_rest"] == 1

    changed_beam = _variant(
        _edit(
            "middle",
            '<beam number="1">continue</beam>',
            '<beam number="1">forward hook</beam>',
            base=moved,
        ),
        base=moved,
    )
    changed_beam_calls: list[bool] = []
    changed_beam_content = compare_content(
        changed_beam,
        score,
        cross_staff_owned=lambda *_args: (
            changed_beam_calls.append(True) or True
        ),
    )

    assert changed_beam_calls == []
    assert changed_beam_content.category_counts()["pitch"] > 0
    assert changed_beam_content.category_counts()["beam"] > 0


def test_split_staves_keep_their_source_part_in_operation_locations() -> None:
    """Keep music21's staff fragments attached to one MusicXML part."""
    result = compare_content(_rule_1_pair(), BASE)
    locations = [
        location
        for operation in result.record.operations
        for location in (operation.location1, operation.location2)
        if location is not None
    ]

    assert {location.part_index for location in locations} == {0}
    assert {location.staff for location in locations} == {0, 1}


def test_note_moved_to_another_musicxml_part_is_not_cross_staff() -> None:
    """Do not pair matching notes from different source parts."""
    result = compare_content(_move_note_to_second_part_score(), SINGLE)

    assert result.tally.extras > 0
    assert all(
        "cross_staff" not in difference.categories
        for difference in result.differences
    )


def test_beamed_rest_does_not_interrupt_voice_position_assignment() -> None:
    """Retain a rest that musicdiff reconstructs inside a beam group."""
    score = _beamed_rest_score()

    tally = voice_order_tally(score, score)

    assert tally.elements > 0
    assert tally.identical == tally.elements
    assert tally.extras == 0


def test_double_stem_event_cannot_be_assigned_to_one_voice() -> None:
    """Stop when a double stem cannot be represented by one position."""
    double_stem = _variant(
        _edit(
            "m1s1n1",
            "        <staff>1</staff>\n",
            "        <stem>double</stem>\n        <staff>1</staff>\n",
        ),
    )

    with pytest.raises(ProtocolDefectError, match="double-stem event"):
        voice_order_tally(double_stem, double_stem)


def test_unison_stem_swap_pairs_notes_by_distinctive_content() -> None:
    """Keep distinctive note content aligned across a stem swap."""
    t_text, y_text = _unison_stem_swap_pair()

    layout = compare_layout(t_text, y_text)

    assert [operation.name for operation in layout.operations] == [
        "editstemdirection",
        "editstemdirection",
    ]


BASE_LAYOUT_ELEMENT_COUNT = 18
"""The base's 15 explicit stems, bracketed triplet, system break and brace."""


def _stem_direction_pair() -> tuple[str, str]:
    y_text = _variant(
        _edit(
            "m1s1n1",
            "<staff>1</staff>",
            "<stem>up</stem>\n        <staff>1</staff>",
        ),
    )
    t_text = _variant(
        _edit(
            "m1s1n1",
            "<stem>up</stem>",
            "<stem>down</stem>",
            base=y_text,
        ),
        base=y_text,
    )
    return t_text, y_text


def _staccato(placement: str | None) -> str:
    attribute = "" if placement is None else f' placement="{placement}"'
    return _variant(
        _notations(
            "m1s1n3",
            f"          <articulations><staccato{attribute}/>"
            "</articulations>\n",
        ),
    )


def _fermata(shape: str) -> str:
    return _variant(
        _notations("m1s1n3", f'          <fermata type="{shape}"/>\n'),
    )


_UNTYPED_FERMATA = _variant(_notations("m1s1n3", "          <fermata/>\n"))


def _forte(placement: str) -> str:
    direction = _direction("          <dynamics><f/></dynamics>\n").replace(
        "<direction>",
        f'<direction placement="{placement}">',
    )
    return _variant(_before("m1s1n3", direction))


def _rest_at(step: str, octave: int) -> str:
    return _variant(
        _edit(
            "m2s1n6",
            "<rest/>",
            "<rest>\n"
            f"          <display-step>{step}</display-step>\n"
            f"          <display-octave>{octave}</display-octave>\n"
            "        </rest>",
        ),
    )


def _trill(placement: str) -> str:
    return _variant(
        _notations(
            "m1s1n3",
            f'          <ornaments><trill-mark placement="{placement}"/>'
            "</ornaments>\n",
        ),
    )


LAYOUT_PAIRS: dict[str, tuple[str, str]] = {
    "stem-direction": _stem_direction_pair(),
    "placement": (_staccato("below"), _staccato("above")),
    "inverted-fermata": (_fermata("upright"), _fermata("inverted")),
    "untyped-inverted-fermata": (_UNTYPED_FERMATA, _fermata("inverted")),
    "dynamic-placement": (_forte("below"), _forte("above")),
    "rest-position": (_rest_at("D", 5), _rest_at("B", 5)),
    "trill-placement": (_trill("below"), _trill("above")),
}
"""Layout calibration pairs ``(T(Y), Y)``: each differs only in one hint."""

_MEASURE_3_SYSTEM_BREAK = _variant(
    (
        '    <measure number="3">\n',
        '    <measure number="3">\n      <print new-system="yes"/>\n',
    ),
)


def test_single_voice_stem_change_is_layout_only() -> None:
    """Keep stem direction out of canonical position for one voice."""
    t_text, y_text = LAYOUT_PAIRS["stem-direction"]
    content = compare_content(t_text, y_text)
    voice_order = voice_order_tally(t_text, y_text)
    layout = compare_layout(t_text, y_text)

    assert content.counted == ()
    assert content.tally == ElementTally(52, 52, 0)
    assert voice_order == ElementTally(19, 19, 0)
    assert [operation.name for operation in layout.operations] == [
        "editstemdirection",
    ]


@pytest.mark.parametrize(
    ("t_text", "y_text"),
    list(LAYOUT_PAIRS.values()),
    ids=list(LAYOUT_PAIRS),
)
def test_layout_pair_is_one_non_identical_layout_element(
    t_text: str,
    y_text: str,
) -> None:
    """Count a changed layout hint once, as a layout difference only."""
    layout = compare_layout(t_text, y_text)

    assert [d.symbols for d in layout.differences] == [("layout",)]
    assert layout.tally == ElementTally(
        BASE_LAYOUT_ELEMENT_COUNT + 1,
        BASE_LAYOUT_ELEMENT_COUNT,
        0,
    )
    _assert_file(compare_file(t_text, y_text))


@pytest.mark.parametrize(
    ("t_text", "y_text"),
    [
        (_fermata("upright"), _UNTYPED_FERMATA),
        (_UNTYPED_FERMATA, _fermata("upright")),
    ],
    ids=["typed-t", "untyped-t"],
)
def test_untyped_fermata_is_upright(t_text: str, y_text: str) -> None:
    """Read a fermata without a type as upright, as MusicXML defines it."""
    layout = compare_layout(t_text, y_text)

    assert layout.operations == ()
    assert layout.tally == ElementTally(
        BASE_LAYOUT_ELEMENT_COUNT,
        BASE_LAYOUT_ELEMENT_COUNT,
        0,
    )
    _assert_file(compare_file(t_text, y_text))


def test_untyped_fermata_in_a_file_is_upright(tmp_path: Path) -> None:
    """Read a file's fermata without a type as upright."""
    y_path = tmp_path / "y.musicxml"
    y_path.write_text(_UNTYPED_FERMATA, encoding="utf-8")

    assert compare_layout(_fermata("upright"), y_path).operations == ()
    assert compare_layout(_fermata("inverted"), y_path).differences


@pytest.mark.parametrize(
    ("t_text", "y_text", "expected"),
    [
        (
            _variant(_edit("m4s2n1", 'bracket="yes"', 'bracket="no"')),
            BASE,
            ElementTally(
                BASE_LAYOUT_ELEMENT_COUNT,
                BASE_LAYOUT_ELEMENT_COUNT - 1,
                0,
            ),
        ),
        (
            _staccato("above"),
            _staccato(None),
            ElementTally(
                BASE_LAYOUT_ELEMENT_COUNT,
                BASE_LAYOUT_ELEMENT_COUNT,
                1,
            ),
        ),
        (
            _rest_at("D", 5),
            BASE,
            ElementTally(
                BASE_LAYOUT_ELEMENT_COUNT,
                BASE_LAYOUT_ELEMENT_COUNT,
                1,
            ),
        ),
        (
            _MEASURE_3_SYSTEM_BREAK,
            BASE,
            ElementTally(
                BASE_LAYOUT_ELEMENT_COUNT,
                BASE_LAYOUT_ELEMENT_COUNT,
                1,
            ),
        ),
        (
            BASE,
            _MEASURE_3_SYSTEM_BREAK,
            ElementTally(
                BASE_LAYOUT_ELEMENT_COUNT + 1,
                BASE_LAYOUT_ELEMENT_COUNT,
                0,
            ),
        ),
    ],
    ids=[
        "tuplet-bracket",
        "t-only-placement",
        "t-only-rest-position",
        "t-only-system-break",
        "y-only-system-break",
    ],
)
def test_layout_value_of_one_side_is_non_identical_or_extra(
    t_text: str,
    y_text: str,
    expected: ElementTally,
) -> None:
    """Count a lost ``Y`` value as non-identical, an added one as extra."""
    layout = compare_layout(t_text, y_text)

    assert [d.symbols for d in layout.differences] == [("layout",)]
    assert layout.tally == expected
    _assert_file(compare_file(t_text, y_text))


def test_layout_of_deleted_content_is_identical_and_of_inserted_is_extra() -> (
    None
):
    """Leave layout lost with content to the content pass, per the limit."""
    removed = _variant(
        (
            "      <backup>\n"
            "        <duration>48</duration>\n"
            "      </backup>\n" + _note("m3s1n2"),
            "",
        ),
    )

    deleted = compare_layout(removed, BASE)
    inserted = compare_layout(BASE, removed)

    assert deleted.differences == ()
    assert deleted.tally == ElementTally(
        BASE_LAYOUT_ELEMENT_COUNT,
        BASE_LAYOUT_ELEMENT_COUNT,
        0,
    )
    assert inserted.differences == ()
    assert inserted.tally == ElementTally(
        BASE_LAYOUT_ELEMENT_COUNT - 1,
        BASE_LAYOUT_ELEMENT_COUNT - 1,
        1,
    )


def test_voice_coupling_pair_is_one_pitch_difference() -> None:
    """Count C4 and E4 against two C4s as one pitch difference."""
    t_text = _variant(_edit("m3s1n2", "<step>E</step>", "<step>C</step>"))
    result = compare_file(t_text, BASE)

    _assert_only(result.content, "pitch")
    _assert_file(result, {"pitch": 1})


def test_dropped_ordinary_tie_is_one_tie_difference() -> None:
    """Count a dropped ordinary tie as one musicdiff tie difference."""
    _, t_text, y_text = CATEGORY_PAIRS["tie"]
    result = compare_file(t_text, y_text)

    _assert_file(result, {"tie": 1})
    assert [d.categories for d in result.content.counted] == [{"tie"}]
    assert [op.name for op in result.content.record.operations] == ["tieins"]


def test_tie_displayed_only_by_tied_is_not_a_tie_end() -> None:
    """Read tie ends from ``tie``, as music21 does, not from ``tied``."""
    displayed = _variant(
        _edit("m1s2n1", '        <tie type="start"/>\n', ""),
        _edit("m1s2n2", '        <tie type="stop"/>\n', ""),
    )
    result = compare_file(BASE, displayed)

    assert failure_element_count(displayed) == BASE_FILE_ELEMENT_COUNT - 1
    assert result.checks["tie_ends"] == ElementTally(0, 0, 0)
    _assert_file(result, {"tie": 1})


_UNISON = _unison_base()
_GRACES = _double_grace_base()
_SINGLE_TUPLET = _single_tuplet_base()
_REST_HOOK = _rest_hook_base()
_NO_REST_HOOK = _variant(
    _edit(
        "m2s2n8",
        '        <beam number="2">backward hook</beam>\n',
        "",
        base=_REST_HOOK,
    ),
    base=_REST_HOOK,
)
_SLUR = ("slur_endpoints", (1, 0))


def _level_2_beams(base: str, *changes: tuple[str, str, str]) -> str:
    """Replace the level 2 beam state of each ``(note id, old, new)``."""
    return _variant(
        *(
            _edit(
                note_id,
                f'<beam number="2">{old}</beam>',
                f'<beam number="2">{new}</beam>',
                base=base,
            )
            for note_id, old, new in changes
        ),
        base=base,
    )


# The staff 2 sixteenths of measure 2 split into two level 2 groups.
_LEVEL_2_SPLIT = _level_2_beams(
    BASE,
    ("m2s2n6", "continue", "end"),
    ("m2s2n7", "continue", "begin"),
)
# Each group of _LEVEL_2_SPLIT becomes two hooks.
_LEVEL_2_HOOKS = _level_2_beams(
    _LEVEL_2_SPLIT,
    ("m2s2n5", "begin", "forward hook"),
    ("m2s2n6", "end", "backward hook"),
)
# The E3 of the second group has a backward hook, and the rest no level 2.
_HOOK_BACKWARD = _variant(
    _edit("m2s2n7", "begin", "backward hook", base=_LEVEL_2_SPLIT),
    _edit(
        "m2s2n8",
        '        <beam number="2">end</beam>\n',
        "",
        base=_LEVEL_2_SPLIT,
    ),
    base=_LEVEL_2_SPLIT,
)
_HOOK_FORWARD = _variant(
    _edit("m2s2n7", "backward hook", "forward hook", base=_HOOK_BACKWARD),
    base=_HOOK_BACKWARD,
)


def _two_note_tremolo(strokes: int) -> str:
    """Make the E5 and G5 of measure 1 a two-note tremolo of half notes.

    Each note keeps the sounding quarter of its duration, as the forward
    conversion writes a two-note tremolo.
    """
    half = (
        "<type>half</type>\n"
        "        <time-modification>\n"
        "          <actual-notes>2</actual-notes>\n"
        "          <normal-notes>1</normal-notes>\n"
        "        </time-modification>"
    )
    halves = _variant(
        _edit("m1s1n3", "<type>quarter</type>", half),
        _edit("m1s1n4", "<type>quarter</type>", half),
    )
    return _marked(
        halves,
        ("m1s1n3", _tremolo("start", strokes)),
        ("m1s1n4", _tremolo("stop", strokes)),
    )


# Each pair is ``T(Y)``, ``Y`` and the check that scores its one change.
CHECK_PAIRS: dict[str, tuple[str, str, tuple[str, tuple[int, int]]]] = {
    "extended-trill-end": (
        _trill_score("m2s1n1"),
        _trill_score("m1s1n4"),
        ("wavy_line", (1, 1)),
    ),
    "extended-trill-unison-move": (
        _unison_trill_score("m3s1n2"),
        _unison_trill_score("m3s1n1"),
        ("wavy_line", (1, 1)),
    ),
    "dashes-end": (
        _dashes_score("m1s1n4"),
        _dashes_score("m1s1n3"),
        ("dashes", (1, 1)),
    ),
    "dashes-text": (
        _dashes_score("m1s1n3", on_dolce=True),
        _dashes_score("m1s1n3"),
        ("dashes", (1, 1)),
    ),
    "tremolo-single": (
        _marked(BASE, ("m1s1n3", _tremolo("single", 2))),
        _marked(BASE, ("m1s1n3", _tremolo("single", 3))),
        ("tremolo_strokes", (1, 0)),
    ),
    "tremolo-two-note": (
        _two_note_tremolo(2),
        _two_note_tremolo(3),
        ("tremolo_strokes", (1, 0)),
    ),
    "tremolo-unison-swap": (
        _marked(
            _UNISON,
            ("m3s1n1", _tremolo("single", 2)),
            ("m3s1n2", _tremolo("single", 3)),
        ),
        _marked(
            _UNISON,
            ("m3s1n1", _tremolo("single", 3)),
            ("m3s1n2", _tremolo("single", 2)),
        ),
        ("tremolo_strokes", (2, 0)),
    ),
    "hook-value": (_HOOK_FORWARD, _HOOK_BACKWARD, ("beam_hooks", (1, 0))),
    "tuplet-ratio": (
        _variant(
            _edit(
                "m4s2n1",
                "<normal-notes>2</normal-notes>",
                "<normal-notes>1</normal-notes>",
                base=_SINGLE_TUPLET,
            ),
            base=_SINGLE_TUPLET,
        ),
        _SINGLE_TUPLET,
        ("tuplet_ratio", (1, 0)),
    ),
    "fingering-grace-swap": (
        _marked(
            _GRACES,
            ("m2s1n3", _fingering("2")),
            ("m2s1n3b", _fingering("1")),
        ),
        _marked(
            _GRACES,
            ("m2s1n3", _fingering("1")),
            ("m2s1n3b", _fingering("2")),
        ),
        ("fingering", (2, 2)),
    ),
    "fingering-unison-swap": (
        _marked(
            _UNISON,
            ("m3s1n1", _fingering("2")),
            ("m3s1n2", _fingering("1")),
        ),
        _marked(
            _UNISON,
            ("m3s1n1", _fingering("1")),
            ("m3s1n2", _fingering("2")),
        ),
        ("fingering", (2, 2)),
    ),
    "slur-end-to-other-voice": (
        _slur_score("m2s1n7", "m3s1n2"),
        _slur_score("m2s1n7", "m3s1n1"),
        _SLUR,
    ),
    "slur-start-to-unison-voice": (
        _slur_score("m3s1n2", "m4s1n1", _UNISON),
        _slur_score("m3s1n1", "m4s1n1", _UNISON),
        _SLUR,
    ),
    "slur-start-to-second-grace": (
        _slur_score("m2s1n3b", "m2s1n5", _GRACES),
        _slur_score("m2s1n3", "m2s1n5", _GRACES),
        _SLUR,
    ),
    "slur-end-to-other-staff": (
        _slur_score("m5s1n1", "m6s2n1"),
        _slur_score("m5s1n1", "m6s1n1"),
        _SLUR,
    ),
    "slur-end-to-earlier-note": (
        _slur_score("m3s1n1", "m4s1n5"),
        _slur_score("m3s1n1", "m4s1n2"),
        _SLUR,
    ),
    "tie-end": (
        _tie_end_score("m4s1n5"),
        _tie_end_score("m4s1n1"),
        ("tie_ends", (1, 1)),
    ),
    "tie-in-deletion": (
        _volta_score(tie_in=False),
        _volta_score(tie_in=True),
        ("tie_ends", (1, 0)),
    ),
    "tie-in-insertion": (
        _volta_score(tie_in=True),
        _volta_score(tie_in=False),
        ("tie_ends", (0, 1)),
    ),
    "rest-hook-deletion": (_NO_REST_HOOK, _REST_HOOK, ("rest_beams", (1, 0))),
    "rest-hook-insertion": (_REST_HOOK, _NO_REST_HOOK, ("rest_beams", (0, 1))),
    "notehead-parentheses": (
        _parenthesized("m3s1n2"),
        _parenthesized("m3s1n1"),
        ("notehead_parentheses", (1, 1)),
    ),
}


@pytest.mark.parametrize("pair", CHECK_PAIRS, ids=str)
def test_check_pair_gives_only_its_check_elements(pair: str) -> None:
    """Score each supplementary-check pair by its check alone."""
    t_text, y_text, (check, elements) = CHECK_PAIRS[pair]

    _assert_file(compare_file(t_text, y_text), **{check: elements})


def test_beams_of_a_rest_that_musicdiff_scores_are_not_elements() -> None:
    """Leave the beams of a rest that becomes a note to musicdiff."""
    pitch = "<pitch>\n          <step>C</step>\n          <octave>3</octave>\n"
    t_text = _variant(
        _edit("m2s2n8", "<rest/>", pitch + "        </pitch>", base=_REST_HOOK),
        base=_REST_HOOK,
    )
    result = compare_file(t_text, _REST_HOOK)

    assert result.content.category_counts()["rhythm_rest"] == 1
    assert result.check_differences() == {}
    assert result.checks["rest_beams"] == ElementTally(0, 0, 0)


def test_tuplet_ratio_counts_each_event_of_a_changed_group() -> None:
    """Count each event of a three-event group whose ratio changes."""
    t_text = BASE.replace(
        "<normal-notes>2</normal-notes>",
        "<normal-notes>1</normal-notes>",
    )

    _assert_file(compare_file(t_text, BASE), tuplet_ratio=(3, 0))


def test_fingering_digits_of_one_mark_are_separate_elements() -> None:
    """Score a two-finger trill fingering such as ``23`` digit by digit."""
    y_text = _marked(BASE, ("m1s1n2", _fingering("23")))
    t_text = _marked(BASE, ("m1s1n2", _fingering("2")))
    result = compare_file(t_text, y_text)

    assert result.checks["fingering"] == ElementTally(2, 1, 0)
    _assert_file(result, fingering=(1, 0))


@pytest.mark.parametrize(
    ("reprint", "category"),
    [
        (_notations("m5s1n1", _TRILL_MARK), "ornament"),
        (_before("m5s1n1", _line_direction("cresc.", None)), "text"),
    ],
    ids=["trill-mark", "words"],
)
def test_reprint_without_an_open_line_is_one_difference(
    reprint: tuple[str, str],
    category: str,
) -> None:
    """Count a mark or words that continue no line as a difference."""
    result = compare_file(BASE, _variant(reprint))

    _assert_only(result.content, category)
    _assert_file(result, {category: 1})


def test_note_on_its_voices_staff_is_a_cross_staff_check_element() -> None:
    """Score a cross-staff note by its staff, keyed without its voice."""
    score = _cross_staff_beam_score()
    home = _variant(
        _edit("m4s1n2", "<staff>2</staff>", "<staff>1</staff>", base=score),
        base=score,
    )

    # The protocol has no cross-staff check pair; musicdiff also reports the
    # changed beam fragments of each staff, so only the check is pinned.
    assert compare_file(home, score).check_differences()["cross_staff"] == (
        1,
        0,
    )
    assert compare_file(score, home).check_differences()["cross_staff"] == (
        0,
        1,
    )


def _stemmed_measure_1(base: str = BASE) -> str:
    """Stem the events of measure 1 that can share a staff after a move."""
    return _variant(
        _edit(
            "m1s1n3",
            "<staff>1</staff>",
            "<stem>up</stem>\n        <staff>1</staff>",
            base=base,
        ),
        *(
            _edit(
                f"m1s2n{index}",
                "<staff>2</staff>",
                "<stem>down</stem>\n        <staff>2</staff>",
                base=base,
            )
            for index in (1, 2)
        ),
        base=base,
    )


def test_voice_with_one_note_on_another_staff_stays_on_its_staff() -> None:
    """Keep a voice on the staff that holds most of its events."""
    home = _stemmed_measure_1()
    moved = _variant(
        _edit("m1s1n3", "<staff>1</staff>", "<staff>2</staff>", base=home),
        base=home,
    )
    annotated = annotate(moved)

    assert voice_order_by_measure(annotated, annotated)[0] == ElementTally(
        2, 2, 0
    )
    assert failure_element_count(home) == BASE_FILE_ELEMENT_COUNT
    assert failure_element_count(moved) == BASE_FILE_ELEMENT_COUNT + 1
    # The moved E5 makes staff 2 print two voices, so the C3 voice moves to
    # the down-stem position; musicdiff scores the gap the move leaves.
    for t_text, y_text, cross_staff in (
        (home, moved, (1, 0)),
        (moved, home, (0, 1)),
    ):
        result = compare_file(t_text, y_text)
        assert result.checks["voice_order"] == ElementTally(
            VOICE_ORDER_ELEMENT_COUNT,
            VOICE_ORDER_ELEMENT_COUNT - 1,
            0,
        )
        _assert_file(
            result,
            {"rhythm_rest": 1},
            cross_staff=cross_staff,
            voice_order=(1, 0),
        )


def _cross_staff_beam_off_voice_staff(beam_staff: int) -> str:
    """Beam three eighths of measure 4's voice 7, two of them on a staff.

    The voice keeps four events on staff 1, so staff 1 is its staff, while a
    beam with two events on staff 2 belongs to staff 2 in the format.
    """
    second = _note("m4s1n2").replace('id="m4s1n2"', 'id="m4s1x"')
    rest = (
        '      <note id="m4s1r">\n'
        "        <rest/>\n"
        "        <duration>6</duration>\n"
        "        <voice>7</voice>\n"
        "        <type>eighth</type>\n"
        "        <staff>1</staff>\n"
        "      </note>\n"
    )
    base = _variant(
        _before("m4s1n3", second.replace("<step>C</step>", "<step>D</step>")),
        _before("m4s1n4", rest),
    )

    def eighth(note_id: str, beam: str, staff: int) -> tuple[str, str]:
        return _edit(
            note_id,
            "<duration>12</duration>",
            "<duration>6</duration>",
            "<type>quarter</type>",
            "<type>eighth</type>",
            "        <staff>1</staff>\n",
            f'        <beam number="1">{beam}</beam>\n'
            f"        <staff>{staff}</staff>\n",
            base=base,
        )

    beamed = _variant(
        eighth("m4s1n2", "begin", beam_staff),
        eighth("m4s1x", "continue", beam_staff),
        eighth("m4s1n3", "end", 1),
        base=base,
    )
    return _variant(
        *(
            _edit(
                f"m4s2n{index}",
                "        <staff>2</staff>\n",
                "        <stem>down</stem>\n        <staff>2</staff>\n",
                base=beamed,
            )
            for index in range(1, 5)
        ),
        base=beamed,
    )


def test_voice_stays_on_its_staff_when_its_beam_belongs_to_another() -> None:
    """Group a voice by its staff, not by the staff of its beam group."""
    same_staff = _cross_staff_beam_off_voice_staff(1)
    cross_staff = _cross_staff_beam_off_voice_staff(2)
    annotated = annotate(cross_staff)

    assert voice_order_by_measure(annotated, annotated)[3] == ElementTally(
        3, 3, 0
    )
    assert compare_file(cross_staff, cross_staff).checks[
        "cross_staff"
    ] == ElementTally(2, 2, 0)
    assert (
        failure_element_count(cross_staff)
        == failure_element_count(same_staff) + 2
    )
    # Voice 7 stays one voice on staff 1; voice 5 moves to the down-stem
    # position because staff 2 now prints two voices.
    for t_text, y_text, cross_staff_difference in (
        (same_staff, cross_staff, (2, 0)),
        (cross_staff, same_staff, (0, 2)),
    ):
        result = compare_file(t_text, y_text)
        assert result.checks["voice_order"] == ElementTally(
            VOICE_ORDER_ELEMENT_COUNT,
            VOICE_ORDER_ELEMENT_COUNT - 1,
            0,
        )
        assert result.check_differences() == {
            "cross_staff": cross_staff_difference,
            "voice_order": (1, 0),
        }


def test_voice_number_reused_on_two_staves_is_two_voices() -> None:
    """Split one MusicXML voice whose events on two staves overlap."""
    reused = _variant(
        *(
            _edit(f"m1s2n{index}", "<voice>5</voice>", "<voice>1</voice>")
            for index in (1, 2)
        ),
    )
    annotated = annotate(reused)

    assert voice_order_by_measure(annotated, annotated)[0] == ElementTally(
        2, 2, 0
    )
    assert failure_element_count(reused) == BASE_FILE_ELEMENT_COUNT
    result = compare_file(BASE, reused)
    assert result.checks["voice_order"] == ElementTally(
        VOICE_ORDER_ELEMENT_COUNT, VOICE_ORDER_ELEMENT_COUNT, 0
    )
    _assert_file(result)


def _grace(note_id: str, step: str, octave: int, voice: int, staff: int) -> str:
    return (
        f'      <note id="{note_id}">\n'
        '        <grace slash="yes"/>\n'
        "        <pitch>\n"
        f"          <step>{step}</step>\n"
        f"          <octave>{octave}</octave>\n"
        "        </pitch>\n"
        f"        <voice>{voice}</voice>\n"
        "        <type>eighth</type>\n"
        f"        <staff>{staff}</staff>\n"
        "      </note>\n"
    )


def test_reused_voice_number_counts_grace_notes_per_staff() -> None:
    """Count a grace note's position among its own staff's voice only."""
    fingered = _grace("g2", "B", 2, 5, 2).replace(
        "      </note>\n",
        "        <notations>\n"
        "          <technical>\n"
        "            <fingering>2</fingering>\n"
        "          </technical>\n"
        "        </notations>\n"
        "      </note>\n",
    )
    separate = _variant(
        _before("m1s1n1", _grace("g1", "B", 5, 1, 1)),
        _before("m1s2n1", fingered),
    )
    reused = _variant(
        *(
            _edit(
                note_id, "<voice>5</voice>", "<voice>1</voice>", base=separate
            )
            for note_id in ("g2", "m1s2n1", "m1s2n2")
        ),
        base=separate,
    )

    # Two grace notes and one fingering more than ``BASE``.
    assert failure_element_count(separate) == BASE_FILE_ELEMENT_COUNT + 3
    assert failure_element_count(reused) == BASE_FILE_ELEMENT_COUNT + 3
    for t_text, y_text in ((separate, reused), (reused, separate)):
        result = compare_file(t_text, y_text)
        assert result.checks["fingering"] == ElementTally(1, 1, 0)
        assert result.checks["voice_order"] == ElementTally(
            VOICE_ORDER_ELEMENT_COUNT, VOICE_ORDER_ELEMENT_COUNT, 0
        )
        _assert_file(result)


def _grace_on_staff_2_pairs() -> dict[str, tuple[str, str]]:
    """Pairs whose ``Y`` shows a grace note of measure 2 on staff 2.

    The grace notes keep their voice, which has its other events on staff 1.
    """
    grace = _note("m2s1n3")
    moved = grace.replace("<staff>1</staff>", "<staff>2</staff>")
    last = _note("m2s2n8")
    backup = (
        "      <backup>\n        <duration>36</duration>\n      </backup>\n"
    )
    first = grace.replace('id="m2s1n3"', 'id="m2s1n3a"').replace(
        "<step>D</step>", "<step>C</step>"
    )
    two = _variant((grace, first + grace))

    def beamed(note_id: str, beam: str, staff: int) -> tuple[str, str]:
        return _edit(
            note_id,
            "        <staff>1</staff>\n",
            f"        <staff>{staff}</staff>\n"
            f'        <beam number="1">{beam}</beam>\n',
            base=two,
        )

    return {
        "grace-moved": (
            BASE,
            _variant(_edit("m2s1n3", "<staff>1</staff>", "<staff>2</staff>")),
        ),
        "grace-after-backup": (
            BASE,
            _variant((grace, ""), (last, last + backup + moved)),
        ),
        "first-of-two-graces": (
            two,
            _variant(
                _edit(
                    "m2s1n3a", "<staff>1</staff>", "<staff>2</staff>", base=two
                ),
                base=two,
            ),
        ),
        "first-of-two-beamed-graces": (
            _variant(
                beamed("m2s1n3a", "begin", 1),
                beamed("m2s1n3", "end", 1),
                base=two,
            ),
            _variant(
                beamed("m2s1n3a", "begin", 2),
                beamed("m2s1n3", "end", 1),
                base=two,
            ),
        ),
    }


GRACE_ON_STAFF_2_PAIRS = _grace_on_staff_2_pairs()


@pytest.mark.parametrize("pair", GRACE_ON_STAFF_2_PAIRS)
def test_grace_note_on_another_staff_keeps_its_voices_order(pair: str) -> None:
    """Order a voice's events as written, whichever staff shows them."""
    home, moved = GRACE_ON_STAFF_2_PAIRS[pair]
    annotated = annotate(moved)

    assert voice_order_by_measure(annotated, annotated)[1] == ElementTally(
        2, 2, 0
    )
    # The moved grace note adds one cross-staff element. A home with two
    # grace notes has one musicdiff element more than ``BASE``.
    home_count = BASE_FILE_ELEMENT_COUNT + (home is not BASE)
    assert failure_element_count(home) == home_count
    assert failure_element_count(moved) == home_count + 1
    for t_text, y_text, cross_staff in (
        (home, moved, (1, 0)),
        (moved, home, (0, 1)),
    ):
        result = compare_file(t_text, y_text)
        assert result.checks["voice_order"] == ElementTally(
            VOICE_ORDER_ELEMENT_COUNT, VOICE_ORDER_ELEMENT_COUNT, 0
        )
        _assert_file(result, cross_staff=cross_staff)


@dataclass(frozen=True, slots=True)
class _ValuePair:
    """A pair whose ``T(Y)`` changes one keyword value of ``Y``.

    ``edits`` turn the canonical text of ``reverse(Y)`` into that of
    ``reverse(T(Y))``; each replaces every occurrence of its old text.
    """

    t_text: str
    y_text: str
    category: str
    count: int
    edits: tuple[tuple[str, str], ...]


def _value_cycle(
    option: str,
    category: str,
    build: Callable[[str], str],
    values: tuple[tuple[str, str, int], ...],
) -> dict[str, _ValuePair]:
    """Change each value of a cycle of ``(key, text, count)`` to the next.

    ``build`` makes the score that holds a value from its key, ``text`` is
    how the value reads in the canonical text, and ``count`` is how many
    differences the change from it to the next value gives.
    """
    following = values[1:] + values[:1]
    return {
        f"{option}-{key}": _ValuePair(
            build(next_key),
            build(key),
            category,
            count,
            ((text, next_text),),
        )
        for (key, text, count), (next_key, next_text, _) in zip(
            values,
            following,
            strict=True,
        )
    }


def _with_directions(
    *directions: tuple[str, int, str],
    base: str = BASE,
) -> str:
    """Insert each ``(note id, staff, direction content)`` before its note."""
    return _variant(
        *(
            _before(note_id, _direction(inner, staff))
            for note_id, staff, inner in directions
        ),
        base=base,
    )


def _closing_barline(style: str, *, repeat: bool = False) -> str:
    """Close measure 7 with ``style`` and optionally a backward repeat."""
    old = "        <bar-style>light-heavy</bar-style>\n      </barline>\n"
    backward = '        <repeat direction="backward"/>\n' if repeat else ""
    new = (
        f"        <bar-style>{style}</bar-style>\n{backward}      </barline>\n"
    )
    return _variant((old, new))


def _endings(*brackets: tuple[int, int, int]) -> str:
    """Add each ``(number, first, last)`` volta bracket over its measures."""
    text = BASE
    final = "<bar-style>light-heavy</bar-style>\n"
    for number, first, last in brackets:
        tag = f'    <measure number="{first}">\n'
        start = (
            '      <barline location="left">\n'
            f'        <ending number="{number}" type="start"/>\n'
            "      </barline>\n"
        )
        text = _variant((tag, tag + start), base=text)
        stop = f'        <ending number="{number}" type="stop"/>\n'
        measure = _measure(str(last), text)
        closed = (
            measure.replace(final, final + stop)
            if final in measure
            else measure.replace(
                "    </measure>",
                f'      <barline location="right">\n{stop}'
                "      </barline>\n    </measure>",
            )
        )
        text = _variant((measure, closed), base=text)
    return text


def _articulated(name: str) -> str:
    """Mark the E5 of measure 1 with one articulation or a fermata."""
    if name == "fermata":
        return _marked(BASE, ("m1s1n3", "          <fermata/>\n"))
    articulations = (
        "          <articulations>\n"
        f"            <{name}/>\n"
        "          </articulations>\n"
    )
    return _marked(BASE, ("m1s1n3", articulations))


def _arpeggiated(number: str | None) -> str:
    """Arpeggiate an E5+G5 of staff 1 and the triad of staff 2 in measure 2.

    With a ``number``, MusicXML joins both chords into one arpeggio.
    """
    root = _note("m2s1n1")
    member = (
        root.replace('id="m2s1n1"', 'id="m2s1n1b"')
        .replace("        <pitch>", "        <chord/>\n        <pitch>")
        .replace("<step>E</step>", "<step>G</step>")
    )
    chord = _variant((root, root + member))
    attribute = f' number="{number}"' if number else ""
    return _marked(
        chord,
        *(
            (note_id, f"          <arpeggiate{attribute}/>\n")
            for note_id in ("m2s1n1", "m2s1n1b", "m2s2n1", "m2s2n2", "m2s2n3")
        ),
    )


def _ornamented(name: str) -> str:
    return _marked(BASE, ("m1s1n3", _ornaments(f"            <{name}/>\n")))


def _dynamic(name: str) -> str:
    dynamics = (
        f"          <dynamics>\n            <{name}/>\n          </dynamics>\n"
    )
    return _with_directions(("m1s1n3", 1, dynamics))


def _staff_2_clef(sign: str, line: int, octave_change: int = 0) -> str:
    """Change the staff 2 clef at the start of measure 6."""
    change = (
        f"          <clef-octave-change>{octave_change}</clef-octave-change>\n"
        if octave_change
        else ""
    )
    return _variant(
        _before(
            "m6s2n1",
            _attributes(
                '        <clef number="2">\n'
                f"          <sign>{sign}</sign>\n"
                f"          <line>{line}</line>\n{change}"
                "        </clef>\n",
            ),
        ),
    )


def _metronome(unit: str) -> str:
    """Mark 96 per MusicXML beat ``unit``, dotted when it ends with a dot."""
    dot = "            <beat-unit-dot/>\n" if unit.endswith(".") else ""
    return _words_at(
        "          <metronome>\n"
        f"            <beat-unit>{unit.removesuffix('.')}</beat-unit>\n"
        f"{dot}"
        "            <per-minute>96</per-minute>\n"
        "          </metronome>\n",
    )


def _pedal(kind: str, *, line: bool) -> str:
    shape = 'line="yes"' if line else 'line="no" sign="yes"'
    return f'          <pedal type="{kind}" {shape}/>\n'


def _pedals(*marks: tuple[str, str]) -> str:
    """Insert each ``(note id, pedal)`` on staff 2 before its note."""
    return _with_directions(*((note_id, 2, xml) for note_id, xml in marks))


def _tied(base: str, note_id: str, kind: str) -> str:
    """Add a tie of ``kind`` to one note of ``base``."""
    tie = _variant(
        _edit(
            note_id,
            "        <voice>",
            f'        <tie type="{kind}"/>\n        <voice>',
            base=base,
        ),
        base=base,
    )
    return _marked(tie, (note_id, f'          <tied type="{kind}"/>\n'))


def _hairpin(kind: str) -> str:
    return _with_directions(
        ("m1s1n3", 1, f'          <wedge type="{kind}"/>\n'),
        ("m1s1n4", 1, '          <wedge type="stop"/>\n'),
    )


# The MusicXML type, size and E5 octave of each ottava sign; MusicXML
# writes sounding pitches, so the written E5 stays.
_OTTAVAS = {
    "8va": ("down", 8, 6),
    "8vb": ("up", 8, 4),
    "15ma": ("down", 15, 7),
    "15mb": ("up", 15, 3),
}


def _ottava(sign: str) -> str:
    """Put the E5 of measure 1 under an ottava line of ``sign``."""
    kind, size, octave = _OTTAVAS[sign]
    shifted = _variant(
        _edit("m1s1n3", "<octave>5</octave>", f"<octave>{octave}</octave>"),
    )
    return _with_directions(
        (
            "m1s1n3",
            1,
            f'          <octave-shift type="{kind}" size="{size}"/>\n',
        ),
        (
            "m1s1n4",
            1,
            f'          <octave-shift type="stop" size="{size}"/>\n',
        ),
        base=shifted,
    )


_DOUBLE_BAR = _closing_barline("light-light")
_REPEAT_BAR = _closing_barline("light-heavy", repeat=True)
_ONE_BRACKET = _endings((1, 6, 7))
_TWO_BRACKETS = _endings((1, 6, 6), (2, 7, 7))
_CONTINUED_BRACKET = _endings((1, 5, 7))
_ENDED_BRACKET = _endings((1, 5, 6), (2, 7, 7))
_SLASHLESS_GRACE = _variant(_edit("m2s1n3", 'slash="yes"', 'slash="no"'))
_REHEARSAL = _words_at("          <rehearsal>Allegro con brio</rehearsal>\n")
_PEDAL_LINE = _pedals(
    ("m1s2n1", _pedal("start", line=True)),
    ("m1s2n2", _pedal("stop", line=True)),
)
_PEDAL_SIGNS = _pedals(
    ("m1s2n1", _pedal("start", line=False)),
    ("m1s2n2", _pedal("stop", line=False)),
)
# The C5 of measure 5 is tied into the first ending, measure 6, of a volta.
_VOLTA = _tied(
    _tied(_TWO_BRACKETS, "m5s1n2", "start"),
    "m6s1n2",
    "stop",
)
_TIE_IN = _tied(_VOLTA, "m7s1n1", "stop")
_TIE_END = _tied(_tied(_VOLTA, "m6s1n2", "start"), "m7s1n1", "stop")

# Each pair names the option and the value of ``Y`` that ``T(Y)`` changes.
VALUE_PAIRS: dict[str, _ValuePair] = {
    "time-symbol-numeric": _ValuePair(
        _COMMON_TIME,
        BASE,
        "time_signature",
        2,
        (("\nm 1\n", "\nm 1 time-symbol=common\n"),),
    ),
    "time-symbol-common": _ValuePair(
        BASE,
        _COMMON_TIME,
        "time_signature",
        2,
        (("\nm 1 time-symbol=common\n", "\nm 1\n"),),
    ),
    "time-symbol-cut": _ValuePair(
        _CUT_TIME.replace('<time symbol="cut">', "<time>"),
        _CUT_TIME,
        "time_signature",
        2,
        (("\nm 1 time-symbol=cut\n", "\nm 1\n"),),
    ),
    "right-final": _ValuePair(
        _DOUBLE_BAR,
        BASE,
        "repeat_ending",
        2,
        (("right=final", "right=double"),),
    ),
    "right-double": _ValuePair(
        _REPEAT_BAR,
        _DOUBLE_BAR,
        "repeat_ending",
        4,
        (("right=double", "right=repeat"),),
    ),
    "right-repeat": _ValuePair(
        BASE,
        _REPEAT_BAR,
        "repeat_ending",
        4,
        (("right=repeat", "right=final"),),
    ),
    "ending-start": _ValuePair(
        _TWO_BRACKETS,
        _ONE_BRACKET,
        "repeat_ending",
        6,
        (
            ("ending=1:start", "ending=1:single"),
            ("ending=end", "ending=2:single"),
        ),
    ),
    "ending-single": _ValuePair(
        _ONE_BRACKET,
        _TWO_BRACKETS,
        "repeat_ending",
        6,
        (
            ("ending=1:single", "ending=1:start"),
            ("ending=2:single", "ending=end"),
        ),
    ),
    "ending-continue": _ValuePair(
        _ENDED_BRACKET,
        _CONTINUED_BRACKET,
        "repeat_ending",
        4,
        (
            ("ending=end", "ending=2:single"),
            ("ending=continue", "ending=end"),
        ),
    ),
    "ending-end": _ValuePair(
        _CONTINUED_BRACKET,
        _ENDED_BRACKET,
        "repeat_ending",
        4,
        (
            ("ending=end", "ending=continue"),
            ("ending=2:single", "ending=end"),
        ),
    ),
    **_value_cycle(
        "articulation",
        "articulation_fermata",
        _articulated,
        (
            ("staccato", "[stacc]", 1),
            ("accent", "[acc]", 1),
            ("tenuto", "[ten]", 1),
            ("staccatissimo", "[stacciss]", 1),
            ("strong-accent", "[marc]", 1),
            ("detached-legato", "[port]", 2),
            ("fermata", "[ferm]", 2),
        ),
    ),
    "arpeggio-arp": _ValuePair(
        _arpeggiated("1"),
        _arpeggiated(None),
        "arpeggio",
        3,
        (
            ("[arp,beam1=start]", "[beam1=start,rel=arpeggio@r3:member]"),
            ("[arp]", "[rel=arpeggio@r3:member]"),
        ),
    ),
    "arpeggio-member": _ValuePair(
        _arpeggiated(None),
        _arpeggiated("1"),
        "arpeggio",
        3,
        (
            ("[beam1=start,rel=arpeggio@r3:member]", "[arp,beam1=start]"),
            ("[rel=arpeggio@r3:member]", "[arp]"),
        ),
    ),
    "grace-slashed": _ValuePair(
        _SLASHLESS_GRACE,
        BASE,
        "grace",
        1,
        (("[grace/]", "[grace]"),),
    ),
    "grace-plain": _ValuePair(
        BASE,
        _SLASHLESS_GRACE,
        "grace",
        1,
        (("[grace]", "[grace/]"),),
    ),
    **_value_cycle(
        "ornament",
        "ornament",
        _ornamented,
        (
            ("trill-mark", "[orn=trill]", 1),
            ("mordent", "[orn=mordent]", 1),
            ("inverted-mordent", "[orn=inverted-mordent]", 1),
            ("turn", "[orn=turn]", 1),
            ("inverted-turn", "[orn=inverted-turn]", 1),
        ),
    ),
    "beam-start": _ValuePair(
        _LEVEL_2_HOOKS,
        _LEVEL_2_SPLIT,
        "beam",
        2,
        (
            (
                "C3/16[beam1=start,beam2=start]",
                "C3/16[beam1=start,beam2=hook-forward]",
            ),
            (
                "D3/16[beam1=continue,beam2=end]",
                "D3/16[beam1=continue,beam2=hook-backward]",
            ),
        ),
    ),
    "beam-continue": _ValuePair(
        _LEVEL_2_SPLIT,
        BASE,
        "beam",
        2,
        (
            (
                "D3/16[beam1=continue,beam2=continue]",
                "D3/16[beam1=continue,beam2=end]",
            ),
            (
                "E3/16[beam1=continue,beam2=continue]",
                "E3/16[beam1=continue,beam2=start]",
            ),
        ),
    ),
    "beam-end": _ValuePair(
        BASE,
        _LEVEL_2_SPLIT,
        "beam",
        2,
        (
            (
                "D3/16[beam1=continue,beam2=end]",
                "D3/16[beam1=continue,beam2=continue]",
            ),
            (
                "E3/16[beam1=continue,beam2=start]",
                "E3/16[beam1=continue,beam2=continue]",
            ),
        ),
    ),
    "beam-hook-backward": _ValuePair(
        _HOOK_FORWARD,
        _HOOK_BACKWARD,
        "beam",
        1,
        (
            (
                "E3/16[beam1=continue,beam2=hook-backward]",
                "E3/16[beam1=continue,beam2=hook-forward]",
            ),
        ),
    ),
    "beam-hook-forward": _ValuePair(
        _HOOK_BACKWARD,
        _HOOK_FORWARD,
        "beam",
        1,
        (
            (
                "E3/16[beam1=continue,beam2=hook-forward]",
                "E3/16[beam1=continue,beam2=hook-backward]",
            ),
        ),
    ),
    **_value_cycle(
        "dynamic",
        "dynamic",
        _dynamic,
        tuple(
            (name, f"<{name}>", 1)
            for name in (
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
            )
        ),
    ),
    "clef-G": _ValuePair(
        _staff_2_clef("C", 1),
        _staff_2_clef("G", 1),
        "clef",
        1,
        (("<clef G1>", "<clef C1>"),),
    ),
    "clef-F": _ValuePair(
        _staff_2_clef("C", 3),
        _staff_2_clef("F", 3),
        "clef",
        1,
        (("<clef F3>", "<clef C3>"),),
    ),
    "clef-C": _ValuePair(
        _staff_2_clef("F", 4),
        _staff_2_clef("C", 4),
        "clef",
        1,
        (("<clef C4>", "<clef F4>"),),
    ),
    "clef-octave-up": _ValuePair(
        _staff_2_clef("G", 2, -1),
        _staff_2_clef("G", 2, 1),
        "clef",
        1,
        (("<clef G2+8>", "<clef G2-8>"),),
    ),
    "clef-octave-down": _ValuePair(
        _staff_2_clef("G", 2, 1),
        _staff_2_clef("G", 2, -1),
        "clef",
        1,
        (("<clef G2-8>", "<clef G2+8>"),),
    ),
    **_value_cycle(
        "metronome",
        "tempo_rehearsal",
        _metronome,
        (
            ("quarter", '"" q=96>', 1),
            ("half", '"" h=96>', 1),
            ("eighth", '"" e=96>', 1),
            ("quarter.", '"" q.=96>', 1),
            ("half.", '"" h.=96>', 1),
        ),
    ),
    "direction-tempo": _ValuePair(
        _REHEARSAL,
        _TEMPO,
        "tempo_rehearsal",
        2,
        (('<tempo "Allegro con brio">', '<rehearsal "Allegro con brio">'),),
    ),
    "direction-rehearsal": _ValuePair(
        _TEMPO,
        _REHEARSAL,
        "tempo_rehearsal",
        2,
        (('<rehearsal "Allegro con brio">', '<tempo "Allegro con brio">'),),
    ),
    "pedal-line-start": _ValuePair(
        _PEDAL_SIGNS,
        _PEDAL_LINE,
        "pedal",
        1,
        (
            ("<ped(>[rel=pedal@r2:line-start]", "<pedal-sign down>"),
            ("<ped)>[rel=pedal@r2:line-release]", "<pedal-sign up>"),
            ("tie@r3:", "tie@r2:"),
        ),
    ),
    "pedal-line-change": _ValuePair(
        _pedals(
            ("m2s2n1", _pedal("start", line=True)),
            ("m2s2n5", _pedal("start", line=False)),
            ("m2s2n6", _pedal("stop", line=True)),
        ),
        _pedals(
            ("m2s2n1", _pedal("start", line=True)),
            ("m2s2n5", _pedal("change", line=True)),
            ("m2s2n6", _pedal("stop", line=True)),
        ),
        "pedal",
        2,
        (("<ped(>[rel=pedal@r3:line-change]", "<pedal-sign down>"),),
    ),
    "pedal-sign-down": _ValuePair(
        _pedals(
            ("m1s2n1", _pedal("stop", line=False)),
            ("m1s2n2", _pedal("stop", line=False)),
        ),
        _PEDAL_SIGNS,
        "pedal",
        1,
        (("<pedal-sign down>", "<pedal-sign up>"),),
    ),
    "pedal-sign-up": _ValuePair(
        _pedals(
            ("m1s2n1", _pedal("start", line=False)),
            ("m1s2n2", _pedal("start", line=False)),
        ),
        _PEDAL_SIGNS,
        "pedal",
        1,
        (("<pedal-sign up>", "<pedal-sign down>"),),
    ),
    "tie-in": _ValuePair(
        _TIE_END,
        _TIE_IN,
        "tie",
        1,
        (
            ("C5{tie@r3:end}/1[down]", "C5{tie@r3:end,tie@r4:start}/1[down]"),
            ("C5{tie-in}/1", "C5{tie@r4:end}/1"),
        ),
    ),
    "tie-end": _ValuePair(
        _TIE_IN,
        _TIE_END,
        "tie",
        1,
        (
            ("C5{tie@r3:end,tie@r4:start}/1[down]", "C5{tie@r3:end}/1[down]"),
            ("C5{tie@r4:end}/1", "C5{tie-in}/1"),
        ),
    ),
    "hairpin-cresc": _ValuePair(
        _hairpin("diminuendo"),
        _hairpin("crescendo"),
        "hairpin",
        2,
        (
            (
                "<cresc(>[rel=hairpin@r3:start:cresc]",
                "<dim(>[rel=hairpin@r3:start:dim]",
            ),
        ),
    ),
    "hairpin-dim": _ValuePair(
        _hairpin("crescendo"),
        _hairpin("diminuendo"),
        "hairpin",
        2,
        (
            (
                "<dim(>[rel=hairpin@r3:start:dim]",
                "<cresc(>[rel=hairpin@r3:start:cresc]",
            ),
        ),
    ),
    **_value_cycle(
        "ottava",
        "ottava",
        _ottava,
        tuple((sign, sign, 1) for sign in _OTTAVAS),
    ),
}


@pytest.mark.parametrize("pair", VALUE_PAIRS, ids=str)
def test_value_pair_differs_only_in_its_option_category(pair: str) -> None:
    """Count a changed keyword value in its option's category alone."""
    value = VALUE_PAIRS[pair]
    result = compare_file(value.t_text, value.y_text)

    assert result.category_counts() == Counter({value.category: value.count})


@pytest.mark.parametrize("pair", VALUE_PAIRS, ids=str)
def test_value_pair_changes_only_its_value(pair: str) -> None:
    """Reverse both scores to valid texts that differ only in the value."""
    value = VALUE_PAIRS[pair]
    expected = from_musicxml_text(value.y_text)
    for old, new in value.edits:
        assert old in expected
        expected = expected.replace(old, new)

    assert from_musicxml_text(value.t_text) == expected


_DURATION = re.compile(r"<duration>(\d+)</duration>")

NO_DIFFERENCE_PAIRS: dict[str, tuple[str, str]] = {
    "identical": (BASE, BASE),
    "identical-single-staff": (SINGLE, SINGLE),
    "identical-beamed-rest-overlap": (
        _beamed_rest_score(),
        _beamed_rest_score(),
    ),
    "identifiers": (
        BASE.replace('id="m', 'id="x').replace('"P1"', '"P9"'),
        BASE,
    ),
    "divisions": (
        _DURATION.sub(
            lambda m: f"<duration>{int(m[1]) * 2}</duration>",
            BASE,
        ).replace("<divisions>12</divisions>", "<divisions>24</divisions>"),
        BASE,
    ),
    "renumbered-voices": (
        BASE.replace("<voice>5</voice>", "<voice>2</voice>").replace(
            "<voice>6</voice>",
            "<voice>1</voice>",
        ),
        BASE,
    ),
    "reprints": (_variant((_M5_REPRINTS, "")), BASE),
    "reprints-after-common-time": (
        _variant((_M5_REPRINTS, ""), base=_COMMON_TIME),
        _COMMON_TIME,
    ),
    "reprints-after-cut-time": (
        _variant((_M5_CUT_REPRINTS, ""), base=_CUT_TIME),
        _CUT_TIME,
    ),
    "line-reprints": _line_reprint_pair(),
    "bold-line-reprints": _line_reprint_pair(bold=True),
    "wavy-line-on-its-trilled-note": (
        _marked(BASE, ("m1s1n3", _TRILL_MARK)),
        _marked(
            BASE,
            (
                "m1s1n3",
                _ornaments(
                    "            <trill-mark/>\n"
                    '            <wavy-line type="start" number="1"/>\n'
                    '            <wavy-line type="stop" number="1"/>\n',
                ),
            ),
        ),
    ),
}


@pytest.mark.parametrize("pair", NO_DIFFERENCE_PAIRS, ids=str)
def test_pair_without_content_change_gives_no_difference(pair: str) -> None:
    """Report no difference for identifiers, divisions and reprints."""
    t_text, y_text = NO_DIFFERENCE_PAIRS[pair]
    result = compare_file(t_text, y_text)
    layout = compare_layout(t_text, y_text).tally

    _assert_file(result)
    assert result.content.differences == ()
    assert result.content.pairings == ()
    assert result.tally.fidelity == 1.0
    assert layout == ElementTally(layout.elements, layout.elements, 0)


def test_text_space_check_compares_reversed_canonical_texts() -> None:
    """Flag a pair whose reversed canonical texts differ."""
    t_same, y_same = NO_DIFFERENCE_PAIRS["identifiers"]
    _, t_pitch, y_pitch = CATEGORY_PAIRS["pitch"]

    assert text_space_consistent(t_same, y_same)
    assert not text_space_consistent(t_pitch, y_pitch)


def test_voice_order_ignores_raw_voice_numbers() -> None:
    """Keep canonical positions stable when raw voice numbers are renumbered."""
    t_text, _ = NO_DIFFERENCE_PAIRS["renumbered-voices"]
    tally = voice_order_tally(t_text, BASE)

    assert tally.elements == VOICE_ORDER_ELEMENT_COUNT
    assert tally.identical == tally.elements
    assert tally.extras == 0


def test_identical_pair_has_no_layout_operation() -> None:
    """Report no layout operation for an identical pair."""
    layout = compare_layout(BASE, BASE)

    assert layout.operations == ()
    assert layout.record.normalized_distance == 0.0
    assert layout.tally == ElementTally(
        BASE_LAYOUT_ELEMENT_COUNT,
        BASE_LAYOUT_ELEMENT_COUNT,
        0,
    )
    assert failure_layout_element_count(BASE) == BASE_LAYOUT_ELEMENT_COUNT


def test_whole_score_comparison_is_retained_as_a_diagnostic() -> None:
    """Keep the original whole-score alignment result separate from scoring."""
    base = _variant(_edit("m5s1n1", "<step>G</step>", "<step>E</step>"))
    result = compare_content(_rule_1_pair(base), base)

    _assert_only(result, "cross_staff")
    assert [
        operation.name for operation in result.record.whole_score.operations
    ] == [
        "delbar",
        "insbar",
        "voicedel",
    ]
    assert result.record.operations != result.record.whole_score.operations


def test_retained_record_keeps_every_raw_operation() -> None:
    """Retain each raw operation with its kinds, cost and location."""
    t_text = _variant(_edit("m1s1n2", "<step>D</step>", "<step>F</step>"))
    result = compare_content(t_text, BASE)

    (operation,) = result.record.operations
    assert operation.name == "pitchnameedit"
    assert operation.kind1 == operation.kind2 == "AnnNote"
    assert operation.cost == result.record.omr_edit_distance == 1
    assert operation.location2 is not None
    assert operation.location2.measure_number == "1"
    assert result.record.symbols_t == result.record.symbols_y > 0


def test_extra_element_lowers_fidelity() -> None:
    """Count an invented mark as an extra element."""
    _, t_text, y_text = CATEGORY_PAIRS["dynamic"]
    tally = compare_content(t_text, y_text).tally

    assert tally.extras == 1
    assert tally.identical == tally.elements
    assert tally.fidelity == tally.elements / (tally.elements + 1)


def test_failure_counts_every_in_scope_element_of_the_score() -> None:
    """Count a failed file's content and check elements on its score alone."""
    fingered = _marked(BASE, ("m1s1n3", _fingering("3")))

    assert failure_element_count(BASE) == BASE_FILE_ELEMENT_COUNT
    assert failure_element_count(fingered) == BASE_FILE_ELEMENT_COUNT + 1


def test_failure_count_includes_the_elements_of_every_check() -> None:
    """Count each check's elements on one score as a self-comparison does."""
    scores = [
        _cross_staff_beam_score(),
        _with_credits(_credit("title", "Title")),
        _trill_score("m2s1n1"),
        _dashes_score("m1s1n4"),
        _parenthesized("m3s1n1"),
        _variant(
            _edit(
                "m4s1n1",
                "<type>quarter</type>\n",
                "<type>quarter</type>\n"
                '        <accidental parentheses="yes">natural</accidental>\n',
            ),
        ),
        _marked(BASE, ("m1s1n3", _fingering("3"))),
        _marked(BASE, ("m1s1n3", _tremolo("single", 3))),
    ]
    counted: set[str] = set()
    for score in scores:
        result = compare_file(score, score)
        assert failure_element_count(score) == result.tally.elements
        counted |= {
            name for name, tally in result.checks.items() if tally.elements
        }

    assert counted == set(CHECKS)


def test_pooled_fidelity_pools_elements_over_files() -> None:
    """Pool identical and total elements over the files of an arm."""
    tallies = [ElementTally(10, 9, 0), ElementTally(30, 30, 0)]

    assert pooled_fidelity(tallies) == 39 / 40
    assert pooled_fidelity([]) is None


def test_unassignable_difference_stops_the_run() -> None:
    """Stop on a difference that the assignment cannot assign."""
    t_text = _variant(
        _notations(
            "m1s1n3",
            "          <ornaments>\n"
            "            <shake/>\n"
            "          </ornaments>\n",
        ),
    )

    with pytest.raises(ProtocolDefectError):
        compare_content(t_text, BASE)


def test_unparsable_score_is_a_parse_failure() -> None:
    """Report a score that musicdiff cannot parse as a failure."""
    with pytest.raises(ScoreParseError):
        compare_content("<score-partwise", BASE)


def test_coupling_fix_fails_closed_on_other_musicdiff_source(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Refuse to patch a voice-coupling source other than 6.0's."""

    def other(*_args: object) -> None:
        return None

    monkeypatch.setattr(
        Comparison,
        "_voices_coupling_recursive",
        staticmethod(other),
    )

    with pytest.raises(InstrumentMismatchError):
        comparison.install_coupling_fix()


def test_coupling_fix_fails_closed_on_other_musicdiff_version(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Refuse to patch a musicdiff version other than 6.0."""

    def other(*_args: object) -> None:
        return None

    monkeypatch.setattr(
        Comparison,
        "_voices_coupling_recursive",
        staticmethod(other),
    )
    monkeypatch.setattr(
        comparison.importlib.metadata,
        "version",
        lambda _name: "6.1",
    )

    with pytest.raises(InstrumentMismatchError, match=r"6\.1"):
        comparison.install_coupling_fix()


def test_voice_order_detects_stem_position_swap_in_1_2_1_beam_sequence() -> (
    None
):
    """Score a 1-2-1 cross-staff beam against its overlapping home voice."""
    y_text = _two_staff_score(
        _TwoStaffScore(
            3,
            8,
            3,
            (
                _musicxml_note(
                    _MusicXmlNote(
                        "start", "C", 5, 1, 1, 1, "up", "eighth", "begin"
                    )
                ),
                _musicxml_note(
                    _MusicXmlNote(
                        "middle", "D", 3, 1, 1, 2, "up", "eighth", "continue"
                    )
                ),
                _musicxml_note(
                    _MusicXmlNote("end", "E", 5, 1, 1, 1, "up", "eighth", "end")
                ),
            ),
            (
                _musicxml_note(
                    _MusicXmlNote("overlap", "A", 4, 1, 2, 1, "down", "eighth")
                ),
            ),
            1,
            1,
        ),
    )
    t_text = _two_staff_score(
        _TwoStaffScore(
            3,
            8,
            3,
            (
                _musicxml_note(
                    _MusicXmlNote(
                        "start", "C", 5, 1, 1, 1, "down", "eighth", "begin"
                    )
                ),
                _musicxml_note(
                    _MusicXmlNote(
                        "middle", "D", 3, 1, 1, 2, "up", "eighth", "continue"
                    )
                ),
                _musicxml_note(
                    _MusicXmlNote("end", "E", 5, 1, 1, 1, "up", "eighth", "end")
                ),
            ),
            (
                _musicxml_note(
                    _MusicXmlNote("overlap", "A", 4, 1, 2, 1, "up", "eighth")
                ),
            ),
            1,
            1,
        ),
    )

    content = compare_content(t_text, y_text)
    voice_order = voice_order_tally(t_text, y_text)
    layout = compare_layout(t_text, y_text)

    assert content.counted == ()
    assert [operation.name for operation in layout.operations] == [
        "editstemdirection",
        "editstemdirection",
    ]
    assert voice_order.elements > 0
    assert voice_order.identical < voice_order.elements
    assert voice_order.extras == 0


def test_staff_local_beam_voice_renumbering_preserves_positions() -> None:
    """Allow lower-staff voice IDs to renumber independently."""
    original = _four_voice_beam_score((5, 6))
    renumbered = _four_voice_beam_score((1, 2))

    content = compare_content(renumbered, original)
    voice_order = voice_order_tally(renumbered, original)

    assert content.differences == ()
    assert voice_order == ElementTally(4, 4, 0)


def test_cross_staff_beam_ownership_ignores_other_active_home_beams() -> None:
    """Use source voice identity when a foreign beam event is flattened."""
    score = _two_staff_score(
        _TwoStaffScore(
            3,
            8,
            3,
            (
                _musicxml_note(
                    _MusicXmlNote(
                        "cross-start", "C", 5, 1, 1, 1, "up", "eighth", "begin"
                    )
                ),
                _musicxml_note(
                    _MusicXmlNote(
                        "cross-middle",
                        "D",
                        3,
                        1,
                        1,
                        2,
                        "up",
                        "eighth",
                        "continue",
                    )
                ),
                _musicxml_note(
                    _MusicXmlNote(
                        "cross-stop", "E", 5, 1, 1, 1, "up", "eighth", "end"
                    )
                ),
            ),
            (
                _musicxml_note(
                    _MusicXmlNote(
                        "other-start",
                        "A",
                        4,
                        1,
                        2,
                        1,
                        "down",
                        "eighth",
                        "begin",
                    )
                ),
                _musicxml_note(
                    _MusicXmlNote(
                        "other-middle",
                        "B",
                        4,
                        1,
                        2,
                        1,
                        "down",
                        "eighth",
                        "continue",
                    )
                ),
                _musicxml_note(
                    _MusicXmlNote(
                        "other-stop",
                        "C",
                        5,
                        1,
                        2,
                        1,
                        "down",
                        "eighth",
                        "end",
                    )
                ),
            ),
            0,
            0,
        ),
    )

    content = compare_content(score, score)
    voice_order = voice_order_tally(score, score)

    assert content.differences == ()
    assert content.tally.fidelity == 1.0
    assert voice_order == ElementTally(2, 2, 0)


def test_grace_event_between_beam_start_and_stop_preserves_identity() -> None:
    """Ignore an intervening grace event when scoring a complete beam."""
    _, beamed = _mixed_stem_beam_pair()
    first_note = _note("m4s1n1", beamed)
    grace = (
        '      <note id="m4s1grace">\n'
        '        <grace slash="yes"/>\n'
        "        <pitch>\n"
        "          <step>D</step>\n"
        "          <octave>5</octave>\n"
        "        </pitch>\n"
        "        <voice>7</voice>\n"
        "        <type>16th</type>\n"
        "        <staff>1</staff>\n"
        "      </note>\n"
    )
    score = _variant((first_note, first_note + grace), base=beamed)

    content = compare_content(score, score)
    voice_order = voice_order_tally(score, score)

    assert content.differences == ()
    assert content.tally.fidelity == 1.0
    assert voice_order.elements > 0
    assert voice_order.identical == voice_order.elements
    assert voice_order.extras == 0
