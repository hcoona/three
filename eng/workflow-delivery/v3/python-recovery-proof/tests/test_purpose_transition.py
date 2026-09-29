# Native identity fixtures and exact contract values are intentional.
# ruff: noqa: PLC0414, PLR2004

"""Verified admission strengthens purpose without discarding audit history."""

import json
from dataclasses import replace
from datetime import UTC, datetime, timedelta
from types import SimpleNamespace

import pytest
import registry_read
from campaign import digest, read, save_json
from read_policy import ReadPending, ReadStopped
from test_campaign import campaign as campaign
from test_campaign import prepare, write_gate
from test_registry_read import index, response
from test_registry_read import native_pair as native_pair
from three_workflow_delivery_v3.adapters.pypi import PythonRegistry


@pytest.fixture
def purpose_fixture(campaign, native_pair):
    """Build real independent admission and persistent native audit evidence."""

    def create(mode="stop-after-wheel"):
        root, binding = prepare(
            campaign, target=native_pair[0].witness.target, mode=mode
        )
        directory = root / "operation/registry-audit"
        directory.parent.mkdir()
        ledger_path = directory.parent / "registry-ledger.json"
        save_json(ledger_path, {})
        registry = PythonRegistry("testpypi")
        clock = [datetime(2026, 9, 28, 23, tzinfo=UTC)]
        deadline = (clock[0] + timedelta(hours=4)).isoformat()
        calls, replies = [], []

        def admit(purpose, **changes):
            facts = {
                "run": 15,
                "terminal": True,
                "dispatch_resolved": True,
                "publisher_quiescent": True,
                "native_read_admitted": True,
                "purpose": purpose,
            }
            facts.update(changes)
            write_gate(root, binding, "diagnostic-read", **facts)
            return registry_read.AuditAdmission(root, binding, 15)

        def wire(url, _headers, _maximum_bytes):
            calls.append(url)
            assert replies, "unexpected native request"
            reply = replies.pop(0)
            if isinstance(reply, BaseException):
                raise reply
            return reply

        def construct(purpose, admission, *, baseline=None, originals=None):
            ledger = read(ledger_path)
            return registry_read.RegistryAudit(
                directory,
                ledger,
                deadline,
                registry,
                originals
                or (
                    native_pair[:1] if purpose == "seed-proof" else native_pair
                ),
                save=lambda: save_json(ledger_path, ledger),
                now=lambda: clock[0],
                wire=wire,
                baseline=baseline,
                purpose=purpose,
                admission=admission,
            )

        def diagnose(distributions):
            admission = admit("diagnostic")
            replies.extend(
                [response(index(registry, distributions, 201))]
                + [response(item.content) for item in distributions]
            )
            caller = construct("diagnostic", admission)
            try:
                caller.step()
            except ReadPending as pending:
                clock[0] = datetime.fromisoformat(pending.next_not_before)
                construct("diagnostic", admission).step()
            return admission

        return SimpleNamespace(
            root=root,
            binding=binding,
            path=ledger_path,
            directory=directory,
            registry=registry,
            clock=clock,
            deadline=deadline,
            calls=calls,
            replies=replies,
            admit=admit,
            construct=construct,
            diagnose=diagnose,
            pair=native_pair,
        )

    return create


@pytest.mark.parametrize("purpose", ["seed-proof", "recovery-proof"])
def test_verified_diagnostic_strengthening_reuses_exact_history(
    purpose_fixture, purpose
):
    """Stronger checks preserve bytes, original admission, and time."""
    fixture = purpose_fixture(
        "none" if purpose == "recovery-proof" else "stop-after-wheel"
    )
    expected = fixture.pair if purpose == "recovery-proof" else fixture.pair[:1]
    fixture.diagnose(expected)
    before = read(fixture.path)
    original_gate = digest(
        fixture.root / "independent-diagnostic-read-gate.json"
    )
    calls = list(fixture.calls)
    admission = fixture.admit(purpose)
    proof_gate = digest(fixture.root / "independent-diagnostic-read-gate.json")
    caller = fixture.construct(purpose, admission)
    observation = caller.step()
    after = read(fixture.path)
    assert observation.files == expected
    assert after["initial_purpose"] == "diagnostic"
    assert after["purpose"] == purpose
    assert len(after["purpose_history"]) == 2
    assert original_gate in json.dumps(after["purpose_history"])
    assert proof_gate in json.dumps(after["purpose_history"])
    for key in ("requests", "current_responses", "pacing", "file_pacing"):
        assert after.get(key) == before.get(key)
    assert (
        after["observations"][: len(before["observations"])]
        == before["observations"]
    )
    assert fixture.calls == calls
    assert caller.deadline == fixture.deadline


