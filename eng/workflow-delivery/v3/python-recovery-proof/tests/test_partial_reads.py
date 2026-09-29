# HTTP classifications and wait intervals are accepted protocol fixtures.
# Tables supply independent HTTP facts and controlled fixtures.
# ruff: noqa: PLR2004, PLC0414, PLR0913, PLR0917

"""Known response status remains authoritative after an incomplete body."""

from datetime import datetime, timedelta

import base_operator
import pytest
import read_policy
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
