# Historical deadlines and reviewed identities are literal protocol fixtures.
# ruff: noqa: E501, PLR2004
"""Separate terminal audit lifetime never renews the original publisher."""

from datetime import UTC, datetime, timedelta

import campaign as campaign_module
import pytest
import read_continuation
from campaign import digest, read, save_json
from normal_operator import Operator
from read_policy import ReadPending, ReadStopped
from test_campaign import write_gate
from test_read_continuation import (
    AUDIT_DEADLINE,
    DEADLINE,
    OLD_TARGET,
    RUN,
    pending_continuation,
    sidecar_bytes,
)
from test_read_continuation import campaign as campaign  # noqa: PLC0414
from test_read_continuation import (
    continuation_fixture as continuation_fixture,  # noqa: PLC0414
)
from test_read_continuation import native_pair as native_pair  # noqa: PLC0414
from test_registry_read import index, response


def admit_changed_binding(fixture):
    """Bind an invalid proposal correctly so its semantic check is exercised."""
    save_json(fixture.sidecar / "binding.json", fixture.binding)
    fixture.admission["binding_sha256"] = digest(
        fixture.sidecar / "binding.json"
    )
    fixture.admission["files_sha256"]["binding.json"] = fixture.admission[
        "binding_sha256"
    ]
    for key in ("original_deadline", "audit_deadline"):
        fixture.admission[key] = fixture.binding[key]
    save_json(fixture.sidecar / "independent-admission.json", fixture.admission)


def complete_wheel(fixture):
    """Read the exact wheel through the real parser and durable audit."""
    fixture.replies.extend(
        [
            response(index(fixture.registry, fixture.pair[:1], 201)),
            response(fixture.pair[0].content),
        ]
    )
    return fixture.caller().step()


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("audit_deadline", DEADLINE),
        ("audit_deadline", "2026-10-06T02:27:04.836609+00:00"),
        ("original_deadline", AUDIT_DEADLINE),
        ("governance_expiry", "2026-10-06T02:27:04.836609+00:00"),
    ],
)
def test_terminal_rejects_semantically_invalid_deadline_before_initialization(
    continuation_fixture, field, value
):
    """Even consistently admitted proposals cannot redefine either lifetime."""
    fixture = continuation_fixture
    fixture.binding[field] = value
    admit_changed_binding(fixture)
    before = sidecar_bytes(fixture)
    with pytest.raises(AssertionError):
        fixture.caller().step()
    assert sidecar_bytes(fixture) == before
    assert fixture.calls == fixture.verified == []


@pytest.mark.parametrize(
    "continuation_fixture",
    [
        {"schema": "workflow-delivery/v3/python-governance-v1"},
        {"state": "disabled"},
        {"live_enabled": False},
        {"operation-profile-digest": "sha256:" + "0" * 64},
        {"inspected-at": "2026-09-30T03:00:00+00:00"},
        {"expires-at": "2026-10-06T02:27:04.836609+00:00"},
    ],
    indirect=True,
)
def test_terminal_rejects_bound_but_invalid_current_governance(
    continuation_fixture,
):
    """Actual bound local bytes must satisfy the protected Governance contract."""
    fixture = continuation_fixture
    assert (
        digest(fixture.governance_path) == fixture.binding["governance_sha256"]
    )
    before = sidecar_bytes(fixture)
    with pytest.raises(AssertionError):
        fixture.caller().step()
    assert sidecar_bytes(fixture) == before
    assert fixture.calls == fixture.verified == []


def test_terminal_rejects_changed_current_governance_bytes(
    continuation_fixture,
):
    """Semantically identical replacement bytes still violate the original digest."""
    fixture = continuation_fixture
    fixture.governance_path.write_bytes(
        fixture.governance_path.read_bytes() + b"\n"
    )
    before = sidecar_bytes(fixture)
    with pytest.raises(AssertionError):
        fixture.caller().step()
    assert sidecar_bytes(fixture) == before
    assert fixture.calls == fixture.verified == []


