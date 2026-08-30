"""Scenario-focused tests for first-slice CI planning."""

from __future__ import annotations

from dataclasses import replace
from pathlib import Path

import pytest
from three_workflow_delivery_v3.catalogs import catalog_digest
from three_workflow_delivery_v3.ci.planner import (
    form_pull_request_candidate,
    form_slice_validation_candidate,
    plan_ci_qualification,
)
from three_workflow_delivery_v3.records.ci import (
    CI_LANE_IDS,
    CI_WORKFLOW_PATH,
    CiCandidate,
    CiQualificationSnapshot,
)
from three_workflow_delivery_v3.repository.compiler import (
    CompilationContext,
    CompiledBuild,
    CompiledOutput,
    CompiledQualitySelection,
    CompiledReleaseUnit,
    RepositoryModelSnapshot,
    compile_release_policy,
)
from three_workflow_delivery_v3.repository.descriptors import (
    FIRST_SLICE_PACKAGE,
    FIRST_SLICE_POLICY_PATH,
    FIRST_SLICE_RELEASE_UNIT,
    load_release_policy,
)
from three_workflow_delivery_v3.repository.node_provider import (
    NbgvFacts,
    ProjectNode,
)

SHA_A = "a" * 40
SHA_B = "b" * 40
SHA_C = "c" * 40
SHA_D = "d" * 40
PRODUCT_PATH = "src/public/lib/hcoona-release-smoke-npm"
PROJECT_SOURCE = f"{PRODUCT_PATH}/src/index.js"
WORKFLOW_RUN_ID = 7001
RUN_ATTEMPT = 2
REPO_ROOT = Path(__file__).resolve().parents[6]


def _pr_candidate() -> CiCandidate:
    return form_pull_request_candidate(
        repository="hcoona/three",
        request_id="pr-42",
        workflow_run_id=WORKFLOW_RUN_ID,
        run_attempt=RUN_ATTEMPT,
        selected_ref="refs/pull/42/merge",
        base_sha=SHA_A,
        head_sha=SHA_B,
        tested_merge_sha=SHA_C,
        comparison_identity=(SHA_A, SHA_B),
    )


def _manual_candidate() -> CiCandidate:
    return form_slice_validation_candidate(
        repository="hcoona/three",
        request_id="slice-validation-7001",
        workflow_run_id=WORKFLOW_RUN_ID,
        run_attempt=RUN_ATTEMPT,
        selected_ref="refs/heads/feature/manual-slice",
        target=SHA_C,
    )


def _repository_model(
    candidate: CiCandidate,
    *,
    target: str | None = None,
    control: str | None = None,
) -> RepositoryModelSnapshot:
    selected_target = candidate.target if target is None else target
    return RepositoryModelSnapshot(
        context=CompilationContext(
            request_id=candidate.request_id,
            purpose=candidate.purpose,
            workflow_run_id=candidate.workflow_run_id,
            run_attempt=candidate.run_attempt,
            target=selected_target,
            producer="plan",
            control=(
                f"workflow-delivery-v3:{selected_target}"
                if control is None
                else control
            ),
            catalog_digest=catalog_digest(),
        ),
        manifest_digest="sha256:" + ("1" * 64),
        provider_result_digests=("sha256:" + ("2" * 64),),
        project_nodes=(
            ProjectNode(
                project_id=FIRST_SLICE_PACKAGE,
                package_name=FIRST_SLICE_PACKAGE,
                path=PRODUCT_PATH,
                manifest_path=f"{PRODUCT_PATH}/package.json",
                private=False,
                workspace_dependencies=(),
            ),
        ),
        release_units=(
            CompiledReleaseUnit(
                release_unit=FIRST_SLICE_RELEASE_UNIT,
                descriptor_path=(
                    f"{PRODUCT_PATH}/workflow-delivery.release-unit.yml"
                ),
                builds=(
                    CompiledBuild(
                        build_id="npm-package",
                        definition="node/npm-package-v1",
                        project_id=FIRST_SLICE_PACKAGE,
                        entry_point=f"{PRODUCT_PATH}/package.json",
                        outputs=(
                            CompiledOutput(
                                output_id="npm-tarball",
                                role="primary-package",
                                kind="npm-tarball",
                            ),
                        ),
                        required_native_projections=("npmPackageVersion",),
                    ),
                ),
            ),
        ),
        quality=(
            CompiledQualitySelection(
                path=f"{PRODUCT_PATH}/workflow-delivery.quality.yml",
                ecosystem="node",
                preset="node/hcoona-release-smoke-npm-v1",
                required=("node/project-build-v1", "node/project-test-v1"),
                advisory=(),
            ),
        ),
        release_policy_path=FIRST_SLICE_POLICY_PATH,
        release_policy=compile_release_policy(
            load_release_policy(
                REPO_ROOT / FIRST_SLICE_POLICY_PATH,
                _target_path=FIRST_SLICE_POLICY_PATH,
            )
        ),
        nbgv=NbgvFacts(
            canonical_version="1.2.3",
            sem_ver1="1.2.3-beta-0042-e123456",
            sem_ver2="1.2.3-beta.42.ge123456",
            version_height=42,
            git_commit_id=selected_target,
            public_release=False,
            npm_package_version="1.2.3-beta.42.ge123456",
            node_api_result_digest="sha256:" + ("3" * 64),
        ),
        reverse_index=(
            (
                FIRST_SLICE_PACKAGE,
                (f"{FIRST_SLICE_RELEASE_UNIT}/npm-package",),
            ),
        ),
        unresolved=(),
        ready=True,
    )


