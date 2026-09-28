# Runtime assertions are mandatory; campaign import rejects optimized Python.
# Strict data bindings are external independently reviewed evidence.
# ruff: noqa: ANN401, E501, PLR0915, PLR2004, S101, S603, S607, PT018, T201, SIM905
"""Admitted GET-only continuation of the retained terminal b31 audit."""

import argparse
import hashlib
import os
import re
import subprocess
import sys
from datetime import UTC, datetime, timedelta
from pathlib import Path
from typing import Any

from campaign import (
    AUTHORS,
    EXPIRY,
    Campaign,
    bound_files,
    digest,
    gate,
    read,
    save_json,
)
from proof_checks import diagnostic_gate
from read_policy import ReadPending, ReadStopped, deadline_guard
from registry_read import RegistryAudit, successful_index
from source_checks import CHECKOUT, verify
from three_workflow_delivery_v3.python_cli import PythonInputs

FIELDS = frozenset(
    "schema scenario attempt run purpose target tree version mode original_binding_sha256 original_ledger_sha256 original_started original_deadline governance_expiry governance_sha256 profile frozen_inputs_sha256 caller_source caller_tree caller_files_sha256 protocol_sha256 reader_checkout reader_source reader_tree original_evidence_sha256 independent_reviewers".split()
)
ADMISSION_FIELDS = frozenset(
    "schema reviewer result binding_sha256 protocol_sha256 caller_source caller_tree original_binding_sha256 original_ledger_sha256 original_deadline governance_sha256 profile frozen_inputs_sha256 dispatch_resolved terminal publisher_quiescent wheel_reservation_preserved files_sha256".split()
)


