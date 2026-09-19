"""Current Live Eligibility ownership of bounded static-reference evidence."""

from __future__ import annotations

from datetime import UTC, datetime, timedelta
from pathlib import Path
from types import SimpleNamespace
from typing import cast

import pytest
from three_workflow_delivery_v3.release import eligibility
from three_workflow_delivery_v3.release.eligibility import (
    EligibilityResult,
    EnabledGovernanceActivation,
    GovernanceObservation,
    LiveEligibilityContext,
    parse_governance_attestation,
)
from three_workflow_delivery_v3.release.static_reference_model import (
    STATIC_REFERENCE_POLICY_ID,
    BoundedStaticReferenceResult,
    StaticReferenceErrorKind,
    StaticReferenceFinding,
)
from three_workflow_delivery_v3.release.static_reference_policy import (
    STATIC_REFERENCE_POLICY_DIGEST,
)
from three_workflow_delivery_v3.repository.descriptors import (
    GOVERNANCE_MAX_AGE_DAYS,
    GOVERNANCE_PATH,
    GOVERNANCE_REF,
    GOVERNANCE_REPOSITORY,
    GovernanceSource,
)

from .test_eligibility import _attestation_content, _ready_activation

TARGET = "e" * 40
NOW = datetime(2026, 9, 1, 6, 0, tzinfo=UTC)
REPOSITORY_ROOT = Path("/controlled/current-run-repository")
WORKFLOW_RUN_ID = 8101
COMPLETE_EVENT_COUNT = 3
REJECTED_EVENT_COUNT = 2


def _context() -> LiveEligibilityContext:
    return LiveEligibilityContext(
        purpose="live-release",
        request_id="release-request-42",
        workflow_run_id=WORKFLOW_RUN_ID,
        selected_ref="refs/heads/release",
        target=TARGET,
        repository_model_digest="sha256:" + ("1" * 64),
        producer="evaluate-live-eligibility",
        control="trusted",
        release_policy_digest="sha256:" + ("2" * 64),
        catalog_digest="sha256:" + ("3" * 64),
    )


def _governance() -> GovernanceObservation:
    source = GovernanceSource(
        repository=GOVERNANCE_REPOSITORY,
        ref=GOVERNANCE_REF,
        path=GOVERNANCE_PATH,
        max_age_days=GOVERNANCE_MAX_AGE_DAYS,
    )
    attestation = parse_governance_attestation(
        _attestation_content(
            inspected_at=(NOW - timedelta(days=1)).strftime(
                "%Y-%m-%dT%H:%M:%SZ"
            ),
            expires_at=(NOW + timedelta(days=30)).strftime(
                "%Y-%m-%dT%H:%M:%SZ"
            ),
            activation=_ready_activation(
                captured_at=(NOW - timedelta(days=2)).strftime(
                    "%Y-%m-%dT%H:%M:%SZ"
                ),
            ),
            live_enabled=True,
        )
    )
    return GovernanceObservation(
        source=source,
        eligibility_main_sha="a" * 40,
        current_main_sha="c" * 40,
        object_format="sha1",
        blob_oid="b" * 40,
        canonical_content_digest=attestation.content_digest,
        observed_at=NOW,
        attestation=attestation,
    )


def _result(
    *,
    findings: tuple[StaticReferenceFinding, ...] = (),
    error_kind: StaticReferenceErrorKind | None = None,
) -> BoundedStaticReferenceResult:
    return BoundedStaticReferenceResult(
        source_kind="git-target",
        target=TARGET,
        policy_id=STATIC_REFERENCE_POLICY_ID,
        policy_digest=STATIC_REFERENCE_POLICY_DIGEST,
        implementation_identities=(),
        findings=findings,
        error_kind=error_kind,
    )