def _plan(
    *,
    candidate: CiCandidate | None = None,
    changed_paths: tuple[str, ...] | None = (PROJECT_SOURCE,),
    comparison_identity: tuple[str, str] | None = (SHA_A, SHA_B),
    diagnostics: tuple[str, ...] = (),
) -> CiQualificationSnapshot:
    selected_candidate = _pr_candidate() if candidate is None else candidate
    model = _repository_model(selected_candidate)
    return plan_ci_qualification(
        selected_candidate,
        model,
        repository_model_digest=model.snapshot_digest,
        changed_paths=changed_paths,
        comparison_identity=comparison_identity,
        diagnostics=diagnostics,
    )


def _selected_lanes(plan: CiQualificationSnapshot) -> tuple[str, ...]:
    return tuple(
        obligation.lane_id
        for obligation in plan.obligations
        if obligation.selected
    )


def test_candidates_bind_exact_event_identity() -> None:
    """Bind PR comparison identity and manual target-only identity."""
    pr = _pr_candidate()
    assert pr.workflow_path == CI_WORKFLOW_PATH
    assert (pr.base_sha, pr.head_sha, pr.tested_merge_sha) == (
        SHA_A,
        SHA_B,
        SHA_C,
    )
    assert pr.target == pr.workflow_sha == SHA_C

    manual = _manual_candidate()
    assert manual.purpose == "slice-validation"
    assert manual.target == manual.workflow_sha == SHA_C
    assert (manual.base_sha, manual.head_sha, manual.tested_merge_sha) == (
        None,
        None,
        None,
    )


@pytest.mark.parametrize(
    "comparison",
    [None, (SHA_A,), (SHA_A, SHA_A), (SHA_A, SHA_D)],
)
def test_pr_candidate_rejects_unavailable_or_conflicting_comparison(
    comparison: object,
) -> None:
    """Reject missing, malformed, equal, or mismatched PR ranges."""
    kwargs = {
        "repository": "hcoona/three",
        "request_id": "pr-42",
        "workflow_run_id": WORKFLOW_RUN_ID,
        "run_attempt": RUN_ATTEMPT,
        "selected_ref": "refs/pull/42/merge",
        "base_sha": SHA_A,
        "head_sha": SHA_B,
        "tested_merge_sha": SHA_C,
        "comparison_identity": comparison,
    }
    with pytest.raises((TypeError, ValueError)):
        form_pull_request_candidate(**kwargs)  # type: ignore[arg-type]


def test_project_change_selects_complete_first_slice() -> None:
    """Select all four lanes and exact first-slice scope for project work."""
    plan = _plan()
    assert plan.ready
    assert _selected_lanes(plan) == CI_LANE_IDS
    assert plan.selected_project_nodes == (FIRST_SLICE_PACKAGE,)
    assert plan.selected_release_units == (FIRST_SLICE_RELEASE_UNIT,)
    assert plan.selected_variants == ("npm-package",)
    assert plan.selected_outputs == (
        ("npm-tarball", "primary-package", "npm-tarball"),
    )
    assert plan.expected_evidence_ids == tuple(
        obligation.expected_evidence_id for obligation in plan.obligations
    )


