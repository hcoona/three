# Fixture identities and limits are literal expected contract values.
# Optimized-interpreter tests execute only the fixed local test script.
# ruff: noqa: E501, PLR2004, PLC0415, S603

"""Transport boundaries with controlled HTTP and credential seams."""

import json
from datetime import UTC, datetime, timedelta
from types import SimpleNamespace

import base_operator
import pytest
from base_operator import Operator as Base
from base_operator import OperatorDeadlineExceeded
from campaign import read
from read_policy import ReadPending, ReadStopped
from test_campaign import TARGET, construct, write_gate
from test_campaign import (
    campaign as campaign,  # noqa: PLC0414 - pytest fixture re-export.
)

API = "https://api.github.com/repos/hcoona/three"


class Reply:
    """Provide the controlled Reply fixture."""

    def __init__(self, status=200, body=b"{}", headers=()) -> None:
        """Provide the controlled   init   fixture."""
        self.status, self.body, self.headers = status, body, headers
        self.offset = 0

    def getheaders(self):
        """Provide the controlled getheaders fixture."""
        return self.headers

    def read(self, size):
        """Provide the controlled read fixture."""
        piece = self.body[self.offset : self.offset + size]
        self.offset += len(piece)
        return piece


@pytest.fixture
def wire(monkeypatch):
    """Provide the controlled wire fixture."""
    calls, replies, credentials = [], [], []

    class Connection:
        """Provide the controlled Connection fixture."""

        def __init__(self, host, timeout) -> None:
            """Provide the controlled   init   fixture."""
            self.host = host
            assert 0 < timeout <= 30

        def request(self, method, path, body, headers) -> None:
            """Provide the controlled request fixture."""
            calls.append((self.host, method, path, body, headers))
            if replies and isinstance(replies[0], BaseException):
                raise replies.pop(0)

        def getresponse(self):
            """Provide the controlled getresponse fixture."""
            return replies.pop(0) if replies else Reply()

        def close(self) -> None:
            """Provide the controlled close fixture."""

    def credential(argv, **kwargs):
        """Provide the controlled credential fixture."""
        assert argv == ["gh", "auth", "token"]
        assert kwargs["timeout"] <= 30
        credentials.append(argv)
        return SimpleNamespace(stdout="controlled-test-token\n")

    monkeypatch.setattr(
        base_operator.http.client, "HTTPSConnection", Connection
    )
    monkeypatch.setattr(base_operator.subprocess, "run", credential)
    return SimpleNamespace(
        calls=calls, replies=replies, credentials=credentials
    )


@pytest.fixture
def base(tmp_path):
    """Provide the controlled base fixture."""
    op = Base(tmp_path / "operation", TARGET)
    op.mode = "stop-after-wheel"
    return op


def test_dispatch_reserves_before_uncertain_send_and_cannot_retry(
    campaign, wire
) -> None:
    """Verify dispatch reserves before uncertain send and cannot retry."""
    op, _ = construct(campaign)
    wire.replies.append(ConnectionError("uncertain fixture send"))
    with pytest.raises(ConnectionError):
        op.dispatch()
    ledger = read(op.ledger_path)
    assert len(ledger["requests"]) == 1
    assert ledger["requests"][0]["send_started"] is True
    assert json.loads(wire.calls[0][3]) == {
        "ref": "main",
        "inputs": {
            "registry": "testpypi",
            "recovery-proof": "stop-after-wheel",
        },
    }
    with pytest.raises(AssertionError):
        op.dispatch()
    assert len(wire.calls) == 1


def test_approval_requires_current_gate_and_spends_single_missing_effect(
    campaign, wire
) -> None:
    """Verify approval requires current gate and spends single missing effect."""
    op, binding = construct(campaign)
    with op.deadline():
        op.ledger.update(run=123, preparation_audit_completed=op.now())
        op.save()
    with pytest.raises(FileNotFoundError):
        op.approve("fixture")
    assert not wire.calls
    write_gate(
        op.directory.parent,
        binding,
        "preapproval",
        run=123,
        dispositions=["upload", "upload"],
    )
    wire.replies.append(ConnectionError("uncertain approval fixture"))
    with pytest.raises(ConnectionError):
        op.approve("fixture")
    assert campaign.read()["scenarios"][0]["attempts"][0][
        "uploads_reserved"
    ] == ["wheel"]
    with pytest.raises(AssertionError):
        op.approve("fixture")
    assert len(wire.calls) == 1
    assert op.ledger["requests"][0]["category"] == "approval"


