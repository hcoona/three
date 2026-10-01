"""Immutable Ruby observation, approval and pre-mutation authority."""

from dataclasses import replace
from datetime import timedelta

import pytest
from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.release.ruby_publication import (
    RubyApprovalBundle,
    RubyPublicationSnapshot,
    render_ruby_approval_summary,
)

from ..ruby_integration_fixtures import NOW, reference
from . import ruby_fixtures
from .ruby_fixtures import approval_proof

ruby_publication_cases = ruby_fixtures.ruby_publication_cases


@pytest.mark.parametrize("name", ["rubygems", "github-packages"])
@pytest.mark.parametrize(
    ("native_state", "classification", "action"),
    [("missing", "missing", True), ("exact", "exact-satisfied", False)],
)
def test_ruby_publication_snapshot_seals_zero_or_one_exact_action(
    ruby_publication_cases, name, native_state, classification, action
):
    """Keep one qualified original and distinct registry authority."""
    case = ruby_publication_cases[name]
    observation = replace(
        case.snapshot.observation, native=getattr(case, native_state)
    )
    snapshot = RubyPublicationSnapshot(
        observation, reference(observation.to_document())
    )
    assert observation.classification == classification
    assert snapshot.action_required is action
    assert snapshot.attempt == case.decision.snapshot.attempt
    assert snapshot.registry == case.registry
    document = snapshot.to_document()
    expected = None
    if action:
        expected = {
            "kind": "ruby-gem",
            "profile-digest": case.registry.profile_digest,
            "mutable-resource": case.registry.origin
            + "/gems/"
            + case.original.filename,
            "filename": case.original.filename,
            "artifact-reference": (
                case.decision.artifact.reference.to_document()
            ),
        }
    assert document["action"] == expected


@pytest.mark.parametrize("state", ["conflicting", "unknown", "unexpected"])
def test_ruby_publication_blocks_untrusted_native_state_before_approval(
    ruby_publication_cases, state
):
    """Only missing or exact state admits a publication snapshot."""
    case = ruby_publication_cases["rubygems"]
    observation = replace(
        case.snapshot.observation,
        native=replace(case.missing, classification=state),
    )
    assert observation.classification == "blocked"
    with pytest.raises(ValueError, match="blocks publication"):
        RubyPublicationSnapshot(
            observation, reference(observation.to_document())
        )


@pytest.mark.parametrize(
    "change", ["destination", "version", "decision-reference", "naive-time"]
)
def test_ruby_remote_observation_requires_current_qualified_binding(
    ruby_publication_cases, change
):
    """An observation cannot import another registry, version or predecessor."""
    case = ruby_publication_cases["rubygems"]
    updates = {
        "destination": {
            "native": ruby_publication_cases["github-packages"].missing
        },
        "version": {"native": replace(case.missing, version="9.0.0")},
        "decision-reference": {"decision_reference": reference({})},
        "naive-time": {"observed_at": NOW.replace(tzinfo=None)},
    }
    with pytest.raises(ValueError):  # noqa: PT011 - distinct boundary messages
        replace(case.snapshot.observation, **updates[change])


def test_ruby_exact_state_cannot_schedule_approval_bundle(
    ruby_publication_cases,
):
    """A zero-action snapshot cannot borrow the mutating approval path."""
    case = ruby_publication_cases["rubygems"]
    observation = replace(case.snapshot.observation, native=case.exact)
    snapshot = RubyPublicationSnapshot(
        observation, reference(observation.to_document())
    )
    with pytest.raises(ValueError, match="exact action summary"):
        RubyApprovalBundle(
            snapshot,
            reference(snapshot.to_document()),
            case.bundle.summary_reference,
        )


