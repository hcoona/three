"""Independent Ruby Governance source and protection admission scenarios."""

from dataclasses import replace
from datetime import datetime, timedelta
from pathlib import Path

import pytest
from three_workflow_delivery_v3.adapters.rubygems import RubyRegistry
from three_workflow_delivery_v3.canonical import (
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.release.ruby_governance import (
    RubyGovernance,
    blocked_ruby_governance,
    ruby_governance_path,
)

from ..ruby_integration_fixtures import NOW, TARGET, governance, ready_document

ROOT = Path(__file__).resolve().parents[6]


@pytest.mark.parametrize("name", ["github-packages", "rubygems"])
def test_ruby_checked_in_governance_preserves_destination_activation(name):
    """Both destinations retain independently attested normal configuration."""
    registry = RubyRegistry(name)
    content = (ROOT / ruby_governance_path(registry)).read_bytes()
    document = parse_canonical_json(content)
    observed = datetime.fromisoformat(document["inspected-at"])
    admitted = RubyGovernance(registry, content, TARGET, observed)
    assert content == canonicalize(admitted.document)
    assert admitted.document["live_enabled"] is True
    assert admitted.document["state"] == "ready"
    admitted.require_live(observed)
    assert admitted.document["publisher"]["environment"] == registry.environment
    other = RubyRegistry(
        "rubygems" if name == "github-packages" else "github-packages"
    )
    with pytest.raises(ValueError, match="authority or profile"):
        RubyGovernance(other, content, TARGET, observed)


@pytest.mark.parametrize("name", ["github-packages", "rubygems"])
@pytest.mark.parametrize("bypass", [False, True])
def test_ruby_ready_governance_keeps_actual_boolean_and_ownership(name, bypass):
    """Retain actual bypass and destination-specific ownership."""
    registry = RubyRegistry(name)
    doc = ready_document(registry)
    doc["configuration"]["can-admins-bypass"] = bypass
    admitted = RubyGovernance(registry, canonicalize(doc), TARGET, NOW)
    admitted.require_live(NOW)
    assert admitted.document["configuration"]["can-admins-bypass"] is bypass
    assert admitted.document["configuration"]["package-ownership"][
        "account"
    ] == ("hcoona" if name == "github-packages" else "Raffle2282")
    assert admitted.document["publisher"]["authentication"] == (
        "github-job-token" if name == "github-packages" else "ruby-oidc"
    )
    assert admitted.document["publisher"]["audience"] == (
        None if name == "github-packages" else "rubygems.org"
    )
    assert (
        admitted.document["configuration"]["publisher-registration"]
        == doc["publisher"]
    )
    admitted.require_same_live(
        replace(
            admitted,
            source_commit="b" * 40,
            observed_at=NOW + timedelta(seconds=1),
        ),
        NOW + timedelta(seconds=1),
    )


@pytest.mark.parametrize("value", [None, 0, 1, 0.0, "false", "true"])
def test_ruby_governance_rejects_coerced_admin_bypass(value):
    """A Boolean-looking primitive cannot replace actual service state."""
    registry = RubyRegistry("rubygems")
    doc = ready_document(registry)
    doc["configuration"]["can-admins-bypass"] = value
    with pytest.raises(ValueError, match="actual Boolean"):
        RubyGovernance(registry, canonicalize(doc), TARGET, NOW)


@pytest.mark.parametrize(
    "change",
    [
        "schema",
        "profile",
        "publisher",
        "hybrid",
        "integer-enabled",
        "extra",
        "missing",
        "unpinned-source",
    ],
)
def test_ruby_governance_rejects_hybrid_or_foreign_authority(change):
    """Ready source is a closed explicit admission, never a registry alias."""
    registry = RubyRegistry("github-packages")
    doc = ready_document(registry)
    if change == "schema":
        doc["schema"] = "workflow-delivery/v3/python-governance-v2"
    elif change == "profile":
        doc["operation-profile-digest"] = RubyRegistry(
            "rubygems"
        ).profile_digest
    elif change == "publisher":
        doc["publisher"]["workflow"] = "workflow-delivery-v3-ruby-bootstrap.yml"
    elif change == "hybrid":
        doc["live_enabled"] = False
    elif change == "integer-enabled":
        doc["live_enabled"] = 1
    elif change == "extra":
        doc["native-acceptance"] = {"passed": True}
    elif change == "missing":
        doc.pop("configuration")
    else:
        doc["source-evidence-revision"] = "unbound"
    with pytest.raises((ValueError, TypeError), match="Ruby"):
        RubyGovernance(registry, canonicalize(doc), TARGET, NOW)


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("environment-id", True),
        ("reviewer-id", True),
        ("prevent-self-review", True),
        ("protected-main-only", False),
        ("secret-count", 1),
        ("wait-timer", 1),
        ("accepted-writers", ["hcoona", "foreign"]),
        ("sentinel", "foreign/v1"),
        ("attestation-digest", "unbound"),
        ("publisher-registration", {}),
        ("package-ownership", {"confirmed": True}),
        ("scope-limitation", "unbounded guarantee"),
    ],
)
def test_ruby_governance_requires_exact_protected_configuration(field, value):
    """Require exact package and Environment attestations."""
    registry = RubyRegistry("rubygems")
    doc = ready_document(registry)
    doc["configuration"][field] = value
    with pytest.raises((ValueError, TypeError), match="Ruby"):
        RubyGovernance(registry, canonicalize(doc), TARGET, NOW)