def _evaluate_with_result(
    monkeypatch: pytest.MonkeyPatch,
    result: BoundedStaticReferenceResult,
) -> tuple[eligibility.LiveEligibilityDecision, list[object]]:
    events: list[object] = []
    governance = _governance()

    def scan(
        repository_root: Path,
        *,
        source_kind: str,
        target: str,
    ) -> BoundedStaticReferenceResult:
        events.append(("scan", repository_root, source_kind, target))
        return result

    def validate(value: BoundedStaticReferenceResult) -> None:
        events.append(("validate", value))

    def observe(
        source: GovernanceSource,
        client: object,
        *,
        now: datetime,
    ) -> GovernanceObservation:
        events.append(("governance", source, client, now))
        return governance

    policy = SimpleNamespace(
        governance=governance.source, release_unit="hcoona-release-smoke-npm"
    )
    snapshot = object()
    client = object()
    monkeypatch.setattr(eligibility, "_validate_source", lambda _source: None)
    monkeypatch.setattr(
        eligibility,
        "_validate_live_context",
        lambda _context, _snapshot, _policy: None,
    )
    monkeypatch.setattr(
        eligibility,
        "_ADMITTED_DESTINATION_PRIMITIVE_IDS",
        frozenset(
            {
                cast(
                    "EnabledGovernanceActivation",
                    governance.attestation.activation,
                ).destination_primitive.admission_key
            }
        ),
    )
    monkeypatch.setattr(eligibility, "scan_bounded_static_references", scan)
    monkeypatch.setattr(
        eligibility,
        "validate_live_static_reference_result",
        validate,
    )
    monkeypatch.setattr(eligibility, "observe_governance_source", observe)

    decision = eligibility.evaluate_live_eligibility(
        _context(),
        cast("object", snapshot),
        cast("object", policy),
        cast("object", client),
        repository_root=REPOSITORY_ROOT,
        now=NOW,
    )
    return decision, events


def test_live_eligibility_runs_its_own_exact_target_static_reference_scan(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Scan the current target inside one evaluator call before Governance."""
    expected = _result()

    decision, events = _evaluate_with_result(monkeypatch, expected)

    assert events[0] == (
        "scan",
        REPOSITORY_ROOT,
        "git-target",
        TARGET,
    )
    assert events[1] == ("validate", expected)
    assert events[2][0] == "governance"
    assert events[2][1].ref == "refs/heads/main"
    assert events[2][1] == decision.governance.source
    assert len(events) == COMPLETE_EVENT_COUNT
    assert decision.static_reference is expected
    assert decision.context.workflow_run_id == WORKFLOW_RUN_ID
    assert decision.result is EligibilityResult.PASS
    assert decision.diagnostics == ()

    document = decision.to_document()

    assert document["static-reference"] == expected.to_document()
    assert document["static-reference"]["result"] == "clean"
    assert document["static-reference"]["source-kind"] == "git-target"
    assert document["static-reference"]["target"] == TARGET
    assert "consumer-policy" not in document
    assert document["result"] == "pass"
    assert document["diagnostics"] == []
    assert document["governance"]["eligibility-main-sha"] == "a" * 40
    assert document["governance"]["git-object-format"] == "sha1"
    assert document["governance"]["blob-oid"] == "b" * 40
    assert document["governance"]["canonical-content-digest"] == (
        decision.governance.attestation.content_digest
    )
    admitted_attestation = document["governance"]["admitted-attestation"]
    assert admitted_attestation["schema"] == eligibility.ATTESTATION_SCHEMA
    assert admitted_attestation["accepted_publisher"] == "hcoona"
    assert admitted_attestation["activation"]["state"] == "ready"
    assert {
        "resolved-commit",
        "content-sha256",
    }.isdisjoint(document["governance"])


def test_live_eligibility_validates_static_reference_before_governance(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Do not observe Governance after a malformed scan result is rejected."""
    events: list[object] = []
    policy = SimpleNamespace(
        governance=_governance().source, release_unit="hcoona-release-smoke-npm"
    )
    expected = _result()

    monkeypatch.setattr(eligibility, "_validate_source", lambda _source: None)
    monkeypatch.setattr(
        eligibility,
        "_validate_live_context",
        lambda _context, _snapshot, _policy: None,
    )
    monkeypatch.setattr(
        eligibility,
        "scan_bounded_static_references",
        lambda *args, **kwargs: (
            events.append(("scan", args, kwargs)) or expected
        ),
    )

    def reject(value: BoundedStaticReferenceResult) -> None:
        events.append(("validate", value))
        message = "bounded static-reference Result target mismatch"
        raise ValueError(message)

    monkeypatch.setattr(
        eligibility,
        "validate_live_static_reference_result",
        reject,
    )
    monkeypatch.setattr(
        eligibility,
        "observe_governance_source",
        lambda *args, **kwargs: events.append(("governance", args, kwargs)),
    )

    with pytest.raises(
        ValueError,
        match="bounded static-reference Result target mismatch",
    ):
        eligibility.evaluate_live_eligibility(
            _context(),
            cast("object", object()),
            cast("object", policy),
            cast("object", object()),
            repository_root=REPOSITORY_ROOT,
            now=NOW,
        )

    assert events[0][0] == "scan"
    assert events[1] == ("validate", expected)
    assert len(events) == REJECTED_EVENT_COUNT