@pytest.mark.parametrize(
    "record",
    [
        "binding",
        "independent-admission",
        "initialization",
        "ledger",
        "completion",
    ],
)
@pytest.mark.parametrize("fault", ["legacy-schema", "mixed-deadline"])
def test_terminal_rejects_legacy_or_mixed_sidecar_without_migration(
    continuation_fixture, record, fault
):
    """Each retained stage rejects old schema or altered audit authority unchanged."""
    fixture = continuation_fixture
    if record == "completion":
        complete_wheel(fixture)
    elif record in ("initialization", "ledger"):
        pending_continuation(fixture)
    path = fixture.sidecar / f"{record}.json"
    value = read(path)
    if fault == "legacy-schema":
        value["schema"] = value["schema"].removesuffix("v2") + "v1"
    else:
        value["audit_deadline"] = DEADLINE
    save_json(path, value)
    if record == "binding":
        fixture.binding = value
        admit_changed_binding(fixture)
    before = sidecar_bytes(fixture)
    calls = list(fixture.calls)
    with pytest.raises(AssertionError):
        fixture.caller().step()
    assert sidecar_bytes(fixture) == before
    assert fixture.calls == calls


@pytest.mark.parametrize(
    "orphan", ["missing-ledger", "responses", "observations", "completion.json"]
)
def test_terminal_cannot_reinitialize_orphaned_evidence(
    continuation_fixture, orphan
):
    """Construction uncertainty cannot replenish the audit state."""
    fixture = continuation_fixture
    if orphan == "missing-ledger":
        pending_continuation(fixture)
        (fixture.sidecar / "ledger.json").unlink()
    elif orphan.endswith(".json"):
        save_json(fixture.sidecar / orphan, {})
    else:
        (fixture.sidecar / orphan).mkdir()
        (fixture.sidecar / orphan / "retained.bin").write_bytes(
            b"retained evidence"
        )
    before = sidecar_bytes(fixture)
    calls = list(fixture.calls)
    with pytest.raises(AssertionError, match=r"cannot.*reset"):
        fixture.caller().step()
    assert sidecar_bytes(fixture) == before
    assert fixture.calls == calls


def test_terminal_pending_reads_preserve_service_wait_and_history_across_publisher_expiry(
    continuation_fixture,
):
    """Transient response evidence and service backoff survive publisher expiry."""
    fixture = continuation_fixture
    fixture.clock[0] = datetime.fromisoformat(DEADLINE) - timedelta(seconds=10)
    fixture.replies.append(
        (503, {"Retry-After": "120"}, b"temporarily unavailable")
    )
    with pytest.raises(ReadPending):
        fixture.caller().step()
    before = read(fixture.sidecar / "ledger.json")
    files = sidecar_bytes(fixture)
    assert before["requests"][0]["bytes"] == len(b"temporarily unavailable")
    assert (
        fixture.sidecar / before["requests"][0]["body"]
    ).read_bytes() == b"temporarily unavailable"
    assert before["pacing"]["consecutive_errors"] == 1
    assert before["pacing"]["requests"] == 2
    assert before["pacing"]["backoff_step"] == 2
    assert (
        before["pacing"]["service_not_before"]
        == before["pacing"]["next_not_before"]
    )
    fixture.clock[0] = datetime.fromisoformat(DEADLINE) + timedelta(seconds=1)
    with pytest.raises(ReadPending) as waiting:
        fixture.caller().step()
    assert waiting.value.next_not_before == before["pacing"]["next_not_before"]
    assert sidecar_bytes(fixture) == files
    assert len(fixture.calls) == 1
    fixture.clock[0] = datetime.fromisoformat(waiting.value.next_not_before)
    complete_wheel(fixture)
    after = read(fixture.sidecar / "ledger.json")
    assert after["original_deadline"] == DEADLINE
    assert after["audit_deadline"] == AUDIT_DEADLINE
    assert sum(record["bytes"] for record in after["requests"]) > sum(
        record["bytes"] for record in before["requests"]
    )
    assert after["requests"][:1] == before["requests"]
    assert after["inherited_observations"] == before["inherited_observations"]
    assert after["pacing"]["consecutive_errors"] == 0
    assert after["pacing"]["backoff_step"] == 3
    assert len(fixture.calls) == 3
    for name, raw in files.items():
        if name.startswith("responses/"):
            assert (fixture.sidecar / name).read_bytes() == raw
    assert {
        name: digest(fixture.root / name)
        for name in fixture.binding["original_evidence_sha256"]
    } == fixture.binding["original_evidence_sha256"]


def test_terminal_audit_does_not_revive_hard_stop_after_publisher_expiry(
    continuation_fixture,
):
    """The separate audit expiry does not turn an existing hard stop into pending."""
    fixture = continuation_fixture
    fixture.clock[0] = datetime.fromisoformat(DEADLINE) - timedelta(seconds=10)
    fixture.replies.append((403, {}, b"forbidden"))
    with pytest.raises(ReadStopped, match="non-transient"):
        fixture.caller().step()
    before = sidecar_bytes(fixture)
    fixture.clock[0] = datetime.fromisoformat(DEADLINE) + timedelta(seconds=1)
    with pytest.raises(ReadStopped, match="non-transient"):
        fixture.caller().step()
    assert sidecar_bytes(fixture) == before
    assert len(fixture.calls) == 1
    assert read(fixture.sidecar / "ledger.json")["classification"] == "stopped"