def test_artifact_redirect_hops_are_counted_without_credential_forwarding(
    base, wire
) -> None:
    """Verify artifact redirect hops are counted without credential forwarding."""
    wire.replies.extend(
        [
            Reply(
                302,
                b"",
                [
                    (
                        "Location",
                        "https://results-receiver.actions.githubusercontent.com/artifact?signature=fixture",
                    )
                ],
            ),
            Reply(200, b"immutable"),
        ]
    )
    raw = base.transfer(
        "artifact",
        "repos/hcoona/three/actions/artifacts/15/zip",
        "artifact-wheel",
    )
    assert raw == b"immutable"
    assert len(base.ledger["requests"]) == 2
    assert "Authorization" in wire.calls[0][4]
    assert "Authorization" not in wire.calls[1][4]
    assert len(wire.credentials) == 1
    assert (
        base.transfer(
            "artifact", "repos/hcoona/three/actions/artifacts/15/zip", "again"
        )
        == b"immutable"
    )
    assert len(wire.calls) == 2


@pytest.mark.parametrize(
    "url",
    [
        "https://evil.example/x",
        "http://results-receiver.actions.githubusercontent.com/x",
        "https://user:pass@api.github.com/repos/hcoona/three/x",
        "https://api.github.com/repos/other/repo/x",
    ],
)
def test_transport_rejects_unapproved_origin_without_credentials(
    base, wire, url
) -> None:
    """Verify transport rejects unapproved origin without credentials."""
    with pytest.raises(AssertionError):
        base.request("artifact", "blocked", url)
    assert wire.calls == wire.credentials == []
    assert base.ledger["requests"] == []


def test_control_redirect_and_failed_request_do_not_retry(base, wire) -> None:
    """Verify control redirect and failed request do not retry."""
    wire.replies.append(
        Reply(
            302,
            b"",
            [("Location", "https://api.github.com/repos/hcoona/three/other")],
        )
    )
    with pytest.raises(ReadStopped):
        base.get("repos/hcoona/three/anything", "redirect")
    assert len(wire.calls) == 1
    assert len(base.ledger["requests"]) == 1
    assert base.ledger["requests"][0]["status"] == 302


@pytest.mark.parametrize(
    ("category", "count"), [("json", 400), ("artifact", 128), ("log", 16)]
)
def test_get_request_counts_are_diagnostic_without_a_refill(
    base, wire, category, count
) -> None:
    """Former read ceilings do not prevent another scoped evidence GET."""
    base.ledger["requests"] = [{"category": category} for _ in range(count)]
    base.request(category, "next-evidence", API + "/x")
    assert len(wire.calls) == 1
    assert wire.calls[0][1] == "GET"
    assert len(base.ledger["requests"]) == count + 1
    assert base.ledger["requests"][-1]["status"] == 200


@pytest.mark.parametrize("status", [429, 503])
def test_dispatch_rate_or_service_failure_never_retries_post(
    campaign, wire, status
) -> None:
    """Read recovery cannot turn a transient-looking POST into a resend."""
    op, _ = construct(campaign)
    wire.replies.append(Reply(status, b"temporarily unavailable"))
    with pytest.raises(AssertionError):
        op.dispatch()
    with pytest.raises(AssertionError):
        op.dispatch()
    assert [call[1] for call in wire.calls] == ["POST"]
    assert [entry["status"] for entry in op.ledger["requests"]] == [status]
    assert len(campaign.read()["scenarios"][0]["attempts"]) == 1


def test_poll_spacing_survives_reentry_without_a_count_ceiling(
    base, wire, monkeypatch
) -> None:
    """Poll count is diagnostic; retained minimum spacing survives restart."""
    import read_policy

    current = [datetime.now(UTC)]

    class Clock(datetime):
        @classmethod
        def now(cls, _tz=None):
            return current[0]

    monkeypatch.setattr(base_operator.datetime, "datetime", Clock)
    monkeypatch.setattr(read_policy, "datetime", Clock)
    base.get("repos/hcoona/three/actions/runs/1", "poll", poll=True)
    with pytest.raises(AssertionError):
        base.get("repos/hcoona/three/actions/runs/1", "poll-again", poll=True)
    assert len(wire.calls) == 1
    old = (current[0] - timedelta(seconds=31)).isoformat()
    base.ledger["requests"] = [
        {"category": "json", "poll": True, "started": old} for _ in range(240)
    ]
    base.save()
    reopened = Base(base.directory, TARGET)
    with pytest.raises(ReadPending):
        reopened.get("repos/hcoona/three/actions/runs/1", "not-due", poll=True)
    current[0] += timedelta(seconds=30)
    reopened.get("repos/hcoona/three/actions/runs/1", "next-poll", poll=True)
    reopened.get("repos/hcoona/three/actions/runs/1/jobs", "required-evidence")
    assert len(reopened.ledger["requests"]) == 242
    assert len(wire.calls) == 3


