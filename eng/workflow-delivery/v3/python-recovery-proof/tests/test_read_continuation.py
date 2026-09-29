# Immutable historical identities are fixed protocol fixtures.
# ruff: noqa: E501, PLR2004

"""Supplementary read evidence preserves the terminal Attempt's authority."""

import base64
import hashlib
import sys
from datetime import UTC, datetime
from pathlib import Path
from types import SimpleNamespace

import pytest
import read_continuation
from campaign import EXPIRY, digest, read, save_json
from read_continuation import Continuation
from read_policy import ReadPending, ReadStopped
from test_campaign import REVIEWER, construct, prepare, write_gate
from test_campaign import campaign as campaign  # noqa: PLC0414
from test_registry_read import index, response
from test_registry_read import native_pair as native_pair  # noqa: PLC0414
from three_workflow_delivery_v3.adapters.pypi import PythonRegistry

OLD_TARGET = "86b63fd09fcf3d20ee2615b35e37f4a541d0dc27"
OLD_TREE = "c6ca3df1c54915433b19767621caedb07b8b52b8"
STARTED = "2026-09-28T21:55:15.465230+00:00"
DEADLINE = "2026-09-29T01:55:15.465230+00:00"
AUDIT_DEADLINE = EXPIRY.isoformat()
RUN = 36489233271


def protected_governance(campaign, registry, monkeypatch, changes):
    """Bind real local Governance bytes with optional invalid fixture fields."""
    checkout = campaign.directory.parent / "protected-checkout"
    governance_path = (
        checkout
        / ".github/workflow-delivery/governance/hcoona-release-smoke-python-testpypi.json"
    )
    governance_path.parent.mkdir(parents=True)
    governance = {
        "schema": "workflow-delivery/v3/python-governance-v2",
        "state": "ready",
        "live_enabled": True,
        "operation-profile-digest": registry.profile_digest,
        "inspected-at": "2026-09-28T02:27:04.836609+00:00",
        "expires-at": AUDIT_DEADLINE,
    }
    governance.update(changes)
    save_json(governance_path, governance)
    monkeypatch.setattr(read_continuation, "CHECKOUT", checkout)
    return governance_path