@pytest.mark.parametrize("purpose", ["seed-proof", "recovery-proof"])
def test_diagnostic_absence_cannot_be_promoted_to_proof(
    purpose_fixture, purpose
):
    """Diagnostic completion does not satisfy proof inventory requirements."""
    fixture = purpose_fixture(
        "none" if purpose == "recovery-proof" else "stop-after-wheel"
    )
    fixture.diagnose(())
    before = read(fixture.path)
    calls = list(fixture.calls)
    caller = fixture.construct(purpose, fixture.admit(purpose))
    with pytest.raises(AssertionError):
        caller.step()
    after = read(fixture.path)
    assert after["classification"] == "stopped"
    assert after["requests"] == before["requests"]
    assert after["observations"] == before["observations"]
    assert fixture.calls == calls


@pytest.mark.parametrize(
    "fault",
    [
        "no-admission",
        "wrong-run",
        "wrong-target",
        "author-gate",
        "different-admitted-run",
        "author-trust-flag",
    ],
)
def test_purpose_strengthening_rejects_missing_or_incompatible_admission(
    purpose_fixture, fault
):
    """Caller flags cannot authorize proof or change the Attempt identity."""
    fixture = purpose_fixture()
    fixture.diagnose(fixture.pair[:1])
    before = read(fixture.path)
    calls = list(fixture.calls)

    def construct_invalid():
        if fault == "no-admission":
            admission = None
        elif fault == "author-trust-flag":
            admission = SimpleNamespace(purpose="seed-proof", trusted=True)
        elif fault == "wrong-run":
            admission = fixture.admit("seed-proof", run=16)
        elif fault == "wrong-target":
            admission = fixture.admit("seed-proof", target="f" * 40)
        elif fault == "different-admitted-run":
            write_gate(
                fixture.root,
                fixture.binding,
                "diagnostic-read",
                run=16,
                terminal=True,
                dispatch_resolved=True,
                publisher_quiescent=True,
                native_read_admitted=True,
                purpose="seed-proof",
            )
            admission = registry_read.AuditAdmission(
                fixture.root, fixture.binding, 16
            )
        else:
            admission = fixture.admit(
                "seed-proof", reviewer="/root/python_hosted_operator"
            )
        fixture.construct("seed-proof", admission)

    with pytest.raises((AssertionError, TypeError)):
        construct_invalid()
    assert read(fixture.path) == before
    assert fixture.calls == calls


def test_verified_admission_cannot_adopt_unbound_diagnostic_history(
    purpose_fixture,
):
    """New proof admission cannot retroactively authorize prior reads."""
    fixture = purpose_fixture()
    fixture.replies.extend(
        [
            response(index(fixture.registry, fixture.pair[:1], 201)),
            response(fixture.pair[0].content),
        ]
    )
    fixture.construct("diagnostic", None).step()
    before = read(fixture.path)
    calls = list(fixture.calls)
    with pytest.raises(AssertionError, match="unbound"):
        fixture.construct("seed-proof", fixture.admit("seed-proof"))
    assert read(fixture.path) == before
    assert fixture.calls == calls


