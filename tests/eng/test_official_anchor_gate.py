"""Official repository writes require a decision from the read-only receiver."""

import re
from pathlib import Path

import pytest
import yaml

from .ci_workflow_shell import action_steps, condition, dependencies

WORKFLOWS = Path(__file__).resolve().parents[2] / ".github/workflows"
GATE = "workflow-delivery-v3-anchor-gate.yml"


@pytest.mark.parametrize(
    ("caller", "publisher"),
    [
        ("browser-store", "publish"),
        ("npm", "publish-npmjs"),
        ("python", "publish"),
        ("ruby", "publish-ruby-rubygems"),
    ],
)
def test_repository_writer_requires_receipt_receiver_permission(
    caller, publisher
):
    """Only a successful receiver's explicit permission grants a write."""
    jobs = yaml.safe_load(
        (WORKFLOWS / f"workflow-delivery-v3-{caller}-release.yml").read_text()
    )["jobs"]
    gate = jobs["anchor-gate"]
    assert gate["uses"] == f"./.github/workflows/{GATE}"
    assert gate["permissions"] == {"contents": "read"}
    assert "secrets" not in gate
    verify = jobs["verify" if caller == "npm" else "verify-result"]
    download = next(
        step
        for step in action_steps(verify, "actions/download-artifact")
        if step["with"]["path"] == "${{ runner.temp }}/publication"
    )
    ids = re.findall(
        r"needs\.[\w-]+\.outputs\.publication-id",
        download["with"]["artifact-ids"],
    )
    assert f"needs.{publisher}.outputs.publication-id" in ids
    missing = dict.fromkeys(ids, "")
    missing["needs.observe.outputs.write-needed"] = "true"
    assert not any(
        condition(term, missing) for term in download["if"].split("||")
    )
    gate_inputs = {
        "inputs.channel": "official",
        "inputs.registry": "npmjs" if caller == "npm" else "pypi",
        "inputs.destination": "rubygems",
        "inputs.dry-run": False,
        "needs.prepare.result": "success",
        "needs.observe.result": "success",
        "needs.observe.outputs.anchor-write-needed": "true",
        f"needs.{publisher}.result": "failure",
        "cancelled": False,
    }
    # A failed command can still produce a receipt; only the reader assesses it.
    assert condition(gate["if"], gate_inputs, success=False)
    if caller == "npm":
        assert not condition(
            gate["if"], gate_inputs | {"inputs.registry": "github-packages"}
        )
    for role, producer in (
        ("control", "prepare"),
        ("preparation", "prepare"),
        ("observation", "observe"),
        ("publication", publisher),
    ):
        assert producer in dependencies(gate)
        assert gate["with"][f"{role}-id"] == (
            "${{ needs." + producer + f".outputs.{role}-id" + " }}"
        )
    writer = jobs["publish-github-anchor"]
    assert "anchor-gate" in dependencies(writer)
    assert writer["permissions"] == {"contents": "write"}
    for status, permitted, allowed in (
        ("success", "true", True),
        ("success", "false", False),
        ("success", "", False),
        ("skipped", "", False),
        ("failure", "true", False),
    ):
        assert (
            condition(
                writer["if"],
                {
                    "cancelled": False,
                    "needs.anchor-gate.result": status,
                    "needs.anchor-gate.outputs.permitted": permitted,
                },
            )
            is allowed
        )


def test_gate_receives_only_these_same_run_artifacts():
    """The selected CLI needs neither destination access nor a write token."""
    workflow = yaml.safe_load((WORKFLOWS / GATE).read_text())
    job = workflow["jobs"]["evaluate"]
    assert workflow["permissions"] == {}
    assert job["permissions"] == {"contents": "read"}
    assert "environment" not in job
    assert not action_steps(job, "actions/checkout")
    downloads = action_steps(job, "actions/download-artifact")
    assert {step["with"]["artifact-ids"] for step in downloads} == {
        "${{ inputs." + role + "-id }}"
        for role in ("control", "preparation", "observation", "publication")
    }
    for step in downloads:
        assert step["with"]["digest-mismatch"] == "error"
        assert not step.get("continue-on-error", False)
        assert not {"run-id", "repository", "name", "pattern"} & set(
            step["with"]
        )
    decision = next(step for step in job["steps"] if step.get("id") == "gate")
    assert "release may-publish-github-anchor" in decision["run"]
    assert '>> "$GITHUB_OUTPUT"' in decision["run"]


@pytest.mark.parametrize(
    "caller", ["browser-store", "github", "npm", "nuget", "python", "ruby"]
)
def test_typed_refusal_reaches_only_live_verifier(caller):
    """Route live typed refusals only to verification."""
    jobs = yaml.safe_load(
        (WORKFLOWS / f"workflow-delivery-v3-{caller}-release.yml").read_text()
    )["jobs"]
    verify = jobs["verify" if caller == "npm" else "verify-result"]
    for preflight, dry in [
        ("success", False),
        ("failure", False),
        ("success", True),
    ]:
        values = {
            "cancelled": False,
            "inputs.channel": "official",
            "inputs.destination": "rubygems",
            "inputs.dry-run": dry,
            "inputs.registry": {
                "npm": "npmjs",
                "nuget": "nugetorg",
                "python": "pypi",
            }.get(caller, ""),
            "needs.prepare.result": "success",
            "needs.observe.result": preflight,
            "needs.observe.outputs.write-needed": (
                "true" if dry else "false" if preflight == "success" else ""
            ),
            "needs.observe.outputs.anchor-write-needed": (
                "true" if dry else "false" if preflight == "success" else ""
            ),
            "needs.anchor-gate.result": "skipped",
            "needs.anchor-gate.outputs.permitted": "",
            "needs.publish.result": "skipped",
            "needs.publish-npmjs.result": "skipped",
            "needs.publish-ruby-rubygems.result": "skipped",
        }
        assert condition(verify["if"], values, success=False) == (
            preflight == "success" and not dry
        )
        for name, job in jobs.items():
            if name.startswith("publish") or name == "anchor-gate":
                assert not condition(
                    job["if"], values, success=preflight == "success"
                )


@pytest.mark.parametrize("caller", ["browser-store", "npm", "python", "ruby"])
def test_only_independent_verifier_receipt_downloads_can_fail_and_continue(
    caller,
):
    """A failed download neither suppresses the other nor proves receipt."""
    jobs = yaml.safe_load(
        (WORKFLOWS / f"workflow-delivery-v3-{caller}-release.yml").read_text()
    )["jobs"]
    verify_job = "verify" if caller == "npm" else "verify-result"
    soft_failures = {
        (name, step["id"]): step
        for name, job in jobs.items()
        for step in job.get("steps", [])
        if step.get("continue-on-error")
    }
    assert set(soft_failures) == {
        (verify_job, "primary-receipt"),
        (verify_job, "anchor-receipt"),
    }
    for step in soft_failures.values():
        assert step["continue-on-error"] is True
        assert step["uses"].startswith("actions/download-artifact@")
        assert step["with"]["digest-mismatch"] == "error"
        assert "artifact-ids" in step["with"]
        assert "!= ''" in step["if"]
    verifier = next(
        step
        for step in jobs[verify_job]["steps"]
        if re.search(r"\brelease [\"']?complete-", step.get("run", ""))
    )
    assert not verifier.get("if")
    assert verifier["env"]["PRIMARY_RECEIPT_OUTCOME"] == (
        "${{ steps.primary-receipt.outcome }}"
    )
    assert verifier["env"]["ANCHOR_RECEIPT_OUTCOME"] == (
        "${{ steps.anchor-receipt.outcome }}"
    )
