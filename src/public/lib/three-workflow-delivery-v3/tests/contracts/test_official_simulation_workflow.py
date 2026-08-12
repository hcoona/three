"""Contracts for the commit-6 Official release simulation workflow."""

from __future__ import annotations

import re
from pathlib import Path, PurePosixPath
from typing import Any, cast

import yaml

REPO_ROOT = Path(__file__).resolve().parents[6]
WORKFLOW = (
    REPO_ROOT / ".github/workflows/workflow-delivery-v3-official-simulate.yml"
)
CHECKOUT = "actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1"
UV = "astral-sh/setup-uv@c771a70e6277c0a99b617c7a806ffedaca235ff9"
MISE = "jdx/mise-action@7e36c90d9ab29c415a2384db3006f3ec8a8cc654"
UPLOAD = "actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a"
DOWNLOAD = "actions/download-artifact@3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c"
RETENTION_DAYS = 45
EVIDENCE_COUNT = 4
OPTIONAL_QUALIFICATION_DOWNLOADS = (
    "Download build Evidence by artifact ID",
    "Download project-test Evidence by artifact ID",
    "Download artifact-contents Evidence by artifact ID",
    "Download install-import Evidence by artifact ID",
    "Download Release Artifact record by artifact ID",
)
EXPECTED_NEEDS: dict[str, str | list[str] | None] = {
    "request": None,
    "discover-node": "request",
    "compile-simulation-model": "discover-node",
    "create-simulation-identity": "compile-simulation-model",
    "plan-simulation": "create-simulation-identity",
    "build-tarball": "plan-simulation",
    "project-test": "plan-simulation",
    "npm-artifact-qualification": "build-tarball",
    "qualification-finalizer": [
        "build-tarball",
        "project-test",
        "npm-artifact-qualification",
    ],
    "observe-npmjs": "qualification-finalizer",
    "materialize-hypothetical-actions": "observe-npmjs",
    "simulation-finalizer": "materialize-hypothetical-actions",
}
EXPECTED_TIMEOUTS = {
    "request": 10,
    "discover-node": 10,
    "compile-simulation-model": 10,
    "create-simulation-identity": 10,
    "plan-simulation": 10,
    "build-tarball": 15,
    "project-test": 15,
    "npm-artifact-qualification": 15,
    "qualification-finalizer": 5,
    "observe-npmjs": 10,
    "materialize-hypothetical-actions": 10,
    "simulation-finalizer": 5,
}
EXPECTED_RAW_ROLES = {
    "Upload Release Intent": "request",
    "Upload Provider Result": "node-provider",
    "Upload admitted Repository Model": "repository-model",
    "Upload Simulation Binding": "identity",
    "Upload Qualification Snapshot": "qualification-snapshot",
    "Upload Adapter context": "adapter-context",
    "Upload Release Artifact record": "release-artifact",
    "Upload build Evidence": "build-evidence",
    "Upload project-test Evidence": "project-test-evidence",
    "Upload artifact-contents Evidence": "artifact-contents-evidence",
    "Upload install-import Evidence": "install-import-evidence",
    "Upload Qualification Decision": "qualification-decision",
    "Upload observation boundary": "observation-unavailable",
    "Upload hypothetical-actions boundary": "hypothetical-actions",
    "Upload Simulation Outcome": "outcome",
    "Upload deterministic human summary": "summary",
}


def _document() -> dict[str, Any]:
    document = yaml.safe_load(WORKFLOW.read_text(encoding="utf-8"))
    assert isinstance(document, dict)
    return document


def _events(document: dict[str, Any]) -> dict[str, Any]:
    events = cast("dict[Any, Any]", document).get("on")
    if events is None:
        events = cast("dict[Any, Any]", document).get(True)
    assert isinstance(events, dict)
    return events


def _steps(job: dict[str, Any]) -> list[dict[str, Any]]:
    steps = job["steps"]
    assert isinstance(steps, list)
    return steps


