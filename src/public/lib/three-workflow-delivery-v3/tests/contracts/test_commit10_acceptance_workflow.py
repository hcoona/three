"""Commit-10 contracts for the temporary destination-acceptance workflow."""

# ruff: noqa: D103, E501, PLR2004, S102

from __future__ import annotations

import hashlib
import json
import os
import re
import subprocess
from pathlib import Path
from typing import Any, cast

import pytest
import yaml
from three_workflow_delivery_v3.adapters.github_packages import (
    FixedAcceptanceSuiteResult,
    FixedCoordinateAcceptanceProbeResult,
    ValidatedAcceptanceRequestProof,
)
from three_workflow_delivery_v3.canonical import parse_canonical_json
from three_workflow_delivery_v3.records.governance import (
    admit_governance_acceptance_evidence,
)

REPO_ROOT = Path(__file__).resolve().parents[6]
WORKFLOW = (
    REPO_ROOT
    / ".github/workflows/workflow-delivery-v3-buddy-smoke-acceptance.yml"
)
ZERO_SHA = "0" * 40
FINALIZED_TARGET_SHA = "5a84bebd05407e1859fe76f400dcb4f4cbcd002e"
EXPECTED_JOBS = (
    "validate-fixed-inputs",
    "acceptance-review",
    "probe-absent-create-readback",
    "probe-exact-and-conflict",
    "capture-governance-evidence",
)
PROBE_JOBS = EXPECTED_JOBS[2:4]
INPUT_EXPRESSION = re.compile(r"\$\{\{\s*inputs\.[^}]+\}\}")
ACTION_PIN = re.compile(r"^[0-9a-f]{40}$")
EXPECTED_ACTION_USES = {
    "actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1",
    "actions/setup-node@820762786026740c76f36085b0efc47a31fe5020",
    "actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a",
    "astral-sh/setup-uv@20cfd1bf945f4377ade1205e4dbc17946fc9a30d",
}
CREDENTIAL_VALUE = re.compile(
    r"(?i)(\$\{\{\s*(github\.token|secrets\.[^}]+)\s*\}\}|"
    r"\b(NODE_AUTH_TOKEN|NPM_TOKEN|GH_TOKEN|GITHUB_TOKEN)\b|"
    r"_authToken\s*=|authorization:|bearer\s+)"
)
CREDENTIAL_KEYS = {
    "auth-token",
    "authorization",
    "gh-token",
    "github-token",
    "github_token",
    "node-auth-token",
    "node_auth_token",
    "npm-token",
    "npm_token",
    "token",
    "wdv3_acceptance_github_token",
    "_authtoken",
}


def _document() -> dict[str, Any]:
    document = yaml.safe_load(WORKFLOW.read_text(encoding="utf-8"))
    assert isinstance(document, dict)
    return document


def _trigger(document: dict[str, Any]) -> dict[str, Any]:
    trigger = document.get("on")
    if trigger is None:
        trigger = cast("dict[Any, Any]", document).get(True)
    assert isinstance(trigger, dict)
    return trigger


def _jobs() -> dict[str, dict[str, Any]]:
    jobs = _document()["jobs"]
    assert isinstance(jobs, dict)
    return jobs


def _steps(job: dict[str, Any]) -> list[dict[str, Any]]:
    steps = job["steps"]
    assert isinstance(steps, list)
    return steps


def _runs(job: dict[str, Any]) -> tuple[str, ...]:
    return tuple(str(step["run"]) for step in _steps(job) if "run" in step)


def _non_comment_command(command: str) -> str:
    return "\n".join(
        line
        for line in command.splitlines()
        if not line.lstrip().startswith("#")
    )


def _assert_no_input_expression_in_run(job: dict[str, Any]) -> None:
    for command in _runs(job):
        assert INPUT_EXPRESSION.search(command) is None


def test_trigger_inputs_topology_and_fixed_sentinel_are_exact() -> None:
    document = _document()
    trigger = _trigger(document)
    dispatch = trigger["workflow_dispatch"]

    assert set(trigger) == {"workflow_dispatch"}
    assert set(dispatch["inputs"]) == {
        "target_sha",
        "package_coordinate",
        "confirm",
    }
    assert all(
        value["required"] is True for value in dispatch["inputs"].values()
    )
    assert tuple(_jobs()) == EXPECTED_JOBS
    assert document["permissions"] == {}
    assert document["env"]["WDV3_ACCEPTANCE_TARGET_SHA"] == FINALIZED_TARGET_SHA
    assert document["env"]["WDV3_ACCEPTANCE_PACKAGE_COORDINATE"].endswith(
        "wdv3-acceptance.1"
    )


def test_validation_fails_closed_before_review_or_mutation() -> None:
    jobs = _jobs()
    validation = "\n".join(_runs(jobs["validate-fixed-inputs"]))

    assert jobs["acceptance-review"]["needs"] == "validate-fixed-inputs"
    assert jobs["probe-absent-create-readback"]["needs"] == "acceptance-review"
    assert jobs["probe-exact-and-conflict"]["needs"] == (
        "probe-absent-create-readback"
    )
    assert validation.count("exit 1") >= 7
    assert ZERO_SHA in validation
    assert "INPUT_TARGET_SHA" in validation
    assert "INPUT_PACKAGE_COORDINATE" in validation
    assert "INPUT_CONFIRM" in validation


def test_every_probe_is_first_attempt_only_and_only_probe_jobs_write_packages() -> (
    None
):
    jobs = _jobs()

    for probe in PROBE_JOBS:
        condition = str(jobs[probe]["if"])
        assert "github.run_attempt == 1" in condition
        if probe == "probe-exact-and-conflict":
            assert (
                "needs.probe-absent-create-readback.result == 'success'"
                in condition
            )
        assert jobs[probe]["permissions"] == {
            "contents": "read",
            "packages": "write",
        }
    assert jobs["acceptance-review"]["if"] == "${{ github.run_attempt == 1 }}"
    assert jobs["capture-governance-evidence"]["if"] == (
        "${{ always() && github.run_attempt == 1 }}"
    )
    assert all(
        job in PROBE_JOBS or "packages" not in details.get("permissions", {})
        for job, details in jobs.items()
    )


def test_workflow_executes_exact_fixed_five_scenario_inventory() -> None:
    jobs = _jobs()
    absent = "\n".join(_runs(jobs["probe-absent-create-readback"]))
    conflict = "\n".join(_runs(jobs["probe-exact-and-conflict"]))
    capture = "\n".join(_runs(jobs["capture-governance-evidence"]))

    assert "--suite absent-create-readback" in absent
    assert "--suite exact-and-conflict" in conflict
    for scenario in (
        "absent-create-readback",
        "exact",
        "identical-race",
        "differing-race",
        "lost-response",
    ):
        assert scenario in absent + conflict + capture
    assert "--scenario" not in absent + conflict


