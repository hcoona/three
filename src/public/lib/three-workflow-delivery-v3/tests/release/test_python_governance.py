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
        "passed-integer",
        "registry",
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
    elif change == "passed-integer":
        doc["native-acceptance"]["passed"] = 1
    elif change == "registry":
        doc["native-acceptance"]["registry"] = "pypi"
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
    """Freshness cannot transfer admission between profiles or generations."""
    previous = governance()
    fresh = governance(observed_at=NOW + timedelta(seconds=1))
    if change == "destination":
        fresh = governance("pypi", NOW + timedelta(seconds=1))
    elif change == "content":
        doc = fresh.document
        doc["native-acceptance"]["generation"] = "f" * 32
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
