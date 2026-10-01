"""Bound modeled absence evidence to a real validated operation request."""

from datetime import timedelta

import pytest
from three_workflow_delivery_v3.adapters.rubygems import RubyRegistry
from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.release.ruby_configuration import (
    RubyBootstrapConfiguration,
    RubyFirstProjectInspection,
)

from ..ruby_integration_fixtures import NOW
from .ruby_configuration_fixtures import (
    bootstrap_request,
    configuration,
    configuration_document,
    inspection,
    inspection_document,
)
from .ruby_operation_fixtures import instant

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
