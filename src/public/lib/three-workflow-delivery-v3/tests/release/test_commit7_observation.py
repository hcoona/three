"""Commit-7 observation admission and simulation outcome mapping."""

from __future__ import annotations

# ruff: noqa: D103
import hashlib
from dataclasses import replace
from typing import TYPE_CHECKING, Protocol, cast

import pytest
from three_workflow_delivery_v3 import cli as cli_module
from three_workflow_delivery_v3.canonical import canonical_sha256, canonicalize
from three_workflow_delivery_v3.records.release import (
    HYPOTHETICAL_ACTIONS_REPORT_PRODUCER,
    NPMJS_OBSERVER_PRODUCER,
    ObservationRequestFacts,
    ObservationResponseFacts,
    ObservationValue,
    ProjectionObservation,
)
from three_workflow_delivery_v3.records.release_transport import (
    ReleaseAdmissionBindings,
    release_record_from_document,
    validate_release_admission_bindings,
)
from three_workflow_delivery_v3.release.finalizer import (
    desired_projection_state_digest,
    finalize_simulation,
    materialize_hypothetical_actions,
)
from three_workflow_delivery_v3.release.simulation import (
    HypotheticalActionsReport,
    SimulationObservationSet,
    hypothetical_actions_report_from_bytes,
    simulation_observation_set_from_bytes,
)
from three_workflow_delivery_v3.release.workflow import (
    form_release_adapter_context,
)

if TYPE_CHECKING:
    from pathlib import Path

    from three_workflow_delivery_v3.canonical import JsonValue

    from .conftest import QualifiedSimulation


class _CanonicalRecord(Protocol):
    def to_document(self) -> dict[str, JsonValue]:
        """Return canonical record document."""


def _write_record(path: Path, record: _CanonicalRecord) -> Path:
    path.write_bytes(canonicalize(record.to_document()))
    return path


def _uploaded_arguments(
    name: str,
    path: Path,
    semantic_digest: str,
    artifact_id: int,
) -> list[str]:
    option = name.replace("_", "-")
    upload_digest = f"sha256:{hashlib.sha256(path.read_bytes()).hexdigest()}"
    return [
        f"--{option}",
        str(path),
        f"--{option}-digest",
        semantic_digest,
        f"--{option}-artifact-id",
        str(artifact_id),
        f"--{option}-artifact-digest",
        upload_digest,
    ]


def _observation(
    simulation: QualifiedSimulation,
    classification: str,
) -> ProjectionObservation:
    projection = simulation.snapshot.destination_projections[0]
    artifact = simulation.artifact
    desired = desired_projection_state_digest(
        simulation.snapshot,
        projection.projection_id,
        artifact,
    )
    if classification == "exact-satisfied":
        value = ObservationValue(
            classification=classification,
            owner="scope:@hcoona",
            coordinate=projection.coordinate,
            content_sha512=artifact.content.content_sha512,
            witness_digest=artifact.witness_digest,
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
        qualification_snapshot_digest=simulation.snapshot.snapshot_digest,
        projection_digest=projection.projection_digest,
        desired_state_digest=desired,
        method="GET",
        url="https://registry.npmjs.org/test-observation",
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
        status_detail=classification,
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
        subject=simulation.binding.simulation,
        purpose="release-simulation",
        target=simulation.snapshot.target,
        producer=NPMJS_OBSERVER_PRODUCER,
        qualification_snapshot_digest=simulation.snapshot.snapshot_digest,
        projection=projection,
        desired_state_digest=desired,
        observation_contract_id=projection.observation_contract_id,
        request_facts=request_facts,
        request_digest=request_digest,
        response_facts=response_facts,
        response_digest=response_digest,
        value=value,
    )


