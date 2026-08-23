"""Literal integration scenarios from the approved first-slice CI LLD."""

from __future__ import annotations

import hashlib
import importlib.util
import json
import shutil
import subprocess
import sys
from dataclasses import dataclass, replace
from functools import cache
from pathlib import Path
from typing import TYPE_CHECKING, Any, cast

import pytest
import yaml
from three_workflow_delivery_v3.catalogs import catalog_digest
from three_workflow_delivery_v3.ci.evidence import (
    form_ci_evidence,
    form_empty_lane_result,
    form_evidence_lane_result,
)
from three_workflow_delivery_v3.ci.finalizer import (
    CiBootstrapProjectionRequest,
    finalize_ci_slice,
    qualifies_precoexistence_bootstrap_projection,
    render_ci_slice_summary,
)
from three_workflow_delivery_v3.ci.planner import (
    form_pull_request_candidate,
    form_slice_validation_candidate,
    plan_ci_qualification,
)
from three_workflow_delivery_v3.records.ci import (
    CI_LANE_IDS,
    CiArtifact,
    CiCandidate,
    CiEvidence,
    CiLaneResult,
    CiObligation,
    CiQualificationSnapshot,
    ci_qualification_snapshot_digest,
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

if TYPE_CHECKING:
    from collections.abc import Sequence

REPO_ROOT = Path(__file__).resolve().parents[6]
WORKFLOW_PATH = REPO_ROOT / ".github/workflows/workflow-delivery-v3-ci.yml"
V1_CI_PATH = REPO_ROOT / ".github/workflows/ci.yml"
DISABLED_GOVERNANCE_FIXTURE = (
    REPO_ROOT
    / "src/public/lib/three-workflow-delivery-v3/tests/fixtures/release/"
    "governance-disabled.json"
)
HK_CONFIG = REPO_ROOT / "hk.pkl"
HK_SUPPORT = REPO_ROOT / "src/private/lib/hk"
HK_RANGE_HELPER = Path("eng/scripts/workflow_delivery_v3_hk.py")
CONTROL_STEP_NAME = "v3-control-pytest"
CONSUMER_STEP_NAME = "hcoona-release-smoke-npm-consumer-policy"
POLICY_PATH = "eng/workflow-delivery/v3/policies/hcoona-release-smoke-npm.yml"
SHADOW_CHECK_NAME = "Workflow Delivery v3 / hcoona-release-smoke-npm (shadow)"
SCAN_ERROR_EXIT_CODE = 2

SHA_A = "a" * 40
SHA_B = "b" * 40
SHA_C = "c" * 40
SHA_D = "d" * 40
DIGEST_OUTPUT = "sha256:" + ("c" * 64)
DIGEST_ARTIFACT = "sha256:" + ("d" * 64)
DIGEST_PROVENANCE = "sha256:" + ("e" * 64)
SHA512_ARTIFACT = "sha512:" + ("f" * 128)
WORKFLOW_RUN_ID = 7001
RUN_ATTEMPT = 2
PRODUCT_PATH = "src/public/lib/hcoona-release-smoke-npm"
PROJECT_SOURCE = f"{PRODUCT_PATH}/src/index.ts"
UNRELATED_PRODUCT_SOURCE = "src/public/lib/hcoona-release-smoke/src/index.ts"


def _load_consumer_policy() -> Any:
    script = REPO_ROOT / "eng/scripts/workflow_delivery_v3_consumer_policy.py"
    spec = importlib.util.spec_from_file_location(
        "_workflow_delivery_v3_scenario_consumer_policy",
        script,
    )
    if spec is None or spec.loader is None:
        message = f"cannot load consumer-policy module from {script}"
        raise AssertionError(message)
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


_CONSUMER_POLICY = _load_consumer_policy()
ACCEPTANCE_FIXTURE_PATH = _CONSUMER_POLICY.ACCEPTANCE_FIXTURE_PATH
ACCEPTANCE_NPM_MANIFEST_PATH = _CONSUMER_POLICY.ACCEPTANCE_NPM_MANIFEST_PATH
APPROVED_CONSUMER_EXCEPTIONS = _CONSUMER_POLICY.APPROVED_CONSUMER_EXCEPTIONS
DEPENDENCY_SURFACE_CATALOG = _CONSUMER_POLICY.DEPENDENCY_SURFACE_CATALOG
OWN_DECLARATION_PATH = _CONSUMER_POLICY.OWN_DECLARATION_PATH
PACKAGE_NAME = _CONSUMER_POLICY.PACKAGE_NAME
classify_dependency_surface = _CONSUMER_POLICY.classify_dependency_surface
consumer_policy_main = _CONSUMER_POLICY.main
scan_consumer_policy = _CONSUMER_POLICY.scan_consumer_policy


@dataclass(frozen=True, slots=True)
class HistoryChange:
    """One real Git-history change used to exercise an HK trigger."""

    kind: str
    path: str
    old_path: str | None = None


SURFACE_CASES = (
    (
        "dependency-manifest",
        "consumer/package.json",
        json.dumps(
            {
                "name": "ordinary-consumer",
                "dependencies": {PACKAGE_NAME: "^1.2.3"},
            },
        ),
    ),
    (
        "lockfile",
        "consumer/package-lock.json",
        json.dumps(
            {
                "lockfileVersion": 3,
                "packages": {
                    f"node_modules/{PACKAGE_NAME}": {"version": "1.2.3"},
                },
            },
        ),
    ),
    (
        "workflow",
        ".github/workflows/consumer.yml",
        (
            "name: consumer\n"
            "on: push\n"
            "jobs:\n"
            "  consume:\n"
            "    runs-on: ubuntu-latest\n"
            "    steps:\n"
            f"      - run: pnpm add --save-exact {PACKAGE_NAME}@1.2.3\n"
        ),
    ),
    (
        "composite-action",
        ".github/actions/consumer/action.yml",
        json.dumps(
            {
                "runs": {
                    "using": "composite",
                    "steps": [
                        {
                            "run": (
                                f"pnpm add --save-exact {PACKAGE_NAME}@1.2.3"
                            ),
                            "shell": "bash",
                        },
                    ],
                },
            },
        ),
    ),
    (
        "install-bootstrap-script",
        "tools/install-consumer.sh",
        f"npm install --ignore-scripts {PACKAGE_NAME}@1.2.3\n",
    ),
    (
        "dependency-configuration",
        "renovate.json",
        json.dumps(
            {
                "packageRules": [
                    {"matchPackageNames": [PACKAGE_NAME]},
                ],
            },
        ),
    ),
)
GIT_TRANSITIONS = ("add", "modify", "delete", "rename-out", "rename-in")


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


def _bootstrap_request(
    *,
    pull_request_number: int = 42,
    base_sha: str = SHA_A,
    head_sha: str = SHA_B,
    tested_merge_sha: str = SHA_C,
) -> CiBootstrapProjectionRequest:
    return CiBootstrapProjectionRequest(
        pull_request_number=pull_request_number,
        base_sha=base_sha,
        head_sha=head_sha,
        tested_merge_sha=tested_merge_sha,
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


def _repository_model(candidate: CiCandidate) -> RepositoryModelSnapshot:
    context = CompilationContext(
        request_id=candidate.request_id,
        purpose=candidate.purpose,
        workflow_run_id=candidate.workflow_run_id,
        run_attempt=candidate.run_attempt,
        target=candidate.target,
        producer="plan",
        control=f"workflow-delivery-v3:{candidate.workflow_sha}",
        catalog_digest=catalog_digest(),
    )
    return RepositoryModelSnapshot(
        context=context,
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
            git_commit_id=candidate.target,
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


def _incremental_plan(
    *,
    changed_paths: tuple[str, ...] = (PROJECT_SOURCE,),
    comparison_identity: object = (SHA_A, SHA_B),
) -> CiQualificationSnapshot:
    candidate = _pr_candidate()
    model = _repository_model(candidate)
    return plan_ci_qualification(
        candidate,
        model,
        repository_model_digest=model.snapshot_digest,
        changed_paths=changed_paths,
        comparison_identity=cast("Any", comparison_identity),
    )


def _manual_plan() -> CiQualificationSnapshot:
    candidate = _manual_candidate()
    model = _repository_model(candidate)
    return plan_ci_qualification(
        candidate,
        model,
        repository_model_digest=model.snapshot_digest,
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
        transport_digest=DIGEST_ARTIFACT,
        tarball_basename="hcoona-hcoona-release-smoke-npm-1.2.3.tgz",
        content_sha256=DIGEST_ARTIFACT,
        content_sha512=SHA512_ARTIFACT,
        byte_size=1234,
        provenance_digest=DIGEST_PROVENANCE,
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
    diagnostics: tuple[str, ...] = ("mechanical execution completed",),
) -> CiEvidence:
    return form_ci_evidence(
        plan,
        obligation=_obligation(plan, lane_id),
        producer=lane_id,
        workflow_run_id=WORKFLOW_RUN_ID,
        run_attempt=RUN_ATTEMPT,
        runner="ubuntu-24.04",
        raw_outcome=raw_outcome,
        output_digests=(DIGEST_OUTPUT,),
        artifacts=(
            (_artifact(plan),) if lane_id == "npm-artifact-build" else ()
        ),
        diagnostics=diagnostics,
    )


def _lane_results(
    plan: CiQualificationSnapshot,
    *,
    outcomes: dict[str, str] | None = None,
    diagnostics: dict[str, tuple[str, ...]] | None = None,
) -> tuple[CiLaneResult, ...]:
    selected_outcomes = {} if outcomes is None else outcomes
    selected_diagnostics = {} if diagnostics is None else diagnostics
    results: list[CiLaneResult] = []
    for obligation in plan.obligations:
        if obligation.selected:
            evidence = _evidence(
                plan,
                obligation.lane_id,
                raw_outcome=selected_outcomes.get(
                    obligation.lane_id,
                    "success",
                ),
                diagnostics=selected_diagnostics.get(
                    obligation.lane_id,
                    ("mechanical execution completed",),
                ),
            )
            results.append(form_evidence_lane_result(plan, evidence))
        else:
            results.append(
                form_empty_lane_result(plan, lane_id=obligation.lane_id),
            )
    return tuple(results)


def _finalize(
    plan: CiQualificationSnapshot,
    results: tuple[CiLaneResult, ...],
    *,
    elapsed_seconds: int,
):
    supersession_state = (
        "not-applicable"
        if plan.candidate.event_kind != "pull_request"
        else "not-superseded"
    )
    return finalize_ci_slice(
        plan,
        results,
        elapsed_seconds=elapsed_seconds,
        supersession_state=supersession_state,
    )


def _selected_lanes(plan: CiQualificationSnapshot) -> tuple[str, ...]:
    return tuple(
        obligation.lane_id
        for obligation in plan.obligations
        if obligation.selected
    )


def _workflow() -> dict[Any, Any]:
    return cast(
        "dict[Any, Any]",
        yaml.safe_load(WORKFLOW_PATH.read_text(encoding="utf-8")),
    )


def _workflow_step(job_id: str, name: str) -> dict[str, Any]:
    jobs = cast("dict[str, dict[str, Any]]", _workflow()["jobs"])
    steps = cast("list[dict[str, Any]]", jobs[job_id]["steps"])
    return next(step for step in steps if step.get("name") == name)


def _run(
    command: Sequence[str],
    *,
    cwd: Path,
) -> subprocess.CompletedProcess[str]:
    return subprocess.run(  # noqa: S603
        command,
        cwd=cwd,
        check=True,
        capture_output=True,
        text=True,
    )


def _git(repo: Path, *arguments: str) -> subprocess.CompletedProcess[str]:
    return _run(("git", *arguments), cwd=repo)


def _write(repo: Path, relative_path: str, content: bytes | str) -> None:
    path = repo / relative_path
    path.parent.mkdir(parents=True, exist_ok=True)
    if isinstance(content, bytes):
        path.write_bytes(content)
    else:
        path.write_text(content, encoding="utf-8")


def _commit(repo: Path, message: str) -> str:
    _git(repo, "add", "--all")
    _git(repo, "commit", "--quiet", "--message", message)
    return _git(repo, "rev-parse", "HEAD").stdout.strip()


def _initialize_hk_repository(
    repo: Path,
    *,
    baseline_paths: Sequence[str] = (),
) -> str:
    repo.mkdir()
    shutil.copy2(HK_CONFIG, repo / "hk.pkl")
    shutil.copytree(HK_SUPPORT, repo / "src/private/lib/hk")
    helper = repo / HK_RANGE_HELPER
    helper.parent.mkdir(parents=True)
    shutil.copy2(REPO_ROOT / HK_RANGE_HELPER, helper)
    for path in baseline_paths:
        _write(repo, path, "baseline\n")
    _git(repo, "init", "--quiet")
    _git(repo, "config", "user.name", "Workflow Delivery Scenario")
    _git(
        repo,
        "config",
        "user.email",
        "workflow-delivery-scenario@example.invalid",
    )
    return _commit(repo, "baseline")


@cache
def _hk_executable() -> str:
    install_root = _run(
        ("mise", "where", "hk@1.53.0"),
        cwd=REPO_ROOT,
    ).stdout.strip()
    executable = Path(install_root) / "hk"
    version = _run((str(executable), "--version"), cwd=REPO_ROOT)
    assert version.stdout.strip() == "hk 1.53.0"
    return str(executable)


def _changed_paths(repo: Path, base: str, head: str) -> tuple[str, ...]:
    result = _run(
        (
            sys.executable,
            str(repo / HK_RANGE_HELPER),
            "--repository",
            str(repo),
            "--from-ref",
            base,
            "--to-ref",
            head,
        ),
        cwd=repo,
    )
    return tuple(json.loads(result.stdout))


def _hk_step_from_result(
    result: subprocess.CompletedProcess[str],
    step_name: str,
) -> dict[str, Any]:
    plan = cast("dict[str, Any]", json.loads(result.stdout))
    steps = cast("list[dict[str, Any]]", plan["steps"])

    assert plan["hook"] == "check"
    assert plan["runType"] == "check"
    assert "small" in cast("list[str]", plan["profiles"])
    assert len(steps) == 1
    assert steps[0]["name"] == step_name
    return steps[0]


def _hk_step_for_range(
    repo: Path,
    base: str,
    head: str,
    step_name: str,
) -> dict[str, Any]:
    result = _run(
        (
            sys.executable,
            str(repo / HK_RANGE_HELPER),
            "--repository",
            str(repo),
            "--from-ref",
            base,
            "--to-ref",
            head,
            "--",
            _hk_executable(),
            "--no-progress",
            "check",
            "--plan",
            "--json",
            "--step",
            step_name,
        ),
        cwd=repo,
    )
    return _hk_step_from_result(result, step_name)


def _hk_step_for_all(repo: Path, step_name: str) -> dict[str, Any]:
    result = _run(
        (
            _hk_executable(),
            "--no-progress",
            "check",
            "--plan",
            "--json",
            "--step",
            step_name,
            "--all",
        ),
        cwd=repo,
    )
    return _hk_step_from_result(result, step_name)


def _history_change(
    category: str,
    surface_path: str,
    transition: str,
) -> HistoryChange:
    archive_path = f"archive/{category}.txt"
    if transition == "rename-out":
        return HistoryChange("rename", archive_path, old_path=surface_path)
    if transition == "rename-in":
        return HistoryChange("rename", surface_path, old_path=archive_path)
    return HistoryChange(transition, surface_path)


def _apply_history_change(repo: Path, change: HistoryChange) -> None:
    if change.kind == "add":
        _write(repo, change.path, "added dependency surface\n")
    elif change.kind == "modify":
        _write(repo, change.path, "modified dependency surface\n")
    elif change.kind == "delete":
        (repo / change.path).unlink()
    elif change.kind == "rename":
        assert change.old_path is not None
        (repo / change.path).parent.mkdir(parents=True, exist_ok=True)
        _git(repo, "mv", change.old_path, change.path)
    else:
        message = f"unsupported scenario transition: {change.kind}"
        raise AssertionError(message)


def _initialize_policy_repository(repo: Path) -> tuple[Path, str]:
    repo.mkdir()
    for path in (
        OWN_DECLARATION_PATH,
        ACCEPTANCE_FIXTURE_PATH,
        ACCEPTANCE_NPM_MANIFEST_PATH,
    ):
        source = REPO_ROOT / path
        destination = repo / path
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, destination)
    _git(repo, "init", "--quiet")
    _git(repo, "config", "user.name", "Consumer Policy Scenario")
    _git(
        repo,
        "config",
        "user.email",
        "consumer-policy-scenario@example.invalid",
    )
    return repo, _commit(repo, "approved exceptions")


def test_ci_scenario_project_source_change_selects_complete_slice() -> None:
    """Exercise literal LLD CI scenario 1 over the complete static slice."""
    plan = _incremental_plan()
    results = _lane_results(plan)
    evidence = tuple(cast("CiEvidence", result.evidence) for result in results)
    decision = _finalize(plan, results, elapsed_seconds=60)
    jobs = cast("dict[str, dict[str, Any]]", _workflow()["jobs"])

    assert (
        plan.candidate.base_sha,
        plan.candidate.head_sha,
        plan.candidate.tested_merge_sha,
        plan.candidate.target,
    ) == (SHA_A, SHA_B, SHA_C, SHA_C)
    assert plan.candidate.purpose == "ci-pr-slice-shadow"
    assert plan.scope_mode == "incremental"
    assert plan.changed_paths == (PROJECT_SOURCE,)
    assert (
        _selected_lanes(plan)
        == CI_LANE_IDS
        == (
            "root-hk",
            "project-build",
            "project-test",
            "npm-artifact-build",
        )
    )
    assert tuple(item.prerequisites for item in plan.obligations) == (
        (),
        (),
        (),
        (),
    )
    assert plan.selected_project_nodes == (FIRST_SLICE_PACKAGE,)
    assert plan.selected_release_units == (FIRST_SLICE_RELEASE_UNIT,)
    assert plan.selected_variants == ("npm-package",)
    assert tuple(item.evidence_id for item in evidence) == (
        plan.expected_evidence_ids
    )
    assert all(
        item.plan_digest == ci_qualification_snapshot_digest(plan)
        and item.obligation.selected
        and item.normalized_outcome == "satisfied"
        for item in evidence
    )
    assert evidence[-1].artifacts == (_artifact(plan),)
    assert all(item.artifacts == () for item in evidence[:-1])
    assert tuple(job_id for job_id in jobs if job_id in CI_LANE_IDS) == (
        CI_LANE_IDS
    )
    assert all(jobs[lane_id]["needs"] == "plan" for lane_id in CI_LANE_IDS)
    assert decision.terminal_result == "success"
    assert len(decision.admitted_evidence_digests) == len(CI_LANE_IDS)


@pytest.mark.parametrize(
    "path",
    [
        "mise.toml",
        "mise.lock",
        "package.json",
        "pnpm-lock.yaml",
        "pnpm-workspace.yaml",
    ],
)
def test_global_input_change_runs_complete_slice_scenario(
    path: str,
) -> None:
    """Exercise each global tool input through Plan, lanes, and Finalizer."""
    plan = _incremental_plan(changed_paths=(path,))
    results = _lane_results(plan)
    decision = _finalize(plan, results, elapsed_seconds=60)

    assert plan.ready is True
    assert plan.changed_paths == (path,)
    assert _selected_lanes(plan) == CI_LANE_IDS
    assert plan.selected_project_nodes == (FIRST_SLICE_PACKAGE,)
    assert plan.selected_release_units == (FIRST_SLICE_RELEASE_UNIT,)
    assert plan.selected_variants == ("npm-package",)
    assert tuple(result.lane_id for result in results) == CI_LANE_IDS
    assert all(result.disposition == "satisfied" for result in results)
    assert (
        tuple(
            cast("CiEvidence", result.evidence).evidence_id
            for result in results
        )
        == plan.expected_evidence_ids
    )
    assert decision.terminal_result == "success"
    assert len(decision.admitted_evidence_digests) == len(CI_LANE_IDS)


def test_ci_scenario_slice_validation_selects_full_slice_without_synthetic_range() -> (  # noqa: E501
    None
):
    """Exercise literal LLD CI scenario 2 without inventing a Git range."""
    plan = _manual_plan()
    results = _lane_results(plan)
    decision = _finalize(plan, results, elapsed_seconds=60)
    workflow = _workflow()
    events = cast("dict[str, Any]", workflow[True])
    root_hk_run = cast(
        "str",
        _workflow_step(
            "root-hk",
            "Run permanent root HK and consumer policy",
        )["run"],
    )
    records = (
        plan.candidate.to_document(),
        plan.to_document(),
        *(
            cast("CiEvidence", result.evidence).to_document()
            for result in results
        ),
        *(result.to_document() for result in results),
        decision.to_document(),
    )

    assert events["workflow_dispatch"] is None
    assert plan.candidate.event_kind == "workflow_dispatch"
    assert plan.candidate.purpose == "slice-validation"
    assert (
        plan.candidate.base_sha,
        plan.candidate.head_sha,
        plan.candidate.tested_merge_sha,
    ) == (None, None, None)
    assert plan.candidate.target == plan.candidate.workflow_sha == SHA_C
    assert plan.scope_mode == "slice-validation"
    assert plan.changed_paths == ()
    assert _selected_lanes(plan) == CI_LANE_IDS
    assert plan.selected_project_nodes == (FIRST_SLICE_PACKAGE,)
    assert plan.selected_release_units == (FIRST_SLICE_RELEASE_UNIT,)
    assert plan.selected_variants == ("npm-package",)
    assert all(
        "slice-validation" in json.dumps(record, sort_keys=True)
        for record in records
    )
    assert all(
        "repository-wide" not in json.dumps(record, sort_keys=True).lower()
        and "full validation" not in json.dumps(record, sort_keys=True).lower()
        and "full-validation" not in json.dumps(record, sort_keys=True).lower()
        for record in records
    )
    assert "slice-validation selected the complete first-slice scope" in (
        " ".join(plan.diagnostics)
    )
    assert decision.terminal_result == "success"
    assert "repository-wide" not in decision.summary.text.lower()
    assert "full validation" not in decision.summary.text.lower()
    assert 'if [[ "${GITHUB_EVENT_NAME}" == "pull_request" ]]' in root_hk_run
    assert "workflow_delivery_v3_hk.py" in root_hk_run
    assert "else\n  mise exec -- hk --no-progress check --all" in root_hk_run


def test_ci_scenario_project_test_failure_fails_shadow_check() -> None:
    """Exercise literal LLD CI scenario 3 and propagate failed test Evidence."""
    plan = _incremental_plan()
    results = _lane_results(
        plan,
        outcomes={"project-test": "failure"},
        diagnostics={
            "project-test": (
                "diagnostic text claims the project tests passed",
            ),
        },
    )
    decision = _finalize(plan, results, elapsed_seconds=60)
    project_test = next(
        result for result in results if result.lane_id == "project-test"
    )
    finalizer = cast(
        "dict[str, Any]",
        cast("dict[str, Any]", _workflow()["jobs"])["required-finalizer"],
    )
    enforce_run = cast(
        "str",
        _workflow_step(
            "required-finalizer",
            "Admit available results and finalize",
        )["run"],
    )

    assert project_test.disposition == "failed"
    assert project_test.evidence is not None
    assert project_test.evidence.raw_outcome == "failure"
    assert project_test.evidence.normalized_outcome == "failed"
    assert "passed" in " ".join(project_test.evidence.diagnostics)
    assert tuple(
        result.disposition
        for result in results
        if result.lane_id != "project-test"
    ) == ("satisfied", "satisfied", "satisfied")
    assert {
        item.obligation.lane_id: item.outcome
        for item in decision.obligation_dispositions
    }["project-test"] == "failed"
    assert (
        decision.terminal_result
        == decision.summary.terminal_result
        == ("failure")
    )
    assert "project-test=failed" in decision.explanation
    assert "non-authoritative shadow result" in render_ci_slice_summary(
        decision,
    )
    assert finalizer["name"] == SHADOW_CHECK_NAME
    assert finalizer["if"] == "always()"
    assert '"${cli[@]}" ci finalize' in enforce_run
    assert '--started-at "${STARTED_AT}"' in enforce_run
    assert "date +%s" not in enforce_run


def test_ci_scenario_repository_only_change_has_valid_empty_affected_lanes() -> (  # noqa: E501
    None
):
    """Exercise literal LLD CI scenario 4 with three exact empty lanes."""
    plan = _incremental_plan(changed_paths=("docs/wiki/README.md",))
    results = _lane_results(plan)
    root = results[0]
    empty = results[1:]
    decision = _finalize(plan, results, elapsed_seconds=60)
    incomplete = _finalize(plan, empty, elapsed_seconds=60)
    plan_digest = ci_qualification_snapshot_digest(plan)

    assert plan.ready is True
    assert plan.changed_paths == ("docs/wiki/README.md",)
    assert _selected_lanes(plan) == ("root-hk",)
    assert plan.selected_project_nodes == ()
    assert plan.selected_release_units == ()
    assert plan.selected_variants == ()
    assert root.lane_id == "root-hk"
    assert root.disposition == "satisfied"
    assert root.evidence is not None
    assert root.evidence.evidence_id == plan.expected_evidence_ids[0]
    assert tuple(result.lane_id for result in empty) == CI_LANE_IDS[1:]
    assert all(
        result.plan_digest == plan_digest
        and result.disposition == "empty"
        and result.evidence is None
        for result in empty
    )
    assert tuple(item.outcome for item in decision.obligation_dispositions) == (
        "satisfied",
        "empty",
        "empty",
        "empty",
    )
    assert decision.terminal_result == "success"
    assert tuple(
        item.outcome for item in incomplete.obligation_dispositions
    ) == ("incomplete", "empty", "empty", "empty")
    assert incomplete.terminal_result == "incomplete"


def test_ci_scenario_missing_comparison_blocks_without_full_fallback() -> None:
    """Exercise literal scenario 5 through empties and Finalizer failure."""
    cases = (
        ("missing", None, "unavailable"),
        ("unavailable", (), "unavailable"),
        ("conflicting", (SHA_D, SHA_B), "conflicts"),
    )

    for name, comparison_identity, diagnostic in cases:
        plan = _incremental_plan(
            comparison_identity=comparison_identity,
        )
        joined_diagnostics = " ".join(plan.diagnostics)
        lane_results = tuple(
            form_empty_lane_result(plan, lane_id=lane_id)
            for lane_id in CI_LANE_IDS
        )
        decision = _finalize(
            plan,
            lane_results,
            elapsed_seconds=60,
        )

        assert name in {"missing", "unavailable", "conflicting"}
        assert plan.ready is False
        assert plan.scope_mode == "incremental"
        assert plan.changed_paths == (PROJECT_SOURCE,)
        assert plan.candidate.target == SHA_C
        assert _selected_lanes(plan) == ()
        assert not any(
            obligation.required or obligation.selected
            for obligation in plan.obligations
        )
        assert plan.expected_evidence_ids == ()
        assert plan.selected_project_nodes == ()
        assert plan.selected_release_units == ()
        assert plan.selected_variants == ()
        assert diagnostic in joined_diagnostics
        assert "full" not in joined_diagnostics.lower()
        assert "slice-validation" not in joined_diagnostics
        assert tuple(result.lane_id for result in lane_results) == CI_LANE_IDS
        assert all(
            result.disposition == "empty" and result.evidence is None
            for result in lane_results
        )
        assert tuple(
            disposition.outcome
            for disposition in decision.obligation_dispositions
        ) == ("empty", "empty", "empty", "empty")
        assert decision.admitted_evidence_digests == ()
        assert decision.terminal_result == "failure"
        assert decision.summary.terminal_result == "failure"
        assert "Plan was not ready" in decision.summary.text


def test_ci_scenario_coexistence_emits_no_authoritative_decision() -> None:
    """Exercise literal LLD CI scenario 6 for both shadow event modes."""
    workflow = _workflow()
    text = WORKFLOW_PATH.read_text(encoding="utf-8")
    events = cast("dict[str, Any]", workflow[True])
    jobs = cast("dict[str, dict[str, Any]]", workflow["jobs"])
    governance = cast(
        "dict[str, Any]",
        json.loads(DISABLED_GOVERNANCE_FIXTURE.read_bytes()),
    )
    event_plans = (
        ("pull_request", "ci-pr-slice-shadow", _incremental_plan()),
        ("workflow_dispatch", "slice-validation", _manual_plan()),
    )

    for event_kind, purpose, plan in event_plans:
        decision = _finalize(
            plan,
            _lane_results(plan),
            elapsed_seconds=60,
        )
        rendered = render_ci_slice_summary(decision)

        assert plan.candidate.event_kind == event_kind
        assert plan.candidate.purpose == purpose
        assert decision.authority == "non-authoritative"
        assert decision.summary.authority == "non-authoritative"
        assert decision.producer == "required-finalizer"
        assert rendered == decision.summary.text
        assert "non-authoritative" in rendered
        assert decision.terminal_result == "success"

    assert events == {"pull_request": None, "workflow_dispatch": None}
    assert tuple(jobs) == (
        "request",
        "discover-node",
        "plan",
        "root-hk",
        "project-build",
        "project-test",
        "npm-artifact-build",
        "required-finalizer",
    )
    assert all(
        fragment not in job_id.lower()
        for job_id in jobs
        for fragment in ("decision", "advisory", "ruleset", "activation")
    )
    assert all(
        fragment not in text.lower()
        for fragment in ("ruleset", "required-check", "activation", "advisory")
    )
    assert "Final Decision" not in text
    assert "authoritative" not in text.lower().replace(
        "non-authoritative",
        "",
    )
    ci_bytes = V1_CI_PATH.read_bytes()
    pinned_node = b"          node-version: '24.14.0'\n"
    capture_step = b"""\
      - name: Capture setup tool paths
        shell: bash
        run: |
          set -Eeuo pipefail

          dotnet_path="$(command -v dotnet)"
          go_path="$(command -v go)"
          java_path="$(command -v java)"
          node_path="$(command -v node)"
          powershell_path="$(command -v pwsh)"
          python_path="$(command -v python3)"
          ruby_path="$(command -v ruby)"

          {
            printf 'MISE_LINK_DOTNET=%s\\n' "$dotnet_path"
            printf 'MISE_LINK_GO=%s\\n' "$go_path"
            printf 'MISE_LINK_JAVA=%s\\n' "$java_path"
            printf 'MISE_LINK_NODE=%s\\n' "$node_path"
            printf 'MISE_LINK_POWERSHELL=%s\\n' "$powershell_path"
            printf 'MISE_LINK_PYTHON=%s\\n' "$python_path"
            printf 'MISE_LINK_RUBY=%s\\n' "$ruby_path"
          } >> "$GITHUB_ENV"

"""
    base_links = b"""\
          mise link core:dotnet@10 "$(which dotnet)"
          mise link go@1 "$(which go)"
          mise link java@25 "$(which java)"
          mise link node@24 "$(which node)"
          mise link powershell@7 "$(which pwsh)"
          mise link python@3.14 "$(which python3)"
          mise link ruby@3.3 "$(which ruby)"
"""
    forced_links = b"""\
          mise link --force core:dotnet@10 "$MISE_LINK_DOTNET"
          mise link --force go@1 "$MISE_LINK_GO"
          mise link --force java@25 "$MISE_LINK_JAVA"
          mise link --force node@24 "$MISE_LINK_NODE"
          mise link --force powershell@7 "$MISE_LINK_POWERSHELL"
          mise link --force python@3.14 "$MISE_LINK_PYTHON"
          mise link --force ruby@3.3 "$MISE_LINK_RUBY"
"""
    assert ci_bytes.count(pinned_node) == 1
    assert ci_bytes.count(capture_step) == 1
    assert ci_bytes.count(forced_links) == 1
    reconstructed_base = (
        ci_bytes.replace(
            pinned_node,
            b"          node-version: 24\n",
            1,
        )
        .replace(
            forced_links,
            base_links,
            1,
        )
        .replace(
            capture_step,
            b"",
            1,
        )
    )
    assert hashlib.sha256(reconstructed_base).hexdigest() == (
        "a0ca041623f8f90771a35c25bc14ceeb25810111c50dfcb17b6e34d988f62fca"
    )
    assert governance["live_enabled"] is False
    assert hashlib.sha256(
        DISABLED_GOVERNANCE_FIXTURE.read_bytes(),
    ).hexdigest() == (
        "54220cc9c45aee4ba6ee66ad8571b8284d23f8496ded1644e256af0935cb8bcb"
    )
    assert "live_enabled" not in text


def test_ci_scenario_policy_only_selects_control_pytest_not_unrelated_source(
    tmp_path: Path,
) -> None:
    """Exercise literal LLD CI scenario 7 with real Git and HK plans."""
    policy_repo = tmp_path / "policy-repo"
    policy_base = _initialize_hk_repository(policy_repo)
    _write(policy_repo, POLICY_PATH, "policy\n")
    policy_head = _commit(policy_repo, "policy-only change")
    policy_paths = _changed_paths(policy_repo, policy_base, policy_head)
    policy_control = _hk_step_for_range(
        policy_repo,
        policy_base,
        policy_head,
        CONTROL_STEP_NAME,
    )
    policy_consumer = _hk_step_for_range(
        policy_repo,
        policy_base,
        policy_head,
        CONSUMER_STEP_NAME,
    )

    product_repo = tmp_path / "unrelated-product-repo"
    product_base = _initialize_hk_repository(product_repo)
    _write(
        product_repo,
        UNRELATED_PRODUCT_SOURCE,
        "export const value = 1;\n",
    )
    product_head = _commit(product_repo, "unrelated product source")
    product_paths = _changed_paths(product_repo, product_base, product_head)
    product_control = _hk_step_for_range(
        product_repo,
        product_base,
        product_head,
        CONTROL_STEP_NAME,
    )

    assert policy_paths == (POLICY_PATH,)
    assert policy_control["name"] == CONTROL_STEP_NAME
    assert policy_control["status"] == "included"
    assert policy_control["fileCount"] == 1
    assert policy_consumer["name"] == CONSUMER_STEP_NAME
    assert policy_consumer["status"] == "skipped"
    assert policy_consumer["fileCount"] == 0
    assert product_paths == (UNRELATED_PRODUCT_SOURCE,)
    assert product_control["name"] == CONTROL_STEP_NAME
    assert product_control["status"] == "skipped"
    assert product_control["fileCount"] == 0


def test_ci_scenario_consumer_policy_trigger_coverage(
    tmp_path: Path,
) -> None:
    """Exercise literal LLD CI scenario 8 across every category/history form."""
    categories = tuple(rule.category for rule in DEPENDENCY_SURFACE_CATALOG)

    assert categories == tuple(case[0] for case in SURFACE_CASES)
    assert categories == (
        "dependency-manifest",
        "lockfile",
        "workflow",
        "composite-action",
        "install-bootstrap-script",
        "dependency-configuration",
    )
    assert GIT_TRANSITIONS == (
        "add",
        "modify",
        "delete",
        "rename-out",
        "rename-in",
    )

    for category, surface_path, _content in SURFACE_CASES:
        for transition in GIT_TRANSITIONS:
            change = _history_change(category, surface_path, transition)
            baseline_paths = (
                (change.old_path or change.path,)
                if change.kind in {"modify", "delete", "rename"}
                else ()
            )
            repo = tmp_path / f"{category}-{transition}"
            base = _initialize_hk_repository(
                repo,
                baseline_paths=baseline_paths,
            )
            _apply_history_change(repo, change)
            head = _commit(repo, f"{category} {transition}")
            paths = _changed_paths(repo, base, head)
            step = _hk_step_for_range(
                repo,
                base,
                head,
                CONSUMER_STEP_NAME,
            )
            expected_paths = (
                (cast("str", change.old_path), change.path)
                if change.kind == "rename"
                else (change.path,)
            )

            assert paths == expected_paths
            assert step["name"] == CONSUMER_STEP_NAME
            assert step["status"] == "included"
            assert step["fileCount"] == 1

    manual_repo = tmp_path / "slice-validation"
    _initialize_hk_repository(manual_repo)
    manual = _hk_step_for_all(manual_repo, CONSUMER_STEP_NAME)

    assert manual["name"] == CONSUMER_STEP_NAME
    assert manual["status"] == "included"
    assert cast("int", manual["fileCount"]) > 0


def test_ci_scenario_consumer_reference_blocks_except_acceptance_fixtures(  # noqa: PLR0915
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Exercise literal LLD CI scenario 9 and all exact exceptions."""
    expected_categories = tuple(case[0] for case in SURFACE_CASES)
    hk_config = HK_CONFIG.read_text(encoding="utf-8")
    step_start = hk_config.index(f'["{CONSUMER_STEP_NAME}"]')
    step_end = hk_config.index("\n  }", step_start)
    step_config = hk_config[step_start:step_end]

    assert expected_categories == (
        "dependency-manifest",
        "lockfile",
        "workflow",
        "composite-action",
        "install-bootstrap-script",
        "dependency-configuration",
    )
    assert (
        "workflow_delivery_v3_consumer_policy.py --repository-root ."
        in step_config
    )

    for category, path, content in SURFACE_CASES:
        repository, target = _initialize_policy_repository(
            tmp_path / f"ordinary-{category}",
        )
        _write(repository, path, content)
        result = scan_consumer_policy(repository)
        return_code = consumer_policy_main(
            ["--repository-root", str(repository)],
        )
        captured = capsys.readouterr()
        rule = classify_dependency_surface(path)

        assert rule is not None
        assert rule.category == category
        assert result.target == target
        assert result.consumers == (path,)
        assert path in {surface.path for surface in result.scanned_surfaces}
        assert tuple(
            surface.path for surface in result.admitted_exceptions
        ) == tuple(
            sorted(
                (
                    OWN_DECLARATION_PATH,
                    ACCEPTANCE_FIXTURE_PATH,
                    ACCEPTANCE_NPM_MANIFEST_PATH,
                )
            )
        )
        assert return_code == 1
        assert captured.err == ""
        assert json.loads(captured.out)["consumers"] == [path]

    approved_repo, approved_target = _initialize_policy_repository(
        tmp_path / "approved-exceptions",
    )
    approved = scan_consumer_policy(approved_repo)
    approved_return_code = consumer_policy_main(
        ["--repository-root", str(approved_repo)],
    )
    approved_output = capsys.readouterr()
    expected_exceptions = tuple(
        sorted(APPROVED_CONSUMER_EXCEPTIONS, key=lambda item: item.path),
    )

    assert approved.target == approved_target
    assert approved.consumers == ()
    assert approved_return_code == 0
    assert approved_output.err == ""
    assert json.loads(approved_output.out)["consumers"] == []
    assert tuple(
        (
            exception.path,
            exception.category,
            exception.context,
            exception.content_digest,
        )
        for exception in APPROVED_CONSUMER_EXCEPTIONS
    ) == (
        (
            OWN_DECLARATION_PATH,
            "dependency-manifest",
            "name",
            (
                "sha256:"
                "a7d84bac91fe5f9fa7ccfbf46cd065cd85ded95188046d96f6f2c9ce97775566"
            ),
        ),
        (
            ACCEPTANCE_FIXTURE_PATH,
            "dependency-manifest",
            f"dependencies.{PACKAGE_NAME}",
            (
                "sha256:"
                "a28d7f1e161df6948cdc2f122e78b9a38f425b481877178e29c8cd8ef30b0aa2"
            ),
        ),
        (
            ACCEPTANCE_NPM_MANIFEST_PATH,
            "dependency-manifest",
            "name",
            (
                "sha256:"
                "d032b543a77820f9660a629e7deee6140664150a2c0a7de8048d37947afc957e"
            ),
        ),
    )
    assert tuple(
        (surface.path, surface.content_digest)
        for surface in approved.admitted_exceptions
    ) == tuple(
        (exception.path, exception.content_digest)
        for exception in expected_exceptions
    )

    for mutation in (
        "path",
        "context",
        "digest",
        "own-declaration-digest",
    ):
        repository, _target = _initialize_policy_repository(
            tmp_path / f"mutated-{mutation}",
        )
        expected_path = ACCEPTANCE_FIXTURE_PATH
        fixture = repository / ACCEPTANCE_FIXTURE_PATH
        if mutation == "path":
            fixture.rename(
                fixture.with_name("consumer-policy-acceptance-moved.json"),
            )
            with pytest.raises(
                ValueError,
                match="approved consumer-policy exception is missing",
            ):
                scan_consumer_policy(repository)
        elif mutation == "context":
            document = json.loads(fixture.read_text(encoding="utf-8"))
            document["devDependencies"] = document.pop("dependencies")
            fixture.write_text(json.dumps(document), encoding="utf-8")
        elif mutation == "own-declaration-digest":
            expected_path = OWN_DECLARATION_PATH
            own_declaration = repository / OWN_DECLARATION_PATH
            own_declaration.write_bytes(own_declaration.read_bytes() + b"\n")
        else:
            fixture.write_bytes(fixture.read_bytes() + b"\n")

        return_code = consumer_policy_main(
            ["--repository-root", str(repository)],
        )
        captured = capsys.readouterr()

        if mutation == "path":
            assert return_code == SCAN_ERROR_EXIT_CODE
            assert captured.out == ""
            assert "approved consumer-policy exception is missing" in (
                captured.err
            )
        else:
            reopened = scan_consumer_policy(repository)
            assert return_code == 1
            assert captured.err == ""
            assert json.loads(captured.out)["consumers"] == [expected_path]
            assert reopened.consumers == (expected_path,)
            assert expected_path not in {
                surface.path for surface in reopened.admitted_exceptions
            }


def test_completed_failure_is_failure_not_incomplete() -> None:
    """Additively pin scenario 3 as a completed failed project-test command."""
    plan = _incremental_plan()
    completed_failure_results = _lane_results(
        plan,
        outcomes={"project-test": "failure"},
        diagnostics={"project-test": ("completed npm test command failed",)},
    )
    missing_results = tuple(
        result
        for result in completed_failure_results
        if result.lane_id != "project-test"
    )
    completed_decision = _finalize(
        plan,
        completed_failure_results,
        elapsed_seconds=60,
    )
    missing_decision = _finalize(
        plan,
        missing_results,
        elapsed_seconds=60,
    )
    completed_project_test = next(
        result
        for result in completed_failure_results
        if result.lane_id == "project-test"
    )

    assert completed_project_test.disposition == "failed"
    assert completed_project_test.evidence is not None
    assert completed_project_test.evidence.raw_outcome == "failure"
    assert completed_project_test.evidence.normalized_outcome == "failed"
    assert completed_decision.terminal_result == "failure"
    assert {
        item.obligation.lane_id: item.outcome
        for item in completed_decision.obligation_dispositions
    }["project-test"] == "failed"
    assert missing_decision.terminal_result == "incomplete"


def test_runtime_infrastructure_paths_are_missing_result_incomplete() -> None:
    """Close timeout/cancellation/infrastructure/no-result as incomplete."""
    plan = _incremental_plan()
    complete_results = _lane_results(plan)

    for reason, lane_id in (
        ("timeout", "project-build"),
        ("cancellation", "project-test"),
        ("infrastructure", "npm-artifact-build"),
        ("no-result", "project-test"),
    ):
        missing_lane_results = tuple(
            result for result in complete_results if result.lane_id != lane_id
        )
        decision = _finalize(
            plan,
            missing_lane_results,
            elapsed_seconds=60,
        )

        assert reason in {
            "timeout",
            "cancellation",
            "infrastructure",
            "no-result",
        }
        assert decision.terminal_result == "incomplete"
        assert {
            item.obligation.lane_id: item.outcome
            for item in decision.obligation_dispositions
        }[lane_id] == "incomplete"

    completed_failure = _finalize(
        plan,
        _lane_results(
            plan,
            outcomes={"project-test": "failure"},
            diagnostics={
                "project-test": (
                    "verified completed project-test Adapter command failure",
                ),
            },
        ),
        elapsed_seconds=60,
    )

    assert completed_failure.terminal_result == "failure"
    assert {
        item.obligation.lane_id: item.outcome
        for item in completed_failure.obligation_dispositions
    }["project-test"] == "failed"


def test_ci_scenario_precoexistence_bootstrap_preserves_blocked_decision() -> (
    None
):
    """Project only the first broad implementation PR without changing truth."""
    changed_paths = tuple(
        f"unmodeled/bootstrap/path-{index:03}.txt" for index in range(283)
    )
    plan = _incremental_plan(changed_paths=changed_paths)
    decision = _finalize(
        plan,
        _lane_results(plan),
        elapsed_seconds=60,
    )

    assert plan.ready is False
    assert len(plan.diagnostics) == len(changed_paths)
    assert all(
        diagnostic == f"changed path is unclassified: {path}"
        for diagnostic, path in zip(
            plan.diagnostics,
            changed_paths,
            strict=True,
        )
    )
    assert decision.terminal_result == "failure"
    assert decision.failure_class == "incomplete-model-plan"
    assert decision.next_action == "fix-model-plan-and-rerun"
    assert all(
        not item.obligation.selected
        and item.outcome == "empty"
        and not item.evidence_digests
        for item in decision.obligation_dispositions
    )
    assert qualifies_precoexistence_bootstrap_projection(
        decision,
        request=_bootstrap_request(),
        base_contains_ci_workflow=False,
    )
    assert not qualifies_precoexistence_bootstrap_projection(
        decision,
        request=_bootstrap_request(),
        base_contains_ci_workflow=True,
    )


@pytest.mark.parametrize(
    (
        "pull_request_number",
        "event_base_sha",
        "event_head_sha",
        "event_tested_merge_sha",
    ),
    [
        (43, SHA_A, SHA_B, SHA_C),
        (42, SHA_D, SHA_B, SHA_C),
        (42, SHA_A, SHA_D, SHA_C),
        (42, SHA_A, SHA_B, SHA_D),
    ],
)
def test_precoexistence_bootstrap_projection_rejects_identity_drift(
    pull_request_number: int,
    event_base_sha: str,
    event_head_sha: str,
    event_tested_merge_sha: str,
) -> None:
    """Bind bootstrap status to the exact pull-request candidate identity."""
    plan = _incremental_plan(changed_paths=("unmodeled/bootstrap.txt",))
    decision = _finalize(plan, _lane_results(plan), elapsed_seconds=60)

    assert not qualifies_precoexistence_bootstrap_projection(
        decision,
        request=_bootstrap_request(
            pull_request_number=pull_request_number,
            base_sha=event_base_sha,
            head_sha=event_head_sha,
            tested_merge_sha=event_tested_merge_sha,
        ),
        base_contains_ci_workflow=False,
    )


def test_bootstrap_projection_allows_unavailable_platform_proof() -> None:
    """Use exact event identity when the platform proof is unavailable."""
    plan = _incremental_plan(changed_paths=("unmodeled/bootstrap.txt",))
    decision = finalize_ci_slice(
        plan,
        _lane_results(plan),
        elapsed_seconds=60,
        supersession_state="unsupported",
    )

    assert decision.supersession_reason == "platform-proof-unavailable"
    assert qualifies_precoexistence_bootstrap_projection(
        decision,
        request=_bootstrap_request(),
        base_contains_ci_workflow=False,
    )


def test_precoexistence_bootstrap_projection_rejects_other_failures() -> None:
    """Keep manual, lane, supersession, and mixed-diagnostic failures red."""
    blocked = _incremental_plan(
        changed_paths=("unmodeled/bootstrap.txt",),
    )
    mixed = plan_ci_qualification(
        blocked.candidate,
        _repository_model(blocked.candidate),
        repository_model_digest=_repository_model(
            blocked.candidate
        ).snapshot_digest,
        changed_paths=("unmodeled/bootstrap.txt",),
        comparison_identity=(SHA_A, SHA_B),
        diagnostics=("repository model comparison is incomplete",),
    )
    mismatched_path = replace(
        blocked,
        diagnostics=(
            "changed path is unclassified: unmodeled/not-the-change.txt",
        ),
    )
    project_plan = _incremental_plan()
    decisions = (
        _finalize(
            _manual_plan(),
            _lane_results(_manual_plan()),
            elapsed_seconds=60,
        ),
        _finalize(
            project_plan,
            _lane_results(
                project_plan,
                outcomes={"project-test": "failure"},
            ),
            elapsed_seconds=60,
        ),
        finalize_ci_slice(
            blocked,
            _lane_results(blocked),
            elapsed_seconds=60,
            supersession_state="superseded",
        ),
        _finalize(mixed, _lane_results(mixed), elapsed_seconds=60),
        _finalize(
            mismatched_path,
            _lane_results(mismatched_path),
            elapsed_seconds=60,
        ),
    )

    assert all(
        not qualifies_precoexistence_bootstrap_projection(
            decision,
            request=_bootstrap_request(),
            base_contains_ci_workflow=False,
        )
        for decision in decisions
    )


def test_reserved_ci_scenario_inventory_is_exact() -> None:
    """Keep the reserved scenario prefix inventory at the approved ten."""
    reserved = tuple(
        name
        for name, value in globals().items()
        if name.startswith("test_ci_scenario_") and callable(value)
    )

    assert reserved == (
        "test_ci_scenario_project_source_change_selects_complete_slice",
        "test_ci_scenario_slice_validation_selects_full_slice_without_synthetic_range",
        "test_ci_scenario_project_test_failure_fails_shadow_check",
        "test_ci_scenario_repository_only_change_has_valid_empty_affected_lanes",
        "test_ci_scenario_missing_comparison_blocks_without_full_fallback",
        "test_ci_scenario_coexistence_emits_no_authoritative_decision",
        "test_ci_scenario_policy_only_selects_control_pytest_not_unrelated_source",
        "test_ci_scenario_consumer_policy_trigger_coverage",
        "test_ci_scenario_consumer_reference_blocks_except_acceptance_fixtures",
        "test_ci_scenario_precoexistence_bootstrap_preserves_blocked_decision",
    )
