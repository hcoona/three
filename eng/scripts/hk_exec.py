"""Generic wrapper script for running hk check tools with watchdog timeout."""

from __future__ import annotations

import contextlib
import os
import signal
import subprocess
import sys
import time
from datetime import datetime

from hk_file_operands import existing_operands

_MIN_QUOTED_LEN = 2
_DEFAULT_TIMEOUT_SECONDS = 120
_DEFAULT_HEARTBEAT_SECONDS = 30


def normalize_path_arg(value: str) -> str:
    """Strip surrounding quotes and whitespace from a file path."""
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
    raw = os.getenv(
        "HK_EXEC_TIMEOUT_SECONDS",
        str(_DEFAULT_TIMEOUT_SECONDS),
    ).strip()

    try:
        timeout_seconds = int(raw)
    except ValueError:
        timeout_seconds = _DEFAULT_TIMEOUT_SECONDS

    return max(1, timeout_seconds)


def resolve_heartbeat_seconds() -> int:
    """Read heartbeat interval from environment or default."""
    raw = os.getenv(
        "HK_EXEC_HEARTBEAT_SECONDS",
        str(_DEFAULT_HEARTBEAT_SECONDS),
    ).strip()

    try:
        heartbeat_seconds = int(raw)
    except ValueError:
        heartbeat_seconds = _DEFAULT_HEARTBEAT_SECONDS

    return max(5, heartbeat_seconds)


def parse_positive_seconds(value: str) -> int | None:
    """Parse a positive integer duration in seconds."""
    try:
        seconds = int(value)
    except ValueError:
        return None
    if seconds < 1:
        return None
    return seconds


def parse_wrapper_options(
    argv: list[str],
) -> tuple[int | None, list[str], str | None]:
    """Parse hk_exec.py options before the wrapped command."""
    timeout_seconds: int | None = None
    index = 0
    while index < len(argv):
        arg = argv[index]
        if arg == "--timeout-seconds":
            if index + 1 >= len(argv):
                return None, [], "--timeout-seconds requires a value"
            parsed = parse_positive_seconds(argv[index + 1])
            if parsed is None:
                return None, [], "--timeout-seconds must be a positive integer"
            timeout_seconds = parsed
            index += 2
            continue
        if arg.startswith("--timeout-seconds="):
            parsed = parse_positive_seconds(arg.split("=", 1)[1])
            if parsed is None:
                return None, [], "--timeout-seconds must be a positive integer"
            timeout_seconds = parsed
            index += 1
            continue
        break
    return timeout_seconds, argv[index:], None


def resolve_per_file_mode() -> bool:
    """Check whether opt-in per-file diagnostic execution is enabled."""
    return os.getenv("HK_EXEC_PER_FILE", "0").strip().lower() in {
        "1",
        "true",
        "yes",
        "on",
    }


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

    with contextlib.suppress(ProcessLookupError):
        os.killpg(process.pid, signal.SIGKILL)


def run_with_watchdog(  # noqa: PLR0913, PLR0917
    command: list[str],
    timeout_seconds: int,
    heartbeat_seconds: int,
    index: int,
    total: int,
    label: str,
) -> tuple[int | None, float, bool]:
    """Run a command with blocking wait, periodic heartbeat, and timeout."""
    process = subprocess.Popen(
        command,
        start_new_session=os.name != "nt",
    )

    start = time.monotonic()
    next_heartbeat = float(heartbeat_seconds)

    while True:
        elapsed = time.monotonic() - start
        remaining_timeout = timeout_seconds - elapsed
        if remaining_timeout <= 0:
            kill_process_tree(process)
            with contextlib.suppress(subprocess.TimeoutExpired):
                process.wait(timeout=5)
            return None, time.monotonic() - start, True

        wait_window = min(remaining_timeout, max(0.0, next_heartbeat - elapsed))
        try:
            returncode = process.wait(timeout=wait_window)
        except subprocess.TimeoutExpired:
            elapsed = time.monotonic() - start
            if elapsed >= next_heartbeat and elapsed < timeout_seconds:
                print(
                    f"[{now_iso()}] [{index}/{total}] still running"
                    f" elapsed={elapsed:.0f}s: {label}",
                    flush=True,
                )
                while next_heartbeat <= elapsed:
                    next_heartbeat += heartbeat_seconds
            continue

        return returncode, time.monotonic() - start, False


