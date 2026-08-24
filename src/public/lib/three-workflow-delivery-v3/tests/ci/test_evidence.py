"""Evidence and static-lane result tests for the first-slice Plan."""

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
    admit_lane_result_for_plan,
    form_ci_evidence,
    form_empty_lane_result,
    form_evidence_lane_result,
    normalize_required_outcome,
)
from three_workflow_delivery_v3.records.ci import (
    CI_LANE_IDS,
    CI_WORKFLOW_PATH,
    CiArtifact,
    CiCandidate,
    CiEvidence,
    CiObligation,
    CiQualificationSnapshot,
    admit_ci_qualification_snapshot_json,
)

FIXTURE_ROOT = Path(__file__).parents[1] / "fixtures" / "ci"
SHA_A = "a" * 40
SHA_B = "b" * 40
SHA_C = "c" * 40
DIGEST_C = "sha256:" + ("c" * 64)
DIGEST_D = "sha256:" + ("d" * 64)
DIGEST_E = "sha256:" + ("e" * 64)
SHA512_F = "sha512:" + ("f" * 128)


def _candidate() -> CiCandidate:
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


def _rebind_plan_document(
    source: dict[str, JsonValue],
    *,
    selected_lanes: tuple[str, ...],
) -> dict[str, JsonValue]:
    document = copy.deepcopy(source)
    document.setdefault("selected-outputs", [])
    complete_slice = selected_lanes == CI_LANE_IDS
    document["changed-paths"] = (
        source["changed-paths"] if complete_slice else ["docs/wiki/README.md"]
    )
    document["selected-project-nodes"] = (
        ["@hcoona/hcoona-release-smoke-npm"] if complete_slice else []
    )
    document["selected-release-units"] = (
        ["hcoona-release-smoke-npm"] if complete_slice else []
    )
    document["selected-variants"] = ["npm-package"] if complete_slice else []
    document["selected-outputs"] = (
        [
            {
                "output-id": "npm-tarball",
                "logical-role": "primary-package",
                "media-kind": "npm-tarball",
            }
        ]
        if complete_slice
        else []
    )
    obligations = cast("list[JsonValue]", document["obligations"])
    expected_evidence_ids: list[JsonValue] = []
    for value in obligations:
        obligation = cast("dict[str, JsonValue]", value)
        lane_id = cast("str", obligation["lane-id"])
        selected = lane_id in selected_lanes
        obligation["selected"] = selected
        obligation["required"] = selected
        request = cast(
            "dict[str, JsonValue]",
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
            },
        )
        request_digest = canonical_sha256(request)
        evidence_id = (
            f"evidence:{lane_id}:{request_digest.removeprefix('sha256:')}"
        )
        obligation["request-digest"] = request_digest
        obligation["expected-evidence-id"] = evidence_id
        if selected:
            expected_evidence_ids.append(evidence_id)
    document["expected-evidence-ids"] = expected_evidence_ids
    return document


def _plan(
    selected_lanes: tuple[str, ...] = CI_LANE_IDS,
) -> CiQualificationSnapshot:
    source = cast(
        "dict[str, JsonValue]",
        json.loads((FIXTURE_ROOT / "ready-plan.json").read_bytes()),
    )
    document = _rebind_plan_document(
        source,
        selected_lanes=selected_lanes,
    )
    encoded = canonicalize(document)
    return admit_ci_qualification_snapshot_json(
        encoded,
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
    lane_id: str = "root-hk",
    *,
    raw_outcome: str = "success",
    diagnostics: tuple[str, ...] = ("mechanical execution completed",),
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
        diagnostics=diagnostics,
    )


def test_evidence_and_selected_lane_bind_exact_plan_position() -> None:
    """Bind Evidence and its lane result to one selected Plan position."""
    plan = _plan()
    evidence = _evidence(plan, "project-test")
    lane = form_evidence_lane_result(plan, evidence)
    assert (
        evidence.evidence_id
        == _obligation(
            plan,
            "project-test",
        ).expected_evidence_id
    )
    assert evidence.normalized_outcome == "satisfied"
    assert lane.disposition == "satisfied"
    assert lane.evidence == evidence
    assert admit_lane_result_for_plan(plan, lane) == lane


def test_unselected_lane_emits_exact_empty_result() -> None:
    """Emit empty only for an unselected static lane."""
    plan = _plan(("root-hk",))
    lane = form_empty_lane_result(plan, lane_id="project-build")
    assert lane.disposition == "empty"
    assert lane.evidence is None
    with pytest.raises(ValueError, match="root-hk"):
        form_empty_lane_result(plan, lane_id="root-hk")


@pytest.mark.parametrize(
    ("field", "value", "error"),
    [
        ("producer", "project-test", "producer"),
        ("workflow_run_id", 7002, "run or attempt"),
        ("run_attempt", 3, "run or attempt"),
        ("runner", "windows-2025", "runner"),
        ("output_digests", (), "requires an output digest"),
    ],
)
def test_evidence_rejects_wrong_binding_or_missing_output(
    field: str,
    value: object,
    error: str,
) -> None:
    """Reject substituted execution identity or absent output facts."""
    plan = _plan()
    kwargs: dict[str, object] = {
        "obligation": _obligation(plan, "root-hk"),
        "producer": "root-hk",
        "workflow_run_id": plan.workflow_run_id,
        "run_attempt": plan.run_attempt,
        "runner": "ubuntu-24.04",
        "raw_outcome": "success",
        "output_digests": (DIGEST_C,),
    }
    kwargs[field] = value
    with pytest.raises((TypeError, ValueError), match=error):
        form_ci_evidence(plan, **kwargs)  # type: ignore[arg-type]


