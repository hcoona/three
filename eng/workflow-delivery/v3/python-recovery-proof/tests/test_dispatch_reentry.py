# Fixed identities and expiry windows are accepted protocol fixtures.
# ruff: noqa: PLC0414, PLR2004

"""The protected dispatch caller resumes only its pending pre-dispatch GET."""

import importlib.util
import json
from datetime import UTC, datetime
from pathlib import Path
from types import SimpleNamespace

import base_operator
import campaign as campaign_module
import normal_operator
import pytest
import read_policy
from campaign import digest, read, save_json
from read_policy import ReadPending, ReadStopped
from test_campaign import TARGET, prepare
from test_campaign import campaign as campaign
from test_operator import Reply
from test_operator import wire as wire


@pytest.fixture
def dispatch_fixture(campaign, wire, monkeypatch):
    """Load the real caller with admitted files and controlled external I/O."""
    repository = Path(__file__).resolve().parents[5]
    governance_path = (
        repository
        / ".github/workflow-delivery/governance"
        / "hcoona-release-smoke-python-testpypi.json"
    )
    governance = read(governance_path)
    clock = [datetime(2026, 9, 29, 0, 0, tzinfo=UTC)]

    class Clock(datetime):
        @classmethod
        def now(cls, _tz=None):
            return clock[0]

    for module in (campaign_module, normal_operator, read_policy):
        monkeypatch.setattr(module, "datetime", Clock)
    monkeypatch.setattr(base_operator.datetime, "datetime", Clock)
    root, binding = prepare(
        campaign,
        semver2="0.1.0-beta.31",
        governance_sha256=digest(governance_path),
        profile=governance["operation-profile-digest"],
    )
    save_json(
        root / "postmerge-ci.json", {"headSha": TARGET, "conclusion": "success"}
    )
    save_json(
        root / "nbgv.json",
        {
            "GitCommitId": TARGET,
            "PublicRelease": True,
            "SemVer2": "0.1.0-beta.31",
        },
    )
    trust = Path("/etc/ssl/certs/ca-certificates.crt")
    save_json(
        root / "local-ca-preparation.json", {"bundle_sha256": digest(trust)}
    )
    monkeypatch.setenv("SSL_CERT_FILE", str(trust))
    monkeypatch.setenv("WDV3_SCENARIO", "01")
    monkeypatch.setenv("WDV3_ATTEMPT", "01")
    spec = importlib.util.spec_from_file_location(
        "controlled_dispatch_caller", campaign.directory / "dispatch_normal.py"
    )
    caller = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(caller)
    monkeypatch.setattr(caller, "CHECKOUT", repository)
    monkeypatch.setattr(caller, "datetime", Clock)
    head = [TARGET]
    source_calls = []

    def source(argv, **_kwargs):
        source_calls.append(argv)
        if argv == ["git", "rev-parse", "HEAD"]:
            return head[0]
        if argv == ["git", "rev-parse", "HEAD^{tree}"]:
            return binding["tree"]
        assert argv == ["git", "status", "--porcelain"]
        return ""

    monkeypatch.setattr(caller.subprocess, "check_output", source)
    return SimpleNamespace(
        caller=caller,
        root=root,
        binding=binding,
        campaign=campaign,
        clock=clock,
        wire=wire,
        head=head,
        source_calls=source_calls,
        ledger=root / "operation/ledger.json",
    )


def pending_initial_main(fixture):
    """Create the actual canonical caller's first pending idempotent GET."""
    fixture.wire.replies.append(
        Reply(503, b"temporarily unavailable", [("Retry-After", "180")])
    )
    with pytest.raises(ReadPending) as pending:
        fixture.caller.main()
    assert [call[1] for call in fixture.wire.calls] == ["GET"]
    return datetime.fromisoformat(pending.value.next_not_before)


def current_main_reply(target=TARGET):
    """Return remote-main evidence independently of local source."""
    return Reply(200, json.dumps({"object": {"sha": target}}).encode())


def test_actual_dispatch_caller_resumes_pending_get_without_refilling_attempt(
    dispatch_fixture,
):
    """Pending reads reuse one reservation before exactly one dispatch."""
    fixture = dispatch_fixture
    due = pending_initial_main(fixture)
    original_state = fixture.campaign.read()
    original = read(fixture.ledger)
    source_before = len(fixture.source_calls)
    with pytest.raises(ReadPending):
        fixture.caller.main()
    assert len(fixture.source_calls) > source_before
    assert fixture.campaign.read() == original_state
    assert read(fixture.ledger) == original
    assert len(fixture.wire.calls) == 1
    fixture.clock[0] = due
    fixture.wire.replies.extend([current_main_reply(), Reply(204, b"")])
    fixture.caller.main()
    ledger = read(fixture.ledger)
    state = fixture.campaign.read()
    assert len(state["scenarios"]) == 1
    assert len(state["scenarios"][0]["attempts"]) == 1
    assert (
        state["scenarios"][0]["attempts"][0]
        == original_state["scenarios"][0]["attempts"][0]
    )
    assert ledger["started"] == original["started"]
    assert ledger["deadline"] == original["deadline"]
    assert ledger["requests"][0] == original["requests"][0]
    assert [call[1] for call in fixture.wire.calls] == ["GET", "GET", "POST"]
    assert ledger["dispatch_response"] == 204
    with pytest.raises(AssertionError):
        fixture.caller.main()
    assert read(fixture.ledger) == ledger
    assert fixture.campaign.read() == state
    assert len(fixture.wire.calls) == 3


