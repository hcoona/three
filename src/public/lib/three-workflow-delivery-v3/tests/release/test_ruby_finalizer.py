"""Ruby scalar terminal admission, fresh zero-action proof and uncertainty."""

from copy import deepcopy
from dataclasses import replace
from datetime import timedelta

import pytest
from three_workflow_delivery_v3.adapters.ruby_registry import RubyHttpResponse
from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.records.release_transport import (
    ReleaseAdmissionBindings,
)
from three_workflow_delivery_v3.release.attempt_finalizer import (
    finalize_attempt_outcome,
)
from three_workflow_delivery_v3.release.ruby_finalizer import (
    RubyExactSatisfiedProof,
    RubyFinalizationInputs,
    finalize_ruby_attempt_outcome,
    ruby_exact_proof_from_document,
)
from three_workflow_delivery_v3.release.ruby_publication import (
    RubyPublicationSnapshot,
)
from three_workflow_delivery_v3.release.ruby_qualification import (
    RubyQualificationDecision,
)

from ..ruby_integration_fixtures import NOW, reference
from . import ruby_fixtures
from .ruby_fixtures import execute

ruby_publication_cases = ruby_fixtures.ruby_publication_cases


def _inputs(case):
    marker = case.marker
    authorization = marker.authorization
    bundle = authorization.bundle
    snapshot = bundle.snapshot
    observation = snapshot.observation
    return RubyFinalizationInputs(
        case.decision,
        observation.decision_reference,
        case.original.content,
        (observation, snapshot.observation_reference),
        (snapshot, bundle.snapshot_reference),
        (bundle, authorization.bundle_reference),
        (authorization, marker.authorization_reference),
    )


def _finalize(inputs, **overrides):
    attempt = inputs.decision.snapshot.attempt
    options = {
        "current": ReleaseAdmissionBindings(
            "live-release",
            attempt.workflow_run_id,
            None,
            attempt.execution.target,
        ),
        "run_attempt": 1,
        "publisher_conclusion": "success",
        "publication_step_outcome": "success",
        "publication_terminal_reference": canonicalize(
            None
            if inputs.terminal is None
            else inputs.terminal[1].to_document()
        ).decode(),
        "observation_conclusion": "success",
    }
    entry = overrides.pop("entry", finalize_attempt_outcome)
    options.update(overrides)
    return entry(inputs, **options)


def _exact_inputs(case):
    observation = replace(case.snapshot.observation, native=case.exact)
    observation_ref = reference(observation.to_document(), 911)
    snapshot = RubyPublicationSnapshot(observation, observation_ref)
    snapshot_ref = reference(snapshot.to_document(), 912)
    proof = RubyExactSatisfiedProof(
        snapshot,
        snapshot_ref,
        replace(
            case.decision.snapshot.governance,
            observed_at=NOW + timedelta(seconds=1),
        ),
        case.exact,
        NOW + timedelta(seconds=1),
    )
    return RubyFinalizationInputs(
        case.decision,
        observation.decision_reference,
        case.original.content,
        (observation, observation_ref),
        (snapshot, snapshot_ref),
        exact_proof=(proof, reference(proof.to_document(), 913)),
    )


@pytest.mark.parametrize("state", ["published", "rejected", "lost"])
@pytest.mark.parametrize(
    "entry", [finalize_attempt_outcome, finalize_ruby_attempt_outcome]
)
def test_ruby_result_finalizes_through_shared_scalar_dispatch(
    ruby_publication_cases, tmp_path, state, entry
):
    """Shared Outcome uses audited Result lineage and mutation uncertainty."""
    case = ruby_publication_cases["rubygems"]
    responses = {
        "published": [
            RubyHttpResponse(200, b"accepted"),
            *case.exact_responses(),
        ],
        "rejected": [RubyHttpResponse(409, b"rejected")],
        "lost": [OSError("lost response")],
    }
    result, _transport, _timeline = execute(case, tmp_path, responses[state])
    result_ref = reference(result.to_document(), 920)
    inputs = replace(
        _inputs(case),
        terminal=(result, result_ref),
        result_marker=(case.marker, result.marker_reference),
    )
    outcome = _finalize(inputs, entry=entry)
    assert outcome.disposition == (
        "published" if state == "published" else "publication-failed"
    )
    assert outcome.possibly_mutated is (state != "published")
    assert outcome.direct_predecessor.kind == "publication-result"
    assert outcome.direct_predecessor.reference == result_ref
    assert outcome.attempt == case.marker.attempt


