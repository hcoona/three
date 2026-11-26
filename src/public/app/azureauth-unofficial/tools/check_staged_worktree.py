from __future__ import annotations

import os
import subprocess
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


def changed_paths(*args: str) -> set[str]:
    result = subprocess.run(
        ["git", "diff", "--name-only", "-z", "--no-ext-diff", *args],
        cwd=ROOT,
        check=True,
        capture_output=True,
    )
    return {
        os.fsdecode(path)
        for path in result.stdout.split(b"\0")
        if path
    }


def untracked_paths() -> set[str]:
    result = subprocess.run(
        ["git", "ls-files", "--others", "--exclude-standard", "-z"],
        cwd=ROOT,
        check=True,
        capture_output=True,
    )
    return {
        os.fsdecode(path)
        for path in result.stdout.split(b"\0")
        if path
    }


def main() -> int:
    unstaged = changed_paths()
    untracked = untracked_paths()
    if not unstaged and not untracked:
        return 0

    print(
        "ERROR: pre-commit checks require a worktree containing only staged changes.",
        file=sys.stderr,
    )
    if unstaged:
        print("Unstaged paths:", file=sys.stderr)
        for path in sorted(unstaged):
            print(f"  {path}", file=sys.stderr)
    if untracked:
        print("Untracked paths:", file=sys.stderr)
        for path in sorted(untracked):
            print(f"  {path}", file=sys.stderr)
    print(
        "Stage the intended content or set these paths aside, then retry.",
        file=sys.stderr,
    )
    return 1


if __name__ == "__main__":
    sys.exit(main())
