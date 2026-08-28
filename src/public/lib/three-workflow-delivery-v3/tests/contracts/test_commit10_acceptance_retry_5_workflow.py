"""Exact contract for the temporary retry-5 preparation workflow."""

# ruff: noqa: D103, E501, PLR2004, S603, SLF001

from __future__ import annotations

import ast
import hashlib
import json
import os
import re
import subprocess
import sys
from dataclasses import replace
from pathlib import Path
from typing import Any, cast

import pytest
import yaml
from three_workflow_delivery_v3 import canonical
from three_workflow_delivery_v3.adapters.github_packages import (
    AcceptanceRunnerDiagnostic,
    FixedAcceptanceSuiteResult,
    FixedCoordinateAcceptanceProbeResult,
    ValidatedAcceptanceRequestProof,
    fixed_acceptance_coordinates,
    fixed_acceptance_scenario_specs,
)
from three_workflow_delivery_v3.records import governance as governance_module

REPO_ROOT = Path(__file__).resolve().parents[6]
WORKFLOW_RELATIVE_PATH = (
    ".github/workflows/workflow-delivery-v3-buddy-smoke-acceptance-retry-5.yml"
)
WORKFLOW_PATH = REPO_ROOT / WORKFLOW_RELATIVE_PATH
WORKFLOW_STEM = "workflow-delivery-v3-buddy-smoke-acceptance-retry-5"
ENVIRONMENT = WORKFLOW_STEM

ZERO_SHA = "0" * 40
TEST_ONLY_NONZERO_TARGET_SHA = "d" * 40
PACKAGE_NAME = "@hcoona/hcoona-release-smoke-npm"
BASE_COORDINATE = f"{PACKAGE_NAME}@0.0.0-wdv3-acceptance.17"
CONFIRMATION = "I_ACCEPT_DISPOSABLE_GITHUB_PACKAGES_PROBES_RETRY_5"
CONFIRMATION_DIGEST = (
    "sha256:71fdd8f8cbb3ab90dd94745a18337d89a893fbdaeea35fafa733bc13d75c308f"
)
SCENARIO_BINDINGS = (
    (
        "absent-create-readback",
        f"{PACKAGE_NAME}@0.0.0-wdv3-acceptance.17",
        "wdv3-acceptance-17",
    ),
    (
        "exact",
        f"{PACKAGE_NAME}@0.0.0-wdv3-acceptance.17",
        "wdv3-acceptance-17",
    ),
    (
        "identical-race",
        f"{PACKAGE_NAME}@0.0.0-wdv3-acceptance.18",
        "wdv3-acceptance-18",
    ),
    (
        "differing-race",
        f"{PACKAGE_NAME}@0.0.0-wdv3-acceptance.19",
        "wdv3-acceptance-19",
    ),
    (
        "lost-response",
        f"{PACKAGE_NAME}@0.0.0-wdv3-acceptance.20",
        "wdv3-acceptance-20",
    ),
)

JOB_ORDER = (
    "validate-fixed-inputs",
    "acceptance-review",
    "probe-absent-create-readback",
    "probe-exact-and-conflict",
    "capture-governance-evidence",
)
PROBE_JOBS = (
    "probe-absent-create-readback",
    "probe-exact-and-conflict",
)

CHECKOUT = "actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1"
SETUP_UV = "astral-sh/setup-uv@20cfd1bf945f4377ade1205e4dbc17946fc9a30d"
SETUP_NODE = "actions/setup-node@820762786026740c76f36085b0efc47a31fe5020"
UPLOAD = "actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a"
ACTION_PINS = {CHECKOUT, SETUP_UV, SETUP_NODE, UPLOAD}


def _load_workflow() -> tuple[dict[str, Any], str]:
    """Load only inside test execution so collection succeeds before YAML."""
    assert WORKFLOW_PATH.is_file(), (
        "E-WORKFLOW-ABSENT: required retry-5 workflow is absent at "
        f"{WORKFLOW_RELATIVE_PATH}"
    )
    raw = WORKFLOW_PATH.read_text(encoding="utf-8")
    value = yaml.safe_load(raw)
    assert isinstance(value, dict), "retry-5 workflow must be a YAML mapping"
    return cast("dict[str, Any]", value), raw


def _triggers(document: dict[str, Any]) -> dict[str, Any]:
    value = document.get("on")
    if value is None:
        value = cast("dict[object, Any]", document).get(True)
    assert isinstance(value, dict)
    return cast("dict[str, Any]", value)


def _jobs(document: dict[str, Any]) -> dict[str, dict[str, Any]]:
    value = document.get("jobs")
    assert isinstance(value, dict)
    assert all(isinstance(job, dict) for job in value.values())
    return cast("dict[str, dict[str, Any]]", value)


def _steps(job: dict[str, Any]) -> list[dict[str, Any]]:
    value = job.get("steps")
    assert isinstance(value, list)
    assert all(isinstance(step, dict) for step in value)
    return cast("list[dict[str, Any]]", value)


def _step(job: dict[str, Any], name: str) -> dict[str, Any]:
    matches = [step for step in _steps(job) if step.get("name") == name]
    assert len(matches) == 1, f"{name!r} must occur exactly once"
    return matches[0]


def _run(step: dict[str, Any]) -> str:
    value = step.get("run")
    assert isinstance(value, str)
    return value


def _needs(job: dict[str, Any]) -> tuple[str, ...]:
    value = job.get("needs", ())
    if isinstance(value, str):
        return (value,)
    assert isinstance(value, (list, tuple))
    assert all(isinstance(name, str) for name in value)
    return tuple(value)


def _transitive_needs(
    jobs: dict[str, dict[str, Any]],
    job_name: str,
) -> set[str]:
    dependencies: set[str] = set()
    pending = list(_needs(jobs[job_name]))
    while pending:
        dependency = pending.pop()
        assert dependency in jobs
        if dependency in dependencies:
            continue
        dependencies.add(dependency)
        pending.extend(_needs(jobs[dependency]))
    return dependencies


def _validation_script(document: dict[str, Any]) -> str:
    return _run(
        _step(
            _jobs(document)["validate-fixed-inputs"],
            "Fail closed before review or mutation",
        )
    )


def _guard_conditions(script: str) -> list[str]:
    return re.findall(r"if \[\[ (.+) \]\]; then", script)


def _execute_shell(
    script: str,
    *,
    environment: dict[str, str],
    cwd: Path | None = None,
) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        (
            "/usr/bin/bash",
            "--noprofile",
            "--norc",
            "-euo",
            "pipefail",
            "-c",
            script,
        ),
        check=False,
        capture_output=True,
        text=True,
        cwd=cwd,
        env=os.environ | environment | {"UV_OFFLINE": "1"},
        timeout=10,
    )


def _run_fixed_input_guard(
    document: dict[str, Any],
    *,
    input_target_sha: str,
    fixed_target_sha: str,
    overrides: dict[str, str] | None = None,
) -> subprocess.CompletedProcess[str]:
    environment = {
        "INPUT_TARGET_SHA": input_target_sha,
        "INPUT_PACKAGE_COORDINATE": BASE_COORDINATE,
        "INPUT_CONFIRM": CONFIRMATION,
        "WDV3_ACCEPTANCE_TARGET_SHA": fixed_target_sha,
        "WDV3_ACCEPTANCE_PACKAGE_COORDINATE": BASE_COORDINATE,
        "WDV3_ACCEPTANCE_CONFIRMATION": CONFIRMATION,
        "WDV3_ACCEPTANCE_REF": "refs/heads/main",
        "GITHUB_REF": "refs/heads/main",
        "GITHUB_RUN_ATTEMPT": "1",
    }
    environment.update(overrides or {})
    return _execute_shell(
        _validation_script(document),
        environment=environment,
    )


def _terminal_python(document: dict[str, Any]) -> str:
    run = _run(
        _step(
            _jobs(document)["capture-governance-evidence"],
            "Form and admit terminal Governance evidence",
        )
    )
    marker = "python - <<'PY'\n"
    assert run.count(marker) == 1
    script, terminator = run.split(marker, 1)[1].rsplit("\nPY", 1)
    assert not terminator.strip()
    return script


def _terminal_environment(
    evidence_path: Path,
    overrides: dict[str, str] | None = None,
) -> dict[str, str]:
    environment = {
        "INPUT_TARGET_SHA": "f" * 40,
        "INPUT_PACKAGE_COORDINATE": (
            f"{PACKAGE_NAME}@0.0.0-wdv3-acceptance.999"
        ),
        "VALIDATE_RESULT": "failure",
        "REVIEW_RESULT": "skipped",
        "ABSENT_JOB_RESULT": "skipped",
        "CONFLICT_JOB_RESULT": "skipped",
        "REVIEW_ARTIFACT_ID": "",
        "ABSENT_RESULT": "",
        "ABSENT_MUTATION_CLASSIFICATION": "",
        "ABSENT_SCENARIO_INVENTORY": "",
        "ABSENT_RECORD_JSON": "",
        "ABSENT_RECORD_DIGEST": "",
        "ABSENT_ARTIFACT_ID": "",
        "ABSENT_ARTIFACT_DIGEST": "",
        "CONFLICT_RESULT": "",
        "CONFLICT_MUTATION_CLASSIFICATION": "",
        "CONFLICT_SCENARIO_INVENTORY": "",
        "CONFLICT_RECORD_JSON": "",
        "CONFLICT_RECORD_DIGEST": "",
        "CONFLICT_ARTIFACT_ID": "",
        "CONFLICT_ARTIFACT_DIGEST": "",
        "WDV3_ACCEPTANCE_TARGET_SHA": ZERO_SHA,
        "WDV3_ACCEPTANCE_PACKAGE_COORDINATE": BASE_COORDINATE,
        "WDV3_ACCEPTANCE_CONFIRMATION": CONFIRMATION,
        "WDV3_FILE": str(evidence_path),
        "GITHUB_REPOSITORY": "hcoona/three",
        "GITHUB_REF": "refs/heads/main",
        "GITHUB_WORKFLOW_SHA": "a" * 40,
        "GITHUB_RUN_ID": "424242",
        "GITHUB_RUN_ATTEMPT": "1",
    }
    environment.update(overrides or {})
    return environment