def _step(job: dict[str, Any], name: str) -> dict[str, Any]:
    return next(item for item in _steps(job) if item["name"] == name)


def _run(job: dict[str, Any], name: str) -> str:
    command = _step(job, name)["run"]
    assert isinstance(command, str)
    return command


def _uses_steps(document: dict[str, Any]) -> list[dict[str, Any]]:
    return [
        step
        for job in document["jobs"].values()
        for step in _steps(job)
        if "uses" in step
    ]


def _raw_artifact_name(settings: dict[str, Any]) -> str:
    """Model upload-artifact v7 archive:false physical naming."""
    assert settings["archive"] is False
    path = settings["path"]
    assert isinstance(path, str)
    return PurePosixPath(path).name


def test_official_simulation_event_permissions_and_concurrency_are_exact() -> (
    None
):
    """Expose only the same-repository selected-ref simulation dispatch."""
    document = _document()
    raw = WORKFLOW.read_text(encoding="utf-8")

    assert _events(document) == {"workflow_dispatch": None}
    assert "inputs:" not in raw
    assert document["permissions"] == {"contents": "read"}
    assert all("permissions" not in job for job in document["jobs"].values())
    assert document["concurrency"] == {
        "group": (
            "wdv3-simulation-${{ github.run_id }}-${{ github.run_attempt }}"
        ),
        "cancel-in-progress": False,
    }
    request = _run(document["jobs"]["request"], "Normalize fixed request")
    assert '--selected-ref "${GITHUB_REF}"' in request
    assert '--target "${GITHUB_SHA}"' in request
    assert "github.ref ==" not in raw


def test_official_simulation_dag_runner_and_deadlines_are_exact() -> None:
    """Pin the approved 12-job topology and LLD deadlines."""
    jobs = _document()["jobs"]

    assert set(jobs) == set(EXPECTED_NEEDS)
    for name, expected_needs in EXPECTED_NEEDS.items():
        if expected_needs is None:
            assert "needs" not in jobs[name]
        else:
            assert jobs[name]["needs"] == expected_needs
        assert jobs[name]["runs-on"] == "ubuntu-24.04"
        assert jobs[name]["timeout-minutes"] == EXPECTED_TIMEOUTS[name]


def test_official_simulation_actions_and_checkouts_are_immutable() -> None:
    """Use commit-5 full-SHA pins and exact selected-target checkouts."""
    document = _document()
    uses_steps = _uses_steps(document)
    uses_lines = [
        line.strip()
        for line in WORKFLOW.read_text(encoding="utf-8").splitlines()
        if line.strip().startswith("uses:")
    ]
    pin = re.compile(
        r"uses:\s+[^@\s]+@[0-9a-f]{40}\s+#\s+v[0-9][0-9A-Za-z.-]*\Z"
    )

    assert uses_lines
    assert all(pin.fullmatch(line) for line in uses_lines)
    assert {step["uses"] for step in uses_steps} == {
        CHECKOUT,
        UV,
        MISE,
        UPLOAD,
        DOWNLOAD,
    }
    checkout_steps = [step for step in uses_steps if step["uses"] == CHECKOUT]
    assert len(checkout_steps) == len(EXPECTED_NEEDS)
    assert all(
        step["with"]
        == {
            "fetch-depth": 0,
            "persist-credentials": False,
            "ref": "${{ github.sha }}",
        }
        for step in checkout_steps
    )


