"""Exact failure acknowledgement cannot reopen writes, quota or lifetime."""

from datetime import timedelta
from pathlib import Path

import pytest
from three_workflow_delivery_v3._ruby_native import ruby_digest
from three_workflow_delivery_v3.canonical import (
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.release import (
    ruby_configuration_continuation as m01,
)
from three_workflow_delivery_v3.release.ruby_configuration_continuation import (
    RubyConfigurationM01Continuation,
)
from three_workflow_delivery_v3.release.ruby_configuration_ledger import (
    RubyConfigurationLedger,
    _verify_completed_phase,
)

from ..ruby_integration_fixtures import NOW
from .ruby_configuration_continuation_fixtures import (
    acknowledgement_path,
    finish_read,
    incident,
    retained_tree,
)
from .ruby_configuration_ledger_fixtures import (
    EVIDENCE,
    complete_get,
    ledger,
    mutation,
)
from .ruby_operation_fixtures import instant


def test_production_acknowledgement_pins_only_the_disposed_incident():
    """A fixture override must not accidentally become production authority."""
    assert m01.M01_IDENTITIES == {
        "plan-digest": (
            "sha256:"
            "418c68da191ec0f6b78c37144a147fce6cd6092a56575f3134e38d50fe05b09b"
        ),
        "failed-phase-digest": (
            "sha256:"
            "660f1af39737c8b4e7522dd6dc91b842eb97cc3904fdc50b574ea1425f88744d"
        ),
        "failed-request-digest": (
            "sha256:"
            "8b9292bb0df4274e92a212f8cb3a7d14dde310a4e375d26671c85c92e3d3139c"
        ),
        "failed-response-digest": (
            "sha256:"
            "6e84a967e742477092e71591fb659a20009dfd9de7aa6e840a500efa3261f1c1"
        ),
        "failed-receipt-digest": (
            "sha256:"
            "354d39a2be9d4fa43c6317bc94c655052995467732061ec8476628769d12ee99"
        ),
        "original-journal-digest": (
            "sha256:"
            "3d53cae953091234f0d2e863261150fe3beb7a45353f2b32c13d3f1a7ffa93c5"
        ),
    }


@pytest.mark.parametrize(
    "field",
    [
        "plan-digest",
        "failed-phase-digest",
        "failed-request-digest",
        "failed-response-digest",
        "failed-receipt-digest",
        "original-journal-digest",
    ],
)
def test_acknowledgement_rejects_each_different_incident_identity(
    tmp_path, monkeypatch, field
):
    """Changing even one pinned identity cannot authorize a new exception."""
    selected, ack = incident(tmp_path, monkeypatch)
    original = retained_tree(selected.plan.directory)
    doc = ack.document
    doc[field] = "sha256:" + "e" * 64
    with pytest.raises(ValueError, match="disposed incident"):
        RubyConfigurationM01Continuation(canonicalize(doc))
    assert retained_tree(selected.plan.directory) == original


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("schema", "other"),
        ("verdict", "pending"),
        ("reviewer", "test-source-author"),
        ("reviewer", ""),
        ("author", ""),
        (
            "carrier",
            "https://github.com/hcoona/three/issues/955#issuecomment-1",
        ),
        ("source-commit", "main"),
        ("caller-digest", "sha256:BAD"),
        ("protocol-digest", "bad"),
        ("reviewed-at", "2026-10-01T00:00:00"),
        ("expires-at", "2099-01-01T00:00:00Z"),
        ("phases", {"environment-write": 1}),
    ],
)
def test_acknowledgement_rejects_scope_renewal_or_invalid_review(
    tmp_path, monkeypatch, field, value
):
    """Closed fields and independent review identity are admission inputs."""
    selected, ack = incident(tmp_path, monkeypatch)
    doc = ack.document
    doc[field] = value
    with pytest.raises(ValueError, match=r"M01|fields|record string"):
        RubyConfigurationM01Continuation(canonicalize(doc))
    assert not (
        selected.plan.directory / "post-configuration-controls"
    ).exists()