@pytest.mark.parametrize(
    "path",
    [
        PROJECT_SOURCE,
        f"{PRODUCT_PATH}/workflow-delivery.release-unit.yml",
        f"{PRODUCT_PATH}/workflow-delivery.quality.yml",
        FIRST_SLICE_POLICY_PATH,
        "src/public/lib/three-workflow-delivery-v3/src/control.py",
        ".github/actions/workflow-delivery-v3-node/action.yml",
        "eng/scripts/workflow_delivery_v3_control.py",
        CI_WORKFLOW_PATH,
    ],
)
def test_slice_affecting_paths_select_all_lanes(path: str) -> None:
    """Treat every first-slice control or product input as slice-affecting."""
    assert _selected_lanes(_plan(changed_paths=(path,))) == CI_LANE_IDS


@pytest.mark.parametrize(
    "path",
    [
        "docs/wiki/README.md",
        "README.md",
        "LICENSES/MIT.txt",
        ".gitattributes",
        "nested/package.json",
        "nested/package-lock.json",
        "tools/postinstall-consumer.js",
        ".github/workflows/consume.yml",
    ],
)
def test_repository_only_change_selects_root_hk(path: str) -> None:
    """Select only root HK for repository-only paths."""
    plan = _plan(changed_paths=(path,))
    assert plan.ready
    assert _selected_lanes(plan) == ("root-hk",)
    assert plan.selected_project_nodes == ()
    assert plan.selected_release_units == ()
    assert plan.selected_variants == ()
    assert plan.selected_outputs == ()


def test_scholarly_publication_change_set_selects_complete_scope() -> None:
    """Classify the scholarly change set with its global toolchain input."""
    plan = _plan(
        changed_paths=(
            ".agents/skills/scholarly-pdf-reconstruction/SKILL.md",
            ".agents/skills/scholarly-print-assembly/scripts/assemble_print.py",
            (
                ".agents/skills/scholarly-render-qa/assets/"
                "release-manifest.schema.json"
            ),
            ".typos.toml",
            "apm.lock.yaml",
            "apm.yml",
            "mise.toml",
            "pyproject.toml",
            (
                "src/private/lib/scholarly-publication/tests/"
                "test_validate_package.py"
            ),
        )
    )

    assert plan.ready
    assert _selected_lanes(plan) == CI_LANE_IDS
    assert plan.selected_project_nodes == (FIRST_SLICE_PACKAGE,)
    assert plan.selected_release_units == (FIRST_SLICE_RELEASE_UNIT,)
    assert plan.selected_variants == ("npm-package",)
    assert plan.selected_outputs == (
        ("npm-tarball", "primary-package", "npm-tarball"),
    )


@pytest.mark.parametrize(
    ("path", "selected_lanes"),
    [
        (".gitattributes", ("root-hk",)),
        (".testagent/plan.md", ("root-hk",)),
        (
            "docs/wiki/analyses/workflow-delivery/v3/agent-handoff.md",
            ("root-hk",),
        ),
        ("docs/wiki/log.md", ("root-hk",)),
        (".github/workflows/REFACTOR_PLAN.md", ("root-hk",)),
        ("hk.pkl", ("root-hk",)),
        (
            (
                "src/public/lib/three-workflow-delivery-v3/src/"
                "three_workflow_delivery_v3/cli.py"
            ),
            CI_LANE_IDS,
        ),
        (
            "src/public/lib/three-workflow-delivery-v3/tests/test_cli.py",
            CI_LANE_IDS,
        ),
        (CI_WORKFLOW_PATH, CI_LANE_IDS),
    ],
)
def test_current_change_surfaces_are_admitted_without_project_overselection(
    path: str,
    selected_lanes: tuple[str, ...],
) -> None:
    """Classify the current implementation diff at its narrowest valid scope."""
    plan = _plan(changed_paths=(path,))
    assert plan.ready
    assert _selected_lanes(plan) == selected_lanes
    if selected_lanes == ("root-hk",):
        assert plan.selected_project_nodes == ()
        assert plan.selected_release_units == ()
        assert plan.selected_variants == ()
        assert plan.selected_outputs == ()


def test_manual_slice_validation_always_selects_complete_slice() -> None:
    """Select the complete slice without a synthetic changed range."""
    plan = _plan(
        candidate=_manual_candidate(),
        changed_paths=None,
        comparison_identity=None,
    )
    assert plan.ready
    assert plan.scope_mode == "slice-validation"
    assert plan.changed_paths == ()
    assert _selected_lanes(plan) == CI_LANE_IDS
    assert plan.selected_outputs == (
        ("npm-tarball", "primary-package", "npm-tarball"),
    )
    assert "repository-wide" not in " ".join(plan.diagnostics).lower()


