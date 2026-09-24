"""Hosted workflow evidence ordering and authority boundaries."""

import re
from pathlib import Path

import pytest
import yaml

_ROOT = Path(__file__).resolve().parents[5]
_WORKFLOW = _ROOT / ".github/workflows/workflow-delivery-v3-python-smoke.yml"
_RECORD = (
    _ROOT / ".github/actions/workflow-delivery-v3-python-record/action.yml"
)
_SETUP = _ROOT / ".github/actions/workflow-delivery-v3-python-setup/action.yml"
_RECORD_ACTION = "./.github/actions/workflow-delivery-v3-python-record"


@pytest.fixture
def workflow():
    """Read the exact hosted definition under review."""
    return yaml.safe_load(_WORKFLOW.read_text())


def _stage(steps, command):
    matches = [
        i
        for i, step in enumerate(steps)
        if f"python_cli {command} " in step.get("run", "")
    ]
    assert len(matches) == 1
    return matches[0]


def _persist(steps, role):
    matches = [
        i
        for i, step in enumerate(steps)
        if step.get("uses") == _RECORD_ACTION and step["with"]["role"] == role
    ]
    assert len(matches) == 1
    return matches[0]


def test_python_workflow_scopes_oidc_to_environment_gated_publisher(workflow):
    """Only the action-bearing publisher gets OIDC and an Environment."""
    assert workflow["permissions"] == {}
    jobs = workflow["jobs"]
    assert {
        name
        for name, job in jobs.items()
        if job["permissions"].get("id-token") == "write"
    } == {"publish-python"}
    assert {name for name, job in jobs.items() if "environment" in job} == {
        "publish-python"
    }
    publisher = jobs["publish-python"]
    assert (
        publisher["environment"]["name"]
        == "${{ needs.prepare-python-publication.outputs.environment }}"
    )
    assert "workflow_dispatch" in publisher["if"]
    assert "action-required == 'true'" in " ".join(publisher["if"].split())
    assert all(
        job["permissions"].get("contents") == "read" for job in jobs.values()
    )
    assert all("packages" not in job["permissions"] for job in jobs.values())


def test_python_workflow_orders_durable_authority_before_credentials_and_upload(
    workflow,
):
    """One publisher validates Authorization and marker before their effects."""
    steps = workflow["jobs"]["publish-python"]["steps"]
    assert (
        _stage(steps, "authorize")
        < _persist(steps, "authorization")
        < _stage(steps, "token")
    )
    assert (
        _stage(steps, "token")
        < _stage(steps, "marker")
        < _persist(steps, "marker")
        < _stage(steps, "execute")
    )
    assert (
        _stage(steps, "execute")
        < _persist(steps, "result")
        < _stage(steps, "terminal")
    )
    for command in ("authorize", "token", "marker", "execute"):
        step = steps[_stage(steps, command)]
        assert not step.get("continue-on-error", False)
        assert "if" not in step
    for role in ("authorization", "marker", "result"):
        step = steps[_persist(steps, role)]
        assert not step.get("continue-on-error", False)
        assert "if" not in step
    assert steps[_stage(steps, "terminal")]["if"] == "always()"
    assert (
        workflow["jobs"]["publish-python"]["outputs"][
            "publication-step-outcome"
        ]
        == "${{ steps.execute.outcome }}"
    )
    assert (
        workflow["jobs"]["publish-python"]["outputs"]["terminal-reference"]
        == "${{ steps.terminal.outputs.terminal-reference }}"
    )


