# Fixture identities and limits are literal expected contract values.
# The campaign fixture builder passes the complete scenario context.
# ruff: noqa: E501, PLR2004, PLR0913, PLR0917, PLC0415

"""Campaign tests use local evidence fixtures; no hosted effects."""

import shutil
from datetime import UTC, datetime, timedelta
from pathlib import Path

import pytest
from campaign import Campaign, digest, gate, read, save_json
from normal_operator import Operator

TARGET = "a" * 40
REVIEWER = "/root/python_recovery_design_review"


@pytest.fixture
def campaign(tmp_path, monkeypatch):
    """Provide the controlled campaign fixture."""
    monkeypatch.setattr(
        "normal_operator.verify_source", lambda _target, _tree: None
    )
    directory = tmp_path / "operator"
    directory.mkdir()
    for source in Path(__file__).resolve().parents[1].glob("*.py"):
        if not source.name.startswith("test_"):
            shutil.copyfile(source, directory / source.name)
    (directory / "protocol.md").write_text("Accepted bounded protocol fixture")
    value = Campaign(directory)
    value.initialize()
    return value


def write_gate(directory, binding, kind, **facts):
    """Provide the controlled write gate fixture."""
    evidence = directory / "review-evidence.json"
    if not evidence.exists():
        save_json(evidence, {"fixture": "independently retained evidence"})
    doc = {
        "reviewer": REVIEWER,
        "result": "passed",
        "kind": kind,
        "target": binding["target"],
        "scenario": binding["scenario"],
        "attempt": binding["attempt"],
        "mode": binding["mode"],
        "binding_sha256": digest(directory / "execution-binding.json"),
        "files_sha256": {evidence.name: digest(evidence)},
    }
    doc.update(facts)
    save_json(directory / f"independent-{kind}-gate.json", doc)
    return doc


def prepare(
    campaign,
    scenario="01",
    attempt="01",
    mode="stop-after-wheel",
    target=TARGET,
    version="0.1.0b31",
    **extra,
):
    """Provide the controlled prepare fixture."""
    directory = campaign.attempt_directory(scenario, attempt)
    directory.mkdir(parents=True)
    binding = {
        "scenario": scenario,
        "attempt": attempt,
        "target": target,
        "tooling": target,
        "tree": "b" * 40,
        "version": version,
        "mode": mode,
        "governance_sha256": "c" * 64,
        "profile": "sha256:" + "d" * 64,
        "frozen_inputs_sha256": "e" * 64,
        "protocol_sha256": digest(campaign.directory / "protocol.md"),
        "files_sha256": {
            p.name: digest(p)
            for p in campaign.directory.glob("*.py")
            if not p.name.startswith("test_")
        },
        "filenames": [
            f"hcoona_release_smoke_python-{version}-py3-none-any.whl",
            f"hcoona_release_smoke_python-{version}.tar.gz",
        ],
        "independent_reviewers": [REVIEWER],
    }
    binding.update(extra)
    save_json(directory / "execution-binding.json", binding)
    write_gate(directory, binding, "execution")
    return directory, binding


def construct(campaign, **kwargs):
    """Provide the controlled construct fixture."""
    directory, binding = prepare(campaign, **kwargs)
    campaign.reserve(binding["scenario"], binding["attempt"])
    return Operator(directory / "operation", binding["target"]), binding


def close(campaign, op, binding, conclusion="exact-partial", **changes) -> None:
    """Provide the controlled close fixture."""
    with op.deadline():
        if conclusion != "no-dispatch":
            op.ledger["run"] = int(binding["scenario"] + binding["attempt"])
            op.save()
    facts = {
        "run": op.ledger.get("run"),
        "terminal": True,
        "publisher_quiescent": True,
        "dispatch_resolved": True,
        "destination_state": "wheel-only"
        if conclusion == "exact-partial"
        else "absent",
        "native_audit_complete": True,
        "conclusion": conclusion,
        "ledger_sha256": digest(op.ledger_path),
        "wheel_upload_sent": False,
        "sdist_upload_sent": False,
    }
    facts.update(changes)
    write_gate(op.directory.parent, binding, "closure", **facts)
    campaign.close(binding["scenario"], binding["attempt"])


