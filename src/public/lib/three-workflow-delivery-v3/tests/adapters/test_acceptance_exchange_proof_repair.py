"""Regression proof for request-bound GitHub Packages acceptance outcomes."""

# ruff: noqa: D102, D103, D107, S105, S106, SLF001, TC003

from __future__ import annotations

import hashlib
import json
from pathlib import Path
from types import SimpleNamespace
from typing import Any, Self, cast

import pytest
from three_workflow_delivery_v3 import cli as cli_module
from three_workflow_delivery_v3.adapters.github_packages import (
    ACCEPTANCE_COORDINATES,
    ACCEPTANCE_SCENARIO_SPECS,
    AcceptanceRunnerDiagnostic,
    FixedAcceptanceSuiteResult,
    ValidatedAcceptanceRequestProof,
    run_fixed_coordinate_acceptance_probe,
)
from three_workflow_delivery_v3.canonical import canonical_sha256

TIMEOUT_SECONDS = 7.0
MAX_RESPONSE_BYTES = 8192
MAX_OUTPUT_BYTES = 4096
RESPONSE_DIGEST = "sha256:" + ("a" * 64)
SECRET = "must-not-survive-diagnostics"
TAGS = {scenario: tag for scenario, _version, tag in ACCEPTANCE_SCENARIO_SPECS}


class ScriptedTransport:
    """Return deterministic, offline pre-state and readback documents."""

    def __init__(self, observations: list[dict[str, object]]) -> None:
        self.observations = list(observations)
        self.calls = 0

    def observe(
        self,
        coordinate: str,
        tag: str,
        *,
        timeout_seconds: float,
        max_response_bytes: int,
        deadline: float | None = None,
    ) -> dict[str, object]:
        del coordinate, tag, deadline
        self.calls += 1
        assert 0 < timeout_seconds <= TIMEOUT_SECONDS
        assert max_response_bytes == MAX_RESPONSE_BYTES
        return self.observations.pop(0)


class ScriptedRunner:
    """Return one closed runner document or raise one local exception."""

    def __init__(
        self,
        document: dict[str, object] | None = None,
        error: Exception | None = None,
    ) -> None:
        self.document = document
        self.error = error
        self.calls = 0

    def run(
        self,
        argv: tuple[str, ...],
        *,
        env: dict[str, str],
        timeout_seconds: float,
        max_output_bytes: int,
        deadline: float | None = None,
    ) -> dict[str, object]:
        del deadline
        self.calls += 1
        assert argv[:2] == ("npm", "publish")
        assert env == {"NPM_CONFIG_IGNORE_SCRIPTS": "true"}
        assert 0 < timeout_seconds <= TIMEOUT_SECONDS
        assert max_output_bytes == MAX_OUTPUT_BYTES
        if self.error is not None:
            raise self.error
        assert self.document is not None
        return self.document


def _coordinate(scenario: str) -> str:
    return ACCEPTANCE_COORDINATES[scenario]


def _tag(scenario: str) -> str:
    return TAGS[scenario]


def _absent() -> dict[str, object]:
    return {
        "state": "absent",
        "response-identity-digest": RESPONSE_DIGEST,
    }


def _proof(
    tarball: bytes,
    *,
    scenario: str,
    upstream_status: int = 201,
) -> ValidatedAcceptanceRequestProof:
    return ValidatedAcceptanceRequestProof.from_validated_exchange(
        raw_request=b'{"request":"exact-npm-couchdb-body"}',
        tarball=tarball,
        package_coordinate=_coordinate(scenario),
        tag=_tag(scenario),
        upstream_status=upstream_status,
        selected_headers={
            "Content-Type": "application/json",
            "ETag": '"request-bound"',
        },
        response_body=b'{"ok":true}',
    )


@pytest.mark.parametrize("upstream_status", [200, 201])
def test_validated_request_proof_closed_document_round_trip_preserves_status(
    upstream_status: int,
) -> None:
    proof = _proof(
        b"closed-proof-round-trip",
        scenario="absent-create-readback",
        upstream_status=upstream_status,
    )

    reopened = ValidatedAcceptanceRequestProof.from_closed_document(
        proof.to_document(),
        package_coordinate=proof.package_coordinate,
        tag=proof.tag,
        response_identity_digest=proof.response_identity_digest,
    )

    assert reopened == proof
    assert reopened.upstream_status == upstream_status
    assert reopened.response_identity_digest == proof.response_identity_digest


