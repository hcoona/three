"""Concrete operation DAG, permission separation and finite action counts."""

import re
from pathlib import Path

import pytest
import yaml

ROOT = Path(__file__).resolve().parents[5]
RECORD = "./.github/actions/workflow-delivery-v3-ruby-operation-record"
JOB_MINUTES = 30
STEP_MINUTES = 10


def load(kind):
    """Read the concrete manually dispatched caller, not a copied manifest."""
    return yaml.safe_load(
        (
            ROOT / f".github/workflows/workflow-delivery-v3-ruby-{kind}.yml"
        ).read_text()
    )


def stage(steps, name):
    """Select the one public command in the actual step sequence."""
    matches = [
        index
        for index, step in enumerate(steps)
        if re.search(
            r"ruby_operation_cli " + re.escape(name) + r"(?: |$)",
            step.get("run", ""),
        )
    ]
    assert len(matches) == 1
    return matches[0]


def persist(steps, role):
    """Locate the singular immutable upload of a known role."""
    matches = [
        index
        for index, step in enumerate(steps)
        if step.get("uses") == RECORD and step["with"]["role"] == role
    ]
    assert len(matches) == 1
    return matches[0]


@pytest.mark.parametrize("kind", ["bootstrap", "smoke"])
def test_operation_workflow_scopes_privileges_and_manual_admission(
    kind,
):
    """Only the selected Environment publisher possesses mutation capability."""
    workflow = load(kind)
    assert workflow["permissions"] == {}
    assert set(workflow["on"]) == {"workflow_dispatch"}
    assert set(workflow["on"]["workflow_dispatch"]["inputs"]) == {
        "operation",
        "destination",
    }
    assert workflow["concurrency"]["cancel-in-progress"] is False
    assert "inputs.destination" in workflow["concurrency"]["group"]
    request = workflow["jobs"]["request-ruby-operation"]
    for requirement in (
        "github.run_attempt == 1",
        "github.actor_id == '712433'",
        "github.ref == 'refs/heads/main'",
        "github.event_name == 'workflow_dispatch'",
    ):
        assert requirement in request["if"]
    publishers = {}
    for name, job in workflow["jobs"].items():
        permission = job["permissions"]
        if name.startswith("publish-ruby-"):
            destination = (
                "github-packages"
                if name.endswith("github-packages")
                else "rubygems"
            )
            publishers[destination] = name
            assert "environment" in job
            assert f"outputs.destination == '{destination}'" in job["if"]
            assert "result == 'success'" in job["if"]
            assert permission.get("packages") == (
                "write" if destination == "github-packages" else None
            )
            assert permission.get("id-token") == (
                "write" if destination == "rubygems" else None
            )
            assert all(
                not re.search(
                    r"ruby_operation_cli "
                    r"(build|quality|provider|remote)(?: |$)",
                    step.get("run", ""),
                )
                for step in job["steps"]
            )
        else:
            assert "environment" not in job
            assert "id-token" not in permission
            assert permission.get("packages") != "write"
    assert set(publishers) == {"github-packages", "rubygems"}
    prefix = (
        "publish-ruby-bootstrap-" if kind == "bootstrap" else "publish-ruby-"
    )
    assert publishers == {name: prefix + name for name in publishers}


@pytest.mark.parametrize("kind", ["bootstrap", "smoke"])
def test_operation_workflow_orders_readback_and_reservations(
    kind,
):
    """Preparation precedes Environment admission; marker precedes execution."""
    workflow = load(kind)
    prepare_name = (
        "prepare-ruby-bootstrap-publication"
        if kind == "bootstrap"
        else "prepare-ruby-publication"
    )
    preparation = workflow["jobs"][prepare_name]["steps"]
    assert (
        stage(preparation, "initialize")
        < persist(preparation, "phase-eligibility")
        < stage(preparation, "observe")
    )
    assert (
        stage(preparation, "summary")
        < persist(preparation, "summary")
        < stage(preparation, "bundle")
        < persist(preparation, "bundle")
    )
    for name, job in workflow["jobs"].items():
        if not name.startswith("publish-ruby-"):
            continue
        assert prepare_name in job["needs"]
        steps = job["steps"]
        assert (
            stage(steps, "initialize")
            < stage(steps, "authorize")
            < persist(steps, "authorization")
            < persist(steps, "phase-pre-marker")
            < stage(steps, "marker")
            < persist(steps, "marker")
            < persist(steps, "phase-execute")
            < stage(steps, "execute")
            < persist(steps, "result")
        )
        assert steps[stage(steps, "export")]["if"] == "always()"
        assert (
            "steps.execute.outcome"
            in job["outputs"]["publication-step-outcome"]
        )


