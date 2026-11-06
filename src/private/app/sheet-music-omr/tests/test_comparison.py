"""Calibration scenarios of the round-trip comparison instrument.

Each pair compares a self-authored reference ``Y`` from
``fixtures/comparison`` with a round trip ``T(Y)`` derived from it by exact
text edits, and asserts what the calibration section of
``docs/comparison-protocol.md`` expects of it.
"""

from __future__ import annotations

import re
from pathlib import Path

import pytest
from musicdiff.comparison import Comparison

from sheet_music_omr import comparison
from sheet_music_omr.comparison import (
    ContentResult,
    ElementTally,
    InstrumentMismatchError,
    ProtocolDefectError,
    ScoreParseError,
    compare_content,
    compare_layout,
    failure_element_count,
    pooled_fidelity,
)

FIXTURES = Path(__file__).resolve().parent / "fixtures" / "comparison"
BASE = (FIXTURES / "base.musicxml").read_text(encoding="utf-8")
SINGLE = (FIXTURES / "single-staff.musicxml").read_text(encoding="utf-8")


def _note(note_id: str, text: str = BASE) -> str:
    start = text.index(f'      <note id="{note_id}">')
    end = text.index("</note>\n", start) + len("</note>\n")
    return text[start:end]


def _edit(note_id: str, *changes: str) -> tuple[str, str]:
    """Replace each old text of ``changes`` once in one note."""
    note = edited = _note(note_id)
    for old, new in zip(changes[::2], changes[1::2], strict=True):
        assert edited.count(old) == 1
        edited = edited.replace(old, new)
    return note, edited


def _before(note_id: str, xml: str) -> tuple[str, str]:
    tag = f'      <note id="{note_id}">\n'
    return tag, xml + tag


def _notations(note_id: str, inner: str) -> tuple[str, str]:
    notations = f"        <notations>\n{inner}        </notations>\n"
    return _edit(note_id, "      </note>\n", notations + "      </note>\n")


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


def _variant(*edits: tuple[str, str], base: str = BASE) -> str:
    text = base
    for old, new in edits:
        assert text.count(old) == 1
        text = text.replace(old, new)
    return text


def _assert_only(result: ContentResult, category: str) -> None:
    assert len(result.differences) == 1
    assert result.counted[0].categories == {category}


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


def _rule_2_pair() -> str:
    """Move the last E5 of measure 4 from voice 1 to voice 2."""
    note = _note("m4s1n4")
    forward = (
        "      <forward>\n"
        "        <duration>{}</duration>\n"
        "        <voice>{}</voice>\n"
        "        <staff>1</staff>\n"
        "      </forward>\n"
    )
    moved = note.replace("<voice>1</voice>", "<voice>2</voice>")
    return _variant(
        (note, forward.format(12, 1)),
        (forward.format(24, 2), forward.format(12, 2) + moved),
    )


def _rule_1_pair() -> str:
    """Move the voice-2 C5 of measure 5 to staff 2, keeping its voice."""
    note = _note("m5s1n2")
    backup = (
        "      <backup>\n        <duration>48</duration>\n      </backup>\n"
    )
    moved = note.replace("<staff>1</staff>", "<staff>2</staff>")
    return _variant(
        (note + backup, ""), (_note("m5s2n1"), moved + backup + _note("m5s2n1"))
    )


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
    "tuplet": ("tuplet", BASE.replace(_TRIPLET, _SEXTUPLET), BASE),
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
}


@pytest.mark.parametrize("pair", CATEGORY_PAIRS, ids=str)
def test_category_pair_gives_one_difference_in_its_category(pair: str) -> None:
    """Report exactly one difference, in the target category."""
    category, t_text, y_text = CATEGORY_PAIRS[pair]

    _assert_only(compare_content(t_text, y_text), category)


def test_category_pairs_cover_every_musicdiff_category() -> None:
    """Cover each category that musicdiff scores with a category pair."""
    covered = {category for category, _, _ in CATEGORY_PAIRS.values()}

    # The fingering and heading pairs are scored by the supplementary checks.
    assert covered == set(comparison.CONTENT_CATEGORIES) - {
        "fingering",
        "heading",
    }


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
    result = compare_content(pair(), BASE)

    _assert_only(result, "cross_staff")
    assert len(result.non_identical) == 1
    assert result.tally.extras == 0


def test_cross_staff_check_element_disregards_rule_1_pair() -> None:
    """Leave a rule-1 pair to the cross-staff check that owns it."""
    result = compare_content(
        _rule_1_pair(),
        BASE,
        cross_staff_owned=lambda _y, _t: True,
    )

    assert result.differences == ()


def test_note_moved_to_other_voice_is_one_non_identical_voice_element() -> None:
    """Pair a note moved to another voice as one voice element."""
    result = compare_content(_rule_2_pair(), BASE)

    _assert_only(result, "voice")
    assert len(result.non_identical) == 1
    assert result.tally.extras == 0


def test_voice_coupling_pair_is_one_pitch_difference() -> None:
    """Count C4 and E4 against two C4s as one pitch difference."""
    t_text = _variant(_edit("m3s1n2", "<step>E</step>", "<step>C</step>"))

    _assert_only(compare_content(t_text, BASE), "pitch")


def test_dropped_ordinary_tie_is_one_tie_difference() -> None:
    """Count a dropped ordinary tie as one musicdiff tie difference."""
    _, t_text, y_text = CATEGORY_PAIRS["tie"]
    result = compare_content(t_text, y_text)

    assert [d.categories for d in result.counted] == [{"tie"}]
    assert [op.name for op in result.record.operations] == ["tieins"]


_DURATION = re.compile(r"<duration>(\d+)</duration>")

NO_DIFFERENCE_PAIRS: dict[str, tuple[str, str]] = {
    "identical": (BASE, BASE),
    "identical-single-staff": (SINGLE, SINGLE),
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
        BASE.replace("<voice>5</voice>", "<voice>1</voice>").replace(
            "<voice>6</voice>",
            "<voice>2</voice>",
        ),
        BASE,
    ),
    "reprints": (_variant((_M5_REPRINTS, "")), BASE),
}


@pytest.mark.parametrize("pair", NO_DIFFERENCE_PAIRS, ids=str)
def test_pair_without_content_change_gives_no_difference(pair: str) -> None:
    """Report no difference for identifiers, divisions and reprints."""
    t_text, y_text = NO_DIFFERENCE_PAIRS[pair]
    result = compare_content(t_text, y_text)

    assert result.differences == ()
    assert result.tally.fidelity == 1.0


def test_identical_pair_has_no_layout_operation() -> None:
    """Report no layout operation for an identical pair."""
    record = compare_layout(BASE, BASE)

    assert record.operations == ()
    assert record.normalized_distance == 0.0


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
    """Count a failed file's elements on its score alone."""
    count = failure_element_count(BASE)

    assert count == compare_content(BASE, BASE).tally.elements > 0


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
