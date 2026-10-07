"""Finite configuration spending survives failures without reset or repair."""

import os
from datetime import timedelta, timezone
from pathlib import Path

import pytest
from three_workflow_delivery_v3._ruby_native import ruby_digest
from three_workflow_delivery_v3.canonical import (
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.release.ruby_configuration_ledger import (
    RubyConfigurationLedger,
    RubyConfigurationPlan,
    configuration_phase_limits,
    initialize_ruby_configuration_ledger,
)

from ..ruby_integration_fixtures import NOW
from .ruby_configuration_ledger_fixtures import (
    EVIDENCE,
    REPOSITORY,
    complete_get,
    ledger,
    mutation,
    plan_document,
)
from .ruby_operation_fixtures import instant


@pytest.mark.parametrize(
    ("destination", "reads"), [("github-packages", 286), ("rubygems", 263)]
)
def test_fixed_configuration_allocations_cannot_transfer(destination, reads):
    """Each destination retains its exact read sum and three single writes."""
    limits = configuration_phase_limits(destination)
    assert (
        sum(
            value
            for role, value in limits.items()
            if not role.endswith("-write")
        )
        == reads
    )
    assert {
        role: value for role, value in limits.items() if role.endswith("-write")
    } == {"environment-write": 1, "branch-write": 1, "marker-write": 1}
    assert {
        value for role, value in limits.items() if role.endswith("-controls")
    } == {32}
    assert sum(role.endswith("-controls") for role in limits) == 8  # noqa: PLR2004
    assert ("initial-inventory" in limits) is (destination == "github-packages")
    assert ("initial-owners" in limits) is (destination == "rubygems")
    limits["initial-controls"] = 999
    assert configuration_phase_limits(destination)["initial-controls"] == 32  # noqa: PLR2004


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("schema", "other"),
        ("campaign", "C" * 32),
        ("source-commit", "main"),
        ("caller-digest", "bad"),
        ("protocol-digest", "sha256:" + "A" * 64),
        ("destination", "pypi"),
        ("directory", "relative-ledger"),
        ("issued-at", NOW.isoformat()),
        ("expires-at", instant(NOW)),
        ("expires-at", instant(NOW + timedelta(days=7, microseconds=1))),
        ("phases", {"initial-controls": 1000}),
    ],
)
def test_plan_rejects_noncanonical_authority_lifetime_and_allocations(
    tmp_path, field, value
):
    """Invalid identity/path/time/budget never initializes local state."""
    doc = plan_document(tmp_path / "ledger")
    doc[field] = value
    with pytest.raises(ValueError, match=r"Ruby|unsupported Ruby"):
        RubyConfigurationPlan(canonicalize(doc))
    assert not (tmp_path / "ledger").exists()


@pytest.mark.parametrize(
    "change", ["extra", "missing", "noncanonical", "alias", "boolean-quota"]
)
def test_plan_closed_bytes_and_exact_directory(tmp_path, change):
    """Key or lexical path aliases cannot silently change admitted bytes."""
    doc = plan_document(tmp_path / "ledger")
    if change == "extra":
        doc["token"] = "synthetic-not-a-credential"  # noqa: S105 - nonsecret invalid field
    elif change == "missing":
        doc.pop("caller-digest")
    elif change == "alias":
        doc["directory"] = str(tmp_path / "absent" / ".." / "ledger")
    elif change == "boolean-quota":
        doc["phases"]["bootstrap-main"] = True
    content = canonicalize(doc) + (b"\n" if change == "noncanonical" else b"")
    with pytest.raises(ValueError, match=r"Ruby|not canonical"):
        RubyConfigurationPlan(content)


@pytest.mark.parametrize(
    "now",
    [
        NOW - timedelta(microseconds=1),
        NOW + timedelta(days=7),
        NOW.replace(tzinfo=None),
    ],
)
def test_plan_clock_rejects_early_expired_or_naive_initialization(
    tmp_path, now
):
    """Rejected clocks cannot leave an apparently initialized ledger."""
    plan = RubyConfigurationPlan(
        canonicalize(plan_document(tmp_path / "ledger"))
    )
    with pytest.raises(ValueError, match=r"not current|aware clock"):
        initialize_ruby_configuration_ledger(plan, now=now)
    assert not plan.directory.exists()


