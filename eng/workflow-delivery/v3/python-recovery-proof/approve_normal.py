# Runtime assertions are mandatory; campaign import rejects optimized Python.
# Literal IDs, URLs and limits mirror the accepted fixed protocol.
# ruff: noqa: E501, PLR2004, S101

"""One Environment approval after independent current-run evidence admission."""

import hashlib

from campaign import gate as read_gate
from dispatch_normal import BINDING, ROOT, TARGET
from normal_operator import Operator


def main() -> None:
    """Execute the bound caller stage once."""
    directory = ROOT / "operation"
    assert (directory / "ledger.json").exists()
    op = Operator(directory, TARGET)
    with op.deadline():
        assert "preparation_audit_completed" in op.ledger
        gate = read_gate(
            ROOT,
            "independent-preapproval-gate.json",
            BINDING,
            "preapproval",
            run=op.ledger["run"],
        )
        for name in (
            "preapproval-replay.json",
            "independent-qualification.json",
            "inputs/references.json",
        ):
            assert (
                gate["files_sha256"]["operation/" + name]
                == hashlib.sha256((directory / name).read_bytes()).hexdigest()
            )
        expected = (
            ["upload", "upload"]
            if BINDING["mode"] == "stop-after-wheel"
            else ["already-present", "upload"]
        )
        assert gate["dispositions"] == expected
        repo = "repos/hcoona/three"
        main_endpoint = repo + "/git/ref/heads/main"
        run_endpoint = repo + f"/actions/runs/{op.ledger['run']}"
        pending_endpoint = run_endpoint + "/pending_deployments"
        # Wait before refreshing any prerequisite, so a successful prefix cannot
        # perpetually renew its wait while a later prerequisite is still pending.
        op.require_reads_ready((main_endpoint, run_endpoint, pending_endpoint))
        current = op.get(main_endpoint, "preapproval-main")
        assert current["object"]["sha"] == TARGET
        run = op.get(run_endpoint, "preapproval-run")
        assert run["id"] == op.ledger["run"]
        assert run["head_sha"] == TARGET
        assert run["run_attempt"] == 1
        assert run["event"] == "workflow_dispatch"
        assert run["head_branch"] == "main"
        assert run["actor"]["login"] == "hcoona"
        assert run["actor"]["id"] == 712433
        assert (
            run["path"]
            == ".github/workflows/workflow-delivery-v3-python-smoke.yml"
        )
        assert run["status"] == "waiting"
        assert run["conclusion"] is None
        pending = op.get(pending_endpoint, "preapproval-pending")
        assert len(pending) == 1
        assert pending[0]["environment"]["id"] == 22765954016
        assert (
            pending[0]["environment"]["name"]
            == "workflow-delivery-v3-python-testpypi"
        )
        assert pending[0]["current_user_can_approve"] is True
        status, _, _content = op.approve(
            f"Issue #843 hosted recovery: independently admitted current-run proof mode {BINDING['mode']}; scenario {BINDING['scenario']}, Attempt {BINDING['attempt']}; protocol {BINDING['protocol_sha256']}. One missing-file invocation; no same-Attempt resend."
        )
        assert status == 200, "approval response uncertain; slot remains spent"
        op.ledger["approval_response"] = status
        op.save()


if __name__ == "__main__":
    main()
