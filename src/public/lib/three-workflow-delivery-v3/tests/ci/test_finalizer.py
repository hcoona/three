"""Finalizer tests for quality verdicts, elapsed time, and summaries."""

from __future__ import annotations

import copy
import json
from dataclasses import replace
from pathlib import Path
from typing import cast

import pytest
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
)
from three_workflow_delivery_v3.ci.evidence import (
    form_ci_evidence,
    form_empty_lane_result,
    form_evidence_lane_result,
)
from three_workflow_delivery_v3.ci.finalizer import (
    admit_planned_evidence,
    derive_ci_supersession_state,
    finalize_ci_slice,
    render_ci_slice_summary,
)
from three_workflow_delivery_v3.records.ci import (
    CI_LANE_IDS,
    CI_WORKFLOW_PATH,
    CiArtifact,
    CiCandidate,
    CiEvidence,
    CiLaneResult,
    CiObligation,
    CiQualificationSnapshot,
    admit_ci_qualification_snapshot_json,
    ci_artifact_digest,
)

FIXTURE_ROOT = Path(__file__).parents[1] / "fixtures" / "ci"
SHA_A = "a" * 40
SHA_B = "b" * 40
SHA_C = "c" * 40
DIGEST_C = "sha256:" + ("c" * 64)
DIGEST_D = "sha256:" + ("d" * 64)
DIGEST_E = "sha256:" + ("e" * 64)
SHA512_F = "sha512:" + ("f" * 128)
PR_SLO_SECONDS = 720


def _candidate(*, manual: bool = False) -> CiCandidate:
    if manual:
        return CiCandidate(
            event_kind="workflow_dispatch",
            purpose="slice-validation",
            repository="hcoona/three",
            workflow_path=CI_WORKFLOW_PATH,
            workflow_sha=SHA_C,
            request_id="slice-validation-7001",
            producer="request",
            workflow_run_id=7001,
            run_attempt=2,
            selected_ref="refs/heads/feature/manual-slice",
            target=SHA_C,
            base_sha=None,
            head_sha=None,
            tested_merge_sha=None,
        )
    return CiCandidate(
        event_kind="pull_request",
        purpose="ci-pr-slice-shadow",
        repository="hcoona/three",
        workflow_path=CI_WORKFLOW_PATH,
        workflow_sha=SHA_C,
        request_id="pr-42",
        producer="request",
        workflow_run_id=7001,
        run_attempt=2,
        selected_ref="refs/pull/42/merge",
        target=SHA_C,
        base_sha=SHA_A,
        head_sha=SHA_B,
        tested_merge_sha=SHA_C,
    )


def _plan_document(mode: str) -> dict[str, JsonValue]:
    document = cast(
        "dict[str, JsonValue]",
        json.loads((FIXTURE_ROOT / "ready-plan.json").read_bytes()),
    )
    document = copy.deepcopy(document)
    document["selected-outputs"] = cast(
        "list[JsonValue]",
        [
            {
                "output-id": "npm-tarball",
                "logical-role": "primary-package",
                "media-kind": "npm-tarball",
            }
        ],
    )
    selected_lanes = CI_LANE_IDS
    if mode == "manual":
        document["candidate"] = cast(
            "dict[str, JsonValue]",
            json.loads((FIXTURE_ROOT / "manual-candidate.json").read_bytes()),
        )
        document["scope-mode"] = "slice-validation"
        document["changed-paths"] = []
        document["diagnostics"] = [
            "slice-validation selected the complete first-slice scope"
        ]
    elif mode == "repository-only":
        selected_lanes = ("root-hk",)
        document["changed-paths"] = ["docs/wiki/README.md"]
        document["selected-project-nodes"] = []
        document["selected-release-units"] = []
        document["selected-variants"] = []
        document["selected-outputs"] = []
        document["diagnostics"] = ["selected repository conformance only"]
    elif mode == "blocked":
        selected_lanes = ()
        document["changed-paths"] = ["src/private/app/unclassified/source.py"]
        document["selected-project-nodes"] = []
        document["selected-release-units"] = []
        document["selected-variants"] = []
        document["selected-outputs"] = []
        document["ready"] = False
        document["diagnostics"] = ["changed path is unclassified"]
    elif mode != "complete":
        message = f"unsupported Plan mode: {mode}"
        raise AssertionError(message)

    obligations = cast("list[JsonValue]", document["obligations"])
    expected_evidence_ids: list[JsonValue] = []
    for value in obligations:
        obligation = cast("dict[str, JsonValue]", value)
        lane_id = cast("str", obligation["lane-id"])
        selected = lane_id in selected_lanes
        obligation["selected"] = selected
        obligation["required"] = selected
        request_digest = canonical_sha256(
            {
                "schema": "workflow-delivery/v3/ci-obligation-request",
                "candidate-digest": canonical_sha256(document["candidate"]),
                "repository-model-digest": document["repository-model-digest"],
                "lane-id": lane_id,
                "definition-id": obligation["definition-id"],
                "definition-digest": obligation["definition-digest"],
                "prerequisites": obligation["prerequisites"],
                "selected": selected,
                "required": selected,
                "scope-mode": document["scope-mode"],
                "changed-paths": document["changed-paths"],
                "selected-project-nodes": document["selected-project-nodes"],
                "selected-release-units": document["selected-release-units"],
                "selected-variants": document["selected-variants"],
                "selected-outputs": document["selected-outputs"],
            }
        )
        evidence_id = (
            f"evidence:{lane_id}:{request_digest.removeprefix('sha256:')}"
        )
        obligation["request-digest"] = request_digest
        obligation["expected-evidence-id"] = evidence_id
        if selected:
            expected_evidence_ids.append(evidence_id)
    document["expected-evidence-ids"] = expected_evidence_ids
    return document