@pytest.mark.parametrize("conclusion", ["success", "failure", "cancelled"])
def test_ruby_marker_only_finalization_is_unknown_possibly_mutated(
    ruby_publication_cases, conclusion
):
    """A job scalar cannot stand in for an absent post-marker Result."""
    case = ruby_publication_cases["rubygems"]
    marker_ref = reference(case.marker.to_document(), 907)
    outcome = _finalize(
        replace(_inputs(case), terminal=(case.marker, marker_ref)),
        publisher_conclusion=conclusion,
        publication_step_outcome=conclusion,
    )
    assert outcome.disposition == "unknown"
    assert outcome.possibly_mutated is True
    assert outcome.direct_predecessor.kind == "mutation-marker"
    assert outcome.direct_predecessor.reference == marker_ref


@pytest.mark.parametrize("name", ["rubygems", "github-packages"])
def test_ruby_zero_action_uses_fresh_exact_proof_without_publisher(
    ruby_publication_cases, name
):
    """Fresh independently replayed exact state bypasses approval and upload."""
    inputs = _exact_inputs(ruby_publication_cases[name])
    outcome = _finalize(
        inputs,
        publisher_conclusion="skipped",
        publication_step_outcome=None,
        publication_terminal_reference=None,
    )
    assert outcome.disposition == "exact-satisfied"
    assert outcome.possibly_mutated is False
    assert (
        outcome.direct_predecessor.kind == "exact-satisfied-finalization-proof"
    )
    assert outcome.direct_predecessor.reference == inputs.exact_proof[1]
    assert inputs.bundle is inputs.authorization is inputs.terminal is None


def test_ruby_zero_action_missing_fresh_proof_is_unknown(
    ruby_publication_cases,
):
    """Earlier exact observation cannot satisfy terminal freshness."""
    inputs = replace(
        _exact_inputs(ruby_publication_cases["rubygems"]), exact_proof=None
    )
    outcome = _finalize(
        inputs,
        publisher_conclusion="skipped",
        publication_step_outcome=None,
        publication_terminal_reference=None,
    )
    assert outcome.disposition == "unknown"
    assert outcome.possibly_mutated is False


@pytest.mark.parametrize("with_proof", [False, True])
def test_ruby_zero_action_cannot_schedule_publisher(
    ruby_publication_cases, with_proof
):
    """Neither zero-action branch can acquire an upload job after planning."""
    inputs = _exact_inputs(ruby_publication_cases["rubygems"])
    if not with_proof:
        inputs = replace(inputs, exact_proof=None)
    with pytest.raises(
        ValueError, match=r"foreign authority|scheduled a publisher"
    ):
        _finalize(inputs)


@pytest.mark.parametrize(
    "change", ["time", "governance-time", "missing", "destination"]
)
def test_ruby_exact_proof_rejects_stale_or_inexact_observations(
    ruby_publication_cases, change
):
    """A fresh proof binds its current Snapshot and independently read state."""
    case = ruby_publication_cases["rubygems"]
    proof = _exact_inputs(case).exact_proof[0]
    updates = {
        "time": {"observed_at": NOW},
        "governance-time": {
            "fresh_governance": replace(proof.fresh_governance, observed_at=NOW)
        },
        "missing": {"native": case.missing},
        "destination": {
            "native": ruby_publication_cases["github-packages"].exact
        },
    }
    with pytest.raises(ValueError, match=r"fresh exact|stale or not current"):
        replace(proof, **updates[change])


@pytest.mark.parametrize(
    "change", ["run", "target", "purpose", "rerun", "boolean-attempt"]
)
def test_ruby_finalizer_rejects_foreign_current_run_or_purpose(
    ruby_publication_cases, change
):
    """Current platform bindings own terminal admission."""
    inputs = _inputs(ruby_publication_cases["rubygems"])
    attempt = inputs.decision.snapshot.attempt
    overrides = {
        "run": {
            "current": ReleaseAdmissionBindings(
                "live-release", 1000, None, attempt.execution.target
            )
        },
        "target": {
            "current": ReleaseAdmissionBindings(
                "live-release", attempt.workflow_run_id, None, "f" * 40
            )
        },
        "purpose": {
            "current": ReleaseAdmissionBindings(
                "release-simulation",
                attempt.workflow_run_id,
                1,
                attempt.execution.target,
            )
        },
        "rerun": {"run_attempt": 2},
        "boolean-attempt": {"run_attempt": True},
    }
    with pytest.raises(ValueError, match="current Attempt"):
        _finalize(inputs, **overrides[change])


