"""Scenario tests for commit-6 record transport and Release CLI boundaries."""

from __future__ import annotations

import hashlib
import json
from dataclasses import replace
from pathlib import Path

import pytest
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
