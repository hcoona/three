# HTTP classifications and wait intervals are accepted protocol fixtures.
# Tables supply independent HTTP facts and controlled fixtures.
# ruff: noqa: PLR2004, PLC0414, PLR0913, PLR0917

"""Known response status remains authoritative after an incomplete body."""

import signal
from datetime import datetime, timedelta

import base_operator
import pytest
import read_policy
import registry_read
from base_operator import Operator
from campaign import read
from read_policy import ReadPending, ReadStopped
from test_campaign import TARGET
from test_operator import Reply
from test_operator import base as base
from test_operator import wire as wire
from test_registry_read import audit_fixture as audit_fixture
from test_registry_read import native_pair as native_pair


class PartialReply(Reply):
    """Return received body bytes before a transfer timeout."""

    def read(self, size):
        """Return the received prefix, then reproduce a body timeout."""
        if self.offset >= len(self.body):
            message = "body transfer interrupted"
            raise TimeoutError(message)
        return super().read(size)


@pytest.fixture
def partial_clock(base, monkeypatch):
    """Advance pacing without sleeping or changing the original lifetime."""
    current = [
        datetime.fromisoformat(base.ledger["started"]) + timedelta(seconds=1)
    ]

    class Clock(datetime):
        @classmethod
        def now(cls, _tz=None):
            return current[0]

    monkeypatch.setattr(base_operator.datetime, "datetime", Clock)
    monkeypatch.setattr(read_policy, "datetime", Clock)
    return current


@pytest.mark.parametrize(
    ("status", "headers", "wait"),
    [
        (None, {}, 30),
        (200, {}, 30),
        (503, {"Retry-After": "180"}, 180),
        (401, {}, None),
        (403, {}, None),
        (403, {"X-RateLimit-Remaining": "0", "Retry-After": "180"}, 180),
    ],
)
def test_partial_github_response_preserves_status_wait_and_reentry(
    base, wire, partial_clock, status, headers, wait
):
    """Known denial stops; transient partial reads keep service waits."""
    wire.replies.append(
        TimeoutError("before response")
        if status is None
        else PartialReply(
            status, b"partial received bytes", list(headers.items())
        )
    )
    with pytest.raises(ReadStopped if wait is None else ReadPending) as error:
        base.get("repos/hcoona/three/actions/jobs/15", "partial-body")
    entry = read(base.ledger_path)["requests"][0]
    if status is not None:
        assert entry["status"] == status
        assert (
            base.directory / entry["body"]
        ).read_bytes() == b"partial received bytes"
    else:
        assert entry["status"] in (None, "pending")
    reopened = Operator(base.directory, TARGET)
    with pytest.raises(ReadStopped if wait is None else ReadPending):
        reopened.get("repos/hcoona/three/actions/jobs/16", "different-endpoint")
    assert len(wire.calls) == len(wire.credentials) == 1
    if wait is not None:
        expected = partial_clock[0] + timedelta(seconds=wait)
        assert error.value.next_not_before == expected.isoformat()
        partial_clock[0] = expected
        wire.replies.append(Reply(200, b'{"id":16}'))
        assert reopened.get(
            "repos/hcoona/three/actions/jobs/16", "resumed"
        ) == {"id": 16}
        assert len(wire.calls) == 2


@pytest.mark.parametrize(
    ("status", "headers", "wait"),
    [
        (None, {}, 30),
        (200, {}, 30),
        (503, {"Retry-After": "180"}, 180),
        (401, {}, None),
        (403, {}, None),
        (403, {"X-RateLimit-Remaining": "0", "Retry-After": "180"}, None),
    ],
)
def test_partial_registry_response_preserves_known_status_and_received_evidence(
    audit_fixture, status, headers, wait
):
    """A body timeout cannot override HTTP denial or erase received bytes."""
    fixture = audit_fixture
    failure = TimeoutError("incomplete registry response")
    failure.retained_status = status
    failure.retained_headers = headers
    failure.retained_body = (
        b"partial registry body" if status is not None else b""
    )
    fixture.replies.append(failure)
    with pytest.raises(ReadStopped if wait is None else ReadPending) as error:
        fixture.construct().step()
    ledger = read(fixture.path)
    assert ledger["requests"][0]["status"] == status
    body = fixture.root / "audit" / ledger["requests"][0]["body"]
    assert body.read_bytes() == failure.retained_body
    with pytest.raises(ReadStopped if wait is None else ReadPending):
        fixture.construct().step()
    assert len(fixture.calls) == 1
    if wait is not None:
        assert (
            error.value.next_not_before
            == (fixture.clock[0] + timedelta(seconds=wait)).isoformat()
        )
        assert ledger["classification"] == "pending"
    else:
        assert ledger["classification"] == "stopped"