def test_initialize_is_exclusive_and_never_refreshes_original_plan(tmp_path):
    """Opening or retrying initialization cannot reset the canonical plan."""
    selected = ledger(tmp_path / "ledger")
    plan = selected.plan
    assert (plan.directory / "plan.json").read_bytes() == plan.content
    assert plan.directory.stat().st_mode & 0o777 == 0o700  # noqa: PLR2004
    with pytest.raises(FileExistsError):
        initialize_ruby_configuration_ledger(plan, now=NOW)
    view = plan.document
    view["expires-at"] = instant(NOW + timedelta(days=1))
    assert plan.document["expires-at"] == instant(NOW + timedelta(days=7))
    assert RubyConfigurationLedger(plan).plan.content == plan.content


def test_fixture_plan_and_initialization_share_explicit_clock(tmp_path):
    """An alternate clock keeps the same bounded lifetime and expiry guard."""
    now = NOW + timedelta(days=30)
    selected = ledger(tmp_path / "ledger", now=now)

    assert selected.plan.document["issued-at"] == instant(now)
    assert selected.plan.document["expires-at"] == instant(
        now + timedelta(days=7)
    )
    selected.begin("initial-controls", now=now)
    with pytest.raises(ValueError, match="not current"):
        selected.plan.require_current(now + timedelta(days=7))


@pytest.mark.parametrize("change", ["missing", "replaced", "symlink"])
def test_missing_or_replaced_plan_blocks_reopen_and_existing_ledger(
    tmp_path, change
):
    """Already-open objects recheck immutable plan bytes before beginning."""
    selected = ledger(tmp_path / "ledger")
    path = selected.plan.directory / "plan.json"
    if change == "replaced":
        path.write_bytes(b"{}")
    else:
        original = tmp_path / "original-plan.json"
        path.rename(original)
        if change == "symlink":
            path.symlink_to(original)
    with pytest.raises(
        ValueError, match=r"missing or replaced|lost its admitted plan"
    ):
        RubyConfigurationLedger(selected.plan)
    with pytest.raises(
        ValueError, match=r"missing or replaced|lost its admitted plan"
    ):
        selected.begin("initial-controls", now=NOW)
    with pytest.raises(FileExistsError):
        initialize_ruby_configuration_ledger(selected.plan, now=NOW)
    assert not (selected.plan.directory / "initial-controls").exists()


@pytest.mark.parametrize(
    "remaining", [timedelta(days=7), timedelta(minutes=12)]
)
def test_phase_has_thirty_minute_deadline_capped_by_plan(tmp_path, remaining):
    """The phase binds UTC and never extends the plan expiry."""
    selected = ledger(
        tmp_path / "ledger", **{"expires-at": instant(NOW + remaining)}
    )
    shifted = NOW.astimezone(timezone(timedelta(hours=8)))
    phase = selected.begin("initial-controls", now=shifted)
    assert phase.deadline == NOW + min(remaining, timedelta(minutes=30))
    assert phase.limit == 32  # noqa: PLR2004
    assert parse_canonical_json(phase.content)["started-at"] == instant(NOW)
    with pytest.raises(ValueError, match=r"stale|not current"):
        phase.spend("GET", "/user", None, now=phase.deadline)
    assert not (phase.directory / "01.request.json").exists()


def test_phase_request_receipt_and_terminal_join_original_bytes(tmp_path):
    """Persist exact intent before send then bind response and final receipt."""
    selected = ledger(tmp_path / "ledger")
    phase = selected.begin("initial-controls", now=NOW)
    reservation = phase.spend("GET", "/user", None, now=NOW)
    assert (phase.directory / "01.request.json").read_bytes() == reservation
    assert parse_canonical_json(reservation) == {
        "schema": "workflow-delivery/v3/ruby-configuration-send-v1",
        "phase-digest": ruby_digest(phase.content),
        "ordinal": 1,
        "method": "GET",
        "path": "/user",
        "body-digest": None,
        "reserved-at": instant(NOW),
    }
    phase.complete_send(reservation, EVIDENCE, successful=True, now=NOW)
    receipt = (phase.directory / "01.receipt.json").read_bytes()
    assert parse_canonical_json(receipt) == {
        "schema": "workflow-delivery/v3/ruby-configuration-receipt-v1",
        "request-digest": ruby_digest(reservation),
        "evidence-digest": ruby_digest(EVIDENCE),
        "successful": True,
        "completed-at": instant(NOW),
    }
    assert (phase.directory / "01.evidence.json").read_bytes() == EVIDENCE
    result = phase.finish(now=NOW)
    assert (phase.directory / "complete.json").read_bytes() == result
    assert parse_canonical_json(result)["receipts"] == [ruby_digest(receipt)]
    following = RubyConfigurationLedger(selected.plan).begin(
        "post-configuration-controls", now=NOW
    )
    assert following.limit == 32  # noqa: PLR2004
    with pytest.raises(ValueError, match="closed"):
        phase.spend("GET", "/user", None, now=NOW)


