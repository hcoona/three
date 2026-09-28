# Runtime assertions are mandatory; campaign import rejects optimized Python.
# ruff: noqa: ANN401, PLR2004, PLR0913, S101, N818
"""Persistent pacing for ordinary scoped GETs, without renewable quotas."""

import signal
import ssl
from contextlib import contextmanager
from datetime import UTC, datetime, timedelta
from email.utils import parsedate_to_datetime
from http.client import IncompleteRead, RemoteDisconnected
from typing import Any

import campaign  # noqa: F401 - reject optimized execution


class ReadPending(RuntimeError):
    """Yield supervision until the retained next eligible request time."""

    def __init__(self, next_not_before: str) -> None:
        """Retain the durable resume time."""
        self.next_not_before = next_not_before
        super().__init__(f"read pending until {next_not_before}")


class ReadStopped(RuntimeError):
    """Stop reads pending independent diagnosis or original deadline expiry."""


@contextmanager
def deadline_guard(deadline: str, *, now: Any = None) -> Any:
    """Interrupt blocked read work at its original deadline."""
    clock = now or (lambda: datetime.now(UTC))
    left = (datetime.fromisoformat(deadline) - clock()).total_seconds()
    reason = "original read deadline exhausted"
    if left <= 0:
        raise ReadStopped(reason)
    assert signal.getitimer(signal.ITIMER_REAL) == (0.0, 0.0)
    previous = signal.getsignal(signal.SIGALRM)

    def expired(_signum: Any, _frame: Any) -> None:
        raise ReadStopped(reason)

    signal.signal(signal.SIGALRM, expired)
    signal.setitimer(signal.ITIMER_REAL, left)
    try:
        yield
        if clock() >= datetime.fromisoformat(deadline):
            expired(None, None)
    finally:
        signal.setitimer(signal.ITIMER_REAL, 0)
        signal.signal(signal.SIGALRM, previous)


def transient(status: Any, headers: Any, error: Any, *, github: bool) -> bool:
    """Retry only explicit temporary transport/service and rate failures."""
    headers = {k.lower(): v for k, v in (headers or {}).items()}
    if error is not None:
        return not isinstance(error, ssl.SSLError) and isinstance(
            error,
            (
                TimeoutError,
                ConnectionResetError,
                ConnectionAbortedError,
                IncompleteRead,
                RemoteDisconnected,
            ),
        )
    return status in (408, 429, 500, 502, 503, 504) or (
        github
        and status == 403
        and (
            headers.get("x-ratelimit-remaining") == "0"
            or "retry-after" in headers
        )
    )


class ReadPacer:
    """Persist request admission before transport; never sleep or reset time."""

    def __init__(
        self, state: dict, deadline: str, *, now: Any = None, save: Any = None
    ) -> None:
        """Bind persistent pacing state and its original deadline."""
        self.state = state
        self.deadline = datetime.fromisoformat(deadline)
        self.now = now or (lambda: datetime.now(UTC))
        self.save = save or (lambda: None)

    def stop(self, reason: str) -> None:
        """Retain a terminal read classification."""
        self.state["stopped"] = reason
        self.save()
        raise ReadStopped(reason)

    def before(self, kind: str) -> None:
        """Reserve one paced GET or yield before any transport effect."""
        assert kind in ("registry", "github", "transfer")
        if self.state.get("stopped"):
            raise ReadStopped(self.state["stopped"])
        now = self.now()
        due = datetime.fromisoformat(
            self.state.get("next_not_before", now.isoformat())
        )
        if now >= self.deadline or due >= self.deadline:
            self.stop("original read deadline exhausted")
        if now < due:
            raise ReadPending(due.isoformat())
        step = self.state.get("backoff_step", 0)
        gap = (30, 60, 120, 300)[min(step, 3)] if kind == "registry" else 30
        self.state.update(
            last_started=now.isoformat(),
            next_not_before=(now + timedelta(seconds=gap)).isoformat(),
        )
        if kind == "registry":
            self.state["backoff_step"] = step + 1
        self.state["requests"] = self.state.get("requests", 0) + 1
        self.save()

    def after(
        self,
        kind: str,
        *,
        status: Any = None,
        headers: Any = None,
        error: Any = None,
        github: bool = False,
        reset_errors: bool = True,
    ) -> None:
        """Record service pacing and distinguish transient from hard failure."""
        assert kind in ("registry", "github", "transfer")
        now = self.now()
        headers = {k.lower(): v for k, v in (headers or {}).items()}
        due = datetime.fromisoformat(
            self.state.get("next_not_before", now.isoformat())
        )
        service_due = datetime.fromisoformat(
            self.state.get("service_not_before", now.isoformat())
        )
        try:
            if "retry-after" in headers:
                value = headers["retry-after"]
                retry = (
                    now + timedelta(seconds=int(value))
                    if value.isdigit()
                    else parsedate_to_datetime(value)
                )
                assert retry.tzinfo is not None
                service_due = max(service_due, retry)
            if (
                headers.get("x-ratelimit-remaining") == "0"
                and "x-ratelimit-reset" in headers
            ):
                service_due = max(
                    service_due,
                    datetime.fromtimestamp(
                        int(headers["x-ratelimit-reset"]), UTC
                    ),
                )
            if "x-poll-interval" in headers:
                interval = int(headers["x-poll-interval"])
                assert interval >= 0
                service_due = max(
                    service_due,
                    now + timedelta(seconds=interval),
                )
        except (ValueError, TypeError, OverflowError, AssertionError):
            self.stop("malformed service pacing header")
        due = max(due, service_due)
        self.state["service_not_before"] = service_due.isoformat()
        self.state["next_not_before"] = due.isoformat()
        if error is None and status in (200, 201, 204, 301, 302, 303, 307, 308):
            if reset_errors:
                self.state["consecutive_errors"] = 0
            self.save()
            return
        if not transient(status, headers, error, github=github):
            reason = type(error).__name__ if error else status
            self.stop(f"non-transient read failure: {reason}")
        count = self.state.get("consecutive_errors", 0) + 1
        self.state["consecutive_errors"] = count
        gap = (30, 60, 120, 300)[min(count - 1, 3)]
        due = max(due, now + timedelta(seconds=gap))
        self.state["next_not_before"] = due.isoformat()
        self.save()
        if count >= 5:
            self.stop("five consecutive transient read failures")
        self.pending(kind)

    def pending(self, kind: str) -> None:
        """Yield a pending observation without resetting its polling history."""
        assert kind in ("registry", "github", "transfer")
        due = self.state["next_not_before"]
        if (
            datetime.fromisoformat(due) >= self.deadline
            or self.now() >= self.deadline
        ):
            self.stop("original read deadline exhausted")
        raise ReadPending(due)