def test_failed_immutable_transfer_retries_same_identity_then_reuses_verified_capture(
    base, wire, monkeypatch
):
    """Failed reads retain evidence; completed immutable bytes need no refill."""
    import read_policy

    current = [datetime.now(UTC)]

    class Clock(datetime):
        @classmethod
        def now(cls, _tz=None):
            return current[0]

    monkeypatch.setattr(base_operator.datetime, "datetime", Clock)
    monkeypatch.setattr(read_policy, "datetime", Clock)
    endpoint = "repos/hcoona/three/actions/artifacts/15/zip"
    wire.replies.append(Reply(503, b"temporary failure"))
    with pytest.raises(ReadPending):
        base.transfer("artifact", endpoint, "wheel")
    first = read(base.ledger_path)["requests"][0]
    assert (base.directory / first["body"]).read_bytes() == b"temporary failure"
    reopened = Base(base.directory, TARGET)
    with pytest.raises(ReadPending):
        reopened.transfer("artifact", endpoint, "wheel")
    assert len(wire.calls) == 1
    current[0] += timedelta(seconds=30)
    wire.replies.append(Reply(200, b"immutable wheel"))
    assert (
        reopened.transfer("artifact", endpoint, "wheel") == b"immutable wheel"
    )
    third = Base(base.directory, TARGET)
    assert third.transfer("artifact", endpoint, "wheel") == b"immutable wheel"
    assert [call[2] for call in wire.calls] == ["/" + endpoint, "/" + endpoint]
    assert len(third.ledger["transfers"]) == 1
    assert [entry["status"] for entry in third.ledger["requests"]] == [503, 200]
    retained = third.ledger["completed_transfers"][endpoint]
    (third.directory / retained["body"]).write_bytes(b"corrupted")
    with pytest.raises(AssertionError):
        third.transfer("artifact", endpoint, "wheel")
    assert len(wire.calls) == 2


def test_capture_reentry_reuses_exact_endpoint_and_rejects_changed_retained_bytes(
    base, wire
):
    """Capture replay cannot silently adopt another run or corrupted JSON."""
    endpoint = "repos/hcoona/three/actions/runs/1/jobs"
    wire.replies.append(Reply(200, b'{"jobs":[{"id":15}]}'))
    assert base.capture_get(endpoint, "jobs") == {"jobs": [{"id": 15}]}
    reopened = Base(base.directory, TARGET)
    assert reopened.capture_get(endpoint, "jobs") == {"jobs": [{"id": 15}]}
    with pytest.raises(AssertionError):
        reopened.capture_get("repos/hcoona/three/actions/runs/2/jobs", "jobs")
    capture = reopened.ledger["captured_json"]["jobs"]
    (reopened.directory / capture["body"]).write_bytes(b'{"jobs":[]}')
    with pytest.raises(AssertionError):
        reopened.capture_get(endpoint, "jobs")
    assert len(wire.calls) == 1


def test_transient_failure_breaker_cannot_be_reset_by_changing_endpoint(
    base, wire, monkeypatch
):
    """The read-stage breaker applies across requests, not just one URL."""
    import read_policy

    current = [datetime.now(UTC)]

    class Clock(datetime):
        @classmethod
        def now(cls, _tz=None):
            return current[0]

    monkeypatch.setattr(base_operator.datetime, "datetime", Clock)
    monkeypatch.setattr(read_policy, "datetime", Clock)
    for ordinal in range(1, 6):
        wire.replies.append(Reply(503, b"temporary failure"))
        with pytest.raises(
            ReadPending if ordinal < 5 else ReadStopped
        ) as error:
            base.get(
                f"repos/hcoona/three/actions/jobs/{ordinal}", f"job-{ordinal}"
            )
        assert len(wire.calls) == ordinal
        if ordinal < 5:
            expected = current[0] + timedelta(
                seconds=(30, 60, 120, 300)[ordinal - 1]
            )
            assert error.value.next_not_before == expected.isoformat()
            current[0] = expected
    reopened = Base(base.directory, TARGET)
    with pytest.raises(ReadStopped):
        reopened.get("repos/hcoona/three/actions/jobs/6", "another-endpoint")
    assert len(wire.calls) == 5
    assert [entry["status"] for entry in reopened.ledger["requests"]] == [
        503
    ] * 5


