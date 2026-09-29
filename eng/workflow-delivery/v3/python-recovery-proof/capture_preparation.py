# Runtime assertions are mandatory; campaign import rejects optimized Python.
# Literal IDs, URLs and limits mirror the accepted fixed protocol.
# Each audited stage keeps one reservation/evidence transaction boundary.
# ruff: noqa: PLR0915, PLR2004, S101

"""Capture one completed preparation DAG using the existing bounded ledger."""

import hashlib
import json
import re

from capture_artifact import retain_artifact, retain_bytes, validate_artifacts
from dispatch_normal import ROOT, TARGET
from normal_operator import Operator

PREPARATION_JOBS = (
    "request-python",
    "discover-python",
    "compile-python",
    "plan-python-release",
    "build-python",
    "qualify-python",
    "prepare-python-publication",
)


def main() -> None:
    """Execute the bound caller stage once."""
    assert (ROOT / "operation" / "ledger.json").exists()
    op = Operator(ROOT / "operation", TARGET)
    with op.deadline():
        op.ledger.setdefault("preparation_capture_started", op.now())
        op.save()
        run = op.ledger["run"]
        endpoint = f"repos/hcoona/three/actions/runs/{run}"
        jobs_doc = op.capture_get(
            endpoint + "/jobs?per_page=100", "preparation-jobs"
        )
        assert jobs_doc["total_count"] <= 100
        jobs = {j["name"]: j for j in jobs_doc["jobs"]}
        assert len(jobs) == len(jobs_doc["jobs"])
        for name in PREPARATION_JOBS:
            job = jobs[name]
            assert job["run_id"] == run
            assert job["head_sha"] == TARGET
            assert job["run_attempt"] == 1
            assert job["status"] == "completed", name
            assert job["conclusion"] == "success", name
        pending = op.capture_get(
            endpoint + "/pending_deployments", "preparation-pending"
        )
        assert len(pending) == 1
        assert pending[0]["environment"]["id"] == 22765954016
        assert pending[0]["current_user_can_approve"] is True
        logs = ROOT / "operation" / "preparation-logs"
        logs.mkdir(exist_ok=True)
        refs = {}
        trace = []
        for name in PREPARATION_JOBS:
            raw = op.transfer(
                "log",
                f"repos/hcoona/three/actions/jobs/{jobs[name]['id']}/logs",
                name,
            )
            retain_bytes(logs / (name + ".log"), raw)
            lines = raw.decode("utf-8").splitlines()
            maps = []
            for line in lines:
                match = re.search(
                    r"(?:NEXT_REFERENCES|WDV3_REFERENCES): (\{.*\})\s*$", line
                )
                if match:
                    value = json.loads(match.group(1))
                    assert isinstance(value, dict)
                    maps.append(value)
            assert maps, name
            final = max(maps, key=len)
            for role, value in refs.items():
                assert final.get(role) == value, (name, role)
            refs = final
            trace.append(
                {
                    "job": name,
                    "job_id": jobs[name]["id"],
                    "log_sha256": hashlib.sha256(raw).hexdigest(),
                    "maps": maps,
                }
            )
        expected = {
            "request",
            "intent",
            "governance",
            "provider",
            "model",
            "qualification",
            "wheel",
            "sdist",
            "build-report",
            "artifacts",
            "quality",
            "decision",
            "observation",
            "publication",
            "summary",
            "bundle",
        }
        assert set(refs) == expected
        retain_bytes(
            logs / "edge-trace.json",
            (json.dumps(trace, indent=2) + "\n").encode(),
        )
        artifacts_doc = op.capture_get(
            endpoint + "/artifacts?per_page=100", "preparation-artifacts"
        )
        assert artifacts_doc["total_count"] == len(expected)
        artifacts = validate_artifacts(refs, artifacts_doc, run, TARGET)
        inputs = ROOT / "operation" / "inputs"
        inputs.mkdir(exist_ok=True)
        for role, ref in refs.items():
            retain_artifact(
                op, inputs, role, ref, artifacts[ref["artifact-id"]]
            )
        retain_bytes(
            inputs / "references.json",
            json.dumps(refs, sort_keys=True, separators=(",", ":")).encode(),
        )
        op.ledger.setdefault("preparation_capture_completed", op.now())
        op.save()


if __name__ == "__main__":
    main()