def test_completed_phase_cannot_restart_or_transfer_unused_sends(tmp_path):
    """Finishing zero sends consumes the phase and leaves later caps fixed."""
    selected = ledger(tmp_path / "ledger")
    phase = selected.begin("initial-controls", now=NOW)
    assert parse_canonical_json(phase.finish(now=NOW))["receipts"] == []
    with pytest.raises(FileExistsError):
        selected.begin("initial-controls", now=NOW)
    next_phase = selected.begin("bootstrap-main", now=NOW)
    assert next_phase.limit == 1
    complete_get(next_phase, REPOSITORY + "/branches/main")
    with pytest.raises(ValueError, match="exhausted"):
        next_phase.spend("GET", REPOSITORY + "/branches/main", None, now=NOW)
    assert not (next_phase.directory / "02.request.json").exists()


@pytest.mark.parametrize("stage", ["begun", "reserved", "failed"])
def test_interrupted_or_failed_phase_prevents_any_later_phase(tmp_path, stage):
    """New object construction cannot resume or bypass incomplete spending."""
    selected = ledger(tmp_path / "ledger")
    phase = selected.begin("initial-controls", now=NOW)
    if stage != "begun":
        reservation = phase.spend("GET", "/user", None, now=NOW)
        if stage == "failed":
            phase.complete_send(
                reservation, EVIDENCE, successful=False, now=NOW
            )
        with pytest.raises(ValueError, match=r"pending|failed"):
            phase.spend("GET", "/user", None, now=NOW)
        with pytest.raises(ValueError, match=r"pending|failed"):
            phase.finish(now=NOW)
    reopened = RubyConfigurationLedger(selected.plan)
    with pytest.raises(ValueError, match="missing or replaced"):
        reopened.begin("bootstrap-main", now=NOW)
    assert not (selected.plan.directory / "bootstrap-main").exists()


@pytest.mark.parametrize(
    "bad", ["reservation", "boolean", "noncanonical-evidence"]
)
def test_completion_requires_exact_pending_request_and_evidence(tmp_path, bad):
    """Malformed evidence cannot produce a receipt or clear pending intent."""
    selected = ledger(tmp_path / "ledger")
    phase = selected.begin("initial-controls", now=NOW)
    reservation = phase.spend("GET", "/user", None, now=NOW)
    with pytest.raises(ValueError, match=r"pending send|not canonical"):
        phase.complete_send(
            reservation + (b"\n" if bad == "reservation" else b""),
            EVIDENCE + (b"\n" if bad == "noncanonical-evidence" else b""),
            successful=1 if bad == "boolean" else True,
            now=NOW,
        )
    assert not (phase.directory / "01.receipt.json").exists()
    with pytest.raises(ValueError, match="pending"):
        phase.spend("GET", "/user", None, now=NOW)


@pytest.mark.parametrize(
    "filename",
    [
        "01.request.json",
        "01.evidence.json",
        "01.receipt.json",
        "complete.json",
        "phase.json",
    ],
)
@pytest.mark.parametrize("change", ["missing", "changed", "symlink"])
def test_next_phase_requires_all_prior_completed_originals(
    tmp_path, filename, change
):
    """Loss or replacement of any joined prior evidence blocks new work."""
    selected = ledger(tmp_path / "ledger")
    phase = selected.begin("initial-controls", now=NOW)
    complete_get(phase)
    phase.finish(now=NOW)
    path = phase.directory / filename
    if change == "changed":
        path.write_bytes(canonicalize({"altered": True}))
    else:
        original = tmp_path / filename
        path.rename(original)
        if change == "symlink":
            path.symlink_to(original)
    with pytest.raises(ValueError, match=r"Ruby|fields"):
        selected.begin("bootstrap-main", now=NOW)
    assert not (selected.plan.directory / "bootstrap-main").exists()


