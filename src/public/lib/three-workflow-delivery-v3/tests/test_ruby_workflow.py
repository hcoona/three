"""Ruby PR CI workflow privileges and immutable record transport."""

import os
import re
import shutil
from pathlib import Path

import pytest
import yaml
from three_workflow_delivery_v3._ruby_native import (
    ruby_executable,
    ruby_profile,
)

_ROOT = Path(__file__).resolve().parents[5]
_WORKFLOW = _ROOT / ".github/workflows/workflow-delivery-v3-ruby-smoke.yml"
_RECORD = _ROOT / ".github/actions/workflow-delivery-v3-ruby-record/action.yml"
_SETUP = _ROOT / ".github/actions/workflow-delivery-v3-ruby-setup/action.yml"
_RECORD_ACTION = "./.github/actions/workflow-delivery-v3-ruby-record"
_SETUP_ACTION = "./.github/actions/workflow-delivery-v3-ruby-setup"


@pytest.fixture
def workflow():
    """Read the exact proposed hosted definition."""
    return yaml.safe_load(_WORKFLOW.read_text())


def _stage(steps, command):
    matches = [
        i
        for i, step in enumerate(steps)
        if f"ruby_cli {command} " in step.get("run", "")
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


def test_ruby_workflow_is_ci_only_without_publication_authority(workflow):
    """PR validation cannot acquire OIDC, package writes or an Environment."""
    assert set(workflow["on"]) == {"pull_request"}
    assert workflow["env"]["WDV3_PURPOSE"] == "ci-pr-slice-shadow"
    assert workflow["permissions"] == {}
    assert set(workflow["jobs"]) == {
        "request-ruby",
        "discover-ruby",
        "compile-ruby",
        "plan-ruby-ci",
        "build-ruby",
        "qualify-ruby",
    }
    for job in workflow["jobs"].values():
        assert "environment" not in job
        assert job["permissions"]["contents"] == "read"
        assert set(job["permissions"]) <= {"contents", "actions"}
        assert "secrets" not in job
        assert all(value == "read" for value in job["permissions"].values())
    assert workflow["concurrency"]["cancel-in-progress"] is True
    assert (
        "github.event.pull_request.number" in workflow["concurrency"]["group"]
    )


def test_ruby_workflow_exact_target_history_timeouts_and_pinned_actions(
    workflow,
):
    """Every stage reads one exact target without retained credentials."""
    for job in workflow["jobs"].values():
        checkouts = [
            step
            for step in job["steps"]
            if step.get("uses", "").startswith("actions/checkout@")
        ]
        assert len(checkouts) == 1
        assert checkouts[0]["with"] == {
            "ref": "${{ github.sha }}",
            "fetch-depth": 0,
            "persist-credentials": False,
        }
        assert 0 < job["timeout-minutes"] <= 30  # noqa: PLR2004 - reviewed CI timeout bound
    steps = [step for job in workflow["jobs"].values() for step in job["steps"]]
    steps += yaml.safe_load(_RECORD.read_text())["runs"]["steps"]
    steps += yaml.safe_load(_SETUP.read_text())["runs"]["steps"]
    for step in steps:
        action = step.get("uses", "")
        if action and not action.startswith("./"):
            assert re.fullmatch(r"[^@]+@[a-f0-9]{40}", action)


def test_ruby_workflow_explicit_ids_and_retained_prior_edges(workflow):
    """Download exact predecessor IDs; artifact names cannot select payloads."""
    for name, job in workflow["jobs"].items():
        assert job["outputs"] == {
            "references": "${{ steps.edges.outputs.references }}",
            "artifact-ids": "${{ steps.edges.outputs.artifact-ids }}",
        }
        export = job["steps"][_stage(job["steps"], "export")]
        assert export["if"] == "always()"
        references = job["env"]["WDV3_REFERENCES"]
        assert (
            references == "{}"
            if name == "request-ruby"
            else "needs." in references
        )
        for step in job["steps"]:
            if step.get("uses", "").startswith("actions/download-artifact@"):
                options = step["with"]
                assert "needs." in options["artifact-ids"]
                assert "name" not in options
                assert "pattern" not in options
                assert options["skip-decompress"] is True
                assert options["digest-mismatch"] == "error"
                assert options["path"] == ".wdv3"


def test_ruby_workflow_orders_build_and_quality_artifacts_before_decision(
    workflow,
):
    """Immutable references exist before the next consumer runs."""
    for job_name, command, role in [
        ("request-ruby", "request", "request"),
        ("discover-ruby", "provider", "provider"),
        ("compile-ruby", "compile", "model"),
        ("plan-ruby-ci", "plan", "ci-plan"),
    ]:
        steps = workflow["jobs"][job_name]["steps"]
        assert (
            _stage(steps, command)
            < _persist(steps, role)
            < _stage(steps, "export")
        )
    build = workflow["jobs"]["build-ruby"]["steps"]
    assert (
        _stage(build, "build")
        < _persist(build, "gem")
        < _persist(build, "build-report")
        < _stage(build, "export")
    )
    steps = workflow["jobs"]["qualify-ruby"]["steps"]
    assert (
        _stage(steps, "artifacts")
        < _persist(steps, "artifacts")
        < _stage(steps, "quality")
        < _persist(steps, "quality")
        < _stage(steps, "decision")
        < _persist(steps, "decision")
    )


def test_ruby_workflow_retains_failure_and_fails_qualification_gate(
    workflow,
):
    """Build loss can form an incomplete Decision but cannot green the job."""
    job = workflow["jobs"]["qualify-ruby"]
    assert "always()" in job["if"]
    assert "needs.plan-ruby-ci.result == 'success'" in job["if"]
    refs = job["env"]["WDV3_REFERENCES"]
    assert "needs.build-ruby.outputs.references" in refs
    assert "needs.build-ruby.outputs.artifact-ids" in refs
    assert "needs.plan-ruby-ci.outputs.references" in refs
    steps = job["steps"]
    assert (
        steps[_stage(steps, "artifacts")]["if"]
        == "needs.build-ruby.result == 'success'"
    )
    assert (
        steps[_persist(steps, "artifacts")]["if"]
        == "steps.artifacts.outcome == 'success'"
    )
    assert (
        steps[_stage(steps, "quality")]["if"]
        == "steps.artifact-records.outcome == 'success'"
    )
    assert (
        steps[_persist(steps, "quality")]["if"]
        == "steps.quality.outcome == 'success'"
    )
    decision = steps[_stage(steps, "decision")]
    assert decision["continue-on-error"] is True
    assert "if" not in decision
    persisted = steps[_persist(steps, "decision")]
    assert not persisted.get("continue-on-error", False)
    guards = [
        i
        for i, step in enumerate(steps)
        if step.get("env", {}).get("DECISION")
        == "${{ steps.decision.outputs.qualification-result }}"
    ]
    assert len(guards) == 1
    gate = steps[guards[0]]
    assert _persist(steps, "decision") < guards[0] < _stage(steps, "export")
    assert 'test "${DECISION}" = passed' in gate["run"]
    assert not gate.get("continue-on-error", False)


def test_ruby_record_action_binds_only_exact_immutable_upload_readback():
    """No overwrite, broad artifact search or binding before readback."""
    action = yaml.safe_load(_RECORD.read_text())
    steps = action["runs"]["steps"]
    upload = next(
        i
        for i, step in enumerate(steps)
        if step.get("uses", "").startswith("actions/upload-artifact@")
    )
    download = next(
        i
        for i, step in enumerate(steps)
        if step.get("uses", "").startswith("actions/download-artifact@")
    )
    bind = _stage(steps, "bind")
    assert upload < download < bind
    for step in steps:
        assert "if" not in step
        assert not step.get("continue-on-error", False)
    uploaded = steps[upload]["with"]
    assert uploaded["archive"] is False
    assert uploaded["overwrite"] is False
    assert uploaded["if-no-files-found"] == "error"
    assert uploaded["path"] == "${{ inputs.path }}"
    downloaded = steps[download]["with"]
    assert (
        downloaded["artifact-ids"] == "${{ steps.upload.outputs.artifact-id }}"
    )
    assert downloaded["path"] == ".wdv3/readback"
    assert downloaded["skip-decompress"] is True
    assert downloaded["digest-mismatch"] == "error"
    assert "name" not in downloaded
    assert "pattern" not in downloaded
    assert '--payload ".wdv3/readback/' in steps[bind]["run"]
    assert steps[bind]["env"] == {
        "RECORD_ROLE": "${{ inputs.role }}",
        "RECORD_PATH": "${{ inputs.path }}",
        "RECORD_ID": "${{ steps.upload.outputs.artifact-id }}",
        "RECORD_DIGEST": "${{ steps.upload.outputs.artifact-digest }}",
        "RECORD_URL": "${{ steps.upload.outputs.artifact-url }}",
    }
    assert (
        steps[-1]["env"]["NEXT_REFERENCES"]
        == "${{ steps.bind.outputs.references }}"
    )
    assert '"${GITHUB_ENV}"' in steps[-1]["run"]
    assert (
        action["outputs"]["references"]["value"]
        == "${{ steps.bind.outputs.references }}"
    )


def test_ruby_setup_locks_control_dependencies_without_project_install():
    """Setup cannot execute smoke installation or retain auth material."""
    steps = yaml.safe_load(_SETUP.read_text())["runs"]["steps"]
    uv = next(
        step
        for step in steps
        if step.get("uses", "").startswith("astral-sh/setup-uv@")
    )
    ruby = next(
        step
        for step in steps
        if step.get("uses", "").startswith("ruby/setup-ruby@")
    )
    assert uv["with"]["version-file"] == ".config/uv/uv.toml"
    assert uv["with"]["github-token"] == ""
    assert ruby["with"]["ruby-version"] == "4.0.7"
    assert ruby["with"]["bundler-cache"] is False
    commands = "\n".join(step.get("run", "") for step in steps)
    assert (
        "uv sync --frozen --package three-workflow-delivery-v3 --no-dev"
        in commands
    )
    assert "GITHUB_PATH" in commands
    assert "gem install" not in commands
    assert "bundle install" not in commands
    assert "GITHUB_TOKEN" not in commands
    assert "ACTIONS_ID_TOKEN" not in commands


def test_ruby_workflow_installs_native_tools_only_for_owning_jobs(workflow):
    """Pure record stages need Python/Git; provider/build/quality own Ruby."""
    setup = yaml.safe_load(_SETUP.read_text())
    ruby = next(
        step
        for step in setup["runs"]["steps"]
        if step.get("uses", "").startswith("ruby/setup-ruby@")
    )
    condition = ruby.get("if", "")
    match = re.search(r"inputs\.([a-z-]+)", condition)
    assert match is not None, (
        "Ruby installation must be explicitly job-selected"
    )
    option = match.group(1)
    assert "== 'true'" in condition
    assert setup["inputs"][option].get("default") in {"false", False}
    enabled = {
        name
        for name, job in workflow["jobs"].items()
        if any(
            step.get("uses") == _SETUP_ACTION
            and step.get("with", {}).get(option) in {"true", True}
            for step in job["steps"]
        )
    }
    assert enabled == {"discover-ruby", "build-ruby", "qualify-ruby"}
    dotnet = {
        name
        for name, job in workflow["jobs"].items()
        if any(
            step.get("uses", "").startswith("actions/setup-dotnet@")
            for step in job["steps"]
        )
    }
    assert dotnet == {"discover-ruby"}


def test_common_python_ci_keeps_native_ruby_ahead_of_late_mise_shim(
    tmp_path, monkeypatch
):
    """Later shim creation cannot replace the installed native interpreter."""
    native = ruby_executable()
    mise = shutil.which("mise")
    assert mise is not None
    steps = yaml.safe_load((_ROOT / ".github/workflows/ci.yml").read_text())[
        "jobs"
    ]["python-tests"]["steps"]
    shims = tmp_path / "mise-shims"
    shims.mkdir()
    # Setup actions prepend their tool directories. Use the actual workflow
    # ordering while keeping the competing shim entirely inside this fixture.
    path = []
    for step in steps:
        action = step.get("uses", "").split("@", 1)[0]
        if action == "ruby/setup-ruby":
            assert step["with"]["ruby-version"] == "4.0.7"
            path.insert(0, str(native.parent))
        elif action == "jdx/mise-action":
            path.insert(0, str(shims))
    assert set(path) == {str(native.parent), str(shims)}
    monkeypatch.setenv("PATH", os.pathsep.join(path))
    before = ruby_profile(cwd=tmp_path, home=tmp_path / "before-home")
    (shims / "ruby").symlink_to(Path(mise).resolve())
    after = ruby_profile(cwd=tmp_path, home=tmp_path / "after-home")
    assert ruby_executable() == native
    assert after == before
    assert (after["ruby"], after["rubygems"]) == ("4.0.7", "4.0.20")
