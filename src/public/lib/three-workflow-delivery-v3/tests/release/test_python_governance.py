"""Disabled destinations and strictly modeled future Governance admission."""

from dataclasses import replace
from datetime import timedelta
from pathlib import Path

import pytest
from three_workflow_delivery_v3.adapters.pypi import PythonRegistry
from three_workflow_delivery_v3.canonical import (
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.release.python_governance import (
    PythonGovernance,
    blocked_python_governance,
    python_governance_path,
)

from ..python_fixtures import NOW, TARGET, governance, ready_document

_ROOT = Path(__file__).resolve().parents[6]


@pytest.mark.parametrize("name", ["testpypi", "pypi"])
def test_python_tracked_destinations_remain_explicitly_disabled(name):
    """Both tracked profiles fail before any registry access."""
    registry = PythonRegistry(name)
    doc = parse_canonical_json(
        (_ROOT / python_governance_path(registry)).read_bytes()
    )
    assert doc == blocked_python_governance(registry)
    assert doc["schema"] == "workflow-delivery/v3/python-governance-v2"
    assert "native-acceptance" not in doc
    assert doc["live_enabled"] is False
    assert doc["publisher"]["audience"] == name
    blocked = PythonGovernance(registry, canonicalize(doc), TARGET, NOW)
    with pytest.raises(ValueError, match="remains disabled"):
        blocked.require_live(NOW)


@pytest.mark.parametrize(
    "change",
    [
        "hybrid",
        "enabled-integer",
        "profile",
        "publisher",
        "v1-schema",
        "missing-field",
        "secret",
        "reviewer",
        "environment-id",
        "extra",
    ],
)
def test_python_governance_rejects_hybrid_foreign_or_coerced_authority(change):
    """Only explicit evidenced protections can enter modeled ready state."""
    registry = PythonRegistry("testpypi")
    doc = ready_document(registry)
    if change == "hybrid":
        doc["live_enabled"] = False
    elif change == "enabled-integer":
        doc["live_enabled"] = 1
    elif change == "profile":
        doc["operation-profile-digest"] = PythonRegistry("pypi").profile_digest
    elif change == "publisher":
        doc["publisher"]["audience"] = "pypi"
    elif change == "v1-schema":
        doc["schema"] = "workflow-delivery/v3/python-governance-v1"
    elif change == "missing-field":
        doc.pop("configuration")
    elif change == "secret":
        doc["configuration"]["secret-count"] = 1
    elif change == "reviewer":
        doc["configuration"]["reviewer-id"] = True
    elif change == "environment-id":
        doc["configuration"]["environment-id"] = True
    else:
        doc["external-grant"] = True
    with pytest.raises(ValueError, match="Python"):
        PythonGovernance(registry, canonicalize(doc), TARGET, NOW)


@pytest.mark.parametrize("offset", [timedelta(hours=-2), timedelta(days=1)])
def test_python_governance_rejects_future_or_expired_evidence(offset):
    """Admission is valid at inspection and strictly before expiry."""
    admitted = governance()
    with pytest.raises(ValueError, match="stale or not current"):
        admitted.require_live(NOW + offset)
    admitted.require_live(NOW)


@pytest.mark.parametrize(
    "change", ["destination", "content", "older-observation", "disabled"]
)
def test_python_governance_rejects_mid_attempt_drift(change):
    """Freshness cannot transfer admission between profiles or revisions."""
    previous = governance()
    fresh = governance(observed_at=NOW + timedelta(seconds=1))
    if change == "destination":
        fresh = governance("pypi", NOW + timedelta(seconds=1))
    elif change == "content":
        doc = fresh.document
        doc["source-evidence-revision"] = "f" * 40
        fresh = replace(fresh, content=canonicalize(doc))
    elif change == "older-observation":
        fresh = replace(fresh, observed_at=NOW - timedelta(seconds=1))
    else:
        fresh = replace(
            fresh,
            content=canonicalize(blocked_python_governance(fresh.registry)),
        )
    with pytest.raises(ValueError, match="Python"):
        previous.require_same_live(fresh, NOW + timedelta(seconds=2))


@pytest.mark.parametrize("name", ["testpypi", "pypi"])
def test_python_ready_v2_requires_configuration_without_native_proof(name):
    """Modeled admission retains independent source and destination binding."""
    admitted = governance(name)
    document = admitted.document
    assert set(document) == {
        "schema",
        "publisher",
        "accepted-operator",
        "live_enabled",
        "state",
        "operation-profile-digest",
        "inspected-at",
        "expires-at",
        "configuration",
        "source-evidence-revision",
    }
    assert document["schema"] == "workflow-delivery/v3/python-governance-v2"
    assert document["source-evidence-revision"] == TARGET
    assert (
        document["configuration"]["publisher-registration"]
        == document["publisher"]
    )
    assert document["publisher"]["audience"] == name
    admitted.require_live(NOW)
    admitted.require_same_live(
        governance(name, NOW + timedelta(seconds=1)), NOW + timedelta(seconds=1)
    )


@pytest.mark.parametrize("state", ["blocked", "ready"])
@pytest.mark.parametrize("native", [None, {"passed": True}])
def test_python_governance_v2_rejects_native_hybrids(state, native):
    """Neither a null slot nor invented passing native proof belongs to v2."""
    registry = PythonRegistry("testpypi")
    document = (
        ready_document(registry)
        if state == "ready"
        else blocked_python_governance(registry)
    )
    document["native-acceptance"] = native
    with pytest.raises(ValueError, match="Python"):
        PythonGovernance(registry, canonicalize(document), TARGET, NOW)


@pytest.mark.parametrize("source_commit", ["", "f" * 39, "g" * 40])
def test_python_governance_requires_protected_source_commit(source_commit):
    """A valid ready document cannot supply its own current-main provenance."""
    admitted = governance()
    with pytest.raises(ValueError, match="protected source provenance"):
        replace(admitted, source_commit=source_commit)


@pytest.mark.parametrize("revision", [None, "", "g" * 40, "f" * 39])
def test_python_governance_requires_pinned_source_evidence(revision):
    """Configuration attestation cannot replace the accepted dependency pin."""
    admitted = governance()
    document = admitted.document
    document["source-evidence-revision"] = revision
    with pytest.raises(ValueError, match="Python"):
        replace(admitted, content=canonicalize(document))


@pytest.mark.parametrize(
    "change",
    ["missing", "foreign-publisher", "extra-writer", "bypass", "attestation"],
)
def test_python_ready_v2_still_requires_exact_configuration(change):
    """Platform reliance grants no exemption from configuration protections."""
    admitted = governance()
    document = admitted.document
    configuration = document["configuration"]
    if change == "missing":
        document["configuration"] = None
    elif change == "foreign-publisher":
        configuration["publisher-registration"]["workflow"] = (
            "workflow-delivery-v3-native-python-acceptance.yml"
        )
    elif change == "extra-writer":
        configuration["accepted-writers"].append("foreign")
    elif change == "bypass":
        configuration["can-admins-bypass"] = True
    else:
        configuration["attestation-digest"] = "unbound"
    with pytest.raises(ValueError, match="Python"):
        replace(admitted, content=canonicalize(document))


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
def test_python_governance_freshness_boundaries_remain_strict(instant):
    """Inspection is inclusive; expiry and future observation fail closed."""
    inspected = NOW - timedelta(hours=1)
    expires = NOW + timedelta(days=1)
    observed = inspected if instant == "inspection" else NOW
    admitted = governance(observed_at=observed)
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
    [
        timedelta(),
        timedelta(days=-1),
        timedelta(days=90, microseconds=1),
    ],
)
def test_python_governance_rejects_invalid_freshness_interval(duration):
    """The unchanged positive ninety-day ceiling constrains ready admission."""
    admitted = governance()
    document = admitted.document
    document["expires-at"] = (
        (NOW - timedelta(hours=1) + duration).isoformat().replace("+00:00", "Z")
    )
    with pytest.raises(ValueError, match="freshness window"):
        replace(admitted, content=canonicalize(document))


def test_python_governance_accepts_exact_ninety_day_interval():
    """The published maximum is inclusive, without extending expiry."""
    admitted = governance()
    document = admitted.document
    expires = NOW - timedelta(hours=1) + timedelta(days=90)
    document["expires-at"] = expires.isoformat().replace("+00:00", "Z")
    admitted = replace(admitted, content=canonicalize(document))
    admitted.require_live(expires - timedelta(microseconds=1))
    with pytest.raises(ValueError, match="stale or not current"):
        admitted.require_live(expires)