@pytest.fixture
def continuation_fixture(campaign, native_pair, monkeypatch, request):
    """Build real binding/gate records while replacing only reader/source seams."""
    registry = PythonRegistry("testpypi")
    governance_path = protected_governance(
        campaign, registry, monkeypatch, getattr(request, "param", {})
    )
    op, original = construct(
        campaign,
        target=OLD_TARGET,
        tree=OLD_TREE,
        profile=registry.profile_digest,
        governance_sha256=digest(governance_path),
    )
    root = op.directory.parent
    sidecar = root / "read-continuation"
    sidecar.mkdir()
    state = campaign.read()
    attempt = state["scenarios"][0]["attempts"][0]
    attempt.update(
        started=STARTED, deadline=DEADLINE, uploads_reserved=["wheel"]
    )
    campaign.save(state)
    op.ledger.update(
        started=STARTED,
        deadline=DEADLINE,
        run=RUN,
        registry_audit_started="2026-09-28T22:10:00+00:00",
        registry_audit_requests=[
            {
                "kind": "index",
                "status": 200,
                "started": "2026-09-28T22:10:00+00:00",
            }
        ],
    )
    save_json(op.ledger_path, op.ledger)
    inputs = op.directory / "inputs"
    inputs.mkdir()
    baseline = index(registry, native_pair[:1], 200)
    save_json(
        inputs / "result.json",
        {
            "operations": [
                {
                    "status": "succeeded",
                    "readback-exact": True,
                    "observation": {
                        "phase": {
                            "terminal": "exact",
                            "reads": [
                                {
                                    "response": {
                                        "body": base64.b64encode(
                                            baseline
                                        ).decode(),
                                        "status": 200,
                                        "digest": "sha256:"
                                        + hashlib.sha256(baseline).hexdigest(),
                                    }
                                }
                            ],
                        }
                    },
                }
            ]
        },
    )
    save_json(inputs / "references.json", {})
    save_json(inputs / "outcome.json", {"outcome": "failed"})
    for relative in (
        "frozen-inputs.json",
        "operation/terminal-replay.json",
        "operation/terminal-controls.json",
        "operation/registry-audit/observation.json",
    ):
        path = root / relative
        path.parent.mkdir(exist_ok=True)
        save_json(path, {})
    (root / "operation/registry-audit/response-1.bin").write_bytes(
        index(registry, (), 100)
    )
    trust = Path("/etc/ssl/certs/ca-certificates.crt")
    monkeypatch.setenv("SSL_CERT_FILE", str(trust))
    save_json(
        root / "local-ca-preparation.json", {"bundle_sha256": digest(trust)}
    )
    write_gate(
        root,
        original,
        "diagnostic-read",
        run=RUN,
        terminal=True,
        dispatch_resolved=True,
        publisher_quiescent=True,
        native_read_admitted=True,
        purpose="seed-proof",
    )
    (sidecar / "protocol.md").write_text("Accepted corrected read protocol")
    binding = {
        "schema": "testpypi-read-continuation-v2",
        "purpose": "resolve-terminal-wheel-only",
        **{
            key: original[key]
            for key in (
                "scenario",
                "attempt",
                "target",
                "tree",
                "version",
                "mode",
                "governance_sha256",
                "profile",
                "frozen_inputs_sha256",
            )
        },
        "run": RUN,
        "original_binding_sha256": digest(root / "execution-binding.json"),
        "original_ledger_sha256": digest(op.ledger_path),
        "original_started": STARTED,
        "original_deadline": DEADLINE,
        "audit_deadline": AUDIT_DEADLINE,
        "governance_expiry": EXPIRY.isoformat(),
        "caller_source": "f" * 40,
        "caller_tree": "e" * 40,
        "caller_files_sha256": original["files_sha256"],
        "protocol_sha256": digest(sidecar / "protocol.md"),
        "reader_checkout": str(root / "reader"),
        "reader_source": OLD_TARGET,
        "reader_tree": OLD_TREE,
        "original_evidence_sha256": {
            p.relative_to(root).as_posix(): digest(p)
            for p in root.rglob("*")
            if p.is_file() and not p.is_relative_to(sidecar)
        },
        "independent_reviewers": [REVIEWER],
    }
    save_json(sidecar / "binding.json", binding)
    admission = {
        "schema": "testpypi-read-continuation-admission-v2",
        "reviewer": REVIEWER,
        "result": "passed",
        "binding_sha256": digest(sidecar / "binding.json"),
        **{
            key: binding[key]
            for key in (
                "protocol_sha256",
                "caller_source",
                "caller_tree",
                "original_binding_sha256",
                "original_ledger_sha256",
                "original_deadline",
                "audit_deadline",
                "governance_sha256",
                "profile",
                "frozen_inputs_sha256",
            )
        },
        **dict.fromkeys(
            (
                "dispatch_resolved",
                "terminal",
                "publisher_quiescent",
                "wheel_reservation_preserved",
            ),
            True,
        ),
        "files_sha256": {
            name: digest(sidecar / name)
            for name in ("binding.json", "protocol.md")
        },
    }
    save_json(sidecar / "independent-admission.json", admission)
    verified = []
    monkeypatch.setattr(
        read_continuation,
        "verify_callers",
        lambda value: verified.append(
            (value["caller_source"], value["reader_source"])
        ),
    )
    artifacts = [
        SimpleNamespace(variant=d.variant, inspect=lambda _raw, d=d: d)
        for d in native_pair
    ]
    decision = SimpleNamespace(
        artifacts=artifacts,
        snapshot=SimpleNamespace(
            governance=SimpleNamespace(registry=registry),
            model=SimpleNamespace(
                provider=SimpleNamespace(
                    nbgv=SimpleNamespace(pep440_version="0.1.0b31")
                )
            ),
        ),
    )
    reader_calls = []

    def reader_inputs(*args):
        reader_calls.append(args)
        return SimpleNamespace(
            decision=lambda: decision, content=lambda role: role.encode()
        )

    monkeypatch.setattr(read_continuation, "PythonInputs", reader_inputs)
    clock = [datetime(2026, 9, 29, 3, tzinfo=UTC)]
    calls, replies = [], []

    def wire(url, headers, maximum_bytes):
        calls.append((url, headers, maximum_bytes))
        assert replies
        reply = replies.pop(0)
        if isinstance(reply, BaseException):
            raise reply
        return reply

    def caller():
        return Continuation(root, now=lambda: clock[0], wire=wire)

    return SimpleNamespace(
        governance_path=governance_path,
        root=root,
        sidecar=sidecar,
        binding=binding,
        admission=admission,
        original=original,
        campaign=campaign,
        clock=clock,
        calls=calls,
        replies=replies,
        caller=caller,
        registry=registry,
        pair=native_pair,
        verified=verified,
        reader_calls=reader_calls,
    )