def test_response_body_and_cumulative_transfer_budget_are_enforced(
    base, wire
) -> None:
    """Verify response body and cumulative transfer budget are enforced."""
    base.ledger["requests"] = [
        {"category": "artifact", "bytes": (512 << 20) - 4}
    ]
    wire.replies.append(Reply(200, b"12345"))
    with pytest.raises(ReadStopped, match="AssertionError"):
        base.request("artifact", "oversize", API + "/actions/artifacts/1/zip")
    assert base.ledger["requests"][-1]["bytes"] == 5
    assert base.ledger["requests"][-1]["retained_bytes"] == 5
    with pytest.raises(AssertionError):
        base.request("artifact", "exhausted", API + "/actions/artifacts/2/zip")
    assert len(wire.calls) == 1


def test_successful_exhausted_rate_limit_blocks_other_endpoints_after_restart(
    base, wire, monkeypatch
):
    """A valid response cannot discard a service-wide future reset time."""
    import read_policy

    current = [datetime.now(UTC).replace(microsecond=0)]
    # Keep the fixture lifetime earlier than the controlled whole-second clock.
    base.ledger["started"] = (current[0] - timedelta(seconds=1)).isoformat()
    base.save()

    class Clock(datetime):
        @classmethod
        def now(cls, _tz=None):
            return current[0]

    monkeypatch.setattr(base_operator.datetime, "datetime", Clock)
    monkeypatch.setattr(read_policy, "datetime", Clock)
    reset = current[0] + timedelta(seconds=120)
    wire.replies.append(
        Reply(
            200,
            b'{"id":15}',
            [
                ("X-RateLimit-Remaining", "0"),
                ("X-RateLimit-Reset", str(int(reset.timestamp()))),
            ],
        )
    )
    assert base.get(
        "repos/hcoona/three/actions/jobs/15", "last-budgeted-read"
    ) == {"id": 15}
    reopened = Base(base.directory, TARGET)
    with pytest.raises(ReadPending) as pending:
        reopened.get("repos/hcoona/three/actions/jobs/16", "different-endpoint")
    assert pending.value.next_not_before == reset.isoformat()
    assert len(wire.calls) == len(wire.credentials) == 1
    assert len(reopened.ledger["requests"]) == 1
    current[0] = reset - timedelta(seconds=1)
    with pytest.raises(ReadPending):
        reopened.get("repos/hcoona/three/actions/jobs/16", "still-waiting")
    current[0] = reset
    wire.replies.append(Reply(200, b'{"id":16}'))
    assert reopened.get(
        "repos/hcoona/three/actions/jobs/16", "after-reset"
    ) == {"id": 16}
    assert len(wire.calls) == 2
    assert [entry["status"] for entry in reopened.ledger["requests"]] == [
        200,
        200,
    ]