def test_probe_outputs_bind_canonical_records_and_immutable_artifacts() -> None:
    jobs = _jobs()

    for probe in PROBE_JOBS:
        outputs = jobs[probe]["outputs"]
        assert set(outputs) == {
            "result",
            "mutation-classification",
            "scenario-inventory",
            "record-digest",
            "record-json",
            "artifact-id",
            "artifact-digest",
        }
        assert "steps.upload.outputs.artifact-id" in outputs["artifact-id"]
        assert (
            "steps.upload.outputs.artifact-digest" in outputs["artifact-digest"]
        )
        upload = next(
            step for step in _steps(jobs[probe]) if step.get("id") == "upload"
        )
        assert upload["with"]["archive"] is False
        assert upload["with"]["overwrite"] is False


def test_terminal_capture_checks_out_immutable_source_before_uv() -> None:
    steps = _steps(_jobs()["capture-governance-evidence"])

    assert steps[0]["uses"].startswith("actions/checkout@")
    assert steps[0]["with"] == {
        "ref": "${{ github.workflow_sha }}",
        "persist-credentials": False,
        "token": "${{ github.token }}",
    }
    assert steps[1]["uses"].startswith("astral-sh/setup-uv@")
    command = str(steps[2]["run"])
    assert "record-digest" in command
    assert "artifact-digest" in command
    assert "scenario-inventory" in command
    assert "unknown" in command
    assert "incomplete" in command
    assert "complete" in command
    assert 'ranks = {"complete": 0, "incomplete": 1, "unknown": 2}' in command
    assert '"release-lineage": "none"' in command


def test_acceptance_workflow_credentials_only_enter_probe_command_env() -> None:
    allowed = {
        (
            "probe-absent-create-readback",
            "Run fixed absent/create/readback suite",
            "env.WDV3_ACCEPTANCE_GITHUB_TOKEN",
        ),
        (
            "probe-exact-and-conflict",
            "Run fixed exact and conflict suite",
            "env.WDV3_ACCEPTANCE_GITHUB_TOKEN",
        ),
    }
    credential_locations = []
    jobs = _jobs()
    for job_name in PROBE_JOBS:
        job = jobs[job_name]
        for step in _steps(job):
            uses = str(step.get("uses", ""))
            with_values = cast("dict[str, object]", step.get("with", {}))
            if uses.startswith("actions/checkout@"):
                assert with_values.get("persist-credentials") is False
            for scope, values in (
                ("env", cast("dict[str, object]", step.get("env", {}))),
                ("with", with_values),
            ):
                for key, value in values.items():
                    location = (job_name, step["name"], f"{scope}.{key}")
                    normalized_key = key.casefold().replace("-", "_")
                    rendered_value = "" if value is None else str(value)
                    if rendered_value and (
                        normalized_key in CREDENTIAL_KEYS
                        or CREDENTIAL_VALUE.search(rendered_value) is not None
                    ):
                        credential_locations.append(location)
            command = str(step.get("run", ""))
            if CREDENTIAL_VALUE.search(command) is not None:
                credential_locations.append((job_name, step["name"], "run"))

    assert set(credential_locations) == allowed


def test_terminal_capture_emits_evidence_when_record_exists_but_artifact_outputs_missing() -> (
    None
):
    command = "\n".join(_runs(_jobs()["capture-governance-evidence"]))

    assert "record-json" in command
    assert "artifact-id" in command
    assert "artifact-digest" in command
    assert "record present" in command or "record_json" in command
    assert "artifact outputs missing" in command
    assert "probe_result = max(" in command
    assert '"incomplete"' in command
    assert "emit_evidence" in command or "write_evidence" in command


@pytest.mark.parametrize(
    "terminal_result",
    ["failure", "cancelled", "skipped"],
)
@pytest.mark.parametrize("probe", PROBE_JOBS)
def test_terminal_capture_classifies_probe_dependency_startedness_exactly(
    probe: str,
    terminal_result: str,
) -> None:
    command = "\n".join(_runs(_jobs()["capture-governance-evidence"]))

    assert f"needs.{probe}.result == '{terminal_result}'" in command
    if terminal_result in {"failure", "cancelled"}:
        assert "possibly-started" in command
        assert 'classification = "unknown"' in command
    else:
        assert "not-started" in command
        assert '"incomplete"' in command


def test_terminal_capture_recomputes_record_digest_from_canonical_suite() -> (
    None
):
    command = "\n".join(_runs(_jobs()["capture-governance-evidence"]))

    assert "FixedAcceptanceSuiteResult" in command
    assert "canonical_sha256" in command
    assert "ValidatedAcceptanceRequestProof.from_closed_document" in command
    assert "validated-request-proof" in command
    assert "validated_request_proof=" in command
    assert 'record_digest = suite.to_document()["record-digest"]' in command
    assert "needs.probe-absent-create-readback.outputs.record-digest" in command
    assert "needs.probe-exact-and-conflict.outputs.record-digest" in command


def test_failed_later_probe_preserves_validated_suite_facts() -> None:
    command = "\n".join(_runs(_jobs()["capture-governance-evidence"]))

    failure_branch = command.split('job_result in {"failure", "cancelled"}', 1)[
        1
    ].split('elif job_result == "skipped":', 1)[0]
    assert "record_json is None" in failure_branch
    assert 'probe_result = "unknown"' in failure_branch
    assert "record_digest = None" not in failure_branch
    assert "scenarios = []" not in failure_branch


def test_untrusted_inputs_never_interpolate_directly_into_run_scripts() -> None:
    jobs = _jobs()

    for job in jobs.values():
        _assert_no_input_expression_in_run(job)
    commands = "\n".join(
        command for job in jobs.values() for command in _runs(job)
    )
    assert "--github-token" not in commands
    assert "WDV3_ACCEPTANCE_GITHUB_TOKEN: ${{ github.token }}" in (
        WORKFLOW.read_text(encoding="utf-8")
    )


@pytest.mark.parametrize(
    "unsafe",
    [
        'echo "${{ inputs.target_sha }}"',
        'printf "%s" "${{ inputs.package_coordinate }}"',
        "value=${{ inputs.confirm }}",
    ],
)
def test_negative_static_guard_detects_direct_input_interpolation(
    unsafe: str,
) -> None:
    job = {"steps": [{"run": unsafe}]}

    with pytest.raises(AssertionError):
        _assert_no_input_expression_in_run(job)


def test_all_actions_are_full_sha_pinned() -> None:
    for job in _jobs().values():
        for step in _steps(job):
            uses = step.get("uses")
            if uses is None:
                continue
            _action, separator, revision = str(uses).partition("@")
            assert separator == "@"
            assert ACTION_PIN.fullmatch(revision)


def test_acceptance_workflow_never_activates_live_or_creates_release_lineage() -> (
    None
):
    source = WORKFLOW.read_text(encoding="utf-8").lower()

    assert "live_enabled: true" not in source
    assert "id-token: write" not in source
    assert "receipt" not in source
    assert "release attempt" not in source
    assert '"release-lineage": "none"' in source


def test_acceptance_target_is_bound_to_reviewed_implementation_merge() -> None:
    document = _document()

    assert document["env"]["WDV3_ACCEPTANCE_TARGET_SHA"] == FINALIZED_TARGET_SHA
    assert (
        _trigger(document)["workflow_dispatch"]["inputs"]["target_sha"][
            "default"
        ]
        == FINALIZED_TARGET_SHA
    )