def test_python_record_action_binds_exact_downloaded_upload_before_export():
    """Immutable non-archive upload is downloaded by ID before role binding."""
    document = yaml.safe_load(_RECORD.read_text())
    steps = document["runs"]["steps"]
    upload = next(
        i
        for i, s in enumerate(steps)
        if s.get("uses", "").startswith("actions/upload-artifact@")
    )
    download = next(
        i
        for i, s in enumerate(steps)
        if s.get("uses", "").startswith("actions/download-artifact@")
    )
    bind = _stage(steps, "bind")
    assert upload < download < bind
    # Default success() prevents failed persistence from exporting a role.
    for step in steps:
        assert "if" not in step
        assert not step.get("continue-on-error", False)
    options = steps[upload]["with"]
    assert options["overwrite"] is False
    assert options["archive"] is False
    assert options["if-no-files-found"] == "error"
    downloaded = steps[download]["with"]
    assert (
        downloaded["artifact-ids"] == "${{ steps.upload.outputs.artifact-id }}"
    )
    assert downloaded["skip-decompress"] is True
    assert downloaded["digest-mismatch"] == "error"
    assert '--payload ".wdv3/readback/' in steps[bind]["run"]
    assert (
        steps[bind]["env"]["RECORD_DIGEST"]
        == "${{ steps.upload.outputs.artifact-digest }}"
    )
    assert (
        steps[bind]["env"]["RECORD_ID"]
        == "${{ steps.upload.outputs.artifact-id }}"
    )
    assert (
        steps[-1]["env"]["NEXT_REFERENCES"]
        == "${{ steps.bind.outputs.references }}"
    )
    assert "GITHUB_ENV" in steps[-1]["run"]


def test_python_workflow_uses_exact_target_full_history_and_immutable_actions(
    workflow,
):
    """Every stage admits one exact checkout and pinned external actions."""
    for job in workflow["jobs"].values():
        checkouts = [
            s
            for s in job["steps"]
            if s.get("uses", "").startswith("actions/checkout@")
        ]
        assert len(checkouts) == 1
        options = checkouts[0]["with"]
        assert options["ref"] == "${{ github.sha }}"
        assert options["fetch-depth"] == 0
        assert options["persist-credentials"] is False
        assert job["timeout-minutes"] > 0
    documents = [
        workflow,
        yaml.safe_load(_RECORD.read_text()),
        yaml.safe_load(_SETUP.read_text()),
    ]
    steps = [s for job in documents[0]["jobs"].values() for s in job["steps"]]
    steps += [s for doc in documents[1:] for s in doc["runs"]["steps"]]
    for step in steps:
        action = step.get("uses", "")
        if action and not action.startswith("./"):
            assert re.fullmatch(r"[^@]+@[a-f0-9]{40}", action)


def test_python_workflow_transports_only_explicit_current_dag_artifact_ids(
    workflow,
):
    """Explicit IDs prevent name searches from selecting foreign outputs."""
    for name, job in workflow["jobs"].items():
        assert (
            job["outputs"]["references"]
            == "${{ steps.edges.outputs.references }}"
        )
        assert (
            job["outputs"]["artifact-ids"]
            == "${{ steps.edges.outputs.artifact-ids }}"
        )
        edges = job["steps"][_stage(job["steps"], "export")]
        assert edges["if"] == "always()"
        if name == "request-python":
            assert job["env"]["WDV3_REFERENCES"] == "{}"
        else:
            assert "needs." in job["env"]["WDV3_REFERENCES"]
        for step in job["steps"]:
            if step.get("uses", "").startswith("actions/download-artifact@"):
                options = step["with"]
                assert "needs." in options["artifact-ids"]
                assert "name" not in options
                assert "pattern" not in options
                assert options["skip-decompress"] is True
                assert options["digest-mismatch"] == "error"


def test_python_workflow_finalizer_retains_cancellation_and_scalar_contract(
    workflow,
):
    """Finalization uses direct platform facts and one terminal scalar."""
    finalizer = workflow["jobs"]["finalize-attempt"]
    assert "always()" in finalizer["if"]
    assert "needs.qualify-python.result == 'success'" in finalizer["if"]
    env = finalizer["env"]
    assert env["PUBLISHER_CONCLUSION"] == "${{ needs.publish-python.result }}"
    assert (
        env["PUBLICATION_STEP_OUTCOME"]
        == "${{ needs.publish-python.outputs.publication-step-outcome }}"
    )
    assert (
        env["TERMINAL_REFERENCE"]
        == "${{ needs.publish-python.outputs.terminal-reference }}"
    )
    steps = finalizer["steps"]
    assert _stage(steps, "finalize") < _persist(steps, "outcome")
    assert steps[_stage(steps, "finalize")]["continue-on-error"] is True
    assert (
        workflow["concurrency"]["cancel-in-progress"]
        == "${{ github.event_name == 'pull_request' }}"
    )
    assert "inputs.registry" in workflow["concurrency"]["group"]


