"""Closed protected envelopes, exact requests and separate local admission."""

from copy import deepcopy
from datetime import timedelta

import pytest
from three_workflow_delivery_v3._ruby_native import ruby_digest
from three_workflow_delivery_v3.adapters.rubygems import RubyRegistry
from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.release.ruby_operation import (
    RubyOperationEnvelope,
    RubyOperationRequest,
    disabled_ruby_operation_envelope,
    ruby_operation_binding,
    validate_ruby_operation_admission,
    validate_ruby_operation_reservation,
)

from ..ruby_fixtures import ROOT
from ..ruby_integration_fixtures import NOW, TARGET
from .ruby_operation_fixtures import (
    DIGEST,
    GENERATION,
    REVIEW,
    SLOT,
    admission_document,
    current_environment,
    envelope_document,
    instant,
    request,
    request_document,
)

EXPECTED_SLOTS = tuple(
    f"{destination}-{kind}"
    for destination in ("github-packages", "rubygems")
    for kind in ("bootstrap", "normal01", "normal02", "normal03", "normal04")
)


def test_ruby_default_envelope_has_exact_disabled_finite_campaign():
    """The default factory supplies no enabled operation."""
    envelope = RubyOperationEnvelope(
        canonicalize(disabled_ruby_operation_envelope())
    )
    doc = envelope.document
    assert doc == disabled_ruby_operation_envelope()
    assert doc["slots"] == dict.fromkeys(EXPECTED_SLOTS)
    assert doc["registry-partitions"] == {
        "eligibility": 3,
        "pre-marker": 3,
        "execute": 20,
        "zero-action": 3,
        "remote-consumer": 3,
    }
    assert sum(doc["registry-partitions"].values()) == 32  # noqa: PLR2004
    assert (
        doc["maximum-uploads"]
        == doc["maximum-token-exchanges"]
        == doc["maximum-oidc-assertions"]
        == 1
    )
    for slot in EXPECTED_SLOTS:
        with pytest.raises(ValueError, match="disabled"):
            envelope.enabled_slot(slot, NOW)


def test_ruby_protected_envelope_preserves_fixed_authority_and_finite_slots():
    """Protected activation changes slots without changing campaign bounds."""
    path = (
        ROOT
        / ".github/workflow-delivery/requests/hcoona-release-smoke-ruby.json"
    )
    doc = RubyOperationEnvelope(path.read_bytes()).document
    fixed = disabled_ruby_operation_envelope()
    assert {key: value for key, value in doc.items() if key != "slots"} == {
        key: value for key, value in fixed.items() if key != "slots"
    }
    assert set(doc["slots"]) == set(EXPECTED_SLOTS)


@pytest.mark.parametrize("slot", EXPECTED_SLOTS)
def test_ruby_slot_binding_fixes_destination_workflow_and_profile(slot):
    """Each finite slot independently fixes its normal/bootstrap tuple."""
    binding = ruby_operation_binding(slot)
    destination, mode = slot.rsplit("-", 1)
    bootstrap = mode == "bootstrap"
    assert binding["destination"] == destination
    assert binding["repository"] == "hcoona/three"
    assert binding["ref"] == "refs/heads/main"
    assert binding["actor"] == binding["reviewer"] == "hcoona"
    assert binding["actor-id"] == binding["reviewer-id"] == 712433  # noqa: PLR2004
    assert binding["package"] == "hcoona-release-smoke-ruby"
    assert binding["kind"] == ("bootstrap" if bootstrap else "normal")
    assert (
        binding["workflow"]
        == "workflow-delivery-v3-ruby-"
        + ("bootstrap" if bootstrap else "smoke")
        + ".yml"
    )
    assert binding["configuration-path"] == (
        ".github/workflow-delivery/"
        + ("configuration" if bootstrap else "governance")
        + "/hcoona-release-smoke-ruby-"
        + destination
        + ".json"
    )
    assert binding["registry"] == RubyRegistry(destination).origin
    assert binding["environment"] == RubyRegistry(destination).environment
    assert (
        binding["operation-profile-digest"]
        == RubyRegistry(destination).profile_digest
    )
    selected = request(slot)
    selected.require_current(current_environment(selected), NOW)
    assert selected.document["slot"] == slot


