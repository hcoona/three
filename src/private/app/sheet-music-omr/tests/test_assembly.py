"""Scenario tests for the assembly of system documents.

Every document here is self-authored for this project and transcribes no
existing score.
"""

from __future__ import annotations

from pathlib import Path
from typing import TYPE_CHECKING

import pytest

from sheet_music_omr import (
    AssemblyStatus,
    assemble,
    parse_document,
    validate_document,
)
from sheet_music_omr.cli import main

if TYPE_CHECKING:
    from collections.abc import Callable

    from sheet_music_omr import AssemblyResult

FIXTURES = Path(__file__).resolve().parent / "fixtures"
ASSEMBLY = FIXTURES / "assembly"
STUDY = ("study-1.txt", "study-2.txt", "study-3.txt")
WALTZ = (
    FIXTURES / "system-first.txt",
    FIXTURES / "system-continuation.txt",
    ASSEMBLY / "waltz-3.txt",
)

INITIAL = (
    '{"measure":1,"offset":"0","event_counts":{},"accidental_scope":"octave",'
    '"accidentals":[],"open":[]}'
)


def system(
    page: int,
    number: int,
    body: str,
    carry_in: str = INITIAL,
) -> str:
    """Return a system document in 2/4 whose blocks start on line 9."""
    return (
        "format: text-score/2\nkey: 0\ntime: 2/4\nclefs: G2 F4\n"
        f"carry: octave\npage: {page}\nsystem: {number}\n"
        f"carry-in: {carry_in}\n{body}\n"
    )


def score(body: str) -> str:
    """Return an assembled score in 2/4 whose blocks start on line 6."""
    return (
        "format: text-score/2\nkey: 0\ntime: 2/4\nclefs: G2 F4\n"
        f"carry: octave\n{body}\n"
    )


def carry(measure: int, offset: str = "0", counts: str = "{}") -> str:
    """Return a carry-in state without accidentals or open relations."""
    return (
        f'{{"measure":{measure},"offset":"{offset}","event_counts":{counts},'
        '"accidental_scope":"octave","accidentals":[],"open":[]}'
    )


def _study(
    edit: Callable[[str, str], str] = lambda _name, text: text,
    names: tuple[str, ...] = STUDY,
) -> AssemblyResult:
    texts = [(ASSEMBLY / name).read_text("utf-8") for name in names]
    documents = [
        parse_document(edit(name, text))
        for name, text in zip(names, texts, strict=True)
    ]
    return assemble(documents, names=names)


def _replace(target: str, old: str, new: str) -> Callable[[str, str], str]:
    def edit(name: str, text: str) -> str:
        if name != target:
            return text
        assert old in text
        return text.replace(old, new, 1)

    return edit


def _found(result: AssemblyResult) -> list[str]:
    return [
        f"{d.source}:{d.diagnostic.code}@{d.diagnostic.location}"
        for d in result.diagnostics
    ]


def test_assembles_study_in_canonical_form() -> None:
    """Join a split measure, carried relations, a bracket and a tie-in."""
    expected = (ASSEMBLY / "study.expected.txt").read_text("utf-8")
    expected = expected.split("\n", 1)[1]
    result = _study(names=("study-3.txt", "study-1.txt", "study-2.txt"))
    assert result.status is AssemblyStatus.SUCCESS
    assert result.text == expected
    assert result.score is not None
    assert validate_document(result.score) == ()


def test_assembled_score_text_is_titled() -> None:
    """Write the optional title and composer in the assembled score."""
    result = assemble(
        [parse_document(system(1, 1, "m 1 right=final\n1.1: C4/2"))],
        title="Small Study",
        composer="Nobody",
    )
    assert result.status is AssemblyStatus.SUCCESS
    assert result.text == (
        'format: text-score/2\ntitle: "Small Study"\ncomposer: "Nobody"\n'
        "key: 0\ntime: 2/4\nclefs: G2 F4\ncarry: octave\n"
        "m 1 right=final\n1.1: C4/2\n"
    )


