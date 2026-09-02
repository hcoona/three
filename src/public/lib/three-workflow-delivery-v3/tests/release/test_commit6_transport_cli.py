"""Scenario tests for commit-6 record transport and Release CLI boundaries."""

from __future__ import annotations

import hashlib
import json
from dataclasses import replace
from pathlib import Path

import pytest
from three_workflow_delivery_v3 import cli as cli_module
from three_workflow_delivery_v3.canonical import canonical_sha256, canonicalize
from three_workflow_delivery_v3.records.release import (
    ObservationRequestFacts,
    ObservationResponseFacts,
    ObservationValue,
    ProjectionObservation,
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
    desired_projection_state_digest,
    finalize_qualification,
    finalize_simulation,
)
from three_workflow_delivery_v3.release.simulation import (
    HypotheticalActionsReport,
    SimulationObservationSet,
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
            ProjectionObservation,
            SimulationObservationSet,
            HypotheticalActionsReport,
            SimulationOutcome,
        ),
    )
    return _write(path, canonicalize(record.to_document()))


def _transport_digest(path: Path) -> str:
    return f"sha256:{hashlib.sha256(path.read_bytes()).hexdigest()}"


def _current_arguments(scenario) -> list[str]:
    return [
        "--workflow-run-id",
        str(scenario.intent.workflow_run_id),
        "--run-attempt",
        str(scenario.binding.simulation.run_attempt),
        "--target",
        scenario.intent.target,
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
    scenario,
    *,
    producer: str | None = None,
) -> ReleaseAdmissionBindings:
    return ReleaseAdmissionBindings(
        purpose="release-simulation",
        workflow_run_id=scenario.intent.workflow_run_id,
        run_attempt=scenario.binding.simulation.run_attempt,
        target=scenario.intent.target,
        producer=producer,
    )


def _observation(
    scenario,
    classification: str,
) -> ProjectionObservation:
    projection = scenario.snapshot.destination_projections[0]
    desired = desired_projection_state_digest(
        scenario.snapshot,
        projection.projection_id,
        scenario.artifact,
    )
    if classification == "exact-satisfied":
        value = ObservationValue(
            classification=classification,
            owner="scope:@hcoona",
            coordinate=projection.coordinate,
            content_sha512=scenario.artifact.content.content_sha512,
            witness_digest=scenario.artifact.witness_digest,
            routing=(),
        )
    else:
        value = ObservationValue(
            classification=classification,
            owner=None,
            coordinate=None,
            content_sha512=None,
            witness_digest=None,
            routing=(),
        )
    request_facts = ObservationRequestFacts(
        qualification_snapshot_digest=scenario.snapshot.snapshot_digest,
        projection_digest=projection.projection_digest,
        desired_state_digest=desired,
        method="GET",
        url="https://registry.npmjs.org/test-cli-observation",
        headers=(),
    )
    request_digest = request_facts.request_digest
    response_facts = ObservationResponseFacts(
        stage="synthetic",
        requested_url=request_facts.url,
        final_url=request_facts.url,
        redirects=(),
        status=200,
        selected_headers=(),
        truncated=False,
        body_sha256=None,
    )
    response_digest = canonical_sha256(
        {
            "schema": "workflow-delivery/v3/observation-response",
            "request-digest": request_digest,
            "facts": response_facts.to_document(),
            "value": value.to_document(),
        }
    )
    return ProjectionObservation(
        subject=scenario.binding.simulation,
        purpose="release-simulation",
        target=scenario.snapshot.target,
        producer="observe-npmjs",
        qualification_snapshot_digest=scenario.snapshot.snapshot_digest,
        projection=projection,
        desired_state_digest=desired,
        observation_contract_id=projection.observation_contract_id,
        request_facts=request_facts,
        request_digest=request_digest,
        response_facts=response_facts,
        response_digest=response_digest,
        value=value,
    )


