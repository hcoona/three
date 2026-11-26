"""Wrapper script for running actionlint with watchdog timeout."""

from __future__ import annotations

import os
import sys

from hk_exec import normalize_path_arg, run_command
from hk_file_operands import existing_operands


def resolve_timeout_seconds() -> int:
    """Read timeout from environment or return the default."""
    raw = os.getenv("HK_ACTIONLINT_TIMEOUT_SECONDS", "120").strip()

    try:
        timeout_seconds = int(raw)
    except ValueError:
        timeout_seconds = 120

    return max(1, timeout_seconds)


def resolve_heartbeat_seconds() -> int:
    """Read heartbeat interval from environment or default."""
    raw = os.getenv("HK_ACTIONLINT_HEARTBEAT_SECONDS", "30").strip()

    try:
        heartbeat_seconds = int(raw)
    except ValueError:
        heartbeat_seconds = 30

    return max(5, heartbeat_seconds)


def build_actionlint_command() -> list[str]:
    """Build the actionlint CLI command prefix."""
    shellcheck = os.getenv("HK_ACTIONLINT_SHELLCHECK", "").strip()
    pyflakes = os.getenv("HK_ACTIONLINT_PYFLAKES", "").strip()

    command = ["actionlint"]

    if shellcheck:
        command.append(f"-shellcheck={shellcheck}")

    if pyflakes:
        command.append(f"-pyflakes={pyflakes}")

    return command


def main() -> int:
    """Run actionlint across the file batch with watchdog supervision."""
    paths = [
        normalize_path_arg(p) for p in sys.argv[1:] if normalize_path_arg(p)
    ]
    paths = existing_operands(paths)
    if sys.argv[1:] and not paths:
        print("No existing file operands; skipping file check.")
        return 0
    return run_command(
        build_actionlint_command(),
        paths,
        resolve_timeout_seconds(),
        resolve_heartbeat_seconds(),
        allow_per_file=False,
    )


if __name__ == "__main__":
    raise SystemExit(main())
