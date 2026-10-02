"""Bound modeled absence evidence to a real validated operation request."""

from datetime import datetime, timedelta

import pytest
from three_workflow_delivery_v3._ruby_native import ruby_digest
from three_workflow_delivery_v3.adapters.rubygems import RubyRegistry
from three_workflow_delivery_v3.canonical import (
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.release.ruby_configuration import (
    RubyBootstrapConfiguration,
    RubyFirstProjectInspection,
    blocked_ruby_bootstrap_configuration,
)
from three_workflow_delivery_v3.release.ruby_governance import (
    RubyGovernance,
    blocked_ruby_governance,
)
from three_workflow_delivery_v3.release.ruby_operation import (
    RubyOperationEnvelope,
    RubyOperationRequest,
    disabled_ruby_operation_envelope,
)

from ..ruby_integration_fixtures import NOW, ROOT, TARGET
from .ruby_configuration_fixtures import (
    bootstrap_request,
    configuration,
    configuration_document,
    inspection,
    inspection_document,
)
from .ruby_operation_fixtures import (
    envelope_document,
    instant,
    request_document,
)

OTHER_DIGEST = "sha256:" + "e" * 64


@pytest.mark.parametrize("destination", ["github-packages", "rubygems"])
def test_inspection_joins_exact_bootstrap_without_circular_request_identity(
    destination,
):
    """Inspection joins exact bootstrap without circular request identity."""
    config = configuration(destination)
    inspected = inspection(config)
    request = bootstrap_request(config)
    inspected.require_request(request, NOW, before_dispatch=True)
    inspected.require_request(request, NOW + timedelta(hours=1))
    assert inspected.document == inspection_document(config)
    assert "request-digest" not in inspected.document
    changed_view = inspected.document
    changed_view["absence-verdict"] = "present"
    assert inspected.document["absence-verdict"] == "absent"


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("schema", "other"),
        ("destination", "github-packages"),
        ("slot", "rubygems-normal01"),
        ("generation", "C" * 32),
        ("target", "main"),
        ("configuration-digest", OTHER_DIGEST),
        ("control-baseline-digest", OTHER_DIGEST),
        ("operator-principal", {"login": "Raffle2282"}),
        ("absence-kind", "ruby-coordinate"),
        ("absence-verdict", "present"),
        ("evidence-manifest-digest", "bad"),
        ("dispatch-by", instant(NOW + timedelta(minutes=10, microseconds=1))),
        ("dispatch-by", instant(NOW + timedelta(minutes=10, microseconds=-1))),
        ("expires-at", instant(NOW + timedelta(hours=2, microseconds=1))),
        ("expires-at", instant(NOW)),
        ("observed-at", NOW.isoformat()),
    ],
)
def test_inspection_rejects_wrong_binding_absence_or_deadline(field, value):
    """Inspection rejects wrong binding absence or deadline."""
    config = configuration()
    doc = inspection_document(config)
    doc[field] = value
    with pytest.raises(ValueError, match=r"Ruby|not canonical"):
        RubyFirstProjectInspection(canonicalize(doc), config)


@pytest.mark.parametrize(
    "principal",
    [
        {"login": "foreign", "id": 712433},
        {"login": "hcoona", "id": True},
        {"kind": "unauthenticated-public-endpoint"},
    ],
)
def test_github_inspection_requires_exact_owner_principal(principal):
    """Github inspection requires exact owner principal."""
    config = configuration("github-packages")
    doc = inspection_document(config)
    doc["operator-principal"] = principal
    with pytest.raises(ValueError, match="binding differs"):
        RubyFirstProjectInspection(canonicalize(doc), config)


@pytest.mark.parametrize("mode", ["extra", "missing", "noncanonical"])
def test_inspection_schema_and_bytes_are_closed(mode):
    """Inspection schema and bytes are closed."""
    config = configuration()
    doc = inspection_document(config)
    if mode == "extra":
        doc["request-digest"] = OTHER_DIGEST
    elif mode == "missing":
        doc.pop("evidence-manifest-digest")
    content = canonicalize(doc) + (b"\n" if mode == "noncanonical" else b"")
    with pytest.raises(ValueError, match=r"Ruby|not canonical"):
        RubyFirstProjectInspection(content, config)