def _execute_terminal(
    document: dict[str, Any],
    tmp_path: Path,
    overrides: dict[str, str] | None = None,
) -> tuple[subprocess.CompletedProcess[str], Path]:
    evidence_path = tmp_path / ".wdv3" / "retry-5-evidence.json"
    evidence_path.parent.mkdir(parents=True)
    completed = subprocess.run(
        (sys.executable, "-c", _terminal_python(document)),
        check=False,
        capture_output=True,
        text=True,
        cwd=tmp_path,
        env=os.environ
        | _terminal_environment(evidence_path, overrides)
        | {"UV_OFFLINE": "1"},
        timeout=10,
    )
    return completed, evidence_path


def _install_test_only_finalized_profile(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    profiles = governance_module._GOVERNANCE_ACCEPTANCE_PROFILES
    preparation = tuple(
        profile
        for profile in profiles
        if profile.package_coordinate == BASE_COORDINATE
    )
    assert len(preparation) == 1
    finalized = governance_module._GovernanceAcceptanceProfile(
        package_coordinate=preparation[0].package_coordinate,
        workflow_path=preparation[0].workflow_path,
        environment=preparation[0].environment,
        target_sha=TEST_ONLY_NONZERO_TARGET_SHA,
        confirmation_digest=preparation[0].confirmation_digest,
        scenario_coordinates=preparation[0].scenario_coordinates,
    )
    monkeypatch.setattr(
        governance_module,
        "_GOVERNANCE_ACCEPTANCE_PROFILES",
        tuple(
            finalized if profile is preparation[0] else profile
            for profile in profiles
        ),
    )


def _retry_5_terminal_proof(
    scenario: str,
    *,
    upstream_status: int,
) -> ValidatedAcceptanceRequestProof:
    binding = next(
        (coordinate, tag)
        for bound_scenario, coordinate, tag in SCENARIO_BINDINGS
        if bound_scenario == scenario
    )
    label = f"retry-5-terminal-{upstream_status}-{scenario}".encode()
    return ValidatedAcceptanceRequestProof.from_validated_exchange(
        raw_request=b'{"request":"' + label + b'"}',
        tarball=b"tarball-" + label,
        package_coordinate=binding[0],
        tag=binding[1],
        upstream_status=upstream_status,
        selected_headers={
            "Content-Type": "application/json",
            "ETag": f'"{scenario}-{upstream_status}"',
        },
        response_body=b'{"accepted":true}',
    )


def _retry_5_complete_terminal_suites(
    *,
    upstream_status: int,
) -> tuple[FixedAcceptanceSuiteResult, FixedAcceptanceSuiteResult]:
    coordinates = {
        scenario: (coordinate, tag)
        for scenario, coordinate, tag in SCENARIO_BINDINGS
    }
    absent_proof = _retry_5_terminal_proof(
        "absent-create-readback",
        upstream_status=upstream_status,
    )
    lost_response_proof = _retry_5_terminal_proof(
        "lost-response",
        upstream_status=upstream_status,
    )
    absent_diagnostic = AcceptanceRunnerDiagnostic(
        exit_classification="protocol-confirmed",
        upstream_status=upstream_status,
        exception_category=None,
        request_correlation_digest=absent_proof.request_digest,
    )

    def result(  # noqa: PLR0913
        scenario: str,
        *,
        pre_state: str,
        post_state: str,
        response_result: str,
        action_executed: bool,
        mutation_started: bool,
        content_sha512: str,
        response_identity_digest: str,
        diagnostics: tuple[str, ...] = (),
        proof: ValidatedAcceptanceRequestProof | None = None,
        runner_diagnostic: AcceptanceRunnerDiagnostic | None = None,
    ) -> FixedCoordinateAcceptanceProbeResult:
        coordinate, tag = coordinates[scenario]
        return FixedCoordinateAcceptanceProbeResult(
            scenario=scenario,
            package_coordinate=coordinate,
            tag=tag,
            pre_state=pre_state,
            post_state=post_state,
            result=response_result,
            mutation_classification="complete",
            action_executed=action_executed,
            mutation_started=mutation_started,
            response_identity_digest=response_identity_digest,
            content_sha512=content_sha512,
            diagnostics=diagnostics,
            validated_request_proof=proof,
            runner_diagnostic=runner_diagnostic,
        )

    shared_content = absent_proof.tarball_sha512
    return (
        FixedAcceptanceSuiteResult(
            suite="absent-create-readback",
            scenarios=(
                result(
                    "absent-create-readback",
                    pre_state="absent",
                    post_state="exact",
                    response_result="protocol-confirmed",
                    action_executed=True,
                    mutation_started=True,
                    content_sha512=absent_proof.tarball_sha512,
                    response_identity_digest=(
                        absent_proof.response_identity_digest
                    ),
                    proof=absent_proof,
                    runner_diagnostic=absent_diagnostic,
                ),
            ),
        ),
        FixedAcceptanceSuiteResult(
            suite="exact-and-conflict",
            scenarios=(
                result(
                    "exact",
                    pre_state="exact",
                    post_state="exact",
                    response_result="exact-no-mutation",
                    action_executed=False,
                    mutation_started=False,
                    content_sha512=shared_content,
                    response_identity_digest="sha256:" + ("1" * 64),
                ),
                result(
                    "identical-race",
                    pre_state="absent",
                    post_state="exact",
                    response_result="identical-race-exact",
                    action_executed=True,
                    mutation_started=True,
                    content_sha512="sha512:" + ("3" * 128),
                    response_identity_digest="sha256:" + ("2" * 64),
                    diagnostics=("identical-race-exact",),
                ),
                result(
                    "differing-race",
                    pre_state="absent",
                    post_state="conflicting",
                    response_result="differing-race-conflict",
                    action_executed=True,
                    mutation_started=True,
                    content_sha512="sha512:" + ("4" * 128),
                    response_identity_digest="sha256:" + ("3" * 64),
                    diagnostics=("conflicting-remote-bytes-or-tag",),
                ),
                result(
                    "lost-response",
                    pre_state="absent",
                    post_state="exact",
                    response_result="lost-response-exact-after-start",
                    action_executed=True,
                    mutation_started=True,
                    content_sha512=lost_response_proof.tarball_sha512,
                    response_identity_digest=(
                        lost_response_proof.response_identity_digest
                    ),
                    diagnostics=("mutation-started-and-readback-exact",),
                    proof=lost_response_proof,
                ),
            ),
        ),
    )


def _retry_5_noncomplete_conflict_suite(
    complete: FixedAcceptanceSuiteResult,
    *,
    classification: str,
) -> FixedAcceptanceSuiteResult:
    scenarios = list(complete.scenarios)
    if classification == "incomplete":
        scenarios[0] = replace(
            scenarios[0],
            result="runner-failed-before-mutation",
            mutation_classification="incomplete",
            diagnostics=("runner-did-not-prove-mutation-start",),
            validated_request_proof=None,
            runner_diagnostic=None,
        )
    else:
        scenarios[-1] = replace(
            scenarios[-1],
            result="lost-response",
            mutation_classification="unknown",
            diagnostics=(
                "mutation-may-have-started",
                "human-reconciliation-required",
            ),
            validated_request_proof=None,
            runner_diagnostic=None,
        )
    return FixedAcceptanceSuiteResult(
        suite=complete.suite,
        scenarios=tuple(scenarios),
    )


def _complete_terminal_overrides(
    absent: FixedAcceptanceSuiteResult,
    conflict: FixedAcceptanceSuiteResult,
) -> dict[str, str]:
    absent_document = absent.to_document()
    conflict_document = conflict.to_document()
    return {
        "INPUT_TARGET_SHA": TEST_ONLY_NONZERO_TARGET_SHA,
        "VALIDATE_RESULT": "success",
        "REVIEW_RESULT": "success",
        "ABSENT_JOB_RESULT": "success",
        "CONFLICT_JOB_RESULT": "success",
        "REVIEW_ARTIFACT_ID": "700",
        "ABSENT_RESULT": absent.result,
        "ABSENT_MUTATION_CLASSIFICATION": absent.mutation_classification,
        "ABSENT_SCENARIO_INVENTORY": json.dumps(
            list(absent.scenario_inventory)
        ),
        "ABSENT_RECORD_JSON": json.dumps(absent_document),
        "ABSENT_RECORD_DIGEST": cast(
            "str",
            absent_document["record-digest"],
        ),
        "ABSENT_ARTIFACT_ID": "701",
        "ABSENT_ARTIFACT_DIGEST": "a" * 64,
        "CONFLICT_RESULT": conflict.result,
        "CONFLICT_MUTATION_CLASSIFICATION": conflict.mutation_classification,
        "CONFLICT_SCENARIO_INVENTORY": json.dumps(
            list(conflict.scenario_inventory)
        ),
        "CONFLICT_RECORD_JSON": json.dumps(conflict_document),
        "CONFLICT_RECORD_DIGEST": cast(
            "str",
            conflict_document["record-digest"],
        ),
        "CONFLICT_ARTIFACT_ID": "702",
        "CONFLICT_ARTIFACT_DIGEST": "sha256:" + ("b" * 64),
        "WDV3_ACCEPTANCE_TARGET_SHA": TEST_ONLY_NONZERO_TARGET_SHA,
        "GITHUB_WORKFLOW_SHA": "e" * 40,
    }


def _execute_terminal_in_process(
    document: dict[str, Any],
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    overrides: dict[str, str],
) -> Path:
    evidence_path = tmp_path / ".wdv3" / "retry-5-evidence.json"
    evidence_path.parent.mkdir(parents=True)
    environment = _terminal_environment(evidence_path, overrides)
    with monkeypatch.context() as execution:
        execution.chdir(tmp_path)
        for name, value in environment.items():
            execution.setenv(name, value)
        exec(  # noqa: S102
            compile(
                _terminal_python(document),
                str(WORKFLOW_PATH),
                "exec",
            ),
            {"__name__": "__main__"},
        )
    return evidence_path


def _assigned_value(tree: ast.Module, name: str) -> ast.expr:
    matches = [
        node.value
        for node in tree.body
        if isinstance(node, ast.Assign)
        and any(
            isinstance(target, ast.Name) and target.id == name
            for target in node.targets
        )
    ]
    assert len(matches) == 1, f"{name!r} must be assigned exactly once"
    return matches[0]


def _dict_fields(node: ast.expr) -> dict[str, ast.expr]:
    assert isinstance(node, ast.Dict)
    fields: dict[str, ast.expr] = {}
    for key, value in zip(node.keys, node.values, strict=True):
        assert isinstance(key, ast.Constant)
        assert isinstance(key.value, str)
        fields[key.value] = value
    return fields


def test_retry_5_workflow_identity_dispatch_and_preparation_defaults_are_exact() -> (
    None
):
    document, raw = _load_workflow()
    triggers = _triggers(document)
    inputs = triggers["workflow_dispatch"]["inputs"]

    assert WORKFLOW_PATH.relative_to(REPO_ROOT).as_posix() == (
        WORKFLOW_RELATIVE_PATH
    )
    assert WORKFLOW_PATH.name == f"{WORKFLOW_STEM}.yml"
    assert WORKFLOW_PATH.stem == WORKFLOW_STEM == ENVIRONMENT
    assert document["name"] == WORKFLOW_STEM
    assert tuple(triggers) == ("workflow_dispatch",)
    assert tuple(inputs) == ("target_sha", "package_coordinate", "confirm")
    assert inputs == {
        "target_sha": {
            "description": "Reviewed protected-finalization target SHA",
            "required": True,
            "default": ZERO_SHA,
            "type": "string",
        },
        "package_coordinate": {
            "description": "Fixed disposable GitHub Packages base coordinate",
            "required": True,
            "default": BASE_COORDINATE,
            "type": "string",
        },
        "confirm": {
            "description": "Explicit disposable-probe confirmation",
            "required": True,
            "default": CONFIRMATION,
            "type": "string",
        },
    }
    assert document["env"] == {
        "WDV3_ACCEPTANCE_TARGET_SHA": ZERO_SHA,
        "WDV3_ACCEPTANCE_PACKAGE_COORDINATE": BASE_COORDINATE,
        "WDV3_ACCEPTANCE_CONFIRMATION": CONFIRMATION,
        "WDV3_ACCEPTANCE_REF": "refs/heads/main",
        "WDV3_PURPOSE": "destination-acceptance",
    }
    assert "sha256:" + hashlib.sha256(CONFIRMATION.encode()).hexdigest() == (
        CONFIRMATION_DIGEST
    )
    assert document["concurrency"] == {
        "group": (
            "hcoona-release-smoke-npm-workflow-delivery-v3-"
            "buddy-smoke-acceptance-retry-5"
        ),
        "cancel-in-progress": False,
    }
    assert TEST_ONLY_NONZERO_TARGET_SHA not in raw
    action_shas = {action.rsplit("@", 1)[1] for action in ACTION_PINS}
    literal_sha_tokens = set(
        re.findall(r"(?<![0-9a-f])[0-9a-f]{40}(?![0-9a-f])", raw)
    )
    assert literal_sha_tokens == action_shas | {ZERO_SHA}
    retry_5_profiles = tuple(
        profile
        for profile in governance_module._GOVERNANCE_ACCEPTANCE_PROFILES
        if profile.package_coordinate == BASE_COORDINATE
    )
    assert tuple(
        (profile.workflow_path, profile.environment, profile.target_sha)
        for profile in retry_5_profiles
    ) == ((WORKFLOW_RELATIVE_PATH, ENVIRONMENT, ZERO_SHA),)


def test_retry_5_workflow_is_manual_only_with_exact_five_job_dag() -> None:
    document, _raw = _load_workflow()
    triggers = _triggers(document)
    jobs = _jobs(document)

    assert tuple(triggers) == ("workflow_dispatch",)
    assert set(triggers).isdisjoint(
        {
            "push",
            "pull_request",
            "pull_request_target",
            "schedule",
            "workflow_call",
            "repository_dispatch",
            "workflow_run",
            "release",
        }
    )
    assert tuple(jobs) == JOB_ORDER
    assert {name: _needs(job) for name, job in jobs.items()} == {
        "validate-fixed-inputs": (),
        "acceptance-review": ("validate-fixed-inputs",),
        "probe-absent-create-readback": ("acceptance-review",),
        "probe-exact-and-conflict": ("probe-absent-create-readback",),
        "capture-governance-evidence": JOB_ORDER[:-1],
    }
    assert {name: _transitive_needs(jobs, name) for name in JOB_ORDER} == {
        "validate-fixed-inputs": set(),
        "acceptance-review": {"validate-fixed-inputs"},
        "probe-absent-create-readback": {
            "validate-fixed-inputs",
            "acceptance-review",
        },
        "probe-exact-and-conflict": {
            "validate-fixed-inputs",
            "acceptance-review",
            "probe-absent-create-readback",
        },
        "capture-governance-evidence": set(JOB_ORDER[:-1]),
    }
    assert {
        name: job["runs-on"] for name, job in jobs.items()
    } == dict.fromkeys(
        JOB_ORDER,
        "ubuntu-24.04",
    )
    assert all("uses" not in job for job in jobs.values())


def test_retry_5_first_attempt_guards_and_terminal_always_capture_are_exact() -> (
    None
):
    jobs = _jobs(_load_workflow()[0])

    assert {name: job.get("if") for name, job in jobs.items()} == {
        "validate-fixed-inputs": "${{ github.run_attempt == 1 }}",
        "acceptance-review": "${{ github.run_attempt == 1 }}",
        "probe-absent-create-readback": "${{ github.run_attempt == 1 }}",
        "probe-exact-and-conflict": (
            "${{ github.run_attempt == 1 && "
            "needs.probe-absent-create-readback.result == 'success' }}"
        ),
        "capture-governance-evidence": (
            "${{ always() && github.run_attempt == 1 }}"
        ),
    }
    assert all(
        "github.run_attempt == 1" in cast("str", jobs[name]["if"])
        for name in JOB_ORDER
    )
    assert jobs["capture-governance-evidence"]["if"].startswith(
        "${{ always() && "
    )
    for job_name in PROBE_JOBS:
        assert [step.get("if") for step in _steps(jobs[job_name])] == [
            None,
            None,
            None,
            None,
            None,
            "${{ always() }}",
            "${{ always() }}",
            "${{ always() }}",
        ]


def test_retry_5_permissions_environment_and_token_boundaries_are_exact() -> (
    None
):
    document, raw = _load_workflow()
    jobs = _jobs(document)

    assert document["permissions"] == {}
    assert {name: job["permissions"] for name, job in jobs.items()} == {
        "validate-fixed-inputs": {"contents": "read"},
        "acceptance-review": {},
        "probe-absent-create-readback": {
            "contents": "read",
            "packages": "write",
        },
        "probe-exact-and-conflict": {
            "contents": "read",
            "packages": "write",
        },
        "capture-governance-evidence": {"contents": "read"},
    }
    assert {
        name
        for name, job in jobs.items()
        if job["permissions"].get("packages") == "write"
    } == set(PROBE_JOBS)
    assert {
        name: job["environment"]
        for name, job in jobs.items()
        if "environment" in job
    } == {"acceptance-review": ENVIRONMENT}
    assert all(
        job.get("continue-on-error", False) is False for job in jobs.values()
    )
    assert all(
        step.get("continue-on-error", False) is False
        for job in jobs.values()
        for step in _steps(job)
    )
    token_steps = [
        (job_name, step["name"])
        for job_name, job in jobs.items()
        for step in _steps(job)
        if "${{ github.token }}" in json.dumps(step, sort_keys=True)
    ]
    assert token_steps == [
        (
            "probe-absent-create-readback",
            "Run fixed absent/create/readback suite",
        ),
        ("probe-exact-and-conflict", "Run fixed exact and conflict suite"),
    ]
    assert raw.count("${{ github.token }}") == len(PROBE_JOBS)
    lowered = raw.casefold()
    for forbidden in (
        "secrets:",
        "secrets.",
        "id-token:",
        "personal_access_token",
        "github_pat",
        "npm_token",
    ):
        assert forbidden not in lowered


@pytest.mark.parametrize(
    ("input_target", "fixed_target", "overrides", "expected_status"),
    [
        pytest.param(
            TEST_ONLY_NONZERO_TARGET_SHA,
            TEST_ONLY_NONZERO_TARGET_SHA,
            {},
            0,
            id="test-only-finalized-shape-control",
        ),
        pytest.param(ZERO_SHA, ZERO_SHA, {}, 1, id="preparation-zero-target"),
        pytest.param(
            "e" * 40,
            TEST_ONLY_NONZERO_TARGET_SHA,
            {},
            1,
            id="wrong-target",
        ),
        pytest.param(
            TEST_ONLY_NONZERO_TARGET_SHA,
            TEST_ONLY_NONZERO_TARGET_SHA,
            {
                "INPUT_PACKAGE_COORDINATE": f"{PACKAGE_NAME}@0.0.0-wdv3-acceptance.18"
            },
            1,
            id="wrong-package",
        ),
        pytest.param(
            TEST_ONLY_NONZERO_TARGET_SHA,
            TEST_ONLY_NONZERO_TARGET_SHA,
            {"INPUT_CONFIRM": "NOT_THE_RETRY_5_CONFIRMATION"},
            1,
            id="wrong-sentinel",
        ),
        pytest.param(
            TEST_ONLY_NONZERO_TARGET_SHA,
            TEST_ONLY_NONZERO_TARGET_SHA,
            {"GITHUB_REF": "refs/heads/not-main"},
            1,
            id="wrong-protected-ref",
        ),
        pytest.param(
            TEST_ONLY_NONZERO_TARGET_SHA,
            TEST_ONLY_NONZERO_TARGET_SHA,
            {"GITHUB_RUN_ATTEMPT": "2"},
            1,
            id="non-first-attempt",
        ),
    ],
)
def test_retry_5_fixed_input_guard_executes_before_review_and_package_write(
    input_target: str,
    fixed_target: str,
    overrides: dict[str, str],
    expected_status: int,
) -> None:
    document, raw = _load_workflow()
    jobs = _jobs(document)
    validation = jobs["validate-fixed-inputs"]
    guard = _step(validation, "Fail closed before review or mutation")
    script = _validation_script(document)

    assert TEST_ONLY_NONZERO_TARGET_SHA not in raw
    assert [step["name"] for step in _steps(validation)] == [guard["name"]]
    assert guard["env"] == {
        "INPUT_TARGET_SHA": "${{ inputs.target_sha }}",
        "INPUT_PACKAGE_COORDINATE": "${{ inputs.package_coordinate }}",
        "INPUT_CONFIRM": "${{ inputs.confirm }}",
    }
    assert _guard_conditions(script) == [
        '"${INPUT_TARGET_SHA}" != "${WDV3_ACCEPTANCE_TARGET_SHA}"',
        f'"${{WDV3_ACCEPTANCE_TARGET_SHA}}" == "{ZERO_SHA}"',
        f'"${{INPUT_TARGET_SHA}}" == "{ZERO_SHA}"',
        (
            '"${INPUT_PACKAGE_COORDINATE}" != '
            '"${WDV3_ACCEPTANCE_PACKAGE_COORDINATE}"'
        ),
        '"${INPUT_CONFIRM}" != "${WDV3_ACCEPTANCE_CONFIRMATION}"',
        '"${GITHUB_REF}" != "${WDV3_ACCEPTANCE_REF}"',
        '"${GITHUB_RUN_ATTEMPT}" != "1"',
    ]
    assert script.count("exit 1") == len(_guard_conditions(script))
    assert validation.get("continue-on-error", False) is False
    assert guard.get("continue-on-error", False) is False
    for downstream in ("acceptance-review", *PROBE_JOBS):
        assert "validate-fixed-inputs" in _transitive_needs(jobs, downstream)

    completed = _run_fixed_input_guard(
        document,
        input_target_sha=input_target,
        fixed_target_sha=fixed_target,
        overrides=overrides,
    )

    assert (completed.returncode, completed.stdout, completed.stderr) == (
        expected_status,
        "",
        "",
    )


def test_retry_5_job_timeouts_and_operation_deadlines_are_exact() -> None:
    jobs = _jobs(_load_workflow()[0])

    assert {name: job["timeout-minutes"] for name, job in jobs.items()} == dict(
        zip(JOB_ORDER, (5, 30, 10, 15, 10), strict=True)
    )
    deadlines = {}
    for job_name in PROBE_JOBS:
        probe = next(
            step for step in _steps(jobs[job_name]) if step.get("id") == "probe"
        )
        matches = re.findall(r"--timeout-seconds ([0-9]+)", _run(probe))
        assert len(matches) == 1
        deadlines[job_name] = int(matches[0])
    assert deadlines == {
        "probe-absent-create-readback": 120,
        "probe-exact-and-conflict": 300,
    }
    assert (
        "timeout"
        not in _triggers(_load_workflow()[0])["workflow_dispatch"]["inputs"]
    )


def test_retry_5_scenarios_and_immutable_artifacts_are_exact() -> None:
    document, _raw = _load_workflow()
    jobs = _jobs(document)
    actual_specs = fixed_acceptance_scenario_specs(BASE_COORDINATE)
    actual_coordinates = fixed_acceptance_coordinates(BASE_COORDINATE)

    assert (
        tuple(
            (
                scenario,
                f"{PACKAGE_NAME}@{version}",
                tag,
            )
            for scenario, version, tag in actual_specs
        )
        == SCENARIO_BINDINGS
    )
    assert tuple(actual_coordinates.items()) == tuple(
        (scenario, coordinate)
        for scenario, coordinate, _tag in SCENARIO_BINDINGS
    )
    assert tuple(scenario for scenario, *_rest in SCENARIO_BINDINGS) == (
        "absent-create-readback",
        "exact",
        "identical-race",
        "differing-race",
        "lost-response",
    )
    assert SCENARIO_BINDINGS[0][1:] == SCENARIO_BINDINGS[1][1:]
    assert len(set(SCENARIO_BINDINGS[2:])) == len(SCENARIO_BINDINGS[2:])

    expected_uploads = {
        (
            "acceptance-review",
            "Upload immutable acceptance review coordinates",
        ): {
            "name": (
                "wdv3-acceptance-review-r${{ github.run_id }}-"
                "ra${{ github.run_attempt }}"
            ),
            "path": (
                ".wdv3/acceptance-review-r${{ github.run_id }}-"
                "ra${{ github.run_attempt }}.txt"
            ),
        },
        (
            "probe-absent-create-readback",
            "Upload immutable absent/create/readback suite",
        ): {
            "name": (
                "wdv3-acceptance-probe-absent-r${{ github.run_id }}-"
                "ra${{ github.run_attempt }}"
            ),
            "path": (
                ".wdv3/probe-absent-r${{ github.run_id }}-"
                "ra${{ github.run_attempt }}.json"
            ),
        },
        (
            "probe-exact-and-conflict",
            "Upload immutable exact/conflict suite",
        ): {
            "name": (
                "wdv3-acceptance-probe-conflict-r${{ github.run_id }}-"
                "ra${{ github.run_attempt }}"
            ),
            "path": (
                ".wdv3/probe-conflict-r${{ github.run_id }}-"
                "ra${{ github.run_attempt }}.json"
            ),
        },
        (
            "capture-governance-evidence",
            "Upload immutable Governance acceptance evidence",
        ): {
            "name": (
                "wdv3-governance-acceptance-r${{ github.run_id }}-"
                "ra${{ github.run_attempt }}"
            ),
            "path": (
                ".wdv3/governance-acceptance-r${{ github.run_id }}-"
                "ra${{ github.run_attempt }}.json"
            ),
        },
    }
    uploads = {
        (job_name, cast("str", step["name"])): step
        for job_name, job in jobs.items()
        for step in _steps(job)
        if step.get("uses") == UPLOAD
    }
    assert set(uploads) == set(expected_uploads)
    for key, expected_identity in expected_uploads.items():
        upload = uploads[key]
        assert upload["with"] == {
            **expected_identity,
            "if-no-files-found": "error",
            "include-hidden-files": True,
            "retention-days": 45,
            "overwrite": False,
            "archive": False,
        }
        assert upload.get("if", "${{ success() }}") in {
            "${{ success() }}",
            "${{ always() }}",
        }


def test_retry_5_review_artifact_materializes_exact_reviewer_visible_identity(
    tmp_path: Path,
) -> None:
    review = _jobs(_load_workflow()[0])["acceptance-review"]
    materialize = _step(review, "Materialize immutable review coordinates")
    run_id = "424242"
    run_attempt = "1"
    environment = {
        "WDV3_PURPOSE": "destination-acceptance",
        "INPUT_TARGET_SHA": TEST_ONLY_NONZERO_TARGET_SHA,
        "INPUT_PACKAGE_COORDINATE": BASE_COORDINATE,
        "GITHUB_WORKFLOW_REF": (
            f"hcoona/three/{WORKFLOW_RELATIVE_PATH}@refs/heads/main"
        ),
        "GITHUB_RUN_ID": run_id,
        "GITHUB_RUN_ATTEMPT": run_attempt,
    }

    assert materialize["env"] == {
        "INPUT_TARGET_SHA": "${{ inputs.target_sha }}",
        "INPUT_PACKAGE_COORDINATE": "${{ inputs.package_coordinate }}",
    }
    completed = _execute_shell(
        _run(materialize),
        environment=environment,
        cwd=tmp_path,
    )

    assert (completed.returncode, completed.stdout, completed.stderr) == (
        0,
        "",
        "",
    )
    artifact = (
        tmp_path / ".wdv3" / f"acceptance-review-r{run_id}-ra{run_attempt}.txt"
    )
    assert (
        artifact.read_bytes()
        == (
            "purpose=destination-acceptance\n"
            f"target-sha={TEST_ONLY_NONZERO_TARGET_SHA}\n"
            f"package-coordinate={BASE_COORDINATE}\n"
            "workflow=hcoona/three/"
            f"{WORKFLOW_RELATIVE_PATH}@refs/heads/main\n"
            f"run={run_id}\n"
            f"run-attempt={run_attempt}\n"
        ).encode()
    )


_CLASSIFIER_PAIR_CASES = tuple(
    pytest.param(
        {
            "PROBE_RESULT": result,
            "PROBE_MUTATION_CLASSIFICATION": classification,
        },
        (
            result
            if (result, classification)
            in {
                ("success", "complete"),
                ("incomplete", "incomplete"),
                ("unknown", "unknown"),
            }
            else "unknown"
        ),
        (
            classification
            if (result, classification)
            in {
                ("success", "complete"),
                ("incomplete", "incomplete"),
                ("unknown", "unknown"),
            }
            else "unknown"
        ),
        id=f"pair-{result}-{classification}",
    )
    for result in ("success", "incomplete", "unknown", "invalid")
    for classification in ("complete", "incomplete", "unknown", "invalid")
)
_CLASSIFIER_ARTIFACT_CASES = tuple(
    pytest.param(
        {
            "PROBE_RESULT": result,
            "PROBE_MUTATION_CLASSIFICATION": classification,
            **missing,
        },
        "unknown" if result == "unknown" else "incomplete",
        "unknown" if classification == "unknown" else "incomplete",
        id=f"artifact-{result}-{missing_name}",
    )
    for result, classification in (
        ("success", "complete"),
        ("incomplete", "incomplete"),
        ("unknown", "unknown"),
    )
    for missing_name, missing in (
        ("missing-id", {"UPLOAD_ARTIFACT_ID": ""}),
        ("missing-digest", {"UPLOAD_ARTIFACT_DIGEST": ""}),
        (
            "missing-both",
            {
                "UPLOAD_ARTIFACT_ID": "",
                "UPLOAD_ARTIFACT_DIGEST": "",
            },
        ),
    )
)
_CLASSIFIER_OUTCOME_CASES = tuple(
    pytest.param(
        overrides,
        "unknown",
        "unknown",
        id=case_id,
    )
    for case_id, overrides in (
        ("probe-failure-only", {"PROBE_OUTCOME": "failure"}),
        ("upload-failure-only", {"UPLOAD_OUTCOME": "failure"}),
        (
            "both-outcomes-failed",
            {
                "PROBE_OUTCOME": "failure",
                "UPLOAD_OUTCOME": "failure",
            },
        ),
    )
)
_CLASSIFIER_CASES = (
    *_CLASSIFIER_PAIR_CASES,
    *_CLASSIFIER_ARTIFACT_CASES,
    *_CLASSIFIER_OUTCOME_CASES,
)


@pytest.mark.parametrize("job_name", PROBE_JOBS)
@pytest.mark.parametrize(
    ("overrides", "expected_result", "expected_classification"),
    _CLASSIFIER_CASES,
)
def test_retry_5_probe_completion_classifier_is_monotone_and_forwards_records(
    tmp_path: Path,
    job_name: str,
    overrides: dict[str, str],
    expected_result: str,
    expected_classification: str,
) -> None:
    job = _jobs(_load_workflow()[0])[job_name]
    classify = next(
        step for step in _steps(job) if step.get("id") == "classify"
    )
    require = _steps(job)[-1]
    github_output = tmp_path / "classification-output.txt"
    environment = {
        "GITHUB_OUTPUT": str(github_output),
        "PROBE_OUTCOME": "success",
        "PROBE_RESULT": "success",
        "PROBE_MUTATION_CLASSIFICATION": "complete",
        "PROBE_SCENARIO_INVENTORY": '["scenario"]',
        "PROBE_RECORD_DIGEST": "sha256:" + ("a" * 64),
        "PROBE_RECORD_JSON": '{"schema":"suite"}',
        "UPLOAD_OUTCOME": "success",
        "UPLOAD_ARTIFACT_ID": "17",
        "UPLOAD_ARTIFACT_DIGEST": "sha256:" + ("b" * 64),
    }
    environment.update(overrides)

    classified = _execute_shell(_run(classify), environment=environment)

    assert classified.returncode == 0, classified.stderr
    outputs = dict(
        line.split("=", 1)
        for line in github_output.read_text(encoding="utf-8").splitlines()
    )
    assert outputs == {
        "result": expected_result,
        "mutation-classification": expected_classification,
        "scenario-inventory": environment["PROBE_SCENARIO_INVENTORY"],
        "record-digest": environment["PROBE_RECORD_DIGEST"],
        "record-json": environment["PROBE_RECORD_JSON"],
    }
    required = _execute_shell(
        _run(require),
        environment={
            "MUTATION_CLASSIFICATION": expected_classification,
        },
    )
    assert required.returncode == (
        0 if expected_classification == "complete" else 1
    )
    assert (required.stdout, required.stderr) == ("", "")


def test_retry_5_terminal_program_has_exact_expected_one_and_registry_boundary() -> (
    None
):
    document, _raw = _load_workflow()
    terminal = _jobs(document)["capture-governance-evidence"]
    evidence_step = _step(
        terminal,
        "Form and admit terminal Governance evidence",
    )
    script = _terminal_python(document)
    tree = ast.parse(script)
    expected = ast.literal_eval(_assigned_value(tree, "expected"))

    assert evidence_step["env"] == {
        "INPUT_TARGET_SHA": "${{ inputs.target_sha }}",
        "INPUT_PACKAGE_COORDINATE": "${{ inputs.package_coordinate }}",
        "VALIDATE_RESULT": "${{ needs.validate-fixed-inputs.result }}",
        "REVIEW_RESULT": "${{ needs.acceptance-review.result }}",
        "ABSENT_JOB_RESULT": (
            "${{ needs.probe-absent-create-readback.result }}"
        ),
        "CONFLICT_JOB_RESULT": "${{ needs.probe-exact-and-conflict.result }}",
        "REVIEW_ARTIFACT_ID": (
            "${{ needs.acceptance-review.outputs.artifact-id }}"
        ),
        "ABSENT_RESULT": (
            "${{ needs.probe-absent-create-readback.outputs.result }}"
        ),
        "ABSENT_MUTATION_CLASSIFICATION": (
            "${{ needs.probe-absent-create-readback.outputs."
            "mutation-classification }}"
        ),
        "ABSENT_SCENARIO_INVENTORY": (
            "${{ needs.probe-absent-create-readback.outputs."
            "scenario-inventory }}"
        ),
        "ABSENT_RECORD_JSON": (
            "${{ needs.probe-absent-create-readback.outputs.record-json }}"
        ),
        "ABSENT_RECORD_DIGEST": (
            "${{ needs.probe-absent-create-readback.outputs.record-digest }}"
        ),
        "ABSENT_ARTIFACT_ID": (
            "${{ needs.probe-absent-create-readback.outputs.artifact-id }}"
        ),
        "ABSENT_ARTIFACT_DIGEST": (
            "${{ needs.probe-absent-create-readback.outputs.artifact-digest }}"
        ),
        "CONFLICT_RESULT": (
            "${{ needs.probe-exact-and-conflict.outputs.result }}"
        ),
        "CONFLICT_MUTATION_CLASSIFICATION": (
            "${{ needs.probe-exact-and-conflict.outputs."
            "mutation-classification }}"
        ),
        "CONFLICT_SCENARIO_INVENTORY": (
            "${{ needs.probe-exact-and-conflict.outputs.scenario-inventory }}"
        ),
        "CONFLICT_RECORD_JSON": (
            "${{ needs.probe-exact-and-conflict.outputs.record-json }}"
        ),
        "CONFLICT_RECORD_DIGEST": (
            "${{ needs.probe-exact-and-conflict.outputs.record-digest }}"
        ),
        "CONFLICT_ARTIFACT_ID": (
            "${{ needs.probe-exact-and-conflict.outputs.artifact-id }}"
        ),
        "CONFLICT_ARTIFACT_DIGEST": (
            "${{ needs.probe-exact-and-conflict.outputs.artifact-digest }}"
        ),
    }
    assert (
        'export WDV3_FILE=".wdv3/governance-acceptance-'
        'r${GITHUB_RUN_ID}-ra${GITHUB_RUN_ATTEMPT}.json"' in _run(evidence_step)
    )
    assert expected == (
        (
            "probe-absent-create-readback",
            ("absent-create-readback",),
            "ABSENT",
        ),
        (
            "probe-exact-and-conflict",
            ("exact", "identical-race", "differing-race", "lost-response"),
            "CONFLICT",
        ),
    )
    assert tuple(len(inventory) for _probe, inventory, _prefix in expected) == (
        1,
        4,
    )
    evidence_fields = _dict_fields(_assigned_value(tree, "document"))
    assert tuple(evidence_fields) == (
        "schema",
        "purpose",
        "workflow",
        "target-sha",
        "package-coordinate",
        "confirmation-digest",
        "environment",
        "reviewer",
        "recovery",
        "dependency-results",
        "probe-facts",
        "mutation-classification",
        "producer",
        "workflow-run-id",
        "run-attempt",
        "release-lineage",
    )
    assert ast.literal_eval(evidence_fields["schema"]) == (
        "workflow-delivery/v3/governance-acceptance-evidence"
    )
    assert ast.literal_eval(evidence_fields["purpose"]) == (
        "destination-acceptance"
    )
    assert ast.literal_eval(evidence_fields["environment"]) == ENVIRONMENT
    assert ast.literal_eval(evidence_fields["reviewer"]) == {
        "login": None,
        "source": "unavailable-in-job-context",
    }
    assert ast.literal_eval(evidence_fields["producer"]) == (
        "capture-governance-evidence"
    )
    assert ast.literal_eval(evidence_fields["release-lineage"]) == "none"
    workflow_fields = _dict_fields(evidence_fields["workflow"])
    assert ast.literal_eval(workflow_fields["path"]) == WORKFLOW_RELATIVE_PATH
    recovery_fields = _dict_fields(evidence_fields["recovery"])
    assert ast.literal_eval(recovery_fields["environment"]) == ENVIRONMENT
    assert ast.literal_eval(recovery_fields["job"]) == "acceptance-review"

    call_names = [
        ast.unparse(call.func)
        for call in ast.walk(tree)
        if isinstance(call, ast.Call)
    ]
    assert (
        call_names.count("ValidatedAcceptanceRequestProof.from_closed_document")
        == 1
    )
    assert call_names.count("AcceptanceRunnerDiagnostic") == 1
    assert call_names.count("admit_governance_acceptance_evidence") == 1
    assert call_names.count("canonicalize") == 2
    assert script.count(f'"{BASE_COORDINATE}"') == 1
    assert script.count(f'"{CONFIRMATION}"') == 0
    assert script.count(f'"{ZERO_SHA}"') == 2
    assert "suite.to_document()" in script
    assert "suite_document != record_json" in script


def test_retry_5_terminal_program_executes_rejected_dispatch_with_fixed_identity(
    tmp_path: Path,
) -> None:
    document, _raw = _load_workflow()

    completed, evidence_path = _execute_terminal(document, tmp_path)

    assert (completed.returncode, completed.stdout, completed.stderr) == (
        0,
        "",
        "",
    )
    evidence_bytes = evidence_path.read_bytes()
    evidence_document = json.loads(evidence_bytes)
    assert canonical.canonicalize(evidence_document) == evidence_bytes
    admitted = governance_module.admit_governance_acceptance_evidence(
        evidence_bytes
    )
    assert (
        admitted.workflow.path,
        admitted.workflow.repository,
        admitted.workflow.ref,
        admitted.workflow.sha,
        admitted.target_sha,
        admitted.package_coordinate,
        admitted.confirmation_digest,
        admitted.environment,
    ) == (
        WORKFLOW_RELATIVE_PATH,
        "hcoona/three",
        "refs/heads/main",
        "a" * 40,
        ZERO_SHA,
        BASE_COORDINATE,
        CONFIRMATION_DIGEST,
        ENVIRONMENT,
    )
    assert tuple(
        (result.job, result.result) for result in admitted.dependency_results
    ) == (
        ("validate-fixed-inputs", "failure"),
        ("acceptance-review", "skipped"),
        ("probe-absent-create-readback", "skipped"),
        ("probe-exact-and-conflict", "skipped"),
    )
    assert (admitted.reviewer, admitted.reviewer_source) == (
        None,
        "unavailable-in-job-context",
    )
    assert (
        admitted.workflow_run_id,
        admitted.run_attempt,
        admitted.recovery.workflow_run_id,
        admitted.recovery.deployment,
        admitted.recovery.job,
    ) == (
        424242,
        1,
        424242,
        "run:424242/environment:acceptance",
        "acceptance-review",
    )
    assert admitted.recovery.artifact_id is None
    assert admitted.mutation_classification == "incomplete"
    assert tuple(
        (
            fact.result,
            fact.record_digest,
            fact.artifact_id,
            fact.artifact_digest,
            fact.scenarios,
        )
        for fact in admitted.probe_facts
    ) == (
        ("incomplete", None, None, None, ()),
        ("incomplete", None, None, None, ()),
    )
    assert admitted.to_document() == evidence_document
    assert admitted.evidence_digest == canonical.canonical_sha256(
        evidence_document
    )


@pytest.mark.parametrize("upstream_status", [200, 201])
@pytest.mark.parametrize(
    "review_binding",
    [
        pytest.param(
            ("700", 700, "complete"),
            id="review-artifact-bound",
        ),
        pytest.param(
            ("", None, "incomplete"),
            id="review-artifact-missing",
        ),
    ],
)
def test_retry_5_terminal_program_executes_finalized_evidence(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    upstream_status: int,
    review_binding: tuple[str, int | None, str],
) -> None:
    review_artifact_output, expected_artifact_id, expected_classification = (
        review_binding
    )
    document, _raw = _load_workflow()
    _install_test_only_finalized_profile(monkeypatch)
    absent, conflict = _retry_5_complete_terminal_suites(
        upstream_status=upstream_status
    )
    overrides = _complete_terminal_overrides(absent, conflict)
    overrides.update(
        {
            "GITHUB_RUN_ID": "515151",
            "REVIEW_ARTIFACT_ID": review_artifact_output,
        }
    )

    evidence_path = _execute_terminal_in_process(
        document,
        tmp_path,
        monkeypatch,
        overrides,
    )

    evidence_bytes = evidence_path.read_bytes()
    evidence_document = json.loads(evidence_bytes)
    assert canonical.canonicalize(evidence_document) == evidence_bytes
    admitted = governance_module.admit_governance_acceptance_evidence(
        evidence_bytes
    )
    admitted_document = admitted.to_document()
    probe_facts = cast(
        "list[dict[str, Any]]",
        admitted_document["probe-facts"],
    )
    assert (
        admitted.target_sha,
        admitted.workflow.sha,
        admitted.recovery.artifact_id,
        admitted.mutation_classification,
        admitted.workflow_run_id,
        admitted.run_attempt,
        admitted.recovery.workflow_run_id,
        admitted.recovery.deployment,
        admitted.recovery.job,
    ) == (
        TEST_ONLY_NONZERO_TARGET_SHA,
        "e" * 40,
        expected_artifact_id,
        expected_classification,
        515151,
        1,
        515151,
        "run:515151/environment:acceptance",
        "acceptance-review",
    )
    assert tuple(
        (
            fact["result"],
            fact["record-digest"],
            fact["artifact-id"],
            fact["artifact-digest"],
            tuple(
                scenario["scenario"]
                for scenario in cast(
                    "list[dict[str, Any]]",
                    fact["scenarios"],
                )
            ),
        )
        for fact in probe_facts
    ) == (
        (
            "success",
            absent.to_document()["record-digest"],
            701,
            "sha256:" + ("a" * 64),
            ("absent-create-readback",),
        ),
        (
            "success",
            conflict.to_document()["record-digest"],
            702,
            "sha256:" + ("b" * 64),
            ("exact", "identical-race", "differing-race", "lost-response"),
        ),
    )
    assert probe_facts[0]["scenarios"] == absent.to_document()["scenarios"]
    assert probe_facts[1]["scenarios"] == conflict.to_document()["scenarios"]
    absent_scenario = cast(
        "list[dict[str, Any]]",
        probe_facts[0]["scenarios"],
    )[0]
    absent_scenario_proof = cast(
        "dict[str, Any]",
        absent_scenario["validated-request-proof"],
    )
    assert absent_scenario["runner-diagnostic"] == {
        "exit-classification": "protocol-confirmed",
        "upstream-status": upstream_status,
        "exception-category": None,
        "request-correlation-digest": absent_scenario_proof["request-digest"],
    }
    assert tuple(
        scenario["validated-request-proof"]["upstream-status"]
        for fact in probe_facts
        for scenario in cast(
            "list[dict[str, Any]]",
            fact["scenarios"],
        )
        if "validated-request-proof" in scenario
    ) == (upstream_status, upstream_status)


@pytest.mark.parametrize("failed_probe", ["ABSENT", "CONFLICT"])
@pytest.mark.parametrize("job_result", ["failure", "cancelled"])
def test_retry_5_terminal_program_writes_unknown_evidence_for_probe_failure(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    failed_probe: str,
    job_result: str,
) -> None:
    document, _raw = _load_workflow()
    _install_test_only_finalized_profile(monkeypatch)
    absent, conflict = _retry_5_complete_terminal_suites(upstream_status=200)
    overrides = _complete_terminal_overrides(absent, conflict)
    overrides[f"{failed_probe}_JOB_RESULT"] = job_result
    prefixes_to_clear = [failed_probe]
    if failed_probe == "ABSENT":
        overrides["CONFLICT_JOB_RESULT"] = "skipped"
        prefixes_to_clear.append("CONFLICT")
    for prefix in prefixes_to_clear:
        for suffix in (
            "RESULT",
            "MUTATION_CLASSIFICATION",
            "SCENARIO_INVENTORY",
            "RECORD_JSON",
            "RECORD_DIGEST",
            "ARTIFACT_ID",
            "ARTIFACT_DIGEST",
        ):
            overrides[f"{prefix}_{suffix}"] = ""
    overrides[f"{failed_probe}_RESULT"] = "unknown"
    overrides[f"{failed_probe}_MUTATION_CLASSIFICATION"] = "unknown"

    evidence_path = _execute_terminal_in_process(
        document,
        tmp_path,
        monkeypatch,
        overrides,
    )

    admitted = governance_module.admit_governance_acceptance_evidence(
        evidence_path.read_bytes()
    )
    dependency_results = {
        dependency.job: dependency.result
        for dependency in admitted.dependency_results
    }
    probe_facts = {fact.probe: fact for fact in admitted.probe_facts}
    failed_job = (
        "probe-absent-create-readback"
        if failed_probe == "ABSENT"
        else "probe-exact-and-conflict"
    )
    failed_fact = probe_facts[failed_job]
    assert dependency_results[failed_job] == job_result
    assert admitted.mutation_classification == "unknown"
    assert failed_fact.result == "incomplete"
    assert all(
        value is None
        for value in (
            failed_fact.record_digest,
            failed_fact.artifact_id,
            failed_fact.artifact_digest,
        )
    )


@pytest.mark.parametrize("prefix", ["ABSENT", "CONFLICT"])
@pytest.mark.parametrize(
    "job_result",
    ["success", "failure", "cancelled", "skipped"],
)
@pytest.mark.parametrize(
    "classifier_outputs",
    [
        (result, classification)
        for result in ("", "success", "incomplete", "unknown")
        for classification in ("", "complete", "incomplete", "unknown")
    ],
)
def test_retry_5_terminal_no_record_classifier_gate_is_exact(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    prefix: str,
    job_result: str,
    classifier_outputs: tuple[str, str],
) -> None:
    document, _raw = _load_workflow()
    _install_test_only_finalized_profile(monkeypatch)
    absent, conflict = _retry_5_complete_terminal_suites(upstream_status=200)
    overrides = _complete_terminal_overrides(absent, conflict)
    output_suffixes = (
        "RESULT",
        "MUTATION_CLASSIFICATION",
        "SCENARIO_INVENTORY",
        "RECORD_JSON",
        "RECORD_DIGEST",
        "ARTIFACT_ID",
        "ARTIFACT_DIGEST",
    )
    overrides[f"{prefix}_JOB_RESULT"] = job_result
    for suffix in output_suffixes:
        overrides[f"{prefix}_{suffix}"] = ""
    (
        overrides[f"{prefix}_RESULT"],
        overrides[f"{prefix}_MUTATION_CLASSIFICATION"],
    ) = classifier_outputs
    if prefix == "ABSENT" and job_result != "success":
        overrides["CONFLICT_JOB_RESULT"] = "skipped"
        for suffix in output_suffixes:
            overrides[f"CONFLICT_{suffix}"] = ""

    allowed = classifier_outputs == ("", "") or (
        classifier_outputs == ("unknown", "unknown")
        and job_result in {"failure", "cancelled"}
    )
    evidence_path = tmp_path / ".wdv3" / "retry-5-evidence.json"
    if not allowed:
        with pytest.raises(
            ValueError,
            match=rf"{prefix} outputs exist without a suite record",
        ):
            _execute_terminal_in_process(
                document,
                tmp_path,
                monkeypatch,
                overrides,
            )
        assert not evidence_path.exists()
        return

    evidence_path = _execute_terminal_in_process(
        document,
        tmp_path,
        monkeypatch,
        overrides,
    )
    admitted = governance_module.admit_governance_acceptance_evidence(
        evidence_path.read_bytes()
    )
    probe = (
        "probe-absent-create-readback"
        if prefix == "ABSENT"
        else "probe-exact-and-conflict"
    )
    fact = next(fact for fact in admitted.probe_facts if fact.probe == probe)
    expected_classification = (
        "unknown" if job_result in {"failure", "cancelled"} else "incomplete"
    )
    assert admitted.mutation_classification == expected_classification
    assert (
        fact.result,
        fact.record_digest,
        fact.artifact_id,
        fact.artifact_digest,
        fact.scenarios,
    ) == ("incomplete", None, None, None, ())


@pytest.mark.parametrize("suite_classification", ["incomplete", "unknown"])
@pytest.mark.parametrize("job_result", ["failure", "cancelled"])
def test_retry_5_terminal_program_retains_noncomplete_canonical_suite(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    suite_classification: str,
    job_result: str,
) -> None:
    document, _raw = _load_workflow()
    _install_test_only_finalized_profile(monkeypatch)
    absent, complete_conflict = _retry_5_complete_terminal_suites(
        upstream_status=200
    )
    conflict = _retry_5_noncomplete_conflict_suite(
        complete_conflict,
        classification=suite_classification,
    )
    overrides = _complete_terminal_overrides(absent, conflict)
    overrides["CONFLICT_JOB_RESULT"] = job_result

    evidence_path = _execute_terminal_in_process(
        document,
        tmp_path,
        monkeypatch,
        overrides,
    )

    admitted = governance_module.admit_governance_acceptance_evidence(
        evidence_path.read_bytes()
    )
    fact = admitted.probe_facts[1]
    assert admitted.mutation_classification == "unknown"
    assert fact.result == suite_classification
    assert (
        fact.record_digest,
        fact.artifact_id,
        fact.artifact_digest,
    ) == (
        conflict.to_document()["record-digest"],
        702,
        "sha256:" + ("b" * 64),
    )
    assert fact.scenarios == tuple(
        cast("list[dict[str, Any]]", conflict.to_document()["scenarios"])
    )


@pytest.mark.parametrize(
    "missing_output",
    ["CONFLICT_ARTIFACT_ID", "CONFLICT_ARTIFACT_DIGEST"],
)
def test_retry_5_terminal_program_retains_suite_when_artifact_binding_is_missing(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    missing_output: str,
) -> None:
    document, _raw = _load_workflow()
    _install_test_only_finalized_profile(monkeypatch)
    absent, conflict = _retry_5_complete_terminal_suites(upstream_status=200)
    overrides = _complete_terminal_overrides(absent, conflict)
    overrides.update(
        {
            "CONFLICT_JOB_RESULT": "failure",
            "CONFLICT_RESULT": "unknown",
            "CONFLICT_MUTATION_CLASSIFICATION": "unknown",
            missing_output: "",
        }
    )

    evidence_path = _execute_terminal_in_process(
        document,
        tmp_path,
        monkeypatch,
        overrides,
    )

    evidence_bytes = evidence_path.read_bytes()
    admitted = governance_module.admit_governance_acceptance_evidence(
        evidence_bytes
    )
    fact = admitted.probe_facts[1]
    conflict_scenarios = cast(
        "list[dict[str, Any]]",
        conflict.to_document()["scenarios"],
    )
    assert admitted.mutation_classification == "unknown"
    assert fact.result == "incomplete"
    assert fact.record_digest == conflict.to_document()["record-digest"]
    assert fact.artifact_id is None
    assert fact.artifact_digest is None
    assert (
        tuple(scenario["scenario"] for scenario in fact.scenarios)
        == conflict.scenario_inventory
    )
    assert fact.scenarios == tuple(conflict_scenarios)


@pytest.mark.parametrize(
    "overrides",
    [
        pytest.param({"ABSENT_RECORD_JSON": "{}"}, id="skipped-record"),
        pytest.param(
            {"ABSENT_ARTIFACT_ID": "17"},
            id="orphaned-artifact-id",
        ),
        pytest.param(
            {"ABSENT_RECORD_DIGEST": "sha256:" + ("b" * 64)},
            id="orphaned-record-digest",
        ),
        pytest.param({"ABSENT_RESULT": "success"}, id="orphaned-result"),
        pytest.param(
            {"ABSENT_SCENARIO_INVENTORY": ('["absent-create-readback"]')},
            id="orphaned-inventory",
        ),
    ],
)
def test_retry_5_terminal_program_fails_closed_on_optional_evidence(
    tmp_path: Path,
    overrides: dict[str, str],
) -> None:
    document, _raw = _load_workflow()

    completed, evidence_path = _execute_terminal(
        document,
        tmp_path,
        overrides,
    )

    assert completed.returncode != 0
    assert completed.stdout == ""
    assert "ValueError" in completed.stderr
    assert not evidence_path.exists()


@pytest.mark.parametrize(
    ("output_name", "output_value", "error_match"),
    [
        pytest.param(
            "ABSENT_RECORD_JSON",
            "{",
            "Expecting property name",
            id="malformed-json",
        ),
        pytest.param(
            "ABSENT_RECORD_JSON",
            "[]",
            "ABSENT suite record is not an object",
            id="non-object-record",
        ),
        pytest.param(
            "ABSENT_ARTIFACT_DIGEST",
            "not-a-digest",
            "ABSENT artifact digest is not an exact sha256 digest",
            id="malformed-artifact-digest",
        ),
        pytest.param(
            "ABSENT_SCENARIO_INVENTORY",
            '["exact"]',
            "ABSENT output scenario inventory is not exact",
            id="contradictory-output-inventory",
        ),
        pytest.param(
            "ABSENT_RECORD_DIGEST",
            "sha256:" + ("0" * 64),
            "ABSENT suite record digest output is not exact",
            id="contradictory-record-digest-output",
        ),
        pytest.param(
            "ABSENT_RESULT",
            "unknown",
            "ABSENT suite result and mutation classification outputs mismatch",
            id="result-classification-output-mismatch",
        ),
    ],
)
def test_retry_5_terminal_program_reaches_specific_record_validation(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    output_name: str,
    output_value: str,
    error_match: str,
) -> None:
    document, _raw = _load_workflow()
    _install_test_only_finalized_profile(monkeypatch)
    absent, conflict = _retry_5_complete_terminal_suites(upstream_status=200)
    overrides = _complete_terminal_overrides(absent, conflict)
    overrides[output_name] = output_value

    with pytest.raises(ValueError, match=error_match):
        _execute_terminal_in_process(
            document,
            tmp_path,
            monkeypatch,
            overrides,
        )
    assert not (tmp_path / ".wdv3" / "retry-5-evidence.json").exists()


def test_retry_5_actions_toolchains_and_closed_fixed_suite_route_are_exact() -> (
    None
):
    document, raw = _load_workflow()
    jobs = _jobs(document)
    uses_by_job = {
        name: [
            cast("str", step["uses"]) for step in _steps(job) if "uses" in step
        ]
        for name, job in jobs.items()
    }

    assert uses_by_job == {
        "validate-fixed-inputs": [],
        "acceptance-review": [UPLOAD],
        "probe-absent-create-readback": [
            CHECKOUT,
            SETUP_UV,
            SETUP_NODE,
            UPLOAD,
        ],
        "probe-exact-and-conflict": [
            CHECKOUT,
            SETUP_UV,
            SETUP_NODE,
            UPLOAD,
        ],
        "capture-governance-evidence": [CHECKOUT, SETUP_UV, UPLOAD],
    }
    assert {use for uses in uses_by_job.values() for use in uses} == ACTION_PINS
    assert all(
        re.fullmatch(r"[^@\s]+@[0-9a-f]{40}", use)
        for uses in uses_by_job.values()
        for use in uses
    )
    for job_name in PROBE_JOBS:
        job = jobs[job_name]
        assert _step(job, "Check out reviewed target")["with"] == {
            "ref": "${{ inputs.target_sha }}",
            "persist-credentials": False,
        }
        assert _step(job, "Install uv")["with"] == {
            "version": "0.12.5",
            "github-token": "",
        }
        assert _step(job, "Install exact Node.js")["with"] == {
            "node-version": "24.19.0"
        }
        assert _run(_step(job, "Install and verify exact npm toolchain")) == (
            "npm install --global npm@11.17.0\n"
            'test "$(node --version)" = "v24.19.0"\n'
            'test "$(npm --version)" = "11.17.0"\n'
        )
    assert _step(
        jobs["capture-governance-evidence"],
        "Check out immutable workflow source",
    )["with"] == {
        "ref": "${{ github.workflow_sha }}",
        "persist-credentials": False,
    }
    assert _step(
        jobs["capture-governance-evidence"],
        "Install uv",
    )["with"] == {"version": "0.12.5", "github-token": ""}

    probe_commands = tuple(
        _run(
            next(
                step for step in _steps(jobs[name]) if step.get("id") == "probe"
            )
        )
        for name in PROBE_JOBS
    )
    assert tuple(
        re.findall(r"--suite ([a-z-]+)", command)[0]
        for command in probe_commands
    ) == ("absent-create-readback", "exact-and-conflict")
    assert all(
        command.count(
            "three-workflow-delivery-v3 governance run-fixed-acceptance-probe"
        )
        == 1
        for command in probe_commands
    )
    assert re.findall(
        r"(?m)^\s*three-workflow-delivery-v3\s+([a-z][a-z0-9-]*)\b",
        raw,
    ) == ["governance", "governance"]
    dispatch_inputs = _triggers(document)["workflow_dispatch"]["inputs"]
    assert set(dispatch_inputs).isdisjoint(
        {
            "suite",
            "scenario",
            "tag",
            "ref",
            "channel",
            "release_unit",
            "live",
            "bypass",
            "force",
        }
    )
    for forbidden in (
        "--tag",
        "--scenario",
        "--channel",
        "--release-unit",
        "--bypass",
        "--force",
        "live_enabled",
    ):
        assert forbidden not in raw


def test_retry_5_probe_outputs_steps_and_completion_wiring_are_exact() -> None:
    jobs = _jobs(_load_workflow()[0])
    expected_outputs = {
        "result": "${{ steps.classify.outputs.result }}",
        "mutation-classification": (
            "${{ steps.classify.outputs.mutation-classification }}"
        ),
        "scenario-inventory": (
            "${{ steps.classify.outputs.scenario-inventory }}"
        ),
        "record-digest": "${{ steps.classify.outputs.record-digest }}",
        "record-json": "${{ steps.classify.outputs.record-json }}",
        "artifact-id": "${{ steps.upload.outputs.artifact-id }}",
        "artifact-digest": "${{ steps.upload.outputs.artifact-digest }}",
    }
    expected_steps = {
        "probe-absent-create-readback": (
            "Check out reviewed target",
            "Install uv",
            "Install exact Node.js",
            "Install and verify exact npm toolchain",
            "Run fixed absent/create/readback suite",
            "Upload immutable absent/create/readback suite",
            "Classify absent/create/readback completion",
            "Require complete absent/create/readback evidence",
        ),
        "probe-exact-and-conflict": (
            "Check out reviewed target",
            "Install uv",
            "Install exact Node.js",
            "Install and verify exact npm toolchain",
            "Run fixed exact and conflict suite",
            "Upload immutable exact/conflict suite",
            "Classify exact/conflict completion",
            "Require complete exact/conflict evidence",
        ),
    }
    expected_commands = {
        "probe-absent-create-readback": (
            "uv run --python 3.13 --package three-workflow-delivery-v3 \\\n"
            "  three-workflow-delivery-v3 governance "
            "run-fixed-acceptance-probe \\\n"
            "  --suite absent-create-readback \\\n"
            '  --package-coordinate "${INPUT_PACKAGE_COORDINATE}" \\\n'
            '  --target-sha "${INPUT_TARGET_SHA}" \\\n'
            "  --timeout-seconds 120 \\\n"
            '  --output ".wdv3/probe-absent-r${GITHUB_RUN_ID}-'
            'ra${GITHUB_RUN_ATTEMPT}.json" \\\n'
            '  --github-output "${GITHUB_OUTPUT}"\n'
        ),
        "probe-exact-and-conflict": (
            "uv run --python 3.13 --package three-workflow-delivery-v3 \\\n"
            "  three-workflow-delivery-v3 governance "
            "run-fixed-acceptance-probe \\\n"
            "  --suite exact-and-conflict \\\n"
            '  --package-coordinate "${INPUT_PACKAGE_COORDINATE}" \\\n'
            '  --target-sha "${INPUT_TARGET_SHA}" \\\n'
            "  --timeout-seconds 300 \\\n"
            '  --output ".wdv3/probe-conflict-r${GITHUB_RUN_ID}-'
            'ra${GITHUB_RUN_ATTEMPT}.json" \\\n'
            '  --github-output "${GITHUB_OUTPUT}"\n'
        ),
    }

    for job_name in PROBE_JOBS:
        job = jobs[job_name]
        steps = _steps(job)
        probe = next(step for step in steps if step.get("id") == "probe")
        classify = next(step for step in steps if step.get("id") == "classify")
        require = steps[-1]

        assert job["outputs"] == expected_outputs
        assert tuple(step["name"] for step in steps) == expected_steps[job_name]
        assert probe["env"] == {
            "WDV3_ACCEPTANCE_GITHUB_TOKEN": "${{ github.token }}",
            "INPUT_PACKAGE_COORDINATE": "${{ inputs.package_coordinate }}",
            "INPUT_TARGET_SHA": "${{ inputs.target_sha }}",
        }
        assert _run(probe) == expected_commands[job_name]
        assert classify["env"] == {
            "PROBE_OUTCOME": "${{ steps.probe.outcome }}",
            "PROBE_RESULT": "${{ steps.probe.outputs.result }}",
            "PROBE_MUTATION_CLASSIFICATION": (
                "${{ steps.probe.outputs.mutation-classification }}"
            ),
            "PROBE_SCENARIO_INVENTORY": (
                "${{ steps.probe.outputs.scenario-inventory }}"
            ),
            "PROBE_RECORD_DIGEST": ("${{ steps.probe.outputs.record-digest }}"),
            "PROBE_RECORD_JSON": "${{ steps.probe.outputs.record-json }}",
            "UPLOAD_OUTCOME": "${{ steps.upload.outcome }}",
            "UPLOAD_ARTIFACT_ID": "${{ steps.upload.outputs.artifact-id }}",
            "UPLOAD_ARTIFACT_DIGEST": (
                "${{ steps.upload.outputs.artifact-digest }}"
            ),
        }
        assert require["env"] == {
            "MUTATION_CLASSIFICATION": (
                "${{ steps.classify.outputs.mutation-classification }}"
            )
        }
        assert [step.get("id") for step in steps[-4:]] == [
            "probe",
            "upload",
            "classify",
            None,
        ]

    assert jobs["acceptance-review"]["outputs"] == {
        "artifact-id": "${{ steps.upload.outputs.artifact-id }}"
    }


_RETRY_5_SECRET_CONTEXT_REFERENCE = re.compile(
    r"\bsecrets\s*(?:\.|\[\s*['\"])",
    re.IGNORECASE,
)


def _retry_5_secret_context_references(workflow_text: str) -> tuple[str, ...]:
    return tuple(
        match.group(0)
        for match in _RETRY_5_SECRET_CONTEXT_REFERENCE.finditer(workflow_text)
    )


def _assert_retry_5_workflow_has_no_secret_context_reference(
    workflow_text: str,
) -> None:
    references = _retry_5_secret_context_references(workflow_text)
    assert references == (), (
        f"retry-5 workflow must not reference the secrets context: {references}"
    )


@pytest.mark.parametrize(
    ("forbidden_reference", "detected_prefix"),
    [
        pytest.param(
            "${{ secrets.PAT }}",
            "secrets.",
            id="dotted-context",
        ),
        pytest.param(
            "${{ secrets['PAT'] }}",
            "secrets['",
            id="single-quoted-bracket-context",
        ),
        pytest.param(
            '${{ secrets["PAT"] }}',
            'secrets["',
            id="double-quoted-bracket-context",
        ),
    ],
)
def test_retry_5_secret_context_guard_rejects_dotted_and_bracket_references(
    forbidden_reference: str,
    detected_prefix: str,
) -> None:
    _document, raw = _load_workflow()
    built_in_context_reference = "${{ github.token }}"

    assert raw.count(built_in_context_reference) == len(PROBE_JOBS)
    _assert_retry_5_workflow_has_no_secret_context_reference(raw)

    mutated = raw.replace(
        built_in_context_reference,
        forbidden_reference,
        1,
    )

    assert mutated != raw
    assert _retry_5_secret_context_references(mutated) == (detected_prefix,)
    with pytest.raises(
        AssertionError,
        match="must not reference the secrets context",
    ):
        _assert_retry_5_workflow_has_no_secret_context_reference(mutated)
