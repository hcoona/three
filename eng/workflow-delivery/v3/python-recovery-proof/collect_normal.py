# Runtime assertions are mandatory; campaign import rejects optimized Python.
# Literal IDs, URLs and limits mirror the accepted fixed protocol.
# ruff: noqa: E501, PLR2004, S101

"""Bounded, read-only collection for the already dispatched normal Attempt."""

import datetime
import sys

from dispatch_normal import ROOT, TARGET
from normal_operator import Operator


def main() -> None:
    """Execute the bound caller stage once."""
    assert (ROOT / "operation" / "ledger.json").exists()
    operator = Operator(ROOT / "operation", TARGET)
    with operator.deadline():
        mode = sys.argv[1]
        repo = "repos/hcoona/three"
        if mode == "discover":
            assert "run" not in operator.ledger
            dispatch = [
                x
                for x in operator.ledger["requests"]
                if x["category"] == "dispatch"
            ]
            assert len(dispatch) == 1
            assert dispatch[0]["send_started"]
            start = datetime.datetime.fromisoformat(
                dispatch[0]["started"]
            ).replace(microsecond=0)
            endpoint = (
                repo
                + "/actions/workflows/workflow-delivery-v3-python-smoke.yml/runs?event=workflow_dispatch&branch=main&per_page=100"
            )
            data = operator.get(endpoint, "discovery", poll=True)
            assert data["total_count"] <= 100 or (
                data["workflow_runs"]
                and datetime.datetime.fromisoformat(
                    data["workflow_runs"][-1]["created_at"].replace(
                        "Z", "+00:00"
                    )
                )
                < start
            ), "discovery inventory incomplete"
            runs = [
                r
                for r in data["workflow_runs"]
                if datetime.datetime.fromisoformat(
                    r["created_at"].replace("Z", "+00:00")
                )
                >= start
            ]
            if not runs:
                return
            assert len(runs) == 1, "ambiguous dispatch identity"
            run = runs[0]
            assert run["head_sha"] == TARGET
            assert run["run_attempt"] == 1
            assert run["actor"]["login"] == "hcoona"
            assert run["actor"]["id"] == 712433
            assert run["triggering_actor"]["login"] == "hcoona"
            assert run["repository"]["id"] == 1102295886
            assert (
                run["path"]
                == ".github/workflows/workflow-delivery-v3-python-smoke.yml"
            )
            assert run["event"] == "workflow_dispatch"
            assert run["head_branch"] == "main"
            operator.ledger["run"] = run["id"]
            operator.save()
        elif mode == "poll":
            run = operator.get(
                repo + f"/actions/runs/{operator.ledger['run']}",
                "run-state",
                poll=True,
            )
            assert run["head_sha"] == TARGET
            assert run["run_attempt"] == 1
        elif mode in ("jobs", "artifacts", "pending", "approval-history"):
            run = operator.ledger["run"]
            suffix = {
                "jobs": "jobs?per_page=100",
                "artifacts": "artifacts?per_page=100",
                "pending": "pending_deployments",
                "approval-history": "approvals",
            }[mode]
            data = operator.get(repo + f"/actions/runs/{run}/" + suffix, mode)
            if mode == "jobs":
                assert data["total_count"] <= 100
            elif mode == "artifacts":
                assert data["total_count"] <= 32
            else:
                pass
        else:
            msg = "Unsupported read-only collection mode"
            raise ValueError(msg)


if __name__ == "__main__":
    main()