@pytest.mark.parametrize(
    "slot",
    [
        "rubygems-normal05",
        "rubygems",
        "github-packages-normal00",
        "pypi-bootstrap",
        "../rubygems-normal01",
    ],
)
def test_ruby_operation_cannot_expand_slot_inventory(slot):
    """No alias, extra retry or other destination has a valid binding."""
    with pytest.raises(ValueError, match="finite campaign"):
        ruby_operation_binding(slot)


@pytest.mark.parametrize(
    "change",
    [
        "schema",
        "repository",
        "ref",
        "path",
        "lifetime",
        "upload",
        "assertion",
        "exchange",
        "partition",
        "partition-bool",
        "obligation",
        "extra",
        "extra-slot",
        "missing-slot",
    ],
)
def test_ruby_envelope_rejects_changed_authority_and_effect_limits(change):
    """The protected carrier cannot redefine its own fixed validator bounds."""
    doc = deepcopy(disabled_ruby_operation_envelope())
    mutations = {
        "schema": lambda: doc.update(schema="foreign"),
        "repository": lambda: doc.update(repository="hcoona/foreign"),
        "ref": lambda: doc.update(ref="refs/heads/other"),
        "path": lambda: doc.update(path="alternate.json"),
        "lifetime": lambda: doc.update(
            {"maximum-request-lifetime-seconds": 14401}
        ),
        "upload": lambda: doc.update({"maximum-uploads": 2}),
        "assertion": lambda: doc.update({"maximum-oidc-assertions": 2}),
        "exchange": lambda: doc.update({"maximum-token-exchanges": 2}),
        "partition": lambda: doc["registry-partitions"].update(execute=21),
        "partition-bool": lambda: doc["registry-partitions"].update(
            eligibility=True
        ),
        "obligation": doc["obligations"].pop,
        "extra": lambda: doc.update(extra=True),
        "extra-slot": lambda: doc["slots"].update({"rubygems-normal05": None}),
        "missing-slot": lambda: doc["slots"].pop(SLOT),
    }
    mutations[change]()
    with pytest.raises((ValueError, TypeError)):
        RubyOperationEnvelope(canonicalize(doc))


def test_ruby_default_envelope_budget_edit_cannot_modify_validator_authority():
    """Editing an emitted document cannot change the fixed request ceiling."""
    document = disabled_ruby_operation_envelope()
    partition = document["registry-partitions"]
    original = deepcopy(partition)
    try:
        partition["execute"] = 21
        with pytest.raises(ValueError, match="closed authority"):
            RubyOperationEnvelope(canonicalize(document))
    finally:
        # Keep a failing alias regression isolated from later test cases.
        partition.clear()
        partition.update(original)


@pytest.mark.parametrize(
    "change",
    [
        "generation",
        "duplicate-generation",
        "reversed",
        "over-seven-days",
        "non-utc",
        "naive",
        "binding",
        "profile",
        "configuration",
        "caller",
        "protocol",
        "github-budget",
        "extra",
        "missing",
    ],
)
def test_ruby_enabled_slot_rejects_partial_or_foreign_admission(change):
    """A finite enabled slot requires every reviewed identity and window."""
    doc = envelope_document()
    slot = doc["slots"][SLOT]
    mutations = {
        "generation": lambda: slot.update(generation="C" * 32),
        "duplicate-generation": lambda: doc["slots"].update(
            {
                "rubygems-normal02": {
                    **deepcopy(slot),
                    "binding": ruby_operation_binding("rubygems-normal02"),
                }
            }
        ),
        "reversed": lambda: slot.update({"expires-at": slot["not-before"]}),
        "over-seven-days": lambda: slot.update(
            {"expires-at": instant(NOW + timedelta(days=7, seconds=1))}
        ),
        "non-utc": lambda: slot.update(
            {"not-before": "2026-09-30T12:00:00+00:00"}
        ),
        "naive": lambda: slot.update({"not-before": "2026-09-30T12:00:00"}),
        "binding": lambda: slot["binding"].update(
            destination="github-packages"
        ),
        "profile": lambda: slot["binding"].update(
            {"operation-profile-digest": "sha256:" + "f" * 64}
        ),
        "configuration": lambda: slot.update({"configuration-digest": "bad"}),
        "caller": lambda: slot.update({"caller-digest": "bad"}),
        "protocol": lambda: slot.update({"protocol-digest": "bad"}),
        "github-budget": lambda: slot.update({"github-budget-digest": "bad"}),
        "extra": lambda: slot.update(extra=True),
        "missing": lambda: slot.pop("caller-digest"),
    }
    mutations[change]()
    with pytest.raises((ValueError, TypeError)):
        RubyOperationEnvelope(canonicalize(doc))


