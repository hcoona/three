"""Wrapper script for running pkl eval with path normalization."""

from __future__ import annotations

import subprocess
import sys

_MIN_QUOTED_LEN = 2


def normalize_path(value: str) -> str:
    """Strip surrounding double-quotes from a path."""
    stripped = value.strip()
    if (
        len(stripped) >= _MIN_QUOTED_LEN
        and stripped[0] == '"'
        and stripped[-1] == '"'
    ):
        return stripped[1:-1]
    return stripped


def collect_paths(argv: list[str]) -> list[str]:
    """Gather file paths from argv or stdin."""
    candidates = argv[1:]
    if candidates:
        return [normalize_path(p) for p in candidates if normalize_path(p)]

    return [
        normalize_path(line)
        for line in sys.stdin.read().splitlines()
        if normalize_path(line)
    ]


def main() -> int:
    """Evaluate each pkl file and report failures."""
    paths = collect_paths(sys.argv)
    if not paths:
        return 0

    has_error = False

    for path in paths:
        result = subprocess.run(
            ["pkl", "eval", path],
            capture_output=True,
            text=True,
            check=False,
        )

        if result.returncode == 0:
            continue

        has_error = True
        if result.stdout:
            print(
                result.stdout,
                end="",
                file=sys.stdout,
            )
        if result.stderr:
            print(
                result.stderr,
                end="",
                file=sys.stderr,
            )

    return 1 if has_error else 0


if __name__ == "__main__":
    raise SystemExit(main())
