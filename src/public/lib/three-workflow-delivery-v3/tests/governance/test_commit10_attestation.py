"""Commit-10 contracts for the actual protected disabled attestation."""

from __future__ import annotations

# ruff: noqa: D103, E501
from datetime import timedelta
from pathlib import Path
from types import SimpleNamespace
from typing import TYPE_CHECKING

from three_workflow_delivery_v3.canonical import (
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.release import eligibility
from three_workflow_delivery_v3.release.eligibility import (
    EligibilityResult,
    GovernanceBlob,
    LiveEligibilityContext,
    parse_governance_attestation,
)
from three_workflow_delivery_v3.release.static_reference_model import (
    STATIC_REFERENCE_POLICY_ID,
    BoundedStaticReferenceResult,
)
from three_workflow_delivery_v3.release.static_reference_policy import (
    STATIC_REFERENCE_POLICY_DIGEST,
)
from three_workflow_delivery_v3.repository.descriptors import (
    FIRST_SLICE_PACKAGE,
    GOVERNANCE_MAX_AGE_DAYS,
    GOVERNANCE_PATH,
    GOVERNANCE_REF,
    GOVERNANCE_REPOSITORY,
)

if TYPE_CHECKING:
    import pytest

REPO_ROOT = Path(__file__).resolve().parents[6]
ACTUAL_ATTESTATION = REPO_ROOT / GOVERNANCE_PATH
NORMAL_BUDDY = (
    REPO_ROOT / ".github/workflows/workflow-delivery-v3-buddy-smoke.yml"
)
LIVE_STATIC_REFERENCE_IMPLEMENTATIONS = (
    "@npmcli/package-json@8.0.0",
    "@pnpm/deps.path@1101.0.1",
    "@pnpm/lockfile.fs@1100.2.5",
    "@pnpm/lockfile.utils@1102.1.0",
    "@pnpm/resolving.npm-resolver@1104.1.0",
    "@pnpm/workspace.spec-parser@1100.0.1",
    "@pnpm/workspace.workspace-manifest-reader@1100.1.8",
    "NuGet.Packaging@7.9.0",
    "NuGet.ProjectModel@7.9.0",
    "dotnet-runtime@10.0.8",
    "node@24.19.0",
    "npm-package-arg@14.0.0",
)


def _content() -> bytes:
    assert ACTUAL_ATTESTATION.is_file(), (
        "commit-10 protected disabled attestation is missing"
    )
    return ACTUAL_ATTESTATION.read_bytes()


class _RecordingGovernanceClient:
    def __init__(self, content: bytes) -> None:
        self.content = content
        self.calls: list[tuple[str, ...]] = []

    def is_ref_protected(self, repository: str, ref: str) -> bool:
        self.calls.append(("protected", repository, ref))
        return True

    def resolve_ref(self, repository: str, ref: str) -> str:
        self.calls.append(("resolve", repository, ref))
        return "a" * 40

    def read_blob(
        self,
        repository: str,
        commit: str,
        path: str,
    ) -> GovernanceBlob:
        self.calls.append(("read", repository, commit, path))
        return GovernanceBlob(blob_oid="b" * 40, content=self.content)


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


def test_disabled_attestation_decision_cannot_cross_the_pre_attempt_gate(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    content = _content()
    attestation = parse_governance_attestation(content)
    client = _RecordingGovernanceClient(content)
    source = SimpleNamespace(
        repository=GOVERNANCE_REPOSITORY,
        ref=GOVERNANCE_REF,
        path=GOVERNANCE_PATH,
        max_age_days=GOVERNANCE_MAX_AGE_DAYS,
    )
    policy = SimpleNamespace(governance=source)
    static_reference = BoundedStaticReferenceResult(
        source_kind="git-target",
        target="e" * 40,
        policy_id=STATIC_REFERENCE_POLICY_ID,
        policy_digest=STATIC_REFERENCE_POLICY_DIGEST,
        implementation_identities=LIVE_STATIC_REFERENCE_IMPLEMENTATIONS,
        findings=(),
    )
    context = LiveEligibilityContext(
        purpose="live-release",
        request_id="request-42",
        workflow_run_id=8101,
        run_attempt=3,
        selected_ref="refs/heads/main",
        target="e" * 40,
        repository_model_digest="sha256:" + ("1" * 64),
        producer="evaluate-live-eligibility",
        control="trusted",
        release_policy_digest="sha256:" + ("2" * 64),
        catalog_digest="sha256:" + ("3" * 64),
    )
    monkeypatch.setattr(eligibility, "_validate_source", lambda _source: None)
    monkeypatch.setattr(
        eligibility,
        "_validate_live_context",
        lambda _context, _snapshot, _policy: None,
    )
    monkeypatch.setattr(
        eligibility,
        "scan_bounded_static_references",
        lambda *_args, **_kwargs: static_reference,
    )

    decision = eligibility.evaluate_live_eligibility(
        context,
        object(),
        policy,
        client,
        repository_root=REPO_ROOT,
        now=attestation.inspected_at + timedelta(seconds=1),
    )
    caller = NORMAL_BUDDY.read_text(encoding="utf-8")

    assert decision.result is EligibilityResult.BLOCKED
    assert decision.diagnostics == ("governance-live-disabled",)
    assert decision.static_reference is static_reference
    assert tuple(call[0] for call in client.calls) == (
        "protected",
        "resolve",
        "read",
    )
    assert (
        "needs.evaluate-live-eligibility.outputs.live-result == 'admitted'"
        in caller
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
