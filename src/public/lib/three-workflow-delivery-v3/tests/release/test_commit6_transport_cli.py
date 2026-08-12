"""Scenario tests for commit-6 record transport and Release CLI boundaries."""

from __future__ import annotations

import hashlib
import json
from dataclasses import replace
from pathlib import Path

import pytest
from three_workflow_delivery_v3 import cli as cli_module
from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.records.release import (
    QualificationDecision,
    QualificationEvidence,
    QualificationSnapshot,
    ReleaseArtifact,
    ReleaseIntent,
    SimulationBinding,
    SimulationOutcome,
    admit_release_record,
    release_record_digest,
)
from three_workflow_delivery_v3.records.release_transport import (
    ReleaseAdmissionBindings,
)
from three_workflow_delivery_v3.release.finalizer import (
    finalize_qualification,
    finalize_simulation,
)
from three_workflow_delivery_v3.repository.compiler import (
    admit_repository_model_snapshot,
)

REPO_ROOT = Path(__file__).resolve().parents[6]


def _write(path: Path, content: bytes) -> Path:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(content)
    return path


def _write_record(path: Path, record: object) -> Path:
    assert isinstance(
        record,
        (
            ReleaseIntent,
            SimulationBinding,
            QualificationSnapshot,
            ReleaseArtifact,
            QualificationEvidence,
            QualificationDecision,
            SimulationOutcome,
        ),
    )
    return _write(path, canonicalize(record.to_document()))


def _transport_digest(path: Path) -> str:
    return f"sha256:{hashlib.sha256(path.read_bytes()).hexdigest()}"


def _current_arguments(record: ReleaseIntent) -> list[str]:
    return [
        "--workflow-run-id",
        str(record.workflow_run_id),
        "--run-attempt",
        str(record.run_attempt),
        "--target",
        record.target,
    ]


def _uploaded_arguments(
    name: str,
    path: Path,
    semantic_digest: str,
    artifact_id: int,
) -> list[str]:
    option = name.replace("_", "-")
    return [
        f"--{option}",
        str(path),
        f"--{option}-digest",
        semantic_digest,
        f"--{option}-artifact-id",
        str(artifact_id),
        f"--{option}-artifact-digest",
        _transport_digest(path),
    ]


def _bindings(
    intent: ReleaseIntent,
    *,
    producer: str | None = None,
) -> ReleaseAdmissionBindings:
    return ReleaseAdmissionBindings(
        purpose="release-simulation",
        workflow_run_id=intent.workflow_run_id,
        run_attempt=intent.run_attempt,
        target=intent.target,
        producer=producer,
    )


def test_every_transported_commit6_release_record_round_trips_closed_schema(
    qualified_simulation,
) -> None:
    """Deserialize every cross-job Release record under current authority."""
    scenario = qualified_simulation
    outcome = finalize_simulation(
        scenario.snapshot,
        scenario.decision,
        artifacts=(scenario.artifact,),
    )
    records = (
        (scenario.intent, None),
        (scenario.binding, None),
        (scenario.snapshot, None),
        (scenario.artifact, "build-tarball"),
        (scenario.evidence[0], "build-tarball"),
        (scenario.evidence[1], "project-test"),
        (scenario.evidence[2], "npm-artifact-qualification"),
        (scenario.evidence[3], "npm-artifact-qualification"),
        (scenario.decision, None),
        (outcome, None),
    )

    for record, producer in records:
        canonical_bytes = canonicalize(record.to_document())
        admitted = admit_release_record(
            canonical_bytes,
            expected_type=type(record),
            expected_digest=release_record_digest(record),
            expected_bindings=_bindings(
                scenario.intent,
                producer=producer,
            ),
        )
        assert admitted == record
        assert type(admitted) is type(record)

        unknown = record.to_document()
        unknown["payload-selected-authority"] = "forbidden"
        with pytest.raises(ValueError, match="unknown field"):
            admit_release_record(
                canonicalize(unknown),
                expected_type=type(record),
                expected_digest=release_record_digest(record),
                expected_bindings=_bindings(
                    scenario.intent,
                    producer=producer,
                ),
            )

    admitted_model = admit_repository_model_snapshot(
        scenario.admitted_repository_model.canonical_bytes,
        expected_context=scenario.admitted_repository_model.snapshot.context,
        expected_digest=scenario.admitted_repository_model.canonical_digest,
    )
    assert admitted_model == scenario.admitted_repository_model