def test_ordinary_begin_stays_blocked_and_exact_continuation_preserves_failure(
    tmp_path, monkeypatch
):
    """The exception adds a read identity without converting failed writes."""
    selected, ack = incident(tmp_path, monkeypatch)
    failed = selected.plan.directory / "marker-write"
    original = retained_tree(failed)
    journal = (selected.plan.directory / "phases.jsonl").read_bytes()
    with pytest.raises(ValueError, match="missing or replaced"):
        selected.begin("post-configuration-controls", now=NOW)
    phase = finish_read(selected, ack)
    assert (
        parse_canonical_json(phase.content)["continuation-digest"] == ack.digest
    )
    assert phase.limit == 32  # noqa: PLR2004 - original read allocation
    assert retained_tree(failed) == original
    assert (
        parse_canonical_json((failed / "01.receipt.json").read_bytes())[
            "successful"
        ]
        is False
    )
    assert not (failed / "complete.json").exists()
    assert (
        (selected.plan.directory / "phases.jsonl")
        .read_bytes()
        .startswith(journal)
    )
    assert parse_canonical_json(
        (selected.plan.directory / "phases.jsonl").read_bytes().splitlines()[-1]
    )["phase-digest"] == ruby_digest(phase.content)


@pytest.mark.parametrize(
    "name",
    [
        "initial-controls",
        "initial-inventory",
        "environment-write",
        "branch-write",
        "marker-write",
        "initial-owners",
        "unknown",
    ],
)
def test_continuation_rejects_all_writes_initial_and_unknown_phases(
    tmp_path, monkeypatch, name
):
    """No read acknowledgement can add or repeat configuration effects."""
    selected, ack = incident(tmp_path, monkeypatch)
    original = retained_tree(selected.plan.directory)
    with pytest.raises(ValueError, match=r"M01|outside"):
        selected.begin(name, now=NOW, continuation=ack)
    assert retained_tree(selected.plan.directory) == original


@pytest.mark.parametrize(
    "name",
    [
        "bootstrap-controls",
        "bootstrap-inventory",
        "bootstrap-main",
        "postbootstrap-controls",
        "postbootstrap-package",
        *[
            f"normal{index:02}-{role}"
            for index in range(1, 5)
            for role in ("controls", "package", "main")
        ],
    ],
)
def test_later_reads_require_postconfiguration_then_keep_original_allocations(
    tmp_path, monkeypatch, name
):
    """Every remaining phase is one original allocation and GET-only."""
    selected, ack = incident(tmp_path, monkeypatch)
    with pytest.raises(ValueError, match="postconfiguration read first"):
        selected.begin(name, now=NOW, continuation=ack)
    assert not (selected.plan.directory / name).exists()
    finish_read(selected, ack)
    phase = selected.begin(name, now=NOW, continuation=ack)
    expected = (
        32
        if name.endswith("controls")
        else 10
        if name.endswith("inventory")
        else 1
    )
    assert phase.limit == expected
    with pytest.raises(ValueError, match="read phase cannot mutate"):
        phase.spend(*mutation("github-packages", "marker-write"), now=NOW)
    assert not (phase.directory / "01.request.json").exists()
    path = (
        "/repos/hcoona/three/branches/main"
        if name.endswith("-main")
        else "/user/packages/rubygems/hcoona-release-smoke-ruby"
        if name.endswith("-package")
        else "/user/packages?package_type=rubygems&per_page=100&page=1"
        if name.endswith("-inventory")
        else "/user"
    )
    reservation = complete_get(phase, path)
    phase.finish(now=NOW)
    assert parse_canonical_json(reservation)["method"] == "GET"
    assert parse_canonical_json(reservation)["path"] == path
    assert (
        parse_canonical_json((phase.directory / "complete.json").read_bytes())[
            "status"
        ]
        == "complete"
    )


