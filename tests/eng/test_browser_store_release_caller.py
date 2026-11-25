"""Browser-store Release workflow credential, gate and transfer contracts."""

import re
from pathlib import Path

import pytest
import yaml

from .ci_workflow_shell import (
    action_steps,
    assert_release_transfers,
    condition,
    dependencies,
    selected_control_call,
)

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ".github/workflows/workflow-delivery-v3-browser-store-release.yml"
STORES = ["chrome-web-store", "firefox-amo", "edge-addons"]
READ_SCOPE = "https://www.googleapis.com/auth/chromewebstore.readonly"
WRITE_SCOPE = "https://www.googleapis.com/auth/chromewebstore"
STORE_SECRETS = (
    "AMO_JWT_ISSUER",
    "AMO_JWT_SECRET",
    "EDGE_API_KEY",
    "EDGE_CLIENT_ID",
)


@pytest.fixture
def workflow():
    """Load the current workflow as semantic YAML values."""
    return yaml.safe_load((ROOT / WORKFLOW).read_text())


def test_dispatch_selects_an_official_store_or_a_dry_buddy_request(workflow):
    """Store and channel are dispatch choices; dry run is the default."""
    inputs = workflow["on"]["workflow_dispatch"]["inputs"]
    assert set(inputs["store"]["options"]) == set(STORES)
    assert set(inputs["channel"]["options"]) == {"official", "buddy"}
    assert inputs["dry-run"]["default"] is True
    env = workflow["env"]
    assert env["DESTINATION"] == "${{ inputs.store }}"
    assert env["CHANNEL"] == "${{ inputs.channel }}"
    for variable in (
        "WORKFLOW_DELIVERY_CWS_EXTENSION_ID",
        "WORKFLOW_DELIVERY_AMO_ADDON_ID",
        "WORKFLOW_DELIVERY_EDGE_PRODUCT_ID",
    ):
        assert "vars." + variable in env["STORE_ITEM"]
    assert env["STORE_PUBLISHER"] == (
        "${{ vars.WORKFLOW_DELIVERY_CWS_PUBLISHER_ID }}"
    )
    concurrency = workflow["concurrency"]
    assert concurrency["cancel-in-progress"] is False
    assert "inputs.store" in concurrency["group"]
    assert "steam-account-history-to-csv" in concurrency["group"]
    assert not any(
        field in concurrency["group"]
        for field in ("github.run_id", "github.ref", "inputs.dry-run")
    )


def test_only_the_publisher_holds_environment_secrets_and_write_scope(
    workflow,
):
    """The gated publisher alone receives store secrets and the write scope."""
    jobs = workflow["jobs"]
    assert workflow["permissions"] == {}
    assert "secrets." not in yaml.safe_dump(workflow["env"])
    permitted_auth = {"observe", "publish", "verify-result"}
    for name, job in jobs.items():
        permissions = {"contents": "read"}
        if name in permitted_auth:
            permissions["id-token"] = "write"
        assert job["permissions"] == permissions
        assert ("environment" in job) == (name == "publish")
        for checkout in action_steps(job, "actions/checkout"):
            assert checkout["with"]["persist-credentials"] is False
        text = yaml.safe_dump(job)
        for secret in STORE_SECRETS:
            assert ("secrets." + secret in text) == (name == "publish")
        if name != "publish":
            assert "secrets." not in text
            assert "publish-browser-store" not in text
        auth = action_steps(job, "google-github-actions/auth")
        assert bool(auth) == (name in permitted_auth)
        for step in auth:
            for store in STORES:
                for dry in (False, True):
                    assert condition(
                        step["if"],
                        {"inputs.store": store, "inputs.dry-run": dry},
                    ) == (
                        store == "chrome-web-store"
                        and (name == "publish" or not dry)
                    )
            assert step["with"]["create_credentials_file"] is False
            assert step["with"]["export_environment_variables"] is False
            assert step["with"]["token_format"] == (
                "access_token"  # noqa: S105 - Output kind, not a secret.
            )
            assert step["with"]["access_token_scopes"] == (
                WRITE_SCOPE if name == "publish" else READ_SCOPE
            )
    assert jobs["publish"]["environment"]["name"] == (
        "${{ format('workflow-delivery-v3-{0}', inputs.store) }}"
    )