@pytest.mark.parametrize("upstream_status", [202, 204])
def test_validated_request_proof_closed_document_rejects_other_two_xx_status(
    upstream_status: int,
) -> None:
    proof = _proof(
        b"closed-proof-invalid-status",
        scenario="absent-create-readback",
    )
    document = proof.to_document()
    document["upstream-status"] = upstream_status
    document["response-identity-digest"] = canonical_sha256(
        {
            "request-digest": document["request-digest"],
            "upstream-status": upstream_status,
            "selected-headers": document["selected-headers"],
            "response-body-digest": document["response-body-digest"],
        }
    )

    with pytest.raises(ValueError, match="upstream-status"):
        ValidatedAcceptanceRequestProof.from_closed_document(
            document,
            package_coordinate=proof.package_coordinate,
            tag=proof.tag,
            response_identity_digest=document["response-identity-digest"],
        )


def _protocol_confirmed_runner_document(
    proof: ValidatedAcceptanceRequestProof,
) -> dict[str, object]:
    return {
        "outcome": "protocol-confirmed",
        "action-executed": True,
        "mutation-started": True,
        "validated-request-proof": proof,
        "request-digest": proof.request_digest,
        "upstream-status": proof.upstream_status,
        "selected-headers": dict(proof.selected_headers),
        "response-body-digest": proof.response_body_digest,
        "response-identity-digest": proof.response_identity_digest,
    }


def _exact_readback(
    tarball: bytes,
    *,
    scenario: str,
) -> dict[str, object]:
    return {
        "state": "exact",
        "tag": _tag(scenario),
        "content-sha512": "sha512:" + hashlib.sha512(tarball).hexdigest(),
        "response-identity-digest": RESPONSE_DIGEST,
    }


def _run_probe(
    tmp_path: Path,
    *,
    scenario: str,
    runner: ScriptedRunner,
    observations: list[dict[str, object]],
) -> Any:
    tarball = tmp_path / f"{scenario}.tgz"
    tarball.write_bytes(f"{scenario}-artifact".encode())
    return run_fixed_coordinate_acceptance_probe(
        scenario=scenario,
        package_coordinate=_coordinate(scenario),
        tag=_tag(scenario),
        tarball=tarball,
        tarball_sha512=(
            "sha512:" + hashlib.sha512(tarball.read_bytes()).hexdigest()
        ),
        transport=ScriptedTransport(observations),
        runner=runner,
        timeout_seconds=TIMEOUT_SECONDS,
        max_response_bytes=MAX_RESPONSE_BYTES,
        max_output_bytes=MAX_OUTPUT_BYTES,
    )


@pytest.mark.parametrize("upstream_status", [200, 201])
def test_normal_runner_propagates_proxy_accepted_exchange_proof(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    upstream_status: int,
) -> None:
    scenario = "absent-create-readback"
    tarball = tmp_path / f"{scenario}.tgz"
    tarball_bytes = f"{scenario}-artifact".encode()
    tarball.write_bytes(tarball_bytes)
    proof = _proof(
        tarball_bytes,
        scenario=scenario,
        upstream_status=upstream_status,
    )

    class Proxy:
        registry = "http://127.0.0.1:4873"
        observed = cli_module.threading.Event()
        processed = cli_module.threading.Event()

        def __init__(self, **_kwargs: object) -> None:
            self.proof = proof
            self.observed.set()
            self.processed.set()

        def __enter__(self) -> Self:
            return self

        def __exit__(self, *_args: object) -> None:
            pass

    class Process:
        returncode = 1

        def communicate(self, timeout: float) -> tuple[str, str]:
            assert 0 < timeout <= TIMEOUT_SECONDS
            return "", "npm emitted an independently unrecognized result"

        def poll(self) -> int:
            return self.returncode

        def kill(self) -> None:
            self.returncode = -9

    monkeypatch.setattr(cli_module, "_LostResponseProxy", Proxy)
    monkeypatch.setattr(
        cli_module.subprocess,
        "Popen",
        lambda *_args, **_kwargs: Process(),
    )
    runner = cli_module._AcceptanceNpmRunner(
        tmp_path / ".npmrc",
        contender_tarballs={},
        token="dedicated-readback-token",
    )

    result = runner.run_scenario(
        scenario,
        ("npm", "publish", str(tarball), "--tag", _tag(scenario)),
        env={"NPM_CONFIG_IGNORE_SCRIPTS": "true"},
        timeout_seconds=TIMEOUT_SECONDS,
        max_output_bytes=MAX_OUTPUT_BYTES,
    )

    assert result["outcome"] == "protocol-confirmed"
    assert result["validated-request-proof"] is proof
    assert result["request-digest"] == proof.request_digest
    assert result["upstream-status"] == upstream_status
    assert result["response-identity-digest"] == proof.response_identity_digest
    assert result["action-executed"] is True
    assert result["mutation-started"] is True