def validate_binding(directory: Path, campaign: Campaign, now: Any) -> tuple:
    """Reestablish immutable original identity and independent continuation admission."""
    sidecar = directory / "read-continuation"
    binding = read(sidecar / "binding.json")
    assert set(binding) == FIELDS
    assert binding["schema"] == "testpypi-read-continuation-v1"
    assert binding["purpose"] == "resolve-terminal-wheel-only"
    assert (
        binding["scenario"],
        binding["attempt"],
        binding["run"],
        binding["version"],
        binding["mode"],
    ) == ("01", "01", 36489233271, "0.1.0b31", "stop-after-wheel")
    assert type(binding["run"]) is int
    assert directory == campaign.attempt_directory("01", "01").resolve()
    original = campaign.validate_binding(directory, historical=True)
    for key in (
        "scenario",
        "attempt",
        "target",
        "tree",
        "version",
        "mode",
        "governance_sha256",
        "profile",
        "frozen_inputs_sha256",
    ):
        assert binding[key] == original[key]
    assert (
        binding["target"]
        == binding["reader_source"]
        == "86b63fd09fcf3d20ee2615b35e37f4a541d0dc27"
    )
    assert (
        binding["tree"]
        == binding["reader_tree"]
        == "c6ca3df1c54915433b19767621caedb07b8b52b8"
    )
    assert binding["original_binding_sha256"] == digest(
        directory / "execution-binding.json"
    )
    assert binding["original_ledger_sha256"] == digest(
        directory / "operation/ledger.json"
    )
    original_ledger = read(directory / "operation/ledger.json")
    state = campaign.read()
    assert not state["stopped"]
    assert len(state["scenarios"]) == 1
    assert len(state["scenarios"][0]["attempts"]) == 1
    attempt = state["scenarios"][0]["attempts"][0]
    assert attempt["closure"] is None
    assert attempt["uploads_reserved"] == ["wheel"]
    assert attempt["binding_sha256"] == binding["original_binding_sha256"]
    assert (
        binding["original_started"]
        == attempt["started"]
        == original_ledger["started"]
    )
    assert (
        binding["original_deadline"]
        == attempt["deadline"]
        == original_ledger["deadline"]
    )
    assert binding["original_deadline"] == "2026-09-29T01:55:15.465230+00:00"
    assert binding["governance_expiry"] == EXPIRY.isoformat()
    assert (
        datetime.fromisoformat(binding["original_started"])
        <= now
        < min(EXPIRY, datetime.fromisoformat(binding["original_deadline"]))
    )
    assert original_ledger["run"] == binding["run"]
    assert (
        "registry_audit_started" in original_ledger
        and "registry_audit_completed" not in original_ledger
    )
    evidence = binding["original_evidence_sha256"]
    required = {
        "execution-binding.json",
        "operation/ledger.json",
        "frozen-inputs.json",
        "local-ca-preparation.json",
        "independent-diagnostic-read-gate.json",
        "operation/terminal-replay.json",
        "operation/terminal-controls.json",
        "operation/registry-audit/response-1.bin",
        "operation/registry-audit/observation.json",
    }
    required.update(
        path.relative_to(directory).as_posix()
        for path in (directory / "operation/inputs").iterdir()
        if path.is_file()
    )
    assert required <= evidence.keys()
    bound_files(directory, evidence)
    diagnostic = gate(
        directory,
        "independent-diagnostic-read-gate.json",
        original,
        "diagnostic-read",
        run=binding["run"],
    )
    diagnostic_gate(diagnostic, binding["run"])
    reviewers = binding["independent_reviewers"]
    assert isinstance(reviewers, list) and reviewers
    assert all(
        isinstance(r, str) and r.startswith("/root/") and r not in AUTHORS
        for r in reviewers
    )
    admission = read(sidecar / "independent-admission.json")
    assert set(admission) == ADMISSION_FIELDS
    assert admission["schema"] == "testpypi-read-continuation-admission-v1"
    assert (
        admission["reviewer"] in reviewers
        and admission["reviewer"] not in AUTHORS
    )
    assert admission["result"] == "passed"
    assert admission["binding_sha256"] == digest(sidecar / "binding.json")
    for key in (
        "protocol_sha256",
        "caller_source",
        "caller_tree",
        "original_binding_sha256",
        "original_ledger_sha256",
        "original_deadline",
        "governance_sha256",
        "profile",
        "frozen_inputs_sha256",
    ):
        assert admission[key] == binding[key]
    for key in (
        "dispatch_resolved",
        "terminal",
        "publisher_quiescent",
        "wheel_reservation_preserved",
    ):
        assert admission[key] is True
    assert {"binding.json", "protocol.md"} <= admission["files_sha256"].keys()
    bound_files(sidecar, admission["files_sha256"])
    assert digest(sidecar / "protocol.md") == binding["protocol_sha256"]
    return binding, original_ledger


def verify_callers(binding: dict) -> None:
    """Bind all caller bytes and loaded modules to the protected caller tree."""
    directory = Path(__file__).resolve().parent
    files = binding["caller_files_sha256"]
    assert set(files) == {
        p.name for p in directory.glob("*.py") if not p.name.startswith("test_")
    }
    bound_files(directory, files)
    for key in ("caller_source", "caller_tree"):
        assert re.fullmatch(r"[0-9a-f]{40}", binding[key])

    def git(*args: str) -> bytes:
        return subprocess.check_output(["git", *args], cwd=CHECKOUT, timeout=30)

    assert git("rev-parse", "HEAD").decode().strip() == binding["caller_source"]
    assert (
        git("rev-parse", "HEAD^{tree}").decode().strip()
        == binding["caller_tree"]
    )
    assert not git("status", "--porcelain").strip()
    for name, expected in files.items():
        assert Path(name).name == name
        actual = git(
            "show",
            f"{binding['caller_source']}:eng/workflow-delivery/v3/python-recovery-proof/{name}",
        )
        assert hashlib.sha256(actual).hexdigest() == expected
        module = sys.modules.get(Path(name).stem)
        if module is not None:
            assert (
                Path(module.__file__).resolve() == (directory / name).resolve()
            )
    protocol = git(
        "show",
        f"{binding['caller_source']}:src/public/lib/three-workflow-delivery-v3/docs/validation/python-hosted-recovery.md",
    )
    assert hashlib.sha256(protocol).hexdigest() == binding["protocol_sha256"]
    verify(
        binding["reader_source"],
        binding["reader_tree"],
        checkout=binding["reader_checkout"],
    )
    reader_root = (
        Path(binding["reader_checkout"]).resolve()
        / "src/public/lib/three-workflow-delivery-v3/src"
    )
    for name, module in tuple(sys.modules.items()):
        if name.startswith("three_workflow_delivery_v3") and getattr(
            module, "__file__", None
        ):
            assert Path(module.__file__).resolve().is_relative_to(reader_root)