def test_seed_then_fresh_recovery_stops_at_first_audited_success(
    campaign,
) -> None:
    """Verify seed then fresh recovery stops at first audited success."""
    seed, b1 = construct(campaign)
    with seed.deadline():
        campaign.reserve_upload("01", "01", TARGET)
    close(campaign, seed, b1)
    recovery, b2 = construct(
        campaign, attempt="02", mode="none", seed_attempt="01"
    )
    with recovery.deadline():
        campaign.reserve_upload("01", "02", TARGET)
    close(
        campaign,
        recovery,
        b2,
        "audited-success",
        destination_state="complete",
        seed_proof_accepted=True,
        recovery_proof_accepted=True,
        clean_consumers="passed",
    )
    state = campaign.read()
    assert state["stopped"] is True
    assert [
        a["uploads_reserved"] for a in state["scenarios"][0]["attempts"]
    ] == [["wheel"], ["sdist"]]
    prepare(campaign, attempt="03", mode="none")
    with pytest.raises(AssertionError):
        campaign.reserve("01", "03")
    assert len(campaign.read()["scenarios"][0]["attempts"]) == 2


def test_failed_approval_reservations_are_never_refilled(campaign) -> None:
    """Verify failed approval reservations are never refilled."""
    for ordinal in range(1, 5):
        op, binding = construct(campaign, attempt=f"{ordinal:02}")
        with op.deadline():
            campaign.reserve_upload("01", f"{ordinal:02}", TARGET)
        close(campaign, op, binding, "terminal-no-upload")
    attempts = campaign.read()["scenarios"][0]["attempts"]
    assert len(attempts) == 4
    assert sum(len(a["uploads_reserved"]) for a in attempts) == 4
    with pytest.raises(AssertionError):
        campaign.attempt_directory("01", "05")
    assert all(a["uploads_reserved"] == ["wheel"] for a in attempts)


def test_five_scenarios_are_finite_and_versions_cannot_repeat(campaign) -> None:
    """Verify five scenarios are finite and versions cannot repeat."""
    for ordinal in range(1, 6):
        op, binding = construct(
            campaign, scenario=f"{ordinal:02}", version=f"0.1.0b{30 + ordinal}"
        )
        close(campaign, op, binding, "no-dispatch")
    assert len(campaign.read()["scenarios"]) == 5
    with pytest.raises(AssertionError):
        campaign.attempt_directory("06", "01")
    with pytest.raises(AssertionError):
        campaign.initialize()
    assert len(campaign.read()["scenarios"]) == 5


@pytest.mark.parametrize(
    "change", ["target", "version", "frozen_inputs_sha256", "mode"]
)
def test_recovery_rejects_incompatible_source_or_prestate(
    campaign, change
) -> None:
    """Verify recovery rejects incompatible source or prestate."""
    seed, b1 = construct(campaign)
    close(campaign, seed, b1)
    kwargs = {"attempt": "02", "mode": "none"}
    kwargs[change] = {
        "target": "f" * 40,
        "version": "0.1.0b99",
        "frozen_inputs_sha256": "f" * 64,
        "mode": "stop-after-wheel",
    }[change]
    prepare(campaign, **kwargs)
    with pytest.raises(AssertionError):
        campaign.reserve("01", "02")
    assert len(campaign.read()["scenarios"][0]["attempts"]) == 1


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("publisher_quiescent", False),
        ("dispatch_resolved", False),
        ("terminal", False),
        ("destination_state", "complete"),
    ],
)
def test_unresolved_or_wrong_partial_closure_blocks_successor(
    campaign, field, value
) -> None:
    """Verify unresolved or wrong partial closure blocks successor."""
    op, binding = construct(campaign)
    with pytest.raises(AssertionError):
        close(campaign, op, binding, **{field: value})
    assert campaign.read()["scenarios"][0]["attempts"][0]["closure"] is None
    prepare(campaign, attempt="02", mode="none")
    with pytest.raises(AssertionError):
        campaign.reserve("01", "02")


