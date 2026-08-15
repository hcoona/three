"""Commit-10 contracts for the actual protected disabled attestation."""

from __future__ import annotations

# ruff: noqa: D103, E501, TC003
import importlib.util
import sys
from datetime import timedelta
from pathlib import Path
from types import ModuleType

from three_workflow_delivery_v3.canonical import (
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.release.eligibility import (
    EligibilityResult,
    parse_governance_attestation,
)
from three_workflow_delivery_v3.repository.descriptors import (
    FIRST_SLICE_PACKAGE,
    GOVERNANCE_MAX_AGE_DAYS,
    GOVERNANCE_PATH,
    GOVERNANCE_REF,
    GOVERNANCE_REPOSITORY,
)

REPO_ROOT = Path(__file__).resolve().parents[6]
ACTUAL_ATTESTATION = REPO_ROOT / GOVERNANCE_PATH
NORMAL_BUDDY = (
    REPO_ROOT / ".github/workflows/workflow-delivery-v3-buddy-smoke.yml"
)


def _content() -> bytes:
    assert ACTUAL_ATTESTATION.is_file(), (
        "commit-10 protected disabled attestation is missing"
    )
    return ACTUAL_ATTESTATION.read_bytes()


def _eligibility_test_contract() -> ModuleType:
    path = Path(__file__).parents[1] / "release/test_eligibility.py"
    name = "_commit10_reused_eligibility_contract"
    spec = importlib.util.spec_from_file_location(name, path)
    assert spec is not None
    assert spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


def test_actual_protected_attestation_is_canonical_disabled_and_exactly_bound() -> (
    None
):
    content = _content()
    document = parse_canonical_json(content)
    attestation = parse_governance_attestation(content)
    lifetime = attestation.expires_at - attestation.inspected_at

    assert canonicalize(document) == content
    assert attestation.to_document() == document
    assert attestation.release_policy == "hcoona-release-smoke-npm"
    assert attestation.package == FIRST_SLICE_PACKAGE
    assert attestation.live_enabled is False
    assert timedelta(0) < lifetime <= timedelta(days=GOVERNANCE_MAX_AGE_DAYS)
    assert (GOVERNANCE_REPOSITORY, GOVERNANCE_REF, GOVERNANCE_PATH) == (
        "hcoona/three",
        "refs/heads/main",
        (".github/workflow-delivery/governance/hcoona-release-smoke-npm.json"),
    )


def test_actual_attestation_accepts_only_hcoona_admin_and_exact_access() -> (
    None
):
    attestation = parse_governance_attestation(_content())
    inventory = attestation.access_inventory

    assert attestation.issuer == "hcoona"
    assert tuple(
        (writer.login, writer.role) for writer in attestation.accepted_writers
    ) == (("hcoona", "Admin"),)
    assert inventory is not None
    assert tuple(
        (grant.subject, grant.access) for grant in inventory.repository
    ) == (("hcoona", "admin"),)
    assert tuple(
        (grant.subject, grant.access) for grant in inventory.package
    ) == (("hcoona", "write"),)
    assert tuple(
        (grant.subject, grant.access) for grant in inventory.manage_actions
    ) == (("hcoona", "allowed"),)
    assert {
        grant.subject
        for grants in (
            inventory.repository,
            inventory.package,
            inventory.manage_actions,
        )
        for grant in grants
    } == {"hcoona"}


def test_disabled_attestation_decision_cannot_cross_the_pre_attempt_gate() -> (
    None
):
    contract = _eligibility_test_contract()
    content = _content()
    attestation = parse_governance_attestation(content)
    client = contract.RecordingGovernanceClient(content)
    decision = contract._evaluate(  # noqa: SLF001
        client,
        now=attestation.inspected_at + timedelta(seconds=1),
    )
    caller = NORMAL_BUDDY.read_text(encoding="utf-8")

    assert decision.result is EligibilityResult.BLOCKED
    assert decision.diagnostics == ("governance-live-disabled",)
    assert tuple(call[0] for call in client.calls) == (
        "protected",
        "resolve",
        "read",
    )
    assert (
        "needs.evaluate-live-eligibility.outputs.live-result == 'admitted'"
        in (caller)
    )
    assert decision.result.value == "blocked"
    assert decision.result.value != "admitted"


def test_commit10_attestation_is_governance_only_and_never_acceptance_activation() -> (
    None
):
    document = parse_canonical_json(_content())
    serialized = _content().decode("utf-8")

    assert document["live_enabled"] is False
    assert "workflow-delivery-v3-buddy-smoke-acceptance" not in serialized
    assert "packages: write" not in serialized
    assert "mutation-classification" not in serialized


def test_commit10_attestation_binds_disabled_normal_live_without_acceptance_evidence() -> (
    None
):
    document = parse_canonical_json(_content())

    assert document["live_enabled"] is False
    assert document["release_policy"] == "hcoona-release-smoke-npm"
    assert "governance-acceptance-evidence" not in document