@pytest.mark.parametrize(
    "target",
    ["01.request.json", "01.evidence.json", "01.receipt.json", "complete.json"],
)
def test_partial_durable_writes_leave_unresumable_phase(
    tmp_path, monkeypatch, target
):
    """A partial filesystem write is not repaired or interpreted as success."""
    selected = ledger(tmp_path / "ledger")
    phase = selected.begin("initial-controls", now=NOW)
    reservation = (
        None
        if target == "01.request.json"
        else phase.spend("GET", "/user", None, now=NOW)
    )
    if target == "complete.json":
        phase.complete_send(reservation, EVIDENCE, successful=True, now=NOW)
    native_open = Path.open

    def partial(path, *args, **kwargs):
        mode = args[0] if args else kwargs.get("mode", "r")
        if path == phase.directory / target and mode == "xb":
            with native_open(path, "xb") as stream:
                stream.write(b'{"partial":')
            message = "controlled partial write"
            raise OSError(message)
        return native_open(path, *args, **kwargs)

    with monkeypatch.context() as patch:
        patch.setattr(Path, "open", partial)
        actions = {
            "01.request.json": lambda: phase.spend(
                "GET", "/user", None, now=NOW
            ),
            "complete.json": lambda: phase.finish(now=NOW),
        }
        action = actions.get(
            target,
            lambda: phase.complete_send(
                reservation, EVIDENCE, successful=True, now=NOW
            ),
        )
        with pytest.raises(OSError, match="partial write"):
            action()
    assert (phase.directory / target).read_bytes() == b'{"partial":'
    with pytest.raises(ValueError, match=r"pending|closed"):
        phase.spend("GET", "/user", None, now=NOW)
    with pytest.raises(ValueError):  # noqa: PT011 - malformed retained JSON or absent terminal
        RubyConfigurationLedger(selected.plan).begin("bootstrap-main", now=NOW)
    assert not (selected.plan.directory / "bootstrap-main").exists()


def test_fsync_failure_during_reserve_keeps_send_spent(tmp_path, monkeypatch):
    """A write whose durability is unknown must not be issued again."""
    phase = ledger(tmp_path / "ledger").begin("initial-controls", now=NOW)

    def fail(_descriptor):
        message = "controlled flush failure"
        raise OSError(message)

    with monkeypatch.context() as patch:
        patch.setattr(os, "fsync", fail)
        with pytest.raises(OSError, match="flush failure"):
            phase.spend("GET", "/user", None, now=NOW)
    original = (phase.directory / "01.request.json").read_bytes()
    with pytest.raises(ValueError, match="pending"):
        phase.spend("GET", "/user", None, now=NOW)
    assert (phase.directory / "01.request.json").read_bytes() == original


@pytest.mark.parametrize("destination", ["github-packages", "rubygems"])
@pytest.mark.parametrize(
    ("role", "update"),
    [
        ("environment-write", False),
        ("branch-write", False),
        ("marker-write", False),
        ("marker-write", True),
    ],
)
def test_exact_mutation_role_is_spent_once_without_body_credentials(
    tmp_path, destination, role, update
):
    """Each fixed mutation body is hashed and cannot buy a second send."""
    phase = ledger(tmp_path / "ledger", destination).begin(role, now=NOW)
    method, path, body = mutation(destination, role, update=update)
    reservation = phase.spend(method, path, body, now=NOW)
    doc = parse_canonical_json(reservation)
    assert doc["method"] == method
    assert doc["path"] == path
    assert doc["body-digest"] == ruby_digest(body)
    assert "body" not in doc
    phase.complete_send(reservation, EVIDENCE, successful=True, now=NOW)
    with pytest.raises(ValueError, match="exhausted"):
        phase.spend(*mutation(destination, role, update=not update), now=NOW)
    assert not (phase.directory / "02.request.json").exists()


@pytest.mark.parametrize(
    "change", ["method", "path", "body", "noncanonical", "bypass", "credential"]
)
def test_mutation_request_cannot_override_reviewed_role(tmp_path, change):
    """Reject injected write scope before persisting any intent."""
    phase = ledger(tmp_path / "ledger").begin("environment-write", now=NOW)
    method, path, body = mutation("github-packages", "environment-write")
    if change == "method":
        method = "DELETE"
    elif change == "path":
        path += "/other"
    elif change == "body":
        body = canonicalize({})
    elif change == "noncanonical":
        body += b"\n"
    else:
        doc = parse_canonical_json(body)
        doc["can_admins_bypass" if change == "bypass" else "token"] = (
            False if change == "bypass" else "synthetic-secret"
        )
        body = canonicalize(doc)
    with pytest.raises(ValueError, match="exact role"):
        phase.spend(method, path, body, now=NOW)
    assert not (phase.directory / "01.request.json").exists()