@pytest.mark.parametrize(
    "link",
    [
        "decision_reference",
        "observation",
        "publication",
        "bundle",
        "authorization",
    ],
)
def test_ruby_finalizer_rejects_rebound_predecessor_transport(
    ruby_publication_cases, link
):
    """Matching payload digest alone cannot substitute a direct predecessor."""
    case = ruby_publication_cases["rubygems"]
    inputs = replace(
        _inputs(case),
        terminal=(case.marker, reference(case.marker.to_document(), 907)),
    )
    value = getattr(inputs, link)
    changed = (
        replace(value, artifact_id=999)
        if link == "decision_reference"
        else (value[0], replace(value[1], artifact_id=999))
    )
    with pytest.raises(ValueError, match=r"lineage|Authorization"):
        _finalize(replace(inputs, **{link: changed}))


@pytest.mark.parametrize(
    "scalar", [" null", "null\n", "{}", "[]", "true", "{invalid"]
)
def test_ruby_finalizer_rejects_noncanonical_terminal_scalar(
    ruby_publication_cases, scalar
):
    """Admit only the shared canonical terminal scalar."""
    with pytest.raises((ValueError, TypeError)):
        _finalize(
            _inputs(ruby_publication_cases["rubygems"]),
            publication_terminal_reference=scalar,
        )


def test_ruby_finalizer_rejects_missing_or_different_marker_predecessor(
    ruby_publication_cases, tmp_path
):
    """Result admission requires its exact current marker reference."""
    case = ruby_publication_cases["rubygems"]
    result, _transport, _timeline = execute(
        case, tmp_path, [RubyHttpResponse(409, b"rejected")]
    )
    inputs = replace(
        _inputs(case), terminal=(result, reference(result.to_document(), 920))
    )
    with pytest.raises(ValueError, match="exact marker predecessor"):
        _finalize(inputs)
    with pytest.raises(ValueError, match="exact marker predecessor"):
        _finalize(
            replace(
                inputs,
                result_marker=(
                    case.marker,
                    replace(result.marker_reference, artifact_id=999),
                ),
            )
        )
    with pytest.raises(ValueError, match="terminal scalar"):
        _finalize(
            replace(
                inputs, result_marker=(case.marker, result.marker_reference)
            ),
            publication_terminal_reference=canonicalize(
                reference(result.to_document(), 999).to_document()
            ).decode(),
        )


def test_ruby_failed_qualification_cannot_acquire_downstream_authority(
    ruby_publication_cases,
):
    """A nonpassing decision stops all downstream authority."""
    case = ruby_publication_cases["rubygems"]
    failed = RubyQualificationDecision(case.decision.snapshot, ())
    inputs = RubyFinalizationInputs(
        failed, reference(failed.to_document()), case.original.content
    )
    assert (
        _finalize(
            inputs,
            publisher_conclusion="skipped",
            publication_step_outcome=None,
        )
        is None
    )
    with pytest.raises(ValueError, match="cannot have downstream"):
        _finalize(replace(inputs, observation=_inputs(case).observation))


def test_ruby_finalizer_replays_original_response_instead_of_exact_flag(
    ruby_publication_cases, tmp_path
):
    """A recomputed Result digest cannot legitimize tampered remote bytes."""
    case = ruby_publication_cases["rubygems"]
    result, _transport, _timeline = execute(
        case,
        tmp_path,
        [RubyHttpResponse(200, b"accepted"), *case.exact_responses()],
    )
    observations = deepcopy(result.visibility)
    observations[0]["native"]["responses"][-1]["response"]["body-base64"] = (
        "eA=="
    )
    forged = replace(result, visibility=observations)
    inputs = replace(
        _inputs(case),
        terminal=(forged, reference(forged.to_document(), 920)),
        result_marker=(case.marker, result.marker_reference),
    )
    with pytest.raises(ValueError, match="original responses"):
        _finalize(inputs)


def test_ruby_exact_proof_import_replays_original_and_rejects_open_shape(
    ruby_publication_cases,
):
    """Exact proof import validates original bytes and current lineage."""
    case = ruby_publication_cases["rubygems"]
    proof = _exact_inputs(case).exact_proof[0]
    parsed = ruby_exact_proof_from_document(
        proof.to_document(),
        proof.snapshot,
        proof.fresh_governance,
        case.original.content,
    )
    assert parsed == proof
    document = proof.to_document()
    document["extra"] = True
    with pytest.raises(ValueError, match="differs from current"):
        ruby_exact_proof_from_document(
            document,
            proof.snapshot,
            proof.fresh_governance,
            case.original.content,
        )
