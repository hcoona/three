# Expected pacing values are literal protocol boundaries.
# ruff: noqa: PLR2004

"""Retained pacing behavior with a deterministic clock and no network."""

import ssl
from datetime import UTC, datetime, timedelta
from email.utils import format_datetime

import pytest
from campaign import read, save_json
from read_policy import ReadPacer, ReadPending, ReadStopped


@pytest.fixture
def clock():
    """Supply a mutable UTC clock without real sleeps."""
    return [datetime(2026, 9, 28, 23, tzinfo=UTC)]


def test_registry_backoff_and_original_deadline_survive_reentry(
    tmp_path, clock
):
    """A valid stale response and a restart never reset the polling ladder."""
    path = tmp_path / "pacing.json"
    deadline = (clock[0] + timedelta(hours=4)).isoformat()
    save_json(path, {})
    for ordinal, gap in enumerate((30, 60, 120, 300, 300), start=1):
        state = read(path)
        pacer = ReadPacer(
            state,
            deadline,
            now=lambda: clock[0],
            save=lambda state=state: save_json(path, state),
        )
        start = clock[0]
        pacer.before("registry")
        pacer.after("registry", status=200)
        with pytest.raises(ReadPending) as pending:
            pacer.pending("registry")
        assert (
            pending.value.next_not_before
            == (start + timedelta(seconds=gap)).isoformat()
        )
        assert read(path)["requests"] == ordinal
        assert read(path)["consecutive_errors"] == 0
        reopened = ReadPacer(read(path), deadline, now=lambda: clock[0])
        with pytest.raises(ReadPending):
            reopened.before("registry")
        assert reopened.state["requests"] == ordinal
        clock[0] += timedelta(seconds=gap)
    clock[0] = datetime.fromisoformat(deadline)
    with pytest.raises(ReadStopped, match="deadline"):
        ReadPacer(read(path), deadline, now=lambda: clock[0]).before("registry")


@pytest.mark.parametrize("header", ["seconds", "date", "reset", "poll"])
def test_longer_service_wait_survives_restart_and_shorter_wait_cannot_reduce_it(
    clock, header
):
    """Service-directed waits take the maximum with the retained local wait."""
    expected = clock[0] + timedelta(seconds=180)
    headers = {
        "seconds": {"Retry-After": "180"},
        "date": {"Retry-After": format_datetime(expected)},
        "reset": {
            "X-RateLimit-Remaining": "0",
            "X-RateLimit-Reset": str(int(expected.timestamp())),
        },
        "poll": {"X-Poll-Interval": "180"},
    }[header]
    state = {}
    deadline = (clock[0] + timedelta(hours=4)).isoformat()
    pacer = ReadPacer(state, deadline, now=lambda: clock[0])
    pacer.before("github")
    pacer.after("github", status=200, headers=headers, github=True)
    resumed = ReadPacer(state, deadline, now=lambda: clock[0])
    resumed.after(
        "github", status=200, headers={"Retry-After": "1"}, github=True
    )
    with pytest.raises(ReadPending) as pending:
        resumed.before("github")
    assert pending.value.next_not_before == expected.isoformat()
    assert state["requests"] == 1
    clock[0] = expected
    resumed.before("github")
    assert state["requests"] == 2


def test_five_consecutive_transient_errors_stop_across_reentry(clock):
    """Five failures spend the read stage; a fresh object cannot clear it."""
    state = {}
    deadline = (clock[0] + timedelta(hours=4)).isoformat()
    for ordinal in range(1, 6):
        pacer = ReadPacer(state, deadline, now=lambda: clock[0])
        pacer.before("github")
        with pytest.raises(ReadPending if ordinal < 5 else ReadStopped):
            pacer.after("github", status=503)
        assert state["consecutive_errors"] == ordinal
        assert state["requests"] == ordinal
        clock[0] = datetime.fromisoformat(state["next_not_before"])
    with pytest.raises(ReadStopped, match="five consecutive"):
        ReadPacer(state, deadline, now=lambda: clock[0]).before("github")
    assert state["requests"] == 5


def test_valid_regression_resets_only_error_streak(clock):
    """A valid pending index clears transport failures, preserving backoff."""
    state = {}
    pacer = ReadPacer(
        state, (clock[0] + timedelta(hours=4)).isoformat(), now=lambda: clock[0]
    )
    pacer.before("registry")
    with pytest.raises(ReadPending):
        pacer.after("registry", error=TimeoutError())
    clock[0] = datetime.fromisoformat(state["next_not_before"])
    pacer.before("registry")
    pacer.after("registry", status=200)
    with pytest.raises(ReadPending):
        pacer.pending("registry")
    assert state["consecutive_errors"] == 0
    assert state["backoff_step"] == 2
    assert datetime.fromisoformat(state["next_not_before"]) == clock[
        0
    ] + timedelta(seconds=60)


@pytest.mark.parametrize(
    ("status", "error"),
    [
        (401, None),
        (403, None),
        (None, ssl.SSLError("invalid trust")),
        (None, ValueError("invalid content")),
    ],
)
def test_hard_failures_stop_before_any_retry(clock, status, error):
    """Permission, TLS and invalid data are never transient retries."""
    state = {}
    pacer = ReadPacer(
        state, (clock[0] + timedelta(hours=4)).isoformat(), now=lambda: clock[0]
    )
    pacer.before("github")
    with pytest.raises(ReadStopped, match="non-transient"):
        pacer.after("github", status=status, error=error, github=True)
    clock[0] += timedelta(minutes=10)
    with pytest.raises(ReadStopped):
        pacer.before("github")
    assert state["requests"] == 1


def test_recognized_github_rate_limit_waits_but_registry_403_stops(clock):
    """GitHub rate evidence cannot excuse a registry permission error."""
    headers = {"X-RateLimit-Remaining": "0", "Retry-After": "120"}
    deadline = (clock[0] + timedelta(hours=4)).isoformat()
    github = ReadPacer({}, deadline, now=lambda: clock[0])
    github.before("github")
    with pytest.raises(ReadPending):
        github.after("github", status=403, headers=headers, github=True)
    assert github.state["consecutive_errors"] == 1
    registry = ReadPacer({}, deadline, now=lambda: clock[0])
    registry.before("registry")
    with pytest.raises(ReadStopped):
        registry.after("registry", status=403, headers=headers)
    assert registry.state["requests"] == 1


def test_wait_reaching_original_deadline_stops_without_another_request(clock):
    """A service wait at the deadline yields incomplete evidence, no refill."""
    deadline = clock[0] + timedelta(seconds=90)
    state = {}
    pacer = ReadPacer(state, deadline.isoformat(), now=lambda: clock[0])
    pacer.before("registry")
    with pytest.raises(ReadStopped, match="deadline"):
        pacer.after("registry", status=429, headers={"Retry-After": "90"})
    assert state["requests"] == 1
    assert state["next_not_before"] == deadline.isoformat()
    with pytest.raises(ReadStopped):
        ReadPacer(state, deadline.isoformat(), now=lambda: clock[0]).before(
            "registry"
        )
    assert state["requests"] == 1