def _plan(mode: str = "complete") -> CiQualificationSnapshot:
    document = _plan_document(mode)
    return admit_ci_qualification_snapshot_json(
        canonicalize(document),
        expected_candidate=_candidate(manual=mode == "manual"),
        expected_repository_model_digest=cast(
            "str",
            document["repository-model-digest"],
        ),
        expected_root_hk_definition=cast(
            "str",
            document["root-hk-definition"],
        ),
        expected_root_hk_definition_digest=cast(
            "str",
            document["root-hk-definition-digest"],
        ),
        expected_plan_digest=canonical_sha256(document),
    )


def _obligation(
    plan: CiQualificationSnapshot,
    lane_id: str,
) -> CiObligation:
    return next(item for item in plan.obligations if item.lane_id == lane_id)


def _artifact(plan: CiQualificationSnapshot) -> CiArtifact:
    return CiArtifact(
        candidate=plan.candidate,
        producer="npm-artifact-build",
        workflow_run_id=plan.workflow_run_id,
        run_attempt=plan.run_attempt,
        output_id="npm-tarball",
        logical_role="primary-package",
        media_kind="npm-tarball",
        artifact_id=9001,
        artifact_name=(
            f"wdv3-{plan.workflow_run_id}-{plan.run_attempt}-npm-tarball.tgz"
        ),
        artifact_url=(
            f"https://github.com/{plan.candidate.repository}/actions/runs/"
            f"{plan.workflow_run_id}/artifacts/9001"
        ),
        transport_digest=DIGEST_D,
        tarball_basename="hcoona-hcoona-release-smoke-npm-1.2.3.tgz",
        content_sha256=DIGEST_D,
        content_sha512=SHA512_F,
        byte_size=1234,
        provenance_digest=DIGEST_E,
        entries=(
            "package/README.md",
            "package/dist/index.js",
            "package/package.json",
            "package/workflow-delivery/provenance.json",
        ),
        lifecycle_scripts=(("test", "node --test"),),
    )


def _evidence(
    plan: CiQualificationSnapshot,
    lane_id: str,
    *,
    raw_outcome: str = "success",
) -> CiEvidence:
    return form_ci_evidence(
        plan,
        obligation=_obligation(plan, lane_id),
        producer=lane_id,
        workflow_run_id=plan.workflow_run_id,
        run_attempt=plan.run_attempt,
        runner="ubuntu-24.04",
        raw_outcome=raw_outcome,
        output_digests=(DIGEST_C,),
        artifacts=(
            (_artifact(plan),) if lane_id == "npm-artifact-build" else ()
        ),
        diagnostics=("mechanical execution completed",),
    )


def _lane_results(
    plan: CiQualificationSnapshot,
    *,
    outcomes: dict[str, str] | None = None,
) -> tuple[CiLaneResult, ...]:
    selected_outcomes = {} if outcomes is None else outcomes
    return tuple(
        (
            form_evidence_lane_result(
                plan,
                _evidence(
                    plan,
                    obligation.lane_id,
                    raw_outcome=selected_outcomes.get(
                        obligation.lane_id,
                        "success",
                    ),
                ),
            )
            if obligation.selected
            else form_empty_lane_result(plan, lane_id=obligation.lane_id)
        )
        for obligation in plan.obligations
    )