@pytest.mark.parametrize(
    ("scenario", "proxy_has_proof"),
    [
        ("absent-create-readback", True),
        ("absent-create-readback", False),
        ("lost-response", True),
        ("lost-response", False),
    ],
    ids=[
        "normal-proof-discards-output",
        "normal-proof-free-rejects-output",
        "lost-response-proof-discards-output",
        "lost-response-proof-free-rejects-output",
    ],
)
def test_runner_output_bound_is_non_authoritative_only_with_proxy_proof(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    *,
    scenario: str,
    proxy_has_proof: bool,
) -> None:
    tarball = tmp_path / f"{scenario}.tgz"
    tarball_bytes = f"{scenario}-artifact".encode()
    tarball.write_bytes(tarball_bytes)
    proof = _proof(tarball_bytes, scenario=scenario)

    class Proxy:
        registry = "http://127.0.0.1:4873"
        observed = cli_module.threading.Event()
        processed = cli_module.threading.Event()

        def __init__(self, **_kwargs: object) -> None:
            self.proof = proof if proxy_has_proof else None
            self.observed.set()
            self.processed.set()

        def __enter__(self) -> Self:
            return self

        def __exit__(self, *_args: object) -> None:
            pass

    class Process:
        returncode = 1

        def communicate(self, timeout: float) -> tuple[str, str]:
            assert timeout > 0
            return "oversized", "output"

        def poll(self) -> int:
            return self.returncode

        def kill(self) -> None:
            self.returncode = -9

    monkeypatch.setattr(cli_module, "_LostResponseProxy", Proxy)
    monkeypatch.setattr(
        cli_module.subprocess,
        "Popen",
        lambda *_args, **_kwargs: Process(),
    )
    runner = cli_module._AcceptanceNpmRunner(
        tmp_path / ".npmrc",
        contender_tarballs={},
        token="dedicated-readback-token",
    )

    if proxy_has_proof:
        result = runner.run_scenario(
            scenario,
            ("npm", "publish", str(tarball), "--tag", _tag(scenario)),
            env={"NPM_CONFIG_IGNORE_SCRIPTS": "true"},
            timeout_seconds=TIMEOUT_SECONDS,
            max_output_bytes=1,
        )
        assert result["outcome"] == (
            "protocol-confirmed"
            if scenario == "absent-create-readback"
            else "lost-response-processed"
        )
        assert result["validated-request-proof"] is proof
    else:
        with pytest.raises(ValueError, match="bounded limit"):
            runner.run_scenario(
                scenario,
                ("npm", "publish", str(tarball), "--tag", _tag(scenario)),
                env={"NPM_CONFIG_IGNORE_SCRIPTS": "true"},
                timeout_seconds=TIMEOUT_SECONDS,
                max_output_bytes=1,
            )


@pytest.mark.parametrize("upstream_status", [200, 201])
def test_normal_create_propagates_request_bound_accepted_exchange_proof(
    tmp_path: Path,
    upstream_status: int,
) -> None:
    scenario = "absent-create-readback"
    tarball = f"{scenario}-artifact".encode()
    proof = _proof(
        tarball,
        scenario=scenario,
        upstream_status=upstream_status,
    )

    result = _run_probe(
        tmp_path,
        scenario=scenario,
        runner=ScriptedRunner(_protocol_confirmed_runner_document(proof)),
        observations=[_absent(), _exact_readback(tarball, scenario=scenario)],
    )

    assert result.result == "protocol-confirmed"
    assert result.mutation_classification == "complete"
    assert result.validated_request_proof is proof
    assert (
        result.to_document()["validated-request-proof"] == proof.to_document()
    )
    assert proof.upstream_status == upstream_status
    document = result.to_document()
    assert document["diagnostics"] == []
    assert document["runner-diagnostic"] == {
        "exit-classification": "protocol-confirmed",
        "upstream-status": upstream_status,
        "exception-category": None,
        "request-correlation-digest": proof.request_digest,
    }


