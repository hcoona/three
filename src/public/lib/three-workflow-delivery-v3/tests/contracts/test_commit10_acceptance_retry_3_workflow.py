"""Compact contract for the temporary retry-3 acceptance workflow."""

# ruff: noqa: D103, PLR2004, S102, S603

from __future__ import annotations

import ast
import hashlib
import json
import os
import re
import subprocess
import sys
from pathlib import Path
from typing import Any, cast

import pytest
import yaml
from three_workflow_delivery_v3.adapters.github_packages import (
    AcceptanceRunnerDiagnostic,
    FixedAcceptanceSuiteResult,
    FixedCoordinateAcceptanceProbeResult,
    ValidatedAcceptanceRequestProof,
)
from three_workflow_delivery_v3.canonical import canonicalize

REPO_ROOT = Path(__file__).resolve().parents[6]
WORKFLOW_RELATIVE_PATH = (
    ".github/workflows/workflow-delivery-v3-buddy-smoke-acceptance-retry-3.yml"
)
WORKFLOW_PATH = REPO_ROOT / WORKFLOW_RELATIVE_PATH
LLD_PATH = (
    REPO_ROOT / "docs/wiki/analyses/workflow-delivery/v3/"
    "hcoona-release-smoke-npm-lld.md"
)
ENVIRONMENT = "workflow-delivery-v3-buddy-smoke-acceptance-retry-3"
ZERO_SHA = "0" * 40
FINALIZED_TARGET_SHA = "a61f9a4e44458bfd7bc7bfd96f6db848ce047c0c"
COORDINATE = "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.9"
CONFIRMATION = "I_ACCEPT_DISPOSABLE_GITHUB_PACKAGES_PROBES_RETRY_3"
CONFIRMATION_DIGEST = (
    "sha256:33e59948941f5f1111d5017ab80dd33c90dd2ac8d1a17203e7f7382a8c5b2c72"
)
JOBS = {
    "validate-fixed-inputs",
    "acceptance-review",
    "probe-absent-create-readback",
    "probe-exact-and-conflict",
    "capture-governance-evidence",
}
PINS = {
    "actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1",
    "astral-sh/setup-uv@20cfd1bf945f4377ade1205e4dbc17946fc9a30d",
    "actions/setup-node@820762786026740c76f36085b0efc47a31fe5020",
    "actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a",
}


def _document() -> dict[str, Any]:
    value = yaml.safe_load(WORKFLOW_PATH.read_text(encoding="utf-8"))
    assert isinstance(value, dict)
    return value


def _triggers(document: dict[str, Any]) -> dict[str, Any]:
    value = document.get("on")
    if value is None:
        value = cast("dict[object, Any]", document).get(True)
    assert isinstance(value, dict)
    return value


def _steps(job: dict[str, Any]) -> list[dict[str, Any]]:
    value = job["steps"]
    assert isinstance(value, list)
    return value


def _step(job: dict[str, Any], name: str) -> dict[str, Any]:
    matches = [step for step in _steps(job) if step.get("name") == name]
    assert len(matches) == 1
    return matches[0]


def _terminal_python() -> str:
    run = _step(
        _document()["jobs"]["capture-governance-evidence"],
        "Form and admit terminal Governance evidence",
    )["run"]
    assert isinstance(run, str)
    marker = "python - <<'PY'\n"
    assert run.count(marker) == 1
    script, terminator = run.split(marker, 1)[1].rsplit("\nPY", 1)
    assert not terminator.strip()
    return script


