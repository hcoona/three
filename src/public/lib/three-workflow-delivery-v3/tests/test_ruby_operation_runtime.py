"""Durable spending, fixed phase leases and current authority guards."""

import os
from datetime import timedelta
from types import SimpleNamespace

import pytest
from three_workflow_delivery_v3 import ruby_operation_runtime as module
from three_workflow_delivery_v3.adapters.ruby_registry import RubyHttpResponse
from three_workflow_delivery_v3.release.ruby_operation import (
    RUBY_REGISTRY_PARTITIONS,
)

from .acceptance.ruby_bootstrap_fixtures import CURRENT
from .ruby_hosted_fixtures import hosted_case
from .ruby_registry_fixtures import ScriptedTransport, json_response

DIRECT_LIMIT = 128
ORIGINAL_SPENDS = 3
INITIAL_CHECKS = 2


def prepare(case, monkeypatch, phase):
    """Admit an actual immutable phase marker and one explicit local history."""
    operation = case.admit(monkeypatch)
    producer = case.inputs().producer("phase-" + phase)
    monkeypatch.setenv("GITHUB_JOB", producer)
    document = module.phase_claim(operation, phase)
    case.add("phase-" + phase, document)
    module.initialize_spending(case.directory / "spending" / phase)
    return operation


def send(budget):
    """Use actual request budgeting over a controlled endpoint response."""
    return budget.request("GET", "https://rubygems.org/test", {}, None, 32)


@pytest.mark.parametrize("bootstrap", [True, False])
@pytest.mark.parametrize("destination", ["rubygems", "github-packages"])
@pytest.mark.parametrize("phase", list(RUBY_REGISTRY_PARTITIONS))
def test_hosted_phase_partitions_spend_fixed_nontransferable_caps(
    tmp_path, monkeypatch, bootstrap, destination, phase
):
    """Every concrete reader spends its own cap, including operator debit."""
    case = hosted_case(tmp_path, destination=destination, bootstrap=bootstrap)
    prepare(case, monkeypatch, phase)
    debit = int(
        bootstrap and destination == "rubygems" and phase == "eligibility"
    )
    cap = RUBY_REGISTRY_PARTITIONS[phase]
    transport = ScriptedTransport(
        *[RubyHttpResponse(200, b"ok") for _ in range(cap - debit)]
    )
    monkeypatch.setattr(
        module, "RubyOperationHttpsTransport", lambda _: transport
    )
    reader = module.registry_reader(case.inputs(), phase, "synthetic-job-token")
    for _ in range(cap - debit):
        assert send(reader.budget).body == b"ok"
    assert (
        len(module.spending_members(case.directory / "spending" / phase))
        == cap - debit
    )
    with pytest.raises(ValueError, match=r"partition|exceeds"):
        send(reader.budget)
    assert len(transport.requests) == cap - debit


@pytest.mark.parametrize("bootstrap", [True, False])
@pytest.mark.parametrize(
    "loss",
    ["whole", "middle", "last", "membership", "changed", "symlink", "hardlink"],
)
def test_hosted_spent_history_loss_never_refills_reader(
    tmp_path, monkeypatch, bootstrap, loss
):
    """T01: complete or partial loss fails before any repeated native send."""
    case = hosted_case(tmp_path, bootstrap=bootstrap)
    prepare(case, monkeypatch, "execute")
    transport = ScriptedTransport(*[RubyHttpResponse(200, b"ok")] * 3)
    monkeypatch.setattr(
        module, "RubyOperationHttpsTransport", lambda _: transport
    )
    reader = module.registry_reader(
        case.inputs(), "execute", "synthetic-job-token"
    )
    for _ in range(3):
        send(reader.budget)
    directory = case.directory / "spending" / "execute"
    membership = directory.with_name("execute-membership.json")
    if loss == "whole":
        directory.rename(directory.with_name("lost-history"))
    elif loss in {"middle", "last"}:
        entry = directory / ("002.json" if loss == "middle" else "003.json")
        entry.rename(directory.parent / "lost-entry")
    elif loss == "membership":
        membership.rename(membership.with_suffix(".lost"))
    elif loss == "changed":
        (directory / "002.json").write_bytes(b"{}")
    else:
        entry = directory / "002.json"
        saved = directory.parent / "saved-entry"
        entry.rename(saved)
        if loss == "symlink":
            entry.symlink_to(saved)
        else:
            os.link(saved, entry)
    reopened = module.registry_reader(
        case.inputs(), "execute", "synthetic-job-token"
    )
    with pytest.raises(ValueError, match=r"history|membership|entry"):
        send(reopened.budget)
    assert len(transport.requests) == ORIGINAL_SPENDS
    with pytest.raises((ValueError, FileExistsError)):
        module.initialize_spending(directory)


