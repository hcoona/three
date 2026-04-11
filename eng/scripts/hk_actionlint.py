"""Wrapper script for running actionlint with watchdog timeout."""

from __future__ import annotations

import contextlib
import os
import signal
import subprocess
import sys
import time
from datetime import datetime

_MIN_QUOTED_LEN = 2


def normalize_path(value: str) -> str:
    """Strip surrounding quotes and escaped quotes from a path."""
    normalized = value.strip().replace('\\"', '"')

    while len(normalized) >= _MIN_QUOTED_LEN and (
        (normalized[0] == '"' and normalized[-1] == '"')
        or (normalized[0] == "'" and normalized[-1] == "'")
    ):
        normalized = normalized[1:-1].strip()

    return normalized


def now_iso() -> str:
    """Return the current time as an ISO 8601 string."""
    return datetime.now().astimezone().isoformat(timespec="seconds")


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


def build_actionlint_command(
    path: str | None = None,
) -> list[str]:
    """Build the actionlint CLI command list."""
    shellcheck = os.getenv("HK_ACTIONLINT_SHELLCHECK", "").strip()
    pyflakes = os.getenv("HK_ACTIONLINT_PYFLAKES", "").strip()

    command = [
        "actionlint",
        f"-shellcheck={shellcheck}",
        f"-pyflakes={pyflakes}",
    ]

    if path is not None:
        command.append(path)

    return command


def kill_process_tree(
    process: subprocess.Popen[object],
) -> None:
    """Terminate a process and its children."""
    if process.poll() is not None:
        return

    if os.name == "nt":
        subprocess.run(
            [
                "taskkill",
                "/F",
                "/T",
                "/PID",
                str(process.pid),
            ],
            check=False,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
        return

    try:
        os.killpg(process.pid, signal.SIGKILL)
    except ProcessLookupError:
        return


def run_with_watchdog(  # noqa: PLR0913
    command: list[str],
    timeout_seconds: int,
    heartbeat_seconds: int,
    index: int,
    total: int,
    label: str,
) -> tuple[int | None, float, bool]:
    """Run a command with periodic heartbeat and timeout."""
    process = subprocess.Popen(
        command,
        start_new_session=os.name != "nt",
    )

    start = time.monotonic()
    next_heartbeat = heartbeat_seconds

    while True:
        returncode = process.poll()
        elapsed = time.monotonic() - start

        if returncode is not None:
            return returncode, elapsed, False

        if elapsed >= next_heartbeat:
            print(
                f"[{now_iso()}] [{index}/{total}]"
                f" still running"
                f" elapsed={elapsed:.0f}s:"
                f" {label}",
                flush=True,
            )
            next_heartbeat += heartbeat_seconds

        if elapsed >= timeout_seconds:
            kill_process_tree(process)
            with contextlib.suppress(
                subprocess.TimeoutExpired,
            ):
                process.wait(timeout=5)
            return None, elapsed, True

        time.sleep(1)


def main() -> int:
    """Run actionlint on each file with a watchdog."""
    paths = [normalize_path(p) for p in sys.argv[1:] if normalize_path(p)]
    timeout = resolve_timeout_seconds()
    heartbeat = resolve_heartbeat_seconds()

    if not paths:
        ts = now_iso()
        label = "(no explicit files)"
        print(
            f"[{ts}] [1/1] actionlint {label} (timeout={timeout}s)",
            flush=True,
        )
        rc, elapsed, timed_out = run_with_watchdog(
            build_actionlint_command(),
            timeout,
            heartbeat,
            1,
            1,
            label,
        )
        if timed_out:
            print(
                f"[{now_iso()}] [1/1] timed out after {timeout}s: {label}",
                file=sys.stderr,
                flush=True,
            )
            return 1

        print(
            f"[{now_iso()}] [1/1] finished exit={rc} elapsed={elapsed:.1f}s",
            flush=True,
        )
        return 0 if rc is None else rc

    has_error = False
    total = len(paths)

    for index, path in enumerate(paths, start=1):
        ts = now_iso()
        print(
            f"[{ts}] [{index}/{total}] actionlint {path} (timeout={timeout}s)",
            flush=True,
        )

        rc, elapsed, timed_out = run_with_watchdog(
            build_actionlint_command(path),
            timeout,
            heartbeat,
            index,
            total,
            path,
        )

        if timed_out:
            print(
                f"[{now_iso()}] [{index}/{total}]"
                f" timed out after"
                f" {timeout}s: {path}",
                file=sys.stderr,
                flush=True,
            )
            has_error = True
            continue

        print(
            f"[{now_iso()}] [{index}/{total}]"
            f" finished exit={rc}"
            f" elapsed={elapsed:.1f}s",
            flush=True,
        )
        has_error = has_error or (rc is not None and rc != 0)

    return 1 if has_error else 0


if __name__ == "__main__":
    raise SystemExit(main())
