# Fixture identities and intervals are accepted protocol boundaries.
# ruff: noqa: PLR2004
"""Actual preapproval with retained pacing and controlled transport."""

import hashlib
import importlib.util
import json
import sys
from datetime import UTC, datetime, timedelta
from pathlib import Path
from types import SimpleNamespace

import base_operator
import campaign as campaign_module
import normal_operator
import pytest
import read_policy
from base_operator import Operator as Base
from base_operator import OperatorDeadlineExceeded
from campaign import digest, read, save_json
from read_policy import ReadPending, ReadStopped
from test_campaign import TARGET, construct, write_gate
from test_campaign import campaign as campaign  # noqa: PLC0414
from test_operator import Reply
from test_operator import wire as wire  # noqa: PLC0414

REPO = "repos/hcoona/three"
MAIN = REPO + "/git/ref/heads/main"
RUN = REPO + "/actions/runs/123"
PENDING = RUN + "/pending_deployments"


@pytest.fixture
def clock(monkeypatch):
    """Control all actual operator clocks without sleeps or deadline bypass."""
    current = [datetime.now(UTC)]

    class Clock(datetime):
        @classmethod
        def now(cls, _tz=None):
            return current[0]

    monkeypatch.setattr(base_operator.datetime, "datetime", Clock)
    for module in (read_policy, normal_operator, campaign_module):
        monkeypatch.setattr(module, "datetime", Clock)
    return current


def response(value, interval=None, *, status=200, headers=()):
    """Keep response bytes and service headers in actual operator evidence."""
    values = list(headers)
    if interval is not None:
        values.append(("X-Poll-Interval", str(interval)))
    return Reply(status, json.dumps(value).encode(), values)


def prerequisites():
    """Return the actual caller's three distinct mutable prerequisites."""
    return [
        {"object": {"sha": TARGET}},
        {
            "id": 123,
            "head_sha": TARGET,
            "run_attempt": 1,
            "event": "workflow_dispatch",
            "head_branch": "main",
            "actor": {"login": "hcoona", "id": 712433},
            "path": ".github/workflows/workflow-delivery-v3-python-smoke.yml",
            "status": "waiting",
            "conclusion": None,
        },
        [
            {
                "environment": {
                    "id": 22765954016,
                    "name": "workflow-delivery-v3-python-testpypi",
                },
                "current_user_can_approve": True,
            }
        ],
    ]


@pytest.fixture
def approval(campaign, clock, monkeypatch):  # noqa: ARG001 - clock admission fixture
    """Load the real caller with isolated bindings and real evidence gates."""
    op, binding = construct(campaign)
    with op.deadline():
        op.ledger.update(run=123, preparation_audit_completed=op.now())
        op.save()
    files = {}
    for name in (
        "preapproval-replay.json",
        "independent-qualification.json",
        "inputs/references.json",
    ):
        path = op.directory / name
        path.parent.mkdir(exist_ok=True)
        save_json(path, {"run": 123, "fixture": name})
        files["operation/" + name] = digest(path)
    write_gate(
        op.directory.parent,
        binding,
        "preapproval",
        run=123,
        dispositions=["upload", "upload"],
        files_sha256=files,
    )
    monkeypatch.setitem(
        sys.modules,
        "dispatch_normal",
        SimpleNamespace(
            BINDING=binding, ROOT=op.directory.parent, TARGET=TARGET
        ),
    )
    spec = importlib.util.spec_from_file_location(
        "controlled_approve_normal",
        Path(__file__).resolve().parents[1] / "approve_normal.py",
    )
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return SimpleNamespace(caller=module.main, op=op, campaign=campaign)


def test_approval_poll_header_allows_fresh_checks_and_one_post(
    approval, wire, clock
):
    """Endpoint poll headers permit the complete actual approval journey."""
    wire.replies.extend(
        response(value, interval)
        for value, interval in zip(
            prerequisites(), (300, 600, 900), strict=True
        )
    )
    wire.replies.append(response([]))
    approval.caller()
    assert [(c[1], c[2]) for c in wire.calls] == [
        ("GET", "/" + MAIN),
        ("GET", "/" + RUN),
        ("GET", "/" + PENDING),
        ("POST", "/" + PENDING),
    ]
    assert json.loads(wire.calls[-1][3])["state"] == "approved"
    ledger = read(approval.op.ledger_path)
    assert ledger["approval_response"] == 200
    assert [entry["status"] for entry in ledger["requests"]] == [200] * 4
    assert approval.campaign.read()["scenarios"][0]["attempts"][0][
        "uploads_reserved"
    ] == ["wheel"]
    clock[0] += timedelta(seconds=900)
    wire.replies.extend(response(value) for value in prerequisites())
    with pytest.raises(AssertionError):
        approval.caller()
    assert [c[1] for c in wire.calls].count("POST") == 1
    assert read(approval.op.ledger_path)["requests"][:4] == ledger["requests"]