def _finalize(
    plan: CiQualificationSnapshot,
    lane_results: tuple[CiLaneResult, ...],
    *,
    elapsed_seconds: object,
    supersession_state: str | None = None,
):
    state = (
        "not-applicable"
        if plan.candidate.event_kind != "pull_request"
        else "not-superseded"
    )
    return finalize_ci_slice(
        plan,
        lane_results,
        elapsed_seconds=elapsed_seconds,  # type: ignore[arg-type]
        supersession_state=(
            state if supersession_state is None else supersession_state
        ),
    )


def test_success_records_met_pr_slo_without_changing_verdict() -> None:
    """Record a met PR SLO alongside an unchanged successful verdict."""
    plan = _plan()
    decision = _finalize(
        plan,
        _lane_results(plan),
        elapsed_seconds=PR_SLO_SECONDS,
    )
    assert decision.terminal_result == "success"
    assert decision.elapsed_seconds == PR_SLO_SECONDS
    assert decision.pr_slo == "met"
    assert decision.pr_slo_reason == "ordinary-pull-request"
    assert render_ci_slice_summary(decision) == decision.summary.text
    assert "elapsed=720s" in decision.summary.text
    assert "pr-12-minute-slo=met" in decision.summary.text
    assert "candidate-digest=" in decision.summary.text
    assert f"repository-model-digest={plan.repository_model_digest}" in (
        decision.summary.text
    )
    assert f"plan-digest={decision.plan_digest}" in decision.summary.text
    assert "obligations=root-hk=satisfied" in decision.summary.text
    assert (
        f"artifact-digests={ci_artifact_digest(_artifact(plan))}"
        in decision.summary.text
    )
    assert "failure-class=none; next-action=none" in decision.summary.text


def test_missing_pr_slo_does_not_change_quality_verdict() -> None:
    """Keep successful quality successful when the PR SLO is missed."""
    plan = _plan()
    decision = _finalize(
        plan,
        _lane_results(plan),
        elapsed_seconds=PR_SLO_SECONDS + 1,
    )
    assert decision.terminal_result == "success"
    assert decision.pr_slo == "missed"
    assert decision.pr_slo_reason == "ordinary-pull-request"


def test_manual_slice_slo_is_not_applicable() -> None:
    """Report the PR SLO as not applicable for manual slice validation."""
    plan = _plan("manual")
    decision = _finalize(
        plan,
        _lane_results(plan),
        elapsed_seconds=900,
    )
    assert decision.terminal_result == "success"
    assert decision.pr_slo == "not-applicable"
    assert decision.pr_slo_reason == "not-pull-request"
    assert "slice-validation result" in decision.summary.text
    assert "shadow" not in decision.summary.text