@pytest.mark.parametrize(
    ("field", "expected"),
    [
        ("target_sha", FINALIZED_TARGET_SHA),
        (
            "package_coordinate",
            "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.1",
        ),
        ("confirm", "I_ACCEPT_DISPOSABLE_GITHUB_PACKAGES_PROBES"),
        ("workflow_ref", "refs/heads/main"),
        ("workflow_path", "workflow-delivery-v3-buddy-smoke-acceptance.yml"),
        ("run_attempt", "${{ github.run_attempt == 1 }}"),
    ],
)
def test_acceptance_dispatch_inputs_and_constants_are_exact(
    field: str,
    expected: str,
) -> None:
    document = _document()
    source = WORKFLOW.read_text(encoding="utf-8")

    assert expected in source
    if field in {"target_sha", "package_coordinate", "confirm"}:
        dispatch_input = _trigger(document)["workflow_dispatch"]["inputs"][
            field
        ]
        assert dispatch_input["required"] is True


@pytest.mark.parametrize("job", EXPECTED_JOBS)
def test_acceptance_dag_environment_and_concurrency_are_exact(job: str) -> None:
    document = _document()
    details = _jobs()[job]

    assert document["concurrency"]["group"].endswith(
        "workflow-delivery-v3-buddy-smoke-acceptance"
    )
    if job == "acceptance-review":
        assert details["environment"] == (
            "workflow-delivery-v3-buddy-smoke-acceptance"
        )
    else:
        assert "environment" not in details


@pytest.mark.parametrize("job", EXPECTED_JOBS)
def test_acceptance_permissions_keep_package_write_only_in_probe_jobs(
    job: str,
) -> None:
    permissions = _jobs()[job].get("permissions", {})

    assert ("packages" in permissions) is (job in PROBE_JOBS)
    if job in PROBE_JOBS:
        assert permissions["packages"] == "write"
        assert permissions["contents"] == "read"


@pytest.mark.parametrize(
    "required",
    [
        "actions/checkout@",
        "actions/setup-node@",
        "astral-sh/setup-uv@",
        "actions/upload-artifact@",
    ],
)
def test_acceptance_action_pins_and_evidence_retention_are_exact(
    required: str,
) -> None:
    document = _document()
    source = WORKFLOW.read_text(encoding="utf-8")

    assert required in source
    assert "retention-days: 45" in source
    assert {
        str(step["uses"])
        for job in document["jobs"].values()
        for step in job.get("steps", [])
        if "uses" in step
    } == EXPECTED_ACTION_USES
    assert all(
        ACTION_PIN.fullmatch(str(step["uses"]).partition("@")[2])
        for job in document["jobs"].values()
        for step in job.get("steps", [])
        if "uses" in step
    )


@pytest.mark.parametrize("job", EXPECTED_JOBS)
def test_each_validation_review_and_probe_job_independently_rejects_reruns(
    job: str,
) -> None:
    details = _jobs()[job]
    command = "\n".join(_runs(details))

    if job == "capture-governance-evidence":
        assert "always()" in details["if"]
    else:
        assert "github.run_attempt == 1" in details.get("if", command)
    assert "github.run_attempt" in details.get("if", command)


@pytest.mark.parametrize("probe", PROBE_JOBS)
def test_each_probe_has_the_exact_first_attempt_job_guard(probe: str) -> None:
    details = _jobs()[probe]

    condition = str(details["if"])
    assert "github.run_attempt == 1" in condition
    if probe == "probe-exact-and-conflict":
        assert (
            "needs.probe-absent-create-readback.result == 'success'"
            in condition
        )
    assert details["needs"] in {"acceptance-review", PROBE_JOBS[0]}


@pytest.mark.parametrize("dependency", EXPECTED_JOBS[:-1])
def test_terminal_capture_has_exact_always_guard_and_every_dependency(
    dependency: str,
) -> None:
    capture = _jobs()["capture-governance-evidence"]
    command = "\n".join(_runs(capture))

    assert capture["if"] == "${{ always() && github.run_attempt == 1 }}"
    assert dependency in capture["needs"]
    assert f"needs.{dependency}.result" in command


@pytest.mark.parametrize(
    "field",
    [
        '"login": None',
        "unavailable-in-job-context",
        "workflow-run-id",
        '"environment"',
        '"deployment"',
        '"artifact-id"',
    ],
)
def test_terminal_evidence_declares_reviewer_unavailable_and_recovery_coordinates(
    field: str,
) -> None:
    command = "\n".join(_runs(_jobs()["capture-governance-evidence"]))

    assert field in command
    assert "github.actor" not in command


@pytest.mark.parametrize(
    "section",
    [
        '"schema": "workflow-delivery/v3/governance-acceptance-evidence"',
        '"purpose": "destination-acceptance"',
        '"dependency-results"',
        '"probe-facts"',
        '"release-lineage": "none"',
    ],
)
def test_terminal_capture_payload_admits_full_closed_governance_evidence_contract(
    section: str,
) -> None:
    command = "\n".join(_runs(_jobs()["capture-governance-evidence"]))

    assert section in command
    assert "admit_governance_acceptance_evidence" in command


@pytest.mark.parametrize(
    "surface",
    [
        "tests/contracts/test_commit10_acceptance_workflow.py",
        "tests/governance/test_commit10_acceptance_evidence.py",
        ".github/workflows/workflow-delivery-v3-buddy-smoke-acceptance.yml",
    ],
)
def test_commit10_surfaces_have_exact_codeowners_and_hk_inventory(
    surface: str,
) -> None:
    codeowners = REPO_ROOT / ".github/CODEOWNERS"
    hk = REPO_ROOT / "hk.pkl"

    assert surface in codeowners.read_text(encoding="utf-8")
    assert "hcoona" in codeowners.read_text(encoding="utf-8")
    assert "workflow-delivery-v3" in hk.read_text(encoding="utf-8")


def test_normal_buddy_remains_disabled_before_attempt_without_legacy_route() -> (
    None
):
    normal = (
        REPO_ROOT / ".github/workflows/workflow-delivery-v3-buddy-smoke.yml"
    ).read_text(encoding="utf-8")

    assert "live_enabled" in normal
    assert "workflow-delivery-v3-buddy-smoke-acceptance.yml" not in normal


def test_fail_closed_static_assertions_accept_required_exact_guard_conditions() -> (
    None
):
    validation = "\n".join(_runs(_jobs()["validate-fixed-inputs"]))

    assert validation.count("exit 1") >= 7
    assert ZERO_SHA in validation