@pytest.mark.parametrize(
    ("classification", "terminal", "failure_class", "next_action", "actions"),
    [
        ("absent", "success", "none", "none", 1),
        ("exact-satisfied", "success", "none", "none", 0),
        ("unknown", "incomplete", "unknown-observation", "rerun-simulation", 0),
        (
            "unprovable",
            "incomplete",
            "unprovable-observation",
            "fix-observation-capability-and-rerun",
            0,
        ),
        (
            "partial",
            "failure",
            "reconciliation-required",
            "reconcile-destination-state",
            0,
        ),
        (
            "conflicting",
            "failure",
            "reconciliation-required",
            "reconcile-destination-state",
            0,
        ),
    ],
)
def test_finalize_simulation_maps_commit7_observation_outcomes(  # noqa: PLR0913
    qualified_simulation: QualifiedSimulation,
    classification: str,
    terminal: str,
    failure_class: str,
    next_action: str,
    actions: int,
) -> None:
    observation = _observation(qualified_simulation, classification)

    outcome = finalize_simulation(
        qualified_simulation.snapshot,
        qualified_simulation.decision,
        observations=(observation,),
        artifacts=(qualified_simulation.artifact,),
    )

    assert outcome.terminal_result == terminal
    assert outcome.failure_class == failure_class
    assert outcome.next_action == next_action
    assert len(outcome.hypothetical_actions) == actions
    assert outcome.observation_digests == (observation.observation_digest,)


def test_materialize_hypothetical_actions_accepts_only_absent_and_exact(
    qualified_simulation: QualifiedSimulation,
) -> None:
    absent = _observation(qualified_simulation, "absent")
    exact = _observation(qualified_simulation, "exact-satisfied")
    unknown = _observation(qualified_simulation, "unknown")

    assert (
        len(
            materialize_hypothetical_actions(
                qualified_simulation.snapshot,
                qualified_simulation.decision,
                (absent,),
                (qualified_simulation.artifact,),
            )
        )
        == 1
    )
    assert (
        materialize_hypothetical_actions(
            qualified_simulation.snapshot,
            qualified_simulation.decision,
            (exact,),
            (qualified_simulation.artifact,),
        )
        == ()
    )
    with pytest.raises(ValueError, match="absent or exact-satisfied"):
        materialize_hypothetical_actions(
            qualified_simulation.snapshot,
            qualified_simulation.decision,
            (unknown,),
            (qualified_simulation.artifact,),
        )


def test_projection_observation_crosses_transport_with_current_bindings(
    qualified_simulation: QualifiedSimulation,
) -> None:
    observation = _observation(qualified_simulation, "exact-satisfied")
    document = observation.to_document()
    admitted = release_record_from_document(
        document,
        expected_type=ProjectionObservation,
    )
    assert isinstance(admitted, ProjectionObservation)
    expected = ReleaseAdmissionBindings(
        purpose="release-simulation",
        workflow_run_id=qualified_simulation.intent.workflow_run_id,
        run_attempt=qualified_simulation.intent.run_attempt,
        target=qualified_simulation.snapshot.target,
        producer=NPMJS_OBSERVER_PRODUCER,
    )

    assert admitted == observation
    assert canonicalize(document) == canonicalize(admitted.to_document())
    assert admitted.request_facts.url == (
        "https://registry.npmjs.org/test-observation"
    )
    assert admitted.request_facts.headers == ()
    expected_status = 200
    assert admitted.response_facts.status == expected_status
    assert admitted.response_facts.final_url == admitted.request_facts.url
    assert admitted.response_facts.redirects == ()
    assert admitted.response_facts.selected_headers == ()
    assert admitted.response_facts.body_sha256 is None
    validate_release_admission_bindings(admitted, expected)
    for bad in (
        replace(expected, purpose="live-release"),
        replace(expected, run_attempt=expected.run_attempt + 1),
        replace(expected, target="f" * 40),
        replace(expected, producer="other-observer"),
    ):
        with pytest.raises(ValueError, match="binding mismatch"):
            validate_release_admission_bindings(admitted, bad)


def test_projection_observation_rejects_request_fact_digest_tampering(
    qualified_simulation: QualifiedSimulation,
) -> None:
    observation = _observation(qualified_simulation, "absent")
    document = observation.to_document()
    request_facts = cast("dict[str, JsonValue]", document["request-facts"])
    request_facts["url"] = "https://registry.npmjs.org/tampered"

    with pytest.raises(ValueError, match="request digest mismatch"):
        release_record_from_document(
            document,
            expected_type=ProjectionObservation,
        )