@pytest.mark.parametrize(
    (
        "action_executed",
        "mutation_started",
        "post_state",
        "post_sha_matches",
    ),
    [
        (False, True, "exact", True),
        (True, False, "exact", True),
        (True, True, "unknown", True),
        (True, True, "exact", False),
    ],
    ids=[
        "action-not-executed",
        "mutation-not-started",
        "post-not-exact",
        "post-sha512-mismatch",
    ],
)
def test_normal_protocol_confirmed_requires_every_authoritative_condition(
    tmp_path: Path,
    *,
    action_executed: bool,
    mutation_started: bool,
    post_state: str,
    post_sha_matches: bool,
) -> None:
    scenario = "absent-create-readback"
    tarball = f"{scenario}-artifact".encode()
    proof = _proof(tarball, scenario=scenario)
    runner_document = _protocol_confirmed_runner_document(proof)
    runner_document["action-executed"] = action_executed
    runner_document["mutation-started"] = mutation_started
    readback = _exact_readback(tarball, scenario=scenario)
    readback["state"] = post_state
    if not post_sha_matches:
        readback["content-sha512"] = "sha512:" + ("0" * 128)

    result = _run_probe(
        tmp_path,
        scenario=scenario,
        runner=ScriptedRunner(runner_document),
        observations=[_absent(), readback],
    )

    assert result.mutation_classification == "incomplete"
    assert result.result != "protocol-confirmed"
    proof_retained = action_executed and mutation_started
    assert (result.validated_request_proof is proof) is proof_retained
    document = result.to_document()
    assert ("validated-request-proof" in document) is proof_retained
    assert ("runner-diagnostic" in document) is proof_retained


def test_normal_create_without_http_exchange_proof_remains_incomplete(
    tmp_path: Path,
) -> None:
    scenario = "absent-create-readback"
    tarball = f"{scenario}-artifact".encode()

    result = _run_probe(
        tmp_path,
        scenario=scenario,
        runner=ScriptedRunner(
            {
                "outcome": "created",
                "action-executed": True,
                "mutation-started": True,
            }
        ),
        observations=[_absent(), _exact_readback(tarball, scenario=scenario)],
    )

    assert result.result == "created-without-request-proof"
    assert result.mutation_classification == "incomplete"
    assert result.validated_request_proof is None


def test_protocol_confirmed_without_proof_key_remains_incomplete(
    tmp_path: Path,
) -> None:
    scenario = "absent-create-readback"
    tarball = f"{scenario}-artifact".encode()
    proof = _proof(tarball, scenario=scenario)
    runner_document = _protocol_confirmed_runner_document(proof)
    del runner_document["validated-request-proof"]

    result = _run_probe(
        tmp_path,
        scenario=scenario,
        runner=ScriptedRunner(runner_document),
        observations=[_absent(), _exact_readback(tarball, scenario=scenario)],
    )

    assert result.mutation_classification == "incomplete"
    assert result.result != "protocol-confirmed"
    assert result.validated_request_proof is None
    assert "validated-request-proof" not in result.to_document()


@pytest.mark.parametrize("upstream_status", [200, 201])
def test_lost_response_with_complete_identity_reconciles_after_ambiguity(
    tmp_path: Path,
    upstream_status: int,
) -> None:
    scenario = "lost-response"
    tarball = f"{scenario}-artifact".encode()
    proof = _proof(
        tarball,
        scenario=scenario,
        upstream_status=upstream_status,
    )

    result = _run_probe(
        tmp_path,
        scenario=scenario,
        runner=ScriptedRunner(
            {
                "outcome": "lost-response-processed",
                "action-executed": True,
                "mutation-started": True,
                "validated-request-proof": proof,
            }
        ),
        observations=[_absent(), _exact_readback(tarball, scenario=scenario)],
    )

    assert result.result == "lost-response-exact-after-start"
    assert result.mutation_classification == "complete"
    assert result.pre_state == "absent"
    assert result.post_state == "exact"
    assert result.mutation_started is True
    assert result.validated_request_proof is proof
    assert proof.upstream_status == upstream_status
    assert proof.response_identity_digest != RESPONSE_DIGEST
    assert result.response_identity_digest == proof.response_identity_digest
    assert (
        result.to_document()["response-identity-digest"]
        == proof.response_identity_digest
    )


