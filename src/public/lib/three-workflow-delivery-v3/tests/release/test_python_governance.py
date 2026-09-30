"""Independent tracked admissions and fail-closed Governance scenarios."""

from dataclasses import replace
from datetime import datetime, timedelta
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
def test_python_blocked_destinations_reject_live(name):
    """Either destination can be disabled independently of tracked admission."""
    registry = PythonRegistry(name)
    doc = blocked_python_governance(registry)
    assert doc["schema"] == "workflow-delivery/v3/python-governance-v2"
    assert "native-acceptance" not in doc
    assert doc["live_enabled"] is False
    assert doc["publisher"]["audience"] == name
    blocked = PythonGovernance(registry, canonicalize(doc), TARGET, NOW)
    with pytest.raises(ValueError, match="remains disabled"):
        blocked.require_live(NOW)


@pytest.mark.parametrize(
    ("name", "environment_id", "can_admins_bypass"),
    [("testpypi", 22765954016, False), ("pypi", 23047309006, True)],
)
def test_python_tracked_admission_has_freshness_and_isolation(
    name, environment_id, can_admins_bypass
):
    """Reviewed normal-workflow admissions have isolated, finite validity."""
    registry = PythonRegistry(name)
    content = (_ROOT / python_governance_path(registry)).read_bytes()
    doc = parse_canonical_json(content)
    assert doc["schema"] == "workflow-delivery/v3/python-governance-v2"
    assert doc["state"] == "ready"
    assert doc["live_enabled"] is True
    assert "native-acceptance" not in doc
    assert doc["publisher"] == {
        "repository": "hcoona/three",
        "owner-id": 712433,
        "project": "hcoona-release-smoke-python",
        "registry": registry.origin,
        "audience": name,
        "workflow": "workflow-delivery-v3-python-smoke.yml",
        "environment": registry.environment,
        "selected-ref": "refs/heads/main",
    }
    assert doc["configuration"]["publisher-registration"] == doc["publisher"]
    assert doc["configuration"]["environment-id"] == environment_id
    assert doc["configuration"]["can-admins-bypass"] is can_admins_bypass
    inspected = datetime.fromisoformat(doc["inspected-at"])
    expires = datetime.fromisoformat(doc["expires-at"])
    # The synthetic source commit exercises parsing, not current-main proof.
    admitted = PythonGovernance(registry, content, TARGET, inspected)
    admitted.require_live(inspected)
    admitted.require_live(expires - timedelta(microseconds=1))
    before_inspection = inspected - timedelta(microseconds=1)
    with pytest.raises(ValueError, match="stale or not current"):
        replace(admitted, observed_at=before_inspection).require_live(
            before_inspection
        )
    with pytest.raises(ValueError, match="stale or not current"):
        admitted.require_live(expires)
    other = PythonRegistry("pypi" if name == "testpypi" else "testpypi")
    with pytest.raises(ValueError, match="authority or profile mismatch"):
        PythonGovernance(other, content, TARGET, inspected)


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
    ("name", "bypass", "accepted"),
    [
        ("pypi", False, True),
        ("pypi", True, True),
        ("testpypi", False, True),
        ("testpypi", True, False),
    ],
)
def test_python_governance_preserves_destination_bypass_policy(
    name, bypass, accepted
):
    """Production accepts either observed Boolean; TestPyPI stays false-only."""
    registry = PythonRegistry(name)
    document = ready_document(registry)
    document["configuration"]["can-admins-bypass"] = bypass
    content = canonicalize(document)
    if not accepted:
        with pytest.raises(ValueError, match="Python"):
            PythonGovernance(registry, content, TARGET, NOW)
        return
    admitted = PythonGovernance(registry, content, TARGET, NOW)
    admitted.require_live(NOW)
    assert admitted.content == content
    assert admitted.document["configuration"]["can-admins-bypass"] is bypass
    assert admitted.document["configuration"]["accepted-writers"] == ["hcoona"]


@pytest.mark.parametrize("name", ["testpypi", "pypi"])
@pytest.mark.parametrize("bypass", [None, 0, 1, 0.0, 1.0, "false", "true"])
def test_python_governance_rejects_non_boolean_bypass(name, bypass):
    """Boolean-looking JSON values cannot coerce the observed configuration."""
    registry = PythonRegistry(name)
    document = ready_document(registry)
    document["configuration"]["can-admins-bypass"] = bypass
    with pytest.raises(ValueError, match="Python"):
        PythonGovernance(registry, canonicalize(document), TARGET, NOW)


@pytest.mark.parametrize("name", ["testpypi", "pypi"])
@pytest.mark.parametrize(
    "change",
    [
        "missing",
        "missing-bypass",
        "foreign-publisher",
        "extra-writer",
        "attestation",
    ],
)
def test_python_ready_v2_still_requires_exact_configuration(name, change):
    """Platform reliance grants no exemption from configuration protections."""
    admitted = governance(name)
    document = admitted.document
    configuration = document["configuration"]
    if name == "pypi":
        configuration["can-admins-bypass"] = True
    if change == "missing":
        document["configuration"] = None
    elif change == "missing-bypass":
        configuration.pop("can-admins-bypass")
    elif change == "foreign-publisher":
        configuration["publisher-registration"]["workflow"] = (
            "workflow-delivery-v3-native-python-acceptance.yml"
        )
    elif change == "extra-writer":
        configuration["accepted-writers"].append("foreign")
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