def test_inspection_expiry_cannot_outlive_configuration():
    """Inspection expiry cannot outlive configuration."""
    doc = configuration_document()
    doc["expires-at"] = instant(NOW + timedelta(hours=1))
    config = RubyBootstrapConfiguration(
        RubyRegistry("rubygems"), canonicalize(doc)
    )
    inspected_doc = inspection_document(config)
    with pytest.raises(ValueError, match="invalid deadlines"):
        RubyFirstProjectInspection(canonicalize(inspected_doc), config)
    inspected_doc["expires-at"] = doc["expires-at"]
    inspected = RubyFirstProjectInspection(canonicalize(inspected_doc), config)
    assert inspected.document["expires-at"] == doc["expires-at"]


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("generation", "d" * 32),
        ("configuration-digest", OTHER_DIGEST),
        ("protocol-digest", OTHER_DIGEST),
    ],
)
def test_inspection_rejects_coherent_request_for_other_envelope_binding(
    field, value
):
    """Inspection rejects coherent request for other envelope binding."""
    config = configuration()
    request = bootstrap_request(config, envelope_changes={field: value})
    with pytest.raises(ValueError, match="binding differs"):
        inspection(config).require_request(request, NOW)


def test_inspection_rejects_other_valid_target():
    """Inspection rejects other valid target."""
    config = configuration()
    doc = inspection_document(config)
    doc["target"] = "e" * 40
    inspected = RubyFirstProjectInspection(canonicalize(doc), config)
    with pytest.raises(ValueError, match="binding differs"):
        inspected.require_request(bootstrap_request(config), NOW)


def test_inspection_rejects_valid_normal_request():
    """Inspection rejects valid normal request."""
    config = configuration()
    request = bootstrap_request(config, slot="rubygems-normal01")
    with pytest.raises(ValueError, match="binding differs"):
        inspection(config).require_request(request, NOW)


@pytest.mark.parametrize(
    ("changes", "now"),
    [
        ({"issued-at": instant(NOW - timedelta(seconds=1))}, NOW),
        ({"issued-at": instant(NOW + timedelta(seconds=1))}, NOW),
        ({"expires-at": instant(NOW + timedelta(hours=2, seconds=1))}, NOW),
        (
            {"expires-at": instant(NOW + timedelta(hours=1))},
            NOW + timedelta(hours=1),
        ),
    ],
)
def test_inspection_enforces_request_issue_and_expiry(changes, now):
    """Inspection enforces request issue and expiry."""
    config = configuration()
    request = bootstrap_request(
        config,
        envelope_changes={
            "not-before": instant(NOW - timedelta(minutes=1)),
            "expires-at": instant(NOW + timedelta(days=6)),
        },
        request_changes=changes,
    )
    with pytest.raises(ValueError, match="inspection lifetime"):
        inspection(config).require_request(request, now)


def test_dispatch_deadline_is_exclusive_but_runtime_remains_valid():
    """Dispatch deadline is exclusive but runtime remains valid."""
    config = configuration()
    inspected = inspection(config)
    request = bootstrap_request(config)
    inspected.require_request(
        request,
        NOW + timedelta(minutes=10, microseconds=-1),
        before_dispatch=True,
    )
    with pytest.raises(ValueError, match="inspection lifetime"):
        inspected.require_request(
            request, NOW + timedelta(minutes=10), before_dispatch=True
        )
    inspected.require_request(request, NOW + timedelta(minutes=10))
    inspected.require_request(
        request, NOW + timedelta(hours=2, microseconds=-1)
    )
    with pytest.raises(ValueError, match="inspection lifetime"):
        inspected.require_request(request, NOW + timedelta(hours=2))


@pytest.mark.parametrize(
    "now", [NOW - timedelta(microseconds=1), NOW.replace(tzinfo=None)]
)
def test_inspection_rejects_preobservation_and_naive_runtime(now):
    """Inspection rejects preobservation and naive runtime."""
    config = configuration()
    with pytest.raises(ValueError, match=r"Ruby|not canonical"):
        inspection(config).require_request(bootstrap_request(config), now)


@pytest.mark.parametrize(
    "observed", [NOW - timedelta(seconds=1), NOW + timedelta(days=7)]
)
def test_inspection_requires_current_configuration_when_observed(observed):
    """Coherent inspection intervals cannot make stale configuration current."""
    config = configuration()
    doc = inspection_document(config)
    doc.update(
        {
            "observed-at": instant(observed),
            "dispatch-by": instant(observed + timedelta(minutes=10)),
            "expires-at": instant(observed + timedelta(hours=2)),
        }
    )
    with pytest.raises(ValueError, match="unavailable or stale"):
        RubyFirstProjectInspection(canonicalize(doc), config)