def test_readonly_continuation_preserves_original_authority_and_exposes_no_mutation(
    continuation_fixture,
):
    """Supplementary exact bytes do not rewrite a failed publication or its ledger."""
    fixture = continuation_fixture
    hashes = fixture.binding["original_evidence_sha256"]
    state_before = fixture.campaign.read()
    fixture.replies.extend(
        [
            response(index(fixture.registry, fixture.pair[:1], 201)),
            response(fixture.pair[0].content),
        ]
    )
    caller = fixture.caller()
    assert not any(
        hasattr(caller, name)
        for name in (
            "dispatch",
            "approve",
            "upload",
            "request",
            "acquire_token",
        )
    )
    completion = caller.step()
    assert datetime.fromisoformat(DEADLINE) < fixture.clock[0] < EXPIRY
    assert completion["destination_state"] == "wheel-only"
    assert datetime.fromisoformat(completion["completed"]) < EXPIRY
    for name, schema in (
        ("binding", "testpypi-read-continuation-v2"),
        ("independent-admission", "testpypi-read-continuation-admission-v2"),
        ("initialization", "testpypi-read-continuation-initialization-v2"),
        ("ledger", "testpypi-read-continuation-ledger-v2"),
        ("completion", "testpypi-read-continuation-completion-v2"),
    ):
        record = read(fixture.sidecar / f"{name}.json")
        assert record["schema"] == schema
        assert record["original_deadline"] == DEADLINE
        assert record["audit_deadline"] == AUDIT_DEADLINE
    assert read(fixture.root / "operation/inputs/outcome.json") == {
        "outcome": "failed"
    }
    assert (
        completion["original_ledger_sha256"] == hashes["operation/ledger.json"]
    )
    assert fixture.campaign.read() == state_before
    assert {name: digest(fixture.root / name) for name in hashes} == hashes
    assert fixture.verified == [("f" * 40, OLD_TARGET)]
    ledger = read(fixture.sidecar / "ledger.json")
    assert ledger["original_deadline"] == DEADLINE
    assert ledger["pacing"]["backoff_step"] == 2
    assert len(ledger["inherited_observations"]) == 1
    assert fixture.caller().step() == completion
    assert len(fixture.calls) == 2


@pytest.mark.parametrize(
    "fault",
    [
        "deadline",
        "original-ledger",
        "original-binding",
        "reader",
        "reviewer",
        "quiescence",
        "protocol",
    ],
)
def test_continuation_tampering_or_expiry_blocks_before_network(
    continuation_fixture, fault
):
    """Unchanged owner authority still requires exact original and independent facts."""
    fixture = continuation_fixture
    if fault == "deadline":
        fixture.clock[0] = datetime.fromisoformat(AUDIT_DEADLINE)
    elif fault in ("original-ledger", "original-binding"):
        path = fixture.root / (
            "operation/ledger.json"
            if fault == "original-ledger"
            else "execution-binding.json"
        )
        path.write_bytes(path.read_bytes() + b"\n")
    elif fault == "reader":
        fixture.binding["reader_source"] = "f" * 40
        save_json(fixture.sidecar / "binding.json", fixture.binding)
    elif fault == "protocol":
        (fixture.sidecar / "protocol.md").write_text("changed")
    else:
        fixture.admission[
            "reviewer" if fault == "reviewer" else "publisher_quiescent"
        ] = "/root/python_hosted_operator" if fault == "reviewer" else False
        save_json(
            fixture.sidecar / "independent-admission.json", fixture.admission
        )
    with pytest.raises(ReadStopped if fault == "deadline" else AssertionError):
        fixture.caller().step()
    assert fixture.calls == []
    assert not (fixture.sidecar / "ledger.json").exists()
    if fault == "deadline":
        assert not (fixture.sidecar / "initialization.json").exists()
        assert fixture.verified == []
        assert fixture.reader_calls == []


def pending_continuation(fixture):
    """Retain a genuinely admitted pending read before its independent audit deadline."""
    fixture.replies.append(response(index(fixture.registry, (), 100)))
    with pytest.raises(ReadPending):
        fixture.caller().step()
    return read(fixture.sidecar / "ledger.json")


def sidecar_bytes(fixture):
    """Snapshot all existing continuation evidence for no-rewrite assertions."""
    return {
        p.relative_to(fixture.sidecar).as_posix(): p.read_bytes()
        for p in fixture.sidecar.rglob("*")
        if p.is_file()
    }