def test_every_transported_commit6_release_record_round_trips_closed_schema(
    qualified_simulation,
) -> None:
    """Deserialize every cross-job Release record under current authority."""
    scenario = qualified_simulation
    observation = _observation(scenario, "exact-satisfied")
    outcome = finalize_simulation(
        scenario.snapshot,
        scenario.decision,
        observations=(observation,),
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
            expected_bindings=_bindings(scenario, producer=producer),
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
                expected_bindings=_bindings(scenario, producer=producer),
            )

    admitted_model = admit_repository_model_snapshot(
        scenario.admitted_repository_model.canonical_bytes,
        expected_context=scenario.admitted_repository_model.snapshot.context,
        expected_digest=scenario.admitted_repository_model.canonical_digest,
    )
    assert admitted_model == scenario.admitted_repository_model


@pytest.mark.parametrize(
    "substitution",
    [
        "simulation",
        "qualification-snapshot-digest",
        "qualification-decision-digest",
    ],
)
def test_simulation_outcome_rejects_hypothetical_action_binding_substitution(
    qualified_simulation,
    substitution: str,
) -> None:
    """Reject canonical nested actions from another simulation lineage."""
    scenario = qualified_simulation
    absent = _observation(scenario, "absent")
    outcome = finalize_simulation(
        scenario.snapshot,
        scenario.decision,
        observations=(absent,),
        artifacts=(scenario.artifact,),
    )
    assert len(outcome.hypothetical_actions) == 1
    action = outcome.hypothetical_actions[0]
    if substitution == "simulation":
        substituted_action = replace(
            action,
            simulation=replace(
                action.simulation,
                workflow_run_id=action.simulation.workflow_run_id + 1,
            ),
        )
    elif substitution == "qualification-snapshot-digest":
        substituted_action = replace(
            action,
            qualification_snapshot_digest="sha256:" + ("8" * 64),
        )
    else:
        substituted_action = replace(
            action,
            qualification_decision_digest="sha256:" + ("9" * 64),
        )

    with pytest.raises(
        ValueError,
        match="Simulation Outcome action binding mismatch",
    ):
        replace(
            outcome,
            hypothetical_actions=(substituted_action,),
        )

    document = outcome.to_document()
    document["hypothetical-actions"] = [substituted_action.to_document()]
    canonical_bytes = canonicalize(document)
    with pytest.raises(
        ValueError,
        match="Simulation Outcome action binding mismatch",
    ):
        admit_release_record(
            canonical_bytes,
            expected_type=SimulationOutcome,
            expected_digest=(
                f"sha256:{hashlib.sha256(canonical_bytes).hexdigest()}"
            ),
            expected_bindings=_bindings(scenario),
        )


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
            expected_bindings=_bindings(scenario),
        )
    with pytest.raises(ValueError, match="run_attempt"):
        admit_release_record(
            canonicalize(scenario.snapshot.to_document()),
            expected_type=QualificationSnapshot,
            expected_digest=scenario.snapshot.snapshot_digest,
            expected_bindings=replace(
                _bindings(scenario),
                run_attempt=scenario.binding.simulation.run_attempt + 1,
            ),
        )
    with pytest.raises(ValueError, match="purpose"):
        admit_release_record(
            canonicalize(scenario.snapshot.to_document()),
            expected_type=QualificationSnapshot,
            expected_digest=scenario.snapshot.snapshot_digest,
            expected_bindings=replace(
                _bindings(scenario),
                purpose="live-release",
                run_attempt=None,
            ),
        )
    with pytest.raises(ValueError, match="producer"):
        admit_release_record(
            canonicalize(scenario.evidence[0].to_document()),
            expected_type=QualificationEvidence,
            expected_digest=scenario.evidence[0].evidence_digest,
            expected_bindings=_bindings(scenario, producer="other-producer"),
        )
    with pytest.raises(ValueError, match="QualificationDecision"):
        admit_release_record(
            canonicalize(scenario.evidence[0].to_document()),
            expected_type=QualificationDecision,
            expected_digest=scenario.evidence[0].evidence_digest,
            expected_bindings=_bindings(scenario),
        )
    with pytest.raises(ValueError, match="canonical digest mismatch"):
        admit_release_record(
            intent_bytes,
            expected_type=ReleaseIntent,
            expected_digest="sha256:" + ("0" * 64),
            expected_bindings=_bindings(scenario),
        )