def test_retry_3_dispatch_and_profile_literals_are_exact() -> None:
    document = _document()
    triggers = _triggers(document)
    inputs = triggers["workflow_dispatch"]["inputs"]

    assert set(triggers) == {"workflow_dispatch"}
    assert inputs == {
        "target_sha": {
            "description": "Reviewed protected-finalization target SHA",
            "required": True,
            "default": FINALIZED_TARGET_SHA,
            "type": "string",
        },
        "package_coordinate": {
            "description": "Fixed disposable GitHub Packages base coordinate",
            "required": True,
            "default": COORDINATE,
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
        "WDV3_ACCEPTANCE_TARGET_SHA": FINALIZED_TARGET_SHA,
        "WDV3_ACCEPTANCE_PACKAGE_COORDINATE": COORDINATE,
        "WDV3_ACCEPTANCE_CONFIRMATION": CONFIRMATION,
        "WDV3_ACCEPTANCE_REF": "refs/heads/main",
        "WDV3_PURPOSE": "destination-acceptance",
    }
    terminal = _terminal_python()
    assert WORKFLOW_RELATIVE_PATH in terminal
    assert terminal.count(ENVIRONMENT) >= 3
    assert (
        "sha256:" + hashlib.sha256(CONFIRMATION.encode()).hexdigest()
        == CONFIRMATION_DIGEST
    )
    assert '"confirmation-digest": "sha256:" + hashlib.sha256(' in terminal
    assert (
        '("exact", "identical-race", "differing-race", "lost-response")'
        in terminal
    )


def test_retry_3_has_exact_five_job_first_attempt_dag() -> None:
    jobs = _document()["jobs"]

    assert set(jobs) == JOBS
    assert jobs["validate-fixed-inputs"].get("needs") is None
    assert jobs["acceptance-review"]["needs"] == "validate-fixed-inputs"
    assert jobs["probe-absent-create-readback"]["needs"] == "acceptance-review"
    assert (
        jobs["probe-exact-and-conflict"]["needs"]
        == "probe-absent-create-readback"
    )
    assert set(jobs["capture-governance-evidence"]["needs"]) == JOBS - {
        "capture-governance-evidence"
    }
    assert {name: job["if"] for name, job in jobs.items()} == {
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
    assert {name: job.get("environment") for name, job in jobs.items()} == {
        "validate-fixed-inputs": None,
        "acceptance-review": ENVIRONMENT,
        "probe-absent-create-readback": None,
        "probe-exact-and-conflict": None,
        "capture-governance-evidence": None,
    }


def test_retry_3_permissions_limit_packages_write_to_probe_jobs() -> None:
    document = _document()
    jobs = document["jobs"]
    writers = {
        name
        for name, job in jobs.items()
        if job.get("permissions", {}).get("packages") == "write"
    }

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
    assert writers == {
        "probe-absent-create-readback",
        "probe-exact-and-conflict",
    }
    assert all(
        permission in {"read", "write"}
        for job in jobs.values()
        for permission in job.get("permissions", {}).values()
    )


def test_retry_3_toolchain_and_action_revisions_are_fully_pinned() -> None:
    document = _document()
    lld = LLD_PATH.read_text(encoding="utf-8")
    uses_by_job = {
        name: [step["uses"] for step in _steps(job) if "uses" in step]
        for name, job in document["jobs"].items()
    }
    uses = [use for job_uses in uses_by_job.values() for use in job_uses]
    runs = "\n".join(
        step.get("run", "")
        for job in document["jobs"].values()
        for step in _steps(job)
    )

    checkout, uv, node, upload = (
        "actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1",
        "astral-sh/setup-uv@20cfd1bf945f4377ade1205e4dbc17946fc9a30d",
        "actions/setup-node@820762786026740c76f36085b0efc47a31fe5020",
        "actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a",
    )
    assert uses_by_job == {
        "validate-fixed-inputs": [],
        "acceptance-review": [upload],
        "probe-absent-create-readback": [checkout, uv, node, upload],
        "probe-exact-and-conflict": [checkout, uv, node, upload],
        "capture-governance-evidence": [checkout, uv, upload],
    }
    assert set(uses) == PINS
    assert all(re.fullmatch(r"[^@]+@[0-9a-f]{40}", use) for use in uses)
    assert runs.count("npm install --global npm@11.17.0") == 2
    assert runs.count('test "$(node --version)" = "v24.19.0"') == 2
    assert runs.count('test "$(npm --version)" = "11.17.0"') == 2
    node_steps = [
        step
        for job in document["jobs"].values()
        for step in _steps(job)
        if step.get("uses", "").startswith("actions/setup-node@")
    ]
    assert [step["with"]["node-version"] for step in node_steps] == [
        "24.19.0",
        "24.19.0",
    ]
    assert (
        "Retry 3 therefore installs and verifies Node 24.19.0 and npm 11.17.0"
        in lld
    )
    assert (
        "the original capture remains historical\n"
        "replay evidence rather than current execution authority" in lld
    )


def test_retry_3_concurrency_and_checkouts_are_exact() -> None:
    document = _document()
    jobs = document["jobs"]

    assert document["concurrency"] == {
        "group": (
            "hcoona-release-smoke-npm-workflow-delivery-v3-"
            "buddy-smoke-acceptance-retry-3"
        ),
        "cancel-in-progress": False,
    }
    for job_name in (
        "probe-absent-create-readback",
        "probe-exact-and-conflict",
    ):
        checkout = _step(jobs[job_name], "Check out reviewed target")
        assert checkout["with"] == {
            "ref": "${{ inputs.target_sha }}",
            "persist-credentials": False,
        }
    terminal_checkout = _step(
        jobs["capture-governance-evidence"],
        "Check out immutable workflow source",
    )
    assert terminal_checkout["with"] == {
        "ref": "${{ github.workflow_sha }}",
        "persist-credentials": False,
        "token": "${{ github.token }}",
    }


def test_retry_3_probe_and_terminal_evidence_wiring_are_exact() -> None:
    jobs = _document()["jobs"]
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
    expected_probe_steps = {
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
    expected_probe_commands = {
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
    authorized_mutation_steps = []
    for job_name, expected_steps in expected_probe_steps.items():
        job = jobs[job_name]
        steps = _steps(job)
        tail = steps[-4:]
        probe = tail[0]
        assert job["outputs"] == expected_outputs
        assert tuple(step["name"] for step in steps) == expected_steps
        assert [step.get("id") for step in tail] == [
            "probe",
            "upload",
            "classify",
            None,
        ]
        assert [step.get("if") for step in tail] == [
            None,
            "${{ always() }}",
            "${{ always() }}",
            "${{ always() }}",
        ]
        assert probe["env"] == {
            "WDV3_ACCEPTANCE_GITHUB_TOKEN": "${{ github.token }}",
            "INPUT_PACKAGE_COORDINATE": "${{ inputs.package_coordinate }}",
            "INPUT_TARGET_SHA": "${{ inputs.target_sha }}",
        }
        assert probe["run"] == expected_probe_commands[job_name]
        authorized_mutation_steps.append(probe)
    assert [
        step
        for job in jobs.values()
        for step in _steps(job)
        if "WDV3_ACCEPTANCE_GITHUB_TOKEN" in step.get("env", {})
    ] == authorized_mutation_steps

    terminal_steps = _steps(jobs["capture-governance-evidence"])
    assert [step["name"] for step in terminal_steps] == [
        "Check out immutable workflow source",
        "Install uv",
        "Form and admit terminal Governance evidence",
        "Upload immutable Governance acceptance evidence",
    ]
    assert terminal_steps[2]["env"] == {
        "INPUT_TARGET_SHA": "${{ inputs.target_sha }}",
        "INPUT_PACKAGE_COORDINATE": "${{ inputs.package_coordinate }}",
        "INPUT_CONFIRM": "${{ inputs.confirm }}",
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
    terminal_upload = terminal_steps[3]
    terminal_shell_path = (
        ".wdv3/governance-acceptance-r${GITHUB_RUN_ID}-"
        "ra${GITHUB_RUN_ATTEMPT}.json"
    )
    terminal_upload_path = (
        ".wdv3/governance-acceptance-r${{ github.run_id }}-"
        "ra${{ github.run_attempt }}.json"
    )
    assert (
        f'export WDV3_FILE="{terminal_shell_path}"' in terminal_steps[2]["run"]
    )
    assert terminal_upload["if"] == "${{ always() }}"
    assert terminal_upload["uses"] == (
        "actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a"
    )
    assert terminal_upload["with"] == {
        "name": (
            "wdv3-governance-acceptance-r${{ github.run_id }}-"
            "ra${{ github.run_attempt }}"
        ),
        "path": terminal_upload_path,
        "if-no-files-found": "error",
        "include-hidden-files": True,
        "retention-days": 45,
        "overwrite": False,
        "archive": False,
    }


def test_retry_3_terminal_capture_is_always_and_reconstructs_diagnostics() -> (
    None
):
    terminal = _document()["jobs"]["capture-governance-evidence"]
    script = _terminal_python()
    tree = ast.parse(script)
    calls = [node for node in ast.walk(tree) if isinstance(node, ast.Call)]
    probe_result_calls = [
        call
        for call in calls
        if isinstance(call.func, ast.Name)
        and call.func.id == "FixedCoordinateAcceptanceProbeResult"
    ]
    diagnostic_calls = [
        call
        for call in calls
        if isinstance(call.func, ast.Name)
        and call.func.id == "AcceptanceRunnerDiagnostic"
    ]

    assert terminal["if"] == "${{ always() && github.run_attempt == 1 }}"
    assert set(terminal["needs"]) == JOBS - {"capture-governance-evidence"}
    assert len(probe_result_calls) == 1
    runner_keyword = next(
        keyword
        for keyword in probe_result_calls[0].keywords
        if keyword.arg == "runner_diagnostic"
    )
    assert ast.unparse(runner_keyword.value) == (
        "runner_diagnostic_or_none(scenario)"
    )
    assert len(diagnostic_calls) == 1
    assert {keyword.arg for keyword in diagnostic_calls[0].keywords} == {
        "exit_classification",
        "upstream_status",
        "exception_category",
        "request_correlation_digest",
    }
    assert 'record_digest = suite.to_document()["record-digest"]' in script
    assert "record_digest != asserted_digest" in script
    assert 'record_digest != record_json.get("record-digest")' in script


def _run_fixed_input_validation(
    **overrides: str,
) -> subprocess.CompletedProcess[str]:
    document = _document()
    validation = _step(
        document["jobs"]["validate-fixed-inputs"],
        "Fail closed before review or mutation",
    )
    target = FINALIZED_TARGET_SHA
    environment: dict[str, str] = {
        **os.environ,
        "INPUT_TARGET_SHA": target,
        "INPUT_PACKAGE_COORDINATE": COORDINATE,
        "INPUT_CONFIRM": CONFIRMATION,
        "WDV3_ACCEPTANCE_TARGET_SHA": target,
        "WDV3_ACCEPTANCE_PACKAGE_COORDINATE": COORDINATE,
        "WDV3_ACCEPTANCE_CONFIRMATION": CONFIRMATION,
        "WDV3_ACCEPTANCE_REF": "refs/heads/main",
        "GITHUB_REF": "refs/heads/main",
        "GITHUB_RUN_ATTEMPT": "1",
        **overrides,
    }
    return subprocess.run(
        ["/usr/bin/bash", "-c", validation["run"]],
        check=False,
        capture_output=True,
        text=True,
        env=environment,
    )


def test_retry_3_fixed_input_validation_accepts_finalized_nonzero_target() -> (
    None
):
    result = _run_fixed_input_validation()

    assert result.returncode == 0
    assert result.stdout == ""
    assert result.stderr == ""


def test_retry_3_zero_sentinel_fails_before_review_or_mutation() -> None:
    result = _run_fixed_input_validation(
        INPUT_TARGET_SHA=ZERO_SHA,
        WDV3_ACCEPTANCE_TARGET_SHA=ZERO_SHA,
    )

    assert result.returncode == 1
    assert result.stdout == ""


@pytest.mark.parametrize(
    "overrides",
    [
        {"INPUT_TARGET_SHA": "e" * 40},
        {"INPUT_PACKAGE_COORDINATE": COORDINATE.replace(".9", ".10")},
        {"INPUT_CONFIRM": CONFIRMATION.removesuffix("_3") + "_2"},
        {"GITHUB_REF": "refs/heads/release"},
        {"GITHUB_RUN_ATTEMPT": "2"},
    ],
    ids=["target", "package", "confirmation", "ref", "run-attempt"],
)
def test_retry_3_fixed_input_validation_rejects_each_wrong_input(
    overrides: dict[str, str],
) -> None:
    result = _run_fixed_input_validation(**overrides)

    assert result.returncode == 1
    assert result.stdout == ""


def test_retry_3_fixed_input_guards_have_exact_fail_closed_order() -> None:
    jobs = _document()["jobs"]
    run = _step(
        jobs["validate-fixed-inputs"],
        "Fail closed before review or mutation",
    )["run"]

    assert re.findall(r"if \[\[ (.+) \]\]; then", run) == [
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
    assert "environment" not in jobs["validate-fixed-inputs"]
    assert jobs["acceptance-review"]["needs"] == "validate-fixed-inputs"
    assert jobs["probe-absent-create-readback"]["needs"] == "acceptance-review"


def test_retry_3_terminal_reconstructs_proof_and_runner_diagnostic(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    proof = ValidatedAcceptanceRequestProof.from_validated_exchange(
        raw_request=b'{"name":"retry-3-proof-bound"}',
        tarball=b"retry-3-proof-bound-tarball",
        package_coordinate=COORDINATE,
        tag="wdv3-acceptance-9",
        upstream_status=201,
        selected_headers={"Content-Type": "application/json", "ETag": '"r3"'},
        response_body=b'{"created":true}',
    )
    diagnostic = AcceptanceRunnerDiagnostic(
        exit_classification="protocol-confirmed",
        upstream_status=201,
        exception_category=None,
        request_correlation_digest=proof.request_digest,
    )
    absent_suite = FixedAcceptanceSuiteResult(
        suite="absent-create-readback",
        scenarios=(
            FixedCoordinateAcceptanceProbeResult(
                scenario="absent-create-readback",
                package_coordinate=COORDINATE,
                tag="wdv3-acceptance-9",
                pre_state="absent",
                post_state="exact",
                result="protocol-confirmed",
                mutation_classification="complete",
                action_executed=True,
                mutation_started=True,
                response_identity_digest=proof.response_identity_digest,
                content_sha512=proof.tarball_sha512,
                diagnostics=("protocol-confirmed",),
                validated_request_proof=proof,
                runner_diagnostic=diagnostic,
            ),
        ),
    )
    conflict_suite = FixedAcceptanceSuiteResult(
        suite="exact-and-conflict",
        scenarios=tuple(
            FixedCoordinateAcceptanceProbeResult(
                scenario=scenario,
                package_coordinate=coordinate,
                tag=tag,
                pre_state="exact" if scenario == "exact" else "absent",
                post_state="conflicting"
                if scenario == "differing-race"
                else "exact",
                result=result,
                mutation_classification="complete",
                action_executed=scenario != "exact",
                mutation_started=scenario != "exact",
                response_identity_digest="sha256:" + digit * 64,
                content_sha512="sha512:" + digit * 128,
                diagnostics=(),
            )
            for scenario, coordinate, tag, result, digit in (
                (
                    "exact",
                    COORDINATE,
                    "wdv3-acceptance-9",
                    "exact-no-mutation",
                    "2",
                ),
                (
                    "identical-race",
                    COORDINATE.replace(".9", ".10"),
                    "wdv3-acceptance-10",
                    "identical-race-exact",
                    "3",
                ),
                (
                    "differing-race",
                    COORDINATE.replace(".9", ".11"),
                    "wdv3-acceptance-11",
                    "differing-race-conflict",
                    "4",
                ),
                (
                    "lost-response",
                    COORDINATE.replace(".9", ".12"),
                    "wdv3-acceptance-12",
                    "lost-response-exact-after-start",
                    "5",
                ),
            )
        ),
    )
    absent_record = absent_suite.to_document()
    conflict_record = conflict_suite.to_document()
    evidence_path = tmp_path / "governance-complete.json"

    class _Admitted:
        def __init__(self, content: bytes) -> None:
            self._document = json.loads(content)

        def to_document(self) -> dict[str, Any]:
            return self._document

    from three_workflow_delivery_v3.records import governance  # noqa: PLC0415

    monkeypatch.setattr(
        governance,
        "admit_governance_acceptance_evidence",
        _Admitted,
    )
    environment = {
        "INPUT_TARGET_SHA": ZERO_SHA,
        "WDV3_ACCEPTANCE_TARGET_SHA": ZERO_SHA,
        "WDV3_ACCEPTANCE_PACKAGE_COORDINATE": COORDINATE,
        "WDV3_ACCEPTANCE_CONFIRMATION": CONFIRMATION,
        "VALIDATE_RESULT": "success",
        "REVIEW_RESULT": "success",
        "ABSENT_JOB_RESULT": "success",
        "CONFLICT_JOB_RESULT": "success",
        "REVIEW_ARTIFACT_ID": "701",
        "ABSENT_RESULT": "success",
        "ABSENT_MUTATION_CLASSIFICATION": "complete",
        "ABSENT_SCENARIO_INVENTORY": '["absent-create-readback"]',
        "ABSENT_RECORD_JSON": json.dumps(absent_record),
        "ABSENT_RECORD_DIGEST": cast("str", absent_record["record-digest"]),
        "ABSENT_ARTIFACT_ID": "702",
        "ABSENT_ARTIFACT_DIGEST": "sha256:" + "6" * 64,
        "CONFLICT_RESULT": "success",
        "CONFLICT_MUTATION_CLASSIFICATION": "complete",
        "CONFLICT_SCENARIO_INVENTORY": (
            '["exact","identical-race","differing-race","lost-response"]'
        ),
        "CONFLICT_RECORD_JSON": json.dumps(conflict_record),
        "CONFLICT_RECORD_DIGEST": cast("str", conflict_record["record-digest"]),
        "CONFLICT_ARTIFACT_ID": "703",
        "CONFLICT_ARTIFACT_DIGEST": "sha256:" + "7" * 64,
        "GITHUB_REPOSITORY": "hcoona/three",
        "GITHUB_REF": "refs/heads/main",
        "GITHUB_WORKFLOW_SHA": "a" * 40,
        "GITHUB_RUN_ID": "303",
        "GITHUB_RUN_ATTEMPT": "1",
        "WDV3_FILE": str(evidence_path),
    }
    for name, value in environment.items():
        monkeypatch.setenv(name, value)

    exec(compile(_terminal_python(), WORKFLOW_RELATIVE_PATH, "exec"), {})

    raw = evidence_path.read_bytes()
    evidence = json.loads(raw)
    absent_fact = evidence["probe-facts"][0]
    reconstructed = absent_fact["scenarios"][0]
    assert raw == canonicalize(evidence)
    assert evidence["mutation-classification"] == "complete"
    assert [fact["result"] for fact in evidence["probe-facts"]] == [
        "success",
        "success",
    ]
    assert absent_fact["record-digest"] == absent_record["record-digest"]
    assert absent_fact["record-digest"] == environment["ABSENT_RECORD_DIGEST"]
    assert reconstructed["mutation-classification"] == "complete"
    assert reconstructed["response"]["result"] == "protocol-confirmed"
    assert reconstructed["validated-request-proof"] == proof.to_document()
    assert reconstructed["runner-diagnostic"] == {
        "exit-classification": "protocol-confirmed",
        "upstream-status": 201,
        "exception-category": None,
        "request-correlation-digest": proof.request_digest,
    }


def test_retry_3_terminal_script_emits_canonical_rejected_dispatch(
    tmp_path: Path,
) -> None:
    evidence_path = tmp_path / "governance.json"
    environment = {
        **os.environ,
        "INPUT_TARGET_SHA": ZERO_SHA,
        "WDV3_ACCEPTANCE_TARGET_SHA": ZERO_SHA,
        "WDV3_ACCEPTANCE_PACKAGE_COORDINATE": COORDINATE,
        "WDV3_ACCEPTANCE_CONFIRMATION": CONFIRMATION,
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
        "GITHUB_REPOSITORY": "hcoona/three",
        "GITHUB_REF": "refs/heads/main",
        "GITHUB_WORKFLOW_SHA": "a" * 40,
        "GITHUB_RUN_ID": "303",
        "GITHUB_RUN_ATTEMPT": "1",
        "WDV3_FILE": str(evidence_path),
    }

    result = subprocess.run(
        [sys.executable, "-c", _terminal_python()],
        cwd=REPO_ROOT,
        check=False,
        capture_output=True,
        text=True,
        env=environment,
    )

    assert result.returncode == 0, result.stderr
    raw = evidence_path.read_bytes()
    document = json.loads(raw)
    assert raw == canonicalize(document)
    assert document["target-sha"] == ZERO_SHA
    assert document["mutation-classification"] == "incomplete"
    assert [item["result"] for item in document["dependency-results"]] == [
        "failure",
        "skipped",
        "skipped",
        "skipped",
    ]
    assert all(fact["scenarios"] == [] for fact in document["probe-facts"])


def test_retry_3_is_owned_and_contains_no_live_or_release_route() -> None:
    raw = WORKFLOW_PATH.read_text(encoding="utf-8")
    codeowners = (REPO_ROOT / ".github/CODEOWNERS").read_text(encoding="utf-8")

    assert f"/{WORKFLOW_RELATIVE_PATH} @hcoona" in codeowners.splitlines()
    assert re.search(r"\b(?:Live|Release)\b", raw) is None
    assert "workflow_call:" not in raw
    assert "schedule:" not in raw
    assert "push:" not in raw