@pytest.mark.parametrize(
    ("comparison", "paths", "diagnostic"),
    [
        (None, (PROJECT_SOURCE,), "comparison identity is unavailable"),
        (
            (SHA_A, SHA_D),
            (PROJECT_SOURCE,),
            "comparison identity conflicts",
        ),
        (
            (SHA_A, SHA_B),
            ("src/private/app/unclassified/source.py",),
            "changed path is unclassified",
        ),
        (
            (SHA_A, SHA_B),
            (".github/workflows/helper.py",),
            "changed path is unclassified",
        ),
    ],
)
def test_invalid_incremental_inputs_block_without_partial_work(
    comparison: tuple[str, str] | None,
    paths: tuple[str, ...],
    diagnostic: str,
) -> None:
    """Block invalid incremental facts without selecting partial work."""
    plan = _plan(changed_paths=paths, comparison_identity=comparison)
    assert not plan.ready
    assert _selected_lanes(plan) == ()
    assert plan.expected_evidence_ids == ()
    assert plan.selected_project_nodes == ()
    assert diagnostic in " ".join(plan.diagnostics)


def test_planner_binds_repository_model_target_control_and_digest() -> None:
    """Bind planning to the exact current Repository Model Snapshot."""
    candidate = _pr_candidate()
    model = _repository_model(candidate)
    with pytest.raises(ValueError, match="digest does not match"):
        plan_ci_qualification(
            candidate,
            model,
            repository_model_digest="sha256:" + ("f" * 64),
            changed_paths=(PROJECT_SOURCE,),
            comparison_identity=(SHA_A, SHA_B),
        )
    mismatched = _repository_model(candidate, target=SHA_D)
    with pytest.raises(ValueError, match="target does not match"):
        plan_ci_qualification(
            candidate,
            mismatched,
            repository_model_digest=mismatched.snapshot_digest,
            changed_paths=(PROJECT_SOURCE,),
            comparison_identity=(SHA_A, SHA_B),
        )
    mismatched = _repository_model(candidate, control=f"other:{SHA_C}")
    with pytest.raises(ValueError, match="control does not match"):
        plan_ci_qualification(
            candidate,
            mismatched,
            repository_model_digest=mismatched.snapshot_digest,
            changed_paths=(PROJECT_SOURCE,),
            comparison_identity=(SHA_A, SHA_B),
        )


@pytest.mark.parametrize(
    (
        "purpose",
        "request_id",
        "workflow_run_id",
        "run_attempt",
        "message",
    ),
    [
        pytest.param(
            "slice-validation",
            "pr-42",
            WORKFLOW_RUN_ID,
            RUN_ATTEMPT,
            "purpose does not match",
            id="purpose",
        ),
        pytest.param(
            "ci-pr-slice-shadow",
            "pr-43",
            WORKFLOW_RUN_ID,
            RUN_ATTEMPT,
            "request does not match",
            id="request",
        ),
        pytest.param(
            "ci-pr-slice-shadow",
            "pr-42",
            WORKFLOW_RUN_ID + 1,
            RUN_ATTEMPT,
            "workflow run or attempt is not current",
            id="workflow-run",
        ),
        pytest.param(
            "ci-pr-slice-shadow",
            "pr-42",
            WORKFLOW_RUN_ID,
            RUN_ATTEMPT + 1,
            "workflow run or attempt is not current",
            id="run-attempt",
        ),
    ],
)
def test_planner_rejects_repository_model_identity_mismatch(
    purpose: str,
    request_id: str,
    workflow_run_id: int,
    run_attempt: int,
    message: str,
) -> None:
    """Reject recomputed Models whose request identity is not current."""
    candidate = _pr_candidate()
    model = _repository_model(candidate)
    mismatched = replace(
        model,
        context=replace(
            model.context,
            purpose=purpose,
            request_id=request_id,
            workflow_run_id=workflow_run_id,
            run_attempt=run_attempt,
        ),
    )

    with pytest.raises(ValueError, match=message):
        plan_ci_qualification(
            candidate,
            mismatched,
            repository_model_digest=mismatched.snapshot_digest,
            changed_paths=(PROJECT_SOURCE,),
            comparison_identity=(SHA_A, SHA_B),
        )