def test_official_simulation_uses_only_raw_id_bound_artifact_transport() -> (
    None
):
    """Require exact immutable stock artifact settings in every job."""
    document = _document()
    upload_steps = [
        step for step in _uses_steps(document) if step["uses"] == UPLOAD
    ]
    download_steps = [
        step for step in _uses_steps(document) if step["uses"] == DOWNLOAD
    ]
    raw = WORKFLOW.read_text(encoding="utf-8")

    assert upload_steps
    assert len({step["with"]["name"] for step in upload_steps}) == len(
        upload_steps
    )
    for job in document["jobs"].values():
        steps_by_id = {
            step["id"]: step
            for step in _steps(job)
            if isinstance(step.get("id"), str)
        }
        for step in _steps(job):
            if step.get("uses") != UPLOAD:
                continue
            settings = step["with"]
            assert settings["retention-days"] == RETENTION_DAYS
            assert settings["overwrite"] is False
            assert settings["archive"] is False
            assert settings["include-hidden-files"] is True
            assert settings["if-no-files-found"] == "error"
            name = settings["name"]
            assert _raw_artifact_name(settings) == name
            tarball_name = (
                "${{ needs.plan-simulation.outputs.tarball-artifact-name }}"
            )
            if name == tarball_name:
                assert settings["path"] == f".wdv3/{tarball_name}"
                continue
            match = re.fullmatch(
                r"\$\{\{ steps\.([^.]+)\.outputs\.([^ }]+) \}\}",
                name,
            )
            assert match is not None
            producer = steps_by_id[match.group(1)]
            command = producer["run"]
            assert isinstance(command, str)
            assert f'echo "{match.group(2)}=' in command
            role = EXPECTED_RAW_ROLES[step["name"]]
            assert f"wdv3-release-simulation-{role}-r" in command
            assert "ra${GITHUB_RUN_ATTEMPT}" in command
            assert "digest" in command.lower()
            assert any(extension in command for extension in (".json", ".md"))
    for step in upload_steps:
        settings = step["with"]
        assert _raw_artifact_name(settings) == settings["name"]
    assert download_steps
    for step in download_steps:
        settings = step["with"]
        assert set(settings) == {
            "artifact-ids",
            "path",
            "skip-decompress",
            "digest-mismatch",
        }
        assert settings["skip-decompress"] is True
        assert settings["digest-mismatch"] == "error"
        assert "needs." in settings["artifact-ids"]
        assert "name" not in settings
        assert "github-token" not in settings
    assert "outputs.artifact-id" in raw
    assert "outputs.artifact-digest" in raw
    assert "--qualification-snapshot-artifact-id" in raw
    assert "--qualification-snapshot-artifact-digest" in raw
    assert "ACTIONS_RUNTIME_TOKEN" not in raw
    assert "ACTIONS_RESULTS_URL" not in raw
    for stale_basename in (
        "release-intent.json",
        "node-provider-result.json",
        "repository-model.json",
        "simulation-binding.json",
        "qualification-snapshot.json",
        "release-adapter-context.json",
        "release-artifact.json",
        "build-evidence.json",
        "project-test-evidence.json",
        "artifact-contents-evidence.json",
        "install-import-evidence.json",
        "qualification-decision.json",
        "observation-boundary.json",
        "actions-boundary.json",
    ):
        assert f".wdv3/input/{stale_basename}" not in raw


def test_upload_artifact_v7_raw_mode_ignores_configured_name() -> None:
    """Regress the v7 behavior that made fixed input basenames unsafe."""
    settings = {
        "name": "wdv3-release-simulation-request-r1-ra2-digest.json",
        "path": ".wdv3/release-intent.json",
        "archive": False,
    }

    assert _raw_artifact_name(settings) == "release-intent.json"
    assert _raw_artifact_name(settings) != settings["name"]