@pytest.mark.parametrize(
    ("phase_name", "method", "path", "body"),
    [
        ("initial-controls", "POST", "/user", None),
        ("initial-controls", "GET", "/user", b"{}"),
        ("initial-controls", "GET", "https://evil.invalid/user", None),
        ("initial-controls", "GET", "/repos/hcoona/other", None),
        (
            "initial-controls",
            "GET",
            REPOSITORY + "/rulesets?includes_parents=true&per_page=100&page=6",
            None,
        ),
        (
            "initial-inventory",
            "GET",
            "/user/packages?package_type=rubygems&per_page=100&page=11",
            None,
        ),
        (
            "initial-inventory",
            "GET",
            "/users/hcoona/packages?package_type=rubygems&per_page=100&page=1",
            None,
        ),
        ("bootstrap-main", "GET", "/user", None),
    ],
)
def test_read_roles_reject_injected_endpoints_or_mutations(
    tmp_path, phase_name, method, path, body
):
    """Wrong origin, namespace, page and method never consume a valid intent."""
    phase = ledger(tmp_path / "ledger").begin(phase_name, now=NOW)
    with pytest.raises(
        ValueError, match=r"cannot mutate|outside its finite role"
    ):
        phase.spend(method, path, body, now=NOW)
    assert not (phase.directory / "01.request.json").exists()


def test_cross_destination_phase_cannot_be_borrowed(tmp_path):
    """RubyGems cannot consume the GitHub owner-package inventory allowance."""
    selected = ledger(tmp_path / "ledger", "rubygems")
    with pytest.raises(ValueError, match="outside its plan"):
        selected.begin("initial-inventory", now=NOW)
    assert not (selected.plan.directory / "initial-inventory").exists()


@pytest.mark.parametrize("operation", ["spend", "finish"])
@pytest.mark.parametrize("change", ["missing", "changed", "symlink"])
def test_active_phase_requires_original_completed_send_evidence(
    tmp_path, operation, change
):
    """Losing completed response bytes must stop further sends or closure."""
    selected = ledger(tmp_path / "ledger")
    phase = selected.begin("initial-controls", now=NOW)
    complete_get(phase)
    evidence_path = phase.directory / "01.evidence.json"
    if change == "changed":
        evidence_path.write_bytes(canonicalize({"different": True}))
    else:
        evidence_path.rename(tmp_path / "lost-evidence.json")
        if change == "symlink":
            evidence_path.symlink_to(tmp_path / "lost-evidence.json")
    actions = {
        "spend": lambda: phase.spend("GET", "/user", None, now=NOW),
        "finish": lambda: phase.finish(now=NOW),
    }
    with pytest.raises(ValueError, match=r"evidence|missing|changed"):
        actions[operation]()
    assert not (phase.directory / "02.request.json").exists()
    assert not (phase.directory / "complete.json").exists()


@pytest.mark.parametrize("operation", ["complete", "spend", "finish"])
def test_phase_clock_cannot_regress_between_public_operations(
    tmp_path, operation
):
    """Earlier caller timestamps cannot turn completed work into fresh work."""
    phase = ledger(tmp_path / "ledger").begin("initial-controls", now=NOW)
    later = NOW + timedelta(minutes=1)
    reservation = phase.spend("GET", "/user", None, now=later)
    if operation != "complete":
        phase.complete_send(reservation, EVIDENCE, successful=True, now=later)
    actions = {
        "complete": lambda: phase.complete_send(
            reservation, EVIDENCE, successful=True, now=NOW
        ),
        "spend": lambda: phase.spend("GET", "/user", None, now=NOW),
        "finish": lambda: phase.finish(now=NOW),
    }
    with pytest.raises(ValueError, match="stale"):
        actions[operation]()
    assert not (phase.directory / "02.request.json").exists()
    assert not (phase.directory / "complete.json").exists()