def test_release_cli_transports_current_attempt_through_commit6_stop_line(  # noqa: PLR0915
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    qualified_simulation,
) -> None:
    """Run the canonical CLI record chain through truthful incompleteness."""
    scenario = qualified_simulation
    current = _current_arguments(scenario)
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
    context_arguments = _uploaded_arguments(
        "adapter_context",
        context_path,
        plan_outputs["adapter-context-digest"],
        105,
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
    monkeypatch.setattr(
        cli_module,
        "observe_npmjs_projection",
        lambda *_args, **_kwargs: _observation(scenario, "exact-satisfied"),
    )
    observation_path = tmp_path / "observation-set.json"
    assert (
        cli_module.main(
            [
                "release",
                "observe-npmjs",
                *current,
                *snapshot_arguments,
                *decision_arguments,
                *context_arguments,
                *artifact_arguments,
                "--output",
                str(observation_path),
            ]
        )
        == 0
    )
    observation_document = json.loads(observation_path.read_bytes())
    assert observation_document["schema"].endswith("simulation-observation-set")
    assert len(observation_document["observations"]) == 1
    assert (
        observation_document["observations"][0]["value"]["classification"]
        == "exact-satisfied"
    )
    observation_digest = hashlib.sha256(observation_path.read_bytes())
    observation_semantic_digest = f"sha256:{observation_digest.hexdigest()}"
    observation_arguments = _uploaded_arguments(
        "observation_set",
        observation_path,
        observation_semantic_digest,
        207,
    )

    actions_path = tmp_path / "actions-report.json"
    assert (
        cli_module.main(
            [
                "release",
                "materialize-hypothetical-actions",
                *current,
                *snapshot_arguments,
                *decision_arguments,
                *observation_arguments,
                *artifact_arguments,
                "--output",
                str(actions_path),
            ]
        )
        == 0
    )
    actions_document = json.loads(actions_path.read_bytes())
    assert actions_document["schema"].endswith("hypothetical-actions-report")
    assert actions_document["actions"] == []
    assert actions_document["publication-snapshot-emitted"] is False
    actions_digest = (
        f"sha256:{hashlib.sha256(actions_path.read_bytes()).hexdigest()}"
    )
    actions_arguments = _uploaded_arguments(
        "actions_report",
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
    assert first_result == 0

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
    assert second_result == 0
    assert outcome_path.read_bytes() == first_outcome
    assert summary_path.read_bytes() == first_summary
    outcome = admit_release_record(
        first_outcome,
        expected_type=SimulationOutcome,
        expected_digest=(f"sha256:{hashlib.sha256(first_outcome).hexdigest()}"),
        expected_bindings=_bindings(scenario),
    )
    assert isinstance(outcome, SimulationOutcome)
    assert outcome.terminal_result == "success"
    assert outcome.failure_class == "none"
    assert outcome.next_action == "none"
    assert b"Observation records: `1`" in first_summary


def test_release_cli_intent_is_stable_across_simulation_reruns(
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
    qualified_simulation,
) -> None:
    """Keep Release Intent stable while platform attempt inputs change."""
    scenario = qualified_simulation
    run_attempt = scenario.binding.simulation.run_attempt
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
                str(run_attempt),
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
                str(run_attempt + 1),
                "--output",
                str(rerun_path),
            ]
        )
        == 0
    )
    first = json.loads(first_path.read_bytes())
    rerun = json.loads(rerun_path.read_bytes())
    assert first["request-id"] == rerun["request-id"]
    assert "run-attempt" not in first
    assert "run-attempt" not in rerun
    assert first_path.read_bytes() == rerun_path.read_bytes()

    result = cli_module.main(
        [
            "release",
            "admit-intent",
            "--workflow-run-id",
            str(scenario.intent.workflow_run_id),
            "--run-attempt",
            str(run_attempt + 1),
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
    assert result == 0
    assert captured.err == ""


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
        assert outcome.failure_class != "unknown-observation"