def test_python_workflow_zero_action_skips_bundle_and_environment_authority(
    workflow,
):
    """Fresh exact proof is independent from approved mutation authority."""
    steps = workflow["jobs"]["prepare-python-publication"]["steps"]
    for command in ("summary", "bundle"):
        assert (
            steps[_stage(steps, command)]["if"]
            == "steps.prepare.outputs.action-required == 'true'"
        )
    for role in ("summary", "bundle"):
        assert (
            steps[_persist(steps, role)]["if"]
            == "steps.prepare.outputs.action-required == 'true'"
        )
    assert all(
        "python_cli exact-proof " not in step.get("run", "") for step in steps
    )
    final_steps = workflow["jobs"]["finalize-attempt"]["steps"]
    proof = _stage(final_steps, "exact-proof")
    persisted = _persist(final_steps, "exact-proof")
    assert proof < persisted < _stage(final_steps, "finalize")
    for step in (final_steps[proof], final_steps[persisted]):
        condition = " ".join(step["if"].split())
        assert "action-required == 'false'" in condition
        assert "needs.publish-python.result == 'skipped'" in condition


@pytest.mark.parametrize("failure_point", ["generation", "persistence"])
def test_python_workflow_exact_proof_failure_preserves_outcome_attempt(
    workflow, failure_point
):
    """Optional proof failure cannot suppress persisting an unknown Outcome."""
    job = workflow["jobs"]["finalize-attempt"]
    steps = job["steps"]
    generate = _stage(steps, "exact-proof")
    persist = _persist(steps, "exact-proof")
    finalize = _stage(steps, "finalize")
    outcome = _persist(steps, "outcome")
    failed = generate if failure_point == "generation" else persist
    assert steps[failed]["continue-on-error"] is True
    assert generate < persist < finalize < outcome
    condition = " ".join(steps[persist]["if"].split())
    assert condition == (
        "needs.prepare-python-publication.outputs.action-required == 'false'"
        " && needs.publish-python.result == 'skipped'"
        " && steps.exact-proof.outcome == 'success'"
    )
    # Tolerated proof failure leaves ordinary success gating open, while
    # setup/download failure still prevents admission of missing inputs.
    for step in steps[:generate]:
        assert not step.get("continue-on-error", False)
    assert "if" not in steps[finalize]
    assert steps[finalize]["continue-on-error"] is True
    assert "if" not in steps[outcome]
    assert not steps[outcome].get("continue-on-error", False)
    stop = next(
        i
        for i, step in enumerate(steps)
        if step.get("run") == 'test "${FINALIZER_OUTCOME}" = success'
    )
    assert outcome < stop
    assert steps[stop]["env"]["FINALIZER_OUTCOME"] == (
        "${{ steps.finalize.outcome }}"
    )
    assert not steps[stop].get("continue-on-error", False)
    assert job["permissions"] == {"contents": "read"}
    assert "environment" not in job


def test_python_workflow_persists_failed_qualification_before_stopping(
    workflow,
):
    """Failed Qualification remains terminal authority without Outcome."""
    qualification = workflow["jobs"]["qualify-python"]
    steps = qualification["steps"]
    decision = _stage(steps, "decision")
    persisted = _persist(steps, "decision")
    stop = next(
        i
        for i, step in enumerate(steps)
        if step.get("run") == 'test "${DECISION}" = passed'
    )
    assert steps[decision]["continue-on-error"] is True
    assert "if" not in steps[persisted]
    assert decision < persisted < stop
    assert (
        "needs.qualify-python.result == 'success'"
        in workflow["jobs"]["finalize-attempt"]["if"]
    )
    assert steps[_stage(steps, "export")]["if"] == "always()"