def test_release_transport_rejects_canonical_binding_and_substitution_attacks(
    qualified_simulation,
) -> None:
    """Reject noncanonical, stale, cross-purpose, and wrong-type input."""
    scenario = qualified_simulation
    intent_bytes = canonicalize(scenario.intent.to_document())

    with pytest.raises(ValueError, match="not canonical"):
        admit_release_record(
            json.dumps(
                scenario.intent.to_document(),
                indent=2,
            ).encode(),
            expected_type=ReleaseIntent,
            expected_digest=scenario.intent.intent_digest,
            expected_bindings=_bindings(scenario.intent),
        )
    with pytest.raises(ValueError, match="run_attempt"):
        admit_release_record(
            intent_bytes,
            expected_type=ReleaseIntent,
            expected_digest=scenario.intent.intent_digest,
            expected_bindings=replace(
                _bindings(scenario.intent),
                run_attempt=scenario.intent.run_attempt + 1,
            ),
        )
    with pytest.raises(ValueError, match="purpose"):
        admit_release_record(
            canonicalize(scenario.snapshot.to_document()),
            expected_type=QualificationSnapshot,
            expected_digest=scenario.snapshot.snapshot_digest,
            expected_bindings=replace(
                _bindings(scenario.intent),
                purpose="live-release",
            ),
        )
    with pytest.raises(ValueError, match="producer"):
        admit_release_record(
            canonicalize(scenario.evidence[0].to_document()),
            expected_type=QualificationEvidence,
            expected_digest=scenario.evidence[0].evidence_digest,
            expected_bindings=_bindings(
                scenario.intent,
                producer="other-producer",
            ),
        )
    with pytest.raises(ValueError, match="QualificationDecision"):
        admit_release_record(
            canonicalize(scenario.evidence[0].to_document()),
            expected_type=QualificationDecision,
            expected_digest=scenario.evidence[0].evidence_digest,
            expected_bindings=_bindings(scenario.intent),
        )
    with pytest.raises(ValueError, match="canonical digest mismatch"):
        admit_release_record(
            intent_bytes,
            expected_type=ReleaseIntent,
            expected_digest="sha256:" + ("0" * 64),
            expected_bindings=_bindings(scenario.intent),
        )