def test_interrupted_successful_rate_persistence_keeps_wait_for_other_endpoints(
    base, wire, monkeypatch
):
    """The first persisted response-pacing state must already honor the reset."""
    import read_policy

    current = [datetime.now(UTC).replace(microsecond=0)]
    base.ledger["started"] = (current[0] - timedelta(seconds=1)).isoformat()
    base.save()

    class Clock(datetime):
        @classmethod
        def now(cls, _tz=None):
            return current[0]

    class Interrupted(BaseException):
        """Stop subsequent persistence as if this process had terminated."""

    monkeypatch.setattr(base_operator.datetime, "datetime", Clock)
    monkeypatch.setattr(read_policy, "datetime", Clock)
    reset = current[0] + timedelta(seconds=120)
    wire.replies.append(
        Reply(
            200,
            b'{"id":15}',
            [
                ("X-RateLimit-Remaining", "0"),
                ("X-RateLimit-Reset", str(int(reset.timestamp()))),
            ],
        )
    )
    persist = base.save
    interrupted = []

    def interrupt_after_pacing_persistence():
        if interrupted:
            raise Interrupted
        persist()
        if any(
            state.get("next_not_before") == reset.isoformat()
            and state.get("consecutive_errors") == 0
            for state in base.ledger.get("read_pacing", {}).values()
        ):
            interrupted.append(base.ledger_path.read_bytes())
            raise Interrupted

    monkeypatch.setattr(base, "save", interrupt_after_pacing_persistence)
    with pytest.raises(Interrupted):
        base.get("repos/hcoona/three/actions/jobs/15", "rate-response")
    assert len(interrupted) == 1
    assert base.ledger_path.read_bytes() == interrupted[0]
    reopened = Base(base.directory, TARGET)
    with pytest.raises(ReadPending) as pending:
        reopened.get("repos/hcoona/three/actions/jobs/16", "after-interruption")
    assert pending.value.next_not_before == reset.isoformat()
    assert len(wire.calls) == len(wire.credentials) == 1
    assert len(reopened.ledger["requests"]) == 1
    current[0] = reset
    wire.replies.append(Reply(200, b'{"id":16}'))
    assert reopened.get(
        "repos/hcoona/three/actions/jobs/16", "reset-reached"
    ) == {"id": 16}
    assert len(wire.calls) == 2


def test_artifact_id_limit_and_expiration_prevent_new_reads(base, wire) -> None:
    """Verify artifact id limit and expiration prevent new reads."""
    base.ledger["transfers"] = [
        f"repos/hcoona/three/actions/artifacts/{i}/zip" for i in range(32)
    ]
    with pytest.raises(AssertionError):
        base.transfer(
            "artifact", "repos/hcoona/three/actions/artifacts/33/zip", "extra"
        )
    base.ledger["started"] = (
        datetime.now(UTC) - timedelta(hours=4, seconds=1)
    ).isoformat()
    with pytest.raises(OperatorDeadlineExceeded):
        base.get("repos/hcoona/three/actions/runs/1", "late")
    assert wire.calls == []


@pytest.mark.usefixtures("wire")
def test_reservation_is_durable_before_credentials(base, monkeypatch) -> None:
    """Verify reservation is durable before credentials."""
    seen = []
    original = base_operator.subprocess.run

    def inspect(argv, **kwargs):
        """Provide the controlled inspect fixture."""
        ledger = read(base.ledger_path)
        seen.append(ledger["requests"][-1].copy())
        return original(argv, **kwargs)

    monkeypatch.setattr(base_operator.subprocess, "run", inspect)
    base.get("repos/hcoona/three/actions/runs/1", "reserved")
    assert seen[0]["status"] == "pending"
    assert seen[0]["send_started"] is False
    assert read(base.ledger_path)["requests"][0]["send_started"] is True


@pytest.mark.parametrize("optimization", ["-O", "-OO"])
@pytest.mark.parametrize(
    "module",
    [
        "dispatch_normal",
        "approve_normal",
        "collect_normal",
        "capture_preparation",
        "capture_terminal",
        "audit_preparation",
        "audit_terminal",
        "audit_registry",
        "normal_operator",
        "base_operator",
        "read_policy",
        "registry_read",
        "read_continuation",
    ],
)
def test_optimized_python_refuses_before_any_state_or_effect(
    tmp_path, optimization, module
):
    """Reject optimized callers before credentials, transport or state creation."""
    import os
    import subprocess
    import sys
    from pathlib import Path

    source = Path(__file__).resolve().parents[1]
    script = """
import importlib, socket, ssl, subprocess, sys
from pathlib import Path

def denied(*args, **kwargs):
    raise SystemExit("external effect reached before optimized-mode refusal")
socket.socket = denied
subprocess.run = denied
subprocess.check_output = denied
try:
    importlib.import_module(sys.argv[1])
except RuntimeError as error:
    if str(error) != "Hosted operator requires non-optimized Python; assertions are mandatory.":
        raise
    print("optimized caller refused")
else:
    raise SystemExit("optimized caller unexpectedly admitted")
"""
    environment = dict(os.environ, PYTHONPATH=str(source))
    completed = subprocess.run(
        [sys.executable, optimization, "-c", script, module],
        cwd=tmp_path,
        env=environment,
        capture_output=True,
        text=True,
        timeout=30,
        check=False,
    )
    assert completed.returncode == 0, completed.stderr
    assert completed.stdout.strip() == "optimized caller refused"
    assert list(tmp_path.iterdir()) == []