@pytest.mark.parametrize("when", ["before", "start", "last", "expiry", "naive"])
def test_ruby_slot_admission_window_has_inclusive_start_exclusive_end(when):
    """A seven-day enablement window cannot admit a late request."""
    envelope = request().envelope
    now = {
        "before": NOW - timedelta(microseconds=1),
        "start": NOW,
        "last": NOW + timedelta(days=7) - timedelta(microseconds=1),
        "expiry": NOW + timedelta(days=7),
        "naive": NOW.replace(tzinfo=None),
    }[when]
    if when in {"start", "last"}:
        assert envelope.enabled_slot(SLOT, now)["generation"] == GENERATION
    else:
        with pytest.raises(ValueError, match="admission window"):
            envelope.enabled_slot(SLOT, now)


@pytest.mark.parametrize(
    "change",
    [
        "schema",
        "target",
        "control",
        "tree",
        "nbgv-target",
        "nbgv-private",
        "native-version",
        "manifest",
        "envelope",
        "slot",
        "generation",
        "binding",
        "configuration-digest",
        "caller-digest",
        "protocol-digest",
        "github-budget-digest",
        "expired",
        "over-four-hours",
        "outside-slot",
        "extra",
        "missing",
    ],
)
def test_ruby_exact_request_rejects_drift_or_extended_authority(change):
    """Exact bytes bind one target and bounded reviewed inputs."""
    envelope = request().envelope
    doc = request_document(envelope)
    mutations = {
        "schema": lambda: doc.update(schema="foreign"),
        "target": lambda: doc.update(target="A" * 40),
        "control": lambda: doc.update(control="f" * 40),
        "tree": lambda: doc.update(tree="short"),
        "nbgv-target": lambda: doc["nbgv"]["raw"].update(GitCommitId="f" * 40),
        "nbgv-private": lambda: doc["nbgv"]["raw"].update(PublicRelease=False),
        "native-version": lambda: doc["nbgv"].update(
            {"native-version": "0.1.0.pre.beta.8"}
        ),
        "manifest": lambda: doc.update({"source-manifest-digest": "bad"}),
        "envelope": lambda: doc.update({"envelope-digest": DIGEST}),
        "slot": lambda: doc.update(slot="rubygems-normal02"),
        "generation": lambda: doc.update(generation="d" * 32),
        "binding": lambda: doc["binding"].update(workflow="foreign.yml"),
        "expired": lambda: doc.update({"expires-at": doc["issued-at"]}),
        "over-four-hours": lambda: doc.update(
            {"expires-at": instant(NOW + timedelta(hours=4, seconds=1))}
        ),
        "outside-slot": lambda: doc.update(
            {"issued-at": instant(NOW - timedelta(seconds=1))}
        ),
        "extra": lambda: doc.update({"artifact-reference": {}}),
        "missing": lambda: doc.pop("tree"),
    }
    if change.endswith("-digest"):
        doc[change] = "sha256:" + "f" * 64
    else:
        mutations[change]()
    with pytest.raises((ValueError, TypeError)):
        RubyOperationRequest(canonicalize(doc), envelope)