def test_planner_rejects_wrong_repository_model_producer() -> None:
    """Require the exact Plan-owned Repository Model producer."""
    candidate = _pr_candidate()
    model = _repository_model(candidate)
    mismatched = replace(
        model,
        context=replace(model.context, producer="compile-model"),
    )

    with pytest.raises(ValueError, match="producer must be plan"):
        plan_ci_qualification(
            candidate,
            mismatched,
            repository_model_digest=mismatched.snapshot_digest,
            changed_paths=(PROJECT_SOURCE,),
            comparison_identity=(SHA_A, SHA_B),
        )


def test_changed_paths_are_canonical_and_unique() -> None:
    """Sort trusted paths and reject duplicates or unsafe spellings."""
    plan = _plan(changed_paths=("docs/z.md", "README.md", "docs/a.md"))
    assert plan.changed_paths == ("README.md", "docs/a.md", "docs/z.md")
    with pytest.raises(ValueError, match="duplicate"):
        _plan(changed_paths=("README.md", "README.md"))
    with pytest.raises(ValueError, match="invalid path"):
        _plan(changed_paths=("../README.md",))


def test_authoritative_empty_changed_paths_select_root_hk_only() -> None:
    """Treat an authoritative empty comparison as root-HK-only scope."""
    plan = _plan(changed_paths=())
    assert plan.ready
    assert plan.changed_paths == ()
    assert _selected_lanes(plan) == ("root-hk",)
    assert plan.selected_project_nodes == ()
    assert plan.selected_release_units == ()
    assert plan.selected_variants == ()
    assert plan.selected_outputs == ()


def test_manual_slice_rejects_caller_selected_scope_claims() -> None:
    """Reject changed ranges and repository-wide claims for manual slices."""
    candidate = _manual_candidate()
    model = _repository_model(candidate)
    with pytest.raises(ValueError, match="rejects changed paths"):
        plan_ci_qualification(
            candidate,
            model,
            repository_model_digest=model.snapshot_digest,
            changed_paths=("README.md",),
        )
    with pytest.raises(ValueError, match="cannot claim repository-wide"):
        plan_ci_qualification(
            candidate,
            model,
            repository_model_digest=model.snapshot_digest,
            diagnostics=("repository-wide full validation",),
        )


def test_incomplete_identity_valid_repository_model_blocks_without_lanes() -> (
    None
):
    """Block an identity-valid incomplete model without partial execution."""
    candidate = _pr_candidate()
    model = _repository_model(candidate)
    incomplete = replace(
        model,
        unresolved=("required NBGV facts are unavailable",),
        release_policy=None,
        ready=False,
    )
    plan = plan_ci_qualification(
        candidate,
        incomplete,
        repository_model_digest=incomplete.snapshot_digest,
        changed_paths=(PROJECT_SOURCE,),
        comparison_identity=(SHA_A, SHA_B),
    )
    assert not plan.ready
    assert _selected_lanes(plan) == ()
    assert plan.expected_evidence_ids == ()
    assert "required NBGV facts are unavailable" in " ".join(plan.diagnostics)


def test_planner_rejects_malformed_or_tampered_repository_model() -> None:
    """Raise for malformed closure state or a digest-tampered model."""
    candidate = _pr_candidate()
    model = _repository_model(candidate)
    falsely_complete = replace(
        model,
        unresolved=("authoring is incomplete",),
        ready=False,
    )
    with pytest.raises(ValueError, match="claims compiled policy"):
        plan_ci_qualification(
            candidate,
            falsely_complete,
            repository_model_digest=falsely_complete.snapshot_digest,
            changed_paths=(PROJECT_SOURCE,),
            comparison_identity=(SHA_A, SHA_B),
        )
    malformed = replace(model, release_policy=None, ready=False)
    with pytest.raises(ValueError, match="unresolved facts"):
        plan_ci_qualification(
            candidate,
            malformed,
            repository_model_digest=malformed.snapshot_digest,
            changed_paths=(PROJECT_SOURCE,),
            comparison_identity=(SHA_A, SHA_B),
        )
    incomplete = replace(
        model,
        unresolved=("required NBGV facts are unavailable",),
        release_policy=None,
        ready=False,
    )
    with pytest.raises(ValueError, match="digest does not match"):
        plan_ci_qualification(
            candidate,
            incomplete,
            repository_model_digest=model.snapshot_digest,
            changed_paths=(PROJECT_SOURCE,),
            comparison_identity=(SHA_A, SHA_B),
        )