def test_foreign_plan_and_rubygems_cannot_use_continuation(
    tmp_path, monkeypatch
):
    """Even a well-formed ack cannot migrate the original incident."""
    selected, ack = incident(tmp_path, monkeypatch)
    for destination in ("github-packages", "rubygems"):
        other = ledger(tmp_path / destination, destination)
        with pytest.raises(ValueError, match="another plan or destination"):
            other.begin(
                "post-configuration-controls", now=NOW, continuation=ack
            )
        assert not (
            other.plan.directory / "post-configuration-controls"
        ).exists()
    assert not (
        selected.plan.directory / "post-configuration-controls"
    ).exists()


@pytest.mark.parametrize(
    "offset", [timedelta(microseconds=-1), timedelta(seconds=1)]
)
def test_review_must_follow_failure_and_not_be_in_future(
    tmp_path, monkeypatch, offset
):
    """Review timestamps cannot predate the incident or postdate invocation."""
    selected, ack = incident(tmp_path, monkeypatch)
    doc = ack.document
    doc["reviewed-at"] = instant(NOW + offset)
    updated = RubyConfigurationM01Continuation(canonicalize(doc))
    acknowledgement_path(selected).write_bytes(updated.content)
    with pytest.raises(ValueError, match="review predates"):
        selected.begin(
            "post-configuration-controls", now=NOW, continuation=updated
        )
    assert not (
        selected.plan.directory / "post-configuration-controls"
    ).exists()


@pytest.mark.parametrize(
    "remaining", [timedelta(minutes=12), timedelta(days=7)]
)
def test_continuation_cannot_renew_plan_or_phase_expiry(
    tmp_path, monkeypatch, remaining
):
    """The next deadline remains min(original expiry, thirty minutes)."""
    selected, ack = incident(
        tmp_path, monkeypatch, **{"expires-at": instant(NOW + remaining)}
    )
    phase = selected.begin(
        "post-configuration-controls", now=NOW, continuation=ack
    )
    assert phase.deadline == NOW + min(remaining, timedelta(minutes=30))
    with pytest.raises(ValueError, match=r"stale|not current"):
        phase.spend("GET", "/user", None, now=phase.deadline)
    with pytest.raises(ValueError, match="not current"):
        ack.require_current(selected.plan, now=NOW + remaining)
    assert not (phase.directory / "01.request.json").exists()


def test_expired_failed_phase_is_not_reopened_by_a_fresh_read_phase(
    tmp_path, monkeypatch
):
    """A later read uses its own deadline within the unchanged original plan."""
    selected, ack = incident(tmp_path, monkeypatch)
    failed = selected.plan.directory / "marker-write"
    before = retained_tree(failed)
    later = NOW + timedelta(hours=1)
    phase = finish_read(selected, ack, now=later)
    assert parse_canonical_json((failed / "phase.json").read_bytes())[
        "deadline"
    ] == instant(NOW + timedelta(minutes=30))
    assert phase.deadline == later + timedelta(minutes=30)
    assert retained_tree(failed) == before
    assert not (failed / "complete.json").exists()


@pytest.mark.parametrize("operation", ["begin", "spend", "complete", "finish"])
@pytest.mark.parametrize(
    "change", ["missing", "changed", "symlink", "hardlink"]
)
def test_acknowledgement_loss_or_change_blocks_every_current_stage(
    tmp_path, monkeypatch, operation, change
):
    """An in-memory object cannot outlive its unchanged retained review."""
    selected, ack = incident(tmp_path, monkeypatch)
    phase = (
        None
        if operation == "begin"
        else selected.begin(
            "post-configuration-controls", now=NOW, continuation=ack
        )
    )
    request = (
        phase.spend("GET", "/user", None, now=NOW)
        if operation == "complete"
        else None
    )
    path = acknowledgement_path(selected)
    preserved = tmp_path / "preserved-ack.json"
    path.rename(preserved)
    if change == "changed":
        path.write_bytes(ack.content + b"\n")
    elif change == "symlink":
        path.symlink_to(preserved)
    elif change == "hardlink":
        path.hardlink_to(preserved)
    before = retained_tree(selected.plan.directory)
    actions = {
        "begin": lambda: selected.begin(
            "post-configuration-controls", now=NOW, continuation=ack
        ),
        "spend": lambda: phase.spend("GET", "/user", None, now=NOW),
        "complete": lambda: phase.complete_send(
            request, EVIDENCE, successful=True, now=NOW
        ),
        "finish": lambda: phase.finish(now=NOW),
    }
    with pytest.raises(ValueError, match=r"M01"):
        actions[operation]()
    assert retained_tree(selected.plan.directory) == before


