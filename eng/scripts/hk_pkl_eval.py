"""Wrapper script for running pkl eval with path normalization."""

from __future__ import annotations

import subprocess
import sys

_MIN_QUOTED_LEN = 2


def normalize_path(value: str) -> str:
    """Strip surrounding quotes and whitespace from a file path."""
    normalized = value.strip().replace('\\"', '"')

    while len(normalized) >= _MIN_QUOTED_LEN and (
        (normalized[0] == '"' and normalized[-1] == '"')
        or (normalized[0] == "'" and normalized[-1] == "'")
    ):
        normalized = normalized[1:-1].strip()

    return normalized


def collect_paths(argv: list[str]) -> list[str]:
    """Collect file paths from argv or stdin with normalization."""
    candidates = argv[1:] if argv[1:] else sys.stdin.read().splitlines()
    paths: list[str] = []
    for candidate in candidates:
        normalized = normalize_path(candidate)
        if normalized:
            paths.append(normalized)
    return paths


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