def _terminal_suite(
    suite: str,
    scenarios: tuple[
        tuple[str, str, str, str, str, tuple[str, ...]],
        ...,
    ],
) -> FixedAcceptanceSuiteResult:
    proof = ValidatedAcceptanceRequestProof.from_validated_exchange(
        raw_request=b'{"_id":"@hcoona/hcoona-release-smoke-npm"}',
        tarball=b"terminal-suite-lost-response",
        package_coordinate=(
            "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.4"
        ),
        tag="wdv3-acceptance-4",
        upstream_status=201,
        selected_headers={"Content-Type": "application/json", "ETag": '"v1"'},
        response_body=b'{"ok":true}',
    )
    return FixedAcceptanceSuiteResult(
        suite=suite,
        scenarios=tuple(
            FixedCoordinateAcceptanceProbeResult(
                scenario=scenario,
                package_coordinate=(
                    "@hcoona/hcoona-release-smoke-npm@"
                    f"0.0.0-wdv3-acceptance.{index}"
                ),
                tag=f"wdv3-acceptance-{index}",
                pre_state=pre_state,
                post_state=post_state,
                result=result,
                mutation_classification=classification,
                action_executed=scenario != "exact",
                mutation_started=scenario != "exact",
                response_identity_digest=(
                    proof.response_identity_digest
                    if result == "lost-response-exact-after-start"
                    else "sha256:" + ("2" * 64)
                ),
                content_sha512="sha512:" + content_digit * 128,
                diagnostics=diagnostics,
                validated_request_proof=(
                    proof
                    if result == "lost-response-exact-after-start"
                    else None
                ),
            )
            for index, (
                scenario,
                pre_state,
                post_state,
                result,
                classification,
                diagnostics,
            ) in enumerate(scenarios, start=1)
            for content_digit in ("4" if post_state == "conflicting" else "3",)
        ),
    )


def _execute_terminal_capture(  # noqa: PLR0913
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    *,
    absent: FixedAcceptanceSuiteResult,
    conflict: FixedAcceptanceSuiteResult | None,
    conflict_job_result: str = "success",
    conflict_upload_bound: bool = True,
    review_artifact_id: str = "700",
) -> dict[str, Any]:
    absent_document = absent.to_document()
    conflict_document = conflict.to_document() if conflict is not None else None
    environment = {
        "WDV3_FILE": str(tmp_path / ".wdv3/evidence.json"),
        "VALIDATE_RESULT": "success",
        "REVIEW_RESULT": "success",
        "ABSENT_JOB_RESULT": "success",
        "CONFLICT_JOB_RESULT": conflict_job_result,
        "REVIEW_ARTIFACT_ID": review_artifact_id,
        "ABSENT_RESULT": absent.result,
        "ABSENT_MUTATION_CLASSIFICATION": absent.mutation_classification,
        "ABSENT_SCENARIO_INVENTORY": json.dumps(
            list(absent.scenario_inventory)
        ),
        "ABSENT_RECORD_JSON": json.dumps(absent_document),
        "ABSENT_RECORD_DIGEST": absent_document["record-digest"],
        "ABSENT_ARTIFACT_ID": "701",
        "ABSENT_ARTIFACT_DIGEST": "sha256:" + ("a" * 64),
        "CONFLICT_RESULT": (
            conflict.result
            if conflict is not None and conflict_upload_bound
            else "unknown"
            if conflict is not None
            else ""
        ),
        "CONFLICT_MUTATION_CLASSIFICATION": (
            conflict.mutation_classification
            if conflict is not None and conflict_upload_bound
            else "unknown"
            if conflict is not None
            else ""
        ),
        "CONFLICT_SCENARIO_INVENTORY": (
            json.dumps(list(conflict.scenario_inventory))
            if conflict is not None
            else ""
        ),
        "CONFLICT_RECORD_JSON": (
            json.dumps(conflict_document)
            if conflict_document is not None
            else ""
        ),
        "CONFLICT_RECORD_DIGEST": (
            conflict_document["record-digest"]
            if conflict_document is not None
            else ""
        ),
        "CONFLICT_ARTIFACT_ID": (
            "702" if conflict is not None and conflict_upload_bound else ""
        ),
        "CONFLICT_ARTIFACT_DIGEST": (
            "sha256:" + ("b" * 64)
            if conflict is not None and conflict_upload_bound
            else ""
        ),
        "GITHUB_REPOSITORY": "hcoona/three",
        "GITHUB_REF": "refs/heads/main",
        "GITHUB_WORKFLOW_SHA": "b" * 40,
        "GITHUB_RUN_ID": "101",
        "GITHUB_RUN_ATTEMPT": "1",
        "INPUT_TARGET_SHA": "a" * 40,
        "INPUT_PACKAGE_COORDINATE": (
            "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.1"
        ),
        "INPUT_CONFIRM": "I_ACCEPT_DISPOSABLE_GITHUB_PACKAGES_PROBES",
    }
    monkeypatch.chdir(tmp_path)
    (tmp_path / ".wdv3").mkdir()
    for name, value in environment.items():
        monkeypatch.setenv(name, str(value))
    command = _runs(_jobs()["capture-governance-evidence"])[0]
    script = command.split("<<'PY'\n", 1)[1].rsplit("\nPY", 1)[0]

    exec(compile(script, str(WORKFLOW), "exec"), {"__name__": "__main__"})

    return cast(
        "dict[str, Any]",
        parse_canonical_json((tmp_path / ".wdv3/evidence.json").read_bytes()),
    )


def _terminal_suites(
    *,
    lost_response: tuple[str, str, str, tuple[str, ...]] = (
        "exact",
        "lost-response-exact-after-start",
        "complete",
        ("mutation-started-and-readback-exact",),
    ),
) -> tuple[
    FixedAcceptanceSuiteResult,
    FixedAcceptanceSuiteResult,
]:
    (
        lost_response_post_state,
        lost_response_result,
        lost_response_classification,
        lost_response_diagnostics,
    ) = lost_response
    return (
        _terminal_suite(
            "absent-create-readback",
            (
                (
                    "absent-create-readback",
                    "absent",
                    "exact",
                    "created",
                    "complete",
                    (),
                ),
            ),
        ),
        _terminal_suite(
            "exact-and-conflict",
            (
                (
                    "exact",
                    "exact",
                    "exact",
                    "exact-no-mutation",
                    "complete",
                    (),
                ),
                (
                    "identical-race",
                    "absent",
                    "exact",
                    "identical-race-exact",
                    "complete",
                    ("identical-race-exact",),
                ),
                (
                    "differing-race",
                    "absent",
                    "conflicting",
                    "differing-race-conflict",
                    "complete",
                    ("conflicting-remote-bytes-or-tag",),
                ),
                (
                    "lost-response",
                    "absent",
                    lost_response_post_state,
                    lost_response_result,
                    lost_response_classification,
                    lost_response_diagnostics,
                ),
            ),
        ),
    )


@pytest.mark.parametrize("job_result", ["failure", "cancelled"])
def test_terminal_capture_retains_unknown_suite_after_late_job_end(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    job_result: str,
) -> None:
    absent, conflict = _terminal_suites(
        lost_response=(
            "unknown",
            "timeout",
            "unknown",
            (
                "mutation-may-have-started",
                "human-reconciliation-required",
            ),
        )
    )

    evidence = _execute_terminal_capture(
        tmp_path,
        monkeypatch,
        absent=absent,
        conflict=conflict,
        conflict_job_result=job_result,
    )

    fact = evidence["probe-facts"][1]
    assert evidence["mutation-classification"] == "unknown"
    assert fact["result"] == "unknown"
    assert fact["record-digest"] == conflict.to_document()["record-digest"]
    assert fact["artifact-id"] == 702
    assert fact["artifact-digest"] == "sha256:" + ("b" * 64)
    assert fact["scenarios"] == conflict.to_document()["scenarios"]
    assert fact["scenarios"][-1]["action"]["mutation-started"] is True
    assert fact["scenarios"][-1]["response"]["diagnostics"] == [
        "mutation-may-have-started",
        "human-reconciliation-required",
    ]