def test_ruby_approval_summary_exposes_effect_bounds_and_rejects_drift(
    ruby_publication_cases,
):
    """Bind the immutable approval summary to original and destination."""
    case = ruby_publication_cases["rubygems"]
    summary = render_ruby_approval_summary(case.snapshot).decode()
    for fact in (
        case.original.filename,
        case.original.digest,
        case.registry.origin,
        case.registry.profile_digest,
        case.snapshot.attempt.execution.target,
    ):
        assert fact in summary
    assert "Only HTTP 200" in summary
    assert "No retry, rollback" in summary
    assert case.bundle.summary == summary.encode()
    with pytest.raises(ValueError, match="exact action summary"):
        replace(case.bundle, summary_reference=reference({}))
    with pytest.raises(ValueError, match="immutable reference"):
        replace(case.bundle, snapshot_reference=reference({}))


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("repository", "hcoona/foreign"),
        ("run-id", 1000),
        ("run-attempt", 2),
        ("run-attempt", True),
        ("target", "f" * 40),
        ("environment", "foreign"),
        ("environment-id", 1),
        ("environment-id", True),
        ("deployment-id", 0),
        ("deployment-id", True),
        ("reviewer-id", 1),
        ("reviewer", "foreign"),
        ("state", "pending"),
        ("native-response-digest", "invalid"),
        ("sentinel", "foreign"),
        ("extra", True),
    ],
)
def test_ruby_authorization_requires_exact_native_current_run_approval(
    ruby_publication_cases, field, value
):
    """Native proof rejects coercion, pending approval and foreign authority."""
    case = ruby_publication_cases["rubygems"]
    proof = approval_proof(case.snapshot)
    proof[field] = value
    with pytest.raises(ValueError):  # noqa: PT011 - strict proof validators
        replace(case.authorization, approval_evidence=canonicalize(proof))


def test_ruby_authorization_cannot_predate_observation_or_outlive_governance(
    ruby_publication_cases,
):
    """Approval completion stays within its original authority lifetime."""
    authorization = ruby_publication_cases["rubygems"].authorization
    with pytest.raises(ValueError, match="predates"):
        replace(authorization, completed_at=NOW - timedelta(seconds=1))
    with pytest.raises(ValueError, match="stale or not current"):
        replace(authorization, completed_at=NOW + timedelta(days=2))


@pytest.mark.parametrize("state", ["exact", "conflicting", "unknown"])
def test_ruby_marker_requires_fresh_missing_even_if_new_state_is_exact(
    ruby_publication_cases, state
):
    """Native state drift blocks the previously approved upload."""
    case = ruby_publication_cases["rubygems"]
    states = {
        "exact": case.exact,
        "conflicting": replace(case.missing, classification="conflicting"),
        "unknown": replace(case.missing, classification="unknown"),
    }
    with pytest.raises(ValueError, match="state or authority drifted"):
        replace(case.marker, pre_state=states[state])


@pytest.mark.parametrize(
    "change",
    [
        "stale-governance",
        "configuration",
        "before-approval",
        "authorization-reference",
    ],
)
def test_ruby_marker_revalidates_governance_and_approval_predecessors(
    ruby_publication_cases, change
):
    """Fresh inspection cannot silently replace the frozen authority source."""
    marker = ruby_publication_cases["rubygems"].marker
    document = marker.fresh_governance.document
    document["configuration"]["can-admins-bypass"] = True
    updates = {
        "stale-governance": {
            "fresh_governance": replace(
                marker.fresh_governance, observed_at=NOW
            )
        },
        "configuration": {
            "fresh_governance": replace(
                marker.fresh_governance, content=canonicalize(document)
            )
        },
        "before-approval": {"observed_at": NOW},
        "authorization-reference": {"authorization_reference": reference({})},
    }
    with pytest.raises(ValueError):  # noqa: PT011 - independent drift boundaries
        replace(marker, **updates[change])


def test_ruby_marker_accepts_unchanged_admitted_governance_at_later_source(
    ruby_publication_cases,
):
    """An unrelated main revision does not freeze unchanged admitted content."""
    original = ruby_publication_cases["rubygems"].marker
    fresh = replace(original.fresh_governance, source_commit="f" * 40)
    marker = replace(original, fresh_governance=fresh)
    assert marker.fresh_governance.digest == original.fresh_governance.digest
    assert marker.to_document()["governance-source-commit"] == "f" * 40
    assert marker.authorization == original.authorization