@pytest.mark.parametrize(
    "tamper", ["caller", "protocol", "gate", "author", "evidence"]
)
def test_admission_rejects_tampering_and_author_gates(campaign, tamper) -> None:
    """Verify admission rejects tampering and author gates."""
    directory, binding = prepare(campaign)
    if tamper == "caller":
        (campaign.directory / "campaign.py").write_text("changed")
    elif tamper == "protocol":
        (campaign.directory / "protocol.md").write_text("changed")
    elif tamper == "gate":
        doc = read(directory / "independent-execution-gate.json")
        doc["binding_sha256"] = "f" * 64
        save_json(directory / "independent-execution-gate.json", doc)
    elif tamper == "author":
        binding["independent_reviewers"] = ["/root/python_hosted_operator"]
        save_json(directory / "execution-binding.json", binding)
        write_gate(
            directory,
            binding,
            "execution",
            reviewer="/root/python_hosted_operator",
        )
    else:
        (directory / "review-evidence.json").write_text("changed")
    with pytest.raises(AssertionError):
        campaign.reserve("01", "01")
    assert campaign.read()["scenarios"] == []


def test_construction_failure_spends_attempt_and_missing_ledger_cannot_be_recreated(
    campaign, monkeypatch
) -> None:
    """Verify construction failure spends attempt and missing ledger cannot be recreated."""
    directory, binding = prepare(campaign)
    campaign.reserve("01", "01")
    from base_operator import Operator as Base

    original = Base.__init__
    monkeypatch.setattr(
        Base,
        "__init__",
        lambda *_args: (_ for _ in ()).throw(
            OSError("fixture construction failure")
        ),
    )
    with pytest.raises(OSError, match="fixture construction failure"):
        Operator(directory / "operation", TARGET)
    monkeypatch.setattr(Base, "__init__", original)
    attempt = campaign.read()["scenarios"][0]["attempts"][0]
    assert attempt["construction"]["state"] == "failed"
    with pytest.raises(AssertionError):
        Operator(directory / "operation", TARGET)
    write_gate(
        directory,
        binding,
        "closure",
        conclusion="no-dispatch",
        ledger_sha256=None,
        construction_failed_before_return=True,
        dispatch_sent=False,
        destination_state="absent",
    )
    campaign.close("01", "01")
    op, _ = construct(campaign, attempt="02")
    op.ledger_path.unlink()
    with pytest.raises(AssertionError):
        Operator(op.directory, TARGET)
    assert not op.ledger_path.exists()


def test_invocation_lock_resume_and_deadline_are_original(campaign) -> None:
    """Verify invocation lock resume and deadline are original."""
    op, _binding = construct(campaign)
    resumed = Operator(op.directory, TARGET)
    with op.deadline():
        op.ledger["fixture_counter"] = 7
        op.save()
        with pytest.raises(BlockingIOError), campaign.locked():
            pass
    with resumed.deadline():
        assert resumed.ledger["fixture_counter"] == 7
        assert resumed.ledger["started"] == op.ledger["started"]
    state = campaign.read()
    state["scenarios"][0]["attempts"][0]["deadline"] = (
        datetime.now(UTC) - timedelta(seconds=1)
    ).isoformat()
    campaign.save(state)
    with pytest.raises(AssertionError), resumed.deadline():
        pytest.fail("expired caller entered")


def test_closed_old_callers_retained_when_new_scenario_uses_reviewed_correction(
    campaign,
) -> None:
    """Verify closed old callers retained when new scenario uses reviewed correction."""
    op, binding = construct(campaign)
    close(campaign, op, binding, "no-dispatch")
    (campaign.directory / "proof_checks.py").write_text(
        "# independently reviewed corrected source fixture\n"
    )
    next_op, _ = construct(campaign, scenario="02", version="0.1.0b32")
    assert next_op.started != op.started
    assert (
        digest(op.directory.parent / "callers/proof_checks.py")
        == binding["files_sha256"]["proof_checks.py"]
    )


def test_gate_path_escape_is_rejected(campaign) -> None:
    """Verify gate path escape is rejected."""
    directory, binding = prepare(campaign)
    write_gate(
        directory,
        binding,
        "execution",
        files_sha256={
            "../../../../../protocol.md": digest(
                campaign.directory / "protocol.md"
            )
        },
    )
    with pytest.raises(AssertionError):
        gate(directory, "independent-execution-gate.json", binding, "execution")