def test_failed_plan_flush_cannot_be_reinitialized(tmp_path, monkeypatch):
    """An interrupted initial durable write leaves a spent directory."""
    plan = RubyConfigurationPlan(
        canonicalize(plan_document(tmp_path / "ledger"))
    )

    def fail(_descriptor):
        message = "controlled initialization flush"
        raise OSError(message)

    with monkeypatch.context() as patch:
        patch.setattr(os, "fsync", fail)
        with pytest.raises(OSError, match="initialization flush"):
            initialize_ruby_configuration_ledger(plan, now=NOW)
    assert plan.directory.exists()
    with pytest.raises(FileExistsError):
        initialize_ruby_configuration_ledger(plan, now=NOW)


@pytest.mark.parametrize("destination", ["github-packages", "rubygems"])
@pytest.mark.parametrize("state", ["reserved", "completed"])
def test_lost_whole_spent_phase_blocks_reopen_reuse_and_other_phase(
    tmp_path, destination, state
):
    """Missing member state cannot renew a previously consumed mutation role."""
    selected = ledger(tmp_path / "ledger", destination)
    phase = selected.begin("marker-write", now=NOW)
    reservation = phase.spend(*mutation(destination, "marker-write"), now=NOW)
    if state == "completed":
        phase.complete_send(reservation, EVIDENCE, successful=True, now=NOW)
        phase.finish(now=NOW)
    originals = {
        path.name: path.read_bytes() for path in phase.directory.iterdir()
    }
    journal = (selected.plan.directory / "phases.jsonl").read_bytes()
    preserved = tmp_path / "preserved-spent-phase"
    phase.directory.rename(preserved)
    with pytest.raises(ValueError, match="reserved phase is missing"):
        RubyConfigurationLedger(selected.plan)
    for role in ("marker-write", "bootstrap-main"):
        with pytest.raises(ValueError, match="reserved phase is missing"):
            selected.begin(role, now=NOW + timedelta(seconds=1))
        assert not (selected.plan.directory / role).exists()
    with pytest.raises(ValueError, match="reserved phase is missing"):
        phase.spend(*mutation(destination, "marker-write"), now=NOW)
    assert {
        path.name: path.read_bytes() for path in preserved.iterdir()
    } == originals
    assert (selected.plan.directory / "phases.jsonl").read_bytes() == journal
    assert (
        selected.plan.directory / "plan.json"
    ).read_bytes() == selected.plan.content
    assert parse_canonical_json(reservation)["ordinal"] == 1
    assert not (preserved / "02.request.json").exists()


@pytest.mark.parametrize(
    "change",
    [
        "missing",
        "empty",
        "partial-header",
        "partial-entry",
        "wrong-plan",
        "missing-entry",
        "duplicate-entry",
        "wrong-phase-digest",
        "symlink",
    ],
)
def test_missing_partial_or_unjoined_phase_journal_stops_all_ledger_use(
    tmp_path, change
):
    """Missing or inconsistent inventory cannot be silently reconstructed."""
    selected = ledger(tmp_path / "ledger")
    phase = selected.begin("initial-controls", now=NOW)
    complete_get(phase)
    path = selected.plan.directory / "phases.jsonl"
    original = path.read_bytes()
    preserved = tmp_path / "preserved-phases.jsonl"
    path.rename(preserved)
    lines = original.splitlines(keepends=True)
    replacements = {
        "empty": b"",
        "partial-header": lines[0][:-1],
        "partial-entry": original[:-1],
        "missing-entry": lines[0],
        "duplicate-entry": original + lines[1],
    }
    if change == "symlink":
        path.symlink_to(preserved)
    elif change in replacements:
        path.write_bytes(replacements[change])
    elif change in {"wrong-plan", "wrong-phase-digest"}:
        index = 0 if change == "wrong-plan" else 1
        record = parse_canonical_json(lines[index][:-1])
        record["plan-digest" if index == 0 else "phase-digest"] = (
            "sha256:" + "e" * 64
        )
        lines[index] = canonicalize(record) + b"\n"
        path.write_bytes(b"".join(lines))
    with pytest.raises(ValueError, match=r"Ruby|canonical"):
        RubyConfigurationLedger(selected.plan)
    with pytest.raises(ValueError, match=r"Ruby|canonical"):
        selected.begin("bootstrap-main", now=NOW)
    with pytest.raises(ValueError, match=r"Ruby|canonical"):
        phase.spend("GET", "/user", None, now=NOW)
    with pytest.raises(ValueError, match=r"Ruby|canonical"):
        phase.finish(now=NOW)
    assert preserved.read_bytes() == original
    assert not (selected.plan.directory / "bootstrap-main").exists()
    assert not (phase.directory / "02.request.json").exists()
    assert not (phase.directory / "complete.json").exists()


