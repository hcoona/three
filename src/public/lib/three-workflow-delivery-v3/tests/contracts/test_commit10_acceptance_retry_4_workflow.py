"""Static contract for the temporary retry-4 acceptance workflow."""

# ruff: noqa: D103, E501, PLR2004, S603

from __future__ import annotations

import ast
import hashlib
import os
import re
import subprocess
from pathlib import Path
from typing import Any, cast

import yaml

REPO_ROOT = Path(__file__).resolve().parents[6]
WORKFLOW_RELATIVE_PATH = (
    ".github/workflows/workflow-delivery-v3-buddy-smoke-acceptance-retry-4.yml"
)
WORKFLOW_PATH = REPO_ROOT / WORKFLOW_RELATIVE_PATH
WORKFLOW_STEM = "workflow-delivery-v3-buddy-smoke-acceptance-retry-4"
ENVIRONMENT = "workflow-delivery-v3-buddy-smoke-acceptance-retry-4"

ZERO_SHA = "0" * 40
TEST_ONLY_NONZERO_TARGET_SHA = "d" * 40
PACKAGE_NAME = "@hcoona/hcoona-release-smoke-npm"
BASE_COORDINATE = "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.13"
CONFIRMATION = "I_ACCEPT_DISPOSABLE_GITHUB_PACKAGES_PROBES_RETRY_4"
CONFIRMATION_DIGEST = (
    "sha256:b6f94d3c13c98b0714404959dd878230f8302ee849038a536f5a18cc3a85c7ec"
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
    """Load the workflow only after a test body has started."""
    assert WORKFLOW_PATH.is_file(), (
        "E-WORKFLOW-ABSENT: required retry-4 workflow is absent at "
        f"{WORKFLOW_RELATIVE_PATH}"
    )
    raw = WORKFLOW_PATH.read_text(encoding="utf-8")
    value = yaml.safe_load(raw)
    assert isinstance(value, dict), "retry-4 workflow must be a YAML mapping"
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
    validation = _step(
        _jobs(document)["validate-fixed-inputs"],
        "Fail closed before review or mutation",
    )
    run = validation.get("run")
    assert isinstance(run, str)
    return run


def _guard_conditions(script: str) -> list[str]:
    return re.findall(r"if \[\[ (.+) \]\]; then", script)


def _run_fixed_input_guard(
    document: dict[str, Any],
    *,
    target_sha: str,
    overrides: dict[str, str] | None = None,
) -> subprocess.CompletedProcess[str]:
    environment = {
        **os.environ,
        "INPUT_TARGET_SHA": target_sha,
        "INPUT_PACKAGE_COORDINATE": BASE_COORDINATE,
        "INPUT_CONFIRM": CONFIRMATION,
        "WDV3_ACCEPTANCE_TARGET_SHA": target_sha,
        "WDV3_ACCEPTANCE_PACKAGE_COORDINATE": BASE_COORDINATE,
        "WDV3_ACCEPTANCE_CONFIRMATION": CONFIRMATION,
        "WDV3_ACCEPTANCE_REF": "refs/heads/main",
        "GITHUB_REF": "refs/heads/main",
        "GITHUB_RUN_ATTEMPT": "1",
    }
    environment.update(overrides or {})
    return subprocess.run(
        ("/usr/bin/bash", "-c", _validation_script(document)),
        check=False,
        capture_output=True,
        text=True,
        env=environment,
    )


def _terminal_python(document: dict[str, Any]) -> str:
    terminal = _step(
        _jobs(document)["capture-governance-evidence"],
        "Form and admit terminal Governance evidence",
    )
    run = terminal.get("run")
    assert isinstance(run, str)
    marker = "python - <<'PY'\n"
    assert run.count(marker) == 1
    script, terminator = run.split(marker, 1)[1].rsplit("\nPY", 1)
    assert not terminator.strip()
    return script


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


def test_retry_4_workflow_uses_exact_temporary_path_stem_and_environment_identity() -> (
    None
):
    document, _raw = _load_workflow()
    jobs = _jobs(document)

    assert WORKFLOW_PATH.relative_to(REPO_ROOT).as_posix() == (
        ".github/workflows/"
        "workflow-delivery-v3-buddy-smoke-acceptance-retry-4.yml"
    )
    assert WORKFLOW_PATH.name == (
        "workflow-delivery-v3-buddy-smoke-acceptance-retry-4.yml"
    )
    assert WORKFLOW_PATH.stem == WORKFLOW_STEM
    assert ENVIRONMENT == WORKFLOW_STEM
    assert document["name"] == (
        "Workflow Delivery v3 Buddy smoke destination acceptance retry 4"
    )
    assert jobs["acceptance-review"]["environment"] == ENVIRONMENT
    assert [name for name, job in jobs.items() if "environment" in job] == [
        "acceptance-review"
    ]


def test_retry_4_workflow_declares_exact_five_jobs_in_order() -> None:
    document, _raw = _load_workflow()
    jobs = _jobs(document)

    assert tuple(jobs) == JOB_ORDER
    assert {name: _needs(job) for name, job in jobs.items()} == {
        "validate-fixed-inputs": (),
        "acceptance-review": ("validate-fixed-inputs",),
        "probe-absent-create-readback": ("acceptance-review",),
        "probe-exact-and-conflict": ("probe-absent-create-readback",),
        "capture-governance-evidence": (
            "validate-fixed-inputs",
            "acceptance-review",
            "probe-absent-create-readback",
            "probe-exact-and-conflict",
        ),
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


def test_retry_4_workflow_applies_first_attempt_guards_and_terminal_always_capture() -> (
    None
):
    document, _raw = _load_workflow()
    jobs = _jobs(document)

    assert {name: job.get("if") for name, job in jobs.items()} == {
        "validate-fixed-inputs": "${{ github.run_attempt == 1 }}",
        "acceptance-review": "${{ github.run_attempt == 1 }}",
        "probe-absent-create-readback": ("${{ github.run_attempt == 1 }}"),
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
        assert [step.get("if") for step in _steps(jobs[job_name])[-3:]] == [
            "${{ always() }}",
            "${{ always() }}",
            "${{ always() }}",
        ]


def test_retry_4_workflow_scopes_environment_and_packages_write_permissions_to_exact_jobs() -> (
    None
):
    document, _raw = _load_workflow()
    jobs = _jobs(document)

    assert document["permissions"] == {}
    assert {name: job.get("permissions") for name, job in jobs.items()} == {
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
        if job.get("permissions", {}).get("packages") == "write"
    } == set(PROBE_JOBS)
    assert {
        name: job["environment"]
        for name, job in jobs.items()
        if "environment" in job
    } == {"acceptance-review": ENVIRONMENT}
    assert all(
        permission in {"read", "write"}
        for job in jobs.values()
        for permission in job.get("permissions", {}).values()
    )


def test_retry_4_workflow_zero_target_stops_before_review_and_write_capable_probes() -> (
    None
):
    document, _raw = _load_workflow()
    jobs = _jobs(document)
    inputs = _triggers(document)["workflow_dispatch"]["inputs"]
    validation = jobs["validate-fixed-inputs"]
    guard = _step(validation, "Fail closed before review or mutation")
    script = _validation_script(document)

    assert ZERO_SHA == "0000000000000000000000000000000000000000"
    assert len(ZERO_SHA) == 40
    assert ZERO_SHA.isascii()
    assert set(ZERO_SHA) == {"0"}
    assert inputs["target_sha"]["default"] == ZERO_SHA
    assert document["env"]["WDV3_ACCEPTANCE_TARGET_SHA"] == ZERO_SHA
    assert validation.get("continue-on-error", False) is False
    assert guard.get("continue-on-error", False) is False
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
    assert [step["name"] for step in _steps(jobs["validate-fixed-inputs"])] == [
        "Fail closed before review or mutation"
    ]

    result = _run_fixed_input_guard(document, target_sha=ZERO_SHA)

    assert (result.returncode, result.stdout, result.stderr) == (1, "", "")
    for downstream in ("acceptance-review", *PROBE_JOBS):
        assert "validate-fixed-inputs" in _transitive_needs(jobs, downstream)
    assert all(
        jobs[name]["permissions"]["packages"] == "write" for name in PROBE_JOBS
    )
    assert "packages" not in jobs["capture-governance-evidence"]["permissions"]


def test_retry_4_workflow_test_only_nonzero_placeholder_satisfies_finalized_guard_shape() -> (
    None
):
    document, raw = _load_workflow()
    inputs = _triggers(document)["workflow_dispatch"]["inputs"]

    assert TEST_ONLY_NONZERO_TARGET_SHA == "d" * 40
    assert re.fullmatch(r"[0-9a-f]{40}", TEST_ONLY_NONZERO_TARGET_SHA)
    assert TEST_ONLY_NONZERO_TARGET_SHA != ZERO_SHA
    assert TEST_ONLY_NONZERO_TARGET_SHA not in raw
    assert inputs["target_sha"]["default"] == ZERO_SHA
    assert document["env"]["WDV3_ACCEPTANCE_TARGET_SHA"] == ZERO_SHA

    result = _run_fixed_input_guard(
        document,
        target_sha=TEST_ONLY_NONZERO_TARGET_SHA,
    )

    assert (result.returncode, result.stdout, result.stderr) == (0, "", "")
    assert _guard_conditions(_validation_script(document))[1:3] == [
        f'"${{WDV3_ACCEPTANCE_TARGET_SHA}}" == "{ZERO_SHA}"',
        f'"${{INPUT_TARGET_SHA}}" == "{ZERO_SHA}"',
    ]


def test_retry_4_workflow_dispatch_identity_confirmation_digest_and_concurrency_are_exact() -> (
    None
):
    document, raw = _load_workflow()
    triggers = _triggers(document)
    inputs = triggers["workflow_dispatch"]["inputs"]

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
    package_name, version = BASE_COORDINATE.rsplit("@", 1)
    assert package_name == PACKAGE_NAME
    assert version == "0.0.0-wdv3-acceptance.13"
    assert raw.count(BASE_COORDINATE) == 3
    assert raw.count(CONFIRMATION) == 2
    assert (
        "sha256:" + hashlib.sha256(CONFIRMATION.encode()).hexdigest()
        == CONFIRMATION_DIGEST
    )
    assert document["concurrency"] == {
        "group": (
            "hcoona-release-smoke-npm-workflow-delivery-v3-"
            "buddy-smoke-acceptance-retry-4"
        ),
        "cancel-in-progress": False,
    }


def test_retry_4_workflow_pins_current_actions_toolchains_checkout_and_probe_wiring() -> (
    None
):
    document, _raw = _load_workflow()
    jobs = _jobs(document)
    uses_by_job = {
        name: [
            cast("str", step["uses"]) for step in _steps(job) if "uses" in step
        ]
        for name, job in jobs.items()
    }
    uses = [use for job_uses in uses_by_job.values() for use in job_uses]

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
    assert set(uses) == ACTION_PINS
    assert all(re.fullmatch(r"[^@\s]+@[0-9a-f]{40}", use) for use in uses)

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
    npm_install = (
        "npm install --global npm@11.17.0\n"
        'test "$(node --version)" = "v24.19.0"\n'
        'test "$(npm --version)" = "11.17.0"\n'
    )
    authorized_probe_steps = []
    for job_name in PROBE_JOBS:
        job = jobs[job_name]
        steps = _steps(job)
        assert job["outputs"] == expected_outputs
        assert tuple(step["name"] for step in steps) == expected_steps[job_name]
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
        assert _step(job, "Install and verify exact npm toolchain")["run"] == (
            npm_install
        )
        probe_step = next(step for step in steps if step.get("id") == "probe")
        assert probe_step["env"] == {
            "WDV3_ACCEPTANCE_GITHUB_TOKEN": "${{ github.token }}",
            "INPUT_PACKAGE_COORDINATE": "${{ inputs.package_coordinate }}",
            "INPUT_TARGET_SHA": "${{ inputs.target_sha }}",
        }
        assert probe_step["run"] == expected_commands[job_name]
        assert [step.get("id") for step in steps[-4:]] == [
            "probe",
            "upload",
            "classify",
            None,
        ]
        authorized_probe_steps.append(probe_step)

    assert [
        step
        for job in jobs.values()
        for step in _steps(job)
        if "WDV3_ACCEPTANCE_GITHUB_TOKEN" in step.get("env", {})
    ] == authorized_probe_steps
    terminal_checkout = _step(
        jobs["capture-governance-evidence"],
        "Check out immutable workflow source",
    )
    assert terminal_checkout["with"] == {
        "ref": "${{ github.workflow_sha }}",
        "persist-credentials": False,
        "token": "${{ github.token }}",
    }
    assert _step(
        jobs["capture-governance-evidence"],
        "Install uv",
    )["with"] == {"version": "0.12.5", "github-token": ""}


def test_retry_4_workflow_wires_terminal_governance_evidence_exactly() -> None:
    document, _raw = _load_workflow()
    jobs = _jobs(document)
    terminal = jobs["capture-governance-evidence"]
    terminal_steps = _steps(terminal)

    assert [step["name"] for step in terminal_steps] == [
        "Check out immutable workflow source",
        "Install uv",
        "Form and admit terminal Governance evidence",
        "Upload immutable Governance acceptance evidence",
    ]
    evidence_step = terminal_steps[2]
    assert evidence_step["env"] == {
        "INPUT_TARGET_SHA": "${{ inputs.target_sha }}",
        "INPUT_PACKAGE_COORDINATE": "${{ inputs.package_coordinate }}",
        "VALIDATE_RESULT": "${{ needs.validate-fixed-inputs.result }}",
        "REVIEW_RESULT": "${{ needs.acceptance-review.result }}",
        "ABSENT_JOB_RESULT": (
            "${{ needs.probe-absent-create-readback.result }}"
        ),
        "CONFLICT_JOB_RESULT": ("${{ needs.probe-exact-and-conflict.result }}"),
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
    evidence_path = (
        ".wdv3/governance-acceptance-r${GITHUB_RUN_ID}-"
        "ra${GITHUB_RUN_ATTEMPT}.json"
    )
    assert f'export WDV3_FILE="{evidence_path}"' in evidence_step["run"]

    terminal_upload = terminal_steps[3]
    assert terminal_upload["if"] == "${{ always() }}"
    assert terminal_upload["uses"] == UPLOAD
    assert terminal_upload["with"] == {
        "name": (
            "wdv3-governance-acceptance-r${{ github.run_id }}-"
            "ra${{ github.run_attempt }}"
        ),
        "path": (
            ".wdv3/governance-acceptance-r${{ github.run_id }}-"
            "ra${{ github.run_attempt }}.json"
        ),
        "if-no-files-found": "error",
        "include-hidden-files": True,
        "retention-days": 45,
        "overwrite": False,
        "archive": False,
    }

    script = _terminal_python(document)
    tree = ast.parse(script)
    expected = ast.literal_eval(_assigned_value(tree, "expected"))
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

    evidence_node = _assigned_value(tree, "document")
    evidence_fields = _dict_fields(evidence_node)
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
    assert ast.dump(
        evidence_fields["confirmation-digest"],
        include_attributes=False,
    ) == ast.dump(
        ast.parse(
            '"sha256:" + hashlib.sha256('
            'os.environ["WDV3_ACCEPTANCE_CONFIRMATION"].encode()'
            ").hexdigest()",
            mode="eval",
        ).body,
        include_attributes=False,
    )

    workflow_fields = _dict_fields(evidence_fields["workflow"])
    assert tuple(workflow_fields) == ("repository", "path", "ref", "sha")
    assert ast.literal_eval(workflow_fields["path"]) == WORKFLOW_RELATIVE_PATH
    recovery_fields = _dict_fields(evidence_fields["recovery"])
    assert tuple(recovery_fields) == (
        "workflow-run-id",
        "environment",
        "deployment",
        "job",
        "artifact-id",
    )
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
    diagnostic_call = next(
        call
        for call in ast.walk(tree)
        if isinstance(call, ast.Call)
        and ast.unparse(call.func) == "AcceptanceRunnerDiagnostic"
    )
    assert {keyword.arg for keyword in diagnostic_call.keywords} == {
        "exit_classification",
        "upstream_status",
        "exception_category",
        "request_correlation_digest",
    }
    assert script.count(f'"{BASE_COORDINATE}"') == 1
    assert script.count(f'"{CONFIRMATION}"') == 0
    assert script.count(f'"{ZERO_SHA}"') == 2
    assert 'record_digest = suite.to_document()["record-digest"]' in script
    assert "record_digest != asserted_digest" in script
    assert 'record_digest != record_json.get("record-digest")' in script


def test_retry_4_workflow_rejects_wrong_dispatch_inputs() -> None:
    document, _raw = _load_workflow()
    wrong_cases: dict[str, dict[str, str]] = {
        "target": {"INPUT_TARGET_SHA": "e" * 40},
        "package-base": {
            "INPUT_PACKAGE_COORDINATE": (
                f"{PACKAGE_NAME}@0.0.0-wdv3-acceptance.14"
            )
        },
        "confirmation": {"INPUT_CONFIRM": "NOT_THE_RETRY_4_CONFIRMATION"},
        "protected-ref": {"GITHUB_REF": "refs/heads/not-main"},
        "run-attempt": {"GITHUB_RUN_ATTEMPT": "2"},
    }

    accepted = _run_fixed_input_guard(
        document,
        target_sha=TEST_ONLY_NONZERO_TARGET_SHA,
    )
    rejected = {
        name: _run_fixed_input_guard(
            document,
            target_sha=TEST_ONLY_NONZERO_TARGET_SHA,
            overrides=overrides,
        )
        for name, overrides in wrong_cases.items()
    }

    assert (accepted.returncode, accepted.stdout, accepted.stderr) == (
        0,
        "",
        "",
    )
    assert {
        name: (result.returncode, result.stdout, result.stderr)
        for name, result in rejected.items()
    } == dict.fromkeys(wrong_cases, (1, "", ""))


def test_retry_4_workflow_exposes_no_live_release_bypass_force_or_generalized_triggers() -> (
    None
):
    document, raw = _load_workflow()
    triggers = _triggers(document)
    inputs = triggers["workflow_dispatch"]["inputs"]
    jobs = _jobs(document)
    terminal = _terminal_python(document)

    assert tuple(triggers) == ("workflow_dispatch",)
    assert set(triggers).isdisjoint(
        {
            "workflow_call",
            "schedule",
            "push",
            "pull_request",
            "pull_request_target",
            "repository_dispatch",
            "workflow_run",
        }
    )
    assert tuple(inputs) == ("target_sha", "package_coordinate", "confirm")
    assert set(inputs).isdisjoint(
        {
            "ref",
            "channel",
            "release_unit",
            "suite",
            "tag",
            "environment",
            "live",
            "release",
            "bypass",
            "force",
        }
    )
    assert all("uses" not in job for job in jobs.values())
    assert re.search(r"\b(?:Live|Release)\b", raw) is None
    assert re.search(r"(?i)\blive\b", raw) is None
    assert re.search(r"(?i)\bbypass\b", raw) is None
    assert re.search(r"(?i)\bforce\b", raw) is None
    route_identifiers = {
        "triggers": tuple(str(name) for name in triggers),
        "inputs": tuple(str(name) for name in inputs),
        "jobs": tuple(jobs),
        "job-names": tuple(str(job.get("name", "")) for job in jobs.values()),
        "step-names": tuple(
            str(step.get("name", ""))
            for job in jobs.values()
            for step in _steps(job)
        ),
    }
    route_pattern = re.compile(r"(?i)(?:^|[\s_-])(?:live|release)(?:$|[\s_-])")
    assert {
        surface: tuple(
            identifier
            for identifier in identifiers
            if route_pattern.search(identifier)
        )
        for surface, identifiers in route_identifiers.items()
    } == dict.fromkeys(route_identifiers, ())
    assert re.findall(
        (
            r"(?m)^\s*three-workflow-delivery-v3\s+"
            r"([a-z][a-z0-9-]*)\b"
        ),
        raw,
    ) == ["governance", "governance"]
    assert '"release-lineage": "none"' in terminal
    assert "--channel" not in raw
    assert "--release-unit" not in raw
    assert "--bypass" not in raw
    assert "--force" not in raw
    assert "live_enabled" not in raw
    assert document["env"]["WDV3_PURPOSE"] == "destination-acceptance"


def test_retry_4_terminal_program_preserves_fixed_identity_after_rejected_dispatch(
    tmp_path: Path,
) -> None:
    import sys  # noqa: PLC0415

    from three_workflow_delivery_v3.canonical import (  # noqa: PLC0415
        canonicalize,
    )
    from three_workflow_delivery_v3.records.governance import (  # noqa: PLC0415
        admit_governance_acceptance_evidence,
    )

    document, _raw = _load_workflow()
    program = _terminal_python(document)
    evidence_path = tmp_path / "governance-acceptance.json"
    empty_home = tmp_path / "empty-home"
    empty_home.mkdir()
    run_id = 608
    workflow_sha = "a" * 40
    environment = {
        "HOME": str(empty_home),
        "PATH": os.environ.get("PATH", os.defpath),
        "PYTHONNOUSERSITE": "1",
        "USERPROFILE": str(empty_home),
        "WDV3_FILE": str(evidence_path),
        "INPUT_TARGET_SHA": "e" * 40,
        "INPUT_PACKAGE_COORDINATE": (
            f"{PACKAGE_NAME}@0.0.0-wdv3-acceptance.17"
        ),
        "WDV3_ACCEPTANCE_TARGET_SHA": ZERO_SHA,
        "WDV3_ACCEPTANCE_PACKAGE_COORDINATE": BASE_COORDINATE,
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
        "GITHUB_RUN_ATTEMPT": "1",
        "GITHUB_RUN_ID": str(run_id),
        "GITHUB_REPOSITORY": "hcoona/three",
        "GITHUB_REF": "refs/heads/main",
        "GITHUB_WORKFLOW_SHA": workflow_sha,
    }
    if system_root := os.environ.get("SYSTEMROOT"):
        environment["SYSTEMROOT"] = system_root
    if "INPUT_CONFIRM" in program:
        environment["INPUT_CONFIRM"] = "NOT_THE_RETRY_4_CONFIRMATION"

    result = subprocess.run(
        (sys.executable, "-I", "-c", program),
        check=False,
        capture_output=True,
        text=True,
        env=environment,
    )

    assert (result.returncode, result.stdout, result.stderr) == (0, "", "")
    assert evidence_path.is_file()
    evidence = evidence_path.read_bytes()
    admitted = admit_governance_acceptance_evidence(evidence)
    assert evidence == canonicalize(admitted.to_document())
    assert admitted.workflow.to_document() == {
        "repository": "hcoona/three",
        "path": WORKFLOW_RELATIVE_PATH,
        "ref": "refs/heads/main",
        "sha": workflow_sha,
    }
    assert (admitted.target_sha, admitted.package_coordinate) == (
        ZERO_SHA,
        BASE_COORDINATE,
    )
    assert admitted.confirmation_digest == CONFIRMATION_DIGEST
    assert admitted.environment == ENVIRONMENT
    assert admitted.reviewer_record.to_document() == {
        "login": None,
        "source": "unavailable-in-job-context",
    }
    assert admitted.recovery.to_document() == {
        "workflow-run-id": run_id,
        "environment": ENVIRONMENT,
        "deployment": f"run:{run_id}/environment:acceptance",
        "job": "acceptance-review",
        "artifact-id": None,
    }
    assert tuple(
        result.to_document() for result in admitted.dependency_results
    ) == (
        {"job": "validate-fixed-inputs", "result": "failure"},
        {"job": "acceptance-review", "result": "skipped"},
        {"job": "probe-absent-create-readback", "result": "skipped"},
        {"job": "probe-exact-and-conflict", "result": "skipped"},
    )
    assert tuple(fact.to_document() for fact in admitted.probe_facts) == (
        {
            "probe": "probe-absent-create-readback",
            "result": "incomplete",
            "scenario-inventory": ["absent-create-readback"],
            "record-digest": None,
            "artifact-id": None,
            "artifact-digest": None,
            "scenarios": [],
        },
        {
            "probe": "probe-exact-and-conflict",
            "result": "incomplete",
            "scenario-inventory": [
                "exact",
                "identical-race",
                "differing-race",
                "lost-response",
            ],
            "record-digest": None,
            "artifact-id": None,
            "artifact-digest": None,
            "scenarios": [],
        },
    )
    assert admitted.mutation_classification == "incomplete"
    assert (admitted.workflow_run_id, admitted.run_attempt) == (run_id, 1)