def test_projection_observation_rejects_response_fact_digest_tampering(
    qualified_simulation: QualifiedSimulation,
) -> None:
    observation = _observation(qualified_simulation, "absent")
    document = observation.to_document()
    response_facts = cast("dict[str, JsonValue]", document["response-facts"])
    response_facts["status"] = 204

    with pytest.raises(ValueError, match="response digest mismatch"):
        release_record_from_document(
            document,
            expected_type=ProjectionObservation,
        )


def test_projection_observation_rejects_purpose_and_target_substitution(
    qualified_simulation: QualifiedSimulation,
) -> None:
    observation = _observation(qualified_simulation, "absent")
    document = observation.to_document()
    document["purpose"] = "live-release"
    with pytest.raises(ValueError, match="purpose binding mismatch"):
        release_record_from_document(
            document,
            expected_type=ProjectionObservation,
        )

    wrong_target = replace(observation, target="f" * 40)
    with pytest.raises(ValueError, match="binding mismatch"):
        finalize_simulation(
            qualified_simulation.snapshot,
            qualified_simulation.decision,
            observations=(wrong_target,),
            artifacts=(qualified_simulation.artifact,),
        )


def test_direct_projection_observation_rejects_producer_substitution(
    qualified_simulation: QualifiedSimulation,
) -> None:
    observation = replace(
        _observation(qualified_simulation, "absent"),
        producer="other-observer",
    )

    with pytest.raises(ValueError, match="producer mismatch"):
        finalize_simulation(
            qualified_simulation.snapshot,
            qualified_simulation.decision,
            observations=(observation,),
            artifacts=(qualified_simulation.artifact,),
        )


def test_successful_simulation_rejects_empty_observation_tuple(
    qualified_simulation: QualifiedSimulation,
) -> None:
    with pytest.raises(ValueError, match="exactly one Observation"):
        finalize_simulation(
            qualified_simulation.snapshot,
            qualified_simulation.decision,
            observations=(),
            artifacts=(qualified_simulation.artifact,),
        )


def test_failed_or_incomplete_qualification_needs_no_observation(
    qualified_simulation: QualifiedSimulation,
) -> None:
    failed = replace(
        qualified_simulation.decision,
        terminal_result="failure",
        failure_class="quality-failure",
        next_action="fix-quality-failure-and-rerun",
    )

    outcome = finalize_simulation(
        qualified_simulation.snapshot,
        failed,
    )

    assert outcome.terminal_result == "failure"
    assert outcome.failure_class == "quality-failure"
    assert outcome.observation_digests == ()
    assert outcome.hypothetical_actions == ()


def test_observation_set_rejects_producer_substitution(
    qualified_simulation: QualifiedSimulation,
) -> None:
    scenario = qualified_simulation
    observation = _observation(scenario, "absent")
    bundle = SimulationObservationSet(
        simulation=scenario.binding.simulation,
        purpose="release-simulation",
        target=scenario.snapshot.target,
        producer=NPMJS_OBSERVER_PRODUCER,
        workflow_run_id=scenario.intent.workflow_run_id,
        run_attempt=scenario.intent.run_attempt,
        qualification_snapshot_digest=scenario.snapshot.snapshot_digest,
        qualification_decision_digest=scenario.decision.decision_digest,
        observations=(observation,),
    )

    with pytest.raises(ValueError, match="producer must be observe-npmjs"):
        SimulationObservationSet(
            simulation=scenario.binding.simulation,
            purpose="release-simulation",
            target=scenario.snapshot.target,
            producer="other-observer",
            workflow_run_id=scenario.intent.workflow_run_id,
            run_attempt=scenario.intent.run_attempt,
            qualification_snapshot_digest=scenario.snapshot.snapshot_digest,
            qualification_decision_digest=scenario.decision.decision_digest,
            observations=(observation,),
        )
    document = bundle.to_document()
    document["producer"] = "other-observer"
    with pytest.raises(ValueError, match="producer must be observe-npmjs"):
        simulation_observation_set_from_bytes(
            canonicalize(document),
            snapshot=scenario.snapshot,
            decision=scenario.decision,
            expected_digest=canonical_sha256(document),
        )