def test_build_is_uploaded_before_artifact_and_evidence_are_formed() -> None:
    """Run mechanics once, upload bytes, then bind upload metadata."""
    jobs = _document()["jobs"]
    build_steps = _steps(jobs["build-tarball"])
    names = [step["name"] for step in build_steps]
    build_run = _run(jobs["build-tarball"], "Run build mechanics once")
    form_run = _run(
        jobs["build-tarball"],
        "Form Release Artifact and build Evidence after upload",
    )
    qualification_run = _run(
        jobs["qualification-finalizer"],
        "Close qualification Decision",
    )

    assert names.index("Run build mechanics once") < names.index(
        "Upload exact raw tarball"
    )
    assert names.index("Upload exact raw tarball") < names.index(
        "Form Release Artifact and build Evidence after upload"
    )
    assert build_run.count(" release run-build ") == 1
    assert '--tarball-output ".wdv3/${TARBALL_NAME}"' in build_run
    assert "${TARBALL_NAME}.tgz" not in build_run
    assert "release run-build" not in form_run
    assert "release form-uploaded-artifact" in form_run
    assert '--tarball ".wdv3/${TARBALL_NAME}"' in form_run
    assert '--tarball-artifact-name "${TARBALL_NAME}"' in form_run
    assert "--tarball-artifact-id" in form_run
    assert "--tarball-artifact-url" in form_run
    assert "--tarball-artifact-digest" in form_run
    assert qualification_run.count('-evidence ".wdv3/input/') == EVIDENCE_COUNT
    assert (
        'add_record release-artifact ".wdv3/input/'
        "${{ needs.build-tarball.outputs.release-artifact-artifact-name }}"
    ) in qualification_run
    assert jobs["npm-artifact-qualification"]["needs"] == "build-tarball"


def test_qualification_finalizer_optional_downloads_fail_closed() -> None:
    """Distinguish genuinely empty IDs from present but failed transport."""
    jobs = _document()["jobs"]
    finalizer = jobs["qualification-finalizer"]
    finalize = _step(finalizer, "Close qualification Decision")
    finalize_run = _run(finalizer, "Close qualification Decision")

    for name in OPTIONAL_QUALIFICATION_DOWNLOADS:
        step = _step(finalizer, name)
        assert step["uses"] == DOWNLOAD
        assert step["if"].endswith(" != ''")
        assert "continue-on-error" not in step
        assert step["with"]["digest-mismatch"] == "error"

    assert finalize["if"] == "always()"
    assert 'if [[ -z "${id}" ]]; then' in finalize_run
    assert "return 0" in finalize_run
    assert 'if [[ -z "${digest}" || -z "${upload}" ]]; then' in finalize_run
    assert "has artifact ID but missing digest metadata" in finalize_run
    assert 'if [[ ! -f "${path}" ]]; then' in finalize_run
    assert "artifact ID ${id} was present but ${path} was not downloaded" in (
        finalize_run
    )


def test_commit6_observation_and_publication_stop_line_is_truthful() -> None:
    """Perform no observation network or live publication work at commit 6."""
    document = _document()
    jobs = document["jobs"]
    observe = jobs["observe-npmjs"]
    observe_run = _run(
        observe,
        "Emit explicit unavailable boundary without observation",
    )
    materialize_run = _run(
        jobs["materialize-hypothetical-actions"],
        "Materialize empty action set",
    )
    finalize_run = _run(
        jobs["simulation-finalizer"],
        "Finalize canonical Simulation Outcome and summary",
    )
    raw = WORKFLOW.read_text(encoding="utf-8")
    lowered = raw.lower()

    assert "emit-observation-unavailable" in observe_run
    assert all(
        token not in observe_run.lower()
        for token in (
            "curl ",
            "wget ",
            "npm view",
            "npm pack",
            "registry.npmjs",
            "https://",
        )
    )
    assert all(
        "release-artifact" not in str(step.get("name", "")).lower()
        for step in _steps(observe)
    )
    assert "materialize-hypothetical-actions" in materialize_run
    assert "finalize-simulation" in finalize_run
    assert "continue-on-error" in _step(
        jobs["simulation-finalizer"],
        "Finalize canonical Simulation Outcome and summary",
    )
    preserve = _run(
        jobs["simulation-finalizer"],
        "Preserve commit-6 non-success",
    )
    assert '== "success"' in preserve
    assert "exit 1" in preserve
    for forbidden in (
        "secrets.",
        "id-token:",
        "packages:",
        "npm_token",
        "npm-token",
        "authorization",
        "capability",
        "receipt",
        "publication-snapshot",
        "projection-observation",
        "release-attempt-identity",
        "official-product-identity",
        "official-execution-identity",
    ):
        assert forbidden not in lowered
