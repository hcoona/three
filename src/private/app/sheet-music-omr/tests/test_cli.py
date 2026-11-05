"""Scenario tests for the command-line interface."""

from __future__ import annotations

from pathlib import Path
from typing import TYPE_CHECKING

from sheet_music_omr.cli import main

if TYPE_CHECKING:
    import pytest

FIXTURES = Path(__file__).resolve().parent / "fixtures"


def test_validate_lists_review_markers(
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Validate a valid file and list each review marker with its location."""
    valid = str(FIXTURES / "system-first.txt")
    marked = str(FIXTURES / "system-continuation.txt")
    assert main(["validate", valid, marked]) == 0
    lines = capsys.readouterr().out.splitlines()
    assert lines == [
        (
            f"{marked}:12:37: review: untimed review marker <?> in measure 3,"
            ' 2.1; candidates ["sfz","sf"]'
        ),
        (
            f"{marked}:12:53: review: timed review span ?(1/4) in measure 3,"
            ' 2.1; candidates ["B2/4","Bb2/4"]'
        ),
        (
            f"{marked}:14:66: review: timed review span ?(1/4) in measure 4,"
            " 1.1; no candidates"
        ),
        (
            f"{marked}:15:40: review: untimed review marker <?> in measure 4,"
            " 2.1; no candidates"
        ),
    ]


def test_validate_reports_diagnostics_and_fails(
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Report the rule class and location of an invalid file and fail."""
    invalid = str(FIXTURES / "invalid" / "misspelled-flag.txt")
    valid = str(FIXTURES / "assembled-score.txt")
    assert main(["validate", invalid, valid]) == 1
    assert capsys.readouterr().out == (
        f"{invalid}:11:23: grammar/note_flag: invalid note flag 'stac'\n"
    )


def test_canonicalize_writes_canonical_text(
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Write the canonical text of a non-canonical document."""
    source = FIXTURES / "noncanonical-system.txt"
    expected = (FIXTURES / "noncanonical-system.canonical.txt").read_text(
        encoding="utf-8",
    )
    assert main(["canonicalize", str(source)]) == 0
    assert capsys.readouterr().out == expected.split("\n", 1)[1]


def test_canonicalize_rejects_invalid_input(
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Reject an invalid document without writing any text."""
    invalid = str(FIXTURES / "invalid" / "misspelled-flag.txt")
    assert main(["canonicalize", invalid]) == 1
    captured = capsys.readouterr()
    assert not captured.out
    assert captured.err.startswith(f"{invalid}:11:23: grammar/note_flag:")
