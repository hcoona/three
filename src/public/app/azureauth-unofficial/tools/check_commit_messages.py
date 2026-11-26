"""Report commit messages that miss the repository's commit shape (advisory).

The shape is a Conventional Commits header, a nonempty body, and a final trailer
block (CONTRIBUTING.md#commits). Merge commits are skipped. The script always exits
0; findings are GitHub Actions warning annotations.
"""

from __future__ import annotations

import os
import re
import subprocess
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
ZERO_SHA = "0" * 40
# Conventional Commits 1.0.0: a type noun, an optional parenthesized scope, an optional
# "!", then ": " and a description. Types are case-insensitive.
HEADER = re.compile(r"^[A-Za-z]+(\([^()\s]+\))?!?: \S.*$")


def git(*args: str) -> str | None:
    result = subprocess.run(
        ["git", *args], cwd=ROOT, check=False, capture_output=True, text=True
    )
    return result.stdout if result.returncode == 0 else None


def commit_range() -> str | None:
    base = os.environ.get("COMMIT_MESSAGE_BASE", "")
    if not base or base == ZERO_SHA or git("rev-parse", "--verify", "--quiet", f"{base}^{{commit}}") is None:
        return None
    return f"{base}..HEAD"


def problems(message: str, trailers: str) -> list[str]:
    lines = message.strip("\n").split("\n")
    found: list[str] = []
    if not HEADER.fullmatch(lines[0]):
        found.append("header is not a Conventional Commits header")
    paragraphs = [p for p in "\n".join(lines[1:]).split("\n\n") if p.strip()]
    if not trailers.strip():
        found.append("no final trailer block")
        body = paragraphs
    else:
        body = paragraphs[:-1]
    if not body:
        found.append("empty body")
    return found


def main() -> int:
    revision_range = commit_range()
    if revision_range is None:
        print("No usable base commit; commit messages were not checked.", file=sys.stderr)
        return 0
    commits = (git("rev-list", "--no-merges", "--reverse", revision_range) or "").split()
    for commit in commits:
        message = git("log", "-1", "--format=%B", commit) or ""
        trailers = git("log", "-1", "--format=%(trailers:only,unfold)", commit) or ""
        for problem in problems(message, trailers):
            print(f"::warning title=Commit message::{commit[:12]}: {problem}")
    print(f"Checked {len(commits)} commit message(s) in {revision_range}.", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
