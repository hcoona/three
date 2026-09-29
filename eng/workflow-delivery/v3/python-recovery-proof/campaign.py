# Runtime assertions are mandatory; campaign import rejects optimized Python.
# Native records and operator JSON retain their dynamic external schema.
# Literal IDs, URLs and limits mirror the accepted fixed protocol.
# ruff: noqa: ANN401, E501, PLR2004, S101, PLR0915

"""Finite isolated hosted-recovery campaign. Importing has no effects."""

import fcntl
import hashlib
import json
import os
import re
import shutil
from contextlib import contextmanager
from datetime import UTC, datetime, timedelta
from pathlib import Path
from typing import Any

if not __debug__:
    message = "Hosted operator requires non-optimized Python; assertions are mandatory."
    raise RuntimeError(message)

EXPIRY = datetime.fromisoformat("2026-10-05T02:27:04.836609+00:00")
SCHEMA = "testpypi-hosted-recovery-campaign-v1"
AUTHORS = {
    "/root",
    "/root/python_hosted_operator",
    "/root/python_recovery_test_generator",
    "/root/read_test_generator",
}


def digest(path: Any) -> Any:
    """Hash the exact retained file bytes."""
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def read(path: Any) -> Any:
    """Read and validate the persistent campaign inventory."""
    return json.loads(Path(path).read_bytes())


def save_json(path: Any, value: Any) -> None:
    """Publish durable reservations before an effect can be reached."""
    path = Path(path)
    temporary = path.with_suffix(".next")
    with temporary.open("w") as stream:
        temporary.chmod(0o600)
        json.dump(value, stream, indent=2)
        stream.write("\n")
        stream.flush()
        os.fsync(stream.fileno())
    temporary.replace(path)
    descriptor = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY)
    try:
        os.fsync(descriptor)
    finally:
        os.close(descriptor)


def bound_files(directory: Any, files: Any) -> None:
    """Reject changed or escaping bound evidence paths."""
    assert isinstance(files, dict)
    assert files
    directory = Path(directory).resolve()
    for name, expected in files.items():
        assert isinstance(name, str)
        assert not Path(name).is_absolute()
        assert ".." not in Path(name).parts
        path = (directory / name).resolve()
        assert path.is_relative_to(directory)
        assert path != directory
        assert digest(path) == expected, f"bound evidence changed: {name}"


def gate(
    directory: Any, filename: Any, binding: Any, kind: Any, *, run: Any = None
) -> Any:
    """Consume an independently authored gate, never create one."""
    directory = Path(directory)
    document = read(directory / filename)
    reviewers = binding["independent_reviewers"]
    assert reviewers
    assert all(r.startswith("/root/") and r not in AUTHORS for r in reviewers)
    assert document["reviewer"] in reviewers
    assert document["reviewer"] not in AUTHORS
    assert document["result"] == "passed"
    assert document["kind"] == kind
    assert document["target"] == binding["target"]
    assert document["scenario"] == binding["scenario"]
    assert document["attempt"] == binding["attempt"]
    assert document["mode"] == binding["mode"]
    assert document["binding_sha256"] == digest(
        directory / "execution-binding.json"
    )
    bound_files(directory, document["files_sha256"])
    if run is not None:
        assert document["run"] == run
    return document