def test_hypothetical_actions_report_rejects_producer_substitution(
    qualified_simulation: QualifiedSimulation,
) -> None:
    scenario = qualified_simulation
    observation = _observation(scenario, "absent")
    observation_set = SimulationObservationSet(
        simulation=scenario.binding.simulation,
        purpose="release-simulation",
        target=scenario.snapshot.target,
        producer=NPMJS_OBSERVER_PRODUCER,
        workflow_run_id=scenario.intent.workflow_run_id,
        run_attempt=scenario.intent.run_attempt,
        qualification_snapshot_digest=scenario.snapshot.snapshot_digest,
        qualification_decision_digest=scenario.decision.decision_digest,
        observations=(observation,),
    )

    with pytest.raises(
        ValueError,
        match="producer must be materialize-hypothetical-actions",
    ):
        HypotheticalActionsReport(
            simulation=scenario.binding.simulation,
            purpose="release-simulation",
            target=scenario.snapshot.target,
            producer="other-materializer",
            workflow_run_id=scenario.intent.workflow_run_id,
            run_attempt=scenario.intent.run_attempt,
            qualification_snapshot_digest=scenario.snapshot.snapshot_digest,
            qualification_decision_digest=scenario.decision.decision_digest,
            observation_set_digest=observation_set.set_digest,
            observation_digests=observation_set.observation_digests,
            actions=(),
            publication_snapshot_emitted=False,
        )
    report = HypotheticalActionsReport(
        simulation=scenario.binding.simulation,
        purpose="release-simulation",
        target=scenario.snapshot.target,
        producer=HYPOTHETICAL_ACTIONS_REPORT_PRODUCER,
        workflow_run_id=scenario.intent.workflow_run_id,
        run_attempt=scenario.intent.run_attempt,
        qualification_snapshot_digest=scenario.snapshot.snapshot_digest,
        qualification_decision_digest=scenario.decision.decision_digest,
        observation_set_digest=observation_set.set_digest,
        observation_digests=observation_set.observation_digests,
        actions=(),
        publication_snapshot_emitted=False,
    )
    document = report.to_document()
    document["producer"] = "other-materializer"
    with pytest.raises(
        ValueError,
        match="producer must be materialize-hypothetical-actions",
    ):
        hypothetical_actions_report_from_bytes(
            canonicalize(document),
            snapshot=scenario.snapshot,
            decision=scenario.decision,
            observations=observation_set,
            expected_digest=canonical_sha256(document),
        )


def test_cli_observe_npmjs_skips_network_for_failed_qualification(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    qualified_simulation: QualifiedSimulation,
) -> None:
    def fail_network(*_args: object, **_kwargs: object) -> None:
        message = "network observer must not run"
        raise AssertionError(message)

    scenario = qualified_simulation
    failed = replace(
        scenario.decision,
        terminal_result="failure",
        failure_class="quality-failure",
        next_action="fix-quality-failure-and-rerun",
    )
    context = form_release_adapter_context(
        scenario.snapshot,
        scenario.admitted_repository_model,
        source_date_epoch=scenario.request.source_date_epoch,
        node_version=scenario.request.node_version,
        pnpm_version=scenario.request.pnpm_version,
        npm_version=scenario.request.npm_version,
    )
    snapshot_path = _write_record(tmp_path / "snapshot.json", scenario.snapshot)
    decision_path = _write_record(tmp_path / "decision.json", failed)
    context_path = _write_record(tmp_path / "context.json", context)
    output_path = tmp_path / "observation-set.json"
    monkeypatch.setattr(cli_module, "observe_npmjs_projection", fail_network)

    result = cli_module.main(
        [
            "release",
            "observe-npmjs",
            "--workflow-run-id",
            str(scenario.intent.workflow_run_id),
            "--run-attempt",
            str(scenario.intent.run_attempt),
            "--target",
            scenario.intent.target,
            *_uploaded_arguments(
                "qualification_snapshot",
                snapshot_path,
                scenario.snapshot.snapshot_digest,
                801,
            ),
            *_uploaded_arguments(
                "qualification_decision",
                decision_path,
                failed.decision_digest,
                802,
            ),
            *_uploaded_arguments(
                "adapter_context",
                context_path,
                context.context_digest,
                803,
            ),
            "--output",
            str(output_path),
        ]
    )

    assert result == 0
    document = output_path.read_bytes()
    bundle = SimulationObservationSet(
        simulation=scenario.binding.simulation,
        purpose="release-simulation",
        target=scenario.snapshot.target,
        producer=NPMJS_OBSERVER_PRODUCER,
        workflow_run_id=scenario.intent.workflow_run_id,
        run_attempt=scenario.intent.run_attempt,
        qualification_snapshot_digest=scenario.snapshot.snapshot_digest,
        qualification_decision_digest=failed.decision_digest,
        observations=(),
    )
    assert document == canonicalize(bundle.to_document())