def test_release_cli_transports_current_attempt_through_commit6_stop_line(  # noqa: PLR0915
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    qualified_simulation,
) -> None:
    """Run the canonical CLI record chain through truthful incompleteness."""
    scenario = qualified_simulation
    current = _current_arguments(scenario.intent)
    intent_path = tmp_path / "intent.json"
    model_path = _write(
        tmp_path / "repository-model.json",
        scenario.admitted_repository_model.canonical_bytes,
    )
    binding_path = tmp_path / "simulation-binding.json"
    snapshot_path = tmp_path / "qualification-snapshot.json"
    context_path = tmp_path / "adapter-context.json"
    github_output = tmp_path / "github-output.txt"
    plan_output = tmp_path / "plan-output.txt"

    assert (
        cli_module.main(
            [
                "release",
                "normalize-simulation-request",
                "--repository",
                scenario.intent.repository,
                "--selected-ref",
                scenario.intent.selected_ref,
                "--actor",
                scenario.intent.actor,
                *current,
                "--output",
                str(intent_path),
                "--github-output",
                str(github_output),
            ]
        )
        == 0
    )
    assert intent_path.read_bytes() == canonicalize(
        scenario.intent.to_document()
    )
    intent_arguments = _uploaded_arguments(
        "intent",
        intent_path,
        scenario.intent.intent_digest,
        101,
    )
    assert (
        cli_module.main(
            [
                "release",
                "admit-intent",
                *current,
                *intent_arguments,
            ]
        )
        == 0
    )

    model_arguments = _uploaded_arguments(
        "repository_model",
        model_path,
        scenario.admitted_repository_model.canonical_digest,
        102,
    )
    assert (
        cli_module.main(
            [
                "release",
                "create-simulation-identity",
                *current,
                *intent_arguments,
                *model_arguments,
                "--output",
                str(binding_path),
            ]
        )
        == 0
    )
    assert binding_path.read_bytes() == canonicalize(
        scenario.binding.to_document()
    )
    binding_arguments = _uploaded_arguments(
        "simulation_binding",
        binding_path,
        scenario.binding.binding_digest,
        103,
    )

    tool_outputs = {
        "git": str(scenario.request.source_date_epoch),
        "node": scenario.request.node_version,
        "pnpm": scenario.request.pnpm_version,
        "npm": scenario.request.npm_version,
    }
    monkeypatch.setattr(
        cli_module,
        "_command_stdout",
        lambda command, _cwd: tool_outputs[command[0]],
    )
    assert (
        cli_module.main(
            [
                "release",
                "plan-qualification",
                "--repo-root",
                str(REPO_ROOT),
                *current,
                *intent_arguments,
                *model_arguments,
                *binding_arguments,
                "--output",
                str(snapshot_path),
                "--adapter-context-output",
                str(context_path),
                "--github-output",
                str(plan_output),
            ]
        )
        == 0
    )
    assert snapshot_path.read_bytes() == canonicalize(
        scenario.snapshot.to_document()
    )
    plan_outputs = dict(
        line.split("=", 1)
        for line in plan_output.read_text(encoding="utf-8").splitlines()
    )
    assert plan_outputs["tarball-artifact-name"].endswith(".tgz")

    snapshot_arguments = _uploaded_arguments(
        "qualification_snapshot",
        snapshot_path,
        scenario.snapshot.snapshot_digest,
        104,
    )
    evidence_specs = (
        ("build_evidence", scenario.evidence[0], 201),
        ("project_test_evidence", scenario.evidence[1], 202),
        ("artifact_contents_evidence", scenario.evidence[2], 203),
        ("install_import_evidence", scenario.evidence[3], 204),
    )
    evidence_arguments: list[str] = []
    for name, evidence, artifact_id in evidence_specs:
        path = _write_record(tmp_path / f"{name}.json", evidence)
        evidence_arguments.extend(
            _uploaded_arguments(
                name,
                path,
                evidence.evidence_digest,
                artifact_id,
            )
        )
    artifact_path = _write_record(
        tmp_path / "release-artifact.json",
        scenario.artifact,
    )
    artifact_arguments = _uploaded_arguments(
        "release_artifact",
        artifact_path,
        scenario.artifact.artifact_digest,
        205,
    )
    decision_path = tmp_path / "qualification-decision.json"
    assert (
        cli_module.main(
            [
                "release",
                "finalize-qualification",
                *current,
                *snapshot_arguments,
                *evidence_arguments,
                *artifact_arguments,
                "--output",
                str(decision_path),
            ]
        )
        == 0
    )
    assert decision_path.read_bytes() == canonicalize(
        scenario.decision.to_document()
    )

    decision_arguments = _uploaded_arguments(
        "qualification_decision",
        decision_path,
        scenario.decision.decision_digest,
        206,
    )
    observation_path = tmp_path / "observation-boundary.json"
    assert (
        cli_module.main(
            [
                "release",
                "emit-observation-unavailable",
                *current,
                *snapshot_arguments,
                *decision_arguments,
                "--output",
                str(observation_path),
            ]
        )
        == 0
    )
    observation_document = json.loads(observation_path.read_bytes())
    assert observation_document["status"] == "unavailable"
    assert observation_document["authoritative"] is False
    assert observation_document["network-performed"] is False
    observation_digest = hashlib.sha256(observation_path.read_bytes())
    observation_semantic_digest = f"sha256:{observation_digest.hexdigest()}"
    observation_arguments = _uploaded_arguments(
        "observation_boundary",
        observation_path,
        observation_semantic_digest,
        207,
    )

    actions_path = tmp_path / "actions-boundary.json"
    assert (
        cli_module.main(
            [
                "release",
                "materialize-hypothetical-actions",
                *current,
                *snapshot_arguments,
                *decision_arguments,
                *observation_arguments,
                "--output",
                str(actions_path),
            ]
        )
        == 0
    )
    actions_document = json.loads(actions_path.read_bytes())
    assert actions_document["actions"] == []
    assert actions_document["publication-snapshot-emitted"] is False
    actions_digest = (
        f"sha256:{hashlib.sha256(actions_path.read_bytes()).hexdigest()}"
    )
    actions_arguments = _uploaded_arguments(
        "actions_boundary",
        actions_path,
        actions_digest,
        208,
    )

    outcome_path = tmp_path / "simulation-outcome.json"
    summary_path = tmp_path / "simulation-summary.md"
    first_result = cli_module.main(
        [
            "release",
            "finalize-simulation",
            *current,
            *snapshot_arguments,
            *decision_arguments,
            *observation_arguments,
            *actions_arguments,
            *artifact_arguments,
            "--output",
            str(outcome_path),
            "--summary-output",
            str(summary_path),
        ]
    )
    first_outcome = outcome_path.read_bytes()
    first_summary = summary_path.read_bytes()
    assert first_result == 1

    second_result = cli_module.main(
        [
            "release",
            "finalize-simulation",
            *current,
            *snapshot_arguments,
            *decision_arguments,
            *observation_arguments,
            *actions_arguments,
            *artifact_arguments,
            "--output",
            str(outcome_path),
            "--summary-output",
            str(summary_path),
        ]
    )
    assert second_result == 1
    assert outcome_path.read_bytes() == first_outcome
    assert summary_path.read_bytes() == first_summary
    outcome = admit_release_record(
        first_outcome,
        expected_type=SimulationOutcome,
        expected_digest=(f"sha256:{hashlib.sha256(first_outcome).hexdigest()}"),
        expected_bindings=_bindings(scenario.intent),
    )
    assert isinstance(outcome, SimulationOutcome)
    assert outcome.terminal_result == "incomplete"
    assert outcome.failure_class == "unsupported-observation"
    assert outcome.next_action == "implement-observation-adapter"
    assert b"unsupported-observation" in first_summary