@pytest.mark.parametrize(
    ("name", "field", "value"),
    [
        ("github-packages", "visibility", "internal"),
        ("rubygems", "visibility", "private"),
        ("rubygems", "repository", "hcoona/foreign"),
        ("github-packages", "read", False),
        ("github-packages", "publish", 1),
        ("rubygems", "authentication", "github-job-token"),
    ],
)
def test_ruby_governance_rejects_unconfirmed_package_access(name, field, value):
    """Effective read/publish access must match the independent registry."""
    registry = RubyRegistry(name)
    doc = ready_document(registry)
    doc["configuration"]["package-access"][field] = value
    with pytest.raises(ValueError, match="package access"):
        RubyGovernance(registry, canonicalize(doc), TARGET, NOW)


@pytest.mark.parametrize("name", ["github-packages", "rubygems"])
def test_ruby_governance_accepts_public_package_access(name):
    """Bind public visibility to exact access and authentication."""
    registry = RubyRegistry(name)
    doc = ready_document(registry)
    doc["configuration"]["package-access"]["visibility"] = "public"
    accepted = RubyGovernance(registry, canonicalize(doc), TARGET, NOW)
    accepted.require_live(NOW)
    assert (
        accepted.document["configuration"]["package-access"]["visibility"]
        == "public"
    )


@pytest.mark.parametrize(
    "instant",
    [
        "inspection",
        "before-expiry",
        "expiry",
        "before-inspection",
        "before-observation",
    ],
)
def test_ruby_governance_freshness_boundaries(instant):
    """Inspection is inclusive and expiry/future observation fail closed."""
    inspected = NOW - timedelta(hours=1)
    expires = NOW + timedelta(days=1)
    admitted = governance(
        observed_at=inspected if instant == "inspection" else NOW
    )
    now = {
        "inspection": inspected,
        "before-expiry": expires - timedelta(microseconds=1),
        "expiry": expires,
        "before-inspection": inspected - timedelta(microseconds=1),
        "before-observation": NOW - timedelta(microseconds=1),
    }[instant]
    if instant in {"inspection", "before-expiry"}:
        admitted.require_live(now)
    else:
        with pytest.raises(ValueError, match="stale or not current"):
            admitted.require_live(now)


@pytest.mark.parametrize(
    "duration",
    [timedelta(), timedelta(days=-1), timedelta(days=90, microseconds=1)],
)
def test_ruby_governance_rejects_invalid_admission_interval(duration):
    """A ready record cannot waive the positive ninety-day ceiling."""
    current = governance()
    doc = current.document
    doc["expires-at"] = (
        (NOW - timedelta(hours=1) + duration).isoformat().replace("+00:00", "Z")
    )
    with pytest.raises(ValueError, match="freshness window"):
        replace(current, content=canonicalize(doc))


@pytest.mark.parametrize(
    "change", ["destination", "content", "older-observation", "disabled"]
)
def test_ruby_governance_rejects_within_attempt_drift(change):
    """New observation never transfers or revives changed live authority."""
    previous = governance()
    fresh = governance(observed_at=NOW + timedelta(seconds=1))
    if change == "destination":
        fresh = governance("rubygems", NOW + timedelta(seconds=1))
    elif change == "content":
        doc = fresh.document
        doc["source-evidence-revision"] = "b" * 40
        fresh = replace(fresh, content=canonicalize(doc))
    elif change == "older-observation":
        fresh = replace(fresh, observed_at=NOW - timedelta(seconds=1))
    else:
        fresh = replace(
            fresh, content=canonicalize(blocked_ruby_governance(fresh.registry))
        )
    with pytest.raises(ValueError, match="Ruby"):
        previous.require_same_live(fresh, NOW + timedelta(seconds=2))


@pytest.mark.parametrize(
    "change",
    ["source-commit", "naive-observation", "naive-now", "non-utc-source"],
)
def test_ruby_governance_requires_protected_source_and_aware_time(change):
    """Source provenance and explicit UTC evidence remain mandatory."""
    current = governance()
    if change == "naive-now":
        with pytest.raises(ValueError, match="stale or not current"):
            current.require_live(NOW.replace(tzinfo=None))
        return
    if change == "source-commit":
        updates = {"source_commit": "g" * 40}
    elif change == "naive-observation":
        updates = {"observed_at": NOW.replace(tzinfo=None)}
    else:
        doc = current.document
        doc["inspected-at"] = "2026-09-30T11:00:00+01:00"
        updates = {"content": canonicalize(doc)}
    with pytest.raises(ValueError, match=r"provenance|timestamps must be UTC"):
        replace(current, **updates)


def test_ruby_governance_admits_exact_ninety_day_window():
    """The ninety-day upper bound is inclusive without extending expiry."""
    current = governance()
    doc = current.document
    expires = NOW - timedelta(hours=1) + timedelta(days=90)
    doc["expires-at"] = expires.isoformat().replace("+00:00", "Z")
    current = replace(current, content=canonicalize(doc))
    current.require_live(expires - timedelta(microseconds=1))
    with pytest.raises(ValueError, match="stale or not current"):
        current.require_live(expires)