@pytest.mark.parametrize(
    "path",
    [
        ".github/workflow-delivery/governance/hcoona-release-smoke-npm.json",
        ".github/workflows/workflow-delivery-v3-ci.yml",
        "src/public/lib/three-workflow-delivery-v3/src/"
        "three_workflow_delivery_v3/ci/planner.py",
        "src/public/lib/three-workflow-delivery-v3/src/"
        "three_workflow_delivery_v3/ci/finalizer.py",
        "Directory.Build.props",
        "Directory.Build.targets",
        "mise.toml",
        "nuget.config",
    ],
)
def test_broad_pr_changes_are_excluded_from_ordinary_slo(path: str) -> None:
    """Exclude broad control and toolchain changes with a closed reason."""
    document = _plan_document("complete")
    document["changed-paths"] = [path]
    if path.startswith(".github/workflow-delivery/governance/") or path in {
        "Directory.Build.props",
        "Directory.Build.targets",
        "nuget.config",
    }:
        document["selected-project-nodes"] = []
        document["selected-release-units"] = []
        document["selected-variants"] = []
        document["selected-outputs"] = []
        selected_lanes = ("root-hk",)
    else:
        selected_lanes = CI_LANE_IDS
    obligations = cast("list[JsonValue]", document["obligations"])
    expected_evidence_ids: list[JsonValue] = []
    for value in obligations:
        obligation = cast("dict[str, JsonValue]", value)
        lane_id = cast("str", obligation["lane-id"])
        selected = lane_id in selected_lanes
        obligation["selected"] = selected
        obligation["required"] = selected
        request_digest = canonical_sha256(
            cast(
                "dict[str, JsonValue]",
                {
                    "schema": "workflow-delivery/v3/ci-obligation-request",
                    "candidate-digest": canonical_sha256(document["candidate"]),
                    "repository-model-digest": document[
                        "repository-model-digest"
                    ],
                    "lane-id": lane_id,
                    "definition-id": obligation["definition-id"],
                    "definition-digest": obligation["definition-digest"],
                    "prerequisites": obligation["prerequisites"],
                    "selected": selected,
                    "required": selected,
                    "scope-mode": document["scope-mode"],
                    "changed-paths": document["changed-paths"],
                    "selected-project-nodes": document[
                        "selected-project-nodes"
                    ],
                    "selected-release-units": document[
                        "selected-release-units"
                    ],
                    "selected-variants": document["selected-variants"],
                    "selected-outputs": document["selected-outputs"],
                },
            )
        )
        evidence_id = (
            f"evidence:{lane_id}:{request_digest.removeprefix('sha256:')}"
        )
        obligation["request-digest"] = request_digest
        obligation["expected-evidence-id"] = evidence_id
        if selected:
            expected_evidence_ids.append(evidence_id)
    document["expected-evidence-ids"] = expected_evidence_ids
    plan = admit_ci_qualification_snapshot_json(
        canonicalize(document),
        expected_candidate=_candidate(),
        expected_repository_model_digest=cast(
            "str",
            document["repository-model-digest"],
        ),
        expected_root_hk_definition=cast(
            "str",
            document["root-hk-definition"],
        ),
        expected_root_hk_definition_digest=cast(
            "str",
            document["root-hk-definition-digest"],
        ),
        expected_plan_digest=canonical_sha256(document),
    )
    decision = _finalize(
        plan,
        _lane_results(plan),
        elapsed_seconds=PR_SLO_SECONDS + 1,
    )
    assert decision.pr_slo == "excluded"
    assert decision.pr_slo_reason == "broad-change"
    assert "pr-12-minute-slo=excluded" in decision.summary.text
    assert "pr-slo-reason=broad-change" in decision.summary.text


def test_completed_quality_failure_is_failure() -> None:
    """Keep a completed failed obligation as a quality failure."""
    plan = _plan()
    decision = _finalize(
        plan,
        _lane_results(plan, outcomes={"project-test": "failure"}),
        elapsed_seconds=600,
    )
    project_test = next(
        disposition
        for disposition in decision.obligation_dispositions
        if disposition.obligation.lane_id == "project-test"
    )
    assert decision.terminal_result == "failure"
    assert decision.failure_class == "quality-failure"
    assert decision.next_action == "fix-quality-failure-and-rerun"
    assert project_test.outcome == "failed"
    assert "project-test=failed" in decision.explanation


def test_missing_or_canceled_selected_work_is_finalizer_incomplete() -> None:
    """Derive incomplete when selected work has no lane result."""
    plan = _plan()
    results = tuple(
        result
        for result in _lane_results(plan)
        if result.lane_id != "project-test"
    )
    decision = _finalize(plan, results, elapsed_seconds=600)
    project_test = next(
        disposition
        for disposition in decision.obligation_dispositions
        if disposition.obligation.lane_id == "project-test"
    )
    assert decision.terminal_result == "incomplete"
    assert decision.failure_class == "incomplete-qualification"
    assert decision.next_action == "rerun-candidate"
    assert project_test.outcome == "incomplete"
    assert project_test.evidence_digests == ()
    assert "project-test" in decision.summary.text


def test_repository_only_and_blocked_plans_close_empty_lanes() -> None:
    """Close unselected lanes and keep a blocked Plan unsuccessful."""
    repository_only = _plan("repository-only")
    repository_decision = _finalize(
        repository_only,
        _lane_results(repository_only),
        elapsed_seconds=300,
    )
    assert tuple(
        item.outcome for item in repository_decision.obligation_dispositions
    ) == ("satisfied", "empty", "empty", "empty")
    assert repository_decision.terminal_result == "success"

    blocked = _plan("blocked")
    blocked_decision = _finalize(
        blocked,
        _lane_results(blocked),
        elapsed_seconds=30,
    )
    assert all(
        item.outcome == "empty"
        for item in blocked_decision.obligation_dispositions
    )
    assert blocked_decision.terminal_result == "failure"
    assert blocked_decision.failure_class == "incomplete-model-plan"
    assert blocked_decision.next_action == "fix-model-plan-and-rerun"
    assert "Plan was not ready" in blocked_decision.summary.text
    assert "changed path is unclassified" in blocked_decision.summary.text
    assert f"plan-digest={blocked_decision.plan_digest}" in (
        blocked_decision.summary.text
    )