@pytest.mark.parametrize("failed_index", [1, 2])
def test_approval_downstream_wait_restarts_all_fresh_prerequisites(
    approval, wire, clock, failed_index
):
    """Downstream waits cannot perpetually renew a successful earlier prefix."""
    intervals = (300, 600, 900)
    for index, value in enumerate(prerequisites()[: failed_index + 1]):
        wire.replies.append(
            response(
                value,
                intervals[index],
                status=503 if index == failed_index else 200,
                headers=(("Retry-After", "120"),)
                if index == failed_index
                else (),
            )
        )
    start = clock[0]
    with pytest.raises(ReadPending):
        approval.caller()
    before = read(approval.op.ledger_path)
    assert not any(c[1] == "POST" for c in wire.calls)
    assert (
        approval.campaign.read()["scenarios"][0]["attempts"][0][
            "uploads_reserved"
        ]
        == []
    )
    # Main is due sooner than the failed downstream resource. No prefix GET
    # may extend main's interval when the whole fresh sequence is not ready.
    clock[0] = start + timedelta(seconds=300)
    with pytest.raises(ReadPending) as waiting:
        approval.caller()
    due = start + timedelta(seconds=intervals[failed_index])
    assert waiting.value.next_not_before == due.isoformat()
    assert read(approval.op.ledger_path) == before
    assert len(wire.calls) == failed_index + 1
    clock[0] = due
    wire.replies.extend(
        response(value, interval)
        for value, interval in zip(prerequisites(), intervals, strict=True)
    )
    wire.replies.append(response([]))
    approval.caller()
    assert [(c[1], c[2]) for c in wire.calls[failed_index + 1 :]] == [
        ("GET", "/" + MAIN),
        ("GET", "/" + RUN),
        ("GET", "/" + PENDING),
        ("POST", "/" + PENDING),
    ]
    after = read(approval.op.ledger_path)
    assert after["approval_response"] == 200
    assert after["requests"][: failed_index + 1] == before["requests"]
    failed = before["requests"][-1]
    assert (approval.op.directory / failed["body"]).read_bytes() == json.dumps(
        prerequisites()[failed_index]
    ).encode()


@pytest.mark.parametrize("changed", ["main", "run", "pending", "gate"])
def test_reentry_rejects_changed_prerequisite(approval, wire, clock, changed):
    """Fresh retry cannot reuse stale mutable or independent evidence."""
    wire.replies.extend(
        [response(prerequisites()[0], 300), response({}, status=503)]
    )
    with pytest.raises(ReadPending):
        approval.caller()
    clock[0] += timedelta(seconds=300)
    values = prerequisites()
    if changed == "main":
        values[0]["object"]["sha"] = "f" * 40
    elif changed == "run":
        values[1]["status"] = "completed"
    elif changed == "pending":
        values[2][0]["current_user_can_approve"] = False
    else:
        (approval.op.directory / "preapproval-replay.json").write_text(
            "changed"
        )
    wire.replies.extend(response(value) for value in values)
    with pytest.raises(AssertionError):
        approval.caller()
    assert all(c[1] == "GET" for c in wire.calls)
    assert (
        len(wire.calls)
        == 2 + {"main": 1, "run": 2, "pending": 3, "gate": 0}[changed]
    )
    assert (
        approval.campaign.read()["scenarios"][0]["attempts"][0][
            "uploads_reserved"
        ]
        == []
    )


def test_same_resource_wait_survives_restart(tmp_path, wire, clock):
    """Other resources remain available while exact URL pacing persists."""
    op = Base(tmp_path / "operation", TARGET)
    start = clock[0]
    wire.replies.append(response({"first": True}, 300))
    assert op.get(MAIN, "first") == {"first": True}
    first = read(op.ledger_path)["requests"][0]
    reopened = Base(op.directory, TARGET)
    assert reopened.get(RUN, "other") == {}
    for offset in (0, 299):
        clock[0] = start + timedelta(seconds=offset)
        with pytest.raises(ReadPending) as pending:
            Base(op.directory, TARGET).get(MAIN, "still-waiting")
        assert (
            pending.value.next_not_before
            == (start + timedelta(seconds=300)).isoformat()
        )
        assert len(wire.calls) == 2
    clock[0] = start + timedelta(seconds=300)
    reopened = Base(op.directory, TARGET)
    wire.replies.append(response({"second": True}))
    assert reopened.get(MAIN, "due") == {"second": True}
    assert reopened.ledger["requests"][0] == first
    assert (op.directory / first["body"]).read_bytes() == b'{"first": true}'


