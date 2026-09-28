# Runtime assertions are mandatory; campaign import rejects optimized Python.
# Literal IDs, URLs and limits mirror the accepted fixed protocol.
# Each audited stage keeps one reservation/evidence transaction boundary.
# ruff: noqa: E501, PLR0915, PLR2004, S101

"""Replay captured action-required current-run authority and clean consumers once."""

import hashlib
import json
import os
from datetime import datetime

from dispatch_normal import BINDING, CHECKOUT, ROOT, TARGET
from normal_operator import Operator
from proof_checks import preparation, seed_archives
from three_workflow_delivery_v3.adapters.pypi import PythonRegistry
from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.python_cli import PythonInputs, _manifest
from three_workflow_delivery_v3.release.python_governance import (
    PythonGovernance,
)
from three_workflow_delivery_v3.release.python_qualification import (
    qualify_python_release,
)
from three_workflow_delivery_v3.repository.python_model import (
    admit_python_provider_facts,
    compile_python_repository_model,
)


def main() -> None:
    """Execute the bound caller stage once."""
    assert (ROOT / "operation" / "ledger.json").exists()
    op = Operator(ROOT / "operation", TARGET)
    with op.deadline():
        assert "preparation_capture_completed" in op.ledger
        assert "preparation_audit_started" not in op.ledger
        op.ledger["preparation_audit_started"] = op.now()
        op.save()
        run_id = op.ledger["run"]
        discovery = [
            r
            for r in op.ledger["requests"]
            if r["name"] == "discovery" and r["status"] == 200
        ]
        assert discovery
        doc = json.loads((op.directory / discovery[-1]["body"]).read_bytes())
        runs = [r for r in doc["workflow_runs"] if r["id"] == run_id]
        assert len(runs) == 1
        run = runs[0]
        assert run["head_sha"] == TARGET
        assert run["run_attempt"] == 1
        assert run["event"] == "workflow_dispatch"
        assert run["head_branch"] == "main"
        assert run["repository"]["full_name"] == "hcoona/three"
        assert run["repository"]["id"] == 1102295886
        assert run["actor"]["login"] == "hcoona"
        assert run["actor"]["id"] == 712433
        assert run["triggering_actor"]["login"] == "hcoona"
        assert (
            run["path"]
            == ".github/workflows/workflow-delivery-v3-python-smoke.yml"
        )
        os.environ.update(
            {
                "WDV3_PYTHON_PROOF": BINDING["mode"],
                "WDV3_REGISTRY": "testpypi",
                "GITHUB_SHA": TARGET,
                "GITHUB_RUN_ID": str(run_id),
                "GITHUB_RUN_ATTEMPT": "1",
                "GITHUB_REPOSITORY": "hcoona/three",
                "GITHUB_EVENT_NAME": "workflow_dispatch",
                "GITHUB_REF": "refs/heads/main",
                "GITHUB_ACTOR": "hcoona",
                "GITHUB_WORKFLOW_SHA": TARGET,
                "GITHUB_WORKFLOW_REF": f"hcoona/three/{run['path']}@refs/heads/main",
            }
        )
        directory = op.directory / "inputs"
        inputs = PythonInputs(
            directory,
            "live-release",
            (directory / "references.json").read_text(),
        )
        # These references came from original producer logs, not artifact listing.
        for role in inputs.references:
            inputs.content(role)
            inputs.transport(role)
        facts = admit_python_provider_facts(
            inputs.content("provider"),
            manifest=_manifest(inputs),
            request_reference=inputs.reference("request"),
            result_reference=inputs.reference("provider"),
            result_transport=inputs.transport("provider"),
        )
        compiled = compile_python_repository_model(CHECKOUT, facts)
        assert canonicalize(compiled.to_document()) == inputs.content("model")
        qualification = inputs.qualification()
        governance_doc = inputs.document("governance")
        original_governance_instance = PythonGovernance(
            PythonRegistry(governance_doc["registry"]),
            canonicalize(governance_doc["content"]),
            governance_doc["source-commit"],
            datetime.fromisoformat(governance_doc["observed-at"]),
        )
        assert qualification.governance == original_governance_instance, (
            "original Governance edge mismatch"
        )
        artifacts = inputs.artifacts()
        decision = inputs.decision()
        assert decision.result == "passed"
        assert len(decision.evidence) == 3
        snapshot = inputs.publication()
        dispositions = preparation(
            snapshot,
            BINDING,
            (inputs.content("wheel"), inputs.content("sdist")),
            seed_contents=seed_archives(ROOT, BINDING)
            if BINDING["mode"] == "none"
            else None,
        )
        bundle = inputs.bundle()
        assert bundle.snapshot == snapshot
        assert artifacts[0].filename == BINDING["filenames"][0]
        assert artifacts[1].filename == BINDING["filenames"][1]
        original_governance = canonicalize(
            inputs.document("governance")["content"]
        )
        assert (
            hashlib.sha256(original_governance).hexdigest()
            == "1831282b13059eb101b9ef095c6917f6f82a0aa6972e1d130dab8d19c49d0a86"
        )
        report = {
            "run": run_id,
            "target": TARGET,
            "mode": BINDING["mode"],
            "dispositions": dispositions,
            "model_recomputed": True,
            "original_qualification": decision.result,
            "summary": inputs.content("summary").decode(),
            "artifact_digests": {
                r: inputs.reference(r).payload_digest
                for r in ("wheel", "sdist")
            },
            "publication_digest": inputs.reference(
                "publication"
            ).payload_digest,
            "bundle_document": bundle.to_document(),
        }
        (op.directory / "preapproval-replay.json").write_text(
            json.dumps(report, indent=2) + "\n"
        )
        evidence = qualify_python_release(
            qualification,
            artifacts,
            (inputs.content("wheel"), inputs.content("sdist")),
        )
        (op.directory / "independent-qualification.json").write_bytes(
            canonicalize([e.to_document() for e in evidence])
        )
        assert len(evidence) == 3
        assert all(e.result == "passed" for e in evidence), (
            "independent consumer failure; no retry"
        )
        op.ledger["preparation_audit_completed"] = op.now()
        op.save()


if __name__ == "__main__":
    main()
