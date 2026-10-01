"""Durable sole-operator reservations remain consumed after local failure."""

import os
import stat
from datetime import timedelta, timezone
from pathlib import Path

import pytest
from three_workflow_delivery_v3._ruby_native import ruby_digest
from three_workflow_delivery_v3.canonical import (
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.release.ruby_operation import (
    RubyOperationEnvelope,
    RubyOperationRequest,
    validate_ruby_operation_reservation,
)
from three_workflow_delivery_v3.release.ruby_operation_ledger import (
    RubyOperationLedger,
    initialize_ruby_operation_ledger,
)

from ..ruby_integration_fixtures import NOW
from .ruby_operation_fixtures import (
    DIGEST,
    REVIEW,
    SLOT,
    admission_document,
    envelope_document,
    request,
    request_document,
)


@pytest.fixture
def campaign(tmp_path):
    """Use only a new local POSIX ledger and modeled admission evidence."""
    directory = tmp_path / "ledger"
    header = initialize_ruby_operation_ledger(directory, "a" * 32)
    ledger = RubyOperationLedger(directory)
    selected = request()
    admission = canonicalize(admission_document(selected, ruby_digest(header)))
    return ledger, selected, admission


def _reserve(campaign):
    ledger, selected, admission = campaign
    return ledger.reserve(
        selected, admission, review=REVIEW, now=NOW + timedelta(minutes=2)
    )


def _snapshot(directory):
    """Capture file bytes and link targets without following links."""
    return {
        path.name: ("link", str(path.readlink()))
        if path.is_symlink()
        else ("file", path.read_bytes())
        for path in directory.iterdir()
    }


def _fail_fsync(monkeypatch, directory, phase):
    """Select journal, event-file, or directory persistence independently."""
    real_fsync = os.fsync
    journal_inode = (directory / "events.jsonl").stat().st_ino

    def fail(descriptor):
        metadata = os.fstat(descriptor)
        selected = (
            (phase == "journal" and metadata.st_ino == journal_inode)
            or (
                phase == "file"
                and stat.S_ISREG(metadata.st_mode)
                and metadata.st_ino != journal_inode
            )
            or (phase == "directory" and stat.S_ISDIR(metadata.st_mode))
        )
        if selected:
            message = "controlled durable-write failure"
            raise OSError(message)
        return real_fsync(descriptor)

    monkeypatch.setattr(os, "fsync", fail)


def test_ruby_ledger_reserves_and_joins_once_with_exact_retained_bytes(
    campaign,
):
    """A slot persists admission before a single actual-run association."""
    ledger, selected, admission = campaign
    header = (ledger.directory / "campaign.json").read_bytes()
    assert header == ledger.content
    assert ledger.directory.stat().st_mode & 0o777 == 0o700  # noqa: PLR2004
    reservation = _reserve(campaign)
    parsed = validate_ruby_operation_reservation(
        reservation, selected, admission, review=REVIEW
    )
    assert parsed["ledger-digest"] == ruby_digest(header)
    assert parsed["request-digest"] == selected.digest
    assert (ledger.directory / (SLOT + ".json")).read_bytes() == reservation
    run = ledger.join_run(selected, reservation, 991)
    assert parse_canonical_json(run) == {
        "schema": "workflow-delivery/v3/ruby-operation-run-join-v1",
        "request-digest": selected.digest,
        "reservation-digest": ruby_digest(reservation),
        "run-id": 991,
        "run-attempt": 1,
    }
    assert (ledger.directory / (SLOT + ".run.json")).read_bytes() == run
    with pytest.raises(FileExistsError):
        _reserve(campaign)
    with pytest.raises(FileExistsError):
        ledger.join_run(selected, reservation, 992)
    assert (ledger.directory / (SLOT + ".json")).read_bytes() == reservation
    assert (ledger.directory / (SLOT + ".run.json")).read_bytes() == run


@pytest.mark.parametrize(
    "change", ["relative", "noncanonical", "campaign", "existing-directory"]
)
def test_ruby_ledger_initialization_cannot_reuse_or_alias_location(
    tmp_path, change
):
    """Initialization is explicit and cannot repair an existing ledger root."""
    directory = tmp_path / "ledger"
    identity = "a" * 32
    if change == "relative":
        directory = Path("relative-test-ledger")
    elif change == "noncanonical":
        directory = tmp_path / "absent" / ".." / "ledger"
    elif change == "campaign":
        identity = "A" * 32
    else:
        directory.mkdir()
    with pytest.raises((ValueError, FileExistsError)):
        initialize_ruby_operation_ledger(directory, identity)
    assert not (tmp_path / "ledger" / "campaign.json").exists()


@pytest.mark.parametrize(
    "change",
    [
        "missing-header",
        "noncanonical",
        "schema",
        "directory",
        "campaign",
        "slots",
        "extra",
        "symlink",
    ],
)
def test_ruby_ledger_reopen_rejects_missing_corrupt_or_rebound_header(
    campaign, change
):
    """Recovery cannot reconstruct the admitted campaign from partial state."""
    ledger, _selected, _admission = campaign
    path = ledger.directory / "campaign.json"
    document = parse_canonical_json(path.read_bytes())
    if change == "missing-header":
        path.rename(ledger.directory / "lost-header.json")
    elif change == "symlink":
        path.rename(ledger.directory / "other.json")
        path.symlink_to(ledger.directory / "other.json")
    else:
        changes = {
            "noncanonical": lambda: None,
            "schema": lambda: document.update(schema="other"),
            "directory": lambda: document.update(
                directory=str(ledger.directory / "other")
            ),
            "campaign": lambda: document.update(campaign="invalid"),
            "slots": document["slots"].pop,
            "extra": lambda: document.update(extra=True),
        }
        changes[change]()
        path.write_bytes(
            canonicalize(document)
            + (b"\n" if change == "noncanonical" else b"")
        )
    with pytest.raises((ValueError, FileNotFoundError)):
        RubyOperationLedger(ledger.directory)
    with pytest.raises(FileExistsError):
        initialize_ruby_operation_ledger(ledger.directory, "a" * 32)


@pytest.mark.parametrize(
    "change", ["ledger", "review", "early", "expiry", "naive"]
)
def test_ruby_reservation_rejects_unadmitted_ledger_review_or_time(
    campaign, change
):
    """Admission rejection cannot create a consumable reservation."""
    ledger, selected, admission = campaign
    now = NOW + timedelta(minutes=2)
    review = REVIEW
    if change == "ledger":
        admission = canonicalize(admission_document(selected, DIGEST))
    elif change == "review":
        review = b"another modeled verdict"
    elif change == "early":
        now = NOW
    elif change == "expiry":
        now = NOW + timedelta(hours=4)
    else:
        now = now.replace(tzinfo=None)
    with pytest.raises((ValueError, TypeError)):
        ledger.reserve(selected, admission, review=review, now=now)
    assert not (ledger.directory / (SLOT + ".json")).exists()


@pytest.mark.parametrize("phase", ["journal", "file", "directory"])
def test_ruby_reservation_fsync_failure_keeps_slot_spent(
    campaign, monkeypatch, phase
):
    """An ambiguous durable write cannot authorize a replacement reservation."""
    ledger, _selected, _admission = campaign
    with monkeypatch.context() as patch:
        _fail_fsync(patch, ledger.directory, phase)
        with pytest.raises(OSError, match="durable-write"):
            _reserve(campaign)
    path = ledger.directory / (SLOT + ".json")
    assert path.exists() == (phase != "journal")
    retained = _snapshot(ledger.directory)
    expected = ValueError if phase == "journal" else FileExistsError
    with pytest.raises(expected):
        _reserve(campaign)
    assert _snapshot(ledger.directory) == retained


def test_ruby_partial_reservation_write_remains_spent(campaign, monkeypatch):
    """A truncated write is retained for investigation, never refilled."""
    ledger, _selected, _admission = campaign
    native_open = Path.open

    def partial(path, *args, **kwargs):
        mode = args[0] if args else kwargs.get("mode", "r")
        if path.name == SLOT + ".json" and mode == "xb":
            with native_open(path, "xb") as stream:
                stream.write(b'{"partial":')
            message = "controlled partial reservation"
            raise OSError(message)
        return native_open(path, *args, **kwargs)

    with monkeypatch.context() as patch:
        patch.setattr(Path, "open", partial)
        with pytest.raises(OSError, match="partial reservation"):
            _reserve(campaign)
    retained = _snapshot(ledger.directory)
    with pytest.raises(
        ValueError, match="reserved event is missing or changed"
    ):
        _reserve(campaign)
    assert (ledger.directory / (SLOT + ".json")).read_bytes() == b'{"partial":'
    assert _snapshot(ledger.directory) == retained


@pytest.mark.parametrize("run", [True, 0, -1, "991", 1.0])
def test_ruby_run_join_rejects_coerced_or_invalid_native_id(campaign, run):
    """The durable join cannot accept Boolean or textual run identities."""
    ledger, selected, _admission = campaign
    reservation = _reserve(campaign)
    with pytest.raises(ValueError, match="actual-run join"):
        ledger.join_run(selected, reservation, run)
    assert not (ledger.directory / (SLOT + ".run.json")).exists()


@pytest.mark.parametrize(
    "change", ["missing", "different-bytes", "symlink", "request"]
)
def test_ruby_run_join_requires_retained_original_reservation(campaign, change):
    """Actual-run association cannot repair or replace lost reserved bytes."""
    ledger, selected, _admission = campaign
    reservation = _reserve(campaign)
    path = ledger.directory / (SLOT + ".json")
    if change == "missing":
        path.rename(ledger.directory / "lost-reservation.json")
    elif change == "different-bytes":
        path.write_bytes(reservation + b"\n")
    elif change == "symlink":
        path.rename(ledger.directory / "other.json")
        path.symlink_to(ledger.directory / "other.json")
    else:
        doc = parse_canonical_json(reservation)
        doc["request-digest"] = DIGEST
        reservation = canonicalize(doc)
        path.write_bytes(reservation)
    with pytest.raises((ValueError, FileNotFoundError)):
        ledger.join_run(selected, reservation, 991)
    assert not (ledger.directory / (SLOT + ".run.json")).exists()


def test_ruby_destination_stop_blocks_remaining_slots_only_for_that_registry(
    campaign,
):
    """A retained completion consumes the destination's remaining campaign."""
    ledger, _selected, _admission = campaign
    audit = {
        "fixture": "modeled admitted completion, not actual service evidence"
    }
    ledger.stop_destination("rubygems", audit)
    assert (
        ledger.directory / "rubygems.complete.json"
    ).read_bytes() == canonicalize(audit)
    for slot in (
        "rubygems-bootstrap",
        "rubygems-normal01",
        "rubygems-normal04",
    ):
        selected = request(slot)
        admission = canonicalize(
            admission_document(selected, ruby_digest(ledger.content))
        )
        with pytest.raises(ValueError, match="already complete"):
            ledger.reserve(
                selected,
                admission,
                review=REVIEW,
                now=NOW + timedelta(minutes=2),
            )
    other = request("github-packages-normal01")
    admission = canonicalize(
        admission_document(other, ruby_digest(ledger.content))
    )
    reserved = ledger.reserve(
        other, admission, review=REVIEW, now=NOW + timedelta(minutes=2)
    )
    assert parse_canonical_json(reserved)["slot"] == "github-packages-normal01"
    with pytest.raises(FileExistsError):
        ledger.stop_destination("rubygems", audit)


@pytest.mark.parametrize("phase", ["journal", "file", "directory"])
def test_ruby_partial_completion_write_still_stops_destination(
    campaign, monkeypatch, phase
):
    """A failed completion flush cannot reopen remaining slots."""
    ledger, _selected, _admission = campaign
    with monkeypatch.context() as patch:
        _fail_fsync(patch, ledger.directory, phase)
        with pytest.raises(OSError, match="durable-write"):
            ledger.stop_destination("rubygems", {"fixture": "completion"})
    path = ledger.directory / "rubygems.complete.json"
    assert path.exists() == (phase != "journal")
    retained = _snapshot(ledger.directory)
    message = "reserved event" if phase == "journal" else "already complete"
    with pytest.raises(ValueError, match=message):
        _reserve(campaign)
    assert not (ledger.directory / (SLOT + ".json")).exists()
    assert _snapshot(ledger.directory) == retained


def test_ruby_ledger_cannot_stop_foreign_destination(campaign):
    """This campaign cannot persist unrelated registry authority."""
    ledger, _selected, _admission = campaign
    with pytest.raises(ValueError, match="foreign destination"):
        ledger.stop_destination("pypi", {})
    assert sorted(path.name for path in ledger.directory.iterdir()) == [
        "campaign.json",
        "events.jsonl",
    ]


def test_ruby_new_generation_cannot_refill_reserved_slot(campaign):
    """Fresh request/admission bytes cannot replace this campaign's slot."""
    ledger, first, _admission = campaign
    reserved = _reserve(campaign)
    doc = envelope_document()
    doc["slots"][SLOT]["generation"] = "d" * 32
    envelope = RubyOperationEnvelope(canonicalize(doc))
    second = RubyOperationRequest(
        canonicalize(request_document(envelope)), envelope
    )
    assert second.digest != first.digest
    admission = canonicalize(
        admission_document(second, ruby_digest(ledger.content))
    )
    with pytest.raises(FileExistsError):
        ledger.reserve(
            second, admission, review=REVIEW, now=NOW + timedelta(minutes=2)
        )
    assert (ledger.directory / (SLOT + ".json")).read_bytes() == reserved


def test_ruby_failed_initialization_cannot_reconstruct_campaign(
    tmp_path, monkeypatch
):
    """A partial initialization needs investigation, never automatic repair."""
    directory = tmp_path / "ledger"

    def fail(_descriptor):
        message = "controlled initialization failure"
        raise OSError(message)

    with monkeypatch.context() as patch:
        patch.setattr(os, "fsync", fail)
        with pytest.raises(OSError, match="initialization failure"):
            initialize_ruby_operation_ledger(directory, "a" * 32)
    assert directory.is_dir()
    retained = (directory / "campaign.json").read_bytes()
    with pytest.raises(FileExistsError):
        initialize_ruby_operation_ledger(directory, "a" * 32)
    assert (directory / "campaign.json").read_bytes() == retained


@pytest.mark.parametrize("phase", ["journal", "file", "directory"])
def test_ruby_failed_run_join_cannot_replace_actual_run(
    campaign, monkeypatch, phase
):
    """An ambiguous join write cannot be retried with another run."""
    ledger, selected, _admission = campaign
    reservation = _reserve(campaign)
    with monkeypatch.context() as patch:
        _fail_fsync(patch, ledger.directory, phase)
        with pytest.raises(OSError, match="durable-write"):
            ledger.join_run(selected, reservation, 991)
    path = ledger.directory / (SLOT + ".run.json")
    assert path.exists() == (phase != "journal")
    retained = _snapshot(ledger.directory)
    expected = ValueError if phase == "journal" else FileExistsError
    with pytest.raises(expected):
        ledger.join_run(selected, reservation, 992)
    assert _snapshot(ledger.directory) == retained
    assert (ledger.directory / (SLOT + ".json")).read_bytes() == reservation
    if phase != "journal":
        assert parse_canonical_json(path.read_bytes())["run-id"] == 991  # noqa: PLR2004


def test_ruby_ledger_initialization_syncs_new_directory_parent(
    tmp_path, monkeypatch
):
    """Campaign creation syncs both file content and directory entry."""
    real_fsync = os.fsync
    synced = set()

    def record(descriptor):
        metadata = os.fstat(descriptor)
        synced.add((metadata.st_dev, metadata.st_ino))
        return real_fsync(descriptor)

    directory = tmp_path / "ledger"
    with monkeypatch.context() as patch:
        patch.setattr(os, "fsync", record)
        header = initialize_ruby_operation_ledger(directory, "a" * 32)
    for path in (
        tmp_path,
        directory,
        directory / "campaign.json",
        directory / "events.jsonl",
    ):
        metadata = path.stat()
        assert (metadata.st_dev, metadata.st_ino) in synced
    assert RubyOperationLedger(directory).content == header


@pytest.mark.parametrize("operation", ["reserve", "join", "stop"])
@pytest.mark.parametrize("change", ["missing", "changed", "symlink"])
def test_ruby_ledger_operations_recheck_original_campaign_header(
    campaign, operation, change
):
    """An already-open object cannot act after campaign identity is lost."""
    ledger, selected, _admission = campaign
    reservation = _reserve(campaign) if operation == "join" else None
    path = ledger.directory / "campaign.json"
    if change in {"missing", "symlink"}:
        path.rename(ledger.directory / "lost-header.json")
        if change == "symlink":
            path.symlink_to(ledger.directory / "lost-header.json")
    else:
        document = parse_canonical_json(path.read_bytes())
        document["campaign"] = "b" * 32
        path.write_bytes(canonicalize(document))
    actions = {
        "reserve": lambda: _reserve(campaign),
        "join": lambda: ledger.join_run(selected, reservation, 991),
        "stop": lambda: ledger.stop_destination(
            "rubygems", {"fixture": "completion"}
        ),
    }
    with pytest.raises((ValueError, FileNotFoundError)):
        actions[operation]()
    assert not (ledger.directory / (SLOT + ".run.json")).exists()
    assert not (ledger.directory / "rubygems.complete.json").exists()
    if operation != "join":
        assert not (ledger.directory / (SLOT + ".json")).exists()


def test_ruby_dangling_completion_marker_still_blocks_new_reservation(campaign):
    """Missing link targets cannot make a terminal marker look absent."""
    ledger, _selected, _admission = campaign
    completion = ledger.directory / "rubygems.complete.json"
    completion.symlink_to(ledger.directory / "missing-audit")
    with pytest.raises(ValueError, match="unjoined state"):
        _reserve(campaign)
    assert completion.is_symlink()
    assert not (ledger.directory / (SLOT + ".json")).exists()


def test_ruby_reservation_normalizes_aware_time_to_importable_utc(campaign):
    """Equivalent operator timezones retain one canonical runtime instant."""
    ledger, selected, admission = campaign
    now = (NOW + timedelta(minutes=2)).astimezone(timezone(timedelta(hours=8)))
    content = ledger.reserve(selected, admission, review=REVIEW, now=now)
    doc = validate_ruby_operation_reservation(
        content, selected, admission, review=REVIEW
    )
    assert doc["reserved-at"] == "2026-09-30T12:02:00Z"


def _assert_broken_ledger_is_unchanged(campaign, reservation):
    """Reopen and every existing-object mutation must fail without repair."""
    ledger, selected, _admission = campaign
    retained = _snapshot(ledger.directory)
    actions = (
        lambda: RubyOperationLedger(ledger.directory),
        lambda: _reserve(campaign),
        lambda: ledger.join_run(selected, reservation, 992),
        lambda: ledger.stop_destination("github-packages", {"fixture": "stop"}),
    )
    for action in actions:
        with pytest.raises(ValueError, match=r"Ruby|JSON|canonical"):
            action()
        assert _snapshot(ledger.directory) == retained


@pytest.mark.parametrize("role", ["reservation", "run", "completion"])
@pytest.mark.parametrize("change", ["missing", "changed", "symlink"])
def test_ruby_lost_recorded_event_cannot_replenish_campaign(
    campaign, role, change
):
    """Loss of a whole event outside the ledger cannot replenish authority."""
    ledger, selected, _admission = campaign
    reservation = _reserve(campaign)
    names = {
        "reservation": SLOT + ".json",
        "run": SLOT + ".run.json",
        "completion": "rubygems.complete.json",
    }
    if role == "run":
        ledger.join_run(selected, reservation, 991)
    elif role == "completion":
        ledger.stop_destination("rubygems", {"fixture": "completion"})
    path = ledger.directory / names[role]
    original = path.read_bytes()
    preserved = ledger.directory.parent / ("preserved-" + path.name)
    path.rename(preserved)
    if change == "changed":
        path.write_bytes(original + b"\n")
    elif change == "symlink":
        path.symlink_to(preserved)
    _assert_broken_ledger_is_unchanged(campaign, reservation)
    assert preserved.read_bytes() == original
    if change == "missing":
        assert not path.exists()


@pytest.mark.parametrize(
    "change",
    [
        "missing",
        "empty",
        "torn-header",
        "changed-header",
        "symlink",
        "torn-event",
        "changed-digest",
        "missing-entry",
        "duplicate-entry",
        "foreign-entry",
        "extra-field",
        "noncanonical-entry",
        "extra-member",
    ],
)
def test_ruby_event_journal_damage_blocks_reopen_and_active_operations(
    campaign, change
):
    """A malformed or incomplete membership journal is never reconstructed."""
    ledger, _selected, _admission = campaign
    reservation = _reserve(campaign)
    journal = ledger.directory / "events.jsonl"
    original = journal.read_bytes()
    preserved = ledger.directory.parent / "preserved-events.jsonl"
    journal.rename(preserved)
    lines = original.splitlines(keepends=True)
    if change == "symlink":
        journal.symlink_to(preserved)
    elif change != "missing":
        simple_changes = {
            "empty": b"",
            "torn-header": lines[0][:-1],
            "torn-event": original[:-1],
            "missing-entry": lines[0],
            "duplicate-entry": original + lines[1],
            "noncanonical-entry": lines[0] + b" " + lines[1],
        }
        content = original
        if change in simple_changes:
            content = simple_changes[change]
        elif change == "changed-header":
            header = parse_canonical_json(lines[0][:-1])
            header["campaign-digest"] = DIGEST
            content = canonicalize(header) + b"\n" + lines[1]
        elif change == "extra-member":
            (ledger.directory / "unjoined.json").write_bytes(b"{}")
        else:
            event = parse_canonical_json(lines[1][:-1])
            if change == "changed-digest":
                event["digest"] = DIGEST
            elif change == "foreign-entry":
                event["name"] = "rubygems-normal05.json"
                (ledger.directory / event["name"]).write_bytes(reservation)
            else:
                event["extra"] = True
            prefix = original if change == "foreign-entry" else lines[0]
            content = prefix + canonicalize(event) + b"\n"
        journal.write_bytes(content)
    _assert_broken_ledger_is_unchanged(campaign, reservation)
    assert preserved.read_bytes() == original
    assert (ledger.directory / (SLOT + ".json")).read_bytes() == reservation


@pytest.mark.parametrize("failure", ["short-write", "write-then-raise"])
def test_ruby_interrupted_journal_append_remains_spent(
    campaign, monkeypatch, failure
):
    """A partial append blocks later actions without an event file."""
    ledger, _selected, _admission = campaign
    original = (ledger.directory / "events.jsonl").read_bytes()
    native_write = os.write

    def interrupted(descriptor, content):
        count = native_write(descriptor, content[:11])
        if failure == "write-then-raise":
            message = "controlled interrupted append"
            raise OSError(message)
        return count

    with monkeypatch.context() as patch:
        patch.setattr(os, "write", interrupted)
        with pytest.raises(OSError, match=r"partial|interrupted append"):
            _reserve(campaign)
    retained = (ledger.directory / "events.jsonl").read_bytes()
    assert retained.startswith(original)
    assert len(retained) == len(original) + 11
    assert not retained.endswith(b"\n")
    assert not (ledger.directory / (SLOT + ".json")).exists()
    _assert_broken_ledger_is_unchanged(campaign, b"not-created")


def test_ruby_journal_append_failure_before_write_has_no_effect(
    campaign, monkeypatch
):
    """An error before any journal byte cannot return a dispatch reservation."""
    ledger, _selected, _admission = campaign
    original = _snapshot(ledger.directory)

    def fail(_descriptor, _content):
        message = "controlled append failure before write"
        raise OSError(message)

    with monkeypatch.context() as patch:
        patch.setattr(os, "write", fail)
        with pytest.raises(OSError, match="before write"):
            _reserve(campaign)
    assert _snapshot(ledger.directory) == original
    assert RubyOperationLedger(ledger.directory).content == ledger.content


@pytest.mark.parametrize("role", ["reservation", "run", "completion"])
def test_ruby_event_creation_failure_after_journal_append_blocks_campaign(
    campaign, monkeypatch, role
):
    """Journal spending precedes event creation for every durable event role."""
    ledger, selected, _admission = campaign
    reservation = _reserve(campaign) if role == "run" else b"not-created"
    names = {
        "reservation": SLOT + ".json",
        "run": SLOT + ".run.json",
        "completion": "rubygems.complete.json",
    }
    native_open = Path.open
    journal = ledger.directory / "events.jsonl"
    before = journal.read_bytes()

    def fail(path, *args, **kwargs):
        mode = args[0] if args else kwargs.get("mode", "r")
        if path.name == names[role] and mode == "xb":
            message = "controlled event creation failure"
            raise OSError(message)
        return native_open(path, *args, **kwargs)

    actions = {
        "reservation": lambda: _reserve(campaign),
        "run": lambda: ledger.join_run(selected, reservation, 991),
        "completion": lambda: ledger.stop_destination("rubygems", {}),
    }
    with monkeypatch.context() as patch:
        patch.setattr(Path, "open", fail)
        with pytest.raises(OSError, match="event creation"):
            actions[role]()
    retained = journal.read_bytes()
    assert retained.startswith(before)
    event = parse_canonical_json(retained[len(before) : -1])
    assert event["name"] == names[role]
    assert not (ledger.directory / names[role]).exists()
    _assert_broken_ledger_is_unchanged(campaign, reservation)


def test_ruby_all_fixed_slots_progress_and_duplicates_preserve_journal(
    campaign,
):
    """All ten slots remain single-use through reopen and completion."""
    ledger, _selected, _admission = campaign
    slots = [
        f"{destination}-{kind}"
        for destination in ("github-packages", "rubygems")
        for kind in (
            "bootstrap",
            "normal01",
            "normal02",
            "normal03",
            "normal04",
        )
    ]
    assert parse_canonical_json(ledger.content)["slots"] == slots
    for run_id, slot in enumerate(slots, start=991):
        selected = request(slot)
        admission = canonicalize(
            admission_document(selected, ruby_digest(ledger.content))
        )
        reservation = ledger.reserve(
            selected, admission, review=REVIEW, now=NOW + timedelta(minutes=2)
        )
        run = ledger.join_run(selected, reservation, run_id)
        assert parse_canonical_json(reservation)["slot"] == slot
        assert parse_canonical_json(run)["run-id"] == run_id
        retained = _snapshot(ledger.directory)
        with pytest.raises(FileExistsError):
            ledger.reserve(
                selected,
                admission,
                review=REVIEW,
                now=NOW + timedelta(minutes=3),
            )
        with pytest.raises(FileExistsError):
            ledger.join_run(selected, reservation, run_id + 100)
        assert _snapshot(ledger.directory) == retained
        ledger = RubyOperationLedger(ledger.directory)
    for destination in ("github-packages", "rubygems"):
        ledger.stop_destination(destination, {"fixture": "completion"})
        retained = _snapshot(ledger.directory)
        with pytest.raises(FileExistsError):
            ledger.stop_destination(destination, {"fixture": "replacement"})
        assert _snapshot(ledger.directory) == retained
        ledger = RubyOperationLedger(ledger.directory)
    lines = (ledger.directory / "events.jsonl").read_bytes().splitlines()
    events = [parse_canonical_json(line) for line in lines[1:]]
    assert {event["name"] for event in events} == {
        *(slot + suffix for slot in slots for suffix in (".json", ".run.json")),
        "github-packages.complete.json",
        "rubygems.complete.json",
    }
    assert len(events) == 2 * len(slots) + 2
    for event in events:
        assert event["digest"] == ruby_digest(
            (ledger.directory / event["name"]).read_bytes()
        )