@pytest.mark.parametrize("extra_microseconds", [0, 1])
def test_ruby_request_expiry_cannot_outlive_shorter_enabled_slot(
    extra_microseconds,
):
    """The slot's earlier deadline wins over the four-hour request maximum."""
    document = envelope_document()
    document["slots"][SLOT]["expires-at"] = instant(NOW + timedelta(hours=1))
    envelope = RubyOperationEnvelope(canonicalize(document))
    doc = request_document(envelope)
    doc["expires-at"] = instant(
        NOW + timedelta(hours=1, microseconds=extra_microseconds)
    )
    if extra_microseconds:
        with pytest.raises(ValueError, match="request lifetime"):
            RubyOperationRequest(canonicalize(doc), envelope)
    else:
        selected = RubyOperationRequest(canonicalize(doc), envelope)
        assert (
            selected.document["expires-at"]
            == document["slots"][SLOT]["expires-at"]
        )


@pytest.mark.parametrize("kind", ["envelope", "request", "admission"])
@pytest.mark.parametrize("encoding", ["whitespace", "duplicate", "nonobject"])
def test_ruby_operation_records_require_exact_canonical_closed_bytes(
    kind, encoding
):
    """Equivalent or ambiguous JSON cannot silently acquire another identity."""
    selected = request()
    original, parser = {
        "envelope": (selected.envelope.content, RubyOperationEnvelope),
        "request": (
            selected.content,
            lambda raw: RubyOperationRequest(raw, selected.envelope),
        ),
        "admission": (
            canonicalize(admission_document(selected)),
            lambda raw: validate_ruby_operation_admission(
                raw, selected, review=REVIEW
            ),
        ),
    }[kind]
    raw = {
        "whitespace": original + b"\n",
        "duplicate": b'{"schema":"a","schema":"b"}',
        "nonobject": b"[]",
    }[encoding]
    with pytest.raises((ValueError, TypeError)):
        parser(raw)


@pytest.mark.parametrize(
    "field",
    [
        "GITHUB_REPOSITORY",
        "GITHUB_REF",
        "GITHUB_EVENT_NAME",
        "GITHUB_ACTOR",
        "GITHUB_ACTOR_ID",
        "GITHUB_TRIGGERING_ACTOR",
        "GITHUB_RUN_ATTEMPT",
        "GITHUB_SHA",
        "GITHUB_WORKFLOW_SHA",
        "GITHUB_WORKFLOW_REF",
    ],
)
def test_ruby_current_run_rejects_each_foreign_platform_binding(field):
    """Neither a run scalar nor an earlier review replaces current identity."""
    selected = request()
    environment = current_environment(selected)
    environment[field] = "foreign"
    with pytest.raises(ValueError, match="execution differs"):
        selected.require_current(environment, NOW)


@pytest.mark.parametrize("run", ["", "0", "-1", "01", "1.0", "true", "991\n"])
def test_ruby_current_run_requires_exact_positive_native_run_id(run):
    """Coerced and missing run IDs cannot join operational evidence."""
    selected = request()
    environment = current_environment(selected)
    environment["GITHUB_RUN_ID"] = run
    with pytest.raises(ValueError, match="native current run"):
        selected.require_current(environment, NOW)


@pytest.mark.parametrize("seconds", [-1, 0, 14399, 14400])
def test_ruby_current_run_enforces_request_lifetime_without_main_tip_equality(
    seconds,
):
    """Runtime stays pinned to T after unrelated main advancement."""
    doc = envelope_document()
    doc["slots"][SLOT]["not-before"] = instant(NOW - timedelta(hours=1))
    doc["slots"][SLOT]["expires-at"] = instant(NOW + timedelta(days=1))
    envelope = RubyOperationEnvelope(canonicalize(doc))
    selected = RubyOperationRequest(
        canonicalize(request_document(envelope)), envelope
    )
    environment = current_environment(selected)
    environment["UNRELATED_CURRENT_MAIN_SHA"] = "f" * 40
    if 0 <= seconds < 14400:  # noqa: PLR2004
        selected.require_current(environment, NOW + timedelta(seconds=seconds))
        assert (
            selected.document["target"]
            == selected.document["control"]
            == TARGET
        )
    else:
        with pytest.raises(ValueError, match="execution differs"):
            selected.require_current(
                environment, NOW + timedelta(seconds=seconds)
            )


