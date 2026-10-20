"""Browser-store Release workflow credential, gate and transfer contracts."""

import re
from pathlib import Path

import pytest
import yaml

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


def uses(job, action):
    """Select action use sites without coupling to step names."""
    return [
        step
        for step in job["steps"]
        if step.get("uses", "").startswith(action + "@")
    ]


def condition(expression, values):
    """Evaluate the bounded native boolean gate, independent of text spacing."""
    text = expression.removeprefix("${{").removesuffix("}}")
    text = text.replace("always()", "True").replace(
        "cancelled()", str(values["cancelled"])
    )
    text = re.sub(
        r"\b(?:inputs|needs)\.[\w.-]+",
        lambda match: repr(values[match[0]]),
        text,
    )
    text = re.sub(r"\btrue\b", "True", text)
    text = re.sub(r"\bfalse\b", "False", text)
    text = text.replace("&&", " and ").replace("||", " or ")
    text = re.sub(r"!(?!=)", " not ", text)
    return bool(eval(text, {"__builtins__": {}}, {}))  # noqa: S307 - Controlled boolean workflow expression.


def test_dispatch_selects_an_official_store_or_a_dry_buddy_request(workflow):
    """Store and channel are dispatch choices; dry run is the default."""
    inputs = workflow["on"]["workflow_dispatch"]["inputs"]
    assert inputs["store"]["options"] == STORES
    assert inputs["channel"]["options"] == ["official", "buddy"]
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
    assert set(jobs) == {"prepare", "observe", "publish", "verify-result"}
    assert workflow["permissions"] == {}
    assert jobs["prepare"]["permissions"] == {"contents": "read"}
    for name in ("observe", "publish", "verify-result"):
        assert jobs[name]["permissions"] == {
            "contents": "read",
            "id-token": "write",
        }
    for name, job in jobs.items():
        assert ("environment" in job) == (name == "publish")
        text = yaml.safe_dump(job)
        for secret in STORE_SECRETS:
            assert ("secrets." + secret in text) == (name == "publish")
        auth = uses(job, "google-github-actions/auth")
        assert len(auth) == (0 if name == "prepare" else 1)
        for step in auth:
            # A dry run acquires no store credential, even read-only.
            chrome = "inputs.store == 'chrome-web-store'"
            assert step["if"] == (
                chrome
                if name == "publish"
                else chrome + " && inputs.dry-run == false"
            )
            assert step["with"]["create_credentials_file"] is False
            assert step["with"]["token_format"] == "access_token"  # noqa: S105 - Output kind, not a secret.
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


@pytest.mark.parametrize("publish", ["success", "failure", "skipped"])
def test_verification_follows_any_publisher_result(workflow, publish):
    """Verification completes from store state after any publisher result."""
    job = workflow["jobs"]["verify-result"]
    values = {
        "cancelled": False,
        "inputs.dry-run": False,
        "needs.prepare.result": "success",
        "needs.observe.result": "success",
        "needs.publish.result": publish,
    }
    assert condition(job["if"], values)
    assert not condition(job["if"], {**values, "inputs.dry-run": True})
    assert not condition(job["if"], {**values, "cancelled": True})
    text = "\n".join(step.get("run", "") for step in job["steps"])
    assert "observe-browser-store" in text
    assert "complete-browser-store" in text


def test_preparation_builds_once_without_product_install_scripts(workflow):
    """One unprivileged build serves every store from frozen dependencies."""
    text = "\n".join(
        step.get("run", "") for step in workflow["jobs"]["prepare"]["steps"]
    )
    assert "install --frozen-lockfile --ignore-scripts" in text
    assert text.count("release prepare-browser-store") == 1
    for name in ("observe", "publish", "verify-result"):
        job_text = "\n".join(
            step.get("run", "") for step in workflow["jobs"][name]["steps"]
        )
        assert "pnpm" not in job_text
        assert "receive " in job_text


def test_actions_are_pinned_and_artifacts_are_immutable(workflow):
    """Every action is pinned; transfers use immutable digest-checked IDs."""
    for job in workflow["jobs"].values():
        for step in job["steps"]:
            if "uses" in step:
                assert re.fullmatch(r"[\w./-]+@[0-9a-f]{40}", step["uses"])
        for step in uses(job, "actions/upload-artifact"):
            assert step["if"] == "always()"
            assert step["with"]["retention-days"] == 45
            assert step["with"]["overwrite"] is False
            assert "github.run_attempt" in step["with"]["name"]
        for step in uses(job, "actions/download-artifact"):
            assert "name" not in step["with"]
            assert step["with"]["digest-mismatch"] == "error"
            assert step["with"]["artifact-ids"].startswith("${{ needs.")