def _require_modeled_bootstrap_join(envelope, config, slot, now):
    """Exercise actual admission with synthetic target, NBGV and absence."""
    selected = envelope.document["slots"][slot]
    assert selected["configuration-digest"] == config.digest
    expires = min(
        now + timedelta(hours=2),
        datetime.fromisoformat(selected["expires-at"]),
        datetime.fromisoformat(config.document["expires-at"]),
    )
    request_doc = request_document(envelope, slot)
    request_doc.update(
        {"issued-at": instant(now), "expires-at": instant(expires)}
    )
    request = RubyOperationRequest(canonicalize(request_doc), envelope)
    inspected_doc = inspection_document(config)
    inspected_doc.update(
        {
            "schema": "workflow-delivery/v3/ruby-first-project-inspection-v"
            + ("2" if slot.endswith("-bootstrap02") else "1"),
            "slot": slot,
            "generation": selected["generation"],
            "target": request.document["target"],
            "control-baseline-digest": config.document["github-controls"][
                "control-baseline-digest"
            ],
            "observed-at": instant(now),
            "dispatch-by": instant(now + timedelta(minutes=10)),
            "expires-at": instant(expires),
        }
    )
    inspected = RubyFirstProjectInspection(canonicalize(inspected_doc), config)
    inspected.require_request(request, now, before_dispatch=True)


def test_shipped_github_normal_carrier_joins_current_protocol_and_governance():
    """Retained-time joins test shipped policy, not live operation readiness."""
    envelope = RubyOperationEnvelope(
        (
            ROOT
            / ".github/workflow-delivery/requests"
            / "hcoona-release-smoke-ruby.json"
        ).read_bytes()
    )
    selected = envelope.document["slots"]["github-packages-normal01"]
    protocol = ruby_digest(
        (
            ROOT
            / "src/public/lib/three-workflow-delivery-v3/docs"
            / "ruby-operation-protocol.md"
        ).read_bytes()
    )
    assert selected["protocol-digest"] == protocol
    content = (ROOT / selected["binding"]["configuration-path"]).read_bytes()
    document = parse_canonical_json(content)
    inspected = datetime.fromisoformat(document["inspected-at"])
    # TARGET is modeled provenance; actual protected delivery is audited apart.
    governance = RubyGovernance(
        RubyRegistry("github-packages"), content, TARGET, inspected
    )
    assert selected["configuration-digest"] == governance.digest
    start = datetime.fromisoformat(selected["not-before"])
    expires = datetime.fromisoformat(selected["expires-at"])
    governance_expires = datetime.fromisoformat(document["expires-at"])
    now = max(start, inspected)
    assert start <= now < expires <= governance_expires
    assert inspected <= now < governance_expires
    assert expires <= start + timedelta(days=7)
    assert governance_expires <= inspected + timedelta(days=7)
    assert governance_expires <= datetime.fromisoformat(
        "2026-10-08T03:39:53.109710Z"
    )
    assert envelope.enabled_slot("github-packages-normal01", now) == selected
    governance.require_live(now)


def test_successor_inspection_rejects_coherent_other_protocol():
    """Rejoined envelope/request hashes cannot replace configuration policy."""
    config = configuration("github-packages")
    slot = "github-packages-bootstrap02"
    document = disabled_ruby_operation_envelope(version=2)
    selected = envelope_document(slot)["slots"][slot]
    selected.update(
        {"configuration-digest": config.digest, "protocol-digest": OTHER_DIGEST}
    )
    document["slots"][slot] = selected
    envelope = RubyOperationEnvelope(canonicalize(document))
    with pytest.raises(ValueError, match="first-project binding differs"):
        _require_modeled_bootstrap_join(envelope, config, slot, NOW)


@pytest.mark.parametrize("destination", ["github-packages", "rubygems"])
def test_shipped_normal_activation_preserves_destination_and_bootstrap_gates(
    destination,
):
    """Only GitHub normal is ready; all bootstrap carriers remain blocked."""
    registry = RubyRegistry(destination)
    control = ROOT / ".github/workflow-delivery"
    governance_bytes = (
        control / "governance" / f"hcoona-release-smoke-ruby-{destination}.json"
    ).read_bytes()
    document = parse_canonical_json(governance_bytes)
    observed = (
        datetime.fromisoformat(document["inspected-at"])
        if destination == "github-packages"
        else NOW
    )
    governance = RubyGovernance(registry, governance_bytes, TARGET, observed)
    if destination == "github-packages":
        assert governance.document["live_enabled"] is True
        assert governance.document["state"] == "ready"
        governance.require_live(observed)
    else:
        assert governance.document == blocked_ruby_governance(registry)
        with pytest.raises(ValueError):  # noqa: PT011 - blocked ready state
            governance.require_live(observed)
    assert governance_bytes == canonicalize(governance.document)
    assert (
        governance.document["operation-profile-digest"]
        == registry.profile_digest
    )
    configuration_bytes = (
        control
        / "configuration"
        / f"hcoona-release-smoke-ruby-{destination}.json"
    ).read_bytes()
    config = RubyBootstrapConfiguration(registry, configuration_bytes)
    assert config.document == blocked_ruby_bootstrap_configuration(registry)
    assert configuration_bytes == canonicalize(config.document)
    with pytest.raises(ValueError, match="unavailable or stale"):
        config.require_current(NOW)
