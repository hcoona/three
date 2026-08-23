"""Tests for monthly commit heatmap layout."""

from collections import Counter
from datetime import date
from pathlib import Path
from runpy import run_path

build_month_matrix = run_path(str(Path(__file__).parents[1] / "main.py"))[
    "build_month_matrix"
]


def test_january_rollover_uses_consecutive_month_rows() -> None:
    """Keep the first January days adjacent across the ISO-year boundary."""
    friday_count = 1
    monday_count = 2
    matrix = build_month_matrix(
        2021,
        1,
        Counter(
            {
                date(2021, 1, 1): friday_count,
                date(2021, 1, 4): monday_count,
            }
        ),
    )

    assert list(matrix.index) == list(range(5))
    assert matrix.at[0, 4] == friday_count
    assert matrix.at[1, 0] == monday_count


def test_december_rollover_preserves_the_sixth_calendar_row() -> None:
    """Keep the final December Monday after an ISO-week rollover."""
    sunday_count = 3
    monday_count = 4
    matrix = build_month_matrix(
        2024,
        12,
        Counter(
            {
                date(2024, 12, 29): sunday_count,
                date(2024, 12, 30): monday_count,
            }
        ),
    )

    assert list(matrix.index) == list(range(6))
    assert matrix.at[4, 6] == sunday_count
    assert matrix.at[5, 0] == monday_count