def test_expired_admitted_pending_continuation_records_stop_without_more_work(
    continuation_fixture,
):
    """Expiry finalizes admitted pending state without changing its evidence."""
    fixture = continuation_fixture
    before = pending_continuation(fixture)
    files = sidecar_bytes(fixture)
    campaign_before = fixture.campaign.read()
    calls, verified = list(fixture.calls), list(fixture.verified)
    readers = list(fixture.reader_calls)
    fixture.clock[0] = datetime.fromisoformat(AUDIT_DEADLINE)
    with pytest.raises(ReadStopped, match="original read deadline exhausted"):
        fixture.caller().step()
    after = read(fixture.sidecar / "ledger.json")
    assert after["classification"] == "stopped"
    assert after["reason"] == "original read deadline exhausted"
    assert after["original_deadline"] == DEADLINE
    for key in (
        "requests",
        "observations",
        "inherited_observations",
        "binding_sha256",
        "admission_sha256",
        "original_ledger_sha256",
    ):
        assert after[key] == before[key]
    for key, value in before["pacing"].items():
        assert after["pacing"][key] == value
    assert {
        name: raw
        for name, raw in sidecar_bytes(fixture).items()
        if name != "ledger.json"
    } == {name: raw for name, raw in files.items() if name != "ledger.json"}
    assert fixture.campaign.read() == campaign_before
    for name, expected in fixture.binding["original_evidence_sha256"].items():
        assert digest(fixture.root / name) == expected
    assert fixture.calls == calls
    assert fixture.verified == verified
    assert fixture.reader_calls == readers
    assert not (fixture.sidecar / "completion.json").exists()


@pytest.mark.parametrize(
    "fault",
    [
        "malformed-ledger",
        "unbound-initialization",
        "changed-binding",
        "changed-governance",
    ],
)
def test_expired_continuation_cannot_rewrite_malformed_or_unbound_state(
    continuation_fixture,
    fault,
):
    """Expiry does not grant permission to adopt arbitrary existing state."""
    fixture = continuation_fixture
    pending_continuation(fixture)
    if fault == "malformed-ledger":
        (fixture.sidecar / "ledger.json").write_text("not JSON")
    elif fault == "unbound-initialization":
        value = read(fixture.sidecar / "initialization.json")
        value["binding_sha256"] = "f" * 64
        save_json(fixture.sidecar / "initialization.json", value)
    elif fault == "changed-governance":
        fixture.governance_path.write_bytes(
            fixture.governance_path.read_bytes() + b"\n"
        )
    else:
        value = read(fixture.sidecar / "binding.json")
        value["original_ledger_sha256"] = "f" * 64
        save_json(fixture.sidecar / "binding.json", value)
    files = sidecar_bytes(fixture)
    campaign_before = fixture.campaign.read()
    calls, verified = list(fixture.calls), list(fixture.verified)
    readers = list(fixture.reader_calls)
    fixture.clock[0] = datetime.fromisoformat(AUDIT_DEADLINE)
    with pytest.raises((ReadStopped, AssertionError, ValueError)):
        fixture.caller().step()
    assert sidecar_bytes(fixture) == files
    assert fixture.campaign.read() == campaign_before
    assert fixture.calls == calls
    assert fixture.verified == verified
    assert fixture.reader_calls == readers


def test_expired_completed_continuation_keeps_original_completion_unchanged(
    continuation_fixture,
):
    """Local expiry bookkeeping cannot turn completed evidence into a failure."""
    fixture = continuation_fixture
    fixture.replies.extend(
        [
            response(index(fixture.registry, fixture.pair[:1], 201)),
            response(fixture.pair[0].content),
        ]
    )
    fixture.caller().step()
    files = sidecar_bytes(fixture)
    calls, verified = list(fixture.calls), list(fixture.verified)
    readers = list(fixture.reader_calls)
    fixture.clock[0] = datetime.fromisoformat(AUDIT_DEADLINE)
    with pytest.raises(ReadStopped, match="original read deadline exhausted"):
        fixture.caller().step()
    assert sidecar_bytes(fixture) == files
    assert read(fixture.sidecar / "ledger.json")["classification"] == "complete"
    assert (
        read(fixture.sidecar / "completion.json")["destination_state"]
        == "wheel-only"
    )
    assert fixture.calls == calls
    assert fixture.verified == verified
    assert fixture.reader_calls == readers


def test_unknown_continuation_state_blocks_new_version_scenario(
    continuation_fixture,
):
    """A prepared new source cannot discard unresolved native state."""
    fixture = continuation_fixture
    prepare(
        fixture.campaign, scenario="02", target="f" * 40, version="0.1.0b32"
    )
    with pytest.raises(AssertionError, match="unresolved"):
        fixture.campaign.reserve("02", "01")
    state = fixture.campaign.read()
    assert len(state["scenarios"]) == 1
    assert state["scenarios"][0]["attempts"][0]["uploads_reserved"] == ["wheel"]


