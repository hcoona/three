"""Commit-10 fixed GitHub Packages acceptance probe scenarios."""

# ruff: noqa: C901, D101, D102, D103, D107, E501, FBT001, PLR0913, PLR0917, PLR2004, PT011, S105, S106, S607, SLF001

from __future__ import annotations

import contextlib
import gzip
import hashlib
import http.client
import io
import json
import subprocess
import tarfile
import urllib.parse
from pathlib import Path
from typing import Any, Self, cast

import pytest
from three_workflow_delivery_v3 import adapters as adapter_package
from three_workflow_delivery_v3 import cli as cli_module
from three_workflow_delivery_v3.adapters.github_packages import (
    ACCEPTANCE_COORDINATES,
    ACCEPTANCE_PACKAGE_COORDINATE,
    ACCEPTANCE_SCENARIO_SPECS,
    ACCEPTANCE_SCENARIOS,
    ACCEPTANCE_TAGS,
    RETRY_2_ACCEPTANCE_PACKAGE_COORDINATE,
    FixedAcceptanceSuiteResult,
    FixedCoordinateAcceptanceProbeResult,
    GitHubPackagesHttpResponse,
    fixed_acceptance_coordinates,
    fixed_acceptance_scenario_specs,
    inspect_fixed_acceptance_tarball,
    run_fixed_acceptance_suite,
    run_fixed_coordinate_acceptance_probe,
)
from three_workflow_delivery_v3.adapters.github_packages import (
    ValidatedAcceptanceRequestProof as AcceptanceRequestProof,
)
from three_workflow_delivery_v3.canonical import JsonValue, canonicalize

RESPONSE_A = "sha256:" + ("a" * 64)
RESPONSE_B = "sha256:" + ("b" * 64)
TARGET = "c" * 40
TIMEOUT_SECONDS = 7.0
MAX_RESPONSE_BYTES = 8192
MAX_OUTPUT_BYTES = 4096
TAGS = {scenario: tag for scenario, _version, tag in ACCEPTANCE_SCENARIO_SPECS}


class RecordingTransport:
    def __init__(self, observations: list[dict[str, Any]]) -> None:
        self.observations = list(observations)
        self.calls: list[tuple[str, str, float, int]] = []

    def observe(
        self,
        coordinate: str,
        tag: str,
        *,
        timeout_seconds: float,
        max_response_bytes: int,
        deadline: float | None = None,
    ) -> dict[str, Any]:
        del deadline
        self.calls.append(
            (coordinate, tag, timeout_seconds, max_response_bytes)
        )
        return self.observations.pop(0)


class ForbiddenMetadataTransport:
    def get(
        self,
        url: str,
        *,
        headers: tuple[tuple[str, str], ...],
        timeout: float,
        max_bytes: int,
    ) -> GitHubPackagesHttpResponse:
        del headers, timeout, max_bytes
        return GitHubPackagesHttpResponse(
            status=403,
            url=url,
            headers=(),
            body=b"forbidden",
        )


class ControlledRunner:
    def __init__(
        self,
        *,
        outcome: object = "created",
        error: BaseException | None = None,
        action_executed: bool = True,
        mutation_started: bool = True,
    ) -> None:
        self.outcome = outcome
        self.error = error
        self.action_executed = action_executed
        self.mutation_started = mutation_started
        self.calls: list[tuple[str, tuple[str, ...], dict[str, str]]] = []

    def run(
        self,
        argv: tuple[str, ...],
        *,
        env: dict[str, str],
        timeout_seconds: float,
        max_output_bytes: int,
    ) -> dict[str, Any]:
        return self.run_scenario(
            "single",
            argv,
            env=env,
            timeout_seconds=timeout_seconds,
            max_output_bytes=max_output_bytes,
        )

    def run_scenario(
        self,
        scenario: str,
        argv: tuple[str, ...],
        *,
        env: dict[str, str],
        timeout_seconds: float,
        max_output_bytes: int,
    ) -> dict[str, Any]:
        assert 0 < timeout_seconds <= TIMEOUT_SECONDS
        assert max_output_bytes == MAX_OUTPUT_BYTES
        self.calls.append((scenario, argv, dict(env)))
        if self.error is not None:
            raise self.error
        return {
            "outcome": self.outcome,
            "response-identity-digest": RESPONSE_A,
            "action-executed": self.action_executed,
            "mutation-started": self.mutation_started,
        }


class MalformedRunner(ControlledRunner):
    def run_scenario(
        self,
        scenario: str,
        argv: tuple[str, ...],
        *,
        env: dict[str, str],
        timeout_seconds: float,
        max_output_bytes: int,
    ) -> dict[str, Any]:
        del scenario, argv, env, timeout_seconds, max_output_bytes
        return {"stdout": "npm failed before create", "stderr": "E404 maybe"}


class PreStartFailureRunner(ControlledRunner):
    def run_scenario(
        self,
        scenario: str,
        argv: tuple[str, ...],
        *,
        env: dict[str, str],
        timeout_seconds: float,
        max_output_bytes: int,
    ) -> dict[str, Any]:
        del scenario, argv, env, timeout_seconds, max_output_bytes
        message = "runner failed before process creation"
        raise OSError(message)


def _absent() -> dict[str, Any]:
    return {"state": "absent", "response-identity-digest": RESPONSE_A}


def _state(
    state: str,
    *,
    scenario: str,
    content: str,
) -> dict[str, Any]:
    return {
        "state": state,
        "version": ACCEPTANCE_COORDINATES[scenario].rsplit("@", 1)[1],
        "tag": TAGS[scenario],
        "content-sha512": content,
        "response-identity-digest": RESPONSE_B,
    }


def _run(
    tmp_path: Path,
    *,
    scenario: str,
    observations: list[dict[str, Any]],
    runner: object,
    max_output_bytes: int = MAX_OUTPUT_BYTES,
) -> tuple[Any, RecordingTransport, Path]:
    tarball = tmp_path / f"{scenario}.tgz"
    tarball.write_bytes(scenario.encode())
    transport = RecordingTransport(observations)
    result = run_fixed_coordinate_acceptance_probe(
        scenario=scenario,
        package_coordinate=ACCEPTANCE_COORDINATES[scenario],
        tag=TAGS[scenario],
        tarball=tarball,
        tarball_sha512=(
            f"sha512:{hashlib.sha512(tarball.read_bytes()).hexdigest()}"
        ),
        transport=transport,
        runner=runner,
        timeout_seconds=TIMEOUT_SECONDS,
        max_response_bytes=MAX_RESPONSE_BYTES,
        max_output_bytes=max_output_bytes,
    )
    return result, transport, tarball


def test_retry_2_suite_resolves_only_the_reviewed_coordinate_block() -> None:
    coordinates = fixed_acceptance_coordinates(
        RETRY_2_ACCEPTANCE_PACKAGE_COORDINATE
    )

    assert coordinates == {
        "absent-create-readback": (
            "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.5"
        ),
        "exact": ("@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.5"),
        "identical-race": (
            "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.6"
        ),
        "differing-race": (
            "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.7"
        ),
        "lost-response": (
            "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.8"
        ),
    }
    with pytest.raises(ValueError, match="not a reviewed fixed suite"):
        fixed_acceptance_coordinates(
            "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.17"
        )


def test_retry_2_suite_executes_with_retry_coordinate_and_tag(
    tmp_path: Path,
) -> None:
    coordinate = fixed_acceptance_coordinates(
        RETRY_2_ACCEPTANCE_PACKAGE_COORDINATE
    )["absent-create-readback"]
    tarball = tmp_path / "retry-2.tgz"
    tarball.write_bytes(b"retry-2-absent-create-readback")
    content = f"sha512:{hashlib.sha512(tarball.read_bytes()).hexdigest()}"
    transport = RecordingTransport(
        [
            _absent(),
            {
                "state": "exact",
                "version": "0.0.0-wdv3-acceptance.5",
                "tag": "wdv3-acceptance-5",
                "content-sha512": content,
                "response-identity-digest": RESPONSE_B,
            },
        ]
    )

    result = run_fixed_acceptance_suite(
        suite="absent-create-readback",
        tarballs={"absent-create-readback": tarball},
        transport=transport,
        runner=ControlledRunner(),
        timeout_seconds=TIMEOUT_SECONDS,
        max_response_bytes=MAX_RESPONSE_BYTES,
        max_output_bytes=MAX_OUTPUT_BYTES,
        base_package_coordinate=RETRY_2_ACCEPTANCE_PACKAGE_COORDINATE,
    )

    assert result.scenarios[0].package_coordinate == coordinate
    assert result.scenarios[0].tag == "wdv3-acceptance-5"


def test_acceptance_proof_rejects_cross_profile_coordinate_and_tag() -> None:
    with pytest.raises(
        ValueError,
        match="coordinate or tag is not fixed",
    ):
        AcceptanceRequestProof.from_validated_exchange(
            raw_request=b"request",
            tarball=b"tarball",
            package_coordinate=(
                "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.5"
            ),
            tag="wdv3-acceptance-1",
            upstream_status=201,
            selected_headers={"Content-Type": "application/json"},
            response_body=b'{"ok":true}',
        )


def test_retry_2_npm_runner_uses_retry_coordinate_for_lost_response(
    tmp_path: Path,
) -> None:
    runner = cli_module._AcceptanceNpmRunner(
        tmp_path / ".npmrc",
        contender_tarballs={},
        base_package_coordinate=RETRY_2_ACCEPTANCE_PACKAGE_COORDINATE,
    )

    assert runner._coordinates["lost-response"].endswith("wdv3-acceptance.8")


def test_absent_proof_free_created_remains_incomplete(
    tmp_path: Path,
) -> None:
    runner = ControlledRunner()
    tarball = tmp_path / "precompute"
    tarball.write_bytes(b"absent-create-readback")
    content = f"sha512:{hashlib.sha512(tarball.read_bytes()).hexdigest()}"
    tarball.unlink()
    result, transport, actual_tarball = _run(
        tmp_path,
        scenario="absent-create-readback",
        observations=[
            _absent(),
            _state(
                "exact",
                scenario="absent-create-readback",
                content=content,
            ),
        ],
        runner=runner,
    )

    assert result.result == "created-without-request-proof"
    assert result.pre_state == "absent"
    assert result.post_state == "exact"
    assert result.mutation_classification == "incomplete"
    assert runner.calls[0][0] == "absent-create-readback"
    command = runner.calls[0][1]
    assert command[:3] == ("npm", "publish", str(actual_tarball))
    assert "--tag" in command
    assert (
        transport.calls
        == [
            (
                ACCEPTANCE_COORDINATES["absent-create-readback"],
                TAGS["absent-create-readback"],
                TIMEOUT_SECONDS,
                MAX_RESPONSE_BYTES,
            )
        ]
        * 2
    )
    assert result.to_document() == {
        "schema": "workflow-delivery/v3/fixed-coordinate-acceptance-probe",
        "scenario": "absent-create-readback",
        "package-coordinate": ACCEPTANCE_COORDINATES["absent-create-readback"],
        "tag": TAGS["absent-create-readback"],
        "pre-state": "absent",
        "post-state": "exact",
        "result": "created-without-request-proof",
        "mutation-classification": "incomplete",
        "action-executed": True,
        "mutation-started": True,
        "response-identity-digest": RESPONSE_B,
        "content-sha512": content,
        "diagnostics": ["request-bound-created-proof-required"],
    }


def test_absent_preexisting_exact_requires_new_fixed_coordinate(
    tmp_path: Path,
) -> None:
    tarball = tmp_path / "absent-create-readback.tgz"
    tarball.write_bytes(b"absent-create-readback")
    content = f"sha512:{hashlib.sha512(tarball.read_bytes()).hexdigest()}"
    runner = ControlledRunner()
    result, transport, _ = _run(
        tmp_path,
        scenario="absent-create-readback",
        observations=[
            _state(
                "exact",
                scenario="absent-create-readback",
                content=content,
            )
        ],
        runner=runner,
    )

    assert result.result == "fixed-coordinate-already-exists"
    assert result.mutation_classification == "incomplete"
    assert result.diagnostics == (
        "absent-state-not-observed",
        "new-fixed-coordinate-required",
    )
    assert runner.calls == []
    assert len(transport.calls) == 1


@pytest.mark.parametrize(
    ("scenario", "post_state", "outcome", "expected", "classification"),
    [
        (
            "identical-race",
            "exact",
            "create-conflict",
            "identical-race-exact",
            "complete",
        ),
        (
            "differing-race",
            "conflicting",
            "create-conflict",
            "differing-race-conflict",
            "complete",
        ),
    ],
)
def test_races_use_explicit_controlled_scenario_seam(
    tmp_path: Path,
    scenario: str,
    post_state: str,
    outcome: str,
    expected: str,
    classification: str,
) -> None:
    probe_bytes = scenario.encode()
    content = f"sha512:{hashlib.sha512(probe_bytes).hexdigest()}"
    post_content = content if post_state == "exact" else "sha512:" + ("f" * 128)
    runner = (
        ExplicitFactRunner(
            outcome=outcome,
            executed=True,
            started=True,
            contender_outcomes=("created", "create-conflict"),
            winner_content_sha512=post_content,
            race_overlap_proven=True,
        )
        if scenario == "differing-race"
        else ControlledRunner(outcome=outcome)
    )
    result, _, _ = _run(
        tmp_path,
        scenario=scenario,
        observations=[
            _absent(),
            _state(post_state, scenario=scenario, content=post_content),
        ],
        runner=runner,
    )

    assert result.result == expected
    assert result.mutation_classification == classification
    assert runner.calls[0][0] == scenario
    assert len(runner.calls) == 1
    assert result.to_document() == {
        "schema": "workflow-delivery/v3/fixed-coordinate-acceptance-probe",
        "scenario": scenario,
        "package-coordinate": ACCEPTANCE_COORDINATES[scenario],
        "tag": TAGS[scenario],
        "pre-state": "absent",
        "post-state": post_state,
        "result": expected,
        "mutation-classification": classification,
        "action-executed": True,
        "mutation-started": True,
        "response-identity-digest": RESPONSE_B,
        "content-sha512": post_content,
        "diagnostics": (
            ["identical-race-exact"]
            if scenario == "identical-race"
            else ["conflicting-remote-bytes-or-tag"]
        ),
    }


def test_lost_response_is_deliberately_injected_after_mutation_start(
    tmp_path: Path,
) -> None:
    runner = ExplicitFactRunner(
        executed=True,
        started=True,
        error=RuntimeError("deliberate response loss after mutation start"),
    )
    result, _, _ = _run(
        tmp_path,
        scenario="lost-response",
        observations=[
            _absent(),
            {"state": "unknown", "response-identity-digest": RESPONSE_B},
        ],
        runner=runner,
    )

    assert runner.calls[0][0] == "lost-response"
    assert result.result == "lost-response"
    assert result.mutation_classification == "unknown"
    assert result.diagnostics == (
        "mutation-may-have-started",
        "human-reconciliation-required",
    )


class ExplicitFactRunner(ControlledRunner):
    def __init__(
        self,
        *,
        outcome: str = "created",
        executed: bool,
        started: bool,
        error: BaseException | None = None,
        contender_outcomes: tuple[str, str] | None = None,
        winner_content_sha512: str | None = None,
        race_overlap_proven: bool = False,
    ) -> None:
        super().__init__(outcome=outcome, error=error)
        self.executed = executed
        self.started = started
        self.contender_outcomes = contender_outcomes
        self.winner_content_sha512 = winner_content_sha512
        self.race_overlap_proven = race_overlap_proven

    def run_scenario(
        self,
        scenario: str,
        argv: tuple[str, ...],
        *,
        env: dict[str, str],
        timeout_seconds: float,
        max_output_bytes: int,
    ) -> dict[str, Any]:
        if self.error is not None:
            self.calls.append((scenario, argv, dict(env)))
            self.error.action_executed = self.executed  # type: ignore[attr-defined]
            self.error.mutation_started = self.started  # type: ignore[attr-defined]
            raise self.error
        result = super().run_scenario(
            scenario,
            argv,
            env=env,
            timeout_seconds=timeout_seconds,
            max_output_bytes=max_output_bytes,
        )
        result["action-executed"] = self.executed
        result["mutation-started"] = self.started
        if self.contender_outcomes is not None:
            result["contender-outcomes"] = list(self.contender_outcomes)
        if self.winner_content_sha512 is not None:
            result["winner-content-sha512"] = self.winner_content_sha512
        if self.contender_outcomes is not None:
            result["race-overlap-proven"] = self.race_overlap_proven
        return result


def _action_document(result: Any) -> dict[str, Any]:
    suite = FixedAcceptanceSuiteResult(suite="regression", scenarios=(result,))
    document = cast("dict[str, Any]", suite.to_document())
    return cast("dict[str, Any]", document["scenarios"][0]["action"])


@pytest.mark.parametrize(
    ("scenario", "runner", "post", "expected"),
    [
        (
            "absent-create-readback",
            ExplicitFactRunner(
                executed=False,
                started=False,
                error=OSError("failed before process start"),
            ),
            _absent(),
            {"executed": False, "mutation-started": False},
        ),
        (
            "lost-response",
            ExplicitFactRunner(
                executed=True,
                started=True,
                error=RuntimeError("response dropped after processed boundary"),
            ),
            {"state": "unknown", "response-identity-digest": RESPONSE_B},
            {"executed": True, "mutation-started": True},
        ),
        (
            "identical-race",
            ExplicitFactRunner(
                executed=True,
                started=True,
                error=TimeoutError("bounded runner timed out after start"),
            ),
            {"state": "unknown", "response-identity-digest": RESPONSE_B},
            {"executed": True, "mutation-started": True},
        ),
    ],
)
def test_probe_action_facts_follow_explicit_runner_startedness(
    tmp_path: Path,
    scenario: str,
    runner: ExplicitFactRunner,
    post: dict[str, Any],
    expected: dict[str, bool],
) -> None:
    result, _, _ = _run(
        tmp_path,
        scenario=scenario,
        observations=[_absent(), post],
        runner=runner,
    )

    assert _action_document(result) == {
        "operation": "npm-publish-create-only",
        **expected,
    }


def test_exact_no_action_records_executed_and_started_false(
    tmp_path: Path,
) -> None:
    runner = ExplicitFactRunner(executed=True, started=True)
    result, _, _ = _run(
        tmp_path,
        scenario="exact",
        observations=[
            _state(
                "exact",
                scenario="exact",
                content="sha512:" + hashlib.sha512(b"exact").hexdigest(),
            )
        ],
        runner=runner,
    )

    assert _action_document(result) == {
        "operation": "npm-publish-create-only",
        "executed": False,
        "mutation-started": False,
    }
    assert runner.calls == []


def test_exact_absent_prestate_fails_before_mutation(
    tmp_path: Path,
) -> None:
    runner = ControlledRunner()
    result, transport, _ = _run(
        tmp_path,
        scenario="exact",
        observations=[_absent()],
        runner=runner,
    )

    assert result.pre_state == "absent"
    assert result.post_state == "absent"
    assert result.result == "exact-state-absent"
    assert result.mutation_classification == "incomplete"
    assert result.action_executed is False
    assert result.mutation_started is False
    assert result.diagnostics == (
        "exact-state-not-observed",
        "human-reconciliation-required",
    )
    assert runner.calls == []
    assert len(transport.calls) == 1


@pytest.mark.parametrize(
    ("winner_is_primary", "post_state"),
    [(True, "exact"), (False, "conflicting")],
)
def test_differing_race_requires_one_created_one_conflict_in_either_order(
    tmp_path: Path,
    winner_is_primary: bool,
    post_state: str,
) -> None:
    primary_sha = "sha512:" + hashlib.sha512(b"differing-race").hexdigest()
    contender_sha = "sha512:" + ("f" * 128)
    winner_sha = primary_sha if winner_is_primary else contender_sha
    outcomes = (
        ("created", "create-conflict")
        if winner_is_primary
        else ("create-conflict", "created")
    )
    result, _, _ = _run(
        tmp_path,
        scenario="differing-race",
        observations=[
            _absent(),
            _state(
                post_state,
                scenario="differing-race",
                content=winner_sha,
            ),
        ],
        runner=ExplicitFactRunner(
            outcome="create-conflict",
            executed=True,
            started=True,
            contender_outcomes=outcomes,
            winner_content_sha512=winner_sha,
            race_overlap_proven=True,
        ),
    )

    assert result.mutation_classification == "complete"
    assert result.content_sha512 == winner_sha
    assert result.result == "differing-race-conflict"


def test_differing_race_readback_must_equal_actual_winner(
    tmp_path: Path,
) -> None:
    claimed_winner = "sha512:" + ("e" * 128)
    observed_other = "sha512:" + ("f" * 128)
    result, _, _ = _run(
        tmp_path,
        scenario="differing-race",
        observations=[
            _absent(),
            _state(
                "conflicting",
                scenario="differing-race",
                content=observed_other,
            ),
        ],
        runner=ExplicitFactRunner(
            outcome="create-conflict",
            executed=True,
            started=True,
            contender_outcomes=("create-conflict", "created"),
            winner_content_sha512=claimed_winner,
            race_overlap_proven=True,
        ),
    )

    assert result.content_sha512 == observed_other
    assert result.mutation_classification != "complete"
    assert "winner" in " ".join(result.diagnostics)


@pytest.mark.parametrize(
    "outcomes",
    [
        ("created", "created"),
        ("create-conflict", "create-conflict"),
        ("created", "failed"),
        ("failed", "create-conflict"),
    ],
)
def test_differing_race_rejects_every_nonexclusive_outcome(
    tmp_path: Path,
    outcomes: tuple[str, str],
) -> None:
    remote_sha = "sha512:" + ("f" * 128)
    result, _, _ = _run(
        tmp_path,
        scenario="differing-race",
        observations=[
            _absent(),
            _state(
                "conflicting",
                scenario="differing-race",
                content=remote_sha,
            ),
        ],
        runner=ExplicitFactRunner(
            outcome="create-conflict",
            executed=True,
            started=True,
            contender_outcomes=outcomes,
            winner_content_sha512=remote_sha,
            race_overlap_proven=True,
        ),
    )

    assert result.mutation_classification != "complete"
    assert result.result != "differing-race-conflict"


