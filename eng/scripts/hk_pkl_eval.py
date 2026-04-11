from __future__ import annotations

import subprocess
import sys


def normalize_path(value: str) -> str:
    stripped = value.strip()
    if len(stripped) >= 2 and stripped[0] == '"' and stripped[-1] == '"':
        return stripped[1:-1]
    return stripped


def collect_paths(argv: list[str]) -> list[str]:
    candidates = argv[1:]
    if candidates:
        return [
            normalize_path(path) for path in candidates if normalize_path(path)
        ]

    from_stdin = [
        normalize_path(line)
        for line in sys.stdin.read().splitlines()
        if normalize_path(line)
    ]
    return from_stdin


def main() -> int:
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
            print(result.stdout, end="", file=sys.stdout)
        if result.stderr:
            print(result.stderr, end="", file=sys.stderr)

    return 1 if has_error else 0


if __name__ == "__main__":
    raise SystemExit(main())