@pytest.mark.parametrize(
    "name",
    [
        "phase.json",
        "01.request.json",
        "01.evidence.json",
        "01.receipt.json",
        "complete.json",
        "original-plan",
        "original-response",
        "prior-success",
    ],
)
def test_failed_originals_and_successful_predecessors_are_never_reconstructed(
    tmp_path, monkeypatch, name
):
    """One exact failed member and all other successful chains remain needed."""
    selected, ack = incident(tmp_path, monkeypatch)
    root = selected.plan.directory
    paths = {
        "original-plan": root.with_name("ledger-originals") / "plan.json",
        "original-response": root.with_name("ledger-originals")
        / "marker-write/01.response.json",
        "prior-success": root / "initial-controls/01.evidence.json",
    }
    path = paths.get(name, root / "marker-write" / name)
    path.write_bytes(canonicalize({"changed": True}))
    before = retained_tree(root)
    with pytest.raises(ValueError, match=r"M01|prior send|reserved phase"):
        selected.begin("post-configuration-controls", now=NOW, continuation=ack)
    assert retained_tree(root) == before
    assert not (root / "post-configuration-controls").exists()


@pytest.mark.parametrize("state", ["begun", "pending", "failed"])
def test_additional_partial_or_failed_read_stops_reopen_and_later_work(
    tmp_path, monkeypatch, state
):
    """M01 exempts only the original marker; every new failure is terminal."""
    selected, ack = incident(tmp_path, monkeypatch)
    phase = selected.begin(
        "post-configuration-controls", now=NOW, continuation=ack
    )
    if state != "begun":
        request = phase.spend("GET", "/user", None, now=NOW)
        if state == "failed":
            phase.complete_send(request, EVIDENCE, successful=False, now=NOW)
    before = retained_tree(selected.plan.directory)
    for name in ("post-configuration-controls", "bootstrap-main"):
        with pytest.raises(ValueError, match="missing or replaced"):
            RubyConfigurationLedger(selected.plan).begin(
                name, now=NOW, continuation=ack
            )
    assert retained_tree(selected.plan.directory) == before
    assert not (selected.plan.directory / "bootstrap-main").exists()


def test_completed_phase_cannot_refill_and_main_completion_reader_is_compatible(
    tmp_path, monkeypatch
):
    """Normal consumers validate continued phase metadata and one-read caps."""
    selected, ack = incident(tmp_path, monkeypatch)
    finish_read(selected, ack)
    main = selected.begin("bootstrap-main", now=NOW, continuation=ack)
    request = complete_get(main, "/repos/hcoona/three/branches/main")
    with pytest.raises(ValueError, match="exhausted"):
        main.spend("GET", "/repos/hcoona/three/branches/main", None, now=NOW)
    terminal = main.finish(now=NOW)
    _verify_completed_phase(main.directory, selected.plan)
    reopened = RubyConfigurationLedger(selected.plan)
    with pytest.raises(FileExistsError):
        reopened.begin("bootstrap-main", now=NOW, continuation=ack)
    assert parse_canonical_json(terminal)["receipts"] == [
        ruby_digest((main.directory / "01.receipt.json").read_bytes())
    ]
    assert (main.directory / "01.request.json").read_bytes() == request
    assert not (main.directory / "02.request.json").exists()
    assert not (selected.plan.directory / "marker-write/complete.json").exists()


