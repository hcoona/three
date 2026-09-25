"""Actual bootstrap workflow artifact order and disabled native/live gates."""

from pathlib import Path

import yaml
from three_workflow_delivery_v3.acceptance.python_bootstrap_contract import (
    SLOT_PATH,
    WORKFLOW,
    BootstrapRequest,
)
from three_workflow_delivery_v3.canonical import canonicalize, parse_json_strict

_ROOT = Path(__file__).resolve().parents[6]


def test_bootstrap_workflow_binds_immutable_artifact_dag():
    """Every dependency downloads an explicit returned ID and verifies bytes."""
    workflow = yaml.safe_load((_ROOT / WORKFLOW).read_text())
    assert set(workflow["on"]) == {"workflow_dispatch"}
    assert set(workflow["on"]["workflow_dispatch"]["inputs"]) == {
        "request_digest",
        "tooling_sha",
    }
    jobs = workflow["jobs"]
    assert set(jobs) == {"prepare", "publisher", "audit"}
    uploads = [
        step
        for job in jobs.values()
        for step in job["steps"]
        if step.get("uses", "").startswith("actions/upload-artifact@")
    ]
    assert [step["id"] for step in uploads] == [
        "prepare-upload",
        "authorization-upload",
        "marker-upload",
        "result-upload",
        "audit-upload",
    ]
    assert all(
        step["with"]["archive"] is False
        and step["with"]["overwrite"] is False
        and step["with"]["if-no-files-found"] == "error"
        for step in uploads
    )
    expected = {
        "publisher": [
            "needs.prepare.outputs.artifact-id",
            "steps.authorization-upload.outputs.artifact-id",
            "steps.marker-upload.outputs.artifact-id",
        ],
        "audit": [
            "needs.prepare.outputs.artifact-id",
            "needs.publisher.outputs.authorization-artifact-id",
            "needs.publisher.outputs.marker-artifact-id",
            "needs.publisher.outputs.result-artifact-id",
        ],
    }
    for name, identities in expected.items():
        downloads = [
            step["with"]
            for step in jobs[name]["steps"]
            if step.get("uses", "").startswith("actions/download-artifact@")
        ]
        assert [item["artifact-ids"] for item in downloads] == [
            "${{ " + identity + " }}" for identity in identities
        ]
        assert all(
            item["skip-decompress"] is True
            and item["digest-mismatch"] == "error"
            and "name" not in item
            for item in downloads
        )
    steps = jobs["publisher"]["steps"]
    schedule = []
    for step in steps:
        if step.get("uses", "").startswith("actions/download-artifact@"):
            schedule.append("download")
        elif step.get("id", "").endswith("-upload"):
            schedule.append(step["id"])
        elif "python_bootstrap " in step.get("run", ""):
            command = step["run"].split("python_bootstrap ")[1].split()[0]
            if command in {"authorize", "marker", "execute"}:
                schedule.append(command)
                if command != "authorize":
                    assert "--authorization .wdv3/input/" in step["run"]
                if command == "execute":
                    assert "--marker .wdv3/input/" in step["run"]
    assert schedule == [
        "download",
        "authorize",
        "authorization-upload",
        "download",
        "marker",
        "marker-upload",
        "download",
        "execute",
        "result-upload",
    ]


def test_bootstrap_workflow_limits_capability_and_retains_failure():
    """Only the protected publisher can mint; result keeps.

    Only the protected publisher can mint; result keeps failed
    authorization.
    """
    workflow = yaml.safe_load((_ROOT / WORKFLOW).read_text())
    assert workflow["permissions"] == {}
    assert workflow["concurrency"] == {
        "group": "wdv3-python-project-testpypi",
        "cancel-in-progress": False,
    }
    for name, job in workflow["jobs"].items():
        assert job["permissions"].get("id-token") == (
            "write" if name == "publisher" else None
        )
        assert "github.run_attempt == 1" in job["if"]
        assert "github.actor_id == '712433'" in job["if"]
        assert "github.sha == inputs.tooling_sha" in job["if"]
        assert "github.ref_protected" in job["if"]
        checkout = next(
            step
            for step in job["steps"]
            if step.get("uses", "").startswith("actions/checkout@")
        )
        assert checkout["with"] == {
            "ref": "${{ github.sha }}",
            "fetch-depth": 0,
            "persist-credentials": False,
        }
    publisher = workflow["jobs"]["publisher"]
    assert publisher["environment"] == "workflow-delivery-v3-python-testpypi"
    assert not any(
        "build-fixtures" in step.get("run", "")
        or "dotnet" in step.get("run", "")
        or "python_bootstrap prepare " in step.get("run", "")
        for step in publisher["steps"]
    )
    result = next(
        step
        for step in publisher["steps"]
        if step.get("name") == "Archive surviving result evidence"
    )
    assert result["if"] == "always()"
    assert "--directory .wdv3/bootstrap-result --output" in result["run"]
    for job_name, identity in (
        ("publisher", "result-upload"),
        ("audit", "audit-upload"),
    ):
        retained = next(
            step
            for step in workflow["jobs"][job_name]["steps"]
            if step.get("id") == identity
        )
        assert retained["if"] == "always()"
    final = workflow["jobs"]["audit"]["steps"][-1]
    assert (
        "steps.audit-upload.outputs.artifact-id"
        in final["env"]["WDV3_AUDIT_REFERENCE"]
    )
    assert (
        '("prepared", "authorization", "marker", "result", "audit")'
        in final["run"]
    )
    assert '"tooling-sha": os.environ["WDV3_TOOLING_SHA"]' in final["run"]


def test_bootstrap_request_does_not_admit_native_or_live():
    """A valid bootstrap request leaves native and normal gates disabled."""
    request = parse_json_strict((_ROOT / SLOT_PATH).read_bytes())
    if request is not None:
        BootstrapRequest(canonicalize(request))
    native = parse_json_strict(
        (
            _ROOT / ".github/workflow-delivery/native/python-requests.json"
        ).read_bytes()
    )
    assert native == {"testpypi": None, "pypi": None}
    for registry in ("testpypi", "pypi"):
        governance = parse_json_strict(
            (
                _ROOT
                / ".github/workflow-delivery/governance"
                / f"hcoona-release-smoke-python-{registry}.json"
            ).read_bytes()
        )
        assert governance["live_enabled"] is False
        assert governance["native-acceptance"] is None
        assert governance["state"] == "blocked"