def set_reader_environment(binding: dict) -> None:
    """Recreate only the retained current-run context for the pinned reader."""
    target = binding["target"]
    os.environ.update(
        {
            "WDV3_PYTHON_PROOF": binding["mode"],
            "WDV3_REGISTRY": "testpypi",
            "GITHUB_SHA": target,
            "GITHUB_RUN_ID": str(binding["run"]),
            "GITHUB_RUN_ATTEMPT": "1",
            "GITHUB_REPOSITORY": "hcoona/three",
            "GITHUB_EVENT_NAME": "workflow_dispatch",
            "GITHUB_REF": "refs/heads/main",
            "GITHUB_ACTOR": "hcoona",
            "GITHUB_WORKFLOW_SHA": target,
            "GITHUB_WORKFLOW_REF": "hcoona/three/.github/workflows/workflow-delivery-v3-python-smoke.yml@refs/heads/main",
        }
    )


class Continuation:
    """Append registry evidence to one original Attempt without mutable capabilities."""

    def __init__(
        self, attempt_directory: Any, *, now: Any = None, wire: Any = None
    ) -> None:
        """Locate the original Attempt without constructing another lifetime."""
        self.directory = Path(attempt_directory).resolve()
        assert (
            self.directory.name == "01"
            and self.directory.parent.name == "attempts"
        )
        self.campaign = Campaign(self.directory.parents[3])
        self.sidecar = self.directory / "read-continuation"
        self.now = now or (lambda: datetime.now(UTC))
        self.wire = wire

    def step(self) -> dict:
        """Validate exact independent admission, then advance only registry GETs."""
        with (
            self.campaign.locked(),
            deadline_guard("2026-09-29T01:55:15.465230+00:00", now=self.now),
        ):
            binding, original = validate_binding(
                self.directory, self.campaign, self.now()
            )
            verify_callers(binding)
            assert (
                os.environ.get("SSL_CERT_FILE")
                == "/etc/ssl/certs/ca-certificates.crt"
            )
            assert (
                digest(os.environ["SSL_CERT_FILE"])
                == read(self.directory / "local-ca-preparation.json")[
                    "bundle_sha256"
                ]
            )
            set_reader_environment(binding)
            inputs_path = self.directory / "operation/inputs"
            inputs = PythonInputs(
                inputs_path,
                "live-release",
                (inputs_path / "references.json").read_text(),
            )
            decision = inputs.decision()
            originals = tuple(
                a.inspect(inputs.content(a.variant)) for a in decision.artifacts
            )
            assert decision.snapshot.governance.registry.name == "testpypi"
            assert (
                decision.snapshot.governance.registry.profile_digest
                == binding["profile"]
            )
            assert (
                decision.snapshot.model.provider.nbgv.pep440_version
                == binding["version"]
            )
            baseline = successful_index(read(inputs_path / "result.json"))
            assert baseline is not None
            ledger_path = self.sidecar / "ledger.json"
            binding_digest = digest(self.sidecar / "binding.json")
            initialization_path = self.sidecar / "initialization.json"
            initialization = {
                "binding_sha256": binding_digest,
                "admission_sha256": digest(
                    self.sidecar / "independent-admission.json"
                ),
                "original_deadline": binding["original_deadline"],
            }
            if ledger_path.exists():
                assert read(initialization_path) == initialization
                ledger = read(ledger_path)
                assert ledger["binding_sha256"] == binding_digest
                assert (
                    ledger["original_deadline"] == binding["original_deadline"]
                )
                assert (
                    ledger["original_ledger_sha256"]
                    == binding["original_ledger_sha256"]
                )
                assert ledger["admission_sha256"] == digest(
                    self.sidecar / "independent-admission.json"
                )
            else:
                assert not initialization_path.exists(), (
                    "continuation construction uncertainty cannot reset its ledger"
                )
                assert not any(
                    (self.sidecar / name).exists()
                    for name in ("responses", "observations", "completion.json")
                ), "prior continuation evidence cannot be reset"
                save_json(initialization_path, initialization)
                prior = original["registry_audit_requests"]
                assert (
                    len(prior) == 1
                    and prior[0]["kind"] == "index"
                    and prior[0]["status"] == 200
                )
                started = prior[0]["started"]
                ledger = {
                    "schema": "testpypi-read-continuation-ledger-v1",
                    "binding_sha256": binding_digest,
                    "admission_sha256": digest(
                        self.sidecar / "independent-admission.json"
                    ),
                    "original_deadline": binding["original_deadline"],
                    "original_ledger_sha256": binding["original_ledger_sha256"],
                    "inherited_observations": prior,
                    "requests": [],
                    "classification": "pending",
                    "pacing": {
                        "last_started": started,
                        "next_not_before": (
                            datetime.fromisoformat(started)
                            + timedelta(seconds=30)
                        ).isoformat(),
                        "backoff_step": 1,
                        "consecutive_errors": 0,
                        "requests": 1,
                    },
                }
                save_json(ledger_path, ledger)
            completion_path = self.sidecar / "completion.json"
            if completion_path.exists():
                completion = read(completion_path)
                assert completion["binding_sha256"] == binding_digest
                assert completion["ledger_sha256"] == digest(ledger_path)
                bound_files(self.sidecar, completion["files_sha256"])
                return completion
            audit = RegistryAudit(
                self.sidecar,
                ledger,
                binding["original_deadline"],
                decision.snapshot.governance.registry,
                originals[:1],
                save=lambda: save_json(ledger_path, ledger),
                now=self.now,
                wire=self.wire,
                baseline=baseline,
            )
            audit.step()
            assert (
                digest(self.directory / "operation/ledger.json")
                == binding["original_ledger_sha256"]
            )
            bound_files(self.directory, binding["original_evidence_sha256"])
            files = {
                p.relative_to(self.sidecar).as_posix(): digest(p)
                for folder in ("responses", "observations")
                for p in (self.sidecar / folder).iterdir()
                if p.is_file()
            }
            files.update(
                {
                    name: digest(self.sidecar / name)
                    for name in (
                        "binding.json",
                        "protocol.md",
                        "independent-admission.json",
                        "initialization.json",
                    )
                }
            )
            completion = {
                "schema": "testpypi-read-continuation-completion-v1",
                "binding_sha256": binding_digest,
                "ledger_sha256": digest(ledger_path),
                "original_ledger_sha256": binding["original_ledger_sha256"],
                "destination_state": "wheel-only",
                "completed": self.now().isoformat(),
                "files_sha256": files,
            }
            save_json(completion_path, completion)
            return completion


def main() -> None:
    """Run one bounded continuation step; pending exit 75 invites paced resume."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("attempt_directory", type=Path)
    args = parser.parse_args()
    try:
        result = Continuation(args.attempt_directory).step()
    except ReadPending as pending:
        print(str(pending))
        raise SystemExit(75) from pending
    except ReadStopped as stopped:
        print(str(stopped))
        raise SystemExit(1) from stopped
    print(result["destination_state"])


if __name__ == "__main__":
    main()