@pytest.mark.parametrize(
    ("dry", "needed", "allowed"),
    [
        (False, True, True),
        (False, False, False),
        (True, True, False),
        (True, False, False),
    ],
)
def test_publisher_gate_excludes_dry_runs_and_satisfied_stores(
    workflow, dry, needed, allowed
):
    """A dry run or an already-satisfied store never reaches the gate."""
    assert (
        condition(
            workflow["jobs"]["publish"]["if"],
            {
                "inputs.dry-run": dry,
                "needs.observe.outputs.write-needed": str(needed).lower(),
                "cancelled": False,
            },
        )
        is allowed
    )


@pytest.mark.parametrize(
    ("publish", "publication_id"),
    [("success", "42"), ("failure", "42"), ("failure", ""), ("skipped", "")],
)
def test_verification_follows_any_publisher_result(
    workflow, publish, publication_id
):
    """Verification completes from store state after any publisher result."""
    job = workflow["jobs"]["verify-result"]
    assert {"prepare", "observe", "publish"} <= set(dependencies(job))
    values = {
        "cancelled": False,
        "inputs.dry-run": False,
        "needs.prepare.result": "success",
        "needs.observe.result": "success",
        "needs.publish.result": publish,
        "needs.publish.outputs.publication-id": publication_id,
    }
    assert condition(job["if"], values, success=publish == "success")
    assert not condition(job["if"], {**values, "inputs.dry-run": True})
    assert not condition(job["if"], {**values, "cancelled": True})
    for prereq in ("prepare", "observe"):
        assert not condition(
            job["if"], {**values, f"needs.{prereq}.result": "failure"}
        )
    publication = next(
        step
        for step in action_steps(job, "actions/download-artifact")
        if step["with"]["path"] == "${{ runner.temp }}/publication"
    )
    assert condition(publication["if"], values) is bool(publication_id)
    assert not job.get("continue-on-error", False)
    text = "\n".join(step.get("run", "") for step in job["steps"])
    assert "observe-browser-store" in text
    assert "complete-browser-store" in text


def test_preparation_builds_once_without_product_install_scripts(workflow):
    """One unprivileged build serves every store from frozen dependencies."""
    text = "\n".join(
        step.get("run", "") for step in workflow["jobs"]["prepare"]["steps"]
    )
    assert "install --frozen-lockfile --ignore-scripts" in text
    assert "release prepare-browser-store" in text
    for name in ("observe", "publish", "verify-result"):
        job_text = "\n".join(
            step.get("run", "") for step in workflow["jobs"][name]["steps"]
        )
        assert "pnpm" not in job_text
        assert "release prepare-browser-store" not in job_text


def test_actions_are_pinned_and_artifacts_are_immutable(workflow):
    """Every action is pinned; transfers use immutable digest-checked IDs."""
    for job in workflow["jobs"].values():
        for step in job["steps"]:
            if "uses" in step:
                assert re.fullmatch(r"[\w./-]+@[0-9a-f]{40}", step["uses"])
    assert_release_transfers(workflow)


@pytest.mark.parametrize(
    ("job_name", "stage"),
    [
        ("prepare", ["request"]),
        ("observe", ["receive", "observation"]),
        ("publish", ["receive", "publication"]),
        ("verify-result", ["receive", "verification"]),
    ],
)
def test_selected_control_executable_reaches_caller(
    workflow, tmp_path, job_name, stage
):
    """Every stage invokes the selected runtime on the transferred control."""
    arguments = selected_control_call(
        workflow,
        job_name,
        root=ROOT,
        cwd=tmp_path,
        tools=("node", "pnpm"),
    )
    assert arguments == [
        str(tmp_path / "control/WorkflowDelivery.dll"),
        "release",
        "job-inputs",
        "browser-store",
        *stage,
    ]