@pytest.mark.parametrize("status", [None, 200, 503])
def test_registry_deadline_retains_received_facts_and_rethrows_without_retry(
    audit_fixture, monkeypatch, status
):
    """Termination preserves received evidence and the original deadline."""
    fixture = audit_fixture
    deadline = fixture.clock[0] + timedelta(seconds=60)
    terminated = base_operator.OperatorDeadlineExceeded(
        "original deadline exhausted"
    )
    calls, closed = [], []

    class DeadlineReply(Reply):
        def read(self, size):
            if self.offset:
                fixture.clock[0] = deadline
                raise terminated
            return super().read(size)

    class Connection:
        def __init__(self, host, **_kwargs):
            self.host = host

        def request(self, method, path, **_kwargs):
            calls.append((method, self.host, path))

        def getresponse(self):
            if status is None:
                fixture.clock[0] = deadline
                raise terminated
            return DeadlineReply(
                status,
                b"received partial index",
                [
                    ("Content-Type", "application/vnd.pypi.simple.v1+json"),
                    ("Retry-After", "180"),
                    ("Set-Cookie", "must-not-be-retained"),
                ],
            )

        def close(self):
            closed.append(self.host)

    monkeypatch.setattr(
        registry_read.http.client, "HTTPSConnection", Connection
    )
    caller = fixture.construct(deadline=deadline.isoformat())
    caller.wire = registry_read.https_get
    with pytest.raises(base_operator.OperatorDeadlineExceeded) as error:
        caller.step()
    assert error.value is terminated
    assert terminated.retained_status == status
    expected_body = b"" if status is None else b"received partial index"
    assert terminated.retained_body == expected_body
    ledger = read(fixture.path)
    assert len(ledger["requests"]) == 1
    entry = ledger["requests"][0]
    assert entry["status"] == status
    assert entry["error"] == "OperatorDeadlineExceeded"
    assert (caller.directory / entry["body"]).read_bytes() == expected_body
    assert read(caller.directory / entry["headers"]) == (
        {}
        if status is None
        else {
            "content-type": "application/vnd.pypi.simple.v1+json",
            "retry-after": "180",
        }
    )
    assert ledger["classification"] == "stopped"
    assert not ledger.get("observations")
    assert not ledger.get("current_responses")
    assert "destination_state" not in ledger
    assert caller.deadline == deadline.isoformat()
    assert fixture.clock[0] == deadline
    assert calls == [
        ("GET", "test.pypi.org", "/simple/hcoona-release-smoke-python/")
    ]
    assert closed == ["test.pypi.org"]
    resumed = fixture.construct(deadline=deadline.isoformat())
    resumed.wire = registry_read.https_get
    with pytest.raises(ReadStopped):
        resumed.step()
    assert read(fixture.path) == ledger
    assert len(calls) == 1


def test_registry_wire_terminating_partial_bytes_remain_bounded(monkeypatch):
    """Exception-provided partial bytes cannot bypass the raw response bound."""
    terminated = base_operator.OperatorDeadlineExceeded(
        "original deadline exhausted"
    )
    terminated.partial = b"more-partial-bytes-than-admitted"
    closed = []

    class DeadlineReply(Reply):
        def read(self, size):
            if self.offset:
                raise terminated
            return super().read(size)

    class Connection:
        def __init__(self, *_args, **_kwargs):
            pass

        def request(self, *_args, **_kwargs):
            pass

        def getresponse(self):
            return DeadlineReply(200, b"part")

        def close(self):
            closed.append(True)

    monkeypatch.setattr(
        registry_read.http.client, "HTTPSConnection", Connection
    )
    with pytest.raises(base_operator.OperatorDeadlineExceeded) as error:
        registry_read.https_get("https://test.pypi.org/simple/example/", {}, 8)
    assert error.value is terminated
    assert terminated.retained_status == 200
    assert terminated.retained_body == b"partmore-"
    assert closed == [True]