def test_supersession_state_is_closed_and_runtime_unsupported_is_explicit() -> (
    None
):
    """Exclude trusted supersession and mark unavailable runtime proof N/A."""
    plan = _plan()
    results = _lane_results(plan)
    superseded = _finalize(
        plan,
        results,
        elapsed_seconds=60,
        supersession_state="superseded",
    )
    unsupported = _finalize(
        plan,
        results,
        elapsed_seconds=60,
        supersession_state="unsupported",
    )

    assert superseded.pr_slo == "excluded"
    assert superseded.pr_slo_reason == "superseded-candidate"
    assert superseded.supersession_reason == "trusted-superseded-candidate"
    assert unsupported.pr_slo == "not-applicable"
    assert unsupported.pr_slo_reason == "supersession-unavailable"
    assert unsupported.supersession_reason == "platform-proof-unavailable"


def test_trusted_current_pr_identity_derives_exact_supersession_state() -> None:
    """Compare the complete current and planned PR identities."""
    plan = _plan()
    assert plan.candidate.base_sha is not None
    assert plan.candidate.head_sha is not None
    assert plan.candidate.tested_merge_sha is not None
    current = {
        "current_base_sha": plan.candidate.base_sha,
        "current_head_sha": plan.candidate.head_sha,
        "current_tested_merge_sha": plan.candidate.tested_merge_sha,
    }

    assert derive_ci_supersession_state(plan, **current) == "not-superseded"
    for field in current:
        superseded = current | {field: "f" * 40}
        assert derive_ci_supersession_state(plan, **superseded) == "superseded"
    with pytest.raises(ValueError, match="unavailable"):
        derive_ci_supersession_state(
            plan,
            **(current | {"current_head_sha": "not-a-sha"}),
        )
    with pytest.raises(ValueError, match="pull-request"):
        derive_ci_supersession_state(
            _plan("manual"),
            **current,
        )


def test_finalizer_admits_only_exact_nonduplicate_plan_evidence() -> None:
    """Admit only conflict-free Evidence from the exact Plan."""
    plan = _plan()
    evidence = tuple(
        _evidence(plan, obligation.lane_id) for obligation in plan.obligations
    )
    assert admit_planned_evidence(plan, evidence) == evidence
    with pytest.raises(ValueError, match="duplicate or conflicting"):
        admit_planned_evidence(plan, (evidence[0], evidence[0]))
    other_plan = _plan("manual")
    with pytest.raises(ValueError, match="exact Plan binding"):
        admit_planned_evidence(plan, (_evidence(other_plan, "root-hk"),))


def test_finalizer_rejects_duplicate_or_nonempty_unselected_lane() -> None:
    """Reject duplicate lane results and omitted empty lane results."""
    plan = _plan()
    results = _lane_results(plan)
    with pytest.raises(ValueError, match="duplicate or conflicting"):
        _finalize(
            plan,
            (*results, results[0]),
            elapsed_seconds=60,
        )

    repository_only = _plan("repository-only")
    root_result = form_evidence_lane_result(
        repository_only,
        _evidence(repository_only, "root-hk"),
    )
    with pytest.raises(ValueError, match="empty result"):
        _finalize(
            repository_only,
            (root_result,),
            elapsed_seconds=60,
        )


def test_decision_rejects_summary_or_slo_contradiction() -> None:
    """Reject human summary or SLO fields that contradict machine facts."""
    plan = _plan()
    decision = _finalize(
        plan,
        _lane_results(plan),
        elapsed_seconds=600,
    )
    with pytest.raises(ValueError, match="Summary text"):
        replace(
            decision,
            summary=replace(
                decision.summary,
                text="non-authoritative shadow result: contradictory",
            ),
        )
    with pytest.raises(ValueError, match="SLO result"):
        replace(decision, pr_slo="missed")


@pytest.mark.parametrize("elapsed_seconds", [-1, 1.5, True])
def test_finalizer_requires_exact_nonnegative_elapsed_seconds(
    elapsed_seconds: object,
) -> None:
    """Require trusted elapsed time as an exact nonnegative integer."""
    plan = _plan()
    with pytest.raises((TypeError, ValueError)):
        _finalize(
            plan,
            _lane_results(plan),
            elapsed_seconds=elapsed_seconds,  # type: ignore[arg-type]
        )