def test_acceptance_cli_persists_http_200_validated_request_proof(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    scenario = "absent-create-readback"
    upstream_status = 200
    tarball = f"{scenario}-artifact".encode()
    proof = _proof(
        tarball,
        scenario=scenario,
        upstream_status=upstream_status,
    )
    probe = _run_probe(
        tmp_path,
        scenario=scenario,
        runner=ScriptedRunner(_protocol_confirmed_runner_document(proof)),
        observations=[_absent(), _exact_readback(tarball, scenario=scenario)],
    )
    suite = FixedAcceptanceSuiteResult(
        suite=scenario,
        scenarios=(probe,),
    )
    monkeypatch.setenv("WDV3_ACCEPTANCE_GITHUB_TOKEN", "upstream-secret")
    monkeypatch.setattr(
        cli_module,
        "_build_acceptance_tarball",
        lambda root, **_kwargs: root / "unused.tgz",
    )
    monkeypatch.setattr(
        cli_module,
        "run_fixed_acceptance_suite",
        lambda **_kwargs: suite,
    )
    output = tmp_path / "acceptance.json"
    arguments = cast(
        "cli_module._AcceptanceProbeArguments",
        SimpleNamespace(
            package_coordinate=_coordinate(scenario),
            suite=scenario,
            target_sha="c" * 40,
            timeout_seconds=TIMEOUT_SECONDS,
            max_response_bytes=MAX_RESPONSE_BYTES,
            max_output_bytes=MAX_OUTPUT_BYTES,
            output=str(output),
            github_output=None,
        ),
    )

    status = cli_module._governance_run_fixed_acceptance_probe_command(
        arguments
    )
    persisted = json.loads(output.read_bytes())
    persisted_scenario = persisted["scenarios"][0]

    assert status == 0
    assert persisted_scenario["validated-request-proof"] == proof.to_document()
    assert (
        persisted_scenario["validated-request-proof"]["upstream-status"]
        == upstream_status
    )
    assert (
        persisted_scenario["response"]["identity-digest"]
        == proof.response_identity_digest
    )


def test_lost_response_without_mutation_startedness_cannot_reconcile(
    tmp_path: Path,
) -> None:
    scenario = "lost-response"
    tarball = f"{scenario}-artifact".encode()
    proof = _proof(tarball, scenario=scenario)

    result = _run_probe(
        tmp_path,
        scenario=scenario,
        runner=ScriptedRunner(
            {
                "outcome": "lost-response-processed",
                "action-executed": True,
                "mutation-started": False,
                "validated-request-proof": proof,
            }
        ),
        observations=[_absent(), _exact_readback(tarball, scenario=scenario)],
    )

    assert result.mutation_classification == "incomplete"
    assert result.result != "lost-response-exact-after-start"
    assert result.mutation_started is False
    assert result.validated_request_proof is None


def test_nonfixed_coordinate_is_rejected_before_observation_or_mutation(
    tmp_path: Path,
) -> None:
    scenario = "lost-response"
    tarball = tmp_path / "wrong-coordinate.tgz"
    tarball.write_bytes(b"wrong-coordinate")
    runner = ScriptedRunner(
        {
            "outcome": "lost-response-processed",
            "action-executed": True,
            "mutation-started": True,
        }
    )

    transport = ScriptedTransport([_absent()])

    with pytest.raises(ValueError, match="fixed coordinate"):
        run_fixed_coordinate_acceptance_probe(
            scenario=scenario,
            package_coordinate=("@hcoona/hcoona-release-smoke-npm@9.9.9"),
            tag=_tag(scenario),
            tarball=tarball,
            tarball_sha512=(
                "sha512:" + hashlib.sha512(tarball.read_bytes()).hexdigest()
            ),
            transport=transport,
            runner=runner,
            timeout_seconds=TIMEOUT_SECONDS,
            max_response_bytes=MAX_RESPONSE_BYTES,
            max_output_bytes=MAX_OUTPUT_BYTES,
        )

    assert transport.calls == 0
    assert runner.calls == 0


@pytest.mark.parametrize("upstream_status", [200, 201])
@pytest.mark.parametrize("post_state", ["unknown", "conflicting"])
def test_missing_or_mismatched_readback_remains_fail_closed(
    tmp_path: Path,
    post_state: str,
    upstream_status: int,
) -> None:
    scenario = "lost-response"
    tarball = f"{scenario}-artifact".encode()
    proof = _proof(
        tarball,
        scenario=scenario,
        upstream_status=upstream_status,
    )
    readback = _exact_readback(tarball, scenario=scenario)
    readback["state"] = post_state
    if post_state == "conflicting":
        readback["content-sha512"] = "sha512:" + ("0" * 128)

    result = _run_probe(
        tmp_path,
        scenario=scenario,
        runner=ScriptedRunner(
            {
                "outcome": "lost-response-processed",
                "action-executed": True,
                "mutation-started": True,
                "validated-request-proof": proof,
            }
        ),
        observations=[_absent(), readback],
    )

    assert result.mutation_classification in {"incomplete", "unknown"}
    assert result.result != "lost-response-exact-after-start"
    assert result.validated_request_proof is None


@pytest.mark.parametrize("pre_state", ["exact", "conflicting"])
def test_preexisting_coordinate_is_not_credited_to_current_request(
    tmp_path: Path,
    pre_state: str,
) -> None:
    scenario = "lost-response"
    tarball = f"{scenario}-artifact".encode()
    preexisting = _exact_readback(tarball, scenario=scenario)
    preexisting["state"] = pre_state
    runner = ScriptedRunner(
        {
            "outcome": "lost-response-processed",
            "action-executed": True,
            "mutation-started": True,
        }
    )

    result = _run_probe(
        tmp_path,
        scenario=scenario,
        runner=runner,
        observations=[preexisting],
    )

    assert runner.calls == 0
    assert result.result == "fixed-coordinate-already-exists"
    assert result.mutation_classification == "incomplete"
    assert result.action_executed is False
    assert result.mutation_started is False


def test_runner_failure_diagnostics_are_structured_bounded_and_redacted(
    tmp_path: Path,
) -> None:
    scenario = "absent-create-readback"
    tarball = f"{scenario}-artifact".encode()
    error = RuntimeError(f"stdout={SECRET}; stderr={SECRET}")
    error.action_executed = True  # type: ignore[attr-defined]
    error.mutation_started = True  # type: ignore[attr-defined]

    result = _run_probe(
        tmp_path,
        scenario=scenario,
        runner=ScriptedRunner(error=error),
        observations=[_absent(), _exact_readback(tarball, scenario=scenario)],
    )
    document = result.to_document()
    retained = json.dumps(document, sort_keys=True)

    assert SECRET not in retained
    assert "stdout" not in retained.lower()
    assert "stderr" not in retained.lower()
    assert len(retained.encode()) <= MAX_OUTPUT_BYTES
    assert all(isinstance(item, str) for item in document["diagnostics"])
    diagnostic = document["runner-diagnostic"]
    assert set(diagnostic) == {
        "exit-classification",
        "upstream-status",
        "exception-category",
        "request-correlation-digest",
    }
    assert diagnostic == {
        "exit-classification": "runner-failed-after-mutation-start",
        "upstream-status": None,
        "exception-category": "RuntimeError",
        "request-correlation-digest": None,
    }


def test_runner_exception_subclass_uses_closed_base_category(
    tmp_path: Path,
) -> None:
    scenario = "absent-create-readback"
    tarball = f"{scenario}-artifact".encode()
    error = FileNotFoundError("npm executable was not found")
    error.action_executed = False  # type: ignore[attr-defined]
    error.mutation_started = False  # type: ignore[attr-defined]

    result = _run_probe(
        tmp_path,
        scenario=scenario,
        runner=ScriptedRunner(error=error),
        observations=[_absent(), _exact_readback(tarball, scenario=scenario)],
    )

    assert result.to_document()["runner-diagnostic"] == {
        "exit-classification": "runner-failed-before-mutation",
        "upstream-status": None,
        "exception-category": "OSError",
        "request-correlation-digest": None,
    }


def test_unadmitted_exception_startedness_omits_runner_diagnostic(
    tmp_path: Path,
) -> None:
    scenario = "absent-create-readback"
    tarball = f"{scenario}-artifact".encode()

    result = _run_probe(
        tmp_path,
        scenario=scenario,
        runner=ScriptedRunner(error=TimeoutError("ambiguous timeout")),
        observations=[_absent(), _exact_readback(tarball, scenario=scenario)],
    )

    assert result.mutation_classification == "unknown"
    assert "runner-diagnostic" not in result.to_document()


@pytest.mark.parametrize(
    (
        "error",
        "exception_category",
        "expected_result",
        "expected_classification",
    ),
    [
        (TimeoutError("timeout"), "TimeoutError", "timeout", "unknown"),
        (
            OSError("operating system"),
            "OSError",
            "runner-failed-after-mutation-start",
            "incomplete",
        ),
        (
            RuntimeError("runtime"),
            "RuntimeError",
            "runner-failed-after-mutation-start",
            "incomplete",
        ),
        (
            ValueError("value"),
            "ValueError",
            "runner-failed-after-mutation-start",
            "incomplete",
        ),
    ],
    ids=["timeout", "os-error", "runtime-error", "value-error"],
)
def test_invalid_raw_upstream_diagnostic_preserves_local_runner_error(
    tmp_path: Path,
    error: Exception,
    exception_category: str,
    expected_result: str,
    expected_classification: str,
) -> None:
    scenario = "absent-create-readback"
    tarball = f"{scenario}-artifact".encode()
    error.action_executed = True  # type: ignore[attr-defined]
    error.mutation_started = True  # type: ignore[attr-defined]
    error.upstream_diagnostic = {  # type: ignore[attr-defined]
        "upstream-status": 500,
        "exception-category": None,
        "request-correlation-digest": None,
    }

    result = _run_probe(
        tmp_path,
        scenario=scenario,
        runner=ScriptedRunner(error=error),
        observations=[_absent(), _exact_readback(tarball, scenario=scenario)],
    )

    assert result.result == expected_result
    assert result.mutation_classification == expected_classification
    assert result.action_executed is True
    assert result.mutation_started is True
    assert result.to_document()["runner-diagnostic"] == {
        "exit-classification": "runner-failed-after-mutation-start",
        "upstream-status": None,
        "exception-category": exception_category,
        "request-correlation-digest": None,
    }


@pytest.mark.parametrize(
    (
        "exit_classification",
        "upstream_status",
        "exception_category",
        "request_correlation_digest",
    ),
    [
        ("runner-failed-after-mutation-start", None, None, None),
        ("runner-failed-before-mutation", 500, None, RESPONSE_DIGEST),
        (
            "runner-failed-after-action-start",
            None,
            "TimeoutError",
            RESPONSE_DIGEST,
        ),
        ("runner-malformed-before-mutation", 500, None, RESPONSE_DIGEST),
    ],
    ids=[
        "empty",
        "status-before-mutation",
        "transport-after-action-start",
        "status-malformed-before-mutation",
    ],
)
def test_runner_diagnostic_rejects_empty_or_pre_mutation_request_binding(
    exit_classification: str,
    upstream_status: int | None,
    exception_category: str | None,
    request_correlation_digest: str | None,
) -> None:
    with pytest.raises(ValueError, match="diagnostic"):
        AcceptanceRunnerDiagnostic(
            exit_classification=exit_classification,
            upstream_status=upstream_status,
            exception_category=exception_category,
            request_correlation_digest=request_correlation_digest,
        )


@pytest.mark.parametrize(
    "upstream_status",
    [100, 200, 500, 599],
    ids=["status-100", "status-200", "status-500", "status-599"],
)
def test_runner_diagnostic_rejects_unbound_noncreated_status(
    upstream_status: int,
) -> None:
    with pytest.raises(ValueError, match="diagnostic"):
        AcceptanceRunnerDiagnostic(
            exit_classification="runner-failed-after-mutation-start",
            upstream_status=upstream_status,
            exception_category=None,
            request_correlation_digest=None,
        )


@pytest.mark.parametrize(
    ("field", "replacement"),
    [
        ("request-digest", "sha256:" + ("0" * 64)),
        ("upstream-status", 202),
        ("response-identity-digest", "sha256:" + ("0" * 64)),
    ],
)
def test_protocol_confirmed_rejects_substituted_exchange_facts(
    tmp_path: Path,
    field: str,
    replacement: object,
) -> None:
    scenario = "absent-create-readback"
    tarball = f"{scenario}-artifact".encode()
    proof = _proof(tarball, scenario=scenario)
    runner_document = _protocol_confirmed_runner_document(proof)
    runner_document[field] = replacement

    result = _run_probe(
        tmp_path,
        scenario=scenario,
        runner=ScriptedRunner(runner_document),
        observations=[_absent(), _exact_readback(tarball, scenario=scenario)],
    )

    assert result.mutation_classification == "incomplete"
    assert result.result != "protocol-confirmed"
    assert result.validated_request_proof is None


def test_protocol_confirmed_rejects_malformed_proof(
    tmp_path: Path,
) -> None:
    scenario = "absent-create-readback"
    tarball = f"{scenario}-artifact".encode()
    proof = _proof(tarball, scenario=scenario)
    runner_document = _protocol_confirmed_runner_document(proof)
    runner_document["validated-request-proof"] = proof.to_document()

    result = _run_probe(
        tmp_path,
        scenario=scenario,
        runner=ScriptedRunner(runner_document),
        observations=[_absent(), _exact_readback(tarball, scenario=scenario)],
    )

    assert result.mutation_classification == "incomplete"
    assert result.result != "protocol-confirmed"
    assert result.validated_request_proof is None


@pytest.mark.parametrize(
    ("upstream_status", "exception_category"),
    [
        (200, None),
        (201, None),
        (202, None),
        (409, None),
        (500, None),
        (None, "TimeoutError"),
        (None, "OSError"),
        (None, "HTTPException"),
    ],
    ids=[
        "status-200",
        "status-201",
        "status-202",
        "status-409",
        "status-500",
        "transport-timeout",
        "transport-os-error",
        "transport-http-exception",
    ],
)
def test_acceptance_probe_preserves_non_authoritative_upstream_diagnostic_matrix_with_incomplete_readback(  # noqa: E501
    tmp_path: Path,
    upstream_status: int | None,
    exception_category: str | None,
) -> None:
    scenario = "absent-create-readback"
    tarball = f"{scenario}-artifact".encode()
    request_digest = "sha256:" + ("d" * 64)
    upstream_diagnostic = {
        "upstream-status": upstream_status,
        "exception-category": exception_category,
        "request-correlation-digest": request_digest,
    }

    result = _run_probe(
        tmp_path,
        scenario=scenario,
        runner=ScriptedRunner(
            {
                "outcome": "failed",
                "action-executed": True,
                "mutation-started": True,
                "upstream-diagnostic": upstream_diagnostic,
            }
        ),
        observations=[
            _absent(),
            _exact_readback(tarball, scenario=scenario),
        ],
    )
    document = result.to_document()
    expected_runner_diagnostic = {
        "exit-classification": "runner-failed-after-mutation-start",
        **upstream_diagnostic,
    }

    assert result.result == "runner-failed-after-mutation-start"
    assert result.mutation_classification == "incomplete"
    assert result.pre_state == "absent"
    assert result.post_state == "exact"
    assert result.action_executed is True
    assert result.mutation_started is True
    assert result.response_identity_digest == RESPONSE_DIGEST
    assert result.content_sha512 == (
        "sha512:" + hashlib.sha512(tarball).hexdigest()
    )
    assert result.diagnostics == ("runner-did-not-prove-controlled-outcome",)
    assert result.validated_request_proof is None
    assert "validated-request-proof" not in document
    actual_runner_diagnostic = document.get("runner-diagnostic")
    if actual_runner_diagnostic is None:
        pytest.fail(
            "Adapter omitted the expected non-authoritative runner-diagnostic",
            pytrace=False,
        )
    assert actual_runner_diagnostic == expected_runner_diagnostic
    assert set(actual_runner_diagnostic) == {
        "exit-classification",
        "upstream-status",
        "exception-category",
        "request-correlation-digest",
    }
    assert document["diagnostics"] == [
        "runner-did-not-prove-controlled-outcome"
    ]
    assert "upstream-diagnostic" not in document


@pytest.mark.parametrize(
    "upstream_diagnostic",
    [
        {
            "upstream-status": None,
            "exception-category": None,
            "request-correlation-digest": None,
        },
        {
            "upstream-status": 500,
            "exception-category": None,
            "request-correlation-digest": None,
        },
    ],
    ids=["empty", "status-without-request"],
)
def test_acceptance_probe_rejects_empty_or_unbound_raw_upstream_diagnostic(
    tmp_path: Path,
    upstream_diagnostic: dict[str, object],
) -> None:
    scenario = "absent-create-readback"
    tarball = f"{scenario}-artifact".encode()

    with pytest.raises(ValueError, match="upstream diagnostic"):
        _run_probe(
            tmp_path,
            scenario=scenario,
            runner=ScriptedRunner(
                {
                    "outcome": "failed",
                    "action-executed": True,
                    "mutation-started": True,
                    "upstream-diagnostic": upstream_diagnostic,
                }
            ),
            observations=[
                _absent(),
                _exact_readback(tarball, scenario=scenario),
            ],
        )


@pytest.mark.parametrize(
    "outcome",
    ["protocol-confirmed", "failed", "malformed-action-facts"],
    ids=["protocol-confirmed", "failed", "malformed-action-facts"],
)
def test_returned_runner_document_rejects_explicit_null_upstream_diagnostic(
    tmp_path: Path,
    outcome: str,
) -> None:
    scenario = "absent-create-readback"
    tarball = f"{scenario}-artifact".encode()
    runner_document: dict[str, object]
    if outcome == "protocol-confirmed":
        runner_document = _protocol_confirmed_runner_document(
            _proof(tarball, scenario=scenario)
        )
    elif outcome == "failed":
        runner_document = {
            "outcome": "failed",
            "action-executed": True,
            "mutation-started": True,
        }
    else:
        runner_document = {
            "outcome": "failed",
            "action-executed": "not-a-bool",
            "mutation-started": False,
        }
    runner_document["upstream-diagnostic"] = None

    with pytest.raises(ValueError, match="upstream diagnostic"):
        _run_probe(
            tmp_path,
            scenario=scenario,
            runner=ScriptedRunner(runner_document),
            observations=[
                _absent(),
                _exact_readback(tarball, scenario=scenario),
            ],
        )


def test_lost_response_proof_rejects_conflicting_raw_upstream_diagnostic(
    tmp_path: Path,
) -> None:
    scenario = "lost-response"
    tarball = f"{scenario}-artifact".encode()
    proof = _proof(tarball, scenario=scenario)

    with pytest.raises(ValueError, match="does not bind"):
        _run_probe(
            tmp_path,
            scenario=scenario,
            runner=ScriptedRunner(
                {
                    "outcome": "lost-response-processed",
                    "validated-request-proof": proof,
                    "action-executed": True,
                    "mutation-started": True,
                    "upstream-diagnostic": {
                        "upstream-status": 500,
                        "exception-category": None,
                        "request-correlation-digest": ("sha256:" + ("e" * 64)),
                    },
                }
            ),
            observations=[
                _absent(),
                _exact_readback(tarball, scenario=scenario),
            ],
        )