def test_strengthening_rejects_changed_original_bytes(purpose_fixture):
    """A valid gate cannot substitute different distribution bytes."""
    fixture = purpose_fixture()
    fixture.diagnose(fixture.pair[:1])
    before = read(fixture.path)
    calls = list(fixture.calls)
    replacement = replace(fixture.pair[0], content=b"changed original archive")
    with pytest.raises(AssertionError, match="originals changed"):
        fixture.construct(
            "seed-proof", fixture.admit("seed-proof"), originals=(replacement,)
        )
    assert read(fixture.path) == before
    assert fixture.calls == calls


@pytest.mark.parametrize("state", ["stopped", "expired"])
def test_strengthening_cannot_revive_stopped_or_expired_audit(
    purpose_fixture, state
):
    """New admission cannot erase terminal stop or reset the lifetime."""
    fixture = purpose_fixture()
    fixture.diagnose(fixture.pair[:1])
    if state == "stopped":
        ledger = read(fixture.path)
        ledger.update(
            classification="stopped", reason="original read hard stop"
        )
        save_json(fixture.path, ledger)
    else:
        fixture.clock[0] = datetime.fromisoformat(fixture.deadline)
    before = read(fixture.path)
    calls = list(fixture.calls)
    with pytest.raises((AssertionError, ReadStopped)):
        fixture.construct("seed-proof", fixture.admit("seed-proof")).step()
    after = read(fixture.path)
    assert after["classification"] == "stopped"
    for key in (
        "requests",
        "observations",
        "purpose",
        "initial_purpose",
        "purpose_history",
        "admission_deadline",
    ):
        assert after[key] == before[key]
    if state == "stopped":
        assert after == before
    else:
        assert after["reason"] == "original read deadline exhausted"
    assert fixture.calls == calls


@pytest.mark.parametrize("pending", ["regressed", "transient"])
def test_pending_diagnostic_strengthening_preserves_backoff_and_failed_history(
    purpose_fixture,
    pending,
):
    """Proof admission cannot discard a pending regression or poll early."""
    fixture = purpose_fixture()
    baseline = index(fixture.registry, fixture.pair[:1], 200)
    fixture.replies.append(
        response(index(fixture.registry, (), 100))
        if pending == "regressed"
        else TimeoutError("interrupted diagnostic index")
    )
    with pytest.raises(ReadPending):
        fixture.construct(
            "diagnostic", fixture.admit("diagnostic"), baseline=baseline
        ).step()
    before = read(fixture.path)
    calls = list(fixture.calls)
    with pytest.raises(ReadPending):
        fixture.construct(
            "seed-proof", fixture.admit("seed-proof"), baseline=baseline
        ).step()
    after = read(fixture.path)
    assert after["classification"] == "pending"
    assert after["requests"] == before["requests"]
    assert after.get("observations") == before.get("observations")
    assert after["pacing"] == before["pacing"]
    assert fixture.calls == calls
    assert after["initial_purpose"] == "diagnostic"


def test_diagnostic_pair_cannot_satisfy_wheel_only_seed_proof(purpose_fixture):
    """Reusing the admitted pair does not broaden the seed predicate."""
    fixture = purpose_fixture()
    fixture.diagnose(fixture.pair)
    before = read(fixture.path)
    calls = list(fixture.calls)
    caller = fixture.construct(
        "seed-proof", fixture.admit("seed-proof"), originals=fixture.pair
    )
    with pytest.raises(AssertionError):
        caller.step()
    after = read(fixture.path)
    assert after["classification"] == "stopped"
    assert after["requests"] == before["requests"]
    assert after["observations"] == before["observations"]
    assert fixture.calls == calls


@pytest.mark.parametrize("purpose", ["diagnostic", "recovery-proof"])
def test_seed_proof_cannot_downgrade_or_switch_to_recovery(
    purpose_fixture, purpose
):
    """Seed and recovery purposes stay distinct after strengthening."""
    fixture = purpose_fixture()
    fixture.diagnose(fixture.pair[:1])
    fixture.construct("seed-proof", fixture.admit("seed-proof")).step()
    before = read(fixture.path)
    calls = list(fixture.calls)
    with pytest.raises(AssertionError):
        fixture.construct(purpose, fixture.admit(purpose))
    assert read(fixture.path) == before
    assert fixture.calls == calls
