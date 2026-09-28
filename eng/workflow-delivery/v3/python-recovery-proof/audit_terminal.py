# Runtime assertions are mandatory; campaign import rejects optimized Python.
# Literal IDs, URLs and limits mirror the accepted fixed protocol.
# Each audited stage keeps one reservation/evidence transaction boundary.
# ruff: noqa: E501, S101

"""Replay the original finalizer inputs and controls offline in the same lifetime."""

import argparse
import hashlib
import json
import os
from datetime import datetime

from dispatch_normal import BINDING, CHECKOUT, ROOT, TARGET
from normal_operator import Operator
from proof_checks import result_contract
from terminal_controls import extract
from three_workflow_delivery_v3.adapters.pypi import PythonRegistry
from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.python_cli import (
    PythonInputs,
    _finalize,
    _manifest,
)
from three_workflow_delivery_v3.release.python_governance import (
    PythonGovernance,
)
from three_workflow_delivery_v3.release.python_publication import (
    audit_python_publication_result,
    python_publication_result_from_document,
)
from three_workflow_delivery_v3.repository.python_model import (
    admit_python_provider_facts,
    compile_python_repository_model,
)


def main() -> None:
    """Execute the bound caller stage once."""
    directory = ROOT / "operation"
    assert (directory / "ledger.json").exists()
    op = Operator(directory, TARGET)
    with op.deadline():
        assert "terminal_capture_completed" in op.ledger
        assert "terminal_audit_started" not in op.ledger
        controls = extract(directory, op.ledger["run"], TARGET)
        controls_path = directory / "terminal-controls.json"
        assert not controls_path.exists()
        controls_path.write_text(json.dumps(controls, indent=2) + "\n")
        assert controls["target"] == TARGET
        assert controls["run"] == op.ledger["run"]
        # The retained source files and exact finalizer step environment are
        # independently inspected before the later terminal gate is issued.
        assert controls["files_sha256"]
        for name, digest in controls["files_sha256"].items():
            path = directory / name
            assert path.resolve().is_relative_to(directory.resolve())
            assert hashlib.sha256(path.read_bytes()).hexdigest() == digest
        op.ledger["terminal_audit_started"] = op.now()
        op.save()
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
        recovered = PythonInputs(
            directory / "inputs",
            "live-release",
            (directory / "inputs/terminal-references.json").read_text(),
        )
        original = PythonInputs(
            directory / "inputs", "live-release", controls["references"]
        )
        assert "outcome" not in original.references
        for role, reference in original.references.items():
            assert recovered.references[role] == reference
            original.content(role)
            original.transport(role)
        facts = admit_python_provider_facts(
            original.content("provider"),
            manifest=_manifest(original),
            request_reference=original.reference("request"),
            result_reference=original.reference("provider"),
            result_transport=original.transport("provider"),
        )
        model = compile_python_repository_model(CHECKOUT, facts)
        assert canonicalize(model.to_document()) == original.content("model")
        qualification = original.qualification()
        governance = original.document("governance")
        assert qualification.governance == PythonGovernance(
            PythonRegistry(governance["registry"]),
            canonicalize(governance["content"]),
            governance["source-commit"],
            datetime.fromisoformat(governance["observed-at"]),
        )
        assert (
            hashlib.sha256(canonicalize(governance["content"])).hexdigest()
            == "1831282b13059eb101b9ef095c6917f6f82a0aa6972e1d130dab8d19c49d0a86"
        )
        decision = original.decision()
        assert decision.result == "passed"
        output = directory / "replayed-outcome.json"
        assert not output.exists()
        arguments = argparse.Namespace(
            output=output,
            publisher_conclusion=controls["publisher_conclusion"],
            publication_step_outcome=controls["publication_step_outcome"],
            terminal_reference=controls["terminal_reference"],
            observation_conclusion=controls["observation_conclusion"],
        )
        successful = _finalize(arguments, original)
        assert output.read_bytes() == recovered.content("outcome")
        result = python_publication_result_from_document(
            original.document("result")
        )
        audit_python_publication_result(result, original.marker())
        result_contract(result, BINDING["mode"])
        assert successful is (BINDING["mode"] == "none")
        report = {
            "run": op.ledger["run"],
            "target": TARGET,
            "original_controls_sha256": hashlib.sha256(
                (directory / "terminal-controls.json").read_bytes()
            ).hexdigest(),
            "original_outcome_sha256": hashlib.sha256(
                recovered.content("outcome")
            ).hexdigest(),
            "replayed_outcome": json.loads(output.read_bytes()),
            "successful": successful,
        }
        (directory / "terminal-replay.json").write_text(
            json.dumps(report, indent=2) + "\n"
        )
        op.ledger["terminal_audit_completed"] = op.now()
        op.save()


if __name__ == "__main__":
    main()
