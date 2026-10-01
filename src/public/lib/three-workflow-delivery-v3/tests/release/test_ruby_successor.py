"""Linked successor spending and strict version boundaries."""

# Contract counts are literal; fault injection targets the durable write seam.
# ruff: noqa: PLR2004, SLF001

import os
from dataclasses import replace
from datetime import timedelta
from pathlib import Path
from types import SimpleNamespace

import pytest
from three_workflow_delivery_v3._ruby_native import ruby_digest
from three_workflow_delivery_v3.acceptance.ruby_bootstrap_contract import (
    RubyBootstrapInputs,
    admit_ruby_bootstrap,
)
from three_workflow_delivery_v3.canonical import (
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.release import ruby_operation_ledger as storage
from three_workflow_delivery_v3.release.ruby_configuration import (
    RubyFirstProjectInspection,
)
from three_workflow_delivery_v3.release.ruby_operation import (
    RubyOperationEnvelope,
    RubyOperationRequest,
    disabled_ruby_operation_envelope,
    ruby_operation_binding,
)

from ..ruby_integration_fixtures import NOW
from .ruby_configuration_fixtures import (
    configuration,
    inspection_document,
    review_document,
)
from .ruby_operation_fixtures import (
    REVIEW,
    admission_document,
    current_environment,
    envelope_document,
    instant,
    request,
    request_document,
)

OLD = "github-packages-bootstrap"
NEW = "github-packages-bootstrap02"
V1_SLOTS = tuple(
    f"{destination}-{kind}"
    for destination in ("github-packages", "rubygems")
    for kind in ("bootstrap", "normal01", "normal02", "normal03", "normal04")
)


def v2_request(slot=NEW, *, config=None, generation=None):
    """Create coherent modeled request bytes, not real reviewer provenance."""
    document = disabled_ruby_operation_envelope(version=2)
    document["slots"][slot] = envelope_document(slot)["slots"][slot]
    if config is not None:
        document["slots"][slot]["configuration-digest"] = config.digest
    if generation is not None:
        document["slots"][slot]["generation"] = generation
    envelope = RubyOperationEnvelope(canonicalize(document))
    selected = request_document(envelope, slot)
    if config is not None:
        selected["expires-at"] = instant(NOW + timedelta(hours=2))
    return RubyOperationRequest(canonicalize(selected), envelope)


def reserve(ledger, selected):
    """Pass real exact admission validation before durable local spending."""
    admission = canonicalize(
        admission_document(selected, ruby_digest(ledger.content))
    )
    return ledger.reserve(
        selected, admission, review=REVIEW, now=NOW + timedelta(minutes=2)
    )


def snapshot(directory):
    """Observe bytes and links without following a substituted target."""
    return {
        path.name: ("link", str(path.readlink()))
        if path.is_symlink()
        else ("file", path.read_bytes())
        for path in directory.iterdir()
    }


@pytest.fixture
def predecessor(tmp_path):
    """Generate the only admissible old history using original v1 APIs."""
    directory = tmp_path / "predecessor"
    header = storage.initialize_ruby_operation_ledger(directory, "a" * 32)
    old = storage.RubyOperationLedger(directory)
    selected = request(OLD)
    reservation = reserve(old, selected)
    members = {
        path.name: ruby_digest(path.read_bytes())
        for path in directory.iterdir()
    }
    return SimpleNamespace(
        directory=directory,
        old=old,
        header=header,
        request=selected,
        reservation=reservation,
        members=members,
        original=snapshot(directory),
        target=tmp_path / "successor",
        anchor=directory.with_name(directory.name + ".successor.json"),
    )


def initialize(case, *, target=None, campaign="b" * 32):
    """Only temporary synthetic predecessors are ever opened by these tests."""
    return storage.initialize_ruby_successor_ledger(
        target or case.target,
        campaign,
        predecessor_directory=case.directory,
        predecessor_members=case.members,
    )


@pytest.fixture
def successor(predecessor):
    """Open an actual successor over synthetic admitted history."""
    initialize(predecessor)
    predecessor.ledger = storage.RubyOperationLedger(predecessor.target)
    return predecessor


def test_ruby_v2_envelope_has_only_one_disabled_successor():
    """The new inventory preserves original slots and effect limits."""
    old = RubyOperationEnvelope(
        canonicalize(disabled_ruby_operation_envelope())
    )
    new = RubyOperationEnvelope(
        canonicalize(disabled_ruby_operation_envelope(version=2))
    )
    assert old.version == 1
    assert new.version == 2
    assert old.document["slots"] == dict.fromkeys(V1_SLOTS)
    assert new.document["slots"] == dict.fromkeys((*V1_SLOTS, NEW))
    assert {
        key: value
        for key, value in new.document.items()
        if key not in {"slots", "schema"}
    } == {
        key: value
        for key, value in old.document.items()
        if key not in {"slots", "schema"}
    }
    with pytest.raises(ValueError, match="disabled"):
        new.enabled_slot(NEW, NOW)


def test_ruby_successor_slot_uses_existing_bootstrap_binding():
    """The additional slot retains the bootstrap configuration."""
    selected = v2_request()
    binding = selected.document["binding"]
    assert binding == ruby_operation_binding(OLD)
    assert binding["kind"] == "bootstrap"
    assert binding["workflow"] == "workflow-delivery-v3-ruby-bootstrap.yml"
    assert (
        binding["configuration-path"]
        == ".github/workflow-delivery/configuration/"
        "hcoona-release-smoke-ruby-github-packages.json"
    )
    selected.require_current(current_environment(selected), NOW)


@pytest.mark.parametrize(
    "change", ["new-in-v1", "missing", "extra", "old-enabled", "schema"]
)
def test_ruby_versioned_envelope_keeps_historical_inventory_closed(change):
    """Neither historical decoding nor v2 can silently widen one-shot slots."""
    document = disabled_ruby_operation_envelope(version=2)
    if change == "new-in-v1":
        document["schema"] = "workflow-delivery/v3/ruby-operation-envelope-v1"
    elif change == "missing":
        document["slots"].pop(NEW)
    elif change == "extra":
        document["slots"]["rubygems-bootstrap02"] = None
    elif change == "old-enabled":
        document["slots"][OLD] = envelope_document(OLD)["slots"][OLD]
    else:
        document["schema"] = "workflow-delivery/v3/ruby-operation-envelope-v3"
    with pytest.raises(ValueError, match=r"Ruby|JSON|canonical"):
        RubyOperationEnvelope(canonicalize(document))


def test_ruby_successor_preserves_predecessor_and_binds_one_location(
    successor,
):
    """New state inherits exact originals without editing the spent ledger."""
    case = successor
    header = parse_canonical_json(case.ledger.content)
    anchor = parse_canonical_json(case.anchor.read_bytes())
    assert case.old.version == 1
    assert case.ledger.version == 2
    assert header["predecessor"] == {
        "directory": str(case.directory),
        "campaign-digest": ruby_digest(case.header),
        "members": case.members,
    }
    assert anchor["directory"] == str(case.target)
    assert anchor["campaign-digest"] == ruby_digest(case.ledger.content)
    assert anchor["campaign"] == header["campaign"]
    before = snapshot(case.target)
    for target in (case.target, case.target.with_name("alternate")):
        with pytest.raises((ValueError, FileExistsError)):
            initialize(case, target=target, campaign="c" * 32)
    assert not case.target.with_name("alternate").exists()
    assert snapshot(case.target) == before
    assert snapshot(case.directory) == case.original


@pytest.mark.parametrize(
    "stage", ["anchor-fsync", "directory", "header", "journal"]
)
def test_ruby_successor_partial_initialization_never_selects_another_path(
    predecessor, monkeypatch, stage
):
    """The anchor is already spent when any successor-state creation fails."""
    case = predecessor
    mkdir, durable, fsync = Path.mkdir, storage._durable_new, os.fsync

    def fail_sync(descriptor):
        if (
            stage == "anchor-fsync"
            and case.anchor.exists()
            and os.fstat(descriptor).st_ino == case.anchor.stat().st_ino
        ):
            message = "controlled successor anchor fsync failure"
            raise OSError(message)
        return fsync(descriptor)

    def fail_mkdir(path, *args, **kwargs):
        if path == case.target and stage == "directory":
            assert case.anchor.is_file()
            message = "controlled successor mkdir failure"
            raise OSError(message)
        return mkdir(path, *args, **kwargs)

    def fail_write(path, content):
        name = "campaign.json" if stage == "header" else "events.jsonl"
        if path == case.target / name:
            assert case.anchor.is_file()
            message = "controlled successor file failure"
            raise OSError(message)
        return durable(path, content)

    with monkeypatch.context() as patch:
        patch.setattr(Path, "mkdir", fail_mkdir)
        patch.setattr(storage, "_durable_new", fail_write)
        patch.setattr(os, "fsync", fail_sync)
        with pytest.raises(OSError, match="controlled successor"):
            initialize(case)
    anchor = case.anchor.read_bytes()
    partial = snapshot(case.target) if case.target.exists() else None
    for target in (case.target, case.target.with_name("alternate")):
        with pytest.raises((ValueError, FileExistsError)):
            initialize(case, target=target)
    assert case.anchor.read_bytes() == anchor
    assert (snapshot(case.target) if case.target.exists() else None) == partial
    assert not case.target.with_name("alternate").exists()
    assert snapshot(case.directory) == case.original


@pytest.mark.parametrize(
    "change",
    [
        "missing",
        "changed",
        "extra-normal",
        "completion",
        "admitted-digest",
        "hardlink",
    ],
)
def test_ruby_successor_rejects_incomplete_predecessor_before_creation(
    predecessor, change
):
    """Extra prior spending cannot reset the campaign."""
    case = predecessor
    path = case.directory / (OLD + ".json")
    if change == "missing":
        path.rename(case.directory.parent / "lost-reservation")
    elif change == "changed":
        path.write_bytes(b"{}")
    elif change == "extra-normal":
        reserve(case.old, request("github-packages-normal01"))
        case.members = {
            p.name: ruby_digest(p.read_bytes())
            for p in case.directory.iterdir()
        }
    elif change == "completion":
        case.old.stop_destination(
            "github-packages", {"fixture": "admitted completion"}
        )
        case.members = {
            p.name: ruby_digest(p.read_bytes())
            for p in case.directory.iterdir()
        }
    elif change == "hardlink":
        os.link(path, case.directory.parent / "aliased-reservation")
    else:
        case.members[OLD + ".json"] = "sha256:" + "9" * 64
    before = snapshot(case.directory)
    with pytest.raises((ValueError, FileNotFoundError)):
        initialize(case)
    assert not case.anchor.exists()
    assert not case.target.exists()
    assert snapshot(case.directory) == before


def test_ruby_successor_inherits_spending_and_reserves_each_remaining_slot_once(
    successor,
):
    """Inherited spending leaves exactly one additional bootstrap."""
    case = successor
    slots = (NEW, *(slot for slot in V1_SLOTS if slot != OLD))
    for run_id, slot in enumerate(slots, start=991):
        selected = v2_request(slot)
        reserved = reserve(case.ledger, selected)
        joined = case.ledger.join_run(selected, reserved, run_id)
        assert parse_canonical_json(reserved)["slot"] == slot
        assert parse_canonical_json(reserved)["ledger-digest"] == ruby_digest(
            case.ledger.content
        )
        assert parse_canonical_json(joined)[
            "reservation-digest"
        ] == ruby_digest(reserved)
        before = snapshot(case.target)
        with pytest.raises((ValueError, FileExistsError)):
            reserve(case.ledger, v2_request(slot, generation="e" * 32))
        assert snapshot(case.target) == before
        case.ledger = storage.RubyOperationLedger(case.target)
    assert not (case.target / (OLD + ".json")).exists()
    records = [
        parse_canonical_json(path.read_bytes())
        for path in case.target.glob("*.json")
    ]
    recorded_slots = [
        record["slot"]
        for record in records
        if record["schema"]
        == "workflow-delivery/v3/ruby-operation-reservation-v1"
    ]
    assert set(recorded_slots) == set(slots)
    assert (
        len(
            [
                slot
                for slot in recorded_slots
                if slot.startswith("github-packages-normal")
            ]
        )
        == 4
    )
    assert (
        len(
            [
                slot
                for slot in recorded_slots
                if slot.startswith("rubygems-normal")
            ]
        )
        == 4
    )
    assert sorted(slot for slot in recorded_slots if "bootstrap" in slot) == [
        NEW,
        "rubygems-bootstrap",
    ]
    assert snapshot(case.directory) == case.original


@pytest.mark.parametrize("destination", ["github-packages", "rubygems"])
def test_ruby_successor_completion_stops_only_its_destination(
    successor, destination
):
    """Admitted completion stops spending; caller owns audit provenance."""
    case = successor
    case.ledger.stop_destination(
        destination, {"fixture": "independently admitted normal completion"}
    )
    before = snapshot(case.target)
    for slot in (NEW, *(slot for slot in V1_SLOTS if slot != OLD)):
        if slot.startswith(destination + "-"):
            with pytest.raises(ValueError, match="complete"):
                reserve(case.ledger, v2_request(slot))
            assert snapshot(case.target) == before
    other = (
        "rubygems-normal01"
        if destination == "github-packages"
        else "github-packages-normal01"
    )
    assert (
        parse_canonical_json(reserve(case.ledger, v2_request(other)))["slot"]
        == other
    )
    assert snapshot(case.directory) == case.original


@pytest.mark.parametrize("version_pair", [(1, 2), (2, 1)])
def test_ruby_successor_wrong_ledger_version_cannot_spend(
    successor, version_pair
):
    """Historical records do not admit mixed-version writes."""
    case = successor
    ledger = case.old if version_pair[0] == 1 else case.ledger
    selected = (
        v2_request()
        if version_pair[1] == 2
        else request("github-packages-normal01")
    )
    before = snapshot(ledger.directory)
    with pytest.raises(ValueError, match=r"Ruby|JSON|canonical"):
        reserve(ledger, selected)
    assert snapshot(ledger.directory) == before


@pytest.mark.parametrize("change", ["ledger", "request", "review"])
def test_ruby_successor_reservation_requires_exact_independent_admission(
    successor, change
):
    """Version-correct requests still need the exact ledger and review join."""
    case = successor
    selected = v2_request()
    document = admission_document(selected, ruby_digest(case.ledger.content))
    field = {
        "ledger": "ledger-digest",
        "request": "request-digest",
        "review": "review-digest",
    }[change]
    document[field] = "sha256:" + "9" * 64
    before = snapshot(case.target)
    with pytest.raises(ValueError, match=r"Ruby|admission"):
        case.ledger.reserve(
            selected,
            canonicalize(document),
            review=REVIEW,
            now=NOW + timedelta(minutes=2),
        )
    assert snapshot(case.target) == before
    assert snapshot(case.directory) == case.original


@pytest.mark.parametrize(
    "change", ["inherited-event", "extra", "truncated-journal"]
)
def test_ruby_successor_rejects_reintroduced_or_unjoined_spending(
    successor, change
):
    """Inherited events or missing journal rows never restore slots."""
    case = successor
    reserve(case.ledger, v2_request())
    journal = case.target / "events.jsonl"
    if change == "inherited-event":
        name = OLD + ".json"
        (case.target / name).write_bytes(case.reservation)
        with journal.open("ab") as stream:
            stream.write(
                canonicalize(
                    {"name": name, "digest": ruby_digest(case.reservation)}
                )
                + b"\n"
            )
    elif change == "extra":
        (case.target / "unknown.json").write_bytes(b"{}")
    else:
        journal.write_bytes(journal.read_bytes().splitlines(keepends=True)[0])
    before = snapshot(case.target)
    with pytest.raises(ValueError, match=r"Ruby|journal"):
        storage.RubyOperationLedger(case.target)
    with pytest.raises(ValueError, match=r"Ruby|journal"):
        reserve(case.ledger, v2_request("github-packages-normal01"))
    assert snapshot(case.target) == before
    assert snapshot(case.directory) == case.original


@pytest.mark.parametrize(
    "role", ["predecessor", "anchor", "header", "journal", "reservation"]
)
@pytest.mark.parametrize(
    "change", ["missing", "changed", "symlink", "hardlink"]
)
def test_ruby_successor_history_damage_blocks_reopen_and_active_operations(
    successor, role, change
):
    """Old open objects recheck lineage before reserve, join or completion."""
    case = successor
    selected = v2_request()
    reserved = reserve(case.ledger, selected)
    path = {
        "predecessor": case.directory / (OLD + ".json"),
        "anchor": case.anchor,
        "header": case.target / "campaign.json",
        "journal": case.target / "events.jsonl",
        "reservation": case.target / (NEW + ".json"),
    }[role]
    saved = case.directory.parent / "saved-original"
    if change in {"missing", "symlink"}:
        path.rename(saved)
        if change == "symlink":
            path.symlink_to(saved)
    elif change == "hardlink":
        os.link(path, saved)
    else:
        path.write_bytes(path.read_bytes()[:-1])
    retained = snapshot(case.target), snapshot(case.directory)
    for action in (
        lambda: storage.RubyOperationLedger(case.target),
        lambda: reserve(case.ledger, v2_request("github-packages-normal01")),
        lambda: case.ledger.join_run(selected, reserved, 991),
        lambda: case.ledger.stop_destination(
            "github-packages", {"fixture": "completion"}
        ),
    ):
        with pytest.raises((ValueError, FileNotFoundError)):
            action()
        assert (snapshot(case.target), snapshot(case.directory)) == retained
    assert not (case.target / (NEW + ".run.json")).exists()
    assert not (case.target / "github-packages.complete.json").exists()


@pytest.mark.parametrize("destination", ["github-packages", "rubygems"])
def test_ruby_versioned_inspection_joins_current_destination_under_v2(
    destination,
):
    """Only GitHub uses inspection-v2; RubyGems v1 still joins a v2 campaign."""
    config = configuration(destination)
    document = inspection_document(config)
    slot = destination + "-bootstrap"
    if destination == "github-packages":
        document["schema"] = (
            "workflow-delivery/v3/ruby-first-project-inspection-v2"
        )
        document["slot"] = slot = NEW
    inspected = RubyFirstProjectInspection(canonicalize(document), config)
    selected = v2_request(slot, config=config)
    inspected.require_request(selected, NOW, before_dispatch=True)
    assert inspected.document["slot"] == selected.document["slot"]
    assert inspected.document["configuration-digest"] == config.digest
    with pytest.raises(ValueError, match="inspection lifetime"):
        inspected.require_request(
            selected, NOW + timedelta(minutes=10), before_dispatch=True
        )


@pytest.mark.parametrize(
    "change",
    ["rubygems-v2", "old-slot-v2", "new-slot-v1", "extra", "wrong-envelope"],
)
def test_ruby_successor_inspection_cannot_rebind_version_or_destination(change):
    """New inspection cannot rebind a historical or foreign bootstrap."""
    config = configuration(
        "rubygems" if change == "rubygems-v2" else "github-packages"
    )
    document = inspection_document(config)
    document["schema"] = "workflow-delivery/v3/ruby-first-project-inspection-v2"
    document["slot"] = NEW
    if change == "old-slot-v2":
        document["slot"] = OLD
    elif change == "new-slot-v1":
        document["schema"] = (
            "workflow-delivery/v3/ruby-first-project-inspection-v1"
        )
    elif change == "extra":
        document["request-digest"] = "sha256:" + "9" * 64
    if change == "wrong-envelope":
        inspected = RubyFirstProjectInspection(canonicalize(document), config)
        with pytest.raises(ValueError, match="v2 envelope"):
            inspected.require_request(request(OLD), NOW)
    else:
        with pytest.raises(ValueError, match=r"Ruby|JSON|canonical"):
            RubyFirstProjectInspection(canonicalize(document), config)


def hosted_chain(case, destination="github-packages"):
    """Join real local ledger records with modeled hosted inputs."""
    config = configuration(destination)
    document = inspection_document(config)
    slot = destination + "-bootstrap"
    if destination == "github-packages":
        slot = NEW
        document.update(
            schema="workflow-delivery/v3/ruby-first-project-inspection-v2",
            slot=slot,
        )
    inspected = RubyFirstProjectInspection(canonicalize(document), config)
    selected = v2_request(slot, config=config)
    review = canonicalize(review_document(selected, config, inspected))
    admission = canonicalize(
        admission_document(selected, ruby_digest(case.ledger.content), review)
    )
    reserved = case.ledger.reserve(
        selected, admission, review=review, now=NOW + timedelta(minutes=2)
    )
    inputs = RubyBootstrapInputs(
        selected, config, inspected, review, admission, reserved
    )
    joined = case.ledger.join_run(selected, reserved, 991)
    return inputs, joined, current_environment(selected)


@pytest.mark.parametrize("destination", ["github-packages", "rubygems"])
def test_hosted_bootstrap_admits_complete_v2_destination_chain(
    successor, destination
):
    """The actual hosted boundary accepts only its exactly joined bootstrap."""
    inputs, joined, environment = hosted_chain(successor, destination)
    before = snapshot(successor.target), snapshot(successor.directory)
    run = admit_ruby_bootstrap(
        inputs, joined, environment, NOW + timedelta(minutes=2)
    )
    assert run.inputs == inputs
    assert run.request.envelope.version == 2
    assert run.request.document["slot"] == (
        NEW if destination == "github-packages" else "rubygems-bootstrap"
    )
    assert run.run_id == 991
    assert run.to_document() == {
        "schema": "workflow-delivery/v3/ruby-bootstrap-run-v1",
        "purpose": "destination-bootstrap",
        "request-digest": inputs.request.digest,
        "configuration-digest": inputs.configuration.digest,
        "inspection-digest": ruby_digest(inputs.inspection.content),
        "review-digest": ruby_digest(inputs.review),
        "admission-digest": ruby_digest(inputs.admission),
        "reservation-digest": ruby_digest(inputs.reservation),
        "run-join-digest": ruby_digest(joined),
        "target": inputs.request.document["target"],
        "control": inputs.request.document["target"],
        "workflow-run-id": 991,
        "run-attempt": 1,
    }
    binding = run.provider_binding()
    assert binding.request_id == (
        "ruby-bootstrap:" + inputs.request.document["slot"] + ":" + "c" * 32
    )
    assert binding.purpose == "destination-bootstrap"
    assert binding.producer == "provide-ruby-bootstrap"
    run.require_current(NOW + timedelta(minutes=3))
    assert (snapshot(successor.target), snapshot(successor.directory)) == before


@pytest.mark.parametrize(
    "change",
    [
        "v1-inspection",
        "v1-request",
        "foreign-inspection",
        "foreign-configuration",
        "normal-request",
        "review",
        "admission",
        "reservation",
        "run-join",
        "native-workflow",
        "native-attempt",
    ],
)
def test_hosted_successor_rejects_incompatible_chain_at_real_boundary(
    successor, change
):
    """The slot fix cannot bypass version, destination or original joins."""
    inputs, joined, environment = hosted_chain(successor)
    if change == "v1-inspection":
        inspected = RubyFirstProjectInspection(
            canonicalize(inspection_document(inputs.configuration)),
            inputs.configuration,
        )
        inputs = replace(inputs, inspection=inspected)
    elif change == "v1-request":
        inputs = replace(inputs, request=request(OLD))
    elif change == "foreign-inspection":
        foreign = configuration("rubygems")
        inspected = RubyFirstProjectInspection(
            canonicalize(inspection_document(foreign)), foreign
        )
        inputs = replace(inputs, inspection=inspected)
    elif change == "foreign-configuration":
        inputs = replace(inputs, configuration=configuration("rubygems"))
    elif change == "normal-request":
        inputs = replace(inputs, request=v2_request("github-packages-normal01"))
    elif change in {"review", "admission", "reservation"}:
        document = parse_canonical_json(getattr(inputs, change))
        document["request-digest"] = "sha256:" + "e" * 64
        inputs = replace(inputs, **{change: canonicalize(document)})
    elif change == "run-join":
        document = parse_canonical_json(joined)
        document["reservation-digest"] = "sha256:" + "e" * 64
        joined = canonicalize(document)
    elif change == "native-workflow":
        environment["GITHUB_WORKFLOW_REF"] = (
            "hcoona/three/.github/workflows/"
            "workflow-delivery-v3-ruby-smoke.yml@refs/heads/main"
        )
    else:
        environment["GITHUB_RUN_ATTEMPT"] = "2"
    before = snapshot(successor.target), snapshot(successor.directory)
    with pytest.raises(ValueError, match=r"Ruby|canonical|schema|keys"):
        admit_ruby_bootstrap(
            inputs, joined, environment, NOW + timedelta(minutes=2)
        )
    assert (snapshot(successor.target), snapshot(successor.directory)) == before
