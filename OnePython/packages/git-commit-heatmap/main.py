import argparse
from collections import Counter
from datetime import datetime, timedelta

import git
import matplotlib.pyplot as plt
import pandas as pd
import seaborn as sns


def get_commit_dates(repo_path):
    repo = git.Repo(repo_path)
    return [commit.committed_datetime.date() for commit in repo.iter_commits()]


def build_month_matrix(year, month, counts):
    first_day = datetime(year, month, 1).date()
    if month == 12:
        next_month_first_day = datetime(year + 1, 1, 1).date()
    else:
        next_month_first_day = datetime(year, month + 1, 1).date()
    last_day = next_month_first_day - timedelta(days=1)

    all_days = pd.date_range(first_day, last_day).date

    daily_counts = [counts.get(day, 0) for day in all_days]

    weekdays = [day.weekday() for day in all_days]
    week_nums = [
        ((day.isocalendar()[1]) - first_day.isocalendar()[1]) for day in all_days
    ]

    if week_nums[0] < 0:
        base_week = first_day.isocalendar()[1]
        week_nums = [day.isocalendar()[1] + 52 - base_week for day in all_days]

    df = pd.DataFrame(0, index=range(max(week_nums) + 1), columns=range(7))

    for count, w, wd in zip(daily_counts, week_nums, weekdays):
        df.at[w, wd] = count

    return df


def main():
    parser = argparse.ArgumentParser(description="Draw monthly git commit heatmap")
    parser.add_argument("repo", help="Git repo path")
    parser.add_argument("year", type=int, help="Year (e.g., 2024)")
    parser.add_argument("month", type=int, choices=range(1, 13), help="Month (1-12)")
    args = parser.parse_args()

    dates = get_commit_dates(args.repo)
    if not dates:
        print("No commits found.")
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

    plt.title(f"Git Commit Heatmap - {args.repo} ({args.year}-{args.month:02d})")
    plt.ylabel("Week Number in Month")
    plt.xlabel("Weekday")
    plt.tight_layout()
    plt.show()


if __name__ == "__main__":
    main()