def test_release_cli_request_id_is_rerun_stable_but_transport_is_attempt_bound(
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
    qualified_simulation,
) -> None:
    """Keep request identity stable while rejecting a prior-attempt payload."""
    scenario = qualified_simulation
    first_path = tmp_path / "attempt-3.json"
    rerun_path = tmp_path / "attempt-4.json"
    base = [
        "release",
        "normalize-simulation-request",
        "--repository",
        scenario.intent.repository,
        "--selected-ref",
        scenario.intent.selected_ref,
        "--actor",
        scenario.intent.actor,
        "--workflow-run-id",
        str(scenario.intent.workflow_run_id),
        "--target",
        scenario.intent.target,
    ]
    assert (
        cli_module.main(
            [
                *base,
                "--run-attempt",
                str(scenario.intent.run_attempt),
                "--output",
                str(first_path),
            ]
        )
        == 0
    )
    assert (
        cli_module.main(
            [
                *base,
                "--run-attempt",
                str(scenario.intent.run_attempt + 1),
                "--output",
                str(rerun_path),
            ]
        )
        == 0
    )
    first = json.loads(first_path.read_bytes())
    rerun = json.loads(rerun_path.read_bytes())
    assert first["request-id"] == rerun["request-id"]
    assert first["run-attempt"] + 1 == rerun["run-attempt"]
    assert first_path.read_bytes() != rerun_path.read_bytes()

    result = cli_module.main(
        [
            "release",
            "admit-intent",
            "--workflow-run-id",
            str(scenario.intent.workflow_run_id),
            "--run-attempt",
            str(scenario.intent.run_attempt + 1),
            "--target",
            scenario.intent.target,
            *_uploaded_arguments(
                "intent",
                first_path,
                scenario.intent.intent_digest,
                301,
            ),
        ]
    )
    captured = capsys.readouterr()
    assert result == 1
    assert "run_attempt" in captured.err


def test_simulation_finalizer_preserves_non_successful_qualification(
    qualified_simulation,
) -> None:
    """Do not replace qualification failure or incompleteness with stop-line."""
    scenario = qualified_simulation
    failed_build = replace(
        scenario.evidence[0],
        raw_result="failure",
        normalized_outcome="failed",
        artifact_digests=(),
        result_facts=(),
        diagnostics=("build failed",),
    )
    failed = finalize_qualification(
        scenario.snapshot,
        (failed_build, scenario.evidence[1]),
        (),
    )
    incomplete = finalize_qualification(
        scenario.snapshot,
        scenario.evidence[:-1],
        (scenario.artifact,),
    )

    for decision in (failed, incomplete):
        outcome = finalize_simulation(
            scenario.snapshot,
            decision,
        )
        assert outcome.terminal_result == decision.terminal_result
        assert outcome.failure_class == decision.failure_class
        assert outcome.next_action == decision.next_action
        assert outcome.failure_class != "unsupported-observation"