def split_command_and_files(
    argv: list[str],
) -> tuple[list[str], list[str]]:
    """Use an explicit file boundary, retaining the legacy separator form."""
    if "--hk-files" in argv:
        sep = argv.index("--hk-files")
        return argv[:sep], argv[sep + 1 :]
    if "--" not in argv:
        return argv, []

    sep = len(argv) - 1 - argv[::-1].index("--")
    return argv[:sep], argv[sep + 1 :]


def main() -> int:
    """Run hk check commands with watchdog supervision."""
    timeout_override, user_args, option_error = parse_wrapper_options(
        sys.argv[1:],
    )
    if option_error is not None:
        print(option_error, file=sys.stderr)
        raise SystemExit(2)
    cmd_args, file_args = split_command_and_files(
        user_args,
    )
    late_timeout_override, cmd_args, late_option_error = parse_wrapper_options(
        cmd_args,
    )
    if late_option_error is not None:
        print(late_option_error, file=sys.stderr)
        raise SystemExit(2)

    if not cmd_args:
        print(
            "hk_exec.py requires a command before '--'",
            file=sys.stderr,
        )
        return 2

    norm_files = [
        normalize_path_arg(p) for p in file_args if normalize_path_arg(p)
    ]
    norm_files = existing_operands(norm_files)
    if file_args and not norm_files:
        print("No existing file operands; skipping file check.")
        return 0

    timeout = (
        late_timeout_override or timeout_override or resolve_timeout_seconds()
    )
    return run_command(cmd_args, norm_files, timeout)


def _run_supervised_item(  # noqa: PLR0913, PLR0917
    command: list[str],
    cmd_str: str,
    label: str,
    timeout: int,
    heartbeat: int,
    index: int,
    total: int,
) -> int:
    print(
        f"[{now_iso()}] [{index}/{total}] {cmd_str} {label}"
        f" (timeout={timeout}s)",
        flush=True,
    )
    rc, elapsed, timed_out = run_with_watchdog(
        command,
        timeout,
        heartbeat,
        index,
        total,
        label,
    )
    if timed_out:
        print(
            f"[{now_iso()}] [{index}/{total}] timed out after {timeout}s:"
            f" {label}",
            file=sys.stderr,
            flush=True,
        )
        return 1

    print(
        f"[{now_iso()}] [{index}/{total}] finished exit={rc}"
        f" elapsed={elapsed:.1f}s",
        flush=True,
    )
    return 0 if rc is None else rc


def run_command(
    cmd_args: list[str],
    norm_files: list[str],
    timeout: int,
    heartbeat: int | None = None,
    *,
    allow_per_file: bool = True,
) -> int:
    """Execute an explicit file batch, opt-in per-file loop, or bare tool."""
    resolved_heartbeat = (
        resolve_heartbeat_seconds() if heartbeat is None else heartbeat
    )
    cmd_str = " ".join(cmd_args)
    if not norm_files:
        return _run_supervised_item(
            cmd_args,
            cmd_str,
            "(no explicit files)",
            timeout,
            resolved_heartbeat,
            1,
            1,
        )

    if allow_per_file and resolve_per_file_mode():
        has_error = False
        total = len(norm_files)
        for index, path in enumerate(norm_files, start=1):
            rc = _run_supervised_item(
                [*cmd_args, path],
                cmd_str,
                path,
                timeout,
                resolved_heartbeat,
                index,
                total,
            )
            has_error = has_error or rc != 0
        return 1 if has_error else 0

    count = len(norm_files)
    label = norm_files[0] if count == 1 else f"<batch {count} files>"
    return _run_supervised_item(
        [*cmd_args, *norm_files],
        cmd_str,
        label,
        timeout,
        resolved_heartbeat,
        1,
        1,
    )


if __name__ == "__main__":
    raise SystemExit(main())
