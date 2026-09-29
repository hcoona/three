# Protocol timings and byte budgets are intentional expected values.
# ruff: noqa: PLC0414, PLR2004

"""Bounded stdlib reads must distinguish early EOF from complete responses."""

import io
from datetime import datetime, timedelta
from http.client import HTTPResponse
from types import SimpleNamespace

import pytest
import registry_read
from base_operator import Operator
from campaign import read
from read_policy import ReadPending, ReadStopped
from test_campaign import TARGET
from test_operator import base as base
from test_operator import wire as wire
from test_partial_reads import partial_clock as partial_clock
from test_registry_read import audit_fixture as audit_fixture
from test_registry_read import index
from test_registry_read import native_pair as native_pair


def stdlib_response(body, *, declared=None, status=200, headers=()):
    """Use real HTTPResponse bounded reads over an in-memory byte stream."""
    fields = list(headers)
    if declared is not None:
        fields.append(("Content-Length", str(declared)))
    raw = f"HTTP/1.1 {status} fixture\r\n".encode()
    raw += b"".join(f"{name}: {value}\r\n".encode() for name, value in fields)
    raw += b"\r\n" + body
    response = HTTPResponse(
        SimpleNamespace(makefile=lambda *_args: io.BytesIO(raw))
    )
    response.begin()
    return response


@pytest.fixture
def registry_connection(monkeypatch):
    """Replace only HTTPS transport with finite real stdlib response objects."""
    replies, calls, closed = [], [], []

    class Connection:
        def __init__(self, host, **_kwargs):
            self.host = host

        def request(self, method, path, **_kwargs):
            calls.append((method, self.host, path))

        def getresponse(self):
            assert replies, "unexpected registry request"
            return replies.pop(0)

        def close(self):
            closed.append(self.host)

    monkeypatch.setattr(
        registry_read.http.client, "HTTPSConnection", Connection
    )
    return SimpleNamespace(replies=replies, calls=calls, closed=closed)


@pytest.mark.parametrize("category", ["artifact", "log"])
def test_silent_eof_transfer_waits_then_reuses_same_immutable_identity(
    base,
    wire,
    partial_clock,
    category,
):
    """A short200 response cannot become a cached successful artifact or log."""
    endpoint = (
        "repos/hcoona/three/actions/artifacts/15/zip"
        if category == "artifact"
        else "repos/hcoona/three/actions/runs/15/logs"
    )
    prefix, complete = b"prefix", b"prefix-complete-immutable-bytes"
    wire.replies.append(
        stdlib_response(
            prefix, declared=len(complete), headers=[("Retry-After", "180")]
        )
    )
    expected_wait = partial_clock[0] + timedelta(seconds=180)
    with pytest.raises(ReadPending) as pending:
        base.transfer(category, endpoint, "immutable-eof")
    assert pending.value.next_not_before == expected_wait.isoformat()
    ledger = read(base.ledger_path)
    failed = ledger["requests"][0]
    assert failed["status"] == 200
    assert failed["error_type"] == "IncompleteRead"
    assert (base.directory / failed["body"]).read_bytes() == prefix
    assert failed["retained_bytes"] == len(prefix)
    assert endpoint not in ledger["completed_transfers"]
    assert ledger["transfers"] == [endpoint]
    resumed = Operator(base.directory, TARGET)
    with pytest.raises(ReadPending):
        resumed.transfer(category, endpoint, "immutable-eof")
    assert len(wire.calls) == 1
    partial_clock[0] = expected_wait
    wire.replies.append(stdlib_response(complete, declared=len(complete)))
    assert resumed.transfer(category, endpoint, "immutable-eof") == complete
    again = Operator(base.directory, TARGET)
    assert again.transfer(category, endpoint, "immutable-eof") == complete
    assert [call[2] for call in wire.calls] == ["/" + endpoint, "/" + endpoint]
    assert again.ledger["transfers"] == [endpoint]
    assert (base.directory / failed["body"]).read_bytes() == prefix