def test_trill_ending_on_direction_needs_a_person() -> None:
    """Report an extended trill and review markers instead of success."""
    documents = [parse_document(path.read_text("utf-8")) for path in WALTZ]
    names = [path.name for path in WALTZ]
    result = assemble(documents, names=names)
    assert result.status is AssemblyStatus.NEEDS_PERSON
    assert result.text is None
    assert [(str(t.start), str(t.end)) for t in result.trills] == [
        ("system-first.txt:20:133", "system-continuation.txt:11:65"),
    ]
    assert [
        (str(m.marker.location), m.marker.marker.candidates)
        for m in result.markers
    ] == [
        ("12:37", ("sfz", "sf")),
        ("14:66", ("B5/4", "Bb5/4")),
        ("15:40", ()),
    ]


def test_review_markers_need_a_person_and_keep_the_score() -> None:
    """Keep the score but report each review marker with its candidates."""
    first = system(1, 1, 'm 1 to=1/4\n1.1: <?>["p","pp"] C4/4')
    second = system(
        1,
        2,
        "m 1 from=1/4 right=final\n1.1: D4/4",
        carry(1, "1/4", '{"1.1":2}'),
    )
    result = assemble([parse_document(first), parse_document(second)])
    assert result.status is AssemblyStatus.NEEDS_PERSON
    assert result.text is not None
    assert '1.1: <?>["p","pp"] C4/4 D4/4\n' in result.text
    assert [
        (m.source, str(m.marker.location), m.marker.marker.candidates)
        for m in result.markers
    ] == [("#1", "10:6", ("p", "pp"))]


PAIR = (
    system(1, 1, "m 1 to=1/4\n1.1: C4/4\n2.1: C3/4"),
    system(
        1,
        2,
        "m 1 from=1/4 right=final\n1.1: D4/4\n2.1: D3/4",
        carry(1, "1/4", '{"1.1":1,"2.1":1}'),
    ),
)

ARCS = (
    '{"measure":2,"offset":"0","event_counts":{},"accidental_scope":"octave",'
    '"accidentals":[],"open":['
    + ",".join(
        f'{{"id":"r{label}","kind":"arc","start":{{"measure":1,"offset":"0",'
        '"staff":1,"voice":1,"event_index":1,"member_index":1},'
        '"pitch":{"step":"C","octave":4,"alter":0}}'
        for label in (1, 2)
    )
    + "]}"
)

INVALID_STUDY = {
    "first carry-in is not initial": (
        _replace("study-1.txt", "", ""),
        ("study-2.txt", "study-3.txt"),
        "study-2.txt:assembly/initial_carry@9:11",
    ),
    "relation open at the end": (
        _replace("study-1.txt", "", ""),
        ("study-1.txt",),
        "study-1.txt:assembly/relation_open@13:6",
    ),
    "carry-in differs from carry-out": (
        _replace(
            "study-2.txt",
            '"voice":1,"event_index":1,"member_index":null}},{"id":"r3"',
            '"voice":1,"event_index":2,"member_index":null}},{"id":"r3"',
        ),
        STUDY,
        "study-2.txt:assembly/carry_in@9:11",
    ),
    "key differs from the state after the previous system": (
        _replace("study-3.txt", "key: 0", "key: 1"),
        STUDY,
        "study-3.txt:assembly/state@3:6",
    ),
    "clef differs from the state after the previous system": (
        _replace("study-3.txt", "clefs: G2 F4", "clefs: G2 C3"),
        STUDY,
        "study-3.txt:assembly/state@5:11",
    ),
    "two documents claim one system": (
        _replace("study-3.txt", "page: 2\nsystem: 1", "page: 1\nsystem: 2"),
        STUDY,
        "study-3.txt:assembly/order@7:7",
    ),
    "tie-in alteration differs from the source measure": (
        _replace("study-3.txt", "C5{tie-in}", "Cn5{tie-in}"),
        STUDY,
        "study-3.txt:semantic/tie_in@12:6",
    ),
    "document is invalid on its own": (
        _replace("study-3.txt", "C3/2.", "C3/2"),
        STUDY,
        "study-3.txt:semantic/measure_fill@13:1",
    ),
}