def test_normal_exact_satisfied_skips_privileged_jobs():
    """No-action observation bypasses summary, bundle, Environment and token."""
    workflow = load("smoke")
    preparation = workflow["jobs"]["prepare-ruby-publication"]["steps"]
    for index in (
        stage(preparation, "summary"),
        persist(preparation, "summary"),
        stage(preparation, "bundle"),
        persist(preparation, "bundle"),
    ):
        assert (
            preparation[index]["if"]
            == "steps.publication.outputs.action-required == 'true'"
        )
    for name, job in workflow["jobs"].items():
        if name.startswith("publish-ruby-"):
            assert "outputs.action-required == 'true'" in " ".join(
                job["if"].split()
            )
    finalizer = workflow["jobs"]["finalize-attempt"]
    steps = finalizer["steps"]
    assert (
        persist(steps, "phase-zero-action")
        < stage(steps, "zero-proof")
        < persist(steps, "zero-governance")
        < persist(steps, "exact-proof")
        < stage(steps, "finalize")
    )
    for role in ("phase-zero-action", "zero-governance", "exact-proof"):
        assert "outputs.action-required == 'false'" in " ".join(
            steps[persist(steps, role)]["if"].split()
        )


@pytest.mark.parametrize("kind", ["bootstrap", "smoke"])
def test_operation_terminal_and_separate_remote_are_failure_aware(kind):
    """Cancellation and missing DAG edges still reach the terminal command."""
    jobs = load(kind)["jobs"]
    name = (
        "finalize-ruby-bootstrap" if kind == "bootstrap" else "finalize-attempt"
    )
    finalizer = jobs[name]
    assert finalizer["if"] == "always()"
    assert set(finalizer["needs"]) == set(jobs) - {
        name,
        "verify-ruby-bootstrap-remote"
        if kind == "bootstrap"
        else "verify-ruby-operation",
    }
    assert (
        finalizer["steps"][stage(finalizer["steps"], "finalize")]["if"]
        == "always()"
    )
    assert (
        "always()"
        in finalizer["steps"][persist(finalizer["steps"], "terminal")]["if"]
    )
    remote = jobs[
        "verify-ruby-bootstrap-remote"
        if kind == "bootstrap"
        else "verify-ruby-operation"
    ]
    assert remote["needs"] == name
    assert "result == 'success'" in remote["if"]
    assert "terminal-result" in remote["if"]
    assert (
        persist(remote["steps"], "phase-remote-consumer")
        < stage(remote["steps"], "remote")
        < persist(remote["steps"], "remote-consumer")
    )


@pytest.mark.parametrize("kind", ["bootstrap", "smoke"])
def test_operation_actions_pin_deadlines_ids_and_checkout(
    kind,
):
    """No artifact discovery, implicit target or unbounded action is allowed."""
    workflow = load(kind)
    assert workflow["env"]["ACTIONS_ARTIFACT_UPLOAD_CONCURRENCY"] == "1"
    assert workflow["env"]["ACTIONS_ARTIFACT_UPLOAD_TIMEOUT_MS"] == "60000"
    for job in workflow["jobs"].values():
        assert job["timeout-minutes"] == JOB_MINUTES
        for step in job["steps"]:
            assert 0 < step["timeout-minutes"] <= STEP_MINUTES
            action = step.get("uses", "")
            if action and not action.startswith("./"):
                assert re.fullmatch(r"[^@]+@[a-f0-9]{40}", action)
            if action.startswith("actions/checkout@"):
                assert step["with"] == {
                    "ref": "${{ github.sha }}",
                    "fetch-depth": 0,
                    "persist-credentials": False,
                    "submodules": False,
                    "lfs": False,
                }
            if action.startswith("actions/download-artifact@"):
                assert step["if"] == "env.WDV3_ARTIFACT_IDS != ''"
                assert (
                    step["with"]["artifact-ids"]
                    == "${{ env.WDV3_ARTIFACT_IDS }}"
                )
                assert step["with"]["skip-decompress"] is True
                assert step["with"]["digest-mismatch"] == "error"
                assert not {"name", "pattern"} & set(step["with"])