def test_cli_finalize_rejects_hypothetical_action_substitution(
    tmp_path: Path,
    qualified_simulation: QualifiedSimulation,
) -> None:
    scenario = qualified_simulation
    observation = _observation(scenario, "absent")
    observation_set = SimulationObservationSet(
        simulation=scenario.binding.simulation,
        purpose="release-simulation",
        target=scenario.snapshot.target,
        producer=NPMJS_OBSERVER_PRODUCER,
        workflow_run_id=scenario.intent.workflow_run_id,
        run_attempt=scenario.intent.run_attempt,
        qualification_snapshot_digest=scenario.snapshot.snapshot_digest,
        qualification_decision_digest=scenario.decision.decision_digest,
        observations=(observation,),
    )
    substituted_report = HypotheticalActionsReport(
        simulation=scenario.binding.simulation,
        purpose="release-simulation",
        target=scenario.snapshot.target,
        producer=HYPOTHETICAL_ACTIONS_REPORT_PRODUCER,
        workflow_run_id=scenario.intent.workflow_run_id,
        run_attempt=scenario.intent.run_attempt,
        qualification_snapshot_digest=scenario.snapshot.snapshot_digest,
        qualification_decision_digest=scenario.decision.decision_digest,
        observation_set_digest=observation_set.set_digest,
        observation_digests=observation_set.observation_digests,
        actions=(),
        publication_snapshot_emitted=False,
    )
    snapshot_path = _write_record(tmp_path / "snapshot.json", scenario.snapshot)
    decision_path = _write_record(tmp_path / "decision.json", scenario.decision)
    artifact_path = _write_record(tmp_path / "artifact.json", scenario.artifact)
    observation_path = _write_record(
        tmp_path / "observation-set.json",
        observation_set,
    )
    report_path = _write_record(
        tmp_path / "actions-report.json",
        substituted_report,
    )

    result = cli_module.main(
        [
            "release",
            "finalize-simulation",
            "--workflow-run-id",
            str(scenario.intent.workflow_run_id),
            "--run-attempt",
            str(scenario.intent.run_attempt),
            "--target",
            scenario.intent.target,
            *_uploaded_arguments(
                "qualification_snapshot",
                snapshot_path,
                scenario.snapshot.snapshot_digest,
                901,
            ),
            *_uploaded_arguments(
                "qualification_decision",
                decision_path,
                scenario.decision.decision_digest,
                902,
            ),
            *_uploaded_arguments(
                "observation_set",
                observation_path,
                observation_set.set_digest,
                903,
            ),
            *_uploaded_arguments(
                "actions_report",
                report_path,
                substituted_report.report_digest,
                904,
            ),
            *_uploaded_arguments(
                "release_artifact",
                artifact_path,
                scenario.artifact.artifact_digest,
                905,
            ),
            "--output",
            str(tmp_path / "outcome.json"),
            "--summary-output",
            str(tmp_path / "summary.md"),
        ]
    )

    assert result == 1