def test_evidence_rejects_substituted_obligation_and_lane() -> None:
    """Reject obligation and lane substitution after planning."""
    plan = _plan()
    root = _obligation(plan, "root-hk")
    with pytest.raises(ValueError, match="not planned"):
        form_ci_evidence(
            plan,
            obligation=replace(root, obligation_id="ci:substitute"),
            producer="root-hk",
            workflow_run_id=plan.workflow_run_id,
            run_attempt=plan.run_attempt,
            runner="ubuntu-24.04",
            raw_outcome="success",
            output_digests=(DIGEST_C,),
        )
    lane = form_evidence_lane_result(plan, _evidence(plan))
    with pytest.raises(ValueError, match="Evidence does not match"):
        replace(lane, lane_id="project-test", producer="project-test")


@pytest.mark.parametrize(
    ("raw", "normalized"),
    [
        ("success", "satisfied"),
        ("failure", "failed"),
        ("skipped", "skipped"),
        ("timed-out", "timed-out"),
        ("unknown", "unknown"),
    ],
)
def test_required_outcomes_are_closed_and_mechanical(
    raw: str,
    normalized: str,
) -> None:
    """Normalize only mechanically observable executor outcomes."""
    assert normalize_required_outcome(raw) == normalized
    assert _evidence(_plan(), raw_outcome=raw).normalized_outcome == normalized


@pytest.mark.parametrize(
    "outcome",
    [
        "satisfied",
        "failed",
        "canceled",
        "conflicted",
        "incomplete",
        "advisory",
    ],
)
def test_impossible_or_finalizer_only_outcomes_are_not_public(
    outcome: str,
) -> None:
    """Keep conflict and incomplete states out of Evidence formation."""
    with pytest.raises(ValueError, match="invalid closed value"):
        normalize_required_outcome(outcome)


def test_diagnostics_cannot_promote_failed_mechanics() -> None:
    """Ignore human diagnostic claims when normalizing mechanics."""
    evidence = _evidence(
        _plan(),
        raw_outcome="failure",
        diagnostics=("all checks passed",),
    )
    assert evidence.normalized_outcome == "failed"


def test_success_closure_is_lane_specific() -> None:
    """Require npm success to bind both output provenance and artifact."""
    plan = _plan()
    npm = _obligation(plan, "npm-artifact-build")
    common = {
        "obligation": npm,
        "producer": "npm-artifact-build",
        "workflow_run_id": plan.workflow_run_id,
        "run_attempt": plan.run_attempt,
        "runner": "ubuntu-24.04",
        "raw_outcome": "success",
    }
    with pytest.raises(ValueError, match="CI artifact record"):
        form_ci_evidence(
            plan,
            **common,
            output_digests=(DIGEST_C,),
        )
    with pytest.raises(ValueError, match="output digest"):
        form_ci_evidence(
            plan,
            **common,
            output_digests=(),
            artifacts=(_artifact(plan),),
        )

    root = _obligation(plan, "root-hk")
    with pytest.raises(ValueError, match="cannot claim CI artifacts"):
        form_ci_evidence(
            plan,
            obligation=root,
            producer="root-hk",
            workflow_run_id=plan.workflow_run_id,
            run_attempt=plan.run_attempt,
            runner="ubuntu-24.04",
            raw_outcome="success",
            output_digests=(DIGEST_C,),
            artifacts=(_artifact(plan),),
        )


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("output_id", "other-output"),
        ("logical_role", "secondary-package"),
        ("media_kind", "generic-archive"),
    ],
)
def test_npm_artifact_identity_must_match_planned_output_contract(
    field: str,
    value: str,
) -> None:
    """Bind output ID, logical role, and media kind to the immutable Plan."""
    plan = _plan()
    if field == "output_id":
        mutated = replace(
            _artifact(plan),
            output_id=value,
            artifact_name="wdv3-7001-2-other-output.tgz",
        )
    elif field == "logical_role":
        mutated = replace(_artifact(plan), logical_role=value)
    else:
        mutated = replace(_artifact(plan), media_kind=value)

    with pytest.raises(ValueError, match="planned output contract"):
        form_ci_evidence(
            plan,
            obligation=_obligation(plan, "npm-artifact-build"),
            producer="npm-artifact-build",
            workflow_run_id=plan.workflow_run_id,
            run_attempt=plan.run_attempt,
            runner="ubuntu-24.04",
            raw_outcome="success",
            output_digests=(DIGEST_C,),
            artifacts=(mutated,),
        )


def test_unsuccessful_evidence_may_close_without_output_digests() -> None:
    """Permit a closed failed outcome when no output artifact was produced."""
    plan = _plan()
    evidence = form_ci_evidence(
        plan,
        obligation=_obligation(plan, "project-test"),
        producer="project-test",
        workflow_run_id=plan.workflow_run_id,
        run_attempt=plan.run_attempt,
        runner="ubuntu-24.04",
        raw_outcome="failure",
        output_digests=(),
    )
    assert evidence.normalized_outcome == "failed"
    assert evidence.output_digests == ()
    assert evidence.artifacts == ()