def test_exact_continuation_closure_allows_new_version_without_refilling_effects(
    continuation_fixture,
):
    """Independent known-state closure keeps the old source and effect spend."""
    fixture = continuation_fixture
    fixture.replies.extend(
        [
            response(index(fixture.registry, fixture.pair[:1], 201)),
            response(fixture.pair[0].content),
        ]
    )
    fixture.caller().step()
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
        read_continuation_original_deadline=DEADLINE,
        read_continuation_audit_deadline=AUDIT_DEADLINE,
        conclusion="exact-partial",
        ledger_sha256=digest(fixture.root / "operation/ledger.json"),
        **{
            f"read_continuation_{name}_sha256": digest(
                fixture.sidecar / f"{name}.json"
            )
            for name in ("binding", "ledger", "completion")
        },
    )
    fixture.campaign.close("01", "01")
    (fixture.campaign.directory / "protocol.md").write_text(
        "New accepted protocol"
    )
    prepare(
        fixture.campaign, scenario="02", target="f" * 40, version="0.1.0b32"
    )
    fixture.campaign.reserve("02", "01")
    state = fixture.campaign.read()
    assert [(s["target"], s["version"]) for s in state["scenarios"]] == [
        (OLD_TARGET, "0.1.0b31"),
        ("f" * 40, "0.1.0b32"),
    ]
    assert [
        a["uploads_reserved"] for s in state["scenarios"] for a in s["attempts"]
    ] == [["wheel"], []]
    assert read(fixture.root / "operation/inputs/outcome.json") == {
        "outcome": "failed"
    }


def test_wheel_only_continuation_cannot_adopt_known_complete_closure(
    continuation_fixture,
):
    """The historical b31 sidecar remains restricted to exact wheel-only state."""
    fixture = continuation_fixture
    fixture.replies.extend(
        [
            response(index(fixture.registry, fixture.pair[:1], 201)),
            response(fixture.pair[0].content),
        ]
    )
    fixture.caller().step()
    write_gate(
        fixture.root,
        fixture.original,
        "closure",
        run=RUN,
        terminal=True,
        publisher_quiescent=True,
        dispatch_resolved=True,
        destination_state="complete",
        native_audit_complete=True,
        read_continuation_original_deadline=DEADLINE,
        read_continuation_audit_deadline=AUDIT_DEADLINE,
        conclusion="terminal-complete",
        outcome_sha256=digest(fixture.root / "operation/inputs/outcome.json"),
        files_sha256={
            "operation/inputs/outcome.json": digest(
                fixture.root / "operation/inputs/outcome.json"
            )
        },
        ledger_sha256=digest(fixture.root / "operation/ledger.json"),
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
    assert read(fixture.root / "operation/inputs/outcome.json") == {
        "outcome": "failed"
    }


def test_caller_admission_rejects_a_reader_imported_outside_its_pinned_checkout(
    tmp_path, monkeypatch
):
    """Correct caller bytes cannot excuse a reader from another source tree."""
    callers = Path(read_continuation.__file__).resolve().parent
    repository = Path(__file__).resolve().parents[5]
    protocol = b"Accepted read protocol"
    binding = {
        "caller_source": "f" * 40,
        "caller_tree": "e" * 40,
        "caller_files_sha256": {
            p.name: digest(p) for p in callers.glob("*.py")
        },
        "protocol_sha256": hashlib.sha256(protocol).hexdigest(),
        "reader_source": OLD_TARGET,
        "reader_tree": OLD_TREE,
        "reader_checkout": str(repository),
    }
    checked = []

    def git(argv, **_kwargs):
        args = argv[1:]
        if args == ["rev-parse", "HEAD"]:
            return (binding["caller_source"] + "\n").encode()
        if args == ["rev-parse", "HEAD^{tree}"]:
            return (binding["caller_tree"] + "\n").encode()
        if args == ["status", "--porcelain"]:
            return b""
        assert args[0] == "show"
        return (
            protocol
            if args[1].endswith("python-hosted-recovery.md")
            else (callers / Path(args[1]).name).read_bytes()
        )

    monkeypatch.setattr(read_continuation.subprocess, "check_output", git)
    monkeypatch.setattr(
        read_continuation,
        "verify",
        lambda target, tree, **kwargs: checked.append(
            (target, tree, kwargs["checkout"])
        ),
    )
    read_continuation.verify_callers(binding)
    monkeypatch.setitem(
        sys.modules,
        "three_workflow_delivery_v3.foreign_fixture",
        SimpleNamespace(__file__=str(tmp_path / "foreign.py")),
    )
    with pytest.raises(AssertionError):
        read_continuation.verify_callers(binding)
    assert checked == [(OLD_TARGET, OLD_TREE, str(repository))] * 2
