from __future__ import annotations

from datetime import datetime
import os
import signal
import subprocess
import sys
import time


def normalize_path(value: str) -> str:
    normalized = value.strip().replace('\\"', '"')

    while len(normalized) >= 2 and (
        (normalized[0] == '"' and normalized[-1] == '"')
        or (normalized[0] == "'" and normalized[-1] == "'")
    ):
        normalized = normalized[1:-1].strip()

    return normalized


def now_iso() -> str:
    return datetime.now().astimezone().isoformat(timespec="seconds")


def resolve_timeout_seconds() -> int:
    raw_value = os.getenv("HK_ACTIONLINT_TIMEOUT_SECONDS", "120").strip()

    try:
        timeout_seconds = int(raw_value)
    except ValueError:
        timeout_seconds = 120

    return max(1, timeout_seconds)


def resolve_heartbeat_seconds() -> int:
    raw_value = os.getenv("HK_ACTIONLINT_HEARTBEAT_SECONDS", "30").strip()

    try:
        heartbeat_seconds = int(raw_value)
    except ValueError:
        heartbeat_seconds = 30

    return max(5, heartbeat_seconds)


def build_actionlint_command(path: str | None = None) -> list[str]:
    shellcheck_cmd = os.getenv("HK_ACTIONLINT_SHELLCHECK", "").strip()
    pyflakes_cmd = os.getenv("HK_ACTIONLINT_PYFLAKES", "").strip()

    command = [
        "actionlint",
        f"-shellcheck={shellcheck_cmd}",
        f"-pyflakes={pyflakes_cmd}",
    ]

    if path is not None:
        command.append(path)

    return command


def kill_process_tree(process: subprocess.Popen[object]) -> None:
    if process.poll() is not None:
        return

    if os.name == "nt":
        subprocess.run(
            ["taskkill", "/F", "/T", "/PID", str(process.pid)],
            check=False,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
        return

    try:
        os.killpg(process.pid, signal.SIGKILL)
    except ProcessLookupError:
        return


def run_with_watchdog(
    command: list[str],
    timeout_seconds: int,
    heartbeat_seconds: int,
    index: int,
    total: int,
    label: str,
) -> tuple[int | None, float, bool]:
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
                f"[{now_iso()}] [{index}/{total}] still running elapsed={elapsed:.0f}s: {label}",
                flush=True,
            )
            next_heartbeat += heartbeat_seconds

        if elapsed >= timeout_seconds:
            kill_process_tree(process)
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                pass
            return None, elapsed, True

        time.sleep(1)


def main() -> int:
    paths = [
        normalize_path(path) for path in sys.argv[1:] if normalize_path(path)
    ]
    timeout_seconds = resolve_timeout_seconds()
    heartbeat_seconds = resolve_heartbeat_seconds()

    if not paths:
        started_at = now_iso()
        print(
            f"[{started_at}] [1/1] actionlint (no explicit files) (timeout={timeout_seconds}s)",
            flush=True,
        )
        returncode, elapsed, timed_out = run_with_watchdog(
            build_actionlint_command(),
            timeout_seconds,
            heartbeat_seconds,
            1,
            1,
            "(no explicit files)",
        )
        if timed_out:
            print(
                f"[{now_iso()}] [1/1] timed out after {timeout_seconds}s: (no explicit files)",
                file=sys.stderr,
                flush=True,
            )
            return 1

        print(
            f"[{now_iso()}] [1/1] finished exit={returncode} elapsed={elapsed:.1f}s",
            flush=True,
        )
        return 0 if returncode is None else returncode

    has_error = False
    total = len(paths)

    for index, path in enumerate(paths, start=1):
        started_at = now_iso()
        start = time.monotonic()
        print(
            f"[{started_at}] [{index}/{total}] actionlint {path} (timeout={timeout_seconds}s)",
            flush=True,
        )

        returncode, elapsed, timed_out = run_with_watchdog(
            build_actionlint_command(path),
            timeout_seconds,
            heartbeat_seconds,
            index,
            total,
            path,
        )

        if timed_out:
            print(
                f"[{now_iso()}] [{index}/{total}] timed out after {timeout_seconds}s: {path}",
                file=sys.stderr,
                flush=True,
            )
            has_error = True
            continue

        print(
            f"[{now_iso()}] [{index}/{total}] finished exit={returncode} elapsed={elapsed:.1f}s",
            flush=True,
        )
        has_error = has_error or (returncode is not None and returncode != 0)

    return 1 if has_error else 0


if __name__ == "__main__":
    raise SystemExit(main())