@pytest.mark.parametrize(
    "failure", ["partial-append", "append-fsync", "mkdir", "phase-write"]
)
def test_interrupted_phase_journal_transition_cannot_reallocate(  # noqa: C901 - four durable transition failures
    tmp_path, monkeypatch, failure
):
    """A partial journal or interrupted directory creation remains terminal."""
    selected = ledger(tmp_path / "ledger")
    journal_path = selected.plan.directory / "phases.jsonl"
    header = journal_path.read_bytes()
    directory = selected.plan.directory / "marker-write"
    native_write, native_fsync = os.write, os.fsync
    native_mkdir, native_open = Path.mkdir, Path.open

    def write(descriptor, content):
        if failure == "partial-append":
            return native_write(descriptor, content[:7])
        return native_write(descriptor, content)

    def fsync(descriptor):
        if failure == "append-fsync":
            message = "controlled journal flush failure"
            raise OSError(message)
        return native_fsync(descriptor)

    def mkdir(path, *args, **kwargs):
        if failure == "mkdir" and path == directory:
            message = "controlled phase directory failure"
            raise OSError(message)
        return native_mkdir(path, *args, **kwargs)

    def opened(path, *args, **kwargs):
        if failure == "phase-write" and path == directory / "phase.json":
            with native_open(path, "xb") as stream:
                stream.write(b'{"partial":')
            message = "controlled phase file failure"
            raise OSError(message)
        return native_open(path, *args, **kwargs)

    with monkeypatch.context() as patch:
        patch.setattr(os, "write", write)
        patch.setattr(os, "fsync", fsync)
        patch.setattr(Path, "mkdir", mkdir)
        patch.setattr(Path, "open", opened)
        with pytest.raises(OSError, match=r"partial|controlled"):
            selected.begin("marker-write", now=NOW)
    retained = journal_path.read_bytes()
    assert retained.startswith(header)
    assert len(retained) > len(header)
    if failure == "partial-append":
        assert not retained.endswith(b"\n")
    else:
        entry = parse_canonical_json(retained.splitlines()[1])
        assert entry["phase"] == "marker-write"
        assert entry["phase-digest"].startswith("sha256:")
    for role in ("marker-write", "bootstrap-main"):
        with pytest.raises(ValueError, match=r"Ruby|canonical|Expecting"):
            selected.begin(role, now=NOW + timedelta(seconds=1))
    with pytest.raises(ValueError, match=r"Ruby|canonical|Expecting"):
        RubyConfigurationLedger(selected.plan)
    assert journal_path.read_bytes() == retained
    assert not (directory / "01.request.json").exists()
    assert not (selected.plan.directory / "bootstrap-main").exists()


def test_phase_journal_preserves_normal_progression_without_quota_transfer(
    tmp_path,
):
    """Completed phases retain membership and fixed later allocations."""
    selected = ledger(tmp_path / "ledger")
    first = selected.begin("initial-controls", now=NOW)
    complete_get(first)
    first.finish(now=NOW)
    second = RubyConfigurationLedger(selected.plan).begin(
        "marker-write", now=NOW
    )
    reservation = second.spend(
        *mutation("github-packages", "marker-write"), now=NOW
    )
    second.complete_send(reservation, EVIDENCE, successful=True, now=NOW)
    with pytest.raises(ValueError, match="exhausted"):
        second.spend(*mutation("github-packages", "marker-write"), now=NOW)
    second.finish(now=NOW)
    following = RubyConfigurationLedger(selected.plan).begin(
        "bootstrap-main", now=NOW
    )
    assert (first.limit, second.limit, following.limit) == (32, 1, 1)
    lines = (selected.plan.directory / "phases.jsonl").read_bytes().splitlines()
    assert parse_canonical_json(lines[0]) == {
        "schema": "workflow-delivery/v3/ruby-configuration-phases-v1",
        "plan-digest": ruby_digest(selected.plan.content),
    }
    assert [parse_canonical_json(line) for line in lines[1:]] == [
        {"phase": phase.name, "phase-digest": ruby_digest(phase.content)}
        for phase in (first, second, following)
    ]
    assert not (second.directory / "02.request.json").exists()
    assert (second.directory / "01.request.json").read_bytes() == reservation