def test_actual_dispatch_caller_accepts_fresh_attempt(dispatch_fixture):
    """A new admitted Attempt reads main then dispatches once."""
    fixture = dispatch_fixture
    fixture.wire.replies.extend([current_main_reply(), Reply(204, b"")])
    fixture.caller.main()
    assert [call[1] for call in fixture.wire.calls] == ["GET", "POST"]
    assert read(fixture.ledger)["dispatch_response"] == 204
    assert len(fixture.campaign.read()["scenarios"][0]["attempts"]) == 1


def test_resumed_dispatch_requires_fresh_matching_remote_main(dispatch_fixture):
    """A resumed initial read cannot dispatch after remote main moved."""
    fixture = dispatch_fixture
    due = pending_initial_main(fixture)
    fixture.clock[0] = due
    fixture.wire.replies.append(current_main_reply("f" * 40))
    with pytest.raises(AssertionError, match="accepted main moved"):
        fixture.caller.main()
    assert [call[1] for call in fixture.wire.calls] == ["GET", "GET"]
    assert all(
        entry["category"] != "dispatch"
        for entry in read(fixture.ledger)["requests"]
    )
    assert len(fixture.campaign.read()["scenarios"][0]["attempts"]) == 1
    with pytest.raises((AssertionError, KeyError)):
        fixture.caller.main()
    assert [call[1] for call in fixture.wire.calls] == ["GET", "GET"]


@pytest.mark.parametrize(
    "fault",
    [
        "missing-ledger",
        "uncertain-construction",
        "failed-construction",
        "missing-construction",
        "wrong-source",
        "changed-binding",
        "stopped",
        "closed",
        "expired",
        "dispatch-reserved",
        "resolved-run",
        "approval-reserved",
        "upload-reserved",
    ],
)
# Each table row exercises a distinct retained authority boundary.
def test_dispatch_reentry_rejects_nonpending_or_uncertain_authority(  # noqa: C901, PLR0912
    dispatch_fixture, fault
):
    """Only a constructed pending GET without effects may resume."""
    fixture = dispatch_fixture
    due = pending_initial_main(fixture)
    fixture.clock[0] = due
    state = fixture.campaign.read()
    attempt = state["scenarios"][0]["attempts"][0]
    ledger = read(fixture.ledger)
    if fault == "missing-ledger":
        fixture.ledger.unlink()
    elif fault == "uncertain-construction":
        attempt["construction"]["state"] = "started"
    elif fault == "failed-construction":
        attempt["construction"]["state"] = "failed"
    elif fault == "missing-construction":
        del attempt["construction"]
    elif fault == "wrong-source":
        fixture.head[0] = "f" * 40
    elif fault == "changed-binding":
        path = fixture.root / "execution-binding.json"
        path.write_bytes(path.read_bytes() + b"\n")
    elif fault == "stopped":
        ledger.setdefault("read_control", {})["stopped"] = "original hard stop"
    elif fault == "closed":
        attempt["closure"] = "no-dispatch"
    elif fault == "expired":
        fixture.clock[0] = datetime.fromisoformat(ledger["deadline"])
    elif fault == "dispatch-reserved":
        ledger["requests"].append(
            {"category": "dispatch", "send_started": False}
        )
    elif fault == "resolved-run":
        ledger["run"] = 15
    elif fault == "approval-reserved":
        ledger["requests"].append(
            {"category": "approval", "send_started": False}
        )
    else:
        attempt["uploads_reserved"] = ["wheel"]
    fixture.campaign.save(state)
    if fault != "missing-ledger":
        save_json(fixture.ledger, ledger)
    before = fixture.ledger.read_bytes() if fixture.ledger.exists() else None
    with pytest.raises(
        (
            AssertionError,
            FileNotFoundError,
            KeyError,
            ReadStopped,
            base_operator.OperatorDeadlineExceeded,
        )
    ):
        fixture.caller.main()
    assert [call[1] for call in fixture.wire.calls] == ["GET"]
    assert fixture.campaign.read() == state
    assert (
        fixture.ledger.read_bytes() if fixture.ledger.exists() else None
    ) == before