@pytest.mark.parametrize("status", [200, 503])
def test_github_deadline_preserves_exception_partial_bytes_and_owner_marker(
    base, wire, partial_clock, status
):
    """Terminating GET evidence reaches the owning deadline context."""
    started = base.ledger["started"]
    deadline = datetime.fromisoformat(started) + timedelta(hours=4)
    terminated = base_operator.OperatorDeadlineExceeded(
        "original deadline exhausted"
    )

    class DeadlineReply(Reply):
        def read(self, size):
            if self.offset:
                partial_clock[0] = deadline
                raise terminated
            return super().read(size)

    wire.replies.append(DeadlineReply(status, b"partial GitHub evidence"))
    with (
        pytest.raises(base_operator.OperatorDeadlineExceeded) as error,
        base.deadline(),
    ):
        base.get("repos/hcoona/three/actions/jobs/15", "deadline-body")
    assert error.value is terminated
    ledger = read(base.ledger_path)
    assert ledger["deadline_exhausted_at"] == deadline.isoformat()
    assert ledger["started"] == started
    assert len(ledger["requests"]) == 1
    entry = ledger["requests"][0]
    assert entry["status"] == status
    assert entry["error_type"] == "OperatorDeadlineExceeded"
    assert (
        base.directory / entry["body"]
    ).read_bytes() == b"partial GitHub evidence"
    assert entry["retained_bytes"] == len(b"partial GitHub evidence")
    assert len(wire.calls) == len(wire.credentials) == 1
    resumed = Operator(base.directory, TARGET)
    with pytest.raises(base_operator.OperatorDeadlineExceeded):
        resumed.get("repos/hcoona/three/actions/jobs/16", "forbidden-resume")
    assert len(wire.calls) == len(wire.credentials) == 1
    assert read(base.ledger_path) == ledger


def test_continuation_guard_preserves_original_stop_and_partial_response(
    audit_fixture, monkeypatch
):
    """The continuation deadline cause survives wire and audit layers."""
    fixture = audit_fixture
    deadline = fixture.clock[0] + timedelta(seconds=60)
    terminated, calls, closed = [], [], []

    class GuardReply(Reply):
        def read(self, size):
            if self.offset:
                fixture.clock[0] = deadline
                handler = signal.getsignal(signal.SIGALRM)
                try:
                    handler(signal.SIGALRM, None)
                except ReadStopped as error:
                    terminated.append(error)
                    raise
            return super().read(size)

    class Connection:
        def __init__(self, *_args, **_kwargs):
            pass

        def request(self, method, _path, **_kwargs):
            calls.append(method)

        def getresponse(self):
            return GuardReply(
                200,
                b"partial deadline evidence",
                [("Content-Type", "application/json")],
            )

        def close(self):
            closed.append(True)

    monkeypatch.setattr(
        registry_read.http.client, "HTTPSConnection", Connection
    )
    caller = fixture.construct(deadline=deadline.isoformat())
    caller.wire = registry_read.https_get
    with (
        pytest.raises(
            ReadStopped, match=r"^original read deadline exhausted$"
        ) as error,
        read_policy.deadline_guard(
            deadline.isoformat(), now=lambda: fixture.clock[0]
        ),
    ):
        caller.step()
    assert error.value is terminated[0]
    ledger = read(fixture.path)
    assert (
        ledger["reason"]
        == ledger["pacing"]["stopped"]
        == "original read deadline exhausted"
    )
    assert ledger["classification"] == "stopped"
    assert len(ledger["requests"]) == 1
    entry = ledger["requests"][0]
    assert entry["status"] == 200
    assert entry["error"] == "ReadStopped"
    assert (
        caller.directory / entry["body"]
    ).read_bytes() == b"partial deadline evidence"
    assert read(caller.directory / entry["headers"]) == {
        "content-type": "application/json"
    }
    assert not ledger.get("observations")
    assert caller.deadline == deadline.isoformat()
    assert fixture.clock[0] == deadline
    assert calls == ["GET"]
    assert closed == [True]
    assert signal.getitimer(signal.ITIMER_REAL) == (0.0, 0.0)
    resumed = fixture.construct(deadline=deadline.isoformat())
    resumed.wire = registry_read.https_get
    with pytest.raises(
        ReadStopped, match=r"^original read deadline exhausted$"
    ):
        resumed.step()
    assert read(fixture.path) == ledger
    assert calls == ["GET"]