@pytest.mark.parametrize(
    "failure", ["native", "response", "ordinal-fsync", "membership-fsync"]
)
def test_hosted_failed_send_or_persistence_cannot_repeat_reserved_ordinal(
    tmp_path, monkeypatch, failure
):
    """A partial pre-send write remains spent and never reaches transport."""
    case = hosted_case(tmp_path)
    prepare(case, monkeypatch, "execute")
    response = (
        OSError("controlled transport failure")
        if failure == "native"
        else RubyHttpResponse(200, b"x" * 40)
    )
    transport = ScriptedTransport(response)
    monkeypatch.setattr(
        module, "RubyOperationHttpsTransport", lambda _: transport
    )
    reader = module.registry_reader(
        case.inputs(), "execute", "synthetic-job-token"
    )
    if failure.endswith("fsync"):
        original = os.fsync
        count = 0

        def fail(descriptor):
            nonlocal count
            count += 1
            if count == (1 if failure == "ordinal-fsync" else 3):
                message = "controlled fsync failure"
                raise OSError(message)
            original(descriptor)

        monkeypatch.setattr(os, "fsync", fail)
    with pytest.raises((OSError, ValueError)):
        send(reader.budget)
    reopened = module.registry_reader(
        case.inputs(), "execute", "synthetic-job-token"
    )
    with pytest.raises(ValueError, match=r"history|spent"):
        send(reopened.budget)
    assert len(transport.requests) == (0 if failure.endswith("fsync") else 1)


@pytest.mark.parametrize("failure", ["membership-fsync", "mkdir"])
def test_hosted_interrupted_initialization_remains_spent(
    tmp_path, monkeypatch, failure
):
    """A half-created local partition cannot be silently reconstructed."""
    case = hosted_case(tmp_path)
    case.admit(monkeypatch)
    directory = case.directory / "github-spending"
    with monkeypatch.context() as patch:
        if failure == "membership-fsync":

            def fail(_descriptor):
                message = "controlled initialize failure"
                raise OSError(message)

            patch.setattr(os, "fsync", fail)
        else:
            original = type(directory).mkdir

            def fail(path, *args, **kwargs):
                if path == directory:
                    message = "controlled mkdir failure"
                    raise OSError(message)
                return original(path, *args, **kwargs)

            patch.setattr(type(directory), "mkdir", fail)
        with pytest.raises(OSError, match="controlled"):
            module.initialize_spending(directory)
    with pytest.raises((ValueError, FileExistsError)):
        module.initialize_spending(directory)
    with pytest.raises(ValueError, match="missing"):
        module.reserve_local(directory, 1, {"send": "forbidden"})


def test_hosted_github_budget_continues_across_instances_without_refill(
    tmp_path, monkeypatch
):
    """Separate CLI instances share one actual 128-read durable history."""
    case = hosted_case(tmp_path)
    case.admit(monkeypatch)
    module.initialize_spending(case.directory / "github-spending")
    transport = ScriptedTransport(*[json_response({"ok": True})] * 128)
    monkeypatch.setattr(
        module, "RubyOperationHttpsTransport", lambda _: transport
    )
    for _ in range(128):
        github = module.HostedRubyGitHub(case.inputs(), "synthetic-job-token")
        assert github.get("/repos/hcoona/three") == {"ok": True}
    with pytest.raises(ValueError, match="budget exhausted"):
        module.HostedRubyGitHub(case.inputs(), "synthetic-job-token").get(
            "/repos/hcoona/three"
        )
    assert len(transport.requests) == DIRECT_LIMIT
    assert (
        len(tuple((case.directory / "github-responses").iterdir()))
        == DIRECT_LIMIT
    )