class Campaign:
    """Enforce the finite campaign and independent closure gates."""

    def __init__(self, directory: Any) -> None:
        """Load or construct the reserved caller state."""
        assert __debug__, "non-optimized Python required"
        self.directory = Path(directory)
        self.path = self.directory / "campaign.json"

    @contextmanager
    def locked(self) -> Any:
        """Hold the exclusive nonblocking campaign lock."""
        with (self.directory / "campaign.lock").open("a") as stream:
            fcntl.flock(stream, fcntl.LOCK_EX | fcntl.LOCK_NB)
            try:
                yield
            finally:
                fcntl.flock(stream, fcntl.LOCK_UN)

    def read(self) -> Any:
        """Read and validate the persistent campaign inventory."""
        state = read(self.path)
        assert state["schema"] == SCHEMA
        assert state["expires"] == EXPIRY.isoformat()
        scenarios = state["scenarios"]
        assert 0 <= len(scenarios) <= 5
        assert [s["id"] for s in scenarios] == [
            f"{i:02}" for i in range(1, len(scenarios) + 1)
        ]
        assert len({s["version"] for s in scenarios}) == len(scenarios)
        for scenario in scenarios:
            attempts = scenario["attempts"]
            assert 1 <= len(attempts) <= 4
            assert [a["id"] for a in attempts] == [
                f"{i:02}" for i in range(1, len(attempts) + 1)
            ]
            assert sum(len(a["uploads_reserved"]) for a in attempts) <= 4
            for attempt in attempts:
                assert attempt["target"] == scenario["target"]
                assert attempt["uploads_reserved"] in ([], ["wheel"], ["sdist"])
        return state

    def save(self, state: Any) -> None:
        """Persist the original ledger durably."""
        save_json(self.path, state)

    def initialize(self) -> None:
        """Create a fresh campaign without replacing existing state."""
        with self.locked():
            assert not self.path.exists(), "campaign cannot be reset"
            self.save(
                {
                    "schema": SCHEMA,
                    "expires": EXPIRY.isoformat(),
                    "stopped": False,
                    "scenarios": [],
                }
            )

    def attempt_directory(self, scenario: Any, attempt: Any) -> Any:
        """Locate one bounded scenario and Attempt directory."""
        assert re.fullmatch(r"0[1-5]", scenario)
        assert re.fullmatch(r"0[1-4]", attempt)
        return self.directory / "scenarios" / scenario / "attempts" / attempt

    def validate_binding(
        self, directory: Any, *, historical: Any = False
    ) -> Any:
        """Verify source, protocol and frozen execution bindings."""
        binding = read(directory / "execution-binding.json")
        assert binding["target"] == binding["tooling"]
        assert re.fullmatch(r"[0-9a-f]{40}", binding["target"])
        assert re.fullmatch(r"[0-9a-f]{40}", binding["tree"])
        assert binding["mode"] in ("none", "stop-after-wheel")
        protocol = (
            directory / "admission/protocol.md"
            if historical
            else self.directory / "protocol.md"
        )
        if historical and not protocol.exists():
            # Legacy history can only migrate from byte-identical retained authority.
            protocol = self.directory / "protocol.md"
        assert digest(protocol) == binding["protocol_sha256"]
        caller_directory = (
            directory / "callers" if historical else self.directory
        )
        expected_callers = {
            p.name
            for p in caller_directory.glob("*.py")
            if not p.name.startswith("test_")
        }
        assert set(binding["files_sha256"]) == expected_callers
        bound_files(caller_directory, binding["files_sha256"])
        version = binding["version"]
        assert binding["filenames"] == [
            f"hcoona_release_smoke_python-{version}-py3-none-any.whl",
            f"hcoona_release_smoke_python-{version}.tar.gz",
        ]
        return binding

    def retain_protocol(
        self, scenario: Any, attempt: Any, source_path: Any
    ) -> None:
        """Add verified historical provenance without rewriting old bindings."""
        with self.locked():
            directory = self.attempt_directory(scenario, attempt)
            binding = read(directory / "execution-binding.json")
            assert digest(source_path) == binding["protocol_sha256"]
            output = directory / "admission/protocol.md"
            output.parent.mkdir(exist_ok=True)
            if output.exists():
                assert digest(output) == binding["protocol_sha256"]
            else:
                with output.open("xb") as stream:
                    stream.write(Path(source_path).read_bytes())
            assert digest(output) == binding["protocol_sha256"]

    def _verify_closure(self, scenario: Any, attempt: Any) -> Any:
        """Validate the verify closure caller boundary."""
        assert attempt["closure"] is not None, (
            "prior publisher lifetime unresolved"
        )
        directory = self.attempt_directory(scenario["id"], attempt["id"])
        assert (
            digest(directory / "independent-closure-gate.json")
            == attempt["closure_gate_sha256"]
        )
        binding = self.validate_binding(directory, historical=True)
        proof = gate(
            directory, "independent-closure-gate.json", binding, "closure"
        )
        ledger = directory / "operation/ledger.json"
        if attempt["ledger_sha256"] is None:
            assert not ledger.exists()
            assert attempt["construction"]["state"] == "failed"
            assert proof["construction_failed_before_return"] is True
            assert proof["dispatch_sent"] is False
        else:
            assert digest(ledger) == attempt["ledger_sha256"]
        self._verify_continuation(directory, proof)
        if proof["conclusion"] == "terminal-complete":
            self._verify_known_complete(directory, proof)
        return proof

    @staticmethod
    def _verify_known_complete(directory: Any, proof: Any) -> None:
        """Retain known complete diagnosis without rewriting the original Outcome."""
        assert proof["destination_state"] == "complete"
        assert proof["native_audit_complete"] is True
        outcome = directory / "operation/inputs/outcome.json"
        assert proof["outcome_sha256"] == (
            digest(outcome) if outcome.exists() else None
        )
        if outcome.exists():
            assert proof["files_sha256"][
                "operation/inputs/outcome.json"
            ] == digest(outcome)

    @staticmethod
    def _verify_continuation(directory: Any, proof: Any) -> None:
        """Require independently bound supplementary completion when present."""
        sidecar = directory / "read-continuation"
        if not sidecar.exists():
            return
        for name in ("binding", "ledger", "completion"):
            assert proof[f"read_continuation_{name}_sha256"] == digest(
                sidecar / f"{name}.json"
            )
        completion = read(sidecar / "completion.json")
        assert completion["destination_state"] == "wheel-only"
        assert completion["binding_sha256"] == digest(sidecar / "binding.json")
        assert completion["ledger_sha256"] == digest(sidecar / "ledger.json")
        assert completion["original_ledger_sha256"] == digest(
            directory / "operation/ledger.json"
        )
        bound_files(sidecar, completion["files_sha256"])
        assert read(sidecar / "ledger.json")["classification"] == "complete"
        assert proof["destination_state"] == "wheel-only"
        assert proof["native_audit_complete"] is True

    def reserve(self, scenario_id: Any, attempt_id: Any) -> None:
        """Reserve a fresh bounded Attempt after independent admission."""
        with self.locked():
            state = self.read()
            assert datetime.now(UTC) < EXPIRY
            assert not state["stopped"]
            directory = self.attempt_directory(scenario_id, attempt_id)
            binding = self.validate_binding(directory)
            assert (binding["scenario"], binding["attempt"]) == (
                scenario_id,
                attempt_id,
            )
            gate(
                directory,
                "independent-execution-gate.json",
                binding,
                "execution",
            )
            scenarios = state["scenarios"]
            if scenarios:
                previous_scenario = scenarios[-1]
                previous = previous_scenario["attempts"][-1]
                closure = self._verify_closure(previous_scenario, previous)
                assert closure["conclusion"] in (
                    "no-dispatch",
                    "terminal-no-upload",
                    "exact-partial",
                    "terminal-complete",
                )
                assert closure["destination_state"] in (
                    "absent",
                    "wheel-only",
                    "complete",
                )
                if closure["conclusion"] == "terminal-complete":
                    assert scenario_id != previous_scenario["id"], (
                        "known-complete closure requires a new-version scenario"
                    )
            if not scenarios or scenario_id != scenarios[-1]["id"]:
                assert len(scenarios) < 5
                assert scenario_id == f"{len(scenarios) + 1:02}"
                assert attempt_id == "01"
                assert binding["mode"] == "stop-after-wheel"
                assert binding["version"] not in {
                    s["version"] for s in scenarios
                }
                scenarios.append(
                    {
                        "id": scenario_id,
                        "target": binding["target"],
                        "version": binding["version"],
                        "attempts": [],
                    }
                )
            scenario = scenarios[-1]
            attempts = scenario["attempts"]
            assert binding["target"] == scenario["target"]
            assert binding["version"] == scenario["version"]
            assert len(attempts) < 4
            assert attempt_id == f"{len(attempts) + 1:02}"
            if attempts:
                previous_binding = read(
                    self.attempt_directory(scenario_id, attempts[-1]["id"])
                    / "execution-binding.json"
                )
                for key in (
                    "tree",
                    "tooling",
                    "governance_sha256",
                    "profile",
                    "frozen_inputs_sha256",
                    "filenames",
                    "protocol_sha256",
                ):
                    assert binding[key] == previous_binding[key], (
                        f"scenario inputs changed: {key}"
                    )
                if binding["mode"] == "none":
                    assert closure["conclusion"] == "exact-partial"
                    assert closure["destination_state"] == "wheel-only"
                else:
                    assert closure["conclusion"] in (
                        "no-dispatch",
                        "terminal-no-upload",
                    )
                    assert closure["destination_state"] == "absent"
            else:
                assert binding["mode"] == "stop-after-wheel"
            callers = directory / "callers"
            callers.mkdir()
            for name in binding["files_sha256"]:
                shutil.copyfile(self.directory / name, callers / name)
            bound_files(callers, binding["files_sha256"])
            protocol = directory / "admission/protocol.md"
            protocol.parent.mkdir(exist_ok=True)
            if not protocol.exists():
                with protocol.open("xb") as stream:
                    stream.write((self.directory / "protocol.md").read_bytes())
            assert digest(protocol) == binding["protocol_sha256"]
            started = datetime.now(UTC)
            attempts.append(
                {
                    "id": attempt_id,
                    "target": binding["target"],
                    "mode": binding["mode"],
                    "binding_sha256": digest(
                        directory / "execution-binding.json"
                    ),
                    "started": started.isoformat(),
                    "deadline": min(
                        started + timedelta(hours=4), EXPIRY
                    ).isoformat(),
                    "closure": None,
                    "uploads_reserved": [],
                }
            )
            self.save(state)

    def active(self, scenario_id: Any, attempt_id: Any, target: Any) -> Any:
        """Require the exact active unclosed Attempt and caller binding."""
        state = self.read()
        assert not state["stopped"]
        assert datetime.now(UTC) < EXPIRY
        scenario = state["scenarios"][-1]
        attempt = scenario["attempts"][-1]
        assert scenario["id"] == scenario_id
        assert attempt["id"] == attempt_id
        assert attempt["target"] == target
        assert attempt["closure"] is None
        assert datetime.now(UTC) < datetime.fromisoformat(attempt["deadline"])
        directory = self.attempt_directory(scenario_id, attempt_id)
        assert (
            digest(directory / "execution-binding.json")
            == attempt["binding_sha256"]
        )
        self.validate_binding(directory)
        return state, scenario, attempt

    def reserve_upload(
        self, scenario_id: Any, attempt_id: Any, target: Any
    ) -> None:
        """Spend the only reachable file effect before approval."""
        state, scenario, attempt = self.active(scenario_id, attempt_id, target)
        assert not attempt["uploads_reserved"], (
            "approval/upload reservation cannot repeat"
        )
        assert sum(len(a["uploads_reserved"]) for a in scenario["attempts"]) < 4
        attempt["uploads_reserved"] = [
            "wheel" if attempt["mode"] == "stop-after-wheel" else "sdist"
        ]
        self.save(state)

    def close(self, scenario_id: Any, attempt_id: Any) -> None:
        """Close an Attempt using independently retained evidence."""
        with self.locked():
            state = self.read()
            scenario = state["scenarios"][-1]
            attempt = scenario["attempts"][-1]
            assert (scenario["id"], attempt["id"]) == (
                scenario_id,
                attempt_id,
            )
            assert attempt["closure"] is None
            directory = self.attempt_directory(scenario_id, attempt_id)
            binding = self.validate_binding(directory, historical=True)
            proof = gate(
                directory, "independent-closure-gate.json", binding, "closure"
            )
            conclusion = proof["conclusion"]
            assert conclusion in (
                "no-dispatch",
                "terminal-no-upload",
                "exact-partial",
                "terminal-complete",
                "audited-success",
            )
            ledger_path = directory / "operation/ledger.json"
            if not ledger_path.exists():
                assert conclusion == "no-dispatch"
                assert not attempt["uploads_reserved"]
                assert attempt["construction"]["state"] == "failed"
                assert not attempt.get("operator_constructed")
                assert proof["construction_failed_before_return"] is True
                assert proof["dispatch_sent"] is False
                assert proof["ledger_sha256"] is None
            else:
                ledger = read(ledger_path)
                assert ledger["target"] == attempt["target"]
                assert ledger["started"] == attempt["started"]
                assert proof["ledger_sha256"] == digest(ledger_path)
                if conclusion == "no-dispatch":
                    assert "run" not in ledger
                    assert not any(
                        r["category"] == "dispatch" and r["send_started"]
                        for r in ledger["requests"]
                    )
                else:
                    assert proof["run"] == ledger["run"]
                    assert proof["terminal"] is True
                    assert proof["publisher_quiescent"] is True
                    assert proof["dispatch_resolved"] is True
                if conclusion == "terminal-no-upload":
                    assert proof["wheel_upload_sent"] is False
                    assert proof["sdist_upload_sent"] is False
                if conclusion == "exact-partial":
                    assert proof["destination_state"] == "wheel-only"
                    assert proof["native_audit_complete"] is True
                if conclusion == "terminal-complete":
                    self._verify_known_complete(directory, proof)
                if conclusion == "audited-success":
                    assert binding["mode"] == "none"
                    assert proof["destination_state"] == "complete"
                    assert proof["seed_proof_accepted"] is True
                    assert proof["recovery_proof_accepted"] is True
                    assert proof["clean_consumers"] == "passed"
            self._verify_continuation(directory, proof)
            attempt.update(
                closure=conclusion,
                closure_gate_sha256=digest(
                    directory / "independent-closure-gate.json"
                ),
                ledger_sha256=digest(ledger_path)
                if ledger_path.exists()
                else None,
            )
            if conclusion == "audited-success":
                state["stopped"] = True
            self.save(state)