def test_distinct_poll_urls_keep_shared_cadence(tmp_path, wire, clock):
    """Global thirty seconds and endpoint three hundred seconds are distinct."""
    op = Base(tmp_path / "operation", TARGET)
    start = clock[0]
    wire.replies.append(response({}, 300))
    op.get(RUN, "poll-one", poll=True)
    clock[0] += timedelta(seconds=29)
    with pytest.raises(AssertionError):
        Base(op.directory, TARGET).get(RUN + "/jobs", "early-other", poll=True)
    clock[0] += timedelta(seconds=1)
    reopened = Base(op.directory, TARGET)
    reopened.get(RUN + "/jobs", "poll-other", poll=True)
    assert len(wire.calls) == 2
    clock[0] += timedelta(seconds=30)
    with pytest.raises(ReadPending) as pending:
        Base(op.directory, TARGET).get(RUN, "same-not-due", poll=True)
    assert (
        pending.value.next_not_before
        == (start + timedelta(seconds=300)).isoformat()
    )
    clock[0] = start + timedelta(seconds=300)
    Base(op.directory, TARGET).get(RUN, "same-due", poll=True)
    assert [c[2] for c in wire.calls] == [
        "/" + RUN,
        "/" + RUN + "/jobs",
        "/" + RUN,
    ]


@pytest.mark.parametrize("kind", ["retry", "reset", "transient"])
def test_service_and_error_waits_remain_global(tmp_path, wire, clock, kind):
    """A resource interval never erases or expands the service-wide wait."""
    op = Base(tmp_path / "operation", TARGET)
    start = clock[0]
    gap = 30 if kind == "transient" else 120
    due = start + timedelta(seconds=gap)
    if kind == "reset":
        # Integer service epochs must be compared without fractional seconds.
        due = due.replace(microsecond=0)
        headers = [
            ("X-RateLimit-Remaining", "0"),
            ("X-RateLimit-Reset", str(int(due.timestamp()))),
        ]
    else:
        headers = [("Retry-After", "120")] if kind == "retry" else []
    wire.replies.append(
        response(
            {}, 300, status=503 if kind == "transient" else 200, headers=headers
        )
    )
    if kind == "transient":
        with pytest.raises(ReadPending):
            op.get(MAIN, "first")
    else:
        op.get(MAIN, "first")
    before = op.ledger_path.read_bytes()
    clock[0] = due - timedelta(seconds=1)
    with pytest.raises(ReadPending) as pending:
        Base(op.directory, TARGET).get(RUN, "other-blocked")
    assert pending.value.next_not_before == due.isoformat()
    assert op.ledger_path.read_bytes() == before
    clock[0] = due
    assert Base(op.directory, TARGET).get(RUN, "other-due") == {}
    assert len(wire.calls) == 2
    with pytest.raises(ReadPending) as resource_wait:
        Base(op.directory, TARGET).get(MAIN, "resource-still-waiting")
    assert (
        resource_wait.value.next_not_before
        == (start + timedelta(seconds=300)).isoformat()
    )


@pytest.mark.parametrize(
    ("status", "interval"), [(200, "bad"), (200, -1), (401, 300)]
)
def test_malformed_header_stops_other_resources(
    tmp_path, wire, clock, status, interval
):
    """Malformed pacing and permission failure remain durable hard stops."""
    op = Base(tmp_path / "operation", TARGET)
    wire.replies.append(response({}, interval, status=status))
    with pytest.raises(ReadStopped):
        op.get(MAIN, "bad-response")
    clock[0] += timedelta(seconds=600)
    with pytest.raises(ReadStopped):
        Base(op.directory, TARGET).get(RUN, "other-after-stop")
    assert len(wire.calls) == len(wire.credentials) == 1


def test_group_wait_cannot_cross_deadline(tmp_path, wire, clock):
    """Group admission cannot extend the original publisher lifetime."""
    op = Base(tmp_path / "operation", TARGET)
    wire.replies.append(response({}, 14400))
    op.get(MAIN, "too-long")
    with pytest.raises(ReadStopped, match="deadline"):
        op.require_reads_ready((MAIN, RUN, PENDING))
    with pytest.raises(ReadStopped):
        Base(op.directory, TARGET).get(RUN, "after-stop")
    assert len(wire.calls) == 1
    clock[0] += timedelta(hours=4)
    with pytest.raises(OperatorDeadlineExceeded):
        Base(op.directory, TARGET).get(RUN, "expired")
    assert len(wire.calls) == 1


@pytest.mark.parametrize("key", ["global", "github-polls", "resource"])
def test_retained_legacy_wait_is_not_shortened(tmp_path, wire, clock, key):
    """Old combined service waits remain intact; no migration guesses scope."""
    op = Base(tmp_path / "operation", TARGET)
    due = clock[0] + timedelta(seconds=600)
    retained = {
        "next_not_before": due.isoformat(),
        "service_not_before": due.isoformat(),
    }
    if key == "global":
        op.ledger["read_control"] = retained
    else:
        identity = (
            key
            if key == "github-polls"
            else hashlib.sha256(
                ("https://api.github.com/" + MAIN).encode()
            ).hexdigest()
        )
        op.ledger["read_pacing"] = {identity: retained}
    op.save()
    before = op.ledger_path.read_bytes()
    reopened = Base(op.directory, TARGET)
    with pytest.raises(ReadPending) as pending:
        reopened.get(MAIN, "old-wait", poll=key == "github-polls")
    assert pending.value.next_not_before == due.isoformat()
    assert op.ledger_path.read_bytes() == before
    assert not wire.calls