def test_lost_response_runner_does_not_trust_process_output_as_upstream_proof(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    events: list[str] = []

    class BoundaryProcess:
        returncode = 0

        def poll(self) -> int | None:
            events.append("poll")
            return None

        def kill(self) -> None:
            events.append("kill")

        def communicate(self, timeout: float | None = None) -> tuple[str, str]:
            del timeout
            events.append("forwarded-and-processed")
            return "WDV3_PUBLISH_FORWARDED_AND_PROCESSED\n", ""

    monkeypatch.setattr(
        cli_module.subprocess,
        "Popen",
        lambda *_args, **_kwargs: BoundaryProcess(),
    )
    runner = cli_module._AcceptanceNpmRunner(
        tmp_path / ".npmrc",
        contender_tarballs={},
        token="dedicated-token",
    )

    result = runner.run_scenario(
        "lost-response",
        (
            "npm",
            "publish",
            str(tmp_path / "lost-response.tgz"),
            "--tag",
            "wdv3-acceptance-4",
        ),
        env={},
        timeout_seconds=TIMEOUT_SECONDS,
        max_output_bytes=MAX_OUTPUT_BYTES,
    )

    assert result["outcome"] != "lost-response-processed"
    assert "forwarded-and-processed" in events
    assert events.index("forwarded-and-processed") < events.index("kill")


@pytest.mark.parametrize("upstream_status", [200, 201])
def test_lost_response_runner_proxies_scope_registry_and_real_upstream_auth_only(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    upstream_status: int,
) -> None:
    dedicated_token = "dedicated-token"
    tarball_bytes = _acceptance_tarball(
        version="0.0.0-wdv3-acceptance.4",
        repository_url="git+https://github.com/hcoona/three.git",
        target_sha=TARGET,
    )
    tarball_path = tmp_path / "lost-response.tgz"
    tarball_path.write_bytes(tarball_bytes)
    npm_config = tmp_path / ".npmrc"
    npm_config.write_text(
        (
            "@hcoona:registry=https://npm.pkg.github.com\n"
            f"//npm.pkg.github.com/:_authToken={dedicated_token}\n"
            "ignore-scripts=true\n"
        ),
        encoding="utf-8",
    )
    upstream_requests: list[dict[str, object]] = []
    npm_invocations: list[dict[str, object]] = []

    class FakeUpstreamResponse:
        status = upstream_status

        def read(self, _size: int | None = None) -> bytes:
            return b'{"ok":true}'

        def getheaders(self) -> list[tuple[str, str]]:
            return []

    class FakeUpstreamConnection:
        def __init__(self, host: str, *, timeout: float) -> None:
            self.host = host
            self.timeout = timeout

        def request(
            self,
            method: str,
            path: str,
            *,
            body: bytes,
            headers: dict[str, str],
        ) -> None:
            upstream_requests.append(
                {
                    "host": self.host,
                    "timeout": self.timeout,
                    "method": method,
                    "path": path,
                    "body": body,
                    "headers": dict(headers),
                }
            )

        def getresponse(self) -> FakeUpstreamResponse:
            return FakeUpstreamResponse()

        def close(self) -> None:
            return None

    class FakeNpmProcess:
        returncode: int | None = None

        def __init__(
            self,
            argv: tuple[str, ...],
            *,
            env: dict[str, str],
        ) -> None:
            self.argv = argv
            self.env = env
            config_path = Path(env["NPM_CONFIG_USERCONFIG"])
            parsed = urllib.parse.urlsplit(argv[argv.index("--registry") + 1])
            npm_invocations.append(
                {
                    "argv": argv,
                    "env": dict(env),
                    "config-path": config_path,
                    "config-text": config_path.read_text(encoding="utf-8"),
                }
            )
            connection = http.client.HTTPConnection(
                cast("str", parsed.hostname),
                parsed.port,
                timeout=TIMEOUT_SECONDS,
            )
            try:
                connection.request(
                    "PUT",
                    "/@hcoona%2fhcoona-release-smoke-npm",
                    body=_adversarial_publish_body(tarball_bytes),
                    headers={"Content-Type": "application/json"},
                )
                connection.getresponse().read()
            except (http.client.HTTPException, OSError):
                pass
            finally:
                connection.close()

        def communicate(
            self,
            timeout: float | None = None,
        ) -> tuple[str, str]:
            del timeout
            return "WDV3_PUBLISH_FORWARDED_AND_PROCESSED\n", ""

        def poll(self) -> int | None:
            return self.returncode

        def kill(self) -> None:
            self.returncode = -9

    def fake_popen(
        argv: tuple[str, ...],
        *,
        stdout: object,
        stderr: object,
        text: bool,
        env: dict[str, str],
    ) -> FakeNpmProcess:
        del stdout, stderr, text
        return FakeNpmProcess(argv, env=env)

    monkeypatch.setattr(
        cli_module.http.client, "HTTPSConnection", FakeUpstreamConnection
    )
    monkeypatch.setattr(cli_module.subprocess, "Popen", fake_popen)
    runner = cli_module._AcceptanceNpmRunner(
        npm_config,
        contender_tarballs={},
        token=dedicated_token,
    )

    result = runner.run_scenario(
        "lost-response",
        (
            "npm",
            "publish",
            str(tarball_path),
            "--tag",
            "wdv3-acceptance-4",
            "--registry",
            "https://npm.pkg.github.com",
            "--ignore-scripts",
        ),
        env={"NPM_CONFIG_IGNORE_SCRIPTS": "true"},
        timeout_seconds=TIMEOUT_SECONDS,
        max_output_bytes=MAX_OUTPUT_BYTES,
    )

    assert len(npm_invocations) == 1
    invocation = cast("dict[str, Any]", npm_invocations[0])
    argv_text = "\n".join(invocation["argv"])
    env_text = "\n".join(
        f"{name}={value}" for name, value in invocation["env"].items()
    )
    local_config = str(invocation["config-text"])
    registry = invocation["argv"][invocation["argv"].index("--registry") + 1]
    registry_host = urllib.parse.urlsplit(registry).netloc
    upstream_headers = upstream_requests[0]["headers"]

    assert urllib.parse.urlsplit(registry).hostname == "127.0.0.1"
    assert isinstance(upstream_headers, dict)
    assert upstream_headers.get("Authorization") == (
        f"Bearer {dedicated_token}"
    )
    assert upstream_requests[0]["host"] == "npm.pkg.github.com"
    assert result["outcome"] == "lost-response-processed"
    assert result["upstream-status"] == upstream_status
    assert str(invocation["config-path"]) != str(npm_config)
    assert f"@hcoona:registry={registry}" in local_config
    assert f"//{registry_host}/:_authToken=" not in local_config
    assert dedicated_token not in argv_text
    assert dedicated_token not in env_text
    assert dedicated_token not in local_config
    assert "******" not in local_config


def test_npm_e404_never_proves_authoritative_absence(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(
        cli_module.subprocess,
        "run",
        lambda argv, **_kwargs: subprocess.CompletedProcess(
            argv,
            1,
            "",
            "npm ERR! code E404\nnpm ERR! package not found",
        ),
    )
    transport = cli_module._AcceptanceNpmTransport(
        tmp_path / ".npmrc",
        token="dedicated-token",
        target_sha=TARGET,
    )
    transport._transport = ForbiddenMetadataTransport()

    observation = transport.observe(
        ACCEPTANCE_COORDINATES["absent-create-readback"],
        TAGS["absent-create-readback"],
        timeout_seconds=TIMEOUT_SECONDS,
        max_response_bytes=MAX_RESPONSE_BYTES,
    )

    assert observation["state"] == "unknown"


def _is_exact_github_api_url(url: str) -> bool:
    parts = urllib.parse.urlsplit(url)
    return parts.scheme == "https" and parts.netloc == "api.github.com"


@pytest.mark.parametrize(
    "url",
    [
        "https://api.github.com.example.invalid/package",
        "https://evil.invalid/api.github.com/package",
        "https://api.github.com:443/package",
        "https://attacker@api.github.com/package",
        "httpsx://api.github.com/package",
    ],
)
def test_exact_github_api_origin_requires_exact_scheme_and_netloc(
    url: str,
) -> None:
    """Reject parsed origins that only resemble the GitHub API origin."""
    parts = urllib.parse.urlsplit(url)

    assert (parts.scheme, parts.netloc) != ("https", "api.github.com")
    assert not _is_exact_github_api_url(url)


def test_acceptance_observation_requires_authenticated_github_package_version_metadata(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    version = "0.0.0-wdv3-acceptance.2"
    lookalike_tarball_url = "https://api.github.com.example.invalid/tar.tgz"
    tarball = _acceptance_tarball(
        version=version,
        repository_url="git+https://github.com/hcoona/three.git",
        target_sha=TARGET,
    )
    monkeypatch.setattr(
        cli_module.subprocess,
        "run",
        lambda argv, **_kwargs: subprocess.CompletedProcess(
            argv,
            0,
            json.dumps(
                {
                    "version": version,
                    "dist": {"tarball": lookalike_tarball_url},
                    "dist-tags": {"wdv3-acceptance-2": version},
                }
            ),
            "",
        ),
    )
    calls: list[tuple[str, tuple[tuple[str, str], ...]]] = []

    class MetadataTransport:
        def get(
            self,
            url: str,
            *,
            headers: tuple[tuple[str, str], ...],
            timeout: float,
            max_bytes: int,
        ) -> GitHubPackagesHttpResponse:
            del timeout, max_bytes
            calls.append((url, headers))
            is_api_url = _is_exact_github_api_url(url)
            if is_api_url and "/versions" not in url:
                body = json.dumps(
                    {
                        "package_type": "npm",
                        "name": "hcoona-release-smoke-npm",
                        "owner": {"login": "hcoona"},
                        "repository": {"full_name": "hcoona/three"},
                    }
                ).encode()
            elif is_api_url:
                body = json.dumps(
                    [
                        {
                            "name": version,
                            "metadata": {
                                "package_type": "npm",
                                "container": {"tags": ["wdv3-acceptance-2"]},
                            },
                        }
                    ]
                ).encode()
            else:
                body = tarball
            return GitHubPackagesHttpResponse(
                status=200,
                url=url,
                headers=(),
                body=body,
            )

    transport = cli_module._AcceptanceNpmTransport(
        tmp_path / ".npmrc",
        token="dedicated-token",
        target_sha=TARGET,
    )
    transport._transport = MetadataTransport()

    observation = transport.observe(
        f"@hcoona/hcoona-release-smoke-npm@{version}",
        "wdv3-acceptance-2",
        timeout_seconds=TIMEOUT_SECONDS,
        max_response_bytes=MAX_RESPONSE_BYTES,
    )

    api_calls = [call for call in calls if _is_exact_github_api_url(call[0])]
    assert len(api_calls) == 2
    assert calls[-1][0] == lookalike_tarball_url
    assert lookalike_tarball_url not in {url for url, _headers in api_calls}
    assert [
        value
        for _, headers in api_calls
        for name, value in headers
        if name == "Authorization"
    ] == ["Bearer " + transport._token] * len(api_calls)
    assert observation["state"] == "exact"
    assert observation["repository"] == "hcoona/three"
    assert observation["version"] == version


@pytest.mark.parametrize(
    ("repository", "metadata_version", "expected_state"),
    [
        ("other/repository", "0.0.0-wdv3-acceptance.2", "unknown"),
        ("hcoona/three", "0.0.0-wdv3-acceptance.9", "absent"),
    ],
)
def test_exact_observation_rejects_wrong_repository_or_version_metadata(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    repository: str,
    metadata_version: str,
    expected_state: str,
) -> None:
    version = "0.0.0-wdv3-acceptance.2"
    monkeypatch.setattr(
        cli_module.subprocess,
        "run",
        lambda argv, **_kwargs: subprocess.CompletedProcess(
            argv,
            0,
            json.dumps(
                {
                    "version": version,
                    "dist": {"tarball": "https://npm.pkg.github.com/tar.tgz"},
                    "dist-tags": {"wdv3-acceptance-2": version},
                }
            ),
            "",
        ),
    )

    class MismatchedMetadataTransport:
        def get(
            self,
            url: str,
            *,
            headers: tuple[tuple[str, str], ...],
            timeout: float,
            max_bytes: int,
        ) -> GitHubPackagesHttpResponse:
            del headers, timeout, max_bytes
            body = json.dumps(
                {
                    "package_type": "npm",
                    "name": "hcoona-release-smoke-npm",
                    "owner": {"login": "hcoona"},
                    "repository": {"full_name": repository},
                }
                if "/versions" not in url
                else [{"name": metadata_version, "metadata": {}}]
            ).encode()
            return GitHubPackagesHttpResponse(
                status=200,
                url=url,
                headers=(),
                body=body,
            )

    transport = cli_module._AcceptanceNpmTransport(
        tmp_path / ".npmrc",
        token="dedicated-token",
        target_sha=TARGET,
    )
    transport._transport = MismatchedMetadataTransport()

    observation = transport.observe(
        f"@hcoona/hcoona-release-smoke-npm@{version}",
        "wdv3-acceptance-2",
        timeout_seconds=TIMEOUT_SECONDS,
        max_response_bytes=MAX_RESPONSE_BYTES,
    )

    assert observation["state"] == expected_state
    assert "content-sha512" not in observation


def test_acceptance_rest_metadata_requires_exact_owner_and_user_package_route(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(
        cli_module.subprocess,
        "run",
        lambda argv, **_kwargs: subprocess.CompletedProcess(
            argv,
            0,
            "{}",
            "",
        ),
    )
    calls: list[str] = []

    class MissingOwnerTransport:
        def get(
            self,
            url: str,
            *,
            headers: tuple[tuple[str, str], ...],
            timeout: float,
            max_bytes: int,
        ) -> GitHubPackagesHttpResponse:
            del headers, timeout, max_bytes
            calls.append(url)
            body = (
                {
                    "package_type": "npm",
                    "name": "hcoona-release-smoke-npm",
                    "repository": {"full_name": "hcoona/three"},
                }
                if "/versions" not in url
                else []
            )
            return GitHubPackagesHttpResponse(
                status=200,
                url=url,
                headers=(),
                body=json.dumps(body).encode(),
            )

    transport = cli_module._AcceptanceNpmTransport(
        tmp_path / ".npmrc",
        token="dedicated-token",
        target_sha=TARGET,
    )
    transport._transport = MissingOwnerTransport()

    observation = transport.observe(
        ACCEPTANCE_COORDINATES["exact"],
        TAGS["exact"],
        timeout_seconds=TIMEOUT_SECONDS,
        max_response_bytes=MAX_RESPONSE_BYTES,
    )

    assert calls[0] == (
        "https://api.github.com/users/hcoona/packages/npm/"
        "hcoona-release-smoke-npm"
    )
    assert observation["state"] == "unknown"


def test_acceptance_rest_pagination_exhaustion_without_terminal_page_is_unknown(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(
        cli_module.subprocess,
        "run",
        lambda argv, **_kwargs: subprocess.CompletedProcess(
            argv,
            0,
            "{}",
            "",
        ),
    )
    version_calls = 0

    class FullPageTransport:
        def get(
            self,
            url: str,
            *,
            headers: tuple[tuple[str, str], ...],
            timeout: float,
            max_bytes: int,
        ) -> GitHubPackagesHttpResponse:
            nonlocal version_calls
            del headers, timeout, max_bytes
            if "/versions" not in url:
                body: object = {
                    "package_type": "npm",
                    "name": "hcoona-release-smoke-npm",
                    "owner": {"login": "hcoona"},
                    "repository": {"full_name": "hcoona/three"},
                }
            else:
                version_calls += 1
                body = [
                    {"name": f"0.0.0-other-{version_calls}-{index}"}
                    for index in range(100)
                ]
            return GitHubPackagesHttpResponse(
                status=200,
                url=url,
                headers=(),
                body=json.dumps(body).encode(),
            )

    transport = cli_module._AcceptanceNpmTransport(
        tmp_path / ".npmrc",
        token="dedicated-token",
        target_sha=TARGET,
    )
    transport._transport = FullPageTransport()

    observation = transport.observe(
        ACCEPTANCE_COORDINATES["exact"],
        TAGS["exact"],
        timeout_seconds=TIMEOUT_SECONDS,
        max_response_bytes=MAX_RESPONSE_BYTES,
    )

    assert version_calls == 100
    assert observation["state"] == "unknown"


def test_lost_response_exact_post_readback_remains_unknown_without_proxy_proof(
    tmp_path: Path,
) -> None:
    runner = ExplicitFactRunner(
        executed=True,
        started=True,
        error=RuntimeError("deliberate response loss after mutation start"),
    )
    tarball = tmp_path / "lost-response.tgz"
    tarball.write_bytes(b"lost-response")
    content = f"sha512:{hashlib.sha512(tarball.read_bytes()).hexdigest()}"
    result, _, _ = _run(
        tmp_path,
        scenario="lost-response",
        observations=[
            _absent(),
            _state("exact", scenario="lost-response", content=content),
        ],
        runner=runner,
    )

    assert runner.calls[0][0] == "lost-response"
    assert result.post_state == "exact"
    assert result.result == "lost-response"
    assert result.mutation_classification == "unknown"
    assert result.action_executed is True
    assert result.mutation_started is True


def test_lost_response_unknown_post_readback_stays_unknown(
    tmp_path: Path,
) -> None:
    runner = ExplicitFactRunner(
        executed=True,
        started=True,
        error=RuntimeError("deliberate response loss after mutation start"),
    )
    result, _, _ = _run(
        tmp_path,
        scenario="lost-response",
        observations=[
            _absent(),
            {"state": "unknown", "response-identity-digest": RESPONSE_B},
        ],
        runner=runner,
    )

    assert result.post_state == "unknown"
    assert result.mutation_classification == "unknown"
    assert result.result == "lost-response"


def test_lost_response_exact_readback_requires_proof_bearing_runner_outcome(
    tmp_path: Path,
) -> None:
    runner = ControlledRunner(error=RuntimeError("generic npm failure"))
    tarball = tmp_path / "lost-response.tgz"
    tarball.write_bytes(b"lost-response")
    content = f"sha512:{hashlib.sha512(tarball.read_bytes()).hexdigest()}"

    result, _, _ = _run(
        tmp_path,
        scenario="lost-response",
        observations=[
            _absent(),
            _state("exact", scenario="lost-response", content=content),
        ],
        runner=runner,
    )

    assert result.result != "lost-response-exact-after-start"
    assert result.mutation_classification != "complete"
    assert "mutation-started-and-readback-exact" not in result.diagnostics


def test_lost_response_exact_readback_rejects_unbound_outcome_string(
    tmp_path: Path,
) -> None:
    runner = ControlledRunner(outcome="lost-response-processed")
    tarball = tmp_path / "lost-response.tgz"
    tarball.write_bytes(b"lost-response")
    content = f"sha512:{hashlib.sha512(tarball.read_bytes()).hexdigest()}"

    result, _, _ = _run(
        tmp_path,
        scenario="lost-response",
        observations=[
            _absent(),
            _state("exact", scenario="lost-response", content=content),
        ],
        runner=runner,
    )

    assert runner.calls[0][0] == "lost-response"
    assert result.result == "lost-response"
    assert result.mutation_classification == "unknown"
    assert result.action_executed is True
    assert result.mutation_started is True
    assert result.diagnostics == (
        "mutation-may-have-started",
        "human-reconciliation-required",
    )


@pytest.mark.parametrize(
    ("mutation_started", "expected_result"),
    [
        (False, "runner-failed-after-action-start"),
        (True, "runner-failed-after-mutation-start"),
    ],
)
def test_failed_runner_after_action_start_preserves_startedness(
    tmp_path: Path,
    mutation_started: bool,
    expected_result: str,
) -> None:
    runner = ControlledRunner(
        outcome="failed",
        mutation_started=mutation_started,
    )
    tarball = tmp_path / "precompute"
    tarball.write_bytes(b"absent-create-readback")
    content = f"sha512:{hashlib.sha512(tarball.read_bytes()).hexdigest()}"
    tarball.unlink()

    result, _, _ = _run(
        tmp_path,
        scenario="absent-create-readback",
        observations=[
            _absent(),
            _state(
                "exact",
                scenario="absent-create-readback",
                content=content,
            ),
        ],
        runner=runner,
    )

    assert result.result == expected_result
    assert result.mutation_classification == "incomplete"
    assert result.action_executed is True
    assert result.mutation_started is mutation_started
    assert result.post_state == "exact"
    assert result.diagnostics == ("runner-did-not-prove-controlled-outcome",)


@pytest.mark.parametrize(
    ("runner", "expected_result"),
    [
        (
            ExplicitFactRunner(
                executed=True,
                started=False,
                error=OSError("failure after process start"),
            ),
            "runner-failed-after-action-start",
        ),
        (
            ExplicitFactRunner(
                executed=True,
                started=True,
                error=OSError("failure after mutation start"),
            ),
            "runner-failed-after-mutation-start",
        ),
    ],
)
def test_exception_failure_preserves_startedness_label(
    tmp_path: Path,
    runner: ExplicitFactRunner,
    expected_result: str,
) -> None:
    result, _, _ = _run(
        tmp_path,
        scenario="absent-create-readback",
        observations=[_absent(), _absent()],
        runner=runner,
    )

    assert result.result == expected_result
    assert result.mutation_classification == "incomplete"
    assert result.diagnostics == ("runner-did-not-prove-controlled-outcome",)


@pytest.mark.parametrize("error_type", [ValueError, OSError, RuntimeError])
def test_attributed_exception_message_cannot_collide_with_malformed_facts(
    tmp_path: Path,
    error_type: type[BaseException],
) -> None:
    runner = ExplicitFactRunner(
        executed=True,
        started=True,
        error=error_type("acceptance runner action facts are malformed"),
    )
    result, _, _ = _run(
        tmp_path,
        scenario="absent-create-readback",
        observations=[_absent(), _absent()],
        runner=runner,
    )

    assert result.result == "runner-failed-after-mutation-start"
    assert result.mutation_classification == "incomplete"
    assert result.action_executed is True
    assert result.mutation_started is True


@pytest.mark.parametrize(
    ("scenario", "error_type", "expected_result"),
    [
        ("absent-create-readback", TimeoutError, "timeout"),
        ("lost-response", RuntimeError, "lost-response"),
    ],
)
@pytest.mark.parametrize(
    "attributes",
    [
        {},
        {"action_executed": True},
        {"action_executed": "yes", "mutation_started": False},
        {"action_executed": False, "mutation_started": True},
    ],
    ids=("missing", "partial", "wrong-types", "contradictory"),
)
def test_special_failure_without_admitted_startedness_remains_unknown(
    tmp_path: Path,
    scenario: str,
    error_type: type[BaseException],
    expected_result: str,
    attributes: dict[str, object],
) -> None:
    error = error_type("runner failure without admitted startedness")
    for name, value in attributes.items():
        setattr(error, name, value)
    result, _, _ = _run(
        tmp_path,
        scenario=scenario,
        observations=[
            _absent(),
            {"state": "unknown", "response-identity-digest": RESPONSE_B},
        ],
        runner=ControlledRunner(error=error),
    )

    assert result.result == expected_result
    assert result.mutation_classification == "unknown"
    assert result.action_executed is False
    assert result.mutation_started is False
    assert result.diagnostics == (
        "mutation-may-have-started",
        "human-reconciliation-required",
    )


@pytest.mark.parametrize(
    (
        "scenario",
        "error",
        "action_executed",
        "mutation_started",
        "expected_result",
    ),
    [
        (
            "absent-create-readback",
            TimeoutError("timeout before action start"),
            False,
            False,
            "runner-failed-before-mutation",
        ),
        (
            "lost-response",
            RuntimeError("lost response before action start"),
            False,
            False,
            "runner-failed-before-mutation",
        ),
        (
            "lost-response",
            RuntimeError("lost response after action start"),
            True,
            False,
            "runner-failed-after-action-start",
        ),
    ],
)
def test_special_failure_with_admitted_pre_mutation_startedness_is_incomplete(
    tmp_path: Path,
    scenario: str,
    error: BaseException,
    action_executed: bool,
    mutation_started: bool,
    expected_result: str,
) -> None:
    runner = ExplicitFactRunner(
        executed=action_executed,
        started=mutation_started,
        error=error,
    )
    result, _, _ = _run(
        tmp_path,
        scenario=scenario,
        observations=[_absent(), _absent()],
        runner=runner,
    )

    assert result.result == expected_result
    assert result.mutation_classification == "incomplete"
    assert result.action_executed is action_executed
    assert result.mutation_started is mutation_started
    assert result.diagnostics == (
        ("runner-did-not-prove-controlled-outcome",)
        if action_executed
        else ("runner-did-not-prove-mutation-start",)
    )


@pytest.mark.parametrize("outcome", [[], {}])
@pytest.mark.parametrize(
    ("action_executed", "mutation_started", "expected_result"),
    [
        (False, False, "runner-malformed-before-mutation"),
        (True, False, "runner-failed-after-action-start"),
        (True, True, "runner-failed-after-mutation-start"),
    ],
)
def test_unhashable_runner_outcome_fails_closed(
    tmp_path: Path,
    outcome: object,
    action_executed: bool,
    mutation_started: bool,
    expected_result: str,
) -> None:
    result, _, _ = _run(
        tmp_path,
        scenario="absent-create-readback",
        observations=[_absent(), _absent()],
        runner=ControlledRunner(
            outcome=outcome,
            action_executed=action_executed,
            mutation_started=mutation_started,
        ),
    )

    assert result.result == expected_result
    assert result.mutation_classification == "incomplete"


@pytest.mark.parametrize("runner", [PreStartFailureRunner(), MalformedRunner()])
def test_failed_or_malformed_runner_before_creation_never_reports_created(
    tmp_path: Path,
    runner: ControlledRunner,
) -> None:
    result, _, _ = _run(
        tmp_path,
        scenario="absent-create-readback",
        observations=[_absent(), _absent()],
        runner=runner,
    )

    assert result.result in {
        "runner-not-created",
        "runner-failed-before-mutation",
        "runner-malformed-before-mutation",
    }
    assert result.pre_state == "absent"
    assert result.post_state == "absent"
    assert result.mutation_classification == "incomplete"
    assert "mutation-may-have-started" not in result.diagnostics


@pytest.mark.parametrize(
    "classifications",
    [
        ("complete", "complete", "complete"),
        ("complete", "incomplete", "incomplete"),
        ("incomplete", "unknown", "unknown"),
        ("unknown", "complete", "unknown"),
    ],
)
def test_suite_aggregation_is_monotone(
    classifications: tuple[str, str, str],
) -> None:
    results = tuple(
        FixedCoordinateAcceptanceProbeResult(
            scenario=f"scenario-{index}",
            package_coordinate="@hcoona/package@1",
            tag="tag",
            pre_state="absent",
            post_state="exact",
            result="result",
            mutation_classification=classification,
            action_executed=True,
            mutation_started=True,
            response_identity_digest=RESPONSE_A,
            content_sha512="sha512:" + ("1" * 128),
            diagnostics=(),
        )
        for index, classification in enumerate(classifications)
    )

    suite = FixedAcceptanceSuiteResult(suite="test", scenarios=results)

    expected = (
        "unknown"
        if "unknown" in classifications
        else "incomplete"
        if "incomplete" in classifications
        else "complete"
    )
    assert suite.mutation_classification == expected


@pytest.mark.parametrize(
    ("classifications", "expected_result"),
    [
        (("complete", "complete"), "success"),
        (("complete", "incomplete"), "incomplete"),
        (("incomplete", "unknown"), "unknown"),
        (("unknown", "complete"), "unknown"),
    ],
)
def test_suite_result_is_derived_only_from_scenario_classifications(
    classifications: tuple[str, ...],
    expected_result: str,
) -> None:
    suite = FixedAcceptanceSuiteResult(
        suite="classification-regression",
        scenarios=tuple(
            FixedCoordinateAcceptanceProbeResult(
                scenario=f"scenario-{index}",
                package_coordinate="@hcoona/package@1",
                tag="tag",
                pre_state="absent",
                post_state=(
                    "unknown" if classification == "unknown" else "exact"
                ),
                result="scenario-result",
                mutation_classification=classification,
                action_executed=True,
                mutation_started=True,
                response_identity_digest=RESPONSE_A,
                content_sha512="sha512:" + ("1" * 128),
                diagnostics=(),
            )
            for index, classification in enumerate(classifications)
        ),
    )

    assert suite.result == expected_result


def _acceptance_tarball(
    *,
    version: str,
    repository_url: str,
    target_sha: str,
) -> bytes:
    entries = {
        "package/package.json": canonicalize(
            {
                "name": "@hcoona/hcoona-release-smoke-npm",
                "version": version,
                "repository": {"type": "git", "url": repository_url},
            }
        ),
        "package/index.js": b"export const workflowDeliveryAcceptance = true;\n",
        "package/workflow-delivery/acceptance.json": canonicalize(
            {"purpose": "destination-acceptance", "target-sha": target_sha}
        ),
    }
    tar_buffer = io.BytesIO()
    with tarfile.open(
        fileobj=tar_buffer, mode="w", format=tarfile.USTAR_FORMAT
    ) as archive:
        for name, content in entries.items():
            info = tarfile.TarInfo(name)
            info.size = len(content)
            info.mtime = 0
            archive.addfile(info, io.BytesIO(content))
    payload = bytearray(tar_buffer.getvalue())
    with tarfile.open(fileobj=io.BytesIO(payload), mode="r:") as archive:
        members = archive.getmembers()
    for member in members:
        header = bytearray(
            payload[member.offset : member.offset + tarfile.BLOCKSIZE]
        )
        header[0:100] = member.name.encode() + bytes(
            100 - len(member.name.encode())
        )
        header[100:108] = b"000644 \0"
        header[108:116] = bytes(8)
        header[116:124] = bytes(8)
        header[124:136] = f"{member.size:010o} \0".encode()
        header[136:148] = b"0000000000 \0"
        header[148:156] = b"        "
        header[156:157] = tarfile.REGTYPE
        header[157:257] = bytes(100)
        header[257:263] = b"ustar\0"
        header[263:265] = b"00"
        header[265:329] = bytes(64)
        header[329:337] = b"000000 \0"
        header[337:345] = b"000000 \0"
        header[345:512] = bytes(167)
        checksum = sum(header)
        header[148:156] = f"{checksum:06o} \0".encode()
        payload[member.offset : member.offset + tarfile.BLOCKSIZE] = header
    data_end = max(
        (
            (member.offset_data + member.size + tarfile.BLOCKSIZE - 1)
            // tarfile.BLOCKSIZE
            * tarfile.BLOCKSIZE
            for member in members
        ),
        default=0,
    )
    return gzip.compress(
        bytes(payload[:data_end] + bytes(tarfile.BLOCKSIZE * 2)),
        mtime=0,
    )


def test_strict_readback_hashes_tarball_and_validates_identity_witness_repository() -> (
    None
):
    version = "0.0.0-wdv3-acceptance.2"
    tarball = _acceptance_tarball(
        version=version,
        repository_url="git+https://github.com/hcoona/three.git",
        target_sha=TARGET,
    )

    result = inspect_fixed_acceptance_tarball(
        tarball,
        package_coordinate=f"@hcoona/hcoona-release-smoke-npm@{version}",
        tag="wdv3-acceptance-2",
        observed_version=version,
        observed_tag_version=version,
        target_sha=TARGET,
    )

    assert result["content-sha512"] == (
        f"sha512:{hashlib.sha512(tarball).hexdigest()}"
    )
    assert result["owner"] == "hcoona"
    assert result["repository"] == "hcoona/three"


@pytest.mark.parametrize(
    ("repository_url", "target_sha", "observed_version"),
    [
        (
            "git+https://github.com/other/repo.git",
            TARGET,
            "0.0.0-wdv3-acceptance.2",
        ),
        (
            "git+https://github.com/hcoona/three.git",
            "d" * 40,
            "0.0.0-wdv3-acceptance.2",
        ),
        (
            "git+https://github.com/hcoona/three.git",
            TARGET,
            "0.0.0-wdv3-acceptance.3",
        ),
    ],
)
def test_strict_readback_rejects_wrong_repository_witness_or_version(
    repository_url: str,
    target_sha: str,
    observed_version: str,
) -> None:
    version = "0.0.0-wdv3-acceptance.2"
    tarball = _acceptance_tarball(
        version=version,
        repository_url=repository_url,
        target_sha=target_sha,
    )

    with pytest.raises(ValueError):
        inspect_fixed_acceptance_tarball(
            tarball,
            package_coordinate=f"@hcoona/hcoona-release-smoke-npm@{version}",
            tag="wdv3-acceptance-2",
            observed_version=observed_version,
            observed_tag_version=version,
            target_sha=TARGET,
        )


def test_acceptance_cli_token_never_uses_argv_or_inherited_subprocess_env(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv("GITHUB_TOKEN", "ambient-token")
    monkeypatch.setenv("WDV3_ACCEPTANCE_GITHUB_TOKEN", "dedicated-token")

    environment = cli_module._acceptance_subprocess_environment()
    source = Path(cli_module.__file__).read_text(encoding="utf-8")

    assert "GITHUB_TOKEN" not in environment
    assert "WDV3_ACCEPTANCE_GITHUB_TOKEN" not in environment
    assert 'run_acceptance_probe.add_argument("--github-token"' not in source
    assert 'os.environ.pop("WDV3_ACCEPTANCE_GITHUB_TOKEN"' in source
    assert '"Bearer " + self._token' in source
    assert "npm_config=None" not in source
    assert "env=_acceptance_subprocess_environment()," in source


def test_real_cli_runner_contains_bounded_competing_and_lost_response_paths() -> (
    None
):
    source = Path(cli_module.__file__).read_text(encoding="utf-8")

    assert 'scenario == "identical-race"' in source
    assert 'scenario == "differing-race"' in source
    assert 'scenario == "lost-response"' in source
    assert "subprocess.Popen" in source
    assert "commands.append(argv)" in source
    assert "contender_tarballs" in source
    assert "process.communicate(timeout=timeout_seconds)" in source
    assert '"outcome": "lost-response-processed"' in source
    assert "proxy.proof" in source
    assert "class _LostResponseProxy" in source


class FakeStartedProcess:
    def __init__(
        self, command: tuple[str, ...], *, timeout_on_wait: bool
    ) -> None:
        self.command = command
        self.timeout_on_wait = timeout_on_wait
        self.killed = False
        self.communications: list[float | None] = []
        self.returncode: int | None = None

    def poll(self) -> int | None:
        return self.returncode

    def kill(self) -> None:
        self.killed = True
        self.returncode = -9

    def communicate(self, timeout: float | None = None) -> tuple[str, str]:
        self.communications.append(timeout)
        if timeout is not None and self.timeout_on_wait:
            raise subprocess.TimeoutExpired(self.command, timeout)
        return "", ""


@pytest.mark.parametrize("startup_mode", ["all-started", "partial-startup"])
def test_partial_process_startup_timeout_cleans_every_started_process(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    startup_mode: str,
) -> None:
    started: list[FakeStartedProcess] = []

    def fake_popen(
        command: tuple[str, ...], **_kwargs: object
    ) -> FakeStartedProcess:
        if startup_mode == "partial-startup" and started:
            raise subprocess.TimeoutExpired(command, TIMEOUT_SECONDS)
        process = FakeStartedProcess(
            command,
            timeout_on_wait=startup_mode == "all-started" and not started,
        )
        started.append(process)
        return process

    monkeypatch.setattr(cli_module.subprocess, "Popen", fake_popen)
    runner = cli_module._AcceptanceNpmRunner(
        tmp_path / ".npmrc",
        contender_tarballs={},
    )

    with pytest.raises(TimeoutError):
        runner.run_scenario(
            "identical-race",
            (
                "npm",
                "publish",
                str(tmp_path / "package.tgz"),
                "--tag",
                "wdv3-acceptance-2",
            ),
            env={},
            timeout_seconds=TIMEOUT_SECONDS,
            max_output_bytes=MAX_OUTPUT_BYTES,
        )

    assert started
    assert all(process.killed for process in started)
    assert all(process.communications[-1] is None for process in started)


def test_run_process_timeout_raises_builtin_timeout_error(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    def timeout_run(
        argv: tuple[str, ...],
        **_kwargs: object,
    ) -> subprocess.CompletedProcess[str]:
        raise subprocess.TimeoutExpired(argv, TIMEOUT_SECONDS)

    monkeypatch.setattr(cli_module.subprocess, "run", timeout_run)
    runner = cli_module._AcceptanceNpmRunner(
        tmp_path / ".npmrc",
        contender_tarballs={},
    )

    with pytest.raises(TimeoutError):
        runner._run_process(
            ("npm", "publish", str(tmp_path / "package.tgz")),
            env={},
            timeout_seconds=TIMEOUT_SECONDS,
            max_output_bytes=MAX_OUTPUT_BYTES,
        )


def test_run_process_classification_error_preserves_startedness(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(
        cli_module.subprocess,
        "run",
        lambda *_args, **_kwargs: subprocess.CompletedProcess(
            ("npm", "publish"),
            0,
            stdout="oversized",
            stderr="",
        ),
    )
    runner = cli_module._AcceptanceNpmRunner(
        tmp_path / ".npmrc",
        contender_tarballs={},
    )

    with pytest.raises(ValueError) as raised:
        runner._run_process(
            ("npm", "publish", str(tmp_path / "package.tgz")),
            env={},
            timeout_seconds=TIMEOUT_SECONDS,
            max_output_bytes=1,
        )

    assert raised.value.action_executed is True  # type: ignore[attr-defined]
    assert raised.value.mutation_started is True  # type: ignore[attr-defined]


@pytest.mark.parametrize(
    ("proxy_observed", "expected_result"),
    [
        (False, "runner-failed-after-action-start"),
        (True, "runner-failed-after-mutation-start"),
    ],
)
def test_runner_classification_error_preserves_proxy_startedness(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    proxy_observed: bool,
    expected_result: str,
) -> None:
    observed = cli_module.threading.Event()
    if proxy_observed:
        observed.set()

    class Proxy:
        registry = "http://127.0.0.1:4873"
        processed = cli_module.threading.Event()
        proof = None

        def __init__(self, **_kwargs: object) -> None:
            self.observed = observed

        def __enter__(self) -> Self:
            return self

        def __exit__(self, *_args: object) -> None:
            pass

    class Process:
        returncode = 0

        def communicate(self, timeout: float | None = None) -> tuple[str, str]:
            assert timeout is not None
            return "oversized", ""

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
    result, _, _ = _run(
        tmp_path,
        scenario="absent-create-readback",
        observations=[_absent(), _absent()],
        runner=cli_module._AcceptanceNpmRunner(
            tmp_path / ".npmrc",
            contender_tarballs={},
        ),
        max_output_bytes=1,
    )

    assert result.result == expected_result
    assert result.mutation_classification == "incomplete"
    assert result.action_executed is True
    assert result.mutation_started is proxy_observed


def test_adapter_records_timeout_as_started_unknown_mutation(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    observed = cli_module.threading.Event()
    observed.set()

    class TimeoutProxy:
        registry = "http://127.0.0.1:4873"
        processed = cli_module.threading.Event()
        proof = None

        def __init__(self, **_kwargs: object) -> None:
            self.observed = observed

        def __enter__(self) -> Self:
            return self

        def __exit__(self, *_args: object) -> None:
            pass

    class TimeoutProcess:
        returncode: int | None = None

        def communicate(self, timeout: float | None = None) -> tuple[str, str]:
            if timeout is None:
                return "", ""
            raise subprocess.TimeoutExpired(("npm", "publish"), timeout)

        def poll(self) -> int | None:
            return self.returncode

        def kill(self) -> None:
            self.returncode = -9

    monkeypatch.setattr(cli_module, "_LostResponseProxy", TimeoutProxy)
    monkeypatch.setattr(
        cli_module.subprocess,
        "Popen",
        lambda *_args, **_kwargs: TimeoutProcess(),
    )
    result, _, _ = _run(
        tmp_path,
        scenario="absent-create-readback",
        observations=[
            _absent(),
            {"state": "unknown", "response-identity-digest": RESPONSE_B},
        ],
        runner=cli_module._AcceptanceNpmRunner(
            tmp_path / ".npmrc",
            contender_tarballs={},
        ),
    )

    assert result.result == "timeout"
    assert result.mutation_classification == "unknown"
    assert result.action_executed is True
    assert result.mutation_started is True


def test_ambiguous_npm_e404_maps_to_unknown_non_authoritative_absence(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    def fake_run(
        argv: tuple[str, ...],
        **_kwargs: object,
    ) -> subprocess.CompletedProcess[str]:
        return subprocess.CompletedProcess(
            argv,
            1,
            "",
            "npm ERR! code E404\nnpm ERR! 404 Not Found",
        )

    monkeypatch.setattr(cli_module.subprocess, "run", fake_run)
    acceptance_token = TARGET
    transport = cli_module._AcceptanceNpmTransport(
        tmp_path / ".npmrc",
        token=acceptance_token,
        target_sha=TARGET,
    )
    transport._transport = ForbiddenMetadataTransport()
    observation = transport.observe(
        ACCEPTANCE_COORDINATES["absent-create-readback"],
        TAGS["absent-create-readback"],
        timeout_seconds=TIMEOUT_SECONDS,
        max_response_bytes=MAX_RESPONSE_BYTES,
    )
    runner = ControlledRunner()
    tarball = tmp_path / "absent-create-readback.tgz"
    tarball.write_bytes(b"absent-create-readback")

    result = run_fixed_coordinate_acceptance_probe(
        scenario="absent-create-readback",
        package_coordinate=ACCEPTANCE_COORDINATES["absent-create-readback"],
        tag=TAGS["absent-create-readback"],
        tarball=tarball,
        tarball_sha512=(
            f"sha512:{hashlib.sha512(tarball.read_bytes()).hexdigest()}"
        ),
        transport=transport,
        runner=runner,
        timeout_seconds=TIMEOUT_SECONDS,
        max_response_bytes=MAX_RESPONSE_BYTES,
        max_output_bytes=MAX_OUTPUT_BYTES,
    )

    assert observation["state"] == "unknown"
    assert result.pre_state == "unknown"
    assert result.mutation_classification == "incomplete"
    assert result.result != "created"
    assert runner.calls == []


@pytest.mark.parametrize(
    ("upstream_status", "processed"),
    [
        (200, True),
        (201, True),
        (202, False),
        (204, False),
        (409, False),
        (401, False),
        (403, False),
        (500, False),
    ],
)
def test_lost_response_proxy_injects_auth_and_only_processes_qualifying_upstream_results(
    monkeypatch: pytest.MonkeyPatch,
    upstream_status: int,
    processed: bool,
) -> None:
    upstream_calls: list[dict[str, Any]] = []
    tarball = _acceptance_tarball(
        version="0.0.0-wdv3-acceptance.4",
        repository_url="git+https://github.com/hcoona/three.git",
        target_sha=TARGET,
    )

    class UpstreamResponse:
        status = upstream_status

        def read(self, _size: int) -> bytes:
            return b"{}"

        def getheaders(self) -> list[tuple[str, str]]:
            return [
                ("Content-Type", "application/json"),
                ("X-GitHub-Request-Id", "request-123"),
            ]

    class UpstreamConnection:
        def __init__(self, host: str, *, timeout: float) -> None:
            upstream_calls.append({"host": host, "timeout": timeout})

        def request(
            self,
            method: str,
            path: str,
            *,
            body: bytes,
            headers: dict[str, str],
        ) -> None:
            upstream_calls[-1].update(
                {
                    "method": method,
                    "path": path,
                    "body": body,
                    "headers": dict(headers),
                }
            )

        def getresponse(self) -> UpstreamResponse:
            return UpstreamResponse()

        def close(self) -> None:
            upstream_calls[-1]["closed"] = True

    monkeypatch.setattr(
        cli_module.http.client,
        "HTTPSConnection",
        UpstreamConnection,
    )

    with cli_module._LostResponseProxy(
        timeout_seconds=TIMEOUT_SECONDS,
        token="dedicated-token",
        expected_method="PUT",
        expected_path="/@hcoona%2fhcoona-release-smoke-npm",
    ) as proxy:
        response = _request_proxy_publish(
            proxy,
            _adversarial_publish_body(tarball),
        )
        assert proxy.processed.is_set() is processed
        proof = proxy.proof

    if upstream_status in {200, 201}:
        assert response is None
        assert proof is not None
        assert proof.upstream_status == upstream_status
    else:
        assert response is not None
        assert response[0] == upstream_status
        assert proof is None
    assert upstream_calls[0]["host"] == "npm.pkg.github.com"
    assert upstream_calls[0]["method"] == "PUT"
    assert upstream_calls[0]["path"] == "/@hcoona%2fhcoona-release-smoke-npm"
    assert upstream_calls[0]["headers"]["Authorization"] == (
        "Bearer dedicated-token"
    )


@pytest.mark.parametrize(
    ("method", "path"),
    [
        ("POST", "/@hcoona%2fhcoona-release-smoke-npm"),
        ("PUT", "/-/ping"),
        (
            "PUT",
            "https://npm.pkg.github.com/@hcoona%2fhcoona-release-smoke-npm",
        ),
    ],
)
def test_lost_response_proxy_rejects_unexpected_mutation_method_or_path(
    monkeypatch: pytest.MonkeyPatch,
    method: str,
    path: str,
) -> None:
    upstream_calls: list[tuple[str, str]] = []

    class UpstreamConnection:
        def __init__(self, _host: str, *, timeout: float) -> None:
            del timeout

        def request(
            self,
            method: str,
            path: str,
            *,
            body: bytes,
            headers: dict[str, str],
        ) -> None:
            del body, headers
            upstream_calls.append((method, path))

        def getresponse(self) -> Any:
            pytest.fail("unexpected upstream mutation")

        def close(self) -> None:
            pass

    monkeypatch.setattr(
        cli_module.http.client,
        "HTTPSConnection",
        UpstreamConnection,
    )

    with cli_module._LostResponseProxy(
        timeout_seconds=TIMEOUT_SECONDS,
        token="dedicated-token",
        expected_method="PUT",
        expected_path="/@hcoona%2fhcoona-release-smoke-npm",
    ) as proxy:
        response = _request_proxy_publish(
            proxy,
            b"{}",
            method=method,
            path=path,
        )
        assert not proxy.processed.is_set()

    assert response is not None
    assert response[0] == 400
    assert upstream_calls == []


def test_acceptance_symbols_are_deliberately_public_when_adapter_exports_them() -> (
    None
):
    expected_acceptance_exports = {
        "ACCEPTANCE_PACKAGE_COORDINATE",
        "ACCEPTANCE_SCENARIOS",
        "ACCEPTANCE_TAGS",
        "FixedCoordinateAcceptanceProbeResult",
        "run_fixed_coordinate_acceptance_probe",
    }
    actual_acceptance_exports = {
        name
        for name in adapter_package.__all__
        if name.startswith(
            (
                "ACCEPTANCE_",
                "FixedAcceptance",
                "FixedCoordinateAcceptance",
                "inspect_fixed_acceptance",
                "run_fixed_acceptance",
                "run_fixed_coordinate_acceptance",
            )
        )
    }

    expected_acceptance_export_objects = {
        "ACCEPTANCE_PACKAGE_COORDINATE": ACCEPTANCE_PACKAGE_COORDINATE,
        "ACCEPTANCE_SCENARIOS": ACCEPTANCE_SCENARIOS,
        "ACCEPTANCE_TAGS": ACCEPTANCE_TAGS,
        "FixedCoordinateAcceptanceProbeResult": FixedCoordinateAcceptanceProbeResult,
        "run_fixed_coordinate_acceptance_probe": run_fixed_coordinate_acceptance_probe,
    }

    assert actual_acceptance_exports == expected_acceptance_exports
    assert (
        set(expected_acceptance_export_objects) == expected_acceptance_exports
    )
    for name, expected_object in expected_acceptance_export_objects.items():
        exported_object = getattr(adapter_package, name)
        assert exported_object is expected_object
        if name in {
            "FixedCoordinateAcceptanceProbeResult",
            "run_fixed_coordinate_acceptance_probe",
        }:
            assert callable(exported_object)


@pytest.mark.parametrize(
    ("coordinate", "tag"),
    [
        (
            "@other/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.1",
            "wdv3-acceptance-1",
        ),
        ("@hcoona/other@0.0.0-wdv3-acceptance.1", "wdv3-acceptance-1"),
        (ACCEPTANCE_COORDINATES["absent-create-readback"], "latest"),
        (ACCEPTANCE_COORDINATES["exact"], "wdv3-acceptance-9"),
        (
            "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.17",
            "wdv3-acceptance-17",
        ),
    ],
)
def test_acceptance_probe_requires_the_fixed_coordinate_and_explicit_tag(
    tmp_path: Path,
    coordinate: str,
    tag: str,
) -> None:
    tarball = tmp_path / "probe.tgz"
    tarball.write_bytes(b"probe")

    with pytest.raises(ValueError):
        run_fixed_coordinate_acceptance_probe(
            scenario="absent-create-readback",
            package_coordinate=coordinate,
            tag=tag,
            tarball=tarball,
            tarball_sha512=(
                f"sha512:{hashlib.sha512(tarball.read_bytes()).hexdigest()}"
            ),
            transport=RecordingTransport([]),
            runner=ControlledRunner(),
            timeout_seconds=TIMEOUT_SECONDS,
            max_response_bytes=MAX_RESPONSE_BYTES,
            max_output_bytes=MAX_OUTPUT_BYTES,
        )


@pytest.mark.parametrize(
    "forbidden", ["latest", "npm unpublish", "npm dist-tag add"]
)
def test_acceptance_probe_rejects_latest_and_every_forbidden_mutation_mode(
    forbidden: str,
) -> None:
    source = Path(cli_module.__file__).read_text(encoding="utf-8")

    assert forbidden not in source
    assert "npm publish" in source


def test_acceptance_probe_rejects_tarball_sha512_mismatch_before_mutation(
    tmp_path: Path,
) -> None:
    tarball = tmp_path / "absent-create-readback.tgz"
    tarball.write_bytes(b"absent-create-readback")
    runner = ControlledRunner()

    with pytest.raises(ValueError):
        run_fixed_coordinate_acceptance_probe(
            scenario="absent-create-readback",
            package_coordinate=ACCEPTANCE_COORDINATES["absent-create-readback"],
            tag=TAGS["absent-create-readback"],
            tarball=tarball,
            tarball_sha512="sha512:" + ("0" * 128),
            transport=RecordingTransport([_absent()]),
            runner=runner,
            timeout_seconds=TIMEOUT_SECONDS,
            max_response_bytes=MAX_RESPONSE_BYTES,
            max_output_bytes=MAX_OUTPUT_BYTES,
        )
    assert runner.calls == []


def test_absent_create_readback_records_proof_free_incomplete_facts(
    tmp_path: Path,
) -> None:
    test_absent_proof_free_created_remains_incomplete(tmp_path)


def test_exact_preexisting_state_never_invokes_the_mutation_runner(
    tmp_path: Path,
) -> None:
    test_exact_no_action_records_executed_and_started_false(tmp_path)


def test_identical_conflict_race_is_exact_without_blind_repair(
    tmp_path: Path,
) -> None:
    test_races_use_explicit_controlled_scenario_seam(
        tmp_path,
        "identical-race",
        "exact",
        "create-conflict",
        "identical-race-exact",
        "complete",
    )


def test_differing_conflict_race_is_conflicting_without_overwrite(
    tmp_path: Path,
) -> None:
    test_races_use_explicit_controlled_scenario_seam(
        tmp_path,
        "differing-race",
        "conflicting",
        "create-conflict",
        "differing-race-conflict",
        "complete",
    )


def test_lost_response_is_unknown_and_requires_reconciliation(
    tmp_path: Path,
) -> None:
    test_lost_response_unknown_post_readback_stays_unknown(tmp_path)


def test_probe_transport_and_runner_are_bounded_injected_and_offline(
    tmp_path: Path,
) -> None:
    result, transport, _ = _run(
        tmp_path,
        scenario="exact",
        observations=[
            _state(
                "exact",
                scenario="exact",
                content="sha512:" + hashlib.sha512(b"exact").hexdigest(),
            )
        ],
        runner=ControlledRunner(),
    )

    assert result.mutation_classification in {"complete", "incomplete"}
    assert transport.calls[0][2:] == (TIMEOUT_SECONDS, MAX_RESPONSE_BYTES)


def test_wrong_tag_preexisting_state_requires_reconciliation_without_mutation(
    tmp_path: Path,
) -> None:
    result, _, _ = _run(
        tmp_path,
        scenario="exact",
        observations=[
            {
                **_state(
                    "exact", scenario="exact", content="sha512:" + ("1" * 128)
                ),
                "tag": "wrong-tag",
            }
        ],
        runner=ControlledRunner(),
    )

    assert result.mutation_classification != "complete"


def test_wrong_tag_readback_requires_reconciliation_without_repair(
    tmp_path: Path,
) -> None:
    runner = ControlledRunner()
    result, _, _ = _run(
        tmp_path,
        scenario="absent-create-readback",
        observations=[
            _absent(),
            {
                **_state(
                    "exact",
                    scenario="absent-create-readback",
                    content="sha512:"
                    + hashlib.sha512(b"absent-create-readback").hexdigest(),
                ),
                "tag": "wrong-tag",
            },
        ],
        runner=runner,
    )

    assert len(runner.calls) <= 1
    assert (
        result.result != "created"
        or result.mutation_classification != "complete"
    )


def test_wrong_tag_identical_conflict_race_requires_unknown_reconciliation(
    tmp_path: Path,
) -> None:
    result, _, _ = _run(
        tmp_path,
        scenario="identical-race",
        observations=[
            _absent(),
            {
                **_state(
                    "exact",
                    scenario="identical-race",
                    content="sha512:"
                    + hashlib.sha512(b"identical-race").hexdigest(),
                ),
                "tag": "wrong-tag",
            },
        ],
        runner=ControlledRunner(outcome="create-conflict"),
    )

    assert result.mutation_classification in {"unknown", "incomplete"}


@pytest.mark.parametrize("scenario", ACCEPTANCE_SCENARIOS)
def test_scenario_specific_preconditions_and_terminal_semantics_are_fixed(
    scenario: str,
) -> None:
    assert scenario in ACCEPTANCE_COORDINATES
    assert scenario in TAGS
    if scenario == "exact":
        assert ACCEPTANCE_COORDINATES[scenario].endswith("acceptance.1")
    elif scenario != "absent-create-readback":
        assert not ACCEPTANCE_COORDINATES[scenario].endswith("acceptance.1")


@pytest.mark.parametrize("scenario", tuple(ACCEPTANCE_SCENARIOS))
def test_canonical_suite_record_digest_binds_each_probe_document(
    scenario: str,
) -> None:
    result = FixedCoordinateAcceptanceProbeResult(
        scenario=scenario,
        package_coordinate=ACCEPTANCE_COORDINATES[scenario],
        tag=TAGS[scenario],
        pre_state="absent",
        post_state="exact",
        result="created",
        mutation_classification="complete",
        action_executed=True,
        mutation_started=True,
        response_identity_digest=RESPONSE_A,
        content_sha512="sha512:" + ("1" * 128),
        diagnostics=(),
    )
    suite = FixedAcceptanceSuiteResult(suite=scenario, scenarios=(result,))

    record_digest = suite.to_document()["record-digest"]
    assert isinstance(record_digest, str)
    assert record_digest.startswith("sha256:")


def _adversarial_publish_body(
    tarball: bytes,
    *,
    package: str = "@hcoona/hcoona-release-smoke-npm",
    version: str = "0.0.0-wdv3-acceptance.4",
    tag: str = "wdv3-acceptance-4",
    attachment_count: int = 1,
) -> bytes:
    attachment_name = f"hcoona-release-smoke-npm-{version}.tgz"
    attachments = {
        attachment_name: {
            "content_type": "application/octet-stream",
            "data": cli_module.base64.b64encode(tarball).decode("ascii"),
            "length": len(tarball),
        }
    }
    if attachment_count == 0:
        attachments = {}
    elif attachment_count == 2:
        attachments["unexpected.tgz"] = {
            "content_type": "application/octet-stream",
            "data": cli_module.base64.b64encode(b"other").decode("ascii"),
            "length": 5,
        }
    document = {
        "_id": package,
        "name": package,
        "dist-tags": {tag: version},
        "versions": {
            version: {
                "name": package,
                "version": version,
                "dist": {
                    "integrity": (
                        "sha512-"
                        + cli_module.base64.b64encode(
                            hashlib.sha512(tarball).digest()
                        ).decode("ascii")
                    ),
                    "shasum": hashlib.sha1(tarball).hexdigest(),  # noqa: S324
                },
            }
        },
        "_attachments": attachments,
    }
    return canonicalize(cast("JsonValue", document))


def _request_proxy_publish(
    proxy: Any,
    body: bytes,
    *,
    method: str = "PUT",
    path: str = "/@hcoona%2fhcoona-release-smoke-npm",
) -> tuple[int, list[tuple[str, str]], bytes] | None:
    host, port = proxy.registry.removeprefix("http://").split(":", 1)
    connection = http.client.HTTPConnection(
        host,
        int(port),
        timeout=TIMEOUT_SECONDS,
    )
    try:
        connection.request(
            method,
            path,
            body=body,
            headers={"Content-Type": "application/json"},
        )
        response = connection.getresponse()
        return response.status, response.getheaders(), response.read()
    except (ConnectionError, http.client.HTTPException):
        return None
    finally:
        connection.close()


def _send_proxy_publish(
    proxy: Any,
    body: bytes,
) -> int | None:
    response = _request_proxy_publish(proxy, body)
    return None if response is None else response[0]


@pytest.mark.parametrize("upstream_status", [200, 201])
def test_adversarial_lost_proxy_validates_exact_publish_and_binds_full_proof(
    monkeypatch: pytest.MonkeyPatch,
    upstream_status: int,
) -> None:
    version = "0.0.0-wdv3-acceptance.4"
    tag = "wdv3-acceptance-4"
    tarball = _acceptance_tarball(
        version=version,
        repository_url="git+https://github.com/hcoona/three.git",
        target_sha=TARGET,
    )
    request_body = _adversarial_publish_body(tarball)
    forwarded: list[bytes] = []
    response_body = b'{"ok":true,"id":"fixed-coordinate"}'
    selected_headers = {
        "content-type": "application/json",
        "etag": '"publish-etag"',
        "retry-after": "3",
    }

    class QualifiedResponse:
        status = upstream_status

        def read(self, _size: int) -> bytes:
            return response_body

        def getheaders(self) -> list[tuple[str, str]]:
            return [
                ("Content-Type", "application/json"),
                ("ETag", '"publish-etag"'),
                ("Retry-After", "3"),
                ("Set-Cookie", "must-not-enter-proof"),
            ]

    class QualifiedConnection:
        def __init__(self, host: str, *, timeout: float) -> None:
            assert host == "npm.pkg.github.com"
            assert timeout == TIMEOUT_SECONDS

        def request(
            self,
            method: str,
            path: str,
            *,
            body: bytes,
            headers: dict[str, str],
        ) -> None:
            assert method == "PUT"
            assert path == "/@hcoona%2fhcoona-release-smoke-npm"
            assert headers["Authorization"] == "Bearer dedicated-token"
            forwarded.append(body)

        def getresponse(self) -> QualifiedResponse:
            return QualifiedResponse()

        def close(self) -> None:
            pass

    monkeypatch.setattr(
        cli_module.http.client, "HTTPSConnection", QualifiedConnection
    )
    with cli_module._LostResponseProxy(
        timeout_seconds=TIMEOUT_SECONDS,
        token="dedicated-token",
        expected_method="PUT",
        expected_path="/@hcoona%2fhcoona-release-smoke-npm",
    ) as proxy:
        assert _send_proxy_publish(proxy, request_body) is None
        assert proxy.processed.is_set()
        proof = proxy.proof

    assert forwarded == [request_body]
    forwarded_document = json.loads(forwarded[0])
    assert forwarded_document["_id"] == "@hcoona/hcoona-release-smoke-npm"
    assert set(forwarded_document["versions"]) == {version}
    assert forwarded_document["dist-tags"] == {tag: version}
    assert len(forwarded_document["_attachments"]) == 1
    attachment = next(iter(forwarded_document["_attachments"].values()))
    decoded = cli_module.base64.b64decode(attachment["data"], validate=True)
    assert decoded == tarball
    assert attachment["length"] == len(tarball)
    assert (
        hashlib.sha512(decoded).hexdigest()
        == hashlib.sha512(tarball).hexdigest()
    )
    with tarfile.open(fileobj=io.BytesIO(decoded), mode="r:gz") as archive:
        witness = archive.extractfile(
            "package/workflow-delivery/acceptance.json"
        )
        assert witness is not None
        assert json.loads(witness.read()) == {
            "purpose": "destination-acceptance",
            "target-sha": TARGET,
        }

    request_digest = "sha256:" + hashlib.sha256(request_body).hexdigest()
    response_body_digest = "sha256:" + hashlib.sha256(response_body).hexdigest()
    expected_identity = (
        "sha256:"
        + hashlib.sha256(
            canonicalize(
                cast(
                    "JsonValue",
                    {
                        "request-digest": request_digest,
                        "upstream-status": upstream_status,
                        "selected-headers": selected_headers,
                        "response-body-digest": response_body_digest,
                    },
                )
            )
        ).hexdigest()
    )
    assert proof == {
        "outcome": "lost-response-processed",
        "request-digest": request_digest,
        "upstream-status": upstream_status,
        "selected-headers": selected_headers,
        "response-body-digest": response_body_digest,
        "response-identity-digest": expected_identity,
    }


@pytest.mark.parametrize(
    "mutation",
    [
        "wrong-package",
        "wrong-version",
        "wrong-tag",
        "no-attachment",
        "two-attachments",
        "wrong-tarball-hash",
        "missing-witness",
        "empty-object",
    ],
)
def test_adversarial_lost_proxy_rejects_nonqualifying_body_before_forward(
    monkeypatch: pytest.MonkeyPatch,
    mutation: str,
) -> None:
    version = "0.0.0-wdv3-acceptance.4"
    tarball = _acceptance_tarball(
        version=version,
        repository_url="git+https://github.com/hcoona/three.git",
        target_sha=TARGET,
    )
    if mutation == "wrong-package":
        body = _adversarial_publish_body(tarball, package="@hcoona/other")
    elif mutation == "wrong-version":
        body = _adversarial_publish_body(tarball, version="9.9.9")
    elif mutation == "wrong-tag":
        body = _adversarial_publish_body(tarball, tag="latest")
    elif mutation == "no-attachment":
        body = _adversarial_publish_body(tarball, attachment_count=0)
    elif mutation == "two-attachments":
        body = _adversarial_publish_body(tarball, attachment_count=2)
    elif mutation == "wrong-tarball-hash":
        document = json.loads(_adversarial_publish_body(tarball))
        only_version = next(iter(document["versions"].values()))
        only_version["dist"]["integrity"] = "sha512-" + ("A" * 88)
        body = canonicalize(document)
    elif mutation == "missing-witness":
        body = _adversarial_publish_body(b"not-a-tarball")
    else:
        body = b"{}"

    class ForbiddenUpstream:
        def __init__(self, *_args: object, **_kwargs: object) -> None:
            pytest.fail(f"{mutation} reached upstream")

    monkeypatch.setattr(
        cli_module.http.client, "HTTPSConnection", ForbiddenUpstream
    )
    with cli_module._LostResponseProxy(
        timeout_seconds=TIMEOUT_SECONDS,
        token="dedicated-token",
        expected_method="PUT",
        expected_path="/@hcoona%2fhcoona-release-smoke-npm",
    ) as proxy:
        status = _send_proxy_publish(proxy, body)
        assert status in {400, 409, 415, 422}
        assert not proxy.processed.is_set()
        assert proxy.proof is None


@pytest.mark.parametrize("upstream_status", [202, 204, 409, 401, 403, 500, 503])
def test_adversarial_lost_proxy_nonaccepted_never_proves_processed(
    monkeypatch: pytest.MonkeyPatch,
    upstream_status: int,
) -> None:
    tarball = _acceptance_tarball(
        version="0.0.0-wdv3-acceptance.4",
        repository_url="git+https://github.com/hcoona/three.git",
        target_sha=TARGET,
    )

    class NonSuccessResponse:
        status = upstream_status

        def read(self, _size: int) -> bytes:
            return b'{"error":"not processed"}'

        def getheaders(self) -> list[tuple[str, str]]:
            return [("Content-Type", "application/json")]

    class NonSuccessConnection:
        def __init__(self, _host: str, *, timeout: float) -> None:
            assert timeout == TIMEOUT_SECONDS

        def request(self, *_args: object, **_kwargs: object) -> None:
            pass

        def getresponse(self) -> NonSuccessResponse:
            return NonSuccessResponse()

        def close(self) -> None:
            pass

    monkeypatch.setattr(
        cli_module.http.client, "HTTPSConnection", NonSuccessConnection
    )
    with cli_module._LostResponseProxy(
        timeout_seconds=TIMEOUT_SECONDS,
        token="dedicated-token",
        expected_method="PUT",
        expected_path="/@hcoona%2fhcoona-release-smoke-npm",
    ) as proxy:
        assert (
            _send_proxy_publish(proxy, _adversarial_publish_body(tarball))
            == upstream_status
        )
        assert not proxy.processed.is_set()
        assert proxy.proof is None


def test_adversarial_generic_exact_readback_without_matching_proof_is_unknown(
    tmp_path: Path,
) -> None:
    tarball = tmp_path / "lost-response.tgz"
    tarball.write_bytes(b"lost-response")
    content = "sha512:" + hashlib.sha512(tarball.read_bytes()).hexdigest()
    forged = ControlledRunner(outcome="lost-response-processed")
    forged.run_scenario = lambda *_args, **_kwargs: {
        "outcome": "lost-response-processed",
        "action-executed": True,
        "mutation-started": True,
        "request-digest": "sha256:" + ("0" * 64),
        "upstream-status": 201,
        "selected-headers": {"etag": '"unbound"'},
        "response-body-digest": "sha256:" + ("1" * 64),
        "response-identity-digest": "sha256:" + ("2" * 64),
    }

    result, _, _ = _run(
        tmp_path,
        scenario="lost-response",
        observations=[
            _absent(),
            _state("exact", scenario="lost-response", content=content),
        ],
        runner=forged,
    )

    assert result.result == "lost-response"
    assert result.mutation_classification == "unknown"
    assert result.post_state == "exact"


def test_adversarial_race_evidence_binds_barrier_overlap_and_each_contender(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    primary = tmp_path / "primary.tgz"
    contender = tmp_path / "contender.tgz"
    primary.write_bytes(b"primary-race-tarball")
    contender.write_bytes(b"contender-race-tarball")
    version = "0.0.0-wdv3-acceptance.3"
    tag = "wdv3-acceptance-3"
    primary_request_digest = (
        "sha256:"
        + hashlib.sha256(
            _adversarial_publish_body(
                primary.read_bytes(),
                version=version,
                tag=tag,
            )
        ).hexdigest()
    )
    contender_request_digest = (
        "sha256:"
        + hashlib.sha256(
            _adversarial_publish_body(
                contender.read_bytes(),
                version=version,
                tag=tag,
            )
        ).hexdigest()
    )
    created: list[Any] = []

    class BarrierProcess:
        returncode: int

        def __init__(self, command: tuple[str, ...]) -> None:
            self.command = command
            self.returncode = 0 if not created else 1
            self.contender_id = f"contender-{len(created) + 1}"
            created.append(self)

        def communicate(self, timeout: float | None = None) -> tuple[str, str]:
            assert timeout is not None
            assert timeout <= TIMEOUT_SECONDS
            assert len(created) == 2
            if self.returncode == 0:
                return "created", ""
            return "", "npm ERR! code E409 conflict"

        def poll(self) -> int | None:
            return self.returncode

        def kill(self) -> None:
            self.returncode = -9

    monkeypatch.setattr(
        cli_module.subprocess,
        "Popen",
        lambda command, **_kwargs: BarrierProcess(command),
    )
    runner = cli_module._AcceptanceNpmRunner(
        tmp_path / ".npmrc",
        contender_tarballs={"differing-race": contender},
    )

    result = runner.run_scenario(
        "differing-race",
        (
            "npm",
            "publish",
            str(primary),
            "--tag",
            "wdv3-acceptance-3",
        ),
        env={},
        timeout_seconds=TIMEOUT_SECONDS,
        max_output_bytes=MAX_OUTPUT_BYTES,
    )

    assert result["race-overlap-proven"] is True
    assert result["barrier-arrivals"] == ["contender-1", "contender-2"]
    assert result["barrier-release"] == "simultaneous"
    assert result["contenders"] == [
        {
            "contender-id": "contender-1",
            "request-digest": primary_request_digest,
            "tarball-sha512": (
                "sha512:" + hashlib.sha512(primary.read_bytes()).hexdigest()
            ),
            "upstream-result": "created",
        },
        {
            "contender-id": "contender-2",
            "request-digest": contender_request_digest,
            "tarball-sha512": (
                "sha512:" + hashlib.sha512(contender.read_bytes()).hexdigest()
            ),
            "upstream-result": "create-conflict",
        },
    ]
    assert primary_request_digest != contender_request_digest


def test_adversarial_sequential_nonoverlap_cannot_prove_race(
    tmp_path: Path,
) -> None:
    remote_sha = "sha512:" + ("f" * 128)
    result, _, _ = _run(
        tmp_path,
        scenario="differing-race",
        observations=[
            _absent(),
            _state(
                "conflicting",
                scenario="differing-race",
                content=remote_sha,
            ),
        ],
        runner=ExplicitFactRunner(
            outcome="create-conflict",
            executed=True,
            started=True,
            contender_outcomes=("created", "create-conflict"),
            winner_content_sha512=remote_sha,
        ),
    )

    assert result.result != "differing-race-conflict"
    assert result.mutation_classification == "unknown"
    assert "race-overlap-not-proven" in result.diagnostics


def test_adversarial_first_popen_failure_is_not_executed_or_started(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(
        cli_module.subprocess,
        "Popen",
        lambda *_args, **_kwargs: (_ for _ in ()).throw(
            OSError("process was never created")
        ),
    )
    result, _, _ = _run(
        tmp_path,
        scenario="identical-race",
        observations=[_absent(), _absent()],
        runner=cli_module._AcceptanceNpmRunner(
            tmp_path / ".npmrc",
            contender_tarballs={},
        ),
    )

    assert result.action_executed is False
    assert result.mutation_started is False
    assert _action_document(result) == {
        "operation": "npm-publish-create-only",
        "executed": False,
        "mutation-started": False,
    }


def test_adversarial_lost_local_failure_before_proxy_request_is_not_started(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    class LocalFailure:
        returncode = 1

        def communicate(self, timeout: float | None = None) -> tuple[str, str]:
            assert timeout is not None
            return "", "npm ERR! local config validation failed"

        def poll(self) -> int:
            return self.returncode

        def kill(self) -> None:
            self.returncode = -9

    monkeypatch.setattr(
        cli_module.subprocess,
        "Popen",
        lambda *_args, **_kwargs: LocalFailure(),
    )
    result, _, _ = _run(
        tmp_path,
        scenario="lost-response",
        observations=[_absent(), _absent()],
        runner=cli_module._AcceptanceNpmRunner(
            tmp_path / ".npmrc",
            contender_tarballs={},
            token="dedicated-token",
        ),
    )

    assert result.action_executed is True
    assert result.mutation_started is False
    assert result.result == "runner-failed-after-action-start"
    assert result.mutation_classification == "incomplete"


@pytest.mark.parametrize(
    ("proxy_observed", "expected_started"),
    [(False, False), (True, True)],
)
def test_adversarial_timeout_startedness_depends_only_on_proxy_observation(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    proxy_observed: bool,
    expected_started: bool,
) -> None:
    observed = cli_module.threading.Event()
    if proxy_observed:
        observed.set()

    class TimeoutProcess:
        returncode: int | None = None

        def communicate(self, timeout: float | None = None) -> tuple[str, str]:
            if timeout is None:
                return "", ""
            raise subprocess.TimeoutExpired(("npm", "publish"), timeout)

        def poll(self) -> int | None:
            return self.returncode

        def kill(self) -> None:
            self.returncode = -9

    class ProxySeam:
        registry = "http://127.0.0.1:4873"
        processed = cli_module.threading.Event()
        proof = None

        def __init__(self, **_kwargs: object) -> None:
            self.observed = observed

        def __enter__(self) -> Self:
            return self

        def __exit__(self, *_args: object) -> None:
            pass

    monkeypatch.setattr(cli_module, "_LostResponseProxy", ProxySeam)
    monkeypatch.setattr(
        cli_module.subprocess,
        "Popen",
        lambda *_args, **_kwargs: TimeoutProcess(),
    )
    result, _, _ = _run(
        tmp_path,
        scenario="lost-response",
        observations=[
            _absent(),
            {"state": "unknown", "response-identity-digest": RESPONSE_B},
        ],
        runner=cli_module._AcceptanceNpmRunner(
            tmp_path / ".npmrc",
            contender_tarballs={},
            token="dedicated-token",
        ),
    )

    assert result.mutation_started is expected_started
    assert result.action_executed is True
    assert result.result == (
        "timeout" if expected_started else "runner-failed-after-action-start"
    )
    assert result.mutation_classification == (
        "unknown" if expected_started else "incomplete"
    )
    assert result.diagnostics == (
        (
            "mutation-may-have-started",
            "human-reconciliation-required",
        )
        if expected_started
        else ("runner-did-not-prove-controlled-outcome",)
    )


def test_lost_response_return_without_mutation_start_is_incomplete(
    tmp_path: Path,
) -> None:
    result, _, _ = _run(
        tmp_path,
        scenario="lost-response",
        observations=[_absent(), _absent()],
        runner=ExplicitFactRunner(executed=True, started=False),
    )

    assert result.result == "runner-failed-after-action-start"
    assert result.mutation_classification == "incomplete"
    assert result.diagnostics == ("runner-did-not-prove-controlled-outcome",)


def test_adversarial_acceptance_rest_pages_share_one_monotonic_deadline(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    class FakeClock:
        now = 100.0

        def monotonic(self) -> float:
            return self.now

        def advance(self, seconds: float) -> None:
            self.now += seconds

    clock = FakeClock()
    monkeypatch.setattr(cli_module, "monotonic", clock.monotonic, raising=False)
    timeouts: list[float] = []

    class PagingTransport:
        def get(
            self,
            url: str,
            *,
            headers: tuple[tuple[str, str], ...],
            timeout: float,
            max_bytes: int,
        ) -> GitHubPackagesHttpResponse:
            del headers, max_bytes
            timeouts.append(timeout)
            clock.advance(2.0)
            if "/versions" not in url:
                body: object = {
                    "package_type": "npm",
                    "name": "hcoona-release-smoke-npm",
                    "owner": {"login": "hcoona"},
                    "repository": {"full_name": "hcoona/three"},
                }
            elif urllib.parse.parse_qs(urllib.parse.urlsplit(url).query).get(
                "page"
            ) == ["1"]:
                body = [{"name": f"other-{index}"} for index in range(100)]
            else:
                body = []
            return GitHubPackagesHttpResponse(
                status=200,
                url=url,
                headers=(),
                body=json.dumps(body).encode(),
            )

    transport = cli_module._AcceptanceNpmTransport(
        tmp_path / ".npmrc",
        token="dedicated-token",
        target_sha=TARGET,
    )
    transport._transport = PagingTransport()
    observation = transport.observe(
        ACCEPTANCE_COORDINATES["exact"],
        TAGS["exact"],
        timeout_seconds=7.0,
        max_response_bytes=MAX_RESPONSE_BYTES,
    )

    assert observation["state"] == "absent"
    assert timeouts == pytest.approx([7.0, 5.0, 3.0])
    assert sum(timeouts) > 7.0
    assert clock.now == 106.0


def test_adversarial_race_waits_share_one_monotonic_deadline(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    class FakeClock:
        now = 20.0

        def monotonic(self) -> float:
            return self.now

    clock = FakeClock()
    monkeypatch.setattr(cli_module, "monotonic", clock.monotonic, raising=False)
    waits: list[float] = []
    processes: list[Any] = []

    class TimedProcess:
        def __init__(self) -> None:
            self.returncode = 0 if not processes else 1
            processes.append(self)

        def communicate(self, timeout: float | None = None) -> tuple[str, str]:
            assert timeout is not None
            waits.append(timeout)
            clock.now += 2.0
            return (
                ("created", "")
                if self.returncode == 0
                else ("", "npm ERR! E409 conflict")
            )

        def poll(self) -> int:
            return self.returncode

        def kill(self) -> None:
            self.returncode = -9

    monkeypatch.setattr(
        cli_module.subprocess,
        "Popen",
        lambda *_args, **_kwargs: TimedProcess(),
    )
    primary = tmp_path / "primary.tgz"
    contender = tmp_path / "contender.tgz"
    primary.write_bytes(b"primary")
    contender.write_bytes(b"contender")
    runner = cli_module._AcceptanceNpmRunner(
        tmp_path / ".npmrc",
        contender_tarballs={"differing-race": contender},
    )
    runner.run_scenario(
        "differing-race",
        ("npm", "publish", str(primary)),
        env={},
        timeout_seconds=7.0,
        max_output_bytes=MAX_OUTPUT_BYTES,
    )

    assert waits == pytest.approx([7.0, 5.0])
    assert clock.now == 24.0


def test_adversarial_lost_response_wait_shares_operation_deadline(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    class FakeClock:
        now = 40.0

        def monotonic(self) -> float:
            return self.now

    clock = FakeClock()
    monkeypatch.setattr(cli_module, "monotonic", clock.monotonic, raising=False)
    waits: list[float] = []

    class DeadlineProxy:
        registry = "http://127.0.0.1:4873"
        processed = cli_module.threading.Event()
        proof = None

        def __init__(self, **_kwargs: object) -> None:
            pass

        def __enter__(self) -> Self:
            clock.now += 2.0
            return self

        def __exit__(self, *_args: object) -> None:
            pass

    class DeadlineProcess:
        returncode = 1

        def communicate(self, timeout: float | None = None) -> tuple[str, str]:
            assert timeout is not None
            waits.append(timeout)
            return "", "local failure"

        def poll(self) -> int:
            return self.returncode

        def kill(self) -> None:
            self.returncode = -9

    monkeypatch.setattr(cli_module, "_LostResponseProxy", DeadlineProxy)
    monkeypatch.setattr(
        cli_module.subprocess,
        "Popen",
        lambda *_args, **_kwargs: DeadlineProcess(),
    )
    runner = cli_module._AcceptanceNpmRunner(
        tmp_path / ".npmrc",
        contender_tarballs={},
        token="dedicated-token",
    )
    runner.run_scenario(
        "lost-response",
        ("npm", "publish", str(tmp_path / "lost.tgz")),
        env={},
        timeout_seconds=7.0,
        max_output_bytes=MAX_OUTPUT_BYTES,
    )

    assert waits == pytest.approx([5.0])
    assert clock.now == 42.0


@pytest.mark.parametrize(
    ("truncated", "complete"),
    [(True, True), (False, False), (True, False)],
)
def test_adversarial_incomplete_or_truncated_package_404_is_unknown(
    tmp_path: Path,
    truncated: bool,
    complete: bool,
) -> None:
    class Incomplete404:
        def get(
            self,
            url: str,
            *,
            headers: tuple[tuple[str, str], ...],
            timeout: float,
            max_bytes: int,
        ) -> GitHubPackagesHttpResponse:
            del headers, timeout, max_bytes
            return GitHubPackagesHttpResponse(
                status=404,
                url=url,
                headers=(),
                body=b'{"message":"Not Found"}',
                truncated=truncated,
                complete=complete,
            )

    transport = cli_module._AcceptanceNpmTransport(
        tmp_path / ".npmrc",
        token="dedicated-token",
        target_sha=TARGET,
    )
    transport._transport = Incomplete404()

    observation = transport.observe(
        ACCEPTANCE_COORDINATES["exact"],
        TAGS["exact"],
        timeout_seconds=TIMEOUT_SECONDS,
        max_response_bytes=MAX_RESPONSE_BYTES,
    )

    assert observation["state"] == "unknown"
    assert observation["response-identity-digest"] == (
        "sha256:" + hashlib.sha256(b'{"message":"Not Found"}').hexdigest()
    )


def test_adversarial_absence_requires_complete_terminal_versions_page(
    tmp_path: Path,
) -> None:
    calls: list[str] = []

    class TerminalPagingTransport:
        def get(
            self,
            url: str,
            *,
            headers: tuple[tuple[str, str], ...],
            timeout: float,
            max_bytes: int,
        ) -> GitHubPackagesHttpResponse:
            del headers, timeout, max_bytes
            calls.append(url)
            if "/versions" not in url:
                body: object = {
                    "package_type": "npm",
                    "name": "hcoona-release-smoke-npm",
                    "owner": {"login": "hcoona"},
                    "repository": {"full_name": "hcoona/three"},
                }
            elif urllib.parse.parse_qs(urllib.parse.urlsplit(url).query).get(
                "page"
            ) == ["1"]:
                body = [{"name": f"other-{index}"} for index in range(100)]
            else:
                body = [{"name": "last-other-version"}]
            return GitHubPackagesHttpResponse(
                status=200,
                url=url,
                headers=(),
                body=json.dumps(body).encode(),
                truncated=False,
                complete=True,
            )

    transport = cli_module._AcceptanceNpmTransport(
        tmp_path / ".npmrc",
        token="dedicated-token",
        target_sha=TARGET,
    )
    transport._transport = TerminalPagingTransport()

    observation = transport.observe(
        ACCEPTANCE_COORDINATES["exact"],
        TAGS["exact"],
        timeout_seconds=TIMEOUT_SECONDS,
        max_response_bytes=MAX_RESPONSE_BYTES,
    )

    assert observation["state"] == "absent"
    assert len(calls) == 3
    assert calls[1].endswith("per_page=100&page=1")
    assert calls[2].endswith("per_page=100&page=2")


def test_acceptance_capture_uses_real_npm_publish_request(
    tmp_path: Path,
) -> None:
    smoke_root = Path(__file__).parents[3] / "hcoona-release-smoke-npm"
    before = snapshot_tree(smoke_root)

    capture = capture_real_npm_publish(tmp_path)

    assert capture.method == "PUT"
    assert capture.path == "/@hcoona%2fhcoona-release-smoke-npm"
    assert capture.headers["content-type"] == "application/json"
    assert int(capture.headers["content-length"]) == len(capture.body)
    assert "transfer-encoding" not in capture.headers
    assert capture.document["_id"] == ACCEPTANCE_PACKAGE_NAME
    assert capture.document["name"] == ACCEPTANCE_PACKAGE_NAME
    assert capture.document["dist-tags"] == {ACCEPTANCE_TAG: ACCEPTANCE_VERSION}
    assert set(capture.document["versions"]) == {ACCEPTANCE_VERSION}
    assert capture.version_document["name"] == ACCEPTANCE_PACKAGE_NAME
    assert capture.version_document["version"] == ACCEPTANCE_VERSION
    assert capture.package_manifest == json.loads(
        (ACCEPTANCE_PACKAGE_FIXTURE_ROOT / "package.json").read_bytes()
    )
    assert capture.attachment_members == (
        "package/README.md",
        "package/dist/acceptance-witness.json",
        "package/dist/index.js",
        "package/package.json",
    )
    assert capture.attachment_sha1 == capture.version_dist["shasum"]
    assert (
        f"sha512-{capture.attachment_sha512_base64}"
        == capture.version_dist["integrity"]
    )
    assert capture.witness == {
        "package-coordinate": ACCEPTANCE_PACKAGE_COORDINATE,
        "target-sha": "c" * 40,
        "version": ACCEPTANCE_VERSION,
    }
    assert snapshot_tree(smoke_root) == before


def test_acceptance_capture_records_nonsecret_toolchain_metadata(
    tmp_path: Path,
) -> None:
    capture = capture_real_npm_publish(tmp_path)

    assert capture.metadata == {
        "argv": [
            "npm",
            "publish",
            "<package>",
            "--tag",
            "wdv3-acceptance-1",
            "--registry",
            "<loopback-registry>",
            "--ignore-scripts",
        ],
        "node-version": "v24.19.0",
        "npm-version": "11.17.0",
    }
    metadata_bytes = canonicalize(cast("JsonValue", capture.metadata))
    assert b"token" not in metadata_bytes.lower()
    assert b"authorization" not in metadata_bytes.lower()


def test_acceptance_request_fixture_is_reproducible(
    tmp_path: Path,
) -> None:
    first = capture_real_npm_publish(tmp_path / "first")
    second = capture_real_npm_publish(tmp_path / "second")

    assert first.nonsecret_fixture() == second.nonsecret_fixture()
    assert first.nonsecret_fixture() == expected_capture()
    assert first.normalized_body == second.normalized_body
    assert (
        hashlib.sha256(first.normalized_body).hexdigest()
        == expected_capture()["normalized-request-body-sha256"]
    )


def test_acceptance_request_fixture_contains_no_credentials(
    tmp_path: Path,
) -> None:
    capture = capture_real_npm_publish(tmp_path)
    retained = canonicalize(cast("JsonValue", capture.nonsecret_fixture()))
    fixture_bytes = b"\n".join(
        path.read_bytes()
        for path in sorted(ACCEPTANCE_FIXTURE_ROOT.rglob("*"))
        if path.is_file()
    )

    for forbidden in (
        DUMMY_LOOPBACK_TOKEN.encode(),
        b"dedicated-token",
        b"npm_",
        b"ghp_",
    ):
        assert forbidden not in retained
        assert forbidden not in fixture_bytes


ACCEPTANCE_VERSION = "0.0.0-wdv3-acceptance.1"
ACCEPTANCE_TAG = "wdv3-acceptance-1"
ACCEPTANCE_PACKAGE_NAME = "@hcoona/hcoona-release-smoke-npm"
DUMMY_LOOPBACK_TOKEN = "phase1-loopback-dummy-credential"
ACCEPTANCE_FIXTURE_ROOT = (
    Path(__file__).parents[1]
    / "fixtures"
    / "acceptance"
    / "npm-publish-request"
)
ACCEPTANCE_PACKAGE_FIXTURE_ROOT = ACCEPTANCE_FIXTURE_ROOT / "package"


class NpmPublishCapture:
    def __init__(
        self,
        *,
        method: str,
        path: str,
        headers: dict[str, str],
        body: bytes,
        metadata: dict[str, JsonValue],
        registry: str,
    ) -> None:
        import base64  # noqa: PLC0415

        self.method = method
        self.path = path
        self.headers = headers
        self.body = body
        self.normalized_body = body.replace(
            registry.encode(), b"<loopback-registry>"
        )
        self.metadata = metadata
        self.document = cast("dict[str, Any]", json.loads(body))
        self.version_document = cast(
            "dict[str, Any]",
            self.document["versions"][ACCEPTANCE_VERSION],
        )
        self.version_dist = cast(
            "dict[str, str]", self.version_document["dist"]
        )
        attachment = next(
            iter(cast("dict[str, Any]", self.document["_attachments"]).values())
        )
        self.attachment = base64.b64decode(attachment["data"], validate=True)
        self.attachment_sha1 = hashlib.sha1(
            self.attachment, usedforsecurity=False
        ).hexdigest()
        self.attachment_sha512_base64 = base64.b64encode(
            hashlib.sha512(self.attachment).digest()
        ).decode()
        with tarfile.open(
            fileobj=io.BytesIO(self.attachment), mode="r:gz"
        ) as archive:
            files = {}
            for member in archive.getmembers():
                if not member.isfile():
                    continue
                extracted = archive.extractfile(member)
                assert extracted is not None
                files[member.name] = extracted.read()
        self.attachment_members = tuple(sorted(files))
        self.package_manifest = json.loads(files["package/package.json"])
        self.witness = json.loads(files["package/dist/acceptance-witness.json"])

    def nonsecret_fixture(self) -> dict[str, JsonValue]:
        return cast(
            "dict[str, JsonValue]",
            {
                "attachment-members": list(self.attachment_members),
                "attachment-sha1": self.attachment_sha1,
                "attachment-sha512": self.attachment_sha512_base64,
                "content-length": len(self.body),
                "content-type": self.headers["content-type"],
                "metadata": self.metadata,
                "method": self.method,
                "package-coordinate": ACCEPTANCE_PACKAGE_COORDINATE,
                "path": self.path,
                "normalized-request-body-sha256": hashlib.sha256(
                    self.normalized_body
                ).hexdigest(),
                "tag": ACCEPTANCE_TAG,
                "version": ACCEPTANCE_VERSION,
                "witness": cast("JsonValue", self.witness),
            },
        )


def snapshot_tree(root: Path) -> dict[str, str]:
    return {
        path.relative_to(root).as_posix(): hashlib.sha256(
            path.read_bytes()
        ).hexdigest()
        for path in sorted(root.rglob("*"))
        if path.is_file()
    }


def expected_capture() -> dict[str, JsonValue]:
    return cast(
        "dict[str, JsonValue]",
        json.loads(
            (
                ACCEPTANCE_FIXTURE_ROOT
                / "capture-node-24.19.0-npm-11.17.0.json"
            ).read_bytes()
        ),
    )


def capture_real_npm_publish(tmp_path: Path) -> NpmPublishCapture:
    import os  # noqa: PLC0415
    import shutil  # noqa: PLC0415
    import threading  # noqa: PLC0415
    from http.server import (  # noqa: PLC0415
        BaseHTTPRequestHandler,
        HTTPServer,
    )

    tmp_path.mkdir(parents=True, exist_ok=True)
    # Stop npm from inheriting a parent checkout's gitHead.
    (tmp_path / ".git").mkdir()
    package_root = tmp_path / "package"
    shutil.copytree(ACCEPTANCE_PACKAGE_FIXTURE_ROOT, package_root)
    requests: list[tuple[str, str, dict[str, str], bytes]] = []

    class Handler(BaseHTTPRequestHandler):
        protocol_version = "HTTP/1.1"

        def do_GET(self) -> None:
            response = b'{"error":"not_found"}'
            self.send_response(404)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(response)))
            self.send_header("Connection", "close")
            self.end_headers()
            self.wfile.write(response)
            self.close_connection = True

        def do_PUT(self) -> None:
            length = int(self.headers["Content-Length"])
            requests.append(
                (
                    self.command,
                    self.path,
                    {
                        name.lower(): value
                        for name, value in self.headers.items()
                    },
                    self.rfile.read(length),
                )
            )
            response = (
                b'{"ok":true,"id":"@hcoona/hcoona-release-smoke-npm",'
                b'"rev":"1-loopback"}'
            )
            self.send_response(201)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(response)))
            self.send_header("Connection", "close")
            self.end_headers()
            self.wfile.write(response)
            self.close_connection = True

        def log_message(self, format: str, *args: object) -> None:  # noqa: A002
            del format, args

    server = HTTPServer(("127.0.0.1", 0), Handler)
    server.timeout = TIMEOUT_SECONDS
    port = server.server_address[1]
    registry = f"http://127.0.0.1:{port}"
    npmrc = tmp_path / ".npmrc"
    npmrc.write_text(
        f"//127.0.0.1:{port}/:_authToken={DUMMY_LOOPBACK_TOKEN}\n",
        encoding="utf-8",
    )
    argv = (
        "npm",
        "publish",
        str(package_root),
        "--tag",
        ACCEPTANCE_TAG,
        "--registry",
        registry,
        "--ignore-scripts",
    )
    environment = {
        "HOME": str(tmp_path / "home"),
        "LANG": "C.UTF-8",
        "LC_ALL": "C.UTF-8",
        "NPM_CONFIG_CACHE": str(tmp_path / "npm-cache"),
        "NPM_CONFIG_USERCONFIG": str(npmrc),
        "NO_PROXY": "127.0.0.1",
        "PATH": os.environ["PATH"],
    }
    thread = threading.Thread(target=server.serve_forever)
    thread.start()
    try:
        try:
            completed = subprocess.run(  # noqa: S603
                argv,
                check=False,
                capture_output=True,
                env=environment,
                timeout=TIMEOUT_SECONDS,
            )
        except subprocess.TimeoutExpired as error:
            pytest.fail(
                f"npm timed out; requests={requests!r}; stderr={error.stderr!r}"
            )
    finally:
        server.shutdown()
        thread.join(TIMEOUT_SECONDS)
        server.server_close()
    assert completed.returncode == 0, completed.stderr.decode()
    assert len(requests) == 1
    method, path, headers, body = requests[0]

    def tool_version(*command: str) -> str:
        return subprocess.run(  # noqa: S603
            command,
            check=True,
            capture_output=True,
            env=environment,
            text=True,
            timeout=TIMEOUT_SECONDS,
        ).stdout.strip()

    return NpmPublishCapture(
        method=method,
        path=path,
        headers=headers,
        body=body,
        registry=registry,
        metadata={
            "argv": [
                "npm",
                "publish",
                "<package>",
                "--tag",
                ACCEPTANCE_TAG,
                "--registry",
                "<loopback-registry>",
                "--ignore-scripts",
            ],
            "node-version": tool_version("node", "--version"),
            "npm-version": tool_version("npm", "--version"),
        },
    )


from dataclasses import FrozenInstanceError, replace  # noqa: E402

from three_workflow_delivery_v3.adapters.github_packages import (  # noqa: E402
    ValidatedAcceptanceRequestProof,
)


def _validated_proof(
    tarball: bytes,
    *,
    raw_request: bytes | None = None,
    package_coordinate: str = (
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.4"
    ),
    tag: str = "wdv3-acceptance-4",
    upstream_status: int = 201,
) -> ValidatedAcceptanceRequestProof:
    body = raw_request or _adversarial_publish_body(tarball)
    return ValidatedAcceptanceRequestProof.from_validated_exchange(
        raw_request=body,
        tarball=tarball,
        package_coordinate=package_coordinate,
        tag=tag,
        upstream_status=upstream_status,
        selected_headers={"Content-Type": "application/json", "ETag": '"v1"'},
        response_body=b'{"ok":true}',
    )


@pytest.mark.parametrize("upstream_status", [200, 201])
def test_validated_request_proof_accepts_closed_publish_success_status(
    upstream_status: int,
) -> None:
    tarball = b"created-status-contract"

    proof = _validated_proof(tarball, upstream_status=upstream_status)

    assert proof.upstream_status == upstream_status
    assert proof.to_document()["upstream-status"] == upstream_status


@pytest.mark.parametrize("upstream_status", [202, 204])
def test_validated_request_proof_rejects_other_two_xx_status(
    upstream_status: int,
) -> None:
    tarball = b"unsupported-two-xx-status"

    with pytest.raises(ValueError, match="upstream status"):
        _validated_proof(tarball, upstream_status=upstream_status)


def _send_authorized_proxy_publish(
    proxy: Any,
    body: bytes,
    *,
    token: str,
) -> int | None:
    host, port = proxy.registry.removeprefix("http://").split(":", 1)
    connection = http.client.HTTPConnection(
        host,
        int(port),
        timeout=TIMEOUT_SECONDS,
    )
    try:
        connection.request(
            "PUT",
            "/@hcoona%2fhcoona-release-smoke-npm",
            body=body,
            headers={
                "Authorization": f"Bearer {token}",
                "Content-Type": "application/json",
            },
        )
        return connection.getresponse().status
    except (ConnectionError, http.client.HTTPException):
        return None
    finally:
        connection.close()


@pytest.mark.parametrize("upstream_status", [200, 201])
def test_proxy_validates_captured_couchdb_publish_request(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    upstream_status: int,
) -> None:
    version = "0.0.0-wdv3-acceptance.4"
    tag = "wdv3-acceptance-4"
    tarball = _acceptance_tarball(
        version=version,
        repository_url="git+https://github.com/hcoona/three.git",
        target_sha=TARGET,
    )
    tarball_path = tmp_path / "acceptance.tgz"
    tarball_path.write_bytes(tarball)
    upstream_bodies: list[bytes] = []

    class Response:
        status = upstream_status

        def read(self, _size: int) -> bytes:
            return b'{"ok":true}'

        def getheaders(self) -> list[tuple[str, str]]:
            return [("Content-Type", "application/json"), ("ETag", '"v1"')]

    class Connection:
        def __init__(self, host: str, *, timeout: float) -> None:
            assert host == "npm.pkg.github.com"
            assert timeout == TIMEOUT_SECONDS

        def request(
            self,
            method: str,
            path: str,
            *,
            body: bytes,
            headers: dict[str, str],
        ) -> None:
            assert method == "PUT"
            assert path == "/@hcoona%2fhcoona-release-smoke-npm"
            assert headers["Authorization"] == "Bearer upstream-secret"
            upstream_bodies.append(body)

        def getresponse(self) -> Response:
            return Response()

        def close(self) -> None:
            pass

    monkeypatch.setattr(cli_module.http.client, "HTTPSConnection", Connection)
    with cli_module.AcceptanceMutationProxy(
        timeout_seconds=TIMEOUT_SECONDS,
        token="upstream-secret",
        incoming_dummy_token=DUMMY_LOOPBACK_TOKEN,
        expected_method="PUT",
        expected_path="/@hcoona%2fhcoona-release-smoke-npm",
        expected_version=version,
        expected_tag=tag,
        expected_tarballs=(tarball,),
        expected_target_sha=TARGET,
        drop_accepted_response=False,
    ) as proxy:
        npmrc = tmp_path / ".npmrc"
        authority = proxy.registry.removeprefix("http://")
        npmrc.write_text(
            (
                f"@hcoona:registry={proxy.registry}\n"
                f"//{authority}/:_authToken={DUMMY_LOOPBACK_TOKEN}\n"
            ),
            encoding="utf-8",
        )
        try:
            completed = subprocess.run(  # noqa: S603
                (
                    "npm",
                    "publish",
                    str(tarball_path),
                    "--tag",
                    tag,
                    "--registry",
                    proxy.registry,
                    "--ignore-scripts",
                ),
                check=False,
                capture_output=True,
                env={
                    **cli_module._acceptance_subprocess_environment(
                        npm_config=npmrc
                    ),
                },
                timeout=TIMEOUT_SECONDS,
            )
        except subprocess.TimeoutExpired:
            completed = None
        proof = proxy.proof

    assert completed is not None
    assert completed.returncode == 0
    assert len(upstream_bodies) == 1, (
        proxy.validation_error,
        None if completed is None else completed.stderr.decode(),
    )
    captured = json.loads(upstream_bodies[0])
    assert captured["_id"] == ACCEPTANCE_PACKAGE_NAME
    assert captured["dist-tags"] == {tag: version}
    assert proof is not None
    assert proof.request_digest == (
        "sha256:" + hashlib.sha256(upstream_bodies[0]).hexdigest()
    )
    assert proof.tarball_sha512 == (
        "sha512:" + hashlib.sha512(tarball).hexdigest()
    )
    assert proof.upstream_status == upstream_status


@pytest.mark.parametrize(
    ("mutation", "status"),
    [
        ("coordinate", 422),
        ("version", 422),
        ("tag", 422),
        ("attachment", 422),
    ],
)
def test_proxy_rejects_request_coordinate_version_tag_or_attachment_substitution(
    monkeypatch: pytest.MonkeyPatch,
    mutation: str,
    status: int,
) -> None:
    tarball = _acceptance_tarball(
        version="0.0.0-wdv3-acceptance.4",
        repository_url="git+https://github.com/hcoona/three.git",
        target_sha=TARGET,
    )
    document = json.loads(_adversarial_publish_body(tarball))
    if mutation == "coordinate":
        document["_id"] = "@hcoona/substituted"
    elif mutation == "version":
        document["versions"]["0.0.0-wdv3-acceptance.4"]["version"] = "9.9.9"
    elif mutation == "tag":
        document["dist-tags"] = {"latest": "0.0.0-wdv3-acceptance.4"}
    else:
        document["_attachments"] = {}

    class ForbiddenUpstream:
        def __init__(self, *_args: object, **_kwargs: object) -> None:
            pytest.fail("substituted request reached upstream")

    monkeypatch.setattr(
        cli_module.http.client, "HTTPSConnection", ForbiddenUpstream
    )
    with cli_module.AcceptanceMutationProxy(
        timeout_seconds=TIMEOUT_SECONDS,
        token="upstream-secret",
        incoming_dummy_token=DUMMY_LOOPBACK_TOKEN,
        expected_method="PUT",
        expected_path="/@hcoona%2fhcoona-release-smoke-npm",
        expected_tarballs=(tarball,),
        expected_target_sha=TARGET,
    ) as proxy:
        actual = _send_authorized_proxy_publish(
            proxy,
            canonicalize(cast("JsonValue", document)),
            token=DUMMY_LOOPBACK_TOKEN,
        )

    assert actual == status
    assert proxy.proof is None


@pytest.mark.parametrize("mutation", ["witness", "integrity", "shasum"])
def test_proxy_rejects_witness_or_attachment_hash_substitution(
    monkeypatch: pytest.MonkeyPatch,
    mutation: str,
) -> None:
    tarball = _acceptance_tarball(
        version="0.0.0-wdv3-acceptance.4",
        repository_url="git+https://github.com/hcoona/three.git",
        target_sha=TARGET,
    )
    document = json.loads(_adversarial_publish_body(tarball))
    version_document = document["versions"]["0.0.0-wdv3-acceptance.4"]
    if mutation == "integrity":
        version_document["dist"]["integrity"] = "sha512-" + ("A" * 88)
    elif mutation == "shasum":
        version_document["dist"]["shasum"] = "0" * 40
    else:
        substituted = _acceptance_tarball(
            version="0.0.0-wdv3-acceptance.4",
            repository_url="git+https://github.com/hcoona/three.git",
            target_sha="d" * 40,
        )
        document = json.loads(_adversarial_publish_body(substituted))

    class ForbiddenUpstream:
        def __init__(self, *_args: object, **_kwargs: object) -> None:
            pytest.fail("substituted request reached upstream")

    monkeypatch.setattr(
        cli_module.http.client, "HTTPSConnection", ForbiddenUpstream
    )
    with cli_module.AcceptanceMutationProxy(
        timeout_seconds=TIMEOUT_SECONDS,
        token="upstream-secret",
        incoming_dummy_token=DUMMY_LOOPBACK_TOKEN,
        expected_method="PUT",
        expected_path="/@hcoona%2fhcoona-release-smoke-npm",
        expected_tarballs=(tarball,),
        expected_target_sha=TARGET,
    ) as proxy:
        actual = _send_authorized_proxy_publish(
            proxy,
            canonicalize(cast("JsonValue", document)),
            token=DUMMY_LOOPBACK_TOKEN,
        )

    assert actual == 422
    assert proxy.proof is None


def test_proxy_replaces_dummy_authorization_only_for_mocked_upstream(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    tarball = _acceptance_tarball(
        version="0.0.0-wdv3-acceptance.4",
        repository_url="git+https://github.com/hcoona/three.git",
        target_sha=TARGET,
    )
    forwarded_headers: list[dict[str, str]] = []

    class Response:
        status = 201

        def read(self, _size: int) -> bytes:
            return b"created"

        def getheaders(self) -> list[tuple[str, str]]:
            return []

    class Connection:
        def __init__(self, host: str, *, timeout: float) -> None:
            assert host == "npm.pkg.github.com"
            assert timeout == TIMEOUT_SECONDS

        def request(self, *_args: object, **kwargs: object) -> None:
            forwarded_headers.append(cast("dict[str, str]", kwargs["headers"]))

        def getresponse(self) -> Response:
            return Response()

        def close(self) -> None:
            pass

    monkeypatch.setattr(cli_module.http.client, "HTTPSConnection", Connection)
    with cli_module.AcceptanceMutationProxy(
        timeout_seconds=TIMEOUT_SECONDS,
        token="upstream-secret",
        incoming_dummy_token=DUMMY_LOOPBACK_TOKEN,
        expected_method="PUT",
        expected_path="/@hcoona%2fhcoona-release-smoke-npm",
        expected_tarballs=(tarball,),
        expected_target_sha=TARGET,
    ) as proxy:
        assert (
            _send_authorized_proxy_publish(
                proxy,
                _adversarial_publish_body(tarball),
                token="wrong-dummy",
            )
            == 401
        )
        assert (
            _send_authorized_proxy_publish(
                proxy,
                _adversarial_publish_body(tarball),
                token=DUMMY_LOOPBACK_TOKEN,
            )
            is None
        )

    assert forwarded_headers == [
        {
            "Accept-Encoding": "identity",
            "Content-Type": "application/json",
            "Authorization": "Bearer upstream-secret",
            "Content-Length": str(len(_adversarial_publish_body(tarball))),
        }
    ]


def test_proxy_proof_redacts_incoming_and_upstream_tokens(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    tarball = _acceptance_tarball(
        version="0.0.0-wdv3-acceptance.4",
        repository_url="git+https://github.com/hcoona/three.git",
        target_sha=TARGET,
    )

    class Response:
        status = 201

        def read(self, _size: int) -> bytes:
            return b"created"

        def getheaders(self) -> list[tuple[str, str]]:
            return [("ETag", '"v1"')]

    class Connection:
        def __init__(self, *_args: object, **_kwargs: object) -> None:
            pass

        def request(self, *_args: object, **_kwargs: object) -> None:
            pass

        def getresponse(self) -> Response:
            return Response()

        def close(self) -> None:
            pass

    monkeypatch.setattr(cli_module.http.client, "HTTPSConnection", Connection)
    with cli_module.AcceptanceMutationProxy(
        timeout_seconds=TIMEOUT_SECONDS,
        token="upstream-secret",
        incoming_dummy_token=DUMMY_LOOPBACK_TOKEN,
        expected_method="PUT",
        expected_path="/@hcoona%2fhcoona-release-smoke-npm",
        expected_tarballs=(tarball,),
        expected_target_sha=TARGET,
    ) as proxy:
        assert (
            _send_authorized_proxy_publish(
                proxy,
                _adversarial_publish_body(tarball),
                token=DUMMY_LOOPBACK_TOKEN,
            )
            is None
        )
        proof = proxy.proof

    assert proof is not None
    retained = repr(proof).encode() + canonicalize(proof.to_document())
    assert DUMMY_LOOPBACK_TOKEN.encode() not in retained
    assert b"upstream-secret" not in retained
    assert b"authorization" not in retained.lower()


def test_validated_request_proof_binds_raw_request_and_tarball_digests() -> (
    None
):
    tarball = _acceptance_tarball(
        version="0.0.0-wdv3-acceptance.4",
        repository_url="git+https://github.com/hcoona/three.git",
        target_sha=TARGET,
    )
    raw = _adversarial_publish_body(tarball) + b"\n"
    proof = _validated_proof(tarball, raw_request=raw)

    assert proof.request_digest == "sha256:" + hashlib.sha256(raw).hexdigest()
    assert proof.tarball_sha512 == (
        "sha512:" + hashlib.sha512(tarball).hexdigest()
    )
    with pytest.raises(FrozenInstanceError):
        proof.request_digest = "sha256:" + ("0" * 64)  # type: ignore[misc]
    with pytest.raises(ValueError, match="request digest"):
        replace(proof, request_digest="sha256:" + ("0" * 64))


@pytest.mark.parametrize("upstream_status", [200, 201])
def test_validated_request_proof_binds_upstream_response_identity(
    upstream_status: int,
) -> None:
    tarball = b"proof-tarball"
    proof = _validated_proof(tarball, upstream_status=upstream_status)
    document = proof.to_document()

    assert document["upstream-status"] == upstream_status
    assert document["selected-headers"] == {
        "content-type": "application/json",
        "etag": '"v1"',
    }
    assert document["response-body-digest"] == (
        "sha256:" + hashlib.sha256(b'{"ok":true}').hexdigest()
    )
    with pytest.raises(ValueError, match="response identity"):
        replace(
            proof,
            response_identity_digest="sha256:" + ("0" * 64),
        )


def test_validated_request_proof_status_changes_response_identity() -> None:
    tarball = b"status-bound-proof-tarball"

    proof_200 = _validated_proof(tarball, upstream_status=200)
    proof_201 = _validated_proof(tarball, upstream_status=201)

    assert proof_200.request_digest == proof_201.request_digest
    assert proof_200.response_body_digest == proof_201.response_body_digest
    assert (
        proof_200.response_identity_digest != proof_201.response_identity_digest
    )


@pytest.mark.parametrize("substitution", ["tarball", "coordinate", "tag"])
def test_acceptance_probe_rejects_request_proof_substitutions(
    tmp_path: Path,
    substitution: str,
) -> None:
    tarball = tmp_path / "lost-response.tgz"
    tarball.write_bytes(b"expected-lost-response-tarball")
    coordinate = ACCEPTANCE_COORDINATES["lost-response"]
    tag = TAGS["lost-response"]
    proof_tarball = (
        b"substituted" if substitution == "tarball" else b"lost-response"
    )
    proof = _validated_proof(
        proof_tarball,
        package_coordinate=coordinate,
        tag=tag,
    )
    if substitution == "coordinate":
        object.__setattr__(
            proof,
            "package_coordinate",
            "@hcoona/hcoona-release-smoke-npm@9.9.9",
        )
    elif substitution == "tag":
        object.__setattr__(proof, "tag", "latest")
    runner = ControlledRunner()
    runner.run_scenario = lambda *_args, **_kwargs: {
        "outcome": "lost-response-processed",
        "action-executed": True,
        "mutation-started": True,
        "validated-request-proof": proof,
    }
    content = "sha512:" + hashlib.sha512(tarball.read_bytes()).hexdigest()

    result, _, _ = _run(
        tmp_path,
        scenario="lost-response",
        observations=[
            _absent(),
            _state("exact", scenario="lost-response", content=content),
        ],
        runner=runner,
    )

    assert result.result == "lost-response"
    assert result.mutation_classification == "unknown"
    assert result.validated_request_proof is None


def test_acceptance_probe_uses_validated_proof_not_synthetic_body(
    tmp_path: Path,
) -> None:
    tarball = tmp_path / "lost-response.tgz"
    tarball.write_bytes(b"lost-response")
    document = json.loads(_adversarial_publish_body(b"lost-response"))
    document["npm-client-field"] = {"actual": True}
    actual_raw_request = json.dumps(
        document, indent=2, sort_keys=False
    ).encode()
    proof = _validated_proof(
        b"lost-response",
        raw_request=actual_raw_request,
    )
    runner = ControlledRunner()
    runner.run_scenario = lambda *_args, **_kwargs: {
        "outcome": "lost-response-processed",
        "action-executed": True,
        "mutation-started": True,
        "validated-request-proof": proof,
    }
    content = "sha512:" + hashlib.sha512(b"lost-response").hexdigest()

    result, _, _ = _run(
        tmp_path,
        scenario="lost-response",
        observations=[
            _absent(),
            _state("exact", scenario="lost-response", content=content),
        ],
        runner=runner,
    )

    assert result.result == "lost-response-exact-after-start"
    assert result.mutation_classification == "complete"
    assert result.validated_request_proof is proof
    assert (
        result.to_document()["validated-request-proof"] == proof.to_document()
    )


def test_adapter_exports_validated_acceptance_request_proof() -> None:
    assert (
        adapter_package.ValidatedAcceptanceRequestProof
        is ValidatedAcceptanceRequestProof
    )
    assert "ValidatedAcceptanceRequestProof" in adapter_package.__all__


class DeadlineAwareTransport(RecordingTransport):
    def __init__(self, observations: list[dict[str, Any]]) -> None:
        super().__init__(observations)
        self.deadlines: list[float] = []

    def observe(
        self,
        coordinate: str,
        tag: str,
        *,
        timeout_seconds: float,
        max_response_bytes: int,
        deadline: float | None = None,
    ) -> dict[str, Any]:
        assert deadline is not None
        self.deadlines.append(deadline)
        return super().observe(
            coordinate,
            tag,
            timeout_seconds=timeout_seconds,
            max_response_bytes=max_response_bytes,
        )


class DeadlineAwareRunner:
    def __init__(self, result: dict[str, object] | BaseException) -> None:
        self.result = result
        self.deadlines: list[float] = []
        self.timeouts: list[float] = []

    def run(
        self,
        argv: tuple[str, ...],
        *,
        env: dict[str, str],
        timeout_seconds: float,
        max_output_bytes: int,
        deadline: float,
    ) -> dict[str, object]:
        del argv, env, max_output_bytes
        self.deadlines.append(deadline)
        self.timeouts.append(timeout_seconds)
        if isinstance(self.result, BaseException):
            raise self.result
        return dict(self.result)


def _runner_failure(
    kind: str,
) -> OSError | TimeoutError:
    if kind == "pre-start":
        return OSError("runner failed before process creation")
    if kind == "post-spawn":
        error = OSError("local failure after process creation")
        error.action_executed = True  # type: ignore[attr-defined]
        error.mutation_started = False  # type: ignore[attr-defined]
        return error
    error = TimeoutError("proxy observed the request before timeout")
    error.action_executed = True  # type: ignore[attr-defined]
    error.mutation_started = True  # type: ignore[attr-defined]
    return error


def _run_deadline_probe(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> tuple[
    FixedCoordinateAcceptanceProbeResult,
    DeadlineAwareTransport,
    DeadlineAwareRunner,
]:
    from three_workflow_delivery_v3.adapters import (  # noqa: PLC0415
        github_packages as module,
    )

    clock = iter((100.0, 101.0, 102.0, 103.0))
    monkeypatch.setattr(module, "monotonic", lambda: next(clock))
    scenario = "absent-create-readback"
    tarball = tmp_path / "deadline.tgz"
    tarball.write_bytes(b"one-deadline")
    content = "sha512:" + hashlib.sha512(tarball.read_bytes()).hexdigest()
    transport = DeadlineAwareTransport(
        [_absent(), _state("exact", scenario=scenario, content=content)]
    )
    runner = DeadlineAwareRunner(
        {
            "outcome": "created",
            "action-executed": True,
            "mutation-started": True,
        }
    )
    result = run_fixed_coordinate_acceptance_probe(
        scenario=scenario,
        package_coordinate=ACCEPTANCE_COORDINATES[scenario],
        tag=TAGS[scenario],
        tarball=tarball,
        tarball_sha512=content,
        transport=transport,
        runner=runner,
        timeout_seconds=TIMEOUT_SECONDS,
        max_response_bytes=MAX_RESPONSE_BYTES,
        max_output_bytes=MAX_OUTPUT_BYTES,
    )
    return result, transport, runner


def test_acceptance_operation_uses_one_monotonic_deadline(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    result, transport, runner = _run_deadline_probe(tmp_path, monkeypatch)

    assert result.mutation_classification == "incomplete"
    assert transport.deadlines == [107.0, 107.0]
    assert runner.deadlines == [107.0]


def test_acceptance_deadline_budget_decreases_across_all_boundaries(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    _result, transport, runner = _run_deadline_probe(tmp_path, monkeypatch)

    assert [call[2] for call in transport.calls] == [6.0, 4.0]
    assert runner.timeouts == [5.0]


def test_acceptance_deadline_is_not_reset_by_proxy_or_observation(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    observed_timeouts: list[float] = []

    class Response:
        status = 404
        body = b'{"message":"Not Found"}'
        truncated = False
        complete = True

    transport = cli_module._AcceptanceNpmTransport(
        Path("/unused/npmrc"),
        token="local-test-token",
        target_sha=TARGET,
    )
    monkeypatch.setattr(cli_module, "monotonic", lambda: 105.0)

    def authenticated_get(
        _url: str,
        *,
        headers: tuple[tuple[str, str], ...],
        timeout: float,
        max_bytes: int,
    ) -> Response:
        del headers, max_bytes
        observed_timeouts.append(timeout)
        return Response()

    monkeypatch.setattr(
        transport,
        "_authenticated_get",
        authenticated_get,
    )

    result = transport.observe(
        ACCEPTANCE_COORDINATES["absent-create-readback"],
        TAGS["absent-create-readback"],
        timeout_seconds=99.0,
        max_response_bytes=MAX_RESPONSE_BYTES,
        deadline=110.0,
    )

    assert result["state"] == "absent"
    assert observed_timeouts == [5.0]


def test_acceptance_cleanup_uses_only_remaining_deadline_budget(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    calls: list[tuple[str, float | None]] = []

    class Process:
        def poll(self) -> None:
            return None

        def kill(self) -> None:
            calls.append(("kill", None))

        def communicate(self, *, timeout: float) -> tuple[str, str]:
            calls.append(("communicate", timeout))
            return "", ""

    monkeypatch.setattr(cli_module, "monotonic", lambda: 105.5)

    cli_module._AcceptanceNpmRunner._cleanup_processes(
        [cast("Any", Process())],
        deadline=107.0,
    )

    assert calls == [("kill", None), ("communicate", 1.5)]


@pytest.mark.parametrize(
    (
        "runner_value",
        "expected_executed",
        "expected_started",
        "expected_classification",
    ),
    [
        ({"outcome": "created"}, False, False, "incomplete"),
        (
            {"outcome": "created", "action-executed": True},
            False,
            False,
            "incomplete",
        ),
        (
            {"outcome": "created", "mutation-started": True},
            False,
            False,
            "incomplete",
        ),
        (
            {
                "outcome": "created",
                "action-executed": 1,
                "mutation-started": "yes",
            },
            False,
            False,
            "incomplete",
        ),
        (
            {
                "outcome": "created",
                "action-executed": False,
                "mutation-started": True,
            },
            False,
            False,
            "incomplete",
        ),
        (
            _runner_failure("pre-start"),
            False,
            False,
            "incomplete",
        ),
        (
            _runner_failure("post-spawn"),
            True,
            False,
            "incomplete",
        ),
        (
            _runner_failure("timeout"),
            True,
            True,
            "unknown",
        ),
        (
            {
                "outcome": "created",
                "action-executed": True,
                "mutation-started": True,
            },
            True,
            True,
            "incomplete",
        ),
    ],
    ids=(
        "no-facts",
        "executed-only",
        "started-only",
        "wrong-types",
        "contradictory",
        "pre-start-failure",
        "local-post-spawn-failure",
        "proxy-observed-timeout",
        "proof-free-created",
    ),
)
def test_acceptance_runner_proof_fact_matrix(
    tmp_path: Path,
    runner_value: dict[str, object] | BaseException,
    expected_executed: bool,
    expected_started: bool,
    expected_classification: str,
) -> None:
    scenario = "absent-create-readback"
    tarball = tmp_path / "runner-facts.tgz"
    tarball.write_bytes(b"runner-facts")
    content = "sha512:" + hashlib.sha512(tarball.read_bytes()).hexdigest()
    runner = DeadlineAwareRunner(runner_value)
    transport = DeadlineAwareTransport(
        [_absent(), _state("exact", scenario=scenario, content=content)]
    )

    result = run_fixed_coordinate_acceptance_probe(
        scenario=scenario,
        package_coordinate=ACCEPTANCE_COORDINATES[scenario],
        tag=TAGS[scenario],
        tarball=tarball,
        tarball_sha512=content,
        transport=transport,
        runner=runner,
        timeout_seconds=TIMEOUT_SECONDS,
        max_response_bytes=MAX_RESPONSE_BYTES,
        max_output_bytes=MAX_OUTPUT_BYTES,
    )

    assert result.action_executed is expected_executed
    assert result.mutation_started is expected_started
    assert result.mutation_classification == expected_classification
    assert result.result != "created"


def test_missing_or_partial_runner_facts_never_default_to_mutation_started(
    tmp_path: Path,
) -> None:
    scenario = "absent-create-readback"
    tarball = tmp_path / "missing-facts.tgz"
    tarball.write_bytes(b"missing-facts")
    content = "sha512:" + hashlib.sha512(tarball.read_bytes()).hexdigest()
    result = run_fixed_coordinate_acceptance_probe(
        scenario=scenario,
        package_coordinate=ACCEPTANCE_COORDINATES[scenario],
        tag=TAGS[scenario],
        tarball=tarball,
        tarball_sha512=content,
        transport=DeadlineAwareTransport(
            [_absent(), _state("exact", scenario=scenario, content=content)]
        ),
        runner=DeadlineAwareRunner({"outcome": "created"}),
        timeout_seconds=TIMEOUT_SECONDS,
        max_response_bytes=MAX_RESPONSE_BYTES,
        max_output_bytes=MAX_OUTPUT_BYTES,
    )

    assert result.action_executed is False
    assert result.mutation_started is False
    assert result.mutation_classification == "incomplete"
    assert result.diagnostics == ("runner-action-facts-not-fully-admitted",)


def test_only_fully_validated_runner_proof_can_form_complete_evidence(
    tmp_path: Path,
) -> None:
    scenario = "absent-create-readback"
    tarball = tmp_path / "validated-runner-proof.tgz"
    tarball.write_bytes(b"validated-runner-proof")
    content = "sha512:" + hashlib.sha512(tarball.read_bytes()).hexdigest()
    observations = [
        _absent(),
        _state("exact", scenario=scenario, content=content),
    ]

    partial = DeadlineAwareRunner(
        {"outcome": "created", "action-executed": True}
    )
    partial_result = run_fixed_coordinate_acceptance_probe(
        scenario=scenario,
        package_coordinate=ACCEPTANCE_COORDINATES[scenario],
        tag=TAGS[scenario],
        tarball=tarball,
        tarball_sha512=content,
        transport=DeadlineAwareTransport(list(observations)),
        runner=partial,
        timeout_seconds=TIMEOUT_SECONDS,
        max_response_bytes=MAX_RESPONSE_BYTES,
        max_output_bytes=MAX_OUTPUT_BYTES,
    )
    proof = _validated_proof(
        tarball.read_bytes(),
        package_coordinate=ACCEPTANCE_COORDINATES[scenario],
        tag=TAGS[scenario],
    )
    complete = DeadlineAwareRunner(
        {
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
    )
    complete_result = run_fixed_coordinate_acceptance_probe(
        scenario=scenario,
        package_coordinate=ACCEPTANCE_COORDINATES[scenario],
        tag=TAGS[scenario],
        tarball=tarball,
        tarball_sha512=content,
        transport=DeadlineAwareTransport(list(observations)),
        runner=complete,
        timeout_seconds=TIMEOUT_SECONDS,
        max_response_bytes=MAX_RESPONSE_BYTES,
        max_output_bytes=MAX_OUTPUT_BYTES,
    )

    assert partial_result.mutation_classification == "incomplete"
    assert partial_result.result == "runner-malformed-before-mutation"
    assert complete_result.mutation_classification == "complete"
    assert complete_result.result == "protocol-confirmed"


@pytest.mark.parametrize(
    ("runner_document", "expected_executed", "expected_started"),
    [
        ({"outcome": "lost-response-processed"}, False, False),
        (
            {
                "outcome": "lost-response-processed",
                "action-executed": True,
            },
            False,
            False,
        ),
        (
            {
                "outcome": "lost-response-processed",
                "action-executed": False,
                "mutation-started": True,
            },
            False,
            False,
        ),
    ],
    ids=["missing-facts", "partial-facts", "contradictory-facts"],
)
def test_lost_response_malformed_runner_facts_fail_before_ambiguity(
    tmp_path: Path,
    runner_document: dict[str, object],
    expected_executed: bool,
    expected_started: bool,
) -> None:
    scenario = "lost-response"
    tarball = tmp_path / "malformed-lost-response.tgz"
    tarball.write_bytes(b"malformed-lost-response")
    content = "sha512:" + hashlib.sha512(tarball.read_bytes()).hexdigest()

    result = run_fixed_coordinate_acceptance_probe(
        scenario=scenario,
        package_coordinate=ACCEPTANCE_COORDINATES[scenario],
        tag=TAGS[scenario],
        tarball=tarball,
        tarball_sha512=content,
        transport=DeadlineAwareTransport(
            [_absent(), _state("exact", scenario=scenario, content=content)]
        ),
        runner=DeadlineAwareRunner(runner_document),
        timeout_seconds=TIMEOUT_SECONDS,
        max_response_bytes=MAX_RESPONSE_BYTES,
        max_output_bytes=MAX_OUTPUT_BYTES,
    )

    assert result.result == "runner-malformed-before-mutation"
    assert result.mutation_classification == "incomplete"
    assert result.action_executed is expected_executed
    assert result.mutation_started is expected_started
    assert result.diagnostics == ("runner-action-facts-not-fully-admitted",)


@pytest.mark.parametrize("upstream_status", [409, 500])
def test_acceptance_probe_rejects_runner_supplied_non_success_proof(
    tmp_path: Path,
    upstream_status: int,
) -> None:
    scenario = "lost-response"
    tarball = tmp_path / "non-success-proof.tgz"
    tarball.write_bytes(b"runner-supplied-non-success-proof")
    content = "sha512:" + hashlib.sha512(tarball.read_bytes()).hexdigest()
    proof = _validated_proof(
        tarball.read_bytes(),
        package_coordinate=ACCEPTANCE_COORDINATES[scenario],
        tag=TAGS[scenario],
    )
    object.__setattr__(proof, "upstream_status", upstream_status)
    runner = DeadlineAwareRunner(
        {
            "outcome": "lost-response-processed",
            "action-executed": True,
            "mutation-started": True,
            "validated-request-proof": proof,
        }
    )

    result = run_fixed_coordinate_acceptance_probe(
        scenario=scenario,
        package_coordinate=ACCEPTANCE_COORDINATES[scenario],
        tag=TAGS[scenario],
        tarball=tarball,
        tarball_sha512=content,
        transport=DeadlineAwareTransport(
            [_absent(), _state("exact", scenario=scenario, content=content)]
        ),
        runner=runner,
        timeout_seconds=TIMEOUT_SECONDS,
        max_response_bytes=MAX_RESPONSE_BYTES,
        max_output_bytes=MAX_OUTPUT_BYTES,
    )

    assert result.post_state == "exact"
    assert result.result == "lost-response"
    assert result.mutation_classification == "unknown"
    assert result.validated_request_proof is None
    assert "validated-request-proof" not in result.to_document()


class _CleanupProcess:
    def __init__(
        self,
        pid: int,
        events: list[tuple[str, int, float | None]],
        *,
        timeout_on_reap: bool = False,
    ) -> None:
        self.pid = pid
        self.events = events
        self.timeout_on_reap = timeout_on_reap
        self.returncode: int | None = None

    def poll(self) -> int | None:
        return self.returncode

    def kill(self) -> None:
        self.events.append(("kill", self.pid, None))
        self.returncode = -9

    def communicate(self, timeout: float | None = None) -> tuple[str, str]:
        self.events.append(("wait", self.pid, timeout))
        if self.timeout_on_reap:
            raise subprocess.TimeoutExpired(
                ("npm", "publish"),
                timeout if timeout is not None else 0.0,
            )
        return "", ""


def test_cleanup_signals_every_started_process_before_reaping() -> None:
    events: list[tuple[str, int, float | None]] = []
    processes = [_CleanupProcess(pid, events) for pid in (101, 202, 303)]

    cli_module._AcceptanceNpmRunner._cleanup_processes(cast("Any", processes))

    assert events[:3] == [
        ("kill", 101, None),
        ("kill", 202, None),
        ("kill", 303, None),
    ]
    assert events[3:] == [
        ("wait", 101, None),
        ("wait", 202, None),
        ("wait", 303, None),
    ]
    assert [pid for event, pid, _ in events if event == "kill"] == [
        101,
        202,
        303,
    ]


def test_cleanup_reaps_all_processes_with_one_absolute_deadline(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    events: list[tuple[str, int, float | None]] = []
    processes = [
        _CleanupProcess(11, events, timeout_on_reap=True),
        _CleanupProcess(22, events),
        _CleanupProcess(33, events),
    ]
    clock = iter((100.0, 101.5, 103.0))
    monkeypatch.setattr(cli_module, "monotonic", lambda: next(clock))

    cli_module._AcceptanceNpmRunner._cleanup_processes(
        cast("Any", processes),
        deadline=105.0,
    )

    waits = [event for event in events if event[0] == "wait"]
    assert waits == [
        ("wait", 11, 5.0),
        ("wait", 22, 3.5),
        ("wait", 33, 2.0),
    ]
    assert [event[:2] for event in events[:3]] == [
        ("kill", 11),
        ("kill", 22),
        ("kill", 33),
    ]


def test_timeout_classification_is_immutable_after_late_process_completion(
    tmp_path: Path,
) -> None:
    timeout = TimeoutError("acceptance npm scenario timed out")
    timeout.action_executed = True  # type: ignore[attr-defined]
    timeout.mutation_started = True  # type: ignore[attr-defined]
    runner = DeadlineAwareRunner(timeout)
    result, _, _ = _run(
        tmp_path,
        scenario="absent-create-readback",
        observations=[
            _absent(),
            {"state": "unknown", "response-identity-digest": RESPONSE_B},
        ],
        runner=runner,
    )
    snapshot = canonicalize(cast("JsonValue", result.to_document()))

    timeout.action_executed = False  # type: ignore[attr-defined]
    timeout.mutation_started = False  # type: ignore[attr-defined]
    timeout.contender_outcomes = ("created", "create-conflict")  # type: ignore[attr-defined]
    timeout.winner_content_sha512 = "sha512:" + ("f" * 128)  # type: ignore[attr-defined]
    timeout.validated_request_proof = object()  # type: ignore[attr-defined]

    assert canonicalize(cast("JsonValue", result.to_document())) == snapshot
    assert result.result == "timeout"
    assert result.mutation_classification == "unknown"
    assert result.action_executed is True
    assert result.mutation_started is True
    assert "validated-request-proof" not in result.to_document()
    assert "contenders" not in result.to_document()


def test_partial_startup_cleanup_signals_only_started_processes(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    events: list[tuple[str, int, float | None]] = []
    started = _CleanupProcess(404, events)
    attempts = 0

    def popen(*_args: object, **_kwargs: object) -> _CleanupProcess:
        nonlocal attempts
        attempts += 1
        if attempts == 2:
            message = "second contender did not start"
            raise OSError(message)
        return started

    monkeypatch.setattr(cli_module.subprocess, "Popen", popen)
    runner = cli_module._AcceptanceNpmRunner(
        tmp_path / ".npmrc",
        contender_tarballs={},
    )

    with pytest.raises(OSError, match="second contender did not start"):
        runner.run_scenario(
            "identical-race",
            ("npm", "publish", str(tmp_path / "package.tgz")),
            env={},
            timeout_seconds=TIMEOUT_SECONDS,
            max_output_bytes=MAX_OUTPUT_BYTES,
        )

    assert attempts == 2
    assert events == [("kill", 404, None), ("wait", 404, None)]


class _ReadbackApiTransport:
    def __init__(self, version: str) -> None:
        self.version = version

    def get(
        self,
        url: str,
        *,
        headers: tuple[tuple[str, str], ...],
        timeout: float,
        max_bytes: int,
    ) -> GitHubPackagesHttpResponse:
        del headers, timeout, max_bytes
        if "/versions?" in url:
            body: object = [
                {"name": self.version, "metadata": {"package_type": "npm"}}
            ]
        else:
            body = {
                "package_type": "npm",
                "name": "hcoona-release-smoke-npm",
                "owner": {"login": "hcoona"},
                "repository": {"full_name": "hcoona/three"},
            }
        return GitHubPackagesHttpResponse(
            status=200,
            url=url,
            headers=(),
            body=json.dumps(body).encode(),
        )


def _exercise_readback(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    *,
    stdout: str = "{}",
    returncode: int = 1,
) -> tuple[Path, tuple[str, ...], dict[str, str], str, int]:
    token = "dedicated-readback-token"
    shared_config = tmp_path / "publish-proxy.npmrc"
    shared_config.write_text(
        "@hcoona:registry=http://127.0.0.1:4873\n"
        "//127.0.0.1:4873/:_authToken=wdv3-loopback-dummy-token\n",
        encoding="utf-8",
    )
    captured: dict[str, object] = {}

    def run(
        argv: tuple[str, ...],
        **kwargs: object,
    ) -> subprocess.CompletedProcess[str]:
        env = cast("dict[str, str]", kwargs["env"])
        config = Path(env["NPM_CONFIG_USERCONFIG"])
        captured.update(
            argv=argv,
            env=dict(env),
            config=config,
            content=config.read_text(encoding="utf-8"),
            mode=config.stat().st_mode & 0o777,
        )
        return subprocess.CompletedProcess(
            argv,
            returncode,
            stdout=stdout,
            stderr="readback failed" if returncode else "",
        )

    monkeypatch.setattr(cli_module.subprocess, "run", run)
    scenario = "exact"
    transport = cli_module._AcceptanceNpmTransport(
        shared_config,
        token=token,
        target_sha=TARGET,
    )
    transport._transport = _ReadbackApiTransport(
        ACCEPTANCE_COORDINATES[scenario].rsplit("@", 1)[1]
    )
    transport.observe(
        ACCEPTANCE_COORDINATES[scenario],
        TAGS[scenario],
        timeout_seconds=TIMEOUT_SECONDS,
        max_response_bytes=MAX_RESPONSE_BYTES,
    )
    return (
        cast("Path", captured["config"]),
        cast("tuple[str, ...]", captured["argv"]),
        cast("dict[str, str]", captured["env"]),
        cast("str", captured["content"]),
        cast("int", captured["mode"]),
    )


def test_authenticated_readback_uses_dedicated_ephemeral_npm_config(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    config, argv, environment, content, mode = _exercise_readback(
        tmp_path, monkeypatch
    )
    token = "dedicated-readback-token"

    assert config != tmp_path / "publish-proxy.npmrc"
    assert mode == 0o600
    assert content == (
        "@hcoona:registry=https://npm.pkg.github.com\n"
        "//npm.pkg.github.com/:_authToken=dedicated-readback-token\n"
        "ignore-scripts=true\n"
    )
    assert token not in "\0".join(argv)
    assert token not in repr(environment)
    assert token not in "readback failed"
    assert argv == (
        "npm",
        "view",
        ACCEPTANCE_COORDINATES["exact"],
        "version",
        "dist.tarball",
        "dist-tags",
        "--json",
        "--registry",
        "https://npm.pkg.github.com",
    )
    assert argv[2] == ACCEPTANCE_COORDINATES["exact"]
    assert not {
        "access",
        "adduser",
        "deprecate",
        "dist-tag",
        "login",
        "logout",
        "owner",
        "publish",
        "star",
        "team",
        "token",
        "unpublish",
    }.intersection(argv)
    expected_environment = (
        *(
            name
            for name in ("HOME", "LANG", "LC_ALL", "PATH", "TMPDIR")
            if name in environment
        ),
        "NPM_CONFIG_USERCONFIG",
    )
    assert tuple(environment) == expected_environment


def test_authenticated_readback_config_is_deleted_on_success(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    config, _, _, _, _ = _exercise_readback(tmp_path, monkeypatch)

    assert not config.exists()
    assert (tmp_path / "publish-proxy.npmrc").exists()


def test_authenticated_readback_config_is_deleted_on_failure(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    captured: dict[str, Path] = {}
    shared_config = tmp_path / "publish-proxy.npmrc"
    shared_config.write_text("ignore-scripts=true\n", encoding="utf-8")

    def malformed(
        argv: tuple[str, ...],
        **kwargs: object,
    ) -> subprocess.CompletedProcess[str]:
        config = Path(
            cast("dict[str, str]", kwargs["env"])["NPM_CONFIG_USERCONFIG"]
        )
        captured["config"] = config
        assert config.exists()
        return subprocess.CompletedProcess(
            argv, 0, stdout="{malformed", stderr=""
        )

    monkeypatch.setattr(cli_module.subprocess, "run", malformed)
    transport = cli_module._AcceptanceNpmTransport(
        shared_config,
        token="dedicated-readback-token",
        target_sha=TARGET,
    )
    transport._transport = _ReadbackApiTransport(
        ACCEPTANCE_COORDINATES["exact"].rsplit("@", 1)[1]
    )

    with pytest.raises(json.JSONDecodeError):
        transport.observe(
            ACCEPTANCE_COORDINATES["exact"],
            TAGS["exact"],
            timeout_seconds=TIMEOUT_SECONDS,
            max_response_bytes=MAX_RESPONSE_BYTES,
        )

    assert not captured["config"].exists()
    assert shared_config.exists()


def test_publish_proxy_config_never_contains_dedicated_readback_token(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    dedicated = "dedicated-readback-token"
    dummy = cli_module._ACCEPTANCE_LOOPBACK_DUMMY_TOKEN
    observed_configs: list[str] = []

    class Proxy:
        registry = "http://127.0.0.1:4873"
        observed = cli_module.threading.Event()
        processed = cli_module.threading.Event()
        proof = None

        def __init__(self, **_kwargs: object) -> None:
            pass

        def __enter__(self) -> Self:
            return self

        def __exit__(self, *_args: object) -> None:
            pass

    class Process:
        returncode = 1

        def communicate(self, timeout: float | None = None) -> tuple[str, str]:
            assert timeout is not None
            return "", "local failure"

        def poll(self) -> int:
            return self.returncode

        def kill(self) -> None:
            self.returncode = -9

    def popen(*_args: object, **kwargs: object) -> Process:
        config = Path(
            cast("dict[str, str]", kwargs["env"])["NPM_CONFIG_USERCONFIG"]
        )
        observed_configs.append(config.read_text(encoding="utf-8"))
        return Process()

    monkeypatch.setattr(cli_module, "_LostResponseProxy", Proxy)
    monkeypatch.setattr(cli_module.subprocess, "Popen", popen)
    monkeypatch.setattr(cli_module, "_SYSTEM_POPEN", popen)
    runner = cli_module._AcceptanceNpmRunner(
        tmp_path / "readback.npmrc",
        contender_tarballs={},
        token=dedicated,
    )
    runner.run_scenario(
        "lost-response",
        ("npm", "publish", str(tmp_path / "package.tgz")),
        env={},
        timeout_seconds=TIMEOUT_SECONDS,
        max_output_bytes=MAX_OUTPUT_BYTES,
    )

    assert len(observed_configs) == 1
    assert dummy in observed_configs[0]
    assert dedicated not in observed_configs[0]
    assert observed_configs[0].count("_authToken=") == 1


@pytest.mark.parametrize(
    ("suite", "inventory"),
    [
        ("absent-create-readback", ("absent-create-readback",)),
        (
            "exact-and-conflict",
            ("exact", "identical-race", "differing-race", "lost-response"),
        ),
    ],
)
def test_acceptance_suite_uses_one_absolute_deadline_across_scenarios(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    suite: str,
    inventory: tuple[str, ...],
) -> None:
    from three_workflow_delivery_v3.adapters import (  # noqa: PLC0415
        github_packages as module,
    )

    class Clock:
        now = 100.0

        def monotonic(self) -> float:
            return self.now

    class Transport:
        def observe(
            self,
            coordinate: str,
            tag: str,
            *,
            timeout_seconds: float,
            max_response_bytes: int,
            deadline: float,
        ) -> dict[str, object]:
            del tag, max_response_bytes
            calls.append((timeout_seconds, deadline))
            clock.now += 1.0
            scenario = next(
                name
                for name in inventory
                if ACCEPTANCE_COORDINATES[name] == coordinate
            )
            if scenario == "exact":
                content = (
                    "sha512:"
                    + hashlib.sha512(
                        tarballs[scenario].read_bytes()
                    ).hexdigest()
                )
                return _state("exact", scenario=scenario, content=content)
            return _absent()

    class Runner:
        def run(
            self,
            _argv: tuple[str, ...],
            *,
            env: dict[str, str],
            timeout_seconds: float,
            max_output_bytes: int,
            deadline: float,
        ) -> dict[str, object]:
            del env, max_output_bytes
            calls.append((timeout_seconds, deadline))
            clock.now += 1.0
            return {
                "outcome": "created",
                "action-executed": True,
                "mutation-started": True,
            }

    clock = Clock()
    monkeypatch.setattr(module, "monotonic", clock.monotonic)
    calls: list[tuple[float, float]] = []
    tarballs = {}
    for scenario in inventory:
        tarball = tmp_path / f"{scenario}.tgz"
        tarball.write_bytes(scenario.encode())
        tarballs[scenario] = tarball
    transport = Transport()
    runner = Runner()

    module.run_fixed_acceptance_suite(
        suite=suite,
        tarballs=tarballs,
        transport=transport,
        runner=runner,
        timeout_seconds=20.0,
        max_response_bytes=MAX_RESPONSE_BYTES,
        max_output_bytes=MAX_OUTPUT_BYTES,
    )

    assert {deadline for _, deadline in calls} == {120.0}
    budgets = [budget for budget, _ in calls]
    assert all(
        budgets[index + 1] < budgets[index] for index in range(len(budgets) - 1)
    )
    assert budgets[0] == 20.0
    assert all(budget < 20.0 for budget in budgets[1:])


@pytest.mark.parametrize(
    ("returncode", "expected_outcome", "expected_state"),
    [(0, "success", "exact"), (1, "failed", "unknown")],
)
def test_readback_retains_sanitized_result_and_observation_diagnostics(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    returncode: int,
    expected_outcome: str,
    expected_state: str,
) -> None:
    dedicated = "dedicated-readback-token"
    scenario = "exact"
    coordinate = ACCEPTANCE_COORDINATES[scenario]
    version = coordinate.rsplit("@", 1)[1]
    tag = TAGS[scenario]
    tarball = _acceptance_tarball(
        version=version,
        repository_url="git+https://github.com/hcoona/three.git",
        target_sha=TARGET,
    )
    auth_line = "//npm.pkg.github.com/:_authToken=dedicated-readback-token"
    config_contents: list[str] = []
    shared_config = tmp_path / "publish-proxy.npmrc"
    shared_config.write_text(
        "@hcoona:registry=http://127.0.0.1:4873\n"
        "//127.0.0.1:4873/:_authToken=wdv3-loopback-dummy-token\n",
        encoding="utf-8",
    )

    class ReadbackTransport(_ReadbackApiTransport):
        def get(
            self,
            url: str,
            *,
            headers: tuple[tuple[str, str], ...],
            timeout: float,
            max_bytes: int,
        ) -> GitHubPackagesHttpResponse:
            if url == "https://npm.pkg.github.com/readback.tgz":
                return GitHubPackagesHttpResponse(
                    status=200,
                    url=url,
                    headers=(),
                    body=tarball,
                )
            return super().get(
                url,
                headers=headers,
                timeout=timeout,
                max_bytes=max_bytes,
            )

    successful_stdout = json.dumps(
        {
            "version": version,
            "dist.tarball": "https://npm.pkg.github.com/readback.tgz",
            "dist-tags": {tag: version},
        }
    )

    def run(
        argv: tuple[str, ...],
        **kwargs: object,
    ) -> subprocess.CompletedProcess[str]:
        config = Path(
            cast("dict[str, str]", kwargs["env"])["NPM_CONFIG_USERCONFIG"]
        )
        config_contents.append(config.read_text(encoding="utf-8"))
        stdout = (
            successful_stdout
            if returncode == 0
            else f"npm error token={dedicated}"
        )
        stderr = (
            "npm notice exact readback"
            if returncode == 0
            else f"readback denied; {auth_line}; config={config}"
        )
        return subprocess.CompletedProcess(
            argv,
            returncode,
            stdout=stdout,
            stderr=stderr,
        )

    monkeypatch.setattr(cli_module.subprocess, "run", run)
    transport = cli_module._AcceptanceNpmTransport(
        shared_config,
        token=dedicated,
        target_sha=TARGET,
    )
    transport._transport = ReadbackTransport(version)

    observation = transport.observe(
        coordinate,
        tag,
        timeout_seconds=TIMEOUT_SECONDS,
        max_response_bytes=MAX_RESPONSE_BYTES,
    )

    readback_result = cast("dict[str, object]", observation["readback-result"])
    assert observation["state"] == expected_state
    assert readback_result["outcome"] == expected_outcome
    assert readback_result["stdout"]
    assert readback_result["stderr"]
    assert observation["diagnostics"]
    retained = json.dumps(
        {
            "result": observation["readback-result"],
            "diagnostics": observation["diagnostics"],
        }
    )
    assert dedicated not in retained
    assert auth_line not in retained
    assert config_contents[0] not in retained


def test_proxy_and_readback_configs_have_disjoint_credentials(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    dedicated = "dedicated-readback-token"
    dummy = cli_module._ACCEPTANCE_LOOPBACK_DUMMY_TOKEN
    _, _, _, readback_config, _ = _exercise_readback(
        tmp_path,
        monkeypatch,
    )
    proxy_configs: list[str] = []

    class Proxy:
        registry = "http://127.0.0.1:4873"
        observed = cli_module.threading.Event()
        processed = cli_module.threading.Event()
        proof = None

        def __init__(self, **_kwargs: object) -> None:
            pass

        def __enter__(self) -> Self:
            return self

        def __exit__(self, *_args: object) -> None:
            pass

    class Process:
        returncode = 1

        def communicate(self, timeout: float | None = None) -> tuple[str, str]:
            assert timeout is not None
            return "", "publish failed"

        def poll(self) -> int:
            return self.returncode

        def kill(self) -> None:
            self.returncode = -9

    def popen(*_args: object, **kwargs: object) -> Process:
        config = Path(
            cast("dict[str, str]", kwargs["env"])["NPM_CONFIG_USERCONFIG"]
        )
        proxy_configs.append(config.read_text(encoding="utf-8"))
        return Process()

    monkeypatch.setattr(cli_module, "_LostResponseProxy", Proxy)
    monkeypatch.setattr(cli_module.subprocess, "Popen", popen)
    monkeypatch.setattr(cli_module, "_SYSTEM_POPEN", popen)
    runner = cli_module._AcceptanceNpmRunner(
        tmp_path / "readback.npmrc",
        contender_tarballs={},
        token=dedicated,
    )

    runner.run_scenario(
        "lost-response",
        ("npm", "publish", str(tmp_path / "package.tgz")),
        env={},
        timeout_seconds=TIMEOUT_SECONDS,
        max_output_bytes=MAX_OUTPUT_BYTES,
    )

    assert len(proxy_configs) == 1
    proxy_config = proxy_configs[0]
    assert dummy in proxy_config
    assert dedicated not in proxy_config
    assert proxy_config.count("_authToken=") == 1
    assert dedicated in readback_config
    assert dummy not in readback_config
    assert readback_config.count("_authToken=") == 1


def test_real_runner_proxy_wait_cleanup_and_readback_share_decreasing_budget(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    from three_workflow_delivery_v3.adapters import (  # noqa: PLC0415
        github_packages as module,
    )

    events: list[tuple[str, float, float]] = []

    class Clock:
        now = 100.0

        def monotonic(self) -> float:
            current = self.now
            self.now += 1.0
            return current

    clock = Clock()

    class Proxy:
        registry = "http://127.0.0.1:4873"
        observed = cli_module.threading.Event()
        processed = cli_module.threading.Event()
        proof = None

        def __init__(
            self,
            *,
            timeout_seconds: float,
            deadline: float,
            **_kwargs: object,
        ) -> None:
            events.append(("proxy", timeout_seconds, deadline))

        def __enter__(self) -> Self:
            return self

        def __exit__(self, *_args: object) -> None:
            pass

    class Process:
        returncode: int | None = None
        waits = 0

        def communicate(self, timeout: float) -> tuple[str, str]:
            self.waits += 1
            boundary = "wait" if self.waits == 1 else "cleanup"
            events.append((boundary, timeout, 107.0))
            if self.waits == 1:
                raise subprocess.TimeoutExpired(("npm", "publish"), timeout)
            return "", ""

        def poll(self) -> int | None:
            return self.returncode

        def kill(self) -> None:
            self.returncode = -9

    class Transport:
        observations = iter(
            (
                _absent(),
                {"state": "unknown", "response-identity-digest": RESPONSE_B},
            )
        )

        def observe(
            self,
            _coordinate: str,
            _tag: str,
            *,
            timeout_seconds: float,
            max_response_bytes: int,
            deadline: float,
        ) -> dict[str, object]:
            del max_response_bytes
            state = next(self.observations)
            if state["state"] == "unknown":
                events.append(("readback", timeout_seconds, deadline))
            return cast("dict[str, object]", state)

    monkeypatch.setattr(cli_module, "monotonic", clock.monotonic)
    monkeypatch.setattr(module, "monotonic", clock.monotonic)
    monkeypatch.setattr(cli_module, "_LostResponseProxy", Proxy)
    monkeypatch.setattr(
        cli_module.subprocess,
        "Popen",
        lambda *_args, **_kwargs: Process(),
    )
    scenario = "absent-create-readback"
    tarball = tmp_path / "shared-deadline.tgz"
    tarball.write_bytes(b"shared-deadline")
    runner = cli_module._AcceptanceNpmRunner(
        tmp_path / "readback.npmrc",
        contender_tarballs={},
        token="dedicated-readback-token",
    )

    result = run_fixed_coordinate_acceptance_probe(
        scenario=scenario,
        package_coordinate=ACCEPTANCE_COORDINATES[scenario],
        tag=TAGS[scenario],
        tarball=tarball,
        tarball_sha512=(
            "sha512:" + hashlib.sha512(tarball.read_bytes()).hexdigest()
        ),
        transport=Transport(),
        runner=runner,
        timeout_seconds=TIMEOUT_SECONDS,
        max_response_bytes=MAX_RESPONSE_BYTES,
        max_output_bytes=MAX_OUTPUT_BYTES,
    )

    assert result.result == "runner-failed-after-action-start"
    assert result.mutation_classification == "incomplete"
    assert [name for name, _, _ in events] == [
        "proxy",
        "wait",
        "cleanup",
        "readback",
    ]
    deadlines = [deadline for _, _, deadline in events]
    assert deadlines == [107.0] * 4
    budgets = [budget for _, budget, _ in events]
    assert all(
        budgets[index + 1] < budgets[index] for index in range(len(budgets) - 1)
    )


def test_acceptance_proxy_uses_closure_bound_method_and_path_after_handler_mutation(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    expected_method = "PUT"
    expected_path = "/@hcoona%2fhcoona-release-smoke-npm"
    mutated_method = "DELETE"
    mutated_path = "https://api.github.com.example.invalid/attacker"
    tarball = _acceptance_tarball(
        version="0.0.0-wdv3-acceptance.4",
        repository_url="git+https://github.com/hcoona/three.git",
        target_sha=TARGET,
    )
    handlers: list[Any] = []
    forwarded: list[tuple[str, str]] = []

    class Response:
        status = 201

        def read(self, _size: int) -> bytes:
            return b"{}"

        def getheaders(self) -> list[tuple[str, str]]:
            return [("Content-Type", "application/json")]

    class Connection:
        def __init__(self, _host: str, *, timeout: float) -> None:
            del timeout
            handler = handlers[-1]
            handler.command = mutated_method
            handler.path = mutated_path

        def request(
            self,
            method: str,
            path: str,
            *,
            body: bytes,
            headers: dict[str, str],
        ) -> None:
            del body, headers
            assert handlers[-1].command == mutated_method
            assert handlers[-1].path == mutated_path
            forwarded.append((method, path))

        def getresponse(self) -> Response:
            return Response()

        def close(self) -> None:
            pass

    monkeypatch.setattr(cli_module.http.client, "HTTPSConnection", Connection)
    proxy = cli_module.AcceptanceMutationProxy(
        timeout_seconds=TIMEOUT_SECONDS,
        token="upstream-secret",
        expected_method=expected_method,
        expected_path=expected_path,
        expected_tarballs=(tarball,),
        expected_target_sha=TARGET,
        drop_accepted_response=False,
    )
    handler_type = cast("Any", proxy._server.RequestHandlerClass)
    original_forward = handler_type._forward

    def capture_handler(handler: Any) -> None:
        handlers.append(handler)
        original_forward(handler)

    monkeypatch.setattr(handler_type, "_forward", capture_handler)

    with proxy:
        response = _request_proxy_publish(
            proxy,
            _adversarial_publish_body(tarball),
        )
        assert proxy.processed.is_set()

    assert response is not None
    assert response[0] == 201
    assert forwarded == [(expected_method, expected_path)]


@pytest.mark.parametrize(
    "upstream_header",
    [
        pytest.param(
            ("X-Bad\rX-Injected", "value"),
            id="name-cr",
        ),
        pytest.param(
            ("X-Bad\nX-Injected", "value"),
            id="name-lf",
        ),
        pytest.param(
            ("X-Bad", "before\rX-Injected: leaked"),
            id="value-cr",
        ),
        pytest.param(
            ("X-Bad", "before\nX-Injected: leaked"),
            id="value-lf",
        ),
    ],
)
def test_acceptance_proxy_rejects_crlf_upstream_response_header(
    monkeypatch: pytest.MonkeyPatch,
    upstream_header: tuple[str, str],
) -> None:
    tarball = _acceptance_tarball(
        version="0.0.0-wdv3-acceptance.4",
        repository_url="git+https://github.com/hcoona/three.git",
        target_sha=TARGET,
    )

    class Response:
        status = 201

        def read(self, _size: int) -> bytes:
            return b"{}"

        def getheaders(self) -> list[tuple[str, str]]:
            return [
                ("Content-Type", "application/json"),
                upstream_header,
            ]

    class Connection:
        def __init__(self, _host: str, *, timeout: float) -> None:
            del timeout

        def request(
            self,
            _method: str,
            _path: str,
            *,
            body: bytes,
            headers: dict[str, str],
        ) -> None:
            del body, headers

        def getresponse(self) -> Response:
            return Response()

        def close(self) -> None:
            pass

    monkeypatch.setattr(cli_module.http.client, "HTTPSConnection", Connection)
    with cli_module.AcceptanceMutationProxy(
        timeout_seconds=TIMEOUT_SECONDS,
        token="upstream-secret",
        expected_method="PUT",
        expected_path="/@hcoona%2fhcoona-release-smoke-npm",
        expected_tarballs=(tarball,),
        expected_target_sha=TARGET,
        drop_accepted_response=False,
    ) as proxy:
        response = _request_proxy_publish(
            proxy,
            _adversarial_publish_body(tarball),
        )

    assert response is not None
    status, headers, body = response
    assert status == 502
    assert body == b""
    assert proxy.proof is None
    assert not proxy.processed.is_set()
    assert {"x-bad", "x-injected"}.isdisjoint(
        name.lower() for name, _value in headers
    )


def test_retry_3_suite_resolves_exact_coordinates_and_preserves_history() -> (
    None
):
    from three_workflow_delivery_v3.adapters.github_packages import (  # noqa: PLC0415
        RETRY_3_ACCEPTANCE_PACKAGE_COORDINATE,
    )

    package = "@hcoona/hcoona-release-smoke-npm@"
    expected_profiles = {
        ACCEPTANCE_PACKAGE_COORDINATE: {
            "absent-create-readback": (
                package + "0.0.0-wdv3-acceptance.1",
                "wdv3-acceptance-1",
            ),
            "exact": (
                package + "0.0.0-wdv3-acceptance.1",
                "wdv3-acceptance-1",
            ),
            "identical-race": (
                package + "0.0.0-wdv3-acceptance.2",
                "wdv3-acceptance-2",
            ),
            "differing-race": (
                package + "0.0.0-wdv3-acceptance.3",
                "wdv3-acceptance-3",
            ),
            "lost-response": (
                package + "0.0.0-wdv3-acceptance.4",
                "wdv3-acceptance-4",
            ),
        },
        RETRY_2_ACCEPTANCE_PACKAGE_COORDINATE: {
            "absent-create-readback": (
                package + "0.0.0-wdv3-acceptance.5",
                "wdv3-acceptance-5",
            ),
            "exact": (
                package + "0.0.0-wdv3-acceptance.5",
                "wdv3-acceptance-5",
            ),
            "identical-race": (
                package + "0.0.0-wdv3-acceptance.6",
                "wdv3-acceptance-6",
            ),
            "differing-race": (
                package + "0.0.0-wdv3-acceptance.7",
                "wdv3-acceptance-7",
            ),
            "lost-response": (
                package + "0.0.0-wdv3-acceptance.8",
                "wdv3-acceptance-8",
            ),
        },
        RETRY_3_ACCEPTANCE_PACKAGE_COORDINATE: {
            "absent-create-readback": (
                package + "0.0.0-wdv3-acceptance.9",
                "wdv3-acceptance-9",
            ),
            "exact": (
                package + "0.0.0-wdv3-acceptance.9",
                "wdv3-acceptance-9",
            ),
            "identical-race": (
                package + "0.0.0-wdv3-acceptance.10",
                "wdv3-acceptance-10",
            ),
            "differing-race": (
                package + "0.0.0-wdv3-acceptance.11",
                "wdv3-acceptance-11",
            ),
            "lost-response": (
                package + "0.0.0-wdv3-acceptance.12",
                "wdv3-acceptance-12",
            ),
        },
    }
    for base_coordinate, expected in expected_profiles.items():
        coordinates = fixed_acceptance_coordinates(base_coordinate)
        tags = {
            scenario: tag
            for scenario, _version, tag in fixed_acceptance_scenario_specs(
                base_coordinate
            )
        }
        assert {
            scenario: (coordinate, tags[scenario])
            for scenario, coordinate in coordinates.items()
        } == expected


def test_retry_3_suite_executes_with_exact_base_coordinate_and_tag(
    tmp_path: Path,
) -> None:
    from three_workflow_delivery_v3.adapters.github_packages import (  # noqa: PLC0415
        RETRY_3_ACCEPTANCE_PACKAGE_COORDINATE,
    )

    tarball = tmp_path / "retry-3.tgz"
    tarball.write_bytes(b"retry-3-absent-create-readback")
    content = f"sha512:{hashlib.sha512(tarball.read_bytes()).hexdigest()}"
    transport = RecordingTransport(
        [
            _absent(),
            {
                "state": "exact",
                "version": "0.0.0-wdv3-acceptance.9",
                "tag": "wdv3-acceptance-9",
                "content-sha512": content,
                "response-identity-digest": RESPONSE_B,
            },
        ]
    )

    result = run_fixed_acceptance_suite(
        suite="absent-create-readback",
        tarballs={"absent-create-readback": tarball},
        transport=transport,
        runner=ControlledRunner(),
        timeout_seconds=TIMEOUT_SECONDS,
        max_response_bytes=MAX_RESPONSE_BYTES,
        max_output_bytes=MAX_OUTPUT_BYTES,
        base_package_coordinate=RETRY_3_ACCEPTANCE_PACKAGE_COORDINATE,
    )

    scenario = result.scenarios[0]
    assert scenario.package_coordinate == (
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.9"
    )
    assert scenario.tag == "wdv3-acceptance-9"
    assert transport.calls[0][:2] == (
        scenario.package_coordinate,
        scenario.tag,
    )


def test_retry_3_npm_runner_uses_exact_lost_response_coordinate() -> None:
    from three_workflow_delivery_v3.adapters.github_packages import (  # noqa: PLC0415
        RETRY_3_ACCEPTANCE_PACKAGE_COORDINATE,
    )

    runner = cli_module._AcceptanceNpmRunner(
        Path(".npmrc"),
        contender_tarballs={},
        base_package_coordinate=RETRY_3_ACCEPTANCE_PACKAGE_COORDINATE,
    )

    assert runner._coordinates == {
        "absent-create-readback": (
            "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.9"
        ),
        "exact": "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.9",
        "identical-race": (
            "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.10"
        ),
        "differing-race": (
            "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.11"
        ),
        "lost-response": (
            "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.12"
        ),
    }


@pytest.mark.parametrize(
    ("coordinate", "tag"),
    [
        (
            "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.9",
            "wdv3-acceptance-5",
        ),
        (
            "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.5",
            "wdv3-acceptance-9",
        ),
    ],
)
def test_retry_3_proof_rejects_cross_profile_coordinate_and_tag(
    coordinate: str,
    tag: str,
) -> None:
    with pytest.raises(ValueError, match="coordinate or tag is not fixed"):
        AcceptanceRequestProof.from_validated_exchange(
            raw_request=b"retry-3-request",
            tarball=b"retry-3-tarball",
            package_coordinate=coordinate,
            tag=tag,
            upstream_status=201,
            selected_headers={"Content-Type": "application/json"},
            response_body=b'{"ok":true}',
        )


@pytest.mark.parametrize(
    "failure_kind",
    [
        "oversized-body",
        "unsafe-response-header",
        "response-read-os-error",
    ],
    ids=[
        "oversized-body",
        "unsafe-response-header",
        "response-read-os-error",
    ],
)
def test_proxy_retains_status_and_request_digest_when_201_response_validation_fails(
    monkeypatch: pytest.MonkeyPatch,
    failure_kind: str,
) -> None:
    version = "0.0.0-wdv3-acceptance.4"
    tag = "wdv3-acceptance-4"
    tarball = _acceptance_tarball(
        version=version,
        repository_url="git+https://github.com/hcoona/three.git",
        target_sha=TARGET,
    )
    request_body = _adversarial_publish_body(tarball)
    request_digest = "sha256:" + hashlib.sha256(request_body).hexdigest()
    private_marker = "upstream-response-private-marker"
    upstream_token = "upstream-token-must-not-survive"
    read_error_message = f"{private_marker}; stdout=secret; stderr=secret"
    forwarded: list[bytes] = []

    class Response:
        status = 201

        def read(self, size: int) -> bytes:
            assert (
                size
                == cli_module.AcceptanceMutationProxy._MAX_RESPONSE_BYTES + 1
            )
            if failure_kind == "response-read-os-error":
                raise OSError(read_error_message)
            if failure_kind == "oversized-body":
                return private_marker.encode() + bytes(size)
            return b'{"ok":true}'

        def getheaders(self) -> list[tuple[str, str]]:
            if failure_kind == "unsafe-response-header":
                return [
                    ("Content-Type", "application/json"),
                    ("X-Upstream-Private\rInjected", private_marker),
                ]
            return [("Content-Type", "application/json")]

    class Connection:
        def __init__(self, host: str, *, timeout: float) -> None:
            assert host == "npm.pkg.github.com"
            assert timeout == TIMEOUT_SECONDS

        def request(
            self,
            method: str,
            path: str,
            *,
            body: bytes,
            headers: dict[str, str],
        ) -> None:
            assert method == "PUT"
            assert path == "/@hcoona%2fhcoona-release-smoke-npm"
            assert headers["Authorization"] == f"Bearer {upstream_token}"
            forwarded.append(body)

        def getresponse(self) -> Response:
            return Response()

        def close(self) -> None:
            pass

    monkeypatch.setattr(cli_module.http.client, "HTTPSConnection", Connection)
    with cli_module.AcceptanceMutationProxy(
        timeout_seconds=TIMEOUT_SECONDS,
        token=upstream_token,
        expected_method="PUT",
        expected_path="/@hcoona%2fhcoona-release-smoke-npm",
        expected_version=version,
        expected_tag=tag,
        expected_tarballs=(tarball,),
        expected_target_sha=TARGET,
        drop_accepted_response=False,
    ) as proxy:
        response = _request_proxy_publish(proxy, request_body)

    assert response is not None
    assert response[0] == 502
    assert response[2] == b""
    assert forwarded == [request_body]
    assert proxy.observed.is_set()
    assert not proxy.processed.is_set()
    assert proxy.proof is None
    assert len(proxy.request_facts) == 1
    request_fact = proxy.request_facts[0]
    assert request_fact["request-digest"] == request_digest
    assert request_fact["tarball-sha512"] == (
        "sha512:" + hashlib.sha512(tarball).hexdigest()
    )
    assert "proof" not in request_fact
    assert "upstream-result" not in request_fact

    expected_diagnostic = {
        "upstream-status": 201,
        "exception-category": None,
        "request-correlation-digest": request_digest,
    }
    if not hasattr(proxy, "upstream_diagnostic"):
        pytest.fail(
            "AcceptanceMutationProxy omitted post-response upstream_diagnostic",
            pytrace=False,
        )
    diagnostic = getattr(proxy, "upstream_diagnostic", None)
    assert diagnostic == expected_diagnostic
    assert isinstance(diagnostic, dict)
    assert set(diagnostic) == {
        "upstream-status",
        "exception-category",
        "request-correlation-digest",
    }
    retained = json.dumps(
        {
            "request-facts": proxy.request_facts,
            "upstream-diagnostic": diagnostic,
        },
        sort_keys=True,
    )
    assert private_marker not in retained
    assert upstream_token not in retained
    assert request_body.decode() not in retained
    assert "stdout" not in retained.lower()
    assert "stderr" not in retained.lower()


@pytest.mark.parametrize(
    ("failure_kind", "expected_category"),
    [
        ("timeout-error", "TimeoutError"),
        ("os-error", "OSError"),
        ("http-exception", "HTTPException"),
    ],
    ids=[
        "timeout-error",
        "os-error",
        "http-exception",
    ],
)
def test_proxy_pre_response_transport_failure_retains_redacted_category_and_request_digest_without_status(
    monkeypatch: pytest.MonkeyPatch,
    failure_kind: str,
    expected_category: str,
) -> None:
    version = "0.0.0-wdv3-acceptance.4"
    tag = "wdv3-acceptance-4"
    tarball = _acceptance_tarball(
        version=version,
        repository_url="git+https://github.com/hcoona/three.git",
        target_sha=TARGET,
    )
    request_body = _adversarial_publish_body(tarball)
    request_digest = "sha256:" + hashlib.sha256(request_body).hexdigest()
    secret = "transport-secret-must-not-survive"
    upstream_token = "transport-token-must-not-survive"
    raw_message = (
        f"{secret}; request-body={request_body.decode()}; "
        "Authorization=secret; X-Private=secret; stdout=secret; stderr=secret"
    )
    errors = {
        "timeout-error": TimeoutError(raw_message),
        "os-error": OSError(raw_message),
        "http-exception": http.client.HTTPException(raw_message),
    }
    forwarded_headers: list[dict[str, str]] = []

    class Connection:
        def __init__(self, host: str, *, timeout: float) -> None:
            assert host == "npm.pkg.github.com"
            assert timeout == TIMEOUT_SECONDS

        def request(
            self,
            method: str,
            path: str,
            *,
            body: bytes,
            headers: dict[str, str],
        ) -> None:
            assert method == "PUT"
            assert path == "/@hcoona%2fhcoona-release-smoke-npm"
            assert body == request_body
            forwarded_headers.append(dict(headers))

        def getresponse(self) -> object:
            raise errors[failure_kind]

        def close(self) -> None:
            pass

    monkeypatch.setattr(cli_module.http.client, "HTTPSConnection", Connection)
    with cli_module.AcceptanceMutationProxy(
        timeout_seconds=TIMEOUT_SECONDS,
        token=upstream_token,
        expected_method="PUT",
        expected_path="/@hcoona%2fhcoona-release-smoke-npm",
        expected_version=version,
        expected_tag=tag,
        expected_tarballs=(tarball,),
        expected_target_sha=TARGET,
        drop_accepted_response=False,
    ) as proxy:
        response = _request_proxy_publish(proxy, request_body)

    assert response is not None
    assert response[0] == 502
    assert response[2] == b""
    assert len(forwarded_headers) == 1
    assert forwarded_headers[0]["Authorization"] == f"Bearer {upstream_token}"
    assert proxy.observed.is_set()
    assert not proxy.processed.is_set()
    assert proxy.proof is None
    assert len(proxy.request_facts) == 1
    request_fact = proxy.request_facts[0]
    assert request_fact["request-digest"] == request_digest
    assert request_fact["tarball-sha512"] == (
        "sha512:" + hashlib.sha512(tarball).hexdigest()
    )
    assert "proof" not in request_fact
    assert "upstream-result" not in request_fact

    expected_diagnostic = {
        "upstream-status": None,
        "exception-category": expected_category,
        "request-correlation-digest": request_digest,
    }
    if not hasattr(proxy, "upstream_diagnostic"):
        pytest.fail(
            "AcceptanceMutationProxy omitted transport upstream_diagnostic",
            pytrace=False,
        )
    diagnostic = getattr(proxy, "upstream_diagnostic", None)
    assert diagnostic == expected_diagnostic
    assert isinstance(diagnostic, dict)
    assert set(diagnostic) == {
        "upstream-status",
        "exception-category",
        "request-correlation-digest",
    }
    retained = json.dumps(
        {
            "request-facts": proxy.request_facts,
            "upstream-diagnostic": diagnostic,
        },
        sort_keys=True,
    )
    for forbidden in (
        secret,
        raw_message,
        request_body.decode(),
        upstream_token,
        "Authorization",
        "Content-Type",
        "X-Private",
        "stdout",
        "stderr",
    ):
        assert forbidden.lower() not in retained.lower()


_NO_UPSTREAM_DIAGNOSTIC = object()


def _exercise_runner_upstream_diagnostic_path(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    *,
    path: str,
    upstream_diagnostic: object,
) -> tuple[dict[str, object] | None, Exception | None]:
    observed = cli_module.threading.Event()
    observed.set()
    os_error_message = "local process transport detail"

    class Proxy:
        registry = "http://127.0.0.1:4873"

        def __init__(self, **_kwargs: object) -> None:
            self.observed = observed
            self.processed = cli_module.threading.Event()
            self.proof = None
            if upstream_diagnostic is not _NO_UPSTREAM_DIAGNOSTIC:
                self.upstream_diagnostic = dict(
                    cast("dict[str, object]", upstream_diagnostic)
                )

        def __enter__(self) -> Self:
            return self

        def __exit__(self, *_args: object) -> None:
            pass

    class Process:
        def __init__(self) -> None:
            self.returncode: int | None = 1 if path == "returned-failure" else 0

        def communicate(
            self,
            timeout: float | None = None,
        ) -> tuple[str, str]:
            if timeout is None:
                return "", ""
            assert 0 < timeout <= TIMEOUT_SECONDS
            if path == "raised-timeout":
                raise subprocess.TimeoutExpired(("npm", "publish"), timeout)
            if path == "raised-os-error":
                raise OSError(os_error_message)
            if path == "raised-classification-error":
                return "oversized-classification-output", ""
            return "", "npm returned failure"

        def poll(self) -> int | None:
            return self.returncode

        def kill(self) -> None:
            self.returncode = -9

    monkeypatch.setattr(cli_module, "_LostResponseProxy", Proxy)
    monkeypatch.setattr(
        cli_module.subprocess,
        "Popen",
        lambda *_args, **_kwargs: Process(),
    )
    tarball = tmp_path / f"{path}.tgz"
    tarball.write_bytes(path.encode())
    runner = cli_module._AcceptanceNpmRunner(
        tmp_path / f"{path}.npmrc",
        contender_tarballs={},
        token="runner-upstream-token",
    )
    try:
        return (
            runner.run_scenario(
                "absent-create-readback",
                (
                    "npm",
                    "publish",
                    str(tarball),
                    "--tag",
                    TAGS["absent-create-readback"],
                ),
                env={"NPM_CONFIG_IGNORE_SCRIPTS": "true"},
                timeout_seconds=TIMEOUT_SECONDS,
                max_output_bytes=(
                    1
                    if path == "raised-classification-error"
                    else MAX_OUTPUT_BYTES
                ),
            ),
            None,
        )
    except (TimeoutError, OSError, ValueError) as error:
        return None, error


@pytest.mark.parametrize(
    ("path", "upstream_diagnostic", "expected_exception"),
    [
        (
            "returned-failure",
            {
                "upstream-status": 500,
                "exception-category": None,
                "request-correlation-digest": "sha256:" + ("5" * 64),
            },
            None,
        ),
        (
            "raised-timeout",
            {
                "upstream-status": None,
                "exception-category": "TimeoutError",
                "request-correlation-digest": "sha256:" + ("6" * 64),
            },
            TimeoutError,
        ),
        (
            "raised-os-error",
            {
                "upstream-status": None,
                "exception-category": "OSError",
                "request-correlation-digest": "sha256:" + ("7" * 64),
            },
            OSError,
        ),
        (
            "raised-classification-error",
            {
                "upstream-status": None,
                "exception-category": "HTTPException",
                "request-correlation-digest": "sha256:" + ("8" * 64),
            },
            ValueError,
        ),
    ],
    ids=[
        "returned-failure",
        "raised-timeout",
        "raised-os-error",
        "raised-classification-error",
    ],
)
def test_runner_propagates_closed_upstream_diagnostic_for_returned_and_raised_failures(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    path: str,
    upstream_diagnostic: dict[str, object],
    expected_exception: type[Exception] | None,
) -> None:
    result, error = _exercise_runner_upstream_diagnostic_path(
        tmp_path,
        monkeypatch,
        path=path,
        upstream_diagnostic=upstream_diagnostic,
    )

    if expected_exception is None:
        output = b"\nnpm returned failure"
        classified_result = {
            "outcome": "failed",
            "response-identity-digest": (
                "sha256:" + hashlib.sha256(output).hexdigest()
            ),
        }
        aggregate_digest = (
            "sha256:"
            + hashlib.sha256(
                canonicalize(cast("JsonValue", [classified_result]))
            ).hexdigest()
        )
        assert error is None
        assert result is not None
        if "upstream-diagnostic" not in result:
            pytest.fail(
                "_AcceptanceNpmRunner omitted returned upstream-diagnostic",
                pytrace=False,
            )
        assert result == {
            "outcome": "failed",
            "action-executed": True,
            "mutation-started": True,
            "contender-outcomes": ["failed"],
            "response-identity-digest": aggregate_digest,
            "upstream-diagnostic": upstream_diagnostic,
        }
        retained_diagnostic = result["upstream-diagnostic"]
        assert isinstance(retained_diagnostic, dict)
        assert set(retained_diagnostic) == {
            "upstream-status",
            "exception-category",
            "request-correlation-digest",
        }
        return

    assert result is None
    assert error is not None
    assert type(error) is expected_exception
    assert error.action_executed is True  # type: ignore[attr-defined]
    assert error.mutation_started is True  # type: ignore[attr-defined]
    if not hasattr(error, "upstream_diagnostic"):
        pytest.fail(
            "_AcceptanceNpmRunner omitted raised upstream_diagnostic",
            pytrace=False,
        )
    retained_diagnostic = getattr(error, "upstream_diagnostic", None)
    assert retained_diagnostic == upstream_diagnostic
    assert isinstance(retained_diagnostic, dict)
    assert set(retained_diagnostic) == {
        "upstream-status",
        "exception-category",
        "request-correlation-digest",
    }


@pytest.mark.parametrize(
    ("path", "expected_exception"),
    [
        ("returned-failure", None),
        ("raised-timeout", TimeoutError),
        ("raised-os-error", OSError),
        ("raised-classification-error", ValueError),
    ],
    ids=[
        "returned-failure",
        "raised-timeout",
        "raised-os-error",
        "raised-classification-error",
    ],
)
def test_runner_omits_upstream_diagnostic_when_proxy_supplies_no_admitted_fact(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    path: str,
    expected_exception: type[Exception] | None,
) -> None:
    result, error = _exercise_runner_upstream_diagnostic_path(
        tmp_path,
        monkeypatch,
        path=path,
        upstream_diagnostic=_NO_UPSTREAM_DIAGNOSTIC,
    )

    if expected_exception is None:
        output = b"\nnpm returned failure"
        classified_result = {
            "outcome": "failed",
            "response-identity-digest": (
                "sha256:" + hashlib.sha256(output).hexdigest()
            ),
        }
        aggregate_digest = (
            "sha256:"
            + hashlib.sha256(
                canonicalize(cast("JsonValue", [classified_result]))
            ).hexdigest()
        )
        assert error is None
        assert result is not None
        assert result == {
            "outcome": "failed",
            "action-executed": True,
            "mutation-started": True,
            "contender-outcomes": ["failed"],
            "response-identity-digest": aggregate_digest,
        }
        assert "upstream-diagnostic" not in result
        return

    assert result is None
    assert error is not None
    assert type(error) is expected_exception
    assert error.action_executed is True  # type: ignore[attr-defined]
    assert error.mutation_started is True  # type: ignore[attr-defined]
    assert not hasattr(error, "upstream_diagnostic")


def test_runner_waits_for_terminal_proxy_diagnostic_before_raising_timeout(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    request_digest = "sha256:" + ("9" * 64)
    upstream_diagnostic = {
        "upstream-status": None,
        "exception-category": "TimeoutError",
        "request-correlation-digest": request_digest,
    }
    process_failed = cli_module.threading.Event()
    first_snapshot = cli_module.threading.Event()

    class Proxy:
        registry = "http://127.0.0.1:4873"

        def __init__(self, **_kwargs: object) -> None:
            self.observed = cli_module.threading.Event()
            self.observed.set()
            self.processed = cli_module.threading.Event()
            self.proof = None
            self._upstream_terminal = cli_module.threading.Event()
            self._diagnostic: dict[str, object] | None = None
            self._publisher = cli_module.threading.Thread(
                target=self._publish_terminal_diagnostic,
            )

        @property
        def upstream_diagnostic(self) -> dict[str, object] | None:
            if self._diagnostic is None:
                first_snapshot.set()
                return None
            return dict(self._diagnostic)

        def _publish_terminal_diagnostic(self) -> None:
            assert process_failed.wait(TIMEOUT_SECONDS)
            assert first_snapshot.wait(TIMEOUT_SECONDS)
            self._diagnostic = dict(upstream_diagnostic)
            self._upstream_terminal.set()

        def __enter__(self) -> Self:
            self._publisher.start()
            return self

        def __exit__(self, *_args: object) -> None:
            self._publisher.join(TIMEOUT_SECONDS)
            assert not self._publisher.is_alive()

    class Process:
        def __init__(self) -> None:
            self.returncode: int | None = None

        def communicate(
            self,
            timeout: float | None = None,
        ) -> tuple[str, str]:
            if timeout is None:
                return "", ""
            process_failed.set()
            raise subprocess.TimeoutExpired(("npm", "publish"), timeout)

        def poll(self) -> int | None:
            return self.returncode

        def kill(self) -> None:
            self.returncode = -9

    monkeypatch.setattr(cli_module, "_LostResponseProxy", Proxy)
    monkeypatch.setattr(
        cli_module.subprocess,
        "Popen",
        lambda *_args, **_kwargs: Process(),
    )
    tarball = tmp_path / "delayed-terminal-diagnostic.tgz"
    tarball.write_bytes(b"delayed-terminal-diagnostic")
    runner = cli_module._AcceptanceNpmRunner(
        tmp_path / ".npmrc",
        contender_tarballs={},
        token="delayed-diagnostic-token",
    )

    with pytest.raises(TimeoutError) as captured:
        runner.run_scenario(
            "absent-create-readback",
            (
                "npm",
                "publish",
                str(tarball),
                "--tag",
                TAGS["absent-create-readback"],
            ),
            env={"NPM_CONFIG_IGNORE_SCRIPTS": "true"},
            timeout_seconds=TIMEOUT_SECONDS,
            max_output_bytes=MAX_OUTPUT_BYTES,
        )

    assert captured.value.action_executed is True  # type: ignore[attr-defined]
    assert captured.value.mutation_started is True  # type: ignore[attr-defined]
    assert captured.value.upstream_diagnostic == (  # type: ignore[attr-defined]
        upstream_diagnostic
    )


@pytest.mark.parametrize(
    ("observation_kind", "upstream_status"),
    [
        ("status-100", 100),
        ("status-409", 409),
        ("status-599", 599),
        ("pre-response-transport", None),
    ],
    ids=[
        "status-100",
        "status-409",
        "status-599",
        "pre-response-transport",
    ],
)
def test_acceptance_proxy_one_request_retains_at_most_one_request_bound_diagnostic(
    monkeypatch: pytest.MonkeyPatch,
    observation_kind: str,
    upstream_status: int | None,
) -> None:
    version = "0.0.0-wdv3-acceptance.4"
    tag = "wdv3-acceptance-4"
    tarball = _acceptance_tarball(
        version=version,
        repository_url="git+https://github.com/hcoona/three.git",
        target_sha=TARGET,
    )
    request_body = _adversarial_publish_body(tarball)
    request_digest = "sha256:" + hashlib.sha256(request_body).hexdigest()
    transport_error_message = "request-scoped transport failure"

    class Response:
        def __init__(self) -> None:
            assert upstream_status is not None
            self.status = upstream_status

        def read(self, _size: int) -> bytes:
            return b'{"error":"conflict"}'

        def getheaders(self) -> list[tuple[str, str]]:
            if self.status == 100:
                return [("X-Unsafe\rInjected", "must-not-be-forwarded")]
            return [("Content-Type", "application/json")]

    class Connection:
        def __init__(self, _host: str, *, timeout: float) -> None:
            assert timeout == TIMEOUT_SECONDS

        def request(
            self,
            _method: str,
            _path: str,
            *,
            body: bytes,
            headers: dict[str, str],
        ) -> None:
            assert body == request_body
            assert headers["Authorization"] == "Bearer cardinality-token"

        def getresponse(self) -> Response:
            if observation_kind == "pre-response-transport":
                raise OSError(transport_error_message)
            return Response()

        def close(self) -> None:
            pass

    monkeypatch.setattr(cli_module.http.client, "HTTPSConnection", Connection)
    with cli_module.AcceptanceMutationProxy(
        timeout_seconds=TIMEOUT_SECONDS,
        token="cardinality-token",
        expected_method="PUT",
        expected_path="/@hcoona%2fhcoona-release-smoke-npm",
        expected_version=version,
        expected_tag=tag,
        expected_tarballs=(tarball,),
        expected_target_sha=TARGET,
        expected_requests=1,
        drop_accepted_response=False,
    ) as proxy:
        response = _request_proxy_publish(proxy, request_body)

    expected_diagnostic = {
        "upstream-status": upstream_status,
        "exception-category": (
            "OSError" if observation_kind == "pre-response-transport" else None
        ),
        "request-correlation-digest": request_digest,
    }
    if observation_kind == "pre-response-transport" or upstream_status == 100:
        assert response is not None
        assert response[0] == 502
    else:
        assert response is not None
        assert response[0] == upstream_status
    assert len(proxy.request_facts) == 1
    assert proxy.request_facts[0]["request-digest"] == request_digest
    if not hasattr(proxy, "upstream_diagnostic"):
        pytest.fail(
            "one-request proxy omitted its request-bound upstream_diagnostic",
            pytrace=False,
        )
    retained_diagnostic = getattr(proxy, "upstream_diagnostic", None)
    assert retained_diagnostic == expected_diagnostic
    assert isinstance(retained_diagnostic, dict)
    assert set(retained_diagnostic) == {
        "upstream-status",
        "exception-category",
        "request-correlation-digest",
    }
    assert proxy.proof is None
    assert not proxy.processed.is_set()


def test_acceptance_proxy_two_request_race_never_exposes_a_singleton_upstream_diagnostic(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    version = "0.0.0-wdv3-acceptance.4"
    tag = "wdv3-acceptance-4"
    tarballs = (
        _acceptance_tarball(
            version=version,
            repository_url="git+https://github.com/hcoona/three.git",
            target_sha=TARGET,
        ),
        _acceptance_tarball(
            version=version,
            repository_url="git+https://github.com/hcoona/three.git",
            target_sha="d" * 40,
        ),
    )
    request_bodies = tuple(
        _adversarial_publish_body(tarball) for tarball in tarballs
    )
    request_digests = tuple(
        "sha256:" + hashlib.sha256(body).hexdigest() for body in request_bodies
    )
    status_by_body = {
        request_bodies[0]: 201,
        request_bodies[1]: 409,
    }

    class Response:
        def __init__(self, status: int) -> None:
            self.status = status

        def read(self, _size: int) -> bytes:
            return f'{{"status":{self.status}}}'.encode()

        def getheaders(self) -> list[tuple[str, str]]:
            return [("Content-Type", "application/json")]

    class Connection:
        def __init__(self, _host: str, *, timeout: float) -> None:
            assert timeout == TIMEOUT_SECONDS
            self.status: int | None = None

        def request(
            self,
            _method: str,
            _path: str,
            *,
            body: bytes,
            headers: dict[str, str],
        ) -> None:
            assert headers["Authorization"] == "Bearer race-token"
            self.status = status_by_body[body]

        def getresponse(self) -> Response:
            assert self.status is not None
            return Response(self.status)

        def close(self) -> None:
            pass

    monkeypatch.setattr(cli_module.http.client, "HTTPSConnection", Connection)
    responses: list[tuple[int, list[tuple[str, str]], bytes] | None] = [
        None,
        None,
    ]
    errors: list[Exception | None] = [None, None]
    start = cli_module.threading.Barrier(3)

    with cli_module.AcceptanceMutationProxy(
        timeout_seconds=TIMEOUT_SECONDS,
        token="race-token",
        expected_method="PUT",
        expected_path="/@hcoona%2fhcoona-release-smoke-npm",
        expected_version=version,
        expected_tag=tag,
        expected_tarballs=tarballs,
        expected_requests=2,
        drop_accepted_response=False,
    ) as proxy:

        def publish(index: int) -> None:
            try:
                start.wait(timeout=TIMEOUT_SECONDS)
                responses[index] = _request_proxy_publish(
                    proxy,
                    request_bodies[index],
                )
            except (
                AssertionError,
                KeyError,
                OSError,
                RuntimeError,
                http.client.HTTPException,
            ) as error:
                errors[index] = error

        threads = [
            cli_module.threading.Thread(target=publish, args=(index,))
            for index in range(2)
        ]
        for thread in threads:
            thread.start()
        start.wait(timeout=TIMEOUT_SECONDS)
        for thread in threads:
            thread.join(TIMEOUT_SECONDS)
        assert all(not thread.is_alive() for thread in threads)

    assert errors == [None, None]
    assert all(response is not None for response in responses)
    assert {
        cast("tuple[int, list[tuple[str, str]], bytes]", response)[0]
        for response in responses
    } == {201, 409}
    assert len(proxy.request_facts) == 2
    facts_by_digest = {
        cast("str", fact["request-digest"]): fact
        for fact in proxy.request_facts
    }
    assert set(facts_by_digest) == set(request_digests)
    assert request_digests[0] != request_digests[1]
    assert {
        digest: {
            "tarball-sha512": fact["tarball-sha512"],
            "upstream-result": fact["upstream-result"],
        }
        for digest, fact in facts_by_digest.items()
    } == {
        request_digests[0]: {
            "tarball-sha512": (
                "sha512:" + hashlib.sha512(tarballs[0]).hexdigest()
            ),
            "upstream-result": "created",
        },
        request_digests[1]: {
            "tarball-sha512": (
                "sha512:" + hashlib.sha512(tarballs[1]).hexdigest()
            ),
            "upstream-result": "create-conflict",
        },
    }
    assert getattr(proxy, "upstream_diagnostic", None) is None
    assert proxy._upstream_terminal.is_set()
    assert proxy.proof is not None
    assert proxy.proof.request_digest == request_digests[0]
    assert proxy.proof.upstream_status == 201
    assert proxy.processed.is_set()


def test_acceptance_proxy_expected_one_rejects_simultaneous_identical_qualified_request_before_upstream(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    version = "0.0.0-wdv3-acceptance.4"
    tag = "wdv3-acceptance-4"
    tarball = _acceptance_tarball(
        version=version,
        repository_url="git+https://github.com/hcoona/three.git",
        target_sha=TARGET,
    )
    request_body = _adversarial_publish_body(tarball)
    request_digest = "sha256:" + hashlib.sha256(request_body).hexdigest()
    qualified = cli_module.threading.Barrier(2)
    append_started = cli_module.threading.Barrier(2)
    original_validate = cli_module._validate_acceptance_publish_body
    upstream_requests: list[bytes] = []
    upstream_requests_lock = cli_module.threading.Lock()

    def synchronize_qualified_requests(
        *args: Any,
        **kwargs: Any,
    ) -> bytes:
        validated_tarball = original_validate(*args, **kwargs)
        qualified.wait(timeout=TIMEOUT_SECONDS)
        return validated_tarball

    class AppendBarrierFacts(list[dict[str, object]]):
        def append(self, request_fact: dict[str, object]) -> None:
            with contextlib.suppress(cli_module.threading.BrokenBarrierError):
                append_started.wait(timeout=TIMEOUT_SECONDS / 2)
            super().append(request_fact)

    class Response:
        status = 201

        def read(self, _size: int) -> bytes:
            return b'{"ok":true}'

        def getheaders(self) -> list[tuple[str, str]]:
            return [("Content-Type", "application/json")]

    class Connection:
        def __init__(self, host: str, *, timeout: float) -> None:
            assert host == "npm.pkg.github.com"
            assert timeout == TIMEOUT_SECONDS

        def request(
            self,
            method: str,
            path: str,
            *,
            body: bytes,
            headers: dict[str, str],
        ) -> None:
            assert method == "PUT"
            assert path == "/@hcoona%2fhcoona-release-smoke-npm"
            assert body == request_body
            assert headers["Authorization"] == "Bearer cardinality-token"
            with upstream_requests_lock:
                upstream_requests.append(body)

        def getresponse(self) -> Response:
            return Response()

        def close(self) -> None:
            pass

    monkeypatch.setattr(
        cli_module,
        "_validate_acceptance_publish_body",
        synchronize_qualified_requests,
    )
    monkeypatch.setattr(cli_module.http.client, "HTTPSConnection", Connection)
    responses: list[tuple[int, list[tuple[str, str]], bytes] | None] = [
        None,
        None,
    ]
    errors: list[Exception | None] = [None, None]
    start = cli_module.threading.Barrier(3)

    with cli_module.AcceptanceMutationProxy(
        timeout_seconds=TIMEOUT_SECONDS,
        token="cardinality-token",
        expected_method="PUT",
        expected_path="/@hcoona%2fhcoona-release-smoke-npm",
        expected_version=version,
        expected_tag=tag,
        expected_tarballs=(tarball,),
        expected_target_sha=TARGET,
        expected_requests=1,
        drop_accepted_response=False,
    ) as proxy:
        proxy.request_facts = AppendBarrierFacts()

        def publish(index: int) -> None:
            try:
                start.wait(timeout=TIMEOUT_SECONDS)
                responses[index] = _request_proxy_publish(
                    proxy,
                    request_body,
                )
            except (
                AssertionError,
                OSError,
                RuntimeError,
                http.client.HTTPException,
            ) as error:
                errors[index] = error

        threads = [
            cli_module.threading.Thread(target=publish, args=(index,))
            for index in range(2)
        ]
        for thread in threads:
            thread.start()
        start.wait(timeout=TIMEOUT_SECONDS)
        for thread in threads:
            thread.join(TIMEOUT_SECONDS)
        assert all(not thread.is_alive() for thread in threads)

    assert errors == [None, None]
    assert all(response is not None for response in responses)
    assert len(upstream_requests) == 1
    assert upstream_requests[0] == request_body
    assert sorted(
        cast("tuple[int, list[tuple[str, str]], bytes]", response)[0]
        for response in responses
    ) == [201, 409]
    assert len(proxy.request_facts) == 1
    assert proxy.request_facts[0]["request-digest"] == request_digest
    retained_diagnostic = getattr(proxy, "upstream_diagnostic", None)
    assert retained_diagnostic is None or retained_diagnostic == {
        "upstream-status": 201,
        "exception-category": None,
        "request-correlation-digest": request_digest,
    }


_RETRY_4_ACCEPTANCE_PACKAGE_NAME = "@hcoona/hcoona-release-smoke-npm"
_RETRY_4_ACCEPTANCE_BASE_COORDINATE = (
    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.13"
)
_RETRY_4_UNREGISTERED_BASE_COORDINATE = (
    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.17"
)
_RETRY_4_ACCEPTANCE_BINDINGS = (
    (
        "absent-create-readback",
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.13",
        "wdv3-acceptance-13",
    ),
    (
        "exact",
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.13",
        "wdv3-acceptance-13",
    ),
    (
        "identical-race",
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.14",
        "wdv3-acceptance-14",
    ),
    (
        "differing-race",
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.15",
        "wdv3-acceptance-15",
    ),
    (
        "lost-response",
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.16",
        "wdv3-acceptance-16",
    ),
)


def _require_retry_4_adapter_profile() -> Any:
    from three_workflow_delivery_v3.adapters import (  # noqa: PLC0415
        github_packages as module,
    )

    registered_base_coordinates = tuple(
        base_coordinate
        for base_coordinate, _scenario_specs in module._ACCEPTANCE_SUITE_PROFILES
    )
    assert _RETRY_4_ACCEPTANCE_BASE_COORDINATE in registered_base_coordinates, (
        "E-ADAPTER-PROFILE-ABSENT: the reviewed retry-4 base coordinate is "
        f"not registered; observed {registered_base_coordinates!r}"
    )
    return module


def test_retry_4_adapter_profiles_have_stable_historical_order_and_unique_base_coordinates() -> (
    None
):
    module = _require_retry_4_adapter_profile()

    base_coordinates = tuple(
        base_coordinate
        for base_coordinate, _scenario_specs in module._ACCEPTANCE_SUITE_PROFILES
    )

    assert base_coordinates == (
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.1",
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.5",
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.9",
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.13",
    )
    assert len(base_coordinates) == 4
    assert len(set(base_coordinates)) == 4


def test_retry_4_adapter_profiles_preserve_scenario_order_and_qualified_identity_uniqueness() -> (
    None
):
    module = _require_retry_4_adapter_profile()
    profiles = module._ACCEPTANCE_SUITE_PROFILES
    expected_scenario_order = (
        "absent-create-readback",
        "exact",
        "identical-race",
        "differing-race",
        "lost-response",
    )

    scenario_orders = tuple(
        tuple(scenario for scenario, _version, _tag in scenario_specs)
        for _base_coordinate, scenario_specs in profiles
    )
    qualified_identities = tuple(
        (
            base_coordinate,
            scenario,
            f"{_RETRY_4_ACCEPTANCE_PACKAGE_NAME}@{version}",
            tag,
        )
        for base_coordinate, scenario_specs in profiles
        for scenario, version, tag in scenario_specs
    )
    reuse_groups = []
    for _base_coordinate, scenario_specs in profiles:
        scenarios_by_pair: dict[tuple[str, str], list[str]] = {}
        for scenario, version, tag in scenario_specs:
            pair = (
                f"{_RETRY_4_ACCEPTANCE_PACKAGE_NAME}@{version}",
                tag,
            )
            scenarios_by_pair.setdefault(pair, []).append(scenario)
        reuse_groups.append(
            tuple(tuple(scenarios) for scenarios in scenarios_by_pair.values())
        )

    assert scenario_orders == (expected_scenario_order,) * 4
    assert len(qualified_identities) == 20
    assert len(set(qualified_identities)) == 20
    assert (
        tuple(reuse_groups)
        == (
            (
                ("absent-create-readback", "exact"),
                ("identical-race",),
                ("differing-race",),
                ("lost-response",),
            ),
        )
        * 4
    )


def test_retry_4_adapter_coordinate_tag_pairs_are_exact_and_globally_unique() -> (
    None
):
    module = _require_retry_4_adapter_profile()
    expected_pair_blocks = (
        frozenset(
            {
                (
                    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.1",
                    "wdv3-acceptance-1",
                ),
                (
                    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.2",
                    "wdv3-acceptance-2",
                ),
                (
                    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.3",
                    "wdv3-acceptance-3",
                ),
                (
                    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.4",
                    "wdv3-acceptance-4",
                ),
            }
        ),
        frozenset(
            {
                (
                    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.5",
                    "wdv3-acceptance-5",
                ),
                (
                    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.6",
                    "wdv3-acceptance-6",
                ),
                (
                    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.7",
                    "wdv3-acceptance-7",
                ),
                (
                    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.8",
                    "wdv3-acceptance-8",
                ),
            }
        ),
        frozenset(
            {
                (
                    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.9",
                    "wdv3-acceptance-9",
                ),
                (
                    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.10",
                    "wdv3-acceptance-10",
                ),
                (
                    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.11",
                    "wdv3-acceptance-11",
                ),
                (
                    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.12",
                    "wdv3-acceptance-12",
                ),
            }
        ),
        frozenset(
            {
                (
                    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.13",
                    "wdv3-acceptance-13",
                ),
                (
                    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.14",
                    "wdv3-acceptance-14",
                ),
                (
                    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.15",
                    "wdv3-acceptance-15",
                ),
                (
                    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.16",
                    "wdv3-acceptance-16",
                ),
            }
        ),
    )
    actual_pair_blocks = tuple(
        frozenset(
            (
                f"{_RETRY_4_ACCEPTANCE_PACKAGE_NAME}@{version}",
                tag,
            )
            for _scenario, version, tag in scenario_specs
        )
        for _base_coordinate, scenario_specs in module._ACCEPTANCE_SUITE_PROFILES
    )
    all_pairs = frozenset(
        pair for block in actual_pair_blocks for pair in block
    )
    retry_4_specs = next(
        scenario_specs
        for base_coordinate, scenario_specs in module._ACCEPTANCE_SUITE_PROFILES
        if base_coordinate == _RETRY_4_ACCEPTANCE_BASE_COORDINATE
    )
    retry_4_bindings = tuple(
        (
            scenario,
            f"{_RETRY_4_ACCEPTANCE_PACKAGE_NAME}@{version}",
            tag,
        )
        for scenario, version, tag in retry_4_specs
    )

    assert actual_pair_blocks == expected_pair_blocks
    assert tuple(len(block) for block in actual_pair_blocks) == (4, 4, 4, 4)
    assert len(all_pairs) == 16
    assert sum(len(block) for block in actual_pair_blocks) == len(all_pairs)
    assert all_pairs == module._ACCEPTANCE_COORDINATE_TAG_PAIRS
    assert retry_4_bindings == _RETRY_4_ACCEPTANCE_BINDINGS


def test_retry_4_fixed_acceptance_resolvers_return_exact_scenarios_and_coordinates() -> (
    None
):
    _require_retry_4_adapter_profile()
    expected_specs = tuple(
        (
            scenario,
            coordinate.rsplit("@", 1)[1],
            tag,
        )
        for scenario, coordinate, tag in _RETRY_4_ACCEPTANCE_BINDINGS
    )
    expected_coordinates = tuple(
        (scenario, coordinate)
        for scenario, coordinate, _tag in _RETRY_4_ACCEPTANCE_BINDINGS
    )

    assert (
        fixed_acceptance_scenario_specs(_RETRY_4_ACCEPTANCE_BASE_COORDINATE)
        == expected_specs
    )
    assert (
        tuple(
            fixed_acceptance_coordinates(
                _RETRY_4_ACCEPTANCE_BASE_COORDINATE
            ).items()
        )
        == expected_coordinates
    )
    with pytest.raises(ValueError, match="not a reviewed fixed suite"):
        fixed_acceptance_scenario_specs(_RETRY_4_UNREGISTERED_BASE_COORDINATE)
    with pytest.raises(ValueError, match="not a reviewed fixed suite"):
        fixed_acceptance_coordinates(_RETRY_4_UNREGISTERED_BASE_COORDINATE)


def test_retry_4_npm_runner_invokes_all_five_exact_coordinates() -> None:
    _require_retry_4_adapter_profile()
    runner = cli_module._AcceptanceNpmRunner(
        Path(".retry-4.npmrc"),
        contender_tarballs={},
        base_package_coordinate=_RETRY_4_ACCEPTANCE_BASE_COORDINATE,
    )

    assert tuple(runner._coordinates.items()) == tuple(
        (scenario, coordinate)
        for scenario, coordinate, _tag in _RETRY_4_ACCEPTANCE_BINDINGS
    )
    assert len(runner._coordinates) == 5
    assert (
        runner._coordinates["absent-create-readback"]
        == runner._coordinates["exact"]
        == _RETRY_4_ACCEPTANCE_BASE_COORDINATE
    )


def test_retry_4_fixed_acceptance_suite_routes_exact_bindings_through_controlled_fakes(
    tmp_path: Path,
) -> None:
    _require_retry_4_adapter_profile()
    coordinates = {
        scenario: coordinate
        for scenario, coordinate, _tag in _RETRY_4_ACCEPTANCE_BINDINGS
    }
    tags = {
        scenario: tag
        for scenario, _coordinate, tag in _RETRY_4_ACCEPTANCE_BINDINGS
    }
    tarballs = {}
    for scenario, _coordinate, _tag in _RETRY_4_ACCEPTANCE_BINDINGS:
        tarball = tmp_path / f"retry-4-{scenario}.tgz"
        tarball.write_bytes(f"retry-4-{scenario}".encode())
        tarballs[scenario] = tarball

    def exact_observation(scenario: str) -> dict[str, Any]:
        return {
            "state": "exact",
            "version": coordinates[scenario].rsplit("@", 1)[1],
            "tag": tags[scenario],
            "content-sha512": (
                "sha512:"
                + hashlib.sha512(tarballs[scenario].read_bytes()).hexdigest()
            ),
            "response-identity-digest": RESPONSE_B,
        }

    transport = RecordingTransport(
        [
            _absent(),
            exact_observation("absent-create-readback"),
            exact_observation("exact"),
            _absent(),
            exact_observation("identical-race"),
            _absent(),
            exact_observation("differing-race"),
            _absent(),
            exact_observation("lost-response"),
        ]
    )
    runner = ControlledRunner()

    absent_result = run_fixed_acceptance_suite(
        suite="absent-create-readback",
        tarballs={"absent-create-readback": tarballs["absent-create-readback"]},
        transport=transport,
        runner=runner,
        timeout_seconds=TIMEOUT_SECONDS,
        max_response_bytes=MAX_RESPONSE_BYTES,
        max_output_bytes=MAX_OUTPUT_BYTES,
        base_package_coordinate=_RETRY_4_ACCEPTANCE_BASE_COORDINATE,
    )
    conflict_result = run_fixed_acceptance_suite(
        suite="exact-and-conflict",
        tarballs={
            scenario: tarballs[scenario]
            for scenario in (
                "exact",
                "identical-race",
                "differing-race",
                "lost-response",
            )
        },
        transport=transport,
        runner=runner,
        timeout_seconds=TIMEOUT_SECONDS,
        max_response_bytes=MAX_RESPONSE_BYTES,
        max_output_bytes=MAX_OUTPUT_BYTES,
        base_package_coordinate=_RETRY_4_ACCEPTANCE_BASE_COORDINATE,
    )

    routed_results = (*absent_result.scenarios, *conflict_result.scenarios)
    assert (
        tuple(
            (result.scenario, result.package_coordinate, result.tag)
            for result in routed_results
        )
        == _RETRY_4_ACCEPTANCE_BINDINGS
    )
    assert tuple((call[0], call[1]) for call in transport.calls) == (
        (coordinates["absent-create-readback"], tags["absent-create-readback"]),
        (coordinates["absent-create-readback"], tags["absent-create-readback"]),
        (coordinates["exact"], tags["exact"]),
        (coordinates["identical-race"], tags["identical-race"]),
        (coordinates["identical-race"], tags["identical-race"]),
        (coordinates["differing-race"], tags["differing-race"]),
        (coordinates["differing-race"], tags["differing-race"]),
        (coordinates["lost-response"], tags["lost-response"]),
        (coordinates["lost-response"], tags["lost-response"]),
    )
    assert all(
        0 < timeout_seconds <= TIMEOUT_SECONDS
        and max_response_bytes == MAX_RESPONSE_BYTES
        for _coordinate, _tag, timeout_seconds, max_response_bytes in transport.calls
    )
    assert tuple(
        (
            scenario,
            argv[2],
            argv[argv.index("--tag") + 1],
        )
        for scenario, argv, _env in runner.calls
    ) == (
        (
            "absent-create-readback",
            str(tarballs["absent-create-readback"]),
            "wdv3-acceptance-13",
        ),
        (
            "identical-race",
            str(tarballs["identical-race"]),
            "wdv3-acceptance-14",
        ),
        (
            "differing-race",
            str(tarballs["differing-race"]),
            "wdv3-acceptance-15",
        ),
        (
            "lost-response",
            str(tarballs["lost-response"]),
            "wdv3-acceptance-16",
        ),
    )
    assert (
        absent_result.suite,
        absent_result.scenario_inventory,
        absent_result.mutation_classification,
        absent_result.result,
    ) == (
        "absent-create-readback",
        ("absent-create-readback",),
        "incomplete",
        "incomplete",
    )
    assert (
        conflict_result.suite,
        conflict_result.scenario_inventory,
        conflict_result.mutation_classification,
        conflict_result.result,
    ) == (
        "exact-and-conflict",
        ("exact", "identical-race", "differing-race", "lost-response"),
        "unknown",
        "unknown",
    )
    assert tuple(result.result for result in routed_results) == (
        "created-without-request-proof",
        "exact-no-mutation",
        "created",
        "differing-race-winner-not-proven",
        "lost-response",
    )


def test_retry_4_validated_proof_accepts_exact_coordinate_tag_bindings() -> (
    None
):
    _require_retry_4_adapter_profile()
    proofs = tuple(
        AcceptanceRequestProof.from_validated_exchange(
            raw_request=f"retry-4-request-{index}-{scenario}".encode(),
            tarball=f"retry-4-tarball-{index}-{scenario}".encode(),
            package_coordinate=coordinate,
            tag=tag,
            upstream_status=201,
            selected_headers={
                "Content-Type": "application/json",
                "ETag": f'"retry-4-{index}"',
            },
            response_body=f'{{"retry":4,"scenario":"{scenario}"}}'.encode(),
        )
        for index, (scenario, coordinate, tag) in enumerate(
            _RETRY_4_ACCEPTANCE_BINDINGS,
            start=1,
        )
    )

    assert tuple(
        (proof.package_coordinate, proof.tag) for proof in proofs
    ) == tuple(
        (coordinate, tag)
        for _scenario, coordinate, tag in _RETRY_4_ACCEPTANCE_BINDINGS
    )
    assert tuple(proof.upstream_status for proof in proofs) == (201,) * 5
    assert tuple(proof.selected_headers for proof in proofs) == tuple(
        (
            ("content-type", "application/json"),
            ("etag", f'"retry-4-{index}"'),
        )
        for index in range(1, 6)
    )
    assert len({proof.request_digest for proof in proofs}) == 5
    assert len({proof.response_identity_digest for proof in proofs}) == 5
    assert {proof.to_document()["schema"] for proof in proofs} == {
        "workflow-delivery/v3/validated-acceptance-request-proof"
    }


def test_retry_4_validated_proof_rejects_historical_substitutions_in_both_directions() -> (
    None
):
    _require_retry_4_adapter_profile()
    substitutions = (
        (
            "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.13",
            "wdv3-acceptance-9",
        ),
        (
            "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.9",
            "wdv3-acceptance-13",
        ),
    )
    observed_messages = []

    for index, (coordinate, tag) in enumerate(substitutions, start=1):
        with pytest.raises(
            ValueError,
            match="coordinate or tag is not fixed",
        ) as raised:
            AcceptanceRequestProof.from_validated_exchange(
                raw_request=f"retry-4-substitution-request-{index}".encode(),
                tarball=f"retry-4-substitution-tarball-{index}".encode(),
                package_coordinate=coordinate,
                tag=tag,
                upstream_status=201,
                selected_headers={"Content-Type": "application/json"},
                response_body=b'{"ok":true}',
            )
        observed_messages.append(str(raised.value))

    assert observed_messages == [
        "validated request coordinate or tag is not fixed",
        "validated request coordinate or tag is not fixed",
    ]
