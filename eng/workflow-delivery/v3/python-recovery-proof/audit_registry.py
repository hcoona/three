# Runtime assertions are mandatory; campaign import rejects optimized Python.
# Native records and operator JSON retain their dynamic external schema.
# Literal IDs, URLs and limits mirror the accepted fixed protocol.
# Each audited stage keeps one reservation/evidence transaction boundary.
# ruff: noqa: E501, S101

"""One final destination read and independent consumers within the original lifetime."""

import hashlib
import json
import os
from pathlib import Path

from campaign import gate as read_gate
from dispatch_normal import BINDING, ROOT, TARGET
from normal_operator import Operator
from proof_checks import diagnostic_gate
from registry_read import RegistryAudit, successful_index
from three_workflow_delivery_v3.adapters.python import qualify_python_consumer
from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.python_cli import PythonInputs


def main() -> None:
    """Execute the bound caller stage once."""
    directory = ROOT / "operation"
    assert (directory / "ledger.json").exists()
    op = Operator(directory, TARGET)
    with op.deadline():
        gate = read_gate(
            ROOT,
            "independent-diagnostic-read-gate.json",
            BINDING,
            "diagnostic-read",
            run=op.ledger["run"],
        )
        diagnostic_gate(gate, op.ledger["run"])
        assert (
            os.environ.get("SSL_CERT_FILE")
            == "/etc/ssl/certs/ca-certificates.crt"
        )
        trust = json.loads((ROOT / "local-ca-preparation.json").read_text())
        assert (
            hashlib.sha256(
                Path(os.environ["SSL_CERT_FILE"]).read_bytes()
            ).hexdigest()
            == trust["bundle_sha256"]
        )
        os.environ.update(
            {
                "WDV3_PYTHON_PROOF": BINDING["mode"],
                "WDV3_REGISTRY": "testpypi",
                "GITHUB_SHA": TARGET,
                "GITHUB_RUN_ID": str(op.ledger["run"]),
                "GITHUB_RUN_ATTEMPT": "1",
                "GITHUB_REPOSITORY": "hcoona/three",
                "GITHUB_EVENT_NAME": "workflow_dispatch",
                "GITHUB_REF": "refs/heads/main",
                "GITHUB_ACTOR": "hcoona",
                "GITHUB_WORKFLOW_SHA": TARGET,
                "GITHUB_WORKFLOW_REF": "hcoona/three/.github/workflows/workflow-delivery-v3-python-smoke.yml@refs/heads/main",
            }
        )
        inputs = PythonInputs(
            directory / "inputs",
            "live-release",
            (directory / "inputs/references.json").read_text(),
        )
        decision = inputs.decision()
        originals = tuple(
            a.inspect(inputs.content(a.variant)) for a in decision.artifacts
        )
        registry = decision.snapshot.governance.registry
        assert registry.name == "testpypi"
        assert (
            decision.snapshot.model.provider.nbgv.pep440_version
            == BINDING["version"]
        )
        op.ledger.setdefault("registry_audit_started", op.now())
        state = op.ledger.setdefault("registry_read_state", {})
        op.save()
        output = directory / "registry-audit"
        purpose = gate["purpose"]
        assert purpose in ("diagnostic", "seed-proof", "recovery-proof")
        expected = originals if purpose == "recovery-proof" else originals[:1]
        result_path = directory / "inputs/result.json"
        baseline = (
            successful_index(json.loads(result_path.read_bytes()))
            if result_path.exists()
            else None
        )
        audit = RegistryAudit(
            output,
            state,
            op.original_deadline,
            registry,
            expected,
            save=op.save,
            baseline=baseline,
        )
        observation = audit.step()
        (output / "observation.json").write_bytes(
            canonicalize(observation.to_document())
        )
        if purpose != "recovery-proof":
            op.ledger["registry_audit_completed"] = op.now()
            op.save()
            return
        for distribution in observation.files:
            result = qualify_python_consumer(distribution)
            # Actual successful result requires completed adapter assertions; persist original command evidence.
            report = {
                "variant": distribution.variant,
                "filename": distribution.filename,
                "result": "passed",
                "original_digest": result.original_digest,
                "installed": json.loads(result.installed),
                "commands": [json.loads(e) for e in result.command_evidence],
            }
            (output / (distribution.variant + "-consumer.json")).write_text(
                json.dumps(report, indent=2) + "\n"
            )
            assert result.original_digest == distribution.digest
            assert result.variant == distribution.variant
            if result.rebuilt_wheel is not None:
                (output / "sdist-rebuilt.whl").write_bytes(result.rebuilt_wheel)
        op.ledger["registry_audit_completed"] = op.now()
        op.save()


if __name__ == "__main__":
    main()