@pytest.mark.parametrize(
    ("path", "expected"),
    [
        ("bootstrap", (10, 23, 32, 96)),
        ("normal-action", (10, 28, 37, 123)),
        ("normal-zero", (9, 22, 30, 87)),
    ],
)
@pytest.mark.parametrize("destination", ["rubygems", "github-packages"])
def test_operation_dag_matches_reviewed_action_and_id_bounds(
    path, expected, destination
):
    """Count actual roles and selected predecessor IDs on each success path."""
    workflow = load("bootstrap" if path == "bootstrap" else "smoke")
    edges = {}
    counts = [0, 0, 0, 0]
    for name, job in workflow["jobs"].items():
        if name.startswith("publish-ruby-") and (
            path == "normal-zero" or not name.endswith(destination)
        ):
            continue
        candidates = re.findall(
            r"needs\.([a-z0-9-]+)\.outputs\.references",
            job["env"]["WDV3_REFERENCES"],
        )
        incoming = next(
            (
                edges[predecessor]
                for predecessor in candidates
                if predecessor in edges
            ),
            set(),
        )
        roles = []
        for step in job["steps"]:
            action = step.get("uses", "")
            if action.startswith("actions/checkout@"):
                counts[0] += 1
            elif action.startswith("actions/download-artifact@"):
                counts[2] += 1
                counts[3] += len(incoming)
            elif action == RECORD:
                role = step["with"]["role"]
                if path == "normal-zero" and role in {"summary", "bundle"}:
                    continue
                if path != "normal-zero" and role in {
                    "phase-zero-action",
                    "zero-governance",
                    "exact-proof",
                }:
                    continue
                roles.append(role)
        counts[1] += len(roles)
        counts[2] += len(roles)
        counts[3] += len(roles)
        assert not set(roles) & incoming
        edges[name] = incoming | set(roles)
    assert tuple(counts) == expected


def test_operation_record_rejects_empty_id_before_readback():
    """A missing upload ID must never turn into a download-all invocation."""
    action = yaml.safe_load(
        (
            ROOT
            / ".github/actions"
            / "workflow-delivery-v3-ruby-operation-record/action.yml"
        ).read_text()
    )
    steps = action["runs"]["steps"]
    assert stage(steps, "upload-check") == 0
    upload = next(
        index for index, step in enumerate(steps) if step.get("id") == "upload"
    )
    download = next(
        index
        for index, step in enumerate(steps)
        if step.get("uses", "").startswith("actions/download-artifact@")
    )
    guard = next(
        index
        for index, step in enumerate(steps)
        if step.get("run") == 'test -n "${RECORD_ID}"'
    )
    assert upload < guard < download < stage(steps, "bind")
    assert steps[upload]["with"]["archive"] is False
    assert steps[upload]["with"]["overwrite"] is False
    assert steps[upload]["with"]["if-no-files-found"] == "error"
    assert (
        steps[download]["with"]["artifact-ids"]
        == "${{ steps.upload.outputs.artifact-id }}"
    )
    assert steps[download]["with"]["digest-mismatch"] == "error"
    assert steps[download]["with"]["skip-decompress"] is True
    assert (
        ".wdv3/readback/${RECORD_ROLE}/" in steps[stage(steps, "bind")]["run"]
    )
    assert all(
        not step.get("continue-on-error", False) and "if" not in step
        for step in steps
    )