def test_terminal_completion_cannot_be_recorded_at_audit_expiry(
    continuation_fixture, monkeypatch
):
    """Even completed GETs cannot create a completion after the audit deadline."""
    fixture = continuation_fixture
    step = read_continuation.RegistryAudit.step

    def finish_at_expiry(audit):
        step(audit)
        fixture.clock[0] = datetime.fromisoformat(AUDIT_DEADLINE)

    monkeypatch.setattr(
        read_continuation.RegistryAudit, "step", finish_at_expiry
    )
    with pytest.raises((AssertionError, ReadStopped)):
        complete_wheel(fixture)
    assert len(fixture.calls) == 2
    assert not (fixture.sidecar / "completion.json").exists()
    assert (
        read(fixture.sidecar / "ledger.json")["audit_deadline"]
        == AUDIT_DEADLINE
    )
    assert (
        fixture.campaign.read()["scenarios"][0]["attempts"][0]["closure"]
        is None
    )
    assert {
        name: digest(fixture.root / name)
        for name in fixture.binding["original_evidence_sha256"]
    } == fixture.binding["original_evidence_sha256"]


@pytest.mark.parametrize(
    "fault",
    [
        "original-deadline",
        "audit-deadline",
        "at-expiry",
        "after-expiry",
        "legacy-completion",
    ],
)
def test_terminal_closure_rejects_wrong_deadlines_or_late_completion(
    continuation_fixture, fault
):
    """Independent closure cannot accept mixed lifetime or late completion evidence."""
    fixture = continuation_fixture
    completion = complete_wheel(fixture)
    original_deadline, audit_deadline = DEADLINE, AUDIT_DEADLINE
    if fault == "original-deadline":
        original_deadline = AUDIT_DEADLINE
    elif fault == "audit-deadline":
        audit_deadline = DEADLINE
    else:
        if fault == "legacy-completion":
            completion["schema"] = "testpypi-read-continuation-completion-v1"
        else:
            completion["completed"] = (
                datetime.fromisoformat(AUDIT_DEADLINE)
                + timedelta(seconds=int(fault == "after-expiry"))
            ).isoformat()
        save_json(fixture.sidecar / "completion.json", completion)
    write_gate(
        fixture.root,
        fixture.original,
        "closure",
        run=RUN,
        terminal=True,
        publisher_quiescent=True,
        dispatch_resolved=True,
        destination_state="wheel-only",
        native_audit_complete=True,
        conclusion="exact-partial",
        ledger_sha256=digest(fixture.root / "operation/ledger.json"),
        read_continuation_original_deadline=original_deadline,
        read_continuation_audit_deadline=audit_deadline,
        **{
            f"read_continuation_{name}_sha256": digest(
                fixture.sidecar / f"{name}.json"
            )
            for name in ("binding", "ledger", "completion")
        },
    )
    before = fixture.campaign.read()
    with pytest.raises(AssertionError):
        fixture.campaign.close("01", "01")
    assert fixture.campaign.read() == before
    assert before["scenarios"][0]["attempts"][0]["uploads_reserved"] == [
        "wheel"
    ]
    assert read(fixture.root / "operation/inputs/outcome.json") == {
        "outcome": "failed"
    }


def test_terminal_read_admission_does_not_extend_normal_publisher_lifetime(
    continuation_fixture, monkeypatch
):
    """The same Attempt permits scoped audit while the normal operator stays expired."""
    fixture = continuation_fixture

    class Clock(datetime):
        @classmethod
        def now(cls, tz=None):
            return fixture.clock[0].astimezone(tz or UTC)

    monkeypatch.setattr(campaign_module, "datetime", Clock)
    before = fixture.campaign.read()
    with pytest.raises(AssertionError):
        Operator(fixture.root / "operation", OLD_TARGET)
    assert fixture.calls == fixture.verified == []
    assert fixture.campaign.read() == before
    assert complete_wheel(fixture)["destination_state"] == "wheel-only"
    assert fixture.campaign.read() == before
    assert {
        name: digest(fixture.root / name)
        for name in fixture.binding["original_evidence_sha256"]
    } == fixture.binding["original_evidence_sha256"]
