"""Draw a monthly heatmap of commits in a Git repository."""

import argparse
import logging
from collections import Counter
from datetime import date, timedelta
from pathlib import Path

import git
import matplotlib.pyplot as plt
import pandas as pd
import seaborn as sns

DECEMBER = 12


def get_commit_dates(repo_path: Path) -> list[date]:
    """Return all commit dates from a Git repository."""
    repo = git.Repo(repo_path)
    return [commit.committed_datetime.date() for commit in repo.iter_commits()]


def build_month_matrix(
    year: int,
    month: int,
    counts: Counter[date],
) -> pd.DataFrame:
    """Build a week-by-week matrix of commit counts for one month."""
    first_day = date(year, month, 1)
    if month == DECEMBER:
        next_month_first_day = date(year + 1, 1, 1)
    else:
        next_month_first_day = date(year, month + 1, 1)
    all_days = [
        first_day + timedelta(days=offset)
        for offset in range((next_month_first_day - first_day).days)
    ]

    daily_counts = [counts.get(day, 0) for day in all_days]

    weekdays = [day.weekday() for day in all_days]
    first_week_start = first_day - timedelta(days=first_day.weekday())
    week_nums = [(day - first_week_start).days // 7 for day in all_days]

    df = pd.DataFrame(0, index=range(max(week_nums) + 1), columns=range(7))

    for count, w, wd in zip(daily_counts, week_nums, weekdays, strict=True):
        df.at[w, wd] = count

    return df


def main() -> None:
    """Run the command-line heatmap generator."""
    parser = argparse.ArgumentParser(
        description="Draw monthly git commit heatmap",
    )
    parser.add_argument("repo", help="Git repo path")
    parser.add_argument("year", type=int, help="Year (e.g., 2024)")
    parser.add_argument(
        "month",
        type=int,
        choices=range(1, DECEMBER + 1),
        help="Month (1-12)",
    )
    args = parser.parse_args()

    dates = get_commit_dates(Path(args.repo))
    if not dates:
        logging.warning("No commits found.")
        return

    counts = Counter(dates)

    df = build_month_matrix(args.year, args.month, counts)

    plt.figure(figsize=(10, 4))
    sns.heatmap(
        df,
        cmap="YlGn",
        linewidths=1,
        linecolor="gray",
        cbar_kws={"label": "Commits"},
        square=True,
        xticklabels=["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"],
        yticklabels=True,
    )

    plt.title(
        f"Git Commit Heatmap - {args.repo} ({args.year}-{args.month:02d})",
    )
    plt.ylabel("Week Number in Month")
    plt.xlabel("Weekday")
    plt.tight_layout()
    plt.show()


if __name__ == "__main__":
    main()