def test_registry_silent_eof_preserves_parseable_prefix_until_paced_recovery(
    audit_fixture,
    registry_connection,
):
    """Even a parseable index is incomplete when declared bytes are missing."""
    fixture, connection = audit_fixture, registry_connection
    raw_index = index(fixture.registry, fixture.pair[:1], 201)
    connection.replies.append(
        stdlib_response(
            raw_index,
            declared=len(raw_index) + 10,
            headers=[
                ("Content-Type", "application/vnd.pypi.simple.v1+json"),
                ("Retry-After", "180"),
            ],
        )
    )
    caller = fixture.construct()
    original_deadline = caller.deadline
    caller.wire = registry_read.https_get
    expected_wait = fixture.clock[0] + timedelta(seconds=180)
    with pytest.raises(ReadPending) as pending:
        caller.step()
    assert pending.value.next_not_before == expected_wait.isoformat()
    ledger = read(fixture.path)
    entry = ledger["requests"][0]
    assert entry["status"] == 200
    assert entry["error"] == "IncompleteRead"
    assert (caller.directory / entry["body"]).read_bytes() == raw_index
    assert entry["bytes"] == len(raw_index)
    assert read(caller.directory / entry["headers"])["retry-after"] == "180"
    assert ledger["classification"] == "pending"
    assert not ledger.get("observations")
    resumed = fixture.construct(deadline=original_deadline)
    resumed.wire = registry_read.https_get
    with pytest.raises(ReadPending):
        resumed.step()
    assert len(connection.calls) == 1
    fixture.clock[0] = datetime.fromisoformat(pending.value.next_not_before)
    connection.replies.extend(
        [
            stdlib_response(
                raw_index,
                declared=len(raw_index),
                headers=[
                    ("Content-Type", "application/vnd.pypi.simple.v1+json")
                ],
            ),
            stdlib_response(
                fixture.pair[0].content, declared=len(fixture.pair[0].content)
            ),
        ]
    )
    assert resumed.step().files == fixture.pair[:1]
    after = read(fixture.path)
    assert after["classification"] == "complete"
    assert after["requests"][0] == entry
    assert len(connection.calls) == 3
    assert resumed.deadline == original_deadline


@pytest.mark.parametrize(
    ("body", "declared"), [(b"abc", 3), (b"abc", None), (b"", 0)]
)
def test_stdlib_complete_or_eof_framed_transfer_is_accepted(
    base, wire, body, declared
):
    """Exact, EOF-framed and empty responses remain successful reads."""
    wire.replies.append(stdlib_response(body, declared=declared))
    endpoint = "repos/hcoona/three/actions/artifacts/15/zip"
    assert base.transfer("artifact", endpoint, "complete") == body
    assert endpoint in read(base.ledger_path)["completed_transfers"]
    assert len(wire.calls) == 1


@pytest.mark.parametrize(
    ("body", "declared"), [(b"abc", 3), (b"abc", None), (b"", 0)]
)
def test_stdlib_complete_or_eof_framed_registry_wire_is_accepted(
    registry_connection, body, declared
):
    """Transport completeness does not demand a declared body length."""
    connection = registry_connection
    connection.replies.append(stdlib_response(body, declared=declared))
    status, _headers, received = registry_read.https_get(
        "https://test.pypi.org/simple/example/", {}, 8
    )
    assert status == 200
    assert received == body
    assert len(connection.calls) == len(connection.closed) == 1


def test_stdlib_oversize_transfer_remains_hard_failure(base, wire):
    """The intentional size cutoff is not a premature EOF retry condition."""
    base.ledger["requests"] = [
        {"category": "artifact", "bytes": (512 << 20) - 4}
    ]
    wire.replies.append(stdlib_response(b"12345678", declared=10))
    endpoint = "repos/hcoona/three/actions/artifacts/15/zip"
    with pytest.raises(ReadStopped, match="AssertionError"):
        base.transfer("artifact", endpoint, "oversize")
    ledger = read(base.ledger_path)
    entry = ledger["requests"][-1]
    assert entry["error_type"] == "AssertionError"
    assert (base.directory / entry["body"]).read_bytes() == b"12345"
    assert endpoint not in ledger["completed_transfers"]
    assert len(wire.calls) == 1


def test_stdlib_oversize_registry_read_remains_hard_failure(
    audit_fixture, registry_connection
):
    """A registry byte bound remains a hard failure."""
    fixture, connection = audit_fixture, registry_connection
    body = b"x" * ((2 << 20) + 2)
    connection.replies.append(stdlib_response(body, declared=len(body) + 10))
    caller = fixture.construct()
    caller.wire = registry_read.https_get
    with pytest.raises(AssertionError, match="registry response body limit"):
        caller.step()
    ledger = read(fixture.path)
    assert ledger["classification"] == "stopped"
    assert ledger["requests"][0]["bytes"] == (2 << 20) + 1
    assert ledger["requests"][0].get("error") != "IncompleteRead"
    assert len(connection.calls) == 1