@pytest.mark.parametrize(
    ("edit", "names", "expected"),
    INVALID_STUDY.values(),
    ids=INVALID_STUDY.keys(),
)
def test_invalid_study_is_rejected(
    edit: Callable[[str, str], str],
    names: tuple[str, ...],
    expected: str,
) -> None:
    """Reject the study with the rule class and the source location."""
    result = _study(edit, names)
    assert result.status is AssemblyStatus.FAILED
    assert result.text is None
    assert _found(result)[0] == expected


INVALID = {
    "time differs from the state after the previous system": (
        (PAIR[0], PAIR[1].replace("time: 2/4", "time: 1/2")),
        "#2:assembly/state@3:7",
    ),
    "split measure changes len": (
        (
            PAIR[0].replace("m 1 to=1/4", "m 1 len=3/4 to=1/4"),
            PAIR[1],
        ),
        "#2:assembly/split_len@9:1",
    ),
    "split measure leaves a voice unfilled": (
        (PAIR[0], PAIR[1].replace("\n2.1: D3/4", "")),
        "#2:assembly/measure_fill@9:1",
    ),
    "last system leaves its measure unfinished": (
        (PAIR[0],),
        "#1:assembly/measure_fill@9:1",
    ),
    "two arcs from one pitch both end as ties": (
        (
            system(1, 1, "m 1\n1.1: C4{arc@r1:start,arc@r2:start}/2"),
            system(
                1,
                2,
                "m 2 right=final\n1.1: C4{tie@r1:end}/2\n1.2: C4{tie@r2:end}/2",
                ARCS,
            ),
        ),
        "#1:assembly/arc@10:6",
    ),
    "assembled score is not a system document": (
        (score("m 1 right=final\n1.1: C4/2"),),
        "#1:assembly/document_kind@1:1",
    ),
    "second ending skips a number across systems": (
        (
            system(1, 1, "m 1 right=repeat ending=1:start\n1.1: C4/2"),
            system(
                1,
                2,
                "m 2 ending=3:single right=final\n1.1: D4/2",
                carry(2),
            ),
        ),
        "#2:semantic/ending@9:5",
    ),
}


@pytest.mark.parametrize(
    ("texts", "expected"),
    INVALID.values(),
    ids=INVALID.keys(),
)
def test_invalid_documents_are_rejected(
    texts: tuple[str, ...],
    expected: str,
) -> None:
    """Reject the documents with the rule class and the source location."""
    result = assemble([parse_document(text) for text in texts])
    assert result.status is AssemblyStatus.FAILED
    assert _found(result)[0] == expected


def test_last_document_bracket_is_closed() -> None:
    """Close a volta bracket that the last document leaves open."""
    result = assemble(
        [
            parse_document(
                system(1, 1, "m 1 right=final ending=1:start\n1.1: C4/2")
            )
        ]
    )
    assert result.status is AssemblyStatus.SUCCESS
    assert result.text is not None
    assert "m 1 right=final ending=1:single\n" in result.text


def test_cli_exit_status_tells_outcomes_apart(
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Exit 0 on success, 1 on failure and 3 when a person is needed."""
    output = tmp_path / "score.txt"
    study = [str(ASSEMBLY / name) for name in STUDY]
    assert main(["assemble", *study, "-o", str(output)]) == 0
    assert output.read_bytes().count(b"\r") == 0
    assert main(["assemble", *study[1:]]) == 1
    assert main(["assemble", *map(str, WALTZ)]) == 3  # noqa: PLR2004
    errors = capsys.readouterr().err.splitlines()
    assert errors[0].startswith(f"{study[1]}:")
    assert "review: extended trill r8 ends on <trill)>" in errors[-4]