def test_later_phase_rejects_a_different_acknowledgement_digest(
    tmp_path, monkeypatch
):
    """Changing review metadata cannot fork the original continuation."""
    selected, ack = incident(tmp_path, monkeypatch)
    finish_read(selected, ack)
    doc = ack.document
    doc["carrier"] = "https://github.com/hcoona/three/issues/954#issuecomment-2"
    changed = RubyConfigurationM01Continuation(canonicalize(doc))
    acknowledgement_path(selected).write_bytes(changed.content)
    with pytest.raises(ValueError, match="another acknowledgement"):
        selected.begin("bootstrap-main", now=NOW, continuation=changed)
    assert not (selected.plan.directory / "bootstrap-main").exists()


def test_lost_continuation_phase_retains_journal_and_cannot_refill(
    tmp_path, monkeypatch
):
    """Losing a whole directory cannot erase durable reservation history."""
    selected, ack = incident(tmp_path, monkeypatch)
    phase = selected.begin(
        "post-configuration-controls", now=NOW, continuation=ack
    )
    journal = (selected.plan.directory / "phases.jsonl").read_bytes()
    phase.directory.rename(tmp_path / "preserved-phase")
    with pytest.raises(ValueError, match="reserved phase is missing"):
        RubyConfigurationLedger(selected.plan)
    with pytest.raises(ValueError, match="reserved phase is missing"):
        selected.begin("post-configuration-controls", now=NOW, continuation=ack)
    assert (selected.plan.directory / "phases.jsonl").read_bytes() == journal
    assert not phase.directory.exists()


@pytest.mark.parametrize("operation", ["spend", "complete", "finish"])
def test_failed_original_evidence_loss_stops_in_progress_continuation(
    tmp_path, monkeypatch, operation
):
    """Current-stage revalidation includes the exact failed response lineage."""
    selected, ack = incident(tmp_path, monkeypatch)
    phase = selected.begin(
        "post-configuration-controls", now=NOW, continuation=ack
    )
    request = (
        phase.spend("GET", "/user", None, now=NOW)
        if operation == "complete"
        else None
    )
    original = selected.plan.directory / "marker-write/01.evidence.json"
    original.rename(tmp_path / "preserved-response.json")
    before = retained_tree(selected.plan.directory)
    actions = {
        "spend": lambda: phase.spend("GET", "/user", None, now=NOW),
        "complete": lambda: phase.complete_send(
            request, EVIDENCE, successful=True, now=NOW
        ),
        "finish": lambda: phase.finish(now=NOW),
    }
    with pytest.raises(ValueError, match="failed phase"):
        actions[operation]()
    assert retained_tree(selected.plan.directory) == before
    assert not (phase.directory / "complete.json").exists()


def test_interrupted_continuation_reservation_is_durably_spent(
    tmp_path, monkeypatch
):
    """Failure after journal append cannot free the selected read allocation."""
    selected, ack = incident(tmp_path, monkeypatch)
    directory = selected.plan.directory / "post-configuration-controls"
    mkdir = Path.mkdir

    def fail(path, *args, **kwargs):
        if path == directory:
            message = "controlled phase creation failure"
            raise OSError(message)
        return mkdir(path, *args, **kwargs)

    with monkeypatch.context() as patch:
        patch.setattr(Path, "mkdir", fail)
        with pytest.raises(OSError, match="controlled phase"):
            selected.begin(
                "post-configuration-controls", now=NOW, continuation=ack
            )
    journal = (selected.plan.directory / "phases.jsonl").read_bytes()
    assert (
        parse_canonical_json(journal.splitlines()[-1])["phase"]
        == "post-configuration-controls"
    )
    with pytest.raises(ValueError, match="reserved phase is missing"):
        selected.begin("post-configuration-controls", now=NOW, continuation=ack)
    assert (selected.plan.directory / "phases.jsonl").read_bytes() == journal
    assert not directory.exists()