@pytest.mark.parametrize("loss", ["whole", "last", "membership"])
def test_hosted_github_loss_rejects_before_second_effect(
    tmp_path, monkeypatch, loss
):
    """An old response file is not the first guard against duplicate effects."""
    case = hosted_case(tmp_path)
    case.admit(monkeypatch)
    directory = case.directory / "github-spending"
    module.initialize_spending(directory)
    transport = ScriptedTransport(json_response({"ok": True}))
    monkeypatch.setattr(
        module, "RubyOperationHttpsTransport", lambda _: transport
    )
    module.HostedRubyGitHub(case.inputs(), "synthetic-job-token").get(
        "/repos/hcoona/three"
    )
    selected = {
        "whole": directory,
        "last": directory / "001.json",
        "membership": directory.with_name("github-spending-membership.json"),
    }[loss]
    selected.rename(case.directory / "lost-state")
    with pytest.raises(ValueError, match=r"history|membership"):
        module.HostedRubyGitHub(case.inputs(), "synthetic-job-token").get(
            "/repos/hcoona/three"
        )
    assert len(transport.requests) == 1


@pytest.mark.parametrize("bootstrap", [True, False])
@pytest.mark.parametrize(
    "change", ["job", "expired", "debit", "request", "maximum"]
)
def test_hosted_phase_claim_cannot_extend_or_rebind_authority(
    tmp_path, monkeypatch, bootstrap, change
):
    """An immutable upload with altered contents remains an invalid lease."""
    case = hosted_case(tmp_path, bootstrap=bootstrap)
    prepare(case, monkeypatch, "eligibility")
    doc = case.inputs().document("phase-eligibility")
    if change == "job":
        monkeypatch.setenv("GITHUB_JOB", "build-ruby")
    elif change == "expired":
        monkeypatch.setattr(
            module, "now", lambda: CURRENT + timedelta(minutes=31)
        )
    else:
        key = {
            "debit": "operator-spent",
            "request": "request-digest",
            "maximum": "maximum",
        }[change]
        doc[key] = "sha256:" + "9" * 64 if change == "request" else 99
        case.add("phase-eligibility", doc)
    with pytest.raises(ValueError, match=r"Ruby|ruby"):
        module.PhaseReservation(case.inputs(), "eligibility")


@pytest.mark.parametrize("bootstrap", [True, False])
def test_hosted_authority_guard_actually_rechecks_full_history_every_time(
    tmp_path, monkeypatch, bootstrap
):
    """The helper is called at each boundary and stops protected drift."""
    case = hosted_case(tmp_path, bootstrap=bootstrap)
    operation = case.admit(monkeypatch)
    calls = []
    control = SimpleNamespace(
        content=operation.request.envelope.content, main_sha="c" * 40
    )

    class Native:
        def operation_control(self, target):
            calls.append(("operation-control", target))
            return control

        def governance(self, registry, *, initial):
            calls.append(("governance", registry.name, initial.digest))
            return operation.governance(CURRENT)

        def get(self, path):
            calls.append(("get", path))
            return (
                {"protected": True}
                if path.endswith("/main")
                else {
                    "full_name": "hcoona/three",
                    "owner": {"login": "hcoona", "id": 712433},
                }
            )

        def pages(self, path):
            calls.append(("pages", path))
            return []

    guard = module.authority_guard(case.inputs(), Native())
    first = guard()
    second = guard()
    assert first == second
    assert (
        sum(item[0] == "operation-control" for item in calls) == INITIAL_CHECKS
    )
    assert sum(item[0] == "governance" for item in calls) == (
        0 if bootstrap else 2
    )
    control.content = b"changed protected envelope"
    with pytest.raises(ValueError, match="envelope changed"):
        guard()
    assert (
        sum(item[0] == "operation-control" for item in calls) == ORIGINAL_SPENDS
    )