@pytest.mark.parametrize(
    "change",
    [
        "schema",
        "request",
        "caller",
        "review",
        "ledger",
        "same-author",
        "empty-review",
        "verdict",
        "carrier",
        "review-too-early",
        "review-expiry",
        "extra",
    ],
)
def test_ruby_admission_requires_separate_review_bound_to_exact_request(change):
    """A digest alone cannot supply independent review or caller approval."""
    selected = request()
    review = b" " if change == "empty-review" else REVIEW
    doc = admission_document(selected, review=review)
    changes = {
        "schema": lambda: doc.update(schema="foreign"),
        "request": lambda: doc.update({"request-digest": DIGEST}),
        "caller": lambda: doc.update({"caller-digest": "sha256:" + "f" * 64}),
        "review": lambda: doc.update({"review-digest": DIGEST}),
        "ledger": lambda: doc.update({"ledger-digest": "invalid"}),
        "same-author": lambda: doc.update(reviewer=doc["author"]),
        "empty-review": lambda: None,
        "verdict": lambda: doc.update(verdict="pending"),
        "carrier": lambda: doc.update(
            carrier="https://github.com/hcoona/three/issues/953#issuecomment-123"
        ),
        "review-too-early": lambda: doc.update(
            {"reviewed-at": instant(NOW - timedelta(seconds=1))}
        ),
        "review-expiry": lambda: doc.update(
            {"reviewed-at": selected.document["expires-at"]}
        ),
        "extra": lambda: doc.update(extra=True),
    }
    changes[change]()
    with pytest.raises((ValueError, TypeError)):
        validate_ruby_operation_admission(
            canonicalize(doc), selected, review=review
        )


def reservation_document(selected, admission):
    """Construct a modeled retained reservation for public import tests."""
    return {
        "schema": "workflow-delivery/v3/ruby-operation-reservation-v1",
        "ledger-digest": DIGEST,
        "slot": SLOT,
        "generation": GENERATION,
        "request-digest": selected.digest,
        "admission-digest": ruby_digest(admission),
        "reserved-at": instant(NOW + timedelta(minutes=2)),
    }


def test_ruby_admission_and_reservation_join_exact_separate_documents():
    """Closed runtime evidence preserves request, reviewer and slot identity."""
    selected = request()
    admitted = admission_document(selected)
    admission = canonicalize(admitted)
    assert (
        validate_ruby_operation_admission(admission, selected, review=REVIEW)
        == admitted
    )
    reserved = reservation_document(selected, admission)
    assert (
        validate_ruby_operation_reservation(
            canonicalize(reserved), selected, admission, review=REVIEW
        )
        == reserved
    )
    assert selected.digest == ruby_digest(selected.content)
    assert selected.document["nbgv"]["native-version"] == "0.1.0.pre.beta.7"


@pytest.mark.parametrize(
    "change",
    [
        "schema",
        "ledger",
        "request",
        "admission",
        "slot",
        "generation",
        "early",
        "expiry",
        "extra",
        "noncanonical",
    ],
)
def test_ruby_reservation_import_cannot_rebind_or_extend_admission(change):
    """Stored reservations cannot change target, review or time."""
    selected = request()
    admission = canonicalize(admission_document(selected))
    doc = reservation_document(selected, admission)
    changes = {
        "schema": lambda: doc.update(schema="foreign"),
        "ledger": lambda: doc.update({"ledger-digest": "sha256:" + "f" * 64}),
        "request": lambda: doc.update({"request-digest": DIGEST}),
        "admission": lambda: doc.update({"admission-digest": DIGEST}),
        "slot": lambda: doc.update(slot="rubygems-normal02"),
        "generation": lambda: doc.update(generation="f" * 32),
        "early": lambda: doc.update({"reserved-at": instant(NOW)}),
        "expiry": lambda: doc.update(
            {"reserved-at": selected.document["expires-at"]}
        ),
        "extra": lambda: doc.update(extra=True),
        "noncanonical": lambda: None,
    }
    changes[change]()
    content = canonicalize(doc) + (b"\n" if change == "noncanonical" else b"")
    with pytest.raises((ValueError, TypeError)):
        validate_ruby_operation_reservation(
            content, selected, admission, review=REVIEW
        )