def test_python_workflow_build_failure_can_reach_qualification_with_valid_plan(
    workflow,
):
    """Build failure keeps the Decision path open only after valid planning."""
    job = workflow["jobs"]["qualify-python"]
    assert set(job["needs"]) == {
        "build-python",
        "plan-python-ci",
        "plan-python-release",
    }
    assert " ".join(job["if"].split()) == (
        "always() && (needs.plan-python-ci.result == 'success'"
        " || needs.plan-python-release.result == 'success')"
    )
    steps = job["steps"]
    artifacts = _stage(steps, "artifacts")
    assert steps[artifacts]["if"] == "needs.build-python.result == 'success'"
    for step in steps[:artifacts]:
        assert not step.get("continue-on-error", False)
        assert "if" not in step
    decision = steps[_stage(steps, "decision")]
    assert "if" not in decision
    assert decision["continue-on-error"] is True
    prepare = workflow["jobs"]["prepare-python-publication"]
    assert "qualify-python" in prepare["needs"]
    assert prepare["if"] == "github.event_name == 'workflow_dispatch'"


@pytest.mark.parametrize("output", ["references", "artifact-ids"])
def test_python_workflow_missing_build_outputs_use_paired_plan_fallback(
    workflow, output
):
    """Half an export cannot mix Build references with another set of IDs."""
    job = workflow["jobs"]["qualify-python"]
    if output == "references":
        expression = job["env"]["WDV3_REFERENCES"]
    else:
        download = next(
            step
            for step in job["steps"]
            if step.get("uses", "").startswith("actions/download-artifact@")
        )
        expression = download["with"]["artifact-ids"]
        assert download["with"]["digest-mismatch"] == "error"
        assert "name" not in download["with"]
        assert "pattern" not in download["with"]
    assert " ".join(expression.split()) == (
        "${{ needs.build-python.outputs.references"
        " && needs.build-python.outputs.artifact-ids"
        f" && needs.build-python.outputs.{output}"
        " || needs.plan-python-ci.result == 'success'"
        f" && needs.plan-python-ci.outputs.{output}"
        f" || needs.plan-python-release.outputs.{output} }}}}"
    )


@pytest.mark.parametrize(
    "failure_point",
    ["artifacts", "artifact-records", "quality", "quality-records"],
)
def test_python_workflow_evidence_failure_leaves_decision_persistence_reachable(
    workflow, failure_point
):
    """Producer or readback failure leaves quality absent and cannot succeed."""
    steps = workflow["jobs"]["qualify-python"]["steps"]
    indices = {
        "artifacts": _stage(steps, "artifacts"),
        "artifact-records": _persist(steps, "artifacts"),
        "quality": _stage(steps, "quality"),
        "quality-records": _persist(steps, "quality"),
    }
    failed = steps[indices[failure_point]]
    assert failed["continue-on-error"] is True
    assert (
        steps[indices["artifact-records"]]["if"]
        == "steps.artifacts.outcome == 'success'"
    )
    assert (
        steps[indices["quality"]]["if"]
        == "steps.artifact-records.outcome == 'success'"
    )
    assert (
        steps[indices["quality-records"]]["if"]
        == "steps.quality.outcome == 'success'"
    )
    assert list(indices.values()) == sorted(indices.values())
    decision = _stage(steps, "decision")
    persist = _persist(steps, "decision")
    assert indices[failure_point] < decision < persist
    assert "if" not in steps[decision]
    assert steps[decision]["continue-on-error"] is True
    assert "if" not in steps[persist]
    assert not steps[persist].get("continue-on-error", False)
    stop = next(
        i
        for i, step in enumerate(steps)
        if step.get("run") == 'test "${DECISION}" = passed'
    )
    assert persist < stop
    assert (
        steps[stop]["env"]["DECISION"]
        == "${{ steps.decision.outputs.qualification-result }}"
    )
    assert not steps[stop].get("continue-on-error", False)