@pytest.mark.parametrize("job_result", ["failure", "cancelled"])
def test_terminal_capture_retains_complete_suite_after_late_job_end(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    job_result: str,
) -> None:
    absent, conflict = _terminal_suites()

    evidence = _execute_terminal_capture(
        tmp_path,
        monkeypatch,
        absent=absent,
        conflict=conflict,
        conflict_job_result=job_result,
    )

    fact = evidence["probe-facts"][1]
    assert evidence["mutation-classification"] == "unknown"
    assert fact["result"] == "success"
    assert fact["record-digest"] == conflict.to_document()["record-digest"]
    assert fact["artifact-id"] == 702
    assert fact["artifact-digest"] == "sha256:" + ("b" * 64)
    assert fact["scenarios"] == conflict.to_document()["scenarios"]


def test_terminal_capture_retains_complete_suite_when_upload_outputs_are_missing(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    absent, conflict = _terminal_suites()

    evidence = _execute_terminal_capture(
        tmp_path,
        monkeypatch,
        absent=absent,
        conflict=conflict,
        conflict_job_result="failure",
        conflict_upload_bound=False,
    )

    fact = evidence["probe-facts"][1]
    assert evidence["mutation-classification"] == "unknown"
    assert fact["result"] == "incomplete"
    assert fact["record-digest"] == conflict.to_document()["record-digest"]
    assert fact["artifact-id"] is None
    assert fact["artifact-digest"] is None
    assert fact["scenarios"] == conflict.to_document()["scenarios"]


@pytest.mark.parametrize("job_result", ["failure", "cancelled"])
def test_terminal_capture_uses_empty_unknown_fact_without_valid_record(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    job_result: str,
) -> None:
    absent, _ = _terminal_suites()

    evidence = _execute_terminal_capture(
        tmp_path,
        monkeypatch,
        absent=absent,
        conflict=None,
        conflict_job_result=job_result,
    )

    fact = evidence["probe-facts"][1]
    assert evidence["mutation-classification"] == "unknown"
    assert fact["result"] == "unknown"
    assert fact["record-digest"] is None
    assert fact["artifact-id"] is None
    assert fact["artifact-digest"] is None
    assert fact["scenarios"] == []


def test_confirmation_literal_is_exact_and_never_describes_workflow_as_inert() -> (
    None
):
    document = _document()
    expected = "I_ACCEPT_DISPOSABLE_GITHUB_PACKAGES_PROBES"
    confirm = _trigger(document)["workflow_dispatch"]["inputs"]["confirm"]
    validation = "\n".join(_runs(_jobs()["validate-fixed-inputs"]))

    assert confirm["default"] == expected
    assert document["env"]["WDV3_ACCEPTANCE_CONFIRMATION"] == expected
    assert "${INPUT_CONFIRM}" in validation
    assert "${WDV3_ACCEPTANCE_CONFIRMATION}" in validation
    assert "inert" not in confirm["default"].lower()


def test_only_acceptance_review_declares_the_protected_environment() -> None:
    jobs = _jobs()
    environment_jobs = tuple(
        name for name, details in jobs.items() if "environment" in details
    )

    assert environment_jobs == ("acceptance-review",)
    assert jobs["acceptance-review"]["environment"] == (
        "workflow-delivery-v3-buddy-smoke-acceptance"
    )
    assert all("environment" not in jobs[name] for name in PROBE_JOBS)
    assert "environment" not in jobs["capture-governance-evidence"]


def test_zero_sha_validation_failure_cannot_request_environment_review_from_terminal_job() -> (
    None
):
    jobs = _jobs()
    validation = "\n".join(_runs(jobs["validate-fixed-inputs"]))
    capture = jobs["capture-governance-evidence"]

    assert f'== "{ZERO_SHA}"' in validation
    assert "exit 1" in validation
    assert jobs["acceptance-review"]["needs"] == "validate-fixed-inputs"
    assert "validate-fixed-inputs" in capture["needs"]
    assert "environment" not in capture


def test_terminal_capture_executes_with_missing_review_artifact_as_incomplete_evidence(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    absent, conflict = _terminal_suites()
    evidence = _execute_terminal_capture(
        tmp_path,
        monkeypatch,
        absent=absent,
        conflict=conflict,
        review_artifact_id="",
    )
    assert evidence["mutation-classification"] == "incomplete"
    assert evidence["recovery"]["artifact-id"] is None
    assert (
        evidence["probe-facts"][0]["record-digest"]
        == (absent.to_document()["record-digest"])
    )
    assert evidence["probe-facts"][0]["artifact-id"] == 701
    assert evidence["probe-facts"][0]["artifact-digest"] == (
        "sha256:" + ("a" * 64)
    )
    assert (
        evidence["probe-facts"][1]["record-digest"]
        == (conflict.to_document()["record-digest"])
    )
    assert evidence["probe-facts"][1]["artifact-id"] == 702
    assert evidence["probe-facts"][1]["artifact-digest"] == (
        "sha256:" + ("b" * 64)
    )
    assert len(evidence["probe-facts"]) == 2


def test_terminal_reconstruction_uses_each_scenario_mutation_classification() -> (
    None
):
    command = "\n".join(_runs(_jobs()["capture-governance-evidence"]))

    assert (
        'mutation_classification=scenario["mutation-classification"]' in command
    )
    assert 'mutation_classification="complete"' not in command


def test_all_and_only_wdv3_acceptance_uploads_include_hidden_files() -> None:
    uploads = [
        (job_name, step)
        for job_name, job in _jobs().items()
        for step in _steps(job)
        if str(step.get("uses", "")).startswith("actions/upload-artifact@")
        and str(
            cast("dict[str, Any]", step.get("with", {})).get("path", "")
        ).startswith(".wdv3/")
    ]

    assert [(job_name, step["name"]) for job_name, step in uploads] == [
        (
            "acceptance-review",
            "Upload immutable acceptance review coordinates",
        ),
        (
            "probe-absent-create-readback",
            "Upload immutable absent/create/readback suite",
        ),
        (
            "probe-exact-and-conflict",
            "Upload immutable exact/conflict suite",
        ),
        (
            "capture-governance-evidence",
            "Upload immutable Governance acceptance evidence",
        ),
    ]
    assert len(uploads) == 4
    for job_name, step in uploads:
        upload_with = cast("dict[str, Any]", step["with"])
        assert upload_with["include-hidden-files"] is True, job_name
        assert str(upload_with["path"]).startswith(".wdv3/"), job_name


def _execute_rejected_dispatch_capture(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    *,
    target_sha: str,
    package_coordinate: str,
    confirm: str,
) -> tuple[dict[str, Any], bytes]:
    canonical_environment = cast("dict[str, str]", _document()["env"])
    evidence_path = tmp_path / ".wdv3/evidence.json"
    environment = {
        **canonical_environment,
        "WDV3_FILE": str(evidence_path),
        "VALIDATE_RESULT": "failure",
        "REVIEW_RESULT": "skipped",
        "ABSENT_JOB_RESULT": "skipped",
        "CONFLICT_JOB_RESULT": "skipped",
        "REVIEW_ARTIFACT_ID": "",
        "ABSENT_RESULT": "",
        "ABSENT_RECORD_JSON": "",
        "ABSENT_RECORD_DIGEST": "",
        "ABSENT_ARTIFACT_ID": "",
        "ABSENT_ARTIFACT_DIGEST": "",
        "CONFLICT_RESULT": "",
        "CONFLICT_RECORD_JSON": "",
        "CONFLICT_RECORD_DIGEST": "",
        "CONFLICT_ARTIFACT_ID": "",
        "CONFLICT_ARTIFACT_DIGEST": "",
        "GITHUB_REPOSITORY": "hcoona/three",
        "GITHUB_REF": "refs/heads/main",
        "GITHUB_WORKFLOW_SHA": "b" * 40,
        "GITHUB_RUN_ID": "202",
        "GITHUB_RUN_ATTEMPT": "1",
        "INPUT_TARGET_SHA": target_sha,
        "INPUT_PACKAGE_COORDINATE": package_coordinate,
        "INPUT_CONFIRM": confirm,
    }
    monkeypatch.chdir(tmp_path)
    evidence_path.parent.mkdir()
    for name, value in environment.items():
        monkeypatch.setenv(name, value)
    command = _runs(_jobs()["capture-governance-evidence"])[0]
    script = command.split("<<'PY'\n", 1)[1].rsplit("\nPY", 1)[0]

    exec(compile(script, str(WORKFLOW), "exec"), {"__name__": "__main__"})

    evidence_bytes = evidence_path.read_bytes()
    evidence = cast(
        "dict[str, Any]",
        parse_canonical_json(evidence_bytes),
    )
    return evidence, evidence_bytes


def _assert_rejected_dispatch_evidence(
    evidence: dict[str, Any],
    evidence_bytes: bytes,
    *,
    hostile_value: str,
) -> None:
    canonical_environment = cast("dict[str, str]", _document()["env"])
    admitted = admit_governance_acceptance_evidence(evidence_bytes)

    assert admitted.to_document() == evidence
    assert (
        evidence["target-sha"]
        == canonical_environment["WDV3_ACCEPTANCE_TARGET_SHA"]
    )
    assert (
        evidence["package-coordinate"]
        == canonical_environment["WDV3_ACCEPTANCE_PACKAGE_COORDINATE"]
    )
    assert (
        evidence["confirmation-digest"]
        == "sha256:"
        + hashlib.sha256(
            canonical_environment["WDV3_ACCEPTANCE_CONFIRMATION"].encode()
        ).hexdigest()
    )
    assert evidence["dependency-results"] == [
        {"job": "validate-fixed-inputs", "result": "failure"},
        {"job": "acceptance-review", "result": "skipped"},
        {"job": "probe-absent-create-readback", "result": "skipped"},
        {"job": "probe-exact-and-conflict", "result": "skipped"},
    ]
    assert evidence["mutation-classification"] == "incomplete"
    assert all(
        fact["result"] == "incomplete" for fact in evidence["probe-facts"]
    )
    assert all(fact["scenarios"] == [] for fact in evidence["probe-facts"])
    assert hostile_value.encode() not in evidence_bytes


def test_terminal_capture_rejects_hostile_bad_sha_with_admissible_evidence(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    hostile = 'not-a-sha\n"}],"probe-facts":[{"result":"success"}]'
    canonical_environment = cast("dict[str, str]", _document()["env"])

    evidence, evidence_bytes = _execute_rejected_dispatch_capture(
        tmp_path,
        monkeypatch,
        target_sha=hostile,
        package_coordinate=canonical_environment[
            "WDV3_ACCEPTANCE_PACKAGE_COORDINATE"
        ],
        confirm=canonical_environment["WDV3_ACCEPTANCE_CONFIRMATION"],
    )

    _assert_rejected_dispatch_evidence(
        evidence,
        evidence_bytes,
        hostile_value=hostile,
    )


def test_terminal_capture_rejects_hostile_bad_package_with_admissible_evidence(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    hostile = '@hostile/pkg@9.9.9\n","mutation-classification":"complete'
    canonical_environment = cast("dict[str, str]", _document()["env"])

    evidence, evidence_bytes = _execute_rejected_dispatch_capture(
        tmp_path,
        monkeypatch,
        target_sha=canonical_environment["WDV3_ACCEPTANCE_TARGET_SHA"],
        package_coordinate=hostile,
        confirm=canonical_environment["WDV3_ACCEPTANCE_CONFIRMATION"],
    )

    _assert_rejected_dispatch_evidence(
        evidence,
        evidence_bytes,
        hostile_value=hostile,
    )


def test_terminal_capture_rejects_hostile_bad_confirm_with_admissible_evidence(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    hostile = 'NO\nPY\n{"schema":"hostile-confirm"}'
    canonical_environment = cast("dict[str, str]", _document()["env"])

    evidence, evidence_bytes = _execute_rejected_dispatch_capture(
        tmp_path,
        monkeypatch,
        target_sha=canonical_environment["WDV3_ACCEPTANCE_TARGET_SHA"],
        package_coordinate=canonical_environment[
            "WDV3_ACCEPTANCE_PACKAGE_COORDINATE"
        ],
        confirm=hostile,
    )

    _assert_rejected_dispatch_evidence(
        evidence,
        evidence_bytes,
        hostile_value=hostile,
    )


@pytest.mark.parametrize(
    ("target_sha", "workflow_sha", "message"),
    [
        ("0" * 40, "b" * 40, "non-zero target-sha"),
        ("a" * 40, "0" * 40, r"non-zero workflow\.sha"),
    ],
    ids=["target-sha", "workflow-sha"],
)
def test_terminal_complete_evidence_never_emits_zero_sha(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    target_sha: str,
    workflow_sha: str,
    message: str,
) -> None:
    absent = _terminal_suite(
        "absent-create-readback",
        (
            (
                "absent-create-readback",
                "absent",
                "exact",
                "created",
                "complete",
                (),
            ),
        ),
    )
    conflict = _terminal_suite(
        "exact-and-conflict",
        (
            ("exact", "exact", "exact", "exact-no-mutation", "complete", ()),
            (
                "identical-race",
                "absent",
                "exact",
                "identical-race-exact",
                "complete",
                ("identical-race-exact",),
            ),
            (
                "differing-race",
                "absent",
                "conflicting",
                "differing-race-conflict",
                "complete",
                ("conflicting-remote-bytes-or-tag",),
            ),
            (
                "lost-response",
                "absent",
                "exact",
                "lost-response-exact-after-start",
                "complete",
                ("mutation-started-and-readback-exact",),
            ),
        ),
    )
    evidence_path = tmp_path / ".wdv3/evidence.json"
    environment = {
        "WDV3_FILE": str(evidence_path),
        "WDV3_ACCEPTANCE_TARGET_SHA": target_sha,
        "VALIDATE_RESULT": "success",
        "REVIEW_RESULT": "success",
        "ABSENT_JOB_RESULT": "success",
        "CONFLICT_JOB_RESULT": "success",
        "REVIEW_ARTIFACT_ID": "700",
        "ABSENT_RESULT": "success",
        "ABSENT_RECORD_JSON": json.dumps(absent.to_document()),
        "ABSENT_RECORD_DIGEST": absent.to_document()["record-digest"],
        "ABSENT_ARTIFACT_ID": "701",
        "ABSENT_ARTIFACT_DIGEST": "sha256:" + ("a" * 64),
        "CONFLICT_RESULT": "success",
        "CONFLICT_RECORD_JSON": json.dumps(conflict.to_document()),
        "CONFLICT_RECORD_DIGEST": conflict.to_document()["record-digest"],
        "CONFLICT_ARTIFACT_ID": "702",
        "CONFLICT_ARTIFACT_DIGEST": "sha256:" + ("b" * 64),
        "GITHUB_REPOSITORY": "hcoona/three",
        "GITHUB_REF": "refs/heads/main",
        "GITHUB_WORKFLOW_SHA": workflow_sha,
        "GITHUB_RUN_ID": "303",
        "GITHUB_RUN_ATTEMPT": "1",
    }
    monkeypatch.chdir(tmp_path)
    evidence_path.parent.mkdir()
    for name, value in environment.items():
        monkeypatch.setenv(name, str(value))
    command = _runs(_jobs()["capture-governance-evidence"])[0]
    script = command.split("<<'PY'\n", 1)[1].rsplit("\nPY", 1)[0]

    with pytest.raises(ValueError, match=message):
        exec(
            compile(script, str(WORKFLOW), "exec"),
            {"__name__": "__main__"},
        )

    assert not evidence_path.exists()


def test_terminal_incomplete_evidence_preserves_rejected_dispatch_semantics(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    canonical_environment = cast("dict[str, str]", _document()["env"])

    evidence, evidence_bytes = _execute_rejected_dispatch_capture(
        tmp_path,
        monkeypatch,
        target_sha=ZERO_SHA,
        package_coordinate=canonical_environment[
            "WDV3_ACCEPTANCE_PACKAGE_COORDINATE"
        ],
        confirm=canonical_environment["WDV3_ACCEPTANCE_CONFIRMATION"],
    )
    admitted = admit_governance_acceptance_evidence(evidence_bytes)

    assert admitted.mutation_classification == "incomplete"
    assert admitted.target_sha == FINALIZED_TARGET_SHA
    assert admitted.workflow.sha == "b" * 40
    assert admitted.to_document() == evidence


@pytest.mark.parametrize("probe", PROBE_JOBS)
def test_probe_jobs_record_upload_then_classify(probe: str) -> None:
    steps = _steps(_jobs()[probe])
    indexed_ids = {
        str(step["id"]): index
        for index, step in enumerate(steps)
        if "id" in step
    }
    upload = steps[indexed_ids["upload"]]

    assert {"probe", "upload", "classify"} <= indexed_ids.keys()
    assert (
        indexed_ids["probe"] < indexed_ids["upload"] < indexed_ids["classify"]
    )
    assert upload["if"] == "${{ always() }}"
    assert str(upload["uses"]).startswith("actions/upload-artifact@")


@pytest.mark.parametrize("probe", PROBE_JOBS)
def test_probe_classification_gate_runs_after_failed_record_upload_attempt(
    probe: str,
) -> None:
    steps = _steps(_jobs()[probe])
    upload_index = next(
        index for index, step in enumerate(steps) if step.get("id") == "upload"
    )
    classify_indexes = [
        index
        for index, step in enumerate(steps)
        if step.get("id") == "classify"
    ]
    assert len(classify_indexes) == 1
    classify_index = classify_indexes[0]
    condition = str(steps[classify_index]["if"])
    environment = cast("dict[str, str]", steps[classify_index]["env"])

    assert classify_index > upload_index
    assert "always()" in condition
    assert environment["PROBE_OUTCOME"] == "${{ steps.probe.outcome }}"
    assert environment["UPLOAD_OUTCOME"] == "${{ steps.upload.outcome }}"


def test_first_probe_failure_prevents_second_mutation_job() -> None:
    second = _jobs()["probe-exact-and-conflict"]
    condition = str(second["if"])

    assert second["needs"] == "probe-absent-create-readback"
    assert "needs.probe-absent-create-readback.result == 'success'" in condition
    assert "github.run_attempt == 1" in condition
    assert "always()" not in condition


def test_terminal_job_fans_in_all_probe_results_and_outputs() -> None:
    capture = _jobs()["capture-governance-evidence"]
    form_step = next(
        step
        for step in _steps(capture)
        if step["name"] == "Form and admit terminal Governance evidence"
    )
    command = str(form_step["run"])
    executable_command = _non_comment_command(command)
    rendered_capture = json.dumps(capture)
    env = cast("dict[str, str]", form_step["env"])
    expected_bindings = {
        "VALIDATE_RESULT": "${{ needs.validate-fixed-inputs.result }}",
        "REVIEW_RESULT": "${{ needs.acceptance-review.result }}",
        "ABSENT_JOB_RESULT": "${{ needs.probe-absent-create-readback.result }}",
        "CONFLICT_JOB_RESULT": "${{ needs.probe-exact-and-conflict.result }}",
        "REVIEW_ARTIFACT_ID": (
            "${{ needs.acceptance-review.outputs.artifact-id }}"
        ),
        "ABSENT_RESULT": "${{ needs.probe-absent-create-readback.outputs.result }}",
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

    assert capture["if"] == "${{ always() && github.run_attempt == 1 }}"
    assert tuple(capture["needs"]) == EXPECTED_JOBS[:-1]
    for key, expression in expected_bindings.items():
        assert env[key] == expression
        assert f'os.environ["{key}"]' in executable_command or (
            f'os.environ.get("{key}"' in executable_command
        )
    for dependency in EXPECTED_JOBS[:-1]:
        assert f"needs.{dependency}.result" in rendered_capture
    assert "needs.acceptance-review.outputs.artifact-id" in rendered_capture
    for probe in PROBE_JOBS:
        for output in (
            "result",
            "record-json",
            "record-digest",
            "artifact-id",
            "artifact-digest",
        ):
            assert f"needs.{probe}.outputs.{output}" in rendered_capture
        assert f"needs.{probe}.result == 'failure'" not in executable_command
        assert f"needs.{probe}.result == 'skipped'" not in executable_command
    assert "job_results = {" in executable_command
    assert '"ABSENT": os.environ["ABSENT_JOB_RESULT"]' in executable_command
    assert "suite_fact(*item, job_results[item[2]])" in executable_command
    assert '"dependency-results": dependencies' in executable_command
    assert '"probe-facts": probe_facts' in executable_command


def test_terminal_evidence_upload_is_always_attempted() -> None:
    steps = _steps(_jobs()["capture-governance-evidence"])
    form_index = next(
        index
        for index, step in enumerate(steps)
        if step["name"] == "Form and admit terminal Governance evidence"
    )
    upload_index = next(
        index
        for index, step in enumerate(steps)
        if step["name"] == "Upload immutable Governance acceptance evidence"
    )
    upload = steps[upload_index]

    assert upload_index > form_index
    assert upload.get("if") == "${{ always() }}"
    assert upload["with"]["if-no-files-found"] == "error"


@pytest.mark.parametrize("probe", PROBE_JOBS)
def test_package_writing_jobs_pin_exact_node_and_npm_versions(
    probe: str,
) -> None:
    steps = _steps(_jobs()[probe])
    setup_indexes = [
        index
        for index, step in enumerate(steps)
        if str(step.get("uses", "")).startswith("actions/setup-node@")
    ]
    assert len(setup_indexes) == 1
    setup_index = setup_indexes[0]
    setup = steps[setup_index]
    _action, separator, revision = str(setup["uses"]).partition("@")
    mutation_index = next(
        index for index, step in enumerate(steps) if step.get("id") == "probe"
    )
    preceding_commands = "\n".join(
        str(step["run"])
        for step in steps[setup_index + 1 : mutation_index]
        if "run" in step
    )

    assert separator == "@"
    assert ACTION_PIN.fullmatch(revision)
    assert setup["with"]["node-version"] == "24.14.0"
    assert "npm install --global npm@11.9.0" in preceding_commands
    assert 'test "$(node --version)" = "v24.14.0"' in preceding_commands
    assert 'test "$(npm --version)" = "11.9.0"' in preceding_commands


@pytest.mark.parametrize("probe", PROBE_JOBS)
def test_package_writing_setup_is_credential_free(probe: str) -> None:
    steps = _steps(_jobs()[probe])
    checkout = next(
        step
        for step in steps
        if str(step.get("uses", "")).startswith("actions/checkout@")
    )
    setup_steps = [
        step
        for step in steps
        if str(step.get("uses", "")).startswith("actions/setup-node@")
    ]
    assert len(setup_steps) == 1
    setup = setup_steps[0]
    setup_with = cast("dict[str, Any]", setup.get("with", {}))

    assert checkout["with"]["persist-credentials"] is False
    assert not {
        "registry-url",
        "scope",
        "token",
        "github-token",
    }.intersection(key.casefold() for key in setup_with)
    for step in steps:
        if step.get("id") == "probe":
            break
        assert "WDV3_ACCEPTANCE_GITHUB_TOKEN" not in json.dumps(step)


def test_dedicated_token_enters_only_acceptance_process_boundary() -> None:
    jobs = _jobs()
    token_locations = []

    for job_name, job in jobs.items():
        for step in _steps(job):
            rendered = json.dumps(step)
            if "WDV3_ACCEPTANCE_GITHUB_TOKEN" in rendered:
                token_locations.append((job_name, step.get("id"), step["name"]))
                assert step.get("id") == "probe"
                assert step["env"]["WDV3_ACCEPTANCE_GITHUB_TOKEN"] == (
                    "${{ github.token }}"  # noqa: S105
                )
                assert "run-fixed-acceptance-probe" in str(step["run"])

    assert token_locations == [
        (
            "probe-absent-create-readback",
            "probe",
            "Run fixed absent/create/readback suite",
        ),
        (
            "probe-exact-and-conflict",
            "probe",
            "Run fixed exact and conflict suite",
        ),
    ]


@pytest.mark.parametrize("probe", PROBE_JOBS)
def test_probe_job_contract_outputs_source_post_upload_classification(
    probe: str,
) -> None:
    job = _jobs()[probe]
    steps = _steps(job)
    classify_indexes = [
        index
        for index, step in enumerate(steps)
        if step.get("id") == "classify"
    ]
    assert len(classify_indexes) == 1
    classify_index = classify_indexes[0]
    upload_index = next(
        index for index, step in enumerate(steps) if step.get("id") == "upload"
    )

    assert classify_index > upload_index
    assert job["outputs"]["result"] == "${{ steps.classify.outputs.result }}"
    assert job["outputs"]["mutation-classification"] == (
        "${{ steps.classify.outputs.mutation-classification }}"
    )
    assert job["outputs"]["scenario-inventory"] == (
        "${{ steps.classify.outputs.scenario-inventory }}"
    )


@pytest.mark.parametrize("probe", PROBE_JOBS)
def test_post_upload_classification_fails_closed_for_failed_or_missing_upload(
    probe: str,
    tmp_path: Path,
) -> None:
    classify_steps = [
        step for step in _steps(_jobs()[probe]) if step.get("id") == "classify"
    ]
    assert len(classify_steps) == 1
    classify = classify_steps[0]
    command = str(classify["run"])
    environment = cast("dict[str, str]", classify["env"])

    assert classify["if"] == "${{ always() }}"
    assert environment["UPLOAD_OUTCOME"] == "${{ steps.upload.outcome }}"
    assert environment["UPLOAD_ARTIFACT_ID"] == (
        "${{ steps.upload.outputs.artifact-id }}"
    )
    assert environment["UPLOAD_ARTIFACT_DIGEST"] == (
        "${{ steps.upload.outputs.artifact-digest }}"
    )

    cases = (
        (
            "failed-upload",
            "failure",
            "731",
            "sha256:" + ("a" * 64),
            "unknown",
            "unknown",
        ),
        (
            "missing-artifact-id",
            "success",
            "",
            "sha256:" + ("a" * 64),
            "incomplete",
            "incomplete",
        ),
        (
            "missing-artifact-digest",
            "success",
            "731",
            "",
            "incomplete",
            "incomplete",
        ),
        (
            "complete-upload",
            "success",
            "731",
            "sha256:" + ("a" * 64),
            "success",
            "complete",
        ),
    )
    for (
        case,
        upload_outcome,
        artifact_id,
        artifact_digest,
        expected_result,
        expected_classification,
    ) in cases:
        github_output = tmp_path / f"{probe}-{case}.out"
        expression_values = {
            "${{ steps.probe.outcome }}": "success",
            "${{ steps.probe.outputs.result }}": "success",
            "${{ steps.probe.outputs.mutation-classification }}": "complete",
            "${{ steps.probe.outputs.scenario-inventory }}": '["scenario"]',
            "${{ steps.probe.outputs.record-digest }}": "sha256:" + ("b" * 64),
            "${{ steps.probe.outputs.record-json }}": "{}",
            "${{ steps.upload.outcome }}": upload_outcome,
            "${{ steps.upload.outputs.artifact-id }}": artifact_id,
            "${{ steps.upload.outputs.artifact-digest }}": artifact_digest,
        }
        unknown_expressions = (
            set(environment.values()) - expression_values.keys()
        )
        assert not unknown_expressions
        process_environment = os.environ | {
            name: expression_values[value]
            for name, value in environment.items()
        }
        process_environment["GITHUB_OUTPUT"] = str(github_output)

        subprocess.run(  # noqa: S603
            (
                "bash",
                "--noprofile",
                "--norc",
                "-euo",
                "pipefail",
                "-c",
                command,
            ),
            check=True,
            cwd=tmp_path,
            env=process_environment,
        )

        output_pairs = [
            line.partition("=")
            for line in github_output.read_text(encoding="utf-8").splitlines()
        ]
        assert all(separator == "=" for _, separator, _ in output_pairs)
        assert sum(key == "result" for key, _, _ in output_pairs) == 1
        assert (
            sum(key == "mutation-classification" for key, _, _ in output_pairs)
            == 1
        )
        emitted = {key: value for key, _, value in output_pairs}
        assert {
            "result": emitted["result"],
            "mutation-classification": emitted["mutation-classification"],
        } == {
            "result": expected_result,
            "mutation-classification": expected_classification,
        }
